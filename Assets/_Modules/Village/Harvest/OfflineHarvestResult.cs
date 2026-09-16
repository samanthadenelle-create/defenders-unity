// =============================================================================
// OfflineHarvestResult — the per-resource haul accrued while the player was away
// (WO-115). A plain value carrier raised to the welcome-back popup; it never
// touches GameState itself (the service banks; this only reports what it banked).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Resource buckets map 1:1 to MineNode.MineResource / the GameState wallet fields
// (Iron / Wood / Stone / AetherCrystals) so the popup can render +N rows without
// re-deriving anything. Pet harvest (WO-111 Phase 4) folds into the same buckets
// when it lands — no shape change needed.
// =============================================================================

using System.Collections.Generic;

namespace DeNelle.Village
{
    /// <summary>
    /// The result of an offline-accrual claim: how much of each resource was
    /// banked, how long the player was away, and whether the cap clipped it.
    /// </summary>
    public sealed class OfflineHarvestResult
    {
        /// <summary>Iron banked this claim.</summary>
        public int Iron;
        /// <summary>Wood banked this claim.</summary>
        public int Wood;
        /// <summary>Stone banked this claim (the retired "Stone" axis, repurposed — DEF-121).</summary>
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [Newtonsoft.Json.JsonProperty("Food")] public int Stone;
        /// <summary>Aether Crystals banked this claim.</summary>
        public int AetherCrystals;

        /// <summary>Real seconds since the last claim (BEFORE the cap clamp) — for the away-time line.</summary>
        public double AwaySeconds;
        /// <summary>True when <see cref="AwaySeconds"/> exceeded the offline cap (the gentle nudge).</summary>
        public bool WasCapped;

        // =====================================================================
        //  WO-1128 — WHICH CLOCK PRODUCED THIS WINDOW (the reconciliation half)
        // ---------------------------------------------------------------------
        //  The device clock cannot be verified and we do not try (WO-1128 §1).
        //  What we CAN do is record, per window, whether "now" came from the
        //  monotonic server anchor (ServerClock, unforgeable by a wall-clock edit)
        //  or from the raw device clock, and carry the window's own endpoints so
        //  the server can compare the client's DECLARED window against its OWN
        //  elapsed time on the next round trip (api/game/save.js §RECONCILE).
        //
        //  These fields are DIAGNOSTIC + DECLARATIVE, never punitive. Nothing in
        //  the client reduces a haul because the clock was unanchored — a cold
        //  launch is ALWAYS unanchored (Stopwatch dies with the process), and a
        //  player on a plane is not a cheater. Refuse server-side, never punish
        //  client-side.
        // =====================================================================

        /// <summary>
        /// True when <c>TimeSource.NowUnixMs()</c> was server-anchored for THIS claim
        /// (<see cref="DeNelle.Core.State.ServerClock.IsTrusted"/>). False on any cold
        /// launch before the first backend round trip — an expected, honest state.
        /// </summary>
        public bool ServerAnchored;

        /// <summary>Unix-ms this window started at (the persisted claim clock, or the pause edge).</summary>
        public double WindowStartUnixMs;

        /// <summary>Unix-ms "now" that closed this window — the value the clock advanced to.</summary>
        public double NowUnixMs;

        /// <summary>
        /// True when this haul rests on an unverifiable device clock and is therefore
        /// subject to server reconciliation on the next sync. Display/telemetry only.
        /// </summary>
        public bool IsProvisional => !ServerAnchored;

        /// <summary>Short trace/telemetry name of the clock this window trusted.</summary>
        public string ClockSource => ServerAnchored ? "server-anchored" : "device";

        /// <summary>Total units banked across every resource (popup-trigger gate: show only when &gt; 0).</summary>
        public int Total => Iron + Wood + Stone + AetherCrystals;

        // =====================================================================
        //  WO-1231 — WHAT PASSIVE ECHO MENDING DID WITH THE SAME WINDOW
        // ---------------------------------------------------------------------
        //  Offline mending is not a haul, it is a SPEND: EchoRepairService banks an
        //  'echo-repair' share of the away window and completes real repairs, paying
        //  Wood and Iron out of the wallet. So a player could return to materials
        //  already gone with no report that it happened — the away summary was, by
        //  construction, only ever telling them half the story.
        //
        //  ⚠ THIS FIELD IS WHY THE POPUP'S TRIGGER GATE MOVED. `Total > 0` alone means
        //  a window in which mending spent 400 Wood and gathered nothing shows NO
        //  summary at all — the exact case where the player most needs one. The gate
        //  became "haul OR mend" here, and then (LANE G, 2026-09-04) moved onto THIS
        //  type as HasSummaryContent with FOUR axes: haul OR mend OR a finished queue
        //  job OR resources waiting in a collector. Neither the service nor the popup
        //  re-derives it any more — see the LANE G block below.
        // =====================================================================

        /// <summary>
        /// Passive Echo mending's share of the SAME away window (never null in practice —
        /// OfflineHarvestService attaches the live report once every consumer has applied).
        /// </summary>
        public EchoMendReport Mend;

        /// <summary>True when mending did something the player must be told about — it
        /// mended, it spent, or it stalled broke.</summary>
        public bool HasMendNews => Mend != null && Mend.HasContent;

        // =====================================================================
        //  LANE G (2026-09-04) — WHAT THE QUEUE FINISHED, WHAT THE COLLECTORS HOLD,
        //  AND THE ONE GATE THAT DECIDES WHETHER THE RETURNING PLAYER IS TOLD
        // ---------------------------------------------------------------------
        //  The economy map (docs/PROGRAM_RAID_ECONOMY_2026-09-04.md sec.7) opens the
        //  ideal returning session on two beats: "BUILD COMPLETE -> collect" and
        //  "Resources full -> collect". Measured at source before this change, the
        //  away summary could say neither. Its gate was the two-term expression
        //  `Total <= 0 && !HasMendNews` — written TWICE, once in
        //  OfflineHarvestService.OnClaimCompleted and once in WelcomeBackPopup.Show —
        //  so a player whose nodes were idle, whose Echoes were quiet, whose three
        //  overnight builds had finished and whose farm was sitting full got NO SCREEN
        //  AT ALL. A collector-only town scored zero on both terms.
        //
        //  ⚠ HasSummaryContent IS THE ONE GATE. It lives on the result so the service
        //  and the popup cannot disagree about what counts as news. Do not re-derive
        //  it at a call site — a second copy of the gate is the defect this block fixes
        //  (pinned by Editor/Regression/AwaySummaryReportRegression.cs, cases 1-4).
        //
        //  These are REPORT fields, never a second wallet route: the service records a
        //  finished job (it never completes or re-applies one) and READS a collector's
        //  pending (it never banks it — the COLLECT button carries the tap to the
        //  existing CollectorStatusGate.RequestCollectAll).
        // =====================================================================

        /// <summary>
        /// One queue job that finished inside the away window. Verb + Label come from the
        /// SHARED card seam (BuildTimerService.EntryFor) so the summary says the same words
        /// the queue card said while the job was running — never a second vocabulary.
        /// </summary>
        public sealed class OfflineJobLine
        {
            /// <summary>The card verb ("BUILD", "UPGRADE", "TRAIN", ...).</summary>
            public string Verb;
            /// <summary>Player-facing job name ("Barracks").</summary>
            public string Label;
            /// <summary>Unix-ms the job finished — window membership is tested on this, not on arrival order.</summary>
            public double FinishedUnixMs;
        }

        /// <summary>Queue jobs that finished inside THIS window, oldest first. Never null.</summary>
        public readonly List<OfflineJobLine> CompletedJobs = new List<OfflineJobLine>();

        /// <summary>How many queue jobs finished inside this window.</summary>
        public int CompletedJobCount => CompletedJobs.Count;

        /// <summary>
        /// What the collectors hold, grouped BY RESOURCE (owner felt-test rulings 2026-09-04
        /// 22:30: "the collectors need to be seperated" then "Wood Iron Stone different rows").
        /// One line per resource with a non-zero pending, in the HUD rail's fixed order
        /// (Wood, Iron, Stone, Crystals) — never one aggregate line, never one row per building.
        /// </summary>
        public sealed class OfflineCollectorLine
        {
            /// <summary>The game's canon resource word from ResourceBuildingProgression.LabelFor
            /// ("Wood" / "Iron" / "Stone" / "Crystals") — the same word the HUD rail says.</summary>
            public string Resource;
            /// <summary>Whole units held across every collector of this resource — reported, not banked.</summary>
            public int Pending;
            /// <summary>How many collectors of this resource are holding something.</summary>
            public int Collectors;
        }

        /// <summary>Per-resource lines in rail order, filled by OfflineHarvestService.AttachPendingCollectors.
        /// Never null. <see cref="PendingCollectorTotal"/> / <see cref="PendingCollectorCount"/> stay the
        /// SUMS over this list so the gate (<see cref="HasCollectorNews"/>) does not change shape.</summary>
        public readonly List<OfflineCollectorLine> PendingCollectors = new List<OfflineCollectorLine>();

        /// <summary>Units STILL HELD across every collector at reveal time — reported, not banked.</summary>
        public int PendingCollectorTotal;

        /// <summary>How many collectors are holding something (the row's singular/plural).</summary>
        public int PendingCollectorCount;

        // =====================================================================
        //  WO-1434 — THE ECHO SILO, THE FIFTH AXIS THE RETURN SCREEN COULD NOT SAY
        // ---------------------------------------------------------------------
        //  MEASURED on the owner's Seeker, 2026-09-06 (build 358161). The 12:50:05
        //  claim covering 3h40m produced, in this order:
        //      [Flow:Echo]     claim #1: 'echo-silo' share = 13221s ... -> +52884 to
        //                      silo -> 57600/57600 (echoes 4).
        //      [Flow:Offline]  accrued over 13221s: worker-owned=0 node(s), total=0
        //      [Flow:Offline]  claim #1: away summary gate -> haul=0, mendNews=False,
        //                      jobs=0, collectorsPending=42782 ... => REVEAL.
        //  The single largest thing that happened in her absence — 57,600 units into a
        //  FULL silo — scored on NONE of the four axes, so the screen that exists to
        //  report the away window never mentioned it. WO-1434 sec.3 recorded those rows
        //  as coming from the offline-harvest grant; they do not. `Total` was 0 and
        //  `Grant()` never ran. The producer is EchoService.DumpSilos.
        //
        //  These are REPORT fields on the same terms as the collector fields above: the
        //  service READS EchoService.PredictDumpSplit (it never dumps — the COLLECT
        //  button carries the tap to the existing CollectorStatusGate.RequestCollectAll,
        //  which is what calls DumpSilos).
        // =====================================================================

        /// <summary>One resource's share of what a Dump would route out of the Echo silo.</summary>
        public sealed class OfflineSiloLine
        {
            /// <summary>The game's canon resource word ("Wood" / "Iron" / "Stone" / "Crystals").</summary>
            public string Resource;
            /// <summary>Whole units the silo would route to this resource — reported, not banked.</summary>
            public int Pending;
        }

        /// <summary>Per-resource silo shares in rail order, filled by
        /// OfflineHarvestService.AttachSiloPending. Never null.</summary>
        public readonly List<OfflineSiloLine> SiloPending = new List<OfflineSiloLine>();

        /// <summary>Units the Echo silo is holding across every resource — reported, not banked.</summary>
        public int SiloTotal;

        /// <summary>
        /// True when the silo has hit its ceiling and the Echoes have STOPPED gathering.
        /// `FOUNDATIONAL_RULINGS.md` section 7: a faucet that stopped must be said in words.
        /// </summary>
        public bool SiloAtCap;

        /// <summary>True when the Echo silo is holding something the player could collect.</summary>
        public bool HasSiloNews => SiloTotal > 0;

        /// <summary>True when at least one queue job finished inside this window.</summary>
        public bool HasJobNews => CompletedJobs.Count > 0;

        // =====================================================================
        //  WO-1408 -- WAS THE TOWN ATTACKED WHILE I WAS GONE?
        // ---------------------------------------------------------------------
        //  REPORT fields on the same terms as the collector/silo fields above:
        //  OfflineHarvestService.AttachAttacks READS DefenseReportLedger for records
        //  whose timestamp falls inside THIS window. It never appends, never marks
        //  read, never scores -- the DEFENCE REPORT owns all of that, and the row
        //  here is a DOOR onto it (PanelId.DefenseReport), never a second account.
        //
        //  (!) THESE ARE DELIBERATELY NOT TERMS OF HasSummaryContent. The reveal gate
        //  is pinned at five axes by AwaySummaryReportRegression cases 1-4; an attack
        //  row rides an ALREADY-revealed screen. A window whose only news is an attack
        //  is a separate product question (the Defence Report has its own unread badge),
        //  and widening the gate here would silently re-point four oracle cases.
        //
        //  ! AWAY ATTACKS ARE NOT PRODUCED TODAY. DefenseResolution's own doc says away
        //  time becomes PRESSURE, not a simulated battle, and DefenseResolution.Live is
        //  the only value any producer writes. So this is live-empty except for a fight
        //  that ended inside the window edge -- built data-driven so it reports the day
        //  in-absentia resolution lands, rather than needing a second pass then.
        // =====================================================================

        /// <summary>How many defence reports were written inside THIS away window.</summary>
        public int AttackCount;

        /// <summary>The newest breached crossing's display name ("North Gate"), or empty when
        /// the defence held or the crossing was open ground rather than a named gate.</summary>
        public string AttackBreachName = string.Empty;

        /// <summary>The newest attack's one-word verdict ("HELD" / "BREACHED" / "OVERRUN"),
        /// or empty when nothing attacked. ASCII, upper case -- it reaches a mobile font atlas.</summary>
        public string AttackOutcomeWord = string.Empty;

        /// <summary>True when the town was attacked inside this window.</summary>
        public bool HasAttackNews => AttackCount > 0;

        /// <summary>True when a collector is holding something the player could collect.</summary>
        public bool HasCollectorNews => PendingCollectorTotal > 0;

        /// <summary>
        /// THE ONE REVEAL GATE — haul OR mend OR a finished job OR resources waiting in a
        /// collector OR resources waiting in the Echo silo. Both
        /// OfflineHarvestService.OnClaimCompleted and WelcomeBackPopup.Show read this and
        /// nothing else.
        /// <para>WO-1434 added the silo term. Without it a town whose nodes are idle and whose
        /// collectors are empty but whose silo is FULL gets no screen at all — the same
        /// fall-through LANE G fixed for the collector-only town, one axis over.</para>
        /// </summary>
        public bool HasSummaryContent =>
            Total > 0 || HasMendNews || HasJobNews || HasCollectorNews || HasSiloNews;

        /// <summary>A zero haul — nothing accrued, no popup.</summary>
        public static OfflineHarvestResult None => new OfflineHarvestResult();

        /// <summary>Add an integer amount to the bucket for <paramref name="resource"/>.</summary>
        public void Add(MineResource resource, int amount)
        {
            if (amount <= 0) return;
            switch (resource)
            {
                case MineResource.Iron:          Iron += amount;           break;
                case MineResource.Wood:          Wood += amount;           break;
                case MineResource.Stone:          Stone += amount;          break;
                case MineResource.AetherCrystal: AetherCrystals += amount; break;
            }
        }
    }
}
