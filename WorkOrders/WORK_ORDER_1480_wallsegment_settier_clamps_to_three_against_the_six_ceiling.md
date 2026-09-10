# WO-1480: WallSegment.SetTier clamps 1..3 as GAMEPLAY, against the RepoProps ceiling of 6

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Village/Buildings/WallSegment.cs`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1480 -> 1481 in the same edit).

## 1. EVIDENCE

```
WallSegment.cs:156   SetTier clamps the tier to 1..3
```

and the tier drives the per-tier DAMAGE DIVISOR, so this is a gameplay clamp, not a cosmetic one.

`RepoProps.MaxStructureLevel = 6` (`Assets/_Modules/Core/Catalog/RepoProps.cs:69`) is the single structure
ceiling; WO-1108b established it precisely by replacing eight hardcoded 3s. This is a ninth that was missed.

Latent today only because `wall_wood` has `maxLevel: 2`. The moment walls are authored past 3, a level-4 wall
takes level-3 damage reduction and nothing reports it.

## 2. FIX SHAPE

- Clamp to `RepoProps.MaxStructureLevel`, or to an AUTHORED wall ceiling read from the catalog. Never a literal.
- Extend the damage-divisor table to cover the full range, or derive it, so a level the clamp now admits has a
  defined divisor.
- Regression: assert no literal level ceiling exists in `Village/Buildings` (the WO-1108b oracle, widened).

## 3. WHAT NOT TO DO
- Do not raise the literal from 3 to 6. That is the same defect one number later.

## 4. ACCEPTANCE
- [ ] `SetTier` reads the ceiling from `RepoProps` or the catalog; file:line in the RESULT.
- [ ] Divisor defined for every admissible level.
- [ ] Literal-ceiling regression widened; RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.
