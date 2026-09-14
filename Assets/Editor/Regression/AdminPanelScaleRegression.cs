// =============================================================================
// AdminPanelScaleRegression [admin-panel-scale] — WO-1718
// -----------------------------------------------------------------------------
// WHAT BROKE: Settings -> Help -> Dev Tools (DeNelle.HUD.AdminOverlay) rendered
// ~4.2x oversized on the owner's Seeker, every caption overflowing onto the rows
// below it. AdminOverlay.TryBuild creates its OWN runtime PanelSettings and, until
// this WO, set only the name/theme/sortingOrder — so it ran Unity's DEFAULT
// scaleMode, PanelScaleMode.ConstantPhysicalSize (referenceDpi 96), which scales the
// whole panel by realScreenDpi/96. Every OTHER PanelSettings creator in the repo sets
// ScaleWithScreenSize explicitly (BattleSceneBuilder.cs:268-271,
// IntroFlowSceneBuilder.cs:251-254, OnboardingSceneBuilder.cs:328-331,
// ArenaDefensePaletteUI.cs:95-97) and the authored assets carry m_ScaleMode: 2.
//
// ⛔ WHY NO CAPTURE EVER CAUGHT IT, AND WHY THIS SUITE IS A CONFIG CHECK:
// the DPI half of the bug CANNOT reproduce in batchmode or in the editor. With no
// real display, Screen.dpi falls back to PanelSettings.fallbackDpi = 96 — the SAME
// value as referenceDpi — so ConstantPhysicalSize renders exactly 1:1 headless and
// the panel looks perfect in every screenshot ever taken of it. A rendered-pixel
// oracle is therefore structurally incapable of seeing this class of bug. The check
// that actually pins the fix is CASE 1 below: a STATIC read of the three fields on
// the live PanelSettings. It fails on the pre-WO-1718 code by construction, because
// those fields were never assigned and Unity's initializers are
// ConstantPhysicalSize / (1200,800) / match 0.0.
//
// CASE 1  config      — scaleMode / referenceResolution / match / screenMatchMode.
// CASE 2  row metrics — every Button's minHeight >= ElarionUi.TapTarget (88) AND >=
//                       its own font line box; the card ceiling is a PERCENT, not a
//                       pre-ladder desktop-px literal. (Pre-WO-1718: minHeight 38.)
// CASE 3  measured    — best-effort REAL layout pass at 2340x1080 (the Seeker):
//                       button worldBound holds its text on one line and no two
//                       sibling rows intersect. Edit-mode runtime panels are not
//                       driven by the player loop, so the layout tick is reached by
//                       reflection; if it is unavailable this case reports
//                       layout=UNPROVEN(<reason>) and does NOT fail the shared gate —
//                       tooling absence is never a silent pass, but it is also not a
//                       product defect. Reflection here is sanctioned: this is an
//                       Editor regression, not a bridge script (DataRegression itself
//                       reflects), and CASE 1+2 are the load-bearing assertions.
// =============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using DeNelle.Core.UI;
using DeNelle.HUD;

namespace DeNelle.Editor.Regression
{
    public static class AdminPanelScaleRegression
    {
        // The uGUI CanvasScaler every other screen uses (ElarionUiKit.cs:107-111).
        // AdminOverlay's UI Toolkit panel must resolve the SAME reference px.
        private const int ExpectedRefWidth  = 1080;
        private const int ExpectedRefHeight = 1920;
        private const float ExpectedMatch   = 0.5f;

        // A device-realistic landscape frame (owner's Seeker; landscape-only ruling 2026-09-10).
        private const int DeviceWidth  = 2340;
        private const int DeviceHeight = 1080;

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            GameObject host = null;
            PanelSettings created = null;
            RenderTexture rt = null;

            try
            {
                host = new GameObject("AdminPanelScaleRegressionHost");
                host.hideFlags = HideFlags.HideAndDontSave;
                var overlay = host.AddComponent<AdminOverlay>();

                // Awake does not run in edit mode (AdminOverlay is not [ExecuteAlways]), so the
                // build is driven directly — the same public entry HelpMenu uses.
                overlay.TryBuild(null);

                var doc = host.GetComponent<UIDocument>();
                if (doc == null)
                {
                    reason = "FAIL admin-panel-scale: AdminOverlay.TryBuild left no UIDocument on the host.";
                    return false;
                }
                var ps = doc.panelSettings;
                created = ps;
                if (ps == null)
                {
                    reason = "FAIL admin-panel-scale: UIDocument.panelSettings is null after TryBuild.";
                    return false;
                }

                // ── CASE 1: the config that IS the bug ───────────────────────────
                if (ps.scaleMode != PanelScaleMode.ScaleWithScreenSize)
                {
                    failures.Add("AdminOverlay PanelSettings.scaleMode is " + ps.scaleMode +
                                 ", expected ScaleWithScreenSize. Unity's default is ConstantPhysicalSize, " +
                                 "which scales the panel by realScreenDpi/96 (~4.2x on a 400dpi phone) — " +
                                 "WO-1718, the oversized Dev Tools panel.");
                }
                var refRes = ps.referenceResolution;
                if (refRes.x != ExpectedRefWidth || refRes.y != ExpectedRefHeight)
                {
                    failures.Add("AdminOverlay PanelSettings.referenceResolution is " + refRes +
                                 ", expected (" + ExpectedRefWidth + "," + ExpectedRefHeight +
                                 ") to match the uGUI CanvasScaler at ElarionUiKit.cs:109 — the font " +
                                 "ladder (ElarionUi.cs:111-115) is authored in THOSE reference px.");
                }
                if (!Mathf.Approximately(ps.match, ExpectedMatch))
                {
                    failures.Add("AdminOverlay PanelSettings.match is " + ps.match.ToString("0.###") +
                                 ", expected " + ExpectedMatch.ToString("0.###") +
                                 " (ElarionUiKit.cs:111). Unity's default is 0.0, so it must be set explicitly.");
                }
                if (ps.screenMatchMode != PanelScreenMatchMode.MatchWidthOrHeight)
                {
                    failures.Add("AdminOverlay PanelSettings.screenMatchMode is " + ps.screenMatchMode +
                                 ", expected MatchWidthOrHeight (ElarionUiKit.cs:110; the authored " +
                                 "OnboardingPanelSettings.asset carries m_ScreenMatchMode: 0).");
                }
                log.Append("scaleMode=").Append(ps.scaleMode)
                   .Append(" refRes=").Append(refRes)
                   .Append(" match=").Append(ps.match.ToString("0.###"))
                   .Append(" screenMatch=").Append(ps.screenMatchMode);

                var root = doc.rootVisualElement;
                if (root == null)
                {
                    failures.Add("UIDocument.rootVisualElement is null — the overlay tree was never built.");
                    reason = Verdict(failures, log);
                    return failures.Count == 0;
                }

                var buttons = new List<Button>(root.Query<Button>().ToList());
                log.Append(" buttons=").Append(buttons.Count);
                if (buttons.Count == 0)
                {
                    failures.Add("AdminOverlay built ZERO buttons — the dev panel would be empty.");
                }

                // ── CASE 2: row metrics, independent of any rendered pixel ───────
                int tooShort = 0;
                string firstShort = null;
                foreach (var b in buttons)
                {
                    float minH = ResolveLength(b.style.minHeight);
                    float font = ResolveLength(b.style.fontSize);
                    if (font <= 0f) font = ElarionUi.FontBody;
                    float lineBox = font * 1.15f;   // a bold UI line box is ~1.15em
                    float floor = Mathf.Max(ElarionUi.TapTarget, lineBox);
                    if (minH + 0.01f < floor)
                    {
                        tooShort++;
                        if (firstShort == null)
                        {
                            firstShort = b.text + " minHeight=" + minH.ToString("0") +
                                         " fontSize=" + font.ToString("0") +
                                         " needs >=" + floor.ToString("0");
                        }
                    }
                }
                if (tooShort > 0)
                {
                    failures.Add(tooShort + " AdminOverlay button row(s) are shorter than the mobile touch " +
                                 "floor / their own text line box (first: " + firstShort + "). WO-1718: the " +
                                 "minHeight = 38 override undercut ElarionUi.StyleButton's TapTarget (88) and " +
                                 "could not hold a FontBody (50) line — rows drew on top of each other.");
                }

                var cardMax = FindCardMaxWidth(root);
                if (cardMax.unit != LengthUnit.Percent)
                {
                    failures.Add("The AdminOverlay card's maxWidth is " + cardMax.value + " " + cardMax.unit +
                                 ", expected a PERCENT of the panel. A fixed pre-ladder literal (was 560px) " +
                                 "cannot hold a ~45-character caption at fontSize 50 on one line — WO-1718.");
                }
                log.Append(" cardMaxWidth=").Append(cardMax.value).Append(cardMax.unit == LengthUnit.Percent ? "%" : "px");

                // ── CASE 3: best-effort MEASURED layout at the device frame ──────
                rt = new RenderTexture(DeviceWidth, DeviceHeight, 0);
                rt.hideFlags = HideFlags.HideAndDontSave;
                string layoutNote = MeasuredLayoutCheck(ps, root, rt, failures);
                log.Append(' ').Append(layoutNote);
            }
            catch (System.Exception e)
            {
                // A throw inside the Guard.Try registration wrapper would be swallowed into a
                // silent PASS — catch it here and turn it into a real failure line.
                reason = "FAIL admin-panel-scale: threw — " + e.GetType().Name + ": " + e.Message;
                Cleanup(host, created, rt);
                return false;
            }

            Cleanup(host, created, rt);
            reason = Verdict(failures, log);
            return failures.Count == 0;
        }

        private static string Verdict(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                return "OK admin-panel-scale: " + log;
            }
            var sb = new StringBuilder();
            sb.Append("FAIL admin-panel-scale (").Append(failures.Count).Append("): ");
            for (int i = 0; i < failures.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(failures[i]);
            }
            sb.Append("  [measured: ").Append(log).Append(']');
            return sb.ToString();
        }

        private static void Cleanup(GameObject host, PanelSettings ps, RenderTexture rt)
        {
            if (rt != null)
            {
                if (ps != null && ps.targetTexture == rt) ps.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            if (host != null) Object.DestroyImmediate(host);
            // The runtime PanelSettings is created by AdminOverlay itself and is not owned by
            // any asset — destroying the host does not collect it.
            if (ps != null) Object.DestroyImmediate(ps);
        }

        private static float ResolveLength(StyleLength s)
        {
            if (s.keyword != StyleKeyword.Undefined && s.keyword != StyleKeyword.Null) return 0f;
            return s.value.value;
        }

        /// <summary>
        /// The card is the single child of the overlay backdrop (AdminOverlay.BuildUi).
        /// Returns its maxWidth Length, or a 0px Length if the tree shape changed.
        /// </summary>
        private static Length FindCardMaxWidth(VisualElement root)
        {
            foreach (var child in root.Children())
            {
                foreach (var grandchild in child.Children())
                {
                    var style = grandchild.style.maxWidth;
                    if (style.keyword == StyleKeyword.Undefined || style.keyword == StyleKeyword.Null)
                    {
                        return style.value;
                    }
                }
            }
            return new Length(0f, LengthUnit.Pixel);
        }

        /// <summary>
        /// Drives a real layout pass at the device frame and asserts the rendered geometry.
        /// Returns a note for the log. Adds to <paramref name="failures"/> ONLY on a genuine
        /// geometry defect — an unavailable layout tick reports UNPROVEN instead.
        /// </summary>
        private static string MeasuredLayoutCheck(PanelSettings ps, VisualElement root,
                                                  RenderTexture rt, List<string> failures)
        {
            // A panel with a targetTexture takes its size from that texture, which is how a
            // device frame is expressed headlessly.
            ps.targetTexture = rt;

            var panel = root.panel;
            if (panel == null) return "layout=UNPROVEN(root.panel is null in edit mode)";

            // Edit-mode runtime panels are not ticked by the player loop. BaseVisualElementPanel
            // exposes the layout tick internally; the names were confirmed present in
            // UnityEngine.UIElementsModule.dll for 6000.4.8f1 before this reflection was written.
            var panelType = panel.GetType();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            var tick = panelType.GetMethod("ValidateLayout", flags, null, System.Type.EmptyTypes, null)
                       ?? panelType.GetMethod("UpdateWithoutRepaint", flags, null, System.Type.EmptyTypes, null);
            if (tick == null) return "layout=UNPROVEN(no ValidateLayout/UpdateWithoutRepaint on " + panelType.Name + ")";

            try { tick.Invoke(panel, null); }
            catch (System.Exception e) { return "layout=UNPROVEN(layout tick threw " + e.GetType().Name + ")"; }

            var buttons = new List<Button>(root.Query<Button>().ToList());
            int measured = 0, clipped = 0, overlapped = 0;
            string firstClipped = null, firstOverlap = null;

            foreach (var b in buttons)
            {
                var wb = b.worldBound;
                if (wb.width <= 1f || wb.height <= 1f) continue;   // never laid out
                measured++;

                float font = ResolveLength(b.style.fontSize);
                if (font <= 0f) font = ElarionUi.FontBody;
                Vector2 text;
                try
                {
                    text = b.MeasureTextSize(b.text, 0f, VisualElement.MeasureMode.Undefined,
                                                       0f, VisualElement.MeasureMode.Undefined);
                }
                catch (System.Exception)
                {
                    text = new Vector2(0f, font * 1.15f);   // font assets unavailable headless
                }
                if (text.y <= 0f) text.y = font * 1.15f;

                if (wb.height + 0.5f < text.y && firstClipped == null)
                {
                    clipped++;
                    string h = wb.height.ToString("0");
                    string t = text.y.ToString("0");
                    firstClipped = b.text + " row height " + h + " < text line " + t;
                }
                else if (wb.height + 0.5f < text.y)
                {
                    clipped++;
                }
            }

            // Sibling overlap: rows in a flex column must never intersect.
            for (int i = 0; i < buttons.Count; i++)
            {
                for (int j = i + 1; j < buttons.Count; j++)
                {
                    var a = buttons[i];
                    var b = buttons[j];
                    if (a.parent != b.parent) continue;
                    var ra = a.worldBound;
                    var rb = b.worldBound;
                    if (ra.width <= 1f || rb.width <= 1f) continue;
                    if (!ra.Overlaps(rb)) continue;
                    overlapped++;
                    if (firstOverlap == null)
                    {
                        firstOverlap = a.text + " overlaps " + b.text;
                    }
                }
            }

            if (measured == 0)
            {
                ps.targetTexture = null;
                return "layout=UNPROVEN(no button was laid out — worldBound is empty in this context)";
            }
            if (clipped > 0)
            {
                failures.Add(clipped + " AdminOverlay button row(s) are shorter than their own text line at " +
                             DeviceWidth + "x" + DeviceHeight + " (first: " + firstClipped +
                             ") — the caption draws outside its row (WO-1718).");
            }
            if (overlapped > 0)
            {
                failures.Add(overlapped + " AdminOverlay sibling row(s) INTERSECT at " + DeviceWidth + "x" +
                             DeviceHeight + " (first: " + firstOverlap + ") — overlapping dev-panel rows " +
                             "are exactly the owner-reported defect (WO-1718).");
            }

            ps.targetTexture = null;
            return "layout=MEASURED(" + measured + " rows at " + DeviceWidth + "x" + DeviceHeight +
                   ", clipped=" + clipped + ", overlaps=" + overlapped + ")";
        }
    }
}
