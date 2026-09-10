# WO-1468: the ITEM charge badge renders outside the ability bar frame

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/HUD/` ability bar ITEM face.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1468 -> 1469 in the same edit).

## 1. EVIDENCE

Two independent surfaces, same defect:

```
Builds/ui-capture/AdaptiveHudCombat_2670x1200.png   the charge "0" sits outside the frame
device build 358574                                 the charge "7" sits outside the frame
```

The badge is positioned relative to something other than the face rect, so it escapes the bar's frame at both
the headless and the device aspect.

## 2. FIX SHAPE

- Anchor the charge badge INSIDE the ITEM face rect (corner-anchored within the face, as the other faces'
  overlays are), not relative to the bar or the screen.
- Add a measured case: the badge rect is CONTAINED by the ability bar frame rect at every captured aspect.

## 3. WHAT NOT TO DO
- Do not hardcode a pixel offset for one resolution; both captures are different aspects and a fixed offset
  fixes at most one.

## 4. ACCEPTANCE
- [ ] Badge contained by the frame in a fresh `AdaptiveHudCombat` capture, opened in the RESULT.
- [ ] Measured containment case, RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.
