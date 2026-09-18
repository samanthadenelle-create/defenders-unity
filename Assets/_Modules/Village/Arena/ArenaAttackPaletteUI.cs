// =============================================================================
// ArenaAttackPaletteUI — the code-built Arena ATTACK recruit palette (WO-389 #3).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Arena
//
// The ATTACK twin of the defense palette. SIMPLER: there is no grid placement —
// the player RECRUITS a roster, each tap ADDS a troop to the squad (spending its
// PointCost), and the squad sum must stay <= the point pool (DefensePointPool =
// 50, reused as the squad budget). A top label shows "Squad Points: N / 50",
// each card is a recruit (+1) tap greyed when adding it would exceed the budget,
// and Launch / Cancel finish the screen.
//
// uGUI REWRITE (was UI Toolkit): the castle hub has no PanelSettings/theme, so a
// UIDocument palette came up invisible and flooded the console. This is rebuilt
// in the SAME proven uGUI recipe as the sibling ArenaPanel.cs (own Canvas +
// CanvasScaler + GraphicRaycaster, dark scrim that blocks click-through, rounded
// stone cards, ElarionUi colours/fonts). No UIElements / UIDocument / PanelSettings
// anywhere. WebGL-safe: the whole build is wrapped in try/catch and the procedural
// rounded sprite is failure-safe (flat tinted quad fallback), so a texture/exception
// can NEVER blank the screen (same guard as ArenaPanel, PIPELINE_STATE §8).
//
// Pure display: the controller owns the squad list + point math and pushes the live
// spent/remaining each Render, so this never double-counts. The PUBLIC INTERFACE
// (events + Show/Hide/Render) is unchanged so ArenaAttackRecruitController compiles
// untouched. ASCII-only runtime strings.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village.Arena
{
    /// <summary>
    /// The Arena Attack recruit palette. Lists <see cref="ArenaDefenseCatalog"/> defs
    /// as tappable RECRUIT cards (name + point cost), shows the remaining pool, and
    /// raises <see cref="OnRecruit"/> when one is tapped. Code-built uGUI (own Canvas)
    /// so it renders in the castle hub which has no UI-Toolkit PanelSettings/theme.
    /// </summary>
    public sealed class ArenaAttackPaletteUI : MonoBehaviour
    {
        /// <summary>Raised when a card is tapped to recruit a troop — arg is the def.</summary>
        public event Action<ArenaDefenseDef> OnRecruit;

        /// <summary>Raised when the Launch button is tapped (start the raid).</summary>
        public event Action OnLaunchRequested;

        /// <summary>Raised when the Cancel button is tapped (abandon recruiting).</summary>
        public event Action OnCancelRequested;

        // ── uGUI overlay roots ─────────────────────────────────────────────────
        private GameObject _ui;            // the Canvas GameObject (toggled by Show/Hide)
        private TMPro.TextMeshProUGUI _pointsLabel;
        private Transform _cardRow;        // horizontal row that holds the troop cards

        // ── Sleek palette (sourced from ElarionUi — mirrors ArenaPanel's recipe) ──
        private static readonly Color CardBg = ElarionUi.PanelStone;
        private static readonly Color TopBar = ElarionUi.PanelStoneDark;

        // The pure ViewModel owns the catalog projection + budget/affordability rules
        // (single source of truth). The controller pushes the live spend via Render ->
        // vm.SetBudget; this View reads vm.Cards/PointsLabel and never touches the catalog.
        private ArenaPaletteVM _vm;

        // ── Show / Hide ────────────────────────────────────────────────────────

        /// <summary>Show the palette and render it against the supplied budget.</summary>
        public void Show(int spent, int remaining, int squadCount)
        {
            try
            {
                EnsureBuilt();
                if (_ui != null) _ui.SetActive(true);
                Render(spent, remaining, squadCount);
            }
            catch (Exception e)
            {
                Debug.LogError("[ArenaAttackPaletteUI] Show failed (UI may be partial): " + e);
            }
        }

        public void Hide()
        {
            if (_ui != null) _ui.SetActive(false);
        }

        private void OnDestroy()
        {
            _vm?.Dispose();
            _vm = null;
            if (_ui != null) Destroy(_ui);
            _ui = null;
        }

        // ── Construction (once) ──────────────────────────────────────────────────

        private void EnsureBuilt()
        {
            if (_vm == null) _vm = ArenaPaletteVM.CreateDefault(ArenaPaletteMode.Attack);
            if (_ui != null) return;
            BuildModal();
        }

        private void BuildModal()
        {
            // Own Canvas — ScreenSpaceOverlay so it draws regardless of any scene
            // camera/PanelSettings; high sortingOrder so it sits above the HUD.
            _ui = new GameObject("ArenaAttackPaletteCanvas");

            var canvas = _ui.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;  // full-screen modal — above HUD + ArenaPanel (1100)

            var scaler = _ui.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _ui.AddComponent<GraphicRaycaster>();

            // Full-screen dark backdrop. raycastTarget=true so it BLOCKS click-through
            // to the world / build-mode behind it (this was a real bug). A flat tinted
            // quad (rounded:false) — no sprite, so it can never fail to a blank panel.
            var scrim = AddImage(_ui.transform, "Scrim", Vector2.zero, Vector2.one, ElarionUi.Scrim, rounded: false);
            scrim.GetComponent<Image>().raycastTarget = true;

            // Bottom content bar — top label/buttons row over a card tray. Anchored to
            // the bottom slice of the screen so the recruit strip reads like a tray.
            var content = AddImage(_ui.transform, "Content",
                                   new Vector2(0f, 0f), new Vector2(1f, 0.30f), TopBar, rounded: false);

            // Top row: "Squad Points: N / 50" on the left, Launch + Cancel on the right.
            _pointsLabel = AddLabel(content.transform, "Squad Points: 0 / 50",
                                    0.74f, 0.96f, ElarionUi.Aether, ElarionUi.FontHead,
                                    TMPro.TextAlignmentOptions.Left, 0.04f, 0.55f, bold: true);

            AddButton(content.transform, LocalText.Get("armyScreen.cancel"),
                      new Vector2(0.79f, 0.085f), new Vector2(0.74f, 0.96f),
                      CardBg, () => OnCancelRequested?.Invoke(), ButtonKind.Neutral);

            AddButton(content.transform, new LocalizedText("arena.attack_palette.launch_raid").Resolve(),
                      new Vector2(0.92f, 0.075f), new Vector2(0.74f, 0.96f),
                      ElarionUi.GoldButton, () => OnLaunchRequested?.Invoke(), ButtonKind.Gold);

            // Card tray — a recessed well that holds the horizontal row of troop cards.
            var tray = AddImage(content.transform, "CardTray",
                                new Vector2(0.02f, 0.06f), new Vector2(0.98f, 0.66f),
                                ElarionUi.PanelStoneDark, rounded: false);
            _cardRow = tray.transform;
        }

        // ── Render ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-draw the recruit cards + the remaining-points readout for the supplied
        /// budget. A card is enabled only when recruiting its def keeps the squad spend
        /// within the pool (def.PointCost &lt;= the passed-in remaining).
        /// </summary>
        public void Render(int spent, int remaining, int squadCount)
        {
            try
            {
                EnsureBuilt();
                if (_vm == null || _cardRow == null) return;

                // Push the live budget INTO the VM (single source of truth) and read back.
                _vm.SetBudget(spent, remaining, squadCount);

                if (_pointsLabel != null) _pointsLabel.text = _vm.PointsLabel;

                // Rebuild the card row against the new budget.
                for (int i = _cardRow.childCount - 1; i >= 0; i--)
                    Destroy(_cardRow.GetChild(i).gameObject);

                var cards = _vm.Cards;
                int count = cards != null ? cards.Count : 0;
                if (count == 0)
                {
                    AddLabel(_cardRow, "No troops registered.", 0.4f, 0.6f, ElarionUi.Parchment,
                             ElarionUi.FontBody, TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f);
                    return;
                }

                // Lay the cards left-to-right as fractional slices of the tray width.
                const float pad = 0.012f;
                float cardW = (1f - pad * (count + 1)) / count;
                for (int i = 0; i < count; i++)
                {
                    float x0 = pad + i * (cardW + pad);
                    float x1 = x0 + cardW;
                    BuildCard(cards[i], x0, x1);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ArenaAttackPaletteUI] Render failed (cards may be partial): " + e);
            }
        }

        private void BuildCard(ItemVM item, float x0, float x1)
        {
            // Affordable = the VM's projection (this def's cost fits the REMAINING budget).
            bool affordable = item.Affordable;
            string id = item.Id;

            Color bg = affordable
                ? CardBg
                : new Color(ElarionUi.Disabled.r, ElarionUi.Disabled.g, ElarionUi.Disabled.b, 0.85f);

            var btn = AddButton(_cardRow, string.Empty,
                                new Vector2((x0 + x1) * 0.5f, (x1 - x0) * 0.5f),
                                new Vector2(0.10f, 0.90f),
                                bg, () => { if (affordable) OnRecruit?.Invoke(_vm.DefFor(id)); }, ButtonKind.Neutral);
            btn.interactable = affordable;

            // Name (top) + cost (bottom) stacked on the card. Greyed when unaffordable.
            AddLabel(btn.transform, item.Name, 0.52f, 0.94f,
                     affordable ? ElarionUi.Parchment : ElarionUi.ParchmentDim,
                     ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);

            AddLabel(btn.transform, item.Price + " pts", 0.10f, 0.46f,
                     affordable ? ElarionUi.Affordable : ElarionUi.Danger,
                     ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
        }

        // ====================================================================
        // uGUI helpers — copied from ArenaPanel's proven recipe (its are private).
        // ====================================================================

        // A rounded stone image. rounded:false = a flat tinted quad (scrim / trays).
        private static GameObject AddImage(Transform parent, string name, Vector2 min, Vector2 max,
            Color color, bool rounded = true)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = min; r.anchorMax = max;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            if (rounded)
            {
                var sprite = RoundedSprite;
                img.sprite = sprite;
                img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            }
            return go;
        }

        private static TMPro.TextMeshProUGUI AddLabel(Transform parent, string text, float y0, float y1,
            Color color, int size, TMPro.TextAlignmentOptions align,
            float x0 = 0.03f, float x1 = 0.97f, float spacing = 0f, bool bold = false)
        {
            var go = new GameObject("Label", typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.characterSpacing = spacing;
            t.raycastTarget = false;
            if (bold) t.fontStyle = TMPro.FontStyles.Bold;
            return t;
        }

        private enum ButtonKind { Gold, Neutral, Confirm, Danger }

        // anchorX = (centerX, halfWidth); anchorY = (y0, y1) of the button rect.
        private Button AddButton(Transform parent, string label, Vector2 anchorX, Vector2 anchorY,
            Color bg, System.Action onClick, ButtonKind kind)
        {
            var go = new GameObject("Btn_" + (string.IsNullOrEmpty(label) ? "Card" : label),
                                    typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchorX.x - anchorX.y, anchorY.x);
            r.anchorMax = new Vector2(anchorX.x + anchorX.y, anchorY.y);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = bg;
            var sprite = RoundedSprite;
            img.sprite = sprite;
            img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            StyleButtonColors(btn);
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            // Text on gold uses dark ink for contrast; everything else cream parchment.
            if (!string.IsNullOrEmpty(label))
            {
                Color textColor = kind == ButtonKind.Gold ? ElarionUi.Ink : ElarionUi.Parchment;
                var tt = AddLabel(go.transform, label, 0f, 1f, textColor, ElarionUi.FontBody,
                                  TMPro.TextAlignmentOptions.Center, 0f, 1f, spacing: 1f, bold: true);
                tt.raycastTarget = false;
            }
            return btn;
        }

        // Clean subtle brightness feedback (no colour shift) — ArenaPanel's recipe.
        private static void StyleButtonColors(Button button)
        {
            if (button == null) return;
            button.transition = Selectable.Transition.ColorTint;
            var cb = button.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            cb.pressedColor     = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.selectedColor    = cb.highlightedColor;
            cb.disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.07f;
            button.colors = cb;
        }

        // ── Procedural rounded sprite (lazily built once; WebGL failure-safe) ──
        // Mirrors ArenaPanel.RoundedSprite: a 9-sliced white rounded-rect for crisp
        // modern corners. If the Texture2D build throws under WebGL we fall back to
        // null and Images render as flat tinted quads — the panel never blanks.
        private static Sprite _rounded;
        private static bool _roundedTried;
        private static Sprite RoundedSprite
        {
            get
            {
                if (!_roundedTried)
                {
                    _roundedTried = true;
                    try { _rounded = BuildRoundedSprite(); }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[ArenaAttackPaletteUI] rounded sprite build failed (flat quad): " + e.Message);
                        _rounded = null;
                    }
                }
                return _rounded;
            }
        }

        private static Sprite BuildRoundedSprite()
        {
            const int size = 32;
            const int radius = 6;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedRectDistance(x, y, size, size, radius);
                    byte a = (byte)Mathf.Clamp((int)((1f - d) * 255f), 0, 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                                 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private static float RoundedRectDistance(int x, int y, int w, int h, int radius)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float dx = Mathf.Max(Mathf.Max(radius - fx, fx - (w - radius)), 0f);
            float dy = Mathf.Max(Mathf.Max(radius - fy, fy - (h - radius)), 0f);
            float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
            return Mathf.Clamp01(dist + 0.5f);
        }
    }
}
