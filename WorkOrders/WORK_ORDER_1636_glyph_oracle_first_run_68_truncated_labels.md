# WO-1636 - 68 labels are cut mid-word across 21 captured panel builds (the first glyph-oracle run)

**Status:** PARTIALLY IMPLEMENTED 2026-09-10 - 64 of 68 cleared on wave3-capture10 (baselined 68 -> 4): NightMarket 21, RumorBoard 21, DeckCardPurpose 16, HeroSelect 5, BuildMenu 1; the 4 left are Manage ARMY x2 (copy ruling: BUILD BARRACKS fits), EndStateWaveClear x1 (stale capture fixture) and Realm DeckCard_The Night Market x1 (PlayerDeck title, lane C scope); GlyphBaseline shrink to the 4 is the next cleanup (was: PARTIALLY IMPLEMENTED 2026-09-10 - RumorBoard (21), DeckCardPurpose (16), HeroSelect (5) cleared on wave3-capture9 (baselined 68 -> 26 matched); NightMarket (21) and BuildMenu (1) fixes are gated separately; Manage ARMY (2) needs a copy ruling ('BUILD BARRACKS' fits); EndStateWaveClear (1) is a stale capture fixture (was: READY TO IMPLEMENT))
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
