# WO-1569 - Breach probe reads a felled tower after Unity destroyed it

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Combat/AI (edit-only lane; file-disjoint from RaidDeployController, Dungeons, EnemyContent, ObsidianQueue)
**Minted:** 2026-09-07, from the `CLI_LANES_WO_NUMBERS.md` main-line banner (1569 -> 1570, same edit)

## The capture (this is the evidence, not a theory)

Device `SM02G4061955851`, build **2026.09.07.358872**, scene **RaidBase_raider_camp_small**,
F8 seq **4688** (`troop-footman`) and **4689** (`troop-archer`), same instant
(`2026-09-07T05:44:05.73Z` / `.75Z`). Files:
`logs/f8-inbox/capture-device-20260907-004412-seq4688.md` and `-seq4689.md`.

```
[Flow:TroopAI] breach-probe id=troop-footman FAILED: NullReferenceException
  at UnityEngine.Component.get_transform ()
  at DeNelle.Village.DefenseTower.get_WorldPosition ()
  at DeNelle.Village.TroopController+<>c__DisplayClass119_0.<TraceBreachProbe>b__0 ()
  at DeNelle.Core.Diagnostics.Guard.Try (...)
  at DeNelle.Village.TroopController.Update ()
```

## Root cause

`IDamageable` is an **interface**, so `destroyed != null` inside `TraceBreachProbe` compiles to a
plain managed reference comparison and never reaches `UnityEngine.Object`'s overloaded `==`. A
`DefenseTower` dies through `Destructible.NotifyBroken`, which `Destroy(gameObject)`s it
(`DefenseTower.cs:170`, `:349`); Unity's destroy is deferred to end of frame and the `foe-died`
rescan runs on the **next** `Update`, so the probe meets a reference whose native half is already
gone. `destroyed.WorldPosition` -> `transform` -> throw.

Why it never appeared in the 133 measured wall probes: a collapsed `WallSegment` **keeps its
component** (only `IsAlive` flips) - the note at `TroopController.cs:619` says so. Only the
Destroy()-on-death structures reach this path.

**Correction to the dispatch's premise, stated per CLAUDE.md 11B.** This is **not** once-per-frame.
`NearestHostile` is `Physics.OverlapSphereNonAlloc`-based and cannot return a destroyed collider,
and `previousFoe` changes after the rescan, so the probe fires **once per felled structure per
troop**. The two captures prove one throw each, nothing more; the logcat-ring hazard is therefore
not the damage here. The real cost is that **WO-1438's `holeNavmesh=` measurement is lost entirely
for towers** - the lambda aborts before its `FlowTrace.Step` ever runs.

## The fix

1. `TroopController.IsLiveTarget(IDamageable)` - `dmg is UnityEngine.Object uo ? uo != null : dmg != null`.
   Public + static so the regression asserts the live predicate, not a copy of it.
2. Foe validity at both sites uses it, so a Destroy()d foe drops out of `_cachedFoe` and the troop
   re-selects. New reason string `foe-destroyed`, which also gates the probe.
3. `_lastFoePos` / `_lastFoePosValid` record the foe's position **while it is live** (engaged path,
   retarget line) and are handed to the probe. The probe reads no position off the corpse - and
   towers become measurable for the first time instead of being skipped.
4. `DefenseTower.WorldPosition` caches on read behind a Unity alive check, so no other seam can be
   taken down by a tower that died a frame earlier. Safety net, not the fix.
5. `TroopTargetPreferenceRegression` Case 5 pins all three halves.

## Acceptance

- No `breach-probe ... FAILED` line in a raid where a `DefenseTower` is felled.
- The `BREACH:` Step now emits for a felled tower, carrying `holeNavmesh=`.
- `TROOP_TARGET_PREF_OK` on a fresh gate log.

## Do not touch

`RaidDeployController.cs`, `Dungeons/**`, `EnemyContent/**`, `ObsidianQueue*`.
