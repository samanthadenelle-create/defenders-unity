"""execute_allowed_command - the only door to a subprocess.

The command must appear in the work order's ``commands_allowed`` list. There is
no shell interpolation: the command is split with :func:`shlex.split` and run
without ``shell=True``, so a work order cannot smuggle ``&& rm -rf`` past the
allow-list.
"""

from __future__ import annotations

import shlex
import subprocess
from typing import Any

from .base import Tool, ToolContext

DEFAULT_TIMEOUT = 900


def execute_allowed_command(
    ctx: ToolContext, command: str, timeout_seconds: int = DEFAULT_TIMEOUT
) -> dict[str, Any]:
    command = str(command).strip()
    if not command:
        raise ValueError("empty command")
    if command not in ctx.commands_allowed:
        raise PermissionError(
            f"command '{command}' is not in the work order's commands_allowed"
            f" {ctx.commands_allowed}"
        )
    argv = shlex.split(command, posix=False)
    result = subprocess.run(
        argv,
        cwd=str(ctx.workspace.root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=int(timeout_seconds),
    )
    ctx.executed_commands.append(command)
    return {
        "command": command,
        "exit_code": result.returncode,
        "stdout": result.stdout[-20000:],
        "stderr": result.stderr[-20000:],
    }


TOOLS = [
    Tool(
        name="execute_allowed_command",
        fn=execute_allowed_command,
        description=(
            "Run a command that the current work order explicitly allow-lists,"
            " from the workspace root."
        ),
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
