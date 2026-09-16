# WO-2016 implementation handoff — 2026-09-13

**Status: Manage flow and operational captures verified by root; final regression/release gates pending. Owner acceptance not claimed.**

## Scope and preserved work

The initial capture lane changed `Assets/Editor/UICaptureLaunch.cs` and this result. The measured
operational follow-up also corrected `ManageWorkspacePanel.cs` `BuildListRow` as described below.
Existing dirty changes in
`LayoutOracle.cs`, `UiTouchClampRegression.cs`, and the capture harness were preserved. No scenes,
art assets, catalog files, user saves, commits, builds, or external services were changed for WO-2016.

The existing scroll implementation intersects controls with all active ancestor masks, skips fully
clipped controls, and independently checks clamped content endpoints, child extents, excess gaps,
and final-row reachability. The existing touch regression exercises positive and deliberately broken
visibility/endpoint fixtures, including nested masks and scaled padding. These were reviewed, not
rewritten or reported freshly passing.

## Changes

- Isolate all three seeded Manage capture paths behind an in-memory `ISaveProvider`, restored after
  cleanup by a `using` scope. Every frame asserts that real fixture saves occurred and that the
  original provider's save slot stayed unchanged. Live-queue captures also restore the last-tab pref.
- Extend the flow-map plan with each BUILD category at both endpoints; empty queues on every channel;
  in-progress and queue-blocked details; BUILD unaffordable detail; and a max-track ARMY troop whose
  training action remains enabled. New frames check the actual composed state before photographing it.
- Capture Heart Ready, MissingCrystals, Max, and explicitly synthetic MissingPrerequisite. The
  prerequisite fixture uses `HeartProgressionCatalog.LoadForTests` and reloads the real catalog in
  `finally`. Ready also requires a nonempty model-derived unlock preview. No upgrade is executed.
- Apply independent scroll-bound checks to real overflowing grids as well as the synthetic inventory
  stress fixture. Measure rendered/model tile agreement and fully visible capacity; category content
  cannot exceed one viewport of additional scrolling.

The save isolation is justified by captured evidence, not inference: `Builds/wave10i-manageflow1`
lines 528 and 566–580 report `wrote signed save via LocalSaveProvider`. The seeding path calls real
`BuildTimerService.Enqueue`, which persists through `GameStateService.Save`; swapping the state
singleton alone did not isolate that IO. Service-local save timestamps stay on the throwaway service.

## Current-product reconciliation

- BUILD/ALL is now a category chooser, not the former aggregate building grid. The actual category
  routes are captured, while `synthetic-overflow-top/bottom` explicitly stress the real renderer with
  aggregate real inventory. These are not represented as player-reachable BUILD/ALL captures.
- The current model authors BUILD capacity as five columns by two rows, ARMY as three by three,
  and derives research picker capacity from its schools. Numeric checks use that production model;
  the old ticket's requirement for twelve BUILD tiles is not claimed met.
- Heart upgrade authority is instant (`VillageTierService` / `HeartProgression`). There is no queued
  or upgrading Heart state. Those two original capture requirements are inapplicable to this product,
  rather than silently passed. Prerequisites exist in the model but current catalog requirements are
  empty, hence the explicitly synthetic prerequisite frame.
- BUILD's owned/unlocked inventory does not expose the former Locked grid tile route. Existing
  capture-plan reconciliation references WO-1516; the Heart prerequisite surface has separate proof.

## Fresh root execution and visual review

- `Builds/night-manage-flow.log`: `MANAGE_FLOW_MAP_OK 45 frames`, with clean geometry and touch
  across 45 canvases and `UI_GLYPH_OK` for 724 labels. Root and the reviewing agent collectively
  inspected all 45 frames (root 12, reviewing agent 33). Three portrait images retain known cropped
  baked-in titles; these are cosmetic art text, not truncated live labels or controls.
- `Builds/night-manage-operational.log`: real baseline failure, 12/12 frames captured but nine
  geometry and nine touch failures. Priced research-row buttons were only 94.7–96 reference pixels
  tall against the 112-pixel floor. Nine READY labels drew only two of five glyphs because the
  side-by-side icon left approximately 42 pixels for text.
- The bounded production fix reserves 116 reference pixels for a priced row button, then assigns
  its cost a separate lower band. Action rows stack the status icon above the word within the same
  column. Row height, model state and all geometry/touch/glyph assertions remain unchanged.
- `Builds/night-manage-operational-after.log`: `MANAGE_OPERATIONAL_CAPTURE_OK 12/12`, geometry and
  touch clean, all 192 labels clean. Root opened all three school captures and confirmed READY is
  fully readable, prices remain separate, and controls do not overlap.

The 45-frame flow pass predates this final row-layout correction. The subsequent 12-frame operational
pass specifically covers that corrected school row at all three landscape ratios; do not relabel the
earlier flow run as a rerun of the later tree. The subsequent `Builds/night-ui-general.log` reports
`UI_CAPTURE_OK 106`, clean geometry/touch and 998 glyph-checked labels (one existing baseline),
but also `UI_ENDSTATE_FIT_FAIL x2`; therefore general UI is **not** fully passing.
`Builds/night-ui-secondary.log` reports a separate fresh failure: 36/36 frames, nine geometry,
twelve touch and 28 glyph findings; the geometry/touch findings name PartyShop's scroll row
overlapping Purchase. Secondary correction/reproof, final full regression and release artifacts
remain pending root gates.

## Root execution checklist — execution remains root-owned

1. Compile gate, then `DeNelle.Editor.DataRegression.RunAll` on the frozen final tree. Require fresh
   `COMPILE_GATE_OK` / `REGRESSION_OK` and the included `UI_TOUCH_ORACLE_OK` (the existing
   `UiTouchClampRegression`). Do not infer success from process exit code.
2. Run `DeNelle.Editor.UICaptureLaunch.RunManageFlowMapCaptureHeadless`. Require
   `MANAGE_FLOW_MAP_OK`, `UI_CAPTURE_FIDELITY_OK`, `UI_GEOMETRY_OK`, `UI_TOUCH_OK`, and
   `UI_GLYPH_OK`, plus `MANAGE_CAPTURE_SAVE_ISOLATION_OK`, `MANAGE_SCROLL_OK`, and
   `MANAGE_GRID_CAPACITY_OK` for the applicable frames. The plan owns expected counts.
3. Open the fresh `Builds/ui-capture/ManageFlow_*.png` artifacts, especially
   `ManageFlow_BUILD_synthetic-overflow-bottom_2670x1200.png`, each `category-bottom-*`,
   all new `heart-*`, `queue-empty`, `unaffordable`, `in-progress`, `queue-blocked`, and
   `max-trainable` frames. Inspect labels, primary actions, the last row, and unlock preview.
4. For additional landscape-aspect coverage run
   `DeNelle.Editor.UICaptureLaunch.RunManageOperationalCaptureHeadless` and require
   `MANAGE_OPERATIONAL_CAPTURE_OK` plus its geometry/touch/glyph and isolation markers.

Capture output is `Builds/ui-capture/`; the flow-map ledger removes only its owned/retired PNG names
before capturing. The root owns Unity execution, log naming, artifact review, and acceptance decisions.

## Local checks

`python tools/gate_brace.py Assets/Editor/UICaptureLaunch.cs`: passed after edits.
Raw brace equality and NUL-byte check: passed. `git diff --check` on the edited C# file: passed.
These are source hygiene checks, not substitutes for compiling or executing the expanded matrix.

Historical `Builds/wave10i-manageflow1` lines 8856–8858 report clean geometry/touch/glyph for its older
matrix. That run predates this harness growth and does not verify this result.

## Current night execution evidence

`Builds/night-manage-flow.log`: 45/45 PASS; geometry/touch clean and 724 glyph labels clean. Root opened12 frames; the review agent opened27 additional distinct frames and verified six byte-identical duplicates by hash, covering the45-frame set. Three portrait baked-title crops remain cosmetic; no live control defects were identified in that review.

Operational baseline had 9 geometry and 9 touch failures in school rows (and glyph findings). `ManageWorkspacePanel.BuildListRow` now reserves a real 116px priced action height, separate cost line, and full-width READY status word below its icon. `Builds/night-manage-operational-after.log`: 12/12 PASS, geometry/touch clean and 192 glyph labels clean. Root opened three school frames and verified READY, costs, and controls are separate.

Broader secondary capture: baseline 9 geometry / 12 touch / 28 glyph failures; after corrections `Builds/night-ui-secondary-final.log` passes 36/36 and 352 glyph labels. Actual review of 18 images exposed multiline text outside painted button faces despite the rect-based pass. A bounded face correction is now awaiting fresh capture; see `Builds/night-secondary-ui-repair.md`. This result does not close the ticket or establish final release regression/build delivery.

## Fresh secondary painted-face result

`Builds/night-ui-secondary-painted.log` passes 36/36 frames, geometry/touch clean, 352 glyph labels with zero baseline/unproved exceptions. Opened all 18 affected current screenshots across all three aspects. The repaired Alchemy/Jeweler recipe plates, Alchemy blocker CTA, BuildingUpgrade multiline instructions and SeasonTrack claim instruction now contain their complete text. Prices and controls remain separate; PartyShop row/Purchase overlap remains fixed. Normal masked scrolling is retained. CosmeticShop VILLAGE still hugs the ornament edge but remains complete; BuildingUpgrade remains globally dim as in the prior fixture. Detailed scope and offscreen-content limits are in `Builds/night-secondary-ui-repair.md`. Painted-face review now PASS for the repaired visible controls; broader general/final regression and release delivery remain root-owned and are not inferred from this result.
