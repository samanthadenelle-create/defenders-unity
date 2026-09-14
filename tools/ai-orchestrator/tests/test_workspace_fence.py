"""The worker cannot write outside its assigned worktree (spec section 10)."""

from __future__ import annotations

import os
from pathlib import Path

import pytest

from ai_orchestrator.schemas.work_order import parse_work_order
from ai_orchestrator.state.audit_log import AuditLog
from ai_orchestrator.state.database import Database
from ai_orchestrator.tools import ToolContext, default_registry
from ai_orchestrator.workspace import Workspace, WorkspaceViolation

ORDER = {
    "work_order_id": "WO-fence",
    "title": "fence test",
    "objective": "prove the fence",
    "task_type": "doc_change",
    "allowed_paths": ["docs"],
    "prohibited_paths": ["docs/secret"],
}


@pytest.fixture
def workspace(tmp_path):
    root = tmp_path / "worktree"
    (root / "docs").mkdir(parents=True)
    (root / "docs" / "secret").mkdir()
    (root / "docs" / "readme.md").write_text("hello", encoding="utf-8")
    (tmp_path / "outside.txt").write_text("do not touch", encoding="utf-8")
    return Workspace(root=root, work_order=parse_work_order(ORDER))


@pytest.fixture
def registry(workspace, tmp_path):
    db = Database(tmp_path / "state" / "db.sqlite3")
    ctx = ToolContext(
        workspace=workspace,
        audit=AuditLog(db, tmp_path / "state" / "audit.jsonl"),
        task_id="WO-fence",
        commands_allowed=[],
    )
    return default_registry(ctx)


def test_inside_path_is_allowed(workspace):
    assert workspace.resolve("docs/readme.md").is_file()


@pytest.mark.parametrize(
    "escape",
    [
        "../outside.txt",
        "../../outside.txt",
        "docs/../../outside.txt",
        r"..\..\outside.txt",
        "C:/Windows/System32/drivers/etc/hosts",
        "/etc/passwd",
        "Z:/somewhere/else.txt",
    ],
)
def test_escapes_are_refused(workspace, escape):
    with pytest.raises(WorkspaceViolation):
        workspace.resolve(escape)


def test_sibling_with_shared_prefix_is_refused(tmp_path):
    """A STRING prefix check would wrongly allow 'WO-1-evil' inside 'WO-1'."""
    root = tmp_path / "ai-worktrees" / "WO-1"
    root.mkdir(parents=True)
    sibling = tmp_path / "ai-worktrees" / "WO-1-evil"
    sibling.mkdir()
    workspace = Workspace(root=root)
    with pytest.raises(WorkspaceViolation):
        workspace.resolve(str(sibling / "payload.txt"))


def test_prohibited_path_inside_the_root_is_refused(workspace):
    with pytest.raises(WorkspaceViolation):
        workspace.resolve("docs/secret/keys.txt")


def test_path_outside_allowed_paths_is_refused(workspace):
    with pytest.raises(WorkspaceViolation):
        workspace.resolve("src/main.py")


def test_case_insensitive_root_still_matches(workspace):
    """Windows: D:\\EOA and D:\\eoa are the same directory."""
    odd = Path(str(workspace.root).upper()) / "docs" / "readme.md"
    assert workspace.permits(odd) or os.name != "nt"


def test_write_file_tool_refuses_an_escape(registry, tmp_path):
    result = registry.call("write_file", {"path": "../outside.txt", "content": "pwned"})
    assert result.ok is False
    assert "workspace fence" in result.error
    assert (tmp_path / "outside.txt").read_text(encoding="utf-8") == "do not touch"


def test_write_file_tool_writes_inside(registry, workspace):
    result = registry.call("write_file", {"path": "docs/new.md", "content": "# new"})
    assert result.ok is True
    assert (workspace.root / "docs" / "new.md").read_text(encoding="utf-8") == "# new"


def test_unknown_tool_is_refused(registry):
    result = registry.call("rm_rf", {"path": "/"})
    assert result.ok is False and "no such tool" in result.error


def test_destructive_tool_requires_human_approval(registry):
    result = registry.call("git_push", {})
    assert result.ok is False and "human approval" in result.error


def test_command_not_in_allow_list_is_refused(registry):
    result = registry.call("execute_allowed_command", {"command": "git push --force"})
    assert result.ok is False
    assert "commands_allowed" in result.error


def test_every_tool_call_is_recorded(registry):
    registry.call("write_file", {"path": "docs/a.md", "content": "a"})
    registry.call("write_file", {"path": "../b.md", "content": "b"})
    events = registry.context.audit.db.events("WO-fence")
    names = [e["name"] for e in events]
    assert names.count("write_file") == 2
    assert [e["ok"] for e in events if e["name"] == "write_file"] == [1, 0]
