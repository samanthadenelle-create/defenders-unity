"""SQLite board + model-call ledger (spec sections 3, 11, 13).

Every model call is recorded with provider, model, input tokens, output tokens,
estimated cost and task id - spec section 13 lists exactly those six fields, and
the daily/weekly report reads them back out of this one table.
"""

from __future__ import annotations

import sqlite3
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable

SCHEMA = """
CREATE TABLE IF NOT EXISTS tasks (
    task_id           TEXT PRIMARY KEY,
    request           TEXT NOT NULL,
    classification    TEXT NOT NULL,
    state             TEXT NOT NULL,
    reasoner_model    TEXT,
    worker_model      TEXT,
    files_changed     TEXT,
    input_tokens      INTEGER NOT NULL DEFAULT 0,
    output_tokens     INTEGER NOT NULL DEFAULT 0,
    estimated_cost_usd REAL   NOT NULL DEFAULT 0.0,
    duration_seconds  REAL    NOT NULL DEFAULT 0.0,
    tests_run         TEXT,
    failure_reason    TEXT,
    retry_count       INTEGER NOT NULL DEFAULT 0,
    created_at        REAL NOT NULL,
    updated_at        REAL NOT NULL
);

CREATE TABLE IF NOT EXISTS model_calls (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id            TEXT NOT NULL,
    role               TEXT NOT NULL,
    provider           TEXT NOT NULL,
    model              TEXT NOT NULL,
    purpose            TEXT NOT NULL DEFAULT '',
    input_tokens       INTEGER NOT NULL DEFAULT 0,
    output_tokens      INTEGER NOT NULL DEFAULT 0,
    estimated_cost_usd REAL    NOT NULL DEFAULT 0.0,
    duration_seconds   REAL    NOT NULL DEFAULT 0.0,
    created_at         REAL NOT NULL
);

CREATE TABLE IF NOT EXISTS audit_events (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id    TEXT NOT NULL DEFAULT '',
    kind       TEXT NOT NULL,
    name       TEXT NOT NULL,
    ok         INTEGER NOT NULL DEFAULT 1,
    detail     TEXT NOT NULL DEFAULT '',
    created_at REAL NOT NULL
);
"""

# Spec section 11 board states.
BOARD_STATES = (
    "READY",
    "REASONING",
    "PLANNED",
    "EXECUTING",
    "VALIDATING",
    "DONE",
    "RCA",
)


@dataclass
class ModelCall:
    task_id: str
    role: str
    provider: str
    model: str
    purpose: str = ""
    input_tokens: int = 0
    output_tokens: int = 0
    estimated_cost_usd: float = 0.0
    duration_seconds: float = 0.0


@dataclass
class TaskRow:
    task_id: str
    request: str
    classification: str
    state: str = "READY"
    reasoner_model: str | None = None
    worker_model: str | None = None
    files_changed: list[str] = field(default_factory=list)
    tests_run: list[str] = field(default_factory=list)
    failure_reason: str = ""
    retry_count: int = 0
    duration_seconds: float = 0.0


class Database:
    """Thin SQLite wrapper. One file, created on demand, safe to open repeatedly."""

    def __init__(self, path: str | Path) -> None:
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._conn = sqlite3.connect(str(self.path))
        self._conn.row_factory = sqlite3.Row
        self._conn.executescript(SCHEMA)
        self._conn.commit()

    def close(self) -> None:
        self._conn.close()

    # -- board -------------------------------------------------------------
    def create_task(self, row: TaskRow) -> None:
        now = time.time()
        self._conn.execute(
            "INSERT OR REPLACE INTO tasks (task_id, request, classification, state,"
            " reasoner_model, worker_model, files_changed, tests_run, failure_reason,"
            " retry_count, created_at, updated_at)"
            " VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
            (
                row.task_id,
                row.request,
                row.classification,
                row.state,
                row.reasoner_model,
                row.worker_model,
                "\n".join(row.files_changed),
                "\n".join(row.tests_run),
                row.failure_reason,
                row.retry_count,
                now,
                now,
            ),
        )
        self._conn.commit()

    def set_state(self, task_id: str, state: str, **fields: Any) -> None:
        if state not in BOARD_STATES:
            raise ValueError(f"unknown board state '{state}'")
        assignments = ["state = ?", "updated_at = ?"]
        values: list[Any] = [state, time.time()]
        for key, value in fields.items():
            if isinstance(value, (list, tuple)):
                value = "\n".join(str(v) for v in value)
            assignments.append(f"{key} = ?")
            values.append(value)
        values.append(task_id)
        self._conn.execute(
            f"UPDATE tasks SET {', '.join(assignments)} WHERE task_id = ?", values
        )
        self._conn.commit()

    def get_task(self, task_id: str) -> dict[str, Any] | None:
        cur = self._conn.execute("SELECT * FROM tasks WHERE task_id = ?", (task_id,))
        row = cur.fetchone()
        return dict(row) if row else None

    # -- model-call ledger -------------------------------------------------
    def record_model_call(self, call: ModelCall) -> None:
        self._conn.execute(
            "INSERT INTO model_calls (task_id, role, provider, model, purpose,"
            " input_tokens, output_tokens, estimated_cost_usd, duration_seconds, created_at)"
            " VALUES (?,?,?,?,?,?,?,?,?,?)",
            (
                call.task_id,
                call.role,
                call.provider,
                call.model,
                call.purpose,
                call.input_tokens,
                call.output_tokens,
                call.estimated_cost_usd,
                call.duration_seconds,
                time.time(),
            ),
        )
        self._conn.execute(
            "UPDATE tasks SET input_tokens = input_tokens + ?,"
            " output_tokens = output_tokens + ?,"
            " estimated_cost_usd = estimated_cost_usd + ?, updated_at = ?"
            " WHERE task_id = ?",
            (
                call.input_tokens,
                call.output_tokens,
                call.estimated_cost_usd,
                time.time(),
                call.task_id,
            ),
        )
        self._conn.commit()

    def model_calls(self, task_id: str | None = None) -> list[dict[str, Any]]:
        if task_id:
            cur = self._conn.execute(
                "SELECT * FROM model_calls WHERE task_id = ? ORDER BY id", (task_id,)
            )
        else:
            cur = self._conn.execute("SELECT * FROM model_calls ORDER BY id")
        return [dict(r) for r in cur.fetchall()]

    def usage_report(self, since_epoch: float = 0.0) -> list[dict[str, Any]]:
        """Spec section 13's daily/weekly usage report, grouped by provider+model."""
        cur = self._conn.execute(
            "SELECT provider, model, COUNT(*) AS calls,"
            " SUM(input_tokens) AS input_tokens, SUM(output_tokens) AS output_tokens,"
            " SUM(estimated_cost_usd) AS estimated_cost_usd"
            " FROM model_calls WHERE created_at >= ?"
            " GROUP BY provider, model ORDER BY estimated_cost_usd DESC",
            (since_epoch,),
        )
        return [dict(r) for r in cur.fetchall()]

    # -- audit -------------------------------------------------------------
    def record_event(
        self, task_id: str, kind: str, name: str, ok: bool, detail: str = ""
    ) -> None:
        self._conn.execute(
            "INSERT INTO audit_events (task_id, kind, name, ok, detail, created_at)"
            " VALUES (?,?,?,?,?,?)",
            (task_id, kind, name, 1 if ok else 0, detail[:20000], time.time()),
        )
        self._conn.commit()

    def events(self, task_id: str) -> list[dict[str, Any]]:
        cur = self._conn.execute(
            "SELECT * FROM audit_events WHERE task_id = ? ORDER BY id", (task_id,)
        )
        return [dict(r) for r in cur.fetchall()]


def summarize_calls(calls: Iterable[dict[str, Any]]) -> tuple[int, int, int, float]:
    """(count, input_tokens, output_tokens, cost) over a ledger slice."""
    count = inp = out = 0
    cost = 0.0
    for call in calls:
        count += 1
        inp += int(call.get("input_tokens") or 0)
        out += int(call.get("output_tokens") or 0)
        cost += float(call.get("estimated_cost_usd") or 0.0)
    return count, inp, out, cost
