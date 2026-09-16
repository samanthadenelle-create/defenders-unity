// =============================================================================
// HonestFeedbackClaimOnceRegression [honest-feedback-once] -- WO-1432 acceptance 2:
// claiming the thank-you twice is IMPOSSIBLE, and the refusal is TRACED.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Shares HonestFeedbackFixture (declared in
// HonestFeedbackGrantRegression.cs) so the two suites cannot drift apart.
//
// -----------------------------------------------------------------------------
// WHY IT ASSERTS THE TRACE LINE AND NOT JUST THE RETURN VALUE
// -----------------------------------------------------------------------------
// WO-1432 section 5 asks for a "traced no-op", and the two halves are different
// claims. A method could return AlreadyClaimed while quietly moving resources, and
// it could refuse correctly while saying nothing at all -- and a silent refusal is
// exactly the state CLAUDE.md sec.12 forbids ("shows nothing, no error" must be
// impossible). So all three are measured separately:
//   1. the OUTCOME is AlreadyClaimed;
//   2. all THREE wallet deltas are ZERO, measured off the same fields the grant
//      writes (GameState.Wood / .Iron / .Resources.Stone) -- not inferred from (1);
//   3. a [Flow:HonestFeedback] line carrying HonestFeedbackGrant.AlreadyClaimedTrace
//      was actually emitted.
// (3) is captured by swapping FlowTrace.Sink for a recorder that forwards to the
// previous sink -- deterministic, and it does not race Unity's log callback.
//
// This is also why AlreadyClaimedTrace is a public const rather than an inline
// string: the message is a CONTRACT between the seam and this oracle, so an edit
// that guts the trace fails the build instead of quietly removing the evidence a
// future repeat-claim bug would be diagnosed from.
//
// Marker: HONEST_FEEDBACK_ONCE_OK / HONEST_FEEDBACK_ONCE_FAIL. Expected: GREEN.
//
// REVERT RECIPE (RED): delete the HasClaimed() early-return at the top of
// HonestFeedbackGrant.TryApply. The second call then pays a second 1000/1000/1000,
// cases 1 and 2 fire together, and the panel becomes a repeat-claim exploit --
// which is the whole reason WO-1432 section 3 allows exactly one grant seam.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "honest-feedback-once suite", () => { if (!HonestFeedbackClaimOnceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[honest-feedback-once] " + r); });
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Feedback;

namespace DeNelle.Editor
{
    /// <summary>Proves a second thank-you claim moves nothing and says so out loud.</summary>
    public static class HonestFeedbackClaimOnceRegression
    {
        private const string Tag = "[honest-feedback-once]";
        private const string SaveKey = "dotr-save";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- HONEST FEEDBACK CLAIM-ONCE (WO-1432: a second TryApply is a traced no-op) ---");

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;

            GameStateService priorGss = GameStateService.Instance;
            object priorEcon = HonestFeedbackFixture.GetInstance(typeof(EconomyService));
            var priorSink = FlowTrace.Sink;

            GameObject gssGo = null, econGo = null;
            GameState throwaway = null;
            var recorder = new RecordingSink(priorSink);
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (honest-feedback-once oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!HonestFeedbackFixture.InstallState(gss, throwaway))
                {
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "HONEST FEEDBACK ONCE", "GameStateService state seam not reflectable (needs fleet)");
                }

                econGo = new GameObject("EconomyService (honest-feedback-once oracle)");
                var econ = econGo.AddComponent<EconomyService>();
                HonestFeedbackFixture.SetInstance(typeof(EconomyService), econ);

                // Start from a clean, unclaimed save.
                if (throwaway.SeenTutorials != null)
                {
                    throwaway.SeenTutorials[HonestFeedbackKeys.GrantClaimedKey] = false;
                    throwaway.SeenTutorials[HonestFeedbackKeys.OfferedKey] = false;
                }

                // ── FIRST claim: must pay, and must set the flag ───────────────
                var first = HonestFeedbackGrant.TryApply(out var firstApplied);
                log.AppendLine($"  first claim: outcome={first} applied W{firstApplied.Wood}/" +
                               $"F{firstApplied.Stone}/I{firstApplied.Iron}");
                if (first != ThankYouGrantOutcome.Applied)
                {
                    failures.Add(Tag + " [first-claim-pays] the FIRST TryApply returned " + first +
                                 " on a clean save -- the second-claim case below cannot mean anything if the " +
                                 "first one never paid. BROKEN FIXTURE, not a passing guard");
                    reason = Finish(failures, log);
                    return false;
                }
                if (!HonestFeedbackGrant.HasClaimed())
                {
                    failures.Add(Tag + " [flag-set-after-pay] TryApply reported Applied but HasClaimed() is " +
                                 "still false -- the one-time flag (" + HonestFeedbackKeys.GrantClaimedKey +
                                 ") was not written, so nothing stops a second claim");
                    reason = Finish(failures, log);
                    return false;
                }

                // ── SECOND claim: must move nothing, and must SAY so ───────────
                int wBefore = throwaway.Wood, fBefore = throwaway.Resources.Stone, iBefore = throwaway.Iron;

                FlowTrace.Sink = recorder;
                recorder.Lines.Clear();
                var second = HonestFeedbackGrant.TryApply(out var secondApplied);
                FlowTrace.Sink = priorSink;

                int dWood = throwaway.Wood - wBefore;
                int dFood = throwaway.Resources.Stone - fBefore;
                int dIron = throwaway.Iron - iBefore;
                log.AppendLine($"  second claim: outcome={second} measured deltas wood=+{dWood} " +
                               $"stone(Food)=+{dFood} iron=+{dIron}; trace lines captured={recorder.Lines.Count}");

                // 1. the outcome
                if (second != ThankYouGrantOutcome.AlreadyClaimed)
                    failures.Add(Tag + " [second-claim-refused] the SECOND TryApply returned " + second +
                                 " -- expected AlreadyClaimed. One flag, one seam (WO-1432 section 3); anything " +
                                 "else is a repeat-claim exploit");

                // 2. the wallet, MEASURED -- never inferred from (1)
                if (dWood != 0 || dFood != 0 || dIron != 0)
                    failures.Add(Tag + " [second-claim-moves-nothing] the second claim MOVED the wallet by W" +
                                 dWood + "/stone" + dFood + "/I" + dIron + ". A refused claim must be a no-op " +
                                 "in the wallet, not only in the return value");
                if (secondApplied.Wood != 0 || secondApplied.Stone != 0 || secondApplied.Iron != 0)
                    failures.Add(Tag + " [second-claim-reports-nothing] the second claim reported an applied " +
                                 "basket of W" + secondApplied.Wood + "/F" + secondApplied.Stone + "/I" +
                                 secondApplied.Iron + " -- a refused claim must report an empty basket, or a " +
                                 "caller will announce resources nobody received");

                // 3. the trace -- a silent refusal is the banned state (CLAUDE.md sec.12)
                bool traced = false;
                foreach (var line in recorder.Lines)
                {
                    if (line != null &&
                        line.IndexOf(HonestFeedbackGrant.AlreadyClaimedTrace, StringComparison.Ordinal) >= 0)
                    { traced = true; break; }
                }
                if (!traced)
                    failures.Add(Tag + " [second-claim-is-traced] the refusal emitted no line carrying " +
                                 "HonestFeedbackGrant.AlreadyClaimedTrace. A no-op that says nothing is " +
                                 "indistinguishable from a grant that silently failed, and the next repeat-claim " +
                                 "bug would start from zero evidence. Captured lines: " +
                                 (recorder.Lines.Count == 0 ? "none" : string.Join(" | ", recorder.Lines.ToArray())));
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " oracle threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                FlowTrace.Sink = priorSink;
                if (econGo != null) UnityEngine.Object.DestroyImmediate(econGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                HonestFeedbackFixture.SetInstance(typeof(EconomyService), priorEcon);
                HonestFeedbackFixture.SetGssInstance(priorGss);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave); else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        /// <summary>
        /// Records every FlowTrace line AND forwards it to the sink that was installed before,
        /// so running this oracle never costs the run its normal log output.
        /// </summary>
        private sealed class RecordingSink : ITraceSink
        {
            private readonly ITraceSink _inner;
            internal readonly List<string> Lines = new List<string>();
            internal RecordingSink(ITraceSink inner) { _inner = inner; }
            public void Info(string line) { Lines.Add(line); _inner?.Info(line); }
            public void Warn(string line) { Lines.Add(line); _inner?.Warn(line); }
            public void Error(string line) { Lines.Add(line); _inner?.Error(line); }
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "HONEST_FEEDBACK_ONCE_OK");
                return "HONEST FEEDBACK ONCE OK -- the first TryApply paid and set " +
                       HonestFeedbackKeys.GrantClaimedKey + "; the second returned AlreadyClaimed, moved the " +
                       "wallet by exactly zero on all three axes, reported an empty basket, and emitted the " +
                       "contracted [Flow:HonestFeedback] no-op line";
            }
            string reason = "honest-feedback-once: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "HONEST_FEEDBACK_ONCE_FAIL: " + reason);
            return reason;
        }
    }
}
