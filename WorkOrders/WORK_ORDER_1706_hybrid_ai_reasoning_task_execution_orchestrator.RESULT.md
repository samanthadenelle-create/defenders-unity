# WO-1706 RESULT - Hybrid AI Reasoning + Task Execution Orchestrator (Phase 1, worker half)

**Lane:** implementation lane, 2026-09-14
**Outcome:** worker half BUILT and PROVEN on this machine. Reasoner half present, config-driven,
BLOCKED on `OPENAI_API_KEY` exactly as the ticket scoped it.
**Everything below is output captured this session.** No claim here rests on a doc or a memory.

---

## 0. ⚠ READ FIRST - A SECOND ORCHESTRATOR ALREADY EXISTS IN THIS TREE, UNTRACKED

`tools/hybrid-orchestrator/` is an **untracked, working, previously-run implementation of this same
ticket**, and the ticket does not mention it. The lead must pick one before anything is committed, or
the repo carries two orchestrators.

Measured, not inferred:

```
$ git status --short tools/hybrid-orchestrator
?? tools/hybrid-orchestrator/

$ git log --oneline -3 -- tools/hybrid-orchestrator
(no output - never committed)

$ find tools/hybrid-orchestrator -type f -not -path '*/.venv/*' | wc -l
247

$ ls -la --time-style=full-iso tools/hybrid-orchestrator/hybrid/*.py
-rw-r--r-- 1 Elden 197609    50 2026-09-13 17:59:15 hybrid/__init__.py
-rw-r--r-- 1 Elden 197609  3672 2026-09-13 18:08:03 hybrid/api.py
-rw-r--r-- 1 Elden 197609  5619 2026-09-13 18:24:29 hybrid/cli.py
-rw-r--r-- 1 Elden 197609   583 2026-09-13 17:59:15 hybrid/config.py
-rw-r--r-- 1 Elden 197609  2876 2026-09-13 18:20:07 hybrid/context.py
-rw-r--r-- 1 Elden 197609 17990 2026-09-13 18:17:39 hybrid/engine.py
-rw-r--r-- 1 Elden 197609  3331 2026-09-13 18:24:29 hybrid/helpers.py
-rw-r--r-- 1 Elden 197609  1001 2026-09-13 18:08:03 hybrid/lock.py
-rw-r--r-- 1 Elden 197609 10021 2026-09-13 18:24:29 hybrid/providers.py
-rw-r--r-- 1 Elden 197609  1021 2026-09-13 18:01:18 hybrid/router.py
-rw-r--r-- 1 Elden 197609  5337 2026-09-13 18:24:29 hybrid/schemas.py
-rw-r--r-- 1 Elden 197609  3485 2026-09-13 17:59:15 hybrid/state.py
-rw-r--r-- 1 Elden 197609  7744 2026-09-13 18:17:39 hybrid/tools.py
-rw-r--r-- 1 Elden 197609  6484 2026-09-13 18:14:28 hybrid/workspace.py
```

It has the same module shape (providers / router / schemas / tools / state / workspace), its own
`.venv`, a pytest suite (`test_flow.py`, `test_rca.py`, `test_safety.py`), a `benchmarks/` folder
with `cases.json`, `models/Modelfile.*`, `shell/` shortcuts, `config.local.yaml`, and `.state/`
directories from **five completed demo runs** (`WO-90955425…`, `WO-965228f8…`, `WO-bff847ed…`,
`WO-ea78c3a5…`, `WO-fe190eb2…`, each with a `changes.patch`) - which reads like the spec section 22
"five representative tasks" criterion having been demonstrated already.

**I did not read past its file list, did not run its tests** (that would write into its `.state/` and
`.pytest_cache/`), **and did not touch a single byte of it.** My lane was scoped to
`tools/ai-orchestrator/` and I stayed inside it. The lead owns the merge/choose decision.

---

## 1. Files created (all under `tools/ai-orchestrator/`, nothing else touched)

```
tools/ai-orchestrator/.gitignore                     (.venv/ .state/ __pycache__/ *.pyc .pytest_cache/ *.sqlite3)
tools/ai-orchestrator/README.md
tools/ai-orchestrator/requirements.txt
tools/ai-orchestrator/models.yaml                    <- the ONLY place a model name appears
tools/ai-orchestrator/ai.cmd                         <- `ai run "..."` entry point
tools/ai-orchestrator/ai_orchestrator/__init__.py
tools/ai-orchestrator/ai_orchestrator/__main__.py
tools/ai-orchestrator/ai_orchestrator/api.py         <- FastAPI, spec s3/s16 MODE B names
tools/ai-orchestrator/ai_orchestrator/cli.py
tools/ai-orchestrator/ai_orchestrator/config.py
tools/ai-orchestrator/ai_orchestrator/context_builder.py
tools/ai-orchestrator/ai_orchestrator/deterministic.py
tools/ai-orchestrator/ai_orchestrator/orchestrator.py
tools/ai-orchestrator/ai_orchestrator/rca.py
tools/ai-orchestrator/ai_orchestrator/workspace.py
tools/ai-orchestrator/ai_orchestrator/providers/{__init__,base,ollama_provider,openai_provider}.py
tools/ai-orchestrator/ai_orchestrator/router/{__init__,task_router,escalation}.py
tools/ai-orchestrator/ai_orchestrator/schemas/{__init__,work_order,task_result,validation_result}.py
tools/ai-orchestrator/ai_orchestrator/state/{__init__,database,audit_log}.py
tools/ai-orchestrator/ai_orchestrator/tools/{__init__,base,filesystem,git_tools,search,shell,tests}.py
tools/ai-orchestrator/tests/{conftest,test_router,test_work_order_schema,test_workspace_fence,test_retry_cap,test_providers_and_config}.py
```

**42 committable files**, counted against `.gitignore` rather than by eye:

```
$ git ls-files --others --exclude-standard tools/ai-orchestrator | wc -l
42
$ git ls-files --others --exclude-standard tools/ai-orchestrator | grep -c pytest_cache
0
```

No `.cs`, no `Assets/`, no `api/`, no `.claude/`, no scene, no secret. No Unity run, no gate run, and
**I** ran no `git commit/add/stash/checkout/reset` — see the one disclosure in section 5 about the git
commands the orchestrator itself runs inside its throwaway worktree.

## 2. Packages installed (into `tools/ai-orchestrator/.venv`, gitignored, 49 MB)

Requested in `requirements.txt`: `pydantic>=2.9`, `PyYAML>=6.0`, `httpx>=0.27`, `fastapi>=0.115`,
`pytest>=8.0`. pip's own report of what landed, verbatim:

```
Successfully installed PyYAML-6.0.3 annotated-doc-0.0.5 annotated-types-0.8.0 anyio-4.15.1
certifi-2026.7.22 colorama-0.4.6 fastapi-0.141.1 h11-0.16.0 httpcore-1.0.9 httpx-0.28.1 idna-3.19
iniconfig-2.3.0 packaging-26.3 pluggy-1.6.0 pydantic-2.13.5 pydantic-core-2.46.5 pygments-2.21.0
pytest-9.1.1 starlette-1.6.0 typing-extensions-4.16.0 typing-inspection-0.4.4
```

Nothing was installed outside that venv. Python is 3.14.0.

## 3. Ollama, proven not assumed

`ollama list` on this machine: `qwen2.5-coder:7b` (4.7 GB), `llama3.2:3b` (2.0 GB),
`gpt-oss:20b` (13 GB).

I chose the **native `/api/chat`** over the OpenAI-compatible `/v1` shim and proved it before
designing around it - native returns the token counts spec section 13 requires and accepts a raw JSON
Schema in `format`:

```
$ curl -s -m 300 http://localhost:11434/api/chat -d '{"model":"gpt-oss:20b", ... ,"think":"low",
   "format":{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}}'
{"model":"gpt-oss:20b","created_at":"2026-09-14T08:10:55.0677512Z",
 "message":{"role":"assistant","content":"{\"ok\":true}","thinking":"Need to output JSON."},
 "done":true,"done_reason":"stop","total_duration":34623294100,"load_duration":29788474000,
 "prompt_eval_count":76,"prompt_eval_duration":1009211000,"eval_count":21,"eval_duration":3725879000}
HTTP=200 TIME=34.834777
```

That one call proved three things at once: the 13 GB model loads (29.8 s of the 34.8 s was load),
structured output is honoured, and `prompt_eval_count`/`eval_count` are the token source.
`"think":"low"` is set in `models.yaml`, not in code - without it gpt-oss burns minutes reasoning.

## 4. Test output, verbatim

`tools\ai-orchestrator\.venv\Scripts\python.exe -m pytest tests -v --no-header` (PASSED suffixes
trimmed to keep the table readable; the pass/fail verdict is unedited):

```
============================= test session starts =============================
collecting ... collected 63 items

tests/test_providers_and_config.py::test_roles_resolve_through_config PASSED
tests/test_providers_and_config.py::test_no_model_string_is_hard_coded_in_code PASSED
tests/test_providers_and_config.py::test_provider_interfaces_are_interchangeable PASSED
tests/test_providers_and_config.py::test_openai_provider_fails_loudly_without_a_key PASSED
tests/test_providers_and_config.py::test_reasoning_request_is_blocked_not_silently_downgraded PASSED
tests/test_providers_and_config.py::test_reasoning_path_never_fires_a_probe_call_even_with_a_key PASSED
tests/test_providers_and_config.py::test_cost_estimation_uses_config_pricing PASSED
tests/test_providers_and_config.py::test_no_secret_literal_in_the_tree PASSED
tests/test_retry_cap.py::test_worker_gets_exactly_three_attempts_then_rca PASSED
tests/test_retry_cap.py::test_worktree_is_cleaned_up_after_a_failed_run PASSED
tests/test_retry_cap.py::test_a_successful_first_attempt_does_not_retry PASSED
tests/test_retry_cap.py::test_escalation_assessment_names_the_spec_triggers PASSED
tests/test_router.py::test_five_categories[run the tests for the inventory module-DETERMINISTIC] PASSED
tests/test_router.py::test_five_categories[show me the diff-DETERMINISTIC] PASSED
tests/test_router.py::test_five_categories[count the files under Assets/Resources-DETERMINISTIC] PASSED
tests/test_router.py::test_five_categories[compare the hashes of the two catalogs-DETERMINISTIC] PASSED
tests/test_router.py::test_five_categories[add a docstring to the loader-WORKER] PASSED
tests/test_router.py::test_five_categories[generate unit tests for the parser-WORKER] PASSED
tests/test_router.py::test_five_categories[localize the store copy into Spanish-WORKER] PASSED
tests/test_router.py::test_five_categories[decide whether the economy should use a shared bank-REASONING] PASSED
tests/test_router.py::test_five_categories[do an RCA on the raid reward failure-REASONING] PASSED
tests/test_router.py::test_five_categories[what is the right architecture for offline saves-REASONING] PASSED
tests/test_router.py::test_five_categories[implement multi-currency raid rewards-REASONING_THEN_WORKER] PASSED
tests/test_router.py::test_five_categories[fix the bug where pets never despawn-REASONING_THEN_WORKER] PASSED
tests/test_router.py::test_five_categories[the worker failed twice on this, escalate it-ESCALATION] PASSED
tests/test_router.py::test_all_five_categories_are_reachable PASSED
tests/test_router.py::test_deterministic_flag_is_the_only_no_model_class PASSED
tests/test_router.py::test_judgement_words_defeat_a_deterministic_keyword PASSED
tests/test_router.py::test_unknown_request_defaults_to_reasoning_not_worker PASSED
tests/test_router.py::test_deterministic_run_never_invokes_a_provider PASSED
tests/test_work_order_schema.py::test_the_spec_example_validates PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation0-id shape] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation1-empty title] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation2-empty objective] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation3-unknown task type] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation4-unknown risk] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation5-no allowed paths] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation6-absolute posix path] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation7-absolute windows path] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation8-parent escape] PASSED
tests/test_work_order_schema.py::test_invalid_work_orders_are_rejected[mutation9-unknown field] PASSED
tests/test_work_order_schema.py::test_missing_required_field_is_rejected PASSED
tests/test_work_order_schema.py::test_non_json_payload_is_rejected PASSED
tests/test_work_order_schema.py::test_permits_honours_allowed_and_prohibited PASSED
tests/test_work_order_schema.py::test_invalid_work_order_never_reaches_the_worker PASSED
tests/test_workspace_fence.py::test_inside_path_is_allowed PASSED
tests/test_workspace_fence.py::test_escapes_are_refused[../outside.txt] PASSED
tests/test_workspace_fence.py::test_escapes_are_refused[../../outside.txt] PASSED
tests/test_workspace_fence.py::test_escapes_are_refused[docs/../../outside.txt] PASSED
tests/test_workspace_fence.py::test_escapes_are_refused[..\\..\\outside.txt] PASSED
tests/test_workspace_fence.py::test_escapes_are_refused[C:/Windows/System32/drivers/etc/hosts] PASSED
tests/test_workspace_fence.py::test_escapes_are_refused[/etc/passwd] PASSED
tests/test_workspace_fence.py::test_escapes_are_refused[Z:/somewhere/else.txt] PASSED
tests/test_workspace_fence.py::test_sibling_with_shared_prefix_is_refused PASSED
tests/test_workspace_fence.py::test_prohibited_path_inside_the_root_is_refused PASSED
tests/test_workspace_fence.py::test_path_outside_allowed_paths_is_refused PASSED
tests/test_workspace_fence.py::test_case_insensitive_root_still_matches PASSED
tests/test_workspace_fence.py::test_write_file_tool_refuses_an_escape PASSED
tests/test_workspace_fence.py::test_write_file_tool_writes_inside PASSED
tests/test_workspace_fence.py::test_unknown_tool_is_refused PASSED
tests/test_workspace_fence.py::test_destructive_tool_requires_human_approval PASSED
tests/test_workspace_fence.py::test_command_not_in_allow_list_is_refused PASSED
tests/test_workspace_fence.py::test_every_tool_call_is_recorded PASSED

============================= 63 passed in 3.57s ==============================
```

(The `-q` form reads `63 passed in 2.48s`. The suite runs fully offline — no test reaches a model.)

**A defect this suite found in my own code, worth recording.** `_run_needs_reasoner` originally worked
out *why* the reasoner could not run by calling `provider.generate("ping")`. With no key that is
harmless. With a key it would have been a **real, paid, unlogged call whose answer was thrown away** —
precisely the "expensive model used simply because it is available" that spec section 13 forbids, and
it would have falsified this RESULT's own "every model call is logged" claim. The probe is deleted; the
key is now checked against CONFIG, no network involved, and
`test_reasoning_path_never_fires_a_probe_call_even_with_a_key` pins it by setting a fake key and
attaching `ExplodingProvider`.

The four required proofs, and how each is proven by EXECUTION rather than by assertion-on-a-return-value:

| Required proof | Test | How it is proven |
|---|---|---|
| Router classifies the five categories | `test_five_categories` (13 cases) + `test_all_five_categories_are_reachable` | every member of the enum is produced by a real request string |
| **DETERMINISTIC never invokes a provider** | `test_deterministic_run_never_invokes_a_provider` | the whole `run()` executes with `ExplodingProvider`, whose every method raises `AssertionError("provider invoked")`; then the SQLite ledger is asserted **empty** and cost **0.0** |
| Invalid WorkOrder rejected before the worker | 12 rejection cases + `test_invalid_work_order_never_reaches_the_worker` | the bad order is fed to `run()` with `ExplodingProvider` attached - reaching the worker would raise |
| Fence blocks writes outside the worktree | 7 escape cases + sibling-prefix + prohibited-path + `test_write_file_tool_refuses_an_escape` | includes `..\..\`, `C:\Windows`, `/etc/passwd`, a different drive, and `WO-1-evil` vs root `WO-1` (a string-prefix check would wrongly allow that one). The outside file's contents are asserted unchanged after the attempt. |
| Max autonomous retry = 2, then RCA | `test_worker_gets_exactly_three_attempts_then_rca` | asserts `attempts == 3` AND `len(provider.calls) == 3`, then opens the written RCA JSON and checks all six spec s12 fields, then `state == 'RCA'`, `retry_count == 2`, 3 ledger rows |

## 5. End-to-end run - `ai run`, real repo, local worker only

Command (from `tools/ai-orchestrator/`):

```
.venv\Scripts\python.exe -m ai_orchestrator run \
  "Add a module docstring to tools/merge_vfx_aliases.py explaining what the script does" \
  --path tools/merge_vfx_aliases.py --show-diff
```

Printed summary block, verbatim (spec section 18 shape):

```
Task: WO-6930e644c92e
Classification: WORKER
Reasoner: (not used)
Worker: gpt-oss:20b

Planning....... PASS
Execution...... PASS
Tests.......... none defined
Validation..... PASS

Files Changed: 1
OpenAI Calls: 0
Worker Calls: 2
Tokens: 2086 in / 1564 out
Estimated Cost: $0.0000
Duration: 59.8s
Status: COMPLETE
Routing: mechanical execution inside explicit boundaries
```

Captured diff (produced inside a temporary worktree; **the main tree was never modified**):

```diff
diff --git a/tools/merge_vfx_aliases.py b/tools/merge_vfx_aliases.py
index 23eb8f9a9..ebc3948c2 100644
--- a/tools/merge_vfx_aliases.py
+++ b/tools/merge_vfx_aliases.py
@@ -1,5 +1,22 @@
-# Merge code-key aliases into VfxManualPicks.json so built-in PlayKey call sites
-# resolve to owner-picked prefabs after HovlVfxCatalog regenerate.
+"""Merge code-key aliases into VfxManualPicks.json.
+
+This script loads the existing ``VfxManualPicks.json`` file, updates the
+``rows`` dictionary with a predefined set of alias entries, and rewrites the
+file.  The aliases map logical VFX keys to prefab paths, specify whether the
+effect should loop, and provide a scaling factor.  After inserting the
+aliases, the script also ensures that a list of one-shot keys have
+``isLoop`` set to ``False``.
+
+Typical usage:
+
+```
+python tools/merge_vfx_aliases.py
+```
+
+The script prints a summary of the number of rows processed and the
+aliases added.  It is intended to be run manually whenever the VFX catalog
+is regenerated.
+"""
 import json
 import os

@@ -12,7 +29,6 @@ rows = {r["key"]: r for r in data["rows"]}
 def path_of(key: str) -> str:
     return rows[key]["prefabPath"]

-
 aliases = [
     ("Melee_Impact", path_of("Weaponskillsword_Impact"), False, 1.0),
     ("Melee_Slash", path_of("KnightThrust_Impact"), False, 1.0),
@@ -65,7 +81,7 @@ for key, prefab, is_loop, scale in aliases:
         "manual": True,
     }

-out = {"rows": sorted(rows.values(), key=lambda r: r["key"])}
+out = {"rows": sorted(rows.values(), key=lambda r: r["key"]) }
 with open(path, "w", encoding="utf-8", newline="\n") as f:
     json.dump(out, f, indent=4)
     f.write("\n")
```

**Honest finding about that diff:** the docstring is correct and well-written, but the worker also
made **two changes nobody asked for** - it deleted a blank line before `aliases = [` and added a
stray space inside `…["key"]) }`. The workspace fence permitted them because they are inside an
allowed path; the *objective* did not. This is the spec section 2 failure mode ("the worker must not
independently redefine…") showing up in miniature, and it is the single strongest argument for the
spec section 21 benchmark before a permanent worker is chosen. It is recorded, not hidden.

**Worktree lifecycle, measured before and after:**

```
before:  git worktree list | grep -c ai-worktrees   ->  0
         ls ai-worktrees                            ->  No such file or directory
after:   git worktree list | grep -c ai-worktrees   ->  0
         ls ai-worktrees                            ->  No such file or directory
         git status --short tools/merge_vfx_aliases.py  -> (empty: main tree untouched)
```

**Disclosure — git commands the TOOL runs.** I ran no git mutation myself. The orchestrator does, and
only inside the temporary worktree, whose index is separate from the main repo's: `git worktree add
--detach --no-checkout`, then `git sparse-checkout set`, then `git checkout` to populate, then
`git add -A -- .` + `git diff --cached` to capture the patch, then `git worktree remove --force` +
`git worktree prune`. The main repo's index and working tree are untouched — `git status --short
tools/merge_vfx_aliases.py` returns empty, and `git worktree list | grep -c ai-worktrees` returns 0.

The worktree is created `--detach --no-checkout` + sparse-checkout of only the top-level directory the
work order allows + `GIT_LFS_SKIP_SMUDGE=1` (this repo is large and LFS-backed; a full checkout per
task would cost minutes), and removed in a `finally` block followed by `git worktree prune`. `--detach`
means no branch litter - deliberate, given the **97 entries** `git worktree list` already returns in
this repo, 96 of them abandoned `.claude/worktrees/`.

## 6. Model calls logged (spec section 13)

`ai usage`, and the raw ledger rows, after the two E2E runs:

```json
{"calls": 4, "input_tokens": 4100, "output_tokens": 3259, "estimated_cost_usd": 0.0,
 "by_model": [{"provider": "ollama", "model": "gpt-oss:20b", "calls": 4,
               "input_tokens": 4100, "output_tokens": 3259, "estimated_cost_usd": 0.0}]}
```

```
{'task_id':'WO-81a8523f86ba','role':'worker','provider':'ollama','model':'gpt-oss:20b','purpose':'plan',             'input_tokens':198, 'output_tokens':117, 'estimated_cost_usd':0.0,'d':35.8}
{'task_id':'WO-81a8523f86ba','role':'worker','provider':'ollama','model':'gpt-oss:20b','purpose':'execute:attempt1','input_tokens':1816,'output_tokens':1578,'estimated_cost_usd':0.0,'d':57.6}
{'task_id':'WO-6930e644c92e','role':'worker','provider':'ollama','model':'gpt-oss:20b','purpose':'plan',             'input_tokens':198, 'output_tokens':276, 'estimated_cost_usd':0.0,'d':12.0}
{'task_id':'WO-6930e644c92e','role':'worker','provider':'ollama','model':'gpt-oss:20b','purpose':'execute:attempt1','input_tokens':1888,'output_tokens':1288,'estimated_cost_usd':0.0,'d':47.0}
```

Board rows for both: `state=DONE`, `retry_count=0`, `worker_model=gpt-oss:20b`. **4 model calls, 4100
input + 3259 output tokens, $0.00 - zero OpenAI calls.** (The first task, `WO-81a8…`, is the same run
made a minute earlier; its work completed and its worktree was cleaned, but the CLI then died printing
the diff through the Windows cp1252 console codec. Fixed in `cli.py:main` by reconfiguring stdout/stderr
to UTF-8 with `errors="replace"` before parsing args - a diff out of this repo carries characters
cp1252 cannot encode, and a traceback *after* the work is done reads as a failed run.)

## 7. Other paths demonstrated on the CLI

```
$ ai classify "run the tests"
DETERMINISTIC: conventional code performs 'run_tests' reliably; no model is required
deterministic operation: run_tests

$ ai classify "decide the architecture for offline saves"
REASONING: request requires judgement the worker must not make

$ ai models
config: D:\EoA\tools\ai-orchestrator\models.yaml
  reasoner             openai/gpt-5.6-sol
  worker               ollama/gpt-oss:20b
  worker_fallback      openai/gpt-5.6-luna
  worker_local_small   ollama/qwen2.5-coder:7b

$ ai run "git status"
Classification: DETERMINISTIC ... Worker Calls: 0 ... Tokens: 0 in / 0 out ... Status: COMPLETE
Routing: conventional code performs 'git_status' reliably; no model is required

$ ai run "do an RCA on the raid reward serialization failure"
Classification: REASONING
Reasoner: gpt-5.6-sol
Planning....... BLOCKED
Reason: classification REASONING requires the 'reasoner' role, and its provider refused: the
'reasoner' role needs provider 'openai' model 'gpt-5.6-sol', but environment variable
OPENAI_API_KEY is not set. Set it in the environment (never in a repo file) and re-run. The
reasoner half of WO-1706 is blocked until then.
```

That last block is the ticket's "fail LOUDLY, never a silent stub" requirement, executed. The run
STOPS - it does **not** quietly hand the reasoning work to the local worker. `OPENAI_API_KEY` was
confirmed empty in the same shell immediately before (`[<empty>]`).

## 8. Ticket section 5 acceptance, line by line

- [x] `tools/ai-orchestrator/` with the spec s3 layout (`providers/ router/ schemas/ tools/ state/`)
- [x] `models.yaml` per spec s5, plus pricing and a `worker_local_small` benchmark entry; **no model
      string in code** - `test_no_model_string_is_hard_coded_in_code` greps every `.py` in the package
      for every configured model name, exempting only docstrings/comments (naming the model a behaviour
      was PROVEN against is evidence; a literal the program can read is the violation)
- [x] Router classifies into the five categories; DETERMINISTIC never calls a model (proven by execution)
- [x] Pydantic `WorkOrder`, `TaskResult`, `ValidationResult`; invalid orders rejected before the worker
- [x] Tool layer per spec s9 - `ToolRegistry.call` performs all six steps in order (validate, permission,
      execute, capture, record, structured return); worker cannot write outside its worktree
- [x] Ollama provider live against `gpt-oss:20b`; OpenAI provider present, config-driven, loud on no key
- [x] `ai run "<request>"` prints the spec s18 summary block
- [x] Every model call logged with provider/model/tokens/estimated cost/task id
- [x] Max autonomous retry = 2, then an RCA package written to `.state/rca/<task>.json`
- [x] Python tests for router, schema rejection, workspace fence, retry cap - 63 passed
- [x] End-to-end demo of one representative mechanical task with the git diff captured

Also delivered beyond the minimum, because the spec asks for them and they were cheap: spec s11 board
states in SQLite, s13 usage report (`ai usage`), s14 context builder (nothing dumps the repo into a
model), s15 `.ai/` project-knowledge reader, s16 MODE A FastAPI surface with the MODE B operation names,
s17 human-approval gate (`git_push` is registered and refuses), s12 escalation triggers.

## 9. What is NOT done

1. **The reasoner half. Blocked on `OPENAI_API_KEY`** - the ticket's own section 4. The provider class
   is written and its failure path is proven; its **success path has never executed**. The Responses
   API request/response mapping in `openai_provider.py` is written to the documented shape and is
   **UNPROVEN** - I am saying so rather than ticking it. First run with a real key must confirm it, and
   any mismatch is a bug in that file.
2. **`REASONING_THEN_WORKER` end-to-end.** The branch exists and routes; it cannot complete without the
   key. Today a REASONING-class request blocks instead of planning.
3. **Spec s22's "five representative tasks".** I demonstrated **one** (the ticket scoped it that way:
   *"the spec section 22's 'five' waits for the reasoner key"*). Note section 0 above - the sibling
   `tools/hybrid-orchestrator/` appears to have five demo runs on disk.
4. **Spec s21's 20-task benchmark.** Not built. `worker_local_small` (`qwen2.5-coder:7b`) is configured
   as the second candidate so the benchmark has something to compare against. **The worker drift in
   section 5's diff is the concrete reason to run it before anointing a permanent worker.**
5. **`api.py` has not been driven by a client** on this machine. It imports and is wired; that is all I
   can claim.
5b. **The OpenAI pricing in `models.yaml` is a PLACEHOLDER, not a verified price.** `1.25/10.00` and
   `0.25/2.00` per 1M tokens were not read off any live price list this session - with no key there was
   nothing to read them from. `estimated_cost_usd` is computed from them, so every openai-role cost
   figure the ledger will report is provisional until the owner confirms the numbers. The placeholder
   is flagged in `models.yaml` itself, above each role. All cost figures in this RESULT are $0.00
   because every call made was local, where the price genuinely is zero.
6. **Phase 3 / ChatGPT tool integration** (spec s20) - out of Phase 1 scope entirely.
7. **No gate was run and nothing was committed** - per the lane brief. The directory is outside the
   Unity gate's scan.

## 10. What the owner / lead must do next

1. **Decide between this and `tools/hybrid-orchestrator/` (section 0).** Two untracked orchestrators
   for one ticket; only one should be committed. That is a lead/owner call, not a lane's.
2. **Supply `OPENAI_API_KEY` in the environment** (never in a repo file) to unblock the reasoner. Then
   the first real reasoning run also validates the unproven Responses-API mapping.
3. **Confirm the model names AND the prices in `models.yaml`.** `gpt-5.6-sol` / `gpt-5.6-luna` are
   copied verbatim from the owner's spec s5; the per-Mtok prices beside them are **placeholders I
   invented** (section 9 item 5b). Neither could be checked against anything live - there is no key.
   Both are one-line YAML edits, which is the entire point of s5.
4. **Commit decision:** `.venv/`, `.state/` and `.pytest_cache/` are gitignored. The committable set is
   the **42** files `git ls-files --others --exclude-standard tools/ai-orchestrator` lists (section 1).
5. **Optional:** put `tools\ai-orchestrator\ai.cmd` on PATH so `ai run "..."` works from anywhere.


---

## Lead verification + collision ruling (2026-09-14)

- Lead re-ran the suite: `63 passed in 2.66s` (`tools/ai-orchestrator/.venv/Scripts/python.exe -m pytest tools/ai-orchestrator/tests`). Secret grep over py/yaml/md: no hits.
- Owner ruling 2026-09-14 (AskUserQuestion): **keep this build**. The untracked Codex build `tools/hybrid-orchestrator/` (247 files, 2026-09-13, 39 tests passing when the lead ran them) is REMOVED from the tree; its five benchmark cases are folded in as `benchmarks/codex_seed_cases.json` (+ `CODEX_SEED_README.md`) to seed the spec section 21 benchmark. A copy sits in the session scratchpad only until reboot.
- Status bucket: the ticket is BLOCKED (reasoner half needs the key); the worker half is done. The leading word is BLOCKED so the board surfaces the pin instead of closing the ticket.
- The lead minted WO-1706 without first checking the tree for the Codex build - one Opus lane (217k tokens) duplicated 14-hour-old work. Recorded so the boot checklist gains "grep the untracked tree for the ticket subject before minting".
