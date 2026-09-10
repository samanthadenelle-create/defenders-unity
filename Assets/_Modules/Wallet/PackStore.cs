// =============================================================================
// PackStore — THE NIGHT MARKET (WO-1050). The pack store UI + purchase flow.
// -----------------------------------------------------------------------------
// WHAT THIS SCREEN IS NOW. Two columns inside the SAME Obsidian modal:
//
//   ┌ header ──────────────────────────────────────────────────────┐
//   │ The Night Market                     [ your wallet: N SKR ]  │
//   ├───────────────┬──────────────────────────────────────────────┤
//   │ SPOTLIGHT     │ SHELF (scroll; four bands, FIXED order)      │
//   │  aurora       │  ▌FREE TONIGHT      redeem a code           │
//   │  orb/name     │  ▌CLOSE THE GAP     wagon · crate · cart    │
//   │  bar ledger   │  ▌GET THE HEART MOVING  spark · hand · ...  │
//   │  comparison   │  ▌PATRONAGE         patron · founder        │
//   │  CTA          │                                             │
//   ├───────────────┴──────────────────────────────────────────────┤
//   │ 0% STORE FEE · REWARDS DISTRIBUTOR … · TIME AND BEAUTY …     │
//   │      You are never required to spend anything. Ever.         │
//   └──────────────────────────────────────────────────────────────┘
//
// ⛔ THIS PASS IS PRESENTATION ONLY, AND THAT IS LOAD-BEARING, NOT A DISCLAIMER.
// Untouched, in shape and in behaviour: Purchase(), WalletService.Pay,
// PackStoreVM.ApplyPackContents, the PackPurchased event, the analytics calls, and
// EVERY refusal — PurchaseGate.CanBuy is still consulted by BOTH the CTA builder and
// the charge path, so the button and the charge can never disagree.
// FeatureFlags.RealmStorePurchase stays defaultOn:false and is NOT read here.
// With the rail closed the ENTIRE screen still renders — bands, spotlight, ledger,
// trust strip — with "Coming soon" in the CTA slot and no tappable Buy anywhere.
//
// ⛔ THE HONESTY RULE THIS FILE IS WRITTEN TO. The shelf may only advertise what the
// grant seam actually pays. The bar ledger is sourced from PackCatalog.
// LedgerEconomyKeys — the same keys ApplyPackContents routes — so a good that IS
// granted can never be un-drawn and a good that is NOT granted can never appear. The
// comparison line is PURE ARITHMETIC over two real SKUs or it is ABSENT. The wallet
// chip is a READ-ONLY MIRROR of the player's own wallet and never implies the game
// holds SKR. Every one of those is the same rule that got keepers-satchel hidden.
//
// PROVENANCE (CLAUDE.md — Grok drafts, UI/CLI refine, CLI implements): the shape of
// this screen came from a GROK SUGGESTION in WO-1050. What was refined and why is
// recorded in that work order's "CLI refinement" section — the short list is: no new
// SKU was minted, no row ships anchorOnly, the fiat half of the chip is quote-backed
// or absent, the shelf is two cards to a row (the modal is 0.325–0.675 of the screen,
// not full-bleed), the free band carries the promo door only, and the patronage sheen
// sits on the band head rather than on each card.
//
// Landscape only; code-built uGUI (UXML does not work in player builds).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;      // CurrencySkinResolver.WalletConnectionChanged - the connect seam
using DeNelle.Core.Payments;
using DeNelle.Core.Promo;
using DeNelle.Core.Web3;
using DeNelle.Commerce;    // WO-1282 - StoreFocusRequest, the rail-neutral focus latch
// WO-1188 - the confirmation screen reports what ARRIVED, so it reads the ONE authoritative
// wallet total (TownBankCapacity.CurrentOf) before and after the grant and prints the DELTA.
// Aliased rather than a bare `using DeNelle.Core.Economy;` so this file cannot pick up a second
// meaning for any of its existing type names.
using Bank = DeNelle.Core.Economy.TownBankCapacity;
using BankRes = DeNelle.Core.Economy.BankResource;

namespace DeNelle.Wallet
{
    /// <summary>
    /// The pack-store controller. Builds a banded shelf + a spotlight from <see cref="PackCatalog"/>,
    /// drives the SKR purchase flow through <see cref="WalletService"/>, and applies confirmed pack
    /// contents to <see cref="GameStateService"/> via <see cref="PackStoreVM"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PackStore : MonoBehaviour
    {
        [Tooltip("Default currency rail a pack is bought in. SOL / USDC / SKR.")]
        [SerializeField] private CurrencyKind _defaultCurrency = CurrencyKind.Skr;

        private WalletService _wallet;

        // WO-744 MVVM: game-state ownership + entitlement grant + the interactor close-resolve
        // live in the VM so this View never names GameStateService / FindFirstObjectOfType. The
        // money path (WalletService.Pay orchestration) stays here; it asks the VM to read/apply state.
        private PackStoreVM _vm;

        // Modal-arbiter handle (WO-F door): registers with PanelManager so the
        // Realm Store obeys the one-panel-at-a-time rule AND so PanelRouter.Open's
        // post-open VerifyOpenedVisible sees a panel actually recorded open.
        private PanelHandle _panelHandle;
        private NightMarketSharedCardSession _sharedCardSession;
        private readonly List<GenericCardModel> _sharedOfferCards = new List<GenericCardModel>();

        // =====================================================================
        //  UI-001 §R2 — THE LANDSCAPE COMPOSITION, IN AUTHORED REFERENCE PIXELS.
        // ---------------------------------------------------------------------
        //  ⛔ EVERY NUMBER HERE IS REFERENCE PX, NEVER A FRACTION OF A PARENT, and
        //  that is the whole of the P0-3 fix. The CanvasScaler resolves 1080x1920 at
        //  match 0.5 to scale 1.104 on a 2340x1080 landscape phone, so the usable
        //  canvas is 2120 x 978 reference units (UI-001 §0.4) — vertically HALF the
        //  1920 this screen's fractions were measured against. A row authored as a
        //  share of "the panel" therefore landed at roughly double its intended share
        //  of the height it really had, which is how the owner's 2026-08-22 device
        //  frames showed the Grain Cart drawn ON TOP of the Timber Wagon and a 120 SKR
        //  pack reading "20 SKR" with its leading digit occluded. Author the number;
        //  let the market column scroll.
        //
        //  THE SCREEN IS THREE BANDS AND THERE IS EXACTLY ONE AT THE BOTTOM (§6):
        //     top bar   100  — wordmark + covenant + wallet rail; DISPLAY ONLY (§8)
        //     body      746  — spotlight 576 | market (fluid) | commerce 486
        //     bottom    132  — legal left, the canon Close centre, promise right
        //  100 + 746 + 132 = 978, exactly.
        //
        //  ⛔ NEVER ADD A SECOND BOTTOM BAND. The 2026-08-22 frames' "bottom ~35% is
        //  an empty grey slab with an oversized Close floating in it" (P1-6) was a
        //  close-band reservation UNDER a separate trust strip — two bands stacked in
        //  a 978-unit budget. The trust copy and the Close now share this ONE row, and
        //  the Close is re-seated INSIDE it, so there is no cavity left to reclaim.
        // =====================================================================
        private static class NightMarketLayout
        {
            /// <summary>The usable landscape canvas, reference units (UI-001 §0.4) — the value a
            /// 2340x1080 phone resolves to.
            /// <para>⚠ THESE ARE NOW THE FALLBACK, NOT THE MEASUREMENT (WO-1162). The live box comes
            /// from <see cref="PackStore.SurfaceReferenceHeightPx"/> /
            /// <see cref="PackStore.SurfaceReferenceWidthPx"/>, because a 4:3 landscape resolves to
            /// ~1663x1247 and the Seeker's 2670x1200 to ~2149x966 — treating 2120x978 as universal is
            /// how a "reference pixel" quietly became a magic number for one device. They survive as
            /// the no-canvas / headless fallback and as the vertical budget's stated arithmetic
            /// below.</para></summary>
            internal const float UsableWidthPx  = 2120f;
            internal const float UsableHeightPx = 978f;

            /// <summary>Top bar height. Hard-reach zone (§8): read, never tapped.</summary>
            internal const float TopBarPx = 100f;

            /// <summary>The ONE bottom band. It IS <see cref="ElarionUiKit.CanonCtaHeight"/> because
            /// the canon bottom-centre Close is seated in it — the band and the button are the same
            /// row, which is what makes a second band structurally impossible here.</summary>
            internal const float BottomBandPx = ElarionUiKit.CanonCtaHeight;

            /// <summary>Everything left over. 978 - 100 - 132 = 746.</summary>
            internal const float BodyPx = UsableHeightPx - TopBarPx - BottomBandPx;

            // ⛔ THE THREE COLUMN WIDTHS ARE NOT HERE ANY MORE (WO-1162 FIX 1). They used to be
            // two literals - spotlight 576, commerce 486 - measured against ONE surface, with
            // nothing in the file saying what content 576 was protecting. They now come from
            // NightMarketComposition, which DERIVES each rail's minimum from the narrowest thing it
            // has to hold and picks a COMPOSITION (three columns / three narrowed columns / two
            // columns with commerce stacked under the spotlight) instead of shrinking a card. The
            // gap lives there too, because the breakpoint formula is stated in terms of it:
            //     ThreeColumnMinBodyPx = SpotlightMin + ShelfMinForTwoCards + CommerceMin + 2*Gap
            internal static float ColumnGapPx => NightMarketComposition.ColumnGapPx;

            internal const float EdgePadPx        = 18f;

            // ── WO-1334 defect #2 — the utility rails' OWN measurements, stated ONCE ──────
            // ⛔ THESE ARE CONSUMED, NOT RESTATED. BuildUtilityHeading sets its LayoutElement from
            // UtilityHeadingPx and BuildScrollColumn sets its VerticalLayoutGroup from
            // UtilityRowSpacingPx / UtilityColumnPadPx. They are named here because the
            // ACTIONS / CLOSE-THE-GAP vertical budget is arithmetic over exactly these numbers, and
            // that arithmetic (written out at the LandscapeActions region in BuildCommerce) is what
            // showed the column is 59 px OVERSUBSCRIBED rather than mis-split. A measurement that
            // only exists inside a layout call cannot be reasoned about by the next person.
            internal const float UtilityHeadingPx    = 64f;
            internal const float UtilityRowSpacingPx = 6f;
            internal const int   UtilityColumnPadPx  = 8;

            // ── WO-1636 — THE UTILITY ROW'S HORIZONTAL BUDGET, IN REFERENCE PX ────────────
            // The row's icon and caption used to be authored as shares of the button (.035-.17 and
            // .20-.92), which is a share of a rect whose width is whatever the commerce rail
            // resolved to. At 1280x720 that left the caption 302 px and "MONTHLY LEDGER" drew 12 of
            // 13 printable glyphs (Builds/wave3-capture2.glyph-findings.txt:57). Px, so the caption
            // gets the same room at every aspect and the dead gap between the two is spent.
            /// <summary>Left inset of the icon slot inside the row button.</summary>
            internal const float UtilityRowIconLeftPx  = 14f;
            /// <summary>The icon slot's width. Its art fits inside by aspect (AddArt).</summary>
            internal const float UtilityRowIconWidthPx = 52f;
            /// <summary>Gap between the icon slot and the caption.</summary>
            internal const float UtilityRowIconGapPx   = 8f;
            /// <summary>Where the caption starts — derived, never a second literal.</summary>
            internal const float UtilityRowTextLeftPx =
                UtilityRowIconLeftPx + UtilityRowIconWidthPx + UtilityRowIconGapPx;
            /// <summary>Padding between the caption's end and the button's right edge.</summary>
            internal const float UtilityRowTextRightPadPx = 8f;

            // ── WO-1636 — THE BOTTOM BAND'S THREE TENANTS, STATED ONCE ───────────────────
            // ⛔ CONSUMED, NOT RESTATED. SeatCloseInBottomBand reads the Close's centre from here,
            // BuildLandscapeBottomNotice reads the legal copy's left edge from here, and the
            // CommerceCta seat is derived from BOTH of them. They used to be three literals in
            // three methods (.15f, .51f, and a .30-.50 pair) with only a comment tying them
            // together — and the comment was already wrong, because it said "the landscape CTA owns
            // x .30-.50" while nothing enforced it.
            /// <summary>Centre of the canon Close in the landscape bottom band.</summary>
            internal const float BottomBandCloseCentreFrac = 0.15f;
            /// <summary>Left edge of the landscape legal copy.</summary>
            internal const float BottomNoticeLeftFrac = 0.51f;
            /// <summary>Clearance the CTA seat keeps from each neighbour. Comfortably above
            /// LayoutOracle's overlap pad (2 px) — two neighbours that merely fail to overlap are
            /// not a layout.</summary>
            internal const float BottomBandKeepOutPx = 24f;

            /// <summary>The narrowest the landscape legal copy may be squeezed to before the Buy
            /// control leaves the band instead (rule 4 at the CommerceCta seat).
            /// <para>MEASURED, not chosen: the two notice labels are authored across x .51-.995 of
            /// the band, and the NARROWEST captured aspect is 16:9, whose reference band is
            /// 1920 - 2 x EdgePadPx = 1884 px. That share is .485 x 1884 = 914 px, and the
            /// glyph-oracle run flagged NEITHER notice label at 1280x720 — so 914 px is a width
            /// this copy is known to render whole in. Below it we are guessing, so below it the
            /// CTA relocates rather than the copy shrinking into a truncation nobody measured.</para></summary>
            internal const float BottomNoticeMinPx = 914f;

            /// <summary>WO-1636 — the pad either side of a canon CTA's caption, in reference px.
            /// The same 8 px the utility rows use, so a Buy-control caption and a rail caption
            /// breathe alike. See <c>PackStore.SeatCtaLabelInPx</c> for why the kit's .04 fraction
            /// is wrong on a button that is authored at a fixed px width.</summary>
            internal const float CtaLabelPadPx = UtilityRowTextRightPadPx;

            // ⛔ THE CLOSE KEEP-OUT IS NOT HERE. It moved to StoreLegalFooter.CloseKeepOutPx with
            // the copy it protects — a keep-out authored in one file and consumed by the layout in
            // another is the duplicated-measurement shape this file's own comments keep warning
            // about. StoreLegalFooter owns the band's copy AND the space it must leave.

            /// <summary>FULL-BLEED. Owner ruling 1 (2026-08-22): "maximize whole screen". The old
            /// 0.055-0.945 column is retired; safe-area insets are applied in px on the screen host
            /// (<see cref="PackStore.ApplySafeArea"/>), never by shrinking the panel.</summary>
            internal static readonly Vector2 PanelMin = new Vector2(0f, 0f);
            internal static readonly Vector2 PanelMax = new Vector2(1f, 1f);

            internal const int CardsPerRow = NightMarketComposition.CardsPerRow;

            // ⛔ THE CTA SUB-HOST HEIGHT IS NOT A CONSTANT ANY MORE (WO-1162 FIX 1). It was 440f,
            // and the button inside it was authored as the FRACTION 0.030-0.335 of that number.
            // A fraction of a host that shrinks (it does, in the stacked two-column composition)
            // lands the button under MinTouchPx, and a sub-floor control does not fail the clamp -
            // ClampMinTouch GROWS it about its centre, into whatever is beside it. The host height
            // now comes from the resolved plan (NightMarketPlan.CtaHostPx) and the button is
            // authored in PIXELS against it, so the Buy control is the canon size in every mode.
        }

        /// <summary>
        /// Cards per shelf row. Two is the device-verified readability ruling: the earlier three-up
        /// pass cleared the touch floor but made names, contents and prices illegible at play distance.
        /// Readability outranks catalogue density; the shelf scrolls.
        /// </summary>
        private const int CardsPerRow = NightMarketLayout.CardsPerRow;

        /// <summary>
        /// The card variant a band's rows are drawn at. ⛔ THE HEIGHT COMES FROM
        /// <see cref="StorePackCard"/>, WHICH AUTHORS IT IN REFERENCE PX — this file no longer
        /// carries a card height of its own, because two places holding one measurement is how the
        /// row and the card came to disagree in the first place.
        /// </summary>
        private StorePackCardVariant VariantFor(StoreBand band)
        {
            if (_utilityContent != null)
                return band == StoreBand.Basket
                    ? StorePackCardVariant.LandscapeStandard
                    : StorePackCardVariant.Compact;
            return band == StoreBand.Gap ? StorePackCardVariant.Compact : StorePackCardVariant.Standard;
        }

        // Compact landscape reserves enough width for two readable cards. Promote to
        // three only when the measured wide shelf still preserves the card contract;
        // this keeps 800/1280 touch-safe while using the otherwise-empty ultrawide row.
        private int ShelfCardsPerRow
        {
            get
            {
                const int wideCount = 3;
                float chrome = (wideCount - 1) * NightMarketComposition.RowSpacingPx
                    + (2f * NightMarketComposition.RowPadPerSidePx)
                    + (2f * NightMarketComposition.ShelfPadPerSidePx)
                    + NightMarketComposition.ScrollGutterPx;
                float wideCardWidth = (_plan.MarketWidthPx - chrome) / wideCount;
                return ElarionUiKit.SurfaceWidth >= 2000
                       && (_plan.Mode == NightMarketMode.WideThreeColumn
                        || _plan.Mode == NightMarketMode.CompactThreeColumn)
                       && wideCardWidth >= StorePackCard.MinCardWidthPx
                    ? wideCount
                    : CardsPerRow;
            }
        }

        /// <summary>Free-band doors are drawn on the dense rung, so the free row can never out-size
        /// the priced shelf above it.</summary>
        /// <remarks>static readonly, not const: the card heights are DERIVED from their content
        /// budget now (WO-1162 FIX 2), so they are properties rather than compile-time literals.</remarks>
        private static float FreeRowHeightPx => StorePackCard.CompactHeightPx;

        /// <summary>Height of the FREE band's utility-tab row, reference px. ⛔ ONE NUMBER, and the
        /// row and the slot both take it — the literal 132f used to be typed once in BuildFreeBand
        /// and again in BuildUtilityTab, which is the two-places-one-measurement shape that produced
        /// the card-on-card overlap this file's comments keep recording.</summary>
        private const float FreeTabRowPx = 132f;

        /// <summary>Narrowest a utility tab may be authored, reference px. Over MinTouchPx by
        /// construction so three tabs can share the shelf without any of them inflating.</summary>
        private const float FreeTabMinWidthPx = 210f;

        // Kit modal (lazy-built on first open) + the surfaces Render() fills.
        private ElarionUiKit.ObsidianModal _modal;
        private RectTransform _screen;                  // safe-area host: the three bands hang here
        private RectTransform _topBar;                  // §R2 band 1 — 100 ref px, DISPLAY ONLY
        private RectTransform _bodyHost;                // §R2 band 2 — 746 ref px, three columns
        private RectTransform _bottomBand;              // §R2 band 3 — the ONE 132 ref px row
        private RectTransform _marketHost;              // centre column (fluid) — the banded shelf
        private RectTransform _commerceHost;            // right column — status + the ONE Buy control
        private RectTransform _ctaHost;                 // cleared per focus; the CTA lives here
        // WO-1636 — the bottom band's seats, resolved ONCE in EnsureBuilt and consumed downstream.
        // ⛔ THEY ARE DERIVED VALUES, NOT MEASUREMENTS. Reading `.rect.width` at build time returns
        // RAW SCREEN PIXELS (the canvas scaler has not run), which is what shipped a 26.9 px Buy
        // control at 800x360 on WO-1636's first attempt. Nothing below may re-measure them.
        private float _ctaHostWidthPx;                  // the CTA seat's reference width
        private float _bottomNoticeLeftFrac;            // where the landscape legal copy starts
        private Transform _shelfContent;                // band strips + card rows
        private int _persistentShelfChildren;           // the FREE band, built once, never re-rendered
        private Transform _utilityContent;              // landscape upper-right rail: persistent actions
        private Transform _gapUtilityContent;           // landscape lower-right rail: gap offers stay visible
        private int _persistentUtilityChildren;         // action rows survive catalogue renders
        private Transform _spotlightHost;               // rebuilt whole on each focus change
        private TextMeshProUGUI _statusBanner;          // purchase status surface
        private TextMeshProUGUI _balanceLabel;          // the read-only wallet mirror
        private StoreLegalFooterHandle _legalFooter;    // the ONE legal/promise band (StoreLegalFooter)
        private StoreAurora _aurora;                    // Lane G — the four motion moments

        /// <summary>The resolved responsive body composition (WO-1162 FIX 1). Owned by
        /// <see cref="NightMarketComposition"/>; this View only consumes it.</summary>
        private NightMarketPlan _plan;

        /// <summary>The surface the modal was BUILT for. A rotation, a resolution change or a
        /// different device re-resolves the composition — a layout resolved once and then kept is
        /// the same duplicated-state failure as a number copied into a second doc.</summary>
        private Vector2Int _builtForSurface;

        // The promo-code door. A CHILD overlay on this store's own canvas.
        private RedeemCodePanel _redeem;

        // Per-pack currency selection (SKU → chosen rail).
        private readonly Dictionary<string, CurrencyKind> _selectedCurrency = new Dictionary<string, CurrencyKind>();
        private bool _purchaseInFlight;

        private enum CommerceState
        {
            Ready, OpeningWallet, AwaitingApproval, Submitted, Verifying,
            Delivering, Fulfilled, Cancelled, Failed, Delayed
        }

        private CommerceState _commerceState = CommerceState.Ready;
        private string _commerceDetail = string.Empty;
        private float _commerceStateSince;

        // ── Focus (the spotlight) ────────────────────────────────────────────
        private string _focusSku;
        private string _pendingShortfallLabel;
        private int _pendingShortfallMissing;
        // WO-1386 - armed by FocusShortfall, consumed ONCE by RouteGuestShortfallToWalletConnect on
        // the next open. A bool rather than re-reading the label, because the label is never
        // cleared and a route that re-fired on every later open would throw a connect sheet at a
        // player who came back to browse.
        private bool _shortfallWalletRouteArmed;
        // WO-1253 — Manage "Buy builder" sets a pending SKU before opening the store so the
        // spotlight lands on the permanent-builder SKU even when the host is spawned in the same
        // call (OnEnable -> Render runs before an instance method could).
        //
        // WO-1282 — THE LATCH ITSELF MOVED to DeNelle.Commerce.StoreFocusRequest so DeNelle.Village
        // can set it without referencing DeNelle.Wallet. It is the SAME latch with the SAME
        // write-then-consume-once behaviour; only its home changed. There is deliberately NO local
        // copy of it here — two latches over one decision is the duplicated state that goes stale.

        // Selection marks, so a focus change repaints two cards instead of the whole shelf.
        // ⛔ ONE HANDLE PER CARD, HANDED BACK BY THE ONE TEMPLATE. The two parallel dictionaries
        // that used to live here (rails + Outlines) were this file's own second card implementation;
        // UI-001 §2 permits exactly one, and it is StorePackCard.
        private readonly Dictionary<string, StorePackCardHandle> _cardHandles =
            new Dictionary<string, StorePackCardHandle>(StringComparer.Ordinal);

        // ── The wallet mirror ────────────────────────────────────────────────
        private enum BalanceState { NoWallet, Checking, Unavailable, Known }
        private BalanceState _balanceState = BalanceState.NoWallet;
        private double _balanceSkr;

        // WO-1409: browsing the catalogue is public, but a Solana purchase still needs a
        // signing wallet. This is a presentation predicate only; PurchaseGate remains the
        // authority for whether a particular pack may be bought.
        private bool WalletlessBrowsing =>
            !PiDisplay && (_wallet == null || !_wallet.IsRealSigningWallet);

        // ⛔ WO-1334 — THE `~$12.40` FIAT TAIL AND ITS JUPITER QUOTE ARE REMOVED FROM THIS CHIP.
        // The owner ruled the connected chip is `SKR: <balance>` and *"that is the whole chip"*.
        // The fields (_fiatUsd/_hasFiat/_fiatAtRealtime), the FiatStaleSeconds window and
        // RefreshFiatApproximation() went with the tail rather than being left assigned-but-unread,
        // because a live quote nobody renders is a network call and a staleness rule that can only
        // rot. NOTHING ELSE consumed them — the fiat half had exactly one reader, the header text.
        // This is a DISPLAY removal only: no price, SKU, quote-for-purchase or grant is touched;
        // shelf pricing still comes from PurchaseQuoteService, server-side, as before.
        // `StoreStrings.KeyBalanceFiat` / `storeBalanceFiat` are deliberately LEFT IN PLACE so the
        // sentence and its canon row survive if the tail is ever wanted somewhere with room for it.

        /// <summary>Raised when a pack purchase confirms — carries the pack and the tx result.</summary>
        public event Action<PackDef, PaymentResult> PackPurchased;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            _wallet = new WalletService();
            _vm = PackStoreVM.CreateDefault();

            // WO-1398: no player-facing store-name literal survives here. The handle's name is
            // DIAGNOSTIC ONLY (PanelManager.cs:167) and is deliberately NOT the wordmark read:
            // NightMarketUiRegression pins exactly ONE wordmark-key read in this file - the visible
            // modal title (BuildObsidianModal below) - so the diagnostic handle reuses the modal's
            // own object name instead of a second wordmark read.
            _panelHandle = PanelManager.Register("PackStoreUI",
                () => { if (this != null) gameObject.SetActive(false); },
                () => _modal != null && _modal.canvas != null && _modal.canvas.activeInHierarchy);
        }

        private void OnEnable()
        {
            // ⛔ A COMPOSITION RESOLVED ONCE AND THEN KEPT IS A STALE MEASUREMENT (WO-1162 FIX 1).
            // The modal is built lazily and lives for the session, so a rotation, a resolution
            // change or a capture driving the surface would otherwise leave three columns sized for
            // a screen the player no longer has. Rebuilding on open is cheap (the store is not a
            // per-frame surface) and it is the only way the plan can never disagree with the screen.
            DiscardBuildIfSurfaceChanged();

            EnsureBuilt();
            if (_modal != null && _modal.canvas != null)
                _modal.canvas.SetActive(true);
            if (_sharedCardSession == null)
            {
                _sharedCardSession = GetComponent<NightMarketSharedCardSession>();
                if (_sharedCardSession == null)
                    _sharedCardSession = gameObject.AddComponent<NightMarketSharedCardSession>();
            }
            _sharedCardSession.OpenBrowser();

            // ⛔ ADOPT THE LIVE WALLET (2026-08-24, the go-live P0). SetWalletService below is a
            // public injector that NOTHING in the project ever called, so `_wallet` was permanently
            // null and RefreshQuotedPrices asked PurchaseQuoteService for prices with a null wallet
            // - which fails closed ("no signing wallet") and never issues the request. The player saw
            // "Price unavailable" on every pack while their wallet read as connected two inches away
            // on the same screen, and the server log showed ZERO /api/purchases/quote requests.
            //
            // ⚠ ADOPTED ON OPEN, NOT ONCE AT BUILD: the store is built lazily and kept for the
            // session, so a connect that happens AFTER the first open must still be picked up. This
            // is the same reasoning as DiscardBuildIfSurfaceChanged above - a value resolved once and
            // kept is a stale measurement.
            //
            // ⚠ AND THE PRICES NO LONGER DEPEND ON IT (WO-1190, 2026-08-25): the list request is
            // public and goes out with or without a wallet, so the paragraph above is the HISTORY of
            // a fixed defect, not a description of today. Adoption now buys the binding quote a
            // signer and the server a viewer to word `sellableReason` for — nothing on the shelf.
            //
            // An explicitly injected service still wins; this only fills a null.
            // Subscribe FIRST, so a connect that lands during this open is not missed between the
            // check below and the handler being attached.
            CurrencySkinResolver.WalletConnectionChanged -= OnWalletConnectionChanged; // idempotent
            CurrencySkinResolver.WalletConnectionChanged += OnWalletConnectionChanged;

            AdoptLiveWalletIfBetter("open");

            // WO-1323 OWNER RULING (2026-09-02) - point the EXISTING focus latch at the one
            // Pi-priced pack BEFORE the first Render, because Render is where the latch is consumed
            // (ResolveFocusSku). Latched per OPEN, never per Render: a repaint driven by a returning
            // quote must not yank the spotlight back off a card the player just tapped.
            LatchPiSpotlightOnOpen();

            // WO-1388 FUNNEL step 1 of 4 - store_opened {door}. THE ONE PLACE. Read BEFORE Render:
            // Render consumes the SKU latch and RouteGuestShortfallToWalletConnect consumes the
            // shortfall arm, and both are inputs to the inferred door. A caller that named its door
            // (PanelRouter context via PackStoreBootstrap.OpenRealmStoreFromDoor) wins outright.
            TrackStoreOpened();

            Render();
            TraceShelfStateOnOpen();
            // WO-1386 - AFTER the first Render, because the connect door writes the commerce banner
            // and the banner must exist; the spotlight it lands beside was resolved by that Render.
            RouteGuestShortfallToWalletConnect();
            RefreshWalletMirror().Forget();
            RefreshQuotedPrices().Forget();
            // WO-1323 — the Pi shelf's figures come from /api/pi/quote, and this is the one place
            // that asks. No-op on every non-Pi surface.
            RefreshPiDisplayPrices();
            RestorePendingPresentation();

            if (_panelHandle != null) PanelManager.NotifyOpened(_panelHandle);
        }

        // =====================================================================
        //  WO-1388 - THE STORE FUNNEL. Four events, ONE emit site each, so a count can never be
        //  double-fired from a second surface. store_opened / pack_tapped / checkout_started /
        //  checkout_failed sit between the pre-existing bundle_viewed and purchase_completed;
        //  api/admin/stats.js ?view=economy renders them as store_funnel so "0 sales" reads as
        //  WHICH step is the first zero. Every emit is also a [Flow:Store] line, because a
        //  telemetry row nobody can see on a device log is a claim, not a measurement.
        // =====================================================================

        /// <summary>Door names the funnel records. The inferred set; named doors arrive via the latch.</summary>
        private const string DoorHudCard = "hud-card";
        private const string DoorShortfall = "shortfall";
        private const string DoorManage = "manage";

        /// <summary>
        /// store_opened {door}. A named door (settings / vendor / ...) comes from the
        /// <see cref="StoreFocusRequest"/> latch; otherwise the door is INFERRED from the two other
        /// open-time latches: an armed shortfall route means the "Short N wood" door, a pending SKU
        /// focus means the Manage "Buy builder" route, and everything else is the HUD card - which
        /// is where every remaining plain open (the HUD Night Market card and its dock tab) comes from.
        /// </summary>
        private void TrackStoreOpened()
        {
            string named = StoreFocusRequest.ConsumeDoor();
            string door = named ??
                          (_shortfallWalletRouteArmed ? DoorShortfall
                           : StoreFocusRequest.HasPending ? DoorManage
                           : DoorHudCard);
            FlowTrace.Step("Store", "funnel store_opened door=" + door + (named == null ? " (inferred)" : " (named)") + ".");
            Guard.Try("Store", "track store_opened",
                () => DeNelle.Core.Analytics.EventTracker.Track("store_opened", new { door }));
        }

        /// <summary>pack_tapped {sku, section, priceUsd} - emitted from <see cref="FocusPack"/> on a player tap.</summary>
        private static void TrackPackTapped(PackDef pack)
        {
            if (pack == null) return;
            string section = pack.StoreSection ?? string.Empty;
            double priceUsd = pack.Pricing != null ? pack.Pricing.Usd : 0d;
            FlowTrace.Step("Store", "funnel pack_tapped sku=" + pack.Sku + " section=" + section + " priceUsd=" + priceUsd + ".");
            Guard.Try("Store", "track pack_tapped",
                () => DeNelle.Core.Analytics.EventTracker.Track("pack_tapped", new { sku = pack.Sku, section, priceUsd }));
        }

        /// <summary>
        /// checkout_started {sku, channel, rail} - emitted at the head of <see cref="Purchase"/>, which
        /// BOTH rails enter (the provider path branches off inside it), so one site covers Solana,
        /// Google Play and Pi. It fires before the gates on purpose: a gate refusal is then a
        /// checkout_failed against a started checkout, which is the drop the funnel exists to show.
        /// </summary>
        private void TrackCheckoutStarted(PackDef pack)
        {
            string channel = PaymentChannelResolver.Current.ToString();
            var provider = PaymentProviders.Current;
            string rail = OwnsTheRail(provider)
                ? "provider:" + provider.Channel
                : "wallet:" + (_wallet != null ? _wallet.Network.ToString() : "none");
            FlowTrace.Step("Store", "funnel checkout_started sku=" + pack.Sku + " channel=" + channel + " rail=" + rail + ".");
            Guard.Try("Store", "track checkout_started",
                () => DeNelle.Core.Analytics.EventTracker.Track("checkout_started", new { sku = pack.Sku, channel, rail }));
        }

        /// <summary>The four reasons checkout_failed may carry. Anything else is a caller bug.</summary>
        private const string FailWalletRequired = "wallet-required";
        private const string FailProviderRefused = "provider-refused";
        private const string FailCancelled = "cancelled";
        private const string FailError = "error";

        /// <summary>
        /// checkout_failed {sku, reason}. THE ONE emit site - every failure branch in
        /// <see cref="Purchase"/> and <see cref="PurchaseThroughProvider"/> calls this with one of the
        /// four reasons above. A refusal, a cancel and a throw are all "the checkout did not complete",
        /// and the reason is what separates a rule from a bug.
        /// </summary>
        private static void TrackCheckoutFailed(string sku, string reason, string detail)
        {
            FlowTrace.Step("Store", "funnel checkout_failed sku=" + sku + " reason=" + reason +
                                    (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")") + ".");
            Guard.Try("Store", "track checkout_failed",
                () => DeNelle.Core.Analytics.EventTracker.Track("checkout_failed", new { sku, reason }));
        }

        private void OnDisable()
        {
            _sharedCardSession?.Close();
            CurrencySkinResolver.WalletConnectionChanged -= OnWalletConnectionChanged;
            if (_redeem != null) _redeem.Close();
            if (_modal != null && _modal.canvas != null)
                _modal.canvas.SetActive(false);
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
        }

        /// <summary>
        /// The wallet finished connecting while this store was already alive — adopt it and ask the
        /// server for prices, because opening the store is NOT the only moment a wallet can arrive.
        /// </summary>
        /// <remarks>
        /// ⛔ THE DEFECT THIS CLOSES, read straight off the owner's device (2026-08-24):
        /// <code>
        ///   11:33:48  [Flow:Store]  quote list skipped: no signing wallet
        ///   11:34:19  [Flow:Wallet] &lt;- Connect (provider=Solana Wallet, Mainnet) (2799.8ms)
        ///   11:34:19  [Flow:Wallet] auto-resume SUCCEEDED — connected at boot with no player action.
        /// </code>
        /// <para>
        /// THE STORE OPENED 31 SECONDS BEFORE THE WALLET FINISHED CONNECTING, and nothing told it.
        /// The connect takes ~2.8s of association plus whatever the player spends in the wallet app,
        /// so "check on open" loses this race whenever the player reaches the store first — and on
        /// auto-resume, which fires at boot, they usually do.
        /// </para>
        /// <para>
        /// ⚠ AN OPEN-TIME CHECK ALONE CANNOT FIX THIS. The store host is spawned once and kept
        /// (hidden) for the session, so a player who opens the store, watches it say "Price
        /// unavailable", connects, and comes back is served by OnEnable — but a player already
        /// LOOKING at the store when the connect lands would sit on stale copy forever. Event plus
        /// open-time check is what covers both, the same both-halves reasoning CurrencySkinResolver
        /// itself documents for its connect seam ("a view built AFTER the connect never sees the
        /// event, so it must be able to read the state at build time").
        /// </para>
        /// </remarks>
        private void OnWalletConnectionChanged(bool connected, string shortAddress)
        {
            using var _ = FlowTrace.Enter("Store", $"wallet connection changed -> connected={connected}");

            if (!connected)
            {
                FlowTrace.Step("Store", "wallet disconnected — dropping the store's reference so no " +
                                        "stale price or address survives it.");
                _wallet = null;
                if (isActiveAndEnabled) Render();
                return;
            }

            if (!AdoptLiveWalletIfBetter($"connect event ({shortAddress})")) return;

            if (isActiveAndEnabled)
            {
                Render();
                RefreshWalletMirror().Forget();
                RefreshQuotedPrices().Forget();
            }
            else
            {
                FlowTrace.Step("Store", "store is hidden — reference adopted; prices refresh on next open.");
            }
        }

        /// <summary>
        /// Point <see cref="_wallet"/> at the session's LIVE wallet whenever that one can actually
        /// sign and the one we hold cannot. Returns true if the reference changed.
        /// </summary>
        /// <remarks>
        /// ⛔ THE BUG THIS EXISTS TO KILL, and it is why two earlier fixes did nothing (2026-08-24).
        /// <c>PackStore.Awake()</c> does <c>_wallet = new WalletService()</c> — the store mints its
        /// OWN instance, which is never connected to anything. So:
        /// <list type="bullet">
        /// <item><description><c>_wallet</c> is NEVER null, so an "adopt when null" check can never
        /// fire — both of my earlier guards were unreachable, which the device trace proved by their
        /// total absence from the log.</description></item>
        /// <item><description>That instance's <c>IsRealSigningWallet</c> is false FOREVER, so every
        /// path that demanded a signing wallet saw one that could never sign, regardless of what the
        /// player's real wallet was doing two inches away on the same screen.</description></item>
        /// </list>
        /// <para>
        /// ⚠ WHAT THIS NO LONGER DOES — CORRECTED 2026-08-25 (WO-1190). This comment used to say
        /// that without a signing wallet "<c>RefreshPricesAsync</c> fails closed on every open" and
        /// the shelf therefore reads "Price unavailable". THAT IS RETIRED AND IT IS NOW FALSE.
        /// <c>PurchaseQuoteService.RefreshPricesAsync</c> is PUBLIC: it takes a null or unconnected
        /// wallet, sends no signature, requests no backend session (<c>requireAuth:false</c>) and
        /// returns the full sold ladder on <c>WalletService.DefaultNetwork</c>. Browsing prices no
        /// longer authenticates — the owner's question was "why do I need to authorize if I'm just
        /// looking", and the answer is that she does not. Each row instead carries an advisory
        /// <c>sellable</c> / <c>sellableReason</c>, and the shelf renders a PRICE with a worded state
        /// line where it used to render nothing at all.
        /// </para>
        /// <para>
        /// So adopting the live wallet is no longer what makes the shelf show prices. It still
        /// matters for two things and only two: the BINDING quote (<c>RequestQuoteAsync</c>) still
        /// demands a real signing wallet, and a known address lets the server word each row's
        /// <c>sellableReason</c> for this specific viewer instead of for the public.
        /// </para>
        /// <c>SetWalletService</c> existed to REPLACE that placeholder and had zero callers project-wide,
        /// so the placeholder was the only wallet the store ever had.
        /// <para>
        /// ⚠ THE TEST IS CAPABILITY, NOT NULLNESS. "Do I have a wallet object?" was always yes and was
        /// always the wrong question. "Can the wallet I hold actually sign?" is the one that matters,
        /// and it is the same predicate <c>PurchaseQuoteService</c> gates on — so the store and the
        /// quote service can no longer disagree about whether a wallet is usable.
        /// </para>
        /// </remarks>
        private bool AdoptLiveWalletIfBetter(string reason)
        {
            var live = WalletSkinBootstrap.ConnectedWallet;

            if (_wallet != null && _wallet.IsRealSigningWallet)
                return false;   // already holding a signing wallet - nothing to do, stay quiet.

            if (live == null)
            {
                // ⚠ NOT A WARN ANY MORE, AND THE OLD SENTENCE WAS THE STALE BEHAVIOUR ITSELF: it
                // said the shelf "will show 'Price unavailable' because no quote can be requested".
                // Browsing is public now (see the remarks above) — the list request goes out with no
                // wallet and prices arrive. Nothing is wrong here; it is the ordinary state of a
                // player who has not connected yet, so it is a Step, not a warning.
                FlowTrace.Step("Store", $"[{reason}] no signing wallet held or connected - browsing " +
                                        "continues on the PUBLIC price list; only the binding quote " +
                                        "and viewer-specific sellable wording need a wallet.");
                return false;
            }

            if (ReferenceEquals(live, _wallet))
            {
                FlowTrace.Warn("Store", $"[{reason}] already holding the live wallet, but it reports " +
                                        "IsRealSigningWallet=false (connected=" + live.IsConnected + ") - " +
                                        "the connect has not completed or the provider cannot sign.");
                return false;
            }

            if (!live.IsRealSigningWallet)
            {
                FlowTrace.Warn("Store", $"[{reason}] a live wallet exists but it cannot sign yet " +
                                        "(connected=" + live.IsConnected + ") - not adopting a second " +
                                        "wallet that is equally unusable.");
                return false;
            }

            _wallet = live;
            FlowTrace.Step("Store", $"[{reason}] ADOPTED the live signing wallet, replacing the placeholder " +
                                    "PackStore.Awake created - server prices can now be requested.");
            return true;
        }

        private void OnDestroy()
        {
            _sharedCardSession?.Close();
            if (_modal != null && _modal.canvas != null)
                Destroy(_modal.canvas);
        }

        private void Update()
        {
            if (!_purchaseInFlight) return;
            float elapsed = Time.realtimeSinceStartup - _commerceStateSince;
            if (_commerceState == CommerceState.OpeningWallet && elapsed >= 5f)
                RenderCommerceStatus("Wallet is taking longer than usual. Check the wallet app; the request times out at 30 seconds.");
            else if (_commerceState == CommerceState.Verifying && elapsed >= 60f)
                SetCommerceState(CommerceState.Delayed,
                    "Your payment may still settle. It is recorded for reconciliation; do not pay again.");
        }

        /// <summary>
        /// Injects a shared <see cref="WalletService"/> — e.g. the same instance a
        /// <see cref="WalletConnectDialog"/> drives, so a connect there is visible here.
        /// </summary>
        public void SetWalletService(WalletService service)
        {
            if (service == null) return;
            _wallet = service;
            Render();
            RefreshWalletMirror().Forget();
            RefreshQuotedPrices().Forget();
        }

        /// <summary>
        /// Pulls the shelf's SKR figures FROM THE SERVER (WO-1158).
        ///
        /// <para>⛔ THIS USED TO CALL <c>SkrValuationOracle.Refresh()</c> - the client fetching a
        /// market rate and pricing the packs itself, while the backend verified the settled transfer
        /// against a figure of its own. Two opinions about a moving number, reconciled AFTER the
        /// money moved. Now the server prices and this only transports.</para>
        ///
        /// <para>A refusal repaints nothing: the cards keep whatever honest state they had, which is
        /// the WORDS "Price unavailable" rather than a number we made up.</para>
        /// </summary>
        private async UniTaskVoid RefreshQuotedPrices()
        {
            if (await PurchaseQuoteService.RefreshPricesAsync(_wallet) && this != null && isActiveAndEnabled)
                Render();
        }

        /// <summary>
        /// Opens the store pre-focused on the pack that closes a REAL shortfall the caller is looking
        /// at (label = "Wood"/"Iron"/"Food"/"Crystals", missing = the gap). Call BEFORE enabling this
        /// GameObject; a zero/absent shortfall focuses <c>starters-hand</c> instead.
        ///
        /// <para>⚠ THE STORE CANNOT COMPUTE A SHORTFALL BY ITSELF and must not pretend to. A shortfall
        /// only exists relative to a thing the player is blocked ON — an upgrade, a build cost — and
        /// that context lives with the caller, not here. Grok's draft had the store resolve the
        /// player's "live shortfall" on open; there is no such value to read. This seam is that idea,
        /// made honest: whoever HAS the gap hands it over.</para>
        ///
        /// <para>⛔ <see cref="ShortfallPackOffer"/> gains exactly ONE new caller and NO new
        /// capability. It still only returns a <see cref="PackDef"/>; it never grants, charges or
        /// routes, and nothing here gives it a way to. WO-931 is that mistake.</para>
        /// </summary>
        public void FocusShortfall(string resourceLabel, int missing)
        {
            _pendingShortfallLabel = resourceLabel;
            _pendingShortfallMissing = missing;
            _shortfallWalletRouteArmed = missing > 0 && !string.IsNullOrEmpty(resourceLabel);
            FlowTrace.Step("Store", $"FocusShortfall requested: {missing} {resourceLabel}.");
        }

        /// <summary>
        /// WO-1386 (owner ruling 2026-09-04, verbatim: <i>"nothing should be guest buyable on a
        /// crypto account otherwise we can never persist change"</i>). A GUEST whose shortfall
        /// remedy the wallet rule refuses is routed to the WALLET-CONNECT surface, not left on a
        /// pack checkout they cannot complete. On the Solana rail that is EVERY impulse pack, so
        /// the "Short N wood" door would otherwise open onto a card whose Buy is withheld and whose
        /// only way forward is reading a banner.
        ///
        /// <para>Fires ONCE per <see cref="FocusShortfall"/> request (armed there, consumed here) -
        /// never per Render, never on an open the caller did not tie to a shortfall - so a player
        /// browsing for any other reason is not handed a connect sheet. The spotlight STILL lands
        /// on the remedy pack (<see cref="ResolveFocusSku"/> is untouched), so when the connect
        /// completes the pack they came for is the card in front of them, and the Buy face on it
        /// is now real.</para>
        ///
        /// <para>Asks the ONE predicate the CTA asks, <see cref="PurchaseGate.WalletIsTheBlocker"/>,
        /// so this door can never disagree with the button: rail closed => no route (connecting
        /// would not help); wallet attested => no route; Pi skin => no route, because the connect
        /// door is a SOLANA door and a Pi player has no use for it (WO-1323).</para>
        /// </summary>
        private void RouteGuestShortfallToWalletConnect()
        {
            if (!_shortfallWalletRouteArmed) return;
            _shortfallWalletRouteArmed = false;
            if (string.IsNullOrEmpty(_pendingShortfallLabel) || _pendingShortfallMissing <= 0) return;

            var offer = ShortfallPackOffer.Resolve(_pendingShortfallLabel, _pendingShortfallMissing);
            if (!offer.HasOffer || offer.Pack == null) return;

            if (PiDisplay)
            {
                // Pi needs the wallet too (owner 2026-09-04: "same logic based on USD"), but the
                // door below is _wallet.Connect() - a SOLANA handshake a Pi player cannot complete.
                // The Pi plate on the spotlight card (StoreStrings.PiWalletRequired) carries the
                // refusal; a Pi-native connect surface is not in this lane.
                FlowTrace.Step("Store", "shortfall door: Pi skin - the Solana wallet-connect route stands " +
                                        "down; the Pi plate on the card is the refusal (WO-1323 / WO-1386).");
                return;
            }

            if (!PurchaseGate.WalletIsTheBlocker(offer.Pack)) return;

            PaymentChannel channel = PaymentChannelResolver.Current;
            string sentence = PurchaseGate.WalletRefusalSentence(channel);
            FlowTrace.Step("Store",
                $"shortfall door: GUEST on channel {channel} asked for '{offer.Pack.Sku}' " +
                $"({_pendingShortfallMissing} {_pendingShortfallLabel} short) and the wallet rule refuses it - " +
                "routing to the wallet-connect surface instead of a pack checkout (WO-1386). " +
                $"Sentence shown: \"{sentence}\"");
            ConnectForWalletGate(sentence).Forget();
        }

        /// <summary>
        /// WO-1253 — open the store pre-focused on a named SKU (the permanent-builder Manage
        /// route). Static because the host may not exist yet; <see cref="ResolveFocusSku"/>
        /// consumes it on the next Render.
        /// </summary>
        public static void RequestFocusSku(string sku)
            => StoreFocusRequest.RequestFocusSku(sku);

        // =====================================================================
        //  UI construction (kit modal, lazy on first open)
        // =====================================================================

        /// <summary>
        /// Tear the built modal down when the surface it was composed for is gone, so the next
        /// <see cref="EnsureBuilt"/> re-resolves the composition. Does nothing on the common path
        /// (same device, same orientation) — it compares two ints.
        /// </summary>
        private void DiscardBuildIfSurfaceChanged()
        {
            if (_modal == null || _modal.canvas == null) return;
            var now = new Vector2Int(ElarionUiKit.SurfaceWidth, ElarionUiKit.SurfaceHeight);
            if (now == _builtForSurface || now.x <= 1 || now.y <= 1) return;

            FlowTrace.Step("Store", $"surface changed {_builtForSurface.x}x{_builtForSurface.y} -> " +
                                    $"{now.x}x{now.y}; rebuilding the Night Market so the responsive " +
                                    "composition is re-resolved rather than carried over stale.");
            if (_redeem != null) { _redeem.Close(); _redeem = null; }
            Destroy(_modal.canvas);
            _modal = null;
            _shelfContent = null;
            _utilityContent = null;
            _gapUtilityContent = null;
            _statusBanner = null;
            _balanceLabel = null;
            _legalFooter = null;
            _aurora = null;
            _cardHandles.Clear();
            _sharedOfferCards.Clear();

            _persistentShelfChildren = 0;
        }

        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;
            using var _ = FlowTrace.Enter("Store", "EnsureBuilt (Night Market)");

            // ⛔ FULL-BLEED, AND frameName: null IS DELIBERATE (UI-001 §0.1 + §0.3).
            // FrameMerchant is PORTRAIT art (1005x1507) drawn Image.Type.Simple with no 9-slice, so
            // at 2340x1080 it is a 3.25x horizontal STRETCH of an ornate border and its pixel-measured
            // medallion/header fractions stop landing on art features entirely. The procedural
            // obsidian panel is aspect-agnostic, which is the only reason this screen can be the
            // whole screen. Do not "restore" the frame here; it is not a style choice, it is a
            // resolution fact.
            _modal = ElarionUiKit.BuildObsidianModal("PackStoreUI", StoreStrings.Get(StoreStrings.KeyWordmark),
                NightMarketLayout.PanelMin, NightMarketLayout.PanelMax, CloseStore,
                frameName: null, medallionIcon: null);

            if (_modal == null || _modal.canvas == null)
            {
                FlowTrace.Fail("Store", "EnsureBuilt: kit modal failed to build — store cannot draw, player would see a blank/soft-locked panel.");
                return;
            }

            // ⛔ THE STORE COMPOSES ITS OWN THREE BANDS ON chrome.content — NOT ON chrome.layout.body.
            // The kit's body zone carries the CLOSE-BAND RESERVATION (ElarionUiKit.cs:626-668), which
            // divides the fixed CanonCtaHeight(132) by the SHRUNKEN landscape height and lands the body
            // floor at ~0.28 of the panel. On a 978-unit budget that reservation IS the P1-6 cavity:
            // "the bottom ~35% is an empty grey slab with an oversized Close floating in it". We
            // reclaim it by seating the Close INSIDE our own single bottom band below, so the
            // reservation has nothing left to protect and nothing left to waste.
            var content = _modal.chrome.content != null ? _modal.chrome.content.transform
                        : (_modal.chrome.root != null ? _modal.chrome.root.transform : null);
            if (content == null)
            {
                FlowTrace.Fail("Store", "EnsureBuilt: chrome.content is null — nothing to compose on; the store cannot draw.");
                return;
            }

            // The kit's wordmark is the ONE title (§3). Seat it in the top bar rather than adding a
            // second header label — defect #2 was the title printed twice.
            SeatWordmark(content);

            // Lane G host. Built ONCE and reused; under the reduced-motion preference it disables
            // itself on enable and registers nothing, so the flat lights remain.
            _aurora = _modal.canvas.GetComponent<StoreAurora>();
            if (_aurora == null) _aurora = _modal.canvas.AddComponent<StoreAurora>();

            // ── THE THREE BANDS, IN REFERENCE PX (§R2) ────────────────────
            _screen = Region(content, "NightMarket", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Vector4 safe = ApplySafeArea(_screen);
            BuildGround(_screen);

            float pad = NightMarketLayout.EdgePadPx;

            // ── RESOLVE THE COMPOSITION BEFORE ANY COLUMN IS PLACED (WO-1162) ──
            // The body box is what is LEFT after the safe-area inset and the edge padding, on THIS
            // surface — not on the 2120x978 one the old literals were measured against. Everything
            // horizontal below is a consequence of the plan; nothing re-derives a width of its own.
            float surfaceH = SurfaceReferenceHeightPx(content);
            float surfaceW = SurfaceReferenceWidthPx(surfaceH);
            float bodyW = surfaceW - safe.x - safe.y - 2f * pad;
            float bodyH = surfaceH - safe.z - safe.w
                        - NightMarketLayout.TopBarPx - NightMarketLayout.BottomBandPx;
            _plan = NightMarketComposition.Resolve(bodyW, bodyH);
            _builtForSurface = new Vector2Int(ElarionUiKit.SurfaceWidth, ElarionUiKit.SurfaceHeight);
            _topBar = Region(_screen, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(pad, -NightMarketLayout.TopBarPx), new Vector2(-pad, 0f));
            _bodyHost = Region(_screen, "Body", Vector2.zero, Vector2.one,
                new Vector2(pad, NightMarketLayout.BottomBandPx), new Vector2(-pad, -NightMarketLayout.TopBarPx));
            _bottomBand = Region(_screen, "BottomBand", Vector2.zero, new Vector2(1f, 0f),
                new Vector2(pad, 0f), new Vector2(-pad, NightMarketLayout.BottomBandPx));

            BuildHeader(_topBar);

            // ── BODY. WIDTH IS THE ABUNDANT AXIS — SPEND IT ───────────────────
            // Landscape gives ~2120 horizontal units against 978 vertical. Every one of the
            // 2026-08-22 defects was a VERTICAL crowding defect, so the fix is to move work
            // sideways. HOW MANY COLUMNS that buys is no longer assumed: the plan resolved above
            // says three (rails at comfort width), three (rails narrowed to their content minimum),
            // or two (commerce stacked under the spotlight) — chosen so a card, a glyph and a tap
            // target never shrink to keep a column.
            // ⛔ ONE OWNER PLACES THE COLUMNS. NightMarketComposition.Compose is the same call the
            // runtime layout oracle makes, so what the oracle measures is what the player gets — an
            // oracle that placed its own rects would only be proving its own arithmetic.
            var columns = NightMarketComposition.Compose(_bodyHost, _plan);
            _spotlightHost = columns.Spotlight;
            _commerceHost  = columns.Commerce;
            _marketHost    = columns.Market;

            // Translucent stalls (§R6): the ground reads THROUGH the columns, so the screen is one
            // lit space rather than three opaque boxes on black.
            Plate(_spotlightHost, Translucent(NightMarketPalette.GroundRaised, 0.72f));
            Plate(_commerceHost, Translucent(NightMarketPalette.GroundRaised, 0.72f));

            // ⛔ ONE STATUS SURFACE, AND IT LIVES IN THE COMMERCE COLUMN (§3 / P1-5).
            // It used to be a full-width band across the top of the panel while the spotlight drew a
            // SECOND pending line of its own — the owner's frames show the two overlapping each other
            // and clipping under the band top. There is now exactly one, it sits beside the control
            // it describes, and it HOLDS STILL: nothing animates next to text read to make a decision
            // (Lane G rule 2).
            // ⛔ AND IT IS AUTHORED IN PIXELS AGAINST THE PLAN, NOT AS 0.62-0.97 OF THE COLUMN.
            // In the stacked two-column composition the commerce rail is ~254 units tall, where
            // "35% of the column" is 89 px — under two line boxes, i.e. a status message that
            // silently loses its second line. The plan states the band's height; the band takes it.
            bool landscapeRail = _plan.Mode == NightMarketMode.WideThreeColumn ||
                                 _plan.Mode == NightMarketMode.CompactThreeColumn;
            // Landscape commerce has no CTA inside this host (the CTA lives in
            // _bottomBand), so use its full vertical budget. The old 14% dead strip
            // hid the third catch-up offer at supported ultrawide resolutions.
            float utilityFloor = landscapeRail ? 0.02f : 1f;
            var statusBand = Region(_commerceHost, "CommerceStatus",
                landscapeRail ? Vector2.zero : new Vector2(0f, 1f),
                landscapeRail ? new Vector2(1f, 0.14f) : new Vector2(1f, 1f),
                landscapeRail ? new Vector2(NightMarketComposition.CommerceGutterPx, 0f)
                              : new Vector2(NightMarketComposition.CommerceGutterPx, -_plan.StatusBandPx),
                landscapeRail ? new Vector2(-NightMarketComposition.CommerceGutterPx, 0f)
                              : new Vector2(-NightMarketComposition.CommerceGutterPx, 0f));
            _statusBanner = MakeText(statusBand, string.Empty, 30, ElarionUi.Gold,
                FontStyles.Normal, TextAlignmentOptions.TopLeft,
                Vector2.zero, Vector2.one);

            // The CTA sub-host. Cleared and rebuilt per focus so the spotlight column never has to be
            // torn down to repaint a button — and so the status surface above it SURVIVES a rebuild.
            // ⛔ WO-1636 — THE LANDSCAPE CTA SEAT IS PX WIDE NOW, AND THAT IS THE SAME BUG THE
            // HEIGHT ABOVE ALREADY FIXED. The stacked seat has always taken its HEIGHT in px
            // (_plan.CtaHostPx, right below); the landscape seat took its WIDTH as .30-.50 of the
            // bottom band — 20% of a band whose reference width moves with the aspect. At 1280x720
            // that is 375 px, the button inside resolves to 335, its label to 308, and "CONNECT
            // WALLET" drew 12 of 13 printable glyphs (Builds/wave3-capture2.glyph-findings.txt:58):
            // the one control on this screen that takes money, missing a letter.
            //
            // ⛔ AND IT IS SEATED BETWEEN ITS TWO NEIGHBOURS, WITH A STATED ORDER OF PRECEDENCE.
            // The bottom band holds three tenants and they are in it TOGETHER: the canon Close on
            // the left (SeatCloseInBottomBand, centred at BottomBandCloseCentreFrac x CanonCtaWidth),
            // this seat, and the legal copy from BottomNoticeLeftFrac rightward
            // (BuildLandscapeBottomNotice). The rule, in priority order:
            //
            //   1. The CLOSE never moves. It is the way out of a full-screen modal.
            //   2. The BUY CONTROL is seated at CANON WIDTH or it is not seated in this band at
            //      all. It never shrinks — a sub-MinTouchPx Buy control is a dead control, and
            //      ClampMinTouch would then GROW it over whatever is beside it.
            //   3. The LEGAL COPY yields. It is fine print with an autosize band; it gives ground
            //      before the control the screen exists for does.
            //   4. If even a yielded notice cannot leave the copy a readable width, the CTA LEAVES
            //      the band and STACKS into the commerce rail — the same seat the stacked
            //      composition already uses, which NightMarketUiRegression.CheckCommerceCta already
            //      guarantees is at least CanonCtaWidth wide. A designed relocation, not a collapse.
            //
            // ⛔ AND THE BAND WIDTH IS THE PLAN'S, NOT `_bottomBand.rect.width`. WO-1636's first
            // attempt measured the rect and shipped nine UI_GEOMETRY_FAILs (chain 17): at 800x360
            // the Buy control resolved to 26.9 x 119 ref px — 85 px UNDER MinTouchPx — because at
            // build time the rect still reads RAW SCREEN PIXELS (~764) rather than the ~2110
            // reference px the canvas scaler will give it. That is exactly why this file already
            // derives every other width from SurfaceReferenceWidthPx instead of a rect
            // (NightMarketLayout's own header says so), and `bodyW` above IS that derivation:
            // _bottomBand is _screen inset by `pad` on each side, so its reference width is bodyW.
            float bandW = Mathf.Max(1f, bodyW);
            float closeRightPx = NightMarketLayout.BottomBandCloseCentreFrac * bandW
                               + 0.5f * ElarionUiKit.CanonCtaWidth;
            float gapLeftPx = closeRightPx + NightMarketLayout.BottomBandKeepOutPx;
            float noticeLeftPx = NightMarketLayout.BottomNoticeLeftFrac * bandW;
            float gapRightPx = noticeLeftPx - NightMarketLayout.BottomBandKeepOutPx;
            bool ctaStacks = false;

            if (landscapeRail && gapRightPx - gapLeftPx < NightMarketComposition.CommerceMinPx)
            {
                // Rule 3 — the legal copy yields.
                float yieldedNoticeLeftPx = gapLeftPx + NightMarketComposition.CommerceMinPx
                                          + NightMarketLayout.BottomBandKeepOutPx;
                if (bandW - yieldedNoticeLeftPx >= NightMarketLayout.BottomNoticeMinPx)
                {
                    FlowTrace.Warn("Store", $"CommerceCta seat: the {bandW:0}px bottom band cannot hold the Close, a " +
                                            $"canon Buy control and the legal copy at its authored " +
                                            $"{NightMarketLayout.BottomNoticeLeftFrac:0.##} edge, so the LEGAL COPY " +
                                            $"YIELDS from {noticeLeftPx:0}px to {yieldedNoticeLeftPx:0}px. The " +
                                            "fine print gives ground before the Buy control does (rule 3).");
                    noticeLeftPx = yieldedNoticeLeftPx;
                    gapRightPx = noticeLeftPx - NightMarketLayout.BottomBandKeepOutPx;
                }
                else
                {
                    // Rule 4 — relocate, never shrink.
                    ctaStacks = true;
                    FlowTrace.Warn("Store", $"CommerceCta seat: the {bandW:0}px bottom band cannot seat the Close, a " +
                                            $"canon {NightMarketComposition.CommerceMinPx:0}px Buy control AND " +
                                            $"{NightMarketLayout.BottomNoticeMinPx:0}px of readable legal copy. The " +
                                            "Buy control LEAVES the band and stacks into the commerce rail at full " +
                                            "canon size (rule 4) — it is never narrowed, because a Buy control under " +
                                            "MinTouchPx is a dead control the clamp would grow over its neighbour.");
                }
            }

            // The notice's edge is handed downstream as a FRACTION of the band — BuildLandscapeBottomNotice
            // authors anchors, and re-dividing by a rect there would reintroduce the raw-pixel bug.
            _bottomNoticeLeftFrac = Mathf.Clamp01(noticeLeftPx / bandW);

            if (landscapeRail && !ctaStacks)
            {
                // Canon width, centred in the gap so it sits where the retired .30-.50 pair put it
                // without ever touching either neighbour.
                float seatCentrePx = 0.5f * (gapLeftPx + gapRightPx);
                float seatLeftPx = seatCentrePx - 0.5f * NightMarketComposition.CommerceMinPx;
                float seatRightPx = seatLeftPx + NightMarketComposition.CommerceMinPx;
                _ctaHostWidthPx = NightMarketComposition.CommerceMinPx;
                _ctaHost = Region(_bottomBand, "CommerceCta", new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(seatLeftPx, 0f), new Vector2(seatRightPx, 0f));
                float leftClear = seatLeftPx - closeRightPx;
                float rightClear = noticeLeftPx - seatRightPx;
                FlowTrace.Step("Store", $"WO-1636 CommerceCta seat: band {bandW:0}px, Close ends {closeRightPx:0}, " +
                                        $"seat {seatLeftPx:0}..{seatRightPx:0} ({_ctaHostWidthPx:0}px = canon), legal " +
                                        $"copy starts {noticeLeftPx:0} — clearances {leftClear:0}px left / " +
                                        $"{rightClear:0}px right. Reference px from the plan, never from a rect that " +
                                        "still reads raw screen pixels at build time.");
                if (leftClear < NightMarketLayout.BottomBandKeepOutPx - 0.5f ||
                    rightClear < NightMarketLayout.BottomBandKeepOutPx - 0.5f)
                    FlowTrace.Fail("Store", $"CommerceCta seat: clearances {leftClear:0}px / {rightClear:0}px are under " +
                                            $"the {NightMarketLayout.BottomBandKeepOutPx:0}px keep-out — the Buy control " +
                                            "is touching the Close or the legal copy, which is a LayoutOracle Assert B " +
                                            "overlap waiting to happen. The seat arithmetic above is wrong for this band.");
            }
            else
            {
                _ctaHostWidthPx = Mathf.Max(1f, _plan.CommerceWidthPx);
                _ctaHost = Region(_commerceHost, "CommerceCta", Vector2.zero, new Vector2(1f, 0f),
                    Vector2.zero, new Vector2(0f, _plan.CtaHostPx));
            }

            if (landscapeRail)
            {
                // ── WO-1334 defect #2 — MEASURED, NOT FIXED. READ THIS BEFORE RE-SPLITTING. ──
                //
                // The owner photographed "CLOSE THE GAP" apparently overlapping "MONTHLY LEDGER",
                // hiding about half its label.
                //
                // ⛔ IT IS NOT AN OVERLAP, AND TREATING IT AS ONE IS THE TRAP. Both rails are
                // RectMask2D scroll columns and these two regions are DISJOINT (0.64-1.00 and
                // 0.02-0.63): nothing is drawn on top of anything. The ACTIONS rail is simply
                // SHORTER THAN ITS OWN CONTENT, so its last row is cut by its own mask - and what
                // the eye reads directly under a cut-off row is the next rail's heading.
                //
                // ⛔ AND THE COLUMN IS OVERSUBSCRIBED, SO NO RE-SPLIT CAN FIX IT. Measured against
                // NightMarketLayout (978 - 100 - 132 = 746 body px, of which 0.02..1.00 = 731 is
                // available) and the rails' own layout numbers (64 px heading, MinTouchPx+8 rows,
                // 6 px spacing, 8 px padding):
                //     ACTIONS       = 64 + 2*120 + 2*6 + 16 = 332 px   (rail has 268 - 64 SHORT)
                //     CLOSE THE GAP = 64 + 3*120 + 3*6 + 16 = 458 px   (rail has 455 - 3 short)
                //     total 790 px into 731 px = 59 px OVERSUBSCRIBED
                // The three catch-up rows are real: packs.json carries THREE "band": "gap" rows.
                // Widening ACTIONS therefore only moves the clipping onto the third catch-up offer
                // - which is the exact regression the `utilityFloor` comment above records as
                // already having been fixed once. A re-split trades a clipped nav door for a
                // clipped offer and reports itself as a fix.
                //
                // ⭐ THE TWO CANDIDATE FIXES, both needing an owner/design call this WO did not have:
                //   (a) DROP THE "ACTIONS" HEADING. It labels two buttons that already read REDEEM
                //       and MONTHLY LEDGER, and it frees 64+6 = 70 px - which closes the 59 px gap
                //       exactly, with nothing else moved. Cheapest, but it deletes a word the owner
                //       did not ask to lose.
                //   (b) ONE scroll column for both rails, headings inline. Then the only mask edge
                //       is the bottom of the column, nothing is ever cut mid-rail, and the list
                //       scrolls as one. Structurally right; touches _utilityContent /
                //       _gapUtilityContent / _persistentUtilityChildren and Render()'s clear rule.
                var actionsHost = Region(_commerceHost, "LandscapeActions",
                    new Vector2(0f, 0.64f), Vector2.one, Vector2.zero, Vector2.zero);
                var gapHost = Region(_commerceHost, "LandscapeGap",
                    new Vector2(0f, utilityFloor), new Vector2(1f, 0.63f), Vector2.zero, Vector2.zero);
                _utilityContent = BuildScrollColumn(actionsHost);
                _gapUtilityContent = BuildScrollColumn(gapHost);
                BuildLandscapeActions(PromoStrings.Get(PromoStrings.KeyEntry));
                BuildLandscapeGapOffers();
                _persistentUtilityChildren = _utilityContent != null ? _utilityContent.childCount : 0;
            }

            _shelfContent = BuildScrollColumn(_marketHost);

            // ── BAND 1 — FREE TONIGHT, built HERE and never rebuilt ──────────
            // ⛔ THE PROMO DOOR IS CONSTRUCTED AT BUILD TIME, NOT AT RENDER TIME, AND THAT IS THE
            // WHOLE POINT. Render() clears and rebuilds the priced bands from the catalogue; if the
            // free band were rebuilt with them, an empty catalogue, an early Render bail or a single
            // failed card would take the promo system's only player entry point down with it — which
            // is the exact state the promo-redeem-door WO closed, and PromoRedeemEntryRegression
            // pins it here on purpose. Built once, first, unconditionally: there is NO branch it can
            // fall out of, and it is deliberately OUTSIDE the purchase-flag test because redeeming
            // spends no money and must work today with buying disabled.
            //
            // Render() protects it by clearing only the children ABOVE _persistentShelfChildren, so
            // Free also stays FIRST in the fixed band order for free — nothing is asked for before
            // something is given.
            if (!landscapeRail)
                BuildFreeBand(PromoStrings.Get(PromoStrings.KeyEntry));
            _persistentShelfChildren = _shelfContent != null ? _shelfContent.childCount : 0;

            if (landscapeRail) BuildLandscapeBottomNotice(_bottomBand);
            else BuildTrustStrip(_bottomBand);
            SeatCloseInBottomBand();

            _modal.canvas.SetActive(false);   // built hidden; OnEnable shows it

            if (_shelfContent == null)
                FlowTrace.Fail("Store", "EnsureBuilt: shelf container is null after build — cards cannot render (blank store).");
            else if (_statusBanner == null)
                FlowTrace.Warn("Store", "EnsureBuilt: _statusBanner is null after build — purchase status/errors will have no on-screen surface.");
            else
                FlowTrace.Step("Store", "EnsureBuilt: Night Market built — header, spotlight, banded shelf, trust strip.");
        }

        /// <summary>
        /// The 100 ref px top bar: covenant left, the ONE wordmark centre (seated by
        /// <see cref="SeatWordmark"/>), the read-only wallet mirror right.
        /// <para>⛔ DISPLAY ONLY. UI-001 §8 puts y 0-240 screen px in the HARD-REACH zone: it is read,
        /// never tapped. Nothing in this band is a Button, and nothing that takes an action may be
        /// moved into it — the commerce column exists precisely so actions live in the thumb arc.</para>
        /// </summary>
        private void BuildHeader(Transform host)
        {
            if (host == null) return;

            // ⛔ WO-1334b — THE COVENANT PLAQUE AND THE BALANCE CHIP SWAPPED ENDS, and the plaque is
            // the one that moved because the owner named the other one by position: *"in the top
            // left put their balance of what they have just in SKR so they know what they can
            // afford immediately"*. The top-left corner is where a storefront puts the spendable
            // figure, for the reason she gave herself (*"I see every other site in the world does
            // it"*) — it is the first thing the eye lands on when the panel opens, and "what can I
            // afford" is the question the player arrives with. Nothing else on this screen has a
            // stronger claim to that corner, so the plaque — decorative, fixed, read second — takes
            // the right end the chip vacated. It keeps its 0.19 WIDTH so its authored aspect is
            // untouched; only its x offset changed. The wordmark spans x 0.25-0.68 and is not
            // disturbed in either direction.
            AddArt(host, "covenant-plaque", new Vector2(0.795f, 0f), new Vector2(0.985f, 1f));
            AddArt(host, "night-market-wordmark", new Vector2(0.25f, -0.18f), new Vector2(0.68f, 1.20f));
            // ⛔ WO-1334 — THE TWO HEADER "CHIP" PLATES ARE GONE, AND network-frame IS A SAFETY FIX,
            // NOT A DECLUTTER. `network-frame.png` is AUTHORED ART that bakes the words "Mainnet",
            // a green dot and a "[READY] Ready" pill straight into the texture — so it printed
            // "Mainnet" over a DEVNET session, where the tokens are free and a purchase settles for
            // nothing. A network label that cannot be wrong is worse than no label: it is a
            // confident lie on the one surface that takes real money. The live network now comes
            // from `_wallet.NetworkLabel` in RenderBalanceLabel below, where it is READ, not baked.
            // The green dot went with it: the owner is red/green colourblind and a hue is not a
            // message (CLAUDE.md §7). `wallet-frame` never had a sprite at all — AddArt returned
            // false every time — so its only effect was to reserve x 0.69-0.84 for nothing while
            // the balance text was drawn across the SAME band, which is the overlap the owner
            // photographed.

            // The covenant, first in the reading path (§7's three-second read: 0-1s is wordmark +
            // YOUR balance + the covenant). It is the differentiator against every shop this screen
            // was benchmarked on, so it is not footer legalese here.
            // The owner's plaque carries this fixed covenant as authored art. It is intentionally
            // not printed a second time over the plaque.

            // ── The wallet mirror ────────────────────────────────────────────
            // ⛔ THE GAME NEVER HOLDS SKR AND MUST NEVER READ AS IF IT DOES. SKR is Solana Mobile's
            // own governance token — the owner did not mint it, does not own it, and is not
            // releasing a token of her own; it is the settlement rail a dApp Store title converts
            // out through. There is NO in-game SKR ledger, earn loop or spend loop and there must
            // never be one. This label is a READ-ONLY MIRROR of the player's OWN wallet, read
            // through the existing SolanaWalletProvider.GetBalance path. Never written, never
            // granted, never deducted in-game. The copy says "your wallet" for exactly that reason.
            //
            // ── WO-1334b — ONE LINE, TOP LEFT, ON ITS OWN READABLE GROUND ────
            //
            // ⛔ THIS SUPERSEDES THE SAME DAY'S FIRST PLACEMENT. WO-1334 read the owner's *"needs
            // moved left"* as a nudge and shipped x 0.62-0.955 — still the right-hand side. She then
            // said it plainly: *"in the top left put their balance of what they have just in SKR so
            // they know what they can afford immediately ... it shouldn't be on the top right hand
            // side and white where it's over top of everything else, because that's just ugly, it
            // doesn't make sense and you can't read it."* Three separate instructions, all binding:
            //
            //   1. TOP LEFT — not "left-ish". The rect below starts at x 0.018, the panel's own left
            //      margin, and ENDS at 0.315: entirely inside the left third, so no future widening
            //      of the string can walk it back toward the centre, let alone the right.
            //   2. THE WORD "Balance", then the total: `Balance: 3,817 SKR`. This RETIRES the
            //      earlier `SKR: <balance>` ruling she gave hours before — she reconsidered out
            //      loud, and the reason she gave is the one that makes it right: the word is the
            //      storefront convention (*"I see every other site in the world does it"*), and a
            //      bare `SKR: 3,817` asks the player to work out what the number is FOR. The
            //      sentence itself lives in canon-strings.json as `storeBalanceValue`.
            //   3. READABLE — a STATED DEFECT, not a nicety. Parchment-white text laid straight
            //      over the panel's busy art is what *"you can't read it"* describes. It now gets a
            //      ground: the same black ~66%-alpha plate this screen already uses behind its card
            //      text, so this is the established idiom rather than a new one invented here, and
            //      the text is GOLD BOLD on it for the same reason. ⛔ The legibility is carried by
            //      the PLATE (a luminance contrast), never by the hue — the owner is red/green
            //      colourblind (CLAUDE.md §7) and a gold-on-busy-art chip with no ground would read
            //      exactly as badly to her as the white one did.
            //
            // ⛔ THE PLATE IS DERIVED FROM THE LABEL'S OWN RECT, NEVER TYPED A SECOND TIME. A
            // hand-copied plate rect drifts off its text the first time anyone nudges either one,
            // and a half-covered chip is a worse artefact than no plate at all — on the one surface
            // that takes real money. PlateBehind reads the label's anchors and pads them, so moving
            // the chip moves its ground for free.
            //
            // ⚠ The kit fit's floor is passed EXPLICITLY. Its `minSize: 0` default resolves to
            // ElarionUiKit.FontFloor (30), NOT FontHardFloor (20) — a default that has already
            // ellipsised one label in this project. The explicit argument is here so nobody has to
            // re-derive which floor the default meant.
            //
            // ── WO-1636 — THIS LABEL WRAPS. IT DOES NOT ELLIPSIZE. ───────────
            //
            // ⛔ THE RULING ABOVE IS INTACT AND THE FIT IS WHAT CHANGED, so read the two apart. The
            // first glyph-oracle run measured this label on all four captured Night Market surfaces
            // (Builds/wave3-capture2.glyph-findings.txt:44, 50, 56, 59): the rect is 625 x 84 ref px
            // and the WALLETLESS BANNER branch of RenderBalanceLabel — "Connect a wallet to buy -
            // prices shown in USD" — drew 32 of 36 printable glyphs at 1920/2340/2670 and 28 of 36 at
            // 1280x720. The player read a sentence with its last word missing, on the money screen.
            //
            // ⚠ AND NEITHER OF THE FAMILY'S TWO USUAL FIXES IS AVAILABLE HERE, which is why this one
            // is a third. The band cannot be WIDENED: the ruling above puts this rect entirely inside
            // the left third (x .018-.315) and the wordmark art starts at x .25, so every px of width
            // walks into the ruling, the art, or both. The copy cannot be SHORTENED: it is
            // StoreStrings.WalletlessBrowsingBanner, probed by name in NightMarketNoWalletRegression
            // (:335), and re-voicing player copy is the owner's call, not a fit lane's. The font
            // cannot come down (WO-1636 §2). What is left is the axis nobody was using: the band is
            // 84 px tall — TopBarPx(100) x (.92 - .08) — and two line boxes at the floor are
            // 2 x LedgerRowBandPx = 78 px (the 1.30 line factor MEASURED off the 1280x720 panel,
            // not LineBoxPx's 1.25 assumption). The sentence fits on two lines with 6 px to spare,
            // and the Warn below fires if either number ever moves the wrong way.
            //
            // ⚠ RULING-ADJACENT, AND SAID SO RATHER THAN SLIPPED THROUGH. WO-1334b's wording is "ONE
            // LINE, TOP LEFT, ON ITS OWN READABLE GROUND". Its three named instructions are all
            // untouched — the rect, the word "Balance", the plate — and the BALANCE sentence it was
            // written about ("Balance: 3,817 SKR") still renders on one line, because it fits on one.
            // Only the walletless banner, which that ruling never saw, now takes a second line rather
            // than losing its last four characters. If the owner wants one line at any cost, the
            // remaining lever is her copy, and that is her call to make.
            // ⛔ THE TWO Vector2 LITERALS ON THE NEXT CALL ARE READ BY AN ORACLE — DO NOT HOIST THEM
            // INTO CONSTANTS. NightMarketUiRegression's `[top-left]` case parses this rect straight
            // out of the source text: AnchorsAfter (NightMarketUiRegression.cs:537, matcher at
            // :1079-1094) finds the assignment below by name and regexes the first two
            // `new Vector2(<n>f, <n>f)` in the 800 characters that follow, bounding them against the
            // owner's top-left ruling. WO-1636 first named these edges `headerBandY0/Y1` for
            // tidiness and the pin could no longer see them — NIGHT_MARKET_UI_FAIL, "could not
            // derive anchored rect", chain 17, 493/494. Literals here; the derivation below reads
            // the LABEL itself, so there is still no second copy of them anywhere.
            //
            // ⚠ AND THIS COMMENT DELIBERATELY DOES NOT SPELL THE MARKER STRING. The oracle takes
            // the FIRST IndexOf of it in the file, so a comment quoting it verbatim would move the
            // 800-character read window up here and could push the real anchors out of range — a
            // note about the pin breaking the pin.
            _balanceLabel = MakeText(host, string.Empty, 30, ElarionUi.Gold,
                FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0.018f, 0.08f), new Vector2(0.315f, 0.92f));
            ElarionUiKit.FitBlock(_balanceLabel, ElarionUi.FontFloorMobile, 30f);
            // The band px are read back off the label's OWN anchors — not a second copy of ".84"
            // in a log line, which is a copy that goes stale the first time either edge moves.
            var balanceRt = _balanceLabel.rectTransform;
            float headerBandPx = NightMarketLayout.TopBarPx * (balanceRt.anchorMax.y - balanceRt.anchorMin.y);
            float twoLinesPx = 2f * NightMarketComposition.LedgerRowBandPx;
            FlowTrace.Step("Store", "WO-1636 header line: band " + headerBandPx.ToString("0") +
                "px tall vs " + twoLinesPx.ToString("0") +
                "px for two line boxes at the floor — FitBlock (wrap+Truncate), not FitSingleLine " +
                "(NoWrap+Ellipsis), so the walletless banner keeps its last word.");
            if (headerBandPx < twoLinesPx)
                FlowTrace.Warn("Store", "BuildHeader: the header band is " + headerBandPx.ToString("0") +
                    "px and two line boxes need " + twoLinesPx.ToString("0") +
                    "px — the walletless banner will TRUNCATE its second line. Grow the band or " +
                    "take the copy to the owner; never drop the floor.");
            PlateBehind(_balanceLabel, Translucent(NightMarketPalette.Ground, 0.66f),
                        padX: 0.010f, padY: 0.03f);
        }

        /// <summary>
        /// Seat the kit's own title (and its drop-shadow twin) inside the top bar's centre third.
        /// <para>⛔ IT IS RE-ANCHORED, NEVER RE-CREATED. Header() builds the title spanning x
        /// 0.06-0.94 of the panel, which on a FULL-SCREEN panel is a rect straddling both the
        /// covenant and the wallet rail — three texts in one place, and AuditGeometry reports the
        /// overlap it deserves. Adding a second store-local title instead would reproduce defect #2
        /// ("The Night Market" printed twice). One title, moved.</para>
        /// </summary>
        private static void SeatWordmark(Transform content)
        {
            if (content == null) return;
            // The band's share of the LIVE canvas, not of the 2340x1080 one: on a 4:3 landscape the
            // reference canvas is ~1247 units tall, where 100/978 would seat the wordmark 27% too low.
            float bandTop = 1f - (NightMarketLayout.TopBarPx / SurfaceReferenceHeightPx(content));
            for (int i = 0; i < content.childCount; i++)
            {
                var label = content.GetChild(i).GetComponent<TextMeshProUGUI>();
                if (label == null) continue;
                // BuildHeader owns the illustrated wordmark in the live Night Market. Keep the
                // modal's canonical title object for accessibility/lifecycle, but do not double-draw it.
                label.color = new Color(label.color.r, label.color.g, label.color.b, 0f);
                var rt = label.rectTransform;
                rt.anchorMin = new Vector2(0.34f, bandTop);
                rt.anchorMax = new Vector2(0.66f, 1f);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                // Header()'s shadow copy is a dark, semi-transparent twin drawn 1.5 px behind the
                // gilt title. Re-anchoring both would stack them exactly; keep the offset so the
                // shadow still reads as a shadow.
                if (label.color.r < 0.25f && label.color.a < 0.85f)
                {
                    rt.offsetMin = new Vector2(1.5f, -1.5f);
                    rt.offsetMax = new Vector2(1.5f, -1.5f);
                }
                ElarionUiKit.FitSingleLine(label);
            }
        }

        /// <summary>
        /// Move the ONE canon Close into the ONE bottom band.
        /// <para>⛔ THIS IS WHAT MAKES A SECOND BAND IMPOSSIBLE. Canon (2026-07-03) bans an X-close
        /// and puts the Close bottom-centre; the kit therefore seats a fixed 360x132 box at 0.050 of
        /// the PANEL, which on a full-screen store is a button floating in its own reserved strip
        /// BELOW the trust strip — P1-6 exactly. Re-parenting it into the band makes the band and the
        /// button the same row: legal left, Close centre, promise right, 132 units, once.</para>
        /// <para>Position only. The canon SIZE is untouched — CanonCtaWidth/CanonCtaHeight are derived
        /// from by ~25 files and are not this screen's to re-tune — and the onClick wiring is the
        /// kit's, carried across by the reparent.</para>
        /// </summary>
        private void SeatCloseInBottomBand()
        {
            var close = _modal != null && _modal.chrome != null ? _modal.chrome.close : null;
            if (close == null || _bottomBand == null)
            {
                FlowTrace.Warn("Store", "SeatCloseInBottomBand: no close button or no bottom band — " +
                                        "the Close keeps the kit's default band and the single-band rule is NOT held.");
                return;
            }
            var rt = close.transform as RectTransform;
            if (rt == null) return;
            close.transform.SetParent(_bottomBand, false);
            _bottomBand.SetAsLastSibling();
            Plate(_bottomBand, new Color(NightMarketPalette.Ground.r,
                NightMarketPalette.Ground.g, NightMarketPalette.Ground.b, 0.98f));
            bool landscapeRail = _plan.Mode == NightMarketMode.WideThreeColumn ||
                                 _plan.Mode == NightMarketMode.CompactThreeColumn;
            // Keep the complete 360px control inside the left notice seat. At 1280-wide
            // landscape, 0.085 put the button centre closer to the edge than its own
            // half-width and visibly cut away the frame.
            // WO-1636: the landscape centre is read from NightMarketLayout, because the CommerceCta
            // seat is derived from where this button ENDS. A literal here and a derivation there is
            // how the seat and the Close drift into each other.
            float closeX = landscapeRail ? NightMarketLayout.BottomBandCloseCentreFrac : 0.5f;
            rt.anchorMin = new Vector2(closeX, 0.5f);
            rt.anchorMax = new Vector2(closeX, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(ElarionUiKit.CanonCtaWidth, ElarionUiKit.CanonCtaHeight);
            close.transform.SetAsLastSibling();
            MedievalUiSkin.ApplyButton(close, primary: true);
            // Some imported button-state sprites contain no baked label. Keep the action
            // runtime-authored and explicit so hover/selected art can never turn Close into
            // the blank framed slab caught by the screenshot gate.
            var closeLabel = close.GetComponentInChildren<TMP_Text>(true);
            if (closeLabel != null)
            {
                closeLabel.gameObject.SetActive(true);
                closeLabel.text = "CLOSE";
                closeLabel.color = ElarionUi.Parchment;
                closeLabel.raycastTarget = false;
                ElarionUiKit.FitSingleLine(closeLabel, ElarionUi.FontFloorMobile, 38f);
                closeLabel.transform.SetAsLastSibling();
            }
            FlowTrace.Step("Store", "SeatCloseInBottomBand: canon Close seated INSIDE the single 132-unit bottom band.");
        }

        private void BuildLandscapeBottomNotice(Transform host)
        {
            if (host == null) return;
            // Legal copy begins after the CTA's keep-out; the prior .34 start drew two sentences
            // through the CTA at every supported ratio.
            // ⚠ WO-1636 — THIS EDGE IS NOW LOAD-BEARING IN BOTH DIRECTIONS, and the old comment here
            // ("The landscape CTA owns x .30-.50") was a claim nothing enforced. The CTA seat is
            // authored in px between the Close and this copy, and when the band is too narrow to
            // hold all three THIS COPY IS THE TENANT THAT YIELDS (rule 3 at the seat). So the left
            // edge is taken from `_bottomNoticeLeftFrac`, which the seat resolved — one owner for the
            // number instead of two literals that agreed by hand until an aspect changed.
            // ⚠ A FRACTION, RESOLVED FROM PX BY THE SEAT — never re-divided by a rect here. The
            // rect at build time still reads raw screen pixels, which is the whole reason the seat
            // above stopped measuring one.
            float noticeLeftFrac = _bottomNoticeLeftFrac > 0f
                ? _bottomNoticeLeftFrac
                : NightMarketLayout.BottomNoticeLeftFrac;
            // The two labels keep their authored SHARE of whatever is left, so a yielded notice
            // narrows evenly instead of one label eating the other's room.
            float noticeSpan = Mathf.Max(0.001f, 0.995f - NightMarketLayout.BottomNoticeLeftFrac);
            float remaining = Mathf.Max(0.001f, 0.995f - noticeLeftFrac);
            float Reflow(float authored) =>
                noticeLeftFrac + (authored - NightMarketLayout.BottomNoticeLeftFrac) / noticeSpan * remaining;
            var marketLine = MakeText(host, PackCatalog.CurrencyDisclaimer, 19,
                ElarionUi.ParchmentDim, FontStyles.Normal, TextAlignmentOptions.Left,
                new Vector2(noticeLeftFrac, 0.18f), new Vector2(Reflow(0.68f), 0.82f));
            var feeLine = MakeText(host, StoreStrings.Get(StoreStrings.KeyTrustFee), 18,
                ElarionUi.Gold, FontStyles.Bold, TextAlignmentOptions.Right,
                new Vector2(Reflow(0.69f), 0.18f), new Vector2(0.995f, 0.82f));
            ElarionUiKit.FitSingleLine(marketLine, 12f, 19f);
            ElarionUiKit.FitSingleLine(feeLine, 11f, 18f);
        }

        /// <summary>
        /// Lane E — the verifiable claims, and §R2's ONE bottom band. Legal LEFT, the canon Close
        /// CENTRE, the promise RIGHT: one 132-unit row, not a trust strip with a close band under it.
        ///
        /// <para>⛔ THE CENTRE OF THIS BAND IS RESERVED AND THAT IS LOAD-BEARING. The Close is a
        /// 360-px visible button seated dead centre; any copy that reaches under it is reported by
        /// AuditGeometry rule 3 as BUTTON OVER TEXT, and on a device it is simply unreadable. The
        /// keep-out is derived from <see cref="ElarionUiKit.CanonCtaWidth"/> rather than typed as a
        /// fraction, so a future change to the canon button width moves the copy with it.</para>
        ///
        /// <para>Nothing here animates: it is the part of the screen a sceptical player reads slowest.
        /// The COVENANT is not repeated here — it is the top bar's first line (§7's three-second
        /// read). Printing it twice on one screen would blunt the one sentence this shop is built
        /// around; all four claims are still on screen, once each.</para>
        /// </summary>
        private void BuildTrustStrip(Transform host)
        {
            if (host == null) return;

            // ⛔ THE COPY IS NOT AUTHORED HERE ANY MORE. StoreLegalFooter is the ONE owner of the
            // store's legal/promise band — its four claims, the market disclaimer, and the keep-out
            // the canon Close carves out of the centre of this row. It used to live inline in this
            // method, which meant PackStore was the only surface that could ever print the claims
            // correctly while SeasonTrackPanel already re-printed one of the same strings with its
            // own geometry. One owner per concern (CLAUDE.md §7).
            // ⚠ THE BAND'S REAL WIDTH, NOT THE 2120 REFERENCE. The Close keep-out is a FRACTION of
            // whatever this band turned out to be; handing it a constant 2120 on a narrower surface
            // under-reserved the centre and let the claim lane reach under the canon Close.
            _legalFooter = StoreLegalFooter.Build(host, _plan.BodyWidthPx,
                Shorten(WalletService.RewardsDistributorAddress));
        }

        /// <summary>
        /// Opens the Redeem-a-Code overlay on THIS store's canvas.
        /// </summary>
        private void OpenRedeemPanel()
        {
            if (_modal == null || _modal.canvas == null)
            {
                FlowTrace.Fail("Store", "Redeem tapped but the store modal is gone — cannot host the redeem overlay.");
                return;
            }
            if (_redeem == null) _redeem = new RedeemCodePanel(_modal.canvas.transform);
            _redeem.Open();
        }

        /// <summary>
        /// Closes the store exactly the way MarketplaceInteractor does. PackStore lives in
        /// DeNelle.Wallet, which CANNOT reference DeNelle.Village (one-way asmdef dependency:
        /// Village → Wallet), so the VM drives the interactor's private CloseStore() — that path
        /// re-enables HeroLocomotion AND clears _storeOpen, so the hero is never left frozen.
        /// This is the soft-lock guard and WO-1050 does not touch it.
        /// </summary>
        private void CloseStore()
        {
            // WO-1412: the sending Manage surface owns the return door. Closing this panel through
            // its established lifecycle reaches OnDisable -> PanelManager.NotifyClosed, which arms
            // and consumes that SAME WO-1400 arbiter after the close-grace frame. Do not open a
            // second, store-owned return route here.
            string returnDoor = PanelManager.ReturnDoorName;
            FlowTrace.Step("Store", "close -> return opener=" +
                                    (string.IsNullOrEmpty(returnDoor) ? "HUD" : returnDoor));
            if (_vm == null) _vm = PackStoreVM.CreateDefault();
            if (!_vm.CloseViaInteractor())
                gameObject.SetActive(false);
        }

        /// <summary>WO-1409's once-per-open proof: one wallet explanation and USD anchors.</summary>
        private void TraceShelfStateOnOpen()
        {
            bool walletless = WalletlessBrowsing;
            int anchors = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !PackCatalog.IsOnBrowsableShelf(pack)) continue;
                string price = StorePriceMajor(pack);
                if (!string.IsNullOrEmpty(price) && price.IndexOf('$') >= 0) anchors++;
            }
            // ⛔ WO-1409 — `banner=` READS THE LABEL, IT DOES NOT RESTATE THE PREDICATE. It used to
            // print `walletless` a second time, so the line said "a banner is owed" and was read as
            // "a banner is on screen" — and those were NOT the same fact: the 09-05 23:56 capture
            // is walletless with no banner drawn. A trace that can only agree with itself proves
            // nothing (§11B), so this asks the rendered TMP_Text what it actually says.
            bool bannerDrawn = _balanceLabel != null && _balanceLabel.text != null &&
                               _balanceLabel.text.IndexOf(StoreStrings.WalletlessBrowsingBannerProbe,
                                                          StringComparison.OrdinalIgnoreCase) >= 0;
            FlowTrace.Step("Store", "shelf wallet=" + (!walletless).ToString().ToLowerInvariant() +
                                    " anchors=" + anchors + " banner=" + bannerDrawn.ToString().ToLowerInvariant());
            if (walletless && !bannerDrawn)
                FlowTrace.Warn("Store", "shelf: browsing WITHOUT a signing wallet and the header line does " +
                                        "not carry the connect sentence - the player is shown nine USD anchors " +
                                        "and never told why they cannot buy (WO-1409). Header reads: '" +
                                        (_balanceLabel == null ? "<no label>" : _balanceLabel.text) + "'.");
        }

        // =====================================================================
        //  Rendering — the shelf
        // =====================================================================

        /// <summary>Rebuilds the banded shelf and the spotlight from the canonical catalogue.</summary>
        public void Render()
        {
            using var _ = FlowTrace.Enter("Store", "Render");
            if (_shelfContent == null)
            {
                FlowTrace.Warn("Store", "Render: modal not built yet (lazy) — skipped.");
                return;
            }

            // ⛔ CLEAR ONLY THE PRICED BANDS. The first _persistentShelfChildren children are the
            // FREE band, built once in EnsureBuilt so no render failure can take the promo door with
            // it (see the note at that build site).
            for (int i = _shelfContent.childCount - 1; i >= _persistentShelfChildren; i--)
                Destroy(_shelfContent.GetChild(i).gameObject);
            _cardHandles.Clear();
            _sharedOfferCards.Clear();

            // ⛔ AND REBUILD THE CATCH-UP RAIL, BECAUSE IN LANDSCAPE IT IS THE GAP BAND (WO-1339).
            //
            // THE DEFECT, read at source: in the landscape composition the Gap band is NOT drawn on
            // the shelf at all — the band walk below does `if (_utilityContent != null && band ==
            // StoreBand.Gap) continue;` and the three "Close the Gap" rows live in the right-hand
            // rail instead. That rail was built ONCE, in EnsureBuilt, and NOTHING ever rebuilt it.
            //
            // EnsureBuilt runs BEFORE the first RefreshQuotedPrices, so every gap row was stamped
            // with the price string that existed when no server quote had arrived yet — which is the
            // WORDS "Price unavailable" (SolanaPackPricing.AmountLabel returns them for a 0 amount,
            // deliberately). When the quote list then landed, RefreshQuotedPrices called Render()
            // and Render() repainted Basket and Patronage with real figures and SKIPPED the rail.
            // The store host is spawned once and kept for the session, so EnsureBuilt never ran
            // again either: the three impulse packs read "Price unavailable" for the whole session,
            // beside packs showing real prices two inches away. Exactly the shape of the WO-1190 and
            // WO-1334 defects this file already documents — a value resolved once and then kept is a
            // stale measurement.
            //
            // The rail is rebuilt here, from the same PacksInBand(Gap) rows and the same
            // StorePriceMajor call, so a returning quote repaints the catch-up offers with
            // everything else. It is also what makes the rail recover if the catalogue was not
            // readable at build time (rows.Count == 0 used to mean the heading never appeared
            // again). ⚠ ONLY the gap column is cleared: _utilityContent (ACTIONS / REDEEM /
            // MONTHLY LEDGER) is a SEPARATE transform and is deliberately untouched, because those
            // are navigation doors that must survive a catalogue failure.
            RebuildLandscapeGapOffers();

            StoreLegalFooter.RefreshDisclaimer(_legalFooter);

            // Shared ledger scale, computed ONCE per render over every browsable pack. Per GOOD, not
            // global: one scale across all goods would make every crystals bar a sliver next to a
            // wood bar and the comparison it exists to make would be lost.
            var scale = BuildLedgerScale();

            int built = 0;
            // ⛔ FIXED BAND ORDER, AND IT IS THE ENUM ORDER, NOT A SORT. Free is first because
            // nothing is asked for before something is given; Patronage is last so it is the read on
            // the way out. (StoreBand's own declaration order is the authority — see PackCatalog.)
            // StoreBand.Free is ABSENT from this walk on purpose: it is already on the shelf, built
            // once in EnsureBuilt and preserved above. The order is still Free -> Gap -> Basket ->
            // Patronage because Free occupies the first children and these append after them.
            foreach (StoreBand band in new[] { StoreBand.Gap, StoreBand.Basket, StoreBand.Patronage })
            {
                // In the reference landscape, single-resource catch-up offers live in the right
                // utility rail. They remain the same PackDef rows and use the same focus/purchase
                // path; only their presentation changes.
                if (_utilityContent != null && band == StoreBand.Gap) continue;
                var rows = PacksInBand(band);
                if (rows.Count == 0) continue;

                BuildBandHead(band);

                // ⛔ THE ROW IS AUTHORED AT THE CARD'S OWN HEIGHT. Two places holding one
                // measurement is how a 100-unit row came to carry a 168-unit card.
                var variant = VariantFor(band);

                int cardsPerRow = ShelfCardsPerRow;
                for (int i = 0; i < rows.Count; i += cardsPerRow)
                {
                    // ⛔ THE ROW IS SIZED FOR THE TALLEST CARD IT WILL HOLD. A card that carries the
                    // not-sellable state line is ReasonExtraPx taller than a buyable one, and the
                    // strip force-expands its children to the row's own height — so a row measured
                    // before the cards are resolved would squeeze that block back out again, which
                    // is the two-places-hold-one-measurement defect BuildCardRow's header records.
                    bool rowHasReason = false;
                    for (int c = 0; c < cardsPerRow && i + c < rows.Count; c++)
                        if (!string.IsNullOrEmpty(CardNotSellableReason(rows[i + c]))) rowHasReason = true;

                    float rowHeight = StorePackCard.CardHeight(variant, rowHasReason);
                    var strip = BuildCardRow(rowHeight);
                    for (int c = 0; c < cardsPerRow; c++)
                    {
                        if (i + c < rows.Count)
                        {
                            if (BuildPackCard(strip, rows[i + c], band, variant) != null) built++;
                            else FlowTrace.Warn("Store", $"Render: BuildPackCard returned null for pack '{rows[i + c].Sku}' — card skipped.");
                        }
                        else
                        {
                            // A lone card must stay card-sized, not stretch to fill the strip.
                            Spacer(strip);
                        }
                    }
                }
            }

            if (built == 0)
                FlowTrace.Fail("Store", "Render: built 0 pack cards — shelf is EMPTY (no packs in catalogue or all cards failed).");
            else
                FlowTrace.Step("Store", $"Render: built {built} pack card(s) across the four bands.");

            BuildPiShelfNoticeIfNothingIsBuyable();

            // ⛔ WO-1409 — THE HEADER LINE IS A REPAINT, NOT A LIFECYCLE SIDE EFFECT, and that is
            // the whole defect. `_balanceLabel` is born EMPTY (BuildHeader below) and its ONLY
            // writer was RenderBalanceLabel, reached exclusively through RefreshWalletMirror() in
            // OnEnable. So every path that draws this store WITHOUT OnEnable drew a store whose
            // top-left line was the empty string — and the walletless banner
            // ("Connect a wallet to buy - prices shown in USD") is one of that method's branches.
            // Measured on Builds/ui-capture/NightMarket_2670x1200.png (09-05 23:56, i.e. AFTER the
            // copy landed): nine USD anchors, no banner. The headless capture harness composes the
            // panel by Awake -> EnsureBuilt -> Render (UICaptureLaunch.cs:3688-3730) and never
            // enables the object, which is exactly such a path — and so is any future host that
            // re-renders a store that is already enabled.
            //
            // The call is idempotent and costs nothing: RenderBalanceLabel returns on its first
            // line when the label is absent (see its null guard) and only assigns a string
            // otherwise. It does NOT read the wallet — the async mirror still owns that — so this
            // cannot slow the render or reach the network. It simply makes "what the header says"
            // a function of state that every draw evaluates, instead of a value one hook happened
            // to leave behind.
            RenderBalanceLabel();

            FocusPack(ResolveFocusSku(), scale, animate: false);
        }

        /// <summary>
        /// WO-1323 — when a PI player is looking at a shelf on which NOTHING is Pi-purchasable, say
        /// so, in words, at the bottom of the shelf.
        ///
        /// <para>⛔ THIS IS THE HONEST ANSWER TO THE WO'S OPEN QUESTION, AND IT IS DELIBERATELY NOT
        /// THE CONVENIENT ONE. Exactly one sku is Pi-quotable today (<c>hearth-spark</c>) and it
        /// carries <c>storeVisible:false</c> from WO-1069, so the Pi shelf can genuinely have nothing
        /// on it that a Pi player may buy. The two shortcuts both had to be refused: flipping
        /// <c>storeVisible</c> would reverse a pricing ruling with a display change, and falling back
        /// to the SKR figures is the defect this whole work order exists to remove. So the empty
        /// state is SHOWN as an empty state. The cards above it stay fully browsable and fully priced
        /// in USD — nothing is hidden, and nothing is required to play.</para>
        ///
        /// <para>Never built under the SKR skin: <see cref="PiDisplay"/> is false there and this
        /// method returns on its first line, which is why that shelf is unchanged.</para>
        /// </summary>
        private void BuildPiShelfNoticeIfNothingIsBuyable()
        {
            if (!PiDisplay || _shelfContent == null) return;

            int buyable = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !PackCatalog.IsOnBrowsableShelf(pack)) continue;
                if (PiCanSell(pack)) buyable++;
            }
            if (buyable > 0)
            {
                FlowTrace.Step("Store", $"Pi shelf: {buyable} browsable sku(s) are on the Pi rail — no empty-shelf notice.");
                return;
            }

            // ⛔ WO-1323 OWNER RULING — THE SPOTLIGHT COUNTS AS REACHABLE, SO THE NOTICE MUST STAND
            // DOWN. Saying "nothing here can be bought with Pi" beside a live Buy control in the
            // spotlight column would be the store contradicting itself on the same screen, which is
            // worse than either state alone. The two answers come from ONE method
            // (ResolvePiSpotlightPack) so they cannot drift apart: the same object that earns the
            // spotlight is the object that silences this notice.
            var spotlight = ResolvePiSpotlightPack();
            if (spotlight != null)
            {
                FlowTrace.Step("Store",
                    $"Pi shelf: nothing on the browsable shelf is on the Pi rail, but the spotlight carries " +
                    $"'{spotlight.Sku}' and it IS buyable - the empty-shelf notice is suppressed (WO-1323 ruling).");
                return;
            }

            FlowTrace.Warn("Store",
                "Pi shelf: NOTHING on the browsable shelf can be bought with Pi (the Pi rail sells only " +
                $"'{PiEnabledSkuHint}', which is storeVisible:false by WO-1069, or no Pi rail is registered " +
                "at all). Saying so on the shelf rather than falling back to the SKR prices (WO-1323).");

            var go = new GameObject("pi-shelf-empty", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(_shelfContent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 76f;
            MakeText(go.transform, StorePiText.ShelfEmpty.Resolve(), 30,
                ElarionUi.ParchmentDim, FontStyles.Italic, TextAlignmentOptions.Center,
                new Vector2(0.03f, 0.06f), new Vector2(0.97f, 0.94f));
        }

        /// <summary>
        /// Names the one Pi-enabled sku WITHOUT reaching into the Pi provider assembly
        /// (DeNelle.Wallet does not reference it, and must not grow a reference for a log line).
        ///
        /// <para>⚠ WO-1323 OWNER RULING — IT IS NOW ALSO THE SPOTLIGHT CANDIDATE, AND THAT IS STILL
        /// NOT A DECISION. This constant only says WHICH sku to ASK about; whether it may be sold is
        /// answered, every time, by <see cref="IPaymentProvider.CanBuy"/> plus
        /// <see cref="PurchaseGate.CanBuy(PackDef, out string)"/> in
        /// <see cref="ResolvePiSpotlightPack"/>. There is deliberately ONE copy of the string in this
        /// file: a second one (a "spotlight sku" beside a "hint sku") is the duplicated state that
        /// drifts the day the rail widens.</para>
        /// </summary>
        private const string PiEnabledSkuHint = "hearth-spark";

        // =====================================================================
        //  WO-1323 OWNER RULING (2026-09-02) — THE PI SPOTLIGHT
        // ---------------------------------------------------------------------
        //  THE PROBLEM IT SOLVES, in the owner's words: a Pi player currently sees
        //  every pack priced in USD with NO BUY CONTROL ANYWHERE, because the only
        //  Pi-quotable sku (hearth-spark) carries storeVisible:false from WO-1069.
        //
        //  ⛔ THE FIX IS THE SPOTLIGHT, NOT THE SHELF FLAG. Flipping storeVisible
        //     would reverse a PRICING ruling (hearth-spark is strictly dominated by
        //     starters-hand at the same $4.99) with a DISPLAY change — the shortcut
        //     BuildPiShelfNoticeIfNothingIsBuyable already refuses in writing. The
        //     pack is SPOTLIGHTED, not shelved: it never joins a band, never appears
        //     in PacksInBand, never enters BuildLedgerScale, and the browsable shelf
        //     is byte-for-byte the shelf it was.
        //
        //  ⛔ AND IT REUSES THE EXISTING LATCH (DeNelle.Commerce.StoreFocusRequest),
        //     which already honours ANY sku in the catalogue — ResolveFocusSku asks
        //     PackCatalog.Find, never PackCatalog.IsOnBrowsableShelf — so "spotlight
        //     something that is off the browsable shelf" is a capability the latch
        //     already had. No second focus mechanism was invented for this.
        //
        //  ⚠ KNOWN, ACCEPTED LIMIT: the latch is consumed once per store OPEN. If the
        //    player taps a shelf card the spotlight moves off the Pi pack and comes
        //    back on the next open. That is the latch's designed lifetime; a re-latch
        //    on every Render would fight the player for the spotlight every time a
        //    quote returned.
        // =====================================================================

        /// <summary>
        /// The one pack the Pi spotlight may open on, or <c>null</c>. Asked by BOTH the open-time
        /// latch and the empty-shelf notice, so the two can never disagree about whether a Pi player
        /// has something to buy.
        ///
        /// <para>⛔ IT ASKS THE REAL GATES, NOT A FLAG. <see cref="PiCanSell"/> is the rail's own
        /// answer (a registered Pi provider that will sell THIS sku right now) and
        /// <see cref="PurchaseGate.CanBuy(PackDef, out string)"/> is the build-wide + wallet-ceiling
        /// answer — the SAME two questions <see cref="BuildSpotlightCta"/> asks before it builds a Buy
        /// control. If either refuses there is no purchase to reach, so there is nothing to spotlight
        /// and nothing to silence the honest empty-shelf notice with.</para>
        ///
        /// <para>⛔ AND IT RETURNS NULL FOR ANYTHING ALREADY ON THE SHELF. The spotlight exists to
        /// reach a pack the shelf cannot show; a Pi-sellable sku that IS browsable needs no rescue and
        /// the ordinary buyable-count above already covers it. This is why widening the Pi rail later
        /// requires no edit here.</para>
        /// </summary>
        private static PackDef ResolvePiSpotlightPack()
        {
            if (!PiDisplay) return null;

            var pack = PackCatalog.Find(PiEnabledSkuHint);
            if (pack == null) return null;

            // Already reachable through a band - the shelf, not the spotlight, is its home.
            if (PackCatalog.IsOnBrowsableShelf(pack)) return null;

            if (!PiCanSell(pack)) return null;
            if (!PurchaseGate.CanBuy(pack, out _)) return null;

            return pack;
        }

        /// <summary>
        /// WO-1323 owner ruling — on store OPEN, point <see cref="StoreFocusRequest"/> at the one
        /// Pi-priced pack so a Pi player can actually reach a purchase.
        ///
        /// <para>⛔ IT NEVER OVERWRITES A CALLER'S REQUEST. A latch already carrying a sku (the
        /// Manage "Buy builder" route, WO-1253) or a pending shortfall remedy is a deliberate act by
        /// someone who knows what the player just asked for; this is a DEFAULT, and a default that
        /// beats an explicit request is a bug. Both cases return, loudly.</para>
        ///
        /// <para>No-op on every non-Pi surface: <see cref="ResolvePiSpotlightPack"/> returns null the
        /// moment <see cref="PiDisplay"/> is false, which is why the SKR skin's open is unchanged.</para>
        /// </summary>
        private void LatchPiSpotlightOnOpen()
        {
            var pack = ResolvePiSpotlightPack();
            if (pack == null) return;

            if (StoreFocusRequest.HasPending)
            {
                FlowTrace.Step("Store",
                    "Pi spotlight: a caller already latched a focus SKU for this open - theirs wins, " +
                    "the Pi default stands down (WO-1323).");
                return;
            }

            if (!string.IsNullOrEmpty(_pendingShortfallLabel) && _pendingShortfallMissing > 0)
            {
                FlowTrace.Step("Store",
                    $"Pi spotlight: a shortfall offer owns this open ({_pendingShortfallMissing} " +
                    $"{_pendingShortfallLabel} short) - the remedy wins, the Pi default stands down.");
                return;
            }

            StoreFocusRequest.RequestFocusSku(pack.Sku);
            FlowTrace.Step("Store",
                $"Pi spotlight: latched '{pack.Sku}' - the ONE Pi-priced pack, reached through the " +
                "existing focus latch. storeVisible stays FALSE (WO-1069 pricing ruling is untouched); " +
                "the pack is spotlighted, not shelved.");
        }

        /// <summary>
        /// The browsable rows of one band, in catalogue order.
        /// </summary>
        private List<PackDef> PacksInBand(StoreBand band)
        {
            var list = new List<PackDef>();
            foreach (var pack in PackCatalog.Packs)
            {
                // Shelf membership lives on PackCatalog.IsOnBrowsableShelf (storeVisible + the
                // WO-947 impulse/shelfCurated split). Do not re-derive it here — WO-1246's
                // grant-path oracle asks the same helper, so a SKU cannot be visible here and
                // invisible to the oracle (or the reverse).
                if (!PackCatalog.IsOnBrowsableShelf(pack)) continue;

                if (PackCatalog.BandOf(pack) != band) continue;
                list.Add(pack);
            }
            return list;
        }

        /// <summary>
        /// The band head: a 3 px MARK, a mono EYEBROW and a right-aligned sub-label.
        /// <para>⛔ THE MARK AND THE EYEBROW ARE THE BAND'S IDENTITY, not the colour. The owner is
        /// red/green colourblind, so the hue is the FIRST thing allowed to be lost: strip every
        /// colour and the eyebrow still names the band and the mark still separates it. The four
        /// lights additionally step apart in rec.709 value (NightMarketPalette.MinGreyscaleStep), and
        /// that step is asserted by a regression rather than trusted from a comment.</para>
        /// </summary>
        private void BuildBandHead(StoreBand band)
        {
            var go = new GameObject("band-" + band, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(_shelfContent, false);
            // 76, not 46: every store string now clears ElarionUi.FontFloorMobile(30) (UI-001 §0.6),
            // and a 46-unit band cannot hold a 30-unit eyebrow plus its sub-label without one of them
            // spilling onto the card row beneath it.
            go.GetComponent<LayoutElement>().preferredHeight = 76f;
            var host = go.transform;

            var light = NightMarketPalette.For(band);

            // The 3 px mark.
            var mark = Plate(host, light);
            if (mark != null)
            {
                var rt = mark.rectTransform;
                rt.anchorMin = new Vector2(0.005f, 0.12f);
                rt.anchorMax = new Vector2(0.013f, 0.86f);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }

            MakeText(host, BandEyebrowForLayout(band), 34, ElarionUi.Parchment, FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0.03f, 0.06f), new Vector2(0.52f, 0.94f));
            MakeText(host, BandSubLabel(band), 30, ElarionUi.ParchmentDim, FontStyles.Italic,
                TextAlignmentOptions.Right, new Vector2(0.54f, 0.06f), new Vector2(0.99f, 0.94f));

            // G4 — the patronage sheen. A slow iridescent roll along the band head's top edge.
            // ⚠ ON THE BAND HEAD, NOT ON EACH CARD (Grok drew it per-row). One strip is one extra
            // Graphic instead of one per patronage card, which keeps the draw-call budget honest,
            // and it still does the only job it has: it is the ONLY motion anywhere on the shelf,
            // which is what marks the tier.
            if (band == StoreBand.Patronage && _aurora != null)
            {
                _aurora.AddDrift(host, "sheen", new Vector2(0.005f, 0.88f), new Vector2(0.99f, 0.97f),
                    new Color(light.r, light.g, light.b, 0.55f), 14f, new Vector2(1f, 0f),
                    followsBandLight: false, strip: true);
            }
        }

        private static string BandEyebrow(StoreBand band)
        {
            switch (band)
            {
                case StoreBand.Free:      return StoreStrings.Get(StoreStrings.KeyBandFree);
                case StoreBand.Gap:       return StorePresentationText.BandGap.Resolve();
                case StoreBand.Patronage: return StoreStrings.Get(StoreStrings.KeyBandPatronage);
                default:                  return StorePresentationText.BandBasket.Resolve();
            }
        }

        private static string BandSubLabel(StoreBand band)
        {
            switch (band)
            {
                case StoreBand.Free:      return StoreStrings.Get(StoreStrings.KeyBandFreeSub);
                case StoreBand.Gap:       return StorePresentationText.BandGapSub.Resolve();
                case StoreBand.Patronage: return StoreStrings.Get(StoreStrings.KeyBandPatronageSub);
                default:                  return StoreStrings.Get(StoreStrings.KeyBandBasketSub);
            }
        }

        /// <summary>
        /// Lane D — the free band.
        ///
        /// <para>⛔ IT CARRIES THE PROMO DOOR AND NOTHING ELSE, AND THAT IS AN ASSEMBLY FACT, NOT A
        /// SHORTCUT. Grok's draft put the daily chest and the rewarded lantern here.
        /// <c>DailyChestController</c> lives in <b>DeNelle.Village</b>, and the dependency runs
        /// Village → Wallet, one way: this file cannot see the chest's claim state, cannot ask
        /// whether it is claimable and cannot claim it. Drawing a chest card anyway would be a
        /// control that reports nothing and does nothing — the exact dishonesty this pass is
        /// removing everywhere else. The remaining routes were both refused on purpose: reflection
        /// is banned (CLAUDE.md §10) and widening the asmdef to reach one MonoBehaviour is the
        /// dependency the port spec forbids. Surfacing the chest needs a Core-level status seam
        /// (an interface + a CoreServices registration, the IVillageHud shape) — a real, small piece
        /// of work, and NOT one to smuggle into a presentation pass.</para>
        ///
        /// <para>⛔ THE PROMO DOOR STAYS OUTSIDE THE PURCHASE-FLAG TEST. Redeeming spends no money,
        /// touches no wallet rail and must work TODAY with purchases disabled. It is built here, in
        /// a band that is rendered unconditionally, so there is NO branch it can fall out of — the
        /// same structural guarantee it had when it sat outside the card loop.</para>
        /// </summary>
        private void BuildFreeBand(string entryLabel)
        {
            if (_shelfContent == null) return;
            BuildBandHead(StoreBand.Free);
            var tabs = BuildCardRow(FreeTabRowPx);
            BuildUtilityTab(tabs, entryLabel, OpenRedeemPanel, true);
            BuildUtilityTab(tabs, "MONTHLY LEDGER", () => PanelRouter.Open(PanelId.MonthlyLedger), false);
        }

        private string BandEyebrowForLayout(StoreBand band)
        {
            if (_utilityContent != null)
            {
                if (band == StoreBand.Basket) return "PACKS";
                if (band == StoreBand.Patronage) return "MOVING";
            }
            return BandEyebrow(band);
        }

        /// <summary>
        /// The landscape reference's right rail. These are navigation doors, not merchandise, so
        /// they stay visible even when purchasing is disabled or the catalogue fails to load.
        /// </summary>
        private void BuildLandscapeActions(string entryLabel)
        {
            if (_utilityContent == null) return;
            // WO-1409: the two doors already name themselves. Removing their redundant heading
            // returns 70 px to this masked column, exactly the measured deficit that clipped
            // MONTHLY LEDGER into the CLOSE THE GAP heading below it.
            BuildUtilityRow(_utilityContent, entryLabel, "gift-icon", OpenRedeemPanel, true);
            BuildUtilityRow(_utilityContent, "MONTHLY LEDGER", "calendar-icon",
                () => PanelRouter.Open(PanelId.MonthlyLedger), false);
        }

        /// <summary>
        /// Clears the landscape catch-up rail and rebuilds it from the CURRENT catalogue and the
        /// CURRENT quoted prices. Called from <see cref="Render"/> — see the note there.
        /// </summary>
        /// <remarks>
        /// ⛔ THIS IS THE GAP BAND'S REPAINT, and without it the band has none. Nothing persistent
        /// lives in this column (its heading is rebuilt with the rows), so it is cleared whole —
        /// unlike the shelf, whose first children are the Free band, and unlike the ACTIONS rail,
        /// which is a different transform and is never touched here.
        /// </remarks>
        private void RebuildLandscapeGapOffers()
        {
            if (_gapUtilityContent == null) return;      // portrait: the Gap band is on the shelf
            for (int i = _gapUtilityContent.childCount - 1; i >= 0; i--)
                Destroy(_gapUtilityContent.GetChild(i).gameObject);
            BuildLandscapeGapOffers();
        }

        private void BuildLandscapeGapOffers()
        {
            if (_gapUtilityContent == null) return;
            var rows = PacksInBand(StoreBand.Gap);
            if (rows.Count == 0)
            {
                // ⛔ WORDS, NEVER AN EMPTY RAIL. A silently missing band is invisible to every gate
                // and was found only by the owner's eyes, twice (WO-1335, WO-1339). If the
                // catalogue has no gap rows on this pass, the rail still names itself and says so
                // in a sentence — no colour carries this, the owner is red/green colourblind.
                FlowTrace.Warn("Store", "catch-up rail: PacksInBand(Gap) returned 0 rows — the " +
                                        "catalogue is unreadable or no impulse row is storeVisible+shelfCurated. " +
                                        "Drawing the worded empty state rather than an empty rail.");
                BuildUtilityHeading(_gapUtilityContent, "CLOSE THE GAP", NightMarketPalette.For(StoreBand.Gap));
                BuildGapUtilityRow(_gapUtilityContent, "Catch-up offers", "Unavailable right now", null);
                return;
            }

            BuildUtilityHeading(_gapUtilityContent, "CLOSE THE GAP", NightMarketPalette.For(StoreBand.Gap));
            foreach (var pack in rows)
            {
                if (pack == null) continue;
                var captured = pack;
                BuildGapUtilityRow(_gapUtilityContent, pack.Name, StorePriceMajor(pack),
                    () => FocusPack(captured.Sku, BuildLedgerScale(), animate: true));
            }
        }

        private static void BuildUtilityHeading(Transform parent, string label, Color light)
        {
            var go = new GameObject("utility-heading-" + label, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            // WO-1334: ONE source for this height — the ACTIONS/CLOSE-THE-GAP vertical split is
            // arithmetic over it, so a heading that grows here grows the rail that holds it.
            go.GetComponent<LayoutElement>().preferredHeight = NightMarketLayout.UtilityHeadingPx;
            var backing = Plate(go.transform, Color.white);
            var image = backing != null ? backing.GetComponent<Image>() : null;
            var frame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (image != null && frame != null) { image.sprite = frame; image.type = Image.Type.Simple; }
            MakeText(go.transform, label, 29, light, FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0.06f, 0f), new Vector2(0.96f, 1f));
        }

        private static void BuildUtilityRow(Transform parent, string label, string iconResource,
                                            Action action, bool accent)
        {
            var slot = new GameObject("utility-row-" + label, typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(parent, false);
            var le = slot.GetComponent<LayoutElement>();
            le.preferredHeight = ElarionUiKit.MinTouchPx + 8f;
            le.minHeight = ElarionUiKit.MinTouchPx;
            var button = ElarionUiKit.BuildObsidianButton(slot.transform, label,
                ElarionUiKit.ObsidianButtonStyle.Style1,
                accent ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.01f, 0.03f), new Vector2(0.99f, 0.97f), action);
            MedievalUiSkin.ApplyButton(button, primary: accent);
            var face = button != null ? button.targetGraphic as Image : null;
            if (face != null) face.type = Image.Type.Simple;
            // ⛔ WO-1636 — THE ICON GUTTER AND THE TEXT INSET ARE PX, AND THEY COME FROM ONE PAIR OF
            // CONSTANTS. The label used to be x .20-.92 of the button — a 72% share of a rect whose
            // width is whatever the commerce rail resolved to — and at 1280x720 that is 302 px, in
            // which "MONTHLY LEDGER" drew 12 of its 13 printable glyphs
            // (Builds/wave3-capture2.glyph-findings.txt:57). One glyph plus an ellipsis, so the door
            // to the ledger read "MONTHLY LEDGE...". The font is already at 28 and may not come down
            // (WO-1636 §2), and the rail's own minimum is derived from the canon Buy control, not
            // from this caption — so the px that were available were the .08 of button width sitting
            // dead between the icon and the text and the .08 at the right edge.
            //
            //     button 419 px at 1280x720  ->  419 - IconGutter(74) - TextRightPadPx(8) = 337 px
            //     needed = 302 x 14/13 ~= 326 px  (the drawn 12 + the missing glyph + the ellipsis)
            //
            // ⚠ THAT IS A ~3% MARGIN AND IT IS STATED, NOT ROUNDED AWAY. The next capture is what
            // proves it; if 337 is still short, the honest next lever is the commerce rail's
            // MINIMUM (NightMarketComposition.CommerceMinPx derives from the CTA alone and has never
            // accounted for this caption), never this label's floor.
            if (button != null)
            {
                // The icon gets a px SLOT rather than a px AddArt overload — AddArt fills its parent,
                // so one empty rect authored in px puts the art on the same measuring system as the
                // caption beside it without a second signature to keep in step.
                var iconSlot = Region(button.transform, "icon-slot",
                    new Vector2(0f, 0.12f), new Vector2(0f, 0.88f),
                    new Vector2(NightMarketLayout.UtilityRowIconLeftPx, 0f),
                    new Vector2(NightMarketLayout.UtilityRowIconLeftPx +
                                NightMarketLayout.UtilityRowIconWidthPx, 0f));
                AddArt(iconSlot, iconResource, Vector2.zero, Vector2.one);
            }
            var text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
            {
                var textRect = text.rectTransform;
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.pivot = new Vector2(0.5f, 0.5f);
                textRect.offsetMin = new Vector2(NightMarketLayout.UtilityRowTextLeftPx, 0f);
                textRect.offsetMax = new Vector2(-NightMarketLayout.UtilityRowTextRightPadPx, 0f);
                text.alignment = TextAlignmentOptions.Left;
                ElarionUiKit.FitSingleLine(text, ElarionUi.FontFloorMobile, 28f);
            }
        }

        /// <summary>Compact two-field offer row used by the landscape catch-up rail.</summary>
        private static void BuildGapUtilityRow(Transform parent, string name, string price, Action action)
        {
            var slot = new GameObject("gap-offer-" + name, typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(parent, false);
            var layout = slot.GetComponent<LayoutElement>();
            // The button sits at y .03..97 inside this slot, so the slot must be
            // 120 px for the visible/raycast face itself to remain >=112 px.
            layout.preferredHeight = ElarionUiKit.MinTouchPx + 8f;
            layout.minHeight = ElarionUiKit.MinTouchPx;

            var button = ElarionUiKit.BuildObsidianButton(slot.transform, string.Empty,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.01f, 0.03f), new Vector2(0.99f, 0.97f), action);
            MedievalUiSkin.ApplyButton(button, primary: false);
            var face = button != null ? button.targetGraphic as Image : null;
            if (face != null) face.type = Image.Type.Simple;

            var nameLabel = MakeText(button.transform, name, 27, ElarionUi.Parchment,
                FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0.07f, 0.08f), new Vector2(0.53f, 0.92f));
            string priceDisplay = string.Equals(price, "Price unavailable", StringComparison.OrdinalIgnoreCase)
                ? "UNAVAILABLE" : price;
            var priceLabel = MakeText(button.transform, priceDisplay, 22, ElarionUi.Gold,
                FontStyles.Bold, TextAlignmentOptions.Right,
                new Vector2(0.55f, 0.08f), new Vector2(0.94f, 0.92f));
            FitInto(nameLabel, 27f);
            if (priceLabel != null)
            {
                priceLabel.textWrappingMode = TextWrappingModes.NoWrap;
                ElarionUiKit.FitSingleLine(priceLabel, 16f, 22f);
            }
        }

        /// <summary>
        /// One FREE-band utility tab: a full-slot button and nothing else.
        ///
        /// <para>⛔ THE INSET IS 0.02, NOT 0.08, AND THAT IS THE WHOLE RULE HERE. At 0.08-0.92 of a
        /// <see cref="FreeTabRowPx"/> slot minus the row's 6 px of padding, the button derived to
        /// ~106 px tall — UNDER <see cref="ElarionUiKit.MinTouchPx"/>. A sub-floor control does not
        /// fail the clamp; ClampMinTouch GROWS IT ABOUT ITS CENTRE, into the tab beside it. That is
        /// P0-4 ("two giant OPEN slabs drawn over their own cards") re-entered by a different route,
        /// and it is why every control on this screen is authored over the floor rather than rescued
        /// by the clamp. The tab carries no copy of its own, so it can safely take the whole slot.</para>
        /// </summary>
        private void BuildUtilityTab(Transform strip, string label, Action action, bool accent)
        {
            var slot = new GameObject("utility-tab-" + label, typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(strip, false);
            var le = slot.GetComponent<LayoutElement>();
            le.preferredHeight = FreeTabRowPx;
            le.minHeight = FreeTabRowPx;
            le.flexibleWidth = 1f;
            le.minWidth = FreeTabMinWidthPx;
            Plate(slot.transform, NightMarketPalette.GroundRaised);
            var button = ElarionUiKit.BuildObsidianButton(slot.transform, label,
                ElarionUiKit.ObsidianButtonStyle.Style1,
                accent ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.98f), action);
            MedievalUiSkin.ApplyButton(button, primary: accent);
            var text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null) ElarionUiKit.FitSingleLine(text, ElarionUi.FontFloorMobile, 28f);
        }

        private void BuildFreeDoor(Transform strip, string title, string blurb, PanelId panel)
        {
            var slot = new GameObject("free-" + panel, typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(strip, false);
            var le = slot.GetComponent<LayoutElement>();
            le.preferredHeight = FreeRowHeightPx;
            le.minHeight = FreeRowHeightPx;
            le.flexibleWidth = 1f;
            le.minWidth = StorePackCard.MinCardWidthPx;
            Plate(slot.transform, NightMarketPalette.GroundRaised);
            Orb(slot.transform, NightMarketPalette.For(StoreBand.Free));
            FitInto(MakeText(slot.transform, title, 34, ElarionUi.Parchment, FontStyles.Bold,
                TextAlignmentOptions.TopLeft, new Vector2(0.26f, 0.74f), new Vector2(0.96f, 0.98f)), 34);
            FitInto(MakeText(slot.transform, blurb, 30, ElarionUi.ParchmentDim, FontStyles.Italic,
                TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.56f), new Vector2(0.96f, 0.73f)), 30);
            // Same authored band as the redeem door: >= 112 on both axes, and it stops below the
            // blurb so no button is ever drawn over its own card's copy.
            var open = ElarionUiKit.BuildObsidianButton(slot.transform, "OPEN",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.06f, 0.02f), new Vector2(0.60f, 0.53f), () => PanelRouter.Open(panel));
            MedievalUiSkin.ApplyButton(open, primary: false);
            var openLabel = open != null ? open.GetComponentInChildren<TMP_Text>(true) : null;
            if (openLabel != null) ElarionUiKit.FitSingleLine(openLabel, ElarionUi.FontFloorMobile, 34f);
        }

        /// <summary>
        /// One horizontal strip of <see cref="CardsPerRow"/> flex cards, AUTHORED AT
        /// <paramref name="heightPx"/> reference px.
        /// <para>⛔ THE HEIGHT IS A PARAMETER BECAUSE THE ROW AND THE CARD MUST BE ONE NUMBER. The
        /// row used to hold its own constant while the card held another, and the scroll column did
        /// not control child height at all — so every row resolved to a bare RectTransform's default
        /// 100 units, the cards inside were force-expanded down onto it, and ClampMinTouch then grew
        /// each one back to 112 SYMMETRICALLY ABOUT ITS CENTRE, i.e. straight over the row above and
        /// below. That is the mechanism behind P0-3's card-on-card overlap and, through it, the two
        /// WRONG PRICES on the owner's device.</para>
        /// </summary>
        private Transform BuildCardRow(float heightPx)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(_shelfContent, false);
            var rowLe = go.GetComponent<LayoutElement>();
            rowLe.preferredHeight = heightPx;
            rowLe.minHeight = heightPx;
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 9f;
            h.padding = new RectOffset(4, 4, 3, 3);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            return go.transform;
        }

        private static void Spacer(Transform strip)
        {
            var go = new GameObject("spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(strip, false);
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        /// <summary>
        /// One shelf card, built by <see cref="StorePackCard"/> — the ONE template (UI-001 §2 / §R3,
        /// owner ruling 4).
        ///
        /// <para>⛔ THIS METHOD NO LONGER DRAWS A CARD. It RESOLVES one: every string, state word,
        /// badge and tint is looked up here and handed to the template as data. The inline card this
        /// used to be — its own plate, orb, rail, Outline, four MakeText calls and its own touch
        /// clamp — was a second card implementation living beside the shared kit, which is exactly the
        /// parallel-design-system failure §2 exists to forbid. It is also why the art wells shipped
        /// near-black (P1-7) and why the price could be occluded (P0-1/P0-2): there was nowhere
        /// central to fix either.</para>
        ///
        /// <para>⛔ AND IT ASKS NO COMMERCE QUESTION. <see cref="PurchaseGate"/> is the sole client
        /// authority; the card renders a state WORD the caller resolved and carries no Buy control at
        /// all — the one Buy control on this screen is the commerce column's CTA, which consults the
        /// same gate the charge path does.</para>
        /// </summary>
        private GameObject BuildPackCard(Transform strip, PackDef pack, StoreBand band,
                                         StorePackCardVariant variant)
        {
            if (pack == null)
            {
                FlowTrace.Fail("Store", "BuildPackCard: pack is null — cannot build a card.");
                return null;
            }

            // WO2: analytics — player saw this bundle. Guarded: an analytics throw must never blank
            // the whole card.
            FlowTrace.Try("Store", $"track bundle_viewed '{pack.Sku}'", () =>
            {
                DeNelle.Core.Analytics.EventTracker.Track("bundle_viewed", new
                {
                    bundleId   = pack.Sku,
                    bundleName = pack.Name,
                    founderOnly = pack.FounderOnly,
                });
            });

            string sku = pack.Sku;
            var model = new StorePackCardModel
            {
                Sku          = sku,
                Name         = pack.Name,
                // ONE goods line, from the SAME describer the spotlight ledger draws from, so the
                // card and the spotlight can never disagree about what a pack contains.
                Contents     = DescribeContents(pack),
                ValueCaption = ValueCaption(pack),
                // ⛔ TWO NUMBERS, AND THE "APPROX" GOES ON THE DOLLARS (WO-1158 §5, owner ruling
                // 2026-08-23). The player pays SKR and that amount is EXACT - the server's quote
                // pins it to the base unit. The DOLLARS float, because the rate moves. A card
                // showing only "396 SKR" tells a player nothing about what they are spending, and a
                // store that obscures real-money cost reads as a store with something to hide.
                PriceMajor   = StorePriceMajor(pack),
                PriceMinor   = StorePriceMinor(pack),
                Badge        = OneWordBadge(pack.StoreBadge),
                StateWord    = CardStateWord(pack),
                // ⛔ RESOLVED HERE, RENDERED THERE. The card is handed a finished string; it never
                // asks a commerce question of its own (UI-002). Empty on every buyable pack, so a
                // healthy shelf is byte-for-byte the card it was before this line existed.
                NotSellableReason = CardNotSellableReason(pack),
                Band         = band,
                OrbTint      = pack.OrbTint,
                GlyphConcepts = GlyphConceptsFor(pack),
                ArtResource  = NightMarketArt.ForSku(sku),
                Selected     = !string.IsNullOrEmpty(_focusSku) &&
                               string.Equals(sku, _focusSku, StringComparison.Ordinal),
            };

            // WO-1274: adapt the already-resolved strings into the shared neutral card contract.
            // This is deliberately downstream of every commerce resolver above: no second price,
            // entitlement, contents, or channel opinion is introduced here.
            _sharedOfferCards.Add(NightMarketCardAdapter.Adapt(model,
                () => FocusPack(sku, null, animate: true)));

            // Tapping the card moves the spotlight. It NEVER buys.
            var handle = StorePackCard.Build(strip, model, variant,
                () => FocusPack(sku, null, animate: true));
            if (handle == null || handle.Root == null)
            {
                FlowTrace.Fail("Store", $"BuildPackCard '{sku}': the card template returned no root - " +
                                        "this row is missing from the shelf entirely.");
                return null;
            }

            // WO-1409: keep the single-word badge off the art's focal/right edge. StorePackCard
            // returns the rendered label handle, so this remains a caller-owned presentation
            // adjustment rather than a second card implementation.
            if (handle.StateLabel != null && handle.StateLabel.transform.parent is RectTransform badgeRect)
            {
                badgeRect.anchorMin = new Vector2(OneWordBadgeX0, 1f);
                badgeRect.anchorMax = new Vector2(OneWordBadgeX1, 1f);
            }

            _cardHandles[sku] = handle;
            return handle.Root;
        }

        /// <summary>
        /// The word a card carries for its state — never a colour alone. Empty when the card has no
        /// special state (the store badge is then shown instead).
        /// </summary>
        private string CardStateWord(PackDef pack)
        {
            if (_vm != null && _vm.IsOwned(pack.Sku)) return StorePresentationText.CardOwned.Resolve();
            if (pack.AnchorOnly) return StoreStrings.Get(StoreStrings.KeyCardAnchor);
            // ⛔ SALES-NOT-OPEN IS A STATE, NOT A FAULT, so it takes the state pill exactly like
            // "Owned" and "Your gap" do. It ranks BELOW Owned (a pack you already have is not a pack
            // you were denied) and below AnchorOnly (which already draws no buy control), and ABOVE
            // the shortfall hint, because "you cannot buy this yet" outranks "this would close your
            // gap". The pill is a short WORD; the sentence goes on the line above the price.
            if (!string.IsNullOrEmpty(CardNotSellableReason(pack))) return CardNotSellableStateWord;
            if (!string.IsNullOrEmpty(_pendingShortfallLabel) && pack.Impulse &&
                string.Equals(pack.ImpulseResource, _pendingShortfallLabel, StringComparison.OrdinalIgnoreCase))
                return StorePresentationText.CardGap.Resolve();
            return string.Empty;
        }

        // =====================================================================
        //  ⭐ PRICED, BUT NOT PURCHASABLE — THE STATE THE SHELF COULD NOT SAY.
        // ---------------------------------------------------------------------
        //  WO-1190 made the price LIST public: browsing no longer asks the player
        //  to authorize anything, and the server returns the FULL sold ladder with
        //  an advisory `sellable` / `sellableReason` on each row. That closed the
        //  "Price unavailable on every pack" defect and opened a NEW one, which is
        //  what these two strings close: a row the server marks NOT sellable was
        //  drawing its real price beside a LIVE buy control. It was fail-closed —
        //  the binding quote refuses and nothing is charged — but the player was
        //  still invited to tap a button that could not work, and the only place
        //  the refusal appeared was after they had committed to buying.
        //
        //  ⚠ THESE TWO STRINGS ARE A PROPOSAL, NOT A RULING. The player-facing
        //  wording is the OWNER'S call. The server's current sentence
        //  ("Purchases are not open on this network yet. You can browse; buying
        //  unlocks when sales go live.") is another seat's placeholder, and it is
        //  ~95 characters — four wrapped lines in a shelf card's text lane, which
        //  is a card half again as tall or a clipped string, and neither is
        //  shippable. So the CARD carries the short state and the COMMERCE COLUMN
        //  — which has a whole host to spend — prints the server's own sentence
        //  verbatim, where the buy button would have been.
        //
        //  ⛔ AND IT READS AS A STATE, NOT AN ERROR. Sales not being open yet is a
        //  normal condition of a store that has not switched on, not a fault the
        //  player hit: no "cannot", no "failed", no "unavailable" (that word is
        //  spoken for — it means we have NO price, and this card has one).
        // =====================================================================

        /// <summary>The state pill's WORD for a priced-but-not-purchasable pack. PROPOSAL.</summary>
        private const string CardNotSellableStateWord = "Not yet";

        /// <summary>The card's one-line state sentence for the same. PROPOSAL.</summary>
        private const string CardNotSellableLine = "Not for sale yet - browsing only.";

        /// <summary>
        /// The card's not-sellable state line, or EMPTY when the pack is purchasable as far as the
        /// server has said.
        /// <para>⛔ DISPLAY GATING ONLY. <see cref="PurchaseGate"/> and the binding quote remain the
        /// only authorities that can stop a charge; this decides whether the shelf INVITES the tap.
        /// A SKU with no display price at all reads sellable here on purpose — that card already
        /// says "Price unavailable" in the price row, and stacking a second state sentence on top of
        /// it would say the same nothing twice.</para>
        /// </summary>
        private string CardNotSellableReason(PackDef pack)
        {
            if (pack == null || string.IsNullOrEmpty(pack.Sku)) return string.Empty;
            // Owned and anchor-only cards already carry their own state word and already build no
            // buy control — a second state line there would be noise, not information.
            if (_vm != null && _vm.IsOwned(pack.Sku)) return string.Empty;
            if (pack.AnchorOnly) return string.Empty;
            if (PurchaseQuoteService.IsSellable(pack.Sku)) return string.Empty;
            return CardNotSellableLine;
        }

        /// <summary>
        /// The goods-per-dollar caption, or EMPTY.
        /// <para>⛔ PURE ARITHMETIC OVER THE GRANTED BAG, on the same rule as the spotlight's compare
        /// line: it sums the very keys <c>ApplyPackContents</c> pays out, so the caption can never
        /// advertise a figure the grant seam does not deliver. No price, no goods, no caption — an
        /// invented value line is the claim-the-arithmetic-contradicts defect (§7).</para>
        /// </summary>
        private static string ValueCaption(PackDef pack)
        {
            if (pack == null || pack.Pricing == null || pack.Pricing.Usd <= 0d) return string.Empty;
            long goods = 0;
            foreach (string key in PackCatalog.LedgerEconomyKeys) goods += pack.EconomyAmount(key);
            if (goods <= 0) return string.Empty;
            double per = goods / pack.Pricing.Usd;
            return StoreStrings.Format(StoreStrings.KeyValuePerDollar, per.ToString("N0"));
        }

        /// <summary>
        /// Concept ids the card's art well tries, in order, for its glyph.
        /// <para>⛔ NEVER AN EMOJI — TMP's shipped font renders one as a tofu box, and the template
        /// falls back to two-letter ASCII initials when none of these resolve. The ids come from the
        /// pack's own authored data (its impulse resource), never from a SKU-name branch.</para>
        /// </summary>
        private static string[] GlyphConceptsFor(PackDef pack)
        {
            if (pack == null) return null;
            if (!string.IsNullOrEmpty(pack.ImpulseResource))
                return new[] { pack.ImpulseResource.Trim().ToLowerInvariant(), "chest", "coin" };
            return new[] { "chest", "coin", "crest" };
        }

        // =====================================================================
        //  Rendering — the spotlight
        // =====================================================================

        /// <summary>
        /// Which pack the spotlight opens on: the pack that closes a shortfall a caller handed us
        /// (via <see cref="FocusShortfall"/>), else <c>starters-hand</c>, else the first browsable row.
        /// </summary>
        private string ResolveFocusSku()
        {
            string requested = StoreFocusRequest.Consume();
            if (!string.IsNullOrEmpty(requested))
            {
                if (PackCatalog.Find(requested) != null)
                {
                    FlowTrace.Step("Store", "spotlight opens on requested SKU '" + requested + "'.");
                    return requested;
                }
                FlowTrace.Warn("Store", "RequestFocusSku '" + requested + "' is not in the catalogue — falling through.");
            }

            if (!string.IsNullOrEmpty(_pendingShortfallLabel) && _pendingShortfallMissing > 0)
            {
                // The ONE new caller of the resolver. It returns a PackDef and nothing else.
                var offer = ShortfallPackOffer.Resolve(_pendingShortfallLabel, _pendingShortfallMissing);
                if (offer.HasOffer && offer.Pack != null)
                {
                    FlowTrace.Step("Store", $"spotlight opens on the shortfall remedy '{offer.Pack.Sku}' " +
                                            $"({_pendingShortfallMissing} {_pendingShortfallLabel} short).");
                    return offer.Pack.Sku;
                }
                FlowTrace.Step("Store", $"no impulse pack resolves {_pendingShortfallMissing} {_pendingShortfallLabel} — " +
                                        "spotlight falls back to the starter pack.");
            }

            if (!string.IsNullOrEmpty(_focusSku)) return _focusSku;

            var starter = PackCatalog.Find("starters-hand");
            if (starter != null && starter.StoreVisible) return starter.Sku;

            foreach (StoreBand band in new[] { StoreBand.Gap, StoreBand.Basket, StoreBand.Patronage })
            {
                var rows = PacksInBand(band);
                if (rows.Count > 0) return rows[0].Sku;
            }
            return null;
        }

        /// <summary>
        /// Moves the spotlight. Repaints the two selection marks, rebuilds the spotlight column, and
        /// (G2) crossfades the aurora from the old band's light to the new one over 400 ms.
        /// </summary>
        private void FocusPack(string sku, Dictionary<string, int> scale, bool animate)
        {
            if (_spotlightHost == null) return;

            _sharedCardSession?.ShowOffer(sku);

            _focusSku = sku;
            var pack = string.IsNullOrEmpty(sku) ? null : PackCatalog.Find(sku);
            var band = pack != null ? PackCatalog.BandOf(pack) : StoreBand.Basket;
            var light = NightMarketPalette.For(band);

            // WO-1388 FUNNEL step 2 of 4 - pack_tapped. `animate` is true on exactly the three
            // player-tap callers (shelf card, shared offer card, gap-utility row) and false on the
            // render-time default focus, so it is the tap discriminator without a second parameter.
            if (animate) TrackPackTapped(pack);

            // ── REPAINT SELECTION — THREE CARRIERS, AND THE HUE IS THE WEAKEST ───
            // The owner is red/green colourblind, so selection may never be a colour swap. It is
            // carried by (1) the card MOVING TO THE SPOTLIGHT — unmissable by any eye, (2) the ring's
            // ALPHA stepping .5 -> 1.0, which is a luminance change and survives greyscale, and only
            // then (3) the bloom brightening. Strip every hue and the selection still reads.
            foreach (var kv in _cardHandles)
            {
                var handle = kv.Value;
                if (handle == null) continue;
                bool on = string.Equals(kv.Key, sku, StringComparison.Ordinal);
                StorePackCard.SetSelected(handle, on);
                if (handle.Ring != null)
                {
                    var c = handle.Ring.color;
                    c.a = on ? 1f : 0.5f;
                    handle.Ring.color = c;
                }
            }

            BuildSpotlight(pack, band, scale ?? BuildLedgerScale());

            if (_aurora != null)
            {
                if (animate) _aurora.CrossfadeTo(light);
                else _aurora.SetLightImmediate(light);
            }
        }

        private void BuildSpotlight(PackDef pack, StoreBand band, Dictionary<string, int> scale)
        {
            for (int i = _spotlightHost.childCount - 1; i >= 0; i--)
                Destroy(_spotlightHost.GetChild(i).gameObject);

            Plate(_spotlightHost, NightMarketPalette.GroundRaised);

            var light = NightMarketPalette.For(band);

            // G1 — the rolling-colour moment. TWO offset soft gradients on OPPOSED slow paths, so
            // their sum never repeats. Registered first so they sit behind everything else.
            if (_aurora != null)
            {
                _aurora.AddDrift(_spotlightHost, "aurora-a", new Vector2(0f, 0.52f), new Vector2(1f, 1f),
                    new Color(light.r, light.g, light.b, 0.30f), 22f, new Vector2(1f, 0.35f), followsBandLight: true);
                _aurora.AddDrift(_spotlightHost, "aurora-b", new Vector2(0f, 0.52f), new Vector2(1f, 1f),
                    new Color(light.r, light.g, light.b, 0.18f), 29f, new Vector2(-0.7f, -1f), followsBandLight: true);
            }

            if (pack == null)
            {
                MakeText(_spotlightHost, StorePresentationText.SpotlightEmpty.Resolve(), 30,
                    ElarionUi.ParchmentDim, FontStyles.Italic, TextAlignmentOptions.Center,
                    new Vector2(0.06f, 0.44f), new Vector2(0.94f, 0.60f));
                // The commerce column is cleared too: a CTA left standing beside an empty spotlight
                // would offer to buy a pack that is not on screen.
                BuildCommerce(null);
                FlowTrace.Warn("Store", "BuildSpotlight: no focused pack — the spotlight shows its empty line.");
                return;
            }

            var orbTint = NightMarketPalette.ParseTint(pack.OrbTint, light);
            string spotlightArt = string.Equals(pack.Sku, "starters-hand", StringComparison.OrdinalIgnoreCase)
                ? "featured-starters-hand"
                : NightMarketArt.ForSku(pack.Sku);
            if (!AddArt(_spotlightHost, spotlightArt, new Vector2(0.05f, 0.71f), new Vector2(0.95f, 0.98f)))
                Orb(_spotlightHost, orbTint, new Vector2(0.08f, 0.79f), new Vector2(0.30f, 0.95f));

            string spotlightBadge = OneWordBadge(pack.StoreBadge);
            if (!string.IsNullOrEmpty(spotlightBadge))
                MakeText(_spotlightHost, spotlightBadge, 30, ElarionUi.Gold, FontStyles.Bold,
                    TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.90f), new Vector2(0.34f, 0.97f));

            // ⛔ EVERY SIZE BELOW IS AT OR ABOVE ElarionUi.FontFloorMobile(30). UI-001 §0.6 measured
            // the old 12-17 pt store strings at ~10 PHYSICAL px on the device — defects #4 and #8 were
            // never a panel-width problem, they were a font-floor problem, and widening the panel
            // would have left them exactly as unreadable.
            FitInto(MakeText(_spotlightHost, pack.Name, 52, ElarionUi.Parchment, FontStyles.Bold,
                TextAlignmentOptions.BottomLeft, new Vector2(0.06f, 0.610f), new Vector2(0.94f, 0.705f)), 52);
            FitInto(MakeText(_spotlightHost, pack.Tagline, 32, ElarionUi.Parchment, FontStyles.Italic,
                TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.455f), new Vector2(0.94f, 0.605f)), 32);

            // ── The bar ledger ───────────────────────────────────────────────
            MakeText(_spotlightHost, StorePresentationText.LedgerHeading.Resolve(), 30,
                ElarionUi.ParchmentDim, FontStyles.Bold, TextAlignmentOptions.BottomLeft,
                new Vector2(0.06f, 0.405f), new Vector2(0.94f, 0.445f));
            // ⛔ WO-1636 — THE LADDER IS AUTHORED IN REFERENCE PX, AND THE OLD COMMENT HERE WAS
            // WRONG TWICE OVER. It read "0.058 of a 746-unit column is ~43 px", but the code beneath
            // it was `rowH = 0.055f` re-gapped by `+0.010f`, i.e. a .045 BAND, and the column the
            // first glyph-oracle run measured is 729 px, not 746. The band resolved to 32.8 px on
            // every captured landscape surface (Builds/wave3-capture2.glyph-findings.txt:45-49,
            // 51-55, 60-64) and TMP's Ellipsis overflow CULLED THE WHOLE LINE: "4,000", "2,000",
            // "400", "1,500" and "600" each drew ZERO of their glyphs. Fifteen of that run's sixty
            // findings are this one fraction, on the money screen, and no geometry rule could see it
            // because the rect was exactly where it belonged — only the words inside it were gone.
            //
            // A fraction cannot express a line box. The row band, the pitch and what the comparison
            // sentence needs are now px constants on NightMarketComposition (beside the WIDTH
            // derivation that was already there), and the ladder SPENDS a measured budget:
            //
            //     available = ledgerTopFrac * columnPx       .395 x 727.8 = 287.5 px
            //     ladder    = rows x LedgerRowPitchPx        5 x 39       = 195.0 px
            //     left over                                               =  92.5 px
            //     the sentence wants LedgerComparisonGapPx(8) + LedgerComparisonReservePx(78)
            //                                                             =  86.0 px
            //     slack                                                   =   6.5 px
            //
            // (727.8 is the run's own column, back-solved from its measured 32.75 px band over the
            // retired .045 fraction — not a rounded 729, because the row count is a FLOOR and a
            // rounded numerator is how a five-row ladder quietly becomes a four-row one.)
            //
            // ⚠ SO A SHORTER COLUMN CANNOT HOLD EVERYTHING, AND THAT IS SAID OUT LOUD RATHER THAN
            // DRAWN INVISIBLY. Rows are the truth and go first; the comparison sentence is the
            // qualifier and is the thing that gives — the same rule BuildCommerce already applies to
            // its balance-after line. A row that will not seat is DROPPED WITH A WARN, never
            // authored into a band that culls it, which is what the retired fraction did.
            const float ledgerTopFrac = 0.395f;
            // ⛔ THE COLUMN HEIGHT IS THE PLAN'S, NOT THE RECT'S — the same rule the CommerceCta seat
            // states at length. `_spotlightHost` hangs off `_bodyHost`, whose height comes from the
            // canvas, and a rect read during the build returns RAW SCREEN PIXELS: at 800x360 that
            // is a ~128 px column against the ~730 reference px the scaler will give it, which
            // would take `roomForRows` from five to ONE and silently drop four granted goods.
            // `_plan.SpotlightHeightPx` is resolved from SurfaceReferenceHeightPx in
            // NightMarketComposition.Resolve, so it is reference px by construction and needs no
            // cast, no null guard and no layout pass. (`_spotlightHost` is declared `Transform` at
            // :308 anyway, so `.rect` does not even compile on it.)
            float columnPx = Mathf.Max(1f, _plan.SpotlightHeightPx);
            float availablePx = ledgerTopFrac * columnPx;
            float pitchPx     = NightMarketComposition.LedgerRowPitchPx;
            float bandPx      = NightMarketComposition.LedgerRowBandPx;

            // ⛔ ROWS FIRST, AND THE CODE SAYS SO AS PLAINLY AS THE COMMENT DOES. The ladder takes
            // what it needs out of the band and the comparison sentence is offered whatever is left
            // — never the other way round. Reserving the qualifier's px BEFORE the rows is how a
            // budget that is 1 px short silently drops a granted good instead of a sentence, and the
            // oracle cannot tell a fixed label from one that was never built.
            string compare = BuildComparisonLine(pack);
            float comparePx = string.IsNullOrEmpty(compare)
                ? 0f
                : NightMarketComposition.LedgerComparisonGapPx + NightMarketComposition.LedgerComparisonReservePx;
            int roomForRows = Mathf.Max(0, Mathf.FloorToInt(availablePx / pitchPx));

            int drawn = 0, skipped = 0;
            foreach (string key in PackCatalog.LedgerEconomyKeys)
            {
                int amount = pack.EconomyAmount(key);
                if (amount <= 0) continue;
                if (drawn >= 6) break;
                if (drawn >= roomForRows) { skipped++; continue; }
                int max = scale != null && scale.TryGetValue(key, out int m) && m > 0 ? m : amount;
                BuildLedgerRow(_spotlightHost, key, amount, max, light,
                               ledgerTopFrac, drawn * pitchPx, bandPx);
                drawn++;
            }
            // ⚠ "nothing to draw" and "no room to draw it" are DIFFERENT findings and must not share
            // a line — the second one below says the column is short, and reporting it as an empty
            // grant seam would send the next reader to the catalogue instead of to the layout.
            if (drawn == 0 && skipped == 0)
                FlowTrace.Warn("Store", $"BuildSpotlight '{pack.Sku}': the grant seam pays out NOTHING this ledger can draw — " +
                                        "the card advertises no goods at all.");
            if (skipped > 0)
                FlowTrace.Warn("Store", $"BuildSpotlight '{pack.Sku}': the spotlight column is {columnPx:0}px, so its " +
                                        $"ledger band is {availablePx:0}px and seats {roomForRows} row(s) of " +
                                        $"{pitchPx:0}px — {skipped} granted good(s) are NOT drawn. This is a column " +
                                        "too short for its content, not a fit problem: shrinking the row would put " +
                                        "the figures back under the cull line WO-1636 lifted them out of.");

            float ladderPx = drawn * pitchPx;
            FlowTrace.Step("Store", $"WO-1636 ledger ladder: column {columnPx:0}px, band {availablePx:0}px, " +
                                    $"{drawn} row(s) x {pitchPx:0}px = {ladderPx:0}px, comparison reserve " +
                                    $"{comparePx:0}px — rows authored in px, not the retired .045 fraction.");

            // ── The comparison line ──────────────────────────────────────────
            // Its band is px now too, hanging from the bottom of the ladder, so the sentence keeps
            // the two line boxes it needs instead of whatever a fraction happened to leave it.
            float cursorPx = availablePx - ladderPx;
            if (!string.IsNullOrEmpty(compare) && comparePx > 0f && cursorPx >= comparePx)
            {
                float compareTopPx = ladderPx + NightMarketComposition.LedgerComparisonGapPx;
                var compareLabel = MakeText(_spotlightHost, compare, 30, ElarionUi.Parchment,
                    FontStyles.Normal, TextAlignmentOptions.TopLeft,
                    new Vector2(0.06f, ledgerTopFrac), new Vector2(0.94f, ledgerTopFrac));
                var crt = compareLabel.rectTransform;
                crt.pivot = new Vector2(0.5f, 1f);
                crt.offsetMin = new Vector2(0f, -(compareTopPx + NightMarketComposition.LedgerComparisonReservePx));
                crt.offsetMax = new Vector2(0f, -compareTopPx);
                FitInto(compareLabel, 30);
            }
            else if (!string.IsNullOrEmpty(compare))
            {
                FlowTrace.Warn("Store", $"BuildSpotlight '{pack.Sku}': the comparison sentence is DROPPED — the ledger " +
                                        $"band has {cursorPx:0}px left and the sentence needs {comparePx:0}px. The " +
                                        "qualifier gives, the granted figures do not.");
            }

            // The balance-after preview moved to the commerce column, beside the button it qualifies.

            BuildCommerce(pack);
        }

        /// <summary>
        /// One ledger row: the good, its NUMBER, and a bar on a scale shared with every other pack.
        /// <para>The number is the truth and the bar is the comparison. The bar keeps a small visible
        /// stub at the bottom of its range so a $1.99 pack next to a $49.99 pack still shows
        /// something — but the stub is never allowed to overstate: the printed figure beside it is
        /// exact, and it is the figure the grant seam pays.</para>
        /// </summary>
        /// <param name="ledgerTopFrac">The column fraction the ladder hangs from. Both y anchors
        /// collapse onto it so the px offsets below mean px on every surface.</param>
        /// <param name="rowTopPx">This row's top edge, in reference px BELOW that anchor line.</param>
        /// <param name="bandPx">The row's height in reference px
        /// (<see cref="NightMarketComposition.LedgerRowBandPx"/>).</param>
        private void BuildLedgerRow(Transform host, string key, int amount, int max, Color light,
                                    float ledgerTopFrac, float rowTopPx, float bandPx)
        {
            var go = new GameObject("ledger-" + key, typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            // WO-1636 — collapse the y anchors onto the ladder's edge, set the PIVOT BEFORE the
            // offsets, and express the band as px. This is the WO-1623/1628 shape (see
            // BuildCollectionBrowser.cs:41-113): a fraction of a column whose reference height moves
            // with the aspect cannot express "one line box", which is the only thing this row needs.
            rt.anchorMin = new Vector2(0.06f, ledgerTopFrac);
            rt.anchorMax = new Vector2(0.94f, ledgerTopFrac);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -(rowTopPx + bandPx));
            rt.offsetMax = new Vector2(0f, -rowTopPx);

            // Receipt-style rows match the approved landscape reference: recognizable
            // resource art + exact grant. A generic empty socket made five distinct goods
            // look identical, so use the supplied kit icons and retain the socket strictly
            // as a missing-art fallback.
            string icon = LedgerIconResource(key);
            if (string.IsNullOrEmpty(icon) ||
                !AddArt(go.transform, icon, new Vector2(0f, 0.08f), new Vector2(0.11f, 0.92f)))
                Orb(go.transform, light, new Vector2(0f, 0.16f), new Vector2(0.10f, 0.84f));
            MakeText(go.transform, key, 30, ElarionUi.Parchment, FontStyles.Normal,
                TextAlignmentOptions.Left, new Vector2(0.13f, 0f), new Vector2(0.66f, 1f));
            // ⛔ THE PRINTED NUMBER IS THE TRUTH AND IT MUST NEVER CLIP (§5). The bar is only the
            // comparison; if a figure ever had to shrink, it shrinks toward the floor — it is never
            // truncated, because a truncated quantity on a money screen is a wrong quantity.
            //
            // ⚠ WO-1636 — THE CEILING IS 30, NOT 32, AND THAT IS ARITHMETIC RATHER THAN TASTE. TMP
            // computes its line box at fontSizeMAX while auto-sizing, so a [30..32] band asks for a
            // 32 pt line box even on the surfaces where it renders at 30 — and the 2 pt of growth
            // headroom it buys costs 2.5 px of band the ladder above does not have. The FLOOR is
            // untouched (ElarionUi.FontFloorMobile); only the unused ceiling came down.
            var figure = MakeText(go.transform, amount.ToString("N0"),
                NightMarketComposition.LedgerFigureMaxPt, ElarionUi.Parchment, FontStyles.Bold,
                TextAlignmentOptions.Right, new Vector2(0.66f, 0f), new Vector2(1f, 1f));
            ElarionUiKit.FitSingleLine(figure, ElarionUi.FontFloorMobile,
                                       NightMarketComposition.LedgerFigureMaxPt);

            // The band was authored from a 1.33 line-factor ceiling, which is a stated assumption
            // about the font face and not a measurement of it. Measure it here, on the real label,
            // so a face that reads wider names ITSELF in the log instead of quietly re-culling the
            // figure the way the retired fraction did (§12: no silent failures).
            float needPx = figure.preferredHeight;
            if (needPx > bandPx + 0.5f)
                FlowTrace.Warn("Store", $"BuildLedgerRow '{key}': the figure \"{figure.text}\" wants {needPx:0.#}px of " +
                                        $"line box and its band is {bandPx:0.#}px — NightMarketComposition." +
                                        "LedgerRowLineFactorCeiling is too low for this font face and the figure " +
                                        "will cull. Raise the ceiling and re-spend the ladder budget; never shrink " +
                                        "the floor.");
        }

        private static string LedgerIconResource(string economyKey)
        {
            switch ((economyKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wood":     return "wood-icon";
                case "iron":     return "iron-icon";
                case "crystal":
                case "crystals": return "crystal-icon";
                case "stone":    return "stone-icon";
                case "coin":
                case "coins":    return "coin-icon";
                default:          return string.Empty;
            }
        }

        /// <summary>
        /// The per-good maximum across every browsable pack — the SHARED ledger scale, so a bigger
        /// pack is visibly bigger. Recomputed per render because the browsable set can change
        /// (ownership, a data edit) and a cached scale would silently mis-draw every bar.
        /// </summary>
        private Dictionary<string, int> BuildLedgerScale()
        {
            var scale = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string key in PackCatalog.LedgerEconomyKeys) scale[key] = 0;

            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !pack.StoreVisible) continue;
                if (pack.Impulse && !pack.ShelfCurated) continue;
                foreach (string key in PackCatalog.LedgerEconomyKeys)
                {
                    int a = pack.EconomyAmount(key);
                    if (a > scale[key]) scale[key] = a;
                }
            }
            return scale;
        }

        /// <summary>
        /// The comparison line — PURE ARITHMETIC over two real SKUs, or ABSENT.
        ///
        /// <para>⛔ NO ADJECTIVES AND NO INVENTED VALUE INDEX. Two ratios, both computable from the
        /// catalogue: how much more of one good this pack grants than its <c>compareTo</c> row, and
        /// how much more it costs. If <c>compareTo</c> is missing, names a SKU that does not exist,
        /// the two rows share no economy key, or either denominator is zero, this returns EMPTY and
        /// nothing is drawn. A comparison the code cannot compute is a comparison the store does not
        /// make.</para>
        /// </summary>
        private static string BuildComparisonLine(PackDef pack)
        {
            if (pack == null || string.IsNullOrEmpty(pack.CompareTo)) return string.Empty;
            var other = PackCatalog.Find(pack.CompareTo);
            // WO-1409: never compare against merchandise absent from this shelf. A player cannot
            // evaluate "2.7x" against a pack they cannot see, so that sentence is omitted.
            if (other == null || ReferenceEquals(other, pack) || !PackCatalog.IsOnBrowsableShelf(other))
                return string.Empty;

            // The shared key with the largest amount in THIS pack — the good the comparison is most
            // honestly about.
            string bestKey = null;
            int bestMine = 0, bestTheirs = 0;
            foreach (string key in PackCatalog.LedgerEconomyKeys)
            {
                int mine = pack.EconomyAmount(key);
                int theirs = other.EconomyAmount(key);
                if (mine <= 0 || theirs <= 0) continue;
                if (mine > bestMine) { bestKey = key; bestMine = mine; bestTheirs = theirs; }
            }
            if (bestKey == null || bestTheirs <= 0) return string.Empty;

            double usdMine = pack.Pricing != null ? pack.Pricing.Usd : 0d;
            double usdTheirs = other.Pricing != null ? other.Pricing.Usd : 0d;
            if (usdMine <= 0d || usdTheirs <= 0d) return string.Empty;

            double goodsRatio = bestMine / (double)bestTheirs;
            double priceRatio = usdMine / usdTheirs;

            return StoreStrings.Format(StoreStrings.KeyCompareLine,
                goodsRatio.ToString("0.#"), other.Name, bestKey, priceRatio.ToString("0.#"));
        }

        /// <summary>
        /// The commerce column: the ONE Buy control on this screen, plus the balance-after line that
        /// qualifies it. Same gate, same refusals, same words — what changed is WHERE it sits.
        ///
        /// <para>⛔ IT LIVES IN THE RIGHT THUMB ARC ON PURPOSE (UI-001 §8). At 2340x1080 the natural
        /// reach zones are x 0-720 and x 1620-2340 below y 480; the 486-unit commerce column lands
        /// inside the right one. A purchase control in the dead centre of a landscape phone is a
        /// two-handed control.</para>
        ///
        /// <para>⛔ AND THE GATE IS STILL THE ONLY AUTHORITY. <see cref="PurchaseGate.CanBuy"/> is
        /// consulted here and by <c>Purchase()</c>, so the button and the charge can never disagree;
        /// this method renders that decision and never makes one.</para>
        /// </summary>
        private void BuildCommerce(PackDef pack)
        {
            // The status surface lives ABOVE this host and is deliberately NOT cleared: it is the one
            // status surface (§3), and a purchase message must survive the focus change that a
            // player makes while reading it.
            if (_ctaHost == null)
            {
                FlowTrace.Warn("Store", "BuildCommerce: no commerce host — the store cannot draw a Buy control.");
                return;
            }
            for (int i = _ctaHost.childCount - 1; i >= 0; i--)
                Destroy(_ctaHost.GetChild(i).gameObject);
            if (pack == null) return;

            Transform host = _ctaHost;

            // ⛔ THE BUY CONTROL IS AUTHORED IN PIXELS AND THEN EXPRESSED AS A FRACTION OF THE HOST
            // IT LANDED IN — never the other way round (WO-1162 FIX 1). It used to be the literal
            // pair 0.030-0.335 of a fixed 440-unit host, which is 134 px only while the host is 440.
            // The stacked two-column composition gives the rail a ~158-unit CTA host, where the same
            // fractions derive 48 px: under MinTouchPx(112), and a sub-floor control does not fail
            // the clamp — ClampMinTouch GROWS it about its centre, over whatever is beside it.
            // Deriving the fraction from the REAL host height keeps the button the canon size in
            // every composition, so the clamp stays the no-op it is meant to be.
            float ctaHostPx = Mathf.Max(1f, _ctaHost.rect.height > 1f ? _ctaHost.rect.height : _plan.CtaHostPx);
            // ⚠ WO-1636 — THE GUTTER IS A FRACTION OF THE SEAT IT IS APPLIED TO, NOT OF THE RAIL.
            // It read `CommerceGutterPx / _plan.CommerceWidthPx`, which is correct only in the
            // stacked composition where the seat IS the rail. In landscape the seat lives in the
            // bottom band, so a 24 px gutter was being expressed as a share of a different rect and
            // the button came out narrower than canon.
            // ⛔ AND THE WIDTH IS THE ONE THE SEAT AUTHORED (`_ctaHostWidthPx`), NEVER `.rect.width`.
            // A rect read during the build returns raw screen pixels — at 800x360 that is ~764
            // against a ~2110 px reference, which is how WO-1636's first attempt clamped this
            // gutter to .20 and shipped a 26.9 px Buy control.
            float ctaHostWidthPx = Mathf.Max(1f, _ctaHostWidthPx > 1f
                ? _ctaHostWidthPx
                : NightMarketComposition.CommerceMinPx);
            float gutterFrac = Mathf.Clamp(NightMarketComposition.CommerceGutterPx /
                                           ctaHostWidthPx, 0.02f, 0.20f);
            float ctaY0 = NightMarketComposition.CtaBottomPadPx / ctaHostPx;
            float ctaY1 = (NightMarketComposition.CtaBottomPadPx + NightMarketComposition.CtaButtonPx) / ctaHostPx;
            var ctaMin = new Vector2(gutterFrac, ctaY0);
            var ctaMax = new Vector2(1f - gutterFrac, Mathf.Min(1f, ctaY1));

            // Everything ABOVE the button inside this host is OPTIONAL and is drawn only if it fits.
            // One TMP line box each; when the host is the stacked minimum there is room for neither,
            // and dropping a qualifier is correct where squeezing one on top of the Buy control is not.
            float lineBox   = NightMarketComposition.LineBoxPx;
            float aboveCta  = ctaHostPx - (NightMarketComposition.CtaBottomPadPx + NightMarketComposition.CtaButtonPx);
            bool roomForNetwork = aboveCta >= lineBox + 6f;
            bool roomForBalance = aboveCta >= 2f * lineBox + 12f;
            float netY0 = ctaY1 + (6f / ctaHostPx);
            float netY1 = netY0 + (lineBox / ctaHostPx);
            float balY0 = netY1 + (6f / ctaHostPx);
            float balY1 = balY0 + (lineBox / ctaHostPx);

            // ── Balance-after preview, directly above the CTA ──────────────
            // Only when the wallet mirror actually KNOWS a number. Never computed from an assumed
            // balance: "what you will have left" is a promise, and a promise off a guessed figure is
            // the same lie as a fabricated balance.
            // ⚠ AND ONLY WHEN A PRICE EXISTS. With no server quote AmountFor returns 0 (WO-1158),
            // and "what you will have left" would print the player's whole balance back at them as
            // if the pack were free.
            // ⚠ AND ONLY WHEN THE HOST HAS A LINE BOX TO SPARE ABOVE THE BUTTON (WO-1162 FIX 2's
            // rule, applied to the commerce rail): required is the Buy control and its price; the
            // balance-after preview is the qualifier and it is the thing that gives.
            // ⚠ AND NEVER UNDER THE PI SKIN (WO-1323): its copy is "Wallet after: {0} SKR", which is
            // a promise about a rail the Pi player does not pay on. The state test alone would
            // already exclude it (the mirror never reaches Known under Pi), but this screen takes
            // money — the string is excluded by NAME, not by a state that happens to be right today.
            if (!PiDisplay && roomForBalance && _balanceState == BalanceState.Known &&
                pack.AmountFor(CurrencyKind.Skr) > 0d)
            {
                double after = _balanceSkr - pack.AmountFor(CurrencyKind.Skr);
                MakeText(host, StoreStrings.Format(StoreStrings.KeyBalanceAfter, after.ToString("N0")),
                    30, ElarionUi.Parchment, FontStyles.Normal, TextAlignmentOptions.Left,
                    new Vector2(ctaMin.x, balY0), new Vector2(ctaMax.x, balY1));
            }

            // ── anchorOnly: NO BUY CONTROL IS EVER BUILT ─────────────────────
            // On EITHER side of the purchase flag. The row renders fully priced and simply has no
            // button — it cannot be bought, so it cannot disappoint. NO ROW CARRIES THIS TODAY
            // (see PackDef.AnchorOnly for why: WO-1121 is the same-day ruling that made the top
            // rungs buyable behind the wallet rule, and flagging one here would walk that back).
            if (pack.AnchorOnly)
            {
                MakeText(host, StoreStrings.Get(StoreStrings.KeyCardAnchor), 32,
                    ElarionUi.ParchmentDim, FontStyles.Italic, TextAlignmentOptions.Center, ctaMin, ctaMax);
                FlowTrace.Step("Store", $"BuildSpotlightCta '{pack.Sku}': anchorOnly — NO Buy control built (either side of the flag).");
                return;
            }

            if (_vm.IsOwned(pack.Sku))
            {
                MakeText(host, StorePresentationText.CardOwned.Resolve(), 38,
                    new Color(0.55f, 0.90f, 0.55f, 1f), FontStyles.Bold,
                    TextAlignmentOptions.Center, ctaMin, ctaMax);
                return;
            }

            // A durable submitted payment owns the CTA until reconciliation resolves it.
            // Rendering another Buy face here would invite a duplicate charge.
            if (PurchaseEntitlementVerifier.HasPending(pack.Sku))
            {
                // P0 (device, 2026-08-22): this used to be plain text. The screen instructed the
                // player to "reopen this offer to reconcile", but reopening rebuilt the same inert
                // label, leaving a finalized payment permanently undeliverable. This is deliberately
                // a REAL button which re-enters Purchase(). Purchase() sees HasPending before it can
                // call SendPayment, so this path verifies the recorded receipt and cannot pay twice.
                var reconcile = ElarionUiKit.BuildObsidianButton(host,
                    "Reconcile - no new payment",
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    _purchaseInFlight ? ElarionUiKit.ObsidianButtonColor.Gray
                                      : ElarionUiKit.ObsidianButtonColor.Yellow,
                    ctaMin, ctaMax,
                    () => Purchase(pack, SelectedCurrency(pack.Sku)).Forget());
                if (reconcile != null) reconcile.interactable = !_purchaseInFlight;

                var reconcileLabel = reconcile != null
                    ? reconcile.GetComponentInChildren<TMP_Text>(true)
                    : null;
                if (reconcileLabel != null)
                {
                    SeatCtaLabelInPx(reconcileLabel);
                    ElarionUiKit.FitSingleLine(reconcileLabel, ElarionUi.FontFloorMobile, 38f);
                }
                return;
            }

            // WO-1121: the flag test lives BEHIND PurchaseGate.CanBuy() and is not read here. The
            // gate answers the same question plus two more the raw flag cannot (a flag-ON build whose
            // rail has no resolvable mint; and the owner's wallet rule - above $4.99 off-chain
            // (2026-08-21), EVERY price on the Solana rail (WO-1386, 2026-09-04)) — and, the
            // load-bearing part, PackStore.Purchase() consults the SAME method, so the button and
            // the charge path can never disagree. Re-reading FeatureFlags here would re-open exactly
            // the UI-only gate the ruling forbids. The refusal sentence is chosen by
            // PurchaseGate.WalletRefusalSentence per channel - never worded here.
            if (!PurchaseGate.CanBuy(pack, out string gateReason))
            {
                // Two shapes of refusal, and the difference matters to the player:
                //   * the whole rail is closed  -> nothing they can do, so a plain "Coming soon"
                //     label, never a button that looks tappable and is not (WO-1121 §3.5).
                //   * the WALLET rule is the blocker -> there IS something they can do right now, so
                //     this is a REAL button that starts the connect flow.
                // The state is carried by the WORDS in both cases — the owner is red/green
                // colourblind, so a colour swap alone would not be readable.
                string blockedLabel = PurchaseGate.BlockedCtaLabel(pack);
                bool walletIsTheBlocker = PurchaseGate.WalletIsTheBlocker(pack);

                // ⛔ WO-1323 — THE "CONNECT WALLET" DOOR IS A SOLANA DOOR AND MUST NOT OPEN FOR A PI
                // PLAYER. PurchaseGate is UNTOUCHED by WO-1323: the rule (the $4.99 guest ceiling
                // then; wallet at EVERY price on Pi since WO-1386, 2026-09-04) refuses the same packs
                // the gate refuses on every other surface, and nothing about walletAllowed
                // or the SKR charge path moves. What changes is only the FACE this store puts on that
                // refusal — a plate that names the Pi truth, instead of a button that would send a Pi
                // player into a Solana wallet-connect flow they have no use for. A refusal is a PLATE
                // (UI-002); this is that rule applied to an audience the button was never written for.
                if (walletIsTheBlocker && PiDisplay)
                {
                    // WO-1386 (owner 2026-09-04: "mark anything for Pi as same logic based on USD"):
                    // Pi has no guest tier either, so the plate no longer formats the $4.99 line
                    // (StoreStrings.KeyPiWalletGate is STALE - see its note); it says wallet-at-any-
                    // price, Pi-worded, still a PLATE and not the Solana connect button.
                    string piGate = StorePiText.WalletGate.Resolve();
                    FitInto(MakeText(host, piGate, 30, ElarionUi.ParchmentDim, FontStyles.Italic,
                        TextAlignmentOptions.Center, ctaMin, ctaMax), 30);
                    FlowTrace.Step("Store", $"BuildSpotlightCta '{pack.Sku}': wallet-rule refusal, PI wording — " +
                                            "the Solana connect button is withheld under the Pi skin (WO-1323).");
                    return;
                }

                if (!walletIsTheBlocker)
                {
                    // A refusal is a PLATE, never a button (UI-002): nothing here invites a tap that
                    // nothing answers.
                    FitInto(MakeText(host, blockedLabel, 32, ElarionUi.ParchmentDim, FontStyles.Italic,
                        TextAlignmentOptions.Center, ctaMin, ctaMax), 32);
                }
                else
                {
                    string reasonForBanner = gateReason;
                    var connect = ElarionUiKit.BuildObsidianButton(host,
                        blockedLabel,
                        ElarionUiKit.ObsidianButtonStyle.Style1,
                        _purchaseInFlight ? ElarionUiKit.ObsidianButtonColor.Gray
                                          : ElarionUiKit.ObsidianButtonColor.Yellow,
                        ctaMin, ctaMax,
                        () => ConnectForWalletGate(reasonForBanner).Forget());
                    if (connect != null) connect.interactable = !_purchaseInFlight;

                    var connectLabel = connect != null ? connect.GetComponentInChildren<TMP_Text>(true) : null;
                    if (connectLabel != null)
                    {
                        SeatCtaLabelInPx(connectLabel);
                        ElarionUiKit.FitSingleLine(connectLabel, ElarionUi.FontFloorMobile, 38f);
                    }
                }

                FlowTrace.Step("Store", $"BuildSpotlightCta '{pack.Sku}': Buy REFUSED by PurchaseGate — \"{gateReason}\" " +
                                        $"(face='{blockedLabel}', actionable={walletIsTheBlocker}).");
                return;
            }

            // ⛔ THE SERVER SAYS THIS ROW IS NOT SELLABLE TO THIS VIEWER -> NO BUY CONTROL (WO-1190).
            // The price still stands above (the shelf card and the spotlight ledger both print it);
            // what is withheld is the INVITATION. Before this, a not-sellable SKU showed its real
            // price beside a live Buy button and the refusal only arrived after the player committed
            // — fail-closed, but a shelf that lies about what it will sell you.
            //
            // A refusal is a PLATE, never a button (UI-002, the same rule the two branches above
            // follow): there is nothing the player can do here, so nothing here may look tappable.
            // That also settles the touch floor — a plate is not a control, so there is no control
            // to shrink under MinTouchPx; the only interactive rect in this host is the one we did
            // not build.
            //
            // ⛔ AND THE SERVER'S OWN SENTENCE IS PRINTED VERBATIM. It is worded for this viewer and
            // this network; paraphrasing it here would put a second opinion on the screen, and
            // SellableReasonFor already substitutes a worded fallback when the row carried none —
            // so this is never blank and never a bare code.
            var paymentProvider = PaymentProviders.Current;
            bool usesProviderRail = OwnsTheRail(paymentProvider);

            // ⛔ WO-1323 — A PI PLAYER WITH NO PI RAIL GETS NO BUY CONTROL AT ALL, and this is the
            // line that guarantees it. Everything below assumes one of two rails owns the charge; a
            // Pi-skinned session where the provider never registered (which is EXACTLY the state the
            // owner hit on 2026-09-02) would otherwise fall through to the SOLANA path — a Buy button
            // that opens a wallet-connect flow a Pi player has no use for, priced on a rail they do
            // not hold. A refusal is a PLATE, never a button (UI-002), and the shelf card above still
            // shows the pack and its USD anchor: browsable, honest, not purchasable here.
            if (PiDisplay && !PiRailOwnsTheStore)
            {
                FitInto(MakeText(host, StorePiText.RailUnavailable.Resolve(), 30,
                    ElarionUi.ParchmentDim, FontStyles.Italic,
                    TextAlignmentOptions.Center, ctaMin, ctaMax), 30);
                FlowTrace.Warn("Store", $"BuildSpotlightCta '{pack.Sku}': the PI SKIN is active but NO Pi payment " +
                                        "rail is registered, so no Buy control is built. The Solana path below is " +
                                        "NOT offered as a substitute (WO-1323).");
                return;
            }

            if (!usesProviderRail && !PurchaseQuoteService.IsSellable(pack.Sku))
            {
                string notSellable = PurchaseQuoteService.SellableReasonFor(pack.Sku);
                if (string.IsNullOrEmpty(notSellable))
                    notSellable = PurchaseQuoteService.NotSellableFallbackMessage;
                FitInto(MakeText(host, notSellable, 30, ElarionUi.ParchmentDim, FontStyles.Italic,
                    TextAlignmentOptions.Center, ctaMin, ctaMax), 30);
                FlowTrace.Step("Store", $"BuildSpotlightCta '{pack.Sku}': server marked this row NOT " +
                                        $"sellable - \"{notSellable}\". Price is still shown; the Buy " +
                                        "control is not built (nothing charged, nothing to tap).");
                return;
            }

            var rail = SelectedCurrency(pack.Sku);   // SKR canary by default (MON-1147)

            // ⛔ NO SERVER PRICE, NO BUY BUTTON (WO-1158). The client cannot price a pack, so when
            // the server has not quoted one there is nothing honest to put on the face of a button.
            // A refusal is a PLATE, never a button (UI-002): nothing here invites a tap that nothing
            // answers, and the reason is WORDS - the owner is red/green colourblind and a greyed
            // button carries no meaning in greyscale.
            if (usesProviderRail && !paymentProvider.CanBuy(pack.Sku, out string providerReason))
            {
                FitInto(MakeText(host, providerReason, 30, ElarionUi.ParchmentDim, FontStyles.Italic,
                    TextAlignmentOptions.Center, ctaMin, ctaMax), 30);
                return;
            }

            if (!usesProviderRail && rail == CurrencyKind.Skr && pack.AmountFor(rail) <= 0d)
            {
                FitInto(MakeText(host,
                    "Price unavailable right now. Nothing has been charged; reopen the store to retry.",
                    30, ElarionUi.ParchmentDim, FontStyles.Italic,
                    TextAlignmentOptions.Center, ctaMin, ctaMax), 30);
                FlowTrace.Warn("Store", $"BuildSpotlightCta '{pack.Sku}': NO server quote for SKR - " +
                                        "Buy withheld rather than shown against an invented number.");
                return;
            }

            // WO-1323: the devnet marker describes the SOLANA network this purchase would settle on.
            // Under the Pi skin no Solana transaction is contemplated, so the marker is not drawn —
            // it would label a Pi payment with a Solana network.
            if (!PiDisplay && roomForNetwork && _wallet != null && _wallet.Network == WalletNetwork.Devnet)
                MakeText(host, "DEVNET - TEST TOKEN", 30, ElarionUi.Gold,
                    FontStyles.Bold, TextAlignmentOptions.Center,
                    new Vector2(ctaMin.x, netY0), new Vector2(ctaMax.x, netY1));
            else if (!PiDisplay && !roomForNetwork && _wallet != null && _wallet.Network == WalletNetwork.Devnet)
                FlowTrace.Warn("Store", "BuildCommerce: no room above the Buy control for the DEVNET " +
                                        "marker in this composition (" + NightMarketComposition.Describe(_plan) +
                                        ") - the network label is DROPPED rather than drawn over the button.");
            var buy = ElarionUiKit.BuildObsidianButton(host,
                $"Buy - {StorePriceMajor(pack)}",
                ElarionUiKit.ObsidianButtonStyle.Style1,
                _purchaseInFlight ? ElarionUiKit.ObsidianButtonColor.Gray
                                  : ElarionUiKit.ObsidianButtonColor.Yellow,
                ctaMin, ctaMax,
                () => Purchase(pack, SelectedCurrency(pack.Sku)).Forget());
            if (buy != null) buy.interactable = !_purchaseInFlight;

            var buyLabel = buy != null ? buy.GetComponentInChildren<TMP_Text>(true) : null;
            if (buyLabel != null) ElarionUiKit.FitSingleLine(buyLabel, ElarionUi.FontFloorMobile, 42f);

            // G3 — the specular sweep. The one element that must never look asleep. It rides ON the
            // button and carries no information: strip it and the CTA is identical.
            if (buy != null && _aurora != null)
                _aurora.AddSweep(buy.transform, "cta-sweep", Vector2.zero, Vector2.one,
                    new Color(1f, 1f, 1f, 0.55f), 6f, 0.7f);
        }

        private CurrencyKind SelectedCurrency(string sku)
        {
            return _selectedCurrency.TryGetValue(sku, out var c) ? c : _defaultCurrency;
        }

        /// <summary>
        /// WO-1318 — does an IPaymentProvider OWN the charge on this artifact? True for the Google Play
        /// rail (as before) and now also for the Pi Browser rail.
        ///
        /// <para>⛔ IT REPLACED FOUR COPIES OF <c>Channel == PaymentChannel.GooglePlay</c>. Those four
        /// sites are not independent opinions — they are one fact (who takes the money) asked four
        /// times, and the CTA builder disagreeing with <see cref="Purchase"/> about it is the exact
        /// shape of "a price beside a live Buy button and a refusal only after the player committed".
        /// One method, four callers.</para>
        ///
        /// <para>What it deliberately does NOT do is claim the SKR/Solana path changed. When no
        /// provider owns the rail this returns false and every line below behaves byte-identically to
        /// before — Pi is additive (WO-1318 "What NOT to touch").</para>
        /// </summary>
        private static bool OwnsTheRail(IPaymentProvider provider) =>
            provider != null &&
            (provider.Channel == PaymentChannel.GooglePlay || provider.Channel == PaymentChannel.PiBrowser);

        // =====================================================================
        //  WO-1323 — THE PI DISPLAY HINGE, AND WHY IT IS THE SKIN AND NOT THE RAIL
        // ---------------------------------------------------------------------
        //  ⛔ THE PROVIDER CHANNEL IS THE WRONG QUESTION FOR A LABEL. WO-1318 made
        //  the price labels ask `PaymentProviders.Current.Channel == PiBrowser`, and
        //  on 2026-09-02 the owner opened the Night Market in REAL Pi Browser and read
        //  "1022 SKR / 2555 SKR / BUY - 255 SKR" off the shelf. Her session log says
        //  exactly why:
        //
        //      Currency skin resolved: 'pi' (auth=PiSdk, symbol=pi, identity=PiUid)
        //
        //  The SKIN resolved to Pi. The PROVIDER did not register — PaymentChannel is
        //  resolved from WebGLPiPlatform.IsPiBrowserEnvironment and PiPlatform.Current,
        //  either of which can be absent while the player is unmistakably a Pi player.
        //  So the rail answered "no Pi here" and every label fell through to the $SKR
        //  branch, which is the ONE audience that must never see it.
        //
        //  ⛔ THE TWO QUESTIONS ARE GENUINELY DIFFERENT AND BOTH ARE KEPT:
        //     WHO IS LOOKING  -> the SKIN  (PiDisplay)  -> what the shelf may SAY
        //     WHO TAKES MONEY -> the RAIL  (OwnsTheRail) -> what may be CHARGED
        //  A Pi player with no Pi rail must see Pi wording and NO purchase; collapsing
        //  the two either quotes SKR at a Pi player (what happened) or offers a Buy the
        //  rail cannot settle. Never merge them.
        //
        //  ⛔ AND IT IS SKIN ID "pi", NOT MERELY SkinAuthMode.PiSdk. The V1 generic
        //  "wallet" skin also carries PiSdk auth (see CurrencySkinResolver.WalletDefault
        //  — auth mirrors the Pi defaults so ONLY presentation changes), so testing the
        //  auth mode alone would drag the wallet skin into Pi wording it never asked for.
        // =====================================================================

        /// <summary>The Pi rail is registered and will actually take the money.</summary>
        private static bool PiRailOwnsTheStore
        {
            get
            {
                var provider = PaymentProviders.Current;
                return provider != null && provider.Channel == PaymentChannel.PiBrowser;
            }
        }

        /// <summary>The player in front of this store is a PI player. See the block above.</summary>
        private static bool PiSkinActive
        {
            get
            {
                var skin = CurrencySkinResolver.Active;
                return skin != null &&
                       skin.AuthMode == SkinAuthMode.PiSdk &&
                       string.Equals(skin.SkinId, "pi", StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// The one predicate every price, chip and refusal in this file asks before it may print a
        /// $SKR figure or wallet furniture. True for the Pi skin OR the Pi rail; false everywhere
        /// else, which is why the SKR skin is byte-for-byte the store it was.
        /// </summary>
        private static bool PiDisplay => PiSkinActive || PiRailOwnsTheStore;

        /// <summary>
        /// The SERVER's Pi figure for this sku, or empty. Empty is the honest answer and it has three
        /// causes that all mean the same thing to the player: no Pi rail, the rail cannot sell this
        /// sku, or the server refused/expired the quote.
        /// <para>⛔ THERE IS NO ELSE-BRANCH THAT COMPUTES ONE. The client has no rate and must never
        /// acquire one (WO-1318's security model). If this returns empty, the caller says WHERE the
        /// price comes from — it does not derive it.</para>
        /// </summary>
        private static string PiQuotedAmount(PackDef pack)
        {
            if (pack == null || !PiRailOwnsTheStore) return string.Empty;
            var provider = PaymentProviders.Current;
            if (provider == null) return string.Empty;
            DisplayPrice price = provider.GetDisplayPrice(pack.Sku);
            return price.Available ? price.LocalizedText : string.Empty;
        }

        /// <summary>True when the Pi rail will sell THIS sku right now (its own gate, asked once).</summary>
        private static bool PiCanSell(PackDef pack)
        {
            if (pack == null || !PiRailOwnsTheStore) return false;
            var provider = PaymentProviders.Current;
            return provider != null && provider.CanBuy(pack.Sku, out _);
        }

        private string StorePriceMajor(PackDef pack)
        {
            var provider = PaymentProviders.Current;
            if (provider != null && provider.Channel == PaymentChannel.GooglePlay)
            {
                var price = provider.GetDisplayPrice(pack != null ? pack.Sku : string.Empty);
                return price.Available ? price.LocalizedText : "Price unavailable";
            }

            // WO-1323: the Pi player's headline number is the SERVER's Pi amount when one exists, and
            // the USD ANCHOR — a real authored number — when it does not. It is NEVER the $SKR label,
            // which is meaningless inside Pi Browser and names a token this game has never held.
            if (PiDisplay)
            {
                string pi = PiQuotedAmount(pack);
                if (!string.IsNullOrEmpty(pi)) return pi;
                return pack != null ? pack.UsdReference : string.Empty;
            }

            // WO-1409: without a signing wallet the authored USD anchor is still known. It is the
            // honest browse price; the unavailable SKR quote is not repeated on every card.
            if (WalletlessBrowsing)
                return pack != null ? pack.UsdReference : string.Empty;

            return pack != null ? pack.AmountLabel(_defaultCurrency) : string.Empty;
        }

        private string StorePriceMinor(PackDef pack)
        {
            var provider = PaymentProviders.Current;
            if (provider != null && provider.Channel == PaymentChannel.GooglePlay) return string.Empty;

            if (PiDisplay)
            {
                // Pi amount on top -> the USD anchor sits under it, exactly as the WO asks ("Pi
                // amounts from the server quote, ALONGSIDE the USD anchor it already displays").
                if (!string.IsNullOrEmpty(PiQuotedAmount(pack)))
                    return pack != null ? pack.UsdReference : string.Empty;

                // No server figure. Say WHY, in the player's own terms — never guess one.
                if (!PiRailOwnsTheStore) return StorePiText.RailUnavailable.Resolve();
                return PiCanSell(pack)
                    ? StorePiText.PriceAtCheckout.Resolve()
                    : StorePiText.NotOnSale.Resolve();
            }


            if (WalletlessBrowsing) return string.Empty;

            return pack != null ? pack.UsdApprox() : string.Empty;
        }

        /// <summary>
        /// Asks the Pi rail to pull the shelf's Pi figures FROM THE SERVER, then repaints once.
        /// <para>⛔ TRANSPORT ONLY, exactly like <see cref="RefreshQuotedPrices"/> on the SKR side:
        /// this method has no rate, no conversion and no fallback. A refusal repaints nothing and the
        /// cards keep the WORDS they had.</para>
        /// </summary>
        private void RefreshPiDisplayPrices()
        {
            if (!PiDisplay) return;

            var refresher = PaymentProviders.Current as IDisplayPriceRefresher;
            if (refresher == null)
            {
                FlowTrace.Step("Store",
                    "Pi display prices: no Pi rail is registered on this artifact, so there is nothing to " +
                    "ask. The shelf shows the USD anchor and says Pi purchasing is unavailable - it does " +
                    "NOT fall back to the SKR figures (WO-1323).");
                return;
            }

            var skus = new List<string>();
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !PackCatalog.IsOnBrowsableShelf(pack)) continue;
                skus.Add(pack.Sku);
            }

            // ⛔ WO-1323 OWNER RULING — THE SPOTLIGHT SKU IS ASKED FOR TOO, AND WITHOUT THIS LINE THE
            // WHOLE FEATURE IS SILENT. This walk is the browsable shelf, and the one Pi-quotable sku
            // is deliberately NOT on it (storeVisible:false, WO-1069) - so before this line the
            // server was never asked for the only figure it can actually give, and the spotlight
            // would have shown "Priced in Pi at checkout" forever while /api/pi/quote sat live and
            // answering. The provider still filters to its own EnabledSku, so this adds at most one
            // request and can never widen the rail from the client.
            var spotlight = ResolvePiSpotlightPack();
            if (spotlight != null && !skus.Contains(spotlight.Sku)) skus.Add(spotlight.Sku);

            FlowTrace.Step("Store", $"Pi display prices: asking the server for up to {skus.Count} shelf sku(s).");
            refresher.RefreshDisplayPrices(skus, changed =>
            {
                if (!changed || this == null || !isActiveAndEnabled) return;
                Render();
            });
        }

        // =====================================================================
        //  The wallet mirror — READ-ONLY, ASYNC, and never a laundered zero
        // =====================================================================

        /// <summary>
        /// Fills the header chip from the player's OWN wallet. Never blocks the store open: the chip
        /// renders its state first and is refilled when (and if) the reads return.
        ///
        /// <para>⛔ THE ZERO IS AMBIGUOUS AND IS NOT LAUNDERED. <c>balance.Skr == 0</c> can mean the
        /// player genuinely holds none, OR that the SKR mint is unprovisioned on this network
        /// (<c>WalletEndpoints.SkrMint</c> ships EMPTY on both networks today, so the provider's own
        /// comment applies: "leave 0"), OR that the RPC failed (<c>WalletService.GetBalance</c>
        /// catches and returns a zeroed struct). Three different facts. Printing a confident "0 SKR"
        /// would collapse them into one number and tell the player something we do not know — the
        /// same defect class that got keepers-satchel hidden. So: an unconfigured mint is caught
        /// BEFORE the call, and a returned zero renders as UNAVAILABLE rather than as none.
        /// Distinguishing a genuine zero from a failed read needs <c>WalletService.GetBalance</c> to
        /// report success separately; that is a money-path file this WO does not touch.</para>
        /// </summary>
        private async UniTaskVoid RefreshWalletMirror()
        {
            using var _ = FlowTrace.Enter("Store", "RefreshWalletMirror (read-only)");

            // ⛔ WO-1323 — A PI PLAYER HAS NO SOLANA WALLET TO MIRROR, so this whole read is skipped
            // rather than run and then hidden. The owner's Pi Browser session showed "Connect a
            // wallet to see your balance" and a "Mainnet  SKR" chip beside it: both are Solana
            // furniture, and the second names a token the game has never held. RenderBalanceLabel
            // paints the Pi notice instead; nothing here is disabled for the SKR skin.
            if (PiDisplay)
            {
                FlowTrace.Step("Store",
                    "wallet mirror SKIPPED: the Pi skin is active, so there is no Solana wallet to read " +
                    "and no SKR balance to mirror. The header carries the Pi notice instead (WO-1323).");
                SetBalanceState(BalanceState.NoWallet);
                return;
            }

            if (_wallet == null || !_wallet.IsConnected)
            {
                // ⛔ NO WALLET = NO NUMBER. A "0" here would read as "you have none" when the truth
                // is "we did not ask".
                SetBalanceState(BalanceState.NoWallet);
                return;
            }

            string mint = WalletEndpoints.SkrMint(_wallet.Network);
            if (string.IsNullOrEmpty(mint))
            {
                FlowTrace.Step("Store", $"wallet mirror: no SKR mint configured for {_wallet.NetworkLabel} — " +
                                        "showing UNAVAILABLE rather than a zero that would read as 'you have none'.");
                SetBalanceState(BalanceState.Unavailable);
                return;
            }

            SetBalanceState(BalanceState.Checking);

            WalletBalance balance;
            try
            {
                balance = await _wallet.GetBalance();
            }
            catch (Exception ex)
            {
                // No silent catch (§12). The chip degrades; the store is untouched.
                FlowTrace.Fail("Store", $"wallet mirror: GetBalance THREW: {ex.GetType().Name}: {ex.Message} — " +
                                        "chip shows UNAVAILABLE. Nothing else on the screen depends on it.");
                SetBalanceState(BalanceState.Unavailable);
                return;
            }

            if (balance.Skr <= 0d)
            {
                SetBalanceState(BalanceState.Unavailable);
                return;
            }

            _balanceSkr = balance.Skr;
            SetBalanceState(BalanceState.Known);

            // ⛔ WO-1334 — NO SECOND AWAIT HERE ANY MORE. This used to chain
            // `await RefreshFiatApproximation()`, a live Jupiter quote whose ONLY consumer was the
            // `~$12.40` tail on the header chip. The owner ruled the chip is `SKR: <balance>` and
            // nothing else, so the quote went with the tail rather than being left to run, fail,
            // retry and expire against a reader that no longer exists. The balance itself is
            // unchanged: same read, same never-laundered zero, same four honest states.
        }

        private void SetBalanceState(BalanceState state)
        {
            _balanceState = state;
            RenderBalanceLabel();
        }

        private void RenderBalanceLabel()
        {
            if (_balanceLabel == null) return;

            // ⛔ WO-1323 — FIRST LINE, BEFORE THE SWITCH, ON PURPOSE. Every balance state below is a
            // sentence about a SOLANA wallet, and the tail of this method prints a literal "SKR"
            // chip. Under the Pi skin there is exactly one honest thing to say here, and putting the
            // test at the top means no future state can be added past it and reach the SKR text.
            if (PiDisplay)
            {
                _balanceLabel.text = StorePiText.HeaderNotice.Resolve();
                return;
            }

            switch (_balanceState)
            {
                case BalanceState.NoWallet:
                    // ⛔ THESE TWO SENTENCES ARE CANON KEYS, NOT LITERALS. They were the only store
                    // copy still living in code (CLAUDE.md §7 puts every player-facing string in
                    // canon-strings.json, both canonical copies, ASCII only) — and they are UI-002's
                    // wallet-identity wording, which is exactly the class of sentence that must be
                    // reviewable in data rather than buried in a switch on a money screen.
                    //
                    // ⛔ WO-1334 — THE ADDRESS IS GONE FROM THIS BRANCH, NOT SHRUNK. Owner ruling
                    // 2026-09-03: *"they dont need address"*. `storeBalanceBoundAddress` no longer
                    // carries a {0}, so this is Get, not Format — the shortened base58 is not
                    // something a player verifies by eye and it was the single biggest contributor
                    // to the clump. The BRANCH stays, because "a live account is attached" and
                    // "only a durable identity exists" remain two different facts and each still
                    // gets its own SENTENCE.
                    if (WalletlessBrowsing)
                        // WO-1409: ONE source for this sentence (StoreStrings), because the trace
                        // and the oracle both probe for it. See WalletlessBrowsingBanner's header.
                        _balanceLabel.text = StoreStrings.WalletlessBrowsingBanner;
                    else if (_wallet != null && _wallet.Account.IsValid)
                        _balanceLabel.text = StoreStrings.Get(StoreStrings.KeyBalanceBoundAddress);
                    else if (PurchaseGate.HasDurableIdentity)
                        _balanceLabel.text = StoreStrings.Get(StoreStrings.KeyBalanceBoundIdentity);
                    else
                        _balanceLabel.text = StoreStrings.Get(StoreStrings.KeyBalanceNoWallet);
                    return;
                case BalanceState.Checking:
                    _balanceLabel.text = StoreStrings.Get(StoreStrings.KeyBalanceChecking);
                    return;
                case BalanceState.Unavailable:
                    _balanceLabel.text = StoreStrings.Get(StoreStrings.KeyBalanceUnavailable);
                    return;
            }

            // ── WO-1334b — THE CONNECTED CHIP IS ONE LINE: `Balance: 3,817 SKR`
            //
            // ⛔ THE LEADING WORD IS THE RULING, and it RETIRES the same day's earlier `SKR: 3,817`.
            // Owner, 2026-09-03: *"Maybe we could put the word balance and then put their SKR total
            // ... I see every other site in the world does it. All I really care about: in the top
            // left put their balance of what they have just in SKR so they know what they can
            // afford immediately."* A bare `SKR: 3,817` names a token; `Balance: 3,817 SKR` answers
            // the question the player actually opened the store with. The sentence is
            // `storeBalanceValue` in canon-strings.json, so re-wording it is a data edit, not a
            // code edit — and both shipped copies are pinned.
            //
            //
            // ⭐ WHY THE WORD "Connected" IS NOT HERE, so it does not get helpfully re-added: a
            // balance that RENDERS AT ALL already proves the wallet is connected — an unconnected
            // wallet reaches one of the four sentences above instead and never gets a number. So
            // "Connected" would be redundant with the digits beside it. The DISCONNECTED states are
            // where the words must be explicit, and that is exactly where they now are: every
            // storeBalance* sentence above says its state in WORDS, never by hue, because the owner
            // is red/green colourblind (CLAUDE.md §7).
            //
            // ⛔ THE NETWORK TAIL IS A MONEY-SAFETY SIGNAL AND IS NOT OPTIONAL POLISH. On Devnet
            // the SKR is free and a purchase completes for nothing (the matched-pair invariant that
            // MonetizationActivationRegression pins). It is drawn only when the network is NOT
            // mainnet, which is the smallest form that still carries the warning: on mainnet the
            // chip is the owner's exact `SKR: <balance>`, and the moment it is anything else the
            // word appears. Silence therefore means mainnet and a WORD means "this is not real
            // money" — the loud case is the dangerous one, which is the right way round. This
            // replaces the old baked "Mainnet" plate that could not tell the two apart at all.
            string text = StoreStrings.Format(StoreStrings.KeyBalanceValue, _balanceSkr.ToString("N0"));
            if (_wallet != null && _wallet.Network != WalletNetwork.Mainnet)
                text += "  " + _wallet.NetworkLabel.ToUpperInvariant();
            _balanceLabel.text = text;
        }

        // =====================================================================
        //  Contents description (shared by the card line and the ledger's honesty)
        // =====================================================================

        private static string DescribeContents(PackDef pack)
        {
            const int visibleItems = 2;
            var items = new List<string>();
            var c = pack != null ? pack.Contents : null;
            if (c != null)
            {
                if (c.Cosmetics != null && c.Cosmetics.Count > 0)
                    items.Add(c.Cosmetics.Count + (c.Cosmetics.Count == 1 ? " cosmetic" : " cosmetics"));

                var econ = c.Economy;
                if (econ != null)
                {
                    // ⚠ Wood and Iron were MISSING from this list while PackStoreVM.ApplyPackContents
                    // has granted them since ECON-01. Describe every key the grant seam actually pays
                    // out, in grant order — the same rule PackCatalog.LedgerEconomyKeys encodes for
                    // the spotlight bars.
                    AddContentAmount(items, econ.Wood, "wood");
                    AddContentAmount(items, econ.Iron, "iron");
                    AddContentAmount(items, econ.Crystals, "crystals");
                    AddContentAmount(items, econ.Food, "stone");
                    AddContentAmount(items, econ.Coins, "coins");
                }

                // WO-1118 §2.3 — a card may only list convenience the player can actually SPEND.
                // The redeemable set is PackCatalog.IsRedeemableConvenience (single source).
                // Legal-but-unredeemable kinds are still GRANTED into GearInventory — they are
                // simply not advertised until a redeemer ships.
                if (c.Convenience != null && c.Convenience.Count > 0)
                {
                    foreach (var item in c.Convenience)
                    {
                        if (item == null || item.Count <= 0 || string.IsNullOrEmpty(item.Kind)) continue;
                        if (!PackCatalog.IsRedeemableConvenience(item.Kind)) continue;
                        if (PackCatalog.IsPermanentBuilderKind(item.Kind))
                            items.Add("Permanent builder (+1 crew)");
                        else if (item.Kind.IndexOf("lantern", StringComparison.OrdinalIgnoreCase) >= 0)
                            items.Add((item.Kind.Contains("3x") ? "3x" : "2x") +
                                      " lantern x" + item.Count + " runs");
                        else
                            items.Add(item.Count + "x " + item.Kind.Replace('-', ' ').Replace('_', ' '));
                    }
                }
            }

            if (items.Count == 0) return "-";
            int shown = Mathf.Min(visibleItems, items.Count);
            string summary = string.Join(", ", items.GetRange(0, shown));
            int remaining = items.Count - shown;
            return remaining > 0 ? summary + " +" + remaining + " more" : summary;
        }

        private static void AddContentAmount(List<string> items, int amount, string label)
        {
            if (items == null || amount <= 0) return;
            items.Add(amount.ToString("N0") + " " + label);
        }

        // =====================================================================
        //  ⭐ WO-1409 — THE ONE-WORD BADGE'S x BAND, DERIVED, NOT PICKED.
        // ---------------------------------------------------------------------
        //  ⛔ 0.04..0.44 WAS WRONG AND IT SHIPPED A TRUNCATED BADGE. The first
        //  WO-1409 pass moved the pill left (right, and kept) but SHRANK it from
        //  the authored 0.70 to 0.40 without re-deriving the fit budget, and
        //  Builds/ui-capture/NightMarket_2670x1200.png (09-05 23:56) shows the
        //  result: builders-hour's badge reads "FIR..." while starters-hand's
        //  "BEST" fits. That is not a font that shrank -- FontBadge is 30 and
        //  ElarionUi.FontFloorMobile is ALSO 30, so FitSingleLine has nowhere to
        //  go and ellipsises instead. Width is the only free variable here.
        //
        //  THE ARITHMETIC, in StorePackCard's own units so the two files agree:
        //    * packs.json authors exactly three storeBadge values today --
        //      "BEST VALUE", "BEST START", "FIRST BUY" -- so OneWordBadge below
        //      can only ever emit BEST (4) or FIRST (5). FIVE GLYPHS is the case
        //      to size for; a longer one-word badge would need this redone.
        //    * StorePackCard's measured budget: "BEST VALUE" (10 glyphs) is 213 px
        //      bold at FontBadge 30 with PillLetterSpacing 2 => ~21.3 px/glyph, so
        //      "FIRST" measures ~107 px.
        //    * The pill's text box is the pill less PillPadXPx (14) per side, i.e.
        //      width - 28. (Check it against the authored numbers: 0.70 of a 375 px
        //      card = 262 px pill -> a 234 px box, which is the figure written in
        //      StorePackCard's own header.)
        //    * Carry the same 10% margin that budget carries: a 118 px box, so a
        //      146 px pill.
        //    * Worst case is the NARROWEST card the shelf permits -- not the
        //      narrowest one shipped today -- which is StorePackCard.MinCardWidthPx
        //      (300). 146 / 300 = 0.487, rounded up to 0.50.
        //  Hence 0.04 .. 0.54: still hard left, still clear of the art's focal and
        //  right edge (46% of the card top is untouched), and 14% of margin at the
        //  floor instead of a guaranteed ellipsis.
        //
        //  ⚠ THE PILL SITS OVER THE ART WELL BY CONSTRUCTION and that is NOT what
        //  this band fixes. StorePackCard.BuildPill anchors to the CARD top and the
        //  well occupies it, so "badge vs art" cannot be driven to zero from here --
        //  only bounded. NightMarketNoWalletRegression asserts the bound (left half
        //  of the well) rather than an overlap-free card the shared template cannot
        //  produce; a card redesign is the only thing that would, and it is not this
        //  work order's lane.
        // =====================================================================

        /// <summary>Left edge of the one-word badge, as a fraction of the card. See the block above.
        /// <para>PUBLIC so the oracle asserts THE SHIPPED NUMBER rather than a copy of it.</para></summary>
        public const float OneWordBadgeX0 = 0.04f;
        /// <summary>Right edge of the one-word badge. DERIVED (see the block above), never picked.</summary>
        public const float OneWordBadgeX1 = 0.54f;

        /// <summary>WO-1409: merchandising badges are one readable word, never art-covering copy.
        /// <para>PUBLIC so NightMarketNoWalletRegression measures THE WORD THIS METHOD EMITS at the
        /// real font, instead of re-implementing the rule and proving only that its own copy agrees
        /// with itself.</para></summary>
        public static string OneWordBadge(string badge)
        {
            if (string.IsNullOrWhiteSpace(badge)) return string.Empty;
            if (badge.IndexOf("BEST", StringComparison.OrdinalIgnoreCase) >= 0) return "BEST";
            if (badge.IndexOf("FIRST", StringComparison.OrdinalIgnoreCase) >= 0) return "FIRST";
            int split = badge.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            return split > 0 ? badge.Substring(0, split) : badge;
        }

        /// <summary>Exact receipt inventory, including grants intentionally omitted from shelf copy.</summary>
        private static string DescribeGrantedContents(PackDef pack)
        {
            var sb = new StringBuilder();
            var c = pack != null ? pack.Contents : null;
            if (c == null) return "No contents recorded";

            var econ = c.Economy;
            if (econ != null)
            {
                AppendAmount(sb, econ.Wood, "wood");
                AppendAmount(sb, econ.Iron, "iron");
                AppendAmount(sb, econ.Crystals, "crystals");
                AppendAmount(sb, econ.Food, "stone");
                AppendAmount(sb, econ.Coins, "coins");
            }
            if (c.Cosmetics != null)
                foreach (string sku in c.Cosmetics)
                {
                    if (string.IsNullOrWhiteSpace(sku)) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append("cosmetic ").Append(sku);
                }
            if (c.Convenience != null)
                foreach (var item in c.Convenience)
                {
                    if (item == null || item.Count <= 0 || string.IsNullOrWhiteSpace(item.Kind)) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(item.Count).Append("x ").Append(item.Kind.Replace('-', ' ').Replace('_', ' '));
                }
            return sb.Length > 0 ? sb.ToString() : "No item grants";
        }

        private static void AppendAmount(StringBuilder sb, int amount, string label)
        {
            if (amount <= 0) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(amount.ToString("N0")).Append(' ').Append(label);
        }

        // =====================================================================
        //  Purchase flow — UNCHANGED (WO-1050 is presentation only)
        // =====================================================================

        /// <summary>
        /// Runs the full purchase flow for a pack: ensures a wallet is connected,
        /// calls <see cref="WalletService.Pay"/>, awaits devnet confirmation, then
        /// applies the pack contents to <see cref="GameStateService"/>.
        /// </summary>
        public async UniTask<PaymentResult> Purchase(PackDef pack, CurrencyKind currency)
        {
            // ⛔ WO-1149 — STOP THE WORLD FOR THE WHOLE TRANSACTION. Owner, on device 2026-08-22:
            // "we need to stop game during transactions got killed while making purchase test."
            // A purchase is not instant (wallet signs -> chain confirms -> server verifies ->
            // entitlement recorded -> grant -> save verifies), and for all of it the player could
            // neither defend themselves nor cancel without abandoning a transaction that may already
            // have been signed. That is the worst possible moment to force a choice between dying
            // and losing money.
            //
            // ⛔ THIS LINE IS FIRST IN THE METHOD ON PURPOSE, AND IT IS A `using` ON PURPOSE.
            // A hold that fails to release is WORSE than no hold — a frozen game after a completed
            // purchase is a support ticket AND a refund. Placed here, the C# compiler covers EVERY
            // exit below without anyone remembering to: the null-pack guard, the PurchaseGate
            // refusal, the devnet-canary refusal, the already-in-flight and already-owned returns,
            // the wallet-connect cancellation, all four verification outcomes, the pay failure, the
            // catch, and any branch added after this comment was written. Do not convert it to a
            // paired Acquire()/Dispose() — that is exactly the shape that leaves three branches out.
            //
            // (The one exit a `using` cannot cover — the app backgrounded mid-flight and an await
            // that never resumes — is covered by WorldHold's stuck-hold watchdog.)
            //
            // Whole-simulation scope, not combat-only: WorldHold zeroes Time.timeScale, which is the
            // proven, already-wired freeze the pause menu uses. It also stops build/queue timers the
            // player may be watching — accepted, because a transaction is short and bounded while
            // "killed mid-purchase" is unrecoverable.
            using var worldHold = DeNelle.Core.UI.WorldHold.Acquire(DeNelle.Core.UI.WorldHold.ReasonPurchase);
            using var _ = FlowTrace.Enter("Store", $"Purchase pack='{pack?.Sku ?? "<null>"}' {currency}");

            if (pack == null)
            {
                FlowTrace.Fail("Store", "Purchase: pack is null — aborted.");
                return PaymentResult.Failure(string.Empty, currency, "Pack is null.");
            }

            // WO-1388 FUNNEL step 3 of 4 - checkout_started. Before the rail branch and before the
            // gates, so every Buy press is one started checkout and every refusal below is a
            // checkout_failed against it.
            TrackCheckoutStarted(pack);

            using var sharedConfirmation = _sharedCardSession?.EnterConfirmation();

            // WO-1318: widened from GooglePlay-only to "a provider owns this rail" (OwnsTheRail), so
            // the Pi Browser rail takes the same early exit. That exit is load-bearing: everything
            // below it — PurchaseGate's SKR-shaped checks, WalletService.Connect, the SKR quote and
            // the Solana settlement poll — is the SOLANA rail and would veto or dead-end a Pi payment
            // that is perfectly fine. Returning here is what keeps Pi ADDITIVE rather than a rewrite.
            if (OwnsTheRail(PaymentProviders.Current))
                return await PurchaseThroughProvider(pack, currency);

#if MAINNET_CANARY_TEST
            bool isMainnetCanary = string.Equals(pack.Sku, PurchaseGate.MainnetCanarySku,
                StringComparison.Ordinal);
            if (isMainnetCanary)
            {
                // MWA authorization is chain-scoped. Reconnect after selecting Mainnet instead of
                // reusing an earlier Devnet association for this real-value canary.
                if (_wallet.IsConnected) await _wallet.Disconnect();
                _wallet.SetNetwork(WalletNetwork.Mainnet);
            }
#endif

            // ⛔ THE GATE, ON THE CHARGE PATH ITSELF (WO-1121). Defense-in-depth is the weak reading
            // of this check; the strong one is that THIS is where the rule is actually enforced. The
            // CTA builder calls the same PurchaseGate.CanBuy(pack, ...) and can only ever REMOVE a
            // button — but Purchase() is public and is reached from surfaces that never drew that
            // button (a deep link, a future promo). A rule enforced only in the UI is not enforced.
            //
            // The refusal text is the gate's PLAYER-READABLE reason, surfaced on the status banner and
            // returned as the PaymentResult error. Nothing has been charged at this point.
            if (!PurchaseGate.CanBuy(pack, out string gateReason))
            {
                FlowTrace.Warn("Store", $"Purchase '{pack.Sku}' REFUSED at PurchaseGate — \"{gateReason}\" (nothing charged).");
                SetStatus(gateReason);
                // WO-1388: the wallet rule is the ONE refusal that is a funnel step of its own
                // (a guest who has to connect), so it gets its own reason; every other gate
                // sentence is the rail refusing the sale.
                TrackCheckoutFailed(pack.Sku,
                    PurchaseGate.WalletIsTheBlocker(pack) ? FailWalletRequired : FailProviderRefused, gateReason);
                return PaymentResult.Failure(pack.Sku, currency, gateReason);
            }

            if (_wallet.Network == WalletNetwork.Devnet && currency != CurrencyKind.Skr)
            {
                const string skrCanaryOnly = "Today's verified devnet purchase uses test SKR. No other rail was charged.";
                FlowTrace.Warn("Store", $"Purchase '{pack.Sku}' refused: MON-1147 devnet canary is SKR-only.");
                SetStatus(skrCanaryOnly);
                TrackCheckoutFailed(pack.Sku, FailProviderRefused, "devnet canary is SKR-only");
                return PaymentResult.Failure(pack.Sku, currency, skrCanaryOnly);
            }

            if (_purchaseInFlight)
            {
                SetCommerceState(CommerceState.Delayed, "A purchase is already in progress. Do not pay again.");
                return PaymentResult.Failure(pack.Sku, currency, "Purchase already in progress.");
            }

            if (_vm.IsOwned(pack.Sku))
            {
                SetCommerceState(CommerceState.Fulfilled, $"{pack.Name} is already in your collection.");
                return PaymentResult.Failure(pack.Sku, currency, "Already owned.");
            }

            _purchaseInFlight = true;
            Render();
            try
            {
                // The USDC / SOL flow (§7.4): wallet must be connected first.
                if (!_wallet.IsConnected)
                {
                    SetCommerceState(CommerceState.OpeningWallet);
                    var account = await _wallet.Connect();
                    if (!account.IsValid)
                    {
                        FlowTrace.Warn("Store", $"Purchase '{pack.Sku}': wallet connect cancelled/failed — aborted (player NOT charged).");
                        SetCommerceState(CommerceState.Cancelled,
                            "Wallet did not respond. Check that it is installed, then retry.");
                        TrackCheckoutFailed(pack.Sku, FailCancelled, "wallet connect cancelled/failed");
                        return PaymentResult.Failure(pack.Sku, currency, "Wallet not connected.");
                    }
                }

#if MAINNET_CANARY_TEST
                if (isMainnetCanary &&
                    !string.Equals(_wallet.Account.Address, MainnetCanaryCatalog.OwnerWallet,
                        StringComparison.Ordinal))
                {
                    const string ownerOnly = "Mainnet Verification is restricted to the owner test wallet.";
                    SetCommerceState(CommerceState.Failed, ownerOnly);
                    return PaymentResult.Failure(pack.Sku, currency, ownerOnly);
                }
#endif

#if STORE_RAIL_LOCAL_TEST
                // One-time recovery of the owner's first successful Devnet canary. The transfer
                // finalized while the old client was frozen in scaled-time confirmation, before it
                // could persist the returned signature. Production builds do not compile this block.
                // /verify still re-checks the full finalized transaction contract and remains the
                // entitlement authority; this only restores the receipt the client failed to save.
                if (!PurchaseEntitlementVerifier.HasPending(pack.Sku) &&
                    string.Equals(pack.Sku, PurchaseGate.DevnetCanarySku, StringComparison.Ordinal) &&
                    string.Equals(_wallet.Account.Address,
                        "CHKKFkPGz8VZfjpsZjJTqfAUW7vMpdNkkqCVuCcZsfkC", StringComparison.Ordinal))
                {
                    const string recoveredSignature =
                        "5FA9ygfVAiDQKywjM7WaZADGhjA6QJCUhGdKgDGNCBKWhfuxtjpBRDpFnrQzSpAsx72HT9LvdT9vLn9NLZJyyGGX";
                    var recoveredPayment = PaymentResult.Success(pack.Sku, CurrencyKind.Skr,
                        pack.AmountFor(CurrencyKind.Skr), recoveredSignature);
                    PurchaseEntitlementVerifier.Remember(pack, recoveredPayment, _wallet);
                    FlowTrace.Warn("Store", "Recovered finalized Devnet canary receipt 5FA9...yyGGX; verifying without a second payment.");
                }
#endif

                // Ask the durable authority before a new charge. This is the reinstall/new-device
                // restore path: local PlayerPrefs may be empty while the server still owns proof.
                if (!PurchaseEntitlementVerifier.HasPending(pack.Sku))
                {
                    SetCommerceState(CommerceState.Verifying,
                        $"Checking {_wallet.NetworkLabel} for an existing entitlement. No new payment is being requested.");
                    var durable = await PurchaseEntitlementVerifier.ReconcileAsync(pack, _wallet);
                    if (durable.State == EntitlementVerificationState.Fulfilled)
                    {
                        var restored = PaymentResult.Success(pack.Sku, currency,
                            pack.AmountFor(currency), durable.TransactionSignature);
                        if (await RestoreFulfilledOwnershipAsync(pack, restored)) return restored;
                        return Indeterminate(restored,
                            "Your fulfilled purchase was found, but ownership restore is pending.");
                    }
                    if (durable.State == EntitlementVerificationState.Verified)
                    {
                        var restored = PaymentResult.Success(pack.Sku, currency,
                            pack.AmountFor(currency), durable.TransactionSignature);
                        if (await CompleteVerifiedPurchaseAsync(pack, restored)) return restored;
                        return Indeterminate(restored,
                            "Your purchase was found, but delivery is pending. Reopen the store to retry.");
                    }
                }

                // Recovery is checked before a new charge. A submitted signature survives process
                // death; reconnecting the paying wallet resumes verification instead of paying twice.
                if (PurchaseEntitlementVerifier.HasPending(pack.Sku))
                {
                    SetCommerceState(CommerceState.Delayed,
                        "Recovering the recorded payment. Do not pay again.");
                    var recovered = await PurchaseEntitlementVerifier.VerifyPendingAsync(pack, currency, _wallet);
                    if (recovered.State == EntitlementVerificationState.Fulfilled)
                    {
                        var restored = PaymentResult.Success(pack.Sku, currency,
                            pack.AmountFor(currency), recovered.TransactionSignature);
                        if (await RestoreFulfilledOwnershipAsync(pack, restored)) return restored;
                        return Indeterminate(restored,
                            "Your fulfilled purchase was found, but ownership restore is pending.");
                    }
                    if (recovered.State == EntitlementVerificationState.Verified)
                    {
                        var restored = PaymentResult.Success(pack.Sku, currency,
                            pack.AmountFor(currency), recovered.TransactionSignature);
                        if (await CompleteVerifiedPurchaseAsync(pack, restored)) return restored;
                        return Indeterminate(restored,
                            "Payment verified, but delivery is pending. Reopen the store to retry.");
                    }

                    // WO-1188 - REOPENING THE STORE ACTUALLY RESUMES. The give-up copy promises that
                    // reopening "picks it up from here", so this path owes the same polling loop the
                    // post-payment path runs, not one ask and another "come back later".
                    // ⛔ Still no wallet call and no transfer: the loop only re-reads the SAME stored
                    // signature this probe just returned.
                    if (recovered.State == EntitlementVerificationState.Pending &&
                        !string.IsNullOrEmpty(recovered.TransactionSignature))
                    {
                        var resumed = PaymentResult.Success(pack.Sku, currency,
                            pack.AmountFor(currency), recovered.TransactionSignature);
                        if (await AwaitGrantConfirmationAsync(pack, currency, resumed)) return resumed;
                        return Indeterminate(resumed, _commerceDetail);
                    }

                    string recoveryMessage = recovered.State == EntitlementVerificationState.Pending
                        ? "Payment found; waiting for final verification. No second charge was made."
                        : "The pending payment needs support review. No second charge was made.";
                    SetCommerceState(CommerceState.Delayed, recoveryMessage);
                    return PaymentResult.Failure(pack.Sku, currency, recoveryMessage);
                }

                // =============================================================
                //  THE SERVER QUOTES THE PRICE (WO-1158) - LAST THING BEFORE THE WALLET
                // -------------------------------------------------------------
                // ⛔ THE CLIENT DOES NO PRICE ARITHMETIC. It asks, it transfers exactly what it is
                // told, and it presents what the server quoted. Before this, the client resolved a
                // market rate and the backend checked the settled transfer against a constant of its
                // own - two opinions about a moving number, reconciled AFTER the money moved. The
                // moment the market shifted, the player paid and was granted nothing.
                //
                // ⛔ IT IS FETCHED HERE, NOT AT STORE OPEN, ON PURPOSE. The quote's clock starts the
                // instant it is issued and the wallet approval that follows is a HUMAN action with
                // no countdown. Every second between the quote and the prompt is a second of the
                // player's own expiry budget spent on our UI.
                //
                // The two CANARIES come back `pinned` with no rate and no expiry: their amount IS a
                // protocol constant (a proof-of-rail, not a sale), so nothing here reprices them.
                SetCommerceState(CommerceState.Verifying,
                    $"Asking for today's price for {pack.Name}. Nothing has been charged yet.");
                // Context only: the server treats this as a logged hint and owns both eligibility
                // and amount. No percentage or price arithmetic exists in the client.
                string quoteReason = _pendingShortfallMissing > 0 && pack.Impulse &&
                    string.Equals(pack.ImpulseResource, _pendingShortfallLabel,
                        StringComparison.OrdinalIgnoreCase)
                    ? "repair_shortfall" : null;
                var quoted = await PurchaseQuoteService.RequestQuoteAsync(pack, _wallet, quoteReason);
                if (!quoted.Ok)
                {
                    // ⛔ FAIL CLOSED. No quote means no sale - never a stale price, never the
                    // authored one. Charging a made-up number is worse than refusing to sell.
                    string refusal = string.IsNullOrEmpty(quoted.Error)
                        ? PurchaseQuoteService.QuoteUnavailableMessage : quoted.Error;
                    FlowTrace.Warn("Store", $"Purchase '{pack.Sku}' STOPPED before the wallet: no server quote " +
                                            $"- \"{refusal}\" (player NOT charged).");
                    SetCommerceState(CommerceState.Failed, refusal);
                    TrackCheckoutFailed(pack.Sku, FailProviderRefused, "no server quote");
                    return PaymentResult.Failure(pack.Sku, currency, refusal);
                }
                var quote = quoted.Quote;

                // ── THE CONFIRM STEP: the last screen where a player can still decline ──
                // ⛔ SO IT MUST BE UNAMBIGUOUS (WO-1158 §5). It states the EXACT SKR that will be
                // transferred, the approximate dollars behind it, and the rate + SOURCE the figure
                // came from. Which number carries the "~" is not a wording preference: the SKR is
                // exact (the quote pins it to the base unit) and the DOLLARS float, because the rate
                // moves. Every part of it is WORDS and DIGITS - the owner is red/green colourblind,
                // so no hue carries any of this and the greyscale capture is the acceptance test.
                string rateLine = quote.Pinned
                    ? "Fixed test amount - no market rate is used."
                    : $"at ${(quote.Rate ?? 0d):0.########} per SKR ({quote.RateSource}).";
                string discountLine = string.IsNullOrEmpty(quote.DiscountLabel)
                    ? string.Empty : $" {quote.DiscountLabel} applied.";
                string savingLine = string.IsNullOrEmpty(quote.UsdSavingLabel)
                    ? string.Empty : $"; {quote.UsdSavingLabel}";
                SetCommerceState(CommerceState.AwaitingApproval,
                    $"{pack.Name}: you will send exactly {quote.ExactSkrLabel} " +
                    $"({quote.UsdApproxLabel}{savingLine}) on {_wallet.NetworkLabel}.{discountLine} {rateLine} " +
                    "Human approval has no countdown.");

                var result = await _wallet.Pay(pack, currency);

                // A wallet-signed receipt exists before RPC transport completes. Persist it even
                // when submission response is ambiguous; any retry must reconcile, never pay again.
                // ⚠ THE QUOTE ID IS PART OF THE RECEIPT. /verify checks the chain against the quote
                // it ISSUED; a later retry that asked for a fresh quote would be checking a settled
                // transfer against a price nobody agreed to, with the money already gone.
                if (!string.IsNullOrEmpty(result.TxSignature))
                    PurchaseEntitlementVerifier.Remember(pack, result, _wallet, quote.QuoteId);

                if (result.Ok)
                {
#if false
                    // Payment confirmed -> the player IS charged. ApplyPackContents MUST land the
                    // entitlement; it self-reports if GameState is unavailable (paid-for content lost).
                    _vm.ApplyPackContents(pack);
                    FlowTrace.Step("Store", $"Purchase '{pack.Sku}' confirmed — tx {result.TxSignature}, contents applied.");
                    SetStatus($"{pack.Name} unlocked - tx {Shorten(result.TxSignature)}.");
                    PackPurchased?.Invoke(pack, result);

                    // WO2: analytics — purchase confirmed.
                    DeNelle.Core.Analytics.EventTracker.Track("purchase_completed", new
                    {
                        packId   = pack.Sku,
                        packName = pack.Name,
                        currency = currency.ToString(),
                        txSig    = result.TxSignature,
                    });
#endif
                    // Chain confirmation alone is not entitlement authority. Persist first so a
                    // crash cannot strand the charge, then require independent backend proof.
                    SetCommerceState(CommerceState.Submitted, $"Transaction {Shorten(result.TxSignature)}.");
                    // ⛔ WO-1188 - THE MONEY HAS MOVED, SO THE SCREEN STAYS. Everything below used to
                    // be ONE /verify call: if the first answer was not final the player was handed
                    // "reopen the store to resume" - a chore - at the exact moment they had just
                    // spent real money. The polling primitive existed and nothing polled with it.
                    if (!await AwaitGrantConfirmationAsync(pack, currency, result))
                        return Indeterminate(result, _commerceDetail);
                }
                else
                {
                    if (!string.IsNullOrEmpty(result.TxSignature))
                    {
                        SetCommerceState(CommerceState.Delayed,
                            $"Transaction {Shorten(result.TxSignature)} has an unknown submission outcome. " +
                            "It is recorded for reconciliation; do not pay again.");
                        return result;
                    }
                    FlowTrace.Fail("Store", $"Purchase '{pack.Sku}' ({currency}) FAILED: {result.Error}");

                    // ⛔ WO-1579 - ONE FAILURE HERE IS NOT AMBIGUOUS, AND TELLING THE PLAYER TO
                    // "RECONCILE" IT IS A LIE THAT COSTS TRUST. A wallet sign-leg timeout is raised
                    // before anything is submitted, so there is nothing to reconcile and nothing was
                    // charged - the honest sentence is the curated one the provider hands up. It is
                    // matched by IDENTITY against a const, never by parsing the error text, so raw
                    // exception wording can never reach the money screen (the pin
                    // StoreCommerceStateRegression holds). Every OTHER failure keeps the neutral
                    // reconcile wording, because it may have been thrown after the POST left.
                    bool nothingSubmitted = string.Equals(result.Error,
                        TargetedLocalAssociationScenario.SignTimeoutMessage, StringComparison.Ordinal);
                    if (nothingSubmitted)
                    {
                        // SetCommerceState IS the seam: it composes the Failed headline with this
                        // detail and writes the banner through SetStatus (:4338). A second explicit
                        // SetStatus here would CLOBBER that headline with the detail alone.
                        SetCommerceState(CommerceState.Failed,
                            TargetedLocalAssociationScenario.SignTimeoutMessage);
                    }
                    else
                    {
                        SetCommerceState(CommerceState.Failed,
                            "Reopen the store to reconcile before trying another payment.");
                    }
                    TrackCheckoutFailed(pack.Sku, FailError, result.Error);
                }
                return result;
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("Store",
                    $"Purchase '{pack.Sku}' ({currency}) THREW: {ex.GetType().Name}: {ex.Message} — outcome indeterminate; if a charge settled the entitlement may be lost.");
                SetCommerceState(CommerceState.Failed,
                    "Reopen the store to reconcile before trying another payment.");
                TrackCheckoutFailed(pack.Sku, FailError, ex.GetType().Name);
                return PaymentResult.Failure(pack.Sku, currency, ex.Message);
            }
            finally
            {
                _purchaseInFlight = false;
                Render();
                RefreshWalletMirror().Forget();
            }
        }

        private async UniTask<PaymentResult> PurchaseThroughProvider(PackDef pack, CurrencyKind legacyCurrency)
        {
            var provider = PaymentProviders.Current;
            if (!OwnsTheRail(provider))
                return PaymentResult.Failure(pack.Sku, legacyCurrency, "Payment provider unavailable.");
            if (_purchaseInFlight)
                return PaymentResult.Failure(pack.Sku, legacyCurrency, "Purchase already in progress.");
            if (_vm.IsOwned(pack.Sku))
                return PaymentResult.Failure(pack.Sku, legacyCurrency, "Already owned.");
            if (!provider.CanBuy(pack.Sku, out string reason))
            {
                SetCommerceState(CommerceState.Failed, reason);
                TrackCheckoutFailed(pack.Sku, FailProviderRefused, reason);
                return PaymentResult.Failure(pack.Sku, legacyCurrency, reason);
            }

            _purchaseInFlight = true;
            Render();
            try
            {
                var completion = new UniTaskCompletionSource<ProviderPurchaseResult>();
                provider.Purchase(pack.Sku, result => completion.TrySetResult(result));
                var result = await completion.Task;
                if (result.Pending)
                {
                    // WO-1318: the sentence names the rail that is actually holding the purchase. On
                    // Pi it must ALSO say what happens next, because the recovery is automatic
                    // (onIncompletePaymentFound settles it on the next launch) and a player told only
                    // "do not buy it again" would reasonably assume their money is gone.
                    string pending = provider.Channel == PaymentChannel.PiBrowser
                        ? "Pi is still settling this payment. Do not buy it again - reopen the game and " +
                          "it will finish automatically."
                        : "Google Play is processing this purchase. Do not buy it again.";
                    SetCommerceState(CommerceState.Delayed, pending);
                    return PaymentResult.Indeterminate(pack.Sku, legacyCurrency, 0d,
                        result.ProviderTransactionId, pending);
                }
                if (!result.Succeeded)
                {
                    SetCommerceState(CommerceState.Failed, result.Error);
                    // ProviderPurchaseResult carries no cancelled flag, so a user-dismissed sheet and
                    // a real failure both land here as "error"; the detail keeps the provider's text.
                    TrackCheckoutFailed(pack.Sku, FailError, result.Error);
                    return PaymentResult.Failure(pack.Sku, legacyCurrency, result.Error);
                }

                // Success is emitted only after authenticated server verification and durable
                // transaction recording. The local grant remains exactly-once by SKU ownership.
                //
                // ⛔ WO-1318 — THE PI RAIL HAS ALREADY GRANTED, AND CALLING THIS AGAIN WOULD DOUBLE IT.
                // PackStoreVM.ApplyPackContents is NOT idempotent for the ECONOMY half: it records
                // ownership once, but it routes wood/iron/food/crystals/coins through
                // EconomyService.GrantSpendable EVERY time it runs, so a second call on the same
                // payment silently doubles the resources. PiBrowserPaymentProvider settles through
                // PiGrantApplier.ApplyExactlyOnce (write-ahead journal keyed by the Pi paymentId)
                // BEFORE it reports success, because the same grant must also happen with no store
                // open at all — onIncompletePaymentFound can fire at sign-in. So on the Pi rail this
                // call is SKIPPED, and the ownership assertion below still guards delivery.
                // The Google Play path is untouched.
                if (provider.Channel != PaymentChannel.PiBrowser)
                    _vm.ApplyPackContents(pack);
                if (!_vm.IsOwned(pack.Sku))
                    return PaymentResult.Failure(pack.Sku, legacyCurrency,
                        "Purchase verified, but local delivery is still pending.");
                var payment = PaymentResult.Success(pack.Sku, legacyCurrency, 0d,
                    result.ProviderTransactionId);
                PackPurchased?.Invoke(pack, payment);
                SetCommerceState(CommerceState.Fulfilled, $"{pack.Name} received.");
                return payment;
            }
            finally
            {
                _purchaseInFlight = false;
                Render();
            }
        }

        private static PaymentResult Indeterminate(PaymentResult paid, string error)
        {
            paid.Ok = false;
            paid.Error = error;
            return paid;
        }

        // =====================================================================
        //  WO-1188 - THE PROCESSING SCREEN THAT STAYS UNTIL THE GRANT IS CONFIRMED
        // ---------------------------------------------------------------------
        //  Owner, 2026-08-25: "after the purchase is complete ... leave it on a processing screen
        //  until we keep calling back calling back calling back and it confirms that the redemption
        //  happened and then ... tell them X was received, and deposited, and close out gracefully."
        //
        //  ⛔ THE ASK IS RE-ASKED, THE PLAYER IS NEVER RE-CHARGED. This loop calls exactly one
        //  method - VerifyPendingAsync - which reads the ALREADY-PERSISTED signature + quote id
        //  written by Remember() before the loop started. There is no wallet call, no transfer, and
        //  no quote refresh anywhere below. One payment, one transfer.
        // =====================================================================

        /// <summary>
        /// Waits between polls. Fast early (a healthy /verify resolves on the first or second ask),
        /// slowing out so a server that is genuinely still writing is not hammered.
        /// <para>Seven attempts spaced 2/4/8/15/30/30 = <b>89 seconds</b> of foreground waiting, and
        /// that ceiling is not a taste call: the whole purchase runs inside
        /// <see cref="DeNelle.Core.UI.WorldHold"/> (WO-1149), whose stuck-hold watchdog force-releases
        /// the freeze at <c>StuckHoldSeconds = 180</c>. A loop allowed to run longer than that would
        /// unfreeze the world underneath its own modal. 89s leaves headroom for the quote + the
        /// human wallet approval that already happened before the first poll.</para>
        /// </summary>
        private static readonly float[] GrantPollBackoffSeconds = { 2f, 4f, 8f, 15f, 30f, 30f };

        /// <summary>Total asks, including the immediate one. = backoff steps + 1.</summary>
        private const int GrantPollAttempts = 7;

        /// <summary>
        /// Polls the durable authority until the entitlement is granted locally, the server rejects,
        /// or the ceiling is reached. Returns true only when the player's account actually holds the
        /// pack; every false path has already painted an honest, non-failure screen.
        ///
        /// <para>BACKGROUNDED / LOST FOCUS: <c>runInBackground</c> is 0 in ProjectSettings, so Unity
        /// stops the player loop when the app leaves the foreground. This loop is a PlayerLoop
        /// await, so it SUSPENDS there - no timer ticks, no request fires, and no attempt is spent.
        /// On return to the foreground it resumes on the same attempt index. If the OS kills the app
        /// outright, the pending marker written by <see cref="PurchaseEntitlementVerifier.Remember"/>
        /// survives, so the next store open reopens onto the SAME signature and resumes verification
        /// instead of inviting a second charge. In neither case is anything re-submitted.</para>
        /// </summary>
        private async UniTask<bool> AwaitGrantConfirmationAsync(
            PackDef pack, CurrencyKind currency, PaymentResult payment)
        {
            using var _ = FlowTrace.Enter("Store", $"AwaitGrantConfirmation '{pack?.Sku ?? "<null>"}'");
            string shortSig = Shorten(payment.TxSignature);
            string reference = string.Empty;
            string stage = string.Empty;
            bool settledButUnrecorded = false;

            for (int attempt = 1; ; attempt++)
            {
                // Repainting Verifying on EVERY attempt also resets _commerceStateSince, which is
                // what keeps Update()'s 60s "confirmation delayed" nudge from firing over the top of
                // a loop that is still actively asking. The nudge is for a stalled flow, not this one.
                SetCommerceState(CommerceState.Verifying,
                    BuildProcessingDetail(shortSig, attempt, settledButUnrecorded, reference));

                var verified = await PurchaseEntitlementVerifier.VerifyPendingAsync(pack, currency, _wallet);

                if (verified.State == EntitlementVerificationState.Fulfilled)
                {
                    if (await RestoreFulfilledOwnershipAsync(pack, payment)) return true;
                }
                else if (verified.State == EntitlementVerificationState.Verified)
                {
                    if (await CompleteVerifiedPurchaseAsync(pack, payment)) return true;
                }
                else if (verified.State == EntitlementVerificationState.Rejected)
                {
                    // A 4xx from /verify is the server saying this signature is not a payment for
                    // this SKU/wallet/network. That is the ONE outcome the loop cannot resolve, so
                    // it stops asking. Still never the word "failed" - money may have moved.
                    const string rejected =
                        "Payment recorded but could not be verified. Contact support with the transaction receipt.";
                    FlowTrace.Warn("Store", $"Purchase '{pack.Sku}' tx {payment.TxSignature}: {rejected}");
                    SetCommerceState(CommerceState.Delayed, $"Transaction {shortSig}. {rejected}");
                    return false;
                }

                // Pending, Unavailable, or a local delivery step that has not taken yet - all
                // retryable. record_failed (503) lands here as Pending and is EXACTLY the case this
                // loop was built for: the transfer settled, the server's own write did not, and a
                // later ask resolves it with no player action at all.
                if (!string.IsNullOrEmpty(verified.Reference))
                {
                    reference = verified.Reference;
                    stage = verified.Stage ?? string.Empty;
                    settledButUnrecorded = true;
                }

                if (attempt >= GrantPollAttempts)
                {
                    string giveUp = BuildUnfinishedDetail(shortSig, reference);
                    FlowTrace.Warn("Store",
                        $"Purchase '{pack.Sku}' tx {payment.TxSignature}: grant NOT confirmed after " +
                        $"{GrantPollAttempts} polls (settledButUnrecorded={settledButUnrecorded} " +
                        $"stage={(string.IsNullOrEmpty(stage) ? "-" : stage)} " +
                        $"ref={(string.IsNullOrEmpty(reference) ? "-" : reference)}). " +
                        "Payment stands recorded; the pending marker is deliberately NOT cleared.");
                    SetCommerceState(CommerceState.Delayed, giveUp);
                    return false;
                }

                float wait = GrantPollBackoffSeconds[
                    Mathf.Min(attempt - 1, GrantPollBackoffSeconds.Length - 1)];
                // ignoreTimeScale: the WorldHold freeze has Time.timeScale at 0 for the whole
                // purchase, so a scaled delay here would never elapse and the loop would hang.
                await UniTask.Delay(TimeSpan.FromSeconds(wait), ignoreTimeScale: true);
            }
        }

        /// <summary>The words on the processing screen while the loop is still asking. ⛔ It never
        /// tells the player to reopen the store - that instruction is what this ticket removed.</summary>
        private static string BuildProcessingDetail(string shortSig, int attempt,
                                                    bool settledButUnrecorded, string reference)
        {
            var sb = new StringBuilder();
            sb.Append("Transaction ").Append(shortSig).Append(". Payment sent - confirming your delivery (check ")
              .Append(attempt).Append(" of ").Append(GrantPollAttempts).Append("). ");
            if (settledButUnrecorded)
            {
                sb.Append("Your payment has settled and the server is still recording it; ")
                  .Append("this retries on its own");
                if (!string.IsNullOrEmpty(reference)) sb.Append(" (reference ").Append(reference).Append(')');
                sb.Append(". ");
            }
            sb.Append("Stay here - nothing further will be charged.");
            return sb.ToString();
        }

        /// <summary>The bounded, honest give-up. ⛔ The money moved, so this is NOT a failure screen:
        /// no "failed", no bare spinner, and the support reference is surfaced when one exists.</summary>
        private static string BuildUnfinishedDetail(string shortSig, string reference)
        {
            var sb = new StringBuilder();
            sb.Append("Transaction ").Append(shortSig)
              .Append(". Your payment is recorded and nothing further will be charged. ")
              .Append("Delivery has not finished yet - it completes on its own, and reopening the store ")
              .Append("picks it up from here.");
            if (!string.IsNullOrEmpty(reference))
                sb.Append(" Support reference: ").Append(reference).Append('.');
            return sb.ToString();
        }

        // =====================================================================
        //  WO-1188 - MEASURED DELIVERY: report what ARRIVED, never what was asked for
        // ---------------------------------------------------------------------
        //  ⛔ THE CONFIRMATION READS THE WALLET, NOT THE PACK DEFINITION. WO-978 is the ticket where
        //  four economy callers logged the amount REQUESTED as though it were the amount CREDITED;
        //  the one screen where the player is checking whether they got what they paid for is the
        //  worst possible place to repeat that. Balances are snapshotted immediately BEFORE
        //  ApplyPackContents and read again after, and the DELTA is what gets printed. The pack's
        //  authored numbers are used for one purpose only: deciding whether the delta looks short.
        //
        //  ⭐ OWNER RULING 2026-08-25: a PURCHASED grant OVERFLOWS the storage cap - the player paid
        //  for it, they get all of it, and it lives in the ordinary balance above the cap (no escrow,
        //  no held value). TownBankCapacity.IsClampable already exempts PurchasedOrPromised, so this
        //  screen has NO "storage full, you only got some" copy: a purchase that under-delivers is an
        //  ANOMALY to be reported and traced, not a storage disclosure. What the screen DOES say, in
        //  words, is when a resource has landed ABOVE its cap - because earned income stops adding to
        //  that resource until the player spends back under, and they are owed that plainly.
        // =====================================================================

        private readonly struct EconomySnapshot
        {
            public readonly bool Valid;
            private readonly int _wood, _iron, _food, _crystals, _coins;

            private EconomySnapshot(int wood, int iron, int food, int crystals, int coins)
            {
                _wood = wood; _iron = iron; _food = food; _crystals = crystals; _coins = coins;
                Valid = true;
            }

            /// <summary>Reads the ONE authoritative wallet total per resource (TownBankCapacity.CurrentOf
            /// -> GameState, WO-842). Nothing here derives or predicts a balance.</summary>
            public static EconomySnapshot Capture() => new EconomySnapshot(
                Bank.CurrentOf(BankRes.Wood), Bank.CurrentOf(BankRes.Iron), Bank.CurrentOf(BankRes.Food),
                Bank.CurrentOf(BankRes.Crystals), Bank.CurrentOf(BankRes.Coins));

            public int Of(BankRes r)
            {
                switch (r)
                {
                    case BankRes.Wood: return _wood;
                    case BankRes.Iron: return _iron;
                    case BankRes.Food: return _food;
                    case BankRes.Crystals: return _crystals;
                    case BankRes.Coins: return _coins;
                }
                return 0;
            }
        }

        private static readonly BankRes[] ReceiptResources =
        {
            BankRes.Wood, BankRes.Iron, BankRes.Food, BankRes.Crystals, BankRes.Coins,
        };

        private static int AdvertisedAmount(PackDef pack, BankRes r)
        {
            var econ = pack != null && pack.Contents != null ? pack.Contents.Economy : null;
            if (econ == null) return 0;
            switch (r)
            {
                case BankRes.Wood: return Mathf.Max(0, econ.Wood);
                case BankRes.Iron: return Mathf.Max(0, econ.Iron);
                case BankRes.Food: return Mathf.Max(0, econ.Food);
                case BankRes.Crystals: return Mathf.Max(0, econ.Crystals);
                case BankRes.Coins: return Mathf.Max(0, econ.Coins);
            }
            return 0;
        }

        /// <summary>
        /// Builds the receipt body from MEASURED balance deltas plus MEASURED ownership.
        /// <para>Every number printed here was read out of the wallet after the grant. The only thing
        /// taken from the pack definition is the comparison figure used to notice an under-delivery,
        /// and the names of items this screen explicitly says it did NOT count.</para>
        /// </summary>
        private string DescribeMeasuredDelivery(PackDef pack, EconomySnapshot before, string txSignature)
        {
            var lines = new List<string>();
            if (!before.Valid)
            {
                // The replay path: the grant landed in an earlier run, so there is no delta to read
                // and the screen must not invent one from the pack card.
                lines.Add("This purchase was already delivered to your account.");
            }
            else
            {
                var after = EconomySnapshot.Capture();
                var deposited = new StringBuilder();
                var notes = new List<string>();

                foreach (var r in ReceiptResources)
                {
                    int advertised = AdvertisedAmount(pack, r);
                    int credited = Mathf.Max(0, after.Of(r) - before.Of(r));
                    if (advertised <= 0 && credited <= 0) continue;

                    if (credited > 0)
                    {
                        if (deposited.Length > 0) deposited.Append(", ");
                        deposited.Append(credited.ToString("N0")).Append(' ')
                                 .Append(Bank.DisplayName(r).ToLowerInvariant());
                    }

                    if (credited < advertised)
                    {
                        // ⛔ NOT a storage message. A purchased grant is never clamped, so a short
                        // delta means the grant seam itself under-delivered - loud, and said plainly.
                        FlowTrace.Fail("Store",
                            $"MEASURED SHORTFALL on paid pack '{pack?.Sku}': {Bank.WordOf(r)} credited " +
                            $"{credited} of {advertised} (before={before.Of(r)} after={after.Of(r)}). " +
                            "A purchased grant is exempt from the cap, so this is a grant-path defect, " +
                            "not storage. tx " + txSignature);
                        notes.Add($"{Bank.DisplayName(r)}: {credited:N0} of {advertised:N0} arrived so far. " +
                                  "The rest stays recorded against this payment - contact support with the " +
                                  "transaction id below if it does not appear.");
                    }
                    else if (Bank.IsCapped(r))
                    {
                        // Owner ruling 2026-08-25: a purchase may legitimately push a resource ABOVE
                        // its cap. Say so in WORDS - the consequence (earned income stops adding)
                        // belongs to the player, not to a colour or an icon.
                        int max = Bank.MaxOf(r);
                        if (after.Of(r) > max)
                            notes.Add($"{Bank.DisplayName(r)} is above storage ({after.Of(r):N0} of " +
                                      $"{max:N0}). All of it is yours to spend; harvesting and rewards " +
                                      $"will not add more {Bank.DisplayName(r).ToLowerInvariant()} until " +
                                      "you are back under.");
                    }
                }

                if (deposited.Length > 0) lines.Add("Deposited: " + deposited + ".");
                lines.AddRange(notes);
            }

            // Cosmetics are MEASURED too - the wardrobe is asked whether it owns the sku, and one
            // that did not land is traced and simply not claimed.
            if (pack != null && pack.Contents != null && pack.Contents.Cosmetics != null)
            {
                var unlocked = new StringBuilder();
                foreach (string sku in pack.Contents.Cosmetics)
                {
                    if (string.IsNullOrWhiteSpace(sku)) continue;
                    if (_vm != null && _vm.IsOwned(sku))
                    {
                        if (unlocked.Length > 0) unlocked.Append(", ");
                        unlocked.Append(sku);
                    }
                    else
                    {
                        FlowTrace.Warn("Store",
                            $"receipt for '{pack.Sku}': cosmetic '{sku}' is NOT owned after the grant - " +
                            "not claimed on the confirmation. tx " + txSignature);
                    }
                }
                if (unlocked.Length > 0) lines.Add("Unlocked: " + unlocked + ".");
            }

            // Convenience tokens have no read seam this View may use, so the screen states exactly
            // that rather than asserting a delivery it did not measure.
            if (pack != null && pack.Contents != null && pack.Contents.Convenience != null)
            {
                var items = new StringBuilder();
                foreach (var item in pack.Contents.Convenience)
                {
                    if (item == null || item.Count <= 0 || string.IsNullOrWhiteSpace(item.Kind)) continue;
                    if (items.Length > 0) items.Append(", ");
                    items.Append(item.Count).Append("x ")
                         .Append(item.Kind.Replace('-', ' ').Replace('_', ' '));
                }
                if (items.Length > 0)
                    lines.Add("Items recorded with this entitlement (not counted on this screen): "
                              + items + ".");
            }

            if (lines.Count == 0) lines.Add("Ownership recorded on your account.");
            return string.Join("\n", lines);
        }

        private async UniTask<bool> RestoreFulfilledOwnershipAsync(PackDef pack, PaymentResult payment)
        {
            if (pack == null || string.IsNullOrEmpty(payment.TxSignature)) return false;
            // The server says consumables were already delivered. Restore only durable ownership;
            // replaying economy/convenience here would make reinstall an infinite-grant exploit.
            _vm.RestoreFulfilledOwnership(pack);
            if (!_vm.IsOwned(pack.Sku)) return false;
            PurchaseGate.TryClaimGrant(payment.TxSignature);
            await PurchaseEntitlementVerifier.MarkFulfilledAsync(
                pack.Sku, payment.TxSignature, _wallet);
            SetCommerceState(CommerceState.Fulfilled,
                $"{pack.Name} ownership restored. Transaction {Shorten(payment.TxSignature)}.");
            return true;
        }

        /// <summary>Exactly-once local fulfilment after the backend created a durable entitlement.</summary>
        private async UniTask<bool> CompleteVerifiedPurchaseAsync(PackDef pack, PaymentResult payment)
        {
            if (pack == null || string.IsNullOrEmpty(payment.TxSignature)) return false;

            if (!PurchaseGate.TryClaimGrant(payment.TxSignature))
            {
                if (_vm.IsOwned(pack.Sku))
                {
                    bool durablyFulfilled = await PurchaseEntitlementVerifier.MarkFulfilledAsync(
                        pack.Sku, payment.TxSignature, _wallet);
                    if (!durablyFulfilled) return false;
                    // Already granted in an earlier pass: there is no before/after to measure, and
                    // an unmeasured receipt says so rather than reprinting the pack card.
                    ShowFulfillmentReceipt(pack, payment, default(EconomySnapshot));
                    return true;
                }
                PurchaseGate.ReportGrantFailed(payment.TxSignature,
                    $"ledger claimed but pack '{pack.Sku}' is not owned");
                return false;
            }

            SetCommerceState(CommerceState.Delivering,
                $"Transaction {Shorten(payment.TxSignature)}.");
            // ⛔ WO-1188 - THE BASELINE IS TAKEN ON THE LINE BEFORE THE GRANT, ON PURPOSE. It is the
            // only moment at which "what this purchase credited" is measurable at all; read it any
            // later and the confirmation is back to quoting the pack card.
            var beforeGrant = EconomySnapshot.Capture();
            _vm.ApplyPackContents(pack);
            if (!_vm.IsOwned(pack.Sku))
            {
                PurchaseGate.ReportGrantFailed(payment.TxSignature,
                    $"pack '{pack.Sku}' was not owned after ApplyPackContents");
                return false;
            }

            FlowTrace.Step("Store", $"Purchase '{pack.Sku}' backend-verified; tx {payment.TxSignature}, contents applied once.");
            bool durableFulfillmentSucceeded = await PurchaseEntitlementVerifier.MarkFulfilledAsync(
                pack.Sku, payment.TxSignature, _wallet);
            if (!durableFulfillmentSucceeded)
            {
                PurchaseGate.ReportGrantFailed(payment.TxSignature,
                    $"pack '{pack.Sku}' was locally owned but durable fulfillment did not acknowledge");
                return false;
            }
            ShowFulfillmentReceipt(pack, payment, beforeGrant);
            PackPurchased?.Invoke(pack, payment);
            DeNelle.Core.Analytics.EventTracker.Track("purchase_completed", new
            {
                packId = pack.Sku,
                packName = pack.Name,
                currency = payment.Currency.ToString(),
                txSig = payment.TxSignature,
                authority = "server_verified_entitlement"
            });
            return true;
        }

        /// <summary>
        /// The close-out. ⛔ <paramref name="before"/> is the wallet snapshot taken immediately
        /// before the grant; the body is built from the MEASURED delta against it, never from
        /// <c>DescribeGrantedContents(pack)</c> - which is the pack's advertised inventory and is now
        /// used only by diagnostics. An invalid snapshot means "no delta exists to measure", and the
        /// copy says that instead of guessing.
        /// </summary>
        private void ShowFulfillmentReceipt(PackDef pack, PaymentResult payment, EconomySnapshot before)
        {
            string receipt = $"{pack.Name} received\n" +
                             DescribeMeasuredDelivery(pack, before, payment.TxSignature);
            FlowTrace.Step("Store",
                $"receipt for '{pack.Sku}' (MEASURED, not advertised): {receipt.Replace('\n', ' ')} " +
                $"| pack card would have said: {DescribeGrantedContents(pack)}");
            SetCommerceState(CommerceState.Fulfilled,
                $"{receipt}\nTransaction {Shorten(payment.TxSignature)}.");
            // Shared HUD feedback. Wallet cannot reference Village's world-space resource popup;
            // ApplyPackContents already raises the established resource change notifications.
            // The measured receipt can run to several lines (a deposit line, an over-storage note,
            // an items line), where the advertised one-liner never did. The card grows with the
            // content instead of clipping the sentence that explains the money.
            int lineCount = 1;
            for (int i = 0; i < receipt.Length; i++) if (receipt[i] == '\n') lineCount++;
            float cardHeight = Mathf.Clamp(72f + lineCount * 30f, 132f, 300f);
            ElarionUiKit.ShowToast(receipt, ElarionUiKit.ToastTone.Confirm,
                lifeSeconds: lineCount > 2 ? 8f : 5f, cardWidth: 760f, cardHeight: cardHeight);
        }

        /// <summary>
        /// The remedy behind the "Connect Wallet" face on a pack the owner's price rule has gated
        /// (WO-1121). It CHARGES NOTHING — it only runs the connect handshake and re-renders.
        ///
        /// <para>Deliberately NOT "connect, then immediately buy": the player tapped a button that
        /// said Connect Wallet, and auto-charging them for a $49.99 pack off that tap would be a
        /// purchase they never confirmed.</para>
        /// </summary>
        private async UniTaskVoid ConnectForWalletGate(string reason)
        {
            using var _ = FlowTrace.Enter("Store", "ConnectForWalletGate");
            SetCommerceState(CommerceState.OpeningWallet, reason);
            try
            {
                if (_wallet != null && !_wallet.IsConnected)
                {
                    var account = await _wallet.Connect();
                    if (!account.IsValid)
                    {
                        FlowTrace.Warn("Store", "wallet-gate connect cancelled/failed — pack stays gated (nothing charged).");
                        SetCommerceState(CommerceState.Cancelled,
                            "Wallet did not respond. Check that it is installed, then retry. No payment was requested.");
                        return;
                    }
                }

                if (PurchaseGate.HasDurableIdentity)
                    FlowTrace.Step("Store", "wallet-gate satisfied — the higher tiers are now buyable on this save.");
                else
                    SetCommerceState(CommerceState.Failed, reason);
            }
            catch (Exception ex)
            {
                // No silent catch (§12) — a swallowed throw here looks to the player like a button
                // that does nothing, on the one screen where that reads as dishonest.
                FlowTrace.Fail("Store", $"ConnectForWalletGate THREW: {ex.GetType().Name}: {ex.Message} — " +
                                        "nothing was charged; the pack stays gated.");
                SetCommerceState(CommerceState.Failed,
                    "Wallet authorization failed before any payment request. Retry authorization when ready.");
            }
            finally
            {
                Render();
                RefreshWalletMirror().Forget();
            }
        }

        private void SetStatus(string message)
        {
            if (_statusBanner != null) _statusBanner.text = message;
            else FlowTrace.Warn("Store", $"SetStatus (no banner element): {message}");
        }

        private void SetCommerceState(CommerceState state, string detail = null)
        {
            _commerceState = state;
            _commerceDetail = detail ?? string.Empty;
            _commerceStateSince = Time.realtimeSinceStartup;
            RenderCommerceStatus();
        }

        private void RenderCommerceStatus(string temporaryDetail = null)
        {
            string key;
            switch (_commerceState)
            {
                case CommerceState.OpeningWallet: key = StoreStrings.KeyCommerceOpeningWallet; break;
                case CommerceState.AwaitingApproval: key = StoreStrings.KeyCommerceAwaitingApproval; break;
                case CommerceState.Submitted: key = StoreStrings.KeyCommerceSubmitted; break;
                case CommerceState.Verifying: key = StoreStrings.KeyCommerceVerifying; break;
                case CommerceState.Delivering: key = StoreStrings.KeyCommerceDelivering; break;
                case CommerceState.Fulfilled: key = StoreStrings.KeyCommerceFulfilled; break;
                case CommerceState.Cancelled: key = StoreStrings.KeyCommerceCancelled; break;
                case CommerceState.Failed: key = StoreStrings.KeyCommerceFailed; break;
                case CommerceState.Delayed: key = StoreStrings.KeyCommerceDelayed; break;
                default: key = StoreStrings.KeyCommerceReady; break;
            }

            string headline = _commerceState == CommerceState.Verifying
                ? StoreStrings.Format(key, _wallet != null ? _wallet.NetworkLabel : "network")
                : StoreStrings.Get(key);
            string detail = temporaryDetail ?? _commerceDetail;
            SetStatus(string.IsNullOrEmpty(detail) ? headline : headline + "\n" + detail);
        }

        private void RestorePendingPresentation()
        {
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !PurchaseEntitlementVerifier.HasPending(pack.Sku)) continue;
                SetCommerceState(CommerceState.Delayed,
                    $"{pack.Name} has a recorded payment. Reopen this offer to reconcile; do not pay again.");
                return;
            }
            if (!_purchaseInFlight && _commerceState == CommerceState.Ready)
                RenderCommerceStatus();
        }

        private static string Shorten(string signature)
        {
            if (string.IsNullOrEmpty(signature) || signature.Length < 8) return signature ?? string.Empty;
            return $"{signature.Substring(0, 4)}...{signature.Substring(signature.Length - 4)}";
        }

        // =====================================================================
        //  uGUI helpers (same shapes as LeaderboardPanel / CosmeticShopPanel)
        // =====================================================================

        /// <summary>
        /// A layout region: anchors PLUS an offset pair in REFERENCE PIXELS.
        ///
        /// <para>⛔ THE OFFSETS ARE THE POINT, AND THEY REPLACE THE OLD FRACTION-ONLY HELPER.
        /// A rect authored as "0.135 to 0.855 of the parent" silently re-scales with whatever the
        /// parent turned out to be — and on this screen the parent turned out to be 978 units tall,
        /// not the 1920 the fractions were eyeballed against (UI-001 §0.4). Every band and column
        /// here therefore states its size as a NUMBER and lets the remaining axis stretch, which is
        /// what makes 100 + 746 + 132 = 978 hold at 1920x1080, 2340x1080 and the Seeker's 2670x1200
        /// alike.</para>
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

        /// <summary>
        /// Inset the screen host by the device's safe area, in reference px.
        ///
        /// <para>⛔ THIS IS NEW WORK, NOT AN INHERITED GUARANTEE (UI-001 §0.9): nothing in the kit
        /// reads <c>Screen.safeArea</c>, so a full-bleed store would otherwise run its Close and its
        /// price rail under a cutout or a gesture bar. It is applied ONCE, on the host every band
        /// hangs from, so no individual band can forget it — and it inset the HOST rather than
        /// shrinking the panel, because the owner's ruling is that the surface IS the screen.</para>
        ///
        /// <para>A device with no insets (and every batchmode capture, where <c>Screen.safeArea</c>
        /// is the full rect) takes the early return and the geometry is untouched — which is why the
        /// captured pngs measure the same rects the phone gets, minus its notch.</para>
        /// </summary>
        /// <returns>The applied inset in REFERENCE px as (left, right, bottom, top). WO-1162: the
        /// responsive composition has to subtract this before it can resolve a body width, and a
        /// caller that re-derived it from Screen.safeArea itself would be the second copy of a
        /// measurement this method already owns.</returns>
        private static Vector4 ApplySafeArea(RectTransform rt)
        {
            if (rt == null) return Vector4.zero;
            try
            {
                float w = Mathf.Max(1f, Screen.width);
                float h = Mathf.Max(1f, Screen.height);
                Rect safe = Screen.safeArea;
                if (safe.width <= 0f || safe.height <= 0f) return Vector4.zero;

                float left   = Mathf.Clamp01(safe.xMin / w);
                float right  = Mathf.Clamp01((w - safe.xMax) / w);
                float bottom = Mathf.Clamp01(safe.yMin / h);
                float top    = Mathf.Clamp01((h - safe.yMax) / h);
                if (left <= 0f && right <= 0f && bottom <= 0f && top <= 0f) return Vector4.zero;

                // ⚠ THE REFERENCE BOX IS THE LIVE ONE, NOT THE 2120x978 LITERAL. The inset is a
                // fraction of the SURFACE, converted into the reference units this canvas actually
                // resolves to; on a 4:3 landscape those are ~1663x1247, and converting through
                // 2120x978 would have over-inset the width and under-inset the height.
                float refH = SurfaceReferenceHeightPx(rt);
                float refW = SurfaceReferenceWidthPx(refH);
                rt.offsetMin = new Vector2(left * refW, bottom * refH);
                rt.offsetMax = new Vector2(-right * refW, -top * refH);

                FlowTrace.Step("Store", string.Format(
                    "ApplySafeArea: inset L{0:F0} R{1:F0} B{2:F0} T{3:F0} ref px from Screen.safeArea " +
                    "{4} on a {5}x{6} surface (reference box {7:F0}x{8:F0}).",
                    left * refW, right * refW, bottom * refH, top * refH, safe, w, h, refW, refH));

                return new Vector4(left * refW, right * refW, bottom * refH, top * refH);
            }
            catch (Exception e)
            {
                // Never a silent catch (CLAUDE.md §12.2): an un-inset store is a real defect on a
                // notched device, so it is reported rather than swallowed — but it must not stop the
                // store from opening.
                FlowTrace.Warn("Store", "ApplySafeArea threw (" + e.GetType().Name +
                                        ") - the store draws full-bleed with NO safe-area inset.");
            }
            return Vector4.zero;
        }

        /// <summary>
        /// The canvas height this store's fraction anchors really resolve against, in REFERENCE px.
        /// <para>⛔ NOT <c>NightMarketLayout.UsableHeightPx</c>. That 978 is the value the reference
        /// canvas resolves to at 2340x1080 ONLY; a 4:3 landscape resolves to ~1247 and a 2670x1200
        /// Seeker to ~966. Reading it off the kit means the composition is resolved against the
        /// surface the player is holding rather than the one the literals were measured on.</para>
        /// </summary>
        private static float SurfaceReferenceHeightPx(Transform under)
        {
            float h = ElarionUiKit.PostScaleCanvasHeight(under);
            return h > 100f ? h : NightMarketLayout.UsableHeightPx;
        }

        /// <summary>The matching reference WIDTH. The canvas preserves the surface aspect, so the
        /// width is the height times it — one derivation, never a second literal.</summary>
        private static float SurfaceReferenceWidthPx(float referenceHeightPx)
        {
            float sw = Mathf.Max(1f, ElarionUiKit.SurfaceWidth);
            float sh = Mathf.Max(1f, ElarionUiKit.SurfaceHeight);
            float w = referenceHeightPx * (sw / sh);
            return w > 100f ? w : NightMarketLayout.UsableWidthPx;
        }

        /// <summary>
        /// Fit a block of copy inside the rect it was authored in: auto-size DOWN toward
        /// <see cref="ElarionUi.FontFloorMobile"/> and only then let TMP truncate.
        /// <para>⛔ NEVER A RAW OVERFLOW. Unfitted copy draws OUTSIDE its rect and onto whatever is
        /// next to it, which AuditGeometry reports and a player simply reads as a broken screen.
        /// Returns the same label so it can wrap a MakeText call inline.</para>
        /// </summary>
        private static TextMeshProUGUI FitInto(TextMeshProUGUI label, float maxSize)
        {
            if (label == null) return null;
            ElarionUiKit.FitBlock(label, ElarionUi.FontFloorMobile, maxSize);
            return label;
        }

        /// <summary>
        /// The obsidian ground (§R6): one soft radial bloom over the panel's own near-black fill.
        /// <para>⛔ SPRITE TWO OF THE TWO-SPRITE BILL, TINTED — not a third texture, not a particle,
        /// and not a VFX loop slot. It is raycast-off and sits behind everything, so it can neither
        /// eat a tap nor be reported by any geometry rule; if the sprite fails to build the screen
        /// simply keeps its flat fill.</para>
        /// </summary>
        private static void BuildGround(Transform host)
        {
            var sprite = ElarionUiKit.RadialGlowSprite;
            if (host == null || sprite == null) return;
            var go = new GameObject("Ground", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(-0.10f, -0.20f);
            rt.anchorMax = new Vector2(1.10f, 1.25f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = new Color(0.22f, 0.15f, 0.045f, 0.34f); // restrained antique-gold warmth
            img.raycastTarget = false;
            go.transform.SetAsFirstSibling();
        }

        /// <summary>The same colour at a stated alpha — named so a translucency is never a magic
        /// literal buried in a Plate call.</summary>
        private static Color Translucent(Color c, float alpha)
            => new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));

        /// <summary>A flat, non-interactive colour plate filling its parent. Never eats a tap.</summary>
        private static Image Plate(Transform parent, Color color)
        {
            if (parent == null) return null;
            var go = new GameObject("plate", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            go.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>
        /// A dark ground drawn UNDER an existing label, sized from that label's own anchors plus a
        /// stated pad. Never eats a tap.
        ///
        /// <para>⛔ IT TAKES THE LABEL, NOT A RECT, AND THAT IS THE WHOLE POINT. The alternative —
        /// typing the plate's anchors beside the text's — is two copies of one number, which drifts
        /// the first time either is nudged and leaves a chip half on its ground. Same duplicated-state
        /// failure class as CLAUDE.md §2's stale WO block. Derive, never re-type.</para>
        ///
        /// <para>⛔ AND IT IS A LUMINANCE FIX, NOT A COLOUR ONE. The owner is red/green colourblind
        /// (CLAUDE.md §7): text made "readable" by re-tinting it is not readable to her. What makes
        /// a label legible over busy art is a DARK GROUND behind it, which is a brightness contrast
        /// and survives any colour vision. Owner ruling 2026-09-03 on the wallet chip: *"white where
        /// it's over top of everything else ... you can't read it"*.</para>
        /// </summary>
        /// <summary>
        /// WO-1636 — re-seat a canon CTA's label band in reference px.
        ///
        /// <para>⛔ THIS DOES NOT EDIT THE KIT, AND IT MUST NOT. ElarionUiKit.BuildObsidianButton
        /// gives every constructed button a label at x .04-.96 — .92 of the face, which is right for
        /// a button whose width is itself a fraction of something. The canon CTA is not that: it is
        /// exactly <see cref="ElarionUiKit.CanonCtaWidth"/> px wide, so .04 of it is 14.4 px of pad
        /// on each side and the caption is left 331 px. "CONNECT WALLET" measured 12 of 13 printable
        /// glyphs in 308 px (Builds/wave3-capture2.glyph-findings.txt:58) and needs ~332 with its
        /// ellipsis, which 331 does not clear. A px pad leaves 344 and clears it by ~3.5%.</para>
        ///
        /// <para>The pad is the same 8 px the utility rows use on their right edge, so a CTA caption
        /// and a rail caption breathe alike; the kit's own label anchoring is untouched for every
        /// button that is NOT authored at a px width.</para>
        /// </summary>
        private static void SeatCtaLabelInPx(TMP_Text label)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(NightMarketLayout.CtaLabelPadPx, 0f);
            rt.offsetMax = new Vector2(-NightMarketLayout.CtaLabelPadPx, 0f);
        }

        private static Image PlateBehind(TextMeshProUGUI label, Color color, float padX, float padY)
        {
            if (label == null) return null;
            var lrt = label.rectTransform;
            var parent = lrt.parent;
            if (parent == null) return null;

            var go = new GameObject("plate-behind", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(lrt.anchorMin.x - padX, lrt.anchorMin.y - padY);
            rt.anchorMax = new Vector2(lrt.anchorMax.x + padX, lrt.anchorMax.y + padY);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            // Immediately BELOW the label in the sibling order, not first in the band: first-sibling
            // would put it under every other header child too, and this ground belongs to one label.
            go.transform.SetSiblingIndex(Mathf.Max(0, lrt.GetSiblingIndex()));
            return img;
        }

        /// <summary>The card gem. DECORATION: it never carries a state or a meaning by itself.</summary>
        private static void Orb(Transform parent, Color tint)
            => Orb(parent, tint, new Vector2(0.05f, 0.68f), new Vector2(0.20f, 0.94f));

        private static void Orb(Transform parent, Color tint, Vector2 min, Vector2 max)
        {
            if (parent == null) return;
            var go = new GameObject("orb", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            var sprite = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, "slot_item");
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            img.color = new Color(tint.r, tint.g, tint.b, 0.85f);
            img.raycastTarget = false;
        }

        private static bool AddArt(Transform parent, string assetName, Vector2 min, Vector2 max)
        {
            if (parent == null || string.IsNullOrWhiteSpace(assetName)) return false;
            var sprite = NightMarketArt.Load(assetName);
            if (sprite == null) return false;
            var go = new GameObject("art-" + assetName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var visual = new GameObject("Visual", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            visual.transform.SetParent(go.transform, false);
            var visualRt = (RectTransform)visual.transform;
            visualRt.anchorMin = Vector2.zero; visualRt.anchorMax = Vector2.one;
            visualRt.offsetMin = Vector2.zero; visualRt.offsetMax = Vector2.zero;
            var image = visual.GetComponent<RawImage>();
            image.texture = sprite.texture;
            var textureRect = sprite.textureRect;
            image.uvRect = new Rect(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);
            image.color = Color.white;
            image.raycastTarget = false;
            var fitter = visual.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = textureRect.height > 0f ? textureRect.width / textureRect.height : 1f;
            return true;
        }

        private static Transform BuildScrollColumn(Transform host)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D), typeof(Image));
            scrollGo.transform.SetParent(host, false);
            var srt = scrollGo.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = Vector2.one;
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            // WO-1334: the same two numbers the utility-rail vertical split is computed from.
            layout.spacing = NightMarketLayout.UtilityRowSpacingPx;
            layout.padding = new RectOffset(NightMarketLayout.UtilityColumnPadPx, NightMarketLayout.UtilityColumnPadPx,
                                            NightMarketLayout.UtilityColumnPadPx, NightMarketLayout.UtilityColumnPadPx);
            // ⛔ TRUE, AND THIS ONE FLAG IS THE P0-3 ROOT CAUSE. With childControlHeight FALSE a
            // VerticalLayoutGroup ignores LayoutElement.preferredHeight entirely and lays children out
            // at their own rect height — which for a code-built row is RectTransform's default 100
            // units. Every authored 168/240-unit row therefore resolved to 100, the cards inside were
            // squeezed onto it, and ClampMinTouch grew each back to the touch floor by expanding it
            // about its centre INTO ITS NEIGHBOURS. Turning this on is what makes an authored height
            // an authored height.
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            return contentGo.transform;
        }

        /// <summary>
        /// One store label, anchored by fraction of its (already reference-px-sized) region.
        ///
        /// <para>⛔ THE FONT FLOOR IS ENFORCED HERE, AT THE ONE PLACE STORE TEXT IS MADE, and that
        /// is deliberate rather than trusted to twenty call sites. This method used to set the raw
        /// <c>fontSize</c> it was handed — 9 to 17 — with no guard against
        /// <see cref="ElarionUi.FontFloorMobile"/>(30). At the kit's landscape scale those rendered
        /// around TEN PHYSICAL PIXELS, which is what defects #4 and #8 ("too small to scan",
        /// "Redee...") actually were. Widening the panel would not have moved them a pixel.</para>
        /// </summary>
        private static TextMeshProUGUI MakeText(Transform parent, string text, float size,
            Color color, FontStyles style, TextAlignmentOptions align, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            size = Mathf.Max(size, ElarionUi.FontFloorMobile);
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            ElarionUiKit.EnsureFont(t);
            return t;
        }
    }
}
