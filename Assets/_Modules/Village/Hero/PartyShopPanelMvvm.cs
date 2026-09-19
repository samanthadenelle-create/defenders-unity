// =============================================================================
// PartyShopPanelMvvm - the PARTY weapon/armor shop VIEW (docs/STORE_EQUIP_SPEC.md).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hero
//
// A DUMB SKIN: builds presentation (ElarionUiKit dark-glass + gold frame, the SHARED
// kit) and BINDS a PartyShopVM. ALL state/logic (party filter, buy/sell/equip,
// affordability, deltas) lives in the VM - the View never reads game state.
//
// MIRRORS EquipmentPanel (and the legacy ShopPanel it replaced - that panel was DELETED
// on 2026-09-06, WO-1430, so its name below is lineage, not a file to open):
//   * BuildModalCanvas (sortingOrder 31000 + overrideSorting) + Scrim(onTapClose) + PanelFramed;
//   * TOP-LEFT a row of PARTY-MEMBER icon buttons (one per member, portrait/crest) - tap
//     selects -> vm.SelectMember -> Render re-filters; the selected member is highlighted;
//   * BUY / SELL tabs (both on the SAME screen - single-tap, no leaving to sell);
//   * a dynamic scroll grid of item rows, each: the REAL item image (iconPath sprite, glyph
//     fallback), name, price, the stat + delta line, affordability colour, EQUIPPED/OWNED
//     state, and ONE single-tap buy/equip/sell action (no duplicate bars);
//   * scrim / Close ? (touch - no Escape; hotkeys are gone).
//
// Code-built uGUI ONLY (no UXML - ?8). It builds its own Canvas on Open, so it needs no
// PanelSettings. Registered with PanelManager + PanelRouter (PanelId.PartyShop).
// ⚠ FLAG CORRECTED 2026-09-06 (WO-1430): FeatureFlags.PartyShop defaults ON (it always did -
// the "(OFF)" written here was never true of the code) and is now a KILL-SWITCH: the legacy
// ShopPanel twin is DELETED, the dialogue "OpenShop" verb routes to PanelId.PartyShop with NO
// flag branch (DialogueCommandSink.cs:88-93; DialogueService.cs:113 likewise, guarded only by
// the shops-closed-during-combat check), and this bootstrap is the only spawner - so flag OFF
// means no gear shop at all, not a fallback screen.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;                 // URP: the runtime preview camera renders to its RT (mirror BuildPreviewModal)
using UnityEngine.AddressableAssets;                   // Blink "gear/" model resolve for the 3D preview
using UnityEngine.ResourceManagement.AsyncOperations;  // the addressable handle (released on swap/close)
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Crafting;

namespace DeNelle.Village.Hero
{
    [DisallowMultipleComponent]
    public sealed class PartyShopPanelMvvm : MonoBehaviour, IPanelView
    {
        private static readonly Color TabSelectedTint = new Color(1.15f, 1.10f, 0.92f, 1f);
        private static readonly Color TabRestTint     = new Color(0.58f, 0.55f, 0.50f, 1f);
        private static readonly Color RowSelectedTint = new Color(1.15f, 1.10f, 0.92f, 1f);

        private PartyShopVM _vm;
        private InventoryStore _store;
        private readonly List<GearLoadoutEquipTarget> _targetAdapters = new List<GearLoadoutEquipTarget>();

        private string _vendorContext;
        private string _displayName;
        // Owner F8 2026-07-10: when the NPC opened the shop in a single Buy/Sell flow, the mode
        // it locked to (null = both-tabs legacy open). Passed to the VM so it presets + locks the tab.
        private PartyShopTab? _lockMode;

        private GameObject _ui;
        private GameObject _contentRoot;
        private GameObject _partyBar;
        private GameObject _tabBar;
        private GameObject _categoryBar;
        private GameObject _typeBar;
        private RectTransform _scrollContent;
        // WO-1584: the live scroll seam, kept so the SELECTED row can be scrolled into view.
        private ElarionUiKit.ScrollZoneHandle _scrollZone;
        private TMPro.TextMeshProUGUI _headerLabel;
        private TMPro.TextMeshProUGUI _memberLabel;

        // WO-714 W1 (P2): the wallet is the ONE kit CurrencyChip (gold primary) in the frame's
        // FOOTER zone — the chip owns CompactNumber formatting + count-tween.
        private ElarionUiKit.CurrencyChipHandle _walletChip;

        // WO-714 W1 (P1): BUY/SELL + category + type rows are kit BuildTab handles — the kit's
        // plate/underline selected state (shape + luminance), never a hue-only tint.
        private ElarionUiKit.TabHandle _tabBuy, _tabSell;
        private readonly List<(PartyShopCategory cat, ElarionUiKit.TabHandle tab)> _categoryTabs =
            new List<(PartyShopCategory, ElarionUiKit.TabHandle)>();
        private readonly List<(PartyShopType type, ElarionUiKit.TabHandle tab)> _typeTabs =
            new List<(PartyShopType, ElarionUiKit.TabHandle)>();

        // WO-714 W1 (P5): transient VM status surfaces as a kit ToastCard, not a stuck strip.
        private GameObject _toastCard;
        private string _lastStatus;
        private bool _statusBaselined;

        // -- Preview pane (WO-501 owner point 3) - a 3D render of the selected gear + stat-diff + price --
        private GameObject _previewRoot;        // the chrome well that holds the preview widgets
        private Image _previewBacking;          // dark slate frame BEHIND the icon/3D square (transparent-icon backing)
        private RawImage _previewImage;         // fed by the live RenderTexture (3D model)
        private TMPro.TextMeshProUGUI _previewGlyph;  // 2D fallback glyph drawn over the image when no model
        private Image _previewSprite;           // 2D fallback sprite (iconPath/catalog) when no model
        private TMPro.TextMeshProUGUI _previewName;
        private TMPro.TextMeshProUGUI _previewStats;   // flavour/desc line (rarity + class fit)
        private TMPro.TextMeshProUGUI _previewDelta;   // summary delta line (kept; per-stat block below)
        private RectTransform _previewSpecs;           // per-stat block: "Label Value (Delta)" rows (WO: weapons matter)
        private TMPro.TextMeshProUGUI _previewPrice;
        private TMPro.TextMeshProUGUI _previewEmpty;  // "Select an item to preview." empty state

        // The single Purchase/Sell toggle + Equip buttons (bottom action bar).
        private Button _buySellBtn;
        private TMPro.TextMeshProUGUI _buySellLabel;
        private Button _equipBtn;
        private TMPro.TextMeshProUGUI _equipLabel;
        private Button _improveBtn;                          // WO-808 reforge CTA (owned gear only)
        private TMPro.TextMeshProUGUI _improveLabel;
        private Color _improveLabelBase = Color.white;
        // The kit-assigned label ink (contrast law) — the disabled cue dims THIS color.
        private Color _equipLabelBase = Color.white;

        // -- Preview icon BACKING card (tunable) -----------------------------------
        // A dark slate frame drawn BEHIND the preview icon/3D square so a transparent (or
        // baked-grey) icon sits on a neutral premium backing instead of the gold store panel.
        // The square anchors below MUST match the _previewImage/_previewSprite square in
        // BuildPreviewPane (kept in sync via these consts).
        // WO-1877: plate fills the well; name/desc/specs sit UNDER it with enough band that HP
        // + delta stay fully visible (live Seeker frame clipped the HP line under 0.42→0.19).
        private static readonly Vector2 PreviewSquareMin = new Vector2(0.20f, 0.50f);
        private static readonly Vector2 PreviewSquareMax = new Vector2(0.80f, 0.98f);
        // The backing extends a little PAST the icon square so it reads as a frame around it.
        private const float PreviewBackingPad = 0.03f;
        private static readonly Color PreviewBackingColor = new Color(0.05f, 0.05f, 0.06f, 0.92f);  // dark slate

        // -- The isolated runtime 3D-preview rig (the BuildPreviewModal "Offset Forge" pattern) --
        private const int RtSize = 384;
        private const int PreviewLayer = 31;
        private GameObject _rigRoot;
        private GameObject _rigVisual;
        private Camera _rigCam;
        private RenderTexture _rigRt;
        private readonly List<Material> _rigMaterials = new List<Material>();
        private AsyncOperationHandle<GameObject> _rigHandle;
        private bool _rigHandleOpen;
        private string _rigModelId;             // the id currently mounted (skip rebuild when unchanged)
        private int _rigGeneration;             // stale-async guard (a newer select supersedes an in-flight load)
        private float _rigYaw;                  // auto-spin angle

        private PanelHandle _panelHandle;

        // Rows recorded per rebuild as (id, plate, locked) so Render can hold the selected row + keep
        // locked (show-all-but-greyed) rows dimmed across re-dress passes.
        private readonly List<(string id, Image plate, bool locked)> _rowPlates =
            new List<(string id, Image plate, bool locked)>();

        // Dim factor for a locked (ineligible-for-this-member) row so it reads as non-purchasable.
        private const float LockedRowAlpha = 0.45f;

        public bool IsOpen => _ui != null;

        // WO-1430 (2026-09-06): the AutoPilot vendor-contract phase used to read these off the
        // legacy ShopPanel, which was DELETED as doorless in the same change. They are passthroughs
        // to the bound VM (the state owner) so the bot judges the shop the player actually opens,
        // and the View still reads no game state of its own. Null/empty before the first Open.
        /// <summary>The stock the CURRENT vendor context actually built, for contract assertions.</summary>
        public IReadOnlyList<(string id, GearKind kind)> CurrentStock =>
            _vm != null ? _vm.CurrentStock : System.Array.Empty<(string, GearKind)>();

        /// <summary>The vendor context this panel is currently showing ("" when generic).</summary>
        public string VendorContext => _vm != null ? _vm.VendorContext : _vendorContext;

        // -- Registration (mirror BuildingUpgradePanelMvvm) ------------------------

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Party Shop", Close, () => IsOpen);
            PanelRouter.Register(PanelId.PartyShop, OpenGeneric);
            PanelRouter.Register(PanelId.PartyShop, (System.Action<string>)OpenContext);
            // Subject+mode opener (owner F8 2026-07-10): the NPC's Buy/Sell choice opens the
            // shop LOCKED to one flow (top tabs hidden, one list + one action).
            PanelRouter.Register(PanelId.PartyShop, (System.Action<string, string>)OpenContextMode);
        }

        private void OnDestroy()
        {
            DisposeViewModel();
            TeardownRig();
            if (_ui != null) Destroy(_ui);
            _ui = null;
            PanelRouter.Unregister(PanelId.PartyShop, OpenGeneric);
            PanelRouter.Unregister(PanelId.PartyShop, (System.Action<string>)OpenContext);
            PanelRouter.Unregister(PanelId.PartyShop, (System.Action<string, string>)OpenContextMode);
        }

        private void OpenGeneric() => Open(null, null, null);
        private void OpenContext(string vendorContext) => Open(vendorContext, null, null);

        // Owner F8 2026-07-10: the NPC's Buy/Sell choice routes here with mode "buy"/"sell";
        // parse it to the tab the shop opens LOCKED to (unknown mode = both-tabs open).
        private void OpenContextMode(string vendorContext, string mode) =>
            Open(vendorContext, null, ParseMode(mode));

        private static PartyShopTab? ParseMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return null;
            switch (mode.Trim().ToLowerInvariant())
            {
                case "buy":  return PartyShopTab.Buy;
                case "sell": return PartyShopTab.Sell;
                default:      return null;
            }
        }

        // -- Open: resolve party + store at the open-site, build chrome, bind VM ---

        public void Open(string vendorContext, string displayName) =>
            Open(vendorContext, displayName, null);

        public void Open(string vendorContext, string displayName, PartyShopTab? lockMode)
        {
            Close();
            _vendorContext = vendorContext ?? "";
            _displayName = displayName;
            _lockMode = lockMode;

            BuildChrome();
            ConstructViewModel();
            Bind(_vm);

            if (!PanelManager.NotifyOpened(_panelHandle))
                return;   // rejected (e.g. in battle) - NotifyOpened already invoked Close.

            Debug.Log($"[PartyShopPanelMvvm] Opened for vendor '{_vendorContext}'. Bound PartyShopVM (MVVM).");
        }

        // Resolve the live targets (hero + every companion body with a GearLoadout) + the owned
        // store, mirror EquipmentPanel.ConstructViewModel, then inject into the VM. Member levels
        // come from each wearer's HeroProgression (1 when absent).
        private void ConstructViewModel()
        {
            DisposeViewModel();

            var members = new List<IEquipTarget>();
            var levels = new List<int>();
            _targetAdapters.Clear();

            // The player hero first (the default selected member).
            var hero = GameObject.FindWithTag("Player");
            if (hero == null)
            {
                var loco = FindAnyObjectByType<HeroLocomotion>();
                if (loco != null) hero = loco.gameObject;
            }
            if (hero != null)
            {
                var hl = hero.GetComponent<GearLoadout>();
                if (hl == null) hl = hero.AddComponent<GearLoadout>();
                string hjob = ResolveHeroJob(hl);
                var adapter = new GearLoadoutEquipTarget(hl, HeroName(hjob), hjob);
                _targetAdapters.Add(adapter);
                members.Add(adapter);
                levels.Add(ResolveLevel(hero));
            }

            // Companions: each StoryCompanion body carries a GearLoadout bound to its class.
            foreach (var comp in FindObjectsByType<StoryCompanion>())
            {
                if (comp == null) continue;
                var cl = comp.GetComponent<GearLoadout>();
                if (cl == null) continue;
                string cjob = comp.Hero.ToString().ToLowerInvariant();
                var adapter = new GearLoadoutEquipTarget(cl, comp.DisplayName, cjob);
                _targetAdapters.Add(adapter);
                members.Add(adapter);
                levels.Add(ResolveLevel(comp.gameObject));
            }

            // DI-in-Open (strict-MVVM): PartyShopVM.CreateDefault resolves the economy + owned-store
            // handles itself (EconomyService.Instance + VillageInventory.Instance, WO-578 UNIONed with
            // the members — store/Forge/Preview agree) so this View names neither singleton. Members/
            // levels stay View-resolved (they wrap live GameObjects). The factory returns the store so we
            // keep the handle to dispose, and composes the "Party Shop" no-context header default.
            _vm = PartyShopVM.CreateDefault(_vendorContext, _displayName, members, levels, Close, _lockMode, out _store);
        }

        private static int ResolveLevel(GameObject go)
        {
            if (go == null) return 1;
            var prog = go.GetComponent<HeroProgression>();
            return prog != null ? prog.Level : 1;
        }

        private static string ResolveHeroJob(GearLoadout loadout)
        {
            var ha = loadout != null ? loadout.GetComponent<HeroAbilities>() : null;
            string j = ha != null ? ha.HeroClass : null;
            return string.IsNullOrEmpty(j) ? AbilityCatalog.DefaultClass : j;
        }

        private static string HeroName(string job)
        {
            switch ((job ?? "").ToLowerInvariant())
            {
                case "knight": return "Grom";
                case "mage":   return "Thrain";
                case "ranger": return "Sylas";
                case "cleric": return "Elara";
                default:        return Cap(job);
            }
        }

        // -- IPanelView ------------------------------------------------------------

        public void Bind(IPanelViewModel vm)
        {
            Unbind();
            _vm = vm as PartyShopVM;
            if (_vm == null) return;
            _vm.Changed += Render;
            Render();
        }

        public void Unbind()
        {
            if (_vm != null) _vm.Changed -= Render;
        }

        // -- Render: repaint from vm.* ONLY ----------------------------------------

        private void Render()
        {
            if (_vm == null) return;

            if (_headerLabel != null) _headerLabel.text = _vm.Title;
            if (_memberLabel != null) _memberLabel.text = _vm.MemberLabel;
            // WO-714 W1 (P2): the wallet renders through the ONE CurrencyChip — the chip owns
            // formatting (CompactNumber, WO-697) + count-tween; never a raw text line.
            _walletChip?.SetAmount(_vm.Coins);
            // WO-714 W1 (P5): transient status surfaces as a toast, never a stuck strip.
            MaybeToastStatus();

            // WO-598 per-trade chrome: only a GEAR vendor shows the equip context (party
            // selector, member header, Equip button). The Market/Jeweler are goods counters
            // - no paper-doll, no equip affordances (flag_03/flag_11). Binding only; the
            // widgets are still built once in BuildChrome.
            bool gearTrade = _vm.Layout == VendorLayout.Gear;
            if (_partyBar != null) _partyBar.SetActive(gearTrade);
            if (_memberLabel != null) _memberLabel.gameObject.SetActive(gearTrade);
            if (_equipBtn != null) _equipBtn.gameObject.SetActive(gearTrade);

            // Owner F8 2026-07-10: a single-mode (Buy OR Sell) open HIDES the top BUY/SELL strip —
            // the NPC dialogue already chose the mode, so two competing controls collapse to one
            // list + one bottom action. A both-tabs open (TabsLocked == false) still shows the strip.
            if (_tabBar != null) _tabBar.SetActive(!_vm.TabsLocked);

            RebuildPartyBar();
            HighlightTab(_vm.Tab);
            UpdateCategoryBar();
            RebuildTypeBar();
            // WO-1584: the filter/party bands are bound ABOVE (they hide per vendor layout), so the
            // two columns claim the freed height BEFORE the list is built - FinalizeScroll's layout
            // rebuild then measures the final viewport, not the stale one.
            ReseatColumns();
            RebuildList();
            HighlightSelectedRow();
            ScrollSelectedIntoView();
            RenderPreview();
            RenderActionBar();
        }

        // -- Chrome (presentation only) --------------------------------------------

        private void BuildChrome()
        {
            _ui = ElarionUiKit.BuildModalCanvas("PartyShopPanelMvvmUI", 31000);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            ElarionUiKit.Scrim(_ui.transform, onTapClose: () => _vm?.Close());

            // SHARED Obsidian chrome (WO-554): black panel + gold trim + gold header + ONE Close.
            // PORTRAIT-CONFORM (owner 2026-07-04, "use the template for UI"): the Merchant_Panel
            // Blink art is PORTRAIT (1005x1507, aspect ~0.667). The panel rect must match that aspect
            // or the frame stretches into a landscape slab (the delivered bug). A ~0.35w x 0.93h rect
            // on 16:9 ≈ 672x1004px (aspect ~0.669) renders the frame TALL like template.png; the body
            // content below is fraction-anchored inside layout.body, so the two columns re-flow into
            // tall/narrow portrait columns automatically.
            // Shared store size (owner felt-test 2026-07-15: all stores same size / matching Y).
            // These portrait values ARE the shared StorePanel rect the kit exposes.
            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, "Party Shop",
                new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), () => _vm?.Close(),
                headerX0: 0.04f, headerX1: 0.96f, frameName: RpgUiCatalog.FrameMerchant,
                medallionIcon: "sword");
            var panel = chrome.content.transform;
            _headerLabel = chrome.title;
            // flag_06 (owner F8 2026-07-06): "The Forge" title clipped mid-glyph at her window size.
            // Fit-or-ellipsize the shared title inside its band (auto-size down to the FontLabel
            // rung, never below — §1.14 kit text-fit).
            ElarionUiKit.FitSingleLine(_headerLabel, ElarionUi.FontLabel, ElarionUi.FontTitle);

            // WO-582 (Blink frame zone-fit): fit ALL content into the frame's BODY drop-zone (the
            // templated inner well) instead of floating over the whole panel rect — this stops the
            // wallet/party/tabs/list/preview/buttons from overlapping the frame's ornate border. Falls
            // back to the panel rect when no frame is used. Mirrors CraftingPanelMvvm.BuildChrome. The
            // shared Close (chrome.close) stays on the full panel (chrome, not content).
            // The merchant frame's legacy `body` drop-zone is intentionally shallow. Seating this
            // complete multi-column workspace inside it collapsed party/filter bands to 15-40px
            // rails on landscape phones. Party Shop therefore owns the full safe inner panel and
            // explicitly reserves its title and Close bands, like the approved Equipment/Store shell.
            var bodyHost = (RectTransform)panel;

            // WO-714 W1 (P2): the wallet is a kit CurrencyChip (gold primary) riding the frame's
            // FOOTER drop-zone (WO-675 §5 grammar); art-absent fallback = the old top-right band
            // so the wallet never blanks.
            var walletGo = new GameObject("Zone_GoldWallet", typeof(RectTransform));
            walletGo.transform.SetParent(bodyHost, false);
            RectTransform walletHost = walletGo.GetComponent<RectTransform>();
            walletHost.anchorMin = new Vector2(0.78f, 0.905f);
            walletHost.anchorMax = new Vector2(0.96f, 0.965f);
            walletHost.offsetMin = Vector2.zero; walletHost.offsetMax = Vector2.zero;
            Vector2 chipMin = Vector2.zero, chipMax = Vector2.one;
            _walletChip = ElarionUiKit.CurrencyChip(walletHost, ElarionUiKit.CurrencyKind.Gold,
                chipMin, chipMax, primary: true, tag: "Gold");

            // TOP-LEFT party-member selector bar (spec point 1).
            _partyBar = new GameObject("PartyBar", typeof(RectTransform));
            _partyBar.transform.SetParent(bodyHost, false);
            var pb = _partyBar.GetComponent<RectTransform>();
            // F8-10 (fleet 4/4): the 0.80..0.885 band gave each chip's name label a 13px rect —
            // shorter than the 12pt hard-floor line (~15px), so TMP Ellipsis culled the whole
            // line (0 glyphs). Bar grows into the free 0.885..0.90 strip (wallet starts 0.905;
            // MemberLabel below tops at 0.80) and the chip's name band widens (see RebuildPartyBar)
            // so the label seats ~18px — a LAYOUT fix, never a font-floor cut.
            pb.anchorMin = new Vector2(0.04f, 0.74f); pb.anchorMax = new Vector2(0.96f, 0.90f);
            pb.offsetMin = Vector2.zero; pb.offsetMax = Vector2.zero;

            // Selected-member sub-header (name - class (Lv N)).
            var memGo = new GameObject("MemberLabel", typeof(TMPro.TextMeshProUGUI));
            memGo.transform.SetParent(bodyHost, false);
            var mr = memGo.GetComponent<RectTransform>();
            mr.anchorMin = new Vector2(0.04f, 0.685f); mr.anchorMax = new Vector2(0.62f, 0.735f);
            mr.offsetMin = Vector2.zero; mr.offsetMax = Vector2.zero;
            _memberLabel = memGo.GetComponent<TMPro.TextMeshProUGUI>();
            _memberLabel.fontSize = ElarionUi.FontBody;
            _memberLabel.color = ElarionUi.Parchment;
            _memberLabel.fontStyle = TMPro.FontStyles.Bold;
            _memberLabel.alignment = TMPro.TextAlignmentOptions.Left;
            _memberLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(_memberLabel);   // flag_06: "Name - class (Lv N)" never clips

            // BUY / SELL tabs (both on the same screen - spec point 4).
            _tabBar = new GameObject("TabBar", typeof(RectTransform));
            _tabBar.transform.SetParent(bodyHost, false);
            var tb = _tabBar.GetComponent<RectTransform>();
            tb.anchorMin = new Vector2(0.64f, 0.675f); tb.anchorMax = new Vector2(0.96f, 0.74f);
            tb.offsetMin = Vector2.zero; tb.offsetMax = Vector2.zero;
            // WO-714 W1 (P1): kit BuildTab — plate/underline selection (shape + luminance).
            _tabBuy  = ElarionUiKit.BuildTab(_tabBar.transform, "BUY",
                new Vector2(0.02f, 0.05f), new Vector2(0.49f, 0.95f), () => _vm?.SetTab(PartyShopTab.Buy));
            _tabSell = ElarionUiKit.BuildTab(_tabBar.transform, "SELL",
                new Vector2(0.51f, 0.05f), new Vector2(0.98f, 0.95f), () => _vm?.SetTab(PartyShopTab.Sell));
            StylePartyTab(_tabBuy);
            StylePartyTab(_tabSell);

            // Category selector ("dropdown selections": All / Weapons / Armor) - the missing
            // narrow over the combined weapons+armor list. Pinned/hidden for single-kind vendors
            // (CategorySelectorVisible). Sits just under the tab/member band, above the grid.
            _categoryBar = new GameObject("CategoryBar", typeof(RectTransform));
            _categoryBar.transform.SetParent(bodyHost, false);
            var cb = _categoryBar.GetComponent<RectTransform>();
            // WO-840 B2: the category/type rows sat 0.705-0.748 / 0.655-0.70 — a ~0.005
            // gap the selected chip's gold pennant plate overdraws (owner capture: the
            // "All" pennant bleeding over the neighbour row). Re-spaced with real gaps
            // (tab band 0.755 -> cb 0.744 -> 0.703 -> tyb 0.690 -> 0.648) and the chips
            // inset a touch deeper inside each bar (0.10-0.90) so the plate art clears.
            cb.anchorMin = new Vector2(0.04f, 0.605f); cb.anchorMax = new Vector2(0.52f, 0.67f);
            cb.offsetMin = Vector2.zero; cb.offsetMax = Vector2.zero;
            _categoryTabs.Clear();
            CreateCategory("All",      new Vector2(0.01f, 0.24f), PartyShopCategory.All);
            CreateCategory("Weapons",  new Vector2(0.26f, 0.49f), PartyShopCategory.Weapons);
            CreateCategory("Off Hand", new Vector2(0.51f, 0.74f), PartyShopCategory.OffHand);
            CreateCategory("Armor",    new Vector2(0.76f, 0.99f), PartyShopCategory.Armor);

            // Finer weapon/armor TYPE chip row (WO-501 owner point 1) - sits just under the category
            // bar. Rebuilt per Render from _vm.AvailableTypes so it only shows live chips (>0 rows).
            _typeBar = new GameObject("TypeBar", typeof(RectTransform));
            _typeBar.transform.SetParent(bodyHost, false);
            var tyb = _typeBar.GetComponent<RectTransform>();
            // WO-840 B2: dropped from 0.655-0.70 to open the pennant-clearing gap below
            // the category bar (see cb above).
            tyb.anchorMin = new Vector2(0.04f, 0.535f); tyb.anchorMax = new Vector2(0.52f, 0.60f);
            tyb.offsetMin = Vector2.zero; tyb.offsetMax = Vector2.zero;

            // The scroll list area - SLIM name column (WO-501 owner point 2): narrowed to the left ~36%
            // so the 3D preview pane sits beside it. The VerticalLayoutGroup auto-fits the new width.
            _contentRoot = new GameObject("Content", typeof(RectTransform));
            _contentRoot.transform.SetParent(bodyHost, false);
            var cr = _contentRoot.GetComponent<RectTransform>();
            // owner 07-04: widened list column (36%->48%). Orchestrator capture 07-06 #3: bottom
            // raised 0.12->0.23 so the action-bar stack clears the shared Close CTA (see below).
            cr.anchorMin = new Vector2(0.04f, ColumnBottomY); cr.anchorMax = new Vector2(0.52f, 0.525f);
            cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;

            // The 3D render preview pane (WO-501 owner point 3) beside the slim list.
            BuildPreviewPane(bodyHost);

            // -- Bottom action bar (WO-501 owner point 4): Purchase/Sell toggle + Equip --
            // Close is the SHARED top-right Obsidian Close button (WO-554) — no per-panel footer Close.

            // ONE Purchase/Sell button whose label + action TOGGLE on _vm.Tab (the pattern proven by
            // the legacy ShopPanel, deleted 2026-09-06 by WO-1430 - the old ShopPanel.cs:341-344
            // citation no longer resolves) - routes through _vm.Act on the selected id.
            // Orchestrator capture 2026-07-06 #3: FrameMerchant declares NO footer zone and the
            // SHARED Close is force-seated at the panel's bottom-centre DefaultCloseZone (fixed
            // 360x120 CTA, drawn last = over us) — buttons at body-y 0.03-0.105 sat at panel-y
            // ~0.14-0.19, INSIDE the Close band (~0.05-0.17) and rendered half-hidden. The whole
            // bottom stack is raised to clear it: buttons 0.10-0.175, status 0.18-0.22, list/
            // preview bottoms 0.23 (all body-relative; panel-y of the buttons is now ~0.19+).
            // WO-714 W1: both actions are kit BuildObsidianButton — the kit owns plate art,
            // label ink (contrast law: gold plate = dark Ink, dark plate = Parchment) and text-fit.
            // WO-840 B3: the old 0.04-0.28 / 0.30-0.60 / 0.64-0.86 slots left 0.86-1.0
            // EMPTY while "Improve"/"Unequip"/"Purchase NNN Gold" ellipsized ("Impro...",
            // "Uneq..." on the owner's device). All three widened into the free margin
            // (0.02-0.30 / 0.32-0.68 / 0.70-0.98); heights (touch size) unchanged.
            _buySellBtn = ElarionUiKit.BuildObsidianButton(bodyHost, "Purchase",
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.32f, 0.22f), new Vector2(0.68f, 0.38f),
                () => { var s = _vm?.SelectedId; if (!string.IsNullOrEmpty(s)) _vm.Act(s); });
            _buySellLabel = _buySellBtn != null ? _buySellBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>() : null;
            if (_buySellBtn != null)
            {
                MedievalUiSkin.ApplyButton(_buySellBtn, primary: true);
                if (_buySellBtn.targetGraphic is Image primaryPlate)
                    primaryPlate.color = new Color(1f, 0.78f, 0.42f, 1f);
            }

            // EQUIP the selected owned item to the selected member (IEquipTarget seam via the VM).
            // Owner ruling 07-06: 3-state control — the tap dispatches on the SELECTED item's
            // equipped state: worn -> UnequipSelected, owned -> EquipSelected (RenderActionBar
            // swaps the label + disables state 3; the VM guards re-verify, so a stale tap no-ops
            // with a status line rather than mis-acting).
            _equipBtn = ElarionUiKit.BuildObsidianButton(bodyHost, "Equip",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.70f, 0.22f), new Vector2(0.98f, 0.38f),
                () =>
                {
                    if (_vm == null) return;
                    var it = _vm.SelectedItem;
                    if (it.HasValue && it.Value.Equipped) _vm.UnequipSelected();
                    else _vm.EquipSelected();
                });
            _equipLabel = _equipBtn != null ? _equipBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>() : null;
            if (_equipBtn != null) MedievalUiSkin.ApplyButton(_equipBtn, primary: false);
            // The kit set the label ink per contrast law — remember it so RenderActionBar's
            // disabled cue dims the SAME ink (luminance cue) instead of forcing a hue.
            _equipLabelBase = _equipLabel != null ? _equipLabel.color : ElarionUi.Parchment;

            // WO-808 IMPROVE (Option A reforge): the left x 0.02-0.30 band of the raised action
            // row (WO-840 B3 widened from 0.04-0.28). Visible only when the selected row is
            // OWNED gear (RenderActionBar drives label/enable/visibility); the VM re-verifies
            // on tap, so a stale tap no-ops with an honest status line.
            _improveBtn = ElarionUiKit.BuildObsidianButton(bodyHost, "Improve",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.02f, 0.22f), new Vector2(0.30f, 0.38f),
                () => _vm?.ImproveSelected());
            _improveLabel = _improveBtn != null ? _improveBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>() : null;
            if (_improveBtn != null) MedievalUiSkin.ApplyButton(_improveBtn, primary: false);
            _improveLabelBase = _improveLabel != null ? _improveLabel.color : ElarionUi.Parchment;

            // WO-714 W1 (P5): no stuck status strip — vm.Status changes surface as a kit toast
            // (MaybeToastStatus in Render). Baseline the open-time idle hint so it never toasts.
            _statusBaselined = false;

            // Raise the action buttons above the scroll content so a row never eats the tap (ShopPanel trap).
            if (_buySellBtn != null) _buySellBtn.transform.SetAsLastSibling();
            if (_equipBtn != null) _equipBtn.transform.SetAsLastSibling();
            if (_improveBtn != null) _improveBtn.transform.SetAsLastSibling();
            if (chrome.close != null) chrome.close.transform.SetAsLastSibling();
        }

        // WO-714 W1 (P1): category chips are kit BuildTab — plate/underline selection.
        private void CreateCategory(string label, Vector2 anchorX, PartyShopCategory cat)
        {
            var tab = ElarionUiKit.BuildTab(_categoryBar.transform, label,
                new Vector2(anchorX.x, 0.10f), new Vector2(anchorX.y, 0.90f),   // WO-840 B2: deeper inset, pennant clears the bar
                () => _vm?.SetCategory(cat));
            if (tab != null)
            {
                StylePartyTab(tab);
                _categoryTabs.Add((cat, tab));
            }
        }

        private static void StylePartyTab(ElarionUiKit.TabHandle tab)
        {
            if (tab == null || tab.button == null) return;
            MedievalUiSkin.ApplyButton(tab.button, primary: true);
            if (tab.label != null)
            {
                // Compact filter chips need the plate width, not a second large inset.
                var textRect = tab.label.rectTransform;
                textRect.anchorMin = new Vector2(0.02f, textRect.anchorMin.y);
                textRect.anchorMax = new Vector2(0.98f, textRect.anchorMax.y);
                textRect.offsetMin = new Vector2(0f, textRect.offsetMin.y);
                textRect.offsetMax = new Vector2(0f, textRect.offsetMax.y);
                tab.label.characterSpacing = 0f;
            }
            if (tab.button.targetGraphic is Image plate) plate.type = Image.Type.Simple;
            var selected = tab.button.transform.Find("Selected") as RectTransform;
            if (selected == null) return;
            selected.anchorMin = new Vector2(0.08f, 0.02f);
            selected.anchorMax = new Vector2(0.92f, 0.10f);
            selected.offsetMin = Vector2.zero;
            selected.offsetMax = Vector2.zero;
            var image = selected.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = ElarionUi.Gilt;
                image.raycastTarget = false;
            }
        }

        // -- Finer weapon/armor TYPE chip row (WO-501 owner point 1) ------------------
        // Rebuilt per Render from _vm.AvailableTypes so only chips with >0 candidate rows show
        // (never a dead chip) + an "All" chip to clear the narrow. Highlights the active chip.
        private void RebuildTypeBar()
        {
            if (_typeBar == null || _vm == null) return;
            _typeTabs.Clear();
            for (int i = _typeBar.transform.childCount - 1; i >= 0; i--)
            {
                var c = _typeBar.transform.GetChild(i);
                if (c == null) continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            var avail = _vm.AvailableTypes;
            // Only one TYPE => the narrow is meaningless (the hero's gear is already one kind): hide the row.
            if (avail == null || avail.Count <= 1) { _typeBar.SetActive(false); return; }
            _typeBar.SetActive(true);

            // "All" + one chip per available type, evenly spaced across the bar.
            var chips = new List<(string label, PartyShopType type)> { ("All", PartyShopType.Any) };
            foreach (var t in avail) chips.Add((TypeLabel(t), t));

            // WO-714 W1 (P1): type chips are kit BuildTab — plate/underline selection.
            int n = chips.Count;
            const float gap = 0.01f;
            float w = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                var chip = chips[i];
                float x0 = gap + i * (w + gap);
                var tab = ElarionUiKit.BuildTab(_typeBar.transform, chip.label,
                    new Vector2(x0, 0.10f), new Vector2(x0 + w, 0.90f),   // WO-840 B2: deeper inset, pennant clears the bar
                    () => _vm?.SetType(chip.type));
                if (tab == null) continue;
                StylePartyTab(tab);
                if (tab.button != null) tab.button.name = "Type_" + chip.type;
                tab.SetSelected(_vm.Type == chip.type);
                if (tab.label != null) tab.label.color = _vm.Type == chip.type ? ElarionUi.Gilt : ElarionUi.Parchment;
                _typeTabs.Add((chip.type, tab));
            }

            // Equal widths wasted space on 1H/2H while SHIELD clipped at the font
            // floor. Measure the actual styled captions and redistribute spare width.
            Canvas.ForceUpdateCanvases();
            float barWidth = _typeBar.GetComponent<RectTransform>().rect.width;
            var widths = new float[_typeTabs.Count];
            float required = 0f;
            for (int i = 0; i < _typeTabs.Count; i++)
            {
                var caption = _typeTabs[i].Item2.label;
                float textWidth = 0f;
                if (caption != null)
                {
                    bool autoSize = caption.enableAutoSizing;
                    float fontSize = caption.fontSize;
                    caption.enableAutoSizing = false;
                    caption.fontSize = caption.fontSizeMin;
                    textWidth = caption.GetPreferredValues(caption.text).x;
                    caption.fontSize = fontSize;
                    caption.enableAutoSizing = autoSize;
                }
                widths[i] = Mathf.Max(ElarionUiKit.MinTouchPx, (textWidth + 12f) / 0.96f);
                required += widths[i];
            }
            float available = barWidth * (1f - gap * (_typeTabs.Count + 1));
            if (barWidth > 0f && required <= available && _typeTabs.Count > 0)
            {
                float spare = (available - required) / _typeTabs.Count;
                float left = gap;
                for (int i = 0; i < _typeTabs.Count; i++)
                {
                    var rect = (RectTransform)_typeTabs[i].Item2.button.transform;
                    float width = (widths[i] + spare) / barWidth;
                    rect.anchorMin = new Vector2(left, rect.anchorMin.y);
                    rect.anchorMax = new Vector2(left + width, rect.anchorMax.y);
                    left += width + gap;
                }
            }
        }

        private static string TypeLabel(PartyShopType t)
        {
            switch (t)
            {
                case PartyShopType.OneHand: return "1h";
                case PartyShopType.TwoHand: return "2h";
                case PartyShopType.Shield:  return "Shield";
                case PartyShopType.Light:   return "Light";
                case PartyShopType.Heavy:   return "Heavy";
                default:                    return "All";
            }
        }

        // Show the category selector only for vendors that stock BOTH gear kinds (else it is
        // pinned to the single kind and the row is hidden), then highlight the active category.
        private void UpdateCategoryBar()
        {
            if (_categoryBar == null || _vm == null) return;
            bool show = _vm.CategorySelectorVisible;
            _categoryBar.SetActive(show);
            if (!show) return;

            // WO-714 W1: kit tab selection state (shape + luminance), never a hue-only tint.
            for (int i = 0; i < _categoryTabs.Count; i++)
            {
                bool selected = _categoryTabs[i].cat == _vm.Category;
                _categoryTabs[i].tab?.SetSelected(selected);
                if (_categoryTabs[i].tab?.label != null)
                    _categoryTabs[i].tab.label.color = selected ? ElarionUi.Gilt : ElarionUi.Parchment;
            }
        }

        private void HighlightTab(PartyShopTab tab)
        {
            _tabBuy?.SetSelected(tab == PartyShopTab.Buy);
            _tabSell?.SetSelected(tab == PartyShopTab.Sell);
            if (_tabBuy?.label != null) _tabBuy.label.color = tab == PartyShopTab.Buy ? ElarionUi.Gilt : ElarionUi.Parchment;
            if (_tabSell?.label != null) _tabSell.label.color = tab == PartyShopTab.Sell ? ElarionUi.Gilt : ElarionUi.Parchment;
        }

        // -- Party selector (top-left member icon buttons) -------------------------

        private void RebuildPartyBar()
        {
            if (_partyBar == null || _vm == null) return;
            for (int i = _partyBar.transform.childCount - 1; i >= 0; i--)
            {
                var c = _partyBar.transform.GetChild(i);
                if (c == null) continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            var party = _vm.Party;
            int n = Mathf.Max(1, party.Count);
            const float gap = 0.012f;
            float w = (1f - gap * (n + 1)) / n;
            // Cap each member chip's width so a small party doesn't stretch portraits across the bar.
            float chipW = Mathf.Min(w, 0.16f);

            for (int i = 0; i < party.Count; i++)
            {
                int idx = i;
                var member = party[i];
                float x0 = gap + i * (chipW + gap);
                var btn = ElarionUiKit.ButtonPack(_partyBar.transform, "", ElarionUiKit.ButtonKind.Gold,
                    new Vector2(x0, 0.05f), new Vector2(x0 + chipW, 0.95f),
                    () => _vm?.SelectMember(idx), packSpriteName: RpgUiCatalog.ButtonFrame);
                if (btn == null) continue;
                btn.name = "Member_" + idx;
                MedievalUiSkin.ApplyButton(btn, primary: member.Selected);

                // Portrait/crest glyph + class initial as the member token (real portrait sprite when present).
                var icon = ResolvePortrait(member.Class);
                if (icon != null)
                {
                    var imgGo = ElarionUiKit.AddImage(btn.transform, "Portrait",
                        new Vector2(0.12f, 0.42f), new Vector2(0.88f, 0.95f), Color.white, rounded: false);
                    var img = imgGo.GetComponent<Image>();
                    img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
                }
                else
                {
                    ElarionUiKit.Label(btn.transform, ClassCrest(member.Class), 0.44f, 0.98f, ElarionUi.Gilt,
                        ElarionUi.FontHead, TMPro.TextAlignmentOptions.Center, 0.0f, 1f, bold: true);
                }
                // Member first name under the token — UNSELECTED chips only (WO-840 B1).
                // The SELECTED member's name is already carried by the _memberLabel
                // sub-header ("Name - Class (Lv N)") directly under the bar, and the
                // duplicate chip-local copy crowded the portrait's lower edge + painted
                // over that sub-header (owner capture 2026-08-02: "Grom" over the
                // portrait). The redundant selected-chip name goes; unselected chips
                // keep theirs so the player can tell who to tap. Band history (F8-10):
                // 0.02..0.40 of the 0.80..0.90 bar seats ~18px, above the 12pt floor.
                if (!member.Selected)
                {
                    var nameTag = ElarionUiKit.Label(btn.transform, member.Name, 0.02f, 0.40f, ElarionUi.Parchment,
                        ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center, 0.0f, 1f, bold: false);
                    ElarionUiKit.FitSingleLine(nameTag, 0f, ElarionUi.FontMicro);   // flag_06: chip names never spill
                }

                var plate = btn.GetComponent<Image>();
                if (plate != null) plate.color = member.Selected ? TabSelectedTint : TabRestTint;

                // WO-714 W1 (colorblind canon): selection carries a SHAPE cue too — a gilt
                // underline bar on the selected member chip, never the tint alone. (No kit
                // member-chip primitive exists yet — noted for the wave-2 uplift.)
                if (member.Selected)
                {
                    var rim = ElarionUiKit.AddImage(btn.transform, "SelectedRim",
                        new Vector2(0.06f, 0f), new Vector2(0.94f, 0.05f),
                        ElarionUiKit.ObsidianTrim, rounded: false);
                    var rimImg = rim.GetComponent<Image>();
                    if (rimImg != null) rimImg.raycastTarget = false;
                }
            }
        }

        // Resolve a portrait sprite for a class. No dedicated class-portrait sheet exists, so we
        // map to the pack's class glyph (sword/shield/etc) as the token; null -> the View draws
        // the ClassCrest glyph instead. Presentation only.
        private static Sprite ResolvePortrait(string cls)
        {
            switch ((cls ?? "").ToLowerInvariant())
            {
                case "knight": return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconSword);
                case "cleric": return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconHeart);
                case "ranger": return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconCompass);
                case "mage":   return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconQuest);
                default:        return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconTalk);
            }
        }

        private static string ClassCrest(string job)
        {
            switch ((job ?? "").ToLowerInvariant())
            {
                case "knight": return "K";
                case "mage":   return "M";
                case "ranger": return "R";
                case "cleric": return "C";
                default:        return "*";
            }
        }

        // -- Item list -------------------------------------------------------------

        private void RebuildList()
        {
            using var _ = FlowTrace.Enter("Store", $"PartyShop.RebuildList tab={_vm.Tab}");
            ClearContent();
            _rowPlates.Clear();

            int wantCount = _vm.Items.Count;
            var listRoot = BuildScrollContent();

            // WO-1584 - NO ROW WITHOUT A LABEL. A row whose Name is empty paints as a bare plate the
            // player cannot read (the owner's 2026-09-07 frame). The VM already refuses to emit one;
            // this is the View's half of the same law, so a future producer cannot reintroduce it
            // silently. Dropped rows are FAILED by name (§12) and `wantCount` deliberately stays the
            // VM's count, so the "wanted N built M" seam below still reports the loss honestly.
            var paintable = new List<ItemVM>(_vm.Items.Count);
            for (int i = 0; i < _vm.Items.Count; i++)
            {
                var it = _vm.Items[i];
                if (string.IsNullOrEmpty(it.Name))
                {
                    FlowTrace.Fail("Store",
                        $"PartyShop row {i} id='{(string.IsNullOrEmpty(it.Id) ? "<none>" : it.Id)}' has an EMPTY " +
                        "label - SKIPPED rather than painted as an unreadable blank plate.");
                    continue;
                }
                paintable.Add(it);
            }

            // Guard EACH row so one bad ItemVM is logged + skipped, never aborting the whole list
            // (the "blank party-shop tab" class, WO-412/406).
            var (built, failed) = Guard.TryEach("Store", "build party-shop row", paintable,
                item => CreateRow(listRoot, item));

            // STOCKED-N COMMIT SEAM: rows offered vs built - splits data-empty from built-but-broken.
            FlowTrace.Step("Store",
                $"PartyShop stocked {built} row(s) (wanted {wantCount}, failed {failed}).");

            // VERIFY rows>0: show a VISIBLE empty-state row instead of a blank panel.
            if (built == 0)
            {
                if (wantCount == 0)
                    FlowTrace.Warn("Store",
                        $"PartyShop has NO items for tab {_vm.Tab} - showing empty-state row (data-empty).");
                else
                    FlowTrace.Fail("Store",
                        $"PartyShop had {wantCount} item(s) but built 0 rows ({failed} failed) - showing empty-state row (built-but-broken).");
                // WO-598: the BUY empty state is the vendor's AUTHORED emptyLine (vendors.json)
                // - never the raw "No wares in stock." (flag_11). SELL keeps a neutral line.
                CreateEmptyStateRow(listRoot, _vm.Tab == PartyShopTab.Sell
                    ? "Nothing to sell."
                    : (!string.IsNullOrEmpty(_vm.EmptyLine) ? _vm.EmptyLine : "No wares in stock."));
            }
            else if (!string.IsNullOrEmpty(_vm.FooterLine))
            {
                // WO-860 B4 / WEAPONS_DEEP_DIVE §3(e): the shelf HAS rows but perLevelCap thinned
                // it, so the vendor's authored "come back after you level up" line explains the
                // short list. The VM already decided this (FooterLine is null on a FULL shelf and
                // null on an EMPTY one) — the View only renders. DISPLAY-ONLY: no button, no
                // raycast target, so MinTouchPx does not apply.
                CreateFooterNoteRow(listRoot, _vm.FooterLine);
            }

            FinalizeScroll();
        }

        // A single visible row carrying the empty-state copy - the never-blank fallback.
        private void CreateEmptyStateRow(Transform parent, string msg)
        {
            var go = new GameObject("EmptyStateRow", typeof(TMPro.TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeightPx;
            le.minHeight = RowHeightPx;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);
            t.text = msg;
            t.fontSize = ElarionUi.FontLabel;
            t.color = ElarionUi.ParchmentDim;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.raycastTarget = false;
            ElarionUiKit.FitSingleLine(t);   // flag_06: authored empty-lines fit the row
        }

        // WO-860 B4: the capped-shelf FOOTER note — a display-only line that sits UNDER the last
        // item row. It is deliberately shaped so it can NEVER be mistaken for a purchasable row
        // and never read as an error, WITHOUT relying on colour (the owner is red/green
        // colourblind — CLAUDE.md / owner-colorblind memory). Three non-hue cues carry it:
        //   POSITION — always last, below every item row;
        //   SHAPE    — no row plate, no icon, no price column, centred + italic (item rows are
        //              left-aligned text on a plate with a right-hand price);
        //   TEXT     — the vendor's authored sentence, which states the reason in words.
        // Wrapped (not FitSingleLine'd) because the authored copy is a full sentence; shrinking
        // it to one line is what would make it unreadable on a phone.
        private void CreateFooterNoteRow(Transform parent, string msg)
        {
            var go = new GameObject("ShelfFooterNote", typeof(TMPro.TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = FooterNoteHeightPx;
            le.minHeight = FooterNoteHeightPx;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);
            t.text = "— " + msg;          // em-dash lead-in: a NOTE marker, not a shop row
            t.fontSize = ElarionUi.FontMicro;
            t.fontStyle = TMPro.FontStyles.Italic;
            t.color = ElarionUi.ParchmentDim;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.textWrappingMode = TMPro.TextWrappingModes.Normal;
            // WO-1877: Truncate clipped the armorer "come back after you level up" sentence mid-word
            // on the live Seeker frame; overflow so the wrapped copy stays readable.
            t.overflowMode = TMPro.TextOverflowModes.Overflow;
            t.raycastTarget = false;      // display-only: never eats a tap meant for a row
            FlowTrace.Step("Store",
                $"PartyShop rendered capped-shelf FOOTER note under the list: \"{msg}\"");
        }

        // Three lines of FontMicro plus breathing room — the authored footer copy is a sentence.
        private const float FooterNoteHeightPx = 96f;

        // =====================================================================
        // WO-1584 - ADAPTIVE COLUMN BAND (the real cause of the "blank first row"
        // and of the highlight disagreeing with the detail column).
        // ---------------------------------------------------------------------
        // The list column was pinned at body-y 0.36-0.525 - 16.5% of the body. Measured against
        // the owner's 2026-09-07 Seeker frame (2670x1200; CanvasScaler 1080x1920 match 0.5 ->
        // scale 1.243; the frame's inner body reads ~930 device px ~= 748 canvas units) that band
        // is ~123 canvas units, while the TWO rows the sell list built need
        // 2*RowHeightPx(56) + RowGapPx(4) + 2*ListBasePadPx(4) = 124. Content exceeded the
        // viewport by about ONE UNIT, so the mask clipped a row down to the bottom-edge sliver the
        // owner read as an empty row - and the SELECTED row (Iron Scrap, which the detail column
        // correctly named) was the one outside the viewport. `git log -L` names the change:
        // 486cd7b17 (2026-09-01) took the band from 0.23-0.645 to 0.36-0.525 while raising the
        // action buttons to 0.22-0.38.
        //
        // The fix is not a bigger constant - it is that the band is not a constant. On a GOODS or
        // JEWELER vendor the party bar, member header, BUY/SELL strip, category bar and type bar
        // are ALL hidden (the Market is a counter, not a paper-doll), so the top ~35% of the panel
        // is dead black while the list suffocates. Each column now claims the height that is
        // actually free above it, measured from which bands are ACTIVE - never from the layout
        // enum, because the tab strip follows TabsLocked and the type bar follows the live chip
        // count. A gear vendor with every band up lands on the same band it has today, so this
        // cannot regress the Forge/Armorer.
        private const float ColumnBottomY = 0.40f;   // actual reseat authority: clears action-row top 0.38
        private const float ColumnTopCeil = 0.88f;   // under the wallet chip (0.905)
        private const float BandGapY      = 0.015f;

        private void ReseatColumns()
        {
            // Bands that sit over the LEFT (list) column, with the body-y each one occupies down to.
            float listTop = LowestActiveBandBottom(
                (_partyBar, 0.74f),
                (_memberLabel != null ? _memberLabel.gameObject : null, 0.685f),
                (_categoryBar, 0.605f),
                (_typeBar, 0.535f));

            // Bands that sit over the RIGHT (preview) column: only the party bar (full width) and
            // the BUY/SELL strip (x 0.64-0.96) reach it - the category/type bars are left-half only.
            float previewTop = LowestActiveBandBottom(
                (_partyBar, 0.74f),
                (_tabBar, 0.675f));

            SeatBand(_contentRoot != null ? _contentRoot.GetComponent<RectTransform>() : null,
                     0.04f, 0.52f, listTop, "list");
            SeatBand(_previewRoot != null ? _previewRoot.GetComponent<RectTransform>() : null,
                     0.54f, 0.96f, previewTop, "preview");
        }

        // The bottom edge of the LOWEST band that is currently active, minus a breathing gap;
        // ColumnTopCeil when every band is hidden. Reads activeSelf, never the vendor layout.
        private static float LowestActiveBandBottom(params (GameObject go, float bottomY)[] bands)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < bands.Length; i++)
            {
                var go = bands[i].go;
                if (go == null || !go.activeSelf) continue;
                if (bands[i].bottomY < lowest) lowest = bands[i].bottomY;
            }
            if (lowest == float.MaxValue) return ColumnTopCeil;
            return Mathf.Clamp(lowest - BandGapY, ColumnBottomY + 0.05f, ColumnTopCeil);
        }

        private static void SeatBand(RectTransform rt, float x0, float x1, float topY, string tag)
        {
            if (rt == null) return;
            var min = new Vector2(x0, ColumnBottomY);
            var max = new Vector2(x1, topY);
            if (rt.anchorMin == min && rt.anchorMax == max) return;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            FlowTrace.Step("Store",
                $"PartyShop reseat {tag} column -> body-y {ColumnBottomY:0.###}..{topY:0.###} " +
                $"({(topY - ColumnBottomY) * 100f:0.#}% of body).");
        }

        // WO-1584 - the VM's SelectedId is the ONE truth for the highlight (HighlightSelectedRow
        // already reads only it, and always has). What broke was that the lit row could sit OUTSIDE
        // the viewport, so the player saw a detail column naming an item no visible row was lit for.
        // A taller band makes that rare; scrolling the selected row into view makes it impossible,
        // whatever the row count. No-op when the content fits.
        private void ScrollSelectedIntoView()
        {
            if (_vm == null || _scrollZone == null || _scrollZone.scroll == null) return;
            var content = _scrollZone.content;
            var viewport = _scrollZone.viewport;
            if (content == null || viewport == null) return;

            string sel = _vm.SelectedId;
            if (string.IsNullOrEmpty(sel)) return;

            RectTransform target = null;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (child == null) continue;
                if (child.name == "BuyRow_" + sel || child.name == "SellRow_" + sel)
                { target = child as RectTransform; break; }
            }
            if (target == null) return;

            Canvas.ForceUpdateCanvases();
            float contentH = content.rect.height;
            float viewH = viewport.rect.height;
            if (contentH <= viewH + 1f)
            {
                FlowTrace.Step("Store",
                    $"PartyShop selected row '{sel}' is in view (content {contentH:0.#} <= viewport {viewH:0.#}).");
                return;   // everything fits; nothing can be off-screen
            }

            // Content is top-pivoted: the row's distance below the content top, centred in the view.
            float rowTop = -target.anchoredPosition.y + target.rect.height * target.pivot.y;
            float want = Mathf.Clamp(rowTop - (viewH - target.rect.height) * 0.5f, 0f, contentH - viewH);
            float normalized = 1f - (want / Mathf.Max(1f, contentH - viewH));
            _scrollZone.scroll.verticalNormalizedPosition = Mathf.Clamp01(normalized);
            FlowTrace.Step("Store",
                $"PartyShop scrolled SELECTED row '{sel}' into view (content {contentH:0.#} > viewport {viewH:0.#}, " +
                $"normalized {_scrollZone.scroll.verticalNormalizedPosition:0.###}).");
        }

        private void HighlightSelectedRow()
        {
            if (_vm == null) return;
            string sel = _vm.SelectedId;
            for (int i = 0; i < _rowPlates.Count; i++)
            {
                var plate = _rowPlates[i].plate;
                if (plate == null) continue;
                DressRowPlate(plate);
                if (_rowPlates[i].locked) DimPlate(plate);   // keep locked rows greyed after the re-dress
                if (sel != null && _rowPlates[i].id == sel)
                {
                    var c = plate.color;
                    plate.color = new Color(c.r * RowSelectedTint.r, c.g * RowSelectedTint.g, c.b * RowSelectedTint.b, c.a);
                }
            }
        }

        // Grey a locked (ineligible-for-this-member) row plate so it reads as non-purchasable.
        private static void DimPlate(Image plate)
        {
            if (plate == null) return;
            var c = plate.color;
            plate.color = new Color(c.r, c.g, c.b, c.a * LockedRowAlpha);
        }

        // flag_06 (owner F8 2026-07-06): rows were 44px but carry FontBody(50) names — the
        // oversized text painted past each row onto its neighbours ("Requires Lv" stacked over
        // item names). Row height now fits the text ladder (name auto-sizes 30..50 inside it).
        private const float RowHeightPx = 56f;
        private const float RowGapPx    = 4f;

        private Transform BuildScrollContent()
        {
            var well = ElarionUiKit.Well(_contentRoot.transform, Vector2.zero, Vector2.one);
            var wImg = well.GetComponent<Image>();
            if (wImg != null)
            {
                wImg.raycastTarget = false;
                if (DeNelle.Core.FeatureFlags.BlinkChrome) { var c = wImg.color; c.a = 0f; wImg.color = c; }
            }

            // flag_06 t=209 ("i need scrollable area on all menus"): the list zone is now a KIT
            // scroll zone (§1.14 MakeScrollZone) — vertical-only, clamped (no elastic), masked,
            // auto-hiding scrollbar. One call; the hand-rolled viewport plumbing is gone.
            var zone = ElarionUiKit.MakeScrollZone(_contentRoot.transform, RowGapPx, ListBasePadPx);
            _scrollZone = zone;
            _scrollContent = zone.content;
            return zone.content.transform;
        }

        private void FinalizeScroll()
        {
            if (_scrollContent == null) return;
            Canvas.ForceUpdateCanvases();
            var contentArea = _contentRoot != null ? _contentRoot.transform as RectTransform : null;
            if (contentArea != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentArea);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
            CenterShortList();
            // §12 layout oracle: capture the real child rects after every rebuild so a
            // renders-empty screenshot is diagnosable from the log, not re-theorized.
            ElarionUiKit.DumpZoneLayout(_contentRoot != null ? _contentRoot.transform : null, "PartyShop.list");
        }

        // The base top/bottom padding MakeScrollZone was built with (BuildScrollContent).
        private const int ListBasePadPx = 4;

        // WO-840 B4: the roster-filtered V1 stock is small (2-3 rows) inside a tall column,
        // so the list read as mostly-empty black (owner capture 2026-08-02). LAYOUT-ONLY fix
        // (the roster/stock filter is untouched by design): when the stacked rows are SHORTER
        // than the viewport, split the slack into the layout group's top/bottom padding so the
        // rows sit vertically centred; content taller than the zone keeps the plain
        // top-anchored scroll (padding restored to the base).
        private void CenterShortList()
        {
            if (_scrollContent == null) return;
            var vlg = _scrollContent.GetComponent<VerticalLayoutGroup>();
            var viewport = _scrollContent.parent as RectTransform;
            if (vlg == null || viewport == null) return;

            float rowsH = 0f;
            int rows = 0;
            for (int i = 0; i < _scrollContent.childCount; i++)
            {
                var le = _scrollContent.GetChild(i).GetComponent<LayoutElement>();
                if (le == null) continue;
                rowsH += Mathf.Max(le.preferredHeight, le.minHeight);
                rows++;
            }
            if (rows > 1) rowsH += (rows - 1) * RowGapPx;

            float slack = viewport.rect.height - (rowsH + ListBasePadPx * 2f);
            int pad = slack > 0f ? ListBasePadPx + Mathf.FloorToInt(slack * 0.5f) : ListBasePadPx;
            if (vlg.padding.top == pad && vlg.padding.bottom == pad) return;
            vlg.padding = new RectOffset(ListBasePadPx, ListBasePadPx, pad, pad);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        }

        // Slim list row (WO-501 / WO-1877): plate + LEFT thumb of the piece's 2D art + NAME.
        // Stats/delta/price/action live in the preview pane beside the list. Tapping the row
        // inspects it -> _vm.Select(id) -> Render -> RenderPreview.
        private void CreateRow(Transform parent, ItemVM item)
        {
            bool isSell = _vm != null && _vm.Tab == PartyShopTab.Sell;
            bool locked = item.Locked;
            bool hasReason = locked && !string.IsNullOrEmpty(item.LockReason);

            var row = new GameObject((isSell ? "SellRow_" : "BuyRow_") + item.Id,
                typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var le = row.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeightPx;
            le.minHeight = RowHeightPx;

            var rowImg = row.GetComponent<Image>();
            DressRowPlate(rowImg);
            if (locked) DimPlate(rowImg);   // greyed non-purchasable row (re-applied in HighlightSelectedRow)
            _rowPlates.Add((item.Id, rowImg, locked));

            var rowBtn = row.GetComponent<Button>();
            rowBtn.targetGraphic = rowImg;
            ElarionUiKit.StyleButtonColors(rowBtn);
            string id = item.Id;
            // Tap the ROW = inspect (hold-select) -> the preview + action bar follow the selection.
            // Locked rows are still selectable so the player can preview stats + see the requirement.
            rowBtn.onClick.AddListener(() => _vm?.Select(id));

            // WO-1877: left thumb of the same ResolveItemSprite plate the preview well uses.
            // Name shifts right of the thumb so rows are not name-only bars.
            var detail = _vm != null ? _vm.DetailFor(item.Id) : null;
            var thumbSprite = ResolveItemSprite(detail, item);
            float nameX0 = 0.04f;
            if (thumbSprite != null)
            {
                var thumbGo = ElarionUiKit.AddImage(row.transform, "RowThumb",
                    new Vector2(0.02f, 0.08f), new Vector2(0.16f, 0.92f), Color.white, rounded: false);
                var thumbImg = thumbGo.GetComponent<Image>();
                thumbImg.sprite = thumbSprite;
                thumbImg.preserveAspect = true;
                thumbImg.raycastTarget = false;
                if (locked)
                {
                    var c = thumbImg.color;
                    thumbImg.color = new Color(c.r, c.g, c.b, c.a * LockedRowAlpha);
                }
                nameX0 = 0.18f;
            }

            // Name - locked rows dimmed; equipped names bold + gilt so the player sees what is worn.
            // A locked row shrinks the name column to make room for the right-aligned "requires" hint.
            Color nameColor = locked ? ElarionUi.ParchmentDim
                            : (item.Equipped ? ElarionUi.Gilt : ElarionUi.Parchment);
            // Orchestrator capture 2026-07-06 #2 ("Wanderer's Cl…" / "Chainm…" / "Req…"): the
            // 0.06-0.56 name column + the 30px floor ellipsized most names. Rebalanced: wider
            // name column (0.04-0.66; the hint is short-form now), a lower font cap (FontLabel)
            // so auto-size has range, and an explicit 22px min (still legible at desktop scale)
            // before the ellipsis kicks in — full names fit at the captured window size.
            // WO-808: an owned, improved piece carries a right-aligned "Lv N" chip (same
            // ASCII-bracket, dim-parchment recipe as the lock hint — shape + luminance,
            // never hue). Owned rows are never Locked, so the two chips never collide.
            bool hasLevelChip = !locked && item.Level > 1;
            float nameX1 = (hasReason || hasLevelChip) ? 0.66f : 0.96f;
            var nameLbl = ElarionUiKit.Label(row.transform, item.Name,
                0.0f, 1f, nameColor,
                ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left, nameX0, nameX1,
                bold: item.Equipped && !locked);
            ElarionUiKit.FitSingleLine(nameLbl, 22f, ElarionUi.FontLabel);

            if (hasLevelChip)
            {
                var lvLbl = ElarionUiKit.Label(row.transform, LocalText.Format("village.party_shop.level_badge", item.Level),
                    0.0f, 1f, ElarionUi.Gilt,
                    ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Right, 0.68f, 0.96f, bold: true);
                ElarionUiKit.FitSingleLine(lvLbl, 20f, ElarionUi.FontMicro);
            }

            // Lock reason hint, right-aligned on locked rows. Short-form for the slim column:
            // "Requires Lv 10" -> "Lv 10" (composed in ShopCatalog.LockReason; the action bar
            // keeps the long form — this is row-display formatting only, view-local).
            // COLORBLIND CANON (round-3 #3 — owner is red/green colorblind, meaning is NEVER
            // hue-only): the lock cue is carried by SHAPE + LUMINANCE, not color — ASCII
            // brackets "[Lv 6]" mark the requirement, the label renders DIM parchment (not red),
            // and the whole locked row is already luminance-dimmed (DimPlate 0.45 + dim name).
            if (hasReason)
            {
                string reason = item.LockReason.StartsWith("Requires Lv ")
                    ? "Lv " + item.LockReason.Substring("Requires Lv ".Length)
                    : item.LockReason;
                var reasonLbl = ElarionUiKit.Label(row.transform, "[" + reason + "]",
                    0.0f, 1f, ElarionUi.ParchmentDim,
                    ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Right, 0.68f, 0.96f, bold: false);
                ElarionUiKit.FitSingleLine(reasonLbl, 20f, ElarionUi.FontMicro);
            }
        }

        // -- 3D RENDER PREVIEW pane (WO-501 owner point 3) ----------------------------
        // A well beside the slim list holding, top->bottom: the 3D render (RawImage), a 2D
        // sprite/glyph fallback in the same square, the name, the stat line, the colored delta,
        // and a LARGE price. Built once in BuildChrome; repainted per Render via RenderPreview.
        private void BuildPreviewPane(Transform panel)
        {
            // PORTRAIT-CONFORM (owner 2026-07-04): align the preview column to the SAME tall band as
            // the item list (0.12→0.645) so the two columns read as side-by-side portrait columns
            // under the filter stack, instead of a short-and-wide landscape pane. Its internal
            // square/specs/price are pane-relative, so they scale with the taller/narrower column.
            _previewRoot = ElarionUiKit.Well(panel, new Vector2(0.54f, ColumnBottomY), new Vector2(0.96f, 0.67f));
            var wImg = _previewRoot.GetComponent<Image>();
            if (wImg != null)
            {
                wImg.raycastTarget = false;
                if (DeNelle.Core.FeatureFlags.BlinkChrome) { var c = wImg.color; c.a = 0f; wImg.color = c; }
            }
            var pane = _previewRoot.transform;

            // FRAMED BACKING CARD (behind the icon/3D square): a dark slate plate so a transparent or
            // baked-grey icon sits on a neutral premium backing, never the gold store panel / a checker.
            // Padded slightly past the icon square so it reads as a frame; reuses the pack's slot plate
            // sprite (premium frame) when present, else a rounded dark slate fill.
            var backMin = new Vector2(PreviewSquareMin.x - PreviewBackingPad, PreviewSquareMin.y - PreviewBackingPad);
            var backMax = new Vector2(PreviewSquareMax.x + PreviewBackingPad, PreviewSquareMax.y + PreviewBackingPad);
            var backGo = ElarionUiKit.AddImage(pane, "PreviewBacking", backMin, backMax, PreviewBackingColor, rounded: true);
            _previewBacking = backGo.GetComponent<Image>();
            if (_previewBacking != null)
            {
                _previewBacking.raycastTarget = false;
                var plate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotItem);
                if (plate != null)
                {
                    _previewBacking.sprite = plate;
                    _previewBacking.type   = Image.Type.Sliced;
                    _previewBacking.color  = PreviewBackingColor;
                }
            }
            ElarionUiKit.AddInnerRim(backGo, new Color(0f, 0f, 0f, 0.40f));
            backGo.transform.SetAsFirstSibling();   // keep it BEHIND every preview widget

            // The 3D render image (square, top of the pane). Fed by the live RenderTexture in RenderPreview.
            var imgGo = new GameObject("PreviewImage", typeof(RawImage));
            imgGo.transform.SetParent(pane, false);
            var ir = imgGo.GetComponent<RectTransform>();
            ir.anchorMin = PreviewSquareMin; ir.anchorMax = PreviewSquareMax;
            ir.offsetMin = Vector2.zero; ir.offsetMax = Vector2.zero;
            _previewImage = imgGo.GetComponent<RawImage>();
            _previewImage.color = Color.white;
            _previewImage.raycastTarget = false;

            // 2D fallback sprite in the SAME square (shown when no 3D model resolves).
            var spriteGo = ElarionUiKit.AddImage(pane, "PreviewSprite",
                PreviewSquareMin, PreviewSquareMax, new Color(0f, 0f, 0f, 0f), rounded: false);
            _previewSprite = spriteGo.GetComponent<Image>();
            _previewSprite.raycastTarget = false;
            _previewSprite.preserveAspect = true;

            // 2D emoji/glyph fallback over the square (last-resort never-blank).
            _previewGlyph = ElarionUiKit.Label(pane, "", PreviewSquareMin.y, PreviewSquareMax.y, ElarionUi.Parchment,
                ElarionUi.FontTitle, TMPro.TextAlignmentOptions.Center, PreviewSquareMin.x, PreviewSquareMax.x, bold: true);

            // Empty state (nothing selected).
            _previewEmpty = ElarionUiKit.Label(pane, "Select an item to preview.", 0.50f, 0.62f,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f);

            // Name (gilt bold) — under the plate.
            _previewName = ElarionUiKit.Label(pane, "", 0.38f, 0.50f, ElarionUi.Gilt,
                ElarionUi.FontHead, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
            ElarionUiKit.FitSingleLine(_previewName);   // flag_06: long gear names ellipsize in the pane

            // Flavour line (rarity + class fit) - the readable desc under the name.
            _previewStats = ElarionUiKit.Label(pane, "", 0.30f, 0.38f, ElarionUi.ParchmentDim,
                ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f);
            ElarionUiKit.FitSingleLine(_previewStats, 0f, ElarionUi.FontMicro);

            // -- Per-stat SPEC block (WO: "make weapons matter") - one line per spec, top->bottom:
            // "<Label>   <Value> (<Delta>)" with the delta tinted green(up)/red(down)/dim(same). Built
            // here as a vertical container; rebuilt per Render from _vm.SelectedSpecs. Sits between the
            // flavour line and the price. --
            // flag_06: RectMask2D so overflowing spec rows CLIP inside their block instead of
            // painting over the price below (text never overlaps siblings — §1.14 law).
            // WO-1877: taller band so Defense + HP (+ deltas) both stay visible on phone.
            var specsGo = new GameObject("PreviewSpecs", typeof(RectTransform), typeof(RectMask2D));
            specsGo.transform.SetParent(pane, false);
            _previewSpecs = specsGo.GetComponent<RectTransform>();
            _previewSpecs.anchorMin = new Vector2(0.06f, 0.02f); _previewSpecs.anchorMax = new Vector2(0.94f, 0.29f);
            _previewSpecs.offsetMin = Vector2.zero; _previewSpecs.offsetMax = Vector2.zero;
            var specsVlg = specsGo.AddComponent<VerticalLayoutGroup>();
            specsVlg.childAlignment = TextAnchor.UpperCenter;
            specsVlg.spacing = 2f;
            specsVlg.childControlWidth = true;
            specsVlg.childControlHeight = true;
            specsVlg.childForceExpandWidth = true;
            specsVlg.childForceExpandHeight = false;

            // Summary delta line is folded into the per-stat block now; keep the field null so the
            // legacy single-line path is inert (no double-rendering of the delta).
            _previewDelta = null;

            // PRICE - large + readable (WO-501 owner point 3).
            _previewPrice = ElarionUiKit.Label(pane, "", 0.04f, 0.18f, ElarionUi.Gilt,
                ElarionUi.FontTitle, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
            ElarionUiKit.FitSingleLine(_previewPrice);   // flag_06: "Purchase 1,250 Gold" never spills the pane
            _previewPrice.gameObject.SetActive(false);   // action CTA already carries authoritative price; no duplicate summary
        }

        // Repaint the preview pane from _vm.Selected ONLY (name/stats/delta/price) + rebuild the 3D
        // model (or 2D fallback) for the selected id. Never-blank: 3D model -> 2D sprite -> emoji glyph.
        private void RenderPreview()
        {
            if (_vm == null || _previewRoot == null) return;
            var sel = _vm.Selected;

            if (!sel.HasValue)
            {
                // Nothing selected - clear + show the empty state, tear the rig down.
                TeardownRig();
                if (_previewBacking != null) _previewBacking.enabled = false;   // no card behind an empty preview
                if (_previewImage != null) _previewImage.enabled = false;
                if (_previewSprite != null) _previewSprite.color = new Color(0f, 0f, 0f, 0f);
                if (_previewGlyph != null) _previewGlyph.text = "";
                if (_previewName != null) _previewName.text = "";
                if (_previewStats != null) _previewStats.text = "";
                if (_previewDelta != null) _previewDelta.text = "";
                ClearSpecs();
                if (_previewPrice != null) _previewPrice.text = "";
                if (_previewEmpty != null) _previewEmpty.gameObject.SetActive(true);
                return;
            }

            if (_previewEmpty != null) _previewEmpty.gameObject.SetActive(false);
            if (_previewBacking != null) _previewBacking.enabled = true;   // framed card behind the icon/3D square
            var d = sel.Value;
            var item = _vm.SelectedItem;

            if (_previewName != null) _previewName.text = item.HasValue ? item.Value.Name : "";
            // Flavour/desc line (rarity + class fit) under the name.
            if (_previewStats != null) _previewStats.text = d.Description ?? "";
            // Legacy single delta line kept inert (folded into the per-stat block).
            if (_previewDelta != null)
            {
                _previewDelta.text = d.Delta ?? "";
                _previewDelta.color = DeltaColor(d.Delta);
            }
            // Per-stat SPEC block: "Label  Value (Delta)" with the delta tinted by sign.
            RebuildSpecs(_vm.SelectedSpecs);
            if (_previewPrice != null)
            {
                _previewPrice.text = _vm.SelectedPriceText;
                // Affordability-colored on BUY; gilt for refund/owned.
                bool sell = _vm.Tab == PartyShopTab.Sell;
                bool affordable = item.HasValue && (item.Value.Price <= 0 || item.Value.Affordable);
                _previewPrice.color = sell ? ElarionUi.Affordable
                                    : (item.HasValue && !affordable ? ElarionUi.Danger : ElarionUi.Gilt);
            }

            BuildPreviewModelOrFallback(_vm.SelectedId, d, item);
        }

        // -- Per-stat SPEC block (WO: "make weapons matter") --------------------------
        // Render one label per spec: "<Label>   <Value> (<Delta>)" with the (Delta) tinted green for an
        // upgrade, red for a downgrade, dim for same - via TMP rich-text so the colored delta sits inline.
        // A spec with no delta (nothing comparable equipped / SELL tab) shows just "<Label>   <Value>".
        private void RebuildSpecs(System.Collections.Generic.IReadOnlyList<PartyShopSpec> specs)
        {
            ClearSpecs();
            if (_previewSpecs == null || specs == null) return;

            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                string line = "<b>" + Esc(s.Label) + "</b>   " + Esc(s.Value);
                if (!string.IsNullOrEmpty(s.Delta))
                {
                    string hex = ColorUtility.ToHtmlStringRGB(DeltaSignColor(s.DeltaSign));
                    line += "  <color=#" + hex + ">(" + Esc(s.Delta) + ")</color>";
                }

                var go = new GameObject("Spec_" + (string.IsNullOrEmpty(s.Label) ? i.ToString() : s.Label),
                    typeof(TMPro.TextMeshProUGUI), typeof(LayoutElement));
                go.transform.SetParent(_previewSpecs, false);
                var le = go.GetComponent<LayoutElement>();
                // flag_06: rows were 20px but carried FontLabel(40) text — every spec line painted
                // over the next. Row height now fits the mobile floor; the label auto-sizes into it.
                le.preferredHeight = 34f;
                le.minHeight = 30f;
                var t = go.GetComponent<TMPro.TextMeshProUGUI>();
                ElarionUiKit.EnsureFont(t);
                t.richText = true;
                t.text = line;
                t.fontSize = ElarionUi.FontLabel;
                t.color = ElarionUi.Parchment;
                t.alignment = TMPro.TextAlignmentOptions.Center;
                t.raycastTarget = false;
                ElarionUiKit.FitSingleLine(t, 0f, ElarionUi.FontLabel);
            }
        }

        private void ClearSpecs()
        {
            if (_previewSpecs == null) return;
            for (int i = _previewSpecs.childCount - 1; i >= 0; i--)
            {
                var c = _previewSpecs.GetChild(i);
                if (c == null) continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
        }

        // Green (better) / red (worse) / dim (same) for a per-stat delta sign.
        private static Color DeltaSignColor(int sign) =>
            sign > 0 ? ElarionUi.Affordable : (sign < 0 ? ElarionUi.Danger : ElarionUi.ParchmentDim);

        // Strip TMP rich-text control chars from VM-supplied text so a stray '<' can't break the markup.
        private static string Esc(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("<", "[").Replace(">", "]");

        // Resolve the selected gear MODEL into the rig, mirroring the equip load path (DRY the heuristic
        // with EquipmentController.LoadsViaAddressable): addressable "gear/" -> Addressables; else Resources
        // via VisualFactory.Skin (the BuildPreviewModal pattern). Falls back to the 2D sprite, then the
        // emoji glyph - never blanks. Logs the chosen branch for headless capture (?12).
        private void BuildPreviewModelOrFallback(string id, PartyShopDetail detail, ItemVM? item)
        {
            if (string.IsNullOrEmpty(id)) { ShowSpriteFallback(detail, item, "no-id"); return; }
            // WO-1877: armor never mesh-swaps the body, so Armorer preview is always the 2D plate
            // (title + stats under it) — never a 3D turntable / addressable armor mesh.
            if (detail.IconRole == PartyShopVM.IconRoleArmor)
            {
                ShowSpriteFallback(detail, item, "armor-2d-plate");
                return;
            }
            // Preview MODEL descriptor comes from the VM (PreviewModelFor resolves the gear def's
            // prefabPath + addressable flag) so this View never names GearCatalog. Non-gear rows
            // (goods/jeweler/craftable) come back IsGear=false -> straight to the 2D icon/glyph.
            var previewModel = _vm != null ? _vm.PreviewModelFor(id) : default;
            if (!previewModel.IsGear)
            {
                ShowSpriteFallback(detail, item, "non-gear");
                return;
            }
            if (_rigModelId == id && (_rigVisual != null || _rigHandleOpen)) return;   // already mounted

            string prefabPath = previewModel.PrefabPath;
            bool addressable = previewModel.Addressable;

            if (string.IsNullOrEmpty(prefabPath))
            {
                // Most gear today has no prefabPath (GearCatalog "NULL for now") - degrade to 2D.
                ShowSpriteFallback(detail, item, "no-prefab");
                return;
            }

            EnsureRig();
            ClearRigVisual();
            _rigModelId = id;

            if (addressable)
            {
                FlowTrace.Step("Store", $"preview model id={id} branch=addressable path={prefabPath}");
                BeginAddressablePreview(prefabPath, id);
            }
            else
            {
                FlowTrace.Step("Store", $"preview model id={id} branch=resources path={prefabPath}");
                GameObject skinned = null;
                Guard.Try("Store", $"skin preview model '{prefabPath}'",
                    () => skinned = VisualFactory.Skin(_rigVisual.transform, prefabPath, SkinOptions.Prop(2.5f)));
                if (skinned == null)
                {
                    ShowSpriteFallback(detail, item, "resources-miss");
                    return;
                }
                ShowModel();
            }
        }

        // (Weapon/ArmorLoadsViaAddressable MOVED to PartyShopVM.PreviewModelFor — the gear-def read
        //  left the View for the VM per strict-MVVM. The View now consumes PartyShopPreviewModel.)

        // Async Addressables load (mirror BeginAddressableEquip): guarded, stale-checked via _rigGeneration,
        // released on swap/close. On any miss, fall back to the 2D sprite so the preview never blanks.
        private void BeginAddressablePreview(string address, string id)
        {
            int generation = ++_rigGeneration;
            AsyncOperationHandle<GameObject> handle;
            try { handle = Addressables.LoadAssetAsync<GameObject>(address); }
            catch (System.Exception ex)
            {
                FlowTrace.Fail("Store", $"preview addressable load threw for '{address}': {ex.Message} - 2D fallback.");
                ShowSpriteFallback(_vm != null ? _vm.Selected ?? default : default,
                                   _vm != null ? _vm.SelectedItem : null, "addr-threw");
                return;
            }
            _rigHandle = handle;
            _rigHandleOpen = true;
            handle.Completed += op =>
            {
                if (generation != _rigGeneration) return;   // a newer select superseded this load
                if (!op.IsValid() || op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    FlowTrace.Fail("Store", $"preview addressable FAILED for '{address}' (status={op.Status}) - 2D fallback.");
                    ShowSpriteFallback(_vm != null ? _vm.Selected ?? default : default,
                                       _vm != null ? _vm.SelectedItem : null, "addr-miss");
                    return;
                }
                if (_rigVisual == null) return;   // panel closed mid-flight
                GameObject skinned = null;
                Guard.Try("Store", $"skin addressable preview '{address}'",
                    () => skinned = VisualFactory.Skin(_rigVisual.transform, op.Result, SkinOptions.Prop(2.5f)));
                if (skinned == null)
                {
                    ShowSpriteFallback(_vm != null ? _vm.Selected ?? default : default,
                                       _vm != null ? _vm.SelectedItem : null, "addr-skin-null");
                    return;
                }
                ShowModel();
            };
        }

        // Show the 3D render (hide the 2D fallback), framing the camera on the rig.
        private void ShowModel()
        {
            FrameRig();
            if (_previewImage != null) { _previewImage.enabled = true; _previewImage.texture = _rigRt; }
            if (_previewSprite != null) _previewSprite.color = new Color(0f, 0f, 0f, 0f);
            if (_previewGlyph != null) _previewGlyph.text = "";
        }

        // Hide the 3D render, draw the 2D sprite (else the emoji glyph). Never blanks.
        private void ShowSpriteFallback(PartyShopDetail detail, ItemVM? item, string why)
        {
            FlowTrace.Step("Store", $"preview model id={_vm?.SelectedId} branch=sprite ({why})");
            TeardownRig();
            if (_previewImage != null) _previewImage.enabled = false;

            var sprite = ResolveItemSprite(detail, item ?? default);
            if (sprite != null && _previewSprite != null)
            {
                _previewSprite.sprite = sprite;
                _previewSprite.color = Color.white;
                if (_previewGlyph != null) _previewGlyph.text = "";
            }
            else
            {
                if (_previewSprite != null) _previewSprite.color = new Color(0f, 0f, 0f, 0f);
                if (_previewGlyph != null)
                    // WO-1584: the row's OWN authored glyph when the catalog gave it one
                    // ("=" for Iron Scrap), else the coarse role glyph. Data before guess.
                    _previewGlyph.text = !string.IsNullOrEmpty(detail.Glyph)
                        ? detail.Glyph
                        : GlyphForRole(detail.IconRole);
            }
        }

        // Never-blank ASCII glyph per icon role (WO-598 added the goods/jeweler bands).
        private static string GlyphForRole(string role)
        {
            switch (role)
            {
                case PartyShopVM.IconRoleArmor:     return "[]";
                case PartyShopVM.IconRolePotion:    return "!";
                case PartyShopVM.IconRoleMaterial:  return "*";
                case PartyShopVM.IconRoleGem:       return "+";
                case PartyShopVM.IconRoleAccessory: return "o";
                default:                            return "/";
            }
        }

        // -- The isolated runtime preview rig (BuildPreviewModal pattern) -------------
        private void EnsureRig()
        {
            if (_rigRt == null)
            {
                _rigRt = new RenderTexture(RtSize, RtSize, 16, RenderTextureFormat.ARGB32);
                _rigRt.Create();
            }
            if (_rigRoot != null) return;

            _rigRoot = new GameObject("PartyShopPreviewRoot");
            _rigRoot.transform.position = new Vector3(0f, -5000f, 0f);

            var light1 = new GameObject("PreviewLight1").AddComponent<Light>();
            light1.transform.SetParent(_rigRoot.transform, false);
            light1.transform.localPosition = new Vector3(2, 3, -2);
            light1.type = LightType.Directional;
            light1.color = new Color(0.9f, 0.9f, 0.95f);
            light1.intensity = 0.9f;
            light1.cullingMask = 1 << PreviewLayer;

            var light2 = new GameObject("PreviewLight2").AddComponent<Light>();
            light2.transform.SetParent(_rigRoot.transform, false);
            light2.transform.localPosition = new Vector3(-2, 2, 2);
            light2.type = LightType.Directional;
            light2.color = new Color(0.6f, 0.65f, 0.7f);
            light2.intensity = 0.5f;
            light2.cullingMask = 1 << PreviewLayer;

            _rigVisual = new GameObject("PreviewVisual");
            _rigVisual.transform.SetParent(_rigRoot.transform, false);
            _rigVisual.transform.localPosition = Vector3.zero;

            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(_rigRoot.transform, false);
            _rigCam = camGo.AddComponent<Camera>();
            _rigCam.clearFlags = CameraClearFlags.SolidColor;
            _rigCam.backgroundColor = new Color(0.10f, 0.09f, 0.08f, 0f);
            _rigCam.orthographic = true;
            _rigCam.nearClipPlane = 0.1f;
            _rigCam.farClipPlane = 10000f;
            _rigCam.targetTexture = _rigRt;
            _rigCam.cullingMask = 1 << PreviewLayer;
            // Fleet ticket 2026-07-02 (x52, MainCastle_Hall): URP asset m_MSAA:2 vs this RT's
            // default antiAliasing=1 → "Attachment 0 was created with 1 samples but 2 samples
            // were requested" whenever this rig cam renders. Offscreen preview needs no MSAA —
            // match TowerPreviewCamera / HeroPreviewViewer.
            _rigCam.allowMSAA = false;

            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderType = CameraRenderType.Base;
            urp.renderPostProcessing = false;
            urp.requiresColorOption = CameraOverrideOption.Off;
            urp.requiresDepthOption = CameraOverrideOption.Off;

            SetLayerRecursive(_rigRoot.transform, PreviewLayer);
        }

        // Auto-spin the mounted model for a "wow factor" viewer (optional per WO-501). Cheap; only
        // runs while a model is mounted. The owner can swap this for drag-to-rotate in finesse.
        private void Update()
        {
            if (_rigVisual == null || _rigVisual.transform.childCount == 0) return;
            _rigYaw = Mathf.Repeat(_rigYaw + Time.deltaTime * 35f, 360f);
            _rigVisual.transform.localRotation = Quaternion.Euler(0f, _rigYaw, 0f);
        }

        private void ClearRigVisual()
        {
            if (_rigVisual == null) return;
            for (int i = _rigVisual.transform.childCount - 1; i >= 0; i--)
            {
                var c = _rigVisual.transform.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }
        }

        // Frame the ortho camera on the rig bounds from a 3/4 angle (mirror FrameCameraOnRig).
        private void FrameRig()
        {
            if (_rigRoot == null || _rigCam == null) return;
            SetLayerRecursive(_rigRoot.transform, PreviewLayer);
            Bounds b;
            var rends = _rigVisual != null ? _rigVisual.GetComponentsInChildren<Renderer>() : null;
            if (rends != null && rends.Length > 0)
            {
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            }
            else
            {
                b = new Bounds(_rigRoot.transform.position, Vector3.one * 2f);
            }
            float radius = Mathf.Max(0.5f, b.extents.magnitude);
            Vector3 dir = new Vector3(1f, 0.9f, -1f).normalized;
            _rigCam.transform.position = b.center + dir * (radius * 2.5f);
            _rigCam.transform.LookAt(b.center);
            _rigCam.orthographicSize = radius * 1.15f;
        }

        private void ReleaseRigHandle()
        {
            if (_rigHandleOpen && _rigHandle.IsValid())
            {
                Addressables.Release(_rigHandle);
            }
            _rigHandle = default;
            _rigHandleOpen = false;
        }

        private void TeardownRig()
        {
            _rigGeneration++;   // invalidate any in-flight addressable load
            ReleaseRigHandle();
            if (_rigRoot != null) { Destroy(_rigRoot); _rigRoot = null; }
            _rigVisual = null;
            _rigCam = null;
            if (_rigRt != null) { _rigRt.Release(); Destroy(_rigRt); _rigRt = null; }
            for (int i = 0; i < _rigMaterials.Count; i++)
                if (_rigMaterials[i] != null) Destroy(_rigMaterials[i]);
            _rigMaterials.Clear();
            _rigModelId = null;
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        // -- Bottom action bar state (WO-501 owner point 4) ---------------------------
        private void RenderActionBar()
        {
            if (_vm == null) return;
            bool sell = _vm.Tab == PartyShopTab.Sell;
            var item = _vm.SelectedItem;
            bool hasSel = item.HasValue;

            bool locked = hasSel && item.Value.Locked;

            // Purchase/Sell toggle label + enable.
            if (_buySellLabel != null)
            {
                string price = _vm.SelectedPriceText;
                if (sell) _buySellLabel.text = hasSel ? "Sell  " + price : "Sell";
                else if (locked)
                    _buySellLabel.text = string.IsNullOrEmpty(item.Value.LockReason)
                        ? "Locked" : item.Value.LockReason;   // e.g. "Requires Lv 5" / "Class: Ranger"
                else _buySellLabel.text = hasSel
                        ? (item.Value.Equipped || item.Value.Price <= 0 ? "Owned" : "Purchase  " + price)
                        : "Purchase";
            }
            if (_buySellBtn != null)
            {
                // Locked items can never be purchased (wrong class / above level) - button disabled.
                bool canBuy = hasSel && !locked && (sell
                    ? true
                    : !(item.Value.Equipped) && (item.Value.Price <= 0 || item.Value.Affordable));
                _buySellBtn.interactable = canBuy;
            }

            // 3-STATE EQUIP CONTROL (owner ruling 2026-07-06):
            //   1. item EQUIPPED       -> "Unequip" (tap unequips via the VM seam)
            //   2. item OWNED, not worn -> "Equip"  (as before)
            //   3. item NOT PURCHASED  -> non-clickable: kit disabledColor plate (0.5 gray,
            //      0.5 alpha — StyleButtonColors) + DIMMED label. The cue is LUMINANCE +
            //      the label text itself, never hue (owner is red/green colorblind).
            //      Purchase stays the actionable button for state 3.
            if (_equipBtn != null)
            {
                bool equipped = hasSel && item.Value.Equipped;
                bool owned    = hasSel && (equipped || item.Value.Price <= 0);   // Price<=0 == owned/free row (VM convention, PriceText "Owned")
                bool canAct   = hasSel && !sell && !locked && owned;
                _equipBtn.interactable = canAct;
                if (_equipLabel != null)
                {
                    _equipLabel.text = equipped ? "Unequip" : "Equip";   // kit FitSingleLine keeps it fitted
                    // Luminance cue on the KIT-assigned ink (contrast law: gold plate = dark Ink) —
                    // dim the same color, never swap to a different hue.
                    _equipLabel.color = canAct ? _equipLabelBase
                        : new Color(_equipLabelBase.r, _equipLabelBase.g, _equipLabelBase.b,
                                    _equipLabelBase.a * 0.55f);
                }
            }

            // WO-808 IMPROVE state: hidden unless the selection is owned gear; enabled only
            // when a next level exists AND the ledger affords it. Same luminance-dim law.
            if (_improveBtn != null)
            {
                bool visible = _vm.ImproveVisible && !sell;
                _improveBtn.gameObject.SetActive(visible);
                if (visible)
                {
                    bool canImprove = _vm.ImproveEnabled;
                    _improveBtn.interactable = canImprove;
                    if (_improveLabel != null)
                    {
                        _improveLabel.text = _vm.ImproveLabel;
                        _improveLabel.color = canImprove ? _improveLabelBase
                            : new Color(_improveLabelBase.r, _improveLabelBase.g, _improveLabelBase.b,
                                        _improveLabelBase.a * 0.55f);
                    }
                }
            }
        }

        // Real item sprite from the VM detail: prefer iconPath (the rendered item image), else the
        // ItemIconCatalog art for the def, else the pack glyph, else null (the View draws a glyph).
        // WO-1877: armor/weapon MUST NOT short-circuit to IconShield/IconSword before iconPath —
        // Armorer previews are 2D plates of the piece, not a generic role glyph.
        private static Sprite ResolveItemSprite(PartyShopDetail? detail, ItemVM item)
        {
            string role = detail.HasValue ? detail.Value.IconRole : item.IconRole;
            string iconPath = detail.HasValue ? detail.Value.IconPath : null;
            if (string.IsNullOrEmpty(iconPath)) iconPath = item.IconPath;
            string category = detail.HasValue ? detail.Value.IconCategory : null;

            // WO-1584 - MATERIALS AND GEMS GO THROUGH THE MATERIAL SEAM, NOT THE POTION MAPPER.
            // ItemIconCatalog.ForMaterial(id, iconPath, category) has existed since F8-641 for
            // exactly this question (authored icon first, then a mat_* sheet sprite chosen by the
            // row's authored CATEGORY, never the potion keyword mapper - "HealthHerb" / "Iron
            // Scrap" / "Oil Flask" all keyword-match potion rows). This screen never called it:
            // every material fell to ForConsumable and, missing there, to the role glyph. That is
            // the white "*" over "Iron Scrap x43" on the owner's 2026-09-07 Seeker frame
            // (IronScrap authors iconPath "" and category "metal", so ForMaterial resolves
            // mat_ore). ONE producer (the VM's iconPath+category), ONE resolver (ForMaterial) -
            // a second resolver here is the banned fix.
            if (role == PartyShopVM.IconRoleMaterial || role == PartyShopVM.IconRoleGem)
            {
                var mat = ItemIconCatalog.ForMaterial(item.Id, iconPath, category);
                if (mat != null) return mat;
                FlowTrace.Warn("Store",
                    $"ART MISS id='{item.Id}' role='{role}' iconPath='{(string.IsNullOrEmpty(iconPath) ? "<none>" : iconPath)}' " +
                    $"category='{(string.IsNullOrEmpty(category) ? "<none>" : category)}' -> ForMaterial resolved NO sprite; " +
                    "the row falls back to its authored glyph. This id is an ART ASK, not a code bug.");
                return null;
            }

            if (!string.IsNullOrEmpty(iconPath))
            {
                var authored = Resources.Load<Sprite>(iconPath);
                if (authored != null) return authored;
                FlowTrace.Warn("Store",
                    $"ART MISS id='{item.Id}' role='{(string.IsNullOrEmpty(role) ? "<none>" : role)}' " +
                    $"iconPath='{iconPath}' -> Resources.Load returned null; falling through to catalog/glyph.");
            }

            // Armor / weapon: role glyph is the never-blank LAST resort only (WO-1877). The View
            // does not name GearCatalog here — authored iconPath is the plate authority.
            if (role == PartyShopVM.IconRoleArmor)
                return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconShield);
            if (role == PartyShopVM.IconRoleWeapon)
                return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconSword);

            // Catalog art by def (sprite-first, the same source the legacy details pane used).
            // WO-598 goods/jeweler bands: try the sliced item-icon art by id/name; a miss
            // returns null so the caller draws the role glyph (never a wrong sword icon).
            var consumable = ItemIconCatalog.ForConsumable(item.Id, item.Name);
            if (consumable == null)
                FlowTrace.Warn("Store",
                    $"ART MISS id='{item.Id}' role='{(string.IsNullOrEmpty(role) ? "<none>" : role)}' " +
                    $"iconPath='{(string.IsNullOrEmpty(iconPath) ? "<none>" : iconPath)}' -> glyph fallback.");
            return consumable;
        }

        private static Color DeltaColor(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return ElarionUi.ParchmentDim;
            if (delta.StartsWith("+")) return ElarionUi.Affordable;
            if (delta.StartsWith("=")) return ElarionUi.ParchmentDim;
            return ElarionUi.Danger;   // a negative delta (worse than equipped)
        }

        private static void DressRowPlate(Image rowImg)
        {
            if (rowImg == null) return;
            var medieval = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (medieval != null)
            {
                rowImg.sprite = medieval;
                rowImg.type = Image.Type.Simple;
                rowImg.color = Color.white;
                return;
            }
            if (DeNelle.Core.FeatureFlags.BlinkChrome)
            {
                var plate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotItem);
                if (plate != null)
                {
                    rowImg.sprite = plate;
                    rowImg.type   = Image.Type.Sliced;
                    rowImg.color  = Color.white;
                    return;
                }
            }
            rowImg.color = ElarionUiKit.Cell;
            ElarionUiKit.ApplyRounded(rowImg);
        }

        // CreamTab + PathOf REMOVED (WO-714 W1): every tab/chip/action now builds through
        // ElarionUiKit.BuildTab / BuildObsidianButton — the kit owns label ink (contrast law),
        // bold, and §1.14 text-fit, so the per-screen dress pass has nothing left to dress.
        // Ref-grep proof at removal: zero call sites remained.

        // -- Status toast (WO-714 W1, P5) ------------------------------------------
        // vm.Status is transient feedback — surface each CHANGE as a kit ToastCard that
        // auto-dismisses; the open-time idle hint is baselined (never toasted). One live
        // toast at a time; presentation only.

        private void MaybeToastStatus()
        {
            if (_vm == null) return;
            string s = _vm.Status;
            if (!_statusBaselined)
            {
                _statusBaselined = true;
                _lastStatus = s;
                return;
            }
            if (s == _lastStatus) return;
            _lastStatus = s;
            if (string.IsNullOrEmpty(s) || _ui == null) return;

            if (_toastCard != null) Destroy(_toastCard);
            var parts = ElarionUiKit.ToastCard(_ui.transform, ElarionUiKit.ToastTone.Gold,
                accentLeft: false, TextAnchor.MiddleCenter);
            if (parts == null || parts.card == null) return;
            var rt = parts.card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.30f, 0.79f);
            rt.anchorMax = new Vector2(0.70f, 0.86f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (parts.label != null) parts.label.text = s;
            _toastCard = parts.card;
            if (isActiveAndEnabled) StartCoroutine(DismissToast(parts.card, 2.8f));
        }

        private System.Collections.IEnumerator DismissToast(GameObject go, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (go != null) Destroy(go);
            if (_toastCard == go) _toastCard = null;
        }

        private static string Cap(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // -- Teardown ------------------------------------------------------------

        private void ClearContent()
        {
            _scrollContent = null;
            _scrollZone = null;
            if (_contentRoot == null) return;
            for (int i = _contentRoot.transform.childCount - 1; i >= 0; i--)
            {
                var c = _contentRoot.transform.GetChild(i);
                if (c == null) continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
        }

        private void DisposeViewModel()
        {
            Unbind();
            _vm?.Dispose();
            _vm = null;
            _store?.Dispose();
            _store = null;
            foreach (var a in _targetAdapters) a?.Dispose();
            _targetAdapters.Clear();
        }

        private void Close()
        {
            DisposeViewModel();
            TeardownRig();
            _walletChip = null;
            _tabBuy = null; _tabSell = null;
            _categoryTabs.Clear();
            _typeTabs.Clear();
            _toastCard = null;   // destroyed with _ui below
            _lastStatus = null;
            _statusBaselined = false;
            _headerLabel = null;
            _memberLabel = null;
            _previewRoot = null;
            _previewBacking = null;
            _previewImage = null;
            _previewSprite = null;
            _previewGlyph = null;
            _previewName = null;
            _previewStats = null;
            _previewDelta = null;
            _previewSpecs = null;
            _previewPrice = null;
            _previewEmpty = null;
            _buySellBtn = null;
            _buySellLabel = null;
            _equipBtn = null;
            _equipLabel = null;
            if (_ui != null) Destroy(_ui);
            _ui = null;
            _contentRoot = null;
            _partyBar = null;
            _tabBar = null;
            _categoryBar = null;
            _typeBar = null;
            _scrollContent = null;
            _scrollZone = null;
            _rowPlates.Clear();
            PanelManager.NotifyClosed(_panelHandle);
        }
    }
}
