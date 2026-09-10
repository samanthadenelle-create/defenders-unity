using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using Cysharp.Threading.Tasks;

namespace DeNelle.Village
{
    public sealed class BuildCollectionPage
    {
        public CardCollectionDefinition Collection { get; }
        public bool IsRoot => Collection == null;

        private BuildCollectionPage(CardCollectionDefinition collection) => Collection = collection;
        public static BuildCollectionPage Root() => new BuildCollectionPage(null);
        public static BuildCollectionPage For(CardCollectionDefinition collection) =>
            new BuildCollectionPage(collection ?? throw new ArgumentNullException(nameof(collection)));
    }

    /// <summary>Player-facing, data-authored Build collection browser. It owns no placement logic.</summary>
    public sealed class BuildCollectionBrowser : ObsidianNavigationWorkspace<BuildCollectionPage>
    {
        public const int CardsPerPage = CardCollectionPaging.MaxVisibleCards;
        // The catalog/progression row remains live for saves and unlock bookkeeping, but its
        // unfinished card art must not be advertised as a player-ready build choice.
        // WO-2005 widened this to internal so BuildInventoryModel can tell a row that is
        // WAITING ON A FLAG apart from one hidden by a code constant that runs BEFORE any flag
        // is read (:357). The distinction matters: gate_stone's unlock genuinely flips
        // (RewardedProgression.TryUnlockStoneGate, on wall_wood reaching L2) and genuinely does
        // nothing, so it is dead in a way no data edit can revive. Copying the literal into the
        // model would have made two places own one fact.
        internal const string HiddenUntilFinishedArtId = "gate_stone";
        private const string MissingImageCopy = "Image coming soon";

        // =====================================================================
        //  WO-1623 — THE FOOTER LINK'S BAND, IN REFERENCE PIXELS.
        //
        //  ⛔ A FRACTION CANNOT EXPRESS A PIXEL FLOOR. The band used to be authored as
        //  y .05 -> .155 of Zone_Body — .105 of a host whose height CHANGES with the
        //  aspect — and the capture oracle measured it at 60.3 / 53.3 / 52.4 ref px on
        //  1920x1080 / 2340x1080 / 2670x1200 (Builds/wave1-capture, three
        //  SUB-TOUCH-FLOOR BAND lines, every one of the run's UI_GEOMETRY_FAIL x3).
        //  ElarionUiKit.MinTouchPx is 112, so the same authored fraction was ~52-60 px
        //  SHORT on every captured surface, and no fraction fixes that: the shortfall is
        //  a different number of "fraction" on each aspect. These are px, and
        //  MinTouchPx is READ, never retyped — the touch floor has one owner
        //  (ElarionUiKit.cs:347).
        //
        //  ⛔ AND ClampMinTouch IS NOT THE FIX. The oracle's own message says why: it
        //  grows a sub-floor button SYMMETRICALLY about its centre in LateUpdate, so a
        //  60 px band becomes 112 px by pushing ~26 px UP into the category grid and
        //  ~26 px DOWN off the panel. The net is the last resort, not the author.
        //
        //  The grid's bottom edge moves up by exactly this reserve, from the same
        //  constants, so the two rects cannot disagree about where the footer begins.
        // =====================================================================
        /// <summary>The footer link's height. THE kit touch floor, read from its one owner.</summary>
        private const float FooterLinkBandPx = ElarionUiKit.MinTouchPx;
        /// <summary>Gap between the band's bottom edge and the body zone's bottom edge.</summary>
        private const float FooterLinkBottomInsetPx = 12f;
        /// <summary>Gap between the band's TOP edge and the category grid's bottom edge. Must stay
        /// comfortably above <c>LayoutOracle.OverlapPadPx</c> (2) — two tap targets that touch are
        /// the oracle's Assert B, and trading one finding for seven is not a fix.</summary>
        private const float FooterLinkGridGapPx = 16f;
        /// <summary>What the footer costs the category grid, bottom-up, in reference px.</summary>
        private const float FooterLinkReservePx =
            FooterLinkBottomInsetPx + FooterLinkBandPx + FooterLinkGridGapPx;
        private readonly List<GameObject> _pageObjects = new List<GameObject>();
        private RectTransform _panel;
        private CardCollectionDocument _document;
        private CardCollectionDefinition _collection;
        private CardCollectionModel _remoteCollection;
        private CardCollectionCatalog _catalog;
        private CardCollectionRemoteService _remote;
        private int _page;
        private Action<CatalogEntry> _place;

        // WO-2006 / OWNER_RULINGS_LOCKED §25 — the MANAGE PLACED door. Raised when the
        // player taps the "Manage Placed" category card; the palette forwards it to
        // BuildModeController, which puts the session into its existing tap-to-select
        // state. Null when the host never supplied one (older Show overload).
        private Action _managePlaced;

        protected override string WorkspaceName => "Build Collections";

        protected override string TitleFor(BuildCollectionPage page) =>
            page == null || page.IsRoot ? "Build Collections" : page.Collection.Title;

        protected override string SubtitleFor(BuildCollectionPage page) =>
            page == null || page.IsRoot
                ? (BuildFirstUseGuide.Current == BuildFirstUseGuide.Step.Category
                    ? BuildFirstUseGuide.Copy : "Choose what the realm needs next.")
                : string.Empty; // collection guidance owns a dedicated row below its cards

        protected override void RenderPage(BuildCollectionPage page, RectTransform content)
        {
            _panel = content;
            _collection = page != null ? page.Collection : null;
            if (_collection == null) RenderCategories();
            else RenderCollection();
        }

        public void Show(Action<CatalogEntry> place) => Show(place, null);

        /// <summary>
        /// WO-2006 (ruling §25) — open the category root, with the MANAGE PLACED door.
        /// <paramref name="managePlaced"/> is invoked (after <see cref="Close"/>) when the
        /// player taps the "Manage Placed" card. The single-argument overload stays for
        /// callers that predate the door; it simply supplies no callback, in which case the
        /// card is not built at all (a card that does nothing is worse than no card).
        /// </summary>
        public void Show(Action<CatalogEntry> place, Action managePlaced)
        {
            _place = place;
            _managePlaced = managePlaced;
            BuildFirstUseGuide.BeginSession();
            _catalog = CardCollectionCatalog.CreateDefault(Application.persistentDataPath, Application.version);
            _remote = new CardCollectionRemoteService(_catalog,
                System.IO.Path.Combine(Application.persistentDataPath, "card-collections"));
            _document = _catalog.Resolve();
            _collection = null;
            _page = 0;
            Open(BuildCollectionPage.Root());
        }

        private void OnEnable()
        {
            ProgressionUnlocks.Changed += OnProgressionUnlockChanged;
            StructureSingleton.SingletonResolved += OnFiniteCapacityChanged;
            StructureSingleton.SingletonReleased += OnFiniteCapacityChanged;
        }

        private void OnFiniteCapacityChanged(string itemId, GameObject placed) => OnFiniteCapacityChanged(itemId);
        private void OnFiniteCapacityChanged(string itemId)
        {
            if (IsOpen && _collection == null) Refresh();
        }

        private void OnProgressionUnlockChanged(string catalogId)
        {
            if (!IsOpen || _collection == null ||
                string.IsNullOrEmpty(RewardedProgression.LockReasonFor(catalogId))) return;
            Refresh();
        }

        public override void Close()
        {
            base.Close();
            _collection = null;
            _remoteCollection = null;
            _page = 0;
        }

        private void RenderCategories()
        {
            // Reserve the upper body band for the first-use/category guidance. The
            // device capture proved .96 let row one paint directly through that line.
            // ⛔ WO-1623 — THE BOTTOM EDGE IS NO LONGER A FRACTION. It read `.18f`, which
            // reserved 90 px on one captured aspect and 103 px on another for a footer band
            // that needs a FIXED 112 px on all of them (see the FooterLink* constants). The
            // anchor now sits ON the zone's bottom edge and the reserve is applied as a px
            // OFFSET, so the grid clears the footer by the same distance at every aspect and
            // the two rects are derived from one set of numbers.
            var grid = Region("CategoryGrid", new Vector2(.02f, 0f), new Vector2(.98f, .84f));
            grid.offsetMin = new Vector2(0f, FooterLinkReservePx);
            FlowTrace.Step("BuildCollections",
                "WO-1623 category grid bottom = " + FooterLinkReservePx.ToString("0.#") +
                "px above the body zone (footer inset " + FooterLinkBottomInsetPx.ToString("0.#") +
                " + band " + FooterLinkBandPx.ToString("0.#") +
                " + gap " + FooterLinkGridGapPx.ToString("0.#") + "), not the retired .18 fraction.");
            var layout = grid.gameObject.AddComponent<HorizontalLayoutGroup>();
            if (_document?.Collections == null) return;
            var visible = new List<CardCollectionDefinition>();
            foreach (var c in _document.Collections)
            {
                if (c == null || !c.Active || !string.Equals(c.Context, "build", StringComparison.OrdinalIgnoreCase)) continue;
                // Category visibility is based on authoritative item eligibility/unlock, never
                // affordability. An unlocked expensive category remains a useful goal; a category
                // whose definitions are all locked/missing/unfinished is not navigation yet.
                if (!CollectionHasVisibleItems(c)) continue;
                visible.Add(c);
            }
            // Category discovery is one glance, not paged navigation. Seven portrait cards fit
            // comfortably across the supported landscape canvas and read as a collection rather
            // than oversized horizontal buttons.
            layout.spacing = 14f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            // WO-1628 Step 1 (INSTRUMENT ONLY -- CLAUDE.md sec.12: no layout edit until this
            // measurement is read). The card's sub-caption is the label under investigation.
            // The probes are collected here and REPORTED AFTER the grid is complete, because
            // measuring inside this loop measures nothing: every card is a child of the
            // HorizontalLayoutGroup authored just above, so each card added re-sizes its
            // siblings, and no layout pass has run at all while the loop is still filling.
            // The capture harness itself does not force one until after the panel is built
            // (Assets/Editor/UICaptureLaunch.cs:5703-5710 -- Canvas.ForceUpdateCanvases then
            // LayoutRebuilder.ForceRebuildLayoutImmediate on the panel root).
            var subtitleProbes = new List<SubtitleFitProbe>();
            for (int index = 0; index < visible.Count; index++)
            {
                var c = visible[index];
                var captured = c;
                // A collection is browseable content, not an action command.  Building it
                // through ButtonBox made the four choices read as enormous Back/Next buttons
                // (and inherited the button label which then had to be hidden).  Give the
                // collection its own card surface; the Button is only the hit target/state
                // carrier layered over that surface.
                var card = BuildCategoryCard(grid, () =>
                {
                    BuildFirstUseGuide.CategorySelected();
                    OpenCollection(captured);
                });
                var icon = ElarionUiKit.AddImage(card.transform, "CategoryArtwork",
                    new Vector2(.10f, .38f), new Vector2(.90f, .91f), Color.white, false);
                var image = icon.GetComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
                SetArtworkOrFallback(icon.transform, image, Resources.Load<Sprite>(c.IconKey));
                var divider = ElarionUiKit.AddImage(card.transform, "CardDivider",
                    new Vector2(.08f, .35f), new Vector2(.92f, .355f), ElarionUi.Gold, false);
                divider.GetComponent<Image>().raycastTarget = false;
                var explicitTitle = Label(card.transform, c.Title, 30, TextAlignmentOptions.Center,
                    new Vector2(.07f, .22f), new Vector2(.93f, .34f));
                explicitTitle.color = ElarionUi.Gold;
                explicitTitle.fontStyle = FontStyles.Bold;
                explicitTitle.enableWordWrapping = false;
                explicitTitle.enableAutoSizing = true;
                explicitTitle.fontSizeMin = 20f;
                explicitTitle.overflowMode = TextOverflowModes.Overflow;
                explicitTitle.transform.SetAsLastSibling();
                // WO-1411 — THE SUBTITLE IS STATE, NOT A SLOGAN. The authored subtitles
                // ("Build towers and walls.", "Strengthen walls.") restate the title in a
                // verb phrase and leave the player to open every door to find out which one
                // has anything they can afford. The count comes from the VM
                // (StructureCardVM.AffordableCount), which folds the SAME visibility +
                // affordability authorities the item cards behind this door render from, so
                // the promise on the card and the cards inside it cannot disagree.
                int affordable = StructureCardVM.AffordableCount(c);
                var subtitle = Label(card.transform, StructureCardVM.AffordabilityWords(affordable), 21,
                    TextAlignmentOptions.Top, new Vector2(.08f, .05f), new Vector2(.92f, .21f));
                subtitle.color = ElarionUi.Parchment;
                subtitle.raycastTarget = false;
                ElarionUiKit.FitBlock(subtitle, 18f, 21f);
                // WO-1628 Step 1: the line this used to emit HERE is unchanged in wording and
                // still one per card -- it is emitted from ReportSubtitleFit below, once the
                // grid has actually been laid out, with the resolved band and the render facts
                // appended. Emitting it here as well would put a pre-layout twin of every line
                // in the capture log, which is worse than no measurement at all.
                subtitleProbes.Add(new SubtitleFitProbe
                {
                    Prefix = "collection=" + c.CollectionId + " affordable=" + affordable +
                             " subtitle='" + subtitle.text + "'",
                    Label = subtitle,
                    Card = card.GetComponent<RectTransform>()
                });
            }

            BuildManagePlacedCard(grid);
            // WO-1628 Step 1 -- measured AFTER the Manage Placed card, because that card is an
            // additional child of the SAME grid and therefore changes every sibling's width.
            ReportSubtitleFit(grid, subtitleProbes);
            BuildManageDefensesFooterLink();
        }

        // =====================================================================
        //  WO-1411 / REVIEW_MERGED row 10, owner ruling section 2 #13 (written to the
        //  default: rename YES, keep the 8th card NO).
        //
        //  ⛔ THE DOOR SURVIVES; ONLY ITS DRESS CHANGES. Building new defenses and
        //  managing existing ones are still different player intents, and the route is
        //  still the authoritative Manage -> Defense destination — the SAME
        //  PanelRouter.Open(PanelId.Manage, "Defense") call the card carried. What is
        //  retired is the DISGUISE: an eighth CATEGORY CARD, sitting in a grid of
        //  build categories, whose title had to shrink (fontSizeMin 15 against the
        //  categories' 20) to fit two words the other seven do not need. Both reviewers
        //  read it as "a Manage door dressed as a category", and the capture shows the
        //  smaller title. A card in a build grid promises something to build.
        //
        //  A footer TEXT LINK says what it is: a way out of the build catalog to the
        //  screen that manages what is already standing. It sits in the root band below
        //  the card row, so it takes nothing from the seven categories and cannot be
        //  mistaken for one of them.
        //  ⚠ WO-1623 CORRECTED THIS PARAGRAPH: it used to say "(the grid ends at y .18)",
        //  and that parenthesis had become false — the grid's bottom edge is now a PIXEL
        //  reserve (FooterLinkReservePx), because a fraction cannot hold a touch floor.
        //  The band below is authored from the same constants; read them, not a fraction.
        // =====================================================================
        private void BuildManageDefensesFooterLink()
        {
            Guard.Try("BuildCollections", "manage-defenses footer link", () =>
            {
                var link = ButtonBox(_panel, "Already built? Manage defenses >", () =>
                {
                    FlowTrace.Step("Build",
                        "footer link TAPPED -- leaving the build catalog for Manage > Defense " +
                        "(same route the retired Upgrade Defenses card carried).");
                    Close();
                    PanelRouter.Open(PanelId.Manage, "Defense");
                });
                var rt = link.GetComponent<RectTransform>();
                // ⛔ WO-1623 — X IS A FRACTION, Y IS PIXELS, AND THAT ASYMMETRY IS THE FIX.
                // The link's WIDTH was never the defect (the oracle measured 666-746 ref px on
                // every captured aspect), so it stays proportional to the panel. Its HEIGHT is a
                // TOUCH FLOOR, which is a number of pixels on a finger, not a share of a rect —
                // so both y anchors collapse onto the zone's bottom edge and the band's two
                // edges are set as px offsets from it. Pivot is set BEFORE the offsets: moving a
                // pivot afterwards keeps sizeDelta and anchoredPosition, which would slide the
                // rect straight back off the floor. Same shape as UICaptureLaunch.MakePixelBand
                // (:4736) — the codebase's existing pixel-band idiom, mirrored to the bottom edge.
                rt.anchorMin = new Vector2(.28f, 0f);
                rt.anchorMax = new Vector2(.72f, 0f);
                rt.pivot = new Vector2(.5f, 0f);
                rt.offsetMin = new Vector2(0f, FooterLinkBottomInsetPx);
                rt.offsetMax = new Vector2(0f, FooterLinkBottomInsetPx + FooterLinkBandPx);
                link.name = "ManageDefensesFooterLink";
                var label = link.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    // WO-1626: the caption's floor, wrap mode, overflow mode and post-layout fit
                    // guard are ALL the kit's to decide — one owner per concern. The retired
                    // WO-1623 note here guarded a raw sub-floor minimum written straight onto TMP,
                    // below ElarionUiKit.FontHardFloor (20, ElarionUiKitObsidian.cs:3044). Routing
                    // through the factory (ElarionUiKitObsidian.cs:3054-3069) clamps the floor,
                    // sets NoWrap, and arms the fit guard the raw path never armed. 24f preserves
                    // the previous fontSizeMax exactly. Same call shape as :461 on this screen.
                    ElarionUiKit.FitSingleLine(label, 20f, 24f);
                }
                FlowTrace.Step("BuildCollections",
                    "WO-1623 footer band authored in px: inset " +
                    FooterLinkBottomInsetPx.ToString("0.#") + " + height " +
                    FooterLinkBandPx.ToString("0.#") + " (= ElarionUiKit.MinTouchPx) above the " +
                    "body zone's bottom edge, x .28-.72 as before. The retired .05-.155 fraction " +
                    "resolved 52-60 ref px and was the whole of the capture run's geometry red.");
                FlowTrace.Step("Build",
                    "WO-1411: the 8th 'Upgrade Defenses' CARD is retired; the Manage > Defense door " +
                    "is now the footer text link below the category grid (route unchanged).");
            });
        }

        // =====================================================================
        //  WO-2006 / OWNER_RULINGS_LOCKED §25 — THE "MANAGE PLACED" DOOR.
        //
        //  Owner ruling 2026-09-06, from a friend's playtest: "he accidentally put a
        //  palisade down and he didn't mean to and now he has no way to move the
        //  Palisade... we might need to add one more card, which is just move or manage".
        //
        //  ⛔ THIS ADDS NO CAPABILITY AND NO SECOND SELECTION SYSTEM. Move / Upgrade /
        //  Sell already exist, fully wired, on BuildSelectionUI (OnMoveRequested /
        //  OnUpgradeRequested / OnSellRequested -> BuildModeController.BeginMoveSelected /
        //  UpgradeSelected / SellSelected, subscribed in EnsureSelectionUi). The ONLY
        //  missing thing was a DOOR: the sole route in was "enter build mode, then tap the
        //  exact placed piece", which a player who mis-tapped during placement has no
        //  reason to guess. This card closes the browser and hands the session to that
        //  SAME tap-to-select loop (BuildModeController.BeginManagePlaced), announcing the
        //  gesture out loud. One selection owner, one panel, one new signpost.
        //
        //  ⚠ Built ONLY when a callback was supplied. A category card that closes the
        //  browser and then does nothing would be a worse defect than the one it fixes.
        // =====================================================================
        private void BuildManagePlacedCard(RectTransform grid)
        {
            if (_managePlaced == null)
            {
                FlowTrace.Step("BuildCollections",
                    "Manage Placed card SKIPPED — Show() was called without a managePlaced callback " +
                    "(legacy single-arg overload); the card would close onto nothing.");
                return;
            }

            // ⚠ A CARD THAT LEADS TO AN EMPTY MAP IS A DEAD END, AND THE FTUE HITS IT FIRST.
            // Tapping the card CLOSES this browser; with nothing placed the player would land on a
            // bare build camera holding a toast, which is a worse experience than the missing door.
            // This file's own neighbours already state the rule -- BuildPaletteUI.RebuildChips:
            // "an empty section grows no chip (a chip that filters to nothing is a dead end)", and
            // RenderCategories above drops any category with no visible items. Same rule here.
            //
            // ⚠ COUNTS LIVE BODIES, NOT PERSISTED ROWS, AND THAT IS DELIBERATE. During the founding
            // load the BAKED TWIN stands in for a persisted BaseLayout row and carries NO
            // PlacedStructure (BuildModeController.Enter's census says so at length). A baked twin
            // is not tap-selectable either, so a persisted-row count would show the card for pieces
            // the door genuinely cannot reach. Live bodies is the honest number.
            int selectable = FindObjectsByType<PlacedStructure>(FindObjectsSortMode.None).Length;
            if (selectable <= 0)
            {
                FlowTrace.Step("BuildCollections",
                    "Manage Placed card SKIPPED — zero live PlacedStructure bodies, so the card would " +
                    "close the browser onto a map with nothing selectable (the FTUE state). The card " +
                    "returns on the next render once something is built.");
                return;
            }

            var manageCard = BuildCategoryCard(grid, () =>
            {
                FlowTrace.Step("BuildCollections",
                    "Manage Placed card TAPPED — closing the browser and handing the session to the " +
                    "existing tap-to-select loop (ruling §25 door).");
                Close();
                _managePlaced?.Invoke();
            });
            manageCard.name = "ManagePlacedCard";

            var manageArt = ElarionUiKit.AddImage(manageCard.transform, "CategoryArtwork",
                new Vector2(.10f, .38f), new Vector2(.90f, .91f), Color.white, false);
            var manageImage = manageArt.GetComponent<Image>();
            manageImage.preserveAspect = true;
            manageImage.raycastTarget = false;
            // No bespoke art is authored for this card yet; SetArtworkOrFallback owns the
            // art-absent read, exactly as the Upgrade Defenses card does above.
            SetArtworkOrFallback(manageArt.transform, manageImage,
                Resources.Load<Sprite>("UI/ElarionMedieval/cards/buildings"));

            var manageDivider = ElarionUiKit.AddImage(manageCard.transform, "CardDivider",
                new Vector2(.08f, .35f), new Vector2(.92f, .355f), ElarionUi.Gold, false);
            manageDivider.GetComponent<Image>().raycastTarget = false;

            var manageTitle = Label(manageCard.transform, "Manage Placed", 30,
                TextAlignmentOptions.Center, new Vector2(.07f, .22f), new Vector2(.93f, .34f));
            manageTitle.color = ElarionUi.Gold;
            manageTitle.fontStyle = FontStyles.Bold;
            manageTitle.enableWordWrapping = false;
            manageTitle.enableAutoSizing = true;
            manageTitle.fontSizeMin = 15f;
            manageTitle.fontSizeMax = 26f;
            manageTitle.transform.SetAsLastSibling();

            var manageSubtitle = Label(manageCard.transform,
                "Move, upgrade or sell anything already built.", 21, TextAlignmentOptions.Top,
                new Vector2(.08f, .05f), new Vector2(.92f, .21f));
            manageSubtitle.color = ElarionUi.Parchment;
            manageSubtitle.raycastTarget = false;
            ElarionUiKit.FitBlock(manageSubtitle, 18f, 21f);

            FlowTrace.Step("BuildCollections",
                $"Manage Placed card BUILT in the category grid over {selectable} selectable placed " +
                "bodies (ruling §25 door; the move/upgrade/sell controls themselves are " +
                "BuildSelectionUI's and are untouched).");
        }

        private async void OpenCollection(CardCollectionDefinition collection)
        {
            _collection = collection; _remoteCollection = null; _page = 0;
            Push(BuildCollectionPage.For(collection));
            CardCollectionModel loaded = await _remote.ResolveAsync(collection.CollectionId, Application.version);
            if (_collection != collection || !IsOpen) return;
            _remoteCollection = loaded;
            Refresh();
        }

        private void RenderCollection()
        {
            var visibleIds = VisibleItemIds();
            int count = visibleIds.Count;
            int pages = CardCollectionPaging.PageCount(count);
            // Keep the cards in their own upper field. The collection instruction used
            // to occupy the shared shell subtitle at y .78-.84 and painted through the
            // card titles. Seat it below the card row where the screen has deliberate
            // breathing room; paging remains in the footer band beneath it.
            var row = Region("Cards", new Vector2(.02f, .20f), new Vector2(.98f, .96f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 18; layout.padding = new RectOffset(10, 10, 6, 6); layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
            int first = CardCollectionPaging.FirstIndex(_page, count);
            int last = Math.Min(first + CardsPerPage, count);
            for (int i = first; i < last; i++)
            {
                BuildItemCard(row, visibleIds[i]);
            }
            string guidance = BuildFirstUseGuide.Current == BuildFirstUseGuide.Step.Item
                ? BuildFirstUseGuide.Copy : _collection.Subtitle;
            var guidanceLabel = Label(_panel, guidance, 28, TextAlignmentOptions.Center,
                new Vector2(.08f, .11f), new Vector2(.92f, .19f));
            guidanceLabel.color = ElarionUi.Parchment;
            guidanceLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(guidanceLabel, 22f, 28f);
            if (pages > 1)
            {
                var prev = FooterButton("PREVIOUS", new Vector2(.04f, .035f), new Vector2(.22f, .11f), () => { _page = Math.Max(0, _page - 1); Refresh(); });
                prev.interactable = _page > 0;
                Label(_panel, "PAGE " + (_page + 1) + " / " + pages, 26, TextAlignmentOptions.Center, new Vector2(.42f, .035f), new Vector2(.58f, .11f));
                var next = FooterButton("NEXT", new Vector2(.78f, .035f), new Vector2(.96f, .11f), () => { _page = Math.Min(pages - 1, _page + 1); Refresh(); });
                next.interactable = _page + 1 < pages;
            }
        }

        private void BuildItemCard(RectTransform parent, string itemId)
        {
            var entry = CatalogRegistry.Get(itemId);
            string lockReason = RewardedProgression.LockReasonFor(itemId);
            bool progressionLocked = !string.IsNullOrEmpty(lockReason) && !ProgressionUnlocks.IsUnlocked(itemId);
            StructureCardVM vm = entry != null
                ? new StructureCardVM(entry, EconomyService.Instance,
                    BuildModeController.FreeBuildAvailable(entry), progressionLocked, lockReason)
                : null;
            // WO-1572: IsPlayerBuilt, not IsBuilt - same trap as CollectionHasVisibleItems
            // (:613). A card whose baked twin is standing must still read BUILDABLE, exactly
            // as BuildModeController.IsSingletonBuilt (:2334-2346) already judges it; reading
            // IsBuilt here rendered "Built" on a row the arm path was happy to place.
            bool built = entry != null && StructureSingleton.IsSingleton(entry.id) && StructureSingleton.IsPlayerBuilt(entry);
            bool locked = entry == null || (vm != null && vm.Locked);
            bool available = vm != null && vm.Affordable && !locked && !built;
            // Shared collection-card law: the action is a sibling footer BELOW the
            // information card. Keeping it out of the card prevents ornate button art
            // from covering cost/status copy as the layout contracts on mobile.
            var slot = new GameObject("BuildCardSlot_" + itemId, typeof(RectTransform));
            slot.transform.SetParent(parent, false);
            var slotRt = slot.GetComponent<RectTransform>();
            slotRt.anchorMin = Vector2.zero; slotRt.anchorMax = Vector2.one;
            slotRt.offsetMin = slotRt.offsetMax = Vector2.zero;
            // WO-1417: the item card is a KIT SURFACE, not a bespoke navy quad. Same obsidian
            // face (ElarionUiKit.ObsidianFill) + antique-gold perimeter the sibling CATEGORY
            // card one page back already uses, so the row reads as the frame's own material.
            // The old flat #171C29 fill plus an Outline whose colour was the ONLY locked/unlocked
            // cue is gone: hue never carries state here (the owner is red/green colourblind) --
            // the state WORD below is the single carrier.
            var card = Box("BuildCard_" + itemId, slot.transform, ElarionUiKit.ObsidianFill);
            card.anchorMin = new Vector2(0f, .18f);
            card.anchorMax = Vector2.one;
            card.offsetMin = card.offsetMax = Vector2.zero;
            AddGoldPerimeter(card.transform);
            Label(card, vm?.DisplayName ?? Humanize(itemId), 32, TextAlignmentOptions.Center, new Vector2(.05f,.86f), new Vector2(.95f,.98f));
            var artGo = ElarionUiKit.AddImage(card, "Artwork", new Vector2(.12f,.54f), new Vector2(.88f,.84f), Color.white, false);
            var image = artGo.GetComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
            SetArtworkOrFallback(artGo.transform, image, BuildPaletteUI.ResolveEntryArtPublic(entry));
            Label(card, vm?.Description ?? "Catalog definition unavailable.", 25, TextAlignmentOptions.TopLeft, new Vector2(.07f,.30f), new Vector2(.93f,.53f));
            // WO-1417 cost line. `COST: NO COST` was a label contradicting its own key, and the
            // underlying zero was TRUE, not a defect: lumberyard/silo/foundry are NON-TOWER rows,
            // so BuildModeController.FreeBuildAvailable lane 3 makes the FIRST placement of each
            // distinct id free (BuildModeController.cs:3078-3080), while the catalog prices the
            // paid one at wood+iron (structures-catalog lumberyard repo.cost 800 wood / 320 iron).
            // While the freebie is live the price slot shows NOTHING -- owner ruling WO-1010 D20,
            // recorded verbatim at BuildPaletteUI.cs:1418-1424 ("dont show anything on first build
            // just nothing" / "they dont need to know first is free"), which retired the "FREE"
            // label on the carousel card. This surface now obeys the same ruling instead of
            // inventing a second freebie wording. The paid basket is spelled through the ONE
            // shared formatter (CostFormat.Words) every other cost surface uses.
            string cost = vm == null ? "Cost unavailable"
                        : vm.Freebie ? string.Empty
                        : CostFormat.Words(CostParts(vm.EffectiveCost));
            if (!string.IsNullOrEmpty(cost))
                Label(card, "COST: " + cost, 25, TextAlignmentOptions.Center, new Vector2(.06f,.13f), new Vector2(.94f,.23f));
            // ONE state word. The old string was a bracket glyph plus a synonym pair
            // ("[READY] AVAILABLE"); brackets are console furniture, and two words for one state
            // read as two facts. Every value below is a distinct word, so the state survives
            // greyscale with no hue at all.
            string state = locked ? "Locked" : built ? "Built" : !vm.Affordable ? "Unaffordable" : "Ready";
            Label(card, state, 25, TextAlignmentOptions.Center, new Vector2(.06f,.02f), new Vector2(.94f,.12f));
            FlowTrace.Step("Build", "collection-card id=" + itemId + " plate=ElarionUiKit.ObsidianFill+GoldPerimeter"
                + " cost='" + cost + "' state='" + state + "'");
            // The complete state already lives in the wrapping status band above. Button faces
            // carry only a short action/state word so the kit never ellipsizes required copy.
            string buttonFace = available ? "PLACE" : built ? "BUILT" : !locked ? "NEED RESOURCES" : "UNAVAILABLE";
            var place = ButtonBox(slot.transform, buttonFace, () => Place(entry));
            var pr = place.GetComponent<RectTransform>(); pr.anchorMin = new Vector2(.10f,.01f); pr.anchorMax = new Vector2(.90f,.16f); pr.offsetMin = pr.offsetMax = Vector2.zero;
            place.GetComponent<Button>().interactable = available;
            var buttonLabel = place.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonLabel != null)
            {
                buttonLabel.enableWordWrapping = true;
                buttonLabel.enableAutoSizing = true;
                buttonLabel.fontSizeMin = 20f;
                buttonLabel.fontSizeMax = 32f;
                buttonLabel.overflowMode = TextOverflowModes.Overflow;
            }
        }

        private void Place(CatalogEntry entry)
        {
            if (entry == null) return;
            var callback = _place;
            // Done commits the selection, releases the browsing pause, then returns
            // to the existing placement authority. No economy/placement logic moved.
            Done(BuildFirstUseGuide.ItemSelected, () => callback?.Invoke(entry));
        }

        /// <summary>
        /// WO-1571 - THE DIRECT-PLACEMENT DOOR: pick ONE id as if the player had tapped its card,
        /// without ever rendering the category root.
        ///
        /// <para>WHY IT HAS TO EXIST. The root offers COLLECTIONS, and card-collections.json
        /// authors collections for Towers / Walls and Gates / Manage Placed only. A row whose
        /// manageFilters is ECONOMY / CRAFT / STORAGE - <c>arcane-tower</c> ("Cathedral of Magic")
        /// is the captured one - therefore has NO collection to be reached through, so landing its
        /// Manage BUILD button on the root is a dead end by construction, not by a missing tap.
        /// Device build 358872, logcat 2026-09-07 00:58:40.</para>
        ///
        /// <para>⛔ IT REUSES <see cref="Place"/> AND ADDS NOTHING. A bare Close() + arm would skip
        /// what <c>Done</c> does beyond closing - it commits the first-use guide step and releases
        /// the browsing pause (ObsidianNavigationWorkspace.cs:97-107) - and would strand a ghost
        /// behind a held pause: a worse dead end than the one this fixes. Singleton, affordability
        /// and the why-band all stay exactly where they already are, downstream of
        /// BuildModeController.Arm.</para>
        ///
        /// <para>⭐ THE GATE IS <see cref="IsCollectionItemVisible"/>, THE ONE OFFER AUTHORITY, and
        /// it is asked BEFORE anything closes - so a refusal leaves the player on the normal root
        /// with a reason in the trace, never inside an empty session. That predicate is what carries
        /// the WO-1379 first-raid / build-categories lockedIds soft gates; do not add a second
        /// unlock test here.</para>
        /// </summary>
        internal bool PlaceById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                FlowTrace.Warn("BuildCollections", "PlaceById called with an empty id - refused.");
                return false;
            }
            var entry = CatalogRegistry.Get(id);
            if (entry == null)
            {
                FlowTrace.Warn("BuildCollections",
                    "PlaceById '" + id + "': not in CatalogRegistry - the direct door is refused and the " +
                    "category root stands. Nothing is armed.");
                return false;
            }
            if (!IsCollectionItemVisible(id))
            {
                FlowTrace.Warn("BuildCollections",
                    "PlaceById '" + id + "': the ONE offer authority (IsCollectionItemVisible) says this row " +
                    "is not offered right now - locked out by build-categories lockedIds, RewardedProgression " +
                    "or visibleLockedIds. The direct door is refused; the gate is NOT bypassed.");
                return false;
            }
            FlowTrace.Step("BuildCollections",
                "direct-placement door: '" + id + "' picked without rendering the category root " +
                "(WO-1571) - same Place/Done seam a card tap uses.");
            Place(entry);
            return true;
        }

        private List<string> VisibleItemIds()
        {
            var result = new List<string>();
            if (_remoteCollection?.Cards != null)
            {
                foreach (var card in _remoteCollection.Cards)
                    if (card != null && IsCollectionItemVisible(card.StableId))
                        result.Add(card.StableId);
            }
            else if (_collection?.Items != null)
            {
                foreach (var item in _collection.Items)
                    if (item != null && IsCollectionItemVisible(item.ItemId))
                        result.Add(item.ItemId);
            }
            return result;
        }

        /// <summary>
        /// The ONE offer authority: is this catalog id actually shown to the player as a build
        /// choice right now? Widened from private to internal by WO-2005 so
        /// <see cref="BuildInventoryModel"/> can ASK it instead of re-deriving the same four
        /// checks — a second copy of this predicate is how a card ends up listed in Manage and
        /// absent from the browser (or the reverse), with no screen saying which is right.
        /// Still assembly-internal: no View outside DeNelle.Village may call it.
        /// </summary>
        internal static bool IsCollectionItemVisible(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            if (string.Equals(itemId, HiddenUntilFinishedArtId, StringComparison.OrdinalIgnoreCase)) return false;

            // Owner 2026-08-29: hide cards that are not unlocked (Arcane Spire + Catapult were
            // still showing). Mirror BuildPaletteVM WO-964 — lockedIds HIDE until earned; never
            // grey them in the collection browser. Same earn authority: ProgressionUnlocks.
            if (IsBuildCategoryLockedOut(itemId) && !ProgressionUnlocks.IsUnlocked(itemId))
                return false;

            string lockReason = RewardedProgression.LockReasonFor(itemId);
            if (!string.IsNullOrEmpty(lockReason) && !ProgressionUnlocks.IsUnlocked(itemId))
                return false;

            // visibleLockedIds (e.g. Stone Gate) — also hide until unlock; do not tease.
            if (IsBuildCategoryVisibleLocked(itemId) && !ProgressionUnlocks.IsUnlocked(itemId))
                return false;

            return true;
        }

        /// <summary>
        /// Union of every verb's <c>lockedIds</c> from build-categories.json (same rule as
        /// <see cref="BuildPaletteVM.ConfigureGroup"/>). A row gated under ANY verb stays hidden
        /// in collections until <see cref="ProgressionUnlocks"/> flips.
        /// </summary>
        private static bool IsBuildCategoryLockedOut(string itemId)
        {
            foreach (BuildType verb in System.Enum.GetValues(typeof(BuildType)))
            {
                var cat = BuildCategoryRegistry.Get(verb);
                if (cat?.LockedIds != null && cat.LockedIds.Contains(itemId))
                    return true;
            }
            return false;
        }

        private static bool IsBuildCategoryVisibleLocked(string itemId)
        {
            foreach (BuildType verb in System.Enum.GetValues(typeof(BuildType)))
            {
                var cat = BuildCategoryRegistry.Get(verb);
                if (cat?.VisibleLockedReasons != null && cat.VisibleLockedReasons.ContainsKey(itemId))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// WO-1572 - THE CATEGORY-ROOT FILTER, and it asks IsPlayerBuilt, never IsBuilt.
        ///
        /// A singleton row stops being a build choice when THE PLAYER has placed its one
        /// allowed instance - not when the scene bake happens to be showing a twin of it.
        /// This predicate read <c>StructureSingleton.IsBuilt(entry)</c> until 2026-09-07, and
        /// IsBuilt counts an ACTIVE BAKED TWIN (StructureSingleton.cs:120-147, step 2). Every
        /// item in build-realm (barracks / pet-house / arcane-tower) and build-trade (market /
        /// forge / armorer) authors a bakedTwin, so on any save where the bake surfaced them
        /// BOTH WHOLE CATEGORIES DISAPPEARED FROM THE ROOT - the owner's frame
        /// Logs/device/screens/owner-screen-20260907-005742.png shows three cards where
        /// card-collections.json authors seven. A surfaced twin means the item is still
        /// OFFERED: placing it stands the twin down via NotifyPlaced -> Enforce ->
        /// StandDownBakedTwins, and BuildModeController.IsSingletonBuilt (:2334-2346) has
        /// asked IsPlayerBuilt since WO-843 - so the ARM path always agreed the row was
        /// buildable while this filter hid the door to it.
        ///
        /// Walks EVERY item rather than returning on the first hit: the trace below is the
        /// only place that says why a category is missing, and an early return would make the
        /// count a lie. Called once per collection per root render (:139), never per frame.
        /// </summary>
        private static bool CollectionHasVisibleItems(CardCollectionDefinition collection)
        {
            if (collection?.Items == null) return false;
            int total = collection.Items.Count, offered = 0;
            int hiddenByVisibility = 0, missingEntry = 0, playerBuilt = 0;
            foreach (var item in collection.Items)
            {
                if (item == null) { missingEntry++; continue; }
                if (!IsCollectionItemVisible(item.ItemId)) { hiddenByVisibility++; continue; }
                var entry = CatalogRegistry.Get(item.ItemId);
                if (entry == null) { missingEntry++; continue; }
                // Singleton is today's authoritative finite-placement contract. Once the
                // PLAYER's one allowed instance exists it is no longer a build choice;
                // removal raises SingletonReleased and this projection recomputes.
                if (StructureSingleton.IsSingleton(entry.id) && StructureSingleton.IsPlayerBuilt(entry))
                { playerBuilt++; continue; }
                offered++; // repeatable entries remain visible while their definition is eligible
            }
            // WO-1572: the player-built tally counts PLACED instances only - a standing baked
            // twin is not "built" here. Kept as a COMMENT, never appended to the trace string:
            // BuildCollectionPlayerRegression.StringLiterals scans EVERY double-quoted literal
            // in this file for a '[' glyph (WO-1417, palette copy), and it cannot tell a
            // diagnostic string from a string the player reads on a card.
            FlowTrace.Step("BuildCollections",
                "collection=" + collection.CollectionId + " offered=" + offered + "/" + total +
                " (hidden-by-visibility=" + hiddenByVisibility + " no-catalog-entry=" + missingEntry +
                " player-built=" + playerBuilt + ") -> " + (offered > 0 ? "SHOWN" : "DROPPED"));
            return offered > 0;
        }

        /// <summary>Every collection image resolves to art or an intentional neutral placeholder.</summary>
        private static void SetArtworkOrFallback(Transform host, Image image, Sprite sprite)
        {
            if (image == null) return;
            image.sprite = sprite;
            if (sprite != null) { image.color = Color.white; return; }

            image.color = new Color(.10f, .11f, .14f, 1f);
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.48f, .43f, .32f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);
            var fallback = Label(host, MissingImageCopy, 23, TextAlignmentOptions.Center,
                new Vector2(.08f, .12f), new Vector2(.92f, .88f));
            fallback.color = new Color(.82f, .79f, .70f, 1f);
            fallback.raycastTarget = false;
        }

        private Button FooterButton(string text, Vector2 min, Vector2 max, Action action)
        {
            var go = ButtonBox(_panel, text, action);
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=rt.offsetMax=Vector2.zero;
            var button = go.GetComponent<Button>();
            MedievalUiSkin.ApplyButton(button, primary: text == "NEXT");
            return button;
        }
        private RectTransform Region(string name, Vector2 min, Vector2 max)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(_panel,false); var rt=go.GetComponent<RectTransform>();rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=rt.offsetMax=Vector2.zero;return rt; }
        private static GameObject ButtonBox(Transform parent, string text, Action click)
        { return ElarionUiKit.Button(parent,text,ElarionUiKit.ButtonKind.Quiet,Vector2.zero,Vector2.one,click).gameObject; }

        private static GameObject BuildCategoryCard(Transform parent, Action click)
        {
            var go = new GameObject("BuildCollectionCard", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var face = go.GetComponent<Image>();
            // The supplied horizontal content-panel bitmap has a large transparent tail below
            // its visible border. Stretching it into a portrait card leaves the footer copy
            // outside the painted surface. Use native scalable geometry for this vertical
            // component: one continuous obsidian face plus an explicit antique-gold perimeter.
            face.sprite = null;
            face.color = new Color(.025f, .024f, .023f, .985f);
            AddGoldPerimeter(go.transform);
            var button = go.GetComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1f, .92f, .72f, 1f),
                pressedColor = new Color(.82f, .66f, .36f, 1f),
                selectedColor = new Color(1f, .92f, .72f, 1f),
                disabledColor = new Color(.42f, .42f, .42f, .75f),
                colorMultiplier = 1f,
                fadeDuration = .08f
            };
            if (click != null) button.onClick.AddListener(() => click());
            return go;
        }
        /// <summary>The kit BEZEL: an explicit antique-gold perimeter over an obsidian face, in
        /// native scalable geometry (the horizontal frame bitmap does not survive a portrait
        /// stretch -- see BuildCategoryCard's own note). WO-1417 lifted these four edges out of
        /// BuildCategoryCard verbatim so the CATEGORY card and the ITEM card are one material
        /// written once; no new primitive was authored.</summary>
        private static void AddGoldPerimeter(Transform host)
        {
            void Edge(string name, Vector2 min, Vector2 max)
            {
                var edge = ElarionUiKit.AddImage(host, name, min, max,
                    new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, .95f), false);
                edge.GetComponent<Image>().raycastTarget = false;
            }
            Edge("GoldTop",    new Vector2(.018f, .982f), new Vector2(.982f, .992f));
            Edge("GoldBottom", new Vector2(.018f, .008f), new Vector2(.982f, .018f));
            Edge("GoldLeft",   new Vector2(.008f, .018f), new Vector2(.018f, .982f));
            Edge("GoldRight",  new Vector2(.982f, .018f), new Vector2(.992f, .982f));
        }

        /// <summary>WO-1628 Step 1 -- one category card's sub-caption, held until the grid has
        /// been laid out so the band it ends up with can be MEASURED rather than derived. The
        /// prefix is the exact text the pre-WO-1628 trace emitted, so the capture log keeps the
        /// same one-line-per-card shape and the same grep.</summary>
        private sealed class SubtitleFitProbe
        {
            public string Prefix;
            public TextMeshProUGUI Label;
            public RectTransform Card;
        }

        /// <summary>
        /// WO-1628 Step 1 -- INSTRUMENTATION ONLY. Emits, per category card, what the affordability
        /// sub-caption BECAME after layout: the resolved band, the card that holds it, the fitted
        /// font size and its floor/ceiling, the rendered line and character counts against the
        /// source length, whether TMP cut the string, its preferred height, and the two modes the
        /// fit left it in.
        ///
        /// WHY IT IS A SEPARATE PASS AND NOT A LINE INSIDE THE BUILD LOOP: rect sizes inside a
        /// HorizontalLayoutGroup are not resolved until a layout pass runs, and the headless
        /// capture is an EDIT-MODE render (UICaptureLaunch.cs:525-532) with no LateUpdate and no
        /// post-layout fit guard -- ElarionUiKitObsidian.ArmFitGuard returns immediately when
        /// !Application.isPlaying (:3096). So the numbers below are the AUTHORED fit with no
        /// runtime rescue, which is exactly what the three captured PNGs show.
        ///
        /// UNITS: canvas reference px. The card and panel rects are emitted alongside the band on
        /// purpose -- a measurement taken at the wrong moment shows itself as zeros here, so a bad
        /// site can never be mistaken for a real number.
        ///
        /// Per CLAUDE.md sec.12 this stays in the code after the fix.
        /// </summary>
        private static void ReportSubtitleFit(RectTransform grid, List<SubtitleFitProbe> probes)
        {
            if (probes == null || probes.Count == 0) return;
            Guard.Try("Build", "WO-1628 subtitle fit measurement", () =>
            {
                Canvas.ForceUpdateCanvases();
                if (grid != null) LayoutRebuilder.ForceRebuildLayoutImmediate(grid);
                Canvas.ForceUpdateCanvases();
                string gridPx = grid == null ? "none" :
                    grid.rect.width.ToString("0.#") + "x" + grid.rect.height.ToString("0.#");
                foreach (var probe in probes)
                {
                    if (probe == null || probe.Label == null) continue;
                    var t = probe.Label;
                    // ignoreActiveState + forceTextReparsing: in edit mode nothing has ticked TMP,
                    // so without both the textInfo below is the pre-fit mesh, not the shipped one.
                    t.ForceMeshUpdate(true, true);
                    var band = t.rectTransform.rect;
                    string bandPx = band.width.ToString("0.#") + "x" + band.height.ToString("0.#");
                    string cardPx = probe.Card == null ? "none" :
                        probe.Card.rect.width.ToString("0.#") + "x" + probe.Card.rect.height.ToString("0.#");
                    string fontPx = t.fontSize.ToString("0.##") + " floor " + t.fontSizeMin.ToString("0.##") +
                                    " ceiling " + t.fontSizeMax.ToString("0.##");
                    string source = t.text == null ? "null" : t.text.Length.ToString();
                    string rendered = t.textInfo == null ? "none" :
                        t.textInfo.lineCount.ToString() + " lines, " +
                        t.textInfo.characterCount.ToString() + " chars";
                    string modes = t.overflowMode.ToString() + " / " + t.textWrappingMode.ToString();
                    FlowTrace.Step("Build", probe.Prefix +
                        " bandPx=" + bandPx + " cardPx=" + cardPx + " gridPx=" + gridPx +
                        " fontSize=" + fontPx +
                        " rendered=" + rendered + " sourceLen=" + source +
                        " truncated=" + t.isTextTruncated +
                        " preferredHeightPx=" + t.preferredHeight.ToString("0.#") +
                        " modes=" + modes);
                }
            });
        }

        private static RectTransform Box(string name, Transform parent, Color color)
        { return ElarionUiKit.AddImage(parent,name,Vector2.zero,Vector2.one,color,true).GetComponent<RectTransform>(); }
        private static TextMeshProUGUI Label(Transform parent,string text,float size,TextAlignmentOptions align,Vector2 min,Vector2 max)
        {
            var t=ElarionUiKit.Label(parent,text,min.y,max.y,Color.white,Mathf.RoundToInt(size),align,min.x,max.x);
            t.enableWordWrapping=true;
            t.enableAutoSizing=true;
            t.fontSizeMin=Mathf.Min(20f,size);
            t.fontSizeMax=size;
            t.overflowMode=TextOverflowModes.Overflow;
            return t;
        }
        private static string Humanize(string id) => string.IsNullOrEmpty(id) ? "Unknown" : id.Replace('_',' ').Replace('-',' ');
        /// <summary>WO-1417: the collection card's basket now runs through the ONE shared cost
        /// formatter (CostFormat.Parts/Words) instead of a private second formatter that owned its
        /// own separator and its own "NO COST" wording. Same concept ids + words BuildPaletteUI
        /// authors, so the two build surfaces can never disagree about a price.</summary>
        private static IReadOnlyList<CostPart> CostParts(DeNelle.Core.Catalog.ResourceCost c)
        {
            return CostFormat.Parts(new[]
            {
                ("wood", "Wood", c.wood), ("stone", "Stone", c.food),
                ("iron", "Iron", c.iron), ("crystal", "Crystals", c.crystals)
            });
        }
        protected override void OnDisable()
        {
            ProgressionUnlocks.Changed -= OnProgressionUnlockChanged;
            StructureSingleton.SingletonResolved -= OnFiniteCapacityChanged;
            StructureSingleton.SingletonReleased -= OnFiniteCapacityChanged;
            base.OnDisable();
        }
        protected override void OnDestroy()
        {
            ProgressionUnlocks.Changed -= OnProgressionUnlockChanged;
            StructureSingleton.SingletonResolved -= OnFiniteCapacityChanged;
            StructureSingleton.SingletonReleased -= OnFiniteCapacityChanged;
            base.OnDestroy();
        }
    }
}
