// =============================================================================
// RaidDoorPromptRegression - the raid door is POINTED AT, and the pointing cannot
// go stale (WO-1802).
// -----------------------------------------------------------------------------
// Owner ruling 2026-09-16, verbatim: "make the raid door obvious after founding".
// Follow-ups the same day, verbatim:
//   "part of it is the heart fire people don't understand that heart fire is raids...
//    We should have some kind of helper that says try building a barracks or click here
//    to put your barracks - something that we should assist them."
//   "we need to create a desire to raid so we need to announce early what the rewards
//    are and we should start them with a starter army so they can at least do one for
//    free."
//
// THE EVIDENCE THIS SUITE DEFENDS (docs/LIVE_PLAYERS_TRIAGE_2026-09-16.md): ZERO
// players launched a raid that day and THREE in seven days. FOUR ids reached
// founding_path_selected, and for each of them the starter-army grant fired
// raid_funnel_barracks_unlocked AND raid_funnel_army_trained in the SAME SECOND.
// Not one emitted raid_funnel_first_raid_attempted. The raid is the game's hook and
// act 2 of the hackathon video, and the funnel died at the step where a player has
// to FIND the door.
//
// -----------------------------------------------------------------------------
// WHY A SEPARATE SUITE AND NOT ANOTHER CASE IN tutorial-reach.
// -----------------------------------------------------------------------------
// Half these cases are not about tutorial steps at all: [fail-closed] asserts a PURE
// PREDICATE's truth table (DeNelle.Core.HudModel.RaidDoorReadiness) and [once-plus-one]
// asserts a SAVE-LEDGER rule. Bolting those onto a 1491-line reachability suite would
// make its marker mean two different things, and that file is also lane-fenced. Its
// Case 8 / Case 9 precedent is followed in SHAPE (read the authored JSON directly, one
// named case per invariant, notes for gaps) with its own marker:
//
//   RAID_DOOR_PROMPT_OK - <reason>        (all cases passed)
//   RAID_DOOR_PROMPT_FAIL: <reason>       (>=1 case failed)
//
// Distinct on purpose (CLAUDE.md section 8: until 2026-08-02 three classes printed the
// same bare REGRESSION_OK and the check-in gate ran the wrong one unnoticed).
//
// Standalone:
//   run-unity-method DeNelle.Editor.Regression.RaidDoorPromptRegression.RunAll
// The DataRegression.RunAll registration line is left to the committer - that file is
// lane-fenced and this lane may not edit it. The line is written into the RESULT.
//
// -----------------------------------------------------------------------------
// THE FIVE CASES, AND THE WAY EACH ONE FAILS IN REAL LIFE.
// -----------------------------------------------------------------------------
//  A [chain-shape]  All THREE rungs exist, in order, each a CONTEXTUAL + oneShot +
//                   non-pausing beat that completes on a SERVICE SIGNAL rather than on
//                   its own dialogue closing. This is the WO-1340 defect in its purest
//                   form: ctx_talents shipped saying "open your talents" and marked
//                   itself seen the instant the player closed the text box, so it taught
//                   nothing and measured a dismissal as a success. A future edit that
//                   "simplifies" any rung's completion to dialogue.ended would silently
//                   do it again, and the only symptom would be a funnel still reading zero.
//  B [fail-closed]  RaidDoorReadiness' whole truth table on the PURE overload, no scene,
//                   no rails. THIS IS THE CASE THAT MATTERS MOST, because every rail it
//                   reads defaults OPEN: PostureSignals.RaidCapable is true before any
//                   publish, HeartfireLit defaults to the ceiling, and
//                   RaidEntryGate.ArmyStatus defaults { Ready = true, Version = 0 }. Each
//                   default is correct for a display that must not imply an unevaluated
//                   gate - and exactly backwards for an always-on badge, which would light
//                   up on the title screen, in every headless capture, and on a save with
//                   no barracks and no troops.
//                   It ALSO pins the army bar: an earlier draft of this lane judged the
//                   army by DeployableSlots > 0, and RaidSelectionScreen.Open:342-402
//                   TOASTS AND REDIRECTS TO THE DRILLMASTER unless ArmyStatus.Ready - so
//                   one troop would have been walked into a refusal. A badge that does
//                   that does not merely fail to help; it teaches that the HUD lies.
//  C [route-real]   Every highlight id every rung authors is a
//                   TutorialHighlightRegistry.KnownIds member, every rung's CTA verb is
//                   routed by DialogueCommandSink, and each CTA carries ITS OWN step id as
//                   the tap marker. ctx_raid_first and ctx_heartfire both recorded in their
//                   notes that NO Journey/Raids highlight id existed; this ticket added the
//                   first two, and an id that is authored but unregistered points at
//                   nothing while looking authored.
//  D [once-plus-one] Every rung is once-per-save with exactly ONE ledger-capped re-arm.
//                   A re-arm with no ledger is nagware; a latch with no re-arm loses every
//                   player who dismissed a helper once - and losing rung 1 is fatal,
//                   because nothing downstream can then become reachable.
//  E [copy-projected] NO player-visible string in any rung carries a DIGIT outside a
//                   {token}, and every {token} used is registered. The owner ruled the
//                   starter army to TEN on the same day this lane was written, with a
//                   SIBLING LANE (WO-1803) owning the knob - so a literal "3" would have
//                   been wrong within hours. Same for the spoils: they come from the settle
//                   payout's own formula, so the prompt cannot advertise a rate the game
//                   does not pay.
//
// Source-text assertions are used ONLY where the thing pinned IS a wiring fact in a file
// this suite cannot execute (a MonoBehaviour tick, a dialogue verb switch). Everything
// that can be EXECUTED is executed - case B calls the real predicate, case E resolves the
// real token registry.
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
    public static class RaidDoorPromptRegression
    {
        private const string StepsRes = "Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json";
        private const string StepsSA = "Assets/StreamingAssets/Data/Canonical/tutorial/tutorial-steps.json";
        private const string DialoguesRes = "Assets/Resources/Data/Canonical/dialogue/dialogues.json";
        private const string DialoguesSA = "Assets/StreamingAssets/Data/Canonical/dialogue/dialogues.json";
        private const string AdaptersSrc = "Assets/_Modules/Village/Tutorial/V2/TutorialSignalAdapters.cs";
        private const string FlowSrc = "Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs";
        private const string SinkSrc = "Assets/_Modules/Village/Tutorial/DialogueCommandSink.cs";
        private const string RouterSrc = "Assets/_Modules/Core/SceneRouter.cs";

        /// <summary>
        /// One authored rung: the step id (read off the interpreter's own constant, never re-typed,
        /// so a rename cannot leave this suite green against a beat that moved), the SERVICE signal
        /// it must complete on, and the trigger the emitter raises for it.
        /// </summary>
        private struct Rung
        {
            public string BeatId, Trigger, Completion, DialogueId, Label;
        }

        private static Rung[] Rungs => new[]
        {
            new Rung
            {
                BeatId = DeNelle.Village.TutorialFlow.RaidHelperBarracksBeatId,
                Trigger = DeNelle.Core.Tutorial.TutorialSignals.RaidHelperBarracks,
                // A Barracks that was actually PLACED. BuildModeController.StructurePlaced ->
                // TutorialSignalAdapters.OnStructurePlaced raises the per-id form.
                Completion = "build.structure_placed:barracks",
                DialogueId = "tut_ctx_raid_helper_barracks",
                Label = "rung 1 (build a Barracks)",
            },
            new Rung
            {
                BeatId = DeNelle.Village.TutorialFlow.RaidHelperArmyBeatId,
                Trigger = DeNelle.Core.Tutorial.TutorialSignals.RaidHelperArmy,
                // A train/upgrade job that actually LANDED on a line (BarracksService's two
                // success points) - the ctx_post_raid precedent.
                Completion = DeNelle.Core.Tutorial.TutorialSignals.TroopJobQueued,
                DialogueId = "tut_ctx_raid_helper_army",
                Label = "rung 2 (train troops)",
            },
            new Rung
            {
                BeatId = DeNelle.Village.TutorialFlow.RaidDoorBeatId,
                Trigger = DeNelle.Core.Tutorial.TutorialSignals.RaidDoorReady,
                // A raid actually LAUNCHED - raised at SceneRouter.GoRaid, the very call site
                // that emits raid_funnel_first_raid_attempted.
                Completion = DeNelle.Core.Tutorial.TutorialSignals.RaidAttempted,
                DialogueId = "tut_ctx_raid_door",
                Label = "rung 3 (the raid door)",
            },
        };

        // =====================================================================

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("RAID_DOOR_PROMPT_OK - " + reason);
            else Debug.LogError("RAID_DOOR_PROMPT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                var steps = LoadSteps(failures);
                var dialogues = LoadDialogues(failures);
                Case(failures, "chain-shape", () => CaseA_ChainShape(steps, dialogues, failures, notes));
                Case(failures, "fail-closed", () => CaseB_FailClosed(failures, notes));
                Case(failures, "route-real", () => CaseC_RouteReal(steps, dialogues, failures, notes));
                Case(failures, "once-plus-one", () => CaseD_OncePlusOne(failures, notes));
                Case(failures, "copy-projected", () => CaseE_CopyProjected(steps, dialogues, failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "RAID DOOR PROMPT OK - the helper chain is three ordered, non-gating " +
                         "contextual one-shots (build a Barracks -> train troops -> raid), each " +
                         "completing on a SERVICE SIGNAL and never on its own dialogue closing; " +
                         "RaidDoorReadiness answers Unpublished on every unread rail and judges the " +
                         "army by the DOOR'S OWN bar, so no badge or helper can walk a player into a " +
                         "refusal; every authored highlight id is registered and every CTA verb is " +
                         "routed carrying its own step id; each rung is once per save with exactly ONE " +
                         "ledger-capped re-arm; and no player-visible string carries a digit outside a " +
                         "registered {token}" + noteStr;
                return true;
            }
            reason = "raid-door-prompt FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        // =====================================================================
        //  A [chain-shape]
        // =====================================================================

        private static void CaseA_ChainShape(Dictionary<string, JObject> steps,
                                             Dictionary<string, JObject> dialogues,
                                             List<string> failures, List<string> notes)
        {
            if (steps == null) return;   // LoadSteps already reported

            var orders = new List<int>();
            foreach (var rung in Rungs)
            {
                if (!steps.TryGetValue(rung.BeatId, out var beat))
                {
                    failures.Add("[chain-shape] tutorial-steps.json has no step '" + rung.BeatId +
                        "' (" + rung.Label + "). The chain is the whole of WO-1802; without the step " +
                        "the emitter raises '" + rung.Trigger + "' into nothing.");
                    continue;
                }
                orders.Add(beat["order"] != null ? (int)beat["order"] : -1);

                string flowId = (string)beat["flowId"];
                if (!string.Equals(flowId, "contextual", StringComparison.OrdinalIgnoreCase))
                    failures.Add("[chain-shape] " + rung.BeatId + " flowId is '" + (flowId ?? "<null>") +
                        "', not 'contextual'. A MANDATORY beat awaiting a Barracks (or a raid) the " +
                        "player may never do would strand the FTUE forever - worse than no helper at " +
                        "all - and it would break the eight-beat chain tutorial-reach Case 6 pins.");

                if (beat["oneShot"] == null || !(bool)beat["oneShot"])
                    failures.Add("[chain-shape] " + rung.BeatId + " is not oneShot - it would return on " +
                        "every trigger raise, and the emitter re-raises every 30s. That is nagware.");

                if (beat["pausePressure"] != null && (bool)beat["pausePressure"])
                    failures.Add("[chain-shape] " + rung.BeatId + " has pausePressure TRUE. A contextual " +
                        "beat must never gate or hold the world clock; these point at optional doors.");

                if (beat["skippable"] == null || !(bool)beat["skippable"])
                    failures.Add("[chain-shape] " + rung.BeatId + " is not skippable - a helper about an " +
                        "optional activity must always be dismissible.");

                string trigger = (string)beat["trigger"]?["signal"];
                if (!string.Equals(trigger, rung.Trigger, StringComparison.OrdinalIgnoreCase))
                    failures.Add("[chain-shape] " + rung.BeatId + " trigger is '" + (trigger ?? "<null>") +
                        "', expected '" + rung.Trigger + "'.");

                // ── THE LOAD-BEARING ASSERTION OF THIS WHOLE SUITE ──────────
                string completion = (string)beat["completion"]?["signal"];
                string intro = (string)beat["dialogue"]?["intro"];
                if (!string.Equals(completion, rung.Completion, StringComparison.OrdinalIgnoreCase))
                    failures.Add("[chain-shape] " + rung.BeatId + " completes on '" +
                        (completion ?? "<null>") + "', NOT the service signal '" + rung.Completion +
                        "'. This is the WO-1340 defect verbatim: ctx_talents shipped completing on its " +
                        "own dialogue.ended, so it was marked seen the instant the player closed the " +
                        "text box and taught nothing. Each rung must be completed by the THING - a " +
                        "Barracks placed, a troop job queued, a raid launched.");
                if (!string.IsNullOrEmpty(intro) &&
                    string.Equals(completion, "dialogue.ended:" + intro, StringComparison.OrdinalIgnoreCase))
                    failures.Add("[chain-shape] " + rung.BeatId + " completes on its OWN dialogue " +
                        "closing - TutorialStepDef.AwaitsGameplayCompletion would read FALSE and the " +
                        "interpreter would treat it as an announcement, not a teach beat.");

                // The dialogue record must exist and speak as {guide} only.
                if (dialogues != null && !dialogues.ContainsKey(rung.DialogueId))
                    failures.Add("[chain-shape] dialogues.json has no record '" + rung.DialogueId +
                        "' for " + rung.BeatId + " - the beat would surface with no words.");
                else if (dialogues != null)
                {
                    foreach (var ln in LinesOf(dialogues[rung.DialogueId]))
                    {
                        string sp = (string)ln["speaker"];
                        if (!string.Equals(sp, "{guide}", StringComparison.Ordinal))
                            failures.Add("[chain-shape] '" + rung.DialogueId + "' has a line spoken by '" +
                                (sp ?? "<null>") + "'. EVERY tut_* line speaks as '{guide}' and nothing " +
                                "else (TutorialGuideIdentityRegression Case 2) - two contradictory " +
                                "tutorial narrators is the exact defect the Sylas arc retirement fixed.");
                    }
                }
            }

            // The rungs must be ORDERED build -> train -> raid. `order` does not choose the runtime
            // rung (the live diagnosis does), but an out-of-order file is a reader trap and the
            // authored order is the documentation of the chain.
            for (int i = 1; i < orders.Count; i++)
                if (orders[i] <= orders[i - 1])
                    failures.Add("[chain-shape] the rungs are not in ascending `order` (" +
                        string.Join(" -> ", orders) + "). The authored order IS the chain's " +
                        "documentation: build a Barracks, then train, then raid.");

            // The mandatory chain must still be exactly the eight WO-1012 founding beats.
            int mandatory = steps.Count;   // placeholder; recounted from the raw file below
            mandatory = CountMandatory(failures);
            if (mandatory >= 0 && mandatory != 8)
                failures.Add("[chain-shape] the MANDATORY chain now has " + mandatory + " beats, not the " +
                    "eight WO-1012 founding beats. One of this ticket's rungs became mandatory, which " +
                    "would strand any player who never builds a Barracks (tutorial-reach Case 6).");

            // The completion signals must actually be RAISED, or a rung can only ever end on the
            // 240s escape bound - green suite, dead teach.
            string router = ReadText(RouterSrc, failures);
            if (router != null)
            {
                if (!router.Contains("TutorialSignals.RaidAttempted"))
                    failures.Add("[chain-shape] SceneRouter.cs does not raise TutorialSignals" +
                        ".RaidAttempted - the door rung's completion has NO live emitter, so it could " +
                        "only ever time out.");
                else if (!router.Contains("RaidFunnel.RaidAttempted"))
                    notes.Add("SceneRouter raises the tutorial signal but RaidFunnel.RaidAttempted was " +
                              "not found beside it - the two are supposed to share one call site");
            }
        }

        // =====================================================================
        //  B [fail-closed] - the truth table, on the PURE overload
        // =====================================================================

        private static void CaseB_FailClosed(List<string> failures, List<string> notes)
        {
            // (capable, hfPublished, hfLit, armyVersion, armyReady, deployable, attempted)
            //   -> expected blocker, and WHY this row is in the table.
            var rows = new (bool cap, bool hfPub, int hf, int ver, bool ready, int slots, bool done,
                            DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker want, string why)[]
            {
                (true, true, 3, 7, true, 3, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.Ready,
                    "the happy path: barracks, a published Heart with charges, a published roster over " +
                    "the door's own bar, and this install has never attempted a raid"),

                // ── THE FAIL-OPEN DEFAULTS. Every row is a frame that really happens, and
                //    every one would light the badge if the rails were read naively
                //    (PostureSignals.cs:237/:311, RaidEntryGate.cs:78-79).
                (true, true, 3, 0, true, 3, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.Unpublished,
                    "ARMY NEVER PUBLISHED: Version 0 means Ready and DeployableSlots are struct " +
                    "defaults - and Ready DEFAULTS TRUE. Also true headless, where " +
                    "ArmyReadiness.Compute(null) returns READY by design so it can never false-block"),
                (true, false, 3, 7, true, 3, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.Unpublished,
                    "HEARTFIRE NEVER PUBLISHED: HeartfireLit still reads its pre-publish ceiling of 3. " +
                    "This is the title screen, every headless capture and the first frames of any boot"),
                (false, false, 0, 0, false, 0, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.Unpublished,
                    "a genuinely fresh, pre-publish save - nothing may be SAID, not even a blocker, " +
                    "because an unread rail cannot be told from its own default"),

                // ── THE REAL BLOCKERS, in the order the player must solve them.
                (false, true, 3, 7, true, 3, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.NoBarracks,
                    "NO BARRACKS: the raid door REFUSES (WO-1374 capability gate), so rung 1 is the " +
                    "helper - and a 'ready' badge beside a locked Raids card would be a lie"),
                (true, true, 3, 7, false, 1, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.ArmyShort,
                    "ONE DEPLOYABLE TROOP AND !Ready - THE ROW THAT CAUGHT THIS LANE'S OWN BUG. An " +
                    "earlier draft judged the army by DeployableSlots > 0, which is TRUE here, so it " +
                    "would have raised the door prompt; RaidSelectionScreen.Open would then have " +
                    "toasted and redirected to the drillmaster. The bar is RequiredSlots (3 under the " +
                    "WO-823 soft gate), which is exactly what ArmyStatus.Ready carries"),
                (true, true, 3, 7, false, 0, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.ArmyShort,
                    "no troops at all, barracks present - still rung 2, train"),
                (true, true, 0, 7, true, 3, false,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.NoHeartfire,
                    "HEARTFIRE EMPTY: the ONE gate on WHEN a raid may start (WO-1379) is shut. No " +
                    "helper - it is a clock, not an action, and asking the player to wait is noise"),

                // ── The stop condition. This is what makes it a prompt and not nagware.
                (true, true, 3, 7, true, 3, true,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.Attempted,
                    "ALREADY ATTEMPTED outranks EVERYTHING - the funnel latch is set, so this player " +
                    "has found the door"),
                (false, true, 0, 7, false, 0, true,
                    DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.Attempted,
                    "a VETERAN whose barracks was destroyed and army lost must NOT be handed the FTUE " +
                    "helper chain - 'attempted' is checked before any blocker is named"),
            };

            foreach (var r in rows)
            {
                var got = DeNelle.Core.HudModel.RaidDoorReadiness.Diagnose(
                    r.cap, r.hfPub, r.hf, r.ver, r.ready, r.slots, r.done, out string why);
                if (got != r.want)
                    failures.Add("[fail-closed] Diagnose(capable=" + r.cap + ", hfPublished=" + r.hfPub +
                        ", hfLit=" + r.hf + ", armyVersion=" + r.ver + ", armyReady=" + r.ready +
                        ", deployable=" + r.slots + ", attempted=" + r.done + ") returned " + got +
                        ", expected " + r.want + ". ROW: " + r.why + ". The predicate said: \"" + why + "\"");
                if (string.IsNullOrWhiteSpace(why))
                    failures.Add("[fail-closed] Diagnose returned " + got + " with an EMPTY reason. A " +
                        "predicate that cannot explain itself makes every badge defect a guess, which " +
                        "is what CLAUDE.md section 11B forbids by name.");

                // Evaluate must be exactly "Diagnose == Ready" - two predicates would drift.
                bool ev = DeNelle.Core.HudModel.RaidDoorReadiness.Evaluate(
                    r.cap, r.hfPub, r.hf, r.ver, r.ready, r.slots, r.done, out _);
                if (ev != (got == DeNelle.Core.HudModel.RaidDoorReadiness.RaidBlocker.Ready))
                    failures.Add("[fail-closed] Evaluate and Diagnose DISAGREE on the row: " + r.why +
                        ". They must be one rule - the badge reads Evaluate and the helper chain reads " +
                        "Diagnose, and a surface disagreeing with the chain beside it is the defect " +
                        "PlayerDeckWorkspace already records for the Raids card vs the action bar.");
            }

            // The badge strings must be ASCII (a non-ASCII glyph renders as TOFU on device) and
            // must fit the plate they are drawn on.
            foreach (var pair in new[]
            {
                ("DockBadgeGlyph", DeNelle.Core.HudModel.RaidDoorReadiness.DockBadgeGlyph),
                ("CardBadgeWord", DeNelle.Core.HudModel.RaidDoorReadiness.CardBadgeWord),
            })
            {
                if (string.IsNullOrEmpty(pair.Item2))
                { failures.Add("[fail-closed] RaidDoorReadiness." + pair.Item1 + " is empty."); continue; }
                foreach (char c in pair.Item2)
                    if (c > 0x7E || c < 0x20)
                    {
                        failures.Add("[fail-closed] RaidDoorReadiness." + pair.Item1 + " contains the " +
                            "non-ASCII/control char U+" + ((int)c).ToString("X4") + " - it would render " +
                            "as TOFU on device (binding project rule).");
                        break;
                    }
            }
            if (DeNelle.Core.HudModel.RaidDoorReadiness.DockBadgeGlyph.Length > 2)
                failures.Add("[fail-closed] DockBadgeGlyph is '" +
                    DeNelle.Core.HudModel.RaidDoorReadiness.DockBadgeGlyph + "' (" +
                    DeNelle.Core.HudModel.RaidDoorReadiness.DockBadgeGlyph.Length + " chars). " +
                    "ElarionUiKit.StyleAsStackBadge's plate is 52x40 reference px, authored for up to " +
                    "THREE DIGITS - anything longer ellipsises and the always-on affordance disappears " +
                    "while the code still looks correct. The WORDS belong on the deck card.");
        }

        // =====================================================================
        //  C [route-real]
        // =====================================================================

        private static void CaseC_RouteReal(Dictionary<string, JObject> steps,
                                            Dictionary<string, JObject> dialogues,
                                            List<string> failures, List<string> notes)
        {
            if (steps == null) return;

            var known = new HashSet<string>(DeNelle.Core.UI.TutorialHighlightRegistry.KnownIds,
                                            StringComparer.Ordinal);

            // The two ids this ticket introduced are the reason a raid route can exist at all.
            foreach (string required in new[] { "hud.journey_button", "deck.card.raids" })
                if (!known.Contains(required))
                    failures.Add("[route-real] TutorialHighlightRegistry.KnownIds is missing '" + required +
                        "'. These are the FIRST Journey/Raids anchors in the game - ctx_raid_first and " +
                        "ctx_heartfire each recorded in their notes that no such id existed. Removing " +
                        "one returns the raid route to words-only.");

            int authoredTotal = 0;
            foreach (var rung in Rungs)
            {
                if (!steps.TryGetValue(rung.BeatId, out var beat)) continue;

                var authored = new List<string>();
                foreach (var h in beat["highlight"]?.OfType<JValue>() ?? Enumerable.Empty<JValue>())
                {
                    string id = (string)h.Value;
                    if (!string.IsNullOrEmpty(id)) authored.Add(id);
                }
                var hops = beat["route"]?.OfType<JObject>().ToList() ?? new List<JObject>();
                foreach (var hop in hops)
                {
                    string id = (string)hop["highlight"];
                    if (!string.IsNullOrEmpty(id)) authored.Add(id);   // "" = release, legal
                }
                authoredTotal += authored.Count;

                foreach (string id in authored)
                    if (!known.Contains(id))
                        failures.Add("[route-real] " + rung.BeatId + " authors highlight '" + id +
                            "', which is NOT in TutorialHighlightRegistry.KnownIds. DataRegression reds " +
                            "an invented id, and at runtime it resolves to nothing - a spotlight " +
                            "pointing at empty space while the JSON looks correct.");

                // A rung with neither a spotlight nor a route must at least say where to go in
                // WORDS - the owner is red/green colourblind and a resolver degrades to nothing.
                if (authored.Count == 0)
                {
                    string words = ((string)beat["hint"] ?? "") + " " +
                                   ((string)beat["objective"]?["text"] ?? "");
                    if (string.IsNullOrWhiteSpace(words))
                        failures.Add("[route-real] " + rung.BeatId + " authors no highlight AND no " +
                            "hint/objective words - it would point at nothing in either channel.");
                    else
                        notes.Add(rung.BeatId + " has no first-hop spotlight and carries its route in " +
                                  "words only (stated in its _note, not an oversight)");
                }

                // The last hop must RELEASE, or the spotlight masks the screen the player reached.
                if (hops.Count > 0)
                {
                    string last = (string)hops[hops.Count - 1]["highlight"];
                    if (!string.IsNullOrEmpty(last))
                        notes.Add("the last route hop of " + rung.BeatId + " still points at '" + last +
                                  "' instead of releasing the spotlight - check it does not mask a panel");
                }
            }
            if (authoredTotal == 0)
                failures.Add("[route-real] the whole chain authors NO highlight at all. The point of " +
                    "this ticket is that the door was invisible; ctx_raid_first and ctx_heartfire " +
                    "already carry their routes in words alone and the funnel still read zero.");

            // Every CTA verb must be ROUTED by the sink, and must carry ITS OWN step id as the
            // marker - an authored verb the sink does not know logs a warning and no-ops (a dead
            // door), and an unmarked CTA is counted as some other beat's tap.
            string sink = ReadText(SinkSrc, failures);
            if (dialogues != null && sink != null)
            {
                string prefix = DeNelle.Village.DialogueCommandSink.RaidChainCtaArgPrefix;
                foreach (var rung in Rungs)
                {
                    if (!dialogues.TryGetValue(rung.DialogueId, out var rec)) continue;
                    var doors = CommandsOf(rec)
                        .Where(c => IsDoorVerb((string)c["verb"])).ToList();
                    if (doors.Count == 0)
                    {
                        failures.Add("[route-real] '" + rung.DialogueId + "' has no door command - the " +
                            "helper would say what to do and not offer to do it. The owner's direction " +
                            "was 'click here to put your barracks', i.e. a CTA, not an instruction.");
                        continue;
                    }
                    if (doors.Count > 1)
                        failures.Add("[route-real] '" + rung.DialogueId + "' has " + doors.Count +
                            " door commands - one rung, one door (every other ctx_ record here).");

                    foreach (var door in doors)
                    {
                        string verb = (string)door["verb"];
                        if (!sink.Contains("case \"" + verb + "\""))
                            failures.Add("[route-real] dialogue verb '" + verb + "' (in '" +
                                rung.DialogueId + "') is NOT routed by DialogueCommandSink - it falls " +
                                "to the default arm, logs a warning and NO-OPS. A dead door.");
                        var args = door["args"]?.OfType<JValue>()
                                       .Select(a => (string)a.Value).ToList() ?? new List<string>();
                        if (!args.Any(a => string.Equals(a, rung.BeatId, StringComparison.OrdinalIgnoreCase)))
                            failures.Add("[route-real] '" + rung.DialogueId + "' fires " + verb +
                                " without its own step id ('" + rung.BeatId + "') as the tap marker " +
                                "(args: " + string.Join("|", args) + "). raid_door_prompt_tapped would " +
                                "then not name this rung - or worse, the marker drifts and the event " +
                                "silently counts another beat's use of the SAME shared door, so this " +
                                "ticket's conversion rate would include traffic it did not earn.");
                        if (!args.Any(a => !string.IsNullOrEmpty(a) &&
                                           a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                            failures.Add("[route-real] '" + rung.DialogueId + "' marker does not start " +
                                "with DialogueCommandSink.RaidChainCtaArgPrefix ('" + prefix +
                                "'), which is what TrackRaidChainTap matches on - the tap is silent.");
                    }
                }

                if (!sink.Contains("raid_door_prompt_tapped"))
                    failures.Add("[route-real] DialogueCommandSink does not emit raid_door_prompt_tapped " +
                        "- the tap half of the instrumentation is missing, and the live data could not " +
                        "distinguish 'never shown' from 'shown and ignored'.");
                if (!sink.Contains("EnterBuildModeForStructure"))
                    failures.Add("[route-real] the sink no longer calls " +
                        "BuildModeController.EnterBuildModeForStructure - the owner's 'click here to PUT " +
                        "your barracks' becomes 'open the builder and go hunting', and WO-1571 recorded " +
                        "that the category root is a dead end BY CONSTRUCTION for rows with no " +
                        "authored collection.");

                // The two shared doors OTHER beats use must stay UNMARKED.
                foreach (var other in new[] { "tut_ctx_heartfire", "tut_ctx_post_raid" })
                {
                    if (!dialogues.TryGetValue(other, out var rec)) continue;
                    foreach (var c in CommandsOf(rec).Where(c => IsDoorVerb((string)c["verb"])))
                    {
                        var args = c["args"]?.OfType<JValue>()
                                       .Select(a => (string)a.Value).ToList() ?? new List<string>();
                        if (args.Any(a => !string.IsNullOrEmpty(a) &&
                                          a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                            failures.Add("[route-real] '" + other + "' now carries a raid-chain tap " +
                                "marker on its " + (string)c["verb"] + " door, so " +
                                "raid_door_prompt_tapped counts two different beats' CTAs as one. That " +
                                "is the measurement the marker exists to keep separate.");
                    }
                }
            }

            // The emitter and the shown-event must exist.
            string adapters = ReadText(AdaptersSrc, failures);
            if (adapters != null)
            {
                foreach (var rung in Rungs)
                    if (!adapters.Contains(SignalConstName(rung.Trigger)))
                        failures.Add("[route-real] TutorialSignalAdapters never raises the trigger for " +
                            rung.BeatId + " (" + rung.Trigger + ") - that rung has no live emitter, so " +
                            "it can never fire once (the dead-beat class tutorial-reach Case 1 exists " +
                            "to prevent).");
                if (!adapters.Contains("RaidDoorReadiness"))
                    failures.Add("[route-real] the emitter does not consult RaidDoorReadiness. It would " +
                        "be a SECOND definition of 'what is blocking this player', and it would drift " +
                        "from the two badges - exactly the defect PlayerDeckWorkspace records when the " +
                        "Raids card and the action bar each kept their own answer.");
                if (!adapters.Contains("BattleLock.IsInBattle"))
                    failures.Add("[route-real] the emitter does not refuse during a battle " +
                        "(BattleLock.IsInBattle). The owner's rule is that these never show during a " +
                        "wave, and their doors open panels - mid-fight that is worse than useless.");
                if (!adapters.Contains("PlansRevealedPrefix"))
                    failures.Add("[route-real] the emitter no longer consumes " +
                        "TutorialSignals.PlansRevealedPrefix - WO-1804's plans drops hand into this " +
                        "chain through that family ('plans.revealed:battle' / ':bastion', raised by " +
                        "BattlePlansPickup.SignalFor), and without it the two lanes do not meet. Match " +
                        "the PREFIX, not one id: a third plans kind must need no edit here.");
            }
            string flow = ReadText(FlowSrc, failures);
            if (flow != null && !flow.Contains("raid_door_prompt_shown"))
                failures.Add("[route-real] TutorialFlow does not emit raid_door_prompt_shown - without " +
                    "it there is no way to tell a player who never saw a helper from one who ignored " +
                    "it, which is the question the live triage could not answer.");

            // ── THE TWO ALWAYS-ON BADGES READ THE SHARED PREDICATE, AND FAIL CLOSED ──
            // Source-text, because neither surface can be built here: one is a dock medallion and
            // the other is a deck card rendered per page. What is pinned is the WIRING - that each
            // surface asks RaidDoorReadiness rather than keeping its own answer, which is the
            // precise defect PlayerDeckWorkspace's own comment records for the Raids card vs the
            // action bar ("a second check would drift from the first, and the drift is the actual
            // defect").
            foreach (var surface in new[]
            {
                ("Assets/_Modules/HUD/Kit/HudKitController.cs", "the dock JOURNEY face badge"),
                ("Assets/_Modules/HUD/PlayerDeckWorkspace.cs", "the Journey deck Raids card badge"),
            })
            {
                string src = ReadText(surface.Item1, failures);
                if (src == null) continue;
                if (!src.Contains("RaidDoorReadiness"))
                    failures.Add("[route-real] " + surface.Item2 + " does not consult " +
                        "RaidDoorReadiness - it would be a SECOND answer to 'is the raid door open', " +
                        "and the badge would eventually contradict the helper chain beside it.");
            }

            // The deck card's REWARD line must be gated on the published rail, or a pre-publish /
            // headless frame advertises a payout nothing stands behind.
            string deck = ReadText("Assets/_Modules/HUD/PlayerDeckWorkspace.cs", failures);
            if (deck != null && deck.Contains("RaidFirstSpoilsGold") &&
                !deck.Contains("IsNullOrEmpty(goldClause)"))
                failures.Add("[route-real] the Raids card reads PostureSignals.RaidFirstSpoilsGold " +
                    "without a null/empty guard. NULL is that rail's \"not published\" sentinel (the " +
                    "ArmyFillCap == 0 / RaidNextCampName == null convention), so an unguarded read " +
                    "would print an empty or fabricated reward clause on every pre-publish frame.");
            if (deck != null && !deck.Contains("RaidFirstSpoilsGold"))
                notes.Add("the Raids card does not show the first-raid reward clause - the owner asked " +
                          "for it on the badge line (2026-09-16)");
        }

        // =====================================================================
        //  D [once-plus-one]
        // =====================================================================

        private static void CaseD_OncePlusOne(List<string> failures, List<string> notes)
        {
            var rungs = DeNelle.Village.TutorialFlow.RaidChainRungs;
            if (rungs == null || rungs.Length != Rungs.Length)
            {
                failures.Add("[once-plus-one] TutorialFlow.RaidChainRungs has " +
                    (rungs == null ? "null" : rungs.Length.ToString()) + " entries; this suite knows " +
                    Rungs.Length + ". The rung table is the single list the emitter, the interpreter's " +
                    "instrumentation and this suite all read - they must not diverge.");
                return;
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rung in rungs)
            {
                if (string.IsNullOrEmpty(rung.BeatId))
                { failures.Add("[once-plus-one] a RaidChainRungs entry has an empty BeatId."); continue; }
                if (!DeNelle.Village.TutorialFlow.IsRaidChainBeat(rung.BeatId))
                    failures.Add("[once-plus-one] IsRaidChainBeat('" + rung.BeatId + "') is FALSE for a " +
                        "rung in its own table - the shown/tapped instrumentation would skip that rung.");
                if (string.IsNullOrEmpty(rung.RearmLedgerKey) || !rung.RearmLedgerKey.Contains("."))
                    failures.Add("[once-plus-one] rung '" + rung.BeatId + "' re-arm key is '" +
                        (rung.RearmLedgerKey ?? "<null>") + "' - it must be namespaced (e.g. " +
                        "'tutorial.'), because VillageInventory.HasEverAcquired reads the SAME acquired " +
                        "list for item discovery and a bare id there makes a phantom item look " +
                        "discovered.");
                if (!seenKeys.Add(rung.RearmLedgerKey ?? ""))
                    failures.Add("[once-plus-one] two rungs share the re-arm ledger key '" +
                        rung.RearmLedgerKey + "' - the first re-arm spent would consume every rung's " +
                        "second chance at once.");
            }

            string flow = ReadText(FlowSrc, failures);
            if (flow != null)
            {
                if (!flow.Contains("TryRearmContextualOnce"))
                    failures.Add("[once-plus-one] TutorialFlow has no TryRearmContextualOnce - a beat " +
                        "latched by its oneShot flag would be gone forever after one dismissal, and the " +
                        "owner ruled each helper gets one second chance on a later session.");
                if (!flow.Contains("MarkEverAcquired"))
                    failures.Add("[once-plus-one] the re-arm does not spend a monotonic ledger key. " +
                        "Without that latch the re-arm repeats every session, which is nagware and " +
                        "would teach reflex dismissal - the cap is ONE.");
                // The key grammar must stay in ONE file: a caller composing "tutorial_ctx:" itself
                // would un-latch nothing and look exactly like a re-dismissal.
                if (!flow.Contains("CtxSeenPrefix + ctxId"))
                    notes.Add("TutorialFlow.TryRearmContextualOnce no longer composes the seen key from " +
                              "CtxSeenPrefix - check the prefix has not been duplicated into a caller");
            }

            string adapters = ReadText(AdaptersSrc, failures);
            if (adapters != null)
            {
                if (!adapters.Contains("TryRearmContextualOnce"))
                    failures.Add("[once-plus-one] the emitter never asks for the re-arm, so the ONE " +
                        "second chance is unreachable code.");
                if (!adapters.Contains("RaidChainRungs"))
                    failures.Add("[once-plus-one] the emitter does not re-arm from RaidChainRungs - if " +
                        "it re-arms only the last rung, a player who dismissed 'build a Barracks' is " +
                        "stuck forever at the rung where nothing downstream can become reachable.");
                if (!adapters.Contains("RaidFunnel.FirstRaidAttempted"))
                    failures.Add("[once-plus-one] the re-arm is not conditioned on the install never " +
                        "having attempted a raid - it would re-prompt a player who already raided.");
                if (!adapters.Contains("IsContextualSeen"))
                    failures.Add("[once-plus-one] the emitter does not stop on a beat's own latch, so it " +
                        "would re-raise the trigger forever.");
            }
        }

        // =====================================================================
        //  E [copy-projected] - no digits, and every token registered
        // =====================================================================

        private static void CaseE_CopyProjected(Dictionary<string, JObject> steps,
                                                 Dictionary<string, JObject> dialogues,
                                                 List<string> failures, List<string> notes)
        {
            // Register without a scene load, exactly as the runtime does on boot.
            DeNelle.Village.RaidDoorBeatTokens.Register();
            DeNelle.Village.PostRaidBeatTokens.Register();

            var token = new Regex(@"\{([^{}]+)\}");
            var digit = new Regex(@"[0-9]");

            // Everything a PLAYER can read, across both files, for all three rungs.
            var visible = new List<(string where, string text)>();
            if (steps != null)
                foreach (var rung in Rungs)
                {
                    if (!steps.TryGetValue(rung.BeatId, out var beat)) continue;
                    Add(visible, rung.BeatId + ".hint", (string)beat["hint"]);
                    Add(visible, rung.BeatId + ".objective", (string)beat["objective"]?["text"]);
                    foreach (var hop in beat["route"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                        Add(visible, rung.BeatId + ".route.hint", (string)hop["hint"]);
                }
            if (dialogues != null)
                foreach (var rung in Rungs)
                {
                    if (!dialogues.TryGetValue(rung.DialogueId, out var rec)) continue;
                    foreach (var ln in LinesOf(rec))
                        Add(visible, rung.DialogueId + ".line", (string)ln["text"]);
                    foreach (var n in rec["nodes"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                        foreach (var o in n["options"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                            Add(visible, rung.DialogueId + ".option", (string)o["text"]);
                }

            if (visible.Count == 0)
            {
                failures.Add("[copy-projected] found NO player-visible strings across the three rungs - " +
                    "either the rungs are missing or this case is reading the wrong fields, and a case " +
                    "that inspects nothing passes everything.");
                return;
            }

            foreach (var (where, text) in visible)
            {
                // ── NO DIGIT OUTSIDE A {token}. THIS IS THE CASE THE OWNER'S OWN RULING
                //    DEMANDED: the starter army went from 3 to TEN on the day this lane was
                //    written, with a SIBLING LANE (WO-1803) owning the knob, so any literal
                //    would have been wrong within hours. The spoils have the same shape -
                //    they come from the settle payout's own formula, and a hardcoded figure
                //    would advertise a rate the game does not pay.
                string withoutTokens = token.Replace(text, "");
                if (digit.IsMatch(withoutTokens))
                    failures.Add("[copy-projected] " + where + " carries a hardcoded digit: \"" + text +
                        "\". Every number in this chain's copy must be a {token} resolved from a LIVE " +
                        "projection - the troop count from the published army snapshot (the starter " +
                        "army moved 3 -> 10 on 2026-09-16 and WO-1803 owns the knob), the spoils from " +
                        "RaidScoring.EstimateSpoils, the camp from the one ladder authority.");

                // Every token used must be registered, or it reaches the player as literal braces.
                foreach (Match m in token.Matches(text))
                {
                    string key = m.Groups[1].Value;
                    if (!DeNelle.Core.Dialogue.DialogueTextTokens.IsRegistered(key))
                        failures.Add("[copy-projected] " + where + " uses the token \"{" + key +
                            "}\", which NO resolver registers - DialogueTextTokens leaves an unknown " +
                            "token untouched, so the player reads the literal braces on screen.");
                }

                // ASCII only: a non-ASCII glyph is TOFU on device (binding project rule).
                foreach (char c in text)
                    if (c > 0x7E || c < 0x20)
                    {
                        failures.Add("[copy-projected] " + where + " contains U+" +
                            ((int)c).ToString("X4") + " - non-ASCII renders as TOFU on device.");
                        break;
                    }
            }

            // The reward/free-army promise must actually BE made somewhere, or "announce early
            // what the rewards are" silently did not ship.
            string all = string.Join(" ", visible.Select(v => v.text));
            foreach (var needed in new[]
            {
                DeNelle.Village.RaidDoorBeatTokens.FirstCamp,
                DeNelle.Village.RaidDoorBeatTokens.ArmyDeployable,
            })
                if (!all.Contains("{" + needed + "}"))
                    failures.Add("[copy-projected] no rung's copy uses \"{" + needed + "}\". The owner " +
                        "ruled the chain must announce the reward early and say the first raid is free " +
                        "with the army already granted; a chain that names neither is the ticket not " +
                        "shipping its second half.");
            if (!all.Contains("{" + DeNelle.Village.RaidDoorBeatTokens.FirstSpoils + "}") &&
                !all.Contains("{" + DeNelle.Village.RaidDoorBeatTokens.FirstGold + "}"))
                failures.Add("[copy-projected] no rung quotes the spoils projection (neither {" +
                    DeNelle.Village.RaidDoorBeatTokens.FirstSpoils + "} nor {" +
                    DeNelle.Village.RaidDoorBeatTokens.FirstGold + "}) - 'announce early what the " +
                    "rewards are' is the desire half of the owner's direction.");

            // ── "REGISTERED" IS NOT "RESOLVED ON THIS SURFACE" ──────────────────
            // THE BUG THIS ASSERTION EXISTS FOR, caught in review before it shipped: objective
            // text went through TutorialGuide.ResolveToken ONLY (the {guide} swap) and a coach-mark
            // hint went through NOTHING - it was handed straight to ShowToast. Harmless for every
            // earlier step, because {guide} was the only token any of them carried. These rungs
            // author {raid.first.camp} and {raid.army.deployable} into objective.text, hint and
            // route hints, so on those two surfaces the player would have read LITERAL BRACES on
            // the objective banner and the coach toast of the very beat whose job is to be obvious.
            // The registry cannot reach a surface that never calls it, so the CALL is what is pinned.
            string flowSrc = ReadText(FlowSrc, failures);
            if (flowSrc != null && !flowSrc.Contains("DialogueTextTokens.Resolve"))
                failures.Add("[copy-projected] TutorialFlow never calls DialogueTextTokens.Resolve. " +
                    "The objective strip and the coach-mark toast are their OWN render surfaces - " +
                    "registering a resolver does not reach them. Every {token} this chain authors in " +
                    "objective.text / hint / route hints would print as literal braces.");

            // Every key the token file claims to own must really be registered.
            foreach (string key in DeNelle.Village.RaidDoorBeatTokens.Keys)
                if (!DeNelle.Core.Dialogue.DialogueTextTokens.IsRegistered(key))
                    failures.Add("[copy-projected] RaidDoorBeatTokens.Keys lists '" + key +
                        "' but Register() did not register it - the Keys array and the registrations " +
                        "have drifted, which is the duplicated-state failure in miniature.");

            // And the fallbacks must be number-free, or a failed projection reintroduces a literal.
            foreach (var fb in new[]
            {
                ("SpoilsFallback", DeNelle.Village.RaidDoorBeatTokens.SpoilsFallback),
                ("CampFallback", DeNelle.Village.RaidDoorBeatTokens.CampFallback),
            })
                if (string.IsNullOrWhiteSpace(fb.Item2) || digit.IsMatch(fb.Item2))
                    failures.Add("[copy-projected] RaidDoorBeatTokens." + fb.Item1 + " is '" + fb.Item2 +
                        "' - a fallback must be a non-empty, number-free sentence. A blank leaves a hole " +
                        "in the line and a number is a figure nothing stands behind.");
        }

        // =====================================================================
        //  Plumbing
        // =====================================================================

        private static void Add(List<(string, string)> sink, string where, string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) sink.Add((where, text));
        }

        private static bool IsDoorVerb(string verb) =>
            string.Equals(verb, "OpenRaids", StringComparison.Ordinal) ||
            string.Equals(verb, "OpenManageTroops", StringComparison.Ordinal) ||
            string.Equals(verb, "BuildStructure", StringComparison.Ordinal);

        /// <summary>The C# constant NAME for a signal id, so the source-text scan looks for
        /// <c>TutorialSignals.RaidHelperBarracks</c> rather than for the string literal (which the
        /// emitter correctly never types).</summary>
        private static string SignalConstName(string signalId)
        {
            if (string.Equals(signalId, DeNelle.Core.Tutorial.TutorialSignals.RaidHelperBarracks,
                              StringComparison.Ordinal)) return "RaidHelperBarracks";
            if (string.Equals(signalId, DeNelle.Core.Tutorial.TutorialSignals.RaidHelperArmy,
                              StringComparison.Ordinal)) return "RaidHelperArmy";
            return "RaidDoorReady";
        }

        private static IEnumerable<JObject> LinesOf(JObject record) =>
            record["nodes"]?.OfType<JObject>()
                .SelectMany(n => n["lines"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            ?? Enumerable.Empty<JObject>();

        private static IEnumerable<JObject> CommandsOf(JObject record) =>
            record["nodes"]?.OfType<JObject>()
                .SelectMany(n => n["commands"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            ?? Enumerable.Empty<JObject>();

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// Every authored step by id, read out of BOTH canonical copies. Read from JSON rather than
        /// through TutorialStepCatalog on purpose (the tutorial-reach precedent): the authored file
        /// is the truth, so the suite still reports the real defect if a C# field mapping is
        /// dropped. Both copies are compared because a Resources/StreamingAssets divergence ships a
        /// different tutorial to WebGL than to the APK.
        /// </summary>
        private static Dictionary<string, JObject> LoadSteps(List<string> failures)
        {
            string res = ReadText(StepsRes, failures);
            string sa = ReadText(StepsSA, failures);
            if (res == null) return null;
            if (sa != null && !string.Equals(res, sa, StringComparison.Ordinal))
                failures.Add("[source] tutorial-steps.json differs between the Resources and " +
                    "StreamingAssets copies - they must be byte-identical or the platforms run " +
                    "different tutorials.");

            JObject root;
            try { root = JObject.Parse(res); }
            catch (Exception ex)
            { failures.Add("[source] tutorial-steps.json did not parse: " + ex.Message); return null; }

            var map = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var s in root["steps"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string id = (string)s["id"];
                if (string.IsNullOrEmpty(id)) continue;
                if (map.ContainsKey(id))
                {
                    failures.Add("[source] step '" + id + "' is authored more than once - " +
                        "TryTriggerContextual takes the FIRST match, so the others are invisible.");
                    continue;
                }
                map[id] = s;
            }
            return map;
        }

        /// <summary>How many MANDATORY (non-contextual) beats the file carries. -1 on a read/parse
        /// failure already reported elsewhere.</summary>
        private static int CountMandatory(List<string> failures)
        {
            string res = ReadText(StepsRes, failures);
            if (res == null) return -1;
            try
            {
                var root = JObject.Parse(res);
                return root["steps"]?.OfType<JObject>()
                    .Count(s => !string.Equals((string)s["flowId"], "contextual",
                                               StringComparison.OrdinalIgnoreCase)) ?? -1;
            }
            catch { return -1; }
        }

        private static Dictionary<string, JObject> LoadDialogues(List<string> failures)
        {
            string res = ReadText(DialoguesRes, failures);
            string sa = ReadText(DialoguesSA, failures);
            if (res == null) return null;
            if (sa != null && !string.Equals(res, sa, StringComparison.Ordinal))
                failures.Add("[source] dialogues.json differs between the Resources and " +
                    "StreamingAssets copies - they must be byte-identical.");

            JObject root;
            try { root = JObject.Parse(res); }
            catch (Exception ex)
            { failures.Add("[source] dialogues.json did not parse: " + ex.Message); return null; }

            var map = new Dictionary<string, JObject>(StringComparer.Ordinal);
            // The record array's key is DISCOVERED rather than assumed - the file's top level also
            // carries a "speakers" array, and hardcoding the wrong one is a silent empty result.
            foreach (var prop in root.Properties())
            {
                if (!(prop.Value is JArray arr)) continue;
                foreach (var r in arr.OfType<JObject>())
                {
                    string id = (string)r["id"];
                    if (string.IsNullOrEmpty(id) || r["nodes"] == null) continue;
                    if (!map.ContainsKey(id)) map[id] = r;
                }
            }
            return map;
        }

        private static string ReadText(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("[source] missing file: " + path);
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("[source] could not read " + path + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }
    }
}
