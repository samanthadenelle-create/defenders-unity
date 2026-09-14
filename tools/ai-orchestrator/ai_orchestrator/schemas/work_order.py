"""The WorkOrder contract (spec section 7).

The reasoner returns structured JSON, never prose. An object that does not
validate against this schema MUST NOT reach the worker - :func:`parse_work_order`
is the only sanctioned door, and it raises :class:`WorkOrderRejected`.
"""

from __future__ import annotations

import json
import re
from enum import Enum
from pathlib import PurePosixPath
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, ValidationError, field_validator

_WO_ID = re.compile(r"^WO-[A-Za-z0-9._-]{1,64}$")


class RiskLevel(str, Enum):
    low = "low"
    medium = "medium"
    high = "high"


class TaskType(str, Enum):
    code_change = "code_change"
    doc_change = "doc_change"
    data_change = "data_change"
    test_generation = "test_generation"
    localization = "localization"
    refactor = "refactor"
    analysis = "analysis"


class WorkOrderRejected(ValueError):
    """A work order failed schema validation and was refused before the worker."""

    def __init__(self, reason: str, errors: list[dict[str, Any]] | None = None) -> None:
        super().__init__(reason)
        self.reason = reason
        self.errors = errors or []


class WorkOrder(BaseModel):
    """Exactly the object shape in spec section 7, with the fences enforced."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    work_order_id: str
    title: str = Field(min_length=1, max_length=200)
    objective: str = Field(min_length=1)
    task_type: TaskType
    risk: RiskLevel = RiskLevel.low
    allowed_paths: list[str] = Field(min_length=1)
    prohibited_paths: list[str] = Field(default_factory=list)
    requirements: list[str] = Field(default_factory=list)
    implementation_steps: list[str] = Field(default_factory=list)
    acceptance_criteria: list[str] = Field(default_factory=list)
    tests_required: list[str] = Field(default_factory=list)
    commands_allowed: list[str] = Field(default_factory=list)
    requires_reasoner_review: bool = False

    @field_validator("work_order_id")
    @classmethod
    def _id_shape(cls, value: str) -> str:
        if not _WO_ID.match(value):
            raise ValueError("work_order_id must look like 'WO-<id>'")
        return value

    @field_validator("allowed_paths", "prohibited_paths")
    @classmethod
    def _relative_paths_only(cls, values: list[str]) -> list[str]:
        cleaned: list[str] = []
        for raw in values:
            text = str(raw).strip().replace("\\", "/")
            if not text:
                raise ValueError("path entries may not be empty")
            if text.startswith("/") or re.match(r"^[A-Za-z]:", text):
                raise ValueError(
                    f"path '{raw}' is absolute; work-order paths are repo-relative"
                )
            if ".." in PurePosixPath(text).parts:
                raise ValueError(f"path '{raw}' escapes the workspace with '..'")
            cleaned.append(text.rstrip("/"))
        return cleaned

    def permits(self, repo_relative: str) -> bool:
        """True when ``repo_relative`` sits under allowed_paths and outside prohibited."""
        target = PurePosixPath(str(repo_relative).replace("\\", "/"))
        for blocked in self.prohibited_paths:
            if _covers(PurePosixPath(blocked), target):
                return False
        return any(_covers(PurePosixPath(a), target) for a in self.allowed_paths)


def _covers(prefix: PurePosixPath, target: PurePosixPath) -> bool:
    return target == prefix or prefix in target.parents


def parse_work_order(payload: str | bytes | dict[str, Any]) -> WorkOrder:
    """Validate an untrusted payload into a :class:`WorkOrder` or refuse it.

    This is the gate named in spec section 7: "Invalid work orders must not reach
    the worker." Every caller in the orchestrator goes through here.
    """
    if isinstance(payload, (str, bytes)):
        try:
            payload = json.loads(payload)
        except json.JSONDecodeError as exc:
            raise WorkOrderRejected(f"work order is not valid JSON: {exc}") from exc
    if not isinstance(payload, dict):
        raise WorkOrderRejected("work order must be a JSON object")
    try:
        return WorkOrder.model_validate(payload)
    except ValidationError as exc:
        detail = "; ".join(
            f"{'.'.join(str(p) for p in e['loc']) or '<root>'}: {e['msg']}"
            for e in exc.errors()
        )
        raise WorkOrderRejected(
            f"work order failed schema validation: {detail}", exc.errors()
        ) from exc
