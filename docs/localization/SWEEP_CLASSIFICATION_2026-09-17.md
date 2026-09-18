# WO-1857 phase 1 - hardcoded-string sweep: full classification

**Produced:** 2026-09-17. **Pass:** classification and planning ONLY - no locale file, no `.cs`
file and no Localization table asset was touched by this pass.

**Manifest this document classifies:** `docs/localization/manifest.json`, REGENERATED this session
by `tools/localization/build-string-manifest.ps1`. Its own run line, verbatim:

```
Wrote docs/localization/manifest.json: 6459 entries (505 keyed table, 126 keyed calls, 5812 literals, 16 image reviews).
```

## 1. The detector gap - read this before trusting any count below

**The six `AddDockTab` labels that triggered WO-1857 are NOT in the manifest.** Proven, not inferred:
`Assets/_Modules/HUD/Kit/HudKitController.cs` lines 5557, 5558, 5559, 5560, 5567 and 5572 carry
`"Chat"`, `"Leaderboard"`, `"Music"`, `"Settings"`, `"Realm"`, `"Pause"`; the manifest holds 48 rows for
that file and its line numbers are 460 … 5126, 5767, 6320 - none of 5557-5572.

**Why.** `build-string-manifest.ps1:231` skips any line for which `Get-CSharpContext` (`:112-125`)
returns no `uiHint`. That function recognises `ShowToast`, `BuildObsidianModal`, `BuildObsidianButton`,
`.Button(`, `.Label(`, `SetStatus/ShowStatus/SetMessage`, a `.text/.placeholder/.caption/.title =`
assignment, or the bare WORDS title/body/label/caption/tooltip/message/placeholder/status/header/
description appearing anywhere on the line. `AddDockTab(_slideDock.panel, dockRow++, "Chat", OpenClanChat);`
matches none of them, so the literal is invisible. **Every project-local UI helper that is not on that
list is a blind spot** - `AddDockTab`, `ElarionUiKit.ButtonPack`, `MakeWordVerb`, `AddImage`,
`BuildObsidianPanel`, `DetailCardRow`, `AddButton`, and any future one.

**Measured size of the blind spot.** A statement-aware grep over `Assets/_Modules` (non-Editor/Test/
Regression), requiring a UI-construction call on the reconstructed statement, excluding diagnostics,
attributes, already-wired `LocalText` statements, generated files and dev surfaces, and keeping only
copy-shaped literals, returns **337 lines the manifest never emitted** 
(Village 211, Core 41, HUD 36, rest smaller). The full list is §1b.

**Measured, not estimated: 25 of those 337 were opened at source and judged. 16 were real player copy,
9 were not.** 64% precision, so the blind spot holds roughly **216 additional genuine leaks**
on top of the 817 statements in §5. The 9 misses were all one of three shapes, and they are the shapes
the scanner fix must also exclude:

- a `Guard.Try("<system>", "<label>", …)` system tag or step label wrapping a real toast
  (`ManaRecipeScrollService.cs:480`, `StarterArmyGrant.cs:321`, `ManageScreenPanel.cs:5643`,
  `TutorialFlow.cs:2889`, `BreakableContainer.cs:194`) - the COPY on those statements is the toast
  argument, which is a separate, usually already-keyed, literal;
- a host/object name (`FoundingChoiceController.cs:167` `AddImage(_canvas.transform, "Scrim", …)`);
- a separator or pure format hole with no words in it (`"\n"`, `$"{Mathf.RoundToInt(pct*100f)}%"`).

⚠ **I have NOT proven a total leak count for the repo,** and this document does not claim one. What is
proven: 817 statements from the manifest (§5), 22 manifest-absent rows found by a narrow grep (§1a),
and a measured ~216 more inside the 337 of §1b. A scanner-fix lane must land before anyone claims this
sweep is complete.

### 1a. The 22 rows a narrow, high-confidence grep proves ABSENT from the manifest

⚠ **Absent from the manifest is not the same as a genuine leak.** These rows are proven missing; each
one's disposition is in the `note` column, and five of them are NOT leaks - two are continuation lines
of a diagnostic (the same per-line artefact §2b describes, reproduced here because this grep also
checks for logs per line) and three are host names on a statement whose real copy is the next argument.

Lines in `_Modules` whose text calls `ElarionUiKit.(Label|Button*|BuildObsidian*|ShowToast|Caption)`,
`AddDockTab`, `new Label(`, `new Button(`, `SetStatus(` or `ShowToast(`, carry a literal, are not a
comment or a log line, and have **no manifest row at that line**:

| file:line | literal(s) | disposition |
|---|---|---|
| `Assets/_Modules/Core/UI/TutorialSkipUi.cs:184` | 'SkipControl BUILD FAILED - ElarionUiKit.BuildObsidianButton returned no button; ' | NOT a leak - continuation line of a `FlowTrace` diagnostic |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5557` | 'Chat' | genuine leak - **WO-1857 acceptance floor** |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5558` | 'Leaderboard' | genuine leak - **WO-1857 acceptance floor** |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5559` | 'Music' | genuine leak - **WO-1857 acceptance floor** |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5560` | 'Settings' | genuine leak - **WO-1857 acceptance floor** |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5567` | 'Realm' | genuine leak - **WO-1857 acceptance floor** |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5572` | 'Pause' | genuine leak - **WO-1857 acceptance floor** |
| `Assets/_Modules/Village/BuildMode/BuildHudController.cs:475` | 'Done BUILD FAILED - ElarionUiKit.BuildObsidianButton returned no button; ' | NOT a leak - continuation line of a `FlowTrace` diagnostic |
| `Assets/_Modules/Village/BuildMode/BuildHudController.cs:707` | 'OkChip' | `"OkChip"` is a host name; the copy is `PlaceVerbWord` (a variable) - check that variable instead |
| `Assets/_Modules/Village/BuildMode/BuildHudController.cs:732` | 'RotChip', 'ROTATE' | `"RotChip"` is a host name; `"ROTATE"` on the same line IS a leak |
| `Assets/_Modules/Village/BuildMode/BuildHudController.cs:735` | 'CancelChip', 'CANCEL' | `"CancelChip"` is a host name; `"CANCEL"` on the same line IS a leak |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:170` | 'KEEP IT' | genuine leak |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:174` | 'POLISH AGAIN' | genuine leak |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:916` | 'REMOVE', 'EQUIP' | genuine leak |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:955` | 'BACK' | genuine leak |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1230` | 'Unequip' | genuine leak |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1233` | 'Done' | genuine leak |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2435` | 'BACK' | genuine leak |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2441` | 'EQUIPMENT' | genuine leak |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2646` | 'Cancel' | genuine leak |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2655` | 'CONFIRM' | genuine leak |
| `Assets/_Modules/Wallet/PackStore.cs:5308` | '\\\\n' | genuine leak |

Net: **17 genuine leaks** in this table, including all six acceptance-floor rows.

`settings.title` already exists and is translated in all ten locale files, so the `"Settings"` row
reuses it rather than minting a key (this is the fix WO-1856 already applied to its own new button).

### 1b. The full 337 manifest-absent rows - the scanner fix's proof target

§7.1 requires the scanner-fix lane to re-diff the regenerated manifest against this document. That is
only checkable if the list lives here rather than in a scratch file, so it does. ~64% of these are
genuine leaks (measured above); the rest are `Guard.Try` tags, host names and separators. **After the
scanner fix, these lines should appear as `literalCandidate` rows, and the three exclusion shapes named
above should NOT.**

| file:line | literal |
|---|---|
| `Assets/_Modules/Audio/MusicSelectionPanel.cs:107` | Pick the music for where you are. Battle music still takes over during fights. |
| `Assets/_Modules/BattleATB/BattleHudUgui.cs:73` | Knight |
| `Assets/_Modules/BattleATB/BattleHudUgui.cs:74` | Ranger |
| `Assets/_Modules/BattleATB/BattleHudUgui.cs:75` | Wizard |
| `Assets/_Modules/BattleATB/BattleHudUgui.cs:887` | \\n |
| `Assets/_Modules/BattleATB/Engine/Actions.cs:103` | {target.Name} is afflicted with {opts.ApplyStatus.Value.ToToken()}. |
| `Assets/_Modules/BattleATB/Engine/Actions.cs:236` | {t.Name} is marked by {ability.Name}. |
| `Assets/_Modules/BattleATB/Engine/Actions.cs:253` | {actor.Name} gains {ability.SelfStatus.Value.ToToken()}. |
| `Assets/_Modules/BattleATB/Engine/Actions.cs:448` | {t.Name} is afflicted with {special.ApplyStatus.Value.ToToken()}. |
| `Assets/_Modules/BattleATB/Engine/Combat.cs:225` | {unit.Name} bleeds for {lost}. |
| `Assets/_Modules/BattleATB/Engine/Combat.cs:242` | {unit.Name} regenerates {healed} HP. |
| `Assets/_Modules/BattleATB/Engine/Combat.cs:269` | {unit.Name}'s {s.Kind.ToToken()} wears off. |
| `Assets/_Modules/Core/Debug/DebugCanvasUI.cs:156` | <color=#FF6B6B>No Wallet Bound</color> |
| `Assets/_Modules/Core/Debug/DebugCanvasUI.cs:157` | <color=#6BFF9E>{wallet[..Mathf.Min(wallet.Length, 16)]}…</color> |
| `Assets/_Modules/Core/HudModel/DefenseReportChipModel.cs:90` | \\n |
| `Assets/_Modules/Core/Manage/ManageVmProjection.cs:209` | NO DATA |
| `Assets/_Modules/Core/Manage/ManageVmProjection.cs:234` | LEVEL  |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:85` | Ads and Your Privacy |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:93` | Echoes of Elarion can show optional ads - watch one to speed up a build or double  |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:94` | a harvest. You never have to watch one to play.\\n\\n |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:95` | May we use your advertising ID to show ads matched to your interests?\\n\\n |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:96` | If you say no, you will still see ads and still get the rewards - they just will  |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:97` | not be matched to you. You can change this any time in Settings. |
| `Assets/_Modules/Core/UI/GuidePointer.cs:178` | Chevron |
| `Assets/_Modules/Core/UI/LayoutOracle.cs:239` | SUB-TOUCH-FLOOR BAND |
| `Assets/_Modules/Core/UI/LayoutOracle.cs:240` | ' resolves  |
| `Assets/_Modules/Core/UI/LayoutOracle.cs:241` |  ref px -- shortest side  |
| `Assets/_Modules/Core/UI/LayoutOracle.cs:242` |  px UNDER  |
| `Assets/_Modules/Core/UI/LayoutOracle.cs:243` | ElarionUiKit.MinTouchPx ( |
| `Assets/_Modules/Core/UI/LayoutOracle.cs:244` | ). ClampMinTouch will grow it SYMMETRICALLY about its centre at runtime and  |
| `Assets/_Modules/Core/UI/LayoutOracle.cs:245` | spill it into both neighbours. Author the band AT the floor. |
| `Assets/_Modules/Core/UI/LoadingOverlay.cs:149` | Retry |
| `Assets/_Modules/Core/UI/LoadingOverlay.cs:238` | Retry |
| `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:415` | Skip Tutorial |
| `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:416` | Skip the tutorial? You'll keep everything it grants. |
| `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:417` | Skip |
| `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:418` | Keep Playing |
| `Assets/_Modules/Core/UI/ObsidianNavigationWorkspace.cs:128` | Canvas |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:129` | Play Offline |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:137` | Download everything now so the game works without a connection?\\n\\n |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:138` | Checking download size... |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:146` | \\n |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:199` | the game still works normally with a connection. |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:224` | Download everything now so the game works without a connection?\\n\\n |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:225` | Size: {mb:F0} MB\\n{estimate}\\n\\n |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:226` | You need Wi-Fi for this one-time download. After it finishes the game  |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:227` | opens without a connection. |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:278` | {Mathf.RoundToInt(pct * 100f)}%   {doneMb:F0} of {totalMb:F0} MB |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:279` | {Mathf.RoundToInt(pct * 100f)}% |
| `Assets/_Modules/Core/UI/TutorialSkipUi.cs:259` | Skip Tutorial |
| `Assets/_Modules/Core/UI/TutorialSkipUi.cs:260` | Skip the walkthrough? Your progress is saved. |
| `Assets/_Modules/Core/UI/TutorialSkipUi.cs:261` | Skip |
| `Assets/_Modules/Core/UI/TutorialSkipUi.cs:262` | Keep Playing |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:960` | Exit |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:961` | Cancel |
| `Assets/_Modules/Dungeons/DungeonTreasurePanel.cs:225` | TREASURE FOUND |
| `Assets/_Modules/Dungeons/DungeonTreasurePanel.cs:309` | First clear -- a new recipe is remembered. |
| `Assets/_Modules/Dungeons/UI/LoreReadingModal.cs:89` | Scrim |
| `Assets/_Modules/Dungeons/Wanderer/WandererBubble.cs:266` | \\n |
| `Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:210` |  items -- scroll the list for more |
| `Assets/_Modules/HUD/BugReportView.cs:316` | screenshot excluded |
| `Assets/_Modules/HUD/BugReportView.cs:329` | Retry send |
| `Assets/_Modules/HUD/BugReportView.cs:330` | Send report |
| `Assets/_Modules/HUD/DailyQuestHud.cs:163` | Daily Quests |
| `Assets/_Modules/HUD/DailyQuestHud.cs:279` | + Done |
| `Assets/_Modules/HUD/DailyQuestHud.cs:404` | Crystals |
| `Assets/_Modules/HUD/DailyQuestHud.cs:407` | Stone |
| `Assets/_Modules/HUD/DailyQuestHud.cs:410` | Wisdom |
| `Assets/_Modules/HUD/DailyQuestHud.cs:413` | Bonus item |
| `Assets/_Modules/HUD/DailyQuestHud.cs:422` | Complete |
| `Assets/_Modules/HUD/DebuggingController.cs:406` | \\n |
| `Assets/_Modules/HUD/HelpMenu.cs:570` | \| Build button: tower placement \| F: interact \| Esc: pause |
| `Assets/_Modules/HUD/HelpMenu.cs:581` | Music: original score by DeNelle Studios (made with Suno).  |
| `Assets/_Modules/HUD/HelpMenu.cs:582` | Sound effects: leohpaz 'RPG Essentials' and Hovl Studio,  |
| `Assets/_Modules/HUD/HelpMenu.cs:583` | licensed via the Unity Asset Store. |
| `Assets/_Modules/HUD/HelpMenu.cs:647` | Reset Hero & Echoes |
| `Assets/_Modules/HUD/HelpMenu.cs:648` | Reset your hero and Echoes and begin again? This cannot be undone. |
| `Assets/_Modules/HUD/HelpMenu.cs:649` | Reset |
| `Assets/_Modules/HUD/HelpMenu.cs:650` | Keep Progress |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:457` | A wall |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:458` |  is damaged ( |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:643` | Hero |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:4045` | Gold |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:4711` |  enemies remain |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5406` | v/> |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5557` | Chat |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5558` | Leaderboard |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5559` | Music |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5560` | Settings |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5567` | Realm |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5572` | Pause |
| `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:426` | First raid free -  |
| `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:467` | Complete its requirement first |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:150` |  structures |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:151` |  shown with safe placeholders |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:152` |   •  ambient patrols are local presentation only |
| `Assets/_Modules/Onboarding/FirstWatchWelcomeLetter.cs:64` | WELCOME TO THE WATCH |
| `Assets/_Modules/Onboarding/FoundingChoiceController.cs:167` | Scrim |
| `Assets/_Modules/Onboarding/FoundingChoiceController.cs:186` | FOUND YOUR TOWN |
| `Assets/_Modules/Onboarding/FoundingChoiceController.cs:230` | Begin with a ready settlement and starter defenses, or choose an empty realm to build yourself. |
| `Assets/_Modules/Onboarding/FoundingChoiceController.cs:284` | READY SETTLEMENT  (Recommended) |
| `Assets/_Modules/Onboarding/FoundingChoiceController.cs:287` | EMPTY REALM  (Build It Yourself) |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:384` | Choose Your Hero |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:468` | Enter Elarion |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:959` | Rule |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:995` | Pip |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:296` | Scrim |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:307` | YOUR WALLET |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:345` | Continue with Google to protect your progress across devices, or play as a guest on this device. |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:346` | Your wallet is your save. Connect now (one-time on this device). Guest progress stays here until you connect. |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:428` | or tap Play as Guest to start now. |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:431` | or tap Play as Guest to start now. |
| `Assets/_Modules/Onboarding/OnboardingFlow.cs:290` | Scrim |
| `Assets/_Modules/Settings/SettingsController.cs:1011` | Repair Session |
| `Assets/_Modules/Settings/SettingsController.cs:1012` | This clears a stuck screen state. Your progress is not affected. |
| `Assets/_Modules/Settings/SettingsController.cs:1013` | Repair |
| `Assets/_Modules/Settings/SettingsController.cs:1014` | Cancel |
| `Assets/_Modules/Settings/SettingsController.cs:1045` | Repair Session |
| `Assets/_Modules/Village/Arena/ArenaPanel.cs:213` | Close |
| `Assets/_Modules/Village/Arena/ArenaPanel.cs:401` | Close |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:342` | collection= |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:343` |  subtitle=' |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:674` | Artwork |
| `Assets/_Modules/Village/BuildMode/BuildModeController.cs:556` | You can't build in enemy territory. |
| `Assets/_Modules/Village/BuildMode/BuildModeController.cs:2575` | Nothing is built yet — place something first. |
| `Assets/_Modules/Village/BuildMode/BuildModeController.cs:2584` | Tap a building or wall to move, upgrade or sell it. |
| `Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs:100` | ORIENT  |
| `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:324` | Cost: |
| `Assets/_Modules/Village/BuildMode/ObsidianQueueHud.cs:207` | WORK QUEUE |
| `Assets/_Modules/Village/BuildMode/UnderConstructionVisual.cs:462` | {(int)(s / 60)}:{(int)(s % 60):00} |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1045` | NEXT UPGRADE -  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:2075` | Fill |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:762` | Extra queue slot bought - the queue can take  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:763` | Could not buy a slot right now. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:842` | Tier  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:843` | Grid refreshed. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:866` |  (you have  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:878` | s until work here finishes. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:915` | Tier  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:916` | Tier  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:917` | s. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:988` | Upgrade queued - it starts when a builder frees up. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:989` | Upgrade under construction -  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1040` |  Crystals to raise the Heart (you have  |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1295` | Every enhancement here is unlocked. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1296` | Tap the gold tile to unlock the next enhancement. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1373` | Every enhancement here is unlocked. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1374` | Tap the gold tile to unlock the next enhancement. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1446` | Every enhancement here is unlocked. |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1447` | Tap upgrade to raise this structure a level. |
| `Assets/_Modules/Village/Buildings/UI/TowerEmpowerButton.cs:135` | (have {CrystalEconomy.Instance.CurrentCrystals}) |
| `Assets/_Modules/Village/Buildings/UI/TowerEmpowerButton.cs:164` | Need {cost} Crystals  (have {have}) |
| `Assets/_Modules/Village/Buildings/UI/TowerEmpowerButton.cs:165` | Cannot empower now. |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:94` | POLISH AGAIN? |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:153` | There is a  |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:170` | KEEP IT |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:174` | POLISH AGAIN |
| `Assets/_Modules/Village/Harvest/EchoRepairProgressBillboard.cs:43` | Track |
| `Assets/_Modules/Village/Harvest/EchoRepairProgressBillboard.cs:47` | Fill |
| `Assets/_Modules/Village/Harvest/EchoRosterView.cs:190` | ECHOES OF ELARION |
| `Assets/_Modules/Village/Harvest/EchoRosterView.cs:393` | Six spirits wait -- one awakens every  |
| `Assets/_Modules/Village/Harvest/EchoRosterView.cs:468` | Portrait |
| `Assets/_Modules/Village/Harvest/EchoUnlockDialogue.cs:297` | ECHOES OF ELARION |
| `Assets/_Modules/Village/Harvest/EchoUnlockDialogue.cs:483` | Ready to help your realm. Manage this Echo from the Echoes menu. |
| `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs:424` | Echoes 1/6 |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:124` | ECHO HARVEST |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:226` | 2x harvest speed is active |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:228` | Ad closed early - no boost granted |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:245` | Playing ad... |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:247` | Watch Ad - add 1 hour of 2x speed |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:248` | Watch Ad - 2x speed for 1 hour |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:259` | Optional: fills the same silo twice as fast |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackDoorsVM.cs:155` | MANAGE > |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackDoorsVM.cs:169` | REPORT > |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:707` | Track |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:710` | Fill |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1140` | Empty |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1230` | Unequip |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1233` | Done |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1440` | Socket |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1445` | Icon |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1461` | Equipped |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1594` | Silhouette |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1727` | Portrait view - live model unavailable |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1728` | Live model unavailable |
| `Assets/_Modules/Village/Hero/HeroAbilities.cs:993` |  cancelled - you moved. Stand still to shoot. |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:379` | Party Shop |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:412` | Gold |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:772` | Portrait |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1142` | PartyShop.list |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1357` | Purchase 1,250 Gold |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1766` | Locked |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1768` | Owned |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1769` | Purchase |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:763` |  improved to Lv  |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:764` | Improve failed. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:980` | Tap a row to BUY (auto-equips) or EQUIP what you own. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1122` | Tap a row to view it, then Purchase. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1148` | Tap a row to view it, then Purchase. Equip jewelry from the Character screen. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1322` | Nothing in your pack this stall would buy. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1323` | Tap an item to SELL it for coins. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1357` | No jewelry or cut stones in your pack to sell. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1358` | Tap an item to SELL it for coins. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1501` | You own no gear to sell here. |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1502` | Tap an item to SELL it for coins - buy without leaving. |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:394` | Raid |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:409` | RAID:  |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:532` | ARMY - |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:883` | Scout the camp |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1204` | Raid under construction — battleground not in this build. |
| `Assets/_Modules/Village/Hero/RaidEntryBridge.cs:147` | Raids are turned off in this build. |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:392` | No troops yet - train troops at the Barracks, then open Raids. |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:394` | Army  |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:395` | . Train at the Barracks, then open Raids. |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:396` | Army  |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:1010` | Clock:  |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:731` | Fill |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:905` | Fill |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:1038` | Fill |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:1190` | Fill |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:101` | Barracks - Train |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:618` | Couldn't train  |
| `Assets/_Modules/Village/Items/CraftingPanelMvvm.cs:184` | Select a recipe to inspect |
| `Assets/_Modules/Village/Items/CraftingPanelMvvm.cs:185` | Combine ingredients dropped by enemies into potions and bombs. |
| `Assets/_Modules/Village/Items/CraftingPanelMvvm.cs:267` | Alchemy |
| `Assets/_Modules/Village/Items/JewelerPanelMvvm.cs:198` | Select a piece to inspect |
| `Assets/_Modules/Village/Items/JewelerPanelMvvm.cs:199` | Set gems into a ring or amulet to forge a finer piece. |
| `Assets/_Modules/Village/Items/JewelerPanelMvvm.cs:236` | Requires hero level  |
| `Assets/_Modules/Village/NPCs/TownsfolkBubble.cs:149` | \\n |
| `Assets/_Modules/Village/Progression/BattlePlansReveal.cs:339` | Backdrop |
| `Assets/_Modules/Village/Progression/BattlePlansService.cs:546` | plans fallback toast |
| `Assets/_Modules/Village/Progression/ManaRecipeScrollService.cs:480` | Progression |
| `Assets/_Modules/Village/Progression/ManaRecipeScrollService.cs:482` | Recipe learned: Mana Draught. Brew it at the Apothecary. |
| `Assets/_Modules/Village/Progression/SpirePlansCelebration.cs:391` | Backdrop |
| `Assets/_Modules/Village/Progression/SpirePlansCelebration.cs:462` | The Echoes restored  |
| `Assets/_Modules/Village/Progression/SpirePlansCelebration.cs:463` | Your structures are already fully repaired. |
| `Assets/_Modules/Village/Siege/RoamingHordeNotifications.cs:125` | {size}Horde approaching. Expected at the town in {timing}. Return to defend live. |
| `Assets/_Modules/Village/Siege/RoamingHordeNotifications.cs:126` | roaming-horde:{waveId} |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:207` | TAP TO\\nASSIGN |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:254` | No skills unlocked yet. |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:260` | OPEN  |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:297` |  more unlocked |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:389` | Your class kit is fixed. Tap a skill, then a socket, to fill your hot-swap bar. |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:136` | Tap a skill, then a hot-swap slot to assign. |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:137` | Now tap a hot-swap slot. |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:735` |  - next point at Level  |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:761` | </b></color>\\n |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2520` | WISDOM 0 - next point at Level 2 |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2570` | Talent |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2646` | Cancel |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:2837` | Assigned skills - change them in  |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:966` | Plate |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:270` | Raid controls failed to load - you will be evacuated when the clock runs out. |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1657` | will not make it back. |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1728` | Time! Falling back to the castle... |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1729` | Retreating to the castle... |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3081` | RpgUi/troop/ |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3154` | All ready troops are already deployed. |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3187` | [x |
| `Assets/_Modules/Village/Troops/RaidHudController.cs:592` | Star |
| `Assets/_Modules/Village/Troops/StarterArmyGrant.cs:321` | Raid |
| `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs:1469` | Tutorial |
| `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs:2889` | Tutorial |
| `Assets/_Modules/Village/UI/Defense/DefenseReportPanel.cs:192` | Attacks On Your Town |
| `Assets/_Modules/Village/UI/Defense/DefenseReportPanel.cs:910` | Bezel |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:281` | Flawless! The realm is safer because of you! |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:282` | The realm is safer because of you! |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:287` | Continue |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:321` | Wood |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:382` | Fall back and regroup, hero. |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:384` | Continue |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:404` | The raid is lost. You retreat to the castle to fight another day. |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:405` | The dark takes you, but Elarion still needs its defender. |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:407` | Rise again |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:433` | Try Again |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:654` | Return to Castle |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:700` | \\n |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:865` | Return to Castle |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:1020` | A decisive defense. Review what changed before the next assault. |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:1021` | The realm holds. Review the result, then prepare the next defense. |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:1114` | The realm holds - but it took damage. |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:1119` | The realm holds. Spoils claimed. |
| `Assets/_Modules/Village/UI/Guide/GameGuidePanel.cs:100` | Game Guide |
| `Assets/_Modules/Village/UI/Manage/HeartPanel.cs:160` | HEART OF ELARION |
| `Assets/_Modules/Village/UI/Manage/HeartPanel.cs:369` | Cost: |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5162` | Upgrade: |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5176` | After upgrade:  |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5211` | Locked. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5223` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5250` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5270` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5538` | Level  |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5643` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5731` | After upgrade:  |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5755` | TRAIN 1  |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5759` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6160` | Upgrade: |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6170` | After upgrade:  |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6185` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6516` | Research: |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6548` | Locked. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6560` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6576` | Manage |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:7164` | Art |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:884` | Queued - expanded, pick one to cancel |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:885` | Queued x |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2686` | Muster a saved army onto the Training line |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2689` | Open |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2809` |  - Tier  |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2847` |  gold |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:3306` | Ready |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:4253` | build-category: |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:489` | SHEATHED: dialing the on-back (town) pose — saved under the @sheathed key. |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:490` | DRAWN: dialing the in-hand seat. |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:506` | ABSOLUTE: pos/rot ARE the back pose (socket frame, no global yaw). |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:507` | NUDGE: pos/rot add on top of the built-in sheathe pose (zero = today's pose). |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:527` | VERTICAL: weapon stands up (longest to +Y, hilt low); rotation is the absolute in-hand pose. |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:528` | NUDGE: rotation adds on top of the geometric grip (legacy WO-551 path). |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:559` | Saved '{_offsetKey}' to local settings ({AttachmentOffsetRegistry.DevPath}). Re-equipped from file. |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:560` | Save FAILED for '{_offsetKey}' (see Console). |
| `Assets/_Modules/Village/Walls/HubRepairAffordance.cs:423` | \\n |
| `Assets/_Modules/Village/Walls/HubRepairAffordance.cs:436` | \\n |
| `Assets/_Modules/Village/World/BreakableContainer.cs:194` | surface chest in-combat refusal |
| `Assets/_Modules/Village/World/Camps/ChallengeOutpostVictoryController.cs:227` | The garrison is broken. Some of the reward could not be paid out. |
| `Assets/_Modules/Village/World/Camps/ChallengeOutpostVictoryController.cs:228` | The garrison is broken. |
| `Assets/_Modules/Village/World/Camps/ChallengeOutpostVictoryController.cs:229` | Return to Castle |
| `Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs:511` | Echo |
| `Assets/_Modules/Village/World/GateIntelHud.cs:196` |    -   Threat  |
| `Assets/_Modules/Village/World/GateIntelHud.cs:197` |    -   Threat  |
| `Assets/_Modules/Wallet/NightMarketSharedCardSession.cs:31` | View offer |
| `Assets/_Modules/Wallet/NightMarketSharedCardSession.cs:43` | Choose an offer to inspect its full contents and current channel price. |
| `Assets/_Modules/Wallet/NightMarketSharedCardSession.cs:44` | UI/NightMarket/night-market-wordmark |
| `Assets/_Modules/Wallet/PackStore.cs:3442` | Reconcile - no new payment |
| `Assets/_Modules/Wallet/PackStore.cs:3622` | Buy - {StorePriceMajor(pack)} |
| `Assets/_Modules/Wallet/PackStore.cs:5308` | \\n |
| `Assets/_Modules/Wallet/WalletRegistry.cs:204` | SKR rewards distribution to players (Streams A/B/C). |
| `Assets/_Modules/Wallet/WalletRegistry.cs:214` | Devnet pack-purchase smoke-test recipient. |

## 2. Counts

### 2a. What the manifest emitted

| manifest kind | rows |
|---|---|
| `keyedEntry` | 505 |
| `keyedCall` | 126 |
| `literalCandidate` | 5812 |
| `imageTextCandidate` | 16 |

Of the 5812 `literalCandidate` rows: **3291** are C# call
sites under `Assets/_Modules/**`, and **2521** are authored values inside canonical JSON data
files (a different axis entirely - see §6a).

### 2b. Classification of the C# literal candidates

Classified at **statement** level, not line level: the manifest's unit is one literal on one line, which
(a) splits one concatenated sentence into several rows and (b) hides multi-line `FlowTrace`/`Debug.Log`
calls from the scanner's own per-line exclusion, so their continuation lines surface as candidates.

| bucket | literal rows | share |
|---|---|---|
| `false-positive-flowtrace-or-debug` (comment text) | 876 | 26.6% |
| `genuine-leak` | 866 | 26.3% |
| `false-positive-flowtrace-or-debug` | 809 | 24.6% |
| `false-positive-enum-or-identifier` | 496 | 15.1% |
| `false-positive-flowtrace-or-debug` (developer runtime surface) | 190 | 5.8% |
| `already-has-key-but-hardcoded-elsewhere` | 52 | 1.6% |
| `false-positive` (already wired via `LocalText` with an English fallback arg) | 2 | 0.1% |
| **total** | **3291** | |

Collapsed to statements, the actionable set is **817 statements** (51 of which an existing key already covers).

Rolled up into the five buckets the ticket asked for:

| ticket bucket | statements |
|---|---|
| `genuine-leak` | 766 |
| `already-has-key-but-hardcoded-elsewhere` | 51 |
| `false-positive-enum-or-identifier` | 496 literal rows |
| `false-positive-flowtrace-or-debug` | 1875 literal rows (diagnostic 809 + comment text 876 + developer surface 190) |
| `needs-human-judgment` | 2521 canonical-JSON values as ONE category question (§6a) + the named rows in §6 |

## 3. How each bucket was decided, and its measured precision

Rules, applied in this order to the reconstructed statement (not the line):

1. line starts `//` `///` `*` `/*`, or the literal sits in a trailing `//` comment -> comment text.
2. path is generated (`/Generated/`, `*.g.cs`) -> embedded data blob, not a call site.
3. statement already calls `LocalText.Get/Format` or `new LocalizedText` -> already wired.
4. statement carries `[Tooltip] [Header] [SerializeField] [JsonProperty] [Range] …` -> metadata.
5. statement carries `FlowTrace.` `Debug.Log` `Logger.` `Console.` `Guard.Try` `.Record(` `throw new`
   `Exception(` `Sink.Info/Warn/Error` `EmitSheatheTrace` `_lastDetail` -> diagnostic.
6. file is a developer runtime surface (`/DevTools/`, `/Diagnostics/`, `AdminOverlay.cs`,
   `OwnerDevToolsOverlay.cs`, `ElarionUiKitDemo.cs`, `AutoPilot*`, `GateTraversalProof.cs`,
   `DevPanel*`, `MagentaGuard.cs`, `EnemyPlaceholderReveal.cs`, `BreakCaptureHarness.cs`,
   `FlowTrace.cs`, `Guard.cs`) -> developer-facing, correctly never localized.
7. statement builds a wire payload (escaped `\"`, `Json(`, `JsonUtility`, `UnityWebRequest`,
   `SetRequestHeader`, `WWWForm`) -> protocol, not copy.
8. literal is pure punctuation/whitespace, or shorter than 2 chars -> concatenation fragment.
9. literal is PascalCase or carries an `_` and no space (`BuildMenuUI`, `SpikePad`, `Pad_Label`)
   -> GameObject / host name. Real single-word UI copy is either ALL-CAPS (`UNLOCKED`) or one
   capitalised word (`Next`), which this rule deliberately does not catch.
10. literal is an identifier shape (camelCase, kebab, dotted) and the statement has no
    UI-construction call -> identifier.
11. statement assigns an identity/name/path/keyword field and has no UI-construction call -> identifier.
12. otherwise -> `genuine-leak`; and if the trimmed literal case-insensitively equals an existing
    `en.json` English value, -> `already-has-key-but-hardcoded-elsewhere` naming that key.

**Measured precision (read at source, this session).** The rules were built in three passes, each pass
sampling `genuine-leak` rows at source and being corrected by what the read showed - so the numbers
below are what a read actually found, not a self-assessment:

- Pass 1 sample of 22: found comment-resident literals, generated-file data blobs, `ElarionUiKitDemo`
  captions, and single-word identifiers wrongly excluded by an over-broad token rule. Rules 1, 2, 6, 9
  and the narrowing of rule 10 come from that read.
- Pass 2 sample of 16: 14 real, 2 misses - `GameStateService.cs:3152` (a developer hint string
  assembled by a helper the diagnostic rule does not name) and `ManageStateInvariants.cs:56` (a dev
  invariant failure message). ~88%.
- Pass 3 targeted read of the shared-string candidates then showed a distinct residue class:
  `"Title"`, `"Body"`, `"Header"`, `"Footer"`, `"Label"`, `"N0"`, `"HTTP_"` - layout/zone host names,
  format specifiers and identifier prefixes. Opened at source (`MonthlyLedgerPanel.cs:199`
  `Zone(_body, "Header", …)`, `SceneRouter.cs:123` `const string Title = "Title"`,
  `PackStore.cs:4204` `amount.ToString("N0")`) and excluded by the STRUCTURAL / format-specifier /
  trailing-underscore rules above.

After all three passes the residue is one shape: **developer text routed through a project-local emitter**.
Each fixing lane must re-read its own statement before wiring - this document is a work list, not a
licence to wire blind.

Exclusion buckets were sampled the same way (about 10-15 rows each, opened at source). The comment,
diagnostic and identifier buckets read clean. The developer-surface bucket is the one with judgment in
it, and §6 names the borderline file.

## 4. Shared `common.*` candidates - mint these FIRST, before any shard runs

`common.close = "Close"` already exists in `Assets/Resources/Data/Canonical/en.json`, so this is an
EXISTING namespace being extended, not a new convention. Each row below is one literal appearing in
**two or more separate files**; minting it per screen is how a translator ends up with six rows that
say `Cancel`. Where an existing key already covers the concept, **reuse it - do not mint a `common.*`
synonym** (`docs/localization/key-naming.md`, 'Meaning and reuse').

| literal | files | shards | proposal |
|---|---|---|---|
| `Build` | 6 | HUD, Village | **reuse `hud.nav.build`** |
| `ECHOES OF ELARION` | 4 | Core, DialogueUI, Village | `common.echoes_elarion` |
| `Skip` | 4 | Onboarding, Village | `common.skip` |
| `Upgrade` | 4 | Village | **reuse `ownedTown.upgrade`** |
| `(no message)` | 3 | Core, Village | `common.no_message` |
| `Cancel` | 3 | HUD, Village | **reuse `armyScreen.cancel`** |
| `Crafting` | 3 | Dungeons, Village | `common.crafting` |
| `Done` | 3 | Village | `common.done` |
| `Equipped` | 3 | HUD, Village | `common.equipped` |
| `Gold` | 3 | Village | `common.gold` |
| `No hero to equip.` | 3 | Village | `common.no_hero_equip` |
| `Ad` | 2 | Village | `common.ad` |
| `Brom's Rumor Board` | 2 | Village | `common.brom_s_rumor` |
| `Close` | 2 | Core, Onboarding | **reuse `common.close`** |
| `COST:` | 2 | Village | `common.cost` |
| `Crystals:` | 2 | Village | `common.crystals` |
| `Help` | 2 | HUD | **reuse `settings.help.help`** |
| `Iron` | 2 | Village | **reuse `tooltip.resourceIron.title`** |
| `Job` | 2 | Village | `common.job` |
| `Jukebox` | 2 | Audio | `common.jukebox` |
| `Leaderboard` | 2 | HUD | `common.leaderboard` |
| `Lv` | 2 | HUD, Village | `common.lv` |
| `Next` | 2 | HUD, Onboarding | `common.next` |
| `No hero.` | 2 | Village | `common.no_hero` |
| `No inventory.` | 2 | Village | `common.no_inventory` |
| `Powered with SKR` | 2 | Core, Onboarding | `common.powered_skr` |
| `Recipe` | 2 | Dungeons | `common.recipe` |
| `Remnant Chat` | 2 | HUD | `common.remnant_chat` |
| `Retreat` | 2 | Village | `common.retreat` |
| `Select an item first.` | 2 | Village | `common.select_item_first` |
| `Sell` | 2 | Village | **reuse `ownedTown.sell`** |
| `Stake Rewards` | 2 | Core | `common.stake_rewards` |
| `Towers` | 2 | Village | `common.towers` |

⚠ **A shared key is a MEANING claim, not a spelling match.** `key-naming.md` is explicit that the same
English word gets separate keys when it does different jobs - a verb on a button and a noun in a
heading translate differently in German and Japanese. Before collapsing any row above, the lane opens
both call sites and confirms the job is the same. Rows where it is not stay per-screen.

## 5. Per-shard work lists

Sharded by top-level module directory under `Assets/_Modules/`. The shards are **`.cs`-disjoint**: no
source file appears in two shards, so the code edits parallelise per CLAUDE.md §9/§11. Within a shard a
single file may still be large enough to need its own lane - per-file counts are given so the lead can split.

### ⛔ 5a. The shards are NOT disjoint on the locale files - that is a serialization bottleneck

**Every lane, plus the `common.*` lane, has to add rows to the SAME twelve files:**

1. `Assets/Resources/Data/Canonical/en.json`
2. `Assets/StreamingAssets/Data/Canonical/en.json` (must stay byte-equal in value to #1)
3-11. the nine other locale JSON files alongside them
12. `Assets/Localization/Tables/GameStrings_en.asset` (the Unity Localization package's own table -
    a THIRD English copy, and the one WO-1857 §4 records as silently drifting when only the JSON moves)

This is the same shape as `VillageSceneBuilder.cs` in CLAUDE.md §9: a file only ONE lane may touch at a
time. Fanning 13 shard lanes onto one `en.json` produces exactly the merge damage §0/§11 exist to
prevent, and `LocaleParityRegression` would then fail for whichever lane committed second - through no
fault of its own.

**Protocol for the fixing phase (this is the part that makes the parallelism real):**

- A shard lane edits **`.cs` files ONLY.** It does not open a locale JSON or the table asset.
- It emits its key/value additions as a **sidecar** the lead can merge mechanically - a fenced JSON
  block in its own `.RESULT.md`, or a per-shard fragment file, one `"key": "English"` pair per line.
- **ONE merge step per batch** (the lead, or a single dedicated lane) writes all twelve files from the
  collected sidecars, then runs `LocaleParityRegression` once. That is the serialization point, and it
  is cheap because it is mechanical.
- The `common.*` lane runs FIRST and alone, so its keys exist before any shard references them.

Columns: `file:line` is the first line of the statement's literal; **frag** marks a statement whose copy
is built by concatenation, which must become ONE `LocalText.Format` key with named arguments rather than
one key per fragment (`key-naming.md`: 'Prefer one complete sentence over concatenated fragments').
`existing key` non-empty means this is an `already-has-key-but-hardcoded-elsewhere` row: **reuse that
key, do not mint.** Proposed keys are `<domain>.<surface>.<meaning>` per `key-naming.md` and are
SUGGESTIONS - the lane confirms against neighbouring keys in that area before minting.

⛔ **A proposed key marked `[rename]` is a PLACEHOLDER and must not be minted as written.**
`docs/localization/key-naming.md:22-23` forbids using the current English sentence as a key, and the
machine-generated suggestion below does exactly that whenever the copy is a sentence
(`hud.bug_report.includes_recent_game_logs_player` IS the sentence). Any row whose meaning segment runs
four words or more carries `[rename]`: the lane replaces that segment with a short MEANING noun
(`…​.log_notice`, not `…​.includes_recent_game_logs_player`) before it touches a locale file. Short
verb/noun keys (`hud.bug_report.send_report`) are fine as generated. **This matters because WO-1857
routes the fixing work to low-tier lanes, which take a suggestion literally.**

⚠ **A `frag` row shows its literals CONCATENATED, which is wrong when the statement is a ternary.**
`CosmeticShopPanel.cs:472` renders here as `EquipEquipped {displayName}` because the statement is
`… ? "Equip" : "Equipped {displayName}"` - two alternative captions, not one sentence, so it needs TWO
keys. Every `frag` row is read at source before it is wired; the flag means *this statement is not a
single literal*, not *these literals concatenate*.

| shard | statements | files | suggested lanes |
|---|---|---|---|
| `Assets/_Modules/Village/` | 541 | 124 | split by sub-directory |
| `Assets/_Modules/Core/` | 88 | 41 | 1 lane |
| `Assets/_Modules/HUD/` | 72 | 16 | 1 lane |
| `Assets/_Modules/Dungeons/` | 33 | 9 | 1 lane |
| `Assets/_Modules/Onboarding/` | 28 | 9 | 1 lane |
| `Assets/_Modules/Wallet/` | 14 | 7 | 1 lane |
| `Assets/_Modules/Settings/` | 9 | 3 | 1 lane |
| `Assets/_Modules/Web3/` | 9 | 1 | 1 lane |
| `Assets/_Modules/BattleATB/` | 7 | 3 | 1 lane |
| `Assets/_Modules/Audio/` | 6 | 4 | 1 lane |
| `Assets/_Modules/Pets/` | 4 | 2 | 1 lane |
| `Assets/_Modules/DialogueUI/` | 3 | 1 | 1 lane |
| `Assets/_Modules/GooglePlay/` | 3 | 2 | 1 lane |

### Shard `Assets/_Modules/Village/` - 541 statements, 124 files

Heaviest files (split these into their own lanes):

- `Assets/_Modules/Village/Hero/PartyShopVM.cs` - 37
- `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs` - 31
- `Assets/_Modules/Village/Troops/RaidDeployController.cs` - 27
- `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs` - 24
- `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs` - 21
- `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs` - 19
- `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` - 18
- `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` - 15

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs:145` | Defense Points: 50 / 50 |  |  | `village.arena_defense_palette.defense_points_50_50` `[rename]` |
| `Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs:151` | Done |  |  | `village.arena_defense_palette.done` |
| `Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs:194` | No defenders registered. |  |  | `village.arena_defense_palette.no_defenders_registered` |
| `Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs:238` |  pts |  |  | `village.arena_defense_palette.pts` |
| `Assets/_Modules/Village/Arena/ArenaPaletteVM.cs:69` | Arena DefenseRecruit Squad | frag |  | `village.arena_palette.arena_defense` |
| `Assets/_Modules/Village/Arena/ArenaPanel.cs:253` | USE MY CASTLE |  |  | `village.arena.use_my_castle` |
| `Assets/_Modules/Village/Arena/BattleArenaHud.cs:68` | Battle! |  |  | `village.battle_arena.battle` |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:157` | Build Collections |  |  | `village.build_collection_browser.build_collections` |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:517` | Manage Placed |  |  | `village.build_collection_browser.manage_placed` |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:677` | Catalog definition unavailable. |  |  | `village.build_collection_browser.catalog_definition_unavailable` |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:693` | COST:  |  |  | `village.build_collection_browser.cost` |
| `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:1040` | null |  |  | `village.build_collection_browser.null` |
| `Assets/_Modules/Village/BuildMode/BuildFeedbackToast.cs:98` | Can't build there |  |  | `village.build_feedback_toast.can_t_build_there` `[rename]` |
| `Assets/_Modules/Village/BuildMode/BuildHudController.cs:469` | Done |  |  | `village.build_hud.done` |
| `Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs:1711` | Crystals:  |  |  | `village.build_palette.crystals` |
| `Assets/_Modules/Village/BuildMode/BuildPaletteVM.cs:171` | Build |  | `hud.nav.build` | `village.build_palette.build` |
| `Assets/_Modules/Village/BuildMode/BuildPlaceButton.cs:85` | Rotate Left |  |  | `village.build_place_button.rotate_left` |
| `Assets/_Modules/Village/BuildMode/BuildPlaceButton.cs:89` | Rotate Right |  |  | `village.build_place_button.rotate_right` |
| `Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs:158` | DRAG TO ROTATE - THE ORIENTATION IS REMEMBERED |  |  | `village.build_preview.drag_rotate_orientation_remembered` `[rename]` |
| `Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs:508` | Yaw: {Mathf.Repeat(_currentYaw, 360f):0}Â° |  |  | `village.build_preview.yaw_mathf_repeat_currentyaw_360f` `[rename]` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:89` | {baseName}  (Lv {lvl}/{max}) |  |  | `village.build_selection.basename_lv_lvl_max` `[rename]` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:92` | Sell ( |  |  | `village.build_selection.sell` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:103` | Max Tier |  |  | `village.build_selection.max_tier` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:108` | Upgrade ( |  |  | `village.build_selection.upgrade` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:170` | Move |  |  | `village.build_selection.move` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:178` | Upgrade |  | `ownedTown.upgrade` | `village.build_selection.upgrade` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:189` | Sell |  | `ownedTown.sell` | `village.build_selection.sell` |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs:197` | Cancel |  | `armyScreen.cancel` | `village.build_selection.cancel` |
| `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:160` | Structure |  |  | `village.build_structure_info.structure` |
| `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:168` | Lv 1 |  |  | `village.build_structure_info.lv_1` |
| `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:235` | Upgrade to Lv 2 |  |  | `village.build_structure_info.upgrade_lv_2` |
| `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:322` | Cost: FREECost: Free | frag |  | `village.build_structure_info.cost_free` |
| `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs:392` | Cost:Cost: Free | frag |  | `village.build_structure_info.cost_free` |
| `Assets/_Modules/Village/BuildMode/FirstBuyDoorModel.cs:214` |  - raise it now for Short  | frag |  | `village.first_buy_door_model.raise_now` |
| `Assets/_Modules/Village/BuildMode/FirstBuyDoorModel.cs:318` | Wood |  | `tooltip.resourceWood.title` | `village.first_buy_door_model.wood` |
| `Assets/_Modules/Village/BuildMode/FirstBuyDoorModel.cs:319` | Iron |  | `tooltip.resourceIron.title` | `village.first_buy_door_model.iron` |
| `Assets/_Modules/Village/BuildMode/FirstBuyDoorModel.cs:320` | Stone |  | `tooltip.resourceStone.title` | `village.first_buy_door_model.stone` |
| `Assets/_Modules/Village/BuildMode/FirstBuyDoorModel.cs:321` | Crystals |  | `tooltip.resourceCrystals.title` | `village.first_buy_door_model.crystals` |
| `Assets/_Modules/Village/BuildMode/ObsidianQueueHud.cs:264` | {label}   {active.Count}/{slots} busy |  |  | `village.obsidian_queue.label_active_count_slots_busy` `[rename]` |
| `Assets/_Modules/Village/BuildMode/ObsidianQueueHud.cs:294` | +queue |  |  | `village.obsidian_queue.queue` |
| `Assets/_Modules/Village/BuildMode/ObsidianQueueHud.cs:348` | Ad |  |  | `village.obsidian_queue.ad` |
| `Assets/_Modules/Village/BuildMode/ObsidianQueueVM.cs:136` | Queues |  |  | `village.obsidian_queue.queues` |
| `Assets/_Modules/Village/Buildings/HealingFountain.cs:390` | Healing Caravan |  |  | `village.healing_fountain.healing_caravan` |
| `Assets/_Modules/Village/Buildings/HealingFountain.cs:396` | Current Level: {_currentLevel} / {_maxLevel} |  |  | `village.healing_fountain.current_level_currentlevel_maxlevel` `[rename]` |
| `Assets/_Modules/Village/Buildings/HealingFountain.cs:401` | Heals the Heart {HealRate:0.0} HP/s â€” out of battle only. |  |  | `village.healing_fountain.heals_heart_healrate_0_0` `[rename]` |
| `Assets/_Modules/Village/Buildings/HealingFountain.cs:414` | Upgrade cost: {cost} Coins  (you have {coins}) |  |  | `village.healing_fountain.upgrade_cost_cost_coins_have` `[rename]` |
| `Assets/_Modules/Village/Buildings/HealingFountain.cs:435` | Fully attuned â€” restoring the Heart at full flow. |  |  | `village.healing_fountain.fully_attuned_restoring_heart_full` `[rename]` |
| `Assets/_Modules/Village/Buildings/HealingFountain.cs:442` | X  Close |  |  | `village.healing_fountain.x_close` |
| `Assets/_Modules/Village/Buildings/MobileInteractButton.cs:144` | Interact |  |  | `village.mobile_interact_button.interact` |
| `Assets/_Modules/Village/Buildings/MobileInteractButton.cs:361` | Interact |  |  | `village.mobile_interact_button.interact` |
| `Assets/_Modules/Village/Buildings/NPCUpgradeStation.cs:180` | Upgrade |  | `ownedTown.upgrade` | `village.n_p_c_upgrade_station.upgrade` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:576` |  EnhancementsBuilding | frag |  | `village.building_upgrade_panel.enhancements` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:947` | This building has no enhancement path yet. |  |  | `village.building_upgrade_panel.building_has_no_enhancement_path` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:971` |  of  |  |  | `village.building_upgrade_panel.of` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1085` | Fully enhanced |  |  | `village.building_upgrade_panel.fully_enhanced` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1090` |  has reached  |  |  | `village.building_upgrade_panel.has_reached` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1136` | No listed bonuses for this tier. |  |  | `village.building_upgrade_panel.no_listed_bonuses_tier` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1211` | UPGRADE COST: free |  |  | `village.building_upgrade_panel.upgrade_cost_free` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1226` | UPGRADE COST |  |  | `village.building_upgrade_panel.upgrade_cost` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1302` | Upgrade |  | `ownedTown.upgrade` | `village.building_upgrade_panel.upgrade` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:1613` | <open>button ' | frag |  | `village.building_upgrade_panel.button` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:753` | Build queues are not running right now. |  |  | `village.building_upgrade.build_queues_not_running_right` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:850` | Every enhancement here is already unlocked. |  |  | `village.building_upgrade.every_enhancement_here_already_unlocked` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:865` | Requires Heart Level  |  |  | `village.building_upgrade.requires_heart_level` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:877` | Under construction â€”  |  |  | `village.building_upgrade.under_construction` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:922` |  unlocked.Tier  | frag |  | `village.building_upgrade.unlocked` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:928` | You can't afford that yet. |  |  | `village.building_upgrade.can_t_afford_yet` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:969` |  unlocked.Level  | frag |  | `village.building_upgrade.unlocked` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:973` | You can't afford that yet. |  |  | `village.building_upgrade.can_t_afford_yet` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:974` | Every enhancement here is already unlocked. |  |  | `village.building_upgrade.every_enhancement_here_already_unlocked` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:975` | That enhancement needs Magic to unlock. |  |  | `village.building_upgrade.enhancement_needs_magic_unlock` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:979` | Under construction â€” finish the current work first. |  |  | `village.building_upgrade.under_construction_finish_current_work` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:993` | Nothing to unlock here. |  |  | `village.building_upgrade.nothing_unlock_here` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1006` | Nothing to unlock here. |  |  | `village.building_upgrade.nothing_unlock_here` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1014` | Nothing to unlock here. |  |  | `village.building_upgrade.nothing_unlock_here` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1031` | The Heart is already at its highest level. |  |  | `village.building_upgrade.heart_already_its_highest_level` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1039` | Need  |  |  | `village.building_upgrade.need` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1050` |  â€” higher enhancements unlocked.Heart Level raised to  | frag |  | `village.building_upgrade.higher_enhancements_unlocked` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1054` | Couldn't raise the Heart right now. |  |  | `village.building_upgrade.couldn_t_raise_heart_right` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1070` | Research started - check the Research queue. |  |  | `village.building_upgrade.research_started_check_research_queue` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1076` | Can't unlock that perk yet. |  |  | `village.building_upgrade.can_t_unlock_perk_yet` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1083` | Tap the gold tile to unlock the next enhancement. |  |  | `village.building_upgrade.tap_gold_tile_unlock_next` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1230` |  TO TIER  |  |  | `village.building_upgrade.tier` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1355` |  TO LEVEL  |  |  | `village.building_upgrade.level` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1431` |  TO LEVEL  |  |  | `village.building_upgrade.level` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1455` | This building has no enhancements. |  |  | `village.building_upgrade.building_has_no_enhancements` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1488` |  to Tier Raises  | frag |  | `village.building_upgrade.tier` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1552` | Gold |  |  | `village.building_upgrade.gold` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1571` |  to Level Raises  | frag |  | `village.building_upgrade.level` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1592` | Magic |  |  | `village.building_upgrade.magic` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1622` |  to Level Raises  | frag |  | `village.building_upgrade.level` |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs:1690` |  at Level Stronger  | frag |  | `village.building_upgrade.level` |
| `Assets/_Modules/Village/Buildings/Progression/HeartProgression.cs:617` | The Heart is already at its highest level. |  |  | `village.heart_progression.heart_already_its_highest_level` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/HeartProgression.cs:650` | Could not raise the Heart right now. |  |  | `village.heart_progression.could_not_raise_heart_right` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/HeartProgression.cs:658` | Heart raised to Level  |  |  | `village.heart_progression.heart_raised_level` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:175` | That structure does not belong to this town. |  |  | `village.placed_structure_upgrade_service.structure_does_not_belong_town` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:177` | The upgrade service is unavailable. |  |  | `village.placed_structure_upgrade_service.upgrade_service_unavailable` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:184` | Upgrade completed. |  |  | `village.placed_structure_upgrade_service.upgrade_completed` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:191` | That structure could not be identified. |  |  | `village.placed_structure_upgrade_service.structure_could_not_identified` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:202` | That structure has no catalog entry. |  |  | `village.placed_structure_upgrade_service.structure_has_no_catalog_entry` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:221` | Max level reached. |  |  | `village.placed_structure_upgrade_service.max_level_reached` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:247` | s).Under construction ( | frag |  | `village.placed_structure_upgrade_service.under_construction` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:279` | Upgrade failed - see the log. |  |  | `village.placed_structure_upgrade_service.upgrade_failed_see_log` `[rename]` |
| `Assets/_Modules/Village/Buildings/Progression/PlacedStructureUpgradeService.cs:312` | Upgraded to level  |  |  | `village.placed_structure_upgrade_service.upgraded_level` |
| `Assets/_Modules/Village/Buildings/Progression/StructureTapUpgradeController.cs:476` | scene-built body carries no grid cell to compose a placed job key from |  |  | `village.structure_tap_upgrade.scene_built_body_carries_no` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:235` | Build |  | `hud.nav.build` | `village.build_menu.build` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:280` | Crystals:  |  |  | `village.build_menu.crystals` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:345` | Restoring the nearest damaged wall section. |  |  | `village.build_menu.restoring_nearest_damaged_wall_section` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:360` | < Back |  |  | `village.build_menu.back` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:509` | No tower selected. |  |  | `village.build_menu.no_tower_selected` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:515` |  for the  |  |  | `village.build_menu.for_the` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:526` | Tower definition asset missing for the  |  |  | `village.build_menu.tower_definition_asset_missing` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:533` | The build could not be charged - placement blocked. |  |  | `village.build_menu.build_could_not_charged_placement` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:558` | Click a clear tile to raise the  |  |  | `village.build_menu.click_clear_tile_raise` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:753` | Pick an action, then tap a clear tile to raise a tower. |  |  | `village.build_menu.pick_action_then_tap_clear` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/BuildMenuVM.cs:184` | Build |  | `hud.nav.build` | `village.build_menu.build` |
| `Assets/_Modules/Village/Buildings/UI/LevelUpSkillPopup.cs:129` | Level {newLevel}!  Spend a skill point |  |  | `village.level_up_skill.level_newlevel_spend_skill_point` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/LevelUpSkillPopup.cs:261` | Later |  |  | `village.level_up_skill.later` |
| `Assets/_Modules/Village/Buildings/UI/LevelUpSkillPopup.cs:278` | levelup-pillSkill points â€” Spend | frag |  | `village.level_up_skill.skill_points_spend` |
| `Assets/_Modules/Village/Buildings/UI/LevelUpVM.cs:124` | Level Up! |  |  | `village.level_up.level_up` |
| `Assets/_Modules/Village/Buildings/UI/LevelUpVM.cs:144` |   (Lv  |  |  | `village.level_up.lv` |
| `Assets/_Modules/Village/Buildings/UI/PlacedTowerListVM.cs:144` | Towers |  |  | `village.placed_tower_list.towers` |
| `Assets/_Modules/Village/Buildings/UI/TowerEmpowerButton.cs:123` | {emp.crystalCost} Crystals |  |  | `village.tower_empower_button.emp_crystalcost_crystals` |
| `Assets/_Modules/Village/Buildings/UI/TowerEmpowerButton.cs:134` | Need {emp.crystalCost} Crystals   |  |  | `village.tower_empower_button.need_emp_crystalcost_crystals` `[rename]` |
| `Assets/_Modules/Village/Buildings/UI/TowerManagerPanel.cs:150` | Towers |  |  | `village.tower_manager.towers` |
| `Assets/_Modules/Village/Buildings/UI/TowerManagerPanel.cs:443` | Select a tower to manage. |  |  | `village.tower_manager.select_tower_manage` |
| `Assets/_Modules/Village/Buildings/UI/TowerUpgradeButton.cs:56` | tower-upgrade-button |  |  | `village.tower_upgrade_button.tower_upgrade_button` |
| `Assets/_Modules/Village/Catalog/BuildCategoryRegistry.cs:58` | Build |  | `hud.nav.build` | `village.build_category_registry.build` |
| `Assets/_Modules/Village/Catalog/BuildCategoryRegistry.cs:223` | Build |  | `hud.nav.build` | `village.build_category_registry.build` |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:120` | You are risking:  |  |  | `village.jewel_polish_confirm.risking` |
| `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:125` | It could become any of these: |  |  | `village.jewel_polish_confirm.could_become_any_these` `[rename]` |
| `Assets/_Modules/Village/Crafting/JewelPolishService.cs:461` | The stone shatters |  |  | `village.jewel_polish_service.stone_shatters` |
| `Assets/_Modules/Village/Crafting/VillageCraftingPanel.cs:117` | Crafting |  |  | `village.village_crafting.crafting` |
| `Assets/_Modules/Village/Crafting/VillageCraftingPanel.cs:235` | Craft |  |  | `village.village_crafting.craft` |
| `Assets/_Modules/Village/Crafting/WorkshopCraftVM.cs:95` | Crafting |  |  | `village.workshop_craft.crafting` |
| `Assets/_Modules/Village/Dev/ResourceDevTool.cs:271` | grant {currency} FAILED: {e.Message} |  |  | `village.resource_dev_tool.grant_currency_failed_e_message` `[rename]` |
| `Assets/_Modules/Village/Enemies/EnemyRenderDiagnostic.cs:121` |  renderers= |  |  | `village.enemy_render_diagnostic.renderers` |
| `Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs:1250` | hero is DOWN - a body has no combat inputs for PursuitActive to serve |  |  | `village.overworld_encounter_spawner.hero_down_body_has_no` `[rename]` |
| `Assets/_Modules/Village/Harvest/EchoRepairProgressBillboard.cs:26` |  repairingEcho | frag |  | `village.echo_repair_progress_billboard.repairing` |
| `Assets/_Modules/Village/Harvest/EchoRosterVM.cs:54` | ECHOES OF ELARION |  |  | `village.echo_roster.echoes_elarion` |
| `Assets/_Modules/Village/Harvest/EchoRosterView.cs:204` | ECHOES OF ELARION |  |  | `village.echo_roster.echoes_elarion` |
| `Assets/_Modules/Village/Harvest/EchoRosterView.cs:343` |  has answered your call. |  |  | `village.echo_roster.has_answered_call` |
| `Assets/_Modules/Village/Harvest/EchoRosterView.cs:371` | The Tree sleeps. |  |  | `village.echo_roster.tree_sleeps` |
| `Assets/_Modules/Village/Harvest/EchoUnlockDialogue.cs:404` |  joined your Echoes. |  |  | `village.echo_unlock_dialogue.joined_echoes` |
| `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs:371` | Echoes {svc.EchoCount}/{svc.MaxEchoes} |  |  | `village.echo_unlock_feedback.echoes_svc_echocount_svc_maxechoes` `[rename]` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:132` | Echoes  1/4 |  |  | `village.echo_workforce.echoes_1_4` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:143` | Silo  0% |  |  | `village.echo_workforce.silo_0` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:148` | Collect All |  |  | `village.echo_workforce.collect_all` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:155` | Watch Ad - 2x speed for 1 hour |  |  | `village.echo_workforce.watch_ad_2x_speed_1` `[rename]` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:160` | Optional time boost |  |  | `village.echo_workforce.optional_time_boost` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:186` | +{banked} collected!Nothing to collect | frag |  | `village.echo_workforce.banked_collected` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:195` | Collect All |  |  | `village.echo_workforce.collect_all` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:254` | 2x speed active - {minutes} min remaining |  |  | `village.echo_workforce.2x_speed_active_minutes_min` `[rename]` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceHud.cs:269` | New Echo joined! |  |  | `village.echo_workforce.new_echo_joined` |
| `Assets/_Modules/Village/Harvest/EchoWorkforceVM.cs:91` | ECHO HARVEST |  |  | `village.echo_workforce.echo_harvest` |
| `Assets/_Modules/Village/Harvest/OfflineHarvestService.cs:548` | Job |  |  | `village.offline_harvest_service.job` |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackDoorsVM.cs:153` | FINISHED WHILE AWAY |  |  | `village.welcome_back_doors.finished_while_away` |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackDoorsVM.cs:223` | Job |  |  | `village.welcome_back_doors.job` |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs:273` | WELCOME BACK, KEEPER |  |  | `village.welcome_back.welcome_back_keeper` |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs:325` | AETHER CRYSTALS |  |  | `village.welcome_back.aether_crystals` |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs:487` | ALSO FINISHED |  |  | `village.welcome_back.also_finished` |
| `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs:569` | ALSO WAITING |  |  | `village.welcome_back.also_waiting` |
| `Assets/_Modules/Village/Heart/GameOverScreen.cs:417` | '{title}' in '{_defeatScene}' â€” scaled-time respawn/down-beat coroutines freeze until Retry/sceneLoaded |  |  | `village.game_over.title_defeatscene_scaled_time_respawn` `[rename]` |
| `Assets/_Modules/Village/Heart/HeartHudBridgeBootstrap.cs:50` | Intro |  |  | `village.heart_hud_bridge_bootstrap.intro` |
| `Assets/_Modules/Village/Hero/AttackTimingBonus.cs:162` | CHAIN Ã—{_chain}MAX CHAIN! | frag |  | `village.attack_timing_bonus.chain_chain` |
| `Assets/_Modules/Village/Hero/EquipVM.cs:128` | Equipment |  |  | `village.equip.equipment` |
| `Assets/_Modules/Village/Hero/EquipVM.cs:372` | Select an item first. |  |  | `village.equip.select_item_first` |
| `Assets/_Modules/Village/Hero/EquipVM.cs:381` | Select an item first. |  |  | `village.equip.select_item_first` |
| `Assets/_Modules/Village/Hero/EquipVM.cs:383` | No hero to equip. |  |  | `village.equip.no_hero_equip` |
| `Assets/_Modules/Village/Hero/EquipVM.cs:411` | Equipped. |  |  | `village.equip.equipped` |
| `Assets/_Modules/Village/Hero/EquipVM.cs:459` | No hero. |  |  | `village.equip.no_hero` |
| `Assets/_Modules/Village/Hero/EquipVM.cs:467` | Unequipped. |  |  | `village.equip.unequipped` |
| `Assets/_Modules/Village/Hero/EquipmentController.cs:3982` | gripRoot='{gripRoot.name}' body='{(instantiatedBody != null ? instantiatedBody.name :  |  |  | `village.equipment.griproot_griproot_name_body_instantiatedbody` `[rename]` |
| `Assets/_Modules/Village/Hero/EquipmentController.cs:4083` | (tier={tier}; side={side:+0;-0} * body.right). |  |  | `village.equipment.tier_tier_side_side_0` `[rename]` |
| `Assets/_Modules/Village/Hero/EquipmentController.cs:4867` | (~0 = standing; ~90 = LYING ACROSS THE BODY) bodyUp={body.up.ToString(dotBodyUp={Vector3.Dot(m.WorldUnit, body.up):0.##} dotBodyFwd={Vector3.Dot(m.... | frag |  | `village.equipment.dotbodyfwd_vector3_dot_m_worldunit` `[rename]` |
| `Assets/_Modules/Village/Hero/EquipmentController.cs:5622` | ') - its bounds describe an effect, not the body (WO-1226) |  |  | `village.equipment.its_bounds_describe_effect_not` `[rename]` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:805` | No compatible items owned. |  |  | `village.equipment.no_compatible_items_owned` `[rename]` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:857` | Select an item to compare. |  |  | `village.equipment.select_item_compare` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:891` | COMPARE TO EQUIPPED |  |  | `village.equipment.compare_equipped` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1225` | Change  |  |  | `village.equipment.change` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1508` | Equipped  |  |  | `village.equipment.equipped` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1577` | Loading view... |  |  | `village.equipment.loading_view` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1658` | no hero body resolved for the active target (ResolveBody found  |  |  | `village.equipment.no_hero_body_resolved_active` `[rename]` |
| `Assets/_Modules/Village/Hero/EquipmentPanel.cs:1693` | Live view |  |  | `village.equipment.live_view` |
| `Assets/_Modules/Village/Hero/GearProgression.cs:293` | Lv  |  |  | `village.gear_progression.lv` |
| `Assets/_Modules/Village/Hero/HeroArmorVisual.cs:567` | deferred pose-verify: armored body never assigned a clip (lastClipCount={lastCount}) |  |  | `village.hero_armor_visual.deferred_pose_verify_armored_body` `[rename]` |
| `Assets/_Modules/Village/Hero/HeroBodySwapper.cs:516` | Knight |  |  | `village.hero_body_swapper.knight` |
| `Assets/_Modules/Village/Hero/HeroBodySwapper.cs:707` | <null> |  |  | `village.hero_body_swapper.null` |
| `Assets/_Modules/Village/Hero/HeroBodySwapper.cs:1611` | [{slotIndex}] '{slotName}'â†’{label} |  |  | `village.hero_body_swapper.slotindex_slotname_label` |
| `Assets/_Modules/Village/Hero/HeroBodySwapper.cs:1663` | [{slotIndex}] '{slotName}'â†’{label} |  |  | `village.hero_body_swapper.slotindex_slotname_label` |
| `Assets/_Modules/Village/Hero/HeroBodySwapper.cs:1691` | index#{fi} |  |  | `village.hero_body_swapper.index_fi` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:338` | Select an item first. |  |  | `village.inventory.select_item_first` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:339` | That item cannot be used. |  |  | `village.inventory.item_cannot_used` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:340` | No inventory. |  |  | `village.inventory.no_inventory` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:348` | Nothing happens. |  |  | `village.inventory.nothing_happens` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:358` | Used  |  |  | `village.inventory.used` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:360` | That had no effect. |  |  | `village.inventory.had_no_effect` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:372` | Select an item first. |  |  | `village.inventory.select_item_first` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:373` | No inventory. |  |  | `village.inventory.no_inventory` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:377` | Dropped  |  |  | `village.inventory.dropped` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:379` | Nothing to drop. |  |  | `village.inventory.nothing_drop` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:388` | Select an item first. |  |  | `village.inventory.select_item_first` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:389` | That item cannot be equipped. |  |  | `village.inventory.item_cannot_equipped` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:390` | No hero to equip. |  |  | `village.inventory.no_hero_equip` |
| `Assets/_Modules/Village/Hero/InventoryVM.cs:413` | Equipped  |  |  | `village.inventory.equipped` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:514` | Purchase |  |  | `village.party_shop_panel.purchase` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:531` | Equip |  |  | `village.party_shop_panel.equip` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:551` | Improve |  |  | `village.party_shop_panel.improve` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:635` | All |  |  | `village.party_shop_panel.all` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1230` | [Lv  |  |  | `village.party_shop_panel.lv` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1318` | Select an item to preview. |  |  | `village.party_shop_panel.select_item_preview` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1426` | </b>   <b> | frag |  | `village.party_shop_panel.b` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1763` | SellSell   | frag | `ownedTown.sell` | `village.party_shop_panel.sell` |
| `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1795` | EquipUnequip | frag |  | `village.party_shop_panel.unequip` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:672` | Nothing to do for that item. |  |  | `village.party_shop.nothing_do_item` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:682` | Select an item to equip. |  |  | `village.party_shop.select_item_equip` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:685` | You must buy it before you can equip it. |  |  | `village.party_shop.must_buy_before_can_equip` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:693` | That item can't be equipped. |  |  | `village.party_shop.item_can_t_equipped` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:742` | Select an item to improve. |  |  | `village.party_shop.select_item_improve` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:745` | You must own it before you can improve it. |  |  | `village.party_shop.must_own_before_can_improve` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:749` | That item can't be improved. |  |  | `village.party_shop.item_can_t_improved` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:787` | Select an item to unequip. |  |  | `village.party_shop.select_item_unequip` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:789` | No hero selected. |  |  | `village.party_shop.no_hero_selected` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:794` | That item isn't equipped. |  |  | `village.party_shop.item_isn_t_equipped` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:804` |  from Unequipped  | frag |  | `village.party_shop.unequipped` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1095` |  at the crafting station.Craft  | frag |  | `village.party_shop.crafting_station` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1232` | Economy unavailable. |  |  | `village.party_shop.economy_unavailable` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1235` |  - needs Not enough gold for  | frag |  | `village.party_shop.not_enough_gold` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1240` | Purchased  |  |  | `village.party_shop.purchased` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1247` | Economy unavailable. |  |  | `village.party_shop.economy_unavailable` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1251` |  - needs Not enough gold for  | frag |  | `village.party_shop.not_enough_gold` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1256` | ! Equip it from the Character screen.Purchased  | frag |  | `village.party_shop.equip_from_character_screen` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1297` | No inventory. |  |  | `village.party_shop.no_inventory` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1330` | No inventory. |  |  | `village.party_shop.no_inventory` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1388` | From your pack. |  |  | `village.party_shop.from_pack` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1442` | You don't own that. |  |  | `village.party_shop.don_t_own` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1443` | Couldn't sell that. |  |  | `village.party_shop.couldn_t_sell` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1446` | Sold for + |  |  | `village.party_shop.sold` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1544` | Economy unavailable. |  |  | `village.party_shop.economy_unavailable` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1548` |  - needs Not enough gold for  | frag |  | `village.party_shop.not_enough_gold` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1553` | . Tap again to equip; your current loadout was kept.Bought  | frag |  | `village.party_shop.tap_again_equip_current_loadout` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1560` | Economy unavailable. |  |  | `village.party_shop.economy_unavailable` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1564` |  - needs Not enough gold for  | frag |  | `village.party_shop.not_enough_gold` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1569` | . Tap again to equip; your current loadout was kept.Bought  | frag |  | `village.party_shop.tap_again_equip_current_loadout` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1576` | No hero selected to equip. |  |  | `village.party_shop.no_hero_selected_equip` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1593` |  to Equipped  | frag |  | `village.party_shop.equipped` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1600` | No hero selected to equip. |  |  | `village.party_shop.no_hero_selected_equip` `[rename]` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1613` |  to Equipped  | frag |  | `village.party_shop.equipped` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1620` | You don't own that. |  |  | `village.party_shop.don_t_own` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1621` | Couldn't sell that. |  |  | `village.party_shop.couldn_t_sell` |
| `Assets/_Modules/Village/Hero/PartyShopVM.cs:1624` | Sold for + |  |  | `village.party_shop.sold` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:334` | That raid could not be opened. |  |  | `village.raid_deploy.raid_could_not_opened` `[rename]` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:488` | Clock:  |  |  | `village.raid_deploy.clock` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:509` | YOUR FORCES |  |  | `village.raid_deploy.forces` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:667` | No troops trained yet. Visit the Barracks. |  |  | `village.raid_deploy.no_troops_trained_yet_visit` `[rename]` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:793` | SCOUT REPORT |  |  | `village.raid_deploy.scout_report` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:873` | ENEMY BASE |  |  | `village.raid_deploy.enemy_base` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:942` | Spoils unknown |  |  | `village.raid_deploy.spoils_unknown` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1052` | EDIT ARMY |  |  | `village.raid_deploy.edit_army` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1062` | BEGIN ASSAULT |  |  | `village.raid_deploy.begin_assault` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1076` | TRAIN TROOPS |  |  | `village.raid_deploy.train_troops` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1112` | The Barracks could not be opened. |  |  | `village.raid_deploy.barracks_could_not_opened` `[rename]` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1189` | Raid briefing is not ready. |  |  | `village.raid_deploy.raid_briefing_not_ready` `[rename]` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1195` | This raid has no battleground yet. |  |  | `village.raid_deploy.raid_has_no_battleground_yet` `[rename]` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1215` | No troops trained yet. Visit the Barracks. |  |  | `village.raid_deploy.no_troops_trained_yet_visit` `[rename]` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1255` | Assaulting  |  |  | `village.raid_deploy.assaulting` |
| `Assets/_Modules/Village/Hero/RaidDeployVM.cs:68` | RAID:  |  |  | `village.raid_deploy.raid` |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:701` | No raids available. |  |  | `village.raid_selection.no_raids_available` |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:1171` | That raid is unavailable right now. |  |  | `village.raid_selection.raid_unavailable_right_now` `[rename]` |
| `Assets/_Modules/Village/Hero/RealmMapVM.cs:138` | REALM MAP |  |  | `village.realm_map.realm_map` |
| `Assets/_Modules/Village/Hero/RealmMapVM.cs:162` | The Realm |  |  | `village.realm_map.realm` |
| `Assets/_Modules/Village/Hero/RealmMapVM.cs:387` | Elarion |  |  | `village.realm_map.elarion` |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:578` | Brom's Rumor Board |  |  | `village.rumor_board.brom_s_rumor_board` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:798` | Read the letter > |  |  | `village.rumor_board.read_letter` |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:1198` | The board is quiet. |  |  | `village.rumor_board.board_quiet` |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:1203` | Brom posts more as Elarion wakes. |  |  | `village.rumor_board.brom_posts_more_as_elarion` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:222` | Brom's Rumor Board |  |  | `village.rumor_board.brom_s_rumor_board` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:339` | The talk of Elarion. Accept what calls to you. |  |  | `village.rumor_board.talk_elarion_accept_what_calls` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:556` | Quests aren't ready yet. |  |  | `village.rumor_board.quests_aren_t_ready_yet` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:562` | Claimed:  |  |  | `village.rumor_board.claimed` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:567` |  - your stores may be full. Make room, then claim again.Nothing could be credited for  | frag |  | `village.rumor_board.stores_may_full_make_room` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:580` | Quests aren't ready yet. |  |  | `village.rumor_board.quests_aren_t_ready_yet` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:586` | Opening  |  |  | `village.rumor_board.opening` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:593` |  - the objective is pinned to your HUD.Tracking  | frag |  | `village.rumor_board.objective_pinned_hud` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:604` | Quests aren't ready yet. |  |  | `village.rumor_board.quests_aren_t_ready_yet` `[rename]` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:610` |  first.Not yet: finish  | frag |  | `village.rumor_board.not_yet_finish` |
| `Assets/_Modules/Village/Hero/RumorBoardVM.cs:616` | Accepted:  |  |  | `village.rumor_board.accepted` |
| `Assets/_Modules/Village/Hero/SmartMobileCamera.cs:1359` | Enemy |  |  | `village.smart_mobile_camera.enemy` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:138` | Training: idle |  |  | `village.troop_training.training_idle` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:169` | Armies |  |  | `village.troop_training.armies` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:485` | Train |  | `armyScreen.trainButton` | `village.troop_training.train` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:492` | Train x5 |  |  | `village.troop_training.train_x5` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:566` | Training: (queue offline) |  |  | `village.troop_training.training_queue_offline` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:574` | Training: idle |  |  | `village.troop_training.training_idle` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:584` | Training:  |  |  | `village.troop_training.training` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:600` |  unlocks at Barracks Tier  |  |  | `village.troop_training.unlocks_barracks_tier` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:606` |  for training.Queued  | frag |  | `village.troop_training.training` |
| `Assets/_Modules/Village/Hero/TroopTrainingPanel.cs:611` | Trained  |  |  | `village.troop_training.trained` |
| `Assets/_Modules/Village/Hero/TroopTrainingVM.cs:197` | Barracks - Train |  |  | `village.troop_training.barracks_train` |
| `Assets/_Modules/Village/Items/CraftingVM.cs:107` | Alchemy |  |  | `village.crafting.alchemy` |
| `Assets/_Modules/Village/Items/JewelerPanelMvvm.cs:315` | Jewelry |  |  | `village.jeweler_panel.jewelry` |
| `Assets/_Modules/Village/Items/JewelerVM.cs:56` | Set the â€¦ |  |  | `village.jeweler.set` |
| `Assets/_Modules/Village/Monetization/Providers/Pi/PiAdGrantDecision.cs:65` | (no message) |  |  | `village.pi_ad_grant_decision.no_message` |
| `Assets/_Modules/Village/Monetization/Providers/Pi/PiAdProvider.cs:195` | Pi.init did not succeed (env= |  |  | `village.pi_ad_provider.pi_init_did_not_succeed` `[rename]` |
| `Assets/_Modules/Village/NPCs/CastleVendorNpcInjector.cs:864` | Forge |  |  | `village.castle_vendor_npc_injector.forge` |
| `Assets/_Modules/Village/NPCs/CastleVendorNpcInjector.cs:951` | GENERIC merchant fallback -> Resources/{BodyMerchant} (own body Resources/{v.BodyRes} missing) |  |  | `village.castle_vendor_npc_injector.generic_merchant_fallback_resources_bodymerc` `[rename]` |
| `Assets/_Modules/Village/NPCs/GearOfferChoiceUI.cs:120` | Visit |  |  | `village.gear_offer_choice.visit` |
| `Assets/_Modules/Village/Progression/BattlePlans.cs:510` | Bastion PlansEnemy Battle Plans | frag |  | `village.battle_plans.enemy_battle_plans` |
| `Assets/_Modules/Village/Progression/BattlePlans.cs:511` |  - the camp is drawn, the walls are drawn, the watch is drawn.  |  |  | `village.battle_plans.camp_drawn_walls_drawn_watch` `[rename]` |
| `Assets/_Modules/Village/Progression/BattlePlans.cs:512` | Their stronghold |  |  | `village.battle_plans.their_stronghold` |
| `Assets/_Modules/Village/Progression/BattlePlans.cs:513` |  is exposed. |  |  | `village.battle_plans.exposed` |
| `Assets/_Modules/Village/Progression/BattlePlansReveal.cs:388` | Skip |  |  | `village.battle_plans_reveal.skip` |
| `Assets/_Modules/Village/Progression/SpirePlansCelebration.cs:445` | Skip |  |  | `village.spire_plans_celebration.skip` |
| `Assets/_Modules/Village/Progression/SpirePlansCelebration.cs:454` | LET ECHOES REPAIR - FREE |  |  | `village.spire_plans_celebration.let_echoes_repair_free` `[rename]` |
| `Assets/_Modules/Village/Progression/TierSystem.cs:213` | TIER UP!  {milestone.Title} |  |  | `village.tier_system.tier_up_milestone_title` `[rename]` |
| `Assets/_Modules/Village/Siege/RoamingHordeNotifications.cs:124` | Lookout report |  |  | `village.roaming_horde_notifications.lookout_report` |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:140` | AssignedAddedSlot cleared | frag |  | `village.hero_loadout_panel.slot_cleared` |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:144` | Can'tNo hero | frag |  | `village.hero_loadout_panel.no_hero` |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:222` | tap: cleartap: replace | frag |  | `village.hero_loadout_panel.tap_replace` |
| `Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs:401` | Unlocked Skills |  |  | `village.hero_loadout_panel.unlocked_skills` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:127` | Tap a skill, then a hot-swap slot to assign. |  |  | `village.hero_loadout.tap_skill_then_hot_swap` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:148` | Can't change skills during battle. |  |  | `village.hero_loadout.can_t_change_skills_during` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:150` | Pick a skill first, then tap a slot.Slot cleared. | frag |  | `village.hero_loadout.pick_skill_first_then_tap` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:162` | Pick a skill first. |  |  | `village.hero_loadout.pick_skill_first` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:163` | No hero to equip. |  |  | `village.hero_loadout.no_hero_equip` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:164` | Can't change skills during battle. |  |  | `village.hero_loadout.can_t_change_skills_during` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:169` | Assigned to hot-swap slot  |  |  | `village.hero_loadout.assigned_hot_swap_slot` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:175` | That skill is already on the bar. |  |  | `village.hero_loadout.skill_already_bar` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:189` | Pick a skill first. |  |  | `village.hero_loadout.pick_skill_first` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:190` | No hero to equip. |  |  | `village.hero_loadout.no_hero_equip` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:191` | Can't change skills during battle. |  |  | `village.hero_loadout.can_t_change_skills_during` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:194` | Added to your hot-swap bar.No free slot (or already on the bar). | frag |  | `village.hero_loadout.no_free_slot_already_bar` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:203` | Can't change skills during battle. |  |  | `village.hero_loadout.can_t_change_skills_during` `[rename]` |
| `Assets/_Modules/Village/Talents/HeroLoadoutVM.cs:205` | Slot cleared.That slot is already empty. | frag |  | `village.hero_loadout.slot_already_empty` |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:760` | <color=#E5B93F><b> |  |  | `village.hero_skill_tree_panel.color_e5b93f_b` |
| `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs:956` | No talents to show yet. |  |  | `village.hero_skill_tree_panel.no_talents_show_yet` `[rename]` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:192` | Status |  |  | `village.army_muster.status` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:345` | The Barracks is not built yet. |  |  | `village.army_muster.barracks_not_built_yet` `[rename]` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:388` | StatusSummary | frag |  | `village.army_muster.summary` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:724` | No troops unlocked yet - upgrade the Barracks. |  |  | `village.army_muster.no_troops_unlocked_yet_upgrade` `[rename]` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1007` | No troops unlocked yet - upgrade the Barracks. |  |  | `village.army_muster.no_troops_unlocked_yet_upgrade` `[rename]` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1167` | \nThis saved army is empty.\n |  |  | `village.army_muster.saved_army_empty` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1168` | Tap Raid / Hold / Siege for a quick fill,\n |  |  | `village.army_muster.tap_raid_hold_siege_quick` `[rename]` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1169` | or [+] troops on the left.\n |  |  | `village.army_muster.troops_left` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1170` | Then Save and Train Army.\n |  |  | `village.army_muster.then_save_train_army` `[rename]` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1180` | \nCost:  |  |  | `village.army_muster.cost` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1181` | Time:  |  |  | `village.army_muster.time` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1186` |  of \nTrain queue:  | frag |  | `village.army_muster.train_queue` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1193` |  of Starts now:  | frag |  | `village.army_muster.starts_now` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1198` | \nLAST TRAINING ORDER\n |  |  | `village.army_muster.last_training_order` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1203` | \nTip: Training auto-saves this slot. Fill the army, then Raids. |  |  | `village.army_muster.tip_training_auto_saves_slot` `[rename]` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1298` | Name:  |  |  | `village.army_muster.name` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1301` | Save slot  |  |  | `village.army_muster.save_slot` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1304` | Train Army |  |  | `village.army_muster.train_army` |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:1406` | Train Army |  |  | `village.army_muster.train_army` |
| `Assets/_Modules/Village/Troops/ArmyMusterVM.cs:803` | Reloaded  |  |  | `village.army_muster.reloaded` |
| `Assets/_Modules/Village/Troops/ArmyMusterVM.cs:810` | Editing  |  |  | `village.army_muster.editing` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:678` | Time! The assault is called off - your warband retreats. |  |  | `village.raid_deploy.time_assault_called_off_warband` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1008` | Can't deploy there - tap open ground inside the base. |  |  | `village.raid_deploy.can_t_deploy_there_tap` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1019` | No army to deploy. |  |  | `village.raid_deploy.no_army_deploy` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1025` | No more {DisplayName(_armedDefId)} ready to deploy. |  |  | `village.raid_deploy.no_more_displayname_armeddefid_ready` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1040` | Couldn't deploy {DisplayName(_armedDefId)}. |  |  | `village.raid_deploy.couldn_t_deploy_displayname_armeddefid` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1139` | Breach: tap a wall section to order the assault. |  |  | `village.raid_deploy.breach_tap_wall_section_order` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1161` | Breach: that is not a wall - tap a wall section. |  |  | `village.raid_deploy.breach_not_wall_tap_wall` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1187` | Breach: that section is already down. |  |  | `village.raid_deploy.breach_section_already_down` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1201` | Breach: that wall is not the enemy's. |  |  | `village.raid_deploy.breach_wall_not_enemy_s` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1211` | Breach ordered - the warband hits that section. |  |  | `village.raid_deploy.breach_ordered_warband_hits_section` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1441` | Breach: tap a wall section to order the assault. |  |  | `village.raid_deploy.breach_tap_wall_section_order` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1447` | Breach order dropped - the warband picks the weakest wall again. |  |  | `village.raid_deploy.breach_order_dropped_warband_picks` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1482` | BreachBreach ON | frag |  | `village.raid_deploy.breach` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1499` | Rally set â€” idle troops will muster there. |  |  | `village.raid_deploy.rally_set_idle_troops_will` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1656` | Retreat? Tap again to confirm â€” the fallen are lost, and some survivors  |  |  | `village.raid_deploy.retreat_tap_again_confirm_fallen` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:1661` | Confirm Retreat |  |  | `village.raid_deploy.confirm_retreat` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:2779` | Deploy All |  |  | `village.raid_deploy.deploy_all` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:2784` | Breach |  |  | `village.raid_deploy.breach` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:2789` | Rally |  |  | `village.raid_deploy.rally` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:2793` | Retreat |  |  | `village.raid_deploy.retreat` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:2883` | [wo1646-label] empty-tray sentence |  |  | `village.raid_deploy.wo1646_label_empty_tray_sentence` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:2934` |  label= |  |  | `village.raid_deploy.label` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3014` | No troops to deploy - train at the Barracks first. |  |  | `village.raid_deploy.no_troops_deploy_train_barracks` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3123` | Deploy All unavailable - no ready army or hero. |  |  | `village.raid_deploy.deploy_all_unavailable_no_ready` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3133` | Deploy All needs open ground ahead. |  |  | `village.raid_deploy.deploy_all_needs_open_ground` `[rename]` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3153` |  troops in assault formation.Deployed  | frag |  | `village.raid_deploy.troops_assault_formation` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs:3233` | RallyRally ON | frag |  | `village.raid_deploy.rally` |
| `Assets/_Modules/Village/Troops/RaidHudController.cs:705` | CLEAR THE BASE |  |  | `village.raid_hud.clear_base` |
| `Assets/_Modules/Village/Troops/RaidHudController.cs:710` | SPIRE DOWN |  |  | `village.raid_hud.spire_down` |
| `Assets/_Modules/Village/Troops/RaidHudController.cs:724` | Razed  |  |  | `village.raid_hud.razed` |
| `Assets/_Modules/Village/Troops/RaidHudController.cs:728` | Troops  |  |  | `village.raid_hud.troops` |
| `Assets/_Modules/Village/Troops/StarterArmyGrant.cs:167` | FootmanFootmen | frag |  | `village.starter_army_grant.footman` |
| `Assets/_Modules/Village/Troops/StarterArmyGrant.cs:170` | FootmanFootmen | frag |  | `village.starter_army_grant.footman` |
| `Assets/_Modules/Village/Troops/StarterArmyGrant.cs:172` | ArcherArchers | frag |  | `village.starter_army_grant.archers` |
| `Assets/_Modules/Village/Troops/StarterArmyGrant.cs:173` | , free. Open Journey, then Raids.Your first squad is ready -  | frag |  | `village.starter_army_grant.free_open_journey_then_raids` `[rename]` |
| `Assets/_Modules/Village/Troops/TroopController.cs:1357` | routeUnit=[status={_routeStatus} len={routeLenTok} straight={routeStraightTok}  |  |  | `village.troop.routeunit_status_routestatus_len_routelentok` `[rename]` |
| `Assets/_Modules/Village/Troops/TroopController.cs:1478` | -arrived |  |  | `village.troop.arrived` |
| `Assets/_Modules/Village/Troops/TroopController.cs:1870` | CalculatePath-FAILED |  |  | `village.troop.calculatepath_failed` |
| `Assets/_Modules/Village/Troops/TroopDialogueCommands.cs:49` | The Barracks is not built yet. |  |  | `village.troop_dialogue_commands.barracks_not_built_yet` `[rename]` |
| `Assets/_Modules/Village/Troops/TroopGearApplier.cs:217` |  âš  DEGENERATE (arm lies along the body's left/right axis, so GetShieldAxes'  |  |  | `village.troop_gear_applier.degenerate_arm_lies_along_body` `[rename]` |
| `Assets/_Modules/Village/Tutorial/TutorialHudOverlay.cs:71` | {Mathf.Clamp(current, 0, max)} / {max} |  |  | `village.tutorial_hud_overlay.mathf_clamp_current_0_max` `[rename]` |
| `Assets/_Modules/Village/Tutorial/TutorialHudOverlay.cs:206` | tutorial-hint |  |  | `village.tutorial_hud_overlay.tutorial_hint` |
| `Assets/_Modules/Village/UI/Defense/DefenseReportPanel.cs:325` | Unknown force |  |  | `village.defense_report.unknown_force` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:280` | Victory! |  |  | `village.end_state.victory` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:300` | Experience |  |  | `village.end_state.experience` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:308` | Gold |  |  | `village.end_state.gold` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:335` | Iron |  | `tooltip.resourceIron.title` | `village.end_state.iron` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:381` | Defeat |  |  | `village.end_state.defeat` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:402` | YOU HAVE FALLEN |  |  | `village.end_state.have_fallen` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:632` | % razed. |  |  | `village.end_state.razed` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:642` |  troop lost troops lost | frag |  | `village.end_state.troops_lost` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:648` | Victory! |  |  | `village.end_state.victory` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:807` | % razed. |  |  | `village.end_state.razed` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:833` | \nEvery troop came home. |  |  | `village.end_state.every_troop_came_home` `[rename]` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:852` | \nNo spoils - the warband was lost. |  |  | `village.end_state.no_spoils_warband_was_lost` `[rename]` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:972` | Outpost Claimed |  |  | `village.end_state.outpost_claimed` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:1018` | Wave {waveNumber} Cleared |  |  | `village.end_state.wave_wavenumber_cleared` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:1093` | {e.Name} - {state} |  |  | `village.end_state.e_name_state` |
| `Assets/_Modules/Village/UI/EndState/EndStateView.cs:90` | EndState ' |  |  | `village.end_state.endstate` |
| `Assets/_Modules/Village/UI/EndState/EndStateView.cs:96` | EndState ' |  |  | `village.end_state.endstate` |
| `Assets/_Modules/Village/UI/EndState/EndStateView.cs:98` | EndState 'EndStateView.Show (replaced by ' | frag |  | `village.end_state.endstateview_show_replaced_by` `[rename]` |
| `Assets/_Modules/Village/UI/EndState/EndStateView.cs:107` | EndStateView.Show - REPLACED by a new end-state '{vm.Title}' |  |  | `village.end_state.endstateview_show_replaced_by_new` `[rename]` |
| `Assets/_Modules/Village/UI/EndState/EndStateView.cs:1857` | Time   |  |  | `village.end_state.time` |
| `Assets/_Modules/Village/UI/EndState/EndStateView.cs:2825` | END-STATE CONTINUE: '{_vm.Title}' primary fired -> action={_vm.PrimaryRoute} (screen tearing down) |  |  | `village.end_state.end_state_continue_vm_title` `[rename]` |
| `Assets/_Modules/Village/UI/EndState/EndStateView.cs:3088` | EndState ' |  |  | `village.end_state.endstate` |
| `Assets/_Modules/Village/UI/Guide/GuideVM.cs:95` | live |  |  | `village.guide.live` |
| `Assets/_Modules/Village/UI/Manage/HeartPanel.cs:293` | HEART OF ELARION |  | `hud.heart.title` | `village.heart.heart_elarion` |
| `Assets/_Modules/Village/UI/Manage/HeartPanel.cs:359` |  . MAXHEART LEVEL  | frag |  | `village.heart.heart_level` |
| `Assets/_Modules/Village/UI/Manage/HeartPanel.cs:404` | Build: Research:  | frag |  | `village.heart.research` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:2491` | BUILD BARRACKS |  |  | `village.manage_screen.build_barracks` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:3358` | WORK TIMELINE |  |  | `village.manage_screen.work_timeline` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:3780` | Place a structure to unlock Manage categories |  |  | `village.manage_screen.place_structure_unlock_manage_categories` `[rename]` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5294` | BUILDING NOW |  |  | `village.manage_screen.building_now` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5299` |  more |  | `armyScreen.manage` | `village.manage_screen.more` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5304` | OPEN QUEUE |  |  | `village.manage_screen.open_queue` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5312` | No builder at work |  |  | `village.manage_screen.no_builder_work` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5703` | LOCKED . TIER  |  |  | `village.manage_screen.locked_tier` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5841` | TRAINING NOW |  |  | `village.manage_screen.training_now` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5844` | OPEN QUEUE |  |  | `village.manage_screen.open_queue` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5856` | Nothing training. Tap TRAIN to start. |  |  | `village.manage_screen.nothing_training_tap_train_start` `[rename]` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6623` | RESEARCHING NOW |  |  | `village.manage_screen.researching_now` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6628` |  more |  | `armyScreen.manage` | `village.manage_screen.more` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6633` | OPEN QUEUE |  |  | `village.manage_screen.open_queue` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6641` | No research under way |  |  | `village.manage_screen.no_research_under_way` `[rename]` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:7249` | CollapseExpand x | frag |  | `village.manage_screen.collapse` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:7333` | Ad |  |  | `village.manage_screen.ad` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:7418` | Move up |  |  | `village.manage_screen.move_up` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:978` |  - Level  |  |  | `village.manage_screen.level` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:1030` |  - Level  -> L | frag |  | `village.manage_screen.level` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:1594` | A village structure. |  |  | `village.manage_screen.village_structure` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:1846` | A village structure. |  |  | `village.manage_screen.village_structure` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2684` | Armies - saved compositions |  |  | `village.manage_screen.armies_saved_compositions` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:5767` | Next level |  |  | `village.manage_screen.next_level` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:5883` | Placed |  |  | `village.manage_screen.placed` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:5890` | Next level |  |  | `village.manage_screen.next_level` |
| `Assets/_Modules/Village/UI/RotateModelMenu.cs:191` | Rotation: {deg}Â° |  |  | `village.rotate_model_menu.rotation_deg` |
| `Assets/_Modules/Village/UI/RotateModelMenu.cs:259` | Rotate Model |  |  | `village.rotate_model_menu.rotate_model` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:239` | SEATING EDITOR |  |  | `village.seating_editor_overlay.seating_editor` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:246` | Orient the equipped gear live â€” dial from 100% vertical. |  |  | `village.seating_editor_overlay.orient_equipped_gear_live_dial` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:337` | Dial the pose, then Save. |  |  | `village.seating_editor_overlay.dial_pose_then_save` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:389` | Save Offset |  |  | `village.seating_editor_overlay.save_offset` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:398` | Done |  |  | `village.seating_editor_overlay.done` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:461` | No off-hand equipped. |  |  | `village.seating_editor_overlay.no_off_hand_equipped` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:462` | No main weapon equipped. |  |  | `village.seating_editor_overlay.no_main_weapon_equipped` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:463` | Could not switch target. |  |  | `village.seating_editor_overlay.could_not_switch_target` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:482` | Could not switch carry mode. |  |  | `village.seating_editor_overlay.could_not_switch_carry_mode` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:501` | Mode: ABSOLUTE back poseMode: NUDGE on built-in | frag |  | `village.seating_editor_overlay.mode_absolute_back_pose` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:516` | Mode: VERTICAL + delta (off-hand locked) |  |  | `village.seating_editor_overlay.mode_vertical_delta_off_hand` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:517` | Off-hand seating is VERTICAL-only (nudge would not reproduce at runtime). |  |  | `village.seating_editor_overlay.off_hand_seating_vertical_only` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:522` | Mode: NUDGE on geometryMode: VERTICAL + delta | frag |  | `village.seating_editor_overlay.mode_nudge_geometry` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:538` | Mode: NUDGE on built-in |  |  | `village.seating_editor_overlay.mode_nudge_built` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:541` | Reset to the built-in sheathe pose (zero nudge). |  |  | `village.seating_editor_overlay.reset_built_sheathe_pose_zero` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:545` | Mode: VERTICAL + delta |  |  | `village.seating_editor_overlay.mode_vertical_delta` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:548` | Reset to 100% vertical baseline (hilt lower-half, blade up). |  |  | `village.seating_editor_overlay.reset_100_vertical_baseline_hilt` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:553` | No hero. |  |  | `village.seating_editor_overlay.no_hero` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:568` | JSON snippet logged to Console (copy into offsets.json). |  |  | `village.seating_editor_overlay.json_snippet_logged_console_copy` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:576` | Cleared saved offset for '{_offsetKey}' (back to pure geometry). |  |  | `village.seating_editor_overlay.cleared_saved_offset_offsetkey_back` `[rename]` |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs:586` | Re-equipped from the saved file â€” this is exactly what runtime produces. |  |  | `village.seating_editor_overlay.re_equipped_from_saved_file` `[rename]` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:327` | X Axis (Pitch) |  |  | `village.tower_placement_rotate_menu.x_axis_pitch` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:328` | Y Axis (Yaw) |  |  | `village.tower_placement_rotate_menu.y_axis_yaw` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:329` | Z Axis (Roll) |  |  | `village.tower_placement_rotate_menu.z_axis_roll` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:332` | Scale X (width) |  |  | `village.tower_placement_rotate_menu.scale_x_width` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:333` | Scale Y (height) |  |  | `village.tower_placement_rotate_menu.scale_y_height` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:334` | Scale Z (depth) |  |  | `village.tower_placement_rotate_menu.scale_z_depth` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:364` | Preview & Rotate |  |  | `village.tower_placement_rotate_menu.preview_rotate` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:375` | TOWER PLACEMENT |  |  | `village.tower_placement_rotate_menu.tower_placement` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:430` | {Mathf.RoundToInt(initial)}Â° |  |  | `village.tower_placement_rotate_menu.mathf_roundtoint_initial` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:448` | Rst |  |  | `village.tower_placement_rotate_menu.rst` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:464` | SCALE  -  non-uniform (x multiplier) |  |  | `village.tower_placement_rotate_menu.scale_non_uniform_x_multiplier` `[rename]` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:519` | Rst |  |  | `village.tower_placement_rotate_menu.rst` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:545` | Catalog id |  |  | `village.tower_placement_rotate_menu.catalog_id` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:621` | {towerName}  -  {TierLabel()} |  |  | `village.tower_placement_rotate_menu.towername_tierlabel` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:636` | {_costSkr:F0} cost |  |  | `village.tower_placement_rotate_menu.costskr_f0_cost` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:659` | Snap: |  |  | `village.tower_placement_rotate_menu.snap` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:677` | Confirm Placement |  |  | `village.tower_placement_rotate_menu.confirm_placement` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:696` | Cancel |  | `armyScreen.cancel` | `village.tower_placement_rotate_menu.cancel` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:706` | Reset Rotation |  |  | `village.tower_placement_rotate_menu.reset_rotation` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:831` | {Mathf.RoundToInt(snapped)}Â° |  |  | `village.tower_placement_rotate_menu.mathf_roundtoint_snapped` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:861` | {Mathf.RoundToInt(_xDeg)}Â° |  |  | `village.tower_placement_rotate_menu.mathf_roundtoint_xdeg` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:862` | {Mathf.RoundToInt(_yDeg)}Â° |  |  | `village.tower_placement_rotate_menu.mathf_roundtoint_ydeg` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:863` | {Mathf.RoundToInt(_zDeg)}Â° |  |  | `village.tower_placement_rotate_menu.mathf_roundtoint_zdeg` |
| `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:1002` | (no preview) |  |  | `village.tower_placement_rotate_menu.no_preview` |
| `Assets/_Modules/Village/Vfx/ActionBundlePlayer.cs:251` | (name) |  |  | `village.action_bundle_player.name` |
| `Assets/_Modules/Village/Vfx/ActionBundlePlayer.cs:257` | actor(fallback) |  |  | `village.action_bundle_player.actor_fallback` |
| `Assets/_Modules/Village/Vfx/Destructible.cs:283` | Your {label} was destroyed - the old village {label} stands in for it. Rebuild your own at full cost from Build mode.Your {label} was destroyed - r... | frag |  | `village.destructible.label_was_destroyed_old_village` `[rename]` |
| `Assets/_Modules/Village/Walls/HubRepairAffordance.cs:603` | REPAIR ALL |  |  | `village.hub_repair_affordance.repair_all` |
| `Assets/_Modules/Village/Waves/LookoutNoticeChip.cs:60` | s. |  |  | `village.lookout_notice_chip.s` |
| `Assets/_Modules/Village/Waves/WaveCountdownUI.cs:142` | Next wave in Wave incoming! | frag |  | `village.wave_countdown.wave_incoming` |
| `Assets/_Modules/Village/World/AtmosphereProbe.cs:317` |  <-- PLACEHOLDER/UNSTREAMED LAYER(S): this is the 'ground shows its base colour' state |  |  | `village.atmosphere_probe.placeholder_unstreamed_layer_s_ground` `[rename]` |
| `Assets/_Modules/Village/World/BreakableContainer.cs:384` | Base |  |  | `village.breakable_container.base` |
| `Assets/_Modules/Village/World/BreakableContainer.cs:416` | Latch |  |  | `village.breakable_container.latch` |
| `Assets/_Modules/Village/World/Camps/CampPromptUI.cs:194` | [ Tap ]  Claim Camp |  |  | `village.camp_prompt.tap_claim_camp` |
| `Assets/_Modules/Village/World/Camps/CampPromptUI.cs:224` | Build an Outpost |  |  | `village.camp_prompt.build_outpost` |
| `Assets/_Modules/Village/World/Camps/ChallengeOutpostVictoryController.cs:222` | Outpost Cleared |  |  | `village.challenge_outpost_victory.outpost_cleared` |
| `Assets/_Modules/Village/World/Camps/ChallengeOutpostVictoryController.cs:238` | Gold |  |  | `village.challenge_outpost_victory.gold` |
| `Assets/_Modules/Village/World/Camps/EchoTutorialUI.cs:130` |  will aid you in battle. Tap to dismiss. |  |  | `village.echo_tutorial.will_aid_battle_tap_dismiss` `[rename]` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:90` | nextTower |  |  | `village.owned_town.nexttower` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:96` | upgrade |  | `ownedTown.upgrade` | `village.owned_town.upgrade` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:99` | sell |  | `ownedTown.sell` | `village.owned_town.sell` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:109` | moveWest |  |  | `village.owned_town.movewest` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:112` | moveEast |  |  | `village.owned_town.moveeast` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:121` | build |  | `hud.nav.build` | `village.owned_town.build` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:125` | practicereenter | frag |  | `village.owned_town.practice` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:137` | repairMore |  |  | `village.owned_town.repairmore` |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs:140` | castle |  |  | `village.owned_town.castle` |
| `Assets/_Modules/Village/World/Camps/OwnedTownScenePose.cs:27` | The saved construction has no live body. |  |  | `village.owned_town_scene_pose.saved_construction_has_no_live` `[rename]` |
| `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:1284` | NOT PARKED (the park attempt itself did not run). |  |  | `village.raid_victory.not_parked_park_attempt_itself` `[rename]` |
| `Assets/_Modules/Village/World/Camps/Village2RaidController.cs:334` | Retreat |  |  | `village.village2_raid.retreat` |
| `Assets/_Modules/Village/World/Camps/Village2RaidController.cs:365` | STRONGHOLD CLEARED |  |  | `village.village2_raid.stronghold_cleared` |
| `Assets/_Modules/Village/World/Camps/Village2RaidController.cs:373` | Return to Castle |  | `ownedTown.castle` | `village.village2_raid.return_castle` |
| `Assets/_Modules/Village/World/CastleMoatBuilder.cs:450` | EastNorthSouthWest | frag |  | `village.castle_moat.north` |
| `Assets/_Modules/Village/World/CastleMoatBuilder.cs:578` | EastNorthSouthWest | frag |  | `village.castle_moat.north` |
| `Assets/_Modules/Village/World/CastleMoatBuilder.cs:1375` | deck-missing: |  |  | `village.castle_moat.deck_missing` |
| `Assets/_Modules/Village/World/CastleMoatBuilder.cs:1383` | deck-span: |  |  | `village.castle_moat.deck_span` |
| `Assets/_Modules/Village/World/CastleMoatBuilder.cs:1394` | rail-missing: |  |  | `village.castle_moat.rail_missing` |
| `Assets/_Modules/Village/World/CastleMoatBuilder.cs:1474` | reach-bank-offmesh: |  |  | `village.castle_moat.reach_bank_offmesh` |
| `Assets/_Modules/Village/World/CastleMoatBuilder.cs:1489` | reach- |  |  | `village.castle_moat.reach` |
| `Assets/_Modules/Village/World/CrystalMineNode.cs:372` | ã€” Tap ã€• {MineNode.HarvestVerbFor(MineResource.AetherCrystal)} {_grade} âœ¦      {upgrade} |  |  | `village.crystal_mine_node.tap_minenode_harvestverbfor_mineresource_aet` `[rename]` |
| `Assets/_Modules/Village/World/HubFoliageInjector.cs:484` | Rock |  |  | `village.hub_foliage_injector.rock` |
| `Assets/_Modules/Village/World/HubFoliageInjector.cs:487` | Bush |  |  | `village.hub_foliage_injector.bush` |
| `Assets/_Modules/Village/World/HubFoliageInjector.cs:490` | Tree |  |  | `village.hub_foliage_injector.tree` |
| `Assets/_Modules/Village/World/NodeDiscoverySystem.cs:244` | New site discovered! |  |  | `village.node_discovery_system.new_site_discovered` |
| `Assets/_Modules/Village/World/SceneTransitionTrigger.cs:341` | Travel to {dest} |  |  | `village.scene_transition_trigger.travel_dest` |
| `Assets/_Modules/Village/World/SceneTransitionTrigger.cs:422` | the destination |  |  | `village.scene_transition_trigger.destination` |

### Shard `Assets/_Modules/Core/` - 88 statements, 41 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Core/Addressables/AddressablesMemoryProfiler.cs:115` |   [{status}] {kv.Key}  ({age:F0} s) |  |  | `core.addressables_memory_profiler.status_kv_key_age_f0` `[rename]` |
| `Assets/_Modules/Core/Addressables/EnemyContentWarmer.cs:526` | enemy family '{family}' / '{label}' |  |  | `core.enemy_content_warmer.enemy_family_family_label` `[rename]` |
| `Assets/_Modules/Core/Addressables/OfflineContentService.cs:1119` | {ex.GetType().Name}: {ex.Message} |  |  | `core.offline_content_service.ex_gettype_name_ex_message` `[rename]` |
| `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:578` | on-demand HANDLE '{address}': valid={handle.IsValid()} status={StatusText(handle)}  |  |  | `core.structure_content_warmer.demand_handle_address_valid_handle` `[rename]` |
| `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:616` | owner, so no status, result or exception could be read off it.  |  |  | `core.structure_content_warmer.owner_so_no_status_result` `[rename]` |
| `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:1429` | no OperationException was attached to the handle (status= |  |  | `core.structure_content_warmer.no_operationexception_was_attached_handle` `[rename]` |
| `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:1468` | (no message) |  |  | `core.structure_content_warmer.no_message` |
| `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:1515` | the webview refused/aborted outright â€” it is NOT a CDN status. Note: the R2  |  |  | `core.structure_content_warmer.webview_refused_aborted_outright_not` `[rename]` |
| `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:1531` | PROTOCOL-ERROR (an HTTP status the transport treats as failure) |  |  | `core.structure_content_warmer.protocol_error_http_status_transport` `[rename]` |
| `Assets/_Modules/Core/Analytics/EventTracker.cs:708` | X-Guest-IdX-Session + X-Wallet | frag |  | `core.event_tracker.x_session_x_wallet` `[rename]` |
| `Assets/_Modules/Core/Backend/BackendRequestSigner.cs:265` | dotr-save:v1:{wallet}:{nonce}:{payloadTag} |  |  | `core.backend_request_signer.dotr_save_v1_wallet_nonce` `[rename]` |
| `Assets/_Modules/Core/Backend/BackendRequestSigner.cs:536` | dotr-save:v1:{wallet}:{nonce}:load |  |  | `core.backend_request_signer.dotr_save_v1_wallet_nonce` `[rename]` |
| `Assets/_Modules/Core/Data/CardCollectionCatalog.cs:230` | invalid API envelope:  |  |  | `core.card_collection_catalog.invalid_api_envelope` |
| `Assets/_Modules/Core/Data/CardCollectionCatalog.cs:290` | invalid json:  |  |  | `core.card_collection_catalog.invalid_json` |
| `Assets/_Modules/Core/Data/RemoteCatalogOverrides.cs:383` | the text is not valid JSON (malformed or TRUNCATED - a body cut mid-transfer  |  |  | `core.remote_catalog_overrides.text_not_valid_json_malformed` `[rename]` |
| `Assets/_Modules/Core/Debug/DebugCanvasUI.cs:164` | [{ts}] {msg}\n |  |  | `core.debug_canvas.ts_msg` |
| `Assets/_Modules/Core/Dev/FlagCaptureButton.cs:199` | on-screen FLAG button (mobile) |  |  | `core.flag_capture_button.screen_flag_button_mobile` `[rename]` |
| `Assets/_Modules/Core/Geometry/GearSeat.cs:94` | SHIELD: Socket_Shield mid-LeftLowerArm; long axis âˆ¥ arm (wide top at elbow, point at hand); paint outboard; inner toward body; arm through handle... |  |  | `core.gear_seat.shield_socket_shield_mid_leftlowerarm` `[rename]` |
| `Assets/_Modules/Core/Geometry/WeaponOrientHelper.cs:1285` | the vertical, so this prop hangs ACROSS THE BODY â€” the ~90deg the shipped  |  |  | `core.weapon_orient_helper.vertical_so_prop_hangs_across` `[rename]` |
| `Assets/_Modules/Core/Manage/ManageStateInvariants.cs:56` |  has no DisplayName - the View would have to derive a label [manage-state]  | frag |  | `core.manage_state_invariants.has_no_displayname_view_would` `[rename]` |
| `Assets/_Modules/Core/Ops/RemoteTunables.cs:1728` | 25..1000 at the consumer, and floored at one body so a low percent can never  |  |  | `core.remote_tunables.25_1000_consumer_floored_one` `[rename]` |
| `Assets/_Modules/Core/Payments/Providers/GooglePlay/GooglePlayBillingProvider.cs:47` | Google Play Billing disconnected:  |  |  | `core.google_play_billing_provider.google_play_billing_disconnected` `[rename]` |
| `Assets/_Modules/Core/Payments/Providers/GooglePlay/GooglePlayBillingProvider.cs:69` | Google Play Billing initialization failed:  |  |  | `core.google_play_billing_provider.google_play_billing_initialization_failed` `[rename]` |
| `Assets/_Modules/Core/Platform/PiEnvironment.cs:49` | sandbox(testnet) |  |  | `core.pi_environment.sandbox_testnet` |
| `Assets/_Modules/Core/Platform/WebGLPiPlatform.cs:315` | (no message) |  |  | `core.web_g_l_pi_platform.no_message` |
| `Assets/_Modules/Core/Promo/PromoCodeService.cs:182` | network-exception {ex.GetType().Name}: {ex.Message} |  |  | `core.promo_code_service.network_exception_ex_gettype_name` `[rename]` |
| `Assets/_Modules/Core/Promo/PromoCodeUI.cs:138` | Enter Promo Code |  |  | `core.promo_code.enter_promo_code` |
| `Assets/_Modules/Core/Promo/PromoCodeUI.cs:169` | Redeem Code |  |  | `core.promo_code.redeem_code` |
| `Assets/_Modules/Core/Promo/PromoCodeUI.cs:196` | Enter a code first. |  | `promo.redeem.error.empty` | `core.promo_code.enter_code_first` |
| `Assets/_Modules/Core/Promo/PromoCodeUI.cs:202` | Service unavailable. Restart the game. |  |  | `core.promo_code.service_unavailable_restart_game` `[rename]` |
| `Assets/_Modules/Core/Promo/PromoCodeUI.cs:207` | Validating... |  |  | `core.promo_code.validating` |
| `Assets/_Modules/Core/Promo/PromoCodeUI.cs:238` | Checking...Redeem Code | frag |  | `core.promo_code.checking` |
| `Assets/_Modules/Core/Quests/DailyQuests.cs:117` | {target} |  |  | `core.daily_quests.target` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:131` | Invite Friends |  |  | `core.invite_friends.invite_friends` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:148` | Loading... |  |  | `core.invite_friends.loading` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:162` | Copy |  |  | `core.invite_friends.copy` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:169` | Share on X |  |  | `core.invite_friends.share_x` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:180` | Referral reward already claimed. |  |  | `core.invite_friends.referral_reward_already_claimed` `[rename]` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:202` | Claim Reward |  |  | `core.invite_friends.claim_reward` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:228` | Code copied to clipboard! |  |  | `core.invite_friends.code_copied_clipboard` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:235` | Opening X... |  |  | `core.invite_friends.opening_x` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:242` | Enter a referral code. |  |  | `core.invite_friends.enter_referral_code` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:245` | Checking code... |  |  | `core.invite_friends.checking_code` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:277` | Loading... |  |  | `core.invite_friends.loading` |
| `Assets/_Modules/Core/Referral/InviteFriendsUI.cs:286` | Checking...Claim Reward | frag |  | `core.invite_friends.claim_reward` |
| `Assets/_Modules/Core/Referral/ReferralService.cs:236` | Network error: {ex.Message} |  |  | `core.referral_service.network_error_ex_message` `[rename]` |


(See "Scanner fix results, 2026-09-17 (phase 1b)" appended near the end of this document -- moved 2026-09-18 because its original placement split the Core shard table in two, per the Core tagging lane's finding.)

| `Assets/_Modules/Core/Referral/ReferralService.cs:279` | You received {crystals} Aether Crystals! |  |  | `core.referral_service.received_crystals_aether_crystals` `[rename]` |
| `Assets/_Modules/Core/SceneRouter.cs:376` | Retry |  | `offlineFirstRunRetry` | `core.scene_router.retry` |
| `Assets/_Modules/Core/State/GameStateService.cs:2809` | dotr-save:v1:{wallet}:{nonce}:{payloadHashOrLoadTag} |  |  | `core.game_state_service.dotr_save_v1_wallet_nonce` `[rename]` |
| `Assets/_Modules/Core/State/GameStateService.cs:3140` | the request never reached an HTTP status (offline, DNS, timeout or socket). Identity is NOT implicated. |  |  | `core.game_state_service.request_never_reached_http_status` `[rename]` |
| `Assets/_Modules/Core/State/GameStateService.cs:3142` | the SERVER ANSWERED AND REFUSED THE PAYLOAD. The body head above is the server's own code - fix the payload, not the identity. |  |  | `core.game_state_service.server_answered_refused_payload_body` `[rename]` |
| `Assets/_Modules/Core/State/GameStateService.cs:3151` | <empty> |  |  | `core.game_state_service.empty` |
| `Assets/_Modules/Core/State/GameStateService.cs:3152` | why={why} http={result.ResponseCode} body={body} - {hint} |  |  | `core.game_state_service.why_why_http_result_responsecode` `[rename]` |
| `Assets/_Modules/Core/State/GameStateService.cs:3227` | {ex.GetType().Name}: {ex.Message} |  |  | `core.game_state_service.ex_gettype_name_ex_message` `[rename]` |
| `Assets/_Modules/Core/State/SaveBackupService.cs:154` | legacy unsigned body |  |  | `core.save_backup_service.legacy_unsigned_body` |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:101` | Yes, personalise ads |  |  | `core.ad_consent.yes_personalise_ads` |
| `Assets/_Modules/Core/UI/AdConsentPanel.cs:104` | No, keep them generic |  |  | `core.ad_consent.no_keep_them_generic` `[rename]` |
| `Assets/_Modules/Core/UI/AddressableUIManager.cs:283` | AddressableUIManager MEASURED verify '{address}' |  |  | `core.addressable_u_i_manager.addressableuimanager_measured_verify_address` `[rename]` |
| `Assets/_Modules/Core/UI/ElarionUiKit.cs:1860` | \\U0001F512 |  |  | `core.elarion_ui_kit.u0001f512` |
| `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:1870` | No Target |  |  | `core.elarion_ui_kit_obsidian.no_target` |
| `Assets/_Modules/Core/UI/HarvestOverflowModal.cs:114` | HARVEST RESULT |  |  | `core.harvest_overflow.harvest_result` |
| `Assets/_Modules/Core/UI/HudStrings.cs:151` | [[missing: |  |  | `core.hud_strings.missing` |
| `Assets/_Modules/Core/UI/LoadingOverlay.cs:78` | Loading your realm... |  |  | `core.loading_overlay.loading_realm` |
| `Assets/_Modules/Core/UI/LoadingOverlay.cs:82` | Loading your realm... |  |  | `core.loading_overlay.loading_realm` |
| `Assets/_Modules/Core/UI/LoadingOverlay.cs:219` | ECHOES OF ELARION |  |  | `core.loading_overlay.echoes_elarion` |
| `Assets/_Modules/Core/UI/Mvvm/StakeRewardsVM.cs:65` | Stake Rewards |  |  | `core.stake_rewards.stake_rewards` |
| `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:127` | Skip >Skip Tutorial | frag |  | `core.objective_banner.skip_tutorial` |
| `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:161` | {_baseText}  <color=#C9A54A>({_done}/{_count})</color> |  |  | `core.objective_banner.basetext_color_c9a54a_done_count` `[rename]` |
| `Assets/_Modules/Core/UI/ObjectiveBannerUi.cs:377` | Skip > |  |  | `core.objective_banner.skip` |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:153` | Download Now |  |  | `core.offline_opt_in.download_now` |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:156` | Not Now |  |  | `core.offline_opt_in.not_now` |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:198` | We could not check the download right now. Please try again in a moment -  |  |  | `core.offline_opt_in.we_could_not_check_download` `[rename]` |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:214` | Everything is already downloaded. This game will work without a connection. |  |  | `core.offline_opt_in.everything_already_downloaded_game_will` `[rename]` |
| `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:258` | Downloading content. Keep this screen open. |  |  | `core.offline_opt_in.downloading_content_keep_screen_open` `[rename]` |
| `Assets/_Modules/Core/UI/QueueRailView.cs:295` | Open slot |  |  | `core.queue_rail.open_slot` |
| `Assets/_Modules/Core/UI/ShopTheme.cs:252` | Close |  | `common.close` | `core.shop_theme.close` |
| `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:165` | Powered with SKR |  |  | `core.skr_showcase.powered_skr` |
| `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:183` | How SKR powers the realm |  |  | `core.skr_showcase.how_skr_powers_realm` `[rename]` |
| `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:185` | The Seeker / Solana token integration â€” the intended experience. |  |  | `core.skr_showcase.seeker_solana_token_integration_intended` `[rename]` |
| `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:199` | View Stake Rewards |  |  | `core.skr_showcase.view_stake_rewards` |
| `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:213` | Cosmetic perks are SKR-priced at launch â€” testnet only for now. |  |  | `core.skr_showcase.cosmetic_perks_skr_priced_launch` `[rename]` |
| `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:264` | Coming soon â€” testnet preview. No wallet connected. |  |  | `core.skr_showcase.coming_soon_testnet_preview_no` `[rename]` |
| `Assets/_Modules/Core/UI/StakeRewardsPanel.cs:144` | Stake Rewards |  |  | `core.stake_rewards.stake_rewards` |
| `Assets/_Modules/Core/UI/StakeRewardsPanel.cs:165` | Tier:  {vm.TierName} |  |  | `core.stake_rewards.tier_vm_tiername` |
| `Assets/_Modules/Core/UI/StakeRewardsPanel.cs:173` | No active stake yet |  |  | `core.stake_rewards.no_active_stake_yet` `[rename]` |
| `Assets/_Modules/Core/UI/StakeRewardsPanel.cs:178` | Rewards Unlocked |  |  | `core.stake_rewards.rewards_unlocked` |
| `Assets/_Modules/Core/UI/TutorialSkipUi.cs:178` | Skip Tutorial |  |  | `core.tutorial_skip.skip_tutorial` |
| `Assets/_Modules/Core/UI/VillageLoadOverlay.cs:131` | Loading Elarion... |  |  | `core.village_load_overlay.loading_elarion` |

### Shard `Assets/_Modules/HUD/` - 72 statements, 16 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/HUD/BugReportView.cs:168` | REPORT A BUG |  |  | `hud.bug_report.report_bug` |
| `Assets/_Modules/HUD/BugReportView.cs:220` | Includes recent game logs and your player id to help us fix it. |  |  | `hud.bug_report.includes_recent_game_logs_player` `[rename]` |
| `Assets/_Modules/HUD/BugReportView.cs:229` | Send report |  |  | `hud.bug_report.send_report` |
| `Assets/_Modules/HUD/BugReportView.cs:284` | What went wrong? |  |  | `hud.bug_report.what_went_wrong` |
| `Assets/_Modules/HUD/BugReportView.cs:315` | (no screenshot available) |  |  | `hud.bug_report.no_screenshot_available` |
| `Assets/_Modules/HUD/BugReportView.cs:320` | [x] Include screenshot | frag |  | `hud.bug_report.include_screenshot` |
| `Assets/_Modules/HUD/BugReportView.cs:328` | Sending... |  | `feedback.sending` | `hud.bug_report.sending` |
| `Assets/_Modules/HUD/ClanChatPanel.cs:161` | Remnant Chat |  |  | `hud.clan_chat.remnant_chat` |
| `Assets/_Modules/HUD/ClanChatVM.cs:89` | Remnant Chat |  |  | `hud.clan_chat.remnant_chat` |
| `Assets/_Modules/HUD/CosmeticShopPanel.cs:153` | Description |  |  | `hud.cosmetic_shop.description` |
| `Assets/_Modules/HUD/CosmeticShopPanel.cs:259` | Cosmetic Shop |  |  | `hud.cosmetic_shop.cosmetic_shop` |
| `Assets/_Modules/HUD/CosmeticShopPanel.cs:377` |   Cosmetics |  |  | `hud.cosmetic_shop.cosmetics` |
| `Assets/_Modules/HUD/CosmeticShopPanel.cs:465` | Equipped |  |  | `hud.cosmetic_shop.equipped` |
| `Assets/_Modules/HUD/CosmeticShopPanel.cs:472` | EquipEquipped {displayName} | frag |  | `hud.cosmetic_shop.equipped_displayname` |
| `Assets/_Modules/HUD/CosmeticShopPanel.cs:479` | Locked |  |  | `hud.cosmetic_shop.locked` |
| `Assets/_Modules/HUD/DailyQuestHud.cs:450` | Daily Quest Complete\n |  |  | `hud.daily_quest.daily_quest_complete` |
| `Assets/_Modules/HUD/DailyQuestVM.cs:126` | Daily Quests |  |  | `hud.daily_quest.daily_quests` |
| `Assets/_Modules/HUD/DebuggingController.cs:140` | {label} (capture-next-action ARMED) |  |  | `hud.debugging.label_capture_next_action_armed` `[rename]` |
| `Assets/_Modules/HUD/DebuggingController.cs:218` | DBG button (capture-next-click ARMED â€” now click a dead element) |  |  | `hud.debugging.dbg_button_capture_next_click` `[rename]` |
| `Assets/_Modules/HUD/DebuggingController.cs:260` | [DBG] ready â€” tap DBG, then tap a dead button |  |  | `hud.debugging.dbg_ready_tap_dbg_then` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:222` | Help |  | `settings.help.help` | `hud.help_menu.help` |
| `Assets/_Modules/HUD/HelpMenu.cs:506` |  - drag the list for more of Showing  | frag |  | `hud.help_menu.drag_list_more` |
| `Assets/_Modules/HUD/HelpMenu.cs:569` | Controls - WASD/Arrows/dpad: move \| 1/2/3/4 + face buttons: cast Q/W/E/R  |  |  | `hud.help_menu.controls_wasd_arrows_dpad_move` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:580` | Defenders of the Realm v2 - DeNelle Studios. Models: KayKit + Tripo.  |  |  | `hud.help_menu.defenders_realm_v2_denelle_studios` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:600` | Dev actions unlocked. |  |  | `hud.help_menu.dev_actions_unlocked` |
| `Assets/_Modules/HUD/HelpMenu.cs:617` | Grant failed - economy not alive yet. |  |  | `hud.help_menu.grant_failed_economy_not_alive` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:627` | Grant failed - GrantSpendableUncapped not found. |  |  | `hud.help_menu.grant_failed_grantspendableuncapped_not_foun` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:636` | Granted: 50k wood/iron, 25k stone/crystals, 50k gold. |  |  | `hud.help_menu.granted_50k_wood_iron_25k` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:677` | Reset failed - service not alive. |  |  | `hud.help_menu.reset_failed_service_not_alive` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:680` | Reset - heading back to Hero Select... |  |  | `hud.help_menu.reset_heading_back_hero_select` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:686` | Reset failed - see log. |  |  | `hud.help_menu.reset_failed_see_log` `[rename]` |
| `Assets/_Modules/HUD/HelpMenu.cs:719` | Dev tools unavailable - no UI panel settings in this scene. |  |  | `hud.help_menu.dev_tools_unavailable_no_ui` `[rename]` |
| `Assets/_Modules/HUD/HelpMenuVM.cs:159` | Help |  | `settings.help.help` | `hud.help_menu.help` |
| `Assets/_Modules/HUD/Kit/HudCompassWidget.cs:421` |  deg) |  |  | `hud.hud_compass.deg` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:460` | Repair |  |  | `hud.hud_kit.repair` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:556` | Cancel |  | `armyScreen.cancel` | `hud.hud_kit.cancel` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:899` | Build |  | `hud.nav.build` | `hud.hud_kit.build` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:911` | Talk |  | `hud.nav.talk` | `hud.hud_kit.talk` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:924` | Hero |  | `hud.nav.hero` | `hud.hud_kit.hero` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:995` | Journey |  | `hud.nav.journey` | `hud.hud_kit.journey` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:1389` | hud-card |  |  | `hud.hud_kit.hud_card` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:1937` | Start Wave |  |  | `hud.hud_kit.start_wave` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:3810` | CHOOSE AN ITEM |  |  | `hud.hud_kit.choose_item` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:3851` | CHOOSE AN ITEM |  |  | `hud.hud_kit.choose_item` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:3883` | Gameplay is paused while you choose. |  |  | `hud.hud_kit.gameplay_paused_while_choose` `[rename]` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:3891` | HEALING POTION |  |  | `hud.hud_kit.healing_potion` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:3894` | MANA DRAUGHT |  |  | `hud.hud_kit.mana_draught` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:3925` | HEALING POTION  x |  |  | `hud.hud_kit.healing_potion_x` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:3926` | MANA DRAUGHT  x |  |  | `hud.hud_kit.mana_draught_x` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:4438` |   Lv  |  |  | `hud.hud_kit.lv` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:4693` | Wave  |  |  | `hud.hud_kit.wave` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:4989` | Add a skill to activate |  |  | `hud.hud_kit.add_skill_activate` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5767` | gear dock ' |  |  | `hud.hud_kit.gear_dock` |
| `Assets/_Modules/HUD/LeaderboardPanel.cs:96` | Leaderboard |  |  | `hud.leaderboard.leaderboard` |
| `Assets/_Modules/HUD/LeaderboardPanel.cs:284` | Visit Town |  |  | `hud.leaderboard.visit_town` |
| `Assets/_Modules/HUD/LeaderboardVM.cs:94` | Leaderboard |  |  | `hud.leaderboard.leaderboard` |
| `Assets/_Modules/HUD/LeaderboardVM.cs:144` | . Scores are local; ranks shown are placeholder rivals until the online ladder is connected.Source:  | frag |  | `hud.leaderboard.scores_local_ranks_shown_placeholder` `[rename]` |
| `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:352` | [ LOCKED ] |  |  | `hud.player_deck_workspace.locked` |
| `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:927` | Quests |  |  | `hud.player_deck_workspace.quests` |
| `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:952` | raidRaids | frag |  | `hud.player_deck_workspace.raids` |
| `Assets/_Modules/HUD/QuestTrackerVM.cs:64` | Quest Tracker |  |  | `hud.quest_tracker.quest_tracker` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:88` | Town Showcase |  |  | `hud.town_showcase_visit.town_showcase` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:94` | Loading town... |  |  | `hud.town_showcase_visit.loading_town` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:96` | Public read-only showcase |  |  | `hud.town_showcase_visit.public_read_only_showcase` `[rename]` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:112` | Previous |  |  | `hud.town_showcase_visit.previous` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:115` | Return to Leaderboard |  |  | `hud.town_showcase_visit.return_leaderboard` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:118` | Next |  |  | `hud.town_showcase_visit.next` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:130` | Defender |  |  | `hud.town_showcase_visit.defender` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:131` | Loading explicitly shared snapshot... |  |  | `hud.town_showcase_visit.loading_explicitly_shared_snapshot` `[rename]` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:136` | This town is not available to visit. |  |  | `hud.town_showcase_visit.town_not_available_visit` `[rename]` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:143` | This town snapshot could not be displayed safely. |  |  | `hud.town_showcase_visit.town_snapshot_could_not_displayed` `[rename]` |
| `Assets/_Modules/HUD/TownShowcaseVisitPanel.cs:149` | Read-only snapshot v |  |  | `hud.town_showcase_visit.read_only_snapshot_v` `[rename]` |

### Shard `Assets/_Modules/Dungeons/` - 33 statements, 9 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Dungeons/ComposedPropVisuals.cs:97` | Shaft |  |  | `dungeon.composed_prop_visuals.shaft` |
| `Assets/_Modules/Dungeons/ComposedPropVisuals.cs:175` | Keyhole |  |  | `dungeon.composed_prop_visuals.keyhole` |
| `Assets/_Modules/Dungeons/ComposedPropVisuals.cs:233` | Plinth |  |  | `dungeon.composed_prop_visuals.plinth` |
| `Assets/_Modules/Dungeons/ComposedPropVisuals.cs:237` | Bowl |  |  | `dungeon.composed_prop_visuals.bowl` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:198` | Stalk |  |  | `dungeon.ingredient_pickup.stalk` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:200` | Node |  |  | `dungeon.ingredient_pickup.node` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:205` | Belly |  |  | `dungeon.ingredient_pickup.belly` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:207` | Taper |  |  | `dungeon.ingredient_pickup.taper` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:209` | Tip |  |  | `dungeon.ingredient_pickup.tip` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:215` | Core |  |  | `dungeon.ingredient_pickup.core` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:221` | Shard{i} |  |  | `dungeon.ingredient_pickup.shard_i` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:238` | Stalk |  |  | `dungeon.ingredient_pickup.stalk` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:240` | Cap |  |  | `dungeon.ingredient_pickup.cap` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:245` | Taproot |  |  | `dungeon.ingredient_pickup.taproot` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:252` | Belly |  |  | `dungeon.ingredient_pickup.belly` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:254` | Neck |  |  | `dungeon.ingredient_pickup.neck` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:256` | Stopper |  |  | `dungeon.ingredient_pickup.stopper` |
| `Assets/_Modules/Dungeons/Crafting/IngredientPickup.cs:261` | Sphere |  |  | `dungeon.ingredient_pickup.sphere` |
| `Assets/_Modules/Dungeons/DungeonCameraRig.cs:755` | <empty>DESTROYED {body.GetType().Name}{body.GetType().Name}(enabled={body.enabled}) | frag |  | `dungeon.dungeon_camera_rig.body_gettype_name_enabled_body` `[rename]` |
| `Assets/_Modules/Dungeons/DungeonCameraRig.cs:842` | DESTROYED {bodyStage.GetType().Name} (body stage SKIPPED - camera cannot follow) |  |  | `dungeon.dungeon_camera_rig.destroyed_bodystage_gettype_name_body` `[rename]` |
| `Assets/_Modules/Dungeons/DungeonController.cs:2244` | [PLACEHOLDER] |  |  | `dungeon.dungeon.placeholder` |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:262` | Leave Dungeon |  |  | `dungeon.dungeon_exit_interactable.leave_dungeon` |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:268` | Leave Dungeon |  |  | `dungeon.dungeon_exit_interactable.leave_dungeon` |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:791` | SEALED\nDEFEAT BOSS |  |  | `dungeon.dungeon_exit_interactable.sealed_defeat_boss` |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs:958` | Leave dungeon?Continue to exit returns you to town. Cancel keeps you in the dungeon. | frag |  | `dungeon.dungeon_exit_interactable.continue_exit_returns_town_cancel` `[rename]` |
| `Assets/_Modules/Dungeons/DungeonTreasurePanel.cs:250` | The cache holds: |  |  | `dungeon.dungeon_treasure.cache_holds` |
| `Assets/_Modules/Dungeons/DungeonTreasurePanel.cs:317` | Take |  |  | `dungeon.dungeon_treasure.take` |
| `Assets/_Modules/Dungeons/RoomForge/CommonDungeonDoor.cs:475` | LockedOpen Door | frag |  | `dungeon.common_dungeon_door.open_door` |
| `Assets/_Modules/Dungeons/UI/CraftingPanelController.cs:185` | Recipe |  |  | `dungeon.crafting_panel.recipe` |
| `Assets/_Modules/Dungeons/UI/CraftingPanelController.cs:277` | {ing.Shown} / {ing.Need} |  |  | `dungeon.crafting_panel.ing_shown_ing_need` `[rename]` |
| `Assets/_Modules/Dungeons/UI/CraftingPanelController.cs:314` | CraftCrafted | frag |  | `dungeon.crafting_panel.crafted` |
| `Assets/_Modules/Dungeons/UI/DungeonCraftVM.cs:43` | Crafting |  |  | `dungeon.dungeon_craft.crafting` |
| `Assets/_Modules/Dungeons/UI/DungeonCraftVM.cs:143` | Recipe |  |  | `dungeon.dungeon_craft.recipe` |

### Shard `Assets/_Modules/Onboarding/` - 28 statements, 9 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Onboarding/FirstWatchWelcomeLetter.cs:109` | Close |  | `common.close` | `onboarding.first_watch_welcome_letter.close` |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:424` | CHOOSE YOUR DEFENDER |  |  | `onboarding.hero_select.choose_defender` |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:814` | Coming Soon |  | `storeBuyComingSoon` | `onboarding.hero_select.coming_soon` |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:935` | Abilities revealed at launch |  |  | `onboarding.hero_select.abilities_revealed_launch` |
| `Assets/_Modules/Onboarding/HeroSelectController.cs:1059` | COMING SOON |  | `storeBuyComingSoon` | `onboarding.hero_select.coming_soon` |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:365` | Play as Guest |  |  | `onboarding.login_panel.play_as_guest` |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:407` | Opening Google sign-in... you can still tap Play as Guest. |  |  | `onboarding.login_panel.opening_google_sign_can_still` `[rename]` |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:409` | Opening your wallet... you can still tap Play as Guest. |  |  | `onboarding.login_panel.opening_wallet_can_still_tap` `[rename]` |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:427` | Google sign-in did not respond. Try Continue with Google again,  |  |  | `onboarding.login_panel.google_sign_did_not_respond` `[rename]` |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:430` | Your wallet did not respond. Open your wallet app and try Connect Wallet again,  |  |  | `onboarding.login_panel.wallet_did_not_respond_open` `[rename]` |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:442` | Google sign-in failed. Try again, or tap Play as Guest to start now. |  |  | `onboarding.login_panel.google_sign_failed_try_again` `[rename]` |
| `Assets/_Modules/Onboarding/LoginPanelController.cs:444` | Wallet connect failed. Try again, or tap Play as Guest to start now. |  |  | `onboarding.login_panel.wallet_connect_failed_try_again` `[rename]` |
| `Assets/_Modules/Onboarding/OnboardingFlow.cs:327` | Skip |  |  | `onboarding.onboarding_flow.skip` |
| `Assets/_Modules/Onboarding/OnboardingFlow.cs:331` | Next |  |  | `onboarding.onboarding_flow.next` |
| `Assets/_Modules/Onboarding/OnboardingFlow.cs:501` | {index + 1} / {Beats.Length} |  |  | `onboarding.onboarding_flow.index_1_beats_length` `[rename]` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:235` | pet-select-title |  |  | `onboarding.pet_select.pet_select_title` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:245` | Echoes await in Elarion. Visit the Echo Hollow in town to bond your first companion.pet-select-subtitle | frag |  | `onboarding.pet_select.echoes_await_elarion_visit_echo` `[rename]` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:329` | pet-already-titleYou already have a Warden | frag |  | `onboarding.pet_select.already_have_warden` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:336` | {petName} walks the watch beside you. A Warden bonds once â€” that bond can't be traded away. |  |  | `onboarding.pet_select.petname_walks_watch_beside_warden` `[rename]` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:347` | Want another companion? More Wardens can be found out in the realm â€” earned through quests or summoned at the marketplace. |  |  | `onboarding.pet_select.want_another_companion_more_wardens` `[rename]` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:429` | Your Warden awaits in town |  |  | `onboarding.pet_select.warden_awaits_town` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:436` | Visit the Echo Hollow in Elarion to bond your first companion. |  |  | `onboarding.pet_select.visit_echo_hollow_elarion_bond` `[rename]` |
| `Assets/_Modules/Onboarding/PetSelectController.cs:523` | Warden |  |  | `onboarding.pet_select.warden` |
| `Assets/_Modules/Onboarding/PostLoadTopThree.cs:61` | TOP 3 PLAYERS |  |  | `onboarding.post_load_top_three.top_3_players` |
| `Assets/_Modules/Onboarding/PostLoadTopThree.cs:75` | Continue |  |  | `onboarding.post_load_top_three.continue` |
| `Assets/_Modules/Onboarding/SplashLoading.cs:285` | presents |  |  | `onboarding.splash_loading.presents` |
| `Assets/_Modules/Onboarding/StoryIntroController.cs:303` | Skip |  |  | `onboarding.story_intro.skip` |
| `Assets/_Modules/Onboarding/TitleController.cs:400` | Powered with SKR |  |  | `onboarding.title.powered_skr` |

### Shard `Assets/_Modules/Wallet/` - 14 statements, 7 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Wallet/HeartboundStatusClient.cs:148` | transport:  |  |  | `wallet.heartbound_status_client.transport` |
| `Assets/_Modules/Wallet/HeartboundStatusClient.cs:184` | empty response body |  |  | `wallet.heartbound_status_client.empty_response_body` |
| `Assets/_Modules/Wallet/NightMarketComposition.cs:498` | mode={0} body={1:0}x{2:0} spotlight={3:0}x{4:0} market={5:0}x{6:0} commerce={7:0}x{8:0} card={9:0} cta-host={10:0} status={11:0} thresholds[3col-mi... | frag |  | `wallet.night_market_composition.mode_0_body_1_0` `[rename]` |
| `Assets/_Modules/Wallet/PackStore.cs:4640` | {pack.Name} unlocked - tx {Shorten(result.TxSignature)}. |  |  | `wallet.pack_store.pack_name_unlocked_tx_shorten` `[rename]` |
| `Assets/_Modules/Wallet/PurchaseQuoteService.cs:595` | price list |  |  | `wallet.purchase_quote_service.price_list` |
| `Assets/_Modules/Wallet/PurchaseQuoteService.cs:702` | quote '{pack.Sku}' |  |  | `wallet.purchase_quote_service.quote_pack_sku` |
| `Assets/_Modules/Wallet/RedeemCodeVM.cs:54` | Redeem a Code |  | `promo.redeem.entry` | `wallet.redeem_code.redeem_code` |
| `Assets/_Modules/Wallet/SolanaWalletProvider.cs:1088` | RPC transport {ex.GetType().Name}: {ex.Message} |  |  | `wallet.solana_wallet_provider.rpc_transport_ex_gettype_name` `[rename]` |
| `Assets/_Modules/Wallet/SolanaWalletProvider.cs:1105` | RPC HTTP {req.responseCode} returned no signature |  |  | `wallet.solana_wallet_provider.rpc_http_req_responsecode_returned` `[rename]` |
| `Assets/_Modules/Wallet/SolanaWalletProvider.cs:1106` | ; data={data} |  |  | `wallet.solana_wallet_provider.data_data` |
| `Assets/_Modules/Wallet/WalletConnectDialog.cs:222` | Connect WalletConnectingâ€¦ | frag | `settings.wallet.connect` | `wallet.wallet_connect_dialog.connect_wallet` |
| `Assets/_Modules/Wallet/WalletConnectDialog.cs:241` | Connected â€” {_wallet.Account.WalletName} |  |  | `wallet.wallet_connect_dialog.connected_wallet_account_walletname` `[rename]` |
| `Assets/_Modules/Wallet/WalletConnectDialog.cs:244` | Connectingâ€¦ |  |  | `wallet.wallet_connect_dialog.connecting` |
| `Assets/_Modules/Wallet/WalletConnectDialog.cs:247` | No wallet connected |  |  | `wallet.wallet_connect_dialog.no_wallet_connected` |

### Shard `Assets/_Modules/Settings/` - 9 statements, 3 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Settings/MusicToggleBootstrap.cs:104` | music-toggle |  |  | `settings.music_toggle_bootstrap.music_toggle` |
| `Assets/_Modules/Settings/MusicToggleBootstrap.cs:155` | Music: OffMusic: On | frag |  | `settings.music_toggle_bootstrap.music_off` |
| `Assets/_Modules/Settings/MusicToggleBootstrap.cs:164` | Music OffMusic On | frag |  | `settings.music_toggle_bootstrap.music_off` |
| `Assets/_Modules/Settings/PauseController.cs:193` | Paused |  |  | `settings.pause.paused` |
| `Assets/_Modules/Settings/PauseController.cs:219` | Resume |  |  | `settings.pause.resume` |
| `Assets/_Modules/Settings/PauseController.cs:226` | Settings |  | `settings.title` | `settings.pause.settings` |
| `Assets/_Modules/Settings/PauseController.cs:231` | Quit to Title |  |  | `settings.pause.quit_title` |
| `Assets/_Modules/Settings/PauseController.cs:363` | quit to title |  |  | `settings.pause.quit_title` |
| `Assets/_Modules/Settings/SettingsController.cs:404` | Repair Session |  |  | `settings.settings.repair_session` |

### Shard `Assets/_Modules/Web3/` - 9 statements, 1 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Web3/SwapVM.cs:134` | Enter an amount to see the rate. |  | `swap.statusEnter` | `swap.swap.enter_amount_see_rate` `[rename]` |
| `Assets/_Modules/Web3/SwapVM.cs:149` | Enter a valid amount. |  |  | `swap.swap.enter_valid_amount` |
| `Assets/_Modules/Web3/SwapVM.cs:154` | Getting rate... |  | `swap.statusLoading` | `swap.swap.getting_rate` |
| `Assets/_Modules/Web3/SwapVM.cs:188` | Could not fetch rate. Check connection. |  | `swap.statusError` | `swap.swap.could_not_fetch_rate_check` `[rename]` |
| `Assets/_Modules/Web3/SwapVM.cs:197` | Connect your wallet to swap. |  | `swap.statusConnect` | `swap.swap.connect_wallet_swap` |
| `Assets/_Modules/Web3/SwapVM.cs:221` | Connect your wallet to swap. |  | `swap.statusConnect` | `swap.swap.connect_wallet_swap` |
| `Assets/_Modules/Web3/SwapVM.cs:226` | Sending to wallet for approval... |  | `swap.statusSigning` | `swap.swap.sending_wallet_approval` |
| `Assets/_Modules/Web3/SwapVM.cs:239` | Swap failed. Please try again. |  | `swap.statusFailed` | `swap.swap.swap_failed_please_try_again` `[rename]` |
| `Assets/_Modules/Web3/SwapVM.cs:247` | Swap failed. Please try again. |  | `swap.statusFailed` | `swap.swap.swap_failed_please_try_again` `[rename]` |

### Shard `Assets/_Modules/BattleATB/` - 7 statements, 3 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/BattleATB/BattleHudUgui.cs:827` | 's Turn |  |  | `battle.battle_hud_ugui.s_turn` |
| `Assets/_Modules/BattleATB/BattleHudUgui.cs:912` | {u.Hp}/{u.MaxHp} |  |  | `battle.battle_hud_ugui.u_hp_u_maxhp` `[rename]` |
| `Assets/_Modules/BattleATB/BattleHudUgui.cs:918` | {curRes}/{maxRes} |  |  | `battle.battle_hud_ugui.curres_maxres` |
| `Assets/_Modules/BattleATB/Engine/Actions.cs:71` | {actor.Name}'s {opts.Label} is absorbed by {target.Name}'s shield.{actor.Name} hits {target.Name} with {opts.Label} for {dealt} | frag |  | `battle.actions.actor_name_s_opts_label` `[rename]` |
| `Assets/_Modules/BattleATB/Engine/Actions.cs:125` | a basic attack |  |  | `battle.actions.basic_attack` |
| `Assets/_Modules/BattleATB/Engine/Actions.cs:182` | {ability.Name} (splash) |  |  | `battle.actions.ability_name_splash` |
| `Assets/_Modules/BattleATB/Engine/Combat.cs:208` | {unit.Name} suffers {lost} {status.Kind.ToToken()} damage. |  |  | `battle.combat.unit_name_suffers_lost_status` `[rename]` |

### Shard `Assets/_Modules/Audio/` - 6 statements, 4 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Audio/AudioService.cs:1126` | Echoes of Elarion (Main Theme) |  |  | `audio.audio_service.echoes_elarion_main_theme` `[rename]` |
| `Assets/_Modules/Audio/AudioService.cs:1134` | Echoes of Elarion (Main Theme) |  |  | `audio.audio_service.echoes_elarion_main_theme` `[rename]` |
| `Assets/_Modules/Audio/JukeboxVM.cs:70` | Jukebox |  |  | `audio.jukebox.jukebox` |
| `Assets/_Modules/Audio/MusicSelectionPanel.cs:95` | Jukebox |  |  | `audio.music_selection.jukebox` |
| `Assets/_Modules/Audio/MusicSelectionPanel.cs:170` | Audio not ready. |  |  | `audio.music_selection.audio_not_ready` |
| `Assets/_Modules/Audio/MusicSelectionPanelBootstrap.cs:73` | IntroStore | frag |  | `audio.music_selection_panel_bootstrap.intro` |

### Shard `Assets/_Modules/Pets/` - 4 statements, 2 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/Pets/PetDeployer.cs:1202` | Pet |  |  | `echo.pet_deployer.pet` |
| `Assets/_Modules/Pets/PetHeroLeash.cs:560` | COMPLETE path but zero headway - the body is wedged on something the navmesh does not know  |  |  | `echo.pet_hero_leash.complete_path_but_zero_headway` `[rename]` |
| `Assets/_Modules/Pets/PetHeroLeash.cs:616` | route(status={_leadPathStatus}, corners={_leadCornerCount},  |  |  | `echo.pet_hero_leash.route_status_leadpathstatus_corners_leadcorn` `[rename]` |
| `Assets/_Modules/Pets/PetHeroLeash.cs:640` | BODY DID NOT MOVE (carrot written, zero displacement â€” the write is being ignored downstream)body moving | frag |  | `echo.pet_hero_leash.body_did_not_move_carrot` `[rename]` |

### Shard `Assets/_Modules/DialogueUI/` - 3 statements, 1 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/DialogueUI/IntroSequencePlayer.cs:372` | Skip  > |  |  | `dialogue.intro_sequence_player.skip` |
| `Assets/_Modules/DialogueUI/IntroSequencePlayer.cs:457` | DEFENDERS OF THE REALM |  | `heroSelect.title` | `dialogue.intro_sequence_player.defenders_realm` |
| `Assets/_Modules/DialogueUI/IntroSequencePlayer.cs:458` | Echoes of Elarion |  |  | `dialogue.intro_sequence_player.echoes_elarion` |

### Shard `Assets/_Modules/GooglePlay/` - 3 statements, 2 files

| file:line | literal | frag | existing key | proposed key |
|---|---|---|---|---|
| `Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:127` | play-skin |  |  | `store.google_play_storefront.play_skin` |
| `Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:135` | Secure purchases through Google Play |  |  | `store.google_play_storefront.secure_purchases_through_google_play` `[rename]` |
| `Assets/_Modules/GooglePlay/GooglePlayStorefrontVM.cs:62` | Purchases checked and restored.Restore failed. | frag |  | `store.google_play_storefront.purchases_checked_restored` |

## 6. `needs-human-judgment`

### 6a. The 2521 canonical-JSON authored values - one owner ruling, not 2521 decisions

These are NOT C# call sites and they are NOT in scope of anything WO-1857 describes: the ticket's Scope
§1 is `Assets/_Modules/**/*.cs`, and its fix pattern (§3: `HudStrings.cs` / `SettingsText.cs`) has no
meaning for a value sitting in a data file that the game reads by id. They come from
`build-string-manifest.ps1:284-290`, which inventories any canonical-JSON leaf whose FIELD NAME matches
`name|title|label|description|body|text|message|…`. Top files:

| canonical file | rows |
|---|---|
| `Assets/Resources/Data/Canonical/widget-params.json` | 388 |
| `Assets/Resources/Data/Canonical/dialogue/dialogues.json` | 367 |
| `Assets/Resources/Data/Canonical/hero-talents.json` | 169 |
| `Assets/Resources/Data/Canonical/weapons.json` | 135 |
| `Assets/Resources/Data/Canonical/guide-content.json` | 128 |
| `Assets/Resources/Data/Canonical/abilities.json` | 96 |
| `Assets/Resources/Data/Canonical/quests.json` | 87 |
| `Assets/Resources/Data/Canonical/talent-icon-map.json` | 83 |
| `Assets/Resources/Data/Canonical/concept-icons.json` | 68 |
| `Assets/Resources/Data/Canonical/cosmetics.json` | 68 |
| `Assets/Resources/Data/Canonical/enemies.json` | 57 |
| `Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json` | 57 |
| `Assets/Resources/Data/Canonical/structures-catalog.json` | 56 |
| `Assets/Resources/Data/Canonical/building-tiers.json` | 49 |
| `Assets/Resources/Data/Canonical/dungeons/healers-cottage.json` | 47 |
| `Assets/Resources/Data/Canonical/packs.json` | 43 |
| `Assets/Resources/Data/Canonical/canon-strings.json` | 41 |
| `Assets/Resources/Data/Canonical/armor.json` | 40 |
| `Assets/Resources/Data/Canonical/daily-quests.json` | 38 |
| `Assets/Resources/Data/Canonical/troop-upgrades.json` | 35 |

The set is genuinely mixed, which is exactly why it needs a ruling rather than a lane:

- **Real player copy with no visible locale mechanism** - `dialogue/dialogues.json` (367),
  `guide-content.json` (128), `quests.json` (87), `tutorial/tutorial-steps.json` (57),
  `daily-quests.json`, the per-dungeon files. `key-naming.md` §Collections already anticipates these
  (`Dialogue`, `Quests` collections), so the intended home exists - nothing routes into it yet.
- **Catalog display names and blurbs** - `enemies.json`, `weapons.json`, `troops.json`, `cosmetics.json`,
  `structures-catalog.json`, `vendors.json`, `abilities.json`, `hero-talents.json`. Player-facing, but
  each is also a catalog row keyed by id; localizing them is a data-schema decision (a `nameKey`
  alongside `displayName`), not a call-site swap.
- **Not copy at all** - `widget-params.json` (388; values like `NodeSlot (11)` are authoring labels),
  `talent-icon-map.json` (83), `concept-icons.json` (68) - icon/identity maps whose `label`/`name`
  fields are data keys. CLAUDE.md's own line applies: icon identities stay stable English data keys.

**The question for the owner:** does WO-1857 own canonical-JSON content localization, or does that
split into its own ticket with its own schema decision? I am not guessing at this, because the two
answers produce completely different work. Recommendation: **its own ticket** - it needs a schema
choice and a translator workflow, and folding it in would stall the C# sweep that WO-1857 was actually
minted for.

### 6b. Named rows I could not settle

- **`Assets/_Modules/HUD/AdminOverlay.cs`** - bucketed `dev-surface` at file level, and its strings are
  unambiguously developer text (`"FULL RESET (new player - wipes + quits)"`, `"+25 Wisdom (talents)"`).
  But it is not under an `Editor/` directory, it ships in the player build, and CLAUDE.md §5 documents
  it as a live runtime file. If the overlay is reachable in a shipped build on a non-English device,
  these are player-visible. **Owner call: is AdminOverlay reachable in a release build?** If yes it
  needs a decision (localize, or gate it out of release), not a silent exclusion. Same question, lower
  stakes, for `OwnerDevToolsOverlay.cs` and `DevPanelController.cs`.
- **`Assets/_Modules/Core/State/GameStateService.cs:3140-3152`** - identity/HTTP failure hints
  (`"the request never reached an HTTP status (offline, DNS, timeout or socket)…"`). They read as
  developer diagnostics, but they are assembled into a string that may reach a sign-in error surface.
  Needs one read of its consumer before it is either excluded or wired.
- **`Assets/_Modules/Core/Manage/ManageStateInvariants.cs`** - failure text for an invariant check.
  Almost certainly developer-only; worth 60 seconds of confirmation rather than a guess.
- **Cross-domain key reuse the join proposed but a human should veto.** The mechanical English-value
  join matched `HeroSelectController.cs:814/1059 "Coming Soon"` to `storeBuyComingSoon`, and
  `IntroSequencePlayer.cs:457 "DEFENDERS OF THE REALM"` to `heroSelect.title`. Same English, different
  screen and different job - reusing them would be exactly the synonym/over-reuse error `key-naming.md`
  warns about in both directions. These rows are listed in the shards with their matched key, and each
  needs a yes/no before it is wired.
- **`Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs`** - excluded as a generated
  embedded data blob. It does contain real player copy (structure display names and descriptions), but
  it is a GENERATED mirror of `structures-catalog.json`; fixing it here would be overwritten. It belongs
  to the §6a ruling, and is noted so nobody wires it by hand.

## 7. What must happen next, in order

1. **Scanner-fix lane (blocking, do it first).** Teach `Get-CSharpContext` the project's own UI helpers
   (`AddDockTab`, `ElarionUiKit.ButtonPack`, `MakeWordVerb`, `AddImage`, `BuildObsidianPanel`,
   `DetailCardRow`, `AddButton`, `AddPlateRow`, `Caption`), and make `Test-ExcludedCSharpLine`
   STATEMENT-aware so a multi-line `FlowTrace`/`[Tooltip]` stops leaking its continuation lines into
   `literalCandidate` - that one change removes about 1685 false
   positives and adds back the AddDockTab class. Then regenerate the manifest and re-diff against this
   document. **Until this lands, no lane may claim the sweep is complete.**
2. **Acceptance floor lane (`HUD`).** The six `AddDockTab` labels in §1a, reusing `settings.title` for
   `"Settings"`. This is the ticket's stated minimum floor and it is not in the manifest, so it can
   only be done from §1a.
3. **`common.*` lane.** Mint §4 in all ten locale files plus `GameStrings_en.asset`, BEFORE the shards
   run, so the shards reference rather than duplicate.
4. **Shard lanes, file-disjoint, in descending size:** `Village` (split by sub-directory - `UI/Manage`,
   `Hero`, `Troops`, `Buildings`, `BuildMode`, `Harvest` are natural splits), `Core`, `HUD`, `Dungeons`,
   `Onboarding`, `Wallet`, then the small ones (`Settings`, `Web3`, `Audio`, `BattleATB`, `Pets`,
   `GooglePlay`, `DialogueUI`) which can share one lane. **Each lane edits `.cs` only and hands its
   key/value additions back as a sidecar - §5a. It renames every `[rename]` key before minting - §5.**
5. **Owner rulings** from §6 - especially 6a (canonical JSON in or out) and the AdminOverlay question.
6. **Proof, per lane:** brace + NUL gate on every `.cs` touched, `LocaleParityRegression` green (the
   three English copies match), full `REGRESSION_OK <n>/<n>` on the combined tree, and a manifest
   re-diff showing the lane's rows dropped out of `literalCandidate`.

## 8. Reproducing this

```
powershell -NoProfile -File tools/localization/build-string-manifest.ps1
```

The classification scripts for this pass are throwaway analysis, kept out of the repo deliberately
(they are a one-shot triage, not a gate). The durable outputs are this document and the regenerated
manifest. If the numbers here need re-deriving, the rules in §3 are stated completely enough to
re-implement; the scanner fix in §7.1 is the version that should become permanent.

## Scanner fix results, 2026-09-17 (phase 1b)

**Scope of this pass:** `tools/localization/build-string-manifest.ps1` only. No `.cs` file, no locale
file, and no other doc was touched. Nothing was committed. This section is appended, not a rewrite —
everything above this heading is unchanged from the original pass.

### What changed in the scanner

`Get-CSharpContext` (`:112-125` in the pre-fix file) only recognised a fixed list of call shapes
(`ShowToast`, `BuildObsidianModal`, `BuildObsidianButton`, `.Button(`, `.Label(`,
`SetStatus`/`ShowStatus`/`SetMessage`, a `.text/.placeholder/.caption/.title =` assignment, or a bare
named-copy word on the line) and returned no `uiHint` for anything else — the exact gap §1 documents.
Three changes closed it, each proven at source before landing and each verified with an isolated
function-level test (not just a full-corpus rerun) because the failure modes only show up on specific
line shapes that a full rerun's aggregate counts would hide:

**1. Recognise the project's actual UI-construction helpers.** Confirmed by grep, not guessed:
`ElarionUiKit`, `ElarionUiKitObsidian` and `ElarionUiKitDetailCard` are the SAME
`public static partial class ElarionUiKit` (`class ElarionUiKit\b` in `Assets/_Modules/Core/UI/*.cs`,
checked 2026-09-17), so one rule — `\bElarionUiKit(?:Obsidian|DetailCard)?\.\w+\s*\(` — covers every
static method and `new ElarionUiKit.<Type>(` constructor on it (`ButtonPack`, `Label`, `Header`,
`Card`, `BuildObsidianPanel`, `BuildConfirmModal`, `BuildTab`, `BuildToggle`, `BuildNameplate`,
`BuildChatDock`, `CurrencyChip`, `DetailCardRow`, `AddImage`, ...), regardless of method name. Three
bare (no-class-prefix) project-local helpers were added by name, each grepped to its declaration:
`AddDockTab` (`HudKitController.cs`, private, `(RectTransform panel, int i, string label, Action
onTap)`), `MakeWordVerb` (`BuildHudController.cs`, private static, `(parent, name, label, kind, min,
max, onClick, out label)`), `AddButton` (four private declarations across
`ArenaAttackPaletteUI.cs`/`ArenaPanel.cs`/`HeroInventoryController.cs`/`DevPanelController.cs`, all
`(Transform/VisualElement parent, string label, ...)`). **Deviation from the brief:** bare `AddImage`
was deliberately left OUT of the bare-helper list. All four declarations found this session
(`ElarionUiKit.cs:2550`, `QueueRailView.cs:780`, `ArenaAttackPaletteUI.cs:233`, `ArenaPanel.cs:428`,
`HeroInventoryController.cs:636`) are `AddImage(Transform parent, string name, ...)` — the literal
argument is a GameObject/sprite name, never player copy, so matching bare `AddImage` would be a
guaranteed false positive with zero true positives. `ElarionUiKit.AddImage(` is still caught by the
kit-wide rule for consistency with how the tool already treats every other kit method (it never
special-cased no-copy kit methods like `FitSingleLine`/`EnsureFont`/`Rule`/`GoldPerimeter` either) —
that tradeoff is pre-existing, not introduced here, and it is the single largest false-positive class
in the sample below.

**2. A bounded statement-continuation lookback**, because the manifest's unit is one line and a
UI-construction call can open several lines before its literal argument lands (a wrapped multi-arg
call, or a ternary literal) — `EquipmentPanel.cs:915-916` is the proven case:
```
var action = ElarionUiKit.ButtonPack(_approvedDetailHost,
    item.Equipped ? "REMOVE" : "EQUIP", ElarionUiKit.ButtonKind.Gold, ...
```
`Get-CSharpContext` now takes the full line array and its own index, and — only when the current line
carries no same-line hint — collects up to 6 lines backward into a window, stopping at a blank line,
a bare `{`/`}`, or a line whose trailing `//` comment has been stripped and which still ends `;` (that
line completes the PREVIOUS, separate statement and is excluded from the window even if it itself
contains a UI call — `EquipmentPanel.cs:1227` is the proven case: `...FontLabel);   // never wraps
over the buttons` must not let a later, unrelated statement read as a continuation of it). The window
is checked as a whole, not stopped at the first hit in scan order, for two reasons discovered by
reading actual false positives mid-implementation, not by inspection:

- **Order-independent exclusion.** `BuildHudController.cs:494-500` is a `FlowTrace.Step("BuildHud",`
  call whose OWN opening line carries the exclusion marker, but the very next continuation line is
  diagnostic PROSE that happens to name a real method: `"... COMMON kit button
  ElarionUiKit.BuildObsidianButton(Style1,Yellow) seated ..."`. Scanning strictly backward-in-order,
  that prose line matched the UI-construction-call regex BEFORE the scan ever reached the opener line
  that would have excluded it, producing a false `ui-construction-call-continuation` on a pure
  diagnostic string. Collecting the whole window first, then checking `Test-ExcludedCSharpLine` across
  all of it before checking `Test-UiConstructionCallLine` across all of it, closes this regardless of
  which line the loop visits first.
- **Comment lines never count as evidence either way.** `HeroSkillTreePanelMvvm.cs:1877-1883` has a
  five-line `//` comment block directly above a ternary-assigned literal (`"COMING"`), and one comment
  line reads `"...never a label (HeroSkillTreeVM..."` — plain prose containing the substring `"label
  ("`, which trips the PRE-EXISTING `(?:\.Label|\bLabel)\s*\(` rule (PowerShell `-match` is
  case-insensitive by default) even though there is no real `Label(` call anywhere nearby. Comment
  lines (`^(?://|///|\*|/\*)`) are now skipped when building the window — walked past for boundary
  purposes, but never checked for a call or an exclusion. This only stops a comment from contaminating
  a DIFFERENT, later, non-comment statement's literal; it does not touch the pre-existing case where
  the literal itself sits ON a comment line (see Known limitations).

Both fixes were caught by reading actual sampled output mid-implementation (not anticipated in
advance), each reduced to a 4-line reproduction, verified to flip from wrong to right with an isolated
copy of the two functions, and re-verified against the original proven cases (`EquipmentPanel.cs:916`
continuation, `EquipmentPanel.cs:1227` terminated-boundary, bare `AddDockTab`) before the final full
run. That discipline is the only reason the final added-row count (632) differs from the first,
insufficiently-verified run (713) — 81 of the 713 were false continuations from these two bugs.

### Acceptance check — the 6 `AddDockTab` lines

**The exact literals no longer exist in the file.** Re-reading `HudKitController.cs:5555-5592` this
session (after this pass's own baseline run, so the drift is dated to mid-session, not to this fix)
shows all six `AddDockTab` calls now resolve through `new LocalizedText("...").Resolve()` —
`common.remnant_chat`, `common.leaderboard`, `hud.gearDock.music`, `settings.title`,
`hud.gearDock.realm`, `hud.gearDock.pause` — wired by a separate lane between this artifact's original
run and this session (visible in the recent commit log: WO-1859/WO-1861). This is independent, real
work, not a side effect of anything in this pass. Two of those keys' rows are present in
`manifest.json` as `keyedCall`/`migrated`, and the manifest correctly shows **zero** `literalCandidate`
rows at those six lines now — that is the correct, desired end state, not a detection failure.

Because the live example is gone, the detection MECHANISM was verified in isolation instead of on live
data, using the exact original literal shape from the artifact's own citation:
```
Test-UiConstructionCallLine('AddDockTab(_slideDock.panel, dockRow++, "Chat", OpenClanChat);') => True
```
The strongest live proof that the fix works end-to-end is `EquipmentPanel.cs:916`, which no same-line
detector could ever have produced: the fixed manifest now carries both `EQUIP` and `REMOVE` at that
line, via the multi-line lookback (`var action = ElarionUiKit.ButtonPack(_approvedDetailHost,` on line
915, the ternary literal on 916).

### Counts

Both runs were made on the SAME tree (same `git` state throughout this pass; no `.cs` file changed
during it) using the original (HEAD) scanner for the baseline and the fixed scanner for the after —
not the artifact's own original 6459/5812 figures, which were run earlier the same day and have since
drifted (see below).

| run | keyedEntry | keyedCall | literalCandidate | imageTextCandidate |
|---|---|---|---|---|
| baseline (original scanner, current tree) | 532 | 132 | 5814 | 16 |
| fixed (this pass) | 532 | 132 | 6446 | 16 |

`keyedEntry` 505→532 and `keyedCall` 126→132 (vs. the artifact's original figures) are tree drift from
WO-1859 (Remnant rename) and WO-1861 (pseudolocalization harness) landing between the artifact's run
and this baseline — not a scanner change. `keyedEntry`/`keyedCall`/`imageTextCandidate` are byte-identical
between baseline and fixed, confirming this fix touches only the unresolved-literal path and nothing
upstream of it (the `LocalText`/`LocalizedText` detection branches run first and are unmodified).

**632 new `literalCandidate` rows**, all under `Assets/_Modules/**` (0 from canonical JSON, which this
fix does not touch), **0 rows removed** (every baseline-matching line keeps its hint — the five
original patterns are unchanged substrings of the new detector). By surface: 356
`ui-construction-call` (same-line detection) + 276 `ui-construction-call-continuation` (the lookback).
By owner: Village 349, DevTools 80, HUD 69, Core 54, Onboarding 39, Dungeons 16, Settings 11,
GooglePlay 4, Wallet 4, BattleATB 3, Audio 2, DialogueUI 1.

One field-level side effect, checked for consumers before accepting it: the pre-existing `button` (178
rows), `label` (180), `status` (170), `modal` (45) and `toast` (36) `surface` values collapse into the
single `ui-construction-call` name for lines that would have matched either the old or new rule (they
now go through one shared function). No script under `tools/`, `.githooks/`, or
`Assets/Editor/Regression/` reads the manifest's `surface` field, and the current `manifest.json`
carries no `literalDebtBaseline` block (`Assert-LiteralDebtBaseline`'s fingerprint does not include
`surface` either), so nothing consumes the old names. Recorded here in case a future consumer is added
against the old vocabulary.

### Spot-check: 25 rows read at source, stratified by owner

Sampling rule fixed before reading any row: sort the 632 added rows by owner/source/line, take every
⌈N/k⌉-th row per owner, k = 10 for Village, 5 each for Core/HUD/DevTools (DevTools was added to the
sample beyond the original plan specifically because it is the second-largest owner bucket at 80 rows
and the artifact's own rule 6 flags developer surfaces as a known false-positive class — worth
measuring directly rather than assuming).

| file:line | literal | verdict | why |
|---|---|---|---|
| `ArenaAttackPaletteUI.cs:139` | `Cancel` | **genuine leak** | real button label via bare `AddButton(content.transform, "Cancel", ...)` |
| `BuildHudController.cs:732` | `ROTATE` | **genuine leak** | real button word via `MakeWordVerb(..., "ROTATE", ...)` |
| `HonestFeedbackPanel.cs:184` | `quest` | false positive | `medallionIcon: "quest"` — icon-concept identifier arg, not copy |
| `EquipmentPanel.cs:916` | `EQUIP` | **genuine leak** | ternary ButtonPack label, the proven multi-line case |
| `RaidDeployScreen.cs:409` | `RAID: ` | **genuine leak** (frag) | `"RAID: " + raidName` panel title, needs `LocalText.Format` |
| `RumorBoardPanel.cs:1029` | `RewardChip_Word` | false positive | `AddImage(row, "RewardChip_Word", ...)` — GameObject name arg |
| `HeroLoadoutPanelMvvm.cs:260` | `OPEN ` | **genuine leak** (frag) | `"OPEN " + HudStrings...` button label fragment |
| `RaidHudController.cs:568` | `ObjectiveFill` | false positive | `AddImage(objTrack.transform, "ObjectiveFill", ...)` — name arg |
| `ManageScreenPanel.cs:5011` | `BuildingChoice_` | false positive | `AddImage(row, "BuildingChoice_" + choice.Id, ...)` — name arg |
| `ManageScreenPanel.cs:6185` | `upgrade defense` | false positive | `Guard.Try("Manage", "upgrade defense", ...)` tag nested inside the same continuing `BuildObsidianButton` statement |
| `ManageWorkspacePanel.cs:1675` | `TileStatePlate` | false positive | `AddImage(cell, "TileStatePlate", ...)` — name arg |
| `ElarionUiKitDemo.cs:209` | `Low` | false positive | dropdown option INSIDE the kit's own demo scene — developer surface (artifact rule 6), never shipped |
| `LoadingOverlay.cs:238` | `Retry` | **genuine leak** | real fallback button label via `ButtonPack` |
| `OfflineOptInPanel.cs:137` | `Download everything now so the game works without a connection?...` | **genuine leak** | real modal body copy |
| `AdConsentPanel.cs:93` | `Echoes of Elarion can show optional ads...` | **genuine leak** | real ad-consent modal copy |
| `AdminOverlay.cs:474` | `Orient: id '{id}' not in CatalogRegistry...` | false positive | `SetStatus(...)` inside the DEV admin overlay — developer surface (rule 6) |
| `AdminOverlay.cs:834` | `Queue clock was already at real time - nothing to reset.` | false positive | same file, same reason |
| `DailyQuestHud.cs:421` | `In progress` | **genuine leak** | real `DetailCardRow` progress label |
| `HudKitController.cs:457` | `A wall` | **genuine leak** (frag) | real `ShowToast` message fragment |
| `HudKitController.cs:5406` | `settings` | false positive | `tabIconConcept: "settings"` — icon-concept identifier, not copy |
| `DevPanelController.cs:726` | `+100 Crystals` | false positive | dev-panel `AddButton` cheat label — developer surface (rule 6) |
| `DevPanelController.cs:750` | `Arcane Tower: cycle tier` | false positive | same file, same reason |
| `DevPanelController.cs:772` | `HP 50%` | false positive | same file, same reason |
| `DevPanelController.cs:836` | `ATBBattle` | false positive | same file, same reason |
| `DevPanelController.cs:904` | `Run cadence -0.1` | false positive | same file, same reason |

**10 genuine leaks / 15 false positives = 40% precision on this sample.** Materially lower than the
artifact's own measured ~88%, and the reason is structural, not a defect in this pass: the artifact's
high-confidence grep (§1a) matched a NARROW, hand-picked set of shapes chosen because they almost only
ever carry copy (`ElarionUiKit.(Label|Button*|BuildObsidian*|ShowToast|Caption)`, `AddDockTab`, `new
Label(`, `new Button(`, `SetStatus(`, `ShowToast(`). This pass deliberately matches the WHOLE kit
(every `ElarionUiKit.*` method, so it also catches `AddImage`/`GoldPerimeter`/layout-only calls whose
literal argument is a name, not copy) plus bare `AddButton` (which also lives in `DevPanelController.cs`,
a developer-only tool). That breadth is what surfaces the previously-invisible genuine leaks in the
table above (`Cancel`, `ROTATE`, `RAID: `, `OPEN `, `Retry`, `In progress`, `A wall`) — it also surfaces
two known false-positive classes at volume:

- **Developer-surface files (artifact rule 6) are not excluded by this scanner at all**, and this is
  PRE-EXISTING, not introduced by this pass: the baseline (before any of this session's changes) already
  carried 78 `literalCandidate` rows sourced from `Assets/_Modules/DevTools/*`, via the pre-existing
  `Button`/`Label`/named-word rules. This pass's `AddButton` detection adds 80 more from the same owner
  (all 5 sampled DevTools rows were false positives). **Deliberately not fixed in this pass**: excluding
  `/DevTools/` (or the artifact's full rule-6 file list — `/Diagnostics/`, `AdminOverlay.cs`,
  `ElarionUiKitDemo.cs`, `AutoPilot*`, `DevPanel*`, etc.) would also retroactively remove the 78
  pre-existing baseline rows and any other rule-6 rows already in the manifest, which is a broader,
  cross-cutting change than "recognise more UI-construction call shapes" and affects rows this ticket
  did not touch. Recommended as a separate, explicitly-scoped follow-up (a phase-1c precision pass),
  not folded in here.
- **Name/identifier arguments on kit calls whose OTHER argument is real copy** (`AddImage`'s `name`
  param, `medallionIcon`/`tabIconConcept` keyword args) — this is the artifact's own rule 9
  (PascalCase/identifier-shaped literal on a UI-construction line reads as a GameObject/config name, not
  copy) and rule 11 (identity/name/path/keyword field assignment). Rule 9/11 have never been implemented
  in this scanner's `Test-LikelyHumanText`, for ANY call shape — this is the same tradeoff the
  already-recognised `Button(`/`Label(` calls have always had (their own `name`/id arguments were
  already candidates for this before this pass). Widening the detector widens the surface this
  pre-existing gap touches; it does not create a new kind of gap.
- One residual case specific to the lookback: **`Guard.Try` tag/description arguments nested inside a
  still-open UI-construction statement** (`ManageScreenPanel.cs:6185`) are not excluded, because
  `Guard.Try(` is not in `Test-ExcludedCSharpLine`'s pattern list at all (a pre-existing gap — the
  artifact's own rule 5 names `Guard.Try` as an exclusion trigger, but the scanner's
  `Test-ExcludedCSharpLine` only checks `Debug.Log|FlowTrace.|Logger.|Console.`). Low volume; not fixed
  in this pass since it is a pre-existing gap on the exclusion side, not the detection side this ticket
  targets.

### Known limitations (recorded, not fixed — out of this ticket's scope)

- Fix 3 (comment-skip) only protects the LOOKBACK WINDOW. A literal that sits ON a comment line whose
  text itself resembles a UI-construction call (or a named-copy word) still emits — this is the
  artifact's own rule 1 (`false-positive-flowtrace-or-debug (comment text)`, 26.6% of the baseline) and
  is untouched by this pass.
- `[regex]::Replace($trimmed, '//.*$', '')` (statement-terminator detection) also strips from the first
  `//` inside a string literal (e.g. a URL like `"https://..."`), so such a line will not register as a
  terminator and the window can extend one statement further back than intended. Not observed in any
  sampled row; recorded as a theoretical gap.
- A multi-line UI call whose lambda argument body contains its own `;`-terminated statement (e.g.
  `BuildHudController.cs:707-731`, where the `MakeWordVerb(...)` call's `onClick` lambda has a
  multi-line `FlowTrace.Step(...)` call with its own semicolon) stops the lookback window at that
  interior semicolon. This is a false-NEGATIVE direction (a literal further above such a lambda could
  be missed by a line below it) and is considered safe to leave — it does not add false positives, and
  the existing per-line scan still catches same-line literals inside the lambda independently.
- `^\*` in the comment-skip regex (continuation lines of a `/* ... */` block comment, conventionally
  prefixed with `*`) would also skip a bare `*` multiplication-continuation code line in the rare case
  one exists inside the 6-line window. Not observed in any sampled row.

### Bottom line

The `AddDockTab`/`ElarionUiKit.ButtonPack`/`MakeWordVerb`/`BuildObsidianPanel`/`DetailCardRow`/
`AddButton` blind spot described in §1 is closed: the detector mechanism is proven (isolated test) to
recognise the original literal shape, and it surfaces 632 new candidate rows including real,
previously-invisible leaks (`EquipmentPanel.cs:916`'s `EQUIP`/`REMOVE` chief among them, since it
requires the multi-line lookback specifically). The tradeoff for closing the gap this broadly is a
lower headline precision (40% vs. the artifact's 88% on a much narrower net) driven by two
already-documented, pre-existing gaps this pass did not touch (developer-surface files, and
identifier-shaped name arguments) — both are called out here as the natural next phase, not silently
absorbed into this one's numbers.
