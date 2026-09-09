# WORK ORDER 1587 - The offline save queue fails to drain six times in a row while the session renews fine, and the "why=" line the warning points at never prints

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:47, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT (instrument first) - minted 2026-09-07 (CLI) from the owner's device log
**Silo / Lane:** Core/State sync - `Assets/_Modules/Core/State/GameStateService.cs` (the offline queue drain), `Assets/_Modules/Wallet/BackendRequestSigner.cs` (identity for the save call), the `[Flow:Sync]` / `[Flow:Wallet]` traces
**Type:** EXISTING system, DEFECT (cloud save behind; local save safe)
**Priority:** P1 - the store update's headline is "sign in once and stay signed in"; a cloud copy that silently falls behind is the next thing the owner will hit

## Evidence (Seeker, build 2026.09.07.359076, `adb logcat -d -s Unity` read 2026-09-07 09:2x)

```
08:01:26.418 [Flow:Wallet] MintSessionAsync held why=explicit-connect scene=Title caller=explicit-connect
08:01:37.094 [Flow:Sync] offline queue drain FAILED - 1 marker(s) re-queued and RETAINED (never dropped). ... Check the [Flow:Wallet] why= line for the identity reason.
08:11:58.139 [Flow:Sync] offline queue drain FAILED - 2 marker(s) ...
08:15:31.460 [Flow:Wallet] RenewSessionAsync (no signature required) scene=Main_Castle_Overworld caller=Nft.LoadTexture /api/game/save
08:15:31.674 [Flow:Wallet] RenewSessionAsync held - session extended with NO wallet prompt. ...
08:15:31.895 [Flow:Sync] offline queue drain FAILED - 3 marker(s) ...
08:18:35.714 ... 4 marker(s)   08:20:10.533 ... 5 marker(s)   08:23:29.718 ... 6 marker(s)
08:29:39.111 [Flow:Wallet] RenewSessionAsync (no signature required) ... /api/game/save
```

Read off the log:
1. The session mints at connect (08:01:26) and RENEWS without a prompt (08:15:31, 08:29:39) - the WO-1441 /
   renewal-cap rail works on this device against production (Vercel production deployment
   `dpl_ALgUwPLzzhh2bF4EnN36uQ9vobtr`, sha `77e8e8941`, READY).
2. Yet every drain attempt FAILS, six in a row, the retained count climbing 1 -> 6, INCLUDING the one 200 ms
   after a successful renewal (08:15:31.895). So the failure is not "no session".
3. The warning tells the reader to "Check the [Flow:Wallet] why= line for the identity reason" and NO such
   line follows any of the six failures. The only `why=` on the whole log is the mint at 08:01. The
   instrumentation promises a reason it never prints - that is the first defect to fix (CLAUDE.md s12: no
   silent failures).
4. Local saves keep landing (`[Flow:Save] wrote signed save via LocalSaveProvider` at 08:29:23/24/29), so
   the player is safe; the CLOUD copy is behind for the whole session.

## What to do

- **Instrument first.** At the drain's failure branch log the ACTUAL cause with `FlowTrace.Warn("Sync", ...)`:
  the HTTP status and body head of the failed save call, or the exception type, or the identity state
  (session present? expiry? wallet bound?). Make the "why=" promise true in the same place the drain fails,
  not in a different system. Keep the retained-count line.
- Reproduce headless if possible (a queued marker + a mocked 4xx/5xx from /api/game/save), else read the
  next device log after the instrumented build; the owner plays every day.
- Then fix THAT cause. Candidates only (not conclusions): the drain uses a stale/expired session token the
  renewal did not write back; /api/game/save rejects the queued marker's payload (schema version, size);
  the drain runs before the identity is bound on this scene. The trace decides.
- Pin: a regression that a drain failure ALWAYS emits a reason line naming the cause category
  (`SyncDrainReasonRegression` or extend the existing sync suite), plus a case for the fixed cause.

## Not to touch
- The wallet mint/renew rail itself (`WalletSkinBootstrap`, the renewal cap in api/) - proven working here.
- `PackStore`, `SolanaWalletProvider`, `WorldHold` (WO-1579 lane owns them right now).

## Acceptance
- Device log after the fix: the drain succeeds (`offline queue drained N`) after renewal, retained count
  returns to 0; if it fails, a `why=` line follows within the same second naming the cause.
- Regression green, REGRESSION_OK n/n on a fresh log.
