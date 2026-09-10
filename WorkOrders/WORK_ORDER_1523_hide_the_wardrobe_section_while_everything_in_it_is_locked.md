# WO-1523: hide the Wardrobe section on the Hero screen while everything in it is locked

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: READY TO IMPLEMENT - owner ruling, 2026-09-06 20:23)
**Silo:** Hero screen / cosmetics - the hero wardrobe section (`CosmeticShopPanel` or the hero screen's
wardrobe block).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1523 -> 1524 in the same edit).

## 1. EVIDENCE

Owner ruling, verbatim:

> "everything in wardobe is locked so dont show the section in hero"

A section in which every row is locked teaches the player nothing and costs a screenful of the Hero page. It
is the same judgement she made for the Manage BUILD grid at 20:07 (WO-1516) - do not show what cannot be
acted on - applied to a second surface.

## 2. FIX SHAPE

- The VM exposes `WardrobeHasUnlocked`; the View builds the section ONLY when it is true. The View computes
  nothing - same contract as every other Manage/Hero surface.
- When the first cosmetic unlocks, the section appears carrying a `NEW` word so the player notices it arrive.

## 3. WHAT NOT TO DO
- **Do not delete the wardrobe.** This hides an empty section; the feature stands and returns the moment
  something unlocks.
- Do not hide it by collapsing to zero height - it must not be in the tree at all, or a measured layout case
  will still find it.

## 4. ACCEPTANCE
- [ ] Source or measured case: with ZERO unlocked cosmetics, the wardrobe section is ABSENT from the Hero screen.
- [ ] Measured case: with one unlocked cosmetic, the section is present and carries the `NEW` word.
- [ ] A fresh Hero screen capture opened in the RESULT.
- [ ] `REGRESSION_OK n/n` on a fresh log.
