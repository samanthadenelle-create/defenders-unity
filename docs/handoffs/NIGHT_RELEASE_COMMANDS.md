# Night release commands (2026-09-13)

Read-only source audit; commands below were NOT executed by this lane. Root owns gates, builds and authorized distribution. Existing API capture-isolation 37-test proof applies to the approved repository-root Vercel preview payload.

## Sequence and provenance

Finish the regression checkpoint first. Record `git rev-parse HEAD`, `git status --short`, and the exact approved diff before builds. Keep code frozen across targets. After EACH target, archive its fresh build log, artifact SHA256/bytes, Addressables settings and catalog/hash, and R2 parity log before the next build. Generic log names are overwritten.

`AndroidBuild.ApplyVersionStamp` sets BOTH `PlayerSettings.bundleVersion` and `PlayerSettings.Android.bundleVersionCode`: UTC minutes since 2026-01-01, name `yyyy.MM.dd.<minutes>`. It runs for APK and AAB, before content build. There is no version restore in its finally (only Play localization restoration). Minute granularity can collide if two builds start within one minute. Windows/WebGL build methods do not stamp a new version themselves; they inherit current PlayerSettings. Inspect the actual post-Unity ProjectSettings diff; do not assume setters are session-only or that all targets share a version.

For clean-checkpoint provenance, preserve a byte-for-byte prebuild snapshot of tracked settings outside source, record each target's build-induced diff, and restore ONLY individually reviewed build-generated changes after Unity exits, preserving unrelated edits. Alternatively retain and explicitly record those deltas. A clean git HEAD alone cannot describe artifacts built after settings changed. Do not reset the tree broadly or relabel existing artifacts with a later version. Record APK/AAB VERSION log lines and inspect embedded player version/catalog for Windows/WebGL.

APK and AAB share `ServerData/Android` and `Library/com.unity.addressables/aa/Android/settings.json`. Prove/archive APK content immediately after APK, BEFORE AAB replaces the Android settings authority. A later AAB parity pass is not independent proof of the earlier APK's catalog. Preserve remote older generations; no prune is needed.

## Windows release EXE

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-windows.ps1 -Release
powershell -NoProfile -File .\tools\r2-ship.ps1
```

Output is the complete `Builds/Windows` directory, including `DefendersOfTheRealm.exe` and its data/plugins; the EXE alone is not distributable. Wrapper cleans stale output and selects pinned Unity 6000.4.8f1, but its success verdict only tests EXE existence. Root additionally requires fresh `Builds/build.log` containing `[DesktopBuild] SUCCEEDED` AND `ADDRESSABLES_CONTENT_OK ... target=StandaloneWindows64`, artifact freshness, full-directory bytes/hashes, and fresh aggregate `R2_PARITY_OK`. The printed EXE MB is stub size, not package size. Remote folder is `ServerData/StandaloneWindows64`; corresponding Library state folder is `aa/Windows`.

## APK and Firebase

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\overnight-apk-build.ps1 -Tester
powershell -NoProfile -ExecutionPolicy Bypass -File .\distribute-android.ps1 -Apk Builds/Android/DefendersOfTheRealm.apk -Notes 'Night release: use recorded checkpoint and APK version'
```

`-Tester` is the existing Firebase tester variant (TESTER_BUILD); omit it for store release. APK wrapper runs schema parity, Android build with expected SUCCEEDED marker, fresh artifact check, then R2 push/verify. Require `SCHEMA_PARITY_OK`, `[AndroidBuild] VERSION`, `ADDRESSABLES_CONTENT_OK ... target=Android`, `ANDROID_CATALOG_OK`, `[AndroidBuild] SUCCEEDED`, fresh `APK_OK` and aggregate `R2_PARITY_OK`. Logs: `Builds/apk-build.log`, `Builds/overnight-apk-status.txt`, `Builds/r2-parity.log`. Capture exact APK bytes/hash and embedded version, not just rounded APK_OK MB.

Distribution repeats schema/R2 proof even without `-Build`, uses configured Firebase app ID and default `testers` group, uploads and notifies that group. Record actual CLI release URL/version. Do not use `-Build` after the separately verified APK, since that silently creates a different artifact.

## WebGL + R2 + Vercel preview

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-webgl.ps1
powershell -NoProfile -File .\tools\r2-ship.ps1
powershell -NoProfile -File .\tools\web-ship.ps1 -StageOnly
vercel deploy --yes --no-color
```

Run deploy from repository root `D:\eoa`, using its existing Vercel linkage and authenticated environment. Default `vercel deploy` is a preview; no `--prod`, promote or alias step. Root `vercel.json` serves `Builds/WebGL`, while `.vercelignore` includes the existing approved `api/` and package files. Keep that payload. `tools/ship-web.ps1` delegates to command-centre, which promotes production; its default is NOT a preview command. `ship-webgl.ps1` targets itch.io and is also the wrong destination here.

Build wrapper requires index existence and prints directory size, but root must additionally assert fresh `[WebGLBuild] SUCCEEDED`, `ADDRESSABLES_CONTENT_OK ... target=WebGL`, loader/data/framework/wasm presence, bytes and hashes, and aggregate R2 parity AFTER this build. Inspect compressed data payload against the repository's 300 MB guidance: the wrapper warns but does not enforce that size. Keep default Brotli for Vercel; no DevBuild or NoBrotli needed. Require `WEB_STAGE_OK` before deploy.

After capturing the actual preview URL:

```powershell
vercel inspect $nightPreviewUrl --format=json --no-color
vercel curl /index.html --deployment $nightPreviewUrl --yes -- --output Builds/night-preview-index.html
powershell -NoProfile -File .\tools\web-ship.ps1 -VerifyCandidate $nightPreviewUrl
```

Require deployment ready, downloaded index SHA256 equal staged local index, and `WEB_LEGAL_OK scope=candidate`. Inspect preview's loader/data response headers and actual browser boot/hero art load; parity alone cannot prove bundles load or WO-1701 device/WebGL acceptance. Authenticated `vercel curl` handles protected previews. Preview verification does not imply production domains changed.

## Google Play AAB

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\google-play-aab-build.ps1
```

Output `Builds/Android/EchoesOfElarion-GooglePlay.aab`. Wrapper covers signing preflight, expected Android build marker, artifact freshness, signing verification, R2 push/parity and bundletool base-module download estimate. Require `AAB_SIGNING_PREFLIGHT_OK`, `AAB_SIGNING_OK`, `[AndroidBuild] VERSION`, target-specific Addressables/Catalog markers, `[AndroidBuild] SUCCEEDED`, fresh `AAB_OK`, `R2_PARITY_OK` and `AAB_SIZE_OK`. Build method also runs `GooglePlayPackagingGate.AssertBuiltArtifact` before SUCCEEDED. No Play Console upload occurs in this wrapper.

The binding size is bundletool MAX from `get-size total --modules=base`, default ceiling 500,000,000 bytes, NOT raw AAB file size. Do not pass SkipSizeGuard or raise ceiling. Archive size/signing/build/status logs and SHA256; the size result estimates Console behavior, not Console acceptance. If only remeasuring the same artifact is necessary, `google-play-aab-build.ps1 -MeasureOnly` performs no build or upload.

## R2 proof scope

`tools/r2-ship.ps1` is the shared upload path: ensure CORS, push the **ServerData parent**, then verify every actual target with catalogs. `-Target` is compatibility-only and does not narrow verification. Do not call `--push ServerData/Android` (flattens remote keys), WarnOnly or prune. Require fresh aggregate `R2_PARITY_OK`; per-target or push markers do not suffice.

Verifier chooses the catalog using the target's Library Addressables settings, not newest filename. It checks hosted object names/bytes, not loadability, player-to-content provenance, or anonymous read. That is why each build's matching settings/catalog and runtime evidence must be retained. An absent unrelated target state can block aggregate parity; retain existing states and report the exact failure rather than dropping a target to obtain green.
