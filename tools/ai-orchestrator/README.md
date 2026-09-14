# AI Orchestrator (WO-1706, Phase 1 - worker half)

A local service where an expensive reasoning model decides **what** should be done and a
cheap local model executes **how**, inside explicit boundaries. Every request is classified
**before** any model call, so deterministic work never spends a token.

Design authority: the owner's spec, `Work Order_ Hybrid AI Reasoning + Task Execution
Orchestrator (1).md`. Repo ticket: `WorkOrders/WORK_ORDER_1706_*.md`.

## State: the worker half runs; the reasoner half is blocked

| Half | State |
|---|---|
| Local worker (Ollama) | **LIVE** - proven against `gpt-oss:20b` on this machine, 2026-09-14 |
| Reasoner (OpenAI) | **BLOCKED** - no `OPENAI_API_KEY` on this machine. The provider is real and config-driven; every call raises `ProviderConfigError` naming the variable. There is no stub, no canned text, and no silent downgrade to the local worker. |

## Setup

```powershell
python -m venv tools\ai-orchestrator\.venv
tools\ai-orchestrator\.venv\Scripts\python.exe -m pip install -r tools\ai-orchestrator\requirements.txt
ollama serve          # must be reachable at http://localhost:11434
```

## Use

```powershell
tools\ai-orchestrator\ai.cmd run "Add a module docstring to tools/x.py" --path tools/x.py --show-diff
tools\ai-orchestrator\ai.cmd run --work-order my-order.json
tools\ai-orchestrator\ai.cmd classify "run the tests"
tools\ai-orchestrator\ai.cmd usage
tools\ai-orchestrator\ai.cmd models
```

`ai.cmd` is a thin wrapper over `python -m ai_orchestrator`, using this folder's venv.

## Layout (spec section 3)

```
ai_orchestrator/
    providers/    openai_provider.py  ollama_provider.py  base.py
    router/       task_router.py      escalation.py
    schemas/      work_order.py       task_result.py      validation_result.py
    tools/        filesystem.py git_tools.py shell.py tests.py search.py base.py
    state/        database.py         audit_log.py
    orchestrator.py  deterministic.py  workspace.py  context_builder.py  rca.py
    cli.py  api.py
models.yaml       THE ONLY place a model name appears
tests/            pytest suite (offline; no test reaches a real model)
```

## The five classifications (spec section 6)

| Class | Who runs it |
|---|---|
| `DETERMINISTIC` | conventional code in `deterministic.py`. **No provider is constructed.** |
| `WORKER` | local model, inside a work order |
| `REASONING` | reasoner (blocked without a key) |
| `REASONING_THEN_WORKER` | reasoner plans, local model executes |
| `ESCALATION` | back to the reasoner with an RCA package |

## Safety

- The worker writes only through the fenced tool registry, only inside its temporary
  worktree, only under the work order's `allowed_paths`.
- Temporary worktrees live at `ai-worktrees/<WO>-<hex>/` and are removed + pruned in a
  `finally` block. Nothing accumulates.
- `git push` / merge / publish are registered as **requires_approval** and refuse to run
  autonomously (spec section 17). The orchestrator produces a diff; a human lands it.
- Commands must be allow-listed by the work order, and are run without `shell=True`.
- No secret ever lands in the tree: the key is read from the environment at call time.

## Cost control

Every model call is written to `model_calls` in `.state/orchestrator.sqlite3` with
provider, model, input tokens, output tokens, estimated cost and task id. `ai usage`
prints the report. Pricing per 1M tokens is authored in `models.yaml`; local models are 0.

## Tests

```powershell
tools\ai-orchestrator\.venv\Scripts\python.exe -m pytest tools\ai-orchestrator\tests -q
```

They run fully offline. `ExplodingProvider` raises on any use, which is how
"DETERMINISTIC never invokes a provider" is proven by execution rather than by reading
the classifier's return value.

## Known gaps

- The reasoner request/response mapping in `openai_provider.py` follows the Responses API
  shape and has **never been exercised against the live API**. First run with a real key
  must confirm it.
- `api.py` (FastAPI) is present per spec section 3 but is not on the Phase 1 acceptance
  path and has not been driven by a client here.
- Spec section 21's 20-task benchmark is not built. `worker_local_small`
  (`qwen2.5-coder:7b`) is configured as the second candidate for it.
