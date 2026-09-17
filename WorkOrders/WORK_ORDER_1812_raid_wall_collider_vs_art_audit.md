# WORK ORDER 1812 — raid wall collider vs. art: the baked-scene audit oracle

**Status:** IMPLEMENTED
**Silo:** Combat/AI + Editor regression (read-only everywhere except the one new oracle + its registration)
**Opened:** 2026-09-16
**Parent:** WO-1808 (`[enemy-tower-wall-los]`) — this is the other half of that finding
**Sibling lane (do not collide):** WO-1807 owns `Assets/Editor/WallTools/*` and the `RaidBase_*` re-bake

---

## 1. WHY — the raid wall's collider has NO runtime author

Every claim below was re-read at source on 2026-09-16. Nothing here is copied from a doc.

| Fact | Proof, opened at source |
|---|---|
| A raid `WallSegment` never runs `Configure()` → `RebuildCollider()` | `Assets/_Modules/Village/Walls/WallSegment.cs:511-512` says so in its own words: *"raid walls never get Configure()'d, so `_height` sits at its serialized default for them"*. `Configure` is the only caller of `RebuildCollider` (`:335-343`). |
| `RebuildCollider` is the ONLY place that sizes the box AND the ONLY place that assigns the layer | `WallSegment.cs:632-648` — `_blocker.size = new Vector3(_length,_height,_thickness)`, then `int structureLayer = LayerMask.NameToLayer("Structure"); if (structureLayer >= 0) gameObject.layer = structureLayer;` |
| `Awake` does not repair either | `WallSegment.cs:626-629` — it caches `_blocker` and returns. No size, no layer. |
| The turret's LoS is a `Structure`-masked linecast from `position + up*2` | `Assets/_Modules/Village/Buildings/DefenseTower.cs:965-970` (`BlockedByWallAt`), muzzle literal also at `:785`, `:1032`, `:1459`, `:1482` — **five sites, no `MuzzleOffset` const.** |
| `Allegiance == 1` is `EnemyOwned` | `DefenseTower.cs:50-56` (`PlayerOwned = 0`, `EnemyOwned = 1`). |
| `Structure` is layer **8** | `ProjectSettings/TagManager.asset:11-20` — Default, TransparentFX, Ignore Raycast, Tower, Water, UI, Building, Enemy, **Structure**. |

**Consequence:** for a raid wall the collider height and the physics layer are *whatever the bake wrote*.
A wall whose collider is shorter than its art, or whose GameObject is off layer 8, is invisible to
`BlockedByWallAt` — and the player, who can see a wall, watches the turret shoot through it. That is the
owner's WO-1808 wording exactly: *"the tower is attacking through the wall, maybe over but feels like
through"*. WO-1808 fixed the **pick** (`AcquireParty` now calls the gate). This ticket pins the **scene
data the gate reads**, which nothing else in the tree checks.

---

## 2. THE YAML PRE-READ (evidence, 2026-09-16, before the oracle has ever run)

Measured with a Python parser over the four `Assets/Scenes/RaidBase_*.unity` files at HEAD
(`git status Assets/Scenes/` clean — these are the committed bakes). Full TRS composition: quaternion
rotation + non-uniform scale chained through every parent, and prefab-instance roots reconstructed from
`m_Modifications`, so the world numbers are real and not "assumed identity parents". World collider top =
`worldPos.y + rotate(localCenter.y ± size.y/2 × scale.y)`, taken as the max over all 8 box corners.

| Scene | WallSegments | layer | collider world span | enemy towers | muzzle y | tallest wall top ≤12 m | **min margin** |
|---|---|---|---|---|---|---|---|
| `RaidBase_IronBastion` | 158 | **all 8** | 0.00 → **4.00 m** (scaleY 5.057, size.y 0.7910) | 10 | 2.01 (all) | 4.00 | **+1.99 m** |
| `RaidBase_fortified_garrison` | 118 | **all 8** | 0.00 → **5.00 m** (83 × scaleY 3.412/size.y 1.4656; 35 × scaleY 5.057/size.y 0.9887) | 7 | **4.50** on six, 2.12 on one | 5.00 | **+0.50 m** |
| `RaidBase_mage_enclave` | 158 | **all 8** | 0.00 → **4.00 m** | 10 | 2.01 (all) | 4.00 | **+1.99 m** |
| `RaidBase_raider_camp_small` | 58 | **all 8** | 0.00 → **4.00 m** | 4 | 2.00 (all) | 4.00 | **+2.00 m** |

Every collider in all four scenes: `m_Enabled: 1`, `m_IsTrigger: 0`, exactly one `BoxCollider` per
segment, zero segments without one. **No off-layer wall and no disabled/trigger blocker exists today** —
so the oracle's `offLayer` and `shortCollider` counts are expected to come back **0** on the first run,
and the margin line is the number that matters.

Per-tower detail, `RaidBase_fortified_garrison` (the owner's scene):

| Tower | pos | muzzle y | walls ≤12 m | tallest top | margin |
|---|---|---|---|---|---|
| `Watchtower_Mage_0` | (2.68, **2.50**, −16.94) | 4.50 | 4 | 5.00 | **+0.50** |
| `Watchtower_Mage_1` | (−2.68, **2.50**, 16.94) | 4.50 | 3 | 5.00 | **+0.50** |
| `Watchtower_Archer_0` | (43.89, **2.50**, 6.95) | 4.50 | 4 | 5.00 | **+0.50** |
| `Watchtower_Archer_1` | (20.17, **2.50**, −39.59) | 4.50 | 4 | 5.00 | **+0.50** |
| `Watchtower_Archer_4` | (6.95, **2.50**, 43.89) | 4.50 | 4 | 5.00 | **+0.50** |
| `Watchtower_Archer_2` | (−31.42, **2.50**, −31.42) | 4.50 | **0** | — | n/a |
| `Watchtower_Archer_3` | (−39.59, **0.12**, 20.17) | 2.12 | 4 | 5.00 | +2.88 |

Six towers sit at y 2.50; **five** of them have a wall within 12 m, and all five read **+0.50 m**.
`Archer_2` stands clear of the perimeter, so the margin has five samples, not six. Ranges are 26.95 m on
six of the seven and 16 m on one — i.e. these turrets reach far past their own wall.

### ⛔ CORRECTION to the WO-1808 hand-back wording
That hand-back said the muzzle *"clears the wall top by only 0.50 m"*. **It is the other way round:** the
muzzle at 4.50 sits **0.50 m BELOW** the collider top at 5.00. That is *why* the linecast still hits and
why the WO-1808 fix works. A later seat reading "clears by 0.50" would raise the wall to fix a problem
that is already the right way up — and would break it by "fixing" it.

### The real fragility is the 2.50 m TOWER PIVOT, and it is an ART ACCIDENT
`RaidBaseGenerator.PlaceTowerProp` calls `SeatOnGround(go)` →
`SeatOnSurface(go, 0f, …)` (`Assets/Editor/WallTools/RaidBaseGenerator.cs:2121-2143`), which lifts the
object until its **lowest rendered point sits at y = 0**. So a baked `transform.position.y` of 2.50 does
not mean "this tower was raised 2.5 m"; it is **consistent with** that prefab's pivot sitting 2.50 m above
its own mesh floor, while the other three scenes' identical seat produced pivots at y ≈ 0.01.
`RaiseKeep`'s platform is 1.5 m (`RaidBaseDresser.cs:1863-1866`) and no `Mound`/`Plateau` object exists in
the garrison scene (only one `KeepPlatform`), so the platform is not the source either.

⚠ **THAT MECHANISM IS NOT MEASURED — §11B.** It is a static read, and two later steps could produce the
same 2.50: `PlaceTowerProp` continues past the seat into a height-fit block (`ScaleToHeight` on the root,
per `RaidBaseDresser.cs:1286`) that may rescale and re-seat, and `ReplaceChildrenWith` later clads
`Watchtower_*` with a `/Visual` whose bounds start near y ≈ 0.05 while the host stays at 2.50
(`RaidPostOrientationRegression.cs:20-23`). **The conclusion below holds under every one of those
readings** — the pivot is art-dependent either way — but the mechanism sentence is unproven. Two cheap
proofs, either is enough: a `FlowTrace.Step` of `lift` inside `SeatOnSurface` on WO-1807's next bake, or
reading a garrison tower's root `Renderer.bounds.min.y` against its `transform.position.y` on this
oracle's first real run.

**Therefore `position + up*2` is measured from an arbitrary art pivot.** Swap one watchtower FBX for
another with a 3 m pivot offset and every garrison turret silently starts shooting over its own wall,
with no code change, no data change, and nothing in the tree that would notice. That — not the 0.50 m —
is the defect this oracle exists to catch.

### DO NOT IMPLEMENT — the invariant-ownership question for the owner
A 0.50 m margin is fragile by design. Two candidate owners, with numbers:

1. **The muzzle owns it (recommended, not ruled).** Derive the muzzle from the tower's own rendered
   bounds instead of its pivot — e.g. `bounds.min.y + k` or `Mathf.Min(pivot + 2, bounds.max.y - 0.5)` —
   so the height is art-independent. Cost: the `up * 2f` literal is duplicated at **five** sites in
   `DefenseTower.cs` (`:785`, `:970`, `:1032`, `:1459`, `:1482`); this needs ONE `MuzzleOffset` /
   `MuzzleWorld` seam first, or the fix lands in four places and drifts out of the fifth. Nothing today
   guarantees the five stay equal.
2. **The wall collider owns it.** Make the bake emit a blocker tall enough to clear any muzzle — e.g.
   floor the collider top at `maxTowerMuzzle + 1.0 m`. Cost: it couples the perimeter's height to the
   turret roster, it is a **re-bake** of all four scenes (WO-1807's lane), and it makes the wall
   *taller than its art* — which is the mirror-image lie: the shot stops where the player sees sky.

A defensible middle: keep the collider matched to the art (this oracle's pins 3+4), and require
`margin ≥ 1.0 m` from the muzzle side. **Today only `fortified_garrison` would fail such a rule
(+0.50); the other three sit at +1.99/+2.00.** The oracle deliberately asserts only `margin > 0` and
*prints* the number, so the threshold can be ruled on data instead of taste.

### Honest limits of the YAML pre-read — what it CANNOT prove
* **Mesh bounds are not in the YAML.** The dresser's `/Visual` child references an FBX; its renderer
  extents live in the model asset, not the scene. So the pre-read **cannot** compute "collider top vs
  art top" — pins 3 and 4 are decidable **only** in Unity, on the oracle's first real run. What the YAML
  does show is that the host scale chain is shared: `Wall_Outer_SN_0` has `m_LocalScale`
  (7.1248, **3.4117**, 1.8773) and its three children inherit it, so art height = `mesh.y × 3.4117` and
  the collider (`size.y 1.4656 × 3.4117 = 5.00`) uses the same multiplier. Two different scale/size
  pairs in one scene both landing on exactly 5.00 m is strong evidence the generator already derives the
  box from the art — which is why a green first run is the expected outcome, not a surprise.
* Rotations were composed as full quaternions, but **only yaw appears** on these hosts, so the world
  AABB is not inflated by a pitch this parser mis-read.
* It reads `Allegiance` off the serialized `MonoBehaviour`; a tower armed at RUNTIME by
  `GarrisonTurretArmer` would not appear in the table. All 31 towers across the four scenes are already
  serialized `Allegiance: 1`, so for these bakes the two agree.
* It says nothing about whether a running raid honours the linecast. That is
  `[enemy-tower-wall-los]`'s job (WO-1808) and is already proven there.

---

## 3. WHAT WAS BUILT

`Assets/Editor/Regression/RaidWallColliderAuditRegression.cs` — `[raid-wall-audit]`, markers
`RAID_WALL_AUDIT_OK` / `RAID_WALL_AUDIT_FAIL`. Edit mode, no play mode, no bake. Opens each discovered
`Assets/Scenes/RaidBase_*.unity` with `EditorSceneManager.OpenScene(..., Single)`, calls
`Physics.SyncTransforms()` before reading any `Collider.bounds`, and restores the previously active scene.

Per `WallSegment`:
1. `gameObject.layer == LayerMask.NameToLayer("Structure")` → `offLayer`
2. at least one **enabled, non-trigger** `Collider` on the segment itself → `shortCollider`
3. `collider.bounds.max.y >= artBounds.max.y - 0.25` → `shortCollider` (top short by Xm)
4. `collider.bounds.min.y <= artBounds.min.y + 0.25` → `shortCollider` (gap under it)

`artBounds` = encapsulated `Renderer.bounds` of the whole hierarchy (root included). A segment with no
renderer is counted as `noArt` and **fails** — the comparison would otherwise be vacuously green.

Per **enemy-owned** `DefenseTower` (`Allegiance == TowerAllegiance.EnemyOwned`): muzzle =
`transform.position + Vector3.up * 2f`; neighbours = wall blockers whose `bounds.center` is within
**12 m in XZ**; `margin = tallestTop − muzzle.y`; fails when `margin <= 0`.

One `Debug.Log` per scene, pass or fail, exactly:

```
[raid-wall-audit] scene=<name> walls=<n> offLayer=<n> shortCollider=<n> towers=<n> minMuzzleMargin=<m>
```

`minMuzzleMargin=n/a` when no enemy turret has a wall within 12 m — never a sentinel number. Offender
names are capped at 12 per sentence, with `+N more`.

**Degrade-open can never read green.** Each of these is a named FAILURE, not a pass: no `Structure`
layer; zero `RaidBase_*.unity` on disk; a scene with zero `WallSegment`s; a segment with no renderer; a
scene that will not open.

Two things written on purpose:
* `Run` wraps `RunCore` in `try/catch` and returns `false` on a throw. The `Guard.Try` at the
  `DataRegression` call site **swallows** exceptions, so a throwing suite would otherwise read as a pass.
* `RestoreScene` opens an **empty** scene when nothing was open before (the usual batchmode case) instead
  of returning early. Leaving a raid scene loaded would put 100+ `Structure`-layer colliders into the
  physics world, where any later suite that raycasts on that mask would measure this scene instead of its
  own fixture. (`[enemy-tower-wall-los]` is registered at `:351`, so it runs *before* this one and is not
  the victim — but the hazard is general, and the other scene-opening raid suites return early on an empty
  path, so this cleans up after them too.)

Registered ONCE, at `Assets/Editor/Regression/DataRegression.cs:784-792`, immediately after
`[raid-post-orientation]` so the scene-opening suites are adjacent and the ordering is explicit.

## 4. FILES — and what was NOT touched

**Changed (2):**
* `Assets/Editor/Regression/RaidWallColliderAuditRegression.cs` (new)
* `Assets/Editor/Regression/DataRegression.cs` — **one** `Guard.Try` line + its comment block

**Deliberately untouched:** every `.unity` scene; everything under `Assets/Editor/WallTools/`
(WO-1807's lane owns the generator, the dresser and the re-bake); `WallSegment.cs`; `DefenseTower.cs`;
`CLI_LANES_WO_NUMBERS.md` (number pre-assigned by the lead). No git operation, no Unity run, no bake.

## 5. ACCEPTANCE

* [x] `python tools/gate_brace.py` on both files → `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0
* [x] NUL scan on both files → clean (`b'\x00' not in data`)
* [x] Registered exactly once (`grep -c "raid-wall-audit" DataRegression.cs` → the comment + the one call)
* [ ] `COMPILE_GATE_OK` — **lead's gate run.** The new `.cs` has no `.meta` yet; Unity generates it on
      first import and it must go in the SAME commit as the `.cs`.
* [ ] `REGRESSION_OK <n>/<n> suites` with four `[raid-wall-audit] scene=…` lines in the log
* [ ] The four summary lines compared against §2's table. **Expected first run:** `offLayer=0` and
      `shortCollider=0` everywhere; `minMuzzleMargin` 1.99 / **0.50** / 1.99 / 2.00. A `shortCollider`
      count above 0 means pins 3+4 found what only Unity can see (mesh bounds) — report the numbers, do
      not "fix" the tolerance.
* [ ] Owner ruling on §2's invariant-ownership question before any threshold above `> 0` is asserted.
