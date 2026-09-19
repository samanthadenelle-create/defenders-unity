# WO-1875 RESULT — Circle screen dead without a wallet session — CLAIM

**Status:** IMPLEMENTED
**Seat:** CLI lead gated 2026-09-19

Lead verified. `COMPILE_GATE_OK` on `Builds/c1875d.log` (fresh, `-ExpectMarker`). `REGRESSION_OK 585/585 suites` on `Builds/r1875c.log` (fresh, `-ExpectMarker`). `[circle-screen] CIRCLE_SCREEN_OK` 18/18 including re-pointed `[circle-interactive-mint]`. Device felt-verify remains the owner's close.

## What shipped

Opening Circle stays a non-minting read. If attach fails `why=missing` or `why=expired`, the VM enters **NotSignedIn = 6** (appended after Unreachable=5). ONE GOLD face **SIGN IN** (`circle.signIn.face`) is the only minting Circle read (`SendGet("clan/me (sign-in mint)", ..., true, ...)`). Refresh / polling stay false. Writes stay true. Boot never signs. HeartboundStatusClient untouched.

No-wallet / guest stays **NoWallet** with `circle.error.noWallet`. Missing-session is never collapsed into NoWallet or Unreachable.

## How missing vs unreachable is distinguished

1. `BackendRequestSigner.HasLiveWalletSession()` → `SessionUsable(CurrentPlayerId())`. Query only; does not mint.
2. `BackendRequestSigner.SessionGapWhyPublic()` + `LastSessionGapWhy` (set when `TryAttachSession` returns false; cleared on attach).
3. `CircleSource.LastAttachWhy` set when `AttachAndSend` gets `!safeToSend` (from `LastSessionGapWhy`, else `SessionGapWhyPublic()`). Cleared on a successful attach. Request is still never sent. Callback is status 0 + empty body.
4. VM: `status == 0` AND `LastAttachWhy` is `missing`/`expired` → `State=NotSignedIn`, `ErrorKey=circle.error.notSignedIn`. Any other status 0 → `Unreachable` + `circle.error.unreachable`. `PlayerFacingKey(code, status, attachWhy)` refuses to map status 0 + missing to unreachable.

## SignIn mint site

`CircleSource.SignIn` → `SendGet("clan/me (sign-in mint)", MeUrl+"?playerId="+..., true, done)`.
That is the only Circle `SendGet` that passes true. `[circle-interactive-mint]` now requires exactly one such read and fails on zero / two+ / a non-SignIn true GET.

VM `SignIn()`: `FlowTrace` `NotSignedIn -> minting` → Guard → source.SignIn → `-> ok` then `_selfNameAsked=false` + `RefreshAll`, or `-> fail` stay NotSignedIn. `IsBusy` during mint.

Clan chat: `NoRoom` + wallet present + `!HasLiveWalletSession()` resolves `clanChat.notSignedIn`. Does not mint.

## Brace / NUL

`python tools/gate_brace.py` on 8 edited `.cs` files: **`GATE_BRACE_SUMMARY bad=0 of 8`**. NUL scan: **0 of 8**.

## Tests run

**Not run.** EditMode (`CircleScreenVMTests` F/G/H, `CircleErrorMapTests` attachWhy) and DataRegression / `CircleScreenRegression` need the Unity batch the lead owns. Case count stays **18/18** (re-pointed mint case, no new case method).

## Files changed

- `Assets/_Modules/Core/Backend/BackendRequestSigner.cs` — `HasLiveWalletSession`, `SessionGapWhyPublic`, `LastSessionGapWhy`
- `Assets/_Modules/HUD/Circle/CircleSource.cs` — `LastAttachWhy`, `SignIn` minting GET, attach-why on `!safeToSend`
- `Assets/_Modules/HUD/Circle/CircleScreenVM.cs` — `NotSignedIn=6`, `CanSignIn`, `SignInFaceKey`, `SignIn()`, attach-why mapping
- `Assets/_Modules/HUD/Circle/CircleScreenPanel.cs` — GOLD SIGN IN + Refresh on NotSignedIn; `ChatNotSignedInKey`
- `Assets/_Modules/HUD/ClanChatPanel.cs` — `clanChat.notSignedIn` copy; no mint
- `Assets/Tests/EditMode/CircleScreenVMTests.cs` — FakeSource LastAttachWhy+SignIn; F/G/H
- `Assets/Tests/EditMode/CircleErrorMapTests.cs` — status 0 + missing/expired/other
- `Assets/Editor/Regression/CircleScreenRegression.cs` — FrozenKeys + `[circle-interactive-mint]` re-point
- `Assets/Resources/Data/Canonical/{en,es,pt-BR,de,fr,ru,ar,ja,ko,zh-Hans}.json`
- `Assets/StreamingAssets/Data/Canonical/` same 10, byte-identical (`tools/merge_wo1875_signin_keys.py`)
- `Logs/debug/scratch/sweep-sidecar-wo1875.json`
- `WorkOrders/WORK_ORDER_1875_circle_screen_dead_without_wallet_session.md` — Status flipped

BOARD.html not regenerated (lead does). No commit. No APK. No adb.
