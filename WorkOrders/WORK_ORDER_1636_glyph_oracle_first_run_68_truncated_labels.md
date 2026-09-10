# WO-1636 - 68 labels are cut mid-word across 21 captured panel builds (the first glyph-oracle run)

**Status:** FIXED 2026-09-10 - 68 findings -> **0 NEW**, proven on fresh logs read at source: `Builds/wave5-capture4` `UI_GLYPH_OK 97/97 panels labels=902 baselined=2 unproved=0`, `Builds/wave5-navcapture4` `UI_GLYPH_OK 15/15 panels labels=144 baselined=1 unproved=0`, `Builds/wave5-compile3` `COMPILE_GATE_OK`; ZERO `TEXT TRUNCATED` lines and no `ManageWorkspace` finding on either capture. 66 fixed, **2 accepted as debt** - `EndStateWaveClear_repairAll_1920x1080|.../SpoilCell2/SpoilRow/Label|17 of 19` (the CAPTURE FIXTURE is the defect; nothing shipped renders that shape) and `RealmWorkspace_1920x1080|.../DeckCard_The Night Market/Label|12 of 14` (a PlayerDeck TITLE, lane C scope) - the only two rows left in `GlyphBaseline` (`Assets/Editor/UICaptureLaunch.cs:6174-6179`); the nav run's `baselined=1` is the Realm row again, not a third. Manage ARMY closed by owner ruling "BUILD BARRACKS" + a two-line band; `ManageWorkspace_2670x1200.png` reads BUILD / BARRACKS whole. RESULT: `WorkOrders/WORK_ORDER_1636_glyph_oracle_first_run_68_truncated_labels.RESULT.md` (`## CLOSE-OUT 2026-09-10`). PO felt-verifies + closes (§13). (was: PARTIALLY IMPLEMENTED 2026-09-10 - 64 of 68 cleared on wave3-capture10 (baselined 68 -> 4): NightMarket 21, RumorBoard 21, DeckCardPurpose 16, HeroSelect 5, BuildMenu 1; the 4 left are Manage ARMY x2 (copy ruling: BUILD BARRACKS fits), EndStateWaveClear x1 (stale capture fixture) and Realm DeckCard_The Night Market x1 (PlayerDeck title, lane C scope); GlyphBaseline shrink to the 4 is the next cleanup (was: PARTIALLY IMPLEMENTED 2026-09-10 - RumorBoard (21), DeckCardPurpose (16), HeroSelect (5) cleared on wave3-capture9 (baselined 68 -> 26 matched); NightMarket (21) and BuildMenu (1) fixes are gated separately; Manage ARMY (2) needs a copy ruling ('BUILD BARRACKS' fits); EndStateWaveClear (1) is a stale capture fixture (was: READY TO IMPLEMENT)))
**Minted:** 2026-09-10 (lane GLYPH-ORACLE, main-line banner; number pre-assigned by the lead, banner bumped 1636 -> 1637 in the SAME edit)
**Silo / Lane:** UI copy + kit fit, per panel family. NOT the oracle - WO-1630 built the detector; this is the debt it found.
**Severity:** P2 player-visible. Every line below is a caption the player reads with its last word missing. `NightMarket` is the money screen.
**Type:** EXISTING. 21 panel builds already ship this way; nothing here is a new feature.
**Raised by:** the first live run of `LayoutOracle` Assert C (WO-1630), `Builds/wave3-capture2`.
**Owns:** the `GlyphBaseline` entries in `Assets/Editor/UICaptureLaunch.cs`. **Each sub-fix DELETES its own entries in the same commit** - that list is shrink-only and this ticket is why it exists.

---

## 1. The measurement (one run, read at source; nothing here is inferred)

`Builds/wave3-capture2` - **91 panel builds measured, 876 labels read, 0 unproved, 68 truncations
over 21 panel builds** (70 panel builds clean). The screenshot marker on the same log stayed green at
91 frames: **every one of these shipped past a clean geometry run**, because every other rule on that
path measures WHERE a rect is, never whether the words inside it survived.

A "truncation" here is measured, not guessed: TMP's generated mesh drew fewer visible glyphs than the
printable (non-whitespace) characters of the string the label was given, after `ForceMeshUpdate`.

### ✅ ALL 68 ARE NOW LISTED. The 8 that the first run could not print were READ, not inferred.

The seeding run printed only 60 of its 68 finding lines - `GeoMaxPrintedLines` caps PRINTED lines,
the per-panel tally is uncapped and read 68, and the run's own trailing line said `... and 8 more`.
Rather than raise the cap, the **navigation** capture was run over exactly the workspaces the tally
named: `Builds/wave3-navcapture`, **15 panel builds, 144 labels, 19 findings of which 11 were already
listed here** - so the 8 new ones are precisely the 8 the full run could not print. Two independent
runs agreeing on the same 8 is the corroboration; nothing here is derived from a subtraction.

⛔ **THE CAP WAS NOT RAISED AND MUST NOT BE.** Widening the reading closed the gap; widening the cap
would only move the ceiling for the next run. If a future run trips the cap again, run a narrower
entry point - do not touch `GeoMaxPrintedLines`.

The 8, all in the `RealmWorkspace` / `JourneyWorkspace` / `ManageWorkspace` deck-card family, are
tabled with the rest in sec.5 and are flagged `(nav capture)` in `GlyphBaseline`.

## 2. The two families, and the fix shape for each

The 68 measured findings fall into exactly two authoring shapes. Both were read off the runs' own
`overflow=` / `wrap=` fields, not assumed.

| family | count | what it is | fix shape |
|---|---|---|---|
| `overflow=Ellipsis wrap=NoWrap` | **52** | a SINGLE-LINE label fitted to one line; TMP swaps the tail for an ellipsis | give the band the width it needs in **reference px**, or shorten the copy. If the label is authored by hand, route it through the kit's single-line fit so the floor is the kit's, not a local literal. |
| `overflow=Truncate wrap=Normal` | **16** | a MULTI-LINE block (`FitBlock`); the band is too SHORT, so whole lines are dropped | give the block **height in px** for the number of lines the copy actually needs. The deck-card `DeckCardPurpose_*` label is the whole of this family, across `RealmWorkspace` (12) and `JourneyWorkspace` (4) - **one shared authoring site, so one fix should clear all 16.** |

⛔ **NEVER LOWER A FONT BELOW THE KIT FLOOR TO MAKE COPY FIT.** `ElarionUiKit.FontFloor` /
`FontHardFloor` are the floor, and the oracle's own finding line says why: lowering it trades one
unreadable caption for another. Fix the band or fix the copy.

⛔ **DO NOT TOUCH `LayoutOracle.cs`, `UICaptureLaunch.cs`'s Assert C routing, or
`UiTouchClampRegression.cs`.** WO-1630 owns the detector. The only edit this ticket makes to
`UICaptureLaunch.cs` is DELETING `GlyphBaseline` entries as each panel family is fixed.

## 3. Suggested sub-lane split (file-disjoint)

| lane | panels | findings | notes |
|---|---|---|---|
| A | `RumorBoard` + `RumorBoard_page2` | 21 | biggest single win; the poster hook copy is 49-62 chars into a one-line band |
| B | `NightMarket` | 21 | **the money screen** - do this one with eyes on the PNGs |
| C | `RealmWorkspace` (13) + `JourneyWorkspace` (4) | 17 | the `FitBlock` family: 16 are the shared `DeckCardPurpose_*` band HEIGHT, 1 is a single-line label |
| D | `HeroSelect` | 5 | short strings in narrow chips |
| E | `BuildMenuUpgradeTower`, `EndStateWaveClear_repairAll` | 2 | one each, both action-band button labels |
| F | `ManageWorkspace` | 2 | `ManageCard_ARMY/Label` - "BUILD A BARRACKS" at 11 of 14, identical at both aspects |

## 4. Acceptance

1. ~~**Step 0:** read the 8 unprinted findings.~~ **DONE 2026-09-10** - `Builds/wave3-navcapture`
   measured them, all 8 are in `GlyphBaseline` flagged `(nav capture)` and tabled in sec.5, and the
   gap note in that list's header is deleted. `GlyphBaseline` now holds **68**.
2. Each sub-lane's fix **deletes its own `GlyphBaseline` entries in the same commit**. A fix that
   leaves its entry standing has not been proved.
3. A fresh capture after each lane shows `UI_GLYPH_OK` with `baselined=` reduced by exactly the number
   of entries that lane deleted, and `UI_GLYPH_FAIL` on nothing new.
4. No font floor was lowered anywhere. State the band change in px per label.
5. The PNGs are opened. A glyph count is a number; the owner is colourblind and reads words.

## 5. All 68 measured findings, grouped by panel build

Counts are **drawn of printable**. `font` is the resolved size with its autosize band.

### `BuildMenuUpgradeTower_1920x1080` - 1

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelContent/Zone_Body/ActionBand/ObsBtn_Not enough resources/Label` | "NOT ENOUGH RESOURCES" | **16 of 18** | 30 [30..44, enabled=True] | Ellipsis / NoWrap |

### `RumorBoard_1920x1080` - 4

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_daily_claimable/Body/PosterHook` | "Done: Clear 3 waves at the western gate." | **24 of 33** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_daily_claimable/Body/RewardRow/RewardChip_Word/Fill/Label` | "A found item" | **4 of 10** | 24 [24..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_underway/Body/PosterHook` | "Carry the sealed ledger past the flooded stai..." | **27 of 62** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_longest/Body/PosterHook` | "Brom unfolds a letter soaked through and drie..." | **27 of 49** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `RumorBoard_page2_1920x1080` - 3

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_watch1/Body/PosterHook` | "Hold the western fields until the lantern war..." | **29 of 61** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_watch2/Body/PosterHook` | "Hold the western fields until the lantern war..." | **29 of 61** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_avail1/Body/PosterHook` | "Track down why the first bell rings with nobo..." | **28 of 60** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `RumorBoard_2340x1080` - 4

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_daily_claimable/Body/PosterHook` | "Done: Clear 3 waves at the western gate." | **28 of 33** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_daily_claimable/Body/RewardRow/RewardChip_Word/Fill/Label` | "A found item" | **5 of 10** | 24 [24..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_underway/Body/PosterHook` | "Carry the sealed ledger past the flooded stai..." | **31 of 62** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_longest/Body/PosterHook` | "Brom unfolds a letter soaked through and drie..." | **30 of 49** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `RumorBoard_page2_2340x1080` - 3

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_watch1/Body/PosterHook` | "Hold the western fields until the lantern war..." | **32 of 61** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_watch2/Body/PosterHook` | "Hold the western fields until the lantern war..." | **32 of 61** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_avail1/Body/PosterHook` | "Track down why the first bell rings with nobo..." | **30 of 60** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `RumorBoard_2670x1200` - 4

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_daily_claimable/Body/PosterHook` | "Done: Clear 3 waves at the western gate." | **28 of 33** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_daily_claimable/Body/RewardRow/RewardChip_Word/Fill/Label` | "A found item" | **5 of 10** | 24 [24..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_underway/Body/PosterHook` | "Carry the sealed ledger past the flooded stai..." | **31 of 62** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_longest/Body/PosterHook` | "Brom unfolds a letter soaked through and drie..." | **30 of 49** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `RumorBoard_page2_2670x1200` - 3

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_watch1/Body/PosterHook` | "Hold the western fields until the lantern war..." | **33 of 61** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_watch2/Body/PosterHook` | "Hold the western fields until the lantern war..." | **33 of 61** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/PosterRow/Poster_uicap_rumor_avail1/Body/PosterHook` | "Track down why the first bell rings with nobo..." | **30 of 60** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `NightMarket_800x360` - 6

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/NightMarket/TopBar/Text` | "Connect a wallet to buy - prices shown in USD" | **32 of 36** | 30 [30..30, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-wood/Text` | "4,000" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-iron/Text` | "2,000" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-crystals/Text` | "400" | **ZERO of 3** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-stone/Text` | "1,500" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-coins/Text` | "600" | **ZERO of 3** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `NightMarket_915x412` - 6

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/NightMarket/TopBar/Text` | "Connect a wallet to buy - prices shown in USD" | **32 of 36** | 30 [30..30, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-wood/Text` | "4,000" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-iron/Text` | "2,000" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-crystals/Text` | "400" | **ZERO of 3** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-stone/Text` | "1,500" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-coins/Text` | "600" | **ZERO of 3** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `NightMarket_1280x720` - 3

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/NightMarket/TopBar/Text` | "Connect a wallet to buy - prices shown in USD" | **28 of 36** | 30 [30..30, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Commerce/LandscapeActions/Scroll/Content/utility-row-MONTHLY LEDGER/ObsBtn_MONTHLY LEDGER/Label` | "MONTHLY LEDGER" | **12 of 13** | 28 [28..28, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/BottomBand/CommerceCta/ObsBtn_Connect Wallet/Label` | "CONNECT WALLET" | **12 of 13** | 30 [30..38, enabled=True] | Ellipsis / NoWrap |

### `NightMarket_2670x1200` - 6

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/NightMarket/TopBar/Text` | "Connect a wallet to buy - prices shown in USD" | **32 of 36** | 30 [30..30, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-wood/Text` | "4,000" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-iron/Text` | "2,000" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-crystals/Text` | "400" | **ZERO of 3** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-stone/Text` | "1,500" | **ZERO of 5** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/NightMarket/Body/Spotlight/ledger-coins/Text` | "600" | **ZERO of 3** | 30 [30..32, enabled=True] | Ellipsis / NoWrap |

### `EndStateWaveClear_repairAll_1920x1080` - 1

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelContent/Zone_Body/Zone_RewardWell/Band/SpoilCell2/SpoilRow/Label` | "DESTROYED, looted 120" | **17 of 19** | 30 [30..50, enabled=True] | Ellipsis / NoWrap |

### `HeroSelect_1080x1920` - 5

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelContent/HeroStageWell/DetailsStrip/Col_Signature/Label` | "Shield Bash" | **7 of 10** | 25 [25..50, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelContent/HeroStageWell/DetailsStrip/Col_Skills/Label` | "Sword Heroic" | **7 of 11** | 20 [20..40, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelContent/HeroStageWell/DetailsStrip/Col_Skills/Label` | "Shield Bash" | **8 of 10** | 20 [20..40, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelContent/HeroStageWell/DetailsStrip/Col_Skills/Label` | "Warden's Grace" | **8 of 13** | 20 [20..40, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelContent/HeroStageWell/DetailsStrip/Col_Skills/Label` | "Radiant Strike" | **8 of 13** | 20 [20..40, enabled=True] | Ellipsis / NoWrap |

### `RealmWorkspace_1920x1080` - 5

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_The Night Market/Label` | "THE NIGHT MARKET" | **12 of 14** | 30 [30..40, enabled=True] | Ellipsis / NoWrap |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_The Night Market/DeckCardPurpose_The Night Market` | "Browse clearly priced realm offers" | **21 of 30** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Defense Report/DeckCardPurpose_Defense Report` | "Review attacks against your town" | **20 of 28** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Monthly Ledger/DeckCardPurpose_Monthly Ledger` | "Review non-expiring monthly progress" | **20 of 33** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Game Guide/DeckCardPurpose_Game Guide` | "Read controls, systems, and help" | **21 of 28** | 30 [30..34, enabled=True] | Truncate / Normal |

### `RealmWorkspace_2340x1080` - 4

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_The Night Market/DeckCardPurpose_The Night Market` | "Browse clearly priced realm offers" | **23 of 30** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Defense Report/DeckCardPurpose_Defense Report` | "Review attacks against your town" | **23 of 28** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Monthly Ledger/DeckCardPurpose_Monthly Ledger` | "Review non-expiring monthly progress" | **23 of 33** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Game Guide/DeckCardPurpose_Game Guide` | "Read controls, systems, and help" | **23 of 28** | 30 [30..34, enabled=True] | Truncate / Normal |

### `RealmWorkspace_2670x1200` - 4 (2 from the full capture, 2 from the navigation one)

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_The Night Market/DeckCardPurpose_The Night Market` | "Browse clearly priced realm offers" | **24 of 30** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Defense Report/DeckCardPurpose_Defense Report` | "Review attacks against your town" | **23 of 28** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Monthly Ledger/DeckCardPurpose_Monthly Ledger` | "Review non-expiring monthly progress" | **23 of 33** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_Game Guide/DeckCardPurpose_Game Guide` | "Read controls, systems, and help" | **23 of 28** | 30 [30..34, enabled=True] | Truncate / Normal |

### `JourneyWorkspace_1920x1080` - 2  *(navigation capture)*

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/Zone_Body/JourneyCardGrid/DeckCard_Quests/DeckCardPurpose_Quests` | "0 active . 0 ready to claim" | **20 of 21** | 30 [30..34, enabled=True] | Truncate / Normal |
| `ObsidianPanel/PanelFill/Zone_Body/JourneyCardGrid/DeckCard_Raids/DeckCardPurpose_Raids` | "Army 0 / 10 . train to open a camp" | **18 of 25** | 30 [30..34, enabled=True] | Truncate / Normal |

### `JourneyWorkspace_2340x1080` - 1  *(navigation capture)*

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/Zone_Body/JourneyCardGrid/DeckCard_Raids/DeckCardPurpose_Raids` | "Army 0 / 10 . train to open a camp" | **20 of 25** | 30 [30..34, enabled=True] | Truncate / Normal |

### `JourneyWorkspace_2670x1200` - 1  *(navigation capture)*

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelFill/Zone_Body/JourneyCardGrid/DeckCard_Raids/DeckCardPurpose_Raids` | "Army 0 / 10 . train to open a camp" | **20 of 25** | 30 [30..34, enabled=True] | Truncate / Normal |

### `ManageWorkspace_2340x1080` - 1  *(navigation capture)*

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageCategoryGrid/ManageCard_ARMY/Label` | "BUILD A BARRACKS" | **11 of 14** | 30 [30..40, enabled=True] | Ellipsis / NoWrap |

### `ManageWorkspace_2670x1200` - 1  *(navigation capture)*

| label (hierarchy path) | copy | drawn/printable | font | overflow / wrap |
|---|---|---|---|---|
| `ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageCategoryGrid/ManageCard_ARMY/Label` | "BUILD A BARRACKS" | **11 of 14** | 30 [30..40, enabled=True] | Ellipsis / NoWrap |

---

### OWNER RULING 2026-09-10

> **"Manage ARMY copy: BUILD BARRACKS, keep the cook fire"** - owner, verbatim, 2026-09-10 (morning).

This is the ruling the RESULT's `SUB-LANE Remainder` sec.3 asked for. It closes the **Manage ARMY**
pair - the last two of this ticket's 68 that were blocked on a decision rather than on work.

**What it settles.** The locked ARMY launcher card's face copy drops the article:
`"BUILD A BARRACKS"` -> **`"BUILD BARRACKS"`**. Sec.3 named this "the cheapest ruling on offer" and
declined to take it, correctly - the words are player-facing copy behind an owner ruling, and taking
them meant re-pointing a pin. Both halves are now done:

| | before | after | authority |
|---|---|---|---|
| the face literal | `"BUILD A BARRACKS"` | `"BUILD BARRACKS"` | the `faceText` assignment in `RenderLauncherCards`, `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs`, with the ruling quoted at the site |
| the WO-1406 pin | required the bare words `BUILD A BARRACKS` | requires the **code shape** `"BUILD BARRACKS" : title`, and FORBIDS `"BUILD A BARRACKS" : title` | `Assets/Editor/Regression/ManageApprovedLauncherRegression.cs` |

⛔ **The PURPOSE line is NOT touched.** `"Build a Barracks to unlock"` (the wrapped sentence under the
card art) keeps its article - a different string in a different band, it never truncated, and it is
pinned by **two** suites (`ManageApprovedLauncherRegression`'s approved-copy list and
`ManageProgressiveDisclosureRegression`). Only the single-line CTA face moved.

**The pin was re-pointed on the CODE SHAPE, deliberately.** `ManageScreenPanel.cs` carries BOTH strings
inside its own WO-1636 reasoning comments - the measurement record quotes the old copy, the ruling block
quotes the new one - so a `Contains("BUILD BARRACKS")` source-text pin would have passed **before** the
edit and would keep passing **after** a revert: a pin that cannot fail. `"BUILD BARRACKS" : title` is
the assignment itself and prose cannot carry it. Verified against the edited file this session:
new form **1** hit, old form **0**, purpose line still present, retired toast literal still **0**.

⚠ **NOT YET PROVEN, and that is a finding, not a formality.** `~324 px` (old) and `~293 px` (new) are
**glyph-advance ESTIMATES** calibrated against the oracle's own ARMY rect - the oracle logs a rect only
for labels it FAILS, so no run has measured either string in this lane. **The next capture is the
proof.** Per acceptance item 2 the two `GlyphBaseline` entries STAY standing until then, and are deleted
in the same commit as that capture.

---

### ROUND 2, 2026-09-10 - THE CAPTURE RAN AND THE ESTIMATE WAS WRONG BY ONE GLYPH

**`Builds/wave4-capture3`, measured on the ruled copy:**

```
[glyph-oracle] TEXT TRUNCATED [ManageWorkspace_2340x1080]
  '.../ManageCard_ARMY/Label' ("BUILD BARRACKS") draws 12 of 13 printable glyphs.
  (x -150.4..150.4, y -141.5..-81.4) at font 30 [autosize 30..40]
[glyph-oracle] TEXT TRUNCATED [ManageWorkspace_2670x1200]  (x -146.9..146.9, 12 of 13)
```

⛔ **The copy is NOT the problem and does not move again.** "BUILD BARRACKS" is the owner's ruling and
stays; the font stays at `ElarionUiKit.FontFloor`. **The ESTIMATE was the problem** - it read ~293 px
against a ~301 px lane and the string is actually over **300.8 px**, so it never fit at either aspect.
An estimate calibrated off one rect was traded against a real constraint; that is the same error this
ticket's RESULT sec.2 already recorded once, in a different panel, in its own words.

**Every single-line lever is spent, measured not asserted:** the copy is a ruling; the font is at the
floor; the side inset is already at the gold perimeter (0.02/0.98 = **0.96** of the cell); and the cell
is HEIGHT-clamped by `HubCardAspect`, so the band cannot widen. **So the SHAPE changes: the locked ARMY
face becomes a two-line block** - the WO-1623/1628 wrap shape.

**The string is NOT re-authored with a line break.** The band is made two lines tall and the block
fitter wraps at the copy's own space, so the literal stays exactly `"BUILD BARRACKS"` and the
WO-1406 pin keeps reading it.

#### The arithmetic, derived from the two rects above - no new estimate

| | 2340x1080 | 2670x1200 |
|---|---|---|
| face lane (measured) | 300.8 px | **293.8 px** (the tighter one) |
| cell, back-solved at 0.96 | 313.3 x 401.1 | 306.0 x 391.8 |
| title band today | **60.1 px measured** (`y -141.5..-81.4`) vs `0.15 x 401.1 = 60.17` derived | 58.8 derived |
| new two-line face band, `(1-HubArtWellF) - (0.02+HubDescBandF)` = 0.19 | **76.2 px** | **74.4 px** |

The band's height model is **cross-checked, not assumed**: the measured 60.1 px title band and the
derived `0.15 x 401.1 = 60.17` agree to a tenth of a pixel, which is what proves the 401.1 px cell.

**Width, from the same rects.** The full string exceeds 300.8 px, so with a space at ~0.47 of a cap the
bold-caps advance is `c > 300.8 / 13.47 = 22.3 px`; the 12 glyphs that DID draw cap it at `c <= ~23.1`.
The longest wrapped line is **`BARRACKS` = 8c = 179..185 px** against the **293.8 px** lane at the
tighter aspect - **~59% headroom**, against the ~2% deficit one line was losing by. `BUILD` = 5c ~ 115 px.

**Height, from a band that is PROVEN rather than computed.** 0.19 is byte-for-byte `HubDescBandF`, the
description band that is sized for two lines at the mobile floor, uses the same block fitter at the same
floor, and which **this same capture does not flag on any of the three cards at either aspect**. A
fraction that demonstrably seats two floor lines on this card seats them for this caption. It also
clears this file's own two-line threshold, `2 x ElarionUi.FontFloorMobile = 60 px`, by +27% / +24%.

#### What changed in code

| site (line as it is NOW) | change |
|---|---|
| `ManageScreenPanel.cs:2371-2379` | `faceNeedsTwoLines` (locked ARMY only) selects the face rect: two-line band spans art-well-bottom `1f - HubArtWellF` down to description-top `0.02f + HubDescBandF`; every other face keeps today's `HubTitleBandF` band **unchanged** |
| `ManageScreenPanel.cs:2388-2389` | `FitBlock(face, 30f, 40f)` for that one caption; `FitSingleLine(face, 30f, 40f)` **kept verbatim** for the rest |
| `ManageScreenPanel.cs:2150-2155` | new px guard, sibling to the description's at `:2143`: WARNs if the two-line face band drops under `2 x ElarionUi.FontFloorMobile`, so a shrinking card SAYS so instead of cutting `BARRACKS` |
| `ManageScreenPanel.cs` (ruling block) | the "~293 px fits" estimate is corrected in place rather than deleted - the lesson is why the fix is sized off a proven band |

⛔ **A HAZARD I INTRODUCED AND THEN REMOVED - worth recording, because it is this repo's own recurring
bug.** `HudLabelFitRegression.ArgsOf` (`:1133-1141`) takes the **first `IndexOf`** of its anchor in the
whole source and reads to the next terminator; **it has no comment model.** My first draft quoted the
anchors (`face.fontSize`, `face.alignment`, the single-line fit call) inside explanatory comments
*above* the statements, which would have made three deck/Manage parity cases parse prose instead of
values. The comments were reworded to name none of them. Simulated with the real algorithm against the
edited file, all four anchors resolve to what they resolved to before: `36f`,
`TextAlignmentOptions.Center`, `30f, 40f`, `ElarionUi.FontFloorMobile, 34f`.

#### Expected on the next capture

`ManageCard_ARMY/Label` **13 of 13 at both aspects**, rendered as two lines (`BUILD` / `BARRACKS`),
font 30. Nothing new flagged on `ManageCard_BUILD` / `ManageCard_RESEARCH` or the three descriptions.

---

### ROUND 3, 2026-09-10 - THE BAND WAS ONE PIXEL SHORT, AND THE ASSET NAMES THE PIXEL

**`Builds/wave5-capture1` split on round 2's band, which is what pinned the number down exactly:**

| aspect | band | result |
|---|---|---|
| 2340x1080 | **76.2 px** | **WRAPPED - `BUILD` / `BARRACKS`, 13 of 13, two lines. CLEAN.** (confirmed on `Builds/ui-capture/ManageWorkspace_2340x1080.png`, opened) |
| 2670x1200 | **74.4 px** | did **not** wrap - one line, `BUILD BARRACK`, 12 of 13, rect `y -146.2..-71.8` = 74.4 px exactly as predicted |

So the two-line requirement sits in **(74.4, 76.2]**, and the font asset names it to the decimal:
this face is fonted from **`Assets/Resources/RpgUi/font/font_title.asset` (Merriweather)**, whose
`m_FaceInfo` reads **`m_PointSize 64` / `m_LineHeight 80.448`** - a line factor of **1.257**. Two lines
at the 30 px floor need **2 x 30 x 1.257 = 75.4 px**. `76.2 >= 75.4` wraps; `74.4 < 75.4` does not.
**The 2670 band was short by 1.0 px.** Nothing else was ever wrong with the round-2 shape.

⛔ **AND MY FIRST ANSWER THIS ROUND WAS THE WRONG ASSET - RECORDED, NOT QUIETLY CORRECTED.** I read
`font_body` (Alata, `88.32 / 64 = 1.38`), computed 82.8 px, and concluded the wrap was *impossible*
here - I had begun rewriting the fix as a width fix. **Opening the 2340 PNG killed that in one look:
it had already wrapped.** A line factor that is plausible is still a guess; the asset the label is
actually fonted from is the only one that counts. Same failure shape as rounds 1 and 2, caught by the
same cure - look at the picture.

**The fix: ~6 px of height, taken where nothing is drawn.** The band's floor is the description top
(`0.02 + HubDescBandF` = 0.21) and its ceiling was the art well (`1 - HubArtWellF` = 0.40). The ceiling
now goes **0.015 into the well**: band **0.21..0.415 = 0.205 of the card**.

| aspect | band | vs 75.4 (measured) | vs 79.1 (conservative) | band top vs padlock bottom 0.4195 |
|---|---|---|---|---|
| 2340x1080 | **82.2 px** | +6.8 | +3.1 | 0.4150 - clear |
| 2670x1200 | **80.3 px** | +4.9 | +1.2 | 0.4150 - clear |

⚠ **Sized to clear the CONSERVATIVE reading too.** TMP's two-line extent is `2 x lineHeight` = 75.4 px
on one reading and `ascent + lineHeight + |descent| = (70.4 + 80.448 + 17.92) x 30/64` = **79.1 px** on
the other. The captures side with 75.4, so that is the binding number - but the band clears 79.1 as
well, because being wrong by a pixel here ships a caption reading **"BUILD"**.

⛔ **The padlock is the other constraint on this one card, and the band stays under it.**
`BuildLockBadge` mounts a **square** sprite (`lock-badge.png`, **1254x1254**, read from the PNG header,
`preserveAspect`) in a `0.345..0.50 x 0.20..0.76` rect, so it draws card-width x 0.155 centred at
y 0.48 - bottom edge **0.4195**. The new band tops out at **0.4150**, so the caption's *band*, not
merely its ink, stays entirely below it. Going to 0.42 would have tucked the band under the padlock
for 0.5 px of nothing.

⛔ **`HubArtWellF`, `HubTitleBandF`, `HubDescBandF` and `BuildLockBadge` are ALL untouched.** The 0.015
is taken by ONE card's face rect, so no constant moves, no other card moves, and the
mockup-conformance cases that parse those constants read exactly what they read before.

**The guard is re-aimed at the real number** (`ManageScreenPanel.cs`, sibling to the description's
height check): it now warns against `(70.4 + 80.448 + 17.92) * 30 / 64` = 79.1 px, the conservative
extent, and says in the message that TMP will not break the line at all - it keeps ONE line and
Truncate ships `BUILD BARRACK`. ⚠ `2 x ElarionUi.FontFloorMobile` (60) is deliberately **not** reused
there: it is 15 px slacker than this font needs, and that slack is exactly what let this ship.

#### Expected on the next capture (round 3)

`ManageCard_ARMY/Label` **13 of 13 at BOTH aspects**, two lines (`BUILD` / `BARRACKS`) at font 30 -
2340 already renders this and must stay clean; 2670 is the one that changes. Nothing new on
`ManageCard_BUILD` / `ManageCard_RESEARCH` or the three descriptions.

⚠ **The two `GlyphBaseline` strings are now STALE as well as standing:** they encode `11 of 14`, the
count for the RETIRED copy. `wave4-capture3` measured `12 of 13`. So a capture taken **before** this fix
lands reports 2 unbaselined findings *and* 2 stale baseline rows. Both entries are deleted when a
capture comes back clean, exactly as acceptance item 2 requires.

**Removable once a fresh capture shows `UI_GLYPH_OK` with `baselined=` down by exactly 2 and
`UI_GLYPH_FAIL` on nothing new** (read at source this session, `Assets/Editor/UICaptureLaunch.cs:6290`
and `:6293`, with their trailing copy comments at `:6291` / `:6294`):

```
ManageWorkspace_2340x1080|ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageCategoryGrid/ManageCard_ARMY/Label|11 of 14
ManageWorkspace_2670x1200|ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageCategoryGrid/ManageCard_ARMY/Label|11 of 14
```

That would leave this ticket's remainder at **2**: `EndStateWaveClear_repairAll_1920x1080` (the capture
FIXTURE is the defect - RESULT sec.4) and `RealmWorkspace_1920x1080 | DeckCard_The Night Market/Label`
(a PlayerDeck title, lane C scope).

**"keep the cook fire"** is the second half of the same sentence and belongs to a different ticket -
recorded at `WorkOrders/WORK_ORDER_1634_raid_camp_authored_prop_gaps.md`. Noted here only so the
verbatim quote is not split across the record.

**Status deliberately untouched** - the lead flips it after the capture.
