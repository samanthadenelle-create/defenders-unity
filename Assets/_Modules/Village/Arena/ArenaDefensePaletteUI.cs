// =============================================================================
// ArenaDefensePaletteUI — the code-built Arena Defense SETUP palette (WO-389 P2).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Arena
//
// The defense-layer twin of BuildPaletteUI: a horizontal strip of the 6 pre-
// placeable Arena DEFENDERS (ArenaDefenseCatalog), each card showing the troop's
// name + POINT cost. The budget is the point POOL (DefensePointPool = 50), NOT
// EconomyService resources — so the top bar shows "remaining points" and a card
// greys out when adding it would exceed the pool. Tapping a card arms the def via
// OnDefSelected; the Done button exits.
//
// CODE-BUILT, NO UXML (repo rule: .uxml UIDocuments render empty in player builds).
// Owns its own UIDocument + adopts a sibling's PanelSettings exactly like
// BuildPaletteUI so it renders. Pure display: the controller owns the point math
// (it passes in the live spent/remaining each Render) so this never double-counts.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.UIElements;
using DeNelle.Core;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village.Arena
{
    /// <summary>
    /// The Arena Defense setup palette. Lists <see cref="ArenaDefenseCatalog"/> defs
    /// as tappable cards (name + point cost), shows the remaining pool, and raises
    /// <see cref="OnDefSelected"/> when one is armed. Built in code so it renders in
    /// player builds. Mirrors <c>BuildPaletteUI</c>.
    /// </summary>
    public sealed class ArenaDefensePaletteUI : MonoBehaviour
    {
        /// <summary>Raised when a card is tapped — arg is the armed defender def.</summary>
        public event Action<ArenaDefenseDef> OnDefSelected;

        /// <summary>Raised when the Done button is tapped (exit setup).</summary>
        public event Action OnExitRequested;

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _strip;
        private Label _pointsLabel;

        // The pure ViewModel owns the catalog projection + budget/affordability + armed
        // rules (single source of truth). The controller pushes the live spend via Render
        // -> vm.SetBudget; this View reads vm.Cards/PointsLabel and never touches the catalog.
        private ArenaPaletteVM _vm;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            _vm = ArenaPaletteVM.CreateDefault(ArenaPaletteMode.Defense);
            AdoptPanelSettings();
        }

        private void OnDestroy()
        {
            _vm?.Dispose();
            _vm = null;
        }

        /// <summary>
        /// Adopt a sibling UIDocument's PanelSettings so the palette renders (a fresh
        /// doc has none → invisible). Mirrors BuildPaletteUI / BuildMenu.
        /// </summary>
        private void AdoptPanelSettings()
        {
            if (_document == null) return;
            UIDocument hud = null, any = null;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
            {
                if (doc == null || doc == _document || doc.panelSettings == null) continue;
                if (any == null) any = doc;
                if (doc.gameObject.name.IndexOf("Hud", StringComparison.OrdinalIgnoreCase) >= 0) { hud = doc; break; }
            }
            var src = hud ?? any;
            if (src != null)
            {
                _document.panelSettings = src.panelSettings;
                _document.sortingOrder = src.sortingOrder + 6;   // above HUD
                Debug.Log("[ArenaDefensePaletteUI] Adopted PanelSettings from sibling '" + src.gameObject.name + "'.");
            }
            else
            {
                // No UI-Toolkit sibling AND no Resources PanelSettings (castle hub is uGUI)
                // — CREATE one at runtime so the palette renders (inline styles do the look).
                var created = ScriptableObject.CreateInstance<PanelSettings>();
                created.name = "ArenaRuntimePanelSettings";
                created.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                created.referenceResolution = new Vector2Int(1080, 1920);
                foreach (var d in FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
                    if (d != null && d.panelSettings != null && d.panelSettings.themeStyleSheet != null)
                    { created.themeStyleSheet = d.panelSettings.themeStyleSheet; break; }
                _document.panelSettings = created;
                _document.sortingOrder = 5000;
                Debug.Log("[ArenaDefensePaletteUI] Created runtime PanelSettings (theme=" +
                          (created.themeStyleSheet != null ? "yes" : "none") + ").");
            }
        }

        // ── Show / Hide ────────────────────────────────────────────────────────

        /// <summary>Show the palette and render it against the supplied budget.</summary>
        public void Show(int spent, int remaining)
        {
            EnsureBuilt();
            if (_root != null) _root.style.display = DisplayStyle.Flex;
            Render(spent, remaining);
        }

        public void Hide()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
            _vm?.Arm(null);
        }

        private void EnsureBuilt()
        {
            if (_vm == null) _vm = ArenaPaletteVM.CreateDefault(ArenaPaletteMode.Defense);
            if (_root != null) return;
            var docRoot = _document != null ? _document.rootVisualElement : null;
            if (docRoot == null) return;

            _root = new VisualElement { name = "arena-defense-palette-root" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.right = 0; _root.style.bottom = 0;
            _root.style.flexDirection = FlexDirection.Column;
            _root.pickingMode = PickingMode.Ignore;   // click-through; the bar takes taps
            docRoot.Add(_root);

            // Top row: remaining-points readout + Done. Stone bar, gilt under-rule.
            var topBar = new VisualElement();
            topBar.style.flexDirection = FlexDirection.Row;
            topBar.style.justifyContent = Justify.SpaceBetween;
            topBar.style.alignItems = Align.Center;
            topBar.style.paddingLeft = 14; topBar.style.paddingRight = 14;
            topBar.style.paddingTop = 6; topBar.style.paddingBottom = 6;
            topBar.style.backgroundColor = ElarionUi.PanelStone;
            topBar.style.borderBottomWidth = 2;
            topBar.style.borderBottomColor = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.55f);

            _pointsLabel = new Label(LocalText.Format("arena.defense_palette.points_label", 50, 50));
            _pointsLabel.style.color = ElarionUi.Aether;
            _pointsLabel.style.fontSize = ElarionUi.FontHead;
            _pointsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            topBar.Add(_pointsLabel);

            var exitBtn = new Button(() => OnExitRequested?.Invoke()) { text = LocalText.Get("common.done") };
            ElarionUi.StyleButton(exitBtn, ElarionUi.ButtonKind.Gold);
            exitBtn.style.minWidth = 88;
            topBar.Add(exitBtn);
            _root.Add(topBar);

            // Bottom row: horizontal card strip in a recessed stone tray.
            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.style.height = 128;
            scroll.style.backgroundColor = ElarionUi.PanelStoneDark;
            scroll.style.paddingTop = 4; scroll.style.paddingBottom = 4;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _strip = scroll.contentContainer;
            _strip.style.flexDirection = FlexDirection.Row;
            _root.Add(scroll);
        }

        // ── Render ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-draw the cards + the remaining-points readout for the supplied budget. A
        /// card is enabled only when adding its def keeps the spend within the pool
        /// (or it is the currently-armed def, so re-tapping it stays legal).
        /// </summary>
        public void Render(int spent, int remaining)
        {
            EnsureBuilt();
            if (_vm == null || _strip == null) return;

            // Push the live budget INTO the VM (single source of truth) and read back.
            _vm.SetBudget(spent, remaining);

            if (_pointsLabel != null) _pointsLabel.text = _vm.PointsLabel;

            _strip.Clear();
            var projected = _vm.Cards;
            int cards = projected != null ? projected.Count : 0;
            for (int i = 0; i < cards; i++)
                _strip.Add(BuildCard(projected[i]));

            if (cards == 0)
            {
                var none = new Label(new LocalizedText("arena.defense_palette.no_defenders").Resolve());
                none.style.color = Color.white;
                none.style.paddingLeft = 12; none.style.paddingTop = 12;
                _strip.Add(none);
            }
        }

        private VisualElement BuildCard(ItemVM item)
        {
            // The VM projects both the armed highlight (Equipped) and affordability (fits the
            // remaining pool OR is the already-armed card).
            bool armed = item.Equipped;
            bool affordable = item.Affordable;
            string id = item.Id;

            var card = new Button(() =>
            {
                _vm.Arm(id);
                OnDefSelected?.Invoke(_vm.DefFor(id));
                Render(_vm.Spent, _vm.Remaining);   // refresh the armed highlight
            });
            card.style.width = 116; card.style.height = 108;
            card.style.marginLeft = 6; card.style.marginRight = 6;
            card.style.marginTop = 8; card.style.marginBottom = 8;
            card.style.paddingTop = 8; card.style.paddingBottom = 8;
            card.style.paddingLeft = 8; card.style.paddingRight = 8;
            card.style.flexDirection = FlexDirection.Column;
            card.style.justifyContent = Justify.SpaceBetween;
            card.style.backgroundColor = armed ? ElarionUi.AetherDim : ElarionUi.PanelStone;
            ElarionUi.SetRadius(card, ElarionUi.RadiusMd);
            ElarionUi.SetBorderWidth(card, armed ? 2 : 1);
            ElarionUi.SetBorderColor(card, armed
                ? ElarionUi.Gilt
                : new Color(ElarionUi.StoneTrim.r, ElarionUi.StoneTrim.g, ElarionUi.StoneTrim.b, 0.5f));
            card.style.opacity = affordable ? 1f : 0.45f;
            card.SetEnabled(affordable);

            var nameLabel = new Label(item.Name);
            nameLabel.style.color = ElarionUi.Parchment;
            nameLabel.style.fontSize = ElarionUi.FontLabel;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(nameLabel);

            var costLabel = new Label(item.Price + " pts");
            costLabel.style.color = affordable ? ElarionUi.Affordable : ElarionUi.Danger;
            costLabel.style.fontSize = ElarionUi.FontLabel;
            costLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            costLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(costLabel);

            return card;
        }
    }
}
