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

---

# WO-1635 RESULT — lane PROPS-RETIRE (code half), 2026-09-10

**Lane:** PROPS-RETIRE, 2026-09-10
**Worktree:** `.claude/worktrees/agent-a3dd7f19089bc8008` (isolated), branch `dev`
**Base sha:** `5a65a7831` (`git status --short` empty, then `git merge --ff-only refs/heads/dev`)
**Unity:** NOT run — edit-only lane. No gate, no bake, no commit, and **no gate marker is claimed.**
**Scope:** acceptance #1 (code side), #2, #3. **#4 and #6 were already closed** by lane PROPS-CANON
(landed `b10cd6783`). **#5 is N/A for this lane — see §D.**

## A. What changed, and the line it stood at on `5a65a7831`

| File | Was (at `5a65a7831`) | Now |
|---|---|---|
| `Assets/Editor/WallTools/RaidBaseDresser.cs` `:621-633` | `if (list.Count == 0 && def.props != null && def.props.set != null)` — the legacy `props.set` fallback, one instance each, always zone `Courtyard` | **DELETED.** In its place a dated retirement note + `FlowTrace.Warn(Sys, …)` naming the config, its kit and that it authors no props. The prop loop then runs zero times, so the existing `props '<id>': 0 placed` line still prints — an unauthored row is now LOUD and empty instead of silently dressed. |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` `:634` | `if (list.Count == 0) list.AddRange(DefaultProps(kit));` | **DELETED** (the call). |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` `:895-929` | the `DefaultProps(string kit)` method — a kit-keyed third copy of the content, already drifted (its `hexagon-green` branch still handed out `banner_green`/`weaponrack` that Easy's authored 10-entry array had moved past) | **DELETED**, replaced by a dated note where it stood (immediately above `ZoneOf`) saying why a C# prop set must not come back. |
| `Assets/_Modules/Village/World/SceneConfigCatalog.cs` `:60-66` (`PropsDef`) | `/// <summary>The props block ({ set:[ids], count }).</summary>` | Type **kept** (the JSON rows still carry the block and this lane may not edit them), summary rewritten to state it is **legacy, has no raid reader since WO-1635, and changes nothing at bake time**. |
| `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` `:157-158` | `if (dress.IndexOf("def.props", …) < 0) failures.Add("props has no dresser consumer.");` — **the suite REQUIRED the legacy reader** (the blocker §2.3 of the first RESULT flagged) | **Moved and inverted** into a new `CaseOnePropReader`. |
| `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` (new) | — | `CaseOnePropReader` + `CaseSinglePropAuthority`, both registered in `RunAll`, plus a WO-1635 bullet in the file header. |

## B. The RED-first pins (acceptance #3)

`CaseOnePropReader` (source-text oracle over `RaidBaseDresser.cs`) reds if:
1. `def.raidDress.props` is **absent** — the one authority lost its consumer; or
2. `def.props` is **present** — the retired `props.set` reader came back (the exact inversion the
   ticket asked for: the line that used to demand the legacy reader now forbids it); or
3. `DefaultProps` is **present** — a hardcoded C# prop set came back.

Proven by construction: pin 2 matches the literal `def.props`, and the deleted reader was written
`def.props.set[i]`, so restoring it in any recognisable form reds the suite. Neither literal survives
anywhere in the dresser — `grep -n "DefaultProps\|def\.props" Assets/Editor/WallTools/RaidBaseDresser.cs`
returns nothing, and the retirement comments deliberately spell the legacy shape as `props.set` so they
do not trip the pin they document.

`CaseSinglePropAuthority` (JSON oracle) walks **every** row with a `raidDress` block — not a hardcoded
id list, so a row PROP-GAPS adds today is covered the day it lands — and reds if that row authors no
`raidDress.props`, or if it **also** authors a non-empty legacy `props.set`.

## C. ⛔ THE SUITE REDS TODAY, ON PURPOSE, ON ONE ROW

`fortified_garrison` still carries `"props": { "set": ["barracks"], "count": 1 }` **and** `"barracks"`
as the first entry of `raidDress.props` — the duplicate the ticket names in §1. That is acceptance #1's
**JSON half**, and this lane was scoped OFF `scene-configs.json` because lane PROP-GAPS (WO-1634) is
authoring props rows in it concurrently. So `CaseSinglePropAuthority` will emit:

> raid row 'fortified_garrison' authors props in BOTH schemas: legacy props.set carries 1 token
> (barracks ...) while raidDress.props carries 25 instance(s). The legacy block has had no reader
> since WO-1635 — delete it from scene-configs.json so the row states one intent.

**The one-line fix, for whoever owns the JSON:** set `fortified_garrison`'s `"props"` to
`{ "set": [], "count": 0 }` (the shape the other four rows already use). Nothing else changes; the
block has had no reader since this commit. **Do not weaken the case to make it green** — a check that
cannot fail is what §2.3 of this file was written about.

This was a deliberate choice over the alternative (a compound `props.set non-empty AND dresser still
reads it` clause) which would have been **vacuous**: it can only fire when `CaseOnePropReader` has
already fired, so it could never independently red and would not satisfy acceptance #3.

**⚠ IT DOES BLOCK THE AGGREGATE, NOT JUST ITS OWN MARKER.** `RaidBaseLayoutRegression.Run` is invoked
from `Assets/Editor/Regression/DataRegression.cs:741` (`Guard.Try("Regression", "raid-base-layout
suite", …)`, adding to `failures`), so until the token goes the full data-regression entry point
reports FAIL, not just the raid-base suite line. The lead should land the one-token JSON edit in the
same gate run, or expect that FAIL and know exactly what it is.

**Contract this hands lane PROP-GAPS (WO-1634), who is editing the same JSON right now:** any row it
gives a `raidDress` block must author a non-empty `raidDress.props`, and must leave that row's legacy
`"props": { "set": [] }` empty. `CaseSinglePropAuthority` iterates rows by the presence of a
`raidDress` block, so a new or half-authored row reds on its own name. That is the case working, not a
defect — but PROP-GAPS should hear it before its bake.

## D. Scope conflict, stated plainly (CLAUDE.md §11B-B)

The lane brief lists **item 5** (`scene-configs.json` byte-safety: prove LF count == CRLF count after)
in this lane's scope **and** forbids the only file item 5 applies to. Both cannot hold. This lane took
the prohibition as controlling: **no JSON was touched, so item 5 has nothing to prove and travels with
the one-token edit in §C.** Flagging rather than silently choosing, per §11B-B.

## E. Proof that placement did not move (as far as an edit-only lane can prove it)

Baseline is `Builds/wave3-bake7` (newest bake on disk; `ls -t Builds/` puts `wave3-bake7` /
`wave3-navbake7` at the top). **`Builds/wave2-bake2` cited by the ticket is still not a readable log** —
only `wave2-bake2.runner.txt` exists, as the first RESULT already recorded.

    501:[RaidBaseDresser] props 'raider_camp_small': 27 placed (set=building_tent_greenx4*, barrel_largex4*, ...)
    636:[RaidBaseDresser] props 'fortified_garrison': 25 placed (set=barracksx1*, House_Medieval_Mediumx1*, ...)
    667:[RaidBaseDresser] props 'mage_enclave': 27 placed (set=pillar_decoratedx6*, banner_whitex4, ...)
    503:[Flow:RaidBase] dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0 gateW=8.55
    640:[Flow:RaidBase] dressed 'fortified_garrison' kit=synty-castle placed=489 missing=0 gateW=8.72
    671:[Flow:RaidBase] dressed 'mage_enclave' kit=dungeon-stone placed=590 missing=0 gateW=8.58

**27 / 25 / 27 is exactly the sum of `count` over each row's authored `raidDress.props`** (10 entries /
27 instances, 9 / 25, 8 / 27, counted from `scene-configs.json` at `5a65a7831`). Every placed prop is
therefore already accounted for by priority 1 — **neither deleted branch contributed a single instance
to the shipped bake**, which is why the counts must still read 27 / 25 / 27 and the aggregates
314 / 489 / 590 after a re-bake.

**Iron Bastion is untouched, and cannot be touched by this change.** `RaidBaseDresser.Dress(` has
exactly **one** call site repo-wide — `RaidBaseGenerator.cs:541`, inside `BuildFromConfig` — and the
config-driven entry point bakes `RaidConfigIds` = `{ raider_camp_small, fortified_garrison, mage_enclave }`
(`RaidBaseGenerator.cs:399-400`). Iron Bastion ships from the separate hardcoded `Build()` path
(`BuildInOpenScene` / `BuildIntoScene` / `BuildToNewScene`, `:364-394`), which never calls `Dress`. Consistent with that,
`grep -ci "iron_bastion" Builds/wave3-bake7` = **0**. `iron_bastion` and `player_outpost` also carry no
`raidDress` block at all, so `CaseSinglePropAuthority` skips them by construction.

## F. Gate evidence owed by this lane

- `python tools/gate_brace.py <the three .cs>` -> `GATE_BRACE_SUMMARY bad=0 of 3`, exit 0.
- NUL scan: 0 embedded NUL bytes in each of the three files; raw brace counts balanced
  (139/139, 48/48, 29/29).
- No `.unity`, no `.asset`, no `.json` touched. No `System.Reflection` introduced.
- **Unity was NOT run.** The compile gate and the regression run are the lead's step, and no gate
  marker string is claimed or reproduced anywhere in this lane's output. §C says what the raid-base
  suite will report until the JSON token goes.

## G. Files changed by this lane

```
M Assets/Editor/WallTools/RaidBaseDresser.cs
M Assets/Editor/Regression/RaidBaseLayoutRegression.cs
M Assets/_Modules/Village/World/SceneConfigCatalog.cs
M WorkOrders/WORK_ORDER_1635_retire_legacy_props_set_and_stale_dresser_canon.md          (Status flip)
M WorkOrders/WORK_ORDER_1635_retire_legacy_props_set_and_stale_dresser_canon.RESULT.md   (this section)
```

## H. What remains open on WO-1635 after this lane

1. **Acceptance #1, JSON half + #5** — `fortified_garrison`'s `props.set` token (§C, §D). Owner: the
   lane holding `scene-configs.json`.
2. Nothing else. #2 and #3 are delivered here; #4 and #6 were delivered by lane PROPS-CANON at
   `b10cd6783`.

⚠ Per §2.4 above, the flipped Status string leads with `IMPLEMENTED`, so `tools/board_build.py` renders
WO-1635 in the **Done** bucket while §C's one item is still open. The lead ordered that exact string;
the `⚠` banner sits directly under it so a human reader hits the caveat first.
