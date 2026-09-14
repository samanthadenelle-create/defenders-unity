"""Escalation rules (spec section 12).

The worker must STOP rather than improvise. Each trigger below is one line of
the spec's list, turned into something checkable against a live attempt.
"""

from __future__ import annotations

from dataclasses import dataclass, field

ESCALATION_TRIGGERS = (
    "acceptance_criteria_conflict",
    "ambiguous_requirement",
    "architecture_change_required",
    "public_api_change",
    "schema_migration_required",
    "security_boundary_change",
    "more_files_than_expected",
    "tests_failed_twice",
    "dependency_unavailable",
    "worker_confidence_below_threshold",
)


@dataclass
class EscalationDecision:
    escalate: bool
    triggers: list[str] = field(default_factory=list)
    detail: str = ""


def assess_escalation(
    *,
    attempts: int,
    max_retries: int,
    worker_requested: bool = False,
    worker_reason: str = "",
    files_touched: int = 0,
    max_files: int = 12,
    scope_violations: list[str] | None = None,
    tests_failed: list[str] | None = None,
    exceptions: list[str] | None = None,
) -> EscalationDecision:
    """Decide whether this attempt must go back to the reasoner.

    ``attempts`` counts attempts already made. The autonomous budget is
    ``1 + max_retries``; once it is spent the answer is always escalate.
    """
    triggers: list[str] = []
    details: list[str] = []

    if worker_requested:
        triggers.append("worker_confidence_below_threshold")
        details.append(worker_reason or "worker asked to escalate")

    if files_touched > max_files:
        triggers.append("more_files_than_expected")
        details.append(f"{files_touched} files exceeds the cap of {max_files}")

    for violation in scope_violations or []:
        triggers.append("security_boundary_change")
        details.append(f"scope violation: {violation}")

    if (tests_failed or exceptions) and attempts >= 2:
        triggers.append("tests_failed_twice")
        details.append(
            f"failures persisted across {attempts} attempts: "
            + "; ".join((tests_failed or []) + (exceptions or []))[:400]
        )

    if attempts > max_retries:
        triggers.append("retry_budget_exhausted")
        details.append(
            f"{attempts} attempts made; autonomous budget is {1 + max_retries}"
        )

    return EscalationDecision(
        escalate=bool(triggers), triggers=triggers, detail="; ".join(details)
    )
