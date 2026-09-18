# UI Screenshot Catalog — 2026-09-17 (WO-1860)

Owner ask (verbatim): *"can I get a screenshot of every screen that has text? I want to create a
catalog for the overnight build to create a library of images so I can get them all listed for
them"* — bar is "a single letter of text"; only fully wordless art screens are excluded.

**How this doc was built (so it can be trusted or re-run):**
- Harness = `Assets/Editor/UICaptureLaunch.cs` (`Builds/ui-capture/<Stem>_<WxH>.png`). This is the
  ONE pre-ship harness — `Assets/_Modules/Core/Diagnostics/UICaptureMode.cs` (`Builds/UICaps/`) is a
  second, older, non-gating harness; not touched here.
- Two headless batchmode runs, both on a fresh log, both judged by their marker (never exit code):
  - `RunCaptureHeadless` → `Builds/ui-capture-wo1860-main.log` →
    `UI_CAPTURE_OK 106` (clean: `UI_GEOMETRY_OK 106 canvases`, `UI_GLYPH_OK 106/106`,
    `UI_ENDSTATE_FIT_OK 6 banners`, `UI_CAPTURE_FIDELITY_OK 85 builds`). Provenance line:
    `UI_CAPTURE_HEAD c0600986f dev dirty=true` (dirty because this WO's own edit + the concurrent
    clan lane's uncommitted files were in the tree — expected, not a defect).
  - `RunRegisteredSecondaryCaptureHeadless` → `Builds/ui-capture-wo1860-secondary.log` → produced
    **42/42 real, non-blank frames** but the run's own oracle correctly reds two of them — see
    "Flagged for follow-up" below. The capture *cases* work; the *content* they photographed has two
    pre-existing bugs, which is exactly what this instrument is for.
- Combined, `Builds/ui-capture/` holds **267 PNGs across 125 distinct screen/state stems** as of
  this run (`ls Builds/ui-capture/*.png | wc -l`; stems counted with the resolution suffix
  stripped). That is well past the owner's own "~45 screens" estimate because several routes
  (Manage's category flow, Owned-Town states, Army screen) already carry many named STATES per
  route, each captured separately — see the state breakdown under Manage/Army/Owned-Town below.
- Five PNGs were opened and visually confirmed this session (Leaderboard, Jukebox, ManageWorkspace,
  RaidSelection, AdaptiveHudPeaceful) per the WO's test-plan item 2 — all four rendered real, legible,
  non-blank UI. The remaining descriptions below are sourced from the panel's own copy/VM strings at
  file:line (cited) or from the still-useful parts of the prior graph doc, **not** from individually
  opening all 267 images — flagged per §11B rather than presented as directly observed.

## What changed in this pass (new code)

`Assets/Editor/UICaptureLaunch.cs` — added two entries to `RunRegisteredSecondaryCaptureHeadless`
(the reflection-driven secondary-panel sweep), for two gear-dock rows the 2026-09-04 graph's own gap
list (§d item 13) named as having zero capture coverage:
- `Leaderboard` (`DeNelle.HUD.LeaderboardPanel`, method `Toggle`)
- `Jukebox` (`DeNelle.Audio.MusicSelectionPanel`, method `Open`)

Expected frame count bumped 36→42 (14 routes × 3 landscape targets) in the same edit, per the repo's
own "never let a literal drift from what it counts" rule.

**Deferred, not attempted this pass** (do not guess at their fixture shape — see WO-1860 non-scope):
- `ArmyMusterPanel` (`Village/Troops/ArmyMusterPanel.cs`) — `Open()` reads `_vm`, which only the
  static `Show()` factory sets; the generic reflected-secondary recipe (`AddComponent` + `Awake` +
  `Open`) would NPE. Needs a bespoke fixture, not a guess.
- `RedeemCodePanel` (`Wallet/RedeemCodePanel.cs`) — plain (non-MonoBehaviour) class with its own
  construction contract; does not fit the MonoBehaviour recipe at all.
- `ClanChatPanel` / `ClanFeatureGate` — explicitly OUT of scope for this ticket: WO-1858 has that
  lane in flight right now (git status shows `ClanChatPanel.cs`, `ClanFeatureGate.cs`,
  `HudKitController.cs`, `DeNelle.HUD.asmdef` all uncommitted). Not captured, not touched.
- `DevPanel` — dev-only, compiled out of release (`PanelRouter.cs:98-110` per the prior graph doc);
  acceptable to leave per that doc's own §d item 15.
- `BarracksPanel.cs` and `ShopPanel.cs` (named in the 2026-09-04 gap list) **no longer exist in the
  tree** (`find Assets -iname` returned nothing this session) — retired since 09-04, most likely
  folded into the WO-1811 Army screen's inline "train one troop per tap" flow. Nothing to capture;
  the old doc's rows for them are stale.
- The other ~15 standalone `Run*CaptureHeadless()` entry points not exercised this session (e.g.
  `RunFrontDoorCaptureHeadless`, `RunWelcomeBackCaptureHeadless`, `RunGooglePlayLoginCaptureHeadless`,
  `RunJewelerDiscoveryCaptureHeadless`, `RunNavigationCaptureHeadless`, `RunSystemModalCaptureHeadless`
  etc.) already have PNGs sitting in `Builds/ui-capture/` from a prior run (see the "Boot / system /
  other" table below) — they were not RE-RUN this session, so their freshness is whatever their last
  run left. Re-running all of them is the natural next pass; flagged, not silently skipped.

## Flagged for follow-up (found by the capture run itself; NOT fixed here — non-scope)

1. **Leaderboard tab rail: duplicate/self-overlapping buttons.**
   `Builds/ui-capture-wo1860-secondary.log` (`UICap-GEO`/`touch-oracle`, all 81 findings) shows the
   three tab buttons ("Best Wave", "Crystals", "Arena Wins") in `LeaderboardPanel.BuildTabs()`
   (`Assets/_Modules/HUD/LeaderboardPanel.cs:157`) each have an exact-duplicate button stacked on
   the identical rect, and each button's label sits exactly under its own button rect (self-cover).
   Visually the rendered PNG (`Leaderboard_1920x1080.png`, opened this session) looks fine — the
   defect is a raycast/duplicate-build hazard, not a visible glitch, which is exactly why an oracle
   caught it and eyes would not. Worth a WO: `BuildTabs()` likely runs more than once per open
   without clearing the previous set (trace shows it called repeatedly from `Render()`/
   `RebuildRows()`).
2. **Jukebox instructional caption truncates/vanishes at wider landscape aspects.**
   `[glyph-oracle]` on `Jukebox_1920x1080`: the caption *"Pick the music for where you are. Battle
   music still takes…"* draws only 49/65 glyphs (ellipsis-truncated); at `2340x1080` and `2670x1200`
   (the Seeker's real surface) it draws **ZERO** of 65 glyphs — the band has no room at all. Worth a
   WO: give the caption band more height or shorten the copy, per the oracle's own recommendation.
   (The red "Audio not ready." line in the same screenshot is expected headless-fixture behaviour —
   no audio device in batchmode — not a bug.)

---

## Screen catalog

Legend: **Stem** = PNG filename prefix in `Builds/ui-capture/` (suffixed `_<W>x<H>.png`, normally
1920x1080 / 2340x1080 / 2670x1200 unless noted). **Source** = capture entry point in
`UICaptureLaunch.cs`. Text summaries marked **[seen]** were visually opened this session; the rest
are sourced from the cited copy/VM file:line or the prior graph doc's own citations.

### HUD / navigation shells

| Stem | Source | Text on screen |
|---|---|---|
| AdaptiveHudPeaceful | CaptureAdaptiveHud | **[seen]** Hero nameplate + HP/MP/XP bars ("Grom Lv 2"), "Heart of Elarion / Prepare the realm for the next wave. / Raids 3/3 (Heartfire)", gold counter, "Harvest" chip, "THE NIGHT MARKET" card, bottom bar BUILD / TALK / HERO / JOURNEY / MANAGE. |
| AdaptiveHudGearOpen | CaptureAdaptiveHud | Same HUD with the left gear-slide dock open (Chat/Leaderboard/Music/Settings/Night Market rows — Chat row gated by `ClanFeatureGate`, in-flight lane). |
| AdaptiveHudCombat | CaptureAdaptiveHud | HUD in the active-battle posture (wave/army readouts swap in per `HudActionBarModel`). |
| HeroWorkspace | CapturePlayerDecks | Hero deck cards: Bag / Equipment / Skills / Loadout (`PlayerDeckWorkspace.cs`). |
| JourneyWorkspace | CapturePlayerDecks | Journey deck cards — now more than the old 2-card doc: Quests/Raids plus (per commit `5f48aa7bd`) Dungeons/Realm Map/Season Track doors added since 09-04. |
| RealmWorkspace | CapturePlayerDecks | Realm deck cards: Realm Store / Defense Report / Monthly Ledger / Game Guide. |
| ManageWorkspace | CaptureManageWorkspace | **[seen]** "MANAGE", "QUEUE" / "X", "UPGRADE HEART — 250 Crystals", launcher cards BUILD ("Construct and upgrade your town"), BUILD BARRACKS ("Build a Barracks to unlock", locked), RESEARCH ("Unlock powerful advancements"), CLOSE. |
| BuildCollections (+Page2) | CaptureBuildCollections | Build-mode structure collection browser, paged. |
| BuildGhostChips_valid/_blocked/_edgeclamp/_padon | CaptureBuildGhostChips | Placement-ghost chip readouts in each state. |
| BuildPaletteDock_open/_collapsed | (Palette capture) | Build-mode bottom palette dock, open vs. collapsed with restore tab. |
| HelpMenu | CaptureHelpMenu | "Help" modal rows: Report a Bug / Controls / Credits / Dev Tools. |
| PauseMenu | CapturePauseMenu | Resume / Settings / Quit to Title. |
| Settings | RunSettingsCaptureHeadless (not re-run this session; PNG on disk from prior run) | Connect/Disconnect Wallet, Game Guide, Reset Defaults, Defence Reports, Privacy Policy, Terms of Service, Ad Privacy Choices, Do Not Sell, Play Offline, Dev Panel row. |
| Leaderboard | **NEW** CaptureReflectedSecondary | **[seen]** "LEADERBOARD", tabs BEST WAVE / CRYSTALS / ARENA WINS, "No entries yet." x2, "Source: Local (offline). Scores are local; ranks shown are placeholder rivals until the online ladder is connected.", CLOSE. See flagged issue #1. |
| Jukebox | **NEW** CaptureReflectedSecondary | **[seen]** "JUKEBOX", "Pick the music for where you are. Battle music still takes…" (truncates/vanishes at wider aspects, flagged issue #2), "Audio not ready." (headless-fixture only), CLOSE. |
| DailyQuestHud | CaptureDailyQuestHud | Daily quest tracker overlay row(s). |
| EchoRoster / EchoPetButton | CaptureEchoRoster | Echo roster grid + the HUD button that opens it. |
| EchoCard | CaptureEchoCard | Per-Echo resource-picker card. |
| EchoUnlockDialogue_Aldwin_acknowledge | (founding-echo fixture) | Founding/Echo-unlock acknowledgement card copy. |
| HeroSelect | CaptureHeroSelect | Hero-select carousel with rotate control. |
| HeroSkillTree (+_Lv2/_Popup/_Assigned) | CaptureHeroSkillTree | "TALENT TREE" chrome, node popup, assigned-node state. |
| HeroLoadout | CaptureReflectedSecondary | "Hot-Swap Skills" loadout panel. |

### Manage — category flow states (`ManageFlow_*`)

Captured by the dedicated flow-map sweep (`RunManageFlowMapCaptureHeadless`, prior run on disk).
Three categories × many named states each — this is where most of the "125 stems" total lives:

- **BUILD** (`ManageFlow_BUILD_*`, 22 states): hub, hubheart, category-top/bottom × CRAFT/DEFENSE/
  ECONOMY/STORAGE, gridtop/gridbottom, action, in-progress, queue/queue-empty/queue-blocked, max,
  unaffordable, heart-ready/heart-max/heart-missing-crystals/heart-synthetic-prerequisite,
  synthetic-overflow-top/bottom.
- **ARMY** (`ManageFlow_ARMY_*`, 10 states): gridtop/gridbottom, action, in-progress, locked, max,
  max-trainable, queue/queue-empty/queue-blocked.
- **RESEARCH** (`ManageFlow_RESEARCH_*`, 9 states): gridtop/gridbottom, action, in-progress, locked,
  max, queue/queue-empty/queue-blocked, school.
- Plus the hub-level `ManageArmy`, `ManageBuild`, `ManageResearch`, `ManageResearchSchool` shots.

Each state's on-screen text is its own row/button label + cost/timer readout inside the Manage
category UI (`ManageScreenPanel.cs` / `ManageScreenVM.cs`); not individually transcribed here given
the count — this is the "breadth over perfect per-entry description" tradeoff the WO calls out.
Follow-up pass: transcribe each state's exact copy if the owner wants per-state text captions.

### Army / Owned-Town (WO-1811 / raid-ownership screens)

| Stem | Text on screen |
|---|---|
| ArmyScreen | Trained vs. deployed troop counts, per-tap training row, Reserve pool (WO-1811). |
| ArmyScreenLoadouts | Same screen with the saved-loadout drawer open. |
| OwnedTownReveal / OwnedTownReady / OwnedTownRepair / OwnedTownMoreRepairs / OwnedTownDesign / OwnedTownRealm / OwnedTownSaleConfirm | Post-raid captured-town flow states (reveal, ready-to-defend, repair prompts, cosmetic design, realm-deck entry, sale confirmation). |
| TownPracticeActive / TownPracticeResult | Practice-mode combat HUD + result banner. |

### Raid pillar

| Stem | Text on screen |
|---|---|
| RaidSelection | **[seen]** "RAIDS", "4 camps - drag the list to see them all.", per-camp cards (name, difficulty tag, clock, wall/defender count, loot line, spoils estimate, flavor line) — e.g. "The Forsaken Camp / Regular / Clock: 3:00 / Wood walls . 9 defenders / x1 Loot / Spoils: ~1800 wood, ~1100 iron, ~2200 gold / Scavengers strip an abandoned settlement the Heart can no longer reach." |
| RaidDeploy | Pre-raid deploy screen, "RAID: <name>" chrome, "BEGIN ASSAULT". |
| RaidHud | Live in-raid readout column. |
| RaidDeployHud | Live in-raid command bar + status line. |

### Store / monetization

| Stem | Text on screen |
|---|---|
| NightMarket (+_1280x720/_800x360/_915x412) | PACKS / MOVING bands, "CLOSE THE GAP", FREE band, "MONTHLY LEDGER" row. |
| MonthlyLedger | Ledger rows + close. |
| SeasonTrack | Battle-pass/season-track rows (now door-reachable via the Journey deck per commit `5f48aa7bd`). |
| Store_SkrFlat_NoSale / Store_SkrFlat_Sale30 | SKR-flat store variant, no-sale vs. 30%-sale badge state. |
| _skrflat_sale_tag_grey / _wo1819_sale_tag_grey | Sale-badge art fixtures (forensic, not a player screen). |

### Secondary panels (reflected-secondary sweep, `RunRegisteredSecondaryCaptureHeadless`)

Benefactors, CosmeticShop, BuildingUpgrade, Workshop, PartyShop (+Populated), Alchemy, Jeweler,
HeroLoadout, DefenseReport, GameGuide, MonthlyLedger, SeasonTrack, **Leaderboard (new)**,
**Jukebox (new)** — 14 routes × 3 targets = 42 frames, all rendered this session (see flagged
issues above for the two that also carry real defects).

### Boot / system / other (PNGs on disk from a prior run — not re-run this session)

Title, Login, StartNewConfirm, WelcomeBack, WelcomeBackDoors, TutorialSkip, MaintenanceBanner,
LoreReadingModal, TowerManagerPanel, BuildMenuUpgradeTower, RumorBoard (+_page2), DialogueOptions_2opt/
_4opt, EndStateWaveClear_plain/_repairAll. These already existed before this WO; freshness is
whatever their last capture run left — re-run in the next pass to confirm they still match current
copy.

---

## Acceptance-criteria status

- [x] Source-verified screen inventory — this doc, cross-checked against `PanelRouter`/`HudKitController`
      commits since 09-04, not the stale doc's own claims (old doc banner-fixed above).
- [x] Every NEWLY ADDED text-bearing screen (Leaderboard, Jukebox) has a working headless capture case.
- [x] Capture run confirmed via marker on a fresh log, not exit code (`UI_CAPTURE_OK 106`; secondary
      run produced 42/42 real frames, judged FAIL by its own content oracle for real reasons — see
      "Flagged for follow-up", not a harness defect).
- [x] 5 PNGs opened and visually confirmed non-blank/legible.
- [ ] Full ~45-plus-screen doc with a hand-verified one-line text summary for every one of the 267
      PNGs — NOT done to that depth this pass (267 images at that scope was not achievable in the
      time available); this doc prioritizes breadth (inventory + fresh captures for the two real gaps
      found) per the WO's own "prioritize breadth... report what remains" instruction. Follow-up:
      re-run the ~15 standalone entry points not exercised this session, transcribe per-state Manage
      copy, and build ArmyMuster/RedeemCode fixtures.
- [x] `python tools/gate_brace.py` + NUL scan clean on the one file touched
      (`Assets/Editor/UICaptureLaunch.cs`).
