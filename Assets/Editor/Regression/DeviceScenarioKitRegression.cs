// =============================================================================
// DeviceScenarioKitRegression [device-scenario-kit] — WO-1775.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// Pins the GATE, not the runtime behaviour (the behaviour needs a device — see
// WO-1775 §5). Every check here is a source-lint, so it runs headless under
// REGRESSION_OK with no scene, no device, no APK build:
//
//   1 [gate]            DevScenarioIntent.cs compiles ONLY under QA_SCENARIO_BUILD —
//                        the whole file body is wrapped `#if QA_SCENARIO_BUILD ... #endif`.
//   2 [no-reset-verb]   The parser never reads a bare reset/wipe/deleteall extra key —
//                        the only string extras it reads are the named WO-1775 §1.6 keys.
//   3 [save-guard]      `newgame` is refused whenever a save exists (PlayerPrefs.HasKey(
//                        SaveSchema.PlayerPrefsKey)) BEFORE ResetToNewGame() is ever called —
//                        a scripted reset can never destroy an existing save (§1.6.1).
//   4 [asmdef]           DeNelle.DevTools.asmdef's defineConstraints admits QA_SCENARIO_BUILD
//                        (alongside the existing UNITY_EDITOR||DEVELOPMENT_BUILD), or the
//                        whole DevTools assembly — DevScenarioIntent included — compiles out
//                        of a QA_SCENARIO_BUILD APK entirely.
//   5 [ship-quarantine] overnight-apk-build.ps1 only ever stamps QA_SCENARIO_BUILD behind an
//                        explicit opt-in switch (never unconditionally, never merely from
//                        -Tester) — a store/Firebase-tester run must never carry it silently.
//   6 [store-quarantine] QA_SCENARIO_BUILD is not referenced anywhere in the Android build
//                        pipeline (AndroidBuild.cs) or the known ship/distribution scripts —
//                        the define has exactly ONE producer (overnight-apk-build.ps1) and
//                        one consumer (the DevTools asmdef + DevScenarioIntent.cs).
//   7 [filename-tag]    overnight-apk-build.ps1 tags the scenario artifact's filename with
//                        "-scenario" when -Scenario is passed, so it can never be mistaken
//                        for the tester upload at Firebase App Distribution time.
//   8 [hygiene]          No embedded NUL in the touched sources (CLAUDE.md Sec. 0).
//
// EVERY source-lint here reads CODE ONLY where it matters (comment lines dropped, string
// literal CONTENTS blanked) — same CodeText/StripStringLiterals idiom as
// EchoWorldPresenceRegression / SpirePlansCelebrationRegression, so a comment or FlowTrace
// message that merely NAMES a token (e.g. this very file's header) can never satisfy or trip
// a rule that is actually about a CALL or a LITERAL EXTRA KEY.
//
// Markers: DEVICE_SCENARIO_KIT_OK / DEVICE_SCENARIO_KIT_FAIL.
// Standalone: run-unity-method -Method DeNelle.Editor.Regression.DeviceScenarioKitRegression.RunAll
// Registered in DataRegression.RunAll as the "device-scenario-kit suite".
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class DeviceScenarioKitRegression
    {
        private const string IntentSrc  = "Assets/_Modules/DevTools/DevScenarioIntent.cs";
        private const string AsmdefSrc  = "Assets/_Modules/DevTools/DeNelle.DevTools.asmdef";
        private const string ApkBuildPs = "overnight-apk-build.ps1";
        private const string AndroidBuildSrc = "Assets/Editor/AndroidBuild.cs";

        // Known ship/distribution scripts this define must never reach. Read-only membership
        // list, not a re-derivation of the ship chain — WO-1741 / CLAUDE.md sec.16 already own
        // that chain; this just asserts none of its scripts NAME the QA define.
        private static readonly string[] ShipChainScripts =
        {
            "morning-ship-chain.ps1",
            "distribute-android.ps1",
            "install-apk-to-seeker.ps1",
            "tools/r2-ship.ps1",
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("DEVICE_SCENARIO_KIT_OK - " + reason);
            else Debug.LogError("DEVICE_SCENARIO_KIT_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                CheckIntentFileGate(failures);
                CheckNoResetVerb(failures);
                CheckSaveGuardOrdering(failures);
                CheckAsmdefConstraint(failures);
                CheckShipQuarantine(failures);
                CheckStoreQuarantine(failures);
                CheckFilenameTag(failures);
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
            reason = "WO-1775 device-scenario kit gate holds: DevScenarioIntent compiles only under " +
                     "QA_SCENARIO_BUILD; the parser exposes no bare reset/wipe verb and newgame is " +
                     "fail-closed on an existing save BEFORE ResetToNewGame() is ever called; the DevTools " +
                     "asmdef admits the define; overnight-apk-build.ps1 stamps it only behind an explicit " +
                     "opt-in and tags the artifact filename '-scenario'; no store/Play/Firebase-tester build " +
                     "script or AndroidBuild.cs names the define; no NULs.";
            return true;
        }

        // -- 1 [gate] -----------------------------------------------------
        private static void CheckIntentFileGate(List<string> failures)
        {
            string raw = ReadSrc(IntentSrc, failures);
            if (raw == null) return;
            string trimmed = raw.Replace("\r\n", "\n").TrimStart();
            // Skip the leading banner comment block to find the first real statement.
            int i = 0;
            var lines = trimmed.Split('\n');
            int firstCode = -1;
            for (; i < lines.Length; i++)
            {
                string l = lines[i].TrimStart();
                if (l.Length == 0 || l.StartsWith("//", StringComparison.Ordinal)) continue;
                firstCode = i;
                break;
            }
            if (firstCode < 0 || !lines[firstCode].TrimStart().StartsWith("#if QA_SCENARIO_BUILD", StringComparison.Ordinal))
                failures.Add("[gate] " + IntentSrc + " no longer opens with '#if QA_SCENARIO_BUILD' as its " +
                             "first real line — the file would compile into a build that never asked for it");

            string lastNonEmpty = null;
            for (int j = lines.Length - 1; j >= 0; j--)
            {
                if (lines[j].Trim().Length == 0) continue;
                lastNonEmpty = lines[j].Trim();
                break;
            }
            if (lastNonEmpty != "#endif")
                failures.Add("[gate] " + IntentSrc + " does not end on a bare '#endif' — the whole-file " +
                             "wrap is no longer provably closing the file");
        }

        // -- 2 [no-reset-verb] ---------------------------------------------
        // Deliberately reads comments-stripped-but-STRING-LITERALS-KEPT text: the thing being
        // hunted for IS a string literal (an extra key name), and CodeText's blank-the-quotes
        // pass would hide exactly that. A prose mention in a comment (this file's own header
        // included) is excluded by the comment strip; a literal "dotr.reset" key is not.
        private static void CheckNoResetVerb(List<string> failures)
        {
            string raw = ReadSrc(IntentSrc, failures);
            if (raw == null) return;
            string codeKeepStrings = StripCommentsKeepStrings(raw);
            string[] forbidden = { "dotr.reset", "dotr.wipe", "dotr.deleteall", "dotr.clear", "dotr.erase" };
            foreach (var token in forbidden)
                if (codeKeepStrings.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[no-reset-verb] " + IntentSrc + " names the extra key '" + token +
                                 "' in code — this kit must expose NO bare reset/wipe verb (WO-1775 §1.6.1)");
        }

        // -- 3 [save-guard] -------------------------------------------------
        private static void CheckSaveGuardOrdering(List<string> failures)
        {
            string code = ReadCode(IntentSrc, failures);
            if (code == null) return;
            int guardIdx = code.IndexOf("PlayerPrefs.HasKey(SaveSchema.PlayerPrefsKey)", StringComparison.Ordinal);
            if (guardIdx < 0)
            {
                failures.Add("[save-guard] " + IntentSrc + " no longer checks " +
                             "PlayerPrefs.HasKey(SaveSchema.PlayerPrefsKey) — 'newgame' would no longer be " +
                             "fail-closed against an existing save");
                return;
            }
            int resetIdx = code.IndexOf(".ResetToNewGame()", StringComparison.Ordinal);
            if (resetIdx < 0)
                failures.Add("[save-guard] " + IntentSrc + " no longer calls ResetToNewGame() at all — " +
                             "'newgame' would be dead");
            else if (resetIdx < guardIdx)
                failures.Add("[save-guard] ResetToNewGame() is called BEFORE the dotr-save guard in " +
                             IntentSrc + " — the check must run first or a save-bearing target can be reset");
        }

        // -- 4 [asmdef] ------------------------------------------------------
        private static void CheckAsmdefConstraint(List<string> failures)
        {
            string raw = ReadSrc(AsmdefSrc, failures);
            if (raw == null) return;
            if (raw.IndexOf("QA_SCENARIO_BUILD", StringComparison.Ordinal) < 0)
                failures.Add("[asmdef] " + AsmdefSrc + " defineConstraints no longer admit QA_SCENARIO_BUILD " +
                             "— the whole DeNelle.DevTools assembly (DevScenarioIntent included) would compile " +
                             "out of a QA_SCENARIO_BUILD-only APK");
        }

        // -- 5 [ship-quarantine] ----------------------------------------------
        // Reads PowerShell-COMMENT-STRIPPED text (this file's own '#'-led prose about
        // QA_SCENARIO_BUILD — including the header this WO adds to overnight-apk-build.ps1
        // itself — would otherwise be the FIRST match and make the 'is it behind an if-guard'
        // window land in a paragraph of prose instead of real code).
        private static void CheckShipQuarantine(List<string> failures)
        {
            string raw = ReadSrc(ApkBuildPs, failures);
            if (raw == null) return;
            string code = StripPowerShellComments(raw);
            int defineIdx = code.IndexOf("QA_SCENARIO_BUILD", StringComparison.Ordinal);
            if (defineIdx < 0)
            {
                failures.Add("[ship-quarantine] " + ApkBuildPs + " no longer stamps QA_SCENARIO_BUILD in " +
                             "actual code (comments don't count) — the -Scenario switch this WO adds would " +
                             "not exist");
                return;
            }
            // The define must sit behind a param/switch check, not be unconditional. Look at a
            // window of CODE text BEFORE the first mention for an 'if' guard naming a switch.
            int windowStart = Math.Max(0, defineIdx - 400);
            string window = code.Substring(windowStart, defineIdx - windowStart);
            if (window.IndexOf("if (", StringComparison.Ordinal) < 0 &&
                window.IndexOf("if(", StringComparison.Ordinal) < 0)
                failures.Add("[ship-quarantine] QA_SCENARIO_BUILD in " + ApkBuildPs + " does not appear " +
                             "behind an 'if' guard within 400 chars of code before it — it must be strictly " +
                             "opt-in, never stamped unconditionally or merely by -Tester");
        }

        // -- 6 [store-quarantine] ---------------------------------------------
        private static void CheckStoreQuarantine(List<string> failures)
        {
            string code = ReadCode(AndroidBuildSrc, failures);
            if (code != null && code.IndexOf("QA_SCENARIO_BUILD", StringComparison.Ordinal) >= 0)
                failures.Add("[store-quarantine] " + AndroidBuildSrc + " references QA_SCENARIO_BUILD — the " +
                             "Android build pipeline itself must never bake this in; only the opt-in " +
                             "overnight-apk-build.ps1 -Scenario switch may");

            foreach (var script in ShipChainScripts)
            {
                if (!File.Exists(script)) continue; // named by path, not required to exist on every checkout
                string raw;
                try { raw = File.ReadAllText(script); }
                catch (Exception ex) { failures.Add("[store-quarantine] " + script + ": " + ex.Message); continue; }
                if (raw.IndexOf("QA_SCENARIO_BUILD", StringComparison.Ordinal) >= 0)
                    failures.Add("[store-quarantine] " + script + " references QA_SCENARIO_BUILD — a " +
                                 "store/Play/Firebase-tester distribution script must never carry the define");
            }
        }

        // -- 7 [filename-tag] --------------------------------------------------
        private static void CheckFilenameTag(List<string> failures)
        {
            string raw = ReadSrc(ApkBuildPs, failures);
            if (raw == null) return;
            if (raw.IndexOf("QA_SCENARIO_BUILD", StringComparison.Ordinal) < 0) return; // #5 already failed this
            if (raw.IndexOf("-scenario", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("[filename-tag] " + ApkBuildPs + " stamps QA_SCENARIO_BUILD but never tags an " +
                             "artifact filename with '-scenario' — a QA APK could be mistaken for the tester " +
                             "upload at Firebase App Distribution time");
        }

        // -- 8 [hygiene] ---------------------------------------------------
        private static void CheckHygiene(List<string> failures)
        {
            foreach (var path in new[] { IntentSrc, AsmdefSrc })
            {
                try
                {
                    if (!File.Exists(path)) { failures.Add("[hygiene] missing " + path); continue; }
                    var bytes = File.ReadAllBytes(path);
                    for (int i = 0; i < bytes.Length; i++)
                        if (bytes[i] == 0) { failures.Add("[hygiene] embedded NUL in " + path); break; }
                }
                catch (Exception ex) { failures.Add("[hygiene] " + path + ": " + ex.Message); }
            }
        }

        // -- helpers --------------------------------------------------------
        private static string ReadSrc(string path, List<string> failures)
        {
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path);
                failures.Add("[src] missing " + path);
            }
            catch (Exception ex) { failures.Add("[src] " + path + ": " + ex.Message); }
            return null;
        }

        /// <summary>ReadSrc reduced to CODE ONLY (comment lines dropped, string literal
        /// CONTENTS blanked) — same idiom as EchoWorldPresenceRegression.CodeText, kept
        /// file-local per this folder's convention.</summary>
        private static string ReadCode(string path, List<string> failures)
        {
            string raw = ReadSrc(path, failures);
            return raw == null ? null : CodeText(raw);
        }

        private static string CodeText(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sb = new StringBuilder(source.Length);
            foreach (var rawLine in source.Split('\n'))
            {
                string t = rawLine.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) ||
                    t.StartsWith("*",  StringComparison.Ordinal) ||
                    t.StartsWith("/*", StringComparison.Ordinal)) { sb.Append('\n'); continue; }
                sb.Append(StripStringLiterals(rawLine)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>PowerShell variant of CodeText: drops whole '#'-led comment lines and a
        /// trailing '# ...' comment, honouring quotes so a '#' inside a string is never
        /// mistaken for a comment opener. Used only for the .ps1 checks in this suite.</summary>
        private static string StripPowerShellComments(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sb = new StringBuilder(source.Length);
            foreach (var rawLine in source.Split('\n'))
            {
                string t = rawLine.TrimStart();
                if (t.StartsWith("#", StringComparison.Ordinal)) { sb.Append('\n'); continue; }
                bool inSingle = false, inDouble = false;
                int cut = rawLine.Length;
                for (int i = 0; i < rawLine.Length; i++)
                {
                    char c = rawLine[i];
                    if (!inDouble && c == '\'') inSingle = !inSingle;
                    else if (!inSingle && c == '"') inDouble = !inDouble;
                    else if (!inSingle && !inDouble && c == '#') { cut = i; break; }
                }
                sb.Append(rawLine.Substring(0, cut)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Drops whole-comment lines and trailing '//' comments but KEEPS string
        /// literal contents intact — the inverse trade-off from CodeText, used only where a
        /// rule is hunting for a literal (an extra key name), not a call shape.</summary>
        private static string StripCommentsKeepStrings(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sb = new StringBuilder(source.Length);
            foreach (var rawLine in source.Split('\n'))
            {
                string t = rawLine.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) ||
                    t.StartsWith("*",  StringComparison.Ordinal) ||
                    t.StartsWith("/*", StringComparison.Ordinal)) { sb.Append('\n'); continue; }
                // Drop a trailing // comment that follows code, honouring string literals so a
                // '//' inside a quoted key is never mistaken for a comment opener.
                bool inStr = false;
                int cut = rawLine.Length;
                for (int i = 0; i < rawLine.Length; i++)
                {
                    char c = rawLine[i];
                    if (!inStr && c == '/' && i + 1 < rawLine.Length && rawLine[i + 1] == '/') { cut = i; break; }
                    if (c == '"' && (i == 0 || rawLine[i - 1] != '\\')) inStr = !inStr;
                }
                sb.Append(rawLine.Substring(0, cut)).Append('\n');
            }
            return sb.ToString();
        }

        private static string StripStringLiterals(string line)
        {
            var sb = new StringBuilder(line.Length);
            bool inStr = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inStr && c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
                if (c == '"' && (i == 0 || line[i - 1] != '\\')) { inStr = !inStr; sb.Append(c); continue; }
                sb.Append(inStr ? ' ' : c);
            }
            return sb.ToString();
        }
    }
}
