# WO-1823 RESULT — "Text is too small to read": the mobile font floor is now applied

**Status:** IMPLEMENTED

Gate-verified on fresh logs after the lead's GO (2026-09-17 07:51–07:58). The status word is the fixed
label; this sentence is the ruling prose.

| Marker | Log | Evidence |
|---|---|---|
| `COMPILE_GATE_OK :: scripts compiled clean` | `Builds/wo1823-compile.log`, 301,309 bytes, 07:51:35 | fresh |
| `REGRESSION_OK 564/564 suites -- 564 green, 0 red, 0 skipped` | `Builds/wo1823-regression2.log`, 2,438,116 bytes, 07:58:38 | fresh |
| `[hud-label-fit] HUD LABEL FIT OK` | same log | the suite carrying new Case 16 |

**Case 16 `[hud-font-floor]` executed and reported**, verbatim from the marker's notes:
`BuildWaveBlock: 2 numeric font-size literal(s) checked against the 30px floor`;
`BuildRailChip: 1 ...`; `wave headline ... 74 px seats the 30px floor`;
`wave countdown ... 41 px seats the 30px floor`; `Start Wave CTA ... 115 px seats the 30px floor`;
`rail chip (RailChipHeightPx) 112 px seats the 30px floor`; and the open item it was built to surface —
`OPEN (WO-1823, owner/lead ruling): HeartNameFontMin is 20, under the 30px floor.`

⚠ Two notes on the run: `DataRegression` lives in namespace **`DeNelle.Editor`**, not
`DeNelle.Editor.Regression` — the first invocation died with *"executeMethod class 'DataRegression'
could not be found"* and logged no marker, which is why the judged log is `...regression2.log`. And the
WO-1824 one-line comment correction on `BuildRailChip` (below) was made **after** this run: it is a
comment-only change, re-checked with `gate_brace` (`bad=0 of 1`, 434/434 braces, 0 NUL) but **not**
re-gated through Unity.

**Date:** 2026-09-17
**Lane silo:** `Assets/_Modules/HUD/Kit/HudKitController.cs`, `Assets/Editor/Regression/HudLabelFitRegression.cs`
**Player report:** build `2026.09.16.371701`, honest-feedback flow, `Main_Castle_Overworld`, new player
who had just finished the tutorial — verbatim: *"Text is too small to read."*

---

## The floor

`ElarionUi.FontFloorMobile = 30f` — **read at its declaration this session**,
`Assets/_Modules/Core/UI/ElarionUi.cs:123`. Its own comment (`:116-122`) states the contract: detail
surfaces pass THIS to `ElarionUiKit.FitBlock`/`FitSingleLine`, bands grow or content ellipsis-truncates,
and *"text NEVER auto-shrinks below it"*. The five audited HUD sites were authored under it, so the
guarantee did not reach the town HUD at all.

The reuse pattern was already in the codebase and is the one followed here — screens NAME the floor
rather than a number (`ElarionUiKitDetailCard.cs:248/266/274/285/334/347/360`, `HarvestOverflowModal.cs:208/233/284`,
`DailyQuestHud.cs:335/342`). No new helper was invented.

## Before / after, per site (line numbers verified post-edit)

| Site | Before | After | file:line |
|---|---|---|---|
| Wave headline `_waveLabel` | `fontSizeMin = 22f` / `fontSizeMax = 30f` | `fontSizeMin = ElarionUi.FontFloorMobile` / `30f` | `HudKitController.cs:1797/1798` |
| Wave countdown `_waveCountdown` | `fontSizeMin = 18f` / `fontSizeMax = 24f` | both `ElarionUi.FontFloorMobile` | `HudKitController.cs:1808/1809` |
| Start Wave label | `fontSizeMin = 20f` / `fontSizeMax = 30f` | `fontSizeMin = ElarionUi.FontFloorMobile` / `30f` | `HudKitController.cs:1828/1829` |
| Rail/cycle chip `lbl` | `22f` / `30f` + `FitSingleLine(lbl, 22f, 30f)` | `ElarionUi.FontFloorMobile` / `30f` + `FitSingleLine(lbl, ElarionUi.FontFloorMobile, 30f)` | `HudKitController.cs:2365/2366/2367` |
| **Heart name + Heartfire** | `20f` / `26f` | **UNCHANGED — blocked, see below** | `HudKitController.cs:2535/2536/2539/2540` |

Each is a TMP autosize **min/max pair** (`enableAutoSizing = true` at `:1790` and `:1801`; the chip's
pair is then re-armed by `FitSingleLine`). None was a fixed `fontSize`. The countdown's max moved too
because 24f was BELOW the new min and a max under the min is not a range. Colour, band, alignment and
wrapping mode are untouched at every site. Each site carries a short in-source WO-1823 comment naming
the player report.

**Bands seat the floor** (a floor a band cannot seat is worse than a sub-floor min — the post-layout
FitGuard would relax it straight back): a 30 px line needs 30 × 1.2 = 36 ref px, and the wave headline
band is 0.58 × `WaveBandHeightPx` 128 = 74 px, the countdown 0.32 × 128 = 41 px, the CTA 0.90 × 128 =
115 px, the rail chip `RailChipHeightPx` 112 px.

## ⛔ BLOCKED, and it needs an owner/lead ruling: the Heart plate

`HeartNameFontMin/Max` + `HeartfireFontMin/Max` stay at 20/26. Arithmetic, not caution:

- plate = `HudLayoutBands.HeartMount.height` 0.135 (`HudLayoutBands.cs:84`) × refH × 0.96 = **125 ref px**
  at 2670×1200 (140 at 1920×1080)
- name row band 0.74..0.96 (`HudKitController.cs:2505/2506`) = 0.22 × 125 = **27.5 px**; a 30 px line
  needs **36**
- all four rows at a 30 px floor need 36 + 21.6 + 36 + 21.6 = **115.2 px** in a visible frame
  (0.06..0.97 of the plate) that is only **113.75 px** at the capture aspect — it does not fit with
  zero gaps
- `HudLabelFitRegression` Case 10c already asserts exactly this and names the remedy itself: *"Grow
  `HudLayoutBands.HeartMount`, never the font down"* — `DeNelle.Core`, outside this silo, and it moves
  a mount other HUD surfaces sit beside

Raising the literals alone would red the gate and, on device, hand the FitGuard an unseatable band —
the relax-below-floor path, i.e. the reported defect again. The four consts also must NOT be aliased to
`ElarionUi.FontFloorMobile`: Case 10 parses them as float **literals** out of the source
(`HudKitController.cs:2537/2538` says so). The exception is documented in-source above
`HeartNameFontMin` and Case 16d FAILS if that explanation is ever deleted.

## Trade made deliberately, flagged not hidden

`BuildRailChip` fits with `FitSingleLine` — a WIDTH fit — so at a 30 px floor a long multi-part caption
ellipsizes rather than shrinking. Case 15c recorded "Builders idle 2" at 157.1 px (×1.15 slack = 180.6)
in a 202.4 px rect **at 22**; scaled to 30 that is ~246 px, so it would ellipsize. That chip is the
**dormant** surface (build call retired at `HudKitController.cs:817`; Case 15d reds if un-retired),
while Case 2 already MEASURES the **live** Collectors chip at the floor and passes — which is why the
floor was applied rather than the chip special-cased. If a live chip is seen to ellipsize a count, the
fix is `FitBlock` (this method's own header argues for the wrap), never dropping the floor back under
the kit minimum. Case 15c's `chipFloor = 22f` is kept as a lower bound with that gap stated in-source
rather than raised, because raising it reds the gate on a surface that does not build.

## Regression

Extended the existing suite instead of adding a file: **`HudLabelFitRegression` Case 16
`[hud-font-floor]`**, registered in `Run()` beside Case 15. **No `DataRegression.cs` edit was needed** —
the suite is already wired at `DataRegression.cs:679` and emits its own `HUD_LABEL_FIT_OK` /
`HUD_LABEL_FIT_FAIL` marker (canon §8: a distinct marker per entry point).

- **16a** the four raised sites NAME `ElarionUi.FontFloorMobile` (six exact source pins)
- **16b** the real catch for the *next* seat: slices `BuildWaveBlock` and `BuildRailChip` and FAILS on
  ANY numeric `fontSizeMin`/`fontSizeMax` literal under the floor. A symbolic assignment has no digit
  after `= ` and is skipped, so naming the floor passes and retyping a number does not. A slice that
  cannot be taken FAILS ("a renamed method is not a pass"), and the anchors carry no quote characters
  so the gate's brace state machine cannot mis-scan them.
- **16c** each raised band seats floor × 1.2, against the typed `WaveBandHeightPx` and `RailChipHeightPx`
- **16d** FAILS if the Heart-plate exception explanation is removed; notes the open 20 → 30 gap every run
- honest about its own kind: **SOURCE LINT** for the assignments (batchmode runs no layout pass, so a
  built label's `fontSizeMin` is only what was assigned), real arithmetic for the bands

## Gates run

| Check | Result |
|---|---|
| `python tools/gate_brace.py` (the gate's own rule, both files) | `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0 |
| Raw brace count | `HudKitController.cs` 434/434; `HudLabelFitRegression.cs` 216/216 |
| NUL-byte scan (§1, WO-434) | 0 bytes `\x00` in both files |
| `Get-Process Unity` | `unity_count=0` — Unity was not launched |

## NOT CLAIMED (§11B)

- **`COMPILE_GATE_OK` and `HUD_LABEL_FIT_OK` are OUTSTANDING.** The lane was told to hold for an
  explicit GO before any Unity run. Marker absence on a fresh log is a FAILURE, not an unknown — so
  this WO is IMPLEMENTED, not verified. Case 16 has never executed.
- **Not felt-verified.** Per §13 the PO closes on feel; a fresh device capture at 2670×1200 is the only
  proof the player's words are answered. Old captures are stale builds.
- The kit's post-layout guard (`ElarionUiKitObsidian.cs:3555-3557`) still relaxes `fontSizeMin` toward
  `FontHardFloor` (20) when a label renders zero glyphs. **The floor holds at AUTHORING; that last
  resort can still undercut it on device.** Unchanged here, stated so it is not read as a guarantee.
- ⚠ **THE ONE THING TO EYEBALL IN THE CAPTURE AFTER GO — the wave countdown at both aspects.**
  `ElarionUiKit.Label` (`ElarionUiKit.cs:1947-1966`) sets font, size, colour, alignment, spacing and
  `fontStyle = Bold`, and sets **NO** `overflowMode` and **NO** `textWrappingMode` — so the wave labels
  keep TMP's defaults (Overflow, word-wrap on) and are **not** armed with `FitSingleLine`/`FitBlock` at
  all. Previously `_waveCountdown` could shrink to 18 to fit its column; it is now FIXED at 30 (min ==
  max). `HudLabelFitRegression` Case 4(c) ASSERTS the countdown fits at `ElarionUiKit.FontFloor`
  (`ElarionUiKitObsidian.cs:3033` = 30f, the same number as `FontFloorMobile` — both read at source),
  but it measures `FontRole.Body` **unskinned**, while this label draws `ElarionUi.FontLabel` **bold** —
  the exact Body-says-it-fits gap Case 2's own header documents (a 182.7 px Body measurement for a
  string that shipped cut). Widths cannot be measured without Unity, so this is stated, not claimed:
  if bold is wider, "Next wave in 14m 15s" wraps to a second line inside a 41 px band. **Look at the
  countdown in the UI capture at 2670×1200 and 1920×1080.** If it wraps, the fix is a wider label
  column or arming `FitSingleLine` on it — not lowering the floor.
- **Seeker physical dpi** is still unknown (the audit's own open item), so no claim is made that 30 ref
  px clears a WCAG point size — only that the codebase's own named floor is now honoured.
- Sub-floor HUD sites found but **outside** this ticket's five: `_defenseChipLabel` FitBlock at 22f
  (`HudKitController.cs:2188` — `DefenseReportLayoutRegression.cs:451` pins that literal as a string, so
  it cannot move without that suite moving too); `HeartObjectiveFontMin` 16f (`HudKitController.cs:2544`);
  `ElarionUiKit.FontHardFloor` callers at `HudKitController.cs:1461/2890/3308`. Audit row 10 (resource
  counter, `FontMicro` 32) is already above the floor.
- **Stale comment — NOW CORRECTED under WO-1824** (`HudKitController.cs:2346-2354`). The header argued
  for `FitBlock` ("the label wraps instead") while the code has always called `FitSingleLine`, so the
  paragraph argued the case against its own line, and its tail claimed "nothing is clipped or dropped"
  which is false for a width fit. The text now states what the code does — bounded auto-size at the
  floor, no wrap, **ellipsis** on an over-wide caption — and names the wrap as an open behaviour
  ruling. **Comment only; no behaviour change.**

## Files changed

- `Assets/_Modules/HUD/Kit/HudKitController.cs` — four sites raised to the named floor; Heart exception
  documented in-source
- `Assets/Editor/Regression/HudLabelFitRegression.cs` — Case 16 `[hud-font-floor]` + registration +
  Case 15c `chipFloor` annotated
- `WorkOrders/WORK_ORDER_1823_text_too_small_to_read_legibility_audit.md` — Status flipped to
  IMPLEMENTED, implementation section appended
- `WorkOrders/WORK_ORDER_1823_text_too_small_to_read_legibility_audit.RESULT.md` — this file
