# WO-2020 result - Honest Feedback repair and permanent Settings door

**Status:** IMPLEMENTED; TESTER APK INSTALLED ON SEEKER

**Date:** 2026-09-08

## Landed

- Rebuilt Tell Us Honestly as a full-safe-area, two-column layout. Prose, writing field, reward,
  status, Send, store action, caption, title, and Close no longer share collision bands.
- Added Settings > Feedback > Send Feedback, routed through `PanelId.HonestFeedback`.
- The runtime always installs the panel and submit service in eligible hub scenes. The automatic
  prompt remains once-per-save; dismissal no longer removes manual access; the resource grant
  remains once-per-realm.
- Centralized all feedback-flow player copy under `feedback.*` keys via Core `LocalText`, with English
  fallback and device-language table selection.
- Added normalized `language` and raw `systemLanguage` metadata to natural-language feedback payloads.
- Added a focused headless proof capture and `HonestFeedbackSurfaceRegression`; updated the full fleet.

## Evidence

- Compile: `Builds/wo2020-compile.log` - `COMPILE_GATE_OK` (the 12 Solana WebGL package-module
  advisories remain the known WO-1575 optional-target gap; project scripts compiled clean).
- Regression: `Builds/wo2020-regression.log` - `REGRESSION_OK 457/457`, including
  `[honest-feedback-surface]`.
- Feedback render: `Builds/wo2020-feedback-capture-final3.log` - three frames,
  `UI_GEOMETRY_OK 3`, `UI_TOUCH_OK 3/3`, `HONEST_FEEDBACK_CAPTURE_OK 3/3`.
- Eyes-on Seeker proof: `Builds/proof/wo2020-honest-feedback-seeker-2670x1200.png`.
- Settings shell: `Builds/wo2020-settings-capture.log` - `SETTINGS_CAPTURE_OK 3/3`.
- Tester APK: `Builds/Android/DefendersOfTheRealm.apk`, version `2026.09.08.360990` (`360990`),
  462,929,523 bytes, SHA-256
  `316E4352DC41DED840285B74A228EAF28B72827BDBCEBF58FF13D068CE6B7BD8`.
- Content: fresh `R2_PUSH_OK` and `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=276`.
- Device: after the owner removed the Play-signed package, the sanctioned installer returned `Success`.
  Seeker `SM02G4061955851` reports version `2026.09.08.360990` (`360990`), first/last install
  `2026-09-08 11:49:24`.

## Device truth

Seeker `SM02G4061955851` currently holds the Google Play-signed package
`2026.09.07.359670` (`359670`), first installed 2026-09-08 09:29:17. The sanctioned installer reached
ADB but Android refused the tester update with `INSTALL_FAILED_UPDATE_INCOMPATIBLE` because Google
Play's signing certificate differs from the local tester certificate. The Play app was not uninstalled,
so Samantha's local data was preserved. Installing this tester APK now requires an explicit owner choice:
uninstall the Play app (destructive to local data) or create/audit a separate side-by-side tester package.
