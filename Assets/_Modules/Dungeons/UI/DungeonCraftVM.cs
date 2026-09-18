// =============================================================================
// DungeonCraftVM -- the dungeon crafting-pedestal ViewModel (MVVM, Silo F).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Dungeons   Namespace: DeNelle.Dungeons
//
// Projects a CraftingPanelRequest (pedestal snapshot: recipe + crafting data +
// DungeonInventory) into the PROMOTED Core CraftRecipeVM -- the SAME struct the
// village WorkshopCraftVM produces, so the have/need/met math lives in a VM, not in
// the CraftingPanelController View body. Dungeons references ONLY Core + its own
// crafting types (NOT DeNelle.Village) -- module isolation holds.
//
// The projection is a PURE static (unit-testable with a real DungeonInventory +
// fabricated CraftingRecipe). The instance re-projects + raises Changed whenever the
// inventory changes (a pickup or a craft) and forwards Craft / Close to the pedestal.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Dungeons
{
    /// <summary>ViewModel for the dungeon crafting panel. Wraps a live
    /// <see cref="CraftingPanelRequest"/> and projects it into a <see cref="CraftRecipeVM"/>.</summary>
    public sealed class DungeonCraftVM : IPanelViewModel, IDisposable
    {
        // Result-line copy (was in the controller). // LOCALIZE
        private const string MsgGather = "Gather the ingredients";
        private const string MsgReady = "Ready to craft";
        private const string MsgCraftedFmt = "{0} crafted";

        private CraftingPanelRequest _request;
        private DungeonInventory _inv;
        private bool _disposed;

        public DungeonCraftVM(CraftingPanelRequest request)
        {
            Rebind(request);
        }

        // -- IPanelViewModel ----------------------------------------------------
        public event Action Changed;
        // WO-1857: the no-recipe fallback title reuses the shared common.crafting key (this call
        // site is named in COMMON_KEYS_REGISTRY).
        public string Title => Recipe.HasRecipe
            ? Recipe.DisplayName
            : new LocalizedText("common.crafting").Resolve();
        public void Close() { _request?.Pedestal?.ClosePanel(); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unhook();
            Changed = null;
        }

        // -- Read-only data -----------------------------------------------------

        /// <summary>The projected recipe (have/need checklist + craftable + already-crafted flags).</summary>
        public CraftRecipeVM Recipe { get; private set; }

        /// <summary>The result-row status line ("Gather the ingredients" / "Ready to craft" / "&lt;name&gt; crafted").</summary>
        public string ResultText
        {
            get
            {
                var r = Recipe;
                if (!r.HasRecipe) return MsgGather;
                if (r.AlreadyCrafted) return string.Format(MsgCraftedFmt, r.DisplayName);
                return r.CanCraft ? MsgReady : MsgGather;
            }
        }

        // -- Commands -----------------------------------------------------------

        /// <summary>Forward the Craft to the pedestal; on success adopt the fresh request + re-project.</summary>
        public bool Craft()
        {
            var pedestal = _request != null ? _request.Pedestal : null;
            if (pedestal == null) return false;
            var updated = pedestal.TryCraft();
            if (updated != null) Rebind(updated);
            else Reproject();
            Raise();
            return updated != null;
        }

        /// <summary>Adopt a new request snapshot (a re-open / a post-craft refresh) and re-project.</summary>
        public void Rebind(CraftingPanelRequest request)
        {
            Unhook();
            _request = request;
            _inv = request != null ? request.Inventory : null;
            if (_inv != null && _inv.InventoryChanged != null)
                _inv.InventoryChanged.AddListener(OnInventoryChanged);
            Reproject();
        }

        private void Unhook()
        {
            if (_inv != null && _inv.InventoryChanged != null)
                _inv.InventoryChanged.RemoveListener(OnInventoryChanged);
            _inv = null;
        }

        private void OnInventoryChanged() { Reproject(); Raise(); }

        private void Reproject()
        {
            var recipe = _request != null ? _request.Recipe : null;
            var data = _request != null ? _request.CraftingData : null;
            Recipe = Project(recipe, data, _inv);
        }

        // -- PURE projection (unit-testable) ------------------------------------

        /// <summary>Project a dungeon recipe into a Core <see cref="CraftRecipeVM"/>: ingredient glyph/tint +
        /// live have/need/met, the one-shot already-crafted flag, and the craftable flag. Null-safe.</summary>
        public static CraftRecipeVM Project(CraftingRecipe recipe, CraftingDataSet data, DungeonInventory inv)
        {
            if (recipe == null) return default;

            bool alreadyCrafted = inv != null && !string.IsNullOrEmpty(recipe.Id) && inv.HasCrafted(recipe.Id);

            var ingredients = new List<CraftIngredientVM>();
            if (recipe.Ingredients != null)
            {
                foreach (var line in recipe.Ingredients)
                {
                    if (line == null) continue;
                    var ing = data != null ? data.FindIngredient(line.IngredientId) : null;
                    int have = inv != null ? inv.CountOf(line.IngredientId) : 0;
                    bool met = alreadyCrafted || have >= line.Count;
                    int shown = alreadyCrafted ? line.Count : have;
                    string dn = ing != null ? ing.DisplayName : line.IngredientId;
                    string glyph = ing != null && !string.IsNullOrEmpty(ing.Glyph) ? ing.Glyph : "?";
                    string tint = ing != null ? ing.Tint : null;
                    ingredients.Add(new CraftIngredientVM(line.IngredientId, dn, glyph, tint,
                        have, line.Count, shown, met));
                }
            }

            bool canCraft = !alreadyCrafted && inv != null && inv.CanCraft(recipe);
            string resultGlyph = string.IsNullOrEmpty(recipe.ResultGlyph) ? "?" : recipe.ResultGlyph;

            return new CraftRecipeVM(recipe.Id,
                recipe.DisplayName ?? new LocalizedText("common.recipe").Resolve(), recipe.Description,
                resultGlyph, ingredients, canCraft, alreadyCrafted, outputHeld: 0);
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}
