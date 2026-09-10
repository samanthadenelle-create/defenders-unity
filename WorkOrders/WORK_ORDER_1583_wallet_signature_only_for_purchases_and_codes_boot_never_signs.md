# WO-1583: wallet signature only for purchases and codes; boot never signs

**Status:** IMPLEMENTED - 55d3a7c56 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes.
**Silo:** `DeNelle.Core.Web3` (`BackendRequestSigner`) + `DeNelle.Wallet` (`WalletSkinBootstrap`) +
`DeNelle.Core.State` (`GameStateService` save-refusal log level only). Disjoint from Manage/**, Raid*,
BuildMode and `api/` - all untouched.
**Source:** owner ruling 2026-09-07 08:5x, verbatim: ***"everytime i play now im forced to authenticate
... I would think the authentication would only be needed for purchases (and codes)"***.
**Supersedes:** WO-1441's mint-at-connect, for the BOOT path only. WO-1441 Status FIXED stands; a dated
SUPERSEDED block was added to it in this same change.

---

## 1. What was wrong

WO-1441 (committed `32659c0f6`, device-proven 2026-09-07 00:41) fixed a real outage - no wallet holder
had a backend session, so every cloud save was refused - by making **both** connect paths call
`MintSessionForExplicitConnectAsync`. Minting POSTs `/api/auth/session` with a wallet signature, so it
raises an MWA `SignMessage` sheet. One of those paths is `TryAutoResumeAsync`, the **silent boot
reconnect**, which runs with no player action on every launch. WO-1441's own §5 evidence is the proof:
`MintSessionAsync held why=explicit-connect scene=Title caller=explicit-connect` at 00:41:34.254.

The fix bought cloud save at the price of an unasked-for wallet sheet every time the owner opened the
game. That is the trade she reversed.

## 2. The ruling, as implemented

1. **Boot / auto-resume never signs.** The MWA reauthorize restores IDENTITY with no signature. The
   backend session is reused if held, RENEWED over the wire if expired (no signature needed), and
   otherwise simply absent - the game runs, and cloud saves fall back to the existing offline queue.
2. **A signature is requested only on:** a purchase (`IsPurchaseRoute`, `/api/purchases/*`), a promo
   code redeem (`PromoCodeService.cs:168`, `allowInteractiveSessionMint: true`), and an **explicit
   Connect tap** - the SKR corner button and the login-surface Connect Wallet button. The player asked
   for those; boot did not.
3. **The queue drains after.** Already wired: `SyncToBackend` calls `FlushOfflineQueue` first, and the
   `offline queue DRAINED` trace stays.

## 3. Files changed

| File | Change |
|---|---|
| `Assets/_Modules/Core/Web3/BackendRequestSigner.cs` | new `TryResumeSessionWithoutSigningAsync` - reuse or renew, never mint; three `boot never signs (ruling 2026-09-07)` traces. `TryAttachSession`'s no-mint Warn re-worded: `why=missing` is now expected, not an outage. |
| `Assets/_Modules/Wallet/WalletSkinBootstrap.cs` | `ConnectForLoginAsync(bool explicitConnect)`. `LoginWalletBridge.ConnectHandler` passes `true`; `TryAutoResumeAsync` passes `false`. The 09-06 ruling comment is kept as history under a SUPERSEDED banner. |
| `Assets/_Modules/Core/State/GameStateService.cs` | the fail-closed save refusal moves from `Debug.LogError` to `ReportSaveAuthAborted` - a latched `FlowTrace.Warn` saying `session absent, save queued`. Latch re-arms on a successful drain. |
| `Assets/Editor/Regression/BackendSaveAuthRegression.cs` | re-pointed with the ruling (§4). |
| `Assets/Editor/Regression/WalletConnectFailureAttributionRegression.cs` | re-pointed with the ruling (§4). |
| `Assets/Tests/EditMode/LoginSurfacePlatformTests.cs` | `:154` pinned the bare `ConnectHandler = ConnectForLoginAsync` literal, which the lambda breaks. Re-pointed to pin the registration AND `explicitConnect: true`. |

## 4. Suites re-pointed WITH the ruling

Both oracles previously pinned only "a mint must exist". They now pin the tension: **a mint must exist
AND must not be reachable from boot.**

- `BackendSaveAuthRegression`: `TryAutoResumeAsync` must REJECT `MintSessionForExplicitConnectAsync`
  and REQUIRE `explicitConnect: false`; `ConnectForLoginAsync` must REQUIRE
  `TryResumeSessionWithoutSigningAsync` and have `if (explicitConnect)` positioned BEFORE the mint;
  `TryResumeSessionWithoutSigningAsync` must REJECT `MintSessionAsync` and REQUIRE
  `TryRenewSessionAsync` plus the boot trace literal; `ReportSaveAuthAborted` must REJECT
  `Debug.LogError` and REQUIRE the latch. The existing "wired on BOTH connect paths" count stays at 2
  and its message now says **EXPLICIT** paths.
- `WalletConnectFailureAttributionRegression`: keeps the WO-1441 mint pin, adds `explicitConnect`,
  `TryResumeSessionWithoutSigningAsync` and the boot trace literal.

## 5. The cost, stated plainly (do not let this go silent)

⚠ **The task brief said "the session is restored from the persisted token". THERE IS NO PERSISTED
TOKEN.** `BackendRequestSigner.cs:58-68` states the session is held in three in-memory statics
**deliberately** - it is a bearer credential and PlayerPrefs on Android is readable by a backup. So on a
**cold boot** `SessionUsable` is false, `SessionGapWhy` is `missing`, and there is nothing for
`TryRenewSessionAsync` to present. Under this ruling a wallet holder therefore has **no cloud save until
they buy, redeem a code, or tap Connect**; progress is safe locally and queues offline, draining in one
upload when a session appears. That is WO-1441 §4.3's "missing lifecycle" reinstated on purpose, with
the queue as the net.

Getting BOTH no boot sheet AND cloud save at boot requires a **sealed persisted session token** (the
`MwaSessionStore` AES-GCM shape). That is a separate owner ruling and was deliberately not smuggled in
here. Note also that `TryRenewSessionAsync`'s server half is still HELD behind the `signed_at`
migration (WO-1446), so even a warm renewal is not yet proven end to end.

## 6. Acceptance

- [ ] `COMPILE_GATE_OK` on a fresh log.
- [ ] `REGRESSION_OK n/n` with both re-pointed suites green.
- [ ] Owner opens the game on a device build: **no wallet signature sheet at boot**, and the device log
      carries the boot trace line named in the RESULT.
- [ ] A promo code redeem or a purchase still raises exactly one sheet and mints.
- [ ] `offline queue DRAINED` appears after that mint.
- [ ] Owner felt-verifies and closes (PO closes, not CLI - CLAUDE.md §13).

## 7. What NOT to touch

Manage/**, Raid*, BuildMode, `api/`. Do not add session persistence. Do not make the save path fail
open (WO-1441 §5 stands). Do not remove FlowTrace.
