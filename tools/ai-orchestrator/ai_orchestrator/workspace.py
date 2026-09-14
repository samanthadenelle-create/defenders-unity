"""Repository safety: the workspace fence and worktree lifecycle (spec section 10).

Two independent fences, both enforced on every write:

1. **Root fence** - the resolved target must sit inside the assigned worktree.
   ``C:\\``, ``/``, ``$HOME``, a sibling drive and ``..`` escapes are all refused.
2. **Work-order fence** - the path must also sit under ``allowed_paths`` and
   outside ``prohibited_paths``.

Windows specifics that a naive check gets wrong, and which the tests pin:
``Path.resolve()`` on both sides, ``os.path.normcase`` before comparing (drive
letters and case), and a PARTS comparison rather than a string prefix - so
``D:/eoa/ai-worktrees/WO-1-evil`` is not treated as inside
``D:/eoa/ai-worktrees/WO-1``.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import uuid
from dataclasses import dataclass
from pathlib import Path

from .schemas.work_order import WorkOrder


class WorkspaceViolation(PermissionError):
    """A tool tried to touch a path outside the authorised workspace."""


def _norm(path: Path) -> Path:
    return Path(os.path.normcase(os.path.normpath(str(path))))


def is_inside(root: Path, candidate: Path) -> bool:
    """True when ``candidate`` resolves inside ``root``. Case/drive aware."""
    root_n = _norm(root.resolve())
    try:
        cand_n = _norm(Path(candidate).resolve())
    except (OSError, ValueError):
        return False
    if cand_n == root_n:
        return True
    return root_n.parts == cand_n.parts[: len(root_n.parts)]


@dataclass
class Workspace:
    """The only place a worker's file writes are allowed to land."""

    root: Path
    work_order: WorkOrder | None = None

    def __post_init__(self) -> None:
        self.root = Path(self.root).resolve()

    def resolve(self, relative_or_absolute: str | Path) -> Path:
        """Resolve a worker-supplied path, or refuse it.

        Raises :class:`WorkspaceViolation` - never returns a path outside root.
        """
        raw = str(relative_or_absolute).strip()
        if not raw:
            raise WorkspaceViolation("empty path")
        candidate = Path(raw)
        target = candidate if candidate.is_absolute() else self.root / candidate
        if not is_inside(self.root, target):
            raise WorkspaceViolation(
                f"path '{raw}' resolves outside the assigned workspace {self.root}"
            )
        resolved = Path(os.path.normpath(str(target)))
        if self.work_order is not None:
            rel = resolved.relative_to(self.root).as_posix()
            if not self.work_order.permits(rel):
                raise WorkspaceViolation(
                    f"path '{rel}' is not covered by work order"
                    f" {self.work_order.work_order_id} allowed_paths"
                    f" {self.work_order.allowed_paths}"
                    f" / prohibited_paths {self.work_order.prohibited_paths}"
                )
        return resolved

    def permits(self, relative_or_absolute: str | Path) -> bool:
        try:
            self.resolve(relative_or_absolute)
        except WorkspaceViolation:
            return False
        return True


class WorktreeError(RuntimeError):
    """git worktree add/remove failed."""


def _git(args: list[str], cwd: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=str(cwd),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


@dataclass
class Worktree:
    """A temporary git worktree under ``ai-worktrees/`` (spec section 10).

    Cleanup is MANDATORY: the context manager removes the worktree and prunes,
    so the repo never accumulates abandoned AI worktrees the way
    ``.claude/worktrees/`` already has.
    """

    repo_root: Path
    work_order_id: str
    path: Path
    _created: bool = False

    @classmethod
    def create(
        cls,
        repo_root: str | Path,
        work_order_id: str,
        *,
        base: str = "HEAD",
        sparse_paths: list[str] | None = None,
    ) -> "Worktree":
        repo_root = Path(repo_root).resolve()
        slug = f"{work_order_id}-{uuid.uuid4().hex[:8]}"
        path = repo_root / "ai-worktrees" / slug
        path.parent.mkdir(parents=True, exist_ok=True)

        # --detach: no branch litter. --no-checkout + sparse: this repo is large
        # and LFS-backed; a full checkout per task is minutes of nothing.
        args = ["worktree", "add", "--detach", "--no-checkout", str(path), base]
        result = _git(args, repo_root)
        if result.returncode != 0:
            raise WorktreeError(
                f"git worktree add failed: {result.stderr.strip() or result.stdout.strip()}"
            )
        tree = cls(repo_root=repo_root, work_order_id=work_order_id, path=path, _created=True)

        env_skip = {"GIT_LFS_SKIP_SMUDGE": "1"}
        if sparse_paths:
            sparse = _git(["sparse-checkout", "set", *sparse_paths], path)
            if sparse.returncode != 0:
                tree.remove()
                raise WorktreeError(f"git sparse-checkout failed: {sparse.stderr.strip()}")
        checkout = subprocess.run(
            ["git", "checkout"],
            cwd=str(path),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env={**os.environ, **env_skip},
        )
        if checkout.returncode != 0:
            tree.remove()
            raise WorktreeError(f"git checkout in worktree failed: {checkout.stderr.strip()}")
        return tree

    def diff(self) -> str:
        """The complete git diff of the worker's changes (spec section 23)."""
        _git(["add", "-A", "--", "."], self.path)
        result = _git(["diff", "--cached"], self.path)
        return result.stdout

    def remove(self) -> None:
        """Remove the worktree and prune. Idempotent; safe in a finally block."""
        if not self._created:
            return
        _git(["worktree", "remove", "--force", str(self.path)], self.repo_root)
        if self.path.exists():
            shutil.rmtree(self.path, ignore_errors=True)
        _git(["worktree", "prune"], self.repo_root)
        parent = self.path.parent
        try:
            if parent.is_dir() and not any(parent.iterdir()):
                parent.rmdir()
        except OSError:
            pass
        self._created = False

    def __enter__(self) -> "Worktree":
        return self

    def __exit__(self, *exc: object) -> None:
        self.remove()
