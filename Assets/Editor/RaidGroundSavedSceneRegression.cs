using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using DeNelle.Village;

namespace DeNelle.Editor
{
    // WO-1703: reload the serialized output, never call the producer or save assets.
    // Run AFTER RaidNavBake.BakeAll in a separate root-controlled Unity process.
    public static class RaidGroundSavedSceneRegression
    {
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/RaidBase_raider_camp_small.unity",
            "Assets/Scenes/RaidBase_fortified_garrison.unity",
            "Assets/Scenes/RaidBase_mage_enclave.unity",
            "Assets/Scenes/RaidBase_IronBastion.unity",
        };

        public static void RunStandalone()
        {
            var failures = new List<string>();
            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
            bool emptyStartup = false;
            bool opened = false;
            int passed = 0;
            try
            {
                // Never let OpenScene(Single) discard an owner's unsaved work.
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.isDirty) throw new InvalidOperationException("dirty scene open; saved-scene proof refused safely");
                    if (!string.IsNullOrEmpty(scene.path)) continue;
                    if (!Application.isBatchMode || SceneManager.sceneCount != 1 || scene.GetRootGameObjects().Length != 0)
                        throw new InvalidOperationException("populated/interactive untitled scene open; saved-scene proof refused safely");
                    emptyStartup = true;
                }
                foreach (string path in Scenes)
                {
                    try
                    {
                        if (!System.IO.File.Exists(path)) throw new InvalidOperationException("saved scene missing");
                        opened = true;
                        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        Physics.SyncTransforms();
                        VerifyScene(scene);
                        passed++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(path + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex) { failures.Add(ex.Message); }
            finally
            {
                if (opened)
                {
                    try
                    {
                        if (emptyStartup) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        else EditorSceneManager.RestoreSceneManagerSetup(previous);
                    }
                    catch (Exception ex) { failures.Add("scene setup restore failed: " + ex.Message); }
                }
            }
            if (failures.Count == 0 && passed == Scenes.Length)
                Debug.Log($"RAID_GROUND_SAVED_OK {passed}/{Scenes.Length} scenes - persisted textured ground, wall/collision coverage, loaded navigation; IronBastion local-nav limitation logged");
            else Debug.LogError($"RAID_GROUND_SAVED_FAIL {passed}/{Scenes.Length} scenes: " + string.Join("; ", failures));
        }

        private static void VerifyScene(Scene scene)
        {
            Transform[] objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform[] grounds = objects.Where(t => t.name == "RaidGround").ToArray();
            Require(grounds.Length == 1, $"expected one RaidGround, found {grounds.Length}");
            var ground = grounds[0];
            var renderer = ground.GetComponent<Renderer>();
            var collider = ground.GetComponent<MeshCollider>();
            var mesh = ground.GetComponent<MeshFilter>();
            Require(renderer != null && renderer.enabled && ground.gameObject.activeInHierarchy, "ground renderer missing/inactive");
            Require(collider != null && collider.enabled && !collider.isTrigger && !collider.convex && mesh != null &&
                    collider.sharedMesh == mesh.sharedMesh, "ground collider does not share the render mesh");
            Bounds floor = renderer.bounds;
            Require((collider.bounds.center - floor.center).sqrMagnitude < 0.0001f &&
                    (collider.bounds.size - floor.size).sqrMagnitude < 0.0001f, "render/collision world bounds differ");

            bool template = scene.name == "RaidBase_IronBastion";
            var boundary = objects.FirstOrDefault(t => t.name == "ArenaBoundary_Ring");
            Renderer[] wallRenderers;
            Collider[] wallColliders;
            if (boundary != null)
            {
                wallRenderers = boundary.GetComponentsInChildren<Renderer>(true);
                wallColliders = boundary.GetComponentsInChildren<Collider>(true);
            }
            else
            {
                Require(template, "configured raid has no enclosing ArenaBoundary_Ring");
                // The historical template predates the outer landscape ring. Measure its
                // actual authored wall objects, not a made-up perimeter constant.
                var walls = objects.Select(t => t.GetComponent<WallSegment>()).Where(w => w != null).ToArray();
                wallRenderers = walls.SelectMany(w => w.GetComponentsInChildren<Renderer>(true)).ToArray();
                wallColliders = walls.SelectMany(w => w.GetComponentsInChildren<Collider>(true)).ToArray();
            }
            Require(wallRenderers.Length + wallColliders.Length > 0, "no wall geometry to measure");
            foreach (var wall in wallRenderers) Require(ContainsXZ(floor, wall.bounds), $"floor ends before wall renderer {wall.name}: floor={floor} wall={wall.bounds}");
            foreach (var wall in wallColliders)
                if (wall.enabled && wall.gameObject.activeInHierarchy) Require(ContainsXZ(floor, wall.bounds), $"floor ends before wall collider {wall.name}: floor={floor} wall={wall.bounds}");

            var config = SceneConfigCatalog.FindBySceneName(scene.name);
            if (config == null) config = SceneConfigCatalog.Find(scene.name.Substring("RaidBase_".Length));
            string token = config?.raidDress?.floor;
            Require(template || token == "floor_dirt_large" || token == "floor_tile_large", "missing/unsupported authored floor intent");
            string layerName = token == "floor_tile_large" ? "Stoneback_Rock" : "Path_Dirt";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"Assets/Generated/Terrain/{layerName}.terrainlayer");
            Require(layer != null && layer.diffuseTexture != null && layer.tileSize.x > 0f && layer.tileSize.y > 0f, "owned ground terrain layer unavailable");
            Material material = renderer.sharedMaterial;
            Require(material != null && material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") == layer.diffuseTexture,
                    "saved ground does not bind its authored terrain texture");
            Require(layer.diffuseTexture.wrapModeU == TextureWrapMode.Repeat && layer.diffuseTexture.wrapModeV == TextureWrapMode.Repeat,
                    "ground texture does not repeat");
            Vector2 tiling = material.GetTextureScale("_BaseMap");
            Require(Mathf.Abs(tiling.x * layer.tileSize.x - floor.size.x) < 0.02f &&
                    Mathf.Abs(tiling.y * layer.tileSize.y - floor.size.z) < 0.02f, "saved texture repeat period differs from terrain metres");
            string texturePath = AssetDatabase.GetAssetPath(layer.diffuseTexture);
            string materialPath = AssetDatabase.GetAssetPath(material);
            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(scene.path, true));
            Require(!string.IsNullOrEmpty(materialPath) && dependencies.Contains(materialPath) && dependencies.Contains(texturePath),
                    "material/texture does not survive as a serialized scene dependency");

            NavMeshTriangulation navigation = NavMesh.CalculateTriangulation();
            Require(navigation.vertices != null && navigation.indices != null && navigation.indices.Length >= 3, "saved scene loads no navigation triangles");
            Require(NavMesh.GetSettingsCount() > 0, "no configured navigation agent");
            var agent = NavMesh.GetSettingsByIndex(0);
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
            float sampleRadius = Mathf.Max(agent.agentHeight, agent.agentRadius * 2f);
            Vector3 start, end;
            var hero = objects.FirstOrDefault(t => t.name == "HeroStartPoint_PlayerSpawn");
            var staging = objects.FirstOrDefault(t => t.name == "RaidStagingPoint");
            if (hero != null && staging != null)
            {
                start = hero.position;
                end = staging.position;
            }
            else
            {
                Require(template, "configured raid lacks hero/staging approach markers");
                // This template has no gameplay approach contract. Use two distinct
                // interior positions on a measured loaded triangle to prove LOCAL nav
                // connectivity only, never pretend these are hero/deploy markers.
                int best = 0;
                float area = -1f;
                for (int i = 0; i + 2 < navigation.indices.Length; i += 3)
                {
                    Vector3 a = navigation.vertices[navigation.indices[i]];
                    Vector3 b = navigation.vertices[navigation.indices[i + 1]];
                    Vector3 c = navigation.vertices[navigation.indices[i + 2]];
                    float candidate = Vector3.Cross(b - a, c - a).sqrMagnitude;
                    if (candidate > area) { area = candidate; best = i; }
                }
                Require(area > 0.001f, "loaded navigation has only degenerate triangles");
                Vector3 v0 = navigation.vertices[navigation.indices[best]];
                Vector3 v1 = navigation.vertices[navigation.indices[best + 1]];
                Vector3 v2 = navigation.vertices[navigation.indices[best + 2]];
                start = (v0 + v1 + v2) / 3f;
                end = start * 0.5f + v0 * 0.5f;
                Debug.Log($"[RaidGroundSaved] {scene.name} LIMITATION: no approach markers; measured triangle {best / 3} local connectivity only");
            }
            Require(NavMesh.SamplePosition(start, out NavMeshHit from, sampleRadius, filter), "hero/local start is off the loaded navmesh");
            Require(NavMesh.SamplePosition(end, out NavMeshHit to, sampleRadius, filter), "staging/local end is off the loaded navmesh");
            Require(ContainsXZ(floor, new Bounds(from.position, Vector3.zero)) && ContainsXZ(floor, new Bounds(to.position, Vector3.zero)),
                    "sampled navigation endpoint is outside ground");
            var path = new NavMeshPath();
            Require(NavMesh.CalculatePath(from.position, to.position, filter, path) && path.status == NavMeshPathStatus.PathComplete,
                    "loaded navigation has no complete approach/local path");
            Debug.Log($"[RaidGroundSaved] {scene.name} bounds={floor} wallRenderers={wallRenderers.Length} wallColliders={wallColliders.Length} " +
                      $"material={materialPath} texture={texturePath} tileMetres={layer.tileSize} repeats={tiling} " +
                      $"navTriangles={navigation.indices.Length / 3} path={path.status} from={from.position} to={to.position} corners={path.corners.Length}");
        }

        private static bool ContainsXZ(Bounds outer, Bounds inner)
        {
            const float epsilon = 0.01f;
            return outer.min.x <= inner.min.x + epsilon && outer.min.z <= inner.min.z + epsilon &&
                   outer.max.x >= inner.max.x - epsilon && outer.max.z >= inner.max.z - epsilon;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
