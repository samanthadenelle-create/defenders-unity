// Village2FinalizeApproach — finalize the owner's hand-placed approach:
//  1. Remove the blocking collider from ChokePointGate (1) (owner: it blocked traversal).
//  2. Ensure the owner's Cube / Cube (1) (the two gate-block segments) are solid + navmesh
//     obstacles, and the Plane (connecting ground) is NavigationStatic-walkable.
//  3. Rebake the navmesh; verify spawn->stronghold is now a COMPLETE path.
//  4. DUMP the owner's Cube/Cube(1)/Plane transforms so they can be baked into the builder
//     (reproducible) + genericized into the gate-funnel for all gates.
// Run: DeNelle.Editor.Village2FinalizeApproach.Run  (EDITOR CLOSED — scene resave + rebake)
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class Village2FinalizeApproach
    {
        private const string ScenePath = "Assets/Scenes/Village2.unity";

        [MenuItem("Defenders/Village2/Finalize Approach (strip gate collider + rebake)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log("=== Village2 finalize approach ===");

            // 1) Strip the blocking collider from ChokePointGate (1) (and any ChokePointGate variant the owner flagged).
            int stripped = 0;
            foreach (var t in Object.FindObjectsByType<Transform>())
            {
                if (t == null) continue;
                string n = t.name;
                if (!n.Contains("ChokePointGate")) continue;
                bool isOne = n.Contains("(1)") || n.Contains("1");
                Log($"  found gate '{n}' (target={isOne})");
                if (!isOne) continue;
                foreach (var col in t.GetComponents<Collider>())
                {
                    if (col == null || col.isTrigger) continue;   // keep trigger (interaction) colliders
                    col.enabled = false; stripped++;
                    Log($"    disabled blocking collider {col.GetType().Name} on '{n}'.");
                }
            }
            if (stripped == 0) Log("  no non-trigger collider found on ChokePointGate (1) (already stripped or name differs).");

            // 2) Owner's hand-placed pieces: dump transforms + ensure roles.
            DumpAndRole("Cube", obstacle: true);
            DumpAndRole("Cube (1)", obstacle: true);
            DumpAndRole("Plane", obstacle: false);   // walkable ground

            // 3) Rebake + verify.
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            var spawnGo = GameObject.Find("HeroStartPoint_PlayerSpawn");
            var keep = GameObject.Find("Spawn_Keep") ?? GameObject.Find("StrongholdRoot");
            if (spawnGo != null && keep != null
                && NavMesh.SamplePosition(spawnGo.transform.position, out NavMeshHit s, 6f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(keep.transform.position, out NavMeshHit k, 8f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                NavMesh.CalculatePath(s.position, k.position, NavMesh.AllAreas, path);
                Log($"VERIFY spawn -> '{keep.name}': status={path.status} corners={path.corners.Length} (want PathComplete).");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            // WO-1731: the bake earlier in this method wrote the scene's m_NavMeshData. Logging
            // `ok=False` and carrying on leaves the scene pointing at a navmesh it never
            // persisted -- which ships as no navmesh, silently. THROW (RaidNavBake.cs:109-111).
            if (!saved)
                throw new System.InvalidOperationException(
                    "[Village2FinalizeApproach] could not save navigation for " + ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Log($"Saved (ok={saved}). Gate collider stripped + cubes/plane finalized + navmesh rebaked.");
            Log("=== done ===");
        }

        private static void DumpAndRole(string name, bool obstacle)
        {
            var go = GameObject.Find(name);
            if (go == null) { Log($"  '{name}' not found (owner may have renamed it)."); return; }
            var t = go.transform;
            Log($"  RECIPE '{name}': pos={t.position} euler={t.eulerAngles} scale={t.localScale}");
            // ensure a collider exists
            if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
            // navmesh role: obstacle (block) or walkable ground — both flag NavigationStatic; the bake
            // makes the flat plane walkable and the tall cubes obstacles by slope.
            var fl = GameObjectUtility.GetStaticEditorFlags(go);
            GameObjectUtility.SetStaticEditorFlags(go, fl | StaticEditorFlags.NavigationStatic);
        }

        private static void Log(string m) => Debug.Log("[V2Finalize] " + m);
    }
}
