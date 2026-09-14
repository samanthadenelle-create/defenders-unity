"""run_test - execute the work order's ``tests_required`` entries.

A test entry is a command string; it must also appear in ``commands_allowed``,
so a work order cannot name an arbitrary executable as "a test".
"""

from __future__ import annotations

import shlex
import subprocess
from typing import Any

from .base import Tool, ToolContext

DEFAULT_TIMEOUT = 1800


def run_test(ctx: ToolContext, command: str, timeout_seconds: int = DEFAULT_TIMEOUT) -> dict[str, Any]:
    command = str(command).strip()
    if command not in ctx.commands_allowed:
        raise PermissionError(
            f"test command '{command}' is not in commands_allowed {ctx.commands_allowed}"
        )
    try:
        result = subprocess.run(
            shlex.split(command, posix=False),
            cwd=str(ctx.workspace.root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=int(timeout_seconds),
        )
    except subprocess.TimeoutExpired:
        return {
            "command": command,
            "passed": False,
            "exit_code": None,
            "output": f"timed out after {timeout_seconds}s",
        }
    ctx.executed_commands.append(command)
    return {
        "command": command,
        "passed": result.returncode == 0,
        "exit_code": result.returncode,
        "output": (result.stdout + result.stderr)[-20000:],
    }


TOOLS = [
    Tool(
        name="run_test",
        fn=run_test,
        description="Run one allow-listed test command from the workspace root.",
        parameters={
            "type": "object",
            "properties": {
                "command": {"type": "string"},
                "timeout_seconds": {"type": "integer"},
            },
            "required": ["command"],
        },
        mutating=True,
    )
]
