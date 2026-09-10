// =============================================================================
// NightMarketComposition - the ONE owner of the Night Market's responsive body
// layout (WO-1162 section 1, FIX 1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// WHAT THIS REPLACES, AND WHY IT IS A SEPARATE FILE.
// PackStore used to author its three body columns as three hard literals -
// spotlight 576, commerce 486, gaps 20, edge pad 18 - measured against ONE
// surface (the 2120 x 978 reference canvas a 2340x1080 landscape phone resolves
// to). Those numbers are not wrong; they are UNDERIVED. Nothing in the file said
// what content 576 was protecting, so nothing could tell you what happens at
// 4:3, or after a notch eats 200 reference px off the width. The answer, before
// this file, was: the market column absorbs the whole loss, the two cards inside
// it hit StorePackCard.MinCardWidthPx, the HorizontalLayoutGroup stops shrinking
// them, and the row OVERFLOWS its RectMask2D - i.e. the price column of the
// right-hand card is clipped. That is P0-1/P0-2 ("20 SKR" for a 120 SKR pack)
// arriving by a second route.
//
// ==================  THE RULE THIS FILE IS WRITTEN TO  =======================
// NEVER SHRINK A CARD BELOW ITS READABLE MINIMUM, AND NEVER SHRINK TYPOGRAPHY OR
// A TOUCH TARGET TO KEEP THREE COLUMNS. CHANGE THE COMPOSITION INSTEAD.
// So the breakpoint is not a magic width. It is DERIVED FROM CONTENT:
//
//     ThreeColumnMinBodyPx = SpotlightMinPx
//                          + ShelfMinForTwoCardsPx
//                          + CommerceMinPx
//                          + 2 * ColumnGapPx
//
// and each of those three terms is itself derived from the smallest thing it has
// to hold (see the constants below). Above that width the screen is three
// columns; below it the commerce rail moves BENEATH the spotlight and the body
// becomes two columns, which buys back CommerceMinPx + one gap of width without
// touching a single card, glyph or tap target.
//
// ==================  WHY THE MINIMUMS ARE ARITHMETIC HERE  ===================
// The runtime derivation below uses a conservative per-glyph advance constant
// rather than measuring the real TMP font. That is deliberate: a font asset can
// be absent on a cold boot, MeasureLineWidthPx returns -1 there, and a layout
// that cannot resolve its own minimum is a layout that draws nothing. The
// MEASUREMENT lives in the oracle instead - NightMarketRuntimeLayoutRegression
// measures the real strings with the real font at the real floor size and FAILS
// if any minimum here is too small. Arithmetic that ships, pinned by a
// measurement that gates: neither half can quietly drift.
//
// ⛔ ONE OWNER. PackStore calls Resolve() + Compose(); the runtime oracle calls
// the SAME two methods and measures what they produced. There is deliberately no
// second copy of any of these numbers anywhere - a test that recomputes a layout
// from its own constants cannot fail, and this repo has found three of those.
// =============================================================================

using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Wallet
{
    /// <summary>How the Night Market body is composed at the current surface width.</summary>
    public enum NightMarketMode
    {
        /// <summary>Phone portrait: one vertical flow (spotlight, shelf, sticky commerce).</summary>
        PortraitSingleColumn,
        /// <summary>Three columns, both rails at their comfort width. The abundant-width case.</summary>
        WideThreeColumn,
        /// <summary>Three columns, rails interpolated between their minimum and comfort widths.</summary>
        CompactThreeColumn,
        /// <summary>Two columns: spotlight over commerce on the left, the shelf on the right.</summary>
        StackedTwoColumn,
    }

    /// <summary>
    /// A resolved body composition, in canvas REFERENCE pixels. Every field is an absolute size,
    /// never a fraction of a parent - the P0-3 lesson PackStore's own header records.
    /// </summary>
    public struct NightMarketPlan
    {
        public NightMarketMode Mode;

        public float BodyWidthPx;
        public float BodyHeightPx;

        public float SpotlightWidthPx;
        public float MarketWidthPx;
        public float CommerceWidthPx;

        /// <summary>Height of the merchandise shelf. In landscape it owns the full body height.</summary>
        public float MarketHeightPx;

        /// <summary>Spotlight height. Full body height except in <see cref="NightMarketMode.StackedTwoColumn"/>.</summary>
        public float SpotlightHeightPx;
        /// <summary>Commerce height. Full body height except in <see cref="NightMarketMode.StackedTwoColumn"/>.</summary>
        public float CommerceHeightPx;

        /// <summary>Height of the commerce column's status band, pinned to ITS top.</summary>
        public float StatusBandPx;
        /// <summary>Height of the commerce column's CTA sub-host, pinned to ITS bottom.</summary>
        public float CtaHostPx;

        /// <summary>
        /// TRUE when the body is narrower than even the two-column minimum. The composition CLAMPS
        /// rather than shrinking a card - so this is the honest "we ran out of screen" signal, and
        /// the runtime oracle asserts no supported landscape aspect ever reaches it.
        /// </summary>
        public bool Deficit;
        /// <summary>How many reference px short the body is when <see cref="Deficit"/>.</summary>
        public float DeficitPx;

        /// <summary>Width each shelf card resolves to, two-up, at this plan's market width.</summary>
        public float CardWidthPx =>
            (MarketWidthPx - NightMarketComposition.ShelfChromePx) / NightMarketComposition.CardsPerRow;
    }

    /// <summary>
    /// The Night Market's responsive body composition. Pure geometry: it reads no game state, owns
    /// no GameObject lifetime, and can be called from an editor oracle exactly as it is called from
    /// <see cref="PackStore"/>.
    /// </summary>
    public static class NightMarketComposition
    {
        // =====================================================================
        //  THE SHELF - what two readable cards actually cost, horizontally.
        // ---------------------------------------------------------------------
        //  These four numbers are the SAME chrome PackStore.BuildCardRow and
        //  BuildScrollColumn author. They are named here so the minimum below is
        //  a derivation and not an estimate.
        // =====================================================================

        /// <summary>Cards per shelf row. The device-verified readability ruling; the shelf scrolls.</summary>
        public const int CardsPerRow = 2;

        /// <summary>HorizontalLayoutGroup.spacing in PackStore.BuildCardRow.</summary>
        public const float RowSpacingPx = 9f;
        /// <summary>BuildCardRow's horizontal padding, per side.</summary>
        public const float RowPadPerSidePx = 4f;
        /// <summary>BuildScrollColumn's VerticalLayoutGroup padding, per side.</summary>
        public const float ShelfPadPerSidePx = 8f;
        /// <summary>Reserved gutter so a scroll indicator can never sit ON a card's price column.</summary>
        public const float ScrollGutterPx = 24f;

        /// <summary>Everything in the market column that is NOT card, horizontally.</summary>
        public const float ShelfChromePx =
            (CardsPerRow - 1) * RowSpacingPx + 2f * RowPadPerSidePx + 2f * ShelfPadPerSidePx + ScrollGutterPx;

        /// <summary>
        /// The narrowest market column that still fits <see cref="CardsPerRow"/> cards at
        /// <see cref="StorePackCard.MinCardWidthPx"/>. Below this the layout group stops shrinking
        /// and the row overflows its mask - which CLIPS a price. Never cross it.
        /// </summary>
        public static float ShelfMinForTwoCardsPx =>
            CardsPerRow * StorePackCard.MinCardWidthPx + ShelfChromePx;

        /// <summary>A comfortable, not merely legal, shelf card - the width the wide case aims for.</summary>
        public const float ShelfComfortCardPx = 460f;

        /// <summary>The market column width the wide case aims for.</summary>
        public static float ShelfPreferredForTwoCardsPx =>
            CardsPerRow * ShelfComfortCardPx + ShelfChromePx;

        // =====================================================================
        //  THE SPOTLIGHT - derived from its NARROWEST ROW, the bar ledger.
        // ---------------------------------------------------------------------
        //  A ledger row is inset to x 0.06..0.94 of the column and splits that
        //  band three ways: the good's NAME, the bar, and the exact FIGURE. The
        //  figure is the truth on this screen and must never clip, so the column
        //  minimum is whatever makes the WIDEST of those three sub-bands hold its
        //  longest content at the mobile font floor.
        //
        //  The fractions below are the ones BuildLedgerRow authors. Change them
        //  there and change them here - the runtime oracle measures the result,
        //  so a divergence goes red rather than silently narrowing the column.
        // =====================================================================

        public const float LedgerInsetFrac  = 0.88f;   // 0.06 .. 0.94
        public const float LedgerLabelFrac  = 0.26f;   // 0.00 .. 0.26 of the inset band
        public const float LedgerBarFrac    = 0.42f;   // 0.28 .. 0.70
        public const float LedgerNumberFrac = 0.30f;   // 0.70 .. 1.00

        /// <summary>The longest economy key the ledger prints, in characters ("crystals").</summary>
        public const int LedgerLabelChars = 8;
        /// <summary>The widest figure the ledger prints, in characters ("999,999").</summary>
        public const int LedgerFigureChars = 7;

        /// <summary>
        /// Conservative per-glyph horizontal advance, as a fraction of font size. The real advances
        /// come from the font asset; this over-estimates on purpose so the derived minimum is never
        /// too small, and NightMarketRuntimeLayoutRegression MEASURES the real strings and fails if
        /// it ever is. Do not tune this down to make a layout fit.
        /// </summary>
        public const float GlyphAdvanceEm = 0.55f;

        /// <summary>Narrowest a comparison bar may be and still read as a bar rather than a tick.</summary>
        public const float LedgerBarMinPx = 120f;

        /// <summary>The narrowest spotlight column whose ledger row holds all three of its parts.</summary>
        public static float SpotlightMinPx
        {
            get
            {
                float floor  = ElarionUi.FontFloorMobile;
                float label  = LedgerLabelChars  * GlyphAdvanceEm * floor;
                float figure = LedgerFigureChars * GlyphAdvanceEm * floor;
                float inner = Mathf.Max(label / LedgerLabelFrac,
                              Mathf.Max(figure / LedgerNumberFrac, LedgerBarMinPx / LedgerBarFrac));
                return Mathf.Ceil(inner / LedgerInsetFrac);
            }
        }

        // =====================================================================
        //  WO-1636 - THE LEDGER ROW'S HEIGHT, IN REFERENCE PX.
        //
        //  ⛔ THE COLUMN HAD A WIDTH DERIVATION AND NO HEIGHT ONE, AND THAT IS THE
        //  WHOLE DEFECT. Everything above derives the spotlight's WIDTH from the widest
        //  thing a ledger row must hold. Nothing derived its HEIGHT, so BuildSpotlight
        //  authored the ladder as a fraction - rowH .055 of the column with a .010
        //  re-gap, i.e. a .045 BAND - and the first live glyph-oracle run measured what
        //  that resolves to (Builds/wave3-capture2.glyph-findings.txt:45-49, 51-55,
        //  60-64): a 32.8 px band on a 729 px column, on EVERY captured landscape
        //  surface. TMP's Ellipsis overflow CULLS THE WHOLE LINE when the line box will
        //  not seat in the rect, so all five printed figures - "4,000", "2,000", "400",
        //  "1,500", "600" - drew ZERO of their glyphs. Fifteen of the run's sixty
        //  findings are that one authoring mistake, on the money screen, and the rect
        //  passed every geometry rule because the rect was exactly where it belonged.
        //
        //  ⚠ AND THE FIGURE'S AUTOSIZE CEILING IS PART OF THE ARITHMETIC. TMP computes
        //  a line box at fontSizeMAX while auto-sizing, so a [30..32] band needs a
        //  32 pt line box even when it renders at 30. The ceiling is stated here and
        //  consumed by BuildLedgerRow, so the band and the ceiling cannot drift.
        // =====================================================================

        /// <summary>The autosize CEILING the printed ledger figure may reach. Deliberately the
        /// mobile font FLOOR, not a size above it: the band below is one line box at this value,
        /// and 2 pt of unused growth headroom is 2.5 px of line box the ladder cannot afford.
        /// ⛔ This is a CEILING, never a floor - the floor stays <see cref="ElarionUi.FontFloorMobile"/>
        /// and no path here may go under it (WO-1636 §2).</summary>
        public static float LedgerFigureMaxPt => ElarionUi.FontFloorMobile;

        /// <summary>One ledger row's band height, in reference px. Sized so the figure's line box
        /// seats with headroom over <see cref="LineBoxPx"/>'s 1.25 factor - the kit's own fit guard
        /// derives the real factor from the font face (ElarionUiKitObsidian.cs:3150-3153) and a
        /// band authored at exactly 1.25 has no margin if that face reads wider.</summary>
        public static float LedgerRowBandPx => Mathf.Ceil(LedgerFigureMaxPt * LedgerRowLineFactorCeiling);

        /// <summary>The widest font line factor (faceInfo.lineHeight / pointSize) this band is
        /// authored to survive.
        /// <para>⭐ IT IS MEASURED, NOT GUESSED, AND THE MEASUREMENT IS AN ABSENCE. The same
        /// glyph-oracle run that culled these figures on the three ~2.22-aspect surfaces did NOT
        /// flag them at 1280x720 (Builds/wave3-capture2.glyph-findings.txt:56-58 lists three
        /// findings for that panel and no ledger row among them). 16:9 resolves a ~848 px column,
        /// where the retired .045 fraction is 38.2 px - and every figure drew. So the real 30 pt
        /// line box on the shipped face is <b>at most 38.2 px, i.e. a factor of at most
        /// 1.272</b>. 1.30 clears that with margin and still leaves the ladder budget below room to
        /// spend; 1.25 (what <see cref="LineBoxPx"/> assumes) does not clear it at all.</para></summary>
        public const float LedgerRowLineFactorCeiling = 1.30f;

        /// <summary>Gap between two ledger rows. Deliberately ZERO: the rows are text bands, not tap
        /// targets, their leading already separates them, and the lower spotlight is spent to the px
        /// (see <see cref="LedgerComparisonReservePx"/>). Named rather than implied so the ladder's
        /// pitch is one expression and not an assumption.</summary>
        public const float LedgerRowGapPx = 0f;

        /// <summary>Row-to-row pitch down the ladder.</summary>
        public static float LedgerRowPitchPx => LedgerRowBandPx + LedgerRowGapPx;

        /// <summary>Gap between the bottom of the ladder and the comparison line under it.
        /// ⚠ 8 px, not 12: the retired `.012f` fraction resolved to 8.7 px on the 727.8 px column the
        /// run measured, and rounding a retired fraction UP would silently take px off the rows this
        /// ticket exists to give them back.</summary>
        public const float LedgerComparisonGapPx = 8f;

        /// <summary>What the comparison sentence needs. It WRAPS (FitInto -> Truncate) and the live
        /// string is a full sentence in a ~640 px band, so it is a TWO-line block.
        /// ⚠ DERIVED FROM <see cref="LedgerRowBandPx"/>, NOT FROM <see cref="LineBoxPx"/>. LineBoxPx
        /// assumes a 1.25 line factor while the rows beside it assume 1.30; two line boxes at 1.25
        /// is 76 px and the measured worst case is 76.4, so the cheaper constant is 0.4 px short and
        /// would trade fifteen culled figures for one truncated sentence. One assumption governs
        /// both bands.</summary>
        public static float LedgerComparisonReservePx => 2f * LedgerRowBandPx;

        /// <summary>Comfort headroom over the spotlight minimum in the wide case.</summary>
        public const float SpotlightComfort = 1.06f;

        public static float SpotlightPreferredPx => Mathf.Ceil(SpotlightMinPx * SpotlightComfort);

        // =====================================================================
        //  THE COMMERCE RAIL - derived from the ONE Buy control it exists for.
        // =====================================================================

        /// <summary>Breathing room either side of the canon button inside the rail.</summary>
        public const float CommerceGutterPx = 24f;

        /// <summary>The narrowest rail that seats the canon CTA at its canon width without clamping.</summary>
        public static float CommerceMinPx => ElarionUiKit.CanonCtaWidth + 2f * CommerceGutterPx;

        /// <summary>Comfort headroom over the commerce minimum in the wide case.</summary>
        public const float CommerceComfort = 1.19f;

        public static float CommercePreferredPx => Mathf.Ceil(CommerceMinPx * CommerceComfort);

        // ── The commerce rail's own vertical budget ──────────────────────────

        /// <summary>One TMP line box at the mobile font floor.</summary>
        public static float LineBoxPx => Mathf.Ceil(ElarionUi.FontFloorMobile * 1.25f);

        /// <summary>Padding under the CTA inside its sub-host.</summary>
        public const float CtaBottomPadPx = 13f;
        /// <summary>Padding over the CTA inside its sub-host.</summary>
        public const float CtaTopPadPx = 13f;

        /// <summary>
        /// The Buy control's AUTHORED height. It is the canon CTA height, not a fraction of anything:
        /// a fraction of a sub-host that shrinks in stacked mode is exactly how a control lands under
        /// MinTouchPx and gets GROWN over its neighbour by the clamp.
        /// </summary>
        public static float CtaButtonPx => ElarionUiKit.CanonCtaHeight;

        /// <summary>Smallest CTA sub-host that seats the button plus its padding.</summary>
        public static float CtaHostMinPx => CtaBottomPadPx + CtaButtonPx + CtaTopPadPx;

        /// <summary>The CTA sub-host the three-column cases give the rail (room for the optional
        /// balance-after and network lines above the button).</summary>
        public const float CtaHostPreferredPx = 440f;

        /// <summary>Status surface: two line boxes minimum, four when there is room.</summary>
        public static float StatusBandMinPx => 2f * LineBoxPx;
        public static float StatusBandPreferredPx => 4f * LineBoxPx;

        /// <summary>Gap between the status band and the CTA sub-host.</summary>
        public const float CommerceInnerGapPx = 20f;

        /// <summary>
        /// The commerce rail's height when it is stacked UNDER the spotlight - the smallest box that
        /// still holds a real status surface and a full-size Buy control.
        /// </summary>
        public static float CommerceStackedHeightPx =>
            StatusBandMinPx + CommerceInnerGapPx + CtaHostMinPx;

        // =====================================================================
        //  THE BREAKPOINTS
        // =====================================================================

        /// <summary>Gap between body columns.</summary>
        public const float ColumnGapPx = 20f;
        /// <summary>Vertical gap between the spotlight and a stacked commerce rail.</summary>
        public const float StackGapPx = 20f;

        /// <summary>Portrait phone vertical flow. The featured offer remains readable without
        /// consuming the shelf, while commerce stays a full-width sticky action at the bottom.</summary>
        public const float PortraitSpotlightMinPx = 430f;
        public const float PortraitSpotlightMaxPx = 620f;
        public const float PortraitMarketMinPx = 440f;

        /// <summary>
        /// ⭐ THE FORMULA. The narrowest body that can carry three columns without shrinking a card,
        /// a glyph or a tap target below its floor.
        /// </summary>
        public static float ThreeColumnMinBodyPx =>
            SpotlightMinPx + ShelfMinForTwoCardsPx + CommerceMinPx + 2f * ColumnGapPx;

        /// <summary>The body width at which both rails reach their comfort width.</summary>
        public static float ThreeColumnWideBodyPx =>
            SpotlightPreferredPx + ShelfPreferredForTwoCardsPx + CommercePreferredPx + 2f * ColumnGapPx;

        /// <summary>The narrowest body the two-column fallback can carry. Below this we are out of
        /// screen and say so rather than clipping a card.</summary>
        public static float TwoColumnMinBodyPx =>
            SpotlightMinPx + ColumnGapPx + ShelfMinForTwoCardsPx;

        // =====================================================================
        //  RESOLVE
        // =====================================================================

        /// <summary>
        /// Resolve the body composition for a body box of <paramref name="bodyWidthPx"/> x
        /// <paramref name="bodyHeightPx"/> reference px (i.e. AFTER the edge padding and the
        /// safe-area inset have already been taken off).
        /// </summary>
        public static NightMarketPlan Resolve(float bodyWidthPx, float bodyHeightPx)
        {
            var plan = new NightMarketPlan
            {
                BodyWidthPx  = Mathf.Max(1f, bodyWidthPx),
                BodyHeightPx = Mathf.Max(1f, bodyHeightPx),
            };

            float w = plan.BodyWidthPx;
            float h = plan.BodyHeightPx;

            float spotMin = SpotlightMinPx,  spotPref = SpotlightPreferredPx;
            float commMin = CommerceMinPx,   commPref = CommercePreferredPx;
            float shelfMin = ShelfMinForTwoCardsPx;
            float threeMin = ThreeColumnMinBodyPx;
            float threeWide = ThreeColumnWideBodyPx;

            if (h > w)
            {
                // ── PORTRAIT: ONE VERTICAL MOBILE FLOW ───────────────────────
                // This is intentionally selected by orientation before any landscape width
                // breakpoint. Previously a portrait phone fell into StackedTwoColumn and kept a
                // desktop left/right split; on a 360dp-class screen that also hit the deficit clamp.
                plan.Mode = NightMarketMode.PortraitSingleColumn;
                plan.SpotlightWidthPx = w;
                plan.MarketWidthPx = w;
                plan.CommerceWidthPx = w;

                plan.CommerceHeightPx = Mathf.Min(CommerceStackedHeightPx, h);
                float remaining = Mathf.Max(0f, h - plan.CommerceHeightPx - 2f * StackGapPx);
                plan.SpotlightHeightPx = Mathf.Round(Mathf.Clamp(
                    remaining * 0.40f, PortraitSpotlightMinPx, PortraitSpotlightMaxPx));
                plan.MarketHeightPx = Mathf.Round(Mathf.Max(0f, remaining - plan.SpotlightHeightPx));

                if (w < ShelfMinForTwoCardsPx || plan.MarketHeightPx < PortraitMarketMinPx)
                {
                    plan.Deficit = true;
                    plan.DeficitPx = Mathf.Ceil(Mathf.Max(
                        ShelfMinForTwoCardsPx - w,
                        PortraitMarketMinPx - plan.MarketHeightPx));
                }
            }
            else if (w >= threeMin)
            {
                // ── THREE COLUMNS ────────────────────────────────────────────
                // t walks the rails from their minimum to their comfort width. Above the wide
                // threshold t saturates and every remaining pixel goes to the shelf, which is the
                // column the merchandise lives in.
                float span = Mathf.Max(1f, threeWide - threeMin);
                float t = Mathf.Clamp01((w - threeMin) / span);

                plan.Mode = t >= 1f ? NightMarketMode.WideThreeColumn : NightMarketMode.CompactThreeColumn;
                plan.SpotlightWidthPx = Mathf.Round(Mathf.Lerp(spotMin, spotPref, t));
                plan.CommerceWidthPx  = Mathf.Round(Mathf.Lerp(commMin, commPref, t));
                plan.MarketWidthPx    = w - plan.SpotlightWidthPx - plan.CommerceWidthPx - 2f * ColumnGapPx;

                plan.SpotlightHeightPx = h;
                plan.CommerceHeightPx  = h;
                plan.MarketHeightPx = h;
            }
            else
            {
                // ── TWO COLUMNS: the commerce rail moves UNDER the spotlight ──
                // This buys back CommerceMinPx + one gap of width without shrinking a card. The
                // left rail still takes any slack up to its comfort width, so the fallback does not
                // read as a punished layout.
                plan.Mode = NightMarketMode.StackedTwoColumn;

                float slackRail = w - ColumnGapPx - ShelfPreferredForTwoCardsPx;
                float rail = Mathf.Clamp(slackRail, spotMin, spotPref);
                float shelf = w - rail - ColumnGapPx;

                if (shelf < shelfMin)
                {
                    // Take it out of the rail FIRST - the shelf floor is the one that clips a price.
                    rail = Mathf.Max(spotMin, w - ColumnGapPx - shelfMin);
                    shelf = w - rail - ColumnGapPx;
                }

                if (shelf < shelfMin)
                {
                    // ⛔ OUT OF SCREEN. We clamp to the floors and DECLARE it; we do not shrink a
                    // card to make it fit. The caller surfaces this and the oracle asserts that no
                    // supported landscape aspect can reach here.
                    plan.Deficit = true;
                    plan.DeficitPx = Mathf.Ceil(shelfMin - shelf);
                    shelf = shelfMin;
                }

                plan.SpotlightWidthPx = Mathf.Round(rail);
                plan.CommerceWidthPx  = Mathf.Round(rail);
                plan.MarketWidthPx    = Mathf.Round(shelf);

                float commerceH = Mathf.Min(CommerceStackedHeightPx, Mathf.Max(0f, h - StackGapPx));
                plan.CommerceHeightPx  = Mathf.Round(commerceH);
                plan.SpotlightHeightPx = Mathf.Round(Mathf.Max(0f, h - commerceH - StackGapPx));
                plan.MarketHeightPx = h;
            }

            // ── The commerce rail's internal vertical budget ─────────────────
            // Authored in px against the rail's REAL height, so the CTA is the same physical size in
            // every mode. The status band is what gives, never the button.
            float ctaHost = Mathf.Clamp(
                plan.CommerceHeightPx - StatusBandMinPx - CommerceInnerGapPx,
                CtaHostMinPx, CtaHostPreferredPx);
            ctaHost = Mathf.Min(ctaHost, plan.CommerceHeightPx);
            plan.CtaHostPx = Mathf.Round(ctaHost);
            plan.StatusBandPx = Mathf.Round(Mathf.Clamp(
                plan.CommerceHeightPx - plan.CtaHostPx - CommerceInnerGapPx,
                0f, StatusBandPreferredPx));

            return plan;
        }

        /// <summary>
        /// A one-line, greppable description of a resolved plan - what FlowTrace prints and what a
        /// failing oracle quotes back. Keep it ASCII: it reaches the device log.
        /// </summary>
        public static string Describe(NightMarketPlan p)
        {
            return string.Format(
                "mode={0} body={1:0}x{2:0} spotlight={3:0}x{4:0} market={5:0}x{6:0} commerce={7:0}x{8:0} card={9:0} " +
                "cta-host={10:0} status={11:0} thresholds[3col-min={12:0} wide={13:0}]{14}",
                p.Mode, p.BodyWidthPx, p.BodyHeightPx, p.SpotlightWidthPx, p.SpotlightHeightPx,
                p.MarketWidthPx, p.MarketHeightPx, p.CommerceWidthPx, p.CommerceHeightPx,
                p.CardWidthPx, p.CtaHostPx, p.StatusBandPx, ThreeColumnMinBodyPx, ThreeColumnWideBodyPx,
                p.Deficit ? " DEFICIT=" + p.DeficitPx.ToString("0") + "px" : string.Empty);
        }

        // =====================================================================
        //  COMPOSE - place the three column rects for a resolved plan.
        // =====================================================================

        /// <summary>The three body columns, as live RectTransforms.</summary>
        public struct Columns
        {
            public RectTransform Spotlight;
            public RectTransform Market;
            public RectTransform Commerce;
        }

        /// <summary>
        /// Create the three body columns under <paramref name="bodyHost"/> for
        /// <paramref name="plan"/>.
        ///
        /// <para>⛔ THIS IS THE ONLY PLACE THE COLUMNS ARE POSITIONED. PackStore calls it to build
        /// the screen; NightMarketRuntimeLayoutRegression calls it to build a canvas it can MEASURE.
        /// If the oracle placed its own rects it would be proving its own arithmetic, which is the
        /// hollow-pass shape WO-1138 named.</para>
        /// </summary>
        public static Columns Compose(Transform bodyHost, NightMarketPlan plan)
        {
            var cols = new Columns();
            if (bodyHost == null)
            {
                FlowTrace.Fail("Store", "NightMarketComposition.Compose: no body host - the store has no columns to draw into.");
                return cols;
            }

            if (plan.Deficit)
            {
                FlowTrace.Warn("Store", "NightMarketComposition: body is " + plan.DeficitPx.ToString("0") +
                                        " ref px NARROWER than the two-column minimum (" +
                                        TwoColumnMinBodyPx.ToString("0") + "). Columns are clamped to their " +
                                        "floors and the shelf will overrun rather than shrink a card below " +
                                        StorePackCard.MinCardWidthPx.ToString("0") + " px. " + Describe(plan));
            }

            switch (plan.Mode)
            {
                case NightMarketMode.PortraitSingleColumn:
                    // Reading order, top to bottom: selected offer, product shelf, sticky purchase.
                    cols.Commerce = Region(bodyHost, "Commerce",
                        Vector2.zero, new Vector2(1f, 0f),
                        Vector2.zero, new Vector2(0f, plan.CommerceHeightPx));
                    cols.Market = Region(bodyHost, "Market",
                        Vector2.zero, new Vector2(1f, 0f),
                        new Vector2(0f, plan.CommerceHeightPx + StackGapPx),
                        new Vector2(0f, plan.CommerceHeightPx + StackGapPx + plan.MarketHeightPx));
                    cols.Spotlight = Region(bodyHost, "Spotlight",
                        new Vector2(0f, 1f), Vector2.one,
                        new Vector2(0f, -plan.SpotlightHeightPx), Vector2.zero);
                    break;

                case NightMarketMode.StackedTwoColumn:
                    // Left rail: spotlight on top, commerce pinned to the bottom of the body.
                    cols.Commerce = Region(bodyHost, "Commerce",
                        new Vector2(0f, 0f), new Vector2(0f, 0f),
                        new Vector2(0f, 0f), new Vector2(plan.CommerceWidthPx, plan.CommerceHeightPx));
                    cols.Spotlight = Region(bodyHost, "Spotlight",
                        new Vector2(0f, 0f), new Vector2(0f, 1f),
                        new Vector2(0f, plan.CommerceHeightPx + StackGapPx),
                        new Vector2(plan.SpotlightWidthPx, 0f));
                    cols.Market = Region(bodyHost, "Market",
                        Vector2.zero, Vector2.one,
                        new Vector2(plan.SpotlightWidthPx + ColumnGapPx, 0f),
                        new Vector2(0f, 0f));
                    break;

                default:
                    cols.Spotlight = Region(bodyHost, "Spotlight",
                        new Vector2(0f, 0f), new Vector2(0f, 1f),
                        Vector2.zero, new Vector2(plan.SpotlightWidthPx, 0f));
                    cols.Commerce = Region(bodyHost, "Commerce",
                        new Vector2(1f, 0f), new Vector2(1f, 1f),
                        new Vector2(-plan.CommerceWidthPx, 0f), Vector2.zero);
                    cols.Market = Region(bodyHost, "Market",
                        Vector2.zero, Vector2.one,
                        new Vector2(plan.SpotlightWidthPx + ColumnGapPx, 0f),
                        new Vector2(-(plan.CommerceWidthPx + ColumnGapPx), 0f));
                    break;
            }

            FlowTrace.Step("Store", "NightMarketComposition: " + Describe(plan));
            return cols;
        }

        /// <summary>
        /// A rect authored by ANCHOR + absolute OFFSET, never by a fraction of a parent - the same
        /// helper shape PackStore.Region uses, and for the same recorded reason.
        /// </summary>
        private static RectTransform Region(Transform parent, string name,
                                            Vector2 anchorMin, Vector2 anchorMax,
                                            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return rt;
        }
    }
}
