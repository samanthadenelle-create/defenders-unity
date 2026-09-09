# WO-1494: six suites claim to MEASURE and are source-text lint; about 40% of the harness is text matching

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:51, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT
**Silo:** Regression harness. Start with the two HUD suites.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1494 -> 1495 in the same edit).

## 1. EVIDENCE

```
HudUiRegression.cs:1578-1596        ReadAllText + IndexOf
HudUiRegression.cs:1569             justification: "asmdef does not reference DeNelle.HUD"  -- FALSE:
                                    DeNelle.EditorRegression.asmdef DOES reference DeNelle.HUD
HudLabelFitRegression.cs:18,25      cites MeasureLineWidthPx -- that identifier appears ONLY in the comment;
HudLabelFitRegression.cs:257        the checks are RequireLiteral
CollectorIncomeRegression.cs:50,850-880
InventoryArmoryRailRegression.cs:4-5,150
UiCaptureFidelityRegression.cs:558
```

Harness-wide classification from this session: **167 lint / 172 hybrid / 77 measured**.

A suite that greps its own source cannot see the built visual tree; it goes green on code that compiles and
renders wrongly. `HudLabelFitRegression` is the suite that was supposed to catch the ellipsised Night Market
caption (WO-1466) and structurally could not.

## 2. FIX SHAPE

- Start with the two HUD suites, because the blocker for both is FALSE: the asmdef reference exists, so they
  can build the tree and MEASURE. Replace `RequireLiteral` with real rect/width measurement.
- For the other four: either make them measure, or correct their headers and names so they honestly say
  "lint". A lint suite is legitimate; a lint suite CLAIMING to measure is not.
- Fix the false justification comment at `HudUiRegression.cs:1569` in the same commit.

## 3. WHAT NOT TO DO
- Do not delete the lint checks when adding measurement; source lint still catches a deleted call site.

## 4. ACCEPTANCE
- [ ] The two HUD suites measure the built tree; RED proof stated by moving a label out of its box.
- [ ] The remaining four either measure or are renamed/re-headed as lint.
- [ ] The false asmdef comment corrected.
- [ ] `REGRESSION_OK n/n` on a fresh log.
