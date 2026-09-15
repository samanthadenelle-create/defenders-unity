# WO-1756 RESULT — a wall is never AUTO-acquired; the player selects it

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** edit-only (no Unity, no gate, no build, no git — the lead holds the Unity lock and is sole committer)
**Date:** 2026-09-15

## Owner ruling implemented
> ***"i auto target the wall, i should need to select it"***

## Files touched (2, both in the assigned silo)
| File | What |
|---|---|
| `Assets/_Modules/Village/Hero/HeroTargetIndicator.cs` | the refusal, the pure predicate, the traces, three falsified comments corrected |
| `Assets/Editor/Regression/HeroUnitOverWallTargetingRegression.cs` | the two ticket cases + the selection-survives pins |

No `.unity` file touched. No other `.cs` opened for edit. None of the off-limits files
(`RaidAssaultAi.cs`, `TroopController.cs`, `HeroHealth.cs`, `SmartMobileCamera.cs`,
`RaidUntexturedCensus.cs`, `BackendRequestSigner.cs`, `DeNelle.Core.Web3`) was read or written.

## The change, by line (line numbers read at source after the edits)

### 1. The rule is one PURE static — `HeroTargetIndicator.AutoAcquireAdmits` (`:1293`)
```
public static bool AutoAcquireAdmits(bool isWall, bool isStructure, bool unitsOnly)
{
    if (isWall) return false;                      // WO-1756: never auto-acquired, either pass
    if (unitsOnly && isStructure) return false;    // WO-1734: the unit-first pass
    return true;
}
```
WO-1734's `if (unitsOnly && cand is IDamageableStructure) continue;` was FOLDED INTO it rather than
deleted — same classifier, same behaviour for every non-wall. Pure and static so the regression pins
the rule by arithmetic, with no live hero, wall or camera.

### 2. The single call site — `NearestCandidateOfClass` (`:1453`)
```
bool candIsWall = cand is WallSegment;
if (!AutoAcquireAdmits(candIsWall, cand is IDamageableStructure, unitsOnly)) { ...trace...; continue; }
```
It sits in the exact slot WO-1734's `continue` occupied — BEFORE the R2 range gate, in front of an
otherwise byte-unchanged nearest-wins body. `WallSegment` (`Assets/_Modules/Village/Walls/WallSegment.cs:58`)
is in the same namespace and assembly (`DeNelle.Village`), so this is a direct type test, no reflection.

### 3. The refusal trace (`:1463-1476`)
`[Flow:Reticle] WALL REFUSED for auto-acquire - '<name>' at <d>m is SELECT-ONLY (WO-1756 ...)`.
**Throttled, not `Step`**, and only the FIRST wall of a pass builds a string: `FlowTrace.Throttle`
takes an already-concatenated message, so the caller pays the allocation even on a throttled frame,
and a raid puts dozens of panels in acquire range every LateUpdate (CLAUDE.md §12, memory
`logcat-ring-buffer-destroys-evidence`). The rest ride a per-call counter, `_wallsRefusedThisPick`
(`:380`, reset at `:1227`).

Two existing traces were corrected rather than left to lie:
- `AUTO PICK 'none' WHY=` (`:1240-1247`) now reads `N wall(s) REFUSED (WO-1756: walls are select-only, the
  player must tap one); no other acquirable hostile in the engage arc` — so an empty reticle SAYS why.
- `auto-acquire HELD:` (`:1486-1495`) now says *nearest AUTO-ACQUIRABLE hostile* and appends the refused
  count. Without that, a refused wall at 2 m plus a mob at 12 m read as a range bug that is not there
  — the exact false-hunt WO-1734's own comment warns about.

## ⭐ How a player-SELECTED wall survives (the part this ticket exists for)

**The two paths do NOT share a predicate today, and did not before — so nothing had to be separated.**
The refusal lives on the auto branch only, by construction. Proven at source, four links:

1. **`:607` — `CurrentTarget = _locked ?? NearestCandidate()`.** `??` short-circuits. While a tap-lock
   lives, `NearestCandidate()` → `NearestCandidateOfClass` → `AutoAcquireAdmits` **is never executed**.
   The auto rule cannot refuse what it is never asked about.
2. **`:594` is the ONLY per-frame validation of `_locked`** — LateUpdate's three-place clear allow-list:
   `!_locked.IsAlive || !_candidates.Contains(_locked)`. It consults `_candidates`, built by
   `RebuildCandidates` (acquire range + faction + line-of-sight) — **untouched by this WO**, so a
   `WallSegment` is still a full member of that list and a held wall still passes every frame.
3. **`:802` — `_locked = d`** comes from `TryLockAtScreenPoint` → `PickEnemyAtScreenPoint` (a raycast),
   never from the selection body. The tap sets the lock directly.
4. **`IsStillAutoAcquirable` (`:1351`) has exactly one caller** — `:1381` inside
   `ApplyAutoSwitchHysteresis`, whose one caller is `:1234` in `NearestCandidate()`. It is applied to
   `_autoPick`, **never** to `_locked`. Auto-only, and after this change `_autoPick` can never be a
   wall anyway.

**What I changed so it stays that way:** the rule was given its own named, pure seam instead of being
inlined, and the regression pins that `AutoAcquireAdmits(` appears in source **exactly twice** —
the definition and that one call. A third reference fails the suite. That is the guard against a
future edit quietly wiring the auto class-rule into the hold path, which is the only way a selected
wall could ever be dropped.

`IsStillAutoAcquirable`'s doc comment used to claim it answers *"would cand still be ACQUIRED by
NearestCandidateOfClass right now"*. That is no longer word-for-word true (the selection now also
applies a CLASS gate this method deliberately does not re-apply). It is moot, not a hole — but it is
now written down plainly at `:1330-1339`, with an explicit ⛔ against "fixing" it by calling
`AutoAcquireAdmits` there.

## ⚠ What I deliberately left AUTO-acquirable (the owner ruled on WALLS, not on structures)
- **Towers, gates and the raid spire.** All are `IDamageableStructure` but **not** `WallSegment`, so
  they are still auto-acquired on the all-classes fallback, exactly as before, behind the unchanged
  WO-1734 unit-first gate. Widening WO-1756 to every structure would leave the hero unable to
  auto-engage the raid objective — a regression case now FAILS if someone widens it.
- **`CycleTarget` (`:838-865`, the Tab/shoulder cycle) — deliberately UNFILTERED.** It steps one
  target per press and the player sees what she landed on, so it IS selecting; it is also the only
  non-raycast route to a wall. Filtering it would leave a wall reachable only by a raycast tap, a
  worse trap than the one this WO fixes. (Coordinator ruling, 2026-09-15.)

**`EngageLock(null)` was NOT left — it was closed. See below.**

## Regression — `HeroUnitOverWallTargetingRegression`
Pure cases (no scene needed):
- a wall is refused by the **all-classes fallback** — the ticket's headline, "only walls in range → NO target";
- a wall is refused by the unit-first pass too (neither branch can pick one);
- **scope guard:** a non-wall structure is still ADMITTED on the fallback (fails if WO-1756 is widened);
- WO-1734 unchanged: the unit-first pass still refuses a structure; a hostile unit is admitted by both.

Source-shape pins (the file's own honest pattern for a seam that needs a live scene):
- the selection routes through `AutoAcquireAdmits(candIsWall, cand is IDamageableStructure, unitsOnly)`
  and `cand is WallSegment` (the old `unitsOnly && cand is IDamageableStructure` pin was RETIRED —
  it pinned a line this WO folded into the predicate; its sibling failure message
  *"walls may no longer be targetable"* was also reworded, since that is now the design);
- **the selection-survives proof:** `CurrentTarget = _locked ?? NearestCandidate()`,
  `!_locked.IsAlive || !_candidates.Contains(_locked)`, and `AutoAcquireAdmits(` count == 2;
- the `WALL REFUSED` trace exists and is `Throttle`d (§12 permanence + no per-frame flood).

All 11 pinned strings were verified to resolve against the edited `HeroTargetIndicator.cs`.

## Proven vs unproven
**Proven this session:**
- Brace balance by the gate's own rule: `python tools/gate_brace.py` on both files →
  `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0. Raw counts: indicator 136/136, regression 7/7.
- NUL bytes: 0 in both files (byte scan).
- Every source string the regression pins exists verbatim in the edited file; `AutoAcquireAdmits(`
  occurs exactly 2×.
- `WallSegment` is the only wall damageable: `grep -rn "class .*Wall" Assets/_Modules --include=*.cs`
  returns one `IDamageable` wall type (`WallSegment.cs:58`). `DungeonWall` is a plain layout data
  class, not a MonoBehaviour/IDamageable. So `is WallSegment` cannot miss a raid wall.
- No sibling suite pins a string I rewrote: the only other Editor suites naming `HeroTargetIndicator`
  are `RangedFacingLockRegression` (pins `TryGetRangedPrimary` / `SetLockFace` / `CurrentTarget`, and
  BANS hardcoded hero-class names — `WallSegment` is not one), `StructureTargetableRegression` (wall
  LAYER/faction, behavioural), `FrameBudgetMeasureRegression`, `RangedPrimaryRegression`,
  `BreakableContainerChestRegression`, `ShaderPinRegression` — none pin a line this WO edited.

**NOT proven (the lead must gate):**
- **It has not compiled.** No `COMPILE_GATE_OK`, no `REGRESSION_OK` — this lane ran no Unity by order.
- The headless acceptance criterion ("with a wall the nearest hostile and no units, auto-acquire
  returns nothing") is proven only at the PURE-predicate level, not through a live
  `NearestCandidateOfClass` with a real candidate list. The pure case is the same code the game runs,
  but it is not an end-to-end run.
- The device criterion ("the owner's reticle never lands on a wall she did not tap") is felt-test only
  and is the PO's to close.
- The new suite case needs no new `DataRegression` registration — `HeroUnitOverWallTargetingRegression`
  is already registered; I extended it in place. **I did not open `DataRegression.cs` to re-confirm
  that registration this session** — the lead should eyeball it at gate time.

## Board
- WO Status line flipped to `**Status:** IMPLEMENTED, NOT YET GATED`:
  `D:\EoA\WorkOrders\WORK_ORDER_1756_hero_auto_targets_walls_selection_only.md`
- This result file: `D:\EoA\WorkOrders\WORK_ORDER_1756_hero_auto_targets_walls_selection_only.RESULT.md`
- `BOARD.html` NOT regenerated (no git/tooling runs in this lane) — the lead regenerates and commits
  the flip in the same commit as the work.

## Addendum — `EngageLock(null)` closed (coordinator ruling, same session)

`EngageLock(null)` is **a button that picks FOR her**, so the wall ruling binds there too. It used to
take `target = _candidates.Count > 0 ? _candidates[0] : (_locked ?? CurrentTarget);` flat — and with
the garrison dead index 0 is a wall panel, i.e. the owner's complaint in a different hat.

**`:143-164`** now walks `_candidates` (already sorted nearest-first) and takes the **first ADMITTED**
one, **reusing the same predicate** — `AutoAcquireAdmits(c is WallSegment, c is IDamageableStructure, false)`
(`:154`). The signature fit as-is; no second copy of the rule was written. `unitsOnly: false` because
an engage press is not the WO-1734 unit-first pass — it may still take a tower, a gate or the raid
spire, exactly as it always could. Only `WallSegment` is skipped. The loop also carries the same
Unity-aware null/alive skip the selection body uses.

**Only walls in range — what it does (constraint 1):** the loop admits nothing, so `target` stays
null and `:164` degrades to `_locked ?? CurrentTarget` — the player's own prior pick, or the auto
reticle's pick (which after WO-1756 can never be a wall). If both are null, `target` is null and the
existing guard at `:166` (`if (target == null || !target.IsAlive) return;`) **returns**: the engage
press is a silent no-op — no lock, no index-0 fallback, **no throw**. It never picks masonry.

**Pin count is now THREE, not two (constraint 2):** definition + `NearestCandidateOfClass` +
`EngageLock`. The regression's expected number was updated in the same edit, so a stray **fourth**
reference still fails — the guard against the rule leaking into the manual hold is intact.

Regression additions:
- pure: an ENGAGE PRESS over a list of wall panels admits none, so the press finds no target;
- source: the flat `_candidates[0]` take is **absent**; `AutoAcquireAdmits(c is WallSegment, ...)` is
  present (proves reuse, not a copy); the `if (target == null) target = _locked ?? CurrentTarget;`
  degrade is present; `_locked = _candidates[idx];` is still present, pinning that `CycleTarget`
  remains unfiltered.

Re-verified after this edit: `gate_brace.py` both files → `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0;
NUL 0 in both; `AutoAcquireAdmits(` count == 3; the flat `_candidates[0]` string is gone. Still
**uncompiled and ungated** — no Unity was run.
