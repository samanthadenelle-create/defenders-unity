# MASTER CATALOG — Scenes

**Verified 2026-08-02** from the actual tree (`ProjectSettings/EditorBuildSettings.asset`,
`git ls-files "*.unity"`, routing source, `.gitattributes`, git history) — NOT from comments.
Supersedes the 2026-06-12 body and its 2026-07-22 STALE banner. Live anchor at write time:
`CANON_GROUND_TRUTH_2026-08-01.md` (branch `wip/village2-and-f8-tickets`).

Scope: every project scene under `Assets/Scenes/**`, the build-scene list, the routing/oracle
code that wires them, and the deleted-scenes ledger. Third-party pack scenes noted at the end.

---

## 1. Build settings — the REAL list (23 scenes, all `enabled: 1`)

Source: `ProjectSettings/EditorBuildSettings.asset` lines 8–76. The old "13 build scenes"
figure is ~10 scenes stale. Order (index = load-by-name eligibility; #0 = boot):

| # | Scene | Role (one line) |
|---|---|---|
| 0 | `Title` | Boot scene — bumper, title menu, Core singletons spin up |
| 1 | `HeroSelect` | Hero pick; default path now skips PetSelect (see §3) |
| 2 | `PetSelect` | Starter-Warden pick — **bypassed by default** (`ff.BypassPetSelect` ON, `HeroSelectController.cs:44`) |
| 3 | `Village2` | Enemy stronghold / raid target (Elarion TD town name kept) |
| 4 | `Dungeon_HealersCottage` | Starter authored dungeon (verified live 2026-08-01: 12 rooms/5 lore/2 checkpoints/4 encounters/miniboss, FPV) |
| 5 | `ATBBattle` | ATB Last-Stand battle; return-point stash (`SceneRouter.cs:572`) |
| 6 | `Dungeon_FolksGranary` | Stub dungeon — portal **GATED until WO-776** (`DungeonWorldPortalSpawner.cs:597-600`) |
| 7 | `MainCastle_Hall` | **LEGACY** two-scene home hub — reachable only when `ff.mergedworld` OFF (see §2) |
| 8 | `Garrison_troll_outpost` | Additive garrison raid scene (GarrisonController) |
| 9 | `Garrison_ruined_keep` | Additive garrison raid scene |
| 10 | `Garrison_hill_fort` | Additive garrison raid scene |
| 11 | `Garrison_frost_keep` | Additive garrison raid scene |
| 12 | `RaidBase_raider_camp_small` | Config-baked raid base — easiest (`SceneRouter.cs:173`) |
| 13 | `RaidBase_fortified_garrison` | Config-baked raid base — mid (`SceneRouter.cs:175`) |
| 14 | `RaidBase_mage_enclave` | Config-baked raid base — hardest of the three (`SceneRouter.cs:177`) |
| 15 | `Dungeon_Demo` | DungeonComposer v1 output — dev/demo scene (`Assets/Editor/DungeonComposer.cs:30`) |
| 16 | `Garrison_village2_stronghold` | Baked garrison for the `village2_stronghold` recipe (garrison-recipes.json); no by-name code ref found — loaded via derived `Garrison_<id>` naming (see FLAG-6) |
| 17 | `Outpost1` | Dungeon-chain leg (Castle→Outpost1→Dungeon→Outpost2, `Core/World/SceneLink.cs:7-8`); binary-forced in git |
| 18 | `Outpost2` | Dungeon-chain leg; binary-forced |
| 19 | `Dungeon` | Dungeon-chain middle scene (DungeonChainBuilder batchmode output); binary-forced |
| 20 | `Main_Castle_Overworld` | **CANONICAL HOME HUB** — merged castle+overworld, ONE navmesh (see §2) |
| 21 | `KayKitChallengeOutpost` | Bounded challenge arena (KayKitChallengeOutpostBuilder); cave-portal repoint target under `ff.raidwalk` (currently OFF — see FLAG-4) |
| 22 | `DungeonCompose/dg_starter_loop` | Room-Forge composed dungeon loop — **binary on disk** (see §5) |

**On disk but NOT in build settings (4 project scenes):** `BattleHUD_Mockup`,
`HUD_Obsidian_Showcase` (UI mockup/showcase scenes), `VfxGallery` (VFX browse scene),
`RaidBase_IronBastion` (the RaidBaseGenerator TEMPLATE bake — see FLAG-5). Anything not
enabled here cannot load by name: `SceneRouter.LoadScene/LoadSceneWithFade` guard with
`IsSceneRegistered` (`Application.CanStreamedLevelBeLoaded`) and abort with a LogError
(`SceneRouter.cs:216-220`).

Total tracked project scenes under `Assets/Scenes/**`: **27** (23 in build + the 4 above).

---

## 2. Home hub — Main_Castle_Overworld (merged world)

- **`SceneRouter.Castle` is a flag-aware PROPERTY, not a const** (`SceneRouter.cs:150-152`):
  `ff.MergedWorld` ON → `"Main_Castle_Overworld"`; OFF → `"MainCastle_Hall"`.
- **Flag truth:** `FeatureFlags.cs:358` — `Get("mergedworld", defaultOn: true)` → **default ON**.
  ⚠ The XML `<summary>` on this and 11 other flags lies about the default; the trailing `//`
  comment on the property line is authoritative (anchor 08-01 §3b).
- Built by **`WorldMergeBuilder`** (`Assets/Editor/WorldMergeBuilder.cs:53-55`): merges the old
  castle + outer world into ONE continuous scene, then bakes **one navmesh** →
  `Assets/Scenes/Main_Castle_Overworld/NavMesh-Main_Castle_Overworld.asset` (binary-forced by
  the `NavMesh-*.asset binary` gitattributes rule). `WorldBakeOrchestrator` sequences the bakes.
- With MergedWorld ON there is **no additive stream and no seam warp**: `WorldSceneLoader` is
  **DEPRECATED — a logging no-op** (`WorldSceneLoader.cs:2-19,152-161`: "OuterWorld scene removed
  (WO-608 MergedWorld)"). `HubScenes.IsOverworld()` (`HubScenes.cs:42-46`) is the single
  overworld test (`== "Main_Castle_Overworld"`).
- **`MainCastle_Hall.unity` stays on disk AND in build** (#7) as the legacy OFF-path fallback —
  `SceneRoutingRegression` deliberately proves BOTH Castle resolutions are registered under both
  flag states (`SceneRoutingRegression.cs:17-21`), so do not remove it from the build list
  without retiring the flag. Note `CastleWallStairsSeatFix.cs:83`: the 07-07 content sweep
  removed things from the Hall only; the shipped world is the merged scene.

---

## 3. Boot chain (verified against Onboarding source)

```
Title (#0, boot)
  ├─ Continue ──────────────► SceneRouter.GoCastle() ─► Main_Castle_Overworld (flag ON)
  └─ New game / hero pick ──► HeroSelect
HeroSelect ── confirm ("Dive in")
  ├─ ff.BypassPetSelect ON (DEFAULT) ─► FoundingChoiceController.PresentOrContinue(GoCastle)
  │      (founding choice: Default Town vs Build Your Own → GoCastle)   HeroSelectController.cs:44,655-656
  └─ flag OFF (reversibility hatch) ─► GoPetSelect() ─► PetSelect ─► founding choice ─► GoCastle
```
- `PetSelectController.cs:128-129`: with BypassPetSelect ON the PetSelect screen skips itself
  (founding choice then GoCastle). The old "PetSelect confirm → Castle" path is the hatch, not
  the default.
- **`DevBootScene`** (`Assets/_Modules/Core/DevBootScene.cs`): `-bootScene <SceneName>` CLI arg
  loads any BUILD scene directly at startup, skipping onboarding
  (`DefendersOfTheRealm.exe -bootScene Village2`). Arg-gated no-op otherwise; warns and ignores
  names not in Build Settings (lines 27-43). GameState falls back to defaults (fine for QA).

---

## 4. Scene families

### Village2 — raid target (enemy stronghold)
`SceneRouter.Village = "Village2"` (`SceneRouter.cs:135`); reached via `GoVillage()` →
`LoadVillageWithLoader()` (code-built uGUI overlay, async, holds activation to 90%, plants the
decorative Tree of Life at (0,-0.25,0) as the last word — `SceneRouter.cs:470-525`).
Contents are REGENERATED as a layered enemy fortress by
**`EnemyStrongholdBuilder`** (`Assets/Editor/EnemyStrongholdBuilder.cs:2-14`): outer courtyard →
chokepoint → raised keep → boss chamber, recipe `village2_stronghold` in `garrison-recipes.json`
(one recipe, two readers — the builder's local StrongholdRecipe DTO + Core's GarrisonRecipe).
Raid flow driven by `Village2RaidController` (`.../World/Camps/Village2RaidController.cs:12-18`)
mirroring the RaidBase_* victory path. Enemy-owned per `scene-configs.json` → town HUD suppressed
(`HubScenes.SuppressTownHud`, `HubScenes.cs:91-97`).

### RaidBase_* — config-baked raid bases (3 in build + 1 template)
Baked by **`RaidBaseGenerator`** (`Assets/Editor/WallTools/RaidBaseGenerator.cs`) from
`scene-configs.json` (`BuildAllRaidScenes` / `BuildSceneFor(id)` → `RaidBase_<id>.unity`); the
"Iron Bastion" concentric layout is the template (`BuildIronBastion`, root `RaidBase_IronBastion`).
Nav re-baked by `RaidNavBake` (`Assets/Editor/RaidNavBake.cs:36` includes IronBastion) — note it
marks **every Renderer** NavigationStatic and runs the LEGACY `NavMeshBuilder.BuildNavMesh()`
(`:55-70`), so nav carve comes from render meshes, not colliders.
Dressed by **`RaidBaseDresser`** (`Assets/Editor/WallTools/RaidBaseDresser.cs`), called from
`RaidBaseGenerator.cs:424` AFTER rings/turrets/spire/staging exist: zones, wall clad, gatehouse,
floors, and the `raidDress.props` set. ⚠ `RaidBaseGenerator.cs:35` still says prop dressing is
"still dead" — **that comment is STALE**, the props are authored and read (WO-1635 retires it).
Its courtyard cover-ring / cluster math lives in **`CoverRingPlacer`**
(`Assets/Editor/WallTools/CoverRingPlacer.cs`, WO-1633) — the reusable half of
`ProceduralSiegeArenaBuilder.PlaceCoverRing`, extracted so the arena can adopt it without a
third copy; props authored `cover: true` keep their colliders and are real cover.
Entry: `SceneRouter.GoRaid(sceneName)` (`SceneRouter.cs:456`) with one of the three consts
(`SceneRouter.cs:173-177`). On the far side: `RaidGarrisonSpawner` (on the baked root; carries
the STORED config id — the scene name `RaidBase_<id>` does NOT match the config's sceneName,
`RaidGarrisonSpawner.cs:17`) spawns the garrison; `RaidVictoryController` self-installs on any
`RaidBase_*` scene (`RaidVictoryController.cs:54-72`); `HubScenes.IsRaid` = `StartsWith("RaidBase")`
(`HubScenes.cs:52-56`). Loss/retreat evacs via GoCastle. `GameOverScreen.cs:50`: RaidBase scenes
keep their own death flow (intentionally not hubs).

### Garrison_* — additive garrison scenes (5 in build)
`Garrison_troll_outpost / _ruined_keep / _hill_fort / _frost_keep / _village2_stronghold`.
Authored by `GarrisonSceneBuilder`; driven by
`DeNelle.Village.World.Camps.GarrisonController` on `GarrisonRoot` (`Activate()` spawns the
recipe garrison via EnemyFactory; `CleanupAndUnload()` unloads its own scene). Ownership per
`SceneOwnership` / `scene-configs.json` (`SceneOwnership.cs:8`).

### Dungeons
- **`Dungeon_HealersCottage`** — canonical starter dungeon (`SceneRouter.cs:180`). Live-verified
  2026-08-01 from a captured headless run (anchor §2): EnterDungeon 12 rooms/5 lore/2 checkpoints/
  4 encounters/miniboss · HydrateExits · DressTraversalLinks · DoorAlign · FPV third-person mode ·
  HideHeroBody · DungeonHero is the sole mover (HeroLocomotion neutralized in-dungeon).
- **`DungeonCompose/dg_starter_loop`** — Room-Forge composed loop (GraphDungeonComposer /
  DungeonBaker). The overworld's authored dungeon portal routes here — `DungeonWorldPortalSpawner.cs:113`
  (`AuthoredPortal("dg_starter_loop", (140,0,20))`); composed ids ship the FULL scene name and must
  not be `Dungeon_`-re-prefixed (`DungeonPortal.cs:187,226`). `DungeonExitInteractable.cs:15`:
  exits hydrate the ALREADY-BAKED scene with no re-bake.
- **`Dungeon_FolksGranary`** — stub, **gated**: its portal def is skipped "until its content WO
  lands (WO-776)" (`DungeonWorldPortalSpawner.cs:597-600`); the old fallback was removed (line 625).
- **`Dungeon_Demo`** — `DungeonComposer` v1 demo output (`DungeonComposer.cs:15,30,124`).
- **`Dungeon` + `Outpost1` + `Outpost2`** — the DungeonChainBuilder chain
  (Castle→Outpost1→Dungeon→Outpost2 + portal back, `SceneLink.cs:7-8`), resolved at runtime by
  `SceneLinkResolverHost`. DevPanel has an "Outpost1" jump (`DevPanelController.cs:766`).
- **Unbuilt name consts** (`SceneRouter.cs:182-186`): SunkenBellTower, WolfwardensVigil,
  FrostStair, GlassCathedral, ApothecarysVault — no scene files; entry no-ops via
  `DungeonDef.SceneExists`. Portal arming for real scenes only (`FeatureFlags.cs:414`).

### KayKitChallengeOutpost
Bounded challenge arena built by `KayKitChallengeOutpostBuilder`. `CavePortalRepointInjector`
(`.../World/CavePortalRepointInjector.cs:5-13,43-49,85`) repoints the overworld CavePortal trigger
from old `Outpost1` to it — **only when `ff.raidwalk` is ON**, and per the 08-01 anchor §3b
`raidwalk` is **OFF by default** (its XML doc lies). So the default cave portal still targets
Outpost1. `ChallengeOutpostVictoryController` self-installs the victory/return path the builder
doesn't bake (`ChallengeOutpostVictoryController.cs:3-16,41`).

### ATBBattle
Single-load with fade via `GoBattle(BattleParams)` (`SceneRouter.cs:567+`); stashes a
return-point (scene + hero pose) before the Single load so the round trip returns to where the
player fought, not the Village2 default (`SceneRouter.cs:572-574`). Placeholder combatants are
swapped at runtime (`AtbCombatantSwapper`).

---

## 5. Binary-scene ledger (`.gitattributes` — read before ANY scene git surgery)

`*.unity` is normally `text eol=lf`, BUT batchmode `EditorSceneManager.SaveScene` writes BINARY
even under ForceText (proven 2026-06-30 and again 2026-07-24 — "58% NUL"), so these are forced
`binary` verbatim-stored (more-specific rules win):

- `Assets/Scenes/Outpost1.unity`, `Assets/Scenes/Dungeon.unity`, `Assets/Scenes/Outpost2.unity`
- `Assets/Scenes/DungeonCompose/*.unity` (the Room-Forge composer output — the DGN-1 shared-tree
  corruption class; memory: re-bake DungeonCompose scenes ONLY in an isolated worktree)
- Also binary: `*TerrainData.asset`, `NavMesh-*.asset`, `LightingData.asset` (EOL-mangling by the
  `*.asset text` rule corrupted TerrainData — the 2026-06-30 terrain bug).

---

## 6. Deleted / removed scenes ledger

| Scene | Status (verified 2026-08-02) | Evidence |
|---|---|---|
| `Village.unity` | **DELETED** — absent from disk AND index; excluded-forever guard in the oracle | `Test-Path` false; not in `git ls-files`; `SceneRoutingRegression.cs:22-24,53` fails the gate if a bare "Village" re-enters the build list |
| `OuterWorld.unity` | **DELETED** (WO-608 MergedWorld) — content merged into Main_Castle_Overworld | `Test-Path` false; not in ls-files; `WorldSceneLoader.cs:2-19` deprecation header |
| `DungeonCompose/d4_sunken_crypt.unity` | **PURGED** — 58% NUL shared-tree garble | commit `c5b3461c` "chore(dungeon): purge corrupt d4_sunken_crypt scene" |
| `PatriciaLightMode` | never-built route — Defend-the-Tower module REMOVED 2026-06-09 | `SceneRouter.cs:164` const + `GoPatriciaLight` remain as dead code; oracle reports it as a NOTE, not a failure (`SceneRoutingRegression.cs:26-30`) |

History caveat: `git log --diff-filter=D` does not surface the Village/OuterWorld deletions in
this clone's lineage (last path-touching commit `39a69135` shows an Add) — the repo was re-cloned
fresh 2026-06-16 (CLAUDE.md §0) and scene history predating/straddling that is unreliable. The
disk+index+oracle state above is the truth.

---

## 7. Scene-route oracle — `SceneRoutingRegression`

`Assets/Editor/Regression/SceneRoutingRegression.cs`, registered in `DataRegression.RunAll`
(`DataRegression.cs:285`, tag `[scene-route]`). Headless, data-decidable, seconds. Proves:
1. Every LOAD-BEARING `SceneRouter` route const resolves to an ENABLED build scene (mirrors the
   exact `IsSceneRegistered` runtime gate — a miss is a player STRAND).
2. `SceneRouter.Castle` resolves to a registered scene under **BOTH** `ff.MergedWorld` states
   (drives the real property, restores the pref in finally).
3. `Village.unity` stays excluded (a resurrection = canon regression → FAIL).
Stubbed dungeons + the removed PatriciaLight route are NOTES (dangling-route triage), never
failures. Contract: `Run(out string reason)` covenant-style.

---

## 8. Routing code quick map (unchanged pieces condensed)

- **`SceneRouter`** (`Assets/_Modules/Core/SceneRouter.cs`) — consts §1/§4; `LoadScene` (sync,
  guarded, saves first), `LoadSceneWithFade` (UniTask), `LoadVillageWithLoader` (overlay),
  `GoTitle/GoHeroSelect/GoPetSelect/GoVillage/GoCastle/GoRaid(scene)/GoDungeon(id)/GoBattle(p)`,
  `PendingBattle`, `Fader`.
- **`HubScenes`** (`Core/HubScenes.cs`) — `Names = {Village2, MainCastle_Hall, CastleHub,
  CastleHub_MainKeep, Main_Castle_Overworld}` (line 25); `IsHub` / `IsOverworld` / `IsRaid` /
  `IsEnemyOwnedScene` (reads `Data/Canonical/scene-configs.json` via CanonicalJson, cached) /
  `SuppressTownHud` — the WO-550 chokepoint ~14 panel bootstraps gate on.
- **`SceneTransitionTrigger`** (`Village/World/SceneTransitionTrigger.cs`) — proximity seam
  trigger; MergedWorld-aware (line 458-459); outpost fast-travel gate distinguishes
  Garrison_*/Outpost_*/RaidBase_* destinations (lines 211, 273).
- **`WorldSceneLoader`** — DEPRECATED no-op (§2).
- **`SceneOwnership`** (`Village/SceneOwnership.cs`) — runtime enemy-ownership flag from
  scene-configs; HubScenes carries the HUD-safe mirror.

## 9. Third-party scenes in-tree (not project scenes)

`Assets/Dragon/Scene/Scene.unity` (licensed WDallgraphics dragon rig demo, tracked since WO-760)
and 15 `Assets/Lana Studio/Casual RPG VFX/Demo/Scenes/Demo_*.unity` pack demos. Never add these
to build settings.

---

## 10. Risk ledger / FLAGS

- **FLAG-1 (flag-coupled build entry):** `MainCastle_Hall` (#7) must stay in the build list while
  `ff.mergedworld` can be flipped OFF — the oracle enforces both resolutions; removing either
  scene OR retiring the flag must happen together.
- **FLAG-2 (comment lies, carried):** `SceneRouter.cs` header/legacy comments still narrate the
  Village-ending intro and MainCastle_Hall-as-hub; `FeatureFlags` XML summaries lie on 12 defaults
  (incl. `mergedworld`, `raidwalk`) — trust the property line. `HeroSelect` doc-comments still say
  "pick one of 4 heroes"; the shipped default is single-hero V1 + founding choice.
- **FLAG-3 (dev scenes shipped):** `Dungeon_Demo` (#15) and the chain scenes are in the BUILD
  list — they inflate build size and are name-loadable in a player build via `-bootScene`.
  Deliberate for QA today; revisit before store ship.
- **FLAG-4 (dormant repoint):** the KayKitChallengeOutpost cave repoint is dead while
  `ff.raidwalk` defaults OFF — the walk-up outpost path players hit is still Outpost1.
- **FLAG-5 (unregistered template):** `RaidBase_IronBastion.unity` exists on disk, is nav-baked by
  `RaidNavBake`, but is NOT in build settings — `GoRaid("RaidBase_IronBastion")` would abort.
  Template-only today; register it before pointing any UI at it.
- **FLAG-6 (no by-name reference):** `Garrison_village2_stronghold` appears in the build list but
  a repo-wide grep finds no code/doc reference to the exact scene name — it is reached only via
  derived `Garrison_<recipeId>` naming from the `village2_stronghold` recipe. Confirm liveness
  before pruning; if the Village2 in-scene stronghold (EnemyStrongholdBuilder) fully replaced it,
  this entry is a candidate stale build slot.
- **FLAG-7 (binary scenes):** never hand-edit or renormalize the §5 binary scenes; re-bake
  DungeonCompose scenes only in an isolated worktree (d4_sunken_crypt lesson).
- **FLAG-8 (UXML-in-build, carried):** ATBBattle's BattleHUD UIDocument remains UXML-sourced;
  UXML renders empty in player builds (CLAUDE.md §8) — the uGUI BattleHud9Zone path is the live
  mitigation (`ff.battlehud9zone` ON per anchor §3b).
