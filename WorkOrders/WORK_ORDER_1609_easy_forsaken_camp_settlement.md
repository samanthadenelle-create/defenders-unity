# WO-1609 — Easy camp: The Forsaken Camp as a scavenger settlement

**Status:** IMPLEMENTED — baked 2026-09-09 (hexagon-green, 251 dress pieces, missing=0); PO felt-test closes  
**Minted:** 2026-09-09 (CLI) — program WO-1607  
**Priority:** P0 felt — first camp the player raids; this is the screenshot that must not read as a fence  
**Lane:** World / Raid scenes (same writer as 1608, or a worktree after 1608 lands)  
**Config id:** `raider_camp_small` → scene `RaidBase_raider_camp_small`  
**Do not retune:** garrison composition (7 berserker + 2 shaman + necromancer boss), spire HP 1200, tower DPS budget, 180 s clock, `entranceCount: 2`.

> ⚠ **COUNT CORRECTED 2026-09-10 (WO-1635, lane PROPS-CANON).** The Status line above says **251 dress
> pieces**. That was the 2026-09-09 bake and is **stale**, not wrong-at-the-time; the Status line is
> **deliberately left as authored** (CLAUDE.md §15 — a dated WO header is frozen, it gets a banner, not a
> rewrite). The current bake reads **placed=314**:
> `Builds/wave3-bake7:503` — `[Flow:RaidBase] dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0 gateW=8.55 zones=Approach,Gatehouse,Courtyard`.
> ⛔ **Do not "refresh" this number again.** A hand-copied count tracking a live bake rots on the next
> bake — that is the whole defect WO-1635 was minted for. The authority is the newest
> `[Flow:RaidBase] dressed '<config id>' ... placed=` line on a FRESH bake log, never a number in a doc.
> *(WO-1635 §2 cites `Builds/wave2-bake2` for these counts; only `Builds/wave2-bake2.runner.txt` exists in
> `Builds/` — the log body is not on disk — so `Builds/wave3-bake7` is the citation actually read.)*

---

## 1. What it is today (proven)

From `scene-configs.json:66-117` + `RaidBaseGenerator.BuildConfigLayout`:

| Authored | Value | What the baker does with it |
|---|---|---|
| displayName | The Forsaken Camp | Card copy only |
| description | Scavengers strip an abandoned settlement the Heart can no longer reach | Card copy only — **geometry does not match** |
| faction | orc | Theme colour `#5a8f3a` |
| difficulty | Regular | Spire 1200 HP, tower DPS budget 12 |
| wallTier | Wood | Outer `BuildRing` only |
| baseRadius | 31 m | ~20% of the 140 m plane |
| wallSegmentsPerSide | 9 | Minimum panels / side |
| centralBuilding | `tower_siege_tower` | Spire art |
| towers[] | `tower_catapult` × 2 | Type palette (not the count) |
| archerTowerCount / mageTowerCount | 3 / 1 | 4 turrets, **all on the wall line** (`towerPlacementStyle: Cardinal`) |
| entranceCount | 2 | South + north holes |
| interiorWallLayers | 0 | No keep |
| props | empty | Nothing |
| garrison | 7 orc-berserker, 2 orc-shaman, boss orc-necromancer | Ring at 15.5 m |

The player is promised an **abandoned settlement**. They get a wood square, four turrets, a siege-tower spire, and a ring of orcs.

---

## 2. What it should feel like

A **stripped village** the orcs are camping in: broken palisade, two wagon-gaps, tents in the yard, loot piles, a watchtower that actually has a roof, and the stolen siege tower in the middle. You should know where to breach before you deploy.

**Teach on this camp (and only this camp):**

1. Staging is safe. Look. Deploy.  
2. The **gate** is the way in (two of them — flanking is allowed).  
3. Breach, then push the **siege tower** (the spire). Do not farm the palisade.  
4. Watchtowers hurt if you stand in the yard; catapults are slow and obvious.

Do not add an inner keep. Easy must stay a single yard.

---

## 3. Layout (plan — metres from centre, south is +approach)

Arena radius stays **31 m**. Do not grow it (staging already fights the 140 m plane).

```
                    NORTH GATE (secondary, wagon gap)
                           banners
        palisade ── watchtower             watchtower ── palisade
              \                                /
               \     tents  crates  fire      /
                \                           /
     WEST        COURTYARD (camp life)      EAST
                /     SPIRE = siege tower    \
               /    BossSpawn south of spire  \
              /                                \
        palisade ── catapult pit    catapult   ── palisade
                           GATEHOUSE (main)
                      two flank posts + wall_gated
                           APPROACH dirt + green banners
                           STAGING (unchanged math)
```

### 3.1 Approach (`Zone_Approach`)

- Dirt tiles: `floor_dirt_large.fbx` / `floor_dirt_small_weeds.fbx` from Dungeon Remastered (`PackRoot` in `DungeonSceneBuilder.cs:88-89`).
- 2× `banner_green.fbx` or hexagon `flag_green.fbx`.
- Optional: 1× hexagon `wheelbarrow.fbx` + `crate_A_small.fbx` as a looted cart, **offset from the walk line** so it is not a nav pinch.
- No turrets, no garrison slots.

### 3.2 Gatehouse (`Zone_Gatehouse`) — south main, north secondary

South (main):

- Hexagon `wall_straight_gate.fbx` (Village already instantiates this in `VillageSceneBuilder.Walls.cs:118`). Dungeon `wall_gated` is a barred interior opening — **not** this camp’s outer gate.
- Two hexagon `building_watchtower_green.fbx` (or `building_tower_base_green.fbx` + `building_watchtower_green.fbx`) as **flank posts**. If these also carry `DefenseTower`, they **count toward** the existing 3+1 turret budget — do not add a fifth shooter.
- Walkable width ≥ 3.5 m, logged.

North (secondary): cheaper wagon gap: hexagon `fence_wood_*_gate` or `wall_straight_gate` without extra turret. This is the flank the player discovers, not a second fortress. Halloween `wooden_gate.fbx` is allowed as a ruined-settlement tell.

### 3.3 Palisade (outer ring)

- Keep `WallSegment` colliders (Wood tier) for destructibility and nav carve.
- Visual clad: dungeon `barrier.fbx` / `barrier_half.fbx` / `barrier_corner.fbx` / `barrier_column.fbx`.
- 2–4 `wall_broken.fbx` segments on the **east or west** as a **readable weak point** (still has a collider — it is a visual tell, not a free hole). Do not open a third gate.

### 3.4 Courtyard (`Zone_Courtyard`)

This is the “something to it.” Place as **clusters**, not a ring of singles:

| Cluster | Pieces (KayKit, on disk) | Count | Notes |
|---|---|---|---|
| Sleeping camp | hexagon `building_tent_green.fbx` and/or props `tent.fbx`. Synty `SM_Bld_Tent_{Square,Small,Round}_01` allowed as extras | 3–4 | South-west and south-east of spire, 8–12 m out. Nav gap ≥ 2.5 m between tents. |
| Loot pile | dungeon `barrel_large.fbx`, `crate_large.fbx`, `box_stacked.fbx`, `crates_stacked.fbx` | 8–10 total | Against the inner face of the palisade, not in the march line from south gate to spire. Colliders on. Optional `BreakableContainer` on 2 crates max. |
| Fire / cook | dungeon `torch_lit.fbx` or hexagon fire-adjacent props (`haybale.fbx`, `trough.fbx`) | 1 cluster | Visual only. No new light system required; one warm point light allowed if MagentaGuard-safe. |
| Racks | hexagon `weaponrack.fbx`, `bucket_arrows.fbx` | 2 | Against palisade. |
| Rubble | dungeon `rubble_large.fbx`, `rubble_half.fbx` | 3 | Near broken-wall tells. |

Keep a **clear south-gate → spire lane** ≥ 4 m wide. Props dress the sides.

### 3.5 Spire

Keep `centralBuilding: tower_siege_tower`. It matches the story (stolen siege engine). Seat it, do not swap for an arcane spire.

BossSpawn stays south of the spire, clear of its footprint (`RaidBaseGenerator.cs:360-365`).

### 3.6 Towers / siege

Stay at **4 shooters** (3 archer + 1 mage counts). Visuals:

| Slot | Visual | Band |
|---|---|---|
| 2 wall-line watchtowers | hexagon `building_watchtower_green.fbx` | Outer, near south-east and south-west corners — **not** stacked on the gatehouse flanks if those already shoot |
| 1 wall-line mage / shaman post | hexagon `building_tower_A_green.fbx` or `building_archeryrange_green.fbx` | Outer, north-east |
| 2 catapults | catalog `Structures/Catapult` **or** hexagon `building_tower_catapult_green.fbx` | Courtyard emplacements, 10–14 m from centre, east and west. If they are DefenseTowers they consume turret slots — **prefer 2 of the 4 counts here** and 2 watchtowers, rather than 4 wall turrets + 2 extra catapults |

Do not raise `archerTowerCount` / `mageTowerCount`. Do not raise the DPS budget. If hexagon catapults would add shooters, they are **décor** (no `DefenseTower`).

**Palette trap (do not “fix” quietly):** Easy `towers[]` is two catapults. The baker uses that array as the type palette for **every** turret slot, so all four live turrets are catapults statistically, on cylinder art. Putting `tower_ground_archer` in `towers[]` would change Easy DPS. Default for 1609: **visual** watchtowers, **keep catapult stats**, unless the owner rules “Easy wall guns should be archer towers.” Record the ruling in the RESULT.

Cardinal placement stays (Easy teaches “the wall shoots; the centre is a push”).

---

## 4. Garrison seating (still the same 10 bodies)

Do not add units. Seat the existing composition on markers 1608 introduced:

| Who | Slot | Why |
|---|---|---|
| 2 berserkers | `GarrisonSlot_Gate_S_0/1` | South gate Hold |
| 1 berserker | `GarrisonSlot_Gate_N_0` | North gate Hold |
| 4 berserkers | `GarrisonSlot_Yard_*` | Courtyard, near tents |
| 2 shamans | Yard, behind the south tent line (not on the wall) | They shoot into the yard |
| Boss necromancer | `BossSpawn` (keep) | Unchanged |

If 1608’s spawner has no slots yet, the ring remains. 1609 must **author the markers**; the spawner change is 1608.

---

## 5. Data to write

On `raider_camp_small` (both Canonical copies):

- Fill `props` **or** the new `raidDress` block from 1608 with the token list in §3.
- Do **not** change: `baseRadius`, `entranceCount`, `interiorWallLayers`, `garrison`, `difficulty`, star times, `unlockVictories`, `raidCooldownSeconds`.
- `themeColor` stays `#5a8f3a`.

---

## 6. Files

| File | Why |
|---|---|
| scene-configs.json dual-copy | `raidDress` / props for this row only |
| RaidBaseGenerator / RaidBaseDresser | Only if 1608 left Easy-specific tables here — prefer data |
| `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` (from 1608) | Easy-specific cases below |

Do not edit Hard/Extreme rows in this ticket.

---

## 7. Acceptance

**Felt (owner closes):**

- [ ] Eye-level from staging: a **camp**, not a fence. Tents, palisade, gate, watchtower **top** visible.  
- [ ] Top-down: two gates, a clear south lane to the siege-tower spire, props on the sides.  
- [ ] Walking in, you pass a gatehouse, then a yard of tents, then the spire.  

**Engineering:**

- [ ] Bake via `RaidBaseGenerator.BuildSceneFor("raider_camp_small")` then `RaidNavBake`. No `.unity` hand-edit.  
- [ ] Bake log: `Zone_Approach`, `Zone_Gatehouse`, `Zone_Courtyard`, `GATE south width>=3.5`, `GATE north` present.  
- [ ] Staging still SAFE (WO-1520).  
- [ ] Layout regression: Easy courtyard prop count ≥ 12; at least one GameObject whose mesh is a KayKit tent or watchtower when the pack is present; `interiorWallLayers` still 0.  
- [ ] Headless captures: staging eye-level, south gate, courtyard toward spire, top-down. Open the PNGs.  
- [ ] `COMPILE_GATE_OK`. Missing pack → warnings, not a failed bake.  
- [ ] Brace-balance + NUL on every `.cs`.

## 8. Not in scope

Inner keep, spike floors, dungeon ceiling, extra enemies, HP/DPS, star HUD, AI roles, Hexagon **units** as orcs.
