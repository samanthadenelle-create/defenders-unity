# WO-1744 — RESULT (RCA complete; owner console action outstanding)

**Lane:** read-only RCA. **No code was edited. No Unity, build, git or deploy ran.**
**Completed:** 2026-09-15.

## VERDICT

**No code defect.** Every client-side input the Google sign-in path needs is present and correct in
the Play-delivered artifact. The failure is a **missing Android OAuth client registration in the
Google Cloud project**, and it is fixed from the console against the already-installed build — **no
rebuild, no re-ship**.

## THE API — named

**Google Sign-In for Unity v1.0.4**, not Play Games Services, not Firebase Auth, not Credential
Manager. Proof: `GooglePlayIdentityClient.cs:49` sets `UseGameSignIn = false`; the device loads
`libnative-googlesignin.so` (seeker log `:760`) and starts
`com.google.android.gms.auth.api.signin.internal.SignInHubActivity` with
`act=com.google.android.gms.auth.GOOGLE_SIGN_IN` (`:6336`). `libFirebaseCppAuth.so` is linked but
never called on this path — the flow is GSI → our own `/api/auth/google-session`
(`GooglePlayIdentityClient.cs:19`). **The owner's correction is confirmed at source.**

## RANKED CAUSES

| Rank | Cause | Status |
|---|---|---|
| 1 | The Android OAuth client bound to the **Play App Signing** SHA-1 was never created | **STRONGLY SUPPORTED** — `GOOGLE_PLAY_RC_2026-08-30.md:163-166` names it as the outstanding gate; `google-services.json` has one Android client on cert `09:07:83:…`, while `:148-150` records the Play cert as `84:D4:D2:…` |
| 2 | OAuth consent screen still in **Testing** (only listed test users can sign in) | **NOT PROVEN** — console-only; the sole "tester list" on a GSI path, so it is the direct candidate for *"even for testers"* |
| 3 | The local/upload keystore SHA-1 is also unregistered | **UNTESTABLE HERE** — needs the keystore password; one read-only `keytool` command settles it |
| 4 | The `GOOGLE_PLAY` variant strips its own auth | **DISPROVEN** — see below |

## RANK 4 DISPROVEN (the brief's first-class hypothesis)

1. The GSI path **executed end to end on the device** — `:760`, `:6336`, `:6417`, `:6557`. A stripped
   plugin or assembly cannot produce those lines.
2. `GooglePlayContentExclusion.cs:148-212` quarantines **wallet payloads only**. Neither
   `Assets/google-services.json`, nor `FirebaseApp.androidlib/res/values/google-services.xml`, nor
   `Assets/GoogleSignIn/**` is in the list.
3. `DeNelle.GooglePlay.asmdef` carries `defineConstraints: ["GOOGLE_PLAY"]` — compiled **IN**, not
   out. `GoogleSignIn.asmdef` has **no** constraint. `LoginSurfacePlatform.cs:64` takes the
   `#if GOOGLE_PLAY` branch whose failure string at `:82` is verbatim the log line `:7519`.
4. The plugin never reads the Android resource anyway: `GoogleSignInImpl.cs:38-43` passes the C#
   constant (`GooglePlayIdentityClient.cs:17-18`) to the native layer.

## BACKEND IS NOT INVOLVED — measured, not inferred

`grep -c "google-session"` = **0** and `grep -c "auth/google"` = **0** on the whole seeker log.
Source ordering agrees: `GooglePlayIdentityClient.cs:51` throws, `:54-62` (the POST) never runs,
`:76-79` catches. **Do not debug Vercel or any `GOOGLE_*` production variable for this defect.**

## ⚠ TWO CLAIMS IN THE BRIEF THAT THE SUPPLIED LOGS DO NOT SUPPORT

1. **`DEVELOPER_ERROR` / `TokenPendingResult` appear in NEITHER file.** `grep -ci` returns **0** on
   both `logcat-seeker.txt` and `logcat-emulator.txt`. What is captured is `SignInException`
   (`:7518`). `DEVELOPER_ERROR` is consistent with it and is almost certainly what the owner saw,
   but it is **corroboration, not a captured line**.
2. **`logcat-emulator.txt` captures no sign-in attempt.** The app **was** running (pid `12477`,
   **202** `[Flow:*]` lines over `11:19:35.833 → 11:20:25.851`, scene `HeroSelect`) — but the tag
   histogram is `HUD 93, Perf 64, Progression 19, VFX 10, VfxPerfGate 6, Echo 4, WebTrace 2,
   Tunables 2, Maintenance 2` and **`Flow:Auth` = 0**;
   `grep -cE "Flow:Auth|LoginPanel|SignInHub|SignInException|Google sign-in"` = **0**.
   **"It fails on both signing certificates" is therefore UNPROVEN from the supplied evidence** —
   not contradicted, just not captured. That matters: the brief used that claim to demote SHA-1
   mismatch. With it unproven, SHA-1 mismatch is restored to rank 1, and it is the only hypothesis
   with documentary support.
   *(This RESULT first said the emulator log contained nothing from the app, reasoning from
   `grep -c "echoesofelarion"` = 0. That grep only rules out **system** lines naming the package;
   Unity's `[Flow:*]` lines never carry one. Corrected before hand-back, and the error recorded
   rather than quietly fixed, per §11B.)*

## WHAT THE OWNER DOES (full ordered checklist in the WO, §6)

1. **Play Console → Test and release → Setup → App signing** — copy the App signing key SHA-1 (and
   the upload key SHA-1). Re-read it there; the doc value is hearsay.
2. `keytool -list -v -keystore D:\eoa\dotr-release.keystore -alias dotr` — record the local SHA-1.
3. **Google Cloud Console → project `defenders-of-the-realm-echos` (264518851517) → APIs & Services
   → Credentials** — create an **Android** OAuth client for package
   `com.denellestudios.echoesofelarion` for each SHA-1 from steps 1-2 that has none.
4. **APIs & Services → OAuth consent screen** — record Publishing status; add test users or Publish.
5. Force-stop and retest the **installed** build. PASS =
   `[Flow:Auth] Google Play identity verified and bound` (`GooglePlayIdentityClient.cs:73`).
6. Registering the **App signing** SHA-1 covers **every Play tester at once** — no rebuild, no
   per-tester step, no PGS project to publish.

## RELATION TO WO-1742

WO-1742's banner note recorded *"NOT PROVABLE FROM HERE: whether the Play-delivered build's App
Signing SHA-1 is registered on the Android OAuth client."* This WO does not prove it from here
either — it makes it **decidable in one console read** and names the page, the field, and the value
to compare against. WO-1742's separate finding stands untouched: a failed Google sign-in cannot break
the save rail, because the guest identity syncs.

## FILES READ (none modified)

- `logs/device/signin-test/logcat-seeker.txt`, `logs/device/signin-test/logcat-emulator.txt`
- `Assets/_Modules/GooglePlay/GooglePlayIdentityClient.cs`,
  `Assets/_Modules/GooglePlay/DeNelle.GooglePlay.asmdef`
- `Assets/_Modules/Core/Platform/LoginSurfacePlatform.cs`,
  `Assets/_Modules/Core/Payments/GooglePlayIdentityBridge.cs`
- `Assets/GoogleSignIn/Impl/GoogleSignInImpl.cs`, `Assets/GoogleSignIn/GoogleSignInStatusCode.cs`,
  `Assets/GoogleSignIn/GoogleSignIn.asmdef`
- `Assets/google-services.json`,
  `Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml`
- `Assets/Editor/GooglePlayContentExclusion.cs`, `ProjectSettings/ProjectSettings.asset`
- `docs/releases/GOOGLE_PLAY_RC_2026-08-30.md`, `CLI_LANES_WO_NUMBERS.md`

No keys, secrets or the owner's email were written to any file.
