# Pi Ad Network expansion - readiness research

**Researched 2026-09-10 (CLI research lane, read-only).** Every claim below traces to a URL fetched
today or a repo file opened today at the cited line. Where a thing could not be established from a
source read today it is written UNPROVEN with the check that would settle it. Nothing here is
inferred and stated as fact.

Supersedes nothing. Companion to `docs/reference/PI_AD_NETWORK_APPROVAL.md` (2026-09-02), which
remains correct on the approval half; this doc adds the SDK/API surface and the repo gap table.

---

## 1. What the blog post actually announces

Source: https://minepi.com/blog/ad-network-expansion/ (fetched 2026-09-10). **The post is dated
April 10, 2025** - it is not a new announcement. It opens: *"Last year, we introduced the vision and
initial pilot for the Pi Ad Network - where advertisers use Pi to place ads in the Pi ecosystem and
Pi Apps"*.

What it announces:

- The Ad Network moves past the initial pilot (5 community apps) and is **open to all
  ecosystem-listed Pi Apps**.
- Eligibility: the app must be *"listed in the Mainnet Ecosystem Interface and compliant with
  developer ecosystem guidelines."*
- *"Applying does not guarantee your app will be included."*
- Developers *"earn directly in Pi, in proportion to the real attention their apps generate."*
- How to apply, verbatim four steps: open Pi Browser -> "Develop" -> Developer Portal; select the
  app; scroll to the bottom of the app detail page and tap the **"Dev Ad Network"** button; click
  **"Ads Checklist"** and complete all three steps of the Pi Ad Platform Checklist.

What the post does **not** contain: ad formats, geographic regions, revenue-share percentages,
developer eligibility tiers, or any SDK change. There is **no technical "expansion" to build for.**
The whole delta is an application form plus the mainnet-listing precondition.

---

## 2. What the current SDK docs require to serve ads

Pages read 2026-09-10: https://pi-apps.github.io/pi-sdk-docs/pi-sdk/Ads ,
https://pi-apps.github.io/pi-sdk-docs/platform/Ads ,
https://pi-apps.github.io/pi-sdk-docs/platform/PlatformAPI ,
https://pi-apps.github.io/pi-sdk-docs/getting-started/MainnetListing ,
https://raw.githubusercontent.com/pi-apps/pi-platform-docs/master/ads.md

**Eligibility / approval**
- *"Displaying ads is open to all applications in the Pi ecosystem, but only applications approved
  by Pi Core Team can be monetized."* (platform/Ads). So a Testnet app can render ads and be paid
  nothing.
- The docs' own "Developer Ad Network Application" section still reads **"Coming soon..."** - the
  live path is the portal button from the blog post.
- Mainnet listing requirements (getting-started/MainnetListing), verbatim gist: fully functional app
  with professional UI; **developer KYC complete**; no Pi trademark misuse and the domain must not
  start with "pi"; **Pi Authentication only** (no email or third-party logins); **all transactions in
  Pi**, no fiat or non-Pi tokens; no redirection to external sites.

**Feature probe** - the documented gate before any ad call:
```js
const nativeFeaturesList = await Pi.nativeFeaturesList();
const adNetworkSupported = nativeFeaturesList.includes("ad_network");
```

**Ad types**: `interstitial` (no auth, no verification, no `adId`), `rewarded` (**requires an
authenticated user**), and **banner** - banner is NOT an SDK call; it is enabled as "Loading Banner
Ads" in the Developer Portal settings.

**Client API** (`pi-sdk/Ads`), all promise-based:
- `Pi.Ads.isAdReady('interstitial' | 'rewarded')` -> per pi-sdk/Ads, resolves to `true` if an ad is
  available, otherwise `false`. (Our jslib comment at `PiBridge.jslib:312` describes a
  `{ ready: boolean }` object; the docs page read today states the boolean. Not reconciled.)
- `Pi.Ads.requestAd(type)` -> preloads; success result string `"AD_LOADED"`.
- `Pi.Ads.showAd(type)` -> resolves on completion; for rewarded the response carries `result` and
  `adId`. Result strings that appear literally in code examples in `pi-platform-docs/ads.md`
  (re-read today for exactly this): `AD_LOADED`, `AD_REWARDED`, `AD_CLOSED`, `ADS_NOT_SUPPORTED`.
  No TypeScript return-type declarations are published on either page.
- Docs state Pi SDK auth and payment features require running inside Pi Browser.

**The security rule, verbatim**: *"Since users might be running a hacked version of the SDK and
intercept your displayAd('rewarded') method, you must verify the rewarded status of the ad using Pi
Platform API, before rewarding users."* And: *"You should give rewards to your users only if
`mediator_ack_status` for given ad is `granted`."*

**Server endpoint** (platform/PlatformAPI, "Verify a rewarded ad status"):
```
GET https://api.minepi.com/v2/ads_network/status/:adId
Authorization: Key <your Server API Key>
```
Response `RewardedAdStatusDTO`: `identifier` (string, the adId), `mediator_ack_status`
(`"granted"` | `"revoked"` | `"failed"` | `null`), `mediator_granted_at` (ISO 8601 | null),
`mediator_revoked_at` (ISO 8601 | null).

Note: one docs page renders the path as `ads_network_status/{adId}` and the Platform API page as
`/ads_network/status/:adId`. The Platform API page is the endpoint reference and the repo uses that
form; the other is treated as a typo in the guide page.

---

## 3. Gap table

| # | Requirement (source read today) | What the repo has | Verdict |
|---|---|---|---|
| 1 | Probe `nativeFeaturesList()` for `ad_network` before serving | `PiAdProvider.cs:200-210` gate 3 refuses unless the token is present; jslib `PiBridge.jslib:373-398` calls `window.Pi.nativeFeaturesList()` | READY - call shape matches the doc sample exactly |
| 2 | Rewarded requires an authenticated user | `PiAdProvider.cs:212-219` gate 4 waits for `PiSignInController.IsSignedIn` and refuses otherwise | READY |
| 3 | `isAdReady` / `requestAd` / `showAd` bound to the SDK | `WebGLPiPlatform.cs:28-31, 122-152`; jslib `:283, :315, :340` | READY - all three bound, `requestAd` treated as an optimisation (`PiAdProvider.cs:293-307`) |
| 4 | Server-side verify before granting | `api/pi/ads-verify.js:75` calls `GET /ads_network/status/<adId>`; `api/_lib/pi-payments.js:91` `PI_API_ROOT = https://api.minepi.com/v2`; header `Authorization: Key ...` at `pi-payments.js:36-37` | READY - path, root and auth header all match the doc |
| 5 | Grant only on `mediator_ack_status === "granted"` | `ads-verify.js:42, 91-93`; null ack retried 3x1s then refused (`:53-54, :102-108`) | READY - fails closed on every other shape |
| 6 | Client never self-grants | `PiAdProvider.cs:386-399` verifies before `PiAdGrantDecision.Decide`; `PiAdVerifyEndpoint.cs:18-22` returns granted:false on every failure path | READY |
| 7 | API key server-only | `ads-verify.js:28-32`, `PiAdVerifyEndpoint.cs:14-16` - no key on the client | READY |
| 8 | The `PI_NETWORK_API_KEY` in Vercel is the key of the app that will serve the ads | The app was re-registered as a NEW Pi app today with a NEW API key; `ads-verify.js:148-154` refuses everything when `configured()` is false, and would refuse on an upstream 4xx with a stale key. The env var's current value was NOT inspected | UNPROVEN - `vercel env ls` showing `PI_NETWORK_API_KEY` updated after today's re-registration, or the lead confirming it, settles it |
| 9 | Verify endpoint reachable from the Pi-hosted origin | `PiAdVerifyEndpoint.cs:61-62` hardcodes `https://defenders-of-the-realm-v2.vercel.app/api/pi/ads-verify`; CORS `*` at `ads-verify.js:122-124`. But today's Pi app URL is `https://defenders-pi.vercel.app` | UNPROVEN - proves out with one `curl -X POST https://defenders-of-the-realm-v2.vercel.app/api/pi/ads-verify -d '{"adId":"x"}' -H 'content-type: application/json'` from the shipped build's network, expecting `{"success":true,"granted":false,...}` |
| 10 | App listed in the Mainnet Ecosystem Interface | `PiEnvironment.cs:45-46` `Sandbox = true` - "The app is registered on Testnet (WO-1325)". Portal badge Testnet per `PI_AD_NETWORK_APPROVAL.md` | GAP - the docs make listing the selection criterion, and say only approved apps monetize. Whether a Testnet-registered app even receives `ad_network` in `nativeFeaturesList` is precisely what today's timed-out probe failed to answer |
| 11 | Developer KYC complete | Nothing in the repo records this | UNPROVEN - owner checks the Pi Developer Portal profile |
| 12 | Pi Authentication only, no third-party logins | The repo also ships Firebase email auth (per project memory, not re-read at source today) | UNPROVEN - proves out by confirming the WebGL/Pi build path never offers an email login; needs a build inspection, not a doc |
| 13 | All transactions in Pi in the Pi build | Pi payment rail exists (`api/_lib/pi-payments.js`); a USD store also exists | UNPROVEN - needs the Pi build's store surface checked against the listing rule |
| 14 | Interstitial ads | `PiAdProvider.cs:82` - *"The only ad type this provider presents. Interstitials are deliberately absent."* | GAP by choice - a deliberate scope decision, not a defect. Cheap to add: jslib already accepts the type string |
| 15 | Banner ads ("Loading Banner Ads") | No code; it is a portal toggle, not an SDK call | GAP - portal-side only, zero code |
| 16 | Ads Checklist (3 steps) submitted via "Dev Ad Network" | Not started per `PI_AD_NETWORK_APPROVAL.md` | GAP - owner action in the portal |
| 17 | `showAd` cannot hang the reward button | jslib guard `:43-61`; `ShowAdTimeoutMs = 180000`, `ProbeTimeoutMs = 15000` (`WebGLPiPlatform.cs:53-54`) | READY |

**Counts: READY 8, GAP 4, UNPROVEN 5** across 17 rows. (Row 14 is a deliberate scope GAP, not a
defect.)

---

## 4. The nativeFeatures timeout seen today

Captured today on the Seeker in Pi Browser 1.17.1:
`[Flow:Pi] Pi error: local timeout after 15000ms - the Pi SDK never settled nativeFeatures
(id=, where=nativeFeatures-timeout)`.

- **15000ms is ours**, not Pi's: `WebGLPiPlatform.cs:54` `ProbeTimeoutMs = 15_000`, passed to
  `PiNativeFeatures(ProbeTimeoutMs)` at `:152`, and the message text is jslib `:55-61`.
- **Do the SDK docs describe `nativeFeaturesList` as able to hang?** No. `pi-sdk/Utilities` (read
  today) says only that utility methods *"return Promises that resolve with their respective results
  or reject if the feature is unavailable or fails"*. It documents no hang, no timeout, no minimum
  Pi Browser version, and no list of feature strings. The hang is undocumented behaviour.
- **Is our call shape still correct?** Yes. The current docs' own sample is
  `await Pi.nativeFeaturesList()`; jslib `:378, :382` checks `typeof window.Pi.nativeFeaturesList
  === 'function'` and calls `Promise.resolve(window.Pi.nativeFeaturesList())`. Identical. The
  timeout is not a wrong-API symptom.
- **What our jslib does on timeout**: the guard settles once with
  `{type:'error', where:'nativeFeatures-timeout'}` and drops any later resolve/reject
  (`PiBridge.jslib:43-61`). C# then receives an empty feature list, and `PiAdProvider.cs:202-210`
  treats an empty list as a "no" and refuses to register - *"we never register on a probe we could
  not complete."* The empty list is not a comment's promise: `WebGLPiPlatform.cs:341-346` matches
  `where.StartsWith("nativeFeatures")` on the error callback and calls
  `_featuresTcs.TrySetResult(Array.Empty<string>())`, read at source today. So the timeout is
  contained: no ads offered, no hang, one trace line. It is
  correct behaviour, and it is also exactly what an unapproved app looks like, which is why the
  refusal message says so.
- UNPROVEN: whether the hang is caused by Pi Browser 1.17.1, by the app not being ad-approved, or by
  `Pi.init` not having completed first. What would prove it: run the same build in Pi Browser with
  the JS console attached and call `window.Pi.nativeFeaturesList()` by hand after a confirmed
  `Pi.init`, and time it.

---

## 5. Checklist

**Owner, in the Pi Developer Portal (no code):**
1. Confirm developer KYC is complete on the Pi account.
2. Finish the Mainnet Ecosystem Interface listing for the newly registered app - this is the gating
   item for every ad row above.
3. Complete the portal's last pre-mainnet step: a successful testnet purchase in the app.
4. On the app detail page, scroll to the bottom and tap **"Dev Ad Network"**.
5. Open **"Ads Checklist"** and complete all three steps.
6. Decide whether to enable the portal's **Loading Banner Ads** toggle (no code needed).
7. Screenshot the checklist state afterwards so the next session does not re-derive it.

**Code / infra side:**
1. Confirm, and if needed set, `PI_NETWORK_API_KEY` in Vercel to **today's new app key**, in
   Production, Preview and Development - a stale key makes every reward refuse with an upstream 4xx,
   an absent one with `PI_NOT_CONFIGURED`.
2. Prove the verify endpoint answers from the shipped origin (row 9's curl), and record the body.
3. Decide whether `PiAdVerifyEndpoint.BackendBase` should follow the `defenders-pi.vercel.app` app
   URL; today it is hardcoded to `defenders-of-the-realm-v2.vercel.app`.
4. Re-run the Pi Browser session and read whether `nativeFeatures` still times out once the app is
   ad-approved - that single trace line is the readiness signal for the whole feature.
5. Optional, only if the owner wants it: add `interstitial` behind the same seam (jslib and the
   platform layer already accept the type string; `PiAdProvider` deliberately does not offer it).

Nothing above requires an SDK upgrade. There is no new Pi ads API to adopt.

---

## 6. Sources

- https://minepi.com/blog/ad-network-expansion/ (dated 2025-04-10; fetched 2026-09-10)
- https://pi-apps.github.io/pi-sdk-docs/
- https://pi-apps.github.io/pi-sdk-docs/pi-sdk/Ads
- https://pi-apps.github.io/pi-sdk-docs/pi-sdk/Utilities
- https://pi-apps.github.io/pi-sdk-docs/platform/Ads
- https://pi-apps.github.io/pi-sdk-docs/platform/PlatformAPI
- https://pi-apps.github.io/pi-sdk-docs/getting-started/MainnetListing
- https://raw.githubusercontent.com/pi-apps/pi-platform-docs/master/ads.md
- Repo, opened 2026-09-10: `Assets/_Modules/Village/Monetization/Providers/Pi/PiAdProvider.cs`,
  `.../PiAdVerifyEndpoint.cs`, `Assets/Plugins/WebGL/PiBridge.jslib`,
  `Assets/_Modules/Core/Platform/WebGLPiPlatform.cs`, `.../PiEnvironment.cs`, `api/pi/ads-verify.js`,
  `api/_lib/pi-payments.js`, `docs/reference/PI_AD_NETWORK_APPROVAL.md`,
  `WorkOrders/WORK_ORDER_1320_pi_ads_rewarded_behind_the_existing_seam.md` (CLOSED 2026-09-04),
  `WorkOrders/WORK_ORDER_912_ad_revenue_free_path.md` (DONE 2026-08-21),
  `WorkOrders/WORK_ORDER_pi_browser_integration_DEEP.md` (PARKED - future work, owner ruling
  2026-08-21). The other grep hits (WO-152, WO-203, WO-1621) are false positives on the search
  pattern and carry no Pi-ads content.
