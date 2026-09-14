# WORK ORDER 1706 - Hybrid AI Reasoning + Task Execution Orchestrator

**Status:** BLOCKED - worker half IMPLEMENTED and lead-verified 2026-09-14 (63/63 tests re-run by the lead, E2E diff on a real repo file, committed); reasoner half waits on the owner supplying OPENAI_API_KEY as an env var
(result: `WorkOrders/WORK_ORDER_1706_hybrid_ai_reasoning_task_execution_orchestrator.RESULT.md`.
⚠ The RESULT's section 0 records an UNTRACKED second implementation of this same ticket already in the
tree at `tools/hybrid-orchestrator/` - the lead must pick one before committing.)
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from the owner's spec delivered outside the repo
**Source spec:** `C:\Users\Elden\Downloads\Work Order_ Hybrid AI Reasoning + Task Execution Orchestrator (1).md`
(861 lines, owner-authored, read in full 2026-09-14). This ticket SCOPES that spec into the repo's lane
shape; the spec itself stays the authority on the design and is quoted, not re-derived.
**Owner directive (2026-09-14):** *"delegate the most cost-efficient usage of agents to minimize costs
above all time savings"* - this orchestrator is the mechanism. Owner ruling the same day: **Claude lanes
in this repo stay on Opus**; cost is cut by routing MECHANICAL work to the local worker this ticket
builds, not by downgrading the Claude lanes.

---

## 1. Objective (spec section 1-2, verbatim intent)

A local service where an expensive reasoning model decides **what** should be done (planning, RCA,
architecture, work-order authoring, failure resolution) and a cheap local model executes **how**, inside
explicit boundaries (file edits from an approved plan, JSON/YAML/MD changes, doc generation,
localization, test generation, log analysis, repo search, mechanical refactors). Requests are
classified BEFORE any model call into `DETERMINISTIC | WORKER | REASONING | REASONING_THEN_WORKER |
ESCALATION` (spec section 6); deterministic work never touches an LLM.

## 2. Location and shape (lead default - owner may veto)

- Lives at **`tools/ai-orchestrator/`** in this repo (Python, FastAPI, Pydantic, SQLite, per spec
  section 3). Reason: the repo already carries every other operator tool under `tools/`, and the
  orchestrator's tool layer (git, tests, worktrees) is repo-bound. A sibling repo is the alternative if
  the owner wants it reusable across projects.
- Provider abstraction is mandatory (spec section 3-4): `LLMProvider.generate()`,
  `generate_structured()`, `call_tools()`; model names ONLY in config (spec section 5), never in code.
- Worker workspace = a temporary git worktree under `ai-worktrees/WO-<n>/`, cleaned up after the diff is
  captured (spec section 10). ⚠ This repo already has **abandoned agent worktrees** under
  `.claude/worktrees/` (11+ listed by `git worktree list` on 2026-09-14) - the cleanup rule in the spec
  is the fix for that pattern too; do not add a second worktree convention.

## 3. Facts measured on this machine, 2026-09-14 (so nobody re-derives them)

| Fact | Value | How read |
|---|---|---|
| Ollama | `ollama version is 0.34.0` | `ollama --version` |
| Local models already pulled (9 h before minting) | `gpt-oss:20b` (13 GB), `qwen2.5-coder:7b` (4.7 GB), `llama3.2:3b` (2.0 GB) | `ollama list` |
| Python | 3.14.0 | `python --version` |
| OpenAI key | **ABSENT** - not in env, not in `.env.local` / `.env` | `$OPENAI_API_KEY` empty; grep of both files empty |
| Memory headroom at mint | committed 96.8 GB of 111.8 GB limit | `Get-Counter '\Memory\Committed Bytes'` |

The spec's initial worker candidate is `gpt-oss:20b` (section 4); `qwen2.5-coder:7b` is the obvious
second benchmark entry (section 21). The benchmark decides, not this line.

## 4. Blockers and what is buildable NOW

- **Reasoner half is BLOCKED**: spec section 22 requires "OpenAI reasoning provider is configured" and
  the key does not exist on this machine. The owner supplies it (env var `OPENAI_API_KEY`, never a repo
  file - see `.claude/settings.json` secrets posture). Until then the reasoner provider is a real class
  with a real config entry whose calls FAIL LOUDLY with "no key", never a silent stub.
- **Worker half is buildable today with zero API spend**: router + schemas + tool layer + Ollama
  provider + `ai run` CLI against a hand-written work-order JSON (spec section 7-9, 18). Phase 1 DoD
  needs the key; the worker DoD does not.

## 5. Phase 1 deliverable (spec section 18) - the lane's acceptance criteria

- [ ] `tools/ai-orchestrator/` with the layout in spec section 3 (`providers/ router/ schemas/ tools/ state/`).
- [ ] `models.yaml` per spec section 5 (reasoner `openai`/`gpt-5.6-sol`, worker `ollama`/`gpt-oss:20b`,
      `worker_fallback` `openai`/`gpt-5.6-luna`); NO model string anywhere in code.
- [ ] Task router classifies into the five categories; DETERMINISTIC never calls a model (test proves it).
- [ ] Pydantic `WorkOrder`, `TaskResult`, `ValidationResult` per spec sections 7-8; invalid work orders
      are rejected before the worker sees them (test proves it).
- [ ] Tool layer per spec section 9; every call validates, permission-checks, executes, captures,
      records, returns structured output. Worker cannot write outside its worktree (test proves it).
- [ ] Ollama provider live against `gpt-oss:20b`; OpenAI provider present, config-driven, key-absent = loud failure.
- [ ] `ai run "<request>"` prints the spec section 18 summary block.
- [ ] Every model call logged with provider/model/tokens/estimated cost (spec section 13).
- [ ] Max autonomous retry = 2, then an RCA package (spec section 12).
- [ ] Python tests for the router, schema rejection, workspace fence, retry cap. Run them; paste output.
- [ ] End-to-end demo of at least ONE representative mechanical task on this repo, worker-only, with
      the git diff captured (spec section 22's "five" waits for the reasoner key).

## 6. What NOT to touch

- No Unity, no `.cs`, no `Assets/` - this is a Python tool. It must not need the Unity gate.
- No `.claude/hooks/*` edits (the cadence rule is owner canon; the Opus-lane ruling stands).
- No secrets in the tree. No `vercel`/`api/` changes.
- Do not wire it to Codex, Claude, or the F8 daemon in Phase 1.

## 7. Recorded findings from the inherited tree (context for the lane, NOT its scope)

- Codex's 2026-09-13 night gate (clone `D:\eoa-night-build-20260913`, snapshot `dfe0c7403`) read
  `COMPILE_GATE_OK`, `REGRESSION_FAIL 520/522`, `SESSION_GUARDS_FAIL 48 across 3/6`, EditMode
  1030/1036, then Codex ran out of credits until 2026-09-20. The 2 data reds: canonical-JSON fallback
  hashes broke on a CRLF fresh checkout (the tree's `.gitattributes` change covers ONLY
  `api/_lib/sku-catalog.generated.json`, not `Assets/Resources/Data/Canonical/*.json` or its
  StreamingAssets twin - memory `canonical-json-edits-binary-only-verify-newlines`), and the tutorial
  watchdog skipped-step check. Session-guard reds are stale guards (Resources-only art assumption,
  pet name now persisted). None fixed; all recorded here so the next seat starts from data.
- Memory headroom at mint is 15 GB. Memory `commit-charge-leak-blocks-builds`: a reboot is the only
  fix before another full batchmode run.
