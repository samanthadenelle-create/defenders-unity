# WO-1414 RESULT - a new game claims nothing, and it is on HEAD

**Status:** IMPLEMENTED - `6a5c7a36d` on HEAD 2026-09-09 (was: IMPLEMENTED - 2026-09-07
UNCOMMITTED, awaiting gate). Owner felt-test on START NEW is the closing evidence and it is OWED.
**Verified by:** an edit-only doc lane, 2026-09-09. No `.cs` edited, no Unity, no gate, no commit.
Every fact below was read at source or measured this session (CLAUDE.md sec.11B).

---

## 1. The proving evidence, measured 2026-09-09

**HEAD at the time of this write: `eb879496f`** (it moved from `184c8ff06` during the session as
other lanes committed - the ancestry below is unaffected).

| What | Command / read | Result |
|---|---|---|
| The fix commit is on HEAD | `git merge-base --is-ancestor 6a5c7a36d HEAD` | **exit 0** |
| The C/D mechanism file | `git log -1 -- Assets/_Modules/Village/Harvest/OfflineHarvestService.cs` | `6a5c7a36d chore: checkpoint complete workspace and rebuild board`, Wed Sep 9 14:17:30 2026 -0500 |
| The tutorial half | `git log -1 -- Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs` | same sha, same timestamp |
| The A/B half (earlier) | `git log -1 -- Assets/Editor/Regression/NewGamePrefStoreSweepRegression.cs` | `57d3437a2 fix(newgame): WO-1414 - START NEW no longer releases the previous save's welcome-back, and a fresh town's harvest tick starts clean` |

WARNING: `6a5c7a36d` is an **ungated bundle commit** (memory `other-seats-commit-ungated`). Mitigating
evidence, not absolution: `Builds/ready-rca-checkpoint-regression.log` (2026-09-09 14:26:33) reads
`REGRESSION_FAIL: 2 failure(s) (472/474 registered suites green, 0 skipped)`, postdating that commit
by 9 minutes, and **neither** failure is in a WO-1414 file (they are the `[hero-element-cast]` stale
oracle literal and `MANAGE_BUILD_DOOR_FAIL` / WO-2007).

## 2. The mechanism, read at source (file:line)

**C/D - the welcome-back report never covers a live FTUE beat.**
`Assets/_Modules/Village/Harvest/OfflineHarvestService.cs`:

- `:1337-1345` - the header stating the deferral is now SYMMETRIC: *"this block is the half that was
  missing: WO-1414 C only refused to OPEN over a live beat, and nothing ever [took an open one away]"*,
  and the design choice: **DEFERRING, NOT RE-LAYERING** - raising the skip control above the modal
  would leave the DIALOGUE covered, and the dialogue is what the step is waiting on.
- `:1360-1363` - `if (WelcomeBackPopup.IsOpen && TutorialFlow.IsMandatoryChainLive)` then
  `WelcomeBackPopup.DismissIfOpen("mandatory tutorial chain live under the report (WO-1414 D)")`.
- `:1364-1372` - the report is RE-PARKED into the existing `_deferredReveal` / `_tutorialDeferred`
  pair (no new mechanism, no second field) with a `FlowTrace.Warn` naming
  `WelcomeBackUI/ObsidianPanel` sortingOrder 32020 vs the skip control's 6000 - the exact overlap
  the owner's device capture showed.
- `:1374-1379` - the else branch logs a dismissed-but-empty report rather than swallowing it
  (a silent drop would be a sec.12 violation, and this is the only branch where one could happen).
- Related, same file: `:138` `EnsureNewGameSubscription()`; `:1266-1327` the WO-1414 A block -
  a New Game must not inherit a parked away summary; `:1434` / `:1443-1458` the C -> D key widening
  from "awaiting a dialogue" to the whole mandatory chain.

`Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs`:

- `:549-553` - `_modalExcludedSeconds`, *"the slice of `StepClock.Excluded` that was excluded because
  a MODAL owned the screen, kept separately so the STEP-STUCK breakdown can say WHY, not just how
  much. StepClock stays pure and untouched (it is pinned by TutorialWatchdogBoundRegression)."*
- `:341` `IsMandatoryChainLive` (the term `OfflineHarvestService.cs:1360` reads); `:377`
  `LiveChainStateLine`; `:825` the per-step reset; `:2462` the third exclusion; `:2524-2537` never
  rescue a step while a modal the player did not open owns the screen; `:2572-2594` the STEP-STUCK
  breakdown now carries `secondsExcludedModal`.

**A - the away clock.** `OfflineClaimCoordinator.cs:202-233` (the LIVE half on New Game),
`:273-286` (the ANCHOR + its `provenance` word: `fresh` / `zero` / `resume-edge` / `state`),
`:342-349` `TraceWindowProvenance`, `:438` the process flag reset so an oracle starts clean.

**B - the ever-built ledger.** `ResourceBuildingHarvester.cs:111-157` - the LIVE half of the harvest
tick's ledger view, clearing the per-id owed intervals AND the cached gate verdicts so the new
town's first gate evaluation prints, with `:157` naming the exact HELD line the owner's device
logged every 10 s.

## 3. Regression pins - what IS pinned, and what is NOT

WARNING: **This section CORRECTS the dispatch note that said "no regression pin exists".** Two pins were
found on HEAD this session. Stating otherwise would have been hearsay (sec.11B).

**PINNED (A/B - the model state):**
- `Assets/Editor/Regression/OfflineHarvestRegression.cs:322` - case 6 `[new-game-claims-nothing]`,
  THE FRESH-SAVE FIXTURE; assertion text at `:406` *"and an empty ever-built ledger and claims
  nothing (WO-1414)"*. Last touched `f4e4630e3`.
- `Assets/Editor/Regression/NewGamePrefStoreSweepRegression.cs:51` and `:263` - case 7
  `[newgame-away-clock]`: *"A fresh save's away anchor is ZERO and its ever-built [ledger is
  empty]"*. Last touched `57d3437a2`.
- `Assets/_Modules/DevTools/AutoPilotDriver.cs:3559` also records that WO-1414 pins the MODEL.

**PINNED BUT NOT YET REAL (C/D - the deferral MECHANISM):**
`Assets/Editor/Regression/FtueModalDeferralRegression.cs` **does** pin it - its header at `:7` reads
*"THE PIN WO-1090 SHIPPED WITHOUT. The fix landed in `6a5c7a36d`..."*, `:58` names *"the welcome-back
report never owns the screen while the mandatory chain [is live]"*, and it asserts on
`WelcomeBackPopup.IsOpen` / `ActiveResult` (`:85-95`) and `TutorialFlow.IsMandatoryChainLive`
(`:102-108`, `:127`). It carries **WO-1090's** name, not WO-1414's, which is why a `WO-1414` grep
misses it entirely.

WARNING: **But it is not yet evidence.** Read at source 2026-09-09: the file is **UNTRACKED** (`??` in
`git status`), so it is **not on HEAD**; `grep -n "FtueModalDeferral" Assets/Editor/Regression/DataRegression.cs`
returns **nothing**, so it is **not registered** and **has never run in a gate**. It is the PINS
lane's in-flight work. Until the lead registers it and a fresh marker carries it, the C/D behaviour
is proven present in source and proven on HEAD, but **not proven to keep working**. **Flagged, not
certified.**

## 4. Owner felt-test - the closing evidence (device, next build)

Run START NEW from a save that has been away, since the defect only appears when there is a previous
claim stamp to inherit:

1. Play far enough to bank a claim stamp, background the app for a few minutes, relaunch.
2. **START NEW.**
3. **Expect:** no welcome-back report at all on the fresh town (`AwaySeconds` 0, nothing to reveal).
4. **Expect:** the founding dialogue runs to its end - no `STEP-STUCK :: founding_greet` and no
   `SKIP_TOP_HIT_BLOCKED top=ObsidianPanel`.
5. **Expect:** no `'farm' / 'lumbermill' is in the ever-built ledger but NO ResourceCollector`
   HELD lines in the first 60 s.
6. If a report DOES appear during the FTUE, it should vanish and return after the chain finishes -
   that is the WO-1414 D deferral working, not a new bug. The `welcome-back RE-PARKED` warn line
   (`OfflineHarvestService.cs:1367`) is the confirmation.

## 5. Still NOT fixed, and still needing an owner ruling (unchanged)

1. **Default Town founding** marks `collector_lumbermill` / `collector_farm` ever-built while a
   `ResourceCollector` attaches only on PLACEMENT, so the baked twins are ledger ids with no
   collector. A *Default Town* new game can therefore still log HELD lines. This is a founding-path
   ruling, not a reset bug, and it is the one thing that could surprise a fresh-game felt-test.
2. **Evidence D** - the sub-minute welcome-back re-fire needs a window THRESHOLD. Not invented here.

## 6. NOT proven from this lane

- **No gate was run.** The 472/474 verdict quoted above is another run's log, read from disk; it is
  corroboration for the bundle commit, not a gate on this ticket.
- **The C/D pin has never run** (section 3). `FtueModalDeferralRegression.cs` is untracked and
  unregistered; the mechanism is proven present in source and proven on HEAD, not proven to keep
  working.
- **No device capture exists for this fix.** Section 4 is the procedure, not a result.
- The soundness of the rest of `6a5c7a36d` (it is a whole-workspace checkpoint) is outside this
  ticket's scope and was not reviewed here.
