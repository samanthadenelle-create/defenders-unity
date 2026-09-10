# WORK ORDER 1699 - Pi Desktop (App Studio) is not recognised as a Pi host, so the game shows the SKR skin and the Devnet Stub Wallet

**Status:** IMPLEMENTED - awaiting owner felt-check in Pi Desktop
**Minted:** 2026-09-10 16:25 by the CLI lead from the owner's live report ("the stub shows in pi browser, should be the pi adapter"; screenshot: Pi Desktop > App Studio > "Verifying Your App", the game panel showing Connect Wallet). Main-line banner bumped 1699 -> 1700 in the same edit.
**Silo:** Web / Pi platform. One file of logic (`Assets/Plugins/WebGL/PiBridge.jslib`), one page template (instrumentation), no .cs.
**Exception named (orchestration cadence):** the lead implemented this directly. The owner asked for it live while near her weekly token limit, and the change is one detection predicate with the captured fingerprints in hand.

## 1. Captured data (2026-09-10, `analytics_events` event_name=web_trace, build 2026.09.10.364108@defenders-pi.vercel.app)
- 21:05:25Z session wt-ca70fc969c94 (Pi Desktop): `[Flow:Skin] WebGL host is not Pi Browser - resolving the SKR skin (WO-787).` then `Currency skin resolved: 'skr'` then `Connect OK - stub...vcoP (Devnet Stub Wallet)`. That stub is what the owner saw.
- 21:22:04Z session wt-pia5a145917 (Pi Desktop, after the fingerprint crumb went live):
  `host ua=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) PiNetwork/0.6.3 Chrome/134.0.6998.205 Electron/35.2.0 Safari/537.36 referrer=https://app-cdn.minepi.com/ framed=true href=https://defenders-pi.vercel.app/ windowPi=object`
- 21:19:10Z session wt-pi606bece2a (Seeker, Pi Browser):
  `host ua=Mozilla/5.0 (Linux; Android 16; Seeker Build/BP2A.260812.100.A3; wv) ... Mobile Safari/537.36 PiBrowser/1.17.1 referrer=https://app-cdn.minepi.com/ framed=true href=https://defenders-pi.vercel.app/ windowPi=object`, followed by `[Flow:Skin] Pi Browser host detected - resolving the Pi skin.`, `Pi.init OK on TESTNET/sandbox` (WO-1325, intended), `PiAuthenticate(scopes=username)`.
- `https://sdk.minepi.com/pi-sdk.js` (fetched 2026-09-10): `getHostPlatformURL` reads `document.referrer`; host messages go over `window.parent.postMessage`. The SDK never reads the user agent.

## 2. Cause
`PiIsPiBrowser` (`Assets/Plugins/WebGL/PiBridge.jslib`) returned 1 only for a `PiBrowser` UA token or a `pinet.com` host. Pi Desktop's Electron host sends `PiNetwork/0.6.3` and the app is served from defenders-pi.vercel.app, so the predicate returned 0 and `CurrencySkinResolver` (WO-787 Part C) routed to the SKR skin, whose bootstrap auto-connects the Devnet Stub Wallet.

## 3. Change
Two signals added to `PiIsPiBrowser`, after the existing UA token and before the pinet.com host rule:
1. UA token `PiNetwork/` (case-insensitive).
2. framed (`window.top !== window.self`) AND `document.referrer` host is a dotted suffix of `.minepi.com` (exact-or-suffix matching, never a substring; the bare `minepi.com` host is deliberately not accepted).
Off-Pi browsers keep returning 0. Predicate proven in Node against the two captured fingerprints plus five negatives (plain Chrome, evil suffix `minepi.com.evil.tld`, referrer without a frame, bare host): `PIBRIDGE_PREDICATE_OK 7/7` (scratchpad `test_pibridge.js`, 2026-09-10).

Instrumentation kept in `Assets/WebGLTemplates/Pi/index.html`: a `[PiLifecycle] host ua=... referrer=... framed=... windowPi=...` crumb at boot, and `[PiLifecycle] loader progress=N% deviceMemoryGB jsHeapUsedMB jsHeapLimitMB upMs` crumbs every 10% and every 1% from 60% (owner: the phone dies at 65% every time; the last crumb that lands is the evidence, feeding WO-1314 / WO-1484).

## 4. Acceptance
- Pi Desktop App Studio preview: the Title corner reads "Sign in with Pi"; the trace shows `Pi Browser host detected - resolving the Pi skin.` for a session whose host crumb carries `PiNetwork/`.
- Seeker Pi Browser: unchanged (Pi skin, proven 21:19Z on the old build).
- Plain desktop Chrome on defenders-pi.vercel.app: SKR skin (WO-787 Part C unchanged).

## 5. Not proven
- The desktop felt-check itself (owner). App Studio's "Verifying Your App" step should complete once Pi.authenticate runs there; not yet observed.
- Whether Pi Desktop's referrer stays `app-cdn.minepi.com` on the published pinet.com surface (that path is covered by the host rule regardless).
- The 65% loader crash on the Seeker: two boots at 21:17Z died in `unity-loading` with no pagehide (the shape of a WebView kill); the successful 21:19Z load predates the progress crumb, and logcat began after the crashes. Still open under WO-1314 / WO-1484.
