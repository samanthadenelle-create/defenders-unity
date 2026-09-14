"""RCA package (spec section 12).

After the autonomous retry budget is spent the orchestrator stops and assembles
everything the reasoner needs: original request, original work order, diff, test
failures, worker explanation, logs. It is written to ``.state/rca/`` so the
package survives the process and can be handed to the reasoner later - which
matters on this machine, where the reasoner has no key yet.
"""

from __future__ import annotations

import json
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

from .schemas.work_order import WorkOrder


@dataclass
class RcaPackage:
    task_id: str
    original_request: str
    work_order: dict[str, Any]
    diff: str = ""
    test_failures: list[str] = field(default_factory=list)
    worker_explanation: str = ""
    logs: list[dict[str, Any]] = field(default_factory=list)
    attempts: int = 0
    triggers: list[str] = field(default_factory=list)
    created_at: float = field(default_factory=time.time)
    path: str = ""

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def build_rca_package(
    *,
    task_id: str,
    request: str,
    work_order: WorkOrder | None,
    diff: str,
    test_failures: list[str],
    worker_explanation: str,
    logs: list[dict[str, Any]],
    attempts: int,
    triggers: list[str],
    state_dir: str | Path,
) -> RcaPackage:
    package = RcaPackage(
        task_id=task_id,
        original_request=request,
        work_order=work_order.model_dump(mode="json") if work_order else {},
        diff=diff,
        test_failures=test_failures,
        worker_explanation=worker_explanation,
        logs=logs,
        attempts=attempts,
        triggers=triggers,
    )
    out_dir = Path(state_dir) / "rca"
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{task_id}.json"
    out_path.write_text(
        json.dumps(package.to_dict(), indent=2, ensure_ascii=False), encoding="utf-8"
    )
    package.path = str(out_path)
    return package
