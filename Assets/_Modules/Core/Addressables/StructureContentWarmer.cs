// =============================================================================
// StructureContentWarmer — the ASYNC side of the structure art seam, and the
// reason StructureAssetLoader can no longer hang the game.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core (Core/Addressables). References Addressables only — no
// Village/HUD types, so any module can warm/read it.
//
// ⛔ THE P0 THIS EXISTS FOR (captured 2026-08-20, Seeker device, dungeon -> town):
//   08-20 10:25:14.917 [Flow:VisualFactory] -> Skin('Structures/barracks')
//     ... HubStructureVisualInjector:OnSceneLoaded(Scene, LoadSceneMode)
//   ...and then NOTHING, ever again. Device clock reached 10:28:35 while Unity's
//   last log line was 10:25:14.917 — THREE MINUTES of total silence from the
//   process. Alive, foregrounded, stale frame, no ANR (Android only raises those
//   on input-dispatch timeout, and nothing was dispatching).
//
// ⛔ IT WAS NOT A SLOW NETWORK. The owner tested the device WHILE IT WAS HUNG:
//   Wi-Fi associated (SSID Casa-Denelle), ping to the R2 CDN host = 2/2 packets,
//   0% loss, 31.5 ms avg. The CDN was healthy at the exact moment the main thread
//   was dead. A TIMEOUT WOULD NOT HAVE HELPED — the operation was not slow, it was
//   UNABLE TO PROGRESS AT ALL.
//
// ⛔ THE ACTUAL MECHANISM, verified against the Addressables 2.9.1 source that
// ships in this project (Library/PackageCache/com.unity.addressables@8460f1c9c927),
// NOT from memory or from a doc:
//
//   1. AsyncOperationBase.WaitForCompletion (AsyncOperations/AsyncOperationBase.cs:171)
//      is a BARE, UNINTERRUPTIBLE, UNBOUNDED BUSY-SPIN:
//          while (!InvokeWaitForCompletion()) { }
//      No timeout parameter. No yield. No exit condition other than the operation
//      completing. THERE IS NO BOUNDED-SYNCHRONOUS-WAIT API IN THIS PACKAGE — that
//      is a fact about the vendor code, not a design preference of ours.
//
//   2. ProviderOperation.InvokeWaitForCompletion (AsyncOperations/ProviderOperation.cs:66)
//      can return FALSE FOREVER: it pumps m_RM.Update(...) and then
//          if (m_WaitForCompletionCallback == null) return false;
//      so any operation whose provider has not installed a completion callback
//      spins the loop above until the process dies.
//
//   3. AssetBundleResource.WaitForCompletionHandler (ResourceProviders/AssetBundleProvider.cs:543)
//      — the remote-bundle path — does, ON THE MAIN THREAD:
//          if (m_RequestOperation == null) { if (m_WebRequestQueueOperation == null) return false; ... }
//          while (!UnityWebRequestUtilities.IsAssetBundleDownloaded(op))
//              System.Threading.Thread.Sleep(k_WaitForWebRequestMainThreadSleep);
//      i.e. it SLEEPS the main thread waiting on progress that the engine's own
//      player loop is responsible for driving. It also defers BeginOperation()
//      behind m_UnloadOperation — the bundle-unload op — which is exactly what a
//      dungeon->town transition creates.
//
//   Put (1)+(2)+(3) inside a SceneManager.sceneLoaded engine callback — a nested
//   engine call the player loop cannot re-enter — and the thread that would drive
//   the operation to completion is the thread blocked waiting for it. That is a
//   textbook deadlock, and it explains every observed property: silent, permanent,
//   intermittent (a bundle already resident in memory returns without needing to
//   pump), and correlated with the owner's constant town -> dungeon -> town loop
//   (the dungeon load is what evicts the structure content).
//
// -----------------------------------------------------------------------------
// SO THE FIX IS NOT A TIMEOUT. IT IS: NEVER BLOCK, AND NEVER BLOCK FROM A CALLBACK.
//
//   A. This warmer does ALL Addressables work ASYNCHRONOUSLY, from a coroutine on
//      a DontDestroyOnLoad host — i.e. from the player loop, the one place the
//      ResourceManager can actually be pumped.
//   B. Everything it resolves is RETAINED FOREVER (s_retained is never released).
//      That is deliberate and is the anti-eviction measure: the owner runs
//      town -> dungeon -> town constantly, and a released handle lets the bundle
//      unload and re-download on every single cycle — which is what puts the game
//      back on the cold path where the deadlock lives.
//   C. Callers keep their SYNCHRONOUS shape via TryGet, which is a dictionary
//      lookup and cannot block, ever, for any reason.
//   D. Defer() exists so a scene-load callback can hand work to the NEXT FRAME
//      instead of doing it inside the engine callback. Getting off the callback is
//      half the fix; the other half is C.
//
// ⛔ DO NOT ADD WaitForCompletion() TO THIS FILE OR TO StructureAssetLoader.
// Assets/Editor/Regression/StructureLoadBoundedRegression.cs fails the build if you
// do. The ONE allowlisted site is StructureEditorSyncResolver.cs, which is entirely
// inside #if UNITY_EDITOR: the editor uses the AssetDatabase play-mode provider, so
// there is no bundle, no UnityWebRequest and no deadlock — and the batchmode gates
// need a synchronous answer with no player loop to warm from.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
// ⚠ `using System.Text;` rather than a qualified `System.Text.StringBuilder`: this class declares
// `public const string System`, which shadows the System NAMESPACE inside the class body, so any
// `System.X` member access here binds the const and fails to compile.
using System.Text;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;
using DeNelle.Core.Platform;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace DeNelle.Core
{
    /// <summary>Where the structure-art warm pass has got to this launch.</summary>
    public enum StructureContentState
    {
        /// <summary>Nothing started yet (edit mode, or before the boot hook ran).</summary>
        Cold = 0,
        /// <summary>The warm pass is running.</summary>
        Warming = 1,
        /// <summary>The warm pass finished; content bundles are local.</summary>
        Warm = 2,
        /// <summary>The warm pass gave up (deadline / failure). Loads still work on demand,
        /// they just have no head start. Never a hang — only a degraded look.</summary>
        Degraded = 3,
    }

    /// <summary>
    /// Async residency cache for structure art. Warms the Addressables content set off the
    /// player loop, retains what it loads for the whole process, and answers
    /// <see cref="TryGet{T}"/> from memory so synchronous call sites never block.
    /// </summary>
    public static class StructureContentWarmer
    {
        /// <summary>FlowTrace system tag. Shared with StructureAssetLoader on purpose — a
        /// reader chasing a missing building wants one tag, not two.</summary>
        public const string System = "StructureAssets";

        /// <summary>Every structure address carries this prefix (see StructureAssetLoader).</summary>
        public const string AddressPrefix = "Structures/";

        /// <summary>
        /// Wall-clock budget for the whole warm pass. This is a REPORTING deadline, not a
        /// timeout on a blocking call — nothing is blocked, so exceeding it costs a log line
        /// and a degraded look, never a frame. (There is no such thing as a timeout on
        /// WaitForCompletion; see the header.)
        /// </summary>
        public const float WarmDeadlineSeconds = 45f;

        /// <summary>How long an address may sit unresolved before a Warn escalates to a Fail.</summary>
        public const float MissEscalateSeconds = 10f;

        // Resident assets, keyed by type+address. A hit here is the ONLY Addressables-backed
        // answer a synchronous caller can get at runtime, and it costs a dictionary probe.
        private static readonly Dictionary<string, Object> s_resident = new Dictionary<string, Object>();

        // ⛔ NEVER RELEASED, BY DESIGN. See (B) in the header: releasing is what lets the
        // dungeon load evict structure content and put the next town load back on the cold
        // path. The memory is the price of not deadlocking, and it is the right trade.
        private static readonly List<AsyncOperationHandle> s_retained = new List<AsyncOperationHandle>();

        // Addresses with an in-flight async request (dedupe — a skipped skin re-asks every reapply).
        private static readonly HashSet<string> s_inFlight = new HashSet<string>();
        // The residency queue. Named "pi" historically because Pi Browser was the only host that
        // used it; PROD-022's concurrency knob lets ANY host route through it, so read the name as
        // "the serialised queue" rather than as a platform claim.
        private static readonly Queue<string> s_piQueue = new Queue<string>();

        // Addresses that resolved to nothing even asynchronously; stop re-requesting them.
        private static readonly HashSet<string> s_deadAddresses = new HashSet<string>();

        // PROD-022 Lane B — WHY did it fail, not just THAT it failed.
        // The old failure line printed `handle.Status` and nothing else. `Failed` is an effect;
        // "HTTP 404", "request timeout after 20s", "Cannot connect to destination host" are causes,
        // and on Pi Browser (iPhone) the whole open question is WHICH of those the webview produced
        // while the R2 objects were verified 200-and-public from the open internet the same hour.
        // Addressables parks the real story in AsyncOperationHandle.OperationException — usually a
        // RemoteProviderException whose message carries the URL, the UnityWebRequest.result and the
        // HTTP status. We flatten that chain ONCE per address and keep it, so every downstream
        // "model not found" line can name the cause instead of restating the effect.
        private static readonly Dictionary<string, string> s_failureCause = new Dictionary<string, string>();

        // PROD-022 Lane B — bound the residency retry storm.
        // A miss re-requests on the next skin attempt, and a hub re-apply can drive several skin
        // attempts per address per second. On Pi that turned into the same address cycling
        // -> Skin / not found / <- Skin through a session's final seconds with no upper bound.
        // Each address now gets a fixed budget of async attempts; the cap is LOGGED, never silent
        // (CLAUDE.md §12 — the whole point is that giving up says so).
        private static readonly Dictionary<string, int> s_attempts = new Dictionary<string, int>();

        // =====================================================================
        //  PROD-022 — THE REMOTELY TUNABLE KNOBS.
        // ---------------------------------------------------------------------
        //  ⭐ EVERY ONE OF THESE RESOLVES TO ITS OLD HARDCODED VALUE WHEN THERE IS NO
        //  DATABASE ROW, NO NETWORK, OR NO SERVER. RemoteTunables.Registry holds the
        //  defaults and they are the values this file shipped with; the owner-facing
        //  list is docs/PROD022_TUNABLE_FLAGS.md. A build with an empty client_tunables
        //  table behaves exactly like the build before PROD-022 touched it.
        //
        //  These are PROPERTIES, not consts, so a flip in the database takes effect on
        //  the next read rather than at the next 30-minute WebGL rebuild. That is the
        //  entire point (owner ruling 2026-09-02: "all we really have to do is just
        //  flip a flag and possibly redeploy").
        // =====================================================================

        /// <summary>The value <see cref="MaxRequestAttempts"/> had before it was tunable, mirrored
        /// here so a reader can see the shipping number without opening the registry.</summary>
        public const int DefaultMaxRequestAttempts = 3;

        /// <summary>The value <see cref="PiRequestTimeoutSeconds"/> had before it was tunable.</summary>
        public const int DefaultPiRequestTimeoutSeconds = 20;

        /// <summary>How many async fetches a single address may be given before it is retired for
        /// this launch. Default 3 = one cold attempt plus two recoveries from a transient webview
        /// stall. Clamped to at least 1: a budget of zero would retire every address on sight and
        /// there is no diagnosis in a town with no art and no fetches.</summary>
        public static int MaxRequestAttempts =>
            Mathf.Max(1, RemoteTunables.Int(RemoteTunables.KeyAssetsMaxRequestAttempts));

        /// <summary>
        /// The Pi Browser Addressables request timeout, in seconds. Default 20 — UNCHANGED by
        /// PROD-022, deliberately: the root is not proven and picking a new constant would bake in
        /// a guess. It is tunable so the number can be moved by DATA instead. Clamped to at least 1
        /// because a timeout of zero means "no timeout" to UnityWebRequest, which is the captive-
        /// portal hang this project has already been bitten by.
        /// </summary>
        public static int PiRequestTimeoutSeconds =>
            Mathf.Max(1, RemoteTunables.Int(RemoteTunables.KeyPiRequestTimeoutSeconds));

        /// <summary>Ceiling on residency fetches in flight at once. 0 = today (Pi serialises through
        /// its own latch; desktop is unbounded).</summary>
        private static int ConcurrencyCap =>
            Mathf.Max(0, RemoteTunables.Int(RemoteTunables.KeyAssetsMaxConcurrentRequests));

        /// <summary>ON = issue no remote structure request at all on Pi. Default OFF.</summary>
        private static bool RemoteStructureArtDisabled =>
            WebGLPiPlatform.IsPiBrowserEnvironment &&
            RemoteTunables.Bool(RemoteTunables.KeyPiDisableRemoteStructureArt);

        /// <summary>ON = Pi runs the full desktop warm pass instead of on-demand. Default OFF.</summary>
        private static bool PiEagerWarm =>
            RemoteTunables.Bool(RemoteTunables.KeyPiEagerStructureWarm);

        /// <summary>ON = Pi awaits Addressables init + harvests keys before the first load. Default OFF.</summary>
        private static bool PiAwaitInitBeforeFirstLoad =>
            RemoteTunables.Bool(RemoteTunables.KeyPiAwaitInitBeforeFirstLoad);

        /// <summary>
        /// Narration gate for this file's Step lines. ⛔ Warn and Fail NEVER route through here —
        /// CLAUDE.md §12 is binding and a failure that stops being logged is the bug this whole
        /// system exists to avoid. Only the success narration is dimmable.
        /// </summary>
        private static void TraceStep(int minVerbosity, string message)
        {
            if (RemoteTunables.Int(RemoteTunables.KeyTraceAssetVerbosity) < minVerbosity) return;
            FlowTrace.Step(System, message);
        }

        // PROD-022 — the shared residency queue. On Pi with no cap set this behaves EXACTLY like
        // the old `s_piRequestActive` latch (one at a time); with a cap of N it admits N. On any
        // other host with no cap set the queue is bypassed entirely, which is today's unbounded
        // desktop behaviour. s_activeRequests replaces the old bool.
        private static int s_activeRequests;

        // PROD-022 knob 2 — the await-init gate. Only ever armed on Pi, only when the knob is ON.
        private static bool s_piPrewarmStarted;
        private static bool s_piPrewarmDone;

        // PROD-022 Lane B: how many transport-level requests this launch has issued, and the last URL
        // Addressables asked for. On Pi this is the ONLY place the real URL is observable from managed
        // code — the address is ours, the URL is the catalog's, and a 404-vs-timeout argument cannot be
        // settled without seeing which one was fetched.
        private static int s_webRequests;
        private static string s_lastRequestUrl;

        // PROD-022 Lane B (coordinator addendum, owner-supplied WebGL domain knowledge): a HANDLE on the
        // most recent UnityWebRequest, kept so the failure path can read its responseCode + result.
        //
        // ⛔ WHY A TEXT-ONLY CLASSIFIER IS NOT ENOUGH, and this is the whole reason the reference is kept:
        // on WebGL a CORS / preflight rejection surfaces as responseCode == 0 with NO distinctive message
        // text at all. Classified by text alone it lands in CONNECTION or UNCLASSIFIED — i.e. the trace
        // says "the socket dropped it" when the truth is "the browser refused to hand the response to the
        // page". Those two have OPPOSITE fixes, so conflating them sends the next seat the wrong way.
        // The numeric code is the only thing that separates them, and it lives on the request, not on the
        // exception.
        //
        // ⚠ CORRELATION CAVEAT, stated rather than assumed: this is the LAST request issued, not
        // necessarily the one belonging to the failing address. On Pi it is very likely the same one —
        // the Pi policy serialises requests, so only one is active at a time — but a single asset load
        // can issue several (bundle + dependencies). Every line built from it prints the URL as well, so
        // a reader can check the correlation instead of trusting it.
        private static UnityEngine.Networking.UnityWebRequest s_lastRequest;

        // First time each address was asked for and could not be served. Powers the
        // "how long did it wait" number in the Warn/Fail lines.
        private static readonly Dictionary<string, float> s_firstMissAt = new Dictionary<string, float>();

        private static readonly List<Action> s_settleCallbacks = new List<Action>();
        private static readonly List<Action> s_deferred = new List<Action>();

        // Every string key Addressables knows about, harvested once during the warm pass. Used by
        // DependencyClosureTrace to tell a REAL double-ship (address registered AND Resources
        // answered) from content that legitimately still lives in Resources.
        private static readonly HashSet<string> s_registeredKeys = new HashSet<string>();

        private static Host s_host;
        private static bool s_warmStarted;
        private static int s_discovered;

        // 2026-09-09 P0 — WHICH PHASE OF THE WARM PASS WAS RUNNING.
        // The warm pass has no single "address" to name when it dies (init and the bulk download
        // cover the whole key set), so a throw could only ever be reported as "somewhere in
        // WarmRoutine". One word per stage turns the survival wrapper's Fail line into a location.
        private static string s_warmPhase = "not-started";

        /// <summary>Where the warm pass has got to.</summary>
        public static StructureContentState State { get; private set; } = StructureContentState.Cold;

        /// <summary>True once the warm pass has finished or given up — either way it will not change again.</summary>
        public static bool IsSettled => State == StructureContentState.Warm || State == StructureContentState.Degraded;

        /// <summary>Addresses currently being fetched asynchronously.</summary>
        public static int PendingRequests => s_inFlight.Count;

        /// <summary>How many assets are resident (diagnostics + regression).</summary>
        public static int ResidentCount => s_resident.Count;

        /// <summary>How many handles are being retained for the life of the process.</summary>
        public static int RetainedHandleCount => s_retained.Count;

        /// <summary>How many structure addresses the warm pass found in the catalog.</summary>
        public static int DiscoveredAddressCount => s_discovered;

        /// <summary>
        /// True when Addressables has a key for this exact address. Answered from a set harvested
        /// once during the warm pass -- no catalog probe, no handle, no blocking.
        /// <para>This exists to keep the Resources-fallback report HONEST. A fallback is only a
        /// double-ship when the address is ALSO registered; content that was never migrated (e.g.
        /// the four ~33-156 KB Harvest/* FBXs, deliberately Resources-resident per the note at
        /// HarvestSite.cs:274) is answering from the only place it lives.</para>
        /// </summary>
        public static bool IsRegisteredAddress(string address) =>
            !string.IsNullOrEmpty(address) && s_registeredKeys.Contains(address);

        // =====================================================================
        //  Synchronous read — a dictionary probe. CANNOT BLOCK.
        // =====================================================================

        /// <summary>
        /// True when <paramref name="address"/> is already resident as <typeparamref name="T"/>.
        /// This is a dictionary lookup and nothing else: no catalog probe, no handle, no
        /// pumping, no possibility of a wait. It is the whole reason the synchronous call-site
        /// shape survived this fix.
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

            // The warm pass stores under Object (it loads by location, where the concrete type
            // is not known up front). A GameObject parked there is still a valid GameObject
            // answer — resolve it here rather than making the warm pass type-aware.
            if (s_resident.TryGetValue(Key(typeof(Object), address), out var loose) && loose is T hit)
            {
                asset = hit;
                return true;
            }

            return false;
        }

        /// <summary>
        /// PROD-022: the flattened UNDERLYING reason this address last failed to fetch — HTTP status,
        /// <c>UnityWebRequest.result</c>, timeout-vs-protocol-error, and the RemoteProviderException
        /// text when Addressables supplied one. Null when the address has never failed.
        /// <para>Downstream loggers (StructureAssetLoader, VisualFactory) append this so the line the
        /// reader actually finds — "model not found" — states the CAUSE. Before PROD-022 that line was
        /// emitted after the fact and carried no network detail at all, which is why a Pi Browser
        /// session's final seconds proved only that something had gone wrong.</para>
        /// </summary>
        public static string LastFailureCause(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            return s_failureCause.TryGetValue(address, out var cause) ? cause : null;
        }

        /// <summary>How many async fetches this address has already been given (PROD-022 retry cap).</summary>
        public static int AttemptsFor(string address)
        {
            if (string.IsNullOrEmpty(address)) return 0;
            return s_attempts.TryGetValue(address, out int n) ? n : 0;
        }

        /// <summary>True when this address has already been proven absent from Addressables.</summary>
        public static bool IsKnownAbsent(string address) =>
            !string.IsNullOrEmpty(address) && s_deadAddresses.Contains(address);

        /// <summary>
        /// Seconds since this address was FIRST asked for and could not be served, or 0 the
        /// first time. Callers put this straight into their log line so a skip always says how
        /// long it has been waiting — the three minutes of silence happened because nothing
        /// ever said anything.
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
        /// Ask for <paramref name="address"/> without waiting for it. Idempotent (a repeat
        /// while in flight is a no-op) and silent about the common "not registered" case.
        /// The asset lands in the resident cache when it arrives; the caller finds it on a
        /// later attempt. Nothing here can block the calling frame.
        /// </summary>
        public static void Request(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;

            // PROD-022 knob 3 — THE STREAMING KILL SWITCH (pi.disableRemoteStructureArt).
            // Default OFF, so this branch does not exist in a build with no database row.
            //
            // ⭐ WHY IT DEGRADES CLEANLY AND CANNOT BLANK THE TOWN: returning here is
            // INDISTINGUISHABLE, to every caller, from an address that has simply not arrived
            // yet — which is a state they all already handle and have handled since this file
            // was written. StructureAssetLoader returns null, HubStructureVisualInjector
            // re-enables the baked renderers and keeps the baked twin, and the Structure keeps
            // its visible pending-art proxy. Nothing waits, nothing stalls, nothing is hidden.
            //
            // It is the BIG HAMMER and it is decisive in BOTH directions: if the crash loop
            // stops with this on, asset streaming is implicated beyond argument; if it
            // continues, streaming is exonerated and the cause is elsewhere.
            if (RemoteStructureArtDisabled)
            {
                FlowTrace.Throttle(System, "req-killswitch-" + address, 5f,
                    $"on-demand SUPPRESSED '{address}': the PROD-022 streaming kill switch " +
                    $"({RemoteTunables.KeyPiDisableRemoteStructureArt}) is ON, so NO remote structure " +
                    "request is issued on this host. The caller keeps its baked twin / pending-art " +
                    "proxy — this is a deliberate fidelity-for-signal trade, not a failure. Set the " +
                    "row to 0 to restore streaming; no rebuild is needed.");
                return;
            }

            // PROD-022 Lane B — SAY WHY WE DID NOT ASK. Every one of these three returns used to be
            // silent, so a reader watching the same address repeat "model not found" could not tell
            // whether we were re-asking the CDN every frame or had stopped asking entirely on the
            // first failure. Throttled to ~1 line per address per 5s: bounded, but never absent.
            if (s_deadAddresses.Contains(address))
            {
                FlowTrace.Throttle(System, "req-dead-" + address, 5f,
                    $"on-demand SKIP '{address}': address is RETIRED for this launch " +
                    $"(attempts={AttemptsFor(address)}/{MaxRequestAttempts}) — no further fetch will be " +
                    $"issued. Last cause: {LastFailureCause(address) ?? "unrecorded"}");
                return;
            }
            if (s_resident.ContainsKey(Key(typeof(Object), address)))
            {
                FlowTrace.Throttle(System, "req-resident-" + address, 5f,
                    $"on-demand SKIP '{address}': already RESIDENT — a caller is still reporting a miss, " +
                    "so the miss is a TYPE mismatch in TryGet, not a fetch failure.");
                return;
            }
            if (!s_inFlight.Add(address))
            {
                FlowTrace.Throttle(System, "req-inflight-" + address, 5f,
                    $"on-demand SKIP '{address}': a fetch is ALREADY IN FLIGHT " +
                    $"(inFlight={s_inFlight.Count}, queueDepth={s_piQueue.Count}, " +
                    $"activeRequests={s_activeRequests}, waited={SecondsWaiting(address):F1}s). " +
                    "The caller keeps its proxy; this is the expected shape while a bundle downloads.");
                return;
            }

            // PROD-022 Lane B — THE RETRY BUDGET. Spend one attempt, and retire the address once the
            // budget is gone. This is deliberately AFTER the in-flight dedupe (a re-ask while one is
            // already running is not an attempt) and BEFORE any work is started, so the cap bounds
            // network + decompression, not merely the logging.
            s_attempts.TryGetValue(address, out int attempts);
            attempts++;
            s_attempts[address] = attempts;

            if (attempts > MaxRequestAttempts)
            {
                s_inFlight.Remove(address);
                s_deadAddresses.Add(address);
                // ⛔ LOUD, NEVER SILENT. Giving up quietly is how the Pi trace ended with nothing but
                // an effect line repeating. This says the budget is spent, and names the last cause.
                FlowTrace.Fail(System,
                    $"RETRY CAP: '{address}' has now failed {MaxRequestAttempts} async fetch attempt(s) — " +
                    "retiring it for the rest of this launch; the caller keeps its pending-art proxy or " +
                    $"baked twin and will NOT ask again. Last cause: {LastFailureCause(address) ?? "unrecorded"}");
                // ⚠ Deliberately does NOT touch s_activeRequests: nothing was ever dequeued for this
                // address on this pass, so releasing a slot here would admit a DIFFERENT request
                // while one is genuinely still in flight and run two multi-MB downloads at once —
                // the exact concurrency the Pi policy exists to prevent.
                MaybeNotifySettled();
                return;
            }

            EnsureHost();

            // PROD-022 — ROUTE THROUGH THE SERIALISED QUEUE?
            //   * Pi Browser, always: today's behaviour, unchanged (one at a time).
            //   * Any host with assets.maxConcurrentRequests >= 1: the knob is armed.
            //   * Otherwise (desktop, knob at its default 0): straight through, unbounded —
            //     which is byte-for-byte today's desktop path.
            int cap = ConcurrencyCap;
            if (WebGLPiPlatform.IsPiBrowserEnvironment || cap >= 1)
            {
                s_piQueue.Enqueue(address);
                StartNextQueued();
                return;
            }

            StartRequest(address, queued: false);
        }

        /// <summary>
        /// Admit as many queued addresses as the ceiling allows.
        /// <para>
        /// ⭐ WITH THE KNOB AT ITS DEFAULT 0 THE CEILING IS 1, which is precisely the old
        /// <c>s_piRequestActive</c> latch this replaced — one request at a time on Pi, and the
        /// queue untouched on every other host because Request() never enqueues there. A cap of
        /// N admits N, which is the concurrency hypothesis made testable without a rebuild.
        /// </para>
        /// </summary>
        private static void StartNextQueued()
        {
            // PROD-022 knob 2 — hold everything while the await-init prewarm is running. Nothing
            // is dropped; the queue drains the moment init lands (or its deadline passes).
            if (s_piPrewarmStarted && !s_piPrewarmDone) return;

            int cap = ConcurrencyCap;
            int ceiling = cap >= 1 ? cap : 1;

            while (s_piQueue.Count > 0 && s_activeRequests < ceiling)
            {
                s_activeRequests++;
                StartRequest(s_piQueue.Dequeue(), queued: true);
            }
        }

        /// <summary>Give back one concurrency slot and admit whatever that lets through.</summary>
        private static void ReleaseSlotAndPump()
        {
            if (s_activeRequests > 0) s_activeRequests--;
            StartNextQueued();
        }

        private static void StartRequest(string address, bool queued)
        {
            // PROD-022 Lane B — the on-demand branch's own decision points, traced. Desktop never
            // reaches here for structures (its warm pass has already made them resident), so every
            // line below describes a code path ONLY Pi Browser executes. What it prints is what a
            // reader needs to separate "we never asked" from "we asked and it did not come back":
            // which address, whether the catalog can even be expected to know it yet, how deep the
            // Pi serialisation queue is, and how many attempts this address has already spent.
            float startedAt = Now();
            TraceStep(RemoteTunables.VerbosityVerbose,
                $"on-demand START '{address}' attempt={AttemptsFor(address)}/{MaxRequestAttempts} " +
                $"queueDepth={s_piQueue.Count} activeRequests={s_activeRequests} " +
                $"inFlight={s_inFlight.Count} resident={s_resident.Count} " +
                $"state={State} registeredKeysHarvested={s_registeredKeys.Count} " +
                $"webRequestsSoFar={s_webRequests}.");

            bool started = Guard.Try(System, $"async request '{address}'", () =>
            {
                var handle = Addressables.LoadAssetAsync<Object>(address);
                // The handle identity + its state AT ISSUE TIME. A handle that is already Failed on
                // the very next line means the catalog answered synchronously (no location) — a
                // completely different defect from a handle that stays None for 20s and then times
                // out, and the two were indistinguishable before this line existed.
                // ⛔ status via StatusText, NOT handle.Status: a handle that is already invalid on
                // the next line would throw, this whole lambda would report `started=false`, and the
                // address would be RETIRED AS DEAD for the launch by the !started branch below — a
                // permanent art loss caused purely by a diagnostic. PercentComplete is DROPPED from
                // this line for the same reason — it also reads through InternalOp and throws on an
                // invalid handle, and it told a reader nothing that isDone + status do not.
                TraceStep(RemoteTunables.VerbosityVerbose,
                    $"on-demand HANDLE '{address}': valid={handle.IsValid()} status={StatusText(handle)} " +
                    $"isDone={handle.IsDone} " +
                    $"issuedIn={(Now() - startedAt) * 1000f:0.0}ms.");
                handle.Completed += h => OnRequestCompleted(address, h, queued);
            });

            if (!started)
            {
                // Guard already reported via FlowTrace.Fail. An address Addressables refuses
                // outright (InvalidKeyException) is dead for this launch; stop asking so the
                // reapply loop cannot spin on it.
                s_inFlight.Remove(address);
                s_deadAddresses.Add(address);
                // PROD-022: record a cause even here, so the downstream "model not found" line is
                // never the only thing a reader finds. This branch is a THROW from the call itself
                // (no handle exists to interrogate) — a different failure shape from a completed-but-
                // failed operation, and the trace now says which of the two happened.
                s_failureCause[address] = "Addressables refused the request synchronously (the " +
                    "LoadAssetAsync call itself threw — typically InvalidKeyException: no location " +
                    "for this address in the loaded catalog). See the Guard Fail line immediately " +
                    "above for the exception text. | classified=KEY-MISSING";
                if (queued) ReleaseSlotAndPump();
            }
        }

        private static void OnRequestCompleted(string address, AsyncOperationHandle<Object> handle, bool queued)
        {
            s_inFlight.Remove(address);

            // ⛔ THE HANDLE IS NOT AUTOMATICALLY SAFE JUST BECAUSE THIS IS THE Completed CALLBACK.
            // Status and Result both read through InternalOp and THROW on an invalid handle
            // (see TryReadStatus), and a throw here escapes into Addressables' own completion
            // invoker — where it is even less recoverable than the coroutine throw that blanked the
            // town on 2026-09-09. Ask IsValid() first, and NAME THE ADDRESS when it is not.
            if (!TryReadStatus(handle, out var completedStatus))
            {
                s_failureCause[address] = "the AsyncOperationHandle was already INVALID when its own " +
                    "Completed callback ran — the operation had been released/recycled by another " +
                    "owner, so no status, result or exception could be read off it. " +
                    "| classified=HANDLE-INVALID";
                FlowTrace.Fail(System,
                    $"async load of '{address}' completed on an INVALID handle after " +
                    $"{SecondsWaiting(address):F1}s — nothing can be read off it, so the asset is " +
                    "treated as NOT arrived. The address keeps its retry budget " +
                    $"({AttemptsFor(address)}/{MaxRequestAttempts}) and is NOT retired, because an " +
                    "invalid handle proves nothing about the bytes. No Release is attempted: " +
                    "releasing an already-released handle is what creates the next invalid one.");
                MaybeNotifySettled();
                if (queued) ReleaseSlotAndPump();
                return;
            }

            if (completedStatus == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                s_resident[Key(typeof(Object), address)] = handle.Result;
                s_retained.Add(handle);          // ⛔ retained for the process; see header (B)
                float waited = SecondsWaiting(address);
                TraceStep(RemoteTunables.VerbosityNormal,
                    $"'{address}' arrived ASYNC after {waited:F1}s and is now RESIDENT " +
                    $"(retained={s_retained.Count}) — the next skin attempt will use it. " +
                    "It is never released, so the dungeon->town cycle cannot evict it.");
            }
            else
            {
                // PROD-022 Lane B: read the CAUSE off the handle before releasing it. The old line
                // printed handle.Status ("Failed") and stopped there, so a Pi Browser session could
                // only prove that something went wrong — never whether it was the 20s request
                // timeout, an HTTP 404, or the webview refusing the connection outright. Those three
                // point at three completely different fixes.
                string cause = DescribeFailure(handle);
                s_failureCause[address] = cause;

                int attempts = AttemptsFor(address);
                bool budgetSpent = attempts >= MaxRequestAttempts;

                // Retire only when the budget is spent. Before PROD-022 the FIRST failure retired the
                // address permanently, which meant one transient webview stall cost that building its
                // art for the whole launch. The budget is what makes a retry meaningful AND bounded.
                if (budgetSpent) s_deadAddresses.Add(address);

                FlowTrace.Fail(System,
                    $"async load of '{address}' FAILED ({completedStatus}) after {SecondsWaiting(address):F1}s " +
                    $"on attempt {attempts}/{MaxRequestAttempts}. CAUSE: {cause}. " +
                    (budgetSpent
                        ? "Retry budget SPENT — retiring this address for the rest of the launch; the " +
                          "caller keeps its baked twin / pending-art proxy."
                        : "Retry budget remains — the next skin attempt may re-request it.") +
                    " NOTE: this is a visual defect only; the game did NOT stall, which is the whole " +
                    "point of this path.");
                Guard.Try(System, $"release failed handle '{address}'", () => Addressables.Release(handle));
            }

            MaybeNotifySettled();
            if (queued) ReleaseSlotAndPump();
        }

        // =====================================================================
        //  Settle notification + frame deferral
        // =====================================================================

        /// <summary>
        /// Run <paramref name="onSettled"/> once, the next time the warm pass has finished AND
        /// no requests are in flight. Used by HubStructureVisualInjector to re-apply skins that
        /// it had to skip. Fires on the player loop, never from an engine callback.
        /// </summary>
        public static void WhenSettled(Action onSettled)
        {
            if (onSettled == null) return;
            EnsureHost();
            s_settleCallbacks.Add(onSettled);
            // ⛔ DELIBERATELY NOT fired synchronously, even when the condition already holds.
            // Callers register from INSIDE the work the callback re-runs (ApplyAll arms it), so a
            // synchronous fire would re-enter that work mid-pass. Host.Update drains it on a later
            // frame, which is also the only place it is safely off any engine callback.
        }

        /// <summary>
        /// Run <paramref name="work"/> on the NEXT frame. The point is to get OFF an engine
        /// callback (SceneManager.sceneLoaded) before touching content: the captured hang
        /// happened inside Internal_SceneLoaded, where the player loop cannot re-enter to drive
        /// the very operation the callback is waiting on. If no host exists yet (edit mode,
        /// batchmode with no player loop) the work runs INLINE — an editor call has no deadlock
        /// to avoid and a silently-dropped action would be worse.
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

            // PROD-022 knob 2 — while the await-init prewarm is pending, "settled" is a lie: the
            // Pi branch reports Degraded from frame one, so IsSettled is TRUE before the catalog
            // has produced a single location and a WhenSettled retry would fire into nothing.
            // Holding here is the third of the three divergences the on-demand branch's Warn names.
            // With the knob at its default OFF, s_piPrewarmStarted is false and this line is inert.
            if (s_piPrewarmStarted && !s_piPrewarmDone) return;

            var due = s_settleCallbacks.ToArray();
            s_settleCallbacks.Clear();
            foreach (var cb in due)
                Guard.Try(System, "settle callback", () => cb());
        }

        // =====================================================================
        //  The warm pass
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // Pi Browser runs inside an Android WebView with a much tighter practical
            // memory ceiling than desktop WebGL. Loading and retaining all 35 prefabs at
            // boot can kill the renderer, which the host recreates as an apparent game
            // reset. On Pi, leave structures strictly on-demand and serialize requests.
            // PROD-022 — say WHICH configuration produced this session, at the moment the policy
            // is chosen. A felt-test capture whose configuration cannot be reconstructed is a
            // wasted felt-test, and this is the line that makes it reconstructable.
            RemoteTunables.LogConfiguration("StructureContentWarmer.Boot");

            if (WebGLPiPlatform.IsPiBrowserEnvironment)
            {
                // The Pi request timeout override is installed on EVERY Pi path, eager or not:
                // it is a transport bound and a transport instrument, and neither belongs to the
                // residency policy.
                Addressables.WebRequestOverride = InstrumentedPiWebRequest;

                // PROD-022 knob 1 — pi.eagerStructureWarm. Default OFF, so this branch does not
                // exist in a build with no database row and the on-demand policy below is what
                // runs, unchanged.
                //
                // ⛔ WO-PROD-022 forbids re-enabling eager residency on Pi WITHOUT PROOF, and this
                // is how the proof gets gathered rather than assumed: the eager path is the SAME
                // desktop WarmRoutine, reached through the same StartWarm, so there is no second
                // implementation to drift.
                if (PiEagerWarm)
                {
                    FlowTrace.Warn(System,
                        "Pi Browser policy OVERRIDDEN by " + RemoteTunables.KeyPiEagerStructureWarm +
                        "=ON: running the FULL desktop warm pass (await Addressables init, harvest " +
                        "keys, download dependencies, load and retain every structure prefab) on a " +
                        "memory-capped webview. This is a PROD-022 experiment, not the shipping " +
                        "policy — set the row to 0 to restore on-demand streaming, no rebuild needed.");
                    EnsureHost();
                    StartWarm();
                    return;
                }

                s_warmStarted = true;
                State = StructureContentState.Degraded;
                FlowTrace.Step(System,
                    "Pi Browser policy: eager structure download/residency disabled; " +
                    $"{PiRequestTimeoutSeconds}s Addressables request timeout installed; assets load on demand.");

                // PROD-022 knob 2 — pi.awaitInitBeforeFirstLoad. Default OFF, so the divergence
                // Warn below still describes this build truthfully when no row is set.
                //
                // ⭐ THIS IS THE PRIME SUSPECT AND IT IS DELIBERATELY NARROW. It changes ONE thing:
                // the catalog is initialised and the key set harvested BEFORE the first on-demand
                // load is issued. Residency policy is untouched — nothing is downloaded eagerly and
                // nothing is retained that would not have been. Requests raised meanwhile are
                // QUEUED, not dropped, and drain the instant init lands or its deadline passes.
                if (PiAwaitInitBeforeFirstLoad)
                {
                    FlowTrace.Warn(System,
                        "Pi on-demand policy AUGMENTED by " + RemoteTunables.KeyPiAwaitInitBeforeFirstLoad +
                        "=ON: Addressables.InitializeAsync will be awaited and every registered key " +
                        "harvested BEFORE the first on-demand load is issued. Requests raised in the " +
                        "meantime are queued, never dropped. Residency policy is UNCHANGED — this is " +
                        "not the eager warm.");
                    EnsureHost();
                    s_piPrewarmStarted = true;
                    if (s_host == null)
                    {
                        // No player loop to host the coroutine (edit mode / batchmode). Do not hold
                        // the queue hostage to a coroutine that can never run.
                        s_piPrewarmDone = true;
                        FlowTrace.Warn(System,
                            "await-init prewarm requested but there is NO coroutine host (edit mode / " +
                            "batchmode). The gate is released immediately and behaviour falls back to " +
                            "the on-demand default — the knob cannot stall a hostless process.");
                    }
                    else if (!Guard.Try(System, "start Pi await-init prewarm",
                                        () => { s_host.StartCoroutine(PiPrewarmRoutine()); }))
                    {
                        // ⛔ THE GATE MUST NEVER OUTLIVE ITS COROUTINE. If StartCoroutine threw,
                        // nothing will ever set s_piPrewarmDone, and StartNextQueued would hold
                        // every residency request for the life of the launch — a NEW failure mode
                        // introduced by a diagnostic knob, which is exactly what a crash-loop
                        // ticket must not ship. Release it here and say so.
                        s_piPrewarmDone = true;
                        FlowTrace.Fail(System,
                            "await-init prewarm FAILED TO START (see the Guard line above). The gate " +
                            "has been released so residency requests proceed exactly as they do with " +
                            "the knob OFF. Nothing is held.");
                    }
                }

                // PROD-022 Lane B — NAME WHAT THIS BRANCH SKIPS. Desktop reaches residency through
                // WarmRoutine, which awaits Addressables.InitializeAsync, harvests every registered
                // key into s_registeredKeys, and only then loads. This branch does NONE of that: it
                // returns immediately and lets the first on-demand Request() be the first thing that
                // ever touches Addressables. Desktop Chrome on the identical build showed ZERO
                // "not resident" lines; Pi is the only host on this path, so the divergence is worth
                // stating in the trace rather than inferring from two files later.
                // ⚠ THE WORDING IS CONDITIONAL, AND IT HAS TO BE. This Warn asserts three concrete
                // facts about the running build; knob 2 changes all three. A trace line that
                // describes the OTHER configuration is worse than no line at all, because it is the
                // line a future seat will quote as evidence.
                if (!s_piPrewarmStarted)
                {
                    FlowTrace.Warn(System,
                        "Pi on-demand branch DIVERGES from the desktop warm path in three ways, recorded " +
                        "here so a trace reader does not have to derive them: (1) Addressables.InitializeAsync " +
                        "is NOT awaited — the first on-demand request is the first touch of the catalog; " +
                        "(2) no key harvest, so IsRegisteredAddress() answers false for EVERY address this " +
                        "launch and any 'is it registered' report from this host is meaningless; " +
                        "(3) State is reported Degraded from frame one, so IsSettled is TRUE immediately and " +
                        "every WhenSettled retry fires as soon as the in-flight count reaches zero — " +
                        "possibly before the catalog has produced a single location. " +
                        "ALL THREE ARE ADDRESSED BY " + RemoteTunables.KeyPiAwaitInitBeforeFirstLoad +
                        "=1, which is OFF in this session.");
                }
                else
                {
                    FlowTrace.Warn(System,
                        "Pi on-demand branch is running WITH " + RemoteTunables.KeyPiAwaitInitBeforeFirstLoad +
                        "=ON, so the usual three divergences are CLOSED for this session: init IS awaited " +
                        "before the first load, keys ARE harvested (IsRegisteredAddress is meaningful), and " +
                        "WhenSettled is HELD until the prewarm finishes instead of firing at frame one. " +
                        "Residency policy is otherwise unchanged — assets still load on demand.");
                }

                MaybeNotifySettled();
                return;
            }
            // AfterSceneLoad, matching OfflineContentBootstrap: coroutines need a live scene.
            // This is deliberately NOT a barrier — blocking the boot on content is the family
            // of bug this file exists to end.
            EnsureHost();
            StartWarm();
        }

        /// <summary>
        /// PROD-022 knob 2 — await Addressables init and harvest the key set BEFORE the first
        /// on-demand load. Runs ONLY on Pi Browser and ONLY when
        /// <c>pi.awaitInitBeforeFirstLoad</c> is ON; the default build never enters here.
        /// <para>
        /// ⛔ IT CANNOT HANG. The wait is bounded by <see cref="WarmDeadlineSeconds"/> and the gate
        /// is released in EVERY exit path, including the deadline path and the "init would not even
        /// start" path. Nothing downstream awaits this coroutine; the queue simply drains later.
        /// </para>
        /// <para>
        /// It deliberately does NOT download or load anything. The only difference from the
        /// default is WHEN the first request is issued and whether the key set is known — which is
        /// precisely the divergence the on-demand branch's own Warn has been naming since PROD-022
        /// Lane B, now made testable.
        /// </para>
        /// </summary>
        private static IEnumerator PiPrewarmRoutine()
        {
            float t0 = Now();

            AsyncOperationHandle<IResourceLocator> init = default;
            bool initStarted = Guard.Try(System, "Addressables.InitializeAsync (Pi prewarm)",
                () => { init = Addressables.InitializeAsync(false); });

            if (initStarted)
            {
                while (!init.IsDone && Now() - t0 < WarmDeadlineSeconds) yield return null;

                // ⛔ IsValid() FIRST — IsDone is TRUE for an invalid handle, so the old ladder sent
                // the invalid case into the .Status read and threw. Same trap, same file, same fix
                // as WarmRoutine; see TryReadStatus for the source citations. A throw here would
                // have left s_piPrewarmDone false forever and held EVERY residency request for the
                // life of the launch — the exact failure the Guard at the call site was written to
                // prevent, arriving one layer deeper.
                if (!init.IsValid())
                {
                    FlowTrace.Fail(System,
                        $"Pi prewarm: the Addressables INIT handle is INVALID after {Now() - t0:F1}s " +
                        "(the shared initialisation operation was released by whoever started it — " +
                        "AddressablesImpl.cs:359-360 + :420-421). Skipping the status read and the " +
                        "key harvest; the gate is still released below, so nothing is held.");
                }
                else if (!init.IsDone)
                {
                    // Deliberately NOT released while it is still running, and deliberately NOT
                    // waited on. Leaking one handle beats blocking, and beats cancelling the very
                    // initialisation the on-demand loads are about to need.
                    FlowTrace.Warn(System,
                        $"Pi prewarm: Addressables init still running after {Now() - t0:F1}s " +
                        $"(deadline {WarmDeadlineSeconds}s) — releasing the request gate anyway. " +
                        "Behaviour from here is the knob-OFF default; the init is neither cancelled " +
                        "nor waited on.");
                }
                else
                {
                    TraceStep(RemoteTunables.VerbosityNormal,
                        $"Pi prewarm: Addressables init {StatusText(init)} in {Now() - t0:F1}s.");
                    Guard.Try(System, "release Pi prewarm init handle", () => Addressables.Release(init));

                    // Harvest the key set. This is the second half of the divergence: without it
                    // IsRegisteredAddress answers false for EVERY address on this host, so any
                    // "is it registered" report from a Pi session is meaningless.
                    Guard.Try(System, "harvest registered keys (Pi prewarm)", () =>
                    {
                        foreach (var locator in Addressables.ResourceLocators)
                        {
                            if (locator?.Keys == null) continue;
                            foreach (var k in locator.Keys)
                                if (k is string s) s_registeredKeys.Add(s);
                        }
                    });

                    int structureKeys = 0;
                    foreach (var k in s_registeredKeys)
                        if (k.StartsWith(AddressPrefix, StringComparison.Ordinal)) structureKeys++;

                    FlowTrace.Warn(System,
                        $"Pi prewarm COMPLETE in {Now() - t0:F1}s: {s_registeredKeys.Count} registered " +
                        $"key(s) harvested, {structureKeys} of them under '{AddressPrefix}'. " +
                        (structureKeys == 0
                            ? "⛔ ZERO structure addresses are in the loaded catalog. That is the answer " +
                              "to PROD-022's 'model not found' storm and it is a CATALOG problem, not a " +
                              "transport one — the bytes were never findable, so no timeout, retry budget " +
                              "or concurrency cap could have helped."
                            : "The catalog knows these addresses, so a later 'model not found' is a " +
                              "TRANSPORT failure and its CAUSE line names which one."));
                }
            }
            else
            {
                FlowTrace.Fail(System,
                    "Pi prewarm: Addressables.InitializeAsync would not even start (see the Guard line " +
                    "above). Releasing the request gate — behaviour is the knob-OFF default.");
            }

            // EVERY path lands here. The gate is released exactly once.
            s_piPrewarmDone = true;
            StartNextQueued();
            MaybeNotifySettled();
        }

        /// <summary>Start the warm pass if it has not run. Safe to call repeatedly.</summary>
        public static void StartWarm()
        {
            if (s_warmStarted) return;
            s_warmStarted = true;
            EnsureHost();
            if (s_host == null)
            {
                // No player loop (edit mode / batchmode). Nothing to warm from; the editor
                // resolver serves those callers synchronously and safely.
                State = StructureContentState.Degraded;
                FlowTrace.Step(System, "no coroutine host (edit mode) — warm pass skipped; " +
                                       "the editor sync resolver serves editor callers.");
                MaybeNotifySettled();
                return;
            }
            Guard.Try(System, "start structure warm pass", () => s_host.StartCoroutine(RunWarmPass()));
        }

        /// <summary>
        /// ⛔ THE SURVIVAL WRAPPER (2026-09-09 P0). It pumps the warm pass by hand so that ANY throw
        /// out of it is CAUGHT, NAMED, and turned into a TERMINAL state — instead of killing the
        /// coroutine where nothing is watching.
        /// <para>
        /// ⛔ WHY THE TERMINAL STATE IS THE SEVERE HALF, not the tidy half.
        /// <see cref="MaybeNotifySettled"/> returns early while <see cref="IsSettled"/> is false, and
        /// IsSettled is <c>Warm || Degraded</c>. A pass that dies mid-flight therefore leaves State
        /// pinned on <c>Warming</c> FOREVER, so every <see cref="WhenSettled"/> callback — including
        /// HubStructureVisualInjector's re-apply of the skins it had to skip — never fires again.
        /// That is why the owner's town stayed blank rather than filling in as the on-demand fetches
        /// landed, and why the device's miss line still read <c>warmerState=Warming</c> eight seconds
        /// after the throw. A non-terminal state is also a LIE to every diagnostic that prints it:
        /// "Warming" reads as "still in flight" when the truth is "the pass is dead".
        /// </para>
        /// <para>Why a wrapper rather than try/catch inside the pass: C# forbids <c>yield return</c>
        /// inside a try block that has a catch. Pumping the inner enumerator here puts the yield
        /// OUTSIDE the try, which is legal, costs nothing, and needs no restructuring of the pass.</para>
        /// </summary>
        private static IEnumerator RunWarmPass()
        {
            var inner = WarmRoutine();

            while (true)
            {
                object current = null;
                bool moved = false;
                bool faulted = false;

                try
                {
                    moved = inner.MoveNext();
                    if (moved) current = inner.Current;
                }
                catch (Exception ex)
                {
                    faulted = true;
                    FlowTrace.Fail(System,
                        $"warm pass THREW during phase '{s_warmPhase}' and has been stopped: " +
                        $"{ex.GetType().Name}: {Flatten(ex.Message)}. Counts at the throw: " +
                        $"discovered={s_discovered}, resident={s_resident.Count}, " +
                        $"inFlight={s_inFlight.Count}, retained={s_retained.Count}, " +
                        $"registeredKeys={s_registeredKeys.Count}. The pass is NOT restarted, but it " +
                        "is no longer allowed to leave the warmer stuck on Warming — see the terminal " +
                        "state below. On-demand Request() still works and callers keep their " +
                        "pending-art proxy, so this is a degraded look, never a stall.");
                }

                if (faulted || !moved) break;
                yield return current;
            }

            // ⛔ TERMINAL STATE, ON EVERY PATH. Reached on a clean finish (where WarmRoutine has
            // already decided Warm/Degraded via DecideState, so this is inert) and on a throw (where
            // nothing has decided anything). 'Warming' must never survive this line.
            if (State == StructureContentState.Warming)
            {
                State = StructureContentState.Degraded;
                FlowTrace.Fail(System,
                    $"warm pass ENDED without deciding a state (phase '{s_warmPhase}', " +
                    $"discovered={s_discovered}, resident={s_resident.Count}). Forcing DEGRADED so " +
                    "IsSettled becomes true and the WhenSettled re-apply can finally run. Reporting " +
                    "'Warming' forever is itself a defect: it told the 2026-09-09 miss lines the " +
                    "bytes were 'still in flight' when the pass had been dead for 8 seconds.");
            }

            MaybeNotifySettled();
        }

        private static IEnumerator WarmRoutine()
        {
            using var _ = FlowTrace.Enter(System, "WarmRoutine");
            State = StructureContentState.Warming;
            s_warmPhase = "init";
            float t0 = Now();

            // --- 1. Addressables init. THIS is where the remote catalog is fetched, and doing
            // it here (async, on the player loop) is what stops it happening inside a scene-load
            // callback via a WaitForCompletion. The old AddressableRegistered<T> probe triggered
            // exactly this initialisation synchronously and never said a word before it did.
            AsyncOperationHandle<IResourceLocator> init = default;
            bool initStarted = Guard.Try(System, "Addressables.InitializeAsync",
                () => { init = Addressables.InitializeAsync(false); });

            if (initStarted)
            {
                while (!init.IsDone && Now() - t0 < WarmDeadlineSeconds) yield return null;

                // ⛔ IsValid() IS TESTED FIRST, AND THE ORDER IS THE WHOLE FIX. IsDone is TRUE for
                // an invalid handle (see TryReadStatus), so an `if (!IsDone) ... else <read Status>`
                // ladder sends the invalid case into the branch that throws. This was the ROOT of
                // the 2026-09-09 P0.
                if (!init.IsValid())
                {
                    // ⛔ WHY A HANDLE WE NEVER RELEASED GOES INVALID — read at source, not inferred:
                    // Addressables hands EVERY caller the same shared m_InitializationOperation once
                    // initialisation has started (AddressablesImpl.cs:359-360), and arms
                    // ReleaseHandleOnCompletion on that operation when the FIRST caller used the
                    // default autoReleaseHandle=true (:420-421). Addressables' own chain does exactly
                    // that (:107, reached by any load issued before init), and so does
                    // EnemyFamilyPullProbe.cs:33. Our `InitializeAsync(false)` only protects the
                    // operation when WE are the first caller; on the shared-return path the `false`
                    // never reaches :421, the op self-releases on completion, is recycled, its
                    // Version is bumped — and this copy of the handle is stale.
                    FlowTrace.Fail(System,
                        $"Addressables INIT handle is INVALID after {Now() - t0:F1}s: something else " +
                        "owns the shared initialisation operation and released it on completion " +
                        "(AddressablesImpl.cs:359-360 + :420-421). Reading .Status here is what threw " +
                        "'Attempting to use an invalid operation handle' on 2026-09-09 (device capture " +
                        "seq=4708, t=6.07s) and KILLED this coroutine — after which not one structure " +
                        "address was ever requested and the whole town rendered as pending-art proxies. " +
                        "The pass now CONTINUES: an invalid handle means the operation is finished and " +
                        "gone, and the locator enumeration below needs no handle at all.");
                }
                else if (!init.IsDone)
                {
                    // Deliberately NOT released while it is still running, and deliberately NOT
                    // waited on. Leaking one handle beats either alternative.
                    FlowTrace.Warn(System,
                        $"Addressables init still running after {Now() - t0:F1}s — continuing anyway. " +
                        "Nothing is blocked; the town will simply show baked twins until it lands.");
                }
                else
                {
                    FlowTrace.Step(System, $"Addressables init {StatusText(init)} in {Now() - t0:F1}s.");
                    Guard.Try(System, "release init handle", () => Addressables.Release(init));
                }
            }

            // --- 2. Discover every structure address from the in-memory locators. No labels are
            // authored on the Structure_Art group (verified in StructureAddressablesMigrator —
            // it sets a GROUP, and a group name is not an Addressables key), so a label query
            // would silently match nothing. Reading the locators needs no network at all.
            s_warmPhase = "enumerate";
            var keys = new List<string>();
            Guard.Try(System, "enumerate structure addresses", () =>
            {
                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator?.Keys == null) continue;
                    foreach (var k in locator.Keys)
                    {
                        if (!(k is string s)) continue;
                        s_registeredKeys.Add(s);   // full key set -- powers IsRegisteredAddress
                        if (s.StartsWith(AddressPrefix, StringComparison.Ordinal) && !keys.Contains(s))
                            keys.Add(s);
                    }
                }
            });

            FlowTrace.Step(System, $"warm pass found {keys.Count} structure address(es) under '{AddressPrefix}'.");

            // --- 3. Pull the bundles local. DownloadDependenciesAsync costs disk, not memory,
            // so this is cheap next to loading every prefab — and once the bundle is local the
            // on-demand Request() above resolves from a file instead of the CDN.
            if (keys.Count > 0)
            {
                s_warmPhase = "download";
                AsyncOperationHandle dl = default;
                bool dlStarted = Guard.Try(System, "DownloadDependenciesAsync(structures)",
                    () => { dl = Addressables.DownloadDependenciesAsync(keys, Addressables.MergeMode.Union, false); });

                if (dlStarted)
                {
                    while (!dl.IsDone && Now() - t0 < WarmDeadlineSeconds) yield return null;

                    // ⛔ IsValid() FIRST, for the same reason as the init handle above: IsDone is
                    // TRUE for an invalid handle, so testing it first routes the invalid case into
                    // the .Status read and throws out of the coroutine.
                    if (!dl.IsValid())
                    {
                        FlowTrace.Fail(System,
                            $"structure download handle is INVALID after {Now() - t0:F1}s for " +
                            $"{keys.Count} address(es) under '{AddressPrefix}' — the dependency " +
                            "operation was released or recycled by someone other than us. NOT fatal " +
                            "and NOT waited on: the residency phase below re-asks per address through " +
                            "Request(), which is the path that actually makes assets resident. This " +
                            "branch exists so the pass CONTINUES instead of throwing here, which is " +
                            "how the 2026-09-09 blank town happened one handle earlier.");
                    }
                    else if (!dl.IsDone)
                    {
                        FlowTrace.Warn(System,
                            $"structure content still downloading after {Now() - t0:F1}s (deadline {WarmDeadlineSeconds}s) — " +
                            "reporting DEGRADED and moving on. The download is NOT cancelled and NOT waited on; " +
                            "assets that land later still become resident.");
                    }
                    else
                    {
                        FlowTrace.Step(System,
                            $"structure content download {StatusText(dl)} in {Now() - t0:F1}s.");
                        Guard.Try(System, "release download handle", () => Addressables.Release(dl));
                    }
                }
            }

            // --- 4. LOAD AND RETAIN.
            // THIS PHASE IS THE WHOLE POINT AND IT WAS MISSING IN THE FIRST VERSION OF THIS FILE,
            // which shipped to the owner's device on 2026-08-20 and logged, verbatim:
            //     structure content download Succeeded in 2.8s.
            //     warm pass settled as Warm in 2.8s (resident=0, inFlight=0, retained=0).
            // DownloadDependenciesAsync makes the BUNDLES local. It does not put a single ASSET in
            // the resident dictionary. So TryGet missed for every address, the loader fell through
            // to a Resources copy that the CDN migration had DELETED, and build-mode placement
            // ghosts rendered as placeholder PILLS. Warm was a marker claiming a state it had not
            // reached -- the same overclaiming defect class we spent the day removing.
            //
            // COST, MEASURED NOT GUESSED: the Structure_Art group holds exactly 35 addresses
            // (24 of them the catalog's complete visualPrefabPath/upgradeVisualPath set -- full
            // coverage, no gaps) totalling 28.9 MB of source bytes on disk. That is the SAME set
            // DownloadDependenciesAsync already pulled above in 2.8 s on the device. Loading all
            // 35 is therefore the cheap option, not the expensive one, and it removes every reason
            // for a synchronous caller with no retry seam (BuildModeController's ghost) to miss.
            //
            // Request() is reused deliberately: it already dedupes, hooks Completed, indexes into
            // s_resident and retains the handle. One code path, so a late arrival after the
            // deadline still lands instead of being dropped.
            if (keys.Count > 0)
            {
                s_warmPhase = "residency";
                for (int i = 0; i < keys.Count; i++) Request(keys[i]);
                FlowTrace.Step(System,
                    $"warm pass requested {keys.Count} structure asset(s) for RESIDENCY " +
                    "(downloading a bundle is not the same as holding an asset).");

                while (s_inFlight.Count > 0 && Now() - t0 < WarmDeadlineSeconds) yield return null;

                if (s_inFlight.Count > 0)
                    FlowTrace.Warn(System,
                        $"{s_inFlight.Count} structure asset(s) still loading after {Now() - t0:F1}s -- " +
                        "NOT waited on and NOT cancelled; they still become resident when they land.");
            }

            s_warmPhase = "settle";
            s_discovered = keys.Count;
            State = DecideState(s_discovered, s_resident.Count);
            FlowTrace.Step(System,
                $"warm pass settled as {State} in {Now() - t0:F1}s (discovered={s_discovered}, " +
                $"resident={s_resident.Count}, inFlight={s_inFlight.Count}, retained={s_retained.Count}).");
            s_warmPhase = "done";
            MaybeNotifySettled();
        }

        /// <summary>
        /// The ONE place <see cref="StructureContentState.Warm"/> can be decided, so the reported
        /// state cannot drift from the achieved one.
        /// <para>Warm REQUIRES resident content. On 2026-08-20 this pass reported Warm with
        /// resident=0 and the owner got placeholder pills instead of buildings; nothing checked
        /// whether the marker was true. A state that cannot be falsified is not a state, it is a
        /// wish. StructureLoadBoundedRegression fails the suite if this guard is removed.</para>
        /// </summary>
        private static StructureContentState DecideState(int discovered, int resident)
        {
            if (discovered == 0)
            {
                FlowTrace.Warn(System,
                    $"warm pass found NO '{AddressPrefix}' addresses in the catalog. That is the expected " +
                    "state pre-migration and a DEFECT afterwards (35 are authored in the Structure_Art " +
                    "group today). Reporting DEGRADED -- callers fall back to Resources or keep their " +
                    "current visual.");
                return StructureContentState.Degraded;
            }

            if (resident == 0)
            {
                FlowTrace.Fail(System,
                    $"warm pass discovered {discovered} structure address(es) but NOT ONE is resident. " +
                    "Reporting DEGRADED, never Warm. This is the exact 2026-08-20 'pills loading' " +
                    "regression: bundles downloaded, assets never loaded, TryGet missed everything, " +
                    "and Assets/Resources/Structures no longer exists to catch it. Buildings and " +
                    "placement ghosts render as placeholders until this is fixed.");
                return StructureContentState.Degraded;
            }

            if (resident < discovered)
            {
                FlowTrace.Warn(System,
                    $"warm pass is only PARTIALLY resident ({resident}/{discovered}). The missing " +
                    "addresses render as placeholders or keep their baked twin; each one failed " +
                    "with its own line above. Reporting Warm because the fast path does exist.");
            }

            return StructureContentState.Warm;
        }

        // =====================================================================
        //  Plumbing
        // =====================================================================

        private static string Key(Type t, string address) => t.Name + ":" + address;

        /// <summary>
        /// Read <c>handle.Status</c> WITHOUT the throw that killed the warm pass on 2026-09-09.
        /// Returns false when the handle is not valid; <paramref name="status"/> is then
        /// <see cref="AsyncOperationStatus.None"/> and MUST NOT be printed as a real status.
        /// <para>
        /// ⛔ THE TRAP, AND IT IS THE OPPOSITE OF WHAT THE NAMES SUGGEST. Verified in the
        /// Addressables that ships in this project
        /// (Library/PackageCache/com.unity.addressables@8460f1c9c927,
        /// Runtime/ResourceManager/AsyncOperations/AsyncOperationHandle.cs):
        ///   * <c>IsDone</c> (:220 generic, :475 non-generic) is
        ///         <c>!IsValid() || InternalOp.IsDone</c>
        ///     — so an INVALID handle reports itself DONE.
        ///   * <c>Status</c> (:283 / :555) goes through <c>InternalOp</c>, which THROWS
        ///     <c>"Attempting to use an invalid operation handle"</c> (:210 / :465).
        /// Put those together and the idiom this file used everywhere —
        /// <code>
        ///   while (!h.IsDone &amp;&amp; notPastDeadline) yield return null;
        ///   if (!h.IsDone) Warn(); else Step(h.Status);
        /// </code>
        /// — funnels an invalid handle STRAIGHT INTO the throwing branch. A throw out of a
        /// coroutine kills the coroutine, and killing this one is what blanked EVERY building in
        /// the owner's town (device capture seq=4708, 2026-09-09, t=6.07s, scene Title).
        /// </para>
        /// <para>⛔ ASK <c>IsValid()</c> BEFORE <c>Status</c>, <c>Result</c> or
        /// <c>OperationException</c>. NEVER infer validity from <c>IsDone</c>, and never order a
        /// branch so that the IsDone test runs first.</para>
        /// </summary>
        private static bool TryReadStatus(AsyncOperationHandle handle, out AsyncOperationStatus status)
        {
            var read = AsyncOperationStatus.None;
            bool valid = false;

            // Even IsValid() sits behind a Guard. It only touches fields today, but this helper's
            // entire job is "nothing on the warm path throws again", and a diagnostic that can
            // itself fail is worse than no diagnostic (same reasoning as TryReadLastTransport).
            Guard.Try(System, "read Addressables handle validity + status", () =>
            {
                valid = handle.IsValid();
                if (valid) read = handle.Status;
            });

            status = read;
            return valid;
        }

        /// <summary>
        /// One-word description of a handle for a trace line, safe on an invalid handle.
        /// Prints the real status when there is one and the literal <c>INVALID-HANDLE</c> when
        /// there is not — never a manufactured <c>None</c>, because "None" is a legitimate live
        /// status and conflating the two is how the next reader loses an evening.
        /// </summary>
        private static string StatusText(AsyncOperationHandle handle) =>
            TryReadStatus(handle, out var s) ? s.ToString() : "INVALID-HANDLE";

        /// <summary>The last URL Addressables asked the webview to fetch (PROD-022 diagnostics).</summary>
        public static string LastRequestUrl => s_lastRequestUrl;

        /// <summary>How many transport-level Addressables requests this launch has issued (PROD-022).</summary>
        public static int WebRequestCount => s_webRequests;

        /// <summary>
        /// PROD-022 Lane B — the Pi Browser <c>Addressables.WebRequestOverride</c>. It applies the SAME
        /// unchanged <see cref="PiRequestTimeoutSeconds"/> the policy always applied, and additionally
        /// TRACES the request.
        /// <para>⛔ WHY THE URL MATTERS ENOUGH TO LOG: the failing addresses' bundles were measured 200
        /// and publicly readable from the open internet on the same day the device could not load them.
        /// That leaves exactly two possibilities — the device asked for a DIFFERENT url than the one we
        /// verified, or it asked for the right one and the webview did not complete it. Nothing in the
        /// trace could separate those, because the URL was never printed. It is printed now, once per
        /// distinct url, alongside the timeout that will bound it.</para>
        /// </summary>
        private static void InstrumentedPiWebRequest(UnityEngine.Networking.UnityWebRequest request)
        {
            if (request == null) return;

            // The policy itself — byte-identical to what shipped. Set FIRST so a throw in the
            // instrumentation below can never cost the request its bound.
            request.timeout = PiRequestTimeoutSeconds;

            Guard.Try(System, "trace Pi Addressables web request", () =>
            {
                s_webRequests++;
                s_lastRequestUrl = request.url;
                s_lastRequest = request;   // read back on the failure path for responseCode + result
                FlowTrace.Once(System, "piurl-" + request.url,
                    $"Pi transport request #{s_webRequests}: {request.method} {request.url} " +
                    $"(timeout={PiRequestTimeoutSeconds}s). This is the URL the WEBVIEW was asked for — " +
                    "compare it against the object verified readable on the CDN before blaming either side.");
            });
        }

        /// <summary>
        /// PROD-022 Lane B — read the TRANSPORT outcome (HTTP status + <c>UnityWebRequest.Result</c>) off
        /// the most recent request. Returns false when nothing is readable.
        /// <para>⛔ NEVER THROWS AND NEVER ASSUMES THE OBJECT IS ALIVE. Addressables disposes its
        /// UnityWebRequest once the operation completes, and touching a disposed one throws — on the
        /// failure path, where a diagnostic that can itself fail is worse than no diagnostic. Guarded,
        /// and a failed read is reported as "unavailable" rather than as a zero, because a REAL zero is
        /// the single most diagnostically loaded value here and must never be manufactured.</para>
        /// </summary>
        private static bool TryReadLastTransport(out long responseCode, out string result, out string url)
        {
            long code = -1;
            string res = null;
            string u = null;

            bool ok = Guard.Try(System, "read last UnityWebRequest outcome", () =>
            {
                var req = s_lastRequest;
                if (req == null) return;
                u = req.url;
                code = req.responseCode;
                res = req.result.ToString();
            });

            responseCode = code;
            result = res;
            url = u;
            return ok && res != null;
        }

        // =====================================================================
        //  PROD-022 Lane B — turning an Addressables failure into a NAMED CAUSE
        // =====================================================================

        /// <summary>
        /// Flatten <see cref="AsyncOperationHandle.OperationException"/> into one line a reader can act
        /// on: every exception in the inner chain, plus a classification of what the transport actually
        /// did (timeout / HTTP status / connection refused / key not in catalog).
        /// <para>⛔ WHY THIS IS NOT "just log ex.Message": Addressables wraps the real story. The outer
        /// exception is a generic ChainOperation/GroupOperation failure; the RemoteProviderException that
        /// carries the URL, the <c>UnityWebRequest.result</c> and the HTTP status is one or two
        /// InnerExceptions down. Logging only the outer one is how "Failed" became the entire record of a
        /// P0 on a device we cannot attach a debugger to.</para>
        /// <para>Never throws: this runs on a failure path, and a diagnostic that can itself fail is
        /// worse than no diagnostic.</para>
        /// </summary>
        private static string DescribeFailure(AsyncOperationHandle handle)
        {
            string flat = null;
            Guard.Try(System, "describe Addressables failure", () =>
            {
                var sb = new StringBuilder();
                Exception ex = handle.OperationException;
                if (ex == null)
                {
                    sb.Append("no OperationException was attached to the handle (status=")
                      .Append(handle.Status)
                      .Append("). The operation completed without a result and without an exception — " +
                              "usually a catalog entry whose provider produced nothing.");
                }
                else
                {
                    int depth = 0;
                    while (ex != null && depth < 6)
                    {
                        if (depth > 0) sb.Append("  <- ");
                        sb.Append(ex.GetType().Name).Append(": ").Append(Flatten(ex.Message));
                        ex = ex.InnerException;
                        depth++;
                    }
                }
                string all = sb.ToString();

                // PROD-022 Lane B (coordinator addendum): the TRANSPORT numbers, printed verbatim and
                // ALWAYS — even when the text classifier already succeeded. A real HTTP status and a
                // zero must never be indistinguishable in the trace, because on WebGL the zero is the
                // whole tell. "unavailable" is printed as itself and is NEVER collapsed to 0.
                bool haveTransport = TryReadLastTransport(out long code, out string result, out string url);
                sb.Append(" | responseCode=")
                  .Append(haveTransport ? code.ToString() : "unavailable")
                  .Append(" result=")
                  .Append(haveTransport ? result : "unavailable")
                  .Append(" transportUrl=")
                  .Append(url ?? "(none)");

                sb.Append(" | classified=").Append(Classify(all, code, haveTransport));
                flat = sb.ToString();
            });
            return string.IsNullOrEmpty(flat) ? "cause could not be read off the handle" : flat;
        }

        /// <summary>Collapse newlines/tabs so one failure is one trace line (the web sink is line-oriented).</summary>
        private static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(no message)";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return s.Length > 600 ? s.Substring(0, 600) + "…(truncated)" : s;
        }

        /// <summary>
        /// Name the transport verdict in ONE word so a session can be swept with a single grep, and so
        /// the three candidate roots for PROD-022 are separable without reading prose:
        /// <c>TIMEOUT</c> (the 20s Pi request override fired — the fetch never completed),
        /// <c>HTTP-4xx/5xx</c> (the CDN answered and refused — a §16 push/parity question),
        /// <c>CONNECTION</c> (the webview would not or could not open the socket — a Pi Browser
        /// question, not a CDN one), <c>KEY-MISSING</c> (the catalog has no such address — a build
        /// question), <c>CORS-OR-NETWORK</c> (responseCode 0 — see below), <c>UNCLASSIFIED</c> (say so
        /// rather than guess).
        /// <para>⛔ THE NUMERIC CODE IS NOT OPTIONAL, and it is why this takes more than a message string.
        /// On WebGL a CORS / preflight rejection arrives as <c>responseCode == 0</c> carrying NO
        /// distinctive text, so a text-only classifier reads it as CONNECTION or UNCLASSIFIED — "the
        /// socket dropped it" when the truth is "the browser blocked it". Opposite fixes. The zero branch
        /// therefore sits AHEAD of CONNECTION, and every other branch keeps its original wording and
        /// order: text evidence that is actually specific (a timeout, a 404) still wins, because a
        /// genuine timeout also reports responseCode 0 and must not be re-labelled.</para>
        /// </summary>
        /// <param name="message">the flattened exception chain.</param>
        /// <param name="responseCode">the transport's HTTP status, when one could be read.</param>
        /// <param name="codeKnown">false when the request was already disposed — in that case the zero
        /// branch is SKIPPED rather than guessed at, because an unread code is not a zero.</param>
        private static string Classify(string message, long responseCode, bool codeKnown)
        {
            if (string.IsNullOrEmpty(message)) return "UNCLASSIFIED";
            string m = message.ToLowerInvariant();

            if (m.Contains("timeout") || m.Contains("timed out"))
                return "TIMEOUT (the request never completed — on Pi Browser this is the 20s " +
                       "Addressables WebRequestOverride firing, NOT a CDN refusal)";
            if (m.Contains("invalidkey") || m.Contains("no location found") || m.Contains("unknown key"))
                return "KEY-MISSING (Addressables has no location for this address — a content-build " +
                       "question, not a network one)";
            if (m.Contains("404") || m.Contains("not found"))
                return "HTTP-404 (the CDN answered and has no such object — CLAUDE.md §16 push/parity)";
            if (m.Contains("403") || m.Contains("forbidden"))
                return "HTTP-403 (the object exists but is not publicly readable)";
            if (m.Contains("500") || m.Contains("502") || m.Contains("503") || m.Contains("504"))
                return "HTTP-5xx (the CDN/edge failed the request)";
            // ── responseCode 0, AHEAD of CONNECTION (coordinator addendum, owner WebGL knowledge) ──
            if (codeKnown && responseCode == 0)
                return "CORS-OR-NETWORK (responseCode=0: the browser never delivered a response to the " +
                       "page. On WebGL this is the signature of a CORS/preflight rejection OR a request " +
                       "the webview refused/aborted outright — it is NOT a CDN status. Note: the R2 " +
                       "objects were measured on 2026-09-02 as publicly readable WITH " +
                       "`Access-Control-Allow-Origin: *` and a 204 preflight answering `GET, HEAD`, so " +
                       "CORS POLICY IS THE LEAST LIKELY READING OF THIS LINE. And iOS memory jettison " +
                       "is RULED OUT: the owner's device Analytics on 2026-09-02 carried JetsamEvent " +
                       "reports for 08-28 through 09-01 and NONE for 09-02, across a window in which " +
                       "the app died 10+ times - so nothing was killed for memory. Read this as the " +
                       "webview refusing or abandoning the request for some OTHER reason, and pair it " +
                       "with the PiLifecycle navigation= crumb: if the page RELOADED rather than " +
                       "crashed, an in-flight fetch is cancelled and reports exactly this.)";

            if (m.Contains("cannot connect") || m.Contains("connection") || m.Contains("dns") ||
                m.Contains("unable to complete ssl") || m.Contains("curl"))
                return "CONNECTION (the socket never carried the request — the host/webview refused " +
                       "or dropped it; the CDN was never reached)";
            if (m.Contains("protocolerror"))
                return "PROTOCOL-ERROR (an HTTP status the transport treats as failure)";
            if (m.Contains("datareceived") || m.Contains("checksum") || m.Contains("crc") ||
                m.Contains("decompress"))
                return "PAYLOAD (bytes arrived but did not survive verification/decompression — the " +
                       "candidate signature of a memory-capped webview aborting a bundle mid-inflate)";
            return "UNCLASSIFIED";
        }

        private static float Now() => Time.realtimeSinceStartup;

        private static void EnsureHost()
        {
            if (s_host != null) return;
            if (!Application.isPlaying) return;   // no player loop to host a coroutine on

            Guard.Try(System, "create warm host", () =>
            {
                var go = new GameObject("StructureContentWarmer");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_host = go.AddComponent<Host>();
            });
        }

        /// <summary>
        /// DontDestroyOnLoad coroutine host. Also drains <see cref="Defer"/> work each frame —
        /// this is the "get off the engine callback" seam, and it must run from Update (the
        /// player loop) for that to mean anything.
        /// </summary>
        private sealed class Host : MonoBehaviour
        {
            private readonly List<Action> _drain = new List<Action>();

            private void Update()
            {
                // WO-1483: town frame path. 4-arg (accumulating) Measure — see
                // FlowTrace.cs:293-308. Never the 3-arg form on a per-frame site.
                using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                    "Perf", "StructureContentWarmer.Host.Update", 4f, 1f);

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
