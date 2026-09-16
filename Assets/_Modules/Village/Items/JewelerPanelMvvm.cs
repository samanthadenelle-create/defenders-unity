// =============================================================================
// JewelerPanelMvvm — the jeweler jewelry-crafting VIEW (MVVM slice). A DUMB SKIN:
// it INHERITS the shared kit chrome (BuildObsidianPanel: FrameCrafting master-detail
// + zones + the ONE shared Close) and BINDS a JewelerVM. ALL state/logic (recipes,
// base/gem have-need, can-craft, craft command) lives in the VM — the View never
// reads or defines game state; it only DISPLAYS and routes commands.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Items
//
// WO-F conversion (2026-07-03): the old 3-column card grid -> the owner-ratified
// FrameCrafting MASTER-DETAIL template (matches VillageCraftingPanel/Workshop +
// CraftingPanelMvvm):
//   * bodyLeft  (dark well)      = recipe rows (Obsidian buttons, selected=Yellow,
//                                  right-aligned readable state "+ Ready" / "2 of 3")
//                                  in a WO-795 vertical SCROLL WELL (Viewport+RectMask2D
//                                  + VerticalLayoutGroup Content, fixed-height rows) —
//                                  every recipe lists; overflow scrolls, never overlaps
//                                  or truncates
//   * bodyRight (parchment well) = the WO-693 shared COMPACT DETAIL CARD
//                                  (ElarionUiKit.BuildParchmentDetailCard: icon plate +
//                                  name + rarity + flavor -> BESTOWS -> REQUIRES ->
//                                  currency cost chips -> the ONE Set-Gems CTA carrying
//                                  its blocker when disabled)
//   * footer — EMPTY (WO-693 deleted the tiny instruction strip; its content lives on
//                                  the CTA blocker + the empty-state explanation)
// The ONE shared Close is the chrome's (no per-panel X / close_normal / footer close).
// Mobile-first (WO-693): all detail text on the ElarionUi ladder, Fit-protected with
// ElarionUi.FontFloorMobile — never below the readable floor; met/unmet carried by
// ASCII glyph + have/need counts (colorblind law), color reinforcement only.
//
// Code-built uGUI ONLY (no UXML — §8). Registers PanelId.JewelerCrafting.
// Spawned by JewelerPanelBootstrap once a hero exists.
// =============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Catalog;  // StructureRoles — the single naming authority
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village.Items
{
    [DisallowMultipleComponent]
    public sealed class JewelerPanelMvvm : MonoBehaviour, IPanelView
    {
        private JewelerVM _vm;

        private GameObject _ui;
        private Transform _recipeHost;   // bodyLeft — dark list well
        private Transform _listContent;  // scroll Content inside the recipe well (WO-795)
        private Transform _detailHost;   // bodyRight — parchment detail well
        private TextMeshProUGUI _headerLabel;

        private string _selectedRecipeId;
        private PanelHandle _panelHandle;

        public bool IsOpen => _ui != null;

        // WO-693: parchment ink lives in the kit now (ElarionUiKit.ParchmentInk*) — the
        // shared compact detail card owns all detail-pane presentation.

        // ── Registration (mirror CraftingPanelMvvm) ───────────────────────────────

        private void Awake()
        {
            // ⛔ "Jeweler's Bench" here is an INTERNAL ARBITER KEY, never rendered — it is the
            // handle PanelManager routes close/back on. Do NOT "fix" it to the catalog word:
            // the VISIBLE header is set in the build below from StructureRoles, and renaming
            // this handle only breaks the arbiter's bookkeeping.
            _panelHandle = PanelManager.Register("Jeweler's Bench", Close, () => IsOpen);
            PanelRouter.Register(PanelId.JewelerCrafting, Open);
        }

        private void OnDestroy()
        {
            Unbind();
            if (_vm != null) { _vm.Dispose(); _vm = null; }
            if (_ui != null) Destroy(_ui);
            _ui = null;
            PanelRouter.Unregister(PanelId.JewelerCrafting, Open);
        }

        // ── Open: build chrome, construct + bind the VM ───────────────────────────

        public void Open()
        {
            if (!DeNelle.Village.Crafting.JewelerProgression.IsUnlocked)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Crafting",
                    "Jeweler open refused: no dungeon-earned rough stone exists in persistent history.");
                return;
            }
            Close();
            BuildChrome();

            _vm = new JewelerVM(Close);
            Bind(_vm);

            if (!PanelManager.NotifyOpened(_panelHandle))
                return; // rejected (e.g. in battle) — NotifyOpened already invoked Close.

            Debug.Log("[JewelerPanelMvvm] Opened. Bound JewelerVM (MVVM).");
        }

        // ── IPanelView ────────────────────────────────────────────────────────────

        public void Bind(IPanelViewModel vm)
        {
            Unbind();
            _vm = vm as JewelerVM;
            if (_vm == null) return;
            _vm.Changed += Render;
            Render();
        }

        public void Unbind()
        {
            if (_vm != null) _vm.Changed -= Render;
        }

        // ── Render: repaint from vm.* ONLY ────────────────────────────────────────

        private void Render()
        {
            if (_vm == null) return;
            if (_headerLabel != null) _headerLabel.text = _vm.Title;
            RebuildMasterDetail();
        }

        // ── Master-detail (recipe list left / parchment-ink detail right) ─────────

        private void RebuildMasterDetail()
        {
            if (_recipeHost == null || _detailHost == null || _listContent == null || _vm == null) return;

            var recipes = _vm.Recipes;
            int n = recipes != null ? recipes.Count : 0;

            if (n > 0 && (string.IsNullOrEmpty(_selectedRecipeId) || FindIndex(recipes) < 0))
                _selectedRecipeId = recipes[0].RecipeId;

            // Recipe rows (dark well, left) — fixed-height LayoutElement children of the
            // WO-795 scroll Content: EVERY recipe lists; overflow scrolls, never stacks.
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            if (n == 0)
            {
                var host = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                host.transform.SetParent(_listContent, false);
                host.GetComponent<LayoutElement>().preferredHeight = 120f;
                var none = MakeText(host.transform, "No jewelry recipes available.", ElarionUi.FontLabel,
                    ElarionUi.ParchmentDim, FontStyles.Italic, TextAlignmentOptions.Center,
                    new Vector2(0.05f, 0f), new Vector2(0.95f, 1f));
                ElarionUiKit.FitBlock(none, ElarionUi.FontFloorMobile);
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    var r = recipes[i];
                    string id = r.RecipeId;
                    bool selected = id == _selectedRecipeId;
                    string name = string.IsNullOrEmpty(r.OutputName) ? r.DisplayName : r.OutputName;

                    // Fixed-height row host; the kit button fills it (anchors 0..1).
                    var host = new GameObject("Row_" + id, typeof(RectTransform), typeof(LayoutElement));
                    host.transform.SetParent(_listContent, false);
                    var le = host.GetComponent<LayoutElement>();
                    le.preferredHeight = RowPixelH;
                    le.minHeight = RowPixelH;

                    // WO-693: no dangling "+/-" suffix — the row carries a readable right-
                    // aligned state ("+ Ready" / "2 of 3"); selection keeps the gold rim (Yellow).
                    var rowBtn = ElarionUiKit.BuildObsidianButton(host.transform, name,
                        ElarionUiKit.ObsidianButtonStyle.Style1,
                        selected ? ElarionUiKit.ObsidianButtonColor.Yellow
                                 : ElarionUiKit.ObsidianButtonColor.Gray,
                        Vector2.zero, Vector2.one,
                        () => { _selectedRecipeId = id; RebuildMasterDetail(); });
                    ElarionUiKit.AddRowStateSuffix(rowBtn, RowState(r),
                        r.CanCraft ? ElarionUi.Affordable : ElarionUi.ParchmentDim);
                    var recipeLabel = rowBtn != null ? rowBtn.GetComponentInChildren<TMP_Text>() : null;
                    if (recipeLabel != null) ElarionUiKit.FitBlock(recipeLabel, ElarionUi.FontFloorMobile);
                    ElarionUiKit.ApplyMultilineButtonPlate(rowBtn);
                }
            }

            // Detail (parchment well, right — the shared compact card, WO-693).
            for (int i = _detailHost.childCount - 1; i >= 0; i--)
                Destroy(_detailHost.GetChild(i).gameObject);

            int sel = FindIndex(recipes);
            if (sel >= 0) BuildDetail(recipes[sel]);
            else
                ElarionUiKit.BuildParchmentDetailEmpty(_detailHost, "Select a piece to inspect",
                    "Set gems into a ring or amulet to forge a finer piece.");
        }

        /// <summary>Readable list-row state: met -> "+ Ready"; else "met of total" progress.
        /// Glyph + counts carry the state (colorblind law); color is reinforcement only.</summary>
        private static string RowState(JewelerRecipeVM r)
        {
            if (r.CanCraft) return "+ Ready";
            var lines = r.Ingredients;
            int total = lines != null ? lines.Count : 0;
            if (total == 0) return "";
            int met = 0;
            for (int i = 0; i < total; i++) if (lines[i].Met) met++;
            return met + " of " + total;
        }

        private int FindIndex(IReadOnlyList<JewelerRecipeVM> recipes)
        {
            if (recipes == null || string.IsNullOrEmpty(_selectedRecipeId)) return -1;
            for (int i = 0; i < recipes.Count; i++)
                if (recipes[i].RecipeId == _selectedRecipeId) return i;
            return -1;
        }

        private void BuildDetail(JewelerRecipeVM recipe)
        {
            string display = string.IsNullOrEmpty(recipe.OutputName) ? recipe.DisplayName : recipe.OutputName;
            string recipeId = recipe.RecipeId;

            // BESTOWS — every non-zero grant relayed from accessories.json, rendered
            // generically ("+  Max health  +50"); plus the quiet level-requirement row.
            var bestows = new List<ElarionUiKit.DetailCardRow>();
            var grants = recipe.Bestows;
            for (int i = 0; i < grants.Count; i++)
                bestows.Add(new ElarionUiKit.DetailCardRow("+", grants[i].Label, grants[i].Value,
                    ElarionUiKit.DetailRowTone.Good));
            if (recipe.ReqLevel > 0)
                bestows.Add(new ElarionUiKit.DetailCardRow("*", "Requires hero level " + recipe.ReqLevel,
                    "", ElarionUiKit.DetailRowTone.Dim));

            // REQUIRES — "[OK|X] Name .... have / need"; glyph + counts carry the state.
            var requires = new List<ElarionUiKit.DetailCardRow>();
            var lines = recipe.Ingredients;
            for (int i = 0; i < lines.Count; i++)
            {
                var ing = lines[i];
                requires.Add(new ElarionUiKit.DetailCardRow(
                    ing.Met ? "OK" : "X",
                    ing.Name,
                    ing.Have + " / " + ing.Need,
                    ing.Met ? ElarionUiKit.DetailRowTone.Good : ElarionUiKit.DetailRowTone.Bad));
            }

            // COST — the WO-675/676 mirrored currency_* chips.
            var chips = new List<ElarionUiKit.DetailCardChip>();
            var cost = recipe.CostChips;
            for (int i = 0; i < cost.Count; i++)
                chips.Add(new ElarionUiKit.DetailCardChip(
                    cost[i].ConceptId, cost[i].Word, cost[i].Amount));

            var card = ElarionUiKit.BuildParchmentDetailCard(_detailHost, new ElarionUiKit.DetailCardSpec
            {
                IconPath = recipe.OutputIconPath,
                Title = display,
                RarityText = string.IsNullOrEmpty(recipe.Rarity) ? "" : recipe.Rarity.ToUpperInvariant(),
                Flavor = recipe.Flavor,
                Bestows = bestows,
                Requires = requires,
                CostChips = chips,
                CtaLabel = CtaLabel(recipe),
                CtaEnabled = recipe.CanCraft,
                OnCta = () => { if (_vm != null) _vm.Craft(recipeId); },
            });
            if (card != null)
            {
                var rt = card.GetComponent<RectTransform>();
                var le = card.GetComponent<LayoutElement>() ?? card.AddComponent<LayoutElement>();
                float height = Mathf.Max(320f, rt != null ? rt.sizeDelta.y + 28f : 640f);
                le.minHeight = height;
                le.preferredHeight = height;
                le.flexibleHeight = 0f;
            }
        }

        /// <summary>The ONE action carries the blocker when disabled ("SET GEMS - missing 2
        /// gems") — the old instruction strip's job now lives on the CTA / empty-state.</summary>
        private static string CtaLabel(JewelerRecipeVM recipe)
        {
            if (recipe.CanCraft) return "SET GEMS";

            var lines = recipe.Ingredients;
            bool baseMissing = lines.Count > 0 && !lines[0].Met;   // base piece is line 0
            int missingGems = 0;
            for (int i = 1; i < lines.Count; i++)
                if (!lines[i].Met) missingGems += lines[i].Need - lines[i].Have;

            if (baseMissing) return "SET GEMS - need the base piece";
            if (missingGems > 0)
                return "SET GEMS - missing " + missingGems + (missingGems == 1 ? " gem" : " gems");
            return "SET GEMS - not enough resources";               // wallet-short (cost chips show it)
        }

        // ── Chrome (presentation only; INHERITS the shared kit frame + zones) ─────

        private void BuildChrome()
        {
            _ui = ElarionUiKit.BuildModalCanvas("JewelerPanelMvvmUI", 31000);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            ElarionUiKit.Scrim(_ui.transform, onTapClose: () => { if (_vm != null) _vm.Close(); });

            // SHARED Obsidian chrome (FrameCrafting master-detail): black panel + gold trim +
            // gold header + medallion + the ONE shared Close — all built by the kit. The panel
            // adds NO chrome and NO close of its own.
            // VISIBLE header: the catalog row that claims the jeweler role owns the word
            // ("Jeweler"), never a literal here (WO-1161). Generic fallback only.
            string header = StructureRoles.By[StructureRole.Jeweler].DisplayName ?? "Jewelry";
            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, header,
                new Vector2(0.10f, 0.08f), new Vector2(0.90f, 0.92f), () => { if (_vm != null) _vm.Close(); },
                frameName: RpgUiCatalog.FrameCrafting, medallionIcon: "gem");
            _headerLabel = chrome.title;

            var layout = chrome.layout;
            _recipeHost = layout != null && layout.bodyLeft != null
                ? (Transform)layout.bodyLeft
                : (layout != null && layout.body != null ? (Transform)layout.body : chrome.content.transform);
            var detailZone = layout != null && layout.bodyRight != null
                ? (Transform)layout.bodyRight
                : (layout != null && layout.body != null ? (Transform)layout.body : chrome.content.transform);
            _detailHost = ElarionUiKit.MakeScrollZone(detailZone, spacing: 0f, padding: 4).content;

            BuildRecipeScrollWell();

            // WO-693: the tiny footer instruction strip is DELETED — its content lives on
            // the CTA blocker text and the empty-state explanation (one-action law).

            // Close is the SHARED kit Close (top-right) — no per-panel close.
        }

        // ── Recipe scroll well (WO-795: rows never stack/overlap; overflow scrolls) ──

        private const float RowPixelH = 120f;   // fixed row height at/above the mobile touch floor

        /// <summary>Build the vertical scroll well inside the recipe list host, ONCE per
        /// Open (RumorBoardPanel WO-795 pattern): Viewport (near-invisible Image drag
        /// catcher + RectMask2D) + top-anchored Content (VerticalLayoutGroup +
        /// ContentSizeFitter). Rebuilds only clear/refill the Content, so scroll position
        /// survives row selection.</summary>
        private void BuildRecipeScrollWell()
        {
            if (_recipeHost == null) return;

            var viewportGo = new GameObject("RecipeViewport", typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportGo.transform.SetParent(_recipeHost, false);
            var vpr = viewportGo.GetComponent<RectTransform>();
            vpr.anchorMin = new Vector2(0.02f, 0.02f);
            vpr.anchorMax = new Vector2(0.98f, 0.98f);
            vpr.offsetMin = Vector2.zero;
            vpr.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // drag catcher

            var contentGo = new GameObject("RecipeContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var cr = contentGo.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot     = new Vector2(0.5f, 1f);
            cr.offsetMin = Vector2.zero;
            cr.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.childControlWidth  = true; vlg.childForceExpandWidth  = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            vlg.spacing = 8f;
            // Bottom pad = one row so the last recipe scrolls fully clear of the mask.
            vlg.padding = new RectOffset(6, 6, 6, (int)RowPixelH + 8);
            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = viewportGo.GetComponent<ScrollRect>();
            scroll.viewport = vpr;
            scroll.content  = cr;
            scroll.horizontal = false;
            scroll.vertical   = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            _listContent = contentGo.transform;
        }

        // ── uGUI helper (mirrors VillageCraftingPanel.MakeText) ───────────────────

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

        // ── Teardown ──────────────────────────────────────────────────────────────

        private void Close()
        {
            Unbind();
            if (_vm != null) { _vm.Dispose(); _vm = null; }
            _headerLabel = null;
            _recipeHost = null;
            _listContent = null;
            _detailHost = null;
            if (_ui != null) Destroy(_ui);
            _ui = null;
            PanelManager.NotifyClosed(_panelHandle);
        }
    }
}
