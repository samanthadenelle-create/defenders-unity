// =============================================================================
// NPCUpgradeStation — stationed NPC at a key building that offers tiered
// upgrades paid via the Economy class, with visual building transformation.
// -----------------------------------------------------------------------------
// Placed by VillageSceneBuilder at district buildings (Mill, Armorer, Forge,
// Lumbermill, Resource Upgrade, Jeweler).
//
// Interaction: Player approaches or activates → code-built modal (consistent
// with BuildPreviewModal style) showing current tier, benefits, ResourceCost.
// Confirm: EconomyService.Instance.TrySpend(cost) → upgrade visual (tier swap
// or StructureTierVisual animation) + register bonus (e.g. passive Economy
// grant tick or production boost).
//
// All resource handling routes through existing EconomyService (Grant/TrySpend
// / AddResource from WO-106). No duplicate wallets.
//
// Visual upgrade: For now swaps a "tier visual" child or calls StructureTierVisual
// if present on the building root. Future: full prefab swap per tier.
// =============================================================================
using System;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Catalog; // StructureRoles — the single naming authority
using DeNelle.Core.State; // for Economy if needed, but use the Village one
using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// NPC stationed at a building offering upgrades. Ties directly to Economy.
    /// </summary>
    [RequireComponent(typeof(Collider))] // trigger or proximity
    public sealed class NPCUpgradeStation : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Player-facing station name. LEAVE BLANK to take the word from the catalog " +
                 "row that claims the crafting_station role.")]
        // Was hardcoded "Workshop", which kept saying that after the catalog relabelled the
        // crafting station. It is now BLANK by default and resolved through DisplayName below.
        // ⛔ Do NOT put a catalog lookup in this field INITIALISER: a MonoBehaviour field
        // initialiser runs during construction, where Unity forbids Resources.Load — the
        // catalog read has to happen at USE time, which is what DisplayName does.
        public string BuildingName = "";
        public string ResourceTypeHint = "General"; // e.g. "Food", "Iron" for flavor

        /// <summary>
        /// The word shown to the player. An inspector-authored <see cref="BuildingName"/> wins;
        /// otherwise the CATALOG settles it (WO-1161 — StructureRoles is the single naming
        /// authority, so a creative rename lands here with no code change). The final fallback
        /// is a GENERIC word, reached only if the catalog has not loaded — never a rival
        /// proper noun, because a wrong-but-present name is worse than a vague one.
        /// </summary>
        public string DisplayName =>
            !string.IsNullOrEmpty(BuildingName)
                ? BuildingName
                : (StructureRoles.By[StructureRole.CraftingStation].DisplayName ?? "Station");

        [Header("Tiers")]
        public int CurrentTier = 1;
        public int MaxTier = 3;

        [Header("Economy Costs (base, scaled by tier)")]
        public ResourceCost BaseUpgradeCost = new ResourceCost(wood: 30, stone: 20, iron: 10);

        [Header("Visual Root")]
        [Tooltip("The building visual root that will be upgraded (tier children or StructureTierVisual).")]
        public GameObject BuildingVisualRoot;

        [Header("NPC")]
        public Transform NpcStationPoint; // where the character stands
        public string NpcDialogueLine = "I can improve this building for the right resources.";

        private bool _uiOpen;
        private GameObject _upgradeUI;

        // WO-744 MVVM: the economy transaction is owned by a small VM so this world-space
        // View never names EconomyService.Instance. Resolved lazily (CreateDefault is the
        // sole resolution site for the economy singleton).
        private NPCUpgradeVM _vm;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") && !_uiOpen)
            {
                // WO-557 (Yarn removed): stationed NPC upgrade is a TRANSACTION, not dialogue —
                // open the code-built upgrade UI directly (it always was the non-Yarn fallback).
                ShowUpgradeUI();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player") && _uiOpen)
            {
                CloseUI();
            }
        }

        public void ShowUpgradeUI()
        {
            if (_uiOpen) return;
            _uiOpen = true;

            _upgradeUI = new GameObject("UpgradeUI_" + DisplayName);
            _upgradeUI.transform.SetParent(transform, false);

            var canvas = _upgradeUI.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            // Simple panel
            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(_upgradeUI.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(4f, 3f);
            rect.localPosition = new Vector3(0, 3f, 0);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Color.white;

            // Title
            CreateText(panel.transform, $"{DisplayName} (Tier {CurrentTier}/{MaxTier})", new Vector2(0, 1.1f), 22, Color.white);

            // Cost preview
            var nextCost = GetNextTierCost();
            CreateText(panel.transform, $"Cost: {CostString(nextCost)}", new Vector2(0, 0.4f), 16, Color.yellow);

            // Benefits (flavor + future hook)
            CreateText(panel.transform, $"+ Productivity ({ResourceTypeHint})", new Vector2(0, -0.1f), 14, new Color(0.6f, 1f, 0.6f));

            // Buttons
            CreateButton(panel.transform, "Upgrade", new Vector2(-0.8f, -0.9f), TryUpgrade);
            CreateButton(panel.transform, "Close", new Vector2(0.8f, -0.9f), CloseUI);

            Debug.Log($"[NPCUpgradeStation] Opened upgrade UI for {DisplayName}");
        }

        private void CreateText(Transform parent, string txt, Vector2 anchored, int size, Color c)
        {
            var go = new GameObject("Text", typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.text = txt;
            tmp.fontSize = size;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = c;
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = anchored;
            r.sizeDelta = new Vector2(3.6f, 0.5f);
        }

        private void CreateButton(Transform parent, string label, Vector2 anchored, Action onClick)
        {
            var btnGO = new GameObject($"Btn_{label}", typeof(Button), typeof(Image));
            btnGO.transform.SetParent(parent, false);
            var rect = btnGO.GetComponent<RectTransform>();
            rect.anchoredPosition = anchored;
            // WorldSpace canvas (panel is 4x3 world units) — the reference-px canonical pin
            // (360x132) does NOT apply here, so grow the button in its OWN units for a
            // thumb-friendly tap target (VISUAL_TOUCH_CONTRAST_AUDIT 2026-07-14, P1).
            rect.sizeDelta = new Vector2(1.8f, 0.7f);

            btnGO.GetComponent<Image>().color = new Color(0.25f, 0.2f, 0.15f);

            var btn = btnGO.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick());

            var txtGO = new GameObject("Label", typeof(TMPro.TextMeshProUGUI));
            txtGO.transform.SetParent(btnGO.transform, false);
            var tmp = txtGO.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 18;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = Color.white;
            var tRect = txtGO.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.sizeDelta = Vector2.zero;
            MedievalUiSkin.ApplyButton(btn, primary: string.Equals(label, "Upgrade", StringComparison.Ordinal));
        }

        private ResourceCost GetNextTierCost()
        {
            float scale = 1f + (CurrentTier - 1) * 0.6f;
            return new ResourceCost(
                (int)(BaseUpgradeCost.Wood * scale),
                (int)(BaseUpgradeCost.Stone * scale),
                (int)(BaseUpgradeCost.Iron * scale),
                (int)(BaseUpgradeCost.Crystals * scale)
            );
        }

        private string CostString(ResourceCost c)
        {
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[] { ("wood", "Wood", c.Wood), ("stone", "Stone", c.Stone), ("iron", "Iron", c.Iron), ("crystal", "Crystals", c.Crystals) });
            return parts.Count > 0 ? DeNelle.Core.UI.CostFormat.Words(parts) : "Free";
        }

        private void TryUpgrade()
        {
            if (CurrentTier >= MaxTier)
            {
                Debug.Log("[NPCUpgradeStation] Already max tier.");
                CloseUI();
                return;
            }

            var cost = GetNextTierCost();
            if (_vm == null) _vm = NPCUpgradeVM.CreateDefault();
            if (!_vm.TryPurchaseUpgrade(cost))
            {
                Debug.LogWarning($"[NPCUpgradeStation] Cannot afford upgrade for {DisplayName}.");
                return;
            }

            CurrentTier++;

            // Visual upgrade (integrate with existing tier system)
            ApplyVisualUpgrade();

            // Economy benefit hook (example: grant a small immediate bonus + register for future)
            // In real system this could register a ProductionSource with Economy.
            _vm.GrantFirstHarvestBonus(); // symbolic "first harvest boost"
            Debug.Log($"[NPCUpgradeStation] {DisplayName} upgraded to tier {CurrentTier}. Economy charged.");

            // Refresh UI or close
            CloseUI();
            // Re-open to show new tier (or leave closed)
            // ShowUpgradeUI();
        }

        private void ApplyVisualUpgrade()
        {
            GameSfx.PlayBuildingUpgrade();   // satisfying upgrade chime
            if (BuildingVisualRoot == null) BuildingVisualRoot = gameObject;

            // Prefer existing StructureTierVisual if present on the building
            var tierVis = BuildingVisualRoot.GetComponent<StructureTierVisual>();
            if (tierVis != null)
            {
                // Assume it has a SetTier or similar; call via reflection or extend later
                // For now, simple scale bump as "growth"
                BuildingVisualRoot.transform.localScale = Vector3.one * (0.95f + CurrentTier * 0.08f);
                // TODO: full anim / particle "construction" burst
                Debug.Log($"[NPCUpgradeStation] Applied tier visual via StructureTierVisual path for {DisplayName}");
                return;
            }

            // Fallback: simple "upgrade" by scaling + slight color shift on renderers (demo)
            BuildingVisualRoot.transform.localScale = Vector3.one * (0.95f + CurrentTier * 0.08f);
            foreach (var r in BuildingVisualRoot.GetComponentsInChildren<Renderer>())
            {
                if (r.material != null)
                {
                    // Slight "better materials" tint
                    var c = r.material.color;
                    r.material.color = Color.Lerp(c, new Color(0.95f, 0.92f, 0.85f), 0.3f);
                }
            }

            // Optional: spawn a small "upgrade complete" effect (if VFX available)
            // For now just log + scale as proof of visual transformation.
            Debug.Log($"[NPCUpgradeStation] Visual upgrade applied (scale + tint) for {DisplayName} tier {CurrentTier}");
        }

        private void CloseUI()
        {
            if (_upgradeUI != null) Destroy(_upgradeUI);
            _upgradeUI = null;
            _uiOpen = false;
        }
    }
}
