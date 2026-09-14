"""Validator output (spec section 2's PASS/FAIL fork)."""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field


class ValidationCheck(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str
    passed: bool
    detail: str = ""


class ValidationResult(BaseModel):
    model_config = ConfigDict(extra="forbid")

    passed: bool
    checks: list[ValidationCheck] = Field(default_factory=list)
    failure_reason: str = ""

    @classmethod
    def from_checks(cls, checks: list[ValidationCheck]) -> "ValidationResult":
        failed = [c for c in checks if not c.passed]
        return cls(
            passed=not failed,
            checks=checks,
            failure_reason="; ".join(f"{c.name}: {c.detail}" for c in failed),
        )
