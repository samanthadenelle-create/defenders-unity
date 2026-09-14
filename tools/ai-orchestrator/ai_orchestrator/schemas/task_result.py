"""What the worker returns (spec section 8), plus the edit payload it produces."""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class WorkerFileEdit(BaseModel):
    """One whole-file rewrite proposed by the worker model.

    Whole content rather than a diff: a 7B-to-20B local model produces reliable
    file bodies and unreliable unified diffs, and the workspace fence checks the
    path either way.
    """

    model_config = ConfigDict(extra="ignore")

    path: str
    content: str


class WorkerPatch(BaseModel):
    """The structured envelope the worker model is asked to emit."""

    model_config = ConfigDict(extra="ignore")

    files: list[WorkerFileEdit] = Field(default_factory=list)
    summary: str = ""
    needs_escalation: bool = False
    escalation_reason: str = ""

    @staticmethod
    def json_schema_for_provider() -> dict[str, Any]:
        """A hand-written JSON Schema for structured decoding.

        Deliberately not ``model_json_schema()``: Ollama's ``format`` field wants a
        flat schema with no ``$defs``/``$ref``, which pydantic emits for nested
        models.
        """
        return {
            "type": "object",
            "properties": {
                "files": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "path": {"type": "string"},
                            "content": {"type": "string"},
                        },
                        "required": ["path", "content"],
                    },
                },
                "summary": {"type": "string"},
                "needs_escalation": {"type": "boolean"},
                "escalation_reason": {"type": "string"},
            },
            "required": ["files", "summary", "needs_escalation"],
        }


class TaskResult(BaseModel):
    """Spec section 8's return contract, plus the audit fields the board needs."""

    model_config = ConfigDict(extra="forbid")

    status: str = "completed"
    files_changed: list[str] = Field(default_factory=list)
    commands_executed: list[str] = Field(default_factory=list)
    tests_run: list[str] = Field(default_factory=list)
    tests_passed: list[str] = Field(default_factory=list)
    tests_failed: list[str] = Field(default_factory=list)
    summary: str = ""
    exceptions: list[str] = Field(default_factory=list)
    needs_escalation: bool = False

    # Audit extras (spec sections 11/13) - not part of the worker's own JSON.
    work_order_id: str = ""
    attempts: int = 0
    diff: str = ""
    worker_calls: int = 0
    reasoner_calls: int = 0
    classification: str = ""
