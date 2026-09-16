// =============================================================================
// CollectorOverflowRegression [collector-overflow]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers: COLLECTOR_OVERFLOW_OK / COLLECTOR_OVERFLOW_FAIL.
//
// WHAT THIS PINS: OWNER RULING 26b, SECOND HALF - the AUTOMATIC OVERFLOW from a full
// collector into its matching storage.
//
//   Owner, verbatim: "the collectors had a cap as the collectors hit their cap. They
//   couldn't produce anymore unless they had a storage to put it in - the overflow by
//   default would automatically go to their matching storage."
//
// The cap and the stall were already real before this change (ResourceCollector.Accrue
// clamps at Math.Min(cap, ...)). What did NOT exist was the spill: the ONLY deposit path
// in the game was the manual Collect() tap, so a full collector discarded production
// until the player tapped. The point of the spill is UPTIME - a storage upgrade stops
// being "hold more" in the abstract and becomes "your collectors keep working".
//
// ! THE FACT THAT SHAPES EVERY CASE HERE, measured 2026-09-06 (OWNER_RULINGS_LOCKED.md,
//   "Ruling 26 - MEASURED"): A FULL COLLECTOR IS BIGGER THAN AN EARLY BANK. The Quarry
//   holds 7,500 and a level-1 bank holds 3,000 - not "is tight", CANNOT FIT. So PARTIAL
//   overflow is the NORMAL case, not an edge case, and every case below is written
//   against that shape.
//
// ! THE CASE THAT MATTERS MOST is [overflow-never-burns]. The manual tap has been
//   never-burn since WO-1392 (the owner's 414 wood, 2026-09-04); an AUTOMATIC path that
//   burned would be strictly worse, because nobody would be watching when it happened.
//
// OUT OF SCOPE, deliberately, and the owner rules on all of it: collector capacities,
// production rates, harvest intervals, storage costs, storage capacities, and whether the
// manual tap should pay a BONUS (an OPEN question recorded in ruling 26). This suite
// asserts MECHANISM ONLY and lints that no bonus was smuggled in.
//
// -----------------------------------------------------------------------------
// REVERT RECIPES (per case) - what to delete to put the tree back the way it was.
//
//   [overflow-never-burns]  Delete ResourceCollector.SettleOverflow and
//                           SimulateAccrueWithOverflow, and this case.
//   [partial-overflow]      Same two helpers; delete this case.
//   [stall-clears-when-room-appears]
//                           Same two helpers; delete this case.
//   [tap-still-works]       Delete this case only - it asserts the PRE-EXISTING tap and
//                           passes on the tree before ruling 26b as well.
//   [spill-asks-only-what-fits]
//                           Delete ResourceCollector.TryOverflowToBank +
//                           SettleOverflowPool + the auto-overflow while-loop in Accrue
//                           (restore `_pending = System.Math.Min(cap, _pending + amount *
//                           (double)health);`), and delete this case.
//   WHOLE SUITE             Delete this file and its DataRegression.cs registration line.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village.Buildings.Progression;

namespace DeNelle.Editor.Regression
{
    /// <summary>Ruling 26b: a full collector spills into its matching storage, and nothing burns.</summary>
    public static class CollectorOverflowRegression
    {
        private const string CollectorSrc =
            "Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs";

        // The MEASURED numbers from OWNER_RULINGS_LOCKED.md "Ruling 26 - MEASURED" (2026-09-06).
        // Used as FIXTURES only - this suite never asserts they are the right numbers (balance is
        // the owner's), only that the mechanism behaves correctly at the shape they create.
        private const double QuarryCapFixture = 7500.0;
        private const int L1BankFixture = 3000;

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("COLLECTOR_OVERFLOW_OK - " + reason);
            else Debug.LogError("COLLECTOR_OVERFLOW_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            Case(failures, "overflow-never-burns", () => CaseNeverBurns(failures, notes));
            Case(failures, "partial-overflow", () => CasePartialOverflow(failures, notes));
            Case(failures, "stall-clears-when-room-appears", () => CaseStallClears(failures, notes));
            Case(failures, "tap-still-works", () => CaseTapStillWorks(failures, notes));
            Case(failures, "spill-asks-only-what-fits", () => CaseSpillAsksOnlyWhatFits(failures, notes));

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count > 0)
            {
                reason = "collector-overflow FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
                return false;
            }

            reason = "COLLECTOR OVERFLOW OK (owner ruling 26b) - a full collector spills into its " +
                     "matching storage automatically, PARTIAL overflow is handled as the normal case " +
                     "(a 7,500 collector into a 3,000 bank banks 3,000 and keeps the rest PENDING), " +
                     "nothing already held can ever be burned (pendingAfter + banked is never less " +
                     "than pendingBefore, in any of the cases including a completely full bank), a " +
                     "stalled collector resumes by itself the moment storage frees up, a sub-unit " +
                     "remainder never dumps the pool, the spill asks for exactly min(floor(pending), " +
                     "headroom) so it can neither over-drain nor raise a false BANK-FULL loss report, " +
                     "and the MANUAL TAP is unchanged with NO bonus (that is an open owner question)"
                     + noteStr;
            return true;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  THE CASE THAT MATTERS MOST - nothing already held is ever lost.
        // =====================================================================
        //
        //  The books must close in every configuration:
        //     pendingAfter + banked + unproduced == pendingBefore + owed
        //  and the ruling-26b / WO-1392 line must hold in every configuration:
        //     pendingAfter + banked >= pendingBefore
        //  `unproduced` is FUTURE production a stalled collector could not hold. That is the
        //  stall, it is the design, and it is never something the player already had.

        private static void CaseNeverBurns(List<string> failures, List<string> notes)
        {
            // (a) THE headline case: a FULL collector overflowing into a FULL bank loses NOTHING.
            double after = ResourceCollector.SimulateAccrueWithOverflow(
                QuarryCapFixture, QuarryCapFixture, owed: 1000.0, bankRoom: 0,
                out int banked, out double unproduced);
            if (banked != 0 || Math.Abs(after - QuarryCapFixture) > 1e-9)
                failures.Add($"[overflow-never-burns] a full Quarry ({QuarryCapFixture:0}) spilling into a FULL bank " +
                             $"ended at pending {after:0.###} / banked {banked}; expected the pool UNTOUCHED at " +
                             $"{QuarryCapFixture:0} and 0 banked - the collector stalls, it does not empty itself into nowhere");
            if (Math.Abs(unproduced - 1000.0) > 1e-9)
                failures.Add($"[overflow-never-burns] the stalled tick reported {unproduced:0.###} unproduced; expected the " +
                             "whole 1000 (the STALL is the design; what must never be lost is what is already HELD)");

            // (b) The two identities, swept across the shapes that actually occur in play:
            //     bank bigger than the collector (L6), bank smaller (L1), bank exactly full,
            //     bank empty, an empty collector, a zero-capacity collector.
            var pendings   = new double[] { 0.0, 1.0, 1234.5, 3456.0, QuarryCapFixture };
            var caps       = new double[] { 0.0, 3456.0, QuarryCapFixture };
            var owedSet    = new double[] { 0.0, 13.0, 1000.0, 60000.0 };
            var rooms      = new int[] { 0, 1, L1BankFixture, 34000 };
            int swept = 0;
            foreach (double p0 in pendings)
                foreach (double cap in caps)
                    foreach (double owed in owedSet)
                        foreach (int room in rooms)
                        {
                            // A pool ALREADY above its own capacity is skipped, and the skip is not
                            // a convenience. `_pending = Math.Min(cap, ...)` truncates an over-cap
                            // pool, which is a PRE-EXISTING behaviour of the capacity clamp (it long
                            // predates ruling 26b and this lane is not allowed to change it - a
                            // capacity is a balance number and the owner rules on balance). It is
                            // reachable only if a collector's capacity SHRINKS under a held pool
                            // (a talent/echo change), and it is flagged in this lane's hand-back
                            // rather than silently pinned green or silently "fixed" here.
                            if (p0 > cap) continue;

                            double pool = ResourceCollector.SimulateAccrueWithOverflow(
                                p0, cap, owed, room, out int got, out double lost);
                            swept++;

                            double heldBefore = p0;
                            if (pool + got + lost - (p0 + owed) > 1e-6 || (p0 + owed) - (pool + got + lost) > 1e-6)
                                failures.Add($"[overflow-never-burns] BOOKS DO NOT CLOSE at pending={p0} cap={cap} " +
                                             $"owed={owed} room={room}: pending {pool:0.###} + banked {got} + unproduced " +
                                             $"{lost:0.###} != {p0 + owed:0.###}. Some quantity was invented or destroyed.");
                            if (pool + got + 1e-6 < heldBefore)
                                failures.Add($"[overflow-never-burns] ⛔ RESOURCES BURNED at pending={p0} cap={cap} " +
                                             $"owed={owed} room={room}: the collector held {heldBefore:0.###} and " +
                                             $"afterwards only {pool:0.###} is pending with {got} banked. Everything the " +
                                             "player already had must end up either still pending or in the bank - this is " +
                                             "the WO-1392 line and the whole reason ruling 26b's spill is gated on headroom.");
                        }
            notes.Add($"never-burns swept {swept} pending/capacity/owed/headroom combinations");

            // (c) The pure spill helper alone, at the boundaries.
            double left = ResourceCollector.SettleOverflow(QuarryCapFixture, 0, out int moved, out int leftWhole);
            if (moved != 0 || Math.Abs(left - QuarryCapFixture) > 1e-9 || leftWhole != (int)QuarryCapFixture)
                failures.Add($"[overflow-never-burns] SettleOverflow({QuarryCapFixture:0}, room 0) moved {moved} and left " +
                             $"{left:0.###}; expected 0 moved and the pool intact");
            left = ResourceCollector.SettleOverflow(-50.0, 999, out moved, out leftWhole);
            if (moved != 0 || Math.Abs(left) > 1e-9)
                failures.Add($"[overflow-never-burns] a negative pending minted {moved} units into the bank; expected 0");
            left = ResourceCollector.SettleOverflow(120.0, -7, out moved, out leftWhole);
            if (moved != 0 || Math.Abs(left - 120.0) > 1e-9)
                failures.Add($"[overflow-never-burns] a negative headroom moved {moved} units; expected 0 and the pool intact");
        }

        // =====================================================================
        //  PARTIAL OVERFLOW IS THE NORMAL CASE - 7,500 into 3,000.
        // =====================================================================

        private static void CasePartialOverflow(List<string> failures, List<string> notes)
        {
            // (a) The measured shape: a full Quarry against a level-1 bank. What fits banks, the
            //     rest STAYS PENDING, and the collector is no longer at cap so production resumes.
            double after = ResourceCollector.SimulateAccrueWithOverflow(
                QuarryCapFixture, QuarryCapFixture, owed: 100.0, bankRoom: L1BankFixture,
                out int banked, out double unproduced);
            if (banked != L1BankFixture)
                failures.Add($"[partial-overflow] a full Quarry into a level-1 bank banked {banked}; expected exactly " +
                             $"{L1BankFixture} - all of the room and not one unit more");
            double expected = QuarryCapFixture - L1BankFixture + 100.0;   // 4,500 kept + the 100 owed
            if (Math.Abs(after - expected) > 1e-6)
                failures.Add($"[partial-overflow] pending settled at {after:0.###}; expected {expected:0.###} " +
                             $"({QuarryCapFixture:0} held - {L1BankFixture} banked + 100 then produced). A full collector is " +
                             "BIGGER than an early bank (measured 2026-09-06), so partial is the ORDINARY path");
            if (Math.Abs(unproduced) > 1e-9)
                failures.Add($"[partial-overflow] {unproduced:0.###} went unproduced although the spill freed " +
                             $"{L1BankFixture} of room - the collector must resume the moment it has space");

            // (b) The pure helper says the same thing on its own.
            double pool = ResourceCollector.SettleOverflow(QuarryCapFixture, L1BankFixture, out int moved, out int left);
            if (moved != L1BankFixture || left != (int)(QuarryCapFixture - L1BankFixture) ||
                Math.Abs(pool - (QuarryCapFixture - L1BankFixture)) > 1e-9)
                failures.Add($"[partial-overflow] SettleOverflow({QuarryCapFixture:0}, {L1BankFixture}) -> moved {moved}, " +
                             $"left {left}, pool {pool:0.###}; expected {L1BankFixture} / " +
                             $"{(int)(QuarryCapFixture - L1BankFixture)} / {QuarryCapFixture - L1BankFixture:0}");

            // (c) A SUB-UNIT remainder must NOT dump the pool. The bank moves whole units, so a
            //     float-noise leftover that no bank could ever accept must end the loop instead of
            //     spilling a full collector for the sake of 0.4 of a log. This is what
            //     ResourceCollector.OverflowEpsilon = 0.5 buys, and this line is why it is 0.5 and
            //     not float epsilon.
            after = ResourceCollector.SimulateAccrueWithOverflow(
                99.6, 100.0, owed: 0.8, bankRoom: 34000, out banked, out unproduced);
            if (banked != 0)
                failures.Add($"[partial-overflow] a 0.4-unit remainder dumped {banked} units of the pool into storage; " +
                             "a sub-unit leftover must simply end the tick (OverflowEpsilon)");
            if (Math.Abs(after - 100.0) > 1e-6)
                failures.Add($"[partial-overflow] the fractional case settled at {after:0.###}; expected the pool at its " +
                             "cap of 100");

            // (d) The whole pool fitting is still handled: a small collector into a big bank empties.
            after = ResourceCollector.SimulateAccrueWithOverflow(
                3456.0, 3456.0, owed: 50.0, bankRoom: 34000, out banked, out unproduced);
            if (banked != 3456 || Math.Abs(after - 50.0) > 1e-6)
                failures.Add($"[partial-overflow] a full Iron Mine (3456) into a level-6 bank banked {banked} and left " +
                             $"{after:0.###} pending; expected 3456 banked and 50 (the fresh tick) pending");
            notes.Add("partial overflow verified at the measured Quarry 7500 / L1 bank 3000 shape");
        }

        // =====================================================================
        //  THE STALL CLEARS BY ITSELF once storage has room.
        // =====================================================================

        private static void CaseStallClears(List<string> failures, List<string> notes)
        {
            // Tick 1: full collector, no storage room -> STALLED, nothing moves, nothing lost.
            double stalled = ResourceCollector.SimulateAccrueWithOverflow(
                QuarryCapFixture, QuarryCapFixture, owed: 500.0, bankRoom: 0,
                out int banked1, out double unproduced1);
            if (banked1 != 0 || Math.Abs(stalled - QuarryCapFixture) > 1e-9 || Math.Abs(unproduced1 - 500.0) > 1e-9)
                failures.Add($"[stall-clears-when-room-appears] tick 1 (no storage room) banked {banked1} and left " +
                             $"{stalled:0.###} pending with {unproduced1:0.###} unproduced; expected 0 / " +
                             $"{QuarryCapFixture:0} / 500 - a collector with nowhere to put it simply stops");

            // Tick 2: the player builds/upgrades a container (or spends). SAME collector state, and
            // now 2,000 units of room. Production must resume with no tap, no reset, no repair.
            double resumed = ResourceCollector.SimulateAccrueWithOverflow(
                stalled, QuarryCapFixture, owed: 500.0, bankRoom: 2000,
                out int banked2, out double unproduced2);
            if (banked2 != 2000)
                failures.Add($"[stall-clears-when-room-appears] tick 2 banked {banked2} of the 2000 units of new room; " +
                             "the freed space must be used automatically - that is the whole UPTIME argument for a " +
                             "storage upgrade (ruling 26b)");
            if (Math.Abs(unproduced2) > 1e-9)
                failures.Add($"[stall-clears-when-room-appears] tick 2 still discarded {unproduced2:0.###} although " +
                             "storage had room - the collector did not actually resume");
            double expected = QuarryCapFixture - 2000.0 + 500.0;
            if (Math.Abs(resumed - expected) > 1e-6)
                failures.Add($"[stall-clears-when-room-appears] tick 2 settled at {resumed:0.###}; expected {expected:0.###}");
            if (resumed + banked2 + 1e-6 < stalled)
                failures.Add("[stall-clears-when-room-appears] the resume tick LOST part of the stalled pool");

            // A big away window drains the bank across SEVERAL passes rather than one: this is the
            // offline case (Start -> CatchUpAway -> Accrue with hours of owed production).
            double away = ResourceCollector.SimulateAccrueWithOverflow(
                0.0, QuarryCapFixture, owed: 60000.0, bankRoom: 34000,
                out int bankedAway, out double unproducedAway);
            if (bankedAway != 34000)
                failures.Add($"[stall-clears-when-room-appears] a long away window banked {bankedAway} of a 34000-unit " +
                             "bank; the multi-pass spill must fill the bank before the collector re-caps, or an away " +
                             "window can never earn more than one collector-full");
            if (Math.Abs(away - QuarryCapFixture) > 1e-6)
                failures.Add($"[stall-clears-when-room-appears] the away window left {away:0.###} pending; expected the " +
                             $"collector re-capped at {QuarryCapFixture:0}");
            if (Math.Abs((away + bankedAway + unproducedAway) - 60000.0) > 1e-6)
                failures.Add("[stall-clears-when-room-appears] the away window's books do not close");
            notes.Add($"away window 60000 owed -> banked {bankedAway}, pending {away:0}, unproduced {unproducedAway:0}");
        }

        // =====================================================================
        //  THE MANUAL TAP IS UNCHANGED - and pays NO bonus.
        // =====================================================================
        //
        //  Whether tapping should pay a BONUS is an OPEN owner question recorded in ruling 26
        //  ("overflow flows automatically so nothing is ever lost, but tapping pays a bonus" -
        //  proposed, NOT ruled on). This case fails if anyone implements one without a ruling,
        //  and it fails if the tap is removed.

        private static void CaseTapStillWorks(List<string> failures, List<string> notes)
        {
            // The WO-1392 never-burn arithmetic of the TAP, re-asserted here so this suite fails
            // loudly if the spill work ever regresses the tap it sits beside.
            double after = ResourceCollector.SettleCollect(672.9, 258, out int left);
            if (Math.Abs(after - 414.9) > 1e-6 || left != 414)
                failures.Add($"[tap-still-works] SettleCollect(672.9, banked 258) -> {after:0.###}/{left}; expected " +
                             "414.9/414 - the tap drains by what BANKED, never by what was asked");
            after = ResourceCollector.SettleCollect(4000.0, 0, out left);
            if (Math.Abs(after - 4000.0) > 1e-9 || left != 4000)
                failures.Add($"[tap-still-works] a bank-full tap drained the pool to {after:0.###}; expected 4000 waiting");

            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            int collectAt = code.IndexOf("public int Collect(out int requested", StringComparison.Ordinal);
            if (collectAt < 0)
            {
                failures.Add("[tap-still-works] ⛔ the manual Collect(out,out) tap is GONE. Ruling 26b adds an automatic " +
                             "overflow; it does NOT remove the tap (the tap is also CoC's retention hook - the reason " +
                             "to open the app). Restore it or get an owner ruling.");
                return;
            }
            string body = Slice(code, collectAt, "public static double SettleCollect");

            if (!Regex.IsMatch(body, @"GrantSpendable\(wood:\s*amount\)\s*\.Wood") ||
                !Regex.IsMatch(body, @"GrantSpendable\(iron:\s*amount\)\s*\.Iron") ||
                !Regex.IsMatch(body, @"GrantSpendable\(stone:\s*amount\)\s*\.Stone"))
                failures.Add("[tap-still-works] Collect no longer reads the APPLIED basket back from GrantSpendable for " +
                             "wood/iron/food - it is trusting its own request local again, which is how a silent loss hides");
            if (body.IndexOf("SettleCollect(_pending, banked", StringComparison.Ordinal) < 0)
                failures.Add("[tap-still-works] Collect does not settle its pool through SettleCollect(_pending, banked, ...)");
            if (Regex.IsMatch(body, @"_pending\s*-=\s*amount"))
                failures.Add("[tap-still-works] Collect drains by the REQUEST again (`_pending -= amount`) - the WO-1392 " +
                             "defect is back");
            if (body.IndexOf("ResourceGainPopup.Spawn", StringComparison.Ordinal) < 0)
                failures.Add("[tap-still-works] the tap lost its ResourceGainPopup feedback (WO-890) - the most repeated " +
                             "action in the town loop would pay the player silently again");

            // NO TAP BONUS. Any multiply/scale of the tapped amount is the un-ruled bonus.
            if (Regex.IsMatch(body, @"amount\s*=\s*\(int\)[^;]*amount\s*\*") ||
                Regex.IsMatch(body, @"\bTapBonus\b|\bCollectBonus\b|\btapMultiplier\b"))
                failures.Add("[tap-still-works] ⛔ Collect appears to scale the tapped amount - a TAP BONUS is an OPEN " +
                             "owner question in ruling 26 and must NOT be implemented until she rules. Remove it.");
        }

        // =====================================================================
        //  THE SPILL ASKS FOR EXACTLY WHAT FITS - the no-burn proof, at source.
        // =====================================================================

        private static void CaseSpillAsksOnlyWhatFits(List<string> failures, List<string> notes)
        {
            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            int spillAt = code.IndexOf("private int TryOverflowToBank(", StringComparison.Ordinal);
            if (spillAt < 0)
            {
                failures.Add("[spill-asks-only-what-fits] ResourceCollector.TryOverflowToBank is GONE - the automatic " +
                             "overflow (owner ruling 26b, second half) is not implemented. A full collector would go back " +
                             "to discarding production until the player taps.");
                return;
            }
            string body = Slice(code, spillAt, "private double SettleOverflowPool");

            // (a) The one expression the whole no-burn property rests on.
            if (body.IndexOf("HeadroomFor", StringComparison.Ordinal) < 0)
                failures.Add("[spill-asks-only-what-fits] the spill does not read ResourceCollectorService.HeadroomFor - " +
                             "it is not sizing the request to the storage that must accept it");
            if (!Regex.IsMatch(body, @"int\s+ask\s*=\s*want\s*<\s*room\s*\?\s*want\s*:\s*room"))
                failures.Add("[spill-asks-only-what-fits] the spill no longer asks for exactly min(floor(pending), " +
                             "headroom). That single line is BOTH proofs: it is why nothing can be over-drained, and it " +
                             "is why TownBankCapacity.ClampGrant never fires its unthrottled 'BANK FULL ... LOST N' warn " +
                             "and Overflowed event (rendered by BankOverflowToastPresenter.cs:107) for a loss that did " +
                             "not happen - once per tick, forever.");
            if (!Regex.IsMatch(body, @"GrantSpendable\(\s*(wood|iron|food|crystals):\s*ask\s*\)"))
                failures.Add("[spill-asks-only-what-fits] the spill does not grant through EconomyService.GrantSpendable - " +
                             "it must reuse the tap's route to the wallet, not open a second one");
            if (body.IndexOf("SettleOverflowPool(banked", StringComparison.Ordinal) < 0)
                failures.Add("[spill-asks-only-what-fits] the spill does not drain the pool by what BANKED through the " +
                             "shared WO-1392 settle");
            if (Regex.IsMatch(body, @"_pending\s*-=") || Regex.IsMatch(body, @"_pending\s*=\s*0"))
                failures.Add("[spill-asks-only-what-fits] the spill mutates _pending directly - the pool may only move " +
                             "through SettleCollect, or the automatic path can burn what the tap cannot");

            // (b) The GameState guard. EconomyService.Grant clamps wood/iron against the LIVE
            //     GameState when there is one and against its own UNSAVED fallback pool when there
            //     is not (EconomyService.cs:424-460), while HeadroomFor resolves through
            //     TownBankCapacity.CurrentOf, which is GameState-only. Without a live state the two
            //     read different wallets: the trace would lie and the grant would land in the pool
            //     ReportFallbackPoolMutation correctly Fails on. This path runs unattended from
            //     Start() (the away catch-up), so it must simply not spill.
            if (!Regex.IsMatch(body, @"gs\s*\.\s*State\s*==\s*null") &&
                !Regex.IsMatch(body, @"GameStateService\s*\.\s*Instance"))
                failures.Add("[spill-asks-only-what-fits] the spill does not refuse to run without a live GameState. " +
                             "It runs unattended from Start(); with no save service the headroom read and the grant's " +
                             "own clamp measure DIFFERENT wallets and the income lands in the unsaved fallback pool.");

            // (c) The seam is actually wired into Accrue, and Accrue still clamps to capacity.
            int accrueAt = code.IndexOf("public void Accrue(", StringComparison.Ordinal);
            if (accrueAt < 0)
            {
                failures.Add("[spill-asks-only-what-fits] ResourceCollector.Accrue not found - re-point this oracle");
                return;
            }
            string accrue = Slice(code, accrueAt, "public int Collect(");
            if (accrue.IndexOf("TryOverflowToBank(", StringComparison.Ordinal) < 0)
                failures.Add("[spill-asks-only-what-fits] Accrue never calls TryOverflowToBank - the spill exists but is " +
                             "DEAD CODE, and a full collector still discards production. Accrue is the seam because it is " +
                             "the ONE place production arrives: the online tick " +
                             "(ResourceBuildingHarvester.cs:233 calls it unconditionally, full or not) and the offline " +
                             "catch-up (CatchUpAway) both pass through it.");
            if (!Regex.IsMatch(accrue, @"Math\s*\.\s*Min\s*\(\s*cap"))
                failures.Add("[spill-asks-only-what-fits] Accrue no longer clamps pending to the capacity - the cap IS the " +
                             "collector's bound and the spill is built on top of it, not instead of it");

            notes.Add("spill wired at ResourceCollector.Accrue; sized by min(floor(pending), HeadroomFor)");
        }

        // =====================================================================

        /// <summary>From <paramref name="from"/> up to the next occurrence of
        /// <paramref name="until"/> (or end of file), so a lint cannot leak into a neighbour.</summary>
        private static string Slice(string code, int from, string until)
        {
            int end = code.IndexOf(until, from + 1, StringComparison.Ordinal);
            return end > from ? code.Substring(from, end - from) : code.Substring(from);
        }

        private static string ReadText(string path, List<string> failures)
        {
            try
            {
                if (!File.Exists(path))
                {
                    failures.Add("MISSING FILE: " + path);
                    return null;
                }
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                failures.Add("UNREADABLE " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Char-by-char comment strip. A regex pass cannot do this safely: a `//` line containing
        /// a `/*` (or a doc comment quoting code) desynchronises it, and a false red in an oracle
        /// is as expensive as a false green because it sends the next seat to fix working code.
        /// Same shape as CollectorIncomeRegression.StripComments, deliberately - string literals
        /// are KEPT because several asserts above match on them.
        /// </summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new System.Text.StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';
                if (c == '/' && n == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
