"""Router: the five categories, and the no-model guarantee (spec section 6)."""

from __future__ import annotations

import pytest

from ai_orchestrator.orchestrator import Orchestrator
from ai_orchestrator.router.task_router import Classification, TaskRouter


@pytest.mark.parametrize(
    "request_text, expected",
    [
        ("run the tests for the inventory module", Classification.DETERMINISTIC),
        ("show me the diff", Classification.DETERMINISTIC),
        ("count the files under Assets/Resources", Classification.DETERMINISTIC),
        ("compare the hashes of the two catalogs", Classification.DETERMINISTIC),
        ("add a docstring to the loader", Classification.WORKER),
        ("generate unit tests for the parser", Classification.WORKER),
        ("localize the store copy into Spanish", Classification.WORKER),
        ("decide whether the economy should use a shared bank", Classification.REASONING),
        ("do an RCA on the raid reward failure", Classification.REASONING),
        ("what is the right architecture for offline saves", Classification.REASONING),
        ("implement multi-currency raid rewards", Classification.REASONING_THEN_WORKER),
        ("fix the bug where pets never despawn", Classification.REASONING_THEN_WORKER),
        ("the worker failed twice on this, escalate it", Classification.ESCALATION),
    ],
)
def test_five_categories(request_text, expected):
    assert TaskRouter().classify(request_text).classification is expected


def test_all_five_categories_are_reachable():
    router = TaskRouter()
    seen = {
        router.classify(text).classification
        for text in (
            "git status",
            "add a docstring",
            "rca on the crash",
            "implement the feature",
            "escalate this",
        )
    }
    assert seen == set(Classification)


def test_deterministic_flag_is_the_only_no_model_class():
    assert Classification.DETERMINISTIC.uses_model is False
    for other in Classification:
        if other is not Classification.DETERMINISTIC:
            assert other.uses_model is True


def test_judgement_words_defeat_a_deterministic_keyword():
    """'run the tests' is code; 'why do the tests fail' is not."""
    decision = TaskRouter().classify("why do the tests fail after the merge")
    assert decision.classification is Classification.REASONING


def test_unknown_request_defaults_to_reasoning_not_worker():
    decision = TaskRouter().classify("zorbify the frobnicator")
    assert decision.classification is Classification.REASONING


def test_deterministic_run_never_invokes_a_provider(tmp_path, config, exploding_factory):
    """The load-bearing test: a full run() with a provider that raises on use."""
    orchestrator = Orchestrator(
        repo_root=tmp_path,
        config=config,
        state_dir=tmp_path / "state",
        provider_factory=exploding_factory,
    )
    outcome = orchestrator.run("count the files in this directory")

    assert outcome.classification == "DETERMINISTIC"
    assert outcome.status == "COMPLETE"
    assert outcome.deterministic_output["operation"] == "count_files"
    # Zero rows in the model-call ledger is the proof of zero spend.
    assert orchestrator.db.model_calls(outcome.task_id) == []
    assert orchestrator.usage_summary(outcome.task_id)["estimated_cost_usd"] == 0.0
