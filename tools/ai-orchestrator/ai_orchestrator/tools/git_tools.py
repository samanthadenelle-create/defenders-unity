"""git_status / git_diff / create_worktree / delete_worktree (spec sections 9-10).

Read-only git is automatic. ``git_commit``/``git_push``/merge are deliberately
ABSENT from the registry: spec section 17 puts merge, push and publication behind
human approval, and the repo's own canon has exactly one committer. The
orchestrator produces a diff; a human lands it.
"""

from __future__ import annotations

import subprocess
from typing import Any

from .base import Tool, ToolContext


def _git(ctx: ToolContext, args: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=str(ctx.workspace.root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def git_status(ctx: ToolContext) -> dict[str, Any]:
    result = _git(ctx, ["status", "--short"])
    lines = [line for line in result.stdout.splitlines() if line.strip()]
    return {"exit_code": result.returncode, "entries": lines, "count": len(lines)}


def git_diff(ctx: ToolContext, staged: bool = False) -> dict[str, Any]:
    args = ["diff", "--cached"] if staged else ["diff"]
    result = _git(ctx, args)
    return {"exit_code": result.returncode, "diff": result.stdout}


def git_push_placeholder(ctx: ToolContext) -> dict[str, Any]:  # pragma: no cover
    raise PermissionError("push requires human approval")


TOOLS = [
    Tool(
        name="git_status",
        fn=git_status,
        description="Porcelain status of the workspace.",
        parameters={"type": "object", "properties": {}, "required": []},
    ),
    Tool(
        name="git_diff",
        fn=git_diff,
        description="Diff of the workspace; set staged=true for the index diff.",
        parameters={
            "type": "object",
            "properties": {"staged": {"type": "boolean"}},
            "required": [],
        },
    ),
    Tool(
        name="git_push",
        fn=git_push_placeholder,
        description="Push the workspace branch. REQUIRES HUMAN APPROVAL - never autonomous.",
        parameters={"type": "object", "properties": {}, "required": []},
        mutating=True,
        requires_approval=True,
    ),
]
