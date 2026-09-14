"""The engine: classify, plan, execute, validate, escalate (spec sections 2-13).

One pass through :meth:`Orchestrator.run` is the whole Phase 1 flow:

    request -> router -> [deterministic code | work order -> worker -> tests
    -> validator] -> PASS/DONE or FAIL/RCA

Invariants this file is responsible for, each pinned by a test:

* a DETERMINISTIC request never constructs a provider, let alone calls one;
* no work order reaches the worker unless :func:`parse_work_order` accepted it;
* the worker writes only through the fenced tool registry;
* the autonomous budget is ``1 + max_autonomous_retries`` attempts, after which
  an RCA package is produced instead of a third retry;
* every model call is recorded in the SQLite ledger with tokens and cost.
"""

from __future__ import annotations

import time
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .config import DEFAULT_STATE_DIR, OrchestratorConfig, load_config
from .context_builder import build_worker_context
from . import deterministic
from .providers.base import LLMProvider, ProviderConfigError, ProviderError, build_provider
from .rca import RcaPackage, build_rca_package
from .router.escalation import assess_escalation
from .router.task_router import Classification, RoutingDecision, TaskRouter
from .schemas.task_result import TaskResult, WorkerPatch
from .schemas.validation_result import ValidationCheck, ValidationResult
from .schemas.work_order import WorkOrder, WorkOrderRejected, parse_work_order
from .state.audit_log import AuditLog
from .state.database import Database, ModelCall, TaskRow, summarize_calls
from .tools import ToolContext, default_registry
from .workspace import Workspace, Worktree

WORKER_SYSTEM_PROMPT = (
    "You are the WORKER model in a hybrid orchestrator. A reasoning model has"
    " already decided WHAT must happen; you decide only HOW to carry out the"
    " approved work order inside its boundaries.\n"
    "RULES:\n"
    "- Never redefine requirements, architecture, business rules or acceptance"
    " criteria. If the work order is ambiguous or conflicts with itself, set"
    " needs_escalation=true and explain why instead of guessing.\n"
    "- Write ONLY paths listed under ALLOWED FILES.\n"
    "- For each file you change, return its COMPLETE new contents, not a diff"
    " and not a fragment.\n"
    "- Leave files you do not need to change out of the response entirely.\n"
    "- Respond with JSON only, matching the given schema."
)

PLANNER_SYSTEM_PROMPT = (
    "You turn a short mechanical request into a structured work order for a"
    " local worker model. Keep the scope minimal and concrete. allowed_paths"
    " must be repo-relative paths that already exist, taken from the CANDIDATE"
    " PATHS list you are given - never invent a path, never use an absolute"
    " path, never use '..'. Respond with JSON only."
)

_PLAN_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "title": {"type": "string"},
        "objective": {"type": "string"},
        "task_type": {
            "type": "string",
            "enum": [
                "code_change",
                "doc_change",
                "data_change",
                "test_generation",
                "localization",
                "refactor",
                "analysis",
            ],
        },
        "risk": {"type": "string", "enum": ["low", "medium", "high"]},
        "allowed_paths": {"type": "array", "items": {"type": "string"}},
        "requirements": {"type": "array", "items": {"type": "string"}},
        "implementation_steps": {"type": "array", "items": {"type": "string"}},
        "acceptance_criteria": {"type": "array", "items": {"type": "string"}},
    },
    "required": ["title", "objective", "task_type", "allowed_paths"],
}


@dataclass
class RunOutcome:
    """Everything the CLI needs to print the spec section 18 block."""

    task_id: str
    classification: str
    result: TaskResult
    routing_reason: str = ""
    worker_model: str = ""
    reasoner_model: str = ""
    work_order: WorkOrder | None = None
    validation: ValidationResult | None = None
    rca: RcaPackage | None = None
    deterministic_output: dict[str, Any] | None = None
    blocked_reason: str = ""
    duration_seconds: float = 0.0
    model_calls: list[dict[str, Any]] = field(default_factory=list)

    @property
    def status(self) -> str:
        if self.blocked_reason:
            return "BLOCKED"
        if self.rca is not None:
            return "FAILED_RCA"
        return "COMPLETE" if self.result.status == "completed" else self.result.status.upper()


class Orchestrator:
    def __init__(
        self,
        repo_root: str | Path,
        config: OrchestratorConfig | None = None,
        *,
        database: Database | None = None,
        state_dir: str | Path | None = None,
        provider_factory: Any = None,
    ) -> None:
        self.repo_root = Path(repo_root).resolve()
        self.config = config or load_config()
        self.state_dir = Path(state_dir) if state_dir else DEFAULT_STATE_DIR
        self.state_dir.mkdir(parents=True, exist_ok=True)
        self.db = database or Database(self.state_dir / "orchestrator.sqlite3")
        self.audit = AuditLog(self.db, self.state_dir / "audit.jsonl")
        self.router = TaskRouter()
        # Injection seam: tests substitute a provider that raises on any use, or
        # one that returns a scripted failure, without touching this file.
        self._provider_factory = provider_factory or build_provider
        self._providers: dict[str, LLMProvider] = {}

    # -- provider plumbing -------------------------------------------------
    def provider(self, role: str) -> LLMProvider:
        if role not in self._providers:
            self._providers[role] = self._provider_factory(self.config, role)
        return self._providers[role]

    def _call_model(
        self,
        role: str,
        task_id: str,
        purpose: str,
        method: str,
        *args: Any,
        **kwargs: Any,
    ) -> Any:
        """Invoke a provider and ALWAYS record the call (spec section 13)."""
        provider = self.provider(role)
        started = time.time()
        try:
            response = getattr(provider, method)(*args, **kwargs)
        except Exception:
            self.audit.record(task_id, "model_call", f"{role}:{purpose}", False, "raised")
            raise
        duration = time.time() - started
        self.db.record_model_call(
            ModelCall(
                task_id=task_id,
                role=role,
                provider=provider.name,
                model=provider.model,
                purpose=purpose,
                input_tokens=response.input_tokens,
                output_tokens=response.output_tokens,
                estimated_cost_usd=provider.estimate_cost(
                    response.input_tokens, response.output_tokens
                ),
                duration_seconds=duration,
            )
        )
        self.audit.record(
            task_id,
            "model_call",
            f"{role}:{purpose}",
            True,
            f"{provider.name}/{provider.model} in={response.input_tokens}"
            f" out={response.output_tokens} {duration:.1f}s",
        )
        return response

    # -- public entry point ------------------------------------------------
    def run(
        self,
        request: str,
        *,
        work_order: WorkOrder | dict[str, Any] | str | None = None,
        worker_role: str = "worker",
        reasoner_role: str = "reasoner",
        candidate_paths: list[str] | None = None,
        deterministic_kwargs: dict[str, Any] | None = None,
        keep_worktree: bool = False,
    ) -> RunOutcome:
        started = time.time()
        task_id = f"WO-{uuid.uuid4().hex[:12]}"

        decision = (
            RoutingDecision(
                Classification.WORKER,
                "explicit work order supplied; planning is already done",
            )
            if work_order is not None
            else self.router.classify(request)
        )

        self.db.create_task(
            TaskRow(
                task_id=task_id,
                request=request,
                classification=decision.classification.value,
                state="READY",
            )
        )
        self.audit.record(
            task_id, "route", decision.classification.value, True, decision.reason
        )

        outcome = RunOutcome(
            task_id=task_id,
            classification=decision.classification.value,
            result=TaskResult(work_order_id=task_id, classification=decision.classification.value),
            routing_reason=decision.reason,
        )

        try:
            if decision.classification is Classification.DETERMINISTIC:
                self._run_deterministic(outcome, decision, deterministic_kwargs or {})
            elif decision.classification.needs_reasoner:
                self._run_needs_reasoner(outcome, decision, reasoner_role)
            else:
                self._run_worker(
                    outcome,
                    request,
                    work_order,
                    worker_role,
                    candidate_paths,
                    keep_worktree,
                )
        except ProviderConfigError as exc:
            # Loud, never silent: a missing key stops the run and says so.
            outcome.blocked_reason = str(exc)
            outcome.result.status = "blocked"
            outcome.result.exceptions.append(str(exc))
            self.db.set_state(task_id, "RCA", failure_reason=str(exc))
            self.audit.record(task_id, "blocked", "provider_config", False, str(exc))
        except (ProviderError, WorkOrderRejected) as exc:
            outcome.result.status = "failed"
            outcome.result.exceptions.append(f"{type(exc).__name__}: {exc}")
            self.db.set_state(task_id, "RCA", failure_reason=str(exc))
            self.audit.record(task_id, "failed", type(exc).__name__, False, str(exc))

        outcome.duration_seconds = time.time() - started
        outcome.model_calls = self.db.model_calls(task_id)
        outcome.result.worker_calls = sum(
            1 for c in outcome.model_calls if c["role"] == worker_role
        )
        outcome.result.reasoner_calls = sum(
            1 for c in outcome.model_calls if c["role"] == reasoner_role
        )
        self.db.set_state(
            task_id,
            self.db.get_task(task_id)["state"],
            duration_seconds=outcome.duration_seconds,
        )
        return outcome

    # -- branches ----------------------------------------------------------
    def _run_deterministic(
        self,
        outcome: RunOutcome,
        decision: RoutingDecision,
        kwargs: dict[str, Any],
    ) -> None:
        """No provider is constructed here. That is the point."""
        self.db.set_state(outcome.task_id, "EXECUTING")
        operation = decision.operation or "git_status"
        if operation == "build_report":
            kwargs = {**kwargs, "database": self.db}
        output = deterministic.execute(
            operation, self.repo_root, outcome.result.summary, **kwargs
        )
        outcome.deterministic_output = output
        outcome.result.status = "completed"
        outcome.result.summary = f"deterministic operation '{operation}' completed without a model call"
        outcome.validation = ValidationResult.from_checks(
            [ValidationCheck(name="deterministic", passed=True, detail=operation)]
        )
        self.audit.record(outcome.task_id, "deterministic", operation, True, str(output)[:2000])
        self.db.set_state(outcome.task_id, "DONE")

    def _run_needs_reasoner(
        self, outcome: RunOutcome, decision: RoutingDecision, reasoner_role: str
    ) -> None:
        """Stop, loudly, because Phase 1 has no reasoner.

        The key check is made against CONFIG, never by firing a probe request at
        the provider. An earlier draft called ``provider.generate("ping")`` here:
        once a key exists that would have made a real, paid, unlogged call whose
        answer was thrown away - the exact "expensive model used because it is
        available" that spec section 13 forbids.
        """
        self.db.set_state(outcome.task_id, "REASONING")
        provider = self.provider(reasoner_role)
        outcome.reasoner_model = provider.model
        settings = self.config.provider(self.config.role(reasoner_role).provider)
        if settings.api_key() is None:
            var = settings.api_key_env or "the provider's API key variable"
            detail = (
                f"the '{reasoner_role}' role needs provider '{settings.name}' model"
                f" '{provider.model}', but environment variable {var} is not set."
                " Set it in the environment (never in a repo file) and re-run."
                " The reasoner half of WO-1706 is blocked until then."
            )
        else:
            detail = (
                "the reasoner is configured and its key is present, but Phase 1 does"
                " not yet implement the reasoning->work-order handoff"
                " (WO-1706 section 4)."
            )
        raise ProviderConfigError(
            f"classification {decision.classification.value} requires the"
            f" '{reasoner_role}' role, and it cannot run: " + detail
        )

    def _run_worker(
        self,
        outcome: RunOutcome,
        request: str,
        supplied_work_order: WorkOrder | dict[str, Any] | str | None,
        worker_role: str,
        candidate_paths: list[str] | None,
        keep_worktree: bool,
    ) -> None:
        task_id = outcome.task_id
        worker = self.provider(worker_role)
        outcome.worker_model = worker.model
        self.db.set_state(task_id, "REASONING", worker_model=worker.model)

        # --- PLAN -> WorkOrder, always through the schema gate -------------
        if supplied_work_order is None:
            work_order = self._plan_with_worker(task_id, request, worker_role, candidate_paths)
        elif isinstance(supplied_work_order, WorkOrder):
            work_order = supplied_work_order
        else:
            work_order = parse_work_order(supplied_work_order)
        outcome.work_order = work_order
        outcome.result.work_order_id = work_order.work_order_id
        self.db.set_state(task_id, "PLANNED")
        self.audit.record(task_id, "plan", work_order.work_order_id, True, work_order.title)

        # --- EXECUTE in an isolated worktree --------------------------------
        worktree = Worktree.create(
            self.repo_root,
            work_order.work_order_id,
            sparse_paths=sorted({p.split("/")[0] for p in work_order.allowed_paths}) or None,
        )
        try:
            self._execute_in_worktree(outcome, request, work_order, worktree, worker_role)
            outcome.result.diff = worktree.diff()
        finally:
            if keep_worktree:
                self.audit.record(task_id, "worktree", "kept", True, str(worktree.path))
            else:
                worktree.remove()
                self.audit.record(task_id, "worktree", "removed", True, str(worktree.path))

    def _plan_with_worker(
        self,
        task_id: str,
        request: str,
        worker_role: str,
        candidate_paths: list[str] | None,
    ) -> WorkOrder:
        """Draft a work order locally, then validate it like any other.

        The REASONING_THEN_WORKER path uses the reasoner for this. For a request
        the router already judged WORKER-simple, spending a reasoning call to
        write four bullet points is exactly the waste spec section 13 forbids.
        """
        paths = candidate_paths or []
        prompt = (
            f"REQUEST\n{request}\n\n"
            "CANDIDATE PATHS (allowed_paths must be chosen from this list)\n"
            + ("\n".join(f"- {p}" for p in paths) if paths else "- (none supplied)")
            + "\n\nProduce the work order JSON."
        )
        response = self._call_model(
            worker_role,
            task_id,
            "plan",
            "generate_structured",
            prompt,
            _PLAN_SCHEMA,
            system=PLANNER_SYSTEM_PROMPT,
        )
        payload = dict(response.data or {})
        payload["work_order_id"] = task_id
        if paths:
            # The worker proposes; the orchestrator constrains. Anything outside
            # the candidate list is dropped before validation, not after.
            proposed = [p for p in payload.get("allowed_paths", []) if p in paths]
            payload["allowed_paths"] = proposed or paths
        return parse_work_order(payload)

    def _execute_in_worktree(
        self,
        outcome: RunOutcome,
        request: str,
        work_order: WorkOrder,
        worktree: Worktree,
        worker_role: str,
    ) -> None:
        task_id = outcome.task_id
        workspace = Workspace(root=worktree.path, work_order=work_order)
        context = ToolContext(
            workspace=workspace,
            audit=self.audit,
            task_id=task_id,
            commands_allowed=list(work_order.commands_allowed),
        )
        registry = default_registry(context)

        max_retries = self.config.max_autonomous_retries
        budget = 1 + max_retries
        recent_errors = ""
        last_patch: WorkerPatch | None = None
        scope_violations: list[str] = []
        attempts = 0

        while attempts < budget:
            attempts += 1
            outcome.result.attempts = attempts
            self.db.set_state(task_id, "EXECUTING", retry_count=attempts - 1)

            briefing = build_worker_context(
                worktree.path,
                work_order,
                repo_root=self.repo_root,
                tool_names=registry.names(),
                recent_errors=recent_errors,
            )
            response = self._call_model(
                worker_role,
                task_id,
                f"execute:attempt{attempts}",
                "generate_structured",
                briefing,
                WorkerPatch.json_schema_for_provider(),
                system=WORKER_SYSTEM_PROMPT,
            )
            patch = WorkerPatch.model_validate(response.data or {})
            last_patch = patch

            if patch.needs_escalation:
                recent_errors = f"worker escalated: {patch.escalation_reason}"
                self.audit.record(
                    task_id, "escalation", "worker_requested", False, patch.escalation_reason
                )
                break

            # --- apply, through the fenced registry only -------------------
            changed: list[str] = []
            failures: list[str] = []
            scope_violations = []
            for edit in patch.files:
                result = registry.call("write_file", {"path": edit.path, "content": edit.content})
                if not result.ok:
                    failures.append(f"{edit.path}: {result.error}")
                    if "fence" in result.error or "not covered" in result.error:
                        scope_violations.append(edit.path)
                elif result.output and result.output.get("changed"):
                    changed.append(edit.path)
            outcome.result.files_changed = changed
            outcome.result.summary = patch.summary

            # --- tests ------------------------------------------------------
            self.db.set_state(task_id, "VALIDATING")
            passed: list[str] = []
            failed: list[str] = []
            for command in work_order.tests_required:
                test = registry.call("run_test", {"command": command})
                if test.ok and test.output and test.output.get("passed"):
                    passed.append(command)
                else:
                    detail = (test.error or (test.output or {}).get("output", ""))[-2000:]
                    failed.append(f"{command}: {detail}")
            outcome.result.tests_run = list(work_order.tests_required)
            outcome.result.tests_passed = passed
            outcome.result.tests_failed = failed
            outcome.result.commands_executed = list(context.executed_commands)

            validation = self._validate(work_order, changed, failures, failed, scope_violations)
            outcome.validation = validation
            if validation.passed:
                outcome.result.status = "completed"
                outcome.result.needs_escalation = False
                self.db.set_state(
                    task_id, "DONE", files_changed=changed, tests_run=outcome.result.tests_run
                )
                return

            recent_errors = validation.failure_reason
            self.audit.record(task_id, "attempt_failed", f"attempt{attempts}", False, recent_errors)

        # --- budget spent: RCA, never a third retry -------------------------
        escalation = assess_escalation(
            attempts=attempts,
            max_retries=max_retries,
            worker_requested=bool(last_patch and last_patch.needs_escalation),
            worker_reason=last_patch.escalation_reason if last_patch else "",
            files_touched=len(outcome.result.files_changed),
            max_files=self.config.max_files_per_work_order,
            scope_violations=scope_violations,
            tests_failed=outcome.result.tests_failed,
            exceptions=outcome.result.exceptions,
        )
        outcome.result.status = "failed"
        outcome.result.needs_escalation = True
        outcome.rca = build_rca_package(
            task_id=task_id,
            request=request,
            work_order=work_order,
            diff=worktree.diff(),
            test_failures=outcome.result.tests_failed,
            worker_explanation=(last_patch.summary if last_patch else "")
            + (f" | escalation: {last_patch.escalation_reason}" if last_patch and last_patch.escalation_reason else ""),
            logs=self.db.events(task_id),
            attempts=attempts,
            triggers=escalation.triggers,
            state_dir=self.state_dir,
        )
        self.db.set_state(
            task_id,
            "RCA",
            retry_count=max(0, attempts - 1),
            failure_reason=(outcome.validation.failure_reason if outcome.validation else escalation.detail),
        )
        self.audit.record(task_id, "rca", "package", False, outcome.rca.path)

    def _validate(
        self,
        work_order: WorkOrder,
        changed: list[str],
        write_failures: list[str],
        test_failures: list[str],
        scope_violations: list[str],
    ) -> ValidationResult:
        checks = [
            ValidationCheck(
                name="files_written",
                passed=not write_failures,
                detail="; ".join(write_failures),
            ),
            ValidationCheck(
                name="scope",
                passed=not scope_violations,
                detail="; ".join(scope_violations),
            ),
            ValidationCheck(
                name="tests",
                passed=not test_failures,
                detail="; ".join(test_failures)[:2000],
            ),
            ValidationCheck(
                name="produced_changes",
                passed=bool(changed) or work_order.task_type.value == "analysis",
                detail="worker returned no file changes" if not changed else "",
            ),
            ValidationCheck(
                name="file_count",
                passed=len(changed) <= self.config.max_files_per_work_order,
                detail=f"{len(changed)} files changed",
            ),
        ]
        return ValidationResult.from_checks(checks)

    # -- reporting ---------------------------------------------------------
    def usage_summary(self, task_id: str | None = None) -> dict[str, Any]:
        calls = self.db.model_calls(task_id)
        count, inp, out, cost = summarize_calls(calls)
        return {
            "calls": count,
            "input_tokens": inp,
            "output_tokens": out,
            "estimated_cost_usd": cost,
            "by_model": self.db.usage_report(),
        }
