// =============================================================================
// RaidPayoutVisibilityRegression - WO-1374 / WO-1375, Lane B.
// "Raid -> get richer" is only real if the player is TOLD they got richer, and
// a victory only feeds the ladder if something COUNTS it.
// -----------------------------------------------------------------------------
// Standalone:
//   -Method DeNelle.Editor.Regression.RaidPayoutVisibilityRegression.RunStandalone
//
// =============================================================================
//  THE FOUR DEFECTS THIS PINS. Every one of them was live in the tree on
//  2026-09-04 and every oracle below was proven RED against it first.
// -----------------------------------------------------------------------------
//  PIN A  THE VICTORY SCREEN NAMED A CURRENCY THAT DOES NOT EXIST, AND HID MOST
//         OF THE PAYOUT. EndStateVM.FromRaidVictory took exactly TWO ints
//         (lootCrystals, lootFood) and emitted two rows: "Crystals" and "Stone".
//         Wood, iron and gold were MEASURED by RaidVictoryController.GrantLoot
//         (dw / di / dg) and then thrown away, never reaching the screen. Raids
//         pay all five currencies (PROGRAM_RAID_ECONOMY_2026-09-04 section 1:
//         1,800 wood / 1,100 iron / 3,000 food / 2,200 gold / 20-30 crystals on a
//         perfect Camp I), so a three-star clear reported two fifths of what it
//         paid - one fifth of it under a retired name.
//
//  PIN B  NOTHING COUNTED A RAID WIN. Section 4's ladder unlocks target 2 after 3
//         victories, target 3 after 10 and the Iron Bastion after 20, and the
//         input did not exist. RaidClaimService's per-camp claim flags cannot
//         answer it (clearing one camp twice adds nothing to a SET) and
//         GameState.EverCompletedRaid is a bool that a RETREAT also sets.
//
//  PIN C  CLEARING A BAKED RAID CAMP DID NOT ADVANCE "BREAK A CAMP". The only
//         ticker for combat.raid.* in the whole tree was EnemyOutpost.cs:703, the
//         OuterWorld outpost - so the daily quest whose own label is
//         "Break a camp - clear 1 enemy outpost" ignored the player breaking a camp.
//
//  PIN D  A RAID WIN REACHED THE SEASON PASS THROUGH NO DOOR. Section 6 routes
//         raid outcomes into the existing 30-tier pass; ArenaOutcomeRelay grew the
//         raid overload and the raid victory seam never called it.
//
// =============================================================================
//  AND IT PINS THE SHAPE OF THE CURE, NOT JUST ITS PRESENCE.
// -----------------------------------------------------------------------------
//   * EXACTLY ONE report and EXACTLY ONE season publish per settle. Both live
//     after HandleVictory's `_handled` latch, which is the one de-duplicated
//     settle seam; a second call site is a double-counted win, i.e. the ladder
//     skipping a tier and the pass paying twice.
//   * The season publish stays OUTCOME-TYPED. No XP amount crosses the seam -
//     that is the door owner ruling Q4 closed, and BattleMonthlyRegression's
//     [xp-one-door] case guards the other side of it.
//   * The victory count is MONOTONIC and PERSISTED, and an older save is
//     BACKFILLED rather than defaulted to 0.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.UI;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1374/1375: the raid payout is visible, and a victory counts.</summary>
    public static class RaidPayoutVisibilityRegression
    {
        // Relative to Application.dataPath.
        private const string VictoryRel = "_Modules/Village/World/Camps/RaidVictoryController.cs";
        private const string VmRel      = "_Modules/Village/UI/EndState/EndStateVM.cs";

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "raid-payout-visibility: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Standalone batch entry.</summary>
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("RAID_PAYOUT_VISIBILITY_OK - " + reason);
            else Debug.LogError("RAID_PAYOUT_VISIBILITY_FAIL - " + reason);
        }

        private static bool RunCore(out string reason)
        {
            var fails = new List<string>();
            var log = new StringBuilder();

            string victory = ReadCode(VictoryRel, fails);
            string vm      = ReadCode(VmRel, fails);

            CaseA_EveryCreditedCurrencyIsShown(fails, log);
            CaseB_NoRetiredStoneRow(vm, fails, log);
            CaseC_WholeBasketReachesTheScreen(victory, fails, log);
            CaseD_VictoryCountPersistsAndIsMonotonic(fails, log);
            CaseE_ExactlyOneReportAndOnePublish(victory, fails, log);
            CaseF_SeasonPublishStaysOutcomeTyped(victory, fails, log);
            CaseG_NonVictoryExitsReportToo(fails, log);            // WO-1561
            CaseH_RaidEndStatesHoldOnTouch(fails, log);            // WO-1543

            if (fails.Count == 0)
            {
                reason = "RAID PAYOUT VISIBILITY OK -- a settled raid victory shows one spoil row per " +
                         "NON-ZERO credited currency (wood/iron/food/gold/crystals, verified by " +
                         "constructing the real EndStateVM), no row carries the retired 'Stone' name, " +
                         "the WHOLE credited basket reaches the screen instead of two of its five axes, " +
                         "the monotonic RaidVictories counter round-trips the save wire and defaults " +
                         "clean on an older payload, and the settle seam emits EXACTLY ONE combat.raid " +
                         "daily report and EXACTLY ONE outcome-typed season publish (no XP amount); and the " +
                         "NON-VICTORY exits report too - a retreat or a clock-expiry composes razed %, stars, the " +
                         "spoils actually BANKED, the wounded count and the bank-short caveat through the same " +
                         "EndState template (WO-1561), while both raid end states HOLD on interaction and keep " +
                         "their anti-softlock guard with no sibling template's timing moved (WO-1543)";
                return true;
            }

            reason = "raid-payout-visibility FAILED (" + fails.Count + "): " + string.Join(" | ", fails);
            return false;
        }

        // =====================================================================
        //  CASE G - WO-1561: THE NON-VICTORY EXITS REPORT (BEHAVIOURAL)
        // ---------------------------------------------------------------------
        //  THE DEFECT. RaidDeployController.DoRetreat settled the score, paid the
        //  partial loot, reconciled the army and called SceneRouter.GoCastle() with
        //  NO SCREEN AT ALL - and the clock-expiry exit funnels into the same
        //  method. Measured on the pre-change tree: grep -c
        //  "EndStateVM\.|EndStateView.Show" on that file returned 1, and the single
        //  hit was a COMMENT. Nothing picked it up in town either (every reader of
        //  RaidResult is raid-scene-side), so the outcome was computed, BANKED and
        //  discarded unread. A player who retreated - or simply ran out the clock -
        //  landed in town having earned real loot and possibly a star and was told
        //  none of it, while a WIN got the full treatment.
        //
        //  BEHAVIOURAL, not a grep: the defect is that the composition did not
        //  EXIST, so the honest oracle constructs the real view-model and asserts
        //  what it says. Source-lint that DoRetreat actually shows it lives in
        //  RaidExitParityRegression, beside the other exit-parity pins.
        //
        //  RED-FIRST NOTE: this case CANNOT COMPILE against the pre-change tree -
        //  EndStateVM.FromRaidRetreat does not exist there. That build failure IS
        //  the honest red; the contract is exactly what was absent.
        // =====================================================================
        private static void CaseG_NonVictoryExitsReportToo(List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case G] WO-1561 - retreat and timeout show a result");

            // A half-razed retreat that banked a partial basket and wounded two troops.
            var banked = new ResourceCost(wood: 420, stone: 300, iron: 0, crystals: 0, coins: 180);
            var vm = EndStateVM.FromRaidRetreat(EndStateVM.RetreatReason, null, 30f,
                                                stars: 1, destructionPercent: 62, elapsedSeconds: 96f,
                                                credited: banked, rewardShort: false,
                                                // WO-1810: 2 killed + 60% of the 4 survivors (2) = 4 lost.
                                                troopsDeployed: 6, troopsSurvived: 4, troopsLost: 4);
            if (vm == null) { fails.Add("[G] FromRaidRetreat returned null on a normal retreat"); return; }

            // RAZED %, STARS, TIME - the three facts a losing player earned and was never told.
            if (vm.Stars != 1)
                fails.Add("[G] a retreat settling 1 star reported Stars=" + vm.Stars +
                          ". RaidScoring grants a star at >=50% razed, and the exit that earns it must say so.");
            if (vm.TimeSeconds <= 0f)
                fails.Add("[G] the retreat screen carries no clock (TimeSeconds=" + vm.TimeSeconds + ").");
            if (vm.Subtitle == null || vm.Subtitle.IndexOf("62%", StringComparison.Ordinal) < 0)
                fails.Add("[G] a retreat that razed 62% does not say so. Subtitle was: " + (vm.Subtitle ?? "<null>"));

            // SPOILS ACTUALLY BANKED - one row per non-zero axis, exactly the victory screen's rule.
            var labels = new List<string>();
            foreach (var row in vm.Spoils) labels.Add(row != null ? row.Label : "<null row>");
            if (vm.Spoils.Count != 3)
                fails.Add("[G] a retreat banking wood/food/gold drew " + vm.Spoils.Count +
                          " spoil row(s), expected 3. Rows were: " + string.Join(",", labels.ToArray()));
            foreach (string expected in new[] { "Wood", "Gold" })
                if (!labels.Contains(expected))
                    fails.Add("[G] a retreat crediting " + expected.ToLowerInvariant() + " showed no " + expected +
                              " row. Rows were: " + string.Join(",", labels.ToArray()));

            // TROOPS LOST, in words.
            //
            // ⚠ RE-POINTED 2026-09-16 (WO-1810), and the re-point moves WITH an owner ruling, not
            // against a passing test. This pinned "2 troops return wounded" - correct until this
            // ticket, because a fallen troop came home wounded and healed free. Owner ruling:
            // "any troop killed is dead so 60% of whats left", so the honest word is LOST.
            // 6 deployed / 4 survivors on a RETREAT = 2 killed + 60% of 4 survivors (2) = 4 lost.
            if (vm.Subtitle == null || vm.Subtitle.IndexOf("4 troops lost", StringComparison.Ordinal) < 0)
                fails.Add("[G] a retreat that lost 4 troops did not say '4 troops lost' - the COUNT is pinned, " +
                          "not just the word, because an oracle that passes on any number pins nothing. " +
                          "Subtitle was: " + (vm.Subtitle ?? "<null>"));

            // A raid that lost NOBODY still says so - the reassurance an early retreat has earned.
            // troopsLost is passed EXPLICITLY as 0 here: under WO-1810 a retreat with 3 survivors
            // charges 60% of them, so "deployed 3 / survived 3" alone is no longer a zero-loss raid.
            var clean = EndStateVM.FromRaidRetreat(EndStateVM.RetreatReason, null, 30f, 0, 10, 20f,
                                                   default(ResourceCost), false, 3, 3, 0);
            if (clean == null || clean.Subtitle == null ||
                clean.Subtitle.IndexOf("Every troop came home", StringComparison.Ordinal) < 0)
                fails.Add("[G] a retreat that lost nobody does not say so - that is the reassurance an early " +
                          "retreat has earned, and 'lost: 0' is not a sentence.");
            if (clean != null && clean.Spoils.Count != 0)
                fails.Add("[G] a retreat that banked NOTHING drew " + clean.Spoils.Count +
                          " spoil row(s). A screen must never advertise a payout that did not land.");

            // LOOT LOST TO A FULL BANK IS STATED IN WORDS. WO-1461's live case: the deploy card
            // quoted ~1,800 wood and 25 arrived. The number here is the BANKED one, and the
            // shortfall is said out loud rather than silently dropped.
            var shortPay = EndStateVM.FromRaidRetreat(EndStateVM.RetreatReason, null, 30f, 1, 55, 80f,
                                                       new ResourceCost(wood: 25), true, 4, 2);
            if (shortPay == null || shortPay.Subtitle == null ||
                shortPay.Subtitle.IndexOf(EndStateVM.RewardShortSentence, StringComparison.Ordinal) < 0)
                fails.Add("[G] a retreat whose loot was clamped by a full bank does not say so. The player earned " +
                          "it and did not receive it; silence there is the 'I raided and got nothing' " +
                          "unfalsifiability (WO-978 / WO-1461).");

            // THE TIMEOUT EXIT IS THE SAME SCREEN with its own lead sentence - a player who ran
            // out of clock did not choose to leave.
            var timeout = EndStateVM.FromRaidRetreat(EndStateVM.TimeoutReason, null, 30f, 1, 62, 180f,
                                                      banked, false, 6, 4);
            if (timeout == null || timeout.Title != EndStateVM.TimeoutTitle)
                fails.Add("[G] the clock-expiry exit does not title itself '" + EndStateVM.TimeoutTitle +
                          "' - the two non-victory exits are indistinguishable to the player.");
            if (vm.Title != EndStateVM.RetreatTitle)
                fails.Add("[G] the retreat exit does not title itself '" + EndStateVM.RetreatTitle + "'.");

            // IT IS NOT A VICTORY. The emblem and the trace tag must not congratulate a fall-back.
            if (vm.Kind == EndStateKind.Victory)
                fails.Add("[G] the retreat screen reports itself as a Victory.");

            // THE GUARD SURVIVES. A player stranded after a retreat is strictly worse than one who
            // reads a screen too briefly - so this template never opts out with AutoDismissSeconds 0.
            if (vm.AutoDismissSeconds <= 0f)
                fails.Add("[G] the retreat screen has NO anti-softlock guard (AutoDismissSeconds=" +
                          vm.AutoDismissSeconds + "). WO-1561 section 3 forbids that.");

            log.AppendLine("  ok: razed %, stars, banked spoils, wounded count, bank-short caveat, both exit titles");
        }

        // =====================================================================
        //  CASE H - WO-1543: THE RAID END STATES HOLD ON TOUCH (BEHAVIOURAL)
        // ---------------------------------------------------------------------
        //  Owner ruling 2026-09-06: "Hold on touch, longer guard." The guard STAYS
        //  (an end state that never dismisses can strand a player); it is opt-in per
        //  template so no OTHER end state's timing moves silently, which is WO-1543
        //  acceptance 4 and the half a source-grep cannot answer.
        // =====================================================================
        private static void CaseH_RaidEndStatesHoldOnTouch(List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case H] WO-1543 - raid end states hold on touch; nothing else moves");

            var win = EndStateVM.FromRaidVictory(null, null, 30f, 3, 100, 42f,
                                                 new ResourceCost(wood: 500));
            if (win == null || !win.HoldOnInteraction)
                fails.Add("[H] the raid VICTORY screen does not hold on interaction. In 12 seconds the player had to " +
                          "read the star result, up to five spoils rows, a companion line and the bank-overflow " +
                          "caveat, and no tap stopped it.");
            if (win != null && win.AutoDismissSeconds <= 0f)
                fails.Add("[H] the raid victory screen's anti-softlock guard is GONE (AutoDismissSeconds=" +
                          win.AutoDismissSeconds + "). WO-1543 rule 1: never remove it, never set it to 0 on a " +
                          "raid end state - a player who walked away must still be returned home.");

            var lose = EndStateVM.FromRaidRetreat(EndStateVM.RetreatReason, null, 30f, 1, 60, 90f);
            if (lose == null || !lose.HoldOnInteraction)
                fails.Add("[H] the raid NON-VICTORY screen does not hold on interaction. WO-1543's rule applies to " +
                          "BOTH raid screens; two raid results on two different rules is the drift the ticket " +
                          "coordinated WO-1561 to avoid.");
            if (lose != null && lose.AutoDismissSeconds <= 0f)
                fails.Add("[H] the raid retreat screen has no anti-softlock guard.");

            // NO OTHER END STATE MOVED. Each of these is a different surface with its own
            // deliberate dismiss value; the hold is opt-in precisely so none of them changed.
            var others = new[]
            {
                new { Name = "FromBattleDefeat",   Vm = EndStateVM.FromBattleDefeat() },
                new { Name = "FromHeroDeath",      Vm = EndStateVM.FromHeroDeath(true) },
                new { Name = "FromGameOver",       Vm = EndStateVM.FromGameOver(true, "t", "b", null) },
                new { Name = "FromOutpostVictory", Vm = EndStateVM.FromOutpostVictory(null, true) },
            };
            foreach (var o in others)
                if (o.Vm != null && o.Vm.HoldOnInteraction)
                    fails.Add("[H] " + o.Name + " now holds on interaction. The hold is OPT-IN so no other end " +
                              "state's timing moves silently (WO-1543 acceptance 4) - EndStateView serves the " +
                              "arena, the dungeon, hero death, game over and the wave banner too.");

            // FromGameOver's deliberate opt-out is untouched: Retry must be CHOSEN.
            var over = EndStateVM.FromGameOver(true, "t", "b", null);
            if (over != null && over.AutoDismissSeconds != 0f)
                fails.Add("[H] FromGameOver's deliberate AutoDismissSeconds=0 moved to " + over.AutoDismissSeconds +
                          " - an auto-fired Retry would reload the scene without player intent.");

            log.AppendLine("  ok: both raid templates hold and keep their guard; four sibling templates unchanged");
        }

        // =====================================================================
        //  CASE A - one row per non-zero credited currency (BEHAVIOURAL)
        // ---------------------------------------------------------------------
        //  Constructs the REAL view-model rather than reading its source, because
        //  the defect was arithmetic on the arguments, not a missing string: a
        //  source grep for "Wood" would have passed on the broken build (the wave
        //  factory in the same file emits a "Wood" row) while the raid screen
        //  still showed nothing.
        //
        //  RED-FIRST NOTE: this case CANNOT COMPILE against the pre-change tree.
        //  FromRaidVictory's last two parameters were `int lootCrystals, int
        //  lootFood`, so `credited:` does not exist to pass. That build failure IS
        //  the honest red - the oracle asserts the widened contract, and the
        //  contract is what was absent.
        // =====================================================================
        private static void CaseA_EveryCreditedCurrencyIsShown(List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case A] one row per non-zero credited currency");

            // The section-1 perfect Camp I basket, so the numbers under test are the
            // numbers the programme actually specifies.
            var basket = new ResourceCost(wood: 1800, stone: 3000, iron: 1100, crystals: 25, coins: 2200);
            var full = EndStateVM.FromRaidVictory(null, null, 20f, 3, 100, 42f, basket);

            if (full == null) { fails.Add("[A] FromRaidVictory returned null on a full basket"); return; }

            var labels = new List<string>();
            foreach (var row in full.Spoils) labels.Add(row != null ? row.Label : "<null row>");
            string joined = string.Join(",", labels.ToArray());

            foreach (string expected in new[] { "Wood", "Iron", "Gold", "Crystals" })
                if (!labels.Contains(expected))
                    fails.Add("[A] a raid victory crediting " + expected.ToLowerInvariant() +
                              " showed NO " + expected + " row. Rows were: " + joined +
                              ". 'Raid -> get richer' cannot be felt through a screen that omits the payout.");

            if (full.Spoils.Count != 5)
                fails.Add("[A] a basket with all five currencies non-zero produced " + full.Spoils.Count +
                          " spoil row(s), expected 5. Rows were: " + joined);

            // The FOOD axis is asserted by its AMOUNT, not by its word, because that word is a
            // live OWNER question and not an engineering one: EndStateVM.FoodSpoilLabel records
            // the three HUD surfaces that still call this same balance "Stone"
            // (HudKitController.cs:2190, BuildWalletRow.cs:46, DailyQuestHud.cs:407). Pinning
            // the word here would freeze one side of an unsettled ruling. What must never
            // regress is that the axis APPEARS - it is the one the retired code mislabelled.
            // 3,000 is below CompactNumber's 10,000 abbreviation threshold, so the string is exact.
            var amounts = new List<string>();
            foreach (var row in full.Spoils) amounts.Add(row != null ? row.Amount : null);
            if (!amounts.Contains("+3000"))
                fails.Add("[A] a basket crediting 3,000 food produced no row showing +3000. Amounts were: " +
                          string.Join(",", amounts.ToArray()) + ".");
            foreach (var pair in new[] { "+1800", "+1100", "+2200", "+25" })
                if (!amounts.Contains(pair))
                    fails.Add("[A] the section-1 perfect Camp I basket produced no row showing " + pair +
                              ". Amounts were: " + string.Join(",", amounts.ToArray()) + ".");

            // Zero axes are SUPPRESSED - a wood-only raid must not draw four empty rows.
            var woodOnly = EndStateVM.FromRaidVictory(null, null, 20f, 1, 40, 12f,
                                                      new ResourceCost(wood: 500));
            if (woodOnly == null || woodOnly.Spoils.Count != 1)
                fails.Add("[A] a wood-only payout produced " +
                          (woodOnly == null ? "a null VM" : woodOnly.Spoils.Count + " rows") +
                          ", expected exactly 1. Zero-value currencies must not draw rows.");
            else if (woodOnly.Spoils[0].Label != "Wood")
                fails.Add("[A] a wood-only payout drew a row labelled '" + woodOnly.Spoils[0].Label + "'.");

            // An EMPTY basket draws nothing at all (a raid that credited nothing must not
            // advertise a reward it did not pay - the WO-978 honesty contract).
            var empty = EndStateVM.FromRaidVictory(null, null, 20f, 0, 0, 5f, default(ResourceCost));
            if (empty != null && empty.Spoils.Count != 0)
                fails.Add("[A] a raid that credited NOTHING drew " + empty.Spoils.Count +
                          " spoil row(s). A screen must never advertise a payout that did not land.");

            // The UNLOCK LINE is carried when supplied, and absent when not.
            var withUnlock = EndStateVM.FromRaidVictory(null, null, 20f, 3, 100, 40f, basket,
                                                        "The Broken Garrison unlocked");
            if (withUnlock == null || withUnlock.UnlockLine != "The Broken Garrison unlocked")
                fails.Add("[A] the optional unlockLine did not reach EndStateVM.UnlockLine, so the sibling " +
                          "ladder lane has no seam to render an unlock through.");
            if (full.UnlockLine != null)
                fails.Add("[A] a victory with no unlock still carried an UnlockLine ('" + full.UnlockLine + "').");
        }

        // =====================================================================
        //  CASE B - the retired "Stone" row is gone from the raid factory
        // ---------------------------------------------------------------------
        //  GameState.cs:59 records `public int Stone = 20;` being REMOVED as a
        //  balance (WO-1212, 2026-08-26). The raid victory screen went on printing
        //  the FOOD amount under that dead name.
        //
        //  Scoped to the raid factory's own body, not the whole file, so the
        //  oracle cannot be satisfied by deleting an unrelated string and cannot
        //  be tripped by one.
        // =====================================================================
        private static void CaseB_NoRetiredStoneRow(string vm, List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case B] no retired 'Stone' currency row on the raid victory");
            if (string.IsNullOrEmpty(vm)) return;

            string body = ExtractMethodBody(vm, "public static EndStateVM FromRaidVictory");
            if (body == null)
            {
                fails.Add("[B] FromRaidVictory could not be located in EndStateVM.cs - the oracle cannot " +
                          "prove the retired label is gone, and a hollow pass is not a pass.");
                return;
            }

            string code = StripComments(body);
            if (Regex.IsMatch(code, "Label\\s*=\\s*\"Stone\""))
                fails.Add("[B] EndStateVM.FromRaidVictory still emits a spoil row labelled \"Stone\". " +
                          "Stone was retired as a BALANCE (GameState.cs:59, WO-1212) and that row was " +
                          "printing the FOOD amount under a dead currency name.");
        }

        // =====================================================================
        //  CASE C - the WHOLE credited basket reaches the screen
        // ---------------------------------------------------------------------
        //  GrantLoot already measured every axis (dw/df/di/dc/dg). The defect was
        //  that only two of the five survived the trip to the VM. Asserted at
        //  source because it is a wiring shape, and because the alternative -
        //  standing up a live EconomyService and a raid scene - is not something
        //  an EditMode oracle can honestly do.
        // =====================================================================
        private static void CaseC_WholeBasketReachesTheScreen(string victory, List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case C] the whole credited basket reaches the victory screen");
            if (string.IsNullOrEmpty(victory)) return;

            string code = StripComments(victory);

            if (Regex.IsMatch(code, "_crystalsCredited|_foodCredited"))
                fails.Add("[C] RaidVictoryController still carries the two-axis credited capture " +
                          "(_crystalsCredited / _foodCredited). Wood, iron and gold are measured in " +
                          "GrantLoot and would be dropped before the screen, which is PIN A.");

            if (!Regex.IsMatch(code, "_credited\\s*=\\s*new\\s+ResourceCost"))
                fails.Add("[C] GrantLoot no longer builds a whole-basket ResourceCost from the MEASURED " +
                          "wallet deltas. The screen must show what was CREDITED, never what was requested " +
                          "(WO-978): at a capped town bank those differ and the screen is what the player " +
                          "believes.");

            if (!Regex.IsMatch(code, "FromRaidVictory\\([^;]*_credited"))
                fails.Add("[C] the victory screen is no longer handed the credited basket - " +
                          "FromRaidVictory does not receive _credited.");
        }

        // =====================================================================
        //  CASE D - the victory count persists, defaults clean, and is monotonic
        // ---------------------------------------------------------------------
        //  RED-FIRST NOTE: this case CANNOT COMPILE against the pre-change tree.
        //  Neither GameState.RaidVictories nor SaveSchema.PersistedState.
        //  RaidVictories existed, so `state.RaidVictories` is not a member. That
        //  build failure IS the honest red: the field the ladder needs was absent.
        // =====================================================================
        private static void CaseD_VictoryCountPersistsAndIsMonotonic(List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case D] the raid victory count round-trips the save wire");

            // (a) it survives serialize -> deserialize -> validate.
            var outState = new SaveSchema.PersistedState { RaidVictories = 7, RaidVictoriesBackfilled = true };
            string json = JsonConvert.SerializeObject(outState, SaveSchema.JsonSettings);
            var back = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(json, SaveSchema.JsonSettings);
            if (back == null) { fails.Add("[D] the raid-victory payload deserialized to null"); return; }

            var vr = SaveSchema.Validate(back);
            if (!vr.Ok)
                fails.Add("[D] a save carrying raidVictories FAILED validation: field '" + vr.FieldPath +
                          "' (" + vr.Reason + ")");
            if (!back.RaidVictories.HasValue || (int)back.RaidVictories.Value != 7)
                fails.Add("[D] raidVictories did not survive the save round-trip (wrote 7, read back " +
                          (back.RaidVictories.HasValue ? back.RaidVictories.Value.ToString() : "null") + ")");
            if (!back.RaidVictoriesBackfilled.HasValue || !back.RaidVictoriesBackfilled.Value)
                fails.Add("[D] the one-shot backfill latch did not survive the save round-trip, so the " +
                          "claim-flag seed could run twice and INFLATE a veteran's count.");

            // (b) an older payload has no key at all and must load clean, so the Village-side
            //     backfill can still tell an unanswered save from an answered one.
            var old = JsonConvert.DeserializeObject<SaveSchema.PersistedState>("{ }", SaveSchema.JsonSettings);
            if (old == null)
                fails.Add("[D] an old key-less payload deserialized to null - the counter broke old-save loading");
            else if (old.RaidVictories.HasValue)
                fails.Add("[D] an old key-less payload read back raidVictories = " + old.RaidVictories.Value +
                          " instead of null. Default-on-read is broken and the backfill can no longer tell " +
                          "an unanswered save from an answered one.");

            // (c) a corrupt NEGATIVE is clamped, never allowed to run the ladder backwards.
            var neg = new SaveSchema.PersistedState { RaidVictories = -4 };
            var negResult = SaveSchema.Validate(neg);
            if (!negResult.Ok)
                fails.Add("[D] a negative raidVictories was REJECTED outright ('" + negResult.FieldPath +
                          "') rather than clamped. A hand-edited save must load, floored to 0, not fail.");
            else if (!neg.RaidVictories.HasValue || neg.RaidVictories.Value < 0)
                fails.Add("[D] a negative raidVictories survived Validate as " +
                          (neg.RaidVictories.HasValue ? neg.RaidVictories.Value.ToString() : "null") +
                          " - the counter is supposed to be monotonic and non-negative.");

            // (d) the live SO carries the field with a zero default (a NEW save starts at 0).
            var so = ScriptableObject.CreateInstance<GameState>();
            try
            {
                if (so.RaidVictories != 0)
                    fails.Add("[D] a fresh GameState starts at RaidVictories = " + so.RaidVictories +
                              " instead of 0.");
                if (so.RaidVictoriesBackfilled)
                    fails.Add("[D] a fresh GameState starts with the backfill latch already SET, so an " +
                              "existing save loading this default would never be seeded from its claim flags.");
            }
            finally { UnityEngine.Object.DestroyImmediate(so); }
        }

        // =====================================================================
        //  CASE E - exactly one daily report and one season publish, both after
        //           the de-duplicating latch
        // =====================================================================
        private static void CaseE_ExactlyOneReportAndOnePublish(string victory, List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case E] exactly one combat.raid report and one season publish");
            if (string.IsNullOrEmpty(victory)) return;

            string code = StripComments(victory);

            int reports = Regex.Matches(code, "Report\\(\\s*QuestRaidEventId").Count
                        + Regex.Matches(code, "Report\\(\\s*\"combat\\.raid").Count;
            if (reports == 0)
                fails.Add("[E] RaidVictoryController reports NO combat.raid daily. Clearing a baked raid " +
                          "camp therefore does not advance 'Break a camp - clear 1 enemy outpost' " +
                          "(daily-quests.json:291), whose label describes exactly what the player just did. " +
                          "The only ticker in the tree was EnemyOutpost.cs:703 - the OuterWorld outpost.");
            else if (reports > 1)
                fails.Add("[E] RaidVictoryController reports combat.raid " + reports + " times. Exactly ONE " +
                          "report per settle: Report() prefix-matches, so one call already advances both " +
                          "combat.raid.single and combat.raid.double, and a second call double-counts a clear.");

            int publishes = Regex.Matches(code, "ArenaOutcomeRelay\\.Publish\\(").Count;
            if (publishes == 0)
                fails.Add("[E] RaidVictoryController never publishes the raid outcome to the season pass. " +
                          "PROGRAM_RAID_ECONOMY section 6 routes raids into the existing 30-tier pass and " +
                          "ArenaOutcomeRelay already carries the raid overload; the settle seam is the door.");
            else if (publishes > 1)
                fails.Add("[E] RaidVictoryController publishes the raid outcome " + publishes + " times. The " +
                          "relay holds a SINGLE handler precisely because two credits from one raid is a " +
                          "duplicated-state bug, not a feature.");

            // Both must sit after the `_handled` latch - that is what makes them once-per-settle.
            int latch = code.IndexOf("_handled = true", StringComparison.Ordinal);
            int reportAt = code.IndexOf("Report(QuestRaidEventId", StringComparison.Ordinal);
            int publishAt = code.IndexOf("ArenaOutcomeRelay.Publish(", StringComparison.Ordinal);
            if (latch < 0)
                fails.Add("[E] HandleVictory's `_handled = true` latch is gone. It is the ONE de-duplicated " +
                          "settle seam; without it a double OnCleared double-counts the win, the daily and " +
                          "the season publish.");
            else
            {
                if (reportAt >= 0 && reportAt < latch)
                    fails.Add("[E] the combat.raid report is emitted BEFORE the `_handled` latch, so a " +
                              "duplicate victory signal ticks the daily twice.");
                if (publishAt >= 0 && publishAt < latch)
                    fails.Add("[E] the season publish is emitted BEFORE the `_handled` latch, so a duplicate " +
                              "victory signal credits the pass twice.");
            }

            // And the count itself is written exactly once, on the same seam.
            int counted = Regex.Matches(code, "RaidVictories\\+\\+").Count;
            if (counted != 1)
                fails.Add("[E] RaidVictories is incremented " + counted + " time(s) in RaidVictoryController, " +
                          "expected exactly 1. The section-4 ladder reads this counter; a second writer is " +
                          "the ladder skipping a tier.");
        }

        // =====================================================================
        //  CASE F - the season publish stays OUTCOME-TYPED
        // ---------------------------------------------------------------------
        //  Owner ruling Q4 ("NEVER SELL TIERS") is enforced by there being no
        //  amount parameter anywhere on the path. The +50/+25/+25/+100 table
        //  resolves INSIDE BattlePassService where BattleMonthlyRegression's
        //  [xp-one-door] case can see it. A caller that could name an amount
        //  re-opens that door from the outside.
        // =====================================================================
        private static void CaseF_SeasonPublishStaysOutcomeTyped(string victory, List<string> fails, StringBuilder log)
        {
            log.AppendLine("[case F] the season publish carries an outcome, never an XP amount");
            if (string.IsNullOrEmpty(victory)) return;

            string code = StripComments(victory);
            var m = Regex.Match(code, "ArenaOutcomeRelay\\.Publish\\(([^;]*?)\\)\\s*\\)?\\s*;", RegexOptions.Singleline);
            if (!m.Success) return;   // absence is CASE E's failure, not a second report of the same thing.

            string args = m.Groups[1].Value;
            if (Regex.IsMatch(args, "\\bxp\\b|Xp|XP|SeasonXp|AddXp|GrantXp"))
                fails.Add("[F] the raid season publish names an XP amount in its arguments. The seam is " +
                          "OUTCOME-typed by ruling Q4: (win, stars, destruction, firstClear, raidId). The " +
                          "XP table resolves inside BattlePassService, behind the one door.");

            // firstClear must come from the repeatClear read taken BEFORE ClaimBase. Re-deriving it
            // from IsClaimed after the claim reports EVERY clear as a repeat, because MarkClaimed
            // has already flipped the flag - the exact trap RaidClaimService.MarkClaimed documents.
            if (!Regex.IsMatch(args, "!\\s*repeatClear"))
                fails.Add("[F] the publish does not pass `!repeatClear` as firstClear. repeatClear is read " +
                          "BEFORE ClaimBase on purpose; anything re-derived after the claim reports every " +
                          "clear as a repeat and the first-clear season bonus never pays.");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>Reads a source file relative to Application.dataPath. A missing file is a
        /// FAILURE, never a silent skip - this suite must not go green on an absent dependency.</summary>
        private static string ReadCode(string relPath, List<string> fails)
        {
            string full = Path.Combine(Application.dataPath, relPath);
            if (!File.Exists(full))
            {
                fails.Add("source file not found: " + relPath + " (the oracle cannot prove anything about " +
                          "a file it cannot read, and a hollow pass is not a pass)");
                return null;
            }
            return File.ReadAllText(full);
        }

        /// <summary>Strips // and /* */ comments so an assertion can never be satisfied - or
        /// tripped - by prose. The doc-comments in these files quote the very defects under test.</summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            src = Regex.Replace(src, "/\\*.*?\\*/", " ", RegexOptions.Singleline);
            src = Regex.Replace(src, "//[^\\n]*", " ");
            return src;
        }

        /// <summary>
        /// Returns the brace-balanced body of the method whose signature starts with
        /// <paramref name="signature"/>, or null when it cannot be located. Counts braces from
        /// the first one after the signature, so a nested local function or object initialiser
        /// inside the method is included rather than truncating the body early.
        /// </summary>
        private static string ExtractMethodBody(string src, string signature)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int at = src.IndexOf(signature, StringComparison.Ordinal);
            if (at < 0) return null;

            // The braces are spelled NUMERICALLY (123 / 125) rather than as char literals, so
            // this file's own brace count stays balanced under the CLAUDE.md rule-1 counter -
            // the same precedent RaidRepeatClearRegression.cs:76 sets with its balanced pair.
            int i = src.IndexOf((char)123, at);
            if (i < 0) return null;
            int depth = 0;
            for (int j = i; j < src.Length; j++)
            {
                if (src[j] == (char)123) depth++;
                else if (src[j] == (char)125) { depth--; if (depth == 0) return src.Substring(i, j - i + 1); }
            }
            return null;
        }
    }
}
