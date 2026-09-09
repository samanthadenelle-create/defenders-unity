# WO-2018 RESULT — Manage category-first navigation, detail space, and Army frames

**Status:** IMPLEMENTED — AWAITING OWNER DEVICE MATCH

**Date:** 2026-09-08

## Implemented

- BUILD opens on one clean row of four ordered category cards; ALL is now an internal root sentinel only.
- Category cards show only a representative authored portrait and the category name; the oversized count banners were removed after eyes-on review.
- Category selection opens a 5x2 item grid; its heading names the selected category.
- BACK returns from the category item grid to the 2x2 category root.
- State medallions are smaller/inset, state words use rounded dark plates, and name footers have stronger contrast.
- Building detail art is larger, facts keep their authored content, and the action anchors low when space permits.
- Army tiles carry `ContainPortrait`; frame and portrait share one clipped square seat and the state pill ends before it.

## Regression/capture changes

- `ManageProgressiveDisclosureRegression` asserts category order, one-row root geometry, selection, BACK, item authority, Army containment, card hierarchy, and detail lower-well usage.
- `ManageMockupConformanceRegression` preserves full-cell BUILD art while accepting explicit Army containment.
- `ManageBuildDoorRegression` traverses all categories before testing exact per-id BUILD doors.
- `UICaptureLaunch` traverses category → item for BUILD detail proof frames.

## Fresh evidence

- `Builds/wo2018-compile.log`: `COMPILE_GATE_OK :: scripts compiled clean`. The optional WebGL pass reports the pre-existing Solana package/module reference advisory and explicitly classifies it as package-only.
- `Builds/wo2018-regression-final2.log`: `REGRESSION_OK 456/456 suites -- 456 green, 0 red, 0 skipped`.
- `Builds/wo2018-manage-capture-final.log`: `MANAGE_FLOW_MAP_OK`, with geometry=0 and touch=0.
- Eyes-on opened: `ManageFlow_BUILD_gridtop_2670x1200.png`, `ManageFlow_BUILD_action_2670x1200.png`, and `ManageFlow_ARMY_gridtop_2670x1200.png`.

## Closure

Implementation and automated proof are complete. Per Manage ruling 29, only the owner can move this ticket to DONE after judging a fresh device frame.
