// =============================================================================
// WallBreachOrderRegression [wall-breach-order] — WO-1719.
// -----------------------------------------------------------------------------
// Pins the owner's 2026-09-14 breach ruling, verbatim: "tap the wall segment
// directly, and it overrides" / "the most damanged [stays the fallback]" /
// "all together unless they have aggro".
//
// WHAT IS ACTUALLY PROVEN HERE, and why in this shape:
//   1. FALLBACK INTACT  — with no explicit pick the rule is byte-for-byte the old
//      most-damaged / nearest-muster one. This is the case that would catch an
//      "override" implemented by restructuring the selection body.
//   2. OVERRIDE WINS    — a FULL-HP, FAR wall the player tapped beats a badly
//      damaged near one. That is the exact pairing WO-1717 sec.3d says defeats the
//      player today ("the moment ANY wall has taken >0.5 more damage ... that wall
//      wins permanently").
//   3. DEAD PICK FALLS BACK — the ordered panel collapsing returns the warband to
//      the automatic rule, which is this ticket's chosen "what clears an order".
//   4. ALL TOGETHER, EXCEPT AGGRO — a fixture of four Breach-phase troops and one
//      peelThreat troop: all four resolve bucket 2 == the ordered segment; the
//      aggro'd one resolves bucket 0 == its own foe, untouched.
//   5. THE RALLY MARCH RELEASES for an explicit order only (phase and rally are
//      independent axes - without this, case 4 fails for the whole warband any time
//      a rally is set).
//   6. WIRED, NOT JUST PURE — source-text proof that TroopController.SharedBreachFocus
//      and RaidDeployController actually consult TroopBreachOrder. Same pattern as
//      RaidAssaultAiRegression.Case_AllowNonObjectiveWiredIntoPickBucket: a pure rule
//      that nothing calls passes every case and ships nothing.
//
// WO-1746 EXTENDS THIS SUITE rather than adding a new one — the owner's WO-1738
// ruling is the same subject (the Breach order / stance) and a second file would
// have split one rule across two markers. The suite COUNT is therefore unchanged;
// DataRegression.cs needs no new line. Cases 7-12 and their own red proofs are
// documented at RunRulingCases below:
//   7.  a REACHABLE defender beats the wall for an ordinary troop (an unreachable
//       one deliberately does not — that troop is the ruling's blocked warband).
//   8.  the STANCE holds the warband on masonry vs a calm defender; aggro peels.
//   9.  the multiplier: 0.10 reluctant / 1.00 under stance / 1.00 siege either way.
//   10. the stance AUTO-CHAINS — the ordered panel's self-clear is kept, the stance
//       survives it, and the automatic rule picks the next panel at full damage.
//   11. the STANCE (not the standing order) is what releases the rally march.
//   12. all of it is on the LIVE path, including the Attack() gate placement.
// WO-1752 (owner, 2026-09-15 felt-test) then REVERSED part of the above and added 13-16:
//   13. ruling 1 - a non-siege troop with NO stance picks NO wall (-1), a tower is still
//       legal, and the stance re-opens the panel. Cases 7 and 9 were AMENDED for this:
//       7's third assertion was inverted (it demanded the wall) and 9 now demands 0.00.
//   14. ruling 2 - "Siege still hits walls on its own", with and without the stance.
//   15. ruling 3 - an idle troop adopts the hero's live attacker; a busy one does not;
//       and adoption still cannot break an armed Breach stance (WO-1746 sec.4 stands).
//   16. WO-1752 on the live path, including that the adoption runs BEFORE PickBucket.
//
// RED PROOF: delete the `if (explicitFocus != null && explicitFocus.IsAlive) return
// explicitFocus;` early return in RaidAssaultAi.SelectFocusBreach and cases 2 and 4
// fail. Delete the TroopBreachOrder read in TroopController.SharedBreachFocus and
// case 6 fails while 1-5 still pass - which is precisely the failure mode case 6
// exists to catch.
//
// Marker: WALL_BREACH_ORDER_OK / WALL_BREACH_ORDER_FAIL.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core.Combat;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class WallBreachOrderRegression
    {
        private const string Tag = "[wall-breach-order]";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== WallBreachOrderRegression (WO-1719) ===\n");
            try
            {
                Case_NoOrder_AutoMostDamagedStillWins(failures, log);
                Case_ExplicitOrder_OverridesMostDamaged(failures, log);
                Case_CollapsedOrder_FallsBackToAuto(failures, log);
                Case_WarbandRetargetsTogether_AggroTroopKeepsItsFight(failures, log);
                Case_RallyMarch_ReleasedByExplicitOrderOnly(failures, log);
                Case_OverrideWiredIntoTheLivePath(failures, log);
                // WO-1746 — the owner's WO-1738 ruling.
                RunRulingCases(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = log.ToString().TrimEnd() + "\nWALL_BREACH_ORDER_OK";
                return true;
            }

            reason = log.ToString() + string.Join("\n", failures) + "\nWALL_BREACH_ORDER_FAIL";
            return false;
        }

        // ── 1. The automatic rule is untouched when no order stands ──────────────
        private static void Case_NoOrder_AutoMostDamagedStillWins(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_NoOrder_AutoMostDamagedStillWins");
            var fullNear = new StubWall { HpValue = 100f, Pos = Vector3.zero, Label = "fullNear" };
            var hurtFar = new StubWall { HpValue = 40f, Pos = new Vector3(20f, 0f, 0f), Label = "hurtFar" };
            var list = new List<IDamageable> { fullNear, hurtFar };

            if (!ReferenceEquals(RaidAssaultAi.SelectFocusBreach(list, Vector3.zero), hurtFar))
                failures.Add(Tag + " the 2-arg fallback stopped picking the most-damaged wall");
            if (!ReferenceEquals(RaidAssaultAi.SelectFocusBreach(list, Vector3.zero, null), hurtFar))
                failures.Add(Tag + " a NULL explicit focus must fall through to the most-damaged rule");

            var a = new StubWall { HpValue = 100f, Pos = new Vector3(10f, 0f, 0f), Label = "farFull" };
            var b = new StubWall { HpValue = 100f, Pos = new Vector3(3f, 0f, 0f), Label = "nearFull" };
            if (!ReferenceEquals(RaidAssaultAi.SelectFocusBreach(
                    new List<IDamageable> { a, b }, Vector3.zero, null), b))
                failures.Add(Tag + " the nearest-muster tie-break was lost from the fallback");
            log.AppendLine("   fallback = most-damaged, ties by nearest muster: intact");
        }

        // ── 2. The player's tap beats the most-damaged computation outright ──────
        private static void Case_ExplicitOrder_OverridesMostDamaged(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_ExplicitOrder_OverridesMostDamaged");
            // The WO-1717 sec.3d pairing: the tapped panel is FULL HP and FAR; another wall
            // has already been chipped well past the 0.5 HP tie window. Today that chipped
            // wall wins permanently and the warband walks away from the player's tap.
            var tapped = new StubWall { HpValue = 100f, Pos = new Vector3(25f, 0f, 0f), Label = "tapped" };
            var chipped = new StubWall { HpValue = 12f, Pos = Vector3.zero, Label = "chipped" };
            var list = new List<IDamageable> { chipped, tapped };

            var pick = RaidAssaultAi.SelectFocusBreach(list, Vector3.zero, tapped);
            if (!ReferenceEquals(pick, tapped))
                failures.Add(Tag + " an explicit player breach order MUST beat the most-damaged pick " +
                             "(picked '" + LabelOf(pick) + "', expected 'tapped')");

            // And it wins even when it is not in the scanned candidate list at all - a cached
            // scene scan can trail the tap by a frame, and an invisible intermittent refusal
            // is worse than no feature.
            var notInList = RaidAssaultAi.SelectFocusBreach(
                new List<IDamageable> { chipped }, Vector3.zero, tapped);
            if (!ReferenceEquals(notInList, tapped))
                failures.Add(Tag + " the explicit pick must win even when the cached wall scan " +
                             "has not caught up with it yet");
            log.AppendLine("   explicit tap beats a 12hp wall at the muster: yes");
        }

        // ── 3. The ordered panel collapsing is what clears the order ─────────────
        private static void Case_CollapsedOrder_FallsBackToAuto(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_CollapsedOrder_FallsBackToAuto");
            var collapsed = new StubWall { HpValue = 0f, Pos = new Vector3(25f, 0f, 0f), Label = "collapsed" };
            var chipped = new StubWall { HpValue = 12f, Pos = Vector3.zero, Label = "chipped" };
            var fullFar = new StubWall { HpValue = 100f, Pos = new Vector3(40f, 0f, 0f), Label = "fullFar" };

            var pick = RaidAssaultAi.SelectFocusBreach(
                new List<IDamageable> { chipped, fullFar }, Vector3.zero, collapsed);
            if (!ReferenceEquals(pick, chipped))
                failures.Add(Tag + " a DEAD explicit target must fall back to the automatic " +
                             "most-damaged rule, not strand the warband (picked '" + LabelOf(pick) + "')");
            log.AppendLine("   dead order -> automatic rule resumes: yes");
        }

        // ── 4. All together, except aggro ────────────────────────────────────────
        private static void Case_WarbandRetargetsTogether_AggroTroopKeepsItsFight(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_WarbandRetargetsTogether_AggroTroopKeepsItsFight");

            var tapped = new StubWall { HpValue = 100f, Pos = new Vector3(25f, 0f, 0f), Label = "tapped" };
            var chipped = new StubWall { HpValue = 12f, Pos = Vector3.zero, Label = "chipped" };
            var scan = new List<IDamageable> { chipped, tapped };
            var defender = new StubUnit { Label = "defender" };

            // Four breach-phase troops scattered around the base (different musters, so a
            // per-troop nearest rule would give four different answers) plus one that is
            // being hit - the live peelThreat=True / phase=Peel state from the WO-1717
            // device capture.
            var musters = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 5f),
                new Vector3(-12f, 0f, 18f), new Vector3(8f, 0f, -22f),
            };

            for (int i = 0; i < musters.Length; i++)
            {
                var resolved = ResolveWinner(
                    scan, musters[i], explicitFocus: tapped,
                    peelThreat: false, hasUnit: false, unit: null, unitInAttackRange: false);
                if (!ReferenceEquals(resolved, tapped))
                    failures.Add(Tag + " breach-phase troop " + i + " (muster " + musters[i] +
                                 ") resolved '" + LabelOf(resolved) + "' - every phase=Breach troop " +
                                 "must retarget to the ordered segment in the same update");
            }

            // The aggro'd troop: peelThreat -> Peel -> bucket 0, its own foe. The ordered
            // wall is still resolved for it (same shared focus) and must still lose.
            var aggroWinner = ResolveWinner(
                scan, new Vector3(2f, 0f, 2f), explicitFocus: tapped,
                peelThreat: true, hasUnit: true, unit: defender, unitInAttackRange: true);
            if (!ReferenceEquals(aggroWinner, defender))
                failures.Add(Tag + " an aggro'd (peelThreat) troop must KEEP its current fight when a " +
                             "breach order lands - it resolved '" + LabelOf(aggroWinner) + "'");

            // Guard the inverse too: without aggro that same troop WOULD take the wall, so the
            // case above is proving the peel gate and not an accident of the fixture.
            var sameTroopNoAggro = ResolveWinner(
                scan, new Vector3(2f, 0f, 2f), explicitFocus: tapped,
                peelThreat: false, hasUnit: true, unit: defender, unitInAttackRange: false);
            if (!ReferenceEquals(sameTroopNoAggro, tapped))
                failures.Add(Tag + " fixture check: the same troop without aggro should take the " +
                             "ordered wall, so the aggro case proves the peel gate");
            log.AppendLine("   4/4 breach troops -> ordered panel; aggro'd troop -> its foe: yes");
        }

        /// <summary>
        /// The live resolve, reassembled from the SAME pure calls TroopController makes
        /// (TroopController.cs: SelectFocusBreach -> ResolvePhase -> PickBucket -> bucket
        /// switch). Kept in one place so a fixture troop cannot drift from the real order.
        /// </summary>
        private static IDamageable ResolveWinner(
            List<IDamageable> scan, Vector3 muster, IDamageable explicitFocus,
            bool peelThreat, bool hasUnit, IDamageable unit, bool unitInAttackRange)
        {
            IDamageable otherStruct = RaidAssaultAi.SelectFocusBreach(scan, muster, explicitFocus);

            // The rally march: a rally IS set and the troop has NOT arrived, which is the
            // normal mid-raid state. An explicit order releases it (see the 4-arg overload).
            bool hasExplicitOrder = explicitFocus != null && explicitFocus.IsAlive;
            if (RaidAssaultAi.RallyHoldsMarch(true, false, peelThreat, hasExplicitOrder))
                otherStruct = null;

            var phase = RaidAssaultAi.ResolvePhase(peelThreat, false, false);
            int bucket = RaidAssaultAi.PickBucket(
                phase, false, hasUnit, false, otherStruct != null, unitInAttackRange, false);
            switch (bucket)
            {
                case 0: return unit;
                case 2: return otherStruct;
                default: return null;
            }
        }

        // ── 5. The rally march releases for an explicit order, and only that ─────
        private static void Case_RallyMarch_ReleasedByExplicitOrderOnly(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_RallyMarch_ReleasedByExplicitOrderOnly");
            if (!RaidAssaultAi.RallyHoldsMarch(true, false, false, false))
                failures.Add(Tag + " with NO order the rally must still hold the march " +
                             "(owner 2026-09-12: no wall-ring chewing on the way to the flag)");
            if (RaidAssaultAi.RallyHoldsMarch(true, false, false, true))
                failures.Add(Tag + " an EXPLICIT breach order must release the rally march, or no " +
                             "rally-marching troop ever obeys the tap");
            if (RaidAssaultAi.RallyHoldsMarch(false, false, false, false))
                failures.Add(Tag + " no rally must not hold the march");
            if (RaidAssaultAi.RallyHoldsMarch(true, true, false, false))
                failures.Add(Tag + " arrival must still release the march");
            log.AppendLine("   implicit ring-farm still suppressed; explicit order released: yes");
        }

        // ── 6. The pure rule is actually on the live path ────────────────────────
        private static void Case_OverrideWiredIntoTheLivePath(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_OverrideWiredIntoTheLivePath");

            string controller = Read("Assets/_Modules/Village/Troops/TroopController.cs");
            if (controller == null)
            {
                failures.Add(Tag + " TroopController.cs could not be read");
            }
            else
            {
                int start = controller.IndexOf("SharedBreachFocus(Vector3 muster)", StringComparison.Ordinal);
                if (start < 0)
                {
                    failures.Add(Tag + " TroopController.SharedBreachFocus not found - the shared " +
                                 "breach seam moved; re-point this case before trusting it");
                }
                else
                {
                    int end = controller.IndexOf("IsHostileStructure", start, StringComparison.Ordinal);
                    if (end < 0) end = Math.Min(controller.Length, start + 4000);
                    string body = controller.Substring(start, end - start);
                    if (body.IndexOf("TroopBreachOrder", StringComparison.Ordinal) < 0)
                        failures.Add(Tag + " SharedBreachFocus no longer consults TroopBreachOrder - the " +
                                     "player's explicit pick cannot reach the warband, however well the " +
                                     "pure rule above passes");
                }
            }

            string deploy = Read("Assets/_Modules/Village/Troops/RaidDeployController.cs");
            if (deploy == null)
            {
                failures.Add(Tag + " RaidDeployController.cs could not be read");
            }
            else
            {
                if (deploy.IndexOf("TroopBreachOrder.Set(", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " nothing in RaidDeployController SETS a breach order - the " +
                                 "Breach tap is gone");
                if (deploy.IndexOf("ToggleBreach", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the Breach toggle is gone from the raid HUD");
                if (deploy.IndexOf("TroopBreachOrder.Clear()", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " nothing clears the breach order - a stale static would point " +
                                 "the next raid's warband at a destroyed wall");
                // The default tap must stay Rally: HandleRallyTap has to survive, and the breach
                // branch has to be gated on the mode rather than replacing it.
                if (deploy.IndexOf("if (_breachMode) { HandleBreachTap(screenPoint); return; }",
                        StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the breach tap is no longer gated on _breachMode - a wall tap " +
                                 "with Breach OFF must still behave exactly as Rally");
                if (deploy.IndexOf("if (_rallyMode) { HandleRallyTap(screenPoint); return; }",
                        StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the default rally tap route changed - WO-1719 adds a mode, it " +
                                 "does not change the default world tap");
            }
            log.AppendLine("   SharedBreachFocus reads the order; the HUD sets/clears it; Rally untouched");
        }

        // =====================================================================
        //  WO-1746 — the WO-1738 ruling (owner, 2026-09-15)
        // -----------------------------------------------------------------------------
        //  RULING RECORD, verbatim, so this file states what it pins:
        //   1. "Branch B, with a 10% reluctant fallback. Keep WO-1737's shipped durability.
        //      Ordinary troops do NOT auto-attack walls while any hostile unit or reachable
        //      non-wall objective exists. Siege, and any troop under an active Breach stance,
        //      attack walls at full damage. A warband that is BLOCKED (no route to the objective)
        //      with no Breach active attacks the nearest blocking wall at 10% structural damage,
        //      so it never idles into a dead-end."
        //      ⛔ THE LAST SENTENCE OF RULING 1 IS RETIRED BY WO-1752 (2026-09-15). The owner
        //      watched it ship and overruled it: "we need to set it so that the troops 100%
        //      ignore walls, unless explicitly told breach". Kept verbatim above rather than
        //      edited, because the cases below are what enforce the NEW rule and a doctored
        //      quote would make case 7's inverted assertion look like a bug. See cases 13-16.
        //   2. "Breach is a persistent stance that auto-chains. One tap = 'we are breaching';
        //      when the ordered wall falls the warband keeps opening walls (today's self-clear ->
        //      most-damaged/nearest behaviour is KEPT) until the player toggles Breach off or a
        //      hostile pulls aggro."
        //
        //  RED PROOF, per case (reasoned from the seams, NOT executed - this lane may not run
        //  Unity, and a claimed red run would be the guess CLAUDE.md sec.11B forbids):
        //   * Case 7  — has NO WO-1746 line to delete, and that is the point: it pins behaviour
        //     that was ALREADY correct (a reachable defender beating the wall) so this ruling
        //     cannot regress it. Its red proof is to make PreferUnit's Breach branch return false
        //     for a reachable unit; both assertions then fail.
        //   * Case 8  — delete `if (breachStance) return false;` from the 7-arg PreferUnit: the
        //     stance case resolves bucket 0 and the case fails.
        //   * Case 9  — change WallDamageMultiplier to `return 1f;` unconditionally: the 0.10
        //     assertion fails. Change it to always return the reluctant value: the siege and
        //     stance assertions fail. The two halves cannot both be satisfied by a constant.
        //   * Case 10 — make DropInternal disarm the stance (the tempting "tidy up on death"):
        //     StanceActive reads false after the collapse and the auto-chain assertion fails.
        //   * Case 11 — pure only, and it deliberately restates Case 5's rule from the STANCE's
        //     side; its red proof is deleting `if (hasExplicitBreachOrder) return false;` from the
        //     4-arg RallyHoldsMarch. ⚠ Reverting TroopController's 4th ARG to HasOrder does NOT
        //     redden this case - only Case 12's source-text assertion catches that, which is
        //     exactly the pure-vs-wired split Case 12 exists for.
        //   * Case 12 — revert TroopController's RallyHoldsMarch 4th arg to HasOrder, OR fold the
        //     reluctant multiplier back inside Attack()'s
        //     `_preferStructures || ...` gate, or drop the WallSegment scoping: the source-text
        //     assertions fail. This is the case that catches a ruling that compiles, traces
        //     correctly and applies to nobody.
        // =====================================================================
        private static void RunRulingCases(List<string> failures, StringBuilder log)
        {
            // The order + stance are STATIC. Any case that arms them must hand the next suite in
            // DataRegression a clean slate, or a failure lands in someone else's file.
            try
            {
                Case_UnitAcquirable_NonSiegeDoesNotTakeTheWall(failures, log);
                Case_BreachStance_HoldsTheWarbandOnMasonry(failures, log);
                Case_WallDamageMultiplier_TheTenPercentRuling(failures, log);
                Case_StanceAutoChains_WhenTheOrderedWallFalls(failures, log);
                Case_StanceReleasesTheRallyMarch(failures, log);
                Case_RulingWiredIntoTheLivePath(failures, log);
                // WO-1752 — the owner's 2026-09-15 felt-test rulings.
                Case_WallsOff_NonSiegeWithoutStancePicksNoWall(failures, log);
                Case_SiegeStillTakesTheWall(failures, log);
                Case_IdleTroopAdoptsTheHerosAttacker(failures, log);
                Case_WO1752WiredIntoTheLivePath(failures, log);
            }
            finally
            {
                TroopBreachOrder.Clear();
                TroopBreachOrder.SetStanceArmed(false);
            }
        }

        // ── 7a. (a) A reachable defender beats the wall for an ordinary troop ────
        private static void Case_UnitAcquirable_NonSiegeDoesNotTakeTheWall(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_UnitAcquirable_NonSiegeDoesNotTakeTheWall");
            // Blocked warband (Breach phase), a wall in reach, and a live hostile unit that IS
            // acquirable - first already in attack range, then merely route-open. Neither may
            // resolve the wall: "ordinary troops do NOT auto-attack walls while any hostile unit
            // ... exists" (ruling 1).
            int inRange = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: true, routeToUnitOpen: false, breachStance: false);
            if (inRange != 0)
                failures.Add(Tag + " a non-siege troop with a defender IN ATTACK RANGE must take the " +
                             "unit, not the wall (bucket " + inRange + ")");

            int routeOpen = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: true, breachStance: false);
            if (routeOpen != 0)
                failures.Add(Tag + " a non-siege troop with a REACHABLE defender must take the unit, " +
                             "not the wall (bucket " + routeOpen + ")");

            // ⛔ WO-1752 REVERSED THE THIRD ASSERTION THAT USED TO STAND HERE. It read:
            //   "an UNREACHABLE unit does NOT win ... that troop is the ruling's BLOCKED warband,
            //    and it takes the wall at 10% (case 9)"
            // and asserted `unreachable == 2`. The owner watched that ship and overruled it on
            // 2026-09-15: "we need to set it so that the troops 100% ignore walls, unless
            // explicitly told breach". A blocked, stance-less, non-siege troop now takes NOTHING.
            // The assertion is INVERTED rather than deleted, because the old behaviour coming back
            // is the exact regression the owner is watching for.
            //
            // ⚠ The wall is still passed as hasOtherStruct: true AND otherStructIsWall: true on
            // purpose - the candidate is still SEEN (it still prints as has[wall=True] on device);
            // what changed is that it is REFUSED. A case that proved the refusal by not offering a
            // wall would prove nothing.
            int unreachable = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false, breachStance: false,
                otherStructIsWall: true);
            if (unreachable == 2)
                failures.Add(Tag + " WO-1752: a stance-less non-siege troop took the WALL (bucket 2) " +
                             "- the retired 10% reluctant fallback is back. Owner ruling: troops " +
                             "100% ignore walls unless explicitly told breach");
            log.AppendLine("   reachable defender beats the wall; unreachable one now takes NO wall");
        }

        // ── 7b. (b) The stance holds the warband on the panel ───────────────────
        private static void Case_BreachStance_HoldsTheWarbandOnMasonry(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_BreachStance_HoldsTheWarbandOnMasonry");
            // ⚠ THIS IS THE ONE PLACE A READING OF THE RULING WAS CHOSEN - pinned here so a flip
            // cannot be silent. Ruling 2 and WO-1719 both say the stance runs "until the player
            // toggles Breach off or a hostile pulls AGGRO"; aggro is peelThreat, i.e. Peel phase.
            // A merely reachable, non-aggro'd defender therefore does NOT pull the warband off the
            // wall. (WO-1738's one-line status summary reads "units-first is the default inside
            // it", which would be the opposite; the two verbatim owner sources win. Flipping is
            // deleting `if (breachStance) return false;` in RaidAssaultAi.PreferUnit.)
            int stanceWall = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: true, routeToUnitOpen: true, breachStance: true,
                // WO-1752 strengthens this case: the candidate is declared to BE a wall, so the
                // assertion now also proves the new MayTargetWall gate lets the stance through.
                // Before, it passed with otherStructIsWall defaulted to false, i.e. against a
                // candidate the new gate never inspects.
                otherStructIsWall: true);
            if (stanceWall != 2)
                failures.Add(Tag + " with the Breach STANCE armed a non-aggro'd defender must not " +
                             "pull the warband off the panel (bucket " + stanceWall + ")");

            // Aggro still wins, exactly as WO-1719 shipped it: peelThreat -> Peel -> bucket 0.
            int aggro = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Peel, preferStructures: false, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: true, routeToUnitOpen: true, breachStance: true);
            if (aggro != 0)
                failures.Add(Tag + " an AGGRO'D troop must keep its fight even under an armed " +
                             "stance - 'all together unless they have aggro' (bucket " + aggro + ")");
            log.AppendLine("   stance holds masonry vs a calm defender; aggro still peels");
        }

        // ── 7c. (c) + (d) The multiplier itself ─────────────────────────────────
        private static void Case_WallDamageMultiplier_TheTenPercentRuling(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_WallDamageMultiplier_TheTenPercentRuling");

            // ⛔ WO-1752: THIS ASSERTION USED TO DEMAND 0.10 AND NOW DEMANDS 0.00. The owner
            // retired the reluctant fallback outright on 2026-09-15 after watching seven troops
            // chew masonry at wallDmgMult=0.10 while her hero died (device logcat 09-15 13:40:59).
            // The zero is a BACKSTOP, not the fix - RaidAssaultAi.MayTargetWall / PickBucket are
            // what stop the troop walking to the panel at all, and Case "WallsOff" below is the
            // one that proves THAT. This line exists so a future seat cannot restore the 10% by
            // "fixing" a number, and so the device token reads 0.00.
            float noStance = RaidAssaultAi.WallDamageMultiplier(
                preferStructures: false, breachStance: false);
            if (Mathf.Abs(noStance) > 0.0001f)
                failures.Add(Tag + " WO-1752: a stance-less non-siege troop must land ZERO wall " +
                             "damage (the 10% reluctant fallback is retired) - got " + noStance);

            float underStance = RaidAssaultAi.WallDamageMultiplier(
                preferStructures: false, breachStance: true);
            if (Mathf.Abs(underStance - 1f) > 0.0001f)
                failures.Add(Tag + " under an armed Breach stance the wall takes FULL damage - got " +
                             underStance);

            float siegeNoStance = RaidAssaultAi.WallDamageMultiplier(
                preferStructures: true, breachStance: false);
            float siegeStance = RaidAssaultAi.WallDamageMultiplier(
                preferStructures: true, breachStance: true);
            if (Mathf.Abs(siegeNoStance - 1f) > 0.0001f || Mathf.Abs(siegeStance - 1f) > 0.0001f)
                failures.Add(Tag + " SIEGE hits walls at full damage REGARDLESS of the stance " +
                             "(got " + siegeNoStance + " / " + siegeStance + ")");

            // WO-1752: the assertion that used to pin RaidAssaultAi.ReluctantWallDamageMultiplier
            // == 0.1f is GONE because the constant is gone. The predicate that replaced it is
            // pinned instead - one home, as before, just a different shape.
            if (RaidAssaultAi.MayTargetWall(preferStructures: false, breachStance: false))
                failures.Add(Tag + " WO-1752: MayTargetWall said a stance-less non-siege troop may " +
                             "target a wall - the owner's 'troops 100% ignore walls' is undone");
            if (!RaidAssaultAi.MayTargetWall(preferStructures: true, breachStance: false))
                failures.Add(Tag + " WO-1752: SIEGE must keep targeting walls with no stance - " +
                             "owner, same minute: 'Siege still hits walls on its own'");
            if (!RaidAssaultAi.MayTargetWall(preferStructures: false, breachStance: true))
                failures.Add(Tag + " WO-1752: an armed Breach stance must let any troop target the " +
                             "wall - that is what the button means");
            log.AppendLine("   0.00 without stance / 1.00 under stance / 1.00 siege either way");
        }

        // ── 7d. (e) The stance auto-chains past the panel it just felled ────────
        private static void Case_StanceAutoChains_WhenTheOrderedWallFalls(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_StanceAutoChains_WhenTheOrderedWallFalls");
            var ordered = new StubWall { HpValue = 30f, Pos = new Vector3(25f, 0f, 0f), Label = "ordered" };
            var chipped = new StubWall { HpValue = 12f, Pos = Vector3.zero, Label = "chipped" };
            var fullFar = new StubWall { HpValue = 100f, Pos = new Vector3(40f, 0f, 0f), Label = "fullFar" };
            var scan = new List<IDamageable> { chipped, fullFar };

            TroopBreachOrder.SetStanceArmed(true);
            TroopBreachOrder.Set(ordered);
            if (!TroopBreachOrder.StanceActive)
                failures.Add(Tag + " arming Breach and tapping a panel must read as an ACTIVE stance");

            // The panel falls. Today's behaviour is KEPT: the ORDER self-clears. What must NOT
            // happen is the STANCE clearing with it - that is the auto-chain the owner ruled for
            // ("no per-wall tap tax under the 180 s clock").
            ordered.HpValue = 0f;
            if (TroopBreachOrder.HasOrder)
                failures.Add(Tag + " the ordered panel collapsing must still self-clear the ORDER " +
                             "(WO-1719 behaviour is kept, not replaced)");
            if (!TroopBreachOrder.StanceActive)
                failures.Add(Tag + " the stance must SURVIVE the ordered wall falling - that is the " +
                             "auto-chain; disarming here reintroduces the per-wall tap tax");

            var next = RaidAssaultAi.SelectFocusBreach(scan, Vector3.zero, TroopBreachOrder.Target);
            if (!ReferenceEquals(next, chipped))
                failures.Add(Tag + " after the ordered wall fell the automatic most-damaged rule " +
                             "must pick the next panel (picked '" + LabelOf(next) + "')");

            float mult = RaidAssaultAi.WallDamageMultiplier(false, TroopBreachOrder.StanceActive);
            if (Mathf.Abs(mult - 1f) > 0.0001f)
                failures.Add(Tag + " the auto-chained next panel must still be hit at FULL damage " +
                             "while the stance is armed - got " + mult);

            // And the player's cancel really does end it.
            TroopBreachOrder.Clear();
            if (TroopBreachOrder.StanceActive)
                failures.Add(Tag + " the player's cancel (toggle off / retreat / teardown) must " +
                             "disarm the stance");
            log.AppendLine("   order self-clears, stance survives, next panel picked at 1.00");
        }

        // ── 7e. The stance, not the order, is what releases the rally march ─────
        private static void Case_StanceReleasesTheRallyMarch(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_StanceReleasesTheRallyMarch");
            // WO-1746 widens WO-1719's rule. Without it the auto-chain above dies the moment a
            // rally is set - which WO-1719's own remarks say is most of the time mid-raid: the
            // ordered panel falls, HasOrder goes false, the march holds again, the wall bucket is
            // nulled and the warband walks back to the flag instead of opening the next panel.
            if (!RaidAssaultAi.RallyHoldsMarch(true, false, false, false))
                failures.Add(Tag + " with NO stance the rally must still hold the march (the " +
                             "implicit ring-farm suppression, owner 2026-09-12, is untouched)");
            if (RaidAssaultAi.RallyHoldsMarch(true, false, false, true))
                failures.Add(Tag + " an ACTIVE breach stance must release the rally march");
            log.AppendLine("   stance releases the march; ring-farm suppression intact");
        }

        // ── 7f. The ruling is on the LIVE path, not just in the pure rules ──────
        private static void Case_RulingWiredIntoTheLivePath(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_RulingWiredIntoTheLivePath");
            // A pure rule nothing calls passes every case above and ships nothing - the same
            // failure mode Case 6 and RaidAssaultAiRegression.Case_AllowNonObjectiveWiredIntoPickBucket
            // exist to catch.
            string controller = Read("Assets/_Modules/Village/Troops/TroopController.cs");
            if (controller == null)
            {
                failures.Add(Tag + " TroopController.cs could not be read");
            }
            else
            {
                if (controller.IndexOf("RaidAssaultAi.WallDamageMultiplier(", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " TroopController never calls WallDamageMultiplier - the wall " +
                                 "damage ruling is a pure rule with no caller");
                if (controller.IndexOf("rallySet, arrivedAtRally, peelThreat, breachStance",
                        StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the rally march is no longer released by the STANCE - the " +
                                 "auto-chain dies on the first wall whenever a rally is set");
                if (controller.IndexOf("SWING target=", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " TroopController.Attack emits no SWING trace - 'did the troop " +
                                 "actually swing at the wall, and at what multiplier' is unprovable " +
                                 "from a device log again (WO-1723 sec.5 / WO-1730 sec.4)");

                // THE BUG THIS ASSERTION EXISTS FOR: Attack()'s catalog-multiplier block is gated
                // on `_preferStructures || _structureDamageMult != 1f || _unitDamageMult != 1f`,
                // and an ordinary Footman fails all three. A reluctant multiplier applied INSIDE
                // that block would apply to nobody while the trace printed 0.10.
                int attackAt = controller.IndexOf("private void Attack(IDamageable foe)", StringComparison.Ordinal);
                if (attackAt < 0)
                {
                    failures.Add(Tag + " TroopController.Attack not found - re-point this case");
                }
                else
                {
                    int gateAt = controller.IndexOf("_unitDamageMult != 1f)", attackAt, StringComparison.Ordinal);
                    int wallAt = controller.IndexOf("RaidAssaultAi.WallDamageMultiplier(", attackAt, StringComparison.Ordinal);
                    if (gateAt < 0 || wallAt < 0 || wallAt < gateAt)
                        failures.Add(Tag + " the reluctant wall multiplier must be applied AFTER / " +
                                     "OUTSIDE the catalog-multiplier gate in Attack - an ordinary " +
                                     "troop has every catalog mult at default and would be skipped");
                    if (controller.IndexOf("foe is WallSegment", attackAt, StringComparison.Ordinal) < 0)
                        failures.Add(Tag + " the reluctance must be scoped to WALL panels - the " +
                                     "ruling says 'the nearest blocking wall', and widening it to " +
                                     "every structure is a silent across-the-board nerf");
                }
            }

            string deploy = Read("Assets/_Modules/Village/Troops/RaidDeployController.cs");
            if (deploy == null)
                failures.Add(Tag + " RaidDeployController.cs could not be read");
            else if (deploy.IndexOf("TroopBreachOrder.SetStanceArmed(true)", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " nothing ARMS the breach stance - the Breach button no longer " +
                             "declares 'we are breaching' and every troop stays reluctant");
            log.AppendLine("   multiplier, stance arm, rally release and SWING trace all on the live path");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // WO-1752 — owner rulings from the IronBastion felt-test, 2026-09-15.
        //   1. "we need to set it so that the troops 100% ignore walls, unless explicitly told
        //      breach"  -> the WO-1746 10% reluctant fallback is DELETED, not tuned.
        //   2. Same minute, on the catapult: "Siege still hits walls on its own."
        //   3. "they are running around attacking walls while i am getting damaged and killed"
        //      -> a hostile damaging the HERO is acquirable by every troop regardless of its own
        //      sweep radius. Flagged in WO-1752 as the lead's reading, owner to veto.
        //
        // RED PROOF for each case is named on the case itself. What was EXECUTED rather than
        // reasoned is recorded in WORK_ORDER_1752...RESULT.md - do not read a red proof written
        // here as a run that happened.
        // ═══════════════════════════════════════════════════════════════════════

        // ── 13. Ruling 1: walls are OFF for a non-siege troop without the stance ─
        private static void Case_WallsOff_NonSiegeWithoutStancePicksNoWall(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_WallsOff_NonSiegeWithoutStancePicksNoWall");
            // The owner's exact situation, in arguments: Breach phase, no stance, no siege, no
            // unit this troop can reach, nothing but a blocking wall in front of it. WO-1746
            // answered 2 (the wall, at 10%). WO-1752 answers -1: the troop stands.
            int blocked = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: false,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false, breachStance: false,
                otherStructIsWall: true);
            if (blocked != -1)
                failures.Add(Tag + " WO-1752 ruling 1: a non-siege troop with NO stance must pick " +
                             "NOTHING when the only candidate is a wall - got bucket " + blocked);

            // ⭐ AND THE HALF THAT STOPS THIS BECOMING AN ACROSS-THE-BOARD MASONRY NERF. Bucket 2
            // also carries TOWERS and gates, which WO-1746 sec.4B.3 records were never in the
            // ruling (they keep full damage). A troop being shot by a tower must still be able to
            // hit it back, so the refusal is scoped to the candidate's TYPE, not to the bucket.
            // RED PROOF: change PickBucket's mayWall clause from `!otherStructIsWall ||
            // MayTargetWall(...)` to a bare `MayTargetWall(...)` and this assertion fails while
            // the one above still passes - which is precisely the over-reach it is here to catch.
            int tower = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: false,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false, breachStance: false,
                otherStructIsWall: false);
            if (tower != 2)
                failures.Add(Tag + " WO-1752: the wall refusal must be scoped to WALL panels - a " +
                             "TOWER is still a legal target for a stance-less troop (bucket " +
                             tower + ")");

            // The stance is the door back in, and it is the only one for a non-siege troop.
            int stanced = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: false,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false, breachStance: true,
                otherStructIsWall: true);
            if (stanced != 2)
                failures.Add(Tag + " WO-1752: with the Breach stance ARMED the same troop must take " +
                             "the wall - got bucket " + stanced);
            log.AppendLine("   stance-less non-siege: wall refused (-1), tower still legal, stance re-opens it");
        }

        // ── 14. Ruling 2: the catapult is untouched ─────────────────────────────
        private static void Case_SiegeStillTakesTheWall(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_SiegeStillTakesTheWall");
            // Owner, verbatim, when asked whether the catapult follows ruling 1: "Siege still hits
            // walls on its own." With AND without the stance, at FULL damage both ways.
            int siegeNoStance = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: true, hasUnit: false,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false, breachStance: false,
                otherStructIsWall: true);
            if (siegeNoStance != 2)
                failures.Add(Tag + " WO-1752 ruling 2: SIEGE must still take the wall with no " +
                             "stance - got bucket " + siegeNoStance);

            // The one that would be missed by testing siege in isolation: a defender standing in
            // the catapult's face must STILL not pull it off the masonry (WO-1746's siege rule).
            int siegeWithDefender = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: true, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: true, routeToUnitOpen: true, breachStance: false,
                otherStructIsWall: true);
            if (siegeWithDefender != 2)
                failures.Add(Tag + " WO-1752 must not have changed siege: a defender in range still " +
                             "does not pull the catapult off the wall (bucket " + siegeWithDefender + ")");

            float siegeMult = RaidAssaultAi.WallDamageMultiplier(
                preferStructures: true, breachStance: false);
            if (Mathf.Abs(siegeMult - 1f) > 0.0001f)
                failures.Add(Tag + " WO-1752 ruling 2: siege wall damage must stay FULL - got " + siegeMult);
            log.AppendLine("   siege takes the wall with and without the stance, at 1.00");
        }

        // ── 15. Ruling 3: the warband breaks off to whatever is hitting the hero ─
        private static void Case_IdleTroopAdoptsTheHerosAttacker(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_IdleTroopAdoptsTheHerosAttacker");
            // The captured shape this is written from (WO-1752 evidence, device 09-15 13:55:29):
            //   id=troop-shieldguard role=tank IDLE/RALLY: no acquirable hostile inside radius=12.0m
            // - while the hero was being killed. hasUnitInOwnSweep=false + a live hero attacker.
            if (!RaidAssaultAi.AdoptHeroAttacker(hasUnitInOwnSweep: false, heroAttackerLive: true))
                failures.Add(Tag + " WO-1752 ruling 3: a troop with NOTHING in its own sweep must " +
                             "adopt the hero's live attacker - the warband defends the player");

            // A troop already fighting something of its own is NOT yanked across the map: that
            // would thin the line the owner is standing in. WO-1752 states the rule in exactly
            // those words ("A troop with no unit in its own radius but a live hero-attacker").
            if (RaidAssaultAi.AdoptHeroAttacker(hasUnitInOwnSweep: true, heroAttackerLive: true))
                failures.Add(Tag + " WO-1752 ruling 3: a troop with its own unit in range must keep " +
                             "that fight, not break off to the hero's attacker");

            if (RaidAssaultAi.AdoptHeroAttacker(hasUnitInOwnSweep: false, heroAttackerLive: false))
                failures.Add(Tag + " WO-1752 ruling 3: with no live hero attacker there is nothing " +
                             "to adopt");

            // ⛔ THE HALF THIS CASE EXISTS FOR, AND IT IS WO-1746 sec.4 - DO NOT RE-LITIGATE IT.
            // Adoption must NOT become a second thing that breaks an armed Breach stance; AGGRO
            // (the Peel phase) stays the only one. Proven at the selector: an armed stance with an
            // adopted unit present and even reachable still resolves the WALL, exactly as Case 8
            // pins for a calm defender - because adoption changes hasUnit, never the phase.
            int stanceHolds = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: true, routeToUnitOpen: true, breachStance: true,
                otherStructIsWall: true);
            if (stanceHolds != 2)
                failures.Add(Tag + " WO-1752 must not have weakened the Breach stance: an adopted " +
                             "hero-attacker is still just a unit, and the stance outranks it " +
                             "(WO-1746 sec.4, bucket " + stanceHolds + ")");

            // And with the stance OFF, the adopted attacker outranks the wall - which is the whole
            // felt complaint, resolved: "running around attacking walls while i am getting killed".
            int defendsHer = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false, hasUnit: true,
                hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: true, breachStance: false,
                otherStructIsWall: true);
            if (defendsHer != 0)
                failures.Add(Tag + " WO-1752 ruling 3: with no stance, a reachable adopted attacker " +
                             "must beat the wall - got bucket " + defendsHer);
            log.AppendLine("   adopted when idle only; never breaks the stance; beats the wall without one");
        }

        // ── 16. WO-1752 is on the LIVE path, not just in the pure rules ─────────
        private static void Case_WO1752WiredIntoTheLivePath(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_WO1752WiredIntoTheLivePath");
            // The failure mode Case 12 exists for, applied to this ticket: a pure rule nothing
            // calls passes every case above and ships nothing. WO-1752's ruling 3 is especially
            // exposed to it - AdoptHeroAttacker is a two-line predicate that is trivially green
            // and trivially unreferenced.
            string ai = Read("Assets/_Modules/Village/Troops/RaidAssaultAi.cs");
            if (ai == null)
            {
                failures.Add(Tag + " RaidAssaultAi.cs could not be read");
            }
            else if (ai.IndexOf("public const float ReluctantWallDamageMultiplier",
                         StringComparison.Ordinal) >= 0)
            {
                failures.Add(Tag + " WO-1752: ReluctantWallDamageMultiplier is back. The owner " +
                             "deleted the 10% path; re-adding the constant is how the fallback " +
                             "returns one call site at a time");
            }

            string controller = Read("Assets/_Modules/Village/Troops/TroopController.cs");
            if (controller == null)
            {
                failures.Add(Tag + " TroopController.cs could not be read");
                log.AppendLine("   (skipped - controller unreadable)");
                return;
            }

            if (controller.IndexOf("otherStructIsWall", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " WO-1752: TroopController never tells PickBucket whether the " +
                             "masonry candidate IS a wall - the selector then treats every wall as " +
                             "a tower and the ruling applies to nobody");
            if (controller.IndexOf("nearestOtherStruct is WallSegment", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " WO-1752: the wall/not-wall verdict must be read off the LIVE " +
                             "candidate (nearestOtherStruct is WallSegment), never assumed");
            if (controller.IndexOf("HeroAggroTarget.Current", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " WO-1752 ruling 3: nothing reads HeroAggroTarget - the warband " +
                             "still cannot see what is hitting the hero");
            if (controller.IndexOf("RaidAssaultAi.AdoptHeroAttacker(", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " WO-1752 ruling 3: AdoptHeroAttacker is a pure rule with no " +
                             "caller");
            if (controller.IndexOf("heroAttacker='", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " WO-1752 sec.3: the RETARGET line carries no heroAttacker= " +
                             "token - 'did the warband even know she was being hit' is unprovable " +
                             "from a device log");
            if (controller.IndexOf("mayWall=", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " WO-1752 sec.3: the AI line carries no mayWall= token - " +
                             "has[wall=True] alone cannot tell a REFUSED panel from no panel");

            // ⭐ THE ORDERING ASSERTION, AND IT IS THE ONE WITH TEETH. Adopting the hero's attacker
            // AFTER the bucket has been picked changes nothing at all, compiles, traces correctly
            // and ships a no-op. So the adoption must appear BEFORE the PickBucket call in the
            // same file. RED PROOF: move the adoption block below the PickBucket call and this
            // fails while every pure case above still passes.
            int adoptAt = controller.IndexOf("RaidAssaultAi.AdoptHeroAttacker(", StringComparison.Ordinal);
            int pickAt = controller.IndexOf("int bucket = RaidAssaultAi.PickBucket(", StringComparison.Ordinal);
            if (adoptAt >= 0 && pickAt >= 0 && adoptAt > pickAt)
                failures.Add(Tag + " WO-1752 ruling 3: the hero-attacker adoption runs AFTER the " +
                             "bucket is picked - it cannot affect the choice and ships a no-op");

            string publisher = Read("Assets/_Modules/Village/Troops/HeroAggroTarget.cs");
            if (publisher == null)
                failures.Add(Tag + " WO-1752: HeroAggroTarget.cs is missing - ruling 3 has no source");
            else if (publisher.IndexOf("RaidAssaultAi.PeelHurtWindowSeconds", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " WO-1752: HeroAggroTarget minted its own hurt window instead of " +
                             "reusing PeelHurtWindowSeconds - duplicated state (CLAUDE.md sec.2/5/16)");
            log.AppendLine("   wall type, adoption, ordering, trace tokens and the publisher all on the live path");
        }

        private static string Read(string relative)
        {
            try
            {
                string full = Path.Combine(Directory.GetCurrentDirectory(), relative);
                return File.Exists(full) ? File.ReadAllText(full) : null;
            }
            catch { return null; }
        }

        private static string LabelOf(IDamageable d)
        {
            var w = d as StubWall;
            if (w != null) return w.Label;
            var u = d as StubUnit;
            if (u != null) return u.Label;
            return "<null>";
        }

        private sealed class StubWall : IDamageable
        {
            public float HpValue;
            public Vector3 Pos;
            public string Label;
            public CombatFaction Faction { get { return CombatFaction.Hostile; } }
            public Vector3 WorldPosition { get { return Pos; } }
            public float Hp { get { return HpValue; } }
            public bool IsAlive { get { return HpValue > 0f; } }
            public void TakeDamage(float amount, DamageElement element) { }
            public void ApplyStatus(StatusEffect effect, float seconds) { }
        }

        private sealed class StubUnit : IDamageable
        {
            public string Label;
            public CombatFaction Faction { get { return CombatFaction.Hostile; } }
            public Vector3 WorldPosition { get { return Vector3.zero; } }
            public float Hp { get { return 50f; } }
            public bool IsAlive { get { return true; } }
            public void TakeDamage(float amount, DamageElement element) { }
            public void ApplyStatus(StatusEffect effect, float seconds) { }
        }
    }
}
