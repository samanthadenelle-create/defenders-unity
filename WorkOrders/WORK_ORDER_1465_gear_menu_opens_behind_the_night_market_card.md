# WO-1465: the gear menu opens BEHIND the Night Market card, and PAUSE lands on the joystick ring

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/HUD/` gear menu sort order + the Night Market HUD card.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1465 -> 1466 in the same edit).

## 1. EVIDENCE

`Builds/ui-capture/AdaptiveHudGearOpen_2670x1200.png`, captured at HEAD 2026-09-05 23:57:

the menu's first row reads `...ERBOARD` - the word LEADERBOARD is occluded by the Night Market card, which
sits above the menu in the draw order. The same frame shows the PAUSE face landing on top of the joystick
ring, so tapping pause and moving share a rect.

The card was added to the HUD after the gear menu; nothing re-established which of the two is on top.

## 2. FIX SHAPE

- Raise the gear menu above the Night Market card by sort order / sibling index at the point the menu OPENS,
  so the card cannot occlude a menu the player deliberately opened.
- Move PAUSE clear of the joystick ring rect.
- Add a MEASURED overlap case: gear menu open -> no HUD element intersects its rows; PAUSE rect does not
  intersect the joystick rect.

## 3. WHAT NOT TO DO
- Do not hide the Night Market card while the menu is open unless the owner rules it; the card is a live
  monetisation surface (WO-1335).

## 4. ACCEPTANCE
- [ ] Fresh `AdaptiveHudGearOpen` capture opened; LEADERBOARD fully legible.
- [ ] Measured overlap case, RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.
