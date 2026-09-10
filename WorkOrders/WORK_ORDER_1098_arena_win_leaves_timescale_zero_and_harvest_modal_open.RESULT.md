# WO-1098 RESULT - arena win, timeScale 0.00 + open Harvest Result: IMPLEMENTED on HEAD

**Verified:** 2026-09-09 (SILO 0B verify-and-flip pass, read-only; no `.cs` touched)
**Landed in:** `f4e4630e3` ("fix live tester regressions and recurring dragon", 2026-09-09 14:04:37)
**Ancestry proof:** `git merge-base --is-ancestor f4e4630e3 HEAD` -> exit 0 (HEAD = `184c8ff06`, `dev`)
**Compile proof:** `Builds/compile-gate-recurring-dragon-spells.log` (2026-09-09 13:49) ->
`COMPILE_GATE_OK :: scripts compiled clean`.
**Capture-predates-fix proof:** the source capture is
`logs/f8-inbox/capture-20260909-130154-seq4973.md`, written **2026-09-09 13:01**. The fix commit is
**14:04:37** the same day - 1 h 03 m later. The capture cannot have exercised this code.

## ACCEPTANCE ITEM 1 - THE `timeScale` LEAK SITE, NAMED AT file:line

**The 0.00 is NOT a leak, and it is NOT WO-1297's 0.04 hit-stop path. It is the modal's own hold,
working as designed.**

- `Assets/_Modules/Core/UI/HarvestOverflowModal.cs:112`
  `_hold = WorldHold.AcquirePlayerOwned("harvest-overflow-result", ...)`
- `Assets/_Modules/Core/UI/WorldHold.cs:469-472`
  `public static Handle AcquirePlayerOwned(string reason, Func<bool> isOwnerAlive)` ->
  `AcquireKind(reason, 0f, 0f, HoldKind.PlayerOwned, ...)`. **Scale 0.** (`AcquirePlayerOwnedScale`
  exists precisely for a player-owned hold at any OTHER scale - `:480`.)
- `WorldHold.cs:24` states in-code that **WorldHold is the only code in the project that writes
  `Time.timeScale` for a pause**, and `:59` records the audit of every `Time.timeScale =` assignment
  under `Assets/_Modules/` that established it.

So the two reported invariants are **ONE state, not two leaks**: the same visible
`HarvestOverflowModal` registers as `"Harvest Result"` (`HarvestOverflowModal.cs:118`
`PanelManager.Register("Harvest Result", Close, ...)`) - the exact holder string the capture names -
and owns the `timeScale = 0`. Close the modal and both invariants restore together.
**Consequence: WO-1297 is neither confirmed nor implicated by this capture, and this evidence must
not be used to close it** (the WO's own instruction).

## ACCEPTANCE ITEM 2 - WHO OPENED THE MODAL, AND THE FIX

`f4e4630e3` makes passive collection incapable of presenting it, honouring the rule that was already
written at `AutoHarvestService.cs:55`:

| Change | Proof |
|---|---|
| The passive tick opts out explicitly | `AutoHarvestService.cs` -> `ResourceCollectorService.CollectAll(showResult: false)` |
| The flag is threaded, not faked | `ResourceCollectorService.CollectAll(bool showResult = true)`; `HarvestOverflowModal.BeginBatch(showResult ? "CollectAll" : null)`; `if (showResult && rows.Count > 0) HarvestOverflowModal.Present(rows)` |
| The Echo silo dump follows the same flag | `EchoService.DumpSilos(bool showOverflowResult = true)`; the `BankOverflowToastPresenter.BeginWarnScope` is replaced by `default(WarnScope)` when suppressed |
| The away summary no longer opens on trivial absences | `OfflineHarvestService.cs:93` `MinAwaySummarySeconds = 30.0 * 60.0` + `ShouldRevealAwaySummary(result)`, with the gate trace naming `away=...s minimum=...s` |

A manual Collect still shows the screen - the first actionable warning belongs to the player's own tap.

## WHAT THE OWNER FELT-TESTS

Win an arena fight and return to town. Expect: the world clock runs, the interact button works, Back
does not aim at an invisible panel, and no `BATTLE_QUIESCENCE_FAIL (arena win)` in the capture.

## WHAT IS **NOT** PROVEN

- **Acceptance item 3 is not met:** no arena win has been run since the change, so
  `timeScale == 1.0` + no open panel is not proven by a run, and the quiescence gate has not reported
  zero failed invariants.
- **Acceptance item 4 is not met:** the design question in the WO (whether a harvest modal should be
  reachable around an arena battle at all) was **never put to the owner and no answer is recorded.**
  It is still open.
- The passive-vs-player attribution above is inferred from the `f4e4630e3` diff and the rule at
  `AutoHarvestService.cs:55`. **The seq 4973 capture itself was not re-read to prove which caller
  opened that particular modal** - only that the capture predates the fix.
- Neither invariant has been reproduced on **device**; seq 4973 is Editor-side.
- The WO's "was there a swallowed throw at t=73" check was not performed. With the one-state finding
  above it is no longer needed to explain two failures, but it was not ruled out either.
- No regression pin was added for the passive-suppression path; no fresh `REGRESSION_OK` marker exists
  for this change.
