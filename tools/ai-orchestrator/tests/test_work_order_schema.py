"""Invalid work orders are refused BEFORE the worker (spec section 7)."""

from __future__ import annotations

import json

import pytest

from ai_orchestrator.orchestrator import Orchestrator
from ai_orchestrator.schemas.work_order import (
    WorkOrder,
    WorkOrderRejected,
    parse_work_order,
)

VALID = {
    "work_order_id": "WO-000123",
    "title": "Fix Raid Reward Serialization",
    "objective": "Persist all raid reward currencies.",
    "task_type": "code_change",
    "risk": "medium",
    "allowed_paths": ["Assets/Scripts/Raid"],
    "prohibited_paths": ["Assets/Plugins"],
    "requirements": [],
    "implementation_steps": [],
    "acceptance_criteria": [],
    "tests_required": [],
    "commands_allowed": [],
    "requires_reasoner_review": False,
}


def test_the_spec_example_validates():
    order = parse_work_order(VALID)
    assert isinstance(order, WorkOrder)
    assert order.work_order_id == "WO-000123"


@pytest.mark.parametrize(
    "mutation, why",
    [
        ({"work_order_id": "nope"}, "id shape"),
        ({"title": ""}, "empty title"),
        ({"objective": ""}, "empty objective"),
        ({"task_type": "teleport"}, "unknown task type"),
        ({"risk": "catastrophic"}, "unknown risk"),
        ({"allowed_paths": []}, "no allowed paths"),
        ({"allowed_paths": ["/etc/passwd"]}, "absolute posix path"),
        ({"allowed_paths": ["C:/Windows"]}, "absolute windows path"),
        ({"allowed_paths": ["../../secrets"]}, "parent escape"),
        ({"extra_field": True}, "unknown field"),
    ],
)
def test_invalid_work_orders_are_rejected(mutation, why):
    payload = {**VALID, **mutation}
    with pytest.raises(WorkOrderRejected):
        parse_work_order(payload)


def test_missing_required_field_is_rejected():
    payload = {k: v for k, v in VALID.items() if k != "objective"}
    with pytest.raises(WorkOrderRejected):
        parse_work_order(payload)


def test_non_json_payload_is_rejected():
    with pytest.raises(WorkOrderRejected):
        parse_work_order("{not json")
    with pytest.raises(WorkOrderRejected):
        parse_work_order("[1, 2, 3]")


def test_permits_honours_allowed_and_prohibited():
    order = parse_work_order(VALID)
    assert order.permits("Assets/Scripts/Raid/RaidReward.cs")
    assert not order.permits("Assets/Scripts/Town/Town.cs")
    assert not order.permits("Assets/Plugins/anything.cs")


def test_invalid_work_order_never_reaches_the_worker(tmp_path, config, exploding_factory):
    """A bad work order must fail at the gate, not inside the worker."""
    orchestrator = Orchestrator(
        repo_root=tmp_path,
        config=config,
        state_dir=tmp_path / "state",
        provider_factory=exploding_factory,
    )
    bad = json.dumps({**VALID, "allowed_paths": ["../../etc"]})
    outcome = orchestrator.run("apply this work order", work_order=bad)

    assert outcome.result.status == "failed"
    assert any("WorkOrderRejected" in e for e in outcome.result.exceptions)
    # ExplodingProvider would have raised AssertionError had the worker run.
    assert orchestrator.db.model_calls(outcome.task_id) == []
