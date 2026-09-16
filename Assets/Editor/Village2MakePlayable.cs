// =============================================================================
// Village2MakePlayable — make the owner's hand-authored Village2 redo PLAYABLE.
//
// The redo's content root is "StrongholdRoot" (not "Village2"), so Village2Playable's
// B4/C phases (which FindRoot "Village2") silently skip it — it got the ART but no
// playable wiring. DATA-PROVEN issues (F8 break-log + scene grep):
//   1. No hero  -> HeroControlEnsurer emergency-spawns a bare PILL ("carried hero not found").
//   2. No colliders on the structures (walk-through).
//   3. No baked navmesh -> the NavMeshAgent hero can't move / falls through.
// This one pass fixes all three on the SAVED scene: colliders where missing,
// NavigationStatic flags, a real hero at HeroStartPoint_PlayerSpawn, camera/HUD,
// then bakes the navmesh + saves. Reuses Village2Playable's public importers.
//
// Run: DeNelle.Editor.Village2MakePlayable.Run  (run-unity-method, EDITOR CLOSED — project lock + scene resave)
// =============================================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class Village2MakePlayable
    {
        private const string ScenePath = "Assets/Scenes/Village2.unity";

        [MenuItem("Defenders/Village2/MAKE PLAYABLE (colliders + hero + navmesh)")]
        public static void Run()
        {
            Log("=== Village2 MAKE PLAYABLE START ===");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Content root: prefer StrongholdRoot, else the first root with children.
            GameObject root = null;
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "StrongholdRoot") { root = r; break; }
            if (root == null)
                foreach (var r in scene.GetRootGameObjects())
                    if (r.transform.childCount > 0) { root = r; break; }
            if (root == null) { Err("No content root found in Village2. Aborting."); return; }
            Log($"Content root = '{root.name}' ({root.transform.childCount} children).");

            // --- 1) Colliders where missing + NavigationStatic flags ----------------
            int collidersAdded = 0, flagged = 0;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null) continue;
                var go = mr.gameObject;
                if (go.GetComponent<Collider>() == null)
                {
                    var mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) { go.AddComponent<MeshCollider>(); collidersAdded++; }
                    else { go.AddComponent<BoxCollider>(); collidersAdded++; }
                }
                var fl = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go, fl | StaticEditorFlags.NavigationStatic);
                flagged++;
            }
            Log($"Colliders added: {collidersAdded}; renderers flagged NavigationStatic: {flagged}.");

            // --- 2) Scene defaults (camera + light), EventSystem, HUD ----------------
            Village2Playable.AddSceneDefaultsToActiveScene();
            Village2Playable.ImportEventSystem();
            Village2Playable.ImportVillageHud(root.transform);

            // --- 3) A real hero at the owner's HeroStartPoint_PlayerSpawn ------------
            var hero = Village2Playable.ImportHero(root.transform, null);
            var marker = GameObject.Find("HeroStartPoint_PlayerSpawn");
            if (hero != null && marker != null)
            {
                hero.transform.position = marker.transform.position + Vector3.up * 0.9f;
                Village2Playable.WireCameraTargetToHero(hero);
                Log($"Hero seated at HeroStartPoint_PlayerSpawn {marker.transform.position}.");
            }
            else if (hero != null)
            {
                Village2Playable.WireCameraTargetToHero(hero);
                Warn("HeroStartPoint_PlayerSpawn not found — hero left at the importer's default spawn.");
            }

            // --- 4) Bake the navmesh (same pattern as Village2Playable.C) ------------
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            Vector3 probe = marker != null ? marker.transform.position : (hero != null ? hero.transform.position : Vector3.zero);
            bool walkable = UnityEngine.AI.NavMesh.SamplePosition(probe, out _, 5f, UnityEngine.AI.NavMesh.AllAreas);
            Log($"NavMesh baked; spawn-area walkable={walkable} (probe {probe}).");
            if (!walkable)
                Warn("Spawn area not walkable after bake — the floor renderer may not be NavigationStatic-walkable " +
                     "(check it's a flat ground mesh) or the spawn is over the hole. Will iterate.");

            // --- 5) Save ------------------------------------------------------------
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            // WO-1731: the bake earlier in this method wrote the scene's m_NavMeshData. Logging
            // `ok=False` and carrying on leaves the scene pointing at a navmesh it never
            // persisted -- which ships as no navmesh, silently. THROW (RaidNavBake.cs:109-111).
            if (!saved)
                throw new System.InvalidOperationException(
                    "[Village2MakePlayable] could not save navigation for " + ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Log($"Saved '{ScenePath}' (ok={saved}). Colliders+{collidersAdded}, hero={(hero != null ? "yes" : "NO")}, walkable={walkable}.");
            Log("=== Village2 MAKE PLAYABLE DONE — open Village2.unity and Play ===");
        }

        private static void Log(string m)  => Debug.Log("[Village2MakePlayable] " + m);
        private static void Warn(string m) => Debug.LogWarning("[Village2MakePlayable] " + m);
        private static void Err(string m)  => Debug.LogError("[Village2MakePlayable] " + m);
    }
}
