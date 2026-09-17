// =============================================================================
// SyntyCastleUrpMaterials — fix Synty castle materials using non-URP shaders
// =============================================================================
// Synty castle/generic asset materials use Synty/Generic_Basic or
// Synty/Generic_Standard (non-URP shaders), which render as pink/magenta
// fallback on URP device builds. This tool converts all affected materials
// to Universal Render Pipeline/Lit, preserving textures, colors, and alpha.
// Excludes FX/Flags/Water/Decals via SyntyCastleUrpRegression.ShouldSkipMaterial.
//
// NOTE: Assets/Synty is gitignored (.gitignore:732), so converted .mat files
// do NOT commit. This TOOL is the deliverable and must be re-run on fresh clones.
//
// IDEMPOTENT. Re-running is safe; already-converted materials are left alone.
//
// HOW TO RUN:
//   • Editor menu:  Defenders ▸ Art ▸ Fix Synty Castle URP Materials
//   • Batch mode :  -executeMethod DeNelle.Editor.SyntyCastleUrpMaterials.Run
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>
    /// Converts all Synty castle and generic materials from non-URP Synty/Generic_Basic
    /// and Synty/Generic_Standard shaders to Universal Render Pipeline/Lit so they
    /// render correctly on URP device builds instead of falling back to pink/magenta.
    /// </summary>
    public static class SyntyCastleUrpMaterials
    {
        // ── Scope ────────────────────────────────────────────────────────────
        private const string SyntyRoot = "Assets/Synty";

        // ── Shader identification ────────────────────────────────────────────
        // The non-URP shader guids that we're replacing.
        private const string NonUrpShaderGuidBasic = "0730dae39bc73f34796280af9875ce14";    // Synty/Generic_Basic
        private const string NonUrpShaderGuidStandard = "3b44a38ec6f81134ab0f820ac54d6a93";  // Synty/Generic_Standard

        // ── Target URP shader ────────────────────────────────────────────────
        // The Universal Render Pipeline lit shader for all materials.
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        // ── OK marker ────────────────────────────────────────────────────────
        private const string OkMarker = "SYNTY_CASTLE_URP_OK";
        private const string FailMarker = "SYNTY_CASTLE_URP_FAIL";

        // =====================================================================
        //  Entry points
        // =====================================================================

        /// <summary>
        /// Menu entry — fixes all Synty castle materials.
        /// </summary>
        [MenuItem("Defenders/Art/Fix Synty Castle URP Materials")]
        public static void RunMenu()
        {
            Run();
        }

        /// <summary>
        /// Batch-runnable entry point
        /// (-executeMethod DeNelle.Editor.SyntyCastleUrpMaterials.Run).
        /// Walks every material under Assets/Synty/, checks if it uses the
        /// non-URP Synty/Generic_Basic shader, and converts it to
        /// Universal Render Pipeline/Lit with proper property mapping.
        /// Excludes FX and Flags (transparent/cutout materials).
        /// </summary>
        public static void Run()
        {
            Shader urpLit = Shader.Find(UrpLitShaderName);
            if (urpLit == null)
            {
                Debug.LogError(
                    $"[SyntyCastleURP] Shader '{UrpLitShaderName}' not found — is the " +
                    "Universal RP package installed? Aborting.");
                Debug.LogError($"{FailMarker}");
                return;
            }

            if (!AssetDatabase.IsValidFolder(SyntyRoot))
            {
                Debug.LogError($"[SyntyCastleURP] Folder '{SyntyRoot}' not found. Aborting.");
                Debug.LogError($"{FailMarker}");
                return;
            }

            var allMaterials = AssetDatabase.FindAssets("t:Material", new[] { SyntyRoot });
            int converted = 0;
            int alreadyUrp = 0;
            int other = 0;
            int skipped = 0;

            foreach (var guid in allMaterials)
            {
                string matPath = AssetDatabase.GUIDToAssetPath(guid);

                // Skip FX, Flags, Water, Decals (transparent/cutout/procedural/overlay, separate tickets).
                // Uses shared predicate from SyntyCastleUrpRegression (asmdef allows Editor to reference EditorRegression).
                if (SyntyCastleUrpRegression.ShouldSkipMaterial(matPath))
                {
                    skipped++;
                    continue;
                }

                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) continue;

                string shaderName = mat.shader.name;

                // Check if this material uses a non-URP Synty shader.
                // Both Generic_Basic and Generic_Standard have the same property structure.
                bool isNonUrpSynty = shaderName != null &&
                    (shaderName.StartsWith("Synty/Generic_Basic") || shaderName.StartsWith("Synty/Generic_Standard"));

                if (!isNonUrpSynty)
                {
                    // Not a non-URP Synty material; check if it's already URP.
                    if (shaderName != null && shaderName.StartsWith("Universal Render Pipeline/"))
                    {
                        alreadyUrp++;
                    }
                    else
                    {
                        other++;
                    }
                    continue;
                }

                // This is a non-URP Synty material (Basic or Standard). Convert it.
                ConvertMaterialToUrp(mat, matPath, urpLit);
                converted++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[SyntyCastleURP] {allMaterials.Length} material(s) scanned: " +
                $"{converted} converted, {alreadyUrp} already URP, {other} other shaders.");
            Debug.Log($"[SyntyCastleURP] skipped {skipped} FX/Flags/Water/Decals material(s) - transparent/cutout/procedural/overlay, separate tickets");
            Debug.Log($"{OkMarker} {converted} material(s)");
        }

        // =====================================================================
        //  Conversion logic
        // =====================================================================

        /// <summary>
        /// Converts a single material from Synty/Generic_Basic to
        /// Universal Render Pipeline/Lit, migrating textures, colors, and alpha settings.
        /// </summary>
        private static void ConvertMaterialToUrp(Material mat, string matPath, Shader urpLit)
        {
            // Capture old property values and settings before changing the shader.
            Texture mainTex = mat.HasProperty("_Albedo_Map")
                ? mat.GetTexture("_Albedo_Map")
                : null;

            Texture normalMap = mat.HasProperty("_Normal_Map")
                ? mat.GetTexture("_Normal_Map")
                : null;

            Color color = mat.HasProperty("_Color")
                ? mat.GetColor("_Color")
                : Color.white;

            // Preserve render queue setting.
            int originalRenderQueue = mat.renderQueue;

            // Capture alpha clipping settings.
            float cutoffValue = mat.HasProperty("_Cutoff")
                ? mat.GetFloat("_Cutoff")
                : 0.5f;

            bool hasAlphaClip = mat.HasProperty("_AlphaClip") &&
                                Mathf.Approximately(mat.GetFloat("_AlphaClip"), 1f);

            // Switch to the URP shader.
            mat.shader = urpLit;

            // Restore render queue.
            mat.renderQueue = originalRenderQueue;

            // Migrate texture properties.
            // URP/Lit uses _BaseMap instead of _Albedo_Map.
            if (mainTex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", mainTex);
            }

            // URP/Lit uses _NormalMap instead of _Normal_Map.
            if (normalMap != null && mat.HasProperty("_NormalMap"))
            {
                mat.SetTexture("_NormalMap", normalMap);
            }

            // Migrate color properties.
            // URP/Lit uses _BaseColor instead of _Color.
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            // Migrate alpha clipping settings if present.
            if (hasAlphaClip && mat.HasProperty("_AlphaClip"))
            {
                mat.SetFloat("_AlphaClip", 1f);
            }

            if (mat.HasProperty("_Cutoff"))
            {
                mat.SetFloat("_Cutoff", cutoffValue);
            }

            // Mark the material as modified and save it.
            EditorUtility.SetDirty(mat);

            Debug.Log(
                $"[SyntyCastleURP] converted: {Path.GetFileName(matPath)} " +
                $"(albedo={mainTex?.name ?? "none"}, normal={normalMap?.name ?? "none"})");
        }
    }
}
