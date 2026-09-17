# WO-1837 — Hero can still lock/attack an enemy through a wall (different piece than WO-1829)

**Status: READY FOR LEAD REVIEW**

## ⛔ LEAD: THE GENERATOR FIX ALONE IS INERT — READ THIS FIRST

The wall generators bake prefabs/scenes to **DISK**. Fixing them changes **nothing** in any
already-baked dungeon until every `Assets/Dungeon/Rooms/*.prefab` and
`Assets/Scenes/DungeonCompose/dg_*.unity` is re-baked. That is why this lane also added a
**load-time sweep** (`DungeonStructureLayer.ApplyToWalls`, called from
`ComposedDungeonHost.Install` and `DungeonController.Start`) which repairs a stale bake in
memory on the frame the scene loads. **The owner's device is fixed by the sweep, not by the
generators.** A re-bake is still wanted so the on-disk artifact matches; the new suite's case 6
prints the stale count each gate run so it cannot be quietly forgotten. Re-bake per memory
`dungeon-scene-shared-tree-corruption` (isolated worktree only).

## Root cause — PROVEN from disk, not inferred (CLAUDE.md §11B)

`DungeonBakerChecks.SealSocket` (`Assets/_Modules/Dungeons/RoomForge/DungeonBakerChecks.cs:257`)
builds the socket **seal wall** with `GameObject.CreatePrimitive(Cube)` — a solid
`RoomForgeCanon.WallHeight` (4 m) slab with a BoxCollider — and never assigned it a layer.

Proving lines:
- `ProjectSettings/TagManager.asset` `layers[]` → index **8 == "Structure"**.
- `Assets/Scenes/DungeonCompose/dg_ember_deep.unity` (binary) contains **`SEALED_WALL` ×11**
  plus seal children `Seal_s_lower_w`, `Seal_s_upper_e`, `Seal_s_door_01` — the owner's geometry.
- `Assets/Dungeon/Rooms/*.prefab` → **every** `Wall_N`/`Wall_E`/`Wall_W`/`Wall_S_L`/`Wall_S_R`/
  `Wall_N_L`/`Wall_N_R` reads **`m_Layer: 0`** (22 walls in the first 4 prefabs alone).
- `HeroTargetIndicator.HasLoS` (`.../Hero/HeroTargetIndicator.cs:1543-1563`) masks its blocker
  `Physics.Linecast` to `"Structure"` only → every one of those colliders is invisible to it.
- `Assets/Scenes/Dungeon_Demo.unity` (legacy pipeline) → 6 `Wall_*` GOs, **all 6 on layer 0**.

## Full audit — every generator checked

| File / line | Builds | Layer before | Action |
|---|---|---|---|
| `_Modules/Dungeons/RoomForge/DungeonBakerChecks.cs:257` | socket seal wall | **none → Default** | **FIXED** — the screenshot culprit |
| `Editor/RoomForge/DefaultDungeonRoomsBuilder.cs:455` `BuildSolidWall` | `Wall_*` | **none** | **FIXED** |
| `Editor/RoomForge/DefaultStairConnectorRoomsBuilder.cs:741` `BuildSolidWall` | `Wall_*` | **none** | **FIXED** |
| `Editor/RoomForge/DefaultStairwellRoomBuilder.cs:433` `AddBox` | `Wall_N/S`, `Wall*_L/_R/_Mid/_Head`, floors, ceiling, steps, ramp | **none** | **FIXED**, name-discriminated (walls only) |
| `Editor/RoomForge/DungeonKitBuilder.cs:231` | MeshCollider on **every** kit mesh | **none** | **FIXED** via discriminated sweep |
| `_Modules/Dungeons/RoomForge/CommonDungeonDoor.cs:266,334` | door leaf | Structure | already correct (WO-1829) — not touched |
| `DefaultDungeonRoomsBuilder.cs:429-450` | ceiling | n/a | **out of scope** — collider is destroyed (`collider=none`) |
| `DefaultStairConnectorRoomsBuilder.cs:951` `Step_*` | stair treads | n/a | **out of scope** — colliders destroyed |
| `DefaultStairConnectorRoomsBuilder.cs:969` `RampCollider`, `:609`/`DungeonBaker.cs:1180`/`RoomForgeWindow.cs:226` floors | walkable surfaces | Default | **DELIBERATELY LEFT** — see ruling needed |
| `Editor/DungeonChainBuilder.cs:496`, `Editor/DungeonStubBuilder.cs:233` | trigger volumes | Default | **N/A** — `HasLoS` passes `QueryTriggerInteraction.Ignore` |
| `_Modules/Dungeons/` props (`ComposedPropVisuals`, `ComposedTrapHazard`, `IngredientPickup`, `DungeonExitInteractable`, `DungeonTreasureCache`, `WandererBubble`) | props/triggers/quads | Default | **N/A** — not sight-blocking wall geometry |

Only `CommonDungeonDoor`'s two WO-1829 lines existed anywhere in the Dungeons module before
this change; `grep NameToLayer Assets/Editor/RoomForge/` returned **zero** across all 12 files.

## ⚠ RULING NEEDED (do not guess — CLAUDE.md §11B)

Should dungeon **floors/ramps** block line-of-sight across stair levels? They are solid and
currently do not. They were left off `Structure` **on purpose**: `SmartMobileCamera`'s occluder
mask includes that layer (`SmartMobileCamera.cs:1323`, and the town camera's `_enemyMask
m_Bits: 256` is layer 8 alone, `:456`/`:2017`), so a horizontal occluder would make the camera
fade/pull in on the ground the hero stands on. Needs the owner's call, not a lane's guess.

## Owner report

Screenshot, dungeon, mage Thrain Lv11: hero is locked onto "Orc Raider" (203/203 HP, LOCKED
indicator) with a solid wall segment directly between the hero and where the enemy must be. The
compass strip along the top shows enemy markers at E, E, SE (the locked target), and S — several
enemies apparently detected/targetable through geometry. Owner: "this is still targetting in walls"
— referring back to the Ember Deep door bug from earlier this same session (WO-1829).

## Context — WO-1829 already fixed ONE instance of this defect class

WO-1829 (this session, earlier) root-caused and fixed: `CommonDungeonDoor.cs`'s procedurally-built
door leaf (both `BuildLeafAsset` and `BuildFallbackLeaf` paths) was never assigned to the
`Structure` Unity layer, so `HeroTargetIndicator.HasLoS`'s Structure-layer-masked `Physics.Linecast`
never detected a closed door as an obstruction — enemies on the other side of a closed door were
freely targetable and attackable. Fixed by assigning the leaf to `LayerMask.NameToLayer("Structure")`,
mirroring `WallSegment.cs:646-647`'s existing correct pattern.

**This screenshot is NOT a door** — it's a plain wall/wall-adjacent piece, so this is a DIFFERENT
prefab or generation path than the one WO-1829 touched. The same class of defect (some structure
prefab/generation path not assigned to the Structure layer) is recurring on at least one more piece
of dungeon geometry.

## What NOT to do

- Do **not** just special-case fix this one wall piece the way WO-1829 special-cased the door — that
  is how the bug recurred once already. Find every dungeon structure-generation path
  (`RoomForge`, wall/ceiling/prop builders under `Assets/_Modules/Dungeons/`) and confirm each one
  assigns its built colliders to the `Structure` layer, the same way `WallSegment.cs:646-647` does.
- Do not touch `CommonDungeonDoor.cs` unless a fresh audit finds it has regressed — it was already
  fixed and gated this session (commit `4ea4f9a5c`).
- Do not weaken or bypass `HeroTargetIndicator.HasLoS`'s layer mask — the mask is correct; the gap is
  in which colliders get assigned to it.

## Investigation required (instrument first, per CLAUDE.md §12 — do not guess which piece is wrong)

1. Identify which dungeon (scene name) and which specific prefab/piece the hero is standing next to
   in the screenshot — the geometry reads as an angled wall or beam, possibly a room-transition or
   diagonal wall segment, not a straight `WallSegment`.
2. Audit every dungeon room/wall/prop generator under `Assets/_Modules/Dungeons/RoomForge/` (and any
   sibling generators) for colliders built at runtime/editor-time without an explicit layer
   assignment — the same pattern WO-1829 found in `CommonDungeonDoor.cs`.
3. Confirm the fix with a headless or device capture showing `HeroTargetIndicator.HasLoS` correctly
   returning false (or the equivalent FlowTrace line) when a wall of the audited type sits between
   hero and enemy.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched `.cs`.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] Every dungeon structure-generation path confirmed (not assumed) to assign the `Structure`
  layer to its built colliders — cite each file/line checked, not just the one that was broken.
- [ ] A regression case proving line-of-sight is blocked by the specific geometry type from the
  screenshot, not just a re-assertion of WO-1829's door case.
- [ ] Owner felt-verifies on device that enemies behind walls in this dungeon are no longer
  targetable/attackable.
