# Solana dApp Store update checklist — Echoes of Elarion

**Authority date:** 2026-08-22. **Prefilled 2026-09-06** (docs lane — no Unity, no build,
no git writes) against declared stamp `2026.09.07.358574`; see the *Prefill 2026-09-06*
block above Gate A, and **Gate G** for the Google Play lane. Owner-only items are marked
`OWNER:`; items waiting on a binary read `BUILD PENDING`.

**Release posture:** update to the existing live dApp Store app

**Scope:** optional Unity LevelPlay rewarded ads and wallet-approved SKR packs

This is the authoritative operational checklist. `publishing/config.yaml` is the
portal copy source; `publishing/SUBMISSION_READY_2026-08-22.md` holds the expanded
evidence record and reviewer copy.

## Stop rules

- Do **not** create a new publisher, app, Android package, App NFT, or signing key.
- Do **not** enter the new-app/KYC flow. Use the existing publisher account and
  select the existing live app, then **New Version**.
- Do **not** submit an APK unless its Android signing certificate matches the live
  release and both `versionName` and `versionCode` have increased.
- Do **not** publish stale “no ads,” “no advertising SDK,” “wallet identity only,”
  or “no purchases” claims. This update contains LevelPlay rewarded ads and SKR
  purchases.
- Do **not** expose Devnet mint details, test wallets, portal API keys, signer
  keypairs, keystores, passwords, or private keys in listing copy or commits.
- Do **not** submit a local-test, Development, LevelPlay Test Suite, or Devnet APK.
- Do **not** flip the compile-time wallet network to Mainnet or enable the production
  purchase flag without the explicit owner rulings recorded in
  `publishing/SUBMISSION_READY_2026-08-22.md`.
- Do **not** use an APK merely because it exists at the configured path. Bind the
  submission to its recorded hash, version, signing certificate, commit, and device
  evidence.

## Preserved live identity — complete

- [x] Existing App NFT preserved:
      `5MG4atMRDSVn9t75oFz1KVxKdUkyz2wPi2MeunT8yFe6`.
- [x] Existing package preserved: `com.denellestudios.echoesofelarion`.
- [x] Existing publisher preserved: DeNelle Studios.
- [x] Publisher website preserved: https://echoes-of-elarion.vercel.app/
- [x] Support contact preserved: support.EoA@icloud.com
- [x] Existing live release observed on-device: `2026.08.17.328845`.
- [x] This release is documented as an update, not a first submission.

## Legal and disclosure preparation — complete

- [x] Privacy Policy updated for optional rewarded ads, LevelPlay mediation,
      advertising/device data, consent timing, and mediated partners.
- [x] Terms updated for optional rewarded ads and wallet-approved SKR purchases.
- [x] Privacy deployed 2026-08-22:
      https://echoes-of-elarion.vercel.app/privacy — HTTP 200 verified.
- [x] Terms deployed 2026-08-22:
      https://echoes-of-elarion.vercel.app/terms — HTTP 200 verified.
- [x] Portal declarations prepared: **Contains Ads = Yes** and
      **In-App/On-chain Purchases = Yes**.
- [x] Public copy states rewarded ads are optional and user initiated.
- [x] Public copy states SKR pack contents are non-transferable, have no cash-out,
      and are not required to complete the game.
- [x] Reviewer instructions support guest play without a wallet, ad, or purchase.
- [x] Copyright/licence URL resolved to the live Terms URL.

## Monetization infrastructure — complete

- [x] Devnet test SKR mint created with 9 decimals:
      `3BwWSAUZmyngXDSZiCawEnP7iLgY5ANNopBDz94AB77N`.
- [x] Correct Seeker test wallet funded with 100 valueless Devnet SKR:
      `CHKKFkPGz8VZfjpsZjJTqfAUW7vMpdNkkqCVuCcZsfkC`.
- [x] Purchase API routes `/verify`, `/reconcile`, and `/fulfill` are production-deployed
      with the Devnet SKR configuration used for today's canary, and respond with
      controlled validation errors rather than 404/500. Mainnet configuration remains
      a separate final-release gate.
- [x] Backend SKR verifier tests passed: 10/10.
- [x] Backend verifies finalized chain data, signer, server-owned recipient, exact
      mint, decimals, and exact `amountBaseUnits`.
- [x] Mainnet production authority recorded internally: official SKR mint
      `SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3`, decimals `6` (CORRECTED 2026-08-22 - read off the chain; the 9 came from OUR Devnet TEST mint and is wrong for the real token. 1 SKR = 1_000_000 base units).
- [x] Daily Chest physical-device happy path previously proved a completed ad
      granted the displayed 1,000 Gold exactly once.
- [x] Current monetization integration regression passed 264/264 with zero skipped
      (`Builds/reg-f7.log`), and compile gate passed (`Builds/gate-f7.log`) at commit
      `7678cde626538000d5cc940375ce0f04f16ded83`.
- [x] Canary APK version `2026.08.22.336813` / code `336813` exceeded the observed
      live version. This proves the integration build, not the clean submission APK.
- [x] Canary hosted-content run reported `R2_PUSH_OK` and `R2_PARITY_OK 42 object(s)`.
      The clean submission build still needs its own push/parity proof because bundle
      names are content hashed.

The Devnet mint and wallet above are internal test evidence, not store-listing copy.
Devnet infrastructure readiness does not discharge the final APK/device matrix.

## Prefill 2026-09-06 (docs lane) — measured state at HEAD

Filled in without Unity, without a build, without git writes. Every value below was
read at source this session. Nothing here supersedes the Gate A staleness banner.

- **Declared build stamp:** `bundleVersion: 2026.09.07.358574` /
  `AndroidBundleVersionCode: 358574` — `ProjectSettings/ProjectSettings.asset:148,177`.
  Both exceed the live `2026.08.17.328845` / `328845`.
- **Package (unchanged):** `com.denellestudios.echoesofelarion`.
- **Release notes for this build:** `publishing/RELEASE_NOTES_2026-09-07.md`
  (rewritten 2026-09-07 for the store APK `2026.09.07.359419`, awaiting owner approval; its
  "Build identity" block IS the measured Gate A record for that APK - SHA-256, badging,
  signer, define absence, R2 parity - read it there rather than from the STALE block below).
- **R2 hosted content — GREEN and it postdates the stamp.**
  `Builds/r2-parity.log` (2026-09-06 20:24, UTF-16) ends
  `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=271`, preceded by
  `R2_PARITY_TARGET_OK 92 object(s) verified` and
  `R2_PRUNE_TARGET Android newest=catalog_2026.09.07.358574` — the catalog it verified
  is this build's. `Builds/r2-push.log` (19:20) reads
  `R2_PUSH_OK 0 uploaded (0.0 MB), 814 unchanged`. Sanctioned path: `tools\r2-ship.ps1`.
- **Last GREEN compile gate:** `Builds/cg-quiet.log` (20:04) —
  `COMPILE_GATE_OK :: scripts compiled clean`.
- **Last GREEN regression:** `Builds/reg-final2.log` (18:50) —
  `REGRESSION_OK 414/414 suites`.
- ⛔ **THE TREE IS RED AS OF 20:54 AND NO SUBMISSION BUILD EXISTS.** Two fresher runs
  went red after commits landed at 20:12–20:49:
  - `Builds/reg-quiet.log` (20:07) — `REGRESSION_FAIL: 2 failure(s)`
    (417/419 green): a UI-MVVM conformance violation in
    `Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs:252,253`, and a hollow-pass
    marker fail at `NightMarketNoWalletRegression.cs:761`. ⚠ The wrapper printed
    `VERDICT=PASS-UNASSERTED` for that same run because no `-ExpectMarker` was passed —
    judge by the marker in the log, never the wrapper verdict.
  - `Builds/cg-aab.log` (20:54) — `compileErrors=True`; `Builds/aab-build.console.log`
    reads `COMPILE_RED`. Three errors, all in Editor regression files:
    `ManageProgressiveDisclosureRegression.cs(228,41) CS0103 'ObsidianQueueState'`,
    `ManageTroopsTrainDoorRegression.cs(247,17) CS0103 'CheckTroopDetailStats'`,
    `ManageTroopsTrainDoorRegression.cs(286,17) CS0103 'CheckArmyFullIsSaidAndDoesNotTravel'`.
- **Therefore every APK/AAB identity field is `BUILD PENDING`, not `OWNER:`.** The newest
  APK log is `Builds/apk-build.log` (19:19) and the newest AAB record is
  `Builds/aab-status.txt` (2026-09-04, `AAB_SIZE_OK`, 450.7 MiB on disk) — both predate the
  358574 stamp, so neither is the submission candidate. Re-record Gate A in one pass
  against whichever binary actually ships.

## Gate A — final build identity and provenance

> **RE-RECORDED 2026-09-07 09:27 against the store APK `2026.09.07.359419`** (source `05de2d23a`, no
> `TESTER_BUILD`): every identity value is in `publishing/RELEASE_NOTES_2026-09-07.md` -> "Build
> identity", measured from the artifact in one pass. The banner and values below are the 09-03 record,
> kept for history. Still open there and still open now: the live-release certificate match (never
> captured; the in-place update over the live store build is the proof), and the Gate A "pushed" item
> (nothing is pushed until the owner says so).

> ## STALE 2026-09-03 - THE IDENTITY FIELDS BELOW DESCRIBE THE WRONG APK. DO NOT SUBMIT AGAINST THEM.
>
> Gate A was filled in on 2026-09-03 (commit `f1104a5fd`) against APK **`2026.09.04.354266`**.
> **The build that shipped and installed on the device is `2026.09.04.354315`**
> (`ProjectSettings/ProjectSettings.asset:148,177`; the accompanying `Builds/r2-push.log` names
> `Android/catalog_2026.09.04.354315.bin`). The bundle was bumped `354266 -> 354315` in commit
> `0a15744c9` for the final build of the night.
>
> **Therefore STALE, every one of them:** the final APK SHA-256, `versionName`, `versionCode`, the
> source commit, the APK path and its recorded size, and the `aapt2 dump badging` line - each one
> names or hashes a file that is not the submission candidate.
>
> ⛔ **They were NOT re-derived here, deliberately.** The lead re-records this whole block against
> whichever APK actually ships, in one pass, at the moment of submission. Re-deriving them now would
> produce a third set of numbers with no build behind it.
>
> ⚠ **This is the copied-state trap CLAUDE.md §11B exists for, and it is left VISIBLE rather than
> quietly patched.** The values were true and measured when written; the build moved on forty minutes
> later. A recorded identity is only ever true of one file.
>
> **Still valid below, because they are not APK-specific:** the `apksigner` DN, the JAVA_HOME note,
> and the unticked certificate-match item at the end of this gate (see the note in that item - the
> cheap close is an in-place update over the live store build).

- [x] CLI regression is green. Record log: `Builds/regression.log` - `REGRESSION_OK 358/358 suites, 0 red, 0 skipped` (2026-09-03 19:23).
- [x] Final production APK built from commit: `32c9630f5` (branch feat/synty-art-retheme, pushed).
- [x] Final APK path: `Builds/Android/DefendersOfTheRealm.apk` (459.3 MB, built 2026-09-03 19:31).
- [x] Final APK SHA-256: `8bd67ff3108d349d81e0bddfa07f425fa3bd010924d1b3a28bcbf8cc22950f1b`.
- [x] Final `versionName`: `2026.09.04.354266`.
- [x] Final `versionCode`: `354266`.
- [x] Both version values exceed the live release values (live observed `2026.08.17.328845` / `328845`; this build `2026.09.04.354266` / `354266`).
- [x] `apksigner verify --print-certs` passes. Signer #1 DN `CN=DeNelle Studios, OU=Games, O=DeNelle Studios, L=NA, ST=NA, C=US`. (JAVA_HOME must be set to the Unity OpenJDK at `<UnityEditor>/Data/PlaybackEngines/AndroidPlayer/OpenJDK` - apksigner fails without it.)
- [x] Signing certificate SHA-256: `733666ce4ce2c872ab6530eb28d6dbf1e19de26d88ed59d1b5c0209c3da62443`.
- [ ] `OWNER:` ⛔ Certificate matches the existing live dApp Store release - **CANNOT BE PROVEN FROM THIS REPO, and that is a gap in the record, not a pass.** The live release's certificate SHA-256 was never captured (this file still reads `PENDING` for it at the evidence table), so there is nothing to compare against. What IS true: this APK is signed by `dotr-release.keystore`, the keystore configured in `ProjectSettings.asset` (`androidUseCustomKeystore: 1`, alias `dotr`), which is the key this project has always used. The one cheap way to CLOSE it rather than assume it: install this APK over the LIVE store build on a device - Android refuses an update signed by a different key, so a successful in-place update IS the proof. Record the live value here once observed so this is never PENDING again.
- [x] `aapt2 dump badging` confirms the preserved package ID and declared version: `package: name='com.denellestudios.echoesofelarion' versionCode='354266' versionName='2026.09.04.354266'`, `application-label:'Echoes of Elarion'`, `minSdkVersion:'26'`, `targetSdkVersion:'36'`.
- [ ] APK is ARM64/IL2CPP and uses the intended production Android configuration.
- [ ] No local-test defines, Development Build, Devnet endpoints/mint, mock provider,
      or LevelPlay Test Suite activation is present.
- [ ] Production client and backend both use the official Mainnet SKR mint and
      9 decimals; neither can fall through to Devnet.

## Gate B — final APK rewarded-ad device matrix

`OWNER:` **Every item in this gate is a physical-device felt test.** No part of it can be
prefilled or proven headless. Run them against the exact hash recorded in Gate A.

- [ ] Consent choice is resolved before LevelPlay initialization.
- [ ] Daily Chest completion grants the displayed reward exactly once.
- [ ] Daily Chest dismiss grants nothing.
- [ ] Daily Chest no-fill/load/display failure grants nothing and does not block play.
- [ ] Harvest completion grants the displayed boost exactly once.
- [ ] Harvest dismiss grants nothing.
- [ ] Harvest no-fill/load/display failure grants nothing and does not block play.
- [ ] Rapid taps cannot open duplicate presentations or duplicate rewards.
- [ ] Background/foreground during an ad cannot duplicate a reward.
- [ ] Restart after a completed ad does not repeat its reward.
- [ ] Enabled placement IDs match the production LevelPlay dashboard.
- [ ] No banner or forced interstitial appears anywhere in ordinary play.
- [ ] Attach device log/screenshot/video evidence: `________________`.

## Gate C — final APK SKR purchase device matrix

`OWNER:` **Every item in this gate needs a real wallet approving a real transaction.**
Complete a bounded canary before the production flip, then repeat the critical
recipient/mint/amount assertions on the production-configured build.

- [ ] `hearth-spark` shows the ruled SKR price and contents.
- [ ] Confirmation UI shows pack, token, exact amount, network, and recipient.
- [ ] Wallet approval produces the expected SPL `transferChecked` transaction.
- [ ] Backend verifies the transaction and creates exactly one entitlement.
- [ ] Client grants the contents exactly once.
- [ ] `/fulfill` advances the durable record to `fulfilled`.
- [ ] Restart/reconcile restores the entitlement without another grant.
- [ ] Replaying the signature cannot duplicate the entitlement.
- [ ] Wallet cancellation grants nothing.
- [ ] Insufficient balance grants nothing.
- [ ] Timeout/network loss grants nothing and leaves a recoverable pending state.
- [ ] Wrong mint, amount, signer, recipient, or network is rejected.
- [ ] Production-configured build identifies official Mainnet SKR mint
      `SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3` with 6 decimals (CORRECTED 2026-08-22 - the 9 was our Devnet test mint's value).
- [ ] Attach transaction, database row, device log, and restart evidence:
      `________________`.

## Gate D — listing assets and reviewer packet

- [ ] Portal field **Contains Ads** set to **Yes**.
- [ ] Portal field **In-App Purchases/Transactions** set to **Yes**.
- [ ] Privacy URL set to https://echoes-of-elarion.vercel.app/privacy.
- [ ] Terms/licence URL set to https://echoes-of-elarion.vercel.app/terms.
- [ ] `OWNER:` What's New approved. ⚠ **The block quoted below is STALE** — it predates
      three weeks of work and describes almost none of it. The drafted replacement,
      grouped as Fixed / New / Balance with a per-claim commit table, is
      `publishing/RELEASE_NOTES_2026-09-07.md` (2026-09-06). Paste the approved wording
      here and delete the stale quote:

      > Expanded dungeon adventures, a redesigned inventory and skill experience,
      > optional rewarded bonuses, and wallet-approved SKR packs.

- [ ] Reviewer instructions pasted from
      `publishing/SUBMISSION_READY_2026-08-22.md`.
- [ ] `OWNER:` Four clean landscape screenshots captured from the exact final APK
      (1920x1080 recommended; the floor is >=1080 px on BOTH axes —
      `publishing/media/README.md`). None exist yet: `publishing/media/` is empty of
      every required file.
  - [ ] Village rebuilding/defence
  - [ ] Dungeon exploration/combat
  - [ ] Optional rewarded-ad offer before playback
  - [ ] SKR pack confirmation before wallet handoff
- [ ] Screenshots contain no debug overlays, test wallets, transaction signatures,
      Devnet labels, personal information, or placeholder content.
- [ ] `OWNER:` Confirm the existing portal icon and banner remain suitable for this
      update. If either is replaced, the replacement meets `publishing/media/README.md`
      (icon exactly 512x512, banner exactly 1200x600). Verified 2026-09-06:
      `publishing/media/` contains only `README.md` — no `icon-512.png`, no
      `banner-1200x600.png`, no screenshots. Replacements would have to be authored.
- [ ] Listing description matches the monetized build and contains no stale claims.

## Gate E — submit the update

`OWNER:` **Every item in this gate is the owner's, start to finish** — it needs the
publisher account, the publisher wallet, and her approval of on-chain messages. No seat
can prefill or execute any of it.

- [ ] Sign into https://publish.solanamobile.com with the existing publisher account
      and existing publisher wallet.
- [ ] Select the existing Echoes of Elarion app.
- [ ] Choose **New Version**. Do not choose Add a dApp/New dApp.
- [ ] Upload the exact APK hash proven in Gate A.
- [ ] Confirm the portal matched package `com.denellestudios.echoesofelarion`.
- [ ] Confirm version increment and existing signing identity before approval.
- [ ] Review listing disclosures, legal URLs, What’s New, reviewer instructions,
      and media one final time.
- [ ] Submit through the portal or current portal-backed CLI using secrets outside
      the repository.
- [ ] Approve every required publisher-wallet message/transaction.
- [ ] Record the new release ID/Release NFT: `________________`.
- [ ] Record submission timestamp: `________________`.
- [ ] Record submitted APK hash/version/commit in the post-submission record.

## Gate F — review follow-up

- [ ] Confirm the update entered the review queue.
- [ ] Watch the developer email for `publishersupport@dappstore.solanamobile.com`.
- [ ] Record review result and any requested changes.
- [ ] Official guidance currently states 3–5 business days. After five business
      days without a response, use Solana Mobile Discord’s Developer role and
      `#dev-answers` support path.
- [ ] After approval, install/update from the dApp Store on Seeker and rerun the
      purchase/ad smoke tests against the store-delivered binary.

## Gate G — Google Play closed testing (added 2026-09-06)

This checklist was written for the dApp Store only. The owner's second lane tonight is
the newest build into Google Play testing, so it gets its own gate rather than being
smuggled into Gate A.

**Prefilled (no owner decision needed):**

- Build script: `google-play-aab-build.ps1` (repo root — gate scripts live at the root,
  not under `tools\`). Same `bundleVersion` / `AndroidBundleVersionCode` source as the
  APK: `ProjectSettings/ProjectSettings.asset:148,177` → `2026.09.07.358574` / `358574`.
- AAB output path: `Builds/Android/EchoesOfElarion-GooglePlay.aab`
  (named in `Builds/aab-status.txt`).
- Play's hard ceiling is 500,000,000 bytes for an AAB. The last measured bundle passed:
  `AAB_SIZE_OK 469202267 (30797733 under 500000000)` — but that measurement is dated
  2026-09-04 and is **not** this build. Re-measure.
- R2 parity gate applies to this lane too: `Builds/r2-parity.log` is green and names
  `catalog_2026.09.07.358574` (see the prefill block above). `distribute-android` and two
  other chains now refuse a stale parity log.
- Blocked by the same compile-RED recorded above — `aab-build.console.log` reads
  `COMPILE_RED`, so no AAB was produced tonight.

**Owner-only:**

- [ ] `OWNER:` Play Console listing assets (icon, feature graphic, phone/tablet
      screenshots) — separate set from the dApp Store media; specs are Play's, not the
      ones in `publishing/media/README.md`.
- [ ] `OWNER:` Play "What's new" text approved from
      `publishing/RELEASE_NOTES_2026-09-07.md` (Play caps this field at 500 characters,
      so the three blocks likely need her pick of which leads).
- [ ] `OWNER:` Confirm the closed-test track, tester list, and that the 12-tester /
      14-day requirement is satisfied or waived.
- [ ] `OWNER:` Ruling on `publishing/config.yaml:174-177`. That comment block still
      declares the app is **not** on Google Play and deliberately omits
      `google_store_package`. Once a Play listing exists the omission becomes a false
      declaration on the dApp Store side. **Not edited here** — it is her call whether
      to add the package id now or after the Play listing goes live.
- [ ] `OWNER:` Play App Signing / upload key confirmation — same certificate question as
      the dApp Store item in Gate A, and equally unprovable from this repo.

## Final release record

| Evidence | Value |
|---|---|
| Git commit | `PENDING` |
| APK versionName | `PENDING` |
| APK versionCode | `PENDING` |
| APK SHA-256 | `PENDING` |
| Signing certificate SHA-256 | `733666ce4ce2c872ab6530eb28d6dbf1e19de26d88ed59d1b5c0209c3da62443` (THIS build; the LIVE value remains unrecorded) |
| Ad device evidence | `PENDING` |
| SKR transaction signature | `PENDING` |
| Entitlement/fulfillment evidence | `PENDING` |
| New release ID/Release NFT | `PENDING` |
| Submitted at | `PENDING` |
| Review result | `PENDING` |

## Official sources

- Update an existing app:
  https://docs.solanamobile.com/dapp-store/submit-an-update
- Current portal-backed publishing CLI:
  https://docs.solanamobile.com/dapp-store/publishing-cli
- Subsequent-release version/signing requirements:
  https://docs.solanamobile.com/dapp-store/publishing_releases
- APK signing and verification:
  https://docs.solanamobile.com/dapp-store/build-and-sign-an-apk
- Publisher policy:
  https://docs.solanamobile.com/dapp-store/publisher-policy
- Support and review follow-up:
  https://docs.solanamobile.com/dapp-store/support

---

## Evidence record — dApp Store UPDATE candidate `2026.09.16.371627` (recorded 2026-09-15 21:2x by the CLI, per §11B "fill the record as you go")

- [x] Regression green on the built tree: `REGRESSION_OK 539/539 suites -- 539 green, 0 red, 0 skipped` (`Builds/data-regression.log`, 2026-09-15 21:01).
- [x] Built from the tree at commit `f74bf0829` (dev; carries the WO-1760 Arcane Spire re-point; NOT pushed). Chain: `overnight-apk-build.ps1` STORE-shaped, defines `''` (no `TESTER_BUILD`, `grep -c TESTER_BUILD Builds/apk-build.log` = 0).
- [x] APK path: `Builds/Android/DefendersOfTheRealm.apk` (466,293,778 bytes, built 2026-09-15 20:53).
- [x] APK SHA-256: `d23f0ac6596a3487dbf472cd0af2621dbd8dd66b5bc720f2ed2a099c070dc475`.
- [x] `versionName` `2026.09.16.371627` / `versionCode` `371627` (aapt2 dump badging). Strictly higher than the last recorded submission `354266` and than tonight's Play AAB `371610`.
- [x] `apksigner verify --print-certs`: Signer #1 DN `CN=DeNelle Studios, OU=Games, O=DeNelle Studios, L=NA, ST=NA, C=US`, certificate SHA-256 `733666ce4ce2c872ab6530eb28d6dbf1e19de26d88ed59d1b5c0209c3da62443` — IDENTICAL to the 354266 record above (same `dotr-release.keystore`).
- [x] Remote content: `R2_PUSH_OK 2 uploaded (0.1 MB), 1123 unchanged` + `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=204` (`Builds/apk-chain-wrapper.out.log`, 20:54); catalog `catalog_2026.09.16.371627`.
- [ ] `OWNER:` certificate matches the LIVE store release — still the same unprovable-from-here gap as above; the cheap close is unchanged: install this APK OVER the store build on the Seeker; an in-place update IS the proof. Record the observed live cert here when done.
- [ ] `OWNER:` felt-test on device (WO-1760 spire; the only content change vs 371436-era builds).
Release record: `docs/releases/GOOGLE_PLAY_2026.09.16.371610.md` (covers both artifacts).
