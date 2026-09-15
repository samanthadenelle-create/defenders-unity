# WO-1749 RESULT — instrument shipped; (a)/(b) DISPROVEN with numbers; the buried-spire ORDERING defect fixed at its placement (lead-authorised, pass 2)

**Status of this document:** a CLAIM, not a fact. Nothing here has been compiled, gated or baked —
the lane held no Unity lock (a build chain was running) and ran no gate, no bake, no build, no git.
**Lane:** edit-only, 2026-09-15. **Pass 1** = instrument + regression, no geometry. **Pass 2** = the
buried-spire ordering fix, after the lead extended the silo to `RaidBaseGenerator.cs`.

---

## 1. Candidate (a) — "the ramp's slope exceeds the raid agent's max slope" — **DISPROVEN**

Both numbers, each read at source this session:

| Number | Value | Where it was read |
|---|---|---|
| Raid agent MAX SLOPE | **45°** | `Assets/Scenes/RaidBase_IronBastion.unity:107` (`agentSlope: 45`, inside `NavMeshSettings.m_BuildSettings`, `:102-120`). Identical at `Assets/Scenes/RaidBase_raider_camp_small.unity:107`. Also `agentTypeID: 0`, `agentRadius: 0.5`, `agentHeight: 2`, `agentClimb: 0.4`, `cellSize: 0.1667`, `minRegionArea: 2`. |
| `KeepRamp` ACTUAL slope, IronBastion | **11.43°** | measured, not assumed — see below |

The bake itself is the legacy scene bake (`UnityEditor.AI.NavMeshBuilder.BuildNavMesh()`,
`Assets/Editor/RaidNavBake.cs:124-125`), so the scene's own `NavMeshSettings` block above *is* the
build settings. There is no `NavMeshSurface`, no second agent type, no `collectObjects` /
`layerMask` / `useGeometry` override anywhere in `RaidNavBake.cs` (grepped: zero hits for
`agentTypeID`, `NavMeshBuildSettings`, `agentSlope`, `collectObjects`, `layerMask`, `useGeometry`).

**How the 11.43° was obtained — measured first, then confirmed against the source formula.**
Measured: the last raid bake log, `Builds/rebake-raid-navmesh.log` (2026-09-14 14:00), prints the
ramp's real world size from `RaidNavBake.TextureKeepSurfaces`:

```
[RaidNavBake] RaidBase_IronBastion/KeepRamp        ... surfaceMetres=(4.20, 0.35, 7.57)
[RaidNavBake] RaidBase_IronBastion/KeepPlatform    ... surfaceMetres=(26.73, 1.50, 26.73)
```

The cube's local Z extent IS `rise.magnitude` (`RaidBaseDresser.cs:1750`), and the platform is
`1.50 m` tall, so the ramp climbs 1.50 m over a 7.57 m hypotenuse:
`asin(1.50 / 7.57) = 11.43°`. Same log, same reading, the other two keeps:

| scene | rise | hypotenuse | slope | vs `agentSlope` |
|---|---|---|---|---|
| `RaidBase_IronBastion` | 1.50 | 7.57 | **11.43°** | 45 |
| `RaidBase_fortified_garrison` | 1.50 | 7.05 | **12.28°** | 45 |
| `RaidBase_mage_enclave` | 0.80 | 7.46 | **6.16°** | 45 |

Confirmed from source at `Assets/Editor/WallTools/RaidBaseDresser.cs:1736-1752` (`foot`, `landing`,
`rise`), with `half = Innermost * 0.55 = 13.365` back-solved from the measured platform
`26.73 / 2`: `rise = (0, 1.5, 0.42798*half + 1.69592) = (0, 1.5, 7.416)`, `|rise| = 7.567` —
matching the logged `7.57` to the printed precision. **The ramp is not too steep; it is not even
close.** The widest margin in the game.

Corollary also checked: ramp width `4.20 m` against `agentRadius 0.5` leaves a ~3.2 m walkable
strip after erosion, and the landing step is already pinned below 0.10 m by the existing
`RaidKeepRampRegression.CheckKeepRamp` (`Assets/Editor/WallTools/RaidKeepRampRegression.cs:79-110`).

---

## 2. Candidate (b) — "the ramp is excluded from the bake's collected geometry" — **DISPROVEN**

`RaidNavBake` collects by **renderer**, and excludes exactly three classes
(`Assets/Editor/RaidNavBake.cs:104-116`): a renderer with a `WallSegment` parent, a `DefenseTower`
parent, or one under `Zone_Clad` (`IsUnderCladZone`, `:66-76` — unchanged by this lane). There is no layer filter, no tag
filter, and no `NavMeshModifier` anywhere in the file.

`KeepPlatform` and `KeepRamp` are created under **`Zone_Keep`**, not `Zone_Clad`
(`RaidBaseDresser.cs:166` creates the zone, `:211-212` calls `RaiseKeep(keep, ...)`,
`:1722-1756` builds both cubes as children of it), and neither has a `WallSegment` or
`DefenseTower` ancestor. **They are inside the collected set and get `NavigationStatic`.**

Proven present at bake time, not inferred: `TextureKeepSurfaces` only ever touches renderers
literally named `KeepPlatform` / `KeepRamp` (`RaidNavBake.cs:343`), and the 2026-09-14 log
prints one line for each, in all three keep scenes and in `OwnedTown_IronBastion` — so both
renderers demonstrably existed in the scene during that bake.

Side finding while proving this: **`cladExcluded` was 0 in every scene** — the
`"N clad renderer(s) EXCLUDED"` line (`RaidNavBake.cs:121-122`, guarded by `> 0`) appears **nowhere**
in `Builds/rebake-raid-navmesh.log`. Consistent with the file's own WO-1723 Lane B note that the
per-segment clad panels are now reached by the `WallSegment` parent test instead. Not a defect;
recorded so the next reader is not surprised by a silent counter.

---

## 3. Candidate (d), the buried objective — **SOURCE-PROVEN, and FIXED in pass 2.** Candidate (c) still unproven.

What the device data does establish (`TroopController.RefreshRouteToObjective`,
`Assets/_Modules/Village/Troops/TroopController.cs:1131-1160`): the goal handed to
`NavMesh.CalculatePath` is **`spire.WorldPosition`** (`:1144`), i.e. `RaidSpire.transform.position`
(`Assets/_Modules/Village/World/Camps/RaidSpire.cs:251`), and the status printed as `routeObj=` is
the raw `NavMeshPathStatus` (`:1150`).

**A source-proven geometric defect found while checking (d).** Reported in pass 1 as
out-of-silo; **the lead extended the silo and authorised the fix**, which is §3b below:

> `RaidBaseGenerator.PlaceSpire` sets `go.transform.localPosition = Vector3.zero`
> (`Assets/Editor/WallTools/RaidBaseGenerator.cs:875`) and then `SeatOnGround(go)`
> (`:903`), whose whole body is `go.transform.position += new Vector3(0f, -b.min.y, 0f)`
> (`:1813-1820`) — i.e. the spire's transform lands at **y ≈ 0**.
> `RaidBaseDresser.RaiseKeep` then runs **afterwards** (`:211-212`, the last call in `Dress`,
> and the dresser is called by the generator *after* the spire per the file header at `:4`)
> and drops a **solid 1.5 m `KeepPlatform` cube centred on the origin** around it
> (`:1724-1731`).
>
> **So the exact point every troop paths to is 1.5 m inside a solid slab.** Whether Unity's
> query still maps it onto the platform-top mesh, or maps it somewhere disconnected, or fails
> to map it at all, is the one thing that separates (c) from (d) — and it is a runtime
> measurement, not a source reading. The new `RAID_NAV_REACH_GOAL` line answers it in one read.

**What is proven and what is not, stated plainly (CLAUDE.md §11B):** that the objective point sits
inside solid geometry is **proven at source**. That this is *why* the device read `PathPartial`
1650/1650 is **NOT proven** — it is the best-evidenced explanation, and the lead has ruled on that
basis. Candidate **(c)** ("the platform top is not baked walkable at all") remains **unproven** and
is untouched by the fix; the `top->spire` leg and `RAID_NAV_REACH_GOAL` settle it on the next bake.

---

## 3b. THE FIX (pass 2, lead-authorised) — the spire's Y now has ONE owner and it decides LAST

**The defect is the ORDER, and the fix says so in the code.** `PlaceSpire` ends with
`SeatOnGround(go)` — the spire's lowest rendered point at y = 0 — and `RaidBaseDresser.Dress` runs
*afterwards*, its last act raising the slab over that very point.

**Reordering was rejected, deliberately.** `Dress` also **reskins the spire**
(`RaidBaseDresser.cs:1206-1211` replaces its children with the config's art), so a seat taken before
`Dress` is stale on two counts, not one. Moving `PlaceSpire` later would only move the problem. The
spire's Y therefore keeps a single owner — `RaidBaseGenerator` — which now makes its decision *after*
the dresser has finished building the ground under it.

**Nothing downstream was taught to aim higher.** `TroopController.cs` is untouched. No magic offset.

**No height is hardcoded.** The lift is **measured off the slab the dresser actually built**
(collider bounds first, renderer bounds second), so the dungeon-stone keep (0.8 m) and any future
retune of either value follow for free, and a copied `1.5` can never rot in this file.

| Change | Where |
|---|---|
| `KeepPlatformName` const, with a comment forbidding this file from restating the slab's height | `Assets/Editor/WallTools/RaidBaseGenerator.cs:182-188` |
| `ReseatSpireOnKeepPlatform(root, spire)` called immediately after the `RaidBaseDresser.Dress(...)` block, with the ordering reasoning | `:681-688` (the `Dress` call is `:664-673`) |
| `SeatOnGround` reduced to `SeatOnSurface(go, 0f, out _)` — **one** piece of seat arithmetic in the file, no second copy to drift | `:1828-1831` |
| `SeatOnSurface(GameObject, float surfaceY, out float lift)`, returns false (and does **not** move the object) when there are no renderers, so a caller can never assume a seat happened | `:1841-1854` |
| `ReseatSpireOnKeepPlatform` — the WO-1749 ordering RCA in a block comment, then: find `KeepPlatform`, `Physics.SyncTransforms()`, measure, assert the spire is actually over the footprint, seat, and log the before/after Y and the lift | `:1856-1946` |
| `TryMeasuredBounds(Transform, out Bounds)` — collider first, renderer second, false rather than an empty box | `:1948-1965` |

**Every refusal path logs and refuses to guess** rather than falling through silently: no
`KeepPlatform` in the tree (legitimate — `RaiseKeep` only runs when `InnerLayers > 0`, so a camp with
no keep keeps its correct ground seat) logs and returns; unmeasurable slab bounds **warn and refuse
to guess a lift**; a spire whose XZ is outside the slab footprint warns and is **not** lifted onto a
slab it does not stand on.

**Which build path this covers — checked, not assumed.** `BuildAllRaidScenes` → `BuildSceneFor(id)`
→ `BuildConfigLayout(def, root)` is the path that produces `RaidBase_IronBastion` (`:436` logs
`FINAL_RAID_GENERATED_OK config=iron_bastion`; `:528` records the `iron_bastion -> RaidBase_IronBastion`
name mapping), and the `Dress` call the fix follows is inside `BuildConfigLayout` (`:583`). The
separate `Build()` / `BuildIronBastion` pair at `:2262-2274` is the LEGACY flagship layout kept for
its menu items — the file's own comment at `:2303` says it "only lands when the menu item / Build()
is run" — and it neither places a `RaidSpire` nor calls `Dress`, so it is correctly untouched.

**Regression risk checked:** no regression under `Assets/Editor/Regression/` or
`Assets/Editor/WallTools/` asserts anything about the spire's Y or position (grepped). Lifting the
spire does not change the navmesh it carves — the legacy bake voxelises the topmost surface, and the
1.5 m that used to be *inside* the slab never contributed a walkable voxel.

### The carving half — NOT the same fix, and it needs no change

`RaidSpire.cs` contains **zero** occurrences of `NavMeshObstacle` (grepped). Nothing on the spire
carves anything; its *renderers* are `NavigationStatic` and carve their own footprint out of the
platform top, which is what a building is supposed to do. The ramp's landing is at the slab's SOUTH
EDGE (`z = -half + 0.25 ≈ -13.1`), ~13 m from the centre spire, so the spire cannot be eating the
landing. **No change made.** The `top->spire` leg measures it on the next bake either way.

**Same defect class, NOT fixed — each needs its own evidence before anything moves** (recorded in
the code comment at `RaidBaseGenerator.cs:1877-1884` so it cannot be lost):
- **`BossSpawn`** is authored at `(0, 0, -bossOffset)` with `bossOffset = Mathf.Max(4f, innermost * 0.35f)`
  (`RaidBaseGenerator.cs:640-643`) — for IronBastion that is 8.5 m, inside the slab's 13.4 m
  half-extent, so the boss marker is buried on the same arithmetic. Its own comment says
  `RaidGarrisonSpawner` navmesh-snaps from it, which may or may not rescue it.
- **Keep garrison slots** at `r = Mathf.Max(4f, ctx.Innermost * 0.4f)` (`RaidBaseDresser.cs:1708`) —
  9.72 m for IronBastion, likewise inside the slab.
- **The spire's ground seat on a NO-KEEP config** is taken before `Dress` reskins it
  (`RaidBaseDresser.cs:1206-1211`), so a reskin that changes the art's height leaves it stale there
  too. The fix deliberately returns early for those configs rather than silently changing them.

Two other things checked and found **clean**, so the lead does not re-walk them:
- **Prop lane vs. the ramp.** Keep/Choke-zone props go through `PlaceZoneProps`
  (`RaidBaseDresser.cs:1527-1552`), which — unlike the courtyard path — does **not** call
  `AcceptPropSlot`, so `PropKeepout.LaneHalf` never applies to it. It relies on `IsSouthLane`
  (`:1667-1670`: `|x| < 4 && z < -4`) shifting `x` by ±6 m. Post-shift `|x| ∈ (2, 10)`, against a
  ramp half-width of 2.1 m eroded to ~1.6 m walkable — so a keep/choke prop cannot sever the ramp,
  though the margin is thin. Worth a follow-up ticket, not a fix here.
- **Ramp/landing continuity** is already pinned (`RaidKeepRampRegression`, §1 above).

**An adjacent finding the lead should NOT chase as this bug.** The inner keep ring is gated
**NORTH only** (`RaidBaseDresser.cs:187-191`, `northGate: true`, gatehouse at `+z * ring.Radius`)
while the **`KeepRamp` is SOUTH** (foot at `z ≈ -20.5`). The outer gate is also south (`:197`). So
the intended route is south gate → all the way round to the north inner gate → back round to the
south ramp. `TroopController` rejects a route whose length exceeds `straight * RouteDetourFactor`
and reports it as `detour:`, not `PathPartial` (`:1153-1156`) — so **even a perfectly connected
platform may never read `PathComplete` from the south**. That is a second, independent failure
mode sitting behind this one, in the WO-1746 silo. Naming it so it is not mistaken for this ticket.

---

## 4. What shipped

### `Assets/Editor/RaidNavBake.cs` (2 edits)
- `:80-86` (line numbers below are POST-edit, re-read at source after both edits) — a `reachFailures` list, with the reasoning for why a green `RAID_NAV_BAKE_OK` was
  never evidence of anything (it proved "non-empty triangulation", never connectivity).
- `:138-159` — after each scene's bake, while that scene's NavMesh is still live, calls
  `RaidKeepReachRegression.ProbeScene(scene, out reachNote)`. If any scene fails, it emits
  `RAID_NAV_REACH_FAIL <n> scene(s) — …` via `Debug.LogError` and **returns without emitting
  `RAID_NAV_BAKE_OK`**. On success it emits `RAID_NAV_REACH_OK` and then the existing
  `RAID_NAV_BAKE_OK`.
  - ⚠ **Lead, decide this consciously:** this is WO-1749 §1's ask verbatim ("so `RAID_NAV_BAKE_OK`
    can never again be green over a spire nobody can reach"), and it means **the next bake of a
    still-broken IronBastion will withhold `RAID_NAV_BAKE_OK` and log an error**. It deliberately
    does **not** throw, so the diagnostic lines survive to be read. If a chain must not hard-stop
    on this yet, downgrade the `LogError` to `LogWarning` — the reach lines are unaffected.

### `Assets/Editor/WallTools/RaidBaseGenerator.cs` (pass 2 — 4 edits)
The buried-spire ordering fix. Full detail in §3b; lines `:182-188`, `:681-688`, `:1828-1854`,
`:1856-1965`.

### `Assets/Editor/Regression/RaidKeepReachRegression.cs` (new; 538 lines after pass 3)
`DeNelle.Editor.Regression.RaidKeepReachRegression` — the single authority for "does the courtyard
reach the spire", used by BOTH the bake and the suite so the two can never disagree.
`Debug.Log`, never `FlowTrace` (editor assembly — a lane hit CS0103 doing that yesterday).

Per scene it logs one `RAID_NAV_REACH_GOAL` line and up to three `RAID_NAV_REACH` lines in the
format the WO asked for:

```
RAID_NAV_REACH scene=<name> status=<PathComplete|PathPartial|PathInvalid|from-unmapped|from-mapped-off-platform|to-unmapped|CalculatePath-FAILED> corners=<n> lastCornerY=<y> rawStatus=<same vocabulary> rawLastCornerY=<y> leg=<label> from=… fromMapped=… to=… toMapped=…
RAID_NAV_REACH_GOAL scene=<name> goal=… mapped=<bool> mappedPos=… mappedDy=… mappedDist=… platform=<bool> ramp=<bool> outerRadius=…
```

**EVERY leg is measured TWICE, and passes only when BOTH reads are `PathComplete`.**
`status=` snaps both endpoints onto the mesh first and answers *"is what is physically there
connected"*. `rawStatus=` hands the goal in **unsnapped** — which is exactly the call the device
makes (`TroopController.cs:1144-1148` passes `spire.WorldPosition` straight to
`NavMesh.CalculatePath`). Unity's implicit endpoint mapping need not agree with an explicit 6 m
sample, and if it did not, a bake reading `PathComplete` while the device kept reading
`PathPartial` would re-open the exact argument this instrument exists to close. **The delta
between the two on one line is itself a finding.**

Two further hardenings so a leg can never silently mean something other than its label:
`Physics.SyncTransforms()` before the platform's collider bounds are read (collider bounds can be
stale on a freshly opened scene — mirrors `RaidKeepRampRegression.CheckKeepRamp`), and the
`top->spire` start refuses to path at all if its 3 m sample snapped it more than 0.5 m in Y,
reporting `from-mapped-off-platform` instead. Without that guard, an UNBAKED platform top
(candidate c) would quietly snap the start down to the courtyard and the leg would report on a
route it never took.

The three legs are chosen so a failure **names its own cause** instead of conflating three systems;
every probe point is derived from the built tree, never hardcoded (arena radii are per-config):

| leg | from | what it isolates |
|---|---|---|
| `lip->spire` | 1.5 m south of the ramp's top-face foot (`ramp.TransformPoint(0, 0.5, -0.5)`, the same read `RaidKeepRampRegression` uses) | **ramp + platform + goal-mapping only.** Inside the inner ring, outside the platform — no wall, gate or prop can explain a failure here. The load-bearing leg. |
| `top->spire` | on the platform top, `(slab.center.x, slab.max.y + 0.2, slab.min.z + 1)` | goal-mapping alone |
| `court->spire` | `(0, 0.2, -(outerRadius + |slab.min.z|)/2)`, `outerRadius` measured off the `Wall_Outer_*` `WallSegment`s | the whole chain — the WO's acceptance leg |

**The reading that names the cause on the lead's next bake:**
- `lip->spire` `lastCornerY ≈ 0` → the ramp does **not** join the platform; the top is an ISLAND.
- `lip->spire` `lastCornerY ≈ 1.5` and `PathComplete` → ramp and platform are fine; look at the
  `court->spire` leg and at the north-gate detour above.
- `RAID_NAV_REACH_GOAL` `mapped=false`, or `mappedPos.y ≈ 0` rather than `≈ 1.5` → the buried-spire
  defect in §3 is real and is the cause; the fix is generator-side (lift the spire onto the slab, or
  raise it inside `RaiseKeep`).

⚠ **Caveat written into the file header, not just here:** intact walls and towers carry *carving*
`NavMeshObstacle`s (`RaidNavBake.PrepareDestructibleWalls`, `RaidNavBake.cs:220`; `PrepareMovableTowers`, `:162`). Carving is a runtime service and may not
have applied in a batchmode edit-mode probe, so `court->spire` measured here can read optimistically
versus the device. `lip->spire` is carve-independent by construction. A device verdict still comes
from the device's own `routeObj=` lines.

**Discovered set == baked set, verified this session.** `ls Assets/Scenes/RaidBase_*.unity
Assets/Scenes/OwnedTown_*.unity` returns exactly the five files `RaidNavBake.RaidScenes` lists
(`RaidBase_IronBastion`, `RaidBase_fortified_garrison`, `RaidBase_mage_enclave`,
`RaidBase_raider_camp_small`, `OwnedTown_IronBastion`) — so discovery adds no scene the bake does
not cover today, and if one ever appears the suite failing on it is the correct outcome, not a
spurious one (`RaidSceneCoverageRegression` exists because these two sets have drifted before).

Precedent that a scene's BAKED NavMesh is live immediately after `OpenScene` in batchmode:
`Assets/Editor/Regression/BiomeRoadsDropReachProbe.cs:64` opens Single and `:97-100` queries
`NavMesh.SamplePosition` against it.

Scene list is **discovered** (`AssetDatabase.FindAssets("t:Scene", ["Assets/Scenes"])`, filtered to
`RaidBase_*` / `OwnedTown_*`) rather than copied from `RaidNavBake.RaidScenes` — a hardcoded second
copy rots silently (CLAUDE.md §2/§5/§16), and `DeNelle.EditorRegression` cannot reference
`DeNelle.Editor` anyway (that reference runs the other way: `DeNelle.Editor.asmdef:5`).

**No geometry was changed, and no `NavMeshLink` was added.** The WO gates a link on proving the ramp
cannot be made bake-connected; the evidence in §1/§2 is the opposite — the ramp is shallow, wide,
continuous and collected. Shipping a link now would paper over a cause nobody has seen.

---

## 4b. PASS 3 — the criterion question, answered: **it is (b), NOT (a)** — and the criterion changes anyway

### The carve theory is CONTRADICTED BY THE MEASUREMENT

`RAID_NAV_REACH_GOAL` reports `mappedPos=(0, 1.74, 0)` against `goal=(0, 1.52, 0)` on IronBastion,
and `(0, 0.99, 0)` against `(0, 0.82, 0)` on mage_enclave. **The X and the Z are unchanged; only Y
moves** (0.22 m and 0.17 m) — in two independent scenes, with different slab heights and different
spire art.

`NavMesh.SamplePosition` returns the CLOSEST point on the mesh. **If the objective carved the
navmesh under its own footprint, the closest point would be displaced HORIZONTALLY to the carve
edge — metres of X/Z — not 0.2 m straight up.** It is not. So there is walkable navmesh at the
objective's own XZ column, `PathComplete` was **not** unsatisfiable by construction on that bake,
and **(a) is disproven. It is (b).**

### But the criterion is still wrong, for a different reason, and it is now RETIRED

`PathComplete` asks whether the query reached the END POLYGON. The objective is a solid building
whose renderers bake `NavigationStatic`. The moment any objective is wide enough to carve its own
centre — a bigger spire, a different kit, a future boss structure — a route to its CENTRE terminates
at the carve edge and reads `PathPartial` **forever**, and the gate becomes exactly the
unsatisfiable trap you named. **The instinct was right even though the premise did not hold today.**
A criterion whose validity depends on a Unity behaviour nobody controls does not belong in a gate.

**The criterion is now ARRIVED**, and every term in it is measured at the moment it is asked:

> the route's **last corner** must land, **in the plane**, within
> (the objective's footprint half-width, off its own renderer bounds)
> + (the agent radius, read off the **live** `NavMesh.GetSettingsByID(0)`)
> + `ArrivalSlack` (0.5 m).

Nothing is copied from another file, so nothing can rot. A troop standing there is touching the
spire. It is satisfiable whether or not an objective carves; it fails loudly on a genuine island
(a route stopping at IronBastion's platform edge is ~13 m out against an arrival radius of a few
metres); and it cannot be satisfied by breaking geometry. `status=` and `rawStatus=` are still
logged — as evidence, no longer as the gate. The retirement is written into the file header so
nobody restores it.

**Plainly: the earlier `PathComplete` criterion was wrong and this lane shipped it.** Retired.

### The instrument gap that made the red undiagnosable — fixed

`lastCornerY` **alone cannot tell an island from an arrival**: a route that stops 13 m away at the
platform edge and one that stops touching the spire can report the same height. That is why the bake
produced a red that could not be read without a second bake. `RAID_NAV_REACH` now also logs
**`lastCorner=(x,y,z)`, `lastCornerDist=` (planar, to the objective), `arrivalRadius=` and
`arrived=`**, plus the same four for the raw query.

**`lastCornerDist` is the number to read first on any future red**, and it answers the `corners=2`
question directly: a two-corner path is a straight funnel, which only forms across CONNECTED
polygons — so if `lastCornerDist` comes back small, the ramp is connected and the old criterion was
the only thing failing; if it comes back ~13 m, the route stopped at the platform edge and something
genuinely disconnects. The same line already prints `fromMapped=`, which rules the drifted-start
theory in or out in the same read.

### What the 2.07 surface is — **UNPROVEN**, and I will not guess at it

The deltas above the slab top are **consistent across two scenes**: IronBastion `2.07 − 1.50 = 0.57`
and mage_enclave `1.32 − 0.80 = 0.52`; goal mapping `+0.22` and `+0.19`. Two different slab heights,
two different spire models, near-identical offsets. **That rules out the spire art** — different art
would give different deltas — and points at something applied uniformly. Candidates exist (a plinth
on the shared fallback path, a prop class the dresser scatters into the keep zone, a voxelisation
artefact) but I have measured none of them, so per CLAUDE.md §11B they stay candidates.
`lastCorner=(x,y,z)` on the next bake names that surface's XZ, which is what identifies it.

### A LIVE FINDING that may be the real "troops stop and do nothing" — not this silo

`TroopController` measures its attack range to the objective's **CENTRE**, not its surface:
`float sqr = (dmg.WorldPosition - transform.position).sqrMagnitude` (`TroopController.cs:974`),
tested as `nearestObjectiveSqr <= _attackRange * _attackRange` (`:1120`), with
`_attackRange = 2.5f` for a footman (`:82`; the file's own header at `:20` notes the Archer's 14
makes it "a standoff fighter"). **A melee troop must therefore stand within 2.5 m of the centre of a
multi-metre-wide, 14 m-tall spire — i.e. inside it — before it will ever swing.** If that is what it
means, troops that DO reach the platform still stop and do nothing, which is exactly the symptom the
owner described. `RaidAssaultAi.BiasMoveDestination(..., _attackRange)` at `:778-779` may or may not
compensate; **I did not open it, so this is a candidate, not a conclusion.** WO-1746 silo —
reported, not touched. Worth its own ticket.

---

## 4c. `OwnedTown_IronBastion` — who writes it, and the smallest safe fix

**Nothing regenerates it, and `BuildAllRaidScenes` never touches it** — it builds only the configs in
`scene-configs.json` (`RaidBaseGenerator.cs:508-515`, `foreach (var id in ids) BuildSceneFor(id)`).
That is why it still carries `goal=(0, 0.02, 0)` with `mappedDy=1.56` — the buried spire, unfixed.

Two editor scripts WRITE that scene, and **neither rebuilds it** — both patch it in place:
- **`Assets/Editor/OwnedTemplateIdentityBake.cs:14-45`** (`OwnedTemplateIdentityBake.Run`) — reads
  poses from `RaidBase_IronBastion`, resolves each match in the owned town **by pose**
  (`OwnedTownScenePose.TryResolve`), stamps `OwnedTemplateIdentity`, saves. Layout preserved.
- `Assets/Editor/OwnedTownReconstructionProof.cs:13-43` — opens it twice as a proof harness.

(`Assets/Editor/OwnedTownPracticeSceneBuilder.cs:14-33` only READS it and saves to a **different**
path, so it is not a writer of this scene.)

> ### ⚠ A HAZARD THIS LANE'S OWN FIX CREATED — read before running the identity bake
> `OwnedTemplateIdentityBake.Run` captures poses from `RaidBase_IronBastion` **including the
> `RaidSpire`** (`:22` accepts a node with `WallSegment`, `DefenseTower` **or** `RaidSpire`) and then
> resolves the counterpart in the owned town **by pose**. The pass-2 reseat moved the source spire's
> Y by 1.50 m while the owned town's spire is still at y≈0.02. If `OwnedTownScenePose.TryResolve`
> matches on position, that resolve now **fails and throws** (`:37`). **Do not run that bake until
> the owned town's spire is reseated too, or the two are 1.5 m apart by construction.** I did NOT
> open `OwnedTownScenePose.TryResolve` to confirm its match tolerance — that is one read, and it
> decides whether this is a blocker or a non-event.

**Smallest safe fix — one object's Y, no regeneration, nothing else touched.** The operation already
exists and is already proven on four scenes: `RaidBaseGenerator.ReseatSpireOnKeepPlatform` (find
`KeepPlatform`, `Physics.SyncTransforms`, measure the slab, assert the spire is over the footprint,
seat on the measured top). It needs only a one-scene entry point that opens
`Assets/Scenes/OwnedTown_IronBastion.unity`, applies that single reseat, logs the before/after Y and
the lift, and saves — **moving exactly one transform and creating, deleting or re-parenting
nothing.** I have NOT written it: the owner's captured-town layout is protected canon and this goes
to her first. On approval it is a ~20-line editor method plus a menu item, and its own log line is
the proof (`spire y 0.02 -> 1.52, lift=1.50m`).

---

## 5. For the lead — registration line, verbatim (NOT added by this lane)

Add to `Assets/Editor/Regression/DataRegression.cs`, next to the other raid suites (they sit at
`:753-759`), in the same shape as `raid-base-layout` at `:759`:

```csharp
// WO-1749 — CONNECTIVITY, not triangulation. RAID_NAV_BAKE_OK was green while every troop in the
// owner's Seeker session logged routeObj=PathPartial (1650x, PathComplete 0x): a keep platform that
// bakes as a walkable ISLAND produces the same "N verts / M tris" line as one the ramp joins.
DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-keep-reach suite", () => { if (!DeNelle.Editor.Regression.RaidKeepReachRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-keep-reach] " + r); });
```

Note for the lead: this suite calls `EditorSceneManager.OpenScene(..., Single)` on every raid scene
and restores the previously-active scene when it had one, so place it where an open-scene swap is
acceptable (i.e. alongside the other scene-opening suites, not between two that share scene state).
It also bumps the suite count in the `REGRESSION_OK <n>/<n>` marker.

---

## 6. Gate results this lane DID run

| Check | Result |
|---|---|
| `python tools/gate_brace.py` on all THREE files (the gate's OWN rule, per CLAUDE.md §1) | `GATE_BRACE_SUMMARY bad=0 of 3`, **exit 0** — re-run after every edit, including pass 2 |
| Raw brace balance | `RaidBaseGenerator.cs` 379/379 · `RaidNavBake.cs` 70/70 · `RaidKeepReachRegression.cs` 40/40 |
| NUL bytes (`\x00`) | 0 in both files |

**NOT run (hard constraint — the lead holds the Unity lock):** compile gate, `DataRegression`,
`RaidNavBake.BakeAll`, any build, any git operation. **The code has never been compiled.** Treat
every line above as a claim until `COMPILE_GATE_OK` and a fresh bake say otherwise.

---

## 7. Acceptance status

| WO acceptance item | State |
|---|---|
| Ramp slope vs agent max slope, both numbers cited | **DONE** — 11.43° / 12.28° / 6.16° vs 45° (§1) |
| Instrument: bake-time `CalculatePath` courtyard→spire, status + corners + last corner Y | **DONE** (§4) — plus two discriminating legs and the goal-mapping readout |
| Fix the PROVEN cause | **DONE in pass 2, lead-authorised** (§3b): the ordering defect that buries the objective inside the keep slab is fixed at the placement — the spire's Y now has one owner and decides after the dresser, with the lift measured off the slab. (a)/(b) stay disproven; **(c) stays unproven** and the instrument settles it. |
| Regression failing when courtyard→spire is not `PathComplete` | **DONE, then the criterion was CORRECTED in pass 3** (§4b): `PathComplete` is retired as brittle-by-construction against a solid objective; the suite now fails when the route does not **ARRIVE** within the objective's measured reach. Registration line still handed over unregistered (§5). |
| Fresh bake reports `PathComplete` for every raid scene | **BLOCKED on the lead's bake** |
| Device shows `routeObj=PathComplete` after breach | **BLOCKED on a device run** |

---

## 8. §4c FOLLOW-UP — 2026-09-15, appended by the WO-1753 edit-only lane (body above UNCHANGED)

The §4c entry point this RESULT proposed but did not write is now **written, and deliberately NOT run**.

**`DeNelle.Editor.RaidBaseGenerator.ReseatOwnedTownSpire`** —
`Assets/Editor/WallTools/RaidBaseGenerator.cs:520-631` (menu item
`Defenders/Walls/Reseat Owned Town Spire (OwnedTown_IronBastion)`, batchmode-callable by that exact
method name). It opens `Assets/Scenes/OwnedTown_IronBastion.unity`, applies the already-proven
private `ReseatSpireOnKeepPlatform`, and saves. **One transform moves; nothing is created, deleted,
re-parented or re-dressed.** The helper is passed the `KeepPlatform`'s own root (it uses `root`
only to find the slab) and the scene's single `RaidSpire`.

**It REFUSES rather than guesses, and a refusal never saves** (CLAUDE.md §11B) — not exactly one
`RaidSpire`; not exactly one `KeepPlatform`; or the spire's Y did not move (`|Δy| < 0.001`), which is
how the helper's four silent-return branches surface. A no-op that still saved would reserialize the
owner's protected scene for nothing.

**Markers — judge on a FRESH log, never the exit code:**
`OWNED_TOWN_SPIRE_RESEAT_OK <scene> spire y <before> -> <after> lift=<n>m` on success,
`OWNED_TOWN_SPIRE_RESEAT_FAIL <why>` otherwise. Both strings were greped before minting and appear
nowhere else in `Assets/` or `WorkOrders/`.

**Acceptance for the lead, because the scene is owner-authored:** after the run, `git diff --stat` on
`Assets/Scenes/OwnedTown_IronBastion.unity` should be ONE hunk — the spire's `m_LocalPosition.y`. If
the save reserializes more than that, stop and show the owner before committing.

### The identity-bake hazard §4c flagged: **NON-EVENT, and now proven rather than suspected**

`OwnedTownScenePose.TryResolve` (`Assets/_Modules/Village/World/Camps/OwnedTownScenePose.cs:55-93`)
**never compares the node's own position.** With a `templateStructureId` it matches on the id and on
the **PARENT's** `localToWorldMatrix` (`:73-76`); without one (the identity bake's `legacy` first
pass) it walks the captured sibling-index path and compares the NAME (`:79-91`). The gate before both
(`:58`) is `OwnedBaseProgression.ValidatePose`
(`Assets/_Modules/Core/State/OwnedBaseProgression.cs:231-255`), which checks tokens, path integers,
finiteness, non-zero scale and quaternion normalization — **no Y bound, no position tolerance at
all.** `SeatOnSurface` moves the spire's own GameObject, not its parent, so neither the parent frame
nor the sibling path nor the name changes. **`OwnedTemplateIdentityBake.Run` does NOT throw because
of the 1.50 m reseat** — and the `poses.Count != 221` census is a count, which a move cannot change.
(Unproven and out of scope: whether that bake passes for any OTHER reason — it has not been run here.)

