// =============================================================================
// TutorialCompletionPublisherRegression [tutorial-completion-publisher]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only source lint + json read).
//
// WO-1300 acceptance criterion 4. THE CAPTURED DEFECT (F8 seq 4376 + seq 4370,
// 2026-09-02, scene Main_Castle_Overworld), quoted from the owner's inbox:
//
//     [Flow:Tutorial] STEP-STUCK :: founding_walk - no 'hero.reached:guide_gate'
//                     after 120s in-step ... RESCUED via watchdog and recorded as
//                     SKIPPED - the step was NOT completed, its outro is suppressed
//     [Flow:Tutorial] STEP-STUCK :: founding_defend - no 'wave.tutorial_band_repelled'
//                     after 120s in-step ... RESCUED via watchdog and recorded as SKIPPED
//
// Two beats of the OPENING story were silently walked past. Retention is the
// business problem; a tutorial a new player cannot get out of is the most expensive
// defect in the game, so the completion signals get a standing structural pin.
//
// WHAT THIS SUITE PROVES HEADLESSLY:
//
//   Case 1 [publisher-exists]  EVERY mandatory (ftue_v2) step's completion signal, read
//                              from tutorial-steps.json, has a LIVE RUNTIME publisher -
//                              a real TutorialSignals.Raise site under Assets/_Modules
//                              (editor + test code does not count). A step id or a
//                              signal id renamed in the json without moving its
//                              publisher fails HERE, at the gate, instead of on the
//                              owner's phone 120 watchdog-seconds into the FTUE.
//
//   Case 2 [publisher-unique]  The two WO-1300 signals have EXACTLY ONE raise site each,
//                              and it is the expected one:
//                                * hero.reached:*             -> TutorialFlow.TickProximityProbe
//                                * wave.tutorial_band_repelled -> TutorialFlow.TickScriptedWave
//                              A second publisher is how a beat completes from the wrong
//                              place (an ambient wave clearing the scripted-band beat is
//                              exactly what WO-1012 P3 split these ids to prevent).
//
//   Case 3 [signal-family]     Every completion-signal FAMILY authored in the json has a
//                              publisher rule in this suite. A NEW family authored with
//                              no rule fails loudly rather than being silently unchecked -
//                              the orphan this WO exists to make impossible.
//
//   Case 4 [stuck-reports]     The WO-1300 instrumentation is present at source, so the
//                              NEXT stuck beat names ITSELF (CLAUDE.md sec.12 - a step that
//                              can go stuck and cannot report it is the bug repeating):
//                                * TutorialFlow.TickProximityProbe traces BOTH of its
//                                  early-return preconditions (no hero / no anchor) instead
//                                  of returning in silence.
//                                * TutorialFlow.RunScriptedTownWave - a fire-and-forget
//                                  UniTaskVoid - wraps its await chain in try/catch and
//                                  settles the clear poll on a throw, so a faulted arm can
//                                  never leave the beat awaiting a signal whose only
//                                  publisher will never run.
//                                * TutorialWorldAnchors.LatchAnchor traces its failure.
//
//   Case 5 [forbidden-fixes]   The bound and the rescue path are NOT the fix (WO-1300
//                              "What NOT to touch"): WatchdogSeconds stays 120f and the
//                              SKIPPED-rescue trace stays in TickWatchdog.
//
//   Case 6 [teach-spend]     WO-1340. The SPEND teach (ctx_talents) completes on a talent
//                            genuinely LEARNED, not on its own dialogue closing; its signal has
//                            exactly ONE publisher (WisdomCurrencyService.Unlock, raised only
//                            after the Wisdom debit and the unlocked-set insert land); it names
//                            both hops of the Hero -> Skills route in WORDS as well as lighting
//                            them; it gates nothing; and TickContextual releases it on a finite
//                            bound with a self-naming CTX-STUCK line. The defect it is shaped
//                            around SHIPPED: the beat used to complete on
//                            "dialogue.ended:tut_ctx_talents" - the instant the player closed the
//                            text box - and then marked itself seen forever, so a player who
//                            never found the talent screen was taught nothing and could never be
//                            told again. Every layer was individually correct; only the
//                            RELATIONSHIP between the completion signal and the taught action was
//                            wrong, which is why no other suite could see it.
//
//   Case 7 [skip-is-not-completion]
//                            WO-1794. A SKIP-OUT MAY NEVER LAND AS A COMPLETION. The single
//                            finisher TAKES its outcome (FinishFlow(string outcome), no
//                            parameterless form), SkipAll still reuses it but names
//                            "skipped_all", the mandatory chain's end names "completed", and
//                            the tutorial_completed row carries both the string and a
//                            boolean. THE DEFECT SHIPPED AND WAS MEASURED: on 2026-09-16
//                            every tutorial_skipped_all row had a tutorial_completed row in
//                            the SAME SECOND with skips:0 and totalSeconds of 10-33s, so the
//                            card read 8 completions where the truth was 2.
//
//   NOT provable here: that a real FTUE run walks the hero to the gate and repels the
//   band. That is the AutoPilot / owner felt-verify; the PO closes (CLAUDE.md sec.13).
//
// Markers: TUTORIAL_COMPLETION_PUBLISHER_OK / TUTORIAL_COMPLETION_PUBLISHER_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.TutorialCompletionPublisherRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced):
//
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "tutorial-completion-publisher suite", () => { if (!TutorialCompletionPublisherRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tutorial-completion-publisher] " + r); });
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class TutorialCompletionPublisherRegression
    {
        private const string StepsRes = "Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json";
        private const string RuntimeRoot = "Assets/_Modules";

        private const string FlowSrc = "Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs";
        private const string AnchorsSrc = "Assets/_Modules/Village/Tutorial/V2/TutorialWorldAnchors.cs";

        /// <summary>The shipped bound, mirrored deliberately: WO-1300 forbids "fixing" a stuck
        /// step by lengthening it, so a retune must fail here and be argued for.</summary>
        private const string ForbiddenBoundRetune = "WatchdogSeconds = 120f";

        // =====================================================================
        //  Entry points
        // =====================================================================

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("TUTORIAL_COMPLETION_PUBLISHER_OK - " + reason);
            else Debug.LogError("TUTORIAL_COMPLETION_PUBLISHER_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                List<string> mandatory = LoadMandatoryCompletionSignals(failures);
                List<RaiseSite> sites = CollectRaiseSites(failures);

                Case(failures, "publisher-exists", () => Case1_EveryMandatorySignalHasAPublisher(failures, mandatory, sites));
                Case(failures, "publisher-unique", () => Case2_TheTwoStuckSignalsHaveExactlyOnePublisher(failures, sites));
                Case(failures, "signal-family", () => Case3_EveryFamilyHasARule(failures, mandatory));
                Case(failures, "stuck-reports", () => Case4_StuckStepsReportThemselves(failures));
                Case(failures, "forbidden-fixes", () => Case5_ForbiddenFixesNotTaken(failures));
                Case(failures, "teach-spend", () => Case6_SpendTeachCompletesOnASpend(failures, sites));
                Case(failures, "skip-is-not-completion", () => Case7_SkipAllIsNotACompletion(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "TUTORIAL COMPLETION PUBLISHERS OK - every mandatory ftue_v2 step's completion signal has a " +
                         "live runtime publisher, the two WO-1300 signals (hero.reached:* and wave.tutorial_band_repelled) " +
                         "have exactly one raise site each in the expected method, every authored signal family has a " +
                         "publisher rule here, a stuck walk beat and a faulted scripted-band arm both report themselves, " +
                         "and the 120s bound plus the SKIPPED-rescue path are untouched. WO-1340: the spend teach " +
                         "(ctx_talents) completes on a talent genuinely LEARNED rather than on its own text box closing, " +
                         "its sole publisher is WisdomCurrencyService.Unlock and raises only after the debit lands, it " +
                         "names both route hops in words, it gates nothing, and TickContextual releases it with a " +
                         "self-naming CTX-STUCK line if the spend never comes. WO-1794: the single finisher must be TOLD " +
                         "how it was reached - FinishFlow(string outcome) with no parameterless form, the skip path finishes " +
                         "as 'skipped_all' while still reusing that one finisher, the mandatory chain's end finishes as " +
                         "'completed', and the tutorial_completed row carries both the outcome string and the completed " +
                         "boolean, so a skip-out can never be counted as a completion again.";
                return true;
            }
            reason = "tutorial-completion-publisher FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  Inputs
        // =====================================================================

        /// <summary>Completion signals of the MANDATORY chain only (flowId "contextual" steps are
        /// hints - they never gate, so an unwired one is a note elsewhere, not a stuck beat).</summary>
        private static List<string> LoadMandatoryCompletionSignals(List<string> failures)
        {
            var result = new List<string>();
            string json = ReadText(StepsRes, failures);
            if (json == null) return result;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex)
            {
                failures.Add("[parse] tutorial-steps.json is not valid JSON: " + ex.Message);
                return result;
            }

            var arr = root["steps"] as JArray;
            if (arr == null || arr.Count == 0)
            {
                failures.Add("[parse] tutorial-steps.json has no 'steps' array - the whole FTUE is unreadable.");
                return result;
            }

            foreach (var t in arr)
            {
                var o = t as JObject;
                if (o == null) continue;
                if (string.Equals((string)o["flowId"], "contextual", StringComparison.OrdinalIgnoreCase)) continue;
                string sig = o["completion"] != null ? (string)o["completion"]["signal"] : null;
                string id = (string)o["id"];
                if (string.IsNullOrEmpty(sig))
                {
                    failures.Add("[publisher-exists] mandatory step '" + (id ?? "<no id>") + "' has NO completion signal - " +
                                 "it can only ever end via the watchdog's rescued-and-SKIPPED path.");
                    continue;
                }
                if (!result.Contains(sig)) result.Add(sig);
            }
            return result;
        }

        private struct RaiseSite
        {
            public string File;      // repo-relative path
            public string Arg;       // the raise argument text, comments stripped
            public string Method;    // best-effort enclosing method name
        }

        /// <summary>Every live TutorialSignals.Raise site under Assets/_Modules, comments stripped
        /// so prose can never satisfy the lint. Editor + test code is deliberately excluded: a test
        /// double raising a signal is not a publisher the shipped game has.</summary>
        private static List<RaiseSite> CollectRaiseSites(List<string> failures)
        {
            var sites = new List<RaiseSite>();
            string[] files;
            try { files = Directory.GetFiles(RuntimeRoot, "*.cs", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                failures.Add("[source] could not enumerate " + RuntimeRoot + ": " + ex.GetType().Name + ": " + ex.Message);
                return sites;
            }

            var methodRx = new Regex(@"(?:private|public|internal|protected)[^\r\n;{}()]*?\b(\w+)\s*\([^\r\n]*\)\s*$",
                                     RegexOptions.Multiline);
            var raiseRx = new Regex(@"TutorialSignals\.Raise\s*\(");

            foreach (string f in files)
            {
                string src;
                try { src = File.ReadAllText(f); }
                catch { continue; }
                if (src.IndexOf("TutorialSignals.Raise", StringComparison.Ordinal) < 0) continue;

                string code = StripComments(src);
                string rel = f.Replace('\\', '/');

                foreach (Match m in raiseRx.Matches(code))
                {
                    string arg = ReadBalancedArgument(code, m.Index + m.Length);
                    // Best-effort enclosing method: the last method-looking signature before this point.
                    string method = "<unknown>";
                    foreach (Match sig in methodRx.Matches(code.Substring(0, m.Index)))
                        method = sig.Groups[1].Value;
                    sites.Add(new RaiseSite { File = rel, Arg = arg, Method = method });
                }
            }
            return sites;
        }

        /// <summary>Reads the text of one call argument list starting just past its '('.</summary>
        private static string ReadBalancedArgument(string code, int start)
        {
            int depth = 1;
            for (int i = start; i < code.Length; i++)
            {
                if (code[i] == '(') depth++;
                else if (code[i] == ')')
                {
                    depth--;
                    if (depth == 0) return code.Substring(start, i - start).Trim();
                }
            }
            return string.Empty;
        }

        // =====================================================================
        //  Case 1 - no mandatory completion signal is an orphan
        // =====================================================================

        private static void Case1_EveryMandatorySignalHasAPublisher(List<string> failures, List<string> signals,
                                                                   List<RaiseSite> sites)
        {
            if (signals.Count == 0)
            {
                failures.Add("[publisher-exists] no mandatory completion signals were read from tutorial-steps.json - " +
                             "the suite cannot prove anything, which is itself a failure.");
                return;
            }

            foreach (string sig in signals)
            {
                var matched = PublishersFor(sig, sites);
                if (matched.Count == 0)
                    failures.Add("[publisher-exists] completion signal '" + sig + "' has NO live runtime publisher under " +
                                 RuntimeRoot + ". The step awaiting it can ONLY end via the watchdog's rescued-and-SKIPPED " +
                                 "path - the player is walked past that beat with its outro suppressed (WO-1300, F8 seq " +
                                 "4370/4376). Either restore the publisher or re-point the json.");
            }
        }

        /// <summary>The publisher rule per signal family. A family with no rule is Case 3's failure,
        /// never a silent pass.</summary>
        private static List<RaiseSite> PublishersFor(string signal, List<RaiseSite> sites)
        {
            string family = FamilyOf(signal);
            switch (family)
            {
                case "dialogue.ended":
                    return sites.Where(s => s.Arg.Contains("DialogueEndedPrefix")).ToList();
                case "build.structure_placed":
                    return sites.Where(s => s.Arg.Contains("StructurePlacedPrefix")).ToList();
                case "build.tower_placed":
                    return sites.Where(s => s.Arg.Contains("TowerPlaced")).ToList();
                case "hero.reached":
                    // TutorialFlow.TickProximityProbe raises the LIVE awaited id, so the publisher is
                    // the probe itself rather than a per-anchor literal. Matched on the ARGUMENT (the
                    // enclosing-method read is best-effort and must never be able to raise a false alarm).
                    return sites.Where(s => s.Arg.Contains("_awaitSignal")).ToList();
                case "wave.tutorial_band_repelled":
                    return sites.Where(s => s.Arg.Contains("TutorialBandRepelled")).ToList();
                case "wave.cleared":
                    return sites.Where(s => s.Arg.Contains("WaveCleared")).ToList();
                case "arena.resolved":
                    return sites.Where(s => s.Arg.Contains("ArenaWin") || s.Arg.Contains("ArenaLoss")).ToList();
                case "echo.born":
                    return sites.Where(s => s.Arg.Contains("EchoBornSecond")).ToList();
                // WO-1340: a talent actually LEARNED - the completion of the spend teach.
                case "talent.learned":
                    return sites.Where(s => s.Arg.Contains("FirstTalentLearned")).ToList();
                default:
                    return null;   // no rule - Case 3 reports it
            }
        }

        /// <summary>The signal FAMILY: the part before ':' for a parameterised id, else the whole id.</summary>
        private static string FamilyOf(string signal)
        {
            if (string.IsNullOrEmpty(signal)) return string.Empty;
            int c = signal.IndexOf(':');
            return c > 0 ? signal.Substring(0, c) : signal;
        }

        // =====================================================================
        //  Case 2 - the two WO-1300 signals have exactly ONE publisher, in the right place
        // =====================================================================

        private static void Case2_TheTwoStuckSignalsHaveExactlyOnePublisher(List<string> failures, List<RaiseSite> sites)
        {
            AssertSolePublisher(failures, "hero.reached:*", "TickProximityProbe",
                                sites.Where(s => s.Arg.Contains("_awaitSignal")).ToList(),
                                "a second raiser of the awaited walk signal could complete the beat from somewhere the " +
                                "hero has not actually walked to");

            AssertSolePublisher(failures, "wave.tutorial_band_repelled", "TickScriptedWave",
                                sites.Where(s => s.Arg.Contains("TutorialBandRepelled")).ToList(),
                                "WO-1012 P3 split this id from wave.cleared precisely so an AMBIENT clear can never " +
                                "satisfy the scripted-band beat; a second publisher re-opens that hole");
        }

        private static void AssertSolePublisher(List<string> failures, string label, string expectedMethod,
                                                List<RaiseSite> matched, string why)
        {
            if (matched.Count == 0)
            {
                failures.Add("[publisher-unique] '" + label + "' has NO publisher at all - the beat awaiting it can only " +
                             "be rescued-and-SKIPPED (WO-1300).");
                return;
            }
            if (matched.Count > 1)
            {
                failures.Add("[publisher-unique] '" + label + "' has " + matched.Count + " raise sites (" +
                             string.Join(", ", matched.Select(m => m.File + "::" + m.Method)) + ") - exactly one is " +
                             "required: " + why + ".");
                return;
            }
            // The enclosing-method read is a best-effort source scan; "<unknown>" must never fail the
            // gate on its own (a false alarm here would cost more than the pin is worth).
            if (matched[0].Method != "<unknown>" &&
                !string.Equals(matched[0].Method, expectedMethod, StringComparison.Ordinal))
                failures.Add("[publisher-unique] '" + label + "' is published from '" + matched[0].Method + "' (" +
                             matched[0].File + ") but the documented owner is '" + expectedMethod + "'. If the publisher " +
                             "moved on purpose, move this pin with it in the SAME change.");
        }

        // =====================================================================
        //  Case 3 - a newly authored signal family cannot go unchecked
        // =====================================================================

        private static void Case3_EveryFamilyHasARule(List<string> failures, List<string> signals)
        {
            foreach (string sig in signals)
            {
                if (PublishersFor(sig, new List<RaiseSite>()) == null)
                    failures.Add("[signal-family] mandatory completion signal '" + sig + "' belongs to family '" +
                                 FamilyOf(sig) + "', which has NO publisher rule in this suite. Add one here in the same " +
                                 "change that authored the beat - an unruled family is an orphan waiting to happen, which " +
                                 "is the whole reason WO-1300 exists.");
            }
        }

        // =====================================================================
        //  Case 6 - the SPEND teach completes on a SPEND, and cannot wedge (WO-1340)
        // =====================================================================
        //
        // THE DEFECT THIS CASE IS SHAPED AROUND (not a hypothetical - it shipped):
        // ctx_talents existed since WO-T1 and taught nothing. Its completion signal was
        // "dialogue.ended:tut_ctx_talents", so the beat completed the moment the player
        // CLOSED THE TEXT BOX - the first thing anyone does - and then marked itself seen
        // FOREVER (oneShot persistence). A player who never found the talent screen got the
        // hint exactly once, spent nothing, and could never be told again.
        //
        // That is invisible to every other suite in the repo, because at every layer the
        // beat is correct: the step parses, the dialogue exists, the signal has a publisher,
        // the one-shot persists. Only the RELATIONSHIP between the completion signal and the
        // thing being taught is wrong. This case tests that relationship.
        //
        // Retention is the owner's stated business problem and WO-1306 made the mage's first
        // point buy a CASTABLE rather than a stat - work that is wasted if the point is never
        // spent. So the pin is structural, not advisory.

        private const string TeachStepId = "ctx_talents";
        private const string TeachCompletionSignal = "talent.learned:first";
        private const string TeachFirstHighlight = "hud.hero_button";
        private const string WisdomSrc = "Assets/_Modules/Village/Talents/WisdomCurrencyService.cs";

        private static void Case6_SpendTeachCompletesOnASpend(List<string> failures, List<RaiseSite> sites)
        {
            // ── the authored beat ────────────────────────────────────────────────
            JObject step = LoadStepById(TeachStepId, failures);
            if (step == null)
            {
                failures.Add("[teach-spend] step '" + TeachStepId + "' is absent from tutorial-steps.json, so this case " +
                             "checked NOTHING. A FAILURE, not a pass (WO-1138 hollow-pass class): the fixture's absence " +
                             "is exactly the state in which the spend teach silently stops existing.");
                return;
            }

            string sig = step["completion"] != null ? (string)step["completion"]["signal"] : null;
            if (!string.Equals(sig, TeachCompletionSignal, StringComparison.Ordinal))
                failures.Add("[teach-spend] '" + TeachStepId + "' completes on '" + (sig ?? "<none>") + "' but must " +
                             "complete on '" + TeachCompletionSignal + "'. THIS IS THE ORIGINAL DEFECT: completing on " +
                             "dialogue.ended:* means the beat ends when the player closes the text box, which proves only " +
                             "that they closed a box. Completion must be a point genuinely SPENT.");

            // It teaches; it must never gate. A contextual one-shot that paused pressure or
            // became non-skippable would be a blocking beat wearing a hint's clothes.
            if (!string.Equals((string)step["flowId"], "contextual", StringComparison.OrdinalIgnoreCase))
                failures.Add("[teach-spend] '" + TeachStepId + "' is no longer flowId 'contextual'. As a mandatory step it " +
                             "would BLOCK the FTUE chain until a talent is spent - the owner's brief forbids gating " +
                             "anything behind this beat.");
            if ((bool?)step["pausePressure"] == true)
                failures.Add("[teach-spend] '" + TeachStepId + "' sets pausePressure - a teach hint must never hold the game.");
            if ((bool?)step["skippable"] != true)
                failures.Add("[teach-spend] '" + TeachStepId + "' is not skippable.");
            if ((bool?)step["oneShot"] != true)
                failures.Add("[teach-spend] '" + TeachStepId + "' is not oneShot - it would re-fire on every talent point " +
                             "the player ever earns.");

            var hl = step["highlight"] as JArray;
            if (hl == null || hl.Count == 0 || !string.Equals((string)hl[0], TeachFirstHighlight, StringComparison.Ordinal))
                failures.Add("[teach-spend] '" + TeachStepId + "' must open its spotlight on '" + TeachFirstHighlight +
                             "' (hop 1 of the owner-confirmed Hero -> Skills route, 2026-09-03). A contextual hint shows " +
                             "only highlight[0], so an empty or re-ordered list points the player at nothing.");

            var route = step["route"] as JArray;
            if (route == null || route.Count == 0)
                failures.Add("[teach-spend] '" + TeachStepId + "' has no 'route' - the spotlight would sit on the Hero bar " +
                             "face for the whole beat and never point at the SKILLS card, so the second half of the taught " +
                             "path is untaught.");

            // The route is named in WORDS as well as lit: the owner is red/green colourblind,
            // and the lazy highlight resolvers degrade to nothing when a rect is absent.
            string objective = step["objective"] != null ? (string)step["objective"]["text"] : null;
            if (string.IsNullOrEmpty(objective) ||
                objective.IndexOf("Hero", StringComparison.OrdinalIgnoreCase) < 0 ||
                objective.IndexOf("Skills", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("[teach-spend] '" + TeachStepId + "' objective text must NAME both hops ('Hero', 'Skills'). " +
                             "The owner is red/green colourblind and the spotlight resolvers degrade to nothing when a " +
                             "rect is missing, so the words are the affordance that always survives - never the glow alone.");

            // ── the publisher: exactly one, at the choke point ──────────────────
            var matched = sites.Where(s => s.Arg.Contains("FirstTalentLearned")).ToList();
            AssertSolePublisher(failures, TeachCompletionSignal, "Unlock", matched,
                                "WisdomCurrencyService.Unlock is the ONE choke point every learn path funnels through " +
                                "(the legacy immediate HeroSkillTreeVM.Unlock and the node-graph plan/CONFIRM Commit both " +
                                "call it). A second publisher - especially one hung off SkillSystem.SpendPoint, which is " +
                                "the unrelated CRAFT skill economy - would complete this beat without the player ever " +
                                "touching the talent tree");

            if (matched.Count == 1 &&
                matched[0].File.IndexOf("WisdomCurrencyService", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("[teach-spend] '" + TeachCompletionSignal + "' is published from " + matched[0].File +
                             ", not WisdomCurrencyService. The signal must be raised where the Wisdom debit and the " +
                             "unlocked-set insert actually land, or it can claim a spend that did not happen.");

            // Raised only AFTER the state change, never before it.
            string wisdom = ReadText(WisdomSrc, failures);
            if (wisdom == null)
            {
                failures.Add("[teach-spend] fixture absent: " + WisdomSrc + " could not be read, so the publisher's " +
                             "position relative to the debit checked NOTHING. A FAILURE, not a pass.");
            }
            else
            {
                string code = StripComments(wisdom);
                string unlock = ExtractMethod(code, "Unlock");
                if (unlock == null)
                    failures.Add("[teach-spend] WisdomCurrencyService.Unlock not found - the spend teach has no publisher.");
                else
                {
                    int debit = unlock.IndexOf("_unlocked.Add", StringComparison.Ordinal);
                    int raise = unlock.IndexOf("FirstTalentLearned", StringComparison.Ordinal);
                    if (raise < 0)
                        failures.Add("[teach-spend] WisdomCurrencyService.Unlock no longer raises FirstTalentLearned.");
                    else if (debit < 0 || raise < debit)
                        failures.Add("[teach-spend] WisdomCurrencyService.Unlock raises FirstTalentLearned BEFORE the node " +
                                     "is added to the unlocked set. The signal would announce a spend that a later guard " +
                                     "could still reject - it must be raised only after the debit and the insert have landed.");
                }
            }

            // ── the escape: it can never wedge (owner brief, CLAUDE.md sec.12) ──
            string flow = ReadText(FlowSrc, failures);
            if (flow == null)
            {
                failures.Add("[teach-spend] fixture absent: " + FlowSrc + " could not be read, so the escape path " +
                             "checked NOTHING. A FAILURE, not a pass - an unreadable fixture is precisely when a " +
                             "wedged teach beat goes unnoticed.");
                return;
            }

            string flowCode = StripComments(flow);

            if (!flowCode.Contains("ContextualAwaitSeconds"))
                failures.Add("[teach-spend] TutorialFlow has no ContextualAwaitSeconds bound. A teach beat waits on a " +
                             "GAMEPLAY signal and deliberately outlives its dialogue, so the 10s no-dialogue auto-close " +
                             "does not apply to it. Without a finite bound its spotlight can point forever - 'a tutorial " +
                             "step that can wedge is worse than no tutorial step'.");

            string tick = ExtractMethod(flowCode, "TickContextual");
            if (tick == null)
                failures.Add("[teach-spend] TutorialFlow.TickContextual not found - the contextual escape path is gone.");
            else
            {
                if (!tick.Contains("ContextualAwaitSeconds"))
                    failures.Add("[teach-spend] TutorialFlow.TickContextual does not spend the ContextualAwaitSeconds " +
                                 "bound, so nothing ever releases a teach beat whose completion signal never arrives.");
                if (!tick.Contains("CTX-STUCK"))
                    failures.Add("[teach-spend] TutorialFlow.TickContextual does not emit a CTX-STUCK line on expiry. " +
                                 "WO-1300 exists because two stuck beats emitted NOTHING and cost two investigations; a " +
                                 "teach beat that quietly stopped pointing is the same defect in a cheaper coat. Do NOT " +
                                 "strip this (CLAUDE.md sec.12 - instrumentation is permanent).");
                if (!tick.Contains("timeout"))
                    failures.Add("[teach-spend] TutorialFlow.TickContextual never completes the beat on timeout, so the " +
                                 "spotlight is not released and the one-shot is not marked seen.");
            }

            // The teach beat must NOT be completable by its own dialogue ending - the very
            // thing that made the old hint hollow. The guard lives in OnSignal.
            // ⚠ ASSERTED ON THE BRANCH CONDITION, NOT ON THE WHOLE METHOD - and that precision is
            // the point. The first version of this check asked only whether OnSignal MENTIONED
            // _ctxAwaitSignal anywhere, and it passed the mutation that deletes the guard from the
            // dialogue-ended branch: the teach-completion branch above still mentions the field, so
            // the method-wide search stayed satisfied while the defect was fully restored. A
            // hollow assertion that cannot fail is worse than no assertion, because it reports
            // green. Scope the read to the condition that actually decides.
            string onSignal = ExtractMethod(flowCode, "OnSignal");
            if (onSignal == null)
            {
                failures.Add("[teach-spend] TutorialFlow.OnSignal not found.");
            }
            else
            {
                const string DlgBranchMark = "DialogueEndedPrefix + _activeCtx.Dialogue.Intro";
                int branch = onSignal.IndexOf(DlgBranchMark, StringComparison.Ordinal);
                if (branch < 0)
                {
                    failures.Add("[teach-spend] TutorialFlow.OnSignal no longer contains the contextual dialogue-ended " +
                                 "branch in its expected shape, so this oracle cannot prove the teach beat is protected " +
                                 "from being completed by its own text box. Re-point this assertion in the same change.");
                }
                else
                {
                    int ifStart = onSignal.LastIndexOf("if (", branch, StringComparison.Ordinal);
                    string cond = ifStart >= 0 ? onSignal.Substring(ifStart, branch - ifStart) : string.Empty;
                    if (cond.IndexOf("_ctxAwaitSignal", StringComparison.Ordinal) < 0)
                        failures.Add("[teach-spend] the contextual dialogue-ended branch in TutorialFlow.OnSignal is NOT " +
                                     "guarded on _ctxAwaitSignal. Without that guard the beat completes the moment the " +
                                     "player closes the text box - which is the ENTIRE hollow-hint defect WO-1340 removed " +
                                     "(the hint was then marked seen forever, so a player who never found the talent " +
                                     "screen could never be told again).");
                }
            }
        }

        /// <summary>Reads one authored step object out of tutorial-steps.json by id (any flow).</summary>
        private static JObject LoadStepById(string id, List<string> failures)
        {
            string json = ReadText(StepsRes, failures);
            if (json == null) return null;
            try
            {
                var arr = JObject.Parse(json)["steps"] as JArray;
                if (arr == null) return null;
                foreach (var t in arr)
                {
                    var o = t as JObject;
                    if (o != null && string.Equals((string)o["id"], id, StringComparison.Ordinal)) return o;
                }
            }
            catch (Exception ex)
            {
                failures.Add("[teach-spend] tutorial-steps.json unreadable: " + ex.Message);
            }
            return null;
        }

        // =====================================================================
        //  Case 4 - a stuck step must name ITSELF (CLAUDE.md sec.12)
        // =====================================================================

        private static void Case4_StuckStepsReportThemselves(List<string> failures)
        {
            string flow = ReadText(FlowSrc, failures);
            // The fixture's ABSENCE is asserted here, not merely guarded against. ReadText already
            // appends a "[source] missing file" failure, so this is belt-and-braces - but the shape
            // matters independently of the redundancy: with every assertion nested inside a positive
            // `if (flow != null)`, a vanished TutorialFlow.cs made this case check ZERO things and
            // still report green. That is the WO-1138 hollow-pass class, and the ratchet was right to
            // reject it even though ReadText happened to cover it (RaidCooldownRegression case 5,
            // 2026-08-21: a teardown installed a DESTROYED state and the cases under it measured
            // nothing). A suite must state what it could not check, never fall silent.
            if (flow == null)
            {
                failures.Add("[stuck-reports] fixture absent: " + FlowSrc + " could not be read, so WO-1300's " +
                             "self-reporting assertions checked NOTHING. This is a FAILURE, not a pass - an " +
                             "unreadable fixture is exactly the condition under which a stuck tutorial step " +
                             "would go unnoticed.");
                return;
            }
            {
                string code = StripComments(flow);

                string probe = ExtractMethod(code, "TickProximityProbe");
                if (probe == null)
                    failures.Add("[stuck-reports] TutorialFlow.TickProximityProbe not found - the walk beat's only " +
                                 "publisher is gone.");
                else if (CountOccurrences(probe, "FlowTrace.Warn") < 2)
                    failures.Add("[stuck-reports] TutorialFlow.TickProximityProbe has fewer than 2 FlowTrace.Warn sites. " +
                                 "WO-1300: BOTH early-return preconditions (no HeroLocomotion / unresolvable anchor) must " +
                                 "report themselves. Returning in silence is what made the founding_walk STEP-STUCK line " +
                                 "unactionable and cost a second investigation. Do NOT strip these (CLAUDE.md sec.12).");

                string arm = ExtractMethod(code, "RunScriptedTownWave");
                if (arm == null)
                    failures.Add("[stuck-reports] TutorialFlow.RunScriptedTownWave not found - the scripted band is " +
                                 "no longer armed anywhere.");
                else
                {
                    if (!arm.Contains("catch"))
                        failures.Add("[stuck-reports] TutorialFlow.RunScriptedTownWave has no catch. It is a fire-and-forget " +
                                     "UniTaskVoid over an await chain that can fault (SpawnAt awaits the enemy catalog); " +
                                     "unguarded, a throw leaves the clear poll unarmed and founding_defend awaits a signal " +
                                     "whose only publisher will never run (WO-1300, F8 seq 4370).");
                    if (!arm.Contains("SettleScriptedWaveWithoutBand"))
                        failures.Add("[stuck-reports] TutorialFlow.RunScriptedTownWave no longer settles the clear poll when " +
                                     "the band cannot be armed. TutorialWaveSpawner's proceed-don't-wedge contract must hold " +
                                     "for a THROWN arm too, or the beat strands until the watchdog SKIPS it.");
                }

                if (!code.Contains("MarkClearedWithoutBand"))
                    failures.Add("[stuck-reports] the proceed-don't-wedge seam (TutorialWaveSpawner.MarkClearedWithoutBand) " +
                                 "is no longer called from TutorialFlow.");
            }

            string anchors = ReadText(AnchorsSrc, failures);
            if (anchors != null)
            {
                string code = StripComments(anchors);
                string latch = ExtractMethod(code, "LatchAnchor");
                if (latch == null)
                    failures.Add("[stuck-reports] TutorialWorldAnchors.LatchAnchor not found.");
                else if (!latch.Contains("FlowTrace."))
                    failures.Add("[stuck-reports] TutorialWorldAnchors.LatchAnchor fails SILENTLY again. Its caller " +
                                 "(TutorialFlow.EnterStep) discards the return value, so an anchor that never resolves " +
                                 "produced no line at all until the 120s STEP-STUCK - which names the missing SIGNAL, not " +
                                 "the missing ANCHOR (WO-1300).");
            }
        }

        // =====================================================================
        //  Case 5 - the forbidden fixes were not taken (WO-1300 "What NOT to touch")
        // =====================================================================

        private static void Case5_ForbiddenFixesNotTaken(List<string> failures)
        {
            string flow = ReadText(FlowSrc, failures);
            if (flow == null) return;
            string code = StripComments(flow);

            if (!code.Contains(ForbiddenBoundRetune))
                failures.Add("[forbidden-fixes] the default watchdog bound is no longer '" + ForbiddenBoundRetune + "'. " +
                             "WO-1300: raising, removing or configuring the 120s bound is NOT a fix and is explicitly out " +
                             "of scope - the watchdog is the detector that caught this, and blunting it is the failure mode " +
                             "CLAUDE.md sec.12 exists to prevent.");

            if (!code.Contains("STEP-STUCK"))
                failures.Add("[forbidden-fixes] the STEP-STUCK trace is gone from TutorialFlow - that line is the only " +
                             "reason this defect was ever seen.");

            if (!code.Contains("skipped: true"))
                failures.Add("[forbidden-fixes] the watchdog's rescued-as-SKIPPED path appears to have been removed. " +
                             "WO-1300: 'grants still applied so the player is never half-granted' is deliberate and stays.");
        }

        /// <summary>
        /// WO-1794 Case 7 [skip-is-not-completion] — A SKIP-OUT MAY NEVER LAND AS A COMPLETION.
        ///
        /// <para>THE CAPTURED DEFECT (production analytics_events, 2026-09-16): every one of the
        /// day's six <c>tutorial_skipped_all</c> rows had a <c>tutorial_completed</c> row in the
        /// SAME SECOND, carrying <c>skips: 0</c> and <c>totalSeconds</c> of 10.35 / 28.48 / 11.16 —
        /// a tutorial nobody could have played. The metrics card read "started 14, completed 8";
        /// at least six of those eight were skip-outs and the real number was two. The cause was
        /// structural, not a typo: <c>SkipAll</c> deliberately reuses the single finisher, and the
        /// finisher emitted <c>tutorial_completed</c> with NO KNOWLEDGE of how it was reached.</para>
        ///
        /// <para>So this pins the knowledge, not the string: the finisher TAKES an outcome, no call
        /// site may reach it without naming one, the emitted row carries that outcome, and the skip
        /// path names the skip. A future refactor that re-introduces a parameterless finisher, or
        /// drops the property, fails HERE — the only place it can be caught, because the event
        /// itself is indistinguishable from a real completion once it is on the wire.</para>
        /// </summary>
        private static void Case7_SkipAllIsNotACompletion(List<string> failures)
        {
            string flow = ReadText(FlowSrc, failures);
            if (flow == null) return;
            string code = StripComments(flow);

            // The two wire values. Literals, because analytics queries are written against the
            // STRING - renaming one silently orphans every dashboard filter built on it.
            if (!code.Contains("OutcomeCompleted = \"completed\""))
                failures.Add("[skip-is-not-completion] TutorialFlow no longer declares OutcomeCompleted = \"completed\". " +
                             "That literal is what an analytics query filters on; renaming it makes every honest-completion " +
                             "filter silently match nothing.");
            if (!code.Contains("OutcomeSkippedAll = \"skipped_all\""))
                failures.Add("[skip-is-not-completion] TutorialFlow no longer declares OutcomeSkippedAll = \"skipped_all\".");

            // The finisher must be TOLD. A parameterless overload is the defect returning.
            if (!code.Contains("FinishFlow(string outcome)"))
                failures.Add("[skip-is-not-completion] TutorialFlow has no 'FinishFlow(string outcome)'. The 2026-09-16 " +
                             "defect WAS the finisher not knowing how it was reached, so a finisher that cannot be told is " +
                             "that defect restored.");
            if (code.Contains("FinishFlow()"))
                failures.Add("[skip-is-not-completion] a parameterless 'FinishFlow()' exists or is called in TutorialFlow. " +
                             "Every exit must name its outcome; an unnamed one falls through as a completion, which is " +
                             "exactly how six skip-outs became eight completions.");

            // The emitted row carries it.
            string finish = ExtractMethod(code, "FinishFlow");
            if (finish == null)
            {
                failures.Add("[skip-is-not-completion] FinishFlow's body could not be extracted, so this case checked " +
                             "NOTHING - a FAILURE, not a pass (WO-1138 hollow-pass class).");
            }
            else
            {
                if (!finish.Contains("tutorial_completed"))
                    failures.Add("[skip-is-not-completion] FinishFlow no longer emits 'tutorial_completed'. If the event " +
                                 "moved, this case must move with it - an un-pinned emitter is how it drifted in the first place.");
                if (!finish.Contains("outcome ="))
                    failures.Add("[skip-is-not-completion] the 'tutorial_completed' row in FinishFlow does not carry an " +
                                 "'outcome' property. Without it a skip-out and a played-through FTUE are the same row, and " +
                                 "the owner's completion number counts both.");
                if (!finish.Contains("completed ="))
                    failures.Add("[skip-is-not-completion] the 'tutorial_completed' row does not carry the 'completed' " +
                                 "boolean that dashboards filter on.");
            }

            // The skip path names the skip, and still emits its own event.
            string skipAll = ExtractMethod(code, "SkipAll");
            if (skipAll == null)
            {
                failures.Add("[skip-is-not-completion] SkipAll's body could not be extracted, so the skip half of this " +
                             "case checked NOTHING - a FAILURE, not a pass.");
            }
            else
            {
                if (!skipAll.Contains("FinishFlow(OutcomeSkippedAll)"))
                    failures.Add("[skip-is-not-completion] SkipAll does not finish with FinishFlow(OutcomeSkippedAll). " +
                                 "It must still REUSE the single finisher (a divergent finisher is forbidden by the FTUE " +
                                 "spec and would half-grant a skipper) - it must simply say how it got there.");
                if (!skipAll.Contains("tutorial_skipped_all"))
                    failures.Add("[skip-is-not-completion] SkipAll no longer emits 'tutorial_skipped_all' - the event that " +
                                 "made the defect visible at all.");
            }

            // The played-to-the-end path names itself too, so "completed" is asserted by the
            // chain running out of steps rather than by a default.
            string advance = ExtractMethod(code, "AdvanceToNextStep");
            if (advance == null)
                failures.Add("[skip-is-not-completion] AdvanceToNextStep's body could not be extracted, so the genuine-" +
                             "completion half of this case checked NOTHING - a FAILURE, not a pass.");
            else if (!advance.Contains("FinishFlow(OutcomeCompleted)"))
                failures.Add("[skip-is-not-completion] the mandatory chain's end does not call " +
                             "FinishFlow(OutcomeCompleted), so a genuine completion is no longer asserted at the one place " +
                             "that knows the player reached the end.");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        // Brace characters are held as consts so this file's own literal-brace count stays
        // balanced - the project's C# quality gate (CLAUDE.md sec.1) counts raw braces and a
        // lone opening brace inside a string or a regex reads to it as a mismatch.
        private const char OpenBrace = '{';
        private const char CloseBrace = '}';

        /// <summary>Returns the body text of a method by brace matching from its DECLARATION
        /// (the first name+parens immediately followed by an opening brace). Null when absent.</summary>
        private static string ExtractMethod(string code, string name)
        {
            var rx = new Regex(@"\b" + Regex.Escape(name) + @"\s*\([^;()]*\)");
            foreach (Match m in rx.Matches(code))
            {
                int i = m.Index + m.Length;
                while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
                if (i >= code.Length || code[i] != OpenBrace) continue;   // a call site, not a declaration

                int depth = 0;
                for (int j = i; j < code.Length; j++)
                {
                    if (code[j] == OpenBrace) depth++;
                    else if (code[j] == CloseBrace)
                    {
                        depth--;
                        if (depth == 0) return code.Substring(i, j - i + 1);
                    }
                }
                return null;
            }
            return null;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        private static string ReadText(string path, List<string> failures)
        {
            try
            {
                if (!File.Exists(path))
                {
                    failures.Add("[source] missing file: " + path);
                    return null;
                }
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                failures.Add("[source] could not read " + path + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Strips // and /* */ comments so a lint can never be satisfied by prose.</summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", " ");
        }
    }
}
