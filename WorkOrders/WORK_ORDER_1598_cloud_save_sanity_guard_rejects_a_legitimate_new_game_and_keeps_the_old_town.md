# WORK ORDER 1598 - The cloud save sanity guard rejects a legitimate NEW GAME as "implausible_drop" / "rollback", so the cloud row keeps the old town (and would hand it back on load)

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:07:31, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the Command Center event table
**Silo / Lane:** api/game/save.js (the sanity guard + audit), api/game/load.js, test/; client half in `Assets/_Modules/Core/State/GameStateService.cs` (the save body) + the New Game reset path (`ResetToNewGame`)
**Type:** EXISTING system, DEFECT (economy integrity + progress loss on load)
**Priority:** P1 - the owner started a new game this morning as ruled; her cloud row still says 901 crystals

## Evidence (analytics_events, read 2026-09-07 ~11:45 local, read-only)

`save_sanity_reject`: 177 rows in 14 days, 7 ids. Every reject is one of two shapes:

| rule:field | count |
|---|---|
| rollback:bestWave | 156 |
| implausible_drop:wood | 139 |
| implausible_drop:iron | 103 |
| implausible_drop:crystals | 93 |
| implausible_drop:coins | 89 |
| implausible_drop:food | 85 |

Owner's wallet row (`CHKKFkPGz8...`, mode=wallet), eleven times from 00:39Z to 10:26Z today:
`implausible_drop crystals 901 -> 36` (and 901 -> 14, 901 -> 13). The guest id (`guest-local...`,
166 rows) shows the same shape on a fresh town: `wood 15 -> 0`, `iron 5 -> 0`, `bestWave` rollback.

Read: the owner created a NEW GAME (her plan, 2026-09-07 morning). A new town has 13-36 crystals and
wave 0. The server's prior row carries the OLD town (901 crystals, a high bestWave). The guard
(`api/game/save.js` GUARDED_BALANCES / NESTED_BALANCES / the bestWave rollback rule) reads each new
save as an implausible drop and REJECTS those fields, so the cloud copy keeps the old balances. On the
next cloud load (`ApplyBackendState`) the old town's 901 crystals come back over the new town - a
silent duplication of everything she spent or reset, and the new game never truly exists in the cloud.

## What to do

- **api:** a save may declare a reset. The client sends `resetEpoch` (or `newGameId`, a monotonic
  integer / uuid persisted with the local save) in the body; the row stores it. When the incoming
  epoch is NEWER than the stored one, the sanity guard is bypassed ONCE for that write (drops and
  rollbacks are the point of a reset), the audit row records `save_reset_accepted {from, to, ref}`, and
  the stored epoch advances. When the epochs match, the guard applies as today. When the incoming
  epoch is OLDER, refuse `SAVE_RESET_STALE` (an old device replaying). Never bypass on a bare flag; the
  epoch must be monotonic. load.js returns the epoch so a client can tell it is behind.
- **client:** `ResetToNewGame` bumps and persists the epoch; the save body carries it (beside
  `schemaVersion`, WO-1587); `ApplyBackendState` refuses to apply a backend row whose epoch is OLDER
  than the local one (and says so in a `[Flow:Sync]` line), so a stale cloud row can never overwrite a
  newer local new game. FlowTrace at both seams.
- **tests:** api - epoch newer bypasses guard once + audits; equal applies guard; older refused;
  missing epoch (old clients) behaves exactly as today. client - regression that the body carries the
  epoch, that reset bumps it, that a stale backend row is refused.

## Not to touch
- The sanity rules themselves (thresholds), the wallet/session rail, promo.

## Acceptance
- Owner's next new game: the cloud row shows the new balances and `save_reset_accepted` once; no
  `save_sanity_reject` for that player afterwards; a load returns the new town.
- npm test green; REGRESSION_OK n/n on a fresh log.
