// =====================================================================
//  TextFitGuardArmRegression -- WO-1652. THE §1.14 FIT GUARD MUST SAY WHICH
//  DOOR IT TOOK, ESPECIALLY WHEN IT DECLINES TO ARM.
// ---------------------------------------------------------------------
//  WHAT THIS PINS, AND WHY IT EXISTS.
//
//  MEASURED (WO-1651 / WO-1652 §1, Builds/wave5-manageflow1, 2026-09-10): the
//  Manage queue refund note drew ZERO of 33 printable glyphs on all three
//  ManageFlow_{BUILD,ARMY,RESEARCH}_queue frames -- a whole-line cull, which is
//  precisely the event UiKitTextFitGuard's render assert exists to FlowTrace.Fail
//  on. `grep TextFitGuard` over every wave5-* and wave3-capture* log returns ZERO
//  lines. Re-measured in this worktree at HEAD 736b6b4b9: 0 `TextFitGuard` lines
//  in Builds/wave5-manageflow1, alongside 64 `[Flow:UI]` lines -- so the "UI"
//  system DOES print in a headless capture and the guard's silence is the guard's,
//  not the trace channel's.
//
//  PROVEN CAUSE (WO-1652 §2, two independent locks):
//    1. ArmFitGuard early-returns on !Application.isPlaying, and the headless
//       capture entry point never enters Play mode -- so no guard is ever attached.
//    2. Even attached, the guard needs two LateUpdate ticks; the capture harness
//       ticks none (no yield, DestroyImmediate teardown).
//  The early-out was SILENT, which is the whole bug: a capture log with zero guard
//  lines read exactly like a capture where every label was fine.
//
//  ⛔ SCOPE. This suite pins the INSTRUMENTATION CONTRACT ONLY. It deliberately
//  does NOT assert that the guard arms or evaluates in edit mode -- that would be
//  WO-1652 §6 remedy A smuggled in as a test, and remedy A is the one that could
//  silently retire the glyph oracle's teeth. The remedy is TABLED pending a ruling.
//  What is asserted is only this: a declined arm SPEAKS, and it names its branch
//  and the Application.isPlaying value it decided on. Whether the guard should run
//  in captures is a product decision this file takes no position on.
//
//  RED-FIRST (PROD-008 / WO-1138). On the pre-WO-1652 tree Case B and Case C FAIL:
//  ArmFitGuard returns at the isPlaying door without a word, so no `TextFitGuard
//  ARM` line and no `TextFitGuard CENSUS` line exists to find. They pass only once
//  the §4 instrumentation lands. Case A proves the fixture actually reproduces the
//  WO-1651 cull shape, so B and C are not vacuous.
//
//  MEASUREMENT, NOT ASSERTION. Whether a UiKitTextFitGuard component attached and
//  whether any `TextFitGuard EVAL` line appeared are REPORTED in the reason string,
//  never asserted -- they are the two numbers WO-1652 §4 exists to produce, and
//  pinning either one would pick a remedy.
//
//  Canvas geometry mirrors CostRowFitRegression: a WORLD-SPACE canvas sized to the
//  reference-px extent the kit's scaler resolves, because a ScreenSpace canvas in
//  an edit-mode batchmode call reports the editor's own 640x480 and every number
//  is fiction.
// =====================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Editor.Regression
{
    public static class TextFitGuardArmRegression
    {
        private const string Tag = "[textfit-guard-arm]";

        /// <summary>The kit's scaler settings, mirrored so the reference-px extent can be computed
        /// without a live CanvasScaler.Update (which does not run in a synchronous call).</summary>
        private const float RefW = 1080f, RefH = 1920f, Match = 0.5f;

        private const int ProbeW = 1920, ProbeH = 1080;

        /// <summary>The WO-1651 shape: a refund note of 33 printable glyphs.</summary>
        private const string CulledText = "Cancel refunds 100% of what you paid";

        /// <summary>A band far too short to seat even the FontHardFloor(20) line
        /// ((20+1)*~1.35 + 2 ~= 30 px), so TMP's Ellipsis culls the whole line.</summary>
        private const float ThinBandPx = 12f;
        private const float BandWidthPx = 260f;

        [MenuItem("Tools/Regression/UI/Text Fit Guard Arm Trace (WO-1652)")]
        public static void RunMenu()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason); else Debug.LogError(reason);
        }

        /// <summary>Captures every FlowTrace line so the suite can assert the ARM trace, and
        /// forwards to the previous sink so the run log keeps them.</summary>
        private sealed class CapturingSink : ITraceSink
        {
            public readonly List<string> Lines = new List<string>();
            private readonly ITraceSink _inner;
            public CapturingSink(ITraceSink inner) { _inner = inner; }
            public void Info(string line) { Lines.Add(line); _inner?.Info(line); }
            public void Warn(string line) { Lines.Add(line); _inner?.Warn(line); }
            public void Error(string line) { Lines.Add(line); _inner?.Error(line); }
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- TEXT FIT GUARD ARM TRACE (WO-1652): a declined arm must SAY it declined ---");

            bool enabledBefore = FlowTrace.Enabled;
            var sinkBefore = FlowTrace.Sink;
            var sink = new CapturingSink(sinkBefore);
            FlowTrace.Enabled = true;
            FlowTrace.AllOn();
            FlowTrace.Sink = sink;
            // Earlier suites in DataRegression.RunAll call FitSingleLine, so the once-only ARM
            // keys have already fired by the time this suite runs. Without this reset the whole
            // suite reads green-by-accident on a tree with no instrumentation at all.
            FlowTrace.ResetSession();

            GameObject canvas = null;
            try
            {
                canvas = BuildCanvas(ProbeW, ProbeH);
                var label = BuildCulledLabel(canvas.transform);
                if (label == null)
                {
                    failures.Add(Tag + " the probe label could not be built, so this suite proves " +
                                 "nothing. A vacuous trace check reads green forever.");
                    return Finish(failures, log, out reason, enabledBefore, sinkBefore, canvas);
                }

                // THE CALL UNDER TEST. FitSingleLine is the production door into ArmFitGuard.
                ElarionUiKit.FitSingleLine(label);
                Settle(canvas);
                label.ForceMeshUpdate();

                CaseA_FixtureActuallyCulls(label, failures, log);
                CaseB_ArmDeclineSpeaks(sink, failures, log);
                CaseC_CensusCarriesCounts(sink, failures, log);
                CaseD_StandDownDoesNotAccuseTheProducer(failures, log);
                CaseE_ConfirmModalTitleBandSeatsTheFloor(failures, log);
                CaseF_ConfirmModalBodyStaysBetweenHeaderAndFaces(failures, log);
                MeasureOnly_GuardAttachAndEval(label, sink, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw: " + ex.GetType().Name + " " + ex.Message);
            }

            return Finish(failures, log, out reason, enabledBefore, sinkBefore, canvas);
        }

        private static bool Finish(List<string> failures, StringBuilder log, out string reason,
                                   bool enabledBefore, ITraceSink sinkBefore, GameObject canvas)
        {
            Kill(canvas);
            FlowTrace.Sink = sinkBefore;
            FlowTrace.Enabled = enabledBefore;

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine(Tag + " FAIL (" + failures.Count + "):");
                foreach (var f in failures) sb.AppendLine("  - " + f);
                sb.Append(log.ToString());
                reason = sb.ToString();
                return false;
            }

            reason = Tag + " OK - the fit guard's ARM decision is traced on every branch.\n" + log.ToString();
            return true;
        }

        // -----------------------------------------------------------------
        //  CASE A -- the fixture must actually reproduce the WO-1651 cull.
        //  If the band is wide enough to seat a line, Cases B and C are testing
        //  a label that was never in trouble.
        // -----------------------------------------------------------------
        private static void CaseA_FixtureActuallyCulls(TMP_Text label, List<string> failures, StringBuilder log)
        {
            var ti = label.textInfo;
            int chars = ti != null ? ti.characterCount : -1;
            int visible = 0;
            if (ti != null)
                for (int i = 0; i < ti.characterCount; i++)
                    if (ti.characterInfo[i].isVisible) visible++;

            float h = label.rectTransform.rect.height;
            log.AppendLine("  [fixture] band " + ((int)label.rectTransform.rect.width) + "x" + ((int)h) +
                           " px, font " + (label.font != null ? label.font.name : "<null>") +
                           ", chars " + chars + ", visible glyphs " + visible +
                           ", fontSizeMin " + label.fontSizeMin.ToString("F0") +
                           ", overflow " + label.overflowMode + ".");

            if (label.font == null)
            {
                failures.Add(Tag + " CASE A: the probe label resolved NO TMP font asset, so glyph " +
                             "visibility is unmeasurable and 'zero visible' would be true for the wrong " +
                             "reason. Fix the fixture's font resolution before trusting this suite.");
                return;
            }

            if (h > ThinBandPx + 1f)
            {
                failures.Add(Tag + " CASE A: the probe band measured " + h.ToString("0.#") + " px, not the " +
                             "authored " + ThinBandPx + " px - something grew it, so the cull this suite " +
                             "reproduces is not the WO-1651 shape any more.");
                return;
            }

            if (visible != 0)
            {
                failures.Add(Tag + " CASE A: the probe label drew " + visible + " visible glyphs in a " +
                             ((int)h) + " px band. The fixture no longer reproduces the WO-1651 whole-line " +
                             "cull, so the ARM-trace cases below prove nothing about the failing case.");
            }
        }

        // -----------------------------------------------------------------
        //  CASE B -- RED FIRST. The decline must SPEAK, naming its branch and
        //  the Application.isPlaying value it decided on.
        // -----------------------------------------------------------------
        private static void CaseB_ArmDeclineSpeaks(CapturingSink sink, List<string> failures, StringBuilder log)
        {
            string armLine = FindLine(sink, "TextFitGuard ARM");
            if (armLine == null)
            {
                failures.Add(Tag + " CASE B (the WO-1652 bug, RED-first): FitSingleLine armed a label in " +
                             "edit mode and ArmFitGuard emitted NO 'TextFitGuard ARM' line at all. A guard " +
                             "that declines to arm SILENTLY is why every headless capture log carries zero " +
                             "TextFitGuard lines and 'the guard ran and was fine' is indistinguishable from " +
                             "'the guard never ran'. Add the §4 arm trace in ElarionUiKitObsidian.ArmFitGuard.");
                return;
            }

            log.AppendLine("  [arm] " + armLine);

            if (armLine.IndexOf("branch=", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " CASE B: the ARM line does not name its branch (expected 'branch=<name>'): " +
                             armLine);

            if (armLine.IndexOf("isPlaying=", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " CASE B: the ARM line does not carry the Application.isPlaying value " +
                             "(expected 'isPlaying=True|False'), which is the single fact that explains the " +
                             "decline: " + armLine);

            // A regression suite runs in EDIT mode, so the branch taken here is necessarily the
            // isPlaying door. Asserting the branch NAME (not that it armed) keeps this remedy-neutral.
            if (armLine.IndexOf("branch=not-playing", StringComparison.Ordinal) < 0 ||
                armLine.IndexOf("isPlaying=False", StringComparison.Ordinal) < 0)
            {
                failures.Add(Tag + " CASE B: an edit-mode call must report 'branch=not-playing isPlaying=False' " +
                             "- that is the door WO-1652 proved every headless capture takes. Got: " + armLine);
            }
        }

        // -----------------------------------------------------------------
        //  CASE C -- the census. WO-1652 acceptance §3 asks for COUNTS (how many
        //  labels arm, how many relax) off a capture log and a play-mode log, so
        //  the counting line has to exist and has to carry the fields.
        // -----------------------------------------------------------------
        private static void CaseC_CensusCarriesCounts(CapturingSink sink, List<string> failures, StringBuilder log)
        {
            string census = FindLine(sink, "TextFitGuard CENSUS");
            if (census == null)
            {
                failures.Add(Tag + " CASE C (RED-first): no 'TextFitGuard CENSUS' line was emitted. WO-1652 " +
                             "acceptance §3 is a COUNT of armed vs relaxed labels off a fresh log; without a " +
                             "rollup line there is nothing to count and the arm/relax question stays unproven " +
                             "in both directions.");
                return;
            }

            log.AppendLine("  [census] " + census);

            string[] required = { "armCalls=", "armed=", "declinedNotPlaying=", "evaluated=", "relaxed=" };
            foreach (var field in required)
            {
                if (census.IndexOf(field, StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " CASE C: the census line is missing the '" + field + "' field: " + census);
            }

            if (census.IndexOf("armCalls=0", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " CASE C: the census reports armCalls=0 after FitSingleLine was called, so " +
                             "the counter is not wired to the call it counts: " + census);
        }

        // -----------------------------------------------------------------
        //  MEASUREMENT ONLY -- never an assertion. These two numbers ARE the
        //  WO-1652 §4 finding; pinning either would pick a remedy (§6 A/B/C).
        // -----------------------------------------------------------------
        private static void MeasureOnly_GuardAttachAndEval(TMP_Text label, CapturingSink sink, StringBuilder log)
        {
            bool attached = false;
            var comps = label.GetComponents<MonoBehaviour>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c.GetType().Name == "UiKitTextFitGuard") { attached = true; break; }
            }

            string evalLine = FindLine(sink, "TextFitGuard EVAL");
            log.AppendLine("  [measurement, NOT a pass/fail] guard component attached in edit mode: " +
                           (attached ? "YES" : "NO") + "; 'TextFitGuard EVAL' line seen: " +
                           (evalLine != null ? "YES -> " + evalLine : "NO") +
                           ". Both are expected NO on the current tree (edit mode: no arm, no LateUpdate tick). " +
                           "They are recorded, not asserted - WO-1652 §6 remedies A/B/C are TABLED pending an " +
                           "owner ruling, and asserting either value here would pick one.");
        }

        // -----------------------------------------------------------------
        //  CASE D -- WO-1656. THE STAND-DOWN MUST NOT ACCUSE A CORRECTLY-EMPTY LABEL.
        //
        //  RED-FIRST: on the pre-WO-1656 wording the empty half ended "(a blank plate
        //  here is a TEXT-NEVER-SET bug, not a fit bug)" and this case fails on it. It
        //  was measured false on APK 363722 for BOTH labels it fired on -- the WO-1656
        //  discriminating check returned `back-glyph-miss` = 0 and `[Flow:Manage] notice:`
        //  = 0 across two device logs (2026-09-10_0929 and _0943, PID 5095) carrying 550
        //  live [Flow:Manage] lines. The back arrow's sprite resolved, so its label was
        //  blanked on purpose (the WO-1491 ruling); no notice was ever raised for the
        //  notice band. Two healthy labels, called a bug by the net that is supposed to
        //  catch real ones.
        //
        //  It pins the wording through the production string builder, not through a
        //  source-text grep: a source lint would pass on a file that no longer calls it.
        //  And it pins the OTHER half still asserting, because narrowing this warning
        //  must never slide into silencing it (WO-1656 acceptance 4, §12).
        // -----------------------------------------------------------------
        private static void CaseD_StandDownDoesNotAccuseTheProducer(List<string> failures, StringBuilder log)
        {
            const string NoticePath = "ManageScreenUI/ObsidianPanel/PanelContent/Band_Notice/Label";
            const string BackPath = "ObsidianPanel/PanelContent/ManageHeaderActions/ManageWorkspaceBack/Label";

            string empty = ElarionUiKit.FitGuardStandDownMessage(NoticePath, true);
            log.AppendLine("  [stand-down/empty] " + empty);

            if (empty.IndexOf("TEXT-NEVER-SET", StringComparison.Ordinal) >= 0)
            {
                failures.Add(Tag + " CASE D (WO-1656, RED-first): the empty-text stand-down still asserts a " +
                             "TEXT-NEVER-SET bug. Measured false on APK 363722 for both labels it fired on " +
                             "(" + NoticePath + " and " + BackPath + "): back-glyph-miss=0 and " +
                             "'[Flow:Manage] notice:'=0 on two device logs. A warning that cries bug on healthy " +
                             "state is how the net that catches real blank plates gets ignored.");
            }

            if (empty.IndexOf("PRODUCER", StringComparison.Ordinal) < 0)
            {
                failures.Add(Tag + " CASE D: the empty-text stand-down does not say whose call the emptiness is. " +
                             "The guard cannot tell empty-by-design from never-set from inside LateUpdate - the " +
                             "message must hand that judgement to the producer, not guess it.");
            }

            if (empty.IndexOf("EMPTY", StringComparison.Ordinal) < 0 ||
                empty.IndexOf("600 frames", StringComparison.Ordinal) < 0 ||
                empty.IndexOf(NoticePath, StringComparison.Ordinal) < 0)
            {
                failures.Add(Tag + " CASE D: the stand-down stopped reporting the state it stood down on " +
                             "(the label path, that the text was EMPTY, and the 600-frame wait). Narrowing the " +
                             "verdict must not cost the diagnostic - that would be stripping instrumentation " +
                             "by attrition (§12).");
            }

            // The OTHER half must still assert. A rect that never gained height has no by-design reading.
            string zeroHeight = ElarionUiKit.FitGuardStandDownMessage(NoticePath, false);
            log.AppendLine("  [stand-down/zero-height] " + zeroHeight);

            if (zeroHeight.IndexOf("zero-height", StringComparison.Ordinal) < 0 ||
                zeroHeight.IndexOf("LAYOUT bug", StringComparison.Ordinal) < 0)
            {
                failures.Add(Tag + " CASE D: the zero-height stand-down no longer asserts a layout bug. WO-1656 " +
                             "narrowed the EMPTY half only - a plate that never gained height after 600 frames " +
                             "is a defect with no 'by design' reading, and silencing it would be a regression of " +
                             "the detector, not a pass (WO-1656 acceptance 4).");
            }
        }

        // -----------------------------------------------------------------
        //  CASE E -- WO-1690. THE CONFIRM MODAL'S TITLE BAND MUST SEAT THE FLOOR.
        //
        //  MEASURED (Builds/device-frames/2026-09-10_1235_363866_raid_logcat.txt, APK
        //  2026.09.10.363866): the Skip-Tutorial confirm's title logged
        //  `rect 826x26 ... floor 30 -> 21 ... fontSize now 23` -- 7 px under the
        //  owner's floor, 3 px off FontHardFloor, on the FIRST modal a new player
        //  sees. Cause: BuildObsidianPanel authors the header at 6% OF THE PANEL, and
        //  this modal is only 0.32 of the canvas. WO-1690 re-seats that band in
        //  reference PIXELS (ElarionUiKit.SeatHeaderBandInPixels).
        //
        //  RED-FIRST: on the pre-WO-1690 tree the band measures ~26 px against a
        //  ~37 px need and this case FAILS.
        //
        //  ⚠ WORLD-SPACE CANVAS, deliberately. BuildObsidianModal makes its own
        //  ScreenSpace canvas, and in an edit-mode batchmode call that reports the
        //  editor's own 640x480 -- every number would be fiction (the same trap
        //  CostRowFitRegression's header documents). The canvas is converted and
        //  sized to the reference-px extent before measuring.
        // -----------------------------------------------------------------
        private static void CaseE_ConfirmModalTitleBandSeatsTheFloor(List<string> failures, StringBuilder log)
        {
            GameObject canvasGo = null;
            try
            {
                var modal = ElarionUiKit.BuildConfirmModal(
                    "SkipTutorialConfirm", "Skip Tutorial",
                    "Skip the walkthrough? Your progress is saved.",
                    "Skip", "Keep Playing", null, null);

                if (modal == null || modal.canvas == null || modal.chrome == null || modal.chrome.title == null)
                {
                    failures.Add(Tag + " CASE E: BuildConfirmModal returned no title, so the band this case " +
                                 "pins could not be measured at all. A vacuous check reads green forever.");
                    return;
                }
                canvasGo = modal.canvas;

                // The measurement is fiction on a ScreenSpace canvas in batchmode -- convert first.
                var canvas = canvasGo.GetComponent<Canvas>();
                if (canvas != null) canvas.renderMode = RenderMode.WorldSpace;
                var canvasRt = (RectTransform)canvasGo.transform;
                float sf = ScaleFactor(ProbeW, ProbeH);
                canvasRt.sizeDelta = new Vector2(ProbeW / sf, ProbeH / sf);
                canvasRt.position = Vector3.zero;
                canvasRt.localScale = Vector3.one;
                Settle(canvasGo);

                // ── WO-1690b: the ACTION FACES must be authored AT the touch floor ──
                // Measured on Builds/wave9-frontdoor1 (the WO-1688 START NEW confirm at
                // 2670x1200): both faces resolved 356.9x48.5 ref px, i.e. 63.5 px UNDER
                // MinTouchPx(112). The touch oracle's own instruction is "Author the band AT the
                // floor" -- ClampMinTouch growing them symmetrically at runtime spills them into
                // their neighbours, which is a defect, not a rescue. RED on the pre-fix tree.
                CheckFace(modal.confirm, "confirm", failures, log);
                CheckFace(modal.cancel, "cancel", failures, log);

                var title = modal.chrome.title;
                float bandPx = title.rectTransform.rect.height;
                float needPx = ElarionUiKit.MinBandPxForFloor(title);

                log.AppendLine("  [confirm-band] title band " + bandPx.ToString("0.#") + " px vs need " +
                               needPx.ToString("0.#") + " px (FontFloor " + ElarionUiKit.FontFloor.ToString("F0") +
                               ", font " + (title.font != null ? title.font.name : "<null>") + ").");

                if (bandPx + 0.5f < needPx)
                {
                    failures.Add(Tag + " CASE E (WO-1690, RED-first): the confirm modal's title band measures " +
                                 bandPx.ToString("0.#") + " px but a FontFloor(" +
                                 ElarionUiKit.FontFloor.ToString("F0") + ") line needs " + needPx.ToString("0.#") +
                                 " px. The §1.14 guard will relax the title instead - on device it shrank to " +
                                 "23 px, on the first modal a new player sees. Author the band, do not lower " +
                                 "the floor: ElarionUiKit.SeatHeaderBandInPixels is the seam.");
                }
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " CASE E threw: " + ex.GetType().Name + " " + ex.Message);
            }
            finally { Kill(canvasGo); }
        }

        // -----------------------------------------------------------------
        //  CASE F -- WO-1690c. THE BODY MUST STAY BETWEEN THE HEADER AND THE FACES.
        //
        //  ⚠ THE FRAME EVERY ORACLE PASSED. Builds/ui-capture/StartNewConfirm_2670x1200.png
        //  (opened at source 2026-09-10) shows the body's FIRST line drawn over the gold
        //  title -- and above the panel's frame entirely -- while its LAST line is cut
        //  behind the two 112 px faces. Chain 43 reported UI_TOUCH_OK 7/7 and
        //  UI_GLYPH_OK 7/7 on that same frame.
        //
        //  WHY THEY ALL MISSED IT, and it is a coverage hole, not bad luck:
        //    * the glyph oracle counts characters PER LABEL -- all four lines rendered,
        //      so the count was clean;
        //    * the geometry oracle checks a label against ITS OWN plate, not a sibling;
        //    * the fit guard only speaks when a label is culled or relaxed -- an
        //      unbounded label that spills is neither.
        //  LABEL-VS-LABEL OVERLAP HAD NO RULE AT ALL. This case is that rule.
        //
        //  Three assertions, weakest to strongest. The THIRD is the one that catches the
        //  worst version of this frame, because a rect that escapes the panel entirely is
        //  not overlapping any sibling -- it is simply gone.
        // -----------------------------------------------------------------
        private static void CaseF_ConfirmModalBodyStaysBetweenHeaderAndFaces(List<string> failures, StringBuilder log)
        {
            GameObject canvasGo = null;
            try
            {
                var modal = ElarionUiKit.BuildConfirmModal(
                    "StartNewConfirm", "Erase this realm?",
                    "Starting a new game erases your current realm — your town, your hero and " +
                    "everything you have built. This cannot be undone.",
                    "Erase", "Keep", null, null);

                if (modal == null || modal.canvas == null || modal.chrome == null ||
                    modal.chrome.title == null || modal.message == null || modal.confirm == null)
                {
                    failures.Add(Tag + " CASE F: the confirm modal did not return title + message + face, so " +
                                 "the overlap this case pins could not be measured. A vacuous check reads green.");
                    return;
                }
                canvasGo = modal.canvas;

                var canvas = canvasGo.GetComponent<Canvas>();
                if (canvas != null) canvas.renderMode = RenderMode.WorldSpace;
                var canvasRt = (RectTransform)canvasGo.transform;
                float sf = ScaleFactor(ProbeW, ProbeH);
                canvasRt.sizeDelta = new Vector2(ProbeW / sf, ProbeH / sf);
                canvasRt.position = Vector3.zero;
                canvasRt.localScale = Vector3.one;
                Settle(canvasGo);

                float titleBottom = WorldBottom(modal.chrome.title.rectTransform);
                float bodyTop = WorldTop(modal.message.rectTransform);
                float bodyBottom = WorldBottom(modal.message.rectTransform);
                float faceTop = WorldTop((RectTransform)modal.confirm.transform);
                var fillRt = (RectTransform)modal.chrome.content.transform;
                float panelTop = WorldTop(fillRt), panelBottom = WorldBottom(fillRt);

                log.AppendLine("  [confirm-stack] panel [" + panelBottom.ToString("0.#") + ".." + panelTop.ToString("0.#") +
                               "]  titleBottom " + titleBottom.ToString("0.#") +
                               "  body [" + bodyBottom.ToString("0.#") + ".." + bodyTop.ToString("0.#") +
                               "]  faceTop " + faceTop.ToString("0.#") + ".");

                const float Eps = 0.5f;

                // 1. The body must not reach up into the title.
                if (bodyTop > titleBottom + Eps)
                    failures.Add(Tag + " CASE F (WO-1690c, RED-first): the confirm body's top (" +
                                 bodyTop.ToString("0.#") + ") is ABOVE the title's bottom (" +
                                 titleBottom.ToString("0.#") + ") - the two labels overlap and the player reads " +
                                 "the body printed over the gold title. Seat the body between the bands " +
                                 "(ElarionUiKit.SeatConfirmBodyBetweenBands) and bound it with FitBlock.");

                // 2. ...nor down behind the faces.
                if (bodyBottom < faceTop - Eps)
                    failures.Add(Tag + " CASE F (WO-1690c, RED-first): the confirm body's bottom (" +
                                 bodyBottom.ToString("0.#") + ") is BELOW the face band's top (" +
                                 faceTop.ToString("0.#") + ") - its last line is cut behind the buttons. On this " +
                                 "screen that line is 'This cannot be undone.', which is the entire point of the " +
                                 "modal.");

                // 3. ...and it must not escape the panel at all. THE STRONGEST ONE: a rect that
                //    leaves the plate overlaps no sibling, so rules 1-2 can both read clean while
                //    the text floats outside the frame - which is exactly what shipped.
                if (bodyTop > panelTop + Eps || bodyBottom < panelBottom - Eps)
                    failures.Add(Tag + " CASE F (WO-1690c): the confirm body [" + bodyBottom.ToString("0.#") +
                                 ".." + bodyTop.ToString("0.#") + "] escapes its own PanelFill [" +
                                 panelBottom.ToString("0.#") + ".." + panelTop.ToString("0.#") + "]. An unbounded " +
                                 "label spills out of ANY band; the cure is FitBlock, not a taller band.");
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " CASE F threw: " + ex.GetType().Name + " " + ex.Message);
            }
            finally { Kill(canvasGo); }
        }

        private static float WorldTop(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Mathf.Max(Mathf.Max(c[0].y, c[1].y), Mathf.Max(c[2].y, c[3].y));
        }

        private static float WorldBottom(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Mathf.Min(Mathf.Min(c[0].y, c[1].y), Mathf.Min(c[2].y, c[3].y));
        }

        /// <summary>WO-1690b: one confirm face must be authored at MinTouchPx, and its caption band
        /// must seat the FontFloor line. Both are AUTHORING assertions — passing because
        /// ClampMinTouch would have grown it at runtime is exactly what the touch oracle rejects.</summary>
        private static void CheckFace(UnityEngine.UI.Button face, string which, List<string> failures, StringBuilder log)
        {
            if (face == null)
            {
                // A confirm modal built without a cancel is legitimate (BuildConfirmModal takes an
                // empty cancelLabel); this fixture asks for one, so its absence is a fixture fault.
                if (which == "cancel")
                    failures.Add(Tag + " CASE E: the fixture asked for a cancel face and got none, so the " +
                                 "touch-floor assertion below covers one face instead of two.");
                return;
            }

            var rt = (RectTransform)face.transform;
            float w = rt.rect.width, h = rt.rect.height;
            float shortest = Mathf.Min(w, h);
            log.AppendLine("  [face/" + which + "] " + w.ToString("0.#") + "x" + h.ToString("0.#") +
                           " ref px, floor " + ElarionUiKit.MinTouchPx.ToString("F0") + ".");

            if (shortest + 0.5f < ElarionUiKit.MinTouchPx)
            {
                failures.Add(Tag + " CASE E (WO-1690b, RED-first): the confirm modal's " + which + " face " +
                             "resolves " + w.ToString("0.#") + "x" + h.ToString("0.#") + " ref px - shortest " +
                             "side " + shortest.ToString("0.#") + " is " +
                             (ElarionUiKit.MinTouchPx - shortest).ToString("0.#") + " px UNDER MinTouchPx(" +
                             ElarionUiKit.MinTouchPx.ToString("F0") + "). ClampMinTouch would grow it " +
                             "symmetrically at runtime and spill it into both neighbours; author the band AT " +
                             "the floor instead (ElarionUiKit.SeatConfirmFacesAtTouchFloor).");
                return;
            }

            var label = face.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                failures.Add(Tag + " CASE E: the " + which + " face carries no label, so its caption band " +
                             "could not be measured and the glyph assertion below proves nothing.");
                return;
            }

            float band = label.rectTransform.rect.height;
            float need = ElarionUiKit.MinBandPxForFloor(label);
            log.AppendLine("    caption band " + band.ToString("0.#") + " px vs need " + need.ToString("0.#") + " px.");
            if (band + 0.5f < need)
                failures.Add(Tag + " CASE E (WO-1690b): the " + which + " face's caption band is " +
                             band.ToString("0.#") + " px but a FontFloor(" + ElarionUiKit.FontFloor.ToString("F0") +
                             ") line needs " + need.ToString("0.#") + " px - the fit guard will shrink the " +
                             "caption rather than the face growing to hold it.");
        }

        private static string FindLine(CapturingSink sink, string token)
        {
            foreach (var line in sink.Lines)
                if (line != null && line.IndexOf(token, StringComparison.Ordinal) >= 0) return line;
            return null;
        }

        // -----------------------------------------------------------------
        //  Fixture construction.
        // -----------------------------------------------------------------
        private static TMP_Text BuildCulledLabel(Transform parent)
        {
            var go = new GameObject("~FitGuardProbeLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(BandWidthPx, ThinBandPx);

            var t = go.AddComponent<TextMeshProUGUI>();
            var font = ElarionUiKit.ResolveDefaultFont();
            if (font != null) t.font = font;
            t.text = CulledText;
            t.fontSize = 30f;
            t.alignment = TextAlignmentOptions.Left;
            return t;
        }

        private static GameObject BuildCanvas(int w, int h)
        {
            float sf = ScaleFactor(w, h);
            var go = new GameObject("~TextFitGuardProbe", typeof(RectTransform), typeof(Canvas));
            go.hideFlags = HideFlags.HideAndDontSave;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;   // NOT overlay: an overlay canvas in an
                                                         // edit-mode call reports the editor's own
                                                         // 640x480 and every measurement is fiction.
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w / sf, h / sf);
            rt.position = Vector3.zero;
            rt.localScale = Vector3.one;
            return go;
        }

        /// <summary>Mirrors CanvasScaler's ScaleWithScreenSize + MatchWidthOrHeight math.</summary>
        private static float ScaleFactor(int w, int h)
        {
            float logW = Mathf.Log(w / RefW, 2f);
            float logH = Mathf.Log(h / RefH, 2f);
            float sf = Mathf.Pow(2f, Mathf.Lerp(logW, logH, Match));
            return (sf > 0f && !float.IsNaN(sf) && !float.IsInfinity(sf)) ? sf : 1f;
        }

        /// <summary>Force a full synchronous layout pass. Twice, matching the capture harness:
        /// one pass is not always enough for nested rebuilds to settle.</summary>
        private static void Settle(GameObject canvas)
        {
            var rt = canvas.GetComponent<RectTransform>();
            for (int pass = 0; pass < 2; pass++)
            {
                Canvas.ForceUpdateCanvases();
                if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
        }

        private static void Kill(GameObject go)
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
