// =============================================================================
// ArmyMusterRegression — headless oracle for WO-897 "army composition auto-queues
// the build-outs". Marker: ARMY_MUSTER_OK / ARMY_MUSTER_FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Wired into DataRegression.RunAll.
// Style/contract mirrors the other Run(out reason) oracles.
//
// Proves, with REAL types and no play mode:
//   * the composition model — Set/Add clamp, a 0 row is REMOVED, TotalUnits sums;
//   * the pure planner — the parallel-aware batch makespan (1 slot = sum, N slots =
//     earliest-free assignment), the five-per-line depth arithmetic, ASCII durations;
//   * NO SECOND QUEUE — ArmyMusterService routes through BarracksService.EnqueueTraining
//     and never calls BuildTimerService.Enqueue itself (a forked enqueue would bypass the
//     spend + the army cap);
//   * NO SILENT CAP — BarracksService carries the reason-reporting overload the muster
//     needs, and MusterReport exposes the counts a partial muster must surface as TEXT;
//   * ASCII ONLY — no non-ASCII byte in any player-visible string these files emit.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class ArmyMusterRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== ArmyMusterRegression: army composition -> Train-queue muster (WO-897) ===");

            try
            {
                CheckCompositionModel(failures, log);
                CheckBatchMakespan(failures, log);
                CheckDepthArithmetic(failures, log);
                CheckDurationFormatting(failures, log);
                CheckNoSecondQueue(failures, log);
                CheckNoSilentCap(failures, log);
                CheckLoadoutBank(failures, log);
                CheckAsciiOnly(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add($"ArmyMusterRegression threw: {ex.GetType().Name}: {ex.Message}");
            }

            return Verdict(failures, log, out reason);
        }

        // ── 1. composition model ──────────────────────────────────────────────
        private static void CheckCompositionModel(List<string> failures, StringBuilder log)
        {
            var comp = new ArmyComposition { Name = "Test Army" };
            comp.Set("troop-spearman", 5);
            comp.Set("troop-archer", 3);
            comp.Add("troop-outrider", 2);

            if (comp.TotalUnits != 10)
                failures.Add($"ArmyComposition.TotalUnits is {comp.TotalUnits}, expected 10 (5+3+2)");
            if (comp.CountOf("troop-archer") != 3)
                failures.Add($"CountOf(troop-archer) is {comp.CountOf("troop-archer")}, expected 3");

            comp.Add("troop-archer", -1);
            if (comp.CountOf("troop-archer") != 2)
                failures.Add("Add(-1) did not decrement the row");

            // A row driven to 0 must be REMOVED, not left as an empty line that musters nothing.
            comp.Set("troop-archer", 0);
            if (comp.Find("troop-archer") != null)
                failures.Add("a row set to 0 was kept — an empty row must be removed from the composition");

            // Negative counts clamp to 0 (and therefore remove).
            comp.Add("troop-outrider", -99);
            if (comp.CountOf("troop-outrider") != 0)
                failures.Add("a negative count did not clamp to 0");

            if (comp.TotalUnits != 5)
                failures.Add($"TotalUnits after removals is {comp.TotalUnits}, expected 5");

            comp.Clear();
            if (comp.TotalUnits != 0) failures.Add("Clear() left units in the composition");

            log.AppendLine("  composition model (set/add/clamp/remove/total) OK");
        }

        // ── 2. parallel-aware batch time ──────────────────────────────────────
        private static void CheckBatchMakespan(List<string> failures, StringBuilder log)
        {
            var durations = new List<double> { 10d, 10d, 10d, 10d };

            double one = ArmyMusterPlanner.BatchSeconds(durations, 1);
            if (Math.Abs(one - 40d) > 0.001d)
                failures.Add($"BatchSeconds with 1 slot is {one}, expected 40 (the plain sum)");

            double two = ArmyMusterPlanner.BatchSeconds(durations, 2);
            if (Math.Abs(two - 20d) > 0.001d)
                failures.Add($"BatchSeconds with 2 slots is {two}, expected 20 (2 waves of 10s)");

            // Uneven work must go to the EARLIEST-FREE worker, not round-robin:
            // 30/10/10 on 2 slots = 30 (slot A does the 30; slot B does 10+10).
            double uneven = ArmyMusterPlanner.BatchSeconds(new List<double> { 30d, 10d, 10d }, 2);
            if (Math.Abs(uneven - 30d) > 0.001d)
                failures.Add($"BatchSeconds(30/10/10, 2 slots) is {uneven}, expected 30 (earliest-free assignment)");

            if (ArmyMusterPlanner.BatchSeconds(null, 2) != 0d)
                failures.Add("BatchSeconds(null) is not 0");
            if (ArmyMusterPlanner.BatchSeconds(new List<double>(), 2) != 0d)
                failures.Add("BatchSeconds(empty) is not 0");
            // A 0 or negative slot count must not divide-by-zero or return 0 work.
            if (Math.Abs(ArmyMusterPlanner.BatchSeconds(durations, 0) - 40d) > 0.001d)
                failures.Add("BatchSeconds with 0 slots did not clamp to a single worker");

            log.AppendLine("  batch makespan (1 slot = sum, N slots = earliest-free) OK");
        }

        // ── 3. the five-per-line depth cap arithmetic (owner ruling WO-911 Q4) ─
        private static void CheckDepthArithmetic(List<string> failures, StringBuilder log)
        {
            if (ArmyMusterPlanner.TrainQueueDepthCap != 5)
                failures.Add($"TrainQueueDepthCap is {ArmyMusterPlanner.TrainQueueDepthCap} — the owner ruled FIVE per line (WO-911 Q4)");

            if (ArmyMusterPlanner.RoomInLine(0) != 5) failures.Add("RoomInLine(0) is not 5");
            if (ArmyMusterPlanner.RoomInLine(3) != 2) failures.Add("RoomInLine(3) is not 2");
            if (ArmyMusterPlanner.RoomInLine(5) != 0) failures.Add("RoomInLine(5) is not 0");
            // Over-full (a line that somehow exceeded the cap) must read 0 room, never negative.
            if (ArmyMusterPlanner.RoomInLine(9) != 0) failures.Add("RoomInLine(9) is negative — room must floor at 0");
            if (ArmyMusterPlanner.RoomInLine(-2) != 5) failures.Add("RoomInLine(-2) did not treat a negative depth as 0");

            log.AppendLine("  queue-depth arithmetic (cap 5, floored at 0) OK");
        }

        // ── 4. ASCII duration formatting ──────────────────────────────────────
        private static void CheckDurationFormatting(List<string> failures, StringBuilder log)
        {
            if (ArmyMusterPlanner.FormatDuration(0d) != "0s") failures.Add("FormatDuration(0) is not '0s'");
            if (ArmyMusterPlanner.FormatDuration(45d) != "45s") failures.Add("FormatDuration(45) is not '45s'");
            if (ArmyMusterPlanner.FormatDuration(750d) != "12m 30s") failures.Add("FormatDuration(750) is not '12m 30s'");
            if (ArmyMusterPlanner.FormatDuration(7500d) != "2h 05m") failures.Add("FormatDuration(7500) is not '2h 05m'");

            foreach (double d in new[] { 0d, 45d, 750d, 7500d })
                foreach (char c in ArmyMusterPlanner.FormatDuration(d))
                    if (c > 127)
                    {
                        failures.Add($"FormatDuration({d}) emitted a non-ASCII character — TMP renders it as tofu");
                        break;
                    }

            log.AppendLine("  duration formatting (ASCII, s/m/h) OK");
        }

        // ── 5. NO SECOND QUEUE — the muster reuses the single train path ───────
        private static void CheckNoSecondQueue(List<string> failures, StringBuilder log)
        {
            string raw = ReadSource("Assets/_Modules/Village/Troops/ArmyMusterService.cs", failures);
            if (raw == null) return;
            // Search CODE only - the file's header comment legitimately NAMES the enqueue call it
            // delegates to, and a comment must never trip the "you forked the queue" oracle.
            string svc = StripLineComments(raw);

            if (svc.IndexOf("BarracksService.EnqueueTraining", StringComparison.Ordinal) < 0)
                failures.Add("ArmyMusterService does not call BarracksService.EnqueueTraining — the muster MUST reuse the one train path");

            // A direct queue.Enqueue would bypass the resource spend AND the army-cap check.
            if (svc.IndexOf(".Enqueue(JobKind", StringComparison.Ordinal) >= 0 ||
                svc.IndexOf("JobKind.TrainTroop,", StringComparison.Ordinal) >= 0)
                failures.Add("ArmyMusterService enqueues a job DIRECTLY — that forks the train path (spend + army cap bypassed)");

            // Reading the Train channel is fine; owning a private timer/list is not.
            if (svc.IndexOf("ChannelId.Train", StringComparison.Ordinal) < 0)
                failures.Add("ArmyMusterService never names ChannelId.Train — it must measure the EXISTING Train line");

            var t = typeof(ArmyMusterService);
            if (t.GetMethod("Muster", BindingFlags.Public | BindingFlags.Static) == null)
                failures.Add("ArmyMusterService.Muster(ArmyComposition) is missing");
            if (t.GetMethod("Preview", BindingFlags.Public | BindingFlags.Static) == null)
                failures.Add("ArmyMusterService.Preview(ArmyComposition) is missing");
            if (t.GetMethod("TrainLineDepth", BindingFlags.Public | BindingFlags.Static) == null)
                failures.Add("ArmyMusterService.TrainLineDepth() is missing (the depth read the cap needs)");

            log.AppendLine("  no second queue — muster routes through BarracksService.EnqueueTraining OK");
        }

        // ── 6. NO SILENT CAP — the shortfall is reportable ────────────────────
        private static void CheckNoSilentCap(List<string> failures, StringBuilder log)
        {
            // The reason-carrying overload the muster needs to attribute a shortfall.
            var overload = typeof(BarracksService).GetMethod("EnqueueTraining",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(int), typeof(string).MakeByRefType() }, null);
            if (overload == null)
                failures.Add("BarracksService.EnqueueTraining(string,int,out string) is missing — a shortfall could not be attributed");

            // The original two-arg signature must SURVIVE (TroopTrainingVM binds it as a delegate).
            if (typeof(BarracksService).GetMethod("EnqueueTraining",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(int) }, null) == null)
                failures.Add("BarracksService.EnqueueTraining(string,int) was removed — TroopTrainingVM binds it");

            var rep = typeof(MusterReport);
            foreach (string member in new[] { "TotalRequested", "TotalQueued", "TotalNotQueued", "Headline", "Detail", "Rows" })
                if (rep.GetField(member) == null && rep.GetProperty(member) == null)
                    failures.Add($"MusterReport.{member} is missing — the partial-muster tell needs it");

            // A shortfall must be NAMED in the trace, not swallowed.
            string svc = ReadSource("Assets/_Modules/Village/Troops/ArmyMusterService.cs", failures);
            if (svc != null)
            {
                if (svc.IndexOf("FlowTrace.Warn", StringComparison.Ordinal) < 0)
                    failures.Add("ArmyMusterService never calls FlowTrace.Warn — a dropped unit would be a SILENT cap (forbidden)");
                if (svc.IndexOf("Guard.Try", StringComparison.Ordinal) < 0)
                    failures.Add("ArmyMusterService never calls Guard.Try — one bad row could abort the whole muster");
            }

            // The per-troop enqueue Step (the proving line "did all 8 land?" is read from).
            string barracks = ReadSource("Assets/_Modules/Village/Troops/BarracksService.cs", failures);
            if (barracks != null && barracks.IndexOf("train job enqueued", StringComparison.Ordinal) < 0)
                failures.Add("BarracksService does not emit a per-unit 'train job enqueued' Step — a muster leaves no per-troop proving line");

            log.AppendLine("  no silent cap — reason overload + report counts + Warn/Guard instrumentation OK");
        }

        // ── 6b. WO-934 loadout bank: 3 slots + convert round-trip ──────────────
        private static void CheckLoadoutBank(List<string> failures, StringBuilder log)
        {
            if (ArmyLoadoutBank.SlotCount != 3)
                failures.Add($"ArmyLoadoutBank.SlotCount is {ArmyLoadoutBank.SlotCount}, expected 3");

            var army = new ArmyStorage();
            army.EnsureLoadouts();
            if (army.Loadouts == null || army.Loadouts.Count != 3)
                failures.Add($"EnsureLoadouts left {army.Loadouts?.Count ?? -1} slots, expected 3");

            var working = new ArmyComposition { Name = "Test Raid" };
            working.Set("troop-footman", 3);
            working.Set("troop-archer", 2);
            var slot = working.ToLoadout();
            if (slot.TotalUnits != 5)
                failures.Add($"ToLoadout TotalUnits={slot.TotalUnits}, expected 5");
            var back = ArmyComposition.FromLoadout(slot);
            if (back.TotalUnits != 5 || back.CountOf("troop-footman") != 3)
                failures.Add("FromLoadout did not restore footman/archer counts");
            if (!string.Equals(back.Name, "Test Raid", StringComparison.Ordinal))
                failures.Add($"FromLoadout name='{back.Name}', expected 'Test Raid'");

            // Service type surface.
            var svc = typeof(ArmyLoadoutService);
            foreach (string m in new[] { "LoadInto", "SaveFrom", "ApplyRecipe", "Ensure" })
                if (svc.GetMethod(m, BindingFlags.Public | BindingFlags.Static) == null)
                    failures.Add($"ArmyLoadoutService.{m} is missing");

            // The bank must be LIVE UI, and that is a two-link chain since WO-1512 moved the
            // loadout verbs off the View: the VM must call ArmyLoadoutService, and the panel must
            // route through the VM. Pinning only the panel's own use of the service would have
            // failed a correct separation; pinning only the VM would let the screen orphan it.
            string panel = ReadSource("Assets/_Modules/Village/Troops/ArmyMusterPanel.cs", failures);
            if (panel != null)
            {
                if (panel.IndexOf("ArmyMusterVM", StringComparison.Ordinal) < 0)
                    failures.Add("ArmyMusterPanel does not route through ArmyMusterVM — the loadout bank would be unreachable");
                // WO-1857: the save-slot button caption is now a LocalizedText.Format resolve, so the
                // needle pins the resolved KEY instead of the English caption. The retired caption is
                // deliberately not reproduced here - it still appears in a comment inside
                // ArmyMusterPanel.cs, so a source-text needle on it would pass off that comment and
                // assert nothing.
                if (panel.IndexOf("\"village.troops.army_muster.save_slot\"", StringComparison.Ordinal) < 0)
                    failures.Add("ArmyMusterPanel missing Save slot control");
            }

            string vm = ReadSource("Assets/_Modules/Village/Troops/ArmyMusterVM.cs", failures);
            if (vm != null)
            {
                if (vm.IndexOf("ArmyLoadoutService", StringComparison.Ordinal) < 0)
                    failures.Add("ArmyMusterVM does not use ArmyLoadoutService — loadouts would be dead UI");
            }

            log.AppendLine("  loadout bank — 3 slots + To/FromLoadout + service surface + VM->ArmyLoadoutService OK");
        }

        // ── 7. ASCII-only player-visible strings ──────────────────────────────
        private static void CheckAsciiOnly(List<string> failures, StringBuilder log)
        {
            string[] paths =
            {
                "Assets/_Modules/Village/Troops/ArmyComposition.cs",
                "Assets/_Modules/Village/Troops/ArmyMusterService.cs",
                "Assets/_Modules/Village/Troops/ArmyMusterPanel.cs",
            };

            foreach (string rel in paths)
            {
                string src = ReadSource(rel, failures);
                if (src == null) continue;

                var lines = src.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Comments may carry the project's usual typography; STRING LITERALS may not.
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*")) continue;
                    if (line.IndexOf('"') < 0) continue;
                    foreach (char c in line)
                        if (c > 127)
                        {
                            failures.Add($"{rel}:{i + 1} carries a non-ASCII character in a code line with a string literal — TMP renders it as tofu");
                            break;
                        }
                }
            }

            log.AppendLine("  ASCII-only string literals OK");
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>Drops every whole-line // and /// comment so a source oracle matches CODE, not prose.</summary>
        private static string StripLineComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            foreach (string line in src.Split('\n'))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        private static string ReadSource(string relativePath, List<string> failures)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                failures.Add($"source file missing: {relativePath}");
                return null;
            }
            return File.ReadAllText(full);
        }

        private static bool Verdict(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = "ARMY MUSTER OK - composition model + parallel-aware batch time + five-per-line depth " +
                         "arithmetic + ASCII durations + single-train-path reuse + no-silent-cap reporting all hold";
                Debug.Log("ARMY_MUSTER_OK\n" + log);
                return true;
            }
            reason = $"ARMY MUSTER: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"ARMY_MUSTER_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}
