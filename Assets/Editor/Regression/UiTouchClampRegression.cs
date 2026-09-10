// =====================================================================
//  UiTouchClampRegression — WO-1060 section 6.2 / 6.3. THE RED-THEN-GREEN PROOF.
// ---------------------------------------------------------------------
//  WHAT THIS SUITE IS FOR, AND WHY IT IS NOT THE CAPTURE HARNESS.
//
//  UICaptureLaunch.AuditGeometry measures the REAL panels, which is where the
//  defects actually are — but it can only ever tell you what it found. It can
//  never tell you that it is CAPABLE of finding anything. A rule with a typo, an
//  inverted comparison, or a predicate that silently excludes every control
//  reports a clean run and looks identical to a healthy one. That is exactly the
//  shape of the blindness WO-1060 exists to end, and PROD-008 states the rule:
//  AN ORACLE NEVER SEEN RED IS NOT EVIDENCE.
//
//  So this suite hands DeNelle.Core.UI.LayoutOracle canvases whose defects are
//  AUTHORED ON PURPOSE, and fails if the oracle does not catch them:
//
//    RED-A  a control authored 21.6 ref px tall  -> Assert A must fire
//    RED-B  two same-size controls stacked       -> Assert B must fire
//    RED-B2 the same, across DIFFERENT parents   -> Assert B must fire AND
//                                                   classify SameParent=false
//    RED-C  a caption in a band too small for it -> Assert C must fire (WO-1630)
//    GREEN  the identical controls, laid apart   -> the oracle must be silent
//    GREEN-C the same caption given room         -> Assert C must be silent
//
//  RED-B2 is the case the sibling-only test walked past for months (WO-1058's
//  Cancel inside Upgrade's band; the Night Market row drawn over the row above,
//  which ate a price's leading digit). It is pinned separately so a future
//  narrowing of Assert B back to siblings turns THIS suite red instead of
//  quietly restoring the blind spot.
//
//  ⛔ THE MESSAGE IS ASSERTED, NOT JUST THE COUNT. The owner is red/green
//  colourblind; a failure line that does not NAME both widgets and the exact
//  overlap in px is not actionable, so the suite fails a finding whose wording
//  has lost either widget's path or the numbers. Downgrading these string
//  assertions to a bare count would keep the suite green while making every
//  future failure useless to read.
//
//  ⚠ MEASUREMENT. Every case is built at TWO landscape aspects (WO-1060 §3/§6.4).
//  A band that clears MinTouchPx at one aspect can fall under it at another, and
//  a single-aspect proof would miss that entire class. The canvas is WORLD-SPACE
//  and sized by hand to the reference-px extent a ScaleWithScreenSize +
//  MatchWidthOrHeight scaler resolves for that target, because a ScreenSpace
//  canvas in an edit-mode batchmode call reports the editor's own 640x480 —
//  reading a raw rect there was F8-5's root cause.
//
//  ⚠ NO CATALOG, NO PANEL BUILDERS, AND NO TMP IN CASES A / B / B2 / GREEN. Those
//  controls are built by hand and armed with ElarionUiKit.ClampMinTouch, so they
//  cannot go red because a font asset or a sprite failed to resolve headless. They
//  prove the ORACLE, not the art pipeline.
//
//  ⛔ RED-C AND GREEN-C ARE THE DECLARED EXCEPTION, AND THEY MUST BE (WO-1630).
//  Assert C measures GLYPHS ON A GENERATED MESH; there is no way to author that
//  defect without a real TMP_Text and a resolvable font, so the constraint above
//  cannot be honoured by the one case that needs it. It is not quietly bent —
//  it is bounded, and the boundary is enforced:
//    * both cases call ElarionUiKit.EnsureFont, the production resolution seam;
//    * if no font resolves, the case adds a FAILURE that says the rule could not
//      be proved. It does NOT stand down green. A suite that reports a clean line
//      because its fixture never loaded is the exact blindness this whole file
//      exists to end, and CLAUDE.md §11B forbids reading an unproved thing as a
//      proved one;
//    * a TextUnmeasured finding is likewise treated as a FAILURE of RED-C, never
//      as the red it was looking for — a PartialSkip is not a red.
//  So a missing font asset turns this suite RED with a message naming the font as
//  the cause, and can never turn it green.
// =====================================================================

using System.Collections.Generic;
using System.Text;
using TMPro;
using DeNelle.Core.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Editor.Regression
{
    public static class UiTouchClampRegression
    {
        /// <summary>Landscape targets. Two, deliberately: 16:9 and the Seeker's tall-landscape.</summary>
        private static readonly Vector2Int[] Aspects =
        {
            new Vector2Int(1920, 1080),
            new Vector2Int(2340, 1080),
        };

        // The kit's scaler settings, mirrored so the reference-px extent can be computed
        // without a live CanvasScaler.Update (which does not run in a synchronous call).
        private const float RefW = 1080f, RefH = 1920f, Match = 0.5f;

        [MenuItem("Tools/Regression/UI/Touch + Overlap Oracle (WO-1060)")]
        public static void RunMenu()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason); else Debug.LogError(reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            int casesRun = 0;

            foreach (var a in Aspects)
            {
                casesRun += 6;
                CaseSubFloor(a.x, a.y, failures, log);
                CaseOverlapSiblings(a.x, a.y, failures, log);
                CaseOverlapCrossParent(a.x, a.y, failures, log);
                CaseGlyphTruncated(a.x, a.y, failures, log);   // WO-1630 RED-C
                CaseGlyphFits(a.x, a.y, failures, log);        // WO-1630 GREEN-C
                CaseClean(a.x, a.y, failures, log);
            }

            if (failures.Count > 0)
            {
                var sb = new StringBuilder("UI_TOUCH_ORACLE_FAIL x" + failures.Count +
                    " over " + casesRun + " cases -- the clamp/overlap oracle did not behave as pinned. " +
                    "Until this is green, a clean UI_TOUCH_OK proves NOTHING.\n");
                for (int i = 0; i < failures.Count; i++) sb.AppendLine("  - " + failures[i]);
                sb.Append(log);
                reason = sb.ToString();
                return false;
            }

            reason = "UI_TOUCH_ORACLE_OK " + casesRun + "/" + casesRun + " cases -- LayoutOracle went RED on " +
                     "an authored sub-MinTouchPx(" + ElarionUiKit.MinTouchPx.ToString("0.#") + ") band and on " +
                     "stacked controls (sibling AND cross-parent), named both widgets and the overlap in px, " +
                     "and stayed silent on the same controls laid apart -- at " + Aspects.Length + " landscape aspects. " +
                     "WO-1630: it ALSO went red on a caption whose band cannot hold it, naming the label and " +
                     "both glyph counts, and stayed silent on the same caption given room.\n" + log;
            return true;
        }

        // ---------------------------------------------------------------------
        //  RED-A — Assert A must fire on an authored sub-floor band.
        // ---------------------------------------------------------------------
        private static void CaseSubFloor(int w, int h, List<string> failures, StringBuilder log)
        {
            string at = w + "x" + h;
            GameObject canvas = null;
            try
            {
                canvas = BuildCanvas(w, h, out float refPxH);
                var host = Host(canvas.transform, "SubFloorHost");

                // 2% of the canvas height. On a 1080-tall landscape target that resolves to
                // ~21.6 ref px -- the WO-1056 signature, a band authored as a fraction of a
                // small sub-zone that lands a long way under the 112 floor.
                var offender = KitButton(host, "slot-chip-0", new Vector2(0.10f, 0.50f), new Vector2(0.40f, 0.52f));
                // A healthy control in the same canvas, so a rule that flags EVERYTHING also fails.
                KitButton(host, "healthy-cta", new Vector2(0.60f, 0.30f), new Vector2(0.95f, 0.70f));

                Settle(canvas);
                var found = LayoutOracle.Audit(canvas, "SyntheticSubFloor", w, h);

                var hit = First(found, LayoutOracle.FindingKind.SubTouchFloorBand);
                if (hit == null)
                {
                    failures.Add("RED-A @" + at + ": a control authored ~" + (refPxH * 0.02f).ToString("0.#") +
                                 " ref px tall (floor is " + ElarionUiKit.MinTouchPx.ToString("0.#") +
                                 ") produced NO SubTouchFloorBand finding. Assert A is not firing; every " +
                                 "clean UI_TOUCH_OK since is worthless.");
                    return;
                }
                if (!hit.Contains("slot-chip-0"))
                    failures.Add("RED-A @" + at + ": Assert A fired but did not NAME the offending widget " +
                                 "('slot-chip-0' absent). The owner cannot act on an unnamed control. Line: " + hit);
                if (!hit.Contains("UNDER"))
                    failures.Add("RED-A @" + at + ": Assert A fired but did not state the shortfall in px. Line: " + hit);
                if (hit.Contains("healthy-cta"))
                    failures.Add("RED-A @" + at + ": Assert A flagged the HEALTHY control too -- the rule is " +
                                 "over-firing and would be suppressed within a week. Line: " + hit);

                log.AppendLine("  [red-A @" + at + "] " + hit);
            }
            finally { Kill(canvas); }
        }

        // ---------------------------------------------------------------------
        //  RED-B — Assert B must fire on two SIBLING controls in one place.
        // ---------------------------------------------------------------------
        private static void CaseOverlapSiblings(int w, int h, List<string> failures, StringBuilder log)
        {
            string at = w + "x" + h;
            GameObject canvas = null;
            try
            {
                canvas = BuildCanvas(w, h, out _);
                var host = Host(canvas.transform, "Row");

                // Both comfortably ABOVE the touch floor -- this is the case Assert A can
                // never see, and the reason Assert B exists (WO-1058: Cancel authored
                // inside the band Upgrade already owned, both correctly sized).
                KitButton(host, "Upgrade", new Vector2(0.76f, 0.20f), new Vector2(0.98f, 0.80f));
                KitButton(host, "Cancel", new Vector2(0.885f, 0.20f), new Vector2(0.98f, 0.80f));

                Settle(canvas);
                var found = LayoutOracle.Audit(canvas, "SyntheticOverlapSiblings", w, h);

                var hit = First(found, LayoutOracle.FindingKind.ButtonsOverlap);
                if (hit == null)
                {
                    failures.Add("RED-B @" + at + ": 'Upgrade'(0.76-0.98) and 'Cancel'(0.885-0.98) were laid " +
                                 "in the same band and Assert B did NOT fire. Two tap targets in one place and " +
                                 "nothing said so -- this is WO-1058's defect walking free again.");
                    return;
                }
                if (!hit.Contains("Upgrade") || !hit.Contains("Cancel"))
                    failures.Add("RED-B @" + at + ": Assert B fired but did not name BOTH widgets. A collision " +
                                 "message that names one side is not actionable. Line: " + hit);
                if (!hit.Contains("share"))
                    failures.Add("RED-B @" + at + ": Assert B fired without stating the overlap in ref px. " +
                                 "'these overlap' is useless; the numbers are the fix. Line: " + hit);

                var f = FirstFinding(found, LayoutOracle.FindingKind.ButtonsOverlap);
                if (f.HasValue && !f.Value.SameParent)
                    failures.Add("RED-B @" + at + ": two SIBLINGS were classified cross-parent, so the sibling " +
                                 "half would stop feeding the pre-existing UI_GEOMETRY gate. Line: " + hit);

                log.AppendLine("  [red-B @" + at + "] " + hit);
            }
            finally { Kill(canvas); }
        }

        // ---------------------------------------------------------------------
        //  RED-B2 — Assert B must ALSO fire across parents, and say so.
        // ---------------------------------------------------------------------
        private static void CaseOverlapCrossParent(int w, int h, List<string> failures, StringBuilder log)
        {
            string at = w + "x" + h;
            GameObject canvas = null;
            try
            {
                canvas = BuildCanvas(w, h, out _);
                // Two shelf ROWS. Row B's card is drawn over row A's card -- the 2026-08-22
                // Night Market frame where 120 SKR read as "20 SKR" because a neighbour ate
                // the leading digit. Two rows are two parents, which is precisely what the
                // old sibling-only test could not see.
                var rowA = Host(canvas.transform, "ShelfRowA", new Vector2(0.05f, 0.40f), new Vector2(0.95f, 0.75f));
                var rowB = Host(canvas.transform, "ShelfRowB", new Vector2(0.05f, 0.25f), new Vector2(0.95f, 0.60f));
                KitButton(rowA, "card-120-SKR", new Vector2(0.05f, 0.05f), new Vector2(0.45f, 0.95f));
                KitButton(rowB, "card-overlapping", new Vector2(0.05f, 0.05f), new Vector2(0.45f, 0.95f));

                Settle(canvas);
                var found = LayoutOracle.Audit(canvas, "SyntheticOverlapCrossParent", w, h);

                var f = FirstFinding(found, LayoutOracle.FindingKind.ButtonsOverlap);
                if (!f.HasValue)
                {
                    failures.Add("RED-B2 @" + at + ": two cards in DIFFERENT shelf rows were drawn on top of " +
                                 "each other and Assert B did NOT fire. The sibling-only narrowing is back, and " +
                                 "it is the narrowing that let a wrong price ship on the money screen.");
                    return;
                }
                if (f.Value.SameParent)
                    failures.Add("RED-B2 @" + at + ": a CROSS-PARENT pair was classified SameParent, which would " +
                                 "route it into the pre-existing UI_GEOMETRY gate and redden unrelated commits -- " +
                                 "the exact thing WO-1060 §5 forbids. Line: " + f.Value.Message);
                if (!f.Value.Message.Contains("card-120-SKR") || !f.Value.Message.Contains("card-overlapping"))
                    failures.Add("RED-B2 @" + at + ": cross-parent finding did not name both cards. Line: " + f.Value.Message);

                log.AppendLine("  [red-B2 @" + at + "] " + f.Value.Message);
            }
            finally { Kill(canvas); }
        }

        // ---------------------------------------------------------------------
        //  RED-C — Assert C must fire when a caption's band cannot hold it.
        //
        //  The defect is authored to WO-1628's own signature: the Build Collections
        //  category subtitle, fitted with the production ElarionUiKit.FitBlock (normal
        //  wrap, bounded autosize, Truncate) into a band too short for the copy. That
        //  panel rendered "nothing affordable y" — the last word cut after one letter — at
        //  two of three aspects while a geometry run on the same log passed 91 canvases,
        //  because every other rule on that path measures WHERE the rect is.
        //
        //  ⚠ WO-1628's own figure for that panel is "21 of 22", and it is DELIBERATELY NOT
        //  reused as an expectation here: that pair is characterCount vs text.Length, a
        //  different metric on a different denominator from this rule's visible-glyphs vs
        //  printable-characters. Carrying a number across metrics is how this repo's copied
        //  state goes stale, so the counts are read off the run, never quoted from a doc.
        //
        //  A healthy caption carrying the SAME string sits in the same canvas, so a rule
        //  that flags EVERY label fails here rather than shipping and being suppressed.
        // ---------------------------------------------------------------------
        private const string GlyphProbeCopy = "Nothing you can afford is in here yet";

        private static void CaseGlyphTruncated(int w, int h, List<string> failures, StringBuilder log)
        {
            string at = w + "x" + h;
            GameObject canvas = null;
            try
            {
                canvas = BuildCanvas(w, h, out _);
                var host = Host(canvas.transform, "CardGrid");

                // A band ~6% x 3% of the canvas: at 1920x1080 reference px that is roughly
                // 115x32, which cannot hold 37 characters at the 18px autosize floor.
                var cut = KitLabel(host, "cut-caption", GlyphProbeCopy,
                                   new Vector2(0.06f, 0.60f), new Vector2(0.12f, 0.63f), failures, at);
                var roomy = KitLabel(host, "roomy-caption", GlyphProbeCopy,
                                     new Vector2(0.35f, 0.30f), new Vector2(0.90f, 0.55f), failures, at);
                if (cut == null || roomy == null) return;   // KitLabel already recorded WHY

                Settle(canvas);
                var found = LayoutOracle.Audit(canvas, "SyntheticGlyphTruncated", w, h, out int measured);

                // THE SAME FLOOR GREEN-C CARRIES, for the same reason: two labels were authored
                // into this canvas, so anything less than two measured means the fixture, not the
                // rule, is what this run is reporting on.
                if (measured < 2)
                {
                    failures.Add("RED-C @" + at + ": Assert C measured " + measured + " of the 2 labels " +
                                 "this case authored, so whatever it did or did not find is a statement " +
                                 "about the FIXTURE, not about the rule. Not a red and not a pass.");
                    return;
                }

                // ⛔ A STAND-DOWN IS NOT A RED. If the oracle could not measure the label,
                // RED-C proved nothing and says so rather than passing on the wrong kind.
                var unmeasured = First(found, LayoutOracle.FindingKind.TextUnmeasured);
                if (unmeasured != null)
                {
                    failures.Add("RED-C @" + at + ": Assert C could NOT MEASURE the fixture, so the rule " +
                                 "is UNPROVEN this run -- not passed. A run where nothing could be measured " +
                                 "must never read as a run where everything fit. Line: " + unmeasured);
                    return;
                }

                var hit = First(found, LayoutOracle.FindingKind.TextTruncated);
                if (hit == null)
                {
                    failures.Add("RED-C @" + at + ": a " + GlyphProbeCopy.Length + "-character caption was " +
                                 "fitted into a band ~6% x 3% of the canvas with the production FitBlock " +
                                 "(Truncate) and Assert C did NOT fire. Glyph survival is not being measured, " +
                                 "and every clean glyph marker since is worthless -- this is WO-1628's defect " +
                                 "walking free, where the rect is in the right place and the words are gone.");
                    return;
                }
                if (!hit.Contains("cut-caption"))
                    failures.Add("RED-C @" + at + ": Assert C fired but did not NAME the offending label " +
                                 "('cut-caption' absent). The owner is colourblind and cannot act on an " +
                                 "unnamed control. Line: " + hit);
                if (!hit.Contains(" of "))
                    failures.Add("RED-C @" + at + ": Assert C fired without BOTH counts (drawn of printable). " +
                                 "'this is truncated' is useless; the numbers are what size the band. Line: " + hit);
                // EVERY finding is checked, not just the first: a rule that flags the healthy
                // caption in a LATER line is over-firing exactly as much, and would be suppressed
                // within a week. First-line-only would have let that through.
                for (int i = 0; i < found.Count; i++)
                    if (found[i].Message.Contains("roomy-caption"))
                        failures.Add("RED-C @" + at + ": Assert C flagged the HEALTHY caption too -- the rule " +
                                     "is over-firing and would be suppressed within a week. Line: " + found[i].Message);

                log.AppendLine("  [red-C @" + at + "] " + hit);
            }
            finally { Kill(canvas); }
        }

        // ---------------------------------------------------------------------
        //  GREEN-C — the same caption, given room. Assert C must be silent.
        // ---------------------------------------------------------------------
        private static void CaseGlyphFits(int w, int h, List<string> failures, StringBuilder log)
        {
            string at = w + "x" + h;
            GameObject canvas = null;
            try
            {
                canvas = BuildCanvas(w, h, out _);
                var host = Host(canvas.transform, "CardGrid");

                var roomy = KitLabel(host, "roomy-caption", GlyphProbeCopy,
                                     new Vector2(0.20f, 0.30f), new Vector2(0.90f, 0.60f), failures, at);
                if (roomy == null) return;

                Settle(canvas);
                var found = LayoutOracle.Audit(canvas, "SyntheticGlyphFits", w, h, out int measured);

                // ⛔ THE FLOOR, AND IT IS ASSERTED BEFORE ANY EXISTENCE TEST. A GREEN case's whole
                // verdict is SILENCE, so without this every assertion below hangs off "a finding
                // exists" and the method checks ZERO things when the fixture is absent -- it would
                // report green over a canvas that measured nothing at all. That is
                // RaidCooldownRegression case 5 (2026-08-21), where a teardown left a DESTROYED
                // state installed and the cases under it asserted against nothing while staying in
                // the green column. `measured` is Assert C's own count of labels it actually read a
                // mesh for, so this asks the oracle to prove it looked before its silence is
                // allowed to mean anything.
                if (measured < 1)
                {
                    string why = First(found, LayoutOracle.FindingKind.TextUnmeasured) ??
                                 "and NO TextUnmeasured finding either, so Assert C's exclusions " +
                                 "skipped the label outright rather than standing down on it";
                    failures.Add("GREEN-C @" + at + ": Assert C MEASURED NOTHING (labels=" + measured +
                                 ") on a canvas built with one healthy caption, so this case's silence " +
                                 "proves nothing and is NOT a pass. " + why);
                    return;
                }

                var hit = First(found, LayoutOracle.FindingKind.TextTruncated);
                if (hit != null)
                {
                    failures.Add("GREEN-C @" + at + ": a caption with room to spare was reported TRUNCATED. " +
                                 "A rule that fires on healthy copy gets suppressed, not fixed. Line: " + hit);
                    return;
                }
                log.AppendLine("  [green-C @" + at + "] " + GlyphProbeCopy.Length +
                               " chars in a 70% x 30% band -- Assert C silent.");
            }
            finally { Kill(canvas); }
        }

        // ---------------------------------------------------------------------
        //  GREEN — the same controls, laid correctly. The oracle must be silent.
        // ---------------------------------------------------------------------
        private static void CaseClean(int w, int h, List<string> failures, StringBuilder log)
        {
            string at = w + "x" + h;
            GameObject canvas = null;
            try
            {
                canvas = BuildCanvas(w, h, out _);
                var host = Host(canvas.transform, "Row");

                // Same two labels as RED-B, now in disjoint bands, both above the floor.
                KitButton(host, "Upgrade", new Vector2(0.50f, 0.20f), new Vector2(0.72f, 0.80f));
                KitButton(host, "Cancel", new Vector2(0.76f, 0.20f), new Vector2(0.98f, 0.80f));

                Settle(canvas);
                var found = LayoutOracle.Audit(canvas, "SyntheticClean", w, h);

                if (found.Count != 0)
                {
                    var sb = new StringBuilder("GREEN @" + at + ": a correct layout produced " + found.Count +
                        " finding(s). A rule that fires on healthy panels gets suppressed, not fixed:");
                    for (int i = 0; i < found.Count; i++) sb.Append("\n      " + found[i].Message);
                    failures.Add(sb.ToString());
                    return;
                }
                log.AppendLine("  [green @" + at + "] 2 controls, both >= " +
                               ElarionUiKit.MinTouchPx.ToString("0.#") + " px, disjoint -- oracle silent.");
            }
            finally { Kill(canvas); }
        }

        // ---------------------------------------------------------------------
        //  Fixture plumbing.
        // ---------------------------------------------------------------------

        /// <summary>A world-space canvas whose rect IS the reference-px extent the kit's
        /// ScaleWithScreenSize/MatchWidthOrHeight scaler resolves for this target, so every
        /// number the oracle prints is directly comparable to an authored fraction.</summary>
        private static GameObject BuildCanvas(int w, int h, out float refPxH)
        {
            float sf = ScaleFactor(w, h);
            float pxW = w / sf, pxH = h / sf;
            refPxH = pxH;

            var go = new GameObject("~UiTouchOracleProbe", typeof(RectTransform), typeof(Canvas));
            go.hideFlags = HideFlags.HideAndDontSave;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;   // NOT overlay: an overlay canvas in an
                                                         // edit-mode call reports the editor's own
                                                         // 640x480 and every measurement is fiction.
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(pxW, pxH);
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

        private static Transform Host(Transform parent, string name)
        {
            return Host(parent, name, Vector2.zero, Vector2.one);
        }

        private static Transform Host(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go.transform;
        }

        /// <summary>A kit-contract button: real Button, real visible graphic, armed with the
        /// production <see cref="ElarionUiKit.ClampMinTouch"/> guard so Assert A's
        /// "is this a kit button" predicate sees exactly what it sees on a real panel.
        /// Built by hand rather than through ElarionUiKit.Button so a missing font asset or
        /// an unimported sprite cannot redden a suite that is about geometry.</summary>
        private static Button KitButton(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = Color.white;                     // opaque: HasVisibleGraphic must see it
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            ElarionUiKit.ClampMinTouch(btn);             // the production guard, not a stand-in
            return btn;
        }

        /// <summary>A kit-contract CAPTION for Assert C: a real TMP_Text, fitted with the
        /// production <see cref="ElarionUiKit.FitBlock"/> (normal wrap, bounded autosize,
        /// Truncate) so the fixture carries exactly the settings the Build Collections subtitle
        /// carries on a real panel.
        /// <para>⛔ RETURNS NULL AND RECORDS A FAILURE WHEN NO FONT RESOLVES. It never returns a
        /// fontless label for the case to measure, because the resulting silence would be
        /// indistinguishable from a healthy layout — see this file's header. The caller stops.</para></summary>
        private static TMP_Text KitLabel(Transform parent, string name, string copy,
                                         Vector2 min, Vector2 max, List<string> failures, string at)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var t = go.GetComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);                  // the production resolution seam
            if (t.font == null)
            {
                failures.Add("GLYPH FIXTURE @" + at + ": ElarionUiKit.EnsureFont resolved NO TMP font for '" +
                             name + "', so Assert C cannot be proved this run. This is a FAILURE, not a " +
                             "stand-down: a suite that reports clean because its fixture never loaded is the " +
                             "blindness this file exists to end. Fix the font asset, then re-run.");
                return null;
            }
            t.text = copy;
            t.color = Color.white;                       // opaque: the alpha exclusion must not eat it
            t.alignment = TextAlignmentOptions.Top;
            ElarionUiKit.FitBlock(t, 18f, 21f);  // the production fit, not a stand-in
            return t;
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

        private static string First(List<LayoutOracle.Finding> found, LayoutOracle.FindingKind kind)
        {
            var f = FirstFinding(found, kind);
            return f.HasValue ? f.Value.Message : null;
        }

        private static LayoutOracle.Finding? FirstFinding(List<LayoutOracle.Finding> found, LayoutOracle.FindingKind kind)
        {
            for (int i = 0; i < found.Count; i++)
                if (found[i].Kind == kind) return found[i];
            return null;
        }

        private static void Kill(GameObject go)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
    }
}
