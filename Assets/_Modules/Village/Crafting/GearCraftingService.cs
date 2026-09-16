// =============================================================================
// GearCraftingService — WO-293 CanCraft / Craft API for tiered + legendary gear.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Crafting
//
// Lives in DeNelle.Village (NOT Core) precisely because it must call the Village-side
// EconomyService (the unified wallet) and Village inventory — Core can't reference
// Village (CS0234). It DOES read Core's QuestService for the legendary Act-IV gate;
// Village -> Core is the legal direction.
//
// FLOW (atomic):
//   CanCraft(recipeId): recipe known + not quest-locked + wallet covers cost +
//                       inventory covers every component.
//   Craft(recipeId):    re-verify, then spend the unified wallet ONCE via
//                       EconomyService.TrySpend (the SINGLE source of truth — never a
//                       stale local balance; crystals/food round-trip through GameState),
//                       consume components from VillageInventory, and grant the crafted
//                       tiered gear id into VillageInventory so GearLoadout.EquipWeaponById
//                       /EquipArmorById can equip it. Spend is rolled back if a component
//                       consume fails (defensive — CanCraft already guarantees coverage).
//
// EFFECTIVE STATS: a crafted item's power = base def stat (weapons.json/armor.json)
//   x the tier's GearTierTable.StatMultiplier. Exposed via EffectiveWeaponMult /
//   EffectiveArmorDefense so the equip/UI layer can show the tiered value without
//   duplicating the base catalog.
//
// GRACEFUL: every method null-guards and returns false rather than throwing.
// =============================================================================

using DeNelle.Core.Quests;
using DeNelle.Village;        // EconomyService, ResourceCost, GearCatalog (parent namespace)
using UnityEngine;

namespace DeNelle.Village.Crafting
{
    public static class GearCraftingService
    {
        /// <summary>Result of a craft attempt — the granted gear id + tier, or a failure reason.</summary>
        public struct CraftResult
        {
            public bool Success;
            public string GearId;
            public string OutputKind;   // "weapon" | "armor"
            public GearTier Tier;
            public string FailReason;   // human-readable, for toast/log

            public static CraftResult Fail(string reason) =>
                new CraftResult { Success = false, FailReason = reason };
        }

        /// <summary>Fires after a successful craft (UI/HUD can refresh inventory + offer equip).</summary>
        public static event System.Action<CraftResult> OnCrafted;

        /// <summary>
        /// True when the recipe exists, its quest gate (if any) is satisfied, the unified
        /// wallet covers the cost, and the inventory covers every component.
        /// </summary>
        public static bool CanCraft(string recipeId) => Evaluate(recipeId, out _).Length == 0;

        /// <summary>Human-readable reason CanCraft is false ("" when it can craft, recipe null when unknown).</summary>
        public static string WhyCannotCraft(string recipeId) => Evaluate(recipeId, out _);

        /// <summary>
        /// Craft the recipe: verify, spend the unified wallet, consume components, grant the
        /// tiered gear id into inventory. Atomic — no resource is spent unless everything
        /// is covered; component-consume failure rolls the wallet spend back.
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
            if (inv == null && (recipe.Components != null && recipe.Components.Count > 0))
                return CraftResult.Fail("Inventory not ready.");

            var cost = new ResourceCost(recipe.Cost?.Wood ?? 0, recipe.Cost?.Stone ?? 0,
                                        recipe.Cost?.Iron ?? 0, recipe.Cost?.Crystals ?? 0);

            // Spend the unified wallet ONCE (atomic inside EconomyService).
            if (!cost.IsZero && !economy.TrySpend(cost))
                return CraftResult.Fail("Not enough resources.");

            // Consume components. Defensive rollback if any line is unexpectedly short.
            if (recipe.Components != null)
            {
                foreach (var c in recipe.Components)
                {
                    if (c == null || string.IsNullOrEmpty(c.Id) || c.Count <= 0) continue;
                    if (inv == null || !inv.TryConsume(c.Id, c.Count))
                    {
                        if (!cost.IsZero) economy.Grant(cost); // refund the spend
                        return CraftResult.Fail("Missing crafting component: " + c.Id);
                    }
                }
            }

            // Grant the crafted tiered gear into inventory (keyed by gear id) so it can be equipped.
            var tier = recipe.ParsedTier;
            if (!string.IsNullOrEmpty(recipe.OutputGearId))
                inv?.Add(recipe.OutputGearId, 1);

            var result = new CraftResult
            {
                Success = true,
                GearId = recipe.OutputGearId,
                OutputKind = recipe.OutputKind,
                Tier = tier
            };
            OnCrafted?.Invoke(result);
            return result;
        }

        /// <summary>
        /// Effective outgoing-damage multiplier of a crafted weapon recipe =
        /// base weapon damageMult x tier multiplier. 1.0 if the recipe/base is missing.
        /// </summary>
        public static float EffectiveWeaponMult(string recipeId)
        {
            var recipe = GearCraftingRecipeCatalog.Find(recipeId);
            if (recipe == null) return 1f;
            float baseMult = 1f;
            var w = GearCatalog.FindWeapon(recipe.OutputGearId);
            if (w != null) baseMult = Mathf.Max(0.1f, w.damageMult);
            return baseMult * GearTierTable.StatMultiplier(recipe.ParsedTier);
        }

        /// <summary>
        /// Effective fractional damage reduction of a crafted armor recipe =
        /// base armor defense x tier multiplier, clamped 0..0.9. 0 if missing.
        /// </summary>
        public static float EffectiveArmorDefense(string recipeId)
        {
            var recipe = GearCraftingRecipeCatalog.Find(recipeId);
            if (recipe == null) return 0f;
            var a = GearCatalog.FindArmor(recipe.OutputGearId);
            if (a == null) return 0f;
            return Mathf.Clamp(a.defense * GearTierTable.StatMultiplier(recipe.ParsedTier), 0f, 0.9f);
        }

        // ── Internal: single evaluation path shared by CanCraft + Craft ──────────
        // Returns "" when craftable; otherwise a human-readable reason. `recipe` out is
        // the resolved def (null when the id is unknown).
        private static string Evaluate(string recipeId, out GearRecipeDef recipe)
        {
            recipe = GearCraftingRecipeCatalog.Find(recipeId);
            if (recipe == null) return "Unknown recipe.";

            // Legendary / quest gate (Village -> Core is legal).
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

            if (recipe.Components != null && recipe.Components.Count > 0)
            {
                var inv = VillageInventory.Instance;
                if (inv == null) return "Inventory not ready.";
                foreach (var c in recipe.Components)
                {
                    if (c == null || string.IsNullOrEmpty(c.Id) || c.Count <= 0) continue;
                    if (inv.Get(c.Id) < c.Count) return "Missing crafting component: " + c.Id;
                }
            }

            return string.Empty;
        }
    }
}
