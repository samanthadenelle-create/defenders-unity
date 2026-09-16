# Raid Systems Reference: Wall & Structure Building

Technical reference for the wall segment, structure damage, and raid base generation systems. This document is built from code inspection (file:line citations) and is a synthesis, not a raw code dump.

---

## 1. Wall Segment Structure Definition — HP Scale, Damage Tiers, Toughness

### Core Damage Model

A wall segment uses an **inverted damage track**: accumulated damage counts upward from 0 to 100, where 100 represents complete collapse/destruction. The corresponding HP is the inverse: `Hp = Max(0, 100 - Damage)` (`WallSegment.cs:261`).

**Damage scale:**
- `MaxHp = 100f` (inverted: Damage spans 0..100, Hp spans 100..0) (`WallSegment.cs:109`)
- `IsDestroyed` when `Damage >= 100f` (`WallSegment.cs:146`)
- `DamageFraction = Clamp01(Damage / 100f)` for normalized 0..1 representation (`RepairTarget.cs:141`)

### Tier System & Effective HP Scaling

Wall segments are authored with an upgrade **tier** (1..`RepoProps.MaxStructureLevel`, which is 6) (`WallSegment.cs:149`, `RepairTarget.cs:148`). Higher tiers reduce the rate at which damage accumulates on the 0-100 track; they do **not** increase the scale of the track itself.

**Toughness formula** (per-tier divisor):
```
ToughnessFor(tier) = Mathf.Pow(1.6f, tier - 1)
```
- Tier 1: ×1 (baseline)
- Tier 2: ×1.6
- Tier 3: ×2.56
- Applied formula: `effective_damage = incoming_damage / ToughnessFor(tier)` (`WallSegment.cs:334`, `:165-169`)

**Height scaling by tier:** Each tier pulls its wall height from `walls.json` (authored per ladder level); the collider height is updated via `ApplyTierBlockerHeight()` to match the visual height, applying only the Y component of the blocker (`WallSegment.cs:195-226`).

### BULWARK Talent Reduction

The hero's structure-toughness talents (the "Hardened Ramparts" / "Warden of Elarion" nodes) reduce incoming damage on the hero's **own** Elarion perimeter walls, but **NOT** on enemy raid walls. This is critical for game balance: defensive talents never make raiding easier.

**BULWARK mechanics:**
- `structureToughness` (Hardened Ramparts) is always-on; multiplies effective damage by `(1 - reduction)` (`WallSegment.cs:336-344`, `:476-492`)
- `structureToughnessWave` (Warden of Elarion) is added **only while the wave phase is Active**
- Total reduction is capped at 0.5 (50%) (`WallSegment.cs:484-490`)
- **Ownership gate:** The reduction is applied **only if `Faction == CombatFaction.Friendly`** (the hero's own walls). Enemy raid walls have `Faction == CombatFaction.Hostile` and receive NO talent reduction (`WallSegment.cs:343-344`)

The ownership is derived from the scene: `SceneOwnership.IsEnemyOwned ? CombatFaction.Hostile : CombatFaction.Friendly` (`WallSegment.cs:248-249`).

---

## 2. Wall Collapse — Visual & Gameplay State

### Collapse Trigger and Effects

When `Damage` reaches or exceeds 100, `Collapse()` is called exactly once (guarded by `_collapsed` flag, never re-armed) (`WallSegment.cs:362-365`). The collapse is **permanent**; destroyed structures cannot be repaired in-place (see WO-753 ruling below).

**Physical effects at collapse:**

1. **Colliders disabled:** Every solid collider on the section (and children) is disabled so troops and the hero can walk through (`WallSegment.cs:369-375`). This happens immediately, frame 0.
2. **NavMesh carving disabled:** Every `NavMeshObstacle` with `carving=true` is disabled, handing the carved navmesh back to the base walkable surface so agents can path through the gap (`WallSegment.cs:383-388`). The `RaidNavBake` system carves obstacles for walls (`RaidNavBake.cs:171-182`).
3. **Events raised:** `Collapsed` event fires, notifying subscribers (HUD, visuals) (`WallSegment.cs:394`).

### Visual Collapse

A coroutine starts (in-play only; edit-mode regression tests skip it) that **accelerates the section downward and ramps a shader collapse property** (`WallSegment.cs:399-401`, `:409-439`). The visual is **readability only**; the gameplay effect (colliders/obstacles disabled) is instantaneous.

- **Sink distance:** Calculated from renderer bounds, or the configured `_height` if no renderer (`WallSegment.cs:416-423`)
- **Sink fraction:** 0.85 of the span, so a stub of rubble remains visible above ground (`WallSegment.cs:114`)
- **Duration:** Configurable `_collapseSeconds` (default 0.9s), with accelerating ease-out (`WallSegment.cs:427-438`)
- **Shader property:** The `_Collapse` ramp is pushed to the material's `MaterialPropertyBlock` so the visual matches the game's one destruction tell (the Gate uses the same shader ramp) (`WallSegment.cs:121`, `:446-457`)

### Repair Model: Destroyed Sections Are Lost

**WO-753 ruling (owner 2026-07-19):** A fully destroyed/collapsed section is **permanently lost** — it cannot be repaired in-place. It returns only via a full-cost rebuild (placing a fresh section in build mode). The `Repair()` method refuses to revive a destroyed section:

```csharp
if (IsDestroyed) return;  // guards Repair(amount) against destroyed sections
```
(`WallSegment.cs:517`). In `RestoreOwnedTownCondition()` (used only on owned-town load), the same guard prevents restoration on destroyed sections (`WallSegment.cs:505-510`).

---

## 3. Raid Base Generation — Procedural Wall Placement

### Generation Pipeline

`RaidBaseGenerator` is the entry point for creating raid bases. It builds **NOT from hand-edited scenes**, but from configuration data in `scene-configs.json`. The config defines:
- `baseRadius`: the arena's half-extent (metres)
- `wallSegmentsPerSide`: the minimum wall panel count (granularity)
- `wallTier`: the material tier for the outer perimeter (`RaidBaseGenerator.cs:505-509`)
- `interiorWallLayers`: the number of concentric keep rings inside the outer perimeter (`RaidBaseGenerator.cs:515-529`)

### Wall Ring Construction

Each wall ring is built by `BuildRing()`, which:

1. **Computes segment count:** Given the run length (radius × 4 for a square perimeter, 2 for each side) and a `MaxSegmentWidth` cap (3.0m), the builder ensures no panel is stretched past that width. Panels shrink to fit, never widen. (`RaidBaseGenerator.cs:196-199`, `:508-509`)

2. **Places segments:** Each segment gets a `WallSegment` component with:
   - A stable damage id: `wall-<index>` (`WallSegment.cs:64`)
   - A configured tier (higher tiers → tougher walls) (`WallSegment.cs:179-183`)
   - A `BoxCollider` on the "Structure" layer for line-of-sight and physics (`WallSegment.cs:537-545`)
   - A `NavMeshObstacle` with `carving=true` and `carveOnlyStationary=true` so the obstacle is disabled on collapse and the navmesh opens up (`RaidNavBake.cs:171-182`)

3. **Tile arrangement:** Panels are placed along the ring's perimeter, with gate openings cut into the specified sides (south gate always, optional north gate) (`RaidBaseGenerator.cs:506-507`)

### Arena Radius & Footprint

The arena occupies a fraction of the 140m × 140m `RaidGround` plane (`RaidBaseGenerator.cs:92`):
- `radius = MapHalfExtent * sqrt(footprint)` where `footprint` is the config's authored fraction
- Tier (difficulty) determines footprint: Regular 20%, Hard 50%, Extreme 60% (`RaidBaseGenerator.cs:352-360`)
- Minimum radius 10m, maximum 63m (90% of the half-extent) (`RaidBaseGenerator.cs:500`, `:492-499`)

### Exterior Boundary Ring (WO-1632)

Every raid arena also carries an **exterior boundary ring** of landscape pieces (large polyperfect rocks or tree models) that frame the entire 140m plane edge, not just the inner base. This is a **cosmetic perimeter that does NOT affect gameplay**—it has no gates, is not part of the layered defense, and does not touch base HP calculations.

- Placement: `ArenaBoundaryRing.PlaceSquarePerimeter()` (`ArenaBoundaryRing.cs:260-371`)
- Pieces: polyperfect `_M` tier rocks/trees from `Prefabs_M/` (gitignored pack, falls back to primitives) (`ArenaBoundaryRing.cs:76-147`)
- Scale: Band-fitted so the widest piece never reaches farther than the allowed depth into the staging zone (`ArenaBoundaryRing.cs:273-285`)

---

## 4. Cover Ring & Courtyard Decoration Placement

### Courtyard Band Definition

The **courtyard cover ring** is placed in a radial band between the spire exclusion zone and the outer wall, divided into concentric rings. The band is defined by:

- **Inner edge:** Either `CourtyardSpirePad` (9m) around the spire center, or `CourtyardInnerPad` (3.5m) beyond the innermost keep ring if one exists (`RaidBaseDresser.cs:868-872`)
- **Outer edge:** `Radius - CourtyardWallPad` (3.8m inset from the wall line, to avoid burying turrets) (`RaidBaseDresser.cs:873`)
- **Ring spacing:** 7m between concentric rings; maximum 3 rings per courtyard (`RaidBaseDresser.cs:47-50`)

The rings are built deterministically from a seeded RNG so a re-bake reproduces the identical layout (`RaidBaseDresser.cs:862-866`).

### Jitter & Collision

Props in clusters are placed with:
- **Cluster anchor:** Deterministically positioned on one of the concentric rings (`CoverRingPlacer.ClusterAnchorAngle()`)
- **Radial jitter:** Symmetric scatter (both inward and outward), bounded by remaining band slack after accounting for prop size and containment margins (`ArenaBoundaryRing.cs:303-315`)
- **Tangential jitter:** Only as much as the overlap between adjacent pieces allows—never opens a gap (`ArenaBoundaryRing.cs:300-301`)
- **Scale roll:** Props scale 0.92–1.12x (tighter than the arena's 0.9–1.6 to avoid looking oversized) (`RaidBaseDresser.cs:57-58`)

### Keepout Zones

Props are rejected (re-rolled or dropped) if they land in:
1. **The assault lane:** Within `GateWidth/2 + 1.0m` of the center X-axis (the south→north march corridor)
2. **The spire pocket:** Within 66% of `CourtyardSpirePad` from the center
3. **Turret footprints:** A 3.5m clearance circle around each placed `DefenseTower` (`RaidBaseDresser.cs:60-61`, `:972-995`)
4. **Staging zone:** A 10m clearance circle around the `RaidStagingPoint` (the hero's deploy position) (`RaidBaseDresser.cs:63-64`)

Props marked as "cover" keep their colliders (`stripColliders=false`); decoration props have colliders stripped (`RaidBaseDresser.cs:1041-1042`).

---

## 5. Wall Repair Mechanics

### Player-Facing Repair Flow

The repair system is driven by `WallRepairController`, a self-contained MonoBehaviour that:

1. **Scans** for damaged structures on a timer (default 0.75s) (`WallRepairController.cs:223-232`, `:137`)
2. **Selects** a structure on tap (camera raycast with UI guard to prevent tapping through widgets) (`WallRepairController.cs:238-239`, `:401-407`)
3. **Shows a prompt** with materials cost (damage-fraction × structure's catalog build cost) (`WallRepairController.cs:532-572`)
4. **Confirms** and spends through the construction economy (`EconomyService.TrySpend`), then calls `RepairTarget.Repair()` or `RepairFull()` (`WallRepairController.cs:1092-1102`)

### RepairTarget Abstraction

`RepairTarget` wraps one repairable structure (`WallSegment`, `Gate`, or `Building`) behind a uniform interface:
- **Wrapping:** `RepairTarget.TryWrap(collider)` walks `GetComponentInParent<WallSegment>()` / `Gate()` / `Building()` and rejects anything tagged "Player" (`RepairTarget.cs:77-101`)
- **Damage fraction:** Normalized 0..1 (`WallSegment.Damage / 100`, inverted from HP for Gate/Building) (`RepairTarget.cs:134-150`)
- **Repair verb:** `Repair(amount)` for incremental repair, `RepairFull()` for complete restoration (`RepairTarget.cs:198-262`)

### Repair Cost

**Owner ruling 2026-07-11:** Repair cost = `damage_fraction × structure's_own_catalog_build_cost`. Crystals are **never** spent on base repair (though a carve-out allows crystals to cover a materials shortfall in the "repair with crystals" flow). The cost is computed from:

1. **PlacedStructure** → its own catalog row (the id placement charged)
2. **Building** → the `buildings.json` row matching `Building.BuildingId`
3. **ResourceCollector** → the collector row with matching `collectorBuildingId`
4. **Scene-built WallSegment/Gate** → fallback rows `wall_stone` / `gate_stone`
5. **Everything else** → the data-driven `repair_default` row

(`WallRepairController.cs:633-648`, `:866-910`)

### Repair-All & Worst-First Sweep

`RepairAll()` repairs every damaged structure in the scene, worst-first (highest damage fraction first), skipping any that the wallet cannot afford (greedy, partial repair is honest) (`WallRepairController.cs:1035-1130`). The same cost computation and materials-spend path is used; crystals can cover shortfalls if the player has them and the authored exchange rate allows it (`WallRepairController.cs:845-846`, `:1060-1090`).

A destroyed structure (damage-fraction ≥ 0.999) is **never** a Repair-All target; it would require a full-cost rebuild (`WallRepairController.cs:358-362`).

### Pointer-Over-UI Guard (WO-1708)

Before raycasting the world, `HandleTap()` checks `PointerIsOverUi()` to prevent tapping buttons (including the F8 FLAG button) from also selecting/repairing a structure behind the UI (`WallRepairController.cs:406`). This is a **critical gate** that was missing until WO-1708, allowing unintended selections.

---

## 6. NavMesh & Pathing — Wall Role in Navigation

### Ground & Bake Setup

Every raid base has a **continuous `RaidGround` plane** (y=0) that serves as the walkable floor for troops and agents. This plane is:
- **Fitted to the boundary ring** so it does not enlarge the playable area beyond the scenic perimeter (`RaidNavBake.cs:188-238`)
- **Textured** with the scene's owned terrain layer (dirt or tile, repeating at metre scale) (`RaidNavBake.cs:247-265`)
- **Collider:** A shared `MeshCollider` (convex=false, not a trigger) so physics and navigation both walk on it (`RaidNavBake.cs:240-245`)

The legacy Unity NavMesh is baked after all destructible objects (walls, towers) have `NavMeshObstacle` components attached (`RaidNavBake.cs:79-80`).

### Wall Obstacle Carving

Walls are **NOT baked into the navmesh**; they use **runtime carving obstacles:**
- Each `WallSegment` gets a `NavMeshObstacle` with `shape=Box`, `carving=true`, `carveOnlyStationary=true` (`RaidNavBake.cs:172-179`)
- The obstacle's bounds are derived from the segment's own `BoxCollider` (`RaidNavBake.cs:174-175`)
- **Critically:** The obstacle is **enabled while the wall stands** (`obstacle.enabled = wall.HpFraction > 0f`) (`RaidNavBake.cs:179`)
- When the wall collapses and its collider is disabled, the obstacle is **also** disabled, opening the path immediately (`WallSegment.cs:386-387`)

This two-level system (continuous ground bake + carving obstacles for destructibles) means:
- The base floor is guaranteed walkable even if every obstacle is disabled
- Destroying a wall opens a path in real-time without a rebake

### Line-of-Sight Blocking

Walls block tower fire (and hero line-of-sight) via the **"Structure" physics layer**:
- Every wall segment is placed on layer "Structure" (`WallSegment.cs:543-544`)
- Tower line-of-sight linecasts use the "Structure" mask (`DefenseTower.BlockedByWall`, named in `TowerCombat.cs` and `ArcaneTower.cs`)
- On wall collapse, the collider is disabled, so the linecast passes through immediately

This is different from the `RaidSpire` trick (which moves onto the "Enemy" layer to be findable by sweeps); walls stay on "Structure" because moving them would make towers shoot through walls again—**the line-of-sight blocker mask must not change** (`WallSegment.cs:30-39`).

---

## Citations Index

All claims are sourced from the following files (read 2026-09-14):

| File | Lines Referenced | Subject |
|------|-----------------|---------|
| `Assets/_Modules/Village/Walls/WallSegment.cs` | 1-546 | Wall damage model, tier system, toughness formula, BULWARK reduction, collapse mechanics, collider/obstacle management, ownership gate, height scaling |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | 1-975 (partial) | Raid base generation, wall ring placement, segment count computation, arena radius, difficulty tiers, exterior boundary ring integration |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | 1-1059 (partial) | Courtyard cover ring, prop placement, jitter, keepout zones, cover vs. decoration colliders, ring spacing, scale rolls |
| `Assets/Editor/ArenaBoundaryRing.cs` | 1-639 | Arena boundary placement, square perimeter vs. polar ring, band fitting, piece overlap, radial/tangential jitter bounds, material measurement, graceful prefab fallback |
| `Assets/Editor/RaidNavBake.cs` | 1-346 | Ground plane creation and fitting, terrain texture application, wall NavMeshObstacle setup with carving, tower obstacle measurement and carving, enable/disable on health state |
| `Assets/_Modules/Village/Walls/RepairTarget.cs` | 1-348 | Repair target abstraction, damage normalization, display names, cost retrieval, bounds measurement for highlight rings, herotagexclude |
| `Assets/_Modules/Village/Walls/WallRepairController.cs` | 1-1586 (partial) | Player repair loop, structure scanning, selection highlighting, cost computation, materials-spend path, Repair-All sweep, worst-first ordering, pointer-over-UI guard, destroyed structure handling |

---

**Document version:** 2026-09-14  
**Authored from:** Source code reading (no guesses; every claim is traceable to file:line)  
**Status:** Ready for design research / system documentation handoff
