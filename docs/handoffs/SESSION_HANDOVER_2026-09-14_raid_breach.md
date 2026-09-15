# SESSION HANDOVER — 2026-09-14 evening — the raid Breach root cause

**Seat:** Claude desktop (Opus 5), sole committer after the owner closed the second CLI seat.
**Branch:** `dev`, pushed clean through `6879f32fa`. **On the Seeker:** `2026.09.15.370203`.

---

## 1. WHAT THE OWNER REPORTED, AND WHAT IT ACTUALLY WAS

> *"in a raid I select breach, and target the wall, it shows attacked, then moves to next wall segment,
> however nothing happens as far as being able to travel through the hole. There is no visual change
> whereas I would think you could see that wall segment destroyed."*

**Root cause (WO-1723):** the wall the player SEES (`Zone_Clad/Clad_*`, `RaidBaseDresser.cs:527`) was a
root-level SIBLING of the `WallSegment`, not a child. Both systems that must treat a wall as
destructible walk that hierarchy, so both silently missed it:
- `RaidNavBake.cs:69-76` exempted only `GetComponentInParent<WallSegment>()`, so all 60/60 clad panels
  were flagged `NavigationStatic` and **baked into the navmesh as permanent geometry**. A collapsed
  segment lifted only its redundant runtime carve; the baked hole stayed forever.
- `WallSegment.CollapseRoutine`'s `GetComponentsInChildren<Renderer>()` could only sink the segment's
  own meshes — which `HideWallRenderers:485-496` had already DISABLED. The sink moved invisible geometry.

⛔ **Both files named this outcome in advance and nobody had read them:** `RaidNavBake.cs:154-156`
(*"Baking wall geometry itself leaves a permanent hole even after its collider dies"*) and
`WallSegment.cs:475-477` (*"the art is a sibling the sink never moved"*).

## 2. WHAT LANDED

| Commit | What |
|---|---|
| `8b88a5053` | **Lane A** — clad excluded from the nav bake. The breach opens. |
| `55e3464b4` | **Lane B** — one clad panel per `WallSegment`, rubble on collapse, ordered-panel marker |
| `835f90937` | **WO-1731** — bakes now throw if they cannot save; new guard fails a dangling navmesh ref |
| `6879f32fa` | raid scenes regenerated + nav-baked on the new partition |
| `3a8e1a50c` | board |

**Lane A is PROVEN TWICE** — the owner on device (*"i can now walk through destroyed walls"*) AND
measured: wall breach probes went **1.7% → 64% WALKABLE** (166 vs 95, from 11 vs 645).

## 3. OPEN — START HERE NEXT SESSION

1. **⚠ THE GATE NARROWED.** The Q1 partition ruling cut the gate from ~8.3 m to **`gateSpan=1 = 3.99m`**
   (floor 3.50 m) — one module wide instead of three. Clears every check; nobody has felt it. **Owner
   call.** Lever is `gateSpan=2`.
2. **WO-1730 ruling 2 is NOT satisfied** — troops still grind the wall after a breach. Before the
   re-bake, `routeOpen=True` was **0 of 2,670** samples with `routeObj=PathPartial` 2,470. The ticket
   says in bold: **RE-MEASURE on `370203` BEFORE writing any code** — Lane B's repartition may have
   closed it for free. Grep: `adb logcat -d | grep -c 'routeOpen=True'`.
3. **36% of breaches were still sealed** pre-repartition (95/261 `NOT-WALKABLE`). Same re-measure
   settles it; likely the same cause as (2).
4. **WO-1731 §5** — the orphaned `NavMesh-OuterWorld_NavMeshSurface.asset`: delete / adopt / hold.
   Currently held, untracked, untouched. Nobody has compared its coverage.
5. **`VillageSceneBuilder.NavMesh.cs`** has the same missing save-check; left alone because §9 makes
   that file a one-agent bottleneck. Two lines when free.
6. **Unclosed acceptance line:** `ownerAgent=on-mesh` in a captured `[Flow:HeroOwner]` — needs a device
   read on `370203`.

## 4. THE TWO MISTAKES THIS SESSION MADE — both cost a build

- **Built from a dirty tree after seeing a deletion and setting it aside as "not mine".** It shipped:
  the town had no navmesh and the owner walked through walls. Memory:
  `a-deletion-in-git-status-will-ship-if-you-build`.
- **Ran the wrong bake and got a GREEN MARKER on an operation that changed nothing.**
  `RaidNavBake.BakeAll` returned `RAID_NAV_BAKE_OK scenes=5` against scenes still holding 78 segments,
  because a nav bake does not rebuild the ring — `RaidBaseGenerator.BuildAllRaidScenes` does. Caught by
  reading the bake's CONTENT. Memory: `bake-marker-can-be-green-on-the-wrong-operation`.

Also corrected: the lead called the navmesh situation "an unfinished rename". **The owner challenged it
and was right** — Unity names a `NavMeshSurface` bake after the GameObject that owns it, so no human
renamed anything. That was an inference stated as fact (CLAUDE.md §11B).

## 5. TREE STATE — NOT CLEAN, AND THE BUILDS COME FROM IT

353 modified / 236 untracked / **1 deleted** (`docs/WARDROBE_ARCHITECTURE.zip`, harmless, unreferenced).
None of it is this session's. Six entries are high-risk and unreviewed: `ProjectSettings.asset`,
`EditorBuildSettings.asset`, `AddressableAssetSettings.asset`, `AssetGroups/Structure_Art.asset`,
`Main_Castle_Overworld.unity` (~4.4k lines, verified a REGENERATION with net +15 prefabs, not a
deletion), `MainCastle_Hall.unity` (retired legacy, should not be modified).

⚠ A cheap-model triage of this tree produced **two false alarms** — "prefabs deleted from the hub" (75
removed but 90 added) and "SaveSchema may be bumped without a migrator" (`CurrentVersion` unchanged).
Both were checked and killed. Memory `haiku-triage-needs-verification` applies; verify before acting.

## 6. GATES, MARKERS ON FRESH LOGS

```
COMPILE_GATE_OK                    Builds/cgBoth      0 errors outside Packages
REGRESSION_OK 530/530 suites       Builds/regBoth     529 -> 530, the new navmesh guard ran
  [navmesh-reference] NAVMESH REFERENCE OK -- 15 assigned m_NavMeshData
RAID_NAV_BAKE_OK scenes=5          Builds/navbakeC    obstacles 62 / 125 / 168
APK_OK / R2_PARITY_OK / APK_DONE   445 MB, 201 objects
GATE_BRACE_SUMMARY bad=0 of 26     re-run by the lead, NUL-clean
```

**The number that proves the architecture change:** nav bake now excludes **8** clad renderers on
`raider_camp_small`, down from 60 — the other 52 are `WallSegment` children now and the bake's own
`GetComponentInParent` test finds them. The 8 are corner stubs still under `Zone_Clad`, exactly as
Lane B predicted when it argued for KEEPING `IsUnderCladZone` rather than deleting it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
