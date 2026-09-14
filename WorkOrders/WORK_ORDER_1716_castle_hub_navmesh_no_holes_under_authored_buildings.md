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
