// =============================================================================
// AndroidBuild — produces an ARM64 APK for the Solana Seeker phone. Run headless:
//
//   Unity.exe -batchmode -quit -buildTarget Android -projectPath <proj> \
//             -executeMethod DeNelle.Editor.AndroidBuild.BuildSeekerApk
//
// Output: <proj>/Builds/Android/DefendersOfTheRealm.apk
//
// Prerequisites:
//   1. Unity Hub -> 6000.4.8f1 -> Add Modules -> "Android Build Support" (with
//      OpenJDK + Android SDK + NDK sub-modules). Without these the build aborts
//      immediately with a Gradle error.
//   2. The Seeker is ARM64-only — this script forces ARM64, IL2CPP, and the
//      minimum SDK level the Seeker ships with.
//
// After the APK lands, push to the connected Seeker over USB:
//
//   adb install -r Builds\Android\DefendersOfTheRealm.apk
//
// (adb ships with the Android module under
//  C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Data\PlaybackEngines\
//      AndroidPlayer\SDK\platform-tools\adb.exe.)
// =============================================================================

using System;
using System.IO;
using System.Linq;
using DeNelle.Editor.Localization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>
    /// One-shot Android APK build entry point. Configures Seeker-correct player
    /// settings (ARM64-only, IL2CPP, package id), then builds whatever scenes
    /// are enabled in Build Settings to <c>Builds/Android/</c>.
    /// </summary>
    public static class AndroidBuild
    {
        private const string OutputDir = "Builds/Android";
        private const string ApkName = "DefendersOfTheRealm.apk";
        private const string PlayAabName = "EchoesOfElarion-GooglePlay.aab";
        // Must MATCH the installed app so testers UPDATE IN PLACE (verified on the Seeker
        // 2026-07-16: the live package is com.denellestudios.echoesofelarion). The old
        // com.denelle.defenders would install as a SEPARATE app.
        private const string PackageId = "com.denellestudios.echoesofelarion";
        // The HOME-SCREEN LABEL. Unity generates launcher/src/main/res/values/strings.xml from
        // PlayerSettings.productName and the launcher manifest carries
        // android:label="@string/app_name" (both verified in the generated Gradle project), so
        // productName IS the installed app's name. Owner decision 2026-08-08: it must MATCH the
        // store listing, which reads "Echoes of Elarion" — consistent with the live legal pages,
        // which name the App "Echoes of Elarion (a chapter of Defenders of the Realm)".
        //
        // SAFE FOR EXISTING TESTERS — their saves are NOT keyed off this string. Unity keys
        // Android persistence off the PACKAGE NAME: PlayerPrefs lives in
        // /data/data/<pkg>/shared_prefs/<pkg>.v2.playerprefs.xml and Application.persistentDataPath
        // in /storage/emulated/<user>/Android/data/<pkg>/files. MEASURED on the Seeker, not assumed
        // — Logs/seeker-wallet-fresh.txt:688 shows this build resolving persistentDataPath to
        // /storage/emulated/0/Android/data/com.denellestudios.echoesofelarion/files, with the
        // product name appearing nowhere in the path. PackageId is unchanged, so the save
        // location is byte-identical and testers update in place with progress intact.
        //
        // NOTE productName is a GLOBAL PlayerSetting (Unity has no per-platform productName), so
        // this assignment also lands in ProjectSettings.asset and therefore governs the desktop
        // and WebGL players. ProjectSettings.asset carries the SAME value deliberately — see the
        // report; desktop PlayerPrefs (HKCU\Software\DeNelle\<productName>) and persistentDataPath
        // (LocalLow\DeNelle\<productName>) move with it.
        private const string ProductName = "Echoes of Elarion";
        private const string CompanyName = "DeNelle";

        [MenuItem("Defenders/Build/Android APK (Seeker)")]
        public static void BuildSeekerApk()
        {
            BuildAndroidArtifact(isGooglePlay: false);
        }

        /// <summary>
        /// Produces the Play artifact only after the source/package isolation gate can prove
        /// that Wallet, Web3, Solana SDK and MWA cannot enter it.  The gate currently fails
        /// closed while the runtime Village assembly directly references Wallet; this menu
        /// item must never be weakened to "hide the UI and hope stripping removes the SDK".
        /// </summary>
        [MenuItem("Defenders/Build/Google Play AAB (compliance gated)")]
        public static void BuildGooglePlayAab()
        {
            if (!GooglePlayPackagingGate.AssertSourceIsolation())
            {
                EditorApplication.Exit(1);
                return;
            }

            BuildAndroidArtifact(isGooglePlay: true);
        }

        private static void BuildAndroidArtifact(bool isGooglePlay)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[AndroidBuild] No enabled scenes in Build Settings — aborting.");
                EditorApplication.Exit(1);
                return;
            }

            // WO-1282 Lane D. MUST run before the Addressables content build below: a Play build
            // that died mid-flight leaves the wallet payloads quarantined OUTSIDE
            // Resources/StreamingAssets, and the content build reads the tree BEFORE
            // BuildPlayerProcessor.PrepareForBuild ever fires — so without this sweep a Seeker
            // APK could be baked from a wallet-less tree with every marker green. Cheap no-op
            // when the ledger is absent, which is the normal case.
            GooglePlayContentExclusion.EnsureTreeIsWhole();

            ApplyAndroidPlayerSettings();

            // This setting is sticky editor state, so assert it for BOTH artifacts. A Seeker
            // invocation after a Play invocation must go back to APK rather than silently emit
            // an app bundle with the wrong store signature expectations.
            EditorUserBuildSettings.buildAppBundle = isGooglePlay;

            string dir = Path.GetFullPath(OutputDir);
            Directory.CreateDirectory(dir);
            string artifactPath = Path.Combine(dir, isGooglePlay ? PlayAabName : ApkName);

            Debug.Log($"[AndroidBuild] Building {scenes.Length} scene(s) -> {artifactPath}");
            foreach (string s in scenes)
                Debug.Log($"[AndroidBuild]   scene: {s}");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = artifactPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
                // A custom -executeMethod build owns BuildPlayerOptions, so Unity's command-line
                // symbols do not automatically reach the player compilation. Forward them
                // explicitly; with no -extraScriptingDefines argument this remains an empty array.
                extraScriptingDefines = ArtifactScriptingDefines(isGooglePlay),
            };

            if (options.extraScriptingDefines.Length > 0)
                Debug.Log($"[AndroidBuild] Extra scripting defines: {string.Join(";", options.extraScriptingDefines)}");

            // WO-974: build Addressables content EXPLICITLY. Without this the bundles are rebuilt
            // only if an uncommitted per-machine Editor preference happens to say so — so a fresh
            // clone or CI ships stale/absent StreamingAssets/aa and resolves nothing at runtime.
            //
            // WO-1124: SWITCH THE TARGET FIRST. Addressables builds for the ACTIVE target, and
            // BuildPlayer is what switches it — so content built here landed in whichever platform
            // folder the editor happened to be on. From an editor left on Win64 that meant Windows
            // bundles inside an Android APK: the device asked the CDN for an Android catalog that
            // was never uploaded and resolved NOTHING, silently, on a build where every marker was
            // green. The switch lives HERE and not in a wrapper script precisely because the whole
            // failure was assuming a human step; this way it holds for the menu item, batchmode, CI
            // and the ship chain alike. It is a no-op when already on Android, so the fast path
            // stays fast.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log($"[AndroidBuild] active target is '{EditorUserBuildSettings.activeBuildTarget}' — " +
                          "switching to Android BEFORE the content build (WO-1124).");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Debug.LogError("[AndroidBuild] ABORTED — could not switch the active build target to Android. " +
                                   "Building content now would produce the wrong platform's bundles (WO-1124).");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            bool playLocalizationPrepared =
                GooglePlayLocalizationVariant.PrepareForAddressables(options.extraScriptingDefines);
            try
            {
                if (!AddressablesContentBuild.EnsureBuilt("AndroidBuild", BuildTarget.Android))
                {
                    Debug.LogError("[AndroidBuild] ABORTED — Addressables content build failed (WO-974/WO-1124).");
                    EditorApplication.Exit(1);
                    return;
                }

                // WO-1124 §3.2: PROVE the catalog this APK will ask for actually exists, by name, in the
                // Android folder. ApplyVersionStamp already set bundleVersion, so this is a file-exists
                // check against a known name — cheap, and it catches every future variant of "content
                // went somewhere else", not just the target-switch one this ticket found.
                if (!AssertAndroidCatalogForThisBuild()) return;

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    if (isGooglePlay && !GooglePlayPackagingGate.AssertBuiltArtifact(artifactPath))
                    {
                        Debug.LogError("[AndroidBuild] PLAY_ARTIFACT_REJECTED — the AAB contains a forbidden crypto/wallet surface.");
                        EditorApplication.Exit(1);
                        return;
                    }

                    // WO-1486: the marker used to report summary.totalSize, which is the UNCOMPRESSED
                    // total (2367 MB against 463 MiB actually on disk on 2026-09-06). Nobody reading the
                    // log could tell whether the shipped artifact had grown. The FIRST number is now the
                    // artifact's real on-disk length; the report figure is kept as a second LABELLED
                    // number so the old signal is not lost. The literal marker text every caller greps
                    // ('[AndroidBuild] SUCCEEDED') is unchanged, and no caller parses the MB.
                    long onDiskBytes = File.Exists(artifactPath) ? new FileInfo(artifactPath).Length : -1L;
                    string onDisk = onDiskBytes >= 0
                        ? $"{onDiskBytes / (1024 * 1024)} MB on-disk ({onDiskBytes} bytes)"
                        : "on-disk size UNKNOWN (artifact not found at the build path)";

                    Debug.Log($"[AndroidBuild] SUCCEEDED — {onDisk} in {summary.totalTime}. " +
                              $"report-uncompressed={summary.totalSize / (1024 * 1024)} MB. " +
                              $"{(isGooglePlay ? "AAB" : "APK")}: {artifactPath}");
                }
                else
                {
                    Debug.LogError($"[AndroidBuild] FAILED — result={summary.result}, " +
                                   $"errors={summary.totalErrors}. See log for Gradle output.");
                    EditorApplication.Exit(1);
                }
            }
            finally
            {
                if (playLocalizationPrepared)
                    GooglePlayLocalizationVariant.Restore("Android build finally");
            }
        }

        private static string[] ArtifactScriptingDefines(bool isGooglePlay)
        {
            string wanted = isGooglePlay ? "GOOGLE_PLAY" : "DAPP_STORE";
            string forbidden = isGooglePlay ? "DAPP_STORE" : "GOOGLE_PLAY";

            return CommandLineScriptingDefines()
                .Where(value => !string.Equals(value, forbidden, StringComparison.Ordinal))
                // SOLANA_SDK is package-generated, not a valid Play artifact stamp. Explicitly
                // forwarding it would defeat the isolation gate even if supplied by a caller.
                .Where(value => !isGooglePlay || !string.Equals(value, "SOLANA_SDK", StringComparison.Ordinal))
                .Append(wanted)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] CommandLineScriptingDefines()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "-extraScriptingDefines", StringComparison.OrdinalIgnoreCase))
                    continue;

                return args[i + 1]
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// WO-1124. Assert that <c>ServerData/Android/catalog_&lt;bundleVersion&gt;.bin</c> exists after
        /// the content build. This is the check that would have caught the shipped defect: every
        /// other marker in the chain (COMPILE_GATE_OK, APK_OK, R2_PUSH_OK) was green while the APK
        /// carried content the CDN did not host, because none of them ever named a platform.
        /// Exits the editor with a NAMED error rather than returning quietly — a build that cannot
        /// prove its own content must not reach a device.
        /// </summary>
        private static bool AssertAndroidCatalogForThisBuild()
        {
            string version = PlayerSettings.bundleVersion;
            string expected = Path.GetFullPath(Path.Combine("ServerData", "Android", $"catalog_{version}.bin"));

            if (File.Exists(expected))
            {
                Debug.Log($"[AndroidBuild] ANDROID_CATALOG_OK — {expected}");
                return true;
            }

            // Name what IS there. "Missing" alone sends the reader hunting; the sibling listing
            // usually names the wrong-platform folder outright.
            string dir = Path.GetFullPath(Path.Combine("ServerData", "Android"));
            string siblings = Directory.Exists(dir)
                ? string.Join(", ", Directory.GetFiles(dir, "catalog_*.bin").Select(Path.GetFileName))
                : "(ServerData/Android does not exist)";

            Debug.LogError($"[AndroidBuild] ABORTED — ANDROID_CATALOG_MISSING. This APK is stamped " +
                           $"'{version}' and will request 'Android/catalog_{version}.bin' at launch, but that file " +
                           $"was not produced. Present instead: {siblings}. The content was built for another " +
                           "platform or not at all; shipping this APK means no buildings and no enemies (WO-1124).");
            EditorApplication.Exit(1);
            return false;
        }

        /// <summary>
        /// Applies the Seeker-correct PlayerSettings via the supported APIs.
        /// Idempotent — re-running re-asserts every value.
        /// </summary>
        private static void ApplyAndroidPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);

            // IL2CPP is required for ARM64; the Mono backend cannot target ARM64
            // on Android, which the Seeker requires.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // ARM64-only. The Seeker is ARM64; building ARMv7 alongside doubles
            // the APK size for no benefit.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Large-screen / foldable readiness (Play Console, 2026-09-08): do not lock the
            // player activity to landscape. AutoRotation with every direction enabled makes
            // Unity emit an unrestricted orientation contract, while the generated Unity 6
            // GameActivity remains resizeable. The UI already derives its layout from the live
            // canvas and safe area, so tablets, desktop windows and fold posture changes may
            // resize without Android letterboxing the game.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;

            // Min SDK 26 (Android 8) — the Seeker ships with Android 13 (API 33),
            // and 26 is the modern floor for IL2CPP / 64-bit Play Store policy.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // ⛔ TARGET SDK IS PINNED, NOT AUTO (store-readiness audit 2026-08-19).
            // This read `AndroidApiLevelAuto`, which resolves to the HIGHEST SDK INSTALLED ON THE
            // BUILD MACHINE — so the shipped targetSdkVersion was a property of whoever built it,
            // not of the project, and could not be known by reading the repo. Two machines building
            // the same commit could submit two different declared targets, and a store listing's
            // compatibility claims derive from this number.
            // 36 is what Auto has been resolving to here (the APK installed on the Seeker
            // 2026-08-18 reports `targetSdk=36` under `dumpsys package`), so pinning it changes
            // NOTHING about today's binary — it only makes today's binary reproducible.
            // Raise this deliberately when a store policy floor moves; never return it to Auto.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;

            // RELEASE SIGNING (owner 2026-07-16 — testers must be able to UPDATE IN PLACE, which
            // needs a STABLE signature across builds). Read the keystore + passwords from a
            // GITIGNORED keystore.properties at the project root (never committed); if absent, fall
            // back to debug signing so a fresh clone / CI still builds. Passwords are set in-memory
            // for this batchmode session only — do NOT AssetDatabase.SaveAssets() them into
            // ProjectSettings.asset (that would leak the secret into git).
            ApplyReleaseSigning();

            // Every build must carry a DISTINCT, increasing version or tester builds are
            // indistinguishable (see ApplyVersionStamp).
            ApplyVersionStamp();

            Debug.Log($"[AndroidBuild] PlayerSettings: id={PackageId}, IL2CPP, ARM64, minSdk=26, " +
                      "resizeable activity + unrestricted auto-rotation.");
        }

        /// <summary>
        /// Stamps a UNIQUE, MONOTONIC version on every APK.
        /// </summary>
        /// <remarks>
        /// Captured 2026-08-05, distributing the 08-05 build: Firebase App Distribution replied
        /// <c>"re-uploaded already existing release 1.0 (1)"</c> — versionName/versionCode had never
        /// been set, so every tester build overwrote the SAME release and a tester could not tell
        /// one build from the next. Android also refuses an install whose versionCode goes
        /// backwards, so a fixed code is a latent update failure too.
        ///
        /// Scheme: versionCode = minutes elapsed since 2026-01-01 UTC. Monotonic by construction,
        /// STATELESS (no counter file to keep in sync across the owner's two machines — the drift
        /// class that caused the magenta-terrain and 4-of-41-models incidents), and int-safe for
        /// ~4000 years. versionName pairs the human-readable date with that same code.
        ///
        /// Bonus, deliberate: <c>bundleVersion</c> is what <c>Application.version</c> returns, which
        /// feeds <c>WebTrace._buildId</c> and the bug-report <c>app_version</c> column. Those have
        /// been reporting a constant "1.0" for every build, which is exactly why a magenta preview
        /// and a healthy prod were indistinguishable in the trace DB (2026-07-15). This closes that
        /// too.
        /// </remarks>
        private static void ApplyVersionStamp()
        {
            var epoch = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            System.DateTime now = System.DateTime.UtcNow;

            int code = (int)(now - epoch).TotalMinutes;
            if (code <= 0) code = 1; // clock skew / pre-epoch guard — never emit 0 or negative.

            string name = $"{now:yyyy.MM.dd}.{code}";

            PlayerSettings.Android.bundleVersionCode = code;
            PlayerSettings.bundleVersion = name;

            Debug.Log($"[AndroidBuild] VERSION: name={name} code={code} " +
                      "(monotonic — distinct App Distribution release per build).");
        }

        private static void ApplyReleaseSigning()
        {
            string propsPath = Path.Combine(Directory.GetCurrentDirectory(), "keystore.properties");
            if (!File.Exists(propsPath))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[AndroidBuild] keystore.properties not found — DEBUG signing (testers can't update in place).");
                return;
            }

            var kv = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var raw in File.ReadAllLines(propsPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            string ksPath, alias, storePass, keyPass;
            if (!kv.TryGetValue("keystore.path", out ksPath) || !File.Exists(ksPath) ||
                !kv.TryGetValue("keystore.alias", out alias) ||
                !kv.TryGetValue("keystore.storepass", out storePass) ||
                !kv.TryGetValue("keystore.keypass", out keyPass))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[AndroidBuild] keystore.properties incomplete/keystore missing — DEBUG signing.");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ksPath;
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = keyPass;
            Debug.Log($"[AndroidBuild] RELEASE signing: keystore='{Path.GetFileName(ksPath)}' alias='{alias}' (stable signature for tester updates).");
        }
    }
}
