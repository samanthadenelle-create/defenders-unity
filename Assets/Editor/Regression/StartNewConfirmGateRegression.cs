// =============================================================================
// StartNewConfirmGateRegression [startnew-confirm-gate]   — WO-1688 RED pin #1
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
//
// WHAT IT PINS, in the incident's own terms (WO-1688 §1): on 2026-09-10 one
// unintended touch on the title row's MIDDLE face destroyed the owner's realm.
// `TitleController.OnStartNew` called `GameStateService.ResetToNewGame()`
// DIRECTLY from the button callback, guarded only by the `_splashActive`
// double-press latch. The rule this suite makes permanent:
//
//   ⛔ ResetToNewGame IS UNREACHABLE FROM THE TITLE WITHOUT AN EXPLICIT CONFIRM,
//      and a genuine fresh install still goes straight through.
//
// WHY IT IS A SOURCE SWEEP AND NOT A LIVE DRIVE — the honest trade, stated
// plainly, because a reader is owed the limit of what this proves:
//   1. DeNelle.EditorRegression does NOT reference DeNelle.Onboarding (read the
//      asmdef), so TitleController is not a compile-time type here.
//   2. Reflecting to it and invoking the real handler would, IF THE FIX EVER
//      REGRESSED, call ResetToNewGame against the DEVELOPER'S OWN editor
//      PlayerPrefs — the gate would wipe the save of whoever ran it. That is a
//      worse defect than the one being pinned. ResetToNewGameFullClearRegression
//      refuses a live drive for exactly this reason and says so in its header;
//      this suite follows the established trade rather than inventing a new one.
//   3. The VISUAL half is covered elsewhere and deliberately not duplicated
//      here: UICaptureLaunch builds this confirm once per front-door capture and
//      runs its two faces through the touch + glyph oracles.
// So: this suite proves the WIRING SHAPE from source. It cannot prove a pixel.
//
// ⚠ RED-FIRST STATUS: at HEAD before WO-1688, Case 1 fails on
// `TitleController.cs:417` (`GameStateService.Instance?.ResetToNewGame();`
// inside `OnStartNew`). That failure was NOT executed in the authoring lane (no
// Unity there) — it is a static fail-condition, and the lead's gate run is what
// converts it from a claim into a fact.
//
// Cases:
//   1 [no-direct-reset]   OnStartNew's body contains no ResetToNewGame call.
//   2 [single-reset-site] The file has exactly ONE ResetToNewGame call and it
//                         lives in PerformStartNew (the shared "what start new
//                         MEANS" body), not in a button callback.
//   3 [confirm-idiom]     OnStartNew raises ElarionUiKit.BuildConfirmModal (the
//                         repo's own idiom, per TutorialSkipUi) and no bespoke
//                         popup chrome is hand-rolled beside it.
//   4 [fresh-install]     The frictionless path is gated on the EXISTING
//                         HasExistingSave() predicate, with no second notion of
//                         "has a save" introduced beside it.
//   5 [latch-scope]       The _splashActive double-press guard survives, and it
//                         is no longer dropped unconditionally at the top — a
//                         drop there would leave every title face dead after a
//                         declined confirm (a softlock traded for the save loss).
//   6 [copy-names-loss]   The four copy constants exist, the body names the loss
//                         and its permanence, and the destructive face carries
//                         ButtonKind.Danger.
//   7 [cancel-is-safe]    The cancel handler reaches neither PerformStartNew nor
//                         ResetToNewGame. The kit wires Close AND the scrim to
//                         onCancel, so this is what makes every accidental
//                         gesture the safe one.
//
// Markers: STARTNEW_CONFIRM_GATE_OK / STARTNEW_CONFIRM_GATE_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.StartNewConfirmGateRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1688 RED pin #1 — the title's START NEW cannot reach the save wipe
    /// without an explicit confirm, and a fresh install still goes straight through.</summary>
    public static class StartNewConfirmGateRegression
    {
        private const string TitleSrc = "Assets/_Modules/Onboarding/TitleController.cs";

        /// <summary>Standalone entry point (its own marker).</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("STARTNEW_CONFIRM_GATE_OK - " + reason);
            else Debug.LogError("STARTNEW_CONFIRM_GATE_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                string raw = ReadSource(TitleSrc, failures);
                if (raw != null)
                {
                    // ⛔ COMMENTS **AND** STRING LITERALS. Stripping comments alone was not
                    // enough and the suite proved it on itself (chain 41, 2026-09-10): this
                    // oracle reported the WO-1688 defect against the FIXED file, because
                    // OnStartNew's own FlowTrace lines say the words
                    //   "...ResetToNewGame is unreachable from here until..."
                    //   "...proceeding to ResetToNewGame."
                    // and a naked Contains cannot tell an identifier from prose ABOUT that
                    // identifier. An oracle that fires on its subject's documentation is a
                    // false RED, and a false RED costs exactly what a missed one does: the
                    // next reader stops believing the marker. Every CODE-SHAPE assertion
                    // below therefore runs on literal-stripped text; Case 6 alone reads the
                    // raw file, because the copy IS the string.
                    string src = StripLiterals(StripComments(raw));
                    string onStartNew = ExtractMethodBody(src, "private void OnStartNew()", "OnStartNew", failures);
                    string perform = ExtractMethodBody(src, "private void PerformStartNew()", "PerformStartNew", failures);

                    if (onStartNew != null)
                    {
                        Case(failures, "no-direct-reset", () => Case1_NoDirectReset(onStartNew, failures));
                        Case(failures, "confirm-idiom", () => Case3_ConfirmIdiom(onStartNew, src, failures));
                        Case(failures, "fresh-install", () => Case4_FreshInstall(onStartNew, failures));
                        Case(failures, "latch-scope", () => Case5_LatchScope(onStartNew, failures));
                        Case(failures, "cancel-is-safe", () => Case7_CancelIsSafe(onStartNew, failures));
                    }
                    Case(failures, "single-reset-site", () => Case2_SingleResetSite(src, perform, failures));
                    Case(failures, "copy-names-loss", () => Case6_CopyNamesLoss(raw, src, onStartNew, failures));
                }
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "STARTNEW CONFIRM GATE OK - OnStartNew cannot reach ResetToNewGame without the " +
                         "ElarionUiKit confirm; the single reset call site lives in PerformStartNew; the " +
                         "fresh-install path is gated on the EXISTING HasExistingSave() predicate and stays " +
                         "frictionless; the _splashActive latch survives without being dropped on the mere " +
                         "press; the copy names the loss and its permanence with the destructive face on " +
                         "Danger; and cancel/close/scrim reach nothing destructive";
                return true;
            }
            reason = "startnew-confirm-gate FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ── CASE 1 — the defect itself ────────────────────────────────────────
        private static void Case1_NoDirectReset(string onStartNew, List<string> failures)
        {
            if (onStartNew.Contains("ResetToNewGame"))
                failures.Add("[no-direct-reset] OnStartNew's body still reaches ResetToNewGame. THIS IS THE " +
                             "2026-09-10 DEFECT VERBATIM: one touch on the title row's middle face wiped a " +
                             "real player's realm before the hero carousel was even shown. The destructive " +
                             "call belongs behind the confirm, in PerformStartNew.");
        }

        // ── CASE 2 — exactly one destructive call site, in the shared body ────
        private static void Case2_SingleResetSite(string src, string perform, List<string> failures)
        {
            int sites = Regex.Matches(src, @"ResetToNewGame\s*\(").Count;
            if (sites != 1)
            {
                failures.Add("[single-reset-site] expected exactly ONE ResetToNewGame call in " + TitleSrc +
                             " but found " + sites + ". More than one means a second door onto the wipe, " +
                             "which is the shape this ticket exists to close; zero means the reset was " +
                             "removed rather than gated.");
                return;
            }
            if (perform == null)
            {
                failures.Add("[single-reset-site] PerformStartNew() was not found. The confirm's positive " +
                             "action and the fresh-install path must call ONE shared body - otherwise a " +
                             "declined confirm can still leave half a new game behind (OnboardingMode " +
                             "flipped to the fast path, dialogue state wiped).");
                return;
            }
            if (!perform.Contains("ResetToNewGame"))
                failures.Add("[single-reset-site] the one ResetToNewGame call is NOT inside PerformStartNew, " +
                             "so it sits somewhere a button callback can still reach directly.");
            if (!perform.Contains("ChooseFastPath") || !perform.Contains("GoHeroSelect"))
                failures.Add("[single-reset-site] PerformStartNew must carry the WHOLE meaning of Start New " +
                             "(reset + dialogue reset + OnboardingMode.ChooseFastPath + SceneRouter." +
                             "GoHeroSelect). Leaving any of it in OnStartNew means a CANCELLED press still " +
                             "changes state.");
        }

        // ── CASE 3 — the repo's own confirm idiom, not a new one ─────────────
        private static void Case3_ConfirmIdiom(string onStartNew, string src, List<string> failures)
        {
            if (!onStartNew.Contains("BuildConfirmModal"))
                failures.Add("[confirm-idiom] OnStartNew does not raise ElarionUiKit.BuildConfirmModal. " +
                             "WO-1688 §2.1 names the existing idiom (TutorialSkipUi wrapping the kit's " +
                             "ConfirmModal) precisely so a wipe prompt is not a bespoke one-off.");
            if (!onStartNew.Contains("PerformStartNew"))
                failures.Add("[confirm-idiom] OnStartNew never calls PerformStartNew - the confirm's " +
                             "positive action has nothing to invoke.");
            // ⚠ SCOPED TO OnStartNew, AND THE FIRST VERSION WAS NOT — it asked the WHOLE FILE
            // whether it mentions BuildModalCanvas, and the answer is legitimately YES: the
            // title builds its OWN root canvas with it (TitleController.cs:190,
            // `_canvas = ElarionUiKit.BuildModalCanvas("TitleScreenUI", 100);`). That is a kit
            // primitive doing its job, not bespoke popup chrome, so the check was failing
            // correct code. The real assertion is narrow: the CONFIRM PATH must not roll its
            // own canvas or scrim.
            foreach (var handRolled in new[] { "BuildModalCanvas", "Scrim(" })
                if (onStartNew.Contains(handRolled))
                    failures.Add("[confirm-idiom] the confirm path hand-rolls modal chrome ('" + handRolled +
                                 "'). The kit owns the sheet; a second chrome is a second set of rules for " +
                                 "the most destructive button in the game.");
        }

        // ── CASE 4 — the fresh install stays frictionless ────────────────────
        private static void Case4_FreshInstall(string onStartNew, List<string> failures)
        {
            if (!onStartNew.Contains("HasExistingSave()"))
            {
                failures.Add("[fresh-install] OnStartNew does not consult HasExistingSave(). That predicate " +
                             "already gates Continue and is already traced at build time; WO-1688 §2.1 says " +
                             "REUSE it rather than invent a second notion of 'has a save'.");
                return;
            }
            foreach (var banned in new[] { "PlayerPrefs.", "Provider.Exists", ".HasKey(" })
                if (onStartNew.Contains(banned))
                    failures.Add("[fresh-install] OnStartNew introduces a SECOND has-a-save test ('" + banned +
                                 "'). Two predicates drift, and the day they disagree the confirm either " +
                                 "stops appearing for a real save or starts appearing on a fresh install.");
        }

        // ── CASE 5 — the latch survives, with the right scope ────────────────
        private static void Case5_LatchScope(string onStartNew, List<string> failures)
        {
            if (!onStartNew.Contains("if (!_splashActive) return;"))
                failures.Add("[latch-scope] the _splashActive double-press guard was removed from " +
                             "OnStartNew. WO-1688 §2.1 keeps it: it is not a substitute for the confirm, " +
                             "but it is still the double-press guard.");

            int firstDrop = onStartNew.IndexOf("_splashActive = false", StringComparison.Ordinal);
            int gate = onStartNew.IndexOf("HasExistingSave()", StringComparison.Ordinal);
            if (firstDrop >= 0 && gate >= 0 && firstDrop < gate)
                failures.Add("[latch-scope] _splashActive is dropped BEFORE the has-a-save gate, i.e. on the " +
                             "mere press. Then a player who chooses 'keep my realm' finds Continue and Play " +
                             "Intro dead for the rest of the scene, because every handler early-returns on " +
                             "!_splashActive - a front-door softlock traded for the save loss.");

            int confirmAt = onStartNew.IndexOf("BuildConfirmModal", StringComparison.Ordinal);
            int lastDrop = onStartNew.LastIndexOf("_splashActive = false", StringComparison.Ordinal);
            if (confirmAt >= 0 && lastDrop < confirmAt)
                failures.Add("[latch-scope] no _splashActive drop follows the confirm, so the CONFIRMED " +
                             "branch never arms the double-press guard it is supposed to keep.");
        }

        // ── CASE 6 — the words, and which face is destructive ────────────────
        private static void Case6_CopyNamesLoss(string raw, string src, string onStartNew, List<string> failures)
        {
            foreach (var name in new[] { "StartNewConfirmTitle", "StartNewConfirmBody",
                                         "StartNewConfirmEraseLabel", "StartNewConfirmKeepLabel" })
                if (!src.Contains(name))
                    failures.Add("[copy-names-loss] the copy constant " + name + " is missing. The wipe " +
                                 "prompt's words live in ONE named place so the owner can rule on them and " +
                                 "the headless capture can reflect them instead of retyping them.");

            // The body must NAME the loss and its permanence. Facts, not "are you sure".
            string body = ExtractConstValue(raw, "StartNewConfirmBody");
            if (body == null)
            {
                failures.Add("[copy-names-loss] could not read StartNewConfirmBody's literal.");
            }
            else
            {
                string lower = body.ToLowerInvariant();
                if (!lower.Contains("eras") && !lower.Contains("delet"))
                    failures.Add("[copy-names-loss] the confirm body never says the realm is erased. " +
                                 "'Are you sure?' is not a warning; naming what is lost is.");
                if (!lower.Contains("cannot be undone") && !lower.Contains("can't be undone"))
                    failures.Add("[copy-names-loss] the confirm body does not say the loss is permanent. " +
                                 "It is: a wipe is not unrecoverable-FEELING, it is unrecoverable.");
                if (!lower.Contains("realm") && !lower.Contains("town"))
                    failures.Add("[copy-names-loss] the confirm body does not name WHAT is lost.");
            }

            // ── [face-fits] THE MEASURED BUDGET, not a taste rule ────────────────
            // Chain 41 (2026-09-10) built this sheet for the first time and the glyph oracle
            // measured it on the Seeker's real surface:
            //     [glyph-oracle] TEXT TRUNCATED [StartNewConfirm_2670x1200 @2670x1200]
            //     '.../ObsBtn_Erase and Start New/Label' ("ERASE AND START NEW") draws
            //     12 of 16 printable glyphs ... isTextTruncated=True
            // Four glyphs of the most destructive label in the game were not on screen.
            // ⛔ THE UNIT IS PRINTABLE GLYPHS, which is the oracle's own unit — it counted 16
            // for a 19-character string because it does not count the spaces. Counting
            // Length here instead would compare two different things and pass a label the
            // pixels reject. 12 is used as measured, not rounded to a comfortable number.
            // The face BAND (356.9x48.5 ref px, 63.5 under the 112 touch floor) is the
            // FIT-GUARD lane's — WO-1690, ElarionUiKit.BuildConfirmModal. The WORDS are this
            // ticket's, so the words are pinned against what the pixels actually proved.
            const int MeasuredFaceGlyphBudget = 12;
            foreach (var name in new[] { "StartNewConfirmEraseLabel", "StartNewConfirmKeepLabel" })
            {
                string label = ExtractConstValue(raw, name);
                if (label == null) continue;   // absence already reported above
                int printable = 0;
                foreach (var ch in label) if (!char.IsWhiteSpace(ch)) printable++;
                if (printable > MeasuredFaceGlyphBudget)
                    failures.Add("[copy-names-loss/face-fits] " + name + " is \"" + label + "\" (" +
                                 printable + " printable glyphs) but the confirm face measured a " +
                                 MeasuredFaceGlyphBudget + "-glyph budget at 2670x1200 (chain 41: " +
                                 "\"ERASE AND START NEW\" drew 12 of 16). A destructive face that " +
                                 "cannot say its own word is worse than a short one, and the BODY above " +
                                 "it already names the loss in full. Re-run the front-door capture and " +
                                 "read the [glyph-oracle] line before raising this budget - never raise " +
                                 "it because a longer label reads better in a diff.");
            }

            if (onStartNew != null && !onStartNew.Contains("ButtonKind.Danger"))
                failures.Add("[copy-names-loss] the destructive face does not carry ButtonKind.Danger, so " +
                             "the erase face reads like any other primary action.");
        }

        // ── CASE 7 — every accidental gesture is the safe one ────────────────
        private static void Case7_CancelIsSafe(string onStartNew, List<string> failures)
        {
            int at = onStartNew.IndexOf("onCancel:", StringComparison.Ordinal);
            if (at < 0)
            {
                failures.Add("[cancel-is-safe] the confirm supplies no onCancel handler. The kit wires the " +
                             "shared Close AND the full-screen scrim to onCancel, so without it a tap " +
                             "outside the sheet has no defined safe answer.");
                return;
            }
            int end = onStartNew.IndexOf("confirmKind", at, StringComparison.Ordinal);
            string cancelRegion = end > at ? onStartNew.Substring(at, end - at) : onStartNew.Substring(at);
            if (cancelRegion.Contains("PerformStartNew") || cancelRegion.Contains("ResetToNewGame"))
                failures.Add("[cancel-is-safe] the cancel handler reaches the destructive path. Cancel, the " +
                             "shared Close and the scrim all land here; if any of them can wipe, the sheet " +
                             "has made the accident easier, not harder.");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>Brace-matched body of a method, given its exact signature line.</summary>
        private static string ExtractMethodBody(string src, string signature, string label, List<string> failures)
        {
            int sig = src.IndexOf(signature, StringComparison.Ordinal);
            if (sig < 0)
            {
                failures.Add("[source] " + label + " was not found in " + TitleSrc + " with signature '" +
                             signature + "' - the method shape changed and this oracle is proving nothing " +
                             "about it");
                return null;
            }
            int open = src.IndexOf('{', sig);
            if (open < 0) { failures.Add("[source] " + label + " has no opening brace"); return null; }
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            failures.Add("[source] " + label + "'s body is unbalanced - could not find its closing brace");
            return null;
        }

        /// <summary>Read a `private const string NAME = "..." + "...";` literal's joined value.</summary>
        private static string ExtractConstValue(string raw, string name)
        {
            var m = Regex.Match(raw, @"const\s+string\s+" + Regex.Escape(name) + @"\s*=\s*(.*?);",
                                RegexOptions.Singleline);
            if (!m.Success) return null;
            var sb = new System.Text.StringBuilder();
            foreach (Match lit in Regex.Matches(m.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                sb.Append(lit.Groups[1].Value);
            return sb.ToString();
        }

        private static string ReadSource(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("[source] " + path + " not found - the file moved without updating this oracle");
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("[source] could not read " + path + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", " ");
        }

        /// <summary>
        /// Blank the CONTENTS of string and char literals (the quotes are kept, so brace
        /// matching and index arithmetic are unaffected) while PRESERVING interpolation
        /// holes — the code inside <c>$"...{ Foo() }..."</c> is still code, and blanking it
        /// would hide a real call from every assertion above. Handles verbatim (<c>@"..."</c>,
        /// where <c>""</c> is an escaped quote and backslash is literal), interpolated
        /// (<c>$"</c> and <c>$@"</c>) and plain literals, plus char literals.
        /// <para>Run this AFTER StripComments: it does not know about comments, and a quote
        /// inside a comment would otherwise open a literal that never closes.</para>
        /// </summary>
        private static string StripLiterals(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var outp = new System.Text.StringBuilder(src.Length);
            int i = 0;
            while (i < src.Length)
            {
                char c = src[i];

                // ── char literal ────────────────────────────────────────────────
                if (c == '\'')
                {
                    outp.Append(c);
                    i++;
                    while (i < src.Length && src[i] != '\'')
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) { outp.Append(' '); i++; }
                        outp.Append(' ');
                        i++;
                    }
                    if (i < src.Length) { outp.Append('\''); i++; }
                    continue;
                }

                // ── string literal, with its $ / @ prefixes ─────────────────────
                bool interpolated = false, verbatim = false;
                int look = i;
                while (look < src.Length && (src[look] == '$' || src[look] == '@'))
                {
                    if (src[look] == '$') interpolated = true; else verbatim = true;
                    look++;
                }
                // A $ / @ run that is NOT followed by a quote is ordinary code (a verbatim
                // identifier, say): fall through, emit one char, advance one. No loop risk.
                if (look < src.Length && src[look] == '"')
                {
                    for (int k = i; k <= look; k++) outp.Append(src[k]);
                    i = look + 1;
                    int holeDepth = 0;
                    while (i < src.Length)
                    {
                        char d = src[i];
                        if (holeDepth > 0)
                        {
                            // Inside an interpolation hole: this is CODE, keep it verbatim.
                            if (d == '{') holeDepth++;
                            else if (d == '}') holeDepth--;
                            outp.Append(d);
                            i++;
                            continue;
                        }
                        if (!verbatim && d == '\\' && i + 1 < src.Length)
                        {
                            outp.Append(' ').Append(' ');   // escape pair, blanked, length kept
                            i += 2;
                            continue;
                        }
                        if (d == '"')
                        {
                            if (verbatim && i + 1 < src.Length && src[i + 1] == '"')
                            {
                                outp.Append(' ').Append(' ');   // "" = one escaped quote
                                i += 2;
                                continue;
                            }
                            outp.Append('"');
                            i++;
                            break;                              // literal closed
                        }
                        if (interpolated && d == '{')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '{')
                            {
                                outp.Append(' ').Append(' ');   // {{ = a literal brace
                                i += 2;
                                continue;
                            }
                            holeDepth = 1;
                            outp.Append(d);
                            i++;
                            continue;
                        }
                        // Ordinary literal text -> blanked, but newlines are KEPT so a
                        // verbatim multi-line string cannot collapse the file's line shape.
                        outp.Append(d == '\n' || d == '\r' ? d : ' ');
                        i++;
                    }
                    continue;
                }

                outp.Append(c);
                i++;
            }
            return outp.ToString();
        }
    }
}
