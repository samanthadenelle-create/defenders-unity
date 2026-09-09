# WO-1580 - RepairProbe SURFACES logs at error level inside raid scenes, so every raid produces an F8 capture

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:54, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT
**Priority:** P3
**Silo:** Village / Instrumentation (RepairAvailabilityProbe)
**Source:** owner Seeker session 2026-09-07, build 2026.09.07.359076, F8 seq 4698

## Measured fact
seq 4698, 13:15:47.777Z, scene `RaidBase_fortified_garrison`, **kind=error**, stack
`DeNelle.Village.RepairAvailabilityProbe:ReportSurfaces() <- Poll() <- Guard.Try`:
`[Flow:RepairProbe] SURFACES scene='RaidBase_fortified_garrison' WallRepairController=ABSENT
HubRepairAffordance=ABSENT WaveManager=none(pure hub) -> NO repair surface exists in this
scene at all while a structure burns. The player has no way to repair anything here.`

## Root, at source
`Assets/_Modules/Village/Walls/RepairAvailabilityProbe.cs:220-223` - when both surfaces are
absent, `ReportSurfaces` calls `FlowTrace.Fail("RepairProbe", ...)` at :222. `Fail` publishes
at error level, which is what the F8 harness captures.

The probe installs UNCONDITIONALLY (`TrySpawn`, :106-113, wired to `SceneManager.sceneLoaded`
at :95-99); its comment says that is deliberate - it must not inherit the gate it measures.
Correct for a HUB, wrong for a raid base: the player is there to destroy enemy structures,
so "no repair surface" is the DESIGNED state, and every raid with a burning structure mints
an error capture.

## Fix shape
Make the severity depend on the scene class, not on the absence alone.

- The seam already exists: `Assets/_Modules/Core/HubScenes.cs` - `IsRaid(string)` (:61),
  `IsHub(string)` (:38), and `SceneKind Classify(string)` (:165-178, raid tested before hub
  because `IsHub` matches by substring). `SceneRouter.RaidBaseFortifiedGarrison`
  (`Assets/_Modules/Core/SceneRouter.cs:191`) is the scene in the capture.
- In `ReportSurfaces`, resolve the active scene once and branch:
  - `IsRaid` true, both surfaces absent -> `FlowTrace.Step` with the same line plus
    "expected: no repair surface is authored for a raid base".
  - Anything else, both surfaces absent -> keep `FlowTrace.Fail` unchanged; that IS a defect
    and this probe exists to catch it.
- SCOPE THIS TO `IsRaid` ONLY. Do NOT extend it to enemy outposts or dungeons:
  `HubScenes.cs:143-152` records that `Garrison_*`/`Outpost*` are deliberately NOT `IsRaid`
  and whether they are committed assaults is an unasked owner question. Read it before
  widening; the capture proves exactly one scene class.
- Leave the `_lastSurfaceLine` change-detection (:218-219) and its reset (:142) as they are.

## Acceptance
- A raid with burning enemy structures produces NO error-level RepairProbe capture; the
  same information still appears at Step level.

## Do NOT touch
`HubRepairAffordance`'s `SceneHasRepairables()` gate, the burning/invisibly-damaged passes
(`Poll`, `ReportInvisiblyDamaged`), or the unconditional `TrySpawn` install - the probe
keeps running everywhere, it just must not shout where the absence is by design.

## Context, no ticket needed
seq 4694-4697 (13:01:12Z, scene Title) are the wallet connect timeouts: `MWA association
timed out after 9s - no wallet dialed back on port 58755`, `Connect FAILED:
TimeoutException`, the `<queries>` manifest hint, and `Connect REFUSED by the wallet after
11.8s ... A wallet closed its one-shot association endpoint during this attempt, so the
wallet app WAS reachable and answered`. WO-1420-class attribution working as specified -
the lines name which side failed and why. No action.
