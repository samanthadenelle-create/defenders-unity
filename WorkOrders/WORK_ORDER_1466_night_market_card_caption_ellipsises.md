# WO-1466: the Night Market card caption ellipsises and sits outside the label-fit oracle entirely

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** the Night Market HUD card + `Assets/Editor/Regression/HudLabelFitRegression.cs`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1466 -> 1467 in the same edit).

## 1. EVIDENCE

The caption renders cut on BOTH surfaces read this session:

```
"THE NIGHT MA..."
```

- in the HEAD headless capture frame, and
- on device build 357453.

`HudLabelFitRegression`'s authored box list contains NO entry for the card caption. The oracle that exists
precisely to catch ellipsised HUD labels has never been pointed at this one, which is why the cut survived
two builds and an owner felt-test.

## 2. FIX SHAPE

- Add the card caption box to `HudLabelFitRegression`'s box list, so the defect is caught by the suite that
  owns this class of defect.
- Then fix the cut: `FitSingleLine` on the caption, or shorter authored copy. Copy is the owner's call if the
  fit needs more than the shrink allows - raise it with the tool if so.

## 3. WHAT NOT TO DO
- Do not widen the card to fit the words; the card geometry matches the approved HUD layout.
- Do not add a bespoke fit check next to the card. Use the existing oracle.

## 4. ACCEPTANCE
- [ ] Caption box present in `HudLabelFitRegression`; RED proof stated (it fails today).
- [ ] Caption renders complete in a fresh headless capture, opened in the RESULT.
- [ ] `REGRESSION_OK n/n` on a fresh log.
