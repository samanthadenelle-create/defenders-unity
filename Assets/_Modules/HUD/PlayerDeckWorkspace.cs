using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.HudModel;   // WO-1357: PostureSignals.RaidCapable / RaidLock - the ONE raid predicate
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    public enum PlayerDeckKind { Realm, Hero, Journey }

    public sealed class PlayerDeckPage
    {
        public PlayerDeckKind Kind { get; }
        public PlayerDeckPage(PlayerDeckKind kind) => Kind = kind;
    }

    /// <summary>One shared card workspace for the three recognition-heavy player domains.</summary>
    public sealed class PlayerDeckWorkspace : ObsidianNavigationWorkspace<PlayerDeckPage>
    {
        private sealed class Card
        {
            public string Title;
            public string Purpose;
            public string Concept;
            public string ArtKey;
            /// <summary>
            /// WO-1357 follow-up (owner art delivery 2026-09-03) - the card face to mount while
            /// <see cref="Available"/> is false. Null means "there is no dedicated locked face",
            /// and the card keeps <see cref="ArtKey"/> in both states, which is what every other
            /// card in the deck does. A card that DOES supply one is declaring that the locked
            /// state is AUTHORED art (padlock + darkened scene), so the runtime gray tint is
            /// dropped for it - see BuildCard. The plate in that art must stay text-free: title
            /// and reason are live TMP, exactly as WO-1341 settled.
            /// </summary>
            public string LockedArtKey;
            public Func<bool> Available;
            /// <summary>
            /// WO-1357 — the SPECIFIC reason this card is locked, evaluated at render. Null
            /// (or a null/empty return) falls back to the generic "Complete its requirement
            /// first" line below. A locked card that only says LOCKED is a dead end; one that
            /// names its remedy teaches the next goal, which is why this exists.
            /// </summary>
            public Func<string> LockReason;
            public Action Open;
        }

        private static PlayerDeckWorkspace _instance;
        protected override string WorkspaceName => "Player Deck";

        /// <summary>WO-1397: ref px authored ABOVE ElarionUiKit.MinTouchPx for a deck card's
        /// height, so layout rounding can never settle the rect a hair under the floor the
        /// capture touch oracle reads. Not a second floor - a margin on the one floor.</summary>
        private const float TouchFloorMarginPx = 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("Player Deck Workspace");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PlayerDeckWorkspace>();
        }

        protected override void Awake()
        {
            base.Awake();
            _instance = this;
            PanelRouter.Register(PanelId.RealmDeck, OpenRealm);
            PanelRouter.Register(PanelId.HeroDeck, OpenHero);
            PanelRouter.Register(PanelId.JourneyDeck, OpenJourney);
        }

        private void OpenRealm() => Open(new PlayerDeckPage(PlayerDeckKind.Realm));
        private void OpenHero() => Open(new PlayerDeckPage(PlayerDeckKind.Hero));
        private void OpenJourney() => Open(new PlayerDeckPage(PlayerDeckKind.Journey));

        protected override string TitleFor(PlayerDeckPage page) => page.Kind.ToString();

        protected override string SubtitleFor(PlayerDeckPage page)
        {
            switch (page.Kind)
            {
                case PlayerDeckKind.Realm: return "Realm services, records, and guidance.";
                // WO-1523: the line names what the deck actually carries. While no cosmetic is
                // unlocked the Wardrobe card is not built, and a purpose line that still promised a
                // wardrobe would send the player hunting for a section that is not on the screen -
                // the same "name N cards, show N cards" rule WO-1421 settled for Journey below.
                case PlayerDeckKind.Hero:
                    return HeroDeckWardrobeVM.FromCurrentState().WardrobeHasUnlocked
                        ? "Your equipment, inventory, skills, loadout, and wardrobe."
                        : "Your equipment, inventory, skills, and loadout.";
                // WO-1421 (owner 2026-09-06): the deck is two cards, so the line names two.
                // Labelled explicitly for symmetry with the two arms above; `default:` is stacked
                // on the same return so the enum stays exhaustively covered.
                case PlayerDeckKind.Journey:
                default: return "Your quests, and the camps your army can raid.";
            }
        }

        protected override void RenderPage(PlayerDeckPage page, RectTransform content)
        {
            var cards = CardsFor(page.Kind);
            var gridGo = new GameObject(page.Kind + "CardGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            var grid = (RectTransform)gridGo.transform;
            grid.SetParent(content, false);
            grid.anchorMin = new Vector2(0.02f, 0.03f);
            // Reserve the upper body band for the workspace purpose line. The first
            // measured capture proved a .97 top edge let row one cover that line.
            grid.anchorMax = new Vector2(0.98f, 0.82f);
            grid.offsetMin = grid.offsetMax = Vector2.zero;
            var layout = gridGo.GetComponent<GridLayoutGroup>();
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 2;
            layout.spacing = new Vector2(24f, 20f);
            layout.padding = new RectOffset(14, 14, 14, 14);
            Canvas.ForceUpdateCanvases();
            // WO-1397: the grid is two columns wide and as many rows tall as the deck needs, never
            // fewer than two. A deck of up to four cards gets EXACTLY the cell it always had
            // (rows=2 reduces to the old h*0.5f, spacing subtracted once); the Hero deck's fifth
            // card (Wardrobe) opens a third row instead of overflowing the 0.82 top band into the
            // purpose line. Rows, not a third column: every card keeps its measured width, so the
            // WO-1341/HudLabelFitRegression label fits stay exactly what they were.
            int rows = Mathf.Max(2, Mathf.CeilToInt(cards.Count / (float)layout.constraintCount));
            float w = Mathf.Max(1f, grid.rect.width - layout.padding.horizontal - layout.spacing.x);
            float h = Mathf.Max(1f, grid.rect.height - layout.padding.vertical - layout.spacing.y * (rows - 1));
            // WO-1397 gate finding (UI_GEOMETRY_FAIL x10, HeroWorkspace 2670x1200): three rows in
            // the 0.03..0.82 band resolved 108.7 ref px tall - 3.3 under ElarionUiKit.MinTouchPx -
            // and ClampMinTouch would have grown every card into its neighbours at runtime. A cell
            // is therefore never AUTHORED under the floor (plus a 2 px margin, because the touch
            // oracle reads the settled rect and a cell authored exactly AT the floor can round a
            // hair under it). When the floor needs more height than the band has, the grid is
            // EXTENDED DOWNWARD toward the Close band - the top edge is fixed by the purpose line
            // (the WO-1341 note above reserved the TOP, not the bottom) - and never below the
            // body's own bottom edge (anchor 0): a card's text must stay on the body plate
            // (capture RULE 1 [text-off-plate]), and the factory already ends the body above the
            // Close. For every 2-row deck h / rows is far above the floor and the extension never
            // fires, so their geometry is byte-identical to before this note.
            float cellY = Mathf.Max(ElarionUiKit.MinTouchPx + TouchFloorMarginPx, h / rows);
            float needed = cellY * rows + layout.padding.vertical + layout.spacing.y * (rows - 1);
            float bodyH = Mathf.Max(1f, content.rect.height);
            bool extended = false;
            if (needed > grid.rect.height + 0.01f)
            {
                float deficitFrac = (needed - grid.rect.height) / bodyH;
                float newMinY = Mathf.Max(0f, grid.anchorMin.y - deficitFrac);
                extended = newMinY < grid.anchorMin.y;
                grid.anchorMin = new Vector2(grid.anchorMin.x, newMinY);
                Canvas.ForceUpdateCanvases();
                float hAfter = Mathf.Max(1f, grid.rect.height - layout.padding.vertical - layout.spacing.y * (rows - 1));
                if (hAfter / rows < ElarionUiKit.MinTouchPx)
                    FlowTrace.Warn("Navigation", "deck '" + page.Kind + "' grid: " + rows + " rows need " +
                        Mathf.RoundToInt(needed) + " px but the body band (" + Mathf.RoundToInt(bodyH) +
                        " px) cannot give it even at anchor 0 - cell " + (hAfter / rows).ToString("F1") +
                        " is under MinTouchPx " + ElarionUiKit.MinTouchPx + "; ClampMinTouch will grow it into its neighbours");
                cellY = Mathf.Max(ElarionUiKit.MinTouchPx + TouchFloorMarginPx, hAfter / rows);
            }
            layout.cellSize = new Vector2(w * 0.5f, cellY);
            FlowTrace.Step("Navigation", "deck '" + page.Kind + "' grid " + layout.constraintCount + "x" + rows +
                " for " + cards.Count + " card(s), cell " + Mathf.RoundToInt(layout.cellSize.x) + "x" +
                Mathf.RoundToInt(layout.cellSize.y) + " (band " + grid.anchorMin.y.ToString("F3") + ".." +
                grid.anchorMax.y.ToString("F3") + " of body " + Mathf.RoundToInt(bodyH) + " px" +
                (extended ? ", extended toward Close for the touch floor)" : ")"));

            for (int i = 0; i < cards.Count; i++) BuildCard(grid, cards[i]);
        }

        private void BuildCard(RectTransform grid, Card spec)
        {
            bool available = spec.Available == null || spec.Available();
            // Uppercased AT CONSTRUCTION, exactly as Manage does it (ManageScreenPanel.cs:587),
            // so the card face has ONE writer. Never re-assign face.text further down - that is
            // how a screen ends up with two producers for one string (WO-1341).
            var button = ElarionUiKit.BuildObsidianButton(grid, spec.Title.ToUpperInvariant(),
                ElarionUiKit.ObsidianButtonStyle.Style1,
                available ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                Vector2.zero, Vector2.one, () => OpenCard(spec));
            if (button == null) return;
            button.gameObject.name = "DeckCard_" + spec.Title;
            button.interactable = available;
            MedievalUiSkin.ApplyButton(button, primary: available);
            var cardImage = button.GetComponent<Image>();
            // WO-1357 follow-up: a card may ship a SECOND, authored face for its locked state
            // (cards/raids-locked.png - war camp gone dark behind a stone padlock). It is
            // selected here and ONLY here, so the unlocked path below is byte-for-byte the path
            // that already works; the locked branch is the only thing that changes.
            bool authoredLockFace = !available && !string.IsNullOrEmpty(spec.LockedArtKey);
            string artKey = authoredLockFace ? spec.LockedArtKey : spec.ArtKey;
            var illustratedCard = string.IsNullOrEmpty(artKey) ? null :
                Resources.Load<Sprite>("UI/ElarionMedieval/cards/" + artKey);
            if (authoredLockFace && illustratedCard == null)
            {
                // Never silently fall through to the unlocked face - that would show an inviting
                // camp behind a [ LOCKED ] badge. Say so, and let the ArtKey fallback happen with
                // the failure on the record.
                FlowTrace.Warn("HUD", "deck card '" + spec.Title + "' locked face '" +
                    spec.LockedArtKey + "' did not load - falling back to '" + spec.ArtKey + "'");
                authoredLockFace = false;
                artKey = spec.ArtKey;
                illustratedCard = string.IsNullOrEmpty(artKey) ? null :
                    Resources.Load<Sprite>("UI/ElarionMedieval/cards/" + artKey);
            }
            var cardFrame = illustratedCard != null ? illustratedCard :
                Resources.Load<Sprite>("UI/ElarionMedieval/frames/card-frame-empty");
            if (cardImage != null && cardFrame != null)
            {
                if (illustratedCard != null)
                {
                    // Some delivered wide-card PNGs carry an editor checkerboard in their outer
                    // packaging margin and some are already trimmed tight. WO-1311 (owner ruling
                    // 2026-09-02, "fix it that way"): the correction is DERIVED PER SPRITE from
                    // that sprite's own opaque bounds - never from a shared constant. A tight
                    // sprite gets NO correction and renders 1:1; a margined one gets exactly its
                    // own margin removed, per edge, seated inside a native rectangular mask so
                    // the packaging pixels are never displayed or mutated.
                    var fit = ResolveArtFit(artKey, illustratedCard);
                    cardImage.sprite = null;
                    cardImage.color = Color.clear;
                    if (fit.Corrected && button.GetComponent<RectMask2D>() == null)
                        button.gameObject.AddComponent<RectMask2D>();
                    // The gray wash is the stand-in for "this card has no locked art". When the
                    // card HAS an authored locked face it is already the darkened, padlocked
                    // scene, and washing it again only costs the live text its contrast against a
                    // near-black plate. The owner is red/green colourblind, so locked-ness never
                    // rested on the tint anyway: it reads from the padlock, the "[ LOCKED ]" word
                    // badge and the remedy line - shape and words, not hue.
                    Color lockedTint = new Color(.48f, .48f, .50f, .82f);
                    var artSurface = ElarionUiKit.AddImage(button.transform, "IllustratedCardSurface",
                        fit.AnchorMin, fit.AnchorMax,
                        (available || authoredLockFace) ? Color.white : lockedTint, false);
                    artSurface.transform.SetAsFirstSibling();
                    var artImage = artSurface.GetComponent<Image>();
                    artImage.sprite = illustratedCard;
                    artImage.type = Image.Type.Simple;
                    artImage.preserveAspect = false;
                    artImage.raycastTarget = false;
                    button.targetGraphic = artImage;
                    // Illustrated destination cards are complete surfaces. Never SpriteSwap
                    // them to a generic/blank button face on hover or controller selection.
                    button.transition = Selectable.Transition.ColorTint;
                    var colors = button.colors;
                    colors.normalColor = Color.white;
                    colors.highlightedColor = new Color(1.08f, 1.04f, .90f, 1f);
                    colors.selectedColor = colors.highlightedColor;
                    colors.pressedColor = new Color(.82f, .76f, .64f, 1f);
                    // button.interactable is false on a locked card, so the Selectable multiplies
                    // targetGraphic (the art) by disabledColor. That is the SECOND wash, and it
                    // would undo the one dropped above - an authored locked face renders at full
                    // value in both places or in neither.
                    colors.disabledColor = authoredLockFace
                        ? Color.white : new Color(.46f, .46f, .48f, .82f);
                    colors.colorMultiplier = 1f;
                    colors.fadeDuration = .08f;
                    button.colors = colors;
                }
                else
                {
                    cardImage.sprite = cardFrame;
                    cardImage.type = Image.Type.Simple;
                    cardImage.color = available ? Color.white : new Color(.48f, .48f, .50f, .82f);
                }
            }

            // WO-1341, owner ruling: "should match font and format of Manage screen". The
            // reference implementation is ManageScreenPanel.BuildLauncherCards
            // (ManageScreenPanel.cs:620-635) and every number below is copied from it rather
            // than invented here, so the two card surfaces cannot drift again:
            //   title  - anchors x TextPlateX0..0.96, y 0.55..0.90, 36px, CENTRED, Gold
            //            (ParchmentDim when locked), FitSingleLine(30, 40)
            //   purpose- y 0.26..0.52, FontMicro, CENTRED, FitSingleLine(24, 30)
            // The old deck format was 34px BOLD LEFT with a 22px floor and a hard
            // TextOverflowModes.Ellipsis - which is what printed the truncated
            // "Choose the abilities equipped for bat..." in the device capture. Manage neither
            // disables wrapping nor sets an overflow mode, so neither do we.
            var face = button.GetComponentInChildren<TMP_Text>();
            if (face != null)
            {
                var rt = face.rectTransform;
                rt.anchorMin = new Vector2(TextPlateX0(illustratedCard != null), 0.55f);
                rt.anchorMax = new Vector2(0.96f, 0.90f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                face.fontSize = 36f;
                face.alignment = TextAlignmentOptions.Center;
                face.color = available ? ElarionUi.Gold : ElarionUi.ParchmentDim;
                ElarionUiKit.FitSingleLine(face, 30f, 40f);
            }

            if (illustratedCard == null)
            {
                Sprite sprite = ConceptIconResolver.Resolve(spec.Concept);
                var iconFrame = ElarionUiKit.AddImage(button.transform, "IdentityMedallion",
                    new Vector2(0.055f, 0.22f), new Vector2(0.245f, 0.84f), Color.white, false);
                var bezel = iconFrame.GetComponent<Image>();
                bezel.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/circular-bezel-four-point");
                bezel.preserveAspect = true;
                bezel.raycastTarget = false;
                var iconGo = ElarionUiKit.AddImage(iconFrame.transform, "IdentityIcon",
                    new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.78f), Color.white, false);
                var icon = iconGo.GetComponent<Image>();
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                if (sprite == null)
                {
                    icon.color = new Color(0f, 0f, 0f, 0f);
                    var monogram = ElarionUiKit.Label(iconFrame.transform,
                        string.IsNullOrEmpty(spec.Title) ? "?" : spec.Title.Substring(0, 1).ToUpperInvariant(),
                        0.20f, 0.80f, ElarionUi.Gold, 42, TextAlignmentOptions.Center, 0.20f, 0.80f,
                        bold: true);
                    monogram.raycastTarget = false;
                    monogram.gameObject.name = "IdentityMonogram";
                }
            }

            if (!available)
            {
                // WO-1311 acceptance 3. The gray tint is a COLOUR-ONLY signal and the owner is
                // red/green colourblind, so unavailability also carries a NON-COLOUR partner: a
                // literal word badge on a dark plate. Text reads identically under any hue loss.
                float badgeX0 = TextPlateX0(illustratedCard != null);
                var badgePlate = ElarionUiKit.AddImage(button.transform, "LockedBadgePlate",
                    new Vector2(badgeX0, 0.87f), new Vector2(0.93f, 0.99f),
                    new Color(0f, 0f, 0f, .62f), false);
                var plateImage = badgePlate.GetComponent<Image>();
                if (plateImage != null) plateImage.raycastTarget = false;
                var badge = ElarionUiKit.Label(badgePlate.transform, "[ LOCKED ]", 0.02f, 0.98f,
                    ElarionUi.Parchment, 24, TextAlignmentOptions.Center, 0.02f, 0.98f, 4f, true);
                badge.gameObject.name = "LockedBadge";
                badge.enableWordWrapping = false;
                badge.overflowMode = TextOverflowModes.Ellipsis;
                ElarionUiKit.FitSingleLine(badge, 14f, 24f);
            }

            // WO-1357: when the card is locked, the purpose line becomes the REMEDY. The
            // "[ LOCKED ]" badge above says THAT it is shut; this line says WHY and what to do,
            // in words - the owner is red/green colourblind, so neither the gray face nor the
            // badge alone may be the only carrier of meaning.
            string lockLine = null;
            if (!available && spec.LockReason != null)
                lockLine = Guard.Try("HUD", "resolve deck card lock reason '" + spec.Title + "'",
                                     spec.LockReason, null);
            if (!available)
                FlowTrace.Step("Navigation", "deck card '" + spec.Title + "' LOCKED - " +
                    (string.IsNullOrEmpty(lockLine) ? "generic requirement line (no LockReason supplied)" : lockLine));

            var purpose = ElarionUiKit.Label(button.transform,
                available ? spec.Purpose
                          : (string.IsNullOrEmpty(lockLine) ? "Complete its requirement first" : lockLine),
                PurposeTopFrac, PurposeTopFrac,
                available ? ElarionUi.Parchment : ElarionUi.ParchmentDim,
                (int)ElarionUi.FontMicro, TextAlignmentOptions.Center,
                TextPlateX0(illustratedCard != null), 0.96f);
            purpose.gameObject.name = "DeckCardPurpose_" + spec.Title;
            // WO-1636 - Y IS PIXELS, X STAYS A FRACTION, and that asymmetry is the fix. The
            // caption's WIDTH was never implicated - the aspect that kept the MOST glyphs also
            // measured the WIDEST band (370.4 ref px at 2670x1200 vs 329.6 at 1920x1080), so x
            // stays proportional to the card. Its HEIGHT is a number of lines of readable copy,
            // which is px. Both y anchors are collapsed onto the band's existing TOP edge by the
            // Label call above and the height hangs below it. Pivot is set BEFORE the offsets:
            // moving a pivot afterwards keeps sizeDelta and anchoredPosition and would slide the
            // rect straight back off the number. The WO-1628 shape, verbatim.
            var purposeRect = purpose.rectTransform;
            purposeRect.pivot = new Vector2(.5f, 1f);
            purposeRect.offsetMax = Vector2.zero;
            purposeRect.offsetMin = new Vector2(0f, -PurposeBandPx);
            FlowTrace.Step("Navigation", "deck card '" + spec.Title + "' purpose band " +
                PurposeBandPx.ToString("F0") + " ref px below top frac " +
                PurposeTopFrac.ToString("F2") + " (two " + ElarionUi.FontFloorMobile.ToString("F0") +
                "px lines measure " + PurposeTwoLineReqPx.ToString("F1") + " px)");
            // THE DECK FOLLOWS MANAGE (owner ruling: Manage is the card standard;
            // HudLabelFitRegression case 6c reads the floors OUT of ManageScreenPanel and requires
            // this call to equal them). Manage's hub card description moved from a single-line fit
            // to FitBlock on 2026-09-07 because the owner's device showed all three descriptions
            // cut mid-word (owner-screen-20260907-004724.png), and this label carries the
            // SAME defect class - case 6d already records "Choose the abilities equipped for
            // bat..." on this exact line.
            // NOTE - THE FLOOR WENT UP, NOT DOWN: 24f -> ElarionUi.FontFloorMobile (30f). FitBlock
            // wraps into the band rather than shrinking toward a floor and then cutting, and the
            // band is now sized in px for the two lines the wrapped copy actually needs
            // (PurposeBandPx).
            // Truncation, if it ever happens, is VISIBLE at the end of a line instead of three
            // dots that look deliberate.
            ElarionUiKit.FitBlock(purpose, ElarionUi.FontFloorMobile, 34f);
        }

        // =====================================================================
        //  WO-1636 - THE PURPOSE LINE'S HEIGHT IS PIXELS, NOT A SHARE OF THE CARD.
        //
        //  The glyph oracle's first run (Builds/wave3-capture2.glyph-findings.txt:72-81 and
        //  Builds/wave3-navcapture.glyph-findings.txt:1-6) recorded SIXTEEN truncations on this
        //  one label - twelve on RealmWorkspace, four on JourneyWorkspace - each one a whole
        //  second LINE dropped, never a clipped word:
        //
        //    1920x1080  card 202.7 px  band 52.7 px  "Review non-expiring monthly progress" 20/33
        //    2340x1080  card 176.5 px  band 45.9 px  same copy                              23/33
        //    2670x1200  card 173.1 px  band 45.0 px  same copy                              23/33
        //
        //  (card height = the measured band / the 0.26 share it was authored as). The band was
        //  0.26-0.52 of a card whose REFERENCE HEIGHT changes with the aspect, so the same
        //  authoring resolves to 45-53 px - and TWO lines at the fitter's own floor need 68.0.
        //
        //  ⛔ THE DEFECT IS THE UNIT, NOT THE COPY AND NOT THE FONT. This is the identical
        //  failure WO-1628 retired for the build-collection caption, and the cure is the same
        //  one: author the HEIGHT in px, collapse both y anchors onto the edge the band hangs
        //  from, set the pivot BEFORE the offsets. The in-code note that used to sit on the
        //  FitBlock call - "this label's band is 0.26-0.52 of the plate, a genuinely multi-line
        //  seat" - was WRONG, and the oracle is what disproved it: 0.26 of this card has never
        //  been a two-line seat on any captured aspect.
        // =====================================================================
        /// <summary>What TWO lines of purpose copy actually measure, in reference px, at the
        /// fitter's floor.
        /// <para>MEASURED AT THE FONT ASSET, not estimated: Assets/Resources/Localization/Fonts/
        /// ElarionLocaleFallback.asset declares m_PointSize 64, m_LineHeight 73.59375,
        /// m_AscentLine 57.9375, m_DescentLine -13.5625. TMP stacks N lines as
        /// (N-1) x lineHeight + (ascent - descent), so per point that is
        /// (73.59375 + 71.5) / 64 = 2.26709, i.e. 68.01 px at 30 and 77.08 px at 34.
        /// CORROBORATED INDEPENDENTLY: WO-1628's step-1 probe MEASURED preferredHeight 47.6 px
        /// for two lines at fontSize 21 on all twenty-one of its lines, and this model returns
        /// 47.61 for the same input. Two derivations, one number.</para></summary>
        private const float PurposeTwoLineReqPx = 68.01f;
        /// <summary>The purpose band's HEIGHT in reference px. <see cref="PurposeTwoLineReqPx"/>
        /// plus ~2 px of headroom, the same margin WO-1628 left (50 authored over 47.6 measured),
        /// so the fitter has somewhere to land instead of sitting on its floor. Never a fraction
        /// of the card.</summary>
        private const float PurposeBandPx = 70f;
        /// <summary>
        /// Where the purpose band hangs from: the TOP edge it already had. Keeping the top edge
        /// and growing DOWNWARD is what leaves the title, the medallion and the artwork exactly
        /// where they are - the band grows into the card's empty bottom margin, which nothing
        /// else is authored into. The next rect up is the title at .55; the concept medallion
        /// (x .055-.245) never reaches this label's plate, which starts at
        /// <see cref="TextPlateX0"/> .27 at the narrowest.
        /// <para>⭐ THE ROOM BELOW IT WAS MEASURED, NOT ASSUMED - and the frame that measurement
        /// lives in is the trap. An illustrated card's dark text plate is painted into the
        /// delivered PNG, but the art is NOT drawn 1:1: MeasureArtFit seats the sprite's OPAQUE
        /// region onto the button, so a PNG y-fraction p renders at button y (p - fy0)/(fy1 - fy0).
        /// Measured in BUTTON space over the plate's x .49-.96, as a WCAG ratio for ParchmentDim
        /// copy (the arithmetic HudLabelFitRegression.MeasurePlate/Contrast use, replicated
        /// 2026-09-10 across all fifteen faces in Assets/Resources/UI/ElarionMedieval/cards/):
        /// every face holds 10.1:1 or better from .10 to .52 and 9.0:1 or better down to .058.
        /// Only the last .058 of the card - its frame edge - drops (troops-locked 3.8, defense-
        /// report 2.3), and this band never reaches it: <see cref="PurposeBandPx"/> below .52
        /// resolves to a bottom of .175 / .123 / .116 at the three captured aspects.
        /// ⚠ Sampling the same bands in RAW PNG fractions instead says the plate falls off a
        /// cliff below .14 - that reading is an artefact of the packaging margin the fit crops
        /// away, and acting on it would have moved this band for no reason.</para>
        /// </summary>
        private const float PurposeTopFrac = 0.52f;

        /// <summary>
        /// Left edge of a card's text plate. An illustrated card's art fills its left half, so the
        /// plate starts at Manage's authored 0.49 (ManageScreenPanel.cs:624). A text-free card
        /// carries only the concept medallion (x 0.055..0.245), so its plate starts just clear of
        /// that instead of leaving a dead band. Both are CENTRED inside their own plate, which is
        /// the format half of the owner's "match Manage" ruling.
        /// </summary>
        private static float TextPlateX0(bool illustrated) => illustrated ? 0.49f : 0.27f;

        /// <summary>
        /// The anchor rectangle an illustrated card's art surface must occupy INSIDE its button so
        /// that the sprite's own opaque region - and nothing else - fills the card face.
        /// <see cref="Corrected"/> is false for a sprite that is already trimmed tight, in which
        /// case the surface is exactly 0..1 and the art renders 1:1 with no mask.
        /// </summary>
        private struct CardArtFit
        {
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public bool Corrected;
        }

        // One measurement per ART KEY for the life of the process. BuildCard runs only when a deck
        // page is opened, and the fit is read from this dictionary - there is no texture read and
        // no allocation per card draw, and none at all per frame.
        private static readonly Dictionary<string, CardArtFit> _artFitCache =
            new Dictionary<string, CardArtFit>();

        private static readonly CardArtFit IdentityFit =
            new CardArtFit { AnchorMin = Vector2.zero, AnchorMax = Vector2.one, Corrected = false };

        private static CardArtFit ResolveArtFit(string artKey, Sprite sprite)
        {
            string key = string.IsNullOrEmpty(artKey) ? (sprite != null ? sprite.name : "?") : artKey;
            CardArtFit cached;
            if (_artFitCache.TryGetValue(key, out cached)) return cached;
            CardArtFit measured = MeasureArtFit(key, sprite);
            _artFitCache[key] = measured;
            return measured;
        }

        /// <summary>
        /// One delivered card PNG whose packaging margin is OPAQUE, so the alpha route in
        /// <see cref="MeasureArtFit"/> cannot see it. Margins are in pixels off each edge of the
        /// authored image, measured from that PNG's own pixels.
        /// </summary>
        private struct OpaqueMargin
        {
            public string Key;
            public int Width, Height, Left, Top, Right, Bottom;
        }

        // ── OPAQUE PACKAGING MARGIN (owner F8 2026-09-03, "the journey raids button is wrong") ──
        // WO-1311 derives a card's packaging margin from the sprite's alpha-built tight mesh. That
        // works only while the margin is TRANSPARENT. Two delivered cards were flattened onto the
        // authoring tool's CHECKERBOARD instead of being exported with alpha, so every pixel of
        // their border is opaque: the tight mesh honestly reports "no margin", the card renders 1:1
        // and the pale checkerboard draws as a near-white slab around the ornate frame. That is
        // exactly the Journey RAIDS card in the owner's photo, and the same defect sits unreported
        // on the Realm deck's GAME GUIDE card.
        //
        // Nothing at RUNTIME can tell that apart from real art - these importers ship
        // isReadable:0, so Texture2D.GetPixels is not available to fall back on, and a wrong crop
        // is worse than an uncropped margin. So the margin is AUTHORED here, measured once from
        // each delivered PNG, and it is GUARDED twice so it can never outlive the bad export:
        //
        //   1. It is consulted ONLY when the alpha route found no transparent margin AT ALL. A
        //      re-exported, properly-alpha'd PNG takes the measured route and never reaches this
        //      table (that is why quests.png - the same art family, exported correctly, margins
        //      L47 T62 R47 B74 - is absent from it and renders right today).
        //   2. Each row is keyed to that PNG's exact pixel dimensions. Re-author the card at any
        //      other size and it falls through to 1:1 rather than being cropped by a stale number.
        //
        // ⚠ DO NOT add a row here for a card that merely "looks a bit off". A row is a claim that
        // the PNG's border pixels are packaging, and the only proof of that is opening the file.
        // Re-export raids.png / game-guide.png with a transparent margin and the matching row
        // becomes dead - delete it THEN, not before.
        //
        // ⭐ WO-1642 (2026-09-10) - THE "raids" ROW IS GONE, AND THAT IS THE INTENDED ENDING.
        // The rectangular crop above only ever removed a rectangular band; the frame painted into
        // raids.png is ROUNDED, so ~33% of each 60x60 corner block inside the authored bbox stayed
        // opaque checkerboard and drew as four white triangles on the card (owner: "it feels
        // incomplete and not polished"). cards/raids.png now carries a real alpha channel -
        // measured after the fix: alpha margin L49 T62 R48 B76, all four corners (0,0,0,0) - so
        // the WO-1311 tight-mesh route sees the margin, this table is never consulted for it, and
        // HudLabelFitRegression case 7 turns RED if the row is ever put back. game-guide.png is
        // still flattened and keeps its row.
        private static readonly OpaqueMargin[] OpaqueMargins =
        {
            // cards/game-guide.png 1821x864 - checkerboard border, art bbox (53,65)-(1769,776)
            new OpaqueMargin { Key = "game-guide", Width = 1821, Height = 864,
                               Left = 53, Top = 65, Right = 52, Bottom = 88 }
        };

        /// <summary>
        /// Fills the rect-local opaque fractions from <see cref="OpaqueMargins"/> when this art key
        /// is a known opaque-margin delivery AND the sprite still has the exact dimensions that row
        /// was measured against. Returns false (leaving the fractions untouched) otherwise.
        /// </summary>
        private static bool TryOpaqueMargin(string key, Rect rect,
                                            ref float fx0, ref float fx1, ref float fy0, ref float fy1)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < OpaqueMargins.Length; i++)
            {
                var m = OpaqueMargins[i];
                if (!string.Equals(key, m.Key, System.StringComparison.OrdinalIgnoreCase)) continue;
                // quests.png ships at the SAME 1774x887 as raids.png, so the key match above is
                // what keeps a correctly-exported sibling out of this table, not the size check.
                if (Mathf.RoundToInt(rect.width) != m.Width || Mathf.RoundToInt(rect.height) != m.Height)
                {
                    FlowTrace.Warn("HUD", "card art fit: '" + key + "' has an authored opaque margin " +
                        "for " + m.Width + "x" + m.Height + " but the sprite is " +
                        Mathf.RoundToInt(rect.width) + "x" + Mathf.RoundToInt(rect.height) +
                        " - re-authored art, rendering 1:1");
                    return false;
                }
                // Sprite space is bottom-left origin; the measurements above are image space
                // (top-left origin), so Top and Bottom swap on the way in.
                fx0 = Mathf.Clamp01(m.Left / rect.width);
                fx1 = Mathf.Clamp01((rect.width - m.Right) / rect.width);
                fy0 = Mathf.Clamp01(m.Bottom / rect.height);
                fy1 = Mathf.Clamp01((rect.height - m.Top) / rect.height);
                FlowTrace.Step("HUD", "card art fit: '" + key + "' opaque packaging margin (authored) L" +
                    m.Left + " T" + m.Top + " R" + m.Right + " B" + m.Bottom);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Derives the packaging margin from the sprite's OWN geometry.
        /// <para>ROUTE CHOSEN (WO-1311): the sprite's TIGHT MESH, not a pixel read. These card
        /// importers ship <c>isReadable: 0</c>, so <c>Texture2D.GetPixels</c> would throw; flipping
        /// isReadable would keep a second uncompressed copy of ten ~1800x880 textures in memory on
        /// a phone. The importers already set <c>spriteMeshType: 1</c> (Tight) with
        /// <c>alphaIsTransparency: 1</c>, so Unity generated an alpha-derived mesh AT IMPORT TIME
        /// and <c>Sprite.vertices</c> hands us those opaque bounds at runtime for free.</para>
        /// <para>If the bounds cannot be trusted the answer is NO correction and a
        /// <see cref="FlowTrace.Warn"/> naming the card: a wrong crop is worse than an uncropped
        /// margin.</para>
        /// </summary>
        private static CardArtFit MeasureArtFit(string key, Sprite sprite)
        {
            if (sprite == null)
            {
                FlowTrace.Warn("HUD", "card art fit: no sprite for '" + key + "' - rendering 1:1");
                return IdentityFit;
            }

            Rect rect = sprite.rect;
            if (rect.width < 8f || rect.height < 8f)
            {
                FlowTrace.Warn("HUD", "card art fit: '" + key + "' rect too small - rendering 1:1");
                return IdentityFit;
            }

            Vector2[] verts = null;
            Guard.Try("HUD", "read tight mesh bounds for card art '" + key + "'",
                () => { verts = sprite.vertices; });
            if (verts == null || verts.Length < 3)
            {
                FlowTrace.Warn("HUD", "card art fit: '" + key + "' has no tight mesh - rendering 1:1");
                return IdentityFit;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector2 v = verts[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }

            // Mesh vertices are in local units measured from the sprite pivot; the pivot is in
            // pixels from the sprite rect's bottom-left. Convert back to rect-local pixels.
            float ppu = sprite.pixelsPerUnit;
            if (ppu <= 0.0001f) ppu = 100f;
            Vector2 pivot = sprite.pivot;
            float x0 = pivot.x + minX * ppu;
            float x1 = pivot.x + maxX * ppu;
            float y0 = pivot.y + minY * ppu;
            float y1 = pivot.y + maxY * ppu;

            float fx0 = Mathf.Clamp01(x0 / rect.width);
            float fx1 = Mathf.Clamp01(x1 / rect.width);
            float fy0 = Mathf.Clamp01(y0 / rect.height);
            float fy1 = Mathf.Clamp01(y1 / rect.height);

            // The alpha route above can only see a margin that is TRANSPARENT. If it found none,
            // the card may still be carrying an OPAQUE packaging margin - see the OpaqueMargins table.
            bool alphaSawNoMargin = x0 <= 1f && y0 <= 1f &&
                                    (rect.width - x1) <= 1f && (rect.height - y1) <= 1f;
            bool opaqueMargin = false;
            if (alphaSawNoMargin && TryOpaqueMargin(key, rect, ref fx0, ref fx1, ref fy0, ref fy1))
                opaqueMargin = true;

            float spanX = fx1 - fx0;
            float spanY = fy1 - fy0;

            // A believable packaging margin trims a modest border. Anything that claims to eat
            // half the card is a mesh we do not understand - refuse it rather than crop wrongly.
            if (spanX < 0.5f || spanY < 0.5f)
            {
                FlowTrace.Warn("HUD", "card art fit: '" + key + "' opaque span implausible (" +
                    spanX.ToString("F3") + "x" + spanY.ToString("F3") + ") - rendering 1:1");
                return IdentityFit;
            }

            // Sub-pixel slack on every edge means the art is already tight. Render it 1:1 and add
            // no mask - this is the case the retired fixed offset was cropping for no reason.
            if (alphaSawNoMargin && !opaqueMargin)
            {
                FlowTrace.Step("HUD", "card art fit: '" + key + "' is tight - 1:1, no correction");
                return IdentityFit;
            }

            float scaleX = 1f / spanX;
            float scaleY = 1f / spanY;
            var fit = new CardArtFit
            {
                AnchorMin = new Vector2(-fx0 * scaleX, -fy0 * scaleY),
                AnchorMax = new Vector2(-fx0 * scaleX + scaleX, -fy0 * scaleY + scaleY),
                Corrected = true
            };
            // Report the margin from the FRACTIONS, not from the mesh bounds: on the opaque
            // route the mesh legitimately spans the whole rect, and logging x0/x1 here would
            // print "margin L0 T0 R0 B0" beside a corrected anchor set - a trace that lies.
            FlowTrace.Step("HUD", "card art fit: '" + key + "' margin L" +
                Mathf.RoundToInt(fx0 * rect.width) + " T" + Mathf.RoundToInt((1f - fy1) * rect.height) +
                " R" + Mathf.RoundToInt((1f - fx1) * rect.width) + " B" + Mathf.RoundToInt(fy0 * rect.height) +
                " -> anchors " + fit.AnchorMin.ToString("F3") + ".." + fit.AnchorMax.ToString("F3"));
            return fit;
        }

        private void OpenCard(Card spec)
        {
            if (spec == null || spec.Open == null || (spec.Available != null && !spec.Available())) return;
            string title = spec.Title;
            // WO-1400: remember the way back BEFORE closing. The arbiter is exclusive, so this deck
            // cannot stay open beneath the card; the door is arbiter state (PanelManager) and fires
            // only on a close-to-nothing, so Equipment -> Skills -> close still lands back here.
            // The deck is the ONE appearance owner: the reopen is the same Open(page) the HUD face
            // uses, never a second spawner.
            if (Navigation.Count > 0 && Navigation.Current != null)
            {
                PlayerDeckKind kind = Navigation.Current.Kind;
                PanelManager.SetReturnDoor(kind + " deck", () =>
                {
                    FlowTrace.Step("Navigation", "deck return -> " + kind);
                    Open(new PlayerDeckPage(kind));
                });
            }
            else
            {
                FlowTrace.Warn("Navigation", "deck card '" + title + "' tapped with no page on the stack - no return door set");
            }
            Close();
            Guard.Try("Navigation", "open deck card '" + title + "'", spec.Open);
            FlowTrace.Step("Navigation", "deck card -> " + title);
        }

        private static Card Route(string title, string purpose, string concept, PanelId target,
                                  string artKey = null, string openContext = null) => new Card
        {
            Title = title,
            Purpose = purpose,
            Concept = concept,
            ArtKey = artKey,
            Available = () => PanelRouter.IsRegistered(target),
            // WO-1388: a route that names its door passes it as the PanelRouter context (the store
            // funnel's store_opened {door}); every other route keeps the plain open, byte for byte.
            Open = () => { if (openContext == null) PanelRouter.Open(target); else PanelRouter.Open(target, openContext); }
        };

        private static List<Card> CardsFor(PlayerDeckKind kind)
        {
            switch (kind)
            {
                case PlayerDeckKind.Hero:
                    // WO-1341. THE FOUR CARDS BELOW (Bag, Equipment, Skills, Loadout) DELIBERATELY
                    // CARRY NO ArtKey, and that is the whole fix - do not "restore the missing art"
                    // without reading this. (Wardrobe, the fifth, is WO-1397 - see its own note.)
                    //
                    // Every label on the Hero deck rendered TWICE on device (build
                    // 2026.09.03.353742): once as the live TMP text built below, and once as
                    // words BAKED INTO THE PNG. cards/bag.png, cards/equipment.png,
                    // cards/skills.png and cards/loadout.png each have a title and a tagline
                    // painted into the very text-safe plate BuildCard draws into, so the two
                    // copies overlapped in two fonts - and they did not even agree on the words
                    // ("Manage your items" vs "Browse every carried item by category";
                    // "LOAD OUT" vs "Loadout"). One string, two producers.
                    //
                    // The kit standard is EXPLICIT and every other card already follows it -
                    // ManageScreenPanel.cs:606 "the approved kit cards are text-safe layered
                    // faces: illustration and border are art, while title, purpose, count and
                    // interaction remain live". cards/buildings.png (Manage), cards/quests.png
                    // (Journey) and cards/realm-store.png (Realm) are all illustration-left with
                    // an EMPTY plate right. The four Hero PNGs are the only ones in the kit that
                    // break it, so the ART is the duplicate producer and the live text survives.
                    //
                    // Re-authoring those four PNGs text-free is the OWNER'S call, not this
                    // ticket's. When they are re-delivered to the buildings.png standard, add the
                    // art key back as the 5th argument here (one word per line) and nothing else
                    // needs to change. Until then these render through the text-free branch
                    // (card-frame-empty + the concept medallion), which is the same treatment
                    // every other non-illustrated card in the game gets.
                    //
                    // WO-1397: the FIFTH card, Wardrobe, is the Cosmetic Shop's only player door.
                    // PanelId.CosmeticShop was registered by CosmeticShopPanel every session and
                    // opened by nobody - its one caller was a dialogue verb (OpenCosmetics) that no
                    // dialogue uses (docs/qa/UI_SCREEN_GRAPH_2026-09-04.md dead end 4). No PNG was
                    // ever authored for it, so it takes the same text-free face as its four
                    // siblings by construction, not by stripping. The concept id is unmapped in
                    // concept-icons.json ON PURPOSE (owner tags the art; the CLI never picks), so
                    // the medallion renders the "W" monogram exactly as Equipment/Skills do today.
                    // Owner may re-rule the door into the Night Market (WO-1164): move THIS line,
                    // the panel stays. CosmeticShopReachabilityRegression pins the route.
                    //
                    // WO-1523 (owner 2026-09-06: "everything in wardobe is locked so dont show the
                    // section in hero"). The Wardrobe card is now CONDITIONAL: it is added only when
                    // HeroDeckWardrobeVM.WardrobeHasUnlocked is true, and it is ABSENT from the list
                    // rather than locked or zero-height - a collapsed card still shows up in a
                    // measured layout case, which the WO forbids. The View decides nothing: the VM
                    // reads CosmeticSignals.OwnedCount, which DeNelle.Cosmetics publishes (HUD may
                    // not reference that assembly). On its first appearance the card carries the
                    // VM's NEW word on its purpose line until the player opens it once.
                    // This is the owner's explicit exception to the WO-1008 "a hidden door reads as
                    // broken" precedent that keeps the Journey raid card visible-and-locked.
                    var heroCards = new List<Card>
                    {
                        Route(HudStrings.HeroFaceLabel(HudStrings.KeyHeroBag, "deck"),
                            "Every item you carry", "inventory", PanelId.Inventory),
                        Route("Equipment", "Gear worn by your hero", "armor", PanelId.EquipmentPanel),
                        Route(HudStrings.HeroFaceLabel(HudStrings.KeyHeroSkills, "deck"),
                            "Learn and improve skills", "skill", PanelId.HeroSkillTree),
                        Route(HudStrings.HeroFaceLabel(HudStrings.KeyHeroLoadout, "deck"),
                            "Abilities equipped for battle", "magic", PanelId.HeroLoadout)
                    };
                    var wardrobeVm = HeroDeckWardrobeVM.FromCurrentState();
                    if (wardrobeVm.WardrobeHasUnlocked)
                    {
                        var wardrobe =
                            Route("Wardrobe", "Looks for your hero, Echo, and town", "wardrobe", PanelId.CosmeticShop);
                        wardrobe.Purpose = wardrobeVm.PurposeWithBadge(wardrobe.Purpose);
                        var openWardrobe = wardrobe.Open;
                        wardrobe.Open = () => { HeroDeckWardrobeVM.MarkSeen(); openWardrobe(); };
                        heroCards.Add(wardrobe);
                    }
                    return heroCards;
                case PlayerDeckKind.Journey:
                {
                    var journey = JourneyDeckSubtitleVM.FromCurrentState();
                    return new List<Card>
                    {
                        new Card { Title = "Quests", Purpose = TraceJourneySubtitle("Quests", journey.QuestsSubtitle),
                            Concept = "quest", ArtKey = "quests",
                            Available = () => PanelRouter.IsRegistered(PanelId.RumorBoard),
                            Open = () => PanelRouter.Open(PanelId.RumorBoard) },
                        // WO-1357 (owner 2026-09-03: "Raid button under journey should fail
                        // gracefully, it works great if there is a barracks but should show
                        // locked if doesnt have one yet or its destroyed").
                        //
                        // This card used to carry `Available = () => true`, so it offered the raid
                        // door unconditionally and dead-ended with no barracks - while the action
                        // bar's Raids face had honoured PostureSignals.RaidCapable since WO-835.
                        // ONE rule, TWO surfaces, one of them ignoring it: the duplicated-state
                        // class this repo keeps getting burned by. The fix is to read the EXISTING
                        // predicate, never to write a second barracks check here - a second check
                        // would drift from the first, and the drift is the actual defect.
                        //
                        // The card stays VISIBLE and locked rather than hidden: WO-1008 already
                        // settled that a raid door which hides itself reads as broken ("I do not
                        // see a way to start a raid"). Locked-with-a-reason teaches the next goal.
                        // WO-1389 pressure point 6: until the army is full the subtitle is the
                        // fill count ("Army 3 / 10 - train to fill your ranks"), read off the
                        // Village-published posture rail (PostureSignals.ArmyFill*, the same
                        // ArmyReadiness snapshot the raid gate judges). Unpublished (0 cap) or
                        // full = the ordinary purpose line. Cards are rebuilt per page render, so
                        // the count refreshes every time the deck opens.
                        new Card { Title = "Raids", Purpose = TraceJourneySubtitle("Raids", journey.RaidsSubtitle), Concept = "raid",
                            ArtKey = "raids",
                            // Owner art delivery 2026-09-03. cards/raids-locked.png is the war
                            // camp gone dark behind a stone-and-steel padlock, with the right
                            // ~45% left as an EMPTY plate ON PURPOSE: the reason this card is
                            // shut is DYNAMIC (never had a Barracks / lost one / flag off), so
                            // it has to be live text. The owner was shown a version with the
                            // line baked in and chose the wordless re-generate so the live copy
                            // wins. Never re-deliver this face with words on it - that is the
                            // WO-1341 double-label defect, and HudLabelFitRegression pins it.
                            LockedArtKey = "raids-locked",
                            Available = () => PostureSignals.RaidCapable,
                            LockReason = () => PostureSignals.RaidLockCopy(PostureSignals.RaidLock),
                            Open = RaidEntryGate.RequestOpen }
                        // WO-1421 (owner ruling 2026-09-06, verbatim: "under journey, please remove
                        // dungeons season in realm map as they should not be displayed there right
                        // now"). The deck is TWO cards again. The three doors that used to follow
                        // this line are DELETED, not flagged: the flag of that shape was retired on
                        // 2026-09-05 and its absence is pinned, so a flag would fail the gate. The
                        // destination panels stay compiled and registered - a re-add is one line
                        // when there is content behind them. Their canon-strings rows and the three
                        // HudStrings keys stay DORMANT on purpose; deleting a key breaks
                        // HudLabelFitRegression Case 1 (canon parity across both copies).
                        // Pinned by JourneyDeckTwoCardRegression + PublicNavigationRetirementRegression.
                    };
                }
                default:
                    return new List<Card>
                    {
                        Route(HudStrings.StoreFaceLabel("realm-deck"), "Browse clearly priced realm offers", "store", PanelId.RealmStore, "realm-store", openContext: "settings"),
                        Route("Defense Report", "Review attacks against your town", "defense", PanelId.DefenseReport, "defense-report"),
                        Route("Monthly Ledger", "Review non-expiring monthly progress", "ledger", PanelId.MonthlyLedger, "monthly-ledger"),
                        Route("Game Guide", "Read controls, systems, and help", "settings", PanelId.GameGuide, "game-guide")
                    };
            }
        }

        // WO-1421 (2026-09-06): the two dungeon-door helpers that used to live here existed ONLY
        // to decide whether the removed card was available and to narrate its Open lambda. With
        // the card gone they were unreachable, so they are deleted rather than left as dead
        // private state (CLAUDE.md section 5's duplicated-state drift). The status rail itself is
        // untouched and still read by the portal and by its own suites. The traces that went with
        // them are named in the WO-1421 hand-back; none was reachable from a surviving path, so
        // section 12's never-strip rule is not engaged.

        /// <summary>WO-1404: one trace per built state-bearing Journey card.</summary>
        private static string TraceJourneySubtitle(string card, string subtitle)
        {
            subtitle = subtitle ?? "";
            FlowTrace.Step("Journey", "deck card=" + card + " subtitle='" + subtitle + "'");
            return subtitle;
        }

        protected override void OnDestroy()
        {
            PanelRouter.Unregister(PanelId.RealmDeck, OpenRealm);
            PanelRouter.Unregister(PanelId.HeroDeck, OpenHero);
            PanelRouter.Unregister(PanelId.JourneyDeck, OpenJourney);
            if (_instance == this) _instance = null;
            base.OnDestroy();
        }
    }
}
