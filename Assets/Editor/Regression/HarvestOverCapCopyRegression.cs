// =============================================================================
// HarvestOverCapCopyRegression [harvest-overcap-copy]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core).
//
// WO-1099. THE HARVEST RESULT REASSURED THE PLAYER IN THE ONE STATE WHERE THE
// TRUTH MATTERED.
//
// THE MEASURED DEFECT - owner F8 flag seq=4974, 2026-09-09 13:06:17, note "here",
// frame docs/ui-evidence/harvest-result-2026-09-09/flag-seq4974-harvest-result.png:
//
//     Wood   0    732,031 / 34,000  OVER    4,213 waiting, safe
//     Iron   0    639,336 / 34,000  OVER    2,031 waiting, safe
//     Stone  0    411,480 / 34,000  OVER    4,441 waiting, safe
//     footer: "Nothing was lost - every waiting unit banks as soon as there is room."
//
// 34,000 is the L6 container ceiling - the largest storage the game can ever hold -
// so 732,031 is 21x the maximum. "as soon as there is room" is therefore a promise
// that cannot be kept: no build makes room, only spending does. Printed over a
// banked column of 0, the screen read "you collected nothing, and nothing is wrong."
//
// !! THIS SUITE DRIVES HarvestResultVM.Build - THE PURE SEAM. No canvas, no scene,
// no PlayMode, nothing to restore. The View (HarvestOverflowModal) is a dumb skin and
// is not touched by this ticket or asserted by this suite.
//
// !! AND IT ASSERTS SPEND, NEVER "SPEND OR UPGRADE". Above the cap more storage is
// not the fix - BankOverflowStatus.OverCap says so at length, the bank's over-cap
// Warn deliberately drops the "build or upgrade a {container}" clause it prints when
// merely full, and HarvestResultShapeRegression [overcap-spends] already fails the
// build if the door reads anything else. An "upgrade" offer here would be the same
// false promise wearing a different hat.
//
// Cases:
//   1 [overcap-numbers]    banked / pending / over-by are exposed as NUMBERS per row,
//                          equal to the bank's own figures (banked 0 above the cap is
//                          COMPUTED - TownBankCapacity logs "added 0" by design).
//   2 [no-false-comfort]   the over-cap footer contains NEITHER "nothing was lost" NOR
//                          "as soon as there is room", and FooterReassures is false.
//   3 [leads-with-action]  the footer's FIRST word is the verb, it says "spend", and it
//                          names every over-cap container as a cap.
//   4 [safety-survives]    WO-1434 is not traded away: the pending total is still on the
//                          footer and still called safe - just no longer first.
//   5 [ascii-only]         every produced character is ASCII (non-ASCII = device tofu).
//   6 [full-still-reassures] the AT-CAP frame is UNCHANGED - it still reassures exactly
//                          once. Proves this ticket did not break
//                          HarvestResultShapeRegression [said-once], which is a file this
//                          lane does not own.
//   7 [trace-names-amounts] TraceLine carries banked=/pending=/overBy= and footer='overcap'
//                          (CLAUDE.md section 12 - the instrument names the numbers).
//
// Markers: HARVEST_OVERCAP_COPY_OK / HARVEST_OVERCAP_COPY_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.HarvestOverCapCopyRegression.RunAll
//
// OWED, and deliberately not claimed: this lane ran NO Unity. Every case below was
// authored RED against HEAD (the fields it reads did not exist and the footer branch
// it asserts did not exist), but the RED was never OBSERVED - see the WO-1099 RESULT.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class HarvestOverCapCopyRegression
    {
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("HARVEST_OVERCAP_COPY_OK - " + reason);
            else Debug.LogError("HARVEST_OVERCAP_COPY_FAIL: " + reason);
        }

        private const string Collectors = "Collectors";

        /// <summary>The L6 container ceiling the owner's frame was measured against.</summary>
        private const int L6Ceiling = 34000;

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

        /// <summary>
        /// THE OWNER'S seq=4974 FRAME, VERBATIM. Wood 732,031 / Iron 639,336 / Stone 411,480, all
        /// against the 34,000 L6 ceiling, all banking 0, all still holding their waiting units in
        /// the collectors. Requested = granted + waiting, which is what the clamp weighed.
        /// </summary>
        private static List<BankOverflowStatus> OverCapFrame() => new List<BankOverflowStatus>
        {
            Row("Wood",  "Lumberyard", BankResource.Wood, 0, 4213, 732031, L6Ceiling, Collectors, overCap: true),
            Row("Iron",  "Foundry",    BankResource.Iron, 0, 2031, 639336, L6Ceiling, Collectors, overCap: true),
            Row("Stone", "Stoneyard",  BankResource.Food, 0, 4441, 411480, L6Ceiling, Collectors, overCap: true),
        };

        /// <summary>The AT-CAP control (WO-1525's own owner frame): full, not over. It must keep
        /// the reassurance, because at a full bank room really does arrive by spending or upgrading
        /// and those rows carry that door.</summary>
        private static List<BankOverflowStatus> FullFrame() => new List<BankOverflowStatus>
        {
            Row("Wood", "Lumberyard", BankResource.Wood, 2814, 26167, 23186, 26000, Collectors),
            Row("Iron", "Foundry",    BankResource.Iron, 792,  13083, 9208,  10000, Collectors),
        };

        private static int BuiltFor(BankResource r) => r == BankResource.Wood ? 1 : 0;

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                var vm = HarvestResultVM.Build(OverCapFrame(), BuiltFor);
                if (vm == null || vm.Rows.Count != 3)
                {
                    reason = "[harvest-overcap-copy] the owner's three-resource over-cap frame produced " +
                             (vm == null ? "a null VM" : vm.Rows.Count + " row(s)") + ", expected 3";
                    return false;
                }

                string footer = vm.FooterLine ?? string.Empty;

                // -- 1 [overcap-numbers] -----------------------------------------
                // The three amounts, per row, as NUMBERS. RED against HEAD: BankedUnits,
                // PendingUnits and OverCapUnits did not exist, so this case did not compile.
                var expectBanked  = new[] { 0, 0, 0 };
                var expectPending = new[] { 4213, 2031, 4441 };
                var expectOverBy  = new[] { 732031 - L6Ceiling, 639336 - L6Ceiling, 411480 - L6Ceiling };
                for (int i = 0; i < vm.Rows.Count; i++)
                {
                    var r = vm.Rows[i];
                    if (r.BankedUnits != expectBanked[i])
                        failures.Add("[overcap-numbers] " + r.ResourceName + " banked reads " + r.BankedUnits +
                                     ", expected " + expectBanked[i] + " - the bank's own answer above the cap " +
                                     "(TownBankCapacity logs 'added 0' by design), not a default");
                    if (r.PendingUnits != expectPending[i])
                        failures.Add("[overcap-numbers] " + r.ResourceName + " pending reads " + r.PendingUnits +
                                     ", expected " + expectPending[i] + " - a panel that banks 0 must at least " +
                                     "say what is still held");
                    if (r.OverCapUnits != expectOverBy[i])
                        failures.Add("[overcap-numbers] " + r.ResourceName + " over-by reads " + r.OverCapUnits +
                                     ", expected " + expectOverBy[i] + " - the bar said OVER without ever saying " +
                                     "BY HOW MUCH");
                    if (!r.OverCap)
                        failures.Add("[overcap-numbers] " + r.ResourceName + " lost the OverCap flag on the way " +
                                     "through the merge");
                    if (r.OverCapText.IndexOf(HarvestResultVM.N(expectOverBy[i]), StringComparison.Ordinal) < 0)
                        failures.Add("[overcap-numbers] " + r.ResourceName + " over-cap text '" + r.OverCapText +
                                     "' does not carry its own figure");
                }
                if (vm.TotalOverCap != expectOverBy[0] + expectOverBy[1] + expectOverBy[2])
                    failures.Add("[overcap-numbers] the footer's over-by total is " + vm.TotalOverCap +
                                 ", which is not the sum of the rows - the one sentence must be true about the " +
                                 "whole harvest");
                if (vm.TotalPending != expectPending[0] + expectPending[1] + expectPending[2])
                    failures.Add("[overcap-numbers] the footer's pending total is " + vm.TotalPending +
                                 ", which is not the sum of the rows");
                if (vm.TotalBanked != 0)
                    failures.Add("[overcap-numbers] the footer's banked total is " + vm.TotalBanked +
                                 ", expected 0 - nothing banks above the cap");

                // -- 2 [no-false-comfort] ----------------------------------------
                // THE TICKET. Both banned phrases are the owner's frame, verbatim.
                string all = vm.AllText() ?? string.Empty;
                if (all.IndexOf("nothing was lost", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[no-false-comfort] the over-cap screen still says 'nothing was lost' - it is " +
                                 "true of the WAITING units and reads as 'nothing is wrong' under a banked " +
                                 "column of 0");
                if (all.IndexOf("as soon as there is room", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[no-false-comfort] the over-cap screen promises the units bank 'as soon as " +
                                 "there is room' at 21x the L6 ceiling - there is no room and no build can make " +
                                 "any; only spending can");
                if (vm.FooterReassures)
                    failures.Add("[no-false-comfort] FooterReassures is true above the cap");
                if (!vm.FooterOverCap)
                    failures.Add("[no-false-comfort] the over-cap footer branch did not fire (footer='" +
                                 footer + "')");

                // -- 3 [leads-with-action] ---------------------------------------
                // WHAT TO DO, FIRST. Not a state description with an action buried behind it.
                if (!footer.StartsWith("Spend", StringComparison.Ordinal))
                    failures.Add("[leads-with-action] the over-cap footer opens '" +
                                 (footer.Length > 24 ? footer.Substring(0, 24) : footer) +
                                 "' - the player is blocked, so the first word is the verb that unblocks them");
                if (footer.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    footer.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[leads-with-action] the over-cap footer offers storage ('" + footer + "') - " +
                                 "above the cap more storage is not the fix (BankOverflowStatus.OverCap), and " +
                                 "selling it here is the false promise again in a new costume");
                foreach (var container in new[] { "Lumberyard", "Foundry", "Stoneyard" })
                    if (footer.IndexOf(container, StringComparison.Ordinal) < 0)
                        failures.Add("[leads-with-action] the over-cap footer never names the " + container +
                                     " - the container is the ceiling the player is fighting, so it is named " +
                                     "even though it is not the thing to buy");
                if (footer.IndexOf(HarvestResultVM.N(vm.TotalOverCap), StringComparison.Ordinal) < 0)
                    failures.Add("[leads-with-action] the over-cap footer never says HOW FAR over the player is");
                for (int i = 0; i < vm.Rows.Count; i++)
                    if (vm.Rows[i].ActionText != "SPEND " + vm.Rows[i].ResourceName.ToUpperInvariant())
                        failures.Add("[leads-with-action] the " + vm.Rows[i].ResourceName + " door reads '" +
                                     vm.Rows[i].ActionText + "', expected SPEND");

                // -- 4 [safety-survives] -----------------------------------------
                // WO-1434 is not traded away to fix WO-1099. The units ARE safe; the screen just
                // no longer says so before saying what is wrong.
                if (footer.IndexOf(HarvestResultVM.N(vm.TotalPending), StringComparison.Ordinal) < 0)
                    failures.Add("[safety-survives] the over-cap footer dropped the waiting total - the units " +
                                 "are genuinely retained (WO-1392 collectors) and refusing to say so is the " +
                                 "WO-1434 lie pointed the other way");
                if (footer.IndexOf("safe", StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add("[safety-survives] the over-cap footer never calls the waiting units safe");
                for (int i = 0; i < vm.Rows.Count; i++)
                    if (!vm.Rows[i].Waits || vm.Rows[i].Burned)
                        failures.Add("[safety-survives] the " + vm.Rows[i].ResourceName + " row is no longer " +
                                     "marked as retained - the collectors keep what the cap refused");

                // -- 5 [ascii-only] ----------------------------------------------
                foreach (char c in all + footer)
                    if (c > 126 || (c < 32 && c != '\n'))
                    {
                        failures.Add("[ascii-only] the over-cap screen carries U+" +
                                     ((int)c).ToString("X4") + " - it renders as tofu in the mobile atlas");
                        break;
                    }

                // -- 6 [full-still-reassures] ------------------------------------
                // THE CONTROL, and the proof this lane did not break a suite it does not own
                // (HarvestResultShapeRegression [said-once], whose fixture is at-cap, not over).
                var full = HarvestResultVM.Build(FullFrame(), BuiltFor);
                if (full == null || !full.FooterReassures || full.FooterOverCap)
                    failures.Add("[full-still-reassures] an AT-CAP frame no longer reassures (footer='" +
                                 (full == null ? "<null vm>" : full.FooterLine) + "') - at a full bank room " +
                                 "really does arrive by spending or upgrading, and those rows carry that door");
                else if (CountOf(full.AllText(), "nothing was lost") != 1)
                    failures.Add("[full-still-reassures] the AT-CAP reassurance is said " +
                                 CountOf(full.AllText(), "nothing was lost") + " time(s), expected exactly 1");

                // -- 7 [trace-names-amounts] -------------------------------------
                string trace = vm.TraceLine ?? string.Empty;
                foreach (var token in new[] { "banked=0", "pending=" + vm.TotalPending,
                                              "overBy=" + vm.TotalOverCap, "footer='overcap'" })
                    if (trace.IndexOf(token, StringComparison.Ordinal) < 0)
                        failures.Add("[trace-names-amounts] the trace line '" + trace + "' does not carry '" +
                                     token + "' - CLAUDE.md section 12: the instrument names the numbers, or " +
                                     "the next seat re-derives them from a screenshot");
            }
            catch (Exception ex)
            {
                failures.Add("[harvest-overcap-copy] threw: " + ex.GetType().Name + " - " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = string.Join(" | ", failures.ToArray());
                return false;
            }
            reason = "over-cap harvest result: 7/7 (numbers exposed, no false comfort, leads with SPEND, " +
                     "safety kept, ascii, at-cap control unchanged, trace names amounts)";
            return true;
        }

        private static int CountOf(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
