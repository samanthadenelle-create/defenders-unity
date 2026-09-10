// =============================================================================
// BuildMenu — the village build-menu UI (Week 4 → WO-D kit conversion).
// -----------------------------------------------------------------------------
// WO-D conversion (2026-07-03, coverage matrix row #35): UIDocument/UITK panel
// (+ its code-built ElarionUi fallback) -> ONE code-built uGUI surface on the
// Obsidian master frame (BuildObsidianModal: FrameCore, "hammer" medallion).
// The old UXML path never rendered in player builds (uxml-uidocuments-dont-
// render-in-builds), so this retires BOTH the dead .uxml binding and the
// interim ShowCodeFallbackMenu — the kit modal is now the single menu.
//
// Screens (WO-31 flow preserved):
//   Root         — Build Tower / Upgrade Tower / Repair Wall / Manage Towers /
//                  Build Mode (Obsidian verb grid; Close = the chrome's ONE
//                  shared Close per obsidian-panel-chrome canon)
//   BuildTower   — catalog-tower radio + REAL costs vs the REAL ledger + Build
//                  (WO-131 prepaid spend, WO-861 verified: the spend must SUCCEED)
//   UpgradeTower — placed-tower list + upgrade info (cost-enforced TryUpgrade)
//
// Behavioural contracts preserved:
//   • Open()/Close()/Toggle()/IsOpen — BuildMenuHudBridge (HUD Build button) +
//     OnboardingIntegrator call Open().
//   • BuildingPlaced event — still relayed from TowerPlacementSystem.OnTowerPlaced
//     for menu-initiated placements (WO-T1); OnboardingIntegrator +
//     TutorialSignalAdapters subscribe.
//   • WO-131 single authoritative spend (OnConfirmBuild, prepaid: true).
//   • Routes through the PanelManager modal arbiter ("Build") like every other kit
//     modal — battle-lock may reject an open (revert + stay hidden).
//
// WO-861 (owner Tier 0, 2026-08-02) — TWO live economy defects removed from THIS View:
//   1. THE FAKE WALLET. A private GetMaterialCount(id) returned the literals wood=20 /
//      stone=5. Those literals were shown to the player as their balance, were what the
//      Build button's afford gate compared against, and were never deducted — so every
//      tower priced in wood/stone was FREE. A second hard-coded TowerVariantDef table
//      (4 rows x crystal/wood/stone cost + times + upgrade cost + DPS/HP) was a rival
//      cost authority to the catalog. BOTH DELETED. The screen now lists real catalog
//      Tower rows priced by BuildModeController.CostFor against the real IEconomy ledger,
//      all via BuildMenuVM (the View reads no state and calls no service).
//   2. THE UNVERIFIED SPEND. OnConfirmBuild called BuildModeController.ChargeLedger,
//      which DISCARDS IEconomy.TrySpend's bool, then placed with prepaid:true even when
//      the ledger DECLINED — a live free-tower path. The spend is now
//      BuildMenuVM.TrySpendBuild, whose bool decides whether the placement happens.
//
// 2026-08-04 (owner ruling: "fix the build menu to place the tower picked") — THE PICK NOW
// REACHES THE PLACEMENT. WO-861 left the tower rows real but the placement pinned to the
// const BuildMenuVM.PlacedTowerResourcePath = "Towers/DevTower": picking "Archer Tower"
// charged the Archer Tower's catalog price, printed DevTower's 2s raise time, and raised a
// DevTower. Both reads are now keyed on the selected row's id —
// BuildMenuVM.PlacedTowerDataFor(id) / BuildSecondsFor(id).
//
// WO-878 (2026-08-05) — THE OVERLAP. Every band on this screen was a FRACTION OF THE BODY
// ZONE, and that zone is ~423-430 reference px on a landscape canvas (see the derivation in
// BuildMenuLayout), not the ~780 the panel looks like. So the root verb rows resolved to
// 41 px, "< Back" to 50 px, the Upgrade CTA to 54 px and each info row to 34 px — all far
// under ElarionUiKit.MinTouchPx (112). ClampMinTouch then grew every one of them
// SYMMETRICALLY ABOUT ITS CENTRE after layout, so they ate their neighbours: the five root
// verbs (48.9 px stride, 112 px grown) sliced one another's labels, "< Back" grew through
// the "UPGRADE TOWER" title, and the Upgrade CTA grew up into the cost/preview text.
//
// THE LAYOUT IS NOW A FIXED-REFERENCE-PIXEL BAND LADDER (BuildMenuLayout), the
// LeaderboardPanel / SettingsController precedent:
//   sub-screen:  [nav 112: "< Back" | title] gap [content: scroll well, 112 px rows]
//                gap [action 112: cost line / preview line | primary CTA]
//   root:        [2-column verb grid, 112 px cells]
//   footer zone: [status hint | Crystals readout]   (the frame's designed wallet strip)
// Every band that carries a button is authored AT the touch floor, so ClampMinTouch is a
// provable no-op; every text band is a whole TMP line box and is fit-guarded, so it can
// neither clip nor spill. And every player-facing STRING now comes from BuildMenuVM — the
// View lays out and renders, it computes nothing.
// =============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// The village build-menu controller — a code-built Obsidian kit modal.
    /// Root chooser → Build-Tower (element + costs) / Upgrade-Tower (live list),
    /// plus Repair Wall / Manage Towers / Build Mode verbs. Deducts crystals on
    /// a confirmed build (WO-131 prepaid) and relays <see cref="BuildingPlaced"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildMenu : MonoBehaviour
    {
        [Header("Crystal balance")]
        [Tooltip("Crystal balance used when no economy service is alive (standalone testing only).")]
        [SerializeField, Min(0)] private int _localCrystalBalance = 500;

        // ── Kit modal (lazy-built on first Open) ─────────────────────────────
        private ElarionUiKit.ObsidianModal _modal;
        private Transform _bodyHost;           // frame body drop-zone — screens rebuild here
        private TextMeshProUGUI _statusLabel;  // footer strip — status / placement hints
        private TextMeshProUGUI _balanceLabel; // footer strip — the wallet readout

        private bool _isOpen;

        // Modal arbiter handle (DEF-212): opening closes any other open panel;
        // battle-lock may reject — revert and stay hidden.
        private PanelHandle _panelHandle;

        // MVVM Silo C — the paired VM owns the crystal balance, the placed-tower scan, and
        // the Repair-Wall command (replacing the removed reflection seam). Created fresh on
        // Open, disposed on Close. This View reads only VM data (no state/scene services).
        private BuildMenuVM _vm;

        // ── WO-31: multi-screen build/upgrade flow ───────────────────────────
        private enum MenuScreen { Root, BuildTower, UpgradeTower }
        private MenuScreen _screen = MenuScreen.Root;

        // WO-861: the Build-Tower radio now selects a REAL catalog tower id. The old local
        // TowerElement enum + the hard-coded TowerVariantDef balance table (a SECOND cost
        // authority divergent from the catalog) are DELETED — every number on this screen
        // comes from BuildMenuVM.TowerOptions (CatalogRegistry -> BuildModeController.CostFor).
        private string _selectedTowerId;
        // WO-127: the manage/upgrade screen tracks a LIVE Tower (not a Building) so
        // its level reads correctly and the Upgrade button calls Tower.TryUpgrade().
        private Tower _selectedTowerForUpgrade;

        /// <summary>Raised when a building is successfully placed — carries the new Building + its def.
        /// (Relayed from TowerPlacementSystem's commit for menu-initiated placements; args may be null —
        /// both live subscribers treat them as optional. See WO-T1 note at the relay.)</summary>
        public event Action<Building, BuildingDef> BuildingPlaced;

        /// <summary>True while the menu is open.</summary>
        public bool IsOpen => _isOpen;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Build", Close, () => IsOpen);
        }

        private void OnDisable()
        {
            DisposeVm();              // never leave a stale wallet subscription across a teardown
            UnhookPlacementRelay();   // WO-T1 — never leave a stale relay across a teardown
        }

        private void OnDestroy()
        {
            DisposeVm();
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        /// <summary>Re-render the open menu when the VM changes (crystals: dev grant, wave reward).</summary>
        private void OnResourcesChanged()
        {
            if (_isOpen) Render();
        }

        private void DisposeVm()
        {
            if (_vm != null) { _vm.Changed -= OnResourcesChanged; _vm.Dispose(); _vm = null; }
        }

        // =====================================================================
        //  Open / Close
        // =====================================================================

        /// <summary>
        /// Opens the build menu — lazily builds the kit modal, then shows the Root
        /// chooser. Called by the HUD's "Build" button (BuildMenuHudBridge) and the
        /// onboarding flow.
        /// </summary>
        public void Open()
        {
            EnsureBuilt();
            if (_modal == null || _modal.canvas == null) return;
            _isOpen = true;
            _modal.canvas.SetActive(true);
            // Arbiter (DEF-212): closes any other open panel; battle-lock may reject —
            // revert and stay hidden, never force-show (VillageCraftingPanel pattern).
            if (!PanelManager.NotifyOpened(_panelHandle))
            {
                _isOpen = false;
                _modal.canvas.SetActive(false);
                return;
            }

            // Build the VM (resolves economy + tower list + wall-repair controller) and
            // bind its Changed so the open menu live-refreshes when crystals change (a dev
            // grant, a wave reward). Remove-then-add guards against a double subscription.
            DisposeVm();
            _vm = BuildMenuVM.CreateDefault(Close, _localCrystalBalance);
            _vm.Changed += OnResourcesChanged;

            _screen = MenuScreen.Root;            // always open on the chooser
            _selectedTowerId = null;              // re-defaults to the cheapest catalog row
            _selectedTowerForUpgrade = null;
            _upgradeListScrollPos = 1f;           // WO-795: fresh open starts at the list top
            Disarm();
            Render();
            FlowTrace.Step("UI", "Build menu shown (kit modal, FrameCore)");
        }

        /// <summary>
        /// Closes the build menu. Wired to the chrome's ONE shared Close (and the
        /// modal arbiter's close callback).
        /// </summary>
        public void Close()
        {
            _isOpen = false;
            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(false);
            PanelManager.NotifyClosed(_panelHandle);
            DisposeVm();
        }

        /// <summary>Toggles the menu open/closed.</summary>
        public void Toggle()
        {
            if (_isOpen) Close();
            else Open();
        }

        // =====================================================================
        //  Kit modal construction (lazy — first Open builds)
        // =====================================================================

        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;

            // WO-878: the panel is authored from BuildMenuLayout (0.92 of the canvas height).
            // At the old 0.80 the close-band reservation left a body of only ~357 reference px,
            // which cannot seat a touch-floor nav band, a touch-floor action band and a list.
            _modal = ElarionUiKit.BuildObsidianModal("BuildMenuUI", "Build",
                new Vector2(BuildMenuLayout.ModalXMin, BuildMenuLayout.ModalYMin),
                new Vector2(BuildMenuLayout.ModalXMax, BuildMenuLayout.ModalYMax), Close,
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "hammer");

            var layout = _modal.chrome.layout;
            _bodyHost = layout != null && layout.body != null
                ? (Transform)layout.body
                : _modal.chrome.content.transform;

            // The frame's footer strip is the designed WALLET / ACTION band (FrameLayout doc),
            // and the kit has already re-seated it clear of the shared Close. It carries the two
            // persistent read-outs — the status hint (left) and the balance (right) — side by
            // side, so neither competes with the body ladder for the body's ~423 px.
            var footHost = layout != null && layout.footer != null
                ? (Transform)layout.footer
                : _modal.chrome.content.transform;
            _statusLabel = MakeText(footHost, "", ElarionUi.FontMicro, ElarionUi.ParchmentDim, FontStyles.Italic,
                TextAlignmentOptions.Left, new Vector2(0.02f, 0f), new Vector2(0.60f, 1f));
            ElarionUiKit.FitBlock(_statusLabel, ElarionUi.FontFloorMobile);
            _balanceLabel = MakeText(footHost, "", ElarionUi.FontLabel, ElarionUi.Gilt, FontStyles.Bold,
                TextAlignmentOptions.Right, new Vector2(0.62f, 0f), new Vector2(0.98f, 1f));
            ElarionUiKit.FitSingleLine(_balanceLabel, ElarionUi.FontFloorMobile);

            _modal.canvas.SetActive(false);   // built hidden; Open shows it
        }

        // =====================================================================
        //  Rendering — rebuilds the body drop-zone for the current screen
        // =====================================================================

        /// <summary>
        /// Rebuilds the body for the current <see cref="_screen"/> (WO-31 three-screen
        /// flow). All screens rebuild into the frame's body drop-zone as FIXED-PIXEL
        /// bands (BuildMenuLayout); the wallet readout lives in the footer strip and is
        /// only re-texted, never rebuilt.
        /// </summary>
        public void Render()
        {
            if (_bodyHost == null) return;
            for (int i = _bodyHost.childCount - 1; i >= 0; i--)
                Destroy(_bodyHost.GetChild(i).gameObject);

            // WO-697: balance through the ONE kit formatter (compact >= 10k).
            if (_balanceLabel != null)
                _balanceLabel.text = "Crystals: " + ElarionUi.CompactNumber(CrystalBalance);

            switch (_screen)
            {
                case MenuScreen.BuildTower:   RenderBuildTower();   break;
                case MenuScreen.UpgradeTower: RenderUpgradeTower(); break;
                default:                      RenderRoot();         break;
            }
        }

        // ── Root chooser ─────────────────────────────────────────────────────

        /// <summary>
        /// The top-level chooser: a fixed-pixel Obsidian verb GRID — Build Tower /
        /// Upgrade Tower / Repair Wall / Manage Towers / Build Mode. (No Close cell —
        /// the chrome's ONE shared Close covers it.)
        ///
        /// Two columns, not one: five verbs at the 112 px touch floor need 592 px stacked
        /// and the body is ~423-430 px. The old single column asked 0.115 of the body per
        /// row (41 px), which ClampMinTouch grew to 112 px about each row's centre — a
        /// 63 px overlap per row, which is the label-slicing in the 2026-08-04 capture.
        /// </summary>
        private void RenderRoot()
        {
            AddRootCell(0, "Build Tower", ElarionUiKit.ObsidianButtonColor.Yellow,
                () => { _screen = MenuScreen.BuildTower; Render(); });
            AddRootCell(1, "Upgrade Tower", ElarionUiKit.ObsidianButtonColor.Gray,
                () => { _screen = MenuScreen.UpgradeTower; _selectedTowerForUpgrade = null; _upgradeListScrollPos = 1f; Render(); });
            AddRootCell(2, "Repair Wall", ElarionUiKit.ObsidianButtonColor.Gray, OnRepairWall);
            AddRootCell(3, "Manage Towers", ElarionUiKit.ObsidianButtonColor.Gray, () =>
            {
                Close();
                DeNelle.Village.UI.TowerManagerPanel.Instance?.Show();
            });
            // WO-108 — the CREATE verb: enter the player Build Mode (catalog palette +
            // grid placement + persisted BaseLayout). EnsureExists() self-installs the
            // controller; Enter() freezes waves + shows the palette.
            AddRootCell(4, "Build Mode", ElarionUiKit.ObsidianButtonColor.Gray, () =>
            {
                Close();
                BuildModeController.EnsureExists().Enter();
            });
        }

        /// <summary>One verb cell of the root grid: a FIXED <see cref="BuildMenuLayout.RootCellPx"/>
        /// band (== the touch floor, so the kit's post-layout floor guard is a no-op) at the
        /// cell's row, filled edge to edge by the Obsidian button.</summary>
        private void AddRootCell(int index, string label, ElarionUiKit.ObsidianButtonColor color, Action onClick)
        {
            int row = index / BuildMenuLayout.RootColumns;
            int col = index % BuildMenuLayout.RootColumns;
            float colW = 1f / BuildMenuLayout.RootColumns;
            float xMin = col * colW + BuildMenuLayout.RootCellPadFrac;
            float xMax = (col + 1) * colW - BuildMenuLayout.RootCellPadFrac;
            float topPx = row * (BuildMenuLayout.RootCellPx + BuildMenuLayout.BandGapPx);

            var cell = PxBandFromTop(_bodyHost, "RootCell" + index, xMin, xMax, topPx, BuildMenuLayout.RootCellPx);
            ElarionUiKit.BuildObsidianButton(cell, label,
                ElarionUiKit.ObsidianButtonStyle.Style1, color,
                Vector2.zero, Vector2.one, onClick);
        }

        private void OnRepairWall()
        {
            _vm?.RepairNearestWall();   // typed WallRepairController command (reflection seam removed)
            SetStatus("Restoring the nearest damaged wall section.");
        }

        // ── Shared sub-screen bands ──────────────────────────────────────────

        /// <summary>
        /// The top band of a sub-screen: "&lt; Back" and the screen title SIDE BY SIDE in one
        /// touch-floor band. Both were previously stacked fractions (0.14 of the body = 50 px
        /// for the button, a 0.05 slab for the title), so the button grew 31 px past each edge
        /// and swallowed the title — "UPGRADE TOWER clips the Back corner" in the WO-878 report.
        /// Horizontally disjoint rects in one fixed band cannot do that.
        /// </summary>
        private void AddNavBand(string title)
        {
            var nav = PxBandFromTop(_bodyHost, "NavBand", 0f, 1f, 0f, BuildMenuLayout.NavBandPx);
            ElarionUiKit.BuildObsidianButton(nav, "< Back",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0f, 0f), new Vector2(BuildMenuLayout.BackWidthFrac, 1f),
                () => { _screen = MenuScreen.Root; _selectedTowerForUpgrade = null; Disarm(); Render(); });

            var label = MakeText(nav, title, ElarionUi.FontHead, ElarionUi.Gilt, FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(BuildMenuLayout.TitleLeftFrac, 0f), new Vector2(1f, 1f));
            ElarionUiKit.FitSingleLine(label, ElarionUi.FontFloorMobile);
        }

        /// <summary>The bottom band of a sub-screen: the two preview lines on the left and the
        /// primary CTA on the right. FIXED pixels and disjoint from the content band above by
        /// <see cref="BuildMenuLayout.BandGapPx"/> — the cost/preview text can no longer land on
        /// the button, which is the WO-878 defect.</summary>
        private Transform AddActionBand()
            => PxBandFromBottom(_bodyHost, "ActionBand", 0f, 1f, 0f, BuildMenuLayout.ActionBandPx);

        /// <summary>The two VM-authored preview lines inside the action band. Each is exactly half
        /// the band (56 px — a whole line box at the fonts they render) and fit-guarded, so a long
        /// string wraps-and-truncates inside its own half instead of spilling into the other.</summary>
        /// <summary>
        /// The two preview rows on the left of the action band.
        /// ⛔ WO-1636: EACH ROW IS A TWO-LINE BLOCK, AND THAT IS THE WHOLE POINT OF ITS HEIGHT.
        /// Both strings are SENTENCES the VM assembles (BuildMenuVM.UpgradeCostLineFor /
        /// UpgradeStatLineFor / BuildDetailLineFor) and the longest of them measure ~731 px at the
        /// font floor, against an info lane of 538.3 px at the narrowest captured aspect - so
        /// neither can ever be a single line at any width this band could give it. The measured
        /// proof: chain 17 caught "Lvl 1 to 2:  dmg 23.8 to 46.8,  range 18m to 22m" drawing
        /// 33 of 35 glyphs in a 604.1 x 56.0 px rect, where 56 px is exactly one line box.
        /// The rows are half of BuildMenuLayout.ActionBandPx (160 -> 80 px each), which seats two
        /// line boxes at ElarionUi.FontFloorMobile; see BuildMenuLayout for both bounds.
        /// ⛔ FitBlock, NEVER FitSingleLine. FitSingleLine cannot wrap - it shrinks to the floor
        /// and then ellipsises - so swapping it back re-imposes the one-line cap and makes the
        /// extra 48 px of band dead space. The Truncate that FitBlock arms is still the net: copy
        /// that overruns TWO lines goes missing visibly rather than sub-legibly.
        /// </summary>
        private void AddInfoLines(Transform band, string line1, string line2)
        {
            if (band == null) return;
            var top = MakeText(band, line1 ?? string.Empty, ElarionUi.FontLabel, ElarionUi.Parchment,
                FontStyles.Normal, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(BuildMenuLayout.InfoWidthFrac, 1f));
            ElarionUiKit.FitBlock(top, ElarionUi.FontFloorMobile);

            var bottom = MakeText(band, line2 ?? string.Empty, ElarionUi.FontMicro, ElarionUi.ParchmentDim,
                FontStyles.Normal, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(BuildMenuLayout.InfoWidthFrac, 0.5f));
            ElarionUiKit.FitBlock(bottom, ElarionUi.FontFloorMobile);
        }

        /// <summary>The scrolling content band between the nav and action bands — it absorbs
        /// whatever the two fixed rungs leave over (never less than one row at any landscape
        /// aspect). Rows are fixed-pixel cells inside the KIT scroll zone, so an unbounded list
        /// scrolls instead of truncating (WO-795) and no row can be squeezed under the floor.</summary>
        private ElarionUiKit.ScrollZoneHandle AddContentScroll()
        {
            var band = PxStretchBand(_bodyHost, "ContentBand", 0f, 1f,
                BuildMenuLayout.ContentTopInsetPx, BuildMenuLayout.ContentBottomInsetPx);
            return ElarionUiKit.MakeScrollZone(band, BuildMenuLayout.RowGapPx, 4);
        }

        /// <summary>One fixed-height row cell inside a kit scroll zone. The zone's layout group
        /// runs with childControlHeight OFF (kit note), so the cell keeps exactly this height.</summary>
        private static Transform AddScrollRow(Transform content, string name, float heightPx)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(content, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, heightPx);
            return go.transform;
        }

        // ── Build Tower screen ────────────────────────────────────────────────

        /// <summary>
        /// Catalog-tower radio (scroll well, touch-floor rows) + the selected row's REAL catalog
        /// cost against the REAL on-hand ledger + the real raise time + a Build button that STATES
        /// why it is dead when the live wallet cannot cover it. WO-861: every number here is VM
        /// data; WO-878: every player-facing STRING is too — the View authors none of them.
        /// </summary>
        private void RenderBuildTower()
        {
            var options = _vm != null ? _vm.TowerOptions : null;
            if (options == null || options.Count == 0)
            {
                // No fabricated fallback row: an unbootstrapped catalog is reported, not invented.
                AddNavBand("BUILD TOWER");
                var empty = AddActionBand();
                AddInfoLines(empty, "No tower rows in the catalog - nothing can be priced.", string.Empty);
                return;
            }

            var selection = _vm.TowerOptionFor(_selectedTowerId);
            _selectedTowerId = selection.Id;

            // Content first, chrome after: later siblings paint on top, so the two fixed bands
            // are the last word even if a future edit lets the well grow.
            var well = AddContentScroll();
            foreach (var opt in options)
            {
                bool selected = string.Equals(opt.Id, _selectedTowerId, StringComparison.OrdinalIgnoreCase);
                string captured = opt.Id;
                var row = AddScrollRow(well.content, "TowerOption", BuildMenuLayout.RowPx);
                ElarionUiKit.BuildObsidianButton(row,
                    (selected ? "> " : "") + opt.DisplayName,
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    selected ? ElarionUiKit.ObsidianButtonColor.Yellow
                             : ElarionUiKit.ObsidianButtonColor.Gray,
                    Vector2.zero, Vector2.one,
                    () => { _selectedTowerId = captured; Render(); });
            }

            AddNavBand("BUILD TOWER");
            var band = AddActionBand();
            AddInfoLines(band, _vm.CostSummaryFor(selection.Cost), _vm.BuildDetailLineFor(selection));

            var cost = selection.Cost;
            bool canBuild = _vm.CanAfford(cost);
            // COLOURBLIND LAW: the label carries the state ("Not enough Wood (70)"); the grey
            // face only reinforces it.
            var btn = ElarionUiKit.BuildObsidianButton(band, _vm.BuildCtaLabelFor(selection),
                ElarionUiKit.ObsidianButtonStyle.Style1,
                canBuild ? ElarionUiKit.ObsidianButtonColor.Green
                         : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(BuildMenuLayout.CtaLeftFrac, 0f), new Vector2(1f, 1f),
                () => OnConfirmBuild(selection));
            if (btn != null) btn.interactable = canBuild;
        }

        /// <summary>
        /// Arms the PICKED tower's def for tap-to-place (the placement pipeline is shared)
        /// AFTER the spend has actually landed.
        ///
        /// WO-861 ORDER OF OPERATIONS (owner AC: "insufficient wood -> placement blocked and
        /// resources unchanged"):
        ///   1. re-check affordability on the LIVE ledger (the button state can be stale);
        ///   2. resolve the PICKED row's TowerData FIRST, so a missing asset can never charge
        ///      the player (2026-08-04: this resolve was pinned to Towers/DevTower, so the row
        ///      chosen on the screen decided only the PRICE, never the tower that went up);
        ///   3. spend through BuildMenuVM.TrySpendBuild — which HONOURS IEconomy.TrySpend's
        ///      bool. Previously this called BuildModeController.ChargeLedger (which DISCARDS
        ///      that bool) and then placed with prepaid:true regardless, so a DECLINED spend
        ///      still raised a tower. Now a false return returns here: no placement, no charge.
        /// </summary>
        private void OnConfirmBuild(BuildMenuVM.TowerBuildOption option)
        {
            if (_vm == null || option.IsEmpty)
            {
                SetStatus("No tower selected.", isError: true);
                return;
            }
            var cost = option.Cost;
            if (!_vm.CanAfford(cost))
            {
                SetStatus(_vm.ShortfallFor(cost) + " for the " + option.DisplayName + ".", isError: true);
                return;
            }

            // The PICKED row's own TowerData (2026-08-04 owner ruling). This used to read a
            // const pinned to Towers/DevTower, so every build — Archer, Ballista, Spire —
            // raised a DevTower. Still resolved BEFORE the spend, so a missing asset can
            // never charge the player.
            var data = _vm.PlacedTowerDataFor(option.Id);
            if (data == null)
            {
                SetStatus("Tower definition asset missing for the " + option.DisplayName + ".", isError: true);
                return;
            }

            // THE spend. False => the ledger declined; nothing was deducted and nothing is placed.
            if (!_vm.TrySpendBuild(cost, out string spendFailure))
            {
                SetStatus(spendFailure ?? "The build could not be charged - placement blocked.", isError: true);
                Render();   // re-price against whatever the wallet actually says now
                return;
            }

            if (TowerPlacementSystem.Instance == null)
                new GameObject("TowerPlacementSystem").AddComponent<TowerPlacementSystem>();
            // Pass the already-paid cost so TowerPlacementSystem does NOT charge again.
            // WO-T1 FIX — relay TowerPlacementSystem's commit (PlaceTower ->
            // OnTowerPlaced) so this menu's own BuildingPlaced event fires for
            // placements THIS menu initiated (subscribed by OnboardingIntegrator +
            // TutorialSignalAdapters).
            HookPlacementRelay(TowerPlacementSystem.Instance);
            // ECONOMY FIX 2026-08-04 — hand over the EXACT cost `cost` that TrySpendBuild just
            // deducted (the catalog's multi-axis repo.cost), so a right-click cancel refunds
            // that and only that. It used to receive nothing and refund the TowerData asset's
            // `cost` field as CRYSTALS: pay tower_ground_archer's 70 wood + 40 iron (ZERO
            // crystals), cancel, collect the asset's crystal number instead — an unbounded
            // resource-to-crystal converter, repeatable forever.
            TowerPlacementSystem.Instance.StartPlacing(data, prepaid: true, prepaidCost: cost);
            Close();   // hide the menu so the world click lands the placement
            // Name the tower that is ACTUALLY being raised (data.towerName), not the row that
            // was tapped. They are the same string for every row with an authored TowerData;
            // where they differ, the player is told which tower is going up rather than being
            // promised the one the fallback could not supply.
            SetStatus("Click a clear tile to raise the " +
                      DeNelle.Village.UI.PlacedTowerListVM.PrettifyTowerName(data.towerName) + ".");
        }

        // ── WO-T1: BuildingPlaced relay (menu-initiated placements) ───────────
        // One-shot: armed per OnConfirmBuild, fires on the next committed placement,
        // then unhooks (TowerPlacementSystem cancels arming after each placement too).
        private TowerPlacementSystem _relayHooked;

        private void HookPlacementRelay(TowerPlacementSystem tps)
        {
            if (tps == null) return;
            UnhookPlacementRelay();
            _relayHooked = tps;
            tps.OnTowerPlaced += OnMenuInitiatedTowerPlaced;
        }

        private void UnhookPlacementRelay()
        {
            if (_relayHooked != null)
            {
                _relayHooked.OnTowerPlaced -= OnMenuInitiatedTowerPlaced;
                _relayHooked = null;
            }
        }

        private void OnMenuInitiatedTowerPlaced(DeNelle.Core.Data.TowerData _)
        {
            UnhookPlacementRelay();
            // The tower body is raised by TowerConstructionQueue over buildTime (DEF-76),
            // so no live Building/def exists at commit — both live subscribers
            // (OnboardingIntegrator.OnBuildingPlaced ignores its args; tutorial adapters
            // only need the fact) treat the args as optional.
            BuildingPlaced?.Invoke(null, null);
        }

        // ── Upgrade Tower screen ──────────────────────────────────────────────

        // Scroll position persisted across Render() rebuilds — selecting a row
        // re-renders the whole body, which would otherwise snap the list to the top.
        private float _upgradeListScrollPos = 1f;   // 1 = top (uGUI normalized)

        /// <summary>
        /// WO-127: lists every LIVE placed <see cref="Tower"/> (not Building), so the
        /// row level matches the tower's actual <c>CurrentLevel</c>. Selecting one
        /// shows its upgrade info; the Upgrade button routes through the single
        /// cost-enforced <see cref="Tower.TryUpgrade"/>. WO-795: the list lives in a
        /// scroll well — an unbounded tower list scrolls instead of truncating. WO-878:
        /// the well is a fixed-pixel band between the nav and action bands, and its rows
        /// sit AT the touch floor (they were 96 px, which the floor guard grew by 8 px on
        /// each side — exactly consuming the inter-row spacing).
        /// </summary>
        private void RenderUpgradeTower()
        {
            // WO-127 root cause: enumerate LIVE Tower components (the type whose
            // _currentLevel actually upgrades), not the separate Building type whose
            // serialized Level never mutates. MVVM Silo C: the placed-tower scan lives in
            // the VM's tower list (the sanctioned resolution site) — not in this View.
            _vm.Towers.Refresh();
            var towers = _vm.Towers.Towers;

            // Drop a selection that no longer exists in the scene.
            bool stillPresent = false;
            foreach (var tw in towers)
                if (ReferenceEquals(tw, _selectedTowerForUpgrade)) { stillPresent = true; break; }
            if (_selectedTowerForUpgrade == null || !stillPresent)
                _selectedTowerForUpgrade = null;

            float restoreScrollPos = _upgradeListScrollPos;
            var well = AddContentScroll();
            well.scroll.onValueChanged.AddListener(_ => _upgradeListScrollPos = well.scroll.verticalNormalizedPosition);

            bool any = false;
            foreach (var t in towers)
            {
                if (t == null) continue;
                bool selected = ReferenceEquals(t, _selectedTowerForUpgrade);
                // WO-127: print the LIVE level (1..MaxLevel) so it always matches
                // TowerManagerPanel's level for the same tower.
                //
                // 2026-08-04: the row used to label itself with t.name -- the GAMEOBJECT name.
                // That is a scene-graph identifier, not a display string (the construction queue
                // spells it "Tower_<towerName>", a prefab instance carries "(Clone)", an editor
                // stub carries whatever it was built with), so raw ids such as "Stone4" reached
                // the player. The VM resolves the AUTHORED tower name instead, and reports whether
                // the tower has finished being raised so an unbuilt one does not claim "Lvl 1".
                string label = DeNelle.Village.UI.PlacedTowerListVM.FormatMenuRow(
                    _vm.Towers.DisplayNameFor(t), _vm.Towers.LevelOf(t), selected, _vm.Towers.IsBuilt(t));
                Tower captured = t;
                var row = AddScrollRow(well.content, "TowerRow", BuildMenuLayout.RowPx);
                ElarionUiKit.BuildObsidianButton(row, label,
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    selected ? ElarionUiKit.ObsidianButtonColor.Yellow
                             : ElarionUiKit.ObsidianButtonColor.Gray,
                    Vector2.zero, Vector2.one,
                    () => { _selectedTowerForUpgrade = captured; Render(); });
                any = true;
            }
            if (!any)
            {
                var emptyRow = AddScrollRow(well.content, "EmptyRow", BuildMenuLayout.RowPx);
                var note = MakeText(emptyRow, "No towers placed yet.", ElarionUi.FontLabel,
                    ElarionUi.ParchmentDim, FontStyles.Italic, TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one);
                ElarionUiKit.FitSingleLine(note, ElarionUi.FontFloorMobile);
            }

            // Restore the scroll position across a selection re-render (layout must be
            // computed first, or the normalized set is a no-op on a zero-height content).
            Canvas.ForceUpdateCanvases();
            well.scroll.verticalNormalizedPosition = restoreScrollPos;

            AddNavBand("UPGRADE TOWER");
            BuildUpgradeActionBand(_selectedTowerForUpgrade);
        }

        /// <summary>
        /// WO-127: upgrade info + a REAL upgrade action for the selected live tower, laid into
        /// the one fixed action band (preview left, CTA right — they cannot overlap).
        /// The Upgrade button calls the single cost-enforced <see cref="Tower.TryUpgrade"/>
        /// (never the free Upgrade — tower-upgrade consolidation, owner 2026-06-27; the
        /// canonical surface remains the proximity HUD context button) and appears ONLY when
        /// that transaction could actually succeed — never at <see cref="Tower.MaxLevel"/>, on a
        /// tower still under construction, or on a level with no authored cost row.
        /// </summary>
        private void BuildUpgradeActionBand(Tower t)
        {
            var band = AddActionBand();
            if (t == null || _vm == null)
            {
                AddInfoLines(band, "Pick a tower to see what its next level costs.", string.Empty);
                return;
            }

            // WO-861: the REAL price Tower.TryUpgrade charges — the next level's upgradeCost on
            // wood AND iron AND crystals — against the REAL on-hand ledger. WO-878: the five-way
            // availability switch that used to assemble those lines HERE now lives in the VM
            // (UpgradeCostLineFor / UpgradeStatLineFor) — this View only places them.
            var quote = _vm.UpgradeQuoteFor(t);
            AddInfoLines(band, _vm.UpgradeCostLineFor(quote), _vm.UpgradeStatLineFor(quote));

            // No CTA when tapping it could not possibly succeed — Tower.TryUpgrade refuses an
            // unbuilt tower (Uninitialized), a maxed one (Maxed) and an un-authored level
            // (UnknownCost), so offering the button there is an invitation to a dead end.
            if (!quote.HasNextLevel) return;

            // COLOURBLIND LAW: the button STATES why it is dead ("Not enough Iron (100)"), so the
            // green/grey face is reinforcement, never the only signal.
            bool canUpgrade = quote.CanUpgradeNow;
            var cta = ElarionUiKit.BuildObsidianButton(band, _vm.UpgradeCtaLabelFor(quote),
                ElarionUiKit.ObsidianButtonStyle.Style1,
                canUpgrade ? ElarionUiKit.ObsidianButtonColor.Green
                           : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(BuildMenuLayout.CtaLeftFrac, 0f), new Vector2(1f, 1f), () =>
                {
                    var res = _selectedTowerForUpgrade != null
                        ? _selectedTowerForUpgrade.TryUpgrade()
                        : Tower.UpgradeResult.Uninitialized;
                    bool ok = res == Tower.UpgradeResult.Success;
                    string msg = res switch
                    {
                        Tower.UpgradeResult.Success     => "Upgraded to Lvl " + _vm.Towers.LevelOf(_selectedTowerForUpgrade) + ".",
                        Tower.UpgradeResult.Maxed       => "This tower is already at its highest level.",
                        Tower.UpgradeResult.CantAfford  => "Not enough resources to upgrade.",
                        Tower.UpgradeResult.UnknownCost => "This tower cannot be upgraded any further.",
                        Tower.UpgradeResult.NoEconomy   => "Your stores are unavailable right now - try again in a moment.",
                        _                               => "The crew is still raising this tower.",
                    };
                    SetStatus(msg, isError: !ok);
                    Render();
                });
            if (cta != null) cta.interactable = canUpgrade;
        }

        // ── WO-31 helpers ─────────────────────────────────────────────────────
        //
        // DELETED (WO-861 — do not reintroduce, BuildMenuRealEconomyRegression lints for it):
        //   VariantFor / CanAfford(TowerVariantDef) / GetMaterialCount(string)
        // GetMaterialCount was a literal-returning fake wallet ("wood" -> 20, "stone" -> 5)
        // that the player SAW as their balance and the Build button gated on, and that was
        // never deducted. Balances, affordability and the spend all live in BuildMenuVM now,
        // reading IEconomy — the one GameState-backed ledger.
        //
        // DELETED (WO-878): FormatTime — the duration string is VM output
        // (BuildMenuVM.FormatDuration), like every other player-facing line on this screen.

        private void SetStatus(string message, bool isError = false)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = message;
            _statusLabel.color = isError ? ElarionUi.Danger : ElarionUi.ParchmentDim;
        }

        private void Disarm()
        {
            SetStatus("Pick an action, then tap a clear tile to raise a tower.");
        }

        // =====================================================================
        //  Crystal balance — GameState-backed or local (standalone testing)
        // =====================================================================

        /// <summary>The current crystal balance the menu spends from. MVVM Silo C: sourced from
        /// the VM (IEconomy.Crystals — the SAME single crystal store the HUD + dev grants share),
        /// falling back to the standalone-test local value when the menu is closed / no service.
        /// This View reads only VM data.</summary>
        public int CrystalBalance => _vm != null ? _vm.Crystals : _localCrystalBalance;

        // ── FIXED-REFERENCE-PIXEL BANDS (WO-878) ──────────────────────────────
        // The anti-overlap primitive, verbatim from the LeaderboardPanel px-ladder precedent.
        // A band's HEIGHT is set in canvas-local units via offsetMin/offsetMax; on a
        // CanvasScaler'd canvas those units ARE reference px — the same unit
        // ElarionUiKit.MinTouchPx (112) is expressed in. So a band authored at the floor is
        // provably at the floor on every screen size, ClampMinTouch has nothing to grow, and
        // no band can be pushed into its neighbour. x stays fractional: horizontal room is
        // never the constraint on this panel (the body is ~1450 ref px wide).

        /// <summary>Band pinned to the TOP of <paramref name="parent"/>: <paramref name="topPx"/>
        /// down from the top edge, <paramref name="heightPx"/> tall (reference px).</summary>
        private static Transform PxBandFromTop(Transform parent, string name,
            float xMin, float xMax, float topPx, float heightPx)
        {
            var rt = NewBand(parent, name);
            rt.anchorMin = new Vector2(xMin, 1f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMax = new Vector2(0f, -topPx);
            rt.offsetMin = new Vector2(0f, -(topPx + heightPx));
            return rt.transform;
        }

        /// <summary>Band pinned to the BOTTOM of <paramref name="parent"/>: <paramref name="bottomPx"/>
        /// up from the bottom edge, <paramref name="heightPx"/> tall (reference px).</summary>
        private static Transform PxBandFromBottom(Transform parent, string name,
            float xMin, float xMax, float bottomPx, float heightPx)
        {
            var rt = NewBand(parent, name);
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, bottomPx);
            rt.offsetMax = new Vector2(0f, bottomPx + heightPx);
            return rt.transform;
        }

        /// <summary>Band that STRETCHES the parent's full height minus fixed px insets top and
        /// bottom — it absorbs whatever the fixed rungs leave over.</summary>
        private static Transform PxStretchBand(Transform parent, string name,
            float xMin, float xMax, float topInsetPx, float bottomInsetPx)
        {
            var rt = NewBand(parent, name);
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, bottomInsetPx);
            rt.offsetMax = new Vector2(0f, -topInsetPx);
            return rt.transform;
        }

        private static RectTransform NewBand(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
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
}
