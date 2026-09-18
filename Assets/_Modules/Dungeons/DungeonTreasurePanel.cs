// =============================================================================
// DungeonTreasurePanel (WO-850) - the confirm/reward beat for the deepest cache.
// -----------------------------------------------------------------------------
// Owner ruling: "prompt then confirm" - opening the cache presents what is inside
// and the player TAKES it. The grant is the CALLER's callback; this panel only
// decides WHEN it fires, and never reports a reward it did not apply (the WO-844
// potion lesson). Because the shared Close is retired and the scrim has no
// onClose, this modal has NO dismiss - so every way it can end (the Take tap, or
// PanelManager swapping it out for another screen) pays the player, and the
// pending callback is consumed before teardown so it can never pay twice.
//
// Show() returns a BOOL: true only when the modal is really on screen and has
// therefore taken ownership of the grant. It returns false when a duplicate Show
// is refused, or when PanelManager REJECTS the open (the WO-437 battle-lock, which
// tears the panel back down). The caller must grant directly on false - otherwise
// a rejected open would consume the cache and pay nothing.
//
// KIT LAWS OBSERVED:
//  - Built through ElarionUiKit only (UiObsidianConformanceRegression HardFailOnNew
//    rejects a new file that hand-rolls uGUI).
//  - ONE exit. The shared Close is retired and "Take" is the single CTA - the same
//    owner F8 (seq 628) that removed Continue-vs-Close from the Echo emergence
//    beat: two exits on a linear beat read as one choice offered twice.
//  - ASCII-only source (DungeonTreasureRegression case 5 fails the first non-ASCII
//    character - TMP renders it as tofu on device).
//  - Meaning never by colour: every material line prints "Name xN" as TEXT, so the
//    payout is readable red/green colourblind, and the TITLE separates from the
//    SUBTITLE by SIZE + WEIGHT (FontTitle bold vs FontBody regular), never by hue.
//
// =============================================================================
// WO-1228 (2026-08-26) - FIVE EXCLUSIVE BANDS REPLACE THE PIXEL FLOW.
// -----------------------------------------------------------------------------
// The owner's device capture (Seeker 2026.08.26.342290, 2670x1200) showed THREE
// collisions at once: "TREASURE FOUND" drawn on top of "The cache holds:", the
// fifth cache line clipped, and "Take" painted over the first-clear sentence
// ("First clear -- [Take] membered."). All three came from ONE cause and it is
// NOT the shared close-band reservation:
//
//   * The WO-1041 layout hung every element from chrome.content's TOP EDGE in
//     PIXELS (StackTopPx 24, then HeadingPx/LinePx bands). chrome.content is the
//     FULL panel rect 0..1, and the kit seats the gold title inside the frame's
//     header ZONE (FrameCore header = 0.900..0.972 of the panel). At 2670x1200 the
//     panel was 0.24..0.78 of screen = 648 screen px, so the title band occupied
//     roughly 18..65 px from the panel top while the subtitle was pinned at
//     24..90 px. That 24..65 px overlap IS collision 1, and no reservation is
//     involved: the reservation only raises z.body / z.footer / z.bodyLeft /
//     z.bodyRight, and this panel consumed NONE of those zones.
//   * Downstream, the same flow put the payout block at 104..404 local units and
//     the first-clear line at 418..484, while the CTA's authored band (0.05..0.245
//     of content) topped out at 393 - so the tail of the list and the whole footer
//     sentence sat UNDER the button. Collisions 2 and 3, same cause.
//
// The fix is to stop flowing and start BANDING: five exclusive rects, authored as
// fractions of the modal, none of which may intersect (pinned by
// DungeonTreasureRegression cases 8-10). A list that outgrows its band SCROLLS
// inside the band - the modal never grows, and "Take" never moves.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Village.Items;   // MaterialCatalog - display names only, never inventory

namespace DeNelle.Dungeons
{
    /// <summary>The small "TREASURE FOUND" modal shown when the deepest cache is opened.</summary>
    public static class DungeonTreasurePanel
    {
        private const string Sys = "DungeonTreasure";
        private const string PanelName = "DungeonTreasure";

        /// <summary>
        /// THE authored geometry, in ONE place, so the panel and its regression read the same
        /// numbers (a second copy is how the numbers drift apart - CLAUDE.md section 2/5).
        /// Every band is (xMin, yMin, xMax, yMax) as FRACTIONS OF THE MODAL RECT, y bottom-to-top,
        /// matching the approved mockup WorkOrders/WORK_ORDER_1228_mockup_2670x1200.png.
        /// </summary>
        public static class Layout
        {
            /// <summary>Modal footprint (fractions of the SCREEN). The mockup authored
            /// 0.167..0.833; the plate is 0.120..0.880 here so that SIX kit-font rows and a
            /// MinTouchPx-tall CTA both fit WITHOUT shrinking type (WO-1040 section 4 forbids
            /// shrinking the font to buy room). Nothing moved relative to the design - the whole
            /// plate is ~6% taller.</summary>
            public static readonly Vector2 ModalMin = new Vector2(0.210f, 0.120f);
            public static readonly Vector2 ModalMax = new Vector2(0.790f, 0.880f);

            /// <summary>Band 1 - the gold title. The KIT owns this rect: BuildObsidianPanel seats
            /// chrome.title inside the frame's header zone, so this constant MIRRORS FrameCore's
            /// header zone in ElarionUiKit.ZonesFor. DungeonTreasureRegression case 8 re-reads the
            /// kit source and FAILS if the two ever drift.</summary>
            public static readonly Vector4 TitleBand = new Vector4(0.24f, 0.900f, 0.88f, 0.972f);

            /// <summary>Band 2 - "The cache holds:". Its OWN band, clear of the title above.</summary>
            public static readonly Vector4 SubtitleBand = new Vector4(0.10f, 0.813f, 0.90f, 0.875f);

            /// <summary>Band 3 - the loot well (scroll viewport + overflow hint strip).</summary>
            public static readonly Vector4 WellBand = new Vector4(0.039f, 0.322f, 0.961f, 0.788f);

            /// <summary>Band 4 - the first-clear sentence, never overlaid.</summary>
            public static readonly Vector4 NoteBand = new Vector4(0.10f, 0.238f, 0.90f, 0.300f);

            /// <summary>Band 5 - the single exit. Authored 0.175 of the modal tall so the
            /// MinTouchPx=112 floor is met BY CONSTRUCTION at the reference device and
            /// ClampMinTouch never has to grow it into band 4 (the hero-select failure).</summary>
            public static readonly Vector4 CtaBand = new Vector4(0.339f, 0.045f, 0.661f, 0.220f);

            /// <summary>SIX LINES THEN SCROLL (owner ruling 2026-08-26). WO-1230 adopts the same
            /// affordance for the Army roster - the two list surfaces must not diverge.</summary>
            public const int VisibleRows = 6;

            /// <summary>Bottom slice of the well reserved for the "+ N more (scroll)" hint. It is
            /// reserved WHETHER OR NOT it is populated, so the row pitch (and therefore every band
            /// in the table) is identical at 1 line and at 100.</summary>
            public const float HintStripFraction = 0.14f;

            /// <summary>Kit scroll-zone chrome, in canvas units.</summary>
            public const float ScrollSpacingPx = 4f;
            public const int ScrollPaddingPx = 6;

            /// <summary>Row floor: FontFloor(30) x 1.25 line height. A well too short to seat six
            /// rows at this pitch is a LAYOUT bug, and RowHeightPx says so out loud rather than
            /// letting TMP cull the glyphs (the section 12 "no silent clipping" law).</summary>
            public const float MinRowPx = 38f;

            /// <summary>Modal height in POST-SCALE canvas units.</summary>
            public static float ModalHeightPx(float canvasHeightPx)
            {
                return (ModalMax.y - ModalMin.y) * canvasHeightPx;
            }

            /// <summary>Loot-well height in canvas units.</summary>
            public static float WellHeightPx(float canvasHeightPx)
            {
                return (WellBand.w - WellBand.y) * ModalHeightPx(canvasHeightPx);
            }

            /// <summary>Scrolling viewport height (the well minus the reserved hint strip).</summary>
            public static float ViewportHeightPx(float canvasHeightPx)
            {
                return WellHeightPx(canvasHeightPx) * (1f - HintStripFraction);
            }

            /// <summary>Row pitch that seats exactly <see cref="VisibleRows"/> rows in the viewport,
            /// floored at <see cref="MinRowPx"/>.</summary>
            public static float RowHeightPx(float canvasHeightPx)
            {
                float usable = ViewportHeightPx(canvasHeightPx)
                             - 2f * ScrollPaddingPx
                             - (VisibleRows - 1) * ScrollSpacingPx;
                return Mathf.Max(MinRowPx, usable / VisibleRows);
            }

            /// <summary>CTA height in canvas units - compare against ElarionUiKit.MinTouchPx.</summary>
            public static float CtaHeightPx(float canvasHeightPx)
            {
                return (CtaBand.w - CtaBand.y) * ModalHeightPx(canvasHeightPx);
            }

            /// <summary>True when the cache carries more lines than the well shows at once.</summary>
            public static bool Overflows(int lineCount)
            {
                return lineCount > VisibleRows;
            }

            /// <summary>The WO-1230 overflow affordance, WORD FOR WORD - "+ N more (scroll)".
            /// Empty when everything fits.</summary>
            public static string OverflowHint(int lineCount)
            {
                if (!Overflows(lineCount)) return string.Empty;
                return "+ " + (lineCount - VisibleRows) + " more (scroll)";
            }

            /// <summary>The five named bands, in draw order. The regression asserts they are
            /// pairwise NON-intersecting.</summary>
            public static Vector4[] Bands()
            {
                return new[] { TitleBand, SubtitleBand, WellBand, NoteBand, CtaBand };
            }

            /// <summary>Names parallel to <see cref="Bands"/> (for failure messages).</summary>
            public static string[] BandNames()
            {
                return new[] { "title", "subtitle", "well", "note", "cta" };
            }

            /// <summary>Half-open rect intersection on (xMin,yMin,xMax,yMax) fractions.</summary>
            public static bool Intersect(Vector4 a, Vector4 b)
            {
                return a.x < b.z && b.x < a.z && a.y < b.w && b.y < a.w;
            }
        }

        private static GameObject s_canvas;
        private static PanelHandle s_handle;

        // The pending grant, held only while the modal is live. There is NO dismiss on this
        // panel (the shared Close is retired and the scrim has no onClose), so every way the
        // modal can end - the Take tap, or PanelManager swapping us out for another screen -
        // must pay the player. Nulled on teardown so it can never be granted twice.
        private static Action s_onTake;

        /// <summary>True while the reward modal is on screen.</summary>
        public static bool IsOpen => s_canvas != null;

        /// <summary>
        /// Present the reward. <paramref name="onTake"/> runs exactly once, when the modal
        /// ends (the Take tap, or an arbiter-forced close - this panel has no dismiss).
        /// Returns TRUE when the modal is live and has therefore taken ownership of the
        /// grant; FALSE when it refused to open (duplicate Show, unusable chrome, or an
        /// arbiter rejection), in which case the CALLER still owns paying the player.
        /// </summary>
        public static bool Show(IReadOnlyList<(string Id, int Count)> bundle, bool firstClear, Action onTake)
        {
            if (s_canvas != null)
            {
                FlowTrace.Warn(Sys, "reward panel already open - ignoring duplicate Show");
                return false;
            }

            var modal = ElarionUiKit.BuildObsidianModal(
                PanelName, new LocalizedText("dungeon.treasure.title").Resolve(),
                Layout.ModalMin, Layout.ModalMax,
                onClose: null, sortingOrder: 31030,
                frameName: RpgUiCatalog.FrameCore);
            if (modal == null || modal.canvas == null || modal.chrome == null || modal.chrome.content == null)
            {
                FlowTrace.Fail(Sys, "BuildObsidianModal returned no usable chrome - reward panel NOT shown");
                if (modal != null && modal.canvas != null) UnityEngine.Object.Destroy(modal.canvas);
                return false;
            }
            s_canvas = modal.canvas;
            var content = modal.chrome.content.transform;

            // ONE exit: retire the shared Close so Take is the only way out (owner F8 seq 628).
            if (modal.chrome.close != null) modal.chrome.close.gameObject.SetActive(false);

            // Geometry is derived from the POST-SCALE canvas height, never from rect.height:
            // on a canvas's creation frame the CanvasScaler has not applied yet and rect.height
            // returns RAW SCREEN PIXELS (the F8-5 DlgLayout capture, 1351 vs the real 1047).
            float canvasH = ElarionUiKit.PostScaleCanvasHeight(content);

            // -- BAND 2: the subtitle, in its own band under the kit's gold title ---
            // Band 1 (the title) is drawn by the kit into FrameCore's header zone; the two are
            // separated by SIZE + WEIGHT (FontTitle bold vs FontBody regular), so the greyscale
            // read survives - the owner is red/green colourblind and hue may carry nothing.
            var heading = ElarionUiKit.Label(content,
                new LocalizedText("dungeon.treasure.contents_heading").Resolve(),
                Layout.SubtitleBand.y, Layout.SubtitleBand.w,
                ElarionUi.ParchmentDim, ElarionUi.FontBody, TextAlignmentOptions.Center,
                Layout.SubtitleBand.x, Layout.SubtitleBand.z);
            ElarionUiKit.FitSingleLine(heading);

            var lines = new List<string>();
            if (bundle != null)
            {
                foreach (var entry in bundle)
                {
                    if (string.IsNullOrEmpty(entry.Id) || entry.Count <= 0) continue;
                    lines.Add(DisplayNameFor(entry.Id) + " x" + entry.Count);
                }
            }
            if (lines.Count == 0) lines.Add("(empty)");

            // -- BAND 3: the loot well - SIX rows visible, then scroll ---------------
            // FIXED HEIGHT (owner ruling 2026-08-26). The modal never grows with the roll:
            // growth is how this defect class comes back. Beyond six lines the SAME well
            // scrolls (RectMask2D clips inside the well, so nothing can paint over the
            // chrome) and a "+ N more (scroll)" hint - the identical affordance WO-1230 uses
            // for the Army roster - says so in words, never by a cut-off glyph.
            var well = ElarionUiKit.AddImage(content, "TreasureWell",
                new Vector2(Layout.WellBand.x, Layout.WellBand.y),
                new Vector2(Layout.WellBand.z, Layout.WellBand.w),
                ElarionUiKit.ObsidianFill, rounded: true);

            var viewport = ElarionUiKit.AddImage(well.transform, "WellViewport",
                new Vector2(0f, Layout.HintStripFraction), Vector2.one,
                new Color(0f, 0f, 0f, 0f), rounded: false);

            var scroll = ElarionUiKit.MakeScrollZone(viewport.transform,
                Layout.ScrollSpacingPx, Layout.ScrollPaddingPx);

            float rowPx = Layout.RowHeightPx(canvasH);
            for (int i = 0; i < lines.Count; i++)
            {
                // Rows are sized by EXPLICIT sizeDelta: MakeScrollZone's column runs
                // childControlHeight=false precisely because kit rows carry no ILayoutElement.
                var row = ElarionUiKit.Label(scroll.content, lines[i], 0f, 1f,
                    ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.Center,
                    0f, 1f, bold: true);
                row.rectTransform.sizeDelta = new Vector2(0f, rowPx);
                ElarionUiKit.FitSingleLine(row);
            }

            // The hint strip is RESERVED whether or not it is populated, so the row pitch - and
            // with it every band in the table - is identical at one line and at a hundred.
            var hint = ElarionUiKit.Label(well.transform, Layout.OverflowHint(lines.Count),
                0f, Layout.HintStripFraction,
                ElarionUi.ParchmentDim, ElarionUi.FontMicro, TextAlignmentOptions.Center,
                0.04f, 0.96f);
            ElarionUiKit.FitSingleLine(hint);

            // -- BAND 4: the first-clear sentence, never overlaid -------------------
            if (firstClear)
            {
                var unlock = ElarionUiKit.Label(content,
                    new LocalizedText("dungeon.treasure.first_clear_note").Resolve(),
                    Layout.NoteBand.y, Layout.NoteBand.w,
                    ElarionUi.Gilt, ElarionUi.FontBody, TextAlignmentOptions.Center,
                    Layout.NoteBand.x, Layout.NoteBand.z);
                ElarionUiKit.FitSingleLine(unlock);
            }

            // -- BAND 5: the ONE CTA, in a band no other element may enter ----------
            // WO-1857: the ONE CTA's caption resolves from dungeon.treasure.take.
            // NOTE: DungeonTreasureRegression's [panel] check regexes this file's SOURCE for the
            // literal "Take" - that needle must move to the key (sidecar-reported, not edited here).
            ElarionUiKit.Button(content, new LocalizedText("dungeon.treasure.take").Resolve(),
                ElarionUiKit.ButtonKind.Confirm,
                new Vector2(Layout.CtaBand.x, Layout.CtaBand.y),
                new Vector2(Layout.CtaBand.z, Layout.CtaBand.w),
                CloseAndGrant);

            WarnIfBandsCannotSeatContent(canvasH, rowPx, lines.Count);

            // ONE handle for the panel's lifetime (PanelHandle's documented contract).
            // The pending grant is armed only AFTER the arbiter accepts: NotifyOpened can
            // REJECT (WO-437 battle-lock) and invokes the handle's Close on its way out, so
            // arming first would pay the reward inside the rejection AND leave the caller
            // paying it again on our false.
            s_onTake = null;
            if (s_handle == null) s_handle = PanelManager.Register(PanelName, CloseAndGrant, () => IsOpen);
            if (!PanelManager.NotifyOpened(s_handle))
            {
                FlowTrace.Warn(Sys, "PanelManager rejected the reward panel (battle-lock) - caller must grant directly");
                Teardown();
                return false;
            }
            s_onTake = onTake;

            FlowTrace.Step(Sys, string.Format(
                "reward panel opened: {0} line(s), firstClear={1}, canvasH={2:0} modalH={3:0} " +
                "wellH={4:0} rowPx={5:0.0} visible={6} scrolls={7} ctaH={8:0.0} (minTouch={9:0})",
                lines.Count, firstClear, canvasH, Layout.ModalHeightPx(canvasH),
                Layout.WellHeightPx(canvasH), rowPx, Layout.VisibleRows,
                Layout.Overflows(lines.Count), Layout.CtaHeightPx(canvasH), ElarionUiKit.MinTouchPx));
            return true;
        }

        /// <summary>The ONE way this modal ends: tear it down, then pay the pending grant.
        /// Wired to both the Take tap and the arbiter's forced close, because there is no
        /// dismiss on this panel - see <see cref="s_onTake"/>.</summary>
        private static void CloseAndGrant()
        {
            var pending = s_onTake;
            s_onTake = null;                    // consume FIRST - a re-entrant close cannot double-pay
            Teardown();
            // Grant AFTER teardown so a throwing grant can never leave the modal wedged open.
            if (pending != null) Guard.Try(Sys, "treasure take callback", () => pending());
        }

        /// <summary>Destroy the modal and release the arbiter. Never grants; idempotent.</summary>
        private static void Teardown()
        {
            if (s_canvas != null)
            {
                UnityEngine.Object.Destroy(s_canvas);
                s_canvas = null;
            }
            if (s_handle != null) PanelManager.NotifyClosed(s_handle);
        }

        /// <summary>
        /// Player-facing material name. Routes through the shared catalog so the panel and
        /// the inventory always agree; falls back to the raw id (visible, never blank) so a
        /// mis-authored bundle id is OBVIOUS on screen instead of rendering an empty row.
        /// </summary>
        private static string DisplayNameFor(string id)
        {
            string name = null;
            Guard.Try(Sys, "resolve material display name", () =>
            {
                name = MaterialCatalog.DisplayName(id);
            });
            return string.IsNullOrEmpty(name) ? id : name;
        }

        /// <summary>
        /// NO SILENT FAILURES (CLAUDE.md section 12.2). The bands cannot overlap any more - they are
        /// authored disjoint and pinned by regression - but a canvas short enough to squeeze the
        /// well below six legible rows, or a CTA that would need ClampMinTouch's rescue, is still a
        /// LAYOUT bug and must announce itself rather than shipping as a squint.
        /// </summary>
        private static void WarnIfBandsCannotSeatContent(float canvasH, float rowPx, int lineCount)
        {
            if (canvasH <= 1f) return;   // no meaningful canvas yet; the stacking is correct either way

            float seats = (Layout.ViewportHeightPx(canvasH) - 2f * Layout.ScrollPaddingPx
                           - (Layout.VisibleRows - 1) * Layout.ScrollSpacingPx) / Layout.VisibleRows;
            if (seats < Layout.MinRowPx)
            {
                FlowTrace.Warn(Sys, string.Format(
                    "loot well seats only {0:0.0}px per row at canvasH={1:0}, below the {2:0}px legibility " +
                    "floor - showing {3} rows at the floored pitch {4:0.0}px instead ({5} line(s) in the " +
                    "cache), which will overflow the well. The well band needs to grow, NOT the font.",
                    seats, canvasH, Layout.MinRowPx, Layout.VisibleRows, rowPx, lineCount));
            }

            float ctaH = Layout.CtaHeightPx(canvasH);
            if (ctaH < ElarionUiKit.MinTouchPx)
            {
                FlowTrace.Warn(Sys, string.Format(
                    "Take is {0:0.0}px tall, under the {1:0}px touch floor - ClampMinTouch will GROW it, " +
                    "which is how a CTA walks into the band above it (the hero-select failure). Re-author " +
                    "CtaBand rather than relying on the rescue.",
                    ctaH, ElarionUiKit.MinTouchPx));
            }

            if (Layout.Overflows(lineCount))
            {
                FlowTrace.Step(Sys, string.Format(
                    "cache carries {0} line(s); {1} visible, the rest scroll inside the well with a " +
                    "\"{2}\" hint (WO-1230 affordance).",
                    lineCount, Layout.VisibleRows, Layout.OverflowHint(lineCount)));
            }
        }
    }
}
