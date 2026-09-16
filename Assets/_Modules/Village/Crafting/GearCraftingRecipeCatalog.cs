// =============================================================================
// GearCraftingRecipeCatalog — WO-293 tiered-gear + legendary recipe data loader.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Crafting
//
// Loads gear-recipes.json (dual-copied: StreamingAssets source + Resources copy that
// WINS at load in WebGL) via DeNelle.Core.CanonicalJson — exactly the pattern of
// CraftingRecipeCatalog.cs. This is the SEPARATE recipe file for crafting TIERED GEAR
// (and the legendary Aegis reforge), distinct from crafting-recipes.json (consumables/
// torch lane) and consumable-recipes.json (drop-material lane). Keeping it separate
// means the existing crafting stations are untouched (RECONCILE, not replace).
//
// Shape (gear-recipes.json):
//   {
//     "version": 1,
//     "recipes": [
//       {
//         "id": "knight_oath_master",
//         "displayName": "Oathkeeper (Master)",
//         "outputGearId": "knight_oath",   // an existing weapons.json / armor.json id
//         "outputKind": "weapon",          // "weapon" | "armor"
//         "tier": "Master",                // Common | Fine | Master | Legendary
//         "cost": { "wood": 0, "food": 0, "iron": 40, "crystals": 10 },
//         "components": [ { "id": "quench_oil", "count": 1 } ],
//         "requiresQuestId": "",           // legendary recipes gate on a completed quest
//         "saga": "..."                    // optional flavor text
//       }
//     ]
//   }
//
// PURE DATA + lookup. No spending logic here (that's GearCraftingService).
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core;

namespace DeNelle.Village.Crafting
{
    /// <summary>A resource cost block, mirrors EconomyService.ResourceCost fields.</summary>
    [Serializable]
    public sealed class GearRecipeCost
    {
        [JsonProperty("wood")] public int Wood;
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("food")] public int Stone;
        [JsonProperty("iron")] public int Iron;
        [JsonProperty("crystals")] public int Crystals;
    }

    /// <summary>A required crafting component (an inventory-keyed id + count).</summary>
    [Serializable]
    public sealed class GearRecipeComponent
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("count")] public int Count = 1;
    }

    /// <summary>One tiered-gear recipe: spend cost + components -> grant a tiered gear id.</summary>
    [Serializable]
    public sealed class GearRecipeDef
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("displayName")] public string DisplayName;

        /// <summary>The weapons.json / armor.json id this recipe produces (already-existing gear).</summary>
        [JsonProperty("outputGearId")] public string OutputGearId;

        /// <summary>"weapon" or "armor" — which catalog OutputGearId lives in.</summary>
        [JsonProperty("outputKind")] public string OutputKind;

        /// <summary>Tier string: Common | Fine | Master | Legendary (parsed via GearTierTable).</summary>
        [JsonProperty("tier")] public string Tier;

        [JsonProperty("cost")] public GearRecipeCost Cost = new GearRecipeCost();
        [JsonProperty("components")] public List<GearRecipeComponent> Components = new List<GearRecipeComponent>();

        /// <summary>If non-empty, the recipe is locked until QuestService says this quest id is complete
        /// (the legendary Aegis reforge gates on Act IV).</summary>
        [JsonProperty("requiresQuestId")] public string RequiresQuestId;

        /// <summary>Optional saga flavor text shown on the crafted item.</summary>
        [JsonProperty("saga")] public string Saga;

        /// <summary>Parsed tier (defaults Common on unknown/empty).</summary>
        public GearTier ParsedTier => GearTierTable.Parse(Tier);

        public bool IsLegendary => ParsedTier == GearTier.Legendary;
    }

    [Serializable]
    public sealed class GearRecipeData
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("recipes")] public List<GearRecipeDef> Recipes = new List<GearRecipeDef>();
    }

    public static class GearCraftingRecipeCatalog
    {
        private const string StreamingRelativePath = "Data/Canonical/gear-recipes.json";
        private static GearRecipeData _data;

        public static IReadOnlyList<GearRecipeDef> All
        { get { EnsureLoaded(); return _data.Recipes; } }

        public static GearRecipeDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            foreach (var r in _data.Recipes)
                if (r != null && string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        public static void Reload() { _data = null; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_data != null) return;

            // WebGL-safe: Resources.Load first (browser), StreamingAssets File fallback.
            try
            {
                var json = CanonicalJson.Read(StreamingRelativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = JsonConvert.DeserializeObject<GearRecipeData>(json);
                    if (parsed != null && parsed.Recipes != null)
                    { _data = parsed; return; }
                    Debug.LogWarning($"[GearCraftingRecipeCatalog] {StreamingRelativePath} parsed empty.");
                }
                else Debug.LogWarning($"[GearCraftingRecipeCatalog] no Resources copy or StreamingAssets file for {StreamingRelativePath} — gear crafting disabled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GearCraftingRecipeCatalog] Read failed: {ex.Message}");
            }
            _data = new GearRecipeData();
        }
    }
}
