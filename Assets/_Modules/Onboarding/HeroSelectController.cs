// =============================================================================
// HeroSelectController — drives the hero-select screen (intro flow).
// -----------------------------------------------------------------------------
// THE SCREEN (WO-C conversion 2026-07-03, coverage matrix row #18; layout per the
// owner's pinned Blink CHARACTER-CREATION design, canon memory hero-select-blink-
// creation-carousel / WO-559):
//   CHROME     : the Blink Obsidian master frame (FrameCore = Core_Panel) via
//                ElarionUiKit.BuildObsidianPanel — code-built uGUI, NO UIDocument,
//                (owner F8 2026-07-03: swapped OFF FrameCharacter/Stats_Panel — its
//                 arch-constrained body zone [y 0.110-0.605] cramped the 3-column
//                 creation layout and pushed the confirm CTA out of the frame; FrameCore
//                 [body y 0.075-0.855] gives the full-height well the layout needs.)
//                NO UXML, NO borrowed PanelSettings. The kit's shared Close is
//                HIDDEN (this is a forced-flow screen; confirm is the only exit).
//
//   ★ LAYOUT REBUILT BY WO-1083 (2026-08-26, owner-approved mockup
//     WorkOrders/WORK_ORDER_1083_mockup_2670x1200.png). It used to be three side-by-
//     side columns (classes LEFT / hero CENTER / specs RIGHT) with the CTA squeezed
//     into the same band as the arrows — which on the Seeker's 2670x1200 produced
//     five overlaps and left the bottom third of the frame empty. It is now BANDED
//     top-to-bottom inside ONE stage well. The band table is the const block at the
//     top of the class; read THAT, not the call sites.
//   TOP BAND   : the ROTATING CAROUSEL — rotate-prev | side card | FOCAL card |
//                side card | rotate-next. The focal card is larger AND gold-framed;
//                the two side cards are smaller, lower and dimmed (that size
//                difference IS the rotation cue, and it survives greyscale). A locked
//                hero's side card carries a SOON word-ribbon. All four heroes are in
//                the ring; wrap-around. Three inputs rotate one step: swipe, the
//                outboard rotate control, side-card tap.
//                WO-1248: the rotate control is a designed ICON+word (large ASCII
//                chevron over PREV / NEXT), NEVER a kit word-button in a 0.068 lane
//                — that is what truncated "Previous" to "Pr...".
//   MID BAND   : role label under the focal card, then the page-dot rail (DISPLAY
//                ONLY — never tappable, so nothing here is under MinTouchPx), then
//                a divider rule.
//   LOWER BAND : the DETAILS STRIP — the SAME four sections as the old right-hand
//                specs rail, laid ACROSS in four columns: LORE | STATS (HP/ATTACK/
//                SPEED pip rows, uGUI image pips — NO unicode glyphs in TMP) |
//                SIGNATURE | PRIMARY SKILLS. Content and data sources are unchanged.
//   BOTTOM     : the single confirm CTA (Obsidian GREEN) in an EXCLUSIVE band no
//                other element may enter — "Choose <Hero>" enabled on the playable
//                hero, disabled "Coming Soon" on a locked preview.
//
//   ROSTER     : selectable-to-confirm == in PlayableHeroes.All, which since
//                2026-08-05 is Grom/Sylas/Thrain (ff.knightonly defaults OFF). A
//                non-playable class - today only the Cleric, Elara, who has no
//                authored kit - is still TAPPABLE for a PREVIEW but renders visibly
//                locked (SOON ribbon / LOCKED scrim, "Abilities revealed at launch",
//                CTA disabled). ⛔ WO-1083 §3: that Cleric behaviour is CORRECT AS
//                SHIPPED and four cards / four dots is INTENDED — do not "fix" it.
//
// WHY CODE-BUILT (history, preserved): the original screen bound to named UXML
// elements and blanked whenever the UXML failed to instantiate in a player build
// (CLAUDE.md §8). The first fix rebuilt the tree in code but still hosted on a
// UIDocument/UITK. This conversion finishes the job: the whole screen is kit uGUI
// (HelpMenu is the reference conversion), so neither UXML nor PanelSettings can
// break it. Any UIDocument left on the host GameObject is disabled at build time.
//
// COPY: hero name / role / blurb resolve from en.json via CanonStrings at runtime
// (port-spec Part 4). Stats / ability / skills come from HeroCatalog (pure
// presentation data mirrored from abilities.json, legitimately in C#).
//
// PERSISTENCE + ROUTING (contract preserved EXACTLY from the carousel build):
//   * on confirm, GameStateService.ChooseHero(hero) writes GameState.HeroClass
//     and Save()s it;
//   * FeatureFlags.BypassPetSelect ON (default) -> SceneRouter.GoCastle();
//     flag OFF -> SceneRouter.GoPetSelect() (the reversibility hatch);
//   * the playable hero is PRE-persisted on build so GameState always has a
//     valid class even if the player confirms without navigating;
//   * a save that already records a hero self-skips straight to the Castle.
//
// Lives in DeNelle.Onboarding; references DeNelle.Core only — module isolation.
// =============================================================================

using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Drives the hero-select screen (WO-C uGUI conversion of the WO-559 design):
    /// builds — entirely in code, on the Blink Obsidian FrameCharacter chrome — a
    /// top-band rotating carousel over the <see cref="HeroCatalog"/> entries, the
    /// role/page-dot strip, and a four-column details strip, with a single green
    /// confirm CTA in an exclusive bottom band. Every class is tappable for a preview;
    /// only the playable hero (Grom == Knight) is confirmable — locked classes show
    /// a "Coming soon" tag and a LOCKED stage scrim with the CTA disabled. On
    /// confirm it writes <see cref="GameState.HeroClass"/> and routes to the home
    /// hub (or PetSelect when <c>FeatureFlags.BypassPetSelect</c> is OFF). A
    /// returning player who already chose a hero is skipped straight to the Castle.
    /// </summary>
    public sealed class HeroSelectController : MonoBehaviour
    {
        // -- en.json keys for the screen's own copy --------------------------
        private const string TitleKey    = "heroSelect.title";
        private const string SubtitleKey = "heroSelect.subtitle";
        // The confirm CTA en.json key (falls back to "Enter Elarion").
        private const string DiveKey     = "heroSelect.diveVillage";

        [Header("Behaviour")]
        [Tooltip("Skip straight to the Castle when the save already records a hero " +
                 "(a returning player who finished the intro). Editor testing: " +
                 "disable to always show the screen.")]
        [SerializeField] private bool _skipWhenIntroComplete = true;

        // -- Built UI (all created in code; one kit canvas per open) ----------
        private GameObject _canvas;                       // the kit modal canvas root
        private ElarionUiKit.PanelChrome _chrome;         // Blink FrameCharacter chrome
        private RectTransform _stageWell;                 // WO-1083 — THE full-height stage well
        private RectTransform _classColumn;               // TOP band — the rotating carousel
        private RectTransform _stageCenter;               // focal card column (rebuilt per pick)
        private RectTransform _stageRight;                // details strip (rebuilt per pick)
        private Button _confirmButton;                    // footer CTA (Obsidian Green)
        private TextMeshProUGUI _confirmLabel;            // the CTA's kit label (retext per pick)
        private readonly Button[] _carouselCards = new Button[2];
        private readonly Image[] _carouselPortraits = new Image[2];
        private readonly TextMeshProUGUI[] _carouselLabels = new TextMeshProUGUI[2];
        private readonly GameObject[] _carouselSoon = new GameObject[2];
        private RectTransform _dotRail;

        // =====================================================================
        //  WO-1083 — THE BAND TABLE (the whole layout lives here, in ONE place)
        // ---------------------------------------------------------------------
        //  Every number below is a fraction of THE STAGE WELL (Well* consts) or of
        //  the band that owns the element (Car* / Det* consts). Authoring the bands
        //  here — rather than sprinkling literals down the builders — is what makes
        //  the no-overlap invariant checkable: read the table, not the call sites.
        //
        //  ⛔ WHY THE WELL IS *NOT* `_chrome.layout.body` (measured, not inferred):
        //  BuildObsidianPanel's CLOSE-BAND RESERVATION raises FrameCore's body zone
        //  floor from the frame-measured 0.075 to ~0.353 of the panel on a landscape
        //  canvas (ElarionUiKit.cs:628-677 — footer relocation 0.155->0.2075, then
        //  bodyFloor = footer.w + 0.015). At 2670x1200 that leaves a well only 875 px
        //  tall whose BOTTOM edge is screen y=770 of 1200 — which IS defect #5 of the
        //  WO ("bottom ~35% of the panel is empty while everything crams into the top
        //  half"), and it is also why the CTA had to be crushed into the same band as
        //  the arrows and the skill rows (defects #1 and #2). The reservation exists to
        //  keep content off the SHARED CLOSE — and this screen HIDES the shared Close
        //  (forced flow, confirm is the only exit) and uses no footer zone, so there is
        //  nothing to reserve for. We therefore anchor our own well at FrameCore's
        //  frame-MEASURED body rect, which is exactly the mockup's inner area.
        private const float WellXMin = 0.055f, WellXMax = 0.945f;   // == ZonesFor(FrameCore).body x
        private const float WellYMin = 0.075f, WellYMax = 0.835f;   // == ZonesFor(FrameCore).body y, UNreserved

        // Vertical bands of the well (0 = well bottom, 1 = well top). Disjoint by construction.
        private const float CtaBandTop     = 0.185f;   // ⛔ EXCLUSIVE band — nothing else may enter
        private const float CtaBtnYMin     = 0.015f, CtaBtnYMax = 0.180f;
        private const float DetailsYMin    = 0.205f, DetailsYMax = 0.430f;
        private const float DividerY       = 0.440f;
        private const float DotRailYMin    = 0.452f, DotRailYMax = 0.500f;
        private const float CarouselYMin   = 0.500f;   // -> 1.000 (the top band)

        // Horizontal lanes of the carousel band (fractions of the well's width).
        // WO-1248: the rotate lanes used to be 0.068 of the well (Prev 0.148-0.216 /
        // Next 0.784-0.852). At portrait 1080x1920 that is a ~63 px plate; the kit
        // button's 0.92 label inset leaves ~58 px, which is how "Previous" became
        // "Pr..." (NoWrap + Ellipsis). The new lanes are sized so BOTH axes clear
        // MinTouchPx(112) at portrait 1080x1920 AND landscape 2670x1200, and stay
        // disjoint from the side cards (SideLXMin / SideRXMax) by construction.
        private const float PrevXMin = 0.012f, PrevXMax = 0.215f;
        private const float SideLXMin = 0.225f, SideLXMax = 0.405f;
        private const float FocalXMin = 0.4353f, FocalXMax = 0.5650f;
        private const float SideRXMin = 0.595f, SideRXMax = 0.775f;
        private const float NextXMin = 0.785f, NextXMax = 0.988f;

        // ── WO-1234 — WHAT THE DELIVERED ART IS, declared once ───────────────────────
        // The owner's 2026-08-26 delivery is a FULL CARD, not a bare portrait: it bakes
        // in the gold frame AND a name/role plate ("SYLAS / Ranger"). Two pieces of
        // chrome this screen used to draw are therefore now DUPLICATES, and one — the
        // separate name/role band — is what let the LOCKED scrim leave Elara's plate
        // uncovered. This ONE const gates both, so a future bare-portrait delivery
        // restores the frame and the LOCALISED labels together by flipping it to false;
        // nothing was deleted. See BuildCenterStage for the localisation note.
        // ⚠ static readonly, NOT const, deliberately: a const bool would constant-fold every
        // gated branch below and bury the bare-portrait path under CS0162 unreachable-code
        // warnings — noise that trains a seat to ignore the warning list. This keeps both
        // paths compiled and inspectable.
        private static readonly bool PortraitArtIsFullCard = true;

        // Aspect of that art, width/height. 1086x1448 = 3:4 (it was 832x1248 = 2:3).
        // ⛔ The focal card rect is derived from THIS by an AspectRatioFitter, never by a
        // hardcoded band fraction — a fraction is only correct at one canvas aspect, and
        // the wrong one letterboxes the card inside its own rect.
        private const float PortraitArtAspect = 1086f / 1448f;

        // Vertical lanes INSIDE the carousel band (0 = band bottom, 1 = band top).
        private const float CarSideYMin = 0.42f, CarSideYMax = 0.92f;   // side cards: smaller + lower
        // WO-1248: taller than the old 0.53-0.86 strip so the rotate plate's SHORTEST
        // side stays >= MinTouchPx on landscape (where height is the scarce axis).
        private const float CarArrowYMin = 0.42f, CarArrowYMax = 0.90f;

        // Full words now use the same shared medieval button recipe as every other
        // action on the screen. The widened lanes above provide enough room for the
        // canonical font without ellipsis at the supported mobile ratios.
        private const string RotatePrevLabel = "PREVIOUS";
        private const string RotateNextLabel = "NEXT";

        // The four details columns (fractions of the well's width).
        //
        // ── WO-1636: RE-PROPORTIONED FROM MEASURED GLYPH COUNTS, NOT FROM TASTE ──────────
        // The glyph oracle's first run (Builds/wave3-capture2, 2026-09-10) reported FIVE cut
        // captions on this screen, ALL of them in this strip and ALL of them only at PORTRAIT
        // 1080x1920 (the 1920x1080 and 2670x1200 shots of the same screen are clean, because
        // the well is ~1.8x wider there). Measured, from the run's own rects:
        //
        //   Col_Signature/Label  "Shield Bash"     7 of 10 glyphs  x 151.8..277.1 (125.3 px) @25
        //   Col_Skills/Label     "Sword Heroic"    7 of 11         x 344..458.3   (114.3 px) @20
        //   Col_Skills/Label     "Shield Bash"     8 of 10         (same lane)
        //   Col_Skills/Label     "Warden's Grace"  8 of 13         (same lane)
        //   Col_Skills/Label     "Radiant Strike"  8 of 13         (same lane)
        //
        // THE WELL IS 932.0 REFERENCE PX WIDE AT 1080x1920, and that is DERIVED FROM THOSE TWO
        // RECTS, not assumed: SIGNATURE's label at 0.6628..0.7972 of the well back-solves to
        // 151.9..277.0 against a 932 px well centred on 0, and SKILLS' name lane at
        // 0.820+0.28*0.175 .. 0.820+0.98*0.175 back-solves to 343.9..458.0. Two independent
        // labels agreeing to a tenth of a pixel is the corroboration.
        //
        // The old split gave SIGNATURE 0.140 (130 px) and SKILLS 0.175 (163 px) while LORE and
        // STATS took 0.615 between them. Both narrow columns carry PROPER NOUNS out of
        // HeroCatalog - the longest are "Sacred Mending" (Cleric signature) and "Storm of
        // Arrows" / "Warden's Grace" (skill names) - and a name cannot be wrapped, hyphenated
        // or shortened: it is mirrored verbatim from abilities.json and HeroKitMirrorRegression
        // pins that mirror. So the BAND had to move, and it moves toward the two columns that
        // hold names and away from the two that hold a wrapped blurb and image pips.
        //
        // WIDTHS AFTER, in reference px at 1080x1920 (well 932):
        //   LORE    0.262 -> 244.2 px  (was 0.295 -> 275.0)   blurb band grown 0.80 -> 0.84 to compensate
        //   STATS   0.262 -> 244.2 px  (was 0.320 -> 298.2)   pip key lane widened 0.28 -> 0.30 of the column
        //   SIG     0.210 -> 195.7 px  (was 0.140 -> 130.5)   label lane 125.3 -> 187.9 px  (+50%)
        //   SKILLS  0.232 -> 216.2 px  (was 0.175 -> 163.1)   name lane  114.3 -> 165.4 px  (+45%)
        // The three inter-column gaps drop 0.020 -> 0.010 and the outer margins 0.005 -> 0.002,
        // which is where the last ~19 px came from; the columns stay DISJOINT by construction.
        //
        // ⛔ NO FONT FLOOR WAS LOWERED. The signature name still floors at 25 and the skill names
        // at 20 (FitLine below, fontSize*0.5 of FontBody 50 / FontLabel 40), which is what the
        // oracle measured them resolving to. Widening the band is the whole fix.
        //
        // ⚠ THE LORE AND STATS FIGURES ARE ESTIMATES, AND THAT IS RECORDED ON PURPOSE. The
        // oracle logs only the labels it FAILS, so this run carries no rect for the blurb or the
        // pip keys; their new widths were sized from a glyph-advance estimate calibrated against
        // the two rects above (longest EN blurb = hero.ranger.blurb, 168 chars; longest pip key
        // = "ATTACK"). The re-capture is the proof, not this comment.
        private static readonly Vector2[] DetailColumns =
        {
            new Vector2(0.002f, 0.264f),   // LORE
            new Vector2(0.274f, 0.536f),   // STATS
            new Vector2(0.546f, 0.756f),   // SIGNATURE
            new Vector2(0.766f, 0.998f),   // PRIMARY SKILLS
        };
        private Image[] _pageDots;
        private Vector2 _swipeStart;
        private bool _trackingSwipe;
        private const float SwipeThresholdPx = 72f;

        private bool _built;
        private bool _hasSelection;
        private HeroClass _selectedHero;

        // Which catalog slot is on screen (index into HeroCatalog.Heroes).
        private int _shownIndex;

        // Portrait JPGs import as Texture2D rather than Sprite. Convert each once so every
        // carousel lane uses Image.preserveAspect; RawImage stretched the art to its card.
        // (WO-1234: that art is now 3:4 card art, PortraitArtAspect — see the band table.)
        private static readonly Dictionary<HeroClass, Sprite> PortraitSpriteCache =
            new Dictionary<HeroClass, Sprite>();

        // WO-861 Phase 0: the playable set is no longer hardcoded HERE. It comes from the
        // ONE roster truth, DeNelle.Core.State.PlayableHeroes, which the save service and
        // the vendor shelf also read — so a hero cannot be selectable on this screen while
        // the store still thinks he does not exist. That set is { Knight, Ranger, Mage }
        // since the 2026-08-05 unlock (ff.knightonly now defaults OFF) - the screen widened
        // with the flag and needed no layout code change, exactly as designed (the locked
        // tag / stage scrim / CTA state all derive from IsPlayable).
        //
        // The screen OPENS on, and pre-persists, PlayableHeroes.Default (Grom == Knight).
        private static HeroClass DefaultHero => PlayableHeroes.Default;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void OnEnable()
        {
            // A returning player who already finished the intro skips this
            // screen entirely — route on before building any UI.
            if (_skipWhenIntroComplete && IsIntroComplete())
            {
                SceneRouter.GoCastle();
                return;
            }

            BuildScreen();
        }

        private void OnDisable()
        {
            if (_canvas != null) Destroy(_canvas);
            _canvas = null;
            _chrome = null;
            _confirmButton = null;
            _confirmLabel = null;
            _dotRail = null;
            _pageDots = null;
            _stageWell = null;
            for (int i = 0; i < _carouselCards.Length; i++)
            {
                _carouselCards[i] = null;
                _carouselPortraits[i] = null;
                _carouselLabels[i] = null;
                _carouselSoon[i] = null;
            }
            _trackingSwipe = false;
            _built = false;
        }

        // =====================================================================
        //  Returning-player gate
        // =====================================================================

        /// <summary>
        /// True when the save already records a chosen hero — the intro flow is
        /// finished and this screen has nothing to ask. The gate is HeroClass alone
        /// (the pet-select step is gone for single-hero V1).
        /// </summary>
        private static bool IsIntroComplete()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null) return false; // first launch
            return svc.State.HeroClass != HeroClassOpt.None;
        }

        // =====================================================================
        //  Code-built layout — kit uGUI on the Blink master frame
        // =====================================================================

        /// <summary>
        /// Builds the entire hero-select screen in code on a fresh kit canvas:
        ///   canvas (ScreenSpaceOverlay, kit-built)
        ///     └─ Obsidian FrameCore chrome (title in the header zone; Close hidden)
        ///          ├─ subtitle eyebrow (sub-header band)
        ///          └─ HeroStageWell (WO-1083 — the frame-measured body rect), banded:
        ///               ├─ TOP     HeroCarousel — rotate | side card | FOCAL card | side card | rotate
        ///               ├─         (inside the focal column: portrait well, hero name, role label)
        ///               ├─ MID     page-dot rail, then the divider rule
        ///               ├─ LOWER   DetailsStrip — LORE | STATS | SIGNATURE | PRIMARY SKILLS
        ///               └─ BOTTOM  the EXCLUSIVE CTA band — the confirm CTA (Obsidian Green)
        /// No UIDocument, no UXML, no PanelSettings — nothing scene-hosted can blank it.
        /// </summary>
        private void BuildScreen()
        {
            using var _ = FlowTrace.Enter("Onboarding", "HeroSelectController.BuildScreen");

            // The IntroFlowSceneBuilder host GameObject may still carry the legacy
            // UIDocument (+ its UXML). Disable it so nothing paints under/over the
            // kit canvas — this screen no longer renders through UITK at all.
            var legacyDoc = GetComponent<UnityEngine.UIElements.UIDocument>();
            if (legacyDoc != null)
            {
                legacyDoc.enabled = false;
                FlowTrace.Step("Onboarding", "BuildScreen: legacy UIDocument disabled (uGUI conversion owns the screen).");
            }

            // Kit canvas — scene-owned (destroyed by the routing scene-load / OnDisable).
            _canvas = ElarionUiKit.BuildModalCanvas("HeroSelectUI", 5000);
            if (_canvas == null)
            {
                // P0 — no canvas means hero-select renders NOTHING (a blank screen the
                // player can't pass). Fail-loud to the break-log rather than a quiet warn.
                FlowTrace.Fail("Onboarding", "BuildScreen: kit canvas FAILED to build — hero-select will NOT display (BLANK SCREEN).");
                return;
            }

            // Blink master-frame chrome. onClose: null — forced flow; we also hide
            // the shared Close below (confirm is the only exit).
            _chrome = ElarionUiKit.BuildObsidianPanel(_canvas.transform,
                FallbackLocale(TitleKey, "Choose Your Hero"),
                new Vector2(0.015f, 0.02f), new Vector2(0.985f, 0.98f), onClose: null,
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "crest");
            MedievalUiSkin.ApplyShell(_chrome, compact: false);
            if (_chrome.close != null) _chrome.close.gameObject.SetActive(false);
            if (_chrome.layout != null && _chrome.layout.medallion != null)
                _chrome.layout.medallion.gameObject.SetActive(false);

            // W9 (WO-714): the kit's shared open ease (PanelOpenCloseFx, P8) on the
            // master-frame chrome — the same eased scale+fade every kit panel opens
            // with. Skin only; layout, routing and the forced-flow contract untouched.
            ElarionUiKit.AttachPanelOpenFx(_canvas,
                _chrome.root != null ? _chrome.root.GetComponent<RectTransform>() : null);

            // ── WO-1083: THE STAGE WELL ───────────────────────────────────────────
            // Parented on chrome.content (which exists on BOTH the frame and the
            // procedural path) at FrameCore's frame-MEASURED body rect — see the band
            // table's ⛔ note for why layout.body cannot be used here (its floor is
            // raised ~0.28 of the panel by a Close-band reservation for a Close this
            // screen HIDES, which is defect #5 of WO-1083 in one line).
            Transform content = _chrome.content != null
                ? _chrome.content.transform
                : (_chrome.layout != null && _chrome.layout.body != null
                    ? _chrome.layout.body.transform
                    : _canvas.transform);

            _stageWell = MakeZone(content, "HeroStageWell",
                new Vector2(WellXMin, WellYMin), new Vector2(WellXMax, WellYMax));

            // The kit only paints its obsidian backing behind the RESERVED body zone, so
            // the recovered lower third would show raw frame art through it. Paint the
            // well ourselves; first child, raycast-off, so it never eats a card tap.
            var wellFill = ElarionUiKit.AddImage(_stageWell, "WellFill", Vector2.zero, Vector2.one,
                ElarionUiKit.ObsidianFill, rounded: false);
            wellFill.transform.SetAsFirstSibling();
            var wellFillImg = wellFill.GetComponent<Image>();
            if (wellFillImg != null) wellFillImg.raycastTarget = false;

            // Subtitle eyebrow — the frame's SUB-HEADER band, under the title and ABOVE
            // the well (it used to ride the body top and stole a row from the carousel).
            var subtitle = ElarionUiKit.Label(content, "CHOOSE YOUR DEFENDER",
                0.845f, 0.900f, ElarionUi.Gold, ElarionUi.FontLabel,
                TextAlignmentOptions.Center, 0.24f, 0.945f, spacing: 1f, bold: true);
            subtitle.raycastTarget = false;
            FitLine(subtitle);

            // ── The bands (see the band table above; all disjoint by construction) ──
            //   TOP     : the rotating carousel  (_classColumn, focal card = _stageCenter)
            //   MIDDLE  : role label + page dots + divider rule
            //   LOWER   : the four-column details strip (_stageRight)
            //   BOTTOM  : the EXCLUSIVE CTA band
            _classColumn = MakeZone(_stageWell, "HeroCarousel",
                new Vector2(0f, CarouselYMin), new Vector2(1f, 1f));
            _stageCenter = MakeZone(_classColumn, "HeroStage",
                new Vector2(FocalXMin, 0f), new Vector2(FocalXMax, 1f));
            _stageRight  = MakeZone(_stageWell, "DetailsStrip",
                new Vector2(0f, DetailsYMin), new Vector2(1f, DetailsYMax));

            BuildCarousel();

            // Divider rule between the carousel/dots block and the details strip.
            var divider = ElarionUiKit.AddImage(_stageWell, "DividerRule",
                new Vector2(0.005f, DividerY), new Vector2(0.995f, DividerY + 0.0035f),
                new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.45f), rounded: false);
            var dividerImg = divider.GetComponent<Image>();
            if (dividerImg != null) dividerImg.raycastTarget = false;

            FlowTrace.Step("Onboarding", string.Format(
                "BuildScreen bands (WO-1083, fractions of the stage well x[{0:F3},{1:F3}] y[{2:F3},{3:F3}] of the panel): " +
                "carousel y[{4:F3},1.000] dots y[{5:F3},{6:F3}] divider y={7:F3} details y[{8:F3},{9:F3}] " +
                "CTA-EXCLUSIVE y[0.000,{10:F3}] (button y[{11:F3},{12:F3}]).",
                WellXMin, WellXMax, WellYMin, WellYMax,
                CarouselYMin, DotRailYMin, DotRailYMax, DividerY,
                DetailsYMin, DetailsYMax, CtaBandTop, CtaBtnYMin, CtaBtnYMax));

            // ── Confirm CTA — Obsidian GREEN, the one exit. It now owns an EXCLUSIVE
            // band at the bottom of the well (nothing else is authored below
            // DetailsYMin), which is what retires defects #1 (CTA over NEXT) and #2
            // (CTA clipping the skill badges): those two elements are no longer in the
            // CTA's band at all, so no MinTouch clamp growth can bring them back.
            Transform ctaParent = _stageWell;
            Vector2 ctaMin = new Vector2(0.36f, CtaBtnYMin);
            Vector2 ctaMax = new Vector2(0.64f, CtaBtnYMax);
            _confirmButton = ElarionUiKit.BuildObsidianButton(ctaParent,
                FallbackLocale(DiveKey, "Enter Elarion"),
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Green,
                ctaMin, ctaMax, OnDiveVillageClicked);
            ApplyHeroSelectButton(_confirmButton, primary: true);
            _confirmLabel = _confirmButton != null
                ? _confirmButton.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            FitLine(_confirmLabel);   // CTA label may never spill out of the button

            // Open ON the playable hero so the screen starts on the selectable,
            // pre-selected Grom (not a locked class).
            _shownIndex = IndexOf(DefaultHero);

            // Pre-persist the default playable hero so GameState always has a valid class
            // even if the player confirms without navigating (playable-set checked; idempotent).
            GameStateService.Instance?.ChooseHero(DefaultHero);

            // Paint the opening hero into the stage (sets selection + CTA state).
            PopulateStage(_shownIndex);

            _built = true;

            // V — prove the screen built: chrome + all three stage containers + the
            // class buttons + the CTA exist. A built-but-empty hero-select is a
            // dead-end screen, so a missing piece Fails.
            int carouselButtons = _classColumn != null ? CountButtonsRecursive(_classColumn) : 0;
            bool stageOk = _stageCenter != null && _stageRight != null
                           && _stageCenter.childCount > 0 && _stageRight.childCount > 0;
            bool ctaOk = _confirmButton != null;
            if (carouselButtons < 4 || !stageOk || !ctaOk)
            {
                FlowTrace.Fail("Onboarding",
                    $"BuildScreen VERIFY FAILED — carouselButtons={carouselButtons}/4+ " +
                    $"stageOk={stageOk} ctaOk={ctaOk}. Hero-select built but EMPTY/incomplete (dead-end screen).");
            }
            else
            {
                FlowTrace.Step("Onboarding",
                    $"BuildScreen VERIFY ok — carouselButtons={carouselButtons} pooledCards=2 stageOk={stageOk} ctaOk={ctaOk}.");
            }
        }

        // =====================================================================
        //  TOP BAND — the rotating carousel (data-driven from HeroCatalog)
        // =====================================================================

        /// <summary>
        /// Builds the TOP BAND: a full-band swipe surface, the two POOLED side cards
        /// (the heroes either side of the focal one, repainted per rotation by
        /// <see cref="RefreshCarouselChrome"/>), the outboard rotate controls, and
        /// the display-only page-dot rail in its own band beneath. All three rotation
        /// inputs are wired here: swipe (this surface + the focal well), the rotate
        /// plates, and a side-card tap. Every card is tappable (a locked hero previews
        /// into the stage); only a playable hero can be confirmed.
        /// </summary>
        private void BuildCarousel()
        {
            if (_classColumn == null) return;

            var gesture = ElarionUiKit.AddImage(_classColumn, "SwipeSurface", Vector2.zero, Vector2.one,
                new Color(0f, 0f, 0f, 0f), rounded: false);
            gesture.transform.SetAsFirstSibling();
            var trigger = gesture.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.BeginDrag, e => BeginSwipe((PointerEventData)e));
            AddTrigger(trigger, EventTriggerType.EndDrag, e => EndSwipe((PointerEventData)e));

            // Side (previous / next) cards — SMALLER and LOWER than the focal card, which
            // is the depth cue that reads the rotation. Tapping one rotates a step.
            BuildPreviewCard(0, new Vector2(SideLXMin, CarSideYMin), new Vector2(SideLXMax, CarSideYMax));
            BuildPreviewCard(1, new Vector2(SideRXMin, CarSideYMin), new Vector2(SideRXMax, CarSideYMax));

            // Rotate controls — OUTBOARD of the side cards and authored above the
            // touch floor. They deliberately use the same shared medieval word-button
            // component and typography as the other actions on this screen.
            BuildRotateControl(-1);
            BuildRotateControl(1);

            // Page dots — DISPLAY ONLY by design (rotation is swipe / arrow / side-card
            // tap), so they are raycast-off and deliberately below the touch floor; making
            // them tappable would put a real touch target under MinTouchPx. They live in
            // their OWN band under the carousel, not inside it.
            _dotRail = MakeZone(_stageWell, "PageDots",
                new Vector2(0.44f, DotRailYMin), new Vector2(0.56f, DotRailYMax));
            int n = HeroCatalog.Heroes.Length;
            _pageDots = new Image[n];
            float total = n * DotW + Mathf.Max(0, n - 1) * DotGap;
            float start = (1f - total) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float cx = start + i * (DotW + DotGap) + DotW * 0.5f;
                var marker = ElarionUiKit.AddImage(_dotRail, "PageDot_" + i,
                    new Vector2(cx - DotW * 0.5f, 0.24f), new Vector2(cx + DotW * 0.5f, 0.76f),
                    Color.gray, rounded: true);
                _pageDots[i] = marker.GetComponent<Image>();
                if (_pageDots[i] != null) _pageDots[i].raycastTarget = false;
            }
            FlowTrace.Step("Onboarding",
                $"BuildCarousel: top band built - 2 side cards + rotate ICON+word outboard + {n} display-only page dots.");
        }

        /// <summary>
        /// One carousel rotate button. It uses the canonical shared button label,
        /// font, frame, hover, pressed and disabled states rather than bespoke glyphs.
        /// </summary>
        private void BuildRotateControl(int delta)
        {
            if (_classColumn == null) return;
            bool prev = delta < 0;
            var min = new Vector2(prev ? PrevXMin : NextXMin, CarArrowYMin);
            var max = new Vector2(prev ? PrevXMax : NextXMax, CarArrowYMax);
            var btn = ElarionUiKit.BuildObsidianButton(_classColumn,
                prev ? RotatePrevLabel : RotateNextLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                min, max, () => StepCarousel(delta));
            if (btn == null) return;
            ApplyHeroSelectButton(btn, primary: false);
            btn.gameObject.name = prev ? "CarouselPrev" : "CarouselNext";
            FitLine(btn.GetComponentInChildren<TextMeshProUGUI>(true));
        }

        // Page-dot geometry. The ACTIVE dot is larger AND gilt (size + colour), never
        // colour alone — the owner is red/green colourblind, so the state must survive a
        // greyscale check. Centres are fixed; only the half-extents change.
        private const float DotW = 0.09f, DotGap = 0.055f;
        private const float DotActiveHalfW = 0.055f, DotIdleHalfW = 0.032f;
        private const float DotActiveYMin = 0.02f, DotActiveYMax = 0.98f;
        private const float DotIdleYMin = 0.24f, DotIdleYMax = 0.76f;

        /// <summary>Re-sizes one page dot for its active/idle state about its fixed centre.</summary>
        private static void ApplyDotState(Image dot, bool active)
        {
            if (dot == null) return;
            var rt = dot.rectTransform;
            float cx = (rt.anchorMin.x + rt.anchorMax.x) * 0.5f;
            float half = active ? DotActiveHalfW : DotIdleHalfW;
            rt.anchorMin = new Vector2(cx - half, active ? DotActiveYMin : DotIdleYMin);
            rt.anchorMax = new Vector2(cx + half, active ? DotActiveYMax : DotIdleYMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            dot.color = active ? ElarionUi.Gilt : new Color(0.40f, 0.40f, 0.40f, 1f);
        }

        private void BuildPreviewCard(int slot, Vector2 min, Vector2 max)
        {
            var card = ElarionUiKit.BuildObsidianButton(_classColumn, "",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                min, max, () => StepCarousel(slot == 0 ? -1 : 1));
            _carouselCards[slot] = card;
            if (card == null) return;
            ApplyHeroSelectButton(card, primary: false);

            var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portrait.transform.SetParent(card.transform, false);
            var rt = (RectTransform)portrait.transform;
            rt.anchorMin = new Vector2(0.08f, 0.22f); rt.anchorMax = new Vector2(0.92f, 0.94f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _carouselPortraits[slot] = portrait.GetComponent<Image>();
            _carouselPortraits[slot].preserveAspect = true;
            _carouselPortraits[slot].raycastTarget = false;
            _carouselLabels[slot] = ElarionUiKit.Label(card.transform, "", 0.12f, 0.30f,
                ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.03f, 0.97f, bold: true);
            _carouselLabels[slot].raycastTarget = false;
            FitLine(_carouselLabels[slot]);

            // SOON word-ribbon for a locked hero on this side card (WO-1083 §2). The WORD
            // carries the state — never colour alone (greyscale check). Decorative: raycast
            // off, so it can never swallow the card's rotate tap.
            var ribbon = ElarionUiKit.AddImage(card.transform, "SoonRibbon",
                new Vector2(0.30f, 0.82f), new Vector2(0.98f, 0.98f), ElarionUi.GoldButton, rounded: true);
            var ribbonImg = ribbon.GetComponent<Image>();
            if (ribbonImg != null) ribbonImg.raycastTarget = false;
            var ribbonLbl = ElarionUiKit.Label(ribbon.transform, "SOON", 0f, 1f,
                ElarionUi.Ink, ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.04f, 0.96f,
                spacing: 1f, bold: true);
            ribbonLbl.raycastTarget = false;
            FitLine(ribbonLbl);
            ribbon.SetActive(false);
            _carouselSoon[slot] = ribbon;
        }

        private static void ApplyHeroSelectButton(Button button, bool primary)
        {
            if (button == null) return;
            MedievalUiSkin.ApplyButton(button, primary);
            if (button.targetGraphic is Image image)
            {
                var frame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
                if (frame != null) image.sprite = frame;
                image.type = Image.Type.Simple;
                image.color = Color.white;
            }
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(e => action(e));
            trigger.triggers.Add(entry);
        }

        private void BeginSwipe(PointerEventData e) { _trackingSwipe = true; _swipeStart = e.position; }

        private void EndSwipe(PointerEventData e)
        {
            if (!_trackingSwipe) return;
            _trackingSwipe = false;
            float dx = e.position.x - _swipeStart.x;
            if (Mathf.Abs(dx) >= SwipeThresholdPx) StepCarousel(dx < 0f ? 1 : -1);
        }

        private void StepCarousel(int delta)
        {
            if (HeroCatalog.Heroes.Length > 0) PopulateStage(_shownIndex + delta);
        }

        /// <summary>
        /// The class-column label for a catalog hero — the CLASS (enum name, the
        /// data-driven "Knight / Ranger / Mage / Cleric" set), never hardcoded copy.
        /// </summary>
        private static string ClassLabelFor(HeroCardInfo info)
            => info != null ? info.Hero.ToString() : "?";

        // =====================================================================
        //  CENTER + RIGHT — the hero stage (rebuilt per selection)
        // =====================================================================

        /// <summary>
        /// Re-paints the stage for the catalog slot at <paramref name="index"/>:
        /// rebuilds the CENTER (focal portrait + LOCKED scrim when locked + name/
        /// role under it) and RIGHT (lore / stats pips / signature / primary skills)
        /// content, then refreshes the class-button highlight and the confirm CTA
        /// (enabled "Enter Elarion" on the playable hero, disabled "Coming Soon" on
        /// a locked one). The selection is the playable hero only — tapping a locked
        /// class is a PREVIEW, not a pick.
        /// </summary>
        private void PopulateStage(int index)
        {
            if (_stageCenter == null || _stageRight == null) return;
            if (HeroCatalog.Heroes.Length == 0) return;

            index = ((index % HeroCatalog.Heroes.Length) + HeroCatalog.Heroes.Length) % HeroCatalog.Heroes.Length;
            _shownIndex = index;
            HeroCardInfo info = HeroCatalog.Heroes[index];
            bool playable = IsPlayable(info.Hero);

            ClearChildren(_stageCenter);
            ClearChildren(_stageRight);

            BuildCenterStage(info, playable);
            BuildSpecsPanel(info, playable);

            // Selection: only the playable hero can be selected. Previewing a
            // locked class leaves no selectable choice.
            if (playable)
            {
                _selectedHero = info.Hero;
                _hasSelection = true;
            }
            else
            {
                _hasSelection = false;
            }

            RefreshConfirm(playable);
            RefreshCarouselChrome();
        }

        /// <summary>CENTER — the focal hero card (its own frame + baked plate when the art is
        /// card-style), with the LOCKED scrim over the WHOLE card.</summary>
        private void BuildCenterStage(HeroCardInfo info, bool playable)
        {
            // ── WO-1234 — GOLD FRAME: drawn ONLY when the art is a bare portrait ──────
            // The 2026-08-26 art carries its own gold frame, so drawing this one puts a
            // frame inside a frame. The code stays, gated on PortraitArtIsFullCard, so a
            // future bare-portrait delivery restores the focal chrome by flipping ONE
            // const — WO-1083 §2/§4's "focal reads as frame AND size, never colour alone"
            // still holds either way, because the card art's own frame is gold too and the
            // focal card is still the largest on the rail (greyscale-safe by SIZE).
            if (!PortraitArtIsFullCard)
            {
                var focalFrame = ElarionUiKit.AddImage(_stageCenter, "FocalFrame",
                    new Vector2(0f, 0.280f), new Vector2(1f, 1.000f), ElarionUi.GoldButton, rounded: true);
                var focalFrameImg = focalFrame.GetComponent<Image>();
                if (focalFrameImg != null) focalFrameImg.raycastTarget = false;
            }

            // ── The card rect ────────────────────────────────────────────────────────
            // Card-style: a TRANSPARENT rect sized by an AspectRatioFitter to the LARGEST
            // PortraitArtAspect box that fits the focal lane, so the art fills it edge to
            // edge with NO letterbox at any resolution (the fitter re-measures per layout
            // pass — a hardcoded fraction would only be right at 2670x1200). No dark well
            // fill and no inner rim, because the card supplies its own background; the
            // Image stays raycast-ON so it is still the swipe surface.
            // Bare-portrait: the old dark recess, unchanged.
            GameObject well;
            if (PortraitArtIsFullCard)
            {
                well = ElarionUiKit.AddImage(_stageCenter, "HeroCard",
                    Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0f), rounded: false);
                var fitter = well.GetComponent<AspectRatioFitter>();
                if (fitter == null) fitter = well.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = PortraitArtAspect;
            }
            else
            {
                well = ElarionUiKit.Well(_stageCenter, new Vector2(0.022f, 0.295f), new Vector2(0.978f, 0.985f));
            }
            var swipeTrigger = well.GetComponent<EventTrigger>();
            if (swipeTrigger == null) swipeTrigger = well.AddComponent<EventTrigger>();
            AddTrigger(swipeTrigger, EventTriggerType.BeginDrag, e => BeginSwipe((PointerEventData)e));
            AddTrigger(swipeTrigger, EventTriggerType.EndDrag, e => EndSwipe((PointerEventData)e));

            // The hero image itself — sprite-first, texture fallback, glyph last.
            // Card-style fills the rect EXACTLY (0..1): the card IS the rect, which is what
            // lets the scrim below cover the baked nameplate by covering the same 0..1.
            var portraitGo = new GameObject("HeroPortrait", typeof(RectTransform));
            portraitGo.transform.SetParent(well.transform, false);
            var prt = (RectTransform)portraitGo.transform;
            prt.anchorMin = PortraitArtIsFullCard ? Vector2.zero : new Vector2(0.04f, 0.03f);
            prt.anchorMax = PortraitArtIsFullCard ? Vector2.one  : new Vector2(0.96f, 0.97f);
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            ApplyPortrait(portraitGo, info, playable);

            // ── WO-1234 ⭐ — LOCKED scrim over the WHOLE CARD, plate included ─────────
            // It was already parented to this rect at 0..1; what was BROKEN is that the
            // rect used to be the portrait recess INSET inside the frame, while the name
            // and role were separate labels OUTSIDE it. Elara is the Cleric and the Cleric
            // is locked, so the screen rendered a crisp gold "ELARA / Cleric" plate sitting
            // cleanly BELOW a greyed LOCKED / Coming Soon scrim — the locked state visibly
            // contradicting itself. Now the rect IS the card (0..1, aspect-fitted) and the
            // name/role are BAKED INTO that art, so one 0..1 scrim covers portrait, frame
            // and plate together. Greyscale-safe: it is an alpha wash plus the WORD
            // "LOCKED", never a hue.
            if (!playable)
            {
                var scrim = ElarionUiKit.AddImage(well.transform, "LockScrim",
                    Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.55f), rounded: false);
                var scrimImg = scrim.GetComponent<Image>();
                if (scrimImg != null) scrimImg.raycastTarget = false;

                var locked = ElarionUiKit.Label(scrim.transform, "LOCKED",
                    0.46f, 0.60f, ElarionUi.Parchment, ElarionUi.FontHead,
                    TextAlignmentOptions.Center, 0f, 1f, spacing: 3f, bold: true);
                locked.raycastTarget = false;
                FitLine(locked);

                var soon = ElarionUiKit.Label(scrim.transform, "Coming Soon",
                    0.38f, 0.46f, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                    TextAlignmentOptions.Center, 0f, 1f);
                soon.fontStyle = FontStyles.Italic;
                soon.raycastTarget = false;
                FitLine(soon);
            }

            // ── WO-1234 — name + role: SUPPRESSED for card art, NOT DELETED ──────────
            // ⚠ THESE ARE THE LOCALISED PATH. CanonStrings.Locale(info.NameKey/RoleKey)
            // reads en.json; the baked plate on the card art is ENGLISH ONLY. Deleting
            // them would make this screen permanently untranslatable and the loss would be
            // invisible until a second locale shipped — so the code stays, whole, behind
            // one const.
            //
            // HOW LOCALISATION RE-ENABLES THEM: today CanonStrings has exactly one string
            // table (en.json — Canon()/Locale() with no locale selector), so the plate can
            // never disagree with the labels. The moment a second locale lands, this
            // condition becomes the locale test rather than a flat const — render the
            // labels whenever the active locale is not the one baked into the plate — and
            // the card art gets a plate-free variant for those locales. Nothing here needs
            // to be rewritten to get there; only the predicate changes.
            if (!PortraitArtIsFullCard)
            {
                // Owner F8 2026-07-03: the playable hero's name (Knight) uses WHITE so it
                // pops against the frame; a locked hero stays dim.
                var nameLabel = ElarionUiKit.Label(_stageCenter, CanonStrings.Locale(info.NameKey),
                    0.150f, 0.270f, playable ? Color.white : ElarionUi.ParchmentDim,
                    ElarionUi.FontTitle, TextAlignmentOptions.Center, 0.02f, 0.98f, spacing: 1f, bold: true);
                nameLabel.raycastTarget = false;
                FitLine(nameLabel);

                var roleLabel = ElarionUiKit.Label(_stageCenter, CanonStrings.Locale(info.RoleKey),
                    0.020f, 0.140f, ElarionUi.Gold, ElarionUi.FontLabel,
                    TextAlignmentOptions.Center, 0.02f, 0.98f, spacing: 1.5f, bold: true);
                roleLabel.raycastTarget = false;
                FitLine(roleLabel);
            }
        }

        /// <summary>
        /// RIGHT — the specs panel: lore blurb, HP/ATTACK/SPEED pip rows, signature
        /// ability, and the primary Q/F/E/R skill kit — all from HeroCatalog data.
        /// </summary>
        private void BuildSpecsPanel(HeroCardInfo info, bool playable)
        {
            // WO-1083: the same four sections, the same data sources, the same strings —
            // laid ACROSS in four columns under the carousel instead of stacked down a
            // right-hand rail. Each column is a zone, so every section's fractions below
            // are of ITS OWN column and can never bleed into a neighbour.
            var lore   = MakeZone(_stageRight, "Col_Lore",
                new Vector2(DetailColumns[0].x, 0f), new Vector2(DetailColumns[0].y, 1f));
            var stats  = MakeZone(_stageRight, "Col_Stats",
                new Vector2(DetailColumns[1].x, 0f), new Vector2(DetailColumns[1].y, 1f));
            var sig    = MakeZone(_stageRight, "Col_Signature",
                new Vector2(DetailColumns[2].x, 0f), new Vector2(DetailColumns[2].y, 1f));
            var skillsCol = MakeZone(_stageRight, "Col_Skills",
                new Vector2(DetailColumns[3].x, 0f), new Vector2(DetailColumns[3].y, 1f));

            // — LORE —
            SectionHead(lore, "LORE", 0.85f, 1.00f);
            // WO-1636: 0.80 -> 0.84. The LORE column gave 31 px of WIDTH to the two name columns
            // (see DetailColumns), so it takes back the dead strip between the old blurb top and
            // the section head at 0.85 - 12 px of HEIGHT at portrait, which is roughly half a
            // wrapped line and offsets the narrower measure. The blurb is a wrapped FitBlock, so
            // height and width trade directly; a single-line label could not have made this swap.
            var blurb = ElarionUiKit.Label(lore, CanonStrings.Locale(info.BlurbKey),
                0.02f, 0.84f, ElarionUi.Parchment, ElarionUi.FontLabel,
                TextAlignmentOptions.TopLeft, 0.02f, 0.98f);
            blurb.textWrappingMode = TextWrappingModes.Normal;
            blurb.raycastTarget = false;
            FitBlock(blurb);

            // — STATS — (pip rows; uGUI image pips, no unicode glyphs in TMP)
            SectionHead(stats, "STATS", 0.85f, 1.00f);
            BuildPipRow(stats, "HP",     info.Hp,     0.58f, 0.80f);
            BuildPipRow(stats, "ATTACK", info.Attack, 0.31f, 0.53f);
            BuildPipRow(stats, "SPEED",  info.Speed,  0.04f, 0.26f);

            // — SIGNATURE —
            SectionHead(sig, "SIGNATURE", 0.85f, 1.00f);
            var sigName = ElarionUiKit.Label(sig, info.AbilityName,
                0.56f, 0.80f, ElarionUi.Gold, ElarionUi.FontBody,
                TextAlignmentOptions.Left, 0.02f, 0.98f, bold: true);
            sigName.raycastTarget = false;
            FitLine(sigName);
            var sigDesc = ElarionUiKit.Label(sig, info.AbilityDesc,
                0.02f, 0.54f, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                TextAlignmentOptions.TopLeft, 0.02f, 0.98f);
            sigDesc.textWrappingMode = TextWrappingModes.Normal;
            sigDesc.raycastTarget = false;
            FitBlock(sigDesc);

            // — PRIMARY SKILLS — the hero's Q/F/E/R kit (mirrored from abilities.json
            // via HeroCatalog). A labelled placeholder shows for a hero whose kit is
            // not yet authored (e.g. the Cleric — ⛔ WO-1083 §3, unchanged).
            SectionHead(skillsCol, "PRIMARY SKILLS", 0.85f, 1.00f);
            var skills = info.PrimarySkills;
            if (skills != null && skills.Length > 0)
            {
                const float sTop = 0.80f;
                const float sBottom = 0.02f;
                float sRow = (sTop - sBottom) / Mathf.Max(1, skills.Length);
                for (int s = 0; s < skills.Length; s++)
                {
                    float y1 = sTop - s * sRow;
                    // The skills column is NARROW (a quarter of the strip), so the slot
                    // badge gets a wider lane than the old full-width rail used or the
                    // Q/W/E/R glyph autosizes into a smear.
                    // WO-1636: badge 0.02..0.22 -> 0.02..0.17 and the name starts at 0.215 instead
                    // of 0.28. The column itself grew (0.175 -> 0.232 of the well), so the BADGE
                    // still gets 32.4 px - the same plate width it had - while the NAME lane goes
                    // 114.3 -> 165.4 reference px. That is what seats "Warden's Grace" (~147 px at
                    // the 20 px floor) and "Storm of Arrows"; both were cut at 8 of 13 glyphs.
                    BuildSkillRow(skillsCol, skills[s].Slot, skills[s].Name,
                                  y1 - sRow * 0.92f, y1 - sRow * 0.08f,
                                  badgeX1: 0.17f, nameX0: 0.215f);
                }
            }
            else
            {
                var soon = ElarionUiKit.Label(skillsCol, "Abilities revealed at launch",
                    0.40f, 0.80f, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                    TextAlignmentOptions.TopLeft, 0.02f, 0.98f);
                soon.fontStyle = FontStyles.Italic;
                soon.textWrappingMode = TextWrappingModes.Normal;
                soon.raycastTarget = false;
                FitBlock(soon);
            }

            FlowTrace.Step("Onboarding", string.Format(
                "BuildSpecsPanel: details strip built as 4 columns (LORE/STATS/SIGNATURE/SKILLS) " +
                "in well band y[{0:F3},{1:F3}] - skills={2}, playable={3}.",
                DetailsYMin, DetailsYMax, skills != null ? skills.Length : 0, playable));
        }

        /// <summary>A small gilt section heading with a hairline rule beneath it.</summary>
        private static void SectionHead(Transform parent, string text, float y0, float y1)
        {
            var head = ElarionUiKit.Label(parent, text, y0, y1,
                ElarionUi.Gilt, ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.02f, 0.98f, spacing: 2f, bold: true);
            head.raycastTarget = false;
            FitLine(head);

            var rule = ElarionUiKit.AddImage(parent, "Rule", new Vector2(0.02f, y0),
                new Vector2(0.98f, y0 + 0.004f),
                new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.45f), rounded: false);
            var ruleImg = rule.GetComponent<Image>();
            if (ruleImg != null) ruleImg.raycastTarget = false;
        }

        /// <summary>
        /// One stat row — a gold key label + five square uGUI pips (gold filled /
        /// dim empty). Image pips, not text glyphs — NO unicode in TMP (ASCII rule).
        /// </summary>
        private static void BuildPipRow(Transform parent, string label, int value, float y0, float y1)
        {
            // WO-1636: the key lane widens 0.02..0.30 -> 0.02..0.32 of the column and the pip run
            // tightens to match. WHY, since no pip key was in the oracle's findings: the STATS
            // column itself gave 54 reference px to the two name columns (see DetailColumns), and
            // at the OLD 0.28 share the longest key, "ATTACK", would have landed inside ~5% of its
            // own floor - i.e. this fix would have bought two clean columns by quietly opening a
            // THIRD truncation, which is the one thing WO-1636's acceptance forbids. At 0.30 of the
            // narrowed column the lane is 73.3 px against ~66 px of "ATTACK" at the 16 px FitLine
            // floor, so the margin is preserved rather than spent. Pips are IMAGES: they lose
            // 7 px each (34.3 -> 27.3) and cannot truncate, so they are the right thing to trade.
            var key = ElarionUiKit.Label(parent, label, y0, y1,
                ElarionUi.Gold, ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.02f, 0.32f, spacing: 1f, bold: true);
            key.raycastTarget = false;
            FitLine(key);

            value = Mathf.Clamp(value, 0, 5);
            const float pipX0 = 0.35f;
            const float pipW = 0.112f;
            const float pipGap = 0.013f;
            float padY = (y1 - y0) * 0.18f;
            for (int p = 0; p < 5; p++)
            {
                float x0 = pipX0 + p * (pipW + pipGap);
                var pip = ElarionUiKit.AddImage(parent, "Pip" + p,
                    new Vector2(x0, y0 + padY), new Vector2(x0 + pipW, y1 - padY),
                    p < value ? ElarionUi.Gold
                              : new Color(ElarionUi.ParchmentDim.r, ElarionUi.ParchmentDim.g,
                                          ElarionUi.ParchmentDim.b, 0.25f),
                    rounded: true);
                var pipImg = pip.GetComponent<Image>();
                if (pipImg != null) pipImg.raycastTarget = false;
            }
        }

        /// <summary>One primary-skill row — a slot badge (Q/F/E/R) + the ability name.
        /// W9 (WO-714): the badge is sprite-FIRST on the pack's Stat_Element plate
        /// (element/element_stat, 9-sliced, chrome-tinted) with the pre-existing
        /// procedural gold chip as the null-art fallback — the badge ink follows the
        /// plate (parchment on the dark pack plate, Ink on the gold fallback).</summary>
        private static void BuildSkillRow(Transform parent, string slot, string name, float y0, float y1,
                                          float badgeX1 = 0.115f, float nameX0 = 0.15f)
        {
            var plateSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleElement, RpgUiCatalog.ElementStat);
            var badge = ElarionUiKit.AddImage(parent, "SlotBadge",
                new Vector2(0.02f, y0), new Vector2(badgeX1, y1),
                plateSprite != null ? ElarionUiKit.ChromeTint : ElarionUi.GoldButton,
                rounded: plateSprite == null);
            var badgeImg = badge.GetComponent<Image>();
            if (badgeImg != null)
            {
                badgeImg.raycastTarget = false;
                if (plateSprite != null)
                {
                    badgeImg.sprite = plateSprite;
                    badgeImg.type = Image.Type.Sliced;
                    badgeImg.fillCenter = true;
                }
            }
            var badgeLbl = ElarionUiKit.Label(badge.transform, slot, 0f, 1f,
                plateSprite != null ? ElarionUi.Parchment : ElarionUi.Ink, ElarionUi.FontMicro,
                TextAlignmentOptions.Center, 0f, 1f, bold: true);
            badgeLbl.raycastTarget = false;
            FitLine(badgeLbl);

            var nameLbl = ElarionUiKit.Label(parent, name, y0, y1,
                ElarionUi.Parchment, ElarionUi.FontLabel,
                TextAlignmentOptions.Left, nameX0, 0.98f, bold: true);
            nameLbl.raycastTarget = false;
            FitLine(nameLbl);
        }

        // =====================================================================
        //  Confirm CTA + class-highlight state
        // =====================================================================

        /// <summary>
        /// Refreshes the confirm CTA for the current hero: enabled "Enter Elarion" on
        /// the playable hero, disabled "Coming Soon" on a locked one.
        /// </summary>
        private void RefreshConfirm(bool playable)
        {
            if (_confirmButton == null) return;
            if (_confirmLabel != null)
            {
                string heroName = HeroCatalog.Heroes.Length > 0
                    ? CanonStrings.Locale(HeroCatalog.Heroes[_shownIndex].NameKey) : "Hero";
                if (string.IsNullOrEmpty(heroName)) heroName = "Hero";
                _confirmLabel.text = playable ? "CHOOSE " + heroName.ToUpperInvariant() : "COMING SOON";
                FitLine(_confirmLabel);
            }
            _confirmButton.interactable = playable && _hasSelection;
        }

        private void RefreshCarouselChrome()
        {
            int n = HeroCatalog.Heroes.Length;
            if (n == 0) return;
            for (int slot = 0; slot < 2; slot++)
            {
                int index = (_shownIndex + (slot == 0 ? -1 : 1) + n) % n;
                HeroCardInfo info = HeroCatalog.Heroes[index];
                if (_carouselPortraits[slot] != null)
                {
                    _carouselPortraits[slot].sprite = LoadPortraitSprite(info.Hero);
                    _carouselPortraits[slot].color = new Color(0.58f, 0.58f, 0.58f, 0.82f);
                    _carouselPortraits[slot].enabled = _carouselPortraits[slot].sprite != null;
                }
                if (_carouselLabels[slot] != null)
                    _carouselLabels[slot].text = ClassLabelFor(info);
                // The SOON ribbon is presentation of the EXISTING IsPlayable state — the
                // roster, the coercion and Elara's card behaviour are untouched (WO-1083 §3).
                if (_carouselSoon[slot] != null)
                    _carouselSoon[slot].SetActive(!IsPlayable(info.Hero));
            }

            if (_pageDots == null) return;
            for (int i = 0; i < _pageDots.Length; i++)
                ApplyDotState(_pageDots[i], i == _shownIndex);
        }

        // =====================================================================
        //  Confirm — write the hero choice then route (CONTRACT PRESERVED)
        // =====================================================================

        /// <summary>
        /// "Enter Elarion" — the confirm CTA. Persists the chosen (playable) hero, then
        /// routes DIRECTLY to the home hub (MainCastle_Hall) via GoCastle(). A no-op when
        /// the on-screen hero is locked (the CTA is disabled there, but guard anyway).
        ///
        /// The live path is HeroSelect -> Castle — the pet-select step is gone for
        /// single-hero V1. The FeatureFlags.BypassPetSelect branch is kept ONLY as a
        /// reversibility hatch (flag OFF restores the old pet step).
        /// </summary>
        private void OnDiveVillageClicked()
        {
            if (!_hasSelection) return;   // locked hero on screen — nothing to confirm
            PersistHero();

            // Reversibility hatch: flag OFF -> old pet step. DEFAULT path is GoCastle
            // (flag default ON) — single-hero V1 never shows PetSelect.
            if (FeatureFlags.BypassPetSelect)
            {
                // FTUE-01 (2026-07-19): the WO-748 founding choice (Default Town vs Build
                // Your Own) was wired ONLY on the PetSelect route (PetSelectController), so
                // on this default BypassPetSelect path -- HeroSelect straight to the hub --
                // it NEVER showed and every fresh founder silently got the blank template.
                // Present it HERE, at the genuine HeroSelect->hub chokepoint, BEFORE the
                // first Castle load (the choice must set StrategicPlacementMigrated before
                // the Castle-scene migration writer runs). PresentOrContinue self-gates on
                // ShouldOffer (a returning / already-founded player continues straight to
                // GoCastle), so this only fires on a genuine fresh founding and is idempotent.
                FlowTrace.Step("Onboarding", "OnDiveVillageClicked: single-hero V1 -- founding choice then GoCastle (PetSelect skipped).");
                FoundingChoiceController.PresentOrContinue(SceneRouter.GoCastle);
                return;
            }

            SceneRouter.GoPetSelect();
        }

        /// <summary>Writes <see cref="GameState.HeroClass"/> via the service and saves.</summary>
        private void PersistHero()
        {
            var svc = GameStateService.Instance;
            if (svc != null)
            {
                svc.ChooseHero(_selectedHero);
            }
            else
            {
                FlowTrace.Warn("Onboarding", "PersistHero: No GameStateService — the hero choice was NOT persisted. Routing onward anyway.");
            }
        }

        // =====================================================================
        //  Portrait resolution
        // =====================================================================

        /// <summary>
        /// Applies a hero's portrait art to the portrait GameObject — sprite-first
        /// (uGUI Image, aspect kept), Texture2D fallback (RawImage), accent glyph
        /// (the catalog's ASCII letter) last. No missing-asset risk —
        /// Grom/Thrain/Sylas/Elara card art all exists under HeroPortraitPaths.ResourcesFolder.
        /// </summary>
        private static void ApplyPortrait(GameObject host, HeroCardInfo info, bool playable)
        {
            if (host == null || info == null) return;
            string slug = SlugFor(info.Hero);
            float dim = playable ? 1f : 0.5f;   // dim a locked hero

            var portraitSprite = LoadPortraitSprite(info.Hero);
            if (portraitSprite != null)
            {
                var img = host.AddComponent<Image>();
                img.sprite = portraitSprite;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = new Color(dim, dim, dim, 1f);
                img.raycastTarget = false;
                return;
            }

            var portraitTex = Resources.Load<Texture2D>(HeroPortraitPaths.ResourceKey(slug));
            if (portraitTex != null)
            {
                var raw = host.AddComponent<RawImage>();
                raw.texture = portraitTex;
                raw.color = new Color(dim, dim, dim, 1f);
                raw.raycastTarget = false;
                return;
            }

            // Last resort: the catalog's ASCII accent glyph, big and centred.
            var glyph = ElarionUiKit.Label(host.transform, info.Glyph, 0f, 1f,
                info.Accent, 96, TextAlignmentOptions.Center, 0f, 1f, bold: true);
            glyph.raycastTarget = false;
            FitLine(glyph);
        }

        // =====================================================================
        //  Small helpers
        // =====================================================================

        /// <summary>True when a hero class is selectable — delegated to the ONE roster truth
        /// (<see cref="PlayableHeroes"/>), which GameStateService.ChooseHero and the vendor
        /// shelf read as well, so the three can never disagree about who exists.</summary>
        private static bool IsPlayable(HeroClass hero) => PlayableHeroes.IsPlayable(hero);

        /// <summary>Catalog index for a hero class, or 0 when absent (never out of range).</summary>
        private static int IndexOf(HeroClass hero)
        {
            for (int i = 0; i < HeroCatalog.Heroes.Length; i++)
                if (HeroCatalog.Heroes[i].Hero == hero) return i;
            return 0;
        }

        /// <summary>Canon-roster portrait slug for a hero class.</summary>
        private static string SlugFor(HeroClass hero) => hero switch
        {
            HeroClass.Mage   => "Thrain",
            HeroClass.Knight => "Grom",
            HeroClass.Ranger => "Sylas",
            HeroClass.Cleric => "Elara",
            _                => hero.ToString(),
        };

        /// <summary>
        /// Returns the localised string for <paramref name="key"/>, falling back to
        /// <paramref name="fallback"/> when the key is absent (so a label is never
        /// blank if <c>en.json</c> hasn't been updated yet).
        /// </summary>
        private static string FallbackLocale(string key, string fallback)
        {
            var s = CanonStrings.Locale(key);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        // ── Text-fit guards (owner F8 2026-07-06: "screen is writing overtop of
        // itself" at a small window). Every label on this screen lives in a
        // fraction-anchored band; the kit's Label() gives it a FIXED font size with
        // TMP's default Overflow mode, so at small window heights the text spills
        // out of its band and paints over the section below. These two helpers make
        // overflow structurally impossible: TMP autosize shrinks the text to fit
        // its band (down to a legible floor), and Ellipsis truncates anything that
        // still cannot fit — text can never escape its rect again.

        /// <summary>
        /// Fits a SINGLE-LINE label inside its band: no wrapping, autosize between a
        /// legible floor and the authored size, Ellipsis if it still cannot fit.
        /// </summary>
        private static void FitLine(TextMeshProUGUI t)
        {
            if (t == null) return;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Clamp(t.fontSize * 0.5f, 8f, t.fontSize);
        }

        /// <summary>
        /// Fits a MULTI-LINE block (lore / ability copy) inside its band: wrapped,
        /// autosize between a legible floor and the authored size, Ellipsis on the
        /// last visible line if the copy still cannot fit.
        /// </summary>
        private static void FitBlock(TextMeshProUGUI t)
        {
            if (t == null) return;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Clamp(t.fontSize * 0.5f, 8f, t.fontSize);
        }

        /// <summary>A transparent fraction-anchored container RectTransform.</summary>
        private static RectTransform MakeZone(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Destroys all children of a container (stage rebuild).</summary>
        private static void ClearChildren(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Counts the Button components directly under a container (verify).</summary>
        private static int CountButtonsRecursive(Transform t)
        {
            if (t == null) return 0;
            int n = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child.GetComponent<Button>() != null) n++;
                n += CountButtonsRecursive(child);
            }
            return n;
        }

        private static Sprite LoadPortraitSprite(HeroClass hero)
        {
            if (PortraitSpriteCache.TryGetValue(hero, out var cached) && cached != null)
                return cached;

            // WO-1234: the FOLDER comes from the one constant; SlugFor stays the id->filename map.
            string path = HeroPortraitPaths.ResourceKey(SlugFor(hero));
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                var texture = Resources.Load<Texture2D>(path);
                if (texture != null)
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }
            if (sprite != null) PortraitSpriteCache[hero] = sprite;
            return sprite;
        }
    }
}

// =============================================================================
// INTEGRATOR NOTES — wiring the hero-select scene.
// -----------------------------------------------------------------------------
//   1. The HeroSelect scene is generated by DeNelle.Editor.IntroFlowSceneBuilder
//      (menu: Defenders/Intro Flow/Build Hero + Pet Select Scenes). It creates a
//      Camera, an EventSystem, and a host GameObject with this controller
//      attached. The host MAY still carry the legacy UIDocument — BuildScreen()
//      DISABLES it and builds the entire screen as kit uGUI on its own canvas, so
//      neither the UXML nor a missing PanelSettings can affect this screen.
//      (RequireComponent(UIDocument) was removed in the WO-C conversion.)
//
//   2. The intro/story cinematic (StoryIntroController) lives in the TITLE scene,
//      not here. The transition into hero-select is a single-scene LoadScene
//      (SceneRouter.GoHeroSelect), so Unity destroys the Title scene — and the
//      cold-open overlay with it — before this scene's OnEnable runs; they never
//      coexist.
//
//   3. GameStateService must exist before this screen so ChooseHero persists. It is
//      a DontDestroyOnLoad singleton from the Core bootstrap in Title. If a session
//      enters HeroSelect cold, the controller logs a warning and still routes on
//      (the choice just is not saved).
//
//   4. CAROUSEL: every catalog hero is tappable for a stage preview; only a
//      PLAYABLE hero is confirmable. WO-861 Phase 0: the playable set is
//      DeNelle.Core.State.PlayableHeroes — { Knight, Ranger, Mage } since the
//      2026-08-05 unlock (ff.knightonly defaults OFF); the Cleric is the one locked
//      card left. To change the roster, edit that ONE registry — no change here;
//      the locked tag/scrim/CTA state all derive from IsPlayable.
// =============================================================================
