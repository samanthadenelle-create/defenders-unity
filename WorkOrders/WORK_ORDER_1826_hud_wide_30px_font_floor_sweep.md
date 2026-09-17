# WORK ORDER 1826 — HUD-wide 30 px font floor sweep

**Status:** IMPLEMENTED

**Silo:** HUD / UI presentation (no gameplay, no scene files)
**Follows:** WO-1823 (wave label / countdown / Start Wave CTA / rail chip raised to the floor),
WO-1824 (Heart plate tap-to-expand reading overlay)

---

## Owner ruling (verbatim, binding)

> "do it, apply the 30 floor to the hud"

Responding to a player report captured on build 2026.09.16.371701 (honest-feedback flow, new
player straight out of the tutorial): **"Text is too small to read."** WO-1823 raised the four
sites it found in `BuildWaveBlock` / `BuildRailChip`; WO-1824 solved the one plate that
arithmetically cannot seat the floor. The owner now wants the **same standard across the whole
HUD**, not only the sites already touched.

## The floor, and the unit it is measured in

- `ElarionUi.FontFloorMobile = 30f` — `Assets/_Modules/Core/UI/ElarionUi.cs:123`
- `ElarionUiKit.FontFloor = 30f` — `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3033`
- Reference px on the kit canvas: `CanvasScaler` `referenceResolution (1080,1920)`,
  `ScaleWithScreenSize`, match 0.5 — `Assets/_Modules/Core/UI/ElarionUiKit.cs:109`.
  In landscape (2670x1200) that resolves the canvas to **2148 x 965 canvas-local units**
  (`HudKitController.cs:1755`), so a 30 px line is ~3.1 % of screen height. **This is why the
  panel labels authored at 11–18 are genuinely illegible, not merely small.**
- **Seat arithmetic:** a floor line needs `30 x LineHeightFactor`. The oracle's factor is
  `1.2f` (`Assets/Editor/Regression/HudLabelFitRegression.cs:174`), so **need = 36.0 ref px**.
  (`HudKitController.cs:1925` quotes the kit guard's own 1.1499 relax threshold = 34.5 px; the
  oracle's 36 is the stricter of the two and is the number used below.)

## Scope — surfaces excluded, with the gate proven (not assumed from the filename)

| File | Why it is not player-facing |
|---|---|
| `Assets/_Modules/HUD/AdminOverlay.cs` (`:234`, `:384`, `:428`) | `#if DEVELOPMENT_BUILD \|\| UNITY_EDITOR \|\| TESTER_BUILD` wraps the build + input (`:44`, `:70`, `:259`, `:417`, `:557`); `:544` also refuses to open without `IsAuthorised()` |
| `Assets/_Modules/HUD/DebuggingController.cs` (`:250` `fontSize = 16`, `:263` `fontSize = 14`) | Both sites are inside `EnsureOverlay`, compiled out by `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` (`:94`, `:105`, `:137`, `:175`; stated at `:43`), and `Enabled` defaults **false** (`:87`) |
| `Assets/_Modules/HUD/OwnerDevToolsOverlay.cs` (`:491`) | Owner-username gate `OwnerUsername = "samanthadenelle"` (`:61`) **and** `FeatureFlags.DevResourceTool` (`:88`), which suppresses the chip entirely |

## Site ledger — every player-facing font-size site under `Assets/_Modules/HUD`

### A. RAISED to the floor in this ticket

| Site | Before | After | Seat proof |
|---|---|---|---|
| `Kit/HudCompassWidget.cs:233` cardinal tick initial size | `north ? 30f : 26f` | `ElarionUi.FontFloorMobile` | tick label fills `_tickLayer` y 0.03..0.55 of a strip that is 0.34..1.00 of a ~142 px mount = **~49 ref px** (`:196-199`, `:203-204`, `:218-219`) ≥ 36 |
| `Kit/HudCompassWidget.cs:252` `fontSizeMin` | `14f` | `ElarionUi.FontFloorMobile` | same band, ~49 px |
| `Kit/HudCompassWidget.cs:253` `fontSizeMax` | `north ? 30f : 26f` | `ElarionUi.FontFloorMobile` | **raised together with the min — raising the min alone would leave min > max for E/NE/S/SW/W/NW** |
| `Kit/HudCompassWidget.cs:325` `_cardinal.fontSizeMin` | `18f` | `ElarionUi.FontFloorMobile` | heading band y 0.56..1.00 of the ~94 px strip = **~41 ref px** ≥ 36 (`:298-303`). `fontSizeMax` is `ElarionUi.FontMicro` = 32 ≥ 30, so it is left alone |
| `DialogueView.cs:1142` option-row label | `26` | `ElarionUi.FontFloorMobile` | row is a `MinTouchPx` rung (112 px, `ElarionUiKit.cs:317`); the label is y 0.08..0.92 of it = **94 ref px**, seats two floor lines (72) |
| `DialogueView.cs:1146` `FitBlock(minSize: 20f, maxSize: 26f)` | 20 / 26 | floor / 30 | same band; `FitBlock` wraps rather than shrinking, so a long option grows to 2 lines inside 94 px |
| `QuestTrackerHud.cs:164` `"!"` marker glyph | `26` | `ElarionUi.FontFloorMobile` | medallion `_card.sizeDelta = 52 x 52` (`:107`); the glyph rect fills it — 52 ≥ 36. Single glyph, so no width risk |
| `Kit/HudKitController.cs:3750` item-picker hint | `28f` | `ElarionUi.FontFloorMobile` | band is y 0.59..0.71 of a body of **479 ref px** = **57 ref px** ≥ 36. Font only; **band unchanged** — see the correction below |

**⚠ CORRECTION MADE DURING THIS TICKET — a band widening was proposed, then MEASURED and
withdrawn.** A first pass assumed `MedievalUiSkin.ApplyShell(compact: true)` insets the body, could
not bound the hint band from source, and moved its bottom `0.59 -> 0.55` "for safety". Both halves
of that were wrong, and reading two files settled it:

- `ApplyShell` changes **no geometry at all** — it swaps the frame sprite, clears the legacy fill,
  hides the medallion and styles the title (`MedievalUiSkin.cs:15-48`). There is no compact inset.
- The body is therefore the **default** `FrameZones` row, y 0.10..0.875 of `chrome.content`, and
  content is the full frame (`ElarionUiKit.cs:356`, `:619`, `:718`). So:
  panel `= (0.82-0.18) x 965 = 618` ref px → body `= 0.775 x 618 = 479` ref px →
  hint band `= 0.12 x 479 = 57` ref px. **It already seats the floor with 21 px to spare.**
- And the widening would have **created** a collision: the HEALING POTION rung is
  `(0.51-0.34) x 479 = 81` ref px, under `MinTouchPx` 112, so `ClampMinTouch` grows it
  **symmetrically about its centre** (`ElarionUiKit.cs:979-988`) to y **0.308..0.542** of the body.
  A 0.55 band bottom leaves 2 px of clearance; 0.59 leaves 23.

Case 17d now **computes** both numbers rather than pinning a widened band, so neither can be
re-guessed. (Observed in passing, pre-existing and out of scope: the grown potion rungs overlap
each other by ~0.024 of the body — the HEAL rung's grown bottom 0.308 sits inside the MANA rung's
grown top 0.332. Not a font defect; recorded, not touched.)

Sites already at/above the floor and left untouched (recorded so the next audit does not re-open
them): `DialogueView.cs:399` speaker `36`, `:402` affiliation `30` (owner ruled 2026-09-10,
`:395-397`), `:418` body `30`; `PlayerDeckWorkspace.cs:308` `36f`, `:502`
`FitBlock(ElarionUi.FontFloorMobile, 34f)`; `HudKitController.cs:3719` picker title `48f`,
`:3741-3742` potion faces `34f`; `DailyQuestHud.cs:335`,`:342` already name the floor;
`HudCompassWidget.cs:309` `ElarionUi.FontMicro` (32).

### B. DOCUMENTED EXCEPTIONS — deliberately NOT raised

| Site | Value | Why it stays |
|---|---|---|
| `Kit/HudKitController.cs:2553-2554` `HeartNameFontMin/Max` | 20 / 26 | WO-1824 owner ruling: the **docked** plate is a glance; the floor-compliant view is the tap-to-expand overlay (`HeartExpandedFontMin = 30f`, `:2611`). The deficit arithmetic is in the `WO-1823 EXCEPTION` block at `:2527-2545` and `:2769-2771`; `HudLabelFitRegression` Case 16d already carries a `notes.Add` saying **"Do NOT raise this const"** (`:3010-3017`) |
| `Kit/HudKitController.cs:2557-2558` `HeartfireFontMin/Max` | 20 / 26 | same block, same plate, same overlay |
| `Kit/HudKitController.cs:2562-2563` `HeartObjectiveFontMin/Max` | 16 / 18 | same block. Confirmed from the source comment, not assumed |
| `Kit/HudKitController.cs:1220` `NightMarketLabelMaxPx` | 26 | **raising it would re-open a captured defect.** WO-1662 (`:1424-1470`) records that "THE NIGHT MARKET" already **wraps to two lines at 26** and that a one-line render drew "12 of 14 printable" glyphs. Two floor lines need 72 ref px in a plate that is y 0.46..0.92 of the card. `HudLabelFitRegression` Case 11 `[night-market-standout]` measures the word's glyph advances against this exact plate, so a raise is a red gate, not a readability win. Candidate for a width/plate re-author, not a font bump |

### C. LEFT READY for a follow-up — the five flat modal panels

**Not raised, and deliberately not half-raised.** All five build text through a private
`MakeText`/`Text` helper that sets a **fixed `fontSize` with `enableAutoSizing` never set and no
`overflowMode`** (`LeaderboardPanel.cs:375-393`, `ClanChatPanel.cs:326-344`,
`CosmeticShopPanel.cs:687-705`, `BenefactorsWallPanel.cs:315-333`,
`TownShowcaseVisitPanel.cs:241-254`). With no autosize and no overflow handling, a band shorter
than the line **culls its glyphs outright** — the captured `"0 visible glyphs, rect 333x25"`
defect class quoted at `HudKitController.cs:1787`. Their px-band ladders were authored around
11–18 px text, so raising the font means re-authoring the ladder — a layout change, not a
one-line floor bump. A panel showing 30 px on some rows and 12 px on others is worse than one
pending ticket.

Worked deficit for the panel whose ladder is fully written down
(`LeaderboardPanel.cs:133-147`; need = 36.0 ref px):

| Band | Height | 30 px line needs | Verdict |
|---|---|---|---|
| Footer (`FooterH = 30f`, label `12` at `:147`) | 30 px | 36 px | **FAILS by 6.0 px** |
| ProfileStrip (`ProfileH = 68f`) split into two half rows (labels `18` and `14`, `:244-247`) | 34 px each | 36 px | **FAILS by 2.0 px each** |
| List row, non-visit (`preferredHeight = 48f`, three labels at `14`, `:275-280`) | 48 px | 36 px | fits — but the rank column is only x 0.00..0.10 and the score column x 0.78..1.00, so a raise needs a **width** re-author too |
| List row, visit (`preferredHeight = 120f`) | 120 px | 36 px | fits vertically |

Reaching the floor on this panel therefore needs at minimum `FooterH 30 -> 40`, `ProfileH
68 -> 80`, and the row column fractions re-cut — total +22 px of fixed ladder against a
`ListScroll` remainder that is only ~121 px in landscape (`:130-131`). That is the follow-up.

Equivalent sub-floor sites in the other four, all reachable by a player (`PanelRouter.cs:131-132`
for the Founders wall; `AutoPilotDriver.cs:6656`,`:6680` open ClanChat and the Leaderboard from
the dock; `CosmeticShopReachabilityRegression.cs:171` pins the Wardrobe card route; the showcase
opens from `LeaderboardPanel.cs:295`):

- `ClanChatPanel.cs:183`,`:195` `18`; `:224` `11`; `:229` `13`; `:256` `11`; `:280` `12`
- `CosmeticShopPanel.cs:306` `16`; `:320` `12`; `:399` `14`; `:450` `16`; `:452` `12`; `:459` `13`
- `BenefactorsWallPanel.cs:154` `14`; `:160` `12`; `:239` `14`; `:252` `14`; `:255` `16`; `:260` `12`; `:264` `13`
- `LeaderboardPanel.cs:147` `12`; `:244` `18`; `:246` `14`; `:275`,`:277`,`:279` `14`
- `TownShowcaseVisitPanel.cs:94` `24`; `:96` `14`

**Suggested shape for the follow-up (do not invent a new pattern per site):** give each panel's
helper `enableAutoSizing` with `fontSizeMin = ElarionUi.FontFloorMobile`, grow each fixed rung by
the deficit above, and where a rung genuinely cannot grow, route the detail through the
**already-proven WO-1824 tap-to-expand overlay** — `ElarionUiKit.BuildModalCanvas` / `Scrim` /
`Panel` / `ObsidianCloseButton`, exactly as `HudKitController.BuildHeartExpandedOverlay` uses it.

## Acceptance criteria

1. Every site in table A names `ElarionUi.FontFloorMobile` — a retyped `30f` is not acceptable
   (a literal cannot follow the floor when it moves).
2. Nothing in table B is changed; each stays covered by its existing source comment.
3. `HudLabelFitRegression` gains a case that **measures** the new sites — real glyph advances via
   `ElarionUiKit.MeasureLineWidthPx`, never character counts — and fails if any of them drops
   under `ElarionUi.FontFloorMobile` or if its band stops seating a floor line.
4. `python tools/gate_brace.py` clean + no NUL bytes on every `.cs` touched.
5. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a **fresh** log (needs a Unity run — GO
   required, `Get-Process Unity` checked first).

## What NOT to touch

- No `.unity` scene file.
- The three dev surfaces in the exclusion table.
- `NightMarketLabelMaxPx` and the four Heart plate consts (table B) — each is pinned.
- The five flat panels (table C) — they are one follow-up, not this ticket.
