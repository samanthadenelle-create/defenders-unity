"""Deterministic operations - conventional code, zero tokens (spec section 6).

Every operation the router can classify as DETERMINISTIC has an implementation
here. The orchestrator dispatches into this module WITHOUT constructing a
provider, which is the property ``test_router.py`` pins with a provider that
raises on any use.
"""

from __future__ import annotations

import hashlib
import json
import re
import shutil
import subprocess
from pathlib import Path
from typing import Any, Callable


def _git(repo_root: Path, args: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def op_git_status(repo_root: Path, request: str, **_: Any) -> dict[str, Any]:
    result = _git(repo_root, ["status", "--short"])
    lines = [line for line in result.stdout.splitlines() if line.strip()]
    return {"operation": "git_status", "count": len(lines), "entries": lines[:200]}


def op_git_diff(repo_root: Path, request: str, **_: Any) -> dict[str, Any]:
    result = _git(repo_root, ["diff"])
    return {"operation": "git_diff", "diff": result.stdout[:100_000]}


def op_count_files(repo_root: Path, request: str, path: str = ".", **_: Any) -> dict[str, Any]:
    target = (repo_root / path).resolve()
    count = sum(1 for p in target.rglob("*") if p.is_file())
    return {"operation": "count_files", "path": path, "count": count}


def op_list_directory(repo_root: Path, request: str, path: str = ".", **_: Any) -> dict[str, Any]:
    target = (repo_root / path).resolve()
    return {
        "operation": "list_directory",
        "path": path,
        "entries": sorted(p.name for p in target.iterdir())[:500],
    }


def op_parse_json(repo_root: Path, request: str, path: str = "", **_: Any) -> dict[str, Any]:
    if not path:
        raise ValueError("parse_json needs a 'path' argument")
    body = (repo_root / path).read_text(encoding="utf-8")
    parsed = json.loads(body)
    return {
        "operation": "parse_json",
        "path": path,
        "valid": True,
        "top_level_keys": sorted(parsed)[:200] if isinstance(parsed, dict) else None,
    }


def op_compare_hashes(repo_root: Path, request: str, paths: list[str] | None = None, **_: Any) -> dict[str, Any]:
    digests = {}
    for rel in paths or []:
        data = (repo_root / rel).read_bytes()
        digests[rel] = hashlib.sha256(data).hexdigest()
    return {
        "operation": "compare_hashes",
        "digests": digests,
        "identical": len(set(digests.values())) <= 1,
    }


def op_copy_file(repo_root: Path, request: str, source: str = "", destination: str = "", **_: Any) -> dict[str, Any]:
    if not source or not destination:
        raise ValueError("copy_file needs 'source' and 'destination'")
    src, dst = repo_root / source, repo_root / destination
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    return {"operation": "copy_file", "source": source, "destination": destination}


def op_regex_replace(
    repo_root: Path,
    request: str,
    path: str = "",
    pattern: str = "",
    replacement: str = "",
    **_: Any,
) -> dict[str, Any]:
    if not path or not pattern:
        raise ValueError("regex_replace needs 'path' and 'pattern'")
    target = repo_root / path
    body = target.read_text(encoding="utf-8")
    new_body, count = re.subn(pattern, replacement, body)
    if count:
        target.write_text(new_body, encoding="utf-8", newline="")
    return {"operation": "regex_replace", "path": path, "replacements": count}


def op_run_tests(repo_root: Path, request: str, command: str = "", **_: Any) -> dict[str, Any]:
    if not command:
        raise ValueError("run_tests needs an explicit 'command'")
    result = subprocess.run(
        command.split(),
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return {
        "operation": "run_tests",
        "command": command,
        "passed": result.returncode == 0,
        "output": (result.stdout + result.stderr)[-20000:],
    }


def op_search_files(repo_root: Path, request: str, pattern: str = "", **_: Any) -> dict[str, Any]:
    if not pattern:
        raise ValueError("search_files needs a 'pattern'")
    result = subprocess.run(
        ["git", "grep", "-n", "-e", pattern],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    lines = result.stdout.splitlines()[:200]
    return {"operation": "search_files", "pattern": pattern, "matches": lines}


def op_build_report(repo_root: Path, request: str, database: Any = None, **_: Any) -> dict[str, Any]:
    """Usage report from known fields - spec section 13's daily/weekly report."""
    rows = database.usage_report() if database is not None else []
    return {"operation": "build_report", "usage": rows}


OPERATIONS: dict[str, Callable[..., dict[str, Any]]] = {
    "git_status": op_git_status,
    "git_diff": op_git_diff,
    "count_files": op_count_files,
    "list_directory": op_list_directory,
    "parse_json": op_parse_json,
    "compare_hashes": op_compare_hashes,
    "copy_file": op_copy_file,
    "regex_replace": op_regex_replace,
    "run_tests": op_run_tests,
    "search_files": op_search_files,
    "build_report": op_build_report,
}


class UnsupportedDeterministicOperation(RuntimeError):
    """Router named an operation with no implementation - a bug, not a model call."""


def execute(operation: str, repo_root: str | Path, request: str, **kwargs: Any) -> dict[str, Any]:
    fn = OPERATIONS.get(operation)
    if fn is None:
        raise UnsupportedDeterministicOperation(
            f"no deterministic implementation for '{operation}';"
            f" known: {sorted(OPERATIONS)}"
        )
    return fn(Path(repo_root).resolve(), request, **kwargs)
