// =====================================================================
//  FitGuardRelaxAllowlistRegression -- WO-1652 §6 REMEDY B, THE PIN.
//  EVERY §1.14 FLOOR RELAXATION IS AN AUTHORING DEFECT ON A LEASH.
// ---------------------------------------------------------------------
//  WHY THIS EXISTS, MEASURED NOT ASSUMED.
//
//  WO-1652 put an arm/evaluate/census trace on UiKitTextFitGuard. The two logs
//  it was built to produce were then read AT SOURCE by this lane (2026-09-10):
//
//    CAPTURE  Builds/wave5-manageflow3 (1,358,244 bytes), last census:
//             armCalls=2642 armed=0 declinedNotPlaying=2642 evaluated=0 relaxed=0
//             -> 2,642 policed labels, ZERO guarded. Every capture PNG we gate on
//                measures the UN-GUARDED authored layout. Lock 1 confirmed at scale.
//
//    DEVICE   Builds/device-frames/2026-09-10_0929_363722_logcat.txt (APK 363722,
//             PID 5095, 4,459,194 bytes), last census:
//             armCalls=314 armed=314 declinedNotPlaying=0 evaluated=88
//             relaxed=8 stillBlank=0
//             -> the guard IS live in the player build, and it relaxed the owner's
//                FontFloor(30) EIGHT times, entirely silently, in one session.
//
//  THE FINDING THAT PICKS THE REMEDY: all eight relaxation lines read
//  "(0 post-check iterations)" and the session's stillBlank count is 0. So the
//  guard's RESCUE path -- the whole reason it exists, iterating the floor down
//  until glyphs render -- fired ZERO times. Every one of the eight is the STATIC
//  fitMin recompute: the band was authored too short to seat the 30 px floor, so
//  the guard quietly shrank the text instead. The guard is not saving us from
//  culls; it is hiding eight authoring defects.
//
//  THAT IS WHY REMEDY A WAS REJECTED. Running the guard inside captures would
//  paint the SHRUNK text into the PNG a human looks at, retiring the glyph
//  oracle's teeth for the WO-1636 class while making the eight defects render
//  "fine". Remedy B keeps the capture measuring authored truth and moves the
//  relaxations from a logcat ring nobody reads into a pinned, enumerated list.
//
//  ⛔ NO FONT FLOOR IS MOVED BY THIS FILE. ElarionUiKit.FontFloor (30) and
//  FontHardFloor (20) are READ here and never written (WO-1652 acceptance §4).
//
//  RED-FIRST (PROD-008 / WO-1138). Cases A and B feed the parser synthetic lines
//  and FAIL IF IT STAYS GREEN -- an unlisted key and a sub-FontHardFloor final
//  size must each red. A device log is NOT required for those two, so this suite
//  can never read green merely because no log was present; and when there is no
//  logcat on disk to scan, Case D takes the RegressionOutcome.PartialSkip channel
//  so the reason carries a machine-readable [PARTIAL-SKIP] naming the hole,
//  instead of a guard-and-return that the caller's bool would read as a pass.
// =====================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DeNelle.Core.UI;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class FitGuardRelaxAllowlistRegression
    {
        private const string Tag = "[fitguard-relax-allowlist]";

        /// <summary>
        /// THE EIGHT. Every floor relaxation observed on APK 363722 (PID 5095),
        /// Builds/device-frames/2026-09-10_0929_363722_logcat.txt, read at source 2026-09-10.
        /// <para/>
        /// ⚠ THIS IS A LEASH, NOT AN APPROVAL. Each entry is a band authored too short to seat
        /// the owner's FontFloor(30) -- the F8 2026-07-08 "text will never be able to be seen on
        /// mobile at this size" ruling. They are listed so a NINTH one reds instead of vanishing
        /// into the logcat ring. Entries come OFF this list by fixing the band, never by widening
        /// the list, and the smallest of them (the gold chip at 21/23 px) is the one to fix first.
        /// <para/>
        /// Keys are UiKitTextFitGuard.PathOf output: the label's own name plus up to four parents.
        /// </summary>
        // Each entry leaves this array only when a FRESH device logcat no longer carries its
        // relaxKey. Deleting one to go green is forbidden (WO-1658 §5.1). If entries are still
        // here on the remove-by date, THAT is the finding: sub-floor labels shipping for a quarter.
        // WO-1658, origin 2026-09-10, remove-by 2026-12-10 - the eight bands authored under the
        // 30 px floor, measured on APK 2026.09.10.363722 (see the header for the two census reads).
        private static readonly RelaxEntry[] Allowlist =
        {
            new RelaxEntry("ObsidianPanel/PanelFill/HarvestRow_Wood/Well/Label", 22f, 24f,
                           "harvest modal, wood 'FULL' well line - 26 px band"),
            new RelaxEntry("HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Wood/Label", 26f, 29f,
                           "harvest overflow, wood 'waiting, safe' - 32 px band"),
            new RelaxEntry("ObsidianPanel/PanelFill/HarvestRow_Iron/Well/Label", 22f, 24f,
                           "harvest modal, iron 'FULL' well line - 26 px band"),
            new RelaxEntry("HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Iron/Label", 26f, 29f,
                           "harvest overflow, iron 'waiting, safe' - 32 px band"),
            new RelaxEntry("ObsidianPanel/PanelFill/HarvestRow_Stone/Well/Label", 22f, 24f,
                           "harvest modal, stone 'FULL' well line - 26 px band"),
            new RelaxEntry("HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Stone/Label", 26f, 29f,
                           "harvest overflow, stone 'waiting, safe' - 32 px band"),
            new RelaxEntry("HudAreasHost/Area_ActionRail/Widget_resourceChipsCollapsed/CurrencyChip_Gold/Label", 21f, 23f,
                           "WORST OFFENDER: the gold '+4' chip renders at 23 px, 7 px under the owner's " +
                           "floor and 3 px off FontHardFloor, on the always-on HUD rail"),
            new RelaxEntry("ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageHeartFace/Label", 28f, 30f,
                           "Manage hub '250 Crystals' - floor moved 30->28 but fontSize stayed 30, so " +
                           "nothing actually shrank; the band is one px from being a real defect"),
        };

        private sealed class RelaxEntry
        {
            public readonly string Key;
            public readonly float FloorTo;
            public readonly float FinalSize;
            public readonly string Note;
            public RelaxEntry(string key, float floorTo, float finalSize, string note)
            { Key = key; FloorTo = floorTo; FinalSize = finalSize; Note = note; }
        }

        private struct RelaxHit
        {
            public string Key;
            public float FloorTo;
            public float FinalSize;
            public string Raw;
        }

        [MenuItem("Tools/Regression/UI/Fit Guard Relax Allowlist (WO-1652)")]
        public static void RunMenu()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason); else Debug.LogError(reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- FIT GUARD RELAX ALLOWLIST (WO-1652 remedy B): every floor relaxation is leashed ---");
            log.AppendLine("  floors READ (never written): FontFloor=" + ElarionUiKit.FontFloor.ToString("F0") +
                           " FontHardFloor=" + ElarionUiKit.FontHardFloor.ToString("F0") + ".");

            try
            {
                CaseA_ParserRedsOnUnlistedKey(failures, log);
                CaseB_ParserRedsBelowHardFloor(failures, log);
                CaseC_TheAuthoredEightParseAndPass(failures, log);
                CaseD_ScanNewestDeviceLog(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw: " + ex.GetType().Name + " " + ex.Message);
            }

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine(Tag + " FAIL (" + failures.Count + "):");
                foreach (var f in failures) sb.AppendLine("  - " + f);
                sb.Append(log.ToString());
                reason = sb.ToString();
                return false;
            }

            reason = Tag + " OK - " + Allowlist.Length + " leashed relaxations, no new ones, none under the hard floor.\n"
                     + log.ToString();
            return true;
        }

        // -----------------------------------------------------------------
        //  CASE A -- RED-FIRST. A relaxation on a label nobody authorised must red.
        //  Runs with no device log, so this suite can never pass vacuously.
        // -----------------------------------------------------------------
        private static void CaseA_ParserRedsOnUnlistedKey(List<string> failures, StringBuilder log)
        {
            string synthetic = "[Flow:UI] TextFitGuard 'Attack' [HudAreasHost/Area_ActionRail/Widget_bar/" +
                               "Face_Attack/Label]: rect 200x24 lineFactor 1.15 — floor 30 -> 23 " +
                               "(0 post-check iterations), fontSize now 25, chars 6 | " +
                               "relaxKey=HudAreasHost/Area_ActionRail/Widget_bar/Face_Attack/Label " +
                               "floorFrom=30 floorTo=23 finalSize=25";

            var violations = Judge(new[] { synthetic });
            if (violations.Count == 0)
            {
                failures.Add(Tag + " CASE A (RED-first): a relaxation on an UNLISTED label passed judgement. " +
                             "The allowlist is then decorative - a ninth silently-shrunk label would ship " +
                             "exactly the way the eight did, which is the whole defect WO-1652 measured.");
                return;
            }
            log.AppendLine("  [red-A] unlisted key correctly rejected: " + violations[0]);
        }

        // -----------------------------------------------------------------
        //  CASE B -- RED-FIRST. A final size under FontHardFloor must red even
        //  when the key IS on the allowlist. The guard clamps at the hard floor
        //  today; this case is what catches a future change that weakens the clamp.
        // -----------------------------------------------------------------
        private static void CaseB_ParserRedsBelowHardFloor(List<string> failures, StringBuilder log)
        {
            string key = Allowlist[0].Key;
            string synthetic = "[Flow:UI] TextFitGuard 'x' [" + key + "]: rect 742x26 lineFactor 1.15 " +
                               "— floor 30 -> 13 (4 post-check iterations), fontSize now 13, chars 1 | " +
                               "relaxKey=" + key + " floorFrom=30 floorTo=13 finalSize=13";

            var violations = Judge(new[] { synthetic });
            if (violations.Count == 0)
            {
                failures.Add(Tag + " CASE B (RED-first): a label rendered at 13 px passed judgement. That is the " +
                             "exact sub-legible size the F8 2026-07-08 ruling raised FontHardFloor to 20 to end " +
                             "(Sylas 24->13, Affiliation 13->12) - being on the allowlist must never buy a label " +
                             "a trip under the hard floor.");
                return;
            }
            log.AppendLine("  [red-B] sub-hard-floor size correctly rejected: " + violations[0]);
        }

        // -----------------------------------------------------------------
        //  CASE C -- the eight real lines, verbatim from the device log, must parse
        //  and must pass. If they do not, the parser and the allowlist have drifted
        //  apart and Cases A/B/D are judging nothing.
        // -----------------------------------------------------------------
        private static void CaseC_TheAuthoredEightParseAndPass(List<string> failures, StringBuilder log)
        {
            // Legacy PROSE form -- exactly as APK 363722 emitted them, WITHOUT the machine tokens
            // this ticket adds. Kept verbatim on purpose: every log already on disk is in this form,
            // and a parser that only understands the new form would read every existing log as clean.
            string[] observed =
            {
                "TextFitGuard '3,000 / 3,000  FULL' [ObsidianPanel/PanelFill/HarvestRow_Wood/Well/Label]: rect 742x26 lineFactor 1.15 — floor 30 -> 22 (0 post-check iterations), fontSize now 24, chars 19",
                "TextFitGuard '25,875 waiting, safe' [HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Wood/Label]: rect 742x32 lineFactor 1.15 — floor 30 -> 26 (0 post-check iterations), fontSize now 29, chars 20",
                "TextFitGuard '3,000 / 3,000  FULL' [ObsidianPanel/PanelFill/HarvestRow_Iron/Well/Label]: rect 742x26 lineFactor 1.15 — floor 30 -> 22 (0 post-check iterations), fontSize now 24, chars 19",
                "TextFitGuard '6,906 waiting, safe' [HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Iron/Label]: rect 742x32 lineFactor 1.15 — floor 30 -> 26 (0 post-check iterations), fontSize now 29, chars 19",
                "TextFitGuard '3,000 / 3,000  FULL' [ObsidianPanel/PanelFill/HarvestRow_Stone/Well/Label]: rect 742x26 lineFactor 1.15 — floor 30 -> 22 (0 post-check iterations), fontSize now 24, chars 19",
                "TextFitGuard '15,000 waiting, safe' [HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Stone/Label]: rect 742x32 lineFactor 1.15 — floor 30 -> 26 (0 post-check iterations), fontSize now 29, chars 20",
                "TextFitGuard '+4' [HudAreasHost/Area_ActionRail/Widget_resourceChipsCollapsed/CurrencyChip_Gold/Label]: rect 398x26 lineFactor 1.15 — floor 30 -> 21 (0 post-check iterations), fontSize now 23, chars 2",
                "TextFitGuard '250 Crystals' [ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageHeartFace/Label]: rect 452x33 lineFactor 1.15 — floor 30 -> 28 (0 post-check iterations), fontSize now 30, chars 12",
            };

            var hits = new List<RelaxHit>();
            foreach (var line in observed)
            {
                if (TryParseRelax(line, out RelaxHit hit)) hits.Add(hit);
                else failures.Add(Tag + " CASE C: the parser could not read a relaxation line the shipped build " +
                                  "actually emitted. Every scan below is then blind. Line: " + line);
            }

            if (hits.Count != Allowlist.Length)
            {
                failures.Add(Tag + " CASE C: parsed " + hits.Count + " of the " + Allowlist.Length +
                             " observed relaxations. The fixture and the allowlist must stay the same size, " +
                             "or the count this ticket reports is fiction.");
                return;
            }

            var violations = Judge(observed);
            if (violations.Count > 0)
            {
                foreach (var v in violations)
                    failures.Add(Tag + " CASE C: an ALLOWLISTED relaxation was rejected, so the list no longer " +
                                 "describes the build it was measured from: " + v);
                return;
            }

            log.AppendLine("  [green-C] all " + hits.Count + " observed relaxations parse and are leashed:");
            foreach (var e in Allowlist)
                log.AppendLine("      floor 30 -> " + e.FloorTo.ToString("F0") + ", renders at " +
                               e.FinalSize.ToString("F0") + " px  " + e.Key + "  (" + e.Note + ")");
        }

        // -----------------------------------------------------------------
        //  CASE D -- the real scan. Newest device logcat on disk, if any.
        //  Builds/ is gitignored, so a fresh clone has nothing to read; that case
        //  takes RegressionOutcome.PartialSkip (see the three-way rule inline below)
        //  rather than being passed off as a check that ran.
        // -----------------------------------------------------------------
        private static void CaseD_ScanNewestDeviceLog(List<string> failures, StringBuilder log)
        {
            string repoRoot = Directory.GetParent(Application.dataPath).FullName;
            string frames = Path.Combine(Path.Combine(repoRoot, "Builds"), "device-frames");
            // THE THREE-WAY RULE (RegressionOutcome / RegressionMarkerRegression RULE 4). A device
            // logcat is HARNESS-CAPABILITY-ABSENT, not fixture-absent: it is produced by a physical
            // Seeker run, `Builds/` is gitignored, and no batchmode machine can manufacture one. So
            // this section takes the PartialSkip channel — the suite still counts green because Cases
            // A-C really asserted, and the reason string carries a machine-readable [PARTIAL-SKIP]
            // naming the hole. A bare guard-and-return here reads to the caller as a PASS, which is
            // the arithmetic bug RegressionOutcome exists to end.
            if (!Directory.Exists(frames))
            {
                log.AppendLine("  " + RegressionOutcome.PartialSkip("[scan-D] device-log scan",
                    "no device logcat reachable: '" + frames + "' does not exist on this machine (Builds/ is " +
                    "gitignored and a logcat comes from a physical device run) — the allowlist was NOT " +
                    "exercised against real data; the ship-side check is the lead's grep on a fresh logcat"));
                return;
            }

            var candidates = Directory.GetFiles(frames, "*logcat*.txt", SearchOption.TopDirectoryOnly);
            if (candidates.Length == 0)
            {
                log.AppendLine("  " + RegressionOutcome.PartialSkip("[scan-D] device-log scan",
                    "'" + frames + "' exists but holds no *logcat*.txt — the allowlist was NOT exercised " +
                    "against real data"));
                return;
            }

            string newest = null;
            DateTime newestAt = DateTime.MinValue;
            foreach (var c in candidates)
            {
                var at = File.GetLastWriteTimeUtc(c);
                if (at > newestAt) { newestAt = at; newest = c; }
            }

            string[] lines;
            try { lines = File.ReadAllLines(newest); }
            catch (Exception ex)
            {
                log.AppendLine("  " + RegressionOutcome.PartialSkip("[scan-D] device-log scan",
                    "could not read '" + newest + "' (" + ex.GetType().Name + ": " + ex.Message +
                    ") — the allowlist was NOT exercised against real data"));
                return;
            }

            var violations = Judge(lines);
            int relaxCount = CountRelaxations(lines);
            string where = Path.GetFileName(newest) + " (" + newestAt.ToString("u", CultureInfo.InvariantCulture) + ")";

            if (violations.Count > 0)
            {
                foreach (var v in violations)
                    failures.Add(Tag + " CASE D on " + where + ": " + v);
                return;
            }

            log.AppendLine("  [scan-D] " + where + ": " + relaxCount + " relaxation line(s), all leashed. " +
                           "A relaxation is a band that cannot seat FontFloor(" +
                           ElarionUiKit.FontFloor.ToString("F0") + ") - the fix is the band, not the list.");
        }

        // -----------------------------------------------------------------
        //  Judgement + parsing.
        // -----------------------------------------------------------------
        private static List<string> Judge(IEnumerable<string> lines)
        {
            var violations = new List<string>();
            foreach (var line in lines)
            {
                if (!TryParseRelax(line, out RelaxHit hit)) continue;

                if (hit.FinalSize < ElarionUiKit.FontHardFloor || hit.FloorTo < ElarionUiKit.FontHardFloor)
                {
                    violations.Add("'" + hit.Key + "' relaxed to floor " + hit.FloorTo.ToString("F0") +
                                   " and renders at " + hit.FinalSize.ToString("F0") + " px - UNDER FontHardFloor(" +
                                   ElarionUiKit.FontHardFloor.ToString("F0") + "). The F8 2026-07-08 ruling made " +
                                   "that size unshippable; a band this thin is a LAYOUT bug, not something to " +
                                   "shrink into.");
                    continue;
                }

                var entry = Find(hit.Key);
                if (entry == null)
                {
                    violations.Add("NEW relaxation, not on the WO-1652 allowlist: '" + hit.Key + "' floor 30 -> " +
                                   hit.FloorTo.ToString("F0") + ", renders at " + hit.FinalSize.ToString("F0") +
                                   " px - i.e. " + (ElarionUiKit.FontFloor - hit.FinalSize).ToString("F0") +
                                   " px under the owner's FontFloor, and nobody would ever have seen it: the " +
                                   "capture PNGs run un-guarded and this Warn lives in the logcat ring. Fix the " +
                                   "band's height, or bring the entry to the owner before adding it to the list.");
                    continue;
                }

                if (hit.FinalSize < entry.FinalSize)
                {
                    violations.Add("'" + hit.Key + "' now renders at " + hit.FinalSize.ToString("F0") +
                                   " px, SMALLER than the " + entry.FinalSize.ToString("F0") +
                                   " px measured on APK 363722. A leashed relaxation is allowed to stay; it is " +
                                   "not allowed to get worse.");
                }
            }
            return violations;
        }

        private static int CountRelaxations(IEnumerable<string> lines)
        {
            int n = 0;
            foreach (var line in lines) if (TryParseRelax(line, out _)) n++;
            return n;
        }

        private static RelaxEntry Find(string key)
        {
            foreach (var e in Allowlist) if (string.Equals(e.Key, key, StringComparison.Ordinal)) return e;
            return null;
        }

        /// <summary>Reads a relaxation Warn in EITHER form: the machine tokens this ticket adds
        /// (relaxKey=/floorTo=/finalSize=), or the legacy prose every log already on disk carries.
        /// Band-grow, stand-down and render-assert lines are deliberately not relaxations and
        /// return false.</summary>
        private static bool TryParseRelax(string line, out RelaxHit hit)
        {
            hit = default(RelaxHit);
            if (string.IsNullOrEmpty(line)) return false;
            if (line.IndexOf("TextFitGuard", StringComparison.Ordinal) < 0) return false;

            // Preferred: explicit tokens.
            if (line.IndexOf("relaxKey=", StringComparison.Ordinal) >= 0)
            {
                // Bound the key by the NEXT token, not by whitespace: a GameObject name may
                // legally contain spaces, and a truncated key reads as a brand-new relaxation.
                string key = TokenUntil(line, "relaxKey=", " floorFrom=");
                if (string.IsNullOrEmpty(key)) return false;
                if (!TryFloat(Token(line, "floorTo="), out float floorTo)) return false;
                if (!TryFloat(Token(line, "finalSize="), out float finalSize)) return false;
                hit.Key = key; hit.FloorTo = floorTo; hit.FinalSize = finalSize; hit.Raw = line;
                return true;
            }

            // Legacy prose: "TextFitGuard '<text>' [<path>]: rect WxH ... -> <floorTo> (... , fontSize now <n>, ..."
            int marker = line.IndexOf("]: rect ", StringComparison.Ordinal);
            if (marker < 0) return false;                               // grow / stand-down / render-assert lines
            int open = line.LastIndexOf(" [", marker, StringComparison.Ordinal);
            if (open < 0) return false;
            string path = line.Substring(open + 2, marker - open - 2);
            if (string.IsNullOrEmpty(path)) return false;

            int arrow = line.IndexOf(" -> ", marker, StringComparison.Ordinal);
            if (arrow < 0) return false;                                // no floor move = not a relaxation
            if (!TryFloat(Upto(line, arrow + 4, " ("), out float legacyFloorTo)) return false;

            int size = line.IndexOf("fontSize now ", marker, StringComparison.Ordinal);
            if (size < 0) return false;
            if (!TryFloat(Upto(line, size + 13, ","), out float legacyFinal)) return false;

            hit.Key = path; hit.FloorTo = legacyFloorTo; hit.FinalSize = legacyFinal; hit.Raw = line;
            return true;
        }

        private static string Token(string line, string name)
        {
            return TokenUntil(line, name, " ");
        }

        private static string TokenUntil(string line, string name, string stop)
        {
            int i = line.IndexOf(name, StringComparison.Ordinal);
            if (i < 0) return null;
            return Upto(line, i + name.Length, stop);
        }

        private static string Upto(string line, int start, string stop)
        {
            if (start < 0 || start >= line.Length) return null;
            int end = line.IndexOf(stop, start, StringComparison.Ordinal);
            if (end < 0) end = line.Length;
            return line.Substring(start, end - start).Trim();
        }

        private static bool TryFloat(string s, out float v)
        {
            v = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
