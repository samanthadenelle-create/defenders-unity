using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;   // NavMeshSurface / CollectObjects
using UnityEngine.SceneManagement;
using DeNelle.Core.World;    // GarrisonRecipe / GarrisonRecipeCatalog (DeNelle.Core is referenced by Editor)

namespace DeNelle.Editor
{
    // =============================================================================
    // GarrisonSceneBuilder — RECIPE-DRIVEN, CATALOG-FIRST garrison/dungeon builder.
    // -----------------------------------------------------------------------------
    // Run from: Defenders > Scenes > Build All Garrisons (From Recipes)
    // Batchmode: DeNelle.Editor.GarrisonSceneBuilder.BuildAll  (via run-unity-method)
    //
    // THE VISION (owner): "virtually no code, just JSON params." Adding a new fort /
    // cave / region = ONE line in Assets/Resources/Data/Canonical/garrison-recipes.json
    // — ZERO new code. This builder reads EVERY recipe and realizes a whole
    // Garrison_<id>.unity from it:
    //   GarrisonRoot
    //     Environment   (ground, palisade/floor shells, towers, huts)
    //     Props         (campfires, crates, bones, ice, stalagmites, ...)
    //     EnemySpawnPoints (6-10 child Transforms)
    //   + moody lighting tinted by the recipe's theme/lighting/element
    //   + a GarrisonController wired to the recipe's enemies[] + levelRange + threat
    //   + a baked NavMeshSurface, the scene saved + registered in Build Settings.
    //
    // CATALOG-FIRST: every prop is a ROLE ("wall_wood", "watchtower", "stalagmite",
    // "ice_floe", "dungeon_pillar"...) resolved to a REAL prefab on disk through the
    // prop-role map in the .Scenes partial — polyperfect Low Poly Ultimate Pack first
    // (Fantasy_M dungeon parts, Medieval_M forts, Nature_M ice/stones, Survival_M
    // campfire), the committed Resources/Structures set as the guaranteed fallback,
    // then a tinted primitive. Every miss is a single LogWarning (CLAUDE.md §4), never
    // an error — a not-yet-imported pack degrades the scene, it does not break the bake.
    //
    // NO per-theme hardcoded methods — themes are DATA. A single BuildFromRecipe() runs
    // every recipe; BuildAll() loops the catalog. CROSS-ASSEMBLY: DeNelle.Village types
    // (GarrisonController, SceneTransitionTrigger) are added via REFLECTION (the Editor
    // asmdef references DeNelle.Core + Unity.AI.Navigation but NOT DeNelle.Village).
    //
    // No .unity hand-edits. Batchmode-safe (no EditorUtility dialogs). Canon: Elarion.
    // =============================================================================
    public static partial class GarrisonSceneBuilder
    {
        private const string RootName = "GarrisonRoot";

        // ===================================================================
        //  ENTRY POINTS
        // ===================================================================

        [MenuItem("Defenders/Scenes/Build All Garrisons (From Recipes)")]
        public static void BuildAll()
        {
            Log("=== Build ALL garrison/dungeon scenes from recipes START ===");
            GarrisonRecipeCatalog.Reload();
            var recipes = GarrisonRecipeCatalog.All;
            if (recipes == null || recipes.Count == 0)
            {
                Warn("No recipes loaded from garrison-recipes.json — nothing to build. " +
                     "Verify Assets/Resources/Data/Canonical/garrison-recipes.json.");
                return;
            }

            int built = 0;
            foreach (var r in recipes)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                BuildFromRecipe(r);
                built++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log($"=== Build ALL DONE: {built} garrison/dungeon scene(s) built + baked + saved + in Build Settings. ===");
        }

        /// <summary>Build a single recipe by id (batchmode-friendly; pass the id via a wrapper).</summary>
        public static void BuildById(string id)
        {
            GarrisonRecipeCatalog.Reload();
            var r = GarrisonRecipeCatalog.Find(id);
            if (r == null) { Warn($"No recipe with id '{id}' in garrison-recipes.json."); return; }
            BuildFromRecipe(r);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ===================================================================
        //  SHARED: NavMesh bake (Physics Colliders + Collect All so ground/floor +
        //  wall/platform MeshColliders are the source). Persisted to a PER-SCENE
        //  asset path so the recipes don't overwrite one shared nav asset. Keeps the
        //  `using Unity.AI.Navigation;` fix — NavMeshSurface is referenced directly.
        // ===================================================================
        private static void BakeNavMesh(Transform root, string sceneName)
        {
            var surface = root.GetComponent<NavMeshSurface>();
            if (surface == null) surface = root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.BuildNavMesh();

            var data = surface.navMeshData;
            if (data == null)
            {
                Warn($"{sceneName}: navMeshData null after bake — verify ground/floor MeshColliders.");
                return;
            }

            string dir = $"{ScenesDir}/{sceneName}";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(ScenesDir, sceneName);
            string assetPath = $"{dir}/NavMesh-{sceneName}.asset";
            if (!AssetDatabase.Contains(data))
            {
                var existing = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (existing != null) AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(data, assetPath);
                Log($"{sceneName}: navmesh asset written -> {assetPath}");
            }
        }

        // ===================================================================
        //  SHARED: save the scene to a named path.
        // ===================================================================
        private static void SaveSceneTo(Scene scene, string scenePath)
        {
            if (!AssetDatabase.IsValidFolder(ScenesDir))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.MarkSceneDirty(scene);
            bool ok = EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            if (ok) Log("Saved scene -> " + scenePath);
            // WO-1731: BakeNavMesh wrote this scene's m_NavMeshData and deleted the asset it
            // replaced. A save that only LOGS its failure leaves the scene pointing at a navmesh
            // that is gone -- which ships silently as no navmesh at all. THROW instead
            // (RaidNavBake.cs:109-111).
            else throw new System.InvalidOperationException(
                "[GarrisonSceneBuilder] could not save navigation for " + scenePath +
                " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
        }

        // ===================================================================
        //  SHARED: register the scene in Build Settings (idempotent — a scene not in
        //  the build list fails SceneManager.LoadScene silently).
        // ===================================================================
        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
            {
                if (s.path == path)
                {
                    if (!s.enabled) { s.enabled = true; EditorBuildSettings.scenes = scenes.ToArray(); }
                    Log("Scene already in Build Settings (ensured enabled): " + path);
                    return;
                }
            }
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Log("Added scene to Build Settings: " + path);
        }

        // ===================================================================
        //  SHARED: cross-assembly reflection (DeNelle.Village not referenced here).
        // ===================================================================
        private static void SetField(System.Type type, Object comp, string fieldName, object value)
        {
            var f = type.GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(comp, value);
            else Warn("Field '" + fieldName + "' not found on " + type.Name + " — skipped.");
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void Log(string m)  => Debug.Log("[GarrisonSceneBuilder] " + m);
        private static void Warn(string m) => Debug.LogWarning("[GarrisonSceneBuilder] " + m);
        private static void Err(string m)  => Debug.LogError("[GarrisonSceneBuilder] " + m);
    }
}
