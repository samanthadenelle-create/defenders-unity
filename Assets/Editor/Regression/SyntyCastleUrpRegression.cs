// =============================================================================
// SyntyCastleUrpRegression — verify raid-clad Synty materials use URP
// =============================================================================
// Regression test that verifies raid-clad materials in PolygonFantasyKingdom/Materials
// and PolygonGeneric/Materials/Alts/ (excluding FX/Flags/Water/Decals) use URP shaders.
// Only scans .mat files; treats other Synty folders as out-of-scope.
// Provides ShouldSkipMaterial predicate for the tool (asmdef allows Editor->EditorRegression).
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>
    /// Verifies raid-clad Synty materials (PolygonFantasyKingdom/Materials and
    /// PolygonGeneric/Materials/Alts) use URP shaders. Fails if any material
    /// (excluding FX/Flags/Water/Decals) does not use URP. Scopes to .mat files only.
    /// </summary>
    public static class SyntyCastleUrpRegression
    {
        // ── Scope: raid-clad material folders ──────────────────────────────────
        private const string RaidCladFolder1 = "Assets/Synty/PolygonFantasyKingdom/Materials";
        private const string RaidCladFolder2 = "Assets/Synty/PolygonGeneric/Materials/Alts";

        private const string OkMarker = "SYNTY_CASTLE_URP_OK";

        // =====================================================================
        //  Skip predicate (called by tool via asmdef-allowed dependency)
        // =====================================================================

        /// <summary>
        /// Returns true if a material path should be skipped during conversion.
        /// Skips FX, Flags, Water, Decals (transparent/cutout/procedural/overlay).
        /// Called by SyntyCastleUrpMaterials. Only path-based checks (no shader lookups).
        /// </summary>
        public static bool ShouldSkipMaterial(string matPath)
        {
            return matPath.Contains("/FX/") ||
                   matPath.Contains("/Flags/") ||
                   matPath.Contains("/Decals/") ||
                   matPath.Contains("Water");
        }

        /// <summary>
        /// Main entry point for DataRegression registration. Scans raid-clad material
        /// folders only (PolygonFantasyKingdom/Materials, PolygonGeneric/Materials/Alts).
        /// Filters .mat files only; treats other Synty folders as out-of-scope.
        /// </summary>
        public static bool Run(out string report)
        {
            var raidCladMaterials = new List<string>();

            // Scan raid-clad folders only.
            if (AssetDatabase.IsValidFolder(RaidCladFolder1))
            {
                var folder1Guids = AssetDatabase.FindAssets("t:Material", new[] { RaidCladFolder1 });
                raidCladMaterials.AddRange(folder1Guids.Select(g => AssetDatabase.GUIDToAssetPath(g))
                    .Where(p => p.EndsWith(".mat")));
            }

            if (AssetDatabase.IsValidFolder(RaidCladFolder2))
            {
                var folder2Guids = AssetDatabase.FindAssets("t:Material", new[] { RaidCladFolder2 });
                raidCladMaterials.AddRange(folder2Guids.Select(g => AssetDatabase.GUIDToAssetPath(g))
                    .Where(p => p.EndsWith(".mat")));
            }

            List<string> nonUrpMaterials = new List<string>();
            int urpCount = 0;
            int skipped = 0;

            foreach (var matPath in raidCladMaterials)
            {
                // Skip FX/Flags/Water/Decals (transparent/cutout/procedural/overlay).
                if (ShouldSkipMaterial(matPath))
                {
                    skipped++;
                    continue;
                }

                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) continue;

                string shaderName = mat.shader?.name;
                if (string.IsNullOrEmpty(shaderName))
                {
                    nonUrpMaterials.Add($"{matPath} (shader null)");
                    continue;
                }

                if (!shaderName.StartsWith("Universal Render Pipeline/"))
                {
                    nonUrpMaterials.Add($"{matPath} (shader: {shaderName})");
                }
                else
                {
                    urpCount++;
                }
            }

            if (nonUrpMaterials.Count > 0)
            {
                report = $"{nonUrpMaterials.Count} non-URP material(s): " +
                         string.Join("; ", nonUrpMaterials.Take(3));
                return false;
            }

            report = $"{urpCount} URP material(s), {skipped} FX/Flags/Water/Decals skipped";
            return true;
        }
    }
}
