// =============================================================================
// JewelerVM — the jeweler jewelry-crafting panel's PURE ViewModel (MVVM slice).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Items
//
// Mirrors CraftingVM (the Apothecary lane). ALL state + logic lives here,
// view-agnostic:
//   * implements DeNelle.Core.UI.Mvvm.IPanelViewModel (Title / Changed / Close / Dispose)
//   * NO UnityEngine UI types — the View resolves all presentation. Unit-testable
//     without a scene (ARCHITECTURE_PRINCIPLES §2).
//   * the View binds it, re-renders on Changed, and routes user input back as commands.
//
// REUSES existing systems (no new architecture):
//   * recipes -> JewelerRecipeCatalog.All (jeweler-recipes.json)
//   * output / base -> GearCatalog.FindAccessory (accessories.json: name + iconPath)
//   * gems    -> MaterialCatalog (materials.json crystal ingredients: name + iconPath)
//   * have    -> VillageInventory.Instance.Get(id)
//   * craft   -> JewelerCraftingService.CanCraft / Craft (atomic)
// Subscribes to VillageInventory.Changed so a drop/craft re-renders the cards.
// Reuses CraftIngredientVM (CraftingVM.cs) for the base + gem checklist lines.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Catalog;       // StructureRoles — the single naming authority
using DeNelle.Core.UI.Mvvm;
using DeNelle.Core.UI;
using DeNelle.Village;            // GearCatalog
using DeNelle.Village.Crafting;   // JewelerRecipeCatalog, JewelerCraftingService, VillageInventory

namespace DeNelle.Village.Items
{
    /// <summary>One stat/ability grant the output piece bestows — a pure data relay of one
    /// non-zero bonus field on the accessory def (WO-693). Label = friendly stat name
    /// ("Max health"); Value = the formatted grant ("+50" / "+7%").</summary>
    public readonly struct BestowLineVM
    {
        public readonly string Label;
        public readonly string Value;

        public BestowLineVM(string label, string value)
        {
            Label = label ?? "";
            Value = value ?? "";
        }
    }

    /// <summary>One jeweler recipe card's view-agnostic payload: output identity + the base/gem
    /// checklist + an optional wallet cost line + whether it can be crafted right now.
    /// WO-693 adds pure relays of existing accessories.json fields: rarity / req.level /
    /// flavor / the non-zero bonus grants / the structured cost.</summary>
    public readonly struct JewelerRecipeVM
    {
        public readonly string RecipeId;
        public readonly string OutputId;
        public readonly string DisplayName;     // the "Set the …" label
        public readonly string OutputName;      // the upgraded accessory's display name
        public readonly string OutputIconPath;
        public readonly IReadOnlyList<CraftIngredientVM> Ingredients; // base first, then gems
        public readonly string CostLabel;       // "Iron 60, Crystals 10" or "" when free
        public readonly bool CanCraft;
        public readonly string Rarity;          // def.rarity ("epic") or ""
        public readonly int ReqLevel;           // def.req.level or 0
        public readonly string Flavor;          // def.flavor (fallback def.saga) or ""
        public readonly IReadOnlyList<BestowLineVM> Bestows;    // every non-zero bonus field
        public readonly IReadOnlyList<CostPart> CostChips; // structured wallet cost

        public JewelerRecipeVM(string recipeId, string outputId, string displayName, string outputName,
                               string outputIconPath, IReadOnlyList<CraftIngredientVM> ingredients,
                               string costLabel, bool canCraft,
                               string rarity, int reqLevel, string flavor,
                               IReadOnlyList<BestowLineVM> bestows,
                               IReadOnlyList<CostPart> costChips)
        {
            RecipeId = recipeId;
            OutputId = outputId;
            DisplayName = displayName ?? "";
            OutputName = outputName ?? "";
            OutputIconPath = outputIconPath ?? "";
            Ingredients = ingredients ?? Array.Empty<CraftIngredientVM>();
            CostLabel = costLabel ?? "";
            CanCraft = canCraft;
            Rarity = rarity ?? "";
            ReqLevel = reqLevel;
            Flavor = flavor ?? "";
            Bestows = bestows ?? Array.Empty<BestowLineVM>();
            CostChips = costChips ?? Array.Empty<CostPart>();
        }
    }

    /// <summary>
    /// Pure ViewModel for the Jeweler's Bench. Exposes <see cref="Recipes"/> (one
    /// <see cref="JewelerRecipeVM"/> per authored recipe) and the <see cref="Craft"/> command.
    /// Raises <see cref="Changed"/> after each craft and on any inventory change.
    /// </summary>
    public sealed class JewelerVM : IPanelViewModel, IDisposable
    {
        private readonly Action _onClose;
        private readonly Action _invHandler;
        private bool _disposed;

        private readonly List<JewelerRecipeVM> _recipes = new List<JewelerRecipeVM>();

        public JewelerVM(Action onClose)
        {
            _onClose = onClose;

            var inv = VillageInventory.Instance;
            if (inv != null)
            {
                _invHandler = Raise;
                inv.Changed += _invHandler;
            }

            Rebuild();
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        // The catalog row claiming the jeweler role owns this word (it reads "Jeweler", not
        // "Jeweler's Bench"). WO-1161: never retype a player-facing building name — a literal
        // here silently outlives the next creative rename. Generic fallback only.
        public string Title { get; private set; } =
            StructureRoles.By[StructureRole.Jeweler].DisplayName ?? "Jewelry";

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var inv = VillageInventory.Instance;
            if (inv != null && _invHandler != null) inv.Changed -= _invHandler;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>Every jeweler recipe (output + base/gem checklist + cost + can-craft). Never null.</summary>
        public IReadOnlyList<JewelerRecipeVM> Recipes => _recipes;

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Craft a recipe via the atomic JewelerCraftingService.Craft. Re-projects on
        /// success or failure so the cards reflect the new inventory counts.</summary>
        public void Craft(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) return;
            JewelerCraftingService.Craft(recipeId);
            Rebuild();   // VillageInventory.Changed also fires on success; rebuild is idempotent
            Raise();
        }

        // ── Projection (no Unity types) ──────────────────────────────────────────

        private void Rebuild()
        {
            _recipes.Clear();

            var recipes = JewelerRecipeCatalog.All;
            if (recipes == null) return;

            var inv = VillageInventory.Instance;

            foreach (var r in recipes)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;

                var lines = new List<CraftIngredientVM>();

                // BASE accessory line (icon + name + have/need) — resolved via GearCatalog.
                if (r.Base != null && !string.IsNullOrEmpty(r.Base.Id))
                {
                    var baseDef = GearCatalog.FindAccessory(r.Base.Id);
                    string baseName = baseDef != null && !string.IsNullOrEmpty(baseDef.name) ? baseDef.name : r.Base.Id;
                    string baseIcon = baseDef != null ? baseDef.iconPath : null;
                    int baseHave = inv != null ? inv.Get(r.Base.Id) : 0;
                    lines.Add(new CraftIngredientVM(r.Base.Id, baseName, baseIcon, baseHave, r.Base.Count));
                }

                // GEM lines — resolved via MaterialCatalog (crystal ingredients reused as gems).
                if (r.Gems != null)
                {
                    foreach (var g in r.Gems)
                    {
                        if (g == null || string.IsNullOrEmpty(g.Id)) continue;
                        int have = inv != null ? inv.Get(g.Id) : 0;
                        lines.Add(new CraftIngredientVM(
                            g.Id,
                            MaterialCatalog.DisplayName(g.Id),
                            MaterialCatalog.IconPath(g.Id),
                            have,
                            g.Count));
                    }
                }

                var outDef = GearCatalog.FindAccessory(r.OutputAccessoryId);
                string outName = outDef != null && !string.IsNullOrEmpty(outDef.name)
                    ? outDef.name : (r.OutputAccessoryId ?? "");
                string outIcon = outDef != null ? outDef.iconPath : null;

                // WO-693 pure relays of existing accessories.json fields (the card is a READER
                // of the def — no new game state, no logic).
                string rarity = outDef != null ? outDef.rarity : null;
                int reqLevel = outDef != null && outDef.req != null ? outDef.req.level : 0;
                string flavor = outDef != null
                    ? (!string.IsNullOrEmpty(outDef.flavor) ? outDef.flavor : outDef.saga)
                    : null;

                _recipes.Add(new JewelerRecipeVM(
                    r.Id,
                    r.OutputAccessoryId,
                    r.DisplayName,
                    outName,
                    outIcon,
                    lines,
                    CostLabel(r.Cost),
                    JewelerCraftingService.CanCraft(r.Id),
                    rarity,
                    reqLevel,
                    flavor,
                    BuildBestows(outDef),
                    BuildCostChips(r.Cost)));
            }
        }

        // ── WO-693: generic bestows relay ────────────────────────────────────────
        // Enumerates EVERY public numeric field on AccessoryDef via reflection so a future
        // stat (e.g. manaBonus) appears on the card with NO View/VM edit beyond an optional
        // friendly name — the owner's lookup-table + thin-interpreter shape. Shop-price
        // fields are excluded; zero/negative grants are skipped (no empty rows).

        private static readonly HashSet<string> s_nonStatFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "buyWood", "buyFood", "buyIron", "buyCrystals"
        };

        private static readonly Dictionary<string, string> s_statNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "hpBonus",    "Max health" },
            { "defense",    "Defense" },
            { "damageMult", "Damage" },
        };

        private static IReadOnlyList<BestowLineVM> BuildBestows(AccessoryDef def)
        {
            if (def == null) return Array.Empty<BestowLineVM>();
            var rows = new List<BestowLineVM>();
            var fields = typeof(AccessoryDef).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (s_nonStatFields.Contains(f.Name)) continue;

                string label = s_statNames.TryGetValue(f.Name, out var friendly)
                    ? friendly : Prettify(f.Name);

                if (f.FieldType == typeof(int))
                {
                    int v = (int)f.GetValue(def);
                    if (v > 0) rows.Add(new BestowLineVM(label, "+" + v));
                }
                else if (f.FieldType == typeof(float))
                {
                    float v = (float)f.GetValue(def);
                    // Fractional bonuses (0.07 defense / 0.10 damageMult) read as percentages.
                    if (v > 0f) rows.Add(new BestowLineVM(label, "+" + (int)Math.Round(v * 100.0) + "%"));
                }
            }
            return rows;
        }

        /// <summary>"manaBonus" -> "Mana bonus" (fallback for a stat with no friendly name).</summary>
        private static string Prettify(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return "";
            var sb = new System.Text.StringBuilder(fieldName.Length + 4);
            sb.Append(char.ToUpperInvariant(fieldName[0]));
            for (int i = 1; i < fieldName.Length; i++)
            {
                char c = fieldName[i];
                if (char.IsUpper(c)) { sb.Append(' '); sb.Append(char.ToLowerInvariant(c)); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>WO-693: the wallet cost as structured data (currency key + name + amount)
        /// so the View renders the WO-675/676 currency chips. Empty when free.</summary>
        private static IReadOnlyList<CostPart> BuildCostChips(JewelerRecipeCost cost)
        {
            if (cost == null) return Array.Empty<CostPart>();
            return CostFormat.Parts(new[] { ("wood", "Wood", cost.Wood), ("stone", "Stone", cost.Stone), ("iron", "Iron", cost.Iron), ("crystal", "Crystals", cost.Crystals) });
        }

        /// <summary>"Iron 60, Crystals 10" — only the non-zero wallet costs; "" when free.</summary>
        private static string CostLabel(JewelerRecipeCost cost)
        {
            if (cost == null) return "";
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[] { ("wood", "Wood", cost.Wood), ("stone", "Stone", cost.Stone), ("iron", "Iron", cost.Iron), ("crystal", "Crystals", cost.Crystals) });
            return DeNelle.Core.UI.CostFormat.Words(parts);
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}
