# WO-1579 - A purchase world hold outlived its 180s ceiling by over two hours while the wallet round trip ran

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:43, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT
**Priority:** P1
**Silo:** Wallet / Commerce (PackStore, SolanaWalletProvider, WorldHold) - **Source:** owner
Seeker session 2026-09-07, build 2026.09.07.359076, F8 seq 4690-4693

## Measured facts (nothing below is inferred)
- seq 4690, 12:49:07.402Z, Main_Castle_Overworld, kind=error, stack
  `DeNelle.Core.UI.WorldHold:WatchdogTick(Single)`: `STUCK WORLD HOLD: 'purchase'
  (scale 0.00) has been outstanding for 7869.3s, past its 180.0s ceiling ... Force-releasing`.
  Unity `t=8110.16`, so acquired at t~241 (roughly 10:38 UTC). World at timeScale 0.00.
- seq 4691, 12:49:26.514Z: `SendPayment failed: authorization request failed`, stack
  `SignTransaction d__20 -> SendPayment d__21`.
- seq 4692/4693, same second: `Pay 'starters-hand' (Skr, 258) FAILED at provider` /
  `Purchase FAILED`. The freeze was released 19s BEFORE the sign round trip returned.

## Root, at source
- `Assets/_Modules/Wallet/PackStore.cs:3401` takes the hold first thing in `Purchase`:
  `using var worldHold = WorldHold.Acquire(WorldHold.ReasonPurchase)`; ceiling
  `WorldHold.StuckHoldSeconds = 180f` (`Assets/_Modules/Core/UI/WorldHold.cs:211`).
- `Assets/_Modules/Wallet/TargetedLocalAssociationScenario.cs:367` `SignTransaction` awaits
  `client.Authorize`/`client.Reauthorize` (:383-391) and `client.SignTransactions` (:393)
  with NO timeout. The association handshake around them DOES have one (`_clientTimeout`,
  9s, :192/:526-532); the sign leg does not. The round trip is unbounded and the world
  stays frozen for its whole duration.
- The watchdog is NOT "log only": `WorldHold.cs:884-895` force-releases (`RemoveAt(i)`). The
  defect is the ceiling went UNENFORCED for 7869s - `WatchdogTick` is driven by
  `WorldHoldWatchdog.Update` (`Assets/_Modules/Core/UI/WorldHoldWatchdog.cs:48`), dead while
  Unity is backgrounded. Had Update run in foreground for even 180s of those 7869s it would
  have fired then, so Unity WAS paused ~the whole span and the WO-1260 suspend credit
  (`NotifyApplicationPause(bool,float)`, `WorldHold.cs:1013-1031`) WAS reached on resume.
- UNPROVEN, and the CLI's first step: WHY the credit did not reduce the age. Its Step line
  "WorldHold watchdog excluded" is absent from the pulled break-log, but that file carries
  only error/flag captures and today's logcat ring is long since evicted. REPRODUCE with
  `adb logcat` streaming: open store, tap Buy, let the sheet show, background 4+ min,
  return, and read the order of "WorldHold watchdog excluded" vs "STUCK WORLD HOLD" on the
  resume frame. Do not fix before this read (CLAUDE.md s12).

## Fix shape
1. Bound the authorization/sign leg the way `StartAssociation` already bounds association:
   `Task.WhenAny(work, Task.Delay(ceiling))` - a thread-pool timer keeps running while the
   Activity is paused, so it cancels on the first foreground frame. On timeout nothing was
   submitted, so NO charge: return `PaymentResult.Failure`, never `Indeterminate`.
2. Tell the player in words through the existing seam `PackStore.SetStatus`
   (`PackStore.cs:4283-4288`, writes `_statusBanner`). Silent failure is the current bug.
3. Design question to ANSWER, not assume: should a transaction hold exist at all while the
   wallet app owns the screen? The suspend credit is right for a player-owned pause, but a
   signing request that sat two hours of WALL clock has expired whether or not Unity was
   awake. Options: shorten the ceiling, or drop the hold on `OnApplicationPause(true)` and
   re-take it on resume. (Double-dispose is already safe: :338 + :682. Say so in RESULT.)

## Pin + do NOT touch
Add a case to `Assets/Editor/Regression/TransactionWorldHoldRegression.cs` on the
explicit-clock seams `WatchdogTick(float)` (:803) / `NotifyApplicationPause(bool,float)`
(:1013): acquire a `purchase` hold, advance past the ceiling, assert release +
`Time.timeScale` 1.00 + a player-facing status line. Leave alone: the player-owned watchdog
branch (`WorldHold.cs:837-882`, WO-1360/WO-1369 - a live pause menu is never
force-released), the Pi/GooglePlay `OwnsTheRail` early exit, and the `using` at :3401.
