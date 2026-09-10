# WO-1568 - Composed-dungeon doors read as moving walls, not doors

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Minted:** 2026-09-06 (banner main-line row, hundred-and-fourth pass read `next free = 1568`; bumped to 1569 in the same edit)
**Silo:** Dungeons / presentation (RoomForge composed path)
**Lane:** World/Environment - art + presentation only. No gameplay logic, no scene files.
**Owner ask, verbatim:** "look at the door mechanics in the dungeon and see if we can somehow make the working doors look like doors instead of moving walls."

---

## 1. Root cause - with file:line evidence

The composed dungeon's working door is a **bare primitive cube** built at runtime, sitting
in a **raw full-height hole** between two half-walls. Nothing about either half says "door".

### 1.1 The leaf is a scaled cube with a flat colour

`Assets/_Modules/Dungeons/RoomForge/CommonDungeonDoor.cs:51-75` (`BuildDoor`):

```
var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
slab.name = "CommonDoor_Slab";
slab.transform.localPosition = new Vector3(halfWidth, 1.2f, 0f);
slab.transform.localScale = new Vector3(halfWidth * 2f, 2.4f, 0.16f);
...
mat.color = new Color(0.20f, 0.105f, 0.045f, 1f);
```

- The mesh is `PrimitiveType.Cube` - the **same primitive family the room walls are made
  of** (`DefaultDungeonRoomsBuilder.BuildSolidWall`, called from `BuildPerimeterWalls`
  at `Assets/Editor/RoomForge/DefaultDungeonRoomsBuilder.cs:365-405`). A cube swinging
  next to cubes reads as a wall that moved, because geometrically that is exactly
  what it is.
- There is **no frame, no jamb, no lintel, no panelling, no hinge hardware, no handle** -
  zero silhouette cues.
- The only differentiator is **colour** (dark brown vs. the grey wall material). The owner
  is colourblind, so the one cue the current door has is the one cue she cannot use.

### 1.2 The motion is already a hinge - so motion is NOT the defect

`CommonDungeonDoor.cs:15-17` and `:90-93`: the hinge child sits at `-halfWidth`
(`:53-56`) and the leaf rotates `OpenAngle = 100f` at `DegreesPerSecond = 240f` via
`_hinge.localRotation = Quaternion.Euler(0f, _angle, 0f)`. **This is a correct swinging
hinge.** Do not "fix" the animation; it is the geometry that fails.

### 1.3 The opening is a hole, not a doorway - and the leaf does not fill it

`Assets/Editor/RoomForge/DefaultDungeonRoomsBuilder.cs:464-502` (`BuildWallWithGap`)
builds **two solid half-walls flanking a gap** and nothing else - no header slab, no
jambs, no trim:

```
// Left (-X) piece
BuildSolidWall(parent, name + "_L", ..., new Vector3(side, h, thick));
// Right (+X) piece
BuildSolidWall(parent, name + "_R", ..., new Vector3(side, h, thick));
```

Measured against `Assets/_Modules/Dungeons/RoomForge/RoomForgeCanon.cs`:

| Quantity | Value | Source |
|---|---|---|
| Gap clear width | **2.2 m** | `RoomForgeCanon.DoorGap` (`RoomForgeCanon.cs:51`) |
| Wall / gap height | **4.0 m** | `RoomForgeCanon.WallHeight` (`RoomForgeCanon.cs:63`) |
| Door socket `halfWidth` | **1.1** (Door) / 1.5 (Arch) | `DefaultDungeonRoomsBuilder.cs:549` |
| Leaf width | 2 x 1.1 = **2.2 m** | `CommonDungeonDoor.cs:60` |
| Leaf height | **2.4 m** | `CommonDungeonDoor.cs:60` |

So the leaf is 2.4 m tall inside a **4.0 m tall opening**: a permanent **1.6 m open
letterbox above the closed door**. A closed door you can see straight over is not read as
a door. And because the leaf is exactly as wide as the gap, there is no reveal or inset -
the leaf is flush wall-to-wall, which is the classic "sliding wall panel" silhouette.

### 1.4 What is NOT the cause (so the implementer does not go hunting)

- **Door LOGIC is fine and is out of scope.** `CommonDoorPolicy` / `Configure` /
  `Update` / `SetOpen` / `ClaimedConnections` (`CommonDungeonDoor.cs:20-107`) and
  `RoomSocket.Start` (`RoomSocket.cs:56-63`) decide open/closed, proximity, interaction
  and locked. None of it is defective and none of it is touched by this WO.
- **The legacy `DungeonSceneBuilder` path is a different, non-animated system.**
  `Assets/Editor/DungeonSceneBuilder.cs:588-612` already instantiates the real KayKit
  `wall_doorway.fbx` and calls it "A real doorway - collidable frame around a
  walk-through gap". It has **no moving leaf at all**, so it is neither the bug nor the
  fix target. It is, however, **proof the frame art is already wired and shipping**
  elsewhere in this repo.
- **`DungeonController` doors are nav-link PORT pairs**, not visuals
  (`Assets/_Modules/Dungeons/DungeonController.cs:1698-1798`, keyed on layout
  `kind=="doorway"` segments). Out of scope.
- `Assets/_Modules/Dungeons/DungeonPortLink.cs` and `DungeonLayout.cs` carry no door
  visual or animation (grepped 2026-09-06: no `door` visual hits).

**One-line root cause:** the working door is a `PrimitiveType.Cube` of the same family
and dimensions as the walls, distinguished only by colour, hung in an untrimmed
full-height gap it is 1.6 m too short to fill - so every cue that would say "door"
(frame, jamb, lintel, inset leaf, panel relief, hardware) is absent.

---

## 2. Door art found on disk

### 2.1 KayKit Dungeon Remastered 1.1 - the intended kit, measured

`Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/` - **present on disk**, but
**gitignored** (`.gitignore:122` `/Assets/Models/*`; confirmed with `git check-ignore -v`).

| Asset | Formats present | Measured bounds (from the OBJ vertex list) |
|---|---|---|
| `wall_doorway` (frame wall piece, hole in the middle) | `fbx`, `fbx(unity)`, `obj`, `gltf` (also mirrored at `Assets/Models/KayKit/dungeon/wall_doorway.fbx`) | x [-2.000, 2.000], y [0.000, 4.000], z [-0.500, 0.500] -> **4.0 x 4.0 x 1.0 m** |
| `wall_doorway_door` (**the door LEAF**) | **`obj` ONLY** - no FBX, in either kit folder | x [-1.000, 1.000], y [-0.000, 2.750], z [-0.387, 0.387] -> **2.0 w x 2.75 h x 0.77 d**, pivot centred in x, **base at y = 0** |
| `wall_doorway_scaffold` / `_scaffold_door` | `fbx` + `obj` (leaf again obj-only) | timber-braced variant of the same pair |
| `wall_doorway_sides`, `wall_doorway_Tsplit` | `fbx`, `obj`, `gltf` | jamb / T-junction variants |
| `wall_arched`, `wall_gated`, `wall_corner_gated`, `wall_archedwindow_gated` | `fbx` | portcullis / arch family - the "gated" pieces are the portcullis look |

**The fit is measured, not assumed:** the leaf is **2.0 m wide** against a **2.2 m gap**
(0.1 m reveal each side - a real inset, exactly the cue that is missing today) and
**2.75 m tall** against a **4.0 m opening**, leaving a 1.25 m header for a lintel. The
`wall_doorway` frame piece is 4.0 m tall, which matches `RoomForgeCanon.WallHeight = 4.0`
exactly, but it is **4.0 m wide and 1.0 m thick** against a 2.2 m gap in a 0.4 m wall - so
it will interpenetrate the flanking half-walls if dropped in unscaled. See section 3 for
the ruling on that.

### 2.2 Polyperfect - present but gitignored, and wrong theme

`Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Fantasy_M/` carries
`Dungeon_Door_Stone.prefab`, `Dungeon_Door_Prison.prefab`, `Door_Wood.prefab`,
`Door_Prison.prefab` (gitignored: `.gitignore:325`). These are **standalone door props**,
not kit-grid pieces, and they do not share the KayKit dungeon atlas the composed rooms
are themed with. **Listed for completeness; not recommended.**

### 2.3 3DForge / KayKit Prototype Bits - not recommended

`Assets/3DForge/FantasyExteriors/.../fi_vil_wall_stone01_door_square.prefab` is a village
exterior piece. `Assets/Models/KayKit/KayKit Prototype Bits 1.1/Assets/fbx/Door_A.fbx`,
`Door_A_Decorated.fbx`, `Door_B.fbx`, `Primitive_Doorway.fbx`, `Wall_Doorway.fbx` are
greybox prototype bits. Both are theme mismatches against the dungeon atlas.

### 2.4 The gitignore problem is already solved in this module - reuse the solution

Because the packs are gitignored, a **runtime** component cannot rely on
`AssetDatabase`. WO-1007 already solved this for the dungeon exit: the needed kit files
were copied into a **tracked** `Resources` folder.

- On disk today: `Assets/Resources/Dungeon/Exit/wall_arched.fbx`,
  `pillar_decorated.fbx`, `dungeon_texture.png`.
- The resolution ladder is `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:446-485`
  (`ResolveExitProp`): `Resources.Load` -> editor-only `AssetDatabase` from the
  gitignored kit -> caller falls back to a primitive, with `FlowTrace.Warn`, **never
  invisible**. Material resolution: `ResolveKayKitMaterial` (`:488-510`).
- **Copy that ladder verbatim for the door.** Do not invent a second pattern.

---

## 3. Proposed fix shape - presentation only

**One sentence:** give `CommonDungeonDoor` a **framed doorway** (jambs + lintel, filling
the 1.6 m letterbox) and an **inset, panelled leaf** on the hinge it already has -
resolved from KayKit art with a still-door-shaped primitive fallback - and change nothing
about how open/closed is decided.

### 3.1 Why the fix belongs in the runtime component, not the prefab builder

`RoomSocket.Start` (`RoomSocket.cs:56-63`) adds `CommonDungeonDoor`, which builds the
door in `Start` (`CommonDungeonDoor.cs:37-47`). **The door does not exist in any prefab
or any `.unity` file** - it is created at runtime, every session.

Consequence, and it is the reason to reject the alternative of adding the frame in
`DefaultDungeonRoomsBuilder`: **this WO needs no room-prefab rebuild, no recompose and no
re-bake.** Building the frame in the room shells instead would require
`Defenders/Dungeon/Build Default Room Prefabs` + a recompose of every graph + a re-bake
of every `Assets/Scenes/DungeonCompose/*.unity`, which drags in the isolated-worktree
constraint in section 6 for no benefit. Build the frame at runtime alongside the leaf,
under the same one owner.

### 3.2 The frame (new, render-only)

Built by `CommonDungeonDoor` as children of the socket transform, dimensions read from
`RoomForgeCanon` and **never re-typed**:

- **Two jambs** flanking the `DoorGap` (2.2 m) opening, full `WallHeight`, ~0.15-0.2 m
  proud of the wall face so they cast a visible shadow line.
- **A lintel / header** spanning the gap from the top of the leaf up to `WallHeight` -
  this closes the 1.6 m letterbox from section 1.3 and is what turns a hole into a
  doorway. It must remain a doorway read **even while the door is open**.
- **No colliders on any frame piece.** The NavMesh is already baked
  (`DungeonBaker`, `NavMeshCollectGeometry.PhysicsColliders`) and must not need a
  re-bake; a new collider in a baked opening can also trap the hero. This is the same
  reason the exit props strip colliders (`DungeonExitInteractable.AddProp`, "never trap
  the hero"). The **one** blocker collider stays exactly where it is: the leaf's own
  `_blocker` (`CommonDungeonDoor.cs:61`, toggled in `SetOpen` at `:100-106`).

### 3.3 The leaf

- Resolve through the `ResolveExitProp` ladder: `Resources.Load<GameObject>("Dungeon/Door/<stem>")`
  -> editor-only `AssetDatabase` against
  `Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/obj/wall_doorway_door.obj`
  -> primitive fallback.
- **Copy the leaf into `Assets/Resources/Dungeon/Door/`** so it resolves in a player
  build, exactly as `Dungeon/Exit/` does. **Do not copy `dungeon_texture.png` a second
  time** - reuse the existing `Dungeon/Exit/dungeon_texture` via `ResolveKayKitMaterial`'s
  shape.
- **Place the hinge from `renderer.bounds` after instantiate, never from an assumed
  pivot.** The leaf ships only as OBJ; its measured pivot is centred in x with the base at
  y = 0, but derive it rather than hardcode it.
- Leaf sits **inset** in the frame (the measured 2.0 m leaf in the 2.2 m gap gives that
  for free) and is **thinner in z than the wall**, so the reveal is visible.
- **Primitive fallback must still be door-shaped**, not the current flat cube: a leaf
  narrower than the gap, plus at least two raised panel/plank reliefs and a visible
  hinge-side stile. A fallback that reproduces today's slab is a failed fallback.

### 3.4 Locked / interaction doors

`CommonDoorPolicy.Locked` and `.Interaction` get the **same** frame and leaf. Do not add
a second prop, a second spawner or a lock mesh in this WO - the prompt already reads
"Locked" (`CommonDungeonDoor.cs:87`).

### 3.5 Explicitly unchanged

`OpenDistance`, `CloseDistance`, `OpenAngle` (100), `DegreesPerSecond` (240),
`PromptPriority`, `Configure`, `Update`, `SetOpen`, `OnDisable`, `ClaimedConnections`,
`ResetClaims`, `CommonDoorPolicy`, every field on `RoomSocket`. The hinge rotation is
already correct (section 1.2).

---

## 4. Files to edit

| File | Change |
|---|---|
| `Assets/_Modules/Dungeons/RoomForge/CommonDungeonDoor.cs` | `BuildDoor` only: frame construction + art-resolved leaf + door-shaped primitive fallback. Extract the visual build into a static seam (see section 7) so the oracle and the capture can drive it. |
| `Assets/Resources/Dungeon/Door/` (new folder) | Tracked copies of the KayKit leaf (and the frame piece if used). Mirrors `Assets/Resources/Dungeon/Exit/`. |
| `Assets/Editor/Regression/DungeonDressingRegression.cs` *(or a new sibling oracle)* | Pin the door shape: frame present, lintel closes to `RoomForgeCanon.WallHeight`, leaf narrower than `DoorGap`, zero colliders on frame pieces, exactly one collider on the leaf. |
| `Assets/Editor/DungeonSceneCapture.cs` | Only if needed to drive the new static seam so a door appears in the capture (section 7). |
| `Assets/_Modules/Dungeons/RoomForge/README.md` | One line: the common door now builds a framed doorway; canon dims still come from `RoomForgeCanon`. |

Add `FlowTrace.Step("DungeonDoor", ...)` naming which leaf source resolved
(resources / editor-kit / primitive-fallback) and `FlowTrace.Warn` on a fallback, per
CLAUDE.md section 12. Never a silent fallback.

---

## 5. What NOT to touch

- **Any `.unity` file.** No hand-edits, no re-saves. CLAUDE.md section 3.
- **`Assets/Scenes/DungeonCompose/*.unity`** - no re-bake is required by this WO
  (section 3.1). If one is somehow proposed, section 6 applies first.
- **`Assets/Dungeon/Rooms/*.prefab`** - generated; no rebuild required.
- **`Assets/Editor/RoomForge/DefaultDungeonRoomsBuilder.cs`**,
  **`DungeonBaker.cs`**, **`RoomForgeCanon.cs`** - read `RoomForgeCanon`, never edit it,
  never re-type its values.
- **Door logic:** `CommonDoorPolicy`, `Configure`, `Update`, `SetOpen`,
  `ClaimedConnections`, `RoomSocket` fields, keys, locks, prompts.
- **`Assets/Editor/DungeonSceneBuilder.cs`** (legacy non-animated path) and
  **`DungeonController`** nav-link door ports - different systems (section 1.4).
- **`Assets/Resources/Dungeon/Exit/`** - reuse its texture, add nothing to it, remove
  nothing from it.
- Do not strip any existing `FlowTrace` call (CLAUDE.md section 12).

---

## 6. Constraint recorded: composed dungeon scenes and the shared tree

**Baking path (confirmed at source 2026-09-06):** composed dungeon scenes are **baked, not
hand-edited**. `Assets/Editor/RoomForge/DungeonBaker.cs:29` writes to
`Assets/Scenes/DungeonCompose/`, saving at `:592-594`
(`EditorSceneManager.SaveScene(scene, $"{OutputScenesFolder}/{layout.dungeonId}.unity")`),
entered from the menu items at `:140` and `:149`. Note `:624-628`: pure `-batchmode` does
**not** honour ForceText, so a batch bake can land a BINARY scene and says so in a
`FlowTrace.Warn`.

**The constraint (auto-memory `dungeon-scene-shared-tree-corruption`, restated in
`WorkOrders/WORK_ORDER_1007_dungeon_exit_real_asset.md:205` and
`WORK_ORDER_1009_dungeon_interactable_art_and_affordance_pass.md:91-92`):**
DungeonCompose `.unity` files have come back **NUL-corrupt when baked in the shared
working tree**. **Any re-bake happens in an ISOLATED WORKTREE only**, and the resulting
scenes are NUL-checked before they are committed.

**This WO is scoped so that constraint is never triggered** - the door is a runtime
object (section 3.1). Recorded here so the next seat does not re-derive it, and so that
if the implementer proposes moving the frame into the room prefabs, they know the price.

---

## 7. The capture seam - read this before writing acceptance

`Assets/Editor/DungeonSceneCapture.cs:133` opens each scene with
`EditorSceneManager.OpenScene(norm, OpenSceneMode.Single)` and renders through a
`RenderTexture` (`:315-357`). It **never enters play mode**. Therefore
`RoomSocket.Start` / `CommonDungeonDoor.Start` never run, and **the existing
`Builds/dungeon-capture/*.png` set cannot show a door at all**.

Verified: `Builds/dungeon-capture/dg_starter_loop_eye.png` (opened 2026-09-06) shows the
room shell, the torch and stacked KayKit crates - **no door leaf anywhere in frame**,
because there is none in the saved scene.

So acceptance below is only executable if the implementer does one of:

- **(a) preferred** - extract the visual build into a public static seam, e.g.
  `CommonDungeonDoor.BuildDoorVisual(Transform socket, float halfWidth, bool open)`, and
  have both `DungeonSceneCapture` and the regression oracle call it. This is the
  established RoomForge idiom: the oracle drives the same code as the builder
  (`RoomForge/README.md`, "Single source of truth", and `DungeonBakerChecks` living in
  the runtime assembly for exactly this reason).
- **(b)** drive the capture through play mode.

**(a) is the recommendation.** A copied oracle constant is not an oracle - same reasoning
as `RoomForgeCanon`'s own header.

---

## 8. Acceptance criteria

- [ ] Brace balance passes on every `.cs` touched (CLAUDE.md section 1).
- [ ] `COMPILE_GATE_OK` on a fresh log; `REGRESSION_OK <n>/<n> suites` on a fresh log
      (judge the marker, never the exit code).
- [ ] **Headless capture, door CLOSED**: one PNG showing a closed door with a visible
      frame - two jambs and a lintel - and the leaf inset within it. **No open sky, no
      see-through letterbox above the closed leaf.**
- [ ] **Headless capture, door OPEN**: one PNG of the same door swung open, in which the
      opening **still reads as a doorway** (frame and lintel remain) rather than a hole in
      a wall.
- [ ] **Both PNGs are opened and looked at** before the WO is reported done
      (auto-memory `headless-screenshot-verify-ui-before-build`;
      `screenshots-are-primary-evidence-for-visual-defects`).
- [ ] **Greyscale gate (BINDING): desaturate both PNGs to greyscale and confirm the door
      still reads as a door.** The owner is colourblind
      (auto-memory `owner-colorblind-delegate-visual-creative`). The door must be
      identifiable by **shape, frame, inset, panel relief and depth** - **never** by hue
      or by "the brown one". A door that only reads in colour FAILS this WO.
- [ ] Regression oracle pins the door shape (section 4) and fails if the leaf returns to
      a bare full-gap-width slab.
- [ ] `FlowTrace` names the resolved leaf source; a fallback is a `Warn`, never silent.
- [ ] Zero colliders added to frame pieces; exactly one collider on the leaf; `SetOpen`
      still toggles it.
- [ ] `git status` shows **no `.unity` file modified** and no
      `Assets/Dungeon/Rooms/*.prefab` modified.
- [ ] No `RoomForgeCanon` value re-typed anywhere in the diff.
- [ ] `Assets/Resources/Dungeon/Door/` files are **tracked** (the source packs are
      gitignored; an untracked copy resolves on this machine and nowhere else).
- [ ] Owner felt-verifies and closes (CLAUDE.md section 13 - PO closes, not CLI).

---

## 9. Provenance

Read-only diagnosis lane, 2026-09-06. Every file:line above was opened at source this
session; the OBJ bounds in section 2.1 were computed from the vertex lists, not quoted
from a catalog. Nothing in this WO was executed - no Unity run, no git, no asset edited.
