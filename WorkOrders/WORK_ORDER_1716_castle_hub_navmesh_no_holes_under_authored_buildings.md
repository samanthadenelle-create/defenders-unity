# WORK ORDER 1716 - Castle hub navmesh gizmo shows one unbroken sheet, no holes under authored buildings

**Status:** READY TO IMPLEMENT - owner live Unity Editor observation, RCA lane assigned
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from the owner inspecting `Main_Castle_Overworld`
directly in the Editor after reporting "footprint issues with the castle hub, guessing something with
the bake" earlier the same session

## 1. Owner report, verbatim (across the session)

Earlier: "device is attached. seeing footprint issues with the castle hub guessing something with the
bake. i can check in unity if you want." Later, in the Editor: "the blue is the navmesh gizmo so you
can see a few large areas" (screenshot of the courtyard, navmesh overlay covering essentially the
entire ground plane with no visible carve-outs under any placed object).

## 2. What is visible in the screenshot (primary evidence)

Navmesh gizmo screenshot: the blue walkable-area tint covers the whole courtyard inside the walls as
ONE CONTINUOUS SHEET - no visible gaps under the small huts, the tree, the benches, the crates, or any
other placed content. On a correctly carved bake, solid structures should punch holes in the walkable
area so pathfinding cannot route through them.

## 3. What is PROVEN this session (Inspector fact + source read, not inferred)

- Owner selected the "Workbench_Decorated" object in `Main_Castle_Overworld` (the same authored castle
  root WO-1710 registered today: `AuthoredCastleStorefront` component, `Canonical Id: workshop`,
  `Legacy Name: Crafting`) and screenshotted its Inspector.
- A real, configured `Box Collider` component IS present on the object (`Size 3.00012 x 2.60012 x
  1.50012`, `Center (0, 1.3, -5.9604...)`).
- The `Building` component's own `Footprint / Blocker` field reads **None (Box Collider)** in the
  saved scene - the object-reference slot is unassigned despite a usable collider sitting right on the
  same GameObject.
- `Assets/_Modules/Village/Buildings/Building.cs:373-377` - `EnsureBlocker()`:
  ```
  if (_blocker == null) _blocker = GetComponent<BoxCollider>();
  if (_blocker == null) _blocker = gameObject.AddComponent<BoxCollider>();
  ```
  This method WOULD correctly find and wire the existing collider - it does not need one to be
  pre-assigned, it falls back to `GetComponent`. So the missing serialized reference is not itself
  fatal, AS LONG AS `EnsureBlocker()` actually runs for this object at some point.
- `EnsureBlocker()` is called from exactly two places, both inside `Configure(...)` overloads
  (`Building.cs:172` and `:203`). The XML doc on the first (`:160-163`) states it is "Called by
  `VillageController` right after instantiation," and the second's doc (`:175-183`) says it is called
  "by `VillageController` for the fixed buildings and by the build menu for a player-placed one."

## 4. What is NOT proven - instrument before fixing (CLAUDE.md section 12)

- **The load-bearing open question:** does anything call `Configure(...)` on an AUTHORED, hand-placed-
  in-scene castle object (like this Workshop root) at all? The doc comments describe two known callers
  - `VillageController` for "the fixed buildings" and the build menu for player-placed ones - neither
  of which obviously describes an object placed directly into the saved `.unity` scene by hand. If
  `Configure` never runs for this class of object, `_blocker` stays null forever at runtime, and
  whatever consumes `_blocker` for navmesh-carving purposes never sees a collider for this building,
  regardless of the fallback logic above.
- Whether "fixed buildings" in `VillageController`'s own vocabulary INCLUDES authored castle roots like
  this one, or refers only to a different, narrower set (e.g. only the Heart of Elarion or similar
  singleton fixtures) - read `VillageController`'s actual call sites before assuming either way.
- Whether the navmesh BAKE ITSELF (NavMeshSurface settings, static flags, which layers/components it
  reads to carve holes) reads `Building._blocker` at all, or reads Unity's standard `NavigationStatic`
  flag / a `NavMeshModifier` component instead - if it's the latter, this Building-script finding may
  be a red herring and the real gap is a missing `NavMeshModifier`/static flag on authored objects,
  not the `_blocker` field. Check both before concluding.
- Whether this affects ONLY objects sharing this authored-castle-storefront pattern, or every hand-
  placed object in the scene (the tree, benches, crates visible in the screenshot too) - check a
  second, non-`AuthoredCastleStorefront` object (e.g. the tree) for the same missing-wiring pattern
  before concluding scope.
- Gameplay impact: confirm whether this is purely a cosmetic-gizmo concern or an actual pathing defect
  (can a hero/enemy currently walk through these buildings on device) - the RCA should look for or
  describe a way to prove real in-game pathing, not just the edit-time gizmo.

## 5. Acceptance criteria

- [ ] RCA lane identifies, with citation, what the navmesh bake actually reads to carve holes (the
      `Building._blocker` reference, a `NavMeshModifier`/static flag, or something else).
- [ ] RCA lane proves or disproves whether `Configure(...)` runs for authored/hand-placed castle scene
      objects, and cites the actual call chain (or its absence).
- [ ] RCA lane states scope: this object only, the whole authored-castle-storefront family, or every
      placed object in the scene.
- [ ] RCA lane states whether this is a real pathing defect on device or purely a bake/gizmo artifact.
- [ ] Fix (separate lane once cause is proven) ensures every solid placed object in the home hub scene
      correctly excludes its footprint from the baked navmesh, using whichever mechanism the RCA proves
      is authoritative - reusing the existing carving mechanism, not inventing a parallel one.
- [ ] Regression or bake-time check proving navmesh holes exist under authored castle content.

## 6. What NOT to touch

- Do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` -
  those already handle nav-carving correctly for RAID arenas (WO-1703/1704, shipped today); this ticket
  is about the HOME HUB scene, a different bake entirely.
- Do not hand-edit `Main_Castle_Overworld.unity` directly - any structural fix goes through the
  sanctioned castle-builder/scene-generation tooling, never a manual scene edit.

## Live in-Editor bake result (owner, 2026-09-14, NOT saved - diagnostic only)

Owner ran a manual navmesh bake from inside the open Editor session and screenshotted the result. This
is a MIXED result, which is itself load-bearing evidence: it is not a uniform bake-config failure.

- **Holes correctly carved** for: the three back-row huts (each shows a distinct pale-blue rectangular
  cutout matching its footprint) and one gap near the water-trough prop right of center.
- **NO hole carved** (sitting flush on the solid teal mesh) for: the tree, the left-side pair of huts
  (including the authored "Workbench_Decorated"/workshop root from section 3), the bench, and the
  rock/crate cluster lower-right.

This rules out "the bake settings are globally broken" - some objects DO carve correctly. It points
squarely at PER-OBJECT setup being inconsistent: whatever marks an object for the navmesh to notice
(Navigation Static flag, NavMeshModifier component, or the `Building._blocker` wiring from section 3)
is present on some objects and missing on others. The RCA lane's fastest path: compare the Editor
static-flag/component setup of one "works" object (a back-row hut) against one "doesn't work" object
(the workshop root, or the tree) directly, rather than reading source alone.

## PRIMARY FINDING, reframed by the owner: this is an OVERSIZED footprint, not a missing hole

Owner, verbatim: "see the lumbermill and to the left of it still has a huge footprint. thats the real
issue." Confirmed via Inspector screenshot of the `LumberMill` GameObject in `Main_Castle_Overworld`:

- **Transform: Position (17.05, -0.12, 0), Rotation (-90, 0, -96.04), Scale (3.0888, 3.0888, 3.0888)**
  - a uniform scale over 3x, on a model rotated -90 degrees on X.
- `AuthoredCastleStorefront`: Canonical Id `collector_lumbermill`, Legacy Name
  `Lumbermill_Wood_Storefront` - this is the exact `bakedTwins` entry WO-1710 registered today
  (`structures-catalog.json`, `collector_lumbermill` row: `"bakedTwins": ["Lumbermill_Wood_Storefront"]`).
  Catalog's authored `placement.footprint` for this row is a modest `4.9` - the scene's actual scale
  does not match what the data authors.
- `ResourceCollector` (Building Id `lumbermill`) and a `Box Collider` are both present on the object.
- The rendered mesh (`tripo_node_a0ee06b6`, material `OwnerCastle_04`) reads visually as a small
  bench/worktable, far smaller than the huge pale-green ground patch shown selected beneath/around it
  in the scene view - the patch is the actual footprint/exclusion zone, and it is dramatically larger
  than the visible model.

**Known precedent for this exact mechanism**, cited verbatim from `structures-catalog.json`'s own
`_heightCadence` note (read earlier this session): the `collector_farm` row hit the identical bug -
"its (-90,0,0) euler stands the 0.391 axis up, so the 5.6 m height target came out as scale 14.34 and a
14.00 x 14.34 m footprint against a 2.8-5.8 m family" - a flat-lying Tripo model rotated upright by a
-90 X euler, whose fit-to-height code then measures the now-tiny "flat" axis and divides the target
height by it, producing a large uniform scale that inflates the FOOTPRINT along with the height because
the fit is uniform. `collector_farm`'s fix was a `maxFootprint` ceiling, not touching `heightMul`
(section documented at length in that same note). `collector_lumbermill`'s `-90` X rotation and `3.09`
uniform scale match this failure shape closely enough that it should be the RCA lane's first hypothesis
to test, not a fresh investigation from zero.

**Also named by the owner, not yet inspected: "to the left of it" - likely the neighboring collector
(foundry or silo, part of the same `_containerScaleNote2026_08_26` family) - check for the same
Transform pattern before assuming this is lumbermill-only.**

This reframes the ticket's priority: the oversized footprint (not the missing-hole question from
sections 1-4) is the owner-designated real issue. Both may share a root mechanism (a badly-fitted
Tripo model produces both a huge collider AND an inconsistent navmesh carve), or they may be separate -
the RCA lane should determine whether fixing the footprint/scale also resolves the carve-hole
inconsistency, or whether both need independent fixes.


## CORRECTION 2026-09-14: the previous section's hypothesis is DISPROVEN by a live test

Owner moved the `LumberMill` GameObject to a new position and re-baked. Result (screenshot): the
oversized pale-green square footprint STAYED at the ORIGINAL location - it did NOT follow the object.
The building's NEW position shows a normally-sized, correctly-carved hole (matching the "working"
back-row huts from the earlier bake test).

**This disproves the "LumberMill's own 3.09x scaled Transform/collider directly produces the oversized
exclusion zone" hypothesis** from the previous section - if the collider were attached to and derived
from that GameObject's own Transform, moving the object would have moved the zone with it. It did not.

**Corrected working hypothesis, NOT YET PROVEN:** the oversized green square is a SEPARATE artifact
independent of the LumberMill GameObject - candidates, in order of likelihood, to be confirmed by
clicking directly on the square in the Scene view (owner asked to do this next):
1. A stale/orphaned GameObject left at that location from an earlier layout iteration, unrelated to
   the current LumberMill object - i.e. a leftover placement marker or an old baked twin that was
   never cleaned up when the building was re-authored or moved previously.
2. A persisted `BaseLayout`/placement-grid record with no live GameObject backing it at all (a "ghost"
   footprint reserved in save/layout data that the nav or grid system still excludes).
3. Baked directly into the ground/terrain texture or a decal plane, not a separate collider-bearing
   object - would mean this was never a nav-mesh/footprint issue in the code sense at all.

Do not assume which of these it is - the RCA lane's first step is identifying exactly what object (if
any) selecting that square returns, before theorizing further.


## RESOLVED (partially): the square is an invisible CastleBarracks, not the lumbermill

Owner clicked directly on the oversized green square. It selected a GameObject named `CastleBarracks`
(prefab `Military_Barracks`), NOT anything related to the lumbermill - the earlier sections'
lumbermill-scale hypothesis is now fully superseded by this, not just disproven by the move test.

**PROVEN from the Inspector screenshot:**
- Transform: Position (16, 0, -4), Rotation (0, -75.964, 0), Scale (0.6, 0.9, 0.6).
- **`Mesh Filter` and `Mesh Renderer` both read `(Removed)`** - this object has NO visible model.
  Nothing renders there; the object is completely invisible in normal play.
- A `Mesh Collider` IS still present (mesh `military-barracks`), so it still physically collides.
- A `Nav Mesh Obstacle` IS still present and active: `Shape Box`, `Center (0, 2.54145, 0)`,
  **`Size (16.90712, 5.08288, 14.51318)`**, `Carve` checked, `Carve Only Stationary` checked. That size
  is dramatically large relative to the object's own small (0.6/0.9/0.6) scale - the obstacle box does
  not match the visual/collision footprint a barracks should have, and it is this oversized, actively-
  carving box that produced the huge exclusion square in both bake tests.

**HYPOTHESIS, NOT YET PROVEN - cross-reference to WO-1710's original barracks report:** the owner's
very first report tonight was troop training refusing to recognize a placed, visible barracks, and
that she "tried to remove the barracks and re-add barracks... but once I removed it, I still didn't
have any chance to re-add it." This invisible `CastleBarracks` object, sitting in the SAME scene with
no renderer but a live collider and an oversized nav obstacle, is consistent with a removal operation
that stripped only the MeshFilter/MeshRenderer instead of destroying or properly cleaning up the
GameObject - leaving a ghost that (a) still occupies the barracks singleton slot, which would explain
why re-adding a new one never worked, and (b) still carves a wrong-sized navmesh hole nobody can see
the source of. NOT PROVEN: whether this object predates tonight's session, is a leftover from the
owner's in-session removal attempt, or is unrelated to that report entirely - the RCA lane should
check the object's creation/edit history if possible (scene file diff history, or ask the owner
whether this GameObject existed before her removal attempt) rather than assume the connection.

## Acceptance criteria addendum

- [ ] RCA lane determines whether `CastleBarracks` (this specific GameObject) is the leftover from
      the owner's barracks removal attempt referenced in WO-1710, or a separate, unrelated object.
- [ ] Whatever "remove structure" code path exists must fully clean up a removed structure (destroy
      the GameObject or clear its collider/NavMeshObstacle along with its renderer) - a partial
      removal that strips only the visual is its own defect regardless of this specific instance.
- [ ] The `Nav Mesh Obstacle` Size mismatch (16.9 x 5.1 x 14.5 on a 0.6/0.9/0.6-scaled object) should
      be explained - was it hand-authored against the wrong base mesh, or inherited incorrectly.

## CONFIRMED 2026-09-14: hypothesis proven correct, fix NOT yet applied (owner deleted diagnostically only, told not to save)

Owner deleted the ghost `CastleBarracks` GameObject in the open Editor session and re-baked. Screenshot
confirms: the oversized square is gone; every remaining structure shows a normally-sized carved hole.
This PROVES the invisible, renderer-stripped `CastleBarracks` (Mesh Filter/Mesh Renderer both
`(Removed)`, oversized `NavMeshObstacle` Size `16.90712 x 5.08288 x 14.51318`) was the entire cause of
the oversized-footprint symptom - not the lumbermill, not a bake-config issue, not a family-wide
authored-structure problem.

Owner was told NOT to save this deletion - it was a diagnostic-only test. The permanent fix still needs
to land through the sanctioned path:
1. Remove/clean up this specific leftover `CastleBarracks` GameObject properly (via tooling, not a
   manual scene save) so the fix is reproducible from a fresh clone.
2. Find and fix whatever "remove/replace structure" code path can strip a MeshFilter/MeshRenderer
   without also destroying or properly clearing the Collider/NavMeshObstacle - this is the root defect;
   this specific barracks is one instance, but the same code path could do this to any structure a
   player removes.
3. Cross-check against WO-1710's original report: does removing this leftover, plus WO-1710's already-
   shipped fix, together resolve troop training + the original "couldn't re-add a barracks" symptom on
   a fresh test? This needs an owner playtest to close, not just a code read.
