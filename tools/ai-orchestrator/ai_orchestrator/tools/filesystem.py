"""read_file / write_file / list_directory - all fenced by the Workspace."""

from __future__ import annotations

from typing import Any

from .base import Tool, ToolContext

MAX_READ_BYTES = 400_000


def read_file(ctx: ToolContext, path: str, max_bytes: int = MAX_READ_BYTES) -> dict[str, Any]:
    target = ctx.workspace.resolve(path)
    if not target.is_file():
        raise FileNotFoundError(f"{path} is not a file inside the workspace")
    data = target.read_bytes()[: int(max_bytes)]
    return {
        "path": path,
        "content": data.decode("utf-8", errors="replace"),
        "bytes": len(data),
    }


def write_file(ctx: ToolContext, path: str, content: str) -> dict[str, Any]:
    """Write a whole file. The fence is checked BEFORE any bytes are produced."""
    target = ctx.workspace.resolve(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    existed = target.is_file()
    previous = target.read_text(encoding="utf-8", errors="replace") if existed else ""
    if existed and previous == content:
        return {"path": path, "changed": False, "created": False, "bytes": len(content)}
    target.write_text(content, encoding="utf-8", newline="")
    return {
        "path": path,
        "changed": True,
        "created": not existed,
        "bytes": len(content.encode("utf-8")),
    }


def list_directory(ctx: ToolContext, path: str = ".", limit: int = 500) -> dict[str, Any]:
    target = ctx.workspace.resolve(path)
    if not target.is_dir():
        raise NotADirectoryError(f"{path} is not a directory inside the workspace")
    entries = []
    for child in sorted(target.iterdir())[: int(limit)]:
        entries.append(
            {
                "name": child.name,
                "kind": "dir" if child.is_dir() else "file",
                "bytes": child.stat().st_size if child.is_file() else 0,
            }
        )
    return {"path": path, "entries": entries}


TOOLS = [
    Tool(
        name="read_file",
        fn=read_file,
        description="Read a UTF-8 text file inside the assigned workspace.",
        parameters={
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "workspace-relative path"},
                "max_bytes": {"type": "integer"},
            },
            "required": ["path"],
        },
    ),
    Tool(
        name="write_file",
        fn=write_file,
        description="Write the complete new contents of a file inside the workspace.",
        parameters={
            "type": "object",
            "properties": {
                "path": {"type": "string"},
                "content": {"type": "string"},
            },
            "required": ["path", "content"],
        },
        mutating=True,
    ),
    Tool(
        name="list_directory",
        fn=list_directory,
        description="List entries of a directory inside the workspace.",
        parameters={
            "type": "object",
            "properties": {
                "path": {"type": "string"},
                "limit": {"type": "integer"},
            },
            "required": [],
        },
    ),
]
