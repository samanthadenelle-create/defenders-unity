# WO-1383: "YOUR REALM WORKED FOR 0m" after 12.6 hours - the resume claim races the cold-load claim; and the label never shows hours and minutes

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:13, build 2026.09.08.361259). PRIOR STATUS: FIXED - in 65d5a7eae, on the Seeker in build 2026.09.05.355952 (installed 2026-09-04 ~23:40); device proof on 355952: SKIPPED/DEFERRED/RELEASED claim lines, one claim per window, "YOUR REALM WORKED FOR 27m". Awaiting owner felt-test (leave the app for a few minutes, reopen: the popup shows the elapsed time and claims once). Lane history: edit-only lane 23:03 on the two Harvest files + two oracles.

**Owner (2026-09-04 23:02), verbatim:** "can i ask why it says your realm worked for 0m two issues
should be some time that passed and minutes and hours if applicable should show"

## The captured data (Seeker, build 355872, cold launch 22:29:59, pid 28564 - read off `adb logcat`)

```
[Flow:Offline]   -> Claim(resume)
[Flow:Offline]     Claim #1 (resume): ONE delta = 45328s (12.59h) from 1788533671720 to 1788578999262; fanning out to 3 consumer(s).
[Flow:Offline]     claim #1 (resume): clock advanced ONCE to 1788578999262 and persisted.
[Flow:Offline]     claim #1: away summary gate -> haul=0, mendNews=False, jobs=0, collectorsPending=16716 across 3 collector(s) => REVEAL.
[Flow:Offline]   <- Claim(resume) (17.2ms)
[Flow:Offline]   -> Claim(cold-load)
[Flow:Offline]     Claim #2 (cold-load): ONE delta = 0s (0.00h) from 1788578999262 to 1788578999300; fanning out to 3 consumer(s).
[Flow:Offline]     claim #2: away summary gate -> ... => REVEAL.
```

## Two defects, both proven

1. **Double claim on a cold launch.** Android delivers `OnApplicationPause(false)` during boot, so
   `ClaimDeferred("resume")` (`OfflineHarvestService.cs:161-171`) fires alongside
   `ClaimDeferred("cold-load")` (`:158`). Both run `ClaimAfterTwoFrames` -> `ClaimAccrual` (`:179`,
   `:210`), which advances the persisted clock. Claim #1 measured the real 45,328 s window and revealed;
   claim #2, 17 ms later, measured 0 s and its reveal REPLACED the first (`WelcomeBackPopup.Show`
   dismisses `s_active` and rebuilds). The player was away 12.6 h and was told 0m. The accrual itself
   is not lost - claim #1 banked it - only the report is wrong.
2. **The label prints `h` OR `m`, never both** (`WelcomeBackPopup.AwayText`, `:339-343`):
   `hours >= 1 ? "{hours:0.#}h" : "{minutes}m"`. 12.59 h renders `12.6h`; 45 s renders `0m`.

## The fix (lane brief)
- Service: one claim per launch window - a latch keyed on the claim sequence; the second trigger logs
  `SKIPPED` and neither re-claims nor re-reveals. A genuine resume after a real pause still claims.
  Popup: never replace an open summary with one whose AwaySeconds is smaller.
- Popup: `12h 35m` / `35m` / `under 1m` / `1d 1h`; `(STORAGE FULL)` suffix kept.
- Oracles: `OfflineClaimFanOutRegression` gains a cold-load-then-resume case asserting ONE clock advance
  and ONE reveal; `AwaySummaryReportRegression` gains `[away-text-h-m]` over the formatter.

## Acceptance
- [ ] Cold launch after >1 h away: exactly one `Claim #` line advances the clock; one REVEAL; the popup
      reads the real span with hours and minutes.
- [ ] Both oracles green in `DataRegression.RunAll`; the fan-out case proven RED first (mutation recorded
      in the RESULT).
- [ ] Owner felt-test on the Seeker after the next build.

## Related
WO-1231 (mend news), Lane G (away summary rows, commit d1fd1f6e0), WO-1128 (clock trust - unchanged here).
