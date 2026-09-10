# WO-1493: SessionRegression has NEVER run, and its SESSION_GUARDS_OK 6/6 is a hardcoded label

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/Editor/Regression/SessionRegression.cs` + `tools/regression/checkin_gate.ps1`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1493 -> 1494 in the same edit).

## 1. EVIDENCE

The marker appears in ZERO logs:

```
grep -r "SESSION_GUARDS_OK" Builds/ logs/     ->   no hits
```

It is not a stage in `tools/regression/checkin_gate.ps1`. And the count is a string literal:

```
SessionRegression.cs:71   prints "SESSION_GUARDS_OK 6/6 checks"   regardless of how many checks ran
```

(audit G8 in `RegressionMarkerRegression.cs:971` records the same finding).

So one of the three distinct gate markers CLAUDE.md sec.8 established has never fired, and if it did fire it
would report 6/6 whether it ran six checks, one, or none. This is precisely the
`gates-report-success-without-proving-it` class.

## 2. FIX SHAPE

- Add `SessionRegression.RunAll` as a stage in `checkin_gate.ps1`, judged by the marker on a FRESH log, not by
  exit code.
- DERIVE the count from the checks actually executed; never a literal in the format string.
- Add a case to `RegressionMarkerRegression` that fails if any marker's count is a literal.

## 3. WHAT NOT TO DO
- Do not delete the suite because it never ran. It contains real guards; the defect is that nobody invoked it.

## 4. ACCEPTANCE
- [ ] `SESSION_GUARDS_OK <n>/<n>` on a FRESH log, with n derived; paste the line and the log mtime.
- [ ] The gate stage exists and the gate goes red when a session guard fails.
- [ ] `REGRESSION_OK n/n` on a fresh log.
