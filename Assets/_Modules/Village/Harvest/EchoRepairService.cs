// =============================================================================
// EchoRepairService -- the WO-811 REPAIR task consumer (Echoes mend structures).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WO-1108: repair is PASSIVE. It is no longer an assignable task -- the roster COUNT
// drives it (EchoBonusCalculator.RepairFractionsPerSecond sums EVERY owned Echo), so
// this service needed no change here beyond honest wording: its ONE rate input already
// came from that method.
//
// WHAT IT IS: while one or more Echoes are OWNED, this service advances REAL repair on
// damaged structures through the EXISTING repair authority -- WallRepairController
// (TryPeekWorstDamaged / TryRepairWorst). It NEVER invents a parallel HP or wallet system.
// WO-1108: this is now the SOLE repair loop. PetTaskController's rival RepairAll loop --
// a second, uncoordinated repairer racing this one over the same walls and the same
// construction wallet on its own cadence -- was retired with it.
//
// THE WORK MODEL (mirrors how harvest is modeled -- abstract + rate-based, no
// locomotion; Echoes are portrait-card spirits and NEVER fight or path as units):
//   - The ROSTER accrues a WORK BUDGET in structure-damage FRACTIONS at
//     EchoBonusCalculator.RepairFractionsPerSecond() -- the ONE math source
//     (base knob EchoBalanceCatalog.RepairFractionPerHour, now authored in
//     echoes-balance.json and re-tuned 2.0 -> 0.35 by WO-1108 D3 because the sum
//     spans the whole roster instead of one assigned Echo; level-scaled by the
//     shared LaneContribution terms; NO affinity bonus -- Repairs was removed as
//     an affinity, WO-830 ruling 2026-08-02).
//   - Work accrues ONLY while a damaged, non-destroyed structure exists. ZERO
//     targets = ZERO progress (the WO-811 honesty rule -- no fake work banked
//     against a pristine town).
//   - When the budget covers the WORST target's damage fraction AND the wallet
//     covers its cost, the repair COMPLETES through TryRepairWorst: the same
//     catalog-row pricing (damage fraction x build cost, wood/iron/food, talent
//     discount included, crystals never) and the same EconomyService.TrySpend
//     path every hand-repair takes (F8-42 repair-costs canon: repair SPENDS).
//     Broke = a LOUD throttled refusal + the "waiting for materials" status --
//     never free hitpoints.
//   - PRIORITY: MOST-DAMAGED-FIRST (the WallRepairController worst-first sort;
//     matches WO-701's triage instinct -- documented choice, WO-811 Sec.4).
//   - DESTROYED structures are excluded automatically: TryPeekWorstDamaged /
//     TryRepairWorst reuse CollectRepairAllSet, which already skips
//     IsBroken / DamageFraction >= DestroyedFraction (WO-753: destroyed = LOST,
//     rebuild fresh at full cost -- never auto-repaired back).
//
// OFFLINE (single-clock canon, WO-667; RE-WIRED by WO-1147): this service is a
// CONSUMER of OfflineClaimCoordinator, which performs the ONE read of
// GameState.LastHarvestClaimMs, computes ONE elapsed window, hands the SAME window
// to every consumer, and advances the clock exactly once.
//
// WARNING -- THE BUG THAT LIVED HERE: this service used to read the clock itself from a
// Start + TWO-frame coroutine, while OfflineHarvestService WROTE that clock from a
// Start + ONE-frame coroutine. Our read therefore ALWAYS landed after the clock had
// been zeroed to "now", so (now - lastClaim) was always ~0 and OFFLINE REPAIR NEVER
// ACCRUED A SINGLE FRACTION for its entire life -- silently, because a zero window
// is indistinguishable from "the player was not away". Never re-add a local clock
// read here, and never "fix" ordering with an execution-order attribute or an extra
// frame of delay: that is the duplicate-authority pattern, not a fix.
// Our OfflineCapHours cap stays OURS (the coordinator publishes the raw window and
// each consumer clamps it), as does the MaxBankedFractions ceiling. The banked
// budget itself is NOT persisted -- the offline catch-up regenerates it from the
// clock, so a quit mid-accrual loses at most the sub-cap remainder (same
// coarse-persistence stance as the online silo tick).
//
// BATTLE GATE: no mending mid-assault (BattleLock.IsInBattle -- the same gate
// every RepairAll caller holds); battle time accrues no work.
//
// Installed by EchoWorkforceBootstrap on the persistent EchoService host (DDOL,
// no scene authoring). WO-1108: the Echo CARD no longer renders a per-Echo repair
// status line (repair is not an assignment), so Status / HasRepairTargets are now a
// diagnostic + trace surface ("nothing to repair" / "waiting for materials").
// =============================================================================
using System;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village
{
    /// <summary>The repair task's honest, player-explainable state (TEXT on the card,
    /// never hue -- colorblind law).</summary>
    public enum EchoRepairStatus
    {
        /// <summary>WO-1108: no Echo is OWNED (or no GameState yet) -- nobody can mend, so the
        /// rate is the honest zero. This replaced <c>NoneAssigned</c>: repair stopped being an
        /// assignment (every owned Echo mends passively), so "none assigned" became a state the
        /// code can no longer produce and was deleted rather than left as a lie.</summary>
        NoEchoes = 0,
        /// <summary>Echo(es) owned, but nothing is damaged -- zero progress, honestly.</summary>
        NothingToRepair = 1,
        /// <summary>Echo(es) owned and accruing work toward the worst damaged structure.</summary>
        Working = 2,
        /// <summary>Work is ready but the wallet cannot cover the repair cost (repair SPENDS).</summary>
        WaitingMaterials = 3,
    }

    /// <summary>
    /// Drives the WO-811 Echo REPAIR task: accrues rate-based repair work while
    /// Echoes are assigned to repair, and completes real repairs (most-damaged-first,
    /// paid at the live cost canon) through the existing <see cref="WallRepairController"/>
    /// backend. Offline-fair via the shared harvest clock. See the file header.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EchoRepairService : MonoBehaviour, IOfflineClaimConsumer
    {
        public static EchoRepairService Instance { get; private set; }

        /// <summary>Trace name for this consumer's share of the shared offline window.</summary>
        public string OfflineConsumerName => "echo-repair";

        // Cadence for driving the repair backend. (Was described as mirroring
        // PetTaskController.RepairScanInterval; that rival loop is gone as of WO-1108.)
        private const float ScanInterval = 1.5f;
        // Numeric slack for the budget-vs-fraction comparison.
        private const float BudgetEps = 0.0001f;
        // Hard bound on offline catch-up passes (defensive; the budget cap bounds it anyway).
        private const int MaxOfflinePasses = 24;

        [Header("Repair work (WO-811)")]
        [Tooltip("Cap on banked repair work, in structure-damage FRACTIONS (a destroyed-threshold " +
                 "structure is ~1.0). Bounds both online banking and the offline catch-up grant.")]
        [Min(0f)] public float MaxBankedFractions = 2f;

        [Tooltip("Offline catch-up window cap in HOURS (mirrors the Echo silo's 4h default -- " +
                 "the same fairness stance as harvest catch-up on the same clock).")]
        [Min(0f)] public float OfflineCapHours = 4f;

        /// <summary>The task's current honest state (the card status line reads this).</summary>
        public EchoRepairStatus Status { get; private set; } = EchoRepairStatus.NoEchoes;

        /// <summary>True when the last scan found at least one damaged, non-destroyed structure.</summary>
        public bool HasRepairTargets { get; private set; }

        public const string ComplimentaryRepairSeenKey = "echo_plans_complimentary_repair";

        /// <summary>Consumes the plans moment's once-ever free repair offer.</summary>
        public static int ClaimComplimentaryPlansRepair()
        {
            var gs = GameStateService.Instance;
            var state = gs != null ? gs.State : null;
            if (state == null || (state.SeenTutorials != null &&
                state.SeenTutorials.TryGetValue(ComplimentaryRepairSeenKey, out bool used) && used))
                return 0;

            gs.MarkTutorialSeen(ComplimentaryRepairSeenKey);
            var repair = FindAnyObjectByType<WallRepairController>();
            if (repair == null)
            {
                var go = new GameObject("WallRepair_ComplimentaryEchoEngine");
                repair = go.AddComponent<WallRepairController>();
                repair.enabled = false;
            }
            int count = repair.RepairAllComplimentary("Castle Defense Plans Echo offer");
            FlowTrace.Step("Echo", $"complimentary plans repair CLAIMED: {count} structure(s), no resources spent");
            return count;
        }

        /// <summary>Banked repair work in structure-damage fractions (diagnostic readout).</summary>
        public float BankedWork => _workBudget;

        /// <summary>Raised on status transitions and completed repairs (the card re-binds).</summary>
        public event Action Changed;

        private float _workBudget;
        private float _nextScan;
        private float _lastWorkTime = -1f;
        private WallRepairController _repair;

        /// <summary>Fractions banked by the most recent offline window (diagnostic /
        /// regression readout -- proves the share was non-zero).</summary>
        public float LastOfflineGain { get; private set; }

        /// <summary>Seconds of the last shared window this consumer actually counted
        /// (post-cap). Regression reads it to prove all three consumers saw one delta.</summary>
        public double LastOfflineCountedSeconds { get; private set; }

        // =====================================================================
        //  WO-1231 -- THE PLAYER-FACING HALF (communication only, no economy change)
        // ---------------------------------------------------------------------
        //  Passive mending was correct and completely silent: it debited Wood and Iron
        //  with no cause shown, and stalled broke with no reason shown. Both facts lived
        //  ONLY in the FlowTrace lines below, which no player ever reads. These two
        //  members are the seam that carries them to the two approved surfaces --
        //  the Echo card's PASSIVE MENDING block (live) and the while-you-were-away
        //  summary (offline). Nothing here changes a rate, a cost or the count x level
        //  math; the owner ruled 2026-08-26 that the SPEND STAYS.
        //
        //  ⛔ STATIC ON PURPOSE: the away summary is rendered by OfflineHarvestService /
        //  WelcomeBackPopup, and a headless oracle drives ApplyOfflineWindow on a bare
        //  component. Hanging the report off Instance would make both paths depend on a
        //  singleton that editmode AddComponent never populates (Awake does not run).
        // =====================================================================

        /// <summary>
        /// What passive mending DID over the most recent offline window -- the spend
        /// attribution the while-you-were-away summary renders. Reset at the top of every
        /// <see cref="ApplyOfflineWindow"/> so a later claim can never re-report an older
        /// one's spend.
        /// </summary>
        public static EchoMendReport LastOfflineMendReport { get; private set; } = EchoMendReport.None;

        /// <summary>
        /// Player-facing name of the resource the ONLINE loop is currently short of, or ""
        /// when mending is not stalled. Drives the Echo card's stall chip, which is the
        /// only place a player can learn that their walls stopped mending because they are
        /// broke. A WORD, never a hue (colourblind law).
        /// </summary>
        public string StalledResourceLabel { get; private set; } = "";

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            // Destroy(this) -- NOT Destroy(gameObject): shares the EchoService host
            // (CLAUDE.md memory: singleton-dedup-destroys-host).
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            OfflineClaimCoordinator.Register(this);   // one authority fans the offline window to us
        }

        private void OnDestroy()
        {
            OfflineClaimCoordinator.Unregister(this);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // WO-1147: no local offline coroutine. The two-frame deferral this used to own
            // (structures must be present for the WallRepairController sweep) now lives on
            // the ONE claim in OfflineHarvestService.ClaimDeferred, so every consumer is
            // served by the same, correctly-deferred claim instead of racing it.
            OfflineClaimCoordinator.Register(this);
        }

        // =====================================================================
        //  Offline catch-up -- the SAME clock seam as harvest (read-only)
        // =====================================================================

        /// <summary>
        /// THIS consumer's share of the ONE shared offline window (WO-1147): integrate the
        /// repair rate over the window capped at <see cref="OfflineCapHours"/>, bank it
        /// (further capped at <see cref="MaxBankedFractions"/>), then complete as many
        /// worst-first repairs as the budget AND the wallet cover. The window arrives from
        /// <see cref="OfflineClaimCoordinator"/>, which did the single clock read and owns
        /// advancing it -- this method never reads or writes the clock. [Flow:Echo].
        /// </summary>
        public void ApplyOfflineWindow(OfflineClaimWindow window)
        {
            var s = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (s == null) return;

            using var _t = FlowTrace.Enter("Echo", "RepairApplyOfflineWindow");

            LastOfflineGain = 0f;
            LastOfflineCountedSeconds = 0.0;
            // WO-1231: a fresh report per claim. Never carry a previous window's spend
            // into this one -- an away summary that re-reports old debits is a worse lie
            // than the silence it replaced.
            var report = new EchoMendReport { ClaimSequence = window.Sequence };
            LastOfflineMendReport = report;

            float rate = EchoBonusCalculator.RepairFractionsPerSecond();
            if (rate <= 0f)
            {
                FlowTrace.Step("Echo",
                    $"claim #{window.Sequence}: 'echo-repair' share = no owned Echo -- nothing accrues " +
                    $"(window was {window.ElapsedSeconds:F0}s).");
                return;
            }

            double elapsedSec = window.ElapsedSeconds;
            double cappedSec = window.CappedSeconds(OfflineCapHours);
            float gained = (float)(rate * cappedSec);
            _workBudget = Mathf.Min(MaxBankedFractions, _workBudget + gained);
            LastOfflineGain = gained;
            LastOfflineCountedSeconds = cappedSec;
            FlowTrace.Step("Echo",
                $"claim #{window.Sequence}: 'echo-repair' share = {cappedSec:F0}s of the {elapsedSec:F0}s window" +
                (window.ExceedsCap(OfflineCapHours) ? $" (capped at {OfflineCapHours:0.##}h)" : "") +
                $" at {rate * 3600f:0.###} fractions/h -> gained {gained:0.###}, banked {_workBudget:0.###}/{MaxBankedFractions:0.###}.");

            int done = ApplyBankedWork("echo offline repair", report);
            FlowTrace.Step("Echo",
                $"claim #{window.Sequence}: 'echo-repair' applied {done} repair(s); {_workBudget:0.###} work banked for the online loop.");
            // WO-1231: the ONE line that proves the away summary is reporting the same
            // numbers the wallet was actually charged. Permanent (never strip, CLAUDE.md S12).
            FlowTrace.Step("Echo",
                $"claim #{window.Sequence}: 'echo-repair' REPORT -> {report.Repairs} repair(s), " +
                $"+{report.HealthFraction:0.###} wall health, spent w{report.SpentWood}/i{report.SpentIron}/" +
                $"s{report.SpentStone}/c{report.SpentCrystals}" +
                (report.Stalled ? $", STALLED on {report.StalledResource}" : "") + ".");
            Changed?.Invoke();
        }

        /// <summary>Complete worst-first repairs while the banked budget AND the wallet
        /// cover the worst target. Honest stops: no targets, budget short, or broke
        /// (each traced). Returns the number of completed repairs.</summary>
        /// <param name="report">WO-1231: optional player-facing tally. Every completed
        /// repair folds its ACTUAL spend in (the value TryRepairWorst reports, not the
        /// quoted price), and a broke stop names the short resource -- so the away summary
        /// reports what the wallet was really charged and never an estimate.</param>
        private int ApplyBankedWork(string reason, EchoMendReport report = null)
        {
            var repair = EnsureRepair();
            if (repair == null) return 0;

            int done = 0;
            for (int i = 0; i < MaxOfflinePasses; i++)
            {
                if (!repair.TryPeekWorstDamaged(out string name, out float frac, out CoreCost cost))
                {
                    HasRepairTargets = false;
                    if (done == 0)
                        FlowTrace.Step("Echo", $"{reason}: nothing to repair -- banked work held.");
                    break;
                }
                HasRepairTargets = true;

                if (_workBudget + BudgetEps < frac)
                {
                    FlowTrace.Step("Echo",
                        $"{reason}: banked {_workBudget:0.###} < worst '{name}' dmg {frac:0.00} -- keep accruing online.");
                    break;
                }

                if (!repair.CanAffordMaterials(cost))
                {
                    string shortLabel = ShortResourceLabel(repair, cost);
                    if (report != null) report.StalledResource = shortLabel;
                    FlowTrace.Warn("Echo",
                        $"{reason}: cannot afford {WallRepairController.DescribeMaterials(cost)} for '{name}' " +
                        $"-- waiting for materials ({shortLabel}) (repair SPENDS; never free hitpoints).");
                    break;
                }

                if (!repair.TryRepairWorst(reason, out string repairedName, out float repairedFrac, out CoreCost spent))
                    break;   // backend refused (raced/spend failed) -- already traced by the backend

                _workBudget = Mathf.Max(0f, _workBudget - repairedFrac);
                done++;
                if (report != null)
                {
                    report.Repairs++;
                    report.HealthFraction += Mathf.Max(0f, repairedFrac);
                    report.AddSpend(spent);
                }
                FlowTrace.Step("Echo",
                    $"{reason}: repaired '{repairedName}' (dmg {repairedFrac:0.00}) for " +
                    $"{WallRepairController.DescribeMaterials(spent)}; {_workBudget:0.###} work remains.");
            }
            return done;
        }

        // =====================================================================
        //  Online loop -- accrue + complete on the scan cadence
        // =====================================================================

        private void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + ScanInterval;
            Tick();
        }

        private void Tick()
        {
            // Consume the wall-clock delta up front so paused/gated stretches never
            // retro-accrue when the gate lifts.
            float now = Time.time;
            float dt = _lastWorkTime >= 0f ? Mathf.Max(0f, now - _lastWorkTime) : 0f;
            _lastWorkTime = now;

            var s = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (s == null) { SetStatus(EchoRepairStatus.NoEchoes, HasRepairTargets); return; }

            float rate = EchoBonusCalculator.RepairFractionsPerSecond();
            if (rate <= 0f)
            {
                // WO-1108: no OWNED Echo -> no banked work either (honest: the budget is the
                // roster's labor, not a free-floating pool). With repair passive this is
                // reachable only at zero Echoes (or before GameState lands).
                if (_workBudget > 0f)
                    FlowTrace.Step("Echo", $"Echo repair: no owned Echo -- dropping {_workBudget:0.###} banked work.");
                _workBudget = 0f;
                SetStatus(EchoRepairStatus.NoEchoes, HasRepairTargets);
                return;
            }

            // Never mend mid-assault (the same gate every RepairAll caller holds).
            if (DeNelle.Core.Combat.BattleLock.IsInBattle()) return;

            var repair = EnsureRepair();
            if (repair == null) return;

            if (!repair.TryPeekWorstDamaged(out string worstName, out float worstFrac,
                                           out CoreCost worstCost, out GameObject worstTarget))
            {
                // ZERO targets = ZERO progress: no accrual, no fake work (WO-811 honesty rule).
                SetStatus(EchoRepairStatus.NothingToRepair, false);
                FlowTrace.Throttle("Echo", "repair-clean", 15f,
                    "Echo repair: nothing to repair -- no work accrues (honest empty state).");
                return;
            }

            _workBudget = Mathf.Min(MaxBankedFractions, _workBudget + rate * dt);
            EchoRepairProgressBillboard.Show(worstTarget,
                worstFrac > BudgetEps ? Mathf.Clamp01(_workBudget / worstFrac) : 1f,
                RepairingEchoName());
            FlowTrace.Throttle("Echo", "repair-tick", 5f,
                $"Echo repair tick: rate {rate * 3600f:0.###}/h, banked {_workBudget:0.###}, " +
                $"worst '{worstName}' (dmg {worstFrac:0.00}).");

            if (_workBudget + BudgetEps < worstFrac)
            {
                SetStatus(EchoRepairStatus.Working, true);
                return;
            }

            if (!repair.CanAffordMaterials(worstCost))
            {
                // WO-1231: name the SHORT resource, not just "materials". "Waiting for
                // materials" is only actionable once the player knows WHICH one to go get,
                // and this is the state that previously existed nowhere but a FlowTrace.
                string shortLabel = ShortResourceLabel(repair, worstCost);
                SetStatus(EchoRepairStatus.WaitingMaterials, true, shortLabel);
                FlowTrace.Throttle("Echo", "repair-broke", 15f,
                    $"Echo repair: cannot afford {WallRepairController.DescribeMaterials(worstCost)} " +
                    $"for '{worstName}' -- waiting for materials ({shortLabel}) " +
                    "(repair SPENDS; never free hitpoints).");
                return;
            }

            if (repair.TryRepairWorst("echo repair", out string name, out float frac, out CoreCost spent))
            {
                _workBudget = Mathf.Max(0f, _workBudget - frac);
                SetStatus(EchoRepairStatus.Working, true);
                FlowTrace.Step("Echo",
                    $"Echo repair: repaired '{name}' (dmg {frac:0.00}) for " +
                    $"{WallRepairController.DescribeMaterials(spent)}; {_workBudget:0.###} work banked.");
                Changed?.Invoke();
            }
        }

        private static string RepairingEchoName()
        {
            var entry = EchoRosterCatalog.ByCount(1);
            string display = entry != null ? entry.DisplayName : "";
            int comma = display.IndexOf(',');
            return comma > 0 ? display.Substring(0, comma).Trim() : display.Trim();
        }

        /// <param name="stalledLabel">WO-1231: the short resource's player-facing name while
        /// <paramref name="status"/> is <see cref="EchoRepairStatus.WaitingMaterials"/>.
        /// Forced to "" for every other status so the card's stall chip cannot survive the
        /// state that produced it -- a chip that outlives its cause is the same class of lie
        /// as the silence this ticket removed.</param>
        private void SetStatus(EchoRepairStatus status, bool hasTargets, string stalledLabel = "")
        {
            string label = status == EchoRepairStatus.WaitingMaterials ? (stalledLabel ?? "") : "";
            if (Status == status && HasRepairTargets == hasTargets && StalledResourceLabel == label) return;
            Status = status;
            HasRepairTargets = hasTargets;
            StalledResourceLabel = label;
            FlowTrace.Step("Echo", $"Echo repair status -> {status} (targets={hasTargets}" +
                (label.Length > 0 ? $", short of {label}" : "") + ").");
            Changed?.Invoke();
        }

        // =====================================================================
        //  WO-1231 -- WHICH resource is short (the actionable half of the stall)
        // =====================================================================

        /// <summary>
        /// Names the resource(s) the wallet cannot cover for <paramref name="cost"/>, in the
        /// player's words ("Wood", "Wood and Iron").
        /// <para>
        /// It decides this by re-asking the SAME authority the stall itself used --
        /// <see cref="WallRepairController.CanAffordMaterials"/>, once per slot in isolation
        /// -- rather than reading the wallet fields directly. That matters: the affordability
        /// gate runs through EconomyService and has its own rules, so a second, hand-rolled
        /// wallet comparison here would be a duplicate authority that drifts, and would
        /// eventually name a resource the player is NOT actually short of. Reusing the gate
        /// means the chip can only ever say what the gate says.
        /// </para>
        /// Returns "" when every slot is individually affordable (a race, or an
        /// EconomyService-absent refusal) -- the chip then falls back to the un-named
        /// "waiting for materials", which is still true.
        /// </summary>
        private static string ShortResourceLabel(WallRepairController repair, CoreCost cost)
        {
            if (repair == null) return "";
            var missing = new System.Collections.Generic.List<string>(4);
            if (cost.wood > 0 && !repair.CanAffordMaterials(new CoreCost { wood = cost.wood })) missing.Add("Wood");
            if (cost.iron > 0 && !repair.CanAffordMaterials(new CoreCost { iron = cost.iron })) missing.Add("Iron");
            if (cost.stone > 0 && !repair.CanAffordMaterials(new CoreCost { stone = cost.stone })) missing.Add("Stone");
            if (cost.crystals > 0 && !repair.CanAffordMaterials(new CoreCost { crystals = cost.crystals })) missing.Add("Crystals");
            if (missing.Count == 0) return "";
            if (missing.Count == 1) return missing[0];
            return string.Join(" and ", missing.ToArray());
        }

        // =====================================================================
        //  Backend resolution -- reuse the ONE repair authority (pet-task pattern)
        // =====================================================================

        /// <summary>
        /// Resolves the shared repair backend: reuses an existing
        /// <see cref="WallRepairController"/> (a wave scene / HubRepairAffordance installs
        /// one) or creates a LOGIC-ONLY, disabled controller purely to price + apply
        /// repairs -- never a second repair system (mirrors HubRepairAffordance.EnsureRepair
        /// verbatim; the PetTaskController.EnsureRepair it also copied is gone, WO-1108).
        /// </summary>
        private WallRepairController EnsureRepair()
        {
            if (_repair != null) return _repair;
            _repair = FindAnyObjectByType<WallRepairController>();
            if (_repair == null)
            {
                var go = new GameObject("WallRepair_EchoRepairEngine");
                _repair = go.AddComponent<WallRepairController>();
                _repair.enabled = false;   // logic-only: we call TryPeekWorstDamaged / TryRepairWorst directly
                FlowTrace.Step("Echo", "Echo repair task self-installed a logic-only WallRepairController.");
            }
            return _repair;
        }
    }
}
