// =============================================================================
// ScreenOrientationRegression [screen-orientation]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
// Markers: LANDSCAPE_ONLY_OK / LANDSCAPE_ONLY_FAIL.
//
// WO-1631. OWNER RULING 2026-09-10 (morning, via AskUserQuestion): **the game is
// LANDSCAPE ONLY.** Portrait is not a supported presentation of this game, on any
// device, at any screen.
//
// THE MEASURED DEFECT (read at source 2026-09-10, and it is a DATA state, not code):
//   ProjectSettings/ProjectSettings.asset:11  defaultScreenOrientation: 4
//   ProjectSettings/ProjectSettings.asset:63  allowedAutorotateToPortrait: 1
//   ProjectSettings/ProjectSettings.asset:64  allowedAutorotateToPortraitUpsideDown: 1
//   ProjectSettings/ProjectSettings.asset:65  allowedAutorotateToLandscapeRight: 1
//   ProjectSettings/ProjectSettings.asset:66  allowedAutorotateToLandscapeLeft: 1
//
// `4` is UnityEditor.UIOrientation.AutoRotation. That mapping was not recalled - it
// was READ, by reflecting the enum out of the editor install this project pins
// (ProjectSettings/ProjectVersion.txt: 6000.4.8f1):
//   ...\Unity\Hub\Editor\6000.4.8f1\Editor\Data\Managed\UnityEngine\UnityEditor.CoreModule.dll
//   -> Portrait=0, PortraitUpsideDown=1, LandscapeRight=2, LandscapeLeft=3, AutoRotation=4
// (its summary in the sibling UnityEditor.CoreModule.xml:60864-60867 reads
// "Default mobile device orientation").
//
// THE CONSEQUENCE, MEASURED, NOT INFERRED. All NINE device frames captured off the
// Seeker overnight decode as 1200x2670 - PORTRAIT - from their PNG IHDR:
// Builds/device-frames/2026-09-10_0028_title_363195.png, _0031_after_continue_,
// _0033_manage_, and the six 02:02-02:10 DEVICE-PROFILE frames. Two of them were
// opened and read this session: the Title row prints `PLAY INT...` (ellipsised) and
// the Manage screen prints its `MANAGE` title struck through by the QUEUE button
// with `UPGRADE ...` cut beneath it. The screens are authored for a landscape box;
// the device handed them a portrait one.
//
// WHY AN ORACLE AT ALL, for five integers:
//   * ProjectSettings.asset is REWRITTEN WHOLESALE by the editor. Anyone who opens
//     Player Settings -> Resolution and Presentation and ticks an orientation box,
//     and any tooling that round-trips PlayerSettings, silently restores the portrait
//     flags - and the regression is INVISIBLE to every existing gate. Nothing throws,
//     no marker goes red, the build installs and plays; the only detector left is the
//     owner's eyes on a device frame, which is precisely the thing CLAUDE.md sec.14
//     exists so we never rely on. That is the sec.16 signature: silent, green, wrong.
//   * It is decidable from TEXT - no play mode, no device, no build - so it can be
//     trusted to run in the same batch it guards. Shape and method are taken from
//     StackTraceLogTypeRegression, the neighbour that guards the same file.
//
//   CASE 1 [fields-present]  Each of the five orientation keys appears EXACTLY ONCE
//     in the asset and parses as an integer. Zero occurrences, a duplicate, or an
//     unparseable value is a hard FAIL and never a quiet pass: a scan that found
//     nothing to assert must not read as an assertion that passed (WO-1138, and the
//     neighbour's CASE 1 says the same thing about its own key).
//
//   CASE 2 [no-portrait-autorotate]  allowedAutorotateToPortrait AND
//     allowedAutorotateToPortraitUpsideDown are both 0. This is the case the ticket
//     exists for, and it is the half that actually rotated the owner's device.
//
//   CASE 3 [default-forbids-portrait]  defaultScreenOrientation is one of
//     LandscapeRight(2), LandscapeLeft(3) or AutoRotation(4). Portrait(0) and
//     PortraitUpsideDown(1) name a portrait presentation directly and are a FAIL
//     whatever the autorotate flags say - a fixed portrait default is exactly the
//     ruled-out state, reached by a different field.
//
//   CASE 4 [landscape-still-reachable]  The over-correction guard. "No portrait"
//     is satisfiable by setting ALL FOUR flags to 0 while the default stays
//     AutoRotation(4) - a build that is allowed to rotate to nothing at all. So when
//     the default is AutoRotation, at least one of the two landscape flags must be 1,
//     and the RECOMMENDED state is BOTH (a phone held either way up still shows the
//     game the right way round); one landscape flag alone passes with a loud note,
//     because it is legal but it means half the players hold the device upside down.
//     An oracle that only checks the direction of a change cannot see the
//     over-correction - the neighbour's CASE 3 was written for the same reason.
//
//   CASE 5 [no-build-script-portrait-writer]  ADDED 2026-09-10, AFTER THE ASSET WAS
//     FIXED AND A BUILD PUT IT BACK. No `.cs` under Assets/Editor may assign anything
//     but `false` to PlayerSettings.allowedAutorotateToPortrait or
//     ...PortraitUpsideDown. THE MEASURED DEFECT: the ruling commit 29296e086 set the
//     asset's portrait flags to 0; the 05:45 build chain of 2026-09-10 (Windows
//     release, then the Seeker APK, then WebGL) left them back at 1, and the APK
//     2026.09.10.363529 that reached the Seeker was built with portrait autorotate ON.
//     The writer was AndroidBuild.cs:327-331 (as it stood at commit a96bfe332; the same
//     block now reads `false` at :359-363) - `ApplyAndroidPlayerSettings()`, called from
//     the APK path at AndroidBuild.cs:120, which set both portrait flags TRUE via the
//     PlayerSettings API, and a PlayerSettings write is persisted straight back into the
//     asset CASE 2 reads. A grep for `allowedAutorotate` / `UIOrientation` /
//     `defaultInterfaceOrientation` over every `.cs` and `.ps1` in the tree on 2026-09-10
//     returned AndroidBuild.cs and these two regression files and NOTHING ELSE -
//     DesktopBuild.cs and the WebGL build write no orientation at all, so the APK step is
//     the only step in the chain that contains an orientation write. (Not a writer, and
//     do not go looking at it: Assets/_Modules/Core/Validation/OrientationGuard.cs is the
//     WO-363 CHARACTER-facing gate, nothing to do with the screen.)
//     Why CASE 2 alone was not enough: CASE 2 reads a FILE, and a build rewrites that
//     file AFTER the gate has run. An oracle that only guards the data cannot see a
//     writer that runs later, so the ratchet has to guard the SOURCE too.
//     The rule is "must be false", not "must not be true", so `= someFlag` and
//     `=true` (no spaces) are both caught rather than dodged.
//     ⚠ IT WILL ALSO RED ON A STRING LITERAL that pins `= true` - a source-scanning
//     oracle REQUIRING the portrait writer is a portrait writer by proxy, and there was
//     exactly such a pin: GooglePlayPackagingRegression.cs:49,:51 (WO-1255, 2026-09-08)
//     required the literal text `PlayerSettings.allowedAutorotateToPortrait = true` in
//     AndroidBuild.cs, so honouring the ruling turned THAT suite red. Both were fixed in
//     the same change. That behaviour is deliberate and must not be narrowed.
//
// ⛔ WHAT THIS SUITE DOES NOT CLAIM.
//   * It does not claim WebGL is affected or unaffected by these fields. What was
//     measured is narrower and is all that is asserted anywhere: there is no
//     `screenOrientation` attribute in any Android manifest under Assets/, and no
//     `Screen.orientation` / `ScreenOrientation.` write anywhere under
//     Assets/_Modules or Assets/Editor (grepped 2026-09-10, zero hits) - so nothing
//     in this tree overrides the asset at runtime.
//     ⚠ ADDENDUM 2026-09-10 (same day, later): that paragraph was true and it was
//     still not enough. It scanned for RUNTIME overrides and found none - and missed
//     the BUILD-TIME writer at AndroidBuild.cs:327-331, which does not override the
//     asset at runtime, it REWRITES the asset. "Nothing overrides it" was the wrong
//     question. CASE 5 asks the right one.
//     Unity's own summary calls the
//     field "Default mobile device orientation"; the browser owns the WebGL canvas.
//     That is the reasoning, and it is written here as reasoning, not as a proof.
//   * CASE 5 lints the two PORTRAIT AUTOROTATE FLAGS and nothing else. A build script
//     assigning `defaultInterfaceOrientation = UIOrientation.Portrait` would have the
//     SAME rewrite-after-the-gate shape, and only CASE 3 would see it - after the fact,
//     on the asset. No such assignment exists in the tree today (grepped 2026-09-10);
//     the scope was deliberately not widened past the measured defect, and this bullet
//     is where the next seat should start if the default field ever moves.
//   * It does not assert WHICH of the two legal landscape shapes the project should
//     use. Both pass. The choice is the owner's ruling, recorded in WO-1631 sec.4.
//   * It does not look at a single screen, layout, caption or font. A screen that is
//     ugly in landscape is a different ticket; this one is only about whether the
//     player can ever be shown a portrait box at all.
//
// Standalone: run-unity-method
//   -Method DeNelle.Editor.Regression.ScreenOrientationRegression.RunAll
//
// ✔ RESOLVED 2026-09-10: the registration landed - DataRegression.cs:1998 runs this
// suite. The note below is kept as the record of why it was written that way; it no
// longer describes the tree, and RULE 2 no longer names this class.
//
// NOTE FOR THE LEAD: this file is NOT registered in DataRegression.RunAll - the lane
// that wrote it is forbidden from touching that file. Until the registration line is
// added, RegressionMarkerRegression RULE 2 [registration] will name this class
// ("exposes Run(out string) but is NOT referenced in DataRegression.RunAll"). That is
// EXPECTED and it is the correct behaviour of that ratchet. RULE 2's opt-out token
// (the one RepairProbeRegression declares - see RegressionMarkerRegression's
// StandaloneOptOutTokens, and note that its check is a plain IndexOf over the WHOLE
// file, so the token is deliberately NOT spelled here) must NOT be added: this suite
// belongs in the gate, and writing that token anywhere in this file - even in a
// sentence saying it is not wanted - would silently opt the suite out.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class ScreenOrientationRegression
    {
        private const string SettingsRel = "ProjectSettings/ProjectSettings.asset";

        // The YAML keys, split so that this file's own text can never be mistaken for
        // the asset under test by any other source-scanning oracle (the neighbour
        // StackTraceLogTypeRegression splits its own key for the same reason).
        private const string KeyDefault   = "defaultScreen" + "Orientation";
        private const string KeyPortrait  = "allowedAutorotateTo" + "Portrait";
        private const string KeyPortraitU = "allowedAutorotateTo" + "PortraitUpsideDown";
        private const string KeyLandRight = "allowedAutorotateTo" + "LandscapeRight";
        private const string KeyLandLeft  = "allowedAutorotateTo" + "LandscapeLeft";

        // UnityEditor.UIOrientation, reflected out of
        // Unity/Hub/Editor/6000.4.8f1/Editor/Data/Managed/UnityEngine/UnityEditor.CoreModule.dll
        // on 2026-09-10. Never re-derive these from memory - CLAUDE.md sec.11B.
        private const int OrientationPortrait           = 0;
        private const int OrientationPortraitUpsideDown = 1;
        private const int OrientationLandscapeRight     = 2;
        private const int OrientationLandscapeLeft      = 3;
        private const int OrientationAutoRotation       = 4;

        private const int Off = 0;
        private const int On  = 1;

        // CASE 5. Every build entry point this project owns lives under Assets/Editor, and
        // AndroidBuild.cs (the writer that produced the 2026-09-10 defect) is one of them.
        private const string EditorScanRoot = "Assets/Editor";
        private const string SelfFileName   = "ScreenOrientationRegression.cs";

        public static void RunAll()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log("LANDSCAPE_ONLY_OK\n" + reason);
            else    Debug.LogError("LANDSCAPE_ONLY_FAIL\n" + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes    = new List<string>();
            var values   = new Dictionary<string, int>();
            var lineNos  = new Dictionary<string, int>();

            DeNelle.Core.Diagnostics.Guard.Try("Regression", "screen-orientation case 1",
                () => Case1_FieldsPresentAndWellFormed(values, lineNos, failures, notes));
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "screen-orientation case 2",
                () => Case2_PortraitAutorotateIsOff(values, lineNos, failures, notes));
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "screen-orientation case 3",
                () => Case3_DefaultOrientationForbidsPortrait(values, lineNos, failures, notes));
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "screen-orientation case 4",
                () => Case4_LandscapeStillReachable(values, lineNos, failures, notes));
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "screen-orientation case 5",
                () => Case5_NoBuildScriptWritesPortrait(failures, notes));

            if (failures.Count == 0)
            {
                reason = string.Join("; ", notes);
                return true;
            }

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" failure(s):");
            foreach (string f in failures) sb.Append("\n  - ").Append(f);
            if (notes.Count > 0) sb.Append("\n  (context: ").Append(string.Join("; ", notes)).Append(')');
            reason = sb.ToString();
            return false;
        }

        // =====================================================================
        //  CASE 1 - the five keys exist, once each, and parse as integers
        // =====================================================================
        private static void Case1_FieldsPresentAndWellFormed(Dictionary<string, int> values,
                                                            Dictionary<string, int> lineNos,
                                                            List<string> failures, List<string> notes)
        {
            string full = FullPath(SettingsRel);
            if (!File.Exists(full))
            {
                failures.Add("missing file: " + SettingsRel + " - the scan has nothing to read, which is a " +
                             "broken oracle, not a clean tree.");
                return;
            }

            string[] lines;
            try { lines = File.ReadAllLines(full); }
            catch (Exception e)
            {
                failures.Add("could not read " + SettingsRel + ": " + e.GetType().Name + ": " + e.Message);
                return;
            }

            string[] keys = { KeyDefault, KeyPortrait, KeyPortraitU, KeyLandRight, KeyLandLeft };

            foreach (string key in keys)
            {
                var hits = new List<int>();
                string raw = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (!trimmed.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                    hits.Add(i + 1);
                    if (raw == null) raw = trimmed.Substring(key.Length + 1).Trim();
                }

                if (hits.Count == 0)
                {
                    failures.Add("no '" + key + ":' line in " + SettingsRel + ". Either the key was renamed by a " +
                                 "Unity upgrade or the file moved; in both cases this suite is now asserting " +
                                 "nothing while reporting a pass. Fix the scan, do not relax it.");
                    continue;
                }

                if (hits.Count > 1)
                {
                    failures.Add(SettingsRel + " carries '" + key + ":' " + hits.Count + " times (lines " +
                                 string.Join(",", hits.ConvertAll(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()) +
                                 "). This scan reads the FIRST one, so a second occurrence could silently be the " +
                                 "value the editor actually honours. Resolve the duplicate before trusting any " +
                                 "case below.");
                    continue;
                }

                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    failures.Add(SettingsRel + ":" + hits[0] + " - '" + key + "' is '" + raw + "', which is not an " +
                                 "integer. The serialized shape changed; re-derive it before trusting cases 2-4.");
                    continue;
                }

                values[key]  = parsed;
                lineNos[key] = hits[0];
            }

            if (values.Count == keys.Length)
            {
                notes.Add("[case1] " + SettingsRel + " " +
                          KeyDefault + "=" + values[KeyDefault] + " (:" + lineNos[KeyDefault] + "), " +
                          KeyPortrait + "=" + values[KeyPortrait] + " (:" + lineNos[KeyPortrait] + "), " +
                          KeyPortraitU + "=" + values[KeyPortraitU] + " (:" + lineNos[KeyPortraitU] + "), " +
                          KeyLandRight + "=" + values[KeyLandRight] + " (:" + lineNos[KeyLandRight] + "), " +
                          KeyLandLeft + "=" + values[KeyLandLeft] + " (:" + lineNos[KeyLandLeft] + ")");
            }
        }

        // =====================================================================
        //  CASE 2 - neither portrait autorotate flag may be on
        // =====================================================================
        private static void Case2_PortraitAutorotateIsOff(Dictionary<string, int> values,
                                                          Dictionary<string, int> lineNos,
                                                          List<string> failures, List<string> notes)
        {
            if (!values.ContainsKey(KeyPortrait) || !values.ContainsKey(KeyPortraitU)) return;   // CASE 1 failed loudly

            int before = failures.Count;

            CheckIs(values, lineNos, KeyPortrait, Off,
                    "the device rotates the game into a portrait box. This is the flag that produced all nine " +
                    "1200x2670 device frames of 2026-09-10 and every truncation in them",
                    failures);
            CheckIs(values, lineNos, KeyPortraitU, Off,
                    "the device rotates the game into an upside-down portrait box - the same unsupported " +
                    "presentation as above, reached by turning the phone the other way",
                    failures);

            if (failures.Count == before)
                notes.Add("[case2] both portrait autorotate flags are 0 - the device cannot rotate the game into portrait");
        }

        // =====================================================================
        //  CASE 3 - the DEFAULT must not itself name a portrait presentation
        // =====================================================================
        private static void Case3_DefaultOrientationForbidsPortrait(Dictionary<string, int> values,
                                                                    Dictionary<string, int> lineNos,
                                                                    List<string> failures, List<string> notes)
        {
            if (!values.ContainsKey(KeyDefault)) return;   // CASE 1 failed loudly

            int actual = values[KeyDefault];

            if (actual == OrientationPortrait || actual == OrientationPortraitUpsideDown)
            {
                failures.Add(SettingsRel + ":" + lineNos[KeyDefault] + " - " + KeyDefault + " is " + actual +
                             " (UIOrientation." + Name(actual) + "), which fixes the game in PORTRAIT. The owner " +
                             "ruled 2026-09-10 that the game is landscape only. Legal values are " +
                             OrientationLandscapeRight + " (LandscapeRight), " + OrientationLandscapeLeft +
                             " (LandscapeLeft) or " + OrientationAutoRotation + " (AutoRotation with the two " +
                             "portrait flags off). Note that the autorotate flags cannot rescue this: a fixed " +
                             "portrait default does not consult them.");
                return;
            }

            if (actual != OrientationLandscapeRight && actual != OrientationLandscapeLeft &&
                actual != OrientationAutoRotation)
            {
                failures.Add(SettingsRel + ":" + lineNos[KeyDefault] + " - " + KeyDefault + " is " + actual +
                             ", which is not a value of UnityEditor.UIOrientation as read from the 6000.4.8f1 " +
                             "editor install (0..4). The enum changed or the field was corrupted; re-derive it " +
                             "before relaxing this case.");
                return;
            }

            notes.Add("[case3] " + KeyDefault + "=" + actual + " (UIOrientation." + Name(actual) +
                      ") - the default does not name a portrait presentation");
        }

        // =====================================================================
        //  CASE 4 - the over-correction guard: landscape must still be reachable
        // =====================================================================
        private static void Case4_LandscapeStillReachable(Dictionary<string, int> values,
                                                          Dictionary<string, int> lineNos,
                                                          List<string> failures, List<string> notes)
        {
            if (!values.ContainsKey(KeyDefault) || !values.ContainsKey(KeyLandRight) ||
                !values.ContainsKey(KeyLandLeft)) return;   // CASE 1 failed loudly

            if (values[KeyDefault] != OrientationAutoRotation)
            {
                notes.Add("[case4] " + KeyDefault + "=" + values[KeyDefault] + " is a FIXED landscape value, so the " +
                          "autorotate flags do not decide the presentation - nothing to over-correct");
                return;
            }

            bool right = values[KeyLandRight] == On;
            bool left  = values[KeyLandLeft]  == On;

            if (!right && !left)
            {
                failures.Add(SettingsRel + ":" + lineNos[KeyLandRight] + " and :" + lineNos[KeyLandLeft] + " - " +
                             KeyDefault + " is AutoRotation(" + OrientationAutoRotation + ") while BOTH landscape " +
                             "flags are 0. With the portrait flags also off that is a build allowed to rotate to " +
                             "NOTHING, which is not what 'landscape only' means. This is the over-correction: " +
                             "'no portrait' is satisfiable by turning every flag off, and it would pass case 2 " +
                             "while shipping an orientation-less build. Turn " + KeyLandRight + " and " +
                             KeyLandLeft + " back to 1.");
                return;
            }

            if (right && left)
            {
                notes.Add("[case4] AutoRotation with BOTH landscape flags on - the recommended state: the player " +
                          "may hold the device either way up and the game is the right way round");
                return;
            }

            notes.Add("[case4] WARNING - AutoRotation with only " + (right ? KeyLandRight : KeyLandLeft) +
                      " on. Legal and it passes, but it means a player holding the device the other way sees the " +
                      "game upside down and the OS will not correct it. Both landscape flags on is the recommended " +
                      "state (WO-1631 sec.4).");
        }

        // =====================================================================
        //  CASE 5 - no editor build script may assign anything but false to a
        //           portrait autorotate flag (SOURCE lint - see the header)
        // =====================================================================
        private static void Case5_NoBuildScriptWritesPortrait(List<string> failures, List<string> notes)
        {
            string root = FullPath(EditorScanRoot);
            if (!Directory.Exists(root))
            {
                failures.Add("missing directory: " + EditorScanRoot + " - the source lint has nothing to read, " +
                             "which is a broken oracle, not a clean tree (same rule as CASE 1).");
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories); }
            catch (Exception e)
            {
                failures.Add("could not enumerate " + EditorScanRoot + ": " + e.GetType().Name + ": " + e.Message);
                return;
            }

            // PlayerSettings.allowedAutorotateToPortrait[UpsideDown] = <rest of the line>
            // Assembled from parts so this file's own text is never a needle for any other
            // source-scanning oracle - the same reason the YAML keys above are split.
            // The capture deliberately stops at the end of the LINE, not at a ';': half the
            // matches this lint must judge live inside STRING LITERALS in a sibling oracle
            // (see the header), where there is no semicolon at all and the statement ends at
            // a closing quote. The leading identifier of the capture is what is judged, so
            // `= false;`, `= false",` and `=false;` all read as 'false', while `= true`,
            // `= someFlag` and `= !locked` do not - and a right-hand side that is not a
            // literal is a failure in its own right, because an orientation that depends on
            // a variable cannot be decided by reading the repo (CLAUDE.md sec.11B).
            const string pattern = "PlayerSettings" + @"\s*\.\s*" + "allowedAutorotateTo" +
                                   @"Portrait(?:UpsideDown)?\s*=\s*([^;\r\n]*)";
            var rx = new Regex(pattern, RegexOptions.CultureInvariant);

            int scanned = 0;
            int assignments = 0;
            int before = failures.Count;

            foreach (string file in files)
            {
                // Skip this suite's own file: it names the API in prose and in this pattern,
                // and an oracle that fails on its own description asserts nothing.
                if (string.Equals(Path.GetFileName(file), SelfFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string text;
                try { text = File.ReadAllText(file); }
                catch (Exception e)
                {
                    failures.Add("could not read " + Rel(file) + ": " + e.GetType().Name + ": " + e.Message +
                                 " - a file the lint cannot read is a file the lint is not guarding.");
                    continue;
                }

                scanned++;
                if (text.IndexOf("allowedAutorotateTo" + "Portrait", StringComparison.Ordinal) < 0) continue;

                foreach (Match m in rx.Matches(text))
                {
                    assignments++;
                    string rhs = LeadingToken(m.Groups[1].Value);
                    if (string.Equals(rhs, "false", StringComparison.Ordinal)) continue;

                    failures.Add(Rel(file) + ":" + LineOf(text, m.Index) + " - assigns '" + rhs + "' to a portrait " +
                                 "autorotate flag. The owner ruled 2026-09-10 that the game is LANDSCAPE ONLY " +
                                 "(WO-1631); the only value this project may assign here is 'false'. A build " +
                                 "script that sets it TRUE re-enables portrait no matter what " + SettingsRel +
                                 " says, because a PlayerSettings write is persisted back into that file AFTER " +
                                 "this gate has read it - which is exactly how the APK 2026.09.10.363529 reached " +
                                 "the Seeker in portrait. A non-literal right-hand side is a failure too: an " +
                                 "orientation that depends on a variable cannot be decided by reading the repo. " +
                                 "If this is a string literal in another oracle PINNING the writer, that oracle " +
                                 "is a portrait writer by proxy - fix it, do not narrow this scan.");
                }
            }

            if (failures.Count == before)
            {
                notes.Add("[case5] " + scanned + " .cs file(s) under " + EditorScanRoot + " scanned, " +
                          assignments + " portrait-autorotate assignment(s) found, every one of them 'false' - " +
                          "no build script can put the portrait flags back");
            }
        }

        // =====================================================================
        //  helpers
        // =====================================================================
        /// <summary>
        /// The leading C# identifier/keyword of a right-hand side, or "" when it does not
        /// start with one (`!locked`, `(a ? b : c)`). "" is never "false", so those FAIL -
        /// which is the intent: only a bare `false` literal is a decidable orientation.
        /// </summary>
        private static string LeadingToken(string rhs)
        {
            string trimmed = rhs.TrimStart();
            int i = 0;
            while (i < trimmed.Length && (char.IsLetterOrDigit(trimmed[i]) || trimmed[i] == '_')) i++;
            return trimmed.Substring(0, i);
        }

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private static string Rel(string full)
        {
            string cwd = Directory.GetCurrentDirectory();
            string rel = full.StartsWith(cwd, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(cwd.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : full;
            return rel.Replace('\\', '/');
        }

        private static void CheckIs(Dictionary<string, int> values, Dictionary<string, int> lineNos,
                                    string key, int expected, string consequence, List<string> failures)
        {
            int actual = values[key];
            if (actual == expected) return;

            int lineNo = lineNos.ContainsKey(key) ? lineNos[key] : -1;
            failures.Add(SettingsRel + ":" + lineNo + " - " + key + " is " + actual + ", expected " + expected +
                         ". Consequence: " + consequence + ". Fix it in " + SettingsRel + " - and if the editor " +
                         "rewrote this file (opening Player Settings is enough), that is the regression this " +
                         "suite exists to catch, not a reason to change the expectation.");
        }

        private static string Name(int orientation)
        {
            if (orientation == OrientationPortrait)           return "Portrait";
            if (orientation == OrientationPortraitUpsideDown) return "PortraitUpsideDown";
            if (orientation == OrientationLandscapeRight)     return "LandscapeRight";
            if (orientation == OrientationLandscapeLeft)      return "LandscapeLeft";
            if (orientation == OrientationAutoRotation)       return "AutoRotation";
            return "unknown";
        }

        private static string FullPath(string rel) =>
            Path.Combine(Directory.GetCurrentDirectory(), rel.Replace('/', Path.DirectorySeparatorChar));
    }
}
