# WO-1764 — Troops still take walls / the spire over nearby hostiles in the Iron Bastion raid

**Status:** IMPLEMENTED, NOT YET GATED
**Silo:** Combat/AI — `Assets/_Modules/Village/Troops/` only (CLAUDE.md §9 Combat/AI lane)
**Raised by:** owner felt report, 2026-09-16, Seeker, APK `2026.09.16.371627` (built from `f74bf0829`)
**Opened by:** read-only RCA lane, 2026-09-16
**Number:** PRE-ASSIGNED by the lead. `CLI_LANES_WO_NUMBERS.md` was NOT touched by this lane.

---

## 0-NEW. ⭐ THE DEVICE CAPTURE ARRIVED, AND THE OWNER RULED. §0 BELOW IS KEPT AS THE RECORD.

**Everything in §0 was true when this ticket opened and is now superseded by two events, both dated
2026-09-16.** §0 is deliberately NOT rewritten — it is the reason the instrumentation in §10 exists.

### 0-NEW.a — The capture (the build she actually played)
`logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` — 377 MB / 3.16 M lines, pulled
after the owner plugged the Seeker in. **grep it, never open it whole. It is in `logs/device/`, which
is excluded from commits (Firebase tokens): cite line numbers, never copy the file.**

* `09-16 13:24:28 [Flow:Raid] RAID START config='iron_bastion' garrisonAlive=23 enemyLevel=7 spire=3500hp`
* Build on device = **`2026.09.16.371701`**, NOT the `371627` §1 names. ⚠ Which fix commits `371701`
  carries has **NOT** been established by this lane — `git log` was not queried for that build stamp,
  and the lane does not claim it. What the log itself proves is stronger than a commit list and is
  used instead: `routeObj=PathPartial-arrived` appears, so **WO-1730's arrival rule IS in this build**;
  `breachStance=` appears **742** times, so **WO-1746 and WO-1752 are in it**.
* Census over the whole run: `bucket=-1` **244**, `bucket=0` 135, `bucket=1` 118, `bucket=2` 27;
  `stanceYield=wall` **27**, `stanceYield=unit` 23; `route=PathComplete-detour` **17** of 122 answered
  route queries; `route=PathComplete` 29.

**D2 CONFIRMED LIVE — `:3047672` + `:3047673`, one 3 ms window:**
```
[Flow:RaidAI] id=troop-footman job=Front phase=Breach ... bucket=2 preferUnit=False
  breachStance=True stanceYield=wall blocked=True wallDmgMult=1.00 siege=False
  mayWall=True otherStructIsWall=True has[unit=True,obj=False,wall=True]
[Flow:TroopAI] id=troop-footman RETARGET#1 -> won='Wall_Keep1_SS_0(WallSegment)' kind=struct dist=1.3m
  | runnerUpOtherKind='RaidGuard (hollow-warrior-Lv7-16)(EnemyDamageable)' dist=10.1m
  | sweep colliders=10 accepted[unit=1,struct=9] ... | preferUnit=False route=PathComplete-detour
```
The stance held the panel with a live defender 10.1 m away and a **complete** route to it refused as a
detour. That single pairing needs **both** D2 and D1 to flip, which is why both are implemented.

**D3 CONFIRMED LIVE — the exact signature §5's verdict table asks for, 242 selector lines:**
```
:3053030  [Flow:TroopAI] id=troop-footman IDLE/RALLY: no acquirable hostile inside radius=14.0m
           (last sweep colliders=12, accepted[unit=0,struct=11], rejected=1;
            nearestHostileAnyKind='Watchtower_Mage_1(DefenseTower)' @7.4m) action=stand-still
:3049493  [Flow:RaidAI] ... bucket=-1 mayWall=False otherStructIsWall=True
           has[unit=False,obj=False,wall=True]
```
A footman **standing still** with a `DefenseTower` **7.4 m** away — a target WO-1752 explicitly keeps
legal for a stance-less troop. ⚠ And the tick before (`:3053025`) reads
`nearestHostileAnyKind='Wall_Keep1_SS_3(WallSegment)' @7.1m`: the refused wall is **0.3 m nearer than
the tower**, which is why the D3 fix needed a second clause and not only a distance test (see §10C).

**D1's own numbers are still NOT in this log, and that is the finding that shaped the fix.** Every
`route=PathComplete-detour` line carries the verdict and **no lengths**, on both captures. The one
measured detour pair in the whole run is the OBJECTIVE probe — `routeObj=detour:156.5/33.3` (`:3047672`),
excess **123.2 m**, ratio 4.7x — which is a keep-ring crossing and is used in §10A as the "must stay
refused" anchor.

### 0-NEW.b — The ruling (Q1 is CLOSED)
> ## ⭐ OWNER RULING, 2026-09-16, VERBATIM: ***"Units first even inside Breach"***
> — any reachable defender beats the wall, **even under a Breach order**; walls only when no defender
> is reachable.

This reverses the 2026-09-15 reading WO-1746 shipped and WO-1738's status line spent a paragraph
retiring. It is a **reversal after felt-testing, not a paraphrase correction** — recorded as such, with
a dated `⚠ SUPERSEDED IN PART 2026-09-16` banner added at the top of
`WorkOrders/WORK_ORDER_1738_wall_durability_concurrency_fork.md` rather than a rewrite of its body (§15).

**Q2 (§6) REMAINS OPEN and nothing here presumes an answer.**

---

## 0. WHY THIS IS `NEEDS DATA` AND NOT `READY TO IMPLEMENT` — read this first
*(kept verbatim as the record; superseded by §0-NEW above)*

**There is NO capture of the build the owner played, and there is no capture of ANY build that
carries the shipped 1746/1752 selector.** Proven, not assumed:

* The device is unplugged today — no logcat, no F8 bridge, no screencap from the 09-16 session.
  Nothing in `logs/f8-inbox/` or `logs/device/` postdates 2026-09-15 22:04.
* `grep -rl "breachStance=" logs Builds` returns **exactly one file**: `Builds/wo1730-assault-trace.log`
  (2026-09-15 21:46, headless `RaidAssaultTraceCapture`). That is the ONLY log in the repo carrying
  the WO-1746/1752 trace tokens, and it is **not probative**: it sampled 4 troops on
  `RaidBase_raider_camp_small`, every one of them at the deploy point with an empty candidate set —

  ```
  [Flow:RaidAI] id=troop-footman job=Front phase=Breach in[peel=False, routeOpen=False, objInRange=False]
    routeObj=PathPartial routeGap=[last=4.7 straight=48.1 corners=4] bucket=-1 preferUnit=False
    breachStance=False stanceYield=n/a blocked=True wallDmgMult=0.00 siege=False mayWall=False
    otherStructIsWall=False has[unit=False,obj=False,wall=False]
  ```
  `has[unit=False,obj=False,wall=False]` on every sampled line — no unit, no wall, no objective in
  any sweep. A selector cannot be judged on a tick where it had nothing to select between.
* The newest **device** raid data is `logs/device/post-lane-a-370139/logcat_full.txt`, spanning
  `09-14 17:52:04.865` → `09-14 20:25:06.061`, version string `2026.09.15.370139`.
  `grep -c breachStance` on that file = **0**, so it **predates WO-1746** (landed `10c21b9bd`,
  09-15 12:23) and WO-1752 (`d5b0221bb`, 09-15 15:01). It IS however an
  **`RaidBase_IronBastion`** session (936 occurrences of that scene name) — the same scene as
  today's report — so it is the correct pre-fix baseline and every proving line below comes from it.
* `logs/f8-inbox/capture-device-*.md` for 09-15 contain **zero** raid-AI lines, and that is
  structural, not bad luck: those files are rendered from `break-log.jsonl`, which holds
  **errors/exceptions/softlocks only**. `[Flow:RaidAI]` is a `FlowTrace.Throttle` **Step**
  (`TroopController.cs:1219`) and therefore exists **only in logcat**. **F8 alone will never carry
  the line that settles this ticket** — see §5.

So: this ticket names **four located mechanisms with file:line**, **two of them measured on Iron
Bastion**, and the one line the next capture must contain to choose between them. Per CLAUDE.md §12,
static reading LOCATES; it does not conclude. Nothing below is offered as the proven root cause.

---

## 1. The report

Owner, 2026-09-16, Iron Bastion raid: troops are *"still not finding the non-wall targets first"* —
they go for walls before nearby hostiles / structures.

**What is in the APK she played** (lead-verified, `git show --stat`, dates re-read this session):

| Commit | Ticket | In `f74bf0829`? | Date |
|---|---|---|---|
| `10c21b9bd` | WO-1746 units-first outside Breach + Breach as a persistent auto-chaining stance | **YES** | 09-15 12:23 |
| `d5b0221bb` | WO-1752 troops NEVER attack walls without Breach; hero-attacker adoption | **YES** | 09-15 15:01 |
| `06da9b7b6` | WO-1734 occluder fade + hero targets units over walls | **YES** | 09-15 10:12 |
| `ba13b93ef` | WO-1730 route-open-to-spire (arrival radius) | **NO** | 09-15 22:02 |

The working tree's `RaidAssaultAi.cs` / `TroopController.cs` are **clean** (`git status --short` on
`Assets/_Modules/Village/Troops/` shows neither file modified), and
`git diff --stat f74bf0829 HEAD` on that folder is exactly `ba13b93ef`'s 249 lines. **So
worktree = APK + WO-1730, and nothing else.** Line numbers below are worktree unless stated.

---

## 2. Proving lines (all from the Iron Bastion session, 09-14, PRE-fix baseline)

### 2A. ⭐ A reachable hostile was refused as a DETOUR and a wall 8.7 m NEARER won
`logs/device/post-lane-a-370139/logcat_full.txt:145831`, `09-14 20:11:06.580`:

```
[Flow:TroopAI] id=troop-footman role=melee RETARGET#1 reason=foe-null dropped='<none>'
 -> won='Wall_Keep1_SE_15(WallSegment)' kind=struct dist=5.7m
 | runnerUpOtherKind='RaidGuard (hollow-warrior-Lv7-16)(EnemyDamageable)' dist=14.4m
 | sweep colliders=13 accepted[unit=2,struct=11] rejected=0 radius=16.4m preferStruct=False
 | preferUnit=False route=PathComplete-detour
```

**`route=PathComplete-detour` is the load-bearing token.** The NavMesh returned a **complete** route
to that guard and the gate threw it away for being longer than `1.5x` the straight line.
`accepted[unit=2]` — two live hostiles were inside the sweep.

### 2B. ⭐ The same refusal handing the warband the SPIRE over a nearer guard
Same file, same session:

```
[Flow:TroopAI] id=troop-archer ... RETARGET#11 reason=timer
 dropped='RaidGuard (hollow-acolyte-Lv7-4)(EnemyDamageable)' -> won='RaidSpire(RaidSpire)' kind=struct dist=28.3m
 | runnerUpOtherKind='RaidGuard (hollow-acolyte-Lv7-4)(EnemyDamageable)' dist=20.9m
 | sweep colliders=18 accepted[unit=3,struct=15] rejected=0 radius=25.2m preferStruct=False
 | preferUnit=False route=PathComplete-detour
```

It **dropped the guard it was already fighting** and took the spire 7.4 m FARTHER away, with three
units accepted in the sweep. This is "not finding the non-wall targets first" in one line, and the
non-wall target it took instead is the objective, not a wall.

### 2C. Route-status census for the whole session (same file)

```
618  route=not-asked      80  route=PathComplete   26  route=PathComplete-detour
26   route=in-range       24  route=AUTHORED        5  route=RouteInfo    1  route=null
```

**26 of 106 answered route queries (24.5%) were complete routes refused as detours.** Each one is a
tick on which a reachable hostile lost to masonry.

### 2D. The shared wall focus is scene-wide, and the scene has 210 walls
```
09-14 20:11:06.579  [Flow:RaidAI] source=auto focus='Wall_Keep1_SE_15' hp=100 walls=210
09-14 20:11:24.085  [Flow:RaidAI] BREACH ORDER cleared (was 'Wall_Keep1_SE_15'): the ordered wall collapsed …
09-14 20:11:25.945  [Flow:RaidAI] source=auto focus='Wall_Keep1_SE_16' hp=83 walls=209
```
The auto-chain is visibly working (panel collapses → next panel picked with no second tap) and the
candidate set is **all 210 hostile `WallSegment`s in the scene**, not the troop's own sweep.

### 2E. ⛔ A line that is NOT evidence, recorded so the next seat does not misuse it
```
09-14 17:52:04.865 [Flow:TroopAI] id=troop-footman role=melee ENGAGED foe='Wall_Outer_SE_10(WallSegment)'
  kind=struct dist=32.0m attackRange=2.9m inRange=False moved=0.00m/s commanded=4.0 agent=onNavMesh retargets=19
```
A footman targeting a wall **32 m** away looks like the smoking gun for a distance bug. It is not:
`logcat_full.txt:6`, the same 135 ms window, reads
`[Flow:RaidAI] source=order focus='Wall_Outer_SE_10' hp=100 v=12 (player breach order OVERRIDES the most-damaged pick)`.
**The owner had tapped that panel.** Marching 32 m to an ordered wall is WO-1719 working as ruled.
The real defect visible in that line is `moved=0.00m/s` with `commanded=4.0` — a troop that is
**stuck, not mis-targeted**. Do not fold it into this ticket; raise it separately if it recurs.

---

## 3. The four located mechanisms, ranked by strength of evidence

### ⭐ D1 — THE DETOUR GATE (measured on Bastion; UNTOUCHED by every shipped fix)
**`Assets/_Modules/Village/Troops/TroopController.cs:213`**
```csharp
private const float RouteDetourFactor = 1.5f;
```
refused at **`TroopController.cs:1391-1392`**
```csharp
_routeToUnitOpen = len > 0f && straightLine > 0.01f && len <= straightLine * RouteDetourFactor;
if (!_routeToUnitOpen) _routeStatus += "-detour";
```
`_routeToUnitOpen` is the `routeToUnitOpen` argument to `RaidAssaultAi.PreferUnit`
(`TroopController.cs:1159-1161`). In the Breach branch (`RaidAssaultAi.cs:527-528`) a non-siege
troop with any structure in view needs `unitInAttackRange || routeToUnitOpen` to prefer a unit — so
**a detour verdict is a units-lose verdict**, and §2C says that is a quarter of all answered queries.

**Proven untouched by the build under test:** `git show <c> -- TroopController.cs | grep -c
"RouteDetourFactor\|RefreshRouteToUnit"` returns `0` for `10c21b9bd`, and for `d5b0221bb` /
`ba13b93ef` the single hit is the unchanged `RefreshRouteToUnit(...)` **call line in diff context**,
not an edit to the gate. `git log -S"RouteDetourFactor"` names `70812668e` (WO-1595) as the last
functional change. **So WO-1746, WO-1752 and WO-1730 all leave §2A and §2B behaving exactly as
captured.** This is the single best explanation of "*still* not finding them" — the word *still*.

The factor's own comment says it was *"Calibrated on the 09-06 capture, whose worst real route
measured 8.1/7.1 = 1.14x"* — calibrated on `raider_camp_small`-era geometry, never re-measured
against Iron Bastion's 210-panel double ring, where a legitimate route through an open gate or a
breach in a different face is inherently longer than the straight line through the masonry.

### D2 — THE BREACH STANCE IS DELIBERATELY WALLS-FIRST, AND WO-1746 FLAGGED THE DISPUTE ITSELF
**`RaidAssaultAi.cs:526`** (`if (breachStance) return false;` inside `PreferUnit`) and
**`RaidAssaultAi.cs:683`** (`if (!breachStance && hasUnit && unitInAttackRange) return 0;`).
Under an armed stance a *reachable, in-attack-range* defender is refused and the panel wins; only
`peelThreat` (aggro / hurt within 2.5 s / a unit inside 6 m) breaks it.

`TroopBreachOrder.StanceActive` = `_stanceArmed || HasOrder` (`TroopBreachOrder.cs:91`), and WO-1746
made it **persistent with auto-chain** — one tap arms it for the rest of the raid, and a collapsing
panel deliberately does NOT disarm it (`TroopBreachOrder.cs:71-90`). **If the owner tapped Breach
once in that Bastion raid, walls-first for the whole raid is the shipped design, not a bug.**

`RaidAssaultAi.cs:495-500` says so in its own words and marks it as the one contested reading:

> ⚠ THE ONE PLACE A READING WAS CHOSEN, FLAGGED RATHER THAN BURIED. WO-1738's status line
> summarises the ruling as "units-first is the default inside it", which would mean a reachable unit
> still wins under an armed stance. The ruling BODY and WO-1719 both say "until … a hostile pulls
> aggro", i.e. Peel. … flipping to the other reading is deleting the one `breachStance` line below.
> Pinned by WallBreachOrderRegression Case 8 so the flip cannot happen silently.

**The owner's felt report IS the other reading.** This is therefore an **owner ruling question, not
a code defect** — see §6 Q1. Flipping it is deleting `:526` and the `!breachStance` guard at `:683`,
and `Assets/Editor/Regression/WallBreachOrderRegression.cs` **Case 8 must move in the same edit** or
the gate goes red.

### D3 — THE SHARED WALL FOCUS DISPLACES A NEARBY NON-WALL STRUCTURE BEFORE THE SELECTOR SEES IT
**`TroopController.cs:1077-1079`** (in the APK: `f74bf0829:1042-1044`, verified by
`git show f74bf0829:… | grep -n`; `git diff f74bf0829 HEAD` shows **0** changes to these lines):
```csharp
IDamageable shared = SharedBreachFocus(muster);
if (shared != null && shared.IsAlive)
    nearestOtherStruct = shared;
```
`SharedBreachFocus` (`TroopController.cs:1400-1453`) builds its candidate set from
`Object.FindObjectsByType<WallSegment>` (`:1440`) — **walls only, scene-wide** — and the assignment
above is **unconditional**: it does not consult `breachStance`, distance, or whether this troop's own
sweep even contained a wall. Consequences, both located not proven:

* **Stance ON:** bucket 2 is the scene-wide most-damaged panel, so the nearer `DefenseTower` /
  `Gate` in the troop's own sweep can never be bucket 2. "Walls before nearby structures."
* **Stance OFF:** a nearby tower candidate is **overwritten by a wall**, so
  `otherStructIsWall` (`TroopController.cs:1168`) goes True, `mayWall` goes False, and the troop
  lands bucket **-1 — it stands**, with a shootable tower in range it will never pick.

⚠ That second case makes `RaidAssaultAi.cs:640-645`'s stated premise **already false upstream**:

> ⚠ AND NOT BY FILTERING THE WALL OUT OF THE CANDIDATE SET EITHER. If the wall were dropped
> upstream, the next-nearest masonry — a TOWER behind it — becomes bucket 2 … The wall stays the
> candidate, stays visible in the trace as has[wall=True], and is REFUSED here.

The tower is not "behind" the wall in the candidate set — it has already been **discarded** at
`:1079` before `PickBucket` is called. Whoever implements must read that comment against `:1077-1079`
and correct one of the two.

### D4 — WO-1730 (NOT in the APK): it changes the PHASE, which is `PickBucket`'s first argument
**Do not claim WO-1730 "fixes target preference" — it does not, and the owner's report is not about
route-open.** The link is indirect but real and is a code fact:
`RaidAssaultAi.ResolvePhase` (`:346-355`) returns `Breach` whenever `routeToObjectiveOpen` is false,
and WO-1730 is the ticket that finally lets it be true (`ArrivalRadius` / `RouteArrived`,
`RaidAssaultAi.cs:229-249`). In **Push/Finish** the bucket rule is different
(`RaidAssaultAi.cs:650-656`): objective → **unit** → wall, with no detour filter on the unit once
`hasObjective` is false. So landing the warband in Push/Finish changes the ordering for every troop.

**Unproven for Iron Bastion.** WO-1730's proof (`Builds/wo1730-assault-trace.log`,
`routeGap=[last=4.7 …]` vs a 4.30 m carve radius) was measured on `RaidBase_raider_camp_small`.
**Bastion's `routeGap last=` has never been captured on any log in this repo.** Treat WO-1730 as
*plausibly partially covering* the report and prove it on Bastion, do not assume it.

---

## 4. Candidate code sites (single table for the implementation lane)

| # | File:line | What it is |
|---|---|---|
| D1 | `Assets/_Modules/Village/Troops/TroopController.cs:213` | `RouteDetourFactor = 1.5f` |
| D1 | `Assets/_Modules/Village/Troops/TroopController.cs:1391-1392` | the detour refusal + `-detour` token |
| D1 | `Assets/_Modules/Village/Troops/RaidAssaultAi.cs:527-528` | `return unitInAttackRange \|\| routeToUnitOpen` |
| D2 | `Assets/_Modules/Village/Troops/RaidAssaultAi.cs:526` | `if (breachStance) return false;` |
| D2 | `Assets/_Modules/Village/Troops/RaidAssaultAi.cs:683` | the second stance gate |
| D2 | `Assets/_Modules/Village/Troops/TroopBreachOrder.cs:91` | `StanceActive = _stanceArmed \|\| HasOrder` |
| D3 | `Assets/_Modules/Village/Troops/TroopController.cs:1077-1079` | unconditional shared-focus overwrite |
| D3 | `Assets/_Modules/Village/Troops/TroopController.cs:1440` | `FindObjectsByType<WallSegment>` — walls only |
| D4 | `Assets/_Modules/Village/Troops/RaidAssaultAi.cs:650-656` | Push/Finish bucket order |

### Rider — a trace line that now lies (fix in the same edit, CLAUDE.md §12/§15)
`TroopBreachOrder.cs:101-106` still prints, on every disarm:
```
"ordinary troops go RELUCTANT again (10% on walls) unless an explicit order still stands"
```
WO-1752 **deleted** the 10% reluctant path (`RaidAssaultAi.cs:93-107`,
`WallDamageMultiplier` now returns 1 or 0). The next capture will print a number that no longer
exists, and `TroopController.cs:1197-1200` explicitly tells the next seat that *"a 0.10 token on a
fresh log is now proof the build predates this ticket"* — this string will forge that proof.
Repoint the message to "walls are OFF (0.00) for non-siege without the stance".

---

## 5. ⭐ THE CAPTURE INSTRUCTION — exactly what the next run must produce

**F8 will not carry it.** `logs/f8-inbox/capture-device-*.md` is rendered from `break-log.jsonl`
(errors/exceptions/softlocks). `[Flow:RaidAI]` is a `FlowTrace.Throttle` Step and lives **only in
logcat**. A raid that produces no error produces no F8 capture and no raid-AI evidence.

For the owner / the device lane, in this order:

1. Plug the Seeker in. `adb logcat -g` first and record the ring size (memory
   `logcat-ring-buffer-destroys-evidence` — the Flow firehose can evict the window).
2. `adb logcat -c`, then run **one Iron Bastion raid** on APK `2026.09.16.371627`.
3. **Note whether the Breach button was tapped, and when.** This single fact decides D2 vs D1/D3 and
   nothing in the log substitutes for it if the stance was armed before the log started.
4. Fight *beside a live defender with a wall nearby* for ~10 s, and separately stand near a
   `Watchtower_*` with Breach OFF for ~10 s.
5. Immediately: `adb logcat -d > logs/device/pull-<ts>-bastion-1764/logcat_full.txt`

**The one line that settles it** — grep `\[Flow:RaidAI\] id=.*breachStance=` and read these tokens
together on a single line (`TroopController.cs:1219-1237` emits all of them):

```
phase=  bucket=  preferUnit=  breachStance=  stanceYield=  mayWall=  otherStructIsWall=
routeObj=  routeGap=[last= straight= corners=]  has[unit=,obj=,wall=]
```

Paired with the adjacent `[Flow:TroopAI]` line for the same troop id:
`ENGAGED foe='…' kind= dist=`, `route=` (is it `-detour`?), `accepted[unit=,struct=]`,
`nearestHostileAnyKind='…'`.

**How the verdict is read:**

| Observation on the fresh log | Verdict |
|---|---|
| `route=PathComplete-detour` with `accepted[unit>=1]` and a struct winner | **D1 confirmed** — the detour gate is the live cause |
| `breachStance=True stanceYield=wall` while a unit is in attack range | **D2** — shipped-by-design; needs the owner's ruling (§6 Q1), not a fix |
| `has[wall=True] otherStructIsWall=True mayWall=False bucket=-1` while `nearestHostileAnyKind='Watchtower_…'` | **D3 confirmed** — the shared focus displaced the tower |
| `routeObj=` still `PathPartial` with `routeGap=[last=` a small number on Bastion | **D4** — WO-1730 will move the phase here too; deploy it and re-measure |
| `breachStance=False`, no `-detour`, units still losing | **none of the four** — a fifth mechanism; re-open with that line |

Two further facts already closed, so no one re-derives them: the only foe writers are
`TroopController.cs:709` and `:1970`, both `_cachedFoe = NearestHostile()` — **there is no
second/bypass targeting path** to rule out (`grep -rn "\.SetTarget(\|\.Foe *=\|ForceTarget"
Assets/_Modules/Village/Troops/*.cs` returns nothing). And the 32 m line is a player order (§2E).

---

## 6. Ruling needed from the owner before any edit

* ~~**Q1 (blocks D2).**~~ ✅ **RULED 2026-09-16 — see §0-NEW.b.** Verbatim: *"Units first even inside
  Breach"*. Implemented, and implemented as ONE guard
  (`RaidAssaultAi.StanceOutranksReachableUnit`, read at both stance gates rather than deleted at
  either) so a future re-ruling is one line. `WallBreachOrderRegression` Case 8 and case 15 were
  **rewritten**, not deleted, and each carries the history of the reading it used to pin.
* **Q2 (shapes D1).** With the detour gate relaxed, a troop may steer at a hostile it can only reach
  the long way round — WO-1438's frozen-on-a-navmesh-edge failure. Acceptable if it walks the long
  way (real pathing), not acceptable as a straight-line `Move`. Is a **pathed** approach to a
  detoured-but-reachable unit in scope for this ticket, or a follow-up?

---

## 7. What NOT to touch

* ⛔ **`Assets/_Modules/Village/Hero/SmartMobileCamera.cs` and
  `Assets/_Modules/Village/Hero/HeroTargetIndicator.cs`** — both belong to **WO-1765** (camera
  spin / over-the-shoulder), both touched by `06da9b7b6`. File-disjoint by construction: this
  ticket is `Assets/_Modules/Village/Troops/` only.
* ⛔ Do **not** re-introduce `ReluctantWallDamageMultiplier` or any non-zero wall damage for a
  stance-less non-siege troop — the owner deleted it on 2026-09-15 and the dead-end is her accepted
  cost (`RaidAssaultAi.cs:93-107`).
* ⛔ Do **not** filter the wall out of the candidate set upstream to "fix" D3 —
  `RaidAssaultAi.cs:640-645` records why (a tower behind an intact wall becomes bucket 2 and
  structures carry no reachability filter). Fix the **displacement** at `:1077-1079`, keeping the
  wall visible in the trace.
* ⛔ Do **not** delete or disable any `FlowTrace` call (CLAUDE.md §12) — the tokens in §5 are the
  only instrument this ticket has.
* ⛔ Do **not** touch `CLI_LANES_WO_NUMBERS.md` (number pre-assigned), and do **not** `f8-ack` any
  capture — the 266-deep backlog is the lead's.
* ⛔ No scene edits, no bake: `RaidBase_IronBastion.unity` is not in scope.
* Revisit WO-1730's deployment **before** rewriting the selector — it is already committed
  (`ba13b93ef`) and merely absent from the tester APK. Shipping it changes D4's input for free.

---

## 8. Acceptance criteria (for the follow-up implementation, once a verdict exists)

1. A fresh Iron Bastion device logcat contains a `[Flow:RaidAI] … has[unit=True,…]` line on which
   `bucket=0` where the pre-fix build read `bucket=2` for the same situation, quoted in the RESULT.
2. `route=PathComplete-detour` no longer appears with a struct winner while a live unit sits inside
   the sweep (D1), **or** the RESULT explains why that shape is still correct.
3. A regression case in `Assets/Editor/Regression/WallBreachOrderRegression.cs` pins the chosen
   Q1 reading, with Case 8 updated (not deleted) if the ruling flips.
4. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on fresh logs (markers, never exit codes).
5. The `TroopBreachOrder.cs:101-106` "10% on walls" string is corrected in the same commit (§4 rider).
6. Owner felt-verifies on device and closes (CLAUDE.md §13 — PO closes, not CLI).

---

## 9. Evidence index (everything cited, so nothing is re-derived)

| Source | What it proves |
|---|---|
| `logs/device/post-lane-a-370139/logcat_full.txt:145831` | §2A — complete route to a guard refused as detour, wall won |
| same file, `RETARGET#11` archer lines | §2B — spire at 28.3 m beat a guard at 20.9 m |
| same file, route-token census | §2C — 26/106 answered queries refused as detours |
| same file, `09-14 20:11:06.579` / `:25.945` | §2D — scene-wide auto focus, `walls=210`, auto-chain working |
| same file:`6` + `:2` | §2E — the 32 m wall was the player's own order |
| `Builds/wo1730-assault-trace.log` | §0 — the only post-fix trace; all candidates empty |
| `grep -rl breachStance= logs Builds` → 1 file | §0 — no device capture of the shipped selector exists |
| `grep -c breachStance` on post-lane-a → 0 | §0 — that session predates WO-1746 |
| `git show --stat` on the four commits; `git diff --stat f74bf0829 HEAD` | §1 — worktree = APK + WO-1730 |
| `git show <c> \| grep -c RouteDetourFactor` (0 / context-only) | §3 D1 — the detour gate is untouched by every shipped fix |
| `git show f74bf0829:…TroopController.cs \| grep -n` → `1042-1044` | §3 D3 — the overwrite is in the APK |

---

## 10. ⭐ WHAT WAS IMPLEMENTED (2026-09-16 implementation lane) — and the arithmetic

Scope held: `Assets/_Modules/Village/Troops/**` + `Assets/Editor/Regression/WallBreachOrderRegression.cs`.
No `.unity`, no `SmartMobileCamera.cs`, no `HeroTargetIndicator.cs`, no `DataRegression.cs`, no WO banner.
**No Unity gate run and no commit** — the lead gates the combined tree.

### 10A. D1 — the detour rule, re-derived. THE SHAPE WAS WRONG, NOT JUST THE NUMBER.

Steering is `_agent.Move(displacement)` — a straight-line push, **no `SetDestination`, no path
follow** (TroopController's own locomotion comment, `:511-512`; grep confirms a single `_agent.Move`
call site in the file). So what makes a troop grind on masonry instead of arriving is the **ABSOLUTE
extra walking** the straight line does not account for. The old rule was a pure ratio, which scales the
allowance with the distance to the foe — backwards:

| Captured straight line | Old allowance (x1.5) | Excess permitted | Reality |
|---|---|---|---|
| 5.7 m (09-14 footman, wall won) | 8.55 m | **2.85 m** | less than ONE go-around of a single 3.0 m panel |
| 14.4 m (the guard it refused) | 21.6 m | 7.2 m | — |
| 28.3 m (09-14 archer, spire won) | 42.5 m | **14.2 m** | enough to round a whole tower band |

Tightest exactly where a detour is walkable; loosest exactly where it is not.

**The replacement is an OR, so it is WIDENING-ONLY** (`RaidAssaultAi.RouteToUnitOpen`): open when
`routeLen - straightLen <= RouteDetourSlackMeters` **OR** `routeLen <= straightLen * RouteDetourFactor`.
Nothing that passes today can start failing.

**The Bastion arithmetic** (every source opened 2026-09-16, none copied from a doc):

* Authored — `Assets/Resources/Data/Canonical/scene-configs.json`, id `iron_bastion`:
  `baseRadius 54`, `wallSegmentsPerSide 13`, `entranceCount 1`, `interiorWallLayers 1`; that file's own
  field doc: no panel is stretched past **3.0 m** (the WO-1723 partition rule).
* Generated — `Assets/Editor/WallTools/RaidBaseGenerator.cs`: outer ring half-extent **54 m** -> side
  **108 m**, gates `{true,false,twoGates,false}` with `entranceCount 1` = **ONE south gate** (`:720`);
  keep ring at `54 x 0.45` = **24.3 m** half-extent -> side **48.6 m** (`:740`), **ONE north gate**
  (`:741`) — i.e. on the opposite face.
* **MUST BE OPEN** — same courtyard, only a **convex** obstacle between (a 3.0 m module, a corner post,
  a tower base). A NavMesh route round a convex obstacle of width `d` costs at most
  `(pi/2 - 1)*d ~= 0.57*d` of excess; two 3.0 m pieces in series plus agent-radius inflation each side
  is `~= 2*1.7 + ~2 = 5.4 m`.
* **MUST STAY REFUSED** — the straight line crosses a **ring**. Keep ring, south face to the north gate
  and back: excess `~= 2*(24.3 + 48.6 + 24.3) ~= 194 m`. Outer ring, north face to the south gate:
  `~= 2*108 = 216 m`. **And this one is MEASURED, not reasoned:** the 09-16 device log's objective
  probe on this very scene printed `routeObj=detour:156.5/33.3` (`:3047672`) — excess **123.2 m**,
  ratio **4.7x**.
* **`RouteDetourSlackMeters = 8f`** therefore sits **~15x below the smallest measured ring crossing**
  and above the largest convex go-around. That is the margin the ratio never had at close range.

⛔ **DERIVED, NOT MEASURED — said out loud (§11B).** The convex term uses the **authored** 3.0 m module
cap. This lane did **not** measure a tower-base footprint or the baked agent radius on
`RaidBase_IronBastion`, and 8 m is rounded up to cover them. The `routeUnit=[len= straight= excess= …]`
token added in §10D is what replaces the derivation with measurement on the next pull.

⚠ **AND IT IS UNPROVEN THAT THE 09-14 FOOTMAN LINE FLIPS.** Its excess is known only to be `> 7.2 m`;
neither capture prints a unit route length. Do not claim §2A as fixed from this change alone.

**Scope of the widening:** the **unit** probe only (`RefreshRouteToUnit`). `RefreshRouteToObjective`
keeps the **bare ratio** on purpose — `routeToObjectiveOpen` is `ResolvePhase`'s input, so widening it
moves the whole warband from Breach into Push/Finish, which is a different bucket ordering (D4) and is
not what the owner reported. Asserted in the suite so it cannot drift.

### 10B. D2 — ONE ruling guard, not two deletions
`RaidAssaultAi.StanceOutranksReachableUnit` (a property returning `false`) is read at **both** stance
gates: `PreferUnit`'s Breach branch and `PickBucket`'s in-attack-range shortcut. Deleting the two
clauses would have left a future re-flip hunting both sites again — which is exactly how WO-1746
shipped with the second gate **missed** for a whole ticket. Flipping the ruling is now literally one
`return`. Siege is untouched (the `preferStructures` branches return first) and aggro is untouched
(`Peel` returns before the stance is consulted).

### 10C. D3 — the wall focus is a CANDIDATE, not an overwrite
`nearestOtherStruct = shared;` is gone. The shared/ordered/sweep panel now resolves into its own local
`wallFocus`, the scan's single pass additionally records the nearest **wall** and the nearest
**non-wall** structure, and `RaidAssaultAi.NonWallStructSurvives(...)` arbitrates:

1. no non-wall structure -> the wall candidate stands (unchanged);
2. no wall in the sweep -> the non-wall structure stands;
3. **the wall may not be targeted at all** (stance-less non-siege) -> the non-wall structure stands,
   *whatever* the distances. This clause exists because of the captured 7.1 m wall / 7.4 m tower pair
   (§0-NEW.a): a 0.3 m difference is not "the tower is behind the wall", and with the wall refused the
   only alternative is `bucket=-1`, a guaranteed zero;
4. otherwise (armed stance / siege) -> **geometric**: the non-wall structure survives only if it is
   **nearer** than every wall this troop can see. A tower nearer than all visible masonry is not behind
   it. This is what keeps WO-1746's auto-chain intact and what refuses to promote a tower standing
   behind an intact panel — `RaidAssaultAi.PickBucket`'s own note on why the wall is refused *in place*
   rather than filtered out (WO-1438's navmesh-edge freeze).

`PickBucket`'s "a TOWER behind it becomes bucket 2" premise is now true upstream as well, which §3 D3
asked for.

⚠ **Two things flagged, not buried.** (a) Applying the geometric half with the **stance ON** is a
reading taken from the lead's brief, **not an owner ruling**. (b) A promoted tower is still steered at
in a straight line, so it may grind on masonry: the falsifier is `moved=` vs `commanded=` on the
adjacent `[Flow:TroopAI]` line. A route probe is **not** available as a fix — WO-1569 measured
**133 of 133** structure probes returning `CalculatePath-FAILED`, because a structure's `WorldPosition`
sits inside solid geometry.

### 10D. §12 instrumentation — the next pull settles it in one grep
Added to the `[Flow:RaidAI]` throttle line (all string parts built into **locals** first, per §1's
interpolated-string brace rule):
```
routeUnit=[status= len= straight= excess= slack= factor= refused=]
structSrc=  sweepWall=  sweepNonWall=
```
`structSrc` is one of `sweep-nonwall | wall-shared | wall-sweep | sweep-nearest | rally-march | none`.
`route=…-detour` keeps its exact token so §5's verdict table still greps; a route the **ratio** refused
and the **slack** opened prints `…-slack`, so the next capture can name WHICH rule opened it.

### 10E. Rider — a trace that forged its own provenance
`TroopBreachOrder.SetStanceArmed`'s disarm message printed the retired one-tenth wall multiplier.
`TroopController`'s selector comment tells the next seat that a `0.10` token on a fresh log proves the
build predates WO-1752 — so this string would have forged that proof on a build carrying the fix. It now
reads *"walls are OFF (0.00) for non-siege troops without the stance unless an explicit order still
stands"*, and two further stale mentions in the same file's remarks are corrected. Pinned by case 19.

### 10F. Comments retired in the same change (§15)
`RaidAssaultAi.PreferUnit`'s "the one place a reading was chosen" remark (now **RULED**, kept as
record); `TroopController`'s two "Peel is the ONE thing allowed to break an armed stance" /
"the warband therefore STAYS on the panel" paragraphs; `WallBreachOrderRegression`'s header case list
and Case 8's red proof; `TroopBreachOrder`'s two reluctant-path remarks; WO-1738's dated banner.

---

## 11. Files touched
| File | What |
|---|---|
| `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` | `StanceOutranksReachableUnit`, `RouteDetourFactor` (moved here), `RouteDetourSlackMeters`, `RouteToUnitOpen`, `NonWallStructSurvives`; both stance gates re-pointed |
| `Assets/_Modules/Village/Troops/TroopController.cs` | local factor deleted; `RefreshRouteToUnit` calls the new rule + records len/straight/refused; the scan splits wall vs non-wall; `wallFocus` + the arbiter replace the unconditional overwrite; trace tokens; three stale comments |
| `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` | the rider string + two stale remarks |
| `Assets/Editor/Regression/WallBreachOrderRegression.cs` | cases 8 + 15 inverted; cases 17/18/19 added; header + red proofs updated |

## 12. What still needs the device — the ONE thing this ticket cannot close from here
§5's capture procedure stands **unchanged** and is still the close condition. On the next Iron Bastion
pull, grep `\[Flow:RaidAI\] id=.*routeUnit=` and read:
* `routeUnit=[… excess= refused=]` — **the numbers no capture has ever carried.** Re-derive
  `RouteDetourSlackMeters` from them and replace §10A's authored-geometry derivation with measurement.
* `structSrc=sweep-nonwall` on a line whose pre-fix twin read `bucket=-1 mayWall=False
  otherStructIsWall=True` — D3 landed.
* `breachStance=True stanceYield=unit` while a defender is reachable — the new ruling landed.
* `moved=` vs `commanded=` on the paired `[Flow:TroopAI]` line — whether a slack-opened or
  tower-promoted troop actually **arrives**. That is Q2's evidence, and **Q2 is still open**.
