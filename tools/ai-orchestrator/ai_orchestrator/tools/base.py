"""Tool contract (spec section 9).

"LLMs must never receive unrestricted operating-system access." Every tool call
goes through :meth:`ToolRegistry.call`, which performs the spec's six steps in
order: validate input, check permissions, execute, capture output, record the
event, return a structured result. A tool function that bypasses the registry is
a bug.
"""

from __future__ import annotations

import time
import traceback
from dataclasses import dataclass, field
from typing import Any, Callable

from ..state.audit_log import AuditLog
from ..workspace import Workspace, WorkspaceViolation


@dataclass
class ToolResult:
    """Structured output - never a bare string back to a model."""

    ok: bool
    tool: str
    output: Any = None
    error: str = ""
    duration_seconds: float = 0.0

    def to_dict(self) -> dict[str, Any]:
        return {
            "ok": self.ok,
            "tool": self.tool,
            "output": self.output,
            "error": self.error,
        }


@dataclass
class ToolContext:
    """What a tool is allowed to see: a fenced workspace and an audit sink."""

    workspace: Workspace
    audit: AuditLog | None = None
    task_id: str = ""
    commands_allowed: list[str] = field(default_factory=list)
    executed_commands: list[str] = field(default_factory=list)

    def record(self, kind: str, name: str, ok: bool, detail: str = "") -> None:
        if self.audit:
            self.audit.record(self.task_id, kind, name, ok, detail)


ToolFn = Callable[..., Any]


@dataclass
class Tool:
    name: str
    fn: ToolFn
    description: str
    parameters: dict[str, Any]
    mutating: bool = False
    requires_approval: bool = False  # spec section 17


class ToolRegistry:
    """The allow-list. Nothing outside it is callable by a model."""

    def __init__(self, context: ToolContext) -> None:
        self.context = context
        self._tools: dict[str, Tool] = {}

    def register(self, tool: Tool) -> None:
        self._tools[tool.name] = tool

    def names(self) -> list[str]:
        return sorted(self._tools)

    def schema(self) -> list[dict[str, Any]]:
        """OpenAI/Ollama-shaped tool definitions for ``call_tools``."""
        return [
            {
                "type": "function",
                "function": {
                    "name": tool.name,
                    "description": tool.description,
                    "parameters": tool.parameters,
                },
            }
            for tool in self._tools.values()
        ]

    def call(self, name: str, arguments: dict[str, Any] | None = None) -> ToolResult:
        started = time.time()
        arguments = dict(arguments or {})

        # 1. Validate input.
        tool = self._tools.get(name)
        if tool is None:
            result = ToolResult(
                False, name, error=f"no such tool '{name}'; allowed: {self.names()}"
            )
            self.context.record("tool", name, False, result.error)
            return result

        unknown = set(arguments) - set(tool.parameters.get("properties", {}))
        if unknown:
            result = ToolResult(
                False, name, error=f"unknown arguments {sorted(unknown)} for '{name}'"
            )
            self.context.record("tool", name, False, result.error)
            return result
        missing = set(tool.parameters.get("required", [])) - set(arguments)
        if missing:
            result = ToolResult(
                False, name, error=f"missing required arguments {sorted(missing)}"
            )
            self.context.record("tool", name, False, result.error)
            return result

        # 2. Check permissions (spec section 17: destructive work needs a human).
        if tool.requires_approval:
            result = ToolResult(
                False,
                name,
                error=(
                    f"'{name}' is a destructive operation and requires human"
                    " approval; the orchestrator will not perform it autonomously"
                ),
            )
            self.context.record("tool", name, False, result.error)
            return result

        # 3-4. Execute and capture.
        try:
            output = tool.fn(self.context, **arguments)
            ok, error = True, ""
        except WorkspaceViolation as exc:
            output, ok, error = None, False, f"workspace fence: {exc}"
        except Exception as exc:  # noqa: BLE001 - a tool failure is data, not a crash
            output, ok, error = None, False, f"{type(exc).__name__}: {exc}"
            self.context.record("tool_traceback", name, False, traceback.format_exc())

        duration = time.time() - started
        # 5. Record the event.
        self.context.record(
            "tool",
            name,
            ok,
            (error or str(output))[:4000],
        )
        # 6. Return structured results.
        return ToolResult(ok, name, output=output, error=error, duration_seconds=duration)
