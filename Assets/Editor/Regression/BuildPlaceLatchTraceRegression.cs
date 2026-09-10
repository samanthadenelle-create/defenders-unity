// =============================================================================
// BuildPlaceLatchTraceRegression -- WO-1615 (Build -> Move -> PLACE does not seat)
//
// WHAT THIS PINS, AND WHY IT IS A SOURCE ORACLE
// ---------------------------------------------------------------------------
// WO-1615 could not be diagnosed for one reason: the PLACE path had NO trace of its
// own. A tap that never reached the chip and a tap that reached a dead binding
// produced the SAME log -- nothing at all -- while the raw world tap was correctly
// suppressed as "over UI" without ever naming the surface that owned it. CLAUDE.md
// sec.12 forbids fixing on that evidence, so the ticket's FIRST deliverable is the
// two discriminating traces, and sec.12 also says instrumentation is PERMANENT:
// once a system is proven stable it may be flagged off, the calls STAY.
//
// This suite is the never-strip pin. It fails if either trace is deleted or moved
// off its site, and it fails if the two invariants WO-1615 sec.4 protects are
// weakened -- the over-UI suppression, and the single commit latch.
//
// It is a SOURCE oracle (comments stripped, string literals KEPT -- the literals are
// exactly what is being pinned) because the thing under test is a log line, which no
// headless play run can assert without the very capture this ticket is waiting for.
//
// CASES
//   C1  BuildHudController's OkChip verb emits "OkChip TAPPED (onPlace bound=" and
//       still invokes _onPlace -- inside the SAME lambda, trace before the invoke.
//   C2  BuildModeController's over-UI suppression branch NAMES the owner: it reads
//       s_uiHits[0] and reports hits[0] / path / module / movingSelected.
//   C3  The suppression is INTACT (sec.4: never weakened) -- IsPointOverUi still
//       gates the branch and the branch still returns ConfirmKind.None.
//   C4  The ONE commit latch is intact -- the UI PLACE latch still logs
//       "PlaceConfirm: UI PLACE button latch consumed" and returns ConfirmKind.UiPlace,
//       and UpdateMoveLoop still commits ONLY on ConfirmKind.UiPlace (sec.4 forbids a
//       second commit path, which would hide this bug forever).
//
// Wire (DataRegression.RunAll) -- lead-owned file, no lane edits it:
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "place-latch-trace suite", () => { if (!DeNelle.Editor.BuildPlaceLatchTraceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[place-latch-trace] " + r); });
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>
    /// Source oracle for WO-1615: the PLACE confirm path must stay instrumented at both
    /// ends (the chip callback and the over-UI suppression), and must keep exactly one
    /// commit latch.
    /// </summary>
    public static class BuildPlaceLatchTraceRegression
    {
        private const string Tag = "[place-latch-trace]";

        private const string HudRel        = "Assets/_Modules/Village/BuildMode/BuildHudController.cs";
        private const string ControllerRel = "Assets/_Modules/Village/BuildMode/BuildModeController.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== BuildPlaceLatchTraceRegression (WO-1615) ===\n");
            try
            {
                CheckTraces(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "PLACE_LATCH_TRACE_OK the PLACE confirm path is instrumented at both ends " +
                         "(OkChip callback entry + the named over-UI raycast owner), the over-UI " +
                         "suppression is intact, and ConfirmKind.UiPlace is still the only commit latch";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "PLACE_LATCH_TRACE_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // =====================================================================
        private static void CheckTraces(List<string> failures, StringBuilder log)
        {
            string root = Directory.GetParent(Application.dataPath.Replace('\\', '/')).FullName.Replace('\\', '/');

            string hud        = ReadNoComments(root, HudRel, failures, log);
            string controller = ReadNoComments(root, ControllerRel, failures, log);
            if (hud == null || controller == null) return;

            // -- C1: the OkChip callback names itself BEFORE it invokes ---------------
            string okChipRegion = Between(hud, "MakeWordVerb(railRt, \"OkChip\"", "out _okChipLabel);");
            if (okChipRegion == null)
            {
                failures.Add(Tag + " C1: could not find the OkChip verb construction in " + HudRel +
                             " (MakeWordVerb(railRt, \"OkChip\" ... out _okChipLabel);). The GameObject " +
                             "name OkChip is load-bearing -- UICaptureLaunch.AssertConfirmChipInvalid " +
                             "finds the confirm chip by that literal -- so a rename here breaks the " +
                             "capture harness as well as this pin.");
            }
            else
            {
                if (okChipRegion.Contains("OkChip TAPPED (onPlace bound="))
                    log.AppendLine("[C1a] " + HudRel + " OkChip callback emits the WO-1615 entry trace -- OK");
                else
                    failures.Add(Tag + " C1a: the OkChip callback in " + HudRel + " no longer emits " +
                                 "\"OkChip TAPPED (onPlace bound=\". That single line is what separates " +
                                 "\"the click never reached the chip\" from \"the chip fired into a null or " +
                                 "stale binding\" -- WO-1615 sec.3.2, and CLAUDE.md sec.12 forbids stripping it.");

                int tracePos  = okChipRegion.IndexOf("OkChip TAPPED", StringComparison.Ordinal);
                int invokePos = okChipRegion.IndexOf("_onPlace?.Invoke()", StringComparison.Ordinal);
                if (invokePos < 0)
                    failures.Add(Tag + " C1b: the OkChip callback in " + HudRel + " no longer calls " +
                                 "_onPlace?.Invoke(). The PLACE chip would be a button that does nothing -- " +
                                 "the exact defect WO-1615 was opened for.");
                else if (tracePos >= 0 && tracePos < invokePos)
                    log.AppendLine("[C1b] the entry trace precedes _onPlace?.Invoke() -- OK");
                else if (tracePos >= 0)
                    failures.Add(Tag + " C1b: the OkChip entry trace sits AFTER _onPlace?.Invoke() in " +
                                 HudRel + ". It must be the FIRST statement in the callback, or a throw " +
                                 "inside the invoke would erase the very evidence it exists to capture.");
            }

            // -- C2: the over-UI suppression NAMES its owner --------------------------
            // Anchored on the BRANCH, not on the log string: `var top = s_uiHits[0];` sits ABOVE
            // the FlowTrace.Warn literal, so starting at the message text would silently exclude
            // the very read this case exists to pin.
            string suppressRegion = Between(controller, "if (IsPointOverUi(_input.ScreenPoint))", "return ConfirmKind.None;");
            if (suppressRegion == null)
            {
                failures.Add(Tag + " C2: could not find the over-UI suppression branch in " + ControllerRel +
                             " (if (IsPointOverUi(_input.ScreenPoint)) ... return ConfirmKind.None;). That " +
                             "branch is the only place a build tap eaten by UI is reported at all.");
            }
            else
            {
                foreach (string token in new[] { "PlaceConfirm SUPPRESSED: tap at", "s_uiHits[0]",
                                                 "hits[0]='", "path='", "module=", "movingSelected=" })
                {
                    if (suppressRegion.Contains(token))
                        log.AppendLine("[C2] suppression trace reports " + token + " -- OK");
                    else
                        failures.Add(Tag + " C2: the over-UI suppression trace in " + ControllerRel +
                                     " no longer reports '" + token + "'. WO-1615 sec.3.1: saying only " +
                                     "\"is over UI\" without naming the surface is precisely why this ticket " +
                                     "could not be fixed from the existing log.");
                }
            }

            // -- C3: the suppression itself is intact (sec.4) -------------------------
            if (Has(controller, @"if\s*\(\s*IsPointOverUi\s*\(\s*_input\.ScreenPoint\s*\)\s*\)"))
                log.AppendLine("[C3a] IsPointOverUi still gates the world-tap confirm -- OK");
            else
                failures.Add(Tag + " C3a: " + ControllerRel + " no longer gates the world tap on " +
                             "IsPointOverUi(_input.ScreenPoint). WO-1615 sec.4 forbids weakening it: without " +
                             "it a tap on the PLACE chip would ALSO drop a building under the chip.");

            if (Has(controller, @"internal\s+static\s+bool\s+IsPointOverUi"))
                log.AppendLine("[C3b] the shared EventSystem probe IsPointOverUi still exists -- OK");
            else
                failures.Add(Tag + " C3b: " + ControllerRel + " no longer declares IsPointOverUi. " +
                             "LeanTouchBuildDriver's finger handlers call this same probe -- deleting it " +
                             "sends the touch path back to LeanFinger.IsOverGui, which misses every " +
                             "code-built canvas in this project.");

            // -- C4: exactly ONE commit latch (sec.4) ---------------------------------
            if (Has(controller, @"public\s+void\s+RequestUiPlaceConfirm\s*\(\s*\)\s*=>\s*_uiPlaceLatch\s*=\s*true;"))
                log.AppendLine("[C4a] RequestUiPlaceConfirm still sets the one commit latch -- OK");
            else
                failures.Add(Tag + " C4a: " + ControllerRel + " no longer declares " +
                             "RequestUiPlaceConfirm as the _uiPlaceLatch setter. That method is what " +
                             "BuildModeController.EnsureHud binds the PLACE chip to; if it changes shape, " +
                             "the chip's binding is the first thing to re-prove.");

            if (controller.Contains("PlaceConfirm: UI PLACE button latch consumed"))
                log.AppendLine("[C4b] the latch-consumed trace is intact -- OK");
            else
                failures.Add(Tag + " C4b: " + ControllerRel + " no longer logs \"PlaceConfirm: UI PLACE " +
                             "button latch consumed\". That line is the second half of the WO-1615 " +
                             "acceptance chain (OkChip TAPPED -> latch consumed -> MOVE COMMITTED); " +
                             "without it the capture cannot prove the fix.");

            string moveLoop = ExtractMoveLoop(controller);
            if (moveLoop == null)
            {
                failures.Add(Tag + " C4c: could not locate UpdateMoveLoop in " + ControllerRel +
                             " -- the commit path for a move cannot be evaluated.");
            }
            else
            {
                if (Has(moveLoop, @"confirm\s*==\s*ConfirmKind\.UiPlace"))
                    log.AppendLine("[C4c] UpdateMoveLoop still commits only on ConfirmKind.UiPlace -- OK");
                else
                    failures.Add(Tag + " C4c: UpdateMoveLoop no longer commits on 'confirm == " +
                                 "ConfirmKind.UiPlace'. WO-1615 sec.4 forbids a second commit path: a " +
                                 "world-tap or long-press fallback would seat the tower while leaving the " +
                                 "PLACE button broken, hiding this defect permanently.");

                if (Has(moveLoop, @"ConfirmKind\.WorldTap"))
                    failures.Add(Tag + " C4d: UpdateMoveLoop now reacts to ConfirmKind.WorldTap. A world " +
                                 "tap may only RE-AIM the move ghost -- committing on it duplicates the " +
                                 "decider and is exactly the fallback WO-1615 sec.4 bans.");
                else
                    log.AppendLine("[C4d] UpdateMoveLoop does not act on a world tap -- OK");
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Read a file with COMMENTS stripped but STRING LITERALS KEPT. The opposite of the
        /// usual ReadStripped in this folder, and deliberately so: this suite pins log-line
        /// literals, so stripping strings would make every case vacuously pass, while keeping
        /// comments would let a mere mention of a trace in prose satisfy the pin.
        /// </summary>
        private static string ReadNoComments(string root, string rel, List<string> failures, StringBuilder log)
        {
            string abs = root + "/" + rel;
            if (!File.Exists(abs))
            {
                failures.Add(Tag + " missing file: " + rel + " (the PLACE trace chain cannot be evaluated)");
                return null;
            }
            string src = File.ReadAllText(abs);
            src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            src = Regex.Replace(src, @"//[^\n]*", " ");
            log.AppendLine("read " + rel + " (" + src.Length + " chars, comments stripped, strings kept)");
            return src;
        }

        private static bool Has(string source, string pattern)
            => source != null && Regex.IsMatch(source, pattern, RegexOptions.Singleline);

        /// <summary>First region from <paramref name="from"/> up to the first following
        /// <paramref name="to"/>; null when either marker is absent.</summary>
        private static string Between(string source, string from, string to)
        {
            if (source == null) return null;
            int a = source.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = source.IndexOf(to, a, StringComparison.Ordinal);
            if (b < 0) return null;
            return source.Substring(a, b - a + to.Length);
        }

        /// <summary>
        /// The body of UpdateMoveLoop, bounded by the NEXT method signature rather than by
        /// brace matching -- the file's own quality gate counts raw braces, so this suite does
        /// not write unpaired brace literals of its own to walk them.
        /// </summary>
        private static string ExtractMoveLoop(string source)
        {
            const string sig = "private void UpdateMoveLoop()";
            int a = source.IndexOf(sig, StringComparison.Ordinal);
            if (a < 0) return null;
            var next = Regex.Match(source.Substring(a + sig.Length), @"\n\s*(private|public|internal|protected)\s");
            int len = next.Success ? next.Index : Math.Min(8000, source.Length - a - sig.Length);
            return source.Substring(a, sig.Length + len);
        }
    }
}
