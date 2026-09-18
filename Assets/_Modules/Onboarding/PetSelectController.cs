// =============================================================================
// PetSelectController — drives the pet-select screen (intro flow).
// -----------------------------------------------------------------------------
// The owner-acceptance-checklist "Intro & first-run flow" calls out a missing
// pet-select screen: the three starter Wardens + PetDeployer existed, but
// there was no pick-a-pet UI. This is that screen — a sibling of hero-select,
// shown right after it.
//
// THE SCREEN is built ENTIRELY IN CODE — no UXML, no USS. Project memory:
// UXML-sourced UIDocuments come up blank in player builds (BattleHUD / BuildMenu
// learned the hard way), so this screen constructs its root / header / card row /
// footer directly on the UIDocument's rootVisualElement with inline styles
// (the same idiom StoryIntroController uses). The inline values mirror the old
// SelectScreen.uss so the look is unchanged. Three pet cards — Aether Sprite /
// Flame Pup / Ice Wolf — built at runtime from the canonical pets.json via
// IntroPetCatalog (a StreamingAssets read, not a DeNelle.Pets dependency —
// module isolation).
//
// COPY: the screen title / subtitle / confirm label come from en.json via
// CanonStrings; the per-pet name + archetype come straight from pets.json.
// The card glyph is the pet's first initial; the accent uses the pet's own
// glowColor from pets.json — all presentation, no inline canon strings.
//
// PERSISTENCE: on confirm the chosen pet id is written to
// GameState.StarterPetId (#2 — "id of the onboarding starter pet"). There is
// no GameStateService.ChooseStarterPet mutator, so the controller writes the
// SO field directly and calls Save() — see the integrator note at the foot of
// this file. The id space matches DeNelle.Pets.PetCatalog ("pet-aether-sprite"
// etc.) so the village pet system resolves the pick through its own catalog.
//
// On confirm the flow routes to the Village (SceneRouter.GoVillage) — the end
// of the intro chain: Title -> HeroSelect -> PetSelect -> Village.
//
// Lives in DeNelle.Onboarding; references DeNelle.Core only — module isolation.
// =============================================================================

using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Drives the pet-select screen: builds the three starter-pet cards from
    /// pets.json, tracks the player's pick, and on confirm writes
    /// <see cref="GameState.StarterPetId"/> and routes to the Village.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PetSelectController : MonoBehaviour
    {
        // ── Palette — SOURCED from the shared ElarionUi town-HUD language ─────
        // (WO restyle: matches the hero-select / town HUD — dark glass + ornate
        // gold-rune frames — replacing the retired hardcoded purple/amber so the
        // onboarding chain reads as ONE designed game. Role-named locals keep the
        // layout below unchanged while every colour resolves canonically.)
        private static readonly Color ScreenBg   = ElarionUi.PanelStoneDark;                                       // full-screen stone
        private static readonly Color CardBg      = ElarionUi.PanelStoneDark;                                       // card rest fill
        private static readonly Color CardBgActive= ElarionUi.PanelStone;                                           // card selected fill
        private static readonly Color CardBorder  = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.55f); // soft gold rim
        private static readonly Color Amber       = ElarionUi.Gold;                                                 // role line / accents → runic gold
        private static readonly Color PortraitBg  = new Color(0f, 0f, 0f, 0.30f);                                   // dark backing well (town-HUD niche)
        private static readonly Color TitleText   = ElarionUi.Parchment;                                            // primary text
        private static readonly Color SubtitleText= ElarionUi.ParchmentDim;                                         // subtitle text
        private static readonly Color BlurbText   = new Color(ElarionUi.ParchmentDim.r, ElarionUi.ParchmentDim.g, ElarionUi.ParchmentDim.b, 0.85f); // muted flavour
        private static readonly Color GlyphColorBase = ElarionUi.Gilt;                                              // glyph fallback accent
        private static readonly Color ConfirmText = ElarionUi.Ink;                                                  // dark ink on gold CTA
        private static readonly Color ConfirmDisabledText = new Color(ElarionUi.Parchment.r, ElarionUi.Parchment.g, ElarionUi.Parchment.b, 0.35f);
        private static readonly Color ConfirmDisabledBg   = ElarionUi.Disabled;                                     // inert stone grey

        // ── en.json keys for the screen's own copy ───────────────────────────
        private const string TitleKey = "petSelect.title";
        private const string SubtitleKey = "petSelect.subtitle";
        private const string ConfirmKey = "petSelect.confirm";

        // ── en.json key prefix for the per-element pet blurb (elementBlurb.*) ─
        private const string ElementBlurbPrefix = "elementBlurb.";

        [Header("UI")]
        [Tooltip("UIDocument hosting PetSelectScreen.uxml. Falls back to the component on this GameObject.")]
        [SerializeField] private UIDocument _document;

        [Header("Behaviour")]
        [Tooltip("Skip straight to the Village when the save already records a starter pet " +
                 "(a returning player who finished the intro). Editor testing: disable to " +
                 "always show the screen.")]
        [SerializeField] private bool _skipWhenPetChosen = true;

        // ── Bound UI elements ────────────────────────────────────────────────
        private VisualElement _root;
        private Label _title;
        private Label _subtitle;
        private VisualElement _cardRow;
        private Button _confirmButton;

        // One card VisualElement per pet, in IntroPetCatalog order.
        private VisualElement[] _cards;

        private bool _bound;
        private bool _hasSelection;
        private string _selectedPetId;

        // DEF-263: selecting a pet routes to the Village IMMEDIATELY — one tap = go.
        // The DEF-230 confirm-beat timer (_autoAdvanceArmed/_autoAdvanceAt + the
        // Update branch) is gone; OnCardClicked now calls RouteToVillage() directly.
        // _advancing still guards the single route against a late double-tap.
        private bool _advancing;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            // WO-473 / single-hero V1: PetSelect is bypassed in the onboarding flow.
            // Belt-and-braces — if anything still loads this scene with the flag ON,
            // route straight to the castle before binding any UI (no pet step in V1).
            if (FeatureFlags.BypassPetSelect)
            {
                // WO-748: still a FRESH founding path (single-hero V1) — offer the founding
                // choice (Default Town vs Build Your Own) before the hub, then GoCastle.
                FlowTrace.Step("Onboarding", "PetSelectController.OnEnable: BypassPetSelect ON — founding choice then GoCastle (screen skipped).");
                FoundingChoiceController.PresentOrContinue(SceneRouter.GoCastle);
                return;
            }

            // A returning player who already picked a starter pet skips this
            // screen — route on to the Village before binding any UI.
            if (_skipWhenPetChosen && HasStarterPet())
            {
                SceneRouter.GoCastle();
                return;
            }

            // T-029: even when the auto-skip is OFF (editor testing) or the player
            // re-reaches this surface another way, NEVER re-offer a free pick once
            // a starter pet is bonded. Show the "you already have <pet>" state with
            // a hint on how to get another — no second pick, no overwrite.
            if (HasStarterPet())
            {
                BindAlreadyChosen();
                return;
            }

            BindElements();
        }

        private void OnDisable()
        {
            if (_confirmButton != null) _confirmButton.clicked -= OnConfirmClicked;
            _bound = false;
        }

        // DEF-263: a pet pick now routes IMMEDIATELY from OnCardClicked (one tap =
        // go), so the old DEF-230 confirm-beat Update timer is removed entirely.

        // =====================================================================
        //  Returning-player gate
        // =====================================================================

        /// <summary>True when the save already records a starter pet id.</summary>
        private static bool HasStarterPet()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null) return false;
            return !string.IsNullOrEmpty(svc.State.StarterPetId);
        }

        /// <summary>
        /// The persisted starter pet's display name (from pets.json), or a neutral
        /// "Warden" fallback when the id is unknown to the intro catalog. Used by
        /// the already-chosen state so the message names the bonded companion.
        /// </summary>
        private static string ChosenPetName()
        {
            var svc = GameStateService.Instance;
            string id = svc != null && svc.State != null ? svc.State.StarterPetId : null;
            if (!string.IsNullOrEmpty(id))
            {
                var pets = IntroPetCatalog.Pets;
                for (int i = 0; i < pets.Count; i++)
                {
                    if (pets[i] != null && pets[i].Id == id && !string.IsNullOrEmpty(pets[i].Name))
                        return pets[i].Name;
                }
            }
            return "your Warden";
        }

        // =====================================================================
        //  UI Toolkit binding
        // =====================================================================

        private void BindElements()
        {
            using var _ = FlowTrace.Enter("Onboarding", "PetSelectController.BindElements");
            var rootDoc = _document != null ? _document.rootVisualElement : null;
            if (rootDoc == null)
            {
                // P0 — no root means the pet-select renders NOTHING. Fail-loud.
                FlowTrace.Fail("Onboarding", "BindElements: NO UIDocument root — pet-select will NOT display (BLANK SCREEN).");
                return;
            }

            // CODE-BUILT scaffold (no UXML — would render blank in a player build).
            // Clear anything a stale visualTreeAsset may have cloned in, then build
            // root -> header(title+subtitle) -> card row -> footer(confirm).
            rootDoc.Clear();

            _root = new VisualElement { name = "pet-select-root" };
            _root.style.flexGrow = 1f;
            _root.style.position = Position.Absolute;
            _root.style.top = 0; _root.style.left = 0; _root.style.right = 0; _root.style.bottom = 0;
            _root.style.backgroundColor = ScreenBg;
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.alignItems = Align.Stretch;
            _root.style.justifyContent = Justify.FlexStart;
            rootDoc.Add(_root);

            // ── Header — title + subtitle, centred, from en.json ──────────────
            var header = new VisualElement { name = "select-header" };
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 28;
            header.style.paddingTop = 36; header.style.paddingBottom = 0;
            header.style.paddingLeft = 24; header.style.paddingRight = 24;
            _root.Add(header);

            _title = new Label(CanonStrings.Locale(TitleKey)) { name = "pet-select-title" };
            _title.style.fontSize = 34;
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.color = TitleText;
            _title.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.Add(_title);

            // PET-ACQUISITION REWORK (owner 2026-06-13): this screen no longer grants a
            // pet — it's a teaser. Point the player at the Echo Hollow, where the bond
            // actually happens, so the copy doesn't promise a pick this screen won't keep.
            _subtitle = new Label(
                new LocalizedText("onboarding.pet_select.teaser_subtitle").Resolve())
            { name = "pet-select-subtitle" };
            _subtitle.style.marginTop = 8;
            _subtitle.style.fontSize = 15;
            _subtitle.style.unityFontStyleAndWeight = FontStyle.Italic;
            _subtitle.style.color = SubtitleText;
            _subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            _subtitle.style.maxWidth = 720;
            header.Add(_subtitle);

            // ── Card row — the three pet cards, built below ───────────────────
            _cardRow = new VisualElement { name = "pet-card-row" };
            _cardRow.style.flexDirection = FlexDirection.Row;
            _cardRow.style.justifyContent = Justify.Center;
            _cardRow.style.alignItems = Align.Center;
            _cardRow.style.flexGrow = 1f;
            _root.Add(_cardRow);

            BuildCards();

            // ── Footer — confirm CTA, disabled until a pet is chosen ──────────
            var footer = new VisualElement { name = "select-footer" };
            footer.style.marginTop = 14;
            footer.style.alignItems = Align.Center;
            _root.Add(footer);

            _confirmButton = new Button { name = "pet-select-confirm", text = CanonStrings.Locale(ConfirmKey) };
            _confirmButton.style.minWidth = 200;
            _confirmButton.style.height = 52;
            _confirmButton.style.fontSize = 15;
            _confirmButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _confirmButton.style.borderTopLeftRadius = 12; _confirmButton.style.borderTopRightRadius = 12;
            _confirmButton.style.borderBottomLeftRadius = 12; _confirmButton.style.borderBottomRightRadius = 12;
            _confirmButton.style.borderTopWidth = 0; _confirmButton.style.borderBottomWidth = 0;
            _confirmButton.style.borderLeftWidth = 0; _confirmButton.style.borderRightWidth = 0;
            footer.Add(_confirmButton);

            _confirmButton.clicked -= OnConfirmClicked; // guard a double OnEnable
            _confirmButton.clicked += OnConfirmClicked;

            RefreshConfirmEnabled();
            _bound = true;
        }

        // =====================================================================
        //  Already-chosen state (T-029) — no second pick, no overwrite
        // =====================================================================

        /// <summary>
        /// Builds the returning-player state: a player who already bonded a starter
        /// pet does NOT get the card row. Instead we name their existing Warden and
        /// hint at how to gain another (the village/store path), then a single
        /// "Continue" CTA routes onward. The chosen pet is NEVER re-picked or
        /// overwritten here — this surface is read-only for a bonded player.
        ///
        /// Built fully in code (no UXML — renders blank in player builds), mirroring
        /// the BindElements idiom. Copy is inline-literal (not a CanonStrings key) so
        /// it never shows the "[[missing:…]]" placeholder if en.json lacks the key —
        /// the pet NAME is the only canonical value and comes from pets.json.
        /// </summary>
        private void BindAlreadyChosen()
        {
            var rootDoc = _document != null ? _document.rootVisualElement : null;
            if (rootDoc == null)
            {
                FlowTrace.Fail("Onboarding", "BindAlreadyChosen: NO UIDocument root — already-chosen state will NOT display (BLANK SCREEN).");
                return;
            }

            rootDoc.Clear();

            _root = new VisualElement { name = "pet-select-root" };
            _root.style.flexGrow = 1f;
            _root.style.position = Position.Absolute;
            _root.style.top = 0; _root.style.left = 0; _root.style.right = 0; _root.style.bottom = 0;
            _root.style.backgroundColor = ScreenBg;
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.style.paddingLeft = 24; _root.style.paddingRight = 24;
            rootDoc.Add(_root);

            string petName = ChosenPetName();

            var heading = new Label(
                new LocalizedText("onboarding.pet_select.already_bonded_title").Resolve())
            { name = "pet-already-title" };
            heading.style.fontSize = 30;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color = TitleText;
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            _root.Add(heading);

            // WO-1857: interpolated copy becomes ONE positional format key ({0} = the pet's
            // own name), so a locale can put the name anywhere in the sentence.
            var bonded = new Label(
                LocalText.Format("onboarding.pet_select.already_bonded_body", petName))
            { name = "pet-already-bonded" };
            bonded.style.marginTop = 12;
            bonded.style.fontSize = 15;
            bonded.style.unityFontStyleAndWeight = FontStyle.Italic;
            bonded.style.color = SubtitleText;
            bonded.style.unityTextAlign = TextAnchor.MiddleCenter;
            bonded.style.whiteSpace = WhiteSpace.Normal;
            bonded.style.maxWidth = 640;
            _root.Add(bonded);

            var hint = new Label(
                new LocalizedText("onboarding.pet_select.already_bonded_hint").Resolve())
            { name = "pet-already-hint" };
            hint.style.marginTop = 18;
            hint.style.fontSize = 13;
            hint.style.color = BlurbText;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.maxWidth = 600;
            _root.Add(hint);

            var continueButton = new Button
            {
                name = "pet-already-continue",
                text = new LocalizedText("common.continue").Resolve()
            };
            continueButton.style.marginTop = 28;
            continueButton.style.minWidth = 200;
            continueButton.style.height = 52;
            continueButton.style.fontSize = 15;
            continueButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            continueButton.style.backgroundColor = Amber;
            continueButton.style.color = ConfirmText;
            SetBorderRadius(continueButton, 12);
            SetBorderWidth(continueButton, 0);
            continueButton.clicked += () =>
            {
                if (_advancing) return;
                _advancing = true;
                SceneRouter.GoCastle();
            };
            _root.Add(continueButton);

            _bound = true;
        }

        /// <summary>Builds one card per starter pet from the pets.json catalog.</summary>
        private void BuildCards()
        {
            if (_cardRow == null) return;
            _cardRow.Clear();

            var pets = IntroPetCatalog.Pets;
            _cards = new VisualElement[pets.Count];

            if (pets.Count == 0)
            {
                // R — never a blank screen. The catalog came up empty (pets.json missing /
                // failed to parse), so build a VISIBLE fallback card with a Continue path
                // rather than leaving the player on an empty pet-select they can't pass.
                FlowTrace.Fail("Onboarding",
                    "BuildCards: NO starter pets loaded from pets.json — pet-select has nothing to show. " +
                    "Rendering a visible fallback card so the screen is never blank/dead-ended.");
                _cardRow.Add(BuildEmptyFallbackCard());
                return;
            }

            for (int i = 0; i < pets.Count; i++)
            {
                VisualElement card = BuildCard(pets[i]);
                _cardRow.Add(card);
                _cards[i] = card;
            }

            FlowTrace.Step("Onboarding", $"BuildCards: built {pets.Count} starter-pet card(s).");
        }

        /// <summary>
        /// R (never-blank): a visible fallback card shown when the pets.json catalog is
        /// empty. Explains the situation and offers a Continue button into town (the Echo
        /// Hollow is the real bonding surface anyway) — so an empty catalog never leaves
        /// the player on a dead, blank pet-select. Built in code, inline-styled.
        /// </summary>
        private VisualElement BuildEmptyFallbackCard()
        {
            var card = new VisualElement { name = "pet-card-fallback" };
            card.style.width = 360;
            card.style.marginLeft = 10; card.style.marginRight = 10;
            SetBorderRadius(card, 14);
            SetBorderWidth(card, 2);
            SetBorderColor(card, CardBorder);
            card.style.backgroundColor = CardBg;
            card.style.flexDirection = FlexDirection.Column;
            card.style.alignItems = Align.Center;
            card.style.paddingTop = 28; card.style.paddingBottom = 28;
            card.style.paddingLeft = 22; card.style.paddingRight = 22;

            var heading = new Label(
                new LocalizedText("onboarding.pet_select.awaits_title").Resolve());
            heading.style.fontSize = 20;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color = TitleText;
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(heading);

            var blurb = new Label(
                new LocalizedText("onboarding.pet_select.awaits_blurb").Resolve());
            blurb.style.marginTop = 10;
            blurb.style.fontSize = 13;
            blurb.style.color = BlurbText;
            blurb.style.unityTextAlign = TextAnchor.MiddleCenter;
            blurb.style.whiteSpace = WhiteSpace.Normal;
            blurb.style.maxWidth = 320;
            card.Add(blurb);

            var go = new Button
            {
                name = "pet-fallback-continue",
                text = new LocalizedText("common.continue").Resolve()
            };
            go.style.marginTop = 22;
            go.style.minWidth = 180;
            go.style.height = 48;
            go.style.fontSize = 15;
            go.style.unityFontStyleAndWeight = FontStyle.Bold;
            go.style.backgroundColor = Amber;
            go.style.color = ConfirmText;
            SetBorderRadius(go, 12);
            SetBorderWidth(go, 0);
            go.clicked += () =>
            {
                if (_advancing) return;
                _advancing = true;
                SceneRouter.GoCastle();
            };
            card.Add(go);

            return card;
        }

        /// <summary>
        /// Builds one pet card VisualElement from a pets.json entry. Fully
        /// inline-styled (code-built — no USS); values mirror the old
        /// SelectScreen.uss .select-card family.
        /// </summary>
        private VisualElement BuildCard(IntroPetInfo pet)
        {
            Color accent = pet.GlowUnityColor;

            var card = new VisualElement { name = $"pet-card-{pet.Id}" };
            card.style.width = 240;
            card.style.marginLeft = 10; card.style.marginRight = 10;
            SetBorderRadius(card, 14);
            SetBorderWidth(card, 2);
            SetBorderColor(card, CardBorder);
            card.style.backgroundColor = CardBg;
            card.style.overflow = Overflow.Hidden;
            card.style.flexDirection = FlexDirection.Column;

            // Portrait block — the pet's PNG (Resources/PetPortraits/<pet.Id>)
            // with the single-initial glyph as a guaranteed fallback.
            var portrait = new VisualElement();
            portrait.style.height = 130;
            portrait.style.alignItems = Align.Center;
            portrait.style.justifyContent = Justify.Center;
            portrait.style.backgroundColor = PortraitBg;
            var portraitTex = Resources.Load<Texture2D>($"PetPortraits/{pet.Id}");
            if (portraitTex != null)
            {
                portrait.style.backgroundImage = new StyleBackground(portraitTex);
                portrait.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            }
            else
            {
                string initial = !string.IsNullOrEmpty(pet.Name)
                    ? pet.Name.Substring(0, 1).ToUpperInvariant()
                    : "?";
                var glyph = new Label(initial);
                glyph.style.fontSize = 72;
                glyph.style.unityFontStyleAndWeight = FontStyle.Bold;
                glyph.style.color = accent;
                portrait.Add(glyph);
            }
            card.Add(portrait);

            // Element-coloured accent strip.
            var accentStrip = new VisualElement();
            accentStrip.style.height = 4;
            accentStrip.style.backgroundColor = accent;
            card.Add(accentStrip);

            // Text body — name / archetype role / element blurb.
            var body = new VisualElement();
            body.style.paddingTop = 12; body.style.paddingBottom = 16;
            body.style.paddingLeft = 14; body.style.paddingRight = 14;
            body.style.flexGrow = 1f;

            // WO-1857: the no-name fallback is a rendered label, so it gets a key too
            // (same precedent as common.job, the registry's "fallback label" row).
            var nameLabel = new Label(pet.Name
                ?? new LocalizedText("onboarding.pet_select.warden_name_fallback").Resolve());
            nameLabel.style.fontSize = 20;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = TitleText;
            body.Add(nameLabel);

            var roleLabel = new Label(pet.Archetype ?? string.Empty);
            roleLabel.style.marginTop = 3;
            roleLabel.style.fontSize = 11;
            roleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            roleLabel.style.color = Amber;
            body.Add(roleLabel);

            // The blurb is the canon element flavour line from en.json
            // (elementBlurb.aether / .flame / .ice).
            var blurbLabel = new Label(ElementBlurb(pet.Element));
            blurbLabel.style.marginTop = 8;
            blurbLabel.style.fontSize = 12;
            blurbLabel.style.color = BlurbText;
            blurbLabel.style.whiteSpace = WhiteSpace.Normal;
            body.Add(blurbLabel);

            card.Add(body);

            // The whole card is the hit target — capture the id in a local.
            string capturedId = pet.Id;
            card.RegisterCallback<PointerDownEvent>(_ => OnCardClicked(capturedId));

            return card;
        }

        // ── Inline-style border helpers (USS shorthands have no single setter) ─
        private static void SetBorderRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        private static void SetBorderWidth(VisualElement e, float w)
        {
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            e.style.borderTopColor = c; e.style.borderBottomColor = c;
            e.style.borderLeftColor = c; e.style.borderRightColor = c;
        }

        /// <summary>
        /// Resolves the canon element blurb for a pet's element token. Element
        /// ids in pets.json (aether/flame/ice) key directly into the en.json
        /// <c>elementBlurb.*</c> family.
        /// </summary>
        private static string ElementBlurb(string element)
        {
            if (string.IsNullOrEmpty(element)) return string.Empty;
            return CanonStrings.Locale(ElementBlurbPrefix + element);
        }

        // =====================================================================
        //  Selection
        // =====================================================================

        /// <summary>
        /// A pet card was tapped — mark it active, clear the rest, and route to the
        /// Village IMMEDIATELY (DEF-263: one tap = go; the DEF-230 confirm-beat
        /// delay is removed). Once the route has fired (_advancing) late taps are
        /// ignored so a second tap can't double-route.
        /// </summary>
        private void OnCardClicked(string petId)
        {
            if (_advancing) return;   // route already in flight — ignore late taps
            _selectedPetId = petId;
            _hasSelection = true;
            UpdateCardHighlight();
            RefreshConfirmEnabled();

            // DEF-263: selecting a pet IS the commit — go straight to the Village.
            _advancing = true;
            RouteToVillage();
        }

        /// <summary>
        /// Marks the active pet card (amber ring + lifted bg) and clears the
        /// others. Inline styling — the cards are code-built, so the old USS
        /// .select-card--active rule has nothing to hook onto.
        /// </summary>
        private void UpdateCardHighlight()
        {
            if (_cards == null) return;
            var pets = IntroPetCatalog.Pets;
            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] == null || i >= pets.Count) continue;
                bool active = _hasSelection && pets[i].Id == _selectedPetId;
                SetBorderColor(_cards[i], active ? Amber : CardBorder);
                _cards[i].style.backgroundColor = active ? CardBgActive : CardBg;
            }
        }

        /// <summary>
        /// Enables the confirm button only once a pet is chosen; before then it
        /// shows a dimmed, inert-looking state. Inline styling (code-built button).
        /// </summary>
        private void RefreshConfirmEnabled()
        {
            if (_confirmButton == null) return;
            _confirmButton.SetEnabled(_hasSelection);
            _confirmButton.style.backgroundColor = _hasSelection ? Amber : ConfirmDisabledBg;
            _confirmButton.style.color = _hasSelection ? ConfirmText : ConfirmDisabledText;
        }

        // =====================================================================
        //  Confirm — write the choice + route to the Village
        // =====================================================================

        /// <summary>
        /// Confirm tapped: routes to the Castle — the end of the intro flow.
        /// Audit P3 / PET-ACQUISITION REWORK (owner 2026-06-13): this screen NO LONGER
        /// grants a pet. The card tap is a non-binding preview; the actual pet is acquired
        /// from the Echo Hollow pet-shop (PetHouse Yarn node → &lt;&lt;spawn_named_pet&gt;&gt;
        /// → PetAcquisitionService.Acquire). Nothing is persisted here. See RouteToVillage().
        /// </summary>
        private void OnConfirmClicked()
        {
            if (!_hasSelection) return;
            if (_advancing) return;   // auto-advance already routing
            _advancing = true;
            RouteToVillage();
        }

        /// <summary>
        /// Routes onward to the Castle — the end of the intro flow.
        ///
        /// PET-ACQUISITION REWORK (owner 2026-06-13): this screen is NO LONGER the
        /// pet-granting path. The pet is acquired SOLELY from the Echo Hollow pet-shop
        /// (the PetHouse Yarn node → <<spawn_named_pet>> → PetAcquisitionService.Acquire
        /// + PetDeployer.DeployChosen). So this controller must NOT write
        /// GameState.StarterPetId or otherwise pre-grant/pre-spawn a pet — doing so
        /// pre-empted the store as the single opener (the owner's "change option back").
        /// The card tap is now a non-binding preview; whatever the player taps, we route
        /// on WITHOUT persisting ownership, and they bond their Echo at the Hollow in-town.
        /// (The screen could be retired entirely; it is kept as a teaser + routed to the
        /// Hollow rather than deleted.)
        /// </summary>
        private void RouteToVillage()
        {
            // WO-748: at founding, insert the "Default Town vs Build Your Own" choice
            // BEFORE the first hub load (the choice must set StrategicPlacementMigrated
            // before the Castle-scene migration writer runs). PresentOrContinue self-gates:
            // a returning / already-founded player continues straight to GoCastle.
            FoundingChoiceController.PresentOrContinue(SceneRouter.GoCastle);
        }
    }
}

// =============================================================================
// INTEGRATOR NOTES — wiring the pet-select scene.
// -----------------------------------------------------------------------------
//   1. The PetSelect scene is generated by the editor builder
//      DeNelle.Editor.IntroFlowSceneBuilder.BuildAll alongside HeroSelect — a
//      Camera, an EventSystem, and a UIDocument GameObject carrying
//      PetSelectScreen.uxml with this controller attached. The builder
//      registers the scene in Build Settings between HeroSelect and Village.
//
//   2. StarterPetId persistence: this controller writes GameState.StarterPetId
//      directly and calls GameStateService.Save() because no ChooseStarterPet
//      mutator exists. That is functionally equivalent to a mutator (the field
//      is plain, nothing subscribes to a pet-roster change during the intro),
//      but if the team later wants the canonical pattern, add a
//      GameStateService.ChooseStarterPet(string) that sets the field, raises
//      PetsChanged and Save()s — then call it here instead.
//
//   3. The chosen pet id ("pet-aether-sprite" / "pet-flame-pup" / "pet-ice-wolf")
//      is the same id space DeNelle.Pets.PetCatalog uses, so the village /
//      PetDeployer resolves the player's starter through its own catalog.
// =============================================================================
