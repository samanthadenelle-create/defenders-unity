# WO-1823: Text Too Small to Read — Legibility Audit

**Status:** IMPLEMENTED

**Ticket:** WO-1823  
**Route:** Main_Castle_Overworld (home hub, new player progression)  
**Date logged:** 2026-09-17  
**Silo:** Audit only (read-only measurement lane)  

---

## Problem Statement

Player report (bug_reports row 10, build 2026.09.16.371701, 2026-09-17 08:15Z):  
**"Text is too small to read."**

Player profile: New (completed tutorial, two waves, unlocked barracks, trained an army).  
Device: Solana Seeker, 2670x1200 landscape.  
Related reports 7–9 from another new player at same stage: "it is very not intuitive."

---

## Findings Summary

### UI Scaling Architecture

| Property | Value |
|---|---|
| Canvas reference resolution | 1080w × 1920h (portrait mobile) |
| CanvasScaler mode | ScaleWithScreenSize, matchWidthOrHeight = 0.5f |
| Seeker device resolution | 2670w × 1200h (landscape) |
| **Calculated scale factor** | **(2670÷1080 + 1200÷1920) ÷ 2 ≈ 1.55** |
| File authority | `Assets/_Modules/Core/UI/ElarionUiKit.cs:109-111` |

### Typography Constants (Reference Pixels)

| Name | Ref px | Device px (×1.55) | File:Line |
|---|---|---|---|
| FontTitle | 88 | 136.4 | `ElarionUi.cs:111` |
| FontHead | 64 | 99.2 | `ElarionUi.cs:112` |
| FontBody | 50 | 77.5 | `ElarionUi.cs:113` |
| FontLabel | 40 | 62.0 | `ElarionUi.cs:114` |
| FontMicro | 32 | 49.6 | `ElarionUi.cs:115` |
| FontFloorMobile | 30 | 46.5 | `ElarionUi.cs:123` |

### Top-15 Smallest Texts (Town Hub)

Ranked by device pixel height on Seeker; only texts visible in first 10 min (new player):

| Rank | Surface | Element | Authored (ref px) | Device px | Status | File:Line |
|---|---|---|---|---|---|---|
| **1** | Wave counter | **Countdown min** | **18** | **27.9** | ⛔ CRITICAL | `HudKitController.cs:1796` |
| 2 | Wave counter | Start Wave label (min) | 20 | 31.0 | ⚠ Marginal | `HudKitController.cs:1814` |
| 3 | Heart HUD | Heart name (min) | 20 | 31.0 | ⚠ Marginal | `HudKitController.cs:2491` |
| 4 | Wave counter | Wave label (min) | 22 | 34.1 | ⚠ Marginal | `HudKitController.cs:1791` |
| 5 | Town hub | Cycle row labels (min) | 22 | 34.1 | ⚠ Marginal | `HudKitController.cs:2339` |
| 6 | Heart HUD | Heart name (max) | 26 | 40.3 | Below floor | `HudKitController.cs:2492` |
| 7 | Wave counter | Countdown (max) | 24 | 37.2 | ⚠ Marginal | `HudKitController.cs:1797` |
| 8 | Wave counter | Start label (max) | 30 | 46.5 | Below floor | `HudKitController.cs:1815` |
| 9 | Wave counter | Wave label (max) | 30 | 46.5 | Below floor | `HudKitController.cs:1792` |
| 10 | Town hub | Resource counter (count) | 32 (FontMicro) | 49.6 | Below floor | `ElarionUiKitObsidian.cs:slot.count` |

---

## Accessibility Baseline Used

- **Threshold: 28 physical px** (approximates 12pt type at typical phone viewing distance; room for error on 2.47× height scaling to landscape)
- **WCAG AAA body text: 14pt minimum** (at normal distance, ~18–20px on typical phone)
- **Seeker device dpi: Unknown** (not stated in repo; assumed standard Android density; `grep -r "Seeker.*dpi"` returned no matches)

---

## Critical Findings

1. **Wave countdown minimum (27.9 px) is BELOW threshold and SMALLEST text in the new-player path.**  
   - Measured min: 18 ref px at fontSizeMin (HudKitController:1796)
   - Player must read: "3" / "2" / "1" / "Start Wave" to progress
   - File authority: `HudKitController.cs:1796-1797`

2. **Nine other texts are below or near 52 px (conservative 12pt baseline on 160dpi phone).**  
   - All are in the wave banner, heart status, or cycle rows — all visible on first screen
   - File authority: HudKitController.cs lines 1791–1815, 2339–2340, 2491–2492

3. **Resource counters (FontMicro = 32 ref px = 49.6 dev px) are player-critical in early economy.**  
   - Player must read wood/iron/crystal counts to plan builds
   - File authority: `ElarionUiKitObsidian.cs` (CurrencyChip builder)

4. **No hardcoded dpi for Seeker was found.**  
   - Calculation is scale-factor only; physical legibility cannot be verified without device metrics

---

## What Could Not Be Determined

1. **Seeker physical dpi** — required to convert device px back to physical pt size for WCAG compliance; `grep -r "Seeker.*dpi"` / `grep -r "2670.*1200.*density"` returned no hits
2. **FontFloorMobile intent** — constant exists (30px) but no regression or test pin confirms whether min-sizes below it (18–26px) are intentional or a bug
3. **Actual player reading distance** — viewing angle/distance on Seeker unknown; legibility scales with both
4. **Per-screen new-player text coverage** — first 10 min includes Manage/Build/Army screens; full audit of those screens not in scope for this lane

---

## Data Sources (All Read This Session)

- Reference resolution: `Assets/_Modules/Core/UI/ElarionUiKit.cs:109`
- Typography scale: `Assets/_Modules/Core/UI/ElarionUi.cs:111–115, 123`
- HUD text sizes: `Assets/_Modules/HUD/Kit/HudKitController.cs:1791–1815, 2339–2340, 2491–2492`
- CurrencyChip builder: `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs` (FontMicro / FontLabel usage)
- Device spec: Seeker 2670×1200 stated in ticket

---

## Acceptance Criteria

- [ ] Audit data table is correct (scale factor verified against CanvasScaler code, device res confirmed)
- [ ] Top-10 smallest texts are ranked by device px, not assumption
- [ ] All file:line citations checked by grep in this session
- [ ] Threshold (28 px) is named with reasoning, not magic number
- [ ] What was not determinable is listed explicitly

---

**Assigned to:** CLI (for fix implementation when WO-1823 is promoted to IMPLEMENT)

---

## IMPLEMENTATION (2026-09-17, owner ruling: apply the existing mobile floor to every HUD label)

`ElarionUi.FontFloorMobile = 30f` — read at its declaration, `Assets/_Modules/Core/UI/ElarionUi.cs:123`
(the audit table above cites the same line; re-read at source this session, not copied).

Four of the five audited sites now **NAME the floor** instead of a numeric literal, so a future raise
moves every site at once. Line numbers are post-edit:

| Site | Before | After | file:line |
|---|---|---|---|
| Wave headline `_waveLabel` | min 22f / max 30f | min `ElarionUi.FontFloorMobile` / max 30f | `HudKitController.cs:1797/1798` |
| Wave countdown `_waveCountdown` | min 18f / max **24f** | min + max `ElarionUi.FontFloorMobile` | `HudKitController.cs:1808/1809` |
| Start Wave label | min 20f / max 30f | min `ElarionUi.FontFloorMobile` / max 30f | `HudKitController.cs:1828/1829` |
| Rail/cycle chip `lbl` | min 22f / max 30f + `FitSingleLine(lbl, 22f, 30f)` | min `ElarionUi.FontFloorMobile` / max 30f + `FitSingleLine(lbl, ElarionUi.FontFloorMobile, 30f)` | `HudKitController.cs:2365/2366/2367` |

The countdown's **max** had to move too: it was 24f, i.e. BELOW the new min, and a max under the min
is not a range. Every other property (colour, band, alignment, wrapping mode) is untouched.

### ⛔ ONE SITE COULD NOT BE RAISED — Heart plate name/Heartfire (open, needs a ruling)

`HeartNameFontMin/Max` and `HeartfireFontMin/Max` stay at **20/26**
(`HudKitController.cs:2535/2536/2539/2540`, with the reasoning written in-source above them).
This is arithmetic, not caution:

- plate height = `HudLayoutBands.HeartMount.height` (0.135, `HudLayoutBands.cs:84`) × refH × 0.96
  = **125 ref px** at 2670×1200 (140 at 1920×1080)
- the name row band is 0.74..0.96 (`HudKitController.cs:2505/2506`) = 0.22 × 125 = **27.5 ref px**
- `HudLabelFitRegression` Case 10c requires floor × `LineHeightFactor` 1.2 — a 30 px line needs **36**
- all four rows at a 30 px floor need 36 + 21.6 + 36 + 21.6 = **115.2 px** inside a visible frame
  (0.06..0.97 of the plate) that is only **113.75 px** at the capture aspect — it does not fit with
  zero gaps

Case 10c's own remedy is *"Grow `HudLayoutBands.HeartMount`, never the font down"* — that is
`DeNelle.Core`, outside this lane's silo, and it moves a mount other HUD surfaces sit beside.
**Owner/lead ruling required.** Raising the literals alone would red the gate and, on device, hand
the post-layout FitGuard a band it cannot seat — the relax-below-floor path, i.e. the reported defect
again.

### Trade made deliberately (flagged, not hidden)

`BuildRailChip` fits with `FitSingleLine`, a WIDTH fit. At a 30 px floor a long multi-part caption
ellipsizes instead of shrinking. `HudLabelFitRegression` Case 15c recorded "Builders idle 2" at
157.1 px (×1.15 slack = 180.6) in a 202.4 px rect **at 22** — scaled to 30 that is ~246 px, so it
would ellipsize. That chip is the **dormant** surface (its build call is retired,
`HudKitController.cs:817`; Case 15d reds if it is un-retired). Case 2 already MEASURES the **live**
Collectors chip at the floor and passes, which is why the floor is applied rather than the chip
special-cased. If a live chip is seen to ellipsize a count, the fix is `FitBlock` (the method's own
header argues for the wrap) — never dropping the floor back under the kit minimum.

### Regression

Extended the existing suite rather than adding a file: **`HudLabelFitRegression` Case 16
`[hud-font-floor]`** (`Assets/Editor/Regression/HudLabelFitRegression.cs`). It pins the four sites'
symbolic assignments (16a), **scans `BuildWaveBlock` and `BuildRailChip` for ANY sub-floor
`fontSizeMin`/`fontSizeMax` numeric literal** (16b — this is what catches the next seat, not just
today's sites), asserts each raised band seats a floor line against the typed `WaveBandHeightPx` /
`RailChipHeightPx` (16c), and FAILS if the Heart-plate exception explanation is deleted while noting
the open gap on every run (16d). Registered in `Run()` next to Case 15; **no `DataRegression.cs` edit
was needed** — the suite is already wired at `DataRegression.cs:679` and emits its own
`HUD_LABEL_FIT_OK` / `HUD_LABEL_FIT_FAIL` marker.

### Not claimed

- **Not gate-verified.** Unity was not launched (`Get-Process Unity` = 0 this session); the lane was
  told to hold for an explicit GO. `COMPILE_GATE_OK` / `HUD_LABEL_FIT_OK` on a fresh log are
  outstanding, and marker absence is a FAILURE, not an unknown.
- **Not felt-verified.** Per §13 the PO closes on feel; a device capture at 2670×1200 is the proof
  the player's words are answered.
- The kit's post-layout guard (`ElarionUiKitObsidian.cs:3555-3557`) still relaxes `fontSizeMin`
  toward `FontHardFloor` (20) when a label renders zero glyphs. The floor holds **at authoring**;
  that last resort can still undercut it. Unchanged by this WO, stated so it is not mistaken for a
  guarantee.
- Sub-floor HUD sites found but **outside** this ticket's five: `_defenseChipLabel` FitBlock at 22f
  (`HudKitController.cs:2188`, and `DefenseReportLayoutRegression.cs:451` pins that literal string,
  so it cannot be raised without that suite moving too); `HeartObjectiveFontMin` 16f
  (`HudKitController.cs:2544`); the `ElarionUiKit.FontHardFloor` callers at
  `HudKitController.cs:1461/2890/3308`. Audit item 10 (resource counter at `FontMicro` 32) is
  already above the floor.
- **Stale comment found, not fixed:** `BuildRailChip`'s header argues for `FitBlock` ("the label
  wraps instead") while the code calls `FitSingleLine`. The comment describes behaviour the file does
  not have. Left for the ruling above rather than silently switching the fit mode.
