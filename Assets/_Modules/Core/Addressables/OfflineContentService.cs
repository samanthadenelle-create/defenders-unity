// =============================================================================
// OfflineContentService — PROD-010. Opt-in offline mode: pull the whole remote
// content set ONCE over Wi-Fi, then fall back to the local cache whenever the
// network is gone.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core (Core/Addressables). References Addressables only — no
// Village/HUD types, so the seam stays usable from any module.
//
// OWNER SPEC, 2026-08-19 verbatim:
//   "I want prod ten done as an overnight activity where it's gonna opt in for an
//    offline mode. If they do the offline mode, they are going to... on first time,
//    they're gonna have a CDN pull of everything that they need. and that does still
//    need Wi Fi to download initially. But then after that, we need to somehow tell
//    it to default to local if it can't get to Wi Fi."
//
// So three obligations, in this order:
//   1. OPT-IN. Nothing downloads until the player says yes.
//   2. FIRST-RUN PULL. On yes, fetch every remote dependency while online.
//   3. LOCAL FALLBACK. Afterwards, a launch with no network must use the cached
//      bundles instead of failing.
//
// =============================================================================
// ⛔ THE HISTORY THIS FILE IS PAYING FOR — READ IT BEFORE CHANGING THE KEY SET.
// -----------------------------------------------------------------------------
// PROD-010 shipped on 2026-08-19 and DID NOT WORK. The content set was
//     ContentKeys = { "Structure_Art", "Enemy_Art" }
// which are Addressable GROUP names. A group name is NOT an Addressables key — only
// ADDRESSES and LABELS are, and the only labels this project authors are `default`,
// `Locale` and `Locale-en` (AddressableAssetSettings.asset, m_LabelTable). So every
// GetDownloadSizeAsync matched nothing and returned 0, the prompt said "Everything is
// already downloaded", the player was stamped offline-ready, and NOT ONE BYTE WAS
// EVER FETCHED. The owner's assessment was fair: "i would not be asking that of the
// villiage if you had just completed prod 10".
//
// Commit dd6c9732a put in a floor (enumerate real addresses from the loaded catalog;
// keep -1 "cannot measure" distinct from 0 "genuinely cached"). This file finishes it,
// and the finishing move is the one thing whose absence caused the defect:
//
//   ⭐ THE PULL MUST PROVE IT PULLED. After DownloadAllForOffline runs, the remaining
//      download size FOR THE SAME KEY SET is measured again and must be 0. If it is
//      not, the pull FAILED — it says so and the player is NOT stamped offline-ready.
//      A success report that is never checked against an outcome is how a no-op ships
//      as a feature.
//
// =============================================================================
// HOW THE CONTENT SET IS CHOSEN — COMPLETENESS BY CONSTRUCTION, NOT BY PREFIX.
// -----------------------------------------------------------------------------
// The interim fix enumerated addresses under the prefixes "Structures/" and "Enemies/".
// Audited 2026-08-20 against AddressableAssetsData, those prefixes are CORRECT TODAY
// ONLY BY COINCIDENCE, and would be wrong tomorrow:
//
//   group                            addresses  LoadPath profile var  remote?
//   Structure_Art                    35         Remote.LoadPath       YES  (Structures/…)
//   Enemy_Art                        78         Remote.LoadPath       YES  (Enemies/…)
//   Gear                             427        Local.LoadPath        no   (gear/…)
//   Dungeon                          1          Local.LoadPath        no   (dungeon/…)
//   Localization-* (x3)              3          Local.LoadPath        no
//   Default Local Group              0          Local.LoadPath        no
//   (Remote.LoadPath = https://pub-…r2.dev/[BuildTarget])
//
// So the prefixes happen to cover exactly the remote set — but they encode a GUESS
// about naming, and the owner ruled TODAY that enemies re-pack PER FAMILY and
// structures PER ASSET. A re-pack renames and multiplies GROUPS freely, and the next
// remote group that does not begin with "Structures/" or "Enemies/" would be dropped
// SILENTLY — the exact failure mode above, wearing a different hat.
//
// Therefore the runtime set is EVERY ADDRESS IN THE LOADED CATALOG, minus an explicit
// exclusion list (empty today). This is safe and cheap because Addressables answers the
// remote/local question itself: a LOCAL bundle contributes 0 bytes to
// GetDownloadSizeAsync and DownloadDependenciesAsync on it is a no-op. Nothing is
// double-downloaded — bundles are deduplicated by MergeMode.Union. The size the player
// is shown is therefore the true remote set whatever the groups are called.
//
// The second net lives in the Editor: OfflinePullRegression walks the ACTUAL
// AddressableAssetSettings, finds every group whose LoadPath resolves to a remote URL,
// and asserts IsOfflineContentKey() accepts every address in it. If a re-pack ever
// produces a remote group this predicate would drop, the gate fails loudly at build
// time instead of the player finding out on a plane.
//
// ⛔ WHY (3) NEEDS CODE AT ALL, since bundles already cache.
// AddressableAssetSettings has m_DisableCatalogUpdateOnStart: 0 — the catalog is
// refreshed from the CDN at launch. That refresh THROWS or hangs with no network.
// Caching the bundles does not help if the step before them fails. So the fallback
// is not "use the cache" (Addressables does that already) — it is "do not let the
// catalog refresh take the launch down with it." That is what ResolveContentSource
// exists for, and it is the whole difference between a cached game that opens on a
// plane and one that does not.
//
// ⛔ WHY NOT JUST FLIP m_DisableCatalogUpdateOnStart TO 1.
// Because installed players adopt the new remote catalog at launch — that is how a
// shipped build learns about content we upload later. Disabling it globally freezes
// every existing install on the catalog it shipped with. The owner's CDN ruling
// ("lets keep the cdn") depends on that update path staying alive. We degrade on
// FAILURE instead of disabling the feature.
//
// ⛔ NO WaitForCompletion, ANYWHERE IN THIS FILE, EVER.
// A P0 deadlock was fixed on 2026-08-19 caused by exactly that call: the Addressables
// 2.9.1 implementation is `while (!InvokeWaitForCompletion()) { }` — no timeout, no
// exit. Every wait here is a coroutine yield.
//
// SIZE IS MEASURED, NEVER TYPED. GetDownloadSizeAsync answers in bytes for the real
// content set; the prompt shows that number. An earlier plan promised "10 seconds",
// which was true only while PROD-009 was going to shrink the download. The owner
// retired PROD-009 ("PROD 10 kills 10 and 09"), so the honest figure is the whole
// set (~88 MB when measured on 2026-08-19). Never promise seconds.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;

namespace DeNelle.Core
{
    /// <summary>Where the content for this launch is coming from.</summary>
    public enum ContentSource
    {
        /// <summary>Not decided yet this launch.</summary>
        Unknown = 0,
        /// <summary>Network reachable; the remote catalog was refreshed normally.</summary>
        Online = 1,
        /// <summary>No network. Running on the cached catalog + cached bundles.</summary>
        LocalCache = 2,
        /// <summary>No network AND nothing cached — the honest bad case.</summary>
        Unavailable = 3,
    }

    /// <summary>
    /// What a measured download size MEANS. Three states, never two — collapsing
    /// "cannot measure" into "nothing to download" is the PROD-010 defect itself.
    /// </summary>
    public enum OfflineSizeVerdict
    {
        /// <summary>We could not work out what to download. NEVER treat as ready.</summary>
        CannotMeasure = 0,
        /// <summary>Measured, and every byte is already cached.</summary>
        AlreadyCached = 1,
        /// <summary>Measured, and there are real bytes outstanding.</summary>
        NeedsDownload = 2,
    }

    /// <summary>
    /// PROD-010. Opt-in offline download + local fallback. Static, no scene authoring:
    /// call <see cref="ResolveContentSource"/> at boot and
    /// <see cref="DownloadAllForOffline(Action{float},Action{bool})"/> from the opt-in prompt.
    /// </summary>
    public static class OfflineContentService
    {
        private const string Sys = "OfflineContent";
        private const string KeyFirstRunInternetRequired = "offlineFirstRunInternetRequired";
        private const string KeyRetry = "offlineFirstRunRetry";

        /// <summary>Player said yes to offline mode (persisted).</summary>
        private const string PrefOptedIn = "offline.optedin";
        /// <summary>The full pull completed AND VERIFIED at least once (persisted).</summary>
        private const string PrefPulled  = "offline.pulled";
        /// <summary>bundleVersion the completed pull belongs to (content is per-build).</summary>
        private const string PrefPulledBuild = "offline.pulledbuild";

        /// <summary>
        /// How many addresses go into one DownloadDependenciesAsync call.
        ///
        /// NOT one giant call, and NOT one call per address:
        ///  - one giant call holds EVERY downloaded AssetBundle in memory until the whole
        ///    set finishes. On a set this size that is the content-warming memory strain the
        ///    project is currently digging itself out of.
        ///  - one call per address re-queries and re-weights constantly, and (the old bug)
        ///    forces progress to be faked as "keys done / keys total" because per-key byte
        ///    totals are not known up front.
        /// Chunks bound peak memory while still letting MergeMode.Union deduplicate the
        /// shared bundles inside a chunk. With the re-pack producing MANY small bundles this
        /// also means progress advances several times per chunk instead of in one jump.
        ///
        /// ⚠ RESIDUAL RISK, written down rather than hidden: catalog keys include LABELS as well
        /// as addresses, and a label expands to every entry carrying it. If someone ever applies
        /// a blanket label (e.g. `default`) to the whole remote set, the chunk containing that
        /// one key would download everything at once and the memory bound above stops binding.
        /// Correctness is unaffected — MergeMode.Union deduplicates, so nothing is fetched twice.
        ///
        /// Re-checked against the re-pack that LANDED 2026-08-20 (this note previously said "all
        /// 78 enemy + 35 structure entries carry `m_SerializedLabels: []`", which the re-pack made
        /// false the same day): enemy entries now each carry exactly ONE `enemyfam-*` label —
        /// `orc`, `hollow`, `shared`, `troll`, `bosses` — because Enemy_Art packs
        /// PackTogetherByLabel. Structure entries still carry none (PackSeparately). No entry
        /// carries a blanket `default` label. So the widest label expands to one family of ~16
        /// entries, comfortably inside a chunk, and the bound holds. If a blanket label is ever
        /// authored, add it to <see cref="ExcludedKeyPrefixes"/> — its members are already in the
        /// set by address, so excluding it loses no coverage.
        /// </summary>
        private const int ChunkSize = 24;

        /// <summary>
        /// Addresses the offline set deliberately EXCLUDES. Empty today, and that is the
        /// honest state: every non-remote group costs zero bytes anyway, so there is nothing
        /// to trim. It exists as the one named place to put a future optional/DLC prefix, so
        /// that a decision to skip content is WRITTEN DOWN rather than implied by a prefix
        /// list that silently forgot something.
        /// </summary>
        private static readonly string[] ExcludedKeyPrefixes = Array.Empty<string>();

        // =====================================================================
        //  Pure, testable decision logic
        //  (kept free of Addressables + coroutines so the Editor regression can
        //   assert BOTH DIRECTIONS without a loaded catalog or a play session)
        // =====================================================================

        /// <summary>
        /// Is this catalog key part of the offline content set?
        ///
        /// Rejects Addressables' GUID keys (every entry is registered under both its address
        /// and its 32-hex asset GUID; the GUID is a pure duplicate that would double the key
        /// list for no coverage) and anything under <see cref="ExcludedKeyPrefixes"/>.
        /// Everything else — addresses AND labels — is IN, by construction. See the file
        /// header for why this is a completeness decision and not laziness.
        /// </summary>
        public static bool IsOfflineContentKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (LooksLikeAssetGuid(key)) return false;
            for (int i = 0; i < ExcludedKeyPrefixes.Length; i++)
            {
                if (key.StartsWith(ExcludedKeyPrefixes[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>32 lowercase-hex characters — Unity's asset GUID shape.</summary>
        private static bool LooksLikeAssetGuid(string key)
        {
            if (key.Length != 32) return false;
            for (int i = 0; i < 32; i++)
            {
                char c = key[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>
        /// Turn (how many keys resolved, how many bytes were measured) into what the UI is
        /// allowed to say. THE WHOLE POINT is that zero-keys can never come out as
        /// <see cref="OfflineSizeVerdict.AlreadyCached"/>: on 2026-08-19 it did, and every
        /// player was told they were covered while nothing had been fetched.
        /// </summary>
        public static OfflineSizeVerdict ClassifySize(int keyCount, long measuredBytes)
        {
            if (keyCount <= 0) return OfflineSizeVerdict.CannotMeasure;   // ← the shipped defect
            if (measuredBytes < 0) return OfflineSizeVerdict.CannotMeasure;
            return measuredBytes == 0 ? OfflineSizeVerdict.AlreadyCached
                                      : OfflineSizeVerdict.NeedsDownload;
        }

        /// <summary>
        /// Did the pull ACTUALLY PULL? Called with the post-download re-measurement of the
        /// same key set. This is the assertion whose absence caused PROD-010 to ship broken:
        /// handles reporting Succeeded is NOT evidence that bytes landed — a key set that
        /// matches nothing succeeds instantly and downloads nothing.
        /// </summary>
        /// <param name="keyCount">Addresses the set resolved to. 0 = no basis for any claim.</param>
        /// <param name="allHandlesOk">Every download handle finished Succeeded.</param>
        /// <param name="remainingBytes">Re-measured outstanding bytes; negative = unmeasurable.</param>
        public static bool PullVerified(int keyCount, bool allHandlesOk, long remainingBytes, out string reason)
        {
            if (keyCount <= 0)
            {
                reason = "no addresses resolved for the offline set - nothing was fetched and there is " +
                         "no basis for reporting success";
                return false;
            }
            if (!allHandlesOk)
            {
                reason = "one or more download operations failed";
                return false;
            }
            if (remainingBytes < 0)
            {
                reason = "could not re-measure the set after downloading, so the pull is UNPROVEN " +
                         "- treated as failed rather than assumed good";
                return false;
            }
            if (remainingBytes > 0)
            {
                reason = $"downloads reported success but {remainingBytes} byte(s) are still outstanding " +
                         "for the same key set - the pull did NOT pull";
                return false;
            }
            reason = $"verified: {keyCount} address(es), 0 bytes outstanding after the pull";
            return true;
        }

        // =====================================================================
        //  WO-1092. The abandoned cache transaction, and the unique-bundle plan.
        // ---------------------------------------------------------------------
        //  PROVEN CAUSE (docs/READY_RCA_2026-09-09.md, section WO-1092): the Orc
        //  bundle `enemy_models_assets_enemyfam-orc_2220522384eb58b0db363f6c6e1b47ab
        //  .bundle` (19,398,472 bytes, VALID non-zero hash) has a CURRENT-version
        //  cache directory holding a single zero-byte `__lock` and no `__data` /
        //  `__info`, created 2026-09-08 14:51:27, while the PREVIOUS version
        //  `3c9df7ea9a3c1566f0cde489711dfec6` stays complete at 19,400,698 bytes.
        //  Unity opened that cache transaction, abandoned it before commit, kept the
        //  lock, and every later fetch of that bundle succeeded IN MEMORY and never
        //  committed. Verified by hand on this disk 2026-09-09:
        //      <cacheRoot>/912fa7abd447ca13619697b1fce844c6/
        //          2220522384eb58b0db363f6c6e1b47ab/__lock   (0 bytes)
        //          3c9df7ea9a3c1566f0cde489711dfec6/{__data,__info}
        //  so the on-disk shape is <root>/<bundle-name-dir>/<hash>/{__data,__info,__lock}
        //  and the VERSION directory is named by the bundle HASH. That is what makes a
        //  name-independent scan possible: the directory that names the bundle is an
        //  opaque hash, but the version directory underneath it is the catalog Hash,
        //  which the resolved locations DO carry.
        //
        //  ⛔ THE VERIFIER STAYS STRICT. PullVerified and MeasureDownloadSize above are
        //  arithmetically honest and are NOT touched by this fix - relaxing either one
        //  would hide the defect instead of repairing it.
        // =====================================================================

        /// <summary>What one on-disk cache VERSION directory is.</summary>
        public enum CacheVersionState
        {
            /// <summary>Shape we do not recognise. NEVER touched - the safe default.</summary>
            Unknown = 0,
            /// <summary>Committed: both `__data` and `__info` are present.</summary>
            Healthy = 1,
            /// <summary>No lock and no payload. Nothing to repair, nothing to delete.</summary>
            Empty = 2,
            /// <summary>Lock only, and YOUNG - a download may be in flight right now. Leave it.</summary>
            LiveTransaction = 3,
            /// <summary>Lock only, and OLD. The WO-1092 defect. Safe to remove.</summary>
            AbandonedTransaction = 4,
        }

        /// <summary>
        /// How old a lock-only version directory has to be before we are willing to call it
        /// abandoned rather than in flight. A content warmer (EnemyContentWarmer /
        /// StructureContentWarmer) can legitimately hold an open transaction at boot, and
        /// deleting a LIVE one would manufacture the very corruption this repairs. Five
        /// minutes is far longer than any single-bundle fetch in this content set (the
        /// largest bundle is ~19.4 MB) and far shorter than a session.
        /// </summary>
        public const double CacheLockStaleSeconds = 300d;

        /// <summary>
        /// How many passes the pull is allowed. TWO: one normal, one after the repair.
        /// Deliberately NOT a loop-until-success - if a bundle structurally cannot cache,
        /// an honest failure beats a hang (WO-1092 spec).
        /// </summary>
        private const int MaxPullAttempts = 2;
        /// <summary>How many outstanding bundles the failure diagnostic names before summarising.</summary>
        private const int DiagnosticBundleCap = 8;

        /// <summary>
        /// PURE. Classify one cache VERSION directory from its measured signals. Kept free of
        /// the filesystem so the Editor regression can pin BOTH the abandoned case and the
        /// live-transaction case without staging a real download.
        /// </summary>
        /// <param name="versionDirName">Directory name - must be the 32-hex bundle hash, or we refuse.</param>
        /// <param name="hasLock">A `__lock` file is present.</param>
        /// <param name="hasData">A `__data` file is present.</param>
        /// <param name="hasInfo">An `__info` file is present.</param>
        /// <param name="otherEntryCount">Anything else inside (files or subdirectories). Non-zero = refuse.</param>
        /// <param name="lockAgeSeconds">Age of the lock file. Negative = unknown = refuse to call it stale.</param>
        /// <param name="minStaleAgeSeconds">Threshold, normally <see cref="CacheLockStaleSeconds"/>.</param>
        public static CacheVersionState ClassifyCacheVersion(string versionDirName,
                                                             bool hasLock, bool hasData, bool hasInfo,
                                                             int otherEntryCount,
                                                             double lockAgeSeconds, double minStaleAgeSeconds)
        {
            // A committed version is healthy no matter what else sits beside it.
            if (hasData && hasInfo) return CacheVersionState.Healthy;
            // Only ever act on something shaped exactly like a Unity cache version dir.
            if (!LooksLikeAssetGuid(versionDirName)) return CacheVersionState.Unknown;
            if (!hasLock) return CacheVersionState.Empty;
            // Payload half-present, or foreign content: not a shape we are willing to delete.
            if (hasData || hasInfo || otherEntryCount > 0) return CacheVersionState.Unknown;
            if (lockAgeSeconds < 0) return CacheVersionState.Unknown;
            return lockAgeSeconds >= minStaleAgeSeconds
                ? CacheVersionState.AbandonedTransaction
                : CacheVersionState.LiveTransaction;
        }

        /// <summary>One catalog key and the remote bundles it depends on.</summary>
        public struct KeyBundleSet
        {
            public string Key;
            /// <summary>Bundle names this key pulls. EMPTY means "we could not resolve it" - never "none".</summary>
            public string[] BundleNames;
        }

        /// <summary>
        /// PURE. Plan the download chunks by UNIQUE BUNDLE instead of by address.
        ///
        /// WHY: the failed pull reported downloaded=77,346,600 against total=38,549,656.
        /// 19,151,184 + (3 x 19,398,472) = 77,346,600 exactly - three separate 24-address
        /// chunks each touched the SAME shared Orc family bundle. MergeMode.Union
        /// deduplicates WITHIN a chunk and cannot deduplicate ACROSS chunks, so a bundle
        /// shared by ~16 addresses is re-requested once per chunk that touches it.
        ///
        /// A key joins a chunk only if it introduces at least one bundle no earlier key has
        /// already claimed. A key whose bundles are ALL claimed is dropped: its bytes are
        /// already in the plan, so coverage is unchanged. A key with NO resolved bundles is
        /// ALWAYS kept - an unresolved key is an unknown, and dropping unknowns is how the
        /// PROD-010 "zero keys = already cached" defect was built.
        ///
        /// ⚠ HONEST LIMIT: key-based chunking cannot perfectly partition a dependency graph
        /// whose closures partially overlap - a later key can legitimately need one new
        /// bundle plus one already claimed. What it removes is the pathological case above,
        /// where a key set adds NOTHING new and re-requests a whole family bundle.
        /// </summary>
        public static List<List<string>> PlanUniqueBundleChunks(IList<KeyBundleSet> entries, int chunkSize,
                                                               out int uniqueBundles, out int skippedKeys)
        {
            var chunks = new List<List<string>>();
            uniqueBundles = 0;
            skippedKeys = 0;
            if (entries == null || entries.Count == 0) return chunks;
            if (chunkSize < 1) chunkSize = 1;

            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var current = new List<string>();

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (string.IsNullOrEmpty(e.Key)) continue;

                var bundles = e.BundleNames;
                if (bundles != null && bundles.Length > 0)
                {
                    bool introducesSomething = false;
                    for (int b = 0; b < bundles.Length; b++)
                    {
                        if (string.IsNullOrEmpty(bundles[b])) continue;
                        if (!claimed.Contains(bundles[b])) { introducesSomething = true; break; }
                    }
                    if (!introducesSomething) { skippedKeys++; continue; }
                    for (int b = 0; b < bundles.Length; b++)
                    {
                        if (string.IsNullOrEmpty(bundles[b])) continue;
                        if (claimed.Add(bundles[b])) uniqueBundles++;
                    }
                }

                current.Add(e.Key);
                if (current.Count >= chunkSize) { chunks.Add(current); current = new List<string>(); }
            }

            if (current.Count > 0) chunks.Add(current);
            return chunks;
        }

        // =====================================================================
        //  Key enumeration
        // =====================================================================

        /// <summary>
        /// Every key in the offline set, read from the loaded catalog. Empty is a MEANINGFUL
        /// answer and callers must treat it as "cannot size / cannot pull", never as
        /// "nothing to do" — that conflation is exactly the bug this replaced. Needs no
        /// network and cannot go stale against a group rename or a re-pack.
        /// </summary>
        public static List<string> CollectContentKeys()
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Guard.Try(Sys, "enumerate offline content addresses", () =>
            {
                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator?.Keys == null) continue;
                    foreach (var k in locator.Keys)
                    {
                        if (!(k is string s)) continue;
                        if (!IsOfflineContentKey(s)) continue;
                        if (seen.Add(s)) keys.Add(s);
                    }
                }
            });
            FlowTrace.Step(Sys, $"offline content set = {keys.Count} catalog key(s) " +
                                $"(locators={CountLocators()}). Local groups contribute 0 bytes by design.");
            return keys;
        }

        private static int CountLocators()
        {
            int n = 0;
            Guard.Try(Sys, "count locators", () => { foreach (var _ in Addressables.ResourceLocators) n++; });
            return n;
        }

        /// <summary>Resolved once per launch by <see cref="ResolveContentSource"/>.</summary>
        public static ContentSource Source { get; private set; } = ContentSource.Unknown;

        /// <summary>True when the player has opted into offline mode.</summary>
        public static bool OptedIn => PlayerPrefs.GetInt(PrefOptedIn, 0) == 1;

        /// <summary>
        /// True when a full pull has completed AND BEEN VERIFIED FOR THIS BUILD. Content is
        /// content-hashed per build, so a pull from the previous APK does not cover this one —
        /// treating it as covered is how a player who opted in still hits the network on a
        /// fresh install.
        ///
        /// ⚠ The version stamp alone is NOT sufficient and is not relied on alone: a REMOTE
        /// CATALOG UPDATE re-hashes bundles while Application.version never moves (that is the
        /// whole point of keeping m_DisableCatalogUpdateOnStart at 0, and the re-pack landing
        /// today changes every content hash). So <see cref="ResolveContentSource"/> RE-VERIFIES
        /// on every online launch and clears this stamp if bytes have appeared. The stamp says
        /// "verified at some point"; the boot check keeps it honest.
        /// </summary>
        public static bool PulledForThisBuild =>
            PlayerPrefs.GetInt(PrefPulled, 0) == 1 &&
            PlayerPrefs.GetString(PrefPulledBuild, "") == Application.version;

        /// <summary>Record the player's answer. Opting out never deletes an existing cache -
        /// the bytes are already paid for and deleting them helps nobody.</summary>
        public static void SetOptedIn(bool yes)
        {
            PlayerPrefs.SetInt(PrefOptedIn, yes ? 1 : 0);
            PlayerPrefs.Save();
            FlowTrace.Step(Sys, $"offline mode opt-in = {yes}");
        }

        /// <summary>
        /// Stamp the player offline-ready. PRIVATE ON PURPOSE: the only caller is the verified
        /// path in <see cref="DownloadAllForOffline(Action{float,long,long},Action{bool,string})"/>.
        /// The 2026-08-19 defect was reachable precisely because a UI could stamp this from a
        /// measurement it had not proven.
        /// </summary>
        private static void StampOfflineReady()
        {
            PlayerPrefs.SetInt(PrefPulled, 1);
            PlayerPrefs.SetString(PrefPulledBuild, Application.version);
            PlayerPrefs.SetInt(PrefOptedIn, 1);
            PlayerPrefs.Save();
        }

        private static void ClearOfflineReady(string why)
        {
            PlayerPrefs.SetInt(PrefPulled, 0);
            PlayerPrefs.Save();
            FlowTrace.Warn(Sys, $"offline-ready stamp CLEARED: {why}. The player will be offered the " +
                                "download again, which is the truthful state.");
        }

        // =====================================================================
        //  1. Boot: decide where content comes from, and NEVER let this throw
        // =====================================================================

        /// <summary>
        /// Decide this launch's <see cref="ContentSource"/> and, when the network is gone,
        /// keep the game on the cached catalog instead of failing the launch.
        ///
        /// <para>Call once at boot, before the first content load. Runs to completion even
        /// with no network - the failure path is the POINT of this method, not an edge case.</para>
        /// </summary>
        public static IEnumerator ResolveContentSource(Action<ContentSource> onDone = null)
        {
            using var _ = FlowTrace.Enter(Sys, "ResolveContentSource");

#if DEVELOPMENT_BUILD
            // The headed gate proof isolates one-scene locomotion geometry. Its runner may
            // execute in a network-restricted shell; do not let the intentional first-run
            // CDN barrier cover every evidence frame. This flag is absent from release builds.
            if (Array.Exists(Environment.GetCommandLineArgs(), a =>
                string.Equals(a, "-gateProofDir", StringComparison.OrdinalIgnoreCase)))
            {
                Source = ContentSource.Online;
                FlowTrace.Step(Sys, "headed gate proof -> native CDN barrier bypassed for capture isolation");
                onDone?.Invoke(Source);
                yield break;
            }
#endif

            // A WebGL player has already reached its web host and downloaded the shipped
            // catalog before this coroutine can run. Browser cache lifetime is controlled by
            // the browser, not by the native opt-in/offline contract below. Re-probing the
            // remote catalog here can fail independently (CORS, cache policy, or an optional
            // catalog URL) and used to turn a running web game into the impossible modal
            // "An internet connection is required". Use the shipped catalog and let each
            // Addressables request stream/cache normally.
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                Source = ContentSource.Online;
                FlowTrace.Step(Sys, "WebGL player -> ONLINE via shipped catalog (native offline gate skipped)");
                onDone?.Invoke(Source);
                yield break;
            }

            bool reachable = Application.internetReachability != NetworkReachability.NotReachable;
            FlowTrace.Step(Sys, $"reachability={Application.internetReachability} optedIn={OptedIn} " +
                                $"pulledForThisBuild={PulledForThisBuild} build={Application.version}");

            if (!reachable)
            {
                // OFFLINE. Do NOT touch the catalog - a refresh here is what hangs a
                // no-network launch. Cached bundles are usable without it.
                Source = PulledForThisBuild ? ContentSource.LocalCache : ContentSource.Unavailable;
                if (Source == ContentSource.LocalCache)
                    FlowTrace.Step(Sys, "no network -> LOCAL CACHE (full pull completed for this build; " +
                                        "catalog refresh deliberately SKIPPED so it cannot stall the launch)");
                else
                {
                    FlowTrace.Warn(Sys, "no network AND no completed pull for this build -> content UNAVAILABLE. " +
                                        "Buildings and enemies will not resolve; the player must be told plainly, " +
                                        "never left staring at an empty town (PROD-012).");
                    LoadingOverlay.ShowConnectionRequired(
                        HudStrings.Get(KeyFirstRunInternetRequired), HudStrings.Get(KeyRetry));
                }
                onDone?.Invoke(Source);
                yield break;
            }

            // ONLINE. Let Addressables refresh the catalog, but survive a failure: a
            // reachable radio is not a reachable CDN (captive portals, DNS, an R2 outage).
            AsyncOperationHandle<List<string>> check = default;
            bool started = false;
            bool catalogUsable = false;
            try
            {
                check = Addressables.CheckForCatalogUpdates(false);
                started = true;
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, $"CheckForCatalogUpdates threw immediately ({ex.GetType().Name}: {ex.Message}) " +
                                    "-> falling back to the cached catalog.");
            }

            if (started)
            {
                while (!check.IsDone) yield return null;

                if (check.Status == AsyncOperationStatus.Succeeded && check.Result != null && check.Result.Count > 0)
                {
                    FlowTrace.Step(Sys, $"catalog updates available: {check.Result.Count}");
                    AsyncOperationHandle<List<IResourceLocator>> upd = default;
                    bool updStarted = false;
                    try { upd = Addressables.UpdateCatalogs(check.Result, false); updStarted = true; }
                    catch (Exception ex)
                    {
                        FlowTrace.Warn(Sys, $"UpdateCatalogs threw ({ex.Message}) -> cached catalog kept.");
                    }
                    if (updStarted)
                    {
                        while (!upd.IsDone) yield return null;
                        catalogUsable = upd.Status == AsyncOperationStatus.Succeeded;
                        FlowTrace.Step(Sys, $"UpdateCatalogs {(upd.Status == AsyncOperationStatus.Succeeded ? "OK" : "FAILED - cached catalog kept")}");
                        Addressables.Release(upd);
                    }
                }
                else if (check.Status == AsyncOperationStatus.Succeeded)
                {
                    // A completed CDN/catalog probe with no update proves the shipped catalog
                    // is current and usable. Radio reachability alone proves nothing.
                    catalogUsable = true;
                }
                else if (check.Status != AsyncOperationStatus.Succeeded)
                {
                    FlowTrace.Warn(Sys, "CheckForCatalogUpdates FAILED with a reachable network " +
                                        "(captive portal / DNS / CDN outage) -> cached catalog kept, launch continues.");
                }

                Addressables.Release(check);
            }

            if (!catalogUsable)
            {
                Source = PulledForThisBuild ? ContentSource.LocalCache : ContentSource.Unavailable;
                if (Source == ContentSource.Unavailable)
                {
                    FlowTrace.Warn(Sys, "network is reachable but the content catalog could not be proven usable " +
                                        "-> first-run content UNAVAILABLE (captive portal / DNS / CDN outage)");
                    LoadingOverlay.ShowConnectionRequired(
                        HudStrings.Get(KeyFirstRunInternetRequired), HudStrings.Get(KeyRetry));
                }
                else
                {
                    FlowTrace.Warn(Sys, "catalog probe failed -> using the verified per-build local cache");
                }
                onDone?.Invoke(Source);
                yield break;
            }

            Source = ContentSource.Online;
            FlowTrace.Step(Sys, "content source = ONLINE");

            // RE-VERIFY THE OFFLINE PROMISE while we are online and it is free to check.
            // A remote catalog update (or today's per-family/per-asset re-pack) re-hashes
            // bundles without moving Application.version, so a stamp taken before it is no
            // longer true. Measuring costs no network traffic - it is catalog maths plus
            // cache lookups - and it is the difference between a promise and a claim.
            if (PulledForThisBuild) yield return VerifyCachedSetStillComplete();

            onDone?.Invoke(Source);
        }

        /// <summary>
        /// Measure the set again on an online launch; clear the offline-ready stamp if bytes
        /// have appeared. NEVER clears on an unmeasurable answer — an unknown must not cost a
        /// player a download they already paid for, the same way it must not earn them a
        /// promise they have not.
        /// </summary>
        private static IEnumerator VerifyCachedSetStillComplete()
        {
            yield return EnsureInitialized();

            var keys = CollectContentKeys();
            if (keys.Count == 0)
            {
                FlowTrace.Warn(Sys, "offline re-verify SKIPPED - the catalog resolved 0 keys, so the answer " +
                                    "would be an unknown, not a verdict. Stamp left exactly as it was.");
                yield break;
            }

            long bytes = -1; bool measured = false;
            yield return MeasureDownloadSize(keys, (b, ok) => { bytes = b; measured = ok; });

            if (!measured)
            {
                FlowTrace.Warn(Sys, "offline re-verify could not measure the set - stamp left alone.");
                yield break;
            }

            if (bytes > 0)
                ClearOfflineReady($"{bytes} byte(s) of the offline set are no longer cached " +
                                  "(new remote catalog / re-packed bundles)");
            else
                FlowTrace.Step(Sys, $"offline re-verify OK - {keys.Count} key(s), 0 bytes outstanding.");
        }

        // =====================================================================
        //  2. Measurement
        // =====================================================================

        /// <summary>
        /// Total bytes still to download for the whole content set.
        /// <para>0 = genuinely fully cached. <b>-1 = COULD NOT MEASURE</b>, which is a different
        /// answer and must never be rendered as "already downloaded".</para>
        /// </summary>
        public static IEnumerator GetDownloadSize(Action<long> onSize)
            => GetDownloadSize((bytes, keyCount) => onSize?.Invoke(bytes));

        /// <summary>
        /// Measurement that also reports HOW MANY KEYS the set resolved to, so the caller can
        /// run <see cref="ClassifySize"/> for itself. The key count is not decoration: a byte
        /// total of 0 means "already cached" only when the set actually resolved to something,
        /// and a UI that cannot see the difference is the UI that shipped the lie.
        /// </summary>
        public static IEnumerator GetDownloadSize(Action<long, int> onSize)
        {
            yield return EnsureInitialized();

            var keys = CollectContentKeys();
            if (keys.Count == 0)
            {
                // NOT "nothing to download" — "we could not work out what to download". Reporting
                // -1 keeps those two apart, because the caller stamps the player offline-ready on
                // a 0 and must never do that on an unknown. This is the exact conflation that made
                // the group-name bug silent.
                FlowTrace.Fail(Sys, "offline size UNKNOWN - the catalog resolved 0 keys. Either Addressables " +
                                    "has not loaded a catalog yet or the content set is empty; reporting -1 so " +
                                    "the caller cannot mistake this for 'already cached'.");
                onSize?.Invoke(-1, 0);
                yield break;
            }

            long bytes = -1; bool ok = false;
            yield return MeasureDownloadSize(keys, (b, o) => { bytes = b; ok = o; });

            long answer = ok ? bytes : -1;
            FlowTrace.Step(Sys, ok
                ? $"download size for offline mode = {answer} bytes ({answer / (1024f * 1024f):F1} MB) " +
                  $"across {keys.Count} key(s)"
                : "download size for offline mode = UNKNOWN (measurement failed) -> reporting -1");
            onSize?.Invoke(answer, keys.Count);
        }

        /// <summary>
        /// ONE GetDownloadSizeAsync call for the whole key set. Deliberately not a per-key sum:
        /// keys share bundles, so summing per-key answers double-counts and would show the
        /// player a number bigger than the download. The batched overload deduplicates.
        /// </summary>
        private static IEnumerator MeasureDownloadSize(List<string> keys, Action<long, bool> onDone)
        {
            if (keys == null || keys.Count == 0) { onDone?.Invoke(-1, false); yield break; }

            AsyncOperationHandle<long> h = default;
            bool started = false;
            try { h = Addressables.GetDownloadSizeAsync((IEnumerable)keys); started = true; }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, $"GetDownloadSizeAsync(set of {keys.Count}) threw: {ex.GetType().Name}: {ex.Message}");
            }
            if (!started) { onDone?.Invoke(-1, false); yield break; }

            while (!h.IsDone) yield return null;

            bool ok = h.Status == AsyncOperationStatus.Succeeded;
            long bytes = ok ? h.Result : -1;
            if (!ok) FlowTrace.Warn(Sys, "GetDownloadSizeAsync FAILED for the offline set - size is UNKNOWN, not zero.");
            Addressables.Release(h);
            onDone?.Invoke(bytes, ok);
        }

        /// <summary>
        /// Yield until Addressables has a catalog. Without this, a caller that runs before
        /// initialization sees zero locators, zero keys, and an "unknown" that is really just
        /// "too early". No WaitForCompletion — see the file header.
        /// </summary>
        private static IEnumerator EnsureInitialized()
        {
            AsyncOperationHandle<IResourceLocator> h = default;
            bool started = false;
            try { h = Addressables.InitializeAsync(false); started = true; }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, $"Addressables.InitializeAsync threw ({ex.Message}) - continuing; the key " +
                                    "enumeration below will report an unknown rather than a false zero.");
            }
            if (!started) yield break;

            while (!h.IsDone) yield return null;
            if (h.Status != AsyncOperationStatus.Succeeded)
                FlowTrace.Warn(Sys, "Addressables.InitializeAsync FAILED - the catalog may be unusable this launch.");
            Addressables.Release(h);
        }

        // =====================================================================
        //  WO-1092 runtime seam: resolve bundle facts, repair abandoned transactions.
        // =====================================================================

        /// <summary>What the catalog says about one remote bundle. Read, never typed.</summary>
        public sealed class RemoteBundleFact
        {
            public string BundleName;
            public string Hash;
            public bool HashValid;
            public long BundleSize;
        }

        /// <summary>
        /// Walk every key's resolved locations SYNCHRONOUSLY and read the
        /// <c>AssetBundleRequestOptions</c> off each dependency. No async handles: 682 keys
        /// through GetDownloadSizeAsync would be 682 operations, and
        /// <see cref="IResourceLocator.Locate"/> already answers without a network call.
        /// Feeds BOTH the unique-bundle chunk plan and the failure diagnostic.
        /// </summary>
        /// <param name="keys">The offline key set.</param>
        /// <param name="factsByHash">bundle HASH -> fact. The hash is what a cache version
        /// directory is NAMED, which is how a stale lock gets a bundle name in the log.</param>
        public static List<KeyBundleSet> ResolveKeyBundles(List<string> keys,
                                                           Dictionary<string, RemoteBundleFact> factsByHash)
        {
            var entries = new List<KeyBundleSet>();
            if (keys == null || keys.Count == 0) return entries;

            int unresolved = 0;
            Guard.Try(Sys, "resolve bundle dependencies for the offline key set", () =>
            {
                var scratch = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    scratch.Clear();
                    foreach (var locator in Addressables.ResourceLocators)
                    {
                        if (locator == null) continue;
                        IList<IResourceLocation> locs = null;
                        try { if (!locator.Locate(key, typeof(object), out locs)) continue; }
                        catch (Exception ex)
                        {
                            FlowTrace.Warn(Sys, $"Locate('{key}') threw {ex.GetType().Name}: {ex.Message} - " +
                                                "key kept in the plan as UNRESOLVED rather than dropped.");
                            continue;
                        }
                        if (locs == null) continue;
                        for (int i = 0; i < locs.Count; i++) CollectBundleNames(locs[i], scratch, factsByHash, 0);
                    }

                    if (scratch.Count == 0) unresolved++;
                    var arr = new string[scratch.Count];
                    scratch.CopyTo(arr);
                    entries.Add(new KeyBundleSet { Key = key, BundleNames = arr });
                }
            });

            // ⛔ COVERAGE CAN ONLY GROW, NEVER SHRINK. The Guard above swallows a throw part-way
            // through the walk; if that happened, `entries` would be SHORT and every key after the
            // throw would silently drop out of the plan - the exact PROD-010 coverage-cut shape,
            // one layer down. Backfill every missing key as an UNKNOWN (the planner always keeps
            // unknowns) and say how many, loudly.
            if (entries.Count < keys.Count)
            {
                var have = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < entries.Count; i++) have.Add(entries[i].Key);
                int backfilled = 0;
                foreach (var key in keys)
                {
                    if (have.Contains(key)) continue;
                    entries.Add(new KeyBundleSet { Key = key, BundleNames = Array.Empty<string>() });
                    backfilled++;
                }
                FlowTrace.Warn(Sys, $"bundle resolution stopped early - {backfilled} of {keys.Count} key(s) were " +
                                    "never walked. Re-added as UNKNOWN so the plan still covers the whole set; " +
                                    "a short plan would quietly stop fetching part of the content.");
            }

            FlowTrace.Step(Sys, $"bundle resolution: {entries.Count} key(s) planned of {keys.Count} in the set, " +
                                $"{factsByHash.Count} distinct remote bundle(s) named, " +
                                $"{unresolved} key(s) resolved to NO bundle (kept in the plan as unknown).");
            return entries;
        }

        /// <summary>Recursive dependency walk. Depth-bounded so a cyclic locator cannot hang the pull.</summary>
        private static void CollectBundleNames(IResourceLocation loc, HashSet<string> into,
                                               Dictionary<string, RemoteBundleFact> factsByHash, int depth)
        {
            if (loc == null || depth > 6) return;

            if (loc.Data is AssetBundleRequestOptions opts && !string.IsNullOrEmpty(opts.BundleName))
            {
                into.Add(opts.BundleName);
                string hash = opts.Hash;
                if (!string.IsNullOrEmpty(hash) && factsByHash != null && !factsByHash.ContainsKey(hash))
                {
                    bool valid = false;
                    try { valid = Hash128.Parse(hash).isValid; } catch { valid = false; }
                    factsByHash[hash] = new RemoteBundleFact
                    {
                        BundleName = opts.BundleName,
                        Hash = hash,
                        HashValid = valid,
                        BundleSize = opts.BundleSize,
                    };
                }
            }

            var deps = loc.Dependencies;
            if (deps == null) return;
            for (int i = 0; i < deps.Count; i++) CollectBundleNames(deps[i], into, factsByHash, depth + 1);
        }

        /// <summary>
        /// Scan the writable AssetBundle cache and remove EXACTLY the abandoned version
        /// transactions - a version directory holding a stale zero-byte `__lock` and nothing
        /// else (WO-1092). Returns how many were repaired.
        ///
        /// ⛔ TARGETED, NEVER BROAD. The complete PREVIOUS version of the same bundle
        /// (`3c9df7ea...`, 19,400,698 bytes on this disk) must survive, so this never calls
        /// <c>Caching.ClearAllCachedVersions</c> and never calls
        /// <see cref="AddressablesCacheHealth.ReportDownloadFailure"/> - that arms a whole-cache
        /// <c>Caching.ClearCache()</c> for the next launch, which is the opposite of targeted.
        /// </summary>
        /// <param name="minStaleAgeSeconds">
        /// How old a lock must be to count as abandoned. The FIRST pass uses
        /// <see cref="CacheLockStaleSeconds"/> because a content warmer may hold a live
        /// transaction. A RETRY pass passes 0: the pull's own handles have all been released
        /// by then, so a lock-only directory that survived our own attempt is abandoned by
        /// definition - and if the retry pass kept the 5-minute floor it would refuse to
        /// repair the very transaction the failed attempt just abandoned, which is the whole
        /// point of the retry.
        /// </param>
        public static int RepairAbandonedCacheTransactions(Dictionary<string, RemoteBundleFact> factsByHash,
                                                           int attempt, double minStaleAgeSeconds)
        {
            int repaired = 0;
            int inspected = 0;
            int live = 0;

            Guard.Try(Sys, "scan the AssetBundle cache for abandoned version transactions", () =>
            {
#if UNITY_WEBGL
                // ⛔ WEBGL HAS NO ASSETBUNDLE CACHE. `UnityEngine.Caching` and `CachedAssetBundle`
                // live in UnityEngine.AssetBundleModule, which is NOT part of a WebGL player - the
                // identifiers do not exist there at all, which is why the WebGL content compile
                // (Builds/wave1-compile2, 2026-09-09 23:24) failed CS0103 on them while the active
                // Android target compiled clean. Guarded rather than papered over with a using: the
                // platform genuinely cannot have an abandoned cache transaction, because it has no
                // cache to open one in. The decision is still NAMED in the trace (CLAUDE.md §12).
                // The counters are named in the line so they are READ on this target too - an
                // int left assigned-but-unused behind a platform guard is a CS0219 warning.
                FlowTrace.Step(Sys, $"cache repair (attempt={attempt}) NOT APPLICABLE on WebGL - the platform has " +
                                    "no AssetBundle cache, so there is no version transaction to abandon and " +
                                    $"nothing to repair. inspected={inspected}, repaired={repaired}, live={live}.");
                return;
#else
                string root = null;
                try { root = Caching.currentCacheForWriting.path; } catch { root = null; }

                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    FlowTrace.Warn(Sys, $"cache repair (attempt={attempt}) SKIPPED: writable cache path " +
                                        $"'{root ?? "<null>"}' does not exist. The on-disk layout assumption " +
                                        "(<root>/<bundle-dir>/<hash>/{__data,__info,__lock}) could not be checked.");
                    return;
                }

                FlowTrace.Step(Sys, $"cache repair (attempt={attempt}) scanning '{root}' " +
                                    $"(a lock-only version dir counts as abandoned at >= {minStaleAgeSeconds:F0}s).");

                foreach (var bundleDir in Directory.GetDirectories(root))
                {
                    foreach (var versionDir in Directory.GetDirectories(bundleDir))
                    {
                        inspected++;
                        string name = Path.GetFileName(versionDir);
                        string lockPath = Path.Combine(versionDir, "__lock");

                        bool hasLock = File.Exists(lockPath);
                        bool hasData = File.Exists(Path.Combine(versionDir, "__data"));
                        bool hasInfo = File.Exists(Path.Combine(versionDir, "__info"));

                        int other = 0;
                        foreach (var f in Directory.GetFiles(versionDir))
                        {
                            string fn = Path.GetFileName(f);
                            if (fn != "__lock" && fn != "__data" && fn != "__info") other++;
                        }
                        other += Directory.GetDirectories(versionDir).Length;

                        double ageSeconds = -1d;
                        if (hasLock)
                        {
                            try { ageSeconds = (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath)).TotalSeconds; }
                            catch { ageSeconds = -1d; }
                        }

                        var state = ClassifyCacheVersion(name, hasLock, hasData, hasInfo, other,
                                                         ageSeconds, minStaleAgeSeconds);

                        if (state == CacheVersionState.LiveTransaction)
                        {
                            live++;
                            FlowTrace.Step(Sys, $"cache repair (attempt={attempt}) LEFT ALONE " +
                                                $"{DescribeBundle(factsByHash, name)} - lock is only " +
                                                $"{ageSeconds:F0}s old (< {minStaleAgeSeconds:F0}s); a download " +
                                                "may be in flight and deleting it would manufacture corruption.");
                            continue;
                        }
                        if (state != CacheVersionState.AbandonedTransaction) continue;

                        // LIVENESS PROBE. The age bound alone cannot see a CONCURRENT writer, and the
                        // retry pass deliberately drops that bound to 0. Try to take the lock file
                        // exclusively: if anything else holds a handle on it, the transaction is LIVE
                        // and deleting it would manufacture the partial-bundle corruption
                        // AddressablesCacheHealth exists to recover from. If Unity does not hold the
                        // file open during a transaction this probe simply always passes - so it costs
                        // nothing and can only ever prevent a wrong delete.
                        if (!TryTakeExclusively(lockPath, out string holdReason))
                        {
                            live++;
                            FlowTrace.Step(Sys, $"cache repair (attempt={attempt}) LEFT ALONE " +
                                                $"{DescribeBundle(factsByHash, name)} - its __lock is HELD by " +
                                                $"another handle ({holdReason}), so the transaction is live " +
                                                "despite its age. Not deleting.");
                            continue;
                        }

                        FlowTrace.Warn(Sys, $"cache repair (attempt={attempt}) ABANDONED TRANSACTION found: " +
                                            $"{DescribeBundle(factsByHash, name)} at '{versionDir}' holds a " +
                                            $"{FileLength(lockPath)}-byte __lock, no __data, no __info, age " +
                                            $"{ageSeconds:F0}s. Every fetch of this bundle succeeds in memory and " +
                                            "never commits. Removing this exact version transaction; the other " +
                                            "cached versions of the same bundle are untouched.");

                        bool deleted = false;
                        try { Directory.Delete(versionDir, true); deleted = !Directory.Exists(versionDir); }
                        catch (Exception ex)
                        {
                            FlowTrace.Fail(Sys, $"cache repair (attempt={attempt}) could NOT remove " +
                                                $"{DescribeBundle(factsByHash, name)} at '{versionDir}': " +
                                                $"{ex.GetType().Name}: {ex.Message}. The retry below will not help " +
                                                "this bundle - saying so beats a silent second failure.");
                        }

                        if (deleted)
                        {
                            repaired++;
                            FlowTrace.Step(Sys, $"cache repair (attempt={attempt}) REMOVED " +
                                                $"{DescribeBundle(factsByHash, name)} - verified gone from disk.");
                        }
                        else
                        {
                            FlowTrace.Fail(Sys, $"cache repair (attempt={attempt}) reported no exception but " +
                                                $"'{versionDir}' still EXISTS. Not counting it as repaired.");
                        }
                    }
                }

                FlowTrace.Step(Sys, $"cache repair (attempt={attempt}) done: {inspected} version dir(s) inspected, " +
                                    $"{repaired} abandoned transaction(s) removed, {live} live transaction(s) left alone.");
#endif
            });

            return repaired;
        }

        /// <summary>
        /// Can we open this file with NO sharing? Success means nobody else holds it. Failure is
        /// reported as a reason string rather than a bool alone, so the log says WHY we backed off.
        /// </summary>
        private static bool TryTakeExclusively(string path, out string reason)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                reason = "not held";
                return true;
            }
            catch (Exception ex)
            {
                reason = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static long FileLength(string path)
        {
            try { return new FileInfo(path).Length; } catch { return -1L; }
        }

        /// <summary>Name a bundle from its cache-version hash, so no log line says only "a directory".</summary>
        private static string DescribeBundle(Dictionary<string, RemoteBundleFact> factsByHash, string hash)
        {
            if (factsByHash != null && !string.IsNullOrEmpty(hash) &&
                factsByHash.TryGetValue(hash, out var fact) && fact != null)
            {
                return $"bundle '{fact.BundleName}' (hash={hash}, hashValid={fact.HashValid}, {fact.BundleSize} bytes)";
            }
            return $"bundle hash={hash} (not in this build's catalog key set - name unknown)";
        }

        /// <summary>
        /// On a NON-VERIFIED pull, name the bundles that are still not cached, with the four
        /// facts that tell an invalid-hash defect apart from a lost cache write (WO-1092 spec).
        /// Synchronous - <c>Caching.IsVersionCached</c> needs no handle. CAPPED: this fires over
        /// a ~682-key set and an uncapped dump evicts the surrounding evidence out of the 256 KiB
        /// Android logcat ring (memory logcat-ring-buffer-destroys-evidence).
        /// </summary>
        public static void LogOutstandingBundles(Dictionary<string, RemoteBundleFact> factsByHash)
        {
            Guard.Try(Sys, "name the bundles still outstanding after a failed pull", () =>
            {
                if (factsByHash == null || factsByHash.Count == 0)
                {
                    FlowTrace.Warn(Sys, "outstanding-bundle diagnostic has NOTHING to report: no remote bundle " +
                                        "was resolved from the catalog. That is itself the finding.");
                    return;
                }

                int missing = 0, listed = 0, invalidHash = 0;
                long missingBytes = 0;
                foreach (var kv in factsByHash)
                {
                    var f = kv.Value;
                    if (f == null) continue;

                    bool cached = false;
#if !UNITY_WEBGL
                    // Same platform guard as the repair above: `Caching` / `CachedAssetBundle` are
                    // UnityEngine.AssetBundleModule types and do not exist in a WebGL player. On
                    // WebGL `cached` stays false, which is the truthful answer - nothing is cached
                    // there - so the report below still lists the set honestly rather than lying.
                    try
                    {
                        var h = Hash128.Parse(f.Hash);
                        cached = h.isValid && Caching.IsVersionCached(new CachedAssetBundle(f.BundleName, h));
                    }
                    catch { cached = false; }
#endif
                    if (cached) continue;

                    missing++;
                    missingBytes += f.BundleSize > 0 ? f.BundleSize : 0;
                    if (!f.HashValid) invalidHash++;
                    if (listed++ < DiagnosticBundleCap)
                    {
                        FlowTrace.Fail(Sys, $"STILL OUTSTANDING: bundle '{f.BundleName}' hash={f.Hash} " +
                                            $"hashValid={f.HashValid} size={f.BundleSize} - not cached after the pull.");
                    }
                }

                FlowTrace.Fail(Sys, $"outstanding-bundle diagnostic: {missing} of {factsByHash.Count} remote " +
                                    $"bundle(s) are NOT cached ({missingBytes} byte(s)); {invalidHash} carry an " +
                                    $"INVALID hash; first {Mathf.Min(listed, DiagnosticBundleCap)} named above. " +
                                    "An invalid hash means the bundle can never cache at all; a valid hash means " +
                                    "the cache write was lost or the transaction never committed.");
            });
        }

        // =====================================================================
        //  3. The opt-in pull
        // =====================================================================

        /// <summary>
        /// Back-compat overload: fraction-only progress, bool-only result.
        /// </summary>
        public static IEnumerator DownloadAllForOffline(Action<float> onProgress, Action<bool> onDone)
            => DownloadAllForOffline(
                (pct, doneBytes, totalBytes) => onProgress?.Invoke(pct),
                (ok, reason) => onDone?.Invoke(ok));

        /// <summary>
        /// THE FIRST-RUN CDN PULL. Downloads every remote dependency so the game can run
        /// without a network afterwards, then PROVES IT by re-measuring the same key set.
        /// Requires Wi-Fi/data - the owner's spec says so and this refuses rather than
        /// pretending otherwise.
        /// </summary>
        /// <param name="onProgress">(fraction 0..1, bytes downloaded, total bytes). All three
        /// are MEASURED. The fraction is byte-weighted and monotonic - it never jumps back and
        /// never advances on a timer.</param>
        /// <param name="onDone">(success, player-safe reason). True ONLY when the post-pull
        /// re-measurement proves 0 bytes outstanding.</param>
        public static IEnumerator DownloadAllForOffline(Action<float, long, long> onProgress,
                                                        Action<bool, string> onDone)
        {
            using var _ = FlowTrace.Enter(Sys, "DownloadAllForOffline");

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                FlowTrace.Warn(Sys, "offline pull REFUSED: no network. The first pull needs Wi-Fi by design; " +
                                    "saying so is better than a progress bar that never moves.");
                onDone?.Invoke(false, "This one-time download needs a Wi-Fi or data connection.");
                yield break;
            }

            yield return EnsureInitialized();

            var keys = CollectContentKeys();
            if (keys.Count == 0)
            {
                FlowTrace.Fail(Sys, "offline pull ABORTED - the catalog resolved 0 keys, so there is nothing " +
                                    "to fetch and no basis for claiming success. NOT stamping offline-ready.");
                onDone?.Invoke(false, "We could not work out what to download. Please try again in a moment.");
                yield break;
            }

            // Measure BEFORE downloading: this is the denominator for honest progress and the
            // baseline the outcome assertion is judged against.
            long total = -1; bool sized = false;
            yield return MeasureDownloadSize(keys, (b, ok) => { total = b; sized = ok; });

            if (!sized)
            {
                FlowTrace.Fail(Sys, "offline pull ABORTED - could not measure the set, so progress would be " +
                                    "invented and success would be unprovable.");
                onDone?.Invoke(false, "We could not check the download right now. Please try again in a moment.");
                yield break;
            }

            if (total == 0)
            {
                // Genuinely cached - and we know it is genuine because keys.Count > 0.
                FlowTrace.Step(Sys, $"offline pull: {keys.Count} key(s) resolved and 0 bytes outstanding - " +
                                    "already fully cached. Stamping offline-ready on MEASURED evidence.");
                StampOfflineReady();
                onProgress?.Invoke(1f, 0, 0);
                onDone?.Invoke(true, "Everything is already downloaded.");
                yield break;
            }

            // ─── WO-1092 (b): CHUNK BY UNIQUE BUNDLE, NOT BY ADDRESS ────────────────────
            // MergeMode.Union deduplicates INSIDE a chunk and cannot deduplicate ACROSS
            // chunks, so a family bundle shared by ~16 addresses was re-requested by every
            // chunk that touched it: 19,151,184 + (3 x 19,398,472) = 77,346,600, the exact
            // numerator the failed pull reported.
            var factsByHash = new Dictionary<string, RemoteBundleFact>(StringComparer.Ordinal);
            var entries = ResolveKeyBundles(keys, factsByHash);
            var plan = PlanUniqueBundleChunks(entries, ChunkSize, out int uniqueBundles, out int skippedKeys);

            if (plan.Count == 0)
            {
                // Never let a resolution failure shrink the set to nothing - that is the shape
                // of the PROD-010 "zero keys read as already cached" defect. Fall back to the
                // old address chunking and SAY SO.
                FlowTrace.Warn(Sys, "unique-bundle planning produced 0 chunks - falling back to plain address " +
                                    "chunking so coverage is never reduced by a resolution failure.");
                plan = new List<List<string>>();
                for (int s = 0; s < keys.Count; s += ChunkSize)
                    plan.Add(keys.GetRange(s, Mathf.Min(ChunkSize, keys.Count - s)));
            }

            FlowTrace.Step(Sys, $"offline pull START: {keys.Count} key(s), {total} bytes " +
                                $"({total / (1024f * 1024f):F1} MB), chunk size {ChunkSize}. " +
                                $"Planned by UNIQUE BUNDLE: {plan.Count} chunk(s) over {uniqueBundles} distinct " +
                                $"bundle(s); {skippedKeys} key(s) added no new bundle and were folded in " +
                                "(coverage unchanged - their bytes are already in the plan).");

            bool allOk = true;
            long doneBytes = 0;          // this attempt only - the chunk loop's running total
            long downloadedAllAttempts = 0;  // what the player actually pulled, across attempts
            float lastPct = 0f;
            int failedChunks = 0;
            long remaining = -1;
            bool remeasured = false;
            int repairedTotal = 0;

            // ─── WO-1092 (a): repair the abandoned cache transaction, then ONE bounded retry ──
            for (int attempt = 1; attempt <= MaxPullAttempts; attempt++)
            {
                // Pass 1 keeps the 5-minute floor (a warmer may hold a live transaction);
                // a retry pass uses 0, because our own handles are released by then.
                repairedTotal += RepairAbandonedCacheTransactions(
                    factsByHash, attempt, attempt == 1 ? CacheLockStaleSeconds : 0d);

                allOk = true;
                failedChunks = 0;
                doneBytes = 0;

                for (int ci = 0; ci < plan.Count; ci++)
                {
                    var chunk = plan[ci];
                    if (chunk == null || chunk.Count == 0) continue;

                    AsyncOperationHandle h = default;
                    bool started = false;
                    try
                    {
                        h = Addressables.DownloadDependenciesAsync((IEnumerable)chunk, Addressables.MergeMode.Union, false);
                        started = true;
                    }
                    catch (Exception ex)
                    {
                        allOk = false;
                        failedChunks++;
                        FlowTrace.Fail(Sys, $"DownloadDependenciesAsync(attempt={attempt} chunk {ci + 1}/{plan.Count}, " +
                                            $"{chunk.Count} key(s), first '{chunk[0]}') threw: " +
                                            $"{ex.GetType().Name}: {ex.Message}");
                    }
                    if (!started) continue;

                    while (!h.IsDone)
                    {
                        // BYTE-WEIGHTED, from GetDownloadStatus - never a key counter and never a
                        // timer. With the re-pack producing many small bundles this advances
                        // continuously inside a chunk instead of stepping once per chunk.
                        var st = h.GetDownloadStatus();
                        Report(doneBytes + st.DownloadedBytes);
                        yield return null;
                    }

                    var fin = h.GetDownloadStatus();
                    doneBytes += fin.DownloadedBytes;

                    if (h.Status != AsyncOperationStatus.Succeeded)
                    {
                        allOk = false;
                        failedChunks++;
                        AddressablesCacheHealth.ReportDownloadFailure(
                            $"offline attempt {attempt} chunk {ci + 1}/{plan.Count}");
                        FlowTrace.Fail(Sys, $"offline pull FAILED for attempt={attempt} chunk {ci + 1}/{plan.Count} " +
                                            $"(first key '{chunk[0]}') - the player is NOT offline-ready.");
                    }

                    Addressables.Release(h);
                    Report(doneBytes);
                }

                // ⭐ THE OUTCOME ASSERTION. Handles reporting Succeeded is not evidence that bytes
                // landed; on 2026-08-19 a set that matched nothing "succeeded" instantly. Re-measure
                // the SAME key set and require zero outstanding. This single check is the difference
                // between a feature and a no-op wearing a green tick. NOT relaxed by this fix.
                downloadedAllAttempts += doneBytes;

                remaining = -1; remeasured = false;
                yield return MeasureDownloadSize(keys, (b, ok) => { remaining = b; remeasured = ok; });

                if (allOk && remeasured && remaining == 0)
                {
                    FlowTrace.Step(Sys, $"offline pull attempt {attempt}/{MaxPullAttempts} left 0 bytes outstanding " +
                                        $"({doneBytes} byte(s) downloaded this attempt, {repairedTotal} abandoned cache " +
                                        "transaction(s) repaired in total).");
                    break;
                }

                if (attempt >= MaxPullAttempts)
                {
                    FlowTrace.Fail(Sys, $"offline pull attempt {attempt}/{MaxPullAttempts} STILL leaves " +
                                        $"{(remeasured ? remaining.ToString() : "an unmeasurable number of")} byte(s) " +
                                        $"outstanding (failedChunks={failedChunks}, repaired={repairedTotal}). No " +
                                        "further retry BY DESIGN - if a bundle structurally cannot cache, looping " +
                                        "would hang and an honest failure is better. Naming the bundles now.");
                    break;
                }

                FlowTrace.Warn(Sys, $"offline pull attempt {attempt}/{MaxPullAttempts} finished with " +
                                    $"allHandlesOk={allOk}, remeasured={remeasured}, remaining=" +
                                    $"{(remeasured ? remaining.ToString() : "UNKNOWN")}. Re-scanning for abandoned " +
                                    "cache transactions and retrying the whole set ONCE - already-committed bundles " +
                                    "cost a cache check, not a download.");
            }

            bool verified = PullVerified(keys.Count, allOk, remeasured ? remaining : -1, out string verdict);

            // WO-1092 diagnostic: on a failure, say WHICH bundles and whether their hash is even
            // valid. An invalid hash can never cache; a valid one means the write was lost.
            if (!verified) LogOutstandingBundles(factsByHash);

            // Report what the player ACTUALLY pulled across every attempt. Using the last attempt's
            // figure would read as "downloaded 19 MB of 38 MB" on a successful retry.
            onProgress?.Invoke(verified ? 1f : lastPct, downloadedAllAttempts, total);

            if (verified)
            {
                StampOfflineReady();
                FlowTrace.Step(Sys, $"OFFLINE PULL COMPLETE for build {Application.version} - {verdict}. " +
                                    $"{downloadedAllAttempts} byte(s) actually downloaded. Later launches with no network " +
                                    "will use the local cache.");
                onDone?.Invoke(true, "Done. This game now works without a connection.");
            }
            else
            {
                FlowTrace.Fail(Sys, $"OFFLINE PULL NOT VERIFIED ({verdict}); failedChunks={failedChunks}, " +
                                    $"attempts={MaxPullAttempts}, cacheTransactionsRepaired={repairedTotal}, " +
                                    $"downloaded={downloadedAllAttempts}/{total}. NOT stamped - the player stays " +
                                    "online-dependent, which is the truthful state; a half-cache recorded " +
                                    "as complete is worse than no cache at all.");
                onDone?.Invoke(false, "The download did not finish. You can try again any time; " +
                                      "the game still works normally with a connection.");
            }

            void Report(long bytesSoFar)
            {
                float pct = total > 0 ? Mathf.Clamp01(bytesSoFar / (float)total) : 0f;
                if (pct < lastPct) pct = lastPct;   // monotonic: a bar that goes backwards reads as a bug
                lastPct = pct;
                onProgress?.Invoke(pct, bytesSoFar, total);
            }
        }
    }
}
