# WORK ORDER 1858 — Install a WebView plugin + wire the real clan into the Cherry Chat embed

**Status:** READY TO IMPLEMENT

**Minted:** 2026-09-17, by the CLI lead, from the owner's direct ask: *"I want the whole vision
completely running the solution all the way through for the pitch of people working together so I
can test it."* WO-1847 (Cherry Chat embed) landed a correct, fully-scaffolded client (ClanChatPanel,
ClanChatVM, ClanChatSource, IClanChatWebHost, site/clan-chat.html) but flagged two real gaps that
block it from actually rendering anything: no WebView plugin exists in the project at all, and
nothing currently feeds a real clan's server-side id into the embed. This ticket closes both. The
THIRD gap from WO-1847 — `CHERRY_APP_ID` being a placeholder — is a separate, owner-only action
(a real Cherry Chat account at portal.cherry.fun) and is explicitly OUT of this ticket's scope; the
code path must simply fail loudly and correctly when it is still a placeholder, exactly as WO-1847
already built it to do.

## Scope

### Part A — Install gree/unity-webview (owner-ruled choice: free, MIT, over paid Vuplex)

Per the standing zero-revenue/$200-month cost discipline, install **gree/unity-webview**
(https://github.com/gree/unity-webview), not Vuplex. Prefer the UPM git-package installation path
over a hand-imported `.unitypackage` if the package exposes one (check its README/repo for a
`package.json` under a `plugins/` or similar subfolder and a documented git URL for
`Packages/manifest.json` — this is scriptable and avoids an interactive Editor import). If no UPM
path exists and only a `.unitypackage` is offered, STOP and flag this back to the lead rather than
attempting an interactive import from a batchmode agent context — say so plainly in the hand-back
rather than guessing at a workaround.

`IClanChatWebHost` (from WO-1847, `Assets/_Modules/HUD/IClanChatWebHost.cs`) is the seam already
built for exactly this: implement it against the installed plugin's real WebView API (gree's is
typically `WebViewObject`). Do not change `IClanChatWebHost`'s public shape unless it is genuinely
insufficient — read it first and say so if it needs to change, rather than silently widening it.

### Part B — Wire the real clan into `ClanRoomBinding.ClanId`

Read `Assets/_Modules/HUD/ClanChatVM.cs` and find `ClanRoomBinding` (or wherever the WO-1847 record
says the open wiring point lives — re-verify at source, the exact name/location may have shifted).
Nothing today calls `GET /api/clan/me` from the client and feeds the result in. Build the minimal
remote clan client needed: on opening clan chat, call `/api/clan/me`, and if the wallet has a clan,
set `ClanRoomBinding.ClanId` to the real `clans.id` before the embed mounts (so `roomId` in the
Cherry SDK call is a real, correct, per-clan value — never a placeholder, never the wallet address
itself, per the existing per-clan-room-isolation design already proven in the SDK research).
If the wallet has NO clan, the chat door should say so in plain language (no investment/jargon
language per the standing copy rule) rather than attempting to mount a room for a clan that doesn't
exist.

### Non-scope

- Do NOT touch `CHERRY_APP_ID` or attempt to obtain/guess one — leave the placeholder and its
  existing fail-loud check exactly as WO-1847 built it.
- Do NOT touch the server-side clan endpoints (`api/clan/*.js`, `api/_lib/clan*.js`) — this ticket
  is 100% client-side wiring plus the one Package Manager change.
- Do NOT touch WO-1848's test file or any other in-flight lane.

## Acceptance criteria

- [ ] `Packages/manifest.json` (or equivalent) references the WebView plugin; the project still
      compiles clean (`COMPILE_GATE_OK`).
- [ ] `IClanChatWebHost` has a real, non-throwing implementation backed by the installed plugin on
      at least the Android target (this is a mobile game; Android is the shipping platform).
- [ ] With a wallet that IS in a clan, opening clan chat sets `ClanRoomBinding.ClanId` to that
      clan's real UUID before the embed mounts — prove this with a test/regression, not just prose.
- [ ] With a wallet that is NOT in a clan, the chat door shows a plain-language no-clan state and
      never attempts to mount an embed with an empty/placeholder room id.
- [ ] The existing `CHERRY_APP_ID` placeholder check still fires correctly (i.e., with a real
      WebView plugin and a real ClanId wired, the ONLY remaining failure is the app-id placeholder,
      proving this ticket closed exactly the two gaps it set out to close and nothing else is
      silently still broken).
- [ ] `python tools/gate_brace.py` + NUL scan on every `.cs` file touched. Unity `COMPILE_GATE_OK` +
      `REGRESSION_OK <n>/<n>` on the combined tree.

## Test plan

1. Two-wallet setup (reuse WO-1848's story if convenient, or a fresh minimal one): wallet A creates
   a clan. Open clan chat as wallet A. Confirm `ClanRoomBinding.ClanId` resolves to the real clan id
   (a debug/editor read is fine — this cannot be proven on-device without a real Cherry app id).
2. A wallet with no clan opens the chat door. Confirm the plain-language no-clan message, no embed
   mount attempt.
3. Confirm the placeholder-app-id failure still fires correctly once A and B above are working,
   isolating that as the one remaining blocker.

## Rollback

Revert the Package Manager manifest change and the `IClanChatWebHost` implementation; `ClanRoomBinding`
wiring reverts to its WO-1847 unwired state. No schema/save/server implications.

## Copy rules

No investment/jargon language. The no-clan state should read plainly ("You're not in a clan yet" or
similar), never as an error the player caused.
