# WO-1764 RESULT — troops prefer walls over nearby targets on Iron Bastion

**Status of the work:** IMPLEMENTED, NOT YET GATED (single-seat lane; the lead gates the combined tree
and is the sole committer). **No Unity run, no commit, no push by this lane.**
**Ticket:** `WorkOrders/WORK_ORDER_1764_troops_still_prefer_walls_over_nearby_targets_bastion.md`
**Date:** 2026-09-16 · **Lane:** Combat/AI (`Assets/_Modules/Village/Troops/**` + one regression file)

---

## 1. The owner ruling this implements, verbatim

> ## ⭐ 2026-09-16: ***"Units first even inside Breach"***
> — any **reachable** defender beats the wall, even under a Breach order; walls only when **no**
> defender is reachable.

This **reverses** the 2026-09-15 reading WO-1746 shipped (only aggro breaks an armed stance). It is a
reversal after a felt-test, not a paraphrase correction, and it is recorded that way. WO-1738 got a
dated `⚠ SUPERSEDED IN PART 2026-09-16` banner at the top rather than a body rewrite (CLAUDE.md §15).

---

## 2. Files changed (4) — every one brace-balanced and NUL-clean

| File | Change |
|---|---|
| `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` | + `StanceOutranksReachableUnit` (the ONE ruling guard), + `RouteDetourFactor` (moved in from TroopController), + `RouteDetourSlackMeters`, + `RouteToUnitOpen`, + `NonWallStructSurvives`; both stance gates re-pointed at the guard; the "one place a reading was chosen" remark marked RULED |
| `Assets/_Modules/Village/Troops/TroopController.cs` | local detour const deleted (one home); `RefreshRouteToUnit` calls the new rule and records `len/straight/refused`; the existing scan pass now also tracks nearest **wall** vs nearest **non-wall**; the unconditional `nearestOtherStruct = shared;` overwrite replaced by a `wallFocus` candidate + the arbiter; `routeUnit=[…] structSrc= sweepWall= sweepNonWall=` on the `[Flow:RaidAI]` line; three now-false comments retired |
| `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` | the §4 rider string (it printed the retired 10% wall multiplier) + two stale remarks in the same file |
| `Assets/Editor/Regression/WallBreachOrderRegression.cs` | cases 8 and 15 **inverted (rewritten, not deleted)**; cases **17 (D1)**, **18 (D3)**, **19 (wiring + rider)** added; header case list and Case 8's red proof corrected |

**Not touched, as instructed:** `SmartMobileCamera.cs`, `HeroTargetIndicator.cs`, any `.unity`,
`DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`. No F8 ack.

### DataRegression registration line — NONE REQUIRED
The new cases extend the existing `[wall-breach-order]` suite, which is already registered at
`Assets/Editor/Regression/DataRegression.cs:1505`:
```
DeNelle.Core.Diagnostics.Guard.Try("Regression", "wall-breach-order suite", () => { if (!DeNelle.Editor.WallBreachOrderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wall-breach-order] " + r); });
```
No new suite, so **the suite count does not move** and `DataRegression.cs` needs no edit. (Same choice
WO-1746 made, for the same reason: one rule, one marker.)

---

## 3. Gate evidence this lane CAN produce

```
$ python tools/gate_brace.py Assets/_Modules/Village/Troops/TroopController.cs \
    Assets/_Modules/Village/Troops/RaidAssaultAi.cs \
    Assets/_Modules/Village/Troops/TroopBreachOrder.cs \
    Assets/Editor/Regression/WallBreachOrderRegression.cs
GATE_BRACE_SUMMARY bad=0 of 4      (exit 0)
```
Raw brace + NUL scan (both halves of CLAUDE.md §1, including the WO-434 NUL guard):
```
TroopController.cs              open 279  close 279  NUL 0
RaidAssaultAi.cs                open  39  close  39  NUL 0
TroopBreachOrder.cs             open  19  close  19  NUL 0
WallBreachOrderRegression.cs    open  97  close  97  NUL 0
```
⛔ **`COMPILE_GATE_OK` / `REGRESSION_OK` are NOT claimed.** No Unity was launched (single-seat rule).
The suite cases are written but **have never been executed**, so they are a claim, not a fact, until the
lead's gate prints `WALL_BREACH_ORDER_OK` inside a fresh `REGRESSION_OK <n>/<n>` run.

---

## 4. Proving lines — from the build the owner actually played

Source: `logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt`
(377 MB / 3.16 M lines; **grep only**; in `logs/device/`, excluded from commits — cite, never copy).
Build stamp in the log: **`2026.09.16.371701`**. Raid: `09-16 13:24:28 RAID START config='iron_bastion'`.

**D2 — `:3047672` + `:3047673`** (one 3 ms window): `breachStance=True stanceYield=wall … bucket=2
has[unit=True,obj=False,wall=True]`, paired with `won='Wall_Keep1_SS_0(WallSegment)' dist=1.3m |
runnerUpOtherKind='RaidGuard (hollow-warrior-Lv7-16)' dist=10.1m | accepted[unit=1,struct=9] |
preferUnit=False route=PathComplete-detour`. The stance held the panel with a live defender 10.1 m away
whose **complete** route was refused as a detour. Needs **both** D1 and D2 to flip — which is why both
are in this change.

**D3 — `:3053030` + `:3049493`**, 242 selector lines of the same shape:
`IDLE/RALLY … accepted[unit=0,struct=11] … nearestHostileAnyKind='Watchtower_Mage_1(DefenseTower)'
@7.4m … action=stand-still` against `bucket=-1 mayWall=False otherStructIsWall=True
has[unit=False,obj=False,wall=True]`. Exactly the D3 row in the ticket's §5 verdict table.

**The measured Bastion detour — `:3047672`:** `routeObj=detour:156.5/33.3` → excess **123.2 m**,
ratio **4.7x**. A keep-ring crossing, and the anchor for "must stay refused".

**Census (whole run):** `bucket=-1` 244 · `bucket=0` 135 · `bucket=1` 118 · `bucket=2` 27 ·
`stanceYield=wall` 27 · `stanceYield=unit` 23 · `route=PathComplete-detour` 17 of 122 answered queries.

---

## 5. The D1 arithmetic, in one paragraph

The ratio's **shape** is inverted relative to the failure it guards. Steering is a straight-line
`_agent.Move(displacement)` (no `SetDestination` anywhere in the file), so grinding is a function of the
**absolute** extra walking — yet the ratio grants 2.85 m of excess at a 5.7 m straight line and 14.2 m
at 28.3 m. The fix is an absolute budget **OR**ed with the old ratio, so it is widening-only:
`RouteDetourSlackMeters = 8f`. Bastion geometry, read at source: outer ring side **108 m** with ONE
south gate, keep ring side **48.6 m** (54 × 0.45 = 24.3 half-extent) with ONE **north** gate, panels
≤ **3.0 m** (`scene-configs.json` id `iron_bastion`; `RaidBaseGenerator.cs:720/740/741`). A convex
go-around costs ≤ `0.57·d` per obstacle → ~5.4 m for two panels plus agent inflation; a **ring
crossing** costs ~194–216 m derived and **123.2 m measured**. 8 m sits above the first and ~15× below
the second. Only the **unit** probe is widened; `RefreshRouteToObjective` keeps the bare ratio because
it feeds `ResolvePhase` (D4 is a separate axis), and case 19 asserts that it still does.

---

## 6. ⛔ What I could NOT prove — every one of them

1. **`COMPILE_GATE_OK`, `REGRESSION_OK`, `WALL_BREACH_ORDER_OK`** — not run. Nothing here is compiled.
   The three source-text assertions in case 19 match the code by inspection only.
2. **Which commits build `2026.09.16.371701` carries.** `git log` was not queried for that stamp. What
   the log itself proves is used instead: `PathPartial-arrived` present ⇒ WO-1730's arrival rule is in;
   `breachStance=` present 742× ⇒ WO-1746 + WO-1752 are in. The ticket's §1 table still says `371627`
   and that table is now **stale**, flagged in §0-NEW.a rather than rewritten.
   *Cheapest way to close it (not run by this lane):* `git log --oneline -S"371701" -- ProjectSettings`
   or `git log -p --follow ProjectSettings/ProjectSettings.asset | grep -n "371701"` to find the commit
   that stamped that `bundleVersion`, then `git diff <that commit> -- Assets/_Modules/Village/Troops/`.
3. **That the 09-14 §2A footman line flips.** Neither capture prints a unit route length; its excess is
   known only to be `> 7.2 m`. The new `routeUnit=` token exists precisely to close this.
4. **`RouteDetourSlackMeters = 8f` is DERIVED, NOT MEASURED.** The convex term uses the *authored* 3.0 m
   module cap. No tower-base footprint and no baked agent radius were measured on
   `RaidBase_IronBastion`.
5. **That a promoted tower / slack-opened unit is actually reachable by straight-line steering.** This is
   the residual WO-1438 risk and it is **Q2, still open**. Falsifier named in the WO: `moved=` vs
   `commanded=` on the paired `[Flow:TroopAI]` line.
6. **Applying D3's geometric test with the stance ON is a reading from the lead's brief, not an owner
   ruling.** Flagged in the code and the WO, not buried.
7. **Case 4's four-troop fixture does not model the stance** (it calls the 6-arg `PickBucket`, stance
   false), so the ruling flip is invisible to it. Left alone deliberately — widening that harness was
   not in scope. Noted so the next seat knows the gap is known.
8. **No felt verification.** Per §13 the PO closes, not this lane.

---

## 7. What the lead / the next device pull must do

1. Gate the combined tree once: `COMPILE_GATE_OK` + a fresh `REGRESSION_OK <n>/<n> suites` containing
   `WALL_BREACH_ORDER_OK` (marker, never the exit code).
2. Commit by explicit path — the four files above **plus** the WO Status flip and WO-1738's banner in
   the same commit; regenerate `BOARD.html` (`python tools/board_build.py`).
3. Ship it to the Seeker through the sanctioned script (§16 — `tools/r2-ship.ps1` is called by the
   chain; never raw `adb install`).
4. One Iron Bastion raid, then `adb logcat -d > logs/device/pull-<ts>-bastion-1764-verify/logcat_full.txt`
   and grep `\[Flow:RaidAI\] id=.*routeUnit=`. The four reads that close the ticket are listed in the
   WO's §12. **Re-derive `RouteDetourSlackMeters` from the `excess=` numbers** and replace §10A's
   authored-geometry derivation with the measurement.
5. Owner felt-verifies and closes.
