"""FastAPI surface (spec section 3; the section 16 MODE B tool names).

Phase 1 exposes the high-level operations only - submit_work_order,
get_task_status, get_task_result, get_git_diff - never raw shell, per spec
section 16's closing rule. Run it with:

    uvicorn ai_orchestrator.api:app

It is NOT part of the Phase 1 acceptance path (the CLI is), and it has not been
exercised against a live client on this machine.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from .config import load_config
from .orchestrator import Orchestrator
from .schemas.work_order import WorkOrderRejected

app = FastAPI(title="AI Orchestrator", version="0.1.0")


def _orchestrator() -> Orchestrator:
    here = Path(__file__).resolve()
    root = next((p for p in here.parents if (p / ".git").exists()), Path.cwd())
    return Orchestrator(root, load_config())


class SubmitRequest(BaseModel):
    request: str = ""
    work_order: dict[str, Any] | None = None
    candidate_paths: list[str] = []


@app.post("/submit_work_order")
def submit_work_order(body: SubmitRequest) -> dict[str, Any]:
    orchestrator = _orchestrator()
    try:
        outcome = orchestrator.run(
            body.request,
            work_order=body.work_order,
            candidate_paths=body.candidate_paths or None,
        )
    except WorkOrderRejected as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return {
        "task_id": outcome.task_id,
        "classification": outcome.classification,
        "status": outcome.status,
        "result": outcome.result.model_dump(mode="json"),
    }


@app.get("/get_task_status/{task_id}")
def get_task_status(task_id: str) -> dict[str, Any]:
    row = _orchestrator().db.get_task(task_id)
    if row is None:
        raise HTTPException(status_code=404, detail=f"no task {task_id}")
    return row


@app.get("/get_task_result/{task_id}")
def get_task_result(task_id: str) -> dict[str, Any]:
    orchestrator = _orchestrator()
    row = orchestrator.db.get_task(task_id)
    if row is None:
        raise HTTPException(status_code=404, detail=f"no task {task_id}")
    return {"task": row, "model_calls": orchestrator.db.model_calls(task_id)}


@app.get("/usage")
def usage() -> dict[str, Any]:
    return _orchestrator().usage_summary()
