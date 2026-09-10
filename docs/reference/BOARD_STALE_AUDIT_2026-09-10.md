# BOARD STALE AUDIT - 2026-09-10

**What this is.** On 2026-09-10 fifty-seven `WorkOrders/*.md` files still carried a first
`**Status:**` line reading `IMPLEMENTED - 2026-09-0x uncommitted, awaiting gate` (ten of them
`PARTIALLY IMPLEMENTED`), while `git status --short` showed the tree clean apart from
`tools/web-ship.ps1` and `Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset`.
"Uncommitted" was therefore stale for every one of them: the work had either landed in a commit
or had never been written. This ledger records which, with the git proof.

**Method (CLAUDE.md sec.11B - nothing below is inferred).**

1. Claimed files harvested from each WO body plus its `.RESULT.md`, restricted to paths tracked
   in `HEAD`.
2. Pre-window baseline `05cfd4d97` (2026-09-05, the last commit before the 09-06 lane hand-backs).
   A file is "moved" only if it appears in `git diff --name-only 05cfd4d97 HEAD`.
3. **Proof of landing:** the ticket's own `WO-<n>` tag located in a *code or tooling* file in
   `HEAD` (`Assets/ api/ tools/ .claude/ .githooks/ ProjectSettings/ publishing/ web/ dev/`) that
   also appears in that diff, then `git log -S"WO-<n>" --format="%h %as" -1 HEAD -- <file>` to
   name the carrying commit. Fifty-four of fifty-seven were proven this way.
4. Three tickets carried no `WO-<n>` tag in code and were proven by hand at source
   (WO-1498, WO-1508, WO-1514 - see the notes under the table).
5. **No ticket claimed a file that was dirty when this lane started**, so none of the fifty-seven
   was genuinely still uncommitted.
6. ! THE TREE MOVED WHILE THIS LANE RAN. The dirty check in step 5 was taken at lane start
   (`tools/web-ship.ps1` + `ElarionLocaleFallback.asset`). By the time the ledger was written
   `tools/web-ship.ps1` was clean again and `.claude/skills/run-defenders/f8-watch-daemon.ps1`
   had gone dirty from another seat's live F8 work. That file is named by WO-1531, whose proof
   rests on `.claude/skills/run-defenders/f8-device-bridge.ps1` at `c0c30f715` and is unaffected.
   **Re-run `git status --short` before staging.**

**Gate evidence read this session:** `Builds/wave1-reg3` (written 2026-09-09 23:57) carries
`REGRESSION_OK 492/492 suites -- 492 green, 0 red, 0 skipped`, read out of the UTF-16 log, not
copied from a doc.

**Deviation from the requested template, declared (CLAUDE.md sec.11B.B).** The audit request
specified `IMPLEMENTED - <sha> on HEAD 2026-09-09 (was: ...)` (WO-1414's shape). These lines were
written as `IMPLEMENTED - <sha> on HEAD, landed <date> (was: ...)` instead, because 54 of the 57
landed on **2026-09-07**, not 09-09, and stamping 09-09 would have put a wrong date on the board.
To normalise back to WO-1414's shape, a single sed over the first `**Status:**` line would do it.

**Verdict counts.** 57 flipped to IMPLEMENTED-on-HEAD (47 `IMPLEMENTED`, 10 `PARTIALLY
IMPLEMENTED`, the PARTIALLY leads and their remainder notes preserved). **0** flipped back to
`READY TO IMPLEMENT` - every 09-06/09-07 hand-back reached the tree.

**Carrying commits.** The wave-three commit `d6511b8e5` (2026-09-07) carries the bulk; `c0c30f715`
(2026-09-07, wave-four), `bea5c8240`, `cd57a1c1e` and `55d3a7c56` (2026-09-07) carry the rest.
What was measured: for every one of the fifty-seven, the commit that INTRODUCED the proving tag or
symbol in the proving file predates the 09-09 checkpoint `6a5c7a36d` named in the audit request. The
checkpoint may still have carried later touches to the same files; it is not the commit that first
put any of these changes in the tree.

## Table

| WO | claimed files (first 4) | proving file | proving sha | date | verdict |
|---|---|---|---|---|---|
| WO-1445 | Assets/_Modules/Village/Harvest/OfflineHarvestService.cs | `Assets/_Modules/Core/UI/HarvestResultVM.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1447 | Assets/_Modules/Core/State/GameStateService.cs, api/game/load.js | `Assets/_Modules/Core/State/GameStateService.cs` | `bea5c8240` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1448 | Assets/_Modules/Core/State/PersistenceBridge.cs, Assets/_Modules/Core/State/GameStateService.cs, Assets/Editor/Regression/CloudLoadRestoreRegression.cs | `Assets/_Modules/Core/State/GameStateService.cs` | `bea5c8240` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1451 | Assets/_Modules/Village/UI/TowerPreviewCamera.cs, Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs, Assets/Editor/Regression/PreviewRenderTextureSamplesRegression.cs, Assets/_Modules/Village/Hero/HeroPreviewViewer.cs | `Assets/_Modules/Village/UI/TowerPreviewCamera.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1454 | Assets/_Modules/Core/Web3/BackendRequestSigner.cs, Assets/Editor/Regression/BackendSaveAuthRegression.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs, Assets/Editor/Regression/NightMarketNoWalletRegression.cs | `Assets/_Modules/Core/State/GameStateService.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1455 | Assets/_Modules/Core/State/GameStateService.cs, Assets/Editor/Regression/BackendSaveAuthRegression.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs, Assets/Editor/Regression/NightMarketNoWalletRegression.cs | `Assets/_Modules/Core/State/GameStateService.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1462 | Assets/_Modules/Village/Hero/RaidDeployScreen.cs, Assets/Editor/Regression/RaidSelectionLayoutRegression.cs | `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1463 | Assets/_Modules/Village/Troops/RaidDeployController.cs, Assets/Editor/Regression/RaidSelectionLayoutRegression.cs, Assets/_Modules/Core/UI/ElarionUi.cs | `Assets/_Modules/Village/Troops/RaidDeployController.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1464 | Assets/_Modules/Village/Troops/RaidDeployController.cs, Assets/_Modules/Core/UI/HudLayoutBands.cs, Assets/_Modules/HUD/Kit/HudAreasHost.cs, Assets/_Modules/Village/Troops/RaidHudController.cs | `Assets/_Modules/Core/UI/HudLayoutBands.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1465 | Assets/_Modules/HUD/Kit/HudKitController.cs, Assets/Editor/Regression/HudUiRegression.cs | `Assets/_Modules/HUD/Kit/HudKitController.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1466 | Assets/Editor/Regression/HudLabelFitRegression.cs, Assets/_Modules/HUD/Kit/HudKitController.cs | `Assets/_Modules/HUD/Kit/HudKitController.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1468 | Assets/_Modules/HUD/Kit/HudKitController.cs, Assets/Editor/Regression/HudUiRegression.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs, Assets/Editor/Regression/NightMarketNoWalletRegression.cs | `Assets/_Modules/HUD/Kit/HudKitController.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1471 | Assets/_Modules/Core/UI/HarvestOverflowModal.cs, Assets/_Modules/Core/UI/FocusedModalHost.cs, Assets/_Modules/Core/UI/ObsidianNavigationWorkspace.cs, Assets/Editor/Regression/WorldHoldLivenessRegression.cs | `Assets/_Modules/Core/UI/FocusedModalHost.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1472 | Assets/_Modules/Cosmetics/CosmeticApplier.cs, Assets/_Modules/Cosmetics/CosmeticOwnershipService.cs, Assets/Editor/Regression/CosmeticApplyRegression.cs | `Assets/_Modules/Cosmetics/CosmeticApplier.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1473 | Assets/Editor/Regression/VfxLoopFlagRegression.cs, Assets/_Modules/Village/Vfx/VFXManager.cs, Assets/Resources/VFX/VFXCatalog.asset | `Assets/_Modules/Village/Vfx/VFXManager.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1474 | Assets/_Modules/Village/Harvest/EchoBonusCalculator.cs, Assets/_Modules/Village/Harvest/EchoBalanceCatalog.cs, Assets/Resources/Data/Canonical/echoes-balance.json, Assets/Editor/Regression/EchoSpecializationRegression.cs | `Assets/Resources/Data/Canonical/echoes-balance.json` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1476 | Assets/_Modules/Village/Vfx/AmbientAuraPolicy.cs, Assets/_Modules/Village/Heart/HeartAuraController.cs, Assets/Editor/Regression/VfxLoopFlagRegression.cs | `Assets/_Modules/Village/Heart/HeartAuraController.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1478 | Assets/Editor/UICaptureLaunch.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs, WorkOrders/WORK_ORDER_1010_build_ui_carousel_minimize.md, WorkOrders/WORK_ORDER_1411_build_never_says_what_you_can_afford.md | `Assets/Editor/UICaptureLaunch.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1480 | Assets/_Modules/Core/Catalog/RepoProps.cs, Assets/_Modules/Village/Walls/WallSegment.cs, Assets/Editor/Regression/BuildEconomyRegression.cs | `Assets/_Modules/Village/Walls/WallSegment.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1486 | tools/r2-ship.ps1 | `Assets/Editor/AndroidBuild.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1492 | tools/board_build.py, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs, Assets/Editor/Regression/NightMarketNoWalletRegression.cs | `tools/board_build.py` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1493 | Assets/Editor/Regression/SessionRegression.cs, tools/regression/checkin_gate.ps1, Assets/Editor/Regression/RegressionMarkerRegression.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs | `tools/regression/checkin_gate.ps1` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1495 | Assets/Editor/Regression/AllowlistExpiryRegression.cs, Assets/Editor/Regression/DataRegression.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs, Assets/Editor/Regression/NightMarketNoWalletRegression.cs | `Assets/Editor/Regression/AllowlistExpiryRegression.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1496 | Assets/Editor/Regression/DataRegression.cs, Assets/Editor/Regression/RegressionMarkerRegression.cs, Assets/Editor/Regression/DestroyedStructureRegression.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs | `Assets/Editor/Regression/ArenaCombatOracle.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1498 | Assets/_Modules/Core/UI/VillageLoadOverlay.cs, Assets/_Modules/Onboarding/CanonStrings.cs, Assets/Editor/Regression/GlossaryRegression.cs, Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs | `Assets/Editor/Regression/GlossaryRegression.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1499 | Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs, Assets/Editor/Regression/AwaySummaryReportRegression.cs | `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1508 | Assets/_Modules/Village/Enemies/Enemy.cs, Assets/Editor/Regression/EnemyProbeCadenceRegression.cs | `Assets/_Modules/Village/Enemies/Enemy.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1510 | Assets/_Modules/Core/Bridging/IVillageBridge.cs, Assets/_Modules/Village/VillageBridgeService.cs, Assets/Editor/Regression/CoreReflectionSourceRegression.cs | `Assets/_Modules/Core/Bridging/IVillageBridge.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1511 | Assets/_Modules/Village/Arena/BattleArena.cs, Assets/_Modules/Village/Diagnostics/CastleNavTopologyDiag.cs, Assets/_Modules/Village/VisualFactory.cs, Assets/_Modules/HUD/HelpMenu.cs | `Assets/_Modules/Audio/AudioBootstrap.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1512 | docs/ARCHITECTURE_PRINCIPLES.md, Assets/Editor/Regression/UiMvvmConformanceRegression.cs | `Assets/_Modules/HUD/AdminOverlay.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1513 | ProjectSettings/TagManager.asset, Assets/Editor/Regression/StructureTargetableRegression.cs | `Assets/_Modules/Village/Camera/CinemachineCameraController.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1514 | _(silo line only)_ | `dev/tmp/commit_lanes.py` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1515 | Assets/_Modules/Village/UI/Defense/DefenseReportPanel.cs, Assets/Editor/Regression/DefenseReportLayoutRegression.cs, Assets/Editor/Regression/DataRegression.cs | `Assets/_Modules/Core/HudModel/DefenseReportChipModel.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1519 | Assets/Resources/RpgUi/emblem/Necromancer.png, Assets/_Modules/Village/Hero/RaidDeployScreen.cs, Assets/_Modules/Village/Hero/RaidDeployVM.cs, Assets/Editor/Regression/RaidDeployLayoutRegression.cs | `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1520 | Assets/Editor/WallTools/RaidBaseGenerator.cs, Assets/Editor/Regression/RaidStagingMarkerRegression.cs, Assets/Editor/Regression/DataRegression.cs | `Assets/Editor/WallTools/RaidBaseGenerator.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1521 | Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs, Assets/_Modules/Core/Quests/DailyQuests.cs, Assets/_Modules/Village/Quests/DailyQuestRewardBridge.cs, Assets/_Modules/Village/Hero/RumorBoardVM.cs | `Assets/Editor/UICaptureLaunch.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1522 | Assets/Editor/Regression/SkillsPanelLayoutRegression.cs | `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1523 | Assets/Editor/Regression/CosmeticShopReachabilityRegression.cs | `Assets/_Modules/Core/HudModel/CosmeticSignals.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1524 | Assets/_Modules/Pets/Pet.cs, Assets/Editor/Regression/DataRegression.cs, Assets/_Modules/Core/Combat/CombatFactionRules.cs | `Assets/_Modules/Pets/Pet.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1525 | Assets/_Modules/Core/UI/HarvestOverflowModal.cs, Assets/_Modules/Core/UI/HarvestResultVM.cs, Assets/Editor/Regression/HarvestResultShapeRegression.cs | `Assets/_Modules/Core/UI/HarvestOverflowModal.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1530 | docs/RAID_BALANCE_AUDIT_2026-09-06.md, Assets/_Modules/Village/World/Camps/GarrisonStatBlocks.cs, Assets/StreamingAssets/Data/Canonical/scene-configs.json, Assets/StreamingAssets/Data/Canonical/enemies.json | `Assets/_Modules/Village/World/Camps/GarrisonController.cs` | `d6511b8e5` | 2026-09-07 | PARTIALLY-IMPLEMENTED-on-HEAD |
| WO-1531 | .claude/skills/run-defenders/f8-device-bridge.ps1, .claude/skills/run-defenders/f8-watch-daemon.ps1 | `.claude/skills/run-defenders/f8-device-bridge.ps1` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1536 | Assets/StreamingAssets/Data/Canonical/enemies.json | `Assets/Resources/Data/Canonical/enemies.json` | `cd57a1c1e` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1537 | Assets/_Modules/Village/Buildings/Building.cs, Assets/_Modules/Village/Walls/WallSegment.cs, Assets/Editor/Regression/RepairProbeRegression.cs, Assets/_Modules/Village/Walls/RepairTarget.cs | `Assets/_Modules/Village/Walls/RepairTarget.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1538 | Assets/_Modules/Village/Vfx/WeaponTrailController.cs, Assets/Editor/Regression/ArenaCombatOracle.cs | `Assets/_Modules/Village/Vfx/WeaponTrailController.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1540 | Assets/_Modules/Core/FeatureFlags.cs, Assets/Resources/Data/Canonical/structures-catalog.json, Assets/Editor/CastleHubBuilder.cs, Assets/_Modules/Village/BuildMode/StrategicPlacementMigration.cs | `Assets/Editor/Regression/BlankStartCensusRegression.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1542 | Assets/_Modules/Village/Hero/RaidSelectionScreen.cs, Assets/_Modules/Village/Hero/RaidSelectionVM.cs, Assets/_Modules/Village/Hero/RaidDeployScreen.cs, Assets/Editor/Regression/RaidSelectionSpoilsRegression.cs | `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1543 | Assets/_Modules/Village/UI/EndState/EndStateView.cs, Assets/_Modules/Village/World/Camps/RaidVictoryController.cs, Assets/_Modules/Village/UI/EndState/EndStateVM.cs, Assets/Editor/Regression/RaidPayoutVisibilityRegression.cs | `Assets/_Modules/Village/Troops/RaidDeployController.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1561 | Assets/_Modules/Village/Troops/RaidDeployController.cs, Assets/_Modules/Village/UI/EndState/EndStateVM.cs | `Assets/_Modules/Village/Troops/RaidDeployController.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1562 | Assets/_Modules/Village/World/Camps/RaidVictoryController.cs, Assets/_Modules/Village/Hero/RaidSelectionScreen.cs, Assets/_Modules/Village/Hero/RaidSelectionVM.cs, Assets/_Modules/Village/World/Camps/RaidClaimService.cs | `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs` | `d6511b8e5` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1568 | Assets/_Modules/Dungeons/RoomForge/CommonDungeonDoor.cs, Assets/Editor/RoomForge/DefaultDungeonRoomsBuilder.cs, Assets/_Modules/Dungeons/RoomForge/RoomForgeCanon.cs, Assets/Editor/DungeonSceneBuilder.cs | `Assets/Editor/DungeonSceneCapture.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1569 | _(silo line only)_ | `Assets/_Modules/Village/Buildings/DefenseTower.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1570 | Assets/Resources/Data/Canonical/building-tiers.json, Assets/_Modules/Village/BuildMode/StructureCardVM.cs, Assets/Editor/Regression/BuildEconomyRegression.cs | `Assets/_Modules/Village/BuildMode/StructureCardVM.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1571 | Assets/Editor/Regression/ManageBuildDoorRegression.cs | `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1572 | Assets/Resources/Data/Canonical/card-collections.json | `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs` | `c0c30f715` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1581 | Assets/Editor/Regression/PlacedStructureDoorRegression.cs, Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs, Assets/_Modules/Village/BuildMode/BuildModeController.cs | `Assets/_Modules/Village/BuildMode/BuildModeController.cs` | `55d3a7c56` | 2026-09-07 | IMPLEMENTED-on-HEAD |
| WO-1583 | Assets/_Modules/Core/Web3/BackendRequestSigner.cs, Assets/_Modules/Wallet/WalletSkinBootstrap.cs, Assets/_Modules/Core/State/GameStateService.cs, Assets/Editor/Regression/BackendSaveAuthRegression.cs | `Assets/Tests/EditMode/LoginSurfacePlatformTests.cs` | `55d3a7c56` | 2026-09-07 | IMPLEMENTED-on-HEAD |

## Notes on the three hand-proved tickets

- **WO-1498** (retired tagline) - no `WO-1498` token in code. Proved at source instead:
  `Assets/_Modules/Core/UI/VillageLoadOverlay.cs:63` records the removed line, and
  `Assets/Editor/Regression/GlossaryRegression.cs:551` now scans `*.cs` with
  `SearchOption.AllDirectories` - a line that returns **0 hits** in
  `git show 05cfd4d97:Assets/Editor/Regression/GlossaryRegression.cs`. Both files' newest commit is
  `d6511b8e5` (2026-09-07).
- **WO-1508** (probe cadence) - proved by `_nextProbeAt` at
  `Assets/_Modules/Village/Enemies/Enemy.cs:279,1935,1940` plus the dedicated oracle
  `Assets/Editor/Regression/EnemyProbeCadenceRegression.cs`, neither present at the baseline;
  `git log -S"_nextProbeAt" -1` names `d6511b8e5` (2026-09-07). Kept **PARTIALLY** - the ticket's
  own remainder note is preserved.
- **WO-1514** (hardcoded repo roots) - the silo is untracked scratch plus `dev/tmp/*.py`.
  `git grep -n 'r.[CD]:\\eoa' HEAD -- '*.py' '*.ps1'` returns **zero**; the nine
  `dev/tmp/*.py` scripts carry `ROOT = str(Path(__file__).resolve().parents[2])` in `HEAD`;
  `git log -1 -- dev/tmp/commit_lanes.py` = `d6511b8e5` (2026-09-07). The two untracked root
  scripts are gone from disk. Kept **PARTIALLY** - the "one resolver helper" acceptance clause is
  still open per its own `.RESULT.md`.

## Caveats a reader should carry

- The proof establishes that **the ticket's change is on HEAD**, named by the commit that
  introduced the tag or symbol in that file. It does **not** re-judge whether the change is
  *correct* - that is the owner's felt test, which is what every flipped line now says it awaits.
- Ten tickets keep a `PARTIALLY IMPLEMENTED` lead. Their remainder is unchanged and still owed;
  the flip only corrects the word "uncommitted".
- Nine tickets whose first status line already read `CLOSED 2026-09-07 ...` and WO-1414, whose line
  was already flipped on 09-09, were **excluded**: in those the phrase "uncommitted, awaiting gate"
  survives only inside a `(was: ...)` history note, which is a record, not a claim.
