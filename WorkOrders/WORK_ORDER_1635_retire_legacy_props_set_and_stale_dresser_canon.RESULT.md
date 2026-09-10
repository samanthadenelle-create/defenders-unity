# WO-1635 RESULT — lane PROPS-CANON (canon half only)

**Lane:** PROPS-CANON, 2026-09-10
**Worktree:** `.claude/worktrees/agent-ac2fe3f338f28c3ac` (isolated), branch `dev`
**Base sha:** `3da5e5360` (`git merge --ff-only refs/heads/dev`, tree clean before edits)
**Unity:** not run. **No `.cs` edited** — brace/NUL gate is **N/A** for this change (documentation only).
**No gate marker is claimed by this lane.**

---

## 0. What this lane did and did NOT do

This lane executed **acceptance #4 only** — the four (in fact **six**) stale canon lines get dated
correction banners. It also **closed #6 as already-satisfied**. The code/JSON half of the ticket is
untouched.

| Acceptance | This lane | Proof / state at `3da5e5360` |
|---|---|---|
| **#1** one prop authority; drop `props.set` fallback + `fortified_garrison`'s duplicate `"barracks"` | ❌ **NOT DONE** | Fallback still present: `RaidBaseDresser.ScatterProps` `:540-552` reads `def.props.set`. JSON still authors `"props": { "set": ["barracks"], "count": 1 }` on `fortified_garrison` alongside `"barracks"` in `raidDress.props`. |
| **#2** `DefaultProps(kit)` deleted or labelled TGVRU-only | ⚠ **PARTIAL, and by another lane, on the LEAD's tree only** | The lead's uncommitted tree carries `RaidBaseDresser.cs:895` — `⚠ TGVRU EMERGENCY SET ONLY - scene-configs.json raidDress.props IS THE AUTHORITY ... WO-1635 owns retiring it`. That comment is **not** at `3da5e5360`; the body of `DefaultProps` still holds content in both trees. |
| **#3** regression reds on dual-schema / empty `raidDress.props` | ❌ **NOT DONE — and the suite currently ENFORCES THE OPPOSITE, see §2.3** | Full-file grep of `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` (no head limit) returns exactly six `props` hits: `:98-104` (`CaseEasyDress` — Easy `raidDress.props` count >= 12 only) and `:154-155` (`CaseGeneratorWiresDresser`). **No case inspects `props.set` in the JSON**, so nothing reds on dual-schema authoring, and Hard/Extreme empty-`raidDress.props` is unchecked. |
| **#4** four stale doc lines carry dated corrections; frozen headers get banners, not rewrites | ✅ **DONE — six sites, not four** | Table in §1 below. |
| **#5** `scene-configs.json` byte-safe (LF == CRLF) | n/a — **no JSON edited by this lane** | `git status --short` lists no `.json` path. |
| **#6** `RaidBaseGenerator.cs` untouched | ✅ **DONE, and the stale line was already gone** | `RaidBaseGenerator.cs:35` at `3da5e5360` already reads *"WO-1608: `props` + `raidDress` are LIVE — RaidBaseDresser consumes them at bake time."* — **no edit made, file not opened for writing.** |

---

## 1. Lines changed — old -> new, with the proving line for each

All six are **insertions only** (`git diff --stat` = 72 insertions, 0 deletions). Per CLAUDE.md §15 a
dated WO/RESULT is **frozen**: every original sentence is left byte-for-byte as authored and a dated
`⚠ CORRECTED` blockquote is added beside it.

### 1. `WorkOrders/WORK_ORDER_1607_raid_bases_as_places.md:47` (the line WO-1635 §2 row 1 names)

- **Old (left intact):** *"`props` is authored empty and unread. Every raid row has `"props": { "set": [], "count": 0 }`. Generator header `:35` says so: 'Still dead and DELIBERATELY not faked…'"*
- **New:** banner inserted immediately after item 2 correcting **both halves** of the claim.
- **Proof cited:**
  - Authored — `Assets/Resources/Data/Canonical/scene-configs.json` `raidDress.props`: `raider_camp_small` 10 entries / **27** instances, `fortified_garrison` 9 / **25**, `mage_enclave` 8 / **27** (counted from the file this session at `3da5e5360`).
  - Read — `RaidBaseDresser.ScatterProps` consumes `def.raidDress.props`, `Assets/Editor/WallTools/RaidBaseDresser.cs:535-539` at `3da5e5360`.
  - Called — `RaidBaseDresser.Dress(def, root, …)`, `Assets/Editor/WallTools/RaidBaseGenerator.cs:424`.
  - The quoted `:35` header — already corrected, `RaidBaseGenerator.cs:35`.
  - Baked — `Builds/wave3-bake7:503` `dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0`.
- The banner also records that the **`props.set` fallback and the `fortified_garrison` duplicate are still live**, so the correction cannot be misread as "WO-1635 is finished".

### 2. `WorkOrders/WORK_ORDER_1609_easy_forsaken_camp_settlement.md:3` header count

- **Old (left intact):** `**Status:** IMPLEMENTED — baked 2026-09-09 (hexagon-green, 251 dress pieces, missing=0)`
- **New:** banner below the header block: **251 -> 314**.
- **Proof:** `Builds/wave3-bake7:503` — `[Flow:RaidBase] dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0 gateW=8.55 zones=Approach,Gatehouse,Courtyard`.
- The `**Status:**` line itself is **not** edited — `tools/board_build.py` parses the first `**Status:**`, and a frozen dated header is not rewritten (§15). No second `**Status:**` token was introduced.

### 3. `WorkOrders/WORK_ORDER_1610_hard_broken_garrison_fortress.md:3` header count

- **Old (left intact):** `… (synty-castle, keep layer, 531 dress pieces)`
- **New:** banner: **531 -> 489**, plus a pointer that this is the config authoring `"barracks"` in both schemas (WO-1635 acceptance #1, still open).
- **Proof:** `Builds/wave3-bake7:640` — `[Flow:RaidBase] dressed 'fortified_garrison' kit=synty-castle placed=489 missing=0 gateW=8.72 zones=Approach,Gatehouse,Courtyard,Choke,Keep`.

### 4. `WorkOrders/WORK_ORDER_1611_extreme_veiled_enclave_dungeon_keep.md:3` header count

- **Old (left intact):** `… (dungeon-stone keep, 668 dress pieces)`
- **New:** banner: **668 -> 590**.
- **Proof:** `Builds/wave3-bake7:671` — `[Flow:RaidBase] dressed 'mage_enclave' kit=dungeon-stone placed=590 missing=0 gateW=8.58 zones=Approach,Gatehouse,Courtyard,Choke,Keep`.

### 5. `WorkOrders/WORK_ORDER_1607_raid_bases_as_places.RESULT.md:20-22` — **a FIFTH copy the ticket's ledger does not name**

- **Old (left intact):** the child-status table re-quoting `251 dress pieces` / `531 dress pieces` / `668 dress pieces`.
- **New:** `⚠ SUPERSEDED COUNTS` banner above `## 2`, listing all three fresh `placed=` lines.
- **Proof:** the three `Builds/wave3-bake7` lines above.
- **Finding:** these three numbers were hand-copied into **four** documents. Correcting only the three the ledger named would have left the fourth to re-seed the same stale claim.

### 6. `WorkOrders/WORK_ORDER_1608_raid_base_layered_defense_engine.md:20` + `:65` — **a SIXTH copy**

- **Old (left intact):** §1 *"`props` is dead (`:35`)"*; §4 heading *"Consume `props` (currently dead)"* with *"Raid rows all ship `set: []`, `count: 0`"*.
- **New:** one banner after the header block covering both, noting these were **spec-time state** and that **this very WO is the change that falsified them**.
- **Proof:** `RaidBaseGenerator.cs:35`; `RaidBaseGenerator.cs:424`; `RaidBaseDresser.cs:535-539`; the `scene-configs.json` entry/instance counts above — all at `3da5e5360`.
- The banner also names the surviving dual-reader as WO-1635 acceptance #1, still open.

`WorkOrders/WORK_ORDER_1633_raid_courtyard_props_are_decor_not_cover.RESULT.md:14` was inspected and
**deliberately left alone** — it narrates the stale claim in the past tense (*"…said the `props` seam was
authored empty and unread"*), which is accurate history, not a current assertion.

---

## 2. Two corrections to WO-1635's own ledger

Recorded here rather than by rewriting the ticket body:

1. **`Builds/wave2-bake2` is not a readable log.** WO-1635 §2 cites it for the three `placed=` counts.
   Listing `Builds/` at `3da5e5360` shows only `Builds/wave2-bake2.runner.txt` — the log body is not on
   disk. Every count in this lane is therefore cited to **`Builds/wave3-bake7`** (lines 503 / 640 / 671),
   which is present, is newer, and carries the identical three values the ticket predicted
   (314 / 489 / 590). The ticket's *numbers* were right; its *citation* was not readable.
2. **WO-1635 §2 row 2 (`RaidBaseGenerator.cs:35`) was already closed before this lane started.** The
   header no longer says *"Still dead and DELIBERATELY not faked"* at `3da5e5360`. §3's scope boundary
   therefore needs no follow-up ticket and no hand-off to lane ARENA-WALL: **the file was never opened
   for writing by this lane**, and nothing remains to correct there.

### 2.3 ⛔ BLOCKER FOR ACCEPTANCE #1 — the regression suite currently REQUIRES the legacy reader

`RaidBaseLayoutRegression.CaseGeneratorWiresDresser` contains, at `3da5e5360`:

```
:154            if (dress.IndexOf("def.props", StringComparison.Ordinal) < 0)
:155                failures.Add("props has no dresser consumer.");
```

`dress` there is the **source text** of `RaidBaseDresser.cs` (`DresserSrc`, `:25`). So the suite **FAILS
if the string `def.props` is absent from the dresser** — i.e. deleting the `props.set` fallback at
`ScatterProps` `:540-552`, which is exactly acceptance #1, **REDS this suite** unless `:154-155` is
rewritten in the same change. WO-1635 does not mention this; the implementing lane must be told, or it
will make the correct edit and read the red as its own regression.

Fix shape for that lane (not done here, no `.cs` touched): `:154-155` should assert the **authored**
seam — `def.raidDress` / `raidDress.props` — which `:151-153` already checks, and the `def.props` clause
should be inverted into acceptance #3's new case (red **if** `def.props` is still read), so the same line
that used to demand the legacy reader becomes the line that forbids it.

### 2.4 What the derived board will show for this ticket

`tools/board_build.py:187` — `if lead in ("DONE", "IMPLEMENTED", "COMPLETE"): return "Done", False` —
tests the **leading phrase** of the `**Status:**` line. The ordered string leads with `IMPLEMENTED`, so
after `python tools/board_build.py` **WO-1635 renders in the Done bucket**, and `board_build.py:166`
calls Done *"the one bucket nobody re-opens"*. The scope banner in the WO body and this file are **not**
read by the board. The lead ordered that exact status string and it is used verbatim; this is recorded so
the choice is made with eyes open, and it is why the scope banner sits directly beneath the Status line
where a human reader hits it first. (`_MINTED`, `board_build.py:438`, is a free `MULTILINE` search, not a
positional header parse, so inserting the banner between `**Status:**` and `**Minted:**` does not disturb
mint-date resolution.)

Additionally, `RaidBaseDresser.cs` line numbers move under the lead's uncommitted tree
(`ScatterProps`' `raidDress.props` read sits at `:616-619` there vs `:535-539` at `3da5e5360`). Every
banner therefore cites the **method name** as the durable anchor and states the sha the line number was
read at — a line number alone is exactly the hearsay CLAUDE.md §11B forbids.

---

## 3. Verification performed

- `git status --short` clean before edits; `git merge --ff-only refs/heads/dev` -> `3da5e5360`.
- `git diff --stat` after edits: **6 files changed, 72 insertions(+), 0 deletions(-)** — insertion-only,
  no whole-file churn, so the worktree's CRLF checkout (`core.autocrlf=true`, `.md` unspecified in
  `.gitattributes`) did not mangle any EOL.
- No `.cs`, no `.json`, no `.unity`, no `.asset` touched. Brace/NUL gate **N/A**.
- No Unity run, no commit, no push. `BOARD.html` regeneration is the lead's step, per the cadence rule.

## 4. Files changed

```
M WorkOrders/WORK_ORDER_1607_raid_bases_as_places.md
M WorkOrders/WORK_ORDER_1607_raid_bases_as_places.RESULT.md
M WorkOrders/WORK_ORDER_1608_raid_base_layered_defense_engine.md
M WorkOrders/WORK_ORDER_1609_easy_forsaken_camp_settlement.md
M WorkOrders/WORK_ORDER_1610_hard_broken_garrison_fortress.md
M WorkOrders/WORK_ORDER_1611_extreme_veiled_enclave_dungeon_keep.md
?? WorkOrders/WORK_ORDER_1635_retire_legacy_props_set_and_stale_dresser_canon.md   (UNTRACKED)
?? WorkOrders/WORK_ORDER_1635_retire_legacy_props_set_and_stale_dresser_canon.RESULT.md   (UNTRACKED)
```

⚠ The two WO-1635 files are **untracked (`??`), not staged** — the lead's stage-by-explicit-path step must
`git add` them by name or they will be missed. The `.md` is a **byte-verbatim copy of the lead's own tree
copy** (WO-1635 is not on `dev` at `3da5e5360`) plus exactly two insertions: the Status flip and the scope
banner. Expect an add/add on merge; the lead's copy and this one differ only in those two edits.

## 5. What the lead still has to route

WO-1635 acceptance **#1, #2, #3, #5** — the code/JSON half — remain open and belong to a lane that owns
`RaidBaseDresser.cs`, `scene-configs.json` and `RaidBaseLayoutRegression.cs`. That lane is
**file-disjoint from this one**; nothing here blocks it, and nothing here should be read as having done
it.

**Hand that lane §2.3 with the ticket.** `RaidBaseLayoutRegression.cs:154-155` currently reds when
`def.props` is *absent* from the dresser, so acceptance #1's deletion breaks the suite unless those two
lines move in the same commit. WO-1635 does not say so.

**And note §2.4:** the ordered Status string puts WO-1635 in the board's **Done** bucket even though
four of its six acceptance items are open.
