"""`ai run "<request>"` and friends (spec section 18).

    python -m ai_orchestrator run "Add a module docstring to tools/gate_brace.py"
    python -m ai_orchestrator run --work-order wo.json
    python -m ai_orchestrator classify "run the tests"
    python -m ai_orchestrator usage
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .config import ConfigError, load_config
from .orchestrator import Orchestrator, RunOutcome
from .router.task_router import TaskRouter
from .schemas.work_order import WorkOrderRejected


def _repo_root(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit).resolve()
    here = Path(__file__).resolve()
    for parent in here.parents:
        if (parent / ".git").exists():
            return parent
    return Path.cwd()


def _dots(label: str, width: int = 15) -> str:
    return (label + "." * width)[:width]


def format_summary(outcome: RunOutcome) -> str:
    """The spec section 18 block, verbatim in shape."""
    result = outcome.result
    lines = [
        f"Task: {result.work_order_id or outcome.task_id}",
        f"Classification: {outcome.classification}",
        f"Reasoner: {outcome.reasoner_model or '(not used)'}",
        f"Worker: {outcome.worker_model or '(not used)'}",
        "",
    ]

    if outcome.blocked_reason:
        lines += [f"{_dots('Planning')} BLOCKED", "", f"Reason: {outcome.blocked_reason}"]
    else:
        plan_state = "PASS" if outcome.work_order or outcome.deterministic_output else "FAIL"
        exec_state = (
            "PASS"
            if result.status == "completed"
            else ("SKIP" if outcome.deterministic_output and False else "FAIL")
        )
        tests_total = len(result.tests_run)
        tests_line = (
            f"{len(result.tests_passed)}/{tests_total} "
            + ("PASS" if tests_total and not result.tests_failed else "FAIL")
            if tests_total
            else "none defined"
        )
        validation_state = (
            "PASS" if outcome.validation and outcome.validation.passed else "FAIL"
        )
        lines += [
            f"{_dots('Planning')} {plan_state}",
            f"{_dots('Execution')} {exec_state}",
            f"{_dots('Tests')} {tests_line}",
            f"{_dots('Validation')} {validation_state}",
        ]

    calls = outcome.model_calls
    openai_calls = sum(1 for c in calls if c["provider"] == "openai")
    worker_calls = sum(1 for c in calls if c["provider"] != "openai")
    tokens_in = sum(int(c["input_tokens"] or 0) for c in calls)
    tokens_out = sum(int(c["output_tokens"] or 0) for c in calls)
    cost = sum(float(c["estimated_cost_usd"] or 0.0) for c in calls)

    lines += [
        "",
        f"Files Changed: {len(outcome.result.files_changed)}",
        f"OpenAI Calls: {openai_calls}",
        f"Worker Calls: {worker_calls}",
        f"Tokens: {tokens_in} in / {tokens_out} out",
        f"Estimated Cost: ${cost:.4f}",
        f"Duration: {outcome.duration_seconds:.1f}s",
        f"Status: {outcome.status}",
    ]
    if outcome.rca is not None:
        lines.append(f"RCA Package: {outcome.rca.path}")
    if outcome.routing_reason:
        lines.append(f"Routing: {outcome.routing_reason}")
    return "\n".join(lines)


def _cmd_run(args: argparse.Namespace) -> int:
    root = _repo_root(args.repo_root)
    orchestrator = Orchestrator(root, load_config(args.config), state_dir=args.state_dir)

    supplied = None
    if args.work_order:
        supplied = Path(args.work_order).read_text(encoding="utf-8")
    request = args.request or (f"execute work order {args.work_order}" if supplied else "")
    if not request and not supplied:
        print("nothing to do: pass a request or --work-order", file=sys.stderr)
        return 2

    try:
        outcome = orchestrator.run(
            request,
            work_order=supplied,
            candidate_paths=args.path or None,
            keep_worktree=args.keep_worktree,
        )
    except WorkOrderRejected as exc:
        print(f"WORK ORDER REJECTED: {exc}", file=sys.stderr)
        return 3

    print(format_summary(outcome))
    if args.show_diff and outcome.result.diff:
        print("\n--- DIFF ---")
        print(outcome.result.diff)
    if args.json:
        print("\n--- JSON ---")
        print(json.dumps(outcome.result.model_dump(mode="json"), indent=2))
    if outcome.deterministic_output:
        print("\n--- DETERMINISTIC OUTPUT ---")
        print(json.dumps(outcome.deterministic_output, indent=2)[:8000])

    return 0 if outcome.status == "COMPLETE" else 1


def _cmd_classify(args: argparse.Namespace) -> int:
    decision = TaskRouter().classify(args.request)
    print(f"{decision.classification.value}: {decision.reason}")
    if decision.operation:
        print(f"deterministic operation: {decision.operation}")
    return 0


def _cmd_usage(args: argparse.Namespace) -> int:
    orchestrator = Orchestrator(
        _repo_root(args.repo_root), load_config(args.config), state_dir=args.state_dir
    )
    print(json.dumps(orchestrator.usage_summary(args.task_id), indent=2))
    return 0


def _cmd_models(args: argparse.Namespace) -> int:
    config = load_config(args.config)
    print(f"config: {config.path}")
    for name, role in config.roles.items():
        print(f"  {name:20s} {role.provider}/{role.model}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="ai", description="Hybrid AI orchestrator (WO-1706)")
    parser.add_argument("--config", default=None, help="path to models.yaml")
    parser.add_argument("--repo-root", default=None)
    parser.add_argument("--state-dir", default=None)
    sub = parser.add_subparsers(dest="command", required=True)

    run = sub.add_parser("run", help="classify and execute a request")
    run.add_argument("request", nargs="?", default="")
    run.add_argument("--work-order", default=None, help="path to a work-order JSON file")
    run.add_argument("--path", action="append", help="candidate repo-relative path (repeatable)")
    run.add_argument("--show-diff", action="store_true")
    run.add_argument("--json", action="store_true")
    run.add_argument("--keep-worktree", action="store_true")
    run.set_defaults(func=_cmd_run)

    classify = sub.add_parser("classify", help="classify without executing")
    classify.add_argument("request")
    classify.set_defaults(func=_cmd_classify)

    usage = sub.add_parser("usage", help="model-call ledger report")
    usage.add_argument("--task-id", default=None)
    usage.set_defaults(func=_cmd_usage)

    models = sub.add_parser("models", help="show the configured roles")
    models.set_defaults(func=_cmd_models)
    return parser


def main(argv: list[str] | None = None) -> int:
    # A diff out of this repo carries non-cp1252 characters; the default Windows
    # console codec raises UnicodeEncodeError on them AFTER the work is done,
    # which would turn a successful run into a traceback.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):  # pragma: no cover - non-tty sinks
            pass
    args = build_parser().parse_args(argv)
    try:
        return int(args.func(args))
    except ConfigError as exc:
        print(f"CONFIG ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
