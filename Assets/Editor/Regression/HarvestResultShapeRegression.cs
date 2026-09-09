// =============================================================================
// HarvestResultShapeRegression [harvest-result-shape]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core).
//
// WO-1525. The HARVEST RESULT screen was ELEVEN LINES OF PROSE, and the owner said
// so plainly (2026-09-06 20:29, build 358574, frame
// Logs/device/screens/owner-screen-20260906-202933.png):
//
//     "Harvest results feel off and way to much to read, needs organized and
//      visually pleasing"
//
// Four prose lines per resource, three resources over:
//
//     Stone storage: 3000 / 3000 (full)
//     Collected: 0 of 32307 from your collectors | Uncollected: 32307
//     Those 32307 stone are still waiting in your collectors - nothing was lost.
//     Stone storage 3000 is full. Spend stone, or upgrade a Stoneyard, then collect again.
//
// This suite drives HarvestResultVM.Build - the PURE seam - with the owner's ACTUAL
// numbers from that frame, so the assertions are about the SHAPE the player reads,
// never about source text. No canvas, no scene, no PlayMode.
//
// Cases:
//   1  [three-rows]        three resources in, three rows out, in order.
//   2  [banked-is-big]     the banked figure is the row's own field, grouped, and a
//                          zero bank prints "0" (never "+0").
//   3  [waiting-survives]  the WO-1434 reassurance FIGURE is still on every row - the
//                          ticket forbids shortening by deleting it.
//   4  [state-is-a-word]   a full store says the WORD "FULL" (the owner is red/green
//                          colourblind; hue may never carry the state).
//   5  [full-row-has-door] every blocked row carries ONE action to a DEFINED PanelId.
//   6  [build-vs-upgrade]  0 containers built -> BUILD; 1+ -> UPGRADE. Same fixture.
//   7  [said-once]         "nothing was lost" appears EXACTLY ONCE on the whole screen.
//   8  [burn-never-lies]   a genuinely-burning producer gets NO reassurance footer, and
//                          its second figure reads "lost" (WO-1434, inverted).
//   9  [overcap-spends]    OverCap is a different situation and gets the SPEND door.
//   10 [no-paragraphs]     no row field contains a newline - a row is fields, not prose.
//   11 [row-cap]           more resources than MaxRows collapse into "+N more".
//   12 [ascii-only]        every produced character is ASCII (non-ASCII = device tofu).
//   13 [one-row-per-resource] (WO-1525b) THE OWNER'S TWO FRAMES OF 2026-09-07, RECONCILED.
//                          Six producer statuses over three resources -> THREE rows, no
//                          "+N more", and the waiting figures equal the ones the WELCOME BACK
//                          popup showed one minute earlier (40,972 / 21,843 / 45,257). Also
//                          pins that each chip carries an AUTHORED verb/target split, so the
//                          modal draws two lines instead of "UPGRADE STONEYA...".
//   14 [stalled-collector-at-cap] the device's own 01:01 (headroom 0) and 01:03 (headroom
//                          2100) farm states: the banked figure is what MOVED, never what was
//                          held, and a zero bank prints "0".
//
// Markers: HARVEST_RESULT_SHAPE_OK / HARVEST_RESULT_SHAPE_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.HarvestResultShapeRegression.RunAll
//
// OWED, and deliberately not claimed here: this suite proves the DATA shape. That the
// three plates FIT at 2670x1200 and 1920x1080 with no ellipsis is a CAPTURE claim
// (HarvestOverflow_*.png) and the felt-verify is the owner's, per WO-1525 section 4.
// PanelRouter.IsRegistered is FALSE in EditMode (nothing registers a panel outside a
// play session), so case 5 asserts the id is DEFINED, not that it is live.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class HarvestResultShapeRegression
    {
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("HARVEST_RESULT_SHAPE_OK - " + reason);
            else Debug.LogError("HARVEST_RESULT_SHAPE_FAIL: " + reason);
        }

        // -- The owner's 2026-09-06 20:29 frame, verbatim -------------------------
        // Stone: 0 banked of 32,307, cap 3,000, NO Stoneyard built (WO-1525 section 2B).
        // Wood:  2,814 banked of 26,167, store lands on 26,000 / 26,000.
        // Iron:  792 banked of 13,083, store lands on 10,000 / 10,000.
        private static BankOverflowStatus Row(string name, string container, BankResource res,
                                              int granted, int requested, int current, int max,
                                              string source, bool overCap = false)
            => new BankOverflowStatus
            {
                Available = true,
                Resource = res,
                ResourceName = name,
                ContainerName = container,
                Requested = requested,
                Granted = granted,
                Lost = requested - granted,
                Current = current,
                Max = max,
                OverCap = overCap,
                Source = source,
            };

        private const string Collectors = "Collectors";

        /// <summary>The ECHO SILO's source tag - the second retaining producer, and the one whose
        /// rows the harvest result used to draw as extra rows instead of merging (case 13).
        /// Taken from the constant rather than retyped: HarvestResultVM.Retains matches on it.</summary>
        private const string Silo = HarvestOverflowModal.SiloSource;

        private static List<BankOverflowStatus> OwnerFrame() => new List<BankOverflowStatus>
        {
            Row("Stone", "Stoneyard",  BankResource.Food, 0,    32307, 3000,  3000,  Collectors),
            Row("Wood",  "Lumberyard", BankResource.Wood, 2814, 26167, 23186, 26000, Collectors),
            Row("Iron",  "Foundry",    BankResource.Iron, 792,  13083, 9208,  10000, Collectors),
        };

        /// <summary>Containers built, as the owner's save had them: no Stoneyard, one
        /// Lumberyard, two Foundries. This is the ONE live signal the VM takes.</summary>
        private static int BuiltFor(BankResource r)
        {
            if (r == BankResource.Wood) return 1;
            if (r == BankResource.Iron) return 2;
            return 0;
        }

        private static int CountOf(string haystack, string needle)
        {
            int n = 0, i = 0;
            while (true)
            {
                i = haystack.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return n;
                n++;
                i += needle.Length;
            }
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                var vm = HarvestResultVM.Build(OwnerFrame(), BuiltFor);

                // -- 1 [three-rows] ----------------------------------------------
                if (vm.Rows.Count != 3)
                    failures.Add("[three-rows] the owner's three-resource frame produced " + vm.Rows.Count +
                                 " row(s) instead of 3");
                else if (vm.Rows[0].ResourceName != "Stone" || vm.Rows[1].ResourceName != "Wood" ||
                         vm.Rows[2].ResourceName != "Iron")
                    failures.Add("[three-rows] the rows are not in the order the producer handed them in " +
                                 "(got " + vm.Rows[0].ResourceName + "/" + vm.Rows[1].ResourceName + "/" +
                                 vm.Rows[2].ResourceName + ")");

                if (vm.Rows.Count == 3)
                {
                    var stone = vm.Rows[0];
                    var wood = vm.Rows[1];
                    var iron = vm.Rows[2];

                    // -- 2 [banked-is-big] ---------------------------------------
                    if (wood.BankedText != "+2,814")
                        failures.Add("[banked-is-big] wood banked reads '" + wood.BankedText + "', expected '+2,814' " +
                                     "- the BANKED figure is the row's headline and it is grouped");
                    if (iron.BankedText != "+792")
                        failures.Add("[banked-is-big] iron banked reads '" + iron.BankedText + "', expected '+792'");
                    // A plus sign in front of nothing reads as a gain that did not happen.
                    if (stone.BankedText != "0")
                        failures.Add("[banked-is-big] a zero bank reads '" + stone.BankedText + "', expected '0' - " +
                                     "'+0' claims a gain the player did not get");

                    // -- 3 [waiting-survives] ------------------------------------
                    // WO-1525 section 3: do NOT shorten by deleting the waiting figure. It is the
                    // WO-1434 reassurance and the reason the screen is trusted.
                    if (wood.WaitingText.IndexOf("23,353", StringComparison.Ordinal) < 0)
                        failures.Add("[waiting-survives] the wood row lost its waiting figure (got '" +
                                     wood.WaitingText + "', expected it to carry 23,353)");
                    if (stone.WaitingText.IndexOf("32,307", StringComparison.Ordinal) < 0)
                        failures.Add("[waiting-survives] the stone row lost its waiting figure (got '" +
                                     stone.WaitingText + "')");
                    if (wood.WaitingText.IndexOf("waiting", StringComparison.OrdinalIgnoreCase) < 0 ||
                        !wood.Waits)
                        failures.Add("[waiting-survives] a COLLECTOR row does not say WAITING - the law word " +
                                     "(WO-1392/WO-1434) is waiting, never lost");
                    if (wood.Burned || stone.Burned || iron.Burned)
                        failures.Add("[waiting-survives] a collector row is marked BURNED; those units stay in " +
                                     "the collector (ResourceCollector.Collect only drains what banked)");

                    // -- 4 [state-is-a-word] -------------------------------------
                    for (int i = 0; i < vm.Rows.Count; i++)
                    {
                        var r = vm.Rows[i];
                        if (r.StateWord != "FULL")
                            failures.Add("[state-is-a-word] the " + r.ResourceName + " row is at cap but its state " +
                                         "word is '" + r.StateWord + "' - the owner is red/green colourblind, so " +
                                         "FULL is a WORD and never a hue");
                        if (r.StorageText.IndexOf("FULL", StringComparison.Ordinal) < 0)
                            failures.Add("[state-is-a-word] the " + r.ResourceName + " bar label '" + r.StorageText +
                                         "' does not carry the state word");
                    }
                    if (wood.StorageText.IndexOf("26,000 / 26,000", StringComparison.Ordinal) < 0)
                        failures.Add("[state-is-a-word] the wood bar reads '" + wood.StorageText + "' - expected the " +
                                     "AFTER figure 26,000 / 26,000 (WO-1392: Current is the PRE-grant wallet)");
                    if (wood.After != 26000 || Mathf.Abs(wood.Fill01 - 1f) > 0.0001f)
                        failures.Add("[state-is-a-word] the wood bar fill/after disagree with the label (after=" +
                                     wood.After + " fill=" + wood.Fill01 + ")");

                    // -- 5 [full-row-has-door] -----------------------------------
                    for (int i = 0; i < vm.Rows.Count; i++)
                    {
                        var r = vm.Rows[i];
                        if (!r.HasAction)
                        {
                            failures.Add("[full-row-has-door] the " + r.ResourceName + " row is FULL and offers no " +
                                         "door - a wall with no door is the whole complaint");
                            continue;
                        }
                        if (!Enum.IsDefined(typeof(PanelId), r.ActionDoor))
                            failures.Add("[full-row-has-door] the " + r.ResourceName + " door points at an UNDEFINED " +
                                         "PanelId (" + (int)r.ActionDoor + ")");
                        if (r.ActionDoor != PanelId.Manage || r.ActionContext != HarvestResultVM.BuildingsTab)
                            failures.Add("[full-row-has-door] the " + r.ResourceName + " door is '" + r.ActionDoor +
                                         "'/'" + r.ActionContext + "' - ManageScreenPanel.Open(string) accepts only " +
                                         "Defense/Buildings/Research/Troops and IGNORES anything else, which is a " +
                                         "door that half-opens");
                    }

                    // -- 6 [build-vs-upgrade] ------------------------------------
                    if (stone.ActionText != "BUILD STONEYARD")
                        failures.Add("[build-vs-upgrade] with NO Stoneyard built the stone door reads '" +
                                     stone.ActionText + "', expected 'BUILD STONEYARD' - offering UPGRADE for a " +
                                     "structure that does not exist is the worse miss");
                    if (wood.ActionText != "UPGRADE LUMBERYARD")
                        failures.Add("[build-vs-upgrade] with a Lumberyard built the wood door reads '" +
                                     wood.ActionText + "', expected 'UPGRADE LUMBERYARD'");

                    // A null signal must degrade to BUILD, never throw.
                    var noSignal = HarvestResultVM.Build(OwnerFrame(), null);
                    if (noSignal.Rows.Count != 3 || noSignal.Rows[1].ActionText != "BUILD LUMBERYARD")
                        failures.Add("[build-vs-upgrade] a NULL container signal did not degrade to BUILD (wood door " +
                                     "read '" + (noSignal.Rows.Count > 1 ? noSignal.Rows[1].ActionText : "<none>") + "')");

                    // -- 10 [no-paragraphs] --------------------------------------
                    for (int i = 0; i < vm.Rows.Count; i++)
                    {
                        var r = vm.Rows[i];
                        if (r.ResourceName.IndexOf('\n') >= 0 || r.BankedText.IndexOf('\n') >= 0 ||
                            r.WaitingText.IndexOf('\n') >= 0 || r.StorageText.IndexOf('\n') >= 0 ||
                            r.ActionText.IndexOf('\n') >= 0)
                            failures.Add("[no-paragraphs] the " + r.ResourceName + " row carries a newline inside a " +
                                         "field - a row is FIELDS, and prose is what WO-1525 removed");
                        if (r.WaitingText.Length > 40)
                            failures.Add("[no-paragraphs] the " + r.ResourceName + " waiting field is " +
                                         r.WaitingText.Length + " chars ('" + r.WaitingText + "') - it has grown back " +
                                         "into a sentence");
                    }
                }

                // -- 7 [said-once] -----------------------------------------------
                string all = vm.AllText();
                int reassurances = CountOf(all, "nothing was lost");
                if (reassurances != 1)
                    failures.Add("[said-once] 'nothing was lost' appears " + reassurances + " time(s) on the whole " +
                                 "screen, expected exactly 1 - the old body said it once PER RESOURCE");
                if (!vm.FooterReassures || string.IsNullOrEmpty(vm.FooterLine))
                    failures.Add("[said-once] every row on this frame RETAINED its units, yet the footer does not " +
                                 "reassure (footer='" + vm.FooterLine + "')");

                // -- 8 [burn-never-lies] -----------------------------------------
                // OfflineHarvestService.Grant genuinely discards its pre-clamp accrual, so a row
                // from that path must NOT be covered by the reassurance. This is WO-1434's rule
                // read in the other direction, and it is the one the shorter screen could break.
                var burned = HarvestResultVM.Build(new List<BankOverflowStatus>
                {
                    Row("Wood", "Lumberyard", BankResource.Wood, 100, 500, 3900, 4000, "OfflineHarvest"),
                }, BuiltFor);
                if (burned.Rows.Count != 1)
                {
                    failures.Add("[burn-never-lies] the burn fixture produced " + burned.Rows.Count + " row(s)");
                }
                else
                {
                    var b = burned.Rows[0];
                    if (!b.Burned || b.Waits)
                        failures.Add("[burn-never-lies] an OfflineHarvest row is marked as waiting; that path drops " +
                                     "its pre-clamp accrual on the floor");
                    if (b.WaitingText.IndexOf("lost", StringComparison.OrdinalIgnoreCase) < 0)
                        failures.Add("[burn-never-lies] the burned row's second figure reads '" + b.WaitingText +
                                     "' and never says the units were lost");
                    if (burned.FooterReassures ||
                        CountOf(burned.AllText(), "nothing was lost") != 0)
                        failures.Add("[burn-never-lies] the reassurance footer is shown over a row that BURNED - " +
                                     "that is the WO-1434 lie inverted, and it is worse than the long copy");
                }

                // -- 9 [overcap-spends] ------------------------------------------
                var over = HarvestResultVM.Build(new List<BankOverflowStatus>
                {
                    Row("Wood", "Lumberyard", BankResource.Wood, 0, 300, 5000, 4000, Collectors, overCap: true),
                }, BuiltFor);
                if (over.Rows.Count != 1 || over.Rows[0].StateWord != "OVER")
                    failures.Add("[overcap-spends] an OVER-CAP row does not say OVER - it is a DIFFERENT situation " +
                                 "from a full bank (BankOverflowStatus.OverCap) and must not read the same");
                else if (over.Rows[0].ActionText != "SPEND WOOD")
                    failures.Add("[overcap-spends] the over-cap door reads '" + over.Rows[0].ActionText +
                                 "', expected 'SPEND WOOD' - more storage is not the fix above the cap");

                // -- 11 [row-cap] ------------------------------------------------
                // (!) THE RESOURCES MUST BE DISTINCT, and that is the WO-1525b merge speaking.
                // This case used to build MaxRows+2 statuses all carrying BankResource.Wood, which
                // was fine while the VM drew one row per STATUS. It no longer is: HarvestResultVM
                // .Merge folds by resource, so five Wood statuses are now ONE Wood row - the fixture
                // would have been asserting the cap against a screen that had one row on it.
                // BankResource has exactly five members (TownBankCapacity.cs:151-158), which is
                // MaxRows + 2 today; if MaxRows ever rises past 3 this loop runs out of resources
                // and that is a REAL signal, not a fixture bug - the tail line would be unreachable.
                var allResources = new[]
                {
                    BankResource.Wood, BankResource.Iron, BankResource.Food,
                    BankResource.Crystals, BankResource.Coins,
                };
                var many = new List<BankOverflowStatus>();
                for (int i = 0; i < HarvestResultVM.MaxRows + 2 && i < allResources.Length; i++)
                    many.Add(Row("Res" + i, "Store", allResources[i], 10, 100, 990, 1000, Collectors));
                var capped = HarvestResultVM.Build(many, BuiltFor);
                if (capped.Rows.Count != HarvestResultVM.MaxRows)
                    failures.Add("[row-cap] " + many.Count + " resources drew " + capped.Rows.Count + " rows; the cap " +
                                 "is " + HarvestResultVM.MaxRows + " because a fifth plate puts every door under " +
                                 "MinTouchPx");
                if (capped.OverflowLine != "+2 more")
                    failures.Add("[row-cap] the collapsed remainder reads '" + capped.OverflowLine +
                                 "', expected '+2 more'");
                if (capped.TotalRowCount != many.Count)
                    failures.Add("[row-cap] the VM forgot how many resources it was handed (TotalRowCount=" +
                                 capped.TotalRowCount + ")");

                // =====================================================================
                //  -- 13 [one-row-per-resource] -- THE OWNER'S TWO FRAMES, RECONCILED.
                // ---------------------------------------------------------------------
                //  Build 358872, Seeker, one minute apart:
                //    Logs/device/screens/owner-harvest-20260907-011321.png  (WELCOME BACK)
                //        WOOD +2906 / 40972 MORE WAITS   <- MORE WAITS is r.Waits (Pending - Banks),
                //        IRON +1535 / 21843 MORE WAITS      NOT the pending pool. Off-by-Banks trap.
                //        STONE 45257 WAITING / STORAGE FULL - STAYS PUT
                //    Logs/device/screens/owner-screen-20260907-011426.png   (HARVEST RESULT)
                //        WOOD  +2,906 / 12,236 waiting   IRON +1,535 / 6,035   STONE 0 / 30,932
                //        "+3 more"
                //  The BANKED figures agreed to the unit; the WAITING figures did not, because the
                //  harvest result received SIX statuses (three collector + three Echo silo) and
                //  drew only the first three, collapsing the silo half into "+3 more". Merged, the
                //  collector remainders (12,236 / 6,035 / 30,932) plus the silo shares (28,736 /
                //  15,808 / 14,325) give 40,972 / 21,843 / 45,257 - the welcome-back column, to the
                //  unit, summing to its own 108,072 footer. The welcome
                //  back screen had merged both producers per resource all along
                //  (OfflineHarvestService.BuildReturnRows sums FromCollectors + FromSilo).
                //
                //  This case drives the SIX statuses the device produced and asserts the merged
                //  screen now reports the WELCOME BACK figures - to the unit, from the same seam.
                //  RED BY: deleting the HarvestResultVM.Merge call in Build (the pre-fix tree
                //  yields six rows, "+3 more", and wood waiting = 12,236 instead of 40,972).
                // =====================================================================
                const int WoodBanked = 2906, IronBanked = 1535, StoneBanked = 0;
                var device = new List<BankOverflowStatus>
                {
                    // Collector rows: Requested = the pending snapshot, Current = store BEFORE the tap.
                    Row("Wood",  "Lumberyard", BankResource.Wood, WoodBanked,  15142, 23094, 26000, Collectors),
                    Row("Iron",  "Foundry",    BankResource.Iron, IronBanked,   7570,  8465, 10000, Collectors),
                    Row("Stone", "Stoneyard",  BankResource.Food, StoneBanked, 30932,  3000,  3000, Collectors),
                    // Echo silo rows: the SAME three resources, measured after the collectors banked.
                    Row("Wood",  "Lumberyard", BankResource.Wood, 0, 28736, 26000, 26000, Silo),
                    Row("Iron",  "Foundry",    BankResource.Iron, 0, 15808, 10000, 10000, Silo),
                    Row("Stone", "Stoneyard",  BankResource.Food, 0, 14325,  3000,  3000, Silo),
                };
                var dev = HarvestResultVM.Build(device, BuiltFor);
                if (dev.TotalRowCount != 3 || dev.Rows.Count != 3)
                    failures.Add("[one-row-per-resource] six producer statuses over THREE resources drew " +
                                 dev.Rows.Count + " row(s) of " + dev.TotalRowCount + " - the Echo silo half is a " +
                                 "second row for a resource the player already has one for");
                if (!string.IsNullOrEmpty(dev.OverflowLine))
                    failures.Add("[one-row-per-resource] the merged screen still shows '" + dev.OverflowLine +
                                 "' - on the owner's frame that line hid the OTHER HALF of the same three " +
                                 "resources, not a fourth resource. Only Wood/Iron/Food can overflow at all " +
                                 "(TownBankCapacity.UncappableResources), so after the merge it is unreachable");
                if (dev.SourceStatusCount != 6)
                    failures.Add("[one-row-per-resource] the VM did not record all six producer statuses " +
                                 "(SourceStatusCount=" + dev.SourceStatusCount + ") - the trace cannot name the merge");
                if (dev.Rows.Count == 3)
                {
                    // (a) THE BANKED FIGURE IS THE ONE THE BANK ACTUALLY MOVED. The screen's headline
                    //     is composed from the SAME clamp events the grant used - never re-derived.
                    var expectBanked = new[] { "+2,906", "+1,535", "0" };
                    var expectWaiting = new[] { "40,972 waiting, safe", "21,843 waiting, safe", "45,257 waiting, safe" };
                    var expectName = new[] { "Wood", "Iron", "Stone" };
                    for (int i = 0; i < 3; i++)
                    {
                        if (dev.Rows[i].ResourceName != expectName[i])
                            failures.Add("[one-row-per-resource] row " + i + " is '" + dev.Rows[i].ResourceName +
                                         "', expected '" + expectName[i] + "' - the merge must keep rail order");
                        if (dev.Rows[i].BankedText != expectBanked[i])
                            failures.Add("[one-row-per-resource] row " + i + " banked '" + dev.Rows[i].BankedText +
                                         "', expected '" + expectBanked[i] + "' - the screen must state what BANKED");
                        if (dev.Rows[i].WaitingText != expectWaiting[i])
                            failures.Add("[one-row-per-resource] row " + i + " waiting '" + dev.Rows[i].WaitingText +
                                         "', expected '" + expectWaiting[i] + "' - this is the figure the WELCOME " +
                                         "BACK screen showed one minute earlier; the two screens must agree");
                    }
                    // (b) SCREEN == BANKED DELTAS. The store figure the player reads is the pre-tap
                    //     wallet plus exactly what banked - not a fourth independently-read number.
                    var afters = new[] { 23094 + WoodBanked, 8465 + IronBanked, 3000 + StoneBanked };
                    for (int i = 0; i < 3; i++)
                        if (dev.Rows[i].After != afters[i])
                            failures.Add("[one-row-per-resource] row " + i + " shows a store of " + dev.Rows[i].After +
                                         " but the pre-tap wallet plus the banked delta is " + afters[i]);
                    // (c) EVERY ROW IS FULL, SO EVERY ROW CARRIES ONE DOOR - and no door truncates:
                    //     the verb and the target are AUTHORED apart so the chip draws two lines.
                    for (int i = 0; i < 3; i++)
                    {
                        var r = dev.Rows[i];
                        if (r.StateWord != "FULL")
                            failures.Add("[one-row-per-resource] row " + i + " does not say the WORD FULL");
                        if (!r.HasAction) failures.Add("[one-row-per-resource] the blocked row " + i + " carries no door");
                        else if (string.IsNullOrEmpty(r.ActionVerb) || string.IsNullOrEmpty(r.ActionTarget) ||
                                 r.ActionText != r.ActionVerb + " " + r.ActionTarget)
                            failures.Add("[one-row-per-resource] row " + i + " chip '" + r.ActionText + "' has no " +
                                         "authored break point (verb='" + r.ActionVerb + "' target='" + r.ActionTarget +
                                         "') - the modal then draws one line and ellipsizes it, which is how " +
                                         "'UPGRADE STONEYA...' reached the owner");
                        if (r.MergedSources != 2)
                            failures.Add("[one-row-per-resource] row " + i + " folded " + r.MergedSources +
                                         " producer(s), expected 2 (collectors + Echo silo)");
                    }
                    // (d) NOTHING BURNED HERE, so the reassurance is the footer - once.
                    if (!dev.FooterReassures || CountOf(dev.AllText(), "nothing was lost") != 1)
                        failures.Add("[one-row-per-resource] both producers RETAIN (WO-1392 collectors, WO-1434 silo) " +
                                     "yet the screen does not carry the reassurance exactly once");
                }

                // =====================================================================
                //  -- 14 [stalled-collector-at-cap] -- THE OWNER'S 01:01/01:03 STATE.
                // ---------------------------------------------------------------------
                //  Device, build 358872: "auto-overflow 'farm': storage for Food has NO ROOM
                //  (headroom=0) - 0 moved, 32307 STAYS PENDING here and the collector STALLS at
                //  32308/32308", then two minutes later "moved 2100 Food -> storage (held 32307,
                //  storage headroom 2100, asked 2100); 30207 LEFT PENDING".
                //  Both are COLLECTOR rows, so both retain - and the screen must say the banked
                //  figure is the amount that MOVED, never the amount that was held.
                // =====================================================================
                var noRoom = HarvestResultVM.Build(new List<BankOverflowStatus>
                {
                    Row("Stone", "Stoneyard", BankResource.Food, 0, 32307, 3000, 3000, Collectors),
                }, BuiltFor);
                if (noRoom.Rows.Count != 1 || noRoom.Rows[0].BankedText != "0" ||
                    noRoom.Rows[0].WaitingText != "32,307 waiting, safe" ||
                    noRoom.Rows[0].StorageText != "3,000 / 3,000  FULL")
                    failures.Add("[stalled-collector-at-cap] headroom 0 must read banked '0' (never '+0'), " +
                                 "'32,307 waiting, safe' and '3,000 / 3,000  FULL'; got '" +
                                 (noRoom.Rows.Count == 1 ? noRoom.Rows[0].BankedText + "' / '" +
                                  noRoom.Rows[0].WaitingText + "' / '" + noRoom.Rows[0].StorageText : "no row") + "'");
                var someRoom = HarvestResultVM.Build(new List<BankOverflowStatus>
                {
                    Row("Stone", "Stoneyard", BankResource.Food, 2100, 32307, 900, 3000, Collectors),
                }, BuiltFor);
                if (someRoom.Rows.Count != 1 || someRoom.Rows[0].BankedText != "+2,100" ||
                    someRoom.Rows[0].WaitingText != "30,207 waiting, safe" ||
                    someRoom.Rows[0].After != 3000 || someRoom.Rows[0].StateWord != "FULL")
                    failures.Add("[stalled-collector-at-cap] headroom 2100 must read banked '+2,100', " +
                                 "'30,207 waiting, safe' and a store of 3000 marked FULL - the device moved exactly " +
                                 "2100 and left 30207 pending, and the screen must state the MOVED figure");

                // -- 12 [ascii-only] ---------------------------------------------
                string sweep = all + "\n" + burned.AllText() + "\n" + over.AllText() + "\n" + capped.AllText() +
                               "\n" + dev.AllText() + "\n" + noRoom.AllText() + "\n" + someRoom.AllText() +
                               "\n" + vm.TraceLine + "\n" + dev.ScreenText;
                for (int i = 0; i < sweep.Length; i++)
                {
                    char ch = sweep[i];
                    if (ch > 126 || (ch < 32 && ch != '\n'))
                    {
                        failures.Add("[ascii-only] the harvest result carries the non-ASCII character U+" +
                                     ((int)ch).ToString("X4") + " - it renders as tofu in the mobile atlas " +
                                     "(InvariantCulture grouping exists to prevent exactly this)");
                        break;
                    }
                }

                // An empty batch must produce an empty screen, never an exception or an empty state.
                var none = HarvestResultVM.Build(null, BuiltFor);
                if (none.Rows.Count != 0 || !string.IsNullOrEmpty(none.FooterLine))
                    failures.Add("[said-once] a null batch produced content (" + none.Rows.Count + " rows, footer='" +
                                 none.FooterLine + "')");

                // -- 15 [one-close-owner] --------------------------------------
                const string modalPath = "Assets/_Modules/Core/UI/HarvestOverflowModal.cs";
                string modalSource = File.Exists(modalPath) ? File.ReadAllText(modalPath) : null;
                if (modalSource == null)
                    failures.Add("[one-close-owner] could not read " + modalPath);
                else
                {
                    if (modalSource.IndexOf("BuildObsidianModal(\"HarvestOverflowUI\", \"HARVEST RESULT\"",
                            StringComparison.Ordinal) < 0)
                        failures.Add("[one-close-owner] Harvest Result no longer uses the shared obsidian modal owner");
                    if (modalSource.IndexOf("ElarionUiKit.Button(content, \"Close\"", StringComparison.Ordinal) >= 0)
                        failures.Add("[one-close-owner] Harvest Result manually builds a second Close inside content; " +
                                     "BuildObsidianModal already owns the one shared close face");
                }
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            reason = failures.Count == 0
                ? "HARVEST RESULT SHAPE OK - three rows for three resources (SIX producer statuses merge to THREE, so " +
                  "the harvest result and the welcome-back popup report the same waiting figures to the unit), the " +
                  "banked figure is the headline and equals what the bank moved, FULL is a word, every blocked row " +
                  "carries one door with an authored two-line face, 'nothing was lost' is said exactly once and never " +
                  "over a burned row, and every string is ASCII"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }
    }
}
