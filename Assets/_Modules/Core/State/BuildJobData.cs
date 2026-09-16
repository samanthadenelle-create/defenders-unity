// =============================================================================
// BuildJobData — one in-flight construction/upgrade timer (WO-172). Pure data.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.State
//
// A CoC-style timed construction job: placing a building or buying an upgrade
// enqueues one of these, and the structure completes at StartMs + DurationMs.
// PERSISTED in GameState.BuildJobs so the timer counts down in REAL time across
// app close / offline — exactly like the WO-115 offline-harvest clock. The clock
// is unix-ms (TimeSource.NowUnixMs), the same unit as GameState.LastInboxSyncAt /
// LastHarvestClaimMs and PendingTowerBuild.FinishAt, so "remaining" is a plain
// subtraction with no per-frame state.
//
// DESIGN: keyed by StructureId (the player-placed structure / catalog instance the
// job belongs to). WO-108 (player build mode, NOT built yet) will mint a unique id
// per placed structure (PlacedStructureData.itemId + a cell key) and pass it here;
// WO-151 upgrades reuse the same id for the existing structure. The id is opaque to
// this layer — it only ticks the clock and reports done. See BuildTimerService for
// the start/remaining/skip API and the §"WO-108 integration point" note there.
//
// This struct mirrors PendingTowerBuild's shape + persistence convention (a small
// [Serializable] struct in a List<> that round-trips through SaveSchema). It is
// SEPARATE from PendingTowerBuild (an in-session pet-assisted tower build) and from
// TowerConstruction (a seconds-long in-session visual raise) — those are not
// persisted CoC timers; this is the durable, offline-counting one.
// =============================================================================

using System;
using Newtonsoft.Json;
using DeNelle.Core.Jobs;

namespace DeNelle.Core.State
{
    /// <summary>What kind of work a <see cref="BuildJobData"/> represents.</summary>
    public enum BuildJobType
    {
        /// <summary>A brand-new structure being constructed (WO-108 placement).</summary>
        Build = 0,
        /// <summary>An existing structure being upgraded to the next tier (WO-151).</summary>
        Upgrade = 1,
    }

    /// <summary>
    /// WO-911 (M2) — the resource basket a job was CHARGED at commit, carried on the job so a
    /// cancel can refund exactly 100% of it (owner ruling Q1). Deliberately a plain 5-int value
    /// in <c>DeNelle.Core</c>: the tree has three different <c>ResourceCost</c> types
    /// (Core.Catalog, Village, Village.Ledger) and the job layer must not depend on any of them.
    /// All-zero means "nothing was charged" (a free build, or a pre-v37 save).
    /// </summary>
    [Serializable]
    public struct JobCost
    {
        /// <summary>Wood charged.</summary>
        public int Wood;
        /// <summary>Stone charged.</summary>
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("Food")] public int Stone;
        /// <summary>Iron charged.</summary>
        public int Iron;
        /// <summary>Crystals charged.</summary>
        public int Crystals;
        /// <summary>Magic/tech points charged (the TrySpendWithMagic sites).</summary>
        public int Magic;
        /// <summary>Coins/Gold charged.</summary>
        public int Coins;

        /// <summary>Build a paid basket.</summary>
        public JobCost(int wood, int stone, int iron, int crystals, int magic = 0, int coins = 0)
        {
            Wood = wood;
            Stone = stone;
            Iron = iron;
            Crystals = crystals;
            Magic = magic;
            Coins = coins;
        }

        /// <summary>True when nothing was charged (free build, or a pre-v37 job).</summary>
        public bool IsZero => Wood == 0 && Stone == 0 && Iron == 0 && Crystals == 0 && Magic == 0 && Coins == 0;

        /// <summary>ASCII, player-readable summary ("400 wood, 200 food"); "nothing" when zero.</summary>
        public string Describe()
        {
            if (IsZero) return "nothing";
            var sb = new System.Text.StringBuilder();
            if (Wood > 0) sb.Append(Wood).Append(" wood");
            if (Stone > 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(Stone).Append(" stone"); }
            if (Iron > 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(Iron).Append(" iron"); }
            if (Crystals > 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(Crystals).Append(" crystals"); }
            if (Magic > 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(Magic).Append(" magic"); }
            if (Coins > 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(Coins).Append(" gold"); }
            return sb.ToString();
        }
    }

    /// <summary>
    /// One persisted, real-time construction/upgrade timer. Completes at
    /// <see cref="FinishMs"/> = <see cref="StartMs"/> + <see cref="DurationMs"/>.
    /// Counts down offline because the clock is wall-clock unix-ms, not frame time.
    /// </summary>
    [Serializable]
    public struct BuildJobData
    {
        /// <summary>
        /// Opaque id of the structure this job builds/upgrades. WO-108 mints it per
        /// placed structure; WO-151 reuses the structure's id for its upgrade. One
        /// in-flight job per id (BuildTimerService enforces).
        /// </summary>
        [JsonProperty("structureId")] public string StructureId;

        /// <summary>Build vs upgrade — drives the duration curve + completion behaviour.</summary>
        [JsonProperty("jobType")] public int JobType;

        /// <summary>
        /// WO-773 — the richer job kind (Build/Upgrade/Repair/TrainTroop/UnlockTier/LearnMagic/
        /// Tower*/Wall*). This is the "ObsidianJob" kind axis: the completion router uses it to
        /// pick the IJobEffect handler. Additive default-on-read: absent on a pre-WO-773 (v34)
        /// save → 0 = Build; the v34→v35 migration backfills it from <see cref="JobType"/> (an
        /// Upgrade JobType becomes JobKind.Upgrade). See <see cref="JobKind"/>.
        /// </summary>
        [JsonProperty("kind")] public int Kind;

        /// <summary>
        /// WO-773 — the channel this job runs on (Builder/Train/Research). Channels never share
        /// slots, so a training job and a build job run in parallel. Additive default-on-read:
        /// absent → 0 = Builder (the legacy build/upgrade channel); the v34→v35 migration stamps
        /// folded legacy jobs onto the Builder channel. See <see cref="ChannelId"/>.
        /// </summary>
        [JsonProperty("channel")] public int Channel;

        /// <summary>Unix-ms the job started (TimeSource.NowUnixMs at enqueue).</summary>
        [JsonProperty("startMs")] public double StartMs;

        /// <summary>Total job duration in ms. May be reduced by ad-skips / instant-finish (StartMs is pulled back).</summary>
        [JsonProperty("durationMs")] public double DurationMs;

        /// <summary>
        /// F8-51: the LEVEL/TIER this job applies when it completes. For an Upgrade job this is
        /// the target level the structure/building reaches at completion (costs were charged at
        /// commit; the level applies at the completion seam — never before). 0 for Build jobs and
        /// for pre-F8-51 saves (JSON default), which the completion router treats as "nothing to
        /// apply" — additive, back-compatible.
        /// </summary>
        [JsonProperty("targetTier")] public int TargetTier;

        /// <summary>Owned-job instant-build charge, refundable if the unfinished job is cancelled. Old jobs default null.</summary>
        [JsonProperty("instantBuildTokenKey", NullValueHandling = NullValueHandling.Ignore)] public string InstantBuildTokenKey;

        // ─────────────────────────────────────────────────────────────────────
        //  WO-911 (M2) — THE PAID BASKET. Save schema v37.
        //  -------------------------------------------------------------------
        //  Cancel refunds 100% of what was PAID (owner ruling Q1, 2026-08-06),
        //  flat, regardless of elapsed time. That is only computable if the job
        //  remembers its own price: re-deriving it at cancel time is wrong for
        //  BuildModeController.Place (SoftcappedCostFor depends on the LIVE tower
        //  count, and a free-build charged nothing) — the same exploit
        //  TowerPlacementSystem._prepaidCost was introduced to close.
        //
        //  Additive default-on-read: absent on a pre-v37 save → 0, so an
        //  in-flight legacy job cancels cleanly with a ZERO refund. That case is
        //  traced, never silent (see BuildTimerService.CancelChannelJobWithRefund).
        //  Units match the wallet BuildTimerService refunds into
        //  (ResourceLedger.Credit → GameState.Wood/Iron/Resources.Stone/Crystals),
        //  which is the SAME wallet EconomyService.TrySpend debits.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>WO-911 v37 — wood actually charged for this job (0 = free/legacy).</summary>
        [JsonProperty("paidWood")] public int PaidWood;

        /// <summary>WO-911 v37 — food actually charged for this job (0 = free/legacy).</summary>
        [UnityEngine.Serialization.FormerlySerializedAs("PaidFood")]
        [JsonProperty("paidFood")] public int PaidStone;

        /// <summary>WO-911 v37 — iron actually charged for this job (0 = free/legacy).</summary>
        [JsonProperty("paidIron")] public int PaidIron;

        /// <summary>WO-911 v37 — crystals actually charged for this job (0 = free/legacy).</summary>
        [JsonProperty("paidCrystals")] public int PaidCrystals;

        /// <summary>WO-911 v37 — magic/tech points charged (ResourceLedger.TrySpendWithMagic sites).</summary>
        [JsonProperty("paidMagic")] public int PaidMagic;

        /// <summary>v39 — Coins/Gold actually charged for this job.</summary>
        [JsonProperty("paidCoins")] public int PaidCoins;

        /// <summary>The paid basket as one value (WO-911 M2). Never null; all-zero when nothing was charged.</summary>
        [JsonIgnore]
        public JobCost Paid
        {
            get => new JobCost(PaidWood, PaidStone, PaidIron, PaidCrystals, PaidMagic, PaidCoins);
            set
            {
                PaidWood = value.Wood;
                PaidStone = value.Stone;
                PaidIron = value.Iron;
                PaidCrystals = value.Crystals;
                PaidMagic = value.Magic;
                PaidCoins = value.Coins;
            }
        }

        /// <summary>Unix-ms the job completes. Convenience = StartMs + DurationMs (not stored).</summary>
        [JsonIgnore] public double FinishMs => StartMs + DurationMs;

        /// <summary>Strongly-typed view of <see cref="JobType"/>.</summary>
        [JsonIgnore]
        public BuildJobType Type
        {
            get => (BuildJobType)JobType;
            set => JobType = (int)value;
        }

        /// <summary>Strongly-typed view of <see cref="Kind"/> (WO-773 — the ObsidianJob kind axis).</summary>
        [JsonIgnore]
        public JobKind JobKind
        {
            get => (JobKind)Kind;
            set => Kind = (int)value;
        }

        /// <summary>Strongly-typed view of <see cref="Channel"/> (WO-773 — the worker-pool channel).</summary>
        [JsonIgnore]
        public ChannelId ChannelId
        {
            get => (ChannelId)Channel;
            set => Channel = (int)value;
        }
    }
}
