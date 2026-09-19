// =============================================================================
// BuildSelectionUI — the code-built Move/Sell action panel for Build Mode (WO-108 P2).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// When the player taps a placed structure (armed == null), BuildModeController
// selects it and shows this small action panel: MOVE / UPGRADE / SELL (with its
// 50%-refund amount) / CANCEL. It raises callbacks the controller wires to its
// move + sell + upgrade + deselect verbs. The placement palette stays the CREATE
// strip; this is the EDIT panel for an already-placed structure.
//
// WO-D conversion (2026-07-03, coverage matrix row #37): UIDocument/UITK bar ->
// code-built uGUI on the Obsidian kit language. IN-WORLD-ADJACENT strip, not a
// modal: keeps its centre-top position + behaviour, restyled as a slot-plate bar
// (RpgUiCatalog RoleSlot "slot_action") carrying kit buttons (BuildObsidianButton)
// on its own overlay canvas, above the palette. "Cancel" clears the selection and
// IS this strip's close affordance, so its GameObject is named "CloseButton" per
// the close convention (label stays "Cancel").
// =============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;   // FlowTrace — instrument the Upgrade tap path (owner F8 2026-07-17, S12)

namespace DeNelle.Village
{
    /// <summary>
    /// The Build Mode edit-action panel for a selected structure: Move / Upgrade /
    /// Sell / Cancel. Built in code (uGUI + Obsidian kit) so it renders in player
    /// builds; mirrors BuildPaletteUI's canvas ownership.
    /// </summary>
    public sealed class BuildSelectionUI : MonoBehaviour
    {
        /// <summary>Raised when MOVE is tapped — enter the re-placement loop.</summary>
        public event Action OnMoveRequested;

        /// <summary>Raised when SELL is tapped — free + remove + refund.</summary>
        public event Action OnSellRequested;

        /// <summary>Raised when UPGRADE is tapped — spend the tier cost + level up (S5).</summary>
        public event Action OnUpgradeRequested;

        /// <summary>Raised when CANCEL is tapped — clear the selection.</summary>
        public event Action OnCancelRequested;

        // Above the palette strip (900), below kit modals (31000).
        private const int SortingOrder = 910;

        private GameObject _canvas;   // own overlay canvas (kit BuildModalCanvas)
        private TextMeshProUGUI _titleLabel;
        private Button _sellBtn;
        private TMP_Text _sellLabel;
        private Button _upgradeBtn;
        private TMP_Text _upgradeLabel;

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas);
        }

        // ── Show / Hide ────────────────────────────────────────────────────────

        /// <summary>
        /// Show the action panel for a structure (S5: with its upgrade state). The title
        /// carries the current/max tier; the Upgrade button shows the next-tier cost, greys +
        /// reads "Max Tier" at the ceiling, and greys when the player can't yet afford the step.
        /// </summary>
        /// <param name="structureName">Display label.</param>
        /// <param name="refund">Sell refund total (units across all pools).</param>
        /// <param name="level">Current upgrade level (1-based).</param>
        /// <param name="maxLevel">Highest level this structure can reach (1 = not upgradeable).</param>
        /// <param name="upgradeCostTotal">Total units of the next-tier upgrade cost (for display).</param>
        /// <param name="canAffordUpgrade">True when the player can pay the next-tier cost now.</param>
        /// <param name="sellVerb">WO-1876 — "Sell" for standing bodies; "Clear" for owned-town rubble salvage.</param>
        public void Show(string structureName, int refund, int level, int maxLevel,
                         int upgradeCostTotal, bool canAffordUpgrade, string sellVerb = "Sell")
        {
            EnsureBuilt();
            if (_canvas == null) return;

            int lvl = Mathf.Max(1, level);
            int max = Mathf.Max(1, maxLevel);

            if (_titleLabel != null)
            {
                string baseName = string.IsNullOrEmpty(structureName) ? "Structure" : structureName;
                _titleLabel.text = max > 1 ? $"{baseName}  (Lv {lvl}/{max})" : baseName;
            }
            if (_sellLabel != null)
            {
                string verb = string.IsNullOrEmpty(sellVerb) ? "Sell" : sellVerb;
                _sellLabel.text = verb + " (" + Mathf.Max(0, refund) + ")";   // ASCII — no crystal glyph in TMP
            }

            if (_upgradeBtn != null)
            {
                bool upgradeable = max > 1;
                _upgradeBtn.gameObject.SetActive(upgradeable);
                if (upgradeable)
                {
                    bool atMax = lvl >= max;
                    if (atMax)
                    {
                        if (_upgradeLabel != null) _upgradeLabel.text = "Max Tier";
                        _upgradeBtn.interactable = false;
                    }
                    else
                    {
                        if (_upgradeLabel != null) _upgradeLabel.text = "Upgrade (" + Mathf.Max(0, upgradeCostTotal) + ")";
                        // Owner F8 2026-07-17 — "Upgrade does NOTHING": the button was
                        // HARD-DISABLED when unaffordable (interactable = canAffordUpgrade),
                        // so a tap raised no click, ran no handler, and emitted no trace =
                        // a silent dead button. Keep it TAPPABLE (matches the place path):
                        // the handler (UpgradeSelected) is the single gate and pops a
                        // "Not enough X" toast when the player can't yet pay.
                        _upgradeBtn.interactable = true;
                    }
                }
                FlowTrace.Step("BuildUpgrade",
                    "Show '" + (string.IsNullOrEmpty(structureName) ? "?" : structureName) +
                    "' lvl=" + lvl + "/" + max + " cost=" + Mathf.Max(0, upgradeCostTotal) +
                    " afford=" + canAffordUpgrade + " btnActive=" + (max > 1) +
                    " interactable=" + _upgradeBtn.interactable);
            }

            _canvas.SetActive(true);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.SetActive(false);
        }

        private void EnsureBuilt()
        {
            if (_canvas != null) return;

            _canvas = ElarionUiKit.BuildModalCanvas("BuildSelectionCanvas", SortingOrder);

            // Slot-plate bar anchored centre-top so it never overlaps the bottom palette
            // strip. Only the bar raycasts — the rest of the screen stays click-through.
            var bar = new GameObject("SelectionBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(_canvas.transform, false);
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = new Vector2(0.5f, 1f);
            brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -110f);
            brt.sizeDelta = new Vector2(880f, 170f);

            var img = bar.GetComponent<Image>();
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
                img.color = ElarionUiKit.ObsidianFill;   // art-absent fallback: kit obsidian
            }
            img.raycastTarget = true;   // eat taps on the bar so they can't fall through

            // Title row (top band).
            _titleLabel = MakeText(bar.transform, "Structure", 17, ElarionUi.Gilt, FontStyles.Bold,
                TextAlignmentOptions.Center, new Vector2(0.04f, 0.62f), new Vector2(0.96f, 0.94f));

            // Action row (bottom band): Move / Upgrade / Sell / Cancel kit buttons.
            var moveBtn = ElarionUiKit.BuildObsidianButton(bar.transform, "Move",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.035f, 0.10f), new Vector2(0.255f, 0.56f),
                () => OnMoveRequested?.Invoke());

            // S5 — the UPGRADE verb (the CoC sink). Hidden for non-upgradeable structures
            // (maxLevel == 1) and disabled at the tier ceiling / when unaffordable; Show()
            // sets its text + enabled state per the selected structure's tier each time.
            _upgradeBtn = ElarionUiKit.BuildObsidianButton(bar.transform, "Upgrade",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.275f, 0.10f), new Vector2(0.495f, 0.56f),
                () =>
                {
                    // Proves the click FIRES (only reached when interactable + raycast lands).
                    FlowTrace.Step("BuildUpgrade", "Upgrade button tapped -> raise OnUpgradeRequested.");
                    OnUpgradeRequested?.Invoke();
                });
            _upgradeLabel = _upgradeBtn.GetComponentInChildren<TMP_Text>(true);

            _sellBtn = ElarionUiKit.BuildObsidianButton(bar.transform, "Sell",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Red,
                new Vector2(0.515f, 0.10f), new Vector2(0.735f, 0.56f),
                () => OnSellRequested?.Invoke());
            _sellLabel = _sellBtn.GetComponentInChildren<TMP_Text>(true);

            // Cancel clears the selection — this strip's close affordance, so it carries
            // the canonical close name while keeping its "Cancel" label.
            var cancelBtn = ElarionUiKit.BuildObsidianButton(bar.transform, "Cancel",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.755f, 0.10f), new Vector2(0.965f, 0.56f),
                () => OnCancelRequested?.Invoke());
            cancelBtn.gameObject.name = "CloseButton";

            _canvas.SetActive(false);   // built hidden; Show shows it
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
