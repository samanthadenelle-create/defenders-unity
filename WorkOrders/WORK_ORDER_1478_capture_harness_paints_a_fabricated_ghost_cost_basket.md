# WO-1478: a FABRICATED cost basket is hardcoded in the capture harness and has propagated into six files

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/Editor/UICaptureLaunch.cs` + `BuildPreviewModal` docstring + four documents.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1478 -> 1479 in the same edit).

## 1. EVIDENCE

The source of the fiction:

```
Assets/Editor/UICaptureLaunch.cs:4490
  SetPlacingLabel("Arcane Spire", "88 wood, 88 iron, 187 crystals");
```

The AUTHORED catalog row for Arcane Spire is `iron: 360` only. The fabricated string mixes wood + iron +
crystals in one basket - the exact shape `CostBasketSeparationRegression` case 1 FORBIDS (WO-947: regular
structures are wood+iron, magical are crystal-based, never all three).

It has since been copied into five more places, and it misled BOTH independent UI reviewers:

```
Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs   BuildCostTimeLine docstring quotes it
WorkOrders/WORK_ORDER_1010_build_ui_carousel_minimize.md
WorkOrders/WORK_ORDER_1411_build_never_says_what_you_can_afford.md
docs/qa/UI_REVIEW_2026-09-05/REVIEW_A_independent.md
docs/qa/UI_REVIEW_2026-09-05/REVIEW_MERGED.md
```

A capture that paints numbers the game does not use is not a capture of the game.

## 2. FIX SHAPE

- The capture reads the LIVE catalog row for whatever structure it is placing. No literal cost string anywhere
  in `UICaptureLaunch.cs`.
- Scrub the fabricated basket from all six files. In the two review documents add a one-line correction rather
  than rewriting the body (frozen ledgers, CLAUDE.md sec.15).
- Point `CostBasketSeparationRegression` at the capture harness too, so a hardcoded illegal basket in EDITOR
  code fails the same oracle that guards the catalog.

## 3. WHAT NOT TO DO
- Do not "correct" the literal to `360 iron`. Any literal re-rots the moment the catalog is retuned.

## 4. ACCEPTANCE
- [ ] Zero hits for `88 wood` repo-wide (grep pasted in the RESULT).
- [ ] The capture paints the authored row; a fresh Build PNG opened showing `360 iron`.
- [ ] `CostBasketSeparationRegression` covers editor-authored cost strings; RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.
