# WO-1474: the Echo harvest split ignores the authored perEchoBaseRate; three rates are hardcoded and the header misstates the code

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Core/Echoes/EchoBonusCalculator.cs` + `EchoBalanceCatalog.cs` +
`echoes-balance.json`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1474 -> 1475 in the same edit).

## 1. EVIDENCE

```
EchoBalanceCatalog.cs:122   BaseRateFor(...)   -- ZERO callers repo-wide
EchoBonusCalculator.HarvestTargetWeights()     -- uses private consts 3600 / 900 / 4
EchoBonusCalculator.cs:170  header claims the weight is BaseRateFor(id) * level
```

Live proof from the device log:

```
DumpSilos split ... weights [W 7200 / I 3600 / F 3600]
```

Those are the hardcoded constants, not the authored rows. So the authored `perEchoBaseRate` knob is dead, the
header describes a computation that does not happen, and the WO-1331 remote-retune seam cannot reach any of
the three rates because they are C# literals.

## 2. FIX SHAPE

- Pick ONE authority: either wire `HarvestTargetWeights()` to `BaseRateFor`, or delete `BaseRateFor` as a dead
  knob. Do not leave both.
- Whichever way it goes, move the three rates into `echoes-balance.json` so the WO-1331 remote-retune seam can
  reach them.
- Correct the `EchoBonusCalculator.cs:170` header to describe what the code does, in the same commit.

## 3. WHAT NOT TO DO
- Do not change the effective numbers while wiring. This is a plumbing change; the live split must be
  identical before and after unless the owner rules otherwise (the ratio is balance she has approved).

## 4. ACCEPTANCE
- [ ] One authority; the other deleted. File:line in the RESULT.
- [ ] The three rates authored in `echoes-balance.json`; edited from HEAD bytes with the LF count proven
      (memory `canonical-json-edits-binary-only-verify-newlines`).
- [ ] Regression asserts the split is UNCHANGED across the refactor.
- [ ] `REGRESSION_OK n/n` on a fresh log.
