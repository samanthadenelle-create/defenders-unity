// =============================================================================
// SessionShapeRegression — WO-1027: the ache is carried by SHAPE and NUMBER,
// and never by hue.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Markers: SESSION_SHAPE_OK / SESSION_SHAPE_FAIL
//
// WHY A DISTINCT MARKER (canon §8): a shared REGRESSION_OK is how a 22-case suite's pass
// once read as the whole suite's pass. This one says its own name.
//
// ⚠ THE ASSEMBLY LINE THIS SUITE IS WRITTEN AGAINST: DeNelle.EditorRegression references
// DeNelle.Core and DeNelle.Village but NOT DeNelle.HUD. So everything about
// ObsidianQueueGate / HudActionBarModel / QueueRailView (all Core) is BEHAVIOURAL — the
// real code is called — and anything about HudKitController (HUD) has to be a SOURCE LINT.
//
// The greyscale bar (0.45 Rec.709) is this repo's own, set by TalentFocusSingletonRegression.
// The owner is red/green colourblind: every state must be separable with colour stripped.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Core.Jobs;
using DeNelle.Core.HudModel;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class SessionShapeRegression
    {
        /// <summary>Minimum Rec.709 luma separation for a cue to survive a GREYSCALE pass.
        /// Same bar the talent oracle sets — deliberately far above "just visible".
        /// The free queue card scored 0.015 against the busy card before WO-1027.</summary>
        private const float MinGreyscaleLumaGap = 0.45f;

        private const string GateSrc = "Assets/_Modules/Core/UI/ObsidianQueueGate.cs";
        private const string ModelSrc = "Assets/_Modules/Core/HudModel/HudActionBarModel.cs";
        private const string RailSrc = "Assets/_Modules/Core/UI/QueueRailView.cs";
        private const string ViewSrc = "Assets/_Modules/HUD/Kit/HudKitController.cs";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("SESSION_SHAPE_OK - " + reason);
            else Debug.LogError("SESSION_SHAPE_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "idle-authority", () => Case1_IdleAuthority(failures, notes));
                Case(failures, "no-depth-input", () => Case2_NoDepthInput(failures, notes));
                Case(failures, "greyscale", () => Case3_Greyscale(failures, notes));
                Case(failures, "no-hue-signal", () => Case4_NoHueSignal(failures, notes));
                Case(failures, "bar-shape", () => Case5_BarShape(failures, notes));
                Case(failures, "tell-is-a-transition", () => Case6_Transition(failures, notes));
                Case(failures, "one-queues-door", () => Case7_OneDoor(failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes.ToArray()) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "SESSION SHAPE OK - one idle-line authority (idle == zero ACTIVE, never " +
                         "Busy<Slots), the empty socket clears the greyscale bar by SHAPE, the calm " +
                         "bar shows the bare word, and the bar keeps 6 visible / 7 identities" + noteStr;
                return true;
            }
            reason = "session-shape FAIL x" + failures.Count + ": " +
                     string.Join(" | ", failures.ToArray()) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1 - the ONE definition of idle, and the boot lie it must not tell
        // =====================================================================
        private static void Case1_IdleAuthority(List<string> failures, List<string> notes)
        {
            // Nothing published yet: the honest answer is 0, never 3. A bar claiming "3 of 3 idle"
            // on a screen that has heard nothing is the WO's own top risk.
            var blank = new ObsidianQueueGate.WorkQueueStatus { Available = false };
            if (blank.IdleLineCount() != 0)
                failures.Add("[idle-authority] an UNAVAILABLE status reports " + blank.IdleLineCount() +
                             " idle lines - before the first publish the count must be 0, never the " +
                             "line count (that is the boot lie)");
            if (blank.AllLinesLoaded())
                failures.Add("[idle-authority] an UNAVAILABLE status claims the session is complete");

            // All three empty.
            var allIdle = Status(0, 2, 0, 2, 0, 1);
            if (allIdle.IdleLineCount() != 3)
                failures.Add("[idle-authority] three empty lines report " + allIdle.IdleLineCount() + " idle, expected 3");

            // ⭐ THE ASSERTION THIS CASE EXISTS FOR: a line running 1 of 2 builders is WORKING.
            // Idle is Busy == 0, NOT Busy < Slots. Conflating them puts "idle" on the bar while a
            // building is visibly under construction, which reads to the player as a bug.
            var partial = Status(1, 2, 0, 2, 0, 1);
            if (partial.IdleLineCount() != 2)
                failures.Add("[idle-authority] a line running 1 of 2 counted as IDLE (got " +
                             partial.IdleLineCount() + ", expected 2) - idle means zero ACTIVE, " +
                             "never Busy < Slots");
            if (partial.IsLineIdle(ChannelId.Builder))
                failures.Add("[idle-authority] IsLineIdle(Builder) true at 1/2 busy");
            if (partial.FreeSlotsOf(ChannelId.Builder) != 1)
                failures.Add("[idle-authority] FreeSlotsOf(Builder) at 1/2 busy = " +
                             partial.FreeSlotsOf(ChannelId.Builder) + ", expected 1 - the FREE-CARD " +
                             "axis is a different, finer fact than the numeral's");

            // Everything busy but NOT full crew: no line is idle, and the session is NOT complete.
            var busyNotFull = Status(1, 2, 1, 2, 1, 1);
            if (busyNotFull.IdleLineCount() != 0)
                failures.Add("[idle-authority] a line with a worker on it counted as idle");
            if (busyNotFull.AllLinesLoaded())
                failures.Add("[idle-authority] session reported COMPLETE while Builder has a free slot - " +
                             "the session-complete predicate must be STRICTER than the numeral's " +
                             "(a wrong 'you are set' is worse than none)");

            var loaded = Status(2, 2, 2, 2, 1, 1);
            if (!loaded.AllLinesLoaded())
                failures.Add("[idle-authority] every line at full crew did NOT report session complete");

            notes.Add("idle authority: blank=0, allEmpty=3, 1of2=2, loaded=complete");
        }

        // =====================================================================
        //  CASE 2 - depth is NOT concurrency
        // =====================================================================
        private static void Case2_NoDepthInput(List<string> failures, List<string> notes)
        {
            foreach (var p in new[] { GateSrc, ModelSrc })
            {
                string src = ReadSrc(p);
                if (src == null) { failures.Add("[no-depth-input] missing source " + p); continue; }
                // The WORD may appear in a comment saying it is NOT an input; a real read would be
                // an access on the config object.
                if (src.IndexOf("BuildTimerConfig", StringComparison.Ordinal) >= 0)
                    failures.Add("[no-depth-input] " + p + " reads BuildTimerConfig - the queue DEPTH " +
                                 "cap (5/line) is the LINE LENGTH and must never feed an idleness or " +
                                 "concurrency answer");
            }

            // Behavioural half: only the ACTIVE counts move the numeral. Queued (waiting) work is
            // depth, and a line with five jobs waiting behind one worker is still exactly as idle.
            var a = Status(0, 2, 0, 2, 0, 1);
            var b = Status(0, 2, 0, 2, 0, 1);
            b.BuilderQueued = 5; b.TrainQueued = 5; b.ResearchQueued = 5;
            if (a.IdleLineCount() != b.IdleLineCount())
                failures.Add("[no-depth-input] queue LENGTH changed the idle count (" +
                             a.IdleLineCount() + " -> " + b.IdleLineCount() + ")");
            notes.Add("depth/concurrency axes stay separate");
        }

        // =====================================================================
        //  CASE 3 ⭐ - the empty socket must survive a GREYSCALE pass
        // =====================================================================
        private static void Case3_Greyscale(List<string> failures, List<string> notes)
        {
            float well = Luma(QueueRailView.FreeSocketPlate);
            float rim = Luma(QueueRailView.FreeSocketRim);
            float gap = Mathf.Abs(rim - well);

            if (gap < MinGreyscaleLumaGap)
                failures.Add("[greyscale] the empty socket separates from its own well by only " +
                             gap.ToString("F3") + " Rec.709 luma (needs >= " +
                             MinGreyscaleLumaGap.ToString("F2") + ") - a hue-free signal that is " +
                             "still invisible fails the colourblind law just as hard as a hue-only one");
            if (QueueRailView.FreeSocketRim.a < 0.5f)
                failures.Add("[greyscale] the socket rim is under 50% alpha - that is a tint, not a shape");

            // The socket must ALSO differ from a busy card in a NON-COLOUR channel, so it reads at a
            // glance from silhouette alone (rhythm: socket, socket, card).
            if (QueueRailView.FreeSocketInsetPx <= 0f)
                failures.Add("[greyscale] the free card has no INSET - shape is what carries this " +
                             "message; a colour-only difference is exactly what was ruled out");
            if (QueueRailView.FreeSocketRimPx <= 0f)
                failures.Add("[greyscale] the socket rim has no thickness");

            string rail = ReadSrc(RailSrc);
            if (rail != null && rail.IndexOf("BuildSocketRim", StringComparison.Ordinal) < 0)
                failures.Add("[greyscale] QueueRailView no longer builds a socket rim - the empty " +
                             "slot is back to being a slightly darker rectangle");

            notes.Add("socket: well " + well.ToString("F3") + " / rim " + rim.ToString("F3") +
                      " => gap " + gap.ToString("F3") + ", inset " + QueueRailView.FreeSocketInsetPx + "px");
        }

        // =====================================================================
        //  CASE 4 - "let me just add a small warm accent to help" is the regression
        // =====================================================================
        private static void Case4_NoHueSignal(List<string> failures, List<string> notes)
        {
            string[] banned = { "Color.red", "Color.green", "Color.yellow", "Color.magenta" };
            foreach (var p in new[] { GateSrc, ModelSrc, RailSrc })
            {
                string src = ReadSrc(p);
                if (src == null) { failures.Add("[no-hue-signal] missing source " + p); continue; }
                foreach (var b in banned)
                    if (src.IndexOf(b, StringComparison.Ordinal) >= 0)
                        failures.Add("[no-hue-signal] " + p + " uses " + b + " - the session-shape " +
                                     "surfaces carry state in SHAPE and NUMBER only; the owner is " +
                                     "red/green colourblind and CoC's red badge is banned outright");
            }

            // The numeral itself must stay a WORD/NUMBER tell, and must never grow a badge glyph.
            string model = ReadSrc(ModelSrc);
            if (model != null && model.IndexOf("ManageBaseLabel = \"Manage\"", StringComparison.Ordinal) < 0)
                failures.Add("[no-hue-signal] the Manage base label is no longer the bare word 'Manage'");
            notes.Add("no hue-only signal in the three session-shape files");
        }

        // =====================================================================
        //  CASE 5 - the bar shape is frozen (no eighth face, no ordinal renumber)
        // =====================================================================
        private static void Case5_BarShape(List<string> failures, List<string> notes)
        {
            if (HudActionBarModel.ButtonCount != 7)
                failures.Add("[bar-shape] ButtonCount is " + HudActionBarModel.ButtonCount +
                             ", expected 7 (enum IDENTITY bound - Map stays dormant at ordinal 4, so " +
                             "dropping it to 6 puts Upgrade out of bounds)");
            // ⚠ WO-1467 - the MaxVisibleFaces pin that sat here is RETIRED. It asserted a literal
            // against a constant that does not describe the shipped bar: BindActionBar returns
            // early once the adaptive peaceful dock exists, so the model is never subscribed and
            // never sizes a medallion. The ordinal pins below DO still bind (every face array is
            // indexed by the enum ordinal) and stay. The dock itself is measured by
            // HudActionBarRegression.CheckMeasuredPeacefulDock.
            if ((int)ActionBarButtonId.Map != 4)
                failures.Add("[bar-shape] ActionBarButtonId.Map moved off ordinal 4 - every face array " +
                             "is indexed by the ordinal, so this silently re-points other faces");
            if ((int)ActionBarButtonId.Upgrade != 6)
                failures.Add("[bar-shape] ActionBarButtonId.Upgrade moved off ordinal 6");
            if (ObsidianQueueGate.WorkQueueStatus.LineCount != 3)
                failures.Add("[bar-shape] LineCount is " + ObsidianQueueGate.WorkQueueStatus.LineCount +
                             ", expected 3 - it is the denominator the glance is written around");
            notes.Add("bar identities frozen: " + HudActionBarModel.ButtonCount +
                      " ordinals, Map dormant@4, Upgrade@6 (face COUNT is measured off the live " +
                      "dock in HudActionBarRegression, never restated here - WO-1467)");
        }

        // =====================================================================
        //  CASE 6 - a TRANSITION, not a per-frame string build; calm is BARE
        // =====================================================================
        private static void Case6_Transition(List<string> failures, List<string> notes)
        {
            var saved = ObsidianQueueGate.Status;
            try
            {
                var model = new HudActionBarModel(new FakeSource());

                // Calm: nothing idle => the face is EXACTLY the bare word. The rejected nudge must
                // not return as a permanent adornment by another door.
                ObsidianQueueGate.PublishStatus(Status(2, 2, 2, 2, 1, 1));
                model.Tick();
                if (!string.Equals(model.ManageFaceLabel, HudActionBarModel.ManageBaseLabel, StringComparison.Ordinal))
                    failures.Add("[calm-is-bare] with nothing idle the Manage face reads '" +
                                 model.ManageFaceLabel + "' - the calm state IS the bare word");

                // Partial: the denominator is what makes the numeral legible without system knowledge.
                ObsidianQueueGate.PublishStatus(Status(0, 2, 1, 2, 1, 1));
                model.Tick();
                if (model.ManageFaceLabel.IndexOf("1 of 3 idle", StringComparison.Ordinal) < 0)
                    failures.Add("[tell] one idle line reads '" + model.ManageFaceLabel +
                                 "', expected the 'N of 3 idle' numeral");

                // All idle: the denominator is noise at N == LineCount.
                ObsidianQueueGate.PublishStatus(Status(0, 2, 0, 2, 0, 1));
                model.Tick();
                if (model.ManageFaceLabel.IndexOf("3 idle", StringComparison.Ordinal) < 0)
                    failures.Add("[tell] three idle lines read '" + model.ManageFaceLabel + "'");

                // EDGE, not frame: republish the SAME shape repeatedly (Version bumps every time)
                // and the View must be woken at most once.
                int raised = 0;
                model.ManageFaceChanged += () => raised++;
                for (int i = 0; i < 12; i++)
                {
                    ObsidianQueueGate.PublishStatus(Status(0, 2, 0, 2, 0, 1));
                    model.Tick();
                }
                if (raised != 0)
                    failures.Add("[tell-is-a-transition] an UNCHANGED status raised ManageFaceChanged " +
                                 raised + " time(s) across 12 publishes - the tell must be edge-" +
                                 "triggered, or it is a per-frame string build and a trace flood");

                notes.Add("manage tell: bare when calm, 'N of 3 idle' when partial, edge-triggered");
            }
            finally
            {
                ObsidianQueueGate.PublishStatus(saved);   // never leave a fixture published
            }
        }

        // =====================================================================
        //  CASE 7 - exactly ONE Queues door, and it is the bar face
        // =====================================================================
        private static void Case7_OneDoor(List<string> failures, List<string> notes)
        {
            string view = ReadSrc(ViewSrc);
            if (view == null) { failures.Add("[one-queues-door] missing source " + ViewSrc); return; }

            // The retired Builders chip must stay retired: un-retiring it to host the ache would
            // quietly overturn an owner ruling to satisfy a stale sentence in an older WO.
            //
            // ⭐ WO-1667 (a) — THE ANCHOR IS THE FULL RETIREMENT LINE, AND THAT IS THE FIX.
            // ---------------------------------------------------------------------
            // This pin used to search for the SHORT form `// BuildQueueStatusChip(pool);`.
            // That substring occurs TWICE in HudKitController.cs (counted at source 2026-09-10):
            //   :811  the real retirement, inside Build()
            //   :1932 a PROSE MENTION of it, inside BuildQueueStatusChip's own doc comment:
            //         "// `// BuildQueueStatusChip(pool);` in Build(), so nothing here ever runs"
            // So THE COMMENT THAT DOCUMENTS THIS PIN SATISFIED THIS PIN. Replace the real line at
            // :811 with a LIVE call and the prose alone kept this case GREEN — the guard survived
            // its own subject. Measured, not argued (WO-1667 a2, four-input mutation table).
            //
            // The full line below occurs exactly ONCE and cannot be satisfied by prose about it.
            // ⚠ THREE SPACES before the second `//` — this was copied from the source line, not
            // retyped, and it must be re-copied if the authoring is ever reflowed.
            // ⛔ Do NOT "fix" a future failure here by deleting the :1932 prose: it is how a reader
            // of BuildQueueStatusChip learns the method never runs. An oracle that forces useful
            // comments to be deleted is a worse oracle.
            // ⚠ SCOPE, deliberately: this catches the retirement line being REMOVED or EDITED. It
            // does NOT catch a live BuildQueueStatusChip(pool) call added ELSEWHERE while this line
            // stays intact — that is HudLabelFitRegression's 15d (WO-1666 §5), which walks every
            // occurrence and asks whether its own line comments it out. The two are DISJOINT ON
            // PURPOSE; widening this one to duplicate 15d would put two oracles on one fact.
            const string RetirementLine =
                "// BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)";
            if (view.IndexOf(RetirementLine, StringComparison.Ordinal) < 0)
                failures.Add("[one-queues-door] the Builders chip's retirement line is gone from " +
                             "HudKitController - the chip was retired by the owner on 2026-08-07 and " +
                             "the bar's Manage face is the single Queues entry. Looked for the FULL " +
                             "line ('" + RetirementLine + "'), because the short form is also present " +
                             "in the prose at :1932 that describes this very pin (WO-1667a)");

            // The View paints the model's words and decides nothing.
            if (view.IndexOf("ApplyManageFaceTell", StringComparison.Ordinal) < 0)
                failures.Add("[one-queues-door] HudKitController no longer paints the Manage face tell");
            if (view.IndexOf("_barModel.ManageFaceChanged", StringComparison.Ordinal) < 0)
                failures.Add("[one-queues-door] HudKitController does not subscribe ManageFaceChanged - " +
                             "a View that polls instead would be re-introducing a predicate");

            // Ruling (c): no nudge toast, ever.
            string model = ReadSrc(ModelSrc);
            if (model != null && model.IndexOf("Toast", StringComparison.Ordinal) >= 0)
                failures.Add("[one-queues-door] HudActionBarModel mentions a Toast - WO-1027 ruling (c) " +
                             "(the active nudge) is REJECTED: the ache informs, it never nags");
            notes.Add("one Queues door (the Manage face), no nudge toast");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        /// <summary>Rec.709 relative luminance - what a greyscale pass of the capture shows.</summary>
        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static ObsidianQueueGate.WorkQueueStatus Status(
            int bBusy, int bSlots, int tBusy, int tSlots, int rBusy, int rSlots)
        {
            return new ObsidianQueueGate.WorkQueueStatus
            {
                Available = true,
                BuilderBusy = bBusy, BuilderSlots = bSlots,
                TrainBusy = tBusy, TrainSlots = tSlots,
                ResearchBusy = rBusy, ResearchSlots = rSlots,
                SoonestRemainingSec = -1,
            };
        }

        private static string ReadSrc(string relPath)
        {
            try { return File.Exists(relPath) ? File.ReadAllText(relPath) : null; }
            catch { return null; }
        }

        /// <summary>Signal source that holds every bar predicate still — this suite is about the
        /// Manage face's WORDS, not about which faces pack.</summary>
        private sealed class FakeSource : HudActionBarModel.ISource
        {
            public bool TalkAvailable => false;
            public bool RaidCapable => false;
            public bool RaidArmyReady => true;
            public int RaidDeployableSlots => 0;
            public int RaidQueuedSlots => 0;
            public int RaidCapSlots => 5;
            public bool MapUnlocked => false;
            public bool BuildingFocused => false;
        }
    }
}
