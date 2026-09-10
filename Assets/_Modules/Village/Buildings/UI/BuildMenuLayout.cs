// =============================================================================
// BuildMenuLayout - the build menu's FIXED-PIXEL band ladder (WO-878).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHY THIS FILE EXISTS. Every band on BuildMenu used to be a FRACTION OF THE BODY
// ZONE, and the body zone is far smaller than it looks: with the modal anchored at
// 0.92 of a landscape canvas, ElarionUiKit's close-band reservation raises the
// FrameCore body floor to (0.05 + CanonCtaHeight/panelH) + 0.16, so the body
// resolves to
//
//     bodyPx = 0.625 * panelHeightPx - CanonCtaHeight
//
// i.e. ~430 REFERENCE px at 2340x1080 and ~423 px at the Seeker's real 2670x1200 --
// NOT the ~780 px the panel appears to offer. Against that, the shipped fractions
// resolved to:
//
//     root verb row  0.115 x 357 =  41 px      back button  0.14 x 357 =  50 px
//     upgrade CTA    0.15  x 357 =  54 px      info row     0.095 x 357 =  34 px
//
// every one of them UNDER ElarionUiKit.MinTouchPx (112). ClampMinTouch then grows a
// sub-floor button SYMMETRICALLY ABOUT ITS CENTRE, so each of those rects gained
// 30-36 px on EACH side after layout and ate its neighbours: the five root verbs
// (48.9 px stride, 112 px grown height) overlapped by ~63 px each and sliced one
// another's labels, "< Back" grew straight through the "UPGRADE TOWER" title, and
// the Upgrade CTA grew up into the cost/preview text. That is the WO-841/852/865
// failure class verbatim.
//
// THE RULE THIS FILE ENFORCES: a band is a FIXED REFERENCE-PIXEL height, never a
// fraction of a parent, and every band that holds a BUTTON is >= the kit touch
// floor so ClampMinTouch is provably a no-op and can never grow into a neighbour.
// offsetMin/offsetMax on a CanvasScaler'd canvas ARE reference px -- the same unit
// MinTouchPx is expressed in -- so these rungs hold at every screen size with no
// scaler math (the LeaderboardPanel / SettingsController px-ladder precedent).
//
// Pure constants: no Unity UI types, no state, no logic. The View lays out from
// them; BuildMenuLayoutRegression asserts the ladder fits the measured body at both
// capture aspects, which is the assertion the shipped fractions would have failed.
// =============================================================================

using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// The build menu's authored band ladder, in REFERENCE PIXELS on the kit's
    /// 1080x1920 canvas. Named floors only - the View never writes a raw number and
    /// never sizes a band as a fraction of its parent.
    /// </summary>
    public static class BuildMenuLayout
    {
        /// <summary>The kit touch floor. Every band that carries a button is authored AT this
        /// height, so <c>ElarionUiKit.ClampMinTouch</c> has nothing to grow (it is a pure floor
        /// and never shrinks), and therefore cannot inflate a control into a neighbouring band.</summary>
        public const float TouchFloorPx = ElarionUiKit.MinTouchPx;   // 112

        /// <summary>Top band of every sub-screen: the "&lt; Back" button + the screen title,
        /// side by side (horizontally disjoint), so the title can never clip the button.</summary>
        public const float NavBandPx = TouchFloorPx;

        /// <summary>Bottom band of every sub-screen: the cost/preview lines on the left and the
        /// primary CTA on the right, side by side. Disjoint from the content band above it by a
        /// fixed gap, which is what stops the preview text from landing on the button.
        ///
        /// ── WO-1636: 112 -&gt; 160 px, BECAUSE THE INFO LINES NEED TWO LINES AND 112 CANNOT ────
        /// ⛔ THIS BAND IS NO LONGER "the touch floor". It is the touch floor for the BUTTON it
        /// carries (160 &gt;= 112, so ClampMinTouch is still provably a no-op) PLUS the height the
        /// preview text actually needs. The two are different requirements and conflating them is
        /// what capped the text at one line.
        ///
        /// MEASURED (chain 17, `BuildMenuUpgradeTower_1920x1080`): the preview line
        /// "Lvl 1 to 2:  dmg 23.8 to 46.8,  range 18m to 22m" drew 33 of 35 printable glyphs in a
        /// rect x -598.1..6 (604.1 px), y -156.1..-100.1 (56.0 px), at font 30 -
        /// ElarionUi.FontFloorMobile, the bottom of its band. 56 px is ONE line box
        /// (30 x BuildMenuLayoutRegression.LineBoxMul 1.25 = 37.5), so the wrap put the tail on a
        /// second line and Truncate dropped it. The band, not the font, was the constraint.
        ///
        /// ⛔ AND WIDTH ALONE COULD NEVER HAVE FIXED IT. That same string measures ~634 px at
        /// font 30, and the WORST string UpgradeStatLineFor can emit
        /// ("Lvl 9 to 10:  dmg 123.4 to 234.5,  range 18.5m to 22.5m") measures ~731 px, while a
        /// three-axis CostSummaryFor line reaches ~732 px. The info lane was 693.8 px at the
        /// ORIGINAL InfoWidthFrac 0.58 - so the longest real preview line was ALREADY over the
        /// edge before this ticket touched anything, and reverting to 0.58 would not fix it
        /// either. A single line of this band cannot hold this copy at any split.
        ///
        /// WHY 160 EXACTLY - it is bounded on BOTH sides, and both bounds are computed, not felt:
        ///   FLOOR  two line boxes at the font floor = 2 x 37.5 = 75 px per info row, and the two
        ///          rows tile the band (InfoLinePx x2 == ActionBandPx, pinned), so the band needs
        ///          &gt;= 150. 144 gives 72 px a row and still culls the second line; 160 gives 80.
        ///   CEILING BuildMenuLayoutRegression's own [body-fits] formula, evaluated over its own
        ///          Aspects list: the body resolves to 489.0 / 430.5 / 423.1 px at 1920x1080 /
        ///          2340x1080 / 2670x1200, and the ladder must leave one RowPx list row, so
        ///          ActionBandPx &lt;= body - NavBandPx - 2*BandGapPx - RowPx = 241.0 / 182.5 /
        ///          **175.1**. The Seeker's 2670x1200 is the binding aspect.
        /// 160 sits 10 px above the floor and 15.1 px under the tightest ceiling. Do not raise it
        /// toward 175 for comfort - that spends the whole margin the scroll well has left.</summary>
        public const float ActionBandPx = 160f;

        /// <summary>One selectable row inside a scroll well (tower radio row / placed-tower row).
        /// The row IS the tap target, so it sits at the floor. (Was 96 px, which ClampMinTouch grew
        /// by 8 px on each side - exactly consuming the 8 px inter-row spacing.)</summary>
        public const float RowPx = TouchFloorPx;

        /// <summary>One verb cell on the root chooser grid.</summary>
        public const float RootCellPx = TouchFloorPx;

        /// <summary>Gap between two stacked bands. Any positive value keeps them disjoint; 12 px
        /// reads as a deliberate seam at phone density.</summary>
        public const float BandGapPx = 12f;

        /// <summary>Gap between two rows inside a scroll well.</summary>
        public const float RowGapPx = 8f;

        /// <summary>Height of ONE of the two info rows inside the action band.
        /// ⚠ WO-1636: this is now TWO TMP line boxes, not one. It was 56 px - "a whole TMP line
        /// box at the fonts they render" - and that sentence was the defect written down: the
        /// preview copy these rows carry is a SENTENCE (see ActionBandPx for the measurement), so
        /// a one-line row could only ever ellipsise it. At the new band it is 80 px, which seats
        /// two line boxes at the ElarionUi.FontFloorMobile floor (2 x 30 x 1.25 = 75) with 5 px
        /// spare, and the rows still tile the band exactly - the invariant
        /// BuildMenuLayoutRegression [line-box] asserts, and the reason this stays a x0.5
        /// derivation rather than a second typed number.
        /// ⛔ THE ROWS ARE FitBlock, NEVER FitSingleLine (BuildMenu.AddInfoLines). Swapping them
        /// back to a single-line fit re-imposes the one-line cap this height exists to lift, and
        /// the extra 48 px of band would then be paid for nothing.</summary>
        public const float InfoLinePx = ActionBandPx * 0.5f;   // 80

        // ── Root chooser grid ────────────────────────────────────────────────
        /// <summary>Verb columns on the root chooser. Five verbs at the 112 px touch floor need
        /// 592 px stacked in a body that is ~423-430 px tall; two columns need 360 px and fit.</summary>
        public const int RootColumns = 2;
        /// <summary>Build Tower / Upgrade Tower / Repair Wall / Manage Towers / Build Mode.</summary>
        public const int RootVerbCount = 5;
        /// <summary>Horizontal pad on each side of a grid cell, as a fraction of the body width
        /// (width is never the constraint on this screen - the body is ~1450 px wide).</summary>
        public const float RootCellPadFrac = 0.012f;

        // ── Horizontal splits (width is never the constraint; these stay fractional) ──
        /// <summary>"&lt; Back" occupies the left of the nav band.</summary>
        public const float BackWidthFrac = 0.24f;
        /// <summary>The screen title starts clear of the Back button.</summary>
        public const float TitleLeftFrac = 0.28f;
        // ── WO-1636: THE ACTION BAND'S SPLIT, RE-DERIVED FROM A SECOND MEASUREMENT ──────
        // ⚠ THE FIRST ATTEMPT AT THIS SPLIT WAS WRONG, AND HOW IT WAS WRONG IS THE LESSON.
        // Chain 16 moved InfoWidthFrac 0.58 -> 0.505 / CtaLeftFrac 0.62 -> 0.525 to clear a
        // measured CTA truncation. It cleared it - and chain 17's capture then caught the INFO
        // line cut instead. The width was simply MOVED from one column to the other, because the
        // info side had been sized from a glyph-advance ESTIMATE (the oracle logs only the labels
        // it fails, so the info lines had no rect on the first run) while the CTA side had a real
        // measurement. An estimate traded against a measurement is not a budget; it is a guess
        // wearing a number. Both sides are measured now.
        //
        // THE BAND IS 1196.2 REFERENCE PX WIDE AT 1920x1080, and that is measured, not assumed:
        // chain 17 reported the info label at 604.1 px (x -598.1..6) while InfoWidthFrac was
        // 0.505, and 604.1 / 0.505 = 1196.2. Cross-checked against the CTA: 418.2 px of label in
        // a 0.38 lane of that band is a button label inset of exactly 0.9200. The band scales
        // with the canvas, so per aspect (canvasW x ModalWidth 0.70 x FrameCore body 0.890):
        //     1920x1080  band 1196.2 px   <- the NARROWEST, and the aspect both findings fired at
        //     2340x1080  band 1320.5 px
        //     2670x1200  band 1338.2 px
        //
        // ⛔ THE TWO STRINGS DO NOT BOTH FIT ON ONE LINE AT ANY SPLIT. Sized for the LONGEST
        // each side can emit, not for the captured sample:
        //     info  UpgradeStatLineFor worst ("Lvl 9 to 10:  dmg 123.4 to 234.5,  range 18.5m
        //           to 22.5m")                                     ~731 px at the font floor
        //           CostSummaryFor worst (three priced axes)        ~732 px
        //     CTA   "NOT ENOUGH CRYSTALS (220)"                     ~492 px
        //           "NOT ENOUGH RESOURCES" (the chain-16 finding)   ~437 px
        // 731 + 492/0.92 = 1266 px against a 1196.2 px band before any gap: over-subscribed at
        // the narrowest aspect no matter where the split sits. That is the proof that no fraction
        // solves this, and it is why the fix is the WO-1628 shape - the info text becomes a
        // TWO-LINE FitBlock (see ActionBandPx, grown 112 -> 160 to seat the second line), which
        // halves what the info column needs in WIDTH and hands the difference to the CTA.
        //
        // THE SPLIT, with two lines available on the info side (capacity = lane x 2, less ~15%
        // for word-wrap waste), at the binding 1920x1080 aspect:
        //     info lane 0.45 x 1196.2 = 538.3 px -> 1076 px of capacity vs ~731 px needed  +47%
        //     CTA label (1 - 0.47) x 1196.2 x 0.92 = 583.2 px vs ~492 px needed            +19%
        // Both margins only grow at the two wider aspects (CTA label 643.9 / 652.5 px).
        // The 0.02 clear-air gap is preserved, so the [disjoint] invariant
        // BuildMenuLayoutRegression asserts about these two constants still holds.
        //
        // ⛔ ONE CAPTION IS STILL UNFITTABLE, AND IT IS NOT A WIDTH PROBLEM. ShortfallMessage
        // returns CapBlockMessage FIRST (BuildModeController.cs:3547), and that is a multi-
        // SENTENCE paragraph - "... Also over your Iron ceiling of 3,000: needs 3,500." - routed
        // into a FitSingleLine button face. No band width seats a paragraph on one line. It is
        // NOT what either capture caught, it is pre-existing, and it wants its own ticket; do not
        // widen this lane further chasing it.
        /// <summary>The cost/preview lines occupy the left of the action band.</summary>
        public const float InfoWidthFrac = 0.45f;
        /// <summary>The primary CTA occupies the right of the action band, clear of the text.</summary>
        public const float CtaLeftFrac = 0.47f;

        // ── The modal's own anchors ──────────────────────────────────────────
        // ⚠ WO-1636: the "112 px action band" below is the HISTORY, not the current ladder -
        // ActionBandPx is 160 px now (it grew to seat two lines of preview copy; see its own note).
        // The sentence is kept verbatim because it records WHY ModalHeightFrac was raised, and the
        // headroom it bought is what the 160 was spent out of - re-verified 2026-09-10 against
        // BuildMenuLayoutRegression's [body-fits] formula: body 489.0 / 430.5 / 423.1 px vs a
        // 408 px ladder, so the raise still holds with 15.1 px to spare at the tightest aspect.
        // The panel was 0.20-0.80 x 0.10-0.90, which left a body of only ~357 px - too short to
        // seat a 112 px nav band, a 112 px action band and a usable list at the same time. 0.92 of
        // the canvas height yields ~423-430 px, and the wider box also stops the ~1450 px-wide
        // content from being crammed into 60% of a landscape screen.
        public const float ModalXMin = 0.15f;
        public const float ModalYMin = 0.04f;
        public const float ModalXMax = 0.85f;
        public const float ModalYMax = 0.96f;
        /// <summary>Panel height as a fraction of the canvas - the input to the body-height
        /// derivation at the top of this file.</summary>
        public const float ModalHeightFrac = ModalYMax - ModalYMin;   // 0.92

        // ── Derived ladder (the oracle asserts these fit the measured body) ──

        /// <summary>Rows the root grid needs for <see cref="RootVerbCount"/> verbs.</summary>
        public static int RootRows => (RootVerbCount + RootColumns - 1) / RootColumns;

        /// <summary>Total height the root grid occupies, gaps included.</summary>
        public static float RootGridHeightPx => RootRows * RootCellPx + (RootRows - 1) * BandGapPx;

        /// <summary>Distance from the body top to the top of the scrolling content band.</summary>
        public static float ContentTopInsetPx => NavBandPx + BandGapPx;

        /// <summary>Distance from the body bottom to the bottom of the scrolling content band.</summary>
        public static float ContentBottomInsetPx => ActionBandPx + BandGapPx;

        /// <summary>Everything a sub-screen spends before the content band gets a pixel.</summary>
        public static float SubScreenFixedPx => ContentTopInsetPx + ContentBottomInsetPx;
    }
}
