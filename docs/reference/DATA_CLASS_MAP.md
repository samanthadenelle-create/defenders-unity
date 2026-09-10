# DATA_CLASS_MAP — canonical JSON ⇄ owning C# class

**Status:** living registry · **Authored:** 2026-08-09 · **Method:** read from source, not from comments (§12 / CLAUDE.md).

**Why this exists.** The owner asked *"is there an enum for shop/store?"* and answering it took a grep.
The catalog carries **five** classification axes that disagree in shape, and no single doc said which one
decides what. This file is the answer. Every claim below carries a `file.cs:line` or a JSON path.

**Scope:** all **80** JSON files under `Assets/Resources/Data/Canonical/` and their
`Assets/StreamingAssets/Data/Canonical/` twins.

**Read [§7 Classification axes](#7-classification-axes--which-field-decides-what) first** — it is the point of the
doc, and it opens with the owner's shop/store question answered directly. Everything above it is the evidence.

**Quick index:** §1 load path · §2 ⚠ dual-copy drift · §3 buildings · §4 combat · §5 items/economy ·
§6 meta/UI/world · **§7 the axes** · §8 what's persisted · §9 dead-data ledger

---

## 1. The load path

Every catalog reads through one seam:

| Step | Where |
|---|---|
| `CanonicalJson.Read("Data/Canonical/<name>.json")` | `Assets/_Modules/Core/Data/CanonicalJson.cs:41` |
| delegates to a swappable `ICatalogSource` | `CanonicalJson.cs:36` (defaults to `LocalJsonCatalogSource`) |
| **Resources.Load first** (WebGL-safe), StreamingAssets only as fallback | `CanonicalJson.cs:13-17` (header) |
| empty read → `FlowTrace.Warn`, never silent | `CanonicalJson.cs:53-55` |

**The load-order consequence that matters:** the JSON lives in **two** places and
**`Assets/Resources/...` WINS**. `Assets/StreamingAssets/...` is documented as "the source" but is only
ever read when the Resources copy is *absent*. Editing StreamingAssets alone changes nothing at runtime.

Unknown JSON fields are **silently dropped** — `MissingMemberHandling.Ignore`
(`Assets/_Modules/Village/Catalog/CatalogBootstrap.cs:116`). There is no warning for a misplaced or
misspelled field. This is the mechanism behind several entries in [§7](#7-dead-data--missing-fields).
Unknown *enum values*, by contrast, throw (`StringEnumConverter`, `CatalogBootstrap.cs:114`), which
aborts the whole parse and drops the loader onto its hardcoded fallback rows.

---

## 2. Dual-copy drift — ⚠ the highest-impact finding in this doc

The two copies are required to be byte-equal ("Keep them in sync; Resources wins at load time",
`CanonicalJson.cs:16-17`). Of the **77** files present on both sides, **75 are byte-identical. Two have
drifted — and because Resources wins, the drift is silently invisible at runtime.**

| File | Resources (WINS) | StreamingAssets | Effect |
|---|---|---|---|
| `weapons.json` | **96** entries | **431** entries | Resources ids are a clean subset (`shared=96, only-R=0, only-S=335`). **335 authored weapons — 78% of the set — never load.** Every `blink_*` weapon family is unreachable. |
| `armor.json` | **24** entries | **30** entries | **The copies have FORKED, not merely lagged:** `shared=9, only-R=15, only-S=21`. 21 authored armors never load; 15 live only in the WebGL copy and are absent from the "source" copy. |

Neither drift produces any warning — `CanonicalJson.Read` succeeds, so no `FlowTrace.Warn` fires.
Verified by byte-compare of all 75 shared files plus id-set arithmetic on the two outliers.

### Files that exist on ONE side only

| File | Present in | Note |
|---|---|---|
| `ad-creatives.json` | Resources only | no StreamingAssets twin |
| `ad-placements.json` | Resources only | no StreamingAssets twin |
| `widget-params.json` | Resources only | no StreamingAssets twin |
| `skr_staking.json` | StreamingAssets only | no Resources twin → **desktop-only; cannot load in WebGL** |
| `skr_store.json` | StreamingAssets only | no Resources twin → **desktop-only; cannot load in WebGL** |
| `battle_monthly_packs.sample.json` | StreamingAssets only | `.sample` — see §7 |

---

## 3. Building & placement family — the four-catalog tangle

Four separate JSON files describe "a building", each with its own id vocabulary and its own idea of
"type". They overlap only partially.

### 3.1 `structures-catalog.json` — the placeable catalog (29 entries, v15)

**Loader** `CatalogBootstrap.LoadFromJson` — `Assets/_Modules/Village/Catalog/CatalogBootstrap.cs:52` (path const), `:118` (deserialize)
**DTO** `CatalogEntry` — `Assets/_Modules/Core/Catalog/CatalogEntry.cs:29`
**Registry** `CatalogRegistry` — `Assets/_Modules/Core/Catalog/CatalogRegistry.cs:12`; indexes `_byId` (`:14`) **and** `_byType` (`:16`) — note it does **not** index `behaviorId`
**Consumers** `StructureFactory` (`Assets/_Modules/Village/Catalog/StructureFactory.cs`), `BuildModeController`, `BuildPaletteVM`, `StructureCardVM`, `BuildMenuVM`

| Field | Type / owning class | Consumers | Notes |
|---|---|---|---|
| `id` | `string` — `CatalogEntry.cs:31` | everything; `CatalogRegistry._byId` | **The identity + persistence key.** The only catalog field that reaches the save file. |
| `displayName` | `string` — `CatalogEntry.cs:32` | palette cards | |
| `type` | `CatalogType` enum — `CatalogEntry.cs:33` / `Assets/_Modules/Core/Catalog/CatalogType.cs:8` | `CatalogRegistry.OfType`, `BuildCategoryRegistry`, `StructureCardVM` | **Axis A** — see §6 |
| `kind` | `EntryKind` enum — `CatalogEntry.cs:34` / `CatalogType.cs:27` | `Assets/Editor/LayoutValidator.cs:236` only | **Constant.** All 29 rows = `Cell`. No runtime reader. |
| `visualPrefabPath` | `string` — `CatalogEntry.cs:36-46` | `StructureFactory` | 28/29 rows |
| `visualTexturePath` | `string` — `CatalogEntry.cs:49` | `StructureFactory` | 2/29 rows |
| `repo` | `RepoProps` — `CatalogEntry.cs:53` | see 3.2 | behaviour block |
| `composite` | `CellPlacement[]` — `CatalogEntry.cs:56` | `StructureFactory.cs:553-591` | **Never populated** — 0/29 rows. Dead path. |
| `orientation` | `OrientationFix` — `CatalogEntry.cs:66` | `StructureFactory` | 28/29 rows; only `manual:true` is applied |
| `canHitAir` *(top level)* | **none — no such field on `CatalogEntry`** | **nothing** | ⚠ **Dead + lossy.** 4 rows carry it at the top level where only `repo.canHitAir` (`RepoProps.cs:307`) exists. Silently dropped by `MissingMemberHandling.Ignore`. `arcane-tower` has top-level `canHitAir:true` and **no** `repo.canHitAir` — the value is lost entirely and reads as the `false` default. |
| `_note` / `_heightNote` / `_bug22` / `description` | — | — | underscore-prefixed authoring comments; inert by convention |

### 3.2 `repo` block → `RepoProps` (`Assets/_Modules/Core/Catalog/RepoProps.cs`)

| Field | Type / owning class | Values occurring in data (n=29) | Notes |
|---|---|---|---|
| `behaviorId` | **plain `string`** — `RepoProps.cs:104` | `GameplayBuilding` 13 · `DefenseTower` 4 · `ResourceCollector` 3 · `WallSegment` 2 · `Gate` 1 · `CrystalMine` 1 · `HealingFountain` 1 · `HealerTower` 1 · `ArcaneTower` 1 · **absent 2** | **Axis B.** **Not an enum.** Dispatched by a string `switch` at `StructureFactory.cs:741`; unknown value → `FlowTrace.Warn` + no behaviour (`:933`). The 9 data values exactly match the 9 `case` labels — but nothing in the type system enforces that. |
| `navSurface` | `NavSurfaceKind` — `RepoProps.cs:36` / `CatalogType.cs:30` | `Blocker` 27 · `None` 2 | enum member `Walkable` **never used in data** |
| `element` | `DamageElement` — `RepoProps.cs:313` / `Assets/_Modules/Core/Combat/IDamageable.cs:132` | `Aether` 1 · absent 28 | enum members `Flame`, `Ice` **never used in data** |
| `storageResource` | `string` — `RepoProps.cs:170` | `wood` 1 · `iron` 1 · `food` 1 · absent 26 | free-form string, no enum |
| `projectileStyle` | `string` — `RepoProps.cs:324` | `bolt` 3 · `spell` 1 · absent 25 | free-form string, no enum; `pellet` is the documented default but never authored |
| `placement` | `PlacementRules` — `RepoProps.cs:221` / `Assets/_Modules/Core/Catalog/PlacementRules.cs` | see below | |

`placement` → `PlacementRules`:

| Field | Owning class | Values in data | Notes |
|---|---|---|---|
| `mustSitOn` | `PlacementSurface` — `PlacementRules.cs:14` / `CatalogType.cs:33` | `Ground` 27 · `WallWalk` 1 · `AnyTerrain` 1 | enum member `Floor` **never used in data**. Read at `BuildModeController.cs:1523`. |
| `footprint` | `float` — `PlacementRules.cs:20` | 29/29 | **Largely vestigial** — grid cells come from `StructureFactory.MeasureUprightFootprintMetres`; `placement.footprint` is only the prefab-missing fallback (`structures-catalog.json` `_heightCadence` note). |
| `noOverlap` | `bool` — `PlacementRules.cs:17` | 29/29 | ⚠ **authored 29× but read by NOTHING at runtime** — only written, in `CatalogBootstrap` fallback rows (`:219`, `:276`, `:333`) |
| `checkAffordable` | `bool` — `PlacementRules.cs:29` | 29/29 | ⚠ read **only by a regression** (`Assets/Editor/Regression/BuildEconomyRegression.cs:241`), never by runtime placement — setting it `false` does **not** bypass the cost gate |
| `minDistanceFromGate` | `float` — `PlacementRules.cs:23` | 1/29 | |
| `requiresSupport` | `bool` — `PlacementRules.cs:26` | **never supplied** | ⚠ declared, never authored, **read by nothing** |
| `ownedGate` | `string` — `PlacementRules.cs:32` | **never supplied** | ⚠ declared, never authored, **read by nothing** |

### 3.3 `buildings.json` — a SECOND building catalog (10 entries)

**Loader/DTO** `BuildingCatalog` / `BuildingDef` — `Assets/_Modules/Village/Buildings/BuildingCatalog.cs:155` / `:54`
**Consumers** `VendorRegistry`, `InteractableSign`, `CastleVendorNpcInjector`, `AutoPilotDriver`

| Field | Type / owning class | Values in data (n=10) | Notes |
|---|---|---|---|
| `type` | `string` → `BuildingType` via `BuildingDef.ResolvedType` — `BuildingCatalog.cs:63` / `:113` | `Workshop` 3 · `CrystalMine`/`Farm`/`PetHouse`/`ArcaneTower`/`Lumbermill`/`Forge`/`Armorer` 1 each | ⚠ **Same field NAME as `structures-catalog.type`, bound to a DIFFERENT enum.** This is the single biggest source of confusion — see §6. Unknown value → warn + fallback `CrystalMine` (`:120`). |
| `footprint` | `string` → `BuildingFootprint` via `ResolvedFootprint` — `BuildingCatalog.cs:88` / `:128` / enum `:38` | `medium` 8 · `small` 1 · `large` 1 | |
| `isShoppable` | **`bool`** — `BuildingCatalog.cs:100` | `true` **3** (forge, market, jeweler) · absent 7 | **This is the shop axis. There is no shop/store ENUM.** See §6. |
| `isUpgradable` | `bool` — `BuildingCatalog.cs:96` | `true` 6 · absent 4 | not mutually exclusive with `isShoppable` |
| `upgradeType` | `string` — `BuildingCatalog.cs:106` | `resource` 3 · `gear` 2 · `spells` 1 · absent 4 | free-form string, no enum |
| `hp` / `maxHp` / `crystalCost` / `model` / `buildMenuOrder` / `descriptionKey` / `displayName` | `BuildingCatalog.cs:57-91` | 10/10 | |

### 3.4 `build-categories.json` — build verb → palette contents (5 rows, v2)

**Loader** `BuildCategoryRegistry` — `Assets/_Modules/Village/Catalog/BuildCategoryRegistry.cs:59` (path const)
**DTO** `BuildCategory` — `BuildCategoryRegistry.cs:41`

| `buildType` (`BuildType`, `CatalogType.cs:24`) | `catalogTypes` (`CatalogType`) | `lockedIds` |
|---|---|---|
| `Town` | `Resource`, `Collector` | jeweler, mine_crystal, mill, lumbermill, armorer, collector_forge |
| `Defense` | `Tower`, `Gate` | tower_siege_tower, tower_catapult, gate_stone |
| `Walls` | `Wall` | — |
| `Collector` | `Collector` | — (legacy verb) |
| `Support` | `Support` | healing_caravan (legacy verb) |

⚠ **`CatalogType.Decoration` appears in NO build category.** The 2 `Decoration` rows are unreachable from
every palette — see §7.

### 3.5 `building-tiers.json` — tier ladder + research perks (6 buildings, 26 tiers, 17 perks, v6)

**Loader** `BuildingTierCatalog` — `Assets/_Modules/Core/State/BuildingTierCatalog.cs:98`; keyed by raw id string (`Find`, `:107`)
**Consumers** `ModifierService` (`Assets/_Modules/Core/State/ModifierService.cs:99,137,153`), `BuildingInteractable`, `BuildingPerkService`, `DevPanelController`

Covers: `arcane-tower`, `armorer`, `barracks`, `forge`, `lumbermill`, `farm`.
Tier fields (26/26): `tier`, `name`, `effect`, `costWood`, `costFood`, `costCrystal`, `requiresVillageTier`,
`structureHpBonusPct`, `modifiers`, `perks` (16/26).
Perk fields (17/17): `id`, `name`, `effect`, `goldCost`, `iconId`, `isSignature`, `modifiers`.

### 3.6 ⚠ The id vocabularies do not line up

Four files, four id sets:

| id | structures-catalog | buildings.json | building-tiers |
|---|:--:|:--:|:--:|
| `arcane-tower`, `armorer`, `forge`, `lumbermill` | ✓ | ✓ | ✓ |
| `jeweler`, `market`, `pet-house`, `workshop` | ✓ | ✓ | — |
| `barracks` | ✓ | — | ✓ |
| **`farm`** | **—** | ✓ | ✓ |
| **`crystal-mine`** | **—** | ✓ | — |
| `mill`, `mine_crystal` | ✓ | — | — |
| `lumberyard`, `foundry`, `silo`, `collector_*`, `tower_*`, `wall_*`, `gate_stone`, `healing_caravan`, `deco_torch`, `repair_default` | ✓ | — | — |

- **`farm` and `crystal-mine` have no placeable row.** Their placeable counterparts exist under
  *different ids* — `mill` / `collector_farm`, and `mine_crystal`.
- A **fifth** id vocabulary bridges them: `BuildingInteractable.StructureHookIdFor`
  (`Assets/_Modules/Village/Buildings/BuildingInteractable.cs:350`), a substring-match alias layer,
  with a `BuildingType` switch as its fallback (`:369-377`).
  - `mill` matches no substring → falls through to `BuildingType.Farm` → returns `"farm"`, so the farm
    tier ladder **does** apply. It works, via a two-hop indirection that nothing documents.
  - `lumberyard`, `foundry`, `silo` match no substring **and** map to `BuildingType.CrystalMine`
    (`StructureFactory.cs:967-969`), which has **no case in the hook switch** → `StructureHookIdFor`
    returns **null**. These three storage containers therefore resolve no tier ladder and no structure
    dialogue. Flagged as an observation to verify in play, not asserted as a defect.
  - `jeweler` matches no substring → `BuildingType.Workshop` → returns `"workshop"`, i.e. the jeweler
    shares the **workshop** hook. `StructureFactory.cs:962` documents this as intentional (the Yarn route
    resolves by name instead).

---

## 4. Combat / entity / world-sim family (17 files)

All 17 verified byte-identical across the dual copies.

### 4.0 ☠ Three files in this group have NO LOADER

| File | Evidence |
|---|---|
| `enemy-roles.json` | zero `CanonicalJson.Read` / `File.ReadAllText` / `Resources.Load` anywhere in `Assets/`. Only mentions: a doc comment (`Assets/_Modules/Core/Enemies/EnemyTaxonomy.cs:79`) and the dual-copy mirror pin-list (`Assets/Editor/Regression/DataWebRegression.cs:151`). Its 9 role tokens (`defender, attacker, dps_ranged, dps_caster, healer, cc, swarm, trap, boss_tier`) + 25 creature rows are entirely unread. |
| `towers.json` | no loader. `BuildModeController.cs:2321,2350` *comment* that they use "its towers.json tier (range/damage)" — the values are **inlined in code**, not read. Corroborated by `docs/qa/WORK_ORDER_771_raid_system.md:103` ("towers.json … is unwired data"). Also: zone ids are `ice/fire/aether` vs `DamageElement` members `Ice/Flame/Aether` — `fire` ≠ `Flame`, and nothing bridges them. |
| `heart.json` | no loader. Only mention is the mirror pin-list (`DataWebRegression.cs:155`). ⚠ **Live code contradicts it:** `HeartController.cs:97` hardcodes `_hp = 100f` on a 0–100 `Range`, versus the file's `maxHp: 160`. Its 3 phase ids (`intact/wounded/critical`) also disagree with the 5-state vocabulary in `docs/v2-unity-port-spec.md:183` (`serene/vigilant/warning/danger/critical`) — three sources, none implemented. |

The dual-copy mirror gate (`DataWebRegression.cs:148-157`) actively maintains all three files, so the drift check keeps green on catalogs nothing reads.

### 4.1 Per-file registry

| File | Loader (`file.cs:line`) | DTO | Primary consumers |
|---|---|---|---|
| `abilities.json` | `AbilityCatalog.LoadCatalog` `…/Village/Hero/AbilityCatalog.cs:317` | `AbilityCatalogData` `:192`; rows `AbilityDef` `:69` | `HeroAbilities`, `HeroAbilitiesHudBridge`, `HeroSkillTreeVM`, `BattleArena` |
| `enemies.json` | **two loaders** — `WaveDataLoader.LoadEnemiesAsync` `…/Village/Waves/WaveData.cs:497` **and** `OutpostEnemyGroupSpawner.cs:454` | `EnemyCatalog` `WaveData.cs:191`; rows `EnemyDef` `:56` | `WaveManager`, `SmartEnemySpawner`, `EnemyFactory`, `Enemy`, `WildlandsRoster` |
| `waves.json` | `WaveDataLoader.LoadAsync` `WaveData.cs:466` | `WaveSchedule` `:400`; `WaveDef` `:314`, `WaveBatch` `:267` | `WaveManager`, `WaveCompositionBuilder`, `OutpostEnemyGroupSpawner` |
| `spawn-areas.json` | `SpawnAreaTable.EnsureLoaded` `…/Core/World/SpawnAreaTable.cs:265` | `SpawnAreaFile` `:87` | `OverworldEncounterSpawner`, `RegionMobSpawner` |
| `troops.json` | `TroopCatalog` `…/Village/Troops/TroopCatalog.cs:84` | `TroopCatalogData` `:26`; rows `TroopDef` `TroopDef.cs:29` | `TroopFactory`, `TroopController`, `BarracksService`, `TroopTrainingVM` |
| `troop-upgrades.json` | `TroopUpgradeCatalog` `…/Troops/TroopStatResolver.cs:186` | `TroopUpgradeCatalogData` `…/Troops/Data/BarracksData.cs:139` | `TroopStatResolver`, `BarracksProgression` (`BarracksPanelVM` was a consumer until it was DELETED 2026-09-06, WO-1430) |
| `barracks.json` | `BarracksCatalog` `TroopStatResolver.cs:264` | `BarracksCatalogData` `BarracksData.cs:130`; rows `BarracksDef` `:50` | `BarracksProgression`, `BarracksService` (`BarracksPanelVM` DELETED 2026-09-06, WO-1430) |
| `garrison-recipes.json` | `GarrisonRecipeCatalog` `…/Core/Data/GarrisonRecipeCatalog.cs:61` | `GarrisonRecipeFile` `…/Core/Data/GarrisonRecipe.cs:120` | ⚠ **editor-only** — `GarrisonSceneBuilder`, `EnemyStrongholdBuilder`, `CoreCatalogRegression` |
| `motion-castings.json` | runtime `ActionBundleCatalog.EnsureLoaded` `…/Village/Vfx/ActionBundleCatalog.cs:179`; editor `Assets/Editor/MotionCastings.cs:285` | hand-walked `JObject` `:196`; rows `ActionBundleRow` `:46` | `ActionBundlePlayer`, `HeroAbilities`, `KnightPackageControllerBuilder` |
| `weaponskill-animations.json` | ⚠ `KnightPackageControllerBuilder.cs:511` — `File.ReadAllText`, **bypasses `CanonicalJson`**, editor-only | `WsFile` `:542` | that builder only |
| `damage-states.json` | `DamageStatesCatalog.EnsureLoaded` `…/Village/Vfx/StructureDamageVisuals.cs:205` | `FileDef` `:134` (⚠ no `[JsonProperty]` — relies on default member match) | `StructureDamageVisuals`, `RepairAvailabilityProbe` |
| `difficulty-profile.json` | `DifficultyProfileCatalog` `…/Core/Difficulty/DifficultyProfileCatalog.cs:69` | `DifficultyProfile` `DifficultyProfile.cs:49` | `DynamicDifficulty` (sole) |
| `tower-perks.json` | `TowerPerkTable.Reload` `…/Village/Buildings/Tower.cs:1343` | `TowerPerkTable.File` `:1314` | `Tower`, `TowerCombat`, `BuildMenuVM` |
| `walls.json` | `WallDefense.EnsureLoaded` `…/Village/Walls/WallTierData.cs:148` | `WallsFileJson` `:130` (declares **3 of 8** authored keys) | `Enemy.cs:1675,1686` (sole runtime) |
| `enemy-roles.json`, `towers.json`, `heart.json` | **none** | **none** | **none** |

### 4.2 Classification fields — data values vs the enum

| File · field | Values occurring (n) | Enum | Divergence |
|---|---|---|---|
| `abilities.json` · `effect` | 17 distinct / 39 rows: `strike` 8, `aoe` 4, `cleave` 4, `heal` 4, `blink` 3, `meteor` 3, `snare` 3, +10 singletons | `AbilityEffect` `AbilityCatalog.cs:47` = `Strike, Snare, Aoe, Cleave, Heal, Meteor` | ⚠ **11 JSON values have no enum member** (`blink, dash, dot, drainshot, gracebuff, healOverTime, invuln, knockback, manaweave, shield, taunt`) — **17 rows / 44% of the catalog**. DTO stores it as `string` (`:88`) so it parses; they fall through `AbilityAudioBridge.VolumeFor` (`AbilityAudioBridge.cs:62-71`) to the default. |
| `abilities.json` · `slot` | `w` 18, `e` 9, `r` 8, `q` 4 | `AbilitySlot {Q,W,E,R}` `AbilityCatalog.cs:31` | exact match ✅ — but ⚠ a **second unrelated `AbilitySlot`** exists at `…/BattleATB/Engine/Types.cs:27` (name collision) |
| `enemies.json` · `role` | `caster` 5, `elite` 5, `brute` 4, `grunt` 3, `skirmisher` 2 | `EnemyRole` `…/Village/Waves/WaveEnemyGroup.cs:43` = `Tank, Healer, DPS, Ranged, MiniBoss` | 🚨 **ZERO token overlap.** Bridged by a lossy switch `EnemyDef.RoleKind` `WaveData.cs:173` — `brute→Tank, caster→Healer, skirmisher→Ranged, elite→MiniBoss, else→DPS`. **`caster→Healer` means every ranged caster is tagged a healer.** A *second, disagreeing* map keyed on id lives at `OutpostEnemyGroupSpawner.cs:415-433` — `troll-mage` is `role:"caster"` in JSON but hard-cased to `DPS` at `:429`. **Two sources disagree — both reported.** |
| `enemies.json` · `family` | `hollow` 10, `troll` 5, `orc` 4 | none — raw string (`Enemy.cs:2859`). Nearest is `EnemyFaction` `…/Core/Enemies/EnemyTaxonomy.cs:29` = `HollowOnes, Wildlands, Boss` | ⚠ different vocabulary; nothing maps one onto the other. Also the file's own `_schemaNotes.family` documents only `hollow`/`orc` — **`troll` (5 rows) undocumented by its own schema**. |
| `enemies.json` · `spawn[]` | `wave` 10, `camp` 7, `roam` 5, `dungeon` 2, `world` 2 | none | `_schemaNotes.spawn` documents only `wave/roam/camp` — **`dungeon`, `world` undocumented** |
| `enemies.json` · `ai` | `walker` 9, `charger` 6, `skirmisher` 4 | `EnemyAiKind` `WaveData.cs:41` | exact match ✅ |
| `troop-upgrades.json` · `statusKind` | `Bleed, Burn, Haste, Mark, Regen, Shield, Slow, Stun` (24 unlocks) | **`StatusKind`** `BarracksData.cs:102` → `…/BattleATB/Engine/Types.cs:33` | ✅ **the only strongly-typed classification field in this group** — deserializes into the enum itself, so a bad value throws. Unused members: `Poison`, `Freeze`. |
| `troops.json` · `role` | `melee` 5, `ranged` 2, `siege` 1 | none | only `"siege"` is branched on (`TroopController.cs:306`); **`ranged` is a token no code distinguishes** |
| `troops.json` · `element` | literal `"None"` × 8 | `DamageElement` `IDamageable.cs:132` | ⚠ **3 of 4 members (`Aether`, `Flame`, `Ice`) never used by any troop** — the elemental lane is data-inert. A third unrelated `ElementType` enum exists at `…/BattleATB/Engine/Types.cs:30`. |
| `walls.json` · `tiers[].level` | `0, 1, 2, 3` | `WallTier` `WallTierData.cs:30` = `Wood=1, Iron=2, ReinforcedSteel=3` | 🚨 **misaligned on every row.** JSON has a level **`0`** row ("Wooden Fence") with no enum member; names disagree at every index (JSON `1="Stone Wall"` vs `Wood`; `2="Steel Wall"` vs `Iron`; `3="Spiked Steel Wall"` vs `ReinforcedSteel`). `WallTierData.cs:60-101` serves display name + upgrade cost from a **hardcoded C# ladder** independent of the JSON — two ladders live simultaneously and disagree. Both reported. |
| `garrison-recipes.json` · `kind` | `garrison` 3, `dungeon` 1, `stronghold` 1 | none | DTO doc (`GarrisonRecipe.cs:38`) names only `garrison`/`dungeon`; `IsDungeon` (`:85`) tests `=="dungeon"` only, so **the `stronghold` recipe silently takes the open-air branch** |
| `garrison-recipes.json` · `size` / `theme` / `element` | `large` 3, `medium` 2 / `ruined` 2, `troll`+`hill`+`frost` 1 / `ice` 1, absent 4 | none | documented-but-unused: `small`, `ember`, `fire`, `water`. `lighting: stronghold` is outside the documented theme tokens. |
| `motion-castings.json` · `vocabulary` | 35 keywords declared; **only 17 ever authored** | `ActionKeywords` — a **`const string` class, not an enum** `…/Core/Combat/ActionKeywords.cs:28+` | ⚠ **18 declared-but-never-authored** (`idle, combatIdle, injured*, castChannel, parry, dodge, knockdown, gettingUp, death1..5, taunt, victory, windup`). The runtime loader **never reads the `vocabulary` block at all** (`ActionBundleCatalog.cs:196` descends only into `targets`) — the closed vocabulary is enforced only at compile time + in an editor test. |
| `motion-castings.json` · `source` | `motion-caster` 14, `owner-pick` 5, `cli-tank-sway-fix` 4, `cli-hero-sway-fix` 2 | none | DTO comment (`ActionBundleCatalog.cs:71`) declares `motion-caster \| migrated-weaponskill \| auto` — **3 JSON values outside the documented set; 2 documented values never occur** |
| `weaponskill-animations.json` · `class` / `trigger` / `combo` | `knight` 17, `ranger`/`mage`/`cleric` 4 each / `Cast` 19, `Attack` 9, `Block` 1 / `-1` ×26 | none | only `class=="knight"` **and** `trigger=="Attack"` **and** `combo∈{1,2}` are read (`:517-518`) → **exactly 2 of 29 rows can affect the built controller** |
| `damage-states.json` · `perType` keys | `gate` 1, `heart` 1 | none | arbitrary lowercase strings, case-insensitive lookup (`:198`); nothing constrains or validates the key space |
| `spawn-areas.json` · `composition` | slots `tank`/`dps`/`healer` as three named `int` fields (`SpawnAreaTable.cs:67-71`) | maps onto `EnemyRole` per `:24-25` | ⚠ `EnemyRole.Ranged` and `.MiniBoss` have **no representable composition slot** |
| `difficulty-profile.json` | — | — | ✅ **no classification field at all**; 38 scalar dials, JSON keys 1:1 with DTO, zero drift — the cleanest file in the repo |

### 4.3 `waves.json` — the `enemies[]` batches are inert *and now physically absent*

Stronger than the standing canon note. Verified at source:

- The file declares **0 batches across all 20 waves** — the `enemies` key **no longer exists**. Key union of `waves[]` = `_comment, apexBoss, boss, bossHp, countdownSeconds, expectedCombatSeconds, name, waveId`.
- `_schemaNotes._RETIRED_batchFields` records the strip (2026-07-30, WO-783 D1) and says *"Do NOT re-add an 'enemies' array here."*
- Code side: `WaveManager._smartComposition = true` (`WaveManager.cs:197`), branch `:1534`, data-rot guard `WarnAuthoredBatchesDiscarded` `:1647-1658`. Both hubs serialize it on — `Assets/Scenes/MainCastle_Hall.unity:1619`, `Assets/Scenes/Main_Castle_Overworld.unity:3552`.
- **Consequence:** `WaveBatch` (`WaveData.cs:267`) is a **fully dead DTO** — `type/count/spawnPoint/delay/interval` are never supplied. Only `countdownSeconds`, `boss`, `bossHp`, `apexBoss` still take effect.

### 4.4 Dead data in this group

| Where | What | Note |
|---|---|---|
| `garrison-recipes.json` | 🚨 **4 authored keys the DTO does not declare** — `boss`, `destruction{…}`, `layout{courtyard,chokepoint,keep}`, `traps{…}` on `recipes[4]` (`village2_stronghold`) | authored dungeon geometry, silently discarded on every load |
| `walls.json` | **6 of 8 per-tier keys dead** — `name, emoji, effect, targetHeight, meshStraight, meshGate` + top-level `halfSize`, `notes` | the `meshStraight`/`meshGate` glTF paths point at KayKit assets nothing loads |
| `weaponskill-animations.json` | JSON keys with no DTO field: `abilityId, clipExists, fallbackClip, _note, fallbackByTrigger, _notes`; DTO field never read: `slot` (`:547`) | only `class/trigger/combo/clip` reach any logic |
| `tower-perks.json` | **`signatureAbility` is dead** — `Row.SignatureAbility` (`Tower.cs:1311`) has zero readers | the `overcharge` perk authored on tiers 3–4 grants nothing. Also: the tier-label ladder is duplicated verbatim as a hardcoded fallback (`Tower.cs:1330-1334`) — two sources of truth. |
| `enemies.json` | `flavor` (all 19 rows) → `EnemyDef.Flavor` `:118`, zero readers | dead |
| `enemies.json` | DTO declares, JSON never supplies: `movement` (`:104`, default `"ground"` → **`IsFlying`/`CombatLayer.Flying` at `:143,:148` is structurally unreachable from data**), `aggroRadius` `:123`, `groupStaggerDelay` `:125`, `glimmerReward` `:132` | |
| `troops.json` | `shortDescription` (all 8 rows) → `TroopDef.ShortDescription` `:126`, zero readers | dead |
| `damage-states.json` | DTO declares 4 per-type overrides the JSON never supplies — `smolder, fire, criticalBeacon, barOffset` (`:126-129`) | the whole per-type threshold-override mechanism is data-inert |
| `motion-castings.json` | `vfxNote` (4 rows) — no `ActionBundleRow` property | dropped |
| `spawn-areas.json` | 🚨 **cross-file id namespace split** — the 7 unit ids in `families[].tank/dps/healer` (`orc-tank, orc-warrior, orc-mage, skeleton-tank, skeleton-warrior, skeleton-mage, troll-berserker`) **exist in none of `enemies.json`**; they resolve through a separate roster (`EnemyFactory.cs:538`, `AtbCombatantSwapper.cs:36,553`, `BattleATB/Engine/Defs.cs:500`) | two enemy id vocabularies |

**Cross-cutting:** exactly **one** classification field in this group is strongly typed (`statusKind`). Every other one is a raw `string` on the DTO with a hand-written `switch` downstream — which is precisely why the divergences above never surface at load time. 20+ authored JSON keys across five files have no DTO property and are dropped with no warning.



---

## 5. Items / gear / economy / monetization family (20 files + 3 StreamingAssets-only)

⚠ **All entry counts below are of the `Resources` copy — the one that wins.** For `weapons.json` and
`armor.json` that is *not* the whole authored set; see [§2](#2-dual-copy-drift--️-the-highest-impact-finding-in-this-doc).

### 5.1 Per-file registry

| File | Loader (`file.cs:line`) | DTO | Primary consumers |
|---|---|---|---|
| `weapons.json` | `GearCatalog.LoadWeapons` `…/Village/Hero/GearCatalog.cs:625` (path `:243`, read `:651`) | `WeaponCatalogData` `:233` → `WeaponDef` `:41` | `GearLoadout`, `GearStatResolver`, `EquipmentController`, `VendorStockResolver`, `PartyShopVM` |
| `armor.json` | `GearCatalog.LoadArmor` `GearCatalog.cs:631` | `ArmorCatalogData` `:234` → `ArmorDef` `:175` | `GearLoadout`, `HeroArmorVisual`, `ArmorVfxMap`, `EquipVM` |
| `accessories.json` | `GearCatalog.LoadAccessories` `GearCatalog.cs:638` | `AccessoryCatalogData` `…/Hero/AccessoryDef.cs:98` → `AccessoryDef` `:27` | `VendorStockResolver` (jeweler shelf), `JewelerVM`, `GearAppraisal` |
| `gear-levels.json` | `GearLevelCatalog` `…/Hero/GearProgression.cs:96` | `GearLevelCatalogData` `:58` | `GearStatResolver`, `GearProgression`, `EquipVM`, `InventoryVM` |
| `gear-recipes.json` | `GearCraftingRecipeCatalog` `…/Crafting/GearCraftingRecipeCatalog.cs:125` | `GearRecipeData` `:93` → `GearRecipeDef` `:62` | `GearCraftingService`, `WorkshopCraftVM` |
| `jeweler-recipes.json` | `JewelerRecipeCatalog` `…/Crafting/JewelerRecipeCatalog.cs:123` | `JewelerRecipeData` `:92` | `JewelerCraftingService`, `JewelerVM` |
| `consumables.json` | `ConsumableCatalog` `…/Items/ConsumableCatalog.cs:146` | `ConsumableData` → `ConsumableDef` `:48` | `ConsumableUseService`, `ItemInventory`, `CraftingVM`, `VendorStockResolver` |
| `consumable-recipes.json` | `ConsumableCraftingCatalog` `…/Items/ConsumableCraftingCatalog.cs:89` | `ConsumableRecipeData` `:56` | `ItemCraftingService`, `CraftingVM` |
| `crafting-recipes.json` | ⚠ **two loaders** — `CraftingRecipeCatalog` `…/Crafting/CraftingRecipeCatalog.cs:122` **and** `CraftingDataLoader` `…/Dungeons/Crafting/CraftingData.cs:250` | ⚠ **two DTOs** — `CraftingRecipeData` `:62` (recipes only) vs `CraftingDataSet` `CraftingData.cs:146` (**also** `ingredientPlacements` `:158`, `pedestal` `:161`) | `WorkshopCraftVM` / `DungeonController`, `CraftableShopProvider`, `CraftingPedestal` |
| `materials.json` | `MaterialCatalog` `…/Items/MaterialCatalog.cs:98` | `MaterialData` `:54` → `MaterialDef` `:39` | `ItemIdentity`, `CraftingVM`, `JewelerVM`, `VendorStockResolver` |
| `loot-tables.json` | `LootTableCatalog` `…/Items/LootTableCatalog.cs:161` | `LootTableData` `:76` → `LootTableDef` `:59` | `ItemDropSystem`, `ItemDropWatcher`, `DungeonLootGrant` |
| `cosmetics.json` | `CosmeticCatalog` `…/Cosmetics/CosmeticCatalog.cs:138` | `CosmeticCatalogData` `:75` → `CosmeticDef` `:29` | `CosmeticApplier`, `GlimmerCurrencyService`, `PackStoreVM` |
| `storage-caps.json` | `StorageCapsCatalog` `…/Core/Economy/StorageCapsCatalog.cs:113` | `StorageCapsData` `:34` | `TownBankCapacity`, `BankOverflowToastPresenter` |
| `vendors.json` | `VendorRegistry` `…/Hero/VendorRegistry.cs:146` | `VendorData` `:88` → `VendorDef` `:41` | `VendorStockResolver`, `VendorStockContract`, `PartyShopVM` |
| `packs.json` | `PackCatalog` `…/Wallet/PackCatalog.cs:271` | `PackCatalogData` `:141` → `PackDef` `:88` | `PackStore`, `PackStoreVM`, `CryptoPaymentManager` |
| `wallets.json` | `WalletRegistry` `…/Wallet/WalletRegistry.cs:152` | `WalletRegistryData` `:68` → `WalletEntry` `:32` | `WalletService`, `SolanaWalletProvider` |
| `stake-rewards.json` | `StakeRewardsResolver` `…/Core/Platform/StakeRewardsResolver.cs:228` | ⚠ **no DTO** — hand-walked `JObject` `:242` | `StakeRewardsVM`, `StakeRewardsPanel`, `SkrShowcasePanel` |
| `skin.json` | `CurrencySkinResolver` `…/Core/Platform/CurrencySkinResolver.cs:305` | ⚠ **no DTO** — `JObject.Parse` `:311` | `WalletService`, `WalletSkinBootstrap`, `PiSignInController` |
| `ad-placements.json` | ⚠ **no runtime loader** — editor-only, `Assets/Editor/Regression/AdPlacementCovenantRegression.cs:41` (raw `File`, not `CanonicalJson`) | none | none at runtime |
| `ad-creatives.json` | ☠ **none, anywhere** | none | none |

### 5.2 Classification fields

| File · field | Values occurring | Enum | Divergence |
|---|---|---|---|
| `weapons.json` · `job` (n=96) | `knight` 45, `ranger` 22, `any` 19, `mage` 8, **`cleric` 2** | `HeroClass {Mage,Knight,Ranger}` `…/BattleATB/Engine/Types.cs:60` | 🚨 **`cleric` has no `HeroClass` member.** Rows `cleric_starter`, `aegis_hallowed_censer`. The gate is a string compare (`GearCatalog.JobMatches`), so they are catalog-live but **no playable class can ever match them → permanently unequippable stock.** |
| `weapons.json` · `rarity` | `common` 61, `uncommon` 13, `rare` 11, `epic` 7, `legendary` 4 | `ElarionUiKit.Rarity` `…/Core/UI/ElarionUiKit.cs:1774`; re-parsed to `GearTier` `…/Crafting/GearTier.cs:85` | ⚠ `GearTierTable.Parse` (`GearTier.cs:94-95`) **collapses 5 rarities into 4 tiers — `epic` and `legendary` both → `Legendary`.** Epic and legendary gear are indistinguishable to every appraisal/craft path. |
| `weapons.json` · `category` | `sword` 21, `shield` 20, `axe` 16, `bow` 15, `staff` 8, `arrow` 4, `dagger`/`hammer` 1; **absent on 10** | none | only `"shield"` is behaviourally read (`GearCatalog.cs:167`). The 10 rows missing it include **every hand-authored starter** (`knight_starter`, `ranger_starter`, `cleric_starter`, all 3 `aegis_*`) — those are `IsOffHandItem == false` by accident, not authorship. |
| `weapons.json` · `hand` / `damageType` / `element` / `ammoEffect` / `loadVia` / `setId` | `1h` 46 / `2h` 35 · `melee` 58, `ranged` 19, `magic` 4 · `fire` 1 · `burn`/`poison`/`slow` 1 each · `addressable` 65 · `aegis` 4 | none for any | `ammoEffect` values shadow `StatusKind` (`Types.cs:33`) without being bound to it |
| `armor.json` · `job` (n=24) | `any` 9, `knight`/`ranger`/`mage` 5 each | `HeroClass` | ⚠ **no `cleric` here — asymmetric with `weapons.json`** |
| `armor.json` · `weight` | `light` 13, `heavy` 9, **absent 2** | none | resolved by `GearCatalog.ClassWeight`/`ArmorFitsClass` |
| `accessories.json` · `slot` | `ring` 5, `amulet` 5 | none | ⚠ the nearest enum `EquipmentSlot {MainHand, Armor}` (`…/Hero/HeroEquipment.cs:20`) **has neither Ring nor Amulet**. Resolved by string compare (`AccessoryDef.cs:88,92`). Also `category` is an **exact duplicate of `slot`** — one of the two is redundant. |
| `consumables.json` · `kind` / `effect` (n=17) | `potion` 13, `food` 2, `tent` 2 · `heal` 9, `buff` 5, `rest` 2, `mana` 1 | `ConsumableKind` `ConsumableCatalog.cs:42` · `ConsumableEffect` `:45` | ⚠ `ConsumableKind.Unknown` and `ConsumableEffect.None` never occur — unreachable-from-data parse sentinels. A typo degrades to them **with no warn**. Note the enum is a *computed property*; the JSON string is kept in `KindRaw`/`EffectRaw` (`:52-53`). |
| `gear-recipes.json` · `tier` (n=8) | `Legendary` 5, `Master` 2, `Fine` 1 | `GearTier {Common=0,Fine,Master,Legendary}` `GearTier.cs:26` | ⚠ **`Common` never occurs** — it is only reachable as the graceful default (`:96-97`), i.e. it doubles as an "unparsed" sentinel *and* a real tier. **Any typo in `tier` silently becomes Common.** |
| `materials.json` · `kind` / `category` (n=27) | `material` × 27 · `herb` 4, `crystal` 4, `liquid` 4, `metal` 3, `wood` 3, +7 more | none | ⚠ **`kind` is single-valued (zero information) and `MaterialDef.Kind` (`MaterialCatalog.cs:44`) is read by nothing.** The real classifier `ItemIdentityKind` (`ItemIdentity.cs:36`) is derived from *which catalog owns the id*, not from this field. |
| `loot-tables.json` · `source` (n=19) | `enemy` 9, `boss` 5, `dungeon` 5 | none | ⚠ DTO comment (`LootTableCatalog.cs:61`) calls it "documentation" — **stale**: it *is* read, at `ItemDropWatcher.cs:142-143`, which tests `== "boss"` only. **`enemy` (9) and `dungeon` (5) are inert — 14 of 19 rows.** The real boss gate is `drops[].bossOnly`. |
| `cosmetics.json` · `category` / `unlockMethod` / `appliesTo` (n=37) | `village` 18, `hero` 13, `pet` 6 · `achievement` 28, `buy` 9 · 13 distinct | none for any | see §8 — `category` is **save-coupled** |
| `packs.json` · `pricing.*` (n=13) | `usd` 13, `usdc` 13, `sol` 13, `skr` 13 | `CurrencyKind {Sol,Usdc,Skr}` `…/Wallet/WalletService.cs:45` | ⚠ **4 price rails, 3 enum members — `usd` has no member** (display-only reference, `PackCatalog.cs:123`). Reading the enum as "the set of price rails" is wrong. |
| `packs.json` · `tier` | ints **1–13**, one pack per tier | none | ⚠ **doc vs data:** `PackCatalog.cs:92` says *"Pricing tier 1–5"* and `:~163` says *"All five packs"* — **the data has 13.** Both reported; data wins. |
| `packs.json` · `convenience[].kind` | `instant-build` 13, `harvest-auto-collect` 5, `instant-repair` 5, `xp-weekend` 3 | none — a `HashSet<string>` allowlist `PackCatalog.cs:214-223` | 11-entry allowlist, **4 used**. Deliberately a string set (`:205-213`) because it must accept kinds authored in a JSON that never loads at runtime — see 5.3. |
| `stake-rewards.json` · `kind` | `title` 2, `cosmetic` 2, `trickle` 2, `badge` 1 | `StakeRewardKind` `StakeRewardsResolver.cs:34` | ⚠ `Other` never occurs — the unparsed fallback (`:287`). Also `StakeRewardsResolver.DefaultTiers` (`:305-326`) is a **hardcoded C# duplicate** of the JSON; the file's `_note` says "keep in sync" — two sources of truth. |
| `skin.json` · `authMode` / `identityKeyKind` | `PiSdk` 2, `SolanaWallet` 1 · `PiUid` 2, `WalletPubkey` 1 | ✅ `SkinAuthMode` `…/Core/Platform/CurrencySkin.cs:22` · `SkinIdentityKeyKind` `:31` | ✅ **exact 1:1 both.** But the `SkinId` doc comment (`CurrencySkin.cs:~52`) says *"`pi` or `skr`"* while the data ships a **third skin `wallet` — and it is the ACTIVE one**. Stale comment, not a bug (`CurrencySkinResolver.cs:332` handles it). |
| `wallets.json` · `network` | `devnet` 1 of 2 | `WalletCluster {Devnet,Mainnet}` `WalletService.cs:~36` | ⚠ **the enum is not parsed from this JSON** — `WalletEntry.Network` stays a raw string. Enum and data field are unconnected. |
| `ad-placements.json` · `surface` / `rewards[].kind` | `build`/`harvest`/`daily` 1 each · `currency` 3, `timeskip` 1, `harvest` 1 | none | nothing reads any of it — see §9 |

### 5.3 The three StreamingAssets-only monetization files — none load at runtime

`CanonicalJson` falls back to StreamingAssets, so a Resources-less file *could* load. **None of these do — no runtime call site names them.** All three are exempted from the dual-copy gate by `DataWebRegression.IsNonDualCopyByDesign` (`Assets/Editor/Regression/DataWebRegression.cs:130-135`: `.sample.json` OR `skr_` OR `battle_` prefix).

| File | Read by | Consequence |
|---|---|---|
| `skr_staking.json` | editor-only, `Assets/Editor/Regression/MonetizationCovenantRegression.cs:122-127` (raw `File.ReadAllText`) | its `convenienceAllowList` + `perkKindEnum` are **hand-copied** into `PackCatalog.ConvenienceAllowList` (`PackCatalog.cs:221-222`). ⚠ `perkKindEnum` is **literally an enum authored in JSON with no C# counterpart at all.** |
| `skr_store.json` | editor corpus sweep only, `MonetizationCovenantRegression.cs:96` | shape maps to nothing in C#. `CANON_GROUND_TRUTH_2026-08-07.md:213` records a known **2.9× price divergence vs `packs.json`** — consistent with dead data. |
| `battle_monthly_packs.sample.json` | editor corpus sweep only, `:98` | `PackCatalog` reads **only** `packs.json` (`:156`); the `.sample` suffix is never resolved |

Also Resources-only by design (declared at `DataWebRegression.cs:106-115`): `ad-creatives.json`, `ad-placements.json`, `widget-params.json`.

---

## 6. Meta / UI / narrative / world-config family (21 files + 3 directory bundles)

### 6.1 Loader-pattern deviations — five files do NOT go through `CanonicalJson`

| File | How it actually loads | Consequence |
|---|---|---|
| `hud-areas.json` | raw `Resources.Load<TextAsset>` + **`JsonUtility`** — `…/HUD/Kit/HudAreasConfig.cs:56,63` | deliberate — no Newtonsoft in the `DeNelle.HUD` asmdef. No StreamingAssets fallback. |
| `widget-params.json` (337 KB, largest canonical file) | raw `Resources.Load` + `JsonUtility` into a **`private` DTO** `WpFile` — `…/Core/UI/ElarionUiKitObsidian.cs:210,239,242` | no dual-copy guarantee; generated by `Assets/Editor/PrefabParamExtractor.cs:42` |
| `dungeon-layouts/*` | `Resources.Load<TextAsset>(LayoutsResourcePath + dungeonId)` — `…/Dungeons/DungeonRoomBinder.cs:204` (const `:44`), `DungeonTreasureCache.cs:285` | Resources-only, no StreamingAssets fallback |
| `dungeons/*` | own async `UniTask` reader, path built from id at `…/Dungeons/DungeonLayout.cs:281-282` | Android StreamingAssets-in-jar handling |
| `dungeon-graphs/*` | editor `File.ReadAllText` — `Assets/Editor/RoomForge/GraphDungeonComposer.cs:268` | ⚠ **editor-only; zero runtime references** |

☠ **`audio-mix.json` has NO LOADER.** Zero `CanonicalJson.Read` / `Resources.Load` anywhere; only mention is the mirror allowlist `DataWebRegression.cs:156`. Its six tracks + volumes are **hardcoded in C#** at `…/Audio/MusicTrack.cs:24` (`MusicTrack` enum + `TrackDefinition` table), sourced from `docs/audio-mix-spec.md`. **Two independent copies with no sync check.**

⚠ **`scene-configs.json` has THREE loaders with THREE separate `private sealed class SceneConfigFile` DTOs** — `…/Core/HubScenes.cs:193`, `…/Village/SceneOwnership.cs:127`, `…/Village/World/SceneConfigCatalog.cs:215`. One file, three parses, three shapes to keep in sync.

⚠ **`pets.json` has two loaders** — `PetCatalog` (`…/Pets/PetCatalog.cs:196`) and `IntroPetCatalog` (`…/Onboarding/IntroPetCatalog.cs:102`, a 6-field subset view).
⚠ **`canon-strings.json` + `en.json` have three loaders each** — `CanonStrings.cs:124`, `VillageStrings.cs:102`, `HeroCanonNames.cs` — and **no typed DTO at all** (`Dictionary<string,object>`).

### 6.2 `hud-areas.json` vs the action bar — **VERDICT: THEY AGREE**

**Loader** `HudAreasConfig.Load` `…/HUD/Kit/HudAreasConfig.cs:51` · **DTO** `FileShape`/`PostureRow`/`AreaRow` `:30-32` (all `private`) · **Consumers** `HudKitController`, `HudActionBarModel`

- `posture` — 7 values, 1 each. Enum `HudPosture` `…/HUD/Kit/HudPosture.cs:17` (7 members). ✅ **exact 1:1.**
- `area` — 11 distinct. Enum `HudArea` `…/HUD/Kit/HudAreasHost.cs:27` (11 members), parsed by string switch `HudAreasConfig.cs:102-122`. ✅ **exact 1:1.** Failure mode is documented at `:116-118` — an unknown `area` is **row-skipped with a Warn**, which is how the Work button once went dark.

| Ordinal | `ActionBarButtonId` (`…/Core/HudModel/HudActionBarModel.cs:55`) | hud-areas.json widget | Registered |
|---|---|---|---|
| 0 | `Build` | `buildButton` | `HudKitController.cs:491` |
| 1 | `Talk` | `talkButton` | `:503` |
| 2 | `Bag` | `bagButton` | `:519` |
| 3 | `Raids` | `raidsButton` | `:543` |
| **4** | **`Map`** | **— no row —** | **— not registered —** |
| 5 | `Quests` | `questButton` | `:563` |
| 6 | `Upgrade` → `PanelId.Manage` | `upgradeButton` | `:578` |

`calm(town)` `actionBar` = 6 rows in ascending ordinal order (0,1,2,3,5,6), matching `MaxVisibleFaces = 6` (`HudActionBarModel.cs:115`) and `ComputeMask` (`:265-271`). **`Map = 4` is dormant by design, not an off-by-one** — the enum doc (`:62-69`) keeps the ordinal so the View's face arrays don't re-point, and `ButtonCount` stays 7 (`:107`) so `Upgrade = 6` stays in bounds. **The re-point is consistent across enum, JSON and registration.**

⚠ **One latent divergence:** `hostile(prebattle)` and `hostile(activebattle)` both list **`buildButton`** in their `actionBar` row, but `ComputeMask` returns `0` for every non-calm posture (`:280`). The occupancy row grants a mount the model never activates. The in-source claim at `:256-258` that "the model agrees by construction" **does not hold for `buildButton`.** (The other ids in those rows — `assignableSkillRow`, `hpPotionSlot`, `manaPotionSlot` — are independent kit widgets, not bar faces, so those are fine.)

⚠ **Dead / orphan widget rows:** `xpBar` (never registered, inert per `HudKitController.cs:284-288`); `settingsButton` (4 rows, never registered, inert per `:310-315`); and the inverse — `targetCycle` is **registered** (`:1203`) but **has no row in any posture**, so it never mounts.

### 6.3 `echoes-balance.json` — affinity canon verified ✅

**Loader** `EchoBalanceCatalog` `…/Village/Harvest/EchoBalanceCatalog.cs:126` · **DTO** `EchoBalanceData` `:45` · **Consumers** `EchoBonusCalculator`, `EchoAssignments`, `EchoCardVM`

⚠ **The JSON carries NO `affinity` field and no resource field at all** (24 lines total). Its only strings are echo ids and the `crossBonuses[]` names (plus `_authoringNotes`). ⚠ **CORRECTED 2026-09-10 (WO-1430 lane FIELDS-DROP):** this line read *"Its only strings are `levelCurve: \"linear\"` and echo ids"* until the owner ruled that field DROPPED — `levelCurve` is no longer in either twin, and re-adding it REDS `AuthoredFieldReaderRegression` case `[retired-field-stays-retired]`. **Affinity lives in a C# code table** — `EchoRosterCatalog` (`…/Village/Harvest/EchoRosterCatalog.cs`), whose header `:7-16` states the "code table, not JSON" ruling, and the JSON's own `_authoringNotes` agrees: *"Identity (name/element/affinity) stays in the EchoRosterCatalog CODE table; ONLY the numbers live here."* **The two sources agree.**

- Enum `HarvestTarget` `EchoRosterCatalog.cs:78` = `Wood, Iron, Food, Gold, Crystals`. Roster counts: `Crystals` 2 (**Bran + Maren — the deliberately doubled affinity, matching canon**), others 1 each. ✅ all 5 members used, no orphans.
- ✅ **"Match bonus, never a lock" confirmed at source** — `EchoBonusCalculator.cs:107` adds `PreferredLaneMatchBonus` on a plain equality test (`:218`, `:394`); nothing gates assignment. `baseContributionPerEcho` 0.02 + `preferredLaneMatchBonus` 0.03 → matched Lv1 = +5%, unmatched = +2%.
- ✅ **`<resource>:<level>` token grammar confirmed** — producer `EchoRosterCatalog.TargetToken()` `:246-258`, parser `:274-285`, save field `PersistedState.EchoLanes` `SaveSchema.cs:524` (grammar documented `:505-523`).
- ⚠ `LaneType` (`EchoRosterCatalog.cs:64`) has 5 members but **all six echoes are `Harvest`** — `Crafting`, `Defense`, `Exploration` are dormant.

### 6.4 Per-file registry (remainder)

| File | Loader | DTO | Primary consumers |
|---|---|---|---|
| `glossary.json` | `GlossaryCatalog` `…/Village/UI/Guide/GlossaryCatalog.cs:151` | `GlossaryData` `:68` | `GuideVM` (sole) |
| `guide-content.json` | `GuideContentCatalog` `…/UI/Guide/GuideContentCatalog.cs:106` | `GuideContentData` `:61` | `GuideVM` (sole) |
| `concept-icons.json` | `ConceptIconResolver` `…/Core/UI/ConceptIconResolver.cs:190` | `ConceptIconData` `:61` (private) | `ElarionUiKitObsidian`, `QueueIconResolver`, `BuildPaletteUI` |
| `themes.json` | `Theme` `…/Core/Theme/Theme.cs:187` | `ThemeCatalog` `:53` | `Theme` itself; tokens via `ElarionUi`/`UiStyle` |
| `chat-phrases.json` | `ChatPhraseCatalog` `…/Core/Services/ChatPhraseCatalog.cs:96` | `ChatPhraseCatalogData` `:42` | `ClanService`, `ClanChatPanel`, `ClanChatVM` |
| `quests.json` | `QuestCatalog` `…/Core/Quests/QuestCatalog.cs:261` | `QuestCatalogData` `:217` → `QuestDef` `:192` | `QuestService`, `StoryQuestSignalBridge`, `RumorBoardVM`, `QuestTrackerHud` |
| `daily-quests.json` | `DailyQuestCatalog` `…/Core/Quests/DailyQuests.cs:131` | `DailyQuestCatalogData` `:59` | `DailyQuestHud`, `DailyQuestVM`, `ClanService` |
| `tutorial/tutorial-steps.json` | `TutorialStepCatalog` `…/Core/Tutorial/TutorialStepModel.cs:156` | `TutorialStepsData` `:106` | `TutorialFlow` (sole) |
| `dialogue/dialogues.json` | `DialogueCatalog` `…/Core/Dialogue/DialogueModel.cs:154` | `DialogueCatalogData` `:102` | `DialogueService` ×2, `DialogueViewModel`, `CastleVendorNpcInjector` |
| `lore-fragments.json` | `LoreFragmentsLoader` `…/Dungeons/LoreFragments.cs:157` | `LoreFragmentSet` `:71` | `DungeonController`, `LoreReadingModal`, `WandererDialogue` |
| `scene-configs.json` | ⚠ three (above) | ⚠ three private + public `SceneConfigDef` `SceneConfigCatalog.cs:74` | `RaidSelectionScreen`, `RaidScoring`, `RaidGarrisonSpawner` |
| `realm-map.json` | `RealmMapCatalog` `…/Core/World/RealmMapCatalog.cs:160` | `RealmMapData` `:107` | `RealmMapVM` (sole), `RealmMapPanel` |
| `population-milestones.json` | `PopulationMilestonesCatalog` `…/Village/Population/PopulationMilestonesCatalog.cs:125` | `PopulationMilestonesData` `:78` | `PopulationService` (sole) |
| `pets.json` | ⚠ two (above) | `PetCatalogData` `PetCatalog.cs:105` + `IntroPetCatalogData` `IntroPetCatalog.cs:60` | `PetAcquisitionService`, `PetDeployer`, `PetSelectController`, `TutorialFlow` |
| `hero-talents.json` | `HeroTalentCatalog` `…/Village/Talents/HeroTalentCatalog.cs:311` | `HeroTalentData` `:181` | `HeroSkillTreeVM`, `HeroLoadoutVM`, `HeroTalentModifiers`, `WisdomCurrencyService` |
| `canon-strings.json`, `en.json` | ⚠ three each (above) | ⚠ **none — untyped `Dictionary<string,object>`** | `TitleController`, `OnboardingFlow`, `BuildingSign`, `HudKitController` |
| `widget-params.json` | `ElarionUiKitObsidian.cs:239` | `WpFile` `:210` (private) | `ElarionUiKitObsidian` only |
| `audio-mix.json` | ☠ **none** | ☠ none | ☠ none |

### 6.5 Classification fields

| File · field | Values occurring | Enum | Divergence |
|---|---|---|---|
| `quests.json` · `type` (n=24) | `side` 15, `main` 4, `gear` 3, `endgame` 2 | none | ⚠ the tab filter (`RumorBoardVM.cs:344+`) handles `all/story/gear/endgame` only — **`main` + `side` (19 of 24, 79%) have no tab of their own** and fall into the "story" catch-all. Nothing validates the vocabulary, so a typo buckets silently. |
| `quests.json` · `completeOn.kind` (n=63 stages) | `talk` 23, `arena` 11, `build` 9, `pet` 8, `wave` 7, `panel` 5 | enum-as-consts `QuestCompletion.KindTalk..KindRegion` `QuestCatalog.cs:66-82` (**12 declared**) | ⚠ **6 declared kinds never occur** — `reach`, `flag`, `dialoguecommand`, `upgrade`, `population`, `region`. The source flags the last three as *"Emitter NOT built yet"* (`:77-78`), but `reach`/`flag`/`dialoguecommand` are documented **LIVE emitters with zero data using them.** Inverse direction is clean. |
| `quests.json` · `completeOn.targetId` (kind=panel) | `Inventory`, `BuildingUpgrade`, `JewelerCrafting`, `RumorBoard`, `Crafting` | `PanelId` `…/Core/UI/PanelRouter.cs:37` | ✅ all 5 resolve. ⚠ **13 of 18 `PanelId` members are never named by any quest.** 🔴 **`PanelId` is ORDINAL-CRITICAL and append-only** — `PanelRouter.cs:109-110` states verbatim *"Append-only: values are load-bearing"*; two holes already exist (0 retired, 4 removed). Not persisted, but **do not renumber.** |
| `hero-talents.json` · `kind` (n=83 nodes) | `stat` 29, `skill` 22, **`passive` 18, `active` 3** | `SkillNodeKind {Skill, Stat}` `…/Talents/HeroSkillTreeVM.cs:50` (2 members) | 🚨 **21 of 83 nodes (25%) carry a value the type system does not know** — and the DTO doc (`HeroTalentCatalog.cs:150-157`) documents only `"skill" \| "stat"`. Worse: **`Kind` is never read to classify anything** — `IsSkill` (`:161`) derives purely from `!IsNullOrEmpty(AbilityId)`. A dead authoring annotation whose vocabulary has already drifted 25% from its own doc. |
| `hero-talents.json` · `tier` / `branch` | `tier2` 20, `tier1` 18, `tier3` 17, `tier4` 17, **`shared` 11** · `steward` 6, `bulwark` 5, `war` 1 | none | ⚠ DTO doc (`:94`) documents `tier1..tier4` — **`shared` is undocumented**, doing double duty as tier-index *and* array-origin marker. `branch` is on only 12 of 83 nodes and has **1 read** repo-wide — vestigial. |
| `daily-quests.json` · `slot` (n=44) | `combat` 19, `exploration` 13, `wildcard` 12 | none | ✅ 1:1 with the reward rows. ⚠ **persisted to PlayerPrefs**, not `SaveSchema` — `DailyQuests.cs:420`. String-valued → reorder-safe, rename-hostile. |
| `tutorial-steps.json` · `trigger.type` | `prev_complete` 6, `signal` 4, `scene_enter` 1 | none | ✅ all 3 consumed |
| `dialogues.json` · `commands[].verb` | `OpenShop` 8, `portrait` 6, `spawn_named_pet` 3, `RecruitCompanion` 2, +6 singletons | none | ⚠ **two casing vocabularies in one field** — PascalCase (`OpenShop`, `RecruitCompanion`…) alongside snake_case (`portrait`, `play_sfx`…), with **no normalizer on the DTO** (contrast `QuestCompletion.NormalizedKind` `:96`). |
| `guide-content.json` · `status` | **`live` × 30 — the only value** | none | ⚠ DTO declares two states (`GuideContentCatalog.cs:46`) and `IsComing` (`:56-58`) is live code — **the "coming soon" branch is unreachable from shipped content.** |
| `scene-configs.json` · `difficulty` / `wallTier` | `Regular`/`Hard`/`Extreme` 1 each · `Wood` 2, `Iron` 1, `ReinforcedSteel` 1 | ⚠ `Difficulty` (`…/Core/State/Enums.cs:22`) and `WallTier` (`…/Village/Walls/WallTierData.cs:30`) **both exist but neither is bound** — `SceneConfigDef.difficulty` `:82` and `.wallTier` `:86` are raw `string` | two independent vocabularies, no compile-time link. ⚠ **See §4.2 — `WallTier` also disagrees with `walls.json`**, so this string shadows an already-broken enum. |
| `scene-configs.json` · `faction` / `ownership` | `orc` 2, `none` 1, `mixed` 1, `hollow` 1 · `Enemy` 4, `Player` 1 | none | ⚠ schema doc lists 5 factions — **`troll` documented, never used**. ⚠ `garrison.boss` has **two spellings for one boss**: `orc-necromancer` 2 vs bare `necromancer` 1. ⚠ **parsing caveat:** the file's `_schema` pseudo-row holds *prose* in the same value positions — a naive whole-file value scan picks up documentation as data. |
| `realm-map.json` · `gate.kind` | `regionCleared` 4, `bestWave` 1 | enum-as-consts `RealmRegionGate.KindBestWave/KindRegionCleared` `RealmMapCatalog.cs:55-57` | ✅ both used. ⚠ `NodeType` and `RegionId` (`…/Core/World/RegionZone.cs:63,22`) exist but are **not bound** to this file. |
| `pets.json` · `species`/`element`/`archetype`/`stage` | 3/3/3 distinct · `Wandering/Kindled/Attuned/Warden/Heartsworn` ×3 each | none for any | `PetMode` and `PetAcquisitionSource` are runtime enums, unrelated. ⚠ **species strings reach save data** (`petActiveSlots`, v34) and are *also* quest targets — **three-way coupling: pets.json ↔ quests.json ↔ SaveSchema.** |
| `glossary.json` · `group` | `battle` 10, `village` 9, `world` 7, `account` 2 | none | ✅ referential integrity holds both ways |
| `chat-phrases.json` · `category` | `combat` 8, `greeting` 6, `gratitude` 5, `status` 5 | none | ✅ 1:1, no orphans |
| `concept-icons.json` · `role` | `spellicons` 27, `icons` 14, `currency` 10, `abilities` 5, `potion` 3 | none | RpgUi atlas folder names, not a game enum |
| `population-milestones.json` | — | — | ✅ **no classification field at all** — pure numeric thresholds |

### 6.6 Directory bundles

| Bundle | Files | Loader | Notes |
|---|---|---|---|
| `dungeon-graphs/` | 7 | ⚠ **editor-only** `GraphDungeonComposer.cs:268` (DTO `DungeonGraph` `:46`) | zero `_Modules/` references. `traps[].kind` is **overloaded** — carries both trap kinds (`spike` 6, `grate` 2) and encounter kinds (`hollow-group` 15, `orc-group` 4, `troll-group` 2), though the trap DTO documents only `spike \| grate` (`DungeonComposeLayout.cs:126`). |
| `dungeon-layouts/` | 8 + `rooms-catalog.json` | `DungeonRoomBinder.cs:204` (runtime), `DungeonTreasureCache.cs:285`; DTO `DungeonComposeLayout` `…/Dungeons/RoomForge/DungeonComposeLayout.cs:17` | `archetype`: `combat` 46, `hub` 42, `reward` 10, `lore` 8, `boss` 4, `secret` 3 — **no enum**. `connections[].type`: `Door` 36, `StairDown`/`StairUp` 4 each — no enum. ⚠ `encounter.mode` (`room` ×21) is **a JSON field `EncounterSpec` never declares** (`:77-92`) → dropped. `encounter.seatMode` (`ring` ×21) is 100% redundant with its own default (`:84`). `themePalette` (`default` ×23) is a constant. ⚠ `sockets[].facing` includes **`U`** (8×) in an otherwise 4-direction N/S/E/W vocabulary. ⚠ **`EncounterKind` (`…/Dungeons/EncounterTrigger.cs:41`) is NOT bound to `encounter.kind`** — it is a scene-authored `[SerializeField]`. Do not conflate. |
| `dungeons/` | 1 (`healers-cottage.json`) | `DungeonLayoutLoader.LoadAsync` `DungeonLayout.cs:281-295`; DTO `DungeonLayout` `:215` | `rooms[].kind`: `standard` 5, `reward` 3, `checkpoint` 2, `entry` 1, `boss` 1 — ✅ **exact match to the file's own `_schemaNotes.roomKind`**. `walls[].kind`: `solid` 56, `doorway` 17, `illusory` 2. ⚠ `ambientBgm: echoes-beneath-elarion` **does not appear in `audio-mix.json`** — and since that file has no loader, nothing reconciles them. |

🚨 **Two competing room taxonomies for the same concept in sibling directories:** `dungeon-layouts/` uses `archetype` (`combat/hub/reward/lore/boss/secret`) while `dungeons/` uses `rooms[].kind` (`entry/standard/checkpoint/reward/boss`). Neither is an enum; only `reward` and `boss` overlap.

---

## 7. Classification axes — which field decides what?

### ► The owner's question: "is there an enum for shop/store?"

**Answer: there is exactly ONE — and it types the storefront's _look_, not its _identity_.**

| What you might mean by "shop" | Is it an enum? | Where |
|---|---|---|
| **Which shelf UI this storefront renders** | ✅ **YES — `VendorLayout { Gear, Goods, Jeweler }`** | `…/Village/Hero/VendorStockResolver.cs:53`, types `vendors.json` → `layout`. **3 members, 3 values in data, perfect 1:1, no divergence.** Parsed `:154-157`. |
| **Which storefront this IS** (`market`/`forge`/`armorer`/`jeweler`) | ❌ **NO — a plain string, matched by `Contains`** | `VendorRegistry.Find` `…/Hero/VendorRegistry.cs:113-121`, then a 6-branch substring heuristic in `VendorStockContract.AllowedFor` `…/Hero/VendorStockContract.cs:99-134` (matching `"armor"`, `"blacksmith"`, `"craft"`, `"forge"`, `"smith"`, `"jewel"`, `"market"`, `"granary"`, `"farm"`…). **Adding a vendor is a string-authoring act with no compiler backstop.** |
| **Whether a building offers a shop at all** | ❌ **NO — a `bool`** | `BuildingDef.IsShoppable` `BuildingCatalog.cs:100`, true on exactly 3 `buildings.json` rows (forge, market, jeweler) |
| **What a store sells** | ✅ two enums, but they classify *wares*, not stores | `VendorWareKind` `VendorStockResolver.cs:64` and `[Flags] GearKind` `VendorStockContract.cs:37` (mapped `:150-160`) |

**There is no `shopTab`, no `vendorType`, no `storefront` enum anywhere, and no such field in any canonical JSON.**

Two traps worth knowing:
- **`ShoppableKind { Weapon, Armor, Craftable }`** (`…/Village/Hero/ShopCatalog.cs:44`) looks like the answer and is not — `ShopCatalog.cs` **reads no JSON at all** (zero `CanonicalJson`/`Data/Canonical` hits in that file). It is a narrower, non-data-driven duplicate of `VendorWareKind`.
- The **UI-only** shop enums are none of them data-sourced: `PartyShopTab`/`PartyShopCategory`/`PartyShopType` (`PartyShopVM.cs:46,55,63`), `InventoryTabKind` (`InventoryVM.cs:63`). *(`ShopMode` was a fourth, in `ShopVM.cs:31`; that file was DELETED 2026-09-06 by WO-1430 and nothing else referenced the enum.)*

**Divergences on the vendor axis:**
- ⚠ `VendorWareKind.Craftable` / `GearKind.Craftable` are **never triggered by data** — both resolvers handle the case (`VendorStockResolver.cs:359-364`, `VendorStockContract.cs:160`) but no `vendors.json` row declares that category; reachable only via the unregistered-vendor heuristic.
- ⚠ **`GearKind.Potion` never appears as a JSON word** — `vendors.json` authors `"consumable"`, mapped to `GearKind.Potion` at `VendorStockContract.cs:153`. Enum member name and data vocabulary disagree.
- ⚠ The resolvers accept plurals + aliases the data never uses (`weapons`, `armors`, `consumables`, `gems`, `rings`, `amulets`, `accessory`, `craftable(s)` — `VendorStockResolver.cs:264-364`); only the 7 singular forms occur.
- ⚠ `classFilter: "none"` never occurs (all 4 rows are `"roster"`) — the unfiltered branch is dead in data.
- ⚠ `maxReqLevel` is declared (`VendorRegistry.cs:48`) and read (`VendorStockResolver.cs:250`) but **supplied by 0 of 4 rows** → the level cap is permanently uncapped.
- ⚠ `onlyEquippable`, `perLevelCap`, `excludeIdPrefixes`, `footerLine` are supplied only on `forge` and `armorer`; `market` and `jeweler` silently take pre-WO-860 defaults.
- 🚨 **A regression has drifted from the source it cites:** `DataRegression.cs:851,877` requires the shoppable set `{forge, armorer, market, jeweler}`, citing "buildings.json isShoppable" — but the data flags **only 3**. **`armorer` is not `isShoppable` in `buildings.json`.**

---

### The building axis (the original confusion)

### The five axes, and when each one is correct

| # | Axis | Where | Is it an enum? | **Use it to decide…** |
|---|---|---|---|---|
| **A** | `type` | `structures-catalog.json` → `CatalogType` (`CatalogType.cs:8`) | ✅ real C# enum | **What palette/tab it belongs to, and how to query for it.** The selection + presentation axis. `CatalogRegistry.OfType()` (`CatalogRegistry.cs:63`), `BuildCategoryRegistry`, card copy (`StructureCardVM.cs:217`), "is this a tower?" (`BuildModeController.cs:2984`). |
| **B** | `repo.behaviorId` | `structures-catalog.json` → **`string`** (`RepoProps.cs:104`) | ❌ plain string | **What MonoBehaviour gets attached when it is built.** The runtime-behaviour axis. Consumed in exactly one place — the `switch` at `StructureFactory.cs:741`. Not indexed by the registry; nothing else reads it. |
| **C** | `type` | `buildings.json` → `BuildingType` (`Building.cs:30`) | ✅ real C# enum | **Which interaction panel opens on tap.** A per-building *identity*, not a class. Only meaningful for the 13 `GameplayBuilding` rows. |
| **D** | `isShoppable` / `isUpgradable` / `upgradeType` | `buildings.json` (`BuildingCatalog.cs:96-106`) | ❌ two bools + a free string | **What verbs the building's menu offers.** Capability flags, deliberately not mutually exclusive (`BuildingCatalog.cs:98`). |
| **E** | `id` | every file | ❌ string | **Identity, and everything persisted.** The only axis that survives a save/load round-trip. |

### The rule of thumb

> **`type` picks the tab. `behaviorId` picks the component. `BuildingType` picks the panel. `id` is who it is.**

### Why the axes look like they disagree

They disagree because **A and B are not the same kind of thing**, and B is not internally consistent:

`repo.behaviorId` mixes **class-level** values (`GameplayBuilding`, `DefenseTower`, `WallSegment`, `Gate`,
`ResourceCollector`) with **identity-level** values (`CrystalMine`, `HealingFountain`, `HealerTower`,
`ArcaneTower` — one row each). A one-row "class" is an identity wearing a class's clothes. That is why the
value counts never line up with `type`'s:

| id | `type` (axis A) | `behaviorId` (axis B) |
|---|---|---|
| `mine_crystal` | `Resource` | `CrystalMine` ← identity, not a class |
| `arcane-tower` | `Resource` | `GameplayBuilding` |
| `tower_arcane_spire` | `Tower` | `ArcaneTower` ← identity, not a class |
| ~~`tower_healer`~~ | ~~`Support`~~ | ~~`HealerTower`~~ — **ROW RETIRED** 2026-08-14 (WO-990, owner ruling): never buildable (in no build category), removed from `structures-catalog.json` at **v20**. The `HealerTower` **case in `StructureFactory` is deliberately KEPT** as the WO-891 field-pattern reference and is now unreferenced by any row; `healing_caravan` is the design successor. |
| `healing_caravan` | `Support` | `HealingFountain` ← identity, not a class |
| `deco_torch`, `repair_default` | `Decoration` | *(absent)* → no behaviour attached |

### ⚠ Two sources that directly contradict each other

**`market`'s `BuildingType`.** Reported as a conflict, not resolved here:

| Source | Says |
|---|---|
| `buildings.json` → `market.type` | `"Workshop"` → `BuildingType.Workshop` (**3**) |
| `StructureFactory.BuildingTypeForId("market")` — `StructureFactory.cs:957` | `BuildingType.Lumbermill` (**5**) |

`StructureFactory.cs:943-945` acknowledges the collision ("Market and Lumbermill share ordinal 5 … which is
benign") and argues it is harmless because routing re-resolves from the id first — which
`StructureHookIdFor` does (`BuildingInteractable.cs:363` matches `"market"` before the type switch). So the
contradiction is real but currently masked. It stops being masked for any code path that reads
`Building.Type` without an id fallback.

---

## 8. Persisted vs catalog-only

**The brief's assumption — "a persisted enum's numeric values cannot be reordered" — does not apply here,
for two independent reasons. Both verified at source.**

1. **No classification enum is persisted at all.** The save record for a placed structure is
   `PlacedStructureData` (`Assets/_Modules/Core/State/PlacedStructureData.cs:37`) and it stores
   **`itemId` (string)** — `:40` — plus grid cell, yaw, level, `worldY`, `wallMounted`. Not `type`, not
   `kind`, not `behaviorId`, not `BuildingType`. All four are **re-derived from the catalog by id** on load.
2. **Even where an enum is persisted, it is written by NAME, not by ordinal.** `SaveSchema.JsonSettings`
   registers a global `StringEnumConverter` (`Assets/_Modules/Core/State/SaveSchema.cs:74`), and the schema
   comments confirm the intent — "stores its enum keys as NAMES (strings) deliberately, so it survives enum
   renumbering" (`SaveSchema.cs:348-349`, and again `:376`, `:544`).

**Consequences, stated plainly:**

| Change | Safe? |
|---|---|
| Reorder / renumber `CatalogType`, `BuildingType`, `EntryKind`, `NavSurfaceKind`, `PlacementSurface` | ✅ **Safe** — never serialized by ordinal |
| **Rename** an enum *member* | ❌ **Breaks saves** — the name is the wire value |
| **Rename or remove a catalog `id`** | ❌ **Breaks saves** — `PlacedStructureData.itemId` is the persisted identity; an orphaned id loses the placed structure |
| `BuildingType._type` on the component (`Building.cs:66`) | scene serialization only (`[SerializeField]`), not the save file |

`BuildingType` is additionally a **lossy many-to-one projection** — `StructureFactory.BuildingTypeForId`
(`StructureFactory.cs:948`) folds distinct ids together and defaults unknown ids to `CrystalMine` (`:976`):

| `BuildingType` | ids that map to it |
|---|---|
| `CrystalMine` (0) | `lumberyard`, `foundry`, `silo`, `barracks`, **+ every unknown id** |
| `Workshop` (3) | `workshop`, `jeweler` |
| `Farm` (4) | `farm`, `mill` |
| `Lumbermill` (5) | `lumbermill`, **`market`** (see contradiction above) |
| `Armorer` (7) | `armorer`, `blacksmith` |
| `ApothecaryWorkbench` (8), `JewelersBench` (9) | **never produced from the catalog** — set only by runtime injectors (`Assets/_Modules/Village/Items/CraftingStationInjector.cs:145`, `Assets/_Modules/Village/Items/JewelerStationInjector.cs:135`). The catalog cannot express these two stations. |

### Confirmed repo-wide, not just for buildings

All three data groups were swept independently against `SaveSchema.PersistedState` (`SaveSchema.cs:235-639`).
**Not one classification enum in this entire registry is serialized numerically into a save.** Checked and
clear: `Rarity`, `GearTier`, `ConsumableKind`, `ConsumableEffect`, `VendorLayout`, `VendorWareKind`,
`GearKind`, `ShoppableKind`, `CurrencyKind`, `StakeRewardKind`, `SkinAuthMode`, `SkinIdentityKeyKind`,
`ItemCapability`, `ItemIdentityKind`, `HudArea`, `HudPosture`, `ActionBarButtonId`, `PanelId`,
`EncounterKind`, `HarvestTarget`, `LaneType`, `ElementType`. Saves carry **string ids / tokens** instead.

**So the standing rule for this codebase is inverted from the usual one:**

> **Reordering enums is safe. Renaming enum members, catalog ids, or classification *strings* is what breaks saves.**

**The save-coupled values that are NOT enums** (and are therefore the real rename hazards):

| Value | Persisted as | Where |
|---|---|---|
| catalog `id` | `PlacedStructureData.itemId` | `PlacedStructureData.cs:40` |
| `cosmetics.json` `category` | ⚠ a **dictionary KEY** in `GlimmerSaveData.EquippedByCategory` → PlayerPrefs `"dotr-cosmetics-v1"` | `…/Cosmetics/GlimmerCurrencyService.cs:47,59,176,319` — **renaming a category orphans every player's equipped cosmetic** |
| echo `<resource>:<level>` token | `PersistedState.EchoLanes` | `SaveSchema.cs:524` |
| pet **species** string | `petActiveSlots` (v34) | also a quest target — three-way coupling |
| `daily-quests.json` `slot` | ⚠ **PlayerPrefs**, not `SaveSchema` | `DailyQuests.cs:420` |
| gear / item / quest / stage ids | `ownedItemIds` `:243`, `gearInventory` `:267`, `gearLevels` `:623`, `equippedRingId`/`equippedAmuletId` `:441`/`:447`, `QuestProgress` `:273` | |

**Two enums ARE ordinal-critical — for indexing, not persistence.** Do not renumber either:
`PanelId` (`PanelRouter.cs:109-110`: *"Append-only: values are load-bearing"*, two holes already at 0 and 4)
and `ActionBarButtonId` (`HudActionBarModel.cs:62-69` — the View's face arrays are indexed by ordinal;
`Map = 4` is a deliberate dormant hole).

**Save schema version — ⚠ canon conflict.** `SaveSchema.CurrentVersion = **38**`
(`SaveSchema.cs:41`, v38 = WO-934 army loadout bank). `CLAUDE.md` §8 states **v37**. Reporting both;
`SaveSchema.cs:11-12` notes this header has gone stale before, so the const is the safer authority.

---

## 9. Dead data & missing fields

### 9.0 Cross-group ledger — files with NO loader (☠ nothing reads them)

| File | Only mentions | Note |
|---|---|---|
| `enemy-roles.json` | doc comment `EnemyTaxonomy.cs:79`, mirror pin `DataWebRegression.cs:151` | 9 roles + 25 creatures, all unread |
| `towers.json` | code comments `BuildModeController.cs:2321,2350`, mirror pin `:152` | tower range/damage/cooldown **inlined in code instead** |
| `heart.json` | mirror pin `:155` | contradicts `HeartController.cs:97` (`_hp=100` vs `maxHp:160`) |
| `audio-mix.json` | mirror allowlist `:156` | shadowed by the hardcoded `MusicTrack.cs:24` table |
| `ad-creatives.json` | exception registry `DataWebRegression.cs:112-115` | the registry itself calls it *"DEBT, NOT A DESIGN … a REMOVAL CANDIDATE, not a sanctioned exception"* |
| `ad-placements.json` | editor gate only, `AdPlacementCovenantRegression.cs:41` | no runtime loader; its `adUnitId`s appear in **zero** `.cs` files. Behaviour is hardcoded in `FeatureFlags.cs:645` + `BuildTimerConfig.cs:92`. |
| `skr_staking.json`, `skr_store.json`, `battle_monthly_packs.sample.json` | editor covenant gate only | see §5.3 |

**The dual-copy mirror gate keeps six of these files byte-synced and drift-free — on catalogs nothing reads.**

### 9.1 Highest-value divergences, ranked

| # | Finding | Where |
|---|---|---|
| 1 | **335 of 431 weapons + 21 of 30 armors never load** — Resources wins over a drifted StreamingAssets copy, silently | §2 |
| 2 | `enemies.json` `role` vs `EnemyRole` — **zero token overlap**; `caster→Healer` mislabels every ranged caster; a second id-keyed map disagrees with the first | §4.2 |
| 3 | `walls.json` tier ladder vs `WallTier` enum vs a hardcoded C# ladder — **three sources, all misaligned** | §4.2 |
| 4 | `abilities.json` `effect` — 11 values / 17 rows (44%) with no enum member | §4.2 |
| 5 | `hero-talents.json` `kind` — 21/83 nodes carry `passive`/`active` against a 2-member enum, and the field is read by nothing | §6.5 |
| 6 | `weapons.json` `job:"cleric"` — 2 rows no playable class can ever match | §5.2 |
| 7 | `garrison-recipes.json` — 4 authored nested objects (`boss`, `destruction`, `layout`, `traps`) the DTO never declares | §4.4 |
| 8 | `armor.json` `perk` — 15/24 rows of authored gameplay prose, **not on the DTO, read by nothing** | §5.2 |
| 9 | `realm-map.json` — 6 authored, round-tripped DTO fields with zero consumers | §6.5 |
| 10 | `GearTierTable.Parse` collapses `epic`+`legendary` into one tier | §5.2 |

### 9.2 Structure-catalog fields present in JSON, read by nothing

| Where | Field | Evidence |
|---|---|---|
| `structures-catalog.json` (4 rows) | top-level `canHitAir` | no such member on `CatalogEntry`; only `RepoProps.cs:307` exists. Dropped silently (`CatalogBootstrap.cs:116`). **`arcane-tower` loses its only `canHitAir` value this way.** |
| `structures-catalog.json` (29 rows) | `repo.placement.noOverlap` | zero runtime readers; only written in `CatalogBootstrap.cs:219,276,333` |
| `structures-catalog.json` (29 rows) | `repo.placement.checkAffordable` | read only by `BuildEconomyRegression.cs:241`; runtime placement ignores it |
| `structures-catalog.json` (29 rows) | `kind` | always `Cell`; only reader is editor-side `LayoutValidator.cs:236` |

### 9.3 Fields the class declares that the JSON never supplies

| Class | Field | Note |
|---|---|---|
| `PlacementRules` | `requiresSupport` (`PlacementRules.cs:26`) | never authored **and** never read |
| `PlacementRules` | `ownedGate` (`PlacementRules.cs:32`) | never authored **and** never read |
| `CatalogEntry` | `composite` (`CatalogEntry.cs:56`) | 0/29 rows; the builder path `StructureFactory.cs:553-591` is unreachable |

### 9.4 Enum members that never occur in the data

| Enum | Unused members |
|---|---|
| `CatalogType` (`CatalogType.cs:8`) | **`Stairs`, `Floor`, `Room`, `Troop`** — absent from the data *and* from all code (`grep CatalogType.{Stairs,Floor,Room,Troop}` → 0 hits) |
| `EntryKind` (`CatalogType.cs:27`) | `Composite` |
| `NavSurfaceKind` (`CatalogType.cs:30`) | `Walkable` |
| `PlacementSurface` (`CatalogType.cs:33`) | `Floor` |
| `DamageElement` (`IDamageable.cs:132`) | `Flame`, `Ice` (unused *by structures*; combat data may use them — see §4) |
| `BuildingType` (`Building.cs:30`) | `ApothecaryWorkbench`, `JewelersBench` — unreachable from the catalog (injector-only) |

### 9.5 Rows that exist but cannot be reached

| Row | Why |
|---|---|
| `deco_torch` (`type: Decoration`) | `Decoration` is in **no** `build-categories.json` row and `CatalogType.Decoration` appears in no runtime code (only a regression oracle, `Assets/Editor/Regression/CoreCatalogRegression.cs:84`). Yet it is priced as buildable — "the cheapest is deco_torch at 5 wood" (`Assets/_Modules/Core/Catalog/BuildTimerConfig.cs:121`). **Priced to build, unreachable from every palette.** |
| `repair_default` (`type: Decoration`) | **Not a structure at all** — a price-table row read by `WallRepairController` (`Assets/_Modules/Village/Walls/WallRepairController.cs:532`, `:682`). `BuildCardArtRegression.cs:67` excludes it explicitly as "a repair-economy DATA row, not a building". `type: Decoration` here means "keep out of palettes", which is a third meaning for axis A. |
| ~~`Collector` (3 rows) + `Support` (2 rows)~~ | **SUPERSEDED 2026-09-06 (WO-1565).** The type-level fallback prose in `StructureCardVM.DescriptionFor` is DELETED; every buildable row now carries an authored `description` and an unauthored one FAILS `BuildEconomyRegression` `[structure-descriptions]` (`Assets/Editor/Regression/BuildEconomyRegression.cs:235`). No row shows "A village structure." from that seam any more. |

### 9.6 Code that has drifted from its cited source

| Claim | Reality |
|---|---|
| `DataRegression.cs:851` + `:877` require the shoppable vendor set `{forge, armorer, market, jeweler}`, citing "buildings.json isShoppable" | `buildings.json` sets `isShoppable:true` on **only 3** — forge, market, jeweler. **`armorer` is not flagged shoppable in the data.** The regression's list is hardcoded and has drifted from the source it names. |

---

## 10. Maintenance

Per CLAUDE.md §15, update this file **in the same commit** as any change that:
adds/removes a canonical JSON; changes a DTO field; adds/removes/renames an enum member on any of the five
axes; or changes which class loads a file. If deferred, add a one-line `STALE:` flag at the top naming what
is now wrong.
