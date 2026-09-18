// WO-1413: source-level guard for truthful player copy and capture fixtures.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class CopyHygieneRegression
    {
        private const string ModulesRoot = "Assets/_Modules";
        private const string Launcher = "Assets/Editor/UICaptureLaunch.cs";
        private const string CombatHud = "Assets/_Modules/HUD/Kit/HudKitController.cs";
        private const string HelpVm = "Assets/_Modules/HUD/HelpMenuVM.cs";
        private const string Pause = "Assets/_Modules/Settings/PauseController.cs";
        private const string Settings = "Assets/_Modules/Settings/SettingsController.cs";
        private const string EchoVm = "Assets/_Modules/Village/Harvest/EchoWorkforceVM.cs";
        private const string DailyChest = "Assets/_Modules/Village/Monetization/DailyChestController.cs";
        private const string DialogueResources = "Assets/Resources/Data/Canonical/dialogue/dialogues.json";
        private const string DialogueStreaming = "Assets/StreamingAssets/Data/Canonical/dialogue/dialogues.json";
        // WO-1588: the dungeon prompt surfaces. DungeonBaker is in the set ON PURPOSE - the em
        // dash the owner photographed lived in the BAKER, not in any runtime file, so an oracle
        // that scans only Assets/_Modules would have watched it walk straight past.
        private const string DungeonsRoot = "Assets/_Modules/Dungeons";
        private const string DungeonBaker = "Assets/Editor/RoomForge/DungeonBaker.cs";
        private const string LockedPort = "Assets/_Modules/Dungeons/ComposedLockedPort.cs";

        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log(report);
            else Debug.LogError(report);
        }

        public static bool Run(out string report)
        {
            var failures = new List<string>();
            try
            {
                // RED recipe: restore "Repair structures" in BuildOptionProbeDef.
                CaseCaptureFixtures(failures);

                // RED recipe: restore "SKILL I" as the first adaptive skill caption.
                CaseCombatFaces(failures);

                // RED recipe: add "Repair structures" to only one canonical dialogue twin.
                CaseDialogueTwins(failures);

                // RED recipe: change HelpMenuVM's reset face back to "Reset Hero & Pet".
                CaseRetiredCopy(failures);

                // RED recipe: move the Dev Tools candidate below its compile guard.
                CaseDevToolsReleaseGuard(failures);

                // RED recipe: restore the Settings _musicToggle field beside _musicSlider.
                CasePartOneSurfaces(failures);

                // RED recipe: relabel the shared Pause chrome Close as Resume.
                CasePauseExemption(failures);

                // RED recipe: put "Locked <em dash> need key" back into DungeonBaker's Configure call,
                // or point ComposedLockedPort.Update back at the serialized _promptLocked.
                CaseDungeonPromptCopy(failures);
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            report = failures.Count == 0
                ? "COPY_HYGIENE_OK: fixtures use live verbs and distinct chain parts; combat faces name equipped skills or EMPTY; part-one copy remains truthful; Pause exemption preserved; dungeon prompts are dash-free and have one producer"
                : "COPY_HYGIENE_FAIL: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }

        private static void CaseCaptureFixtures(List<string> failures)
        {
            string code = Read(Launcher);
            if (code.Contains("Repair structures"))
                failures.Add("[dialogue-fixture] retired Repair structures option remains");
            if (!code.Contains("\"Gather resources\""))
                failures.Add("[dialogue-fixture] Gather resources replacement is missing");
            if (!code.Contains("\"Standing Watch Over the Western Fields - Part \" + i + \" of 2\""))
                failures.Add("[rumor-fixture] two-part watch chain is not distinguished in words");
        }

        private static void CaseCombatFaces(List<string> failures)
        {
            string code = Read(CombatHud);
            if (code.Contains("\"SKILL I\"") || code.Contains("\"SKILL II\"") || code.Contains("\"SKILL III\""))
                failures.Add("[combat-face] numbered placeholder face remains");
            if (Count(code, "BuildCombatDockSlot(") < 6 || Count(code, "BuildCombatDockSlot(2, \"EMPTY\"") != 1 ||
                Count(code, "BuildCombatDockSlot(3, \"EMPTY\"") != 1 || Count(code, "BuildCombatDockSlot(4, \"EMPTY\"") != 1)
                failures.Add("[combat-face] adaptive skill faces do not seed EMPTY exactly once each");
            if (!code.Contains("h.SetCaption(equipped && !string.IsNullOrWhiteSpace(s.Name) ? s.Name : \"EMPTY\")"))
                failures.Add("[combat-face] live assignable face is not sourced from equipped AbilitySlotRecord.Name");
        }

        private static void CaseDialogueTwins(List<string> failures)
        {
            byte[] a = File.ReadAllBytes(DialogueResources);
            byte[] b = File.ReadAllBytes(DialogueStreaming);
            if (!SameBytes(a, b)) failures.Add("[dialogue-twins] canonical dialogue copies differ byte-for-byte");
            string text = System.Text.Encoding.UTF8.GetString(a);
            if (text.Contains("Repair structures"))
                failures.Add("[dialogue-content] retired fixture phrase leaked into real dialogue data");
        }

        private static void CaseRetiredCopy(List<string> failures)
        {
            foreach (string path in Directory.GetFiles(ModulesRoot, "*.cs", SearchOption.AllDirectories))
            {
                string code = Read(path);
                // Lead correction at gate (2026-09-05): the case-insensitive "& PET" scan matched ordinary C#
                // ("&& pet.Id == id", "&& petUnits.Count") in three files and went RED on code, not copy - a
                // hollow trap. The retired COPY is "Hero & Pet" / "RESET HERO & PET": match case-sensitively
                // and never when the ampersand is half of "&&". RED recipe: put "Reset Hero & Pet" back in HelpMenu.
                if (ContainsRetiredPetPhrase(code))
                    failures.Add("[retired-pet] " + path + " contains the retired '& Pet' copy");
                if (code.IndexOf("to every node's yield", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[retired-yield] " + path + " contains multiplier copy");
            }
        }

        private static void CaseDevToolsReleaseGuard(List<string> failures)
        {
            string code = Read(HelpVm);
            int guard = code.IndexOf("#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD", StringComparison.Ordinal);
            int face = code.IndexOf("\"Dev Tools\"", StringComparison.Ordinal);
            int end = face >= 0 ? code.IndexOf("#endif", face, StringComparison.Ordinal) : -1;
            if (guard < 0 || face < guard || end < face)
                failures.Add("[dev-tools] player Help candidate is not compile-stripped from release");
        }

        private static void CasePartOneSurfaces(List<string> failures)
        {
            string settings = Read(Settings);
            if (settings.Contains("_musicToggle") || settings.Contains("OnMusicOnOffChanged"))
                failures.Add("[settings] Music toggle remains beside the slider");

            string echo = Read(EchoVm);
            if (!echo.Contains("\"Echoes \" + owned + \"/\" + max + \" - harvest +\"") ||
                !echo.Contains("HarvestTogetherBonusPct"))
                failures.Add("[echo] workforce subtitle is not an additive calculator-backed readout");

            string chest = Read(DailyChest);
            int hidden = chest.IndexOf("ad CTA hidden until rewarded placement is ready", StringComparison.Ordinal);
            int build = hidden >= 0 ? chest.IndexOf("BuildObsidianButton", hidden, StringComparison.Ordinal) : -1;
            int earlyReturn = hidden >= 0 ? chest.IndexOf("return;", hidden, StringComparison.Ordinal) : -1;
            if (hidden < 0 || earlyReturn < hidden || (build >= 0 && earlyReturn > build))
                failures.Add("[daily-chest] unavailable ad row is not hidden before button construction");
        }

        private static void CasePauseExemption(List<string> failures)
        {
            // BATCH_STATE 8.9 supersedes the WO's one-verb acceptance for tonight: keep both the
            // primary Resume and kit-owned shared Close until the owner chooses which face retires.
            string code = Read(Pause);
            // WO-1857: the primary face's caption moved from a bare literal into a LocalizedText
            // resolve, and the call now wraps across two lines, so the needle pins the resolved KEY
            // rather than the one-line "helper(host, caption" shape. ApplyButton(resume, primary:
            // true) still proves it is THE primary. The negative clause keeps the same intent - the
            // kit-owned shared Close must never be relabelled with the primary's own caption - now
            // expressed against the key. The retired English word is deliberately not reproduced in
            // this comment: it would satisfy the positive needle and leave this case green while
            // asserting nothing.
            if (!code.Contains("new LocalizedText(\"settings.pause.resume\").Resolve()") ||
                !code.Contains("MedievalUiSkin.ApplyButton(resume, primary: true)") ||
                code.Contains("closeLabel.text = new LocalizedText(\"settings.pause.resume\")"))
                failures.Add("[pause-exemption] approved primary Resume plus untouched shared Close shape changed");
        }

        // =====================================================================================
        // WO-1588 - DUNGEON PROMPT COPY: no dash characters in a player-facing literal, and
        // exactly ONE producer for the locked port's prompt.
        // -------------------------------------------------------------------------------------
        // The owner's frame (F8 seq 4699) read "Locked <em dash> need key" while the only string
        // in ComposedLockedPort.cs used an ASCII hyphen. The producer was DungeonBaker, which
        // passed its own literal into Configure at BAKE time; Unity serialized it, so the retired
        // dash WO-1333 removed from player copy is sitting in every baked dg_*.unity.
        //
        // THREE assertions, because any one alone lets it come back:
        //   1. no em/en dash in a literal handed to MobileInteractButton.Request - the ONE call
        //      that puts words on the prompt bar;
        //   2. no em/en dash in a literal DungeonBaker passes into a `new object[]` Configure
        //      invoke - copy authored there is SERIALIZED, and no runtime fix can reach it;
        //   3. ComposedLockedPort owns its prompt consts and Update shows them, not the field.
        //
        // WHY NOT "no dash anywhere under Assets/_Modules/Dungeons": measured 2026-09-07, that
        // scan returns 174 hits across 62 files and every one of them is a FlowTrace/Guard
        // diagnostic or a [Tooltip] - prose, not player copy. An oracle that goes red on 174
        // correct lines is a hollow trap; it gets suppressed, and then it catches nothing. Pin
        // the seams where player words are actually AUTHORED instead.
        // =====================================================================================
        private const string PromptCall = "MobileInteractButton.Request(";
        private const string BakeConfigCall = "new object[]";

        private static void CaseDungeonPromptCopy(List<string> failures)
        {
            foreach (string path in Directory.GetFiles(DungeonsRoot, "*.cs", SearchOption.AllDirectories))
                foreach (string literal in LiteralsNear(Read(path), PromptCall, 200))
                    if (HasDash(literal))
                        failures.Add("[dungeon-copy] " + path + " shows the player prompt \"" + literal +
                                     "\" - WO-1333 retired em/en dashes from player copy (WO-1588)");

            if (!File.Exists(DungeonBaker))
            {
                failures.Add("[dungeon-copy] missing bake surface " + DungeonBaker);
                return;
            }
            string baker = Read(DungeonBaker);
            foreach (string literal in LiteralsInsideInitializer(baker, BakeConfigCall))
                if (HasDash(literal))
                    failures.Add("[dungeon-copy] DungeonBaker configures a component with the literal \"" + literal +
                                 "\" at BAKE time. Unity SERIALIZES it into every dg_*.unity, so a dash authored " +
                                 "here outlives any runtime fix - which is exactly how the em dash the owner " +
                                 "photographed survived a file that only ever held a hyphen (WO-1588)");

            string port = Read(LockedPort);
            if (!port.Contains("public const string PromptLocked = \"Locked - need key\"") ||
                !port.Contains("public const string PromptOpen = \"Unlock & pass\""))
                failures.Add("[dungeon-copy] ComposedLockedPort no longer owns its prompt consts - the locked-port copy has no single producer, which is the WO-1588 defect exactly");

            int update = port.IndexOf("private void Update()", StringComparison.Ordinal);
            int tryPort = update >= 0 ? port.IndexOf("private void TryPort(", update, StringComparison.Ordinal) : -1;
            string updateBody = (update >= 0 && tryPort > update) ? port.Substring(update, tryPort - update) : null;
            if (updateBody == null)
                failures.Add("[dungeon-copy] could not read ComposedLockedPort.Update - the prompt producer cannot be pinned");
            else if (updateBody.Contains("_promptLocked") || updateBody.Contains("_promptOpen"))
                failures.Add("[dungeon-copy] ComposedLockedPort.Update shows the SERIALIZED prompt field again. Every baked dg_*.unity still carries the retired em-dash string in that field; the consts are what reaches the screen");

            // LITERALS only, not a file-wide Contains: the baker's comment explains the retired
            // string by quoting it, and an oracle that went red on its own documentation would be
            // teaching the next seat to delete the explanation.
            foreach (var pair in StringLiterals(baker))
            {
                string literal = pair.Value;
                if (literal.IndexOf("need key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    literal.IndexOf("Unlock & pass", StringComparison.Ordinal) >= 0)
                    failures.Add("[dungeon-copy] DungeonBaker authors the locked-port prompt again (\"" + literal +
                                 "\") - the baker is the second producer WO-1588 removed, and anything it writes is " +
                                 "serialized into the scene, beyond the reach of any runtime fix");
            }
        }

        /// <summary>
        /// Every double-quoted string literal in a C# source, with line comments, block comments
        /// and char literals skipped. Deliberately NOT a regex: the dungeon files carry em dashes
        /// in their PROSE headers, which are correct there, and a scan that could not tell copy
        /// from comment would go red on documentation.
        /// </summary>
        private static IEnumerable<KeyValuePair<int, string>> StringLiterals(string code)
        {
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
                {
                    while (i < code.Length && code[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/')) i++;
                    i++;
                }
                else if (c == '\'')
                {
                    i++;
                    while (i < code.Length && code[i] != '\'') { if (code[i] == '\\') i++; i++; }
                }
                else if (c == '"')
                {
                    bool verbatim = i > 0 && code[i - 1] == '@';
                    int start = ++i;
                    while (i < code.Length)
                    {
                        if (!verbatim && code[i] == '\\') { i += 2; continue; }
                        if (code[i] == '"')
                        {
                            if (verbatim && i + 1 < code.Length && code[i + 1] == '"') { i += 2; continue; }
                            break;
                        }
                        if (!verbatim && code[i] == '\n') break;   // unterminated; bail out safely
                        i++;
                    }
                    if (i <= code.Length && i > start)
                        yield return new KeyValuePair<int, string>(
                            start, code.Substring(start, Math.Min(i, code.Length) - start));
                }
            }
        }

        /// <summary>
        /// The string literals that begin within <paramref name="window"/> characters after each
        /// occurrence of <paramref name="token"/> - i.e. the literals passed AS ARGUMENTS to that
        /// call. This is what separates a player prompt from the FlowTrace line beside it.
        /// <para>
        /// KNOWN SLACK: this is a character WINDOW, not the argument list. A FlowTrace carrying a
        /// dash written immediately after a Request( call would trip it. If that ever happens the
        /// fix is to tighten this to the call's own parenthesis depth - NOT to delete the case,
        /// which is the whole reason WO-1588's em dash lived on screen for a day.
        /// </para>
        /// </summary>
        private static IEnumerable<string> LiteralsNear(string code, string token, int window)
        {
            var spans = new List<int>();
            for (int at = 0; (at = code.IndexOf(token, at, StringComparison.Ordinal)) >= 0; at += token.Length)
                spans.Add(at);
            if (spans.Count == 0) yield break;

            foreach (var pair in StringLiterals(code))
                foreach (int at in spans)
                    if (pair.Key >= at && pair.Key <= at + window) { yield return pair.Value; break; }
        }

        /// <summary>
        /// The literals sitting INSIDE each `new object[] { ... }` initializer - the argument
        /// basket a reflection Invoke hands to Configure at bake time. Brace-scoped, not
        /// window-scoped: the FlowTrace line two statements later is diagnostics, and going red
        /// on it would be a hollow trap of exactly the kind this suite exists to avoid.
        /// </summary>
        private static IEnumerable<string> LiteralsInsideInitializer(string code, string token)
        {
            for (int at = 0; (at = code.IndexOf(token, at, StringComparison.Ordinal)) >= 0; at += token.Length)
            {
                // Brace chars written as codes, not as '{' / '}' literals: CLAUDE.md sec.1's
                // brace-balance gate counts every brace in the file including the ones inside
                // char literals, and a guard that fails the project's own quality check is not
                // a guard anyone will keep.
                int open = code.IndexOf(BraceOpen, at);
                if (open < 0) yield break;
                int depth = 0, end = -1;
                for (int i = open; i < code.Length; i++)
                {
                    if (code[i] == BraceOpen) depth++;
                    else if (code[i] == BraceClose)
                    {
                        depth--;
                        if (depth == 0) { end = i; break; }
                    }
                }
                if (end < 0) yield break;
                foreach (var pair in StringLiterals(code.Substring(open, end - open + 1)))
                    yield return pair.Value;
            }
        }

        /// <summary>True when the text carries an em dash or an en dash (WO-1333 retired both).</summary>
        // Written as escapes, not as the characters themselves: this file must stay ASCII, and a
        // literal em dash sitting in the guard that bans em dashes is the kind of joke that dies
        // the first time someone re-saves the file in the wrong encoding.
        private const char EmDash = (char)8212;    // U+2014
        private const char EnDash = (char)8211;    // U+2013
        private const char BraceOpen = (char)123;  // {
        private const char BraceClose = (char)125; // }

        private static bool HasDash(string text) =>
            text.IndexOf(EmDash) >= 0 || text.IndexOf(EnDash) >= 0;

        private static string Read(string path) => File.ReadAllText(path);

        /// <summary>True when the source carries the retired "& Pet" / "& PET" COPY - case-sensitive, and the
        /// ampersand must not be half of a C# "&&" (which is how the first version matched "&& pet.Id").</summary>
        private static bool ContainsRetiredPetPhrase(string code)
        {
            foreach (string needle in new[] { "& Pet", "& PET" })
            {
                int at = 0;
                while ((at = code.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
                {
                    bool doubledAmp = at > 0 && code[at - 1] == '&';
                    int end = at + needle.Length;
                    bool wordEnds = end >= code.Length || !char.IsLetterOrDigit(code[end]);
                    if (!doubledAmp && wordEnds) return true;
                    at = end;
                }
            }
            return false;
        }

        private static int Count(string text, string token)
        {
            int count = 0;
            for (int at = 0; (at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0; at += token.Length)
                count++;
            return count;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
