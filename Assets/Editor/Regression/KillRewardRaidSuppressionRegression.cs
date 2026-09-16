// =============================================================================
// KillRewardRaidSuppressionRegression [kill-reward-raid-suppression] — WO-1227.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// THE OWNER RULING THIS PINS (verbatim, 2026-08-26): "raids only pay at end of raid".
//
// WO-1216 put a wood/iron/stone faucet on the ONE seam every kill flows through
// (Enemy's death grant), per the owner's earlier ruling that the drop comes from
// "any kill, not just waves but in the world the encounters". Correct — with one
// unintended consequence: inside a RAID, every defender the player's troops cut down
// also banked materials, so a raid paid TWICE (per kill, then again at the summary).
// The owner has now ruled that a raid pays ONCE, at the end.
//
// WHAT THIS SUITE ASSERTS — and note that groups 1 and 3 are the two halves of the
// SAME risk. Suppressing raid kills is easy; suppressing them WITHOUT touching the
// open-world / wave / dungeon payout the owner has already felt-verified ("kill in
// open world rewarded fair", "the resources pay out good now") is the hard half, so
// the untouched branch is pinned as hard as the new one.
//
//   1 [suppression]  KillRewardBalanceCatalog.KillMaterialBase(gold, m, raid:true)
//                    is ZERO for wood/iron/stone at every gold base — including the
//                    bosses, where the pre-ruling payout was largest.
//   2 [untouched]    KillMaterialBase(gold, m, raid:FALSE) is EXACTLY
//                    MaterialBaseFromGold(gold, m) — the unchanged WO-1216 number —
//                    across the real enemies.json gold spread (3 / 18 / 31 / 120).
//                    Asserted as an identity against the live formula rather than
//                    against hardcoded amounts, so a legitimate owner retune of
//                    kill-rewards.json moves both sides together and this suite does
//                    not become the thing that blocks a balance pass.
//   3 [state]        RaidScoring.RaidInProgress is the raid test, it is FALSE with no
//                    scorer and a non-raid active scene (the open-world / wave /
//                    dungeon case), and TRUE once a scorer exists. Driven live.
//   4 [seam]         Enemy's death grant reads the live raid state ONCE and derives
//                    all three material bases through KillMaterialBase — source-lint,
//                    so a future edit that goes back to calling MaterialBaseFromGold
//                    directly at the grant site (restoring the double-pay) fails here.
//   5 [trace]        The suppression is NOT SILENT (CLAUDE.md Sec. 12): Enemy traces
//                    the withheld kill, RaidVictoryController traces the one end-of-
//                    raid payout. A silent suppression is indistinguishable from a
//                    broken faucet and this repo has been burned by exactly that.
//   6 [payout]       The end-of-raid grant the ruling redirects payment TO still
//                    exists and is still called (RaidScoring.ComputeLoot ->
//                    RaidVictoryController.GrantLoot -> EconomyService.Grant).
//   7 [hygiene]      No embedded NUL in the touched sources (CLAUDE.md Sec. 0).
//
// HOW TO SEE IT RED (WO-1138 — the suite must fail on the pre-ruling behaviour):
// in KillRewardBalanceCatalog.KillMaterialBase, delete the single line
// `if (raidInProgress) return 0;`. That restores exactly the pre-WO-1227 behaviour
// (raid kills pay the full WO-1216 amount), still compiles, and group 1 then fails
// with "raid-active kill paid <n> wood" on every row of the table below. Group 2
// stays GREEN through that revert, which is the point: it is measuring the branch
// the ruling must not touch.
//
// Every source-lint reads CODE ONLY (comment lines dropped, trailing // comments
// dropped, string-literal CONTENTS blanked) so the self-documenting removal notes
// CLAUDE.md Sec. 12/15 asks for cannot trip a rule — the defect
// EchoWorldPresenceRegression records in its own header.
//
// Markers: KILL_REWARD_RAID_SUPPRESSION_OK / KILL_REWARD_RAID_SUPPRESSION_FAIL.
// Standalone: run-unity-method
//   -Method DeNelle.Editor.Regression.KillRewardRaidSuppressionRegression.RunAll
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class KillRewardRaidSuppressionRegression
    {
        private const string EnemySrc   = "Assets/_Modules/Village/Enemies/Enemy.cs";
        private const string ScoringSrc = "Assets/_Modules/Village/Troops/RaidScoring.cs";
        private const string VictorySrc = "Assets/_Modules/Village/World/Camps/RaidVictoryController.cs";

        /// <summary>The real gold spread measured from enemies.json in WO-1216 Sec. 4:
        /// min 3 / median 18 / mean 31 / max 120. Using the REAL spread (not round
        /// numbers) keeps the floor and the cap inside the table.</summary>
        private static readonly int[] GoldBases = { 3, 18, 31, 120 };
        private static readonly string[] Materials = { "wood", "iron", "stone" };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("KILL_REWARD_RAID_SUPPRESSION_OK - " + reason);
            else Debug.LogError("KILL_REWARD_RAID_SUPPRESSION_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                CheckSuppressedInRaid(failures);
                CheckUntouchedOutsideRaid(failures, notes);
                CheckRaidState(failures, notes);
                CheckGrantSeam(failures);
                CheckTraces(failures);
                CheckEndOfRaidPayout(failures);
                CheckHygiene(failures);
            }
            catch (Exception ex)
            {
                failures.Add("[suite] threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures.ToArray());
                return false;
            }
            reason = "WO-1227 holds: a kill taken while a raid is live pays ZERO wood/iron/stone, " +
                     "the identical kill outside a raid pays the unchanged WO-1216 amount, " +
                     "RaidScoring.RaidInProgress is the one raid test (false outside a raid scene, so " +
                     "open-world / wave / dungeon kills cannot reach the gate), the suppression and the " +
                     "single end-of-raid payout are both traced, and that payout is still wired." +
                     (notes.Count > 0 ? " NOTES: " + string.Join("; ", notes.ToArray()) : "");
            return true;
        }

        // -- 1 [suppression] --------------------------------------------------
        // THE RULING. A raid kill pays nothing; the raid pays once, at the end.
        private static void CheckSuppressedInRaid(List<string> failures)
        {
            foreach (int gold in GoldBases)
            {
                foreach (string m in Materials)
                {
                    int paid = KillRewardBalanceCatalog.KillMaterialBase(gold, m, true);
                    if (paid != 0)
                        failures.Add("[suppression] raid-active kill paid " + paid + " " + m +
                                     " at goldBase=" + gold + " - owner ruling 2026-08-26 is " +
                                     "\"raids only pay at end of raid\"; the per-kill material " +
                                     "grant must be ZERO for the whole raid.");
                }
            }
        }

        // -- 2 [untouched] ----------------------------------------------------
        // THE HALF THAT MUST NOT MOVE. Open world / wave / dungeon / arena all take
        // this branch, and the owner has already felt-verified those amounts.
        private static void CheckUntouchedOutsideRaid(List<string> failures, List<string> notes)
        {
            foreach (int gold in GoldBases)
            {
                foreach (string m in Materials)
                {
                    int expected = KillRewardBalanceCatalog.MaterialBaseFromGold(gold, m);
                    int actual = KillRewardBalanceCatalog.KillMaterialBase(gold, m, false);
                    if (actual != expected)
                        failures.Add("[untouched] non-raid kill paid " + actual + " " + m +
                                     " at goldBase=" + gold + " but the unchanged WO-1216 formula " +
                                     "says " + expected + " - the raid ruling must not move the " +
                                     "open-world / wave / dungeon payout.");
                }
            }

            // A non-paying base must stay non-paying in BOTH branches: the floor may never
            // MINT a reward from nothing (the invariant MaterialBaseFromGold already keeps,
            // restated here because the new branch is a second door into the same formula).
            if (KillRewardBalanceCatalog.KillMaterialBase(0, "wood", false) != 0)
                failures.Add("[untouched] a zero gold base minted a material reward outside a raid.");

            notes.Add("live WO-1216 balance (unchanged by this ticket): mult=" +
                      KillRewardBalanceCatalog.GoldToMaterialMultiplier.ToString("0.00") +
                      " floor=" + KillRewardBalanceCatalog.MaterialFloorPerKill +
                      " cap=" + KillRewardBalanceCatalog.MaterialCapPerKill +
                      " -> median enemy (gold 18) pays " +
                      KillRewardBalanceCatalog.MaterialBaseFromGold(18, "wood") + " of each material");
        }

        // -- 3 [state] --------------------------------------------------------
        // Drive the real raid test. EditMode never runs Awake, so the scorer's
        // Instance is set the way the runtime sets it (its own private setter),
        // via reflection — the state is real, only the trigger is simulated.
        private static void CheckRaidState(List<string> failures, List<string> notes)
        {
            var prop = typeof(RaidScoring).GetProperty("RaidInProgress",
                BindingFlags.Public | BindingFlags.Static);
            if (prop == null)
            {
                failures.Add("[state] RaidScoring.RaidInProgress is gone - the ONE \"am I in a raid\" " +
                             "answer Enemy's grant reads. A second flag must never be invented; " +
                             "restore this one.");
                return;
            }

            var instProp = typeof(RaidScoring).GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            var setter = instProp != null ? instProp.GetSetMethod(true) : null;
            if (setter == null)
            {
                notes.Add("group [state] partially SKIPPED - RaidScoring.Instance has no reachable " +
                          "setter, so the raid-active half could not be driven live (the pure-function " +
                          "half in group [suppression] still covers the ruling)");
            }

            RaidScoring prior = RaidScoring.Instance;
            GameObject go = null;
            try
            {
                // No scorer + a non-raid active scene = every open-world, wave and dungeon
                // kill. This assertion IS the guarantee that they are untouched.
                if (setter != null) setter.Invoke(null, new object[] { null });
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!DeNelle.Core.HubScenes.IsRaid(scene))
                {
                    if ((bool)prop.GetValue(null))
                        failures.Add("[state] RaidInProgress read TRUE with no scorer in non-raid scene '" +
                                     scene + "' - every open-world / wave / dungeon kill would be " +
                                     "suppressed. This is the main risk in WO-1227.");
                }
                else
                {
                    notes.Add("group [state] no-scorer case SKIPPED - the active scene '" + scene +
                              "' IS a raid scene");
                }

                if (setter != null)
                {
                    go = new GameObject("RaidScoringOracle");
                    var scorer = go.AddComponent<RaidScoring>();
                    setter.Invoke(null, new object[] { scorer });
                    if (!(bool)prop.GetValue(null))
                        failures.Add("[state] RaidInProgress read FALSE while a live RaidScoring exists - " +
                                     "the scorer's lifetime IS the raid, so raid kills would keep paying.");
                }
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (setter != null) setter.Invoke(null, new object[] { prior });
            }
        }

        // -- 4 [seam] ---------------------------------------------------------
        private static void CheckGrantSeam(List<string> failures)
        {
            string code = CodeText(EnemySrc, failures);
            if (code == null) return;

            if (code.IndexOf("RaidScoring.RaidInProgress", StringComparison.Ordinal) < 0)
                failures.Add("[seam] Enemy.cs no longer reads RaidScoring.RaidInProgress - the death " +
                             "grant is the ONE place that reads the live raid state.");

            // string CONTENTS are blanked by CodeText, so the material names are not
            // matchable - the CALL SHAPE is, and it is the thing that matters.
            if (code.IndexOf("KillMaterialBase(goldBase,", StringComparison.Ordinal) < 0)
                failures.Add("[seam] Enemy.cs's death grant does not derive its material bases " +
                             "through KillRewardBalanceCatalog.KillMaterialBase(goldBase, <material>, " +
                             "raidInProgress) - calling MaterialBaseFromGold directly at the grant " +
                             "site restores the raid double-pay WO-1227 removed.");

            // Exactly three material bases must go through the gated call - one per material.
            int gated = CountOccurrences(code, "KillRewardBalanceCatalog.KillMaterialBase(goldBase,");
            if (gated != 3)
                failures.Add("[seam] expected 3 gated material-base calls in Enemy.cs (wood/iron/stone), " +
                             "found " + gated + " - an ungated material would pay through the raid.");
        }

        // -- 5 [trace] --------------------------------------------------------
        private static void CheckTraces(List<string> failures)
        {
            string enemy = ReadAll(EnemySrc, failures);
            if (enemy != null && enemy.IndexOf("KILL MATERIALS SUPPRESSED", StringComparison.Ordinal) < 0)
                failures.Add("[trace] Enemy.cs no longer traces the suppressed kill - a SILENT " +
                             "suppression is indistinguishable from a broken faucet (CLAUDE.md Sec. 12).");

            string victory = ReadAll(VictorySrc, failures);
            if (victory != null && victory.IndexOf("RAID END PAYOUT", StringComparison.Ordinal) < 0)
                failures.Add("[trace] RaidVictoryController.GrantLoot no longer traces the one " +
                             "end-of-raid payout - it is the counterpart line that proves the raid " +
                             "paid once instead of not at all.");
        }

        // -- 6 [payout] -------------------------------------------------------
        // The ruling redirects payment to the end-of-raid grant; if THAT went away the
        // raid would pay nothing at all, which reads to the player exactly like the bug.
        private static void CheckEndOfRaidPayout(List<string> failures)
        {
            string code = CodeText(VictorySrc, failures);
            if (code == null) return;

            if (code.IndexOf("GrantLoot(", StringComparison.Ordinal) < 0)
                failures.Add("[payout] RaidVictoryController no longer calls GrantLoot - with per-kill " +
                             "materials suppressed, a raid would pay NOTHING.");
            if (code.IndexOf("LootFor(", StringComparison.Ordinal) < 0)
                failures.Add("[payout] RaidVictoryController no longer computes the raid loot " +
                             "(RaidScoring.LootFor) - the payout the ruling points at is gone.");
            if (code.IndexOf("EconomyService.EnsureAvailable()", StringComparison.Ordinal) < 0
                || code.IndexOf("eco.Grant(loot)", StringComparison.Ordinal) < 0)
                failures.Add("[payout] RaidVictoryController's loot no longer reaches EconomyService - " +
                             "the earned-income path the player's wallet actually reads.");
        }

        // -- 7 [hygiene] ------------------------------------------------------
        private static void CheckHygiene(List<string> failures)
        {
            foreach (string path in new[] { EnemySrc, ScoringSrc, VictorySrc })
            {
                string text = ReadAll(path, failures);
                if (text != null && text.IndexOf('\0') >= 0)
                    failures.Add("[hygiene] embedded NUL byte in " + path + " (CLAUDE.md Sec. 0 - " +
                                 "a mount-garbled file poisons the commit).");
            }
        }

        // -- helpers ----------------------------------------------------------

        private static string ReadAll(string relPath, List<string> failures)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), relPath);
            if (!File.Exists(full))
            {
                failures.Add("[suite] source not found: " + relPath);
                return null;
            }
            return File.ReadAllText(full);
        }

        /// <summary>Source with comment lines dropped, trailing // comments dropped and
        /// string-literal CONTENTS blanked — so a lint reads CALLS, never sentences.</summary>
        private static string CodeText(string relPath, List<string> failures)
        {
            string raw = ReadAll(relPath, failures);
            if (raw == null) return null;

            var sb = new StringBuilder(raw.Length);
            foreach (string line in raw.Replace("\r\n", "\n").Split('\n'))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
                    || t.StartsWith("/*", StringComparison.Ordinal))
                    continue;
                sb.Append(StripLine(line)).Append('\n');
            }
            return sb.ToString();
        }

        private static string StripLine(string line)
        {
            var sb = new StringBuilder(line.Length);
            bool inStr = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inStr && c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
                if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                {
                    inStr = !inStr;
                    sb.Append(c);
                    continue;
                }
                sb.Append(inStr ? ' ' : c);
            }
            return sb.ToString();
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
