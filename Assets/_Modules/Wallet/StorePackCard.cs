// =============================================================================
// StorePackCard — THE ONE Night Market card template (UI-001 §R3, owner ruling 4)
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// Owner ruling 2026-08-22 (verbal, ruling 4), quoted so it cannot be paraphrased
// away: "each pack in a sleek rounded rectangle with an image for the pack and
// below show what they are getting - different colors with backgrounds glowing".
//
// THIS FILE IS THE WHOLE OF THAT RULING, AND IT IS ONE TEMPLATE WITH THREE
// VARIANTS — Featured / Standard / Compact. UI-001 §2 permits EXACTLY ONE
// store-local helper on top of the kit, and this is it. A second card builder,
// a per-SKU styling branch, or a second palette is the parallel-design-system
// failure that §2 exists to forbid.
//
// ⛔ THE ASSET BILL IS EXACTLY TWO WHITE SPRITES (§R3), and neither is new art:
//   1. ElarionUiKit's rounded 9-slice   — every corner in the game already uses it;
//      ApplyRounded(img, radiusPx) only rescales its baked 6 px border.
//   2. ElarionUiKit.RadialGlowSprite    — a white radial falloff, added beside it.
// Both are TINTED here through NightMarketPalette / the authored orbTint. There
// are NO per-band textures, NO particles, and ZERO VFX loop slots: the glow is a
// static Image, which is the load-bearing half of ruling 4's implementation note.
//
// ⛔ COLOUR NEVER CARRIES MEANING ALONE. The owner is red/green colourblind, so
// every card states its band in WORDS (the band eyebrow above it), its state in a
// WORD (the state pill), and its price in DIGITS. Strip every hue and the card
// still reads — that is the acceptance test, and it is a greyscale capture, not a
// claim. See NightMarketPalette's header for the four-carrier ordering.
//
// ⛔ TMP IS ASCII-ONLY. The wireframe's emoji glyphs are STAND-INS. In-game the
// art-well glyph is a ConceptIconResolver sprite, or two-letter ASCII initials
// derived from the pack name. Shipping an emoji is a tofu box on first device boot.
//
// ⛔ EVERY VARIANT IS AUTHORED >= MinTouchPx(112) ON BOTH AXES, IN REFERENCE PX,
// so ElarionUiKit.ClampMinTouch is a NO-OP (WO-1060 Assert A). The heights below
// are FIXED reference px and not fractions of a parent zone, precisely because the
// 2026-08-22 device frames showed what a fraction of a SHRUNKEN landscape budget
// does: the usable canvas is 2120 x 978 reference units (UI-001 §0.4), not 1080 x
// 1920, so a height authored as a fraction of "the panel" lands at roughly half
// what its author measured. Author the number; let the column scroll.
//
// ⛔ THIS FILE TOUCHES NO COMMERCE AUTHORITY. It renders decisions; it never makes
// one. `actionable`, the CTA face and the refusal sentence are all PASSED IN by the
// caller, which got them from PurchaseGate — UI-002's binding rule. There is no
// PurchaseGate reference in this file and there must never be one, or the button
// and the charge acquire two different opinions.
//
// Landscape only; code-built uGUI (UXML does not work in player builds — the
// PackStore.uxml/.uss pair still on disk is a TRAP, not a starting point).
// =============================================================================

using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;

namespace DeNelle.Wallet
{
    /// <summary>Which of the one template's three sizes a card is drawn at.</summary>
    public enum StorePackCardVariant
    {
        /// <summary>The spotlight subject: tallest art well, room for the ledger beneath.</summary>
        Featured = 0,
        /// <summary>The shelf default — two-up in the market column.</summary>
        Standard = 1,
        /// <summary>The dense rung (impulse / gap rows). Still a full tap target.</summary>
        Compact = 2,
        /// <summary>Three-up landscape shelf card: one-line name, full contents and price.</summary>
        LandscapeStandard = 3,
    }

    /// <summary>
    /// Everything the card DISPLAYS, resolved by the caller. Deliberately a plain data carrier:
    /// the card must not be able to compute a price, a state or an entitlement, because a second
    /// place that computes commerce truth is a second authority (UI-002).
    /// </summary>
    public struct StorePackCardModel
    {
        /// <summary>Stable id — used for the GameObject name and the tap callback only.</summary>
        public string Sku;
        /// <summary>Pack display name. Wraps to two lines; never ellipsized (§5).</summary>
        public string Name;
        /// <summary>The goods line: top goods, then "+N more", then convenience. Caller-derived
        /// from the SAME describer the spotlight ledger uses, so the two can never disagree.</summary>
        public string Contents;
        /// <summary>The arithmetic caption ("1,922 goods per $"). Empty = drawn absent, never faked.</summary>
        public string ValueCaption;
        /// <summary>Rail amount with its unit, e.g. "36 SKR". NEVER clipped (§5).</summary>
        public string PriceMajor;
        /// <summary>Fiat reference, e.g. "$2.99". Empty when no quote backs it.</summary>
        public string PriceMinor;
        /// <summary>Uppercase merchandising pill. ⛔ Only badges the arithmetic supports (§7).</summary>
        public string Badge;
        /// <summary>State WORD ("Owned" / "Locked" / "Your gap"). Outranks the badge when present.</summary>
        public string StateWord;
        /// <summary>
        /// A SHORT worded reason this pack cannot be bought right now, or EMPTY when it can.
        /// <para>⛔ THIS IS NOT "Price unavailable" AND IT NEVER REPLACES IT. That sentence means we
        /// have no price at all and it is still printed in the price row where the digits would go.
        /// This is the OTHER state, and it is the new one: the pack IS priced, the digits ARE drawn,
        /// and buying is simply not open yet. A card that showed the price with a live buy control
        /// invited a tap that the till would refuse -- fail-closed, but still a lie on the shelf.</para>
        /// <para>⛔ THE CARD DECIDES NOTHING. Like every other string here the caller resolves it
        /// (from PurchaseQuoteService.SellableReasonFor), because a card that could compute
        /// sellability would be a second commerce authority (UI-002).</para>
        /// <para>⚠ KEEP IT SHORT -- it is budgeted at <see cref="ReasonBlockLines"/> lines at
        /// <c>FontCaption</c>. The server's FULL sentence belongs on the commerce column's refusal
        /// plate, which has a whole host to spend; the card carries the state, not the essay.</para>
        /// </summary>
        public string NotSellableReason;
        /// <summary>
        /// The storewide sale ribbon's copy ("30% OFF"), or EMPTY for no ribbon (WO-1800).
        /// <para>⛔ RESOLVED BY THE CALLER FROM THE SERVER'S <c>saleBps</c>/<c>saleLabel</c>, exactly
        /// like every other string here. The card cannot ask whether something is on sale, because a
        /// card that could would be a second commerce authority (UI-002) — and an invented sale sign
        /// is a lie about money, not a layout bug.</para>
        /// <para>⛔ EMPTY IS THE FAIL-CLOSED STATE AND IT IS THE DEFAULT. Absent, zero or unusable
        /// sale bps ⇒ empty ⇒ no ribbon drawn at all. There is no "0% OFF".</para>
        /// </summary>
        public string SaleBadge;
        /// <summary>
        /// The sale's one proof line — "was &lt;s&gt;$4.99&lt;/s&gt; now $2.99 - ends in 2d 4h", or EMPTY.
        /// <para>⛔ IT GETS A BLOCK OF ITS OWN and the card's height grows by it (see
        /// <see cref="SaleExtraPx"/>). The vertical budget above is spent to the pixel, so squeezing
        /// a new line into the price lane or the contents block is the very defect FIX 2 settled:
        /// author the number, let the column scroll, never clip the string.</para>
        /// <para>Carries TMP rich-text <c>&lt;s&gt;</c> markup on the anchor only. The STRIKE is the
        /// greyscale carrier — a crossed-out number reads with every hue removed.</para>
        /// </summary>
        public string SaleLine;
        /// <summary>The band this card lives in — supplies the accent light.</summary>
        public StoreBand Band;
        /// <summary>Authored per-pack tint (packs.json <c>orbTint</c>). Empty falls back to the band light.</summary>
        public string OrbTint;
        /// <summary>Concept ids tried in order for the art-well glyph. Null/miss -> ASCII initials.</summary>
        public string[] GlyphConcepts;
        /// <summary>Resources/UI/NightMarket asset name. Empty preserves the concept/initial fallback.</summary>
        public string ArtResource;
        /// <summary>Draws the selected treatment (brighter bloom + a 1 px inset ring).</summary>
        public bool Selected;
    }

    /// <summary>Live handles a caller needs after the build (selection repaint, state repaint).</summary>
    public sealed class StorePackCardHandle
    {
        public GameObject Root;
        public Button Button;
        public Image Glow;
        public Image Ring;
        public TextMeshProUGUI StateLabel;
        public TextMeshProUGUI PriceLabel;
        /// <summary>The not-sellable state line, or null when the pack is buyable.</summary>
        public TextMeshProUGUI ReasonLabel;
        /// <summary>The storewide sale ribbon's plate, or null when this card is not on sale.</summary>
        public Image SaleRibbon;
        /// <summary>The ribbon's "30% OFF" face, or null.</summary>
        public TextMeshProUGUI SaleRibbonLabel;
        /// <summary>The struck-anchor / effective / countdown proof line, or null.</summary>
        public TextMeshProUGUI SaleLineLabel;
        /// <summary>True when this card carries a sale ribbon — the pulse driver's one question.</summary>
        public bool OnSale;
    }

    /// <summary>The single Night Market card template. Static builders; holds no state.</summary>
    public static class StorePackCard
    {
        // =====================================================================
        //  THE MEASUREMENTS — reference px on the 2120 x 978 landscape budget.
        // ---------------------------------------------------------------------
        //  Every number here is AUTHORED, not derived from a parent fraction, and
        //  every one of them clears MinTouchPx(112) on both axes by construction.
        //  The shortest card is Compact at 228 px tall: 116 px of headroom over
        //  the floor, so no re-layout of the column can quietly push it under.
        // =====================================================================

        /// <summary>Card corner radius, reference px (§R3: "~25 ref").</summary>
        public const float CornerRadiusPx = 25f;

        // =====================================================================
        //  ⭐ THE CARD IS A BUDGET NOW, NOT THREE LITERALS (WO-1162 FIX 2).
        // ---------------------------------------------------------------------
        //  WHAT WAS WRONG WITH 470 / 344 / 228, in arithmetic anyone can redo:
        //  the STANDARD card is 344 px tall with a 152 px art well, and its text
        //  stack was authored as fixed top offsets - name at art+14 in a 96 px
        //  block, contents 6 px later in a 92 px block, the optional value
        //  caption 6 px after THAT in a 40 px block:
        //
        //      name      166 .. 262
        //      contents  268 .. 360      <- the card ENDS at 344
        //      caption   366 .. 406      <- entirely OFF the card
        //      price     268 .. 330      (bottom-pinned, 14 + 62 from the floor)
        //
        //  So the contents block and the PRICE ROW occupied the same 268..330
        //  lane on every standard card, and the value caption was drawn 62 px
        //  below the card's own bottom edge. Compact is the same defect one size
        //  down: a 52 px name block at 115 ends at 167, and the price row starts
        //  at 152. Nothing caught it because every constant involved was legal on
        //  its own - which is exactly why the oracle for this is a MEASUREMENT of
        //  real RectTransforms and not another arithmetic re-derivation.
        //
        //  THE FIX IS NOT TO SHRINK THE TYPE. A card must either FIT its required
        //  content or DROP the optional value caption; price and state are never
        //  what gives. So each variant's height is now the SUM of the blocks it
        //  must carry, at the type sizes it renders them at, and the shelf - which
        //  scrolls - absorbs the taller row. Card readability outranks catalogue
        //  density; that ruling is already why the shelf is two-up.
        // =====================================================================

        /// <summary>TMP line box as a multiple of font size. Conservative: TMP's own line height for
        /// these faces is under this, so a block budgeted here always holds its lines.</summary>
        private const float LineBoxMul = 1.25f;

        /// <summary>Art well -> first text block.</summary>
        private const float TextGapPx = 14f;
        /// <summary>Between two text blocks.</summary>
        private const float BlockGapPx = 6f;
        /// <summary>Last text block -> the bottom-pinned price row. Never zero: this gap is what
        /// makes "the price row is a separate lane" true rather than merely intended.</summary>
        private const float PriceGapPx = 10f;
        /// <summary>Price row -> card bottom.</summary>
        private const float BottomPadPx = 14f;

        /// <summary>One block, sized for <paramref name="lines"/> lines at <paramref name="fontPx"/>.</summary>
        private static float BlockPx(float fontPx, int lines) =>
            Mathf.Ceil(fontPx * LineBoxMul * Mathf.Max(1, lines));

        /// <summary>Name block: two lines everywhere but Compact, where the card is a dense rung.</summary>
        private static float NameFont(StorePackCardVariant v) =>
            v == StorePackCardVariant.LandscapeStandard ? 28f : FontName;
        private static float BodyFont(StorePackCardVariant v) =>
            v == StorePackCardVariant.LandscapeStandard ? 25f : FontBody;
        private static float PriceFont(StorePackCardVariant v) =>
            v == StorePackCardVariant.LandscapeStandard ? 29f : FontPrice;
        private static float MinorFont(StorePackCardVariant v) =>
            v == StorePackCardVariant.LandscapeStandard ? 25f : FontMinor;

        public static float NameBlockPx(StorePackCardVariant v) =>
            BlockPx(NameFont(v), v == StorePackCardVariant.Compact ||
                               v == StorePackCardVariant.LandscapeStandard ? 1 : 2);

        /// <summary>Contents block: two lines. Absent on Compact.</summary>
        public static float ContentsBlockPx(StorePackCardVariant v) => BlockPx(BodyFont(v), 2);

        /// <summary>The OPTIONAL goods-per-dollar caption. One line, and the FIRST thing dropped.</summary>
        public static float CaptionBlockPx => BlockPx(FontCaption, 1);

        /// <summary>The price row. REQUIRED, bottom-pinned, never traded away.</summary>
        public static float PriceBlockPx(StorePackCardVariant v) => BlockPx(PriceFont(v), 1);

        // =====================================================================
        //  ⭐ THE NOT-SELLABLE LINE GETS ITS OWN BLOCK — IT DOES NOT BORROW ONE.
        // ---------------------------------------------------------------------
        //  The price row's x bands (0.06..0.94 with no fiat minor, 0.06..0.62
        //  with one) are MEASURED numbers with a defect behind each of them, and
        //  the budget above is spent to the pixel. So a new required sentence
        //  cannot be squeezed into either: it gets a block of its own, the card's
        //  DERIVED height grows by exactly that block plus its gap, and the shelf
        //  -- which scrolls -- absorbs the taller row. That is the same ruling
        //  FIX 2 settled: author the number, let the column scroll; never shrink
        //  the type and never clip the string.
        //
        //  It is RESERVED BEFORE the optional blocks, like the price lane, because
        //  it is REQUIRED whenever it is present: a card may drop its value caption
        //  to make room for the reason, but it may never drop the reason. A player
        //  looking at a real price with no buy control must be told why in WORDS --
        //  the owner is red/green colourblind, so a dimmed control explains nothing.
        // =====================================================================

        /// <summary>Lines budgeted for the not-sellable state line. Two at <c>FontCaption</c>.</summary>
        public const int ReasonBlockLines = 2;

        /// <summary>The not-sellable state block. Present only on a card that carries a reason.</summary>
        public static float ReasonBlockPx => BlockPx(FontCaption, ReasonBlockLines);

        /// <summary>What a reason costs a card in total: its block plus the gap above it.</summary>
        public static float ReasonExtraPx => ReasonBlockPx + BlockGapPx;

        // =====================================================================
        //  ⭐ THE SALE LINE GETS ITS OWN BLOCK TOO (WO-1800).
        // ---------------------------------------------------------------------
        //  Owner, 2026-09-16: "put big sales signs with x% off!!!! you know some
        //  flash". That is TWO elements with two different budgets, and keeping
        //  them apart is the whole of why this is safe:
        //
        //    1. THE RIBBON is a pure OVERLAY on the art well, top-left, like the
        //       state pill is at top-right. It costs the vertical budget NOTHING,
        //       so no card grows for it and no existing measurement moves.
        //
        //    2. THE SALE LINE ("was $4.99  now $2.99 - ends in 2d 4h") is TEXT,
        //       so it gets a BLOCK, and the card's derived height grows by exactly
        //       that block plus its gap. The budget above is spent to the pixel —
        //       the arithmetic in FIX 2's header is what happens when a new line
        //       is pushed into a lane that was already full, and the price row is
        //       the lane it would have landed in.
        //
        //  ⛔ AND IT IS RESERVED LIKE THE REASON LINE, NOT LIKE THE CAPTION. A
        //  sale card may drop its goods-per-dollar caption; it may never drop the
        //  line that states what the player is actually being charged. A big "30%
        //  OFF" sign with no second number beside it is a claim, not a price.
        // =====================================================================

        /// <summary>The sale proof line's block. One line at <see cref="FontCaption"/>.</summary>
        public static float SaleBlockPx => BlockPx(FontCaption, 1);

        /// <summary>What a sale costs a card in total: its block plus the gap above it.</summary>
        public static float SaleExtraPx => SaleBlockPx + BlockGapPx;

        // ── THE RIBBON, derived from its font exactly like the pill (FIX 4) ───
        //  ⛔ NEVER A LITERAL HEIGHT HERE. A 44 px plate around a 30 px font gave
        //  the pill a 28 px line box and TMP culled the label WHOLE — the badge
        //  drew zero glyphs on every surface. The ribbon carries the LOUDEST copy
        //  on the screen, so it is the last label that may vanish silently.

        /// <summary>The ribbon's font. Deliberately larger than the pill's: it is the "big sign".</summary>
        private const int FontSaleRibbon = 38;
        /// <summary>Breathing room inside the ribbon, per side.</summary>
        private const float RibbonPadXPx = 18f;
        private const float RibbonPadYPx = 6f;
        /// <summary>The ribbon's label box — one line at <see cref="FontSaleRibbon"/>.</summary>
        public static float RibbonTextBoxPx => BlockPx(FontSaleRibbon, 1);
        /// <summary>The ribbon plate: its label's own line box plus its padding.</summary>
        public static float RibbonHeightPx => RibbonTextBoxPx + 2f * RibbonPadYPx;
        /// <summary>The ribbon's x band as fractions of the CARD width.</summary>
        /// <remarks>
        /// ⛔ THIS BAND OVERLAPS THE PILL'S (0.26..0.96) AND THAT IS WHY THE TWO ARE MUTUALLY
        /// EXCLUSIVE, not why the band is narrow. Making them disjoint was tried on paper first and
        /// does not survive arithmetic: the pill's band starts at 0.26 because "BEST VALUE" measures
        /// 219 px bold at font 30 and needs 0.70 of the card (FIX 4's width budget), which leaves the
        /// ribbon 0.02..0.24 — 82 px on the narrowest shipped card (375 px), 46 px after padding, for
        /// copy that must read as the LOUDEST thing on the screen. Stacking them vertically fails the
        /// same way: the pill ends 60 px down and the COMPACT art well is only 101 px tall, so a
        /// 54 px ribbon beneath it overhangs the well by 13 px. So the card draws ONE top badge, by
        /// the ranking at the pill site.
        /// </remarks>
        private const float RibbonX0 = 0.02f;
        private const float RibbonX1 = 0.62f;
        /// <summary>Letter-spacing on the ribbon face, TMP 1/100 em.</summary>
        private const float RibbonLetterSpacing = 4f;

        // =====================================================================
        //  ⭐ THE RIBBON'S TWO COLOURS, AND WHY THEY ARE PUBLIC.
        // ---------------------------------------------------------------------
        //  The owner is red/green colourblind, so the ribbon may not rely on hue
        //  for a single thing it says. It carries FOUR non-colour carriers — the
        //  words ("30% OFF"), the tilt, the position (top-LEFT, where nothing else
        //  on this card sits) and the POLARITY: the state pill is a LIGHT plate
        //  with DARK ink, and this is its exact inverse. Desaturate the capture
        //  and the two badges remain unmistakable.
        //
        //  ⛔ PUBLIC BECAUSE THE CONTRAST IS AN ASSERTION, NOT AN OPINION. The
        //  regression computes the WCAG relative-luminance ratio of these two
        //  values and fails under 4.5:1. An oracle that re-typed the hex could
        //  not fail when this changed; one that reads the fields can.
        // =====================================================================

        /// <summary>The ribbon's fill — near-black, the card's own substrate pushed darker.</summary>
        public static readonly Color SaleRibbonFill = Hex(0x0A, 0x09, 0x0C);
        /// <summary>The ribbon's ink — parchment. Paired with the fill above at ~18:1.</summary>
        public static readonly Color SaleRibbonInk  = Hex(0xF7, 0xF1, 0xE1);

        // =====================================================================
        //  ⭐ THE "FLASH" — ONE PURE FUNCTION, SO IT IS PINNABLE WITHOUT PLAY MODE.
        // ---------------------------------------------------------------------
        //  Owner, 2026-09-16: "you know some flash" + "also make the packs pulse
        //  when the store opens".
        //
        //  ⛔ IT IS SCALE, NOT COLOUR, AND THAT IS THE RULING NOT A PREFERENCE. A
        //  hue-cycling or brightening badge conveys nothing to a red/green
        //  colourblind player and nothing at all in a greyscale capture. MOTION
        //  survives both, so the attention carrier is motion.
        //
        //  ⛔ AND THE CURVE LIVES HERE, NOT IN AN Update BODY. A tick that
        //  computes its own easing inline cannot be tested without a running
        //  player; this signature can be asserted at gate time (1 at t=0, the peak
        //  at the midpoint, 1 at the end, and FLAT at 1 forever after when it does
        //  not loop — which is what "undiscounted cards pulse once" MEANS).
        // =====================================================================

        /// <summary>
        /// The pulse's scale multiplier at <paramref name="t"/> seconds into a pulse of
        /// <paramref name="duration"/> seconds, peaking at <paramref name="peak"/>.
        /// <para>Half a sine: 1 at both ends, <paramref name="peak"/> at the midpoint — so a card
        /// always starts and finishes at its authored size and a pulse can never leave one
        /// permanently enlarged. Returns exactly 1 outside the window when <paramref name="loop"/>
        /// is false, and 1 for any non-positive duration.</para>
        /// </summary>
        public static float EvaluatePulse(float t, float duration, bool loop, float peak)
        {
            if (duration <= 0f) return 1f;
            if (loop) t = Mathf.Repeat(t, duration);
            else if (t <= 0f || t >= duration) return 1f;
            return 1f + (peak - 1f) * Mathf.Sin((t / duration) * Mathf.PI);
        }

        /// <summary>
        /// Wraps <paramref name="s"/> in TMP's strike-through markup, or returns empty for empty.
        /// <para>⛔ THE STRIKE IS THE GREYSCALE CARRIER FOR "this is the OLD price". A dimmer or
        /// differently-tinted anchor says nothing with the hue removed; a line drawn through digits
        /// says it in shape. Kept as one function so the oracle can assert the exact markup instead
        /// of matching a string built at four call sites.</para>
        /// </summary>
        public static string Strike(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : "<s>" + s + "</s>";

        /// <summary>Card heights per variant, reference px — DERIVED from the blocks above.</summary>
        public static float FeaturedHeightPx =>
            FeaturedArtPx + TextGapPx + NameBlockPx(StorePackCardVariant.Featured)
            + BlockGapPx + ContentsBlockPx(StorePackCardVariant.Featured) + BlockGapPx + CaptionBlockPx
            + PriceGapPx + PriceBlockPx(StorePackCardVariant.Featured) + BottomPadPx;

        public static float StandardHeightPx =>
            StandardArtPx + TextGapPx + NameBlockPx(StorePackCardVariant.Standard)
            + BlockGapPx + ContentsBlockPx(StorePackCardVariant.Standard)
            + PriceGapPx + PriceBlockPx(StorePackCardVariant.Standard) + BottomPadPx;

        public static float CompactHeightPx =>
            CompactArtPx + TextGapPx + NameBlockPx(StorePackCardVariant.Compact)
            + PriceGapPx + PriceBlockPx(StorePackCardVariant.Compact) + BottomPadPx;

        public static float LandscapeStandardHeightPx =>
            LandscapeStandardArtPx + TextGapPx + NameBlockPx(StorePackCardVariant.LandscapeStandard)
            + BlockGapPx + ContentsBlockPx(StorePackCardVariant.LandscapeStandard)
            + PriceGapPx + PriceBlockPx(StorePackCardVariant.LandscapeStandard) + BottomPadPx;

        /// <summary>Art-well heights per variant, reference px (§R3: standard 152, compact 101).</summary>
        public const float FeaturedArtPx = 228f;
        public const float StandardArtPx = 152f;
        public const float CompactArtPx  = 101f;
        public const float LandscapeStandardArtPx = 108f;

        /// <summary>Minimum authored card WIDTH. Two-up in the ~1058 px market column leaves
        /// ~500 each; this is the floor a third column would have to respect.</summary>
        public const float MinCardWidthPx = 300f;

        // Glow alphas (§R3). Named so the greyscale oracle can cite them.
        private const float GlowAlpha         = 0.08f;
        private const float GlowAlphaSelected = 0.16f;
        private const float ArtRadialAlpha    = 0.12f;
        private const float BorderAlpha       = 0.50f;

        /// <summary>How far the bloom bleeds OUTSIDE the card rect, reference px. The glow is a
        /// raycast-off decoration; it deliberately does not enlarge the tap target.</summary>
        private const float GlowBleedPx = 34f;

        // The dark vertical gradient behind every card (§R3): #1A1424 -> #110D19.
        // Cards share the public black-iron substrate. Band identity remains in the
        // labelled eyebrow, luminance-stepped rail, border and art bloom; flooding the
        // whole card violet made the Market read as a separate legacy UI family.
        private static readonly Color CardTop    = Hex(0x18, 0x17, 0x14);
        private static readonly Color CardBottom = Hex(0x0B, 0x0C, 0x0D);

        // Text floors, reference px. UI-001 §8 states the screen-px floors at 2340x1080
        // (legal >=30 / body >=40 / names >=44 / CTA price >=54); at the kit's scale 1.104
        // those are within a rounding of the same numbers in reference px, so the reference
        // values are used directly and every one of them clears ElarionUi.FontFloorMobile(30).
        private const int FontName    = 44;
        private const int FontBody    = 40;
        private const int FontCaption = 32;
        private const int FontPrice   = 54;
        private const int FontMinor   = 32;
        private const int FontBadge   = 30;

        // =====================================================================
        //  ⭐ THE STATE / BADGE PILL IS DERIVED FROM ITS FONT (WO-1162 FIX 4).
        // ---------------------------------------------------------------------
        //  WHAT WAS WRONG, in arithmetic anyone can redo: the pill was authored
        //  44 px tall and its label was a CentredText, which insets 8 px on every
        //  side. 44 - 2*8 = a 28 px BOX FOR A 30 px FONT. TMP's Ellipsis overflow
        //  CULLS THE WHOLE LINE when the line box does not seat in the rect (the
        //  mechanism ElarionUiKit's UiKitTextFitGuard header records), and
        //  LiberationSans' line box is 98.89/86 = 1.15 em, i.e. 34.5 px at font
        //  30. 34.5 > 28, so the pill drew ZERO GLYPHS - on every surface, in
        //  both states. "BEST VALUE" was invisible, and so was "Owned", which is
        //  the serious half: a player was shown NOTHING to say they already own
        //  the pack they are being invited to buy.
        //
        //  So the box is now derived from the font and the font is never touched:
        //  text box = BlockPx(FontBadge, 1) = ceil(30 * 1.25) = 38 px, which
        //  clears the 34.5 px line by 10%, and the pill is that plus its own
        //  padding. Nothing here may be "fixed" by lowering FontBadge - 30 IS
        //  ElarionUi.FontFloorMobile, and there is nothing under it.
        //
        //  WIDTH IS THE SAME DEFECT LYING DOWN. The old band was x 0.42..0.96 of
        //  the card (0.54) less 2*8 px of CentredText inset. On the narrowest
        //  shipped shelf card (375 px at 1920x1080) that is a 186 px box, and
        //  "BEST VALUE" MEASURES 219 px bold at font 30 with characterSpacing 4 -
        //  so the moment the height was fixed, the width would have truncated it
        //  instead. The band is widened to 0.26..0.96 (0.70) and the letter-spacing
        //  halved, which gives 234 px at that same card against 213 px of text: a
        //  10% margin at the WORST of the five measured surfaces, and 34% at the
        //  best. Overlong authored copy (founders-vow's ruled SENTENCE) still
        //  degrades through FitSingleLine's ellipsis - which is a readable
        //  shortening, not an invisible label.
        // =====================================================================

        /// <summary>Horizontal breathing room inside the pill, per side.
        /// <para>PUBLIC for the same reason <see cref="PillTextBoxPx"/> is: the pill's WIDTH budget
        /// is arithmetic over this number, and WO-1409 shipped a truncated badge precisely because a
        /// caller narrowed the pill without it. An oracle that re-typed 14 here could not fail when
        /// this changed; one that reads it can (NightMarketNoWalletRegression).</para></summary>
        public const float PillPadXPx = 14f;
        /// <summary>Vertical breathing room inside the pill, per side.</summary>
        private const float PillPadYPx = 5f;
        /// <summary>Letter-spacing on the pill face, in TMP's 1/100 em. Halved from 4 as part of
        /// the width budget above: at font 30 each unit costs 0.3 px per character.</summary>
        private const float PillLetterSpacing = 2f;
        /// <summary>The pill's x band as fractions of the CARD width.</summary>
        private const float PillX0 = 0.26f;
        private const float PillX1 = 0.96f;

        /// <summary>The pill's LABEL box - one line at <see cref="FontBadge"/>. The pill is sized
        /// from this; this is never sized from the pill.</summary>
        public static float PillTextBoxPx => BlockPx(FontBadge, 1);

        /// <summary>The pill itself: its label's own line box plus its padding.</summary>
        public static float PillHeightPx => PillTextBoxPx + 2f * PillPadYPx;

        // =====================================================================
        //  ⭐ THE PRICE ROW'S X BAND (WO-1162 FIX 4).
        // ---------------------------------------------------------------------
        //  The major price took x 0.06..0.62 ALWAYS, because the fiat minor sat
        //  at 0.64..0.94 - even on the cards that have no fiat minor to draw. The
        //  card was reserving a lane for a string it was not rendering, and the
        //  bill was paid by the one string the owner is looking at right now:
        //  with no server quote every card prints "Price unavailable", which
        //  MEASURES 267 px bold at the font floor and got 0.56 * card =
        //  210 px at 1920x1080, 218 px notched, 251 px at 2340x1080. It clipped -
        //  the player reads a cut-off sentence where the price belongs.
        //
        //  The fix is to give the lane the width it already had lying idle: when
        //  PriceMinor is empty the major spans the WHOLE row, 0.06..0.94. That is
        //  330 px at the narrowest measured card and 413 px at the widest, against
        //  267 px of text - a 23% margin at the worst surface, with the font
        //  untouched at its authored 54 / floor 30. The string is NOT shortened:
        //  "Price unavailable" is a REQUIRED state sentence and re-wording it to
        //  fit a lane we were wasting would be fixing the measurement instead of
        //  the card.
        // =====================================================================

        /// <summary>Left edge of the price row, as a fraction of the card width.</summary>
        private const float PriceRowX0 = 0.06f;
        /// <summary>Right edge of the price row.</summary>
        private const float PriceRowX1 = 0.94f;
        /// <summary>Where the MAJOR stops when a fiat minor shares the lane with it.</summary>
        private const float PriceMajorX1WithMinor = 0.62f;
        /// <summary>Where the fiat minor starts. Disjoint from the major's band by construction.</summary>
        private const float PriceMinorX0 = 0.64f;

        /// <summary>Art-well height for a variant, reference px.</summary>
        public static float ArtWellHeight(StorePackCardVariant v) =>
            v == StorePackCardVariant.Featured ? FeaturedArtPx :
            v == StorePackCardVariant.Compact  ? CompactArtPx  :
            v == StorePackCardVariant.LandscapeStandard ? LandscapeStandardArtPx : StandardArtPx;

        /// <summary>Card height for a variant, reference px. Buyable card — no reason line.</summary>
        public static float CardHeight(StorePackCardVariant v) =>
            v == StorePackCardVariant.Featured ? FeaturedHeightPx :
            v == StorePackCardVariant.Compact  ? CompactHeightPx  :
            v == StorePackCardVariant.LandscapeStandard ? LandscapeStandardHeightPx : StandardHeightPx;

        /// <summary>
        /// Card height for a variant that may carry the not-sellable state line.
        /// <para>⛔ THE ROW MUST ASK THIS, NOT <see cref="CardHeight(StorePackCardVariant)"/>. The
        /// shelf strip authors ONE height for the whole row and force-expands its children to it, so
        /// a row sized for buyable cards that then holds a card with a reason block would compress
        /// the taller card -- the same two-places-hold-one-measurement defect that put a 168-unit
        /// card in a 100-unit row (see PackStore.BuildCardRow).</para>
        /// </summary>
        public static float CardHeight(StorePackCardVariant v, bool hasNotSellableReason) =>
            CardHeight(v, hasNotSellableReason, false);

        /// <summary>
        /// Card height for a variant that may carry the not-sellable state line AND/OR the sale
        /// proof line. The two blocks are INDEPENDENT and a card can legitimately carry both.
        /// <para>⛔ THE SHELF STRIP MUST ASK THIS ONE. Same mechanism as the reason block: the row
        /// authors ONE height and force-expands its children to it, so a row measured before the
        /// sale is resolved would squeeze the sale line back out of the taller card. The 2-arg
        /// overload above is kept so every existing caller keeps its exact previous answer.</para>
        /// </summary>
        public static float CardHeight(StorePackCardVariant v, bool hasNotSellableReason, bool hasSale) =>
            CardHeight(v) + (hasNotSellableReason ? ReasonExtraPx : 0f) + (hasSale ? SaleExtraPx : 0f);

        /// <summary>
        /// Build one card under <paramref name="parent"/>.
        /// <para>The WHOLE CARD is the tap target (§R3) — <paramref name="onTap"/> is wired to the
        /// root Button and nothing inside the card is separately tappable, so Assert B cannot find
        /// two interactive rects in one place here.</para>
        /// <para>Never throws on missing data: an absent sprite falls back to ASCII initials, an
        /// absent caption is DRAWN ABSENT rather than invented, and a failed sprite build skips the
        /// bloom instead of painting a white quad.</para>
        /// </summary>
        public static StorePackCardHandle Build(Transform parent, StorePackCardModel model,
                                                StorePackCardVariant variant, Action onTap)
        {
            var handle = new StorePackCardHandle();
            if (parent == null) return handle;

            Color light  = NightMarketPalette.For(model.Band);
            Color accent = NightMarketPalette.ParseTint(model.OrbTint, light);
            string reason = Ascii(model.NotSellableReason);
            bool hasReason = !string.IsNullOrEmpty(reason);
            // ⛔ THE RIBBON IS THE GATE FOR BOTH SALE ELEMENTS. A sale LINE with no ribbon would be
            // a struck price with nothing explaining it, and the caller's fail-closed resolver
            // returns both or neither — so the card reads the badge and never second-guesses it.
            string saleBadge = Ascii(model.SaleBadge);
            bool hasRibbon = !string.IsNullOrEmpty(saleBadge);
            // ⛔ THE BLOCK IS BOUGHT BY THE LINE, NOT BY THE RIBBON, AND THEY ARE NOT THE SAME
            // QUESTION. The ribbon is an overlay and costs no height; the LINE is the thing the card
            // grows for. The server can legitimately send a sale bps with no anchor and no end
            // instant — a badge with nothing left to state — and a card that grew SaleExtraPx for an
            // empty string would carry a band of dead space under its price for no reason.
            bool hasSaleLine = hasRibbon && !string.IsNullOrEmpty(model.SaleLine);
            handle.OnSale = hasRibbon;
            float cardH  = CardHeight(variant, hasReason, hasSaleLine);
            float artH   = ArtWellHeight(variant);

            // ── ROOT ─────────────────────────────────────────────────────────
            // LayoutElement carries the AUTHORED height so a vertical layout group in the market
            // column cannot negotiate it below the floor. flexibleWidth shares the row.
            var rootGo = new GameObject("packcard-" + (model.Sku ?? "unknown"),
                typeof(RectTransform), typeof(LayoutElement), typeof(Button), typeof(Image));
            rootGo.transform.SetParent(parent, false);
            handle.Root = rootGo;

            var le = rootGo.GetComponent<LayoutElement>();
            le.minHeight = cardH;
            le.preferredHeight = cardH;
            le.minWidth = MinCardWidthPx;
            le.flexibleWidth = 1f;

            // The root Image is the raycast surface AND the card's body fill in one object: a
            // separate transparent hit-plate would be a second interactive rect stacked on the
            // visible one, which Assert B correctly reports as an overlap.
            var body = rootGo.GetComponent<Image>();
            body.color = CardBottom;
            ElarionUiKit.ApplyRounded(body, CornerRadiusPx);
            body.raycastTarget = true;

            var card = rootGo.transform;

            // ── GLOW — behind everything, bleeding outside the rect ──────────
            handle.Glow = AddGlow(card, accent, model.Selected ? GlowAlphaSelected : GlowAlpha);

            // ── The dark vertical gradient (top half lightened toward #1A1424) ─
            // Two flat rounded plates rather than a gradient texture: the asset bill is two
            // sprites and a third texture for a background ramp is exactly the drift §R3 bans.
            var topHalf = Plate(card, CardTop, new Vector2(0f, 0.45f), new Vector2(1f, 1f));
            if (topHalf != null) topHalf.color = new Color(CardTop.r, CardTop.g, CardTop.b, 0.85f);

            // ── ART WELL, ON TOP (ruling 4) ──────────────────────────────────
            BuildArtWell(card, model, accent, artH, cardH, variant);

            // ── 1 px border in the band colour at .5 alpha (§R3) ─────────────
            handle.Ring = AddRing(card, new Color(accent.r, accent.g, accent.b, BorderAlpha), 1f);

            // Selected also gets a 1 px INSET ring — a second, brighter carrier so selection is
            // never the bloom alone (the bloom is the first thing a greyscale read loses).
            if (model.Selected)
                AddRing(card, new Color(1f, 1f, 1f, 0.30f), 1f, inset: 5f);

            // ── TEXT STACK, BENEATH THE ART (ruling 4) ───────────────────────
            // Anchored from the TOP in reference px so each row's height is the authored number,
            // not a share of whatever the parent turned out to be.
            //
            // ⛔ AND IT IS SPENT AGAINST A BUDGET (WO-1162 FIX 2). Every block below is drawn only
            // if the space between the art well and the PRICE ROW'S OWN LANE still holds it. The
            // price row is bottom-pinned and reserved FIRST, so no name length, no two-line badge
            // and no long content summary can ever reach into it — that overlap is what shipped
            // (see the budget header above), and "the price must never be clipped" is an acceptance
            // criterion, not a preference.
            float nameFont = NameFont(variant);
            float bodyFont = BodyFont(variant);
            float priceFont = PriceFont(variant);
            float minorFont = MinorFont(variant);
            float contentsBlock = ContentsBlockPx(variant);
            float priceBlock = PriceBlockPx(variant);
            float y = artH + TextGapPx;
            float priceLaneTop = cardH - (BottomPadPx + priceBlock);
            // ⛔ THE SALE LINE IS RESERVED WITH THE PRICE LANE TOO, AND FORGETTING THIS IS THE WHOLE
            // BUG FIX 2 DOCUMENTS. The card's derived height grew by SaleExtraPx, but the TEXT STACK
            // measures its budget DOWN FROM the price lane — so a stack that still measured to
            // `priceLaneTop` would spend the very pixels the sale line was authored into and draw
            // the contents block straight through "was $4.99  now $2.99". The stack's floor is
            // therefore the SALE line's top whenever there is one, not the price lane's.
            //
            //   sale line occupies:  BottomPadPx + priceBlock + BlockGapPx .. + SaleBlockPx
            //   so it costs exactly: BlockGapPx + SaleBlockPx == SaleExtraPx   (the height added)
            float saleTop = priceLaneTop - BlockGapPx - SaleBlockPx;
            float stackFloor = hasSaleLine ? saleTop : priceLaneTop;
            // ⛔ THE REASON LINE IS RESERVED WITH THE PRICE LANE, NOT SPENT WITH THE OPTIONALS. It
            // is REQUIRED whenever it exists: the caller only sets it on a card whose buy control is
            // dead, and a dead control with no words is exactly the state the owner cannot read.
            float reasonTop = stackFloor - PriceGapPx - ReasonBlockPx;
            float budget = (hasReason ? reasonTop - BlockGapPx : stackFloor - PriceGapPx) - y;

            // ── NAME — REQUIRED ──────────────────────────────────────────────
            // It takes its full block, or every remaining pixel if the budget is somehow tighter
            // than the block (which the derived heights make unreachable; this is the guard, not
            // the plan). FitBlock then auto-sizes the words down INSIDE that rect.
            float nameH = Mathf.Min(NameBlockPx(variant), Mathf.Max(0f, budget));
            var name = TopAnchoredText(card, model.Name, Mathf.RoundToInt(nameFont), ElarionUi.Parchment,
                FontStyles.Bold, TextAlignmentOptions.TopLeft, y, nameH);
            if (name != null)
            {
                name.textWrappingMode = TextWrappingModes.Normal;   // names WRAP (§5)
                // FitBlock is the kit's own overflow protection: auto-size DOWN inside
                // [FontFloorMobile .. FontName] and only then truncate. Never a raw Overflow, which
                // would draw the name outside its rect and onto the card above it -- the exact
                // class of spill AuditGeometry rule 1 exists to catch.
                ElarionUiKit.FitBlock(name, ElarionUi.FontFloorMobile, nameFont);
            }
            y += nameH + BlockGapPx;
            budget -= nameH + BlockGapPx;

            if (variant != StorePackCardVariant.Compact)
            {
                // ── CONTENTS — REQUIRED on the priced variants ───────────────
                // It may compress to a single line box before it is dropped; below one line box
                // there is nothing honest to draw and the block is omitted rather than clipped.
                float oneLine = BlockPx(bodyFont, 1);
                float contentsH = Mathf.Min(contentsBlock, Mathf.Max(0f, budget));
                if (contentsH >= oneLine)
                {
                    var contents = TopAnchoredText(card, model.Contents, Mathf.RoundToInt(bodyFont),
                        new Color(0.90f, 0.93f, 0.98f, 1f), FontStyles.Normal,
                        TextAlignmentOptions.TopLeft, y, contentsH);
                    if (contents != null)
                    {
                        contents.textWrappingMode = TextWrappingModes.Normal;
                        float contentsFloor = variant == StorePackCardVariant.LandscapeStandard
                            ? 20f : ElarionUi.FontFloorMobile;
                        ElarionUiKit.FitBlock(contents, contentsFloor, bodyFont);
                    }
                    y += contentsH + BlockGapPx;
                    budget -= contentsH + BlockGapPx;
                }
                else if (!string.IsNullOrEmpty(model.Contents))
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Store",
                        "StorePackCard '" + (model.Sku ?? "?") + "': no line box left for the contents " +
                        "summary in a " + variant + " card (" + cardH.ToString("0") + "px) - it is DROPPED " +
                        "rather than drawn into the price lane.");
                }

                // ── VALUE CAPTION — OPTIONAL, AND THE FIRST THING TO GO ──────
                // ABSENT when the caller could not compute it (an invented value line is the
                // claim-the-arithmetic-does-not-support defect, §7) AND absent when the card has no
                // room left. Dropping a nice-to-have is correct; drawing it 62 px below the card's
                // own bottom edge, which is what shipped, is not.
                if (!string.IsNullOrEmpty(model.ValueCaption))
                {
                    if (budget >= CaptionBlockPx)
                        TopAnchoredText(card, model.ValueCaption, FontCaption, ElarionUi.ParchmentDim,
                            FontStyles.Italic, TextAlignmentOptions.TopLeft, y, CaptionBlockPx);
                    else
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Store",
                            "StorePackCard '" + (model.Sku ?? "?") + "': value caption dropped - " +
                            budget.ToString("0") + "px left, block needs " + CaptionBlockPx.ToString("0") +
                            "px. Required price and state are unaffected.");
                }
            }

            // ── NOT-SELLABLE STATE LINE — REQUIRED WHEN PRESENT ──────────────
            // Its own block, directly above the price gap, in the text lane (0.06..0.94) and NEVER
            // in the price row's measured x bands. The price still prints beneath it in full: the
            // whole point of the public price list is that the shelf now HAS digits where it used to
            // read "Price unavailable", so hiding them here would undo the change that paid for it.
            if (hasReason)
            {
                handle.ReasonLabel = TopAnchoredText(card, reason, FontCaption, ElarionUi.Parchment,
                    FontStyles.Normal, TextAlignmentOptions.TopLeft, reasonTop, ReasonBlockPx);
                if (handle.ReasonLabel != null)
                {
                    handle.ReasonLabel.textWrappingMode = TextWrappingModes.Normal;
                    ElarionUiKit.FitBlock(handle.ReasonLabel, ElarionUi.FontFloorMobile, FontCaption);
                }
                DeNelle.Core.Diagnostics.FlowTrace.Step("Store",
                    "StorePackCard '" + (model.Sku ?? "?") + "': NOT sellable - price is still drawn, " +
                    "the state line reads \"" + reason + "\", and the card carries no buy control.");
            }

            // ── PRICE ROW, pinned to the BOTTOM ──────────────────────────────
            // Bottom-pinned, so a two-line name above can never push the price out of the card —
            // "quantities and currency must never be clipped" is an acceptance criterion, and the
            // 2026-08-22 frames failed it by showing "20 SKR" for a 120 SKR pack.
            // ⛔ THE MAJOR TAKES THE WHOLE ROW WHEN THERE IS NO MINOR TO SHARE IT WITH. Reserving
            // the fiat band on a card that draws no fiat is how "Price unavailable" - the string
            // EVERY card prints while the store has no server quote - got 210px for 267px of text.
            bool priceUnavailable = string.Equals(model.PriceMajor, "Price unavailable",
                System.StringComparison.OrdinalIgnoreCase);
            // A floating fiat reference beside an unavailable authoritative quote creates
            // the broken-looking "Price una... ~ $19..." row. Preserve the required state
            // sentence at full width; the estimate returns when a quote is actually live.
            bool hasMinor = !priceUnavailable && !string.IsNullOrEmpty(model.PriceMinor);
            float priceMajorX1 = hasMinor ? PriceMajorX1WithMinor : PriceRowX1;
            handle.PriceLabel = BottomAnchoredText(card, model.PriceMajor, Mathf.RoundToInt(priceFont), ElarionUi.Gilt,
                FontStyles.Bold, TextAlignmentOptions.BottomLeft, BottomPadPx, priceBlock,
                PriceRowX0, priceMajorX1);
            // ⛔ THE PRICE IS THE ONE STRING ON THIS SCREEN THAT MUST NOT CLIP. On 2026-08-22 the
            // owner's device showed "20 SKR" for a 120 SKR pack and "6 SKR" for a 36 SKR pack --
            // the leading digit occluded. FitSingleLine shrinks toward the floor before it would
            // ever truncate, so the digits survive a narrower column.
            if (handle.PriceLabel != null)
                ElarionUiKit.FitSingleLine(handle.PriceLabel, ElarionUi.FontFloorMobile, priceFont);
            if (hasMinor)
            {
                // ⛔ THE MINOR REFERENCE SHARES THE PRICE LANE, IT DOES NOT SIT ABOVE IT. It used to
                // be bottom-offset 18 in a 44 px box while the major was 14/62 - two overlapping
                // boxes in one lane whose only separation was that they happened not to be wide
                // enough to meet. Same bottom pad, same block height, disjoint x bands.
                var minor = BottomAnchoredText(card, model.PriceMinor, Mathf.RoundToInt(minorFont), ElarionUi.Parchment,
                    FontStyles.Normal, TextAlignmentOptions.BottomRight, BottomPadPx, priceBlock,
                    PriceMinorX0, PriceRowX1);
                // The dollars FLOAT (WO-1158 section 5) but they still must not clip: a "$49.99"
                // that reads "$4" is the same defect as the occluded SKR digit, one currency over.
                if (minor != null)
                    ElarionUiKit.FitSingleLine(minor, ElarionUi.FontFloorMobile, minorFont);
            }

            // ── THE SALE PROOF LINE — its own lane, one block above the price ─
            // ⛔ FULL WIDTH (the price row's own 0.06..0.94 band) AND ONE BLOCK ABOVE THE PRICE ROW,
            // which is exactly the height this card grew by. It cannot collide with the price
            // beneath it (disjoint lanes, both bottom-pinned) and it cannot collide with the text
            // stack above it (that stack is top-anchored off the art well, and the card got TALLER,
            // so the slack between them grew rather than shrank).
            if (hasSaleLine)
            {
                handle.SaleLineLabel = BottomAnchoredText(card, model.SaleLine, FontCaption,
                    ElarionUi.Parchment, FontStyles.Bold, TextAlignmentOptions.BottomLeft,
                    BottomPadPx + priceBlock + BlockGapPx, SaleBlockPx, PriceRowX0, PriceRowX1);
                if (handle.SaleLineLabel != null)
                {
                    // ⛔ RICH TEXT ON, EXPLICITLY. The struck anchor IS the "<s>" markup, and this
                    // is the only label on the card that carries any. Relying on TMP's default
                    // would ship the tags as literal glyphs the day that default changes, which on
                    // this screen reads as "was <s>$4.99</s>" printed at the player.
                    handle.SaleLineLabel.richText = true;
                    handle.SaleLineLabel.textWrappingMode = TextWrappingModes.NoWrap;
                    // Shrinks toward the floor before it would truncate, same rule as the price:
                    // the old price and the new one are both money and neither may be cut.
                    ElarionUiKit.FitSingleLine(handle.SaleLineLabel, ElarionUi.FontFloorMobile, FontCaption);
                }
            }

            // ── THE ONE TOP BADGE — pill (top-right) OR sale ribbon (top-left) ─
            // The state WORD outranks the merchandising badge: "Owned" must never be hidden behind
            // "BEST VALUE", or the player is invited to buy what they already have.
            // ⛔ ONE TOP BADGE PER CARD, RANKED: STATE WORD > SALE RIBBON > MERCHANDISING BADGE.
            //
            // The state WORD still outranks everything, for the reason it always did: "Owned" must
            // never be hidden, and a player denied a purchase must be told in a word. What is NEW is
            // that a sale ribbon outranks the merchandising badge — a live discount is a stronger
            // reason to look than "BEST START", and two loud plates fighting over one rect is worse
            // than either alone, especially desaturated.
            //
            // ⚠ AND ALMOST NOTHING IS ACTUALLY LOST TO THIS. SaleQuoteFor already suppresses the sale
            // for OWNED and ANCHOR-ONLY packs, so the state words that can even reach this branch
            // beside a ribbon are "Not yet" — which the card ALSO prints in full as its reason line,
            // so no information goes missing — and "Your gap". For those two the state wins and the
            // ribbon stands down; the card still carries the struck anchor on its sale line, so the
            // discount is stated in digits either way. Only the big sign yields.
            bool stateOutranksRibbon = !string.IsNullOrEmpty(Ascii(model.StateWord));
            bool drawRibbon = hasRibbon && !stateOutranksRibbon;
            string pill = !string.IsNullOrEmpty(model.StateWord) ? model.StateWord
                        : (drawRibbon ? string.Empty : model.Badge);
            // ⚠ NOT uppercased here. The authored badges ARE already uppercase where the merchandiser
            // meant them to be ("BEST START"), and founders-vow's ruled replacement for its retired
            // FOMO copy is a SENTENCE -- "Founders are named on the Heart." -- which a forced
            // ToUpperInvariant would shout. Presentation must not overrule authored copy.
            if (!string.IsNullOrEmpty(pill) && variant != StorePackCardVariant.LandscapeStandard)
                handle.StateLabel = BuildPill(card, Ascii(pill), cardH, artH);

            // ── THE SALE RIBBON — the "big sign", top-LEFT over the art well ──
            // ⛔ UNCONDITIONAL ACROSS VARIANTS, unlike the pill above. BuildPill is SKIPPED on
            // LandscapeStandard, which is the variant PackStore.VariantFor(Basket) returns for the
            // shipped landscape shelf — a ribbon gated the same way would render NOWHERE on the very
            // cards the owner is looking at, with every gate green.
            if (drawRibbon) BuildSaleRibbon(card, saleBadge, handle);

            // ── THE WHOLE CARD IS THE TAP TARGET ─────────────────────────────
            var btn = rootGo.GetComponent<Button>();
            btn.targetGraphic = body;
            handle.Button = btn;
            if (onTap != null) btn.onClick.AddListener(() => onTap());

            // ⛔ A PROOF, NOT A RESCUE. Every variant is authored >= 228 px tall and >= 300 px wide,
            // so this call CANNOT fire. It is here so that if a future re-layout ever drops the card
            // under the floor, WO-1060's recorder names this exact control instead of the owner
            // finding a card grown over its neighbour on a device days later.
            ElarionUiKit.ClampMinTouch(btn);

            return handle;
        }

        /// <summary>Repaint selection without rebuilding the card (bloom + inset ring only).</summary>
        public static void SetSelected(StorePackCardHandle handle, bool selected)
        {
            if (handle == null || handle.Glow == null) return;
            var c = handle.Glow.color;
            handle.Glow.color = new Color(c.r, c.g, c.b, selected ? GlowAlphaSelected : GlowAlpha);
        }

        // =====================================================================
        //  Pieces
        // =====================================================================

        private static void BuildArtWell(Transform card, StorePackCardModel model, Color accent,
                                         float artH, float cardH, StorePackCardVariant variant)
        {
            float top = 1f - (artH / Mathf.Max(1f, cardH));

            var wellGo = new GameObject("ArtWell", typeof(RectTransform), typeof(Image));
            wellGo.transform.SetParent(card, false);
            var wrt = (RectTransform)wellGo.transform;
            wrt.anchorMin = new Vector2(0f, top); wrt.anchorMax = new Vector2(1f, 1f);
            wrt.offsetMin = new Vector2(4f, 0f); wrt.offsetMax = new Vector2(-4f, -4f);

            // ⛔ NEVER NEAR-BLACK (§4 / P1-7). The first delivery shipped embossed near-black wells
            // presented as product art; the rule is a DELIBERATE two-tone placeholder that reads as
            // "art has not landed yet", not as "this pack is empty". The two tones are derived from
            // the pack's own authored tint, so no per-pack texture and no per-SKU branch is needed.
            var well = wellGo.GetComponent<Image>();
            well.color = CardTop;
            ElarionUiKit.ApplyRounded(well, CornerRadiusPx);
            well.raycastTarget = false;

            // The second tone: a top-centre radial of the band colour (§R3, alpha .28). This is
            // sprite two of the two-sprite bill, tinted — not a gradient texture.
            var glow = ElarionUiKit.RadialGlowSprite;
            if (glow != null)
            {
                var g = new GameObject("ArtRadial", typeof(RectTransform), typeof(Image));
                g.transform.SetParent(wellGo.transform, false);
                var grt = (RectTransform)g.transform;
                grt.anchorMin = new Vector2(0.10f, 0.10f); grt.anchorMax = new Vector2(0.90f, 1.45f);
                grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
                var gi = g.GetComponent<Image>();
                gi.sprite = glow;
                gi.color = new Color(accent.r, accent.g, accent.b, ArtRadialAlpha);
                gi.raycastTarget = false;
            }

            // ── THE GLYPH — icon sprite, else ASCII initials. NEVER an emoji. ──
            Sprite icon = NightMarketArt.Load(model.ArtResource);
            if (model.GlyphConcepts != null && model.GlyphConcepts.Length > 0)
            {
                try { if (icon == null) icon = ConceptIconResolver.ResolveAny(model.GlyphConcepts); }
                catch (Exception e)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Store",
                        "StorePackCard: icon resolve threw for '" + (model.Sku ?? "?") + "' (" +
                        e.GetType().Name + "); falling back to ASCII initials.");
                }
            }

            if (icon != null)
            {
                var ig = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
                ig.transform.SetParent(wellGo.transform, false);
                var irt = (RectTransform)ig.transform;
                irt.anchorMin = new Vector2(0.04f, 0.04f); irt.anchorMax = new Vector2(0.96f, 0.96f);
                irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
                var ii = ig.GetComponent<Image>();
                ii.sprite = icon;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
            }
            else
            {
                var initials = Initials(model.Name);
                var t = CentredText(wellGo.transform, initials,
                    variant == StorePackCardVariant.Compact ? 56 : 84,
                    new Color(1f, 1f, 1f, 0.82f));
                if (t != null) t.characterSpacing = 6f;
            }

            // Bottom scrim so the name below never fights the well's brightest edge.
            var scrim = new GameObject("Scrim", typeof(RectTransform), typeof(Image));
            scrim.transform.SetParent(wellGo.transform, false);
            var srt = (RectTransform)scrim.transform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0.34f);
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            var si = scrim.GetComponent<Image>();
            si.color = new Color(CardBottom.r, CardBottom.g, CardBottom.b, 0.72f);
            si.raycastTarget = false;
        }

        private static TextMeshProUGUI BuildPill(Transform card, string text, float cardH, float artH)
        {
            float wellTop = 1f - (artH / Mathf.Max(1f, cardH));
            // ⛔ DERIVED FROM THE FONT, NEVER THE OTHER WAY ROUND. See the pill budget above: a
            // literal 44 here, minus a text child's 8px inset per side, gave a 30px font a 28px
            // box and TMP culled the line whole - the badge and, worse, "Owned" drew NOTHING.
            float pillH = PillHeightPx;

            var go = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(card, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(PillX0, 1f); rt.anchorMax = new Vector2(PillX1, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -12f);
            rt.sizeDelta = new Vector2(0f, pillH);

            var img = go.GetComponent<Image>();
            img.color = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.92f);
            ElarionUiKit.ApplyRounded(img, pillH * 0.5f);
            // ⛔ raycastTarget OFF. A badge is INFORMATION, not a control: leaving it tappable would
            // put a second interactive rect on top of the card's own, which is precisely what
            // WO-1060 Assert B reports as an overlap — and it would also swallow the card's tap.
            img.raycastTarget = false;

            // The label's own box is PillTextBoxPx by construction: the pill is pillH tall and the
            // inset below is PillPadYPx per side, so the line box the font needs is what is left.
            var t = CentredText(go.transform, text, FontBadge, new Color(0.08f, 0.06f, 0.03f, 1f),
                                PillPadXPx, PillPadYPx);
            if (t != null)
            {
                t.fontStyle = FontStyles.Bold;
                t.characterSpacing = PillLetterSpacing;
                ElarionUiKit.FitSingleLine(t, ElarionUi.FontFloorMobile, FontBadge);
            }
            // Unused wellTop kept out of the layout deliberately: the pill anchors to the CARD top,
            // not the well, so a variant change to the art height cannot move the badge off it.
            _ = wellTop;
            return t;
        }

        /// <summary>
        /// The storewide sale ribbon: a tilted, near-black rounded plate at the card's top-LEFT
        /// carrying the loudest copy on the screen (WO-1800).
        ///
        /// <para>⛔ IT IS NOT THE PILL AND IT MUST NOT REUSE IT. <see cref="BuildPill"/> is SKIPPED
        /// on <see cref="StorePackCardVariant.LandscapeStandard"/> — which is the variant the shipped
        /// landscape shelf draws its Basket band at (PackStore.VariantFor). A ribbon routed through
        /// the pill path would therefore render NOWHERE on the very cards the owner is looking at,
        /// while every gate stayed green. So this builder is unconditional across variants, and the
        /// two badges are separated on four axes: position (left vs right), tilt (diagonal vs level),
        /// polarity (dark plate / light ink vs light plate / dark ink) and their words.</para>
        ///
        /// <para>⛔ raycastTarget OFF on both plate and label, like the pill. A sale sign is
        /// INFORMATION; leaving it tappable would put a second interactive rect over the card's own
        /// (WO-1060 Assert B) and swallow the tap that moves the spotlight.</para>
        /// </summary>
        private static void BuildSaleRibbon(Transform card, string text, StorePackCardHandle handle)
        {
            if (card == null || string.IsNullOrEmpty(text) || handle == null) return;

            var go = new GameObject("SaleRibbon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(card, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(RibbonX0, 1f);
            rt.anchorMax = new Vector2(RibbonX1, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // Sits a little lower than the pill's -12 so the tilt's rising corner clears the card's
            // own rounded top edge instead of poking through it.
            rt.anchoredPosition = new Vector2(0f, -16f);
            rt.sizeDelta = new Vector2(0f, RibbonHeightPx);
            // ⛔ NOT ROTATED, AND THE TILT WAS REMOVED ON ARITHMETIC RATHER THAN ON TASTE. A -9 deg
            // rotation about a (0.5, 1) pivot LIFTS the plate's left end by halfWidth * sin(9 deg) —
            // ~23 px on a 500 px card — against a top offset of only 16, so the corner leaves the
            // card root and the capture harness's containment audit is right to report it. The brief
            // allows either carrier ("a diagonal ribbon OR a bold rounded tag"); the tag is the one
            // that is containable at every measured card width by construction, which matters more
            // than the slant. The shape carriers that remain are the tag's WEIGHT, its corner radius,
            // its font (38 vs the pill's 30) and its polarity — see the colour block above.

            var img = go.GetComponent<Image>();
            img.color = SaleRibbonFill;
            ElarionUiKit.ApplyRounded(img, RibbonHeightPx * 0.28f);   // bold rounded tag, not a capsule
            img.raycastTarget = false;
            handle.SaleRibbon = img;

            // The label's box is RibbonTextBoxPx by construction — the plate is RibbonHeightPx and
            // the inset is RibbonPadYPx per side. Never a literal here: see FIX 4's arithmetic, in
            // which a hardcoded inset gave a 30px font a 28px box and TMP culled the line whole.
            var t = CentredText(go.transform, text, FontSaleRibbon, SaleRibbonInk,
                                RibbonPadXPx, RibbonPadYPx);
            if (t != null)
            {
                t.fontStyle = FontStyles.Bold;
                t.characterSpacing = RibbonLetterSpacing;
                ElarionUiKit.FitSingleLine(t, ElarionUi.FontFloorMobile, FontSaleRibbon);
            }
            handle.SaleRibbonLabel = t;
        }

        private static Image AddGlow(Transform card, Color accent, float alpha)
        {
            var sprite = ElarionUiKit.RadialGlowSprite;
            if (sprite == null) return null;      // no sprite -> no bloom; NEVER a white quad

            var go = new GameObject("Glow", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(card, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-GlowBleedPx, -GlowBleedPx);
            rt.offsetMax = new Vector2(GlowBleedPx, GlowBleedPx);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = new Color(accent.r, accent.g, accent.b, alpha);
            img.raycastTarget = false;            // decoration never eats a tap
            go.transform.SetAsFirstSibling();
            return img;
        }

        private static Image AddRing(Transform card, Color color, float thicknessPx, float inset = 0f)
        {
            // The ring is the ROUNDED sprite with fillCenter OFF — no third sprite (§R3 asset bill).
            var go = new GameObject(inset > 0f ? "RingInset" : "Ring", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(card, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
            var img = go.GetComponent<Image>();
            ElarionUiKit.ApplyRounded(img, CornerRadiusPx);
            if (img.sprite == null) { img.enabled = false; return img; }   // no white slab fallback
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            img.pixelsPerUnitMultiplier = thicknessPx > 0f ? 6f / thicknessPx : 1f;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Image Plate(Transform parent, Color color, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            ElarionUiKit.ApplyRounded(img, CornerRadiusPx);
            img.raycastTarget = false;
            return img;
        }

        // ── Text helpers ─────────────────────────────────────────────────────
        // All three assign a font BEFORE .text (EnsureFont) — a code-built TMP whose font is
        // unresolved at its first GenerateTextMesh NREs deep inside TMP, and force-built capture
        // panels hit that timing edge every run.

        private static TextMeshProUGUI TopAnchoredText(Transform parent, string text, int size,
            Color color, FontStyles style, TextAlignmentOptions align, float topOffsetPx, float heightPx)
        {
            var t = NewText(parent, text, size, color, style, align);
            if (t == null) return null;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.06f, 1f); rt.anchorMax = new Vector2(0.94f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topOffsetPx);
            rt.sizeDelta = new Vector2(0f, heightPx);
            return t;
        }

        private static TextMeshProUGUI BottomAnchoredText(Transform parent, string text, int size,
            Color color, FontStyles style, TextAlignmentOptions align, float bottomOffsetPx,
            float heightPx, float x0, float x1)
        {
            var t = NewText(parent, text, size, color, style, align);
            if (t == null) return null;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(x0, 0f); rt.anchorMax = new Vector2(x1, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, bottomOffsetPx);
            rt.sizeDelta = new Vector2(0f, heightPx);
            return t;
        }

        /// <summary>
        /// A centred label filling its parent, inset by <paramref name="padX"/> / <paramref name="padY"/>.
        /// ⛔ THE PADDING IS A PARAMETER BECAUSE IT IS PART OF A LINE-BOX BUDGET. A hardcoded 8px
        /// inset here is what turned a 44px pill into a 28px box for a 30px font and culled the
        /// badge - callers that size their host FROM the font must be able to say so.
        /// </summary>
        private static TextMeshProUGUI CentredText(Transform parent, string text, int size, Color color,
                                                   float padX = 8f, float padY = 8f)
        {
            var t = NewText(parent, text, size, color, FontStyles.Bold, TextAlignmentOptions.Center);
            if (t == null) return null;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY); rt.offsetMax = new Vector2(-padX, -padY);
            return t;
        }

        private static TextMeshProUGUI NewText(Transform parent, string text, int size, Color color,
                                               FontStyles style, TextAlignmentOptions align)
        {
            if (parent == null) return null;
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);                 // font BEFORE .text (TMP first-generation NRE)
            t.text = Ascii(text);
            t.fontSize = Mathf.Max(size, (int)ElarionUi.FontFloorMobile);   // never below the mobile floor
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;                    // text is never a tap target inside the card
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        // ── ASCII + colour utilities ─────────────────────────────────────────

        /// <summary>
        /// Strips every non-ASCII code point. TMP's shipped font renders anything outside ASCII as
        /// a TOFU BOX, and the one screen that must never look broken is the one that takes money —
        /// so the guard is here, at the single place card text is set, rather than trusted to every
        /// caller and every future packs.json edit.
        /// </summary>
        private static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            bool clean = true;
            for (int i = 0; i < s.Length; i++) { if (s[i] > 126 || s[i] < 9) { clean = false; break; } }
            if (clean) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 32 && c <= 126) sb.Append(c);
                else if (c == '\n' || c == '\t') sb.Append(c);
                // NUMERIC code points, never literal glyphs: this source file stays ASCII-clean
                // itself, so no editor / mount round-trip (CLAUDE.md 0) can re-encode the very
                // characters this method exists to replace.
                else if (c == 0x2019 || c == 0x2018) sb.Append('\'');        // curly quotes
                else if (c == 0x201C || c == 0x201D) sb.Append('"');
                else if (c == 0x2013 || c == 0x2014) sb.Append('-');         // en / em dash
                else if (c == 0x2026) sb.Append("...");                      // ellipsis
                else if (c == 0x00A0) sb.Append(' ');                        // nbsp
                // anything else is DROPPED: a missing character reads as a typo, a tofu box reads
                // as a broken build, and on this screen that difference is money.
            }
            return sb.ToString();
        }

        /// <summary>Two-letter ASCII initials for the placeholder art well ("Hearth Spark" -> "HS").</summary>
        private static string Initials(string name)
        {
            string a = Ascii(name);
            if (string.IsNullOrEmpty(a)) return "??";
            var sb = new StringBuilder(2);
            bool atWordStart = true;
            for (int i = 0; i < a.Length && sb.Length < 2; i++)
            {
                char c = a[i];
                if (c == ' ' || c == '\'' || c == '-') { atWordStart = true; continue; }
                if (atWordStart && char.IsLetterOrDigit(c)) { sb.Append(char.ToUpperInvariant(c)); atWordStart = false; }
            }
            if (sb.Length == 0) sb.Append('?');
            return sb.ToString();
        }

        private static Color Darken(Color c, float toward)
        {
            return new Color(Mathf.Lerp(c.r, 0.06f, 1f - toward),
                             Mathf.Lerp(c.g, 0.05f, 1f - toward),
                             Mathf.Lerp(c.b, 0.10f, 1f - toward), 1f);
        }

        private static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
}
