# WORK ORDER 1585 - "Attacks on your town": the map plate's labels ("1st BREACH", "HEART") draw over the report's text rows

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:07:40, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the owner's Seeker screenshot
**Silo / Lane:** Core/UI defense report - `Assets/_Modules/Core/UI/DefenseMapPlate.cs`, the report panel in `Assets/_Modules/HUD/Kit/HudKitController.cs` (search "ATTACKS ON YOUR TOWN"), `Assets/_Modules/Core/HudModel/DefenseReportChipModel.cs`
**Type:** EXISTING system, LAYOUT DEFECT
**Priority:** P2

## Evidence

`Logs/device/seeker-shots/Screenshot_20260907-052735.png` (Seeker, build 2026.09.07.359076). Right column
of the report: the text rows ("1st BREACH: Open ground at 24s (south-west of the Heart)", "They came from
the west.", "o the Heart", "^ where they broke in (the first is labelled)", "# destroyed") and the map plate
occupy the SAME rectangle. The plate's big yellow labels "1st BREACH" / "HEART" (wrapped mid-word:
"BREA / CH", "HEAR / T") sit on top of the sentences; the plate's frame crosses the first text row; the
top line is clipped at the panel edge ("1st BREACH: Open ground..." cut at the top). Left column: three
attack rows fill a third of their well, the rest is empty.

## What to do

- **Instrument first:** `FlowTrace.Step("DefenseReport", ...)` logging the measured rects of the text
  block and the plate, and the plate's label font size vs plate width, then run the headless capture for
  this panel (find/extend the capture entry) and read them. Confirm from data which element is laid out
  over which (anchors, a missing vertical layout, or a fixed plate height ignoring the text).
- Lay out the right column as a VERTICAL stack: summary lines, then the plate at a height derived from
  the remaining well, then the legend lines ("o the Heart", "^ where they broke in", "# destroyed") BELOW
  the plate, never under it. Plate labels: size from the plate's width so "1st BREACH" and "HEART" never
  wrap; if they cannot fit at MinTouchPx-legible size, use the legend glyphs on the plate and the words in
  the legend.
- Left column: rows at the touch floor with the well filled (the owner's ruling 29 applies to every panel).
- Regression: extend the touch/overlap oracle (WO-1060 family) with this panel - no text rect intersects the
  plate rect; no plate label wraps.

## Not to touch
- The siege/defense simulation and the ledger (`DefenseReport.cs`, `DefenseReportLedger.cs`) - data, not
  presentation; this is a View fix (presentation never touches the objects).

## Acceptance
- Headless capture shows the plate below the summary and above the legend, no overlap, labels on one line.
- Overlap oracle green; REGRESSION_OK n/n on a fresh log. Owner felt-test closes.
