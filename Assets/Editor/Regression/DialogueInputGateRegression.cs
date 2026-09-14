// =============================================================================
// DialogueInputGateRegression - WO-1714 (hero frozen 35.5s, 11.3s of it mid-wave,
// by a dialogue hidden without ever firing Ended).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Shape: public static bool Run(out string reason)
// - registered into DataRegression.RunAll.
//
// THE DEFECT THIS PINS (proven from a device capture, not inferred -
// docs/handoffs/movement_freeze_logcat_2026-09-14.txt, RCA in the WO):
//   HeroLocomotion.InputSuppressed was raised by DialogueService.Started and cleared
//   ONLY by DialogueService.Ended. DialogueView's combat and WO-795 modal truces HIDE
//   a live dialogue without closing it (each logs "Ended NOT fired" by design), so the
//   hero stayed frozen, mute and defenceless with NOTHING ON SCREEN - no timeout, no
//   escape, no cause visible to the player. The builder truce had had a bypass seam
//   since WO-702 (BuildModeState.DialogueHiddenForBuilder -> BuildModeController.cs:772);
//   the other two never did.
//
// HONEST SCOPE. Cases 1, 2 and 5 are REAL assertions against the shipped pure decision
// functions - they compute the actual values the shipped code computes, with no
// PlayMode session, the same precedent HeroLocomotion.TeleportGuardHeld set. Cases 3,
// 4 and 6 are SOURCE LINTS and say so in their reason strings: they prove the decision
// functions are still CALLED from the shipped paths and that the recovery is still
// loud, which no pure test can. Neither half proves the freeze is gone on a device -
// that is the WO's device repro, recorded in its section 7.
//
// WHAT EACH CASE PINS
//   1 [gate]     EvaluateInputSuppressed releases the gate the moment the HUD reports a
//                truce, and while the stuck latch is set. Regressing it restores the
//                exact captured freeze.
//   2 [bound]    InputSuppressionStuck fires ONLY past the bound with nothing accounting
//                for the suppression. Every known hidden state is exempt on purpose - a
//                builder truce legitimately outlasts the bound (the player is building),
//                so bounding it would false-fire every session.
//   3 [loud]     the recovery still emits FlowTrace.Fail. CLAUDE.md sec.12 forbids a
//                silent failure: a force-clear that said nothing would hide a real
//                upstream dialogue-lifecycle defect behind a working-looking game.
//   4 [seam]     DialogueView still PUBLISHES all three flags, from Update, every frame.
//                A transition-only publish leaves a stale TRUE behind when the view is
//                torn down mid-truce - the very bug class the seam closes.
//   5 [harness]  BreakCaptureHarness's suppression whitelist is BOUNDED, and its bound is
//                strictly longer than the game's own recovery, so the game gets first
//                refusal and the harness only reports what outlived it. Unbounded, this
//                freeze could never self-report and the owner had to flag it by hand -
//                exactly the failure CLAUDE.md sec.14 exists to prevent.
//   6 [wiring]   the watchdog is ticked FIRST in HeroLocomotion.Update, before the
//                suppression branch's own early-return. A tick placed after it could
//                never unstick the state it exists to bound.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Dialogue;

namespace DeNelle.Editor.Regression
{
    public static class DialogueInputGateRegression
    {
        private const string LocoSrc    = "Assets/_Modules/Village/Hero/HeroLocomotion.cs";
        private const string ViewSrc    = "Assets/_Modules/HUD/DialogueView.cs";
        private const string HarnessSrc = "Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs";

        /// <summary>Standalone batch entry - prints its own distinct marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("DIALOGUE_INPUT_GATE_OK - " + reason);
            else Debug.LogError("DIALOGUE_INPUT_GATE_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            Case1Gate(failures);
            Case2Bound(failures);
            Case3Loud(failures);
            Case4Seam(failures);
            Case5Harness(failures);
            Case6Wiring(failures);

            if (failures.Count > 0)
            {
                reason = "WO-1714 dialogue input gate: " + string.Join(" | ", failures);
                return false;
            }
            reason = "WO-1714 dialogue input gate: gate release + " +
                     HeroLocomotion.InputSuppressionInvisibleMaxSeconds.ToString("0") +
                     "s invisible bound + loud FlowTrace.Fail recovery + per-frame HUD seam + " +
                     BreakCaptureHarness.HeroSuppressionProgressMaxSeconds.ToString("0") +
                     "s bounded harness whitelist all intact (6/6 cases).";
            return true;
        }

        // ── 1 [gate] ────────────────────────────────────────────────────────────
        private static void Case1Gate(List<string> failures)
        {
            // raw only -> suppressed (the WO-377 behaviour must survive).
            if (!HeroLocomotion.EvaluateInputSuppressed(true, false, false))
                failures.Add("[gate] a live dialogue with no truce and no stuck latch no longer " +
                             "suppresses input - the WO-377 click-through defect is back.");

            // raw + truce -> RELEASED. This single row IS the captured 11.3s mid-wave freeze.
            if (HeroLocomotion.EvaluateInputSuppressed(true, true, false))
                failures.Add("[gate] input stays suppressed while the HUD reports the dialogue " +
                             "HIDDEN by a truce - that is precisely the captured 2026-09-14 freeze: " +
                             "hero frozen and defenceless mid-wave with nothing on screen.");

            // raw + stuck latch -> RELEASED (the watchdog's recovery must actually take effect).
            if (HeroLocomotion.EvaluateInputSuppressed(true, false, true))
                failures.Add("[gate] the stuck latch no longer releases the gate - the watchdog " +
                             "would log a Fail and change nothing.");

            // no raw latch -> never suppressed, whatever the HUD says.
            if (HeroLocomotion.EvaluateInputSuppressed(false, false, false) ||
                HeroLocomotion.EvaluateInputSuppressed(false, true, false))
                failures.Add("[gate] input reads suppressed with no live dialogue at all.");
        }

        // ── 2 [bound] ───────────────────────────────────────────────────────────
        private static void Case2Bound(List<string> failures)
        {
            float over  = HeroLocomotion.InputSuppressionInvisibleMaxSeconds + 1f;
            float under = HeroLocomotion.InputSuppressionInvisibleMaxSeconds - 1f;

            if (HeroLocomotion.InputSuppressionInvisibleMaxSeconds <= 0f)
                failures.Add("[bound] InputSuppressionInvisibleMaxSeconds is <= 0 - a bound of zero " +
                             "force-clears every dialogue on its opening frame; a negative one is " +
                             "the same defect wearing a number.");

            // THE positive row: suppressed, nothing visible, no truce, past the bound.
            if (!HeroLocomotion.InputSuppressionStuck(true, false, false, false, false, over))
                failures.Add("[bound] a dialogue input gate held past the bound with NO visible panel " +
                             "and NO known truce is no longer called stuck - the unbounded freeze the " +
                             "WO was raised for can ship again.");

            // A VISIBLE panel is never stuck: a player may read one line for minutes.
            if (HeroLocomotion.InputSuppressionStuck(true, true, false, false, false, over))
                failures.Add("[bound] a VISIBLE dialogue is force-cleared past the bound - that " +
                             "resurrects WO-377 click-through on every long conversation.");

            // Each known truce is exempt. The builder row is the one that would false-fire
            // every session (a player builds for longer than the bound routinely).
            if (HeroLocomotion.InputSuppressionStuck(true, false, true, false, false, over))
                failures.Add("[bound] a dialogue the HUD reports hidden FOR COMBAT is reported stuck - " +
                             "that state is accounted for and already released by case 1.");
            if (HeroLocomotion.InputSuppressionStuck(true, false, false, true, false, over))
                failures.Add("[bound] a dialogue the HUD reports hidden FOR A MODAL is reported stuck.");
            if (HeroLocomotion.InputSuppressionStuck(true, false, false, false, true, over))
                failures.Add("[bound] a dialogue hidden for the BUILDER truce is reported stuck - this " +
                             "row false-fires a loud Fail on every ordinary build session.");

            // Under the bound, and with no raw latch, nothing fires.
            if (HeroLocomotion.InputSuppressionStuck(true, false, false, false, false, under))
                failures.Add("[bound] suppression is called stuck BEFORE the bound elapses - the " +
                             "one-frame Started -> BuildUi gap would trip it every dialogue.");
            if (HeroLocomotion.InputSuppressionStuck(false, false, false, false, false, over))
                failures.Add("[bound] suppression is called stuck with no raw latch raised at all.");
        }

        // ── 3 [loud] ────────────────────────────────────────────────────────────
        private static void Case3Loud(List<string> failures)
        {
            if (!TryRead(LocoSrc, failures, out string loco)) return;
            string body = MethodBody(loco, "private void TickInputSuppressionWatchdog()");
            if (body == null)
            {
                failures.Add("[loud] source lint: " + LocoSrc + " no longer declares " +
                             "TickInputSuppressionWatchdog - the bound has been deleted outright.");
                return;
            }
            if (!body.Contains("FlowTrace.Fail"))
                failures.Add("[loud] source lint: the stuck-gate recovery in " + LocoSrc +
                             " no longer emits FlowTrace.Fail. CLAUDE.md sec.12 forbids a silent " +
                             "recovery: force-clearing quietly hides a real dialogue-lifecycle defect " +
                             "and the next seat starts from zero evidence.");
            if (!body.Contains("WO-1714"))
                failures.Add("[loud] source lint: the recovery Fail no longer names WO-1714, so a " +
                             "future capture cannot be traced back to this defect.");
            if (!body.Contains("_inputSuppressStuckLatched = true"))
                failures.Add("[loud] source lint: the recovery no longer LATCHES - it would re-Fail " +
                             "every frame and flood the logcat ring that carries the evidence " +
                             "(memory: logcat-ring-buffer-destroys-evidence).");
            if (!body.Contains("InputSuppressionStuck("))
                failures.Add("[loud] source lint: the watchdog no longer decides through the tested " +
                             "InputSuppressionStuck function - case 2 would pass over dead code.");
        }

        // ── 4 [seam] ────────────────────────────────────────────────────────────
        private static void Case4Seam(List<string> failures)
        {
            if (!TryRead(ViewSrc, failures, out string view)) return;

            string update = MethodBody(view, "private void Update()");
            if (update == null || !update.Contains("PublishGateState()"))
                failures.Add("[seam] source lint: " + ViewSrc + " no longer calls PublishGateState() " +
                             "from Update - Village would read a stale truce state forever.");

            string publish = MethodBody(view, "private void PublishGateState()");
            if (publish == null)
            {
                failures.Add("[seam] source lint: " + ViewSrc + " no longer declares PublishGateState - " +
                             "the combat + WO-795 modal truces are back to having no bypass seam, which " +
                             "is the WO-1714 root defect.");
            }
            else
            {
                if (!publish.Contains("HiddenForCombat"))
                    failures.Add("[seam] source lint: PublishGateState no longer publishes HiddenForCombat.");
                if (!publish.Contains("HiddenForModal"))
                    failures.Add("[seam] source lint: PublishGateState no longer publishes HiddenForModal.");
                if (!publish.Contains("PanelVisible"))
                    failures.Add("[seam] source lint: PublishGateState no longer publishes PanelVisible - " +
                                 "the watchdog's only way to tell 'player is reading' from 'stuck' is gone.");
            }

            string disable = MethodBody(view, "private void OnDisable()");
            if (disable == null || !disable.Contains("DialogueGateState.Clear()"))
                failures.Add("[seam] source lint: " + ViewSrc + " no longer clears DialogueGateState on " +
                             "disable - a view torn down mid-truce leaves a stale TRUE that holds the " +
                             "input gate open forever.");

            // The seam's own composition must still release on either truce.
            DialogueGateState.Clear();
            try
            {
                DialogueGateState.HiddenForCombat = true;
                if (!DialogueGateState.HiddenByTruce)
                    failures.Add("[seam] DialogueGateState.HiddenByTruce ignores HiddenForCombat.");
                DialogueGateState.HiddenForCombat = false;
                DialogueGateState.HiddenForModal = true;
                if (!DialogueGateState.HiddenByTruce)
                    failures.Add("[seam] DialogueGateState.HiddenByTruce ignores HiddenForModal.");
            }
            finally { DialogueGateState.Clear(); }

            if (DialogueGateState.HiddenByTruce || DialogueGateState.PanelVisible)
                failures.Add("[seam] DialogueGateState.Clear() does not clear every flag.");
        }

        // ── 5 [harness] ─────────────────────────────────────────────────────────
        private static void Case5Harness(List<string> failures)
        {
            float bound = BreakCaptureHarness.HeroSuppressionProgressMaxSeconds;

            if (bound <= HeroLocomotion.InputSuppressionInvisibleMaxSeconds)
                failures.Add("[harness] the harness whitelist bound (" + bound.ToString("0") +
                             "s) is not longer than the game's own recovery (" +
                             HeroLocomotion.InputSuppressionInvisibleMaxSeconds.ToString("0") +
                             "s) - the harness would report freezes the game was about to fix itself.");

            if (!BreakCaptureHarness.SuppressionCountsAsProgress(bound - 1f))
                failures.Add("[harness] an ordinary scripted beat shorter than the bound no longer " +
                             "counts as progress - every dialogue line becomes a false softlock capture.");
            if (BreakCaptureHarness.SuppressionCountsAsProgress(bound + 1f))
                failures.Add("[harness] hero input suppression past the bound STILL counts as progress - " +
                             "this is the 2026-09-14 blind spot verbatim: the freeze could not " +
                             "self-report and the owner had to flag it by hand (CLAUDE.md sec.14).");

            if (!TryRead(HarnessSrc, failures, out string harness)) return;
            if (!harness.Contains("SuppressionCountsAsProgress(_heroSuppressedHeld)"))
                failures.Add("[harness] source lint: " + HarnessSrc + "'s watchdog no longer gates its " +
                             "suppression whitelist on SuppressionCountsAsProgress - the assertions " +
                             "above would pass over dead code while the shipped branch stayed " +
                             "unconditionally blind.");
        }

        // ── 6 [wiring] ──────────────────────────────────────────────────────────
        private static void Case6Wiring(List<string> failures)
        {
            if (!TryRead(LocoSrc, failures, out string loco)) return;
            // Deliberately NOT brace-matched: HeroLocomotion.Update is hundreds of lines of
            // interpolated trace strings, and this repo has already been bitten once by a brace
            // walk with no interpolated-string model (CLAUDE.md sec.1, WO-1096). Anchoring on the
            // Update signature and comparing offsets needs no brace model at all.
            string src = StripComments(loco);
            int updateAt = src.IndexOf("private void Update()", StringComparison.Ordinal);
            if (updateAt < 0)
            {
                failures.Add("[wiring] source lint: could not locate HeroLocomotion.Update.");
                return;
            }
            int tick = src.IndexOf("TickInputSuppressionWatchdog();", updateAt, StringComparison.Ordinal);
            if (tick < 0)
            {
                failures.Add("[wiring] source lint: HeroLocomotion.Update no longer ticks " +
                             "TickInputSuppressionWatchdog - the bound is declared but never runs.");
                return;
            }
            int gate = src.IndexOf("if (InputSuppressed", updateAt, StringComparison.Ordinal);
            if (gate >= 0 && tick > gate)
                failures.Add("[wiring] source lint: the watchdog tick sits AFTER the InputSuppressed " +
                             "early-return in HeroLocomotion.Update - that branch returns, so the tick " +
                             "could never run on the very frames the hero is stuck.");

            // The composed property must still be the one the whole input surface reads.
            if (!loco.Contains("EvaluateInputSuppressed(_inputSuppressRaw"))
                failures.Add("[wiring] source lint: HeroLocomotion.InputSuppressed no longer composes " +
                             "through EvaluateInputSuppressed - case 1 would pass over dead code while " +
                             "the shipped gate went back to a raw unbounded latch.");
        }

        // ── helpers ─────────────────────────────────────────────────────────────
        private static bool TryRead(string path, List<string> failures, out string text)
        {
            text = null;
            try
            {
                if (!File.Exists(path))
                {
                    failures.Add("[io] missing source file " + path + " - the suite cannot lint what " +
                                 "is not there; a move needs this suite's path updated in the same commit.");
                    return false;
                }
                text = File.ReadAllText(path);
                return true;
            }
            catch (Exception e)
            {
                failures.Add("[io] could not read " + path + " (" + e.GetType().Name + ": " + e.Message + ")");
                return false;
            }
        }

        /// <summary>Brace-matched body of the method whose signature line is <paramref name="signature"/>.
        /// Comments are stripped first so a signature quoted in a comment cannot be matched, and so a
        /// brace inside a comment cannot unbalance the walk. Null when the signature is absent.</summary>
        private static string MethodBody(string source, string signature)
        {
            string src = StripComments(source);
            int at = src.IndexOf(signature, StringComparison.Ordinal);
            if (at < 0) return null;
            int open = src.IndexOf('{', at + signature.Length);
            if (open < 0) return null;
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
            return null;
        }

        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            src = Regex.Replace(src, @"//[^\n]*", " ");
            return src;
        }
    }
}
