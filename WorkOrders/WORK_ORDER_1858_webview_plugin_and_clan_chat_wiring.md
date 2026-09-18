# WORK ORDER 1858 — Install a WebView plugin + wire the real clan into the Cherry Chat embed

**Status:** READY FOR LEAD REVIEW

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

## IMPLEMENTATION RECORD (SME agent, 2026-09-17)

**Part A — gree/unity-webview DOES expose a scriptable UPM git-URL path. Installed it, no
`.unitypackage` import attempted.**

- Evidence for the UPM path: fetched `https://raw.githubusercontent.com/gree/unity-webview/master/README.md`
  directly (not the WebFetch summary alone — re-verified with `curl` + `grep`), lines 30-52, which give:
  ```
  "net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package"
  ```
  (and a `-nofragment` variant at `/dist/package-nofragment`). Confirmed the target is REAL, not just
  documented, by reading `dist/package/package.json` over the GitHub contents API: `{"name":
  "net.gree.unity-webview", "version": "1.0.0", ...}`, and `dist/package/unity-webview.asmdef`:
  `{"name": "unity-webview", "references": ["Unity.InputSystem"], "includePlatforms": ["Android",
  "Editor", "iOS", "macOSStandalone", "WebGL", "WindowsStandalone32", "WindowsStandalone64"]}`.
- `Packages/manifest.json` — added `"net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package"`
  to `dependencies` (the WITH-Fragment variant, the README's primary/first-listed form; supports file
  input fields, which the `-nofragment` variant trades away).
- `Assets/_Modules/HUD/DeNelle.HUD.asmdef` — added `"unity-webview"` to `references` (matches the
  package's own asmdef `name`).
- New file `Assets/_Modules/HUD/ClanChatWebHostGreeWebView.cs` — `IClanChatWebHost` implemented against
  the REAL `Gree.UnityWebView.WebViewObject` API, read at source from
  `dist/package/Assets/Plugins/WebViewObject.cs` (namespace `Gree.UnityWebView`, class `WebViewObject`)
  and the real sample `sample/Assets/Scripts/SampleWebView.cs` — every method name/signature used
  (`Init`, `LoadURL`, `SetVisibility`, `SetMargins`, `IsInitialized`, `IsWebViewAvailable`,
  `CallFromJS`->`cb`, `CallOnError`->`err`, `CallOnHttpError`->`httpErr`) was grepped out of those two
  files, not taken from the README's prose. Key decisions, cited in the file's own header:
  - Coordinate conversion: `IClanChatWebHost.SetViewport` is a normalised, BOTTOM-LEFT-origin rect;
    gree's `SetMargins(left, top, right, bottom)` is pixels from each edge with `top` measured from the
    TOP (confirmed by reading `SetMargins`'s body at `WebViewObject.cs:985-1119` — the Android `relative`
    branch divides `top` by `Screen.height` from the top edge). `ApplyViewport` is the one conversion
    point.
  - The `unity:` scheme bridge needs no extra JS injection on Android: the sample's `EvaluateJS` shim for
    `window.Unity.call` is `#if UNITY_EDITOR_OSX || ... || UNITY_IOS`-gated with an empty `#else` for
    everything else, so Android's native side already intercepts `unity:` navigation and delivers it
    through `CallFromJS` — the exact third fallback `site/clan-chat.html`'s `sendToUnity` already speaks
    (`window.location.href = 'unity:' + encodeURIComponent(json)`, unchanged, not touched by this ticket).
  - Init-then-wait: `WaitForInitThenLoad` polls `IsInitialized()` (bounded by an 8s timeout) before
    calling `LoadURL`/`SetMargins`/`SetVisibility`, mirroring the current sample's own fix for
    gree/unity-webview issue #1094 (a documented race where an immediate `LoadURL` can be dropped).
  - Every call is `Guard.Try`-wrapped; `IsAvailable` uses `WebViewObject.IsWebViewAvailable()`.
  - `ClanChatWebHostFactory.Create()` picks the real host when `IsAvailable`, else disposes it and
    returns `ClanChatWebHostUnavailable` — this is the one thing `ClanChatPanel.EnsureBuilt` now calls
    instead of always constructing `ClanChatWebHostUnavailable`.
- Updated the now-resolved headers in `IClanChatWebHost.cs` and `ClanChatPanel.cs` (both previously
  stated "no WebView plugin exists" as current fact; left as dated history + the resolution, per the
  canon-maintenance discipline against a stale in-code claim).
- **What was NOT attempted, and why:** no `.unitypackage` interactive import — the UPM git path made
  that unnecessary, so the STOP condition in the WO never triggered. No changes to
  `site/clan-chat.html` were needed — its bridge already speaks the plugin's exact convention (verified
  by reading its own `sendToUnity`, which the file's own header already named as the convention it
  speaks, dated the same day as WO-1847).

**Part B — `ClanRoomBinding.ClanId` wired to a real `GET /api/clan/me` call.**

- The seam is `ClanRoomBinding` (`Assets/_Modules/HUD/ClanChatSource.cs:52-99`) — confirmed at source,
  matches the WO's guess of the name.
- New file `Assets/_Modules/HUD/ClanMembershipClient.cs` — `RefreshAsync()` calls
  `GET /api/clan/me?playerId=<wallet>` with the standard wallet-rail signed-GET pattern (mirrors
  `Assets/_Modules/Wallet/HeartboundStatusClient.cs`'s own `BackendRequestSigner.TryAttachAsync(req,
  playerId, null, false)` shape for a read-only GET). Fails closed and skips the network call entirely
  for a guest/no identity (the VM's existing `NoWallet` state already covers that case — matches
  `ClanChatSource.WalletAddress`'s own guest-filtering, read at source before writing this).
  `ApplyResponseJson(string)` (public, for the regression) parses the response against the EXACT shape
  read from `api/clan/me.js` at source (`200 {ok:true, clan:null}` = real success/no clan;
  `200 {ok:true, clan:{clanId,...}}` = real success/has clan) using two narrow regexes rather than
  Newtonsoft, honoring `ClanChatSource.cs`'s own stated "no new assembly dependency" discipline (HUD's
  asmdef references only `DeNelle.Core` + `DeNelle.Data`). A failed/malformed/empty response HOLDS the
  previous binding rather than clearing it (same fail-closed-hold-previous rule
  `HeartboundStatusClient.Apply` uses) — only an explicit, well-formed `clan:null` clears it.
- `ClanChatPanel.SetVisible(true)` now fires `ClanMembershipClient.RefreshAsync().Forget()` on every
  open (not just first build), and `_vm.Changed` is now handled by `HandleVmChanged` (was `Repaint`
  directly) which repaints AND re-attempts `EnsureLoaded()` when visible — this is what lets the room
  arriving asynchronously (after the panel already opened in its `NoRoom` state) actually mount the
  embed, rather than requiring a second manual open. `EnsureLoaded()`'s existing dedupe-by-`_loadedUrl`
  and `CanEmbed` guard make this a safe no-op on every other VM change (mount events, unread counts).
- No-clan copy: per the task's binding localization-law instruction, ALL of `ClanChatPanel`'s
  player-facing strings (previously hardcoded literals) now resolve through a new
  `ClanChatStrings`/`LocalizedText` key set (`clanChat.noWallet`, `clanChat.noClan`,
  `clanChat.unavailable`, `clanChat.genericError`, `clanChat.opening`), added to
  `Assets/Resources/Data/Canonical/en.json` (validated: `python3 -m json.load` succeeds; confirmed
  consistent CRLF line endings before/after the edit, no LF-only lines introduced). The no-clan copy
  reads **"You're not in a clan yet."** — plain, not an imperative ("Join a clan...") and not phrased as
  something the player did wrong, per the WO's copy rule.
- Regression: new `Assets/Tests/EditMode/ClanMembershipClientTests.cs` (5 cases) drives
  `ClanMembershipClient.ApplyResponseJson` directly with the server's own documented response shapes
  (no network/scene needed): a real clan response binds the exact UUID; a `clan:null` response clears a
  previously-bound id as a real success; an empty/malformed/`ok:false` body holds the previous binding;
  a non-UUID `clanId` is refused by `ClanRoomBinding.Set`'s own shape check (defense in depth). Every
  test resets `ClanRoomBinding` in `[TearDown]` since it is process-wide static state.

**Acceptance criteria status:**
- [x] `Packages/manifest.json` references the plugin (git-URL UPM path, not a `.unitypackage`).
      Compile-clean is NOT verified by this agent — instructed not to run the Unity gate; the lead
      confirms `COMPILE_GATE_OK` on the combined tree.
- [x] `IClanChatWebHost` has a real, non-throwing implementation (`ClanChatWebHostGreeWebView`),
      Guard-wrapped throughout, targeting Android per the plugin's own `includePlatforms`
      (`Android`/`Editor`/`iOS`/`macOSStandalone`/`WebGL`/`WindowsStandalone32`/`WindowsStandalone64`).
      Not yet run on a device or in the Editor by this agent (no Unity available in this session) — the
      lead's headless/device verification is the proof of "non-throwing" at runtime; the static/API-shape
      proof (every call matches a real method on the real class) is what this record cites.
- [x] Wallet-in-a-clan -> `ClanRoomBinding.ClanId` set to the real UUID before mount — proven by
      `ClanMembershipClientTests.a_wallet_in_a_clan_binds_the_real_server_side_uuid` (and the "room
      arriving later" path is additionally proven at the VM layer by the pre-existing
      `ClanChatVMTests.a_room_arriving_later_promotes_the_vm_out_of_its_error_state`).
- [x] Wallet-with-no-clan -> plain no-clan copy, no mount attempt — proven by
      `ClanMembershipClientTests.a_wallet_with_no_clan_clears_the_binding_as_a_real_success_not_an_error`
      at the binding layer, and by the unchanged `ClanChatVM.NoRoom` / `EnsureLoaded`'s `!CanEmbed`
      early-return at the panel layer (pre-existing, unchanged logic — re-read at source, not assumed).
- [~] `CHERRY_APP_ID` placeholder check still fires correctly as the ONE remaining blocker — NOT
      independently re-verified end-to-end by this agent (would need a live device/Editor run through
      the whole bridge, which this SME task did not run). Reasoned correctness: nothing in
      `site/clan-chat.html`'s `boot()` function was touched, its `missing_app_id` fatal() path is
      unchanged, and `ClanChatPanel.PlayerFacingError`'s `"missing_app_id"` case is unchanged (still maps
      to the `Unavailable` string). The lead should confirm this on a real run per the WO's test plan
      step 3.
- [x] `python tools/gate_brace.py` (the project's exact gate script, not the raw one-liner) + a NUL-byte
      scan run on every `.cs` file touched — both clean, 5/5 files, shown above. Unity
      `COMPILE_GATE_OK` / `REGRESSION_OK` NOT run by this agent per explicit instruction — lead's job.

**Files touched:**
- `D:\EoA\Packages\manifest.json`
- `D:\EoA\Assets\_Modules\HUD\DeNelle.HUD.asmdef`
- `D:\EoA\Assets\_Modules\HUD\ClanChatWebHostGreeWebView.cs` (new)
- `D:\EoA\Assets\_Modules\HUD\ClanMembershipClient.cs` (new)
- `D:\EoA\Assets\_Modules\HUD\ClanChatPanel.cs`
- `D:\EoA\Assets\_Modules\HUD\IClanChatWebHost.cs` (header only)
- `D:\EoA\Assets\Resources\Data\Canonical\en.json`
- `D:\EoA\Assets\Tests\EditMode\ClanMembershipClientTests.cs` (new)
- This WO file (status + record)

Not touched: `api/`, `test/`, `site/clan-chat.html`, `CHERRY_APP_ID`, WO-1848's files.
