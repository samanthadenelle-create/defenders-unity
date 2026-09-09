# WO-1611 — Extreme camp: The Veiled Enclave as a KayKit dungeon keep

**Status:** IMPLEMENTED — baked 2026-09-09 (dungeon-stone keep, 668 dress pieces); PO felt-test closes  
**Minted:** 2026-09-09 (CLI) — program WO-1607  
**Priority:** P1 felt — this is the camp the owner pointed at KayKit **dungeon** for  
**Lane:** World / Raid scenes (serialize with 1608–1610)  
**Config id:** `mage_enclave` → scene `RaidBase_mage_enclave`  
**Do not retune:** garrison (7 hollow-acolyte + 5 orc-shaman + 7 hollow-warrior + necromancer boss + 3 elites), spire HP 3500, tower DPS budget 20, `entranceCount: 1`, `interiorWallLayers: 1` (already), OverlappingFire 7+3 turrets, `shardDropChance: 0.2`, `unlockVictories: 10`.

---

## 1. What it is today (proven)

From `scene-configs.json:184-239`:

| Authored | Value | Baker result |
|---|---|---|
| displayName | The Veiled Enclave | Copy only |
| description | Something inside has learned to bend fractured memories into magic | Copy only |
| faction | hollow | `#7a5fb0` |
| difficulty | Extreme | Spire 3500, DPS budget 20 |
| wallTier | ReinforcedSteel | Outer ring + **one** inner keep ring (`interiorWallLayers: 1`) |
| baseRadius | 54 m | ~60% of the 140 m plane — staging is already tight (`PlaceStagingMarker` diagonal fallback `:457-481`) |
| centralBuilding | `tower_arcane_spire` | Spire |
| towers[] | `tower_arcane_spire` × 2 | Palette — skyline is more spires, still not a dungeon |
| props | empty | Nothing |
| garrison | hollow acolytes/warriors + shamans | Ring at 27 m |

This is the only flagship camp that already has an inner ring, and it still reads as **two steel fences**. The copy says a **veiled enclave / ritual interior**. The geometry is an arena.

Healer’s Cottage and KayKit Challenge Outpost already prove the dungeon kit in this repo (`DungeonSceneBuilder` PackRoot, `KayKitChallengeOutpostBuilder`). Use that kit **as a keep**, not as a second underground dungeon crawl. The raid stays an outdoor assault that **enters a stone sanctum**.

---

## 2. What it should feel like

A **stone sanctum** the hollow have occupied: outer steel/stone curtain, one gate, a spare courtyard (not a village — this is not Easy), then you **go inside**. Columns, banners, torches, a grate choke, the arcane spire on a dais. It should feel like the Healer’s Cottage fortress palette, seen from a raid.

**Teach on this camp:**

1. The yard is a delay, not the fight.  
2. The keep is a room. Standing in the doorway is death (inner turrets + choke).  
3. The spire is an altar, not another pole.

Do not enclose the **whole 54 m arena** with a ceiling — that is a dungeon level, not a raid. Ceiling / dark ambient apply to the **keep** only.

---

## 3. Kit (KayKit dungeon first)

Primary: `Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/`  
(`DungeonSceneBuilder.cs:88-89`).

Legacy twin `Assets/Models/KayKit/dungeon/` is acceptable via the Outpost token loader.

Hexagon pack: **shrine / castle as the dais dress only** (`building_shrine_*`). Prefer `neutral` or tint; do not paint this camp green/red like the others. No hexagon tents. No hexagon units.

Banners: dungeon `banner_patternA_white.fbx` / `banner_patternC_white.fbx` / `banner_shield_white.fbx` (hollow / memory — not orc green).

---

## 4. Layout (radius stays 54 m — do not grow)

54 m already crowds staging. **Do not add outer towers further out than today.** Prefer moving dress inward.

```
     steel/stone OUTER curtain (one SOUTH gatehouse)
          thin courtyard (columns + banners, not a town)
                    CHOKE
              grate + spike décor
              wall_gated inner door
                    KEEP  (the dungeon room)
         columns  torches  inset shelves
         partial CEILING over the keep only
              dais + ARCANE SPIRE
              BossSpawn on the dais
```

### 4.1 Approach

- `floor_tile_large.fbx` / `floor_tile_large_rocks.fbx`.
- 2× white/purple banners. No carts, no tents.

### 4.2 Outer gatehouse

- Outer door: Hexagon `wall_straight_gate.fbx` **or** Synty castle gate if it does not make Extreme read as Hard. Dungeon `wall_gated.fbx` is reserved for the **inner keep threshold** (it is a barred opening, not a curtain gate). Flank with `wall_archedwindow_gated.fbx` + `wall_pillar.fbx` on the inner face.
- One door. Width 3.5–4.0 m.
- Flank “towers” should read as **stone turret bases** (`wall_corner.fbx` stacked with `column.fbx` / `pillar_decorated.fbx`) if hexagon watchtowers would faction-colour this camp. Catalog `tower_arcane_spire` art may appear here **as one of the existing 10 turrets**, not as extras.

### 4.3 Outer curtain

- Keep ReinforcedSteel `WallSegment` colliders.
- Visual: `wall_cracked.fbx` / `wall.fbx` / `wall_corner.fbx` / `wall_sloped.fbx`.
- Sparse `wall_broken.fbx` as “the veil is cracking,” not as a third gate.

### 4.4 Courtyard — spare on purpose

This is not a settlement. If it is as busy as Easy, Extreme loses identity.

| Piece | Count | Role |
|---|---|---|
| `column.fbx` / `pillar_decorated.fbx` | 6–8 | Colonnade along the march, ≥ 5 m aisle down the centre |
| banners (white / pattern) | 4 | On columns |
| `torch_mounted.fbx` / `torch_lit.fbx` | 6 | On outer inner-face. Candle VFX optional; do not copy Outpost’s full night ambient across the 54 m yard (HUD/troops must stay readable). |
| `rubble_large.fbx` | 3 | Corners |
| `chest.fbx` / `chest_gold.fbx` | 2 | Décor or `BreakableContainer`. Not required to win. |

No barracks. No tents. No weapon racks.

### 4.5 Choke — the dungeon threshold

Inner ring already exists (`interiorWallLayers: 1`). Turn the inner opening into a **threshold**:

- Inner door: `wall_gated.fbx` or `wall_doorway_Tsplit.fbx`.
- Walkable 3.0–3.5 m.
- Beside the strip (not on it): `floor_tile_big_spikes.fbx`, `floor_tile_big_grate.fbx`, `floor_tile_grate_open.fbx`.
- `wall_archedwindow_gated.fbx` as the last thing you see before the room.

V1: **no spike damage type.** Geometry + read only.

### 4.6 Keep — the dungeon room (the point of this ticket)

Build a **room** around the spire, roughly the inner-ring footprint (keep radius is 45% of innermost, so ~0.45 × 54 ≈ 24 m half-extent before the inner ring shrinks it — measure off the bake, do not hardcode a second radius).

Must have:

1. **Floor:** `floor_tile_large.fbx` / `floor_tile_small_decorated.fbx` / a few `floor_tile_small_broken_A.fbx`.  
2. **Walls:** inner ring clad with `wall_cracked` / `wall_inset_shelves` / `wall_inset_candles`.  
3. **Columns:** 4 `pillar_decorated.fbx` at the dais corners.  
4. **Dais:** `floor_foundation_allsides.fbx` (or stacked `floor_foundation_front.fbx`) under the spire, +0.5 to 1.0 m. Stairs: `stairs_wood.fbx` or `stairs_long.fbx` on the **south** face (the side the choke feeds). NavMeshLink required.  
5. **Partial ceiling:** `ceiling_tile.fbx` over the keep **only**, at wall-top height, colliders stripped (Outpost `BuildCeiling` pattern, `:33-39` in that builder). The courtyard stays open sky.  
6. **Torches:** `torch_mounted.fbx` on inner walls. Mood lighting **inside the keep** may follow Healer’s Cottage `ConfigureAmbient` **locally** (a boxed volume), not over the whole raid. If a local volume is too much for V1, mounted torches + slightly darker keep floor tint is enough.  
7. **Altar dress:** hexagon `building_shrine_*.fbx` **behind** the spire, or dungeon `chest_gold.fbx` + `candle_triple.fbx` on the dais. Must not hide the spire silhouette.  
8. Keep `centralBuilding: tower_arcane_spire`.

BossSpawn on the dais, south of the spire, not inside it.

### 4.7 Towers

Stay at **10** (7+3). OverlappingFire stays.

Visual override: stone / arcane, not wood watchtowers.

- Outer band: dungeon-clad turret bases or catalog `Structures/ArcaneSpire_1` / `tower_arcane_spire` art.  
- Inner band: inside the keep, on the dais ring — these are the “do not linger in the doorway” guns.

Do not add an 11th shooter. Do not push the outer band further out (staging).

---

## 5. Garrison seating (same bodies)

| Who | Slot | Job |
|---|---|---|
| 2 hollow-warriors | Outer gate | Hold |
| 5 hollow-warriors | Courtyard colonnade | Delay / Hunter candidates |
| 4 acolytes | Choke flanks + keep door | The doorway problem |
| 3 acolytes | Keep, on the dais | Inner Hold |
| 5 shamans | Outer curtain / inner windows | Shooters |
| 3 elites | Keep corners | Last ring |
| Boss necromancer | Dais BossSpawn | Unchanged |

Hollow bodies stay the catalog enemies. **No KayKit skeleton / adventurer meshes.**

---

## 6. Data to write

On `mage_enclave` (both Canonical copies):

- `raidDress` kit `dungeon-stone`.
- `interiorWallLayers` stays **1**.
- Do not change radius, garrison, difficulty, shard chance, unlockVictories, cooldown, star times.

If 54 m makes staging unsafe after gatehouse flanks, **shrink dress / turret outer band**, not the plane, and log the before/after reach. Never soften the staging assert.

---

## 7. Acceptance

**Felt:**

- [ ] From staging: a stone enclave, not Easy’s camp and not Hard’s barracks fort.  
- [ ] After the inner door: you are **inside a room** (floor, columns, torches, a ceiling over the spire).  
- [ ] The spire reads as an altar in that room.  

**Engineering:**

- [ ] Bake `BuildSceneFor("mage_enclave")` + `RaidNavBake`.  
- [ ] Log: all zones including `Zone_Keep`; `ceiling_tile` count > 0 **under the keep**; courtyard ceiling count = 0.  
- [ ] Inner door width 3.0–3.5 m; outer south 3.5–4.0 m.  
- [ ] NavMeshLink on the dais stair; path staging → spire completes.  
- [ ] Staging SAFE (or a loud layout finding if 54 m cannot hold it — then cut outer-band radius, do not silence the assert).  
- [ ] Captures: staging, outer gate, courtyard colonnade, choke threshold, keep interior toward spire, top-down.  
- [ ] Layout regression: Extreme keep has ≥ 4 columns, ≥ 1 ceiling tile, `props`/`raidDress` non-empty; Easy still has tents (1609 must not regress).  
- [ ] `COMPILE_GATE_OK`. Brace-balance + NUL.  
- [ ] Missing KayKit: bake warns and falls back; does not throw.

## 8. Not in scope

A full multi-room dungeon crawl, Healer’s Cottage encounters, KayKit enemies, extra elites, trap DPS, shrinking `unlockVictories`, Iron Bastion, Village2, star HUD, AI rewrite.
