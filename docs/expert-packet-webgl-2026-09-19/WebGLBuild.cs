// =============================================================================
// WebGLBuild - produces a WebGL (browser) build for playtest delivery. Run
// headless:
//
//   Unity.exe -batchmode -quit -buildTarget WebGL -projectPath <proj> \
//             -executeMethod DeNelle.Editor.WebGLBuild.BuildWebGL
//
// Output: <proj>/Builds/WebGL/ (index.html + Build/ + StreamingAssets/).
// Mirrors DesktopBuild.cs (WO-09 section 2.1). Applies the WebGL Player Settings
// the WO calls for: IL2CPP (mandatory for WebGL), Brotli compression, 512 MB
// memory, no exception support (release), data caching, minimal managed
// stripping.
//
// NOTE: this script COMPILES without the "WebGL Build Support" editor module
// (the PlayerSettings.WebGL / BuildTarget.WebGL APIs live in UnityEditor.dll),
// but BuildPipeline.BuildPlayer(WebGL) will FAIL at runtime until that module is
// installed via Unity Hub. So this is ready-to-build infrastructure; the actual
// build is gated on the module install (see WORK_ORDER_09_webgl_build.RESULT.md).
// =============================================================================

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>One-shot WebGL build entry point. Builds the enabled Build-Settings
    /// scenes, in order, to <c>Builds/WebGL/</c>.</summary>
    public static class WebGLBuild
    {
        private const string OutputDir = "Builds/WebGL";

        [MenuItem("Defenders/Build/WebGL Player")]
        public static void BuildWebGL()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[WebGLBuild] No enabled scenes in Build Settings - aborting.");
                EditorApplication.Exit(1);
                return;
            }

            // --- WebGL Player Settings (WO-09 section 2.1) ---
            // WO-126: itch.io is a static host and does NOT send the
            // `Content-Encoding: br` header, so Brotli-compressed payloads
            // (.wasm.br/.js.br/.data.br) fail to load ("undefined at ...js.br").
            // Pass `-noBrotli` on the batchmode command line to ship uncompressed
            // files instead. Vercel keeps Brotli (default).
            bool noBrotli = System.Environment.GetCommandLineArgs()
                .Any(a => a.Equals("-noBrotli", System.StringComparison.OrdinalIgnoreCase));

            // Pass `-debugExceptions` to ship FULL C# exception stack traces to the
            // browser console — turns the opaque "Uncaught exception from main loop /
            // _JS_CallAsLongAsNoExceptionsSeen" into the real exception type + stack so
            // a runtime crash can be diagnosed. Costs size/perf; OFF for the ship build.
            bool debugExceptions = System.Environment.GetCommandLineArgs()
                .Any(a => a.Equals("-debugExceptions", System.StringComparison.OrdinalIgnoreCase));

            // WO-408 DEFECT 2: BuildOptions.Development was hardcoded on the ship
            // build, which DISABLES Unity's code/data compression entirely (no .br,
            // oversized .wasm/.data). The default ship path must NOT be a dev build.
            // Pass `-devBuild` on the batchmode command line to explicitly opt into a
            // Development player (DevTools QA panel + readable stack traces) when
            // verifying; otherwise the default is BuildOptions.None (compressed ship).
            bool devBuild = System.Environment.GetCommandLineArgs()
                .Any(a => a.Equals("-devBuild", System.StringComparison.OrdinalIgnoreCase));

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            // itch.io + any static host: ALWAYS Brotli compression + decompressionFallback=true.
            // WO-126 / commit 73796b7 (tested on itch): itch is a static host and does NOT send
            // `Content-Encoding: br`, so a plain Brotli payload won't load — BUT shipping the
            // payload UNCOMPRESSED (-noBrotli) makes WebGL.data ~223MB, which EXCEEDS itch's
            // per-file limit and itch REJECTS it (-> empty/blank itch page). The fix is
            // decompressionFallback=true: Unity's loader decompresses the .br IN-JS, so Brotli
            // payloads load everywhere with NO server header AND stay ~half size. A later WO-408
            // refactor inadvertently dropped the fallback + flipped back to -noBrotli (with an
            // untested "emits uncompressed anyway" note) — that regressed the itch build; restored
            // here. -noBrotli is deprecated/ignored.
            if (noBrotli)
                Debug.LogWarning("[WebGLBuild] -noBrotli is deprecated/ignored: uncompressed WebGL.data exceeds itch's per-file limit (itch rejects it). Using Brotli + decompressionFallback instead.");
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            Debug.Log("[WebGLBuild] compressionFormat = Brotli + decompressionFallback = true (itch-safe, no Content-Encoding header needed, ~half size)");
            PlayerSettings.WebGL.memorySize = 512;
            // RELEASE was WebGLExceptionSupport.None — but with None, WebGL try/catch
            // does NOT catch, so ANY thrown exception (e.g. a catalog's File.ReadAllText
            // on the browser's nonexistent filesystem) HALTS the content -> black screen
            // at boot (DEF-124). ExplicitlyThrownExceptionsOnly makes try/catch work so the
            // boot degrades gracefully instead of aborting; small size cost, working build.
            PlayerSettings.WebGL.exceptionSupport = debugExceptions
                ? WebGLExceptionSupport.FullWithStacktrace
                : WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            Debug.Log($"[WebGLBuild] exceptionSupport = {PlayerSettings.WebGL.exceptionSupport}");
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            PlayerSettings.runInBackground = false;

            Debug.Log($"[WebGLBuild] buildOptions = {(devBuild ? "Development (-devBuild: uncompressed QA build)" : "None (compressed ship build)")}");

            string dir = Path.GetFullPath(OutputDir);
            Directory.CreateDirectory(dir);

            Debug.Log($"[WebGLBuild] Building {scenes.Length} scene(s) -> {dir}");
            foreach (string s in scenes)
                Debug.Log($"[WebGLBuild]   scene: {s}");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = dir,   // WebGL outputs a DIRECTORY, not a single file
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                // WO-408 DEFECT 2: default to None (compressed ship build). Development
                // disables all code/data compression (no .br, oversized payload) and is
                // now opt-in via `-devBuild` for verification builds only.
                options = devBuild ? BuildOptions.Development : BuildOptions.None,
            };

            // WO-1315 (2026-09-02): SWITCH THE ACTIVE TARGET BEFORE BUILDING CONTENT.
            //
            // Addressables builds for the ACTIVE editor target, not for the target in
            // BuildPlayerOptions. This path called EnsureBuilt with NO target and NO prior
            // switch, so it inherited whatever the editor happened to be on. Measured on
            // 2026-09-02, straight after a Windows player build:
            //
            //   ADDRESSABLES_CONTENT_OK 751 locations :: WebGLBuild target=StandaloneWindows64
            //     (-> Library/com.unity.addressables/aa/Windows/settings.json)
            //
            // ServerData/WebGL was therefore never regenerated, and the WebGL player shipped
            // against a THREE-DAY-OLD catalog while every marker stayed green. That is
            // occurrence FIVE of the CLAUDE.md sec.16 class and the exact shape of WO-1124,
            // which fixed this same hole in AndroidBuild and left the WebGL path open.
            //
            // The switch lives HERE rather than in build-webgl.ps1 for the reason AndroidBuild
            // records: the whole failure class is assuming a human step. In-method, it holds
            // for the menu item, batchmode, CI and the ship chain alike. No-op when already on
            // WebGL, so the fast path stays fast.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log($"[WebGLBuild] active target is '{EditorUserBuildSettings.activeBuildTarget}' - " +
                          "switching to WebGL BEFORE the content build (WO-1315).");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[WebGLBuild] ABORTED - could not switch the active build target to WebGL. " +
                                   "Building content now would produce the wrong platform's bundles (WO-1315/WO-1124).");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            // WO-974: build Addressables content EXPLICITLY (see AddressablesContentBuild).
            // Matters most here: the web payload is served remotely, so an absent catalog is
            // invisible until a player loads the deployed build and nothing resolves.
            // The target is now passed EXPLICITLY (WO-1315) so EnsureBuilt can refuse a
            // mismatch rather than silently building for whatever is active.
            if (!AddressablesContentBuild.EnsureBuilt("WebGLBuild", BuildTarget.WebGL))
            {
                Debug.LogError("[WebGLBuild] ABORTED - Addressables content build failed (WO-974/WO-1315).");
                EditorApplication.Exit(1);
                return;
            }

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[WebGLBuild] SUCCEEDED - {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime}.");
            }
            else
            {
                Debug.LogError($"[WebGLBuild] FAILED - result={summary.result}, errors={summary.totalErrors}. " +
                               "If result=Failed with no errors, the 'WebGL Build Support' module is likely not installed " +
                               "(install via Unity Hub -> Installs -> 6000.4.8f1 -> Add Modules).");
                EditorApplication.Exit(1);
            }
        }
    }
}
