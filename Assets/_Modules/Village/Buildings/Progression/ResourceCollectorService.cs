// =============================================================================
// ResourceCollectorService - Collect All at Heart (pipe home, WO-663).
// -----------------------------------------------------------------------------
// WO-1392 (2026-09-05) - THIS FILE NOW OWNS THE ONE PER-RESOURCE PENDING PRODUCER.
//
//   THE DEFECT, measured on the owner's two captures one tap apart (build 355952):
//   the welcome-back popup said "WOOD WAITING +672" and the harvest result said
//   "Collected 1979 of 2393 | Uncollected 414 - they are lost". Two producers:
//     * the popup summed Floor(ResourceCollector.PendingAmount) per resource, in its own
//       loop inside OfflineHarvestService.AttachPendingCollectors (the COLLECTORS);
//     * the modal's "of 2393" was BankOverflowStatus.Requested from the ONLY WarnScope in
//       the tree - EchoService.DumpSilos - i.e. the ECHO SILO's wood share, a number the
//       popup never showed. The collectors' own Collect() ran OUTSIDE any scope, so a clamp
//       there was never reported, and its remainder was BURNED (`_pending -= amount`).
//   The trace reconciles to the unit: banked=5171 = collectors 672+403+874 (all fit) +
//   silo wood 1979 + silo iron 1229 + 14 uncapped.
//
//   THE FIX: PendingByResource() below is the ONE producer. The popup's rows read it
//   (OfflineHarvestService.AttachPendingCollectors) and CollectAll snapshots it BEFORE the
//   sweep so the harvest result's "of N" IS the popup's number. Collect() never burns any
//   more (ResourceCollector.Collect banks up to headroom and leaves the rest pending), and
//   the collector rows say so ("still waiting") through HarvestOverflowModal.CollectorSource.
//   The whole tap is ONE modal: CollectAll opens a HarvestOverflowModal batch so the silo
//   dump's own scope (unchanged, EchoService is out of this lane) joins the same screen.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.Ops;
using DeNelle.Core.UI;
using DeNelle.Village;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>Sweeps every live collector pending into the central wallet.</summary>
    public static class ResourceCollectorService
    {
        /// <summary>One per-resource pending line - the popup row and the result row both read this.</summary>
        public sealed class PendingLine
        {
            public HarvestResource Resource;
            /// <summary>Whole units held across every collector of this resource (floored PER collector,
            /// the way each collector's own Collect() floors - so the sum of the rows is what a tap banks).</summary>
            public int Pending;
            /// <summary>How many collectors of this resource are holding something.</summary>
            public int Collectors;
        }

        /// <summary>The HUD rail's fixed order (HudKitController names[] = Wood, Iron, Stone, Crystals;
        /// HarvestResource.Food IS the Stone slot). Every per-resource surface lists in this order.</summary>
        public static readonly HarvestResource[] RailOrder =
            { HarvestResource.Wood, HarvestResource.Iron, HarvestResource.Food, HarvestResource.Crystals };

        /// <summary>
        /// THE ONE PRODUCER (WO-1392). What every live collector is holding, grouped by resource, in
        /// rail order, only resources with a non-zero pending. The welcome-back popup's "WOOD WAITING
        /// +N" rows and the harvest result's "Collected X of N" read THIS and nothing else.
        /// </summary>
        public static List<PendingLine> PendingByResource()
        {
            // WO-1483 frame budget. This service is static and has no Update of its own — its
            // TICK is CollectorStatusPublisher.Update (0.5s cadence), which lands here via the
            // registry sweep below. Measured HERE so the cost is attributed to the sweep, not
            // to the publisher. Accumulating 4-arg overload — no per-tick log; PerfReporter
            // rolls it up 1/s.
            using var _perf = FlowTrace.Measure("Perf", "ResourceCollectorService.PendingByResource", 4f, 1f);

            var samples = new List<KeyValuePair<HarvestResource, double>>();
            foreach (var c in ResourceCollectorRegistry.All)
            {
                if (c == null) continue;
                samples.Add(new KeyValuePair<HarvestResource, double>(c.Resource, c.PendingAmount));
            }
            return AggregatePending(samples);
        }

        /// <summary>
        /// The PURE half of <see cref="PendingByResource"/>: floor each collector's pending, sum per
        /// resource, list in <see cref="RailOrder"/>, drop zeros. Pinned by CollectorIncomeRegression
        /// [popup-and-result-agree] with fixture samples, which is why it takes samples and not the registry.
        /// </summary>
        public static List<PendingLine> AggregatePending(IEnumerable<KeyValuePair<HarvestResource, double>> samples)
        {
            var pending = new Dictionary<HarvestResource, int>();
            var count = new Dictionary<HarvestResource, int>();
            if (samples != null)
            {
                foreach (var s in samples)
                {
                    int whole = (int)System.Math.Floor(s.Value);
                    if (whole <= 0) continue;
                    pending.TryGetValue(s.Key, out int had);
                    pending[s.Key] = had + whole;
                    count.TryGetValue(s.Key, out int n);
                    count[s.Key] = n + 1;
                }
            }
            var lines = new List<PendingLine>(RailOrder.Length);
            foreach (var res in RailOrder)
            {
                if (!pending.TryGetValue(res, out int held) || held <= 0) continue;
                count.TryGetValue(res, out int n);
                lines.Add(new PendingLine { Resource = res, Pending = held, Collectors = n });
            }
            return lines;
        }

        /// <summary>The town-bank axis a harvest resource banks into (Stone is the frozen Food slot).</summary>
        public static BankResource BankResourceOf(HarvestResource r)
        {
            switch (r)
            {
                case HarvestResource.Wood:     return BankResource.Wood;
                case HarvestResource.Iron:     return BankResource.Iron;
                case HarvestResource.Food:     return BankResource.Food;
                default:                       return BankResource.Crystals;
            }
        }

        /// <summary>Units the town bank can still take of this resource right now (int.MaxValue when uncapped).
        /// The one headroom read the pre-COLLECT warning and the result rows share.</summary>
        public static int HeadroomFor(HarvestResource r) => TownBankCapacity.RoomFor(BankResourceOf(r));

        /// <summary>
        /// Collect All: hub collectors + echo silo dump in one CoC swoosh.
        /// Returns total integer resources banked.
        /// </summary>
        public static int CollectAll(bool showResult = true)
        {
            using var _ = FlowTrace.Enter("Harvest", "CollectAll");

            // WO-1243 OPERATOR KILL SWITCH: farming.
            // Gated HERE and not at the chip tap because CollectAll has more than one
            // caller (the collectors chip AND AutoHarvestService), and a gate only the
            // button honours is no gate at all. Refuses BEFORE the first c.Collect(),
            // so nothing is banked and no pending is consumed.
            // !! This is the COURTESY half. The seal itself is enforced server side -
            // see api/_lib/maintenance.js. Fail-OPEN: with the table unreachable this
            // returns false and farming carries on (owner ruling 2026-08-27).
            if (MaintenanceCatalog.Refuses(MaintenanceArea.Farming, "collect-all", out string sealedMsg))
            {
                ElarionUiKit.ShowToast(sealedMsg, ElarionUiKit.ToastTone.Info);
                return 0;
            }

            // WO-1392 - the SNAPSHOT is the popup's producer, read at the tap. "Collected X of N"
            // reports N from here, so the result can only ever disagree with the popup by what
            // accrued between the reveal and the tap - never by a second producer.
            var before = PendingByResource();
            var storeBefore = new Dictionary<HarvestResource, int>();
            foreach (var line in before)
                storeBefore[line.Resource] = TownBankCapacity.CurrentOf(BankResourceOf(line.Resource));

            int total = 0;
            var bankedBy = new Dictionary<HarvestResource, int>();
            // ONE screen per tap: the collector rows and the silo dump's own overflow scope
            // (EchoService.DumpSilos, unchanged) land in the same HARVEST RESULT.
            using (HarvestOverflowModal.BeginBatch(showResult ? "CollectAll" : null))
            {
                foreach (var c in ResourceCollectorRegistry.All)
                {
                    if (c == null) continue;
                    int banked = c.Collect(out int requestedHere, out int leftHere);
                    total += banked;
                    bankedBy.TryGetValue(c.Resource, out int had);
                    bankedBy[c.Resource] = had + banked;
                }

                var rows = BuildCollectorRows(before, bankedBy, storeBefore);
                if (showResult && rows.Count > 0) HarvestOverflowModal.Present(rows);

                var echo = EchoService.Instance;
                if (echo != null)
                    total += echo.DumpSilos(showResult);
            }

            FlowTrace.Step("Harvest", $"collect-all total-banked={total}");
            return total;
        }

        /// <summary>
        /// The harvest result's COLLECTOR rows, PURE (pinned by CollectorIncomeRegression
        /// [popup-and-result-agree] / [overflow-stays-pending]). One row per resource whose pending
        /// did NOT all fit: Requested = the popup's number (the snapshot), Granted = what banked,
        /// Lost = what is STILL WAITING in the collectors (nothing is burned any more - the word
        /// "Lost" is the struct's field name, and <see cref="HarvestOverflowModal.CollectorSource"/>
        /// is what makes the copy say "waiting" instead). Current = the store BEFORE the tap, the
        /// same meaning TownBankCapacity.ClampGrant gives it, so the modal can print one post-collect
        /// figure for both kinds of row.
        /// </summary>
        public static List<BankOverflowStatus> BuildCollectorRows(
            IReadOnlyList<PendingLine> before,
            IReadOnlyDictionary<HarvestResource, int> bankedBy,
            IReadOnlyDictionary<HarvestResource, int> storeBefore)
        {
            var rows = new List<BankOverflowStatus>();
            if (before == null) return rows;
            foreach (var line in before)
            {
                if (line == null || line.Pending <= 0) continue;
                int banked = 0;
                if (bankedBy != null) bankedBy.TryGetValue(line.Resource, out banked);
                if (banked < 0) banked = 0;
                if (banked > line.Pending) banked = line.Pending;
                int waiting = line.Pending - banked;
                if (waiting <= 0) continue;                  // everything fit: no row, no scold

                var bank = BankResourceOf(line.Resource);
                int current = 0;
                if (storeBefore != null) storeBefore.TryGetValue(line.Resource, out current);
                rows.Add(new BankOverflowStatus
                {
                    Available = true,
                    Resource = bank,
                    ResourceName = ResourceBuildingProgression.LabelFor(line.Resource),
                    ContainerName = TownBankCapacity.ContainerNameFor(bank),
                    Requested = line.Pending,
                    Granted = banked,
                    Lost = waiting,
                    Max = TownBankCapacity.MaxOf(bank),
                    Current = current < 0 ? 0 : current,
                    OverCap = current > TownBankCapacity.MaxOf(bank),
                    Source = HarvestOverflowModal.CollectorSource,
                });
                FlowTrace.Warn("Harvest",
                    $"collect-all {ResourceBuildingProgression.LabelFor(line.Resource)}: {banked} of {line.Pending} " +
                    $"banked, {waiting} STILL WAITING in the collectors (bank {current}/{TownBankCapacity.MaxOf(bank)}) " +
                    "- nothing burned (WO-1392); it banks on the next collect once there is room.");
            }
            return rows;
        }

        /// <summary>Sum of pending across all collectors (HUD readout).</summary>
        public static int TotalPending()
        {
            int sum = 0;
            foreach (var line in PendingByResource()) sum += line.Pending;
            return sum;
        }

        /// <summary>Max fill fraction across collectors (siege telegraph).</summary>
        public static float MaxFillFraction()
        {
            float max = 0f;
            foreach (var c in ResourceCollectorRegistry.All)
            {
                if (c != null && c.FillFraction > max) max = c.FillFraction;
            }
            return max;
        }
    }
}
