⚠ SUPERSEDED 2026-09-17 — see `docs/qa/UI_SCREENSHOT_CATALOG_2026-09-17.md` (WO-1860). This doc's
route table predates the clan system, WO-1376/1394/1396/1397/1399/1400/1811 nav changes, the
`ManageFlow` state matrix, and the Owned-Town / Army-screen captures — several of its "NO-CAPTURE"
and "dead end" rows are now stale (e.g. CosmeticShop and SeasonTrack/BattlePass now have real doors
and captures; BarracksPanel.cs/ShopPanel.cs no longer exist in the tree). Kept frozen per CLAUDE.md
§15 (dated point-in-time ledger) — do not edit the body below; read the 2026-09-17 catalog instead.

# UI Screen Graph - code-true, 2026-09-04

Read-only inventory. Every claim below cites a file:line opened this session. Nothing was
measured on a device; this is the graph the CODE describes, built for the owner's ask
("audit the screens reachable from the manage screen ... then same for the other main
buttons from Hud"). Line numbers are as of the working tree on `feat/synty-art-retheme`.

Legend: `->` opens; `<-` closes to; `[cap: Name]` = headless case in
`Assets/Editor/UICaptureLaunch.cs` writing `Builds/ui-capture/<Name>_<WxH>.png`
(OutDir `UICaptureLaunch.cs:156`); `[NO-CAPTURE]` = no case in that file.
(`Assets/_Modules/Core/Diagnostics/UICaptureMode.cs` is a SECOND, older harness writing to
`Builds/UICaps/` (`:68`) with 13 router routes at `:274-286`; it is listed separately and is
NOT the pre-ship `UI_CAPTURE_OK` producer.)

---

## (a) ASCII tree from the HUD root

Root = `HudKitController` (`Assets/_Modules/HUD/Kit/HudKitController.cs`).
Bar model = `Assets/_Modules/Core/HudModel/HudActionBarModel.cs`: `ButtonCount = 7` (`:113`),
`MaxVisibleFaces = 4` (`:121`), `TalkAvailable => PostureSignals.TalkAvailable` (`:163`),
`RaidCapable => PostureSignals.RaidCapable` (`:164`). Map is dormant at ordinal 4 and is never
built (`HudKitController.cs:845-852`).

```
HUD (town)  [cap: AdaptiveHudPeaceful / AdaptiveHudGearOpen / AdaptiveHudCombat  UICaptureLaunch.cs:2751,2825-2850]
|
|-- ACTION BAR (HudActionBarModel; 4 visible max)
|   |-- Build            HudKit:770-776 -> _owner.BuildRequested
|   |     -> BuildButtonBridge.cs:15-16 -> BuildModeController.Toggle() :444-448 -> EnterBuildMode :463
|   |         BUILD MODE (no canvas modal; camera+palette+HUD)   [cap: BuildPaletteDock_open/_collapsed :4936-4985]
|   |         |-- BuildPaletteUI.Show :322-334 -> BuildCollectionBrowser.Show   [cap: BuildCollections(+Page2) :7202-7226]
|   |         |     |-- collection page -> entry tap -> OnEntrySelected -> Arm (ghost)   [cap: BuildGhostChips_* :4612-4694]
|   |         |     |-- "DefenseUpgradeCard" :172-176 -> Close(); PanelRouter.Open(Manage,"Defense")
|   |         |     `-- BACK (base ObsidianNavigationWorkspace.cs:136-138 -> Back :78) / Close :94
|   |         |-- Build HUD intent bar (BuildHudController.Create, BuildModeController:3882-3901):
|   |         |     Rotate L / Rotate R / PLACE / Cancel->Exit / Done->Exit      [NO-CAPTURE as a bar]
|   |         |-- palette OnOrientRequested :3858 -> OpenOrientEditorForArmed :3937 -> BuildPreviewModal :3952  [cap: BuildPreview :2147-2168]
|   |         |-- palette OnRestoreRequested :3862 -> CancelArmed (back to carousel)
|   |         |-- BuildStructureInfoPanel: DISABLED 2026-06-19 (:3864-3872, no OnCardTapped subscriber)
|   |         |-- placed-structure upgrade :2509 -> PanelRouter.Open(BuildingUpgrade, id)
|   |         `-- Exit :586 (Done / Cancel / palette exit) <- HUD
|   |
|   |-- Talk (only if TalkAvailable)   HudKit:781-788 -> HudCommands.Talk() (Core/HUD/HudCommands.cs:114)
|   |     -> TalkHudBridge.cs:54 RegisterTalk, :91 NearestTalk -> NPC Interact -> DialogueView (HUD/DialogueView.cs:22)
|   |         DIALOGUE  [cap: DialogueCompact_Aldwin :1601 ; DialogueOptions_2opt/_4opt :1106-1111]
|   |         |-- close = panel X -> _vm.Close() (DialogueView.cs:265, :618)
|   |         `-- dialogue VERBS (DialogueCommandSink.cs:82-111) -- which the data actually uses:
|   |               OpenRumorBoard  :82  -> RumorBoard        (used: Resources/Data/Canonical/dialogue/dialogues.json)
|   |               OpenUpgrade     :83  -> BuildingUpgrade   (verb NOT present in dialogues.json)
|   |               OpenShop        :86-93 -> PartyShop        (used)
|   |               OpenCraft       :95  -> Crafting          (NOT present)
|   |               OpenAlchemy     :99  -> ConsumableCrafting (used)
|   |               OpenJeweler     :103 -> JewelerCrafting   (used)
|   |               OpenTalents     :106 -> HeroSkillTree     (NOT present)
|   |               OpenCosmetics   :107 -> CosmeticShop      (NOT present)
|   |               OpenRealmStore  :111 -> RealmStore        (used)
|   |             + DialogueService.cs:113 -> PartyShop(structureId)
|   |
|   |-- Hero (enum Bag)   HudKit:790-808 -> PanelRouter.Open(HeroDeck) :800
|   |     HERO DECK  PlayerDeckWorkspace.cs:67 OpenHero; cards :578-583   [cap: HeroWorkspace :6435-6447]
|   |     (every card: OpenCard :530-536 CLOSES the deck first, then opens - there is no "back to deck")
|   |     |-- Bag        -> Inventory      HeroInventoryController.cs:156 -> InventoryUIBuilder   [cap: Bag :6702-6720 ; BagUse_* :6482]
|   |     |     |-- rail: Gear / Weapons / OffHand / Armor / Trinkets / Potions  (InventoryUIBuilder.cs:375-383)
|   |     |     |-- Gear section action -> OpenGearPreview :691 -> EquipmentPanel
|   |     |     |-- Skills -> OpenSkillTree :671 -> HeroSkillTree
|   |     |     |-- Map -> OpenRealmMap :661 ONLY when FeatureFlags.MapTab (default OFF, FeatureFlags.cs:842)
|   |     |     `-- close: Scrim :114 -> Close ; chrome :123
|   |     |-- Equipment  -> EquipmentPanel.cs:159 register; chrome :189; Scrim :175; BACK :954; -> HeroSkillTree :974   [cap: HeroEquipment(+Compare/Equipped) :2669-2731]
|   |     |-- Skills     -> HeroSkillTreePanelMvvm.cs:467; chrome "TALENT TREE" :1940 (X -> _vm.Close); popup :2055; -> EquipmentPanel :2026   [cap: HeroSkillTree :4002 ; _Popup/_Assigned :2272-2362]
|   |     `-- Loadout    -> HeroLoadoutPanelMvvm.cs:61; Scrim :320; chrome "Hot-Swap Skills" :325   [cap: HeroLoadout :6582]
|   |
|   |-- Raids (only if RaidCapable; dims when army not ready)   HudKit:820-834 -> RaidEntryGate.RequestOpen (Core/UI/RaidEntryGate.cs:30)
|   |     -> RaidEntryBridge.cs:89 OnRaidRequested :140-183:
|   |          FeatureFlags.Raid OFF -> toast ; Maintenance refuses -> toast ;
|   |          FeatureFlags.RaidContinuousWalk -> PingNearestRaidOutpost (no screen) ;
|   |          else RaidSelectionScreen.Open() :183
|   |     RAID SELECTION  RaidSelectionScreen.cs:37; chrome "RAIDS" :249 (Close); card tap :539 -> RaidDeployScreen.Open(def)   [cap: RaidSelection :6017 ; RaidsFaceStates :6301]
|   |     `-- RAID DEPLOY  RaidDeployScreen.cs:51; OpenInternal :103; Scrim :115; chrome "RAID: <name>" :126 (Close); "BEGIN ASSAULT" :718; Close :864 <- RaidSelection stays under it (sorting 31050 over 31000, :114)   [cap: RaidDeploy :6109]
|   |
|   |-- Journey (enum Quests)   HudKit:857-860 -> OnQuestsAction :3510-3514 -> PanelRouter.Open(JourneyDeck)
|   |     JOURNEY DECK  PlayerDeckWorkspace.cs:68 OpenJourney; cards :588-623   [cap: JourneyWorkspace :6435-6447]
|   |     |-- Quests  -> RumorBoard   RumorBoardPanelBootstrap.cs:27 -> RumorBoardPanel.cs:80; modal :258; letter "Back" :976   [cap: RumorBoard(+page2, daily) :3678-3739]
|   |     `-- Raids   -> RaidEntryGate.RequestOpen (Available = PostureSignals.RaidCapable :621-623; locked art :617) -> same RAID SELECTION chain as the bar face
|   |     (no Dungeons card, no Realm Map card, no Season card on this deck: cards are exactly the two above, :588-623)
|   |
|   `-- Manage (enum Upgrade, re-pointed)   HudKit:874-878 -> OnManageAction :3535-3542
|         -> ObsidianQueueGate.RequestToggle (ManageScreenPanel.cs:264 subscribes) else PanelRouter.Open(Manage)
|         MANAGE  ManageScreenPanel.cs:258-259 register; Open :286 -> launcher cards; Open("Defense") :315 lands on Defense tab   [cap: ManageWorkspace :6786-6802]
|         |-- close: Scrim :381 -> Close :327 ; chrome "MANAGE" :383
|         |-- LAUNCHER CARDS :612-638 (ActivateLauncherCard :762): Defense / Buildings / Troops / Research
|         |     Troops locked unless BarracksUnlock.IsUnlocked :629,:765 ; lock copy :795-797
|         |-- BACK :558-566 -> ShowLauncher :814 (operational -> cards)
|         |-- QUEUE (title-row) :1102-1104 -> ToggleQueueDrawer :1050 -> queue drawer :1002 (replaces the list band :1054)   [cap: ManageQueueDefense/Troops/Research :6948-6995 ; NO Buildings-tab drawer capture]
|         |     rows: Finish Now -> VM.FinishNow :1377 ; Ad -> VM.WatchAd :1406 (FeatureFlags.RewardedAdSkip, rows absent when OFF :586-590) ;
|         |           Cancel -> VM.Cancel :1449 (100% refund) ; Move up -> VM.BumpUp :1473
|         |-- Buy Builder :969 (BuyBuilderButtonCopy) -> VM.BuySlot :1487 -> StoreFocusRequest(PermanentBuilderSku) + PanelRouter.Open(RealmStore)
|         |-- broke-case -> VM.OpenCrystalStore :1521 -> RealmStore
|         |-- TAB Defense   (TabLabels :344)   [cap: ManageDefense :6858-6874 ; ManageDefense (operational) :7016-7077]
|         |     |-- upgradable tower row -> VM.OpenUpgradePanel :1531 -> BuildingUpgrade(id)  /  placed upgrade CTA -> VM.UpgradePlaced :1537
|         |     |-- "Need another tower?" / Build defense :1445 -> OpenDefenseBuilder :1998 (Close + EnterBuildMode(Defense))
|         |     `-- Repair :1461 -> VM.RepairAll :1505 (instant, WallRepairController)
|         |-- TAB Buildings   [cap: ManageBuildings :7016-7077]
|         |     |-- building row -> VM.UpgradeBuilding :1551 / OpenUpgradePanel -> BuildingUpgrade
|         |     `-- "Need another town structure?" / Open build :1447 -> OpenTownBuilder :2005
|         |-- TAB Troops (BarracksUnlock)   [cap: ManageTroops :7016-7077]
|         |     |-- Train <name> :1747 -> VM.TrainTroop :1611 -> BarracksService.EnqueueTraining (no panel; queue row appears)
|         |     |-- Upgrade <troop> :1770 -> Research-line job
|         |     `-- "Saved army compositions" / Open armies :1521 -> VM.AddMusterRow :1178 -> OpenMuster :1193
|         |           -> TroopDialogueCommands.ShowMusterUI :65 -> ArmyMusterPanel.Show :68
|         |              ARMIES  ArmyMusterPanel.cs:53 "Armies - Loadouts" :267; Scrim :265; Close band :289   [NO-CAPTURE]
|         `-- TAB Research (VisibleTabs gated :418)   [cap: ManageResearch :7016-7077]
|               `-- Research row :1336-1337 -> VM.Research :1571 -> BuildingPerkService.TryResearch (no panel)
|
|-- NON-BAR HUD DOORS
|   |-- Night Market card   HudKit:942 -> BuildNightMarketCard :1021; OpenNightMarket :1155-1159 -> PanelRouter.Open(RealmStore)
|   |     NIGHT MARKET  PackStore (Wallet/PackStore.cs); registered PackStoreBootstrap.cs:48   [cap: NightMarket :3811 ; RealmGoldStore :2385 ; RealmStorePurchase_* :2447]
|   |     |-- CLOSE :1123
|   |     |-- bands: PACKS (Basket) / MOVING (Patronage) :1624-1625 ; CLOSE THE GAP :1674-1679 ; FREE band :1612-1618
|   |     |-- FREE: redeem entry -> OpenRedeemPanel :1183 -> RedeemCodePanel (Wallet/RedeemCodePanel.cs:167)   [NO-CAPTURE]
|   |     |-- FREE: "MONTHLY LEDGER" :1617 (+ utility row :1639) -> PanelRouter.Open(MonthlyLedger)
|   |     |     MONTHLY LEDGER  Wallet/UI/MonthlyLedgerPanel.cs; Scrim :167; close button :213   [cap: MonthlyLedger :6585]
|   |     `-- BuildFreeDoor(PanelId) :1799 -- DEFINED, ZERO CALLERS (see dead ends)
|   |-- LEFT GEAR SLIDE DOCK (WO-439)  HudKit:3663-3672   [cap: AdaptiveHudGearOpen :2835]
|   |     |-- Chat (only if ClanFeatureGate.PlayerFacingEnabled :3662) -> ClanChatPanel.Toggle :3767-3772 (HUD/ClanChatPanel.cs:34)   [NO-CAPTURE]
|   |     |-- Leaderboard -> LeaderboardPanel.Toggle :3775-3780 (HUD/LeaderboardPanel.cs:33)   [NO-CAPTURE]
|   |     |-- Music -> MusicSelectionPanel via Type.GetType reflection :3782-3788 (Audio/MusicSelectionPanel.cs:37)   [NO-CAPTURE]
|   |     |-- "Settings" -> OpenSettings :3740-3746 -> HelpMenu.Instance.ToggleOverlay() (NOT SettingsController) else GameGuide
|   |     |     HELP  HUD/HelpMenu.cs:74; modal "Help" :213; rows from HelpMenuVM.cs:211-220: Report a Bug / Controls (toast :549) / Credits / Dev Tools   [cap: HelpMenu :3151]
|   |     |-- "Night Market" -> OpenRealmStore :3755-3766 -> PanelRouter.Open(RealmDeck)  (NOT RealmStore - see dead ends)
|   |     |     REALM DECK  PlayerDeckWorkspace.cs:66 OpenRealm; cards :626-631   [cap: RealmWorkspace :6435-6447]
|   |     |     |-- Realm Store    -> RealmStore (Night Market above)
|   |     |     |-- Defense Report -> DefenseReportPanel.cs:74 register; chrome "Attacks On Your Town" :123 (Close)   [cap: DefenseReport :6583]
|   |     |     |-- Monthly Ledger -> MonthlyLedger
|   |     |     `-- Game Guide     -> GameGuidePanel.cs:49 register; chrome :100 (Close)   [cap: GameGuide :6584]
|   |     `-- Pause -> PauseGate.RequestBack :3672
|   |           PAUSE  Settings/PauseController.cs; rows :219-231 Resume / Settings / Quit to Title   [cap: PauseMenu :2948]
|   |           `-- Settings :226 -> SettingsController.cs (modal "Settings" :161, Close :162)   [cap: Settings :2869]
|   |                 rows :249-359: Connect/Disconnect Wallet, Game Guide (:685 -> GameGuide), Reset Defaults,
|   |                 Defence Reports (:695 -> DefenseReport), Privacy Policy, Terms of Service, Ad Privacy Choices,
|   |                 Do Not Sell, Play Offline (:339), Dev Panel (:356-359, only if PanelId.DevPanel registered -> :768)
|   |                 `-- DEV PANEL  DevTools/DevPanelController.cs:74; register :232; "Open Realm Map" :869   [NO-CAPTURE; DevTools compiled out of release per PanelRouter.cs:98-110]
|   |-- Builders chip -> OnBuildersChipTapped :1480-1490 = inline peek rail toggle, NOT a screen   [cap: QueueCardRail :4389]
|   |-- Harvest / Collectors chip -> OnCollectorsChipTapped :1525-1538 -> CollectorStatusGate.RequestCollectAll (no screen)   [cap: HarvestOverflow :1851 (the overflow modal)]
|   |-- Echoes chip  Village/Harvest/EchoUnlockFeedback.cs:387-390 -> EchoRoster.Open() (EchoRosterView.cs:34)
|   |     ECHO ROSTER  EchoRosterView.cs:49; modal :189 (onClose Close)   [cap: EchoRoster :3082/:4272 ; EchoPetButton :3116]
|   |     `-- card -> EchoCardView (EchoCardView.cs:109) resource picker   [cap: EchoCard :4308]
|   |-- Heart of Elarion plate  BuildHeartStatus :1729-1826 -- NO tap handler found in that range (status only)
|   |-- Compass  :927 HudCompassWidget.Create (Kit/HudCompassWidget.cs:165) -- file contains no Button token: NO door
|   |-- Start Wave / Start Now  :1360-1363 -> _owner.StartWaveRequested (label :3052) -- action, no screen
|   |-- FLAG chip  Core/Dev/FlagCaptureButton.cs:63 "FLAG" -> F8 capture, no screen
|   `-- Quest tracker  HUD/QuestTrackerHud.cs:191 -> PanelRouter.Open(RumorBoard)
|
`-- WORLD DOORS (walk-up, not HUD)
    |-- BuildingInteractable.cs:387 upgradable building -> BuildingUpgrade(hookId)
    |     BUILDING UPGRADE  Buildings/Progression/BuildingUpgradePanelMvvm.cs:223-224 register   [cap: BuildingUpgrade :6577]
    |-- BuildingInteractable.cs:405 -> DialogueService.PlayStructure (Yarn) ; :420-421 TryPanelFor :485-515:
    |     ArcaneTower -> HeroSkillTree ; Workshop -> Crafting (VillageCraftingPanel.cs:61; modal :118)   [cap: Workshop :6578]
    |     ApothecaryWorkbench -> ConsumableCrafting (Items/CraftingPanelMvvm.cs:69; chrome "Alchemy" :264)   [cap: Alchemy :6580]
    |     JewelersBench -> JewelerCrafting (Items/JewelerPanelMvvm.cs:73; chrome :313)   [cap: Jeweler :6581]
    |     CrystalMine/Farm/Lumbermill/Forge/Armorer -> BuildingUpgrade
    |-- RealmStoreVendor.cs:103 -> RealmStore
    |-- FoundersMonument.cs:107 -> Benefactors (HUD/BenefactorsWallPanel.cs:71; modal :134) -- the ONE door (FoundersMonumentInjector.cs:226)   [cap: Benefactors :6576]
    |-- CastleVendorNpcInjector.cs:1461,1480 -> BuildingUpgrade
    |-- JewelerDiscoveryFtue.cs:84 -> JewelerCrafting
    |-- PartyShop (Hero/PartyShopPanelMvvm.cs:149-153 register; chrome "Party Shop" :356) via OpenShop verb / DialogueService:113   [cap: PartyShop :6579 ; PartyShopPopulated :2564]
    |-- TroopTrainingPanel (Hero/TroopTrainingPanel.cs:41 "Barracks - Train" :101) <- TroopDialogueCommands ShowTrainingUI :54-58 (verb NOT in dialogues.json)   [NO-CAPTURE in ui-capture; AutoPilot only, AutoPilotDriver.cs:6354]
    |-- BarracksPanel (Hero/BarracksPanel.cs "Barracks - Upgrade" :103) <- BarracksPanelVM.cs:185-187   [NO-CAPTURE]
    |-- ShopPanel (Hero/ShopPanel.cs "Vendor Wares" :334) <- only AutoPilotDriver.cs:4437-4443 found   [NO-CAPTURE]
    `-- LEGACY: BuildMenu (Buildings/UI/BuildMenu.cs:89) -> TowerManagerPanel.Instance.Show :312 ; wired only by BuildMenuHudBridge.cs:15-19   [cap: BuildMenuUpgradeTower :3548 ; TowerManagerPanel :3440] -- no HudKit door found
```

---

## (b) Node table

Registry = `PanelId` (`Assets/_Modules/Core/UI/PanelRouter.cs:37-145`). `Register` REPLACES a prior opener (`:185 _openers[id] = open`). Opening one registered panel closes the others through PanelManager (`:148-150`).

| id | builder class : file | opened from (edges) | closes to | capture case (UICaptureLaunch.cs) | PNG stem |
|---|---|---|---|---|---|
| HUD root | HudKitController : HUD/Kit/HudKitController.cs | scene boot | - | CaptureAdaptiveHudOnce :2751 | AdaptiveHudPeaceful / GearOpen / Combat |
| Bar: Raids face states | same | model RaidCapable/ArmyReady | - | CaptureRaidsFaceStatesOnce :6301 | RaidsFaceStates |
| BUILD MODE | BuildModeController : Village/BuildMode/BuildModeController.cs | bar Build (HudKit:776 -> BuildButtonBridge:15) ; Manage "Build defense"/"Open build" (ManageScreenPanel:1998,2005) | Exit :586 -> HUD | CapturePaletteCollapsed :4936 | BuildPaletteDock_open/_collapsed |
| Build Collections | BuildCollectionBrowser : Village/BuildMode/BuildCollectionBrowser.cs:25 | BuildPaletteUI.Show :334 | Back :78 / Close :94 ; DefenseUpgradeCard :172-176 -> Manage(Defense) | CaptureBuildCollections :7202 | BuildCollections, BuildCollections_Page2 |
| Ghost + chips | GhostPreview / BuildHudController | entry tap -> Arm | Cancel/Done -> Exit (:3897-3901) | CaptureBuildGhostChips :4612 | BuildGhostChips_valid/_blocked/_edgeclamp/_padon |
| Orient editor | BuildPreviewModal : Village/BuildMode/BuildPreviewModal.cs | OpenOrientEditorForArmed :3937-3952 | modal close | CaptureBuildPreviewOnce :2147 | BuildPreview |
| Structure Info Preview | BuildStructureInfoPanel | DISABLED (BuildModeController:3864-3872) | - | none | NONE |
| Talk / Dialogue | DialogueView : HUD/DialogueView.cs:22 | bar Talk (HudKit:781-788 -> HudCommands.Talk :114 -> TalkHudBridge:91) ; BuildingInteractable:405 | X -> _vm.Close (DialogueView:265) | RunCompactDialogueCaptureHeadless :1601 ; CaptureDialogueOptionsOnce :1111 | DialogueCompact_Aldwin, DialogueOptions_2opt/_4opt |
| HeroDeck (23) | PlayerDeckWorkspace : HUD/PlayerDeckWorkspace.cs:67 | bar Hero (HudKit:800) ; peaceful dock :1884 | Close :534 before any card opens (no back-to-deck) | CapturePlayerDeckOnce :6737 | HeroWorkspace |
| JourneyDeck (24) | PlayerDeckWorkspace:68 | bar Journey (HudKit:3512) ; peaceful dock :1888 | same | :6737 | JourneyWorkspace |
| RealmDeck (22) | PlayerDeckWorkspace:66 | gear dock "Night Market" (HudKit:3758) | same | :6737 | RealmWorkspace |
| Inventory (14) | HeroInventoryController:156 -> InventoryUIBuilder | Hero deck "Bag" (:579) | Scrim :114 Close | CaptureBagOnce :6702 ; CaptureBagUseOnce :6482 | Bag, BagUse_Selected/_Used |
| EquipmentPanel (11) | EquipmentPanel : Village/Hero/EquipmentPanel.cs:159 | Hero deck (:580) ; Bag Gear action (InventoryUIBuilder:691-693) ; skill tree :2026 | Scrim :175 ; BACK :954 | CaptureHeroEquipmentOnce :2669 | HeroEquipment(+_Compare/_Equipped) |
| HeroSkillTree (7) | HeroSkillTreePanelMvvm : Village/Talents/HeroSkillTreePanelMvvm.cs:467 | Hero deck (:581) ; Bag Skills (InventoryUIBuilder:673) ; EquipmentPanel:974 ; ArcaneTower building (BuildingInteractable:490) ; verb OpenTalents (unused) | chrome X :1940 -> _vm.Close | CaptureHeroSkillTree :4002 ; states :2272 | HeroSkillTree(+_Popup/_Assigned) |
| HeroLoadout (8) | HeroLoadoutPanelMvvm : Village/Talents/HeroLoadoutPanelMvvm.cs:61 | Hero deck "Loadout" (:582) | Scrim :320 | RunRegisteredSecondaryCaptureHeadless :6582 | HeroLoadout |
| RumorBoard (6) | RumorBoardPanel : Village/Hero/RumorBoardPanel.cs:80 (reg. RumorBoardPanelBootstrap.cs:27) | Journey deck (:594) ; QuestTrackerHud:191 ; verb OpenRumorBoard | modal :258 ; letter Back :976 | CaptureRumorBoardOnce :3678 | RumorBoard(+_page2, _daily) |
| Raid Selection | RaidSelectionScreen : Village/Hero/RaidSelectionScreen.cs:37 (static Open) | RaidEntryBridge:183 <- bar Raids (HudKit:830) / Journey card (:623) | chrome Close :249 -> Close :608 -> HUD | CaptureRaidSelectionOnce :6017 | RaidSelection |
| Raid Deploy | RaidDeployScreen : Village/Hero/RaidDeployScreen.cs:51 | RaidSelectionScreen:539 | Scrim :115 / chrome :126 -> Close :864 (selection screen remains) | CaptureRaidDeployOnce :6109 | RaidDeploy |
| RealmMap (15) | RealmMapPanel : Village/Hero/RealmMapPanel.cs:203 | Bag Map section (InventoryUIBuilder:663, flag MapTab OFF) ; DevPanel:871 | modal :234 Close | CaptureRealmMapOnce :3934 | RealmMap |
| Manage (16) | ManageScreenPanel : Village/UI/Manage/ManageScreenPanel.cs:258-259 | bar Manage (HudKit:3535-3542) ; BuildCollectionBrowser:176 ("Defense") | Scrim :381 -> Close :327 | CaptureManageWorkspace :6786 | ManageWorkspace |
| Manage / Defense tab | same, ShowOperational | launcher card / Open("Defense") :315 | BACK :558 -> launcher | CaptureManageDefense :6858 ; CaptureManageOperational(Defense) :7016 | ManageDefense |
| Manage / Buildings tab | same | launcher card | BACK | CaptureManageOperational(Buildings) | ManageBuildings |
| Manage / Troops tab | same (BarracksUnlock gate :629) | launcher card | BACK | CaptureManageOperational(Troops) | ManageTroops |
| Manage / Research tab | same (VisibleTabs :418) | launcher card | BACK | CaptureManageOperational(Research) | ManageResearch |
| Manage queue drawer | same, ToggleQueueDrawer :1050 | QUEUE :1102 ; summary chip :955 | QUEUE again | CaptureManageLiveQueue :6948 (Defense/Troops/Research only) | ManageQueueDefense/Troops/Research ; Buildings NONE |
| Armies / Muster | ArmyMusterPanel : Village/Troops/ArmyMusterPanel.cs:53 | Manage Troops "Open armies" (:1521 -> VM:1193 -> TroopDialogueCommands:65-68) | Scrim :265 / Close band :289 | none | NONE |
| BuildingUpgrade (2) | BuildingUpgradePanelMvvm : Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:223-224 | Manage rows (VM:1531) ; BuildingInteractable:387,:420 ; BuildModeController:2509 ; CastleVendorNpcInjector:1461,1480 ; verb OpenUpgrade (unused) | panel close (not read) | :6577 | BuildingUpgrade |
| RealmStore (13) | PackStore (reg. Wallet/PackStoreBootstrap.cs:48) ; ALSO GooglePlayStorefront.cs:17 under GOOGLE_PLAY | Night Market card (HudKit:1159) ; Realm deck (:628) ; Manage BuySlot/OpenCrystalStore (VM:1490,1523) ; RealmStoreVendor:103 ; verb OpenRealmStore | CLOSE :1123 | CaptureNightMarketStoreOnce :3811 ; RealmGoldStore :2385 ; Purchase :2447 | NightMarket, RealmGoldStore, RealmStorePurchase_* |
| Redeem code | RedeemCodePanel : Wallet/RedeemCodePanel.cs:167 | PackStore free band :1616 -> :1183 | overlay close | none | NONE |
| MonthlyLedger (20) | MonthlyLedgerPanel : Wallet/UI/MonthlyLedgerPanel.cs | PackStore :1617,:1639 ; Realm deck (:630) | Scrim :167 / close :213 | :6585 (OnEnable, private) | MonthlyLedger |
| BattlePass (19) | SeasonTrackPanel : Wallet/UI/SeasonTrackPanel.cs:61 (reg. BattleMonthlyPanelsBootstrap.cs:64) | NONE - no `PanelRouter.Open(PanelId.BattlePass)` in Assets/_Modules; only door-builder is PackStore.BuildFreeDoor :1799 with zero callers | Scrim :158 / close :199 | none | NONE |
| DefenseReport (18) | DefenseReportPanel : Village/UI/Defense/DefenseReportPanel.cs:74 | Realm deck (:629) ; Settings :695 | chrome :123 Close | :6583 | DefenseReport |
| GameGuide (12) | GameGuidePanel : Village/UI/Guide/GameGuidePanel.cs:49 | Realm deck (:631) ; Settings :685 ; gear dock fallback (HudKit:3744) | chrome :100 Close | :6584 | GameGuide |
| Benefactors (21) | BenefactorsWallPanel : HUD/BenefactorsWallPanel.cs:71 | FoundersMonument.cs:107 only | modal :134 | :6576 | Benefactors |
| Help | HelpMenu : HUD/HelpMenu.cs:74 | gear dock "Settings" (HudKit:3742) | modal :213 | CaptureHelpMenuOnce :3151 | HelpMenu |
| Pause | PauseController : Settings/PauseController.cs | gear dock Pause (HudKit:3672 -> PauseGate) | Resume :219 | CapturePauseMenuOnce :2948 | PauseMenu |
| Settings | SettingsController : Settings/SettingsController.cs:161 | Pause "Settings" :226 | Close :162 | CaptureSettingsOnce :2869 | Settings |
| DevPanel (17) | DevPanelController : DevTools/DevPanelController.cs:74 (reg. :232) | Settings :768 (only if registered :356) | Close :222 | none | NONE |
| Clan Chat | ClanChatPanel : HUD/ClanChatPanel.cs:34 | gear dock Chat (HudKit:3767, gated :3662) | Toggle | none | NONE |
| Leaderboard | LeaderboardPanel : HUD/LeaderboardPanel.cs:33 | gear dock (HudKit:3775) | Toggle | none | NONE |
| Music | MusicSelectionPanel : Audio/MusicSelectionPanel.cs:37 | gear dock (HudKit:3782, reflection) | Toggle | none | NONE |
| Echo Roster | EchoRosterView : Village/Harvest/EchoRosterView.cs:49 | Echoes chip (EchoUnlockFeedback.cs:387-390) | modal :189 Close | CaptureEchoRosterPanelOnce :4272 / :3082 | EchoRoster, EchoPetButton |
| Echo Card | EchoCardView : Village/Harvest/EchoCardView.cs:109 | roster card | - | CaptureEchoCardOnce :4308 | EchoCard |
| Crafting (1) Workshop | VillageCraftingPanel : Village/Crafting/VillageCraftingPanel.cs:61 | BuildingType.Workshop (BuildingInteractable:494) ; verb OpenCraft (unused) | modal :118 Close | :6578 | Workshop |
| ConsumableCrafting (9) | CraftingPanelMvvm : Village/Items/CraftingPanelMvvm.cs:69 | ApothecaryWorkbench (BuildingInteractable:505) ; verb OpenAlchemy | chrome :264 | :6580 | Alchemy |
| JewelerCrafting (10) | JewelerPanelMvvm : Village/Items/JewelerPanelMvvm.cs:73 | JewelersBench (:511) ; verb OpenJeweler ; JewelerDiscoveryFtue:84 | chrome :313 | :6581 | Jeweler |
| PartyShop (5) | PartyShopPanelMvvm : Village/Hero/PartyShopPanelMvvm.cs:149-153 | verb OpenShop (DialogueCommandSink:91-92) ; DialogueService:113 | chrome :356 -> _vm.Close | :6579 ; Populated :2564 | PartyShop, PartyShopPopulated |
| CosmeticShop (3) | CosmeticShopPanel : HUD/CosmeticShopPanel.cs:76 | verb OpenCosmetics (DialogueCommandSink:107) - verb absent from dialogues.json ; no BuildingInteractable mapping | modal :253 CloseOverlay | :6576 | CosmeticShop |
| HeroTalents (0) | none | none (retired, PanelRouter.cs:39-43) | - | none | NONE |
| TroopTrainingPanel | Village/Hero/TroopTrainingPanel.cs:41 | TroopDialogueCommands:54-58 (ShowTrainingUI verb absent from dialogues.json) | chrome :101 Close | none (AutoPilot :6354 only) | NONE |
| BarracksPanel | Village/Hero/BarracksPanel.cs | BarracksPanelVM.cs:185-187 | chrome :103 Close | none | NONE |
| ShopPanel "Vendor Wares" | Village/Hero/ShopPanel.cs:334 | only AutoPilotDriver.cs:4437-4443 found | chrome :334 -> _vm.Close | none | NONE |
| Legacy BuildMenu / TowerManagerPanel | Buildings/UI/BuildMenu.cs:89 ; TowerManagerPanel.cs:51 | BuildMenuHudBridge.cs:15-19 ; BuildMenu:312 -> TowerManager | - | :3548 ; :3440 | BuildMenuUpgradeTower, TowerManagerPanel |
| Obsidian queue HUD (legacy) | ObsidianQueueHud.OpenWorkQueue : Village/BuildMode/ObsidianQueueHud.cs:156 | zero callers (HudKit:1476-1478) | - | - | (QueueCardRail :4389 is the chip rail, not this) |
| HUD overlays (not doors) | DailyQuestHud (HUD/DailyQuestHudBootstrap.cs:58) ; EndStateView ; HarvestOverflow | system-driven | - | :3238 ; :5108 ; :1851 | DailyQuestHud, EndStateWaveClear_*, HarvestOverflow |

UICaptureMode (`Builds/UICaps/`, `UICaptureMode.cs:68`) routes at `:274-286`: RealmStore, Inventory, HeroSkillTree, Crafting, BuildingUpgrade, CosmeticShop, PartyShop, RumorBoard, HeroLoadout, ConsumableCrafting, JewelerCrafting, EquipmentPanel, GameGuide - a parallel, older set; not the gate.

---

## (c) DEAD ENDS

1. **PanelId.BattlePass (19) is registered and never opened.** Registered `BattleMonthlyPanelsBootstrap.cs:64`; a repo-wide grep of `Assets/_Modules` finds no `PanelRouter.Open(PanelId.BattlePass` (only the capture fixture list `UICaptureLaunch.cs:6745`). The only PanelId-parameterised door in the store, `PackStore.BuildFreeDoor` (`:1799`), has exactly one occurrence in the file - its definition. `SeasonTrackPanel` (`Wallet/UI/SeasonTrackPanel.cs:61`) has no player door and no capture.
2. **PanelId.RealmStore is registered TWICE.** `PackStoreBootstrap.cs:48` and `GooglePlay/GooglePlayStorefront.cs:17`, both `RuntimeInitializeLoadType.BeforeSceneLoad`; `PanelRouter.Register` replaces (`PanelRouter.cs:185`). The GooglePlay assembly compiles only under `GOOGLE_PLAY` on Android/Editor (`DeNelle.GooglePlay.asmdef` defineConstraints/includePlatforms). In such a build, which store the Night Market card opens depends on static-init order - NOT proven either way here; it is a collision to resolve, not a bug I have observed.
3. **PanelId.RealmMap (15) has no release door.** Openers: Bag "Map" section (`InventoryUIBuilder.cs:661-663`) which runs only when `FeatureFlags.MapTab` is ON (default OFF, `FeatureFlags.cs:842`), and `DevPanelController.cs:869-871` (DevTools is compiled out of release, `PanelRouter.cs:98-110`). `HudKitController.cs:845-851` still says the route "is now reached from the Bag tab row" - stale relative to the flag default.
4. **PanelId.CosmeticShop (3) is unreachable by a player.** Registered `CosmeticShopPanel.cs:76`; the only opener is the dialogue verb `OpenCosmetics` (`DialogueCommandSink.cs:107`), which does not occur in `Assets/Resources/Data/Canonical/dialogue/dialogues.json` (grep, this session); `BuildingInteractable.TryPanelFor` (`:485-515`) has no Cosmetic case.
5. **Dialogue verbs with no data behind them:** `OpenUpgrade`, `OpenCraft`, `OpenTalents`, `OpenCosmetics`, `ShowMusterUI`, `ShowTrainingUI` are handled in code (`DialogueCommandSink.cs:83,95,106,107`; `TroopDialogueCommands.cs:45-68`) but absent from both `dialogues.json` copies. `TroopTrainingPanel` therefore has no in-game door found this session (only `AutoPilotDriver.cs:6354`).
6. **PanelId.HeroTalents (0)** - never registered, never opened (by design, `PanelRouter.cs:39-43`).
7. **Two rows both labelled "Night Market" go to two different screens.** The HUD card opens `RealmStore` (`HudKitController.cs:1159`); the gear-dock row opens `RealmDeck` (`:3758`), a card launcher that itself contains "Realm Store".
8. **The gear-dock row labelled "Settings" opens the HELP menu, not Settings.** `OpenSettings` (`HudKitController.cs:3740-3746`) toggles `HelpMenu` (rows: Report a Bug / Controls / Credits / Dev Tools, `HelpMenuVM.cs:211-220`). The real `SettingsController` is reachable only via Pause -> Settings (`PauseController.cs:226`).
9. **Deck cards have no "back to deck".** `PlayerDeckWorkspace.OpenCard` (`:530-536`) closes the deck BEFORE opening the card's target, so closing Bag / Equipment / Skills / Loadout / Quests / Raids / Realm Store / Defense Report / Monthly Ledger / Game Guide lands on the HUD, not the deck the player came from.
10. **Manage -> store hops leave Manage.** `BuySlot` (`ManageScreenVM.cs:1487-1499`) and `OpenCrystalStore` (`:1521-1525`) open `RealmStore`; PanelManager is exclusive (`PanelRouter.cs:148-150`), so Manage closes and the store's CLOSE (`PackStore.cs:1123`) returns to HUD, not to the Manage tab that sent the player.
11. **`ObsidianQueueHud.OpenWorkQueue`** (`ObsidianQueueHud.cs:156`) has zero callers (`HudKitController.cs:1476-1478`) - legacy screen still in tree.
12. **Legacy `BuildMenu` / `TowerManagerPanel`** (`Buildings/UI/BuildMenu.cs:89`, `TowerManagerPanel.cs:51`) are captured every run (`UICaptureLaunch.cs:3440,:3548`) but the only wiring found is `BuildMenuHudBridge.cs:15-19`; no HudKit door. Reachability NOT proven.
13. **Journey deck is two cards.** `PlayerDeckWorkspace.cs:588-623` = Quests + Raids only; there is no dungeon, realm-map or season card, so the "Journey" word promises more destinations than the deck lists.
14. **BuildStructureInfoPanel** is dead code in the flow (`BuildModeController.cs:3864-3872`, disabled 2026-06-19, no subscriber).
15. No PanelId is registered twice by two DIFFERENT classes other than RealmStore (item 2). Multi-registrations of Manage (`:258-259`), PartyShop (`:149-153`), BuildingUpgrade (`:223-224`), MonthlyLedger (`:70,:75`) are arity overloads of one class, not collisions.

---

## (d) CAPTURE GAP - nodes with no headless case, in player order

1. Build HUD intent bar (Rotate L / Rotate R / PLACE / Cancel / Done) as its own frame - `BuildModeController.cs:3882-3901` (ghost-chip shots exist but no case names the bar).
2. Talk: the NPC-verb result screens are covered, but no case exercises `TalkAvailable` OFF vs ON on the bar (only RaidsFaceStates covers a conditional face, `:6301`).
3. Hero deck -> Inventory rail sections other than the default (Weapons/OffHand/Armor/Trinkets/Potions) - `InventoryUIBuilder.cs:375-383`; only `Bag` and `BagUse_*` exist.
4. Raids: `RaidContinuousWalk` ping path (`RaidEntryBridge.cs:160-164`) and the `FeatureFlags.Raid` OFF toast (`:142-149`) - no frames.
5. Journey deck with the Raids card LOCKED (`PlayerDeckWorkspace.cs:617-623`) - JourneyWorkspace exists but its lock state is not a named case.
6. Manage launcher with Troops LOCKED (`ManageScreenPanel.cs:629,:683-687`) - ManageWorkspace exists; lock state not a named case.
7. Manage / Buildings tab QUEUE drawer - `RunManageLiveQueueCaptureHeadless` covers Defense/Troops/Research only (`UICaptureLaunch.cs:6935-6937`).
8. Manage rows: Finish Now / Watch Ad / Cancel / Move up outcomes and the Notice line (`ManageScreenVM.cs:1377,1406,1449,1473`) - no post-action frame.
9. **Armies / Muster panel** (`ArmyMusterPanel.cs:53`) - reached from Manage Troops "Open armies" (`ManageScreenPanel.cs:1521`). NONE.
10. Manage "Buy Builder" -> store focused on `PermanentBuilderSku` (`ManageScreenVM.cs:1489`) - the store frame with a focused SKU is not captured.
11. **Redeem code overlay** (`RedeemCodePanel.cs:167`) from the Night Market free band. NONE.
12. **SeasonTrackPanel / Battle Pass** (`SeasonTrackPanel.cs:61`). NONE (and no door, dead end 1).
13. Gear dock rows: **Clan Chat** (`ClanChatPanel.cs:34`), **Leaderboard** (`LeaderboardPanel.cs:33`), **Music / Jukebox** (`MusicSelectionPanel.cs:37`). NONE.
14. Pause -> Settings sub-states (quality/difficulty rows `SettingsController.cs:441-464`, Play Offline `:339`, Do-Not-Sell `:323`) - one `Settings` frame only.
15. **Dev Panel** (`DevPanelController.cs:74`). NONE (dev-only; acceptable to leave).
16. World doors: **TroopTrainingPanel** (`TroopTrainingPanel.cs:41`), **BarracksPanel** (`BarracksPanel.cs:103`), **ShopPanel "Vendor Wares"** (`ShopPanel.cs:334`). NONE in ui-capture (TroopTraining is AutoPilot-only, `AutoPilotDriver.cs:6354`).
17. **GooglePlayStorefront** (`GooglePlayStorefront.cs:17`) - the alternate RealmStore under `GOOGLE_PLAY`. NONE (only `GooglePlayLogin` exists, `:1948`).
18. Portrait coverage: only `HeroSelect_1080x1920`, `RumorBoard_1080x2340`, `RumorBoard_1200x2670` exist in `Builds/ui-capture/`; every Manage / deck / Build frame is landscape-only.
