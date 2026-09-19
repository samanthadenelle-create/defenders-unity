// =============================================================================
// CollectorOverflowRegression [collector-overflow]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers: COLLECTOR_OVERFLOW_OK / COLLECTOR_OVERFLOW_FAIL.
//
// WHAT THIS PINS (WO-1883, owner 2026-09-19): pending stays at the collector until
// Collect. Accrue auto-spill to the bank (ruling 26b TryOverflowToBank while accruing)
// is RETIRED so the player has a reason to come back and collect.
//
//   Owner, verbatim: "also in town when you have more resources than you can store I
//   think we only leave them there short term, otherwise there is no reason to need to
//   come back and collect them"
//
// Still pinned from ruling 26b / WO-1392:
//   - Bank-full STALLS at the collector (does not burn what is already held).
//   - Collect banks up to headroom and leaves the remainder pending.
//   - Manual tap unchanged; no tap bonus without an owner ruling.
//
// OUT OF SCOPE: Raid Cache TTL, storage cap numbers, Echo harvest formula.
//
// -----------------------------------------------------------------------------
// REVERT RECIPES (per case) - what to delete to put the tree back the way it was.
//
//   [overflow-never-burns]           Delete ResourceCollector.SimulateAccrue and this case.
//   [pending-until-collect]          Delete this case; restore Accrue auto-spill if wanted.
//   [collect-banks-headroom]         Delete SettleOverflow + this case.
//   [tap-still-works]                Delete this case only - pre-existing tap pin.
//   [no-accrue-auto-spill]           Delete this case; restore TryOverflowToBank in Accrue.
//   WHOLE SUITE                      Delete this file and its DataRegression.cs registration.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village.Buildings.Progression;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1883: Accrue keeps pending at the collector; Collect banks headroom; nothing burns.</summary>
    public static class CollectorOverflowRegression
    {
        private const string CollectorSrc =
            "Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs";

        // Fixtures from OWNER_RULINGS_LOCKED.md "Ruling 26 - MEASURED" (2026-09-06).
        // Balance numbers are the owner's; this suite asserts MECHANISM only.
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
            Case(failures, "pending-until-collect", () => CasePendingUntilCollect(failures, notes));
            Case(failures, "collect-banks-headroom", () => CaseCollectBanksHeadroom(failures, notes));
            Case(failures, "tap-still-works", () => CaseTapStillWorks(failures, notes));
            Case(failures, "no-accrue-auto-spill", () => CaseNoAccrueAutoSpill(failures, notes));

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count > 0)
            {
                reason = "collector-overflow FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
                return false;
            }

            reason = "COLLECTOR OVERFLOW OK (WO-1883) - Accrue keeps pending at the collector " +
                     "(bank room alone never auto-spills), a full collector STALLS and never burns " +
                     "what is already held (pendingAfter + unproduced == pendingBefore + owed, " +
                     "pendingAfter >= held when capacity does not shrink), Collect banks up to " +
                     "headroom and leaves the remainder pending (SettleOverflow / SettleCollect), " +
                     "Accrue has no TryOverflowToBank seam, and the MANUAL TAP is unchanged with " +
                     "NO bonus"
                     + noteStr;
            return true;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  NOTHING ALREADY HELD IS EVER LOST - Accrue stalls at cap.
        // =====================================================================

        private static void CaseNeverBurns(List<string> failures, List<string> notes)
        {
            // (a) Full collector + more owed + full bank: stall, pending untouched, all owed unproduced.
            double after = ResourceCollector.SimulateAccrue(
                QuarryCapFixture, QuarryCapFixture, owed: 1000.0, bankRoom: 0,
                out int banked, out double unproduced);
            if (banked != 0 || Math.Abs(after - QuarryCapFixture) > 1e-9)
                failures.Add($"[overflow-never-burns] a full Quarry ({QuarryCapFixture:0}) with bankRoom 0 " +
                             $"ended at pending {after:0.###} / banked {banked}; expected the pool UNTOUCHED at " +
                             $"{QuarryCapFixture:0} and 0 banked - the collector stalls, it does not empty itself");
            if (Math.Abs(unproduced - 1000.0) > 1e-9)
                failures.Add($"[overflow-never-burns] the stalled tick reported {unproduced:0.###} unproduced; expected the " +
                             "whole 1000 (the STALL is the design; what must never be lost is what is already HELD)");

            // (b) Same identities across shapes. Accrue never banks, so bankRoom is a red-herring
            //     input that must not change the books.
            var pendings = new double[] { 0.0, 1.0, 1234.5, 3456.0, QuarryCapFixture };
            var caps = new double[] { 0.0, 3456.0, QuarryCapFixture };
            var owedSet = new double[] { 0.0, 13.0, 1000.0, 60000.0 };
            var rooms = new int[] { 0, 1, L1BankFixture, 34000 };
            int swept = 0;
            foreach (double p0 in pendings)
                foreach (double cap in caps)
                    foreach (double owed in owedSet)
                        foreach (int room in rooms)
                        {
                            // Capacity clamp truncates an over-cap pool (pre-existing); skip that shape.
                            if (p0 > cap) continue;

                            double pool = ResourceCollector.SimulateAccrue(
                                p0, cap, owed, room, out int got, out double lost);
                            swept++;

                            if (got != 0)
                                failures.Add($"[overflow-never-burns] Accrue banked {got} at pending={p0} cap={cap} " +
                                             $"owed={owed} room={room} - WO-1883 retired auto-spill; banked must be 0");

                            if (pool + got + lost - (p0 + owed) > 1e-6 || (p0 + owed) - (pool + got + lost) > 1e-6)
                                failures.Add($"[overflow-never-burns] BOOKS DO NOT CLOSE at pending={p0} cap={cap} " +
                                             $"owed={owed} room={room}: pending {pool:0.###} + banked {got} + unproduced " +
                                             $"{lost:0.###} != {p0 + owed:0.###}");
                            if (pool + got + 1e-6 < p0)
                                failures.Add($"[overflow-never-burns] RESOURCES BURNED at pending={p0} cap={cap} " +
                                             $"owed={owed} room={room}: held {p0:0.###}, afterwards pending {pool:0.###} " +
                                             $"with {got} banked. WO-1392: already-held units stay pending or bank via Collect.");
                        }
            notes.Add($"never-burns swept {swept} pending/capacity/owed/headroom combinations");
        }

        // =====================================================================
        //  BANK ROOM ALONE DOES NOT EMPTY THE MILL - pending until Collect.
        // =====================================================================

        private static void CasePendingUntilCollect(List<string> failures, List<string> notes)
        {
            // (a) Full Quarry + owed + L1 bank room: Accrue must NOT reduce pending / must NOT bank.
            double after = ResourceCollector.SimulateAccrue(
                QuarryCapFixture, QuarryCapFixture, owed: 100.0, bankRoom: L1BankFixture,
                out int banked, out double unproduced);
            if (banked != 0)
                failures.Add($"[pending-until-collect] Accrue with bankRoom {L1BankFixture} banked {banked}; " +
                             "expected 0 - auto-spill is retired (WO-1883), Collect is the only bank path");
            if (Math.Abs(after - QuarryCapFixture) > 1e-9)
                failures.Add($"[pending-until-collect] Accrue with bank room reduced pending to {after:0.###}; " +
                             $"expected still at cap {QuarryCapFixture:0}");
            if (Math.Abs(unproduced - 100.0) > 1e-9)
                failures.Add($"[pending-until-collect] expected the whole 100 owed unproduced while at cap; got {unproduced:0.###}");

            // (b) Tick with room after a stall: still no auto-resume via spill.
            double resumed = ResourceCollector.SimulateAccrue(
                QuarryCapFixture, QuarryCapFixture, owed: 500.0, bankRoom: 2000,
                out int banked2, out double unproduced2);
            if (banked2 != 0 || Math.Abs(resumed - QuarryCapFixture) > 1e-9 || Math.Abs(unproduced2 - 500.0) > 1e-9)
                failures.Add($"[pending-until-collect] after stall with new room, Accrue banked {banked2} / pending " +
                             $"{resumed:0.###} / unproduced {unproduced2:0.###}; expected 0 / {QuarryCapFixture:0} / 500 - " +
                             "bank room alone does not clear the stall");

            // (c) Away window fills to cap then stalls; does not drain the bank across passes.
            double away = ResourceCollector.SimulateAccrue(
                0.0, QuarryCapFixture, owed: 60000.0, bankRoom: 34000,
                out int bankedAway, out double unproducedAway);
            if (bankedAway != 0)
                failures.Add($"[pending-until-collect] away Accrue banked {bankedAway}; expected 0 (no auto-spill)");
            if (Math.Abs(away - QuarryCapFixture) > 1e-6)
                failures.Add($"[pending-until-collect] away Accrue left {away:0.###}; expected re-capped at {QuarryCapFixture:0}");
            if (Math.Abs((away + bankedAway + unproducedAway) - 60000.0) > 1e-6)
                failures.Add("[pending-until-collect] away Accrue books do not close");
            notes.Add($"away 60000 owed -> pending {away:0}, banked 0, unproduced {unproducedAway:0}");
        }

        // =====================================================================
        //  COLLECT STILL BANKS HEADROOM - remainder stays pending.
        // =====================================================================

        private static void CaseCollectBanksHeadroom(List<string> failures, List<string> notes)
        {
            // Pure Collect-with-headroom model (SettleOverflow): what fits banks, rest stays.
            double pool = ResourceCollector.SettleOverflow(QuarryCapFixture, L1BankFixture, out int moved, out int left);
            if (moved != L1BankFixture || left != (int)(QuarryCapFixture - L1BankFixture) ||
                Math.Abs(pool - (QuarryCapFixture - L1BankFixture)) > 1e-9)
                failures.Add($"[collect-banks-headroom] SettleOverflow({QuarryCapFixture:0}, {L1BankFixture}) -> moved {moved}, " +
                             $"left {left}, pool {pool:0.###}; expected {L1BankFixture} / " +
                             $"{(int)(QuarryCapFixture - L1BankFixture)} / {QuarryCapFixture - L1BankFixture:0}");

            double leftFull = ResourceCollector.SettleOverflow(QuarryCapFixture, 0, out int moved0, out int leftWhole);
            if (moved0 != 0 || Math.Abs(leftFull - QuarryCapFixture) > 1e-9 || leftWhole != (int)QuarryCapFixture)
                failures.Add($"[collect-banks-headroom] SettleOverflow({QuarryCapFixture:0}, room 0) moved {moved0} and left " +
                             $"{leftFull:0.###}; expected 0 moved and the pool intact");

            leftFull = ResourceCollector.SettleOverflow(-50.0, 999, out moved0, out leftWhole);
            if (moved0 != 0 || Math.Abs(leftFull) > 1e-9)
                failures.Add($"[collect-banks-headroom] a negative pending minted {moved0} units; expected 0");

            leftFull = ResourceCollector.SettleOverflow(120.0, -7, out moved0, out leftWhole);
            if (moved0 != 0 || Math.Abs(leftFull - 120.0) > 1e-9)
                failures.Add($"[collect-banks-headroom] a negative headroom moved {moved0}; expected 0 and the pool intact");

            // Whole pool fits: empties via Collect model.
            pool = ResourceCollector.SettleOverflow(3456.0, 34000, out moved, out left);
            if (moved != 3456 || left != 0 || Math.Abs(pool) > 1e-9)
                failures.Add($"[collect-banks-headroom] SettleOverflow(3456, 34000) -> moved {moved}, left {left}, pool {pool:0.###}; " +
                             "expected full drain");
            notes.Add("Collect headroom model verified at Quarry 7500 / L1 bank 3000");
        }

        // =====================================================================
        //  THE MANUAL TAP IS UNCHANGED - and pays NO bonus.
        // =====================================================================

        private static void CaseTapStillWorks(List<string> failures, List<string> notes)
        {
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
                failures.Add("[tap-still-works] the manual Collect(out,out) tap is GONE. WO-1883 retires Accrue " +
                             "auto-spill; it does NOT remove the tap. Restore it or get an owner ruling.");
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
                failures.Add("[tap-still-works] the tap lost its ResourceGainPopup feedback (WO-890)");

            if (Regex.IsMatch(body, @"amount\s*=\s*\(int\)[^;]*amount\s*\*") ||
                Regex.IsMatch(body, @"\bTapBonus\b|\bCollectBonus\b|\btapMultiplier\b"))
                failures.Add("[tap-still-works] Collect appears to scale the tapped amount - a TAP BONUS is an OPEN " +
                             "owner question in ruling 26 and must NOT be implemented until she rules. Remove it.");
            notes.Add("manual Collect tap still SettleCollect / GrantSpendable / popup");
        }

        // =====================================================================
        //  SOURCE: Accrue must NOT auto-spill; Collect still never-burns.
        // =====================================================================

        private static void CaseNoAccrueAutoSpill(List<string> failures, List<string> notes)
        {
            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            if (code.IndexOf("TryOverflowToBank", StringComparison.Ordinal) >= 0)
                failures.Add("[no-accrue-auto-spill] ResourceCollector still names TryOverflowToBank - WO-1883 retired " +
                             "Accrue auto-spill; delete the seam (Collect remains the bank path)");
            if (code.IndexOf("SettleOverflowPool", StringComparison.Ordinal) >= 0)
                failures.Add("[no-accrue-auto-spill] SettleOverflowPool still present - that was the Accrue spill drain");
            if (Regex.IsMatch(code, @"auto-overflow"))
                failures.Add("[no-accrue-auto-spill] FlowTrace / comments still speak of auto-overflow as a live Accrue " +
                             "path - re-point messages to pending-until-Collect (WO-1883)");

            int accrueAt = code.IndexOf("public void Accrue(", StringComparison.Ordinal);
            if (accrueAt < 0)
            {
                failures.Add("[no-accrue-auto-spill] ResourceCollector.Accrue not found - re-point this oracle");
                return;
            }
            string accrue = Slice(code, accrueAt, "public int Collect(");
            if (!Regex.IsMatch(accrue, @"Math\s*\.\s*Min\s*\(\s*cap"))
                failures.Add("[no-accrue-auto-spill] Accrue no longer clamps pending to the capacity - the cap IS the " +
                             "collector's bound and the stall is built on top of it");
            if (Regex.IsMatch(accrue, @"GrantSpendable"))
                failures.Add("[no-accrue-auto-spill] Accrue calls GrantSpendable - that is auto-spill / direct bank; " +
                             "pending must stay until Collect");
            if (accrue.IndexOf("FlowTrace", StringComparison.Ordinal) < 0)
                failures.Add("[no-accrue-auto-spill] Accrue lost FlowTrace - instrumentation stays (owner ruling)");

            // Collect still asks only what the wallet applies (never-burn).
            int collectAt = code.IndexOf("public int Collect(out int requested", StringComparison.Ordinal);
            if (collectAt >= 0)
            {
                string collect = Slice(code, collectAt, "public static double SettleCollect");
                if (collect.IndexOf("SettleCollect(_pending, banked", StringComparison.Ordinal) < 0)
                    failures.Add("[no-accrue-auto-spill] Collect no longer settles via SettleCollect(_pending, banked, ...)");
            }

            notes.Add("Accrue clamps to cap only; no TryOverflowToBank; FlowTrace retained");
        }

        // =====================================================================

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
