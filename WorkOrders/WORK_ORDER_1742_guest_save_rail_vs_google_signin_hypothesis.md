# WO-1742 — The save rail vs. the Google sign-in failure: which one is losing player progress?

**Status:** BLOCKED — RCA complete and proven; Lane A needs an owner ruling on conflict resolution (§8), Lane B needs the owner's console check (§5). **Lane A2 SHIPPED as WO-1745** (`WorkOrders/WORK_ORDER_1745_save_reset_stale_server_side_detection.md`).

> ## ⚠ STALE 2026-09-15 — §3's "zero refusals on /api/game/save" is a VIEW ARTIFACT, not a measurement.
> WO-1745 found that `api/game/save.js:539` has written a durable audit row on **every** 409
> `SAVE_RESET_STALE` since WO-1598 landed 2026-09-07 — under the event name `save_reset_refused`,
> which `api/admin/db.js`'s `view=authrejects` filter never contained. The view used in §3 was
> structurally incapable of returning one of those rows on the date it was run, so **the number of
> 409s in that window is UNKNOWN**, not zero. §8's premise that the 409 "is invisible to the
> dashboard because it is client-side only" is likewise wrong: it was invisible at the VIEW layer.
> The view is widened and the write moved onto `logAuthReject` in WO-1745; after the next deploy,
> `?view=authrejects&code=SAVE_RESET_STALE&since_hours=168` answers it retroactively. **The body
> below is left unrewritten (CLAUDE.md §15 — dated findings are banner-fixed, not rewritten.)**
**Lane:** Backend / save rail (read-only RCA; no code changed by this lane)
**Opened:** 2026-09-15
**Origin:** owner hypothesis — *"what if its actually the real reason we cannot see true usage"*, i.e.
is the emulator's `Status{statusCode=DEVELOPER_ERROR}` Google sign-in failure the reason the
dashboard cannot see real players?

---

## VERDICT: **KILLED as stated. A different mechanism is CONFIRMED in its place.**

Google sign-in failure **cannot** break the save rail. Every claim below traces to something read or
measured on 2026-09-15.

---

## 1. The save path end to end, and what identity each state carries

`GameStateService.SyncToBackend` → `BackendRequestSigner.TryAttachAsync` → `POST /api/game/save`.

The rail is chosen by the **SHAPE of `_state.BoundWallet`**, never by which headers arrive
(`BackendRequestSigner.cs:7-11`, `:196-198`, `:424-431`):

| Player state | `BoundWallet` | Headers sent | Server `trust` |
|---|---|---|---|
| (a) Google Play signed in | `play-<64hex>` (`GameStateService.cs:1195-1206`, bound only by `BindVerifiedExternalIdentity` `:1177-1192` after the server minted a session) | `X-Session` + `X-Wallet` (`BackendRequestSigner.cs:430-431`) | `google` |
| (b) Wallet connected **and attested** | base58 32-44 | `X-Wallet` + `X-Nonce` + `X-Signature` | `wallet` |
| (c) **Guest — declined or failed sign-in** | `guest-local-<64hex>` = `"guest-local-" + SHA256(deviceUniqueIdentifier + salt)` (`GameStateService.cs:2126`) | `X-Guest-Id` only (`BackendRequestSigner.cs:424-426`) | `guest` |

`EnsureAccount` (`:2118-2138`) runs on **every local save** (`:644`) and mints the guest id whenever
`BoundWallet` is empty. A player who never signs in, or whose sign-in fails, therefore always holds a
cloud-capable identity.

### 1b. And the failed-sign-in player can actually REACH gameplay — checked, not assumed

The chain "sign-in fails → guest id → save lands" only holds if the login wall lets the player
through. It does, on every artifact including Play:

- `LoginPanelController.cs:31` states the rule in the file's own header: *"'Play as Guest' is NEVER
  disabled. SetBusy locks every OTHER control; the escape …"*, and `:400` labels the awaited sign-in
  *"SOFTLOCK LAW: this await is bounded. Guest stays interactable throughout"*.
- `:365-369` builds the `"Play as Guest"` button unconditionally — it is outside every
  `#if GOOGLE_PLAY` block in the file (`:87`, `:108`, `:406`, `:426`, `:441`).
- `:406-407`, the **GOOGLE_PLAY** arm, sets the status text to
  `"Opening Google sign-in... you can still tap Play as Guest."`
- `:301` — *"Forced surface (no dismiss X -- guest is the escape)."*

So `DEVELOPER_ERROR` on the Play artifact costs the player cross-device sync, not access and not
cloud save. This was the one unread link in the KILLED chain; it is now read.

## 2. Does a GUEST save reach Neon at all? — YES. There is no skip gate.

The gate is `CanCloudSync()` (`GameStateService.cs:2251-2257`), quoted verbatim:

```csharp
private bool CanCloudSync() =>
    (IsRealWalletConnected() || IsGuestIdentity(_state?.BoundWallet) ||
                               IsGooglePlayIdentity(_state?.BoundWallet));
```

The guest arm is a first-class `||`, not a fallback. Its own doc comment says why: *"The guest rail
exists because the front door offers 'Play as Guest' and a tester who cannot save is a tester we
lose."*

**Measured, not inferred** — `GET /api/admin/db?view=players&limit=200` against production
`https://defenders-of-the-realm-v2.vercel.app`, 2026-09-15:

```
total rows: 65    guest: 56    wallet: 7    legacy: 2    google: 0
guest  n=56  newest updated_at 2026-09-15T16:21:41.764Z  max payload 8227 bytes
wallet n=7   newest updated_at 2026-09-15T16:08:24.259Z  max payload 13326 bytes
```

14 of the 56 guest rows have more than an hour between `created_at` and `updated_at`; the longest run
2026-08-03 → 2026-09-10. Guest rows are created, updated, and still being written **today**.

## 3. Does `api/game/save` reject a guest-shaped identity? — NO.

`guestEnabled()` (`api/_lib/wallet-auth.js:225-229`) returns `true` unless `GUEST_SAVE_ENABLED` is
explicitly falsy. `save.js:1-40` documents the guest rail as one of three first-class rails and writes
`trust='guest'`. The `trust` distribution above is the proof it is honoured in production.

`GET /api/admin/db?view=authrejects` over the 168h window returns **exactly one** code:

```
AUTH_SESSION_EXPIRED  path=/api/auth/session  mode=wallet  hits=58  distinct_ids=2
```

**Zero** refusals on `/api/game/save` or `/api/game/load`, in any mode, in seven days.

## 4. Live data

Confirmed the relayed figures: `player_data` = 65 rows; **max `heroLevel` across all 65 rows = 20**
(one `wallet` row, bestWave 49, echoCount 6). Guest max `heroLevel` = 3, max `bestWave` = 5.
The external "Sminer" progression (Lv 45 / Wave 146 / Echoes 6/6) is **not in the backend under any
trust value**. Neither is any row above Lv 20.

Guest rows outnumber wallet rows 8:1, so the hypothesis is **weakened, not strengthened**, by the data.

## 5. Does `DEVELOPER_ERROR` affect the PLAY-DELIVERED build? — **NOT PROVABLE FROM HERE.**

What the repo does record:

- `Assets/google-services.json` declares **exactly one** Android `oauth_client`, bound to package
  `com.denellestudios.echoesofelarion` with `certificate_hash` `09078344…` (40 hex = one SHA-1).
- `docs/releases/GOOGLE_PLAY_RC_2026-08-30.md:148-149` records the **Play App Signing** certificate
  SHA-1 as `84:D4:D2:09:58:B6:A0:61:39:9B:B5:FF:28:86:05:23:49:6A:72:22`, and states: *"Google OAuth
  must bind the Android client to the Play App Signing certificate, not the upload certificate."*
- The two fingerprints are **different**, and the same document (`:165-167`) lists as still open:
  *"creation/verification of Android OAuth client entries for package … using the Play signing SHA-1
  certificate(s), followed by a real ID-token exchange from the Play-delivered build."*
- `GOOGLE_IDENTITY_ENABLED=true` in production is asserted by that doc (`:157-160`) — a **doc claim,
  not measured by this lane**.

`google-services.json` is a point-in-time export, so it cannot prove what is registered in Cloud
Console **today**. The exact check the owner can run, and nobody else:

> **Play Console → your app → Test and release → Setup → App integrity → App signing key
> certificate → copy the SHA-1.** Then **Google Cloud Console → APIs & Services → Credentials →
> OAuth 2.0 Client IDs**, project `defenders-of-the-realm-echos`: confirm an **Android** client
> exists for package `com.denellestudios.echoesofelarion` whose SHA-1 equals that value. If no such
> client exists, the Play-delivered build gets the same `DEVELOPER_ERROR` the emulator got.

Corroborating measurement either way: **`player_data` holds zero rows with `trust='google'`, ever.**
Consistent with "the Play identity rail has never successfully minted a session for a real player" —
but equally consistent with "no Play-delivered player has tried", so it is not on its own a proof.

## 6. Blast radius — what a player actually loses

For a guest: **nothing structural.** The row is in Neon, keyed by the device hash, and `load.js`
serves it back. The real exposure is that `guest-local-*` is derived from
`SystemInfo.deviceUniqueIdentifier` — a **reinstall on a new device** cannot recover it, because the
id is the sole credential and it is not portable. That is a known, deliberate second-class posture,
not a defect.

For the player hit by §7 below: **everything after the first 409.** Local play continues normally,
the HUD shows progress, and the cloud row silently freezes at the last accepted state. A reinstall or
a device change costs every level, echo and structure earned since then.

---

## 7. ⛔ WHAT IS ACTUALLY LOSING PROGRESS — measured in live external-player traces

`GET /api/admin/db?view=traces&session=wt-4eab26ab5f23` (build
`2026.09.10.364108@defenders-pi.vercel.app`, 2026-09-11) returns this line, verbatim from the
player's own device:

```
[Flow:Sync] cloud save REFUSED - why=reset-stale http=409
body={"ok":false,"code":"SAVE_RESET_STALE","ref":"b9815686"}
- this device's resetEpoch is OLDER than the stored one, so it is trying to push a town from
BEFORE a New Game that happened elsewhere. NOT RETRYABLE: the answer is identical every time,
so the marker is DROPPED rather than looped. The local save is untouched.
```

A sweep of the 26 `web_trace` sessions with >60 captured lines found `SAVE_RESET_STALE` in **2
sessions — possibly one device** (9 occurrences total): `wt-4eab26ab5f23` and `wt-8704b03e8121`, both
on the same build, both on 2026-09-11, and the trace rows carry no identity, so they cannot be
separated. The client's handling is at `GameStateService.cs:3266-3273` — classified `ResetStale`,
marker dropped, **never retried, never surfaced to the player**.

This is a permanent, per-device, silent cloud-save outage that leaves local play looking perfectly
healthy. It explains "a Lv 45 player whose progression is not in the backend" without requiring any
auth failure at all.

### ⛔ 7b. `web_trace` IS STRUCTURALLY BLIND TO ANDROID — do not read platform out of it

Every session in `web_trace` is a WebGL / Pi build, and that fact carries **no information**:
`Assets/_Modules/Core/Diagnostics/WebTrace.cs:41` says so in its own header —

> *"The actual remote POST is WebGL-only (`#if UNITY_WEBGL && !UNITY_EDITOR`)"* — and the guard is
> at `:293`.

An Android device **never** POSTs to `/api/trace`. So:

1. **Sminer's platform is NOT PROVEN.** An earlier draft of this WO inferred "he must be on Pi
   because all traces are Pi." That is a tautology and is struck.
2. **The 409 may be happening on Android at any rate at all and leave no trace.** The only Android
   evidence channel is the F8 harness on a device the owner holds. Lane A2 below exists precisely to
   close this blind spot server-side, where platform does not matter.

### 7c. A second, weaker signature worth one line

Of the 56 guest rows, 14 have more than an hour between `created_at` and `updated_at` — spans of
**38, 37, 34 and 22 days**. Every one of them is still `heroLevel` 1-3, `bestWave` 0-5, and eight of
the fourteen show `wavesCompleted = 0`. A player whose row is being written for five weeks while the
level never moves is *consistent with* a frozen cloud row, and equally consistent with genuine
early-game churn. **NOT PROVEN either way** — recorded because Lane A2's audit rows would settle it
in a day.

## 8. Smallest change that makes a guest player's progress durable

**Lane A — ⛔ NEEDS AN OWNER RULING BEFORE ANY CODE. Do not pick one of these yourself.**

The defect is real and narrow: at `Assets/_Modules/Core/State/GameStateService.cs:3266-3273` a 409
`SAVE_RESET_STALE` is classified NOT RETRYABLE and the marker is **dropped forever**, with no second
attempt, no reconciliation and nothing shown to the player. The device then plays on locally while
its cloud row is frozen — the worst of both outcomes, and the only one the current code can produce.

But **what should replace it is a design decision about whose town survives, not an RCA finding.**
The server holds a town stamped with a NEWER `resetEpoch`; the device holds an older one. Three
mutually exclusive resolutions:

| | Behaviour | Cost |
|---|---|---|
| **(a)** Pull the server's town and overwrite local | the cloud town wins | the player loses whatever they played on this device since the divergence |
| **(b)** Surface the conflict and let the player choose | nobody loses silently | needs a UI screen; a player mid-session gets a modal about save state |
| **(c)** Adopt the server's epoch and push local anyway | this device wins | **overwrites the town the 409 exists to protect** — the exact write WO-1598 refuses |

⛔ **(c) was written into an earlier draft of this WO and is called out here as a trap**, not a
recommendation: it is a client-side route around a server guard, and it silently destroys the "New
Game that happened elsewhere". Do not implement it because it is the smallest diff.

**Do not remove the server-side 409** under any of the three. The refusal is correct; the client's
handling of it is not.

**Open question the lane must answer as part of (a)/(b):** how does a Pi **guest** — a single
device-hash identity, one device — end up BEHIND its own server row at all? The epoch is written in
exactly one place, `GameStateService.cs:1562` (the file's own comment calls it *"the only line in the
game that writes it"*). A candidate worth checking first, **not asserted**: Pi Browser storage
eviction wiping the local epoch while the Neon row keeps the higher value — the same environment
PROD-022 records as killing the tab every 30-60 s. Instrument before theorising (CLAUDE.md §12).

**Lane A2 (detection — UNBLOCKED by the ruling, ship it on its own):** the 409 is invisible to the
dashboard because it is client-side only, and per §7b it is invisible on Android **entirely**.
`api/game/save.js` should write an audit row for `SAVE_RESET_STALE` the way every auth refusal
already does via `logAuthReject` — then `view=authrejects` surfaces it, on every platform, and this
class of outage stops depending on someone reading a WebGL-only trace blob. This is the single
highest-value change in the WO: it is server-side, platform-blind, needs no ruling, and it converts
§7c's "NOT PROVEN either way" into an answer within a day.

**Lane B (parked on §5):** nothing to build until the owner runs the Play Console / Cloud Console
check. If the Android OAuth client is missing the App Signing SHA-1, the fix is console-side, not
code-side.

## 9. ⚠ Does this contradict today's analytics work?

**No.** It is independent and consistent. `EventTracker.cs:290-294` sends only `Content-Type`, so
`api/events/track.js` attributed every player to the literal `unverified` regardless of sign-in —
fixed server-side in `50370c1fb`. That is the **analytics** pipe. The 409 above is the **save** pipe.
Neither causes the other, and neither is caused by Google sign-in. One correction worth recording:
`analytics_events` holds 138,798 rows with the newest at 2026-09-15T16:21:24Z, so telemetry volume was
never the problem — only its attribution was.

## What NOT to touch

- Do **not** weaken the guest rail, `guestEnabled()`, or the guest rate limiter. They work.
- Do **not** remove the 409 refusal server-side. `SAVE_RESET_STALE` is protecting a real invariant
  (WO-1598); the defect is the client's handling of it, not the refusal.
- Do **not** touch `EventTracker` / `track.js` — a different lane landed that today.

## Acceptance criteria

**Lane A2 (do this first — no ruling needed):**
1. `view=authrejects` returns a `SAVE_RESET_STALE` row after a refused save, proven against a real
   response, not by reading the handler.
2. No change to the 409 status, body, or the guard itself.

**Lane A (after the ruling):**
3. The behaviour matches the option the owner named — (a), (b) or (c) from §8 — and the WO records
   which, with the ruling quoted.
4. A device whose `resetEpoch` is behind the stored one ends in a defined state, proven by a headless
   fixture, never by reading the code.
5. No device silently continues with a frozen cloud row: either it reconciles or the player is told.
6. Brace + NUL gate clean on every `.cs` touched (`python tools/gate_brace.py` as well as the gate);
   `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` judged by the MARKER on a FRESH log, not exit code.
