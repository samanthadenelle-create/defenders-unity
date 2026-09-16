# Core — `DeNelle.Core` / `DeNelle.AI`

Foundation assembly. Interfaces, enums, pure data, save/state, shared services.
Every other module may reference Core; Core references nothing first-party.

## Subfolders

| Folder | Contents |
|---|---|
| `Combat/` | `IDamageable`, `IDamageableStructure` (implemented by HeartController, HeroHealth, Building, Tower, Gate, WallSegment), `EnemyState`, `DamageAttribution` |
| `HUD/` | `IVillageHud` (implemented by VillageHudController, resolved via `CoreServices.Hud`) |
| `Audio/` | `IAudioService`, `MusicTrack` |
| `State/` | Save system: `GameState`, `GameStateService`, `SaveSchema`, `SaveMigrator`, `PersistenceBridge`, `DifficultyTuning`, `ServerConfig`, `BackendAuthConfig` |
| `Catalog/` | Catalog data model: `CatalogEntry`, `CatalogRegistry`, `CatalogType`, `PlacementRules`, `BuildTimerConfig` |
| `Data/` | Pure data types: battle pass, campaign, missions, pets, skills, towers |
| `Scripts/AI/` | Behavior-tree primitives (`BTNode`, `Selector`, `Sequence`, `ActionNode`, `Condition`) — **separate asmdef `DeNelle.AI`** |
| `Services/` | `ClanService`, `ChatPhraseCatalog` |
| `Progression/` | `SkillSystem`, `IXpEarner`, `XpEarnerRegistry` |
| `World/` | `ZoneManager`, `RegionZone`, `RegionSpawnTable`, `GameClock`, `CrystalGrade`, ward/world content |
| `UI/` | `PanelManager`, `PanelRouter`, `AddressableUIManager`, `ShopTheme`, `ElarionUi` (shared in-game UI theme: palette + UI-Toolkit helpers + swappable `Resources/UI/panel_bg`/`menu_bg` hook) |
| `Quests/` `Promo/` `Referral/` `Analytics/` | DailyQuests, promo codes, referrals, EventTracker |
| `Backend/` | `BackendRequestSigner`, `IWalletSigner`, `IJupiterService` (interfaces only; impls in Wallet/Web3 modules). ⚠ **Was `Web3/` until WO-1755 (2026-09-15)** — folder AND namespace (`DeNelle.Core.Web3` → `DeNelle.Core.Backend`) were renamed together because IL2CPP writes the SOURCE PATH into `global-metadata.dat`, so the folder name was itself a live `web3` hit in the Google Play artifact |
| `Addressables/` | Group config, memory profiler, `SkinController` |
| `Events/` `Theme/` `Debug/` | DialogueEventBus, Theme, DebugCanvasUI |
| `Tests/` | Save/load round-trip, migrator, schema validation |

## Root files of note

`CoreServices.cs` (service locator — Hud/Audio), `SceneRouter.cs`, `Constants.cs`,
`GameStateBootstrap` (in State/), `DevBootScene.cs`, `IntroLauncher.cs`.

> Maintenance: update this README when files are added/removed.
