// =============================================================================
// ArmyScreenCopyRegression [army-screen-copy] - WO-1811.
// Markers: ARMY_SCREEN_COPY_OK / ARMY_SCREEN_COPY_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Wired into DataRegression.RunAll.
//
// ==========================  WHAT IT PROVES  =================================
// Owner, 2026-09-16: "the armies training screen is where im confused, i have no way
// to understand it" / "if i cant understand it noone else will". The screen had three
// number systems on it at once and spoke engine vocabulary for all three. WO-1811
// moved every player-facing sentence into ArmyBoardCopy - pure functions of integers -
// so this suite can measure the COPY ITSELF, for the four army states that matter,
// with no GameState, no queue service and no canvas:
//
//   1. every sentence, in all four states, is free of the BANNED WORDS - "staged",
//      "SHORT OF" and a save-"slot" number. Those were not style problems: "staged"
//      named a state the player has no model for, "SHORT OF: Army room" read as an
//      error while the button underneath still said Train, and the save slot is the
//      preset bank's index, unrelated to army slots or train slots.
//   2. the numbers ADD UP and are actually IN the sentences: an army line of
//      used/cap and a room line of cap-used, so the two lines on the one screen
//      cannot disagree (they are produced from a single read - ArmyMusterVM.Board).
//   3. nothing resolves to "[[missing:" - i.e. the armyScreen.* keys really are in
//      the canonical table, not just in the C# fallback.
//   4. the ratchet: the RETIRED sentences (the ones the owner photographed) are fed
//      to the same banned-word predicate and MUST be caught. An oracle that cannot
//      fail is worse than no oracle (WO-1138).
//
// ⚠ IT DELIBERATELY DOES NOT MEASURE THE PANEL. Geometry is ArmyMusterLayoutRegression's
// job and it already measures the panel's band table on three live surfaces; a second
// opinion there is how two suites start disagreeing about one layout.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class ArmyScreenCopyRegression
    {
        private const string VmSrc = "Assets/_Modules/Village/Troops/ArmyMusterVM.cs";
        private const string PanelSrc = "Assets/_Modules/Village/Troops/ArmyMusterPanel.cs";

        /// <summary>The words the owner could not place. Matched case-insensitively against every
        /// sentence the copy layer produces.</summary>
        private static readonly string[] Banned = { "staged", "short of", "fits now" };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== ArmyScreenCopyRegression: WO-1811 army screen copy ===");

            if (!File.Exists(VmSrc))
            {
                reason = "army-screen-copy FAIL x1: [fixture] MISSING " + VmSrc;
                return false;
            }

            Case(failures, "empty-army", () => CaseState(failures, log, "empty army",
                used: 0, cap: 10, wounded: 0, recoverySeconds: 0d, training: 0, trainingSeconds: 0d,
                queueDepth: 0, queueCap: 5));

            Case(failures, "seven-of-ten-three-wounded", () => CaseState(failures, log, "7/10 with 3 wounded",
                used: 7, cap: 10, wounded: 3, recoverySeconds: 1140d, training: 0, trainingSeconds: 0d,
                queueDepth: 0, queueCap: 5));

            Case(failures, "full-army", () => CaseState(failures, log, "full army",
                used: 10, cap: 10, wounded: 3, recoverySeconds: 600d, training: 0, trainingSeconds: 0d,
                queueDepth: 0, queueCap: 5));

            Case(failures, "queue-busy", () => CaseState(failures, log, "queue busy",
                used: 8, cap: 10, wounded: 0, recoverySeconds: 0d, training: 5, trainingSeconds: 250d,
                queueDepth: 5, queueCap: 5));

            Case(failures, "ratchet", () => CaseRatchet(failures, log));
            Case(failures, "source", () => CaseSource(failures, log));

            if (failures.Count == 0)
            {
                reason = "ARMY SCREEN COPY OK - four army states measured (empty / 7 of 10 with 3 " +
                         "recovering / full / queue busy): no banned wording, the counts add up, every " +
                         "armyScreen.* key resolves from the table, and the retired sentences still " +
                         "measure RED (the oracle can fail).";
                UnityEngine.Debug.Log("ARMY_SCREEN_COPY_OK\n" + log);
                return true;
            }

            UnityEngine.Debug.LogError("ARMY_SCREEN_COPY_FAIL: " + failures.Count + " failure(s)\n" + log);
            reason = "army-screen-copy FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1-4 - one army state, every sentence it produces.
        // =====================================================================
        private static void CaseState(List<string> failures, StringBuilder log, string tag,
            int used, int cap, int wounded, double recoverySeconds,
            int training, double trainingSeconds, int queueDepth, int queueCap)
        {
            int room = cap - used;
            if (room < 0) room = 0;

            var lines = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("armyLine", ArmyBoardCopy.ArmyLine(used, cap)),
                new KeyValuePair<string, string>("recovering", ArmyBoardCopy.RecoveringLine(wounded, recoverySeconds)),
                new KeyValuePair<string, string>("room", ArmyBoardCopy.RoomLine(room, wounded)),
                new KeyValuePair<string, string>("queue", ArmyBoardCopy.QueueLine(training, trainingSeconds)),
                new KeyValuePair<string, string>("queueFull", ArmyBoardCopy.QueueFullLine(queueDepth, queueCap)),
                new KeyValuePair<string, string>("rowMeta", ArmyBoardCopy.RowMeta(ArmyBoardCopy.CompactDuration(45d), used, 2, training)),
                // The REAL gate arithmetic, not a convenient one: the raid door counts DEPLOYABLE
                // slots, so the shortfall on a 7/10 army with 3 recovering is 6 - of which only 3
                // can be trained today. This is fed the true numbers on purpose (see the check
                // below): a sentence that tells the player to train more troops than the army has
                // room for is the two-axis contradiction this ticket exists to remove.
                new KeyValuePair<string, string>("ready",
                    ArmyBoardCopy.ReadyLine(room <= 0 && wounded <= 0, cap - (used - wounded), wounded)),
                new KeyValuePair<string, string>("reserveLine", ArmyBoardCopy.ReserveLine(2)),
                new KeyValuePair<string, string>("manageBody", ArmyBoardCopy.ManageBody(used, 2)),
                new KeyValuePair<string, string>("dismissPaid", ArmyBoardCopy.DismissFace(120)),
                new KeyValuePair<string, string>("dismissFree", ArmyBoardCopy.DismissFace(0)),
            };

            foreach (var line in lines)
            {
                string what = "[" + tag + "/" + line.Key + "]";
                string text = line.Value ?? "";

                string banned = FirstBanned(text);
                if (banned != null)
                    failures.Add(what + " says '" + banned + "': \"" + text + "\" - that is the vocabulary " +
                                 "the owner could not place (WO-1811).");

                if (text.IndexOf("[[missing:", StringComparison.Ordinal) >= 0)
                    failures.Add(what + " did not resolve from the canonical table: \"" + text +
                                 "\" - the armyScreen.* key is absent from Data/Canonical/en.json.");

                if (text.IndexOf("{0}", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("{1}", StringComparison.Ordinal) >= 0)
                    failures.Add(what + " still carries an unformatted placeholder: \"" + text + "\".");
            }

            // THE NUMBERS. The army line and the room line are produced from ONE read, so they must
            // agree arithmetically AND both must actually SAY their number - a sentence that drops
            // its count is how "Army is full" and "Fits now: 5 of 10" ended up on adjacent screens.
            string army = lines[0].Value;
            if (!Contains(army, used) || !Contains(army, cap))
                failures.Add("[" + tag + "] the army line does not carry both numbers (" + used + "/" + cap +
                             "): \"" + army + "\".");

            string roomLine = lines[2].Value;
            if (room > 0 && !Contains(roomLine, room))
                failures.Add("[" + tag + "] the room line does not say how much room there is (" + room +
                             "): \"" + roomLine + "\".");
            if (room <= 0 && wounded > 0 && !Contains(roomLine, wounded))
                failures.Add("[" + tag + "] the army is full and " + wounded + " troop(s) are recovering, " +
                             "but the line does not name them: \"" + roomLine + "\" - the wounded holding " +
                             "the slots is the CAUSE the old screen never showed.");

            if (wounded <= 0 && !string.IsNullOrEmpty(lines[1].Value))
                failures.Add("[" + tag + "] the recovering line printed at ZERO wounded: \"" + lines[1].Value +
                             "\" - WO-1810 made zero wounded the normal case; it must disappear, not read 0.");

            if (training <= 0 && lines[3].Value.IndexOf("0", StringComparison.Ordinal) >= 0)
                failures.Add("[" + tag + "] the queue line counts zero rather than saying nothing is " +
                             "training: \"" + lines[3].Value + "\".");

            // ── WO-1811 owner ruling 2026-09-16: TRAINED vs DEPLOYED must be unmistakable ──
            // "we need to know how many troops are trained or how many are deployed". A row that
            // prints one number for both is the defect, so the row line must name BOTH counts, and
            // the reserve line must never read as part of the army bar's cap number.
            string rowMeta = lines[5].Value;
            if (!Contains(rowMeta, used) || !Contains(rowMeta, 2))
                failures.Add("[" + tag + "] the troop row does not carry BOTH the active (" + used +
                             ") and reserve (2) counts: \"" + rowMeta + "\" - the owner asked to know " +
                             "how many are trained AND how many are deployed.");
            if (training > 0 && !Contains(rowMeta, training))
                failures.Add("[" + tag + "] " + training + " troop(s) are training and the row does not " +
                             "say so: \"" + rowMeta + "\".");

            // ⛔ THE ONE THAT WOULD HAVE SHIPPED THE BUG AGAIN. On the owner's reference fixture
            // (7 of 10, 3 recovering) the naive shortfall is SIX, but the army has room for THREE -
            // the wounded hold the other three slots. A raid-door sentence naming a number the
            // player cannot act on is the same contradiction as "Army is full" beside
            // "Fits now: 5 of 10". So: the count the door asks for is never larger than the room,
            // unless the sentence also names the recovering troops that explain the difference.
            string readyLine = lines[6].Value;
            int asked = FirstNumber(readyLine);
            if (asked > room && !Contains(readyLine, wounded))
                failures.Add("[" + tag + "] the raid door asks for " + asked + " more troop(s) while the " +
                             "army has room for " + room + ", and does not name the " + wounded +
                             " recovering that explain the gap: \"" + readyLine + "\".");

            string reserveLine = lines[7].Value;
            if (!Contains(reserveLine, 2))
                failures.Add("[" + tag + "] the reserve line does not name the reserve count: \"" +
                             reserveLine + "\".");
            if (!string.IsNullOrEmpty(ArmyBoardCopy.ReserveLine(0)))
                failures.Add("[" + tag + "] the reserve line printed at ZERO reserved: \"" +
                             ArmyBoardCopy.ReserveLine(0) + "\" - it must disappear, not read 0.");

            string paid = lines[9].Value, free = lines[10].Value;
            if (!Contains(paid, 120))
                failures.Add("[" + tag + "] the dismiss face does not name the gold it pays: \"" + paid + "\".");
            if (Contains(free, 0))
                failures.Add("[" + tag + "] a dismissal that pays NOTHING still quotes a price: \"" + free +
                             "\" - training charges nothing (WO-1387), so a zero payout must say nothing " +
                             "rather than promise 0 gold.");

            log.AppendLine("[" + tag + "] " + army + " | " + roomLine + " | " + lines[3].Value +
                           (string.IsNullOrEmpty(lines[1].Value) ? "" : " | " + lines[1].Value) +
                           " | " + rowMeta);
        }

        // =====================================================================
        //  CASE 5 - THE RATCHET. The sentences the owner photographed must be
        //  caught by the very predicate that clears the new ones.
        // =====================================================================
        private static void CaseRatchet(List<string> failures, StringBuilder log)
        {
            string[] retired =
            {
                "STAGED: Wall Hold  (slot 1)",
                "Fits now: 5 of 10 (rest stays staged).",
                "SHORT OF: Army room",
                "5 start now - 5 stay staged",
            };

            foreach (string s in retired)
            {
                if (FirstBanned(s) == null)
                    failures.Add("[ratchet] the RETIRED sentence \"" + s + "\" passes the banned-word " +
                                 "predicate - the predicate cannot fail, so its green proves nothing.");
            }
            // The SECOND predicate needs its own red, or it is a check nobody has proved can fail
            // (WO-1138). This is the sentence the naive shortfall would have shipped on the owner's
            // fixture: "Train 6 more to raid" beside "Room for 3 more", with nothing naming the 3
            // recovering troops that explain the difference.
            const string naive = "Train 6 more to raid";
            if (!(FirstNumber(naive) > 3 && !Contains(naive, 3)))
                failures.Add("[ratchet] the naive raid-door sentence \"" + naive + "\" is NOT caught by " +
                             "the ask-exceeds-room predicate - that check cannot fail, so its green " +
                             "proves nothing.");

            log.AppendLine("[ratchet] " + retired.Length + " retired sentences and the naive raid-door " +
                           "sentence still measure RED, as they must.");
        }

        // =====================================================================
        //  CASE 6 - the source pin: the copy layer stays in the VM, and no
        //  player-facing literal in either file reintroduces a banned word.
        // =====================================================================
        private static void CaseSource(List<string> failures, StringBuilder log)
        {
            string vm = File.ReadAllText(VmSrc);
            if (vm.IndexOf("class ArmyBoardCopy", StringComparison.Ordinal) < 0)
                failures.Add("[source] ArmyBoardCopy is gone from " + VmSrc + " - the army screen's copy " +
                             "has moved back out of the model layer, so nothing can measure it (WO-1811).");

            if (!File.Exists(PanelSrc)) return;
            var literals = Literals(File.ReadAllText(PanelSrc));
            foreach (string lit in literals)
            {
                // GameObject / zone identifiers are a diagnostic hierarchy, not rendered copy.
                if (lit.IndexOf("Zone_", StringComparison.Ordinal) >= 0) continue;
                string banned = FirstBanned(lit);
                if (banned != null)
                    failures.Add("[source] a literal in " + PanelSrc + " still says '" + banned + "': \"" + lit + "\"");
            }
            log.AppendLine("[source] ArmyBoardCopy pinned; " + literals.Count + " literals in " +
                           PanelSrc + " scanned, none banned.");
        }

        /// <summary>
        /// Every STRING LITERAL in a C# source, comments excluded - a small scanner rather than a
        /// bare regex, because this file's own comments quote the retired sentences on purpose
        /// (that is what makes the history readable) and a regex over the raw text reports each of
        /// them as a live defect. Verbatim strings are read as ordinary ones: close enough, since a
        /// verbatim player string in this panel would still be caught by its content.
        /// </summary>
        private static List<string> Literals(string src)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(src)) return found;

            var sb = new StringBuilder();
            bool inString = false, inChar = false, inLine = false, inBlock = false, escape = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';

                if (inLine) { if (c == '\n') inLine = false; continue; }
                if (inBlock) { if (c == '*' && next == '/') { inBlock = false; i++; } continue; }

                if (inString)
                {
                    if (escape) { escape = false; sb.Append(c); continue; }
                    if (c == '\\') { escape = true; sb.Append(c); continue; }
                    if (c == '"') { inString = false; found.Add(sb.ToString()); sb.Length = 0; continue; }
                    sb.Append(c);
                    continue;
                }
                if (inChar)
                {
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
                if (c == '"') { inString = true; sb.Length = 0; continue; }
                if (c == '\'') { inChar = true; continue; }
            }
            return found;
        }

        private static string FirstBanned(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string lower = text.ToLowerInvariant();
            foreach (string b in Banned)
                if (lower.Contains(b)) return b;
            // "slot 1" / "slot 3" - the SAVE slot index, meaningless outside the preset bank.
            if (Regex.IsMatch(lower, "slot\\s+\\d")) return "slot <n>";
            return null;
        }

        /// <summary>The first integer a sentence names, or 0 when it names none.</summary>
        private static int FirstNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var m = Regex.Match(text, "\\d+");
            if (!m.Success) return 0;
            int n;
            return int.TryParse(m.Value, out n) ? n : 0;
        }

        private static bool Contains(string text, int number)
        {
            return !string.IsNullOrEmpty(text) &&
                   Regex.IsMatch(text, "(^|\\D)" + number.ToString() + "($|\\D)");
        }
    }
}
