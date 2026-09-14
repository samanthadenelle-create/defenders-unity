"""Model names live only in config; the OpenAI provider fails loudly with no key."""

from __future__ import annotations

import ast
import re
from pathlib import Path

import pytest

from ai_orchestrator.config import load_config
from ai_orchestrator.orchestrator import Orchestrator
from ai_orchestrator.providers.base import ProviderConfigError, build_provider

TOOL_ROOT = Path(__file__).resolve().parents[1]
PACKAGE = TOOL_ROOT / "ai_orchestrator"


def test_roles_resolve_through_config(config):
    assert config.role("worker").provider == "ollama"
    assert config.role("reasoner").provider == "openai"
    assert config.role("worker_fallback").provider == "openai"
    assert config.max_autonomous_retries == 2


def _docstring_lines(tree: ast.AST) -> set[int]:
    """Line numbers occupied by docstrings - prose, not code."""
    lines: set[int] = set()
    for node in ast.walk(tree):
        if not isinstance(node, (ast.Module, ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
            continue
        body = getattr(node, "body", [])
        if (
            body
            and isinstance(body[0], ast.Expr)
            and isinstance(body[0].value, ast.Constant)
            and isinstance(body[0].value.value, str)
        ):
            first = body[0]
            lines.update(range(first.lineno, (first.end_lineno or first.lineno) + 1))
    return lines


def test_no_model_string_is_hard_coded_in_code():
    """Spec section 5: model names must never be hard-coded.

    Docstrings and comments are exempt: naming the model that a behaviour was
    PROVEN against is evidence, and stripping it would make the file lie. What is
    banned is a model name the program can actually read - a literal, a default
    argument, a dict value.
    """
    config = load_config(TOOL_ROOT / "models.yaml")
    names = {role.model for role in config.roles.values()}
    offenders: list[str] = []
    for path in PACKAGE.rglob("*.py"):
        text = path.read_text(encoding="utf-8")
        exempt = _docstring_lines(ast.parse(text))
        for lineno, line in enumerate(text.splitlines(), 1):
            if lineno in exempt:
                continue
            code = line.split("#", 1)[0]
            if any(name in code for name in names):
                offenders.append(f"{path.relative_to(TOOL_ROOT)}:{lineno}: {line.strip()}")
    assert offenders == [], "model names found in code: " + "; ".join(offenders)


def test_provider_interfaces_are_interchangeable(config):
    for role in ("worker", "reasoner", "worker_fallback"):
        provider = build_provider(config, role)
        for method in ("generate", "generate_structured", "call_tools", "estimate_cost"):
            assert callable(getattr(provider, method))


def test_openai_provider_fails_loudly_without_a_key(config, monkeypatch):
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)
    provider = build_provider(config, "reasoner")
    with pytest.raises(ProviderConfigError) as excinfo:
        provider.generate("anything")
    message = str(excinfo.value)
    assert "OPENAI_API_KEY" in message
    assert "not set" in message


def test_reasoning_request_is_blocked_not_silently_downgraded(tmp_path, config, monkeypatch):
    """A missing key must STOP the run - never fall back to the local worker."""
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)
    orchestrator = Orchestrator(
        repo_root=tmp_path, config=config, state_dir=tmp_path / "state"
    )
    outcome = orchestrator.run("decide the right architecture for offline saves")

    assert outcome.classification == "REASONING"
    assert outcome.status == "BLOCKED"
    assert "OPENAI_API_KEY" in outcome.blocked_reason
    assert orchestrator.db.model_calls(outcome.task_id) == []


def test_reasoning_path_never_fires_a_probe_call_even_with_a_key(
    tmp_path, config, monkeypatch, exploding_factory
):
    """With a key present, the REASONING stop must still cost nothing.

    An earlier draft probed the provider with a live 'ping' to work out why it
    could not run. That would have been a real, paid, unlogged call whose answer
    was discarded - spec section 13's "never used simply because it is available".
    ExplodingProvider raises on any use, so this test fails if the probe returns.
    """
    monkeypatch.setenv("OPENAI_API_KEY", "sk-test-not-a-real-key")
    orchestrator = Orchestrator(
        repo_root=tmp_path,
        config=config,
        state_dir=tmp_path / "state",
        provider_factory=exploding_factory,
    )
    outcome = orchestrator.run("what is the right architecture for offline saves")

    assert outcome.status == "BLOCKED"
    assert "handoff" in outcome.blocked_reason
    assert orchestrator.db.model_calls(outcome.task_id) == []


def test_cost_estimation_uses_config_pricing(config):
    reasoner = build_provider(config, "reasoner")
    worker = build_provider(config, "worker")
    assert reasoner.estimate_cost(1_000_000, 0) == pytest.approx(1.25)
    assert reasoner.estimate_cost(0, 1_000_000) == pytest.approx(10.0)
    assert worker.estimate_cost(1_000_000, 1_000_000) == 0.0


def test_no_secret_literal_in_the_tree():
    """No API key may ever be committed; the env var name is all we carry."""
    pattern = re.compile(r"sk-[A-Za-z0-9]{20,}")
    for path in list(PACKAGE.rglob("*.py")) + [TOOL_ROOT / "models.yaml"]:
        assert not pattern.search(path.read_text(encoding="utf-8")), path
