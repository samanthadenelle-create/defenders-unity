// =============================================================================
// RaidCasualtyRegression — WO-1810. WHAT LOSING A RAID COSTS, pinned.
// -----------------------------------------------------------------------------
// Marker: RAID_CASUALTY_OK / RAID_CASUALTY_FAIL.
//
// OWNER RULING 2026-09-16 ~20:05, verbatim: "there is no cost to losing a raid" /
// "troops return a few injured which has no cost or time" / "loss should lose
// troops and then rebuild" / "maybe lose 100 on fail, lose 60% on retreat" / "but
// any troop killed is dead so 60% of whats left".
//
// WHY AN ORACLE AND NOT A FELT-TEST: the defect this closes was INVISIBLE from the
// chair. On the owner's Seeker (build 372984, 19:59:01) three troops died, the
// reconcile said "wounded 3 ... recovery 1200s", and one minute later the army
// screen read "Army is full. 10/10 slots used". Nothing was broken on screen -
// the loss simply cost nothing. A regression that a wipe removes troops is the
// only thing that notices if this quietly reverts to wound-only.
//
// FOUR CASES:
//   A. the PURE policy - fail / retreat / victory, and the rounding table
//   B. the rounding EDGES - the floor of one, pct 0, pct 100, an empty remainder
//   C. the ArmyStorage mutation END TO END on a fake army (this IS the end-to-end:
//      ReconcileRaidEnd needs a scene + a deploy ledger, so the removal itself is
//      the pure seam and is asserted here against a real roster)
//   D. source-lint - ReconcileRaidEnd still calls the policy AND the removal, and
//      the exits still DECLARE their outcome. A future seat cannot silently go
//      back to wounding.
//
// Pure logic + source-lint. No PlayMode, no scene, no network. Never throws.
// Wire (DataRegression.RunAll):
//   if (!RaidCasualtyRegression.Run(out var r)) failures.Add(r);
//   else log.AppendLine("[raid-casualty] " + r);
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Headless oracle for WO-1810: the raid casualty policy, its rounding, the roster
    /// mutation and the call sites that must keep using them. Never throws.
    /// </summary>
    public static class RaidCasualtyRegression
    {
        private const string DeployRel  = "_Modules/Village/Troops/RaidDeployController.cs";
        private const string VictoryRel = "_Modules/Village/World/Camps/RaidVictoryController.cs";

        // The RULED rates, stated here independently of the code (the same contract
        // RemoteTunablesDefaultsRegression keeps): a change of policy must be a change in
        // two places, never a silent one.
        private const int FailPct = 100;
        private const int RetreatPct = 60;

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== RaidCasualtyRegression: what losing a raid costs (WO-1810) ===");

            try
            {
                CaseA_PurePolicy(failures, log);
                CaseB_RoundingEdges(failures, log);
                CaseC_RosterMutation(failures, log);
                CaseD_CallSites(failures, log);
                CaseE_VictoryScreenStatesTheCost(failures, log);
                CaseF_FailPaysNoLoot(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("RaidCasualtyRegression threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "RAID CASUALTY OK - killed stay dead, a failed raid loses the whole warband, " +
                         "a retreat loses " + RetreatPct + "% of the survivors (nearest, floor of one), " +
                         "victory keeps them; the roster mutation and both call sites are wired";
                Debug.Log("RAID_CASUALTY_OK\n" + log);
                return true;
            }

            reason = "RAID CASUALTY: " + failures.Count + " failure(s): " + string.Join(" | ", failures.ToArray());
            Debug.LogError("RAID_CASUALTY_FAIL: " + reason + "\n" + log);
            return false;
        }

        // ── A. the pure policy ────────────────────────────────────────────────
        private static void CaseA_PurePolicy(List<string> f, StringBuilder log)
        {
            log.AppendLine("[case A] the pure policy across the three outcomes");

            // The captured raid: 10 deployed, 7 survivors, clock expired -> a FAIL.
            var fail = RaidCasualtyPolicy.Decide(10, 7, RaidExitOutcome.Failed, FailPct, RetreatPct);
            Expect(f, "fail killed", 3, fail.Killed);
            Expect(f, "fail lostByPolicy (100% of 7 survivors)", 7, fail.LostByPolicy);
            Expect(f, "fail returned", 0, fail.Returned);
            Expect(f, "fail totalLost", 10, fail.TotalLost);

            // The same raid, left by the player's own Retreat: 60% of the 7 survivors.
            var retreat = RaidCasualtyPolicy.Decide(10, 7, RaidExitOutcome.Retreat, FailPct, RetreatPct);
            Expect(f, "retreat killed", 3, retreat.Killed);
            Expect(f, "retreat lostByPolicy (60% of 7 -> nearest)", 4, retreat.LostByPolicy);
            Expect(f, "retreat returned", 3, retreat.Returned);
            Expect(f, "retreat totalLost", 7, retreat.TotalLost);

            // VICTORY: the killed are still dead ("any troop killed is dead"), survivors keep.
            var win = RaidCasualtyPolicy.Decide(10, 7, RaidExitOutcome.Victory, FailPct, RetreatPct);
            Expect(f, "victory killed (still dead)", 3, win.Killed);
            Expect(f, "victory lostByPolicy", 0, win.LostByPolicy);
            Expect(f, "victory returned", 7, win.Returned);

            // UNDECLARED resolves to the FAIL rate - the ruling's default, and the shape the
            // hero-death settlement relies on (it declares nothing on purpose).
            var undeclared = RaidCasualtyPolicy.Decide(10, 7, RaidExitOutcome.Undeclared, FailPct, RetreatPct);
            if (undeclared.LostByPolicy != fail.LostByPolicy)
                f.Add("[A] an UNDECLARED outcome priced differently from a FAIL (" +
                      undeclared.LostByPolicy + " vs " + fail.LostByPolicy + "). The owner ruled that any " +
                      "non-victory that is not a player retreat loses the warband, so the unknown case must " +
                      "resolve to the fail rate - never to a free exit.");

            // A raid nobody survived cannot lose more than it deployed.
            var wiped = RaidCasualtyPolicy.Decide(5, 0, RaidExitOutcome.Failed, FailPct, RetreatPct);
            Expect(f, "total wipe killed", 5, wiped.Killed);
            Expect(f, "total wipe lostByPolicy (nobody left to lose)", 0, wiped.LostByPolicy);
            Expect(f, "total wipe totalLost", 5, wiped.TotalLost);
        }

        // ── B. the rounding, which is where a policy quietly becomes a different one ──
        private static void CaseB_RoundingEdges(List<string> f, StringBuilder log)
        {
            log.AppendLine("[case B] rounding: integer NEAREST with a floor of one");

            // THE TABLE AT 60%, stated independently of the implementation. ceil() would read
            // 1,2,2,3,5 here - i.e. 100% of two survivors and 71% of seven, neither of which is
            // "60%". That is exactly the substitution this case exists to catch.
            var table = new[]
            {
                new[] { 0, 0 }, new[] { 1, 1 }, new[] { 2, 1 }, new[] { 3, 2 },
                new[] { 4, 2 }, new[] { 5, 3 }, new[] { 7, 4 }, new[] { 10, 6 },
            };
            foreach (var row in table)
            {
                int got = RaidCasualtyPolicy.LostOf(row[0], RetreatPct);
                if (got != row[1])
                    f.Add("[B] " + RetreatPct + "% of " + row[0] + " survivor(s) came out as " + got +
                          ", expected " + row[1] + " (integer nearest, floor of one). A ceil()/floor() " +
                          "substitution changes what every retreat in the game costs.");
            }

            // The FLOOR: a non-empty remainder taxed at all always pays at least one, so
            // retreating with one survivor is never free.
            if (RaidCasualtyPolicy.LostOf(1, 10) != 1)
                f.Add("[B] a 10% loss on ONE survivor rounded to zero - the ruling's floor of one is gone, " +
                      "and a lone-survivor retreat is a free save again.");

            // pct 0 takes NOTHING (the floor must not manufacture a loss out of a zero rate -
            // that is the row an owner sets to undo this feature).
            if (RaidCasualtyPolicy.LostOf(9, 0) != 0)
                f.Add("[B] a 0% loss rate still took troops. A row of 0 is how the owner turns this off; " +
                      "the floor of one must never override a zero rate.");

            // pct 100 takes everyone, and nothing beyond.
            if (RaidCasualtyPolicy.LostOf(9, 100) != 9)
                f.Add("[B] a 100% loss rate did not take all 9 survivors.");

            // Out-of-range is clamped, never wrapped or thrown on: a console typo must degrade.
            if (RaidCasualtyPolicy.Clamp01Pct(400) != 100 || RaidCasualtyPolicy.Clamp01Pct(-5) != 0)
                f.Add("[B] the loss percent is not clamped to 0..100 - a console typo could price a raid " +
                      "outside the ruling entirely.");

            // Victory is charged nothing whatever the rows say.
            if (RaidCasualtyPolicy.LossPctFor(RaidExitOutcome.Victory, 100, 60) != 0)
                f.Add("[B] VICTORY resolved a non-zero loss rate. A win takes the killed and nothing more.");
        }

        // ── C. the roster mutation, end to end on a fake army ─────────────────
        private static void CaseC_RosterMutation(List<string> f, StringBuilder log)
        {
            log.AppendLine("[case C] the ArmyStorage mutation on a real roster");

            // Ten troops; the three with the HIGHEST veterancy are deliberately among the
            // survivors so the rookies-first selection is observable.
            var army = new ArmyStorage();
            for (int i = 0; i < 10; i++)
            {
                var t = new PlayerTroop("troop-" + i, "troop-footman");
                if (i >= 7) t.VeterancyRank = 3;       // troop-7/8/9 are veterans
                army.Owned.Add(t);
            }
            // Plus one troop that was NEVER deployed - it must be untouched by all of this.
            army.Owned.Add(new PlayerTroop("troop-home", "troop-archer"));

            var deployed = new List<string>();
            for (int i = 0; i < 10; i++) deployed.Add("troop-" + i);
            // troop-0/1/2 fell on the field; 3..9 survived.
            var survivors = new List<string> { "troop-3", "troop-4", "troop-5", "troop-6",
                                               "troop-7", "troop-8", "troop-9" };

            var c = RaidCasualtyPolicy.Decide(deployed.Count, survivors.Count,
                                              RaidExitOutcome.Retreat, FailPct, RetreatPct);
            var lostSurvivors = RaidCasualtyPolicy.PickLostSurvivors(army, survivors, c.LostByPolicy);
            Expect(f, "picked survivors to lose", 4, lostSurvivors.Count);

            // ROOKIES FIRST, DETERMINISTICALLY: rank 0 troops 3,4,5,6 go before any veteran.
            foreach (string id in new[] { "troop-3", "troop-4", "troop-5", "troop-6" })
                if (!lostSurvivors.Contains(id))
                    f.Add("[C] the retreat did not take rookie " + id + " before a veteran. Veterancy is " +
                          "EARNED on 3-star clears, so the selection must be rank-ascending and " +
                          "deterministic - never random, or an oracle can never assert it twice.");
            foreach (string id in new[] { "troop-7", "troop-8", "troop-9" })
                if (lostSurvivors.Contains(id))
                    f.Add("[C] the retreat spent VETERAN " + id + " while rank-0 survivors were available.");

            var doomed = new List<string> { "troop-0", "troop-1", "troop-2" };
            doomed.AddRange(lostSurvivors);
            int removed = army.RemoveOwned(doomed);
            Expect(f, "troops removed from the roster", 7, removed);
            Expect(f, "roster size after the retreat", 4, army.Owned.Count);

            // Nobody left behind is WOUNDED: the ruling replaced the free recovery timer.
            foreach (var t in army.Owned)
                if (t != null && t.Wounded)
                    f.Add("[C] '" + t.Id + "' survived the settlement as WOUNDED. The owner's complaint was " +
                          "exactly that - 'troops return a few injured which has no cost or time'. The " +
                          "raid path must remove, not wound.");

            // The never-deployed troop and the veterans are still there.
            foreach (string id in new[] { "troop-home", "troop-7", "troop-8", "troop-9" })
                if (!Has(army, id))
                    f.Add("[C] '" + id + "' was removed although it should have come home (or was never " +
                          "deployed at all). A deletion path that over-reaches destroys an army the player " +
                          "paid for.");

            // IDEMPOTENT: a duplicated raid-exit call must not remove a second time.
            int again = army.RemoveOwned(doomed);
            Expect(f, "a repeated removal of the same ids", 0, again);
            Expect(f, "roster size after the repeat", 4, army.Owned.Count);

            // Null / empty are no-ops, because this runs on a raid exit and must never throw there.
            if (army.RemoveOwned(null) != 0 || army.RemoveOwned(new List<string>()) != 0)
                f.Add("[C] RemoveOwned(null/empty) was not a no-op.");

            // A FAILED raid on the same shape removes EVERYTHING that was deployed.
            var army2 = new ArmyStorage();
            for (int i = 0; i < 10; i++) army2.Owned.Add(new PlayerTroop("t-" + i, "troop-footman"));
            var dep2 = new List<string>();
            for (int i = 0; i < 10; i++) dep2.Add("t-" + i);
            var surv2 = new List<string> { "t-7", "t-8", "t-9" };
            var c2 = RaidCasualtyPolicy.Decide(10, 3, RaidExitOutcome.Failed, FailPct, RetreatPct);
            var all = new List<string>();
            foreach (string id in dep2) if (!surv2.Contains(id)) all.Add(id);
            all.AddRange(RaidCasualtyPolicy.PickLostSurvivors(army2, surv2, c2.LostByPolicy));
            army2.RemoveOwned(all);
            Expect(f, "roster size after a FAILED raid that deployed everyone", 0, army2.Owned.Count);
        }

        // ── D. the call sites (source-lint) ───────────────────────────────────
        private static void CaseD_CallSites(List<string> f, StringBuilder log)
        {
            log.AppendLine("[case D] the reconcile still prices and removes; the exits still declare");

            string deploy = SourceLint.ReadCode(DeployRel, f);
            if (!string.IsNullOrEmpty(deploy))
            {
                string body = SourceLint.Body(deploy, @"public\s+void\s+ReconcileRaidEnd\s*\(\s*int\s+starsEarned\s*\)");
                if (string.IsNullOrEmpty(body))
                {
                    f.Add("[D] RaidDeployController.ReconcileRaidEnd(int starsEarned) not found - the raid " +
                          "settlement seam moved, and the casualty policy moved with it.");
                }
                else
                {
                    if (body.IndexOf("RaidCasualtyPolicy.Decide", StringComparison.Ordinal) < 0)
                        f.Add("[D] ReconcileRaidEnd no longer asks RaidCasualtyPolicy what the raid cost - " +
                              "losing is free again (WO-1810).");
                    if (body.IndexOf("RemoveOwned", StringComparison.Ordinal) < 0)
                        f.Add("[D] ReconcileRaidEnd no longer REMOVES the fallen from the roster. The owner " +
                              "ruled 'any troop killed is dead'; wounding them was the defect.");
                    if (body.IndexOf("ReconcileAfterRaid", StringComparison.Ordinal) < 0)
                        f.Add("[D] the wounded BACKSTOP is gone: a deployed body that escaped removal would " +
                              "walk home silently healthy, and nothing would say so.");
                }

                // Both non-victory exits must DECLARE what they are - the whole policy hangs on
                // telling a chosen retreat apart from a raid that simply failed.
                if (deploy.IndexOf("DeclareRaidExitOutcome(", StringComparison.Ordinal) < 0)
                    f.Add("[D] no exit on RaidDeployController declares its outcome any more, so every raid " +
                          "prices as the undeclared default.");
                if (deploy.IndexOf("RaidExitOutcome.Retreat", StringComparison.Ordinal) < 0)
                    f.Add("[D] nothing in RaidDeployController declares a RETREAT. The timeout funnels through " +
                          "the same DoRetreat method, so without this a chosen retreat is priced as a fail.");
            }

            string victory = SourceLint.ReadCode(VictoryRel, f);
            if (!string.IsNullOrEmpty(victory) &&
                victory.IndexOf("RaidExitOutcome.Victory", StringComparison.Ordinal) < 0)
                f.Add("[D] RaidVictoryController never declares VICTORY. An undeclared exit prices as a FAIL, " +
                      "so a hero dying between the win and the army reconcile would wipe a WON warband.");
        }

        // ── E. a WON raid that killed troops must SAY SO ──────────────────────
        private static void CaseE_VictoryScreenStatesTheCost(List<string> f, StringBuilder log)
        {
            log.AppendLine("[case E] the victory screen states the troops a win cost");

            // 10 deployed, 7 survived, base cleared: the 3 killed are DEAD on a win too, and until
            // WO-1810's follow-up the victory screen never mentioned them - a player could 3-star a
            // camp, lose three troops for good and only find out at the barracks.
            var win = RaidCasualtyPolicy.Decide(10, 7, RaidExitOutcome.Victory, FailPct, RetreatPct);
            Expect(f, "victory totalLost fed to the screen", 3, win.TotalLost);

            var vm = DeNelle.Village.UI.EndStateVM.FromRaidVictory(
                null, null, 20f, 3, 100, 88f, default(DeNelle.Village.ResourceCost), null,
                win.TotalLost);
            if (vm == null)
            {
                f.Add("[E] FromRaidVictory returned null on a normal win");
                return;
            }
            Expect(f, "victory VM TroopsLost", 3, vm.TroopsLost);
            if (vm.Subtitle == null || vm.Subtitle.IndexOf("3 troops lost", StringComparison.Ordinal) < 0)
                f.Add("[E] a WON raid that killed 3 troops does not say '3 troops lost' on its own " +
                      "screen. Killed troops are dead on a victory as well ('any troop killed is dead'), " +
                      "and a win that deletes troops in silence is the same defect the non-victory screen " +
                      "was built to end. Subtitle was: " + (vm.Subtitle ?? "<null>"));

            // A CLEAN SWEEP SAYS NOTHING: a flawless win must not be handed a "0 troops lost"
            // consolation line, and an unknown count (no deploy ledger) must print nothing either.
            var clean = DeNelle.Village.UI.EndStateVM.FromRaidVictory(
                null, null, 20f, 3, 100, 60f, default(DeNelle.Village.ResourceCost), null, 0);
            if (clean != null && clean.Subtitle != null &&
                clean.Subtitle.IndexOf("troops lost", StringComparison.Ordinal) >= 0)
                f.Add("[E] a win that lost NOBODY still printed a troops-lost line: " + clean.Subtitle);
            var unknown = DeNelle.Village.UI.EndStateVM.FromRaidVictory(null, null, 20f);
            if (unknown != null && unknown.Subtitle != null &&
                unknown.Subtitle.IndexOf("troop", StringComparison.Ordinal) >= 0)
                f.Add("[E] a win with NO deploy ledger (-1, unknown) printed a troop line it cannot " +
                      "prove: " + unknown.Subtitle);

            // And the wiring: the victory controller must FEED that count, not leave the default.
            string victory = SourceLint.ReadCode(VictoryRel, f);
            if (!string.IsNullOrEmpty(victory))
            {
                if (victory.IndexOf("LastTroopsLost", StringComparison.Ordinal) < 0)
                    f.Add("[E] RaidVictoryController never reads RaidDeployController.LastTroopsLost, so the " +
                          "victory screen is back to omitting what the win cost.");
                if (victory.IndexOf("_troopsLostThisRaid", StringComparison.Ordinal) < 0)
                    f.Add("[E] the victory path no longer carries the lost count to its screen.");
            }
        }

        // ── F. a FAILED raid pays NOTHING; a RETREAT still pays ───────────────
        private static void CaseF_FailPaysNoLoot(List<string> f, StringBuilder log)
        {
            log.AppendLine("[case F] owner ruling 2026-09-16 ~21:20: fail pays 0, retreat pays as today");

            // THE PAYMENT BRANCH, source-linted: the grant path is a wallet mutation that needs a
            // live EconomyService, so the CONTRACT is pinned at the call site instead of simulated.
            string deploy = SourceLint.ReadCode(DeployRel, f);
            string settle = string.IsNullOrEmpty(deploy)
                ? string.Empty
                : SourceLint.Body(deploy, @"public\s+void\s+SettlePartialLoot\s*\(");
            if (string.IsNullOrEmpty(settle))
            {
                f.Add("[F] could not locate SettlePartialLoot's body - the one non-victory loot authority " +
                      "moved, and the fail-pays-nothing ruling moved with it.");
            }
            else
            {
                if (settle.IndexOf("RaidExitOutcome.Retreat", StringComparison.Ordinal) < 0)
                    f.Add("[F] SettlePartialLoot no longer branches on the declared exit outcome, so a FAILED " +
                          "raid is being paid again (owner ruling 2026-09-16: only a player RETREAT pays).");
                if (settle.IndexOf("Finalize(false)", StringComparison.Ordinal) < 0)
                    f.Add("[F] the fail branch stopped FINALIZING the score. Only the PAYMENT is refused - " +
                          "stars, razed % and the clock still have to reach the result screen, and the " +
                          "Finalized latch is what stops a second exit paying twice.");
                if (settle.IndexOf("GrantRetreatLoot(", StringComparison.Ordinal) < 0)
                    f.Add("[F] the RETREAT branch no longer grants anything - a chosen retreat must still pay " +
                          "the damage-scaled share exactly as before.");
            }

            // THE SCREEN: a fail states the loss in words and draws no spoils rows.
            var failVm = DeNelle.Village.UI.EndStateVM.FromRaidRetreat(
                DeNelle.Village.UI.EndStateVM.TimeoutReason, null, 30f, 0, 12, 180f,
                default(DeNelle.Village.ResourceCost), false, 10, 7, 10);
            if (failVm == null) { f.Add("[F] FromRaidRetreat returned null on a timeout"); return; }
            if (failVm.Spoils.Count != 0)
                f.Add("[F] a FAILED raid drew " + failVm.Spoils.Count + " spoil row(s). It pays nothing now, " +
                      "so a row would advertise a payout that never landed.");
            if (failVm.Subtitle == null ||
                failVm.Subtitle.IndexOf("No spoils - the warband was lost", StringComparison.Ordinal) < 0)
                f.Add("[F] a FAILED raid does not say it banked nothing. An empty spoils grid alone reads as a " +
                      "broken screen, and the owner is red/green colourblind - the verdict is the WORDS. " +
                      "Subtitle was: " + (failVm.Subtitle ?? "<null>"));

            // A RETREAT that banked something still shows it, and is NEVER told the warband was lost.
            var kept = new DeNelle.Village.ResourceCost(wood: 420, stone: 0, iron: 0, crystals: 0, coins: 180);
            var retreatVm = DeNelle.Village.UI.EndStateVM.FromRaidRetreat(
                DeNelle.Village.UI.EndStateVM.RetreatReason, null, 30f, 1, 62, 96f,
                kept, false, 6, 4, 4);
            if (retreatVm == null) { f.Add("[F] FromRaidRetreat returned null on a retreat"); return; }
            if (retreatVm.Spoils.Count != 2)
                f.Add("[F] a RETREAT crediting wood+gold drew " + retreatVm.Spoils.Count +
                      " spoil row(s), expected 2. The retreat share is untouched by this ruling.");
            if (retreatVm.Subtitle != null &&
                retreatVm.Subtitle.IndexOf("No spoils", StringComparison.Ordinal) >= 0)
                f.Add("[F] a RETREAT that banked loot was told it had no spoils.");

            // A retreat that razed nothing banks nothing - and must NOT inherit the fail sentence,
            // because it lost its haul for a different reason.
            var emptyRetreat = DeNelle.Village.UI.EndStateVM.FromRaidRetreat(
                DeNelle.Village.UI.EndStateVM.RetreatReason, null, 30f, 0, 0, 20f,
                default(DeNelle.Village.ResourceCost), false, 5, 5, 3);
            if (emptyRetreat != null && emptyRetreat.Subtitle != null &&
                emptyRetreat.Subtitle.IndexOf("the warband was lost", StringComparison.Ordinal) >= 0)
                f.Add("[F] a RETREAT that simply razed nothing was told the warband was lost - that sentence " +
                      "belongs to the FAIL exit only.");
        }

        // ── helpers ───────────────────────────────────────────────────────────
        private static void Expect(List<string> f, string what, int expected, int got)
        {
            if (expected != got)
                f.Add("[" + what + "] expected " + expected + ", got " + got);
        }

        private static bool Has(ArmyStorage army, string id)
        {
            if (army == null || army.Owned == null) return false;
            foreach (var t in army.Owned)
                if (t != null && string.Equals(t.Id, id, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
