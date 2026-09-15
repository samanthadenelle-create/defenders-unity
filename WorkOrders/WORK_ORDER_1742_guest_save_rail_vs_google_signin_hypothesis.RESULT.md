# WO-1742 RESULT — RCA complete, read-only lane

**Date:** 2026-09-15
**Lane:** read-only RCA. No code, no commits, no DB writes, no deploys. Admin GETs only.
**Artifacts changed by this lane:** the WO itself, this RESULT, and the `CLI_LANES_WO_NUMBERS.md`
banner bump (1742 -> 1743) made in the same edit as the mint.

## Verdict on the owner's hypothesis

**KILLED as stated.** Google sign-in failure (`DEVELOPER_ERROR`) cannot be the reason the dashboard
cannot see real players, because a failed sign-in does not remove the player's cloud identity — it
leaves the `guest-local-<64hex>` id that `EnsureAccount` mints, and the guest rail is live and busy.
The chain holds on the **Play** artifact too: `"Play as Guest"` is built outside every
`#if GOOGLE_PLAY` block and is never disabled (`LoginPanelController.cs:31`, `:301`, `:365-369`,
`:400`), and the Play arm's own status copy is *"Opening Google sign-in... you can still tap Play as
Guest."* (`:406-407`). `DEVELOPER_ERROR` costs cross-device sync, not access and not cloud save.

**A different mechanism is CONFIRMED in its place:** 409 `SAVE_RESET_STALE`, classified NOT RETRYABLE
and silently dropped by the client, measured in two live external-player trace sessions.

## Evidence captured this session (all 2026-09-15)

| Claim | Evidence |
|---|---|
| Guest identity is minted for every un-signed-in player | `GameStateService.cs:2118-2138` (`EnsureAccount`), called at `:644` on every local save |
| The cloud gate admits guests as a first-class arm | `GameStateService.cs:2251-2257` (`CanCloudSync`) |
| The client sends the guest credential | `BackendRequestSigner.cs:424-426` (`X-Guest-Id`) |
| The server honours it | `api/_lib/wallet-auth.js:225-229` (`guestEnabled()` default ON); `api/game/save.js:1-40` |
| Guest saves land in quantity | `view=players&limit=200`: 65 rows — guest 56, wallet 7, legacy 2, google 0 |
| Guest saves land NOW | newest guest `updated_at` = `2026-09-15T16:21:41.764Z` |
| Guest saves persist for weeks | 14 guest rows with >1h create→update span; longest 2026-08-03 → 2026-09-10 |
| No save-path auth refusals at all | `view=authrejects`, 168h window: only `AUTH_SESSION_EXPIRED` on `/api/auth/session`, mode `wallet`, 58 hits / 2 ids |
| Max hero level confirmed at 20 | computed over all 65 full records; guest max 3, wallet max 20 |
| The real failure | `view=traces&session=wt-4eab26ab5f23`: `cloud save REFUSED - why=reset-stale http=409 body={"ok":false,"code":"SAVE_RESET_STALE",...}` on build `2026.09.10.364108@defenders-pi.vercel.app` |
| Spread of that failure | 2 of 26 sampled trace sessions (>60 lines each), 9 occurrences: `wt-4eab26ab5f23`, `wt-8704b03e8121` — same build, same day, no identity in the rows, so **possibly one device** |
| `web_trace` cannot see Android at all | `Assets/_Modules/Core/Diagnostics/WebTrace.cs:41` and the `#if UNITY_WEBGL && !UNITY_EDITOR` guard at `:293` |
| Guest can always get past the login wall, on every artifact | `LoginPanelController.cs:31`, `:301`, `:365-369`, `:400`, `:406-407` |
| Client drops it permanently | `GameStateService.cs:3266-3273` — `ResetStale`, marker dropped, warn-only |
| Telemetry volume was never the problem | `view=overview`: `analytics_events` 138,798 rows, newest `2026-09-15T16:21:24.272Z` |

## Explicitly NOT PROVEN

- **Whether the Play-delivered build hits `DEVELOPER_ERROR`.** `Assets/google-services.json` declares
  one Android OAuth client bound to SHA-1 `09078344…`; `docs/releases/GOOGLE_PLAY_RC_2026-08-30.md:148`
  records the Play App Signing SHA-1 as `84:D4:D2:09:…`. They differ, and that same doc (`:165-167`)
  lists the Play-signing OAuth client as an unfinished external step. A downloaded
  `google-services.json` cannot prove what Cloud Console holds today. **The one check that settles it
  is named in WO-1742 §5** and only the owner can run it.
- **`GOOGLE_IDENTITY_ENABLED=true` in production.** Asserted by a release doc; this lane did not read
  the Vercel env.
- **That "Sminer" specifically hit the 409.** The 409 is proven for two anonymous sessions; no
  username column exists in `player_data` to tie it to him. What IS proven is that his stated
  progression is in no row under any trust value, and that a mechanism producing exactly that outcome
  is live.
- **Sminer's platform.** An earlier draft inferred "Pi, because every trace session is Pi." That is a
  tautology — `WebTrace` only POSTs from WebGL builds (`WebTrace.cs:41`, `:293`), so Android is
  structurally absent from that table and its absence proves nothing. Struck from the WO.
- **Whether the 14 long-lived guest rows are frozen or just churn.** Spans of 38/37/34/22 days, all
  still `heroLevel` 1-3, eight of fourteen at `wavesCompleted = 0`. A signature, not a finding.
  Lane A2's audit rows would settle it.

## Contradiction check against today's analytics work

None. The analytics defect (`EventTracker.cs:290-294` sending no identity headers, fixed server-side
in `50370c1fb`) and the save defect (409 dropped client-side) are different pipes with independent
causes. Neither is caused by Google sign-in. Nothing found today reverses that conclusion.

## Handover

- **Lane A2 is implementable NOW** — server-side audit row for `SAVE_RESET_STALE` in
  `api/game/save.js`. No ruling needed, platform-blind, and it is the change that would have made
  this whole investigation a dashboard query. File-disjoint from today's analytics lane
  (`api/events/track.js` / `EventTracker.cs`).
- **Lane A is BLOCKED on an owner ruling**, deliberately. The fix is not "retry the save" — the
  server holds a town the device's epoch is behind, so the resolution decides whose town survives.
  Three options are laid out in WO-1742 §8; option (c) is flagged as a trap because it routes around
  the WO-1598 guard client-side and destroys the other town. An RCA lane does not get to pick.
- **Lane B is parked** pending the owner's Play Console / Cloud Console SHA-1 comparison (§5).

**Corrections applied after review, recorded so they are not re-derived:** the "Play build has no
guest escape" possibility was checked and disproved rather than assumed; the platform inference from
`web_trace` was struck as a tautology; "2 distinct sessions" was softened to "possibly one device".
