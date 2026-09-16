// =============================================================================
// CosmeticShopPanel - the in-game Cosmetic Shop UI. Opened from the Hero deck's
// Wardrobe card (PlayerDeckWorkspace, WO-1397) through PanelRouter.Open(
// PanelId.CosmeticShop); the dialogue verb OpenCosmetics (DialogueCommandSink)
// opens the same id. There is NO Marketplace interactable - BuildingType has no
// such member and BuildingInteractable.TryPanelFor has no Cosmetic case; the old
// header claimed one and the shop sat unreachable for every player (UI screen
// graph 2026-09-04, dead end 4). Category tabs (Hero / Pet / Village) + a
// scrollable card list; each card shows the preview, name, description,
// Cosmetics price (with DEF-197 "short by N" honesty) and ONE action button
// (Buy / Equip / Equipped / Locked).
// -----------------------------------------------------------------------------
// WO-F conversion (2026-07-03, coverage matrix row #4): UIDocument/UITK ->
// code-built uGUI on the Obsidian master frame (BuildObsidianModal:
// FrameMerchant + coin medallion + the ONE shared Close + scrim), per the
// HelpMenu reference recipe. Scroll list composed inline (ScrollRect +
// VerticalLayoutGroup) like LeaderboardPanel.
//
// Reflection bridge UNCHANGED: DeNelle.HUD does not reference DeNelle.Cosmetics
// (asmdef isolation). Catalog + service resolved lazily by name; any miss =
// "shop unavailable" so the HUD compiles even with Cosmetics stripped.
//
// Spawned by CosmeticShopPanelBootstrap once a scene has a hero.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class CosmeticShopPanel : MonoBehaviour
    {
        private ElarionUiKit.ObsidianModal _modal;
        private Transform _tabHost;
        private Transform _listContent;      // ScrollRect content
        private TextMeshProUGUI _ownershipLabel;
        private ElarionUiKit.ToastParts _toast;
        private float _toastUntil;
        private string _activeCategory = "hero";
        private bool _open;

        // Reflection handles into DeNelle.Cosmetics. Cached after first hit.
        private static Type s_serviceType;
        private static PropertyInfo s_instanceProp;
        private static PropertyInfo s_ownedProp;
        private static MethodInfo s_ownsMethod;
        private static MethodInfo s_equippedForMethod;
        private static MethodInfo s_tryPurchaseMethod;
        private static MethodInfo s_equipMethod;
        private static EventInfo s_changedEvent;

        private static Type s_catalogType;
        private static MethodInfo s_byCategoryMethod;

        private static Type s_defType;
        private static FieldInfo s_defIdField;
        private static FieldInfo s_defCategoryField;
        private static FieldInfo s_defDisplayNameField;
        private static FieldInfo s_defDescriptionField;
        private static FieldInfo s_defPriceField;
        private static FieldInfo s_defUnlockMethodField;
        private static PropertyInfo s_defPreviewColorProp;

        private object _serviceInstance;
        private Delegate _changedHandler;
        // Modal arbiter handle (DEF-212). CloseOverlay is the close action.
        private PanelHandle _panelHandle;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Cosmetic Shop", CloseOverlay, IsOverlayOpen);
            // DEF-213: any door opens this panel by id. WO-1397: the door that exists is the
            // Hero deck Wardrobe card (PlayerDeckWorkspace.CardsFor, PlayerDeckKind.Hero).
            PanelRouter.Register(PanelId.CosmeticShop, OpenOverlay);
        }

        private bool IsOverlayOpen() => _open;

        private void OnEnable()
        {
            ResolveBridge();
            _serviceInstance = ResolveServiceInstance();
            SubscribeChanged();
        }

        private void OnDisable()
        {
            UnsubscribeChanged();
        }

        private void OnDestroy()
        {
            PanelRouter.Unregister(PanelId.CosmeticShop, OpenOverlay);
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        private void Update()
        {
            // WO-437: no global hotkey - opens only through PanelRouter (Hero deck Wardrobe card).
            if (_toast != null && _toast.card != null && _toastUntil > 0f
                && Time.unscaledTime > _toastUntil)
            {
                _toast.card.SetActive(false);
                _toastUntil = 0f;
            }
        }

        // ─── Reflection bridge (UNCHANGED) ───────────────────────────────────

        private static void ResolveBridge()
        {
            if (s_serviceType != null && s_catalogType != null && s_defType != null) return;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (s_serviceType == null)
                        s_serviceType = asm.GetType("DeNelle.Cosmetics.CosmeticOwnershipService", false);
                    if (s_catalogType == null)
                        s_catalogType = asm.GetType("DeNelle.Cosmetics.CosmeticCatalog", false);
                    if (s_defType == null)
                        s_defType = asm.GetType("DeNelle.Cosmetics.CosmeticDef", false);
                    if (s_serviceType != null && s_catalogType != null && s_defType != null) break;
                }

                if (s_serviceType != null)
                {
                    s_instanceProp = s_serviceType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    s_ownedProp  = s_serviceType.GetProperty("OwnedCosmetics", BindingFlags.Public | BindingFlags.Instance);
                    s_ownsMethod          = s_serviceType.GetMethod("Owns",       new[] { typeof(string) });
                    s_equippedForMethod   = s_serviceType.GetMethod("EquippedFor", new[] { typeof(string) });
                    s_equipMethod         = s_serviceType.GetMethod("Equip",       new[] { typeof(string) });
                    s_changedEvent        = s_serviceType.GetEvent("Changed");
                }
                if (s_catalogType != null)
                {
                    s_byCategoryMethod = s_catalogType.GetMethod("ByCategory",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                }
                if (s_defType != null)
                {
                    s_defIdField           = s_defType.GetField("Id");
                    s_defCategoryField     = s_defType.GetField("Category");
                    s_defDisplayNameField  = s_defType.GetField("DisplayName");
                    s_defDescriptionField  = s_defType.GetField("Description");
                    s_defUnlockMethodField = s_defType.GetField("UnlockMethod");
                    s_defPreviewColorProp  = s_defType.GetProperty("PreviewUnityColor");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CosmeticShopPanel] reflection bridge resolve failed: " + ex.Message);
            }
        }

        private object ResolveServiceInstance()
        {
            try { return s_instanceProp?.GetValue(null); }
            catch (Exception e) { FlowTrace.Warn("CosmeticShop", $"ResolveServiceInstance reflected get threw: {e.GetType().Name}: {e.Message}"); return null; }
        }

        private int OwnedCount()
        {
            if (_serviceInstance == null || s_ownedProp == null) return 0;
            try
            {
                var owned = s_ownedProp.GetValue(_serviceInstance) as ICollection;
                return owned != null ? owned.Count : 0;
            }
            catch (Exception e) { FlowTrace.Warn("CosmeticShop", $"OwnedCount reflected get threw: {e.GetType().Name}: {e.Message}"); return 0; }
        }

        private bool OwnsId(string id)
        {
            if (_serviceInstance == null || s_ownsMethod == null) return false;
            try { return (bool)s_ownsMethod.Invoke(_serviceInstance, new object[] { id }); }
            catch (Exception e) { FlowTrace.Warn("CosmeticShop", $"OwnsId('{id}') reflected call threw: {e.GetType().Name}: {e.Message}"); return false; }
        }

        private string EquippedFor(string category)
        {
            if (_serviceInstance == null || s_equippedForMethod == null) return null;
            try { return s_equippedForMethod.Invoke(_serviceInstance, new object[] { category }) as string; }
            catch (Exception e) { FlowTrace.Warn("CosmeticShop", $"EquippedFor('{category}') reflected call threw: {e.GetType().Name}: {e.Message}"); return null; }
        }

        private void EquipId(string id)
        {
            if (_serviceInstance == null || s_equipMethod == null) return;
            try { s_equipMethod.Invoke(_serviceInstance, new object[] { id }); }
            catch (Exception ex)
            {
                FlowTrace.Warn("CosmeticShop", $"EquipId('{id}') reflected call threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private IEnumerable CatalogByCategory(string category)
        {
            if (s_byCategoryMethod == null) yield break;
            object result;
            try { result = s_byCategoryMethod.Invoke(null, new object[] { category }); }
            catch (Exception e) { FlowTrace.Warn("CosmeticShop", $"CatalogByCategory('{category}') reflected call threw: {e.GetType().Name}: {e.Message}"); yield break; }
            if (result is IEnumerable seq)
                foreach (var item in seq) yield return item;
        }

        private void SubscribeChanged()
        {
            if (_serviceInstance == null || s_changedEvent == null) return;
            try
            {
                Action onChanged = Repaint;
                _changedHandler = Delegate.CreateDelegate(s_changedEvent.EventHandlerType,
                    onChanged.Target, onChanged.Method);
                var addMethod = s_changedEvent.GetAddMethod();
                addMethod?.Invoke(_serviceInstance, new object[] { _changedHandler });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CosmeticShopPanel] Changed subscribe failed: " + ex.Message);
                _changedHandler = null;
            }
        }

        private void UnsubscribeChanged()
        {
            if (_serviceInstance == null || s_changedEvent == null || _changedHandler == null) return;
            try
            {
                var removeMethod = s_changedEvent.GetRemoveMethod();
                removeMethod?.Invoke(_serviceInstance, new object[] { _changedHandler });
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("CosmeticShop", $"UnsubscribeChanged reflected call threw: {ex.GetType().Name}: {ex.Message}");
            }
            _changedHandler = null;
        }

        // ─── UI construction (kit modal, lazy on first open) ─────────────────

        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;
            using var _ = FlowTrace.Enter("CosmeticShop", "BuildUi");

            // PORTRAIT sizing (UI review 05): Merchant_Panel is a PORTRAIT frame (~1005x1507). Anchor
            // to a narrow, tall center column so the rendered aspect matches the template instead of
            // stretching the ornate frame into a landscape slab.
            // Shared store size (owner felt-test 2026-07-15: all stores same size / matching Y).
            _modal = ElarionUiKit.BuildObsidianModal("CosmeticShopUI", "Cosmetic Shop",
                ElarionUiKit.StorePanelAnchorMin, ElarionUiKit.StorePanelAnchorMax, CloseOverlay,
                frameName: RpgUiCatalog.FrameMerchant, medallionIcon: "coin");

            var layout = _modal.chrome.layout;
            var body = layout != null && layout.body != null
                ? (Transform)layout.body
                : _modal.chrome.content.transform;

            // Mobile-first (owner rule): compact CENTERED column, not full-bleed edge-to-edge
            // bars — the tabs + card list share one thumb-zone band (0.10–0.90) so cards read as
            // centered plates with side margins on a phone.
            const float BandMin = 0.10f, BandMax = 0.90f;

            // ── PX BAND LADDER (UI audit 2026-08-01, F1) ────────────────────────────
            // WAS: every band was a FRACTION of the body (Cosmetics 0.955-0.995, TabRail
            // 0.875-0.945, CardScroll 0.09-0.865, footer 0.01-0.07) with the tab buttons
            // anchored 0.05-0.95 INSIDE the 0.07-tall rail. Worked arithmetic:
            //   panel = StorePanelAnchor (0.035 -> 0.965) = 0.93 of the modal canvas height
            //   FrameMerchant body, after the WO-714 P6 close-band reservation (which also
            //   relocates the merchant footer band), resolves to ~496 canvas-local px on the
            //   landscape Seeker canvas (~1118 px portrait)
            //   rail       = 0.07 x 496  ~= 35 px
            //   tab button = 0.90 x 35   ~= 31 px
            // BuildObsidianButton ends in ClampMinTouch (ElarionUiKitObsidian.cs:650,:685),
            // which grows a sub-floor button to MinTouchPx = 112 px (ElarionUiKit.cs:317)
            // SYMMETRICALLY ABOUT ITS CENTRE (ElarionUiKit.cs:979-988) -- ~+40 px ABOVE and
            // ~+40 px BELOW the rail, so the tabs overran the Cosmetics balance line above and
            // the card list below. Identical geometry, identical bug, as LeaderboardPanel.
            //
            // NOW: fixed REFERENCE-PIXEL rungs (offsetMin/offsetMax on a CanvasScaler'd canvas
            // are canvas-local units == reference px, the same unit MinTouchPx is measured in).
            // Ladder (top -> bottom of the body):
            //   Cosmetics 40 | gap 8 | TabRail 120 | gap 8 | CardScroll (flex) | gap 8 | Footer 32
            //   fixed total = 40+8+120+8+8+32 = 216 px; the card list takes the remainder
            //   (~280 px landscape = ~3 of the 92 px cards / ~900 px portrait) and scrolls.
            // The 120 px rail is >= the 112 px floor, so ClampMinTouch is a NO-OP on the tabs.
            const float OwnershipH = 40f, TabRailH = 120f, FooterH = 32f, Gap = 8f;
            const float TabRailTop = OwnershipH + Gap;                 // 48
            const float ListTop    = TabRailTop + TabRailH + Gap;    // 176
            const float ListBottom = FooterH + Gap;                  // 40

            // Cosmetics balance — its OWN band at the top of the body well. EYES-SWEEP 2026-07-06
            // (#6): the chip lived in the frame's HEADER zone (0.70–0.99) where the centered
            // "Cosmetic Shop" title painted straight over it on the narrow portrait frame. The
            // title keeps the header; the balance gets the first body band, right-aligned + fitted.
            var ownershipHost = PxBandFromTop(body, "OwnershipBand", BandMin, BandMax, 0f, OwnershipH);
            _ownershipLabel = MakeText(ownershipHost, "0", 16, ElarionUi.Gold, FontStyles.Bold,
                TextAlignmentOptions.Right, Vector2.zero, Vector2.one);
            ElarionUiKit.FitSingleLine(_ownershipLabel);

            // Category tabs — one 120 px rung below the balance line.
            _tabHost = PxBandFromTop(body, "TabRail", BandMin, BandMax, TabRailTop, TabRailH);
            BuildTabs();

            // Scrollable card list — absorbs the remainder between the rail and the footer.
            var scrollHost = PxStretchBand(body, "CardScroll", BandMin, BandMax, ListTop, ListBottom);
            _listContent = BuildScrollColumn(scrollHost);

            // Anti-FOMO footer (spec Section 9).
            var footHost = PxBandFromBottom(body, "FooterBand", BandMin, BandMax, 0f, FooterH);
            MakeText(footHost, "Beauty is earned, never required.", 12, ElarionUi.ParchmentDim,
                FontStyles.Italic, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

            // Toast — low center of the modal canvas.
            _toast = ElarionUiKit.ToastCard(_modal.canvas.transform,
                ElarionUiKit.ToastTone.Gold, accentLeft: true, TextAnchor.MiddleCenter);
            var trt = _toast.card.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.20f, 0.015f);
            trt.anchorMax = new Vector2(0.80f, 0.075f);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            _toast.card.SetActive(false);

            _modal.canvas.SetActive(false);   // built hidden; OpenOverlay shows it
        }

        private void BuildTabs()
        {
            for (int i = _tabHost.childCount - 1; i >= 0; i--)
                Destroy(_tabHost.GetChild(i).gameObject);
            AddTab("Hero", "hero", 0);
            AddTab("Pet", "pet", 1);
            AddTab("Village", "village", 2);
        }

        private void AddTab(string label, string category, int index)
        {
            float x0 = 0.005f + index * (1f / 3f);
            float x1 = x0 + (1f / 3f) - 0.01f;
            // FULL rail height (0..1 = the 120 px rung), NOT the old 0.05-0.95 inset: 120 px is
            // already above MinTouchPx (112), so ClampMinTouch never inflates the button out of
            // its band into the balance line above or the card list below.
            var button = ElarionUiKit.BuildObsidianButton(_tabHost, label,
                ElarionUiKit.ObsidianButtonStyle.Style1,
                category == _activeCategory ? ElarionUiKit.ObsidianButtonColor.Yellow
                                            : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(x0, 0f), new Vector2(x1, 1f),
                () => { _activeCategory = category; BuildTabs(); Repaint(); });
            var caption = button != null ? button.GetComponentInChildren<TMPro.TMP_Text>() : null;
            if (caption != null)
            {
                caption.rectTransform.anchorMin = new Vector2(0.03f, 0f);
                caption.rectTransform.anchorMax = new Vector2(0.97f, 1f);
                caption.rectTransform.offsetMin = Vector2.zero;
                caption.rectTransform.offsetMax = Vector2.zero;
                caption.characterSpacing = 0f;
            }
        }

        private void Repaint()
        {
            if (!_open || _modal == null || _listContent == null) return;

            // Refresh service handle in case the bootstrap raced us.
            if (_serviceInstance == null) _serviceInstance = ResolveServiceInstance();

            if (_ownershipLabel != null)
                // WO-697: currency through the ONE kit formatter (compact >= 10k, no N0 grouping).
                _ownershipLabel.text = ElarionUi.CompactNumber(OwnedCount()) + "  Cosmetics";

            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            int cardCount = 0;
            foreach (var def in CatalogByCategory(_activeCategory))
            {
                if (def == null) continue;
                BuildCard(def);
                cardCount++;
            }
            if (cardCount == 0)
            {
                // Empty-data roll-up: a category with zero cards is shown AND self-reported,
                // so a missing/empty catalog never reads as a blank panel with no trace.
                FlowTrace.Warn("CosmeticShop",
                    $"Repaint: category '{_activeCategory}' produced 0 cosmetic cards — " +
                    "showing the visible empty-state placeholder (data-empty).");
                var rowGo = new GameObject("Empty", typeof(RectTransform), typeof(LayoutElement));
                rowGo.transform.SetParent(_listContent, false);
                rowGo.GetComponent<LayoutElement>().preferredHeight = 48f;
                MakeText(rowGo.transform, "Nothing here yet - check back next season.", 14,
                    ElarionUi.ParchmentDim, FontStyles.Italic, TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one);
            }
        }

        private void BuildCard(object def)
        {
            string id          = s_defIdField?.GetValue(def) as string ?? string.Empty;
            string category    = s_defCategoryField?.GetValue(def) as string ?? string.Empty;
            string displayName = s_defDisplayNameField?.GetValue(def) as string ?? id;
            string description = s_defDescriptionField?.GetValue(def) as string ?? string.Empty;
            string unlockMethod = (s_defUnlockMethodField?.GetValue(def) as string ?? "buy").ToLowerInvariant();
            Color preview      = SafeColor(s_defPreviewColorProp?.GetValue(def));

            bool owned    = OwnsId(id);
            bool equipped = owned && string.Equals(EquippedFor(category), id, StringComparison.OrdinalIgnoreCase);
            bool isAchievement = unlockMethod == "achievement";

            // DEF-197: affordability computed once so the price line + button agree.

            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            cardGo.transform.SetParent(_listContent, false);
            cardGo.GetComponent<LayoutElement>().preferredHeight = 92f;
            var bg = cardGo.GetComponent<Image>();
            var slotSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, "slot_item");
            if (slotSprite != null) { bg.sprite = slotSprite; bg.type = Image.Type.Sliced; bg.color = Color.white; }
            else bg.color = new Color(0f, 0f, 0f, 0.35f);

            // Preview tile — real render when present, tinted swatch fallback.
            var iconGo = new GameObject("Preview", typeof(RectTransform));
            iconGo.transform.SetParent(cardGo.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.015f, 0.14f);
            irt.anchorMax = new Vector2(0.115f, 0.86f);
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            var tex = ResolvePreviewTexture(def);
            if (tex != null)
            {
                var raw = iconGo.AddComponent<RawImage>();
                raw.texture = tex;
                raw.raycastTarget = false;
            }
            else
            {
                var tile = iconGo.AddComponent<Image>();
                tile.color = preview;
                tile.raycastTarget = false;
            }

            // Name / description / price column.
            MakeText(cardGo.transform, displayName, 16, ElarionUi.Parchment, FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0.13f, 0.60f), new Vector2(0.72f, 0.95f));
            MakeText(cardGo.transform, description, 12, ElarionUi.ParchmentDim, FontStyles.Italic,
                TextAlignmentOptions.TopLeft, new Vector2(0.13f, 0.30f), new Vector2(0.72f, 0.58f));

            string priceText;
            if (isAchievement) priceText = owned ? "Earned" : "Earn via play";
            else priceText = owned ? "Owned" : "Unavailable";
            Color priceTint = owned ? ElarionUi.Gold : ElarionUi.ParchmentDim;
            MakeText(cardGo.transform, priceText, 13, priceTint, FontStyles.Normal,
                TextAlignmentOptions.Left, new Vector2(0.13f, 0.05f), new Vector2(0.72f, 0.28f));

            // ONE action button — state machine unchanged.
            if (equipped)
            {
                var b = ElarionUiKit.BuildObsidianButton(cardGo.transform, "Equipped",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                    new Vector2(0.74f, 0.28f), new Vector2(0.985f, 0.72f), null);
                b.interactable = false;
            }
            else if (owned)
            {
                ElarionUiKit.BuildObsidianButton(cardGo.transform, "Equip",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                    new Vector2(0.74f, 0.28f), new Vector2(0.985f, 0.72f),
                    () => { EquipId(id); ShowToast($"Equipped {displayName}"); Repaint(); });
            }
            else
            {
                var b = ElarionUiKit.BuildObsidianButton(cardGo.transform, "Locked",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.74f, 0.28f), new Vector2(0.985f, 0.72f), null);
                b.interactable = false;
            }
        }

        // Optional real preview render, loaded by cosmetic id from Resources. Cached.
        private static readonly Dictionary<string, Texture2D> s_previewCache = new Dictionary<string, Texture2D>();

        private Texture2D ResolvePreviewTexture(object def)
        {
            string id = s_defIdField?.GetValue(def) as string;
            if (string.IsNullOrEmpty(id)) return null;
            if (s_previewCache.TryGetValue(id, out var cached)) return cached;
            Texture2D tex = null;
            try { tex = Resources.Load<Texture2D>($"Cosmetics/Previews/{id}"); }
            catch (Exception ex)
            {
                FlowTrace.Warn("CosmeticShop", $"ResolvePreviewTexture('{id}') threw: {ex.GetType().Name}: {ex.Message} — using swatch fallback.");
                tex = null;
            }
            s_previewCache[id] = tex;
            return tex;
        }

        private static int SafeInt(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt32(value); } catch (Exception e) { FlowTrace.Warn("CosmeticShop", $"SafeInt convert threw: {e.GetType().Name}: {e.Message}"); return 0; }
        }

        private static Color SafeColor(object value)
        {
            if (value is Color c) return c;
            return new Color(0.55f, 0.55f, 0.55f, 1f);
        }

        // ─── Visibility ──────────────────────────────────────────────────────

        public void ToggleOverlay()
        {
            if (_open) CloseOverlay();
            else OpenOverlay();
        }

        public void OpenOverlay()
        {
            using var _ = FlowTrace.Enter("CosmeticShop", "OpenOverlay");
            EnsureBuilt();
            if (_modal == null || _modal.canvas == null)
            {
                FlowTrace.Fail("CosmeticShop", "OpenOverlay: kit modal failed to build — open is a no-op.");
                return;
            }
            _open = true;
            _modal.canvas.SetActive(true);
            // Tell the arbiter we're open; it closes any other panel first. Battle-lock
            // may reject — revert and stay hidden.
            if (!PanelManager.NotifyOpened(_panelHandle))
            {
                _open = false;
                _modal.canvas.SetActive(false);
                FlowTrace.Warn("CosmeticShop", "OpenOverlay: arbiter rejected the open (battle-lock) - shop stays hidden");
                return;
            }
            Repaint();
            TraceOpened();
        }

        /// <summary>
        /// WO-1397: the door's proof line. The shop was unreachable for every player until the
        /// Hero deck Wardrobe card, so an open now says what the player will actually see:
        /// owned/total across the three categories and the equipped hero look. A missing bridge
        /// (Cosmetics assembly stripped, service not yet spawned) is a WARN naming the reason,
        /// never a silent empty list.
        /// </summary>
        private void TraceOpened()
        {
            if (_serviceInstance == null) _serviceInstance = ResolveServiceInstance();
            if (s_catalogType == null || s_byCategoryMethod == null)
            {
                FlowTrace.Warn("CosmeticShop", "shop unavailable: DeNelle.Cosmetics.CosmeticCatalog not resolved - every tab renders data-empty");
                return;
            }
            if (_serviceInstance == null)
            {
                FlowTrace.Warn("CosmeticShop", "shop unavailable: CosmeticOwnershipService.Instance is null - nothing owned, nothing equippable");
                return;
            }
            int total = 0;
            foreach (var category in new[] { "hero", "pet", "village" })
                foreach (var def in CatalogByCategory(category))
                    if (def != null) total++;
            string equipped = EquippedFor("hero");
            FlowTrace.Step("CosmeticShop", "shop opened from Hero deck; owned=" + OwnedCount() + "/" + total +
                " equipped=" + (string.IsNullOrEmpty(equipped) ? "none" : equipped));
        }

        public void CloseOverlay()
        {
            if (_modal == null || _modal.canvas == null) { _open = false; return; }
            _open = false;
            _modal.canvas.SetActive(false);
            PanelManager.NotifyClosed(_panelHandle);
        }

        private void ShowToast(string message)
        {
            if (_toast == null || _toast.card == null || _toast.label == null) return;
            _toast.label.text = message;
            _toast.card.SetActive(true);
            _toastUntil = Time.unscaledTime + 3f;
        }

        // ─── uGUI helpers (same shapes as LeaderboardPanel) ──────────────────

        // ── FIXED-REFERENCE-PIXEL BANDS (UI audit 2026-08-01, F1) ────────────────────
        // Same primitive (and same wording) as LeaderboardPanel: a band's HEIGHT is set in
        // canvas-local units via offsetMin/offsetMax, and on a CanvasScaler'd canvas those
        // units ARE reference px -- the unit ElarionUiKit.MinTouchPx (112) is expressed in.
        // A 120 px rung is therefore provably above the touch floor at every resolution, so
        // ClampMinTouch can never grow a button out of its band into a neighbouring one.
        // (The old fraction-anchored ZoneRect helper is gone -- it WAS the bug.)

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
        /// bottom -- it absorbs whatever the fixed rungs leave over.</summary>
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

        private static Transform BuildScrollColumn(Transform host)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D), typeof(Image));
            scrollGo.transform.SetParent(host, false);
            var srt = scrollGo.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = Vector2.one;
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            // SWEEP 9413 R2 (#5): bottom padding = one card row (92 + spacing) so the last card
            // scrolls fully clear of the RectMask2D instead of slicing mid-glyph at max scroll.
            layout.padding = new RectOffset(8, 8, 8, 100);
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            return contentGo.transform;
        }

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
