// =============================================================================
// FtueModalDeferralRegression [ftue-modal-deferral]
//   Markers: FTUE_MODAL_DEFERRAL_OK / FTUE_MODAL_DEFERRAL_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Data + source only - no scene, no play mode.
//
// THE PIN WO-1090 SHIPPED WITHOUT. The fix landed in 6a5c7a36d and its own RESULT
// file says, verbatim: "No regression pin was added for the deferral or the modal
// exclusion." This suite is that pin. It maps to two acceptance items of
// WorkOrders/WORK_ORDER_1090_welcome_back_over_ftue_skips_a_tutorial_beat.md:
//
//   item 1  "a fresh FTUE start does not leave a welcome-back on screen; the report
//           appears after the chain finishes"       -> cases D, E, F
//   item 3  "a beat held under a modal shows non-zero excluded time ... and is not
//           rescue-skipped"                          -> case G
//
// (Items 2 and 5 - "SKIP_TOP_HIT_BLOCKED no longer fires" and the owner felt-test -
// are RUNTIME facts about a driven FTUE. Nothing here can stand in for them and this
// suite does not pretend to: see the honesty note at the bottom of the RESULT append.)
//
// THE DEFECT, from the capture (F8 seq 4682/4705, 2026-09-05, quoted in the producer's
// own in-code RCA at OfflineHarvestService.cs:1340-1360): at hub load TutorialFlow is
// ARMED but not STARTED, so through the Settle window _step is null and the NARROW key
// (IsAwaitingDialogue) reads FALSE in exactly the frames the welcome-back modal opens
// in. The report then sat at sortingOrder 32020 over the ONE skip control at 6000, the
// founding beat was charged 120 s it could not spend, and the watchdog rescued-and-
// SKIPPED it. Two halves were needed and both are pinned here: the modal is taken away
// and RE-PARKED (half A), and the beat is not CHARGED for modal-held seconds (half B).
//
// WHY THE SHAPE IS PART-RUNTIME, PART-LINT, AND WHERE EACH IS HONEST:
//   * The three statics the fix introduced are PUBLIC and side-effect-free, so cases A
//     and B CONSUME them for real (they are called, not grepped) - which also proves
//     they stay reachable from outside the Village assembly, the property the deferral
//     depends on.
//   * The park / re-park / release branches live in a private MonoBehaviour Update on a
//     service that needs a live GameState, a hub scene and an away window. That cannot
//     be driven from batchmode, so cases C-G are SOURCE-DECIDABLE and are written as
//     CONSUMPTION assertions - a value flowing into the field that stores it - never as
//     "the symbol exists". Each names the mutation that reds it in its failure text.
//
// NO HOLLOW PASS (CLAUDE.md sec.12, INSTRUMENTATION_STANDARD sec.8.5): a producer file
// that cannot be read is the FIXTURE, not an option - it FAILS here, naming the path.
// There is exactly one `return` in Run and it is the failure count.
//
// Standalone batch entry:
//   -Method DeNelle.Editor.Regression.FtueModalDeferralRegression.RunStandalone
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Village;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1090: the welcome-back report never owns the screen while the mandatory
    /// FTUE chain is live, and a beat is never charged for the seconds it does.</summary>
    public static class FtueModalDeferralRegression
    {
        private const string MarkerOk   = "FTUE_MODAL_DEFERRAL_OK";
        private const string MarkerFail = "FTUE_MODAL_DEFERRAL_FAIL";

        private const string HarvestSrc  = "_Modules/Village/Harvest/OfflineHarvestService.cs";
        private const string TutorialSrc = "_Modules/Village/Tutorial/V2/TutorialFlow.cs";

        [MenuItem("Defenders/Regression/FTUE Modal Deferral")]
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason);
            else Debug.LogError(reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            var notes = new List<string>();

            // ── A [seam] the popup's open-state is readable and starts CLOSED ──
            // Consumed, not grepped: if IsOpen were re-implemented as a mirrored bool that
            // latches (the shape the producer comment at WelcomeBackPopup.cs:41 warns against),
            // or hard-wired true, this reds. Both statics are what the deferral keys off.
            // ORDER-DEPENDENCE, DECLARED. These two statics are process-wide. If some earlier suite in
            // the same RunAll left a popup alive, this case cannot make the read - and it must say so
            // rather than FAIL (that would red a healthy tree for another lane's fixture) or pass
            // (that would assert nothing). Checked 2026-09-09: no suite under Assets/Editor/Regression
            // calls WelcomeBackPopup.Show; the only callers are in UICaptureLaunch, a different entry
            // point. The declaration is here because "no caller today" is not an invariant.
            if (DeNelle.Village.UI.WelcomeBackPopup.IsOpen)
                notes.Add(RegressionOutcome.PartialSkip("seam",
                    "a WelcomeBackPopup was already alive when this suite ran (left by an earlier suite in the same " +
                    "batch), so the closed-state read of IsOpen/ActiveResult could not be made. The source cases " +
                    "below still ran"));
            else if (DeNelle.Village.UI.WelcomeBackPopup.ActiveResult != null)
                failures.Add("[seam] WelcomeBackPopup.ActiveResult handed back a result while IsOpen is false. " +
                             "The re-park branch reads ActiveResult BEFORE dismissing; a stale result there " +
                             "re-parks a reveal the player already saw.");
            else
                log.AppendLine("  [seam] WelcomeBackPopup.IsOpen=false / ActiveResult=null with no popup alive");

            // ── B [chain-key] the broad key fails OPEN, and names the Settle window honestly ──
            // Headless there is no TutorialFlow instance, so the getter must answer FALSE (the
            // producer's own documented fail-open rule). RED-first: turning any of its guards
            // into `return true` reds this, and a fail-CLOSED key would suppress the wave loop
            // in every scene that has no tutorial - WaveLoopSuppressedForTutorial delegates here.
            // Same order-dependence declaration. TutorialCoachEscalationRegression builds a TutorialFlow
            // on a throwaway GameObject and DestroyImmediates it in a finally (checked 2026-09-09), so
            // s_instance should be null here - but an instance surviving that teardown is an ENVIRONMENT
            // fact about the batch, not a defect in this fix, and it is declared rather than failed.
            string stateLine = TutorialFlow.LiveChainStateLine;
            bool noFlow = stateLine == "<no flow>";
            if (!noFlow)
                notes.Add(RegressionOutcome.PartialSkip("chain-key",
                    "a TutorialFlow instance was already live when this suite ran (LiveChainStateLine='" +
                    stateLine + "'), so the fail-open read of IsMandatoryChainLive could not be made. The source " +
                    "cases below still ran"));
            else if (TutorialFlow.IsMandatoryChainLive)
                failures.Add("[chain-key] TutorialFlow.IsMandatoryChainLive reads TRUE with no live flow. It " +
                             "must fail OPEN on a missing flag/state/instance - the same rule as the ambient " +
                             "gate. WaveLoopSuppressedForTutorial delegates to it, so a fail-CLOSED key " +
                             "silently holds the wave loop shut wherever no tutorial exists.");
            else
                log.AppendLine("  [chain-key] IsMandatoryChainLive=false with no live flow (fail-open held)");

            if (string.IsNullOrEmpty(stateLine))
                failures.Add("[chain-key] TutorialFlow.LiveChainStateLine returned null/empty. It is null-safe by " +
                             "contract and must name the Settle window honestly ('<no flow>' / 'Settling/<none>'). It is " +
                             "read INSIDE the re-park trace's interpolated message; an empty value there turns " +
                             "the one line that says WHY a reveal was taken away into a blank.");
            else
                log.AppendLine("  [chain-key] LiveChainStateLine with no flow = '" + stateLine + "'");

            // ── C [one-predicate] the getter keeps the shape its capture forced ──
            string tutorial = ReadCode(TutorialSrc);
            if (tutorial == null)
            {
                failures.Add("[fixture] " + TutorialSrc + " is MISSING - the FTUE clock and the chain key cannot " +
                             "be verified. The file under test is not an optional dependency.");
            }
            else
            {
                if (!tutorial.Contains("public static bool WaveLoopSuppressedForTutorial => IsMandatoryChainLive;"))
                    failures.Add("[one-predicate] WaveLoopSuppressedForTutorial no longer DELEGATES to " +
                                 "IsMandatoryChainLive. Two copies of the same three checks is how the welcome-back " +
                                 "key and the wave-loop key drift apart - the exact duplicated-state failure " +
                                 "CLAUDE.md sec.5/sec.8 record.");
                else
                    log.AppendLine("  [one-predicate] the wave-loop key delegates to the one chain predicate");

                // Scoped to the getter, NOT the file: '_phase != Phase.Idle' is a legitimate,
                // unrelated comparison elsewhere in TutorialFlow (a whole-file rule would fire on it
                // and red a healthy tree - the false-positive trap RegressionMarkerRegression RULE 1
                // was rewritten to escape).
                string chainKey = Between(tutorial, "public static bool IsMandatoryChainLive",
                                                    "public static string LiveChainStateLine");
                if (chainKey == null)
                    failures.Add("[chain-key-shape] could not isolate the IsMandatoryChainLive getter ahead of " +
                                 "LiveChainStateLine. Both were added by this fix; if either was moved or renamed, " +
                                 "re-point this case in the SAME change rather than deleting it.");
                else if (!chainKey.Contains("return flow._phase != Phase.Finished;"))
                    failures.Add("[chain-key-shape] IsMandatoryChainLive no longer ends 'return flow._phase != " +
                                 "Phase.Finished;'. That is the whole fix: Idle counts as LIVE because s_instance " +
                                 "is published in Awake while _phase is not set until Start, which IS the hub-load " +
                                 "window the modal opens in (F8 seq 4682).");
                else if (chainKey.Contains("_phase != Phase.Idle"))
                    failures.Add("[chain-key-shape] IsMandatoryChainLive has been WEAKENED with a '!= Phase.Idle' " +
                                 "term. The producer's own doc says do NOT do this: Idle can only mean 'Start has " +
                                 "not run yet', so excluding it re-opens the Settle window the narrow WO-1414 C key " +
                                 "already failed to close.");
                else
                    log.AppendLine("  [chain-key-shape] the chain key still treats Idle as LIVE (Settle window covered)");

                // ── G [step-clock] item 3: the modal is the THIRD exclusion, and it is attributed ──
                if (!tutorial.Contains("bool modal   = DeNelle.Village.UI.WelcomeBackPopup.IsOpen;") &&
                    !tutorial.Contains("bool modal = DeNelle.Village.UI.WelcomeBackPopup.IsOpen;"))
                    failures.Add("[step-clock] TickStepClock no longer reads WelcomeBackPopup.IsOpen. Without it a " +
                                 "beat is charged for seconds the player physically could not spend - the modal is " +
                                 "neither the builder nor a frozen clock, so it falls through both existing gates.");
                else if (!tutorial.Contains("excluded: builder || frozen || modal"))
                    failures.Add("[step-clock] the modal flag is computed but NOT passed to StepClock.Tick's " +
                                 "'excluded' argument. A read that never reaches the consumer is the defect, not " +
                                 "the fix: the breakdown would still read 'excluded 0s' under the report.");
                else if (!tutorial.Contains("if (modal && !builder && !frozen) _modalExcludedSeconds += accepted;"))
                    failures.Add("[step-clock] the modal slice is no longer attributed only when the modal is the " +
                                 "ACTUAL reason the frame was excluded. Dropping the !builder/!frozen terms lets " +
                                 "'of which modal' overstate itself on frames the builder would have excluded " +
                                 "anyway, and the STEP-STUCK breakdown then names the wrong owner.");
                else
                    log.AppendLine("  [step-clock] modal is the third exclusion and its slice is attributed exactly");

                if (!tutorial.Contains("secondsExcludedModal"))
                    failures.Add("[step-clock] the tutorial_step_drop analytics event no longer carries " +
                                 "secondsExcludedModal. That field is the only way a drop recorded in the wild can " +
                                 "be told apart from a genuine stall - acceptance item 3 is measured, not felt.");
                else
                    log.AppendLine("  [step-clock] the drop analytic carries the modal-excluded slice");

                if (!tutorial.Contains("_modalExcludedSeconds = 0f;"))
                    failures.Add("[step-clock] _modalExcludedSeconds is never reset. It is a PER-STEP reason tag " +
                                 "living beside a per-step clock; a total that accumulates across steps reports " +
                                 "modal seconds against a beat that never saw the modal.");
                else
                    log.AppendLine("  [step-clock] the modal slice resets per step, like the clock it annotates");

                if (!tutorial.Contains("watchdog-modal-pause"))
                    failures.Add("[step-clock] the STEP-STUCK watchdog no longer stands down while the report owns " +
                                 "the screen. Excluding the frame from the CHARGED budget and refusing to RESCUE " +
                                 "are two different guarantees; acceptance item 3 needs both ('shows non-zero " +
                                 "excluded time AND is not rescue-skipped').");
                else
                    log.AppendLine("  [step-clock] the watchdog pauses under the modal instead of rescue-skipping");
            }

            // ── D/E/F [deferral] half A: park, re-park, and release on the SAME key ──
            string harvest = ReadCode(HarvestSrc);
            if (harvest == null)
            {
                failures.Add("[fixture] " + HarvestSrc + " is MISSING - the welcome-back deferral cannot be " +
                             "verified. The file under test is not an optional dependency.");
            }
            else
            {
                // The re-park must live in the frame loop: the chain can go live UNDER an already-open
                // report, which is the direction the original fix was missing entirely.
                string update = Between(harvest, "private void Update()", "private void TryShowPopup(");
                if (update == null)
                {
                    failures.Add("[re-park] could not isolate OfflineHarvestService.Update() ahead of TryShowPopup. " +
                                 "The re-park is a per-frame duty; if the method was renamed or reordered, re-point " +
                                 "this case in the SAME change rather than deleting it.");
                }
                else if (!update.Contains("WelcomeBackPopup.IsOpen && TutorialFlow.IsMandatoryChainLive"))
                {
                    failures.Add("[re-park] Update() no longer detects a report that is ALREADY on screen when the " +
                                 "mandatory chain goes live. Parking before the modal opens is only half the " +
                                 "ticket: on F8 seq 4682 the modal opened FIRST, in the Settle window, and the " +
                                 "chain started underneath it.");
                }
                else if (!update.Contains("WelcomeBackPopup.ActiveResult") ||
                         !update.Contains("WelcomeBackPopup.DismissIfOpen("))
                {
                    failures.Add("[re-park] the on-screen report is not lifted (ActiveResult) and dismissed " +
                                 "(DismissIfOpen) before the chain runs. Dismissing WITHOUT reading the result " +
                                 "first destroys the reveal; reading without dismissing leaves the skip control " +
                                 "covered.");
                }
                else if (!update.Contains("_deferredReveal = onScreen;") ||
                         !update.Contains("_tutorialDeferred = true;"))
                {
                    failures.Add("[re-park] the dismissed report is not RE-PARKED into the existing " +
                                 "_deferredReveal/_tutorialDeferred pair. A dismiss with no re-park is a SWALLOWED " +
                                 "reveal - the player's away haul is banked but never shown, and nothing says so.");
                }
                else
                {
                    log.AppendLine("  [re-park] an open report is lifted, dismissed and re-parked when the chain goes live");
                }

                // No silent failure on the one branch where a reveal could vanish (CLAUDE.md sec.12).
                if (update != null && update.Contains("DismissIfOpen(") &&
                    !update.Contains("the open report carried NO result"))
                    failures.Add("[re-park] the 'dismissed but nothing to park' branch no longer traces. That is the " +
                                 "ONE path where a reveal can disappear; an untraced one is a silent failure by " +
                                 "CLAUDE.md sec.12, not an edge case.");

                // The park and the release must key off the SAME predicate or they flap once per frame.
                if (!harvest.Contains("if (TutorialFlow.IsMandatoryChainLive) return;"))
                    failures.Add("[same-key] the deferral RELEASE no longer gates on IsMandatoryChainLive. " +
                                 "Releasing on the narrow !IsAwaitingDialogue while parking on the broad key flaps " +
                                 "release -> show -> park every frame through the Settle window, which is worse " +
                                 "than the bug it replaced.");
                else
                    log.AppendLine("  [same-key] park and release read the same chain predicate");

                // Half A's other direction: the modal must never OPEN over a live chain.
                string show = Between(harvest, "private void TryShowPopup(", "private void OnSceneLoadedForReveal");
                if (show == null)
                    failures.Add("[refuse-open] could not isolate OfflineHarvestService.TryShowPopup(). Re-point " +
                                 "this case in the same change that renames it.");
                else if (!show.Contains("if (TutorialFlow.IsMandatoryChainLive)"))
                    failures.Add("[refuse-open] TryShowPopup no longer refuses to open over a live mandatory chain, " +
                                 "or has gone back to the narrow IsAwaitingDialogue key. The narrow key reads FALSE " +
                                 "in the Settle window - it CANNOT close the window that produced seq 4682.");
                else if (!show.Contains("_tutorialDeferred = true;"))
                    failures.Add("[refuse-open] TryShowPopup refuses the open but does not park the result for " +
                                 "later. A refusal that drops the reveal loses the report entirely instead of " +
                                 "delaying it.");
                else
                    log.AppendLine("  [refuse-open] TryShowPopup defers rather than opening over the chain");
            }

            string noteText = notes.Count == 0 ? string.Empty : "\n  " + string.Join("\n  ", notes);
            reason = failures.Count == 0
                ? MarkerOk + " welcome-back defers to the mandatory FTUE chain (park + re-park + same-key release) " +
                  "and the modal is excluded from - and attributed in - the step clock\n" +
                  log.ToString().TrimEnd() + noteText
                : MarkerFail + " x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }

        /// <summary>Read a tracked producer source file. Returns null when it is absent, which every
        /// caller treats as a FAILURE (fixture rule, INSTRUMENTATION_STANDARD sec.8.5) - never a skip.</summary>
        private static string ReadCode(string assetsRelativePath)
        {
            string full = Path.Combine(Application.dataPath, assetsRelativePath);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        /// <summary>The slice of <paramref name="src"/> between two anchors, so a case can assert a
        /// branch lives in the METHOD that must run it rather than anywhere in the file. Null when
        /// either anchor is gone or out of order - reported, never silently treated as a pass.</summary>
        private static string Between(string src, string startToken, string endToken)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int a = src.IndexOf(startToken, System.StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(endToken, a + startToken.Length, System.StringComparison.Ordinal);
            if (b < 0) return null;
            return src.Substring(a, b - a);
        }
    }
}
