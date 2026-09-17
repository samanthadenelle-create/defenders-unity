# WORK ORDER 1807 — raid corner posts and watchtowers read as inverted / upside down

**Status:** IMPLEMENTED
**Opened:** 2026-09-16
**Silo:** Raid art / world dressing (`Assets/Editor/WallTools/*`) — file-disjoint from the HUD and troop lanes
**Reported by:** owner (PO), felt-test on build **2026.09.17.372984**
**Scene:** `Assets/Scenes/RaidBase_fortified_garrison.unity` (Forsaken Camp), her raid 19:55:43–19:59:01 local, 2026-09-16
**Control scene:** `Assets/Scenes/RaidBase_raider_camp_small.unity` (raided 19:45, **no** complaint)

---

## 1. The report, and what we did NOT have

Owner, verbatim: **"corners seem upside down"** and **"towers are inverted."**

Her screenshot never reached the seat and the device `Screenshots/` folder held nothing from
2026-09-16. So step one of this ticket was to **produce the primary evidence**, not to theorise
from a code read (CLAUDE.md §12: static reading LOCATES candidates, it never CONCLUDES).

---

## 2. Evidence in hand at the start (read, not re-derived)

`logs/device/raid-window-1955.txt` — the raid window of today's device logcat.

1. **Every corner post has a Synty clad on a non-URP shader.** Nine such lines, e.g.
   ```
   19:55:44.099 E [Flow:RaidArt] #3 non-URP shader (renders as the URP fallback) |
     path='RaidBase_fortified_garrison/CornerPost_Outer_S/Visual'
     mesh='SM_Bld_Castle_Wall_Tower_M_01' material='Castle_Wall_01'
     shader='Synty/Generic_Basic' bounds=4.3x7.5x4.3m at (49.0,3.8,-48.9)
   ```
   Same shape for `CornerPost_Outer_{N,E,W}`, `CornerPost_Keep1_{N,E,S,W}`,
   `Watchtower_Archer_1` (`4.1x7.5x4.1m`) and `Watchtower_Archer_2`.

2. **The corner post HOST carries a SECOND, polyperfect renderer of its own.**
   ```
   19:55:49.412 W [Flow:Raid] [wo1639-marker] t+5s OVERSIZED world renderer:
     path=RaidBase_fortified_garrison/CornerPost_Outer_E kind=MeshRenderer
     screenH=1608px (1.34 of screen) camDist=9.1m
     mat=M_10_Brown_Dark_LPUP (Universal Render Pipeline/Lit) tint=0.40/0.29/0.18/a1.00
   ```
   Note the path has **no `/Visual` suffix** — this is the host GameObject itself, on a URP/Lit
   *polyperfect* material, filling **1.34 of the screen height** at 9.1 m.

3. **There is not one line about corner-post rotation anywhere in the capture.** That gap is
   what this ticket had to close.

---

## 3. The three candidate causes, and which one the data supports

| # | Candidate | Verdict |
|---|---|---|
| a | The Synty `/Visual` clad is pitched ±90 X by the wall-panel FBX-flat correction | **DISPROVEN** |
| b | The host FBX imports flat and `PlaceCornerTower` never calls `EnsureUpright` | **NOT the felt defect** |
| c | Two renderers stacked — polyperfect host + Synty clad — a mismatched double tower | **PROVEN. This is it.** |

### (a) is disproven twice over, at two independent levels

**Bounds level** — the captured clad bounds are `4.3 x 7.5 x 4.3 m`, i.e. tall in Y, centred at
y=3.8 with a height of 7.5 → lowest point ≈ **y = 0.05**. A clad pitched −90 X would be
`4.3 x 4.3 x 7.5`. It is not.

**Transform level (baked YAML read, not a render)** — in
`Assets/Scenes/RaidBase_fortified_garrison.unity`:

* `CornerPost_Outer_E` (PrefabInstance block at **:83766–83896**) carries
  `m_LocalRotation` `w=-0.38268343  x=-0  y=0.92387956  z=-0` — a **pure yaw of 225°**, zero pitch,
  zero roll — plus `m_LocalPosition` `(-48.999992, **2.5**, -49)` and **no `m_LocalScale` override**.
* Its `/Visual` child (PrefabInstance at **:122299**, `m_TransformParent: {fileID: 1484694219}` =
  the host's stripped Transform) carries `m_LocalRotation` `w=1  x=0  y=0  z=0` — **IDENTITY**.

The −90 X correction *does* exist and *is* applied — but to **wall panels**. The very next
PrefabInstance in that region is the `steel_wall` panel, carrying
`m_LocalRotation.w = 0.7071068 / m_LocalRotation.x = -0.7071068`. It never reaches a post.

> ⚠ **Both of the "obvious" assertions would have PASSED the bake the owner is looking at.**
> Up-vector · `Vector3.up` on the clad is ~1.0, and its `bounds.min.y` is ~0.05. An oracle that
> asks only those two questions is theatre. Recorded here because it dictates the shape of the
> regression in §6.

### (b) is real in the code but is NOT what she saw

`PlaceCornerTower` (`Assets/Editor/WallTools/RaidBaseGenerator.cs:2072`) sets
`rotation = LookRotation(outward, Vector3.up)` then `SeatOnGround`, and — unlike `PlaceTowerProp`
(`:1535`, which calls `EnsureUpright` at `:1568`) — it **never calls `EnsureUpright`**. So a flat
host FBX *would* survive here.

But it did not happen: the **baked** host rotation for `CornerPost_Outer_E` is pure yaw with
`m_LocalRotation.x = -0` and `.z = -0`. `EnsureUpright` applies a **−90 X**, which cannot produce
that quaternion. Whatever the host mesh's own shape is, nothing tipped its transform.

⚠ **What is NOT proven, and the ticket says so rather than borrowing a nearby number.** The
`Builds/owned-town-chain3.log` from today (15:10) is a **nav-bake / ground-refit** pass over the raid
scenes — its lines for `RaidBase_raider_camp_small` are `[RaidNavBake] refitted RaidGround …`,
`58 destructible walls …`, `4 towers …`. The only `ring 'Outer'`/`SPIRE FIT` **generation** lines in
it belong to config **`iron_bastion`**. So that log does **not** show a regeneration of
`fortified_garrison`, and its absence of `imported FLAT` lines proves nothing about that scene. The
scene files all carry a 15:10 mtime because the nav bake re-saved them, **not** because
`BuildAllRaidScenes` re-ran on them today. The host mesh's own `size.y / max(size.x, size.z)` ratio
— the number that would close candidate (b) outright — is measured by the BEFORE audit's
`ROOT … ratio=` line; see §11.

### (c) is the defect — and it is one line of the dresser

**`Assets/StructureContent/Tower_Medieval_Wood.prefab`** (read at source, 101 lines) is a
**single GameObject** carrying `MeshFilter` (`33000012507805666`), `MeshRenderer`
(`23000013485662024`), `MeshCollider` (`64787272357330344`) — and **`m_Children: []`**.

`RaidBaseDresser.ReskinCombatArt` (`RaidBaseDresser.cs:1204`) routes every `Watchtower_*` and
`CornerPost_*` into `ReplaceChildrenWith(host, towerModel, …)`, which — as its name says —
destroyed **children only**:

```csharp
for (int i = 0; i < host.transform.childCount; i++) { ... doomed.Add(...); }
for (int i = 0; i < doomed.Count; i++) Object.DestroyImmediate(doomed[i]);
var vis = InstantiateVisual(model, host.transform, "Visual", ...);
```

A corner post has **no children**, so that loop replaced **nothing**. The Synty clad was hung as a
new `/Visual` child *on top of* the polyperfect tower that was already rendering on the root. The
baked scene confirms the components survive: every one of those PrefabInstances reads
**`m_RemovedComponents: []`**.

The same shape applies to `Watchtower_*` (built by `PlaceTowerProp`, which instantiates the turret
art onto a root and then `ScaleToHeight`s that root) and to `RaidSpire`.

**Why "upside down / inverted" is the right words for it.** Two towers of different shapes, sizes
and materials occupy the same footprint: a stilted wooden polyperfect tower whose splayed legs and
pitched roof interpenetrate a square Synty stone wall-tower. From hero eye height you see one
silhouette with a roof shape emerging low down and structure where the top should be. There is no
single flipped transform to find — which is exactly why every rotation-shaped theory came up clean.

**Corollary, and it is the arithmetic tell.** `SeatOnGround` seats the **union** of all renderers
at y=0. The clad's own min.y is 0.05, so the clad is *not* the lowest thing — the host mesh is, and
the host had to be **lifted to y = 2.5** to get the polyperfect mesh's belly off the floor. That is
precisely the `m_LocalPosition.y = 2.5` in the baked YAML. Remove the root renderer and the lift
correctly disappears, because the clad then seats itself.

---

## 4. The control scene (owner clarification) — and the value that explains the report

The owner raided `RaidBase_raider_camp_small` at 19:45 with **no** complaint and
`RaidBase_fortified_garrison` at 19:55 with the report. Both are produced by the **same**
`RaidBaseGenerator.BuildAllRaidScenes` entry point and dressed by the **same**
`RaidBaseDresser.ReskinCombatArt`, so the stacking defect is shared-code and **every** `RaidBase_*`
scene carries it. What differs is **what gets stacked on top**.

Read at source from `Assets/Resources/Data/Canonical/scene-configs.json` and
`RaidBaseDresser.DefaultTower` / `DefaultWall` (`RaidBaseDresser.cs:468-473` / `:424-429`):

| | `raider_camp_small` (control, 19:45) | `fortified_garrison` (reported, 19:55) |
|---|---|---|
| `wallTier` | **Wood** | **Iron** |
| `baseRadius` | **31** | **49** |
| `wallSegmentsPerSide` | 9 | 11 |
| `raidDress.kit` | **`hexagon-green`** | **`synty-castle`** |
| `raidDress.wallModule` | `wall_broken` | `SM_Bld_Castle_Wall_01` |
| clad token for posts (`DefaultTower(kit)`) | **`building_watchtower_green`** | **`SM_Bld_Castle_Wall_Tower_M_01`** |
| `centralBuilding` | `tower_ruined_watchtower` | `tower_arcane_spire` |
| `towers[]` | 2x `tower_catapult` | 3x `tower_catapult` + 1x `tower_arcane_spire` |

`SM_Bld_Castle_Wall_Tower_M_01` is exactly the mesh named in the device capture, so the clad token
resolution is confirmed end-to-end for the reported scene.

**The explanatory difference is the MISMATCH, not the stacking.** Both scenes stack, because the host
mesh is the same `Tower_Medieval_Wood` in both. But the garrison stacks a **square Synty stone
wall-tower (`4.3 x 7.5 x 4.3 m`)** onto a **wooden polyperfect stilt tower**: two utterly different
silhouettes interpenetrating. The small camp stacks a green watchtower onto the same host — a much
closer match in shape and material, so the double reads as visual noise rather than as a broken
tower. `RaidPostAudit.AuditAll` measures **all four** baked scenes in one run, so the per-scene
host/clad numbers are in `Builds/raid-post-audit-before.log` rather than argued about.

⚠ **`wallTier` above is the AUTHORED value from `scene-configs.json`.** The
`tier ReinforcedSteel` line in `Builds/owned-town-chain3.log:2907` belongs to the **`iron_bastion`**
config (the `SPIRE FIT config 'iron_bastion'` line is 57 lines below it in the same block), not to
`fortified_garrison`. An earlier draft of this ticket attributed it to the garrison; that was a
misread and is corrected here.

⚠ **The camera distances are measured; the conclusion drawn from them is not.** `camDist=9.1m` on
`CornerPost_Outer_E` and `camDist=78.0m` on `Watchtower_Archer_1/Visual` are captured facts. "That
is why she noticed it in the second camp and not the first" is **inference** — see §10.

---

## 5. The fix (code only; `Assets/Editor/WallTools/*`)

`Assets/Editor/WallTools/RaidBaseDresser.cs` — `ReplaceChildrenWith`, plus two new private helpers:

1. **`StripRootArt(host, clad)`** — removes the host root's `Renderer` and `MeshFilter` **after**
   the clad has been instantiated. The ordering is load-bearing: if the clad model fails to load,
   `ReplaceChildrenWith` returns early and the host keeps its own art, so a post can never end up
   with **no** renderer at all.
2. **The collider is handled deliberately, not left to chance.** A `CornerPost_` clad is
   instantiated with `stripColliders: true`, so the host's own `MeshCollider` is the **only**
   collider on the post. Left alone it would become an invisible *polyperfect-shaped* blocker
   around a *Synty* tower — and, once the 2.5 m lift is gone, one sunk 2.5 m into the ground. So a
   root `MeshCollider` that referenced the mesh being removed is replaced by a **`BoxCollider`
   fitted to the clad's measured bounds** (`CladLocalBox`, host local space; these hosts are
   yaw-only so an axis-aligned local box is faithful). The corner stays solid and the blocker now
   matches what the player can see.
3. **`ReportCladPose(host, clad)`** — the acceptance instrument this ticket was missing. One
   `FlowTrace.Step` per post:
   `[wo1807] pose '<name>': hostEuler=(x,y,z) cladUpDot=… boundsY=[min..max] size=…x…x… ratio=… rootRenderer=none`
   So the next bake log answers "is any post inverted, flat or floating" **from one read**, with no
   device build, no screenshot and no owner playtest.

FlowTrace is **added, never stripped** (CLAUDE.md §12, owner ruling 2026-08-09).

### 5b. Blast radius — `ReplaceChildrenWith` clads three things, so the fix reaches all three

`ReskinCombatArt` (`RaidBaseDresser.cs:1204-1213`) routes `CornerPost_*`, `Watchtower_*` **and**
`RaidSpire` through this one method, so `StripRootArt` reaches all three. That is deliberate — the
stack is the same defect on all three — but it was checked before being left that way:

* **Nothing caches a ROOT renderer or collider on these hosts.** `RaidSpire.cs` has no
  `GetComponent<Renderer>` / `<Collider>` at all. `DefenseTower.cs`'s three self-component calls
  (`:1067`, `:1265`, `:1276`) all operate on a spawned **bolt projectile**, never on the tower root.
* **`RaidNavBake.PrepareMovableTowers` sizes its carving obstacle from
  `GetComponentsInChildren<Collider>(true)` — COLLIDERS, not renderers.** The fix keeps a collider on
  the host (§5 item 2), so the obstacle is still measurable, and it now encloses the art the player
  can actually see rather than a hidden polyperfect footprint.
* **`DefenseTower.EnsureContactCollider` sizes its capsule from renderer bounds** (union of
  `GetComponentsInChildren<Renderer>`). After the fix that union is the clad alone. For the reported
  scene the clad (7.5 m) was already the taller of the two, so the capsule barely moves — but this is
  a real behaviour change and is named here rather than discovered later.
* **`RaidKeepReachRegression.ArrivalRadius` measures the spire footprint off the spire's own
  bounds**, which after the fix is the clad's bounds. The dresser's own note at
  `RaidBaseDresser.cs` (`MapCatalogArt` docblock) records that the generator MEASURES and the dresser
  INSTANTIATES the same catalog-resolved model for the spire, so clad == host model there and the
  radius should not move. **Not independently measured this session** — see §10.
* **The fix is EXACTLY scoped to root-mesh prefabs, and that is not a limitation.** `StripRootArt`
  reads `host.GetComponent<Renderer>()` / `<MeshFilter>()` — the ROOT only. `Tower_Medieval_Wood.prefab`
  has both on the root, which is why the corner posts stacked. A host whose art sits on a CHILD
  instead (the usual shape for a raw FBX import, and `ArcaneSpire_1` is an FBX per the generator's
  own `EnsureUpright` docblock) was **never stacked in the first place**, because
  `ReplaceChildrenWith` already destroyed children. So "root renderer present" and "was stacked"
  are the same condition, and one guard covers both.
  ⚠ **The device capture only PROVES the CornerPost hosts stacked** (the `[wo1639-marker] OVERSIZED`
  line names `CornerPost_Outer_E` and nothing else without a `/Visual` suffix). Whether the
  `Watchtower_*` and `RaidSpire` hosts also carried root art is answered by the BEFORE audit's own
  `rootRenderer=YES/no` line per host — **read it there, do not assume it from this ticket.**
* **`OwnedTown_*` is DERIVED FROM the raid scenes.** `OwnedTownChain`'s header (`:25`) records that
  `OwnedTownSceneBuilder` "has always DERIVED that scene from the raid scene". So `OwnedTown_IronBastion`
  inherits whatever `RaidBase_IronBastion` carries, so it must be re-derived after this fix.
  **That is not a follow-up — it is step 2 of §7's mandatory rebake sequence**
  (`OwnedTownChain.RebuildFromRaid`, which re-derives the town FROM the regenerated raid scene), so the
  owned town and the practice arena are fixed by the same run rather than left behind.

---

## 6. The instruments

**`Assets/Editor/WallTools/RaidPostAudit.cs`** (new) — marker **`RAID_POST_AUDIT_OK <n>`**, distinct
per CLAUDE.md §8. Opens every `Assets/Scenes/RaidBase_*.unity` in edit mode and, for each
`CornerPost_*` / `Watchtower_*`, prints **per renderer**: whether a renderer sits on the host ROOT
(with its mesh, material and bounds), the `/Visual` local rotation, its up-vector dot, its bounds
size, its `min.y`, and `size.y / max(size.x, size.z)` — `EnsureUpright`'s own ratio. It then
photographs one corner post and one watchtower per scene **from hero eye height (1.7 m), standing
outside the ring looking in**, with the same blank-frame detector `DungeonSceneCapture` uses (a
uniform frame is a FAILURE, never "the post is fine").

```
powershell -File .\run-unity-method.ps1 `
  -Method DeNelle.Editor.RaidPostAudit.AuditAll -LogName raid-post-audit.log `
  -ExpectMarker RAID_POST_AUDIT
```
Output: `Builds/raid-post-audit/<scene>_<object>.png`

**`Assets/Editor/Regression/RaidPostOrientationRegression.cs`** (new) — markers
`RAID_POST_ORIENTATION_OK / _FAIL`, registered **once** in `DataRegression.RunAll`
(`Assets/Editor/Regression/DataRegression.cs`, immediately after the `raid-keep-reach` suite).
Four pins per post, on the baked scenes:

1. **No enabled `Renderer` on the host ROOT.** *This is the load-bearing pin* — the other three
   were green on the broken bake (§3).
2. Exactly one `/Visual` child, and it carries renderers.
3. The clad is upright: up-vector · `Vector3.up` **> 0.9** *and* bounds ratio **≥ 0.8**.
4. The clad is seated: `bounds.min.y` within **0.5 m** of the ground.

It implements the rule itself rather than calling `RaidPostAudit.Evaluate`, because
`DeNelle.EditorRegression` does not reference `DeNelle.Editor` (verified in
`Assets/Editor/Regression/DeNelle.EditorRegression.asmdef`) — and an oracle that imports its
subject's own helper cannot catch the subject changing it. Zero scenes, or zero posts across all
scenes, **fails closed** and says so; a missing bake is a gap, not a pass.

---

## 7. Rebake — the raid scenes are BAKED FILES

Never hand-edit a `.unity` (CLAUDE.md §3).

> ⛔ **`BuildAllRaidScenes` MUST NOT BE RUN ALONE.** Its own body says so
> (`RaidBaseGenerator.cs:610-616`, WO-1767): regenerating `RaidBase_IronBastion` **desynchronises the
> owned-town template pair** (`OwnedTown_IronBastion.unity` + `Assets/Resources/OwnedTown/IronBastionTemplate.json`),
> *"which is what broke the capture census in build 2026.09.16.371701"*. An earlier draft of this
> ticket prescribed `BuildAllRaidScenes` → `RaidNavBake.BakeAll` and that would have reproduced that
> break. Corrected here.

The sanctioned sequence, in order:

```
powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes -LogName raid-rebake.log
powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.OwnedTownChain.RebuildFromRaid       -LogName owned-town-chain-1807.log -ExpectMarker OWNED_TOWN_CHAIN_OK
```

Step 1 regenerates **all four** `RaidBase_*` scenes (config-driven entry at `RaidBaseGenerator.cs:596`),
which is what the coordinator asked for: the fix is in shared code, so every scene the generator
produces must be rebaked.

⚠ **Step 1 exists for `fortified_garrison`, `mage_enclave` and `raider_camp_small`. The chain owns
`iron_bastion`.** `RebuildFromRaid` calls `BuildSceneFor("iron_bastion")` itself at its step 1
(`OwnedTownChain.cs:126`), so iron_bastion is regenerated **twice** — once by `BuildAllRaidScenes`,
once by the chain, which then derives the town from *its own* regeneration. That is deliberate, not a
redundant double-bake to optimise away: **do not skip step 2 because step 1 "already did"
iron_bastion**, and do not skip step 1 because the chain "already bakes a raid scene" — it bakes
exactly one. (A per-config batchmode wrapper around the `public static
RaidBaseGenerator.BuildSceneFor(string)` at `:806` would make step 1 surgical. Worth doing; out of
scope for this ticket.)

Step 2 is the **ONE sanctioned chain** and it repairs exactly what step 1 desynchronises: it
re-runs `BuildSceneFor("iron_bastion")`, re-stamps identities, re-derives `OwnedTown_IronBastion`
FROM that raid scene, re-bakes the manifest, reseats the town spire, runs **`RaidNavBake.BakeAll`**
(so no separate nav-bake call is needed — it is step 7 of the chain,
`OwnedTownChain.cs:145`) and re-derives the practice arena. It also closes §5b's `OwnedTown_*`
follow-up **in the same run**, because the town is re-derived from the fixed raid scene.

**How to judge it:** `BuildAllRaidScenes` emits **no `_OK` marker of its own** — only
`[RaidBaseGenerator] baked <n> raid scene(s) from scene-configs.json (<ids>)`. So judge step 1 by
that line **plus** the four `RaidBase_*.unity` mtimes moving, and judge step 2 by
**`OWNED_TOWN_CHAIN_OK`** on a fresh log (the chain withholds it on any of
`OWNED_TEMPLATE_IDS_FAIL`, `OWNED_TOWN_SPIRE_RESEAT_FAIL`, `RAID_NAV_REACH_FAIL`, and requires all
seven of its step markers — `OwnedTownChain.cs:90-108`). Never the exit code (CLAUDE.md §8).

---

## 8. Acceptance criteria

- [ ] `python tools/gate_brace.py` exit 0 and zero NUL bytes on every `.cs` touched.
- [ ] BEFORE audit run: `RAID_POST_AUDIT_FAIL` naming a stacked root renderer on the corner posts
      of the baked scenes — i.e. the instrument reds on the defect it was built for.
- [ ] Rebake all four `RaidBase_*` scenes through `BuildAllRaidScenes` **followed by**
      `OwnedTownChain.RebuildFromRaid` (§7 — never `BuildAllRaidScenes` alone, WO-1767). Judge by
      `OWNED_TOWN_CHAIN_OK` on a **fresh** log plus the scene mtimes, never the exit code (CLAUDE.md §8).
- [ ] AFTER audit run: `RAID_POST_AUDIT_OK <n>` on a fresh log, and the PNGs **opened and
      described** — one upright tower per post, no interpenetrating second silhouette.
- [ ] `RAID_POST_ORIENTATION_OK` inside a fresh `REGRESSION_OK <n>/<n> suites` run (the lead gates
      the combined tree; this lane does not gate or commit).
- [ ] PO felt-verifies on device and closes (CLAUDE.md §13 — CLI never closes).

## 9. What NOT to touch

- No `.unity` hand-edits. Rebake only.
- No `RaidBaseGenerator` placement geometry (radius, panel count, gate index, turret budget) — this
  ticket is about what RENDERS on a post, not where posts go.
- No `EnsureUpright` call added to `PlaceCornerTower`: the measured host rotation is already pure
  yaw, so adding one would be an unproven change. Logged as an open gap in §10 instead.
- `Assets/_Modules/Village/Troops/RaidDeployController.cs` and the HUD files are other lanes'.

## 10. Open gaps, named rather than ticked

- **`PlaceCornerTower` still has no `EnsureUpright` call** while `PlaceTowerProp` does. Not a defect
  today (the host mesh is not flat, and after this fix the host does not render at all), but if a
  future corner-post art token is a flat FBX the clad would inherit nothing and nothing would catch
  it. Left for a ruling; the new regression's pin 3 would red on it.
- **Watchtower height fit is off and this ticket does not fix it.** `PlaceTowerProp` scales the
  HOST to `YHeightVariable * heightMul` (the bake log reads `target=4.80m … achieved=4.80m`), but
  the clad that actually renders measures **7.5 m** tall. The fit measures one model and the dresser
  instantiates another. Separate ticket; recorded here because the number is in the same log.
- **§4's explanation of why the smaller camp drew no complaint is reasoning, not a measurement.**
  The camera distances (9.1 m vs 78 m) are captured facts; "that is why she did not notice it in the
  first camp" is inference. Only the owner can confirm it.
- **The owner's own screenshot was never recovered**, so the before/after comparison rests on the
  headless frames this ticket produced, not on her frame.

## 11. Measured evidence (filled by the audit runs)

**BEFORE** — `Builds/raid-post-audit-before.log`, PNGs under `Builds/raid-post-audit/`.
**AFTER** — `Builds/raid-post-audit-after.log`, same folder, after the rebake.

The lines to read, per post:

```
<name> hostRot=(x,y,z) pos=(x,y,z) rootRenderer=YES|no
  ROOT   mesh='…' mat='…' size=(x,y,z) minY=… ratio=…
  VISUAL localRot=(x,y,z) up=(x,y,z) upDot=… size=(x,y,z) minY=… ratio=…
```

### BEFORE — `Builds/raid-post-audit-before.log`, run 2026-09-16 20:41, marker **`RAID_POST_AUDIT_FAIL: 49 defect(s)`**, `audited 59 post(s); wrote 8 non-blank frame(s)`

The reported scene's own block, verbatim:

```
CornerPost_Outer_E     hostRot=(0,225,0) pos=(-49,2.5,-49) rootRenderer=YES
  ROOT   mesh='tower-medieval_wood' mat='M_10_Brown_Dark_LPUP' size=(2.99,6.5,2.99) minY=2.48 ratio=2.17
  VISUAL localRot=(0,0,0) up=(0,1,0) upDot=1  size=(4.3,7.52,4.3) minY=0    ratio=1.75
```

**This one block settles all three candidates, and it is better evidence than the ticket expected:**

* **(a) DEAD.** `VISUAL localRot=(0,0,0)`, `up=(0,1,0)`, `upDot=1`, `ratio=1.75`, `minY=0`. The Synty
  clad is perfectly upright and perfectly seated. Nothing pitched it.
* **(b) DEAD.** `ROOT … ratio=2.17` — well above `EnsureUpright`'s 0.8 threshold. The polyperfect host
  mesh is **standing up**, not flat. The missing `EnsureUpright` call in `PlaceCornerTower` never
  mattered here. Candidate (b) is closed by measurement, not by argument.
* **(c) PROVEN, and the number nobody predicted is `ROOT minY = 2.48`.** The host still renders
  (`rootRenderer=YES`) a 2.99 x 6.5 x 2.99 m wooden polyperfect tower — and its lowest point is
  **2.48 m ABOVE THE GROUND**. It is not merely stacked; it is **hanging in mid-air**, its legs
  dangling at head height, its 6.5 m body reaching to **8.98 m** while the stone clad it hangs inside
  tops out at 7.52 m. So from the ground the player sees a wooden tower with **no base, floating,
  with its roof sticking out of the top of a stone tower** — which is precisely what "corners seem
  upside down" and "towers are inverted" describe. There was never a flipped transform to find.

Why it floats: `SeatOnGround` seats the **union** of renderers at y=0, and the clad is the lower of
the two, so the clad gets y=0 and the host's own mesh keeps whatever offset its pivot gives it
(`pos=(-49,2.5,-49)` — the 2.5 lift predicted in §3 is confirmed to two decimals).

**Frames opened** (`Builds/raid-post-audit/`):

* `RaidBase_fortified_garrison_CornerPost_Keep1_E.png` — the post fills the frame as one tall
  silhouette. It renders **bright yellow**: that is the `Synty/Generic_Basic` shader falling back, the
  same defect the device capture reported nine times as `non-URP shader (renders as the URP
  fallback)`. **A separate ticket's subject, not this one's** — but it means the clad's own surface
  is unreadable in this frame, so the shape evidence here is the silhouette, and the numbers above
  are what carry the diagnosis.
* `RaidBase_raider_camp_small_CornerPost_Outer_E.png` — **the control, and it supports §4.** The
  corner reads as an ordinary wooden palisade column standing in a stone wall line. Nothing looks
  broken. This is what the owner saw at 19:45 and did not report.
* `RaidBase_fortified_garrison_Watchtower_Archer_0.png` — **not usable.** `GroundShot` stands the
  camera 14 m outward along the post's radial, and for a wall-line watchtower that puts the arena
  boundary wall between the camera and the subject; the frame is a wall. Recorded as an instrument
  limitation, not a finding. The corner-post frames are inside the ring and are fine.

⚠ `ROOT … ratio` is the number that closes (b). **It was read, at `2.17`, not assumed.**

### REBAKE — `Builds/raid-rebake-1807.log` + `Builds/owned-town-chain-1807.log`, 20:43–20:45

```
[RaidBaseGenerator] baked 4 raid scene(s) from scene-configs.json
  (raider_camp_small, fortified_garrison, mage_enclave, iron_bastion).
OWNED_TOWN_CHAIN_OK config=iron_bastion; raid template regenerated with ids emitted at creation;
  owned town re-derived FROM it (owner ruling A, 2026-09-16); manifest re-baked; spire seat verified;
  navmesh baked; artifact lint proven RED before / GREEN after / RED on a blanked id.
  [owned-town-template-identity] 169 census structure(s) in all three scenes (raid, owned town,
  practice arena), all stamped, ids unique, the four id SETS equal, manifest entries=169
```

All four scene mtimes moved to **20:44:32–20:44:40**, so the rebake demonstrably touched them.

### AFTER — two runs, and the second one exists because the FIRST red was MY OWN false positive

**Run 1** (`Builds/raid-post-audit-after.log`, 20:46): `RAID_POST_AUDIT_FAIL: 14 defect(s)`,
`audited 59 post(s)`. **49 → 14: every stacking defect gone.**
`CornerPost_Outer_E  hostRot=(0,225,0) pos=(-49,2.5,-49) rootRenderer=no` — the second tower is gone.

All 14 survivors were the *same* complaint on `hexagon-green` kit posts:
`/Visual bounds ratio 0.75 < 0.8 - it renders FLAT` (8 x IronBastion CornerPost, 2 x IronBastion
Watchtower, 4 x raider_camp_small CornerPost). Every one had `upDot=1` and `minY=0`.

**That red was wrong, and it was my pin, not the art.** The frame was opened —
`Builds/raid-post-audit/RaidBase_raider_camp_small_CornerPost_Outer_E.png` — and shows a clean,
upright, crenellated stone watchtower standing correctly on the corner with the wooden polyperfect
column gone. `building_watchtower_green` is simply **squat**, exactly the false positive
`EnsureUpright`'s own warning text predicts. The ratio pin was therefore re-derived from the three
measured populations (pitched 0.57 / squat 0.75-0.76 / correct 1.75-2.16) and set to **0.65**, which
separates the failure from the art with margin on both sides. **The number was moved because the
frame proved the art correct — not to make a red go green;** the full derivation is in the code at
`RaidPostOrientationRegression.UprightRatio` so the next seat can audit the decision.

**Run 2** (`Builds/raid-post-audit-after2.log`, 20:59):

```
  audited 59 post(s); wrote 8 non-blank frame(s) to Builds/raid-post-audit/
RAID_POST_AUDIT_OK 59
```

with `rootRenderer=no` on every post, and the reseat visible in the other two scenes' corner posts
dropping from a lifted host to `pos=(-54,0,-54)` and `pos=(-31,0,-31)` — **y = 0**, the 2.5 m lift
gone exactly as §3 predicted.

⚠ **The fortified_garrison AFTER frame is NOT usable and that is a separate defect.**
`RaidBase_fortified_garrison_CornerPost_Keep1_E.png` renders **solid yellow**: the Synty clad's
`Synty/Generic_Basic` shader, which the device capture already flags nine times as
`non-URP shader (renders as the URP fallback)`. The post's silhouette fills the frame correctly but
its surface is unreadable. **That shader gap is a different ticket** — this one is judged on the
measured `rootRenderer=no` + `upDot=1` + `minY=0` across all 59 posts, plus the `raider_camp_small`
frame where the art renders properly.

### The 25-minute block, kept as the process record (RESOLVED 20:40)

> ⚠ **STATUS BETWEEN 20:16 AND 20:40:** the audit could not produce a marker,
> because the shared working tree **did not compile** — `Assets/Editor/Regression/EnemyTowerWallLosRegression.cs`
> (untracked, created 20:13 by another lane) does not implement `IDamageableStructure.Faction` on its
> two nested dummy classes, and Unity refuses `-executeMethod` while scripts have compile errors:
> ```
> Assets\Editor\Regression\EnemyTowerWallLosRegression.cs(62,64): error CS0535:
>   'EnemyTowerWallLosRegression.DummyPartyMember' does not implement interface member
>   'IDamageableStructure.Faction'
> Assets\Editor\Regression\EnemyTowerWallLosRegression.cs(69,70): error CS0535: … DummyFlyingPartyMember …
> ```
> `IDamageableStructure.Faction` has existed since 2026-09-06 (`IDamageableStructure.cs:81`), so this
> is an incomplete new file, not a race this lane can safely finish — and the value each dummy should
> return is a design choice inside **their** oracle (it changes what `CombatFactionRules.MayAttack`
> returns there). Per CLAUDE.md §11 this lane does **not** edit another lane's file. **Until that file
> compiles, no rebake and no PNG exist for this ticket, and §8's criteria are unmet.**
>
> **It is not one lane, it is at least two.** This lane ran the audit **10 times** between 20:16 and
> 20:33 (`Builds/rpa-retry.out.log`); nine attempts died on the `EnemyTowerWallLosRegression` pair, and
> attempt 7 at **20:28:40** died on a *different* lane instead:
> ```
> Assets\_Modules\Village\Troops\ArmyMusterPanel.cs(363,25): error CS0103: The name 'CurrentBands' does not exist in the current context
> Assets\_Modules\Village\Troops\ArmyMusterPanel.cs(625,17): error CS0103: The name '_wallet' does not exist in the current context
> ```
> So the shared working tree had **no clean compile window at all** in that 17-minute span. This is the
> single Unity gate of CLAUDE.md §11 doing what it does: parallel edit-only lanes cannot each fire
> Unity, and a lane whose acceptance needs a bake is hostage to whoever last saved a broken `.cs`.
> Recorded as a process finding, not a complaint — the lead batch-gates the combined tree, and the
> rebake in §7 belongs to that batch.
>
> **Resolved by that lane at 20:40** (the file's mtime moved 20:13 → 20:40, size 12274 → 13044). This
> lane never touched it. The audit succeeded on the next retry, 20:41. Kept in the ticket because the
> lesson is reusable: an edit-only lane whose acceptance needs a bake is hostage to every other lane's
> unsaved `.cs`, and the only safe response is to wait and say so — not to "just fix" two lines whose
> correct values live in someone else's design.

## 12. Judging the runs

- BEFORE run: expect **`RAID_POST_AUDIT_FAIL`** — the instrument must red on the defect it was built
  for. ⚠ `run-unity-method.ps1 -ExpectMarker RAID_POST_AUDIT` **substring-matches `_FAIL` too**, and
  `Debug.LogError` does not abort batchmode, so the wrapper can print `VERDICT=PASS` on the red run.
  **Judge the word `_OK` vs `_FAIL`, never the wrapper verdict and never the exit code** (CLAUDE.md §8).
- Unity logs are **UTF-16** in places; a bash `grep -a` silently misses the marker. Read them with
  PowerShell `Select-String`, or `iconv -f UTF-16LE`. (This bit us once in this very session: a watch
  reported `blockers=none` on a log that plainly carried two `error CS0535` lines.)
- Confirm the four `RaidBase_*.unity` **mtimes moved** before believing the rebake ran.

---

*Ticket authored by the WO-1807 lane. Number pre-assigned by the lead; the
`CLI_LANES_WO_NUMBERS.md` banner was already bumped and this lane did not touch it.*
