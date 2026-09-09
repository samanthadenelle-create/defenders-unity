// =============================================================================
// VillageStrings — read-only canon-string resolver for the Village module (Wk4)
// -----------------------------------------------------------------------------
// The Village UI must NEVER hardcode a canon string (port spec Part 4: "the
// Unity agent never types these inline"). The build menu needs the five
// building display names ("Crystal Mine" ...) and their descriptions, all of
// which already live in the canonical JSON:
//
//   canon-strings.json — proper nouns: crystalMine / petHouse / arcaneTower /
//                        workshop / farm  (the BuildingDef.displayName keys).
//   LocalText          — localizable copy: buildingDesc.* description strings.
//
// CanonStrings.cs (DeNelle.Onboarding) does the identical job for the title
// scene, but the Village asmdef does NOT reference Onboarding and must not
// grow a cross-module dependency just for string lookup (asmdef isolation,
// port spec Part 2). This compatibility facade keeps canon proper-name lookup
// local while routing all translatable copy through the global LocalText seam.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// Static read-only access to the canonical strings the Village module
    /// needs: <c>canon-strings.json</c> for protected proper nouns and
    /// <see cref="LocalText"/> for localizable copy.
    /// </summary>
    public static class VillageStrings
    {
        /// <summary>StreamingAssets-relative path to the canon proper-noun file.</summary>
        private const string CanonRelativePath = "Data/Canonical/canon-strings.json";

        private static Dictionary<string, string> _canon;

        /// <summary>
        /// Resolves a key from <c>canon-strings.json</c> (the proper nouns —
        /// e.g. <c>crystalMine</c> -> "Crystal Mine"). Returns a visible
        /// <c>[[missing:key]]</c> marker for an unknown key.
        /// </summary>
        public static string Canon(string key)
        {
            EnsureLoaded();
            return Resolve(_canon, key);
        }

        /// <summary>
        /// Resolves a key through <see cref="LocalText"/> (the localizable copy, e.g.
        /// <c>buildingDesc.crystalMine</c>). Returns a visible
        /// <c>[[missing:key]]</c> marker for an unknown key.
        /// </summary>
        public static string Locale(string key) => LocalText.Get(key);

        /// <summary>
        /// Resolves a building's display name from its <see cref="BuildingDef"/>.
        /// <see cref="BuildingDef.DisplayName"/> is a canon-strings KEY, not a
        /// literal — this is the single place the Village UI turns it into the
        /// player-facing string.
        /// </summary>
        public static string BuildingName(BuildingDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.DisplayName)) return "[[missing:building]]";
            return Canon(def.DisplayName);
        }

        /// <summary>Resolves a building's flavour description from its <see cref="BuildingDef"/>.</summary>
        public static string BuildingDescription(BuildingDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.DescriptionKey)) return string.Empty;
            return Locale(def.DescriptionKey);
        }

        // =====================================================================
        //  Loading  (mirrors CanonStrings.LoadMap — flat string->string maps)
        // =====================================================================

        private static void EnsureLoaded()
        {
            if (_canon == null) _canon = LoadMap(CanonRelativePath);
        }

        private static Dictionary<string, string> LoadMap(string relativePath)
        {
            // WebGL-safe load via CanonicalJson (Resources first, StreamingAssets fallback).
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(relativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    // Both canonical files are flat maps with some leading "_"
                    // metadata keys (e.g. "_comment") and a couple of nested
                    // "_sources" objects — deserialize loosely, keep only the
                    // string-valued entries (the CanonStrings.cs convention).
                    var raw = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    var map = new Dictionary<string, string>();
                    if (raw != null)
                    {
                        foreach (var kv in raw)
                            if (kv.Value is string s) map[kv.Key] = s;
                    }
                    return map;
                }
                Debug.LogError($"[VillageStrings] Canonical file not found (Resources or StreamingAssets): {relativePath}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VillageStrings] Failed to read {relativePath}: {ex.Message}");
            }
            return new Dictionary<string, string>();
        }

        private static string Resolve(Dictionary<string, string> map, string key)
        {
            if (map != null && key != null && map.TryGetValue(key, out var value) && value != null)
                return value;
            Debug.LogWarning($"[VillageStrings] Missing canonical key '{key}'.");
            return $"[[missing:{key}]]";
        }
    }
}
