// =============================================================================
// JewelerRecipeCatalog — WO-553 jeweler jewelry-crafting recipe data loader.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Crafting
//
// Loads jeweler-recipes.json (dual-copied: StreamingAssets source + Resources copy
// that WINS at load in WebGL) via DeNelle.Core.CanonicalJson — exactly the pattern of
// GearCraftingRecipeCatalog.cs / ConsumableCraftingCatalog.cs. This is the SEPARATE
// recipe file for the JEWELER lane (TIER-UP a ring/amulet + gems -> a higher-rarity
// accessory), distinct from gear-recipes.json (Forge) and consumable-recipes.json
// (Apothecary). Keeping it separate means the existing crafting stations are untouched.
//
// Shape (jeweler-recipes.json):
//   {
//     "version": 1,
//     "recipes": [
//       {
//         "id": "jewel_ring_steadfast",
//         "displayName": "Set the Steadfast Ring",
//         "base":   { "id": "ring_iron", "count": 1 },     // an accessories.json id consumed
//         "gems":   [ { "id": "ing_ember_crystal", "count": 2 } ], // materials.json ids consumed
//         "outputAccessoryId": "ring_steadfast",            // an accessories.json id granted
//         "cost":   { "wood": 0, "food": 0, "iron": 30, "crystals": 0 },
//         "requiresQuestId": "",                            // optional QuestService gate
//         "saga": "..."                                     // optional flavor text
//       }
//     ]
//   }
//
// GEMS (owner decision 2026-06-28): the EXISTING crystal-category ingredients in
// materials.json (ing_ember_crystal / ing_aether_shard / ing_heartstone_crystal) ARE
// the gems — no new gem family authored. They drop from bosses at a low rate (loot
// wiring owned by a separate agent).
//
// PURE DATA + lookup. No spending logic here (that's JewelerCraftingService).
// GRACEFUL: a missing/empty catalog yields zero recipes; never throws.
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
    public sealed class JewelerRecipeCost
    {
        [JsonProperty("wood")] public int Wood;
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("food")] public int Stone;
        [JsonProperty("iron")] public int Iron;
        [JsonProperty("crystals")] public int Crystals;
    }

    /// <summary>An inventory-keyed id + count (the base accessory, or one gem line).</summary>
    [Serializable]
    public sealed class JewelerRecipeItem
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("count")] public int Count = 1;
    }

    /// <summary>One jeweler recipe: consume base accessory + gems (+ wallet cost) -> grant a higher-tier accessory id.</summary>
    [Serializable]
    public sealed class JewelerRecipeDef
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("displayName")] public string DisplayName;

        /// <summary>The accessories.json id consumed from VillageInventory (the piece you upgrade).</summary>
        [JsonProperty("base")] public JewelerRecipeItem Base = new JewelerRecipeItem();

        /// <summary>The gem material ids (materials.json) consumed from VillageInventory.</summary>
        [JsonProperty("gems")] public List<JewelerRecipeItem> Gems = new List<JewelerRecipeItem>();

        /// <summary>The accessories.json id granted into inventory (the better piece).</summary>
        [JsonProperty("outputAccessoryId")] public string OutputAccessoryId;

        [JsonProperty("cost")] public JewelerRecipeCost Cost = new JewelerRecipeCost();

        /// <summary>If non-empty, the recipe is locked until QuestService says this quest id is complete.</summary>
        [JsonProperty("requiresQuestId")] public string RequiresQuestId;

        /// <summary>Optional saga flavor text shown on the recipe card.</summary>
        [JsonProperty("saga")] public string Saga;
    }

    [Serializable]
    public sealed class JewelerRecipeData
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("recipes")] public List<JewelerRecipeDef> Recipes = new List<JewelerRecipeDef>();
    }

    public static class JewelerRecipeCatalog
    {
        private const string CanonicalRelativePath = "Data/Canonical/jeweler-recipes.json";
        private static JewelerRecipeData _data;

        public static IReadOnlyList<JewelerRecipeDef> All
        { get { EnsureLoaded(); return _data.Recipes; } }

        public static JewelerRecipeDef Find(string id)
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
                var json = CanonicalJson.Read(CanonicalRelativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = JsonConvert.DeserializeObject<JewelerRecipeData>(json);
                    if (parsed != null)
                    {
                        _data = parsed;
                        if (_data.Recipes == null) _data.Recipes = new List<JewelerRecipeDef>();
                        return;
                    }
                    Debug.LogWarning($"[JewelerRecipeCatalog] {CanonicalRelativePath} parsed empty.");
                }
                else Debug.LogWarning($"[JewelerRecipeCatalog] no Resources copy or StreamingAssets file for {CanonicalRelativePath} — jeweler crafting disabled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JewelerRecipeCatalog] Read failed: {ex.Message}");
            }
            _data = new JewelerRecipeData();
        }
    }
}
