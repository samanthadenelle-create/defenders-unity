// =============================================================================
// HudDockLayoutRegression [dock-layout] (WO-1319) — the bottom action dock can never
// again print its face captions as one overlapping run.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
// Markers: HUD_DOCK_LAYOUT_OK / HUD_DOCK_LAYOUT_FAIL.
//
// WHAT BROKE (owner screenshot, echoes-of-elarion.vercel.app 2026.09.02.352005, a tall
// narrow desktop browser window): "BUILDTALKHERO...QUEUE MANAGE" — five captions with no
// gap, each running into its neighbour. The full measured chain is in the header of
// Assets/_Modules/Core/UI/HudDockLayout.cs; in one line: the dock sliced a mount that is
// 46% of a canvas whose LOCAL width collapses with the aspect into 1/5 fractions, the
// fractions fell under ElarionUiKit.MinTouchPx(112), and UiKitMinTouchGuard then grew every
// slot symmetrically about its centre into gaps that were 9 px wide.
//
// This suite is a CHEAP STRUCTURAL oracle, not a pixel test. It replays the SHIPPING solver
// (DeNelle.Core.UI.HudDockLayout.Solve — the same method the runtime calls) at real measured
// surface sizes and pins the properties that make the defect unreachable. Pixel truth still
// belongs to RunCaptureHeadless plus eyes on the image; a fresh capture at the owner's aspect
// is the ONLY thing that can close the WO.
//
//   1 [floors]     the solver's floor IS ElarionUiKit.MinTouchPx — one ceiling, one source.
//   2 [landscape]  at the shipping landscape sizes the solver reproduces the OLD authored
//                  geometry exactly (no expansion, gap fraction 0.018): the fix cannot have
//                  moved the bar the owner already signed off.
//   3 [overlap]    a full pairwise sweep over a wide aspect ladder (down to 1:4) at BOTH the
//                  5-face and 6-face counts: no two slots overlap, nothing leaves the track,
//                  and no slot is under the touch floor unless the solver has DECLARED
//                  Overflowed (in which case captions are off and gaps are zero).
//   4 [narrow]     the owner's defect aspects specifically: the tiers fire in the right order,
//                  the track never grows LEFT (the MoveCluster's column), and it never grows
//                  past HudAreasHost.SafeRightX.
//   5 [source]     the laws that keep it fixed: no 1/n horizontal fraction slicing left in the
//                  peaceful dock, the caption is fitted (NoWrap + Ellipsis) rather than left on
//                  the kit default, ClampMinTouch is still called, and no embedded NUL.
//
// Standalone: run-unity-method DeNelle.Editor.Regression.HudDockLayoutRegression.RunAll
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class HudDockLayoutRegression
    {
        private const string DockSrc = "Assets/_Modules/HUD/Kit/HudKitController.cs";
        private const string SolverSrc = "Assets/_Modules/Core/UI/HudDockLayout.cs";
        private const string ResponderSrc = "Assets/_Modules/HUD/Kit/HudDockSlotLayout.cs";

        // The ActionBar band, mirrored from HudAreasHost. DeNelle.EditorRegression cannot
        // reference DeNelle.HUD, so these are re-stated — and case 5 FAILS if the numbers in
        // HudAreasHost.cs ever stop matching, so the copy cannot silently drift.
        private const float ActionBarMinX = 0.270f;
        private const float ActionBarMaxX = 0.730f;
        private const float SafeRightX = 0.995f;

        private static float MountFraction { get { return ActionBarMaxX - ActionBarMinX; } }
        private static float RightHeadroomRatio { get { return (SafeRightX - ActionBarMaxX) / MountFraction; } }

        /// <summary>The face counts the dock ships with. FIVE is the calm-town norm (CLAUDE.md §7:
        /// Talk is added only while a talkable NPC is in range) and SIX is the maximum
        /// (HudActionBarModel.MaxVisibleFaces). A five-face bar is the feature working.</summary>
        private static readonly int[] ShippingCounts = { 5, 6 };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("HUD_DOCK_LAYOUT_OK - " + reason);
            else Debug.LogError("HUD_DOCK_LAYOUT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "floors", () => Case1_Floors(failures, notes));
                Case(failures, "landscape", () => Case2_LandscapeUnchanged(failures, notes));
                Case(failures, "overlap", () => Case3_NoOverlapAcrossAspects(failures, notes));
                Case(failures, "narrow", () => Case4_NarrowTiers(failures, notes));
                Case(failures, "source", () => Case5_SourceLaws(failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "HUD DOCK LAYOUT OK - the action dock solves in reference pixels, reproduces " +
                         "the authored landscape geometry unchanged, and cannot overlap a neighbour at " +
                         "any aspect down to 1:4 at 5 OR 6 faces" + noteStr;
                return true;
            }
            reason = "dock-layout FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  Geometry helpers — the runtime's own arithmetic, replayed headlessly.
        // =====================================================================

        private static float MountWidthPx(float surfaceW, float surfaceH)
        {
            return HudDockLayout.CanvasLocalWidthPx(surfaceW, surfaceH) * MountFraction;
        }

        private static HudDockLayout.Solution SolveAt(int count, float surfaceW, float surfaceH)
        {
            float mount = MountWidthPx(surfaceW, surfaceH);
            return HudDockLayout.Solve(count, mount, mount * (1f + RightHeadroomRatio));
        }

        // =====================================================================
        //  1 [floors]
        // =====================================================================
        private static void Case1_Floors(List<string> failures, List<string> notes)
        {
            if (Math.Abs(HudDockLayout.MinSlotPx - ElarionUiKit.MinTouchPx) > 0.001f)
                failures.Add("[floors] HudDockLayout.MinSlotPx (" + HudDockLayout.MinSlotPx +
                             ") has drifted from ElarionUiKit.MinTouchPx (" + ElarionUiKit.MinTouchPx +
                             ") - the touch floor must have exactly ONE source");

            if (HudDockLayout.GapFraction <= 0f || HudDockLayout.GapFraction >= 0.1f)
                failures.Add("[floors] GapFraction " + HudDockLayout.GapFraction + " is not a sane dock gap");

            // The sweep below solves every count at the PEACEFUL gap. That is only a valid bound
            // for the combat dock if its gap is the smaller one (a smaller gap leaves MORE width
            // for the faces, so it can never fail where the wider gap passes).
            if (HudDockLayout.CombatGapFraction > HudDockLayout.GapFraction)
                failures.Add("[floors] CombatGapFraction (" + HudDockLayout.CombatGapFraction +
                             ") now exceeds GapFraction (" + HudDockLayout.GapFraction +
                             ") - the aspect sweep is no longer the stricter bound for the combat " +
                             "dock; sweep both gaps explicitly");

            if (HudDockLayout.GapCount(5) != 6 || HudDockLayout.GapCount(6) != 7)
                failures.Add("[floors] GapCount is wrong: N slots consume N+1 gaps (both outer edges)");

            // The canvas math must agree with the CanvasScaler at the reference size itself.
            float atRef = HudDockLayout.CanvasLocalWidthPx(1080f, 1920f);
            if (Math.Abs(atRef - 1080f) > 0.5f)
                failures.Add("[floors] CanvasLocalWidthPx(1080,1920) = " + atRef.ToString("0.#") +
                             ", expected the reference width 1080 - the scaler mirror is wrong");

            notes.Add("floor " + HudDockLayout.MinSlotPx.ToString("0") + "px, right headroom x" +
                      RightHeadroomRatio.ToString("0.###"));
        }

        // =====================================================================
        //  2 [landscape] — the fix must not move the bar the owner signed off.
        // =====================================================================
        private static void Case2_LandscapeUnchanged(List<string> failures, List<string> notes)
        {
            // (w, h, label) — the Seeker, a 16:9 desktop window, and a tablet.
            float[][] landscape =
            {
                new[] { 2340f, 1080f },
                new[] { 1920f, 1080f },
                new[] { 2048f, 1536f },
            };

            foreach (var s in landscape)
            {
                float mount = MountWidthPx(s[0], s[1]);
                var sol = SolveAt(5, s[0], s[1]);
                string at = s[0].ToString("0") + "x" + s[1].ToString("0");

                if (sol.Tier != 1)
                    failures.Add("[landscape] " + at + " solved at tier " + sol.Tier +
                                 " - landscape must stay on the authored fraction (tier 1). " + sol);
                if (sol.RightExpansionPx > 0.01f)
                    failures.Add("[landscape] " + at + " grew the track by " +
                                 sol.RightExpansionPx.ToString("0.#") + "px - landscape must not expand");

                // Byte-for-byte the retired literals: gap 0.018, width (1 - 6*0.018)/5.
                float oldGap = 0.018f * mount;
                float oldSlot = ((1f - 0.018f * 6f) / 5f) * mount;
                if (Math.Abs(sol.GapPx - oldGap) > 0.05f || Math.Abs(sol.SlotWidthPx - oldSlot) > 0.05f)
                    failures.Add("[landscape] " + at + " geometry moved: slot " +
                                 sol.SlotWidthPx.ToString("0.##") + " vs authored " + oldSlot.ToString("0.##") +
                                 ", gap " + sol.GapPx.ToString("0.##") + " vs " + oldGap.ToString("0.##"));

                if (!HudDockLayout.IsNonOverlapping(sol))
                    failures.Add("[landscape] " + at + " slots overlap: " + sol);
            }
            notes.Add("landscape unchanged at 3 sizes");
        }

        // =====================================================================
        //  3 [overlap] — the property the defect violated, swept.
        // =====================================================================
        private static void Case3_NoOverlapAcrossAspects(List<string> failures, List<string> notes)
        {
            int checks = 0;
            int overflowed = 0;
            // Aspect ladder from a wide desktop down to 1:4 (narrower than any shipping phone,
            // and well past the tall/narrow window the owner captured).
            for (float ratio = 3.0f; ratio >= 0.25f; ratio -= 0.05f)
            {
                float h = 1200f;
                float w = Mathf.Max(1f, h * ratio);
                foreach (int count in ShippingCounts)
                {
                    var sol = SolveAt(count, w, h);
                    checks++;
                    string at = "aspect " + ratio.ToString("0.00") + " x" + count + " faces";

                    if (!HudDockLayout.IsNonOverlapping(sol))
                    {
                        failures.Add("[overlap] " + at + ": SLOTS OVERLAP OR LEAVE THE TRACK - " + sol);
                        continue;
                    }
                    if (sol.GapPx < -0.001f)
                        failures.Add("[overlap] " + at + ": negative gap - " + sol);
                    if (sol.RightExpansionPx < -0.001f)
                        failures.Add("[overlap] " + at + ": the track grew LEFT (" +
                                     sol.RightExpansionPx.ToString("0.#") + "px) - the MoveCluster owns that column");
                    if (sol.RightExpansionPx > sol.MountWidthPx * RightHeadroomRatio + 0.01f)
                        failures.Add("[overlap] " + at + ": the track grew past SafeRightX - " + sol);

                    if (sol.Overflowed)
                    {
                        overflowed++;
                        if (sol.ShowCaptions)
                            failures.Add("[overlap] " + at + ": overflowed but kept captions - " +
                                         "the declared degradation is icon-only");
                        if (sol.GapPx > 0.001f)
                            failures.Add("[overlap] " + at + ": overflowed with gaps still spent - " +
                                         "gaps must collapse before the touch floor does");
                    }
                    else if (sol.SlotWidthPx < HudDockLayout.MinSlotPx - 0.01f)
                    {
                        failures.Add("[overlap] " + at + ": slot " + sol.SlotWidthPx.ToString("0.#") +
                                     "px is under MinTouchPx and the solver did NOT declare Overflowed - " +
                                     "a silent sub-floor slot is exactly what the clamp then grows into its " +
                                     "neighbour (WO-1319). " + sol);
                    }
                }
            }
            notes.Add(checks + " aspect x count solves, " + overflowed + " in the declared overflow tier");
        }

        // =====================================================================
        //  4 [narrow] — the owner's defect shape, named.
        // =====================================================================
        private static void Case4_NarrowTiers(List<string> failures, List<string> notes)
        {
            // A tall/narrow desktop window (the capture) and a phone in PORTRAIT — the Pi
            // Browser shape that lands here before WO-1312's rotation engages.
            float[][] narrow =
            {
                new[] { 720f, 1200f },    // the owner's window shape
                new[] { 1080f, 2340f },   // Seeker, portrait
                new[] { 750f, 1334f },    // small phone, portrait
            };

            foreach (var s in narrow)
            {
                string at = s[0].ToString("0") + "x" + s[1].ToString("0");
                foreach (int count in ShippingCounts)
                {
                    var sol = SolveAt(count, s[0], s[1]);

                    if (!HudDockLayout.IsNonOverlapping(sol))
                        failures.Add("[narrow] " + at + " x" + count + ": slots overlap - " + sol);

                    // The whole point of the WO: a real portrait surface must still seat the
                    // touch floor. If one of these ever lands in the overflow tier, the dock
                    // needs a second ROW, not a smaller button - fail loudly rather than shrink.
                    if (sol.Overflowed)
                        failures.Add("[narrow] " + at + " x" + count + " fell into the OVERFLOW tier: " +
                                     "a shipping portrait surface can no longer seat " + count +
                                     " faces at MinTouchPx in one row. " + sol);
                    else if (sol.SlotWidthPx < HudDockLayout.MinSlotPx - 0.01f)
                        failures.Add("[narrow] " + at + " x" + count + ": slot under the touch floor - " + sol);

                    if (sol.Tier == 1 && sol.RightExpansionPx > 0.01f)
                        failures.Add("[narrow] " + at + " x" + count + ": tier 1 must never expand");
                }
            }

            // The ladder must actually be a ladder: a narrow surface has to leave tier 1.
            var probe = SolveAt(5, 720f, 1200f);
            if (probe.Tier == 1)
                failures.Add("[narrow] 720x1200 x5 still solves at tier 1 - the authored fraction " +
                             "yields " + probe.SlotWidthPx.ToString("0.#") + "px there, which is the " +
                             "defect shape; the expansion tier is not firing");
            notes.Add("owner window 720x1200 x5 -> " + probe);
        }

        // =====================================================================
        //  5 [source] — the laws that keep it unreachable.
        // =====================================================================
        private static void Case5_SourceLaws(List<string> failures, List<string> notes)
        {
            string dock = ReadText(DockSrc, failures, "[source]");
            string solver = ReadText(SolverSrc, failures, "[source]");
            string responder = ReadText(ResponderSrc, failures, "[source]");
            if (dock == null || solver == null || responder == null) return;

            foreach (var pair in new[] {
                new[] { DockSrc, dock }, new[] { SolverSrc, solver }, new[] { ResponderSrc, responder } })
                if (pair[1].IndexOf('\0') >= 0)
                    failures.Add("[source] " + pair[0] + " contains an embedded NUL byte (CLAUDE.md section 1)");

            string code = StripComments(dock);

            // The peaceful dock must still hand its slots to the live solver.
            if (code.IndexOf("HudDockSlotLayout", StringComparison.Ordinal) < 0)
                failures.Add("[source] HudKitController no longer references HudDockSlotLayout - the " +
                             "dock has gone back to build-time geometry and cannot survive a window resize");
            if (code.IndexOf("AddSlot(", StringComparison.Ordinal) < 0)
                failures.Add("[source] the peaceful dock no longer registers its slots with the solver");

            // The COMBAT dock shares the ActionBar mount and had the same defect one posture away.
            var combat = ExtractMethod(code, "private ElarionUiKit.ActionSlotHandle BuildCombatDockSlot");
            if (combat == null)
                notes.Add("BuildCombatDockSlot not found by name - renamed? re-point this law");
            else
            {
                if (combat.IndexOf("AddSlot(", StringComparison.Ordinal) < 0)
                    failures.Add("[source] the combat dock no longer registers its slots with the solver - " +
                                 "six faces in the same mount is the WORSE half of WO-1319, not the safer one");
                if (combat.IndexOf("HudDockLayout.CombatGapFraction", StringComparison.Ordinal) < 0)
                    failures.Add("[source] BuildCombatDockSlot re-hardcodes its gap fraction - one gap, one source");
                if (combat.IndexOf("FitSingleLine", StringComparison.Ordinal) < 0)
                    failures.Add("[source] the combat dock caption is no longer fitted (NoWrap + Ellipsis)");
            }

            // The touch floor must still be applied - the fix is a layout fix, never a
            // relaxation of the floor (WO-1319 acceptance 4).
            if (code.IndexOf("ClampMinTouch", StringComparison.Ordinal) < 0)
                failures.Add("[source] ClampMinTouch was removed from HudKitController - the touch floor " +
                             "is not negotiable; the dock's job is to make the clamp a no-op, not to drop it");

            // The caption's degradation must be authored, not the kit default.
            if (code.IndexOf("FitSingleLine(slot.caption", StringComparison.Ordinal) < 0)
                failures.Add("[source] the dock caption is no longer fitted (NoWrap + bounded autosize + " +
                             "Ellipsis) - an unfitted caption is free to paint past its slot again");

            // No re-introduced 1/n horizontal slicing in the dock builder.
            // ⚠ WO-1672 RE-POINT — READ THIS BEFORE MOVING IT BACK. This law used to extract
            // `private void BuildPeacefulDockSlot`. That method is now a THREE-LINE WRAPPER: the
            // 1/n seed arithmetic this law polices was hoisted into the shared `BuildDockSlot`
            // when the outside dock (WO-1672) started using the same builder. Left pointing at
            // the wrapper, the extract would contain no arithmetic at all, the regex could never
            // match, and the law would report green FOREVER while `BuildDockSlot` was free to
            // re-hardcode the gap - the "green worth nothing" failure WO-1467 removed one file
            // over. A source law must follow the code it names, not the name it was written with.
            var builder = ExtractMethod(code, "private ElarionUiKit.ActionSlotHandle BuildDockSlot");
            if (builder == null)
                notes.Add("BuildDockSlot not found by name - renamed? re-point this law");
            else if (Regex.IsMatch(builder, @"1f\s*-\s*gap\s*\*\s*\(\s*count\s*\+\s*1\s*\)") &&
                     builder.IndexOf("HudDockLayout.GapFraction", StringComparison.Ordinal) < 0)
                failures.Add("[source] BuildDockSlot re-hardcodes its gap fraction instead of " +
                             "reading HudDockLayout.GapFraction - one gap, one source");

            // The responder may never grow the track LEFT.
            string resp = StripComments(responder);
            if (resp.IndexOf("offsetMin = Vector2.zero", StringComparison.Ordinal) < 0)
                failures.Add("[source] HudDockSlotLayout no longer pins the track's LEFT edge to its " +
                             "mount - growing left puts the dock under the MoveCluster stick");

            // The mirrored band numbers must still match HudAreasHost.
            string host = ReadText("Assets/_Modules/HUD/Kit/HudAreasHost.cs", failures, "[source]");
            if (host != null)
            {
                string h = StripComments(host);
                if (h.IndexOf("ActionBarMinX = 0.270f", StringComparison.Ordinal) < 0 ||
                    h.IndexOf("ActionBarMaxX = 0.730f", StringComparison.Ordinal) < 0 ||
                    h.IndexOf("SafeRightX = 0.995f", StringComparison.Ordinal) < 0)
                    failures.Add("[source] HudAreasHost's ActionBar band constants changed; this suite " +
                                 "still solves against " + ActionBarMinX + ".." + ActionBarMaxX +
                                 " (safe right " + SafeRightX + ") - re-derive the mirror above");
            }
            notes.Add("source laws held");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static string ReadText(string path, List<string> failures, string tag)
        {
            try
            {
                if (!File.Exists(path)) { failures.Add(tag + " source not found: " + path); return null; }
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                failures.Add(tag + " could not read " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Blank out // and block comments so a lesson written in prose (which deliberately
        /// quotes the retired shapes) can never fail a source law.</summary>
        private static string StripComments(string src)
        {
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\n]*", " ");
        }

        /// <summary>Crude brace-matched slice of one method body, for a source law that must not
        /// read the whole file. Null when the signature is absent.</summary>
        private static string ExtractMethod(string code, string signature)
        {
            int i = code.IndexOf(signature, StringComparison.Ordinal);
            if (i < 0) return null;
            int open = code.IndexOf('{', i);
            if (open < 0) return null;
            int depth = 0;
            for (int j = open; j < code.Length; j++)
            {
                if (code[j] == '{') depth++;
                else if (code[j] == '}')
                {
                    depth--;
                    if (depth == 0) return code.Substring(open, j - open + 1);
                }
            }
            return null;
        }
    }
}
