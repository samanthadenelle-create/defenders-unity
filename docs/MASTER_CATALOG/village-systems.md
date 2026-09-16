# Master Catalog — Village Systems

> **Footprint cache follow-up, 2026-09-13:** `StructureFactory.MeasureUprightFootprintXZ`
> now resolves art before caching, caches only successful finite measurements, and keys exact
> sizing/orientation inputs plus resolved prefab identity. Manual Euler is applied once through
> `OptsFor`/`Skin`, matching `Create`. Eight before-fix fixture failures are captured in
> `Builds/footprint-cache-baseline.log`; `footprint-cache-after.log` has `FOOTPRINT_CACHE_OK`.
> `footprint-cache-castle-guardrails.log` has `OWNER_CASTLE_FINAL_CHECKS_OK`. This does not
> migrate existing claims, choose storage sizes, or enable authored Barracks movement.

> **Owner castle correction, 2026-09-13:** preserve the saved thirteen objects in
> `Main_Castle_Overworld`, adding the original Arcane Tower as **Cathedral of Learning**.
> Synty storefront substitutions were explicitly rejected as unauthorized. The repaired
> layout is captured in `Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab`.
> `AuthoredCastleStorefront` carries semantic identity separately from the owner's names,
> models, and transforms; marked visuals bypass legacy injector replacement/rotation.
> Stone Quarry/LumberMill/IronMine use the existing `farm`/`lumbermill`/`forge` collector
> keys for stone/wood/iron. IronMine is not a weapons vendor. Crafting uses `workshop`;
> RealmStore retains its direct panel door, outside the build catalog. Weaponsmith and
> Armorer retain distinct `forge` and `armorer` trade identities.
> New marked barracks adoption preserves explicit source identity and exact pose; older
> provenance-free records keep their existing behavior. Authored barracks upgrades retain
> their art. Moving that preserved building currently refuses before changing occupancy;
> an accurate authored-geometry preview is required before enabling that operation.
> Local preview follow-up: `GhostPreview.SetAuthoredEntry` now builds a render-only static
> URP/Lit snapshot with exact ancestor transforms; `MoveToAuthored` takes a full candidate
> quaternion. Entry explicitly refreshes, source hierarchy changes invalidate, and owned
> geometry/materials are cleaned up on disable/destruction. Movement remains disabled:
> this presentation API is not yet connected to the move/occupancy transaction.
> `AUTHORED_GHOST_PREVIEW_OK` in `Builds/castle-preview-local-20260914-third.log` verifies
> independent geometry, texture/tint, refresh/refusal, cleanup and actual catalog transition
> in Edit Mode, plus captures of the actual saved Barracks. Callback bodies were invoked
> explicitly where Edit Mode does not dispatch them; no Play Mode lifecycle claim is made.
> Existing unlock rules remain. These corrections supersede conflicting legacy name/skin
> descriptions below. Evidence and bounded DeepSeek work orders are in
> `Builds/castle-validation-20260913/`; device/Play Mode acceptance is separate from editor proofs.

**Dated 2026-08-02; BuildMode UI row refreshed from source 2026-08-09. Verified from source
(file:line cites), NOT from comments.**

**STALE:** the parenthesised line counts on the BuildMode rows below (other than the Palette/UI +
BuildHudController rows, re-counted 2026-08-09) are from the 2026-08-02 read and have drifted —
e.g. BuildModeController is 3930 not 3477, StrategicPlacementMigration 755 not 474,
UnderConstructionVisual 558 not 258, ObsidianQueueHud 590 not 534, StructureSingleton 651 not 550,
GhostPreview 435 not 402. The prose and the `:line` cites inside each row were not re-verified today;
treat a `:line` on an unrefreshed row as approximate until re-read.
Supersedes the 2026-06-12 edition. Scope: `Assets/_Modules/Village/` subfolders
**BuildMode, Buildings, Walls, Catalog, Arena, Harvest**, plus root
**HubStructureVisualInjector.cs**. (Tutorial/FTUE, Combat-folder tells, EconomyService and
the root economy are catalogued in their own areas now; the 2026-06-12 sections for them are
retired from this file — see git history for the frozen text.)

- **Assembly:** everything below is `DeNelle.Village` (`DeNelle.Village.asmdef`).
- **Namespaces:** `DeNelle.Village` (BuildMode/Buildings/Harvest/Catalog + root),
  `DeNelle.Village.Arena`, `DeNelle.Village.Buildings.Progression`, `DeNelle.Village.Walls`.
- Line counts are `wc -l` on 2026-08-02.

---

## 0. Owner rulings this area is built on (RECORD — binding)

| Ruling | Date | Where enforced |
|---|---|---|
| **Strategic placement is ALWAYS ON** — `ff.strategicplacement` REMOVED (WO-682). New games set the migration marker in `ResetToNewGame`; legacy saves migrate once. | 2026-07-12 | `StrategicPlacementMigration.cs:3-4,49-52` |
| **Destroyed = build fresh at FULL COST.** No repair-of-destroyed, no in-place respawn, no free rebuild (the first-build-free ledger is BURNED on destruction). Supersedes the WO-672 "inoperable shell". | 2026-07-19 + F8 2026-07-30 | `Vfx/Destructible.cs:131-189,210-231` (WO-753) |
| **Singleton-ness is CATALOG-DRIVEN, one authority.** "THERE SHOULD ONLY EVER BE ONE" → a new catalog row with `repo.singleton=true` (+ `repo.bakedTwins`) is fully enforced with ZERO code; the v1 per-building code map is DELETED. | 2026-08-01 | `BuildMode/StructureSingleton.cs:1-17` (WO-819 v2) |
| **Blank founding = truly blank.** On a migrated save a baked twin surfaces only for ids in `GameState.EverBuiltStructureIds` (monotonic; selling never removes). | 2026-08-02 | `StructureSingleton.cs:156-198`, `GameState.cs:518-545` (WO-834, save v36) |
| **Fresh hub: baked stores PRE-STAND, visible + staffed** (CoC/WWCD, "Lever 1") — baked standdown keys on `HasRecord` alone (+ the WO-834 gate), never on has-catalog-row. | 2026-07-24 | `StrategicPlacementMigration.cs:182-208` |
| **ONE true gold button per panel** (WO-832) — the upgrade panel's right-pane CTA is the only gold-filled commit; tabs get a gold underline, cards only select. | 2026-08-02 | `Buildings/Progression/BuildingUpgradePanelMvvm.cs:20-38,91-94` |
| **WO-830 Echo affinity/synergy IMPLEMENTED (pending gates)** — all 6 Echoes prefer Harvest with distinct affinities; the PLAYER picks each Echo's resource (5-chip picker; affinity = match bonus, never a lock); **Maren harvests Crystals** (Bran+Maren = the one doubled affinity, combined trickle slowest); 3 disclosed pair synergies + a hidden tri-synergy (applied-only, NEVER displayed). WO-831 emergence sprite beat wired (art pending, Guard fallback). | 2026-08-02 | `EchoRosterCatalog.cs` / `EchoBonusCalculator.cs` / `EchoAssignments.cs` / `echoes-balance.json`; specs `WorkOrders/WORK_ORDER_830_*.md` + `_831_*.md` |

---

## 1. BuildMode/ — the CREATE verb (place / move / sell / persist)

CoC-style grid building, wired end-to-end. Enter → angled-overview cam + frozen waves →
palette card → info panel → arm → ghost → **two-step commit** (a world tap only DROPS the
pending ghost; the PLACE button is the only commit — `BuildModeController.cs:9-14`) → charge
after commit → append `GameState.BaseLayout` + `MarkEverBuilt` → replay on load via
`BaseLayoutLoader`.

| Class | File (lines) | Responsibility / key API (verified) |
|---|---|---|
| **BuildModeController** | BuildModeController.cs (3477) | MB singleton. Entry/exit + the whole placement loop. `Instance`; static `event BuildModeChanged` (:53) + **`event StructurePlaced(string id)`** (:59 — the LIVE placement signal; tutorial + StructureSingletonBootstrap ride it); `IsActive`; `HasArmedEntry` (:65); camera pan/zoom/orbit w/ 45° yaw detents (:106-111); single-pointer drag-pan for WebGL (:93-105). Commit appends BaseLayout **and calls `state.MarkEverBuilt(_armed.id)`** at the same seam (:1816-1822, WO-834), raises StructurePlaced guarded (:1827), spawns the vendor NPC for the placed storefront (:1829+), then returns to the carousel via `CancelArmed(afterPlacement:true)` (:1854-1864). `IsSingletonBuilt(entry)` (:1909-1918) **delegates to StructureSingleton.IsBuilt** — the bespoke body is deleted; `SingletonAlreadyBuilt` traces the arm/place refusal (:1920-1925). Sell: `SellSelected` frees grid, `RemoveLayoutEntry(itemId, cell)` (:2054, :2755-…), `BaseLayoutLoader.Forget`, cancels its BuildTimer job (:2059-2061). |
| **StructureSingleton** *(+ StructureSingletonBootstrap)* | StructureSingleton.cs (550) | **THE one authority for only-ever-one (WO-819 v2 + WO-834).** Static. `IsSingleton(id)` = catalog `repo.singleton` (:97-102). `IsBuilt(id)` union cheapest-first: BaseLayout record → ACTIVE baked twin (`GameObject.Find`, :121-124) → live `PlacedStructure` → live `Building.IsAlive` (:110-138); per-frame memoized `IsBuilt(CatalogEntry)` for palette polls (:144-153). **`MayBakedTwinSurface(id, everBuilt, migrated)`** — the pure WO-834 blank-town rule (:176-186): pre-migration → true (bake owns the town); migrated → true iff id ∈ EverBuiltStructureIds. `Enforce(id)` (:215-264): placed exists → stand twins down ("placed wins"), UNLESS the WO-673 migration latch holds (`IsManagedId && !StanddownActive`, :231-237); nothing remains → resurface twins (post-sell) unless the blank-town gate is closed → then twins are actively STOOD DOWN (:251-259). `EnforceAll()` sweeps every singleton catalog row per hub load w/ surfaced/suppressed tally (:271-284). Event seam: `NotifyPlaced`/`NotifyRemoved` → `SingletonResolved`/`SingletonReleased` (:74-80, :291-316). Baked twins come **ONLY from catalog `repo.bakedTwins`** (:420-425). Barracks twin resurface routes through `HubStructureVisualInjector.EnsureBarracksSurfaced` so the WO-724 unlock gate holds (:354-364). Bootstrap (:445-549): DDOL, castle-hub scenes only (`MainCastle_Hall`/`Main_Castle_Overworld`, :452-453), waits ≤300 frames for GameStateService, subscribes StructurePlaced once. |
| **StrategicPlacementMigration** *(+ Bootstrap)* | StrategicPlacementMigration.cs (474) | WO-673 L3, **always-on since WO-682**. (a) **One-shot migration writer** `RunIfNeeded()` (:276-361): converts the 8 baked storefronts (`BakedRows`, :85-95 — bakedName→itemId census incl. workshop=weapons-Forge / forge=Armorer trade convention) + 2 runtime stations (`StationRows`, :110-114, hardcoded fallback (±11,0,2)) into grid-quantized BaseLayout records (≤~1.5 m accepted snap drift, yaw→nearest 90°, :387-399), then the **WO-834 template grant**: marks the whole template + `barracks` ever-built (:339-346), sets `StrategicPlacementMigrated`, latches the scene handle, saves. (b) **Standdown oracle**: `StanddownActive` (:157-173 — marker set AND home hub AND not-the-migration-load); **`StanddownActiveForBaked(bakedName, out itemId)`** (:195-208) = `StanddownActive && (HasRecord || !MayBakedTwinSurface)` — Lever-1 HasRecord-only + the WO-834 second clause so a blank founding loads blank at scene load; `StanddownActiveForStation` = unconditional when active (WO-703/BLANK-1, :222-225); `ShouldReplayRecord` — managed ids replay only under standdown (:234-238). Lever-1 read-only census accessors `BakedStorefronts()`/`StationAnchors()` feed CastleVendorNpcInjector (:132-149). |
| **BaseLayoutLoader** *(+ BaseLayoutLoaderBootstrap)* | BaseLayoutLoader.cs (595) | Runtime twin of VillageSceneBuilder. `LoadFromState` once per instance (`_loadedOnce` latch, :128-168; hub-skip set now only legacy `CastleHub`, :54-57). `Rebuild` (:202-250) applies the WO-673 replay filter (withheld-count telemetry) + partial-base Warn. `Spawn` (:258-407): CatalogRegistry → `StructureFactory.Create` (Guarded), yaw = `yawSteps*90 + yawOffset` (:280), persisted `worldY` seats wall-walk defenses (:276), TWO footprints (blocker=unrotated vs grid claim=rotation-honest AABB, :299-313), **walls/gates get the "Structure" physics layer** so tower LoS linecasts hit them (:325-334), `wallMounted` grants +25% elevation range mult (:353-360), DEF-208 tier reskin + `ApplyTierStats` for level>1 (:363-376), re-arms `UnderConstructionVisual` for mid-build saves (:386-388), reload-notifies the vendor injector (:401-404). `AddFootprintBlocker` (:416-512): blocker sized to rendered bounds ×0.85 eave-inset, clamped [1 cell..claim]; NavMeshObstacle carve (no rebake); strips all child colliders — root footprint box is the ONE collider-of-record. Bootstrap (:538-594): DDOL, ensures a loader whenever `SceneRouter.Castle` loads (F8-39 towers-vanish fix). |
| **GhostPreview** | GhostPreview.cs (402) | Translucent green/red ghost via VisualFactory; MPB tinting; tracks created materials and destroys them in `Clear()` (no per-re-arm leak, :41-44). API: `SetEntry` (:63) / `SetPlaceholder` (:142) / `MoveTo(pos,yawSteps|yawDegrees)` (:171/:176) / `SetValid` (:185) / **`SetReason(msg)`** (:206 — the floating "why it's red" world-space label, owner 2026-07-24) / `CurrentPosition` (:231) / `Hide`/`Clear`. Applies the entry's OrientationFix under the yaw (WYSIWYG, :36-39). |
| **PlacedStructure** | PlacedStructure.cs (177) | Runtime marker per placed structure: `itemId, gridCell, footprint, yawSteps, yawOffset, level, worldY, wallMounted, sellValue`, `TierVisual`. |
| **PlacementGrid** | PlacementGrid.cs (317) | Shared cell-occupancy grid singleton; `WorldToCell/CellToWorld/FootprintCells(metres[,yaw])/Occupy/Free/ClearAll`. |
| **ObsidianQueueHud** | ObsidianQueueHud.cs (534) | WO-773/778 — the tucked-away **work-queue modal** (player-facing "Queues"): per-channel active slots + FIFO pending with live 1 s timers for **Builders / Training / Research** (:63-68), never a mixed list. Code-built uGUI on ElarionUiKit; self-installing DDOL (:73-80). Opened via `ObsidianQueueGate.RequestToggle()` (Core seam, `Core/UI/ObsidianQueueGate.cs:26` — HUD never references Village); repaints on `BuildTimerService.QueueChanged`. Colorblind-safe ASCII state markers (`>` running / `...` queued / `-` free, :26-28). Sell-time Instant / Ad-skip / Buy-slot buttons call existing BuildTimerService APIs. |
| **UnderConstructionVisual** | UnderConstructionVisual.cs (258) | WO-612 scaffold visual keyed by `KeyFor(data)`; attached at place + reload while `BuildTimerService.IsBuilding`. |
| **StructureTierVisual** | StructureTierVisual.cs (112) | DEF-208 per-tier visual (bronze/silver/gold or per-tier model). |
| Palette/UI set | BuildPaletteUI.cs (1213), BuildPaletteVM.cs (273), StructureCardVM.cs (247), BuildStructureInfoPanel.cs (369), BuildPreviewModal.cs (531), BuildSelectionUI.cs (227), **BuildHudController.cs (877)**, BuildTabRow.cs (128), BuildWalletRow.cs (168), LiveWalletSource.cs (108), BuildPlaceButton.cs (98), BuildFeedbackToast.cs (194), SiblingPanelSettings.cs (48) | Code-built palette/carousel + card VMs (singleton cards render "Built" via `IsSingletonBuilt`), structure info preview, move/sell/upgrade action panel, browse/placing intent HUD, wallet row, reject-reason toast. |
| **BuildHudController — chrome as of WO-1010 D10/D14/D17/D19 (2026-08-09)** | BuildHudController.cs (877) | ONE landscape overlay canvas (1920x1080, match 0.5, sortingOrder 906, :207-220). Four bands, all fixed reference px: **D19** thin bottom-CENTRE resource strip sized to `BuildWalletRow`'s actual chip count (`BuildResourceStrip`, :243-293) — **the old full-width TOP bar and its "BUILD MODE" label are DELETED**; **D10** compact corner exit labelled just `Done` (76 px visual inside a 112 px invisible MinTouch pad, `BuildCornerDone`, :308-328) — **no "X" glyph**; **D14** the three placement verbs live in a lean FIXED right-edge rail anchored BOTTOM-right and growing upward (`BuildIntentBar`, :392-478) — **the ghost-following chip cluster and its flank/flip layout are RETIRED**; the ghost keeps ONLY its name+cost pill, the one thing `LayoutGhostControlsNow` (:770-841) still follows and clamps (against the reserved bands, not just the screen). **D17 (closed 2026-08-09 evening)**: all three rail verbs render pack SPRITES — `element/check` / `element/rotate` (authored beside `element/cross` in the pack style) / `element/cross`; `MakeVerb` stays sprite-or-null so a missing pack degrades to the ASCII words, never a tofu glyph; sprite-path invalid = dim+disabled with the worded reason on the pill. **PUBLIC seams for other lanes:** `RailReservedWidthPx` (:115) and `ResourceStripReservedPx` (:145) — the carousel/quick-tab lane seats clear of these bands without a cross-edit. `PinSize`, `AllowTwoLineLabel` and the `IntentBtnW`/`PlaceBtnW`/`ExitBtnW` constants were **deleted** in the same pass (:660-663, :849-854). |
| Input seam | IBuildInput.cs (93), DesktopBuildInput.cs (98), LeanTouchBuildDriver.cs (364) | S6 seam; LeanTouch driver is still the ONLY Lean.Touch file in BuildMode; WebGL falls back to the controller's single-pointer drag-pan. |
| Bridges | BuildButtonBridge.cs (136), BuildModeHudBridge.cs (96) | HUD→Toggle wiring by reflection (Village can't ref HUD); combat-HUD hide while building. |
| **RotationCorrectionRegistry** | RotationCorrectionRegistry.cs (149) | WO-282 per-prefab persistent yaw correction (PlayerPrefs). |

**Placement-ownership seam in one breath:** bake/injector own a structure until the one-shot
migration writes records; the NEXT hub load flips `StanddownActive` → records replay + bakes
hide. `StructureSingleton.EnforceAll` then keeps the invariant every hub load: placed wins →
twins down; sold-to-nothing → twins resurface (if ever-built); never-built on a migrated save →
twins suppressed (blank town). Three cooperating gates, one owner each — do NOT add a fourth.

---

## 2. Buildings/ — timers, towers, progression, the upgrade panel

### 2.1 BuildTimerService — the multi-channel "Obsidian" queue (WO-172 → WO-773)
`Buildings/BuildTimerService.cs` (786). ONE DDOL service, **channels Builder / Train /
Research** (`Core/Jobs/JobKind.cs:63 ChannelId`) that never share slots (CoC parallel
workers). Queue math is the pure `DeNelle.Core.Jobs.ObsidianQueueEngine`
(`ObsidianQueueEngine.cs:33`, headless-testable); this MB owns `GameState.ObsidianQueue`
persistence + wall-clock `TimeSource.NowUnixMs()` → **offline-fair**: on load
`SweepAllChannels` completes overdue jobs and cascades pending pulls (:94-105). Legacy
`GameState.BuildJobs` folded into Builder by the v34→v35 migration (:28-33). API (verified
lines): `SlotCount`/`BuySlot` (:159/:168), `ActiveJobsOf`/`PendingJobsOf` (:188/:195),
`StartBuild`/`StartUpgrade` (:217/:226), generic `Enqueue(kind[,channel],targetId,duration)`
(:305/:309), `IsBuilding`/`RemainingSeconds`/`Progress` (:342-356),
`InstantFinishPrice`/`CanWatchAdToSkip`/`WatchAdToSkip`/`TryInstantFinish` (:368-413),
`CompleteJob`/`CompleteChannelJob` (:457/:460), `RepointJob` (:498),
`CancelJob`/`CancelChannelJob` (caller-owns-refund, :530/:533), `ReorderPending` (:562).
Events `JobCompleted/JobStarted/JobSkipped/QueueChanged` (:59-68). Also seeds
`RaidEntryGate.ArmyStatus` once GameState exists (first-frame flash fix, :111-120).

### 2.2 Towers (two generations + combat/LoS)
- **DefenseTower.cs** (973) — the LIVE auto-firing defensive structure. Role-priority
  targeting; `TowerAllegiance {PlayerOwned, EnemyOwned}` (:28-30 — garrison turrets target the
  player party through the same IDamageableStructure seam). **LoS gate:** acquisition + fire
  are blocked when a wall/gate on the **"Structure" layer** sits between muzzle and target —
  `Physics.Linecast(fPos, target, LayerMask.GetMask("Structure"))` (:500-530); fliers exempt
  (:518). Layer applied by `BaseLayoutLoader.Spawn` (:325-334) for placed walls/gates and by
  `WallSegment.RebuildCollider` for configured ones. `ElevationRangeMult` (wall-walk +25%) set
  from the persisted `wallMounted` flag. Breaks at 0 HP → `Destructible.NotifyBroken` (removal,
  §2.5); repair path via `Repair()`.
- **ArcaneTower.cs** (565) — the magic tower variant (element default from catalog;
  aura VFX owned via Destructible).
- **TowerCombat.cs** (617) — shared firing math incl. `BlockedByWall` (the LoS mirror).
- **Tower.cs** (1404) — the **legacy DEF-74/75** placement-tower component
  (TowerPlacementSystem lane): level 1-3 visual swap, upgrade VFX, reflection-based camera
  shake (:31-33 — grandfathered; §10 forbids NEW reflection). Layer fallback
  `"Tower"→"Building"` (:514-515). Kept for the legacy TowerPlacementSystem/BuildMenu path —
  not the BuildMode path.
- Support: TowerPlacementSystem.cs (427, legacy placement), TowerSwapMenu/Service,
  TowerConstruction(Queue), TowerPersistenceService (161), TowerRangeRing, TowerDamageVisuals,
  ProjectilePool/PooledProjectile, ProjectileVFXCatalog (408) / ProjectileArtCatalog (207),
  StructureBurn (413), StructureAttackAlert (266), TowerLoopDevHarness (200).

### 2.3 Progression/ (ns `DeNelle.Village.Buildings.Progression`)
- **ResourceBuildingProgression.cs** (597) — data-driven leveling for Farm/Lumbermill/Forge;
  balance table still **hardcoded in `Build()`** (:233), NOT JSON. Contains the
  Progression-namespace `ResourceCost(HarvestResource, int)` — the OTHER ResourceCost (see
  Risk ledger). `ResourceBuildingState.cs` (234) runtime levels + `TryUpgrade`;
  `TechTree.cs` (84) Magic-gated nodes (`arcane_forge`).
- **BuildingUpgradeVM.cs** (694) — the PURE ViewModel (IPanelViewModel, no UnityEngine.UI;
  `BuildingUpgradeVM.cs:10-16`). `CreateDefault(buildingId, onClose)` resolves economy +
  default building (:93-104); collector-id normalization (`collector_farm`→`farm`,
  :108-110); serves BOTH families — city tier ladder (`BuildingTierCatalog` →
  `BuildingUpgradeService.TryUpgrade`) and legacy resource buildings
  (`ResourceBuildingState.TryUpgrade`) (:18-23); per-tile `CostFor/EffectFor/KeyLine/Gate`
  maps (:74-86, gates `village`/`building-tier`/`cost`); synthetic `villagetier` row routes
  to `VillageTierService.TryUpgrade` (:47-51). Disambiguates the two ResourceCosts via
  `using EcoCost = DeNelle.Village.ResourceCost` (:35).
- **BuildingUpgradePanelMvvm.cs** (1508) — the VIEW, **reworked 2026-08-02 (WO-832 "one true
  button")**: master-detail per the owner-approved 060241 mockup — LEFT horizontal tier-card
  path (cards ONLY select; no in-card gold commit, :22-28), RIGHT detail pane whose big gold
  Upgrade CTA is **the one bright-gold commit on the panel** (:37-39); tabs
  Upgrade|Skills wear a gold UNDERLINE when selected, never the CTA's gold fill (:20-21,
  :91-94). Clean obsidian palette built in-code (:58-68), no UXML, no BuildObsidianPanel.
  Render-dedup via content-signature hash (economy ticks no longer rebuild the tree,
  :99-108). Flag `FeatureFlags.BuildingUpgradePanel` (default ON since WO-476) = kill-switch.
  Spawned by `BuildingUpgradePanelMvvmBootstrap.cs` (85).
- Collectors: ResourceCollector (341) + Service/Registry/Bootstrap, CollectorStackView (437)
  + CollectorStackPropCatalog, ResourceBuildingHarvester (149), CompletedUpgradeApplier (113),
  AutoHarvestService (54), BuildingPerkService (77), VillageTierService (75).

### 2.4 Building interaction / misc
Building.cs (366, `IsAlive`/`BuildingId` — one of StructureSingleton's truth probes),
BuildingInteractable (635), BuildingCatalog (235), BuildingSign(+Injector), InteractableSign,
MarketplaceInteractor, NPCUpgradeStation(+VM), HealingFountain (633), CrystalMine (638) +
CrystalVisual/CrystalVfx, DungeonPortal (299), Door/Drawbridge/CastleDoor controllers,
MobileInteractButton (371), BuildMenu (714) + BuildMenuVM/BuildMenuHudBridge (legacy),
UI/ (TowerManagerPanel, LevelUpSkillPopup(+Bootstrap), TowerUpgrade/Empower buttons,
PlacedTowerListVM, LevelUpVM, ProgressBar, Billboard), DailyQuestTowerBridge,
EmpowermentDebugTrigger.

### 2.5 Destructible (in Vfx/, catalogued here — WO-753)
`Vfx/Destructible.cs` (285). **The ONE owner of a destructible's death lifecycle.**
`Ensure(go)` composed by Building/DefenseTower/ArcaneTower Awake (:58-66). `TeardownVfx`
(:94-129): pool-return held VFXHandles, stop+disable ArcaneAuras, destroy registered roots —
also from `OnDestroy` (catch-all, :277-282). **`NotifyBroken`** (:150-189) executes the full
ruling: VFX teardown → despawn the bound vendor (VendorSeatMarker, real-null checked,
:163-170) → free grid + `BaseLayoutLoader.Forget` + drop the persisted record (:176-183) →
**`BurnFreeBuild(itemId)`** (:210-231 — destruction burns `GameState.FreeBuildsUsed` so a
baked structure's rebuild is never free, owner F8 2026-07-30) → toast "rebuild at full cost"
(`OfferRebuild`, :258-268; interactive confirm deferred) → `Destroy(gameObject)`.
NOT a damage model — never reads HP (:27-29).

---

## 3. Walls/ (ns `DeNelle.Village.Walls` for tier data; segments in `DeNelle.Village`)

| Class | File (lines) | Responsibility |
|---|---|---|
| **WallSegment** | WallSegment.cs (231) | One perimeter section; `IDamageableStructure`. STOP: **THE "Tier 1-3 / `{1, 1.6, 2.56}` table" IS RETIRED (WO-1480, 2026-09-06)  -  the toughness is now DERIVED and the ceiling is READ, not restated.** The literal array `{ 1f, 1f, 1.6f, 2.56f }` defined a divisor for tiers **1..3 only**, and paired with `SetTier`'s literal 1..3 clamp it was a **NINTH hardcoded structure ceiling**  -  WO-1108b replaced eight of them with `RepoProps.MaxStructureLevel` and **missed this one**, so a level-4 wall could silently take level-3 damage reduction. Now: **`public static int MaxTier => RepoProps.MaxStructureLevel`** (`:158`) is the hard bound the clamp may never exceed  -  a row still opts in with its own `repo.maxLevel` (`wall_wood` authors 2 today)  -  and **`public static float ToughnessFor(int tier)`** (`:167`) returns `Mathf.Pow(TierToughnessStep, t-1)` with `TierToughnessStep = 1.6f` (`:106`), **reproducing the old numbers EXACTLY at the tiers that existed** (x1 / x1.6 / x2.56) while keeping every level the ceiling now admits defined. STOP: **Never re-hardcode a level ceiling here or anywhere.** Incoming contact damage is DIVIDED by the result (:57-65). `Configure()` by VillageController; BuildMode-placed walls attach a bare WallSegment (no Configure)  -  the Structure layer comes from `BaseLayoutLoader.Spawn:325-334`. **Height (2026-08-06):** `wall_wood`/`wall_stone` stay at `heightMul` **1.0** (4.0 m)  -  DELIBERATELY unchanged by the `d42e2817` cadence pass; see S4 DELTA (lowering narrows, which opens PATHABLE GAPS in already-saved wall runs). |
| **WallTierData** | WallTierData.cs (215) | The CoC wall ladder DATA only: naming Wood→Iron→Reinforced Steel, per-tier mesh prefab path (**placeholders, owner meshes pending**), upgrade cost (Iron, then Iron+Crystals). Durability deliberately NOT duplicated — `ToughnessFor()` reads WallSegment's table (header :6-20). **Height is NOT here either** — the per-row `heightMul` in `structures-catalog.json` owns it, and the wall rows are the one group the 2026-08-05 cadence ruling left unauthored (§4 DELTA, `d42e2817`). |
| **WallRepairController** | WallRepairController.cs (1057) | The player repair LOOP (scan→select→modal prompt→confirm). **Repair cost ruling 2026-07-11:** damage-fraction × the structure's own BUILD cost in its own materials; Crystals never spent on repair (header :13-22). Spends via EconomyService.TrySpend + the GameState Wood/Iron mirror (GrantSpendable both-sides pattern). HUD cross-wired by reflection (module isolation). NOTE: "destroyed = rebuild at full build cost" here predates WO-753 — a destroyed structure is now REMOVED by Destructible, so the repair loop only ever sees damaged-but-standing structures. |
| Support | RepairTarget (290), RepairHighlight (285), HubRepairAffordance (341), WallRepairHudBridge (242), WallRepairStrings (75), WallLayout (402) | Selection wrapper, amber/violet highlights, hub affordance, HUD bridge, strings, square-ring layout math. |

---

## DELTA 2026-08-21 — the fallback-catalog rescale, and `Village/Items/` (a folder no area file owned)

Read from source 2026-08-21.

### `Catalog/CatalogBootstrap.cs` is now **393 lines** (the §4 row says 228) — 21 fallback cost fields rescaled

The WO-1129 economy-sink rescale moved `structures-catalog.json` and left the hardcoded fallback
mirror stale. **21 fallback cost fields** were brought back to parity; every divergence was exactly
the documented per-ladder factor (build x4, L1->L2 x10, L2->L3 x14) — a uniform deliberate rescale,
not corruption. `BuildEconomyRegression` gate 12 (`[fallback-parity]`) reflects over the constructed
rows and fails the build on ANY field divergence.
⛔ **VERIFIED BY COUNT, and it is the reason WO-1137 exists: `RegisterFallback()` constructs THREE
rows** — `tower_ground_archer`, `tower_ballista`, `tower_arcane_spire` — **against 28 `entries` in
`structures-catalog.json`.** It has now drifted FOUR times. See the master risk ledger.

### `Village/Items/` — NEW `ItemMoteShapes.cs` (383), ns `DeNelle.Village.Items`

⚠ **This folder was not in any `MASTER_CATALOG/<area>.md` scope line before today.** Catalogued here.

The SHAPE FAMILY table for world drop motes: `TintFor(id)` · `HasAuthoredIdentity(id)` ·
`ResolveGlyph(id)` · `IsDrawn(glyph)` · `StableHash(s)` · `FamilyName(glyph)` ·
`SignatureFor(glyph)` / `SignatureForId(id)` · `PartsFor(glyph)`, plus `struct MotePartSpec`.
- **The defect it closes:** `ItemPickupSpawner` spawned ONE hardcoded gold sphere for EVERY drop, so
  a chest that rolled Iron Scrap and one that rolled a Heartwood Bough left byte-identical objects
  on the floor. The drop had no identity at all.
- ⭐ **THE LESSON, proven the same day on `IngredientPickup`:** the sibling defect LOOKED like "the
  tint is broken". It was not — every tint parsed fine, but the authored tints were PASTELS on a
  **non-emissive URP/Lit sphere**, so under light they all washed to the same white pellet. Colour
  was never going to carry identity, and for this owner (red/green colourblind) it could not carry
  it in principle. **Identity rides SHAPE.**
- **It is not a new parallel identity table.** Every `consumables.json` / `materials.json` row
  already carries a `glyph` char and `ItemIdentity.GlyphOf` already surfaced it; this file is the
  missing half, glyph -> silhouette.
- **Unauthored ids** (`loot-tables.json` drops four ids no catalog owns: `monster-hide`, `wild-herb`,
  `tattered-cloth`, `rare-essence` — a PO content gap the `[item-identity]` oracle reports) resolve a
  family by a stable FNV-1a hash of the id, so each still gets a distinct deterministic silhouette
  and is still NAMED from `ItemIdentity.DisplayName`. The right fix is authoring the rows; this file
  refuses to hide the gap behind an identical pellet.
- The spec side (`ResolveGlyph` / `PartsFor` / `SignatureFor` / `TintFor`) constructs no GameObject
  and never throws, so an EditMode oracle can read the silhouette of every dropped id without
  entering play mode. Construction stays in `ItemPickupSpawner`. Oracle:
  `ItemDropMoteIdentityRegression`.

---

## 4. Catalog/ — the data-driven structure buckets

| Class | File (lines) | Responsibility |
|---|---|---|
| **StructureFactory** | StructureFactory.cs (864) | **The ONE creation path** (WO-148): editor bake, runtime placement, and save replay all call `Create(entry, pose, parent)` (:78). Runtime-safe (no UnityEditor). WO-764 Y-height normalization: `YHeightVariable = 4f` × `repo.heightMul` — one number rescales the whole town (:42-68). **THE CADENCE (owner ruling 2026-08-05, `0ac59581` + `d42e2817`) — see the DELTA block below.** *(Corrected 2026-08-06: this row previously read "towers 1.25, siege 0.75"; towers are **1.2**, the owner-ruled ANCHOR, and 1.25 is now the LANDMARK value held by exactly one row.)* **behaviorId → component is an explicit switch, add-cases-not-reflection** (`AttachBehaviorImpl`, :680; cases `DefenseTower/ArcaneTower/WallSegment/Gate/ResourceCollector/CrystalMine/HealingFountain/GameplayBuilding`, :684-787; building-type map :836-859). Also `MeasureUprightFootprintMetres`, `ReskinForLevel` (DEF-208), Composite→`CreateGroup` (:90-94). |
| **CatalogBootstrap** | CatalogBootstrap.cs (228) | Fills CatalogRegistry from **`Data/Canonical/structures-catalog.json`** (StreamingAssets source, Resources copy WINS on WebGL) via CanonicalJson; tiny 2-tower hardcoded fallback only if JSON fails — **that fallback is now PARITY-GUARDED, see the DELTA block**. **Live JSON `version: 8`** *(corrected 2026-08-06: was `version: 6`; `0ac59581` bumped 6 -> 7, `d42e2817` bumped 7 -> 8)* with 12 `repo.singleton:true` rows, 9 carrying `repo.bakedTwins` (WO-1250, 2026-08-27): fountain_healing, pet-house→EchoHollow, market→Marketplace, mill, **forge (Weaponsmith)→Blacksmith_Weapons_Storefront**, **armorer→Forge_Armor_Storefront**, jeweler→Jeweler_Gems (twin tolerated-absent — removed from the bake), arcane-tower→ArcaneTower_MagicUpgrades, collector_farm→Windmill, collector_lumbermill→Lumbermill, barracks→CastleBarracks. ⚠ `workshop` (Crafting Station) no longer authors a baked twin — the retired workshop→Blacksmith / forge→Forge_Armor crossing is what left Weaponsmith+Armorer standing on a new load. Overlays `StructureOrientationLocalStore` (local wins). |
| **BuildCategoryRegistry** | BuildCategoryRegistry.cs (244) | BuildType→CatalogType palette mapping from `build-categories.json` (Town/Defenses/Walls/Collector), data not switch. |
| **StructureOrientationLocalStore** | StructureOrientationLocalStore.cs (136) | Orient-tool dials upserted to `persistentDataPath/structure-orientations.json`; overlaid at startup so owner dials survive sessions/rebuilds; `[OrientRecipe]` console line = the bake-into-repo source. |

### DELTA 2026-08-06 - ONE height cadence across every structure

*Sourced from commits `0ac59581` (the archer-tower anchor), `a67f1e77` (the code-side guidance
correction) and `d42e2817` (the cadence pass). The AUTHORITY now lives IN THE DATA as the
top-level `_heightCadence` key of `structures-catalog.json`, so the ruling travels with the
file; `StructureFactory.YHeightVariable` and `RepoProps.heightMul` carry the summary.*

**The family** (owner ruling: "all of the other structures stay within that cadence...
relatively the same size... all scaled to the same point" - ONE FAMILY, NOT ONE NUMBER),
anchored on the archer tower at **1.2**:

| heightMul | Metres | Group |
|---|---|---|
| **1.25** | 5.0 | **LANDMARK** - `arcane-tower` (the Cathedral of Magic) ONLY. The town's single apex, tallest by design. |
| **1.2** | 4.8 | **TOWER - THE ANCHOR.** Measured: 2.778 m across = **49.9% of a House_Medieval_Medium's 5.562 m fitted diameter**, i.e. the ruling's "exactly half as wide"; a 1x1 claim at the 3 m cell. The wizard tower and arcane spire had been floating 4% above it on the old WO-764 1.25 and came off it on 2026-08-05. |
| **1.0** | 4.0 | **BUILDING BASE** - houses, production, vendors, storage, collectors, civic. **The WIDTH REFERENCE** the ruling measures against. |
| **0.75** | 3.0 | **SIEGE ENGINES** (`tower_catapult`, `tower_siege_tower`) - machines, not architecture, deliberately UNDER the house line; also stops a wall-mounted Sky Ballista out-topping a tower once stacked. |
| **0.35** | 1.4 | **DECORATION** (`deco_torch`) - unauthored it had inherited the 1.0 building default, i.e. **a WALL TORCH fitted to 4 m, as tall as a house**. |

**`heightMul` fits BOUNDS, so equal multipliers do NOT mean equal apparent size.**
`collector_farm` reads as the worst outlier at **1.4** and **is not one** - its windmill blades
inflate the model's Y bounds, so at 1.0 its BODY fitted far smaller than a boxy forge at the
same 4 m (the owner felt-reported exactly that as the shrunk farm, `31b41d19`). **1.4 is a
COMPENSATION, not a cadence value. Left alone with a row note - do NOT "fix" it to 1.0.**

**WALLS ARE DELIBERATELY UNCHANGED** (`wall_wood`, `wall_stone`, `gate_stone` stay at 1.0 = 4 m;
each carries a `_heightNote`). At 4 m a palisade sits at nearly tower height, so it looks like
the obvious next correction. It is not, and the reason is not visual: **the fit is UNIFORM, so
lowering a wall NARROWS it by the same factor**, and every wall in an already-saved town sits on
the cell pitch of its OLD claim. A narrower segment does not re-pitch its neighbours - **it
opens PATHABLE GAPS in existing wall runs and shrinks the NavMeshObstacle with them.** That is
worse than an overlap and **invisible to shrink-is-safe reasoning**. Needs a measured
`StructureHeightAudit` run (before/after cell claim) plus a **save-migration decision** first.

**Why every applied change was SAFE:** each one is a **SHRINK**, and the grid claim is
`ceil(measured / 3 m)`, so a shrink can only REDUCE a cell claim - saved placements cannot
reload overlapping. (RAISING a multiplier is the dangerous direction; always state the
before/after cell claim.) **Tiers 2/3 inherit with no extra keys** - `ReskinForLevel` re-fits
every tier model through the same `OptsFor`/`EffectiveVisualHeight` call and `RepoProps` has no
per-level height field.

**Height and footprint are NOT independently authored.** `BuildModeController` measures the XZ
bounds of the model **AFTER** it is fitted to `EffectiveVisualHeight`
(`StructureFactory.MeasureUprightFootprintMetres`), so ONE uniform number moves both - 1.25 ->
1.2 shrank the archer's diameter by the same 4%. There is no width dial and none is needed. The
authored `PlacementRules.footprint` is **only** the fallback for a prefab that fails to skin;
its archer value had drifted to **2.5** against the catalog's **1.75** and was corrected
(`0ac59581`).

**`repo.visualHeight` is DEAD** - deprecated by WO-764, authored ZERO times, and no longer read
by `StructureFactory.EffectiveVisualHeight` (`RepoProps.cs:267-274`). It is retained only so
older serialized JSON still deserializes. **Do not author against it.** (One legacy editor
reader survives: `Assets/Editor/WallTools/RaidBaseGenerator.cs:350-351`.)

**Catalog version + dual copies:** `structures-catalog.json` bumped **6 -> 7** (`0ac59581`, dual
copies verified byte-identical by md5, 38211 bytes each - exactly one byte less than before, the
character dropped by 1.25 -> 1.2, positive proof no other row moved) and **7 -> 8**
(`d42e2817`). **Live = 8.**

**`CatalogBootstrap.RegisterFallback` (the JSON-load-failure path) had ALL THREE rows drifted**
from the catalog it mirrors - a fallback that has diverged silently ships different geometry
whenever the JSON fails to load. It now has a **reflection-based parity guard** that
deep-compares every public field of the constructed `CatalogEntry` / `RepoProps` /
`PlacementRules` / `OrientationFix` against the parsed catalog row. It **extends
`BuildEconomyRegression`** (failure tag `[fallback-parity]`,
`BuildEconomyRegression.cs:1202-1250`) and rides the existing `BUILDECON_OK` marker - **no new
suite, so the `REGRESSION_OK <n>/<n>` count does not change** (`21c11327`).

---

## 5. Arena/ (ns `DeNelle.Village.Arena`)

Two generations coexist deliberately:

- **BattleArena.cs (2394)** — the **generic real-time battle spine (WO-482)**, LIVE. PvE
  entry `BeginEncounter`: builds an open kite arena (floor + runtime NavMesh, NO structures)
  staged at `ArenaCentre = (5000, 0, 5000)` (:81), `ArenaWorldRadius = 200` (:85) with
  `IsArenaPosition` so shared systems can tell arena hits from home-scene bleed-through
  (:87-90); warps hero in (south) vs enemy family (north) via the shared
  EnemyFactory/EnemyBrain; real-time fight through the EXISTING combat stack (zero new combat
  code); win/lose/flee → `BattleRewardSummary {Xp, Wisdom, Wood, Iron, GearName}` (:58-70) →
  return warp under ScreenFader. Logic-only (presentation reused, header :22-25). Flag
  `FeatureFlags.OverworldEncounter`. Companions: BattleArenaHud (uGUI result/HUD),
  BattleHud9Zone, BattleStarRating, ArenaDeathCam, ArenaBiomeDressing, EncounterParams,
  ArenaVM/ArenaPaletteVM.
- **ArenaMode.cs (476)** — the verified **async-PvP raid loop** (ARENA MVP WO-386/388/389/390),
  kept working untouched; generalization onto BattleArena is extraction-later by design
  (BattleArena.cs:8-11). Herald entry (ArenaHeraldSpawner 445), defense setup + attack recruit
  controllers, hardcoded seed catalogs (ArenaCatalog 3 opponents / ArenaDefenseCatalog 6
  defenders, pool 50 / DefensePatternLibrary — all still `TODO data-driven`), ArenaPanel,
  ArenaProgressStore, ArenaNavMeshBaker (player-castle path only), ArenaHudBridge (staged
  battle hides home HUD; restore under return fade).
- **ArenaWalletService.cs (127)** — **STILL the client-side SKR wager STUB**: PlayerPrefs key
  `dotr-arena-skr-balance`, seed 500 (:38-41); swap point isolated to
  `Debit/Credit/Balance` (header :10-17). Never trust for real funds; real wallet =
  WalletService(CurrencyKind.Skr) + backend escrow (not built).

---

## 6. Harvest/ — Echo workforce + offline accrual

### 6.1 The Echo faucet (LIVE — WO-830 affinity/synergy implemented 2026-08-02, pending gates)
- **EchoService.cs** — owns EchoCount (1..`MaxEchoes=6`, wave-earned every
  `WavesPerEcho=5`), the pooled SILO (hours-capped buffer), Dump-to-wallet, unlock events.
  Persisted in GameState (EchoCount/SiloResources/WavesCompleted, schema v25+). Income:
  `RatePerSecond = EchoCount × (BaseRatePerHour/3600) × EchoBonusCalculator.AggregateHarvestMultiplier() × (1 + HarvestRateBonus())`
  (the count-quadratic spine folded ONCE, never `GlobalHarvestMultiplier` too);
  `SiloCapacity = SiloCapHours × BaseRatePerHour × EchoCount × AggregateHarvestMultiplier()`
  (WO-830 reconcile — capacity now shares the rate's multiplier basis so fill-time ≈
  SiloCapHours; the STEWARD talent factor stays excluded). Offline accrual shares ONLY the
  clock (`GameState.LastHarvestClaimMs`) with OfflineHarvestService — separate faucets, no
  double-grant. **Dump is a 5-WAY split** (WO-830): Wood/Iron/Food/Gold/Crystals by
  `HarvestTargetWeights` (largest-remainder, exact pool); Wood/Iron/Food+Crystals bank via
  `EconomyService.GrantSpendable(..., crystals:)`, Gold via `EconomyService.AddCoins`.
  Founding-echo teaching one-shot `FoundingTaughtKey`.
  **WO-1434 (2026-09-06) — the split is now a PURE function, and the silo can be PREDICTED
  without dumping.** `ReadHarvestWeights(out wW,wI,wF,wG,wC)` (assignment weights + the
  defensive classic split when nothing is assigned) and `SplitPool(pool, w…)` (largest-
  remainder, sums to EXACTLY the pool) were extracted out of `DumpSilos`, which now calls
  them; `PredictDumpSplit()` returns what a dump would route right now (all-zero, never
  null, when there is no service/state/pool); `ShareWood/ShareIron/ShareFood/ShareGold/
  ShareCrystals = 0..4` index that array; the instance property `SiloAtCap`
  (`Silo >= SiloCapacity - 0.5`) is the "the Echoes have STOPPED gathering" fact the return
  screen must say in words (`FOUNDATIONAL_RULINGS.md` §7). ⛔ **Do NOT re-inline the
  apportionment into `DumpSilos`** — two copies of the split is exactly the defect WO-1434
  cured (the return screen and the tap must read one producer).
  ⚠ **`DumpSilos` does NOT burn its overflow** — it settles `s.SiloResources -=
  bankedFromSilo` (the APPLIED basket, WO-1392), so what the bank refuses stays in the silo.
  The `[Flow:Bank] BANK FULL … LOST N` warn is the BANK saying it *refused* the units; both
  live callers retain. Any copy telling the player silo units were lost is a defect
  (pinned: `HarvestResultCopyRegression` `[silo-never-burns]`).
- **EchoBonusCalculator.cs** — the ONE place the curve lives (WO-738/830);
  `AggregateHarvestMultiplier()` = count × (1 + per-echo [base + affinity-match + level]
  + running PAIR bonuses + 6-set + **hidden tri-synergy** when ALL pairs run — the tri term
  is APPLIED-ONLY, excluded from `ReadoutFor().BonusPct` and every displayed string
  (WO-830 §3d; FlowTrace edge-logs activation)). Match law = ASSIGNED resource == affinity.
  `HarvestTargetWeights()` (5-way, by actual assignment) replaced the old 3-way
  `HarvestResourceWeights`. `SynergyFor()` = the DISCLOSED pair readout for the card.
- **EchoRosterCatalog.cs** — the fixed 6-spirit code table; order == EchoCount; each Echo =
  the awakened essence of a soul the Heart guards (WO-752). **WO-830 identity rows: ALL SIX
  `PreferredLane = Harvest`** with affinities Aldwin/Frost→**Food**, Elowen/Nature→**Wood**,
  Corvin/Shadow→**Gold**, Bran/Storm→**Crystals**, Doran/Earth→**Iron**, Maren/Fire→
  **Crystals** (the one deliberately doubled affinity; Repairs removed 2026-08-02).
  New `HarvestTarget` enum + token/label helpers; WO-831 `EmergeLine` per entry +
  `LoadEmergence` (Guard-wrapped, portrait/text fallback).
- **EchoAssignments.cs** — per-echo token storage in `GameState.EchoLanes` CSV. **WO-830
  grammar:** `<resource>:<level>` (wood/iron/food/gold/crystals — the primary form, resource
  preserved) alongside `<lane>:<level>` + `idle`; a v33 generic `harvest:N` defaults on read
  to the echo's AFFINITY resource; pre-v33 bare wood/iron/food read at that resource L1. NO
  schema bump (grammar documented on `SaveSchema.PersistedState.EchoLanes`).
  **`PickableLanes = { Harvest }` only** (dead Crafting chip removed);
  **`PickableResources` = the 5 targets** the card's resource picker offers.
  New APIs: `ResourceTokenOf` / `TryTargetOf` / `AssignHarvest` / `ResourceLabelFor`.
- UI/feedback: EchoWorkforceHud + VM + Bootstrap, EchoRosterView +
  EchoRosterVM/EchoCardView/EchoCardVM (**WO-830: the card is a 5-chip RESOURCE PICKER** —
  affinity disclosed as "Favors: X" + the "(best -- this Echo's calling)" text flag; the
  disclosed pair-synergy line via `SynergyText`; hidden tri never worded), EchoUnlockFeedback,
  **EchoUnlockDialogue (owner ruling 2026-08-05 "it should just simply be one screen": the
  WO-831 two-state beat is COLLAPSED — the emergence screen's headline, EmergeLine arrival copy,
  artwork (`Resources/Echoes/Emergence/<PortraitName>_emerge.png`, LFS; degrades to
  portrait→text, never blocks) and CanvasGroup fade all fold into the top of the ONE awakening
  card; no Continue advance. Buttons 3→2: "I accept your power" + "Tell me more" survive, the
  shared kit Close is retired locally as a duplicate dismiss. Both CTAs seat at one fixed
  480x132 ref-px box on one baseline; all copy parents to the plate (`layout.body`) which
  carries a RectMask2D)**, EchoWispInjector,
  ~~EchoSpiritPresentation~~ (DELETED 2026-08-16, WO-993 - the founding Echo's ethereal hover/yaw-drift/Aura_HeartPulse layer; the guide is a GROUNDED WOLF that walks and keeps its body + PetHeroLeash lead), EchoWaveUnlockBridge, EchoBalanceCatalog (owner-tunable knobs,
  echoes-balance.json: WO-830 `preferredLaneMatchBonus 0.40`, 3 `crossBonuses` pairs
  Provisions/Forge/Fortune @ +0.10, `hiddenTriSynergyBonus 0.25`, Bran+Maren rates 0.45 each
  so the combined crystal trickle stays the slowest faucet).
- Oracles: `EchoSpecializationRegression` (affinity table, balance dual-copy, token grammar,
  pair/tri math incl. applied≠displayed, save round-trip, dump credit across all 5 wallets)
  + `EchoResourcePickerRegression` (chip projection, picker verb, card strings, synergy line,
  WO-831 emergence data/fallback).

### 6.2 WO-830 — status
Implemented 2026-08-02 (this section previously tracked it as pending). Spec + banners:
`WorkOrders/WORK_ORDER_830_echo_harvest_affinity_synergy.md`; sprite beat:
`WORK_ORDER_831_echo_emergence_sprite_beat.md`. Emergence ART is not yet supplied — the
loader degrades to the portrait until the 6 LFS files land under
`Assets/Resources/Echoes/Emergence/`.

### 6.3 Offline accrual + legacy workers
OfflineHarvestService (335, WO-115 claims-on-resume; `OfflineHarvestBootstrap` DDOL) +
OfflineHarvestResult (55) + TimeSource (42, the ONE clock seam) + WelcomeBackPopup (205) +
**WelcomeBackDoorsVM** (WO-1408 — the pure destination decider; see the WO-1408 block below).
WorkerManager (363) + Worker/NodeFillIndicator/WorkerManagerBootstrap — the WO-117 dispatch
system, **retired for V1**: `UseOfflineCatchUp` off and `ClickToDispatch` disabled by
EchoWorkforceBootstrap so it never banks the same nodes (EchoService header :32-34);
OuterWorld-gated anyway. HarvestSourceRegistry (31).

**WO-1434 (2026-09-06) — the return screen is ONE row per resource, from EVERY producer.**
Proven from the owner's device (build 358161): the popup drew 3 of 5 aggregated rows and put
a reward's `+` on amounts that banked zero — she tapped COLLECT on 42,782 and got 0.
- `OfflineHarvestResult` gained a **fifth axis**: `OfflineSiloLine` / `SiloPending` /
  `SiloTotal` / `SiloAtCap` / `HasSiloNews`, and **`HasSummaryContent` now carries the silo
  term** (`Total>0 || HasMendNews || HasJobNews || HasCollectorNews || HasSiloNews`). Without
  it a town with idle nodes, empty collectors and a FULL silo got no screen at all.
- `OfflineHarvestService.AttachSiloPending` (private, Guard-wrapped, called from the claim
  path) reads `EchoService.PredictDumpSplit()` — **it never dumps**; COLLECT still routes to
  `CollectorStatusGate.RequestCollectAll`, which is what calls `DumpSilos`.
- **`BuildReturnRows(result)`** (live: headroom from `ResourceCollectorService.HeadroomFor`)
  and **`BuildReturnRows(result, Func<HarvestResource,int> headroom)`** (PURE, the oracle
  seam) merge BOTH producers per resource in `ResourceCollectorService.RailOrder` and return
  `ReturnRow { Resource, Word, FromCollectors, FromSilo, Pending, Headroom, Banks, Waits,
  NothingBanks }`. Copy helpers: `ReturnRowLabel` (the `+` number is ALWAYS `Banks`, never
  `Pending`), `ReturnRowDestiny`, `ReturnFooterLine`, `SiloStalledLine`.
- ⛔ **RETIRED: `PredictCollectWaits` / `CollectWaitLine` / `WelcomeBackPopup.AddCollectWaitRows`
  and the string `"Storage nearly full - N <res> will wait"`.** They were a SECOND list under
  the rows restating each row's own integer — the owner's "way too much here". The destiny is
  now the row's right-hand column (`AddPlateRow(..., valueSplit: 0.46f)` for a sentence).
- Oracles: `OfflineHarvestRegression` case 4 `[one-row-per-resource]` /
  `[no-gain-without-headroom]` + case 5 `[every-producer-rendered]` (both pins MOVED with the
  copy, rulings in-file), and `HarvestResultCopyRegression` `[silo-never-burns]` (the old
  "were not added to storage" assertion was **inverted** — it had been false since WO-1392).
- **Settled by capture, do NOT re-litigate** (the measurements are the owner's 2026-09-06 save
  and live in `WorkOrders/WORK_ORDER_1434_…md` §8 — read them there, never copy them here):
  the hidden rows were the ECHO SILO, not the offline haul; nothing burns on either producer;
  the "a built silo contributes nothing to the stone ceiling" hypothesis is **KILLED**. What is
  true of the CODE: `silo` (displayName **Stoneyard**) authors `repo.storageResource = "stone"`
  + `storageCapacity 1000` (`structures-catalog.json:1500-1515`), `TownBankCapacity.MaxOf` =
  base + `storageCapacity` over BUILT containers of that resource, and `everBuiltStructureIds`
  is **monotonic — it is not a placement record**, so a silo appearing there proves nothing
  about the cap. Read the placed set (`StructureSingleton.HasPlacedInstance` /
  `BaseLayout`) before ever calling a cap wrong.
- **Balance is UNRESOLVED and is the owner's call.** Three capacity systems are sized against
  different bases with nothing reconciling them: collectors (hours of production), the Echo silo
  (`SiloCapHours x rate`), the town bank (the container ladder). The recommendation — make one
  derive from the other — is recorded in WO-1434 §8 and was **not implemented**; raising the
  ladder alone re-opens the WO-1128 §3.5 clock-forge bound in proportion.

**WO-1408 (2026-09-06) — the return screen has a NEXT DOOR.** The away report told the player
what they earned and then dropped them on a HUD whose loudest surface is the store card, so a
returning player's first reason to tap was to spend (`REVIEW_MERGED.md` row 7).
- **`WelcomeBackDoorsVM`** (`Assets/_Modules/Village/Harvest/UI/WelcomeBackDoorsVM.cs`) is the
  ONE place a destination is decided. `Build(result, raidCapable, armyUsed, armyCap,
  heartfireLit, heartfireMax)` is **PURE** (no `UnityEngine` reference — pinned) and returns
  `Rows` (`WelcomeBackDoorRow { Label, Detail, DoorText, Door, DoorContext, TraceKind }`) plus an
  optional `ReadyLine` / `ReadyDoorText` / `ReadyDoor`. **The View only draws.**
- Rows exist ONLY when true: `FINISHED WHILE AWAY` → `PanelId.Manage` on the tab derived from
  the job's card VERB (`ManageTabFor`: TRAIN→`Troops` wins the mixed case, RESEARCH→`Research`,
  else `Buildings` — the only strings `ManageScreenPanel.Open(string)` accepts);
  `ATTACKED` → `PanelId.DefenseReport`. **No empty state, no disabled door.**
- `OfflineHarvestResult` gained a REPORT-ONLY attack axis (`AttackCount` / `AttackBreachName` /
  `AttackOutcomeWord` / `HasAttackNews`), filled by `OfflineHarvestService.AttachAttacks`, which
  **reads `DefenseReportLedger.All()` and never appends or marks-read** (consuming the unread
  badge would delete the signal the door carries the player to). ⛔ **These are deliberately NOT
  terms of `HasSummaryContent`** — the five-axis gate stays exactly as WO-1434 left it.
  ⚠ Live-empty today: `DefenseResolution`'s own doc says away time becomes PRESSURE, not a
  resolved battle, so only a fight ending on the window edge produces a row.
- **⛔ THE READY DOOR IS `RAID` → `PanelId.JourneyDeck`, AND THERE IS NO `START WAVE`.** WO-1408's
  sketch reads "Heartfire is full - a wave is ready"; `HeartfireCharges.cs`' own header states
  Heartfire has "exactly one sink - marching" and that "RAID ORDERS is dead". Heartfire buys no
  wave and no wave-start door exists. The ready line needs **all three** of `RaidCapable`, a
  non-empty army and a lit charge, and the signals are read at **popup build time** (the reveal
  is deferred to a hub scene, so a claim-time snapshot would be a boot default).
- **⭐ THE DOOR'S ORDER LOOKS BACKWARDS AND IS NOT.** `WelcomeBackPopup.CollectThenRoute` arms
  `PanelManager.SetReturnDoor(...)`, then `Dismiss()`, then `PerformCollect()`.
  `ResourceCollectorService.CollectAll` opens a `HarvestOverflowModal` on the SAME exclusive
  arbiter, so a synchronous `PanelRouter.Open` would swap the harvest result off screen the
  instant it appeared. The WO-1400 return-door arbiter already solves it: a SWAP keeps the door,
  a close-to-nothing fires it. **One mechanism, no coroutine, no second navigation path.**
- The collect verb is `PerformCollect()`, shared by `CollectAndDismiss` (the COLLECT button,
  whose geometry literal is still pinned by `AwaySummaryReportRegression` case 5) and every
  door — one implementation, so the button and the doors cannot drift. `AddCompletedJobRows`
  early-returns once the door row named the same jobs.
- Door rows are `DoorRowH = 0.21f` (the body is 0.60 x 0.84 x screenH, so a `RowH` row is ~57px
  at 1200 — under half `MinTouchPx` 112; 0.21 gives ~127px at 1200 / ~114px at 1080) and the door
  face fills the plate 0..1, because insetting it gives the measurement back. They are drawn
  BEFORE the mend/collector lines so the actionable row survives a full body.
- Pinned by **`WelcomeBackDoorsRegression`** (`Assets/Editor/Regression/WelcomeBackDoorsRegression.cs`,
  348L)  -  **oracle-drivable with no canvas, no scene and no PlayMode**, which is the whole reason the
  decision lives in the VM. Cases: `[two-rows-with-doors]`, `[nothing-means-nothing]`,
  `[manage-tab-is-real]`, `[raid-door-routes]`, `[ready-needs-all-three]`,
  `[popup-routes-through-the-router]`, `[collect-first-then-route]`, `[rows-are-ascii]`,
  `[trace-line]`. Its header records the MEASURED red on the pre-change tree:
  `grep -c "BuildObsidianButton" WelcomeBackPopup.cs` = **1**, `WelcomeBackDoorsVM` did not exist, and
  `PanelRouter` was referenced nowhere under `Village/Harvest/`.

---

## DELTA 2026-09-06  -  WO-1411 (the build flow says what it costs) + WO-1405 (Manage rows say what they buy)

### `StructureCardVM`  -  two NEW static seams, both MODEL-SIDE by ruling

`Assets/_Modules/Village/BuildMode/StructureCardVM.cs`. The merged UI review (row 10) measured eight
category cards where **no card said what was affordable**: the player opens a door, reads five prices
they cannot pay, and backs out having spent taps to learn nothing.

- **`static int AffordableCount(CardCollectionDefinition)`** (`:200`)  -  a **FOLD over the two
  authorities that already decide what one card says**, never a new rule:
  `BuildCollectionBrowser.IsCollectionItemVisible` (the ONE offer authority, so the count can never
  include a row the browser would not draw) and this VM's own `Affordable`/`Locked` (the SAME
  projection the item card renders). STOP: **A second predicate here is exactly how a door promises two
  builds and opens onto five "Unaffordable" cards.** Built singletons are excluded  -  a structure whose
  one allowed instance already stands is not a build choice, however affordable.
- **`static string AffordabilityWords(int)`** (`:322`)  -  `"nothing affordable yet"` / `"1 you can
  build now"` / `"N you can build now"`. **Never a bare number and never a colour** (colourblind law);
  "nothing affordable yet" says the door is still worth remembering rather than reading as an error.
- **`readonly struct PlacementSummary` + `static PlacementSummaryFor(CatalogEntry)`** (`:271`,`:285`)  -
  `CostWords` / `Seconds` / `CrewWords` / `Line`. (!) **This widens the VM's game-state read,
  deliberately and in one place**  -  the class header's "the sole game-state read stays in
  `CreateForEntry`" is now **two** seams, both HERE in the model. That is the **RULING, not a
  preference**: the `[ui-mvvm]` conformance oracle failed `BuildPreviewModal` for reading
  `GameStateService` directly (canon 9  -  a View never touches game state, derived text is model work).
  The View now paints ONE string it is handed.
- STOP: **NOT ONE NUMBER IS INVENTED OR RE-DERIVED**  -  each term comes from the seam that already owns it,
  so the line cannot quote a price or a wait the placement then contradicts:
  - **COST** = `FreeBuildAvailable` + **`SoftcappedCostFor`** (the softcapped value  -  what the ghost
    actually charges), spelled by the ONE shared `CostFormat`. While the first-build freebie is live
    the price slot shows **NOTHING** (owner ruling WO-1010 D20, the same rule the collection card obeys).
  - **TIME** = tier from **`CostFor`** (the INTRINSIC weight  -  a freebie does not make a build instant
    and the softcap surcharge does not stretch the timer) -> the config curve -> `GraceAdjustedDurationMs`
    under `GraceReasonFor`: **the exact three steps `BuildModeController` runs at commit.** Quoting the
    raw curve would print minutes over a build the FTUE finishes in seconds; a hardcoded duration would
    be a guess wearing a suffix. `0` = unknown, and the term is **omitted rather than a fiction quoted**.
  - **CREW** = free Builder slots (`SlotCount(Builder)` minus its active jobs), in **WORDS**
    ("Builder free" / "Builders busy - joins the queue")  -  a bare `0 / 2` leaves the player to work out
    whether that is good news.
  - (!) **ASCII separator `" . "`**: a typed middle-dot renders as **tofu** on the shipped TMP atlas.
- Pinned by **`BuildAffordabilityWordsRegression`** (marker `BUILD_AFFORDABILITY_WORDS_OK`), **minted
  RED against commit `949e848a0`**  -  each pin names the exact fact absent there.

### `ManageScreenVM.CompassSideOf`  -  WHERE a placement stands, in WORDS

`Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:1122` (WO-1405). The measured defect
(`docs/qa/UI_REVIEW_2026-09-05/04-manage-defense.png`, device build 355952) was a row reading
**`"Arcane Spire - grid 5, 16 - L1 -> L2 / Iron 540"`**  -  a **developer grid coordinate on a player
screen**. Rows now read `"north side"` / `"east side"` / `"town center"`. **Never a coordinate**
(owner ruling S2 #5, written to the default NO).

- (!) **THE AXES ARE READ OFF `PlacementGrid`, NOT ASSUMED.** `+Z` is north and `+X` is east because
  `PlacementGrid.CellToWorld` maps `cell.y` to world Z and the grid grows **NORTH ONLY** as
  `gridHeight` increases. **A cell index alone cannot answer this**  -  the same cell number means a
  different side on a grid with a different origin, which is precisely why the coordinate was
  meaningless on screen. The Heart of Elarion stands at world `(0,0,0)`, so the placement's world XZ
  **is** its offset from the Heart.
- (!) **`grid.origin == Vector3.zero` is the NOT-YET-INITIALISED sentinel `Awake` itself tests for** and
  is treated as such  -  reading it as a real origin would put the whole grid north-east of the Heart and
  call **every** placement "north".
- **HEADLESS-PURE**: with no scene in a regression run it falls back to the SHIPPED defaults
  (`cellSize 3`, south edge `-45`, X centred  -  the same numbers `PlacementGrid.Awake` seeds). It
  **never returns `""` and never throws**, so a row cannot silently lose its location clause. Within
  one cell of the Heart on BOTH axes there is no honest side to name -> `"town center"`.
- *(US spelling flagged, not decided silently: there is no player-facing precedent for the word
  anywhere in the corpus  -  measured  -  and the only in-repo spelling is US.)*
- (!) The `AfterUpgradeText`-missing warning beside it is **HONEST ABOUT ITSELF**: today `after` is
  composed unconditionally, so that branch is **UNREACHABLE**. It is a NET for the day the sentence is
  re-sourced from an authored field  -  **do not read its silence as evidence of anything.**
- Pinned by **`ManageRowBenefitRegression`** (marker `MANAGE_ROW_BENEFIT_OK`), a LIVE half driving
  `ManageScreenVM` off a GameState fixture plus a SOURCE half for the one contract no fixture can
  observe. Cases include `[location-is-words]`, `[no-developer-coordinate]`,
  `[coordinate-literal-retired]`, `[defense-row-names-a-benefit]`, `[building-row-names-a-benefit]`,
  `[research-row-names-a-benefit]`, `[troop-upgrade-names-an-effect]`.
- Copy law: the ready line says **"Heartfire n / n lit"**, never "full" — `PostureSignals`
  names it `HeartfireLit` precisely so a pool-fullness metaphor cannot creep toward the currency
  `HeartfireCharges.cs` forbids. (`HeartfireRegression`'s currency-lint is scoped to
  `HeartfireCharges.cs` + `HeartfireService.cs` + the canonical catalogs — read at source
  2026-09-06, `HeartfireRegression.cs:340-354`, `:416-445` — so it does not reach these files;
  the vocabulary is honoured anyway.)
- Oracles: `WelcomeBackDoorsRegression` (9 cases; registered in `DataRegression.RunAll` as
  `[welcome-back-doors]`). Capture: `RunWelcomeBackCaptureHeadless` now emits
  **`WELCOME_BACK_CAPTURE_OK 6/6`** — the original no-doors fixture plus `WelcomeBackDoors_*`.

---

## 7. HubStructureVisualInjector.cs (root, 507) — the baked-hub visual seam

Runtime re-skin of baked castle-hub structures to lightweight Resources models (no scene
edit). The **Swaps table** (:63-87) is the hand-dialed row set (Tripo yaw-90 convention,
WO-764 fit-to-height; CastleBarracks row at :84). Also `Place`s new props (colosseum) with
the ONE blanket `GroundY = 0` rule (:100-109) and exposes `RuntimePlacedProps` for the
AutoPilot prop-seating oracle (:91-97).

**Blank-town gate seam (verified):** each swap first asks
`StrategicPlacementMigration.StanddownActiveForBaked(bakedName, out itemId)` (:277) — which
now folds the WO-834 `MayBakedTwinSurface` clause — and hides the bake when true;
`CastleBarracks` additionally gates on `BarracksUnlock.IsUnlocked` (:291).
**`EnsureBarracksSurfaced()`** (:165-183) reactivates + re-skins the baked barracks only when
unlocked — the route `StructureSingleton.ResurfaceBakedTwins` uses for the barracks twin.
**`ResurfaceStorefront(bakedName)`** (:307) re-activates + re-skins any other stood-down
storefront (idempotent, `LightSkin_` marker child) — the post-sell resurface body.

---

## 8. Risk ledger (prioritized)

1. **WO-830 implemented 2026-08-02 (pending CompileGate + DataRegression):** the roster/
   assignments/calculator now match the ruling (see §6.1). Residual risks: the WO-738 doc's
   dialogue copy is banner-flagged STALE; WO-831 emergence ART is not yet supplied (loader
   degrades to portrait); the two new oracles must be registered in `DataRegression.RunAll`
   by the committer.
2. **Comment drift — catalog version:** `StructureSingleton.cs:17` and `:418` say
   "structures-catalog.json **v5**"; the live JSON is **`version: 8`** *(corrected 2026-08-06;
   this line said `version: 6` — `0ac59581` bumped 6 -> 7, `d42e2817` bumped 7 -> 8)*. Harmless
   today (fields unchanged) but the §15 canon rule says fix the comment on next touch — and the
   drift is now THREE versions wide, which is exactly how a "v5" comment gets trusted.
3. **Two `ResourceCost` structs still coexist** (`DeNelle.Village.ResourceCost` 4-field wallet
   cost vs `Buildings.Progression.ResourceCost` resource+amount). Live mitigation:
   `BuildingUpgradeVM.cs:35` aliases `EcoCost`. Any new Progression-namespace file must
   disambiguate or it will silently bind the wrong one.
4. **StructureSingleton world scans:** `IsBuilt`/`Enforce` use `GameObject.Find` +
   `FindObjectsByType` (incl. inactive in `FindByNameInclInactive`, :429-435). The per-frame
   memo (:85-86) covers palette polls, but `EnforceAll` on hub load is O(scene) per singleton
   row — fine at 12 rows; watch it if the singleton set grows large.
5. **Three singleton rows have NO baked twin** (fountain_healing, mill, armorer): for them
   `IsBuilt` rests on records/live probes only, and the blank-town gate is a no-op (nothing
   to suppress). Expected, but a new bake for one of them MUST add `repo.bakedTwins` or the
   sweep can't see it.
6. **ArenaWalletService is still a PlayerPrefs stub** (seed 500 SKR) and the Arena seed
   catalogs are still hardcoded with TODO-JSON notes — unchanged since the MVP. Do not
   demo it as real custody.
7. **Legacy tower generation lives on:** `Tower.cs`/`TowerPlacementSystem`/`BuildMenu` are the
   pre-BuildMode lane (Tower.cs still uses reflection for camera shake, :31-33). New defense
   work goes through DefenseTower + BuildMode; don't extend the legacy trio.
8. **WallTierData mesh paths are placeholders** (owner meshes pending) and `WallRepairController`'s
   destroyed-structure prose predates WO-753 (destroyed structures are removed before the
   repair loop can see them) — behavior is correct, header text is stale.
9. **Wood/Iron dual-wallet hazard persists** (EconomyService in-session pool vs GameState
   ledger; `GrantSpendable` writes both). WallRepairController and EchoService.Dump both
   deliberately use the both-sides path — keep doing that.
10. **Silo capacity vs rate: RECONCILED (WO-830, 2026-08-02)** — `SiloCapacity` now scales by
    the same `AggregateHarvestMultiplier()` basis as rate (talent factor still excluded by
    design), so fill-time ≈ `SiloCapHours` with specialization active.
