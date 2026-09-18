# WORK ORDER 1778 — A 3-star capture **strands the player on the victory screen**: the retry that was supposed to save it has zero callers

**Status:** DONE - committed b76e1d28d, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW — 2026-09-17: code written, `python tools/gate_brace.py` exit 0 and NUL-clean on all 8 touched files. ⛔ NOT gated: the lane was told to HOLD before any Unity process (multiple lanes open, one seat fires Unity). `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` (acceptance 3) and the device capture (acceptance 2) are still owed.

**Implementation (2026-09-17 lane):**
1. `RaidVictoryController.CanEnterCapturedTown` is now BOUNDED (`CaptureRefusalsBeforeForcedExit = 2`): refusal 1 keeps the `ownedTown.captureRetry` toast and holds the screen, the refusal at the bound parks the receipt, clears `_captureRequired` and returns **true**, so `ReturnHome` routes `GoCastle`. `FlowTrace.Fail` names the forced route. The dead `RetryCaptureAfterDismissal` + `_waitingForCapture` are **deleted** (fix option 1, the "delete it and force the exit" branch), and the false "route home is guaranteed from three independent places" doc paragraph is corrected in place (§15).
2. `RaidCaptureCensus.TryParkForLaterClaim` (new) parks the capture as a **pending** receipt so the forced exit DEFERS the town — `GameStateService` recovers a pending capture on load.
3. `EndStateView.FirePrimary(bool forceDismissIfGateRefuses)`: every gate refusal is traced, and the anti-softlock guard now re-arms its window and **forces the dismissal** on its last pass (`GateRefusalWindowsBeforeForcedDismiss = 3`), via the extracted `AwaitDismissWindow`.
4. `OwnedTownPanel.Show` builds the castle route **before** the busy early-out, with a Warn.
5. `SceneRouter.GoOwnedTown` toasts on refusal instead of only warning.
6. FlowTrace added to `RaidCaptureCensus` (census built / commit refusals / park) and to `OwnedBaseProgression.Fail`, its single refusal funnel — both were at zero.
7. New proof `Assets/Editor/Regression/CaptureStrandExitRegression.cs` (marker `CAPTURE_STRAND_EXIT_OK`), wired into `DataRegression.RunAll`. Case A is a **behavioural** probe: a real `RaidVictoryController` with `_captureCensus = null`, `_captureRequired = true`, `_victoryStars = 3`, asked twice — refuses once, then forces the castle route with `_captureRequired` cleared. That second call returned false pre-fix, so the case is red-first.
8. **Oracle correction after the lead's first combined-tree gate (568/573).** Case C failed with *"RetryCaptureAfterDismissal / `_waitingForCapture` is back"* — a **FALSE FAIL in the new suite, not a regression in the fix**. `grep` over `RaidVictoryController.cs` returns **exactly one** hit, line 116, inside the `///` RCA paragraph that records *why* both were deleted; there is no such code. Case C had been matching RAW text, so it matched its own prose — and the tempting "fix" (deleting the explanation) would have destroyed the canon record instead of dormant code. `ReadSource` now returns **comment-stripped, string-literal-preserving** source (the precedent `RaidWatchdogHonorRegression` sets verbatim: *"comment-stripped source in, so a quoted signature inside prose cannot be matched"*), the stripper is literal-aware so a `//` inside a URL cannot swallow a line (CLAUDE.md §1's trap), and the Case C message now says the match is comment-stripped so the next seat does not re-chase the prose. Re-simulated against the real files: C absent from code; B/D/E1/E2/F all still resolve (census 14 / progression 1 FlowTrace calls, busy@2959 < castle@3059, refusal→toast gap 175).

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** raid exit + owned-town entry — `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs`, `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs`, `Assets/_Modules/Core/SceneRouter.cs`. **No `.unity`, no bake, no AI, no camera.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`. WO-1767 (`bca258130`) and WO-1768 (`0da7af499`) landed 2026-09-16 15:33 and are **NOT** in it.

**Video path:** the marquee beat — act 2 shot 12 of `docs/HACKATHON_CLOCK_IN_SUBMISSION_PLAN_2026-09-16.md`. ⛔ **If this fires on camera the video has no ending.** P0.

---

## 1. SYMPTOM

The player 3-stars Iron Bastion. The victory screen says the base is hers. **Every route off that screen is refused, and the screen cannot be dismissed.** The only feedback is a toast telling her to try again — at a button that will refuse again, forever.

## 2. EVIDENCE

**The precondition fired in the owner's own 2026-09-16 run.** From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped):

```
[Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable identity: Wall_Outer_SS_3
```

She settled at 2 stars (`veterancy: 2 star(s) - no ranks granted (3 stars required).`), so capture was never attempted and the strand was never reached. **A 3-star clear on that same build would have hit it.**

**The strand, traced through source (all read 2026-09-16):**

1. `RaidVictoryController.cs:163-164` swallows the census throw, leaving `_captureCensus == null`.
2. `:1070-1071` then refuses capture forever: `"Final victory cannot capture: precombat census is missing."`
3. `CanEnterCapturedTown` `:1048-1053` toasts `ownedTown.captureRetry` (`Assets/Resources/Data/Canonical/en.json:16` — *"Your town could not be saved. Please try entering again."*) and returns **false**.
4. `ReturnHome` `:1037` returns early on that false.
5. `EndStateView.FirePrimary:2698` returns **before** `Destroy(gameObject)` when the gate refuses, so the auto-dismiss guard (`EndStateView.cs:2769-2777`) fires once and cannot clear the screen.

⛔ **The retry the source promises does not exist.** `RaidVictoryController.RetryCaptureAfterDismissal` (`:1055-1061`) has **zero callers**, and `_waitingForCapture` is never set true (declared `:115`, set false `:1059`, read `:261`). The doc comment at `:104-109` asserting *"a route home is guaranteed from three independent places"* plus a caller watchdog is **false for the capture branch**: `grep VictoryOwnsTheReturn` finds only `HeroHealth.cs:1485` (the death path).

**Two sibling strands on the same lane, same root shape (no guaranteed exit):**

- `OwnedTownPanel.Show:59` — `if (OwnedTownDesignService.IsBusy || OwnedTownConstructionService.IsBusy) return;` bails **before any button is built**, and the panel is built `withClose: false` (`:42`). While a move/build save is in flight the player sees `en.json:14` and has **no "Return to castle" and no X**.
- `SceneRouter.GoOwnedTown:607-608` **refuses silently** when `OwnedBaseProgression.Validate` fails — a `FlowTrace.Warn` only, no toast — so the practice panel's single button (`OwnedTownPracticeController.ReturnToTown:145`) can do nothing with no feedback at all.

## 3. WHAT THIS IS NOT

**WO-1767 (`bca258130`) fixes the CENSUS**, so the precondition should stop firing. ⛔ **That is not this ticket.** This is the unconditional promise: *whatever* refuses, the player always has one route off the victory screen. A refusal that can only be retried through a button that refuses is not a route.

## 4. THE FIX

1. **Wire the dead retry, or delete it and force the exit.** Either `RetryCaptureAfterDismissal` gets its caller and `_waitingForCapture` gets set, or the capture gate falls back to `ReturnHome` → `GoCastle` after a bounded number of refusals, with the capture receipt kept for a later claim. Trace whichever path is taken.
2. `EndStateView.FirePrimary:2698` — a refused gate must still allow the screen to be dismissed after the guard window; never leave a screen whose only CTA is a no-op.
3. `OwnedTownPanel.Show:59` — build the close/return affordance **before** the busy early-out, or build the panel `withClose: true`.
4. `SceneRouter.GoOwnedTown:607-608` — a refusal the player triggered must surface a toast, never only a `Warn`.
5. Add `FlowTrace` to `OwnedBaseProgression.cs` and `RaidCaptureCensus.cs` — both carry **zero** calls today (`grep -c FlowTrace` = 0 on each), so the contract's own refusal reasons are invisible except where the caller happens to echo them.

## 5. ACCEPTANCE

- A headless/editor proof that forces `_captureCensus == null` on a 3-star final victory and asserts the player reaches `Main_Castle_Overworld` or the owned town — never neither.
- A device capture of a real 3-star Bastion clear showing `OWNED_TOWN_CAPTURED:` (`RaidVictoryController.cs:1075`) **or**, on a refusal, a trace line naming the forced route home.
- `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 6. DO NOT TOUCH

WO-1767's census identity code, WO-1768's death-latch (`RaidVictoryController.cs:974`), the tutorial (WO-1788 lane), the victory screen's copy (WO-1783 lane).
