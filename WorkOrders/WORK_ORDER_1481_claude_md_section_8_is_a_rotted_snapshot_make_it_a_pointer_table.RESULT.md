# WO-1481 RESULT - CLAUDE.md section 8 is now a pointer table, not a snapshot

**Status:** IMPLEMENTED - d6511b8e5 on HEAD 2026-09-09 (was READY); owner felt-test closes.
**Verified:** 2026-09-09, SILO 0A verify-and-flip pass, HEAD 184c8ff06, branch dev. Docs-only
ticket; read-only verification, no commit.

## 1. Landing sha

```
d6511b8e5 feat(wave-three): the owner's UI rulings land - Manage words and doors, raid loop
          closes, deploy screen redesign, harvest result, defense-report chip, quest claim,
          wardrobe gate, skills depth; wave-two lanes gated in
```

Found by `git log --oneline -S"A POINTER TABLE, NOT A SNAPSHOT" -- CLAUDE.md`.
`git merge-base --is-ancestor d6511b8e5 HEAD` = **ancestor**.

## 2. Acceptance, item by item, proven at HEAD

### [x] Acceptance 1 - no literal save-schema version, asmdef count, or flag state in CLAUDE.md

- **Section 8 converted.** `CLAUDE.md:335` is now
  `## 8. Pipeline State Quick Reference - A POINTER TABLE, NOT A SNAPSHOT`, and `:337` opens with
  `NO LIVE NUMBER, VERSION, COUNT OR FLAG STATE MAY BE WRITTEN INTO THIS SECTION`, naming WO-1481
  and the 2026-09-06 conversion. `:338-341` records WHY, including that the old text cited
  `RepoProps.cs:69` while the const had moved to `:111`.
- **The fact -> authority table exists** at `CLAUDE.md:346-360`, the same row shape
  `PIPELINE_STATE.md:15-22` uses. Sample rows read at source:
  - `:350` save schema -> `the const at Assets/_Modules/Core/State/SaveSchema.cs:41 - read the
    CONST, never the file's own header at :11-18`
  - `:356` assembly dependencies -> `the .asmdef files themselves (sec.5)`
- **Save schema:** `grep -n "v3[0-9]\|v4[0-9]\|schema v" CLAUDE.md` returns only `:339`, `:350`
  and `:369`, and each is a POINTER or an explicitly-retired history note (`:369` opens
  `DO NOT WRITE THE NUMBER HERE - read it off SaveSchema.CurrentVersion` and says the retired copy
  read `v38` from 2026-08-06 to 2026-09-06). No current version is asserted anywhere.
- **Asmdef count:** `CLAUDE.md:174` now reads
  `DO NOT RESTATE THE COUNT HERE - COUNT THEM: find Assets/_Modules -name '*.asmdef' | wc -l`, with
  `:175` recording that the line said 19 while the command returned 25. `:178` and `:190-201`
  redirect the dependency question to the `.asmdef` files themselves.
- **Flag state (MapTab):** `CLAUDE.md:252-254` now states `FeatureFlags.MapTab NO LONGER EXISTS -
  it was DELETED 2026-09-05 (WO-1396), read Assets/_Modules/Core/FeatureFlags.cs:843-849`, and
  cites `PublicNavigationRetirementRegression` as the authority pinning the absence - which is
  exactly the WO's sec.2 instruction.

### [x] Acceptance 2 - zero `schema v38` hits outside dated frozen ledgers

`grep -rn "schema v38" --include=*.md .` from repo root. Every surviving hit is inside a dated or
frozen artifact, which the acceptance explicitly exempts:

| Hit | Why it is exempt |
|---|---|
| `PIPELINE_STATE.md:54` | under `## VERIFIED SNAPSHOT - 2026-08-21 23:0x (this is itself a dated entry and will go stale)` (`:46`) |
| `PIPELINE_STATE.md:127`, `:171` | annotated in-line `(as of 2026-08-06; live is v38)` / `(frozen history - the value ON 2026-08-02...)` |
| `KEY_FACTS.md:868` | under `## Latest (2026-08-10) - the wave-3 settle` |
| `CLI_LANES_WO_NUMBERS.md:3646` | the frozen WO-934 mint banner entry |
| `WorkOrders/WORK_ORDER_1230_*.md:173`, `WorkOrders/HANDOFF_..._2026-08-09.md:103` | frozen dated WOs / handoff |
| `docs/_archive/root/CANON_GROUND_TRUTH_2026-08-21.md:55` | archived, dated anchor |
| `docs/GET_WELL_PLAN_2026-09-06.md:67` | dated plan; the string appears as the DESCRIPTION of this very defect |
| `tmp/play-deploy-*/` (6 hits) | build staging copies, not canon |

The three live copies the WO named are gone: `CLAUDE.md:320` (replaced by the `:369` pointer),
`PIPELINE_STATE.md:27-28` (now the pointer row `| Save schema version | SaveSchema.CurrentVersion
... |`), and `KEY_FACTS.md:724` / `:1195` (no `v38` at either; `:720-728` now carries CDN and
login-gate facts).

### [x] Acceptance 3 - each removed fact replaced by a runnable command

`CLAUDE.md:346-360` is the table; `:174` is a literal shell command; `:350` and `:369` name the
const and the file. Every removed number now has a place to be read from.

## 3. Residual - real, and NOT one of the three acceptance categories

`CLAUDE.md:369` (inside section 8, whose own header at `:337` forbids live numbers) still writes
two literal live values: **`DEPTH cap of 5 PER LINE`** and **`freeBuildSlots, which stays 2`**. Both
have authorities named one row up at `:353`
(`BuildTimerConfig.queueDepthPerLine (:269)` vs `freeBuildSlots (:231)`), so this is duplicated
state of exactly the kind this ticket exists to delete.

It is NOT a save-schema version, an asmdef count, or a flag state, so it does not fail any of the
three written acceptance lines - but it is the same bug one field over. **Recommend a follow-up
one-line edit** replacing the two numerals with the pointer already sitting at `:353`.

## 4. What the owner should felt-test

Docs-only - there is nothing in the build to feel. The verification is a read:

1. Open `CLAUDE.md` section 8 (`:335`). Does it read as a table of "fact -> where to read it",
   with no numbers you would have to trust?
2. Pick one row and run its authority (e.g. `Assets/_Modules/Core/State/SaveSchema.cs:41`). Does the
   file answer the question without a doc in the loop?
3. Rule on the residual in sec.3: strike the `5 PER LINE` / `stays 2` numerals from `:369`, or leave
   them.

## 5. What is NOT proven

- **`.md` files outside the load-bearing set were not swept.** The check was scoped to the WO's own
  three acceptance lines plus a repo-wide `schema v38` grep. Other rotted copies of other facts
  (gate counts, WO numbers, branch names) were not audited - `docs/GET_WELL_PLAN_2026-09-06.md:67`
  itself names further known-stale claims.
- **No proof the pattern will not recur.** There is no regression or lint pinning "section 8 carries
  no literal number". The rule at `:337` is prose enforced by seat discipline only.
- **Not gate-verified.** Docs-only change; `COMPILE_GATE_OK` was not run this session and would not
  be evidence for this ticket in any case.
