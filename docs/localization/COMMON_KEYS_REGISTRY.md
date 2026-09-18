# `common.*` key registry

**Produced:** 2026-09-17, as part of WO-1857 phase 1 shared-key pre-pass (the "mint these
FIRST" step named in `docs/localization/SWEEP_CLASSIFICATION_2026-09-17.md` §4). This is the
taxonomy document every later shard-tagging lane reads BEFORE minting its own key — if the
string you're about to wire is a bare, screen-agnostic word/phrase, check this table first.
Reuse a row here; do not mint a synonym under a per-screen namespace.

**Scope note:** this pass only minted/confirmed the 33 candidates listed in the sweep doc's
§4 table. It did **not** re-audit the rest of the manifest for additional shared-string
candidates — that is still open work for a future pass.

## How to use this table

- Column **status**: `PRE-EXISTING` = already in the codebase before this pass; `MINTED` =
  added this session; `REJECTED` = considered and deliberately NOT minted (reason given).
- Column **call sites** lists every file:line this pass actually opened and read to confirm
  the meaning is consistent. A lane wiring a NEW call site to one of these keys should still
  open at least one existing call site first and confirm the job matches (verb vs noun,
  button vs heading) before pointing at it — see the "Meaning is a claim" warning below.

## ⚠ Meaning is a claim, not a spelling match

Per `docs/localization/key-naming.md`, the same English word gets a SEPARATE key when it does
a different job in different places — a verb on a button and a noun in a heading translate
differently in German/French/Japanese/etc. Every row below was confirmed same-job across all
its listed call sites by opening the source at that file:line. Where a candidate's job was
NOT consistent, it is recorded under Rejected/Partial below instead of silently collapsed.

---

## Table — confirmed shared keys (reuse these)

| key | value (en) | meaning / job | call sites confirmed this session |
|---|---|---|---|
| `common.close` | Close | Generic dismiss/close button label, used game-wide. | Pre-existing (WO-562 canon); `Assets/_Modules/Core/UI/LocalText.cs:336` (`KeyClose` const). Confirmed additional bare-"Close" button sites during this pass: `Assets/_Modules/HUD/AdminOverlay.cs:379`, `Assets/_Modules/Village/Buildings/NPCUpgradeStation.cs:134`, `Assets/_Modules/Village/Arena/ArenaPanel.cs:213,401`, `Assets/_Modules/Onboarding/FirstWatchWelcomeLetter.cs:109`, `Assets/_Modules/Core/UI/ShopTheme.cs:252`, `Assets/_Modules/Village/Dev/ResourceDevTool.cs:229` (dev tool — do not wire, see note) — all the same job (dismiss button). |
| `common.remnant_chat` | Remnant Chat | Screen/panel title for the Remnant (clan) chat feature. | Pre-existing in JSON (all 10 locales, both mirrors) but was MISSING from the Unity Localization tables until this pass — completed here (Shared Data id 44124330363760650, all 6 enabled-locale tables). Call sites: `Assets/_Modules/HUD/ClanChatPanel.cs:86,161`, `Assets/_Modules/HUD/ClanChatVM.cs:89`. |
| `common.leaderboard` | Leaderboard | Screen/panel title AND the nav dock-tab button label that opens it — same proper-noun feature name in both jobs. | Pre-existing in JSON (all 10 locales); MISSING from Unity tables until this pass — completed here (Shared Data id 44124330363760651). Call sites: `Assets/_Modules/HUD/LeaderboardPanel.cs:51,96`, `Assets/_Modules/HUD/LeaderboardVM.cs:94`, `Assets/_Modules/HUD/Kit/HudKitController.cs:5558` (dock tab). |
| `common.echoes_elarion` | ECHOES OF ELARION | The game's own title/branding string. "Elarion" is the village name and stays untransliterated-but-kept-as-proper-noun in every locale (matches the established `hud.heart.title` convention — see note below). Used both as splash branding and, thematically, as the title of Echo-roster screens. | `Assets/_Modules/Core/UI/LoadingOverlay.cs:219`, `Assets/_Modules/Village/Harvest/EchoRosterView.cs:190,204`, `Assets/_Modules/Village/Harvest/EchoRosterVM.cs:54`, `Assets/_Modules/Village/Harvest/EchoUnlockDialogue.cs:297`. |
| `common.skip` | Skip | Button verb: skip/dismiss the current tutorial step or cinematic beat. Same job at every site (advance past an optional/skippable moment). | `Assets/_Modules/Onboarding/OnboardingFlow.cs:327`, `Assets/_Modules/Onboarding/StoryIntroController.cs:303`, `Assets/_Modules/Core/UI/TutorialSkipUi.cs:261`, `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:417`, `Assets/_Modules/Village/Progression/SpirePlansCelebration.cs:445`, `Assets/_Modules/Village/Progression/BattlePlansReveal.cs:388`. |
| `common.crafting` | Crafting | Noun naming the crafting activity/screen — used as a fallback panel title AND as an Echo-lane assignment category name; both are the same noun concept. | `Assets/_Modules/Dungeons/UI/DungeonCraftVM.cs:43`, `Assets/_Modules/Village/Crafting/WorkshopCraftVM.cs:95`, `Assets/_Modules/Village/Crafting/VillageCraftingPanel.cs:117`, `Assets/_Modules/Village/Harvest/EchoAssignments.cs:282`. |
| `common.done` | Done | Button verb: finish/exit the current mode or drawer. Same job everywhere (dismiss-and-confirm). | `Assets/_Modules/Village/BuildMode/BuildHudController.cs:469`, `Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs:151`, `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1233`, `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:398` (dev-only overlay — see note, do not wire). |
| `common.equipped` | Equipped | Adjective/status word describing an item as currently worn — used as a disabled-button label, a stat-row value, and a badge chip. Same status word in every job. | `Assets/_Modules/HUD/CosmeticShopPanel.cs:465`, `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:344`, `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1461`, `Assets/_Modules/Village/Hero/InventoryGrid.cs:678`. |
| `common.gold` | Gold | Currency-name noun. | `Assets/_Modules/Village/BuildMode/BuildWalletRow.cs:46`, `Assets/_Modules/Village/Harvest/EchoRosterCatalog.cs:279`, `Assets/_Modules/Village/Harvest/EchoService.cs:617`, `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1552,1804,1809`, `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2144,2207`, `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:308`. (`Assets/_Modules/Village/Dev/ResourceDevTool.cs:222` is a dev tool — not counted, do not wire.) |
| `common.no_hero_equip` | No hero to equip. | Status message: player tried to equip/assign something with no active hero. Same job across Equip/Inventory/Talents flows. | `Assets/_Modules/Village/Hero/EquipVM.cs:383`, `Assets/_Modules/Village/Hero/InventoryVM.cs:390`, `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:163,190`. |
| `common.ad` | Ad | Button label for the rewarded-ad-skip chip on a build/train/research queue row. | `Assets/_Modules/Village/BuildMode/ObsidianQueueHud.cs:348`, `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:7333`. |
| `common.brom_s_rumor` | Brom's Rumor Board | Screen title for the Rumor Board feature. "Brom" is a proper noun (NPC name), kept untranslated. | `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:578`, `Assets/_Modules/Village/Hero/RumorBoardVM.cs:222`. |
| `common.crystals` | Crystals: {0} | **Format key** (positional `{0}` via `LocalText.Format`/`string.Format`, NOT a bare word) — the crystal-balance header readout. Both sites build this by string concatenation today (`"Crystals: " + amount`); the wiring lane should replace the concatenation with `LocalText.Format("common.crystals", amount)`. | `Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs:1711` (and header comment `BuildPaletteVM.cs:198`), `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:280`. |
| `common.job` | Job | Fallback/default label for a build-timer or offline-harvest job with no display name. | `Assets/_Modules/Village/Buildings/BuildTimerService.cs:2647,2660`, `Assets/_Modules/Village/Harvest/UI/WelcomeBackDoorsVM.cs:223`, `Assets/_Modules/Village/Harvest/OfflineHarvestService.cs:548`, `Assets/_Modules/Village/BuildMode/ObsidianQueueHud.cs:619`. |
| `common.jukebox` | Jukebox | Screen/panel title for the music-selection feature. | `Assets/_Modules/Audio/JukeboxVM.cs:70`, `Assets/_Modules/Audio/MusicSelectionPanel.cs:95` (registration string at `:65` is a panel-manager id, not player-facing text — not wired). |
| `common.lv` | Lv {0} | **Format key** (positional `{0}`) — the level-abbreviation prefix. Every current call site builds this by hand-concatenating `"Lv " + lvl`; this is a `frag` per the sweep doc's concatenation warning (§5), not a bare shared word. The wiring lane replaces each concatenation with `LocalText.Format("common.lv", lvl)`. | `Assets/_Modules/Village/Hero/GearProgression.cs:293`, `Assets/_Modules/Village/Hero/EquipVM.cs:311,325,333`, `Assets/_Modules/Village/Hero/PartyShopVM.cs:1769,1814`, `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1246`, `Assets/_Modules/Village/Hero/InventoryVM.cs:530,551,572`. |
| `common.next` | Next | Button verb: advance to the next onboarding beat or showcase step. | `Assets/_Modules/Onboarding/OnboardingFlow.cs:331`, `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:118`. |
| `common.no_hero` | No hero. | Status message: an action was attempted (e.g. unequip) with no active hero. **Only ONE genuine player-facing site** — see note below. | `Assets/_Modules/Village/Hero/EquipVM.cs:459`. (`Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:553` is explicitly a dev-only overlay per its own header comment — "DEV-ONLY: launched from the owner dev tools; not exposed to normal players" — do NOT wire it to this key; its text stays hardcoded.) |
| `common.no_inventory` | No inventory. | Status message: the inventory store is unavailable. | `Assets/_Modules/Village/Hero/PartyShopVM.cs:1297,1330`, `Assets/_Modules/Village/Hero/InventoryVM.cs:340,373`. |
| `common.powered_skr` | Powered with SKR | Branding phrase for the SKR grant-preview badge — used as both the Title-screen badge button label and the panel's own modal title. "SKR" is a token ticker, kept untranslated. | `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:165`, `Assets/_Modules/Onboarding/TitleController.cs:400`. |
| `common.ok` | OK | Acknowledge-only button face of a ConfirmModal — the single-face "I have read this" dismissal, NOT a yes/no confirm (`common.close` is the panel-dismiss chrome verb; this is the modal's own accept face). MINTED by the WO-1857 phase-4 merge; deduped against all four phase-4 sidecars and the existing registry before minting, and it is the only `common.*` row this merge added. | `Assets/_Modules/Settings/SettingsController.cs:1053` — the sole call site as of 2026-09-18 (grepped, not assumed). |
| `common.recipe` | Recipe | Fallback generic title when a crafting recipe has no display name. | `Assets/_Modules/Dungeons/UI/DungeonCraftVM.cs:143`, `Assets/_Modules/Dungeons/UI/CraftingPanelController.cs:185`. |
| `common.retreat` | Retreat | Button verb only — see the Partial/Rejected note below, a heading-noun use of the same word exists and must NOT reuse this key. | `Assets/_Modules/Village/Troops/RaidDeployController.cs:2793`, `Assets/_Modules/Village/World/Camps/Village2RaidController.cs:334`. |
| `common.select_item_first` | Select an item first. | Status message: an equip/use action needs an item selected first. | `Assets/_Modules/Village/Hero/EquipVM.cs:381`, `Assets/_Modules/Village/Hero/InventoryVM.cs:338,372,388`. |
| `common.stake_rewards` | Stake Rewards | Screen title for the Stake Rewards panel. | `Assets/_Modules/Core/UI/Mvvm/StakeRewardsVM.cs:65`, `Assets/_Modules/Core/UI/StakeRewardsPanel.cs:50,144`. |
| `common.towers` | Towers | Screen title for the placed-tower manager list. | `Assets/_Modules/Village/Buildings/UI/TowerManagerPanel.cs:150`, `Assets/_Modules/Village/Buildings/UI/PlacedTowerListVM.cs:144`. (`Assets/_Modules/Village/Buildings/UI/BuildMenuVM.cs:108` is a Resources folder-path constant, not UI text — not counted.) |

## Table — reuse an EXISTING non-`common.*` key (do not mint a synonym)

| literal | reuse this key | confirmed at |
|---|---|---|
| `Build` | `hud.nav.build` (value `"BUILD"`) | `Assets/_Modules/HUD/Kit/HudKitController.cs:899`, `Assets/_Modules/Village/BuildMode/BuildPaletteVM.cs:171`, `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:235` — all nav/menu entry buttons into Build Mode. Case difference (Build vs BUILD) is a display-transform, not a meaning difference. |
| `Upgrade` | `ownedTown.upgrade` (value `"Upgrade"`) | `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:178`, `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1302`, `Assets/_Modules/Village/Buildings/NPCUpgradeStation.cs:180` — all "execute the upgrade" buttons. |
| `Cancel` | `armyScreen.cancel` (value `"Cancel"`) | Confirmed as a uniform dismiss/cancel button verb across `Assets/_Modules/Settings/SettingsController.cs:1014`, `Assets/_Modules/Village/Arena/ArenaAttackPaletteUI.cs:139`, `Assets/_Modules/HUD/Kit/HudKitController.cs:556`, `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:961`, `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:275`, `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:197`, `Assets/_Modules/Village/BuildMode/LeanTouchBuildDriver.cs:283`, `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2646`, `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:696`, `Assets/_Modules/Village/UI/RotateModelMenu.cs:307`, `Assets/_Modules/Village/Walls/WallRepairStrings.cs:61`. |
| `Close` | `common.close` | See table above. |
| `Help` | `settings.help.help` (value `"Help"`) | `Assets/_Modules/HUD/HelpMenu.cs:2,133,222,533`, `Assets/_Modules/HUD/HelpMenuVM.cs:159` — same noun (the Help feature name) used as both a Settings row label and the modal's own title. WO-1856 already applied this fix to its own new button. |
| `Iron` | `tooltip.resourceIron.title` (value `"Iron"`) | `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:404`, `Assets/_Modules/Village/BuildMode/FirstBuyDoorModel.cs:319` — resource-name noun, consistent with the tooltip's own usage. (`Assets/_Modules/Village/Dev/ResourceDevTool.cs:224` is a dev tool — not counted.) |
| `Sell` | `ownedTown.sell` (value `"Sell"`) | `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:189` and the shard-table's other cited `Sell` sites — button verb, consistent job. |

## Table — REJECTED (do not mint, do not reuse a common key — keep per-screen or don't localize at all)

| literal | verdict | reasoning |
|---|---|---|
| `(no message)` | **Not player-facing at all — do not localize.** | Every occurrence is a developer-diagnostic fallback for a missing error/log message, consumed only by `FlowTrace.Warn`/`FlowTrace.Fail` or an internal `TaskCompletionSource` result — never rendered to the player. Confirmed by reading past the ternary at each site: `Assets/_Modules/Core/Platform/WebGLPiPlatform.cs:315` (its `err` local feeds only `FlowTrace.Warn` calls at `:320,328,336` and TCS results), `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:1468` (a log-line truncator, `Flatten()`), `Assets/_Modules/Village/Monetization/Providers/Pi/PiAdGrantDecision.cs:65` (feeds a `trace` string), `Assets/_Modules/Village/Monetization/Providers/LevelPlayInitializer.cs:355` (feeds `FlowTrace.Fail`). This is a manifest classification gap (rule 5 in the sweep doc normally excludes `FlowTrace`/diagnostic lines, but this literal sits on the ternary-assignment line one line above the actual `FlowTrace` call, so the automated scanner missed the exclusion) — not a genuine shared UI string. Per CLAUDE.md §rule-6-style developer-surface exclusion, this stays hardcoded English forever. |
| `COST:` | **Only one genuine standalone site confirmed — stays per-screen, do not mint `common.cost`.** | Only `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:693` (`"COST: " + cost`) is a bare, all-caps, standalone cost-prefix label. The sweep doc's "2 files" count for this row appears to merge it with the differently-cased, differently-shaped `"Cost: FREE"` / `"Cost:Cost: Free"` complete-sentence fragments in `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:322,392` — those are already correctly proposed as their own per-screen key (`village.build_structure_info.cost_free`) in the sweep doc's per-shard table (§5), and collapsing a bare-prefix label with a complete-sentence fragment would violate the "verb/noun/fragment does a different job" rule. Recommendation: mint `village.build_collection_browser.cost` per-screen (as the sweep doc's own per-shard table already proposes) instead of a shared key. |
| `Retreat` (heading use) | **Partial reject — the button-verb job is shared (`common.retreat`, above); the heading-noun job is NOT and must stay per-screen.** | `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:720` — `public const string RetreatTitle = "Retreat";` — is the TITLE of the end-of-raid screen shown when the player calls off the assault (a noun naming what happened, read by the player as a headline), which is a different job from the two `Retreat` DANGER buttons that trigger the retreat action (`RaidDeployController.cs:2793`, `Village2RaidController.cs:334`). This is exactly the verb-vs-heading distinction `key-naming.md` warns about (German "Rückzug" as a button vs. a headline can differ; several locales use an imperative/infinitive verb form for the button and a nominal form for the headline). `common.retreat` covers ONLY the two button sites. `RetreatTitle` should get its own key (e.g. `village.end_state.retreat_title`) when that shard is wired — do not point it at `common.retreat`. |

## Locale/table completeness (this pass)

- **22 new `common.*` keys minted** (listed in the first table, excluding the two rows marked
  pre-existing): added to `Assets/Resources/Data/Canonical/*.json` and
  `Assets/StreamingAssets/Data/Canonical/*.json` for all 10 locales (en, es, de, fr, pt-BR, ru,
  ja, ko, zh-Hans, ar) — full key-set parity confirmed against `en.json` for every locale in
  both mirrors (534 keys each, 0 missing/extra either direction).
- Also completed the Unity Localization tables for `common.remnant_chat` and
  `common.leaderboard`, which existed in JSON (all 10 locales) but were missing from the
  Unity string tables — found already partially in flight (uncommitted, added by a concurrent
  session) at `m_Id 44124330363760650`/`44124330363760651` in `GameStrings Shared Data.asset`
  and all 6 enabled-locale table assets; this pass built on top of that in-flight state rather
  than re-minting duplicates.
- Unity `GameStrings Shared Data.asset` and the six enabled-locale table assets
  (`GameStrings_en/es/pt-BR/de/fr/ru.asset`) were extended with the 22 new keys, `m_Id`
  44124330363760655 through 44124330363760676 (continuing the session's existing
  `DistributedUIDGenerator`-style increment sequence — the block starting at
  ...363760650-654 was already in flight from another concurrent session's edit; this pass
  picked up at 655 to avoid an `m_Id` collision).
- `common.crystals` and `common.lv` are **format keys** (`{0}` positional placeholder, matching
  `LocalText.Format`'s `string.Format` convention — see `Assets/_Modules/Core/UI/LocalText.cs:157`)
  and were additionally registered against the `SmartFormatTag` metadata component
  (`rid 7338888474672496640`) in each of the 6 per-locale table assets' `references.RefIds`
  `m_SharedEntries` list, matching the precedent set by `raid.veterancyDenied` /
  `ownedTown.captureRequirement` in an earlier commit this session (`f12a1a5e2`).
- ar/ja/ko/zh-Hans received JSON-only translations (no Unity table asset exists for these
  locales, per the task brief — they are not in the 6-locale enabled-in-build set).
- All 20 touched JSON files were validated with `node -e "JSON.parse(...)"` after editing.
  All 7 touched `.asset` files were validated by stripping Unity's YAML tag directives and
  parsing the remainder with `yaml.safe_load` (Python) — all seven parse cleanly, and the
  `m_Id` set in each of the 6 per-locale tables is identical to the Shared Data key registry
  (532 entries each, exact match).

## Open notes for the next lane

- The **"Lv"** and **"Crystals:"** call sites listed above are all currently doing hand-rolled
  string concatenation (`"Lv " + lvl`, `"Crystals: " + amount`). The wiring lane must replace
  the concatenation with `LocalText.Format(key, args)`, not just swap in the literal — a bare
  `"Lv"` or `"Crystals:"` string substitution would break in any locale where the abbreviation
  doesn't sit in the same word order (this is exactly the `frag` warning in the sweep doc §5).
- `common.no_hero` has only one real player-facing call site; the second file the sweep doc
  counted (`SeatingEditorOverlay.cs`) is developer-only per its own header comment and must not
  be wired to this key.
- `village.end_state.retreat_title` (or similar) still needs its own per-screen key when the
  Village/UI/EndState shard is worked — see the Rejected table above.
