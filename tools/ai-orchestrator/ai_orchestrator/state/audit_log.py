"""Append-only audit trail for every tool call and model call.

Spec section 9 step 5 - "Record the event" - and section 11's per-task record.
Writes to SQLite (queryable) and to a JSONL file (greppable when SQLite is busy).
"""

from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Any

from .database import Database


class AuditLog:
    def __init__(self, db: Database, jsonl_path: str | Path | None = None) -> None:
        self.db = db
        self.jsonl_path = Path(jsonl_path) if jsonl_path else None
        if self.jsonl_path:
            self.jsonl_path.parent.mkdir(parents=True, exist_ok=True)

    def record(
        self,
        task_id: str,
        kind: str,
        name: str,
        ok: bool = True,
        detail: str = "",
        **extra: Any,
    ) -> None:
        self.db.record_event(task_id, kind, name, ok, detail)
        if self.jsonl_path:
            entry = {
                "ts": time.time(),
                "task_id": task_id,
                "kind": kind,
                "name": name,
                "ok": ok,
                "detail": detail[:4000],
                **extra,
            }
            with self.jsonl_path.open("a", encoding="utf-8") as handle:
                handle.write(json.dumps(entry, ensure_ascii=False) + "\n")
