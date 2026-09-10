# Master Catalog — Editor Tools & Gates (`Assets/Editor`) — 2026-08-02

Verified **from the actual code** (not comments) at HEAD `b77a178e` on
`wip/village2-and-f8-tickets`. Supersedes the 2026-06-12 body and its 2026-07-22
STALE banner entirely. `Assets/Editor` = ~200 top-level `.cs` + subtrees
(`Regression/` 82, `RoomForge/` 6, `Catalog/`, `HudComposer/`, `AutoPilot/`,
`Audio/`). Assemblies: the main tree is `DeNelle.Editor.asmdef`; the Regression
tree has its OWN asmdef **`DeNelle.EditorRegression`** (rootNamespace
`DeNelle.Editor`; only `RoomForgeRegression` uses `DeNelle.Editor.Regression`);
`RoomForge/` editor files use namespace `DeNelle.Editor.RoomForge`. This page catalogs the QA/gate
chain, capture harness, builders, importers, animator factories, scene/world
builders, VFX authoring, and the root run scripts that drive them.

"**DESTRUCTIVE**" = mutates scenes/assets/Build Settings. "**SAFE**" = read-only
or writes only reports/markers.

---

## DELTA 2026-09-09 — 18 new suites registered in DataRegression.RunAll

Eighteen new regression suites registered on 2026-09-09, landing across two major gates (COMPILE_GATE_OK + REGRESSION_OK) and several orchestration lanes. All are edit-mode, read-verified from RESULT files, registered once in `DataRegression.RunAll` (line 1930+). All emit their own `_OK` / `_FAIL` markers (read from log, never from this line). Tags in commit order:

- [shop-preview-loader-branch] (WO-1096, SHOP) — `PartyShopPreviewLoaderBranchRegression`
- [hero-playable-bounds] (WO-1094, LOCOMOTION) — `HeroPlayableBoundsRegression`
- [staff-grip-seat] (WO-1431, HERO-GRIP) — `StaffGripSeatRegression`
- [raid-watchdog-honor] (WO-1095 + WO-1594, RAID) — `RaidWatchdogHonorRegression`
- [offline-cache-repair] (WO-1092, CACHE) — `OfflineCacheRepairRegression`
- [harvest-overcap-copy] (WO-1099, HARVEST-COPY) — `HarvestOverCapCopyRegression`
- [store-return-to-manage] (WO-1412, STORE-RETURN) — `StoreReturnToManageRegression`
- [ftue-modal-deferral] (WO-1090, PINS) — `FtueModalDeferralRegression`
- [biome-drop-ground-probe] (WO-1091, PINS) — `BiomeDropGroundProbeRegression`
- [rep-chase-leash] (WO-1093, PINS) — `RepChaseLeashRegression`
- [enemy-asset-type-screen] (WO-1097, LOADER) — `EnemyAssetTypeScreenRegression`
- [place-latch-trace] (WO-1615, PLACE) — `BuildPlaceLatchTraceRegression`
- [play-metadata-identifiers] (WO-1377, META) — `PlayMetadataIdentifierRegression`
- [spoils-bankable] (WO-1461, SPOILS) — `SpoilsAreBankableRegression`
- [raid-spire-siege] (WO-1617, BALLISTA) — `RaidSpireSiegeRegression`
- [troop-shield-seat] (WO-1616, NPC-SHIELD) — `TroopShieldSeatRegression`
- [authored-field-gate] (WO-1430 Group B, FIELDS) — `AuthoredFieldGateRegression`
- [raid-rough-stone] (WO-1373, RAID-3) — `RaidRoughStoneDropRegression`

---

## DELTA 2026-08-21 — 11 new oracles + 2 new shared harness helpers under `Assets/Editor/Regression/`

Read from source 2026-08-21. All are edit-mode, `NEVER throws`, registered ONCE in
`DataRegression.RunAll`, and emit their own `_OK` / `_FAIL` marker. **Read the suite COUNT off the
marker line on a fresh log — never off this file** (CLAUDE.md §8).
`DeNelle.EditorRegression.asmdef` was modified in the same wave to carry the new references.

### Two SHARED helpers — new, and both exist to stop a whole class of false result

- **`SourceLint.cs` (176)** `DeNelle.Editor.Regression` — reads a runtime `.cs` as **CODE ONLY**,
  with comments AND string literals stripped. ⛔ **That stripping is the entire point:** these files
  DOCUMENT the very symbols the pins look for, so an unstripped grep is satisfied by a comment or a
  log message and the pin passes while the call site is absent. Use it for invariants about WHERE
  and in WHAT ORDER a call happens — no runtime assertion can see call order.
- **`HeadlessState.cs` (82)** `DeNelle.Editor` — installs a throwaway `GameState` for an edit-mode
  oracle. ⭐ **Editmode batchmode NEVER runs `GameStateService.Awake`** (Awake fires only in play
  mode / `ExecuteAlways`), so a bare `AddComponent<GameStateService>()` leaves both `Instance` and
  `State` null — the historic cause of the false-FAIL "no GameStateService/State available". It sets
  the private static `_instance` and the `[SerializeField] _state` by reflection, exactly as Awake
  would. Pattern lifted verbatim from `OfflineHarvestRegression` / `CoreSaveContractRegression`.

### New oracles

| File (lines) | Tag / markers | What it pins |
|---|---|---|
| `SiegeCadenceRegression.cs` (205) | `[siege-cadence]` | Drives `SiegeScheduler` against a CONTROLLED clock, no scene: a fresh cadence clock (`<= 0`) SEEDS FORWARD and banks nothing; a long absence CLAMPS to `_maxPendingSieges`; the HARVEST clock (`LastHarvestClaimMs`) is untouched across a window. |
| `SiegeSpawnAuthorityRegression.cs` (288) | `[siege-spawn-authority]` | SOURCE-SCANNING lint (precedent: `HubSceneLiteralRegression`, `BannedVfxRegression`). Fails the gate if a spawn call appears in the siege files, or if a SECOND file writes `AttackerSource.GeneratedPve`. Exists because two systems that both attack the town look fine in isolation and drift apart forever. |
| `DefenseReportContractRegression.cs` (722) | `[defense-report]` | Five pins, headline two: a fully-populated `DefenseOutcomeRecord` round-trips through `SaveSchema.JsonSettings` field-for-field (a report that loses its breaches on reload is worse than none — the player redesigns against a lie); and the **model-(c) proof** — the same record with `Attacker.Source = GhostSnapshot` renders identically, so the ghost path needs no reader change. |
| `RaidCooldownRegression.cs` (622) | `[raid-cooldown]` | Part BEHAVIOURAL (drives the real `DeNelle.Village` statics through a real save/load round trip) and part SOURCE-LINT via `SourceLint`, so a symbol named only in a comment or a log string can never satisfy a pin. |
| `BattleMonthlyRegression.cs` (1182) | `[battle-monthly]` | The pay-to-win firewall of the Battle Pass + Monthly Ledger families, AS A BUILD GATE rather than a review checklist. |
| `ItemDropMoteIdentityRegression.cs` (437) | `[drop-mote]` | Every dropped id resolves a DISTINCT silhouette. Pins the defect that `ItemPickupSpawner` spawned one hardcoded gold sphere for every drop, and enforces the colourblind law (identity rides SHAPE, never tint). |
| `BreakableContainerChestRegression.cs` (720) | `CHEST_OK` / `CHEST_FAIL` | The chest is OPENED, never attacked: no `IDamageable`/`IDamageableStructure`, no `Hostile` faction, no "Enemy" layer rewrite, and the open is gated out of combat. |
| `CosmeticApplyRegression.cs` (669) | `COSMETIC_APPLY_OK/_FAIL` | An equipped cosmetic REACHES A RENDERER. Written because `CosmeticApplier.ApplyCosmetic` was **called from nowhere** and the component's GUID sat on ZERO prefabs and ZERO scenes while every part of the economy around it worked (WO-992). ⚠ **This is the suite in which 6 hollow passes were found — 1 caught by the ratchet, 5 missed. See WO-1138 in the master risk ledger.** |
| `RaidWallMaterialRegression.cs` (226) | `RAID_WALL_MATERIAL_OK/_FAIL` | Asset-lint: raid-base wall art must be reachable from TRACKED assets, not from an FBX-embedded material bound by ABSOLUTE PATH into a `.fbm` folder on the original author's machine. **It FAILED on its first ever run — that failure is real, pre-existing debt, and is now WO-1135.** |
| `AssetRootsRegression.cs` (415) | `ASSET_ROOTS_OK/_FAIL` | The gate `AssetRoots.cs:46` had CLAIMED it had since 2026-08-18 ("change this one line to relocate the tree; do NOT reintroduce the literal") but nothing enforced. |
| `OfflineAccrualTrustRegression.cs` (218) | — | WO-1128's CLIENT half: every offline window records WHICH CLOCK produced it and its own endpoints, so a server can reconcile it. ⚠ **The REFUSAL is server-side** (`api/game/save.js` §RECONCILE, self-tested by `node api/game/save.js`, marker `ACCRUAL_RECONCILE_OK`). Unity cannot execute JavaScript, so the clamp is deliberately NOT asserted here — pretending otherwise would be a gate that proves nothing. |
| `EconomySinkCapRegression.cs` | — | WO-1129 convex Finish-Now pricing; hard-FAILs at exponent `e >= 1` so the word "convex" cannot be used to undo the ruling. |

Also new this wave: `Assets/Editor/RaidBaseMatDiag.cs`, `Assets/Editor/WallTools/RaidWallMaterialFixer.cs`,
`Assets/Editor/DungeonStatusDevMenu.cs`.

---

## 1. THE GATE CHAIN (how a change gets proven)

Canonical cycle (`.claude/skills/run-defenders/SKILL.md:31-38`); every step runs
through `run-unity-method.ps1` (batchmode, editor CLOSED):

| # | Gate | Invoke | Proof marker (grep the LOG, never the exit code) |
|---|---|---|---|
| 1 | Compile | `run-unity-method.ps1 -Method DeNelle.Editor.CompileGate.Run -LogName compile-gate.log` | `COMPILE_GATE_OK :: scripts compiled clean` |
| 2 | Data regression (**THE gate**) | `run-unity-method.ps1 -Method DeNelle.Editor.DataRegression.RunAll -LogName data-regression.log` | `REGRESSION_OK <n>/<n> suites` / `REGRESSION_FAIL: <n> failure(s)` |
| 2b | Check-in battery (22 cases: scenes/NavMesh/lints) | `run-unity-method.ps1 -Method DeNelle.Editor.RegressionSuite.RunAll -LogName regression.log` | `CHECKIN_SUITE_OK <p>/<n> cases` / `CHECKIN_SUITE_FAIL` |
| 3 | Unit tests (Tier 2) | `run-tests.ps1 [-Platform EditMode\|PlayMode]` | `TESTS_OK :: <p>/<t> passed` (judged from NUnit XML) |
| 4 | UI pixels | `run-unity-method.ps1 -Method DeNelle.Editor.UICaptureLaunch.RunCaptureHeadless -LogName ui-capture.log` | `UI_CAPTURE_OK <count>` + open the PNGs |
| 5 | Player build | `build-windows.ps1` / `AndroidBuild.BuildSeekerApk` / `WebGLBuild.BuildWebGL` | `[build] SUCCESS -> ...exe` / `[AndroidBuild] SUCCEEDED` / `[WebGLBuild] SUCCEEDED` |
| 6 | Runtime fleet | `run-autopilot-fleet.ps1 -Count N` (player exe, licence-free) | aggregated ticket list via `AutoPilotTickets.Emit` |

**LAW (memory `gates-report-success-without-proving-it`):**
`run-unity-method.ps1` judges success by `'Exiting batchmode successfully|terminate with return code 0'`
(`run-unity-method.ps1:76`) and exits 0 on that alone (`:87`) — a gate that
REFUSES (marker withheld) or a `REGRESSION_FAIL` logged via `Debug.LogError`
still exits 0. **Always verify the step's own marker + log freshness/size.**

---

## 2. QA / GATE TOOLS

### CompileGate — `Assets/Editor/CompileGate.cs`
The authoritative "does the tree compile" check. Batchmode open forces the full
recompile; if scripts compile, `Run()` (`:23`) executes and, before printing the
marker, runs TWO static scans:
- **NUL-byte scan** (WO-434, `ScanForNulBytes` `:63`): every `Assets/**/*.cs` is
  byte-scanned for embedded/trailing `\x00` (mount-garble, CLAUDE.md §0). Any
  offender → `LogError` + **marker withheld** (`:29-41`).
- **Brace-balance scan** (`ScanBraceBalance` `:120`): counts `{`/`}` **outside
  strings/char literals/line+block comments** (`BraceBalanced` `:149-203`) — the
  CLAUDE.md §1 gate, automated, without lint false positives. Mismatch →
  marker withheld (`:43-52`).
- Marker: `COMPILE_GATE_OK :: scripts compiled clean` (`:54`). **SAFE**. WIRED/LIVE.

### DataRegression — `Assets/Editor/Regression/DataRegression.cs` (2586 lines)
**THE regression gate.** `RunAll()` (`:36-390`, no MenuItem — batchmode only)
loads the REAL canonical catalogs through the SAME loaders the game uses and
validates the resulting objects, then fans out to the sibling suite tree.
Single verdict: **`REGRESSION_OK <n>/<n> suites`** or `REGRESSION_FAIL: <n> failure(s)`
— failures are emitted via `Debug.LogError` so they land in `break-log.jsonl` too.
The count is on the marker line ON PURPOSE (2026-08-02): the registered suites live
between the `>>> REGISTERED ORACLE SUITES — START/END FENCE <<<` comments and are
counted at runtime, so a log can never be mistaken for a smaller suite's pass.
**New suite registrations go ABOVE the END fence.**

**26 inline checks** called from `RunAll` (top-of-file gear checks inline at
`:42-81`, then):

| Check (file:line of definition) | Verifies |
|---|---|
| gear inline (`:42-81`) | weapons/armor.json → non-zero defs, non-blank id/name, non-empty vendor stock |
| `CheckAbilities` `:761` | abilities.json loadouts per class; default 'mage' non-empty |
| `CheckEnemies` `:804` | every enemies.json row's model resolves via `EnemyFactory.ModelForEnemy` → `Resources/Enemies/<model>` prefab (tinted-capsule class of bug) |
| `CheckWaveScaling` `:876` | default WaveScalingCurve escalates past wave 1 + every enemy pays xp/coin |
| `CheckStructures` `:944` | structures-catalog.json entries' `visualPrefabPath` all Resources-load |
| `CheckSingletons` `:1018` | `repo.singleton`/`bakedTwins` shape + dual-copy parity + migration census |
| `CheckBlankTownGate` `:1172` (called `:137`) | **WO-834** (owner F8 seq 592): blank "Build Your Own" founding must NOT surface baked default-town structures — surfacing truth table + v35→v36 migrator seed + source-lint of every surfacing path |
| `CheckNpcModels` `:1275` (called `:146`) | **WO-818**: `repo.npcModel` dual-copy parity; every slug resolves to a STAGED FBX under `Resources/NPCs/KayKit/`; the 12 owner rows verbatim; **WO-833**: `KayKitNpcIdle.controller` exists with ≥1 clip. Markers `NPC_MODELS_OK/FAIL` (`:1398-1400`) |
| `CheckBuildings` `:1406` | buildings.json non-zero + display fields (Model is a KayKit KEY, deliberately not path-asserted) |
| `CheckBarracksProgression` `:1433` (+`CheckCurveBaseline` `:1518`) | barracks/troop-upgrades/troops reconcile; emits `BARRACKS_PROGRESSION_OK` |
| `CheckPopulationMilestones` `:1627` | echo slots ascend 2..5 no gaps |
| `CheckGuideContent` `:1587` | guide-content.json non-blank sections |
| `CheckDialogueSpeakers` `:1533` | every speaker has name+affiliation; declared portraits load |
| `CheckItemCapabilities` `:1686` | ITEM_MODEL.md §2c capability invariants |
| `CheckCraftingChain` `:1771` / `CheckJewelerChain` `:1881` / `CheckTalentLayout` `:2040` | crafting/jeweler/talent data chains |
| `CheckArmedHeroInvariant` `:2096` | level-1 auto-equip resolves per class (Addressables-scale WO-425 guard) |
| `CheckHandSlotRules` `:2159` | REAL GearLoadout 2H/off-hand/1H+shield exclusivity |
| `CheckBattleClosing` `:2293` | victory/defeat clips Resources-load + star rating math |
| `CheckWeaponVfx` `:2361` / `CheckArmorVfx` `:2485` | rarity→trail-color / rim-light maps distinct + monotonic |
| `CheckAccessories` `:2448` | 10 AccessoryDefs, stat caps, iconPath |
| `CheckVendorStock` `:538` | WO-598 honest shelf: coverage, emptyLine, roster-leak, trade bands, day-one Forge |
| `CheckEnemyStructureSweep` `:676` | drives REAL `Enemy.ProbeForStructure` 3 cases under `ff.enemystructureaware` |
| `CheckItemIconCoverage` `:641` | real-art vs glyph counts + Knight start-weapon must resolve art |
| `CheckTutorialSteps` `:395` | tutorial-steps.json registry: exactly **7 mandatory steps** (owner 2026-07-24 end-after-defend), signals/highlights/dialogues resolve, `founding_defense` prepaidTower |

**Registration pattern for sibling suites** — two styles, both `Run(out string reason)` → bool:
1. **Direct** `if (!X.Run(out r)) failures.Add(r); else log.AppendLine("[tag] " + r);`
   — 46 suites, `DataRegression.cs:259-322` (an exception in one aborts the batch).
2. **Guard.Try-wrapped** (`DeNelle.Core.Diagnostics.Guard.Try("Regression", "<name> suite", …)`)
   — 31 suites, `:328-363` (one bad suite logs + is skipped; includes the
   deliberate FAIL-BY-DESIGN set: crystal-production, dungeon-dressing,
   modal-registration, ftue-honesty — red until their fix lands, `:326-327`).

**⚠ `[dungeon-exit]` TAG COLLISION:** TWO different suites log under the same
tag AND the same Guard label "dungeon-exit suite": `DungeonExitRegression.Run`
(`DataRegression.cs:335`) and `DungeonExitReachableRegression.Run` (`:341`). A
green/red `[dungeon-exit]` line in the log is ambiguous between them — rename
one tag when next touched.

77 suite registrations total over 82 files in `Assets/Editor/Regression/`
(81 siblings + DataRegression); the handful of unregistered files are
standalone/orphan oracles — see the inventory below.

### Sibling suite tree — `Assets/Editor/Regression/` (inventory)

Registered tags → classes, in `RunAll` order (D = direct `:259-322`,
G = Guard-wrapped `:328-363`):

| Tag | Class | Reg |
|---|---|---|
| `[covenant]` | MonetizationCovenantRegression | D:259 |
| `[tower-perks]` | TowerPerkRegression | D:260 |
| `[tower-respawn]` | TowerRespawnRegression | D:262 |
| `[def-target]` | DefenseTargetableRegression | D:263 |
| `[arena-prefab]` | ArenaPrefabAuditRegression | D:264 |
| `[core-datahub]` | CoreDataHubRegression | D:266 |
| `[core-catalog]` | CoreCatalogRegression | D:267 |
| `[core-world]` | CoreWorldLogicRegression | D:268 |
| `[core-save]` | CoreSaveContractRegression | D:269 |
| `[hero-prog]` | HeroProgressionRegression | D:270 |
| `[aegis]` | AegisSetReachabilityRegression | D:271 |
| `[build-upgrade]` | BuildingUpgradeRegression | D:272 |
| `[offline-harvest]` | OfflineHarvestRegression | D:273 |
| `[village-econ]` | VillageEconomyRegression | D:274 |
| `[arena-cat]` | ArenaCatalogRegression | D:275 |
| `[companion-roster]` | CompanionRosterRegression | D:276 |
| `[troop-roster]` | TroopRosterRegression (WO-736) | D:278 |
| `[raid-scoring]` | RaidScoringRegression (WO-771.6/.11) | D:280 |
| `[townsfolk]` | TownsfolkDialogueRegression | D:281 |
| `[atb-engine]` | AtbEngineRegression | D:282 |
| `[econ-meta]` | EconomyMetaCatalogRegression | D:283 |
| `[glimmer]` | GlimmerEconomyRegression | D:284 |
| `[scene-route]` | SceneRoutingRegression | D:285 |
| `[art-resource]` | ArtResourceRegression | D:286 |
| `[sfx-webgl]` | SfxWebglAudioRegression (WO-682) | D:288 |
| `[core-save-sme]` | CoreSaveRegression | D:290 |
| `[build-econ]` | BuildEconomyRegression | D:291 |
| `[obsidian-queue]` | ObsidianQueueRegression | D:292 |
| `[troop-recovery]` | ArmyRecoveryRegression (WO-781) | D:294 |
| `[data-web]` | DataWebRegression | D:295 |
| `[hud-ui-sme]` | HudUiRegression | D:296 |
| `[combat-atb]` | CombatAtbRegression | D:297 |
| `[dialogue]` | DialogueRegression | D:298 |
| `[enemy-rig-color]` | EnemyRigColorRegression | D:299 |
| `[enemy-resolver]` | EnemyResolverRegression (WO-772, ENEMY_RESOLVER_OK) | D:301 |
| `[overworld-combat-gate]` | OverworldCombatGateRegression | D:303 |
| `[destroyed-structure]` | DestroyedStructureRegression | D:305 |
| `[orc-binding]` | OrcRigBindingAudit | D:306 |
| `[hero-loco-clips]` | HeroLocomotionClipRegression | D:307 |
| `[ui-obsidian]` | UiObsidianConformanceRegression | D:309 |
| `[ui-mvvm]` | UiMvvmConformanceRegression | D:310 |
| `[hud-posture]` | HudPostureRegression | D:311 |
| `[strategic-placement]` | StrategicPlacementRegression (WO-673) | D:314 |
| `[talent-strategy]` | TalentStrategyRegression (WO-676) | D:317 |
| `[echo-spec]` | EchoSpecializationRegression (WO-738) | D:320 |
| `[room-forge]` | RoomForgeRegression (WO-745) | D:322 |
| `[wave-scaling]` | WaveScalingRegression | G:328 |
| `[enemy-rewards]` | EnemyRewardRegression | G:329 |
| `[wall-mitigation]` | WallHeartMitigationRegression | G:330 |
| `[pack-grant]` | PackGrantRegression | G:331 |
| `[upgrade-authority]` | BuildingUpgradeAuthorityRegression | G:332 |
| `[crystal-production]` | CrystalProductionRegression (fail-by-design) | G:333 |
| `[sfx-resolve]` | SfxResolveRegression | G:334 |
| `[dungeon-exit]` | DungeonExitRegression | G:335 |
| `[dungeon-dressing]` | DungeonDressingRegression (fail-by-design) | G:336 |
| `[dungeon-return]` | DungeonReturnSceneRegression | G:337 |
| `[dungeon-lore]` | DungeonLoreReadableRegression | G:338 |
| `[dungeon-state-reset]` | DungeonStateResetRegression | G:339 |
| `[dungeon-defeat]` | DungeonDefeatEndsRunRegression | G:340 |
| `[dungeon-exit]` (**collision**) | DungeonExitReachableRegression | G:341 |
| `[dungeon-defeat-realtime]` | DungeonRealtimeSettleRegression | G:342 |
| `[dungeon-toast]` | DungeonToastRegression | G:343 |
| `[dungeon-fpv]` | DungeonFpvRegression | G:344 |
| `[modal-registration]` | ModalArbiterRegistrationRegression (fail-by-design) | G:345 |
| `[founding-reach]` | FoundingReachabilityRegression | G:346 |
| `[ftue-honesty]` | FtueHonestyRegression (fail-by-design) | G:347 |
| `[echo-card-copy]` | EchoCardCopyRegression | G:348 |
| `[shader-pin]` | ShaderPinRegression | G:349 |
| `[structure-burn]` | StructureBurnRegression (WO-761) | G:351 |
| `[waves-schema]` | WavesSchemaRegression (EW-3) | G:353 |
| `[wave-authoring]` | WaveAuthoringLiveRegression | G:354 |
| `[gear-levels]` | GearLevelsRegression (WO-808) | G:356 |
| `[pack-cosmetic-integrity]` | PackCosmeticIntegrityRegression (ECON-1) | G:357 |
| `[tower-wall-los]` | TowerWallLosRegression | G:358 |
| `[vfx-aura-diff]` | VfxAuraDifferentiationRegression | G:359 |
| `[tower-proj-map]` | TowerProjectileMapRegression (owner VfxManualPicks tower tiers) | G:361 |
| `[realm-map]` | RealmMapRegression (WO-826) | G:363 |

**Suite contract:** hand-rolled — every suite is a `public static class` with
`public static bool Run(out string reason)`; there is NO attribute/reflection
registry, `RunAll` calls each **by literal name**. Registration is a **manual
follow-up step** (e.g. the 2026-07-26 suite wave was added in one commit and
wired into `RunAll` by a separate `61d1c50a` "wire new oracles" commit) — which
is exactly how orphans happen. Most suites also emit their own uppercase
`<X>_OK` / `<X>_FAIL` marker (e.g. `AEGIS_REACH_OK`, `CORESAVE_OK`,
`STRATEGIC_PLACEMENT_OK`, `ROOMFORGE_REGRESSION_OK`); exceptions:
`GearLevelsRegression` returns only a reason string (`GEAR LEVELS OK/FAIL`, no
`Debug.Log` marker — `GEAR_CURATION_OK/FAIL` belongs to `DataWebRegression`),
and `HudPostureRegression` has no uppercase marker at all.

> ## STOP: THIS "UNREGISTERED" TABLE WAS LARGELY OVERTAKEN ON 2026-09-06  -  READ `DataRegression.RunAll`, NOT THIS
> **WO-1496 registered SEVEN suite files that existed and ran nowhere**, three of them rows below:
> `RepairProbeRegression` (`DataRegression.cs:1707`), `ArenaCombatOracle` (`:1709`) and
> `BlankStartCensusRegression` (`:1738`) are **now registered**, alongside `CombatFoundationRegression`
> (`:1708`), `GearAddressableGroupRegression` (`:1716`), `AssetMoveManifestRegression` (`:1722`) and
> `EnemyArtCoverageRegression` (`:1734`). Several exposed only a `void Run()` and were therefore
> invisible even to `RegressionMarkerRegression`. (!) **`RepairProbeRegression`'s row below still cites
> its own header's "deliberately NOT wired" declaration  -  that declaration no longer matches the call
> site.** (!) **`EnemyArtCoverageRegression` is EXPECTED TO GO RED and that red is the FINDING  -  do not
> weaken the suite or add an exemption** (the honest resolutions are named in `DataRegression` at the
> registration; WO-1496 S3). `BlankStartCensusRegression` is registered **LAST above the end fence,
> deliberately**: it opens `Main_Castle_Overworld` in Single mode, so any suite registered after it
> would run against a swapped scene. **`SessionRegression` is the one row here that is still
> standalone** (`grep SessionRegression DataRegression.cs` returns only two header comment lines).
> The rows are kept rather than deleted, because a deleted row cannot correct a reader who
> half-remembers it. **The call sites are the authority; this table is not.**

**Unregistered / standalone-only (was 4 of 81 siblings; 3 of these 4 were registered 2026-09-06):**

| Class | Entry | How it runs | Marker |
|---|---|---|---|
| `ArenaCombatOracle` | `Run()` `:92` (void — not the out-reason contract) | `[MenuItem Defenders/QA/Run Arena Combat Oracle]` `:91` / `-executeMethod DeNelle.Editor.ArenaCombatOracle.Run` — drives the REAL arena resolve + reads FlowTrace lines | `ARENA_ORACLE_OK/FAIL` |
| `BlankStartCensusRegression` | `Run()` `:61` (void) | `[MenuItem Defenders/Regression/Blank Start Census (WO-703)]` `:60` — blank start scene contains ONLY tree+well+walls/gates | `BLANK_START_OK/FAIL` |
| `RepairProbeRegression` | `RunStandalone()` `:48`, `Run(out reason)` `:53` | script-only. Conformant `Run(out reason)` that RunAll does not call — **deliberate**, declared in its own header ("STANDALONE oracle, deliberately NOT wired into DataRegression.RunAll"); `RegressionMarkerRegression` honours that declaration as its opt-out | `REPAIR_PROBE_OK/FAIL` |
| `SessionRegression` | `RunAll()` (void) | script-only  -  guards a past session's fixes (incl. SaveSchema PetName audit). **Still the one genuinely standalone row here.** | STOP: **`SESSION_GUARDS_OK <pass>/<total> checks`  -  BOTH NUMBERS ARE MEASURED, AND NO DIGIT MAY BE WRITTEN BACK INTO THIS CELL.** *(WO-1493, 2026-09-06.)* It used to print the string literal `SESSION_GUARDS_OK 6/6 checks`  -  **a LABEL, not a MEASUREMENT**: it would have printed `6/6` whether six checks ran, one ran, or **none did**  -  the `gates-report-success-without-proving-it` class, named by `RegressionMarkerRegression` audit **G8**. The checks are now a **TABLE** (`private static readonly SessionCheck[] Checks`, six entries today: `vendor-contract` * `starter-weapons` * `enemy-models` * `structure-prefabs` * `save-round-trip` * `general-vendor-stock`) that `RunAll` iterates; the denominator is `Checks.Length` and the numerator is how many entries returned **without appending a failure**, so adding or removing a check moves the marker with no edit to the format string. A **throwing** check is recorded as a RED check (`check '<name>' THREW ...`) rather than swallowed, so the denominator cannot shrink quietly (S12), and each check logs `-- check '<name>': OK` or `RED`. The FAIL line now reads `...: <n> failure(s) across <total-passed>/<total> check(s)`. **`RegressionMarkerRegression` RULE 6 `[gate-marker-count]` FAILS if a digit is inlined.** *(The 2026-08-02 fix of the `REGRESSION_OK` collision with DataRegression's verdict marker still stands.)* |
| `RegressionMarkerRegression` | `Run(out reason)`, `RunStandalone()` | **registered** in `DataRegression.RunAll` as `[regression-marker]` — source-scans `Assets/Editor` + the gate `.ps1`s: no two oracle files share an `*_OK` literal, every `Run(out string)` oracle under `Assets/Editor/Regression` is registered, every gate grep resolves to exactly one emitter, and no suite green-passes out of a null guard (ratchet) | `REGRESSION_MARKER_OK/FAIL` |

### DELTA 2026-09-06  -  13 suites added or rebuilt this session

STOP: **Do not restate the suite COUNT here**  -  read the `REGRESSION_OK <n>/<n> suites` marker on a FRESH
log. Every entry below was opened at source; each names its tag(s) and what it locks.

| Suite (`Assets/Editor/Regression/...`) | Marker / tag | What it locks, and the honest limit |
|---|---|---|
| `TroopTargetPreferenceRegression` (217L) | `TROOP_TARGET_PREF_OK` * `[troop-target-preference]` | WO-1438. A **reachable** live defender beats a nearer hostile structure; siege (`preferStructures`) is unchanged; the route gate is a FILTER, not steering. Cases `case1..case4` + `b` variants. Its header quotes the proving log line (`accepted[unit=1,struct=17] ... preferStruct=False`). |
| `WelcomeBackDoorsRegression` (348L) | `[two-rows-with-doors]` `[nothing-means-nothing]` `[manage-tab-is-real]` `[raid-door-routes]` `[ready-needs-all-three]` `[popup-routes-through-the-router]` `[collect-first-then-route]` `[rows-are-ascii]` `[trace-line]` | WO-1408. The return screen's destination decisions  -  **no canvas, no scene, no PlayMode**, because `WelcomeBackDoorsVM` is pure. Records the measured pre-change red (one button, no VM, no router reference). |
| `BuildAffordabilityWordsRegression` (180L) | `BUILD_AFFORDABILITY_WORDS_OK` | WO-1411. **Minted RED against commit `949e848a0`**  -  collection subtitles carry the VM's affordability count, the ghost rail says PLACE/ROTATE/CANCEL (BLOCKED when refused), the confirm modal prices the tap with the **real graced duration** and crew, the 8th "Upgrade Defenses" card is a footer link, ruling #13 renames landed in **both** canonical copies, and the banner takes the placement phase from any door. |
| `ManageRowBenefitRegression` (477L) | `MANAGE_ROW_BENEFIT_OK` * `[location-is-words]` `[no-developer-coordinate]` `[coordinate-literal-retired]` `[defense-/building-/research-row-names-a-benefit]` `[troop-upgrade-names-an-effect]` | WO-1405. Every Manage row PRICES the tap **and** says what it buys; no player-facing string carries a developer grid coordinate. LIVE half (GameState fixture driving `ManageScreenVM`) + SOURCE half for the one contract no fixture can observe. |
| `NightMarketNoWalletRegression` (865L) | `NIGHT_MARKET_NO_WALLET_OK` * `[night-market-no-wallet]` `[anchors]` `[badge-budget]` `[badge-live]` `[banner-on-screen]` `[banner-source]` `[ledger-vs-gap]` `[live-store]` | WO-1409. The Night Market a player **without** a wallet sees: **one reason, nine prices**, and a badge that is a word rather than a fragment. Evidence was a screenshot with SIX "Price unavailable" + THREE "UNAVAILABLE" cards and a CONNECT WALLET button tying none of them together. |
| `CloudLoadRestoreRegression` (335L) | `CLOUDLOAD_RESTORE_OK` | WO-1447 + WO-1448. What a cloud LOAD restores and **when it may overwrite the local save**  -  asserts `GameStateService.BackendApplyOutcome`, the DECISION rather than its side effects. Data + logic only, no scene, **no network**. |
| `EnemyProbeCadenceRegression` (284L) | `[enemy-probe-cadence]` * `[cadence]` `[change-gate]` `[drop-paths]` `[no-step]` `[kept]` `[reset]` `[bounded]` `[semantics]` | WO-1450 + WO-1459 S2. (!) **DECLARES ITSELF A SOURCE LINT IN EVERY REASON STRING** (WO-1494: six suites claimed to MEASURE and were text lint). It strips comments/strings from `Enemy.cs` and asserts the SHAPE of the two guards. **It cannot prove the device line count or the frame cost**  -  that is a device capture. What a lint CAN do is stop the guards being reverted, which is exactly how the unthrottled `Step` got back in front of a tester. |
| `FrameBudgetMeasureRegression` (430L) | `[frame-budget-measure]` * `[budget]` `[sites]` `[rollup]` `[overload]` `[4-arg]` | WO-1483 + WO-1459. Same honest-scope declaration: brace-matches each named METHOD BODY and asserts a `FlowTrace.Measure` scope is present inside it. **It cannot prove frame cost.** It exists so the scopes cannot be deleted  -  CLAUDE.md S12, "NEVER STRIP FLOWTRACE". |
| `AllowlistExpiryRegression` (355L) | `ALLOWLIST_EXPIRY_OK` * `[allowlist-expiry]` `[pointer-present]` `[not-expired]` `[scan-alive]` `[definitional-accurate]` | WO-1495. Every exemption/allow-list block under `Assets/Editor/Regression` must carry a **WO pointer, an origin date and an expiry**. An exemption with no owner and no expiry is indistinguishable from a defect someone stopped looking at, and nothing ever forces a re-read  -  so the suite stays **GREEN forever** on the exact content the block was written to cover. Thirteen such blocks were found; the four largest were `MageAbilityIconRegression` KnownGaps, `EnemyPoolResetRegression` BrainExempt, `UiObsidianConformance` AllowList (cited a WO but **no date**) and `ShaderPredicateSingleAuthority`. |
| `CoreReflectionSourceRegression` (247L) | `CORE_REFLECTION_SOURCE_OK` | WO-1511 + WO-1510, two mirror cases about **reflection standing in for a reference that should not be a string**: (1) a runtime file whose owning `.asmdef` already references `DeNelle.Core` may never reach a Core type via `Type.GetType("DeNelle.Core....")`; (2) nothing under `Assets/_Modules/Core` may reach UP into `DeNelle.Village` by `Type.GetType`  -  **that seam is now `IVillageBridge` on `CoreServices`** (new: `Core/Bridging/`, `Village/VillageBridgeService.cs`). |
| `DefenseReportLayoutRegression` (318L) | `DEFENSE_REPORT_LAYOUT_OK` * `[defense-report-layout]` `[dark-plate]` `[derived-pitch]` `[source-laws]` | WO-1515, from the owner's device frame (build 2026.09.07.358574, 20:03): the DETAIL pane rendered as a **flat TAN rectangle with grey text**, near-invisible, and list rows overlapped. Locks the dark plate and a derived row pitch. |
| `RaidStagingMarkerRegression` (471L) | `RAID_STAGING_OK` * `[raid-staging]` | WO-1520, minted from the owner's verbatim ruling (*"as soon as you spawn in you start dying without having even a second to deploy"*). **No PlayMode, no scene load, no bake**  -  it MEASURES the generated geometry from the same two canonical files the builder reads (`scene-configs.json` + `structures-catalog.json`) and **re-reads the builder's own constants out of its source**, so it cannot drift from the code it pins. |
| `PreviewRenderTextureSamplesRegression` (280L) | `PREVIEW_RT_SAMPLES_OK` * `[lint]` `[live-rig]` | WO-1451. The tower preview's `RenderTexture` and its camera must agree on sample count. Evidence: **260 `[BREAK]` errors in 144 seconds** (130 each, alternating)  -  `RenderPass: Attachment 0 was created with 1 samples but 2 samples were requested` / `EndRenderPass: Not inside a Renderpass`, traced to `TowerPreviewCamera.Begin`. |

**Suite detail highlights (verified from code):**

- **RealmMapRegression** (WO-826, newest file, added `eb5d0710` 2026-08-01) —
  `Run` `:41`: (a) `realm-map.json` dual copies byte-identical (`:55-57`);
  (b) per-region id-set + `JToken.DeepEquals` on `mapPoint`/`gate` parity
  (`:66-86`); (c) typed-loader oracle: real `RealmMapCatalog.Reload()` `:91`,
  `home.Title == "Elarion"` `:95-98`, exactly **5 regions** `:104`, gate kinds
  ∈ {bestWave, regionCleared} and `regionCleared` must reference a KNOWN region
  id (`:117-121` — dangling unlock chains fail); (d) canon lint: **"Avalon"
  must never appear** in any player-facing map string (`:134-146`). Verdict
  `REALM_MAP_OK (5 regions, home Elarion, dual copies in parity)` (`:150`).
- **ObsidianQueueRegression — the Queues-button RETIREMENT asserts (owner
  2026-08-01)** — `Run` `:36`, ten sub-checks `:44-53`. Retirement is enforced
  by source/JSON grep, not UI instantiation: `HudKitController` must NOT
  re-register `workQueueButton` (`:282-283`) and MUST carry `queueStatusChip`
  (`:284-285`); BOTH `hud-areas.json` dual copies must have dropped the
  `workQueueButton` row (`:301`) and carry a `queueStatusChip` row (`:302`),
  byte-parity checked (`:304-306`); `HudAreasConfig.TryParseArea` must handle
  `'queuestatus'` (`:308-309` — the code-present/JSON-absent "never renders"
  class); the kit must still call `ObsidianQueueGate.RequestToggle`
  (`:289-290`). Re-adding the retired button id ANYWHERE in those files fails
  the gate.
- **DungeonExit collision, full shape:** the two suites share the Guard label
  ("dungeon-exit suite"), the `[dungeon-exit]` tag, the `"dungeon-exit: "`
  reason prefix (`DungeonExitRegression.cs:111` / 
  `DungeonExitReachableRegression.cs:55`) AND the `DUNGEON_EXIT_OK/FAIL`
  markers (`:106,:113` / `:51,:57`) — but test DIFFERENT systems:
  DungeonExitRegression = composed `DungeonCompose_*` dungeons (reflection over
  3 affordance types, real `DungeonExitInteractable.Spawn`, `DungeonExitSpawner`
  injector with `[RuntimeInitializeOnLoadMethod]`; NavMesh reachability
  downgraded to a note headless); DungeonExitReachableRegression = pure
  source-lint of the RICH hand-built dungeon (`DungeonController.HydrateExits`,
  boss back-door `FindRoom("workshop")`/`BossDefeated`, `ExitToVillage`).
  Already flagged in `CANON_GROUND_TRUTH_2026-08-01.md:94`.
- **WaveAuthoringLiveRegression** (added `7f1f1e6a`) — reflects WaveManager's
  private smart-spawn field and **hard-fails if the field was renamed**
  (`:80-84`, a self-invalidation guard most suites lack), resolves the
  WaveManager script GUID inside every `*.unity`, then asserts: smart=1 ⇒
  waves.json declares NO authored `enemies[]` batches (they'd be silently
  discarded); smart=0 ⇒ every `WaveBatch.Type` exists in enemies.json
  (`:202-214`).
- **GearLevelsRegression** (WO-808, `55643448`) — `gear-levels.json` dual-copy
  byte identity (`:36-37`), `statMult[0] == 1.0` (`:53-54`) strictly climbing
  (`:55-57`), cost arrays length-matched with `[0] == 0` = owned baseline
  (`:63-66`), and rarity COVERAGE: any weapons/armor rarity with no band =
  "silently ladder-less" fail (`:73-89`).

**Per-suite one-liners** (entry `Run(out reason)` line; marker = `<X>_OK/FAIL`
unless noted): AegisSetReachability `:42` full Aegis set assemblable →
Oathweld bonus reachable. ArenaCatalog `:30` 3 opponents (purse=2×wager) + 6
defenders day-one affordable. ArenaPrefabAudit `:56` arena prefab renderers
textured, no oversized "pole" (F8-37). ArmyRecovery `:42` TickRecovery pure
step + AdvanceRecovery on live+offline callers. ArtResource `:56` portraits/
atlas/projectile/icon Resources paths resolve. AtbEngine `:35` isolated ATB
turn/tick invariants. BuildEconomy `:57` structures-catalog parse+dual-copy+
cost sanity+tier-monotonic upgrades. BuildingUpgradeAuthority `:34` TryUpgrade
writes only GameState.BuildingTiers. BuildingUpgrade `:27` Farm/Lumbermill/
Forge curves ascend, Magic-gated Arcane tier (DEF-121). CombatAtb `:43` RNG
reproducibility + RoundTs + damage invariants. CompanionRoster `:48`
class→NPC bijective, never mirrors the hero. CoreCatalog `:34` garrison
recipes + DataInjector round-trip. CoreDataHub `:45` every canonical JSON
non-empty + dual copy exists. CoreSaveContract `:37` save-version triple
aligned + migrate/round-trip. CoreSave `:65` full save suite (envelope, hash,
migration chain, validator, real SaveService round-trip). CoreWorldLogic `:29`
ZoneManager threat/graph + RegionSpawnTable. CrystalProduction `:33` producers
yield >0 data-driven (fail-by-design). DataWeb `:168` dual-copy sync + WebGL
load surface + gear curation. DefenseTargetable `:77` waves carry targetable
structures (F8-41). DestroyedStructure `:48` Repair() no-ops on destroyed
(destroyed = lost). Dialogue `:56` dialogue graph integrity + reachability.
DungeonDefeatEndsRun `:21` lost ATB fight ends run to Village. DungeonDressing
`:37` DressRoom seats >0 real props. DungeonFpv `:29` FPV flag default ON +
framing/joystick gates. DungeonLoreReadable `:15` lore-stone modal chain
present. DungeonRealtimeSettle `:23` one SettleEncounter authority, realtime/
ATB parity. DungeonReturnScene `:17` return to ACTIVE dungeon scene, not a
constant. DungeonStateReset `:17` OnEnable clears run identity. DungeonToast
`:21` checkpoint/craft toasts + Bryn lines. EchoCardCopy `:28` HeaderFor
awaken@1 / level-up@≥2. EchoSpecialization `:54` roster identity + save v33 +
lane bonuses. EconomyMetaCatalog `:57` pets/cosmetics/packs/wallets JSON.
EnemyResolver `:61` id→family→DISTINCT model. EnemyReward `:36` coin+xp
coverage + grant seam. EnemyRigColor `:62` every enemy prefab rigged+colored.
FoundingReachability `:32` ShouldOffer fresh/post-onboard + PresentOrContinue.
FtueHonesty `:33` founding highlights real controls (fail-by-design class).
Glimmer `:44` purchase round-trip + pet-slot persistence. HeroLocomotionClip
`:20` knight clips exist, no T-pose takes. HeroProgression `:38` XP curve +
Wisdom 50@L20 + dmg cap 3.0. HudPosture `:17` pursuit-pulse open/close
lifecycle (no marker). HudUi `:186` tofu oracle + UIDocument fence + Obsidian
conformance (`HUDUI_OK`). ModalArbiterRegistration `:52` every top-band modal
registers with PanelManager (fail-by-design). MonetizationCovenant `:108`
sweeps for pay-to-win/staking claims. OfflineHarvest `:33` 10h cap +
backwards-clock guard. OrcRigBindingAudit `:45` (+menu `:36-37`) avatars drive
the visible mesh. OverworldCombatGate `:41` ff.raidwalk/ff.regionroam gated +
reversible. PackCosmeticIntegrity `:46` every pack SKU Owns after grant.
PackGrant `:35` founders-vow grants currency+cosmetic. RaidScoring `:39` raid
V1 win/stars/loot + live HUD. RoomForge `:65` (see §6). SceneRouting `:55`
route consts + MergedWorld both states + one dungeon-entry. SfxResolve `:33`
core SFX keys load. SfxWebglAudio `:51` no divergent WebGL import overrides.
ShaderPin `:40` build hook pins URP Lit/Terrain Lit. StrategicPlacement `:68`
WO-673 §5 gates incl save v30. StructureBurn `:44` burn lingers ≤50% until
repaired/destroyed. TalentStrategy `:167` talents parse + StatSum + no dead
nodes. TowerPerk `:52` tower-perks table. TowerProjectileMap `:43` owner
VfxManualPicks tower tiers wired + catalogued. TowerRespawn `:62` towers
return on next placement/reload (F8-39). TowerWallLos `:26` walls carry the
"Structure" layer at spawn (LOS blocks). TownsfolkDialogue `:36` archetype
name/line coverage. TroopRoster `:55` 7 troops, ladder 1/1/2/3/4/5/6.
UiMvvmConformance `:135` new Views must bind a VM (VIOLATION vs baseline
WARN). UiObsidianConformance `:185` new uGUI must use the Obsidian kit (same
split). VfxAuraDifferentiation `:38` node/cathedral/spire auras DISTINCT.
VillageEconomy `:41` crystal single source across 3 stores + dual-wallet.
WallHeartMitigation `:28` walls.json heartDamageMultiplier flows to
HeartController. WaveScaling `:28` fallback curve climbs by wave 19.
WavesSchema `:52` both waves.json copies carry `"waves"` with ≥1 wave.

### Legacy RegressionSuite — `Assets/Editor/RegressionSuite.cs`
**SUPERSEDED by DataRegression but still present.** `[MenuItem Defenders/QA/Run
Regression Suite] RunAll()` (`:145-146`), batchmode
`DeNelle.Editor.RegressionSuite.RunAll` (`:16`). Its source-grep gates (camera
yaw, fork-bomb mirror `:390`, command-prefix mirror `:526`) still run when
invoked but it is NOT part of the live gate chain. Do not extend it — add to
DataRegression/sibling suites instead.

### SpawnPathVerifier — `Assets/Editor/SpawnPathVerifier.cs`
**STALE:** still opens the abandoned `Assets/Scenes/Village.unity` Single
(`:32`) — the only remaining editor-tool reference to the corruption-cursed
scene now that the old `OuterWorldBuilder.BakeWorldNavMesh` is gone (file
deleted; see §8). Verifies the wrong scene's spawn routing. SAFE (read-only)
but of no current value.

---

## 3. UI CAPTURE HARNESS — `Assets/Editor/UICaptureLaunch.cs`

Two entries (`:1-33` header):
- `RunCapture()` (`:63-73`, `[MenuItem Defenders/UI/Capture UI Panels]`) —
  **LEGACY Play-mode drive**; produces ZERO pngs under `-batchmode -quit`
  (Unity quits before Play ticks). Menu-only.
- `RunCaptureHeadless()` (`:84-120`) — the reliable path: fully **synchronous
  edit-mode render** of the real code-built uGUI panels to
  `Builds/ui-capture/<Panel>_<WxH>.png`, marker `UI_CAPTURE_OK <count>` (`:119`).
  Per-panel recipe: AddComponent (reflection for unreferenced assemblies
  DeNelle.Settings/DeNelle.HUD via `ResolveType` `:1310`), invoke the private
  `EnsureBuilt`/build methods, inject worst-case VMs, flip canvas to
  ScreenSpaceCamera + RenderTexture (`RenderCanvasToPng` `:1161-1264`, manual
  CanvasScaler math `:1269`), teardown with `DestroyImmediate` only.

**NO-NOGRAPHICS LAW (`:26-27`, `:94-98`):** the wrapper must pass `-batchmode
-quit` and **NO `-nographics`** — with a Null graphics device the pngs are
BLANK (it warns but still "succeeds"). Memory
`headless-screenshot-verify-ui-before-build`: run this + OPEN the pngs before
shipping any UI change.

**10 capture methods** (`:100-109`): `CaptureFoundingEchoCard` `:130` (Aldwin
flavor+lore, 2 resolutions), `CapturePauseMenu` `:211`, `CaptureEchoRoster`
`:315` (pip + right-edge Pets button), `CaptureHelpMenu` `:379`,
`CaptureDailyQuestHud` `:461` (honest empty state), `CaptureLoreReadingModal`
`:537` (longest-body fragment picked from canon), `CaptureTowerManagerPanel`
`:653` (8 stub towers overflow the well), `CaptureBuildMenuUpgradeTower` `:756`,
`CaptureRumorBoard` `:849` (WO-810 worst-case backend, 15 rumors + longest hook,
landscape ×2 + hand-anchored portrait 1080×2340), `CaptureRealmMap` `:977`
(WO-826: fresh-save LOCKED-fog acceptance state, landscape only).

**IN FLIGHT — RumorBoard daily tab:** the worst-case fixture's
`DailyToday => Array.Empty<RumorBoardVM.DailyRow>()` (`:1122`) — the daily-quest
tab renders EMPTY in the shot; a daily-tab worst case is not yet captured.

---

## 4. BUILD TOOLS

### DesktopBuild — `Assets/Editor/DesktopBuild.cs`
- `BuildWindows()` (`:91-243`, `[MenuItem Defenders/Build/Windows x64 Player]`)
  → `Builds/Windows/DefendersOfTheRealm.exe`. Crash mitigations, all verified:
  - Static Batching OFF via reflected `SetBatchingForPlatform(Win64, 0, 1)`
    (`:124-131`, level3-corruption fix) **plus an RCA readback** via
    `GetBatchingForPlatform` (`:137-147`) proving what the setter wrote.
  - **POST-BUILD BATCHING RE-ASSERT GUARD** (`:216-231`, RCA closed 2026-08-01):
    the reverter that kept flipping dynamic batching 1→0 runs INSIDE
    `BuildPlayer`, so the same `SetBatchingForPlatform(0,1)` is re-asserted
    AFTER the build so exit-serialization writes the owner's committed value and
    the build never leaves `ProjectSettings.asset` dirty.
  - Force Direct3D11 (`:169-172`, D3D12 upload-buffer crash on >35MB meshes).
  - Windowed 1600×900 resizable (`:185-188`).
  - `BuildOptions.Development` (`:203`) — deliberate, for the DevTools QA panel.
- `BuildWebGL()` (`:33-89`) — the **Vercel deploy** WebGL path: Gzip (`:57`),
  Minimal stripping (`:56`), `BuildOptions.None` (`:71`), and the
  **exceptionSupport dirt guard** (RCA 2026-08-01, `:58-63` + `:78-80`): sets
  `WebGLExceptionSupport.None` for the build but captures + RESTORES the prior
  (committed) value afterward so ProjectSettings never shows the 1→0 flip.
- Both **DESTRUCTIVE** (build dirs; `EditorApplication.Exit(1)` on fail). LIVE.

### WebGLBuild — `Assets/Editor/WebGLBuild.cs`
`BuildWebGL()` (`:36-141`) — the **itch/static-host** WebGL path. IL2CPP,
**Brotli + `decompressionFallback = true`** (`:89-91` — the WO-126 itch fix;
`-noBrotli` is now deprecated/ignored `:87-88`), 512 MB (`:92`),
`exceptionSupport = ExplicitlyThrownExceptionsOnly` (or `-debugExceptions` →
FullWithStacktrace, `:98-100`; None caused the DEF-124 black-screen), dataCaching,
Minimal stripping. **Ship default = `BuildOptions.None`** (`:124`, WO-408
DEFECT 2 fix); `-devBuild` opts into Development. **DESTRUCTIVE**. LIVE.

**⚠ DUPLICATE MENUITEM (still live):** `Defenders/Build/WebGL Player` is
registered by BOTH `WebGLBuild.BuildWebGL` (`WebGLBuild.cs:36`) and
`DesktopBuild.BuildWebGL` (`DesktopBuild.cs:33`) with **divergent settings**
(Brotli+fallback+ExplicitlyThrown+512MB vs Gzip+None-exceptions). Unity binds
only one to the menu; `-executeMethod` hits whichever is named. `ship-webgl.ps1`
vs the Vercel scripts pick different ones — know which you're invoking.

### AndroidBuild — `Assets/Editor/AndroidBuild.cs`
`BuildSeekerApk()` (`:50-97`, `[MenuItem Defenders/Build/Android APK (Seeker)]`)
→ `Builds/Android/DefendersOfTheRealm.apk`. `ApplyAndroidPlayerSettings`
(`:103-131`): package id **`com.denellestudios.echoesofelarion`** (`:46` — must
match the installed app so testers update in place), IL2CPP (`:111`),
**ARM64-only** (`:115`), minSdk 26 (`:119`). `ApplyReleaseSigning` (`:133-170`):
reads gitignored `keystore.properties` at repo root for stable release signature;
falls back to DEBUG signing with a warning if absent (testers then can't update
in place). Passwords are set in-memory only — never saved into ProjectSettings.
**DESTRUCTIVE**. LIVE (fed by `distribute-android.ps1 -Build`).

---

## 5. KAYKIT NPC PIPELINE (WO-818/WO-833 — the T-pose fix chain)

Root cause this chain kills: a skinned Humanoid whose Animator has no controller
renders its **bind pose** (owner F8 2026-08-02 "NPC Stuck in T Pose").

### KayKitNpcImporter — `Assets/Editor/KayKitNpcImporter.cs`
`StageAll()` (`:106`; menu `Defenders/Art/Stage KayKit NPC Bodies` `:102`;
batchmode `DeNelle.Editor.KayKitNpcImporter.StageAll`). **12 hardcoded `NpcRow`s**
(`:62-100`) from 4 gitignored pack roots (`:44-47`: Adventurers 2.0 + Mystery
Monthly S4/S5/S6): barracks→Paladin_with_Helmet, workshop→Engineer,
forge→Barbarian, armorer→BlackKnight, jeweler→Tiefling, market→Hoarder,
arcane-tower→Mage, pet-house→Druid, collector_farm→Farmer_A, mill→Farmer_B
(texture from the pack's `textures/` folder, `:91-93`),
collector_lumbermill→Ranger, fountain_healing→Cleric. Copies FBX+texture into
`Assets/Resources/NPCs/KayKit/` (`:42`), skips when byte-sizes match
(`:163-167`), flips the COPY to Humanoid (`animationType=Human`,
`CreateFromThisModel`, `importAnimation=false`, `:194-197`), then an avatar
verdict per row (`:199-207`). Markers (`:124-127`): `KAYKIT_STAGE_OK n/12` /
`KAYKIT_STAGE_PARTIAL n/12 (m missing)`. Missing pack = warn, never throw
(fresh clone/CI safe). **DESTRUCTIVE** (asset copies). LIVE.

### KayKitNpcAnimatorSetup — `Assets/Editor/KayKitNpcAnimatorSetup.cs`
`Build()` (`:59`; menu `Defenders/Art/Build KayKit NPC Idle Controller` `:55`).
Builds exactly ONE controller: `Assets/Resources/NPCs/KayKit/KayKitNpcIdle.controller`
(`:45-46`) — one layer, one default state "Idle", **no params, no transitions**
(`:99-103`). The idle clip is the project's own Humanoid mocap standby
`Assets/Action/Knight/Motion/studio-mocap-series-magical-moves/m-standby-idle.fbx`
(`:52-53` — the same clip HeroAnimatorFactory's KnightMocap uses; KayKit's own
animation pack is generic-rigged + gitignored, rejected `:13-15`). Marker:
**`KAYKIT_IDLE_OK`** appended to the success log (`:109-111`);
`KAYKIT_IDLE_FAIL` via LogError on no-clip/create-throw/null (`:64`, `:89`,
`:94`). Idempotent overwrite. The controller asset is **committed**.

### Runtime consumer + gate
`Assets/_Modules/Village/NPCs/KayKitNpcBody.cs`: `Load` (`:44`) resolves
`repo.npcModel` → `Resources.Load("NPCs/KayKit/"+slug)` (unauthored → silent
People fallback; broken slug → one `FlowTrace.Warn` `:67-69`); `ArmIdle` (`:93`)
assigns the controller + `applyRootMotion=false` (`:130`). Call sites:
`CastleVendorNpcInjector.cs:724,:807`, `BarracksNpcInjector.cs:253,:294`.
Gated by `DataRegression.CheckNpcModels` (§2) — note assertion (c) duplicates
the importer's 12-row table verbatim (`DataRegression.cs:1346-1370`), so
**`KayKitNpcImporter.Rows` and `CheckNpcModels` must be edited together**.

### KayKitMaterials — `Assets/Editor/KayKitMaterials.cs`
`FixAllMaterials()` (`:92`; menu `Tools/DeNelle/Fix KayKit Materials` `:78`) —
repairs white/magenta auto-imported KayKit FBX materials under
`Assets/Models/KayKit`: one URP/Lit atlas material per model folder, remapped via
ModelImporter `AddRemap` (`:21-22`). Idempotent. **DESTRUCTIVE** (asset writes).

### KayKitChallengeOutpostBuilder — `Assets/Editor/KayKitChallengeOutpostBuilder.cs`
`Build()` (`:42`; menu `Defenders/World/Build KayKit Challenge Outpost`) —
script-built triple-ring outpost, `NewScene` + saves
`Assets/Scenes/KayKitChallengeOutpost.unity` (`:29`, `:117`). **DESTRUCTIVE**.
⚠ NOT covered by the `.gitattributes` binary-scene rule (see §6) — same
batchmode binary-save exposure class if ever baked headless.

---

## 6. ROOMFORGE — `Assets/Editor/RoomForge/` (namespace `DeNelle.Editor.RoomForge`)

Runtime twin: `Assets/_Modules/Dungeons/RoomForge/` (`DeNelle.Dungeons`). The
"Checks" live there, NOT in the editor tree:
**`DungeonBakerChecks`** (`Assets/_Modules/Dungeons/RoomForge/DungeonBakerChecks.cs:82`)
— `TryMate :110`, `StillMated :138`, `RoomsOverlap :154`, `SealsAsSecret :175`,
master `Compose :232`. Single source of truth shared by the baker
(`DungeonBaker.cs:162-165`) and `RoomForgeRegression` (`:14-18`) — no duplicated
logic.

### DungeonBaker — `DungeonBaker.cs:25`
Entries: `BakeDefault()` `:36` / `BakeSelected()` `:43` (menus under
`Defenders/Dungeon/`), `BakeDefaultBatch()` `:59` (batch, `Exit(0)`), core
`BakeFromFile(path, populateForPlay)` `:78`. Layouts from
`StreamingAssets/Data/Canonical/dungeon-layouts` (default
`d4_sunken_crypt_spine.json`), output `Assets/Scenes/DungeonCompose/<id>.unity`.
**DESTRUCTIVE:** `NewScene(EmptyScene, Single)` at `:117` wipes the open scene
unprompted; saves the scene `:251-253` and mutates Build Settings (`:254`,
`:523-530`). Room prefabs from `Assets/Dungeon/Rooms/<stem>.prefab` with
procedural placeholder fallback (`:305-351`); dresses via
`DungeonDresser.DressRoom` (`:214`); NavMeshSurface bake from PhysicsColliders
(`:222-228`) + first→last path probe (`:231-240`).
- **HARD GATE (WO-745 §2 fix 1, `:176-193`):** any mate/drift/overlap failure →
  no navmesh, no save, no Build Settings touch; `SUMMARY … ABORT` marker
  (`:181`). Failed-scene debug save gated by EditorPrefs
  `DungeonBaker.SaveFailedScenes` (default OFF).
- **NUL/binary-scene history (`:256-290`):** batchmode `SaveScene` writes a
  BINARY SerializedFile. Mitigation: forced `ForceText` + `ForceReserializeAssets`
  (`:263-271`) + a 5-byte `%YAML` self-check that **warns, not fails**
  (`:274-288`). `dg_starter_loop.unity` on disk IS still binary (verified) and
  is protected by `.gitattributes:45-49` (`Assets/Scenes/DungeonCompose/*.unity
  binary` overrides the repo-wide `*.unity text eol=lf` — EOL renormalization is
  what actually corrupts it). Memory `dungeon-scene-shared-tree-corruption`
  stands: re-bake only in an isolated worktree.

### GraphDungeonComposer — `GraphDungeonComposer.cs:75`
`ComposeSelected()` `:87`, `ComposeStarterLoop()` `:102` (populateForPlay:true),
`ComposeStarterLoopBatch()` `:110`, core `ComposeAndBake` `:124`. BFS-solves a
graph (`StreamingAssets/.../dungeon-graphs`, starter `dg_starter_loop.json`)
into a layout json, then delegates to `DungeonBaker.BakeFromFile` (`:177`) —
inherits all its destructive behavior. **⚠ DUAL-COPY DRIFT RISK:** writes ONLY
the StreamingAssets layout copy (`:162-165`), never the
`Resources/Data/Canonical/dungeon-layouts` mirror — the mirror is satisfied by
some other step today, and `RoomForgeRegression` Case 2 is a dual-copy check.

### DungeonDresser — `DungeonDresser.cs:33`
No entry points; `DressRoom(room, seedIndex)` `:54` called only by the baker.
Deterministic seeded placement (`:62`), strips prop colliders (`:14-18`) so
dressing never carves NavMesh, KayKit dungeon pack by filename token with tinted
primitive fallback. SAFE relative to scenes/assets.

### DefaultDungeonRoomsBuilder — `DefaultDungeonRoomsBuilder.cs:25`
`BuildAll()` `:48` (menu), `BuildAllBatch()` `:75` (deliberately no `Exit`).
Writes **17 room prefabs** to `Assets/Dungeon/Rooms` (`:293`) — pinned by
`RoomForgeRegression.ExpectedRoomCount = 17` (`RoomForgeRegression.cs:46`) —
and writes **BOTH** rooms-catalog.json copies (`:487-493`, honors the dual-copy
law). **DESTRUCTIVE** (assets only; no scene touch).

### RoomForgeMaterials — `RoomForgeMaterials.cs:18`
`EnsureMenu()` `:50`. Three URP/Lit mats under `Assets/Dungeon/Materials` —
**deliberately untextured solid stone** (`:1-10`: the KayKit colormap atlas on a
primitive UV-maps rainbow patchwork; contrast `KayKitMaterials`, which DOES wire
the atlas for real FBXs). Idempotent.

### RoomForgeWindow — `RoomForgeWindow.cs:21`
`[MenuItem Defenders/Dungeon/Room Forge] Open()` `:43` — interactive authoring
EditorWindow (6u grain); Save writes a prefab (`:367`) + appends BOTH catalog
copies (`:458-459`). User-driven destructive.

Gates: `RoomForgeRegression.Run` → `[room-forge]` (`DataRegression.cs:322`),
markers `ROOMFORGE_REGRESSION_OK/FAIL`, 10 cases, never opens shipping scenes;
`DungeonDressingRegression.Run` → `[dungeon-dressing]` (`:336`),
`DUNGEON_DRESSING_OK/FAIL`.

---

## 7. ANIMATOR FACTORIES

### HeroAnimatorFactory — `Assets/Editor/HeroAnimatorFactory.cs`
Data-driven: one `Build(HeroSpec)` (`:376`) consumes `Specs[]` (`:179-241`) —
**Knight `:181`, Mage `:201`, Ranger `:221`, Cleric `:227`** → fixed outputs
`Assets/Resources/Heroes/<slug>.controller`. `BuildAll()` (`:243-250`,
`[MenuItem Defenders/Animation/Build Hero Animators (Mixamo)]`). Mixamo clips
under `Assets/Action/<Class>/` + Shared. **DESTRUCTIVE** (controller assets).
- **KnightMocap (owner 2026-07-04, `ff.mocaploco`):** `BuildKnightMocapController()`
  (`:299-374`, `[MenuItem …/Build Knight Mocap Locomotion Controller]`) clones
  the Knight spec → **`Assets/Resources/Heroes/KnightMocap.controller`** (`:287`,
  NEVER Knight.controller `:313`). Studio-mocap sword+shield packs
  (`:255-265`): calm idle `m-standby-idle` (magical-moves) vs braced
  `idle_ready`, gait `walkforward01`/`runforward_218667`, combat-stance split
  (`:479`), turn-in-place tier (only spec with turn clips → TurnDir param,
  `:345-346`, `:434`), prebattle unsheathe (`:152`), directional deaths
  (`:165`, `:610`). "Bound for KnightV3 when ff.mocaploco=1" (`:373`).
  KnightMocap's calm idle is the SAME clip KayKitNpcAnimatorSetup uses (§5).

### CraftPixTownsfolkAnimatorSetup — `Assets/Editor/…` (NEW 2026-08-20, `9a2d1faae`)
`DeNelle.Editor.CraftPixTownsfolkAnimatorSetup.Run` repoints **`AC_CraftPixTownsfolk`**'s Idle and
Walk states off the HERO's mixamo locomotion (`Assets/Action/Shared/Shared_Idle.fbx`,
`Shared_Walk_Forward.fbx` — the same clips Knight/Cleric/Mage/Ranger play) onto the civilian
Supercyan `common_people@idle::idle` / `common_people@walk::walk`. **All 14 CraftPix bodies share
this one controller**, so before the repoint every vendor, every wandering villager and both quest
NPCs stood combat-ready and walked the hero's walk. Drives Unity's own `AnimatorController` API —
**never a hand-edit of the `.controller` asset** — and **refuses** to swap in a clip that is not
imported Humanoid (a Generic clip cannot pose a humanoid rig). Sibling fix the same day: the three
NPC injectors stopped arming CraftPix people with `KayKitNpcIdle` (§5), which plays the Knight's
combat standby; pinned by `NpcIdleControllerRegression` `[npc-idle-controller]`.

### AnimatorSetup — `Assets/Editor/AnimatorSetup.cs`
Canonical shared enemy controller factory (KayKit Character Animations rigs →
`Assets/Generated/Animators/*.controller`; params Speed/Attack/Hit/Dead/Cast).
`[MenuItem Defenders/Animation/Build Animator Controllers] BuildAnimators()`.
Called by EnemyAnimatorSetup. **DESTRUCTIVE**.

### EnemyAnimatorSetup — `Assets/Editor/EnemyAnimatorSetup.cs`
`Setup()` (`:52-53`, `[MenuItem Defenders/Animation/Setup Enemy Animators (DTT)]`):
ensures Generic avatars on the KayKit skeleton meshes, runs
`AnimatorSetup.BuildAnimators()` (`:58`), copies the runtime controllers from
`Assets/Generated/Animators/` into `Assets/Resources/Enemies/` (`:39-40`,
`:61-79`) for the runtime `EnemyAnimatorFactory` (gameplay code — there is no
editor file of that name). **DESTRUCTIVE**.

### DragonAnimatorSetup — `Assets/Editor/DragonAnimatorSetup.cs`
**REWORKED (WO-760, commit `27de1aff`): now builds SYNDRATH from the licensed
`Assets/Dragon/Prefab/Dragon.prefab`** (`:68`) — replaces the old CC-BY-NC
3DHaupt dragon (`:15`). Entries: `BuildSyndrathDragon()` `:106-107` (both),
`BuildDragonAnimator()` `:118-119` → `Assets/Generated/Animators/SyndrathDragon.controller`
(`:71`), `BuildDragonBossPrefab()` `:232-233` →
**`Assets/Resources/Enemies/Boss_Dragon.prefab`** (`:228-229` — the
`WaveManager.SpawnApexBoss` load path) with the rig visual child, root motion
off, capsule collider + DragonBoss, demo MonoBehaviours stripped (`:94`).
Menus under `Defenders/Enemies/`. **DESTRUCTIVE**. LIVE.

---

## 8. SCENE / WORLD BUILDERS (gate-relevant subset)

### CastleHubBuilder — `Assets/Editor/CastleHubBuilder.cs` (~2500 lines)
**THE LAW: MainCastle_Hall is hand-tuned — newer tools are ADD-ONLY and NEVER
regenerate it.** Explicit in code: `AddCastleBridgeSeam` "ADD-ONLY: this NEVER
calls BuildCastleHub / regenerates MainCastle_Hall (the owner has tuned
walls/structures)" (`:1816`), `OpenScene … // NEVER BuildCastleHub` (`:1854`,
`:2022`), `RewireAndRebakeCurrentCastle` "Does NOT regenerate the castle"
(`:1588-1598`).
- Legacy full-regen still present: `BuildCastleHub()` (`:121-122`,
  `[MenuItem Defenders/Scenes/Build CastleHub_MainKeep]`) — **DESTRUCTIVE,
  wipes + rebuilds CastleHubRoot; do not run against the tuned scene.**
- Live add-only/batch entries: `AddNavMeshFloorToCurrentCastle` `:1007`,
  `WireCurrentCastleToOuterWorld` `:1032`, `MakeCastleHubPrimaryStartAndWire`
  `:1193`, `BatchWireCastleAndSave` `:1287`, `BatchRebuildGrandStairAndBake`
  `:1333`, `BatchAddFloorAndBakeCastle` `:1384`,
  `BatchRebuildCastleFromRecipeAndBake` `:1471`, `RewireAndRebakeCurrentCastle`
  `:1598`, `AddCastleBridgeSeam` `:1838` / `RemoveCastleBridgeSeam` `:2016`
  (SerializedObject NavMeshLink wiring, `:1833-1834`, `:1941`),
  `BuildSeamlessOuterWorldSeam` `:2195`, `AddHeartToCurrentCastle` `:2509`,
  `BatchAddCastleWaveSystem` `:2535`.

### WorldMergeBuilder — `Assets/Editor/WorldMergeBuilder.cs`
The WO-608 MergedWorld generator. `BuildMergedWorldScene()` (`:80-81`,
`[MenuItem Defenders/World/Merge Castle + Overworld (build merged scene)]`):
opens `MainCastle_Hall.unity` Single (`:90`) + `OuterWorld.unity` Additive
(`:97`), moves objects across, and **SaveScene-As** to
`Assets/Scenes/Main_Castle_Overworld.unity` (`:51-53`, `:152-154`) — the
originals stay intact. `BakeMergedWorldNavmesh()` (`:167-168`) opens the merged
scene, bakes, **saves it** (`:242`); delegates stair-seat cleanup to
`CastleWallStairsSeatFix.RemoveAllOnOpenScene` (`:315-327`). FlowTrace/Guard
instrumented throughout. **DESTRUCTIVE** (writes the merged scene). LIVE.

### WorldBakeOrchestrator — `Assets/Editor/WorldBakeOrchestrator.cs`
`BakeFullWorld()` (`:17-18`): VillageSceneBuilder.BuildVillage →
ExteriorTerrainBuilder.BuildExterior → then just logs "**OuterWorld removed
(WO-608 MergedWorld). Use WorldMergeBuilder** to rebuild Main_Castle_Overworld"
(`:29`). Step 3 is a no-op by design.

### GONE: OuterWorldBuilder / OuterWorldNavBake
`Assets/Editor/OuterWorldBuilder.cs` and `OuterWorldNavBake.cs` **no longer
exist in the tree** (the old catalog's "BakeWorldNavMesh opens+saves the cursed
Village.unity" risk is dead with them). The only surviving dead-`Village.unity`
opener is `SpawnPathVerifier` (§2).

---

## 9. VFX AUTHORING (owner-tag pipeline)

Law (memory `vfx-map-owner-tags-no-creative-pick`): the OWNER tags VFX keys in
the Caster; code maps key→named-hook **verbatim**, never substitutes.

### VfxCasterWindow — `Assets/Editor/VfxCasterWindow.cs`
`[MenuItem Defenders/Animation/VFX Caster] Open()` (`:111-112`) — the owner's
tagging booth. Reads the generated `Assets/Resources/VFX/HovlVfxCatalog.asset`
(`:34`) + the browse index `Assets/Editor/VfxCasterLibraryIndex.json` (`:35`,
~468 KB); reads/writes picks via `HovlVfxCatalogGenerator.ReadManualPicks/`
`WriteManualPick` (`:380`).

### HovlVfxCatalogGenerator — `Assets/Editor/HovlVfxCatalogGenerator.cs`
`[MenuItem Defenders/VFX/Generate Hovl VFX Catalog] Generate()` (`:269-270`) →
rebuilds the catalog asset; marker **`HOVL_VFX_CATALOG_OK`** (`:45`), withheld
on failure (`:283`). **`ManualPicksPath = Assets/Editor/VfxManualPicks.json`**
(`:52`, ~32 KB — the owner's tag store; manual wins on collision `:346`);
`ReadManualPicks()` `:217` (warn + empty on parse fail), `WriteManualPick()`
`:243-259`. Idempotent.

Consumers: `WeaponVfxMap.cs` (owner-tagged keys only, cites VfxManualPicks.json
line numbers in its comments), tower projectile tiers, auras. Gated by
`[tower-proj-map]` `TowerProjectileMapRegression` (`DataRegression.cs:361`) and
`[vfx-aura-diff]` `VfxAuraDifferentiationRegression` (`:359`).

### Others (one-liners)
- `GearCasterWindow.cs` — `[MenuItem Defenders/Gear/Gear Caster]` (`:132`):
  weapons+armor imaging/curation/offset booth over the canonical 446-item JSON;
  offsets persist through the shared OffsetForge store (no parallel store).
- `SpellsPackVfxMirror.cs` — `[MenuItem Defenders/VFX/Mirror Spells Pack To
  Resources]` (`:29`): copies gitignored Spells Pack VFX into
  `Resources/VFX/Projectiles/` for fresh-clone/WebGL loads.
- `EnemyVfxSetup.cs` — `[MenuItem Defenders/Combat/Setup Enemy VFX Sets]` (`:64`).

---

## 10. RUN SCRIPTS (repo root — the batchmode drivers)

All ASCII-only (PS 5.1), all refuse to start if a Unity process is running
(project lock), all clear a stale `Temp\UnityLockfile` after.

### run-unity-method.ps1
The universal `-executeMethod` wrapper: pinned editor `6000.4.8f1` (fallback
newest 6000.*), args `-batchmode -quit -projectPath -executeMethod <M> -logFile
Builds/<LogName>` (`:51`) — **note: no `-nographics`**, so `RunCaptureHeadless`
renders real pixels through it. Fork-aware wait (Unity relaunch quirk) then
judges from the log (`:76-78`).
**⚠ THE TRAP:** exit 0 requires only the batchmode-clean-exit pattern + no
`error CS` (`:87`) — **a refused gate (marker withheld) or `REGRESSION_FAIL`
still exits 0.** Exit 7 = unrecovered licence error (`:88`), 1 = everything
else. Callers MUST grep the method's own marker.

### run-tests.ps1
Tier-2 gate: `-runTests` headless, judged from the **NUnit results XML**
(`result='Passed' && failed==0 && total>0`), NOT exit codes; prints
`TESTS_OK :: p/t passed` or `TESTS_FAILED`.

### build-windows.ps1
Deletes `Builds/Windows` first (stale-exe native-crash guard), launches
`DesktopBuild.BuildWindows`, licence-error heuristic with recovery detection,
judges success by **exe existence** → `[build] SUCCESS -> <exe> (<MB>)` exit 0.
Memory `desktop-build-after-android-target`: after an APK build pass
`-buildTarget Win64` or SBP/Addressables fails.

### tools/r2-ship.ps1 (NEW 2026-08-20, WO-1130) + the three chains that call it
⛔ **THE one way content reaches players.** `push → verify → judge the MARKER`. Default invocation
**BLOCKS** (exit 16) on parity failure; `-WarnOnly` warns and continues; `-VerifyOnly` uploads
nothing. Callers: **`morning-ship-chain.ps1`** step 2b (blocks, `Die … 16`),
**`overnight-apk-build.ps1`** (blocks), **`install-apk-to-seeker.ps1`** (`-WarnOnly` **deliberately
and only here** — a knowingly-offline/experimental sideload is legitimate).
- **WHY IT EXISTS:** enemy/structure art is served **remotely from R2** with **no local fallback**
  (`Assets/Resources/Enemies` + `.../Structures` were deleted by the CDN migration). Bundle names
  are **content-hashed**, so **every build needs its own push**. An unpushed APK installs, launches,
  and shows tinted capsules with **no error on screen**. Three occurrences: 2026-08-18 (caught by
  hand; `16e22dba3` conceded *"NO GATE COULD HAVE CAUGHT THIS"*), 08-19 (WO-1124, wrong target),
  08-20 (owner played it; device said `HTTP/1.1 404 Not Found` on
  `enemy_art_assets_enemyfam-hollow_*.bundle`).
- **WHY ONE FILE:** the push+verify pair had been copy-pasted into two chains and **already
  drifted** — overnight pushed then verified; morning only verified and printed a FIX command for a
  human. On 08-20 that manual command is the step that got skipped. **A gate whose remedy is another
  manual command is not a gate.**
- **THE TWO ARGUMENT RULES, now spelled exactly once:** `--push` takes the **PARENT** (`ServerData`)
  — `--push ServerData/Android` **flattens** keys to the bucket root and reports `R2_PUSH_OK` while
  uploading objects nobody can read; `--verify-catalog` takes the **EXPLICIT** target
  (`ServerData/Android`) because `ServerData/` holds both platforms and the tool refuses to guess.
  Deletes `Builds/r2-parity.log` before verifying so a **stale log can never read as a pass** (§11
  risk 1 applies here too — judge `R2_PARITY_OK`, never the exit code).
- ⚠ **STILL UNGATED:** a raw `adb install -r <apk>` touches none of these scripts (WO-1130 §5).

### distribute-android.ps1
Pushes the APK to Firebase App Distribution (`firebase appdistribution:distribute`);
AppId from param → `$env:FIREBASE_APP_ID` → gitignored `firebase-appid.txt`;
`-Build` runs the AndroidBuild first; testers/groups params.

### run-autopilot-fleet.ps1
Launches N **player-exe** AutoPilot instances in parallel (no licence needed),
distinct `--seed`/`--run=<i>`, then aggregates every run's break-log into ranked
tickets via the editor-side `AutoPilotTickets.Emit`
(`Assets/Editor/AutoPilot/AutoPilotTickets.cs`). Default `-nographics` (logic/
crash coverage only); `-Graphics` switch renders so per-panel UI shots aren't
blank; `-Phases` filters driver phases.

---

## 11. RISK LEDGER (2026-08-02, prioritized)

1. **`run-unity-method.ps1` exits 0 on gate refusal/FAIL** (`:76`, `:87`) —
   structural; every caller must verify the marker (memory
   `gates-report-success-without-proving-it`). Consider adding an optional
   `-Marker` param that greps and gates the exit code.
2. **`[dungeon-exit]` tag + Guard-label + MARKER collision** — *PARTIALLY FIXED
   2026-08-02:* the DataRegression tag + Guard label for
   `DungeonExitReachableRegression` are now `[dungeon-exit-reachable]`. The two
   suites STILL share the `DUNGEON_EXIT_OK/FAIL` marker literal inside their own
   bodies — allowlisted as named debt in `RegressionMarkerRegression`
   (`KnownDuplicateMarkers`). Renaming one set closes it.
2b. **`SessionRegression` emits `REGRESSION_OK`/`REGRESSION_FAIL`** — **FIXED
   2026-08-02.** It now emits `SESSION_GUARDS_OK/FAIL`, and the legacy
   `Assets/Editor/RegressionSuite.cs` emits `CHECKIN_SUITE_OK/FAIL`. The blast
   radius was larger than this entry recorded: `tools/regression/checkin_gate.ps1`
   was *invoking the 22-case legacy suite* and judging it by the shared
   `REGRESSION_OK` literal, so DataRegression's ~90 registered oracle suites had
   never run in the automated check-in path at all. The gate now runs BOTH and
   requires BOTH markers, and `RegressionMarkerRegression` makes a recurrence red.
3. **Duplicate MenuItem `Defenders/Build/WebGL Player`** — `WebGLBuild.cs:36`
   vs `DesktopBuild.cs:33`, divergent compression/exception settings; the menu
   binds only one. Consolidate or rename.
4. **CheckNpcModels duplicates KayKitNpcImporter's 12-row table**
   (`DataRegression.cs:1346-1370` vs `KayKitNpcImporter.cs:62-100`) — verbatim
   twin dictionaries; a one-sided edit fails the gate confusingly (by design as
   a permission gate, but know it).
5. **GraphDungeonComposer writes only the StreamingAssets layout copy**
   (`GraphDungeonComposer.cs:162-165`) — the Resources mirror is maintained
   elsewhere; dual-copy drift risk against RoomForgeRegression Case 2.
6. **`DungeonBaker.BakeFromFile` wipes the open scene unprompted**
   (`NewScene` `:117`) and batchmode-saves BINARY scenes — mitigated by
   `.gitattributes:45-49` + the ForceText attempt, but the `%YAML` self-check
   only WARNS (`:282-288`). Re-bake only in an isolated worktree (memory).
   `KayKitChallengeOutpost.unity` is NOT under the binary attribute — same
   exposure class.
7. **Fail-by-design suites** (crystal-production, dungeon-dressing,
   modal-registration, ftue-honesty, `DataRegression.cs:326-327`) keep the gate
   RED truthfully until their fixes land — a `REGRESSION_FAIL` needs reading,
   not reflex-reverting.
8. **Legacy still on disk:** `RegressionSuite.cs` (superseded gate, menu still
   registered) and `SpawnPathVerifier.cs` (opens abandoned `Village.unity`
   `:32`). Neither is in the live chain; both invite accidental use.
8b. **Registration is a manual follow-up step** — new suites land in one commit
   and are wired into `RunAll` in another (`61d1c50a` pattern). That is how
   `RepairProbeRegression` gained a fully conformant `Run(out reason)` that
   NOTHING calls (true orphan). When adding a suite, wire it in the SAME
   commit; when auditing coverage, diff the Regression dir against the
   `RunAll` call list.
9. **`BuildOptions.Development` on `DesktopBuild.BuildWindows`** (`:203`) is
   deliberate (DevTools panel) — but means the Windows exe is never a ship
   candidate as-built; WebGL/Android ship `None`.
10. **UICapture RumorBoard daily tab renders empty** in the worst-case fixture
    (`UICaptureLaunch.cs:1122`) — the daily-tab shot is still in flight; don't
    read its emptiness as a bug.

---

*Catalog scope: gate chain + QA (CompileGate, DataRegression + 77 registered
suite calls over the 82-file Regression tree, legacy RegressionSuite,
SpawnPathVerifier), UICaptureLaunch (10 panels), builders (Desktop/WebGL/
Android), KayKit NPC pipeline, RoomForge (6 editor files + runtime Checks),
animator factories (Hero incl KnightMocap / AnimatorSetup / Enemy / Dragon-
Syndrath), scene builders (CastleHub add-only law, WorldMerge, WorldBake stub),
VFX authoring (Caster/Hovl/ManualPicks), and the 5 root run scripts. Other
`Assets/Editor` areas (Catalog/, HudComposer/, Blink importers, Village scene
builder partials, magenta tools, arena/battle builders) are out of this page's
scope — see the area catalogs.*
