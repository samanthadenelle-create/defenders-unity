# WO-1458: raid walls and the spire crown are still admitted to the hostile target set, 320 times a session

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:57, build 2026.09.08.361259). PRIOR STATUS: FIXED - landed in d6511b8e5 (faction-classified admit + Case8 oracle), verified at HEAD
2026-09-07 by lane read; owner felt-test closes (a device raid with zero NON-ENEMY ADMITTED Warns).
PRIOR STATUS: READY TO IMPLEMENT
**Silo:** `Assets/_Modules/Village/Combat/` targeting admit rule + the raid base prefab collider layers.
Reopens the class WO-1047 closed on 2026-08-22.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1458 -> 1459 in the same edit).

## 1. EVIDENCE

Device log, 320 hits between 12:59:00.878 and 14:37:11.366:

```
[hostile-admit] NON-ENEMY ADMITTED TO THE HOSTILE TARGET SET (WO-1047).
  path='RaidBase_raider_camp_small/Wall_Outer_*' impl=DeNelle.Village.WallSegment
```

and

```
ADMITTED-VIA='.../RaidSpire/Crown' layer='Enemy'
```

The second line names the mechanism: a CHILD collider sits on layer `Enemy`, so the parent structure is
admitted through it. The WO-1047 guard is doing its job - it is logging, not blocking - and nobody has acted
on what it logs since it was closed.

> ## ⚠ SUPERSEDED 2026-09-07 - SECTIONS 2 AND 3 BELOW ARE THE WRONG FIX. DO NOT EXECUTE THEM.
> Measured at source this date: `Wall_Outer_*` colliders are put on the **`Structure`** layer by design
> (`Assets/Editor/WallTools/RaidBaseGenerator.cs:1201-1202`) and reach the sweep only because
> `HeroTargetIndicator.Awake` deliberately ORs `Structure` onto the mask (`:425`, WO-853), while
> `RaidSpire/Crown` is relayered to `Enemy` **on purpose at every Awake** by `RaidSpire.EnsureHittable`
> (`Assets/_Modules/Village/World/Camps/RaidSpire.cs:179-222`) so the hero's `Enemy`-masked sweep can hit
> the objective - so a prefab relayer is a no-op, and if it stuck the raid win-condition becomes unkillable.
> The 320 lines were a MIS-CLASSIFIER, not a layer defect (both objects are legitimately
> `CombatFaction.Hostile`: `RaidSpire.cs:227`, `WallSegment.cs:248-249`); it was fixed in **d6511b8e5**,
> which classifies admissions by faction (`HeroTargetIndicator.cs:975-1003`, all three admit sites gated on
> `CombatFactionRules.MayAttack` at `:789/:899/:918`), and the HARD oracle the ticket asked for already
> exists as **`BreakableContainerChestRegression` Case8 `hostile-admit-routes-through-faction-rules`**
> (`Assets/Editor/Regression/BreakableContainerChestRegression.cs:600-658`, registered `:177`, wired
> `DataRegression.cs:659-660`) - a source lint on the admit rule, NOT the prefab scan below, which would go
> red against deliberate working code.

## 2. FIX SHAPE

- Fix the child colliders' layer on the raid base prefabs (`Wall_Outer_*`, `RaidSpire/Crown`) so a structure
  never presents an `Enemy`-layer collider. Prefer this over widening the admit rule.
- Then promote the WO-1047 guard from a warning into a HARD oracle: a regression that scans the raid base
  prefabs and FAILS on any non-enemy component reachable through an `Enemy`-layer collider.

## 3. WHAT NOT TO DO
- Do not special-case `WallSegment` in the admit rule. The layer is wrong; patching the consumer leaves the
  next structure with the same bug and the guard blind to it.

## 4. ACCEPTANCE
- [ ] A full raid device session records ZERO `[hostile-admit] NON-ENEMY ADMITTED` **Warn** lines, with
      `[hostile-admit] HOSTILE STRUCTURE` **Step** lines PRESENT for the walls and the crown. *(Restated
      2026-09-07: the original "ZERO `[hostile-admit]` lines" can never pass, because after d6511b8e5 a
      hostile structure emits a Step by design - and that Step is the proof the reticle still acquires raid
      geometry. Silence would mean the walls became untargetable.)*
- [ ] ~~The prefab-scan oracle exists and goes red when a child collider is put back on `Enemy`.~~
      **SUPERSEDED** - that oracle would fail against `RaidSpire.EnsureHittable`'s deliberate relayer. The
      shipped oracle is Case8 `hostile-admit-routes-through-faction-rules`, which goes red if the admit rule
      stops routing through `CombatFactionRules` or an inline faction copy returns.
- [ ] `REGRESSION_OK n/n` on a fresh log.
