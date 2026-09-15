# WORK ORDER 1749 — Troops stall at the keep platform edge; no route ever reaches the spire (every path is PARTIAL)

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, from the owner's felt-test on the Seeker (tester build `2026.09.15.371127`, scene `RaidBase_IronBastion`).
**Silo:** raid base geometry + raid nav bake — `Assets/Editor/WallTools/RaidBaseDresser.cs` (`KeepPlatform` / `KeepRamp`, `:1720-1752`), `Assets/Editor/RaidNavBake.cs` (what the bake collects), and the raid scenes' NavMesh build settings. ⛔ NOT `RaidAssaultAi.cs` / `TroopController.cs` (WO-1746, landed today) and NOT `TroopFactory.cs` (WO-1748 lane) — the troops are doing what the navmesh tells them; the defect is in the world, not the AI, unless the lane PROVES otherwise.

**Lane hand-back 2026-09-15 (a CLAIM — nothing compiled, gated or baked).** Candidates **(a)** and
**(b)** are **DISPROVEN with numbers** (ramp 11.43° vs `agentSlope: 45`; `KeepPlatform`/`KeepRamp` are
under `Zone_Keep`, inside the collected set). Candidate **(d)** is **SOURCE-PROVEN and FIXED**: the
spire is seated on GROUND by `RaidBaseGenerator.PlaceSpire`, then `RaidBaseDresser.RaiseKeep` raises
the keep slab OVER that point, so the objective every troop paths to sat inside solid geometry. Fixed
at the placement (lead extended the silo) — the spire's Y now has ONE owner and decides AFTER the
dresser, with the lift MEASURED off the slab, no hardcoded height, and nothing downstream taught to
aim higher. Candidate **(c)** stays **UNPROVEN**; the new instrument settles it on the next bake.
**Pass 3, after the lead's bake:** the carve theory is **disproven** (`RAID_NAV_REACH_GOAL` maps the
goal at the SAME XZ, only +0.2 m in Y, in two scenes — a carve would displace it horizontally), so
`PathComplete` was reachable that day. **It is retired anyway as brittle-by-construction** against a
solid objective; the criterion is now **ARRIVED** — the route's last corner within (objective
footprint + live agent radius + slack), every term measured, nothing copied. The instrument now logs
`lastCorner=(x,y,z)` / `lastCornerDist=` / `arrivalRadius=` / `arrived=`, the numbers that were
missing when the red could not be diagnosed. The 2.07 m surface remains **UNPROVEN** and is named on
the next bake. ⚠ Two hazards recorded in the RESULT: `OwnedTemplateIdentityBake.Run` may now throw
(the source spire moved 1.5 m, the owned town's did not), and `TroopController` measures attack range
to the spire's **centre** at 2.5 m for a footman — a possible second reason troops stop and do
nothing (WO-1746 silo, untouched).
⚠ The bake now **withholds `RAID_NAV_BAKE_OK`** when the objective is unreachable (lead ruled: keep
it fail-closed). Full evidence, the exact `DataRegression.cs` registration line (deliberately NOT
added by the lane) and the reading that names the cause:
`WORK_ORDER_1749_troops_stall_at_keep_platform_edge_never_take_the_ramp.RESULT.md`.

## Owner, verbatim
> "also the troops are not using logic when at the inner platform, they are stopping at the edge and need logic to determine to move forward they must use a ramp"

## Evidence (captured, not theorised)
- Device logcat over the whole session on the Seeker (read 2026-09-15 13:5x local): **`routeObj=PathPartial` 1650 times, `PathComplete` 0 times, `PathInvalid` 0 times.** Not one troop resolve, in any phase, ever had a complete route to the objective. (`[Flow:RaidAI] id=… routeObj=…` is emitted by `TroopController` once per resolve.)
- `[Flow:FloorDiag] GROUND 'RaidBase_iron_bastion/Zone_Keep/KeepPlatform' … size=(26.73, 1.50, 26.73)` — the keep platform is a 1.5 m-tall slab; the spire (objective) stands on it.
- `RaidBaseDresser.cs:1736-1752` builds ONE `KeepRamp` primitive (`4.2 m` wide, rotated by `slope`, from `foot` to `landing`); its own comment at `:1741-1742` records a previous bug where the ramp descended the wrong way and left "an unwalkable step".
- The owner watched the warband stop at the platform edge and never walk around to the ramp — consistent with a navmesh that has NO connected walkable path from courtyard to platform top (an agent with a partial path walks to the nearest reachable point and stands).

## What is NOT proven (the lane proves it, no guessing)
Which of these is the actual break: (a) the ramp's slope exceeds the raid agent's max slope so the bake never connects it; (b) the ramp is excluded from the bake's collected geometry (`RaidNavBake` layer/tag/zone filter, or it is under a zone the bake treats as an obstacle); (c) the platform top is not baked as walkable at all (voxel/height filter); (d) the ramp is connected but the spire's own collider/`NavMeshObstacle` carves the landing so `SetDestination(spire)` is always partial. Each has a different fix, and the trace does not yet distinguish them.

## Ask
1. **Instrument first (§12):** add a one-shot `FlowTrace` in the raid nav bake that, for each raid scene, samples `NavMesh.CalculatePath` from a courtyard point to the spire position and logs `status`, corner count and the last corner's height — so `RAID_NAV_BAKE_OK` can never again be green over a spire nobody can reach. Then read the result on the next bake (the lead runs it).
2. Compute the ramp's actual slope angle from the geometry at `RaidBaseDresser.cs:1736-1752` (rise vs run) and compare to the raid agent type's max slope in the NavMesh build settings — cite both numbers.
3. Fix THE PROVEN cause (geometry or bake settings — not the AI). A `NavMeshLink` across the platform lip is acceptable only if the ramp genuinely cannot be made bake-connected; say why.
4. A regression that fails when courtyard→spire is not `PathComplete` in every raid scene (headless, uses the baked data).

## Acceptance
- Fresh `RaidBaseGenerator.BuildAllRaidScenes` + `RaidNavBake.BakeAll` (lead runs): the new trace reports `PathComplete` courtyard→spire for every raid scene.
- On device, `[Flow:RaidAI]` lines show `routeObj=PathComplete` after the outer wall is breached; the owner sees troops take the ramp.
- Lane flips this Status line and writes the `.RESULT.md`.
