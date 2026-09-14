"""Retry budget = 1 + 2, then an RCA package - never a third retry (section 12)."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path

import pytest

from ai_orchestrator.orchestrator import Orchestrator
from ai_orchestrator.router.escalation import assess_escalation


def _git(args: list[str], cwd: Path) -> None:
    result = subprocess.run(
        ["git", *args], cwd=str(cwd), capture_output=True, text=True
    )
    assert result.returncode == 0, result.stderr


@pytest.fixture
def sandbox_repo(tmp_path) -> Path:
    """A tiny real git repo, so worktree create/remove is exercised for real."""
    repo = tmp_path / "sandbox"
    (repo / "docs").mkdir(parents=True)
    (repo / "docs" / "notes.md").write_text("# notes\n", encoding="utf-8")
    _git(["init", "-q", "-b", "main"], repo)
    _git(["config", "user.email", "lane@example.invalid"], repo)
    _git(["config", "user.name", "lane"], repo)
    _git(["add", "-A"], repo)
    _git(["commit", "-qm", "seed"], repo)
    return repo


ORDER = {
    "work_order_id": "WO-retry",
    "title": "retry cap",
    "objective": "prove the autonomous retry budget",
    "task_type": "doc_change",
    "allowed_paths": ["docs"],
}

# A worker that returns no file changes fails the "produced_changes" check every
# time - a deterministic, offline failure loop.
EMPTY_PATCH = {"files": [], "summary": "I could not work out what to change", "needs_escalation": False}


def test_worker_gets_exactly_three_attempts_then_rca(
    sandbox_repo, config, scripted_factory, tmp_path
):
    factory = scripted_factory([EMPTY_PATCH])
    orchestrator = Orchestrator(
        repo_root=sandbox_repo,
        config=config,
        state_dir=tmp_path / "state",
        provider_factory=factory,
    )
    outcome = orchestrator.run("apply the order", work_order=json.dumps(ORDER))

    assert config.max_autonomous_retries == 2
    assert outcome.result.attempts == 3, "budget is 1 initial attempt + 2 retries"
    provider = factory.created["worker"]
    assert len(provider.calls) == 3, "the model was called once per attempt, no more"

    assert outcome.status == "FAILED_RCA"
    assert outcome.result.needs_escalation is True
    assert outcome.rca is not None

    package = json.loads(Path(outcome.rca.path).read_text(encoding="utf-8"))
    # Spec section 12: the reasoner receives all six of these.
    assert package["original_request"] == "apply the order"
    assert package["work_order"]["work_order_id"] == "WO-retry"
    assert "diff" in package
    assert package["attempts"] == 3
    assert package["worker_explanation"]
    assert package["logs"]
    assert "retry_budget_exhausted" in package["triggers"]

    row = orchestrator.db.get_task(outcome.task_id)
    assert row["state"] == "RCA"
    assert row["retry_count"] == 2

    calls = orchestrator.db.model_calls(outcome.task_id)
    assert len(calls) == 3
    assert all(c["input_tokens"] == 11 and c["output_tokens"] == 7 for c in calls)


def test_worktree_is_cleaned_up_after_a_failed_run(
    sandbox_repo, config, scripted_factory, tmp_path
):
    factory = scripted_factory([EMPTY_PATCH])
    orchestrator = Orchestrator(
        repo_root=sandbox_repo,
        config=config,
        state_dir=tmp_path / "state",
        provider_factory=factory,
    )
    orchestrator.run("apply the order", work_order=json.dumps(ORDER))

    listed = subprocess.run(
        ["git", "worktree", "list"], cwd=str(sandbox_repo), capture_output=True, text=True
    ).stdout
    assert "ai-worktrees" not in listed
    assert not (sandbox_repo / "ai-worktrees").exists()


def test_a_successful_first_attempt_does_not_retry(
    sandbox_repo, config, scripted_factory, tmp_path
):
    good = {
        "files": [{"path": "docs/notes.md", "content": "# notes\n\nUpdated.\n"}],
        "summary": "updated the note",
        "needs_escalation": False,
    }
    factory = scripted_factory([good])
    orchestrator = Orchestrator(
        repo_root=sandbox_repo,
        config=config,
        state_dir=tmp_path / "state",
        provider_factory=factory,
    )
    outcome = orchestrator.run("apply the order", work_order=json.dumps(ORDER))

    assert outcome.status == "COMPLETE"
    assert outcome.result.attempts == 1
    assert len(factory.created["worker"].calls) == 1
    assert outcome.result.files_changed == ["docs/notes.md"]
    assert "Updated." in outcome.result.diff


def test_escalation_assessment_names_the_spec_triggers():
    decision = assess_escalation(attempts=3, max_retries=2, tests_failed=["pytest"])
    assert decision.escalate is True
    assert "tests_failed_twice" in decision.triggers
    assert "retry_budget_exhausted" in decision.triggers

    clean = assess_escalation(attempts=1, max_retries=2)
    assert clean.escalate is False
