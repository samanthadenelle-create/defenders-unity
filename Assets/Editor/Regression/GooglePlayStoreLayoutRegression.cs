// =============================================================================
// GooglePlayStoreLayoutRegression [google-play-store-layout] - the Night Market's
// Play skin can never again stack its product rows on top of one another (WO-1743).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// WHAT BROKE (owner device capture, Google Play build 2026.09.09.362625 -
// logs/device/store-listing.png, 2670x1200 Seeker, landscape):
//
//   * Every product row's decorative frame was drawn OVERLAPPING the next. Captions
//     collided with the frames above and below; the list read as one continuous
//     smear of gold bezel.
//   * "Secure purchases through Google Play" was half-covered by the first row.
//   * "CLOSE" - modal chrome, outside the body - sat on top of a product row.
//   * "RESTORE PURCHASES" and "REQUEST ACCOUNT AND DATA DELETION" appeared
//     INTERLEAVED between product rows, seven and eight rows down.
//
// ⚠ THE "UNAVAILABLE" TEXT ON EVERY ROW IS A DIFFERENT DEFECT and is NOT this
// suite's business: that is Play Console product configuration. This panel would
// have shipped illegible with perfectly working products.
//
// THE CAUSE, read at source rather than inferred:
//
//   GooglePlayStorefront.Build HAND-PLACED its rows by fraction anchors -
//       float top = .90f, height = .095f;  y1 = top - i * height;  y0 = y1 - .082f;
//
//   1  THE TOUCH FLOOR ATE THE GAP. ElarionUiKit.BuildModalCanvas scales to a
//      1080x1920 reference with match 0.5, so at 2670x1200 the canvas resolves to
//      ~2148x965 REFERENCE px and this modal's body measures ~542 ref px. The pitch
//      was therefore .095*542 = ~51 px carrying ~44 px rows. Every kit button is
//      armed with ClampMinTouch, and UiKitMinTouchGuard.LateUpdate
//      (ElarionUiKit.cs:1179-1184) grows anything under MinTouchPx=112
//      SYMMETRICALLY ABOUT ITS CENTRE - so each row grew to 112 px and spilled
//      ~34 px into the row above AND the row below.
//
//      The kit had already written this failure down, at
//      ElarionUiKitObsidian.cs:574-580: "The root cause of stacked/overlapping menu
//      buttons (pause, help, ...) was every menu HAND-PLACING fraction-anchored
//      buttons: the MinTouchPx(112) floor grows each button, and on a short modal
//      body those grown rects overlap because the fraction slots are smaller than
//      112px." The storefront was still doing precisely that.
//
//   2  THE COLUMN RAN OFF THE BOTTOM. packs.json ships 18 storeVisible packs, so the
//      loop reached y1 = .90 - 17*.095 = -0.715 - rows 10..18 anchored BELOW the body
//      and passed through the fixed Restore (.20-.285) and Deletion (.105-.19) bands.
//      Index 6 lands at .33 and index 7 at .235, which is exactly where RESTORE
//      PURCHASES appears in the capture. That interleave is measured proof of (2),
//      not a theory that happens to fit.
//
// THE FIX IT PINS: the list is the kit's FIT-OR-SCROLL zone (ElarionUiKit.
// MakeScrollZone, §1.14) with every row sized by an explicit sizeDelta at the touch
// floor. 18 packs + Restore + Deletion = 20 controls; at 112 px with an 8 px gap that
// is 2392 ref px of content in a ~542 px body, so no legible, legally-tappable fixed
// layout exists and a scroll view is the only correct answer.
//
// THE CASES:
//   1 [source-law]    Build calls MakeScrollZone, sizes rows by sizeDelta, and no
//                     longer carries the `i * height` fraction loop. The band
//                     constants exist and are DISJOINT (list < subtitle, hint < list,
//                     status < hint), so no band can grow into another.
//   2 [measured-list] The real kit SCROLL COLUMN, BUILT AND MEASURED at three aspects
//                     including the owner's 2670x1200: every row clears the touch
//                     floor, NO two rows intersect, and the list well does not
//                     intersect the subtitle / hint / status bands. Carries a RED
//                     FIXTURE that rebuilds the OLD fraction loop and FAILS the suite
//                     if it does NOT overlap - so the check can never silently stop
//                     measuring the mechanism.
//
// ⚠ WHY A GEOMETRY CHECK AND NOT ONLY A SOURCE LINT: the defect is a RUNTIME rect
// growth. A lint proves intent; this proves the mechanism. But note the converse
// limit, recorded so nobody over-claims it - UiKitMinTouchGuard.LateUpdate NEVER RUNS
// in an edit-mode batchmode call (ElarionUiKit.cs:1092-1094), so this suite asserts
// the AUTHORED heights and must never be read as proof that nothing grew on a device.
//
// ⛔ DeNelle.EditorRegression cannot reference DeNelle.GooglePlay - that assembly
// carries defineConstraints ["GOOGLE_PLAY"] and is not compiled in a normal editor
// gate. So case 1 reads the panel as SOURCE TEXT (the same way
// RealmStoreSingleRegistrarRegression reaches it) and case 2 rebuilds the shape from
// kit calls only.
//
// Standalone: run-unity-method DeNelle.Editor.Regression.GooglePlayStoreLayoutRegression.RunAll
// Registered in DataRegression.RunAll as the "google-play-store-layout suite".
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class GooglePlayStoreLayoutRegression
    {
        private const string PanelSrc = "Assets/_Modules/GooglePlay/GooglePlayStorefront.cs";
        private const string PacksJson = "Assets/Resources/Data/Canonical/packs.json";

        /// <summary>The kit touch floor (ElarionUiKit.MinTouchPx), restated as a BUDGET and
        /// asserted against the live constant below so it cannot drift.</summary>
        private const float TouchFloorPx = 112f;

        /// <summary>ElarionUiKit.BuildModalCanvas's scaler contract: 1080x1920 reference,
        /// ScaleWithScreenSize, match 0.5 (geometric mean). Read at source 2026-09-15,
        /// ElarionUiKit.cs:107-111.</summary>
        private const float RefW = 1080f, RefH = 1920f;

        /// <summary>BuildObsidianModal's own anchors in GooglePlayStorefront.Build. The body sits
        /// inside this; using the modal band OVERSTATES the body, which makes the overlap
        /// assertions harder to pass, never easier.</summary>
        private const float ModalXMin = .08f, ModalYMin = .04f, ModalXMax = .92f, ModalYMax = .96f;

        /// <summary>The owner's Seeker first. The other two are the common landscape frames.</summary>
        private static readonly int[,] Aspects = { { 2670, 1200 }, { 2340, 1080 }, { 1920, 1080 } };

        /// <summary>Rows the suite builds when packs.json cannot be counted: 18 storeVisible packs
        /// plus Restore plus Deletion, as measured 2026-09-15.</summary>
        private const int FallbackRowCount = 20;

        [UnityEditor.MenuItem("Tools/Regression/UI/Google Play Store Layout")]
        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? "GOOGLE_PLAY_STORE_LAYOUT_OK " : "GOOGLE_PLAY_STORE_LAYOUT_FAIL ") + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                // ⛔ STRIP THE COMMENTS FIRST, AND THIS IS NOT COSMETIC. The fix in
                //    GooglePlayStorefront.Build deliberately QUOTES the defective loop it replaced
                //    ("float top = .90f ... y1 = top - i * height") so the next seat can see what
                //    went wrong. Linting the raw file would match that comment and fail a correct
                //    panel; it would equally PASS a panel that only mentions MakeScrollZone in
                //    prose. Both directions are wrong, so every law below reads CODE ONLY.
                //    (Same helper, same reason, as RealmStoreSingleRegistrarRegression.)
                string raw = ReadSource(PanelSrc, failures);
                string src = raw != null ? StripComments(raw) : null;
                if (src != null) CaseSourceLaw(src, failures, notes);
                CaseMeasuredList(src, failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add("google-play-store-layout threw: " + ex.GetType().Name + " " + ex.Message);
            }

            reason = failures.Count == 0
                ? "2 cases - " + string.Join("; ", notes)
                : failures.Count + " finding(s): " + string.Join(" | ", failures);
            return failures.Count == 0;
        }

        // ── 1. [source-law] ──────────────────────────────────────────────────────
        //  The shape, asserted in the file itself. This half is cheap and catches a
        //  revert; case 2 is the half that proves the geometry.

        private static void CaseSourceLaw(string src, List<string> failures, List<string> notes)
        {
            const string Tag = "[source-law]";

            if (src.IndexOf("ElarionUiKit.MakeScrollZone", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " GooglePlayStorefront.Build no longer calls ElarionUiKit."
                    + "MakeScrollZone. 18 storeVisible packs + Restore + Deletion at the "
                    + TouchFloorPx + "px touch floor need ~2392 reference px and the modal body is "
                    + "~542 - a fixed layout CANNOT hold them, and the fraction loop that tried is "
                    + "what shipped illegible on the owner's Seeker.");

            if (src.IndexOf("sizeDelta", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " no row sizeDelta in " + PanelSrc + ". MakeScrollZone's content "
                    + "column runs childControlHeight:false and its VerticalLayoutGroup resets each "
                    + "child's anchors to (0,1), so a row without an explicit sizeDelta collapses to "
                    + "ZERO height and the whole list renders blank.");

            // ⛔ THE DEFECT'S OWN SIGNATURE. `top - i * height` with a fraction pitch is the
            //    hand-placed column; its return is the bug's return.
            if (Regex.IsMatch(src, @"-\s*i\s*\*\s*height") || Regex.IsMatch(src, @"y1\s*-\s*\.0\d+f"))
                failures.Add(Tag + " the fraction-anchored row loop is back in " + PanelSrc
                    + " (`- i * height`). ElarionUiKitObsidian.cs:574-580 names this exact pattern as "
                    + "the root cause of stacked/overlapping menu buttons: ClampMinTouch grows each "
                    + "row to " + TouchFloorPx + "px about its CENTRE and it spills into both neighbours.");

            // The row height must BE the touch floor, taken from the kit constant - a hand-typed
            // number is how a row drifts back under the floor and re-arms the clamp.
            if (src.IndexOf("RowHeightPx = ElarionUiKit.MinTouchPx", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " RowHeightPx is no longer ElarionUiKit.MinTouchPx. A literal here "
                    + "drifts; the floor must be the kit's own constant so UiKitMinTouchGuard has "
                    + "nothing left to grow.");

            if (Math.Abs(ElarionUiKit.MinTouchPx - TouchFloorPx) > 0.01f)
                failures.Add(Tag + " ElarionUiKit.MinTouchPx is " + Px(ElarionUiKit.MinTouchPx)
                    + " but this suite budgets " + Px(TouchFloorPx) + " - the budget has drifted from the kit.");

            // The bands must be DISJOINT. This is the half that keeps CLOSE, the subtitle and the
            // status line out of the list, and it is decidable from four constants.
            float subY0 = ConstFloat(src, "SubtitleY0", failures);
            float listY0 = ConstFloat(src, "ListZoneY0", failures);
            float listY1 = ConstFloat(src, "ListZoneY1", failures);
            float hintY0 = ConstFloat(src, "HintY0", failures);
            float hintY1 = ConstFloat(src, "HintY1", failures);
            float statY1 = ConstFloat(src, "StatusY1", failures);
            if (float.IsNaN(subY0) || float.IsNaN(listY0) || float.IsNaN(listY1)
                || float.IsNaN(hintY0) || float.IsNaN(hintY1) || float.IsNaN(statY1)) return;

            if (listY1 > subY0 + 0.0001f)
                failures.Add(Tag + " the list well ends at " + F(listY1) + " but the subtitle starts at "
                    + F(subY0) + " - they overlap. The half-covered \"Secure purchases through Google "
                    + "Play\" at the top of the device capture is exactly this.");
            if (hintY1 > listY0 + 0.0001f)
                failures.Add(Tag + " the scroll hint ends at " + F(hintY1) + " but the list well starts at "
                    + F(listY0) + " - they overlap.");
            if (statY1 > hintY0 + 0.0001f)
                failures.Add(Tag + " the status line ends at " + F(statY1) + " but the hint starts at "
                    + F(hintY0) + " - they overlap.");

            notes.Add("[source-law] scroll zone + sizeDelta rows at the kit floor; bands disjoint "
                + "(status<" + F(hintY0) + " hint<" + F(listY0) + " list<" + F(subY0) + ")");
        }

        // ── 2. [measured-list] ───────────────────────────────────────────────────
        //  Build the real kit scroll column and MEASURE it. No GooglePlay dependency:
        //  the shape is MakeScrollZone + sizeDelta rows, all kit behaviour.

        private static void CaseMeasuredList(string src, List<string> failures, List<string> notes)
        {
            int rowCount = CountStoreRows();
            for (int i = 0; i < Aspects.GetLength(0); i++)
                MeasureAt(Aspects[i, 0], Aspects[i, 1], rowCount, src, failures, notes);
            notes.Add("[measured-list] " + rowCount + " rows measured at "
                + Aspects.GetLength(0) + " aspects, no intersections");
        }

        private static void MeasureAt(int w, int h, int rowCount, string src,
            List<string> failures, List<string> notes)
        {
            string tag = "[measured-list:" + w + "x" + h + "]";
            GameObject canvasGo = null;
            try
            {
                float scale = Mathf.Pow(w / RefW, 0.5f) * Mathf.Pow(h / RefH, 0.5f);
                if (scale <= 0f) { failures.Add(tag + " degenerate scaler."); return; }

                canvasGo = NewCanvas("gpsl-" + w + "x" + h, w / scale, h / scale);
                var rootRt = (RectTransform)canvasGo.transform;
                var body = Region(rootRt, "Body",
                    new Vector2(ModalXMin, ModalYMin), new Vector2(ModalXMax, ModalYMax));

                float listY0 = SafeConst(src, "ListZoneY0", .175f);
                float listY1 = SafeConst(src, "ListZoneY1", .92f);
                float subY0 = SafeConst(src, "SubtitleY0", .93f);
                float subY1 = SafeConst(src, "SubtitleY1", 1.00f);
                float hintY0 = SafeConst(src, "HintY0", .12f);
                float hintY1 = SafeConst(src, "HintY1", .17f);
                float statY0 = SafeConst(src, "StatusY0", .01f);
                float statY1 = SafeConst(src, "StatusY1", .11f);
                float gapPx = SafeConst(src, "RowGapPx", 8f);

                var listZone = Region(body, "StoreListZone", new Vector2(.02f, listY0), new Vector2(.98f, listY1));
                var subtitle = Region(body, "Subtitle", new Vector2(.02f, subY0), new Vector2(.98f, subY1));
                var hint = Region(body, "Hint", new Vector2(.03f, hintY0), new Vector2(.97f, hintY1));
                var status = Region(body, "Status", new Vector2(.03f, statY0), new Vector2(.97f, statY1));
                Settle(rootRt);

                var zone = ElarionUiKit.MakeScrollZone(listZone, gapPx, 6);
                if (zone == null || zone.content == null || zone.viewport == null)
                {
                    failures.Add(tag + " MakeScrollZone returned no content column - nothing to measure.");
                    return;
                }

                // The ONE clip that keeps a row off the chrome however long the catalog grows.
                if (zone.viewport.GetComponent<RectMask2D>() == null)
                    failures.Add(tag + " the scroll viewport carries no RectMask2D - overflowing rows "
                        + "can paint over the subtitle, the status line and the chrome CLOSE, which is "
                        + "what the device capture shows.");

                // ⚠ THE ROWS ARE PLAIN RECTS, NOT ElarionUiKit.BuildObsidianButton, AND THAT IS
                //    DELIBERATE. Checked 2026-09-15 across Assets/Editor/Regression/: every suite
                //    that mentions BuildObsidianButton or AddColumnButton does so as a SOURCE LINT
                //    (HeroSelectCarouselRegression, HelpMenuEntryRegression, ...). NOT ONE builds a
                //    kit obsidian button live in an edit-mode batchmode call. That path walks
                //    InstantiateBlinkPrefab -> RpgUiCatalog.Get -> StyleButtonColors ->
                //    ClampMinTouch -> MedievalUiSkin.ApplyButton, and any one of those throwing
                //    headlessly would turn this suite red FOR A REASON THAT IS NOT THE LAYOUT -
                //    a gate failure that teaches nothing and gets the suite switched off.
                //
                //    The geometry claim does not need the art. What is being measured is the
                //    kit COLUMN's stacking rule: MakeScrollZone runs childControlHeight:false, so a
                //    child's height IS its sizeDelta, and the VerticalLayoutGroup then lays those
                //    heights out with `spacing` between them. A plain RectTransform exercises that
                //    rule identically to a button. What this substitution does NOT cover is the
                //    kit's prefab MODE 1, where BuildObsidianButton can return a NESTED Button -
                //    sizing btn.transform instead of the column's direct child. That half is
                //    covered by case 1's source law and by the comment on
                //    GooglePlayStorefront.AddListRow, and it is named here rather than left implied.
                var rows = new List<RectTransform>();
                for (int i = 0; i < rowCount; i++)
                {
                    var r = Region(zone.content, "Row" + i, Vector2.zero, Vector2.one);
                    r.sizeDelta = new Vector2(0f, TouchFloorPx);
                    rows.Add(r);
                }
                Settle(rootRt);
                Settle(rootRt);

                if (rows.Count != rowCount)
                { failures.Add(tag + " built " + rows.Count + " of " + rowCount + " rows."); return; }

                // (a) every row clears the touch floor, so the clamp has nothing to grow.
                for (int i = 0; i < rows.Count; i++)
                {
                    float rh = rows[i].rect.height;
                    if (rh < TouchFloorPx - 1f)
                        failures.Add(tag + " row " + i + " measured " + Px(rh) + ", under the kit floor "
                            + Px(TouchFloorPx) + " - UiKitMinTouchGuard will grow it about its CENTRE at "
                            + "runtime and spill it into both neighbours. That growth is the defect.");
                }

                // (b) NO TWO ROWS INTERSECT. The whole point of the ticket, as arithmetic.
                for (int i = 0; i < rows.Count; i++)
                    for (int j = i + 1; j < rows.Count; j++)
                        if (LayoutOracle.Overlaps(Box(rows[i]), Box(rows[j]), 1f, out float ow, out float oh))
                        {
                            failures.Add(tag + " rows " + i + " and " + j + " INTERSECT by "
                                + Px(ow) + " x " + Px(oh) + " - overlapping product frames are the "
                                + "defect WO-1743 fixed.");
                            i = rows.Count; break;   // one finding is the finding
                        }

                // (c) the well cannot reach the other bands.
                RequireClear(failures, tag, listZone, "the list well", subtitle, "the subtitle");
                RequireClear(failures, tag, listZone, "the list well", hint, "the scroll hint");
                RequireClear(failures, tag, listZone, "the list well", status, "the status line");
                RequireClear(failures, tag, hint, "the scroll hint", status, "the status line");

                // ── ⭐ THE RED FIXTURE. The defect, rebuilt the OLD way, MEASURED. ────────
                //    If this does NOT overlap then the pitch arithmetic above has changed
                //    underneath the suite and every assertion in (b) has quietly stopped
                //    measuring the mechanism. Same negative-fixture discipline as
                //    DefenseReportLayoutRegression's LegacyBand.
                var legacyHost = Region(body, "LegacyFixture", Vector2.zero, Vector2.one);
                var legacy = new List<RectTransform>();
                const float LegacyTop = .90f, LegacyPitch = .095f, LegacyHeight = .082f;
                for (int i = 0; i < 4; i++)
                {
                    float y1 = LegacyTop - i * LegacyPitch, y0 = y1 - LegacyHeight;
                    var r = Region(legacyHost, "Legacy" + i, new Vector2(.03f, y0), new Vector2(.97f, y1));
                    // The clamp, applied by hand - LateUpdate never runs in edit-mode batchmode
                    // (ElarionUiKit.cs:1092-1094), so the growth is reproduced arithmetically.
                    legacy.Add(r);
                }
                Settle(rootRt);
                bool legacyOverlaps = false;
                for (int i = 0; i + 1 < legacy.Count && !legacyOverlaps; i++)
                {
                    Rect a = Grow(Box(legacy[i]), TouchFloorPx);
                    Rect b = Grow(Box(legacy[i + 1]), TouchFloorPx);
                    legacyOverlaps = LayoutOracle.Overlaps(a, b, 1f, out _, out _);
                }
                if (!legacyOverlaps)
                    failures.Add(tag + " THE RED FIXTURE DID NOT OVERLAP. The old fraction column "
                        + "(top .90, pitch .095, height .082) grown to the " + Px(TouchFloorPx)
                        + " floor should collide at this aspect. It did not, so this suite is no "
                        + "longer measuring the WO-1743 mechanism and its green means nothing.");
                UnityEngine.Object.DestroyImmediate(legacyHost.gameObject);

                float wellH = zone.viewport.rect.height;
                float pitch = TouchFloorPx + gapPx;
                notes.Add(tag.Trim('[', ']') + " well " + Px(wellH) + ", "
                    + (wellH / Mathf.Max(1f, pitch)).ToString("0.0", CultureInfo.InvariantCulture)
                    + " rows visible of " + rowCount);
            }
            catch (Exception ex)
            {
                failures.Add(tag + " threw: " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
            }
        }

        private static void RequireClear(List<string> failures, string tag,
            RectTransform a, string aName, RectTransform b, string bName)
        {
            if (a == null || b == null) return;
            if (LayoutOracle.Overlaps(Box(a), Box(b), 1f, out float ow, out float oh))
                failures.Add(tag + " " + aName + " intersects " + bName + " by "
                    + Px(ow) + " x " + Px(oh) + ".");
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>storeVisible packs + Restore + Deletion. Counted from the shipped catalog so
        /// the suite tracks the real list rather than a number typed here once.</summary>
        private static int CountStoreRows()
        {
            try
            {
                if (!File.Exists(PacksJson)) return FallbackRowCount;
                string json = File.ReadAllText(PacksJson);
                // Every pack row carries "sku"; only a HIDDEN one carries "storeVisible": false.
                int packs = Regex.Matches(json, "\"sku\"\\s*:").Count;
                int hidden = Regex.Matches(json, "\"storeVisible\"\\s*:\\s*false").Count;
                int visible = packs - hidden;
                return visible > 0 ? visible + 2 : FallbackRowCount;
            }
            catch { return FallbackRowCount; }
        }

        private static GameObject NewCanvas(string name, float w, float h)
        {
            // WORLD-SPACE and hand-sized: a ScreenSpace canvas in an edit-mode batchmode call
            // reports the editor's own 640x480 (the WO-1060 F8-5 root cause).
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
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
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        private static Rect Box(RectTransform rt)
        {
            if (rt == null) return new Rect();
            rt.GetWorldCorners(_corners);
            float x0 = Mathf.Min(_corners[0].x, _corners[2].x), x1 = Mathf.Max(_corners[0].x, _corners[2].x);
            float y0 = Mathf.Min(_corners[0].y, _corners[2].y), y1 = Mathf.Max(_corners[0].y, _corners[2].y);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>Reproduce UiKitMinTouchGuard's growth: symmetric about the rect's CENTRE, up to
        /// the floor. Arithmetic, because LateUpdate never fires in an edit-mode batchmode call.</summary>
        private static Rect Grow(Rect r, float floor)
        {
            if (r.height >= floor) return r;
            float half = (floor - r.height) * 0.5f;
            return new Rect(r.xMin, r.yMin - half, r.width, floor);
        }

        /// <summary>Code only: line and block comments removed, string and char literals preserved
        /// intact. Ported verbatim in behaviour from RealmStoreSingleRegistrarRegression.StripComments
        /// so both suites judge this same file by one rule.</summary>
        private static string StripComments(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char n = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '\'')
                {
                    sb.Append(c); i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        char s = source[i];
                        if (s == '\\' && i + 1 < source.Length) { sb.Append(s).Append(source[i + 1]); i += 2; continue; }
                        sb.Append(s); i++;
                        if (s == '\'') break;
                    }
                    continue;
                }
                if (c == '"')
                {
                    bool verbatim = i > 0 && source[i - 1] == '@';
                    sb.Append(c); i++;
                    while (i < source.Length)
                    {
                        char s = source[i];
                        if (!verbatim && s == '\\' && i + 1 < source.Length) { sb.Append(s).Append(source[i + 1]); i += 2; continue; }
                        if (s == '"' && verbatim && i + 1 < source.Length && source[i + 1] == '"') { sb.Append("\"\""); i += 2; continue; }
                        sb.Append(s); i++;
                        if (s == '"') break;
                        if (!verbatim && s == '\n') break;
                    }
                    continue;
                }
                if (c == '/' && n == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    { if (source[i] == '\n') sb.Append('\n'); i++; }
                    i += 2;
                    continue;
                }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }

        private static string ReadSource(string path, List<string> failures)
        {
            try
            {
                if (!File.Exists(path)) { failures.Add("source missing: " + path); return null; }
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                failures.Add("could not read " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Reads a `const float NAME = 1.23f;` out of the source. A missing constant is a
        /// FAILURE, never a note - a quiet skip lands green.</summary>
        private static float ConstFloat(string src, string name, List<string> failures)
        {
            var m = Regex.Match(src, @"\b" + Regex.Escape(name) + @"\s*=\s*(-?[0-9]*\.?[0-9]+)f?\s*[;,]");
            if (!m.Success)
            {
                failures.Add("[source-law] constant " + name + " does not exist in " + PanelSrc
                    + " - the band budget cannot be measured.");
                return float.NaN;
            }
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>Same read, but for the MEASURED case, which must still build something when the
        /// source could not be read at all (case 1 has already recorded that failure).</summary>
        private static float SafeConst(string src, string name, float fallback)
        {
            if (src == null) return fallback;
            var m = Regex.Match(src, @"\b" + Regex.Escape(name) + @"\s*=\s*(-?[0-9]*\.?[0-9]+)f?\s*[;,]");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }

        private static string Px(float v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "px";
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
