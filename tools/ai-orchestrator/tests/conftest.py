"""Shared fixtures. No test in this suite may reach a real model."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

TOOL_ROOT = Path(__file__).resolve().parents[1]
if str(TOOL_ROOT) not in sys.path:
    sys.path.insert(0, str(TOOL_ROOT))

from ai_orchestrator.config import load_config  # noqa: E402
from ai_orchestrator.providers.base import LLMProvider, ProviderResponse  # noqa: E402


class ExplodingProvider(LLMProvider):
    """Any use is a test failure.

    This is how "DETERMINISTIC never invokes a provider" is proven: not by
    reading the classifier's return value, but by running the whole orchestrator
    with a provider that raises the moment anything touches it.
    """

    name = "exploding"

    def generate(self, prompt, *, system=None, **options):
        raise AssertionError("provider invoked on a DETERMINISTIC path")

    def generate_structured(self, prompt, schema, *, system=None, **options):
        raise AssertionError("provider invoked on a DETERMINISTIC path")

    def call_tools(self, messages, tools, **options):
        raise AssertionError("provider invoked on a DETERMINISTIC path")


class ScriptedProvider(LLMProvider):
    """Returns canned structured payloads and counts its calls."""

    name = "scripted"

    def __init__(self, settings, role, payloads=None):
        super().__init__(settings, role)
        self.payloads = list(payloads or [])
        self.calls: list[str] = []

    def _next(self, prompt: str) -> ProviderResponse:
        self.calls.append(prompt[:80])
        payload = (
            self.payloads.pop(0)
            if len(self.payloads) > 1
            else (self.payloads[0] if self.payloads else {})
        )
        return ProviderResponse(
            text="", data=dict(payload), input_tokens=11, output_tokens=7
        )

    def generate(self, prompt, *, system=None, **options):
        return self._next(prompt)

    def generate_structured(self, prompt, schema, *, system=None, **options):
        return self._next(prompt)

    def call_tools(self, messages, tools, **options):
        return self._next(str(messages))


@pytest.fixture
def config():
    return load_config(TOOL_ROOT / "models.yaml")


@pytest.fixture
def exploding_factory(config):
    def factory(cfg, role):
        return ExplodingProvider(cfg.provider(cfg.role(role).provider), cfg.role(role))

    return factory


@pytest.fixture
def scripted_factory():
    """Build a provider factory that replays ``payloads`` and records calls."""
    created: dict[str, ScriptedProvider] = {}

    def make(payloads):
        def factory(cfg, role):
            provider = ScriptedProvider(
                cfg.provider(cfg.role(role).provider), cfg.role(role), payloads
            )
            created[role] = provider
            return provider

        factory.created = created  # type: ignore[attr-defined]
        return factory

    return make
