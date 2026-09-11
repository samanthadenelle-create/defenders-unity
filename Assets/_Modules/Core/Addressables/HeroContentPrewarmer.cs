// =============================================================================
// HeroContentPrewarmer — downloads the CHOSEN hero's remote art during the
// post-class-select load screen, and REFUSES to enter the world if it can't
// (WO-1187, owner ruling 2026-09-03).
// -----------------------------------------------------------------------------
// WHY THIS EXISTS (CLAUDE.md §16, read it before editing):
// hero bodies + atlases now live in REMOTE R2 bundles with NO local copy. Remote
// art in this project fails SILENTLY — a bundle that was never pushed produces a
// tinted CAPSULE and NO error on screen, and the only detector left is the owner's
// eyes. That is precisely what §14 exists to never rely on. So this class is the
// gate: the load screen awaits Prewarm(), and on failure the player is held on the
// load screen with a WORDED message instead of being dropped in as a pill.
//
// ⚠ THE FAILURE MESSAGE IS WORDS, NEVER A COLOUR. The owner is red/green
// colourblind (memory: owner-colorblind-delegate-visual-creative). A red banner or
// a red/green dot is NOT a failure state she can read. StatusText always says, in
// plain language, what failed and what to do about it.
//
// WHY A BLOCKING PREWARM AND NOT LAZY LOADING: HeroAssetLoader's call sites -
// AtbCombatantSwapper, HeroBodySwapper, StoryCompanionInjector - are all sync, and
// a remote asset cannot be fetched synchronously without either stalling the main
// thread (standalone) or throwing outright (WebGL). Prewarming here means the asset
// is already in memory by the time anything calls the loader.
// Do NOT remove the prewarm and expect the loader to cope.
//
// !! WO-1701 (2026-09-10) - DOWNLOADING THE BUNDLE WAS NEVER ENOUGH.
// Until WO-1701 this class downloaded the bundle and stopped there, leaving the
// loader to turn those bytes into an object with Addressables.WaitForCompletion.
// On the Seeker in Pi Browser that threw one second after this class logged Ready:
//   21:54:23Z [Flow:HeroPrewarm] 'Mage' art downloaded and cached on attempt 1 - Ready.
//   21:54:24Z error: [Flow:HeroAssets] Addressables resolve 'Heroes/Mage' (GameObject)
//             FAILED: Exception: WebGLPlayer does not support synchronous Addressable
//             loading.
// Addressables refuses a synchronous load on WebGLPlayer REGARDLESS of cache state, so
// a warmer cache could never have fixed it. This class therefore now ALSO performs the
// LoadAssetAsync itself, asynchronously, from this coroutine (the one place the
// ResourceManager can be pumped), and PARKS the loaded objects in a warm dictionary the
// loader reads first. Same shape as StructureContentWarmer.TryGet, for the same reason.
//
// THE WARM ENTRIES ARE NEVER RELEASED. That is deliberate and matches the loader's own
// documented handle policy: parity with Resources.Load, which never unloads. A released
// handle lets the bundle unload and the next resolve is back on the cold path that
// cannot succeed on WebGL at all.
//
// This is presentation-agnostic on purpose (HP B2B, CLAUDE.md architecture law): it
// owns the DOWNLOAD and the STATUS STRING, and knows nothing about any panel. The
// load screen reads State/Progress/StatusText and draws them.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;   // FlowTrace / Guard — §12
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using Object = UnityEngine.Object;   // WO-1701: the warm cache stores UnityEngine.Object, never System.Object

namespace DeNelle.Core
{
    /// <summary>How the chosen hero's remote art download is going. Read by the load screen.</summary>
    public enum HeroPrewarmState
    {
        /// <summary>Nothing requested yet.</summary>
        Idle = 0,
        /// <summary>Download in flight — <see cref="HeroContentPrewarmer.Progress"/> is meaningful.</summary>
        Downloading = 1,
        /// <summary>Art is cached locally; it is safe to enter the world.</summary>
        Ready = 2,
        /// <summary>Every attempt failed. DO NOT enter the world — show StatusText + a Retry.</summary>
        Failed = 3,
    }

    /// <summary>
    /// Downloads one hero's remote Addressables content up-front, so the synchronous
    /// <see cref="HeroAssetLoader"/> call sites hit a warm cache. Gate the world entry on
    /// <see cref="State"/> == <see cref="HeroPrewarmState.Ready"/>.
    /// </summary>
    public static class HeroContentPrewarmer
    {
        /// <summary>Address prefix of the shared hero atlas bundle (HeroTextureLoader's keys).</summary>
        public const string TexAddrPrefix = "Heroes/Textures/";

        /// <summary>How many times a failed download is retried before we word the failure.</summary>
        public const int MaxAttempts = 3;

        /// <summary>Seconds between retry attempts (a flaky mobile connection usually recovers).</summary>
        private const float RetryDelaySeconds = 2f;

        /// <summary>Current state of the prewarm. The load screen gates entry on this.</summary>
        public static HeroPrewarmState State { get; private set; } = HeroPrewarmState.Idle;

        /// <summary>0..1 download progress while <see cref="State"/> is Downloading.</summary>
        public static float Progress { get; private set; }

        /// <summary>
        /// Plain-language status for the player. ALWAYS WORDS — never rely on colour to convey
        /// failure (the owner is red/green colourblind). Safe to display verbatim.
        /// </summary>
        public static string StatusText { get; private set; } = string.Empty;

        /// <summary>The slug of the last hero we prewarmed (or tried to).</summary>
        public static string LastSlug { get; private set; } = string.Empty;

        /// <summary>True when the last requested hero's art is cached and the world may load.</summary>
        public static bool IsReady(string slug) =>
            State == HeroPrewarmState.Ready &&
            string.Equals(LastSlug, slug, System.StringComparison.Ordinal);

        // =====================================================================
        //  WARM CACHE (WO-1701) - loaded objects, held for the process.
        //  HeroAssetLoader.Load<T> reads this BEFORE it touches Addressables.
        // =====================================================================

        /// <summary>
        /// Every hero asset this prewarm has actually LOADED (not merely downloaded), keyed by
        /// "&lt;TypeName&gt;:&lt;address&gt;". The type is part of the key because the hero prefab
        /// and the hero animator controller deliberately share one address and are told apart by
        /// asset type - see HeroAssetLoader.WarmableAddresses.
        /// <para>Never cleared in normal operation and never released. See the file header.</para>
        /// </summary>
        private static readonly Dictionary<string, Object> s_warm = new Dictionary<string, Object>();

        /// <summary>
        /// The load handles behind <see cref="s_warm"/>, retained for the life of the process so
        /// Addressables cannot unload the bundle underneath a resident object. Parity with
        /// Resources.Load, which never unloads; identical policy to StructureContentWarmer.
        /// </summary>
        private static readonly List<AsyncOperationHandle> s_warmHandles = new List<AsyncOperationHandle>();

        /// <summary>How many assets the warm cache currently holds (diagnostics, and the
        /// falsifiable half of "Ready": Ready with zero warm entries is a claim worth doubting).</summary>
        public static int WarmCount => s_warm.Count;

        /// <summary>The cache key. Same idiom as StructureContentWarmer.Key.</summary>
        private static string WarmKey(System.Type type, string address) =>
            (type == null ? "Object" : type.Name) + ":" + address;

        /// <summary>
        /// Synchronous read of the warm cache - a dictionary lookup and NOTHING else. No catalog
        /// probe, no handle, no pumping, no possibility of a wait or a throw, on any platform.
        /// This is what lets HeroAssetLoader's synchronous call-site shape survive on WebGL.
        /// </summary>
        public static bool TryGetWarm<T>(string address, out T asset) where T : Object
        {
            asset = null;
            if (string.IsNullOrEmpty(address)) return false;

            if (s_warm.TryGetValue(WarmKey(typeof(T), address), out var typed) && typed != null)
            {
                asset = typed as T;
                if (asset != null) return true;
            }
            return false;
        }

        /// <summary>
        /// TEST SEAM (Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs). Parks
        /// <paramref name="asset"/> in the warm cache under exactly the key the warm pass would
        /// have used, so a regression can prove the loader's dictionary branch is taken WITHOUT a
        /// catalog, a bundle or a network. Public because the regression lives in the editor
        /// assembly; it retains no handle, so <see cref="ClearWarmForTests"/> fully undoes it.
        /// </summary>
        public static void SeedWarmForTests(System.Type requestedType, string address, Object asset)
        {
            if (string.IsNullOrEmpty(address) || asset == null) return;
            s_warm[WarmKey(requestedType, address)] = asset;
        }

        /// <summary>
        /// TEST SEAM - drops every warm entry seeded by <see cref="SeedWarmForTests"/> AND every
        /// entry the real warm pass loaded, without releasing handles. Returns how many entries
        /// were dropped. A regression MUST call this in a finally block: leaving a throwaway
        /// GameObject parked here would let a later suite resolve a hero to test scaffolding.
        /// </summary>
        public static int ClearWarmForTests()
        {
            int n = s_warm.Count;
            s_warm.Clear();
            return n;
        }

        /// <summary>
        /// Download every remote bundle the given hero needs. Drive this from the load screen
        /// that runs after class select:
        /// <code>yield return HeroContentPrewarmer.Prewarm(slug);
        /// if (HeroContentPrewarmer.State != HeroPrewarmState.Ready) { /* show StatusText + Retry */ }</code>
        /// Never throws — a hard failure lands as <see cref="HeroPrewarmState.Failed"/> plus a worded
        /// <see cref="StatusText"/>, which the caller MUST honour by not entering the world.
        /// </summary>
        public static IEnumerator Prewarm(string slug)
        {
            LastSlug = slug ?? string.Empty;
            Progress = 0f;

            if (string.IsNullOrEmpty(slug))
            {
                // Nothing asked for = nothing to download. Not an error; don't block the load screen.
                State = HeroPrewarmState.Ready;
                StatusText = string.Empty;
                FlowTrace.Step("HeroPrewarm", "no slug supplied — nothing to prewarm (treated as Ready).");
                yield break;
            }

            // Collect the keys this hero needs: its own body address + the shared atlas bundle.
            List<object> keys = CollectKeys(slug);
            if (keys.Count == 0)
            {
                // No REMOTE entry for this hero — the normal state in the editor's
                // "Use Asset Database" play mode, and for any hero still served from
                // Resources (Props/Emotes/SC_*). Nothing to download, so do not block.
                State = HeroPrewarmState.Ready;
                StatusText = string.Empty;
                FlowTrace.Step("HeroPrewarm",
                    $"no Addressables keys for '{slug}' — nothing remote to fetch (editor/asset-database " +
                    "play mode or a deliberately-local hero). Treated as Ready.");
                yield break;
            }

            // How many bytes are actually missing? Zero => already cached from a previous session,
            // which is the common case on the owner's second launch.
            long bytes = 0;
            yield return GetDownloadSize(keys, size => bytes = size);

            if (bytes <= 0)
            {
                // WO-1701: cached bytes are NOT a loaded asset. Warm before declaring Ready -
                // this is the common second-launch path, and the WebGL player has no other way
                // to turn those bytes into a GameObject.
                yield return WarmAssets(slug);
                State = HeroPrewarmState.Ready;
                Progress = 1f;
                StatusText = string.Empty;
                FlowTrace.Step("HeroPrewarm",
                    $"'{slug}' art already cached (0 bytes to download); warm cache holds {WarmCount} asset(s) — Ready.");
                yield break;
            }

            float mb = bytes / (1024f * 1024f);
            FlowTrace.Step("HeroPrewarm", $"'{slug}' needs {mb:0.0} MB from the CDN across {keys.Count} key(s).");

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                State = HeroPrewarmState.Downloading;
                StatusText = attempt == 1
                    ? $"Preparing your {slug}… downloading {mb:0.0} MB."
                    : $"Connection problem. Retrying your {slug} download ({attempt} of {MaxAttempts})…";

                bool ok = false;
                bool faulted = false;
                string error = null;

                AsyncOperationHandle handle = default;
                bool started = Guard.Try("HeroPrewarm", $"DownloadDependenciesAsync '{slug}' attempt {attempt}", () =>
                {
                    handle = Addressables.DownloadDependenciesAsync(keys, Addressables.MergeMode.Union, false);
                });

                if (started && handle.IsValid())
                {
                    while (handle.IsValid() && !handle.IsDone)
                    {
                        Progress = Mathf.Clamp01(handle.PercentComplete);
                        StatusText = $"Preparing your {slug}… {Mathf.RoundToInt(Progress * 100f)}% of {mb:0.0} MB.";
                        yield return null;
                    }

                    if (handle.IsValid())
                    {
                        ok = handle.Status == AsyncOperationStatus.Succeeded;
                        if (!ok)
                        {
                            faulted = true;
                            error = handle.OperationException != null
                                ? handle.OperationException.Message
                                : "unknown Addressables download failure";
                        }
                        // Release the DOWNLOAD handle only — this frees the operation, not the cached
                        // bundle. The bytes stay in the Addressables cache, which is the whole point.
                        Guard.Try("HeroPrewarm", "release download handle", () => Addressables.Release(handle));
                    }
                    else
                    {
                        faulted = true;
                        error = "download handle became invalid mid-flight";
                    }
                }
                else
                {
                    faulted = true;
                    error = "DownloadDependenciesAsync could not be started";
                }

                if (ok)
                {
                    // WO-1701: the bytes are local now, but nothing has been LOADED yet. Do that
                    // here, async, before Ready - the loader must never have to do it synchronously.
                    yield return WarmAssets(slug);
                    State = HeroPrewarmState.Ready;
                    Progress = 1f;
                    StatusText = string.Empty;
                    FlowTrace.Step("HeroPrewarm",
                        $"'{slug}' art downloaded and cached on attempt {attempt}; warm cache holds " +
                        $"{WarmCount} asset(s) — Ready.");
                    yield break;
                }

                FlowTrace.Warn("HeroPrewarm",
                    $"'{slug}' art download attempt {attempt}/{MaxAttempts} FAILED: {error}");

                if (faulted && attempt < MaxAttempts)
                {
                    float until = Time.realtimeSinceStartup + RetryDelaySeconds;
                    while (Time.realtimeSinceStartup < until) yield return null;
                }
            }

            // ── Out of attempts. FAIL LOUDLY AND IN WORDS. ──────────────────────────
            // The caller MUST NOT enter the world now: with no local copy of the hero art,
            // proceeding is exactly the "tinted capsule, no error on screen" outcome §16 warns
            // about. Holding the player on the load screen with this sentence is the feature.
            State = HeroPrewarmState.Failed;
            StatusText =
                $"Could not download your {slug}. Your hero's artwork is missing, so the game has stopped " +
                "here instead of dropping you in without it. Check your internet connection and tap Retry.";

            FlowTrace.Fail("HeroPrewarm",
                $"'{slug}' art could not be downloaded after {MaxAttempts} attempts — world entry BLOCKED. " +
                "Most likely cause: the hero bundles were never pushed to R2 for THIS build (CLAUDE.md §16 — " +
                "bundle names are content-hashed, so every content build needs its own push).");
        }

        /// <summary>
        /// Read the hero the player actually chose out of the save and prewarm its art. This is the
        /// entry point the load screen / SceneRouter uses; it keeps the "which slug?" question in one
        /// place instead of duplicating HeroBodySwapper's resolution at every call site.
        /// </summary>
        public static IEnumerator PrewarmChosenHero()
        {
            string cls = null;
            Guard.Try("HeroPrewarm", "read chosen HeroClass from save", () =>
            {
                // HeroClassOpt is a plain ENUM whose None member is the "not chosen yet" sentinel
                // (Assets/_Modules/Core/State/GameState.cs:45). The ToNullable() extension used by
                // HeroBodySwapper lives in the Village assembly and is NOT visible from Core.
                var svc = DeNelle.Core.State.GameStateService.Instance;
                var st = svc != null ? svc.State : null;
                if (st != null && st.HeroClass != DeNelle.Core.State.HeroClassOpt.None)
                    cls = st.HeroClass.ToString();
            });

            if (string.IsNullOrEmpty(cls))
            {
                // No class chosen yet — the front-end / splash scenes. Nothing to fetch.
                State = HeroPrewarmState.Ready;
                StatusText = string.Empty;
                FlowTrace.Step("HeroPrewarm", "no HeroClass in save yet — nothing to prewarm (pre-class-select scene).");
                yield break;
            }

            yield return Prewarm(cls);
        }

        /// <summary>
        /// The body slugs that could be requested for a class. HeroBodySwapper does NOT simply ask for
        /// the class name: for a Knight it asks for "KnightV3" (FeatureFlags.KnightV3, default ON) or
        /// "KnightPackage" (FeatureFlags.HeroPackage) before falling back to "Knight"
        /// (Assets/_Modules/Village/Hero/HeroBodySwapper.cs:101/112/73). Prewarming only the class name
        /// would therefore download the WRONG bundle for a Knight and leave the one actually loaded
        /// uncached — a main-thread stall or a capsule. We fetch every REGISTERED variant for the class;
        /// unregistered names are skipped, so this costs nothing for the single-variant classes.
        /// FOLLOW-UP: mirror the feature flags here to stop fetching the ~14 MB of unused Knight
        /// variants; correctness first, bytes second.
        /// </summary>
        private static IEnumerable<string> BodySlugCandidates(string heroClass)
        {
            yield return heroClass;
            if (string.Equals(heroClass, "Knight", System.StringComparison.OrdinalIgnoreCase))
            {
                yield return "KnightV3";
                yield return "KnightPackage";
                yield return "knightV2";
            }
        }

        /// <summary>Reset to Idle so the load screen's Retry button can call <see cref="Prewarm"/> again.</summary>
        public static void Reset()
        {
            State = HeroPrewarmState.Idle;
            Progress = 0f;
            StatusText = string.Empty;
        }

        /// <summary>
        /// The keys this hero's art spans: its body address "Heroes/&lt;slug&gt;" plus every
        /// "Heroes/Textures/*" key in the catalog (the atlases HeroTextureLoader paints on ride in
        /// one shared bundle, so any of its keys pulls the bundle). Only keys the catalog actually
        /// knows are returned — an unregistered key would fault the whole download operation.
        /// </summary>
        private static List<object> CollectKeys(string slug)
        {
            var keys = new List<object>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            Guard.Try("HeroPrewarm", $"collect Addressables keys for '{slug}'", () =>
            {
                foreach (string candidate in BodySlugCandidates(slug))
                {
                    string body = HeroAssetLoader.HeroAddrPrefix + candidate;
                    if (KeyRegistered(body) && seen.Add(body)) keys.Add(body);
                }

                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator?.Keys == null) continue;
                    foreach (object key in locator.Keys)
                    {
                        if (!(key is string s)) continue;
                        if (!s.StartsWith(TexAddrPrefix, System.StringComparison.Ordinal)) continue;
                        if (seen.Add(s)) keys.Add(s);
                    }
                }
            });

            return keys;
        }

        // =====================================================================
        //  The warm pass (WO-1701)
        // =====================================================================

        /// <summary>
        /// Turn the downloaded bytes into LOADED objects and park them in the warm cache, so
        /// HeroAssetLoader (and, once it is routed here, HeroTextureLoader) can answer from a
        /// dictionary on every platform.
        /// <para>What it warms, and where the list comes from: the BODY addresses are enumerated
        /// from <c>HeroAssetLoader.WarmableAddresses</c> for every slug in
        /// <see cref="BodySlugCandidates"/> - the loader owns that list, this class does not keep a
        /// second copy of it. The TEXTURE addresses are the "Heroes/Textures/*" keys already
        /// collected for the download, warmed as Texture2D.</para>
        /// <para>Everything is asynchronous and everything is guarded: a single asset that will not
        /// load is reported by address and skipped, never allowed to fault the pass. The pass does
        /// NOT flip the state to Failed on a partial warm - whether a missing BODY should hold the
        /// load screen the way a failed DOWNLOAD does is an open design question recorded in the
        /// WO-1701 RESULT; it is deliberately not decided here.</para>
        /// </summary>
        private static IEnumerator WarmAssets(string slug)
        {
            int before = s_warm.Count;

            foreach (string candidate in BodySlugCandidates(slug))
            {
                foreach (var pair in HeroAssetLoader.WarmableAddresses(candidate))
                {
                    // Generic dispatch, not a second address list: the ADDRESS and the TYPE both
                    // come from WarmableAddresses. Addressables.LoadAssetAsync has no
                    // System.Type overload, so the type has to be reified into a call somewhere,
                    // and one switch here is cheaper than a duplicated list of addresses.
                    if (pair.Key == typeof(GameObject))
                        yield return WarmOne<GameObject>(pair.Value);
                    else if (pair.Key == typeof(RuntimeAnimatorController))
                        yield return WarmOne<RuntimeAnimatorController>(pair.Value);
                    else
                        FlowTrace.Warn("HeroPrewarm",
                            "HeroAssetLoader.WarmableAddresses asked for an asset type this warm pass " +
                            "cannot load: " + (pair.Key == null ? "null" : pair.Key.Name) + " at '" +
                            pair.Value + "'. Add a branch here - a type the loader can request and the " +
                            "prewarm cannot hold is exactly the WO-1701 defect coming back.");
                }
            }

            // THE ATLASES UNDER "Heroes/Textures/" ARE DELIBERATELY *NOT* WARMED HERE, and that
            // is a decision, not an omission. They are one shared bundle covering EVERY class
            // (the file header measures the folder in tens of MB); loading all of them would put
            // every class's atlas in the heap of the one platform least able to afford it - a
            // phone browser - while nothing reads them: HeroTextureLoader still resolves for
            // itself and does not consult this cache. That is cost with no benefit. Resources.Load
            // parity means on-demand, and the chosen class's atlas is what on-demand means.
            // The follow-up that routes HeroTextureLoader through TryGetWarm must warm ONLY the
            // chosen hero's atlases; the cache is already generic and keys Texture2D entries, so
            // that change is a probe plus a guard. Recorded in the WO-1701 RESULT.

            FlowTrace.Step("HeroPrewarm",
                "warm pass for '" + slug + "' loaded " + (s_warm.Count - before) + " new asset(s) (" +
                s_warm.Count + " held in total, " + s_warmHandles.Count + " handle(s) retained).");
        }

        /// <summary>
        /// Load ONE address as <typeparamref name="T"/> and park it. Silent no-op when the address
        /// is not registered for that type - the controller is not always a separate catalog entry,
        /// and a blind LoadAssetAsync on an unregistered key spams a red Addressables error. The
        /// registration probe is HeroAssetLoader's own (public since WO-1701) so the warm pass and
        /// the loader can never disagree about what "registered" means.
        /// </summary>
        private static IEnumerator WarmOne<T>(string address) where T : Object
        {
            if (string.IsNullOrEmpty(address)) yield break;

            string key = WarmKey(typeof(T), address);
            if (s_warm.TryGetValue(key, out var existing) && existing != null) yield break;

            bool registered = false;
            Guard.Try("HeroPrewarm", "catalog probe '" + address + "' (" + typeof(T).Name + ")", () =>
            {
                registered = HeroAssetLoader.AddressableRegistered<T>(address);
            });
            if (!registered) yield break;

            AsyncOperationHandle<T> handle = default;
            bool started = Guard.Try("HeroPrewarm", "LoadAssetAsync '" + address + "' (" + typeof(T).Name + ")", () =>
            {
                handle = Addressables.LoadAssetAsync<T>(address);
            });

            if (!started || !handle.IsValid())
            {
                FlowTrace.Warn("HeroPrewarm",
                    "could not START the warm load of '" + address + "' (" + typeof(T).Name + "). The " +
                    "loader will have to resolve it itself, which on WebGL cannot succeed.");
                yield break;
            }

            while (handle.IsValid() && !handle.IsDone) yield return null;

            if (!handle.IsValid())
            {
                FlowTrace.Warn("HeroPrewarm",
                    "the warm load handle for '" + address + "' (" + typeof(T).Name + ") became invalid mid-flight.");
                yield break;
            }

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                s_warm[key] = handle.Result;
                // NOT released - see the file header. The retained handle is what stops Addressables
                // unloading the bundle under a resident object.
                s_warmHandles.Add(handle);
                FlowTrace.Step("HeroPrewarm",
                    "warm cache HOLDS '" + address + "' (" + typeof(T).Name + ") -> '" + handle.Result.name + "'.");
                yield break;
            }

            string why = handle.OperationException != null
                ? handle.OperationException.Message
                : "no exception reported";
            FlowTrace.Fail("HeroPrewarm",
                "the bundle for '" + address + "' (" + typeof(T).Name + ") is local but the ASSET would not " +
                "load: " + why + ". The warm cache does not hold this address, so HeroAssetLoader must fall " +
                "through - and on WebGL that means the player gets the placeholder body. This is the exact " +
                "WO-1701 symptom recurring for a different reason; do not treat it as cosmetic.");
        }

        /// <summary>True when any locator can locate <paramref name="key"/> (type-agnostic).</summary>
        private static bool KeyRegistered(string key)
        {
            foreach (var locator in Addressables.ResourceLocators)
            {
                if (locator == null) continue;
                if (locator.Locate(key, null, out IList<IResourceLocation> locs) && locs != null && locs.Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>Await GetDownloadSizeAsync and hand the byte count to <paramref name="sink"/>. 0 on any error.</summary>
        private static IEnumerator GetDownloadSize(List<object> keys, System.Action<long> sink)
        {
            AsyncOperationHandle<long> handle = default;
            bool started = Guard.Try("HeroPrewarm", "GetDownloadSizeAsync", () =>
            {
                handle = Addressables.GetDownloadSizeAsync((IEnumerable<object>)keys);
            });

            if (!started || !handle.IsValid())
            {
                // Unknown size. Treat as "something to download" so we still attempt the fetch —
                // never as zero, which would wave a missing bundle straight through the gate.
                sink(1);
                yield break;
            }

            while (handle.IsValid() && !handle.IsDone) yield return null;

            long result = 1;
            if (handle.IsValid())
            {
                result = handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : 1;
                Guard.Try("HeroPrewarm", "release size handle", () => Addressables.Release(handle));
            }
            sink(result);
        }
    }
}
