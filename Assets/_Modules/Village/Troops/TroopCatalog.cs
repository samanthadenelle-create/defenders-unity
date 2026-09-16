// =============================================================================
// TroopCatalog — typed model + loader for the canonical troops.json (WO-453).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Verbatim clone of PetCatalog's loading strategy: the buildable troops are
// CONTENT, not code. TroopCatalog reads
// Assets/StreamingAssets/Data/Canonical/troops.json at load time (mirrored into
// Assets/Resources/Data/Canonical/troops.json so the WebGL-safe Resources path
// resolves) and hydrates typed TroopDef records via Newtonsoft.Json, behind
// DeNelle.Core.CanonicalJson (Resources first, StreamingAssets fallback).
//
// TroopController / TroopFactory read TroopDefs from here; they never type troop
// names / stats inline (canon strings + tuning flow from JSON).
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>The parsed troops.json root.</summary>
    [Serializable]
    public sealed class TroopCatalogData
    {
        [JsonProperty("version")] public int Version;
        /// <summary>The buildable troop defs.</summary>
        [JsonProperty("troops")] public List<TroopDef> Troops = new List<TroopDef>();
    }

    /// <summary>
    /// Static surface over the canonical troops.json — loads + caches the troop
    /// defs and exposes lookup by id. The PetCatalog.cs loading pattern (WO-453).
    /// </summary>
    public static class TroopCatalog
    {
        private const string StreamingRelativePath = "Data/Canonical/troops.json";

        private static TroopCatalogData _data;

        /// <summary>All troop defs, in catalog order (Footman, Archer).</summary>
        public static IReadOnlyList<TroopDef> All
        {
            get { EnsureLoaded(); return _data.Troops; }
        }

        /// <summary>Looks up a troop def by its stable id. Returns null when not found.</summary>
        public static TroopDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            foreach (var troop in _data.Troops)
                if (troop != null && troop.Id == id) return troop;
            foreach (var troop in _data.Troops)
                if (troop?.LegacyIds != null && Array.IndexOf(troop.LegacyIds, id) >= 0) return troop;
            return null;
        }

        /// <summary>Forces a re-read of troops.json (used by tests / the Monday sync).</summary>
        public static void Reload()
        {
            _data = null;
            EnsureLoaded();
        }

        // =====================================================================
        //  Loading — identical strategy to PetCatalog.LoadCatalog().
        // =====================================================================

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            _data = LoadCatalog();
        }

        private static TroopCatalogData LoadCatalog()
        {
            // WebGL-safe load via CanonicalJson: Resources.Load first (works in a
            // browser, where File.ReadAllText(streamingAssetsPath) THROWS), then a
            // StreamingAssets fallback on desktop/editor. troops.json is mirrored into
            // Assets/Resources/Data/Canonical/ so the Resources path resolves.
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(StreamingRelativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = JsonConvert.DeserializeObject<TroopCatalogData>(json);
                    if (parsed != null && parsed.Troops != null && parsed.Troops.Count > 0)
                        return parsed;
                    Debug.LogError("[TroopCatalog] troops.json parsed empty.");
                }
                else
                {
                    Debug.LogError("[TroopCatalog] troops.json not found (Resources or StreamingAssets).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TroopCatalog] Failed to read troops.json: {ex.Message}");
            }

            Debug.LogError("[TroopCatalog] troops.json could not be loaded — using an empty catalog.");
            return new TroopCatalogData { Troops = new List<TroopDef>() };
        }
    }
}
