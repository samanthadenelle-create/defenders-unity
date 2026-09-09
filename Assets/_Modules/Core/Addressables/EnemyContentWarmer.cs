// =============================================================================
// EnemyContentWarmer — the ASYNC side of the enemy art seam, and the reason
// EnemyAssetLoader can no longer hang the game.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core (Core/Addressables). References Addressables only — no
// Village/HUD types, so any module can warm/read it.
//
// ⛔ THE P0 THIS EXISTS FOR — IT IS THE SAME BUG, ON A SECOND SEAM (2026-08-20).
// The structure loader deadlocked a Seeker device for three minutes returning from
// a dungeon: last log line 10:25:14.917, device clock 10:28:35, process alive and
// foregrounded on a stale frame, with a HEALTHY 31.5 ms ping to the R2 CDN taken
// from the hung device. Not slowness — a deadlock. EnemyAssetLoader carried the
// EXACT SAME PATTERN, unfixed, and this file is the half that replaces it.
//
// ⛔ THE MECHANISM, verified against the Addressables 2.9.1 source that ships in
// this project (Library/PackageCache/com.unity.addressables@8460f1c9c927), and
// restated here because the next author will meet it on a THIRD seam:
//
//   1. AsyncOperationBase.WaitForCompletion (AsyncOperations/AsyncOperationBase.cs:171)
//      is a bare, uninterruptible, unbounded busy-spin:
//          while (!InvokeWaitForCompletion()) { }
//      No timeout parameter. No yield. No exit but completion. THERE IS NO BOUNDED
//      SYNCHRONOUS WAIT IN THIS PACKAGE — a fact about the vendor code, not a
//      preference of ours. Do not "just add a timeout"; there is nothing to add it to.
//
//   2. ProviderOperation.InvokeWaitForCompletion (ProviderOperation.cs:66) returns
//      FALSE FOREVER when the provider installed no completion callback, which turns
//      (1) into an infinite loop.
//
//   3. AssetBundleResource.WaitForCompletionHandler (AssetBundleProvider.cs:543)
//      Thread.Sleep()s the MAIN THREAD waiting on download progress that the engine's
//      own player loop is responsible for driving.
//
//   Call that from inside an engine callback the player loop cannot re-enter and the
//   thread that would finish the operation is the thread waiting on it. Deadlock.
//
// -----------------------------------------------------------------------------
// SO THE FIX IS NOT A TIMEOUT. IT IS: NEVER BLOCK, AND NEVER BLOCK FROM A CALLBACK.
//   A. Every Addressables call here is ASYNCHRONOUS, driven from a coroutine on a
//      DontDestroyOnLoad host — i.e. from the player loop, the one place the
//      ResourceManager can actually be pumped.
//   B. What lands is RETAINED for the process (s_retained is never released). The
//      owner's town -> raid -> town loop is exactly what evicts a released bundle and
//      puts the next spawn back on the cold path.
//   C. Synchronous callers keep their shape via TryGet, a dictionary probe that
//      cannot block for any reason.
//   D. Defer() hands work to the NEXT FRAME so a scene-load callback never touches
//      content from inside Internal_SceneLoaded.
//
// -----------------------------------------------------------------------------
// ⛔ HOW THIS DELIBERATELY DIFFERS FROM StructureContentWarmer — PER-FAMILY, ON DEMAND.
// Owner ruling 2026-08-20: "this means I want this broken down to each family of
// enemy". Structures are ~35 addresses / 28.9 MB and every town shows most of them at
// once, so that warmer loads the whole set up front. Enemy content is ~64 MB across
// many families and a given encounter uses ONE or TWO of them. Pulling all of it to
// show two skeletons is the cost this seam exists to avoid.
//
// Therefore the enemy warm pass DISCOVERS ONLY. It does not download and does not
// load a single asset. Content arrives strictly on demand:
//   • Request<T>(address)  — one asset. Addressables pulls only the bundle that holds
//     it, which (once the content is packed per family) IS the family bundle.
//   • WarmFamily(family)   — that family's bundles, no assets, ahead of a wave, so the
//     rest of the roster does not stutter in one at a time.
// A spawner that knows what it is about to spawn should call WarmFamily first; a
// spawner that does not still gets the per-address path for free.
//
// ⛔ CONSEQUENCE FOR THE STATE MARKER, and it is not the structure rule: Ready here
// means "the catalog is up and enemy addresses exist", NOT "content is resident".
// resident==0 after the enemy warm pass is the EXPECTED, CORRECT state — on-demand is
// the whole design. Copying StructureContentWarmer.DecideState's residency assertion
// onto this seam would report a permanent, meaningless red. What Ready must still be
// unable to do is claim success with ZERO discovered addresses: that means the catalog
// never came up or the enemy group never shipped, and every enemy in the game will be
// a capsule. That case is Degraded and says so at error level.
//
// ⛔ DO NOT ADD A BLOCKING WAIT TO THIS FILE OR TO EnemyAssetLoader.
// Assets/Editor/Regression/EnemyLoadBoundedRegression.cs fails the build if you do.
// The ONE allowlisted site is EnemyEditorSyncResolver.cs, entirely inside
// #if UNITY_EDITOR: the editor resolves through the AssetDatabase provider, so there
// is no bundle, no UnityWebRequest and no deadlock — and the batchmode gates run in
// EDIT MODE, where there is no player loop to warm from at all.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace DeNelle.Core
{
    /// <summary>Where the enemy-content discovery pass has got to this launch.</summary>
    public enum EnemyContentState
    {
        /// <summary>Nothing started yet (edit mode, or before the boot hook ran).</summary>
        Cold = 0,
        /// <summary>The discovery pass is running.</summary>
        Warming = 1,
        /// <summary>The catalog is up and enemy addresses exist — on-demand fetches will resolve.
        /// This does NOT claim anything is resident; enemy content is deliberately on demand.</summary>
        Ready = 2,
        /// <summary>No enemy addresses are reachable. Every enemy will degrade to a capsule until
        /// this is fixed. Never a hang — only a visual defect.</summary>
        Degraded = 3,
    }

    /// <summary>
    /// Async, per-family residency cache for enemy art. Discovers the enemy address space off
    /// the player loop, fetches families on demand, retains what it loads for the whole process,
    /// and answers <see cref="TryGet{T}"/> from memory so synchronous call sites never block.
    /// </summary>
    public static class EnemyContentWarmer
    {
        /// <summary>FlowTrace system tag. Shared with EnemyAssetLoader on purpose — a reader
        /// chasing a capsule enemy wants ONE tag, not two.</summary>
        public const string System = "EnemyAssets";

        /// <summary>Every enemy address carries this prefix (see EnemyAssetLoader).</summary>
        public const string AddressPrefix = "Enemies/";

        /// <summary>
        /// Prefix of the per-family Addressables LABEL. A family token becomes a label by
        /// concatenation, so this is the ONE place the label grammar is written down.
        /// <para>The declared set is authored in AddressableAssetSettings.asset and is NOT derived
        /// from <see cref="FamilyOf"/> — a derived token ("skeleton", "ogremage") is only a
        /// CANDIDATE. <see cref="IsDeclaredFamilyLabel"/> is how a caller finds out.</para>
        /// </summary>
        public const string FamilyLabelPrefix = "enemyfam-";

        /// <summary>Wall-clock budget for the discovery pass. A REPORTING deadline, not a timeout
        /// on a blocking call — nothing is blocked, so exceeding it costs a log line, never a frame.</summary>
        public const float WarmDeadlineSeconds = 45f;

        /// <summary>How long an address may sit unresolved before a Warn escalates to a Fail.
        /// Deliberately longer than the structure seam's: enemy content is fetched ON DEMAND at
        /// spawn time, so the first request of a family legitimately includes a download.</summary>
        public const float MissEscalateSeconds = 20f;

        // Resident assets, keyed by type+address. A hit here is the ONLY Addressables-backed
        // answer a synchronous caller can get at runtime, and it costs a dictionary probe.
        private static readonly Dictionary<string, Object> s_resident = new Dictionary<string, Object>();

        // ⛔ NEVER RELEASED, BY DESIGN. See (B) in the header: releasing is what lets a raid load
        // evict enemy content and put the next spawn back on the cold path.
        private static readonly List<AsyncOperationHandle> s_retained = new List<AsyncOperationHandle>();

        // Type+address keys with an in-flight typed request (dedupe — a wave asks per body).
        private static readonly HashSet<string> s_inFlight = new HashSet<string>();
        private static readonly Dictionary<string, string> s_inFlightFamily =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Type+address keys that resolved to nothing even asynchronously; stop re-requesting them.
        private static readonly HashSet<string> s_deadKeys = new HashSet<string>();

        // Families whose bundles have been asked for (dedupe — every body of a wave asks).
        private static readonly HashSet<string> s_familiesRequested = new HashSet<string>();

        // Requested is historical/dedupe state; this set means a family bundle writer is
        // active RIGHT NOW. Keeping them separate prevents a suppressed family prefetch from
        // making later typed requests wait forever.
        private static readonly HashSet<string> s_familyDownloadsInFlight =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Families whose bundle download has completed (diagnostics + the loader's report line).
        private static readonly HashSet<string> s_familiesLocal = new HashSet<string>();

        // A spawn commonly asks for a family prefetch and its first concrete body in the
        // same frame. Starting both operations makes two providers write/read the same cache
        // file concurrently. Queue typed loads behind the family download instead.
        private static readonly Dictionary<string, List<Action<bool>>> s_afterFamilyDownload =
            new Dictionary<string, List<Action<bool>>>(StringComparer.OrdinalIgnoreCase);

        // First time each address was asked for and could not be served. Powers the
        // "how long has it been waiting" number in every skip line.
        private static readonly Dictionary<string, float> s_firstMissAt = new Dictionary<string, float>();

        private static readonly List<Action> s_settleCallbacks = new List<Action>();
        private static readonly List<Action> s_deferred = new List<Action>();

        // Every string key Addressables knows about, harvested once during discovery.
        private static readonly HashSet<string> s_registeredKeys = new HashSet<string>();

        // Just the enemy ones, so a family sweep does not re-walk every locator.
        private static readonly List<string> s_enemyKeys = new List<string>();

        // Every DECLARED per-family label ("enemyfam-*"), harvested from the same locator walk.
        // Labels are catalog KEYS, so they arrive for free with the address harvest — no probe,
        // no network, and no hand-kept table that could drift from the settings asset.
        private static readonly HashSet<string> s_familyLabels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static Host s_host;
        private static bool s_warmStarted;

        /// <summary>Where the discovery pass has got to.</summary>
        public static EnemyContentState State { get; private set; } = EnemyContentState.Cold;

        /// <summary>True once discovery has finished or given up — either way it will not change again.</summary>
        public static bool IsSettled => State == EnemyContentState.Ready || State == EnemyContentState.Degraded;

        /// <summary>Typed addresses currently being fetched asynchronously.</summary>
        public static int PendingRequests => s_inFlight.Count;

        /// <summary>How many assets are resident (diagnostics + regression).</summary>
        public static int ResidentCount => s_resident.Count;

        /// <summary>How many handles are being retained for the life of the process.</summary>
        public static int RetainedHandleCount => s_retained.Count;

        /// <summary>How many enemy addresses the discovery pass found in the catalog.</summary>
        public static int DiscoveredAddressCount => s_enemyKeys.Count;

        /// <summary>How many enemy families have had their bundles asked for this launch.</summary>
        public static int RequestedFamilyCount => s_familiesRequested.Count;

        /// <summary>True when Addressables has a key for this exact address. Answered from a set
        /// harvested once during discovery — no catalog probe, no handle, no blocking.</summary>
        public static bool IsRegisteredAddress(string address) =>
            !string.IsNullOrEmpty(address) && s_registeredKeys.Contains(address);

        /// <summary>True when this family's bundles have finished downloading (or were already local).</summary>
        public static bool IsFamilyLocal(string family) =>
            !string.IsNullOrEmpty(family) && s_familiesLocal.Contains(family);

        /// <summary>The per-family Addressables label for a family token. Pure string work.</summary>
        public static string LabelFor(string family) =>
            string.IsNullOrWhiteSpace(family)
                ? string.Empty
                : FamilyLabelPrefix + family.Trim().ToLowerInvariant();

        /// <summary>
        /// True when this family's label is DECLARED in the catalog, i.e. asking Addressables for
        /// it will resolve instead of throwing InvalidKeyException.
        /// <para>⛔ Answers TRUE when discovery has not harvested any family label at all
        /// (<see cref="DeclaredFamilyLabelCount"/> == 0). That is deliberate: an empty harvest means
        /// "we do not know", and refusing every warm on a not-knowing would silently disable the
        /// whole 2026-08-20 per-family pre-fetch. Absence of evidence is not a refusal.</para>
        /// </summary>
        public static bool IsDeclaredFamilyLabel(string family)
        {
            if (string.IsNullOrWhiteSpace(family)) return false;
            if (s_familyLabels.Count == 0) return true;
            return s_familyLabels.Contains(LabelFor(family));
        }

        /// <summary>How many "enemyfam-*" labels discovery found in the catalog (0 = not harvested).</summary>
        public static int DeclaredFamilyLabelCount => s_familyLabels.Count;

        /// <summary>The declared family labels, comma-joined, for diagnostics.</summary>
        public static string DeclaredFamilyLabels =>
            s_familyLabels.Count == 0 ? "<none harvested>" : string.Join(", ", s_familyLabels);

        /// <summary>True when this family's bundles have been asked for and have not landed yet.</summary>
        public static bool IsFamilyDownloading(string family) =>
            !string.IsNullOrEmpty(family) && s_familyDownloadsInFlight.Contains(family);

        // =====================================================================
        //  Family identity
        // =====================================================================

        /// <summary>
        /// The family token of an enemy address or model slug — the part before the first
        /// underscore, e.g. "Enemies/Skeleton_Minion" and "Skeleton_Warrior" are both "Skeleton",
        /// "Orc_Berserker" is "Orc", "Boss_Dragon" is "Boss". This is the grouping the owner asked
        /// for on 2026-08-20 and it is derived from the ADDRESS, never from a hand-kept table that
        /// would drift the moment a model is added.
        /// </summary>
        public static string FamilyOf(string addressOrSlug)
        {
            if (string.IsNullOrWhiteSpace(addressOrSlug)) return string.Empty;
            string slug = addressOrSlug;
            if (slug.StartsWith(AddressPrefix, StringComparison.Ordinal))
                slug = slug.Substring(AddressPrefix.Length);
            int slash = slug.LastIndexOf('/');
            if (slash >= 0) slug = slug.Substring(slash + 1);
            int us = slug.IndexOf('_');
            return us > 0 ? slug.Substring(0, us) : slug;
        }

        // =====================================================================
        //  Synchronous read — a dictionary probe. CANNOT BLOCK.
        // =====================================================================

        /// <summary>
        /// True when <paramref name="address"/> is already resident as <typeparamref name="T"/>.
        /// A dictionary lookup and nothing else: no catalog probe, no handle, no pumping, no
        /// possibility of a wait. It is the whole reason the synchronous call-site shape survived.
        /// </summary>
        public static bool TryGet<T>(string address, out T asset) where T : Object
        {
            asset = null;
            if (string.IsNullOrWhiteSpace(address)) return false;

            if (s_resident.TryGetValue(Key(typeof(T), address), out var typed) && typed != null)
            {
                asset = typed as T;
                if (asset != null) return true;
            }

            // A family sweep may have parked something under Object (it loads by address, where
            // the concrete type is not known up front). A GameObject parked there is still a valid
            // GameObject answer.
            if (s_resident.TryGetValue(Key(typeof(Object), address), out var loose) && loose is T hit)
            {
                asset = hit;
                return true;
            }

            return false;
        }

        /// <summary>True when this typed address has already been proven absent from Addressables.
        /// Typed, deliberately: a missing CONTROLLER at an address must not condemn the PREFAB that
        /// shares it (the enemy address space disambiguates the two by asset type).</summary>
        public static bool IsKnownAbsent<T>(string address) where T : Object =>
            !string.IsNullOrEmpty(address) && s_deadKeys.Contains(Key(typeof(T), address));

        /// <summary>
        /// Seconds since this address was FIRST asked for and could not be served, or 0 the first
        /// time. Callers put this straight into their log line so a skip always says how long it
        /// has been waiting — the three minutes of silence happened because nothing said anything.
        /// </summary>
        public static float SecondsWaiting(string address)
        {
            if (string.IsNullOrEmpty(address)) return 0f;
            float now = Now();
            if (s_firstMissAt.TryGetValue(address, out float first)) return Mathf.Max(0f, now - first);
            s_firstMissAt[address] = now;
            return 0f;
        }

        // =====================================================================
        //  Asynchronous fetch — the ONLY way content enters the cache at runtime
        // =====================================================================

        /// <summary>
        /// Ask for <paramref name="address"/> as <typeparamref name="T"/> without waiting for it.
        /// Idempotent while in flight. The asset lands in the resident cache when it arrives and
        /// the caller finds it on a later attempt. Nothing here can block the calling frame.
        /// <para>The exact address pulls only its labelled family bundle. Ahead-of-wave family
        /// prefetch is issued separately from the canonical EnemyDef.Family.</para>
        /// </summary>
        public static void Request<T>(string address) where T : Object
        {
            if (string.IsNullOrWhiteSpace(address)) return;

            string key = Key(typeof(T), address);
            string family = FamilyOf(address).ToLowerInvariant();
            if (s_deadKeys.Contains(key)) return;
            if (s_resident.ContainsKey(key)) return;
            bool typedFamilyWriterActive = HasTypedRequestInFlight(family);
            if (!s_inFlight.Add(key)) return;
            s_inFlightFamily[key] = family;

            EnsureHost();
            // Do not infer a bundle label from the model spelling here. The address load below
            // already pulls its exact bundle; encounter lookahead calls WarmFamily with the
            // canonical EnemyDef.Family from enemies.json.

            if (AddressablesCacheHealth.UnsafeThisSession)
            {
                RejectUnsafeRequest<T>(address, key);
                return;
            }

            if (IsFamilyDownloading(family) || typedFamilyWriterActive)
            {
                if (!s_afterFamilyDownload.TryGetValue(family, out var waiting))
                {
                    waiting = new List<Action<bool>>();
                    s_afterFamilyDownload[family] = waiting;
                }
                waiting.Add(ok =>
                {
                    if (ok && !AddressablesCacheHealth.UnsafeThisSession)
                        StartRequest<T>(address, key);
                    else
                        RejectUnsafeRequest<T>(address, key);
                });
                FlowTrace.Step(System, $"'{address}' ({typeof(T).Name}) queued behind the in-flight '{family}' cache writer; the cache file has one writer.");
                return;
            }

            StartRequest<T>(address, key);
        }

        private static void StartRequest<T>(string address, string key) where T : Object
        {
            bool started = Guard.Try(System, $"async request '{address}' ({typeof(T).Name})", () =>
            {
                var handle = Addressables.LoadAssetAsync<T>(address);
                handle.Completed += h => OnRequestCompleted<T>(address, key, h);
            });

            if (!started)
            {
                // Guard already reported via FlowTrace.Fail. An address Addressables refuses
                // outright (InvalidKeyException) is dead for this launch; stop asking so a
                // re-skin loop cannot spin on it.
                s_inFlight.Remove(key);
                s_inFlightFamily.Remove(key);
                s_deadKeys.Add(key);
            }
        }

        private static void RejectUnsafeRequest<T>(string address, string key) where T : Object
        {
            s_inFlight.Remove(key);
            s_inFlightFamily.Remove(key);
            s_deadKeys.Add(key);
            FlowTrace.Fail(System, $"'{address}' ({typeof(T).Name}) was not loaded because the AssetBundle cache is unsafe this launch. A placeholder is retained; restart once to run the armed cache repair.");
            MaybeNotifySettled();
        }

        /// <summary>Untyped convenience — see <see cref="Request{T}"/>.</summary>
        public static void Request(string address) => Request<Object>(address);

        private static void OnRequestCompleted<T>(string address, string key, AsyncOperationHandle<T> handle)
            where T : Object
        {
            string family = FamilyOf(address).ToLowerInvariant();
            s_inFlight.Remove(key);
            s_inFlightFamily.Remove(key);

            bool ok = handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null;
            if (ok)
            {
                s_resident[key] = handle.Result;
                s_retained.Add(handle);          // ⛔ retained for the process; see header (B)
                FlowTrace.Step(System,
                    $"'{address}' ({typeof(T).Name}, family '{FamilyOf(address)}') arrived ASYNC after " +
                    $"{SecondsWaiting(address):F1}s and is now RESIDENT (retained={s_retained.Count}) — " +
                    "the next spawn or re-skin attempt will use the real art. It is never released, so " +
                    "the town -> raid -> town cycle cannot evict it.");
            }
            else
            {
                s_deadKeys.Add(key);
                FlowTrace.Fail(System,
                    $"async load of '{address}' ({typeof(T).Name}) FAILED ({handle.Status}) after " +
                    $"{SecondsWaiting(address):F1}s — every enemy using this asset renders as a TINTED " +
                    "CAPSULE for the rest of the launch. This is a GENUINELY MISSING ASSET, not a slow " +
                    "download: check the address exists in the enemy group and its bundle is uploaded. " +
                    "NOTE: this is a visual defect only; the game did NOT stall, which is the point.");
                Guard.Try(System, $"release failed handle '{address}'", () => Addressables.Release(handle));
            }

            CompleteFamilyWaiters(family, ok);
            MaybeNotifySettled();
        }

        /// <summary>
        /// Pull one FAMILY's bundles local, asynchronously — the owner's 2026-08-20 ruling in code.
        /// Downloads bundles only (cheap: disk, not memory) and loads NOTHING, so a wave of six
        /// skeletons costs one skeleton-sized fetch instead of the whole enemy payload. Idempotent
        /// per family per launch. Safe to call from anywhere, including an engine callback: it
        /// starts an operation and returns.
        /// </summary>
        public static void WarmFamily(string family)
        {
            if (string.IsNullOrWhiteSpace(family)) return;
            family = family.Trim().ToLowerInvariant();
            if (!s_familiesRequested.Add(family)) return;

            EnsureHost();

            // Catalog discovery can settle after the raid's first exact request has already
            // started. Do not then launch a label sweep over the same bundle: whichever path
            // started first remains the sole cache writer.
            if (HasTypedRequestInFlight(family))
            {
                FlowTrace.Step(System,
                    $"family '{family}' prefetch suppressed because an exact asset request already owns its cache write; one writer retained.");
                return;
            }

            string label = LabelFor(family);
            if (State != EnemyContentState.Ready)
            {
                // Not a defect on its own: discovery may not have run yet (a spawn in the first
                // frames), or this family may live entirely in a non-Addressable source. The
                // per-address Request still resolves it; this only forgoes the head start. Allow a
                // later retry rather than pinning the family as asked-for.
                s_familiesRequested.Remove(family);
                FlowTrace.Throttle(System, "fam-empty-" + family, 5f,
                    $"family '{family}' cannot be prefetched before catalog discovery settles " +
                    $"(label='{label}', discovered={s_enemyKeys.Count}, " +
                    $"state={State}) — no family pre-fetch, falling back to per-address requests. " +
                    "Expected before discovery settles; a DEFECT afterwards.");
                return;
            }

            // ⛔ UNDECLARED LABEL = a NAMED DEFECT, NOT AN ENGINE EXCEPTION (WO-1303).
            // DownloadDependenciesAsync throws InvalidKeyException on a key with no location, and
            // Guard.Try turns that into a red error every time an enemy of that family spawns —
            // which is how four F8 captures on 2026-09-02 read as content breakage when the real
            // fault was a caller passing the wrong string. The declared set is known here, so say
            // WHAT was asked for and WHAT exists, once per bad family, and do not throw.
            if (!IsDeclaredFamilyLabel(family))
            {
                s_familiesRequested.Remove(family);   // allow a retry if the catalog grows
                FlowTrace.Throttle(System, "fam-undeclared-" + family, 5f,
                    $"family '{family}' has NO declared label: '{label}' is not in the catalog. " +
                    $"Declared enemy family labels are [{DeclaredFamilyLabels}]. NOTHING was downloaded " +
                    "and NOTHING threw - the bodies still arrive via per-address requests, they just " +
                    "forgo the family head start. This is almost always a CALLER passing the wrong " +
                    "string (a controller name, or a model whose prefix is not its family - e.g. " +
                    "'Skeleton_Warrior' is family 'hollow', not 'skeleton'), NOT missing content. " +
                    "Fix the caller; do NOT add a label, which re-hashes every bundle (CLAUDE.md s16).");
                return;
            }

            bool started = Guard.Try(System, $"download family '{family}' by label '{label}'", () =>
            {
                s_familyDownloadsInFlight.Add(family);
                var dl = Addressables.DownloadDependenciesAsync(label, false);
                dl.Completed += h =>
                {
                    s_familyDownloadsInFlight.Remove(family);
                    bool ok = h.Status == AsyncOperationStatus.Succeeded;
                    if (ok) s_familiesLocal.Add(family);
                    else AddressablesCacheHealth.ReportDownloadFailure($"enemy family '{family}' / '{label}'");
                    FlowTrace.Step(System,
                        $"family '{family}' bundles {h.Status} via '{label}'. " +
                        "Only this family was fetched; the rest of the enemy payload was NOT downloaded.");
                    Guard.Try(System, $"release family download '{family}'", () => Addressables.Release(h));
                    CompleteFamilyWaiters(family, ok);
                    MaybeNotifySettled();
                };
            });

            if (!started)
            {
                s_familyDownloadsInFlight.Remove(family);
                s_familiesRequested.Remove(family);
            }
        }

        private static bool HasTypedRequestInFlight(string family)
        {
            foreach (var pair in s_inFlightFamily)
                if (string.Equals(pair.Value, family, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void CompleteFamilyWaiters(string family, bool ok)
        {
            if (!s_afterFamilyDownload.TryGetValue(family, out var waiting)) return;
            s_afterFamilyDownload.Remove(family);
            foreach (var resume in waiting)
                Guard.Try(System, $"resume request after family '{family}'", () => resume(ok));
        }

        // =====================================================================
        //  Settle notification + frame deferral
        // =====================================================================

        /// <summary>
        /// Run <paramref name="onSettled"/> once, the next time discovery has finished AND no
        /// requests are in flight. Fires on the player loop, never from an engine callback.
        /// </summary>
        public static void WhenSettled(Action onSettled)
        {
            if (onSettled == null) return;
            EnsureHost();
            s_settleCallbacks.Add(onSettled);
            // ⛔ DELIBERATELY NOT fired synchronously even when the condition already holds:
            // callers register from inside the work the callback re-runs. Host.Update drains it.
        }

        /// <summary>
        /// Run <paramref name="work"/> on the NEXT frame. The point is to get OFF an engine
        /// callback (SceneManager.sceneLoaded, OnEnable during a load) before touching content.
        /// If no host exists yet (edit mode, batchmode with no player loop) the work runs INLINE —
        /// an editor call has no deadlock to avoid and a silently-dropped action would be worse.
        /// </summary>
        public static void Defer(Action work)
        {
            if (work == null) return;
            EnsureHost();
            if (s_host == null)
            {
                Guard.Try(System, "run deferred work inline (no host)", work);
                return;
            }
            s_deferred.Add(work);
        }

        private static void MaybeNotifySettled()
        {
            if (s_settleCallbacks.Count == 0) return;
            if (!IsSettled) return;
            if (s_inFlight.Count > 0) return;

            var due = s_settleCallbacks.ToArray();
            s_settleCallbacks.Clear();
            foreach (var cb in due)
                Guard.Try(System, "settle callback", () => cb());
        }

        // =====================================================================
        //  The discovery pass (DISCOVERY ONLY — see the per-family note in the header)
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            EnsureHost();
            StartWarm();
        }

        /// <summary>Start the discovery pass if it has not run. Safe to call repeatedly.</summary>
        public static void StartWarm()
        {
            if (s_warmStarted) return;
            s_warmStarted = true;
            EnsureHost();
            if (s_host == null)
            {
                // No player loop (edit mode / batchmode). Nothing to discover from; the editor
                // resolver serves those callers synchronously and safely.
                State = EnemyContentState.Degraded;
                FlowTrace.Step(System, "no coroutine host (edit mode) — discovery skipped; " +
                                       "the editor sync resolver serves editor callers.");
                MaybeNotifySettled();
                return;
            }
            Guard.Try(System, "start enemy discovery pass", () => s_host.StartCoroutine(WarmRoutine()));
        }

        private static IEnumerator WarmRoutine()
        {
            using var _ = FlowTrace.Enter(System, "WarmRoutine");
            State = EnemyContentState.Warming;
            float t0 = Now();

            // --- 1. Addressables init, ASYNC, on the player loop. This is where the remote
            // catalog is fetched. Doing it here is what stops it happening synchronously inside
            // an enemy spawn — the old AddressableRegistered<T> probe triggered exactly this
            // initialisation from a blocking call and never said a word before it did.
            AsyncOperationHandle<IResourceLocator> init = default;
            bool initStarted = Guard.Try(System, "Addressables.InitializeAsync",
                () => { init = Addressables.InitializeAsync(false); });

            if (initStarted)
            {
                while (!init.IsDone && Now() - t0 < WarmDeadlineSeconds) yield return null;
                if (!init.IsDone)
                {
                    // Deliberately NOT released while running, and deliberately NOT waited on.
                    // Leaking one handle beats either alternative.
                    FlowTrace.Warn(System,
                        $"Addressables init still running after {Now() - t0:F1}s — continuing anyway. " +
                        "Nothing is blocked; enemies spawned before it lands wear capsules and re-skin later.");
                }
                else
                {
                    FlowTrace.Step(System, $"Addressables init {init.Status} in {Now() - t0:F1}s.");
                    Guard.Try(System, "release init handle", () => Addressables.Release(init));
                }
            }

            // --- 2. Discover the enemy address space from the in-memory locators. No labels are
            // authored on the enemy group (the grouper sets a GROUP, and a group name is not an
            // Addressables key), so a label query would silently match nothing. Reading locators
            // needs no network at all.
            Guard.Try(System, "enumerate enemy addresses", () =>
            {
                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator?.Keys == null) continue;
                    foreach (var k in locator.Keys)
                    {
                        if (!(k is string s)) continue;
                        s_registeredKeys.Add(s);
                        if (s.StartsWith(AddressPrefix, StringComparison.Ordinal) && !s_enemyKeys.Contains(s))
                            s_enemyKeys.Add(s);
                        // Labels are catalog keys too. Harvesting them here is what lets WarmFamily
                        // REFUSE an undeclared label instead of letting Addressables throw.
                        if (s.StartsWith(FamilyLabelPrefix, StringComparison.OrdinalIgnoreCase))
                            s_familyLabels.Add(s);
                    }
                }
            });

            // --- 3. THAT IS ALL. NOTHING IS DOWNLOADED AND NOTHING IS LOADED HERE.
            // ⛔ Do not "improve" this by pulling every address the way StructureContentWarmer
            // does. Owner ruling 2026-08-20: enemy content is fetched PER FAMILY, on demand. The
            // families a session actually uses arrive through WarmFamily/Request at spawn time;
            // eagerly warming them all is the 64 MB pull this seam exists to avoid.
            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < s_enemyKeys.Count; i++) families.Add(FamilyOf(s_enemyKeys[i]));

            State = DecideState(s_enemyKeys.Count);
            FlowTrace.Step(System,
                $"enemy discovery settled as {State} in {Now() - t0:F1}s (addresses={s_enemyKeys.Count}, " +
                $"families={families.Count}, resident={s_resident.Count} — resident=0 is CORRECT here, " +
                "enemy content is fetched per family on demand).");
            MaybeNotifySettled();
        }

        /// <summary>
        /// The ONE place <see cref="EnemyContentState.Ready"/> can be decided, so the reported
        /// state cannot drift from the achieved one.
        /// <para>⛔ NOTE THE DELIBERATE DIFFERENCE FROM StructureContentWarmer.DecideState: it
        /// refuses Warm when nothing is RESIDENT, because structures are warmed eagerly and
        /// resident==0 there means the pass silently did nothing. Here resident==0 is the
        /// EXPECTED state — content is on demand — so asserting residency would paint a
        /// permanent false red. What Ready still cannot survive is ZERO DISCOVERED ADDRESSES:
        /// that means every enemy in the game will be a capsule, and it is reported as such.</para>
        /// </summary>
        private static EnemyContentState DecideState(int discovered)
        {
            if (discovered == 0)
            {
                FlowTrace.Fail(System,
                    $"discovery found NO '{AddressPrefix}' addresses in the catalog. Assets/Resources/Enemies " +
                    "no longer exists (the content moved to Addressables), so there is NOTHING left to fall " +
                    "back to: EVERY enemy this launch renders as a tinted capsule. Reporting Degraded, never " +
                    "Ready. Check the enemy group shipped and its catalog/bundles are reachable. NOTE: this " +
                    "is a visual defect only — nothing blocks and nothing hangs.");
                return EnemyContentState.Degraded;
            }

            return EnemyContentState.Ready;
        }

        // =====================================================================
        //  Plumbing
        // =====================================================================

        private static string Key(Type t, string address) => t.Name + ":" + address;

        private static float Now() => Time.realtimeSinceStartup;

        private static void EnsureHost()
        {
            if (s_host != null) return;
            if (!Application.isPlaying) return;   // no player loop to host a coroutine on

            Guard.Try(System, "create enemy warm host", () =>
            {
                var go = new GameObject("EnemyContentWarmer");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_host = go.AddComponent<Host>();
            });
        }

        /// <summary>
        /// DontDestroyOnLoad coroutine host. Also drains <see cref="Defer"/> work each frame —
        /// the "get off the engine callback" seam, which must run from Update (the player loop)
        /// for that to mean anything.
        /// </summary>
        private sealed class Host : MonoBehaviour
        {
            private readonly List<Action> _drain = new List<Action>();

            private void Update()
            {
                // WO-1483: town frame path. 4-arg (accumulating) Measure — see
                // FlowTrace.cs:293-308. Never the 3-arg form on a per-frame site.
                using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                    "Perf", "EnemyContentWarmer.Host.Update", 4f, 1f);

                if (s_deferred.Count > 0)
                {
                    _drain.Clear();
                    _drain.AddRange(s_deferred);
                    s_deferred.Clear();
                    for (int i = 0; i < _drain.Count; i++)
                    {
                        var work = _drain[i];
                        Guard.Try(System, "deferred work", () => work());
                    }
                }

                MaybeNotifySettled();
            }

            private void OnDestroy()
            {
                if (s_host == this) s_host = null;
            }
        }
    }
}
