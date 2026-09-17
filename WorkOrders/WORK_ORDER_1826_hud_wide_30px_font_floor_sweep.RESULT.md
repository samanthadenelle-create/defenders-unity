# WORK ORDER 1826 — RESULT

**Outcome:** IMPLEMENTED (code + oracle landed; the Unity gate run is pending the lead's GO)
**Date:** 2026-09-17
**Ruling served:** owner, verbatim — "do it, apply the 30 floor to the hud"

## Files changed (4 runtime + 1 oracle)

| File | Change |
|---|---|
| `Assets/_Modules/HUD/Kit/HudCompassWidget.cs` | cardinal tick initial size `north ? 30f : 26f` → `ElarionUi.FontFloorMobile`; `fontSizeMin` `14f` → floor; `fontSizeMax` `north ? 30f : 26f` → floor; `_cardinal.fontSizeMin` `18f` → floor |
| `Assets/_Modules/HUD/DialogueView.cs` | option-row label `26` → `(int)ElarionUi.FontFloorMobile`; `FitBlock(minSize: 20f, maxSize: 26f)` → `(ElarionUi.FontFloorMobile, 30f)` |
| `Assets/_Modules/HUD/QuestTrackerHud.cs` | icon-less quest marker glyph `26` → `ElarionUi.FontFloorMobile` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | combat item-picker hint `28f` → `ElarionUi.FontFloorMobile`. **Font only — a proposed band widening was measured and withdrawn (below).** |
| `Assets/Editor/Regression/HudLabelFitRegression.cs` | new `Case17_FontFloorSweep` `[hud-font-floor-sweep]`, registered in `Run()` next to Case 16; new shared helpers `SeatsFloor` / `RequireIn` |

Nothing else was touched. No `.unity` file, no scene, no gameplay code, no `System.Reflection`.

## Every value, before → after

| file:line (post-edit) | Before | After |
|---|---|---|
| `HudCompassWidget.cs:233` | `north ? 30f : 26f` | `ElarionUi.FontFloorMobile` |
| `HudCompassWidget.cs:252` | `fontSizeMin = 14f` | `= ElarionUi.FontFloorMobile` |
| `HudCompassWidget.cs:253` | `fontSizeMax = north ? 30f : 26f` | `= ElarionUi.FontFloorMobile` |
| `HudCompassWidget.cs:325` | `_cardinal.fontSizeMin = 18f` | `= ElarionUi.FontFloorMobile` |
| `DialogueView.cs:1142` | `26` | `(int)ElarionUi.FontFloorMobile` |
| `DialogueView.cs:1146` | `FitBlock(minSize: 20f, maxSize: 26f)` | `FitBlock(minSize: ElarionUi.FontFloorMobile, maxSize: 30f)` |
| `QuestTrackerHud.cs:164` | `gt.fontSize = 26` | `= ElarionUi.FontFloorMobile` |
| `HudKitController.cs:3750` | `hint.fontSize = 28f` | `= ElarionUi.FontFloorMobile` |

Line numbers above were read with `grep -n` after the edits, not computed.

**Every raise names the symbol, never a retyped `30f`** — a literal cannot follow the floor when
it moves, which is the lesson Case 16a already pins for WO-1823's four sites.

## Downstream layout math checked (the WO-1823 audit pattern)

- **Compass min/max raised together.** Raising `fontSizeMin` alone would have left `min > max`
  on the six non-north ticks (max was `26f`), which TMP resolves by ignoring the floor entirely —
  a change that looks applied and does nothing. North stays distinguished by colour (`Gilt`) and
  its 4 px graduation bar, not by being the only legible tick.
- **Compass `fontSizeMax` left at `ElarionUi.FontMicro` (32)** for the heading readout — already
  above the floor; the source comment at `:298-303` explains why 40 would be culled there.
- **Seat arithmetic, need = 30 x 1.2 = 36.0 ref px** (`HudLabelFitRegression.LineHeightFactor`):
  compass tick layer ~49 px; compass heading band ~41 px; dialogue option label 94 px
  (0.84 of `MinTouchPx` 112); quest medallion 52 px. All seat the floor.
- **Item-picker hint: a band widening was proposed, then MEASURED and WITHDRAWN.** The first pass
  assumed `MedievalUiSkin.ApplyShell(compact: true)` insets the body and moved the band bottom
  `0.59 → 0.55` "for safety". Two file reads killed both halves of that:
  `ApplyShell` changes **no geometry** (`MedievalUiSkin.cs:15-48`), so the body is the **default**
  `FrameZones` row y 0.10..0.875 of the full-frame content (`ElarionUiKit.cs:356`, `:619`, `:718`):
  panel `(0.82-0.18) x 965 = 618` → body `0.775 x 618 = 479` → hint band `0.12 x 479 = **57 ref
  px**`, already seating the floor with 21 px spare. And the widening would have **caused** a
  collision: the HEALING POTION rung is `0.17 x 479 = 81` px, under `MinTouchPx` 112, so
  `ClampMinTouch` grows it symmetrically about its centre (`ElarionUiKit.cs:979-988`) to y
  **0.308..0.542** of the body — 2 px from a 0.55 bottom, 23 px from 0.59. **Band reverted; font
  raise kept.** Case 17d now computes both numbers instead of pinning a widened band.
  (Pre-existing, out of scope, recorded: the two grown potion rungs overlap each other by ~0.024
  of the body — HEAL's grown bottom 0.308 sits inside MANA's grown top 0.332.)

## Left as documented exceptions (not changed, each already explained in-source)

- `HudKitController.cs` Heart plate consts `HeartNameFontMin/Max` (20/26),
  `HeartfireFontMin/Max` (20/26), `HeartObjectiveFontMin/Max` (16/18) — the WO-1824 owner ruling:
  the docked plate is a glance, the tap-to-expand overlay (`HeartExpandedFontMin = 30f`) is the
  floor-compliant reading state. Case 16d already emits "Do NOT raise this const".
- `NightMarketLabelMaxPx = 26f` — WO-1662 records the title already **wraps to two lines at 26**
  and that a one-line render drew 12 of 14 printable glyphs; Case 11 measures its glyph advances
  against that plate, so a raise is a red gate. Needs a plate/width re-author, not a font bump.

## Left READY for a follow-up, with the math

The five flat modal panels (`LeaderboardPanel`, `ClanChatPanel`, `CosmeticShopPanel`,
`BenefactorsWallPanel`, `TownShowcaseVisitPanel`) — ~26 sites at 11–18 px, all player-reachable.
Each builds text through a private helper with a fixed `fontSize`, **no `enableAutoSizing` and no
`overflowMode`**, so raising the font without re-cutting the px ladder culls glyphs outright.
Worked deficits are in the ticket; the shortest version: Leaderboard needs
`FooterH 30 → 40` (fails by 6.0 px), `ProfileH 68 → 80` (two 34 px half-rows, fails by 2.0 px
each), and the row column fractions re-cut, against a `ListScroll` remainder of only ~121 px in
landscape. Deliberately **not half-raised** — a panel showing 30 px on some rows and 12 px on
others is worse than one pending ticket.

`Case17_FontFloorSweep` sub-case 17e keeps that gap honest: it does not fail on their sizes, but
it **fails if any of the five gains `enableAutoSizing` without naming the floor** (which would
shrink those labels further while looking fixed), and it notes the pending count every run.

## Oracle — measured, not counted

`Case17_FontFloorSweep` `[hud-font-floor-sweep]`, registered in `Run()`:

- **17a** — four compass pins must name `ElarionUi.FontFloorMobile`; explicit min-without-max
  guard; both compass bands seat a floor line; **the widest cardinal name `"NW"` is measured
  through `ElarionUiKit.MeasureLineWidthPx` (real glyph advances), charged with
  `BoldOnlyWidthSlack` (1.10) because the tick is `FontStyles.Bold` + `characterSpacing 2f`**,
  against the 76 px tick.
- **17b** — the dialogue option size *and* its `FitBlock` min must both name the floor (a
  floor-named size with a 20f FitBlock min is not a floor at all); the 94 px row band is seated.
- **17c** — the quest marker names the floor, and the 52 x 52 `_card` pin means the seat
  arithmetic cannot silently measure a rect that no longer exists.
- **17d** — the hint font is pinned; the band is **computed** from the authored fractions (57 ref
  px) rather than pinned at a widened value; the grown potion rung's top is computed and asserted
  clear of the band's 0.59 edge; and the sentence is **measured** at the floor against its box,
  because `enableWordWrapping` is `false` there so an overflow CLIPS.
- **17e** — the table-C regression guard above.

An unmeasurable font headlessly is recorded as a **stated skip (a note)**, never a pass.

## Gates

- `python tools/gate_brace.py` on all five `.cs`: **`GATE_BRACE_SUMMARY bad=0 of 5`, exit 0.**
- NUL scan: **0 NUL bytes** in each of the five files.
- Every pin literal Case 17 asserts was grep-verified present in its file after the edits
  (10 of 10 `OK`) — so the new case cannot red on its own anchors.
- **`COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` NOT RUN — no Unity run was made (GO not given).**
  That is the one acceptance criterion this hand-back does not carry.
