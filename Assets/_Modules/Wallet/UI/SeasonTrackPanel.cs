// =============================================================================
// SeasonTrackPanel - the Season Track screen (WO section U1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// CODE-BUILT uGUI. Not UXML: UXML renders EMPTY in player builds (CLAUDE.md
// section 8, learned the hard way). The dead PackStore.uxml/.uss next to this file
// are the fossils of that lesson - do not follow them.
//
// =============================================================================
//  THE PROBLEM THIS SCREEN SOLVES
// -----------------------------------------------------------------------------
// About thirty tiers do not fit on one landscape screen and must not become a wall
// of identical cells. So the track is TWO PARALLEL ROWS, not a grid: free rewards
// on the top row, premium on the bottom, one column per tier, scrolling
// horizontally with the current tier centred on open. A player reads DOWN a column
// to compare the two lanes at one tier, and ACROSS a row to see a lane's arc.
//
// =============================================================================
//  THREE RULES THAT ARE NOT STYLE, AND WHAT BREAKS IF THEY ARE DROPPED
// -----------------------------------------------------------------------------
// 1. EVERY STATE CARRIES A WORD. The owner is RED/GREEN COLOURBLIND. Colour is the
//    LAST carrier of meaning and the first that is allowed to be lost, so each of
//    the four column states prints its word from canon-strings
//    (CLAIMED / READY / LOCKED / LANE LOCKED) and the greyscale values of the two
//    lane lights step apart. Strip every hue and this screen still reads. There is
//    a regression case on exactly this and it is not decorative.
//
// 2. PREMIUM-LOCKED SHOWS THE REWARD, IT NEVER HIDES IT. Concealing a locked
//    reward turns the track into a mystery box, which section 8 forbids as
//    explicitly as it forbids gacha - and showing it is also the honest sell. The
//    lock is a small glyph and a word beside a fully-legible reward, never a
//    silhouette.
//
// 3. ONE ANIMATED ELEMENT, MAXIMUM. Motion on this screen means "this is claimable
//    now" and means nothing else, so only the READY state pulses. If everything
//    moves, nothing is being said.
//
// The screen NEVER auto-pops (section 8, discovery rule C5) - it has no Update
// that opens itself and no boot hook. The player opens it.
//
// ⛔ THE ONLY PURCHASABLE OBJECT ON THIS ENTIRE SCREEN IS THE LANE UNLOCK. Owner
// ruling Q4 (2026-08-21) is NEVER SELL TIERS - no catch-up SKU, no "unlock what
// you missed", no pro-rating, nowhere on the track. And today even the lane is not
// for sale (no SKR ledger, purchases flag-off), so the footer states that plainly
// rather than drawing a button that cannot complete.
//
// ASCII-only strings, all from canon-strings.json via StoreStrings.
// =============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using static DeNelle.Core.UI.LocalText;

namespace DeNelle.Wallet
{
    /// <summary>The season track screen: header, two-lane scrolling rail, footer.</summary>
    [DisallowMultipleComponent]
    public sealed class SeasonTrackPanel : MonoBehaviour
    {
        // ── Zones, as fractions of the WO's 2670 x 1200 landscape canvas ──────
        // Header 200 / Track 580 / Footer 420, top-down. Written as the division so
        // the wireframe number stays readable next to the fraction it produced.
        //
        // WHY THE HEADER IS 200 AND NOT THE WIREFRAME'S 120 (first capture, 2026-09-05):
        // the shared Close is a FIXED 360 x 132 reference-px box (ElarionUiKit.CanonCtaWidth
        // x CanonCtaHeight - every Close is the same size on every screen, owner F8 x3), and
        // the modal canvas is 1080 x 1920 matched 0.5, so a 120/1200 header RESOLVES to only
        // 82..91 ref px on the three capture canvases (2670x1200 / 2340x1080 / 1920x1080).
        // A 132 px control cannot be seated inside an 82 px band by any anchor arithmetic;
        // it overhung the header and the geometry oracle read BUTTON OVER TEXT on all four
        // header texts at all three resolutions. 200/1200 resolves to 142 / 144 / 159 ref px
        // (body 0.94 of canvas height, header 0.99 - 0.8333 of body), which holds the 132 px
        // box with 10 px to spare at the tightest. The track gives up the 80 px: it needs
        // strip 46 + two cells 150 + spacing 12 + padding 16 = 374, and keeps >= 438.
        private static readonly Vector2 HeaderMin = new Vector2(0.015f, 1f - 200f / 1200f);
        private static readonly Vector2 HeaderMax = new Vector2(0.985f, 0.99f);
        private static readonly Vector2 TrackMin  = new Vector2(0.015f, 420f / 1200f);
        private static readonly Vector2 TrackMax  = new Vector2(0.985f, 1f - 200f / 1200f);
        private static readonly Vector2 FooterMin = new Vector2(0.015f, 0.02f);
        private static readonly Vector2 FooterMax = new Vector2(0.985f, 420f / 1200f);

        /// <summary>The panel body inside the modal canvas. Public so the regression probe builds
        /// the body from the view's numbers instead of a copy of them.</summary>
        public static readonly Vector2 BodyMin = new Vector2(0.02f, 0.03f);
        public static readonly Vector2 BodyMax = new Vector2(0.98f, 0.97f);

        // -- The header's two bands, in REFERENCE px (not fractions) ------------
        // The Close is a fixed-px box, so the band that owns it is fixed-px too: a
        // fraction of the header would hand the Close a different share of the row at
        // every resolution and the text band's edge would drift toward it. CloseBand is
        // right-aligned in the header and CanonCtaWidth + 2 gutters wide; TextBand is
        // the header minus exactly CloseBandPx. The two rects therefore cannot intersect
        // at ANY canvas size - the gutter (12 px) is the guaranteed clearance, and it is
        // deliberately larger than LayoutOracle.OverlapPadPx (2 px).
        /// <summary>Clear space on each side of the Close inside its band, reference px.</summary>
        public const float CloseBandGutterPx = 12f;
        /// <summary>Width of the header's close band: the kit's canonical Close plus a gutter each side.</summary>
        public static readonly float CloseBandPx = ElarionUiKit.CanonCtaWidth + 2f * CloseBandGutterPx;

        /// <summary>Lane label gutter: 200 of 2670 px, inside the track.</summary>
        private const float LaneGutterFraction = 200f / 2670f;

        // Column geometry in REFERENCE px. Both are floored against the kit's touch
        // minimum rather than trusted from the wireframe, so a later re-proportion
        // cannot quietly push a tappable cell under the floor.
        private static readonly float ColumnWidth = Mathf.Max(184f, ElarionUiKit.MinTouchPx);
        private static readonly float CellHeight  = Mathf.Max(150f, ElarionUiKit.MinTouchPx);
        private const float TierStripHeight = 46f;

        private GameObject _canvas;
        private Transform  _body;
        private Transform  _rail;
        private ScrollRect _scroll;
        private TextMeshProUGUI _tierLine, _xpLine, _daysLine, _footerNote;
        private Button _claimCta;
        private TextMeshProUGUI _claimCtaLabel;

        // ── The modal-arbiter handle (DEF-212) ────────────────────────────────
        // Carried over verbatim from the retired BattleMonthlyPanels wrapper, which is the
        // ONE thing that file got right: without a handle the arbiter never learns this screen
        // is up, so PanelRouter's post-open visibility verify sees "nothing open" and Fail-logs
        // a perfectly rendered panel as the WO-465 invisible-scrim class - and two modals could
        // sit on screen at once. The name here is a DIAGNOSTIC label (FlowTrace / DeathTrace),
        // never drawn: every player-facing sentence on this screen comes from canon-strings.
        private PanelHandle _panelHandle;

        private void Awake()
        {
            _panelHandle = PanelManager.Register(
                "Season Track",
                () => { if (this != null) gameObject.SetActive(false); },
                () => this != null && _canvas != null && _canvas.activeInHierarchy);
        }

        private void OnEnable()
        {
            EnsureBuilt();
            BattlePassService.Changed += Render;
            if (_canvas != null) _canvas.SetActive(true);
            Render();
            CentreOnCurrentTier();

            // ⛔ CLOSE ON FALSE. NotifyOpened REFUSES the open during a battle (WO-437: no
            // shopping while being killed). A refusal that left this canvas up would put an
            // un-arbitrated modal over live combat, so the refusal is honoured immediately.
            if (_panelHandle != null && !PanelManager.NotifyOpened(_panelHandle))
            {
                FlowTrace.Warn("BattlePass", "SeasonTrackPanel: arbiter refused the open (battle-lock) - closing.");
                Close();
            }
        }

        private void OnDisable()
        {
            BattlePassService.Changed -= Render;
            if (_canvas != null) _canvas.SetActive(false);
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas);
        }

        /// <summary>Closes the screen. Player-initiated only - nothing here opens itself.</summary>
        private void Close() => gameObject.SetActive(false);

        // =====================================================================
        //  Build
        // =====================================================================

        private void EnsureBuilt()
        {
            if (_canvas != null) return;
            using var _ = FlowTrace.Enter("BattlePass", "SeasonTrackPanel.EnsureBuilt");

            _canvas = ElarionUiKit.BuildModalCanvas("SeasonTrackUI", 620);
            if (_canvas == null)
            {
                FlowTrace.Fail("BattlePass", "SeasonTrackPanel: modal canvas failed to build - the player would " +
                                             "see nothing at all. Screen not shown.");
                return;
            }

            ElarionUiKit.Scrim(_canvas.transform, Close);
            _body = BuildBodyInto(_canvas.transform);

            var season = BattlePassService.Season;
            string title = season != null && !string.IsNullOrEmpty(season.Name)
                ? StoreStrings.Get(StoreStrings.KeySeasonTrackTitle) + " - " + season.Name
                : StoreStrings.Get(StoreStrings.KeySeasonTrackTitle);
            var header = BuildHeaderInto(_body, title, season != null ? season.Tagline : null, Close);
            _tierLine = header.TierLine;
            _xpLine   = header.XpLine;
            _daysLine = header.DaysLine;

            BuildTrack();
            BuildFooter();

            // The bands, MEASURED, before the canvas goes dark: a resolved close rect that
            // intersects a resolved text rect is the defect the first capture found, and this
            // line is written so that defect prints as a Warn instead of an "ok".
            var root = _canvas.GetComponent<RectTransform>();
            Canvas.ForceUpdateCanvases();
            LogHeaderBands(header, root);

            _canvas.SetActive(false);
            FlowTrace.Step("BattlePass", "SeasonTrackPanel built: header, two-lane rail, footer.");
        }

        /// <summary>The header's parts. Returned by <see cref="BuildHeaderInto"/> so a probe can
        /// measure the view's OWN construction rather than a copy of it.</summary>
        public sealed class HeaderParts
        {
            public Transform Host;
            public Transform TextBand;
            public Transform CloseBand;
            public Button Close;
            public TextMeshProUGUI Title, Tagline, TierLine, XpLine, DaysLine;

            /// <summary>Every header text, in reading order. Tagline is null when the season has none.</summary>
            public TextMeshProUGUI[] Texts
            {
                get { return new[] { Title, Tagline, TierLine, XpLine, DaysLine }; }
            }
        }

        /// <summary>
        /// The panel body (glass plate + ground) inside a modal canvas, at <see cref="BodyMin"/>..
        /// <see cref="BodyMax"/>. Static and public for the same reason as
        /// <see cref="BuildHeaderInto"/>: the regression probe must build the body the way the
        /// screen does, not the way a test author remembers it.
        /// </summary>
        public static Transform BuildBodyInto(Transform canvasRoot)
        {
            var panel = ElarionUiKit.Panel(canvasRoot, BodyMin, BodyMax, deep: true);
            var body = panel.transform;
            Plate(body, NightMarketPalette.Ground);
            return body;
        }

        /// <summary>
        /// Builds the header into <paramref name="body"/>: a TextBand carrying the five header
        /// texts and, to its right, a CloseBand carrying the kit's shared Close. The two bands
        /// are disjoint BY CONSTRUCTION (see the CloseBandPx comment): the text band's right
        /// edge is inset by exactly the close band's width, so no anchor fraction, font size or
        /// resolution can put the Close over a header text.
        /// <para>Static so the layout regression measures this exact construction.</para>
        /// </summary>
        public static HeaderParts BuildHeaderInto(Transform body, string title, string tagline, Action onClose)
        {
            var parts = new HeaderParts();
            parts.Host = Zone(body, "Header", HeaderMin, HeaderMax);

            // -- TextBand: the header minus the close band, on the left ----------
            var textBand = Zone(parts.Host, "TextBand", Vector2.zero, Vector2.one);
            var trt = textBand.GetComponent<RectTransform>();
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = new Vector2(-CloseBandPx, 0f);
            parts.TextBand = textBand;

            parts.Title = Text(textBand, title, 20, ElarionUi.Parchment, FontStyles.Bold, TextAlignmentOptions.Left,
                               new Vector2(0f, 0.35f), new Vector2(0.42f, 1f));
            parts.Title.gameObject.name = "Title";
            // A single-line fact: bounded auto-size then the kit's Ellipsis, never a wrap that
            // stacks the season name under the word "Season".
            ElarionUiKit.FitSingleLine(parts.Title);

            if (!string.IsNullOrEmpty(tagline))
            {
                parts.Tagline = Text(textBand, tagline, 11, ElarionUi.ParchmentDim, FontStyles.Italic,
                                     TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0.42f, 0.35f));
                parts.Tagline.gameObject.name = "Tagline";
            }

            parts.TierLine = Text(textBand, string.Empty, 15, NightMarketPalette.Patronage, FontStyles.Bold,
                                  TextAlignmentOptions.Center, new Vector2(0.42f, 0.35f), new Vector2(0.72f, 1f));
            parts.TierLine.gameObject.name = "TierLine";
            ElarionUiKit.FitSingleLine(parts.TierLine);

            parts.XpLine = Text(textBand, string.Empty, 11, ElarionUi.ParchmentDim, FontStyles.Normal,
                                TextAlignmentOptions.Center, new Vector2(0.42f, 0f), new Vector2(0.72f, 0.35f));
            parts.XpLine.gameObject.name = "XpLine";

            // A COUNT OF DAYS, never a ticking clock. Nothing is lost at season close (earned
            // rewards are kept and unclaimed ones auto-grant), so a countdown here would be
            // manufactured urgency over a deadline that does not exist.
            parts.DaysLine = Text(textBand, string.Empty, 12, ElarionUi.ParchmentDim, FontStyles.Normal,
                                  TextAlignmentOptions.Right, new Vector2(0.72f, 0.35f), new Vector2(1f, 1f));
            parts.DaysLine.gameObject.name = "DaysLine";
            ElarionUiKit.FitSingleLine(parts.DaysLine);

            // -- CloseBand: fixed px, right-aligned, vertically centred in the header --
            // Sized to the Close itself (CanonCtaHeight tall) so SeatSharedCloseInside, which
            // seats the box's BOTTOM at the zone floor and grows it UP by CanonCtaHeight, fills
            // the band exactly: the Close is centred in the header row at every resolution.
            var closeBandGo = new GameObject("CloseBand", typeof(RectTransform));
            closeBandGo.transform.SetParent(parts.Host, false);
            var crt = closeBandGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 0.5f);
            crt.anchorMax = new Vector2(1f, 0.5f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(CloseBandPx, ElarionUiKit.CanonCtaHeight);
            parts.CloseBand = closeBandGo.transform;

            parts.Close = ElarionUiKit.ObsidianCloseButton(closeBandGo.transform, onClose,
                                                           new Vector4(0f, 0f, 1f, 1f));
            return parts;
        }

        /// <summary>
        /// One <c>[Flow:BattlePass] header bands</c> line with the RESOLVED close and text rects
        /// (reference px, root-canvas space), plus a Warn per header text the Close intersects.
        /// Returns false, and says so, when the root canvas has not resolved a size yet - an
        /// unmeasured header is reported as unmeasured, never as fine.
        /// </summary>
        public static bool LogHeaderBands(HeaderParts header, RectTransform root)
        {
            if (header == null || header.Close == null || header.TextBand == null || root == null)
            {
                FlowTrace.Warn("BattlePass", "header bands: UNMEASURED - header parts or canvas root missing.");
                return false;
            }
            try
            {
                Rect closeR, textR;
                if (!LayoutOracle.TryRectInRoot((RectTransform)header.Close.transform, root, out closeR) ||
                    !LayoutOracle.TryRectInRoot((RectTransform)header.TextBand, root, out textR))
                {
                    FlowTrace.Warn("BattlePass", "header bands: UNMEASURED - close/text rects did not resolve " +
                                                 "(root canvas not sized yet).");
                    return false;
                }

                Rect hostR;
                bool hostOk = LayoutOracle.TryRectInRoot((RectTransform)header.Host, root, out hostR);
                float gap = closeR.xMin - textR.xMax;
                FlowTrace.Step("BattlePass",
                    "header bands: close y" + closeR.yMin.ToString("0.#") + ".." + closeR.yMax.ToString("0.#") +
                    " x" + closeR.xMin.ToString("0.#") + ".." + closeR.xMax.ToString("0.#") +
                    ", text y" + textR.yMin.ToString("0.#") + ".." + textR.yMax.ToString("0.#") +
                    " x" + textR.xMin.ToString("0.#") + ".." + textR.xMax.ToString("0.#") +
                    ", gap " + gap.ToString("0.#") + " px, header " +
                    (hostOk ? hostR.height.ToString("0.#") : "?") + " px tall (resolved ref px).");

                if (hostOk && LayoutOracle.OutsideBy(closeR, hostR) > LayoutOracle.ContainSlackPx)
                    FlowTrace.Warn("BattlePass", "header bands: the Close escapes the header by " +
                                                 LayoutOracle.OutsideBy(closeR, hostR).ToString("0.#") +
                                                 " px - the header band is shorter than CanonCtaHeight.");

                bool clean = true;
                foreach (var t in header.Texts)
                {
                    if (t == null) continue;
                    Rect tr;
                    if (!LayoutOracle.TryRectInRoot(t.rectTransform, root, out tr)) continue;
                    float ow, oh;
                    if (!LayoutOracle.Overlaps(closeR, tr, LayoutOracle.OverlapPadPx, out ow, out oh)) continue;
                    clean = false;
                    FlowTrace.Warn("BattlePass", "header bands: Close covers '" + t.gameObject.name + "' by " +
                                                 ow.ToString("0.#") + "x" + oh.ToString("0.#") +
                                                 " ref px - the capture oracle will red this as BUTTON OVER TEXT.");
                }
                return clean;
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass", "header bands: measurement THREW " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private void BuildTrack()
        {
            var host = Zone(_body, "Track", TrackMin, TrackMax);

            // ── Lane labels: a fixed gutter, OUTSIDE the scroll ──────────────
            // They must not scroll away: a row without its label is two rows the player cannot tell
            // apart, and "which lane am I looking at" is the one question this screen must always
            // be able to answer.
            var gutter = Zone(host, "LaneLabels", new Vector2(0f, 0f), new Vector2(LaneGutterFraction, 1f));
            Plate(gutter, NightMarketPalette.GroundRaised);

            // FREE takes VERDANT, the same light the Night Market's free band uses, so "this costs
            // nothing" reads identically across every screen that says it.
            LaneLabel(gutter, StoreStrings.Get(StoreStrings.KeySeasonTrackLaneFree),
                      NightMarketPalette.Free, 0.50f, 0.86f);
            LaneLabel(gutter, StoreStrings.Get(StoreStrings.KeySeasonTrackLanePremium),
                      NightMarketPalette.Patronage, 0.10f, 0.46f);

            var scrollHost = Zone(host, "Rail", new Vector2(LaneGutterFraction, 0f), new Vector2(1f, 1f));
            _rail = BuildHorizontalRail(scrollHost, out _scroll);
        }

        private void LaneLabel(Transform parent, string word, Color light, float y0, float y1)
        {
            // The MARK: a 3 px rail that carries lane identity without any hue at all.
            var mark = new GameObject("mark", typeof(RectTransform), typeof(Image));
            mark.transform.SetParent(parent, false);
            var mrt = mark.GetComponent<RectTransform>();
            mrt.anchorMin = new Vector2(0.86f, y0); mrt.anchorMax = new Vector2(0.92f, y1);
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
            var mimg = mark.GetComponent<Image>();
            mimg.color = light;
            mimg.raycastTarget = false;

            Text(parent, word, 12, light, FontStyles.Bold, TextAlignmentOptions.Right,
                 new Vector2(0.04f, y0), new Vector2(0.82f, y1));
        }

        private Transform BuildHorizontalRail(Transform host, out ScrollRect scroll)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect),
                                          typeof(RectMask2D), typeof(Image));
            scrollGo.transform.SetParent(host, false);
            var srt = scrollGo.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                                           typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 0f); crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;

            var layout = contentGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            return contentGo.transform;
        }

        private void BuildFooter()
        {
            var host = Zone(_body, "Footer", FooterMin, FooterMax);
            Plate(host, new Color(0f, 0f, 0f, 0.30f));

            // The earn-rate line. It is the sentence that makes the whole screen defensible in a
            // store listing: the track is earned by playing.
            Text(host, StoreStrings.Get(StoreStrings.KeySeasonTrackEarnRate), 12, ElarionUi.Parchment,
                 FontStyles.Normal, TextAlignmentOptions.Left,
                 new Vector2(0.02f, 0.62f), new Vector2(0.60f, 0.95f));

            Text(host, StoreStrings.Get(StoreStrings.KeySeasonTrackKeptForever), 11, ElarionUi.ParchmentDim,
                 FontStyles.Normal, TextAlignmentOptions.Left,
                 new Vector2(0.02f, 0.28f), new Vector2(0.60f, 0.60f));

            Text(host, StoreStrings.Get(StoreStrings.KeyTrustNeverPower), 10, NightMarketPalette.Free,
                 FontStyles.Bold, TextAlignmentOptions.Left,
                 new Vector2(0.02f, 0.04f), new Vector2(0.60f, 0.26f));

            // The claim CTA - the screen's action, and it costs nothing.
            _claimCta = ElarionUiKit.Button(host, StoreStrings.Get(StoreStrings.KeySeasonTrackClaimCta),
                                            ElarionUiKit.ButtonKind.Gold,
                                            new Vector2(0.63f, 0.52f), new Vector2(0.98f, 0.95f),
                                            OnClaimTapped);
            _claimCtaLabel = _claimCta != null ? _claimCta.GetComponentInChildren<TextMeshProUGUI>() : null;
            if (_claimCtaLabel != null) ElarionUiKit.FitBlock(_claimCtaLabel, ElarionUi.FontFloorMobile);
            ElarionUiKit.ApplyMultilineButtonPlate(_claimCta);

            // The lane-unlock zone. TODAY IT IS A SENTENCE, NOT A BUTTON, and that is the honest
            // state: the season names no purchasable pass SKU, so a Buy control here could not
            // complete. When a real SKU and an SKR writer land, this becomes the ONE purchasable
            // control on the screen - and ruling Q4 means it stays the only one, forever.
            _footerNote = Text(host, string.Empty, 11, ElarionUi.ParchmentDim, FontStyles.Normal,
                               TextAlignmentOptions.Left,
                               new Vector2(0.63f, 0.04f), new Vector2(0.98f, 0.48f));
        }

        // =====================================================================
        //  Render
        // =====================================================================

        /// <summary>Rebuilds the rail and refreshes every live line. Safe to call repeatedly.</summary>
        public void Render()
        {
            if (_rail == null) return;
            using var _ = FlowTrace.Enter("BattlePass", "SeasonTrackPanel.Render");

            var season = BattlePassService.Season;

            // ⚠ DETACH BEFORE Destroy. Destroy is DEFERRED to end-of-frame, so a doomed child is
            // still counted by childCount until then. Render can legitimately run twice in one frame
            // (the claim CTA renders, and the Changed event it raised renders again) — without the
            // detach, the second pass would build a second full rail on top of a set that has not
            // been reaped yet, and the player would see every tier twice for a frame.
            for (int i = _rail.childCount - 1; i >= 0; i--)
            {
                var child = _rail.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            if (season == null || season.Tiers == null || season.Tiers.Count == 0)
            {
                Text(_rail, StoreStrings.Get(StoreStrings.KeySeasonTrackEmpty), 13, ElarionUi.ParchmentDim,
                     FontStyles.Italic, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
                SetHeaderLines(0, 0);
                FlowTrace.Warn("BattlePass", "Render: no season authored - empty state shown (NOT a blank panel).");
                return;
            }

            for (int i = 0; i < season.Tiers.Count; i++)
                BuildColumn(season.Tiers[i]);

            SetHeaderLines(BattlePassService.HighestTierReached, season.Tiers.Count);
            RefreshCta(season);
        }

        private void SetHeaderLines(int tier, int count)
        {
            if (_tierLine != null)
                _tierLine.text = StoreStrings.Format(StoreStrings.KeySeasonTrackTierLine, tier, count);

            if (_xpLine != null)
            {
                var next = BattlePassService.NextTier;
                _xpLine.text = next == null
                    ? StoreStrings.Format(StoreStrings.KeySeasonTrackXpLineCapstone, BattlePassService.Xp)
                    : StoreStrings.Format(StoreStrings.KeySeasonTrackXpLine,
                                          BattlePassService.Xp, BattlePassService.XpFor(next));
            }

            if (_daysLine != null)
                _daysLine.text = StoreStrings.Format(StoreStrings.KeySeasonTrackDaysLeft,
                                                     BattlePassService.DaysRemaining);
        }

        private void RefreshCta(BattlePassSeason season)
        {
            bool claimable = BattlePassService.HasClaimable;

            if (_claimCta != null) _claimCta.interactable = claimable;
            if (_claimCtaLabel != null)
                _claimCtaLabel.text = claimable
                    ? StoreStrings.Get(StoreStrings.KeySeasonTrackClaimCta)
                    : StoreStrings.Get(StoreStrings.KeySeasonTrackNothingToClaim);

            if (_footerNote == null) return;

            if (BattlePassService.PremiumLaneOwned)
                _footerNote.text = StoreStrings.Get(StoreStrings.KeySeasonTrackLaneRetro);
            else if (season != null && season.HasPurchasablePremiumLane)
                _footerNote.text = StoreStrings.Get(StoreStrings.KeySeasonTrackLaneCta) + " - " +
                                   StoreStrings.Get(StoreStrings.KeySeasonTrackLaneRetro);
            else
                _footerNote.text = StoreStrings.Get(StoreStrings.KeySeasonTrackLaneNotForSale);
        }

        private void OnClaimTapped()
        {
            int n = BattlePassService.ClaimAllReady();
            FlowTrace.Step("BattlePass", "claim CTA tapped: " + n + " reward(s) granted.");
            Render();
        }

        // =====================================================================
        //  One column = one tier, two stacked cells
        // =====================================================================

        private void BuildColumn(BattlePassTier tier)
        {
            if (tier == null) return;

            var col = new GameObject("Tier" + tier.Tier, typeof(RectTransform), typeof(LayoutElement),
                                     typeof(VerticalLayoutGroup));
            col.transform.SetParent(_rail, false);
            var le = col.GetComponent<LayoutElement>();
            le.preferredWidth = ColumnWidth;
            le.minWidth = ColumnWidth;

            var vl = col.GetComponent<VerticalLayoutGroup>();
            vl.spacing = 6f;
            vl.childControlHeight = false;
            vl.childControlWidth = true;
            vl.childForceExpandHeight = false;
            vl.childForceExpandWidth = true;

            // ── the tier number strip ────────────────────────────────────────
            var strip = new GameObject("TierNo", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            strip.transform.SetParent(col.transform, false);
            strip.GetComponent<LayoutElement>().preferredHeight = TierStripHeight;
            var stripImg = strip.GetComponent<Image>();
            stripImg.color = NightMarketPalette.GroundRaised;
            stripImg.raycastTarget = false;

            string stripText = tier.IsCapstone
                ? tier.Tier + "  " + StoreStrings.Get(StoreStrings.KeySeasonTrackCapstone)
                : tier.Tier.ToString();
            Text(strip.transform, stripText, tier.IsCapstone ? 10 : 13,
                 tier.IsCapstone ? NightMarketPalette.Patronage : ElarionUi.Parchment,
                 FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

            // ── free cell (top row), then premium (bottom row) ───────────────
            BuildCell(col.transform, tier, tier.Free, BattlePassService.FreeState(tier),
                      NightMarketPalette.Free, premium: false);
            BuildCell(col.transform, tier, tier.Premium, BattlePassService.PremiumState(tier),
                      NightMarketPalette.Patronage, premium: true);
        }

        private void BuildCell(Transform parent, BattlePassTier tier, RewardGrant grant, TierState state,
                               Color laneLight, bool premium)
        {
            var cell = new GameObject(premium ? "Premium" : "Free",
                                      typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            cell.transform.SetParent(parent, false);
            cell.GetComponent<LayoutElement>().preferredHeight = CellHeight;

            var plate = cell.GetComponent<Image>();
            plate.raycastTarget = false;
            // Value, not hue, carries "is this yours yet": a claimed cell sits back, a ready cell
            // sits forward. Both readings survive with the colour removed.
            plate.color = state == TierState.Earned
                ? new Color(laneLight.r, laneLight.g, laneLight.b, 0.10f)
                : NightMarketPalette.GroundRaised;

            // The 3 px lane mark down the cell - lane identity with no hue required.
            var mark = new GameObject("mark", typeof(RectTransform), typeof(Image));
            mark.transform.SetParent(cell.transform, false);
            var mrt = mark.GetComponent<RectTransform>();
            mrt.anchorMin = new Vector2(0f, 0f); mrt.anchorMax = new Vector2(0.035f, 1f);
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
            var mimg = mark.GetComponent<Image>();
            mimg.color = laneLight;
            mimg.raycastTarget = false;

            // ⛔ THE REWARD IS ALWAYS DRAWN, IN EVERY STATE INCLUDING PREMIUM-LOCKED. This line is
            // the rule from the WO's U1 made literal - there is no branch here that hides it.
            string body = grant != null ? grant.Describe() : string.Empty;
            if (string.IsNullOrEmpty(body)) body = "-";
            Text(cell.transform, body, 10, ElarionUi.Parchment, FontStyles.Normal,
                 TextAlignmentOptions.Center, new Vector2(0.08f, 0.30f), new Vector2(0.96f, 0.92f));

            // The WORD. This is what makes the state survive greyscale.
            string word = StateWord(state);
            Color wordColor = state == TierState.Locked || state == TierState.PremiumLocked
                ? ElarionUi.ParchmentDim
                : laneLight;
            // A shape as well as a word, for the states that mean "not yours yet".
            string glyph = state == TierState.PremiumLocked || state == TierState.Locked ? new LocalizedText("village.season_track.state_locked_prefix").Resolve() : "";
            Text(cell.transform, glyph + word, 9, wordColor, FontStyles.Bold,
                 TextAlignmentOptions.Center, new Vector2(0.08f, 0.04f), new Vector2(0.96f, 0.28f));

            // THE ONE ANIMATION. Only READY pulses, and only because motion here means exactly one
            // thing: this is claimable now.
            if (state == TierState.Ready)
            {
                cell.AddComponent<PulseOnReady>().Bind(plate, NightMarketPalette.GroundRaised, laneLight);
            }
        }

        private static string StateWord(TierState state)
        {
            switch (state)
            {
                case TierState.Earned:        return StoreStrings.Get(StoreStrings.KeySeasonTrackStateEarned);
                case TierState.Ready:         return StoreStrings.Get(StoreStrings.KeySeasonTrackStateReady);
                case TierState.PremiumLocked: return StoreStrings.Get(StoreStrings.KeySeasonTrackStatePremiumLock);
                default:                      return StoreStrings.Get(StoreStrings.KeySeasonTrackStateLocked);
            }
        }

        /// <summary>Scrolls so the tier the player is ON is the one they see first.</summary>
        private void CentreOnCurrentTier()
        {
            if (_scroll == null) return;
            int count = BattlePassService.TierCount;
            if (count <= 1) { _scroll.horizontalNormalizedPosition = 0f; return; }
            float t = Mathf.Clamp01((BattlePassService.HighestTierReached - 0.5f) / (count - 1));
            _scroll.horizontalNormalizedPosition = t;
        }

        // =====================================================================
        //  The single animated element
        // =====================================================================

        /// <summary>
        /// A slow plate breathe on a READY cell. Deliberately the ONLY moving thing on this screen:
        /// motion is a word here ("claimable now"), and a screen where everything moves says nothing.
        /// </summary>
        private sealed class PulseOnReady : MonoBehaviour
        {
            private Image _plate;
            private Color _rest, _lit;

            public void Bind(Image plate, Color rest, Color lit)
            {
                _plate = plate;
                _rest = rest;
                _lit = new Color(lit.r, lit.g, lit.b, 0.28f);
            }

            private void Update()
            {
                if (_plate == null) { enabled = false; return; }
                float t = (Mathf.Sin(Time.unscaledTime * 2.2f) + 1f) * 0.5f;
                _plate.color = Color.Lerp(_rest, _lit, t);
            }
        }

        // =====================================================================
        //  uGUI helpers (same shapes as PackStore)
        // =====================================================================

        private static Transform Zone(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go.transform;
        }

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

        private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
                                            FontStyles style, TextAlignmentOptions align,
                                            Vector2 min, Vector2 max)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text ?? string.Empty;
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
