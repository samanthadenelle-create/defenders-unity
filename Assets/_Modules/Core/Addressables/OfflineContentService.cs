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
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
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

            FlowTrace.Step(Sys, $"offline pull START: {keys.Count} key(s), {total} bytes " +
                                $"({total / (1024f * 1024f):F1} MB), chunk size {ChunkSize}.");

            bool allOk = true;
            long doneBytes = 0;
            float lastPct = 0f;
            int failedChunks = 0;

            for (int start = 0; start < keys.Count; start += ChunkSize)
            {
                int count = Mathf.Min(ChunkSize, keys.Count - start);
                var chunk = keys.GetRange(start, count);

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
                    FlowTrace.Fail(Sys, $"DownloadDependenciesAsync(chunk {start}..{start + count - 1}) threw: " +
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
                        $"offline chunk {start}..{start + count - 1}");
                    FlowTrace.Fail(Sys, $"offline pull FAILED for chunk {start}..{start + count - 1} " +
                                        $"(first key '{chunk[0]}') - the player is NOT offline-ready.");
                }

                Addressables.Release(h);
                Report(doneBytes);
            }

            // ⭐ THE OUTCOME ASSERTION. Handles reporting Succeeded is not evidence that bytes
            // landed; on 2026-08-19 a set that matched nothing "succeeded" instantly. Re-measure
            // the SAME key set and require zero outstanding. This single check is the difference
            // between a feature and a no-op wearing a green tick.
            long remaining = -1; bool remeasured = false;
            yield return MeasureDownloadSize(keys, (b, ok) => { remaining = b; remeasured = ok; });

            bool verified = PullVerified(keys.Count, allOk, remeasured ? remaining : -1, out string verdict);

            onProgress?.Invoke(verified ? 1f : lastPct, doneBytes, total);

            if (verified)
            {
                StampOfflineReady();
                FlowTrace.Step(Sys, $"OFFLINE PULL COMPLETE for build {Application.version} - {verdict}. " +
                                    $"{doneBytes} byte(s) actually downloaded. Later launches with no network " +
                                    "will use the local cache.");
                onDone?.Invoke(true, "Done. This game now works without a connection.");
            }
            else
            {
                FlowTrace.Fail(Sys, $"OFFLINE PULL NOT VERIFIED ({verdict}); failedChunks={failedChunks}, " +
                                    $"downloaded={doneBytes}/{total}. NOT stamped - the player stays " +
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
