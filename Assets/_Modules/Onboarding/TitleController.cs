// =============================================================================
// TitleController — the Title scene orchestrator (Week 1 -> WO-C uGUI conversion)
// -----------------------------------------------------------------------------
// WO-C part 1 (2026-07-03, coverage matrix row #15): UIDocument/UITK title
// -> code-built uGUI on the Blink Obsidian kit (ElarionUiKit). The title is the
// FIRST-IMPRESSION surface, so it now renders through the same proven uGUI path
// as the rest of the game (HelpMenu is the reference conversion) instead of the
// UI Toolkit panel that fought four other UIDocuments over the one shared
// "OnboardingPanelSettings" asset (the duplicate-UIDocument input-eating bug).
// This controller no longer renders through a UIDocument AT ALL; the legacy
// scene documents it used to own are explicitly DISABLED in Awake so they can
// neither draw nor steal input.
//
// FLOW CONTRACT (preserved verbatim from the UITK version):
//   * Owner 2026-06-04 SPLASH GATE — the scene opens on a static title screen;
//     the first button press is the browser's audio-unlock gesture.
//   * Continue    -> resume into the Castle home hub (persists Knight at the
//                    load source if the save carries no HeroClass — V1 single-hero).
//   * Start New   -> full save + dialogue reset, fast-path onboarding
//                    (OnboardingMode.ChooseFastPath), route to the HeroSelect
//                    carousel (WO-559: HeroClass=None so the carousel builds).
//   * Play Intro  -> OnboardingMode.ChooseFullTutorial + clear persisted hero,
//                    then the 9-screen cinematic via Core.IntroLauncher; falls
//                    back to the StoryIntro cold-open when no intro player is
//                    registered (build without dialogue assets).
//   * DEF-253 watchdog — the cold-open fallback can never strand the player:
//     SafeStage times each stage out AND an unscaled Update timer force-returns
//     to the title menu.
//
// The old in-Title 4-card hero-select (BuildTitleScreen) is RETIRED: WO-559
// moved hero selection to the HeroSelect carousel scene and no route reached
// the in-Title cards any more (only the watchdog fallback did, showing a screen
// the real flow never used). The watchdog now returns to the title MENU instead.
// With it go the UITK-only workarounds it dragged along (NeutralizeOverlayPanels,
// the WebGL orphan re-assert, PANELDIAG) — none apply to a uGUI canvas.
//
// Visuals: full-screen title art (Resources/Title/Title_L landscape,
// Title_H portrait — the title text is baked into this art), with a vertical
// stack of Obsidian family buttons (Continue = Green when a save exists,
// Start New = Yellow, Play Intro = Gray). When the art is missing the screen
// falls back to an obsidian backdrop with the kit-typography title block
// (CanonStrings — never hardcoded, v2 port-spec Part 4) so it can never blank.
//
// async UniTask for the arrival flow — never async void (port-spec Part 3).
// =============================================================================

using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Drives the Title scene: the splash-gate title menu (Continue / Start New /
    /// Play Intro) and the cold-open fallback arrival sequence. Code-built uGUI
    /// on the Obsidian kit — no UIDocument.
    /// </summary>
    public sealed class TitleController : MonoBehaviour
    {
        [Header("Arrival sequence")]
        [Tooltip("The studio bumper — CUT (owner 2026-06-04) but kept wired by OnboardingSceneBuilder.")]
        [SerializeField] private SplashLoading _splash;

        [Tooltip("The cold-open cinematic — the Play-Intro fallback when IntroLauncher is absent.")]
        [SerializeField] private StoryIntroController _storyIntro;

        [Header("Legacy (WO-C)")]
        [Tooltip("The retired Title UIDocument (TitleScreen.uxml). Still wired by " +
                 "OnboardingSceneBuilder; disabled in Awake so it cannot render or eat input.")]
        [SerializeField] private UnityEngine.UIElements.UIDocument _titleDocument;

        // ── Code-built uGUI title menu ────────────────────────────────────────
        private GameObject _canvas;              // the whole title screen
        private Image _backdropFill;             // obsidian floor — never blank
        private Image _backdropArt;              // Title_L / Title_H cover art
        private AspectRatioFitter _backdropFitter;
        private bool _backdropArtLandscape;      // which orientation art is loaded
        private bool _backdropArtLoaded;
        private readonly Sprite[] _titleArt = new Sprite[2];   // [0]=portrait, [1]=landscape

        // Owner 2026-06-04: web-standard SPLASH GATE. Browsers block audio until a
        // user gesture; the first button press on this menu is that gesture.
        // _splashActive = the menu is up and accepting a choice.
        private bool _splashActive;

        // The 9-screen cinematic / cold-open owns the screen (suppresses the watchdog).
        private bool _introPlaying;

        // DEF-253 hard watchdog: if the cold-open fallback stalls (a WebGL await that
        // never resolves), this plain-Update unscaled timer force-returns to the title
        // menu. Belt-and-braces on top of RunArrival's SafeStage timeouts.
        private bool _arrivalRunning;
        private float _arrivalStart;
        private const float MaxIntroSeconds = 8f;

        private void Awake()
        {
            DisableLegacyUiDocuments();
        }

        private void OnEnable()
        {
            // Animated star/comet background — replaces the React build's
            // landing-page parallax that owners said pulled players in during
            // the 10-15 s decision window. Spawned once per Title scene load.
            if (GameObject.Find("TitleStarfield") == null)
                new GameObject("TitleStarfield").AddComponent<TitleStarfield>();
        }

        private void Start()
        {
            using var _ = FlowTrace.Enter("Onboarding", "TitleController.Start (uGUI title menu)");
            BuildTitleMenu();
            SetTitleVisible(true);

            // W9 (WO-714): the kit's shared open ease (PanelOpenCloseFx, P8) — a gentle
            // fade-in only (null scale target: the full-bleed cover art must not scale),
            // so the front door opens with the same feel as every kit panel. Attached
            // AFTER the show call so the visibility reset cannot stamp on the ease.
            // Skin only — no element moves, no flow change.
            ElarionUiKit.AttachPanelOpenFx(_canvas, null);
            _splashActive = true;
        }

        private void OnDestroy()
        {
            CloseStartNewConfirm();   // WO-1688: the wipe sheet never outlives the title
            if (_canvas != null) Destroy(_canvas);
        }

        /// <summary>WO-1688: pad a confirm face up to the kit touch floor once its layout has
        /// resolved. Returns false while the rect still reads 0 (layout pending).</summary>
        private static bool EnsureConfirmTouchFloor(Button b)
        {
            if (b == null) return true;                       // no face — nothing to do
            var rt = b.transform as RectTransform;
            if (rt == null) return true;
            float h = rt.rect.height;
            if (h <= 0.5f) return false;                      // layout not resolved yet
            if (h < ElarionUiKit.MinTouchPx)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + (ElarionUiKit.MinTouchPx - h));
            return true;
        }

        /// <summary>
        /// WO-C: this controller renders in uGUI now, but the Title scene (built by
        /// OnboardingSceneBuilder before the conversion) still carries the legacy
        /// UIDocuments — the "TitleScreen UIDocument" and the one RequireComponent
        /// used to force onto this GameObject. Five enabled documents sharing the
        /// one OnboardingPanelSettings asset was the input-eating duplicate-panel
        /// bug, so disable ours explicitly: they render nothing for us any more and
        /// must not keep a PanelRaycaster in the click stack.
        /// </summary>
        private void DisableLegacyUiDocuments()
        {
            Guard.Try("Onboarding", "disable legacy Title UIDocuments", () =>
            {
                int disabled = 0;
                if (_titleDocument != null && _titleDocument.enabled)
                {
                    _titleDocument.enabled = false;
                    disabled++;
                }
                var own = GetComponent<UnityEngine.UIElements.UIDocument>();
                if (own != null && own.enabled)
                {
                    own.enabled = false;
                    disabled++;
                }
                // Census note: the bumper (SplashLoading) + MusicSelectionPanel docs are
                // owned by their controllers and are deliberately NOT touched here.
                FlowTrace.Step("Onboarding",
                    $"WO-C: disabled {disabled} legacy Title UIDocument(s) — the title renders via uGUI now " +
                    $"(bumper wired={_splash != null}, left to its owner).");
            });
        }

        // =====================================================================
        //  Title menu (code-built uGUI on the Obsidian kit)
        // =====================================================================

        private void BuildTitleMenu()
        {
            if (_canvas != null) return;

            _canvas = ElarionUiKit.BuildModalCanvas("TitleScreenUI", 100);
            SceneRootAdopt(_canvas);

            // Obsidian floor — the screen can NEVER blank, even with no art on disk.
            var fillGo = new GameObject("BackdropFill", typeof(Image));
            fillGo.transform.SetParent(_canvas.transform, false);
            Stretch(fillGo);
            _backdropFill = fillGo.GetComponent<Image>();
            _backdropFill.color = ElarionUiKit.ObsidianFill;
            _backdropFill.raycastTarget = true;   // eat stray taps outside the buttons

            // Cover art (title text is baked into the art). FIT-TO-SCREEN (owner 2026-07-16
            // "splash needs to fit to screen; looks awful"): EnvelopeParent scale-and-crop was
            // slicing the baked-in "DEFENDERS OF THE REALM" title off both edges on any window
            // whose aspect != the art's (proven on the web capture). FitInParent shows the WHOLE
            // art — the title always reads in full — and the obsidian BackdropFill below covers
            // the letterbox margin so the screen still never blanks.
            var artGo = new GameObject("BackdropArt", typeof(Image), typeof(AspectRatioFitter));
            artGo.transform.SetParent(_canvas.transform, false);
            Stretch(artGo);
            _backdropArt = artGo.GetComponent<Image>();
            _backdropArt.raycastTarget = false;
            _backdropArt.preserveAspect = false;
            _backdropFitter = artGo.GetComponent<AspectRatioFitter>();
            _backdropFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            ApplyBackdropArt(force: true);

            // Art-missing fallback: the kit-typography title block (canon strings).
            if (!_backdropArtLoaded)
                BuildTitleTextBlock(_canvas.transform);

            BuildButtonColumn(_canvas.transform);
#if !GOOGLE_PLAY
            // WO-1363: the badge and its copy are compiled OUT of the Play artifact, not
            // hidden at runtime - a runtime guard still leaves the literal in the binary.
            BuildSkrBadge(_canvas.transform);
#endif

            FlowTrace.Step("Onboarding",
                $"Title menu built (uGUI) — art={( _backdropArtLoaded ? "loaded" : "MISSING (text fallback)")} " +
                $"saveExists={HasExistingSave()}.");
        }

        /// <summary>Landscape/portrait cover art, swapped when the orientation flips.</summary>
        private void ApplyBackdropArt(bool force = false)
        {
            bool landscape = Screen.width >= Screen.height;
            if (!force && _backdropArtLoaded && landscape == _backdropArtLandscape) return;

            int slot = landscape ? 1 : 0;
            if (_titleArt[slot] == null)
            {
                var tex = Resources.Load<Texture2D>(landscape ? "Title/Title_L" : "Title/Title_H");
                if (tex != null)
                    _titleArt[slot] = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                                    new Vector2(0.5f, 0.5f), 100f);
            }

            var sprite = _titleArt[slot];
            if (sprite == null)
            {
                if (_backdropArt != null) _backdropArt.enabled = false;
                _backdropArtLoaded = false;
                FlowTrace.Warn("Onboarding",
                    $"Title art Resources/Title/{(landscape ? "Title_L" : "Title_H")} not found — obsidian + text fallback.");
                return;
            }

            _backdropArt.enabled = true;
            _backdropArt.sprite = sprite;
            _backdropFitter.aspectRatio = sprite.rect.height > 0f
                ? sprite.rect.width / sprite.rect.height : 1f;
            _backdropArtLandscape = landscape;
            _backdropArtLoaded = true;
        }

        /// <summary>Game title / series / tagline in the kit's title typography —
        /// shown only when the cover art (which bakes the title in) is absent.</summary>
        private static void BuildTitleTextBlock(Transform parent)
        {
            var title = ElarionUiKit.Label(parent, CanonStrings.GameTitle,
                0.70f, 0.84f, ElarionUi.Parchment, 64,
                TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f, spacing: 3f, bold: true);
            ElarionUiKit.EnsureFont(title, ElarionUiKit.FontRole.Title);

            var series = ElarionUiKit.Label(parent, CanonStrings.GameSubtitle,
                0.655f, 0.70f, ElarionUi.Gold, 26,
                TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f, spacing: 5f, bold: true);
            ElarionUiKit.EnsureFont(series, ElarionUiKit.FontRole.Title);

            var tagline = ElarionUiKit.Label(parent, CanonStrings.Tagline,
                0.60f, 0.65f, ElarionUi.ParchmentDim, 24,
                TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f);
            ElarionUiKit.EnsureFont(tagline, ElarionUiKit.FontRole.Body);
        }

        /// <summary>The Obsidian button row (owner F8 2026-07-03): small, clean, rounded
        /// buttons on ONE horizontal row, bottom-centre over the art. "Start New" label
        /// is forced WHITE for high contrast ("pops").</summary>
        private void BuildButtonColumn(Transform parent)
        {
            var row = new GameObject("TitleButtons", typeof(RectTransform), typeof(Image));
            row.transform.SetParent(parent, false);
            var rt = (RectTransform)row.transform;
            // A single low, wide band — small clean buttons side-by-side.
            //
            // ⛔ THE ROW HEIGHT IS ARITHMETIC, NOT TASTE, AND THE OLD COMMENT HERE LIED
            // (WO-1664, 2026-09-10). It read "Kept a healthy ~7% screen-height so the touch
            // target stays tappable on mobile" — and the device disagreed in its own words:
            //   [touch-oracle] CLAMP FIRED TitleScreenUI/TitleButtons/ObsBtn_Continue:
            //   authored 399.5x69.5 -> grown 399.5x112 (1.0x on W, 1.61x on H)
            // (Builds/device-frames/2026-09-10_1137_363866_logcat.txt, and byte-identical on the
            // 363786 log before it, so it is standing residue, not a one-build blip). All three
            // faces fired. ClampMinTouch grows a sub-floor face SYMMETRICALLY ABOUT ITS CENTRE,
            // so a rescued face spills into BOTH neighbours — here, into the row's own chrome.
            //
            // THE FORMULA. A face fills 0.10..0.90 of this row (see the loop below), so
            //     faceH_px = (anchorMax.y - anchorMin.y) x refHeight x 0.80
            // and the row must satisfy faceH_px >= ElarionUiKit.MinTouchPx (112).
            // refHeight is the CanvasScaler's post-scale height, and the kit scaler is
            // referenceResolution (1080,1920), MatchWidthOrHeight 0.5 (ElarionUiKit.cs:109-111):
            //     scale = (W/1080)^0.5 x (H/1920)^0.5 ,  refHeight = H / scale
            //   1920x1080 -> 1080.0 | 2340x1080 -> 978.4 | 2670x1200 -> 965.4  (the Seeker)
            // 965.4 is the SMALLEST of the captured aspects, so the band is authored against it —
            // clearing the floor there clears it everywhere.
            //     minimum row height = 112 / (965.4 x 0.80) = 0.1450
            // ⚠ 0.045..0.190 (the first-draft value) resolves to 0.145 x 965.4 x 0.80 = 111.95 px
            // — SIX HUNDREDTHS OF A PIXEL UNDER THE FLOOR, and the clamp would still fire.
            // 0.045..0.195 resolves to 0.150 x 965.4 x 0.80 = 115.8 px, which clears it.
            //
            // The row grows UPWARD off a FIXED bottom margin (anchorMin.y stays 0.045), so the
            // thumb-reach edge does not move. Clearance above: this row and BuildTitleTextBlock
            // both parent to _canvas.transform (:204-206), i.e. the SAME full-canvas space, so
            // the comparison is aspect-independent — the tagline's floor is 0.60 (:265) and the
            // row's new top is 0.195, leaving 0.405 of screen height between them.
            rt.anchorMin = new Vector2(0.20f, 0.045f);
            rt.anchorMax = new Vector2(0.80f, 0.195f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tray = row.GetComponent<Image>();
            var traySprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (traySprite != null) tray.sprite = traySprite;
            tray.type = Image.Type.Simple;
            tray.color = Color.white;
            tray.raycastTarget = false;
            var well = ElarionUiKit.AddImage(row.transform, "TitleActionWell",
                new Vector2(0.01f, 0.08f), new Vector2(0.99f, 0.92f),
                new Color(0.01f, 0.01f, 0.012f, 0.98f), rounded: false);
            if (well != null) well.transform.SetAsFirstSibling();

            // Continue is Green and only present when a save exists (owner spec) —
            // resuming players see it first; fresh installs see Start New leftmost.
            bool hasSave = HasExistingSave();
            var entries = new System.Collections.Generic.List<(string label,
                ElarionUiKit.ObsidianButtonColor color, System.Action onClick, bool whiteLabel)>();
            if (hasSave)
                entries.Add(("Continue", ElarionUiKit.ObsidianButtonColor.Green, OnContinue, false));
            entries.Add(("Start New", ElarionUiKit.ObsidianButtonColor.Yellow, OnStartNew, true));
            entries.Add(("Play Intro", ElarionUiKit.ObsidianButtonColor.Gray, OnPlayIntro, false));

            // Even HORIZONTAL distribution across the row, left to right.
            const float slotGap = 0.035f;
            float slotW = (1f - slotGap * (entries.Count - 1)) / entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                float x0 = i * (slotW + slotGap);
                float x1 = x0 + slotW;
                var e = entries[i];
                var btn = ElarionUiKit.BuildObsidianButton(row.transform, e.label,
                    ElarionUiKit.ObsidianButtonStyle.Style1, e.color,
                    new Vector2(x0, 0.10f), new Vector2(x1, 0.90f), e.onClick);
                MedievalUiSkin.ApplyButton(btn, primary: e.color != ElarionUiKit.ObsidianButtonColor.Gray);
                if (btn != null && btn.targetGraphic is Image buttonImage)
                {
                    var frame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
                    if (frame != null) buttonImage.sprite = frame;
                    buttonImage.type = Image.Type.Simple;
                    buttonImage.color = Color.white;
                }
                var buttonLabel = btn != null ? btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(true) : null;
                if (buttonLabel != null) ElarionUiKit.FitSingleLine(buttonLabel, 24f, 34f);

                // Owner F8: "make the start new text white as well" — force the label
                // TMP colour to white for high contrast where requested.
                if (e.whiteLabel && btn != null)
                {
                    var lbl = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                    if (lbl != null) lbl.color = Color.white;
                }
            }
        }

#if !GOOGLE_PLAY
        /// <summary>"POWERED WITH SKR" grant badge (owner 2026-07-04, ff.skrpreview). Gated OFF by
        /// default so normal players never see it; the grant-recording build flips ff.skrpreview ON
        /// (menu / PlayerPrefs / ?skrpreview=1). One tap opens the read-only, clearly-labeled
        /// <see cref="DeNelle.Core.UI.SkrShowcasePanel"/> — branding + honest value-prop, NO wallet call.
        /// A small gold pill high-center over the art so it reads on camera without crowding the menu.
        /// <para>WO-1363: EXCLUDED FROM THE GOOGLE PLAY VARIANT AT COMPILE TIME. The badge label and
        /// the trace line are string literals; a `#if` around the CALL alone would still leave them in
        /// global-metadata.dat, so the whole method is inside the guard.</para></summary>
        private static void BuildSkrBadge(Transform parent)
        {
            // ?skrpreview=1 (WebGL) is picked up here so the grant build needs no rebuild to flip on.
            FeatureFlags.ApplyUrlActivationOnce();
            if (!FeatureFlags.SkrPreview) return;

            // TOP-LEFT corner pill (owner 2026-07-16 "SKR banner overlaps the title"): the old
            // top-CENTER placement (x 0.34-0.66, y 0.905) sat directly on the baked-in title art.
            // Move it to the top-left margin — clear of the centered title AND the top-right
            // "Sign in with Pi" corner — so it reads on camera without crowding anything.
            var btn = ElarionUiKit.BuildObsidianButton(parent, "Powered with SKR",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.015f, 0.910f), new Vector2(0.265f, 0.968f),
                () => DeNelle.Core.UI.SkrShowcasePanel.Open());

            FlowTrace.Step("Onboarding", "Title: 'Powered with SKR' grant badge shown top-left (ff.skrpreview ON).");
        }
#endif

        /// <summary>True when persisted progress exists — a chosen hero or a completed
        /// onboarding. Gates the Continue button (fresh installs have nothing to resume).</summary>
        private static bool HasExistingSave()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null) return false;
            return svc.State.HeroClass.ToNullable().HasValue || svc.State.Onboarded;
        }

        // =====================================================================
        //  Menu actions (flow contract preserved from the UITK version)
        // =====================================================================

        // =====================================================================
        //  WO-1688 — THE WIPE CONFIRM. Copy lives here, in ONE place.
        // ---------------------------------------------------------------------
        //  ⚠ EXACT WORDS FLAGGED FOR THE OWNER (WO-1688 §2.1 asks for plain copy
        //  that names what is lost, and the WO itself asks that the words be put
        //  in front of her rather than chosen silently). Facts only — the realm,
        //  the hero, the town, and that it cannot be undone. No lore, no "are you
        //  sure". Change these four constants and nothing else moves.
        // =====================================================================
        // ⚠ THE TITLE BAND IS BROKEN AND IT IS NOT THIS STRING'S FAULT (chain 41, 2026-09-10):
        //     [glyph-oracle] TEXT CULLED WHOLE [StartNewConfirm_2670x1200] '.../PanelFill/Label'
        //     ("*  Erase This Realm?") draws ZERO of 16 printable glyphs ... The band has no
        //     room for one character at the resolved size.
        // ZERO, not "a few short" - no wording of any length would seat there, so shortening
        // this is not the fix and would only hide the band defect. The band belongs to the
        // FIT-GUARD lane (WO-1690, ElarionUiKit.BuildConfirmModal); do not edit that file from
        // here. If the repaired band still cannot seat 15 glyphs, the owner-flagged fallback is
        // "Erase Realm?" (11) - a one-line flip, and deliberately NOT pre-applied, because a
        // copy change made to dodge a layout bug outlives the bug.
        private const string StartNewConfirmTitle = "Erase This Realm?";
        private const string StartNewConfirmBody =
            "Starting a new game erases your current realm — your town, your hero and everything " +
            "you have built. This cannot be undone.";
        // ⚠ THE FACES WERE SHORTENED BY MEASUREMENT, NOT BY TASTE (chain 41, 2026-09-10).
        // The first pass authored "Erase and Start New" / "Keep My Realm". The front-door
        // capture built the sheet for the first time and the glyph oracle measured the
        // result on the Seeker's real surface:
        //     [glyph-oracle] TEXT TRUNCATED [StartNewConfirm_2670x1200]
        //     '...ObsBtn_Erase and Start New/Label' ("ERASE AND START NEW") draws 12 of 16
        // Four glyphs of the most destructive label in the game were not on screen. A face
        // that cannot say its own word is worse than a short one, and the BODY above it
        // already names the loss in full ("erases your current realm — your town, your hero
        // ... This cannot be undone"), so the faces only have to name the CHOICE.
        // ⛔ Do NOT lengthen these without re-running the front-door capture and reading the
        // glyph line. The face BAND and the MinTouchPx floor are the FIT-GUARD lane's
        // (WO-1690, ElarionUiKit.BuildConfirmModal) — the WORDS are this ticket's, and
        // [face-fits] below pins them against the measured budget.
        private const string StartNewConfirmEraseLabel = "Erase";
        private const string StartNewConfirmKeepLabel = "Keep";

        /// <summary>The live wipe-confirm sheet (null when closed).</summary>
        private ElarionUiKit.ConfirmModal _startNewConfirm;
        private bool _startNewConfirmTouchFloorApplied;

        // Start New: a genuinely FRESH game, routed to the HeroSelect carousel.
        //
        // WO-1688 (P1, from a real save loss on the owner's device 2026-09-10): this
        // handler used to call ResetToNewGame() DIRECTLY. One unintended touch on the
        // middle face of the title row therefore destroyed a real player's realm nine
        // milliseconds before the hero carousel was even shown, with no confirmation and
        // no undo. The `_splashActive` latch below is a DOUBLE-PRESS guard and never was
        // a confirm; it stays, but it no longer drops on the mere press of Start New.
        private void OnStartNew()
        {
            if (!_splashActive) return;

            // FRESH INSTALL keeps today's frictionless path. A confirm over an empty save
            // is friction for nothing (WO-1688 §2.1), and HasExistingSave() is the SAME
            // predicate that already gates the Continue button — deliberately reused, so
            // there is never a second notion of "has a save" to drift apart.
            if (!HasExistingSave())
            {
                _splashActive = false;
                FlowTrace.Step("Onboarding",
                    "OnStartNew: saveExists=false (genuine fresh install) — no confirm, straight through.");
                PerformStartNew();
                return;
            }

            // A second press while the sheet is already up is a no-op, not a second sheet.
            if (_startNewConfirm != null && _startNewConfirm.canvas != null) return;

            // ⛔ THE LATCH IS NOT DROPPED HERE. It used to be, one line into this method.
            // If it dropped now and the player chose "Keep My Realm", every other title
            // face (Continue / Play Intro) would early-return on !_splashActive forever —
            // a softlock at the front door, traded for the save loss. It drops only on the
            // confirmed branch, and the modal's own full-screen scrim is what stops a
            // stray tap reaching the row underneath while the sheet is open.
            FlowTrace.Step("Onboarding",
                "OnStartNew: saveExists=true — raising the WIPE CONFIRM. NOTHING has been erased at " +
                "this point; ResetToNewGame is unreachable from here until the destructive face is chosen.");
            _startNewConfirmTouchFloorApplied = false;
            _startNewConfirm = ElarionUiKit.BuildConfirmModal(
                "StartNewConfirm",
                StartNewConfirmTitle,
                StartNewConfirmBody,
                StartNewConfirmEraseLabel,
                StartNewConfirmKeepLabel,
                onConfirm: () =>
                {
                    FlowTrace.Step("Onboarding",
                        "OnStartNew: wipe CONFIRMED by the player — proceeding to ResetToNewGame.");
                    CloseStartNewConfirm();      // sheet down BEFORE the work, as TutorialSkipUi does
                    _splashActive = false;
                    PerformStartNew();
                },
                onCancel: () =>
                {
                    // Cancel, the shared Close and a tap on the scrim ALL land here, so the
                    // safe answer is the one every accidental gesture produces.
                    FlowTrace.Step("Onboarding",
                        "OnStartNew: wipe DECLINED — the realm is untouched and the title stays live.");
                    CloseStartNewConfirm();
                },
                // The destructive face is the NON-default one and carries the Danger kind;
                // the kit lays Cancel on the LEFT (0.10-0.48) and Confirm on the RIGHT
                // (0.52-0.90), so the erase face is not under the finger that just pressed
                // START NEW in the bottom row.
                confirmKind: ElarionUiKit.ButtonKind.Danger);

            if (_startNewConfirm == null || _startNewConfirm.canvas == null)
            {
                // A confirm that failed to build must NEVER silently degrade into the old
                // one-touch wipe. Stay on the title and say so.
                FlowTrace.Fail("Onboarding",
                    "OnStartNew: the wipe confirm FAILED TO BUILD — Start New is refused this press " +
                    "rather than falling back to an unconfirmed reset. The realm is untouched.");
                _startNewConfirm = null;
                return;
            }
            SceneRootAdopt(_startNewConfirm.canvas);
        }

        /// <summary>Tear the wipe-confirm sheet down. Safe when nothing is open.</summary>
        private void CloseStartNewConfirm()
        {
            if (_startNewConfirm != null && _startNewConfirm.canvas != null)
                Destroy(_startNewConfirm.canvas);
            _startNewConfirm = null;
            _startNewConfirmTouchFloorApplied = false;
        }

        /// <summary>
        /// EVERYTHING "Start New" MEANS, in one place: the save wipe, the dialogue wipe,
        /// the onboarding-mode choice and the route. It is called from exactly two places —
        /// the fresh-install fast path and the confirm's positive action — so a declined
        /// confirm cannot leave half of a new game behind (flipping OnboardingMode to the
        /// fast path on a cancelled press would do precisely that).
        /// </summary>
        private void PerformStartNew()
        {
            // Wipe the save progression (this also clears SeenTutorials, so once-only
            // recruit/intro beats replay) AND all dialogue state — the $-toggle
            // variable storage and the gameplay->dialogue event latches — so no stale
            // toggle from a prior run carries over. Continue does NOT do this.
            // WO-1688: ResetToNewGame now takes a one-generation local backup of the
            // previous signed save as its first statement, so this line is recoverable.
            GameStateService.Instance?.ResetToNewGame();
            DeNelle.Core.DialogueResetService.ResetForNewGame();

            // DEF onboarding fast-path (owner: fast into battle). "Start New" takes the
            // FAST PATH — a brief companion hook in the village then straight to Wave 1.
            DeNelle.Core.OnboardingMode.ChooseFastPath();

            // WO-559: route to the HeroSelect CAROUSEL scene. ResetToNewGame set
            // HeroClass=None and Save()'d, so the carousel BUILDS instead of
            // self-skipping to the castle.
            FlowTrace.Step("Onboarding", "OnStartNew: routing to the HeroSelect carousel (fresh HeroClass=None).");
            SceneRouter.GoHeroSelect();
        }

        // Play Intro: the full 9-screen cinematic intro, which ends by routing to
        // hero select itself. Falls back to the StoryIntro cold-open if the intro
        // player isn't registered (a build without the dialogue assets).
        private void OnPlayIntro()
        {
            if (!_splashActive) return;
            _splashActive = false;

            // "Play Intro" opts INTO the full tutorial experience — the cinematic
            // here, then the full FTUE companion meeting in the village.
            DeNelle.Core.OnboardingMode.ChooseFullTutorial();

            // WO-559: the intro ends at the HeroSelect carousel, which SELF-SKIPS to
            // the castle when a hero is already persisted. Play Intro is a fresh
            // playthrough, so clear the persisted hero HERE so the carousel builds.
            // Does NOT touch the Continue path.
            var svc = GameStateService.Instance;
            if (svc != null && svc.State != null)
            {
                svc.State.HeroClass = HeroClassOpt.None;
                svc.Save();
                FlowTrace.Step("Onboarding", "OnPlayIntro: cleared persisted HeroClass (None) so the post-intro carousel builds.");
            }

            if (DeNelle.Core.IntroLauncher.Play != null)
            {
                // The cinematic owns the screen until it routes onward; hide the
                // title menu under it and suppress the watchdog.
                _introPlaying = true;
                SetTitleVisible(false);
                DeNelle.Core.IntroLauncher.Play.Invoke();
            }
            else
            {
                RunArrival().Forget();
            }
        }

        // Continue: resume into the Castle home hub (loads the save).
        private void OnContinue()
        {
            if (!_splashActive) return;
            _splashActive = false;
            // START-FLOW GUARANTEE: Continue routes straight to the castle (no
            // hero-select), so if a loaded/stale save has no HeroClass persisted the
            // body builder would reach build with an unset class. Set it HERE at the
            // load source before routing. It persists PlayableHeroes.Default (Knight) -
            // a FALLBACK for a save that never made a pick, not a force. Since the
            // 2026-08-05 unlock (ff.knightonly defaults OFF) ChooseHero keeps a real
            // Ranger/Mage pick; this branch only ever fires when there is no pick at all.
            var svc = GameStateService.Instance;
            if (svc != null && (svc.State == null || !svc.State.HeroClass.ToNullable().HasValue))
            {
                FlowTrace.Warn("Onboarding",
                    "OnContinue: loaded save had no HeroClass — persisting Knight (V1) at the load source before GoCastle.");
                svc.ChooseHero(HeroClass.Knight);
            }
            SceneRouter.GoCastle();
        }

        // =====================================================================
        //  Cold-open fallback (Play Intro without a registered IntroLauncher)
        // =====================================================================

        /// <summary>
        /// Plays the StoryIntro cold-open then returns to the title menu. Each stage
        /// is time-boxed (SafeStage) AND covered by the DEF-253 Update watchdog so a
        /// stalled WebGL await can never strand the player on a dead screen.
        /// </summary>
        private async UniTask RunArrival()
        {
            FlowTrace.Step("Onboarding", "Arrival (cold-open fallback): start.");
            _arrivalRunning = true;
            _arrivalStart = Time.unscaledTime;
            SetTitleVisible(false);

            // The cold open is a multi-beat cinematic: SafeStage passes ForceHide as
            // the on-timeout KILL so a timed-out cinematic is genuinely CANCELLED
            // (CTS cancelled, overlay torn down), not merely abandoned to render on.
            if (_storyIntro != null)
                await SafeStage(_storyIntro.Play(), "storyIntro", () => _storyIntro.ForceHide());
            // Belt-and-braces: ForceHide is idempotent — ensure the overlay is down
            // even on the success path.
            if (_storyIntro != null) _storyIntro.ForceHide();

            if (!_arrivalRunning) return;   // watchdog already returned us to the menu
            _arrivalRunning = false;
            SetTitleVisible(true);
            _splashActive = true;
            FlowTrace.Step("Onboarding", "Arrival: cold-open done — title menu restored.");
        }

        /// <summary>
        /// Awaits an arrival stage but never lets it hang the boot: on timeout or
        /// exception it invokes <paramref name="onTimeout"/> (the authoritative KILL
        /// — pass <c>StoryIntroController.ForceHide</c>) and returns, so the title
        /// menu is always reachable. UNSCALED timeout (DEF-253): a scaled .Timeout
        /// never elapses if anything set Time.timeScale=0.
        /// </summary>
        private static async UniTask SafeStage(UniTask stage, string name, System.Action onTimeout = null)
        {
            try
            {
                await stage.Timeout(System.TimeSpan.FromSeconds(6f),
                                    Cysharp.Threading.Tasks.DelayType.UnscaledDeltaTime);
            }
            catch (System.Exception e)
            {
                try { onTimeout?.Invoke(); }
                catch (System.Exception killEx)
                {
                    FlowTrace.Warn("Onboarding", $"Arrival stage '{name}' kill threw: {killEx.Message}");
                }
                FlowTrace.Warn("Onboarding",
                    $"Arrival stage '{name}' skipped (timeout/exception) — cancelled + returning to the title. {e.Message}");
            }
        }

        private void Update()
        {
            // Orientation flip — swap the landscape/portrait cover art.
            if (_backdropArt != null && _canvas != null && _canvas.activeSelf)
                ApplyBackdropArt();

            // WO-1688: MinTouchPx on the WIPE CONFIRM's two faces. The kit modal lays its
            // buttons out as panel FRACTIONS, which can resolve under the touch floor at a
            // short landscape aspect — so measure one frame after open (rects are valid
            // post-layout) and pad any short face up via sizeDelta. Padding, not growth:
            // anchors are untouched. Same idiom as TutorialSkipUi.EnsureTouchFloor, and it
            // matters more here: the two faces are "erase everything" and "keep it".
            // ⚠ THIS IS A NET, NOT THE FIX, and chain 41 measured exactly how far short:
            //     [touch-oracle] SUB-TOUCH-FLOOR BAND [StartNewConfirm_2670x1200] both faces
            //     resolve 356.9x48.5 ref px -- shortest side 48.5 is 63.5 px UNDER
            //     ElarionUiKit.MinTouchPx (112) ... Author the band AT the floor.
            // The oracle's advice is the ruling: the BAND must be authored at the floor, in
            // ElarionUiKit.BuildConfirmModal, which is the FIT-GUARD lane's file (WO-1690) and
            // must not be edited from here. Growing a 48.5 px face by 63.5 px at runtime
            // spills it symmetrically into both neighbours, so this pass keeps the control
            // TAPPABLE while the band is still wrong - it does not make the band right.
            if (_startNewConfirm != null && _startNewConfirm.canvas != null && !_startNewConfirmTouchFloorApplied)
            {
                bool measured = EnsureConfirmTouchFloor(_startNewConfirm.confirm)
                                & EnsureConfirmTouchFloor(_startNewConfirm.cancel);
                if (measured) _startNewConfirmTouchFloorApplied = true;
            }

            // While the 9-screen cinematic plays it OWNS the screen and routes onward
            // itself — never force the title up over it.
            if (_introPlaying) return;

            // DEF-253 BLOCKER watchdog: if the cold-open fallback stalled past every
            // SafeStage timeout, force the title menu back (never-stuck fallback).
            if (_arrivalRunning && Time.unscaledTime - _arrivalStart > MaxIntroSeconds)
            {
                FlowTrace.Warn("Onboarding",
                    "DEF-253 watchdog tripped — force-returning to the title menu (never-stuck fallback).");
                _arrivalRunning = false;
                if (_storyIntro != null) _storyIntro.ForceHide();
                SetTitleVisible(true);
                _splashActive = true;
            }
        }

        // =====================================================================
        //  Small helpers
        // =====================================================================

        private void SetTitleVisible(bool visible)
        {
            if (_canvas == null) return;
            _canvas.SetActive(visible);
            // W9 guard: hiding the canvas mid open-ease stops the fx coroutine; force
            // the CanvasGroup fully opaque on every SHOW so the restored title menu
            // can never come back stuck semi-transparent.
            if (visible)
            {
                var group = _canvas.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
            }
        }

        /// <summary>Keeps the built canvas in this controller's scene so scene unload
        /// tears it down with the Title scene (BuildModalCanvas creates at root).</summary>
        private void SceneRootAdopt(GameObject go)
        {
            if (go != null && go.scene != gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        }

        /// <summary>Full-rect stretch for a fresh uGUI element.</summary>
        private static void Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
