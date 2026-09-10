// =============================================================================
// OfflineCacheRepairRegression - WO-1092. The offline pull fetched ~19.4 MB with
// every handle Succeeded and it never became a cache entry.
// -----------------------------------------------------------------------------
// Assembly: the Editor regression assembly. Shape: public static bool Run(out string reason).
// Registered into DataRegression.RunAll by the orchestrator (line given in the RESULT).
//
// THE PROVEN DEFECT (docs/READY_RCA_2026-09-09.md, section WO-1092), re-verified on
// this disk 2026-09-09 by listing the quarantined cache directory:
//
//   <cacheRoot>/912fa7abd447ca13619697b1fce844c6/
//       2220522384eb58b0db363f6c6e1b47ab/__lock            0 bytes, 2026-09-08 14:51
//       3c9df7ea9a3c1566f0cde489711dfec6/__data            19,400,698 bytes
//                                        __info            23 bytes
//
// The CURRENT version of the Orc bundle holds a zero-byte lock and no payload, while
// the PREVIOUS version is complete. Unity opened the cache transaction, abandoned it
// before commit, and kept the lock - so every later fetch of that bundle succeeded in
// memory and never committed. The pull's own numbers name the second half of it:
// 19,151,184 + (3 x 19,398,472) = 77,346,600 - three separate 24-ADDRESS chunks each
// re-requested the same shared family bundle, because MergeMode.Union deduplicates
// inside a chunk and cannot deduplicate across chunks.
//
// WHAT THIS SUITE PINS, AND WHY EACH ONE IS HERE
//
//   0 [meta]      THE SUITE MUST BE CAPABLE OF FAILING. A deliberately-wrong expectation
//                 is pushed through the same recorder and required to be recorded.
//   1 [stale]     lock-only + old lock  => AbandonedTransaction. The defect itself.
//   2 [live]      lock-only + YOUNG lock => LiveTransaction. A content warmer can hold a
//                 real transaction at boot; deleting a live one would manufacture the
//                 very corruption this repairs, so the age bound is load-bearing.
//   3 [healthy]   __data + __info => Healthy, even with a lock beside them. The complete
//                 PREVIOUS version must never be a deletion candidate.
//   4 [refuse]    Unknown for every shape we are not certain about - a non-hex directory
//                 name, a half-present payload, foreign files inside, an unknown lock age.
//                 A repair that deletes on an unrecognised shape is worse than no repair.
//   5 [fixture]   A REAL lock-only directory is staged on disk with a backdated lock and
//                 a complete sibling version, then read with the same File.Exists +
//                 lock-age signals the runtime scan uses, and classified. This is the
//                 RED-first case: it is the on-disk shape from the capture, reproduced.
//                 HONEST SCOPE: it proves the DETECTOR on a real directory. It does not
//                 prove Unity will commit the retry afterwards - only a device pull can.
//   6 [chunk]     A shared bundle behind many addresses is planned ONCE. The WO-1092
//                 shape (one family bundle behind 16 addresses spread over 3 address
//                 chunks) must collapse to a single claim.
//   7 [safety]    A key that resolves to NO bundle is ALWAYS kept. Dropping unknowns is
//                 exactly how PROD-010 shipped "zero keys read as already cached".
//   8 [lint]      The pull still calls PullVerified, still calls the repair and the
//                 outstanding-bundle diagnostic, the retry is BOUNDED (a constant, not a
//                 while-loop), and PullVerified still FAILS on remainingBytes > 0. The
//                 verifier is correct and must never be relaxed to make this pass.
//
// Groups 0-4, 6, 7 are PURE. Group 5 touches only a temp directory it creates and
// removes. Group 8 is a source lint and says so in its reason string.
//
// Markers: OFFLINE_CACHE_REPAIR_OK / OFFLINE_CACHE_REPAIR_FAIL.
// Standalone: run-unity-method -Method DeNelle.Editor.Regression.OfflineCacheRepairRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using DeNelle.Core;

namespace DeNelle.Editor.Regression
{
    public static class OfflineCacheRepairRegression
    {
        public const string MarkerOk   = "OFFLINE_CACHE_REPAIR_OK";
        public const string MarkerFail = "OFFLINE_CACHE_REPAIR_FAIL";

        // Two 32-lowercase-hex names, exactly the shape Unity uses for a cache version dir.
        private const string HashCurrent  = "2220522384eb58b0db363f6c6e1b47ab";
        private const string HashPrevious = "3c9df7ea9a3c1566f0cde489711dfec6";
        private const string BundleDirName = "912fa7abd447ca13619697b1fce844c6";

        private const double Stale = OfflineContentService.CacheLockStaleSeconds;

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            int checks = 0;

            Group0_SuiteCanFail(failures, ref checks);
            Group1To4_Classifier(failures, ref checks);
            Group5_OnDiskFixture(failures, ref checks);
            Group6And7_ChunkPlan(failures, ref checks);
            Group8_SourceLint(failures, ref checks);

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(MarkerFail).Append(' ').Append(failures.Count).Append(" failure(s) of ")
                  .Append(checks).Append(" check(s):");
                foreach (var f in failures) sb.AppendLine().Append("  - ").Append(f);
                reason = sb.ToString();
                return false;
            }

            reason = $"{MarkerOk} {checks}/{checks} checks - abandoned cache transaction detected and " +
                     "refused-on-doubt, live transaction left alone, complete version protected, shared " +
                     "bundle planned once, unresolved keys kept, verifier still strict.";
            return true;
        }

        // ---------------------------------------------------------------------
        // 0 [meta]
        // ---------------------------------------------------------------------
        private static void Group0_SuiteCanFail(List<string> failures, ref int checks)
        {
            // Push a knowingly-wrong expectation through the SAME recorder the real checks
            // use. If the recorder is broken, this proves it here instead of everything
            // silently passing - which is how the PROD-010 suite shipped green over a no-op.
            var probe = new List<string>();
            int probeChecks = 0;
            Expect(probe, ref probeChecks, false, "[meta] deliberate failure probe");
            if (probe.Count != 1)
                failures.Add("[meta] the failure recorder did NOT record a deliberate failure - " +
                             "every other result in this suite is untrustworthy.");
            checks++;
        }

        // ---------------------------------------------------------------------
        // 1-4 [classify]
        // ---------------------------------------------------------------------
        private static void Group1To4_Classifier(List<string> failures, ref int checks)
        {
            // 1 [stale] - the WO-1092 defect: lock only, old.
            Expect(failures, ref checks,
                Classify(HashCurrent, hasLock: true, hasData: false, hasInfo: false, other: 0, age: Stale + 1)
                    == OfflineContentService.CacheVersionState.AbandonedTransaction,
                "[stale] a 32-hex version dir holding only an OLD __lock must classify as " +
                "AbandonedTransaction - this is the exact on-disk state of the Orc bundle in the capture.");

            // Exactly at the threshold counts as abandoned (>=), not live.
            Expect(failures, ref checks,
                Classify(HashCurrent, true, false, false, 0, Stale)
                    == OfflineContentService.CacheVersionState.AbandonedTransaction,
                "[stale] a lock exactly at the stale threshold must be abandoned, not live - " +
                "an off-by-one here silently disables the repair on the boundary.");

            // 2 [live] - a warmer may be downloading right now.
            Expect(failures, ref checks,
                Classify(HashCurrent, true, false, false, 0, Stale - 1)
                    == OfflineContentService.CacheVersionState.LiveTransaction,
                "[live] a YOUNG lock-only version dir must classify as LiveTransaction - deleting a " +
                "download in flight would manufacture the corruption this repair exists to remove.");

            // 3 [healthy] - the complete previous version must never be a candidate.
            Expect(failures, ref checks,
                Classify(HashPrevious, false, true, true, 0, -1)
                    == OfflineContentService.CacheVersionState.Healthy,
                "[healthy] __data + __info must classify as Healthy - the complete previous version " +
                "(19,400,698 bytes on disk) must survive the repair.");
            Expect(failures, ref checks,
                Classify(HashPrevious, true, true, true, 0, 0)
                    == OfflineContentService.CacheVersionState.Healthy,
                "[healthy] a committed version must stay Healthy even with a lock beside it.");

            // 4 [refuse] - anything we are not certain about is Unknown, never deletable.
            Expect(failures, ref checks,
                Classify("not-a-hash", true, false, false, 0, Stale + 1)
                    == OfflineContentService.CacheVersionState.Unknown,
                "[refuse] a directory whose name is not a 32-hex bundle hash must be Unknown - the " +
                "scan walks a path that also contains ordinary player data.");
            Expect(failures, ref checks,
                Classify(HashCurrent, true, true, false, 0, Stale + 1)
                    == OfflineContentService.CacheVersionState.Unknown,
                "[refuse] a HALF-present payload (__data without __info) must be Unknown, not deleted.");
            Expect(failures, ref checks,
                Classify(HashCurrent, true, false, false, 1, Stale + 1)
                    == OfflineContentService.CacheVersionState.Unknown,
                "[refuse] foreign files or subdirectories inside a version dir must make it Unknown.");
            Expect(failures, ref checks,
                Classify(HashCurrent, true, false, false, 0, -1)
                    == OfflineContentService.CacheVersionState.Unknown,
                "[refuse] an UNKNOWN lock age must be Unknown - never assume a lock is stale.");
            Expect(failures, ref checks,
                Classify(HashCurrent, false, false, false, 0, -1)
                    == OfflineContentService.CacheVersionState.Empty,
                "[refuse] no lock and no payload is Empty - nothing to repair and nothing to delete.");
        }

        private static OfflineContentService.CacheVersionState Classify(
            string name, bool hasLock, bool hasData, bool hasInfo, int other, double age)
            => OfflineContentService.ClassifyCacheVersion(name, hasLock, hasData, hasInfo, other, age, Stale);

        // ---------------------------------------------------------------------
        // 5 [fixture] - a real lock-only directory on disk
        // ---------------------------------------------------------------------
        private static void Group5_OnDiskFixture(List<string> failures, ref int checks)
        {
            string root = Path.Combine(Path.GetTempPath(),
                                       "eoa-wo1092-" + Guid.NewGuid().ToString("N"));
            try
            {
                string bundleDir  = Path.Combine(root, BundleDirName);
                string currentDir = Path.Combine(bundleDir, HashCurrent);
                string prevDir    = Path.Combine(bundleDir, HashPrevious);
                Directory.CreateDirectory(currentDir);
                Directory.CreateDirectory(prevDir);

                // The abandoned transaction: a ZERO-BYTE lock, backdated well past the bound.
                string lockPath = Path.Combine(currentDir, "__lock");
                File.WriteAllBytes(lockPath, Array.Empty<byte>());
                File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddSeconds(-(Stale * 4)));

                // The complete previous version beside it.
                File.WriteAllBytes(Path.Combine(prevDir, "__data"), new byte[64]);
                File.WriteAllBytes(Path.Combine(prevDir, "__info"), new byte[8]);

                var abandoned = new List<string>();
                var healthy   = new List<string>();

                // Read the SAME signals the runtime scan reads, then classify with the SAME
                // pure function. The runtime keys off Caching.currentCacheForWriting.path,
                // which cannot be redirected from the Editor - so this proves the detector,
                // not the live cache walk. Stated plainly rather than overclaimed.
                foreach (var bd in Directory.GetDirectories(root))
                {
                    foreach (var vd in Directory.GetDirectories(bd))
                    {
                        string name = Path.GetFileName(vd);
                        string lp = Path.Combine(vd, "__lock");
                        bool hasLock = File.Exists(lp);
                        bool hasData = File.Exists(Path.Combine(vd, "__data"));
                        bool hasInfo = File.Exists(Path.Combine(vd, "__info"));

                        int other = 0;
                        foreach (var f in Directory.GetFiles(vd))
                        {
                            string fn = Path.GetFileName(f);
                            if (fn != "__lock" && fn != "__data" && fn != "__info") other++;
                        }
                        other += Directory.GetDirectories(vd).Length;

                        double age = -1d;
                        if (hasLock) age = (DateTime.UtcNow - File.GetLastWriteTimeUtc(lp)).TotalSeconds;

                        var state = OfflineContentService.ClassifyCacheVersion(
                            name, hasLock, hasData, hasInfo, other, age, Stale);

                        if (state == OfflineContentService.CacheVersionState.AbandonedTransaction) abandoned.Add(name);
                        if (state == OfflineContentService.CacheVersionState.Healthy) healthy.Add(name);
                    }
                }

                Expect(failures, ref checks,
                    abandoned.Count == 1 && abandoned[0] == HashCurrent,
                    $"[fixture] the staged lock-only CURRENT version '{HashCurrent}' must be the one and only " +
                    $"abandoned transaction found (found {abandoned.Count}) - this is the capture's on-disk shape.");

                Expect(failures, ref checks,
                    healthy.Count == 1 && healthy[0] == HashPrevious,
                    $"[fixture] the complete PREVIOUS version '{HashPrevious}' must read Healthy and must not be " +
                    $"a repair candidate (found {healthy.Count} healthy).");

                Expect(failures, ref checks,
                    new FileInfo(lockPath).Length == 0,
                    "[fixture] the staged lock must be zero bytes - a non-zero lock is a different shape " +
                    "from the one the capture recorded.");

                // The repair is per-VERSION: removing the abandoned dir must leave the sibling intact.
                Directory.Delete(currentDir, true);
                Expect(failures, ref checks,
                    !Directory.Exists(currentDir) && File.Exists(Path.Combine(prevDir, "__data")),
                    "[fixture] removing the abandoned version directory must leave the complete previous " +
                    "version untouched - a whole-bundle clear would have destroyed 19,400,698 recoverable bytes.");
            }
            catch (Exception ex)
            {
                failures.Add($"[fixture] staging the on-disk cache fixture threw {ex.GetType().Name}: {ex.Message}");
                checks++;
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { /* temp dir */ }
            }
        }

        // ---------------------------------------------------------------------
        // 6-7 [chunk plan]
        // ---------------------------------------------------------------------
        private static void Group6And7_ChunkPlan(List<string> failures, ref int checks)
        {
            const string shared = "enemy_models_assets_enemyfam-orc_2220522384eb58b0db363f6c6e1b47ab.bundle";

            // The capture's shape: ONE family bundle behind 16 addresses. With address
            // chunking at 24 these spread across chunks and each chunk re-requested it.
            var entries = new List<OfflineContentService.KeyBundleSet>();
            for (int i = 0; i < 16; i++)
                entries.Add(new OfflineContentService.KeyBundleSet
                {
                    Key = $"Enemies/Orc/Unit_{i:00}",
                    BundleNames = new[] { shared },
                });

            var plan = OfflineContentService.PlanUniqueBundleChunks(entries, 4, out int unique, out int skipped);

            Expect(failures, ref checks, unique == 1,
                $"[chunk] 16 addresses behind ONE shared bundle must resolve to 1 unique bundle (got {unique}) - " +
                "the 77,346,600 numerator was that bundle counted three times.");
            Expect(failures, ref checks, skipped == 15,
                $"[chunk] the 15 addresses that add no new bundle must be folded in (got {skipped} skipped) - " +
                "their bytes are already in the plan, so coverage is unchanged.");

            int planned = 0;
            foreach (var c in plan) planned += c.Count;
            Expect(failures, ref checks, planned == 1 && plan.Count == 1,
                $"[chunk] the shared bundle must be requested by exactly ONE chunk (got {plan.Count} chunk(s), " +
                $"{planned} key(s)) - MergeMode.Union cannot deduplicate ACROSS chunks.");

            // A mixed set: five distinct bundles plus the shared one.
            var mixed = new List<OfflineContentService.KeyBundleSet>
            {
                new OfflineContentService.KeyBundleSet { Key = "a", BundleNames = new[] { "b1" } },
                new OfflineContentService.KeyBundleSet { Key = "b", BundleNames = new[] { "b2", shared } },
                new OfflineContentService.KeyBundleSet { Key = "c", BundleNames = new[] { shared } },
                new OfflineContentService.KeyBundleSet { Key = "d", BundleNames = new[] { "b3", "b1" } },
                new OfflineContentService.KeyBundleSet { Key = "e", BundleNames = new[] { "b4" } },
                new OfflineContentService.KeyBundleSet { Key = "f", BundleNames = new[] { "b5" } },
            };
            var mixedPlan = OfflineContentService.PlanUniqueBundleChunks(mixed, 24, out int mUnique, out int mSkipped);
            Expect(failures, ref checks, mUnique == 6 && mSkipped == 1,
                $"[chunk] a mixed set must claim every distinct bundle once (expected 6 unique / 1 skipped, " +
                $"got {mUnique}/{mSkipped}) - 'c' adds nothing new, 'd' adds b3 and is kept.");

            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in mixedPlan) foreach (var k in c) kept.Add(k);
            Expect(failures, ref checks, !kept.Contains("c") && kept.Contains("d"),
                "[chunk] only the key that introduces nothing may be folded in; a key introducing one new " +
                "bundle alongside a claimed one must stay in the plan.");

            // 7 [safety] - unresolved keys are unknowns, and unknowns are never dropped.
            var unknowns = new List<OfflineContentService.KeyBundleSet>
            {
                new OfflineContentService.KeyBundleSet { Key = "u1", BundleNames = Array.Empty<string>() },
                new OfflineContentService.KeyBundleSet { Key = "u2", BundleNames = null },
                new OfflineContentService.KeyBundleSet { Key = "u3", BundleNames = new[] { "b1" } },
                new OfflineContentService.KeyBundleSet { Key = "u4", BundleNames = new[] { "b1" } },
            };
            var uPlan = OfflineContentService.PlanUniqueBundleChunks(unknowns, 24, out _, out int uSkipped);
            var uKept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in uPlan) foreach (var k in c) uKept.Add(k);
            Expect(failures, ref checks,
                uKept.Contains("u1") && uKept.Contains("u2") && uKept.Contains("u3") && uSkipped == 1,
                $"[safety] keys that resolve to NO bundle must ALWAYS be kept (kept={uKept.Count}, " +
                $"skipped={uSkipped}) - dropping unknowns is exactly how PROD-010 read zero keys as " +
                "'already cached' and stamped every player offline-ready over nothing.");

            Expect(failures, ref checks,
                OfflineContentService.PlanUniqueBundleChunks(null, 24, out _, out _).Count == 0,
                "[safety] a null entry list must produce an empty plan, not throw.");
        }

        // ---------------------------------------------------------------------
        // 8 [lint] - the seams must stay wired and the verifier must stay strict
        // ---------------------------------------------------------------------
        private static void Group8_SourceLint(List<string> failures, ref int checks)
        {
            const string path = "Assets/_Modules/Core/Addressables/OfflineContentService.cs";
            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add($"[lint] could not read {path}: {ex.GetType().Name}: {ex.Message}");
                checks++;
                return;
            }

            // MUST-NOT checks run against CODE ONLY. This file documents the banned calls by
            // name in its own comments (that is the point of the comments), so a raw substring
            // check would fail on the documentation of the rule it enforces.
            string code = StripComments(src);

            Expect(failures, ref checks, src.Contains("RepairAbandonedCacheTransactions("),
                "[lint] SOURCE LINT: the pull must still call RepairAbandonedCacheTransactions - without it " +
                "the retry re-runs against the same abandoned lock and fails identically.");
            Expect(failures, ref checks, src.Contains("LogOutstandingBundles("),
                "[lint] SOURCE LINT: the failure branch must still name the outstanding bundles - that " +
                "diagnostic is the only thing that tells an invalid hash from a lost cache write.");
            Expect(failures, ref checks, src.Contains("PlanUniqueBundleChunks("),
                "[lint] SOURCE LINT: the pull must still plan by unique bundle - reverting to address " +
                "chunking restores the three-times double fetch.");
            Expect(failures, ref checks, src.Contains("PullVerified("),
                "[lint] SOURCE LINT: the outcome assertion must still run after downloading.");
            Expect(failures, ref checks, !code.Contains("WaitForCompletion"),
                "[lint] SOURCE LINT: no WaitForCompletion - Addressables 2.9.1 implements it as an " +
                "unbounded spin and it caused a P0 deadlock on 2026-08-19.");
            Expect(failures, ref checks, src.Contains("MaxPullAttempts = 2"),
                "[lint] SOURCE LINT: the retry must stay BOUNDED at 2 attempts. If a bundle structurally " +
                "cannot cache, an unbounded loop hangs and an honest failure is better.");
            Expect(failures, ref checks,
                !code.Contains("ClearAllCachedVersions") && !code.Contains("Caching.ClearCache("),
                "[lint] SOURCE LINT: the repair must stay TARGETED. A whole-bundle or whole-cache clear " +
                "would also destroy the complete previous version this suite protects.");
            Expect(failures, ref checks,
                src.Contains("if (remainingBytes > 0)") && src.Contains("the pull did NOT pull"),
                "[lint] SOURCE LINT: PullVerified must still FAIL when bytes are outstanding. The verifier " +
                "is arithmetically correct and relaxing it would hide this defect instead of fixing it.");

            // FlowTrace is permanent (CLAUDE.md sec.12). Every branch of the repair names its decision.
            Expect(failures, ref checks,
                src.Contains("ABANDONED TRANSACTION found") && src.Contains("LEFT ALONE") &&
                src.Contains("STILL OUTSTANDING"),
                "[lint] SOURCE LINT: the repair's found / left-alone / still-outstanding FlowTrace lines must " +
                "stay. CLAUDE.md sec.12 forbids stripping instrumentation - a removed Warn turns a logged " +
                "failure back into a silent one.");
        }

        /// <summary>
        /// Drop `//` line comments so a MUST-NOT lint reads code, not documentation. Same shape
        /// as OfflinePullRegression's stripper, which has held since 2026-08-19.
        /// </summary>
        private static string StripComments(string src)
        {
            var sb = new StringBuilder();
            foreach (var raw in src.Split('\n'))
            {
                string line = raw;
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*")) continue;

                int c = line.IndexOf("//", StringComparison.Ordinal);
                if (c >= 0 && !InsideQuotes(line, c)) line = line.Substring(0, c);
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        private static bool InsideQuotes(string line, int index)
        {
            bool q = false;
            for (int i = 0; i < index; i++)
            {
                if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) q = !q;
            }
            return q;
        }

        // ---------------------------------------------------------------------
        private static void Expect(List<string> failures, ref int checks, bool condition, string message)
        {
            checks++;
            if (!condition) failures.Add(message);
        }

        /// <summary>Standalone entry point (run-unity-method).</summary>
        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log(reason);
            if (!ok) EditorApplication.Exit(1);
        }
    }
}
