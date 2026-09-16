// =============================================================================
// JewelerCraftingService — WO-553 CanCraft / Craft API for jewelry tier-up.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Crafting
//
// Mirrors GearCraftingService.cs (the closest mechanical analog — jewelry is
// equippable). Lives in DeNelle.Village (NOT Core) because it calls the Village-side
// EconomyService (the unified wallet) + VillageInventory; it reads Core's QuestService
// for the optional gate (Village -> Core is the legal direction).
//
// FLOW (atomic):
//   CanCraft(recipeId): recipe known + not quest-locked + wallet covers cost +
//                       inventory covers the base accessory + every gem.
//   Craft(recipeId):    re-verify, then spend the unified wallet ONCE via
//                       EconomyService.TrySpend, consume the base accessory + each gem
//                       from VillageInventory, and grant OutputAccessoryId into
//                       VillageInventory so EquipVM can equip the upgraded piece.
//                       Atomic — nothing is spent unless everything is covered; a
//                       consume failure rolls the spend back AND restores anything
//                       already consumed (defensive — Evaluate already proved coverage).
//
// GRACEFUL: every method null-guards and returns false/Fail rather than throwing.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Quests;
using DeNelle.Village;        // EconomyService, ResourceCost, GearCatalog (parent namespace)

namespace DeNelle.Village.Crafting
{
    public static class JewelerCraftingService
    {
        /// <summary>Result of a craft attempt — the granted accessory id, or a failure reason.</summary>
        public struct CraftResult
        {
            public bool Success;
            public string AccessoryId;   // the granted higher-tier accessory id
            public string FailReason;    // human-readable, for toast/log

            public static CraftResult Fail(string reason) =>
                new CraftResult { Success = false, FailReason = reason };
        }

        /// <summary>Fires after a successful craft (UI/HUD can refresh inventory + offer equip).</summary>
        public static event System.Action<CraftResult> OnCrafted;

        /// <summary>
        /// True when the recipe exists, its quest gate (if any) is satisfied, the unified
        /// wallet covers the cost, and the inventory covers the base accessory + every gem.
        /// </summary>
        public static bool CanCraft(string recipeId) => Evaluate(recipeId, out _).Length == 0;

        /// <summary>Human-readable reason CanCraft is false ("" when it can craft).</summary>
        public static string WhyCannotCraft(string recipeId) => Evaluate(recipeId, out _);

        /// <summary>
        /// Craft the recipe: verify, spend the unified wallet, consume base + gems, grant the
        /// upgraded accessory id into inventory. Atomic — no resource is spent unless everything
        /// is covered; a consume failure refunds the spend and restores prior consumes.
        /// </summary>
        public static CraftResult Craft(string recipeId)
        {
            string why = Evaluate(recipeId, out var recipe);
            if (why.Length != 0 || recipe == null)
                return CraftResult.Fail(string.IsNullOrEmpty(why) ? "Unknown recipe." : why);

            var economy = EconomyService.Instance;
            var inv = VillageInventory.Instance;
            // Evaluate() already proved these non-null + sufficient; re-guard defensively.
            if (economy == null) return CraftResult.Fail("Economy not ready.");
            if (inv == null) return CraftResult.Fail("Inventory not ready.");

            var cost = new ResourceCost(recipe.Cost?.Wood ?? 0, recipe.Cost?.Stone ?? 0,
                                        recipe.Cost?.Iron ?? 0, recipe.Cost?.Crystals ?? 0);

            // Spend the unified wallet ONCE (atomic inside EconomyService).
            if (!cost.IsZero && !economy.TrySpend(cost))
                return CraftResult.Fail("Not enough resources.");

            // Track what we consume so we can fully roll back on any failure.
            var consumed = new List<(string id, int count)>();

            // Consume the base accessory.
            if (recipe.Base != null && !string.IsNullOrEmpty(recipe.Base.Id) && recipe.Base.Count > 0)
            {
                if (!inv.TryConsume(recipe.Base.Id, recipe.Base.Count))
                    return Rollback(economy, cost, inv, consumed, "Missing base piece: " + recipe.Base.Id);
                consumed.Add((recipe.Base.Id, recipe.Base.Count));
            }

            // Consume each gem.
            if (recipe.Gems != null)
            {
                foreach (var g in recipe.Gems)
                {
                    if (g == null || string.IsNullOrEmpty(g.Id) || g.Count <= 0) continue;
                    if (!inv.TryConsume(g.Id, g.Count))
                        return Rollback(economy, cost, inv, consumed, "Missing gem: " + g.Id);
                    consumed.Add((g.Id, g.Count));
                }
            }

            // Grant the upgraded accessory into inventory (keyed by id) so it can be equipped.
            if (!string.IsNullOrEmpty(recipe.OutputAccessoryId))
                inv.Add(recipe.OutputAccessoryId, 1);

            var result = new CraftResult { Success = true, AccessoryId = recipe.OutputAccessoryId };
            OnCrafted?.Invoke(result);
            return result;
        }

        // Refund the wallet spend + restore anything already consumed, then fail with the reason.
        private static CraftResult Rollback(EconomyService economy, ResourceCost cost,
            VillageInventory inv, List<(string id, int count)> consumed, string reason)
        {
            if (!cost.IsZero) economy?.Grant(cost);
            if (inv != null)
                foreach (var c in consumed) inv.Add(c.id, c.count);
            return CraftResult.Fail(reason);
        }

        // ── Internal: single evaluation path shared by CanCraft + Craft ──────────
        // Returns "" when craftable; otherwise a human-readable reason. `recipe` out is
        // the resolved def (null when the id is unknown).
        private static string Evaluate(string recipeId, out JewelerRecipeDef recipe)
        {
            recipe = JewelerRecipeCatalog.Find(recipeId);
            if (recipe == null) return "Unknown recipe.";

            // Optional quest gate (Village -> Core is legal).
            if (!string.IsNullOrEmpty(recipe.RequiresQuestId))
            {
                var quests = QuestService.Instance;
                if (quests == null || !quests.IsCompleted(recipe.RequiresQuestId))
                    return "Locked — complete the saga first.";
            }

            var economy = EconomyService.Instance;
            if (economy == null) return "Economy not ready.";

            var cost = new ResourceCost(recipe.Cost?.Wood ?? 0, recipe.Cost?.Stone ?? 0,
                                        recipe.Cost?.Iron ?? 0, recipe.Cost?.Crystals ?? 0);
            if (!cost.IsZero && !economy.CanAfford(cost)) return "Not enough resources.";

            var inv = VillageInventory.Instance;
            if (inv == null) return "Inventory not ready.";

            if (recipe.Base != null && !string.IsNullOrEmpty(recipe.Base.Id) && recipe.Base.Count > 0)
                if (inv.Get(recipe.Base.Id) < recipe.Base.Count) return "Missing base piece: " + recipe.Base.Id;

            if (recipe.Gems != null)
            {
                foreach (var g in recipe.Gems)
                {
                    if (g == null || string.IsNullOrEmpty(g.Id) || g.Count <= 0) continue;
                    if (inv.Get(g.Id) < g.Count) return "Missing gem: " + g.Id;
                }
            }

            return string.Empty;
        }
    }
}
