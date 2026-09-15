# WO-1744 — Google sign-in fails on the Play-delivered build (`DEVELOPER_ERROR`): which console page fixes it

> *(Minted first as 1743; a parallel Night Market layout lane put 1743 on the banner in the same
> minute, so this lane ceded per §2 first-on-disk-and-referenced-wins and re-minted at 1744.)*

**Status:** BLOCKED — RCA complete and proven; no code change is required. Blocked on the owner's
Google Play Console + Google Cloud Console check (§6), which is the only seat that can see those pages.
**Lane:** Auth / identity (read-only RCA)
**Raised:** 2026-09-15, from the owner's device capture on Seeker `SM02G4061955851`
**Silo:** owner-only (Google Play Console + Google Cloud Console). No CLI seat can see these pages.
**Sibling:** `WorkOrders/WORK_ORDER_1742_guest_save_rail_vs_google_signin_hypothesis.md` — its banner
note records *"NOT PROVABLE FROM HERE: whether the Play-delivered build's App Signing SHA-1 is
registered on the Android OAuth client."* **This WO is the follow-up that makes that decidable, and
it decides it with a console read, not a guess.**

---

## 0. NO CODE DEFECT FOUND

Every client-side input this path needs was verified present and correct in the **Play-delivered**
artifact. Details in §4 and §5. **Nothing in this WO asks for a rebuild.** The fix is console-side
and takes effect against the already-installed build.

---

## 1. WHAT IS ACTUALLY PROVEN, AND WHAT IS NOT

### PROVEN — from `logs/device/signin-test/logcat-seeker.txt` (read 2026-09-15)

| Fact | Evidence |
|---|---|
| The app is the real Play package | `:756` `com.denellestudios.echoesofelarion` |
| **Google Sign-In (GSI), not Play Games Services** | `:760` loaded native libs include `libnative-googlesignin.so`; `:6336` starts `com.denellestudios.echoesofelarion/com.google.android.gms.auth.api.signin.internal.SignInHubActivity` with `act=com.google.android.gms.auth.GOOGLE_SIGN_IN` |
| The request reached Google Play services | `:6417` GMS `com.google.android.gms/.auth.api.signin.ui.SignInActivity`; `:6557` GMS `.signin.activity.SignInActivity` |
| The sign-in threw | `:7518` `W Unity : [Flow:Auth] Google Play sign-in failed: SignInException`, `:7519` `Google Play identity did not produce a verified play-* identity.` |
| It failed three times, then the owner gave up | `:7518` and `:7679` are `SignInException` throws; `:7759→:7760` are 1 ms apart with **no** exception line, i.e. a non-throwing `false` return from `GooglePlayIdentityClient.EnsureSignedInAsync` (`:52`), not a third throw. Then `:8180` `[Flow:Auth] chose Play as Guest.` |
| **The backend was NEVER called** | `grep -c "google-session"` = **0**; `grep -c "auth/google"` = **0**, across the whole seeker log |

### NOT PROVEN — and the brief asserted both as fact

1. ⛔ **The literal line `TokenPendingResult: Status{statusCode=DEVELOPER_ERROR, resolution=null}` is
   NOT in either supplied log.** `grep -ci "DEVELOPER_ERROR"` returns **0** on
   `logcat-seeker.txt` and **0** on `logcat-emulator.txt`; `grep -ci "TokenPendingResult"` likewise
   returns **0/0**. The quoted line came from the owner's own observation, not from these files.
   What the files prove is `SignInException` at the GSI seam. `DEVELOPER_ERROR` is
   `GoogleSignInStatusCode.DeveloperError` (`Assets/GoogleSignIn/GoogleSignInStatusCode.cs:57`) and
   is entirely consistent with what was captured — but it is **corroboration, not a captured line**.

2. ⛔ **`logcat-emulator.txt` CAPTURES NO SIGN-IN ATTEMPT AT ALL.**
   The app **was** running — pid `12477`, **202** `[Flow:*]` lines across the window
   `11:19:35.833 → 11:20:25.851` (50 s), scene `HeroSelect`. But the tag histogram is
   `HUD 93, Perf 64, Progression 19, VFX 10, VfxPerfGate 6, Echo 4, WebTrace 2, Tunables 2,
   Maintenance 2` — **`Flow:Auth` count = 0**. Likewise
   `grep -cE "Flow:Auth|LoginPanel|SignInHub|SignInException|Google sign-in"` = **0**.
   No sign-in was requested, attempted or failed inside this capture.
   **Therefore "it fails on BOTH signing certificates" is UNPROVEN from the supplied evidence** —
   not contradicted, simply not captured. `signin-emulator.mp4` may show what happened on screen;
   the log does not.

   *(Correction note, recorded deliberately per §11B: this WO first asserted the emulator log
   "contains nothing from this app", reasoning from `grep -c "echoesofelarion"` = 0. That grep only
   proves no **system** line named the package — Unity's own `[Flow:*]` lines never carry a package
   name. The conclusion below survives; the evidence sentence did not, and the corrected one is
   above.)*

**This second point changes the ranking.** The brief used "both certs fail" to demote *"the SHA-1
just isn't registered"*. With that claim unproven, SHA-1 registration is restored to **rank 1** — and
it is the hypothesis that carries documentary evidence (§3).

---

## 2. WHICH GOOGLE API IS BEING CALLED — named, from the code and the device

**Google Sign-In for Unity, plugin v1.0.4** (`Assets/GoogleSignIn/Editor/google-signin-plugin_v1.0.4.txt`).

- Entry point: `Assets/_Modules/GooglePlay/GooglePlayIdentityClient.cs:44-51`
  ```
  GoogleSignIn.Configuration = new GoogleSignInConfiguration {
      WebClientId = WebClientId, RequestIdToken = true, RequestEmail = true, UseGameSignIn = false };
  GoogleSignInUser user = await GoogleSignIn.DefaultInstance.SignIn();
  ```
  **`UseGameSignIn = false` (`:49`) is the decisive line: this is NOT Play Games Services.**
- Native bridge: `Assets/GoogleSignIn/Impl/GoogleSignInImpl.cs:38-43` passes `configuration.UseGameSignIn`
  and `configuration.WebClientId` into `GoogleSignIn_Configure` — i.e. the **C# constant** is the
  client id in play, not an Android resource (see §4).
- Native lib on the device: `libnative-googlesignin.so` (seeker `:760`).

### The other loaded libraries are NOT on this path
`libFirebaseCppAuth.so` is also listed at `:760`, but nothing on this path calls Firebase Auth: the
flow is **GSI → our own `/api/auth/google-session`** (`GooglePlayIdentityClient.cs:19`), never
`FirebaseAuth.SignInWithCredential`. **This confirms the owner's correction: this is not Firebase
Auth, and the Firebase Auth console page is the wrong page.**

The `play-*` identity is **ours**, minted by our backend
(`GameStateService.IsGooglePlayIdentity`), not a Play Games player id.

---

## 3. RANKED ROOT CAUSES

### RANK 1 — The Android OAuth client for the Play App Signing certificate was never created. **STRONGLY SUPPORTED.**

`DEVELOPER_ERROR` from GSI with `RequestIdToken = true` means: **Google Play services could not find
an OAuth 2.0 *Android* client, in the same Google Cloud project as the web client, whose
(package name, signing SHA-1) matches the calling app.** It is a configuration verdict returned
before any token is minted — which is exactly why the backend was never called (§1, `grep` = 0).

The repo's own release record says this step was still outstanding:

> `docs/releases/GOOGLE_PLAY_RC_2026-08-30.md:163-166` —
> *"The remaining external identity gate is creation/verification of Android OAuth client entries for
> package `com.denellestudios.echoesofelarion` using the Play signing SHA-1 certificate(s), followed
> by a real ID-token exchange from the Play-delivered build."*

And the same file, `:148-150`:

> *"The Play deployment certificate SHA-1 for the current Seeker/internal-test path is
> `84:D4:D2:09:58:B6:A0:61:39:9B:B5:FF:28:86:05:23:49:6A:72:22`. Google OAuth must bind the Android
> client to the Play App Signing certificate, not the upload certificate."*

Meanwhile `Assets/google-services.json` carries **exactly one** Android OAuth client
(`client_type: 1`), and its `android_info.certificate_hash` is
`09078344013976019417555ec173755544d61f75` — i.e. `09:07:83:44:01:39:76:01:94:17:55:5E:C1:73:75:55:44:D6:1F:75`.

**`09:07:83:…` ≠ `84:D4:D2:…`.** The one registered Android client is bound to a different
certificate from the one Play signs the delivered build with.

⚠ **Caveat that must not be skipped:** `google-services.json` is a *point-in-time export*. GMS
validates against the **live** OAuth client list in the Cloud project at request time. The json shows
what was registered when it was downloaded; **the console is the authority, and editing the json
fixes nothing.** Also, the `84:D4:D2:…` value above is a *doc* value — hearsay under §11B until the
owner re-reads it in Play Console (step 1 of §6).

### RANK 2 — OAuth consent screen still in "Testing". **NOT PROVEN, cheap to check.**

If the consent screen for project `defenders-of-the-realm-echos` is in **Testing** publishing status,
only users on its test-user list can complete sign-in. This is the only "tester list" that exists on
a GSI path, and it is the direct answer to *"even for testers"*. Marked rank 2, not rank 1, because
a Testing-status consent screen usually surfaces as an access-denied/consent error rather than
`DEVELOPER_ERROR`. **It costs one console read to settle, so settle it in the same trip.**

### RANK 3 — The local/upload keystore SHA-1 is also unregistered. **UNTESTABLE FROM HERE; owner has the one command.**

Only relevant if the emulator failure is real (§1 point 2 — currently unproven). The discriminator is
read-only and needs the keystore password, which only the owner has:

```
keytool -list -v -keystore D:\eoa\dotr-release.keystore -alias dotr
```
(keystore path read from `ProjectSettings/ProjectSettings.asset:273-274`.)

Compare the printed `SHA1:` against `09:07:83:44:01:39:76:01:94:17:55:5E:C1:73:75:55:44:D6:1F:75`.
- **Differs** → neither certificate is registered, the "both certs fail" symptom collapses into
  RANK 1, and both SHA-1s should be added in the same console visit.
- **Matches** → a locally-signed build *should* work, and the emulator claim needs a fresh capture
  that actually contains the app before anyone acts on it.

### RANK 4 — The `GOOGLE_PLAY` variant strips its own auth. **DISPROVEN. Do not spend time here.**

The brief called this a first-class hypothesis. It is ruled out by three independent facts:

1. **The device log shows the GSI path executing end to end.** Seeker `:760` loads
   `libnative-googlesignin.so`; `:6336` starts `SignInHubActivity` *inside our package*; `:6417`
   hands to GMS. A stripped plugin or a stripped assembly could not produce those lines.
2. **The quarantine list does not touch anything on this path.**
   `Assets/Editor/GooglePlayContentExclusion.cs:148-212` is wallet-only —
   `Assets/Resources/SolanaUnitySDK`, `wallets.json`, `skr_*.json`, `skin.json`,
   `battle_monthly*.json`, `stake-rewards.json`, `packs.json`, and their StreamingAssets twins.
   Neither `Assets/google-services.json` nor
   `Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml` nor
   `Assets/GoogleSignIn/**` appears in it.
3. **The guards point the right way.** `Assets/_Modules/GooglePlay/DeNelle.GooglePlay.asmdef` carries
   `defineConstraints: ["GOOGLE_PLAY"]` — so the identity client is compiled **IN** for this variant,
   not out. `Assets/GoogleSignIn/GoogleSignIn.asmdef` has **no** define constraint at all, so the
   plugin compiles into every Android artifact. `LoginSurfacePlatform.cs:64` takes the
   `#if GOOGLE_PLAY` branch that calls `GooglePlayIdentityBridge.EnsureSignedInAsync()` — which is
   exactly the branch whose failure message `:7519` is quoting verbatim
   (`LoginSurfacePlatform.cs:82`).

Additional belt-and-braces point: **the plugin does not read `default_web_client_id` from the Android
resources at all.** `GoogleSignInImpl.cs:38-43` passes the C# constant
(`GooglePlayIdentityClient.cs:17-18`) straight to the native layer. Even if that resource *were*
quarantined — it is not — sign-in would still reach GMS with the right client id.

---

## 4. REPO CONFIG, AS READ AT SOURCE 2026-09-15

| Input | Value / location | Matches? |
|---|---|---|
| Package name (Unity) | `com.denellestudios.echoesofelarion` — `ProjectSettings/ProjectSettings.asset:169-170` | ✅ |
| Package name (on device) | `com.denellestudios.echoesofelarion` — seeker log `:756` | ✅ same |
| Package name (google-services.json) | `com.denellestudios.echoesofelarion` — `client[0].client_info.android_client_info` | ✅ same |
| Cloud project | `defenders-of-the-realm-echos`, project number `264518851517` | ✅ |
| Web OAuth client (type 3) | `264518851517-q9i3…avlj1.apps.googleusercontent.com` — `Assets/google-services.json` | ✅ |
| Same value compiled into the build | `GooglePlayIdentityClient.cs:17-18` — **identical string** | ✅ |
| Same value as the backend audience | `docs/releases/GOOGLE_PLAY_RC_2026-08-30.md:171-173` (`GOOGLE_IDENTITY_AUDIENCES`) — **identical string** | ✅ |
| Android OAuth client (type 1) | `264518851517-q1a8…r5p8i…`, cert hash `09078344…44d61f75` | ⚠ **one cert only, and not the Play one** |
| Play App Signing SHA-1 | `84:D4:D2:…:72:22` per `GOOGLE_PLAY_RC_2026-08-30.md:148-150` | ⛔ **no matching Android client in the export** |
| Android resource `default_web_client_id` | `Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml` — present, matches | ✅ (and unused by GSI, §3 rank 4) |

**Conclusion of this table: there is no package-name mismatch and no wrong client id baked into the
build. Everything the repo controls is consistent. The one inconsistency is the certificate the one
registered Android OAuth client is bound to — and that record lives only in the console.**

---

## 5. IS THE BACKEND HALF EVEN REACHABLE? — NO, AND IT IS NOT THE PROBLEM

Ordering from source, `GooglePlayIdentityClient.cs`:
- `:51` `await GoogleSignIn.DefaultInstance.SignIn()` — **throws here**
- `:54-62` build and POST to `/api/auth/google-session` — **never executed**
- `:76-79` `catch` → `FlowTrace.Warn("Auth", "Google Play sign-in failed: " + ex.GetType().Name)`
  → the exact `:7518` line in the log.

Corroborated by measurement, not just by reading: `grep -c "google-session"` = **0** and
`grep -c "auth/google"` = **0** on the seeker log.

> ⛔ **Do not debug Vercel, `GOOGLE_IDENTITY_ENABLED`, `GOOGLE_IDENTITY_KEY`,
> `GOOGLE_IDENTITY_AUDIENCES`, `GOOGLE_PLAY_PACKAGE_NAME` or
> `GOOGLE_PLAY_ACCOUNT_BINDING_KEY` for this defect.** No request from this build has ever reached
> the backend. Those variables are correctly set and simply have not been exercised yet.

---

## 5b. PROVEN BY BYTES, 2026-09-15 (lead) — checklist steps 1 and 2 are now MEASURED, not hearsay

The lane's rank-1 cause rested on a doc value (`GOOGLE_PLAY_RC_2026-08-30.md:148`) and said so. The
lead then read the three certificates directly with `apksigner verify --print-certs`:

| Artifact | Signer SHA-1 | Matches the registered OAuth client? |
|---|---|---|
| Registered in `Assets/google-services.json` (`certificate_hash`) | `09078344013976019417555ec173755544d61f75` | — (this IS the registered value) |
| Local tester APK, `Builds/Android/DefendersOfTheRealm.apk` (dotr keystore) | `09078344013976019417555ec173755544d61f75` | **YES** |
| **Play-delivered APK pulled off the Seeker** (`installerPackageName=com.android.vending`, `versionName=2026.09.09.362625`) | `84d4d20958b6a061399bb5ff28860523496a7222` | **NO** |

So the one Android OAuth client is bound to the **local keystore**, and every Play-delivered build —
every tester's, Sminer's — signs with Google's Play App Signing certificate, which has **no** client.
That is a real error, confirmed by the artifact itself rather than by any document. It is also why
the local tester build's certificate is fine and the failure is specific to the Play track.

**Effect on §6:** steps 1 and 2 (read the Play SHA-1; compare the local keystore) are DONE — the
values above are the answers. **The only remaining step is creating the Android OAuth client for
`84:D4:D2:09:58:B6:A0:61:39:9B:B5:FF:28:86:05:23:49:6A:72:22` in Cloud Console** (step 3), and
checking the consent-screen publishing status (step 4). No rebuild, no per-tester step.

## 6. THE OWNER'S CONSOLE CHECKLIST — ordered, decisive, no rebuild

Two consoles, one trip. **Not the Firebase console.**

### Step 1 — Play Console: read the real App Signing SHA-1
**Play Console → (app *Echoes of Elarion*) → Test and release → Setup → App signing.**
Under **App signing key certificate**, copy the **SHA-1 certificate fingerprint**.
- Record it. Do **not** trust the `84:D4:D2:…` in `GOOGLE_PLAY_RC_2026-08-30.md:148` — re-read it here.
- While on this page also copy the **Upload key certificate** SHA-1 (a second row on the same page).

### Step 2 — Local keystore SHA-1 (one command, needs the keystore password)
```
keytool -list -v -keystore D:\eoa\dotr-release.keystore -alias dotr
```
Record the `SHA1:` line.

### Step 3 — Google Cloud Console: the page that actually fixes this
**console.cloud.google.com → project `defenders-of-the-realm-echos` (number `264518851517`)
→ APIs & Services → Credentials.**

Under **OAuth 2.0 Client IDs**, look at every client of type **Android**:
- **Compare** each one's *Package name* and *SHA-1 certificate fingerprint* against
  `com.denellestudios.echoesofelarion` + the Step 1 value.
- **Expected finding, per §3 rank 1:** there is one Android client bound to
  `09:07:83:44:01:39:76:01:94:17:55:5E:C1:73:75:55:44:D6:1F:75` and **none** bound to the Play
  App Signing SHA-1.
- **Fix:** **+ CREATE CREDENTIALS → OAuth client ID → Application type: Android**, package name
  `com.denellestudios.echoesofelarion`, SHA-1 = the **Step 1 App signing key** value.
  Then repeat for the **Step 1 upload key** SHA-1, and again for the **Step 2 local keystore** SHA-1
  if it differs from both. One Android client per fingerprint; there is no downside to having several.
- Also confirm the **Web application** client
  `264518851517-q9i3…avlj1.apps.googleusercontent.com` is present in **this same project**. If it is
  not, nothing else in this list can work.

### Step 4 — OAuth consent screen (settles §3 rank 2 in the same visit)
**APIs & Services → OAuth consent screen.**
- Read **Publishing status**. If **Testing**, either add every tester's Google account under
  **Test users**, or click **PUBLISH APP** to move to **In production**
  (the basic `email` / `profile` scopes used here need no verification review).
- **Record which it was** — a "Testing" status is the whole answer to *"even for testers"*.

### Step 5 — Retest, no rebuild
Changes at steps 3–4 are server-side at Google and take effect on the **already-installed** Play
build (allow a few minutes; force-stop the app first).
- On the Seeker, tap **Continue with Google** again and re-capture logcat.
- **PASS looks like:** `[Flow:Auth] Google Play identity verified and bound; purchase may proceed.`
  (`GooglePlayIdentityClient.cs:73`) and a `play-*` player id.
- **If it still fails:** re-capture with the app present in the log and hand it back — the next
  question is which *status code* GMS returns, and that needs a log that actually contains it.

### Step 6 — Testers
Once step 3 registers the **App signing** SHA-1, **every** Play internal/closed tester is covered at
once, with no rebuild and no per-tester registration: they all install the same Play-signed artifact.
There is no Play Games Services project to publish and no PGS tester list on this path (§2).

---

## 7. WHAT NOT TO TOUCH

- Do **not** edit `Assets/google-services.json` or
  `Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml`. Neither is the
  authority; GMS reads the live console.
- Do **not** change `GooglePlayIdentityClient.cs`. Its client id matches the json and the deployed
  backend audience (§4).
- Do **not** add anything to `GooglePlayContentExclusion.cs`. Nothing on this path is quarantined,
  and this build reaches the dApp Store where the owner's only real transactions live.
- Do **not** rebuild or re-ship. **No artifact change is required by this WO.**

---

## ACCEPTANCE CRITERIA

1. Play Console App signing SHA-1 recorded (step 1).
2. The Cloud Credentials Android-client list compared against it, and the missing client(s) created
   (step 3).
3. Consent-screen publishing status recorded, and testers unblocked (step 4).
4. A fresh Seeker logcat **containing the app** shows
   `Google Play identity verified and bound` — or, if not, is handed back with the GMS status code
   visible.
