# WO-1495: thirteen regression allowlists have no dated pointer and no expiry

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Regression harness. Thirteen exemption blocks across the suites.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1495 -> 1496 in the same edit).

## 1. EVIDENCE

The four largest, all undated:

```
MageAbilityIconRegression.cs:57            KnownGaps        ~28 entries, undated
EnemyPoolResetRegression.cs:198            BrainExempt      ~23 entries, undated
UiObsidianConformanceRegression.cs:104                      ~19 entries, cites WO-178, no date
ShaderPredicateSingleAuthorityRegression.cs:148             ~12 entries, undated
```

Thirteen blocks in total. An exemption with no date and no owning ticket is indistinguishable from a defect
someone decided to stop looking at - and it never expires, so the suite reports green forever on the exact
content it was written to cover.

The contrast is `ManagePortraitCoverageRegression` (WO-1487), whose exemption list IS dated and therefore
functions as a worklist rather than a hiding place.

## 2. FIX SHAPE

- Every exemption entry carries a WO number, a date, and a remove-by. Entries whose reason nobody can
  reconstruct come OUT and the suite goes red - that is the honest state.
- Add a RATCHET suite that fails on any exemption entry lacking those three fields, and fails when a block
  grows. That is the durable half.

## 3. WHAT NOT TO DO
- Do not date the entries with today to make the ratchet pass. An entry whose origin is unknown is not
  exempt-from-today; it is unproven and should go red.

## 4. ACCEPTANCE
- [ ] All thirteen blocks carry WO + date + remove-by, or the entries are removed.
- [ ] The ratchet suite exists; RED proof stated by adding a bare entry.
- [ ] `REGRESSION_OK n/n` on a fresh log, with any newly-red suites either fixed or ticketed.
