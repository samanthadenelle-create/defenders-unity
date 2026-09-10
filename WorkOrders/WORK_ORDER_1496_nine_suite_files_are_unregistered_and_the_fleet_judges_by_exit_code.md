# WO-1496: nine suite files exist but are unregistered, the fleet asserts file existence, and one suite silently returns in batchmode

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Regression harness + `run-autopilot-fleet.ps1`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1496 -> 1497 in the same edit).

## 1. EVIDENCE

Suite files present in the tree and registered nowhere - so never executed:

```
ArenaCombatOracle          AssetMoveManifestRegression   BlankStartCensusRegression
CombatFoundationRegression EnemyArtCoverageRegression    GearAddressableGroupRegression
RepairProbeRegression
```

(`BuildAffordabilityWordsRegression` and `TroopTargetPreferenceRegression` are pending in tonight's lanes.)

The fleet then checks for the FILE, not for a result:

```
run-autopilot-fleet.ps1:184,209    asserts file existence; judges the run by EXIT CODE
```

which this repo's runners return 0 on for refusals and FAILs alike. And one registered suite quietly does
nothing:

```
DestroyedStructureRegression.cs:204-210   early return in batchmode, with NO Skip token emitted
```

So it counts toward `REGRESSION_OK n/n` while testing nothing.

## 2. FIX SHAPE

- Register the seven unregistered suites, or DELETE them. A suite file that no entry point runs is worse than
  no suite: it reads as coverage.
- The fleet judges by the MARKER on a fresh log, never by exit code (CLAUDE.md sec.8; memory
  `gates-report-success-without-proving-it`).
- `DestroyedStructureRegression` emits an explicit SKIP token on its batchmode early return, and the marker
  line reports skips separately from passes.

## 3. WHAT NOT TO DO
- Do not register a suite that fails and then exempt its cases. If it goes red, that is the finding - ticket it.

## 4. ACCEPTANCE
- [ ] Zero suite files unregistered (a check that enumerates suite types vs the registry, pasted).
- [ ] The fleet refuses on a missing marker; proven by deleting the marker line from a log.
- [ ] Skips reported separately in the marker line.
- [ ] `REGRESSION_OK n/n` on a fresh log, with the new n stated.
