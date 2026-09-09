// =============================================================================
// RaidNavBake — gives every generated RaidBase_*.unity a WALKABLE GROUND FLOOR +
// a baked legacy NavMesh, so deployed troops + the garrison (NavMeshAgents) can
// path. ROOT CAUSE of "Failed to create agent / no valid NavMesh" + the 75s raid
// softlock (owner 2026-06-14): the raid scenes have NO ground floor of their own
// (RaidBaseGenerator places only walls/garrison, ground-seated at y=0, assuming a
// ground that exists only via the town-flow additive terrain) — so a dev-map/direct
// load has nothing to walk on and the navmesh bakes empty (0 verts).
//
// Fix: drop a large flat 'RaidGround' plane at y=0 (with its MeshCollider for nav +
// physics), mark all renderers NavigationStatic, and bake the legacy scene NavMesh
// (UnityEditor.AI.NavMeshBuilder) — the same path CastleWalkable uses. The walls
// carve out as obstacles; the gate openings stay walkable, so the single ground
// plane gives one connected nav surface from the deploy edge to the boss.
//
// Idempotent: re-running reuses the existing RaidGround + re-bakes.
//
// Batchmode: DeNelle.Editor.RaidNavBake.BakeAll
// Menu:      Defenders/Castle/Bake Raid NavMeshes
// =============================================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using DeNelle.Core;

namespace DeNelle.Editor
{
    public static class RaidNavBake
    {
        private static readonly string[] RaidScenes =
        {
            "Assets/Scenes/RaidBase_raider_camp_small.unity",
            "Assets/Scenes/RaidBase_fortified_garrison.unity",
            "Assets/Scenes/RaidBase_mage_enclave.unity",
            "Assets/Scenes/RaidBase_IronBastion.unity",
        };

        private const string GroundName = "RaidGround";
        private const float  GroundScale = 14f;   // Unity Plane = 10m @ scale 1 -> 140m square (covers base + deploy ring)

        [MenuItem("Defenders/Castle/Bake Raid NavMeshes")]
        public static void BakeAll()
        {
            int ok = 0;
            foreach (var scenePath in RaidScenes)
            {
                if (!System.IO.File.Exists(scenePath)) { Debug.LogWarning($"[RaidNavBake] missing {scenePath} — skipped."); continue; }
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                string name = System.IO.Path.GetFileName(scenePath);

                EnsureGround(scene);

                // Mark all renderers + terrains NavigationStatic so the legacy bake includes them
                // (ground bakes walkable; vertical walls/towers carve out as obstacles).
                int marked = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                        GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags | StaticEditorFlags.NavigationStatic);
                        marked++;
                    }
                }

                UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
                UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                var tri = NavMesh.CalculateTriangulation();
                bool walkable = tri.vertices != null && tri.vertices.Length > 0;
                Debug.Log($"[RaidNavBake] {name}: ground + {marked} NavigationStatic -> " +
                          $"{(tri.vertices != null ? tri.vertices.Length : 0)} verts / " +
                          $"{(tri.indices != null ? tri.indices.Length / 3 : 0)} tris " +
                          (walkable ? "OK (troops can path)" : "EMPTY (still no walkable floor!)"));
                if (walkable) ok++;
            }
            Debug.Log($"[RaidNavBake] DONE — {ok}/{RaidScenes.Length} raid scenes now have a walkable navmesh.");
        }

        // Add a flat ground plane at y=0 if the scene has none (idempotent by name).
        // ALWAYS retint: a plane created on an older bake kept Unity's tan Default-Material
        // because this method used to return early, which is the brown pad in the owner's
        // 2026-09-09 raid frame.
        private static void EnsureGround(Scene scene)
        {
            var ground = GameObject.Find(GroundName);
            bool created = false;
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = GroundName;
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = new Vector3(GroundScale, 1f, GroundScale);
                created = true;
            }

            var c = GroundColorFor(scene.name);
            var m = MagentaGuard.BuildUrpLitMaterial(c);
            var r = ground.GetComponent<Renderer>();
            if (r != null && m != null) r.sharedMaterial = m;
            MagentaGuard.ProtectPrimitiveArt(ground, "RaidNavBake.RaidGround");
            Debug.Log($"[RaidNavBake] {(created ? "added" : "retinted")} {GroundName} " +
                      $"({GroundScale * 10f}m square) color={c} scene={scene.name}.");
        }

        private static Color GroundColorFor(string sceneName)
        {
            if (!string.IsNullOrEmpty(sceneName) &&
                sceneName.IndexOf("mage_enclave", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.11f, 0.10f, 0.13f, 1f);
            if (!string.IsNullOrEmpty(sceneName) &&
                sceneName.IndexOf("fortified_garrison", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.20f, 0.19f, 0.18f, 1f);
            return new Color(0.18f, 0.14f, 0.09f, 1f);
        }
    }
}
