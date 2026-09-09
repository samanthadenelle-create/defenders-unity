> **2026-09-09 — Q1 ANSWERED FROM DISK; IMPLEMENT VIA WO-1607–1611.**
> Owner asked again to review the raid bases, use KayKit dungeon, and give them defenses
> beyond fence + tower. The kit inventory, named-zone engine, and per-camp layouts now live
> in `WORK_ORDER_1607_raid_bases_as_places.md` (spine) + 1608 (engine) + 1609 Easy / 1610 Hard /
> 1611 Extreme. Do **not** bake this ticket as a second art pass on the same scenes.
> Q1 four-vibe table below is superseded by 1607 §4 (Easy hexagon-green scavenger camp,
> Hard hexagon-red fortress, Extreme dungeon-stone keep; Iron Bastion parked). Q2 remains:
> Easy first (1609), then Hard, then Extreme.

# WO-1593 — KayKit raid bases: landscape, walls, and towers that are not pillars

**Status:** READY TO IMPLEMENT — kit pick answered by WO-1607; implement through 1608–1611, do not double-bake  
**Minted:** 2026-09-07 — program WO-1592; banner bumped with 1592–1595  
**Priority:** P0 felt — “simple walls / towers look like a pillar / better landscape, could be entire KayKit”  
**Lane:** World / Raid scenes (serialization bottleneck: one agent on raid builders at a time)  
**Scenes:** `RaidBase_raider_camp_small`, `RaidBase_fortified_garrison`, `RaidBase_IronBastion`, `RaidBase_mage_enclave`  
**Law:** never hand-edit `.unity` — rebuild via existing raid/camp builders + injectors

---

## 1. Problem (felt)

Raid bases read as **programmer placeholders**: thin wall runs, towers that silhouette as **vertical pillars**, ground that does not sell a biome. The fight may be fair and still feel cheap.

---

## 2. Creative direction (proposal — owner confirms Q1)

### 2.1 Kit strategy — “KayKit whole camp”

Use **one KayKit family per camp** so the base feels authored, not mixed scrap:

| Camp (easy → hard) | Proposed KayKit vibe | Landscape tell |
|---|---|---|
| `RaidBase_raider_camp_small` | KayKit **Prototype / Medieval** wood + canvas | Dirt ring, campfires, stakes, banners |
| `RaidBase_fortified_garrison` | KayKit **stone fortress** kit | Cobble yard, thicker curtain walls, gatehouse |
| `RaidBase_IronBastion` | KayKit **industrial / dark metal** accents on stone | Slag ground, iron braces, forge smoke props |
| `RaidBase_mage_enclave` | KayKit **mage / crystal** props on stone | Runed plaza, crystal lamps, purple banners |

⚠ Exact prefab folder names must be verified against the imported KayKit packs on disk (`docs/kaykit-asset-catalog.md` / `Assets/` packs). **Owner picks or vetoes the four vibes** before a bake.

### 2.2 Walls — thickness and corners

- Replace “single thin quad / stick” segments with **KayKit wall modules**: straight, corner, T, gate, ruined breach.  
- Footprint must still match navmesh carve / troop pathing (measure cells; do not shrink walkable corridors by accident).  
- Damaged state: optional cracked module or VFX — not required for V1 of this WO if time-boxed.

### 2.3 Towers — platforms, not pillars

A raid tower must read as **architecture**:

| Required silhouette bits | Why |
|---|---|
| Base / plinth | Not a floating pole |
| Shaft with cross-section change | Breaks “cylinder” read |
| Fighting top (crenel / roof / banner) | Skyline identity |
| Optional ladder / door tell | Human scale |

Map existing `DefenseTower` / spire behaviors onto KayKit tower prefabs via catalog `visualPrefabPath` / raid layout JSON — **behavior ids stay frozen**.

### 2.4 Landscape

- Ground: textured terrain or KayKit floor tiles per vibe (not default Unity plane).  
- Scatter: 5–15 prop instances (crates, banners, tents, barrels) **without** blocking the staging pocket (WO-1520).  
- Sky/fog: light per-camp tint (readable day fight — no black crush).

---

## 3. Implementation shape

1. **Inventory** KayKit wall/tower/prop paths that exist in this clone (`tools/art/verify-runtime-art.ps1` + catalog).  
2. Author or extend **raid layout data** (prefer JSON / builder tables over scene hand-edit).  
3. Wire raid base builder / `RaidGarrisonSpawner` seating to new visuals.  
4. Re-bake nav only through approved batchmode path; editor closed.  
5. Capture top-down + eye-level PNGs per camp for owner compare.

---

## 4. Owner ruling needed (one answer)

**Q1.** Approve the four camp vibes in §2.1, or name replacements (still KayKit-first).  
**Q2.** Easy camp first only vs all four camps in one pass? **Recommend Easy + Garrison first**, Iron/Mage stretch.

---

## 5. Acceptance

1. Easy camp eye-level screenshot: walls have visible thickness; at least one tower has a **top**, not a bare pillar.  
2. Staging pocket still outside defender range (WO-1520).  
3. Troops can path to the objective without new softlocks.  
4. No magenta / missing KayKit on a machine with packs imported; missing pack → `LogWarning`, not hard error.  
5. `COMPILE_GATE_OK`; raid smoke still ends (no softlock).  

## 6. Not in scope

Star HUD (1594), AI retune (1595), HP/damage balance, army slot caps.
