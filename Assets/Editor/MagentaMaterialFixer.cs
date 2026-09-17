// =============================================================================
// MagentaMaterialFixer — repairs the magenta renderers found by
// MagentaMaterialScanner. WO-409 Bug 1 fix. Idempotent.
// -----------------------------------------------------------------------------
// Strategy (per offending material):
//   * Material exists but uses a built-in / error shader (Standard, Legacy,
//     Specular setup, Hidden/InternalErrorShader): swap to
//     "Universal Render Pipeline/Lit", carrying base colour + main texture +
//     emission via the standard URP-upgrade mapping (_Color->_BaseColor,
//     _MainTex->_BaseMap). This mirrors PolyperfectUrpFix but works on ANY
//     material asset, wherever it lives.
//   * sharedMaterial == null (empty slot): assign a sensible URP/Lit default
//     (a shared neutral material we create once under Assets/Materials).
//   * F8-49: renderer slot references Unity's BUILT-IN 'Default-Particle' material
//     (Resources/unity_builtin_extra, shader 'Legacy Shaders/Particles/Alpha
//     Blended Premultiply' — magenta under URP). Built-in materials are read-only
//     and live outside Assets/, so the two passes above both miss them. A dedicated
//     prefab pass swaps every such slot to a shared URP Particles/Unlit replica
//     (premultiply blend + the built-in Default-Particle soft-glow texture).
//     Hovl Studio / Mirza Beig packs are gitignored, so this pass — not a YAML
//     edit — is the durable source fix; re-run it after any pack re-import.
//   * F8-49: renderer slot references Unity's BUILT-IN 'Default-Particle' material
//     (Resources/unity_builtin_extra, shader 'Legacy Shaders/Particles/Alpha
//     Blended Premultiply' — magenta under URP). Built-in materials are read-only
//     and live outside Assets/, so the two passes above both miss them. A dedicated
//     prefab pass swaps every such slot to a shared URP Particles/Unlit replica
//     (premultiply blend + the built-in Default-Particle soft-glow texture).
//     Hovl Studio / Mirza Beig packs are gitignored, so this pass — not a YAML
//     edit — is the durable source fix; re-run it after any pack re-import.
//
// If every offender is under Assets/polyperfect, this first delegates to the
// existing Defenders/Art/Fix Polyperfect URP Materials pass (PolyperfectUrpFix)
// so we reuse the project's proven fix, then sweeps anything left over.
//
// Material-asset swaps are in-place (same GUID) so baked prefabs/scenes pick up
// the fix with no re-bake. Null-slot fixes that touch prefab/scene instances are
// written back to the prefab asset and the open scene respectively.
//
// Run (batchmode):  -executeMethod DeNelle.Editor.MagentaMaterialFixer.Run
// Menu:             Defenders/Art/Fix Magenta Materials
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class MagentaMaterialFixer
    {
        private const string DefaultMatPath = "Assets/Materials/MagentaFix_DefaultLit.mat";

        [MenuItem("Defenders/Art/Fix Magenta Materials")]
        public static void Run()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[MagentaMaterialFixer] 'Universal Render Pipeline/Lit' not found — is URP installed? Aborting.");
                return;
            }

            // 1) Reuse the project's proven polyperfect fix first (idempotent; no-op if pack absent).
            try { PolyperfectUrpFix.Fix(); }
            catch (System.Exception e) { Debug.LogWarning($"[MagentaMaterialFixer] PolyperfectUrpFix pass skipped: {e.Message}"); }

            // 2) Sweep ALL material assets in the project for built-in/error shaders.
            int matSwaps = SweepAllMaterials(lit);

            // 2b) F8-49: swap renderer slots that reference Unity's read-only BUILT-IN
            //     legacy particle materials (Default-Particle etc.) — invisible to both
            //     the Assets/ material sweep and the null-slot pass below.
            int builtinSwaps = FixBuiltinLegacyParticleSlotsInPrefabs();

            // 3) Repair null sharedMaterial slots on prefabs + build scenes with a URP default.
            Material def = GetOrCreateDefaultMaterial(lit);
            int prefabNullFixes = FixNullSlotsInPrefabs(def);
            int sceneNullFixes = FixNullSlotsInScenes(def);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MagentaMaterialFixer] DONE — converted {matSwaps} built-in/error material asset(s) → URP/Lit; " +
                      $"swapped {builtinSwaps} built-in legacy particle slot(s) → URP Particles/Unlit; " +
                      $"assigned default to {prefabNullFixes} null prefab slot(s) + {sceneNullFixes} null scene slot(s).");
        }

        // ---- material-asset shader swap (covers built-in + InternalError) ----
        private static int SweepAllMaterials(Shader lit)
        {
            string[] guids = AssetDatabase.FindAssets("t:Material");
            int converted = 0;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                // never touch package/read-only materials we can't author
                if (!path.StartsWith("Assets/")) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                if (UpgradeMaterialToUrp(mat, lit))
                    converted++;
            }
            return converted;
        }

        /// <summary>Swap a built-in / error-shader material to URP/Lit in place. Idempotent. Returns true if changed.</summary>
        public static bool UpgradeMaterialToUrp(Material mat, Shader lit)
        {
            if (mat == null || lit == null) return false;
            var sh = mat.shader;
            string sn = sh != null ? sh.name : "";

            bool needsUpgrade =
                sh == null ||
                sn == "Standard" ||
                sn == "Standard (Specular setup)" ||
                sn.StartsWith("Legacy Shaders/") ||
                sn.Contains("InternalError") ||
                sn.Contains("Hidden/InternalError");

            if (!needsUpgrade) return false;

            // Capture legacy props before the swap drops them.
            Color baseCol = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Color emis = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;

            mat.shader = lit;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mainTex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (emis.maxColorComponent > 0.01f)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emis);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(mat);
            return true;
        }

        // ---- F8-49: built-in legacy particle material slot swap ---------------
        // Unity's built-in 'Default-Particle' material (Resources/unity_builtin_extra)
        // uses 'Legacy Shaders/Particles/Alpha Blended Premultiply' — a dead shader
        // under URP. It is read-only and NOT under Assets/, so SweepAllMaterials cannot
        // upgrade it in place; the slot is also non-null, so the null-slot pass skips
        // it. Root example (F8-49): Hovl 'Flower slash.prefab' → 'Light' child renderer
        // slot 0 = {fileID: 10301, guid: 0000000000000000f000000000000000}.
        // Fix: point every such slot at a shared URP Particles/Unlit replica asset
        // (premultiply blend + the built-in Default-Particle soft-glow texture).

        private const string ParticleFixMatPath = "Assets/Materials/MagentaFix_DefaultParticle_URP.mat";

        /// <summary>Standalone batchmode entry for the F8-49 pass only (Run() also includes it).</summary>
        [MenuItem("Defenders/Art/Fix Built-in Particle Materials (F8-49)")]
        public static void FixBuiltinParticles()
        {
            int n = FixBuiltinLegacyParticleSlotsInPrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MagentaMaterialFixer] FixBuiltinParticles DONE — swapped {n} built-in legacy particle slot(s) → URP Particles/Unlit.");
        }

        private static int FixBuiltinLegacyParticleSlotsInPrefabs()
        {
            Material replacement = GetOrCreateUrpDefaultParticleMaterial();
            if (replacement == null) return 0;

            int slotFixes = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.StartsWith("Assets/")) continue;

                // Cheap pre-filter: only open prefabs whose YAML actually references a
                // built-in material (guid 0000000000000000f000000000000000, type 0).
                try
                {
                    string yaml = System.IO.File.ReadAllText(path);
                    if (!yaml.Contains("guid: 0000000000000000f000000000000000")) continue;
                }
                catch { continue; }

                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;
                bool dirty = false;

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    if (mats == null) continue;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!IsBuiltinLegacyParticleMaterial(mats[i])) continue;
                        mats[i] = replacement;
                        changed = true;
                        slotFixes++;
                    }
                    if (changed)
                    {
                        r.sharedMaterials = mats;
                        EditorUtility.SetDirty(r);
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[MagentaMaterialFixer] built-in legacy particle slot(s) fixed in {path}");
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            return slotFixes;
        }

        /// <summary>True when the material is one of Unity's read-only BUILT-IN materials still on a legacy/dead shader.</summary>
        private static bool IsBuiltinLegacyParticleMaterial(Material m)
        {
            if (m == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(m);
            if (assetPath != "Resources/unity_builtin_extra") return false;   // only built-ins; Assets/ mats are handled by SweepAllMaterials
            var sh = m.shader;
            string sn = sh != null ? sh.name : "";
            return sh == null ||
                   sn.StartsWith("Legacy Shaders/") ||
                   sn.Contains("InternalError");
        }

        /// <summary>
        /// Shared URP replica of Unity's Default-Particle material: URP Particles/Unlit,
        /// transparent premultiply blend (mirrors 'Particles/Alpha Blended Premultiply'),
        /// base map = the built-in Default-Particle soft-glow texture. Created once.
        /// Blend setup mirrors VFXManager.ConfigureUrpParticleBlend (the proven runtime heal).
        /// </summary>
        private static Material GetOrCreateUrpDefaultParticleMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ParticleFixMatPath);
            if (existing != null) return existing;

            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null)
            {
                Debug.LogError("[MagentaMaterialFixer] 'Universal Render Pipeline/Particles/Unlit' not found — cannot build the Default-Particle replacement.");
                return null;
            }

            const string dir = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var mat = new Material(sh) { name = "MagentaFix_DefaultParticle_URP" };
            // _Surface 1 = Transparent; _Blend 1 = Premultiply (URP BaseShaderGUI enum).
            if (mat.HasProperty("_Surface"))  mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))    mat.SetFloat("_Blend", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))   mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // Same soft-glow texture the built-in Default-Particle material used.
            var tex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            AssetDatabase.CreateAsset(mat, ParticleFixMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ---- null-slot repair: prefabs --------------------------------------
        private static int FixNullSlotsInPrefabs(Material def)
        {
            if (def == null) return 0;
            int fixes = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.StartsWith("Assets/")) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;
                bool dirty = false;

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (AssignDefaultToNullSlots(r, def)) dirty = true;
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    fixes++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            return fixes;
        }

        // ---- null-slot repair: build scenes ---------------------------------
        private static int FixNullSlotsInScenes(Material def)
        {
            if (def == null) return 0;
            int fixes = 0;
            string activeBefore = SceneManager.GetActiveScene().path;

            foreach (var entry in EditorBuildSettings.scenes)
            {
                if (entry == null || !entry.enabled) continue;
                string scenePath = entry.path;
                if (string.IsNullOrEmpty(scenePath) || !System.IO.File.Exists(scenePath)) continue;

                Scene scene;
                try { scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[MagentaMaterialFixer] could not open scene {scenePath}: {e.Message}");
                    continue;
                }

                bool dirty = false;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        if (AssignDefaultToNullSlots(r, def)) dirty = true;

                if (dirty)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    fixes++;
                }
            }

            if (!string.IsNullOrEmpty(activeBefore) && System.IO.File.Exists(activeBefore))
            {
                try { EditorSceneManager.OpenScene(activeBefore, OpenSceneMode.Single); }
                catch { /* best-effort restore */ }
            }
            return fixes;
        }

        /// <summary>Fill any null entry in the renderer's shared-material array with the default. Returns true if changed.</summary>
        /// <remarks>
        /// WO-1806 — THE PRODUCER OF THE "FLAT UNTEXTURED PLANE" CLASS.
        /// This method used to stamp ONE default into every null slot of every renderer:
        /// <c>MagentaFix_DefaultLit</c> (URP/Lit, RenderType Opaque, _BaseColor 0.70 grey,
        /// _BaseMap NULL). On a MeshRenderer that is a fine placeholder. On a
        /// ParticleSystemRenderer it is a flat, untextured, OPAQUE grey billboard standing
        /// in the middle of the player's screen — which is exactly what the owner F8'd from
        /// RaidBase_raider_camp_small on 2026-09-16. The device log states the material's
        /// shape in its own words: <c>[Flow:ArcaneDiag] ... mat='MagentaFix_DefaultLit'
        /// shader='Universal Render Pipeline/Lit' baseColor=(0.70,0.70,0.70,1.00)
        /// baseMap=False mainTex=False</c>.
        /// A census of the tree on 2026-09-16 found 4,197 such slots on ParticleSystemRenderers
        /// across 969 prefabs — this pass is where they came from.
        /// The correct particle default ALREADY EXISTED in this same file
        /// (<see cref="GetOrCreateUrpDefaultParticleMaterial"/>, used only by the sibling
        /// built-in-particle pass). It is simply never consulted here. It is now.
        /// </remarks>
        private static bool AssignDefaultToNullSlots(Renderer r, Material def)
        {
            if (r == null) return false;

            // A particle renderer gets the TRANSPARENT particle default, never the opaque
            // Lit one. Resolved lazily and only when a particle renderer is actually seen,
            // so the mesh path costs nothing. If the particle default cannot be built we
            // fall back to the old behaviour rather than leaving a null slot (a null slot
            // renders engine-default MAGENTA, which is strictly worse than a grey slab).
            if (r is ParticleSystemRenderer)
            {
                var particleDef = GetOrCreateUrpDefaultParticleMaterial();
                if (particleDef != null) def = particleDef;
                else Debug.LogWarning("[MagentaMaterialFixer] particle default unavailable — " +
                                      "falling back to the opaque Lit default on '" + r.gameObject.name +
                                      "'. That slot will render as a flat untextured quad (WO-1806).");
            }

            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                r.sharedMaterials = new[] { def };
                EditorUtility.SetDirty(r);
                return true;
            }

            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                {
                    mats[i] = def;
                    changed = true;
                }
            }
            if (changed)
            {
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);
            }
            return changed;
        }

        // ---- shared default material ----------------------------------------
        private static Material GetOrCreateDefaultMaterial(Shader lit)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(DefaultMatPath);
            if (existing != null) return existing;

            const string dir = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var mat = new Material(lit) { name = "MagentaFix_DefaultLit" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, DefaultMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
