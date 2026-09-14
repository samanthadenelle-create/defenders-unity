"""Context builder (spec section 14).

"Do NOT dump an entire repository into either model." The worker receives the
work order, the relevant file bodies, the project conventions from ``.ai/`` and
the most recent error output - nothing else. Repository search runs BEFORE the
model call, not as a tool the model has to think its way to.
"""

from __future__ import annotations

from pathlib import Path

from .schemas.work_order import WorkOrder

MAX_FILE_CHARS = 24_000
MAX_TOTAL_CHARS = 120_000
PROJECT_CONTEXT_DIR = ".ai"
PROJECT_CONTEXT_FILES = (
    "architecture.md",
    "conventions.md",
    "decisions.md",
    "glossary.md",
    "project-rules.md",
)


def project_knowledge(repo_root: str | Path, max_chars: int = 8_000) -> str:
    """Read ``.ai/*.md`` if the project keeps one (spec section 15)."""
    root = Path(repo_root) / PROJECT_CONTEXT_DIR
    chunks: list[str] = []
    budget = max_chars
    for name in PROJECT_CONTEXT_FILES:
        path = root / name
        if not path.is_file() or budget <= 0:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")[:budget]
        budget -= len(text)
        chunks.append(f"### {PROJECT_CONTEXT_DIR}/{name}\n{text}")
    return "\n\n".join(chunks)


def collect_files(workspace_root: Path, work_order: WorkOrder) -> list[tuple[str, str]]:
    """Every existing file the work order allows, as (relative path, body)."""
    collected: list[tuple[str, str]] = []
    total = 0
    for entry in work_order.allowed_paths:
        target = workspace_root / entry
        candidates = (
            [target]
            if target.is_file()
            else sorted(p for p in target.rglob("*") if p.is_file())
            if target.is_dir()
            else []
        )
        for path in candidates:
            rel = path.relative_to(workspace_root).as_posix()
            if not work_order.permits(rel):
                continue
            if total >= MAX_TOTAL_CHARS:
                return collected
            try:
                body = path.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            body = body[:MAX_FILE_CHARS]
            total += len(body)
            collected.append((rel, body))
    return collected


def build_worker_context(
    workspace_root: Path,
    work_order: WorkOrder,
    *,
    repo_root: str | Path | None = None,
    tool_names: list[str] | None = None,
    recent_errors: str = "",
) -> str:
    """The spec section 8 briefing: objective, allowed files, requirements,
    steps, acceptance criteria, tests, available tools - and nothing more."""
    parts: list[str] = [
        f"OBJECTIVE\n{work_order.objective}",
        "ALLOWED FILES (you may write ONLY these paths)\n"
        + "\n".join(f"- {p}" for p in work_order.allowed_paths),
    ]
    if work_order.prohibited_paths:
        parts.append(
            "PROHIBITED PATHS\n"
            + "\n".join(f"- {p}" for p in work_order.prohibited_paths)
        )
    if work_order.requirements:
        parts.append(
            "REQUIREMENTS\n"
            + "\n".join(f"{i}. {r}" for i, r in enumerate(work_order.requirements, 1))
        )
    if work_order.implementation_steps:
        parts.append(
            "IMPLEMENTATION STEPS\n"
            + "\n".join(
                f"{i}. {s}" for i, s in enumerate(work_order.implementation_steps, 1)
            )
        )
    if work_order.acceptance_criteria:
        parts.append(
            "ACCEPTANCE CRITERIA\n"
            + "\n".join(f"- {c}" for c in work_order.acceptance_criteria)
        )
    if work_order.tests_required:
        parts.append("TESTS\n" + "\n".join(f"- {t}" for t in work_order.tests_required))
    if tool_names:
        parts.append("AVAILABLE TOOLS\n" + ", ".join(tool_names))

    files = collect_files(workspace_root, work_order)
    if files:
        rendered = "\n\n".join(
            f"--- FILE: {rel} ---\n{body}" for rel, body in files
        )
        parts.append("CURRENT FILE CONTENTS\n" + rendered)
    else:
        parts.append("CURRENT FILE CONTENTS\n(no existing files under allowed_paths)")

    if repo_root:
        knowledge = project_knowledge(repo_root)
        if knowledge:
            parts.append("PROJECT CONVENTIONS\n" + knowledge)
    if recent_errors:
        parts.append("RECENT ERROR OUTPUT FROM YOUR PREVIOUS ATTEMPT\n" + recent_errors[:8000])
    return "\n\n".join(parts)
