// =============================================================================
// ArmyMusterLayoutRegression [army-muster-layout] - WO-1230.
// Markers: ARMY_MUSTER_LAYOUT_OK / ARMY_MUSTER_LAYOUT_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Wired into DataRegression.RunAll.
//
// ==========================  WHAT IT PROVES  =================================
// The owner's capture (Seeker 2026.08.26.342290, tmp/wo-army-muster-2026-08-26.png)
// showed the Army screen with six collisions, the worst of which was a COUNT that
// wrapped: "20" rendered as a 2 stacked over a 0, in the one field the player was
// actively editing. So this suite asserts, on a LIVE canvas at three surfaces:
//
//   1. every named element of ArmyMusterPanel's layout table resolves to a rect
//      that intersects NO other named element (the WO's "no two of the named
//      elements' rects intersect");
//   2. the roster COUNT field seats the widest three-digit count ON ONE LINE at
//      its authored font, measured off the generated TMP mesh - not counted, not
//      inferred (the WO's "fits its formatted string without wrapping at 3
//      digits");
//   3. every interactive band is at or over MinTouchPx WITHOUT a clamp having to
//      grow it, because a clamp that grows a control after the fact is how the
//      hero-select overlap was made;
//   4. the owner's 2026-08-26 wording ruling holds in the player-facing strings
//      while the CODE IDENTIFIERS are untouched.
//
// ==========================  THE WO-1138 RATCHET  ============================
// An oracle that cannot fail is worse than no oracle. CaseHistoricalIsRed runs the
// SAME two predicates over the geometry this panel actually shipped with - quoted
// from the pre-fix source - and FAILS THE SUITE IF THAT GEOMETRY PASSES. So the
// suite proves its own redness on every run, in-process, rather than resting on a
// one-time manual revert nobody can re-check later. The historical numbers are:
//
//   * both command bands: a 900x112 px box parented to chrome.content, pinned to
//     the panel's top (y=1, -8px) and bottom (y=0, +8px) - ArmyMusterPanel.cs
//     MakeCommandBand + lines 107-108 at HEAD~. The TOP one lands across the
//     header zone (0.115,0.900)-(0.860,0.975); the BOTTOM one lands across the
//     shared kit Close, a fixed 360x132 box seated bottom-centre by
//     ElarionUiKit.SeatSharedCloseInside at DefaultCloseZone. Both are collisions.
//   * the count label: x 0.70..0.73 of the row - THREE PERCENT, about 30 canvas
//     units - against a "999" that measures ~80 at ElarionUi.FontBody.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class ArmyMusterLayoutRegression
    {
        private const string PanelSrc = "Assets/_Modules/Village/Troops/ArmyMusterPanel.cs";

        /// <summary>The panel's ViewModel (WO-1512). The muster TRANSACTION moved here when
        /// presentation was separated from the objects, so the [no-rename] identifier pin reads
        /// this file - the ruling was that <c>Muster()</c> must not be RENAMED, never that the
        /// View must call it directly.</summary>
        private const string VmSrc = "Assets/_Modules/Village/Troops/ArmyMusterVM.cs";
        // WO-1857: Case 4b's ruled wording now lives in the English string table, not in the View.
        private const string EnglishTableSrc = "Assets/Resources/Data/Canonical/en.json";
        private const float Eps = 0.5f;

        private struct Surface
        {
            public string Name; public float W; public float H;
            public Surface(string n, float w, float h) { Name = n; W = w; H = h; }
        }

        /// <summary>The owner's device first, then the two other shapes this panel can open on.
        /// A layout proved on ONE surface is a layout proved by luck.</summary>
        private static readonly Surface[] Surfaces =
        {
            new Surface("seeker-2670x1200", 2670f, 1200f),
            new Surface("landscape-1920x1080", 1920f, 1080f),
            new Surface("portrait-1080x1920", 1080f, 1920f),
        };

        /// <summary>Bands the player must be able to TAP. Each has to clear MinTouchPx as
        /// AUTHORED, so ClampMinTouch never has to grow one into its neighbour.</summary>
        private static readonly HashSet<string> Touchable = new HashSet<string>
        {
            "Slot.Raid", "Slot.Hold", "Slot.Siege", "Clear", "Name", "Save", "Cta", "Close",
        };

        private struct Named
        {
            public string Name; public Rect Frac;
            public Named(string n, Rect r) { Name = n; Frac = r; }
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== ArmyMusterLayoutRegression: WO-1230 army screen partition ===");

            // ── FIXTURE. The thing measured must be on disk; absence is RED with the path. ──
            if (!File.Exists(PanelSrc))
            {
                reason = "army-muster-layout FAIL x1: [fixture] MISSING " + PanelSrc +
                         " - the panel this suite measures is not on disk.";
                return false;
            }
            if (!File.Exists(VmSrc))
            {
                reason = "army-muster-layout FAIL x1: [fixture] MISSING " + VmSrc +
                         " - the panel's ViewModel, which now carries the muster transaction, is not on disk.";
                return false;
            }

            // ── CAPABILITY. No canvas => a DECLARED stand-down, never a pass. ──
            GameObject probe = null;
            string canvasWhy = null;
            try { probe = NewCanvas("aml-probe", 100f, 100f); }
            catch (Exception ex) { canvasWhy = ex.GetType().Name + ": " + ex.Message; }
            finally { if (probe != null) UnityEngine.Object.DestroyImmediate(probe); }
            if (canvasWhy != null)
            {
                return RegressionOutcome.Skip(out reason, "ARMY MUSTER LAYOUT",
                    "no UI canvas can be instantiated in this environment (" + canvasWhy +
                    ") - no rect can be measured");
            }

            try
            {
                foreach (var s in Surfaces)
                {
                    var surface = s;
                    Case(failures, "bands:" + surface.Name, () => CaseBands(surface, failures, log));
                    Case(failures, "count:" + surface.Name, () => CaseCountFits(surface, failures, notes, log));
                }
                Case(failures, "historical-is-red", () => CaseHistoricalIsRed(failures, notes, log));
                Case(failures, "wording", () => CaseWording(failures, log));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : string.Empty;
            if (failures.Count == 0)
            {
                reason = "ARMY MUSTER LAYOUT OK - " + Surfaces.Length + " surfaces MEASURED on a live canvas: " +
                         "every named band disjoint, every touch band at or over MinTouchPx(" +
                         ElarionUiKit.MinTouchPx.ToString("0") + ") as authored, the count field seats '" +
                         ArmyMusterPanel.WidestCount + "' on ONE line, and the pre-fix geometry still " +
                         "measures RED (the oracle can fail)" + noteStr;
                Debug.Log("ARMY_MUSTER_LAYOUT_OK\n" + log);
                return true;
            }

            Debug.LogError("ARMY_MUSTER_LAYOUT_FAIL: " + failures.Count + " failure(s)\n" + log);
            reason = "army-muster-layout FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1 - the panel partition, measured.
        // =====================================================================
        private static void CaseBands(Surface s, List<string> failures, StringBuilder log)
        {
            string tag = "[" + s.Name + "]";
            GameObject root = null;
            try
            {
                float refW, refH;
                ReferenceBox(s, out refW, out refH);
                root = NewCanvas("aml-" + s.Name, refW, refH);
                var rootRt = (RectTransform)root.transform;

                var panel = Region(rootRt, "Panel",
                    new Vector2(ArmyMusterPanel.PanelAnchorMinX, ArmyMusterPanel.PanelAnchorMinY),
                    new Vector2(ArmyMusterPanel.PanelAnchorMaxX, ArmyMusterPanel.PanelAnchorMaxY));

                float panelW = ArmyMusterPanel.PanelFracW * refW;
                float panelH = ArmyMusterPanel.PanelFracH * refH;

                // ⛔ THE PANEL'S OWN TABLE. Not a copy of it - a copy could not fail.
                var bands = ArmyMusterPanel.ComputeBands(panelW, panelH);
                var named = new List<Named>();
                foreach (var b in bands) named.Add(new Named(b.Name, b.Frac));

                var rects = Place(panel, named);
                Settle(rootRt);

                log.AppendLine(tag + " panel " + panelW.ToString("0") + "x" + panelH.ToString("0") +
                               " ref " + refW.ToString("0") + "x" + refH.ToString("0"));

                AssertDisjoint(tag, named, rects, failures, log);
                AssertTouch(tag, named, rects, failures);
                AssertInsidePanel(tag, panel, named, rects, failures);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // =====================================================================
        //  CASE 2 - the count field, measured off the generated mesh.
        // =====================================================================
        private static void CaseCountFits(Surface s, List<string> failures, List<string> notes, StringBuilder log)
        {
            string tag = "[" + s.Name + "]";
            GameObject root = null;
            try
            {
                float refW, refH;
                ReferenceBox(s, out refW, out refH);
                root = NewCanvas("aml-row-" + s.Name, refW, refH);
                var rootRt = (RectTransform)root.transform;

                float panelW = ArmyMusterPanel.PanelFracW * refW;
                float panelH = ArmyMusterPanel.PanelFracH * refH;
                var bands = ArmyMusterPanel.ComputeBands(panelW, panelH);
                Rect roster = ArmyMusterPanel.Band(bands, "Roster");
                float rowW = Mathf.Max(1f, roster.width * panelW - 24f);   // as the panel derives it

                var row = Region(rootRt, "Row", Vector2.zero, Vector2.one);
                row.anchorMin = new Vector2(0.5f, 0.5f); row.anchorMax = new Vector2(0.5f, 0.5f);
                row.sizeDelta = new Vector2(rowW, ElarionUiKit.MinTouchPx);

                var rowBands = ArmyMusterPanel.ComputeRowBands(rowW);
                var named = new List<Named>();
                foreach (var b in rowBands) named.Add(new Named(b.Name, b.Frac));
                var rects = Place(row, named);

                Rect countRect;
                if (!rects.TryGetValue("Row.Count", out countRect))
                {
                    failures.Add(tag + " the row table declares no 'Row.Count' band - the field the owner " +
                                 "edits has no rect at all.");
                    return;
                }

                var host = FindRegion(row, "Row.Count");
                string why;
                bool wrapped = LabelWraps(host, ArmyMusterPanel.WidestCount, ArmyMusterPanel.CountFontSize,
                                          rootRt, out why);
                if (why != null && why.StartsWith("SKIP"))
                {
                    notes.Add(RegressionOutcome.PartialSkip(tag + " count mesh measurement", why.Substring(5)));
                }
                else if (wrapped)
                {
                    failures.Add(tag + " THE COUNT WRAPS: '" + ArmyMusterPanel.WidestCount + "' at font " +
                                 ArmyMusterPanel.CountFontSize + " does NOT fit the authored count band (" +
                                 countRect.width.ToString("0") + "x" + countRect.height.ToString("0") +
                                 " canvas units) on one line - " + why + ". This is the owner's '20' rendering " +
                                 "as a 2 over a 0; the player cannot tell 20 from 200 from 2.");
                }
                else
                {
                    log.AppendLine(tag + " count band " + countRect.width.ToString("0") + " units seats '" +
                                   ArmyMusterPanel.WidestCount + "' on one line (" + why + ")");
                }

                // Glyph-advance cross-check: independent of the mesh path, so a TMP quirk cannot
                // hide a too-narrow band.
                float measured = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body,
                    ArmyMusterPanel.WidestCount, ArmyMusterPanel.CountFontSize, out string detail);
                if (measured < 0f)
                    notes.Add(RegressionOutcome.PartialSkip(tag + " count glyph-advance measurement", detail));
                else if (measured > countRect.width * 0.88f)
                    failures.Add(tag + " '" + ArmyMusterPanel.WidestCount + "' MEASURES " + measured.ToString("0") +
                                 " units (" + detail + ") against a count band of " + countRect.width.ToString("0") +
                                 " units - under the 12% inset the label carries, so it clips or shrinks. Widen " +
                                 "the band; do not shrink the type.");

                AssertDisjoint(tag + "[row]", named, rects, failures, log);
                AssertRowTouch(tag, named, rects, failures);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // =====================================================================
        //  CASE 3 - THE RATCHET. The pre-fix geometry must still measure RED.
        // =====================================================================
        private static void CaseHistoricalIsRed(List<string> failures, List<string> notes, StringBuilder log)
        {
            var s = Surfaces[0];                       // the owner's device
            GameObject root = null;
            try
            {
                float refW, refH;
                ReferenceBox(s, out refW, out refH);
                root = NewCanvas("aml-historical", refW, refH);
                var rootRt = (RectTransform)root.transform;

                var panel = Region(rootRt, "Panel",
                    new Vector2(ArmyMusterPanel.PanelAnchorMinX, ArmyMusterPanel.PanelAnchorMinY),
                    new Vector2(ArmyMusterPanel.PanelAnchorMaxX, ArmyMusterPanel.PanelAnchorMaxY));
                float panelW = ArmyMusterPanel.PanelFracW * refW;
                float panelH = ArmyMusterPanel.PanelFracH * refH;

                // ── the geometry that shipped ────────────────────────────────
                float bw = 900f / panelW;              // MakeCommandBand sizeDelta.x
                float bh = ElarionUiKit.MinTouchPx / panelH;
                float off = 8f / panelH;               // anchoredPosition nudge
                float cw = ElarionUiKit.CanonCtaWidth / panelW;
                float ch = ElarionUiKit.CanonCtaHeight / panelH;

                var old = new List<Named>
                {
                    new Named("old.Header",   Rect.MinMaxRect(0.115f, 0.900f, 0.860f, 0.975f)),
                    new Named("old.Selector", Rect.MinMaxRect(0.5f - bw * 0.5f, 1f - off - bh,
                                                              0.5f + bw * 0.5f, 1f - off)),
                    new Named("old.Action",   Rect.MinMaxRect(0.5f - bw * 0.5f, off,
                                                              0.5f + bw * 0.5f, off + bh)),
                    new Named("old.Close",    Rect.MinMaxRect(0.5f - cw * 0.5f, 0.050f,
                                                              0.5f + cw * 0.5f, 0.050f + ch)),
                };
                var oldRects = Place(panel, old);
                Settle(rootRt);

                int collisions = CountCollisions(old, oldRects);
                if (collisions == 0)
                    failures.Add("[ratchet] the PRE-FIX band geometry measures DISJOINT on this canvas - " +
                                 "the disjointness predicate cannot fail, so its green proves nothing. " +
                                 "Fix the predicate before trusting any other case in this suite.");
                else
                    log.AppendLine("[ratchet] pre-fix band geometry still collides " + collisions +
                                   "x (selector over the title, action over the shared Close) - RED, as it must be.");

                // ── the count band that shipped: x 0.70..0.73 of the row ─────
                var bands = ArmyMusterPanel.ComputeBands(panelW, panelH);
                float rowW = Mathf.Max(1f, ArmyMusterPanel.Band(bands, "Roster").width * panelW - 24f);
                var row = Region(rootRt, "OldRow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                row.sizeDelta = new Vector2(rowW, ElarionUiKit.MinTouchPx);
                var oldCount = new List<Named> { new Named("old.Count", Rect.MinMaxRect(0.70f, 0.14f, 0.73f, 0.86f)) };
                Place(row, oldCount);

                var host = FindRegion(row, "old.Count");
                string why;
                bool wrapped = LabelWraps(host, ArmyMusterPanel.WidestCount, ArmyMusterPanel.CountFontSize,
                                         rootRt, out why);
                if (why != null && why.StartsWith("SKIP"))
                {
                    notes.Add(RegressionOutcome.PartialSkip("[ratchet] pre-fix count measurement", why.Substring(5)));
                }
                else if (!wrapped)
                {
                    failures.Add("[ratchet] the PRE-FIX count band (x 0.70..0.73 of the row, ~" +
                                 (0.03f * rowW).ToString("0") + " units) seats '" + ArmyMusterPanel.WidestCount +
                                 "' on ONE line in this harness - but the owner's device rendered a 2 stacked " +
                                 "over a 0. The wrap predicate is not measuring what the player saw.");
                }
                else
                {
                    log.AppendLine("[ratchet] pre-fix count band still wraps '" + ArmyMusterPanel.WidestCount +
                                   "' (" + why + ") - RED, as it must be.");
                }
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // =====================================================================
        //  CASE 4 - the owner's wording ruling, and the identifiers it spared.
        // =====================================================================
        private static void CaseWording(List<string> failures, StringBuilder log)
        {
            string src = File.ReadAllText(PanelSrc);
            // Read SEPARATELY, never concatenated into src: the 4a/4b/4c literal scans below are
            // written for the View's copy, and the VM authors the archaic word on purpose in its
            // PlayerWords mapping table. Only the [no-rename] pin at 4d reads this.
            string vm = File.ReadAllText(VmSrc);

            // 4a. no NON-ASCII inside a player-facing string literal.
            foreach (Match m in Regex.Matches(src, "\"([^\"\\\\\\n]|\\\\.)*\""))
            {
                foreach (char c in m.Value)
                    if (c > 127)
                    {
                        failures.Add("[ascii] non-ASCII char U+" + ((int)c).ToString("X4") +
                                     " in the string literal " + m.Value + " (" + PanelSrc + ")");
                        break;
                    }
            }

            // 4b. the ruled strings, verbatim.
            // WO-1857: both ruled strings moved out of the View into the English string table, so
            // this case now pins BOTH halves of the seam rather than one source literal - the panel
            // must resolve the ruled KEY, and en.json must still carry the ruled WORDING for it. A
            // key-only pin would have let the owner's 2026-08-26 wording ruling be reworded in the
            // table with this case still green; a source-text pin is no longer possible at all. The
            // retired call-site literals are deliberately not reproduced against `src`: they survive
            // in explanatory comments inside the panel, so a source-text needle on them would pass
            // off those comments and assert nothing.
            string enJson = File.ReadAllText(EnglishTableSrc);
            if (src.IndexOf("\"village.troops.army_muster.train_army_button\"", StringComparison.Ordinal) < 0)
                failures.Add("[wording] the CTA face does not resolve village.troops.army_muster.train_army_button " +
                             "- owner ruling 2026-08-26 ('what dos muster army mean? Thats where im lost').");
            else if (enJson.IndexOf("\"village.troops.army_muster.train_army_button\": \"Train Army\"",
                         StringComparison.Ordinal) < 0)
                failures.Add("[wording] the CTA key resolves, but en.json no longer authors it as the ruled " +
                             "'Train Army' - owner ruling 2026-08-26.");
            if (src.IndexOf("\"village.troops.army_muster.tip_line\"", StringComparison.Ordinal) < 0)
                failures.Add("[wording] the tip line does not resolve village.troops.army_muster.tip_line");
            else if (enJson.IndexOf(
                         "\"village.troops.army_muster.tip_line\": \"Tip: Training auto-saves this slot. " +
                         "Fill the army, then Raids.\"", StringComparison.Ordinal) < 0)
                failures.Add("[wording] the tip-line key resolves, but en.json no longer authors the ruled " +
                             "tip sentence for it.");

            // 4c. NO player-facing 'Muster' survives. String-backed GameObject identifiers are
            //     explicitly exempt: they are diagnostic hierarchy names, not rendered copy.
            foreach (Match m in Regex.Matches(src, "\"([^\"\\\\\\n]|\\\\.)*\""))
            {
                string lit = m.Value;
                // The rewriter's own mapping table names the word in order to remove it.
                if (lit.IndexOf("Nothing to train", StringComparison.Ordinal) >= 0) continue;
                if (lit.IndexOf("Training ordered", StringComparison.Ordinal) >= 0) continue;
                if (lit == "\"Muster\"" || lit == "\"muster\"" || lit == "\"Mustered\"" ||
                    lit == "\"mustered\"" || lit == "\"Nothing to muster\"") continue;   // PlayerWords keys
                if (lit == "\"MusterRow_\"") continue;   // internal GameObject name prefix
                if (lit.IndexOf("Muster", StringComparison.Ordinal) >= 0 && lit.IndexOf("ArmyMuster", StringComparison.Ordinal) < 0)
                {
                    // FlowTrace tags are a diagnostic channel, not a player surface.
                    if (lit.IndexOf("ArmyMusterPanel", StringComparison.Ordinal) >= 0) continue;
                    failures.Add("[wording] a player-facing literal still says 'Muster': " + lit);
                }
            }

            // 4d. ⛔ THE IDENTIFIERS STAY. A rename is a wide mechanical diff with zero player
            //     benefit, and a regression greps the FlowTrace tag.
            if (src.IndexOf("class ArmyMusterPanel", StringComparison.Ordinal) < 0)
                failures.Add("[no-rename] ArmyMusterPanel was RENAMED - the ruling was player-facing strings ONLY.");
            // ⚠ THE PIN IS ON THE IDENTIFIER, NOT ON THE CALLER. The 2026-08-26 ruling was
            //   "player-facing strings ONLY" - it forbade RENAMING ArmyMusterService.Muster(),
            //   it never said the View must call it. WO-1512 moved the transaction to
            //   ArmyMusterVM (ARCHITECTURE_PRINCIPLES.md - presentation never touches the
            //   objects), so the call is asserted THERE. Panel-or-VM would let the identifier
            //   vanish from both; the VM is where it now lives, so that is where it is pinned.
            if (vm.IndexOf("ArmyMusterService.Muster(", StringComparison.Ordinal) < 0)
                failures.Add("[no-rename] ArmyMusterService.Muster() is no longer called by " +
                             "ArmyMusterVM - the ruling forbade renaming it (player-facing " +
                             "strings ONLY); WO-1512 moved the call from the panel to the VM.");
            if (src.IndexOf("FlowTrace.Step(\"Muster\"", StringComparison.Ordinal) < 0)
                failures.Add("[no-rename] the \"Muster\" FlowTrace tag is gone from the panel - a regression " +
                             "greps for it.");

            log.AppendLine("[wording] CTA / tip / ascii asserted on " + PanelSrc +
                           "; no-rename identifier pin asserted on " + VmSrc);
        }

        // =====================================================================
        //  Measurement plumbing
        // =====================================================================

        /// <summary>The reference box a surface resolves to under the kit's CanvasScaler
        /// (1080x1920, MatchWidthOrHeight 0.5) - derived, never tabulated.</summary>
        private static void ReferenceBox(Surface s, out float refW, out float refH)
        {
            float scale = Mathf.Pow(2f, Mathf.Lerp(
                Mathf.Log(s.W / 1080f, 2f), Mathf.Log(s.H / 1920f, 2f), 0.5f));
            refW = s.W / scale;
            refH = s.H / scale;
        }

        private static Dictionary<string, Rect> Place(RectTransform parent, List<Named> bands)
        {
            var map = new Dictionary<string, Rect>();
            for (int i = 0; i < bands.Count; i++)
            {
                var b = bands[i];
                var rt = Region(parent, b.Name,
                    new Vector2(b.Frac.xMin, b.Frac.yMin), new Vector2(b.Frac.xMax, b.Frac.yMax));
                map[b.Name] = WorldRect(rt);
            }
            // A second read AFTER every sibling exists (anchors are independent, but this keeps
            // the numbers honest if a future band ever gains a layout component).
            for (int i = 0; i < bands.Count; i++)
            {
                var rt = FindRegion(parent, bands[i].Name);
                if (rt != null) map[bands[i].Name] = WorldRect(rt);
            }
            return map;
        }

        private static RectTransform FindRegion(RectTransform parent, string name)
        {
            var t = parent != null ? parent.Find(name) : null;
            return t as RectTransform;
        }

        private static void AssertDisjoint(string tag, List<Named> bands, Dictionary<string, Rect> rects,
                                           List<string> failures, StringBuilder log)
        {
            int checkedPairs = 0;
            for (int i = 0; i < bands.Count; i++)
                for (int j = i + 1; j < bands.Count; j++)
                {
                    Rect a, b;
                    if (!rects.TryGetValue(bands[i].Name, out a)) continue;
                    if (!rects.TryGetValue(bands[j].Name, out b)) continue;
                    checkedPairs++;
                    if (Overlaps(a, b))
                        failures.Add(tag + " '" + bands[i].Name + "' " + Fmt(a) + " INTERSECTS '" +
                                     bands[j].Name + "' " + Fmt(b) + " - one of them is drawn on top of " +
                                     "the other on the device.");
                }
            log.AppendLine(tag + " " + checkedPairs + " band pairs measured for intersection");
        }

        private static int CountCollisions(List<Named> bands, Dictionary<string, Rect> rects)
        {
            int n = 0;
            for (int i = 0; i < bands.Count; i++)
                for (int j = i + 1; j < bands.Count; j++)
                {
                    Rect a, b;
                    if (!rects.TryGetValue(bands[i].Name, out a)) continue;
                    if (!rects.TryGetValue(bands[j].Name, out b)) continue;
                    if (Overlaps(a, b)) n++;
                }
            return n;
        }

        private static void AssertTouch(string tag, List<Named> bands, Dictionary<string, Rect> rects,
                                        List<string> failures)
        {
            float floor = ElarionUiKit.MinTouchPx;
            for (int i = 0; i < bands.Count; i++)
            {
                if (!Touchable.Contains(bands[i].Name)) continue;
                Rect r;
                if (!rects.TryGetValue(bands[i].Name, out r)) continue;
                float shortest = Mathf.Min(r.width, r.height);
                if (shortest + Eps < floor)
                    failures.Add(tag + " touch band '" + bands[i].Name + "' resolves to " + Fmt(r) +
                                 " - its shortest side " + shortest.ToString("0") + " is under MinTouchPx(" +
                                 floor.ToString("0") + ") AS AUTHORED, so ClampMinTouch would grow it at " +
                                 "runtime into whatever sits beside it.");
            }
        }

        private static void AssertRowTouch(string tag, List<Named> bands, Dictionary<string, Rect> rects,
                                           List<string> failures)
        {
            float floor = ElarionUiKit.MinTouchPx;
            foreach (string n in new[] { "Row.Minus", "Row.Plus" })
            {
                Rect r;
                if (!rects.TryGetValue(n, out r)) continue;
                float shortest = Mathf.Min(r.width, r.height);
                if (shortest + Eps < floor)
                    failures.Add(tag + " stepper '" + n + "' resolves to " + Fmt(r) + " - shortest side " +
                                 shortest.ToString("0") + " under MinTouchPx(" + floor.ToString("0") +
                                 ") as authored.");
            }
        }

        private static void AssertInsidePanel(string tag, RectTransform panel, List<Named> bands,
                                              Dictionary<string, Rect> rects, List<string> failures)
        {
            Rect outer = WorldRect(panel);
            for (int i = 0; i < bands.Count; i++)
            {
                Rect r;
                if (!rects.TryGetValue(bands[i].Name, out r)) continue;
                if (r.xMin < outer.xMin - Eps || r.xMax > outer.xMax + Eps ||
                    r.yMin < outer.yMin - Eps || r.yMax > outer.yMax + Eps)
                    failures.Add(tag + " '" + bands[i].Name + "' " + Fmt(r) + " spills OUTSIDE the panel " +
                                 Fmt(outer) + " - owner F8 2026-07-04: everything must be inside the panel.");
            }
        }

        /// <summary>
        /// Does <paramref name="text"/> WRAP inside this rect at this size? Measured off the
        /// GENERATED MESH with NORMAL wrapping deliberately ON - that is the failure the owner
        /// photographed, and asking the question with NoWrap already set would answer itself.
        /// <paramref name="why"/> carries the line count either way, or "SKIP..." when no font
        /// resolved (a stand-down that is declared, never a silent pass).
        /// </summary>
        private static bool LabelWraps(RectTransform host, string text, int fontSize,
                                       RectTransform root, out string why)
        {
            why = null;
            if (host == null) { why = "SKIP no host rect was created for the count band"; return false; }

            var go = new GameObject("Probe", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            // The panel insets the label 6% inside its plate; measure the same box it draws in.
            rt.anchorMin = new Vector2(0.06f, 0.06f); rt.anchorMax = new Vector2(0.94f, 0.94f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var t = go.GetComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);
            if (t.font == null) { why = "SKIP no TMP font resolvable - no glyph can be laid out"; return false; }
            t.text = text;
            t.fontSize = fontSize;
            t.enableAutoSizing = false;
            t.textWrappingMode = TextWrappingModes.Normal;      // the failure mode, on purpose
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Center;

            Settle(root);
            t.ForceMeshUpdate();
            var info = t.textInfo;
            if (info == null)
            {
                why = "SKIP TMP produced no textInfo for '" + text + "' after ForceMeshUpdate";
                return false;
            }
            int lines = info.lineCount;
            float pref = t.GetPreferredValues(text).x;
            float boxW = rt.rect.width;
            why = "lines=" + lines + " preferred=" + pref.ToString("0") + " box=" + boxW.ToString("0");
            return lines > 1 || pref > boxW + Eps;
        }

        private static GameObject NewCanvas(string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.hideFlags = HideFlags.HideAndDontSave;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.position = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            return go;
        }

        private static RectTransform Region(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void Settle(RectTransform root)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        private static Rect WorldRect(RectTransform rt)
        {
            if (rt == null) return new Rect();
            rt.GetWorldCorners(_corners);
            float x0 = Mathf.Min(_corners[0].x, _corners[2].x);
            float x1 = Mathf.Max(_corners[0].x, _corners[2].x);
            float y0 = Mathf.Min(_corners[0].y, _corners[2].y);
            float y1 = Mathf.Max(_corners[0].y, _corners[2].y);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        private static bool Overlaps(Rect a, Rect b)
        {
            if (a.width <= Eps || a.height <= Eps || b.width <= Eps || b.height <= Eps) return false;
            return a.xMin + Eps < b.xMax && b.xMin + Eps < a.xMax &&
                   a.yMin + Eps < b.yMax && b.yMin + Eps < a.yMax;
        }

        private static string Fmt(Rect r)
        {
            return "(" + r.xMin.ToString("0") + "," + r.yMin.ToString("0") + ")-(" +
                   r.xMax.ToString("0") + "," + r.yMax.ToString("0") + ")";
        }
    }
}
