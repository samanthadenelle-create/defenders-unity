// =============================================================================
// OfflineHarvestService — accrue resources while the player is away (WO-115).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The OFFLINE rung of the core loop (docs/NORTH_STAR.md): "mines + pets keep
// gathering up to a cap → come back richer." On cold load and on app-resume it
// computes how long the player was gone, accrues each active harvest source's
// yield over that window (clamped to a cap), banks it into GameState, and raises
// a one-tap welcome-back summary.
//
// SUPERSEDES the WO-117 WorkerManager PlayerPrefs catch-up STUB:
//   • WorkerManager kept its own "dotr-harvest-last-active" PlayerPrefs ticks and
//     replayed ForceAutoExtract() per node on Start/resume. That is now disabled
//     (WorkerManager.UseOfflineCatchUp = false) so there is ONE offline path.
//   • This service reads the SAME seam WorkerManager exposes
//     (WorkerManager.ActiveAssignments() + MineNode.RatePerSecond) PLUS the
//     WO-159 Settlement faucet (Settlement.HarvestRatePerSecond draining a finite
//     reserve), and banks via the wallet directly — the established award path
//     (Core can't ref Village; we write GameState resource fields).
//   • The accrual CLOCK now lives in the persisted save (GameState.LastHarvestClaimMs,
//     mirroring LastInboxSyncAt) instead of a side-band PlayerPrefs key, so it
//     round-trips with the rest of the save and reconciles with backend sync.
//
// WO-1147 -- THIS SERVICE NO LONGER OWNS THE CLOCK. It is now one CONSUMER of
// OfflineClaimCoordinator (see that file's header): the coordinator reads
// GameState.LastHarvestClaimMs once, computes ONE elapsed window, fans it out to
// every consumer (node harvest here, the Echo silo, Echo repair), and advances +
// persists the clock exactly once. Before that, this service wrote the clock from
// its own Start+1-frame coroutine while EchoService read it in the SAME frame
// (coin-flip) and EchoRepairService read it one frame LATER (always zero -- offline
// repair never accrued once). Do NOT re-add a write to LastHarvestClaimMs here.
// The 10h away-cap below stays OURS: the coordinator publishes the raw window and
// each consumer clamps with its own documented cap.
//
// DEDUPE vs BACKEND SAVE-SYNC (no double-grant): accrual only ever runs FORWARD
// from LastHarvestClaimMs, and that timestamp is advanced + persisted ATOMICALLY
// with the grant (advance-even-when-zero) by the coordinator. A second resume can't
// re-accrue the same window because the clock already moved past it. GameStateService's backend
// sync ships the FULL snapshot (which will include LastHarvestClaimMs once the save
// owner wires the field through the schema), so server "now" and the banked haul
// stay consistent — the server sees the post-grant wallet + post-grant clock, never
// a stale window to replay. See the save-schema note at the bottom.
//
// WO-1119 -- THE 2x HARVEST BOOST, AND WHY THIS FILE APPLIES IT THE WAY IT DOES.
// The boost multiplies the RATE we integrate and NOTHING ELSE. It is expressed as
// EXTRA INTEGRATION SECONDS (HarvestBoostService.BoostedSeconds) and then clamped
// to OUR OWN pre-existing 10h away-cap, which is untouched. That clamp is the
// covenant: a player who is away long enough to reach the cap banks exactly what
// they always banked -- they just reach it sooner. The boost sells TIME, never
// AMOUNT. Folding the multiplier into the cap instead (Version A) would hand an
// offline player 2x the RESOURCES, which is power, and is forbidden.
// CRYSTALS ARE EXCLUDED ENTIRELY: they are the real-money on-ramp, so a boosted
// crystal node would be a currency printer. The exclusion is asked of
// HarvestBoostService.IsBoostable rather than tested inline, so no accrual path
// can half-apply it.
//
// WO-1128 -- THE CLOCK IS NO LONGER ASSUMED HONEST, AND THIS FILE STOPPED PRETENDING
// TO POLICE IT. We do exactly two things here: (a) read "now" through TimeSource,
// which PREFERS the monotonic ServerClock anchor when this process has synced, and
// (b) RECORD on the result which clock produced the window (OfflineHarvestResult
// .ServerAnchored / WindowStartUnixMs / NowUnixMs). The actual refusal happens on the
// server (api/game/save.js §RECONCILE), which compares the client's DECLARED window
// against its OWN elapsed time since the last accepted save.
// ⛔ DO NOT add a client-side penalty for an unanchored clock. A cold launch is ALWAYS
// unanchored (ServerClock's Stopwatch dies with the process) and offline play is the
// feature, not the exploit. Refuse server-side; never punish client-side.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Defense;   // WO-1408 — the defence-report ledger, READ for the away summary's "attacked" row
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;   // WO-857 Phase F — the town bank cap (this path writes the wallet directly)
using DeNelle.Core.State;
using DeNelle.Core.World;
using DeNelle.Village.Buildings.Progression;   // LANE G — the collector registry, read for the away summary's "waiting" row
using DeNelle.Village.Monetization;   // WO-1119 — HarvestBoostService (Version B rate boost)
using DeNelle.Village.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// Computes resources accrued by worker-claimed nodes + WO-159 settlements +
    /// WO-229 harvesting pets (each derived from the shared MineNode claim seam) while
    /// the app was backgrounded/closed, grants them capped, and raises a welcome-back
    /// summary. Runs on cold load and on resume.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OfflineHarvestService : MonoBehaviour, IOfflineClaimConsumer
    {
        public static OfflineHarvestService Instance { get; private set; }

        public const double MinAwaySummarySeconds = 30.0 * 60.0;

        /// <summary>The haul is always applied; this controls only whether returning
        /// from a meaningful absence opens the interruption-style summary.</summary>
        public static bool ShouldRevealAwaySummary(OfflineHarvestResult result)
            => result != null && result.HasSummaryContent && result.AwaySeconds >= MinAwaySummarySeconds;

        /// <summary>Trace name for this consumer's share of the shared offline window.</summary>
        public string OfflineConsumerName => "harvest-nodes";

        [Header("Cap")]
        [Tooltip("Offline hours credited in one claim. The retention dial: long enough that a " +
                 "twice-a-day check-in feels rewarded, short enough that the mines still want " +
                 "defending. WO-115 suggests 8–12h; default 10h. Owner-tunable in playtest.")]
        [Min(0f)] public float OfflineCapHours = 10f;

        [Header("Resume policy")]
        [Tooltip("Also claim on OnApplicationPause(false) — i.e. when the app returns to the " +
                 "foreground on mobile, not only on a cold load. Off would only accrue on a full " +
                 "relaunch.")]
        public bool ClaimOnResume = true;

        /// <summary>Raised after a claim that banked something — the welcome-back popup listens.</summary>
        public event System.Action<OfflineHarvestResult> Claimed;

        private float OfflineCapSeconds => Mathf.Max(0f, OfflineCapHours) * 3600f;

        // Worker-owned nodes captured during AccrueWorkerNodes, so AccruePets can
        // exclude them and credit ONLY the pet-claimed nodes (disjoint sets → no
        // double-grant). Reused (cleared) each claim; never holds across frames.
        private readonly HashSet<MineNode> _workerOwnedThisClaim = new HashSet<MineNode>();

        // The result produced by the most recent ApplyOfflineWindow, so the public
        // ClaimAccrual() verb can still return "what this consumer banked".
        private OfflineHarvestResult _lastResult;

        private void Awake()
        {
            // Destroy(this) — NOT Destroy(gameObject): may share a host
            // (CLAUDE.md memory: singleton-dedup-destroys-host).
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            OfflineClaimCoordinator.Register(this);
            EnsureSubscribed();                       // WO-1231: the away summary's reveal seam
            EnsureJobSubscription();                  // LANE G: the away summary's finished-jobs seam
            EnsureNewGameSubscription();              // WO-1414: a New Game drops any parked reveal
        }

        private void OnDestroy()
        {
            OfflineClaimCoordinator.Unregister(this);
            DisarmSceneHook();                        // the deferred welcome-back reveal (Title-screen guard)
            if (_subscribedToNewGame)
            {
                // The event's own note: INSTANCE handlers must unsubscribe or the static event
                // holds this destroyed service forever.
                GameStateService.NewGameStarted -= OnNewGameStarted;
                _subscribedToNewGame = false;
            }
            if (_subscribedToJobs)
            {
                var timers = BuildTimerService.Instance;
                if (timers != null) timers.JobCompleted -= OnAnyJobCompleted;
                _subscribedToJobs = false;
            }
            if (_subscribedToCompletion)
            {
                OfflineClaimCoordinator.ClaimCompleted -= OnClaimCompleted;
                _subscribedToCompletion = false;
            }
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // Cold-load claim. Deferred TWO frames so MineNode/Settlement Awake/Start have
            // run and registered, the save has loaded (GameStateService.Awake), AND every
            // other offline consumer (EchoService / EchoRepairService, installed by their
            // own AfterSceneLoad bootstrap) has registered — the fan-out must reach all of
            // them on the SAME claim. Two frames is the slowest consumer's old deferral
            // (EchoRepairService needed scene structures present), now shared.
            ClaimDeferred("cold-load");
        }

        private void OnApplicationPause(bool paused)
        {
            // Tell the ONE authority where the background edge was, so a RESUME claim counts
            // only the truly-away stretch. Every consumer of a resume claim has an online
            // loop that already covered the foreground stretch (the silo tick, the repair
            // tick, settlement/pet node extraction), so counting from the persisted clock —
            // which is what this service used to do — paid that stretch TWICE. We do not
            // stamp the persisted clock at pause: a hard kill while backgrounded must still
            // leave a claimable window for the next cold load.
            OfflineClaimCoordinator.NotePaused(paused);
            if (paused) OpenClaimWindow("app paused");
            if (!paused && ClaimOnResume) ClaimDeferred("resume");
        }

        // =====================================================================
        //  ONE CLAIM PER LAUNCH WINDOW -- the cold-load / resume latch
        // ---------------------------------------------------------------------
        //  Owner felt-test 2026-09-04 22:29 (Seeker, cold launch, pid 28564): the popup
        //  said "YOUR REALM WORKED FOR 0m" after ~12.6h away. The device log names it:
        //      -> Claim(resume)     Claim #1 (resume): ONE delta = 45328s (12.59h) ...
        //                           clock advanced ONCE to 1788578999262 ... => REVEAL
        //      -> Claim(cold-load)  Claim #2 (cold-load): ONE delta = 0s ... => REVEAL   (17 ms later)
        //  Android delivers OnApplicationPause(false) DURING boot, so the "resume" trigger
        //  raced the Start() "cold-load" trigger; both went through ClaimAfterTwoFrames, both
        //  reached the coordinator, and the coordinator (correctly) advanced the clock on the
        //  first, so the second measured ~0 and its reveal REPLACED the real one.
        //
        //  THE RULE: a launch window (process start, or the most recent OnApplicationPause(true))
        //  is claimed AT MOST ONCE, whichever trigger gets there first. The latch lives HERE,
        //  not in the coordinator: the coordinator's job is ONE delta per claim (WO-1147) and
        //  its arithmetic is untouched; deciding whether a second TRIGGER is the same window
        //  is this service's business, because this service is the one that owns both
        //  triggers. A genuine resume after a real pause re-opens the window (OpenClaimWindow
        //  in OnApplicationPause(true)) and still claims.
        //
        //  Two states, deliberately separate:
        //    _claimPending  -- a deferred claim is in its two-frame wait; ANY second trigger
        //                      is skipped, pause edge or not (the pending claim will cover it).
        //    _windowClaimed -- a claim completed since the window opened; a second trigger is
        //                      skipped until OnApplicationPause(true) opens a new window.
        //  ClaimAccrual() (the public oracle/legacy verb) deliberately bypasses the latch: it
        //  is an explicit, named claim, never a lifecycle trigger.
        // =====================================================================

        private bool _claimPending;
        private bool _windowClaimed;
        private string _pendingReason;
        private float _windowClaimedAtRealtime;

        /// <summary>Sequence of the claim that covered the current launch window (0 = none yet).</summary>
        public int WindowClaimSequence { get; private set; }

        /// <summary>Trigger reason of the claim that covered the current launch window.</summary>
        public string WindowClaimReason { get; private set; }

        /// <summary>True when the most recent lifecycle trigger was SKIPPED by the latch (diagnostic / oracle readout).</summary>
        public bool LastDeferredClaimSkipped { get; private set; }

        /// <summary>
        /// Opens a NEW launch window: the next deferred trigger will claim again. Called from
        /// OnApplicationPause(true); public so a headless oracle can model a real pause edge.
        /// Does not cancel a claim already in flight -- that claim will cover the new window.
        /// </summary>
        public void OpenClaimWindow(string why)
        {
            if (!_windowClaimed && WindowClaimSequence == 0)
            {
                FlowTrace.Step("Offline", $"claim window opened ({why}) -- nothing claimed yet this process.");
                return;
            }
            FlowTrace.Step("Offline",
                $"claim window re-opened ({why}) -- claim #{WindowClaimSequence} ({WindowClaimReason}) covered the previous " +
                $"window; the next trigger will claim again.");
            _windowClaimed = false;
            WindowClaimSequence = 0;
            WindowClaimReason = null;
        }

        /// <summary>
        /// The latch. Returns true (and marks the claim pending) when this trigger may run a
        /// claim; false when a claim is already pending or already covered this window, in
        /// which case the skip is traced with the covering claim's number and age.
        /// </summary>
        private bool TryLatchClaim(string reason)
        {
            if (_claimPending)
            {
                LastDeferredClaimSkipped = true;
                FlowTrace.Step("Offline",
                    $"claim({reason}) SKIPPED - claim ({_pendingReason}) is already pending for this window " +
                    "(the second trigger of a cold launch; see the 2026-09-04 22:29 '0m' capture).");
                return false;
            }
            if (_windowClaimed)
            {
                float ageMs = (Time.realtimeSinceStartup - _windowClaimedAtRealtime) * 1000f;
                LastDeferredClaimSkipped = true;
                FlowTrace.Step("Offline",
                    $"claim({reason}) SKIPPED - claim #{WindowClaimSequence} ({WindowClaimReason}) already covered this window " +
                    $"{ageMs:0} ms ago");
                return false;
            }
            LastDeferredClaimSkipped = false;
            _claimPending = true;
            _pendingReason = reason;
            return true;
        }

        /// <summary>
        /// Runs the latched claim through the ONE authority and closes the window on it.
        /// The coordinator fans the window out to every consumer and advances the clock
        /// once; our own share lands in ApplyOfflineWindow, the reveal in OnClaimCompleted.
        /// </summary>
        private OfflineClaimWindow RunLatchedClaim(string reason)
        {
            OfflineClaimWindow window = default;
            try
            {
                window = OfflineClaimCoordinator.Claim(reason);
            }
            finally
            {
                _claimPending = false;
                _pendingReason = null;
                _windowClaimed = true;
                _windowClaimedAtRealtime = Time.realtimeSinceStartup;
                WindowClaimSequence = window.Sequence;
                WindowClaimReason = reason;
            }
            return window;
        }

        private void ClaimDeferred(string reason)
        {
            if (!isActiveAndEnabled) return;
            if (!TryLatchClaim(reason)) return;
            StartCoroutine(ClaimAfterTwoFrames(reason));
        }

        /// <summary>
        /// TEST SEAM: the lifecycle trigger with the two-frame wait collapsed, so a headless
        /// oracle (editmode never pumps coroutines) can drive "cold-load" then "resume" in the
        /// same window and read what the latch did. Returns the coordinator's window, or
        /// <c>default</c> (Sequence 0) when the latch skipped this trigger. Never called by
        /// gameplay -- the lifecycle path is ClaimDeferred.
        /// </summary>
        public OfflineClaimWindow ClaimDeferredNow(string reason)
        {
            // Idempotent registration, as ClaimAccrual: editmode AddComponent never ran Awake.
            OfflineClaimCoordinator.Register(this);
            EnsureSubscribed();
            if (!TryLatchClaim(reason)) return default;
            return RunLatchedClaim(reason);
        }

        private System.Collections.IEnumerator ClaimAfterTwoFrames(string reason)
        {
            // LANE G -- attach the queue listener BEFORE the frames elapse. BuildTimerService's
            // OWN offline sweep runs on its Start + ONE frame (BuildTimerService.SweepNextFrame),
            // i.e. strictly inside this two-frame wait, so subscribing here is what puts the
            // listener in place before the jobs that finished overnight complete. Re-tried on
            // every frame of the wait because BuildTimerService bootstraps itself
            // (RuntimeInitializeOnLoadMethod) and may not exist on the first attempt.
            EnsureJobSubscription();
            yield return null;
            EnsureJobSubscription();
            yield return null;
            EnsureJobSubscription();
            // ONE claim for the whole game: the coordinator fans the window out to every
            // consumer and advances the clock once. Our own share lands in
            // ApplyOfflineWindow; the reveal is raised from OnClaimCompleted.
            RunLatchedClaim(reason);
        }

        // =====================================================================
        //  Accrual — the accrue-on-resume mechanic (WO-115 §1)
        // =====================================================================

        /// <summary>
        /// Runs a FULL offline claim through the one authority
        /// (<see cref="OfflineClaimCoordinator"/>) and returns THIS service's share of it.
        /// The coordinator reads the clock once, fans the same window out to every
        /// consumer, and advances + persists the clock exactly once (even on a zero haul,
        /// so a player who claims their first node later banks no retroactive haul).
        /// Kept as a public verb because oracles + legacy callers drive the claim by name.
        /// </summary>
        public OfflineHarvestResult ClaimAccrual()
        {
            FlowTrace.Step("Offline", "ClaimAccrual");
            // Idempotent: editmode/headless AddComponent never runs Awake, so register here too.
            OfflineClaimCoordinator.Register(this);
            EnsureSubscribed();
            EnsureJobSubscription();
            _lastResult = OfflineHarvestResult.None;
            OfflineClaimCoordinator.Claim("OfflineHarvestService.ClaimAccrual");
            return _lastResult ?? OfflineHarvestResult.None;
        }

        /// <summary>
        /// THIS consumer's share of the shared window: integrate every active harvest
        /// source over the window CLAMPED TO OUR OWN 10h away-cap, bank the haul, and
        /// raise the welcome-back reveal. Never touches the clock — the coordinator owns it.
        /// </summary>
        public void ApplyOfflineWindow(OfflineClaimWindow window)
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null)
            {
                FlowTrace.Warn("Offline", "no GameStateService — node accrual skipped (None)");
                _lastResult = OfflineHarvestResult.None;
                return;
            }

            double elapsedSec = window.ElapsedSeconds;
            double cappedSec = window.CappedSeconds(OfflineCapHours);
            bool wasCapped = window.ExceedsCap(OfflineCapHours);
            if (wasCapped) FlowTrace.Warn("Offline", $"away {elapsedSec:0}s exceeds cap {OfflineCapSeconds:0}s — capped");

            var result = new OfflineHarvestResult
            {
                AwaySeconds = elapsedSec,
                WasCapped = wasCapped,

                // WO-1128 — RECORD WHICH CLOCK PRODUCED THIS WINDOW, and the window's own
                // endpoints, so the next backend round trip can reconcile the DECLARED
                // window against the server's OWN elapsed time (api/game/save.js
                // §RECONCILE). We record; we never reduce. An unanchored clock is the
                // NORMAL state on a cold launch (ServerClock's Stopwatch dies with the
                // process) and offline play must keep paying honestly — see the
                // refuse-don't-punish note in OfflineHarvestResult.
                ServerAnchored = TimeSource.IsServerAnchored,
                WindowStartUnixMs = window.WindowStartUnixMs,
                NowUnixMs = window.NowUnixMs,
            };

            // The one line a capture needs to answer "was this window forgeable?".
            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: clock = {result.ClockSource} " +
                $"(ServerClock.IsTrusted={TimeSource.IsServerAnchored}); window {window.WindowStartUnixMs:0} -> " +
                $"{window.NowUnixMs:0}. " +
                (result.IsProvisional
                    ? "PROVISIONAL — the server reconciles this window on the next save."
                    : "server-anchored — a wall-clock edit could not have moved it."));

            // WO-1119 Version B: the boost's OVERLAP with this window becomes extra integration
            // seconds, clamped to the SAME 10h away-cap we already enforced. Crystal nodes keep
            // the unboosted figure — see the header.
            double boostedSec = HarvestBoostService.BoostedSeconds(window.NowUnixMs, cappedSec, OfflineCapSeconds);

            if (cappedSec > 0.0)
            {
                // ORDER MATTERS for double-grant safety. AccrueWorkerNodes snapshots the
                // worker-owned node set FIRST; AccruePets then credits the pet-owned nodes,
                // which it derives as "claimed but NOT worker-owned" — so the two source
                // sets are disjoint by construction and a node is never counted twice.
                AccrueWorkerNodes(result, cappedSec, boostedSec);
                AccrueSettlements(result, cappedSec, boostedSec);
                AccruePets(result, cappedSec, boostedSec);
                FlowTrace.Step("Offline", $"accrued over {cappedSec:0}s" +
                    (boostedSec > cappedSec ? $" (boosted to {boostedSec:0}s for non-crystal sources)" : "") +
                    $": worker-owned={_workerOwnedThisClaim.Count} node(s), total={result.Total}");
            }

            if (result.Total > 0) Grant(result, state);
            else FlowTrace.Step("Offline", "zero haul — clock still advances (prevents retroactive first-claim)");

            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: 'harvest-nodes' share = {cappedSec:0}s of the {elapsedSec:0}s window " +
                $"(cap {OfflineCapHours:0.##}h) -> total {result.Total}.");

            _lastResult = result;
            _lastResultSeq = window.Sequence;
            // WO-1231: the reveal MOVED to OnClaimCompleted. It cannot fire from here any
            // more, because from here the OTHER consumers' shares have not necessarily been
            // applied yet -- fan-out order is registration order is bootstrap order, i.e.
            // undefined -- and the summary now has to report what passive Echo mending
            // SPENT out of the wallet over the same window. See OnClaimCompleted.
        }

        // =====================================================================
        //  WO-1231 -- the reveal, raised once the WHOLE claim is known
        // =====================================================================

        // Subscription is idempotent and set up in BOTH Awake and ClaimAccrual: an
        // editmode/headless oracle drives ClaimAccrual on a component AddComponent never
        // ran Awake for, which is the same reason Register() is called in both places.
        private bool _subscribedToCompletion;

        // Claim sequence that produced _lastResult. THE FRESH-CLOCK PATH IS WHY THIS
        // EXISTS: OfflineClaimCoordinator.Claim seeds a fresh clock and fans out to
        // NOBODY, so ApplyOfflineWindow never runs and _lastResult still holds the
        // PREVIOUS claim's haul. Without this check the completion handler would happily
        // re-reveal an already-collected summary (and re-report an already-reported
        // spend), which is a worse lie than the silence WO-1231 removed.
        private int _lastResultSeq = -1;

        private void EnsureSubscribed()
        {
            if (_subscribedToCompletion) return;
            OfflineClaimCoordinator.ClaimCompleted += OnClaimCompleted;
            _subscribedToCompletion = true;
        }

        // =====================================================================
        //  LANE G -- WHAT THE QUEUE FINISHED WHILE THE PLAYER WAS AWAY
        // ---------------------------------------------------------------------
        //  Economy map (docs/PROGRAM_RAID_ECONOMY_2026-09-04.md sec.7) beat 1 is
        //  "BUILD COMPLETE -> collect". Nothing reported it. Measured at source
        //  first, so the gap is stated exactly and not overclaimed:
        //    * an UPGRADE-kind job DOES already apply its level on the offline
        //      sweep (BuildTimerService.OnJobCompleted -> CompletedUpgradeApplier
        //      .Apply), and where the structure is spawned the player sees it;
        //    * a NEW-CONSTRUCTION completion, and an upgrade whose structure has
        //      not spawned, were reported NOWHERE;
        //    * and there was no AGGREGATE anywhere -- no screen that said "three
        //      things finished" on the one screen a returning player reads.
        //
        //  THE SEAM IS THE EXISTING EVENT. BuildTimerService.JobCompleted already
        //  fires for live expiry, ad/instant skip AND the offline-fair sweep (it is
        //  raised from the ONE completion seam, OnJobCompleted), so this listener
        //  needs no new completion path and cannot diverge from one. We RECORD only:
        //  nothing here completes, grants or re-applies a job.
        // =====================================================================

        /// <summary>Jobs recorded since the last reveal. Static so a service instance
        /// destroyed by a scene load does not drop the night's completions on the floor;
        /// bounded so an un-revealed backlog can never grow without limit.</summary>
        private static readonly List<OfflineHarvestResult.OfflineJobLine> s_completedJobs =
            new List<OfflineHarvestResult.OfflineJobLine>();

        /// <summary>Hard bound on the recorder. A returning player is shown at most a
        /// handful of rows anyway; this only stops an un-revealed backlog growing forever.</summary>
        private const int MaxRecordedJobs = 32;

        /// <summary>Slack on the window-membership test, in ms. The queue completes a job at
        /// the sweep, which is a couple of frames off the claim's own "now"; without slack a
        /// job that finished on the boundary would be silently dropped from its own report.</summary>
        private const double JobWindowSlackMs = 5000.0;

        private bool _subscribedToJobs;

        /// <summary>Idempotently attach to the ONE job-completion seam. Null-safe: with no
        /// BuildTimerService yet (it self-bootstraps) this is a no-op and is retried.</summary>
        private void EnsureJobSubscription()
        {
            if (_subscribedToJobs) return;
            var timers = BuildTimerService.Instance;
            if (timers == null) return;
            timers.JobCompleted -= OnAnyJobCompleted;   // belt-and-braces: never double-attach
            timers.JobCompleted += OnAnyJobCompleted;
            _subscribedToJobs = true;
            FlowTrace.Step("Offline",
                "away summary attached to BuildTimerService.JobCompleted -- finished jobs will be " +
                "reported on the next reveal.");
        }

        /// <summary>
        /// Record one finished job for the away summary. Guarded (CLAUDE.md sec.12): a bad
        /// label lookup logs and is skipped, and can never break the queue's completion cascade
        /// -- this is a listener on the completion event, so a throw here would propagate.
        /// </summary>
        private void OnAnyJobCompleted(BuildJobData job)
        {
            Guard.Try("Offline", $"record completed job '{job.StructureId}' for the away summary", () =>
            {
                // The SHARED card seam, so the summary says the same words the queue card said.
                var entry = BuildTimerService.EntryFor(job);
                s_completedJobs.Add(new OfflineHarvestResult.OfflineJobLine
                {
                    Verb = string.IsNullOrEmpty(entry.Verb) ? "COMPLETE" : entry.Verb,
                    Label = string.IsNullOrEmpty(entry.Label) ? "Job" : entry.Label,
                    FinishedUnixMs = job.FinishMs,
                });
                while (s_completedJobs.Count > MaxRecordedJobs) s_completedJobs.RemoveAt(0);
                FlowTrace.Step("Offline",
                    $"away summary recorded a finished job: {entry.Verb} '{entry.Label}' " +
                    $"(finishMs={job.FinishMs:0}); {s_completedJobs.Count} awaiting a reveal.");
            });
        }

        /// <summary>
        /// Move the recorded jobs that belong to THIS window onto the result, and drop them
        /// from the recorder so a second reveal can never re-report them.
        /// <para>MEMBERSHIP IS BY FinishMs, not by arrival order. On a RESUME claim the window
        /// starts at the pause edge, so a job the player watched finish while the app was in
        /// the foreground is correctly excluded; on a COLD load the window starts at the
        /// persisted claim clock, so everything that landed since then is genuinely away
        /// news.</para>
        /// </summary>
        private static void AttachCompletedJobs(OfflineHarvestResult result, OfflineClaimWindow window)
        {
            result.CompletedJobs.Clear();
            if (s_completedJobs.Count == 0) return;

            double from = window.WindowStartUnixMs - JobWindowSlackMs;
            double to = window.NowUnixMs + JobWindowSlackMs;
            int skipped = 0;
            for (int i = s_completedJobs.Count - 1; i >= 0; i--)
            {
                var j = s_completedJobs[i];
                if (j == null) { s_completedJobs.RemoveAt(i); continue; }
                if (j.FinishedUnixMs < from || j.FinishedUnixMs > to) { skipped++; continue; }
                result.CompletedJobs.Insert(0, j);        // oldest-first, the order they landed
                s_completedJobs.RemoveAt(i);
            }

            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: away summary claims {result.CompletedJobCount} finished job(s) " +
                $"from window {from:0}..{to:0}; {skipped} recorded job(s) fell outside it and were left " +
                "for a later window.");
        }

        /// <summary>
        /// Read what the collectors are STILL HOLDING, for the report's "waiting" row.
        /// <para>READ-ONLY, and deliberately so: this claim must not bank a collector's pending.
        /// The collectors run their OWN away catch-up on their own per-collector stamp
        /// (ResourceCollector.CatchUpAway) and the player banks it with the COLLECT button.
        /// Banking here would be a second route to the wallet for the same units.</para>
        /// </summary>
        private static void AttachPendingCollectors(OfflineHarvestResult result)
        {
            result.PendingCollectors.Clear();
            // Owner rulings 2026-09-04 22:30: the collectors are SEPARATED, "Wood Iron Stone
            // different rows" -- one row per RESOURCE (not per building), in the HUD rail's fixed
            // order. The resource word is the game's canon LabelFor -- the same word the rail and
            // the collector's own "+N Wood" gain popup say. Never a second vocabulary.
            //
            // WO-1392 - THE ROWS READ THE ONE PRODUCER. This method used to walk
            // ResourceCollectorRegistry.All itself and floor each PendingAmount; the collect path
            // then reported a DIFFERENT number ("of 2393" against this screen's "+672") because it
            // never read this loop. ResourceCollectorService.PendingByResource() is now the single
            // source both screens consume; a second loop here is the defect coming back.
            List<ResourceCollectorService.PendingLine> lines = null;
            Guard.Try("Offline", "read pending collectors for the away summary",
                () => lines = ResourceCollectorService.PendingByResource());
            result.PendingCollectors.AddRange(LinesFrom(lines));
            int total = 0, count = 0;
            foreach (var line in result.PendingCollectors) { total += line.Pending; count += line.Collectors; }
            result.PendingCollectorTotal = total;
            result.PendingCollectorCount = count;
        }

        /// <summary>The popup's rows from the shared producer's lines, PURE (pinned by
        /// CollectorIncomeRegression [popup-and-result-agree]). Word = the canon LabelFor.</summary>
        public static List<OfflineHarvestResult.OfflineCollectorLine> LinesFrom(
            IReadOnlyList<ResourceCollectorService.PendingLine> lines)
        {
            var rows = new List<OfflineHarvestResult.OfflineCollectorLine>();
            if (lines == null) return rows;
            foreach (var line in lines)
            {
                if (line == null || line.Pending <= 0) continue;
                rows.Add(new OfflineHarvestResult.OfflineCollectorLine
                {
                    Resource = ResourceBuildingProgression.LabelFor(line.Resource),
                    Pending = line.Pending,
                    Collectors = line.Collectors,
                });
            }
            return rows;
        }

        /// <summary>
        /// WO-1434 -- what a Dump would route out of the Echo silo per resource. READ ONLY: this
        /// never dumps (COLLECT carries the tap to CollectorStatusGate.RequestCollectAll, which is
        /// what calls EchoService.DumpSilos). Guarded, so a missing EchoService reports an empty
        /// silo rather than blanking the whole away summary.
        /// </summary>
        private static void AttachSiloPending(OfflineHarvestResult result)
        {
            result.SiloPending.Clear();
            result.SiloTotal = 0;
            result.SiloAtCap = false;

            int[] split = null;
            Guard.Try("Offline", "read the Echo silo split for the away summary",
                () => split = EchoService.PredictDumpSplit());
            if (split == null || split.Length < 5) return;

            // Rail order, and ONLY the resources the return screen can speak about. Gold
            // (EchoService.ShareGold) is coins, which the rail does not carry.
            AddSiloLine(result, HarvestResource.Wood,     split[EchoService.ShareWood]);
            AddSiloLine(result, HarvestResource.Iron,     split[EchoService.ShareIron]);
            AddSiloLine(result, HarvestResource.Food,     split[EchoService.ShareFood]);
            AddSiloLine(result, HarvestResource.Crystals, split[EchoService.ShareCrystals]);

            var echo = EchoService.Instance;
            result.SiloAtCap = echo != null && echo.SiloAtCap;
            FlowTrace.Step("Offline",
                $"away summary: Echo silo holds {result.SiloTotal} across {result.SiloPending.Count} resource row(s)" +
                (result.SiloAtCap ? " -- AT CAP, the Echoes have stopped gathering until the player collects." : "."));
        }

        /// <summary>
        /// WO-1408 -- was the town attacked inside THIS window? READ ONLY, and deliberately so:
        /// the DEFENCE REPORT owns the record (append, unread badge, score). This reads
        /// <see cref="DefenseReportLedger.All"/> and counts the records whose timestamp falls
        /// in the window, using the SAME slack the job rows use so the two rows agree about
        /// where the window's edges are.
        /// <para>! It never marks a report read. The away row is a DOOR onto the panel, and a
        /// door that consumed the unread badge before the player walked through it would delete
        /// the very signal the panel exists to raise.</para>
        /// <para>Guarded: a headless boot with no GameStateService reports zero attacks rather
        /// than blanking the whole away summary.</para>
        /// </summary>
        private static void AttachAttacks(OfflineHarvestResult result, OfflineClaimWindow window)
        {
            result.AttackCount = 0;
            result.AttackBreachName = string.Empty;
            result.AttackOutcomeWord = string.Empty;

            double from = window.WindowStartUnixMs - JobWindowSlackMs;
            double to = window.NowUnixMs + JobWindowSlackMs;

            Guard.Try("Offline", "read the defence ledger for the away summary", () =>
            {
                var all = DefenseReportLedger.All();
                if (all == null) return;
                DefenseOutcomeRecord newest = null;
                for (int i = 0; i < all.Count; i++)
                {
                    var rec = all[i];
                    if (rec == null) continue;
                    if (rec.Timestamp < from || rec.Timestamp > to) continue;
                    result.AttackCount++;
                    if (newest == null || rec.Timestamp >= newest.Timestamp) newest = rec;
                }
                if (newest == null) return;
                result.AttackOutcomeWord = newest.Outcome.ToString().ToUpperInvariant();
                if (newest.Breaches == null) return;
                for (int i = 0; i < newest.Breaches.Count; i++)
                {
                    var b = newest.Breaches[i];
                    if (b == null || string.IsNullOrEmpty(b.DisplayName)) continue;
                    result.AttackBreachName = b.DisplayName;   // the FIRST crossing -- "they came from the north first"
                    break;
                }
            });

            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: away summary counted {result.AttackCount} defence report(s) in " +
                $"window {from:0}..{to:0}" +
                (result.AttackCount > 0
                    ? $" -- newest outcome={result.AttackOutcomeWord} breach='{result.AttackBreachName}'."
                    : " -- the town was not attacked (away time becomes PRESSURE today, not a resolved battle)."));
        }

        private static void AddSiloLine(OfflineHarvestResult result, HarvestResource res, int amount)
        {
            if (amount <= 0) return;
            result.SiloPending.Add(new OfflineHarvestResult.OfflineSiloLine
            {
                Resource = ResourceBuildingProgression.LabelFor(res),
                Pending = amount,
            });
            result.SiloTotal += amount;
        }

        // =====================================================================
        //  WO-1434 -- ONE ROW PER RESOURCE, AMOUNT AND DESTINY TOGETHER
        // ---------------------------------------------------------------------
        //  WHAT THIS REPLACES, and why the replacement is not a rewording.
        //
        //  WO-1392 added PredictCollectWaits + CollectWaitLine, which produced a SECOND
        //  list of lines under the "<RES> WAITING +N" rows: "Storage nearly full - 10609
        //  wood will wait". On the owner's screen (2026-09-06, build 358161) that read:
        //      WOOD  WAITING   +10609
        //      IRON  WAITING    +6365
        //      STONE WAITING   +25808
        //      Storage nearly full - 10609 wood will wait
        //      Storage nearly full - 6365 iron will wait
        //      Storage nearly full - 25808 stone will wait
        //  -- six lines for three facts, the same integer printed twice ("way too much
        //  here"), and every row still carrying a REWARD's "+" while its entire amount was
        //  going to wait. Neither half was wrong; they were two halves of one row.
        //
        //  A ReturnRow is that one row. It carries the amount AND what becomes of it, and
        //  it knows about BOTH producers -- the collectors and the Echo silo -- because a
        //  player does not have a collector total and a silo total, she has wood.
        //
        //  ⚠ NOTHING HERE IS LOST, AND THE COPY MUST NEVER SAY IT IS. Proven on the
        //  device, 2026-09-06:
        //    * collectors: ResourceCollector.Collect banks only what fits and leaves the
        //      rest pending (WO-1392); the same pendings came back larger on the next tap
        //      (wood 10656 -> 10776, stone 25870 -> 26026).
        //    * silo: EchoService.DumpSilos settles against the APPLIED basket
        //      (`s.SiloResources -= bankedFromSilo`), and the log says so --
        //      "silo dump: 28800 wood stayed in the silo - Wood storage full". Pool 57600
        //      was still 57600 across three separate dumps (12:51:25, 12:56:03, 12:56:06).
        //  The word "LOST" in the [Flow:Bank] BANK FULL warn is the BANK's statement that
        //  it refused the units; whether they are retained is the CALLER's business, and
        //  both live callers retain. See HarvestOverflowModal.BuildBody.
        // =====================================================================

        /// <summary>
        /// One resource on the return screen: everything waiting for it, from every producer,
        /// and what the bank will do with that on COLLECT.
        /// </summary>
        public struct ReturnRow
        {
            public HarvestResource Resource;
            /// <summary>Canon player word, title case ("Wood" / "Stone").</summary>
            public string Word;
            /// <summary>Units held in the collectors.</summary>
            public int FromCollectors;
            /// <summary>Units the Echo silo would route here on a Dump.</summary>
            public int FromSilo;
            /// <summary>Everything waiting for this resource.</summary>
            public int Pending;
            /// <summary>Town-bank room right now (int.MaxValue when the resource is uncapped).</summary>
            public int Headroom;
            /// <summary>What COLLECT will actually bank.</summary>
            public int Banks;
            /// <summary>What stays where it is — never burned, on either producer.</summary>
            public int Waits;
            /// <summary>True when the whole amount waits: the row must NOT read as a gain.</summary>
            public bool NothingBanks => Banks <= 0 && Pending > 0;
        }

        /// <summary>Live overload: headroom from the town bank (ResourceCollectorService.HeadroomFor).</summary>
        public static List<ReturnRow> BuildReturnRows(OfflineHarvestResult result)
            => BuildReturnRows(result, ResourceCollectorService.HeadroomFor);

        /// <summary>
        /// PURE given a headroom reader (pinned by OfflineHarvestRegression
        /// [one-row-per-resource] / [no-gain-without-headroom] / [every-producer-rendered]).
        /// Rail order, resources with nothing pending dropped. Lines from both producers are
        /// matched back to their resource through the same LabelFor word they were built from.
        /// </summary>
        public static List<ReturnRow> BuildReturnRows(OfflineHarvestResult result,
            System.Func<HarvestResource, int> headroom)
        {
            var rows = new List<ReturnRow>();
            if (result == null || headroom == null) return rows;
            foreach (var res in ResourceCollectorService.RailOrder)
            {
                string word = ResourceBuildingProgression.LabelFor(res);
                int collectors = SumFor(result.PendingCollectors, word);
                int silo = SumSiloFor(result.SiloPending, word);
                int pending = collectors + silo;
                if (pending <= 0) continue;

                int room = headroom(res);
                if (room < 0) room = 0;
                int banks = room >= pending ? pending : room;
                rows.Add(new ReturnRow
                {
                    Resource = res,
                    Word = word,
                    FromCollectors = collectors,
                    FromSilo = silo,
                    Pending = pending,
                    Headroom = room,
                    Banks = banks,
                    Waits = pending - banks,
                });
            }
            return rows;
        }

        private static int SumFor(List<OfflineHarvestResult.OfflineCollectorLine> lines, string word)
        {
            int sum = 0;
            if (lines == null) return 0;
            foreach (var line in lines)
                if (line != null && string.Equals(line.Resource, word, System.StringComparison.OrdinalIgnoreCase))
                    sum += line.Pending;
            return sum;
        }

        private static int SumSiloFor(List<OfflineHarvestResult.OfflineSiloLine> lines, string word)
        {
            int sum = 0;
            if (lines == null) return 0;
            foreach (var line in lines)
                if (line != null && string.Equals(line.Resource, word, System.StringComparison.OrdinalIgnoreCase))
                    sum += line.Pending;
            return sum;
        }

        /// <summary>
        /// The row's LEFT column: the resource and the amount.
        ///
        /// <para>⛔ WO-1434 D1 — <b>THE PLUS-NUMBER IS ALWAYS THE COLLECTABLE AMOUNT, NEVER THE
        /// PENDING ONE.</b> That substitution IS the defect: the screen headlined 42,782 pending,
        /// the owner read it as a reward, tapped COLLECT and banked zero. The rule holds at every
        /// scale, not just at zero headroom — a row with room for 258 of 672 says <c>+258</c>,
        /// because +672 is a promise the tap cannot keep. The whole amount only ever appears
        /// UNSIGNED, in the WAITING form, where no plus can read it as a gain.</para>
        ///
        /// <para>And NO INTEGER APPEARS TWICE ACROSS THE ROW (D2). The label owns what banks; the
        /// destiny column owns what stays. ASCII only.</para>
        /// <code>
        ///   room for all   -> "WOOD +672"
        ///   partial room   -> "WOOD +258"          (destiny: "414 MORE WAITS")
        ///   no room at all -> "WOOD 10609 WAITING" (destiny: "STORAGE FULL - STAYS PUT")
        /// </code>
        /// </summary>
        public static string ReturnRowLabel(ReturnRow r)
            => r.NothingBanks
                ? $"{r.Word.ToUpperInvariant()} {r.Pending} WAITING"
                : $"{r.Word.ToUpperInvariant()} +{r.Banks}";

        /// <summary>
        /// The row's RIGHT column: what becomes of the REST, in WORDS (the owner is red/green
        /// colourblind — no meaning may ride on hue). ASCII only; the device renders a non-ASCII
        /// dash or bullet as tofu. It never restates the label's integer — see
        /// <see cref="ReturnRowLabel"/>.
        /// <code>
        ///   room for all      -> "COLLECT NOW"
        ///   partial room      -> "414 MORE WAITS"
        ///   no room at all    -> "STORAGE FULL - STAYS PUT"
        /// </code>
        /// "WAITS" and "STAYS PUT", never "lost": nothing on either producer burns (see the block
        /// header above for the captured proof).
        /// </summary>
        public static string ReturnRowDestiny(ReturnRow r)
        {
            if (r.Waits <= 0) return "COLLECT NOW";
            if (r.Banks <= 0) return "STORAGE FULL - STAYS PUT";
            return $"{r.Waits} MORE WAITS";
        }

        /// <summary>
        /// The one sentence under the table, when a row could not bank in full. Names the fix and
        /// says plainly that nothing is going away, because the previous screen's own numbers made
        /// the player expect a payout that did not arrive.
        /// </summary>
        public static string ReturnFooterLine(IReadOnlyList<ReturnRow> rows)
        {
            if (rows == null) return null;
            int waiting = 0;
            for (int i = 0; i < rows.Count; i++) waiting += rows[i].Waits;
            if (waiting <= 0) return null;
            // !! "ALREADY WAITING", NOT "PRODUCED" (owner frame 2026-09-07 01:13). The header above
            // this table says "YOUR REALM WORKED FOR 30m" and the rows then showed 108,072 units
            // waiting - which reads as a claim that half an hour produced 108k. It did not: the
            // waiting figures are the STANDING pools in the collectors and the Echo silo, banked up
            // over days against a full store, while the "+" figures are what THIS window earned.
            // The sentence now names where the units already are, so the two kinds of number
            // cannot be read as one.
            // Grouped with the invariant culture: a device locale can otherwise group with U+00A0,
            // which renders as tofu on the mobile atlas (the same reason HarvestResultVM.N exists).
            // ...AND THE PLACE IS COMPOSED FROM THE ROWS, never asserted. A collectors-only window
            // must not claim an Echo silo is holding something: an untrue word on a reassurance
            // sentence is the same defect as an untrue number, and this screen is the one the
            // player checks the HUD rail against.
            int fromCollectors = 0, fromSilo = 0;
            for (int i = 0; i < rows.Count; i++) { fromCollectors += rows[i].FromCollectors; fromSilo += rows[i].FromSilo; }
            string where = fromCollectors > 0 && fromSilo > 0 ? "your collectors and Echo silo"
                         : (fromSilo > 0 ? "your Echo silo" : "your collectors");
            string n = waiting.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            return $"{n} is already waiting in {where} - nothing is lost. Spend, or upgrade storage.";
        }

        /// <summary>
        /// WO-1434 / `FOUNDATIONAL_RULINGS.md` section 7 — a faucet that stopped must be said in
        /// words. A FULL silo is a different fact from a waiting row: a half-full silo also waits,
        /// and it is still gathering. Null when the silo is not at its ceiling.
        /// </summary>
        public static string SiloStalledLine(OfflineHarvestResult result)
            => result != null && result.SiloAtCap && result.SiloTotal > 0
                ? "Echo silo is full - your Echoes gather nothing more until you collect."
                : null;

        /// <summary>
        /// Every consumer has applied and the clock has advanced: attach passive mending's
        /// share of the SAME window and reveal the summary.
        /// <para>
        /// THE GATE IS <see cref="OfflineHarvestResult.HasSummaryContent"/> -- haul OR mend OR
        /// a finished queue job OR resources waiting in a collector -- and it is read off the
        /// RESULT, never re-derived here. WO-1231 first widened it from "haul" to "haul OR
        /// mend" (a window in which the player gathered nothing but mending spent 400 Wood
        /// used to show no summary at all); LANE G (2026-09-04) moved it onto the result and
        /// added the other two axes, because this method and WelcomeBackPopup.Show each held
        /// their own copy of the two-term gate and a collector-only town fell through both.
        /// </para>
        /// </summary>
        private void OnClaimCompleted(OfflineClaimWindow window)
        {
            var result = _lastResult;
            if (result == null) return;
            if (_lastResultSeq != window.Sequence)
            {
                // This claim did not fan out to us (fresh clock, or no GameState) -- the
                // result we are holding belongs to an older, already-revealed window.
                FlowTrace.Step("Offline",
                    $"claim #{window.Sequence}: no share applied to 'harvest-nodes' this claim " +
                    $"(held result is from #{_lastResultSeq}) -- away summary NOT re-revealed.");
                return;
            }

            // Only THIS claim's mend report may be attached. A report from an older window
            // (Echo repair unregistered, or bounced on a null GameState) must never be
            // re-reported as if the wallet had just been charged again.
            var mend = EchoRepairService.LastOfflineMendReport;
            result.Mend = (mend != null && mend.ClaimSequence == window.Sequence) ? mend : EchoMendReport.None;

            // LANE G -- the other two axes of the returning session (economy map sec.7 beat 1).
            AttachCompletedJobs(result, window);
            AttachPendingCollectors(result);
            AttachSiloPending(result);   // WO-1434 -- the fifth axis
            AttachAttacks(result, window);   // WO-1408 -- the row that carries a DOOR, not a sixth gate axis

            // THE GATE IS NOW FOUR AXES, and it is read off the RESULT so this method and
            // WelcomeBackPopup.Show cannot disagree about what counts as news. It used to be
            // "haul OR mend", which showed NOTHING to a player whose nodes were idle, whose
            // Echoes were quiet, whose three overnight builds had finished and whose farm was
            // sitting full -- the exact returning session sec.7 is written around.
            bool hasContent = result.HasSummaryContent;
            bool show = ShouldRevealAwaySummary(result);
            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: away summary gate -> haul={result.Total}, " +
                $"mendNews={result.HasMendNews}, jobs={result.CompletedJobCount}, " +
                $"collectorsPending={result.PendingCollectorTotal} across {result.PendingCollectorCount} " +
                $"collector(s), siloPending={result.SiloTotal} (atCap={result.SiloAtCap}), " +
                $"away={result.AwaySeconds:0}s minimum={MinAwaySummarySeconds:0}s " +
                $"=> {(show ? "REVEAL" : hasContent ? "stored normally; short absence, no modal" : "no reveal")}.");
            if (!show) return;

            Claimed?.Invoke(result);
            TryShowPopup(result);
        }

        // ── Source 1: worker-collected mine nodes (WO-117 seam) ───────────────
        // Each on-station worker drives one node at MineNode.RatePerSecond. We
        // integrate that rate over the capped window. (RatePerSecond is 0 for a
        // finite-reserve node, so those are handled only by the settlement path.)
        // WO-1119: the ONE place the boost is applied to a resource. Crystals never take the
        // boosted figure (HarvestBoostService.IsBoostable), so a crystal node integrates the plain
        // window even while a boost is running.
        private static double SecondsFor(MineResource resource, double cappedSec, double boostedSec) =>
            HarvestBoostService.IsBoostable(resource) ? boostedSec : cappedSec;

        private void AccrueWorkerNodes(OfflineHarvestResult result, double cappedSec, double boostedSec)
        {
            _workerOwnedThisClaim.Clear();

            var wm = WorkerManager.Instance;
            if (wm == null) return;

            IReadOnlyList<MineNode> nodes = wm.ActiveAssignments();
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                // Record worker ownership BEFORE the depletion/rate gates so AccruePets
                // never re-credits a node a worker owns, even one that's spent this frame.
                _workerOwnedThisClaim.Add(node);
                if (node.IsDepleted) continue;
                float rate = node.RatePerSecond;
                if (rate <= 0f) continue;
                int accrued = (int)(rate * SecondsFor(node.Resource, cappedSec, boostedSec));
                result.Add(node.Resource, accrued);
            }
        }

        // ── Source 2: WO-159 settlements draining finite reserves ─────────────
        // A settlement auto-harvests its claimed finite-reserve node at
        // HarvestRatePerSecond. Offline we credit rate × window, but never more than
        // the reserve actually held (the ward/claim gate: only an active settlement on
        // a non-empty reserve accrues — a razed/outpost settlement or empty node yields 0).
        private void AccrueSettlements(OfflineHarvestResult result, double cappedSec, double boostedSec)
        {
            var all = Settlement.All;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || s.Phase != SettlementPhase.Active) continue;
                var node = s.ClaimedNode;
                if (node == null || !node.UseFiniteReserve || node.IsReserveEmpty) continue;

                int owed = (int)(s.HarvestRatePerSecond * SecondsFor(node.Resource, cappedSec, boostedSec));
                if (owed <= 0) continue;
                // Clamp to what's left in the ground so offline can't over-mine a reserve.
                int banked = Mathf.Min(owed, node.ReserveRemaining);
                result.Add(node.Resource, banked);
            }
        }

        // ── Source 3: harvesting pets (WO-229 PetHarvester) ───────────────────
        // A deployed harvesting pet works a MineNode through the SAME seam a Worker
        // does: it claims the node (MineNode.SetWorkerClaim → IsClaimedByWorker) and
        // drives TryAutoExtract() on the node's cooldown. So a pet-worked node banks at
        // exactly the node's RatePerSecond, just like a worker-worked one.
        //
        // We can't enumerate PetHarvester from here (it lives in DeNelle.Pets, which
        // Village must not reference — CLAUDE.md §5/§9; pets reach Village only via the
        // reflection MineNodeBridge). Instead we read the shared MineNode claim seam:
        // a node that is CLAIMED but is NOT one of WorkerManager's active worker
        // assignments is, by elimination, being worked by a pet (the only other thing
        // that calls SetWorkerClaim). That set is disjoint from AccrueWorkerNodes's set
        // by construction (we exclude _workerOwnedThisClaim), so no node is double-counted.
        //
        // Note: claims are runtime-only (not persisted), so on a COLD load a pet hasn't
        // re-acquired a node yet within this deferred frame → pets contribute 0 that
        // launch (the clock still advances; nothing is lost, just not retro-credited).
        // On RESUME (the common mobile case) claims are live, so the away-gap is credited.
        private void AccruePets(OfflineHarvestResult result, double cappedSec, double boostedSec)
        {
#if UNITY_2023_1_OR_NEWER
            var nodes = Object.FindObjectsByType<MineNode>();
#else
            var nodes = Object.FindObjectsByType<MineNode>();
#endif
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || node.IsDepleted) continue;
                if (!node.IsClaimedByWorker) continue;             // unclaimed → no harvester
                if (_workerOwnedThisClaim.Contains(node)) continue; // a Worker owns it → already credited
                float rate = node.RatePerSecond;                   // finite-reserve nodes report 0 (settlement path)
                if (rate <= 0f) continue;
                int accrued = (int)(rate * SecondsFor(node.Resource, cappedSec, boostedSec));
                result.Add(node.Resource, accrued);
            }
        }

        // ── Banking — the established off-chain award path ────────────────────
        // Core can't reference Village, but Village references Core, so writing the
        // GameState resource fields directly is the valid, reflection-free path (the
        // same one MineNode.BankYield uses). Local off-chain currency only — no token mint.
        //
        // WO-857 / WO-901 Phase F — this path writes the wallet DIRECTLY (it cannot call
        // EconomyService.Grant from here for the reason above), so it must apply the town bank
        // cap itself through the SAME one reader. It is the only real income source in the game
        // that bypasses the EconomyService choke, and it is the one most likely to overflow:
        // an away pool banks hours of production in a single frame. Clamp-and-warn (owner ruling
        // 2026-08-04) — TownBankCapacity.ClampGrant raises the [Flow:Bank] warn and the on-screen
        // toast for us. Crystals are UNCAPPED by design and pass through untouched.
        // ⛔ WO-1128 §3.5 — THE CONTAINER CAP IS DOING SECURITY WORK. SAY SO OUT LOUD.
        // The remaining client-side exposure to a FORWARDS device clock (set the phone
        // ahead, relaunch, collect) is bounded HERE, and by nothing else: our 10h
        // away-cap bounds the WINDOW, and TownBankCapacity.ClampGrant below bounds the
        // AMOUNT to what the player's containers can physically hold. The ceiling on a
        // fabricated window is therefore "one full container", not unbounded resources.
        // That is a DELIBERATE property as of WO-1128, no longer an accident:
        //   * a ticket that raises storage capacity widens this hole in proportion
        //     (WO-1108b already took a maxed container from 2000 -> 34000);
        //   * a ticket that makes any resource UNCAPPED removes the bound entirely.
        //     Crystals are already uncapped by design and pass through untouched below —
        //     which is exactly why crystals are excluded from the boost (see the header)
        //     and are the resource the server-side reconciler logs most loudly.
        // If you change either, the server-side reconciliation in api/game/save.js
        // (§RECONCILE) becomes the ONLY line of defence. Do not silently rely on it.
        // =====================================================================
        //  WO-1445 -- WHAT THE CAP REFUSES HERE IS GENUINELY NOT ADDED, AND THE
        //  SCREEN NOW SAYS SO IN WORDS INSTEAD OF PROMISING A FIGURE.
        // =====================================================================
        //  THE TICKET ASKED FOR RETENTION ("retain the remainder on the same pending store the
        //  producers use"). MEASURED AT SOURCE, THERE IS NO SUCH STORE ON THIS PATH, and saying so
        //  is the honest answer rather than inventing one (CLAUDE.md section 11B):
        //    * ResourceCollector.PendingAmount is PER COLLECTOR, per building - the retention the
        //      collectors and the Echo silo both use. This method's income comes from MINE NODES,
        //      SETTLEMENTS and PETS (AccrueWorkerNodes / AccrueSettlements / AccruePets); none of
        //      those has a collector, so there is no pending pool to put a remainder in.
        //    * Pushing it into an unrelated building's collector would re-mint the WO-1392 defect
        //      the other way (a second producer writing a pool one screen reads and another does
        //      not), and it would hit that collector's OWN cap.
        //    * A new persisted store is a save-schema bump - structural, and out of this lane.
        //  So the remainder is NOT ADDED, and the owner's WO-1461 law is honoured the way that law
        //  actually reads: the surplus is never SILENTLY lost. Every refused unit now (a) raises a
        //  permanent FlowTrace.Warn naming the resource and the amount, (b) reaches the HARVEST
        //  RESULT screen as a real row through the SAME BankOverflowStatus seam the collectors use,
        //  and (c) is described on that screen as "not added" with a footer that says WHY - never
        //  as "waiting, safe", which would be the WO-1434 lie inverted.
        //  !! AND THE ROWS ONLY REACH THE SCREEN INSIDE A WARN SCOPE. Measured at source
        //  2026-09-07: BankOverflowToastPresenter.OnOverflow opens with
        //      if (_scopeDepth <= 0) return;                  // "Ruling 3 (WO-1207): NO SCOPE, NO SCOLD"
        //  (BankOverflowToastPresenter.cs:162-171). Grant had NO scope, so for its whole life its
        //  clamps raised the Overflowed event into a presenter that dropped them on the floor -
        //  the away haul's overflow has never once reached the HARVEST RESULT screen. The scope is
        //  what stamps Source and what calls HarvestOverflowModal.Present (`:250-256`); it also
        //  merges repeat clamps of one resource into one row, which is why no separate batch is
        //  opened here - a second aggregation mechanism over the same three statuses is exactly
        //  the duplicated-producer shape WO-1392 was about.
        //  ! The presenter keeps a per-resource cooldown; a second overflowing claim inside it is
        //  deliberately silent on screen (the FlowTrace.Warn below is unthrottled regardless).
        //  Raised to the lead as a contradiction in WORK_ORDER_1445's RESULT.
        // =====================================================================
        private void Grant(OfflineHarvestResult result, GameState state)
        {
            int iron, wood, food, lostIron, lostWood, lostFood;
            // FULLY QUALIFIED on purpose: this file already has `using DeNelle.Village.UI`, and a
            // second `using DeNelle.Core.UI` would put two UI namespaces in scope here for the sake
            // of ONE type. One name, no ambiguity surface.
            using (DeNelle.Core.UI.BankOverflowToastPresenter.BeginWarnScope("OfflineHarvest"))
            {
                iron = TownBankCapacity.ClampGrant(BankResource.Iron, state.Iron, result.Iron, "OfflineHarvest", out lostIron);
                wood = TownBankCapacity.ClampGrant(BankResource.Wood, state.Wood, result.Wood, "OfflineHarvest", out lostWood);
                food = TownBankCapacity.ClampGrant(BankResource.Food, state.Resources.Food, result.Food, "OfflineHarvest", out lostFood);
            }

            // PERMANENT, per resource, and NEVER throttled: a discard on this path used to be
            // computed nowhere and retained nowhere, so it left no line at all. One grep for
            // "away haul NOT ADDED" now names every unit this path could not bank.
            WarnDiscarded("Iron", state.Iron, result.Iron, iron, lostIron);
            WarnDiscarded("Wood", state.Wood, result.Wood, wood, lostWood);
            WarnDiscarded("Stone", state.Resources.Food, result.Food, food, lostFood);

            if (iron > 0) state.Iron += iron;
            if (wood > 0) state.Wood += wood;
            if (food > 0)
            {
                // Food lives on the wallet struct (Resources.Food) — DEF-121.
                var bal = state.Resources;
                bal.Food += food;
                state.Resources = bal;
            }
            if (result.AetherCrystals > 0)
            {
                // Crystals unified onto Resources.Crystals (the single wallet).
                var cbal = state.Resources;
                cbal.Crystals += result.AetherCrystals;
                state.Resources = cbal;
            }

            // Nudge the resource-changed listeners (HUD wallet) without coupling to HUD.
            GameStateService.Instance?.ResourcesChanged?.Invoke();

            // Report what was actually BANKED (post-bank-cap), and name the accrual separately when
            // the two differ — a log that shows the pre-clamp number is how a silent loss hides.
            bool bankTruncated = iron != result.Iron || wood != result.Wood || food != result.Food;
            Debug.Log($"[OfflineHarvest] Banked +{iron} iron, +{wood} wood, " +
                      $"+{food} food, +{result.AetherCrystals} crystals over " +
                      $"{Mathf.RoundToInt((float)result.AwaySeconds)}s away" +
                      (result.WasCapped ? " (away-cap)." : ".") +
                      $" clock={result.ClockSource}{(result.IsProvisional ? " (provisional until sync)" : "")}." +
                      (bankTruncated
                          ? $" BANK FULL - accrued {result.Iron} iron / {result.Wood} wood / {result.Food} food; " +
                            "the surplus was NOT ADDED (there is no store that holds away node yield - see the WO-1445 block above)."
                          : ""));

            // (!) ONE LINE, THE WHOLE CLAIM (CLAUDE.md section 12). Away seconds, and per resource:
            // the cap, what was asked for, what BANKED and what was not added. The device capture
            // of 2026-09-07 had accrue-pending lines and a collector status band but NOTHING that
            // reconciled the away window to the banked deltas, so "looked like a fail" could not be
            // checked against a number. One grep for "away claim banked" answers it now.
            FlowTrace.Step("OfflineHarvest",
                $"away claim banked: away={result.AwaySeconds:0}s capped={result.WasCapped} | " +
                $"WOOD asked={result.Wood} banked={wood} notAdded={lostWood} cap={TownBankCapacity.MaxOf(BankResource.Wood)} | " +
                $"IRON asked={result.Iron} banked={iron} notAdded={lostIron} cap={TownBankCapacity.MaxOf(BankResource.Iron)} | " +
                $"STONE asked={result.Food} banked={food} notAdded={lostFood} cap={TownBankCapacity.MaxOf(BankResource.Food)} | " +
                $"CRYSTALS asked={result.AetherCrystals} banked={result.AetherCrystals} notAdded=0 cap=uncapped. " +
                "The HARVEST RESULT screen composes its figures from these same clamp events " +
                "(HarvestResultVM.Build), so screen == banked by construction.");
        }

        /// <summary>
        /// WO-1445 - the one permanent warn for a unit this path could not bank. Silent above zero
        /// discard is the whole defect; a discard that leaves no line is indistinguishable from
        /// income that never accrued.
        /// </summary>
        private static void WarnDiscarded(string word, int current, int asked, int banked, int lost)
        {
            if (lost <= 0) return;
            FlowTrace.Warn("OfflineHarvest",
                $"away haul NOT ADDED [{word}]: accrued {asked}, banked {banked}, {lost} could not be " +
                $"stored (wallet {current}). Away node/settlement/pet yield has NO pending pool to wait " +
                "in - unlike the collectors and the Echo silo, which both retain - so these units are " +
                "not added at all. The HARVEST RESULT row says \"not added\", never \"waiting, safe\".");
        }

        // =====================================================================
        //  Welcome-back popup (code-built, hosted on a borrowed PanelSettings)
        // =====================================================================

        /// <summary>A reveal that arrived while no hub scene was active (Title, raid, dungeon)
        /// waits here for the next hub load. Owner felt-test 2026-09-04 22:30: the popup fired
        /// OVER the Title screen (CONTINUE / START NEW / PLAY INTRO behind it) because the cold-load
        /// claim runs before any hub scene exists.</summary>
        private OfflineHarvestResult _deferredReveal;
        private bool _sceneHookArmed;

        /// <summary>WO-1414 -- true while the parked reveal is waiting on a TUTORIAL step rather
        /// than on a hub scene. The two deferrals share <see cref="_deferredReveal"/> but not
        /// their release: the scene one is released by sceneLoaded, this one by the poll in
        /// <see cref="Update"/>, because "the step stopped awaiting its dialogue" raises no event.</summary>
        private bool _tutorialDeferred;

        /// <summary>TEST SEAM / oracle readout: a reveal is parked waiting for a hub scene or for
        /// the tutorial. A New Game must leave this FALSE (WO-1414 A).</summary>
        public bool HasDeferredReveal => _deferredReveal != null;

        // =====================================================================
        //  WO-1414 A -- NEW GAME MUST NOT INHERIT A PARKED AWAY SUMMARY
        // ---------------------------------------------------------------------
        //  THE BUG THIS FIXES (owner device 2026-09-05 09:57, build 2026.09.05.356468):
        //  a BRAND-NEW game opened on "YOUR REALM WORKED FOR 8h 22m" with +11520 Wood /
        //  +6912 Iron / +15000 Stone waiting. 8h22m was the wall time since the owner's
        //  PREVIOUS session, and a second New Game reported 1h56m -- the previous one's.
        //
        //  THE CHAIN, all of it in this file and provable from it:
        //    1. OfflineHarvestBootstrap installs this service AfterSceneLoad and DDOLs it,
        //       so it is alive on the TITLE screen carrying the PREVIOUS save.
        //    2. Start() claims ("cold-load"). The window is measured off that save's stamp
        //       and is genuinely 8h22m. Correct so far.
        //    3. TryShowPopup below cannot show it -- Title is not a hub -- so it PARKS the
        //       result in _deferredReveal and arms sceneLoaded (the 2026-09-04 22:30 fix,
        //       commit d1fd1f6e0, which is what introduced this field).
        //    4. The player taps START NEW. ResetToNewGame zeroes the persisted stamp and
        //       notifies its live subscribers -- and _deferredReveal was not one of them.
        //    5. The new game's hub scene loads. OnSceneLoadedForReveal releases the OLD
        //       save's window onto a town that is seconds old. The F8 capture puts the
        //       popup at t=23.6s into Main_Castle_Overworld, which is that release.
        //
        //  So this is instance SIX of the shape GameStateService.cs:1543-1547 names by
        //  number (WO-860 equip, WO-1019 hot-swap bar, WO-1220 talents, WO-1371 collector
        //  fill): state that ResetToNewGame has never heard of. The PERSISTED half was
        //  already right -- the reset zeroes the stamp (GameStateService.cs:1232) and the
        //  coordinator's fresh-clock arm then yields a ZERO window with no fan-out, which
        //  is the "first claim window is 0 and no popup" the ticket asks for. This is the
        //  LIVE half, and it is the whole fix: nothing about the window arithmetic changes.
        // =====================================================================

        private bool _subscribedToNewGame;

        private void EnsureNewGameSubscription()
        {
            if (_subscribedToNewGame) return;
            GameStateService.NewGameStarted -= OnNewGameStarted;
            GameStateService.NewGameStarted += OnNewGameStarted;
            _subscribedToNewGame = true;
        }

        private void OnNewGameStarted()
        {
            var dropped = _deferredReveal;
            _deferredReveal = null;
            _tutorialDeferred = false;
            DisarmSceneHook();

            // The held share is the PREVIOUS save's too: OnClaimCompleted re-reveals whatever
            // _lastResult holds when the sequence matches, and a fresh-clock claim fans out to
            // nobody, so leaving it here is how an already-collected summary comes back.
            _lastResult = null;
            _lastResultSeq = -1;

            // And a report already ON SCREEN when START NEW is pressed belongs to the old town.
            WelcomeBackPopup.DismissIfOpen("new game");

            FlowTrace.Step("Offline",
                dropped != null
                    ? $"New Game: DROPPED a parked welcome-back reveal (away={dropped.AwaySeconds:0}s " +
                      $"haul={dropped.Total} collectorsPending={dropped.PendingCollectorTotal}) - it was measured " +
                      "on the PREVIOUS save and must never be released onto the new town (WO-1414 A)."
                    : "New Game: no parked welcome-back reveal to drop; held share cleared (WO-1414 A).");
        }

        // =====================================================================
        //  WO-1414 C -- the parked reveal's tutorial release (no event to hang on)
        // =====================================================================

        private void Update()
        {
            if (_deferredReveal == null || !_tutorialDeferred) return;
            if (TutorialFlow.IsAwaitingDialogue) return;
            var pending = _deferredReveal;
            _deferredReveal = null;
            _tutorialDeferred = false;
            FlowTrace.Step("Offline",
                $"welcome-back tutorial deferral RELEASED: no step is awaiting a dialogue any more " +
                $"(AwaySeconds={pending.AwaySeconds:0}).");
            TryShowPopup(pending);   // re-runs the combat + hub checks on the way in
        }

        private void TryShowPopup(OfflineHarvestResult result)
        {
            // Suppress during an active wave or while the Defend-the-Tower mode is
            // running — the grant already happened (the popup is only a reveal), so a
            // suppressed popup never loses resources; it's just not shown mid-fight.
            if (IsCombatActive())
            {
                Debug.Log("[OfflineHarvest] Combat active — welcome-back reveal suppressed (haul already banked).");
                return;
            }

            // NEVER ON THE TITLE SCREEN (owner felt-test 2026-09-04 22:30). Same deferral shape as
            // combat, but this one RE-CHECKS: the reveal parks until SceneManager.sceneLoaded hands
            // us a hub scene (DeNelle.Core.HubScenes.IsHub -- the one canonical hub list).
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!DeNelle.Core.HubScenes.IsHub(scene))
            {
                _deferredReveal = result;
                _tutorialDeferred = false;
                if (!_sceneHookArmed)
                {
                    UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedForReveal;
                    _sceneHookArmed = true;
                }
                FlowTrace.Step("Offline",
                    $"welcome-back DEFERRED: active scene '{scene}' is not a hub -- reveal waits for the next hub load. " +
                    $"AwaySeconds={result.AwaySeconds:0} haul={result.Total} collectorsPending={result.PendingCollectorTotal}.");
                return;
            }

            // WO-1414 C -- NEVER OVER A TUTORIAL BEAT THAT IS WAITING FOR A DIALOGUE.
            // Device 2026-09-05: the panel sat over the SKIP control
            // ([Flow:Tutorial] SKIP_TOP_HIT_BLOCKED top=ObsidianPanel path=WelcomeBackUI/ObsidianPanel,
            // capture seq4681) and the founding beat then died on its watchdog
            // (STEP-STUCK :: founding_greet - no 'dialogue.ended:tut_founding_greet' after 120s,
            // seq4682) -- i.e. the first-run tutorial was silently SKIPPED.
            // DEFERRING, NOT RE-LAYERING: raising the SKIP control above the modal would leave the
            // dialogue itself covered, which is the beat the step is actually waiting on. The haul
            // is already banked (this is only a reveal), so waiting costs the player nothing.
            if (TutorialFlow.IsAwaitingDialogue)
            {
                _deferredReveal = result;
                _tutorialDeferred = true;
                FlowTrace.Step("Offline",
                    $"welcome-back DEFERRED: a tutorial step is awaiting '{TutorialFlow.AwaitedDialogueSignal}' -- " +
                    $"the reveal waits for the beat to end (AwaySeconds={result.AwaySeconds:0} haul={result.Total}).");
                return;
            }

            _deferredReveal = null;
            _tutorialDeferred = false;
            FlowTrace.Step("Offline",
                $"welcome-back SHOW: scene='{scene}' is a hub. AwaySeconds={result.AwaySeconds:0} " +
                $"clock={result.ClockSource} haul={result.Total} collectorsPending={result.PendingCollectorTotal}.");
            WelcomeBackPopup.Show(result);
        }

        private void OnSceneLoadedForReveal(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            var pending = _deferredReveal;
            if (pending == null) { DisarmSceneHook(); return; }
            if (!DeNelle.Core.HubScenes.IsHub(scene.name))
            {
                FlowTrace.Step("Offline",
                    $"welcome-back still DEFERRED: loaded scene '{scene.name}' is not a hub (AwaySeconds={pending.AwaySeconds:0}).");
                return;
            }
            _deferredReveal = null;
            _tutorialDeferred = false;
            DisarmSceneHook();
            FlowTrace.Step("Offline",
                $"welcome-back deferred reveal RELEASED: hub scene '{scene.name}' loaded ({mode}). AwaySeconds={pending.AwaySeconds:0}.");
            TryShowPopup(pending);   // re-runs the combat check on the hub it just landed in
        }

        private void DisarmSceneHook()
        {
            if (!_sceneHookArmed) return;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedForReveal;
            _sceneHookArmed = false;
        }

        private static bool IsCombatActive()
        {
            // Active wave?
            var wm = FindAnyObjectByType<WaveManager>();
            if (wm != null && wm.Phase == WavePhase.Active) return true;

            // (Defend-the-Tower mode removed — only an active wave counts as combat now.)
            return false;
        }
    }
}
