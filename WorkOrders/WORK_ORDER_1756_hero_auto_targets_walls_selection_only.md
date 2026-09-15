# WORK ORDER 1756 — The hero AUTO-TARGETS walls; a wall must be SELECTED, never acquired

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, from the owner's felt-test on tester build `2026.09.15.371285`.
**Silo:** `Assets/_Modules/Village/Hero/HeroTargetIndicator.cs` ONLY (+ its regression). ⛔ NOT `RaidAssaultAi.cs` / `TroopController.cs` (that is troop targeting, WO-1752, landed) — this is the HERO's reticle.

## Owner ruling, verbatim
> ***"i auto target the wall, i should need to select it"***

A `WallSegment` is **never** auto-acquirable by the hero. The player taps a wall to target it, exactly as Breach is a deliberate tap for the warband (WO-1752). Once tapped it behaves as a normal target; it simply must never be CHOSEN for her.

## Why WO-1734 did not already do this
WO-1734 made units win over walls — `NearestCandidateOfClass(unitsOnly: true)` first, then `if (!unitWon) pick = NearestCandidateOfClass(unitsOnly: false)` (`HeroTargetIndicator.cs:1217-1219`). That second call is the fallback that still admits walls, and with the garrison dead it is the ONLY branch left, so the reticle snaps to masonry. `WallSegment` reports `Hostile` whenever the scene is enemy-owned (`WallSegment.cs:219`), so it is a legitimate hostile to every generic check — the file's own comments at `:1010-1011` and `:1199` record this.

## The work
1. Exclude `WallSegment` from **auto-acquire** entirely — the fallback included. A structure that is not a wall (tower, gate, spire) is unchanged: the owner ruled on walls, not on structures.
2. **Manual selection must still work.** The player's tap path and `IsStillAutoAcquirable`'s stickiness are different questions — a wall the player CHOSE must not be dropped by the auto-acquire rules on the next frame. Read `:1282` and `:1303-1312` and keep the held-selection path intact; say in the hand-back exactly how a selected wall survives.
3. Trace: the reticle's admit line should say when a wall was refused for auto-acquire, so the next capture proves the rule rather than implying it.
4. Regression: extend `HeroUnitOverWallTargetingRegression` — auto-acquire with ONLY walls in range yields NO target; a wall the player selected stays selected.

## Acceptance
- Headless: with a wall the nearest hostile and no units, auto-acquire returns nothing.
- On device the owner's reticle never lands on a wall she did not tap.
- Lane flips this Status line and writes the `.RESULT.md`.
