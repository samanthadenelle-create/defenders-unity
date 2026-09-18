// =============================================================================
// CraftingPanelController — drives the UI Toolkit crafting panel. DUMB SKIN (MVVM, Silo F).
// -----------------------------------------------------------------------------
// The controller behind CraftingPanel.uxml — the modal the Keeper opens at a
// crafting pedestal. A PASSIVE view: the CraftingPedestal raises typed UnityEvents
// (OpenRequested / CloseRequested); this controller binds a DungeonCraftVM and
// renders its projected CraftRecipeVM. The have/need math + already-crafted logic
// live in the VM, NOT in this View body (Silo F). Craft / Close route back through
// the VM to the pedestal.
//
// MODULE ISOLATION: the panel lives INSIDE DeNelle.Dungeons and references only the
// dungeon's own crafting types + the Core MVVM seam (CraftRecipeVM) — never DeNelle.Village.
//
// All UI is UI Toolkit (UXML/USS). The panel hides itself when no pedestal is open.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Dungeons
{
    /// <summary>
    /// Drives the dungeon crafting panel (CraftingPanel.uxml). Binds a
    /// <see cref="DungeonCraftVM"/> and renders its <see cref="CraftRecipeVM"/> — live
    /// have/need counts, the result row, the Craft button — forwarding Craft / Close
    /// back through the VM to the pedestal. A passive UI view.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CraftingPanelController : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("UIDocument hosting CraftingPanel.uxml. Falls back to the component on this GameObject.")]
        [SerializeField] private UIDocument _document;

        // ── UXML element-name contract with CraftingPanel.uxml ───────────────
        private const string RootName = "crafting-panel-root";
        private const string CardName = "crafting-card";
        private const string RecipeNameName = "crafting-recipe-name";
        private const string RecipeDescName = "crafting-recipe-desc";
        private const string IngredientListName = "crafting-ingredient-list";
        private const string ResultGlyphName = "crafting-result-glyph";
        private const string ResultLabelName = "crafting-result-label";
        private const string CraftButtonName = "crafting-craft-button";
        private const string CloseButtonName = "crafting-close-button";

        // ── USS class names styled by CraftingPanel.uss ──────────────────────
        private const string CellClass = "crafting-ingredient-cell";
        private const string CellHaveClass = "crafting-ingredient-cell--have";
        private const string CellNeedClass = "crafting-ingredient-cell--need";
        private const string GlyphClass = "crafting-ingredient-glyph";
        private const string NameClass = "crafting-ingredient-name";
        private const string CountClass = "crafting-ingredient-count";
        private const string CountMetClass = "crafting-ingredient-count--met";
        private const string TickClass = "crafting-ingredient-tick";
        private const string CraftReadyClass = "crafting-craft-button--ready";
        private const string ResultLabelReadyClass = "crafting-result-label--ready";
        private const string ResultLabelDoneClass = "crafting-result-label--done";

        private const string TickChar = "OK"; // ingredient-met marker (ASCII; the heavy-check glyph tofu'd on the build font)

        // ── Bound UI elements ────────────────────────────────────────────────
        private VisualElement _root;
        private Label _recipeName;
        private Label _recipeDesc;
        private VisualElement _ingredientList;
        private Label _resultGlyph;
        private Label _resultLabel;
        private Button _craftButton;
        private Button _closeButton;

        /// <summary>One built ingredient cell — the count + tick handles for a repaint.</summary>
        private struct IngredientCell
        {
            public VisualElement Root;
            public Label Count;
            public Label Tick;
        }

        private readonly List<IngredientCell> _cells = new List<IngredientCell>();

        // ── Bound ViewModel ──────────────────────────────────────────────────
        private DungeonCraftVM _vm;
        private bool _bound;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BindElements();
            Hide();
        }

        private void OnDisable()
        {
            if (_craftButton != null) _craftButton.clicked -= OnCraftClicked;
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
            DisposeVm();
            _bound = false;
        }

        // =====================================================================
        //  UI Toolkit binding
        // =====================================================================

        private void BindElements()
        {
            _root = _document != null ? _document.rootVisualElement : null;
            if (_root == null)
            {
                Debug.LogWarning(
                    "[CraftingPanelController] No UIDocument root — crafting panel will not display.");
                return;
            }

            VisualElement panel = _root.Q<VisualElement>(RootName) ?? _root;

            _recipeName = panel.Q<Label>(RecipeNameName);
            _recipeDesc = panel.Q<Label>(RecipeDescName);
            _ingredientList = panel.Q<VisualElement>(IngredientListName);
            _resultGlyph = panel.Q<Label>(ResultGlyphName);
            _resultLabel = panel.Q<Label>(ResultLabelName);
            _craftButton = panel.Q<Button>(CraftButtonName);
            _closeButton = panel.Q<Button>(CloseButtonName);

            if (_craftButton != null)
            {
                _craftButton.clicked -= OnCraftClicked; // guard a double OnEnable
                _craftButton.clicked += OnCraftClicked;
            }
            if (_closeButton != null)
            {
                _closeButton.clicked -= OnCloseClicked;
                _closeButton.clicked += OnCloseClicked;
            }

            _bound = true;
        }

        // =====================================================================
        //  Pedestal wiring — the DungeonController hooks these to the pedestal.
        // =====================================================================

        /// <summary>Subscribes this panel to a crafting pedestal's events.</summary>
        public void BindPedestal(CraftingPedestal pedestal)
        {
            if (pedestal == null) return;
            pedestal.OpenRequested.AddListener(Show);
            pedestal.CloseRequested.AddListener(Hide);
        }

        // =====================================================================
        //  Show / hide
        // =====================================================================

        /// <summary>Opens the panel against the pedestal request and paints it.</summary>
        public void Show(CraftingPanelRequest request)
        {
            if (!_bound) BindElements();
            if (request == null || request.Recipe == null)
            {
                Hide();
                return;
            }

            // Bind (or re-bind) the VM to the fresh request snapshot.
            if (_vm == null)
            {
                _vm = new DungeonCraftVM(request);
                _vm.Changed += Refresh;
            }
            else _vm.Rebind(request);

            BuildIngredientCells();

            var r = _vm.Recipe;
            // WO-1857: the fallback title reuses the shared common.recipe key (this call site is
            // named in COMMON_KEYS_REGISTRY). r.DisplayName is authored catalog data - §6a's ruling.
            if (_recipeName != null)
                _recipeName.text = r.DisplayName ?? new LocalizedText("common.recipe").Resolve();
            if (_recipeDesc != null) _recipeDesc.text = r.Description ?? string.Empty;
            if (_resultGlyph != null) _resultGlyph.text = r.ResultGlyph;

            Refresh();

            if (_root != null) _root.style.display = DisplayStyle.Flex;
        }

        /// <summary>Hides the panel — gameplay carries on behind it.</summary>
        public void Hide()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }

        /// <summary>True while the panel is shown.</summary>
        public bool IsShown => _root != null && _root.style.display == DisplayStyle.Flex;

        private void DisposeVm()
        {
            if (_vm != null)
            {
                _vm.Changed -= Refresh;
                _vm.Dispose();
                _vm = null;
            }
        }

        // =====================================================================
        //  Cell construction + repaint
        // =====================================================================

        /// <summary>Builds one cell per recipe ingredient (from the VM projection) into the list container.</summary>
        private void BuildIngredientCells()
        {
            _cells.Clear();
            if (_ingredientList == null || _vm == null) return;
            var ingredients = _vm.Recipe.Ingredients;
            if (ingredients == null) return;
            _ingredientList.Clear();

            foreach (var line in ingredients)
            {
                var cell = new VisualElement();
                cell.AddToClassList(CellClass);

                var glyph = new Label(string.IsNullOrEmpty(line.Glyph) ? "?" : line.Glyph);
                glyph.AddToClassList(GlyphClass);
                glyph.pickingMode = PickingMode.Ignore;
                ApplyGlyphTint(glyph, line.Tint);
                cell.Add(glyph);

                var name = new Label(line.DisplayName);
                name.AddToClassList(NameClass);
                name.pickingMode = PickingMode.Ignore;
                cell.Add(name);

                var count = new Label(string.Empty);
                count.AddToClassList(CountClass);
                count.pickingMode = PickingMode.Ignore;
                cell.Add(count);

                var tick = new Label(string.Empty);
                tick.AddToClassList(TickClass);
                tick.pickingMode = PickingMode.Ignore;
                cell.Add(tick);

                _ingredientList.Add(cell);
                _cells.Add(new IngredientCell { Root = cell, Count = count, Tick = tick });
            }
        }

        /// <summary>Repaints every ingredient cell + the result row + the Craft button off the VM projection.</summary>
        private void Refresh()
        {
            if (_vm == null) return;
            var recipe = _vm.Recipe;
            var ingredients = recipe.Ingredients;
            bool alreadyCrafted = recipe.AlreadyCrafted;

            // ── Ingredient cells (index-aligned with the projection) ──────────
            if (ingredients != null)
            {
                int n = Mathf.Min(_cells.Count, ingredients.Count);
                for (int i = 0; i < n; i++)
                {
                    IngredientCell cell = _cells[i];
                    CraftIngredientVM ing = ingredients[i];
                    bool met = ing.Met;

                    if (cell.Count != null)
                    {
                        cell.Count.text = $"{ing.Shown} / {ing.Need}";
                        cell.Count.EnableInClassList(CountMetClass, met);
                    }
                    if (cell.Tick != null)
                        cell.Tick.text = met ? TickChar : string.Empty;
                    if (cell.Root != null)
                    {
                        cell.Root.EnableInClassList(CellHaveClass, met);
                        cell.Root.EnableInClassList(CellNeedClass, !met);
                    }
                }
            }

            // ── Result row + Craft button ────────────────────────────────────
            bool canCraft = recipe.CanCraft;

            if (_resultLabel != null)
            {
                _resultLabel.text = _vm.ResultText;
                _resultLabel.EnableInClassList(ResultLabelReadyClass, canCraft && !alreadyCrafted);
                _resultLabel.EnableInClassList(ResultLabelDoneClass, alreadyCrafted);
            }

            if (_resultGlyph != null)
            {
                // Glyph plate warms amber when ready, gold once crafted.
                Color plate = alreadyCrafted
                    ? new Color(1f, 0.812f, 0.420f)
                    : canCraft ? new Color(0.910f, 0.659f, 0.290f)
                    : new Color(0.471f, 0.408f, 0.588f);
                _resultGlyph.style.backgroundColor = plate;
            }

            if (_craftButton != null)
            {
                _craftButton.SetEnabled(canCraft);
                _craftButton.EnableInClassList(CraftReadyClass, canCraft);
                // WO-1857: two alternative captions => two keys (a past-tense state word and an
                // imperative verb decline differently in de/fr/ru).
                _craftButton.text = alreadyCrafted
                    ? new LocalizedText("dungeon.craft.crafted_state").Resolve()
                    : new LocalizedText("dungeon.craft.craft_action").Resolve();
            }
        }

        /// <summary>Tints an ingredient cell's glyph plate from a data hex tint.</summary>
        private static void ApplyGlyphTint(Label glyph, string tint)
        {
            if (glyph == null || string.IsNullOrEmpty(tint)) return;
            if (ColorUtility.TryParseHtmlString("#" + tint, out Color c))
                glyph.style.backgroundColor = c;
        }

        // =====================================================================
        //  Button handlers
        // =====================================================================

        /// <summary>Forwards the Craft click through the VM to the pedestal, then repaints.</summary>
        private void OnCraftClicked()
        {
            if (_vm == null) return;
            _vm.Craft();
            Refresh();
        }

        /// <summary>Closes the panel through the VM (pedestal) so its state stays in sync.</summary>
        private void OnCloseClicked()
        {
            if (_vm != null) _vm.Close();
            else Hide();
        }
    }
}
