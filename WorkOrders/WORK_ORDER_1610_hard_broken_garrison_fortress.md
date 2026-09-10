# WO-1610 — Hard camp: The Broken Garrison as a layered fortress

**Status:** IMPLEMENTED — baked 2026-09-09 (synty-castle, keep layer, 531 dress pieces); PO felt-test closes  
**Minted:** 2026-09-09 (CLI) — program WO-1607  
**Priority:** P1 felt — second raid; this is where “better defenses than fence + tower” has to be true  
**Lane:** World / Raid scenes (serialize with 1608/1609; worktree if those are in flight)  
**Config id:** `fortified_garrison` → scene `RaidBase_fortified_garrison`  
**Do not retune:** garrison (4 troll + 2 ogre + 6 berserker + 3 shaman + necromancer + 1 elite), spire HP 2200, tower DPS budget 16, `entranceCount: 1`, OverlappingFire, 180 s clock.

> ⚠ **COUNT CORRECTED 2026-09-10 (WO-1635, lane PROPS-CANON).** The Status line above says **531 dress
> pieces**. That was the 2026-09-09 bake and is **stale**; the Status line is **deliberately left as
> authored** (CLAUDE.md §15 — a dated WO header is frozen, it gets a banner, not a rewrite). The current
> bake reads **placed=489**:
> `Builds/wave3-bake7:640` — `[Flow:RaidBase] dressed 'fortified_garrison' kit=synty-castle placed=489 missing=0 gateW=8.72 zones=Approach,Gatehouse,Courtyard,Choke,Keep`.
> ⛔ **Do not "refresh" this number again** — the authority is the newest `[Flow:RaidBase] dressed
> 'fortified_garrison' ... placed=` line on a FRESH bake log, never a number in a doc.
> ⚠ Related and still OPEN: this config is the one that authors `"barracks"` in **both** prop schemas —
> `"props": { "set": ["barracks"], "count": 1 }` **and** as the first `raidDress.props` entry
> (`Assets/Resources/Data/Canonical/scene-configs.json`, read at `3da5e5360`). WO-1635 acceptance #1 owns
> that; it is **not** closed by this banner.
> *(WO-1635 §2 cites `Builds/wave2-bake2`; only `Builds/wave2-bake2.runner.txt` exists in `Builds/`, so
> `Builds/wave3-bake7` is the citation actually read.)*

---

## 1. What it is today (proven)

From `scene-configs.json:119-182`:

| Authored | Value | Baker result |
|---|---|---|
| displayName | The Broken Garrison | Copy only |
| description | Its soldiers still guard their post, though no living commander remains | Copy only |
| faction | mixed | `#8a6b3a` |
| difficulty | Hard | Spire 2200, DPS budget 16 |
| wallTier | Iron | Outer ring only |
| baseRadius | 49 m | ~49% of the 140 m plane |
| interiorWallLayers | **0** | Schema says this is the kill-zone keep. **Hard authors 0.** There is no choke. |
| entranceCount | 1 | South hole only |
| towerPlacementStyle | OverlappingFire | ~60% wall line, rest guard the spire |
| archer + mage | 5 + 2 | 7 turrets |
| towers[] | catapult ×3 + arcane_spire ×1 | Palette |
| centralBuilding | `tower_arcane_spire` | Spire |
| props | empty | Nothing |
| unlockVictories | 3 | Door pacing — do not change |

The player is promised a **garrison that still holds**. They get a bigger iron square, seven turrets, and a mixed ring of trolls. No gatehouse, no yard buildings, no inner kill-zone — even though the schema field for that kill-zone exists and Extreme already sets it to 1.

Village2 already has the fortress this camp wants: courtyard → choke (width 2, spike/arrow) → raised keep (`garrison-recipes.json:78-83`). Port that **shape** onto this RaidBase.

---

## 2. What it should feel like

An **occupied fort** whose commander is gone: stone curtain, a real gatehouse, a yard with barracks and racks, a **narrow inner gate** you do not want to stand in, then the spire. Trolls hold the yard. Shamans hold the wall. The inner gate is the punch.

**Teach on this camp:**

1. One door. Flanking the wall is the long way (chew iron).  
2. Courtyard is a fight, not a runway.  
3. The choke is lethal if you linger — push through.  
4. Inner turrets cover the spire (OverlappingFire already).

---

## 3. Layout (radius stays 49 m)

```
                         iron curtain + corner towers
                    ┌─────────── WATCH ───────────┐
                    │                             │
                    │   barracks    racks         │
                    │                             │
                    │        COURTYARD            │
                    │     trolls hold the yard    │
                    │                             │
                    │         ┌─ CHOKE ─┐         │
                    │         │ inner   │         │
                    │         │ gate +  │         │
                    │         │ stairs  │         │
                    │         └───┬─────┘         │
                    │             │ KEEP          │
                    │          SPIRE              │
                    │        (raised 1.5 m)       │
                    │                             │
                    └──────── GATEHOUSE ──────────┘
                         murder-slot flanks
                         APPROACH (cobble)
                         STAGING
```

Set `interiorWallLayers: 1` on this row (it is currently 0). That is a **layout** change, not a DPS change. The inner ring already sits at 45% of the outer (`RaidBaseGenerator.cs:340-352`) with a **north** gate so the player crosses the yard under fire. Keep that funnel: outer gate SOUTH, inner gate NORTH — the yard is the kill-box.

### 3.1 Approach

- Dungeon `floor_tile_large.fbx` (fortress palette, `DungeonSceneBuilder.FloorPiece` Fortress).
- Hexagon `flag_red.fbx` × 2.
- Optional hexagon `cannonball_pallet.fbx` off the lane.

### 3.2 Gatehouse (south only)

This camp has **one** entrance. Make it a building:

- **Prefer Synty** `SM_Bld_Castle_Wall_Gate_01.prefab` (children include doors L/R + portcullis) — this is the closest gatehouse / murder-hole in the tree, already used on the hub by `SyntyCastlePerimeterBuilder`.
- Flanks: Synty `SM_Bld_Castle_Wall_Tower_{S,M,L}_01.prefab` (hub corners already use **M**).
- Arrowslit mix-in: `SM_Bld_Castle_Wall_Arrowslit_01.prefab` on the curtain beside the gate (read only; V1 does not add a new turret type).
- Fallback if Synty is missing: dungeon `wall_gated.fbx` + `wall_archedwindow_gated.fbx`, then catalog `StructureContent` gate. Still a gatehouse, not a missing panel.
- Walkable width **3.5–4.5 m** (wider than Easy’s wagon gap; this is a fort door). Log it.
- Do **not** add a north outer gate.

### 3.3 Curtain wall

- Keep Iron `WallSegment` colliders (`Resources/Walls/iron_wall.fbx` — this is the tracked combat wall).
- Visual clad: Synty `SM_Bld_Castle_Wall_01` (+ `_02`…`_05`, corners, destroyed). Cap with `SM_Bld_Castle_Battlements_01`. That is a **curtain**, not a fence.
- Corners: Synty wall towers S/M/L, consuming existing turret slots.
- Spike approach: Synty `SM_Prop_Spike_Fortification_01..03` / `SM_Prop_Spike_Wall_01..03` **outside** the south gate, not in the staging pocket.

### 3.4 Courtyard — the garrison’s home

| Cluster | Pieces | Count | Place |
|---|---|---|---|
| Barracks | hexagon `building_barracks_red.fbx` | 1 | West yard, 16–22 m from centre. Collider; leave ≥ 4 m aisle to the choke. |
| Second hall | hexagon `building_home_A_red.fbx` or `building_workshop_red.fbx` | 1 | East yard, mirrored. |
| Racks / ammo | `weaponrack.fbx`, `bucket_arrows.fbx`, `cannonball_pallet.fbx` | 4–6 | Against curtain, inner face. |
| Crates | dungeon `crate_large.fbx`, `barrel_large.fbx` | 8 | Cover along the **sides** of the south→north march, not on it. |
| Damage tell | dungeon `wall_broken.fbx`, `rubble_large.fbx`, `sword_shield_broken.fbx` | 4 | “Broken” garrison — east curtain inner face. Visual, collider stays. |
| Siege | hexagon `building_tower_cannon_red.fbx` or catalog Ballista/Catapult | 2 | On the curtain or in the yard corners. Count against the 7 turrets if they shoot. |

Clear **south gate → inner north choke** lane ≥ 5 m. This is the assault axis.

### 3.5 Choke (`Zone_Choke`) — NEW for Hard

Inner ring (`interiorWallLayers: 1`) + inner **gatehouse** (smaller: `wall_doorway.fbx` or `wall_gated.fbx`).

- Width: Village2 recipe uses choke `width: 2` (cells). Here: **walkable 3.0–3.8 m** — a funnel, not a door you cannot fit a troll through (trolls are in this garrison).
- Floor tell: 2–4 `floor_tile_big_spikes.fbx` and/or `floor_tile_grate.fbx` as **décor beside** the walkable strip, not covering it. Do not add spike damage in V1.
- Optional: `stairs_long.fbx` if the keep is raised (WO-1607 Q3; default YES). NavMeshLink at the stair, same as `EnemyStrongholdBuilder` (header: “every stair run gets a NavMeshLink”). Fail loud if the link type cannot resolve.

### 3.6 Keep / spire

- Keep `centralBuilding: tower_arcane_spire`.
- Default: **raised 1.5 m** platform (`village2_stronghold` keep.platformHeight is 1.5). Hexagon `building_castle_red.fbx` may **dress** the keep behind/around the spire if it does not swallow the objective or block the stair.
- BossSpawn on the keep, not inside the spire mesh.
- Inner turrets (OverlappingFire remainder) stand on the keep ring so the objective is contested.

If Q3 is ruled “flat inner ring,” skip the 1.5 m platform and stairs; still build the inner gatehouse.

---

## 4. Garrison seating (same 16-ish bodies; 1 elite)

| Who | Slot | Job |
|---|---|---|
| 2 berserkers | South gatehouse | Hold the door |
| 4 trolls | Courtyard, off the centre lane | Yard Hold — they are the reason this camp is Hard |
| 2 ogres | Inner choke flanks | Hold the funnel |
| 4 berserkers | Yard sides | Hunter candidates (WO-1595 follow-up; for now they stand here) |
| 3 shamans | Curtain / watchtower feet | Wall shooters |
| Elite | Keep, near spire | Last guard |
| Boss necromancer | BossSpawn on the keep | Unchanged |

Do not add units. Do not change `eliteCount: 1` or `difficultyMultiplier: 1.25`.

---

## 5. Data to write

On `fortified_garrison` (both Canonical copies):

- `interiorWallLayers`: **0 → 1** (this is the choke; it was authored and unused).
- `raidDress` kit `synty-castle` (WO-1608 schema). Hexagon-red is the fallback if Synty is missing on the bake machine.
- Do not change radius, garrison, difficulty, unlockVictories, cooldown, star times.

---

## 6. Acceptance

**Felt:**

- [ ] Eye-level from staging: a **fort** (stone, gatehouse, tower tops), not a larger Easy square.  
- [ ] Inside: barracks / racks in a yard, then a **narrow inner gate**, then the spire.  
- [ ] One outer door.  

**Engineering:**

- [ ] Bake `BuildSceneFor("fortified_garrison")` + `RaidNavBake`.  
- [ ] Log: `Zone_Gatehouse`, `Zone_Courtyard`, `Zone_Choke`, `Zone_Keep`, inner gate width in 3.0–3.8 m, outer south gate 3.5–4.5 m.  
- [ ] `interiorWallLayers == 1` pinned.  
- [ ] If keep is raised: NavMeshLink present; a dummy agent path staging → spire completes (reuse existing path oracles if any; otherwise bake-log a `PathComplete` probe).  
- [ ] Staging SAFE. 49 m radius already stresses the plane — if staging has to move to the south-west diagonal (`RaidBaseGenerator.cs:457-467`), **report it**, do not hide it. Prefer shrinking dress, not radius, if the diagonal warning fires.  
- [ ] Captures: staging, gatehouse, courtyard toward choke, choke toward spire, top-down.  
- [ ] `COMPILE_GATE_OK`. Brace-balance + NUL.  
- [ ] Easy camp (1609) unchanged by this row.

## 7. Not in scope

Dungeon ceiling, Extreme hollow kit, extra trolls, trap DPS, star HUD, AI Hold/Hunter implementation (markers only), Iron Bastion, Village2 rebake.
