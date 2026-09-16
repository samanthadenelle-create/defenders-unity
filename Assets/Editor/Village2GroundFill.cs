// Village2GroundFill — lay a connecting ground plane so the hero can walk from the
// spawn to the stronghold (RCA proved spawn->stronghold was PathPartial = no walkable
// floor bridging the gap = the "huge wide hole"). Adds one large flat ground, flags it
// NavigationStatic, rebakes the navmesh, verifies the path is now complete, saves.
// A FUNCTIONAL fill the owner then hand-edits/reshapes to taste. Run:
// DeNelle.Editor.Village2GroundFill.Run
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class Village2GroundFill
    {
        private const string ScenePath = "Assets/Scenes/Village2.unity";

        [MenuItem("Defenders/Village2/Fill Connecting Ground + Rebake")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log("=== Village2 connecting-ground fill ===");

            GameObject root = null;
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "StrongholdRoot") { root = r; break; }

            // Idempotent: remove a prior fill so re-runs don't stack.
            var existing = GameObject.Find("ConnectingGround");
            if (existing != null) Object.DestroyImmediate(existing);

            // One generous flat ground covering the spawn (27,-45) + the stronghold (0,0),
            // a hair below the stronghold floor so it never z-fights authored geometry.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ConnectingGround";
            if (root != null) go.transform.SetParent(root.transform, true);
            go.transform.position = new Vector3(5f, -0.10f, -20f);
            go.transform.localScale = new Vector3(110f, 0.2f, 110f);
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(lit) { name = "Village2Ground" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.26f, 0.24f, 0.21f, 1f)); // earthy stone
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var fl = GameObjectUtility.GetStaticEditorFlags(go);
            GameObjectUtility.SetStaticEditorFlags(go, fl | StaticEditorFlags.NavigationStatic);
            Log($"Added ConnectingGround at {go.transform.position} scale {go.transform.localScale}.");

            // Two gate-block segments flanking the arch opening (owner: "i will need two block segments").
            // Gate line ~z=-14; torches at x=+/-2.5 mark the ~5m opening. Blocks sit OUTSIDE that gap so only
            // the central arch is passable (the funnel). Named so the owner can hand-position them precisely.
            AddGateBlock(root, "GateBlock_L", new Vector3(-5.2f, 2f, -14f), new Vector3(5.4f, 4f, 1.2f), lit);
            AddGateBlock(root, "GateBlock_R", new Vector3( 5.2f, 2f, -14f), new Vector3(5.4f, 4f, 1.2f), lit);
            Log("Added GateBlock_L + GateBlock_R flanking the gate arch (hand-position to taste; re-run to rebake).");

            // Rebake the navmesh over the new ground.
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            // Verify: spawn -> gate should now be a COMPLETE path.
            var spawnGo = GameObject.Find("HeroStartPoint_PlayerSpawn");
            var gate = GameObject.Find("Spawn_Gate") ?? GameObject.Find("Spawn_Keep");
            if (spawnGo != null && gate != null
                && NavMesh.SamplePosition(spawnGo.transform.position, out NavMeshHit s, 6f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(gate.transform.position, out NavMeshHit g, 8f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                NavMesh.CalculatePath(s.position, g.position, NavMesh.AllAreas, path);
                Log($"VERIFY spawn -> '{gate.name}': status={path.status} corners={path.corners.Length} (want PathComplete).");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            // WO-1731: the bake earlier in this method wrote the scene's m_NavMeshData. Logging
            // `ok=False` and carrying on leaves the scene pointing at a navmesh it never
            // persisted -- which ships as no navmesh, silently. THROW (RaidNavBake.cs:109-111).
            if (!saved)
                throw new System.InvalidOperationException(
                    "[Village2GroundFill] could not save navigation for " + ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Log($"Saved (ok={saved}). Hand-edit 'ConnectingGround' to reshape the approach; re-run to rebake.");
            Log("=== done ===");
        }

        // One gate-block segment: solid + navmesh obstacle, dark stone, named for hand-editing.
        private static void AddGateBlock(GameObject root, string name, Vector3 pos, Vector3 size, Shader lit)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (root != null) go.transform.SetParent(root.transform, true);
            go.transform.position = pos;
            go.transform.localScale = size;
            var mat = new Material(lit) { name = name + "_Mat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.14f, 0.13f, 0.12f, 1f));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var fl = GameObjectUtility.GetStaticEditorFlags(go);
            GameObjectUtility.SetStaticEditorFlags(go, fl | StaticEditorFlags.NavigationStatic);
        }

        private static void Log(string m) => Debug.Log("[V2GroundFill] " + m);
    }
}
