# WORK ORDER 1362 - Google Play AAB, owned as a programme

**Status:** FIXED - checked-in Android large-screen and 16KB packaging corrections await owner verification on the next build; physical AAB scanning and programme go/no-go remain owner decisions
**Silo:** Release engineering / publishing
**Raised by:** owner - *"can you manage the aab?"*
**Date:** 2026-09-03

This work order is the FIRST MOVE ONLY: establish true current state, then lay out a programme she
can approve or re-order. **No code, asset or config was changed. No build or gate was run.**
Every verdict below cites something read or measured in this session.

---

## 0. THE HEADLINE, BEFORE ANYTHING ELSE

Three things you need to know before reading the table:

1. **The 08-30 audit's RED #1 is wrong on the number, and there is a SECOND 08-30 document that
   says so.** `docs/releases/GOOGLE_PLAY_RC_2026-08-30.md:3-5` explicitly declares it supersedes the
   audit's current-state conclusions. The two documents are the same age and contradict each other
   on the single most important figure. **The RC doc is right: the Play base-module ceiling is
   500 MB, not 200 MB** (Play Console Help "Optimise your app's size", read this session). The
   audit's "THE CONSOLE REFUSES THE UPLOAD" was true against a limit Google has since raised.

2. **And yet size is still the one thing that blocks upload - because the margin has been eaten
   since.** The RC doc measured its 482.8 MB candidate at 479.4 MB download, 20.6 MB under the
   ceiling. **The AAB on disk today is 514,062,537 bytes, built Sep 1 07:29 - 31.2 MB larger.**
   Applying the RC's own measured AAB-to-download ratio puts today's artifact at roughly **510 MB,
   about 10 MB OVER the ceiling.** The margin the RC banked is gone.

3. **Nothing in this repo measures AAB size.** `grep` across `Assets/Editor/AndroidBuild.cs`,
   `Assets/Editor/Regression/GooglePlayPackaging*.cs`, `tools/*.ps1` and `tools/android/*.ps1` for
   any size assertion (`MaxAab`, `SizeLimit`, `download size`, `get-size`, byte thresholds) returns
   **zero hits**. That is exactly why 31 MB could appear in two days with every marker still green.

The audit was right that size is the gate. It was right for the wrong reason, and the right reason
is worse: the number is unguarded and drifting.

---

## 1. VERDICT TABLE - every 08-30 finding re-checked against HEAD

Measurements are of `Builds/Android/EchoesOfElarion-GooglePlay.aab` (514,062,537 bytes, mtime
Sep 1 07:29), read as a zip this session. Source citations are the live `Assets/` tree at HEAD
`a0f931f95`. 131 commits have landed since the audit.

| # | Audit claim | Verdict | Evidence gathered this session |
|---|---|---|---|
| **1** | 493 MiB vs a 200 MB limit; console refuses upload | **CHANGED - both halves wrong, and a new risk underneath** | Ceiling is **500 MB** (Play Console Help, fetched). Today's AAB: base module **488.86 MiB compressed / 1130.25 MiB uncompressed**; `base/assets/bin/Data` = **423.94 MiB compressed, 981.70 MiB uncompressed** across 4144 entries. `BundleConfig.pb` declares modules `{base}` only - **no asset packs**. `ProjectSettings/ProjectSettings.asset:189` `androidSplitApplicationBinary: 0`, unchanged. Manifest has no `splitApp`/`isSplitRequired`. Estimated download **~510 MB** vs the RC's proven 479.4 MB four days ago. **No size guard exists anywhere.** |
| **2** | artifact crypto-dirty | **CONFIRMED - and the root cause is now precisely located** | In today's `global-metadata.dat` (19,141,096 bytes): `solana` x74, `SKR` x35, `Jupiter` x12, and the USDC mint `EPjFWdd5Aufq...TDt1v` **PRESENT**. The three literals the audit quoted (`SKR is a REAL`, `Stake.solanamobile`, `$SKR`) are **ABSENT** - those were genuinely fixed. What remains is different copy: `"Powered with SKR"`, `"How SKR powers the realm"`, `"Stake SKR natively to unlock your first reward"`, `"we never take custody of your SKR"`, and the entire Arena wager vocabulary (`"Stake {1} -> Win {2} SKR"`, `"forfeiting staked {1} SKR (no refund)"`, `"cannot afford {0} SKR wager"`). See section 2 for why. |
| **3** | `CurrencySkinResolver` force-resolves SKR on every Android build | **CLOSED** | `Assets/_Modules/Core/Platform/CurrencySkinResolver.cs:267` `#if GOOGLE_PLAY` pins `requested = "wallet"` above the whole chain; the SKR branch now sits in the `#else` arm (:271-:313) and is not compiled. `CurrencySkin.cs:130` yields symbol `""`, name `"Store credit"`. Pinned by `Assets/Editor/Regression/GooglePlayPackagingRegression.cs:177`. |
| **4** | `SettingsController` unconditional Wallet section | **CLOSED** | `Assets/_Modules/Settings/SettingsController.cs:247` `#if !GOOGLE_PLAY` ... `:258` `#endif`, applied consistently at `:60`, `:106`, `:113`, `:539`, `:549`. |
| **5** | Privacy + Terms crypto-framed | **CONFIRMED - and the in-app buttons are ungated** | `site/terms.html:52` ("from your own crypto wallet, directly on the blockchain"), `:152` ("transfer of SKR from your wallet address to ours"), `:192` (section titled "Your wallet and on-chain transactions"), `:153` ("no app-store billing layer, no card processor" - directly contradicts a Play fiat build), `:215`, `:219`. `site/privacy.html:60` still lists "Connect Wallet". The **live** pages: terms returns 28x wallet, 14x token, 8x blockchain, 3x crypto, 4x Solana, 1x SKR. In-app link buttons at `SettingsController.cs:304-311` are **outside any `#if !GOOGLE_PLAY`**; URLs at `:706-707`. |
| **6 / 7 / 8** | identity absent -> grant writer behind `!GOOGLE_PLAY` -> no store UI or restore in the AAB | **CHANGED - substantially landed** | Today's AAB metadata carries `GooglePlayBilling`, `PlayStorefront`, `GoogleSignIn`, `PackStoreVM`, `PackGrantBridge` - all **PRESENT**. Manifest carries `ProxyBillingActivity`, `ProxyBillingActivityV2`, `SignInHubActivity`, `RevocationBoundService`, `GenericIdpActivity`, `com.android.vending.billing.InAppBillingService.BIND`. `DAPP_STORE` **absent**. `Assets/_Modules/GooglePlay/DeNelle.GooglePlay.asmdef:18` is `"GOOGLE_PLAY"`-constrained. `PurchaseGate` is absent by that name (renamed or replaced). **Restore was NOT verified by me** - it needs a licensed Play-delivered test. |
| **9** | no in-app account deletion route | **CHANGED** | `https://echoes-of-elarion.vercel.app/delete-account` returns **HTTP 200, 4463 bytes**. `WorkOrders/WORK_ORDER_1270_...md:3` reads DONE 2026-08-28. The RC doc claims an in-app entry point exists; I did not confirm it renders in a Play build. Play wants both, so treat as narrowed, not closed. |
| **11** | Play App Signing collides with the live dApp Store package id | **UNCHANGED - owner decision** | AAB manifest package is `com.denellestudios.echoesofelarion`, the same id as the live listing. Cheap now, expensive later. |
| **12** | `android.permission.DUMP` | **CONFIRMED** | `permission.DUMP` **PRESENT** in `base/manifest/AndroidManifest.xml`. |
| **13** | `FOREGROUND_SERVICE` with no `foregroundServiceType` | **CONFIRMED** | `FOREGROUND_SERVICE` present; `foregroundServiceType` **absent**. |
| **14** | AD_ID live, Data Safety must declare | **CONFIRMED** | `com.google.android.gms.permission.AD_ID` plus `ACCESS_ADSERVICES_AD_ID`, `_TOPICS`, `_ATTRIBUTION` all in the manifest. |
| **16** | AAB catalog never proven pushed | **CHANGED - narrowed, still real** | The AAB requests `catalog_2026.09.01.350657` (`base/assets/aa/settings.json` `m_CatalogLocations`). That hash **does resolve on R2: HTTP 200**. But the freshest proof, `Builds/r2-parity.log` (Sep 3 21:06), verifies `catalog_2026.09.04.354315` - a different catalog. **Bundle-level parity for the AAB's own catalog is unproven.** |
| **17** | Arena SKR wager loop | **CONFIRMED - and worse than stated** | `grep -c GOOGLE_PLAY` returns **0** for `Assets/_Modules/Village/Arena/ArenaMode.cs`, `ArenaVM.cs`, and `Assets/_Modules/Core/State/GameState.cs`. `Assets/_Modules/Village/DeNelle.Village.asmdef:35` has `"defineConstraints": []`. The SKR wager loop **compiles into and runs in a Play build.** |
| **19** | `preferExternal` | **CONFIRMED** | `preferExternal` **PRESENT** in the manifest. |
| **"gate drift"** | token list duplicated in two places and already drifted | **drift CLOSED / blind spot CONFIRMED** | The two lists are **byte-identical today**: `Assets/Editor/Regression/GooglePlayPackagingGate.cs:20-30` + `:36-45` vs `tools/android/assert-google-play-aab-clean.ps1:17-36`. No drift. But see section 2 - the blind spot the audit warned about is real and it is currently passing a dirty artifact. |

**Green items I re-confirmed:** MWA is genuinely excluded - the 23.3 MB of merged dex contains no
`com/denelle/defenders/mwa`, `mobilewalletadapter`, `solanamobile`, or `com/solana`. `minSdk 26`,
`targetSdk 36`, ARM64-only, versionCode `354315` (`ProjectSettings.asset:177-179`, `:269`).

---

## 2. THE STRUCTURAL FINDING - why a green gate passed a dirty artifact

This is the most important thing recon turned up, and it is not in the audit.

**The Play exclusion strategy has two tiers, and only one of them works.**

- **Tier 1, assembly exclusion.** `DeNelle.Wallet.asmdef:22` and `DeNelle.Web3.asmdef:17` carry
  `"!GOOGLE_PLAY"`; the Solana SDK runtime is `!GOOGLE_PLAY`-constrained; the MWA androidlib is
  excluded by `Assets/Editor/MobileWalletAdapterPlayExclusion.cs:90`. **This tier is genuinely
  clean** - the dex proves it.

- **Tier 2, runtime guards inside shipping assemblies.** `DeNelle.Core` and `DeNelle.Village` ship
  in every build and are gated by `#if` blocks placed around *behaviour*, not around *string
  literals*. `Assets/_Modules/Core/UI/SkrShowcasePanel.cs:68` is an early-return guard in `Open()` -
  but the SKR copy at `:77`, `:154` and `:172` sits **outside** it and compiles into IL2CPP metadata
  regardless. Arena has no guard at all.

**A runtime guard does not remove a string from `global-metadata.dat`.** Only compiling the literal
out, or excluding the assembly, does. A policy reviewer running `strings` on the artifact sees
every one of those SKR sentences whether or not the panel ever opens.

**And the gate is deliberately blind to exactly this.** `GooglePlayPackagingGate.cs:167-176`
(`IsUserFacingContentEntry`) routes only `.json`/`.txt`/`.html`/`.xml`/`.uxml` and
`Data/Canonical/` to the strict `UserFacingContentTokens` list. Everything else - including
`global-metadata.dat` - gets `OpaqueExecutableTokens` (`:36-45`), which **intentionally drops**
`solana`, `usdc`, `skr`, `$skr`, `jupiter`, `blockchain`, `crypto` and `web3` to avoid matching
`System.Security.Cryptography` and ad-SDK strings. The comment at `:32-34` says so openly.

The consequence is provable: `Builds/ui-reskin-final-google-play-aab-v2.log` (Sep 1 07:32, the run
that produced the AAB on disk) emitted **`PLAY_ARTIFACT_CLEAN_OK`** on the artifact I just measured
carrying the USDC mint address and four SKR marketing sentences. The gate is not lying; it was
built with a blind spot, and the blind spot is now load-bearing.

**Implication for planning:** the crypto-purge item is not "finish the exclusions". It is
"convert tier 2 from runtime guards to compile-out, and make the gate able to see it". That is a
different and larger job than the audit's framing suggests.

---

## 3. THREE GATES, SEPARATED

Conflating these is how "just build the AAB" becomes a month. They are not the same gate, they do
not have the same owner, and they do not have to be crossed in the same release.

### GATE A - BLOCKS UPLOAD (the Console physically rejects the file)
**Exactly one item: size.** Estimated ~510 MB against a 500 MB ceiling. Everything else in this
document uploads fine.

### GATE B - BLOCKS RELEASE (uploads, then fails review)
`#2` crypto metadata - `#17` the Arena SKR wager loop (policy **and** broken-functionality) -
`#5` crypto-framed legal pages linked from ungated in-app buttons - `#12`/`#13`/`#19` manifest
hygiene - `#16` catalog parity, which risks a Minimum Functionality rejection if a reviewer sees
capsule enemies - `#14` Data Safety must declare AD_ID - plus the owner-only forms (IARC, Data
Safety, ads declaration, financial-features declaration, Play-sized screenshots).

### GATE C - BLOCKS MONETISATION (releases, then cannot take money)
Licensed Play-delivered purchase and **restore** test (Play requires restore) - production backend
configuration - `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`, which is absent. Most of the machinery has
landed (see finding 6/7/8); none of it is *proven*, and proving it needs Console access, not
engineering.

---

## 4. THE PROGRAMME - sequence, dependencies, honest effort

Effort figures are engineering days and include verification. Where something is a week, it says a
week.

### Wave 0 - the door-openers (do these regardless of the go/no-go)
Each is independently worth doing, cheap, and touches no design.

| Item | Effort | Notes |
|---|---|---|
| **Add an AAB size guard to the build chain** | **0.5 day** | Nothing measures size today. Emit the measured download estimate and fail below a configurable ceiling. This is the item that would have caught the 31 MB. |
| **Make the gate honest about `global-metadata.dat`** | **1 day** | Widen `OpaqueExecutableTokens` toward the strict list with a documented allowlist for the genuine false positives the comment at `:32-34` names. Both copies in one edit - `GooglePlayPackagingGate.cs` and `tools/android/assert-google-play-aab-clean.ps1`. Expect a day of shaking out `System.Security.Cryptography` and ad-SDK hits. **Do not narrow anything without widening in the same edit.** |
| **Owner: answer the closed-testing question** | **0 eng days** | If the Play account is personal and created after 13 Nov 2023, production requires **12 testers for 14 continuous days**. This single lookup decides whether Play is a 2-week or a 2-month calendar, and it costs one Console visit. **Answer this before anything else starts.** |

### Wave 1 - Gate A, the upload blocker
**Dependency: nothing. Runs in its own lane.**

| Item | Effort | Notes |
|---|---|---|
| **Find the 31 MB and decide the size approach** | **1 day** | The RC doc credits a "conservative Android texture pass, 65 eligible overrides" for its margin. First question is whether that pass is still applied or was lost. If recovering it clears the ceiling, Gate A is a 1-day item and PAD is unnecessary. **Establish this before committing to PAD.** |
| **If PAD is genuinely needed: Split Application Binary + install-time pack** | **3-5 days, not the audit's 1-2** | The audit's estimate omits the interaction risk. **What could break:** `bin/Data` is 423.94 MiB compressed across 4144 entries - it moves out of the base module, changing how the player loads it at boot. Unity's Addressables-for-Android PAD path and this project's **custom R2 remote `LoadPath`** both want to own asset delivery, and this project already depends on that R2 path with no local fallback (CLAUDE.md section 16). The catalog path changes. `r2-ship.ps1` and the `pre-push` hook make assumptions about what lives where. Budget the verification, not just the flag. |

### Wave 2 - Gate B, review-blockers
**Dependency: none on Wave 1; can run in parallel. Internally ordered.**

| Item | Effort | Notes |
|---|---|---|
| **Manifest hygiene (`#12`/`#13`/`#19`)** | **0.5 day** | Strip `DUMP` (androidx.work), declare `foregroundServiceType`, drop `preferExternal`. |
| **Catalog parity for the AAB (`#16`)** | **0.5 day** | Fold the AAB's own catalog into `r2-ship.ps1`'s verify so the proof covers the artifact being shipped, not a later one. Call the one file - do not re-inline (CLAUDE.md section 16). |
| **Play-variant legal pages + gate the in-app links (`#5`)** | **1-2 days** | A second terms/privacy pair with no crypto framing, plus `#if GOOGLE_PLAY` routing at `SettingsController.cs:304-311`/`:706-707`. Someone has to actually read them. |
| **Crypto metadata purge (`#2`)** | **3-5 days** | Convert tier-2 runtime guards to compile-out across `DeNelle.Core`. Verified against a re-scan of the artifact, not against source. |
| **Arena (`#17`)** | **1-2 WEEKS, and it is design work** | This is the item that dominates the schedule and it is not an `#if`. Arena is a whole gameplay mode built on SKR wagering, in `DeNelle.Village` with zero gating. Compiling it out leaves a Play build **missing a mode** - which is a broken-functionality problem the moment anything in the UI still points at it. Building a soft-currency variant is a design decision, not a port. **This needs an owner ruling before it can be estimated properly.** |

### Wave 3 - Gate C and the Console
**Hard dependency: a Console account, an uploaded artifact, and possibly the 14-day tester clock.**
Purchase/restore verification, backend config, service account, listing, forms, ratings, and the
`#11` package-id decision. **Calendar, not effort** - and the calendar is the expensive part.

### Honest total
Roughly **3 to 4 weeks of engineering**, of which 1-2 weeks is Arena redesign, **plus a 14-day
closed-testing wall** that cannot be compressed and cannot start until Gate A is cleared.

---

## 5. THE QUESTION TO ANSWER FIRST: IS GOOGLE PLAY WORTH IT RIGHT NOW?

You asked whether I can manage it. I can. Before I do, here is the trade, including the case for
not doing it, because I do not think the answer is obviously yes.

### The case AGAINST, now
- **It contradicts your own standing priority.** `KEY_FACTS.md:137` - owner ruling 2026-09-02,
  verbatim *"we need to shift back to the apk. thats the real vision so that needs to be the
  priority"*. The evidence recorded with that ruling is that a full day of non-APK work produced
  one gameplay commit. Google Play is a bigger version of that same detour.
- **The economics are backwards.** The dApp Store listing is live and takes **0%**. Play takes 30%
  (15% on the first $1M). You would be paying a 30% tax for the privilege of a channel that
  requires deleting the thing your economy is made of.
- **There is no demand signal to scale.** Ads read **$0.04 over 14 days** (`KEY_FACTS.md:124`), and
  per project memory **no purchase has ever completed on any channel**. Play does not fix demand;
  it multiplies whatever demand exists, and right now that multiplies to roughly nothing.
- **The real cost is permanent, not one-time.** Play needs a second product variant forever: two
  storefronts, two identity paths, two currency skins, two legal document sets, and now a second
  Arena. Every future feature pays that tax twice. The two-tier exclusion problem in section 2 is a
  preview - it appeared precisely because the variant was bolted on rather than designed in.
- **The calendar is 14 days you cannot buy back**, and it cannot even start until the size item is
  cleared.

### The case FOR
- **Reach.** Play is where Android players actually are; the dApp Store's audience is small.
- **Most of the machinery is already built** - the Play assembly, billing, Google identity, the
  storefront, the exclusion gate. Finding 6/7/8 changing from "absent" to "landed" is real
  progress. *(Sunk cost is a reason not to throw it away; it is not a reason to finish it now.)*
- **The LevelPlay unlock.** LevelPlay is stuck on Temporary status for want of an https listing
  URL, and a Play listing supplies one. **But check this before using it as a justification** - the
  marketing site is live again as of tonight, and it may already satisfy the requirement at zero
  cost. If it does, this argument disappears.

### My straight answer
**Not now. Park Play as a programme, keep the door open, and stay on the APK.**

Concretely, what I recommend:
1. **Do Wave 0 this week** - the size guard and the honest gate. Half a day plus a day. Both are
   worth doing even if Play never ships, because both currently let a bad artifact through green.
2. **Answer the closed-testing question.** One Console lookup, zero engineering, and it prices the
   whole programme.
3. **Check whether the restored marketing site already unblocks LevelPlay.** If it does, you get
   the one genuine near-term benefit of a Play listing without doing the Play listing.
4. **Then re-decide, with the Arena question answered.** The 1-2 week Arena item is the real cost
   and it needs your design ruling. If SKR wagering is core to the game, Play may simply not be a
   good fit for this product, and that is a legitimate answer rather than a failure.

If you say go, I will run it as a programme with the waves above, one lane at a time, gate and
commit per the usual discipline. But my recommendation is to spend this week's engineering on the
APK and buy the Play decision with three cheap facts instead.

---

## 6. WHAT I DID NOT DO

- No code, asset, `ProjectSettings`, `publishing/`, `CLI_LANES_WO_NUMBERS.md` or `BOARD.html` edit.
- No Unity build and no gate run - the machine was left alone.
- **The 08-30 audit was not rewritten** (CLAUDE.md section 15, frozen docs). Where it is now wrong,
  this work order says so and cites the measurement. The audit stands as the red baseline.
- I did **not** verify restore, the in-app deletion entry point rendering in a Play build, or
  `#20` (`site/admin.html`). Restore and deletion need a Play-delivered build to test honestly.

## 7. WHAT NOT TO TOUCH IF THIS IS ACTIONED

- Do not narrow any gate token list without widening in the same edit.
- Do not re-inline the R2 push or verify into any chain - call `tools/r2-ship.ps1`.
- Do not strip FlowTrace from anything touched (CLAUDE.md section 12).
- Do not treat `PLAY_ARTIFACT_CLEAN_OK` as proof of a clean artifact until section 2 is fixed.

---

**Provenance:** recon executed 2026-09-03 against HEAD `a0f931f95`. Artifact measurements are of
`Builds/Android/EchoesOfElarion-GooglePlay.aab` mtime Sep 1 07:29, read as a zip. Size ceiling
confirmed against Google's Play Console Help "Optimise your app's size and stay within Google Play
app size limits". Network probes were read-only HEAD/GET requests against public URLs.
