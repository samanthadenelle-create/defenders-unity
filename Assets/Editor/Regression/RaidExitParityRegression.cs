// =============================================================================
// RaidExitParityRegression [raid-exit-parity]  --  markers RAID_EXIT_PARITY_OK / _FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Source-lint (edit mode, no PlayMode).
// Registered in DataRegression.RunAll.  NEVER throws.
//
// Locks the three WO-1110 rulings so they cannot silently rot back:
//
//   PIN 1  EXIT PARITY (WO-1110 sec.3).  A raid has three non-victory exits and they
//          must PAY THE SAME. Hero death used to reconcile the army and stop - it
//          never called RaidScoring.Finalize/LootFor - so a player who razed two
//          thirds of a base and then FELL got LESS than one who razed the same and
//          tapped Retreat. That is the inverse of the perverse incentive the
//          retreat-loot block was written to remove. Both exits now funnel through
//          the ONE authority, RaidDeployController.SettlePartialLoot, and this pin
//          asserts (a) the authority exists, (b) retreat calls it and does not fork
//          its own Finalize, (c) hero death calls it, BEFORE the army reconcile, in
//          the same order retreat uses.
//
//   PIN 2  THE SOFTLOCK ORDER (WO-1110 sec.1).  Start() must bind the clock-expiry
//          subscriber (BindScoringRoutine) BEFORE it builds the HUD, and must build
//          the HUD inside a Guard.Try. With the old order a throw inside BuildHud
//          skipped the StartCoroutine line entirely: no tray, no Retreat button AND
//          no 180s timeout rescue - the raid's only exitless state. The ORDER is the
//          fix; the Guard is the seatbelt. Both are asserted, because either one
//          alone still leaves a hole.
//
//   PIN 3  NO SILENT CATCH (WO-1110 sec.2 / CLAUDE.md sec.12).  The four named sites
//          must each carry a FlowTrace line, and no raid runtime file may contain a
//          bare `catch { }` again. The expensive one is RaidScoring's reward
//          multiplier: a catalog miss silently paid x1 where the card promised x2.2,
//          a 55% pay cut with no trace anywhere.
//
// WO-1768 adds three more, all from ONE device capture (owner's Seeker, build
// 2026.09.16.371701, logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt,
// raid IronBastion): the hero died 1.93s BEFORE the spire fell, i.e. INSIDE the 1.75s
// down-beat, and three separate things went wrong in that window.
//
//   PIN 4  THE CAP LATCHES AT THE DEATH, NOT AFTER THE DOWN-BEAT.  RaidScoring
//          .NotifyHeroDied used to be called inside HandleDeath AFTER its
//          `yield return new WaitForSeconds(_downSeconds)`, so a raid that concluded
//          inside the down-beat settled with the scorer still believing the hero was
//          alive:  13:26:03.396 "[HeroHealth] Hero defeated." ->
//          13:26:05.337 "stars settled: 2 (earned=3 heroDied=False cap=none honor=2
//          clamped=min(3,2))".  WO-1526's 2-star hero-death cap was NOT applied to a
//          raid the hero died in; it read 2 only because honor independently clamped
//          3 -> 2, so the same sequence with honor=3 pays 3 stars AND veterancy to a
//          player who fell.  The call must appear BEFORE that wait.
//
//   PIN 5  THE FAILURE-SETTLE TRACE IS CONDITIONAL.  Both army-side settles on the
//          hero-death exit are LATCHED and both announce their own no-op (logcat
//          3059909 / 3059910), yet the next line announced "army settled as a failure
//          (0 stars); the troops still standing break and flee home, the fallen are
//          wounded" unconditionally (3059911).  Nothing had happened - army 10 /
//          deployable=10 were read 0.09s later.  A trace that asserts work that did not
//          run turns a clean capture into a false lead.  The line must sit inside a test
//          of RaidDeployController.Reconciled, and the truthful opposite line must exist
//          beside it (CLAUDE.md sec.12: make the false trace TRUE, never delete it).
//
//   PIN 6  THE EVAC BRANCH YIELDS TO A VICTORY SCREEN THAT IS UP.  The screen opened at
//          13:26:05.398, the owner touched it at .594 (re-arming WO-1543's full 30s), and
//          HeroHealth's EVAC branch called SceneRouter.GoCastle() at .617:
//          "SCREEN CLOSED: EndState 'Victory!' by EndStateView.OnDestroy (torn down
//          without firing)" at 06.180 - a won raid's victory screen readable for 0.78
//          SECONDS, mid-touch.  HeroHealth must consult
//          RaidVictoryController.VictoryOwnsTheReturn and stand down BEFORE the
//          `enemyOwnedScene || raidDeathExit` test - and must log a Warn on the
//          fall-through, because a bare `yield break` would strand a dead hero on an
//          enemy-owned field (the WO-1437 defect the EVAC branch exists to prevent).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class RaidExitParityRegression
    {
        // Relative to Application.dataPath.
        private const string CtrlRel   = "_Modules/Village/Troops/RaidDeployController.cs";
        private const string ScoreRel  = "_Modules/Village/Troops/RaidScoring.cs";
        private const string HeroRel   = "_Modules/Village/Hero/HeroHealth.cs";
        private const string VmRel     = "_Modules/Village/Hero/RaidDeployVM.cs";
        private const string SelRel    = "_Modules/Village/Hero/RaidSelectionScreen.cs";
        private const string ScreenRel = "_Modules/Village/Hero/RaidDeployScreen.cs";
        // WO-1768 - PIN 6 needs the victory side too: the accessor the death path yields on
        // must still be declared, and must still be set only after EndStateView.Show returned.
        private const string VictRel   = "_Modules/Village/World/Camps/RaidVictoryController.cs";

        // Declared as a balanced PAIR on one line on purpose (RegressionMarkerRegression's
        // precedent): a lone brace char literal trips the CLAUDE.md rule-1 brace counter.
        private const char OpenBrace = '{', CloseBrace = '}';

        /// <summary>A catch block that swallows without logging - CLAUDE.md sec.12 forbids it.</summary>
        private static readonly Regex BareCatch = new Regex(
            @"catch\s*(?:\([^)]*\))?\s*\{\s*\}", RegexOptions.Compiled);

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "raid-exit-parity: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Standalone batch entry.</summary>
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("RAID_EXIT_PARITY_OK - " + reason);
            else Debug.LogError("RAID_EXIT_PARITY_FAIL - " + reason);
        }

        private static bool RunCore(out string reason)
        {
            var fails = new List<string>();

            // Comments are stripped from every file BEFORE any match: this suite's own
            // explanatory comments in those files name the very symbols it looks for
            // (SettlePartialLoot, Finalize, ReconcileRaidEnd), and a comment mention
            // would read as a call - the ordering pin especially would go blind.
            string ctrl   = ReadCode(CtrlRel,   fails);
            string score  = ReadCode(ScoreRel,  fails);
            string hero   = ReadCode(HeroRel,   fails);
            string vm     = ReadCode(VmRel,     fails);
            string sel    = ReadCode(SelRel,    fails);
            string screen = ReadCode(ScreenRel, fails);
            string vict   = ReadCode(VictRel,   fails);

            // -----------------------------------------------------------------
            //  PIN 1 - exit parity: ONE settlement authority, both exits use it
            // -----------------------------------------------------------------
            if (!ctrl.Contains("public void SettlePartialLoot("))
                fails.Add("RaidDeployController no longer exposes `public void SettlePartialLoot(` - " +
                          "the single non-victory loot authority is gone, so the retreat and death " +
                          "exits have nothing to share and will drift apart again (WO-1110 sec.3)");

            string settleBody = Body(ctrl, @"void\s+SettlePartialLoot\s*\([^)]*\)");
            if (string.IsNullOrEmpty(settleBody))
                fails.Add("could not locate SettlePartialLoot's body in RaidDeployController");
            else
            {
                if (!settleBody.Contains("Finalize(false)"))
                    fails.Add("SettlePartialLoot does not call RaidScoring.Finalize(false) - nothing settles the score");
                if (!settleBody.Contains("LootFor("))
                    fails.Add("SettlePartialLoot does not call LootFor(...) - no loot is computed from the settled result");
                if (!settleBody.Contains("GrantRetreatLoot("))
                    fails.Add("SettlePartialLoot does not call GrantRetreatLoot(...) - the loot is computed and then never paid");
            }

            // WO-1561 widened the signature to DoRetreat(string reason) so the timeout exit can
            // name itself on the shared result screen. The pattern follows it rather than
            // matching an empty-bodied forwarder - a lint that locks onto the wrong overload
            // passes while every assertion below it silently stops applying.
            string retreatBody = Body(ctrl, @"void\s+DoRetreat\s*\(");
            if (string.IsNullOrEmpty(retreatBody))
                fails.Add("could not locate DoRetreat's body in RaidDeployController");
            else
            {
                if (!retreatBody.Contains("SettlePartialLoot("))
                    fails.Add("DoRetreat does not route through SettlePartialLoot - the retreat/timeout exit " +
                              "has forked away from the shared settlement (WO-1110 sec.3)");
                if (retreatBody.Contains("Finalize("))
                    fails.Add("DoRetreat calls Finalize(...) directly again - a second settlement path means " +
                              "retreat and death can pay differently, which is the exact bug WO-1110 closed");

                // -------------------------------------------------------------
                //  WO-1561 - A NON-VICTORY EXIT MAY NOT ROUTE TO TOWN IN SILENCE
                // -------------------------------------------------------------
                // THE DEFECT, MEASURED ON THE PRE-CHANGE TREE: DoRetreat ended
                // `SetStatus("Retreating to the castle..."); SceneRouter.GoCastle();`
                // and grep -c "EndStateVM\.|EndStateView.Show" on the whole file
                // returned 1 - a COMMENT. The clock-expiry exit funnels here too, and
                // nothing in town picked the outcome up either (every reader of
                // RaidResult is raid-scene-side), so the result was computed, BANKED
                // and discarded unread. A WIN got the full treatment; the exit a new
                // player is most likely to finish got no screen at all.
                //
                // The pin is a PAIR, because either half alone still leaves the hole:
                // the exit must SHOW a result, and it must not ALSO leave by itself.
                if (!retreatBody.Contains("ShowNonVictoryResult(") &&
                    !retreatBody.Contains("EndStateView.Show"))
                    fails.Add("DoRetreat routes home without showing an end state - the retreat/timeout exit " +
                              "settles the score, pays the loot, reconciles the army and then tells the player " +
                              "NOTHING: not razed %, not stars, not the spoils it just banked, not which troops " +
                              "came home wounded (WO-1561, P0)");
                if (Regex.IsMatch(retreatBody, @"SceneRouter\s*\.\s*GoCastle"))
                    fails.Add("DoRetreat calls SceneRouter.GoCastle directly again - the route home belongs to the " +
                              "result screen's primary action (and its re-armable guard), or the screen is shown " +
                              "and instantly abandoned by a scene load underneath it (WO-1561 / WO-1543)");
            }

            // The result screen exists and reports what was BANKED, not what was awarded. WO-1461
            // records the live case: the deploy card quoted ~1,800 wood and 25 arrived, because
            // the bank was full. A screen fed the REQUESTED loot would restate that lie.
            string showBody = Body(ctrl, @"void\s+ShowNonVictoryResult\s*\(");
            if (!string.IsNullOrEmpty(showBody))
            {
                if (!showBody.Contains("EndStateView.Show"))
                    fails.Add("RaidDeployController.ShowNonVictoryResult never calls EndStateView.Show - the " +
                              "non-victory result is composed and then dropped");
                if (!showBody.Contains("_retreatCredited"))
                    fails.Add("RaidDeployController.ShowNonVictoryResult does not feed the MEASURED credit " +
                              "(_retreatCredited) to the screen. It must report what the wallet actually took, " +
                              "never the loot that was awarded - at a capped town bank those differ, and the " +
                              "screen is what the player believes (WO-978 / WO-1461)");
            }

            // Hero death: it must settle loot, and settle it BEFORE the army reconcile,
            // matching DoRetreat's order (Finalize samples destruction/survival off the
            // live field, so reconciling first would score a torn-down raid).
            int iSettle = hero.IndexOf("SettlePartialLoot(", StringComparison.Ordinal);
            int iRecon  = hero.IndexOf("ReconcileRaidEnd(0)", StringComparison.Ordinal);
            if (iSettle < 0)
                fails.Add("HeroHealth's enemy-owned death branch does not call SettlePartialLoot - dying " +
                          "forfeits razing credit that retreating pays, punishing the more committed play " +
                          "(WO-1110 sec.3; owner default = death pays what retreat pays)");
            if (iRecon < 0)
                fails.Add("HeroHealth's enemy-owned death branch no longer calls ReconcileRaidEnd(0) - the " +
                          "death exit stopped settling the army");
            if (iSettle >= 0 && iRecon >= 0 && iSettle > iRecon)
                fails.Add("HeroHealth settles the army BEFORE the loot (SettlePartialLoot at " + iSettle +
                          ", ReconcileRaidEnd at " + iRecon + ") - the opposite of DoRetreat's order. " +
                          "Finalize samples destruction/survival off the live field, so the two exits " +
                          "would score differently even while calling the same method");

            // -----------------------------------------------------------------
            //  PIN 2 - the softlock: subscribe the clock BEFORE building the HUD
            // -----------------------------------------------------------------
            string startBody = Body(ctrl, @"void\s+Start\s*\(\s*\)");
            if (string.IsNullOrEmpty(startBody))
                fails.Add("could not locate RaidDeployController.Start's body");
            else
            {
                int iBind  = startBody.IndexOf("BindScoringRoutine", StringComparison.Ordinal);
                int iBuild = startBody.IndexOf("BuildHud", StringComparison.Ordinal);
                if (iBind < 0)
                    fails.Add("RaidDeployController.Start no longer starts BindScoringRoutine - the 180s " +
                              "OnTimeExpired subscriber is the raid's last-resort exit");
                if (iBuild < 0)
                    fails.Add("RaidDeployController.Start no longer builds the HUD");
                if (iBind >= 0 && iBuild >= 0 && iBind > iBuild)
                    fails.Add("RaidDeployController.Start builds the HUD BEFORE binding the raid clock. " +
                              "A throw inside BuildHud then skips the subscribe entirely: no tray, no " +
                              "Retreat button and no timeout rescue - the raid's ONLY exitless state " +
                              "(WO-1110 sec.1). Subscribe first, present second");
                if (iBuild >= 0 && !Regex.IsMatch(startBody, @"Guard\.Try\s*\([^;]*BuildHud"))
                    fails.Add("RaidDeployController.Start calls BuildHud outside a Guard.Try - every other " +
                              "risky op in that file is wrapped, and an unguarded presentation throw is what " +
                              "produced the softlock (CLAUDE.md sec.12)");
            }

            // The fault-injection hook the acceptance proof depends on.
            if (!ctrl.Contains("DebugForceBuildHudThrow"))
                fails.Add("the DebugForceBuildHudThrow injection hook is gone - the 'a HUD failure still " +
                          "leaves an exit' acceptance can no longer be PROVEN by injection, only argued");

            // -----------------------------------------------------------------
            //  PIN 3 - no silent catches in the raid runtime
            // -----------------------------------------------------------------
            CheckNoBareCatch(CtrlRel, ctrl, fails);
            CheckNoBareCatch(ScoreRel, score, fails);
            CheckNoBareCatch(VmRel, vm, fails);
            CheckNoBareCatch(SelRel, sel, fails);
            CheckNoBareCatch(ScreenRel, screen, fails);

            CheckTraced(score, @"float\s+ResolveRewardMultiplier\s*\(\s*\)",
                "RaidScoring.ResolveRewardMultiplier",
                "a catalog miss here silently pays x1 where the card promised x2.2 - a 55% pay cut " +
                "the player cannot see and no trace records", fails);
            CheckTraced(vm, @"string\s+ComputeArmyCapText\s*\(\s*\)",
                "RaidDeployVM.ComputeArmyCapText",
                "the army readout silently falls back to 'Army: -' with nothing saying why", fails);
            CheckTraced(sel, @"void\s+OnCardTapped\s*\([^)]*\)",
                "RaidSelectionScreen.OnCardTapped",
                "an unresolved card tap is a DEAD TAP - it reads to the player as a frozen game", fails);
            CheckTraced(screen, @"void\s+Open\s*\(\s*SceneConfigDef[^)]*\)",
                "RaidDeployScreen.Open",
                "a null def means the deploy screen never opens, with no player feedback at all", fails);

            // -----------------------------------------------------------------
            //  PIN 4 - the hero-death star cap latches AT THE DEATH (WO-1768)
            // -----------------------------------------------------------------
            // Offsets, not line numbers: the assertion is purely "which comes first in the
            // source". `.NotifyHeroDied()` is matched with its call parens AND its semicolon so
            // a mention inside a trace STRING (StripLineComments keeps string literals) can
            // never satisfy this pin with prose.
            string deathBody = Body(hero, @"IEnumerator\s+HandleDeath\s*\(\s*\)");
            var mNotify = Regex.Match(hero, @"\.NotifyHeroDied\s*\(\s*\)\s*;");
            if (string.IsNullOrEmpty(deathBody))
                fails.Add("could not locate HeroHealth.HandleDeath's body - the WO-1768 cap-ordering " +
                          "pin cannot be verified");
            else if (!mNotify.Success)
                fails.Add("HeroHealth no longer calls RaidScoring.NotifyHeroDied() anywhere - nothing " +
                          "latches the hero's death on the scorer, so WO-1526's 2-star hero-death cap " +
                          "is never applied to ANY raid (RaidScoring.Finalize reads _heroDied)");
            else
            {
                int iBody = hero.IndexOf(deathBody, StringComparison.Ordinal);
                var mWait = Regex.Match(deathBody, @"WaitForSeconds\s*\(\s*Mathf\.Max\s*\(\s*0\.1f\s*,\s*_downSeconds");
                if (!mWait.Success)
                    fails.Add("could not locate the down-beat `WaitForSeconds(Mathf.Max(0.1f, _downSeconds))` " +
                              "inside HeroHealth.HandleDeath - the WO-1768 cap-ordering pin has lost its " +
                              "reference point and must be re-anchored rather than left passing blind");
                else if (iBody >= 0 && mNotify.Index > iBody + mWait.Index)
                    fails.Add("HeroHealth latches the hero's death on the scorer AFTER the 1.75s down-beat " +
                              "wait (NotifyHeroDied at offset " + mNotify.Index + ", the WaitForSeconds at " +
                              (iBody + mWait.Index) + ") - WO-1768. A raid that concludes INSIDE the " +
                              "down-beat then settles with the scorer still believing the hero is alive. " +
                              "Captured cost: hero defeated 13:26:03.396, then `stars settled: 2 " +
                              "(earned=3 heroDied=False cap=none honor=2 clamped=min(3,2))` at 13:26:05.337 " +
                              "- the 2-star hero-death cap was NOT applied, and honor happened to clamp " +
                              "the same number. With honor=3 that pays 3 stars and veterancy to a player " +
                              "who fell. The latch belongs in BeginDeathSequence, beside `_isDead = true`");
            }

            // -----------------------------------------------------------------
            //  PIN 5 - the failure-settle narration must be TRUE (WO-1768)
            // -----------------------------------------------------------------
            if (!ctrl.Contains("public bool Reconciled"))
                fails.Add("RaidDeployController no longer exposes `public bool Reconciled` - the army " +
                          "reconcile latch is unanswerable again, so the hero-death exit cannot tell " +
                          "whether its own ReconcileRaidEnd(0) settled anything or was a latched no-op " +
                          "(WO-1768)");

            int iFalseLine = hero.IndexOf("army settled as a failure", StringComparison.Ordinal);
            if (iFalseLine < 0)
                fails.Add("HeroHealth no longer carries the `army settled as a failure` trace - it must " +
                          "be made TRUTHFUL, never deleted (CLAUDE.md sec.12 forbids stripping " +
                          "instrumentation; WO-1768 made it conditional, not absent)");
            else
            {
                int windowStart = Math.Max(0, iFalseLine - 400);
                string window = hero.Substring(windowStart, iFalseLine - windowStart);
                if (!Regex.IsMatch(window, @"if\s*\([^)]*[Rr]econcile[^)]*\)"))
                    fails.Add("HeroHealth's `army settled as a failure` trace is not inside a test of " +
                              "whether the reconcile actually RAN - it follows the two Guard.Try settles " +
                              "at the same brace depth and narrates work that may never have happened. " +
                              "On the 2026-09-16 IronBastion capture both settles announced their own " +
                              "no-op (`raid already finalized`, `raid-end reconcile already ran`) and " +
                              "this line still said the army broke and fled; army 10 / deployable=10 " +
                              "were read 0.09s later. Gate it on RaidDeployController.Reconciled (WO-1768)");
                if (hero.IndexOf(".Reconciled", StringComparison.Ordinal) < 0)
                    fails.Add("HeroHealth never reads RaidDeployController.Reconciled - whatever it is " +
                              "branching on, it is not the measured fact of whether the army was settled " +
                              "(WO-1768)");
                if (hero.IndexOf("NOT re-settled", StringComparison.Ordinal) < 0)
                    fails.Add("HeroHealth carries no `NOT re-settled` line - the TRUTHFUL half of the pair " +
                              "is gone, so a hero death on an already-settled raid narrates nothing at all. " +
                              "Both traces are kept and both are honest: that is the WO-1768 contract " +
                              "(CLAUDE.md sec.12)");
            }

            // -----------------------------------------------------------------
            //  PIN 6 - the EVAC branch yields to a victory screen that is UP (WO-1768)
            // -----------------------------------------------------------------
            if (!vict.Contains("public bool VictoryOwnsTheReturn"))
                fails.Add("RaidVictoryController no longer declares `public bool VictoryOwnsTheReturn` - " +
                          "the death path has nothing to ask, so a hero who dies inside the down-beat of " +
                          "a raid that is then WON routes home over the top of the victory screen " +
                          "(WO-1768)");
            else
            {
                int iShow   = vict.IndexOf("EndStateView.Show(", StringComparison.Ordinal);
                int iScrUp  = vict.IndexOf("_victoryScreenUp = true", StringComparison.Ordinal);
                if (iScrUp < 0)
                    fails.Add("RaidVictoryController never sets `_victoryScreenUp = true` - the accessor " +
                              "exists but nothing arms it (WO-1768)");
                else if (iShow >= 0 && iScrUp < iShow)
                    fails.Add("RaidVictoryController sets `_victoryScreenUp = true` BEFORE " +
                              "EndStateView.Show - the latch must only be set once Show has RETURNED, " +
                              "inside the existing try, so a presentation throw leaves it false and the " +
                              "catch's direct ReturnHome (plus the death path's EVAC) still owns the exit. " +
                              "Latching early re-creates the WO-1437 stranding this pin protects (WO-1768)");
            }

            var mEvacIf = Regex.Match(hero, @"if\s*\(\s*enemyOwnedScene\s*\|\|\s*raidDeathExit\s*\)");
            int iOwns   = hero.IndexOf("VictoryOwnsTheReturn", StringComparison.Ordinal);
            if (!mEvacIf.Success)
                fails.Add("could not locate HeroHealth's `if (enemyOwnedScene || raidDeathExit)` EVAC test " +
                          "- the WO-1768 yield-ordering pin has lost its reference point");
            else if (iOwns < 0)
                fails.Add("HeroHealth never consults RaidVictoryController.VictoryOwnsTheReturn - the " +
                          "EVAC branch routes home while a won raid's victory screen is on screen and " +
                          "being touched. Captured cost: `SCREEN OPENED: EndState 'Victory!'` 13:26:05.398, " +
                          "`GoCastle() by HeroHealth.HandleDeath` .617, `SCREEN CLOSED ... torn down " +
                          "without firing` 06.180 - 0.78 seconds of a victory screen, mid-touch (WO-1768)");
            else if (iOwns > mEvacIf.Index)
                fails.Add("HeroHealth consults VictoryOwnsTheReturn AFTER the `enemyOwnedScene || " +
                          "raidDeathExit` test (offsets " + iOwns + " vs " + mEvacIf.Index + ") - the EVAC " +
                          "branch has already routed home by then, so the check cannot save the screen " +
                          "(WO-1768)");
            else
            {
                string gap = hero.Substring(iOwns, mEvacIf.Index - iOwns);
                if (gap.IndexOf("yield break", StringComparison.Ordinal) < 0)
                    fails.Add("HeroHealth reads VictoryOwnsTheReturn but never stands down (`yield break`) " +
                              "before the EVAC test - it asks the question and evacuates anyway (WO-1768)");
                if (gap.IndexOf("FlowTrace.Warn", StringComparison.Ordinal) < 0)
                    fails.Add("the VictoryOwnsTheReturn stand-down has no FlowTrace.Warn on its " +
                              "fall-through path. The yield MUST be belted: RaidVictoryController" +
                              ".ReturnHome opens `if (!CanEnterCapturedTown()) return;` and legitimately " +
                              "refuses while the captured-town census retries, so a bare `yield break` " +
                              "would leave a DEAD hero stranded on an enemy-owned field - the WO-1437 " +
                              "defect the EVAC branch exists to prevent. The watchdog expiry must fall " +
                              "through to EVAC and say why (WO-1768)");
            }

            if (fails.Count == 0)
            {
                Debug.Log("RAID_EXIT_PARITY_OK");
                reason = "RAID EXIT PARITY OK -- one SettlePartialLoot authority shared by the retreat and " +
                         "hero-death exits (loot settled before the army reconcile on both); Start binds the " +
                         "raid clock before a Guard.Try'd BuildHud (no exitless state); 4 named catches traced " +
                         "and 0 bare catches across 5 raid runtime files; PIN 4 the hero-death star cap " +
                         "latches BEFORE the down-beat wait; PIN 5 the `army settled as a failure` trace is " +
                         "gated on RaidDeployController.Reconciled and its truthful `NOT re-settled` twin is " +
                         "kept; PIN 6 the EVAC branch consults RaidVictoryController.VictoryOwnsTheReturn " +
                         "(armed only after EndStateView.Show returned) and stands down with a Warn-belted " +
                         "fall-through before the enemyOwnedScene test";
                return true;
            }

            reason = "raid-exit-parity (" + fails.Count + "): " + string.Join(" | ", fails.ToArray());
            Debug.LogError("RAID_EXIT_PARITY_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static void CheckNoBareCatch(string rel, string code, List<string> fails)
        {
            if (string.IsNullOrEmpty(code)) return;   // the missing-file failure was already recorded
            if (BareCatch.IsMatch(code))
                fails.Add(rel + " contains a bare empty catch block again - CLAUDE.md sec.12 forbids a " +
                          "catch that swallows without logging (WO-1110 sec.2)");
        }

        private static void CheckTraced(string code, string signature, string label, string why, List<string> fails)
        {
            if (string.IsNullOrEmpty(code)) return;
            string body = Body(code, signature);
            if (string.IsNullOrEmpty(body))
            {
                fails.Add("could not locate " + label + "'s body - its silent-failure pin cannot be verified");
                return;
            }
            if (body.IndexOf("FlowTrace.", StringComparison.Ordinal) < 0)
                fails.Add(label + " emits no FlowTrace line on its failure path: " + why + " (WO-1110 sec.2)");
        }

        /// <summary>
        /// The brace-matched body of the first method whose signature matches, from the
        /// signature's opening brace to its balanced close. Brace-matched rather than
        /// indentation-matched so a nested block cannot end the extraction early.
        /// </summary>
        private static string Body(string code, string signaturePattern)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            var m = Regex.Match(code, signaturePattern);
            if (!m.Success) return string.Empty;
            int open = code.IndexOf(OpenBrace, m.Index + m.Length);
            if (open < 0) return string.Empty;
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == OpenBrace) depth++;
                else if (code[i] == CloseBrace)
                {
                    depth--;
                    if (depth == 0) return code.Substring(open, i - open + 1);
                }
            }
            return string.Empty;
        }

        /// <summary>Reads a file under Assets/ with // comments stripped; records a failure if missing.</summary>
        private static string ReadCode(string rel, List<string> fails)
        {
            string path = Path.Combine(Application.dataPath, rel);
            if (!File.Exists(path))
            {
                fails.Add("raid runtime file missing: " + rel);
                return string.Empty;
            }
            try { return StripLineComments(File.ReadAllText(path)); }
            catch (IOException ex)
            {
                fails.Add("could not read " + rel + ": " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>Strips // line comments (string-literal aware), preserving line structure.</summary>
        private static string StripLineComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            bool inStr = false, esc = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';
                if (inStr)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '/' && n == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
