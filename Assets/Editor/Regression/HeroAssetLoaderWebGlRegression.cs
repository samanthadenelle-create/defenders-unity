// =============================================================================
// HeroAssetLoaderWebGlRegression - the hero art path can be served WITHOUT a
// synchronous Addressables load. Markers: HERO_WEBGL_LOAD_OK / HERO_WEBGL_LOAD_FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Register in DeNelle.Editor.DataRegression.RunAll.
// Standalone: run-unity-method -Method DeNelle.Editor.Regression.HeroAssetLoaderWebGlRegression.RunAll
//
// THE DEFECT THIS PINS (WO-1701; captured 2026-09-10, Seeker + Pi Browser, web_trace
// session wt-56a6e825d88, build 2026.09.10.364108):
//   21:54:23Z [Flow:HeroPrewarm] 'Mage' art downloaded and cached on attempt 1 - Ready.
//   21:54:24Z error: [Flow:HeroAssets] Addressables resolve 'Heroes/Mage' (GameObject)
//             FAILED: Exception: WebGLPlayer does not support synchronous Addressable
//             loading. Please do not use WaitForCompletion on the WebGLPlayer platform.
//   21:54:24Z [Flow:HeroBody] class=Mage slug=Mage - kicking Blink base load
//             'hero/base/HumanMale'.
// The owner's Mage entered the town as the naked HumanMale base body. R2 parity was
// GREEN for WebGL that afternoon (R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL
// objects=279) and the bundle had been cached one second earlier, so this was never a
// missing push: Addressables refuses WaitForCompletion on WebGLPlayer regardless of cache
// state, Guard.Try swallowed the throw, and the null fell through to a Resources copy the
// remote migration had deleted.
//
// WHY HALF OF THIS IS A SOURCE LINT. The property is "no code path on the WebGL player
// reaches the synchronous load". A runtime test cannot demonstrate that from an Editor
// process - UNITY_WEBGL is not defined here, so the very branch under test is compiled
// IN. The only oracle that is both cheap and honest is: the call is not reachable off
// that guard. Case 1 proves the absence structurally; case 2 proves the branch that must
// serve WebGL instead actually works.
//
// IT LINTS CODE ONLY. Comments and string-literal contents are blanked (via
// ComposedDungeonRunRegression.StripCommentsAndStrings) before any match, because the
// files under guard carry long tombstone comments naming WaitForCompletion precisely so
// the next author understands the ban. A rule that matched its own tombstone would punish
// exactly the documentation CLAUDE.md sections 12/15 demand. Case 3 proves the detector
// flags the known-bad shape and clears the fixed one, so this gate is falsifiable rather
// than decorative.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Proves (1) every WaitForCompletion in HeroAssetLoader.cs sits under a !UNITY_WEBGL
    /// guard and the warm-cache probe precedes the Addressables load, (2) a warm-cached
    /// address is served from the dictionary with no Addressables call, (3) the detector in
    /// (1) actually discriminates. Returns true (summary) / false (detail); never throws.
    /// </summary>
    public static class HeroAssetLoaderWebGlRegression
    {
        public const string MarkerOk   = "HERO_WEBGL_LOAD_OK";
        public const string MarkerFail = "HERO_WEBGL_LOAD_FAIL";

        /// <summary>The blocking call. Assembled from parts so this constant is not itself a
        /// literal that a future, dumber grep of this repo would flag.</summary>
        private const string BlockingCall = "WaitFor" + "Completion";

        /// <summary>The preprocessor symbol whose ABSENCE must gate the blocking call.</summary>
        private const string WebGlGuard = "!UNITY_WEBGL";

        private const string LoaderSrc    = "Assets/_Modules/Core/Addressables/HeroAssetLoader.cs";
        private const string PrewarmerSrc = "Assets/_Modules/Core/Addressables/HeroContentPrewarmer.cs";

        /// <summary>The warm-cache probe that must appear BEFORE any Addressables load.</summary>
        private const string WarmProbe = "HeroContentPrewarmer.TryGetWarm";

        /// <summary>The Addressables load call the probe must precede.</summary>
        private const string AddrLoad = "Addressables.LoadAssetAsync";

        /// <summary>A slug that exists in no catalog and in no Resources folder. The whole
        /// argument of case 2 rests on that: nothing but the warm dictionary can answer it.</summary>
        private const string ProbeSlug = "__wo1701_warm_probe__";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log(MarkerOk + " - " + reason);
            else Debug.LogError(MarkerFail + " - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- HERO ART LOADS WITHOUT A SYNCHRONOUS ADDRESSABLES CALL (WO-1701) ---");

            try
            {
                Case1_BlockingCallIsGuarded(failures, log);
                Case2_WarmCacheServesTheLoader(failures, log);
                Case3_DetectorDiscriminates(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[suite] threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures.ToArray());
                Debug.LogError(log.ToString());
                return false;
            }

            reason = "HeroAssetLoader reaches " + BlockingCall + "() only under a " + WebGlGuard +
                     " guard, probes HeroContentPrewarmer's warm cache before it touches " +
                     "Addressables at all, and a warm-cached address is served straight out of " +
                     "that dictionary - so the WebGL player, which throws on any synchronous " +
                     "Addressable load, has a path to the real hero body instead of the Blink " +
                     "base placeholder.";
            Debug.Log(log.ToString());
            return true;
        }

        // =====================================================================
        //  Case 1 - the blocking call is unreachable on the WebGL player
        // =====================================================================
        private static void Case1_BlockingCallIsGuarded(List<string> failures, StringBuilder log)
        {
            string loaderRaw = ReadOrNull(LoaderSrc);
            if (loaderRaw == null)
            {
                failures.Add("[guard] " + LoaderSrc + " is missing - the hero seam has moved or been " +
                             "deleted, and this gate cannot protect a path it cannot find. Re-point " +
                             "LoaderSrc at whatever now owns hero resolution.");
                return;
            }

            string loader = Code(loaderRaw);

            int total, unguarded;
            List<int> unguardedLines = ScanBlockingCalls(loader, out total, out unguarded);

            if (unguarded > 0)
            {
                failures.Add("[guard] " + LoaderSrc + " calls " + BlockingCall + "() OUTSIDE a " +
                             WebGlGuard + " block, at code-line(s) " + Join(unguardedLines) + " (" +
                             unguarded + " of " + total + " occurrence(s)). On WebGLPlayer that call " +
                             "THROWS - 'WebGLPlayer does not support synchronous Addressable loading' - " +
                             "even when the bundle is already cached, which is exactly what shipped on " +
                             "2026-09-10: the throw was swallowed by Guard.Try, the null fell through to " +
                             "a Resources copy that no longer exists, and every WebGL player got the " +
                             "naked Blink base body. Serve the asset from HeroContentPrewarmer's warm " +
                             "cache and keep the synchronous branch behind #if " + WebGlGuard + ".");
            }
            else
            {
                log.AppendLine("OK: " + total + " " + BlockingCall + "() occurrence(s) in " + LoaderSrc +
                               ", all under a " + WebGlGuard + " guard" +
                               (total == 0 ? " (zero occurrences is strictly stronger)" : ""));
            }

            // The warm probe must come FIRST, not merely exist. A probe placed after the
            // Addressables load would satisfy a naive "contains" lint while changing nothing.
            int warmAt = loader.IndexOf(WarmProbe, StringComparison.Ordinal);
            int loadAt = loader.IndexOf(AddrLoad, StringComparison.Ordinal);

            if (warmAt < 0)
            {
                failures.Add("[order] " + LoaderSrc + " no longer calls " + WarmProbe + ". The warm " +
                             "dictionary is the ONLY branch a WebGL player can take for a remote hero; " +
                             "without it the loader is back to the 2026-09-10 behaviour.");
            }
            else if (loadAt >= 0 && warmAt > loadAt)
            {
                failures.Add("[order] " + LoaderSrc + " calls " + AddrLoad + " (code-offset " + loadAt +
                             ") BEFORE " + WarmProbe + " (code-offset " + warmAt + "). The Addressables " +
                             "path would win every resolve and the warm cache would be dead weight - the " +
                             "same shape as the WO-1187 defect where Resources won over Addressables.");
            }
            else
            {
                log.AppendLine("OK: " + WarmProbe + " is probed at code-offset " + warmAt +
                               ", before any " + AddrLoad + " (offset " +
                               (loadAt < 0 ? "absent" : loadAt.ToString()) + ")");
            }

            // The prewarmer is the async half. If it ever blocks, the fix has been undone from
            // the other end: a coroutine that WaitForCompletion()s is not asynchronous.
            string prewarmRaw = ReadOrNull(PrewarmerSrc);
            if (prewarmRaw == null)
            {
                failures.Add("[guard] " + PrewarmerSrc + " is missing - the warm cache the loader " +
                             "depends on has no owner.");
            }
            else
            {
                string prewarmer = Code(prewarmRaw);
                if (prewarmer.IndexOf(BlockingCall, StringComparison.Ordinal) >= 0)
                    failures.Add("[guard] " + PrewarmerSrc + " calls " + BlockingCall + "(). The prewarm " +
                                 "runs as a coroutine precisely so the load is asynchronous on every " +
                                 "platform; a blocking call here reintroduces the WebGL throw one layer up.");
                else if (prewarmer.IndexOf("LoadAssetAsync", StringComparison.Ordinal) < 0)
                    failures.Add("[guard] " + PrewarmerSrc + " never calls LoadAssetAsync. Downloading a " +
                                 "bundle is NOT loading an asset - that distinction is the whole of " +
                                 "WO-1701. Without the load, the warm cache is always empty and the " +
                                 "loader falls through on WebGL exactly as before.");
                else
                    log.AppendLine("OK: " + PrewarmerSrc + " loads assets asynchronously and never blocks");
            }
        }

        // =====================================================================
        //  Case 2 - a warm-cached address is served from the dictionary
        // =====================================================================
        private static void Case2_WarmCacheServesTheLoader(List<string> failures, StringBuilder log)
        {
            string address = HeroAssetLoader.AddressFor(ProbeSlug);
            if (string.IsNullOrEmpty(address))
            {
                failures.Add("[warm] HeroAssetLoader.AddressFor returned nothing for a non-empty slug - " +
                             "the address builder the warm pass and the loader share is broken.");
                return;
            }

            GameObject probe = null;
            try
            {
                // 1. THE PREMISE. Neither of the loader's other two sources can answer this
                //    address: it is in no Addressables catalog and there is no Resources asset at
                //    it. That is what licenses the inference in step 3 - if the loader returns the
                //    seeded reference afterwards, the warm dictionary is the ONLY thing that could
                //    have produced it, so no Addressables call was needed.
                //
                //    Both halves are probed DIRECTLY rather than by calling LoadHeroPrefab, on
                //    purpose: a cold call through the loader ends in FlowTrace.Fail, which routes
                //    to Debug.LogError (FlowTrace.cs:169-172). A suite must not manufacture a red
                //    error line in the middle of a green run - the gates are judged by grepping a
                //    fresh log, and a deliberate LogError is indistinguishable from a real one.
                bool inCatalog = HeroAssetLoader.AddressableRegistered<GameObject>(address);
                GameObject inResources = Resources.Load<GameObject>(address);
                if (inCatalog || inResources != null)
                {
                    failures.Add("[warm] the throwaway probe address '" + address + "' is answerable " +
                                 "without the warm cache (Addressables registered=" + inCatalog +
                                 ", Resources=" + (inResources == null ? "null" : "'" + inResources.name + "'") +
                                 "). Something in the project now owns that address, so this case can no " +
                                 "longer prove the dictionary was the source. Change ProbeSlug to a name " +
                                 "nothing owns.");
                    return;
                }

                probe = new GameObject("wo1701_warm_probe");
                probe.hideFlags = HideFlags.HideAndDontSave;

                // 2. SEED via the public test seam, under exactly the key the warm pass uses.
                HeroContentPrewarmer.SeedWarmForTests(typeof(GameObject), address, probe);

                if (!HeroContentPrewarmer.TryGetWarm<GameObject>(address, out GameObject direct) ||
                    !ReferenceEquals(direct, probe))
                {
                    failures.Add("[warm] HeroContentPrewarmer.TryGetWarm did not return the object " +
                                 "SeedWarmForTests had just parked at '" + address + "'. The warm cache " +
                                 "cannot serve the loader if it cannot serve itself.");
                    return;
                }

                // 3. THE ASSERTION. The public loader entry point returns the seeded reference.
                GameObject hot = HeroAssetLoader.LoadHeroPrefab(ProbeSlug);
                if (!ReferenceEquals(hot, probe))
                {
                    failures.Add("[warm] HeroAssetLoader.LoadHeroPrefab did NOT return the warm-cached " +
                                 "object for '" + address + "' (got " +
                                 (hot == null ? "null" : "'" + hot.name + "'") + "). The address resolves " +
                                 "nowhere else in this project - it is in no catalog and has no Resources copy, " +
                                 "checked one moment earlier - so " +
                                 "the loader is not reading the warm dictionary first, and a WebGL player " +
                                 "has no way to reach its hero body.");
                    return;
                }

                // 4. TYPE SCOPING. The prefab and the animator controller deliberately share one
                //    address, so a cache keyed by address alone would hand a GameObject to a
                //    caller asking for a controller.
                if (HeroContentPrewarmer.TryGetWarm<RuntimeAnimatorController>(address, out _))
                {
                    failures.Add("[warm] the warm cache answered a RuntimeAnimatorController request at '" +
                                 address + "' where only a GameObject was seeded. The key must include the " +
                                 "asset TYPE - the hero prefab and its controller share one address on " +
                                 "purpose and are told apart by type.");
                    return;
                }

                log.AppendLine("OK: '" + address + "' is unanswerable without the cache, and LoadHeroPrefab returned the " +
                               "warm-cached reference with no Addressables call, and the cache stayed " +
                               "type-scoped");
            }
            catch (Exception ex)
            {
                failures.Add("[warm] threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // Never leave scaffolding parked where a later suite could resolve a hero to it.
                try { HeroContentPrewarmer.ClearWarmForTests(); } catch { }
                if (probe != null)
                {
                    try { UnityEngine.Object.DestroyImmediate(probe); } catch { }
                }
            }

            if (HeroContentPrewarmer.TryGetWarm<GameObject>(address, out _))
                failures.Add("[warm] ClearWarmForTests left '" + address + "' in the warm cache. A test " +
                             "seed that outlives its case turns every later hero resolve into a lie.");
        }

        // =====================================================================
        //  Case 3 - the detector in case 1 actually discriminates
        // =====================================================================
        private static void Case3_DetectorDiscriminates(List<string> failures, StringBuilder log)
        {
            var cases = new List<OracleCase>
            {
                new OracleCase
                {
                    Name = "known-bad: the shipped 2026-09-10 shape, no guard at all",
                    ExpectUnguarded = true,
                    Source = "var handle = Addressables.LoadAssetAsync<T>(address);\n" +
                             "result = handle." + BlockingCall + "();",
                },
                new OracleCase
                {
                    Name = "clean: guarded by " + WebGlGuard,
                    ExpectUnguarded = false,
                    Source = "#if " + WebGlGuard + "\n" +
                             "result = handle." + BlockingCall + "();\n" +
                             "#endif",
                },
                new OracleCase
                {
                    Name = "clean: guarded by " + WebGlGuard + " || UNITY_EDITOR (the shipped guard)",
                    ExpectUnguarded = false,
                    Source = "#if " + WebGlGuard + " || UNITY_EDITOR\n" +
                             "result = handle." + BlockingCall + "();\n" +
                             "#endif",
                },
                new OracleCase
                {
                    Name = "known-bad: in the #else arm, i.e. the WebGL branch",
                    ExpectUnguarded = true,
                    Source = "#if " + WebGlGuard + "\n" +
                             "result = null;\n" +
                             "#else\n" +
                             "result = handle." + BlockingCall + "();\n" +
                             "#endif",
                },
                new OracleCase
                {
                    Name = "known-bad: guarded by an unrelated symbol",
                    ExpectUnguarded = true,
                    Source = "#if UNITY_STANDALONE\n" +
                             "result = handle." + BlockingCall + "();\n" +
                             "#endif",
                },
                new OracleCase
                {
                    Name = "clean: the call is named in a comment and in a string only",
                    ExpectUnguarded = false,
                    Source = "// never call " + BlockingCall + "() here\n" +
                             "FlowTrace.Warn(System, \"do not use " + BlockingCall + "\");",
                },
            };

            foreach (var c in cases)
            {
                int total, unguarded;
                ScanBlockingCalls(Code(c.Source), out total, out unguarded);
                bool flagged = unguarded > 0;
                if (flagged != c.ExpectUnguarded)
                    failures.Add("[oracle] the guard detector " + (flagged ? "FLAGGED" : "PASSED") +
                                 " a case it must " + (c.ExpectUnguarded ? "flag" : "pass") + ": " + c.Name +
                                 ". A gate that cannot tell the broken shape from the fixed one proves " +
                                 "nothing about either.");
            }

            log.AppendLine("OK: the guard detector discriminates across " + cases.Count +
                           " synthetic cases (unguarded, guarded, #else arm, wrong symbol, prose-only)");
        }

        private sealed class OracleCase
        {
            public string Name;
            public string Source;
            public bool ExpectUnguarded;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>
        /// Walk <paramref name="code"/> line by line, keeping a stack of preprocessor frames, and
        /// classify every occurrence of the blocking call as guarded or not. A frame counts as a
        /// guard when its condition text contains <see cref="WebGlGuard"/>; nesting is conjunctive,
        /// so ANY guarding frame on the stack means the code compiles only off the WebGL player.
        /// An <c>#else</c> arm is never a guard - the else of a !UNITY_WEBGL block IS the WebGL
        /// branch, and the else of anything unrelated is unrelated.
        /// <para>Input must already be comment- and string-blanked; preprocessor directives survive
        /// that pass unchanged, which is what makes this walk possible.</para>
        /// </summary>
        private static List<int> ScanBlockingCalls(string code, out int total, out int unguarded)
        {
            total = 0;
            unguarded = 0;
            var lines = new List<int>();
            if (string.IsNullOrEmpty(code)) return lines;

            var stack = new List<bool>();
            string[] rows = code.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].TrimStart();

                if (trimmed.StartsWith("#if", StringComparison.Ordinal))
                {
                    stack.Add(trimmed.IndexOf(WebGlGuard, StringComparison.Ordinal) >= 0);
                    continue;
                }
                if (trimmed.StartsWith("#elif", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack[stack.Count - 1] = trimmed.IndexOf(WebGlGuard, StringComparison.Ordinal) >= 0;
                    continue;
                }
                if (trimmed.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (stack.Count > 0) stack[stack.Count - 1] = false;
                    continue;
                }
                if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                int at = 0;
                while ((at = rows[i].IndexOf(BlockingCall, at, StringComparison.Ordinal)) >= 0)
                {
                    total++;
                    if (!AnyGuard(stack))
                    {
                        unguarded++;
                        lines.Add(i + 1);
                    }
                    at += BlockingCall.Length;
                }
            }

            return lines;
        }

        private static bool AnyGuard(List<bool> stack)
        {
            for (int i = 0; i < stack.Count; i++)
                if (stack[i]) return true;
            return false;
        }

        private static string Join(List<int> values)
        {
            if (values == null || values.Count == 0) return "(none)";
            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(values[i]);
            }
            return sb.ToString();
        }

        /// <summary>Comment- and string-blanked source. Shared with the sibling source-lints so a
        /// tombstone comment naming the banned call can never satisfy or trip a rule.</summary>
        private static string Code(string source) =>
            ComposedDungeonRunRegression.StripCommentsAndStrings(source ?? string.Empty);

        private static string ReadOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }
    }
}
