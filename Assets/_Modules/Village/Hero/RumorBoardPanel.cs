// =============================================================================
// RumorBoardPanel - Brom's Rumor Board.  WO-1192 v3 FULL REBUILD.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hero
//
// READ-ONLY consumer of RumorBoardVM (strict MVVM): the View renders VM
// projections and routes taps to Invoke / NextPage / PrevPage; it never touches
// QuestService / QuestCatalog / DailyQuestService.
//
// WO-1521 (owner report 2026-09-06, "quests say one quest to claim but no idea how
// or what to do to complete it"): a poster's ONE door now wears the face the VM
// gives it - Claim / Go To / Accept - and every tap goes to RumorBoardVM.Invoke.
// This View never branches on the row kind: a skin that picks the verb is how a
// CLAIM face ends up starting a quest. The empty-state gate is the VM's IsQuiet
// (the whole LIST) and no longer `shown == 0` (this PAGE), which is precisely how
// "The board is quiet." painted while the Journey card said one was ready to claim.
//
// =============================================================================
//  WHY THIS FILE WAS REBUILT RATHER THAN TUNED (WO-1192, owner-approved v3)
// -----------------------------------------------------------------------------
//  The master-detail board (tab band + card list + detail pane + In-Progress
//  section + status line) was measured on FRESH captures on 2026-08-25 and again
//  on 2026-08-26 and failed both times, differently in each orientation:
//    * portrait  - the detail pane overlaid the whole list, the "* All" tab chip
//                  floated over the "In Progress" heading, reward chips truncated
//                  to "X... / Crys... / St... / Ma...".
//    * landscape - the status line bisected the second In-Progress card, the
//                  objective ended MID-WORD ("begun to sin"), two card titles
//                  truncated to the IDENTICAL string, and the lower third was
//                  dead black.
//  Those are not six tuning bugs. They are one structural bug: FIVE competing
//  regions (tabs / list / detail / status / footer) fighting for a screen that
//  only ever had room for one, so every aspect change re-broke a different one.
//
//  THE v3 CONCEPT (owner: "i like it" + "go"): THREE SELF-CONTAINED RUMOR
//  POSTERS. No tabs, no detail pane, no In-Progress, no selection step. Each
//  poster carries its own type tag, title, one-line hook, rewards and its OWN
//  Accept. Next > and Previous up top page by three and WRAP. Dense copy lives
//  behind a "Read the letter >" full-card overlay.
//
//  [STOP] THERE IS NO ALLOW-LIST ENTRY FOR THIS PANEL, AND THERE MUST NEVER BE ONE
//  (owner ruling 2026-08-24: no waivers; LayoutOracle's TouchBaseline allow-list
//  may only ever SHRINK). The six WO-1060 findings against this panel were all of
//  one shape - a card overlapping the tab-band chip label or the detail pane at
//  portrait-tall aspects. They are retired BY CONSTRUCTION here: the tab band and
//  the detail pane no longer exist, so nothing can overlap either of them.
//
//  LANDSCAPE ONLY (owner ruling 2026-08-26). The game is landscape; portrait work
//  is out of scope and is deliberately NOT re-litigated. There is exactly ONE
//  layout in this file - no portrait branch to drift out of sync with it, which is
//  what the two-layout file cost every time an aspect changed.
//
// -----------------------------------------------------------------------------
//  THE GEOMETRY LAW (unchanged from WO-866, and the reason this file survives)
// -----------------------------------------------------------------------------
//  Every band inside a poster is a FIXED REFERENCE-PIXEL budget hung off the
//  card's top or bottom edge - never a fraction of the card. A fraction band
//  scales with the aspect and TMP culls its line box whole the moment it dips
//  under one line; that is what produced the -11 px detail body in WO-866 and the
//  culled reward chips in WO-1060. The card's own PLACEMENT is fractional (it has
//  to be, it is a column of a three-column board); its CONTENTS are not.
//
//  Buttons are authored AT the kit touch floor, never grown into it. Each kit
//  button is built into a host RectTransform whose FIXED pixel height is already
//  >= ElarionUiKit.MinTouchPx, so ClampMinTouch has nothing to rescue - and
//  therefore nothing to spill into a neighbour. [STOP] ClampMinTouch is NOT a cause of
//  any overlap here and must not be named as one.
//
//  ASCII-ONLY, including comments: the shipped LiberationSans SDF has no
//  non-Latin glyphs, a tofu oracle scans UI files, and
//  RumorBoardLayoutRegression asserts this whole file is ASCII.
//
//  COLOURBLIND LAW (the owner is red/green colourblind): the type tag is
//  separable by FILL + POSITION (a filled gold plate that OVERHANGS the card's
//  top-left corner), the NEW chip by its OUTLINE + top-right position, and Accept
//  by being the only large framed box on the card. Every one of those survives a
//  greyscale pass because none of them is carried by hue.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village.Hero
{
    public sealed class RumorBoardPanel : MonoBehaviour
    {
        // =====================================================================
        //  THE v3 ANCHOR TABLE - fractions of the SCREEN, verbatim from
        //  WorkOrders/WORK_ORDER_1192_mockup_v3_2670x1200.png's spec table.
        //  Public so RumorBoardLayoutRegression can diff them against that table
        //  without a reflection bridge into private state.
        // =====================================================================

        /// <summary>The modal's footprint on the screen. Both axes use the SAME pair, which is
        /// what lets <see cref="PanelFrac"/> convert a screen fraction for either axis.</summary>
        public const float PanelAnchorMin = 0.02f;
        /// <inheritdoc cref="PanelAnchorMin"/>
        public const float PanelAnchorMax = 0.98f;

        public const float TitleXMin = 0.056f;
        public const float TitleXMax = 0.600f;
        public const float TitleYMin = 0.860f;
        public const float TitleYMax = 0.935f;

        /// <summary>Top of the head row (Previous / Next / Close), as a screen fraction.</summary>
        public const float HeadTopY = 0.935f;
        public const float NextXMin = 0.640f;
        public const float NextXMax = 0.755f;

        /// <summary>The Previous face. Full word, never "Prev" / "Pr..." - FitSingleLine
        /// ellipsises past the floor, which is exactly the truncation the owner bounce
        /// (2026-08-27) must not ship.</summary>
        public const string PreviousLabel = "Previous";
        /// <summary>The Next face. Kept as a named constant so both paging labels live
        /// in one place and the layout oracle can lint them without a string hunt.</summary>
        public const string NextLabel = "Next >";
        /// <summary>Pixel gap between Previous and Next (and between the title and
        /// Previous). Above the overlap oracle's 6 px clearance.</summary>
        public const float HeadGapPx = 16f;
        /// <summary>BuildObsidianButton insets its label to x 0.04..0.96 of the host.
        /// The host width is the MEASURED label divided by this, never a character guess.</summary>
        public const float PageButtonLabelInset = 0.92f;
        /// <summary>MeasureLineWidthPx sums regular-weight advances; the button is bold.
        /// 10% slack so the bold face cannot push a glyph into ellipsis.</summary>
        public const float PageButtonBoldSlack = 1.10f;
        /// <summary>Horizontal CENTRE of the shared Close box. The Close keeps its canonical
        /// <see cref="ElarionUiKit.CanonCtaWidth"/> x <see cref="ElarionUiKit.CanonCtaHeight"/>
        /// pixel size (owner F8 x3: every Close is the same box on every screen), so only its
        /// centre can be authored - a right EDGE would resolve to a different fraction on every
        /// aspect and eventually spill outside the panel.</summary>
        public const float CloseCentreX = 0.880f;

        public const float PosterYMin = 0.083f;
        public const float PosterYMax = 0.767f;
        public const float Poster1XMin = 0.056f;
        public const float Poster1XMax = 0.318f;
        public const float Poster2XMin = 0.375f;
        public const float Poster2XMax = 0.637f;
        public const float Poster3XMin = 0.693f;
        public const float Poster3XMax = 0.955f;

        // =====================================================================
        //  THE POSTER'S FIXED-PIXEL STACK (reference px, top-down then bottom-up).
        //  Read the file header: fixed px, never a fraction of the card.
        // =====================================================================

        /// <summary>Head row band height. At/above the touch floor with margin, and FIXED so
        /// Previous, Next and Close are tappable at every aspect (the mockup's 0.823-0.917 band
        /// resolves to 91 ref px at 2670x1200 - 21 px UNDER the floor - which is why the head
        /// row is a pixel band here and not the table's fraction).</summary>
        public const float HeadBandPx = 120f;

        /// <summary>Overhanging TYPE TAG plate height.</summary>
        public const float TypeTagPx = 76f;
        /// <summary>Outlined NEW chip on the card's top-right edge.</summary>
        public const float NewChipPx = 60f;
        /// <summary>How far the tag and the NEW chip poke ABOVE the card's top edge.
        ///
        /// The mockup draws them straddling the edge at their half-height (38 px). That is
        /// 38 px of the panel's head gutter, and at 2670x1200 the gutter between the poster
        /// top and the shared Close's bottom is only ~30 px - a half-height straddle puts the
        /// NEW chip INSIDE the Close's box on the right-hand poster. The overhang is therefore
        /// a DECLARED number (16), not a consequence of the plate's height, and
        /// RumorBoardLayoutRegression pins it against that measured gutter at every aspect.
        /// The tag still reads as overhanging; it just cannot reach the head row.</summary>
        public const float TypeTagOverhangPx = 16f;

        /// <summary>Title band top inset. Clears the tag plate, which hangs from
        /// +TypeTagOverhangPx down to TypeTagPx - TypeTagOverhangPx (60 px) below the top.</summary>
        public const float TitleTopPx = 92f;
        /// <summary>The LARGEST size the poster title may render at. It is the ceiling handed to
        /// <c>ElarionUiKit.FitBlock</c> at the title's build site, declared as a const so this band
        /// and RumorBoardLayoutRegression budget against the size that ACTUALLY draws.
        /// WO-1636: this band's doc used to read "TWO FontBody(50) line boxes (2 x 62.5 = 125)"
        /// while the fit call capped the title at 40 - the band reserved 25 px for a size the title
        /// can never reach, and the regression pinned it against FontBody for the same reason.
        /// Those 25 px are what the two-line hook below is funded from.</summary>
        public const float TitleFontMaxPx = 40f;
        /// <summary>TWO TitleFontMaxPx(40) line boxes (2 x 50 = 100) plus 4 px of slack.</summary>
        public const float TitleBandPx = 104f;
        /// <summary>Hook band top inset (title floor + an 8 px breath).</summary>
        public const float HookTopPx = TitleTopPx + TitleBandPx + 8f;
        /// <summary>TWO FontMicro(32) line boxes (2 x 40 = 80) plus 2 px of slack.
        /// WO-1636 (the first glyph-oracle run, Builds/wave3-capture2): the hook was ONE line in a
        /// 46 px band and that run MEASURED IT ELLIPSIZING ON EIGHTEEN LABELS - "Carry the sealed
        /// ledger past the flooded stai..." drew 27 of 62 printable glyphs at 1920x1080. The band
        /// is 451.2 ref px WIDE at that aspect and the poster row is three owner-approved columns,
        /// so WIDTH cannot grow; the honest fix is the band's HEIGHT. Two lines at the widest
        /// measured advance (15.04 px/char, from "Done: Clear 3 waves at the western gate." drawing
        /// 30 chars in 451.2 px) hold ~60 chars, which is why RumorBoardVM.HookMaxChars cuts at 52.</summary>
        public const float HookBandPx = 82f;
        /// <summary>"Read the letter &gt;" band top inset (hook floor + a 12 px breath).</summary>
        public const float ReadTopPx = HookTopPx + HookBandPx + 12f;
        /// <summary>The letter link is a REAL tap target, so it is authored AT the touch floor.
        /// A 30 px gilt line with a 30 px hit box is a defect, not a lighter treatment.</summary>
        public const float ReadBandPx = 112f;

        /// <summary>Accept's band, bottom-hung.</summary>
        public const float AcceptBottomPx = 20f;
        /// <summary>Accept's height, AUTHORED AT the kit touch floor and READ FROM its one owner
        /// so the two can never drift (the WO-1623 idiom). WO-1636 took the 8 px of margin this
        /// band carried over the floor and spent it on the two-line hook: a caption the player
        /// cannot finish reading costs more than 8 px of thumb comfort on a face that already
        /// meets the floor. The mockup's 140 screen px resolves to ~113 ref px at 2670x1200,
        /// so 112 is still the mockup's own number to within a pixel.</summary>
        public const float AcceptBandPx = ElarionUiKit.MinTouchPx;
        /// <summary>Reward chip row, bottom-hung above Accept.</summary>
        public const float RewardBottomPx = AcceptBottomPx + AcceptBandPx + 18f;
        /// <summary>The chip it has to seat, exactly: <see cref="ChipHeightPx"/> (52), which is
        /// itself above one FontMicro line box (40). WO-1636 spent the 8 px this band carried
        /// above the chip on the two-line hook band.</summary>
        public const float RewardBandPx = ChipHeightPx;
        /// <summary>Gilt hairline above the reward row.</summary>
        public const float RulePx = 2f;
        /// <summary>Hairline bottom inset.</summary>
        public const float RuleBottomPx = RewardBottomPx + RewardBandPx + 16f;

        /// <summary>Everything the stack consumes from the card's TOP edge.</summary>
        public const float PosterStackTopPx = ReadTopPx + ReadBandPx;
        /// <summary>Everything the stack consumes from the card's BOTTOM edge.</summary>
        public const float PosterStackBottomPx = RuleBottomPx + RulePx;
        /// <summary>The shortest card the stack can honestly live in, with a 24 px gutter
        /// between its two halves. Pinned by RumorBoardLayoutRegression against the card
        /// height the poster band really resolves to at every landscape capture aspect.</summary>
        public const float PosterMinHeightPx = PosterStackTopPx + PosterStackBottomPx + 24f;

        /// <summary>Status band at the panel's floor (one FontMicro line box).</summary>
        public const float StatusBandPx = 40f;
        /// <summary>Status band's inset from the panel floor.</summary>
        public const float StatusBottomPx = 4f;

        /// <summary>Card edge padding, as a fraction of the card (a border inset, not a band).</summary>
        public const float CardSideFrac = 0.05f;

        /// <summary>Chip metrics - measured label + padding, never a per-character guess.</summary>
        private const float ChipPadPx = 18f;
        private const float ChipSpacingPx = 8f;
        /// <summary>The reward chip's own height. PUBLIC since WO-1636 because RewardBandPx is
        /// now authored AS this number and RumorBoardLayoutRegression pins that it seats it -
        /// a band that must hold a chip should be budgeted from the chip, not from a literal.</summary>
        public const float ChipHeightPx = 52f;
        // Reward chips are secondary metadata in a dense four-chip row. A 24px floor
        // remains comfortably legible at the supported physical resolutions and avoids
        // replacing authoritative XP / MORE words with ellipses.
        private const float ChipMinFontPx = 24f;

        /// <summary>Swipe distance (screen px) that commits a page turn. Same gesture family
        /// and the same threshold as the hero-select carousel, deliberately.</summary>
        private const float SwipeThresholdPx = 72f;

        // ONE chip language: border + fill + ink are the same for a reward chip and a NEW
        // chip. The WORD (or the icon) carries meaning, never the colour.
        private static readonly Color ChipBorder = new Color(ElarionUi.Gilt.r, ElarionUi.Gilt.g, ElarionUi.Gilt.b, 0.60f);
        private static readonly Color ChipFill = new Color(0.05f, 0.045f, 0.04f, 1f);
        private static readonly Color PlateInk = new Color(0.05f, 0.045f, 0.04f, 1f);
        private static readonly Color CardFill = new Color(0.035f, 0.033f, 0.038f, 0.98f);
        private static readonly Color HairLine = new Color(ElarionUi.Gilt.r, ElarionUi.Gilt.g, ElarionUi.Gilt.b, 0.45f);
        private static readonly Color CardEdge = new Color(ElarionUi.Gilt.r, ElarionUi.Gilt.g, ElarionUi.Gilt.b, 0.38f);

        private GameObject _ui;
        private Transform _content;
        private RectTransform _posterHost;
        private TMPro.TextMeshProUGUI _statusText;
        private RumorBoardVM _vm;
        private PanelHandle _handle;

        private Vector2 _swipeStart;
        private bool _trackingSwipe;

        // -- Public API ----------------------------------------------------------

        public void Open()
        {
            Close();

            if (_handle == null)
                _handle = PanelManager.Register("Rumor Board", Close, () => _ui != null);

            _vm = RumorBoardVM.CreateDefault(Close);
            _vm.Changed += Repaint;

            // The modal is built with an EMPTY kit title on purpose. The kit's own title is a
            // CENTRED band across x 0.06-0.94 at y 0.92-0.98, which is exactly where the v3
            // head row's Next and Close live - a centred title there resolves as a genuine
            // BUTTON OVER TEXT finding, not a cosmetic one. The board's title is authored below
            // at the mockup's LEFT rect instead, so the two can never share a band.
            var modal = ElarionUiKit.BuildObsidianModal("RumorBoardPanelUI", "",
                new Vector2(PanelAnchorMin, PanelAnchorMin),
                new Vector2(PanelAnchorMax, PanelAnchorMax),
                Close, sortingOrder: 1000);
            MedievalUiSkin.ApplyShell(modal.chrome);
            EnsureBackdrop(modal);
            ForceSimpleArtwork(modal.chrome != null ? modal.chrome.close : null);
            _ui = modal.canvas;
            var panel = modal.chrome != null ? modal.chrome.content : null;
            if (panel == null)
            {
                Debug.LogError("[RumorBoardPanel] the kit returned no panel content - board not built.");
                return;
            }
            _content = panel.transform;

            // This board authors its own left-aligned title and head-row rule. The
            // kit's intentionally empty title still produces a crest, shadow, and
            // underline; those decorations otherwise sit behind the paging controls.
            RetireUnusedKitHeader(panel.transform, modal.chrome.title);

            // The ONE shared Close, RE-SEATED (position only) into the head row beside Next.
            // Owner ruling: Close is a LABELED BUTTON next to Next - no X glyph. It keeps the
            // kit's canonical box, so it is still the same Close as every other screen's; only
            // the band it sits in is this board's.
            if (modal.chrome.close != null)
            {
                float closeBandY = PanelFrac(HeadTopY) - CloseBandHeightFraction(panel.transform);
                float cx = CloseCentreXFrac();
                // SeatSharedCloseInside takes (xMin, yBand, xMax, yTop) and seats the canonical
                // box's BOTTOM at yBand, centred on (xMin+xMax)/2. Going through the kit's own
                // seater keeps ONE seating rule for the shared Close instead of a second copy
                // of its arithmetic here.
                ElarionUiKit.SeatSharedCloseInside(modal.chrome.close,
                    new Vector4(cx, closeBandY, cx, closeBandY));
            }

            BuildSwipeSurface();
            BuildPreviousButton();
            BuildNextButton();
            BuildTitle();

            var hostGo = new GameObject("PosterRow", typeof(RectTransform));
            hostGo.transform.SetParent(_content, false);
            _posterHost = hostGo.GetComponent<RectTransform>();
            _posterHost.anchorMin = Vector2.zero;
            _posterHost.anchorMax = Vector2.one;
            _posterHost.offsetMin = Vector2.zero;
            _posterHost.offsetMax = Vector2.zero;

            Repaint();

            if (!PanelManager.NotifyOpened(_handle)) return;

            Debug.Log("[RumorBoardPanel] Opened (WO-1192 v3: three posters, Previous/Next wrap).");
        }

        public void Close()
        {
            if (_vm != null) { _vm.Changed -= Repaint; _vm.Dispose(); _vm = null; }

            if (_ui != null) Destroy(_ui);
            _ui = null;
            _content = null;
            _posterHost = null;
            _statusText = null;
            _trackingSwipe = false;
            PanelManager.NotifyClosed(_handle);
        }

        private void OnDestroy()
        {
            if (_vm != null) { _vm.Changed -= Repaint; _vm.Dispose(); _vm = null; }
            if (_ui != null) Destroy(_ui);
        }

        // -- Screen-fraction -> panel-fraction ------------------------------------

        /// <summary>Convert a fraction of the SCREEN (the v3 table's space) into a fraction of
        /// the PANEL (the space the kit's chrome.content anchors resolve in). Valid for both
        /// axes because the modal uses the same anchor pair on both.</summary>
        public static float PanelFrac(float screenFrac) =>
            (screenFrac - PanelAnchorMin) / (PanelAnchorMax - PanelAnchorMin);

        /// <summary>The shared Close box's height as a fraction of THIS panel's height, read
        /// off the kit's canonical pixel box and <c>PostScaleCanvasHeight</c> - never a live
        /// <c>rect.height</c>, which returns RAW SCREEN PIXELS on the canvas's creation frame
        /// (the F8-5 root cause the kit documents).</summary>
        private static float CloseBandHeightFraction(Transform panel)
        {
            float canvasH = ElarionUiKit.PostScaleCanvasHeight(panel);
            float panelHPx = Mathf.Max(1f, (PanelAnchorMax - PanelAnchorMin) * canvasH);
            return Mathf.Clamp(ElarionUiKit.CanonCtaHeight / panelHPx, 0.02f, 0.45f);
        }

        private static float CloseCentreXFrac() => PanelFrac(CloseCentreX);

        private static void RetireUnusedKitHeader(Transform panel, TMPro.TextMeshProUGUI kitTitle)
        {
            if (panel == null) return;
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                var child = panel.GetChild(i);
                var label = child.GetComponent<TMPro.TextMeshProUGUI>();
                if (label != null && (label == kitTitle || label.text.Trim() == ElarionUi.CrestGlyph))
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                if (child.name == "Rule")
                {
                    var rt = child as RectTransform;
                    if (rt != null && rt.anchorMin.y > 0.85f)
                        child.gameObject.SetActive(false);
                }
            }
        }

        // -- Backdrop (WO-1521 sec.4) ---------------------------------------------

        /// <summary>Minimum alpha a backdrop must carry to actually hide the town behind it.
        /// The kit authors 0.94 (ElarionUiKit.BuildObsidianPanel's withBackdrop block); anything
        /// materially below that reads as "the town bleeds through", which is the owner's
        /// report. Public so the oracle fixtures the SAME number the panel repairs against.</summary>
        public const float BackdropAlphaFloor = 0.9f;

        /// <summary>The kit's own object name for the full-screen backdrop image.</summary>
        public const string BackdropObjectName = "Backdrop";

        /// <summary>
        /// PURE. Given what was actually found on the built hierarchy, says whether the backdrop
        /// is doing its job. Pure so the oracle can drive every branch with a fixture instead of
        /// a device capture - which is exactly what WO-1521 sec.4 was blocked on.
        /// </summary>
        /// <param name="present">A child named <see cref="BackdropObjectName"/> was found.</param>
        /// <param name="activeInHierarchy">That object is actually being drawn.</param>
        /// <param name="alpha">Its Image colour alpha (0 when it carries no Image at all).</param>
        public static bool BackdropNeedsRepair(bool present, bool activeInHierarchy, float alpha) =>
            !present || !activeInHierarchy || alpha < BackdropAlphaFloor;

        /// <summary>
        /// WO-1521 sec.4 - THE OWED HIERARCHY DUMP, TAKEN EVERY TIME THE BOARD OPENS.
        ///
        /// The ticket says the backdrop is ABSENT and the town bleeds through. Read at source,
        /// it is BUILT: BuildObsidianModal -> BuildObsidianPanel(withBackdrop: true) authors a
        /// full-rect 0.94-alpha "Backdrop" image, and MedievalUiSkin.ApplyShell never touches
        /// chrome.backdrop. Naming a cause from that source read would be the inference-fix
        /// CLAUDE.md sec.12 bans, so this does the other thing: it MEASURES the built object and
        /// says what it found, every open, in the trace - present? drawn? what alpha? That line
        /// is the evidence the next capture was going to have to produce by hand.
        ///
        /// And when the invariant is genuinely violated it REPAIRS it rather than shipping the
        /// leak a second night. The repair is conditional by construction, so this can never
        /// stack a second backdrop on top of the kit's own - the failure mode a blind "add a
        /// backdrop" would have had.
        /// </summary>
        private static void EnsureBackdrop(ElarionUiKit.ObsidianModal modal)
        {
            if (modal == null || modal.canvas == null) return;
            var root = modal.canvas.transform;

            var found = modal.chrome != null && modal.chrome.backdrop != null
                ? modal.chrome.backdrop.transform
                : root.Find(BackdropObjectName);
            var img = found != null ? found.GetComponent<Image>() : null;

            bool present = found != null;
            bool drawn = present && found.gameObject.activeInHierarchy;
            float alpha = img != null ? img.color.a : 0f;

            DeNelle.Core.Diagnostics.FlowTrace.Step("RumorBoard",
                "backdrop dump: present=" + present + " drawn=" + drawn +
                " alpha=" + alpha.ToString("0.00") +
                " image=" + (img != null) + " canvasChildren=" + root.childCount + ".");

            if (!BackdropNeedsRepair(present, drawn, alpha)) return;

            DeNelle.Core.Diagnostics.FlowTrace.Warn("RumorBoard",
                "the modal backdrop is NOT hiding the town (present=" + present + " drawn=" + drawn +
                " alpha=" + alpha.ToString("0.00") + ") - repairing it to the kit's own value. " +
                "This warning IS the finding: the kit authors this backdrop at 0.94 and nothing " +
                "in this panel removes it, so a run that prints this line names a seam no source " +
                "read could (WO-1521 sec.4).");

            if (img == null)
            {
                // Through the KIT primitive, never a hand-rolled Image: ElarionUiKit.AddImage is
                // the same call BuildObsidianPanel's own withBackdrop block makes, so the repair
                // and the original are one builder (and this View stays clear of the
                // hand-rolled-uGUI law UiObsidianConformanceRegression arms).
                var go = ElarionUiKit.AddImage(root, BackdropObjectName, Vector2.zero, Vector2.one,
                                               new Color(0.02f, 0.015f, 0.012f, 0.94f), rounded: false);
                if (go == null) return;
                // Behind the panel chrome, in front of the scrim - the sibling index the kit's
                // own backdrop occupies (it is added immediately before the panel).
                go.transform.SetSiblingIndex(Mathf.Max(0, root.childCount - 2));
                found = go.transform;
                img = go.GetComponent<Image>();
            }
            if (found != null && !found.gameObject.activeSelf) found.gameObject.SetActive(true);
            if (img != null)
            {
                var c = img.color;
                // Alpha is the only channel this repairs. The ink stays whatever the kit chose,
                // unless the image was never tinted at all (pure white default), in which case
                // the kit's own backdrop ink is the honest value to fall back to.
                bool untinted = c.r >= 1f && c.g >= 1f && c.b >= 1f;
                img.color = untinted
                    ? new Color(0.02f, 0.015f, 0.012f, 0.94f)
                    : new Color(c.r, c.g, c.b, 0.94f);
                img.raycastTarget = false;
            }
        }

        // -- Chrome ---------------------------------------------------------------

        /// <summary>A transparent, raycast-ON plate UNDER the posters that carries the swipe.
        /// It is the FIRST child so every poster and every button sits above it; the poster
        /// plates are raycast-OFF, so a drag that starts on a card still reaches this surface
        /// while a TAP on Accept / Read still belongs to the button. Same gesture family as the
        /// hero-select carousel (BeginDrag / EndDrag over a threshold), deliberately: one swipe
        /// idiom in the game, not a second one invented per screen.</summary>
        private void BuildSwipeSurface()
        {
            var go = ElarionUiKit.AddImage(_content, "SwipeSurface",
                new Vector2(0f, PanelFrac(PosterYMin)), new Vector2(1f, PanelFrac(PosterYMax)),
                new Color(0f, 0f, 0f, 0.002f), rounded: false);
            go.transform.SetAsFirstSibling();
            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var trigger = go.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.BeginDrag, e => BeginSwipe(e as PointerEventData));
            AddTrigger(trigger, EventTriggerType.EndDrag, e => EndSwipe(e as PointerEventData));
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type,
                                       UnityEngine.Events.UnityAction<BaseEventData> cb)
        {
            if (trigger == null) return;
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(cb);
            trigger.triggers.Add(entry);
        }

        private void BeginSwipe(PointerEventData e)
        {
            if (e == null) return;
            _trackingSwipe = true;
            _swipeStart = e.position;
        }

        private void EndSwipe(PointerEventData e)
        {
            if (!_trackingSwipe || e == null) return;
            _trackingSwipe = false;
            float dx = e.position.x - _swipeStart.x;
            if (Mathf.Abs(dx) < SwipeThresholdPx) return;
            // Same gesture family as hero-select: swipe left advances, swipe right goes
            // back. Both wrap. Owner felt-test 2026-08-27 asked for Previous; the swipe
            // must make the same trip the new face does, or the board has two paging
            // idioms.
            if (dx < 0f) NextPage();
            else PrevPage();
        }

        /// <summary>Host width for a head-row paging button, in reference px. MEASURED
        /// from the live font's glyph advances at FontBody, then divided by the kit's
        /// label inset so FitSingleLine never has to ellipsis ("Pr..."). Floored at
        /// MinTouchPx. Public so RumorBoardLayoutRegression pins the same number.</summary>
        public static float PageButtonWidthPx(string label)
        {
            if (string.IsNullOrEmpty(label)) return ElarionUiKit.MinTouchPx;
            float measured = ElarionUiKit.MeasureLineWidthPx(
                ElarionUiKit.FontRole.Body, label, ElarionUi.FontBody, out _);
            if (measured < 0f)
                measured = label.Length * ElarionUi.FontBody * 0.70f;
            float host = (measured * PageButtonBoldSlack) / PageButtonLabelInset;
            return Mathf.Max(ElarionUiKit.MinTouchPx, host);
        }

        private void BuildTitle()
        {
            // Right edge is Next's left, then inset by Previous's measured host plus two
            // head gaps, so the title never paints through Previous at any aspect (the
            // mockup TitleXMax of 0.600 overlaps a measured Previous at 1920x1080).
            float titleRightInset = PageButtonWidthPx(PreviousLabel) + 2f * HeadGapPx;
            var t = ElarionUiKit.Label(_content, "Brom's Rumor Board",
                PanelFrac(TitleYMin), PanelFrac(TitleYMax),
                ElarionUi.Gilt, ElarionUi.FontTitle, TMPro.TextAlignmentOptions.Left,
                PanelFrac(TitleXMin), PanelFrac(NextXMin), bold: true);
            t.gameObject.name = "BoardTitle";
            var titleRt = t.rectTransform;
            titleRt.offsetMax = new Vector2(-titleRightInset, titleRt.offsetMax.y);
            ElarionUiKit.FitSingleLine(t, ElarionUi.FontFloorMobile, 46f);
        }

        /// <summary>Previous in a FIXED-PIXEL host sized from the MEASURED label, hung off
        /// Next's left edge. Steps one page of three BACKWARD and WRAPS (the pair of Next;
        /// owner felt-test 2026-08-27: "A previous button would be nice").</summary>
        private void BuildPreviousButton()
        {
            float width = PageButtonWidthPx(PreviousLabel);
            var host = new GameObject("PreviousHost", typeof(RectTransform));
            host.transform.SetParent(_content, false);
            var rt = host.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(PanelFrac(NextXMin), PanelFrac(HeadTopY));
            rt.anchorMax = new Vector2(PanelFrac(NextXMin), PanelFrac(HeadTopY));
            rt.pivot = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(width, HeadBandPx);
            rt.anchoredPosition = new Vector2(-HeadGapPx, 0f);

            var previous = ElarionUiKit.BuildObsidianButton(host.transform, PreviousLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                Vector2.zero, Vector2.one, PrevPage);
            ApplyPosterButton(previous, primary: true);
        }

        /// <summary>Next &gt; in a FIXED-PIXEL head band. Advances one page of three and WRAPS
        /// (owner ruling: the keep-going form - no bottom arrows, no page dots).</summary>
        private void BuildNextButton()
        {
            var host = new GameObject("NextHost", typeof(RectTransform));
            host.transform.SetParent(_content, false);
            var rt = host.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(PanelFrac(NextXMin), PanelFrac(HeadTopY));
            rt.anchorMax = new Vector2(PanelFrac(NextXMax), PanelFrac(HeadTopY));
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, HeadBandPx);
            rt.anchoredPosition = Vector2.zero;

            var next = ElarionUiKit.BuildObsidianButton(host.transform, NextLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                Vector2.zero, Vector2.one, NextPage);
            ApplyPosterButton(next, primary: true);
        }

        private void BuildStatusBand()
        {
            var go = new GameObject("StatusLine", typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(_content, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(PanelFrac(Poster1XMin), 0f);
            rt.anchorMax = new Vector2(PanelFrac(Poster3XMax), 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, StatusBandPx);
            rt.anchoredPosition = new Vector2(0f, StatusBottomPx);

            _statusText = go.GetComponent<TMPro.TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(_statusText);
            _statusText.fontSize = ElarionUi.FontMicro;
            _statusText.color = ElarionUi.ParchmentDim;
            _statusText.alignment = TMPro.TextAlignmentOptions.Center;
            _statusText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            _statusText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            _statusText.raycastTarget = false;
            _statusText.text = _vm != null ? _vm.Status : "";
        }

        // -- Paint -----------------------------------------------------------------

        private void NextPage()
        {
            if (_vm != null) _vm.NextPage();
        }

        private void PrevPage()
        {
            if (_vm != null) _vm.PrevPage();
        }

        private void Repaint()
        {
            if (_posterHost == null || _vm == null) return;
            ClearChildren(_posterHost);

            var page = _vm.PageQuests;
            int shown = page != null ? page.Count : 0;
            for (int i = 0; i < shown && i < RumorBoardVM.PageSize; i++)
                BuildPoster(i, page[i].Id, page[i].Name);

            // An empty board says so, in words, in the space the posters would have used -
            // never a large dead-black region (the WO-866 lesson, kept).
            // STOP WO-1521 - THE GATE IS THE VM's `IsQuiet` (the whole LIST), NOT `shown` (this
            // PAGE). It read `shown == 0` and that is how "The board is quiet." painted while the
            // Journey card said one quest was ready to claim: an empty page is not an empty board.
            if (_vm.IsQuiet) BuildEmptyNote();

            // NEW is read while the posters are being built and cleared immediately after, so
            // a rumor wears the chip exactly once.
            _vm.MarkPageSeen();

            if (_statusText != null) _statusText.text = _vm.Status;
        }

        private static void ColumnFor(int index, out float xMin, out float xMax)
        {
            switch (index)
            {
                case 0: xMin = Poster1XMin; xMax = Poster1XMax; return;
                case 1: xMin = Poster2XMin; xMax = Poster2XMax; return;
                default: xMin = Poster3XMin; xMax = Poster3XMax; return;
            }
        }

        /// <summary>ONE self-contained rumor poster: overhanging TYPE TAG, optional NEW chip,
        /// a two-line title, a TWO-LINE hook (one line until WO-1636 measured it ellipsizing),
        /// the letter link, a reward chip row and its OWN
        /// Accept. No selection step exists anywhere on this board - the card the player reads
        /// is the card the player accepts.</summary>
        private void BuildPoster(int index, string id, string title)
        {
            if (string.IsNullOrEmpty(id)) return;
            ColumnFor(index, out float xMin, out float xMax);

            // The card plate: a gilt-dim edge over a dark fill (the kit's chip/plate language).
            // Both images are raycast-OFF so a drag across the card reaches the swipe surface.
            var card = ElarionUiKit.AddImage(_posterHost, "Poster_" + id,
                new Vector2(PanelFrac(xMin), PanelFrac(PosterYMin)),
                new Vector2(PanelFrac(xMax), PanelFrac(PosterYMax)),
                CardEdge, rounded: true);
            var cardImg = card.GetComponent<Image>();
            if (cardImg != null)
            {
                var cardFrame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
                if (cardFrame != null)
                {
                    cardImg.sprite = cardFrame;
                    cardImg.type = Image.Type.Simple;
                    cardImg.color = Color.white;
                }
                cardImg.raycastTarget = false;
            }

            var fill = ElarionUiKit.AddImage(card.transform, "Fill", Vector2.zero, Vector2.one,
                new Color(CardFill.r, CardFill.g, CardFill.b, 0.12f), rounded: true);
            var frt = fill.GetComponent<RectTransform>();
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            var fillImg = fill.GetComponent<Image>();
            if (fillImg != null) fillImg.raycastTarget = false;

            // Everything the overlay hides lives under one body root, so opening the letter is
            // one SetActive instead of a per-widget hunt (and so the oracle can never measure a
            // hidden poster against the overlay that covers it).
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(card.transform, false);
            var body = bodyGo.GetComponent<RectTransform>();
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = Vector2.zero;
            body.offsetMax = Vector2.zero;

            BuildTypeTag(body, _vm.TypeFor(id));
            if (_vm.IsNew(id)) BuildNewChip(body);

            var titleLabel = ElarionUiKit.Label(body, title ?? id, 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontBody, TMPro.TextAlignmentOptions.Top,
                CardSideFrac, 1f - CardSideFrac, bold: true);
            titleLabel.gameObject.name = "PosterTitle";
            HangTop((RectTransform)titleLabel.transform, TitleTopPx, TitleBandPx);
            titleLabel.textWrappingMode = TMPro.TextWrappingModes.Normal;
            titleLabel.alignment = TMPro.TextAlignmentOptions.Center;
            // TWO lines, fitted as a block: a title too long for two lines shrinks INSIDE its
            // band. It never clips a descender and it never runs into the hook. The ceiling is
            // TitleFontMaxPx, not a literal - TitleBandPx is budgeted from that same const, so the
            // band and the size that draws in it can no longer disagree (WO-1636).
            ElarionUiKit.FitBlock(titleLabel, ElarionUi.FontFloorMobile, TitleFontMaxPx);

            // WO-1521: the hook band carries the row's OBJECTIVE. For an offer that IS the
            // letter's hook (unchanged); for an ACTIVE quest it is the current stage's objective
            // and for a CLAIMABLE daily it is the finished job. The band is the same fixed-pixel
            // budget either way, so the layout law and its oracle are untouched.
            var hookLabel = ElarionUiKit.Label(body, _vm.ObjectiveFor(id), 0f, 1f,
                ElarionUi.ParchmentDim, ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center,
                CardSideFrac, 1f - CardSideFrac);
            hookLabel.gameObject.name = "PosterHook";
            HangTop((RectTransform)hookLabel.transform, HookTopPx, HookBandPx);
            // WO-1636 - TWO LINES, WRAPPED, FITTED AS A BLOCK. This was NoWrap + FitSingleLine
            // until the first glyph-oracle run (Builds/wave3-capture2) measured TMP swapping the
            // tail of eighteen hooks for an ellipsis: 27 of 62 printable glyphs at 1920x1080,
            // where the band is 451.2 ref px wide. The VM's word-boundary cut was never the
            // problem - the ONE-LINE budget was, and a single line of this band holds ~30 chars
            // against a 72-char cut. The only cut a player now sees is the VM's, on a word.
            hookLabel.textWrappingMode = TMPro.TextWrappingModes.Normal;
            // The VM already cut the hook at a SENTENCE or a WORD boundary, so this fit only
            // ever has to close a rounding gap. A hook can no longer end mid-word - which is
            // the one thing both failing captures did ("begun to sin", "lantern eels. Sh").
            ElarionUiKit.FitBlock(hookLabel, ElarionUi.FontFloorMobile, ElarionUi.FontMicro);

            string questId = id;
            var readHost = new GameObject("ReadHost", typeof(RectTransform));
            readHost.transform.SetParent(body, false);
            var readRt = readHost.GetComponent<RectTransform>();
            readRt.anchorMin = new Vector2(CardSideFrac, 1f);
            readRt.anchorMax = new Vector2(1f - CardSideFrac, 1f);
            readRt.pivot = new Vector2(0.5f, 1f);
            readRt.offsetMin = Vector2.zero;
            readRt.offsetMax = Vector2.zero;
            readRt.sizeDelta = new Vector2(0f, ReadBandPx);
            readRt.anchoredPosition = new Vector2(0f, -ReadTopPx);
            var read = ElarionUiKit.BuildObsidianButton(readHost.transform, "Read the letter >",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                Vector2.zero, Vector2.one, () => OpenLetter(card.transform, body, questId));
            ApplyPosterButton(read, primary: true);

            // Gilt hairline over the reward row - the same rule language as the rest of the kit.
            var rule = ElarionUiKit.AddImage(body, "RewardRule",
                new Vector2(CardSideFrac, 0f), new Vector2(1f - CardSideFrac, 0f),
                HairLine, rounded: false);
            var rrt = rule.GetComponent<RectTransform>();
            rrt.pivot = new Vector2(0.5f, 0f);
            rrt.sizeDelta = new Vector2(0f, RulePx);
            rrt.anchoredPosition = new Vector2(0f, RuleBottomPx);
            var ruleImg = rule.GetComponent<Image>();
            if (ruleImg != null) ruleImg.raycastTarget = false;

            BuildRewardRow(body, questId);

            var acceptHost = new GameObject("AcceptHost", typeof(RectTransform));
            acceptHost.transform.SetParent(body, false);
            var art = acceptHost.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(CardSideFrac, 0f);
            art.anchorMax = new Vector2(1f - CardSideFrac, 0f);
            art.pivot = new Vector2(0.5f, 0f);
            art.offsetMin = Vector2.zero;
            art.offsetMax = Vector2.zero;
            art.sizeDelta = new Vector2(0f, AcceptBandPx);
            art.anchoredPosition = new Vector2(0f, AcceptBottomPx);
            // WO-1521 - ONE DOOR PER POSTER, ITS FACE AND ITS DESTINATION BOTH CHOSEN BY THE VM.
            // The face is Claim / Go To / Accept and the tap goes to RumorBoardVM.Invoke, which
            // owns the branch. STOP Do NOT re-branch on the row kind here: a View that decides which
            // verb to call is how a CLAIM face ends up starting a quest.
            var action = ElarionUiKit.BuildObsidianButton(acceptHost.transform, _vm.ActionLabelFor(questId),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                Vector2.zero, Vector2.one, () => OnAction(questId));
            action.gameObject.name = "PosterAction";
            ApplyPosterButton(action, primary: true);
        }

        /// <summary>Hang a rect from its parent's TOP edge as a FIXED-PIXEL band.</summary>
        private static void HangTop(RectTransform rt, float topPx, float heightPx)
        {
            if (rt == null) return;
            float xMin = rt.anchorMin.x, xMax = rt.anchorMax.x;
            rt.anchorMin = new Vector2(xMin, 1f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, heightPx);
            rt.anchoredPosition = new Vector2(0f, -topPx);
        }

        /// <summary>The loudest thing on the card: a FILLED gold plate with INK text that
        /// OVERHANGS the card's top-left corner. It is separable by fill and by position, so it
        /// survives greyscale - the owner is red/green colourblind and a hue-only tag would be
        /// invisible to her.</summary>
        private void BuildTypeTag(RectTransform card, string typeName)
        {
            var plate = ElarionUiKit.AddImage(card, "TypeTag",
                new Vector2(0.02f, 1f), new Vector2(0.02f, 1f), ElarionUi.Gilt, rounded: true);
            var rt = plate.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(0f, TypeTagPx);
            rt.anchoredPosition = new Vector2(0f, TypeTagOverhangPx);
            var img = plate.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            string text = TypeTagLabel(typeName);
            var lbl = ElarionUiKit.Label(plate.transform, text, 0f, 1f,
                PlateInk, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
            lbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            ElarionUiKit.FitSingleLine(lbl, ElarionUi.FontFloorMobile, ElarionUi.FontLabel);

            // MEASURE the label (TMP's own metrics, valid before any layout pass) and size the
            // plate to it - a per-character guess is what over-asked by ~37% in the WO-866 RCA.
            float w = lbl.GetPreferredValues(text).x;
            if (w <= 1f) w = text.Length * 22f;
            rt.sizeDelta = new Vector2(w + 2f * ChipPadPx, TypeTagPx);   // height unchanged; only the ask grows
        }

        /// <summary>The display map from a quest's `type` field to the tag WORD. One map, in one
        /// place - the wording is a display concern and never a per-poster literal.</summary>
        private static string TypeTagLabel(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "MAIN";
            switch (typeName)
            {
                case "Gear": return "GEAR";
                case "Endgame": return "ENDGAME";
                case "Daily": return "DAILY";
                case "Side": return "SIDE";
                default: return "MAIN";
            }
        }

        private void BuildNewChip(RectTransform card)
        {
            var chip = ElarionUiKit.AddImage(card, "NewChip",
                new Vector2(0.98f, 1f), new Vector2(0.98f, 1f), ChipBorder, rounded: true);
            var rt = chip.GetComponent<RectTransform>();
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(150f, NewChipPx);
            rt.anchoredPosition = new Vector2(0f, TypeTagOverhangPx);
            var img = chip.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            var inner = ElarionUiKit.AddImage(chip.transform, "Fill", Vector2.zero, Vector2.one,
                ChipFill, rounded: true);
            var irt = inner.GetComponent<RectTransform>();
            irt.offsetMin = new Vector2(2f, 2f);
            irt.offsetMax = new Vector2(-2f, -2f);
            var innerImg = inner.GetComponent<Image>();
            if (innerImg != null) innerImg.raycastTarget = false;

            var lbl = ElarionUiKit.Label(inner.transform, "NEW", 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center,
                0.06f, 0.94f, bold: true);
            lbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            ElarionUiKit.FitSingleLine(lbl, ElarionUi.FontFloorMobile, ElarionUi.FontMicro);
        }

        /// <summary>The reward row: ONE chip per authored reward, never a fixed count and never
        /// a fixed set of labels. A currency reward renders as the kit's ICON + NUMBER chip
        /// (WO-1195 law - never a letter standing in for a resource); XP and a granted item
        /// render as WORDS, which is what the owner's mockup shows.</summary>
        private void BuildRewardRow(RectTransform card, string id)
        {
            var chips = _vm.RewardChipsFor(id);
            if (chips == null || chips.Count == 0) return;

            var rowGo = new GameObject("RewardRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(card, false);
            var rt = rowGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(CardSideFrac, 0f);
            rt.anchorMax = new Vector2(1f - CardSideFrac, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, RewardBandPx);
            rt.anchoredPosition = new Vector2(0f, RewardBottomPx);
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            // Reward rows can contain up to five authoritative grants. Share the row
            // evenly so one long word cannot push neighboring currency chips off-card.
            hlg.childControlWidth = true; hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = ChipSpacingPx;

            // Dense rewards reserve one explicit summary chip. Keeping three grants
            // plus the summary produced four equal slivers and reduced "XP 650" to
            // "X..." at 1920x1080. A player needs readable values more than a fourth
            // micro-chip, so overflow rows show two exact grants plus one summary.
            int visible = Mathf.Min(chips.Count, chips.Count > 3 ? 2 : 3);
            for (int i = 0; i < visible; i++)
            {
                var c = chips[i];
                if (c.IsCurrency) MakeCurrencyChip(rt, c);
                else MakeWordChip(rt, c.Text);
            }
            if (chips.Count > visible)
                MakeWordChip(rt, "+" + (chips.Count - visible) + " MORE");
        }

        /// <summary>Map the VM's neutral reward kind onto the kit's CurrencyKind. The kit then
        /// resolves the icon through <c>ElarionUiKit.ConceptIdFor</c>, which is the ONE
        /// translator to a concept id - re-deriving one here would be a second registry, and
        /// canon sec.7 records what that cost (the Stone row wearing the Food art).</summary>
        private static ElarionUiKit.CurrencyKind KitKind(RumorBoardVM.RewardKind kind)
        {
            switch (kind)
            {
                case RumorBoardVM.RewardKind.Crystals: return ElarionUiKit.CurrencyKind.Crystal;
                case RumorBoardVM.RewardKind.Wood: return ElarionUiKit.CurrencyKind.Wood;
                case RumorBoardVM.RewardKind.Iron: return ElarionUiKit.CurrencyKind.Iron;
                case RumorBoardVM.RewardKind.Magic: return ElarionUiKit.CurrencyKind.Wisdom;
                // WO-1521: a daily slot pays Wisdom directly, so the kind exists in its own right
                // now. It lands on the SAME CurrencyKind as Magic - one concept, one icon.
                case RumorBoardVM.RewardKind.Wisdom: return ElarionUiKit.CurrencyKind.Wisdom;
                // Canon sec.7: the authored `food` slot IS Stone, and CurrencyKind.Food is the
                // enum member that maps to the "stone" concept id.
                default: return ElarionUiKit.CurrencyKind.Food;
            }
        }

        /// <summary>The word the chip falls back to when the icon art is absent, so a reward
        /// chip is never a naked number (colourblind law).</summary>
        private static string KindTag(RumorBoardVM.RewardKind kind)
        {
            switch (kind)
            {
                case RumorBoardVM.RewardKind.Crystals: return "Crystals";
                case RumorBoardVM.RewardKind.Wood: return "Wood";
                case RumorBoardVM.RewardKind.Iron: return "Iron";
                case RumorBoardVM.RewardKind.Magic: return "Magic";
                case RumorBoardVM.RewardKind.Wisdom: return "Wisdom";
                default: return "Stone";
            }
        }

        private static void MakeCurrencyChip(RectTransform row, RumorBoardVM.RewardChipVM chip)
        {
            var handle = ElarionUiKit.CurrencyChip(row, KitKind(chip.Kind),
                Vector2.zero, Vector2.one, primary: false, tag: KindTag(chip.Kind));
            if (handle == null || handle.root == null) return;
            handle.root.name = "RewardChip_" + chip.Kind;
            // A layout-group child must NOT carry stretch anchors: the group writes sizeDelta,
            // which a 0..1 anchor pair then re-interprets as a DELTA from the parent's size and
            // the chip resolves to the whole row. Collapse to a centre anchor first.
            var chipRt = handle.root.GetComponent<RectTransform>();
            if (chipRt != null)
            {
                chipRt.anchorMin = new Vector2(0.5f, 0.5f);
                chipRt.anchorMax = new Vector2(0.5f, 0.5f);
                chipRt.pivot = new Vector2(0.5f, 0.5f);
            }
            var le = handle.root.GetComponent<LayoutElement>();
            if (le == null) le = handle.root.AddComponent<LayoutElement>();
            le.preferredHeight = ChipHeightPx;
            le.preferredWidth = 0f;
            le.flexibleWidth = 1f;
            handle.SetAmount(chip.Amount, animate: false);
        }

        /// <summary>ONE word chip: gilt-dim border, obsidian fill, parchment micro label, sized
        /// from its label's MEASURED width and FITTED inside its own borders - so a crowded row
        /// shrinks text INSIDE the chip instead of painting it across the next one.</summary>
        private static void MakeWordChip(RectTransform row, string text)
        {
            if (row == null || string.IsNullOrEmpty(text)) return;

            var chip = ElarionUiKit.AddImage(row, "RewardChip_Word",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ChipBorder, rounded: true);
            var le = chip.AddComponent<LayoutElement>();
            le.preferredHeight = ChipHeightPx;
            le.flexibleWidth = 1f;
            le.minWidth = 0f;
            var borderImg = chip.GetComponent<Image>();
            if (borderImg != null) borderImg.raycastTarget = false;

            var fill = ElarionUiKit.AddImage(chip.transform, "Fill", Vector2.zero, Vector2.one,
                ChipFill, rounded: true);
            var frt = fill.GetComponent<RectTransform>();
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            var fillImg = fill.GetComponent<Image>();
            if (fillImg != null) fillImg.raycastTarget = false;

            string display = text.EndsWith(" Drop", System.StringComparison.OrdinalIgnoreCase)
                ? text.Substring(0, text.Length - 5) : text;
            var lbl = ElarionUiKit.Label(fill.transform, display, 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center, 0f, 1f);
            lbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            // WO-1636 - THE CHIP CLAIMS THE WIDTH ITS OWN WORD NEEDS.
            // This line read `le.preferredWidth = 0f;` and that ZERO is the proven cause of the
            // first glyph-oracle run's reward finding: "A found item" drew 4 of 10 printable
            // glyphs in a 73 ref px label at 1920x1080 (Builds/wave3-capture2). A currency chip
            // in the SAME row does claim one - ElarionUiKit's CurrencyChipHandle.SyncPreferredWidth
            // (ElarionUiKitObsidian.cs:836-845) writes preferredWidth from its amount text on every
            // SetAmount, and MakeCurrencyChip above calls SetAmount. So the word chip was the only
            // child of the HorizontalLayoutGroup asking for nothing, and the group gave it what was
            // left. MEASURE the word instead, through the kit's own glyph-advance measurer (the
            // PageButtonWidthPx idiom, including its fallback when no font resolves), and add the
            // chip's two side pads. minWidth stays 0 on purpose: a crowded row must shrink these
            // chips PROPORTIONALLY inside the card, never overflow it.
            float measured = ElarionUiKit.MeasureLineWidthPx(
                ElarionUiKit.FontRole.Body, display, ElarionUi.FontMicro, out _);
            if (measured < 0f) measured = display.Length * ElarionUi.FontMicro * 0.70f;
            le.preferredWidth = measured + 2f * ChipPadPx;
            float floor = display.EndsWith(" MORE", System.StringComparison.OrdinalIgnoreCase)
                ? 18f : ChipMinFontPx;
            ElarionUiKit.FitSingleLine(lbl, floor, ElarionUi.FontMicro);
        }

        // -- The letter overlay -----------------------------------------------------

        /// <summary>"Read the letter &gt;" - a FULL-CARD overlay carrying the whole prose in its
        /// own scrolling well plus a Back face. The board itself never shows dense copy, which
        /// is the owner's ruling ("less detail, more simple concept") and also why nothing on
        /// the poster has to truncate. The poster body is DEACTIVATED while the overlay is up:
        /// two live surfaces stacked in one rect is the exact class of defect this rebuild
        /// exists to remove, and a deactivated body cannot be measured against the overlay.</summary>
        private void OpenLetter(Transform card, RectTransform body, string id)
        {
            if (card == null || _vm == null) return;
            if (body != null) body.gameObject.SetActive(false);

            var overlay = ElarionUiKit.AddImage(card, "LetterOverlay", Vector2.zero, Vector2.one,
                PlateInk, rounded: true);
            var ort = overlay.GetComponent<RectTransform>();
            ort.offsetMin = new Vector2(2f, 2f);
            ort.offsetMax = new Vector2(-2f, -2f);
            var overlayImg = overlay.GetComponent<Image>();
            if (overlayImg != null) overlayImg.raycastTarget = true;   // the overlay eats board taps

            // The scrolling well. A masked viewport with a content-fitted block: the letter is
            // ALWAYS fully available and NEVER truncated - an obviously scrollable body reads as
            // a design, a clipped word reads as a bug.
            var viewGo = new GameObject("LetterViewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            viewGo.transform.SetParent(overlay.transform, false);
            var view = viewGo.GetComponent<RectTransform>();
            view.anchorMin = new Vector2(CardSideFrac, 0f);
            view.anchorMax = new Vector2(1f - CardSideFrac, 1f);
            view.offsetMin = new Vector2(0f, AcceptBottomPx + AcceptBandPx + 16f);
            view.offsetMax = new Vector2(0f, -24f);

            var contentGo = new GameObject("LetterContent",
                typeof(RectTransform), typeof(TMPro.TextMeshProUGUI), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var letter = contentGo.GetComponent<TMPro.TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(letter);
            letter.text = _vm.LetterFor(id);
            letter.fontSize = ElarionUi.FontLabel;
            letter.color = ElarionUi.Parchment;
            letter.alignment = TMPro.TextAlignmentOptions.TopLeft;
            letter.textWrappingMode = TMPro.TextWrappingModes.Normal;
            letter.overflowMode = TMPro.TextOverflowModes.Overflow;   // it SCROLLS; it never ellipsizes
            letter.raycastTarget = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewGo.GetComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            var backHost = new GameObject("BackHost", typeof(RectTransform));
            backHost.transform.SetParent(overlay.transform, false);
            var brt = backHost.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(CardSideFrac, 0f);
            brt.anchorMax = new Vector2(1f - CardSideFrac, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            brt.sizeDelta = new Vector2(0f, AcceptBandPx);
            brt.anchoredPosition = new Vector2(0f, AcceptBottomPx);
            var overlayGo = overlay;
            var back = ElarionUiKit.BuildObsidianButton(backHost.transform, "Back",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                Vector2.zero, Vector2.one, () => CloseLetter(overlayGo, body));
            ApplyPosterButton(back, primary: true);
        }

        private void CloseLetter(GameObject overlay, RectTransform body)
        {
            if (body != null) body.gameObject.SetActive(true);
            SafeDestroy(overlay);
        }

        // The handoff button sources carry wide ornamental end caps. Their imported
        // nine-slice borders are intentionally large for full-width CTAs, but collapse
        // when used by this board's compact poster/head controls. Simple scaling keeps
        // the complete authored silhouette visible at every supported landscape ratio.
        private static void ApplyPosterButton(Button button, bool primary)
        {
            MedievalUiSkin.ApplyButton(button, primary);
            ForceSimpleArtwork(button);
            var label = button != null ? button.GetComponentInChildren<TMPro.TMP_Text>() : null;
            if (label != null) ElarionUiKit.FitSingleLine(label, ElarionUi.FontFloorMobile, 36f);
        }

        private static void ForceSimpleArtwork(Button button)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image != null) image.type = Image.Type.Simple;
        }

        // -- Empty state ------------------------------------------------------------

        /// <summary>A board with nothing on it says so, in words, in the middle column. The old
        /// panel left the same region flat black, which reads as a broken screen rather than an
        /// early one.</summary>
        private void BuildEmptyNote()
        {
            var plate = ElarionUiKit.AddImage(_posterHost, "EmptyBoard",
                new Vector2(PanelFrac(Poster2XMin), PanelFrac(PosterYMin)),
                new Vector2(PanelFrac(Poster2XMax), PanelFrac(PosterYMax)),
                CardEdge, rounded: true);
            var img = plate.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            var fill = ElarionUiKit.AddImage(plate.transform, "Fill", Vector2.zero, Vector2.one,
                CardFill, rounded: true);
            var frt = fill.GetComponent<RectTransform>();
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            var fillImg = fill.GetComponent<Image>();
            if (fillImg != null) fillImg.raycastTarget = false;

            var head = ElarionUiKit.Label(plate.transform, "The board is quiet.", 0.52f, 0.62f,
                ElarionUi.Gilt, ElarionUi.FontBody, TMPro.TextAlignmentOptions.Center,
                CardSideFrac, 1f - CardSideFrac, bold: true);
            ElarionUiKit.FitSingleLine(head, ElarionUi.FontFloorMobile, ElarionUi.FontBody);

            var note = ElarionUiKit.Label(plate.transform, "Brom posts more as Elarion wakes.",
                0.40f, 0.50f, ElarionUi.ParchmentDim, ElarionUi.FontMicro,
                TMPro.TextAlignmentOptions.Center, CardSideFrac, 1f - CardSideFrac);
            ElarionUiKit.FitBlock(note, ElarionUi.FontFloorMobile, ElarionUi.FontMicro);
        }

        // -- Commands ---------------------------------------------------------------

        /// <summary>The ONE poster door. WO-1521: the VM's Invoke picks Claim / Go To / Accept
        /// from the row's kind, so this View never has to know which. The status the VM writes is
        /// toasted either way - a claim that credited nothing must SAY so, not fail quietly.</summary>
        private void OnAction(string id)
        {
            var vm = _vm;
            if (vm == null) return;
            // GO TO closes the board, and Close() nulls _vm - so hold the VM in a local or the
            // message the player most needs ("Tracking X - the objective is pinned to your HUD")
            // is the one that never gets toasted. Dispose only unsubscribes; Status stays readable.
            vm.Invoke(id);   // Claim / GoTo / Accept + status; the VM raises Changed -> Repaint
            if (!string.IsNullOrEmpty(vm.Status))
                ElarionUiKit.ShowToast(vm.Status, ElarionUiKit.ToastTone.Info);
        }

        // -- Helpers ----------------------------------------------------------------

        /// <summary>Destroys immediately in edit mode (the UICaptureLaunch headless-screenshot
        /// path repaints without Play - runtime Destroy is edit-illegal), normally in play.</summary>
        private static void SafeDestroy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private static void ClearChildren(RectTransform host)
        {
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var c = host.GetChild(i);
                if (c != null) SafeDestroy(c.gameObject);
            }
        }
    }
}
