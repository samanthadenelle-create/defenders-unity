// =============================================================================
// BuildPaletteUI — the code-built Build Mode palette (WO-108 P1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// A horizontal strip of buildable cards at the bottom of the screen, populated
// straight from CatalogRegistry (the SAME buckets StructureFactory builds from —
// no parallel BuildableItem type, per the WO build-ready update). Each card shows
// the entry's display name + cost; unaffordable cards grey out. Tapping a card
// arms it for placement via the OnEntrySelected callback.
//
// WO-D conversion (2026-07-03, coverage matrix row #36): UIDocument/UITK strip ->
// code-built uGUI on the Obsidian kit language. This is an IN-WORLD-ADJACENT
// strip, NOT a full modal: it keeps its bottom-of-screen position + behaviour,
// restyled with kit buttons (BuildObsidianButton) and slot plates (RpgUiCatalog
// RoleSlot "slot_action") on its own overlay canvas — no PanelSettings adoption
// needed any more (that was a UIDocument requirement). "Done" exits Build Mode
// and IS this strip's close affordance, so its GameObject is named "CloseButton"
// per the close convention (label stays "Done").
// =============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DeNelle.Core.Catalog;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// The Build Mode structure palette. Lists CatalogRegistry entries as tappable
    /// slot-plate cards and raises <see cref="OnEntrySelected"/> when one is armed.
    /// Built in code (uGUI + Obsidian kit) so it renders in player builds.
    /// </summary>
    public sealed class BuildPaletteUI : MonoBehaviour
    {
        /// <summary>Raised when a palette card is tapped — arg is the armed entry.</summary>
        public event Action<CatalogEntry> OnEntrySelected;

        /// <summary>
        /// WO-352 — raised when a palette card is tapped, BEFORE arming, so the controller
        /// can show the Structure Info Preview panel and defer arming until the player taps
        /// "Place". When a subscriber is attached this REPLACES the immediate-arm behaviour
        /// (the card no longer raises <see cref="OnEntrySelected"/> on tap); with no
        /// subscriber the legacy immediate-arm path is unchanged. Arg = the tapped entry.
        /// </summary>
        public event Action<CatalogEntry> OnCardTapped;

        /// <summary>Raised when the palette's Done/exit button is tapped.</summary>
        public event Action OnExitRequested;

        /// <summary>
        /// WO-2006 / OWNER_RULINGS_LOCKED §25 — raised when the player taps the "Manage
        /// Placed" card in the category browser. The controller answers by putting the live
        /// build session into its EXISTING tap-to-select state (BeginManagePlaced); this
        /// event carries no selection state of its own and never opens a second panel.
        /// </summary>
        public event Action OnManagePlacedRequested;

        /// <summary>
        /// WO-1010 P2: the player tapped the minimized "^ Buildings (n)" tab and wants the
        /// carousel back. The BRAIN decides what that means — it routes to the SAME no-charge
        /// cancel every other return-to-carousel uses, so an un-placed ghost is dropped
        /// without a refund path of its own.
        /// </summary>
        public event Action OnRestoreRequested;

        /// <summary>
        /// Raised to open the 3-axis orient editor ON THE ARMED ENTRY (no typing an id).
        /// Arg is the armed entry id.
        ///
        /// WO-1010 D1 (owner screenshot review 2026-08-08): the palette's on-screen "Orient"
        /// WORD-BUTTON is GONE. It was never in this WO's spec (§2 retires rotation word-buttons
        /// — the ghost/rail rotate control is the ONE rotate affordance), and pinned top-right of
        /// the dock it half-covered the Echoes chip so the HUD read "hoes 1/6". The EVENT stays
        /// because BuildModeController.cs:3747 still subscribes it (OpenOrientEditorForArmed);
        /// removing the event would be a silent behaviour deletion in a file this agent does not
        /// own. No palette chrome raises it today — a dev seat drives the orient editor through
        /// the controller. Nothing player-facing regressed: the button was dev-gated anyway
        /// (DevHotkeys || isDebugBuild), which is exactly why it only ever appeared in the
        /// owner's felt-test builds and never in ship.
        /// </summary>
        public event Action<string> OnOrientRequested;

        // MVVM Silo C: the catalog types + unlock-gated ids + the CatalogRegistry query +
        // the affordability/freebie projection now live in BuildPaletteVM (the sanctioned
        // resolution site). This View keeps only _activeType (for the tab underline); the
        // card data comes from _vm.Cards and the balance from _vm.Crystals. The palette
        // still serves every build verb (Town / Defenses / Walls) via Configure.

        /// <summary>
        /// Point the palette at a build verb (owner 2026-07-10). Delegates to the VM, which
        /// sources the catalog types + unlock-gated ids from BuildCategoryRegistry and rebuilds
        /// the cards. Called by BuildModeController before Show; re-renders live if open.
        /// </summary>
        public void Configure(BuildType type)
        {
            _activeType = type;
            // WO-1172 Option B: a verb change resets the group filter to All — the default
            // must always be the state that hides nothing (and a Town filter label means
            // nothing to another verb's sections anyway).
            _activeFilterLabel = null;
            EnsureVm();
            _vm.Configure(type);
            UpdateTabHighlight();   // WO-673 — move the gold underline to the active category tab
            if (_canvas != null && _canvas.activeSelf) Render();
        }

        // Strips sit ABOVE the HUD but BELOW kit modals (BuildObsidianModal defaults 31000).
        private const int SortingOrder = 900;

        // MVVM Silo C — the paired VM (catalog query + wallet + card projections). Created
        // lazily; the View binds its Changed and renders _vm.Cards / _vm.Crystals only.
        private BuildPaletteVM _vm;

        private GameObject _canvas;           // own overlay canvas (kit BuildModalCanvas)
        private Transform _stripContent;      // horizontal-layout card host inside the scroll
        private TextMeshProUGUI _balanceLabel;
        private BuildCollectionBrowser _collectionBrowser;
        private string _armedId;

        // Collapse-on-place (owner "minimize on select" + 2026-07-16 redesign): while an
        // entry is armed the shop FULLY minimizes — EVERY dock background is hidden so no
        // black wall covers the map. These refs let Collapse()/Expand() toggle the header
        // band, the crystals line, and the card tray. The "Placing: <name>" label is folded
        // into the HUD intent bar (BuildHudController.SetPlacingLabel) — no summary panel here.
        private GameObject _topBarGo;         // the dock header band (hidden while collapsed)
        private GameObject _trayGo;           // the scroll well (hidden while collapsed)
        private GameObject _crystalsRowGo;    // WO-1172 Option B: now the CHIP BAND (group filter
                                              // chips + the crystals read-out) above the tray
                                              // (hidden while collapsed; name kept for the
                                              // Collapse()/Expand() seam)
        // WO-1172 Option B (owner pick 2026-08-24): the grouped palette filters by SEGMENTED
        // CHIPS instead of inline dividers. _chipHost is the chip row's layout parent (rebuilt
        // every Render — sections are data and can change); _activeFilterLabel is the selected
        // section's label, null = "All" (the DEFAULT, always — constraint: nothing hides behind
        // a tap by default; Configure resets it).
        private Transform _chipHost;
        private string _activeFilterLabel;
        // WO-1010 D21 (owner ruling late 2026-08-09, WO §7): the category tabs LEFT the bottom
        // panel — the right-edge quick-tab stack is now the PERMANENT category selector, visible
        // in BOTH the PICK phase and the collapsed/placing state. In PICK a tap re-points the
        // card row (Configure); while collapsed a tap reopens the carousel PRE-FILTERED (the
        // standard no-charge cancel). Three entries — Town / Defense / Castle Structures — where
        // Castle Structures is the RENAMED Walls display category (keys stay stable).
        private struct QuickTab
        {
            public GameObject Go;
            public TextMeshProUGUI Label;
            public BuildType Type;
            public string Caption;
            public GameObject Underline;   // gilt active tell — position/shape, never colour alone
        }
        private readonly List<QuickTab> _quickTabs = new List<QuickTab>(3);
        private const float RestoreTabW = 260f;

        // ── The carousel dock's own fixed-pixel height (WO-1010 D21) ───────────
        // PROMOTED from locals inside EnsureBuilt (COLUMN-FIT 2026-08-16): the quick-tab band math has to
        // seat clear of the dock, and it must read the dock's REAL height rather than a
        // number re-typed in a comment. Tray 259 + crystals line 44 = 303.
        // WO-1172 Option B (owner pick 2026-08-24): the 44px crystals line grew into a 112px
        // CHIP BAND (group filter chips ARE controls, so they take the MinTouchPx floor —
        // unlike Option A's header plates, which were reads). The growth comes OUT of the tray
        // (259 -> 191) so DockHeightPx stays 303: DockTopPx feeds the whole right-edge column
        // fit (quick-tab stack 410..778, Done 787..899, 923 required vs 965.4 available on the
        // Seeker — only 42.4px spare), so raising the dock would overflow Done off the canvas.
        // Cards at the 191px tray stand ~143px tall — above the touch floor, no card redesign.
        // The crystals read-out folds into the chip band's right end (text, not a control).
        private const float TrayHeightPx   = 191f;
        private const float ChipBandPx     = ElarionUiKit.MinTouchPx;         // 112
        private const float DockHeightPx   = TrayHeightPx + ChipBandPx;       // 303 — unchanged

        // ── WO-1010 D21 / COLUMN-FIT 2026-08-16: the quick-tab stack's FIXED-PIXEL band math ─────
        // ⚠ THE OLD NOTE HERE REASONED AGAINST AN ASSUMED 1080-TALL CANVAS. IT IS NOT.
        // Everything below is in 1920x1080 REFERENCE px, but the canvas is only 1080 REFERENCE
        // px tall on a 16:9 surface. The owner's Seeker is 2670x1200 (20:9) and, with
        // MatchWidthOrHeight=0.5, resolves to a canvas 2148 x 965.4 REFERENCE px. The old math
        // spent the vertical budget as though 1080 were available and the column summed to
        // exactly 1080 — so on the real device Done overlapped the top tab. Never seat off
        // "1080"; seat off the sum below and check it against PostScaleCanvasHeight.
        //
        // The right edge is split by OWNERSHIP (the D7/D8-class lesson — two surfaces must
        // never draw in one band), measured from the canvas BOTTOM:
        //   strip      18..98   — BuildHudController.ResourceStripReservedPx
        //   dock       98..401  — this palette's own carousel (PICK phase) = DockTopPx
        //   verb row  114..246  — the D14 row (PLACING phase). HORIZONTAL since COLUMN-FIT 2026-08-16
        //                         (384 wide x 132 tall, bottom-right); it used to be a 384px
        //                         TALL column here and that 384px was the whole deficit.
        //   THIS STACK 410..778 — 9px clear of the dock (the taller of the two tenants below
        //                         it), three QuickTabHeightPx(112) boxes + two 16px gutters
        //                         = 368px. Tab centres bottom-up: 466 / 594 / 722.
        //   Done      787..899  — BuildHudController seats it off QuickTabStackTopPx.
        //   + 24px top inset    -> 923 REQUIRED, vs 965.4 available at 2670x1200 (42.4 spare)
        //                          and 1080 at 16:9 (157 spare).
        // Tab height is MinTouchPx(112), NOT CanonCtaHeight(132): the column's scarce axis is
        // vertical, Done already takes the floor for exactly that reason, and 3x20px of CTA
        // flourish is what buys the fit. One column, one box size, one rhythm.
        // X: box right edge 72px in from the screen edge -> x 1588..1848 at 1920 wide (the
        // capture-documented D15 column). Both canvases resolve 1:1 in landscape (this modal
        // canvas is 1080x1920 ref, match 0.5), so these px are directly comparable with
        // BuildHudController's.
        private const float QuickTabGutterPx     = 16f;
        private const float QuickTabEdgeInsetPx  = 72f;
        /// <summary>Clearance the stack keeps above the dock and below Done.</summary>
        private const float QuickTabClearPx      = 9f;
        /// <summary>One tab's box height — the kit MinTouch floor, matching Done's box.</summary>
        public const float QuickTabHeightPx = ElarionUiKit.MinTouchPx;   // 112
        /// <summary>TOP of the carousel dock in reference px from the canvas bottom: the D19
        /// strip's reserved band + the dock's own fixed height (98 + 303 = 401). PUBLIC so a
        /// neighbouring lane can prove it seats clear without re-deriving the dock.</summary>
        public const float DockTopPx = BuildHudController.ResourceStripReservedPx + DockHeightPx;
        /// <summary>BOTTOM of the quick-tab stack (410) — 9px over the dock, which is the
        /// TALLER of the two things below it (the D14 verb row tops out at 246).</summary>
        public const float QuickTabStackBottomPx = DockTopPx + QuickTabClearPx;
        /// <summary>TOP of the quick-tab stack (778) = 410 + 3*112 + 2*16. PUBLIC because
        /// BuildHudController seats Done off it — it used to hand-copy the number, and a
        /// hand-copied number is how the seat went stale the first time.</summary>
        public const float QuickTabStackTopPx =
            QuickTabStackBottomPx + QuickTabHeightPx * 3f + QuickTabGutterPx * 2f;
        /// <summary>Bottom breathing room under the card tray so a device safe-area inset
        /// (gesture bar / rounded corner) cannot clip the cost line off the cards.</summary>
        private const float TrayBottomInsetPx = 28f;
        /// <summary>Fixed pixel width of one card (never a fraction of the band — UI_PLAYBOOK §3).
        /// Named because the D13 band-coverage trace has to reason about it.</summary>
        private const float CardWidthPx = 260f;
        /// <summary>WO-1172 Option B — width bounds for one filter chip. Width scales with the
        /// DATA label (group labels are authored, variable length) between these clamps; height
        /// is the band's 112px (MinTouchPx — chips are CONTROLS, unlike Option A's header
        /// plates, so the touch floor binds).</summary>
        private const float ChipMinWidthPx = 160f;
        private const float ChipMaxWidthPx = 340f;

        // ── WO-1186: THE CHIP BAND AND THE CRYSTAL READ-OUT ARE TWO DISJOINT BANDS ──────────
        // Proving log: Builds/uicap-0825am.log (UI_CAPTURE_OK 89), three findings, one per
        // captured resolution (1920x1080 / 2340x1080 / 2670x1200 -- the Seeker's real surface):
        //   'PaletteDock/ChipRow/Chips/Chip_Other' (x 396..603) covers
        //   'PaletteDock/ChipRow/Text' ("Crystals: 0") (x 468..756.6) by 135x96 ref px.
        // The chip host was anchored 0..0.80 of a 1560px dock (x -780..468) and the read-out
        // 0.80..0.985 (x 468..756.6) -- authored disjoint, and NOT disjoint on screen, because
        // the HorizontalLayoutGroup does not shrink its children: six chips at their natural
        // widths ran 1391px inside a 1248px host and simply OVERFLOWED into the read-out. A
        // chip is a Button and the count is not, so the chip won every tap over the number the
        // player uses to judge whether she can afford the placement.
        //
        // ⛔ THE FIX IS GEOMETRIC, NOT Z-ORDER (WO-1186 AC3): a transparent-but-raycasting
        // control still steals the tap, so re-stacking would have hidden the defect, not fixed
        // it. Three parts, all geometry:
        //   1. the bands are named consts with a REAL GUTTER between them (below);
        //   2. the chip run is FIT to its band -- widths scale down toward the touch floor so
        //      the natural run never exceeds the host (RebuildChips);
        //   3. the host CLIPS (RectMask2D), so even an unfittable run is contained. A masked
        //      region does not raycast either, so the clip removes the tap-theft as well as the
        //      overprint -- containment, not concealment.
        /// <summary>The dock's own fixed reference width. Named because the chip-fit math has to
        /// resolve the band widths BEFORE the layout runs, and a re-typed 1560 is how that goes
        /// stale (the fixed-pixel-band rule, UI_PLAYBOOK §3).</summary>
        private const float DockWidthPx = 1560f;
        /// <summary>Right edge of the CHIP band as a fraction of the dock (x -780..553.8).</summary>
        private const float ChipBandRightFrac = 0.855f;
        /// <summary>Left edge of the READ-OUT band (x 569.4) -- 15.6px clear of the chip band, so
        /// the two never share a pixel even at the sub-pixel rounding the oracle measures at.</summary>
        private const float ReadoutBandLeftFrac = 0.865f;
        /// <summary>Right edge of the READ-OUT band (x 756.6) -- unchanged from before WO-1186,
        /// so the number sits exactly where the owner has been reading it.</summary>
        private const float ReadoutBandRightFrac = 0.985f;
        /// <summary>Gap between two chips. Shared by the layout group AND the fit math -- one
        /// source, because two copies of a spacing is how the run mis-measures its own width.</summary>
        private const float ChipGapPx = 12f;
        /// <summary>Chip run inset from the left edge of its host.</summary>
        private const float ChipPadLeftPx = 16f;
        /// <summary>Chip run inset from the right edge of its host.</summary>
        private const float ChipPadRightPx = 8f;
        /// <summary>Vertical inset of a chip inside the 112px band.</summary>
        private const float ChipPadVertPx = 8f;

        // WO-673 category switcher (always on — WO-682), RE-HOMED by WO-1010 D21: the
        // owner-ruled three build categories — Town / Defense / Castle Structures (the
        // renamed Walls) — now live in the right-edge quick-tab stack, NOT a bottom tab
        // row. Tapping a tab Configure()s this palette for that verb (placement stays
        // generic; BuildModeController's _activeBuildType is only ever used to Configure
        // this palette). The active tab carries a gold UNDERLINE — position/shape tell,
        // never color alone (owner is red/green colorblind). The BuildTabRow component is
        // RETIRED from the dock (no callers remain — see its header note).
        private BuildType _activeType = BuildType.Defense;

        private void OnEnable()
        {
            // MVVM Silo C: the VM owns the live wallet subscriptions (EconomyService.OnChanged
            // + GameState.ResourcesChanged). The View just binds the VM's Changed so per-card
            // cost/affordability stays live (owner felt-test 2026-07-17 "update the price").
            EnsureVm();
            _vm.Changed -= OnVmChanged;
            _vm.Changed += OnVmChanged;
        }

        private void OnDisable()
        {
            _collectionBrowser?.Close();
            if (_vm != null) _vm.Changed -= OnVmChanged;
        }

        private void OnDestroy()
        {
            if (_vm != null) { _vm.Changed -= OnVmChanged; _vm.Dispose(); _vm = null; }
            _collectionBrowser?.Close();
            if (_canvas != null) Destroy(_canvas);
        }

        /// <summary>The VM re-projected (a wallet mutation or a verb change) — re-render if shown.</summary>
        private void OnVmChanged()
        {
            if (_canvas != null && _canvas.activeSelf) Render();
        }

        /// <summary>Create + bind the paired VM (idempotent). The sole VM-resolution point.</summary>
        private void EnsureVm()
        {
            if (_vm != null) return;
            _vm = BuildPaletteVM.CreateDefault(_activeType, null);
            _vm.Changed -= OnVmChanged;
            _vm.Changed += OnVmChanged;
        }

        // ── Show / Hide ────────────────────────────────────────────────────────

        public void Show()
        {
            // WO-1273: Build browsing is category-first and remains paused until the
            // player chooses Place or exits. Placement still enters through the exact
            // existing OnEntrySelected -> BuildModeController.Arm seam.
            if (_collectionBrowser == null)
            {
                _collectionBrowser = gameObject.GetComponent<BuildCollectionBrowser>();
                if (_collectionBrowser == null)
                    _collectionBrowser = gameObject.AddComponent<BuildCollectionBrowser>();
            }
            if (_canvas != null) _canvas.SetActive(false);
            // WO-2006 (ruling §25): the second callback is the MANAGE PLACED door. Passing
            // it here is what makes the card exist at all — BuildCollectionBrowser refuses to
            // build a card it cannot honour.
            _collectionBrowser.Show(
                entry => OnEntrySelected?.Invoke(entry),
                () => OnManagePlacedRequested?.Invoke());
        }

        /// <summary>
        /// WO-1571 - forward a DIRECT placement pick to the collection browser, which honours it
        /// through its own Place/Done seam (so OnEntrySelected -> BuildModeController.Arm still
        /// runs, unchanged). Returns false when the browser is absent or the row is not offered;
        /// the caller then leaves the ordinary root standing.
        /// </summary>
        public bool PlaceById(string id) =>
            _collectionBrowser != null && _collectionBrowser.PlaceById(id);

        public void Hide()
        {
            _collectionBrowser?.Close();
            if (_canvas != null) _canvas.SetActive(false);
            _armedId = null;
        }

        /// <summary>
        /// WO-352 — set which entry the palette shows as ARMED (gilt highlight + Orient
        /// button), without raising OnEntrySelected. The controller calls this after the
        /// player confirms "Place" in the Structure Info Preview, so the palette stays in
        /// sync with the deferred-arm flow. Pass null to clear the highlight.
        /// </summary>
        public void SetArmed(string id)
        {
            _armedId = id;
            if (_canvas != null && _canvas.activeSelf) Render();
        }

        private void EnsureBuilt()
        {
            if (_canvas != null) return;

            _canvas = ElarionUiKit.BuildModalCanvas("BuildPaletteCanvas", SortingOrder);

            // Bottom-CENTERED dock sized to content (owner F8 2026-07-06, board #4):
            // the palette lists 3 cards, so it no longer spans a full-width black wall.
            // 540 wide = padding 24 + 3×160 cards + 2×10 spacing; 224 tall = 44px
            // header row (balance | Orient | Done) over a 180px card tray. Only the
            // dock's own graphics raycast — the rest of the screen stays click-through
            // so world taps still land placements.
            // WO-673 (always on — WO-682): the dock carries a category tab row
            // (Town / Defenses / Walls) between the header and the card tray.
            // Band split rebalanced (owner felt-test 2026-07-15 "long thin rectangles"):
            // the header + tab bands are tall enough (~140px at the 540-tall dock) to
            // seat a CanonCtaHeight (132px) button WITHOUT overflowing into the tray, so
            // the tabs + Orient/Done render as proper boxes, not full-band thin bars.
            // header 0.74–1.0 (~140px), tabs 0.48–0.74 (~140px), tray 0–0.48 (~259px).
            // WO-1010 cosmetic band-tightening (2026-08-09, owner mockup bar "this needs to
            // be clean"): the 540px dock carried a ~140px HEADER band whose only tenant was
            // the 16px Crystals line — a third of the panel read as empty black (the owner's
            // "This screen is not correct" PICK capture). The header band is COLLAPSED to
            // zero (kept as a GO for the Collapse() seam).
            // WO-1010 D21 (owner, late 2026-08-09): the CATEGORY TAB BAND DISSOLVES from the
            // dock entirely — the tabs move to the right-edge quick-tab stack. The dock slims
            // to the CARD ROW + one slim crystals line: tray 259px + crystals 44px = 303px,
            // fixed pixels, centred, resting on the D19 resource frame. No dead band.
            // (TrayHeightPx / CrystalsBandPx / DockHeightPx are class consts now — COLUMN-FIT 2026-08-16,
            // so the quick-tab band math can seat off the dock's real height.)
            const float trayTop = TrayHeightPx / DockHeightPx;             // ~0.630 of 303px (WO-1172 B)
            const float headerBottom = 1.0f;

            // Grok slice 4 (landscape density): the shop is now a LARGE landscape
            // bottom carousel, not the old 540px portrait dock — wider so more
            // icon-first tiles read at once (owner CoC shop bar). Bottom-centred.
            var dock = new GameObject("PaletteDock", typeof(RectTransform));
            dock.transform.SetParent(_canvas.transform, false);
            var drt = (RectTransform)dock.transform;
            drt.anchorMin = new Vector2(0.5f, 0f);
            drt.anchorMax = new Vector2(0.5f, 0f);
            drt.pivot = new Vector2(0.5f, 0f);
            // WO-1010 D19 SEATING: the dock's bottom clears the resource strip's PUBLISHED
            // reserved band (BuildHudController.ResourceStripReservedPx — same assembly, the
            // const exists precisely so this lane can seat clear without a cross-edit). The
            // strip is a bottom-centre band on a HIGHER sorting order (906 vs this canvas's
            // 900); anchored flush to 0 the dock's card tray drew UNDER it and the strip
            // overprinted the cards' cost line. Fixed pixels, not a fraction, per the
            // fixed-pixel-band rule — in PICK the carousel now rests ON the strip, never
            // overlapping the cards (D19: "the carousel rests on it").
            drt.anchoredPosition = new Vector2(0f, BuildHudController.ResourceStripReservedPx);
            FlowTrace.Step("BuildPalette",
                "D19 seating: dock bottom lifted to " + BuildHudController.ResourceStripReservedPx +
                "px (the strip's published reserved band) -- carousel rests ON the strip, cards clear of it");
            // Phone enlargement (owner felt-test 2026-07-14 "make it larger for
            // selection on a phone"): wide dock so the shop tiles read big and
            // thumb-reachable on a small landscape phone screen (CoC shop bar).
            // Height history: 440 -> 540 (2026-07-15, band rebalance) -> 410 (2026-08-09
            // band-tightening) -> 303 (2026-08-09 D21: the tab band dissolved to the
            // right-edge stack, so the dock is exactly the ~259px card tray + a 44px
            // crystals line — the mockup's slim bottom panel).
            drt.sizeDelta = new Vector2(DockWidthPx, DockHeightPx);

            // Slim header row: obsidian fill + gold under-rule (the kit panel language).
            // Held as _topBarGo so Collapse() can hide the whole header band (it was the
            // "giant black pane" left standing during placement — owner device screenshots).
            var topBar = ElarionUiKit.AddImage(dock.transform, "TopBar",
                new Vector2(0f, headerBottom), new Vector2(1f, 1f), ElarionUiKit.ObsidianFill, rounded: false);
            _topBarGo = topBar;
            var rule = ElarionUiKit.AddImage(topBar.transform, "GoldRule",
                new Vector2(0f, 0f), new Vector2(1f, 0f), ElarionUiKit.ObsidianTrim, rounded: false);
            var rrt = rule.GetComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(0f, 2f);
            rrt.pivot = new Vector2(0.5f, 0f);
            var ruleImg = rule.GetComponent<Image>();
            if (ruleImg != null) ruleImg.raycastTarget = false;

            // (WO-1010 band-tightening: the balance label used to live here in the header
            // band; it now seats beside the tabs — see below, after the tab row builds —
            // so the header band could collapse to zero and stop reading as empty black.)

            // WO-1010 D1 (owner screenshot review 2026-08-08): the "Orient" WORD-BUTTON that
            // used to be pinned here (dock top-right, 300x132, dev-gated) is REMOVED. It was
            // not in this WO's spec — §2 retires the rotation word-buttons in favour of the ONE
            // rotate control on the ghost/rail — and because it survived Collapse it sat over
            // the HUD's Echoes chip during placement, clipping it to "hoes 1/6". The
            // OnOrientRequested EVENT is kept (BuildModeController still subscribes it); only
            // the on-screen chrome is gone.

            // NOTE (2026-07-16 redesign): the palette's own "Done" exit was REMOVED to end
            // the duplicate-exit problem — the ONE exit is BuildHudController's top-band
            // "X Done" (always visible while Build Mode is open). OnExitRequested stays on
            // the API for back-compat but is no longer raised from this strip.

            // WO-1010 D21: the bottom CategoryTabs band + BuildTabRow are GONE — the
            // category selector is the right-edge quick-tab stack (built below, canvas
            // level, so it survives Collapse).
            // WO-1172 Option B (owner pick 2026-08-24): the slim crystals line grew into the
            // CHIP BAND — the group-filter chips (All / <authored group labels> / Other, all
            // DATA except the fixed "All") on the left, the crystals read-out folded into the
            // right end. Chips are rebuilt per Render (RebuildChips) because the sections are
            // data-driven and change with the verb; only the band chrome is built here.
            var chipRow = ElarionUiKit.AddImage(dock.transform, "ChipRow",
                new Vector2(0f, trayTop), new Vector2(1f, headerBottom),
                ElarionUiKit.ObsidianFill, rounded: false);
            _crystalsRowGo = chipRow;

            // WO-1186: the chip host CLIPS. RectMask2D is the hard containment backstop under
            // the fit math in RebuildChips -- and it is a raycast filter as well as a visual
            // one, so a chip that runs past the band edge is neither drawn nor tappable there.
            // That is what makes this a geometric containment and not a z-order dodge.
            var chipHostGo = new GameObject("Chips",
                typeof(RectTransform), typeof(RectMask2D), typeof(HorizontalLayoutGroup));
            chipHostGo.transform.SetParent(chipRow.transform, false);
            var chipRt = chipHostGo.GetComponent<RectTransform>();
            chipRt.anchorMin = new Vector2(0f, 0f);
            chipRt.anchorMax = new Vector2(ChipBandRightFrac, 1f);
            chipRt.offsetMin = Vector2.zero; chipRt.offsetMax = Vector2.zero;
            var chipLayout = chipHostGo.GetComponent<HorizontalLayoutGroup>();
            chipLayout.spacing = ChipGapPx;
            chipLayout.padding = new RectOffset(
                (int)ChipPadLeftPx, (int)ChipPadRightPx, (int)ChipPadVertPx, (int)ChipPadVertPx);
            chipLayout.childAlignment = TextAnchor.MiddleLeft;
            chipLayout.childControlWidth = false;     // chips keep their own (fitted) width
            chipLayout.childControlHeight = true;     // chips fill the 112px band (touch floor)
            chipLayout.childForceExpandWidth = false;
            chipLayout.childForceExpandHeight = true;
            _chipHost = chipHostGo.transform;
            FlowTrace.Step("BuildPalette",
                "WO-1186 bands: chips 0.." + ChipBandRightFrac.ToString("0.###") +
                " (" + (DockWidthPx * ChipBandRightFrac).ToString("F0") + "px, clipped), readout " +
                ReadoutBandLeftFrac.ToString("0.###") + ".." + ReadoutBandRightFrac.ToString("0.###") +
                " (" + (DockWidthPx * (ReadoutBandRightFrac - ReadoutBandLeftFrac)).ToString("F0") +
                "px) -- disjoint, gutter " +
                (DockWidthPx * (ReadoutBandLeftFrac - ChipBandRightFrac)).ToString("F1") + "px");

            _balanceLabel = MakeText(chipRow.transform, "Crystals: 0", 16, ElarionUi.Gilt,
                FontStyles.Bold, TextAlignmentOptions.Right,
                new Vector2(ReadoutBandLeftFrac, 0.05f), new Vector2(ReadoutBandRightFrac, 0.95f));
            // NoWrap: the read-out band is 187px and the string is short (CompactNumber), but a
            // wrapped count would grow the text rect downward into the chips' vertical band --
            // the same defect on the other axis.
            _balanceLabel.textWrappingMode = TextWrappingModes.NoWrap;

            // Bottom: horizontal-scrolling slot-plate card tray in a recessed dark well
            // (content-width now, so it reads as a dock — not a screen-wide wall).
            // WO-1010 D13 — THE TRAY IS OPAQUE. It used to be Color(0,0,0,0.55): a 1560px-wide,
            // 45%-SEE-THROUGH sheet laid straight over the live 3D town. That is a direct
            // violation of UI_PLAYBOOK §6 ("anything over the 3D field must carry its OWN edge —
            // it may not borrow contrast from the world"), and it is the mechanism behind D13's
            // "raw 3D models spilled over the world ... over the field AND PANEL (no obsidian
            // card frames)" plus "stray prop models scatter across the bottom band": the shop
            // band was a WINDOW, and what showed through it was the town's real buildings at
            // world scale, wearing no card frame because they were never cards.
            //
            // WHY THE OWNER ONLY SAW IT ON THE DEFENSES TAB — the tab-specificity is the proof.
            // Town lists SIX rows (Resource + Collector) and its cards fill the tray edge to
            // edge, so almost no glass is left uncovered. Defenses lists only THREE unlocked
            // rows — Archer Tower, Ballista, Arcane Spire; tower_siege_tower / tower_catapult /
            // gate_stone are lockedIds in build-categories.json — so ~3x260px of a ~1560px band
            // is card and the remaining ~45% is bare glass onto the field. Nothing about the
            // card CODE differs between the tabs (verified: one BuildCard path, and all three
            // Defenses portraits share byte-identical import settings); the only variable is how
            // much of the band the cards happen to cover.
            //
            // Obsidian fill + a gold top rule (the kit panel language, WO-562) so the band reads
            // as a dock in front of the world instead of a tinted pane of it.
            var tray = ElarionUiKit.AddImage(dock.transform, "CardTray",
                new Vector2(0f, 0f), new Vector2(1f, trayTop),
                ElarionUiKit.ObsidianFill, rounded: false);
            var trayRule = ElarionUiKit.AddImage(tray.transform, "TrayTopRule",
                new Vector2(0f, 1f), new Vector2(1f, 1f), ElarionUiKit.ObsidianTrim, rounded: false);
            var trayRuleRt = trayRule.GetComponent<RectTransform>();
            trayRuleRt.sizeDelta = new Vector2(0f, 2f);
            trayRuleRt.pivot = new Vector2(0.5f, 1f);
            var trayRuleImg = trayRule.GetComponent<Image>();
            if (trayRuleImg != null) trayRuleImg.raycastTarget = false;
            // SAFE-AREA INSET AT THE BOTTOM. The tray anchored flush to 0, so the card plates
            // ran to the very edge of the canvas with the COST LINE sitting on it — the first
            // card capture shows it. On any device with a gesture bar or a rounded corner, the
            // price is the first thing the inset eats, and a card whose cost you cannot read is
            // exactly the confusion WO-1010 exists to remove. A fixed pixel inset (not a
            // fraction) per the WO's fixed-pixel-band rule, so it does not scale away on a
            // short canvas.
            var trayRt = tray.transform as RectTransform;
            if (trayRt != null) trayRt.offsetMin = new Vector2(trayRt.offsetMin.x, TrayBottomInsetPx);
            _trayGo = tray;

            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D), typeof(Image));
            scrollGo.transform.SetParent(tray.transform, false);
            var srt = scrollGo.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);   // raycast surface for drag-scroll

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 0f); crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var layout = contentGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.childControlWidth = false;    // cards keep their fixed width
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            _stripContent = contentGo.transform;

            // (2026-07-16 redesign) The old full-dock "PlacingSummary" ObsidianFill panel
            // was REMOVED — spanning 0..headerBottom of the dock, it WAS the black wall that
            // covered the map during placement. The "Placing: <name>" text now lives as a
            // slim pill in the HUD intent cluster (BuildHudController.SetPlacingLabel), so
            // Collapse just hides every dock background and leaves the map fully visible.

            // WO-1010 D21: the PERMANENT right-edge quick-tab stack — canvas-level (not a
            // dock child) so it stands in BOTH the PICK phase and the collapsed state.
            EnsureQuickTabs();

            _canvas.SetActive(false);   // built hidden; Show shows it
        }

        // ── Grok slice 4: collapse-on-place (owner "minimize on select") ──────────

        /// <summary>
        /// FULLY minimize the shop while placing (owner redesign 2026-07-16): hide EVERY
        /// dock background — the header band, the crystals line, AND the card tray — so NO
        /// black wall covers the map/ghost. The "Placing: &lt;name&gt;" label is folded into
        /// the HUD intent cluster (BuildHudController.SetPlacingLabel), so the dock shows no
        /// summary panel of its own. What stands is the PERMANENT right-edge quick-tab stack
        /// (D21) — in this state it is the way back into a pre-filtered carousel. (The
        /// dev-only Orient button that used to survive Collapse here is GONE, WO-1010 D1.)
        /// Called from BuildModeController.Arm. <paramref name="armedDisplayName"/> is
        /// retained for API compat (the label is now owned by the HUD). Safe before build
        /// (no-op).
        /// </summary>
        /// <summary>
        /// True while the shop is minimized to the quick-tabs. WO-1010 D12: the build nudge
        /// stick is gated on item-selected AND carousel-minimized (owner 2026-08-09), so the HUD
        /// needs the second condition and the brain reads it from here rather than inferring it.
        /// </summary>
        public bool IsCollapsed { get; private set; }

        public void Collapse(string armedDisplayName)
        {
            // =================================================================
            //  WO-1581 - COLLAPSE MUST CLOSE WHAT EXPAND OPENS. THIS IS THE FIX.
            //
            //  WO-1273 redefined Expand(): it no longer restores a dock carousel, it
            //  calls Show(), which OPENS the full-screen modal BuildCollectionBrowser
            //  and takes a PLAYER-OWNED WorldHold (timeScale 0). Collapse was NOT
            //  updated to match - it went on hiding legacy _canvas chrome and never
            //  touched _collectionBrowser. The pair stopped being symmetric, and every
            //  EDIT-side caller paid for it:
            //
            //    * BuildModeController.SelectStructure calls CancelArmed() (which calls
            //      Expand) and then Collapse(label) "so the two do not fight". After
            //      WO-1273 the Collapse no longer wins, so tapping a placed structure
            //      re-opened the CATALOG on top of the Move/Upgrade/Sell panel the
            //      player had just asked for. Device log, build 2026.09.07.359076:
            //        08:28:02.424  tap-select: hit - SELECTS 'lumberyard'
            //        08:28:02.429  PanelManager: 'Build Collections' opened and verified visible
            //      The tester's "I cannot find MOVE" is that pair of lines: the MOVE /
            //      CANCEL row was rendered, then buried under the catalog frame.
            //    * BuildModeController.BeginManagePlaced (the WO-2006 ruling-25 door)
            //      had no Collapse at all, so the card closed the browser and CancelArmed
            //      re-opened it in the SAME frame - "manage buildings doesnt do anything".
            //
            //  The browser close goes BEFORE the `_canvas == null` guard on purpose: the
            //  legacy dock canvas is built lazily and Show() deactivates it, so a guard
            //  that returns first would silently skip the only line that still matters.
            //  Guarded on IsOpen so the Arm path - where Done() has already closed the
            //  browser - does not emit a second close / a second WorldHold release.
            // =================================================================
            if (_collectionBrowser != null && _collectionBrowser.IsOpen)
            {
                FlowTrace.Step("BuildPalette",
                    "collapse: CLOSING the collection browser (WO-1273 made Expand open a modal " +
                    "workspace; Collapse is its inverse). Without this the catalog stands over " +
                    "the selection panel and MOVE/UPGRADE/SELL are unreachable.");
                _collectionBrowser.Close();
            }

            if (_canvas == null) return;
            FlowTrace.Step("BuildHud", $"Collapse refs: topBar={_topBarGo!=null} tray={_trayGo!=null} crystals={_crystalsRowGo!=null}");
            if (_topBarGo != null) _topBarGo.SetActive(false);
            if (_trayGo != null) _trayGo.SetActive(false);
            if (_crystalsRowGo != null) _crystalsRowGo.SetActive(false);
            IsCollapsed = true;
            // WO-1010 D21: the quick-tab stack is PERMANENT (visible in PICK too) — Collapse
            // only guarantees it exists and its counts are fresh; nothing toggles here.
            EnsureQuickTabs();
            RefreshQuickTabCounts();
            FlowTrace.Step("BuildHud",
                "palette collapsed: all dock chrome hidden (no black wall) — Placing label folded into intent bar; right-edge quick-tabs stand");
        }

        // ── WO-1010 D21: the PERMANENT right-edge category quick-tab stack ─────
        /// <summary>
        /// Ensure the quick-tab stack exists and is up. WO-1010 D21 (owner ruling late
        /// 2026-08-09): the stack is the ONE category selector, visible in BOTH the PICK
        /// phase and the collapsed/placing state — the bottom tab band is gone, so hiding
        /// this would leave the shop with no category affordance at all.
        ///
        /// Rebuilds if the list is empty OR its entries were destroyed with a previous
        /// canvas (Unity fake-null): a stale list would silently leave the shop with no
        /// selector, the exact dead end P2 existed to prevent.
        /// </summary>
        private void EnsureQuickTabs()
        {
            if (_canvas == null) return;
            if (_quickTabs.Count == 0 || _quickTabs[0].Go == null)
            {
                _quickTabs.Clear();
                BuildQuickTabs();
            }
            for (int i = 0; i < _quickTabs.Count; i++)
            {
                var t = _quickTabs[i];
                if (t.Go != null && !t.Go.activeSelf) t.Go.SetActive(true);
            }
        }

        /// <summary>Refresh each tab's live count caption ("Town (6)").</summary>
        private void RefreshQuickTabCounts()
        {
            for (int i = 0; i < _quickTabs.Count; i++)
            {
                var t = _quickTabs[i];
                if (t.Go == null || t.Label == null) continue;
                t.Label.text = t.Caption + " (" + CountForType(t.Type) + ")";
            }
        }

        /// <summary>
        /// How many cards a category verb WOULD show, without disturbing the live projection.
        ///
        /// The counts are computed on a THROWAWAY VM over the same providers, never by
        /// re-Configuring the live one: the live VM is what the carousel is currently rendering
        /// (and, while minimized, what the player is placing from), so counting by mutating it
        /// would re-render the shop underneath an in-flight placement. A count is a question,
        /// not a state change.
        /// </summary>
        private static int CountForType(BuildType type)
        {
            int n = 0;
            Guard.Try("BuildPalette", "count type " + type, () =>
            {
                BuildPaletteVM probe = null;
                try
                {
                    probe = BuildPaletteVM.CreateDefault(type, null);
                    n = probe.Cards != null ? probe.Cards.Count : 0;
                }
                finally
                {
                    // CreateDefault subscribes the live wallet feeds. A throw between here and
                    // Dispose would leak that subscription onto a VM nothing renders, so the
                    // teardown is in a finally, not on the happy path.
                    probe?.Dispose();
                }
            });
            return n;
        }

        private void BuildQuickTabs()
        {
            if (_canvas == null) return;

            // RIGHT EDGE, stacked VERTICALLY in the MIDDLE band (D21/COLUMN-FIT 2026-08-16 — band math at
            // the QuickTab* consts above: the dock tops out at 401 and the horizontal D14 verb
            // row at 246; this stack owns 410..778, with Done at 787..899 above it).
            // Fixed-pixel 260x112 boxes (QuickTabHeightPx = the MinTouchPx floor, the same box
            // Done uses) so a wide landscape canvas cannot stretch them into thin bars.
            //
            // THREE categories (owner D8 resolution via D21, 2026-08-09): Town / Defense /
            // Castle Structures — where Castle Structures is the RENAMED Walls display
            // category (walls + gates-to-come, verticality later). Display rename only: the
            // underlying BuildType.Walls key and build-categories.json keys are untouched.
            // The Walls verb stays behind FeatureFlags.WallsTab (defaultOn flipped TRUE by
            // the same ruling) — flag off = two tabs, exactly as the old bottom row did.
            bool wallsOn = DeNelle.Core.FeatureFlags.WallsTab;
            float step = QuickTabHeightPx + QuickTabGutterPx;                     // 128
            float bottomCentre = QuickTabStackBottomPx + QuickTabHeightPx * 0.5f; // 466
            // Top -> bottom reads Town / Defense / Castle Structures (the ruling's order).
            AddQuickTab("Town", BuildType.Town, bottomCentre + 2f * step, "BuildPaletteRestoreTab");
            AddQuickTab("Defense", BuildType.Defense, bottomCentre + step, "BuildPaletteQuickTab_Defense");
            if (wallsOn)
            {
                // Caption resolves through the DATA display seam (build-categories.json
                // 'label' via BuildCategoryRegistry) so the rename lives in one place; the
                // registry's hardcoded fallback mirrors it.
                string castleCaption = BuildCategoryRegistry.Get(BuildType.Walls).Label;
                if (string.IsNullOrEmpty(castleCaption) || castleCaption == "Build") castleCaption = "Castle Structures";
                AddQuickTab(castleCaption, BuildType.Walls, bottomCentre, "BuildPaletteQuickTab_CastleStructures");
            }
            FlowTrace.Step("BuildPalette",
                "D21/COLUMN-FIT 2026-08-16 quick-tab stack built: " + _quickTabs.Count + " tabs, box " + RestoreTabW +
                "x" + QuickTabHeightPx + "px at x-inset " + QuickTabEdgeInsetPx + ", y band " +
                QuickTabStackBottomPx + ".." + QuickTabStackTopPx + " (3x" + QuickTabHeightPx +
                "px + 2x" + QuickTabGutterPx + "px gutters), " + QuickTabClearPx +
                "px clear of the dock top " + DockTopPx + " and of Done's bottom " +
                (QuickTabStackTopPx + QuickTabClearPx) + "; the D14 verb row is HORIZONTAL now and " +
                "tops out at " + BuildHudController.VerbRowTopPx + ", far below this stack. " +
                "Walls/'Castle Structures' tab " + (wallsOn ? "PRESENT" : "ABSENT") +
                " (FeatureFlags.WallsTab=" + wallsOn + ")");
        }

        /// <summary>
        /// One right-edge quick-tab. <paramref name="objectName"/> is explicit because
        /// UICaptureLaunch asserts the collapsed palette still has an active
        /// "BuildPaletteRestoreTab" — the first tab keeps that name so the capture contract
        /// survives the P2 -> D15 -> D21 changes without editing the capture harness.
        /// <paramref name="yCentrePx"/> is FIXED reference px from the canvas BOTTOM (the
        /// same coordinate system the D14 rail seats in), never a fraction of height — a
        /// fraction would drift the stack into the rail's band on a short canvas.
        /// </summary>
        private void AddQuickTab(string caption, BuildType type, float yCentrePx, string objectName)
        {
            var btn = ElarionUiKit.BuildObsidianButton(_canvas.transform, caption,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.90f, 0.5f), new Vector2(0.90f, 0.5f),
                () =>
                {
                    bool collapsed = IsCollapsed;
                    FlowTrace.Step("BuildPalette",
                        "quick-tab tapped type=" + type + " collapsed=" + collapsed +
                        (type == BuildType.Walls
                            ? " (walls surfacing as 'Castle Structures', FeatureFlags.WallsTab=" +
                              DeNelle.Core.FeatureFlags.WallsTab + ")"
                            : string.Empty) +
                        (collapsed
                            ? " -> reopening the carousel PRE-FILTERED (standard no-charge cancel of any un-placed ghost)"
                            : " -> re-pointing the card row (PICK phase category switch)"));
                    EnsureVm();
                    if (collapsed)
                    {
                        // Filter FIRST, then ask the brain to restore: OnRestoreRequested routes
                        // through CancelArmed -> Expand -> Render, so the verb must already be
                        // set or the carousel would come back on the previous category and only
                        // switch on the next re-render.
                        _vm.Configure(type);
                        _activeType = type;
                        _activeFilterLabel = null;   // WO-1172 B: verb change -> chips back to All
                        UpdateTabHighlight();
                        OnRestoreRequested?.Invoke();
                    }
                    else
                    {
                        // PICK phase: the tap IS the category switch (the old bottom-tab path).
                        Configure(type);
                    }
                });
            if (btn == null) return;
            btn.gameObject.name = objectName;
            // FIXED-PIXEL seat: anchor to the canvas' bottom-right corner and stamp the box.
            // (PinSize would centre on the fraction anchor rect, i.e. a HEIGHT fraction —
            // exactly the drift the band math forbids.)
            var rt = btn.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(RestoreTabW, QuickTabHeightPx);
                rt.anchoredPosition = new Vector2(-(QuickTabEdgeInsetPx + RestoreTabW * 0.5f), yCentrePx);
            }

            // Active-category tell: a gilt underline pinned to the tab's bottom edge —
            // POSITION + SHAPE carry the meaning, never colour alone (owner is red/green
            // colourblind). Same grammar the retired BuildTabRow used.
            var underline = new GameObject("ActiveUnderline", typeof(RectTransform), typeof(Image));
            underline.transform.SetParent(btn.transform, false);
            var urt = (RectTransform)underline.transform;
            urt.anchorMin = new Vector2(0.08f, 0f);
            urt.anchorMax = new Vector2(0.92f, 0f);
            urt.pivot = new Vector2(0.5f, 0f);
            urt.sizeDelta = new Vector2(0f, 3f);
            var uimg = underline.GetComponent<Image>();
            uimg.color = ElarionUi.Gilt;
            uimg.raycastTarget = false;
            underline.SetActive(type == _activeType);

            // Tutorial spotlight re-registration (WO-1010 D21): the bottom tab row that
            // owned "build.tab_town"/"build.tab_defenses" is gone — the highlights re-home
            // here so the founding tutorial beats never dangle. Registry re-register is
            // idempotent (last write wins; fake-null guard drops destroyed rects).
            if (type == BuildType.Town && rt != null)
                TutorialHighlightRegistry.Register("build.tab_town", rt);
            else if (type == BuildType.Defense && rt != null)
                TutorialHighlightRegistry.Register("build.tab_defenses", rt);

            _quickTabs.Add(new QuickTab
            {
                Go = btn.gameObject,
                Label = btn.GetComponentInChildren<TMP_Text>(true) as TextMeshProUGUI,
                Type = type,
                Caption = caption,
                Underline = underline
            });

            // Capture-caught (wave-4 PICK PNG): "Castle Structures (N)" ELLIPSIZED inside the
            // 260px box — the kit button's fixed font size cannot know this stack carries the
            // one long caption. Autosize down instead of truncating: a shrunk word is
            // readable, "Castle Struct..." is not (and the ellipsis ate the live count).
            var qtLabel = _quickTabs[_quickTabs.Count - 1].Label;
            if (qtLabel != null)
            {
                qtLabel.textWrappingMode = TextWrappingModes.NoWrap;
                qtLabel.enableAutoSizing = true;
                qtLabel.fontSizeMin = 13f;
            }
        }

        /// <summary>
        /// Expand the shop back to the full header + tabs + carousel (called from CancelArmed,
        /// i.e. every return-to-carousel: after a placement OR a cancel). Owner felt-test
        /// 2026-07-17 fixes both palette defects at the ONE return point:
        ///  - GLOW: clear <see cref="_armedId"/> so the last-picked card's gilt icon halo does
        ///    not "just stay on" — the carousel comes back with NO card armed, so the glow is
        ///    the truthful single-selection cue (exactly one armed card, or none), never stuck.
        ///  - PRICE: RE-RENDER so every card recomputes its CURRENT cost live. A just-placed
        ///    building's first-build freebie is now consumed, so its card flips FREE -> real
        ///    cost on close (not only on reselect). A freebie placement mutates no wallet, so
        ///    neither ResourcesChanged nor OnChanged would otherwise fire this refresh.
        /// </summary>
        public void Expand()
        {
            // PROD-018: WO-1273 retired the legacy carousel as the browsing owner.
            // Placement/cancel still returns through Expand(), so restore the live
            // collection browser and force it to re-evaluate singleton eligibility.
            _armedId = null;
            IsCollapsed = false;
            Show();
            FlowTrace.Step("BuildPalette",
                "expand: restored BuildCollectionBrowser categories after place/cancel; " +
                "eligibility re-rendered and legacy carousel remains inactive");
        }

        /// <summary>
        /// Public wrapper over <see cref="ResolveEntryArt"/> so the Build HUD carousel can
        /// reuse the SAME data-driven card art (Grok reuse ledger) without a second resolver.
        /// </summary>
        public static Sprite ResolveEntryArtPublic(CatalogEntry e) => ResolveEntryArt(e);

        // ── Render ──────────────────────────────────────────────────────────────

        public void Render()
        {
            FlowTrace.Step("BuildPalette", "palette-build-start");
            EnsureBuilt();
            EnsureVm();
            // WO-1010 D21: the permanent right-edge selector rides every render — counts
            // stay live (a placement/spend can change what a category would list).
            EnsureQuickTabs();
            RefreshQuickTabCounts();
            UpdateTabHighlight();
            if (_stripContent == null)
            {
                FlowTrace.Warn("BuildPalette", "Render aborted: strip content is null (palette never built)");
                return;
            }

            for (int i = _stripContent.childCount - 1; i >= 0; i--)
                Destroy(_stripContent.GetChild(i).gameObject);
            UpdateBalance();

            // MVVM Silo C: the candidate gather + unlock filter + affordability projection
            // now live in the VM. The View renders _vm.Cards (each a StructureCardVM). The
            // catalog-count trace is emitted by the VM on (re)build.
            var cards = _vm.Cards;

            // WO-1167 grouping, rendered as WO-1172 OPTION B (owner pick 2026-08-24): the VM
            // projects Sections (authored order + trailing Other) and the View surfaces them
            // as SEGMENTED FILTER CHIPS in the band above the tray — All / <label> / Other.
            // "All" is the DEFAULT, always (nothing hides behind a tap by default), and the
            // strip renders either every card (All) or exactly one section's cards, in their
            // existing WO-963 order — same objects, same BuildCard path, so arming/locking/
            // affordability are untouched. Sections empty = ungrouped verb = no chips, the
            // flat strip exactly as before (Defense / Walls / legacy verbs).
            // Colourblind-safe: the active chip carries a gilt UNDERLINE (position/shape, the
            // quick-tab grammar) and every chip carries its label + live count in words.
            var sections = _vm.Sections;
            Guard.Try("BuildPalette", "rebuild filter chips", () => RebuildChips(sections));
            IReadOnlyList<StructureCardVM> toRender = cards;
            if (sections != null && sections.Count > 0 && !string.IsNullOrEmpty(_activeFilterLabel))
            {
                PaletteSectionVM active = null;
                for (int i = 0; i < sections.Count; i++)
                    if (sections[i] != null && string.Equals(sections[i].Label, _activeFilterLabel,
                            StringComparison.OrdinalIgnoreCase)) { active = sections[i]; break; }
                if (active != null)
                {
                    toRender = active.Cards;
                }
                else
                {
                    // The filtered group vanished under us (data changed / verb re-pointed with
                    // a same-named stale label). Falling back to All is the only state that can
                    // never hide a building — and it is said out loud, never silent.
                    FlowTrace.Warn("BuildPalette",
                        $"active chip filter '{_activeFilterLabel}' matches no section -- resetting to All");
                    _activeFilterLabel = null;
                }
            }

            // §12: guard EACH card build so one bad entry (missing field / kit quirk) is
            // logged + skipped instead of blanking the whole palette — the WebGL "shows
            // nothing, no error" silent-failure class becomes a logged line.
            var built = Guard.TryEach("BuildPalette", "build card", toRender,
                c => BuildCard(c));
            FlowTrace.Step("BuildPalette",
                $"rows-added: built={built.built} failed={built.failed} " +
                $"filter={(_activeFilterLabel ?? "All")} sections={(sections != null ? sections.Count : 0)} " +
                "(WO-1172 Option B chips)");

            // WO-1010 D13 — BAND COVERAGE. The defect was tab-specific (Defenses, not Town) and
            // nothing in the card code is tab-specific, so the variable is how much of the tray
            // band the cards actually cover. This line states it in numbers: card width x count
            // + layout spacing/padding against the tray's own resolved width. Anything well under
            // 1.0 is bare tray band, which is the surface that used to be see-through onto the
            // 3D town. Keep it: it is the one number that distinguishes "the shop is drawn wrong"
            // from "the shop is drawn right and there is simply not much in this category".
            var trayRt2 = _trayGo != null ? _trayGo.transform as RectTransform : null;
            float trayW = trayRt2 != null ? trayRt2.rect.width : 0f;
            // (WO-1172 Option B: the strip is cards-only again — the Option A header plates
            // left the tray, so the coverage arithmetic is back to the plain card run.)
            float contentW = built.built * CardWidthPx + Mathf.Max(0, built.built - 1) * 10f + 24f;
            FlowTrace.Step("BuildPalette",
                $"band-coverage: cards={built.built} contentPx={contentW:F0} trayPx={trayW:F0} " +
                $"cover={(trayW > 0f ? contentW / trayW : 0f):F2} (tray fill is OPAQUE -- D13)");

            if (built.built == 0)
            {
                var none = MakeText(_stripContent, toRender.Count == 0
                        ? "No buildables registered."
                        : "Buildables failed to load.",
                    14, ElarionUi.Parchment, FontStyles.Italic, TextAlignmentOptions.Left,
                    Vector2.zero, Vector2.one);
                var lrt = none.GetComponent<RectTransform>();
                lrt.sizeDelta = new Vector2(360f, 0f);
                none.gameObject.AddComponent<LayoutElement>().preferredWidth = 360f;
            }
        }

        /// <summary>
        /// WO-1172 Option B — rebuild the filter chip row from the VM's sections. Chips are
        /// DATA: "All" (the one fixed UI word) + one chip per NON-EMPTY section, in section
        /// order, each captioned "&lt;label&gt; (&lt;count&gt;)". An empty section grows no chip
        /// (a chip that filters to nothing is a dead end — the mockup brief's own rule), and a
        /// verb with no sections grows no chips at all (the band keeps only the crystals
        /// read-out — the flat-strip verbs). Rebuilt every Render because the sections are
        /// data and change with the verb/wallet. Colourblind-safe: the ACTIVE chip carries the
        /// gilt underline (position/shape, the quick-tab grammar) + bold gilt text; counts and
        /// labels are words, never colour.
        /// </summary>
        private void RebuildChips(IReadOnlyList<PaletteSectionVM> sections)
        {
            if (_chipHost == null) return;
            for (int i = _chipHost.childCount - 1; i >= 0; i--)
                Destroy(_chipHost.GetChild(i).gameObject);

            bool grouped = sections != null && sections.Count > 0;
            if (!grouped)
            {
                if (!string.IsNullOrEmpty(_activeFilterLabel))
                {
                    FlowTrace.Step("BuildPalette",
                        "chips: verb has no sections -- clearing stale filter '" + _activeFilterLabel + "'");
                    _activeFilterLabel = null;
                }
                return;
            }

            // ── WO-1186: MEASURE THE WHOLE RUN BEFORE BUILDING ANY OF IT ───────────────
            // The chip count is DATA (one per authored group, plus All), so the run's natural
            // width is not knowable at authoring time -- which is precisely how it overflowed
            // its band and landed on the crystal count. Pass 1 collects the captions and their
            // natural widths; pass 2 fits that run to the band; only then does pass 3 build.
            int total = _vm != null && _vm.Cards != null ? _vm.Cards.Count : 0;
            var words   = new List<string>();
            var labels  = new List<string>();
            var actives = new List<bool>();
            var widths  = new List<float>();

            words.Add(ChipWords("All", total)); labels.Add(null);
            actives.Add(string.IsNullOrEmpty(_activeFilterLabel));
            widths.Add(NaturalChipWidth(words[0]));
            for (int i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                if (s == null || s.Cards == null || s.Cards.Count == 0) continue;
                string w = ChipWords(s.Label, s.Cards.Count);
                words.Add(w); labels.Add(s.Label);
                actives.Add(string.Equals(s.Label, _activeFilterLabel, StringComparison.OrdinalIgnoreCase));
                widths.Add(NaturalChipWidth(w));
            }

            int n = widths.Count;
            float bandPx    = DockWidthPx * ChipBandRightFrac;
            float usablePx  = bandPx - ChipPadLeftPx - ChipPadRightPx - ChipGapPx * Mathf.Max(0, n - 1);
            float naturalPx = 0f;
            for (int i = 0; i < n; i++) naturalPx += widths[i];

            float fittedPx = naturalPx;
            if (naturalPx > usablePx && naturalPx > 0.01f)
            {
                // Scale the run down toward the touch floor. MinTouchPx is the hard floor: a
                // filter chip is a CONTROL, so it may never be authored below it (the same rule
                // that sized the band at 112px in the first place).
                float scale = usablePx / naturalPx;
                fittedPx = 0f;
                for (int i = 0; i < n; i++)
                {
                    widths[i] = Mathf.Max(ElarionUiKit.MinTouchPx, widths[i] * scale);
                    fittedPx += widths[i];
                }
            }

            float runPx = ChipPadLeftPx + ChipPadRightPx + ChipGapPx * Mathf.Max(0, n - 1) + fittedPx;
            if (runPx > bandPx + 0.5f)
            {
                // Cannot fit even at the floor -- the clip is holding it, which means the tail
                // chips are unreachable. Said OUT LOUD (§12: no silent failure); the fix is a
                // scrolling chip rail, which is WO-1167's behaviour scope, not this ticket's.
                FlowTrace.Warn("BuildPalette",
                    $"WO-1186 chip run does NOT fit: chips={n} runPx={runPx:F0} bandPx={bandPx:F0} " +
                    "-- clipped at the band edge (contained, but the tail chips are unreachable)");
            }

            for (int i = 0; i < n; i++)
                BuildChip(words[i], actives[i], labels[i], widths[i]);

            FlowTrace.Step("BuildPalette",
                $"chips rebuilt: sections={sections.Count} chips={n} active={(_activeFilterLabel ?? "All")} " +
                $"naturalPx={naturalPx:F0} fittedPx={fittedPx:F0} runPx={runPx:F0} bandPx={bandPx:F0} " +
                "(WO-1172 Option B; WO-1186 fit-to-band -- All is the default, nothing hidden behind a tap)");
        }

        /// <summary>The width one chip WANTS, before the WO-1186 fit pass. Width follows the DATA
        /// label between the clamps; the label autosizes down inside it, so a long authored word
        /// shrinks rather than truncating (the quick-tab lesson).</summary>
        private static float NaturalChipWidth(string words)
        {
            int len = words != null ? words.Length : 1;
            return Mathf.Clamp(90f + len * 13f, ChipMinWidthPx, ChipMaxWidthPx);
        }

        /// <summary>The chip's caption string. ONE source, so the width math and the label can
        /// never measure different text.</summary>
        private static string ChipWords(string caption, int count)
        {
            return (string.IsNullOrEmpty(caption) ? "?" : caption) + " (" + count + ")";
        }

        /// <summary>One filter chip. <paramref name="filterLabel"/> null = the All chip.
        /// <paramref name="widthPx"/> is the WO-1186 FITTED width -- the caller measures the whole
        /// run against the band first, because one chip cannot know whether the run overflows.</summary>
        private void BuildChip(string words, bool isActive, string filterLabel, float widthPx)
        {
            // Built like a card, NOT via AddImage: AddImage stretches the child's anchors
            // across the parent, and a stretched-anchor child under a HorizontalLayoutGroup
            // ignores the layout's slotting — the first capture showed every chip overprinted
            // at one spot. Default point anchors + an explicit width are what the layout
            // needs (the exact recipe the card path uses).
            var chipGo = new GameObject("Chip_" + (filterLabel ?? "All"),
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            chipGo.transform.SetParent(_chipHost, false);
            // WO-1186: the width arrives FITTED. It used to be computed here from the label
            // alone, with no knowledge of the band -- which is why six chips ran 143px past
            // their host and onto the crystal read-out. The label still autosizes down inside
            // whatever width it is given, so a shrunken chip shrinks its word rather than
            // truncating it (the quick-tab lesson).
            float w = Mathf.Max(ElarionUiKit.MinTouchPx, widthPx);
            var chipRt2 = chipGo.GetComponent<RectTransform>();
            chipRt2.sizeDelta = new Vector2(w, 0f);
            chipGo.GetComponent<LayoutElement>().preferredWidth = w;

            var img = chipGo.GetComponent<Image>();
            var plate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotAction);
            if (plate != null)
            {
                img.sprite = plate;
                img.type = Image.Type.Sliced;
                img.fillCenter = true;
                img.color = Color.white;
            }
            else
            {
                img.color = ElarionUiKit.ObsidianFill;
            }
            img.raycastTarget = true;
            var btn = chipGo.GetComponent<Button>();
            btn.targetGraphic = img;
            string captured = filterLabel;
            btn.onClick.AddListener(() =>
            {
                FlowTrace.Step("BuildPalette",
                    "chip tapped filter=" + (captured ?? "All") + " (was " + (_activeFilterLabel ?? "All") + ")");
                _activeFilterLabel = captured;
                Render();
            });

            var label = MakeText(chipGo.transform, words, 17,
                isActive ? ElarionUi.Gilt : ElarionUi.Parchment, FontStyles.Bold,
                TextAlignmentOptions.Center, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f));
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = 19f;

            // Active tell — the gilt underline, same grammar as the quick tabs. Position +
            // shape carry the state; the gilt text is the redundant second cue, never the only one.
            var underline = new GameObject("ActiveUnderline", typeof(RectTransform), typeof(Image));
            underline.transform.SetParent(chipGo.transform, false);
            var urt = (RectTransform)underline.transform;
            urt.anchorMin = new Vector2(0.10f, 0f);
            urt.anchorMax = new Vector2(0.90f, 0f);
            urt.pivot = new Vector2(0.5f, 0f);
            urt.sizeDelta = new Vector2(0f, 4f);
            urt.anchoredPosition = new Vector2(0f, 6f);
            var uimg = underline.GetComponent<Image>();
            uimg.color = ElarionUi.Gilt;
            uimg.raycastTarget = false;
            underline.SetActive(isActive);
        }

        private void BuildCard(StructureCardVM card)
        {
            // MVVM Silo C: the freebie / effective-cost / affordability projection is the VM's
            // (StructureCardVM), computed off the SAME BuildModeController.EffectiveCostFor seam
            // the validator/commit use — so a live freebie is a zero cost = the card never greys
            // out on a first build. The View only paints it; the CatalogEntry (card.Entry) is
            // used ONLY to raise the existing arm events + resolve card art.
            var e = card.Entry;
            bool freebie = card.Freebie;
            DeNelle.Core.Catalog.ResourceCost cost = card.EffectiveCost;
            bool affordable = card.Affordable;
            // BM-2 (WO-746): a singleton row whose one copy is already placed renders as a
            // non-armable "Built" card (desaturated + a Built chip, no cost) instead of a
            // buyable that can only fail at arm time. Presentation-only — the query is the
            // quiet twin of the WO-707 arm/commit gate (BuildModeController.IsSingletonBuilt);
            // enforcement semantics are unchanged. Non-singleton rows always compute false.
            bool built = BuildModeController.IsSingletonBuilt(e);
            // WO-1013: a visible-locked card (build-categories 'visibleLockedIds', unlock flag
            // still down) renders with its NORMAL cost + the lock reason IN WORDS, and can
            // never arm. Words + dimmed shape carry the state, never colour alone.
            bool locked = card.Locked;
            bool armed = !built && !locked && card.Id == _armedId;

            // Slot-plate card: the Blink "slot_action" plate as the face (Obsidian fill
            // fallback when the mirrored art is absent), a Button over the whole plate.
            var cardGo = new GameObject("Card_" + e.id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            cardGo.transform.SetParent(_stripContent, false);
            var rt = cardGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CardWidthPx, 0f);
            cardGo.GetComponent<LayoutElement>().preferredWidth = CardWidthPx;

            // BM-3 (WO-746): register this card under a STABLE tutorial-spotlight id
            // ("build.card.<entryId>") every Render(), so a step can anchor its glow to the
            // exact card it asks the player to build. Re-registering on each rebuild re-arms
            // the registry (idempotent), and the destroyed old RectTransform is dropped by
            // TutorialHighlightRegistry.Resolve's fake-null guard. UiSpotlight follows the
            // card's liveness (hides while the tray is collapsed/inactive, re-acquires here).
            TutorialHighlightRegistry.Register("build.card." + e.id, rt);
            FlowTrace.Step("Build", $"card-register id=build.card.{e.id} entryId={e.id}");

            var img = cardGo.GetComponent<Image>();
            var plate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotAction);
            if (plate != null)
            {
                img.sprite = plate;
                img.type = Image.Type.Sliced;
                img.fillCenter = true;
                // WO (owner felt-test 2026-07-16, said twice): the armed card used to
                // FLOOD the whole plate gilt — on the sliced SlotAction plate that gold
                // wash read as "a big yellow circle drawn around the card." Removed. The
                // plate now keeps its own obsidian face whether armed or not; the armed
                // cue is a GLOW on the ICON (built below in BuildCard), so the selection
                // reads as "this item is glowing," not "a ring is around it."
                img.color = Color.white;
            }
            else
            {
                img.color = ElarionUiKit.ObsidianFill;
            }

            var btn = cardGo.GetComponent<Button>();
            btn.targetGraphic = img;
            // BM-2: a Built singleton stays TAPPABLE (so the tap can explain via the toast)
            // but never arms; an unaffordable non-built card greys out + is non-interactable.
            // The Button is kept for its disabled-tint + press-transition visuals ONLY — the
            // actual tap is delivered by CardTapGuard below (see WO note), so no onClick
            // listener is attached (that avoids any desktop double-fire with the guard).
            btn.interactable = !locked && (built || affordable);

            // ── Touch-web tap-vs-scroll guard (WO: build carousel tap dead on mobile) ──
            // The card Button is a grandchild of the horizontal ScrollRect (Scroll ->
            // Content -> Card_*). On touch WebGL a few-px finger drift makes the ScrollRect
            // claim the gesture as a DRAG, which flips the pointer's eligibleForClick off and
            // CANCELS the Button's OnPointerClick — so OnEntrySelected -> Arm -> Collapse never
            // fired (worked with a dev mouse, dead on a phone). CardTapGuard listens on
            // IPointerDown/IPointerUp (which still fire even after the ScrollRect eats the drag
            // stream): it records the pointer-down screen position and treats pointer-up as a
            // CLICK only when travel stayed under a small scaled threshold (~a few % of screen),
            // otherwise it was a scroll and it does nothing (the ScrollRect keeps the drag).
            // Platform-agnostic — the same travel guard delivers the tap on desktop and touch,
            // so no #if UNITY_WEBGL divergence. Routes through the SAME select path the old
            // onClick used (_armedId + OnEntrySelected + Render), so Arm -> Collapse is unchanged.
            var tapId = e.id;
            var tapEntry = e;
            bool tapBuilt = built;
            bool tapAffordable = affordable;
            bool tapLocked = locked;
            cardGo.AddComponent<CardTapGuard>().Init(() =>
            {
                FlowTrace.Step("BuildPalette", $"card onClick FIRED id={tapEntry.id}");
                // WO-1013: a visible-locked card never arms and never opens the preview --
                // the tap is inert; the reason words on the card explain the state (no toast,
                // no announcement chrome per WO-1013 sec 3).
                if (tapLocked)
                {
                    FlowTrace.Step("BuildPalette",
                        $"palette: tapped LOCKED card '{tapId}' -- arm refused (visible-lock, WO-1013)");
                    return;
                }
                // BM-2 (WO-746): the singleton's one copy is already placed — arming is
                // refused; the tap surfaces the SAME "Already built - your town has one" toast
                // the WO-707 arm/commit gate uses, so the card stays discoverable but reads as
                // not-buyable. (Enforcement semantics unchanged — presentation + this tap only.)
                if (tapBuilt)
                {
                    FlowTrace.Step("Build", $"palette: tapped BUILT singleton card '{tapId}' — arm refused, Singleton toast (WO-746 BM-2).");
                    BuildFeedbackToast.Show(BuildRejectReason.Singleton);
                    return;
                }
                // An unaffordable, non-built card was non-interactable under the old Button —
                // preserve that: the tap is inert (the greyed card explains itself visually).
                if (!tapAffordable) return;
                // WO-352 — if a preview subscriber is attached, defer arming: raise
                // OnCardTapped so the controller shows the Structure Info Preview panel
                // (it calls SetArmed on "Place"). Otherwise keep the legacy immediate-arm.
                if (OnCardTapped != null)
                {
                    FlowTrace.Warn("BuildPalette", "card routed to preview (OnCardTapped) - immediate-arm bypassed");
                    OnCardTapped.Invoke(tapEntry);
                    return;
                }
                _armedId = tapId;
                OnEntrySelected?.Invoke(tapEntry);
                Render();   // refresh the armed highlight
            });

            // Built singletons AND unaffordable cards read as dimmed (built a touch stronger so
            // "already placed" is unmistakable); meaning is also carried by the Built chip / the
            // cost word, never colour alone (owner is red/green colourblind).
            if (built) cardGo.AddComponent<CanvasGroup>().alpha = 0.5f;
            else if (locked) cardGo.AddComponent<CanvasGroup>().alpha = 0.55f;   // WO-1013 -- dimmed, reason words carry the meaning
            else if (!affordable) cardGo.AddComponent<CanvasGroup>().alpha = 0.45f;

            // Armed = bright gilt name (the icon glows below; the label now sits on the
            // plate's normal obsidian face, so gilt reads — the old dark Ink assumed a
            // gold-flooded plate that no longer exists).
            var nameLabel = MakeText(cardGo.transform,
                card.DisplayName,
                14, armed ? ElarionUi.Gilt : ElarionUi.Parchment, FontStyles.Bold,
                TextAlignmentOptions.Center, new Vector2(0.06f, 0.70f), new Vector2(0.94f, 0.96f));
            nameLabel.raycastTarget = false;

            // WO-1081: the tile itself must say what the building does; the detail panel is
            // not on the live gesture path. One bounded line cannot displace the cost band.
            string effectText = card.Description ?? string.Empty;
            if (effectText.Length > 48) effectText = effectText.Substring(0, 45) + "...";
            var effectLabel = MakeText(cardGo.transform, effectText, 11,
                ElarionUi.Parchment, FontStyles.Normal, TextAlignmentOptions.Center,
                new Vector2(0.06f, 0.57f), new Vector2(0.94f, 0.70f));
            effectLabel.raycastTarget = false;
            effectLabel.textWrappingMode = TextWrappingModes.NoWrap;
            effectLabel.overflowMode = TextOverflowModes.Ellipsis;

            // ── Art band UNDER the name (owner 2026-07-06) ────────────────────
            // Priority: (a) Resources/Portraits/<key> building portraits (catalog id,
            // then displayName slug — the key comes from the entry's own data, no
            // per-tower switch), (b) the concept-icons.json table via
            // ConceptIconResolver (data decides), (c) a procedural obsidian plate
            // carrying the entry's initial — NEVER a blank band (null-art law).
            // -- Armed GLOW on the ICON (owner felt-test 2026-07-16, said twice) --
            // Replaces the removed gold-flooded plate (the "big yellow circle"). A soft
            // gilt halo is built BEFORE the art band so it renders BEHIND the icon (the
            // light reads as emanating FROM the item), then a gentle emissive pulse
            // (IconGlowPulse) makes the selected item visibly glow. Sprite-first (the
            // kit rounded sprite via AddImage rounded:true) with a flat tinted-quad
            // fallback baked into ApplyRounded — it can NEVER blank if the sprite build
            // failed under WebGL. ASCII only; no Blink runtime refs.
            if (armed)
            {
                FlowTrace.Step("BuildHud", "armed glow: soft gilt icon halo on card id=" + e.id);
                // Inset within the card (owner felt-test 2026-07-17): the glow + its pulse must
                // stay ON this one card. The old 0.02..0.98 halo pulsing to 1.12 scale bled onto
                // the neighbour card, reading as "two cards glowing." Kept comfortably inside.
                var glowGo = ElarionUiKit.AddImage(cardGo.transform, "ArmedIconGlow",
                    new Vector2(0.14f, 0.16f), new Vector2(0.86f, 0.80f),
                    new Color(ElarionUi.Gilt.r, ElarionUi.Gilt.g, ElarionUi.Gilt.b, 0.55f),
                    rounded: true);
                var glowImg = glowGo.GetComponent<Image>();
                if (glowImg != null) glowImg.raycastTarget = false;
                glowGo.AddComponent<IconGlowPulse>();
            }

            // WO-1010 D13 §12 INSTRUMENTATION — resolve the art through the TRACED resolver so
            // every card states, per render, WHICH branch produced its picture. "The card looks
            // wrong" is otherwise unsplittable into art-missing vs art-wrong-shape vs
            // card-never-built; this line makes the next device run answer it in one read
            // instead of another theory. Never strip these (owner ruling).
            var art = ResolveEntryArtTraced(e, out string artBranch);

            // The art band ALWAYS carries its own dark plate, in BOTH branches. Every Defenses
            // portrait in the tree (archer-tower / ballista / arcane-spire) is a
            // TRANSPARENT-BACKGROUND sticker PNG, so with no plate of its own the picture draws
            // straight onto whatever happens to be behind the card — which on any surface where
            // the card plate is absent is the 3D field itself (UI_PLAYBOOK §6). The art Image is
            // now a CHILD of that plate rather than being the plate, so the frame cannot go
            // missing with the sprite.
            var bandGo = new GameObject("Art", typeof(RectTransform), typeof(Image));
            bandGo.transform.SetParent(cardGo.transform, false);
            var brt = (RectTransform)bandGo.transform;
            brt.anchorMin = new Vector2(0.10f, 0.26f);
            brt.anchorMax = new Vector2(0.90f, 0.56f);
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var bandImg = bandGo.GetComponent<Image>();
            bandImg.raycastTarget = false;
            bandImg.color = new Color(0f, 0f, 0f, 0.45f);   // recessed dark well, always
            if (art != null)
            {
                var artGo = new GameObject("ArtImage", typeof(RectTransform), typeof(Image));
                artGo.transform.SetParent(bandGo.transform, false);
                var art_rt = (RectTransform)artGo.transform;
                art_rt.anchorMin = Vector2.zero; art_rt.anchorMax = Vector2.one;
                art_rt.offsetMin = Vector2.zero; art_rt.offsetMax = Vector2.zero;
                var artImg = artGo.GetComponent<Image>();
                artImg.raycastTarget = false;
                artImg.sprite = art;
                // preserveAspect keeps the picture INSIDE the band. It is what stops a tall
                // sticker PNG from being stretched, and it is why a card preview can never draw
                // outside its own 208x86 reference band no matter what the source image is.
                artImg.preserveAspect = true;
                // Armed = the icon reads warm/lit (over its glow halo); a BUILT singleton reads
                // desaturated ("already placed"); rest = plain white.
                artImg.color = built ? new Color(0.62f, 0.62f, 0.62f, 1f)
                    : (armed ? new Color(1f, 0.965f, 0.82f, 1f) : Color.white);
            }
            else
            {
                // (c) fallback: the dark well already painted above + the entry's gilt initial.
                string glyphSource = card.DisplayName;
                string glyph = string.IsNullOrEmpty(glyphSource)
                    ? "?" : glyphSource.Substring(0, 1).ToUpperInvariant();
                MakeText(bandGo.transform, glyph, 30, ElarionUi.Gilt, FontStyles.Bold,
                    TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            }

            // The per-card D13 line: id, the art branch, the sprite's real pixel shape, and
            // whether the obsidian plate resolved. If a future capture ever shows a frameless
            // card again, "plate=False" or "art=GLYPH" names the cause without a second run.
            var artRect = art != null ? art.rect : new Rect();
            FlowTrace.Step("BuildPaletteArt",
                $"card id={e.id} type={e.type} art={artBranch} " +
                $"sprite={(art != null ? art.name : "<none>")} " +
                $"px={(int)artRect.width}x{(int)artRect.height} " +
                $"plate={(plate != null)} built={built} armed={armed} " +
                $"freebie={freebie} affordable={affordable}");
            if (art == null)
                FlowTrace.Warn("BuildPaletteArt",
                    $"card id={e.id} resolved NO art on any branch (portrait id/slug/alias, concept-icons) " +
                    "-- rendering the gilt-initial plate. A card reading as a bare letter is THIS line.");

            if (built)
            {
                // BM-2 (WO-746): a "Built" chip (WORD + a rounded shape plate) replaces the
                // cost — the singleton is placed, so there is nothing to buy. Text + shape carry
                // the meaning, never colour alone (owner is red/green colourblind). ASCII only.
                var chipBack = ElarionUiKit.AddImage(cardGo.transform, "BuiltChip",
                    new Vector2(0.20f, 0.03f), new Vector2(0.80f, 0.22f),
                    ElarionUiKit.ObsidianFill, rounded: true);
                var chipImg = chipBack.GetComponent<Image>();
                if (chipImg != null) chipImg.raycastTarget = false;
                var chipLabel = MakeText(chipBack.transform, "Built", 13, ElarionUi.Gilt,
                    FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
                chipLabel.raycastTarget = false;
            }
            else
            {
                // WO-1010 D20 (owner ruling 2026-08-08, verbatim: "dont show anything on first
                // build just nothing, only afterwards" + "they dont need to know first is free"
                // + "they will see it didnt cost them to place"): while the first-build-free rule
                // applies to a card its price slot shows NOTHING. No "FREE", no "First build
                // FREE" — the player learns the first one is free by not being charged. This
                // supersedes D9's labelling clause; the label used to read "FREE".
                //
                // WO-1010 D9 verification half, WHICH STAYS: UNAFFORDABLE SAYS SO IN A WORD.
                // The unaffordable case once differed from the affordable one ONLY by
                // ElarionUi.Danger vs ElarionUi.Affordable — the cost string was byte-identical.
                // Red-vs-green is precisely the discrimination this project cannot rely on (the
                // owner is red/green colorblind), so an unaffordable card was indistinguishable
                // from an affordable one for the person it matters most to. "NEED" leads so the
                // state is read before the numbers; colour stays a redundant second cue. ASCII only.
                // WO-1013: a visible-locked card shows its NORMAL cost, plainly -- no NEED
                // prefix and no affordable/danger colouring, because affordability is not the
                // state that matters while locked; the lock reason chip (below) carries the
                // state in words. Never "FREE" (D20 holds here too -- freebie is forced off
                // for locked cards at the VM).
                var costParts = CostParts(cost);
                string costText = (locked || !freebie) ? CostFormat.Words(costParts) : string.Empty;
                // An EMPTY price slot is built as nothing at all, not as an empty label — a
                // zero-width TMP object in the band is invisible but still a layout participant,
                // and "shows nothing" should mean nothing is there.
                if (!string.IsNullOrEmpty(costText))
                {
                    ElarionUiKit.CostRow(cardGo.transform, costParts,
                        new Vector2(0.06f, 0.03f), new Vector2(0.94f, 0.24f),
                        locked ? ElarionUi.Parchment : (affordable ? ElarionUi.Affordable : ElarionUi.Danger),
                        !locked && !affordable ? "NEED" : null);
                }
            }

            // ── Targeting tag (towers only) — at-a-glance anti-air read ─────────
            // A compact "Land / Air / Land+Air" caption pinned to the bottom of the art
            // band so the player counters the flying dragon BEFORE tapping into detail
            // (owner 2026-07-08: Ballista = Air only, ground towers = Land only, Wizard/
            // Arcane = Land + Air). Colorblind-safe: meaning is the TEXT, never color
            // alone (owner is red/green colorblind). ASCII-only — WO-683: the old
            // leading shape glyphs rendered as tofu boxes on the shipped TMP font.
            // WO-1013: while locked, the reason chip takes the tag band -- one message at a
            // time, and "Recover the plans" outranks "Land + Air" until the card is live.
            string targetTag = locked ? null : card.TargetingTag;
            if (locked)
            {
                var lockBackGo = new GameObject("LockReason", typeof(RectTransform), typeof(Image));
                lockBackGo.transform.SetParent(bandGo.transform, false);
                var lrt = (RectTransform)lockBackGo.transform;
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 0.34f);
                lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
                var lockImg = lockBackGo.GetComponent<Image>();
                lockImg.color = new Color(0f, 0f, 0f, 0.72f);   // dark backing for legibility over art
                lockImg.raycastTarget = false;
                string reasonWords = string.IsNullOrEmpty(card.LockReason) ? "Locked" : card.LockReason;
                var lockLabel = MakeText(lockBackGo.transform, reasonWords, 12,
                    ElarionUi.Parchment, FontStyles.Bold, TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one);
                lockLabel.raycastTarget = false;
                // The one long phrase must never ellipsize inside the 208px band -- shrink,
                // don't truncate (the quick-tab caption lesson).
                lockLabel.textWrappingMode = TextWrappingModes.NoWrap;
                lockLabel.enableAutoSizing = true;
                lockLabel.fontSizeMin = 9f;
                FlowTrace.Step("BuildPalette",
                    $"card-locked id={e.id} reason='{reasonWords}' costShown='{CostLabel(cost)}' (WO-1013)");
            }
            if (!string.IsNullOrEmpty(targetTag))
            {
                var tagBackGo = new GameObject("TargetTag", typeof(RectTransform), typeof(Image));
                tagBackGo.transform.SetParent(bandGo.transform, false);
                var trt = (RectTransform)tagBackGo.transform;
                trt.anchorMin = new Vector2(0f, 0f);
                trt.anchorMax = new Vector2(1f, 0.30f);
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
                var tagImg = tagBackGo.GetComponent<Image>();
                tagImg.color = new Color(0f, 0f, 0f, 0.62f);   // dark backing for legibility over art
                tagImg.raycastTarget = false;
                var tagLabel = MakeText(tagBackGo.transform, targetTag, 12,
                    ElarionUi.Gilt, FontStyles.Bold, TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one);
                tagLabel.raycastTarget = false;
            }
        }

        // ── Entry art resolution (owner 2026-07-06 image band) ────────────────

        // Session-lifetime cache keyed on the Resources path; nulls are cached too,
        // so a portrait-less entry costs ONE failed lookup, not one per Render
        // (the PortraitCache pattern — DialogueUI/PortraitCache.cs; that class lives
        // in DeNelle.DialogueUI which DeNelle.Village does not reference, so the
        // small load-or-wrap recipe is mirrored here instead of adding a dependency).
        private static readonly Dictionary<string, Sprite> EntryArtCache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve a catalog entry's card art, data-driven off the entry itself:
        /// (a) Resources/Portraits/&lt;id&gt; then Portraits/&lt;displayName-slug&gt;
        /// (the existing building-portrait set), (b) the concept-icons.json table
        /// (id / slug / catalog type token) via ConceptIconResolver. Null when no
        /// art exists — the caller renders the glyph fallback plate, never blank.
        /// </summary>
        /// <summary>
        /// Portrait files whose NAME does not match the catalog id they belong to.
        ///
        /// The three WO-707 stockpile containers were authored as storage_&lt;resource&gt; while the
        /// catalog calls them by building name — so the art shipped, sat in Resources/Portraits,
        /// and the resolver never looked for it. The cards rendered as bare letter glyphs and
        /// read as missing art. They were not missing; the lookup simply had no way to know
        /// "the Lumberyard is the WOOD store". Aliases here rather than renaming the files,
        /// because the storage_&lt;resource&gt; naming is the more meaningful one — it says what the
        /// building holds, which is what a future foundry/silo variant would also want.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> PortraitAliases =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // WO-707 stockpiles — authored by WHAT THEY HOLD, not by building name.
                { "lumberyard", "storage_wood" },   // stores Wood
                { "foundry",    "storage_iron" },   // stores Iron
                { "silo",       "storage_food" },   // stores Grain/food
                // Walls — authored Title_Case by material. Exact case matters: Resources.Load
                // is case-insensitive on Windows but NOT on every platform, so a lowercase key
                // here would resolve on this machine and silently return null on a device.
                { "wall_wood",  "Wooden_Wall" },
                { "wall_stone", "Stone_Wall" },
                // 2026-08-23 (WO-1161): the { "workshop" -> "forge" } alias that used to sit
                // here is RETIRED, and the comment above it that told you to KEEP the pin was
                // retired with it. Both were written when 'workshop' was believed to be the
                // WEAPONS building; the naming pass settled that it is NOT — 'workshop' is the
                // CRAFTING STATION (role crafting_station, displayName "Crafting Station") and
                // 'forge' is the weaponsmith. The alias was therefore hanging the FORGE's
                // picture on the Crafting Station's card: a confidently wrong portrait, which
                // is worse than an obvious gap. With no alias the card falls through to the
                // concept icon until Portraits/crafting-station art is authored — an honest
                // placeholder that shows the gap instead of hiding it behind another building.
                { "mine_crystal",      "Crystal_Mines" },
                { "tower_siege_tower", "Sky_Ballista" },     // id says siege, the building is the anti-air ballista
                { "healing_caravan",   "Healing_Caravan" },  // renamed 2026-08-09 (was fountain_healing)
                // The collector variant IS the Lumber Mill — reuse its existing portrait rather
                // than commissioning a second image of the same building.
                { "collector_lumbermill", "lumbermill" },
                // WO-1163 renamed the Food producer to Quarry, but its shipped visual remains
                // Structures/farm. Keep the portrait tied to the real model across label changes.
                { "collector_farm", "farm" },
            };

        private static Sprite ResolveEntryArt(CatalogEntry e)
            => ResolveEntryArtTraced(e, out _);

        /// <summary>
        /// <see cref="ResolveEntryArt"/> with the WINNING BRANCH reported out (WO-1010 D13,
        /// CLAUDE.md §12). The resolver has four ordered candidates and they fail differently:
        /// a portrait found by id is authored art, a portrait found by DISPLAY-NAME SLUG is art
        /// hanging off a label creative can rename (UI_PLAYBOOK §14 — the exact trap that nearly
        /// deleted the Weaponsmith card), an alias hit is art whose filename disagrees with its
        /// catalog id, a concept-icon is a generic gap-filler, and null is a letter glyph.
        /// A caller that only sees "a Sprite or not" cannot tell those apart in a capture, which
        /// is how "the card looks wrong" stayed a theory. Pure read-out; resolution order and
        /// results are UNCHANGED.
        /// </summary>
        private static Sprite ResolveEntryArtTraced(CatalogEntry e, out string branch)
        {
            branch = "null-entry";
            if (e == null) return null;

            string slug = SlugOf(e.displayName);

            var s = LoadPortrait(e.id);
            if (s != null) { branch = "portrait-id"; return s; }

            s = LoadPortrait(slug);
            if (s != null) { branch = "portrait-slug:" + slug; return s; }

            if (!string.IsNullOrEmpty(e.id) && PortraitAliases.TryGetValue(e.id, out var aliased))
            {
                s = LoadPortrait(aliased);
                if (s != null) { branch = "portrait-alias:" + aliased; return s; }
            }

            s = ConceptIconResolver.ResolveAny(e.id, slug, e.type.ToString());
            branch = s != null ? "concept-icon" : "GLYPH";
            return s;
        }

        /// <summary>"Archer Tower" -> "archer-tower" (the Portraits/ file convention).</summary>
        private static string SlugOf(string name)
            => string.IsNullOrEmpty(name) ? null : name.Trim().ToLowerInvariant().Replace(' ', '-');

        // Load a Portraits/ sprite directly when possible; fall back to wrapping a
        // Default-imported Texture2D in a runtime Sprite (the portraits import as
        // plain textures, so a bare Resources.Load-as-Sprite returns null for them).
        private static Sprite LoadPortrait(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string path = "Portraits/" + key;
            if (EntryArtCache.TryGetValue(path, out var cached)) return cached;

            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                    sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                           new Vector2(0.5f, 0.5f));
            }
            EntryArtCache[path] = sprite;   // cache nulls too — one lookup per miss
            return sprite;
        }

        // ── Cost string formatting (pure presentation; cost/affordability live in the VM) ──

        /// <summary>Compact per-resource cost string for the card (skips zero slots; ASCII only).</summary>
        private static IReadOnlyList<CostPart> CostParts(DeNelle.Core.Catalog.ResourceCost c)
        {
            // WO-1010 D20: a zero cost prints NOTHING, not "Free". The freebie branch in
            // BuildCard already short-circuits, but a genuinely zero-priced catalog row would
            // otherwise sneak the retired word back onto a card through this formatter — which
            // is exactly how a removed label comes back six months later.
            return CostFormat.Parts(new[]
            {
                ("wood", "Wood", c.wood), ("stone", "Stone", c.stone),
                ("iron", "Iron", c.iron), ("crystal", "Crystals", c.crystals)
            });
        }

        private static string CostLabel(DeNelle.Core.Catalog.ResourceCost c) => CostFormat.Words(CostParts(c));

        private void UpdateBalance()
        {
            // WO-697: balance through the ONE kit formatter (compact >= 10k). Crystals come
            // from the VM (IEconomy.Crystals — the single GameState-backed crystal store).
            if (_balanceLabel != null)
                _balanceLabel.text = "Crystals: " + ElarionUi.CompactNumber(_vm != null ? _vm.Crystals : 0);
        }

        // ── Category tabs (WO-1010 D21: the right-edge quick-tab stack) ────────

        /// <summary>Move the gilt underline to the quick-tab matching <see cref="_activeType"/>.
        /// A legacy verb with no tab (Collector/Support) simply lights nothing — never a
        /// throw, never a wrong underline. No-op when the stack was never built.</summary>
        private void UpdateTabHighlight()
        {
            for (int i = 0; i < _quickTabs.Count; i++)
            {
                var t = _quickTabs[i];
                if (t.Underline != null) t.Underline.SetActive(t.Type == _activeType);
            }
        }

        // WO-1010 D1: UpdateOrientButton() is GONE with the button it gated. Its whole job was
        // the dev visibility rule (DevHotkeys || isDebugBuild) AND the armed check for a control
        // that no longer exists; keeping a method that toggles nothing would be dead code that
        // reads as a live rule. History, so nobody re-adds it by accident: F8-30 dev-gated the
        // button after the owner tapped it mid-tutorial and the orient modal click-locked the
        // screen; WO-707 then re-admitted it to Development builds. D1 retires it outright.

        // ── Consistent-size pin (mirrors ElarionUiKit.PinCanonicalCtaSize) ──────
        /// <summary>
        /// Collapse a kit button's fraction-of-parent anchors to a POINT at the anchor
        /// rect's centre and stamp a fixed <paramref name="w"/> x <paramref name="h"/>
        /// pixel box, so the wide dock header can never stretch it into a thin bar.
        /// Height must be >= ElarionUiKit.MinTouchPx so the kit touch-floor guard no-ops.
        /// </summary>
        private static void PinSize(Button button, float w, float h)
        {
            if (button == null) return;
            var rt = button.transform as RectTransform;
            if (rt == null) return;
            Vector2 centre = (rt.anchorMin + rt.anchorMax) * 0.5f;
            rt.anchorMin = centre;
            rt.anchorMax = centre;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }

        // ── uGUI helper (LeaderboardPanel/VillageCraftingPanel shape) ─────────
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

    /// <summary>
    /// Tap-vs-scroll guard for a build-carousel card (WO: mobile card tap dead). The card
    /// Button is a grandchild of a horizontal ScrollRect, which on touch claims a few-px
    /// finger drift as a DRAG and cancels the Button's OnPointerClick — so the card never
    /// armed on a phone (worked with a dev mouse). This component listens on IPointerDown /
    /// IPointerUp — which STILL fire even after the ScrollRect consumes the drag stream — and
    /// treats pointer-up as a CLICK only when the pointer travelled less than a small,
    /// screen-scaled threshold; a larger travel was a scroll and is ignored (the ScrollRect
    /// keeps its drag). Platform-agnostic: the same travel guard delivers a reliable tap on
    /// desktop mouse and touch WebGL alike, so no per-platform branch is needed. Pure input
    /// plumbing; self-contained; ASCII only; null-safe.
    /// </summary>
    internal sealed class CardTapGuard : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private System.Action _onTap;
        private Vector2 _downPos;
        private bool _tracking;
        // A tap may drift this many screen pixels before it is re-classified as a scroll.
        // Scaled to the device: ~2.5% of the smaller screen dimension (WebGL phone DPIs vary
        // widely), floored at 20px so it is never tighter than comfortable finger jitter.
        private float _thresholdPx = 20f;

        /// <summary>Wire the confirmed-tap callback (idempotent per card build).</summary>
        public void Init(System.Action onTap)
        {
            _onTap = onTap;
            float dim = Mathf.Min(Screen.width, Screen.height);
            _thresholdPx = Mathf.Max(20f, dim * 0.025f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null) return;
            _downPos = eventData.position;
            _tracking = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_tracking || eventData == null) return;
            _tracking = false;
            float travel = (eventData.position - _downPos).magnitude;
            if (travel <= _thresholdPx)
                _onTap?.Invoke();
        }
    }

    /// <summary>
    /// Gentle emissive pulse for the armed-card icon glow (owner felt-test 2026-07-16,
    /// "instead of the circle use the VFX that makes the item glow"). Eases the halo's
    /// alpha + scale up and down so the SELECTED item visibly GLOWS, rather than sitting
    /// under a static ring/circle. Pure presentation; self-contained; ASCII only. Uses
    /// unscaled time so the pulse breathes even if Build Mode ever pauses gameplay time.
    /// </summary>
    internal sealed class IconGlowPulse : MonoBehaviour
    {
        private Image _img;
        private RectTransform _rt;
        private float _baseAlpha = 0.55f;

        private void Awake()
        {
            _img = GetComponent<Image>();
            _rt = transform as RectTransform;
            if (_img != null) _baseAlpha = _img.color.a;
        }

        private void Update()
        {
            // 0..1 eased breathing wave (~0.5 Hz).
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
            if (_img != null)
            {
                var c = _img.color;
                c.a = Mathf.Lerp(_baseAlpha * 0.55f, Mathf.Min(1f, _baseAlpha + 0.30f), k);
                _img.color = c;
            }
            if (_rt != null)
            {
                // Gentle breath that stays within the card (owner 2026-07-17): the old 1.12
                // peak overflowed the halo onto the neighbouring card ("two cards glowing").
                float s = Mathf.Lerp(0.96f, 1.05f, k);
                _rt.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
