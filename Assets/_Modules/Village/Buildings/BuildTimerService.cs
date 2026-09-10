// =============================================================================
// BuildTimerService — the common "Obsidian" multi-channel work queue (WO-172 +
// WO-773). CoC-style timed jobs + rewarded-ad/instant speedups, now GENERALIZED.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// ONE shared service, MULTIPLE independent CHANNELS (Builder / Train / Research).
// Every timed action in the game flows through it — build, repair, upgrade,
// tier-unlock, learn-magic, troop-training, towers, walls — but a channel NEVER
// shares slots with another channel, so a troop training and a wall upgrade run in
// PARALLEL (CoC feel: builders and the barracks don't compete). Each channel has:
//   • N concurrent ACTIVE slots (BuildTimerConfig.freeBuildSlots + purchased slots)
//   • one FIFO PENDING queue — a job past the slot count QUEUES (it does NOT reject);
//     on completion the freed slot AUTO-PULLS the next pending job (cascades offline).
//
// The queue MATH is the pure DeNelle.Core.Jobs.ObsidianQueueEngine (headlessly
// testable); this MonoBehaviour is the thin wrapper that owns the GameState-backed
// channels (GameState.ObsidianQueue), feeds the engine TimeSource.NowUnixMs(), and
// dispatches the completion EFFECT (the existing Build/Upgrade seams + the IJobEffect
// registry for the extensible kinds).
//
// PERSISTENCE: every job lives in GameState.ObsidianQueue (per-channel active +
// pending lists of BuildJobData) and the clock is TimeSource.NowUnixMs() — wall-clock
// unix-ms. So timers keep counting while the app is closed: on load this service
// sweeps every channel, completes jobs whose FinishMs passed, and cascades pending
// pulls (offline-fair). Tick() only drives UI/visuals while open.
//
// BACK-COMPAT: the WO-172 legacy GameState.BuildJobs was folded into the Builder
// channel by the v34→v35 migration and is no longer read here. The public Builder-
// facing API (StartBuild/StartUpgrade/IsBuilding/RemainingSeconds/Progress/HasFreeSlot/
// ActiveJobs/skips/CompleteJob/CancelJob/RepointJob) is unchanged for its callers
// (BuildModeController / UnderConstructionVisual) — it now operates on the Builder
// channel and QUEUES instead of rejecting when full.
//
// AD SEAM / ASMDEF notes are unchanged from WO-172 (see history).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;   // ad-skip window stamp: culture-invariant round-trip parse
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Village.Monetization;   // WO-1120 — AdGateService, the ad-placements.json interpreter
using DeNelle.Wallet;                 // WO-1253 — PackCatalog.PermanentBuilderSku ownership.
                                      // WO-1282 — PackCatalog now lives in the RAIL-NEUTRAL
                                      // DeNelle.Commerce assembly (Village no longer references
                                      // DeNelle.Wallet at all), but it KEEPS the DeNelle.Wallet
                                      // NAMESPACE: PromoCodeService resolves "DeNelle.Wallet.
                                      // PackContents" as a reflection string literal. Namespace and
                                      // assembly are orthogonal; this using still resolves.

// WO-911 refund plumbing refers to the resource ledger as `Ledger.*` (ResourceCost,
// HarvestResource, ResourceLedger). There is no namespace by that name - those types
// live in DeNelle.Village.Buildings.Progression. One alias resolves every reference
// (:632, :642-645, :1070+) rather than rewriting each call site.
using Ledger = DeNelle.Village.Buildings.Progression;

namespace DeNelle.Village
{
    /// <summary>
    /// WHY a Builder job qualifies for the <see cref="BuildTimerConfig.firstBuildSeconds"/>
    /// grace (WO-945). The CALLER decides the reason (it owns the catalog id, the ever-built
    /// ledger and the pallets carve-out); the service applies the SAME shortening for either
    /// reason but traces them DISTINCTLY, so a capture can tell tutorial-time grace from
    /// first-build grace. <see cref="None"/> = pay the real tier curve.
    /// </summary>
    public enum BuildGraceReason
    {
        /// <summary>No grace — the tier curve applies unchanged.</summary>
        None = 0,
        /// <summary>First-ever build of this structure id (owner ruling 2026-08-06).</summary>
        FirstBuild = 1,
        /// <summary>Player not yet Onboarded — EVERY qualifying build is snappy so the
        /// tutorial never stalls on a timer (WO-945, the ruling's intent made literal).</summary>
        Onboarding = 2,
    }

    /// <summary>
    /// The common Obsidian work queue (WO-773): N concurrent active slots + a FIFO
    /// pending queue PER CHANNEL (Builder/Train/Research), offline-fair via TimeSource,
    /// with rewarded-ad / premium speedups on the Builder channel. Persisted in
    /// GameState.ObsidianQueue. Self-bootstrapping singleton (mirrors OfflineHarvestService).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildTimerService : MonoBehaviour
    {
        public static BuildTimerService Instance { get; private set; }

        /// <summary>Raised when a job finishes (timer elapsed, ad/instant skip, or offline catch-up). Arg = the completed job.</summary>
        public event Action<BuildJobData> JobCompleted;

        /// <summary>Raised when a job starts running (immediate start OR auto-pulled from the queue). Arg = the started job.</summary>
        public event Action<BuildJobData> JobStarted;

        /// <summary>Raised when a job's remaining time changes via a skip (ad / instant). Arg = the updated job.</summary>
        public event Action<BuildJobData> JobSkipped;

        /// <summary>
        /// WO-1042 — raised when a job is REMOVED without completing, from the one cancel choke point
        /// (<see cref="CancelChannelJob"/>, which <see cref="CancelChannelJobWithRefund"/> delegates to).
        /// <para>
        /// WHY THIS EXISTS: the v37 paid basket refunds RESOURCES, and it is the right contract for
        /// every job that spends resources. A jewel-polish job spends an ITEM (the rough stone), and
        /// JobCost has no item lane — so without this hook, cancelling a polish would silently eat the
        /// player's stone. The alternative, not consuming the stone until completion, would let one
        /// stone back five queued jobs. A cancel signal is the small, correct seam; it is NOT a second
        /// timer system and it changes no existing behaviour (nothing else subscribes).
        /// </para>
        /// </summary>
        public event Action<BuildJobData> JobCancelled;

        /// <summary>WO-773 — raised whenever ANY channel's active/pending set changes (for the queue HUD).</summary>
        public event Action QueueChanged;

        private BuildTimerConfig _config;

        /// <summary>The tunable curve/ad/slot knobs. Loaded from Resources, code-default if absent.</summary>
        public BuildTimerConfig Config => _config ??= LoadConfig();

        // ── Bootstrap / lifecycle ─────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("BuildTimerService");
            DontDestroyOnLoad(go);
            go.AddComponent<BuildTimerService>();
        }

        private void Awake()
        {
            // Destroy(this) not Destroy(gameObject) — may share a host (CLAUDE.md memory).
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_journeyRaidProjection != null)
            {
                _journeyRaidProjection.Dispose();
                _journeyRaidProjection = null;
            }
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // Offline catch-up: any job that finished while the app was closed completes now
            // (one frame's slack so structures/registries have awoken).
            StartCoroutine(SweepNextFrame());
        }

        private System.Collections.IEnumerator SweepNextFrame()
        {
            yield return null;
            SweepAllChannels();
        }

        // While open, complete jobs the moment they expire so the UI/visual flips without
        // waiting for the next load. Cheap: a handful of jobs across channels, checked ~1/s.
        private float _nextTick;
        private bool _statusSeeded;   // first-frame flash fix (review 2026-08-01), see below
        private DeNelle.Village.Hero.RaidSelectionVM _journeyRaidProjection;
        private static readonly Func<string, bool> JourneyRaidSceneProbeFallback =
            DeNelle.Core.SceneRouter.IsSceneInBuild;
        private int _journeyRaidVictoryInput = int.MinValue;
        private int _journeyRaidCatalogInput = int.MinValue;
        private int _journeyRaidDeployableInput = int.MinValue;
        private Func<string, bool> _journeyRaidSceneProbeInput;
        private void Update()
        {
            // FIRST-FRAME FLASH FIX (review 2026-08-01): RaidEntryGate.ArmyStatus defaults
            // READY pre-publish, so the HUD Raids button could flash bright for up to 1s on
            // hub load (the 1 Hz tick below may land BEFORE GameState exists, publishing the
            // null-state READY snapshot, then wait a full second). Seed ONE publish through
            // the same path the moment GameState is first available, off the tick cadence.
            if (!_statusSeeded && State != null)
            {
                _statusSeeded = true;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "army status seed publish (first Update with GameState, pre-heartbeat).");
                PublishStatus();
            }

            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            SweepAllChannels();
            PublishStatus();   // WO-778: 1s heartbeat keeps the HUD chip countdown live
        }

        // =====================================================================
        //  Slots / channels
        // =====================================================================

        /// <summary>The GameState-backed multi-channel queue (never null while a state exists).</summary>
        private static ObsidianQueueState Queue
        {
            get
            {
                var s = State;
                if (s == null) return null;
                return s.ObsidianQueue ??= ObsidianQueueState.Empty();
            }
        }

        /// <summary>The <see cref="ChannelState"/> for <paramref name="id"/> (null only when no state exists).</summary>
        private static ChannelState GetChannel(ChannelId id)
        {
            var q = Queue;
            return q?.Channel(id);
        }

        /// <summary>
        /// Derived slot count for <paramref name="id"/>: the config free slots + this channel's
        /// purchased slots. (Owner-design milestone unlocks — +1 slot at account L10/L20 — layer
        /// on top here as a future tuning dial; the queue mechanism already honours any count.)
        /// </summary>
        public int SlotCount(ChannelId id)
        {
            int free = Mathf.Max(1, Config.freeBuildSlots);
            var ch = GetChannel(id);
            int bought = ch != null ? Mathf.Max(0, ch.BoughtSlots) : 0;
            // WO-1253: the permanent-builder SKU is CONCURRENCY on the Builder channel only.
            // It never widens Train/Research and never raises queue depth.
            bool extraBuilder = id == ChannelId.Builder && OwnsPermanentBuilder();
            bool temporaryBuilder = id == ChannelId.Builder && ch != null &&
                                    IsTemporarySlotActive(ch.TemporarySlotEndsAtUnixMs, TimeSource.NowUnixMs());
            return ConcurrencyOf(free, bought, extraBuilder) + (temporaryBuilder ? 1 : 0);
        }

        /// <summary>Pure wall-clock check used by the live service and focused regression.</summary>
        public static bool IsTemporarySlotActive(double endsAtUnixMs, double nowUnixMs)
            => endsAtUnixMs > 0d && nowUnixMs < endsAtUnixMs;

        /// <summary>One-time grant guard, kept pure so stacking/reclaim regressions are headless.</summary>
        public static bool CanClaimTemporarySlot(bool alreadyClaimed) => !alreadyClaimed;

        /// <summary>
        /// WO-1388 - the PACK path's guard, pure. The one-time taste (<paramref name="reclaimAfterExpiry"/>
        /// false) is exactly <see cref="CanClaimTemporarySlot(bool)"/>. The pack path may RE-CLAIM once the
        /// previous window has EXPIRED (every purchase is a window), but never while one is ACTIVE - that
        /// is the deferral case the redeemer handles, and it is refused here so the deferral is the only
        /// way a second window can start.
        /// </summary>
        public static bool CanClaimTemporarySlot(bool alreadyClaimed, bool activeNow, bool reclaimAfterExpiry)
        {
            if (!alreadyClaimed) return true;
            return reclaimAfterExpiry && !activeNow;
        }

        /// <summary>
        /// Prefix of the refusal <see cref="TryGrantTemporaryBuilder(double, bool, out string)"/> answers while
        /// a window is RUNNING. The pack redeemer keys its DEFER decision on <see cref="IsTemporaryBuilderActive"/>,
        /// not on this text; the constant exists so the oracle and the trace name the same sentence.
        /// </summary>
        public const string TemporaryBuilderActiveRefusal = "Temporary builder is already active.";

        /// <summary>True while a temporary Builder crew window is running RIGHT NOW (taste or pack).</summary>
        public bool IsTemporaryBuilderActive
        {
            get
            {
                var ch = GetChannel(ChannelId.Builder);
                return ch != null && IsTemporarySlotActive(ch.TemporarySlotEndsAtUnixMs, TimeSource.NowUnixMs());
            }
        }

        /// <summary>
        /// Grants the builder-only temporary worker once. An active window is refused rather than
        /// stacked, extended, or reclaimed after expiry. The server must still verify the purchase
        /// before calling this seam; this durable guard makes local settlement idempotent.
        /// </summary>
        public bool TryGrantTemporaryBuilder(double durationSeconds, out string failure)
            => TryGrantTemporaryBuilder(durationSeconds, false, out failure);

        /// <summary>
        /// WO-1388 - the same grant with the pack's re-claim rule. <paramref name="reclaimAfterExpiry"/>
        /// true lets a NEW window start once the previous one has expired (the pack is repeatable);
        /// an ACTIVE window is still refused, with <see cref="TemporaryBuilderActiveRefusal"/>, so the
        /// caller can defer rather than burn. False is byte-for-byte the one-time taste above.
        /// </summary>
        public bool TryGrantTemporaryBuilder(double durationSeconds, bool reclaimAfterExpiry, out string failure)
        {
            failure = null;
            var ch = GetChannel(ChannelId.Builder);
            if (ch == null)
            {
                failure = "Save not loaded.";
                DeNelle.Core.Diagnostics.FlowTrace.Warn("TempBuilder",
                    "grant refused: no Builder channel (save not loaded). reclaimAfterExpiry=" + reclaimAfterExpiry);
                return false;
            }

            double now = TimeSource.NowUnixMs();
            bool activeNow = IsTemporarySlotActive(ch.TemporarySlotEndsAtUnixMs, now);
            if (!CanClaimTemporarySlot(ch.TemporarySlotClaimed, activeNow, reclaimAfterExpiry))
            {
                failure = activeNow
                    ? TemporaryBuilderActiveRefusal
                    : "Temporary builder was already claimed.";
                DeNelle.Core.Diagnostics.FlowTrace.Step("TempBuilder",
                    "grant refused: " + failure + " (claimed=" + ch.TemporarySlotClaimed + " active=" + activeNow +
                    " reclaimAfterExpiry=" + reclaimAfterExpiry + ").");
                return false;
            }

            double seconds = Math.Max(0d, durationSeconds);
            if (seconds <= 0d)
            {
                failure = "Temporary builder duration is disabled.";
                DeNelle.Core.Diagnostics.FlowTrace.Warn("TempBuilder", "grant refused: duration " + durationSeconds + " <= 0.");
                return false;
            }
            ch.TemporarySlotClaimed = true;
            ch.TemporarySlotEndsAtUnixMs = now + seconds * 1000d;
            Persist();
            ObsidianQueueEngine.PullIntoFreeSlots(ch, SlotCount(ChannelId.Builder), now);
            Persist();
            RaiseQueueChanged();
            DeNelle.Core.Diagnostics.FlowTrace.Step("TempBuilder",
                "granted +1 Builder crew for " + (seconds / 3600d).ToString("0.##", CultureInfo.InvariantCulture) +
                "h (reclaim=" + reclaimAfterExpiry + ") -> SlotCount(Builder)=" + SlotCount(ChannelId.Builder) +
                " endsAtMs=" + ch.TemporarySlotEndsAtUnixMs.ToString("F0", CultureInfo.InvariantCulture) + ".");
            return true;
        }

        /// <summary>
        /// WO-1388 - PackStoreVM (via the same reflection bridge as
        /// <see cref="OnPermanentBuilderEntitlement"/>) tells the service a convenience token landed in
        /// GearInventory, so a 'temporary-builder' pack starts its crew NOW rather than on the next 1 Hz
        /// sweep. Idempotent: the redeemer spends nothing when nothing is owed.
        /// </summary>
        public void OnConvenienceTokensGranted()
        {
            DeNelle.Core.Diagnostics.FlowTrace.Step("TempBuilder", "convenience tokens granted - redeem pass.");
            ConvenienceRedeemer.TryRedeemTemporaryBuilder();
        }

        /// <summary>Data-first default grant; pack/server settlement may call this after ownership verification.</summary>
        public bool TryGrantTemporaryBuilder(out string failure)
            => TryGrantTemporaryBuilder(Config != null ? Config.temporaryBuilderSeconds : 0f, out failure);

        /// <summary>Seconds remaining on the temporary Builder worker; zero after expiry.</summary>
        public double TemporaryBuilderSecondsRemaining()
        {
            var ch = GetChannel(ChannelId.Builder);
            return ch == null ? 0d : Math.Max(0d, (ch.TemporarySlotEndsAtUnixMs - TimeSource.NowUnixMs()) / 1000d);
        }

        /// <summary>
        /// WO-1253 — concurrency math in one place so the oracle and the live service cannot drift.
        /// <paramref name="ownsPermanentBuilder"/> adds +1 crew; it must NEVER be fed into
        /// <see cref="DepthOf"/>.
        /// </summary>
        public static int ConcurrencyOf(int freeBuildSlots, int boughtSlots, bool ownsPermanentBuilder)
        {
            return Mathf.Max(1, freeBuildSlots) + Mathf.Max(0, boughtSlots) + (ownsPermanentBuilder ? 1 : 0);
        }

        /// <summary>
        /// WO-1253 — depth math in one place. Crystal <see cref="TryBuySlot"/> still widens the
        /// line via <paramref name="boughtSlots"/>; the permanent-builder SKU is not an input.
        /// 0 authored = uncapped.
        /// </summary>
        public static int DepthOf(int queueDepthPerLine, int boughtSlots)
        {
            if (queueDepthPerLine <= 0) return 0;
            return queueDepthPerLine + Mathf.Max(0, boughtSlots);
        }

        /// <summary>
        /// True when <c>GameState.OwnedItemIds</c> carries the permanent-builder store SKU.
        /// Derived from the entitlement, never a save flag, so a reinstall restore of ownership
        /// is enough.
        /// </summary>
        public bool OwnsPermanentBuilder()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return PackCatalog.OwnsPermanentBuilder(state != null ? state.OwnedItemIds : null);
        }

        /// <summary>
        /// WO-1252 — true when the permanent-builder SKU is actually on the browsable shelf
        /// AND this player does not already own it. The placement toast may name the store
        /// builder only when this is true; dangling an unavailable purchase is the defect.
        /// </summary>
        public bool OffersPermanentBuilder()
        {
            if (OwnsPermanentBuilder()) return false;
            var pack = PackCatalog.Find(PackCatalog.PermanentBuilderSku);
            return PackCatalog.IsOnBrowsableShelf(pack);
        }

        /// <summary>
        /// WO-1253 grant hook. PackStoreVM calls this after recording the SKU. Idempotent:
        /// <paramref name="alreadyHad"/> true means a re-settle, so concurrency does not bump
        /// again (SlotCount is derived from ownership).
        /// </summary>
        public void OnPermanentBuilderEntitlement(bool alreadyHad)
        {
            if (alreadyHad)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("BuilderSku", "player bought builder already-had");
                return;
            }
            DeNelle.Core.Diagnostics.FlowTrace.Step("BuilderSku", "player bought builder applied");
            var ch = GetChannel(ChannelId.Builder);
            if (ch == null) return;
            ObsidianQueueEngine.PullIntoFreeSlots(ch, SlotCount(ChannelId.Builder), TimeSource.NowUnixMs());
            Persist();
            RaiseQueueChanged();
        }

        /// <summary>WO-911 — extra slots PURCHASED on <paramref name="id"/> (0 for an untouched channel).</summary>
        public int BoughtSlotsOf(ChannelId id)
        {
            var ch = GetChannel(id);
            return ch != null ? Mathf.Max(0, ch.BoughtSlots) : 0;
        }

        /// <summary>
        /// Grant an extra slot on <paramref name="id"/> WITHOUT charging or gating. Persists.
        /// </summary>
        /// <remarks>
        /// ⚠ WO-911 — this is the raw GRANT. It was the whole of B3 (unlimited free parallel
        /// workers, which also erodes the waiting pain the crystal sink monetizes). Player-facing
        /// callers MUST use <see cref="TryBuySlot"/>, which applies the owner's Echo gate and the
        /// crystal charge. This entry point survives for grants that are legitimately free
        /// (milestone/dev/test) and is now explicitly named as such.
        /// </remarks>
        public void GrantSlot(ChannelId id)
        {
            var ch = GetChannel(id);
            if (ch == null) return;
            ch.BoughtSlots = Mathf.Max(0, ch.BoughtSlots) + 1;
            Persist();
            // A newly-freed slot may immediately pull a pending job.
            ObsidianQueueEngine.PullIntoFreeSlots(ch, SlotCount(id), TimeSource.NowUnixMs());
            Persist();
            RaiseQueueChanged();
        }

        /// <summary>Back-compat alias for <see cref="GrantSlot"/> — see its remarks before calling.</summary>
        [Obsolete("WO-911: use TryBuySlot for any player-facing purchase (it applies the Echo gate + crystal charge). GrantSlot is the free grant.")]
        public void BuySlot(ChannelId id) => GrantSlot(id);

        // =====================================================================
        //  WO-911 — THE EXTRA-SLOT SINK: ECHO-GATED, CRYSTAL-PRICED (Q6 / M11)
        //  -------------------------------------------------------------------
        //  Owner ruling Q6: "each Echo above 2 unlocks the OPTION to purchase one
        //  extra queue slot with crystals." A TWO-STEP gate — the Echo count
        //  unlocks the RIGHT to buy; crystals complete it. NEITHER STEP ALONE
        //  GRANTS THE SLOT. Not the account-level milestone dial, not a building
        //  level. Convenience only: a slot buys parallelism, never combat power.
        //
        //  Q7 permits three sinks (a pack, a direct crystal buy, and a TEMPORARY
        //  unlock after watching X ads). Only the DIRECT CRYSTAL BUY is built
        //  here. The temporary one is deliberately NOT built: an EXPIRING slot
        //  needs a persisted per-channel expiry timestamp, ChannelState.BoughtSlots
        //  is a permanent int with no expiry concept, and that lands in the same
        //  SaveSchema territory as WO-912's rolling-window ad state — which the WO
        //  requires be designed JOINTLY, in ONE coordinated bump, rather than
        //  producing two competing rollover mechanisms in one file.
        // =====================================================================

        /// <summary>
        /// WO-911 (Q6) — how many extra slots the player's Echo count entitles them to BUY on a
        /// channel. "Each Echo above 2": <c>EchoCount - extraSlotEchoFloor</c>, floored at 0.
        /// </summary>
        /// <remarks>
        /// Reads <see cref="DeNelle.Core.State.GameState.EchoCount"/> — the PERSISTED authority in
        /// <c>DeNelle.Core</c> — deliberately, NOT <c>EchoService.Instance</c>. EchoService lives in
        /// DeNelle.Village and floors its getter at 1, and reading the Core field keeps this gate
        /// working headlessly and in any assembly that can already see GameState. Six Echoes exist,
        /// so the lever tops out at four extra slots per channel.
        /// </remarks>
        public int EchoEntitledSlots()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null) return 0;
            int floor = Config != null ? Mathf.Max(0, Config.extraSlotEchoFloor) : 2;
            return Mathf.Max(0, state.EchoCount - floor);
        }

        /// <summary>
        /// WO-911 (Q6) — crystal price of the NEXT extra slot on <paramref name="id"/>. Rises with
        /// the slots already bought on that channel so the second one is not the same trivial spend
        /// as the first. 0 when the sink is disabled in config.
        /// </summary>
        public int NextSlotPrice(ChannelId id)
        {
            int baseCost = Config != null ? Mathf.Max(0, Config.extraSlotBaseCrystals) : 0;
            if (baseCost <= 0) return 0;
            var ch = GetChannel(id);
            int bought = ch != null ? Mathf.Max(0, ch.BoughtSlots) : 0;
            return baseCost * (1 + bought);
        }

        /// <summary>
        /// WO-911 (Q6) — the owner's TWO-STEP purchase: the Echo count must unlock the right to buy
        /// AND the crystals must be spent. Charges the one GameState wallet, then widens both the
        /// worker pool and the line (see <see cref="QueueDepthLimit"/>).
        /// </summary>
        /// <param name="failure">
        /// Player-readable ASCII reason on a false return, null on success. State is carried by TEXT
        /// (the owner is red/green colourblind) and the broke case is prefixed with
        /// <see cref="InsufficientCrystalsPrefix"/> so the caller can route to the crystal store.
        /// </param>
        public bool TryBuySlot(ChannelId id, out string failure)
        {
            failure = null;
            var ch = GetChannel(id);
            if (ch == null) { failure = "Save not loaded."; return false; }

            int entitled = EchoEntitledSlots();
            int bought = Mathf.Max(0, ch.BoughtSlots);
            if (bought >= entitled)
            {
                // STEP ONE failed. Say WHAT unlocks it — an unexplained locked button is the bug.
                failure = EchoGateRefusal(entitled);
                DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                    $"slot buy on {id} refused — Echo gate ({bought} bought / {entitled} entitled).");
                return false;
            }

            int price = NextSlotPrice(id);
            if (price <= 0) { failure = "Extra slots are not for sale right now."; return false; }

            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null) { failure = "Save not loaded."; return false; }
            if (state.Resources.Crystals < price)
            {
                // STEP TWO failed. Stay visible + route to the faucet; never a silent no-op.
                failure = InsufficientCrystalsPrefix + $"{price} needed, {state.Resources.Crystals} held.";
                DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                    $"slot buy on {id} declined — broke ({state.Resources.Crystals}/{price}).");
                return false;
            }

            svc.AddCrystals(-price);
            GrantSlot(id);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                $"extra slot bought on {id} for {price} crystals (now {SlotCount(id)} slots, " +
                $"line depth {QueueDepthLimit(id)}).");
            return true;
        }

        /// <summary>
        /// WO-911 STEP ONE's refusal text, factored out of <see cref="TryBuySlot"/> so the pre-tap
        /// PROBE (<see cref="CanBuySlot"/>) and the act itself quote ONE sentence and can never drift.
        /// </summary>
        private static string EchoGateRefusal(int entitled)
            => entitled <= 0
                ? "Locked. Awaken a 3rd Echo to unlock extra queue slots."
                : $"Locked. You have used all {entitled} slot(s) your Echoes unlock - awaken another Echo.";

        /// <summary>
        /// WO-1045 — the PRE-TAP probe for <see cref="TryBuySlot"/>: may the player be OFFERED an
        /// extra slot on <paramref name="id"/> right now?
        /// <para>
        /// True => render the offer; <paramref name="reason"/> is null and
        /// <see cref="NextSlotPrice"/> is the ask. False => do NOT render a buy CTA;
        /// <paramref name="reason"/> is the same player-readable ASCII sentence
        /// <see cref="TryBuySlot"/> would have returned, so the UI states the unlock condition
        /// instead of showing a wall.
        /// </para>
        /// </summary>
        /// <remarks>
        /// ⚠ Deliberately does NOT test the crystal balance. Owner's rule (mirrored at
        /// <see cref="TryInstantFinish(ChannelId,string,out string)"/>): the button STAYS VISIBLE when
        /// broke and routes to the faucet — <see cref="TryBuySlot"/> returns the
        /// <see cref="InsufficientCrystalsPrefix"/> failure for that. Hiding the offer from a broke
        /// player would be the unexplained-locked-button bug in a new place.
        /// </remarks>
        public bool CanBuySlot(ChannelId id, out string reason)
        {
            reason = null;
            var ch = GetChannel(id);
            if (ch == null) { reason = "Save not loaded."; return false; }

            int entitled = EchoEntitledSlots();
            int bought = Mathf.Max(0, ch.BoughtSlots);
            if (bought >= entitled) { reason = EchoGateRefusal(entitled); return false; }

            if (NextSlotPrice(id) <= 0) { reason = "Extra slots are not for sale right now."; return false; }
            return true;
        }

        // ── Builder-channel convenience (the WO-172 callers) ──────────────────
        private static ChannelState Builder => GetChannel(ChannelId.Builder);
        private int BuilderSlots => SlotCount(ChannelId.Builder);

        /// <summary>A read-only snapshot of the Builder channel's running jobs (WO-172 API).</summary>
        public IReadOnlyList<BuildJobData> ActiveJobs => Builder != null ? Builder.ActiveJobs : System.Array.Empty<BuildJobData>();

        /// <summary>A read-only snapshot of a channel's running jobs (WO-773).</summary>
        public IReadOnlyList<BuildJobData> ActiveJobsOf(ChannelId id)
        {
            var ch = GetChannel(id);
            return ch != null ? ch.ActiveJobs : System.Array.Empty<BuildJobData>();
        }

        /// <summary>A read-only snapshot of a channel's FIFO pending queue (WO-773).</summary>
        public IReadOnlyList<BuildJobData> PendingJobsOf(ChannelId id)
        {
            var ch = GetChannel(id);
            return ch != null ? ch.PendingQueue : System.Array.Empty<BuildJobData>();
        }

        // =====================================================================
        //  Start a job — Builder channel (WO-108/WO-151 build + upgrade)
        // =====================================================================

        // ─────────────────────────────────────────────────────────────────────
        //  ★ WO-108 INTEGRATION POINT ★  (unchanged for callers)
        //  Placement commit calls StartBuild(structureId, tier); upgrades call
        //  StartUpgrade(structureId, targetTier). A full slot set now QUEUES the job
        //  (returns the pending job) instead of rejecting — the freed slot auto-pulls it.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Start (or QUEUE) a BUILD job for <paramref name="structureId"/> at
        /// <paramref name="tier"/> on the Builder channel. Returns the job (running or pending),
        /// or null if one already runs/queues for this id. Caller charges cost BEFORE calling.
        /// </summary>
        public BuildJobData? StartBuild(string structureId, int tier = 0, bool firstEverBuild = false)
            => StartBuilderJob(structureId, BuildJobType.Build, JobKind.Build, tier,
                               grace: firstEverBuild ? BuildGraceReason.FirstBuild : BuildGraceReason.None);

        /// <summary>
        /// WO-911 (M2) — <see cref="StartBuild(string,int,bool)"/> that RECORDS what the caller
        /// charged, so a later cancel can refund exactly 100% of it (owner ruling Q1). Pass the
        /// basket that was actually debited — <c>default</c> for a free build. Prefer this overload
        /// at every charging site; the un-costed one exists only for callers that genuinely pay
        /// nothing.
        /// </summary>
        public BuildJobData? StartBuild(string structureId, int tier, bool firstEverBuild, JobCost paid)
            => StartBuilderJob(structureId, BuildJobType.Build, JobKind.Build, tier,
                               grace: firstEverBuild ? BuildGraceReason.FirstBuild : BuildGraceReason.None,
                               paid: paid);

        /// <summary>
        /// WO-945 — <see cref="StartBuild(string,int,bool,JobCost)"/> that carries WHY the grace
        /// applies (<see cref="BuildGraceReason"/>), so onboarding-time grace and first-build grace
        /// trace distinctly. Prefer this at the placement site; the bool overloads above stay for
        /// back-compat (and are oracle-pinned by ObsidianQueueRegression).
        /// </summary>
        public BuildJobData? StartBuild(string structureId, int tier, BuildGraceReason grace, JobCost paid)
            => StartBuilderJob(structureId, BuildJobType.Build, JobKind.Build, tier,
                               grace: grace, paid: paid);

        /// <summary>
        /// Start (or QUEUE) an UPGRADE job for <paramref name="structureId"/> to
        /// <paramref name="targetLevel"/> on the Builder channel (F8-51 — level applies at
        /// completion via CompletedUpgradeApplier). Returns the job (running or pending), or
        /// null if one already runs/queues for this id.
        /// </summary>
        public BuildJobData? StartUpgrade(string structureId, int targetLevel)
            => StartBuilderJob(structureId, BuildJobType.Upgrade, JobKind.Upgrade,
                               Mathf.Max(0, targetLevel - 2), targetLevel);

        /// <summary>
        /// WO-911 (M2) — <see cref="StartUpgrade(string,int)"/> that RECORDS the charged basket for
        /// the 100%-flat cancel refund (ruling Q1).
        /// </summary>
        public BuildJobData? StartUpgrade(string structureId, int targetLevel, JobCost paid)
            => StartBuilderJob(structureId, BuildJobType.Upgrade, JobKind.Upgrade,
                               Mathf.Max(0, targetLevel - 2), targetLevel, paid: paid);

        /// <summary>
        /// True when a NEW Builder job would start immediately (a free Builder slot exists).
        /// With the queue a full slot no longer rejects — it queues — so callers may skip this
        /// check; it remains for UI ("all builders busy → will queue").
        /// </summary>
        public bool HasFreeSlot
        {
            get
            {
                var ch = Builder;
                return ch != null && ch.ActiveJobs.Count < BuilderSlots;
            }
        }

        /// <summary>
        /// WO-945 — the PURE grace math (headlessly testable, mirrors ObsidianQueueEngine's
        /// pure-core pattern): returns the duration a Builder job actually runs after the
        /// <paramref name="grace"/> rule. Applies only to builds (never upgrades), only when a
        /// reason is present and <paramref name="firstBuildSeconds"/> is enabled (&gt; 0), and
        /// ONLY EVER SHORTENS — a tier curve already under the grace is returned unchanged.
        /// FirstBuild and Onboarding shorten identically; they differ only in the trace the
        /// caller (StartBuilderJob) emits.
        /// </summary>
        public static double GraceAdjustedDurationMs(double durationMs, BuildGraceReason grace,
                                                     bool isUpgrade, float firstBuildSeconds)
        {
            if (grace == BuildGraceReason.None || isUpgrade || firstBuildSeconds <= 0f)
                return durationMs;
            double graceMs = firstBuildSeconds * 1000.0;
            return graceMs < durationMs ? graceMs : durationMs;
        }

        private BuildJobData? StartBuilderJob(string structureId, BuildJobType type, JobKind kind, int tier,
                                              int targetLevel = 0, BuildGraceReason grace = BuildGraceReason.None,
                                              JobCost paid = default)
        {
            if (string.IsNullOrEmpty(structureId)) return null;
            var ch = Builder;
            if (ch == null) return null;

            // One job per structure id (across active AND pending).
            if (IndexInChannel(ch, structureId) >= 0) return null;

            var curveKind = type == BuildJobType.Upgrade ? BuildJobKind.Upgrade : BuildJobKind.Build;
            double durationMs = Config.DurationSecondsForTier(tier, curveKind) * 1000.0;

            // OWNER RULING 2026-08-06 -- first-build grace. The FIRST time the player places a
            // given structure it builds in firstBuildSeconds instead of the tier curve, so
            // onboarding never stalls on a timer. Storage containers (the "pallets" -- lumberyard
            // / foundry / silo) are EXCLUDED by her explicit carve-out; the caller decides that,
            // because only it knows the catalog id (structureId here is the JOB key, which for a
            // placement is UnderConstructionVisual.KeyFor(data), NOT the catalog id).
            //
            // WO-945 -- the ruling's intent made literal: while the player is NOT Onboarded,
            // EVERY qualifying build gets the grace, not just the first-per-id (the tutorial asks
            // for two towers of the SAME id; tower #2 ran the full 90s curve and the scripted
            // teaching wave nearly destroyed it mid-construction). The caller passes the REASON
            // (BuildGraceReason) so the two rules trace distinctly in a capture; the shortening
            // itself is the pure GraceAdjustedDurationMs seam below, which the regression drives
            // headlessly.
            //
            // Upgrades never qualify: "first build" means the first BUILD. An upgrade of a
            // structure you have never owned is not reachable anyway.
            {
                double graced = GraceAdjustedDurationMs(durationMs, grace,
                    type == BuildJobType.Upgrade, Config.firstBuildSeconds);
                // Only ever SHORTEN. A tier-0 curve already under the grace (or a retuned config)
                // must not be made slower by this rule (GraceAdjustedDurationMs guarantees it;
                // the < test here just keeps the trace to genuine shortenings).
                if (graced < durationMs)
                {
                    if (grace == BuildGraceReason.Onboarding)
                        DeNelle.Core.Diagnostics.FlowTrace.Step("BuildTimer",
                            $"ONBOARDING grace on '{structureId}': {durationMs / 1000.0:0.#}s -> " +
                            $"{Config.firstBuildSeconds:0.#}s (not-yet-onboarded rule, WO-945).");
                    else
                        DeNelle.Core.Diagnostics.FlowTrace.Step("BuildTimer",
                            $"FIRST-BUILD grace on '{structureId}': {durationMs / 1000.0:0.#}s -> " +
                            $"{Config.firstBuildSeconds:0.#}s (tier {tier} curve bypassed).");
                    durationMs = graced;
                }
            }

            // WO-676 STEWARD (Foreman's Pace): ONE HeroTalentModifiers read shortens every
            // build/upgrade timer at job start. StatSum is null-safe (0 with no service/tree)
            // and the sum is clamped so a mis-authored node can never make timers negative.
            float haste = Mathf.Clamp01(DeNelle.Village.Talents.HeroTalentModifiers.StatSum(
                HeroTalentClassReader.Slug(), "buildTime"));
            if (haste > 0f)
            {
                durationMs *= 1.0 - haste;
                DeNelle.Core.Diagnostics.FlowTrace.Once("Talent", "buildTime",
                    $"buildTime -{haste:P0} applied to build/upgrade timer duration (WO-676 Foreman's Pace).");
            }

            // WO-1246: an instant-build charge skips the remaining timer. Consume only when
            // there is still time to skip — a already-zero job (or a queued one that will
            // complete on pull) must not burn a paid token for nothing.
            if (durationMs > 0.0 && ConvenienceRedeemer.TrySkipBuildTimer())
                durationMs = 0.0;

            var job = new BuildJobData
            {
                StructureId = structureId,
                JobType = (int)type,
                Kind = (int)kind,
                Channel = (int)ChannelId.Builder,
                DurationMs = durationMs,
                TargetTier = targetLevel,
                Paid = paid,                      // WO-911 M2 — the refund basket rides the job
            };

            bool started = ObsidianQueueEngine.Enqueue(ch, BuilderSlots, job, TimeSource.NowUnixMs(),
                                                       QueueDepthLimit(ChannelId.Builder), out bool accepted);
            if (!accepted)
            {
                // WO-911 (Q4) — the line is at its DEPTH cap. Refuse LOUDLY: the caller has usually
                // already charged, so a silent null here would eat the player's resources.
                _lastEnqueueFailure = LineFullMessage(ChannelId.Builder);
                DeNelle.Core.Diagnostics.FlowTrace.Warn("BuildTimer",
                    $"StartBuilderJob REFUSED for '{structureId}' — {_lastEnqueueFailure} " +
                    "(caller must refund whatever it charged).");
                return null;
            }
            _lastEnqueueFailure = null;
            Persist();

            if (type == BuildJobType.Upgrade)
                DeNelle.Core.Diagnostics.FlowTrace.Step("BuildTimer",
                    $"upgrade '{structureId}' {(started ? "started" : "QUEUED")} ({durationMs / 1000.0:0}s, " +
                    $"tier {Mathf.Max(1, targetLevel - 1)}->{targetLevel})");

            if (started) JobStarted?.Invoke(job);
            RaiseQueueChanged();

            // A zero-duration RUNNING job completes immediately (queued ones start later).
            if (started && durationMs <= 0) CompleteJob(structureId);
            return job;
        }

        // =====================================================================
        //  Generic enqueue — the "everything flows through it" seam (WO-773)
        // =====================================================================

        /// <summary>
        /// Enqueue a job of any <paramref name="kind"/> onto its default channel (Build/Repair/
        /// Upgrade/Tower*/Wall* → Builder; TrainTroop → Train; UnlockTier/LearnMagic → Research).
        /// Starts immediately if a slot is free on that channel, else queues. The effect on
        /// completion is the IJobEffect registered for the kind. Returns the job, or null if one
        /// already runs/queues for <paramref name="targetId"/> on that channel.
        /// </summary>
        public BuildJobData? Enqueue(JobKind kind, string targetId, double durationSeconds, int targetTier = 0)
            => Enqueue(kind, JobChannels.DefaultChannel(kind), targetId, durationSeconds, targetTier);

        /// <summary>
        /// WO-911 (M2) — default-channel enqueue that RECORDS the charged basket for the 100%-flat
        /// cancel refund (ruling Q1).
        /// </summary>
        public BuildJobData? Enqueue(JobKind kind, string targetId, double durationSeconds, int targetTier, JobCost paid)
            => Enqueue(kind, JobChannels.DefaultChannel(kind), targetId, durationSeconds, targetTier, paid);

        /// <summary>Enqueue a job onto an explicit <paramref name="channel"/> (see the default-channel overload).</summary>
        public BuildJobData? Enqueue(JobKind kind, ChannelId channel, string targetId, double durationSeconds, int targetTier = 0)
            => Enqueue(kind, channel, targetId, durationSeconds, targetTier, default);

        /// <summary>
        /// Enqueue onto an explicit <paramref name="channel"/>, recording the basket the caller
        /// charged (WO-911 M2). Returns null when the id is already in flight OR when the line is at
        /// its DEPTH cap — check <see cref="LastEnqueueFailure"/> to tell those apart and to get a
        /// player-readable reason.
        /// </summary>
        public BuildJobData? Enqueue(JobKind kind, ChannelId channel, string targetId, double durationSeconds,
                                     int targetTier, JobCost paid)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            var ch = GetChannel(channel);
            if (ch == null) return null;
            if (IndexInChannel(ch, targetId) >= 0)
            {
                _lastEnqueueFailure = "Already in the queue.";
                return null;
            }

            double duration = durationSeconds;
            if (kind == JobKind.Repair && duration > 0.0 && ConvenienceRedeemer.TrySkipRepairTimer())
                duration = 0.0;

            var job = new BuildJobData
            {
                StructureId = targetId,
                JobType = kind == JobKind.Upgrade || kind == JobKind.TowerUpgrade || kind == JobKind.WallUpgrade
                    ? (int)BuildJobType.Upgrade : (int)BuildJobType.Build,
                Kind = (int)kind,
                Channel = (int)channel,
                DurationMs = Math.Max(0.0, duration) * 1000.0,
                TargetTier = targetTier,
                Paid = paid,                      // WO-911 M2 — the refund basket rides the job
            };

            bool started = ObsidianQueueEngine.Enqueue(ch, SlotCount(channel), job, TimeSource.NowUnixMs(),
                                                       QueueDepthLimit(channel), out bool accepted);
            if (!accepted)
            {
                _lastEnqueueFailure = LineFullMessage(channel);
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Obsidian",
                    $"job '{kind}' -> '{targetId}' REFUSED on {channel} — {_lastEnqueueFailure} " +
                    "(caller must refund whatever it charged).");
                return null;
            }
            _lastEnqueueFailure = null;
            Persist();
            DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                $"job '{kind}' -> '{targetId}' {(started ? "started" : "QUEUED")} on {channel} ({duration:0}s).");
            if (started) JobStarted?.Invoke(job);
            RaiseQueueChanged();
            if (started && job.DurationMs <= 0) CompleteChannelJob(channel, targetId);
            return job;
        }

        // =====================================================================
        //  WO-911 (M1) — QUEUE DEPTH: 5 TOTAL PER LINE (owner ruling Q4)
        // =====================================================================

        private string _lastEnqueueFailure;

        /// <summary>
        /// WO-911 — why the LAST enqueue attempt was refused, in player-readable ASCII, or null if
        /// the last attempt succeeded. Callers surface this instead of failing silently (§12).
        /// </summary>
        public string LastEnqueueFailure => _lastEnqueueFailure;

        /// <summary>
        /// WO-911 (Q4) — the DEPTH cap for <paramref name="id"/>: how many items may be lined up on
        /// that ONE channel (active + pending). Authored data
        /// (<see cref="BuildTimerConfig.queueDepthPerLine"/>, 5) plus the purchased slots on this
        /// channel, so an Echo-gated slot buy widens the line as well as the worker pool. 0 when the
        /// config disables the cap.
        /// </summary>
        /// <remarks>
        /// ⚠ Per-CHANNEL, never global (ruling Q4): Builder at 5 must not block Research or Train.
        /// And this is NOT <see cref="BuildTimerConfig.freeBuildSlots"/> — see WO-911 section 2d.
        /// </remarks>
        public int QueueDepthLimit(ChannelId id)
        {
            int authored = Config != null ? Config.queueDepthPerLine : 0;
            var ch = GetChannel(id);
            int bought = ch != null ? Mathf.Max(0, ch.BoughtSlots) : 0;
            // WO-1253: depth stays authored + crystal-bought slots. The permanent-builder
            // SKU is CONCURRENCY and is deliberately not an input here.
            return DepthOf(authored, bought);
        }

        /// <summary>WO-911 — true when <paramref name="id"/> can accept no more work right now.</summary>
        public bool IsLineFull(ChannelId id) => ObsidianQueueEngine.IsFull(GetChannel(id), QueueDepthLimit(id));

        /// <summary>WO-911 — items currently lined up on <paramref name="id"/> (active + pending).</summary>
        public int QueueDepth(ChannelId id)
        {
            var ch = GetChannel(id);
            return ch != null ? ch.Count : 0;
        }

        /// <summary>
        /// WO-911 — the ASCII, colour-independent reason a line refused work. State is carried by
        /// TEXT because the owner is red/green colourblind; never encode "full" by tint alone.
        /// <para>
        /// ⚠ WO-1045 — PUBLIC on purpose. This sentence used to be reachable only AFTER a refusal
        /// (via <see cref="LastEnqueueFailure"/>), so every pre-tap surface that wanted to say "the
        /// line is full" had to re-compose it — and one already did, verbatim, in
        /// <c>PlacedStructureUpgradeService.TryStart</c>. Two copies of a player-facing sentence is
        /// the drift bug in miniature. A button now quotes THIS before the tap, so what the player
        /// reads on a greyed-out button is byte-identical to what the service would have refused with.
        /// </para>
        /// <para>
        /// ⚠ It names the DEPTH axis (<see cref="QueueDepthLimit"/>, how many may be LINED UP) and
        /// never the CONCURRENCY axis (<see cref="BuildTimerConfig.freeBuildSlots"/>, how many run at
        /// once). Exhausted concurrency does NOT refuse — it QUEUES. Never re-word this into "all
        /// builders are busy": that is a different, non-blocking condition with a different remedy.
        /// </para>
        /// </summary>
        public string LineFullMessage(ChannelId id)
            => $"{ChannelWord(id)} queue is full ({QueueDepth(id)}/{QueueDepthLimit(id)}). Cancel or finish an item first.";

        // =====================================================================
        //  WO-1252 — PLACE-TIME BUSY COPY (CONCURRENCY next-step, not DEPTH)
        //  -------------------------------------------------------------------
        //  LineFullMessage above is DEPTH and must never say "busy" (WO-1045
        //  oracle). This sentence is the placement toast: the owner is blocked
        //  and needs a next step, not a restatement of the wall.
        // =====================================================================

        /// <summary>
        /// WO-1252. Toast inner width in reference px (card 500 minus left accent + padding).
        /// Each composed line must fit this at <see cref="BusyCrewGlyphPx"/>.
        /// </summary>
        public const int BusyCrewToastInnerPx = 462;

        /// <summary>
        /// WO-1252. Conservative glyph advance for 24 px LegacyRuntime on the place toast.
        /// Matches the in-repo truncation budget used by BuilderSkuRegression (14 is tighter
        /// than that suite's 16 because this is body copy, not a CTA).
        /// </summary>
        public const int BusyCrewGlyphPx = 14;

        /// <summary>
        /// WO-1252. ASCII next-step copy when a place is blocked because Builder crews are
        /// saturated. Distinct from <see cref="LineFullMessage"/> (DEPTH). Always names
        /// wait-or-Manage; names TryBuySlot / the store builder only when the player
        /// actually has those options. Explicit newlines — wrap is deliberate, never
        /// a mid-word ellipsis.
        /// </summary>
        public string BusyCrewMessage()
            => ComposeBusyCrewMessage(CanBuySlot(ChannelId.Builder, out _), OffersPermanentBuilder());

        /// <summary>
        /// WO-1252. Pure composer so the oracle can drive both branches (qualifies-to-buy
        /// vs does-not) without a live wallet. Instance method <see cref="BusyCrewMessage"/>
        /// asks the service what options the player actually has and quotes this.
        /// </summary>
        public static string ComposeBusyCrewMessage(bool canBuySlot, bool offersPermanentBuilder)
        {
            // Measured at BusyCrewGlyphPx against BusyCrewToastInnerPx. Longest line
            // ("Wait, or complete under Manage.") is 31 glyphs * 14 = 434 px <= 462.
            string msg = "Builders are busy.\nWait, or complete under Manage.";
            if (canBuySlot)
                msg += "\nOr buy an extra queue slot.";
            if (offersPermanentBuilder)
                msg += "\nOr get a store builder.";
            return msg;
        }

        // =====================================================================
        //  WO-911 (M2) — COST ADAPTERS
        //  -------------------------------------------------------------------
        //  The tree carries THREE unrelated ResourceCost types (Core.Catalog's
        //  lowercase struct, EconomyService's PascalCase struct with Coins, and
        //  the Ledger's one-line-per-resource readonly struct). JobCost is a
        //  plain 5-int value in Core so BuildJobData depends on none of them;
        //  these adapters are the ONLY place the shapes meet, so a charge site
        //  records its basket in one call and cannot mis-map a field.
        // =====================================================================

        /// <summary>WO-911 — the catalog cost shape (structures-catalog / upgradeCost) as a paid basket.</summary>
        public static JobCost ToJobCost(DeNelle.Core.Catalog.ResourceCost c)
            => new JobCost(c.wood, c.food, c.iron, c.crystals);

        /// <summary>WO-911/v39 — the EconomyService cost shape as a fully refundable paid basket.</summary>
        /// <remarks>
        /// Fully qualified on purpose: this file carries <c>using DeNelle.Core.Catalog;</c>, so a bare
        /// <c>ResourceCost</c> here would silently mean the ENCLOSING namespace's type and read as the
        /// same overload as the one above. Naming both explicitly makes the pair unmistakable.
        /// </remarks>
        public static JobCost ToJobCost(DeNelle.Village.ResourceCost c)
        {
            return new JobCost(c.Wood, c.Food, c.Iron, c.Crystals, coins: c.Coins);
        }

        /// <summary>WO-911 — a ledger cost-line list (+ optional magic) as a paid basket.</summary>
        public static JobCost ToJobCost(System.Collections.Generic.IReadOnlyList<Ledger.ResourceCost> lines, int magic = 0)
        {
            var jc = new JobCost(0, 0, 0, 0, Mathf.Max(0, magic));
            if (lines == null) return jc;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Amount <= 0) continue;
                switch (line.Resource)
                {
                    case Ledger.HarvestResource.Wood: jc.Wood += line.Amount; break;
                    case Ledger.HarvestResource.Food: jc.Food += line.Amount; break;
                    case Ledger.HarvestResource.Iron: jc.Iron += line.Amount; break;
                    case Ledger.HarvestResource.Crystals: jc.Crystals += line.Amount; break;
                }
            }
            return jc;
        }

        /// <summary>ASCII display word for a channel ("Builders" / "Training" / "Research").</summary>
        public static string ChannelWord(ChannelId id)
        {
            switch (id)
            {
                case ChannelId.Train: return "Training";
                case ChannelId.Research: return "Research";
                default: return "Builders";
            }
        }

        // =====================================================================
        //  Query — Builder channel (WO-172 API, back-compat)
        // =====================================================================

        /// <summary>True if a job (running OR queued) is in flight for this structure id on the Builder channel.</summary>
        public bool IsBuilding(string structureId) => Builder != null && IndexInChannel(Builder, structureId) >= 0;

        /// <summary>Seconds remaining for the Builder job on <paramref name="structureId"/> (full duration while queued; 0 if none).</summary>
        public double RemainingSeconds(string structureId) => RemainingSeconds(ChannelId.Builder, structureId);

        /// <summary>
        /// WO-911 — seconds remaining for a job on ANY channel. A QUEUED job (StartMs = 0) reports
        /// its FULL duration: it has not started, so nothing has elapsed. That is the pricing input
        /// ruling Q5 needs, and it feeds the EXISTING curve
        /// (<see cref="BuildTimerConfig.InstantFinishPrice"/>) rather than a second one.
        /// </summary>
        public double RemainingSeconds(ChannelId channel, string structureId)
        {
            var job = FindInChannel(GetChannel(channel), structureId, out bool _);
            if (!job.HasValue) return 0;
            var j = job.Value;
            if (j.StartMs <= 0) return j.DurationMs / 1000.0;   // queued — not started yet
            double remMs = j.FinishMs - TimeSource.NowUnixMs();
            return remMs > 0 ? remMs / 1000.0 : 0;
        }

        /// <summary>0..1 progress for the Builder job on <paramref name="structureId"/> (0 while queued; 1 if none/done).</summary>
        public float Progress(string structureId)
        {
            var job = FindInChannel(Builder, structureId, out bool _);
            if (!job.HasValue) return 1f;
            var j = job.Value;
            if (j.StartMs <= 0) return 0f;      // queued — not started yet
            if (j.DurationMs <= 0) return 1f;
            double elapsed = TimeSource.NowUnixMs() - j.StartMs;
            return Mathf.Clamp01((float)(elapsed / j.DurationMs));
        }

        /// <summary>Crystal price to instant-finish this Builder job right now (0 = none in flight / paid skip disabled).</summary>
        public int InstantFinishPrice(string structureId) => InstantFinishPrice(ChannelId.Builder, structureId);

        // =====================================================================
        //  WO-911 — SPEEDUPS ON EVERY CHANNEL, INCLUDING QUEUED JOBS
        //  -------------------------------------------------------------------
        //  Owner's monetization spine: "Finish on ALL channels", "always show
        //  Finish while a job runs", "price from remaining time with a minimum".
        //  Ruling Q5 extends it to a job that has NOT started.
        //
        //  Before this WO these three wrappers hard-resolved the Builder channel
        //  and early-returned on StartMs <= 0, so Train/Research showed no CTA at
        //  all and 3 of a 5-deep queue offered nothing. The COMPLETION machinery
        //  was ALREADY channel-generic (CompleteChannelJob), so this is a
        //  generalization of the wrappers -- NOT new machinery, and NOT a second
        //  pricing curve. BuildTimerConfig.InstantFinishPrice stays the only
        //  curve; all that changed is what "remaining" means for a queued job
        //  (its full duration -- see RemainingSeconds above).
        // =====================================================================

        /// <summary>
        /// WO-911 — crystal price to Complete Now on ANY channel, for a RUNNING **or QUEUED** job
        /// (ruling Q5). 0 only when no such job exists or the paid skip is disabled in config.
        /// Priced from remaining time through the existing curve, with its authored minimum, so a
        /// near-done job is never free.
        /// </summary>
        public int InstantFinishPrice(ChannelId channel, string structureId)
        {
            var job = FindInChannel(GetChannel(channel), structureId, out bool _);
            if (!job.HasValue) return 0;

            // WO-1042 / owner ruling 2026-08-16 — a RANDOM-outcome job is never priced for a paid
            // finish. Returning 0 here is the AFFORDANCE half of the exclusion: both queue UIs
            // (ManageScreenPanel's Finish CTA and ObsidianQueueHud's rush row) build their button
            // only when price > 0, so the button cannot be rendered for a polish job by any list
            // that prices its rows through this method — including a future generic one. The ACT
            // half is refused independently in TryInstantFinish, so this is defence in depth and
            // not the only gate. See JobRushPolicy for why (it is a loot-box question, not a
            // balance one).
            if (!JobRushPolicy.AllowsPaidInstantFinish(job.Value.JobKind)) return 0;

            // WO-1372 Lane D — a TRAINING job is priced in GOLD (HireReinforcementsPrice); every
            // other kind keeps the crystal price. Same curve, different knob pair + wallet; the
            // wallet half is branched identically in TryInstantFinish so the face and the debit
            // can never disagree. FinishPaysGold is the ONE currency map for both.
            double remaining = RemainingSeconds(channel, structureId);
            return FinishPaysGold(job.Value.JobKind)
                ? Config.HireReinforcementsPrice(remaining)
                : Config.InstantFinishPrice(remaining);
        }

        /// <summary>True when a rewarded-ad skip is allowed right now (cooldown clear AND daily cap not hit).</summary>
        public bool CanWatchAdToSkip(string structureId) => CanWatchAdToSkip(ChannelId.Builder, structureId);

        /// <summary>
        /// WO-911 — ad-skip eligibility on ANY channel. Still RUNNING-ONLY: the ad grants a fixed
        /// -N minutes off a countdown, and a queued job has no countdown to shorten yet (pulling its
        /// StartMs back would silently start it out of FIFO order). A queued item's speed-up is
        /// Complete Now, which is explicit and priced.
        /// </summary>
        public bool CanWatchAdToSkip(ChannelId channel, string structureId)
        {
            // RELEASE BLOCKER GATE (2026-08-07): OFF by default, so every ad CTA is ABSENT until a
            // real ad SDK + WO-912 server-side ad-window validation land. See
            // FeatureFlags.RewardedAdSkip for the two hard prerequisites.
            if (!DeNelle.Core.FeatureFlags.RewardedAdSkip)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Once("Obsidian", "adskip-flagoff",
                    "Ad-skip CTA suppressed on every channel: ff.rewardedadskip is OFF (no ad SDK " +
                    "is wired, so the reward would be granted for free). Not a missing button.");
                return false;
            }

            var job = FindInChannel(GetChannel(channel), structureId, out bool isActive);
            if (!job.HasValue || !isActive || job.Value.StartMs <= 0) return false;

            // ⚠ NO POLICY GATE HERE, AND THAT IS DELIBERATE (owner ruling 2026-08-16).
            // Ad-skip is ALLOWED on a random-outcome job. The doctrine is SELL THE WAIT, NEVER THE
            // ROLL: an ad shortens a countdown the player would have reached anyway and involves no
            // purchase, so it is not the regulated shape. Only the CASH/crystal instant-finish is
            // excluded — see JobRushPolicy and InstantFinishPrice. Do not "tidy" a gate in here.
            if (UnderDailyAdCap() == false) return false;

            // WO-1120 — THE PLACEMENT GATE, ANDed with the WO-912 rolling window above, never
            // merged with it. They answer different questions: the rolling window is the owner's
            // 2026-08-06 four-hour conversion wall, and the placement carries its own per-local-day
            // cap, per-placement cooldown and the global hardDailyCap from ad-placements.json.
            // Merging them would silently retire one of the two rulings; ANDing means the stricter
            // one binds, which is the only safe direction for a gate that hands out real value.
            if (!AdGateService.IsOffered(BuildSkipPlacementId)) return false;

            var mgr = RewardedAdManager.Instance;
            return mgr != null && mgr.IsAdReady;
        }

        /// <summary>
        /// The ad-placements.json placement this channel's skip is served by. It is the file's
        /// "THE ONLY V1 PLACEMENT" and its reward (reward.build.timeskip) pays MINUTES, never a
        /// completion — an instant finish is what crystals are sold for, and giving it away for a
        /// watch would hand away the paid product for free.
        /// </summary>
        public const string BuildSkipPlacementId = "place.build.skip";

        /// <summary>
        /// Watch a rewarded ad to knock a fixed chunk (Config.adSkipSeconds) off the remaining
        /// timer. Opt-in, store-build only, capped per day. The timer always finishes on its own.
        /// </summary>
        [Obsolete("MON-1146: use the asynchronous WatchAdToSkip overload with onComplete.")]
        public bool WatchAdToSkip(string structureId) => WatchAdToSkip(ChannelId.Builder, structureId);

        /// <summary>WO-911 — ad-skip on ANY channel (running jobs only; see <see cref="CanWatchAdToSkip(ChannelId,string)"/>).</summary>
        [Obsolete("MON-1146: synchronous rewarded ads are permanently refused; use the onComplete overload.")]
        public bool WatchAdToSkip(ChannelId channel, string structureId)
        {
            DeNelle.Core.Diagnostics.FlowTrace.Fail("Obsidian",
                $"WatchAdToSkip('{structureId}' on {channel}) sync overload REFUSED (WO-1146). " +
                "It cannot represent a later SDK reward callback and it bypasses the placement " +
                "ledger. No ad was shown, no time was skipped, and no allowance was consumed.");
            return false;
#if false
            // Same gate as CanWatchAdToSkip — the ACT is refused too, not just the affordance, so a
            // stale UI reference or a direct caller can never reach the reward while the flag is OFF.
            if (!DeNelle.Core.FeatureFlags.RewardedAdSkip)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Obsidian",
                    $"WatchAdToSkip('{structureId}' on {channel}) REFUSED: ff.rewardedadskip is OFF. " +
                    "No ad SDK is wired, so granting the skip would be a free reward with no ad shown. " +
                    "No time was skipped and no window allowance was consumed.");
                return false;
            }

            var job = FindInChannel(GetChannel(channel), structureId, out bool isActive);
            if (!job.HasValue || !isActive || job.Value.StartMs <= 0) return false;
            if (!UnderDailyAdCap()) return false;

            var mgr = RewardedAdManager.Instance;
            if (mgr == null) return false;

            return mgr.TryShowAd(() =>
            {
                RecordAdSkipUsed();
                ApplySkipSeconds(channel, structureId, Config.adSkipSeconds);
            });
#endif
        }

        /// <summary>
        /// WO-1125 — the ASYNC ad-skip. Same gates and same grant as the bool overload, but the
        /// outcome arrives through <paramref name="onComplete"/> when the ad actually finishes,
        /// which is the only shape a real SDK can honour. The return value means PRESENTATION
        /// STARTED, never "reward earned".
        ///
        /// <para>The bool overload above is kept and still correct for the shipping state (the flag
        /// is OFF, so it refuses synchronously). It becomes WRONG the moment a real network is
        /// wired: the reward callback lands seconds after the return, so `granted` is always false
        /// and the player is told the ad failed after watching all of it. New callers use this one.</para>
        ///
        /// <para>EVERY refusal path reports through <paramref name="onComplete"/> too. A UI that
        /// disables a button on the call and re-enables it in the callback must never be left
        /// hanging by an early return - a silent refusal is a stuck button (CLAUDE.md section 12).</para>
        /// </summary>
        public bool WatchAdToSkip(ChannelId channel, string structureId,
                                  Action<DeNelle.Core.Ads.AdShowResult> onComplete)
        {
            if (!DeNelle.Core.FeatureFlags.RewardedAdSkip)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Obsidian",
                    $"WatchAdToSkip('{structureId}' on {channel}) REFUSED: ff.rewardedadskip is OFF. " +
                    "No ad SDK is wired, so granting the skip would be a free reward with no ad shown. " +
                    "No time was skipped and no window allowance was consumed.");
                onComplete?.Invoke(DeNelle.Core.Ads.AdShowResult.Unavailable(
                    DeNelle.Core.Ads.AdUnavailableReason.Disabled));
                return false;
            }

            var job = FindInChannel(GetChannel(channel), structureId, out bool isActive);
            if (!job.HasValue || !isActive || job.Value.StartMs <= 0)
            {
                onComplete?.Invoke(DeNelle.Core.Ads.AdShowResult.Unavailable(
                    DeNelle.Core.Ads.AdUnavailableReason.Disabled));
                return false;
            }

            if (!UnderDailyAdCap())
            {
                // OUR cap, not the network's - CappedByGame keeps that distinction in telemetry.
                onComplete?.Invoke(DeNelle.Core.Ads.AdShowResult.Unavailable(
                    DeNelle.Core.Ads.AdUnavailableReason.CappedByGame));
                return false;
            }

            var mgr = RewardedAdManager.Instance;
            if (mgr == null)
            {
                onComplete?.Invoke(DeNelle.Core.Ads.AdShowResult.Unavailable(
                    DeNelle.Core.Ads.AdUnavailableReason.NotInitialised));
                return false;
            }

            // WO-1120 — routed through the PLACEMENT INTERPRETER rather than straight at
            // RewardedAdManager. AdGateService re-checks the placement's own cooldown / daily cap /
            // global hard cap, screens the reward against _LAW_1 a second time, records the watch
            // in the placement ledger, and still grants ONLY from the SDK's genuine earned-reward
            // callback. The subject (WHICH job) is ours to supply and only ours — that is what the
            // action below is; the POLICY stays in the gate, so a call site can pick the job but
            // never the rules.
            //
            // RecordAdSkipUsed() stays here and is NOT moved into the gate: it stamps the WO-912
            // four-hour rolling window in the SAVE, which is a different ledger with a different
            // shape and a different ruling behind it. Two ledgers, both advanced on the same
            // genuine reward.
            return AdGateService.Present(
                BuildSkipPlacementId,
                () =>
                {
                    RecordAdSkipUsed();
                    ApplySkipSeconds(channel, structureId, Config.adSkipSeconds);
                },
                onComplete);
        }

        /// <summary>
        /// Premium instant-finish: spend crystals (single GameState wallet) to complete the
        /// Builder job now. No-op if the price is 0 (disabled) or unaffordable.
        /// </summary>
        public bool TryInstantFinish(string structureId) => TryInstantFinish(ChannelId.Builder, structureId);

        /// <summary>
        /// WO-911 — Complete Now on ANY channel, for a RUNNING or QUEUED job (ruling Q5), spending
        /// crystals from the one GameState wallet. Acts ONLY on the id passed in (ruling Q11: never
        /// a game-wide pass, never an ambiguous aggregate).
        /// </summary>
        /// <param name="failure">
        /// Player-readable ASCII reason on a false return, or null on success. NEVER a silent
        /// no-op: today's UI taps a visible button and nothing happens (WO-911 section 2c #5).
        /// "Broke" is reported distinctly so the caller can route to the crystal store.
        /// </param>
        public bool TryInstantFinish(ChannelId channel, string structureId, out string failure)
        {
            failure = null;

            // ⛔ THE RULING GATE (owner 2026-08-16) — checked FIRST, before price, wallet or state,
            // so the refusal is unambiguous and can never be mistaken for "you are broke" or "there
            // is no job". A paid instant resolve of a RANDOM outcome is mechanically a loot box and
            // is regulated in several jurisdictions in the shipping plan; a re-polish can even trade
            // DOWN, so a purchase could buy a strictly worse item. Waiting and ad-skip stay open.
            // InstantFinishPrice also returns 0 for these kinds (which hides the button) — this is
            // the ACT half, defence in depth, because a gap in a list UI must hit a wall, not silence.
            // See JobRushPolicy.
            {
                var gated = FindInChannel(GetChannel(channel), structureId, out bool _);
                if (gated.HasValue &&
                    JobRushPolicy.RefusePaidFinish(gated.Value.JobKind, "TryInstantFinish",
                                                   structureId, out string ruled))
                {
                    failure = ruled;
                    return false;
                }
            }

            int price = InstantFinishPrice(channel, structureId);
            if (price <= 0)
            {
                failure = "Nothing to finish here.";
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Obsidian",
                    $"TryInstantFinish('{structureId}' on {channel}) — no priced job (price {price}).");
                return false;
            }

            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null)
            {
                failure = "Save not loaded.";
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Obsidian", "TryInstantFinish with no GameState.");
                return false;
            }

            // ⛔ WO-1372 Lane D — THE WALLET BRANCH. A TrainTroop job is paid in GOLD (owner
            // 2026-09-04: "gold buys hire mercenaries instead of waiting on time"); every other
            // kind pays crystals below, byte-for-byte as before. This is the ONE place a coins
            // debit for a finish may live (HireReinforcementsRegression case 5a scans _Modules for
            // a second one). The price quoted by InstantFinishPrice is already the gold price for
            // this kind, so the face and the debit are the same number.
            if (FinishPaysGold(channel, structureId))
            {
                if (state.Resources.Coins < price)
                {
                    // Its OWN prefix, never the crystal one: gold is EARNED (raids/selling), and
                    // routing a gold shortfall to the crystal store would sell nothing that helps.
                    failure = InsufficientGoldPrefix + $"{price} needed, {state.Resources.Coins} held.";
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                        $"TryInstantFinish('{structureId}' on {channel}) declined — broke on GOLD " +
                        $"({state.Resources.Coins}/{price}).");
                    return false;
                }

                SpendCoins(svc, state, price);
                CompleteAnyJob(channel, structureId);
                DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                    $"{HireReinforcementsVerb}: '{structureId}' on {channel} for {price} gold " +
                    $"(coins now {state.Resources.Coins}).");
                return true;
            }

            if (state.Resources.Crystals < price)
            {
                // Owner's rule: the button STAYS VISIBLE when broke and offers a route to buy.
                // The caller reads InsufficientCrystalsPrefix to decide to open the store.
                failure = InsufficientCrystalsPrefix + $"{price} needed, {state.Resources.Crystals} held.";
                DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                    $"TryInstantFinish('{structureId}') declined — broke ({state.Resources.Crystals}/{price}).");
                return false;
            }

            svc.AddCrystals(-price);
            CompleteAnyJob(channel, structureId);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                $"Complete Now: '{structureId}' on {channel} for {price} crystals.");
            return true;
        }

        /// <summary>WO-911 — <see cref="TryInstantFinish(ChannelId,string,out string)"/> without the reason.</summary>
        public bool TryInstantFinish(ChannelId channel, string structureId)
            => TryInstantFinish(channel, structureId, out _);

        /// <summary>
        /// WO-911 — marker that prefixes the "cannot afford" failure so a caller can tell the broke
        /// case (route to the crystal store) from a structural one, without parsing prose.
        /// </summary>
        public const string InsufficientCrystalsPrefix = "Not enough crystals: ";

        // =====================================================================
        //  WO-1372 LANE D — HIRE REINFORCEMENTS: gold buys TROOP time
        //  -------------------------------------------------------------------
        //  Owner ruling 2026-09-04, verbatim: "gold buys hire mercenaries
        //  instead of waiting on time." Creative canon §6: the mercenaries ARE
        //  the same unit (no second roster, no upkeep, no expiry) and the button
        //  reads HIRE REINFORCEMENTS, never "Skip Training".
        //
        //  NOT a second speed-up. The speed-up (TryInstantFinish + CompleteAnyJob
        //  + the one skip curve) was already channel-generic; the defect was the
        //  CURRENCY. So Lane D is: ONE currency map (FinishPaysGold), the gold
        //  branch inside TryInstantFinish, and a gold knob pair on the SAME curve.
        //  A Train job completing this way reaches OnJobCompleted ->
        //  JobEffectRegistry -> BarracksService.TrainTroopEffect exactly like a
        //  timer expiry, so the troop lands in the roster by the shipping path.
        // =====================================================================

        /// <summary>The canon face for the gold-priced training finish (creative canon §6).
        /// Every surface quotes THIS constant; a retyped face drifts from canon.</summary>
        public const string HireReinforcementsVerb = "HIRE REINFORCEMENTS";

        /// <summary>
        /// WO-1372 — prefixes the "cannot afford" failure of a GOLD-priced finish, DISTINCT from
        /// <see cref="InsufficientCrystalsPrefix"/>: a gold shortfall must never route the player to
        /// the crystal store (which sells no gold — gold is earned by raiding and selling).
        /// </summary>
        public const string InsufficientGoldPrefix = "Not enough gold: ";

        /// <summary>
        /// THE ONE CURRENCY MAP. True for <see cref="JobKind.TrainTroop"/> and ONLY TrainTroop: gold
        /// buys troop time, every other channel keeps crystals (which is what funds the crystal
        /// ladders — BuildTimerConfig "ONE WALLET, DELIBERATELY"). No currency creep.
        /// </summary>
        public static bool FinishPaysGold(JobKind kind) => kind == JobKind.TrainTroop;

        /// <summary>
        /// <see cref="FinishPaysGold(JobKind)"/> for a LIVE job (running or queued) on
        /// <paramref name="channel"/>. False when no such job exists.
        /// </summary>
        public bool FinishPaysGold(ChannelId channel, string structureId)
        {
            var job = FindInChannel(GetChannel(channel), structureId, out bool _);
            return job.HasValue && FinishPaysGold(job.Value.JobKind);
        }

        // The GOLD debit for a finish. GameState.Resources.Coins is the one coin store (the same
        // struct EconomyService.AddCoins and the town HUD read), and GameStateService owns its
        // persistence — so this mirrors GameStateService.AddCrystals' exact shape (read struct,
        // clamp, assign back, Save, ResourcesChanged) rather than routing through
        // EconomyService.Instance, a scene MonoBehaviour that is absent pre-boot and in the
        // headless/editor oracles (it would silently no-op the spend there). ⛔ Nothing outside
        // this file may debit coins for a finish (HireReinforcementsRegression case 5a).
        private static void SpendCoins(GameStateService svc, GameState state, int price)
        {
            var r = state.Resources;
            int before = r.Coins;
            r.Coins = Mathf.Max(0, r.Coins - price);
            state.Resources = r;
            svc.Save();
            svc.ResourcesChanged?.Invoke();
            DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                $"gold debit for a finish: {before} -> {r.Coins} (-{price}).");
        }

        /// <summary>
        /// LEGACY ADAPTER, kept ONLY because BuildTimerMercenaryRegression pins this signature by
        /// reflection. It is <see cref="TryInstantFinish(ChannelId,string,out string)"/> — nothing
        /// else. The stub this replaced was a SECOND finish mechanism (its own wallet read, its own
        /// coins debit, its own "shrink the timer to ~1s" completion that bypassed CompleteAnyJob and
        /// the JobRushPolicy wall, and it refused QUEUED jobs that ruling Q5 says are finishable).
        /// <paramref name="costGold"/> is NOT honoured: the service prices the hire itself
        /// (<see cref="InstantFinishPrice(ChannelId,string)"/>), so the face and the debit are one
        /// number; a caller-supplied figure that disagrees is traced, not charged.
        /// </summary>
        public bool TryHireMercenaries(BuildJobData job, int costGold, out string failure)
        {
            ChannelId channel = job.ChannelId;
            int quoted = InstantFinishPrice(channel, job.StructureId);
            if (costGold != quoted)
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Obsidian",
                    $"TryHireMercenaries('{job.StructureId}') was handed {costGold} gold but the service " +
                    $"prices the hire at {quoted}; charging the service's price (one mechanism, one number).");
            return TryInstantFinish(channel, job.StructureId, out failure);
        }

        // Apply a time skip by pulling StartMs back by `seconds`; if that finishes it, complete it.
        private void ApplySkipSeconds(ChannelId channel, string structureId, float seconds)
        {
            var ch = GetChannel(channel);
            if (ch == null || seconds <= 0f) return;
            int i = ActiveIndexInChannel(ch, structureId);
            if (i < 0) return;

            var j = ch.ActiveJobs[i];
            if (j.StartMs <= 0) return;             // queued — nothing to skip yet
            j.StartMs -= seconds * 1000.0;          // earlier start → earlier finish
            ch.ActiveJobs[i] = j;

            if (j.FinishMs <= TimeSource.NowUnixMs())
            {
                CompleteChannelJob(channel, structureId);
            }
            else
            {
                Persist();
                JobSkipped?.Invoke(j);
                RaiseQueueChanged();
            }
        }

        // =====================================================================
        //  Completion / cancel
        // =====================================================================

        /// <summary>Force-complete a Builder job now (skips/instant/offline). Removes + applies effect + auto-pulls the next queued job.</summary>
        public void CompleteJob(string structureId) => CompleteChannelJob(ChannelId.Builder, structureId);

        /// <summary>Force-complete a specific job on <paramref name="channel"/> now, then cascade the channel's queue.</summary>
        public void CompleteChannelJob(ChannelId channel, string structureId)
        {
            var ch = GetChannel(channel);
            if (ch == null) return;
            int i = ActiveIndexInChannel(ch, structureId);
            if (i < 0) return;

            var job = ch.ActiveJobs[i];
            ch.ActiveJobs.RemoveAt(i);
            OnJobCompleted(job);

            // The freed slot pulls the next queued job; then resolve any newly-due (cascade).
            ObsidianQueueEngine.PullIntoFreeSlots(ch, SlotCount(channel), TimeSource.NowUnixMs());
            ObsidianQueueEngine.Resolve(ch, SlotCount(channel), TimeSource.NowUnixMs(), OnJobCompleted);
            Persist();
            RaiseQueueChanged();
        }

        /// <summary>
        /// WO-911 (ruling Q5) — force-complete a job on <paramref name="channel"/> whether it is
        /// RUNNING or still QUEUED, then cascade the channel.
        /// -------------------------------------------------------------------------------------
        /// <see cref="CompleteChannelJob"/> is deliberately ACTIVE-ONLY: it scans ActiveJobs and
        /// silently no-ops on a pending id (which is exactly why the zero-duration auto-complete at
        /// the enqueue seams is gated on <c>started</c>). Ruling Q5 requires a player to be able to
        /// pay to finish an item that has not started, so this wrapper lifts the pending job out of
        /// the FIFO first and completes it directly — it does NOT promote it into a slot, because
        /// that would evict FIFO order for the items ahead of it. The items behind it close the gap
        /// through the normal pull, so no hole is left (the same guarantee cancel gives).
        /// </summary>
        public void CompleteAnyJob(ChannelId channel, string structureId)
        {
            var ch = GetChannel(channel);
            if (ch == null) return;

            // ⛔ THE BYPASS WALL (owner ruling 2026-08-16). This method is the paid verb's executor —
            // TryInstantFinish charges crystals and then calls exactly this. It is PUBLIC, so a future
            // seat could write `AddCrystals(-p); CompleteAnyJob(...)` and route around the gate in
            // TryInstantFinish entirely. That would be a GAP; the owner asked for a WALL. Refusing
            // here means the bypass fails loudly instead of quietly reinstating the loot box.
            //
            // ⚠ WHY CompleteChannelJob IS DELIBERATELY *NOT* GATED THE SAME WAY: it is the executor
            // for the AD-SKIP path (ApplySkipSeconds completes through it) and for the offline sweep,
            // and ad-skip on a random outcome is ALLOWED by the same ruling. Gating it would break a
            // sanctioned path. The asymmetry is intentional, not an oversight.
            {
                var gated = FindInChannel(ch, structureId, out bool _);
                if (gated.HasValue &&
                    JobRushPolicy.RefusePaidFinish(gated.Value.JobKind, "CompleteAnyJob",
                                                   structureId, out string _ruled))
                {
                    return;
                }
            }

            if (ActiveIndexInChannel(ch, structureId) >= 0)
            {
                CompleteChannelJob(channel, structureId);
                return;
            }

            int p = PendingIndexInChannel(ch, structureId);
            if (p < 0)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Obsidian",
                    $"CompleteAnyJob('{structureId}' on {channel}) — no such job, active or pending.");
                return;
            }

            var job = ch.PendingQueue[p];
            ch.PendingQueue.RemoveAt(p);            // the rest shift up; pending jobs hold no slot
            DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                $"Complete Now on a QUEUED job '{structureId}' ({channel}) — lifted from position {p} of the line.");
            OnJobCompleted(job);

            ObsidianQueueEngine.PullIntoFreeSlots(ch, SlotCount(channel), TimeSource.NowUnixMs());
            ObsidianQueueEngine.Resolve(ch, SlotCount(channel), TimeSource.NowUnixMs(), OnJobCompleted);
            Persist();
            RaiseQueueChanged();
        }

        // The ONE completion seam — live expiry, ad/instant skip AND the offline-fair sweep all
        // route here so the effect lands identically. Order: (1) the F8-51 Upgrade level apply
        // (proven CompletedUpgradeApplier seam) for Upgrade jobs; (2) the IJobEffect registry for
        // the extensible kinds (Repair/TrainTroop/UnlockTier/LearnMagic/…) — a no-op for Build/
        // Upgrade so they never double-apply; (3) the JobCompleted event (UnderConstructionVisual
        // reveal etc.). Guarded so one bad apply logs + never blocks the cascade.
        private void OnJobCompleted(BuildJobData job)
        {
            if (job.Type == BuildJobType.Upgrade && job.TargetTier > 0)
                DeNelle.Core.Diagnostics.Guard.Try("BuildTimer", "apply completed upgrade",
                    () => Buildings.Progression.CompletedUpgradeApplier.Apply(job));

            JobEffectRegistry.Apply(job);
            JobCompleted?.Invoke(job);
        }

        /// <summary>
        /// Re-key an in-flight Builder job (F8-51): a placed structure MOVED mid-timer changes its
        /// cell-derived key. Timer progress is untouched. Handles both active + queued jobs.
        /// </summary>
        public void RepointJob(string oldStructureId, string newStructureId)
        {
            if (string.IsNullOrEmpty(newStructureId) || oldStructureId == newStructureId) return;
            var ch = Builder;
            if (ch == null) return;

            int a = ActiveIndexInChannel(ch, oldStructureId);
            if (a >= 0)
            {
                var j = ch.ActiveJobs[a];
                j.StructureId = newStructureId;
                ch.ActiveJobs[a] = j;
            }
            else
            {
                int p = PendingIndexInChannel(ch, oldStructureId);
                if (p < 0) return;
                var j = ch.PendingQueue[p];
                j.StructureId = newStructureId;
                ch.PendingQueue[p] = j;
            }
            Persist();
            RaiseQueueChanged();
            DeNelle.Core.Diagnostics.FlowTrace.Step("BuildTimer",
                $"job re-keyed '{oldStructureId}' -> '{newStructureId}' (structure moved mid-timer)");
        }

        /// <summary>
        /// Cancel a Builder job WITHOUT completing it (e.g. the player sells the structure
        /// mid-build). Removes it from active OR the pending queue; caller owns any refund.
        /// A cancelled active slot auto-pulls the next queued job.
        /// </summary>
        public bool CancelJob(string structureId) => CancelChannelJob(ChannelId.Builder, structureId);

        /// <summary>Cancel a job on <paramref name="channel"/> without completing it. Auto-pulls the queue on success.</summary>
        public bool CancelChannelJob(ChannelId channel, string structureId)
        {
            var ch = GetChannel(channel);
            if (ch == null) return false;

            int a = ActiveIndexInChannel(ch, structureId);
            if (a >= 0)
            {
                var cancelled = ch.ActiveJobs[a];      // read BEFORE removal — the cancel destroys it
                ch.ActiveJobs.RemoveAt(a);
                ObsidianQueueEngine.PullIntoFreeSlots(ch, SlotCount(channel), TimeSource.NowUnixMs());
                Persist();
                RaiseQueueChanged();
                RaiseJobCancelled(cancelled);
                return true;
            }
            int p = PendingIndexInChannel(ch, structureId);
            if (p >= 0)
            {
                var cancelled = ch.PendingQueue[p];
                ch.PendingQueue.RemoveAt(p);
                Persist();
                RaiseQueueChanged();
                RaiseJobCancelled(cancelled);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Fire <see cref="JobCancelled"/>, Guard-wrapped (§12) so a misbehaving subscriber logs via
        /// FlowTrace.Fail and can never leave the queue half-cancelled. Raised AFTER the removal is
        /// persisted, so a subscriber that grants an item back sees a consistent queue.
        /// </summary>
        private void RaiseJobCancelled(BuildJobData job)
        {
            DeNelle.Core.Diagnostics.Guard.Try("Obsidian",
                $"raise JobCancelled '{job.StructureId}' ({job.JobKind})",
                () => JobCancelled?.Invoke(job));
        }

        // =====================================================================
        //  WO-911 — CANCEL WITH A 100% REFUND (owner ruling Q1)
        // =====================================================================

        /// <summary>
        /// WO-911 (ruling Q1) — cancel ONE job and refund <b>100% of what was paid for it</b>,
        /// FLAT, regardless of how much time has elapsed. An ACTIVE job at 90% refunds exactly the
        /// same as an untouched pending one.
        /// -------------------------------------------------------------------------------------
        /// The owner took the Finish-Now interaction knowingly: yes, a full refund on a nearly-done
        /// job is a free alternative to paying crystals — <b>the player still loses the elapsed
        /// TIME, and the time is the real cost</b>. Do NOT "protect" the sink with a partial refund
        /// and do NOT add an elapsed-time scaling curve.
        ///
        /// The refund is the basket the job CARRIES (v37 <see cref="BuildJobData.Paid"/>), never a
        /// re-derivation: the placement path charges against the LIVE tower count and a first-build
        /// freebie charges nothing, so re-deriving would refund a number the player never paid.
        /// A pre-v37 job carries no basket and refunds ZERO — traced, never silent.
        ///
        /// Credited through <see cref="Ledger.ResourceLedger"/>, which writes the SAME GameState
        /// fields every charge site debits (EconomyService.TrySpend reads/writes those very fields),
        /// and is UNCAPPED — an EconomyService "earned income" grant would silently evaporate
        /// against the town bank cap and eat the refund.
        ///
        /// Cancelling an ACTIVE job frees its slot and the next pending job starts immediately;
        /// cancelling a PENDING one closes the gap. Both are existing engine behaviour.
        /// </summary>
        /// <param name="refunded">What was actually credited back (all-zero on failure or a legacy job).</param>
        /// <returns>True if a job was found and cancelled.</returns>
        public bool CancelChannelJobWithRefund(ChannelId channel, string structureId, out JobCost refunded)
            => CancelChannelJobWithRefund(channel, structureId, out refunded, out _);

        /// <summary>
        /// ECON-SWEEP 2026-08-16 (defect 3) — same cancel, plus the honesty flag the notice needs.
        /// <para>
        /// <paramref name="unrefundedCurrency"/> comes back as a player-readable currency name
        /// ("gold") when the cancelled job was paid for in a currency <see cref="JobCost"/> has no
        /// lane for, and "" otherwise. Research is the only such kind today
        /// (<see cref="JobCurrency.SpendsUnrefundableCoins"/>). WITHOUT this the UI could only see an
        /// all-zero basket and told the player "Nothing to refund." for money that WAS taken. This
        /// changes no refund POLICY -- it only stops the message from lying about it.
        /// </para>
        /// </summary>
        public bool CancelChannelJobWithRefund(ChannelId channel, string structureId, out JobCost refunded,
                                               out string unrefundedCurrency)
        {
            refunded = default;
            unrefundedCurrency = "";
            var ch = GetChannel(channel);
            if (ch == null) return false;

            // Read the basket BEFORE the job is removed — the cancel destroys the record.
            var found = FindInChannel(ch, structureId, out bool wasActive);
            if (!found.HasValue)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Obsidian",
                    $"cancel+refund: no job '{structureId}' on {channel}.");
                return false;
            }
            var paid = found.Value.Paid;
            // ECON-SWEEP 2026-08-16 (defect 3): read the kind BEFORE the cancel destroys the record.
            if (!CancelChannelJob(channel, structureId)) return false;

            if (paid.IsZero)
            {
                // Either a genuinely free build or a pre-v37 save. Both are legitimate; neither is
                // silent (§12) — a zero refund the player did not expect must be explainable.
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Obsidian",
                    $"cancelled '{structureId}' on {channel} with a ZERO refund — the job carries no " +
                    "paid basket (free build, or an in-flight job from a pre-v37 save).");
                return true;
            }

            DeNelle.Core.Diagnostics.Guard.Try("Obsidian", "refund cancelled job", () =>
            {
                // One atomic wallet mutation followed by ONE authoritative save. CancelChannelJob
                // persisted the queue removal already; this later save must contain the complete
                // material + Gold refund or a reload can resurrect the pre-refund wallet.
                var service = GameStateService.Instance;
                var state = service?.State;
                if (state == null) return;
                state.Wood += paid.Wood;
                state.Iron += paid.Iron;
                state.Magic += paid.Magic;
                var wallet = state.Resources;
                wallet.Food += paid.Food;
                wallet.Crystals += paid.Crystals;
                wallet.Coins += paid.Coins;
                state.Resources = wallet;
                service.Save();
                service.ResourcesChanged?.Invoke();
            });

            refunded = paid;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                $"cancelled {(wasActive ? "ACTIVE" : "queued")} '{structureId}' on {channel} — " +
                $"refunded 100% ({paid.Describe()}).");
            return true;
        }

        /// <summary>
        /// Reorder a PENDING job to <paramref name="index"/> within its channel's FIFO (drag-reorder).
        /// No-op for a running job or an out-of-range move. Persists + raises QueueChanged.
        /// </summary>
        public bool ReorderPending(ChannelId channel, string targetId, int index)
        {
            var ch = GetChannel(channel);
            if (ch == null) return false;
            int p = PendingIndexInChannel(ch, targetId);
            if (p < 0) return false;
            index = Mathf.Clamp(index, 0, ch.PendingQueue.Count - 1);
            if (index == p) return true;
            var job = ch.PendingQueue[p];
            ch.PendingQueue.RemoveAt(p);
            ch.PendingQueue.Insert(index, job);
            Persist();
            RaiseQueueChanged();
            return true;
        }

        // Sweep EVERY channel: complete due jobs + cascade pending pulls (open OR offline).
        private void SweepAllChannels()
        {
            var q = Queue;
            if (q == null || q.Channels == null) return;
            double now = TimeSource.NowUnixMs();
            int totalCompleted = 0;
            // Copy the keys — Resolve mutates the channel lists + fires listeners that may re-enter.
            var ids = new List<ChannelId>(q.Channels.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                var ch = q.Channel(ids[i]);
                totalCompleted += ObsidianQueueEngine.Resolve(ch, SlotCount(ids[i]), now, OnJobCompleted);
            }
            if (totalCompleted > 0)
            {
                Persist();
                RaiseQueueChanged();
            }

            // WO-1388 - the DEFERRED pack crew starts the moment the running window ends. This is
            // the one pickup for a deferral (and for a token that landed with no live service):
            // the sweep already runs on load and at 1 Hz, so no second timer is introduced.
            // No-op in the common case (nothing owed); the redeemer traces every real transition.
            DeNelle.Core.Diagnostics.Guard.Try("TempBuilder", "sweep redeem pass",
                () => ConvenienceRedeemer.TryRedeemTemporaryBuilder());
        }

        // =====================================================================
        //  Ad-skip cap - a ROLLING WINDOW anchored on first use (owner, 2026-08-06)
        // =====================================================================
        //  Was a calendar-day cap. Her ruling: "when the first ad comes in, we mark a
        //  timer, and that's their four hour rolling from there."
        //
        //  WHY FIXED-FROM-FIRST-USE AND NOT A SLIDING WINDOW - do not "improve" this:
        //  a sliding window drips the allowance back one at a time, so a free player
        //  limps along indefinitely. This one lets them burn the allowance and then hit
        //  a HARD WALL AT ZERO for the rest of the window. The wall IS the conversion
        //  trigger - the cap exists to create the spend moment, not to limit ad revenue.
        //  A day-reset was also worse for a different reason: it punishes an evening
        //  player who spent their allowance that morning. Rolling always offers a way back.
        //
        //  NO SCHEMA BUMP. The two persisted fields already existed (v13/WO-172) and only
        //  change MEANING: AdSkipDayKey now holds the window-start instant (round-trip
        //  "o" format), AdSkipsUsedToday the count within it. The names are now misleading
        //  and worth renaming on the next schema touch.
        //
        //  ⭐ RESOLVED - the "KNOWN LIMIT" that stood here is FIXED and the warning was stale.
        //  It read: "the window start is a DEVICE clock (UtcNow) ... deliberately not solved
        //  here because no ad SDK is wired yet." Both halves are now false. WO-912 §7.1 landed:
        //  RecordAdSkipUsed stamps TimeSource.NowUnixMs() (server-anchored when a handshake has
        //  happened this process), NOT DateTime.UtcNow - there is no DateTime.UtcNow left in this
        //  file - and the ad SDK IS wired (LevelPlay 9.5.1, account approved 2026-08-24, flag ON).
        //
        //  ⛔ THE REASON THE WARNING EXISTED STILL GOVERNS, which is why it is rewritten rather
        //  than deleted: a device-clock window lets a player roll the phone forward and mint a
        //  fresh allowance, and once a real SDK is behind it that is FABRICATED IMPRESSIONS
        //  against a live ad account - what networks ban for. There is now a live approved
        //  account to lose. Never re-stamp this window from a device clock.
        //
        //  (Deleted 2026-08-24. It had already been called out as stale in FeatureFlags.cs's
        //  RewardedAdSkip block, and left standing anyway - a warning the repo knew was wrong.)

        private bool UnderDailyAdCap()
        {
            int cap = Config.adSkipsPerWindow;
            if (cap <= 0) return true;              // 0 = unlimited
            RollWindowIfNeeded();
            var state = State;
            return state != null && state.AdSkipsUsedToday < cap;
        }

        private void RecordAdSkipUsed()
        {
            RollWindowIfNeeded();
            var state = State;
            if (state == null) return;
            // The FIRST watch of a window is what starts the clock, so stamp it here
            // rather than on the check - merely opening a screen must not burn the window.
            //
            // WO-912 §7.1: the stamp is unix-ms from TimeSource (server-anchored when a
            // handshake has happened this process), NOT DateTime.UtcNow. Stamping from the
            // device clock is what let a player roll the phone forward and mint a fresh
            // allowance - i.e. fabricated impressions against a live ad account.
            // The field stays a string and the SCHEMA DOES NOT BUMP: a unix-ms number
            // round-trips through it fine, and RollWindowIfNeeded parses both this shape
            // and the legacy ISO/day-key shapes.
            if (string.IsNullOrEmpty(state.AdSkipDayKey))
            {
                state.AdSkipDayKey = TimeSource.NowUnixMs().ToString("F0", CultureInfo.InvariantCulture);
                DeNelle.Core.Diagnostics.FlowTrace.Step("Obsidian",
                    $"ad-skip window OPENED - serverAnchored={TimeSource.IsServerAnchored}. " +
                    "An unanchored open is legitimate (offline//fresh launch) and is reconciled " +
                    "by the server on the next save round trip.");
            }
            state.AdSkipsUsedToday++;
            Persist();
        }

        private void RollWindowIfNeeded()
        {
            var state = State;
            if (state == null) return;

            float windowSeconds = Config.adSkipWindowSeconds;
            if (windowSeconds <= 0f) return;        // no window configured => never rolls

            if (string.IsNullOrEmpty(state.AdSkipDayKey)) return;   // no window open yet

            // WO-912: the stamp is unix-ms as of this WO. Two legacy shapes still exist in
            // the wild and BOTH must keep loading - a save is not re-writable on demand:
            //   * ISO round-trip ("o") written between v13 and WO-912
            //   * "yyyy-MM-dd" device-local day key from the original WO-172 build
            double startMs;
            if (!double.TryParse(state.AdSkipDayKey,
                                 NumberStyles.Float, CultureInfo.InvariantCulture, out startMs))
            {
                DateTime legacyStart;
                if (DateTime.TryParse(state.AdSkipDayKey,
                                      CultureInfo.InvariantCulture,
                                      DateTimeStyles.RoundtripKind,
                                      out legacyStart))
                {
                    startMs = new DateTimeOffset(legacyStart.ToUniversalTime()).ToUnixTimeMilliseconds();
                }
                else
                {
                    // Anything unparseable: treat as a closed window rather than trusting it.
                    // The player gets a fresh allowance once, on upgrade - the forgiving
                    // direction, and the one that cannot strand someone at zero forever.
                    state.AdSkipDayKey = null;
                    state.AdSkipsUsedToday = 0;
                    return;
                }
            }

            double elapsed = (TimeSource.NowUnixMs() - startMs) / 1000d;
            // A NEGATIVE elapsed means the clock moved backwards since the stamp - treat it
            // as tampering and close the window rather than leaving it open forever.
            //
            // WO-912 §7.3 - REFUSE, DON'T PUNISH. Closing the window costs the player at
            // most one allowance and self-heals; we never wipe state, flag an account, or
            // tell the player they were caught. A false positive here is ordinary life
            // (timezone change, DST, a dead coin cell, someone correcting a wrong clock),
            // and punishing that would break a paying player's save for nothing. It is also
            // why we do not teach an attacker what the detector measures.
            if (elapsed < 0d)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Obsidian",
                    $"ad-skip window CLOSED EARLY - clock moved BACKWARDS {(-elapsed):F0}s since the " +
                    $"stamp (serverAnchored={TimeSource.IsServerAnchored}). Refusing the window, not " +
                    "punishing the save. A rising rate here is the signal to move the window fully " +
                    "server-side (WO-912 §7.2 defence 1).");
            }

            if (elapsed < 0d || elapsed >= windowSeconds)
            {
                state.AdSkipDayKey = null;
                state.AdSkipsUsedToday = 0;
            }
        }

        // =====================================================================
        //  Internals
        // =====================================================================

        private static GameState State => GameStateService.Instance != null ? GameStateService.Instance.State : null;

        private void RaiseQueueChanged()
        {
            DeNelle.Core.Diagnostics.Guard.Try("Obsidian", "raise QueueChanged", () => QueueChanged?.Invoke());
            PublishStatus();   // WO-778: chip snapshot tracks every queue mutation
        }

        // WO-778: presentation-ready summary for the persistent HUD chip (Core seam).
        // Remaining time is computed HERE (TimeSource is Village-side) so the HUD never
        // clocks; the HUD polls DeNelle.Core.UI.ObsidianQueueGate.Status only.
        private void PublishStatus()
        {
            var s = new DeNelle.Core.UI.ObsidianQueueGate.WorkQueueStatus();
            if (State != null)
            {
                s.Available = true;
                double now = TimeSource.NowUnixMs();
                double soonest = double.MaxValue;

                void Fill(ChannelId id, out int busy, out int slots, out int queued)
                {
                    var act = ActiveJobsOf(id);
                    busy = act.Count;
                    slots = SlotCount(id);
                    queued = PendingJobsOf(id).Count;
                    for (int i = 0; i < act.Count; i++)
                        if (act[i].StartMs > 0 && act[i].FinishMs < soonest)
                            soonest = act[i].FinishMs;
                }

                Fill(ChannelId.Builder,  out s.BuilderBusy,  out s.BuilderSlots,  out s.BuilderQueued);
                Fill(ChannelId.Train,    out s.TrainBusy,    out s.TrainSlots,    out s.TrainQueued);
                Fill(ChannelId.Research, out s.ResearchBusy, out s.ResearchSlots, out s.ResearchQueued);

                s.SoonestRemainingSec = soonest == double.MaxValue
                    ? -1
                    : (int)System.Math.Max(0.0, (soonest - now) / 1000.0);

                // CARD-RAIL VIEW (WO-864; supersedes the WO-778 text rows): every channel
                // publishes its jobs active-first, then the waiting line in order. The
                // extra card fields (verb / icon / stack) are resolved HERE, once per
                // publish, so the always-on HUD rail, the Work Queue modal and any future
                // Manage screen render the SAME card from the SAME data and can never
                // disagree. Still pure presentation marshalling — no timer/economy logic.
                s.Entries = BuildEntries(ChannelId.Builder, now);
                s.TrainEntries = BuildEntries(ChannelId.Train, now);
                s.ResearchEntries = BuildEntries(ChannelId.Research, now);
            }
            DeNelle.Core.UI.ObsidianQueueGate.PublishStatus(s);
            PublishArmyStatus();
        }

        // WO-864: one channel's card list — ACTIVE jobs first (each owns a slot and a live
        // countdown), then the FIFO waiting line. Identical PENDING troop trains collapse to
        // ONE card carrying an "xN" badge (the CoC "Barbarian x5" read) instead of N cards
        // that say the same word. Active jobs never collapse — each is a real running slot.
        // FREE slots are NOT published here: the view derives them from SlotCount, so a
        // channel that grows a slot needs no publisher change.
        private const int MaxPublishedCards = 24;   // sanity bound; the rail tails the rest

        private System.Collections.Generic.List<DeNelle.Core.UI.ObsidianQueueGate.QueueEntry> _entryBuf
            = new System.Collections.Generic.List<DeNelle.Core.UI.ObsidianQueueGate.QueueEntry>();

        private DeNelle.Core.UI.ObsidianQueueGate.QueueEntry[] BuildEntries(ChannelId channel, double now)
        {
            var act = ActiveJobsOf(channel);
            var pend = PendingJobsOf(channel);
            _entryBuf.Clear();

            for (int i = 0; i < act.Count && _entryBuf.Count < MaxPublishedCards; i++)
            {
                var e = MakeEntry(act[i]);
                e.RemainingSec = (int)System.Math.Max(0.0, (act[i].FinishMs - now) / 1000.0);
                e.Queued = false;
                _entryBuf.Add(e);
            }

            int firstPending = _entryBuf.Count;
            for (int i = 0; i < pend.Count && _entryBuf.Count < MaxPublishedCards; i++)
            {
                var e = MakeEntry(pend[i]);
                e.RemainingSec = -1;
                e.Queued = true;

                // Collapse into an earlier PENDING card of the same stack key, if any.
                string key = StackKey(pend[i].StructureId);
                int merged = -1;
                if (key != null)
                    for (int j = firstPending; j < _entryBuf.Count; j++)
                        if (string.Equals(StackKey(_entryBuf[j].JobId), key, System.StringComparison.Ordinal))
                        { merged = j; break; }

                if (merged >= 0)
                {
                    var m = _entryBuf[merged];
                    m.StackCount++;
                    _entryBuf[merged] = m;
                }
                else _entryBuf.Add(e);
            }

            return _entryBuf.ToArray();
        }

        // A card record for one job: player-facing name + the VERB (owner ruling 2026-08-03 —
        // the card is built on the verb; art is the enhancement) + the icon route.
        /// <summary>
        /// The presentation-ready card shape for a job (label, verb, icon keys, tier). PUBLIC so
        /// the Manage screen's rows resolve their icon through the SAME path as the card rail -
        /// two independent icon lookups would eventually disagree about what a job looks like,
        /// which is the class of bug the shared QueueRailView already exists to prevent.
        /// </summary>
        public static DeNelle.Core.UI.ObsidianQueueGate.QueueEntry EntryFor(BuildJobData job) => MakeEntry(job);

        private static DeNelle.Core.UI.ObsidianQueueGate.QueueEntry MakeEntry(BuildJobData job)
        {
            string label = ObsidianQueueHud.FormatJobTarget(job);
            if (string.IsNullOrEmpty(label)) label = PrettyJobLabel(job.StructureId);
            // The stack badge carries the count, so drop the "x1" the label suffixes onto trains.
            if (label.EndsWith(" x1", System.StringComparison.Ordinal))
                label = label.Substring(0, label.Length - 3);

            var e = new DeNelle.Core.UI.ObsidianQueueGate.QueueEntry
            {
                Label = label,
                Verb = CardVerb(job.JobKind),
                JobId = job.StructureId,
                TargetTier = job.TargetTier,
                StackCount = 1,
                Free = false,
            };

            // WO-1294: troop IconId is the canonical portrait filename under RpgUi/troop.
            string troopId = TroopIdOfJob(job);
            if (!string.IsNullOrEmpty(troopId))
            {
                var def = TroopCatalog.Find(troopId);
                if (def != null && !string.IsNullOrEmpty(def.IconId))
                {
                    e.IconRole = DeNelle.Core.UI.RpgUiCatalog.RoleTroop;
                    e.IconKey = def.IconId;
                }
            }
            return e;
        }

        /// <summary>ASCII uppercase card verb. Never a raw enum name.</summary>
        private static string CardVerb(JobKind kind)
        {
            switch (kind)
            {
                case JobKind.Build:
                case JobKind.TowerBuild:      return "BUILD";
                case JobKind.Repair:          return "REPAIR";
                case JobKind.TrainTroop:      return "TRAIN";
                case JobKind.UnlockTier:      return "UNLOCK";
                case JobKind.LearnMagic:      return "LEARN";
                case JobKind.Upgrade:
                case JobKind.TowerUpgrade:
                case JobKind.WallUpgrade:
                case JobKind.BarracksUpgrade:
                case JobKind.TroopUpgrade:    return "UPGRADE";
                default:                      return "WORK";
            }
        }

        // The troop a job targets ("barracks-train:troop-footman:9f2c41ab" -> "troop-footman"),
        // or null when the job is not troop-shaped.
        private static string TroopIdOfJob(BuildJobData job)
        {
            string id = job.StructureId ?? "";
            if (id.StartsWith(BarracksService.TrainPrefix, System.StringComparison.Ordinal))
            {
                var parts = id.Split(':');
                return parts.Length >= 2 ? parts[1] : null;
            }
            if (id.StartsWith(BarracksService.TroopUpgradePrefix, System.StringComparison.Ordinal))
                return id.Substring(BarracksService.TroopUpgradePrefix.Length);
            return null;
        }

        // Jobs that are INTERCHANGEABLE collapse onto one card. Only troop trains qualify:
        // every structure job is unique by its "@cell" suffix, so collapsing those would hide
        // real work. Returns null for anything that must stay its own card.
        private static string StackKey(string structureId)
        {
            if (string.IsNullOrEmpty(structureId)) return null;
            if (!structureId.StartsWith(BarracksService.TrainPrefix, System.StringComparison.Ordinal)) return null;
            var parts = structureId.Split(':');
            return parts.Length >= 2 ? (parts[0] + ":" + parts[1]) : null;
        }

        // Full-army gate (owner ruling: HUD Raids button greys unless the army is full
        // counting ready + queued troops): publish the Raids-button army snapshot on the
        // SAME cadence as the chip snapshot (QueueChanged edges + the 1s heartbeat).
        // RaidEntryGate bumps its Version only on a value change, so the 1 Hz republish
        // is repaint-free for the HUD. The MATH lives in ArmyReadiness.Compute — the one
        // readiness formula (owner review 2026-08-01); this method only relays it to the
        // Core seam. Null GameState/Army -> Compute returns READY so headless/AutoPilot
        // never false-dims (mirrors RaidSelectionScreen's bypass).
        private void PublishArmyStatus()
        {
            var s = ArmyReadiness.Compute(State);
            // WO-1407: RequiredSlots rides along (the WO-823 soft-gate bar, 3 or the cap) so the
            // Heart plate's "Train N troops to unlock Raids" names the SAME number this gate
            // judged Ready against - HeartObjectiveCopy.Resolve reads it off the seam.
            // WO-1641: PastFirstRaid rides the SAME relay so the plate can say what falling short
            // MEANS - "unlock Raids" before the first raid, "for the next raid" after it. It is a
            // WORDING input only; Ready is still the whole door decision, made in ArmyReadiness.
            DeNelle.Core.UI.RaidEntryGate.PublishArmyStatus(
                s.Ready, s.DeployableSlots, s.QueuedSlots, s.CapSlots, s.RequiredSlots, s.PastFirstRaid);

            // WO-1389 pressure point 6 - "Army N / M" for the Journey Raids card subtitle rides
            // the SAME relay, on the SAME cadence, off the SAME snapshot: this method is the one
            // readiness relay that may read ArmyReadiness (RaidsDiscoverabilityRegression D5
            // forbids it in RaidCapabilityHudBridge - readiness decides DIM and REFUSAL, never
            // visibility - which is where this producer first landed and was moved from).
            // RosterSlots counts every roster body incl. wounded (the "3" a player sees in
            // Manage), CapSlots is the army cap. PostureSignals.SetArmyFill is change-only, so
            // the 1 Hz republish is repaint-free. Null State/Army -> Compute's headless READY
            // snapshot carries (0, 0); the Journey capture publishes its explicit 0/10 fixture
            // so visual evidence never mistakes the pre-producer sentinel for a real cap.
            DeNelle.Core.Diagnostics.Guard.Try("HudKit", "publish army fill", () =>
                DeNelle.Core.HudModel.PostureSignals.SetArmyFill(s.RosterSlots, s.CapSlots));

            // WO-1404 - project the SAME raid-card set as RaidSelectionVM. This stays in Village
            // because HUD may only read Core seams; the Journey card consumes the change-only
            // count beside ArmyFill. The open-camp <= predicate itself is newly authored below:
            // at this base RaidSelectionVM exposes GarrisonCount and IsLocked, but no open-count.
            DeNelle.Core.Diagnostics.Guard.Try("HudKit", "publish open raid camps", () =>
                PublishJourneyOpenCamps(State));
        }

        /// <summary>
        /// Rebuilds the traced raid projection only when its victory/catalog/scene-probe inputs
        /// change, and recounts only when that projection or the deployable-body count changes.
        /// The one-second heartbeat therefore performs cheap scalar comparisons without creating
        /// a RaidSelectionVM (whose constructor intentionally traces its projection) every tick.
        /// </summary>
        private void PublishJourneyOpenCamps(GameState state)
        {
            int deployableBodies = 0;
            if (state != null && state.Army != null)
                foreach (var troop in state.Army.GetDeployable())
                    if (troop != null) deployableBodies++;

            int victories = state != null ? Math.Max(0, state.RaidVictories) : 0;
            int catalogInput = JourneyRaidCatalogInput();
            var sceneProbe = DeNelle.Village.Hero.RaidSelectionVM.SceneAvailableProvider ??
                JourneyRaidSceneProbeFallback;
            bool projectionChanged = _journeyRaidProjection == null ||
                _journeyRaidVictoryInput != victories ||
                _journeyRaidCatalogInput != catalogInput ||
                !ReferenceEquals(_journeyRaidSceneProbeInput, sceneProbe);

            if (projectionChanged)
            {
                if (_journeyRaidProjection != null) _journeyRaidProjection.Dispose();

                // CreateDefault is the sole flagship/fallback catalog resolver. Its temporary
                // projection is used only to recover that exact def set; the retained projection
                // receives the authoritative persisted victory count explicitly.
                var defs = new List<SceneConfigDef>();
                using (var resolved = DeNelle.Village.Hero.RaidSelectionVM.CreateDefault())
                {
                    for (int i = 0; i < resolved.Raids.Count; i++)
                    {
                        var def = resolved.DefFor(resolved.Raids[i].Id);
                        if (def != null) defs.Add(def);
                    }
                }
                _journeyRaidProjection = new DeNelle.Village.Hero.RaidSelectionVM(
                    defs, null, victories, sceneProbe);
                _journeyRaidVictoryInput = victories;
                _journeyRaidCatalogInput = catalogInput;
                _journeyRaidSceneProbeInput = sceneProbe;
            }

            if (!projectionChanged && _journeyRaidDeployableInput == deployableBodies) return;
            _journeyRaidDeployableInput = deployableBodies;

            int openCamps = 0;
            // WO-1541: the NEXT camp, picked in this SAME walk. See the publish below for why it
            // rides here and not in a second loop of its own.
            SceneConfigDef nextCamp = null;
            for (int i = 0; i < _journeyRaidProjection.Raids.Count; i++)
            {
                string id = _journeyRaidProjection.Raids[i].Id;
                if (_journeyRaidProjection.IsLocked(id)) continue;
                var def = _journeyRaidProjection.DefFor(id);
                // WO-1404: NEW open predicate at this base; do not attribute it to RaidSelectionVM.
                if (def != null && DeNelle.Village.Hero.RaidSelectionVM.GarrisonCount(def) <= deployableBodies)
                    openCamps++;
                // WO-1541: the NEXT camp is the cheapest UNLOCKED one to reach - lowest
                // unlockVictories - and it is deliberately NOT filtered by the open predicate
                // above. The two facts answer different questions and must not be merged:
                //   RaidOpenCampCount = "how many can you take RIGHT NOW" (the Journey deck)
                //   RaidNextCamp      = "who are you training FOR"        (the Manage/ARMY line)
                // A camp you cannot yet field an army against is precisely the one that motivates
                // training, so gating the name on <= deployableBodies would blank the sentence in
                // the exact state it exists to serve.
                if (def != null && (nextCamp == null || def.unlockVictories < nextCamp.unlockVictories))
                    nextCamp = def;
            }
            DeNelle.Core.HudModel.PostureSignals.SetRaidOpenCampCount(openCamps);

            // WO-1541 - THE ONE PRODUCER OF "WHICH CAMP IS NEXT".
            // ManageScreenVM.BuildTroopArmySummary used to build its OWN RaidSelectionVM over
            // SceneConfigCatalog.All-where-IsEnemy and run this same minimum-unlockVictories walk
            // a second time. Two derivations, two separator styles, one drift waiting to happen.
            // The walk now lives here, beside the count it belongs with, on the SAME relay and the
            // SAME cadence, and Manage READS the result. ⛔ Do not re-derive a camp name anywhere
            // else - the oracle in ManageTroopsTrainDoorRegression fails the build if you do.
            //
            // ⚠ SET DIFFERENCE, RECORDED RATHER THAN ASSUMED: this projection's defs come from
            // RaidSelectionVM.CreateDefault (the sole flagship/fallback resolver, above), whereas
            // the retired Manage walk filtered SceneConfigCatalog.All on IsEnemy. Where the two
            // sets disagree the CreateDefault set now wins, because it is the set the raid GRID
            // itself offers - naming a camp the grid does not list would be the worse bug.
            DeNelle.Core.Diagnostics.Guard.Try("HudKit", "publish next raid camp", () =>
            {
                if (nextCamp == null)
                {
                    DeNelle.Core.HudModel.PostureSignals.SetRaidNextCamp(null, 0);
                    return;
                }
                string campName = !string.IsNullOrEmpty(nextCamp.displayName) ? nextCamp.displayName : nextCamp.id;
                DeNelle.Core.HudModel.PostureSignals.SetRaidNextCamp(
                    campName, DeNelle.Village.Hero.RaidSelectionVM.GarrisonCount(nextCamp));
            });
        }

        /// <summary>Stable fingerprint of every catalog field that can change the resolved raid
        /// set, its escalation lock, its scene lock, or its garrison threshold.</summary>
        private static int JourneyRaidCatalogInput()
        {
            unchecked
            {
                int hash = 17;
                var all = SceneConfigCatalog.All;
                hash = hash * 31 + all.Count;
                for (int i = 0; i < all.Count; i++)
                {
                    var def = all[i];
                    if (def == null) { hash *= 31; continue; }
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(def.id ?? "");
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(def.sceneName ?? "");
                    hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(def.ownership ?? "");
                    hash = hash * 31 + def.unlockVictories;
                    hash = hash * 31 + DeNelle.Village.Hero.RaidSelectionVM.GarrisonCount(def);
                }
                return hash;
            }
        }

        // Player-facing label for a queue row: strip the placement suffix ("@15_7"), then
        // title-case the id's tokens ("tower_arcane_spire" -> "Tower Arcane Spire",
        // "barracks-upgrade" -> "Barracks Upgrade"). Pure string work — no catalog lookup,
        // so the chip stays Core-safe and never blocks on data readiness.
        private static string PrettyJobLabel(string structureId)
        {
            if (string.IsNullOrEmpty(structureId)) return "Job";
            string id = structureId;
            int at = id.IndexOf('@');
            if (at > 0) id = id.Substring(0, at);
            var parts = id.Split('-', '_', ':');
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1));
            }
            return sb.Length > 0 ? sb.ToString() : "Job";
        }

        // Index of a job (active OR pending) in a channel by structure id; -1 if none.
        private static int IndexInChannel(ChannelState ch, string id)
        {
            if (ActiveIndexInChannel(ch, id) >= 0) return 0;
            if (PendingIndexInChannel(ch, id) >= 0) return 0;
            return -1;
        }

        private static int ActiveIndexInChannel(ChannelState ch, string id)
        {
            if (ch == null || ch.ActiveJobs == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < ch.ActiveJobs.Count; i++)
                if (ch.ActiveJobs[i].StructureId == id) return i;
            return -1;
        }

        private static int PendingIndexInChannel(ChannelState ch, string id)
        {
            if (ch == null || ch.PendingQueue == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < ch.PendingQueue.Count; i++)
                if (ch.PendingQueue[i].StructureId == id) return i;
            return -1;
        }

        // Find a job (active first, then pending) in a channel; isActive reports which list.
        private static BuildJobData? FindInChannel(ChannelState ch, string id, out bool isActive)
        {
            isActive = false;
            int a = ActiveIndexInChannel(ch, id);
            if (a >= 0) { isActive = true; return ch.ActiveJobs[a]; }
            int p = PendingIndexInChannel(ch, id);
            if (p >= 0) return ch.PendingQueue[p];
            return null;
        }

        private static void Persist() => GameStateService.Instance?.Save();

        private static BuildTimerConfig LoadConfig()
        {
            var cfg = Resources.Load<BuildTimerConfig>(BuildTimerConfig.ResourcesPath);
            return cfg != null ? cfg : BuildTimerConfig.CreateDefault();
        }
    }
}
