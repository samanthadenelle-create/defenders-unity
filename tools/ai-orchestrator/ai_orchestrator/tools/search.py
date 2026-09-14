"""search_files - repository search BEFORE model invocation (spec section 14)."""

from __future__ import annotations

import re
from typing import Any

from .base import Tool, ToolContext

_SKIP_DIRS = {".git", ".venv", "node_modules", "__pycache__", "Library", "Temp", "obj"}


def search_files(
    ctx: ToolContext,
    pattern: str,
    path: str = ".",
    glob: str = "*",
    max_results: int = 100,
) -> dict[str, Any]:
    root = ctx.workspace.resolve(path)
    try:
        regex = re.compile(pattern)
    except re.error as exc:
        raise ValueError(f"invalid regex '{pattern}': {exc}") from exc

    matches: list[dict[str, Any]] = []
    for candidate in sorted(root.rglob(glob)):
        if len(matches) >= int(max_results):
            break
        if not candidate.is_file():
            continue
        if _SKIP_DIRS & set(candidate.parts):
            continue
        try:
            text = candidate.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for lineno, line in enumerate(text.splitlines(), start=1):
            if regex.search(line):
                matches.append(
                    {
                        "path": candidate.relative_to(ctx.workspace.root).as_posix(),
                        "line": lineno,
                        "text": line.strip()[:400],
                    }
                )
                if len(matches) >= int(max_results):
                    break
    return {"pattern": pattern, "match_count": len(matches), "matches": matches}


TOOLS = [
    Tool(
        name="search_files",
        fn=search_files,
        description="Regex-search files inside the workspace; returns path/line/text.",
        parameters={
            "type": "object",
            "properties": {
                "pattern": {"type": "string"},
                "path": {"type": "string"},
                "glob": {"type": "string"},
                "max_results": {"type": "integer"},
            },
            "required": ["pattern"],
        },
    )
]
