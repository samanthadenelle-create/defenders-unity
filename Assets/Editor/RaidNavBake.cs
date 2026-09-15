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
using DeNelle.Village;

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
            "Assets/Scenes/OwnedTown_IronBastion.unity",
        };

        private const string GroundName = "RaidGround";
        private const float  GroundScale = 14f;   // Legacy approach MINIMUM; measured enclosing walls can extend it.
        private const string BoundaryName = "ArenaBoundary_Ring";
        private const string GroundMaterials = "Assets/Generated/RaidGround";
        private const string CladZoneName = "Zone_Clad";

        /// <summary>
        /// Checks if a transform (or any of its parents up the chain) is named CladZoneName.
        /// Used to identify renderer objects that live under Zone_Clad — the visible wall ring
        /// that must be excluded from NavigationStatic so the ground beneath it bakes walkable.
        /// <para/>
        /// ⚠ WO-1723 LANE B CHANGED WHAT THIS REACHES — AND IT IS STILL LOAD-BEARING.
        /// Lane B re-parented every per-segment clad panel under the <c>WallSegment</c> it clads,
        /// so those panels are now excluded by the <c>GetComponentInParent&lt;WallSegment&gt;()</c>
        /// test on its own and this helper no longer decides their fate. It is NOT redundant:
        /// <c>RaidBaseDresser.CladCorners</c> still leaves the corner STUBS — the span each side
        /// hands to its corner post — under <c>Zone_Clad</c>, because no WallSegment owns them,
        /// and this name test is the only thing that reaches those.
        /// <para/>
        /// It is deliberately kept as a belt-and-braces guard for the panels too: it costs one
        /// parent walk per renderer, and removing a working exclusion in the same change that
        /// replaces it is how a re-parent regression would silently re-seal the ring. Judge by a
        /// fresh bake log's <c>clad renderer(s) EXCLUDED</c> count, not by this comment.
        /// </summary>
        private static bool IsUnderCladZone(Transform t)
        {
            while (t != null)
            {
                if (t.name == CladZoneName)
                    return true;
                t = t.parent;
            }
            return false;
        }

        [MenuItem("Defenders/Castle/Bake Raid NavMeshes")]
        public static void BakeAll()
        {
            int ok = 0;
            // WO-1749. A green RAID_NAV_BAKE_OK only ever proved the triangulation was NON-EMPTY.
            // Connectivity was never asked, so a keep platform that bakes as a walkable ISLAND
            // produced an identical green line — which is how the owner's Seeker session logged
            // routeObj=PathPartial 1650 times and PathComplete ZERO times under a green bake.
            var reachFailures = new System.Collections.Generic.List<string>();
            foreach (var scenePath in RaidScenes)
            {
                if (!System.IO.File.Exists(scenePath)) { Debug.LogWarning($"[RaidNavBake] missing {scenePath} — skipped."); continue; }
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                string name = System.IO.Path.GetFileName(scenePath);

                EnsureGround(scene);
                PrepareDestructibleWalls(scene);
                PrepareMovableTowers(scene);

                // Mark all renderers + terrains NavigationStatic so the legacy bake includes them
                // (ground bakes walkable; vertical walls/towers carve out as obstacles).
                // The visible raid wall (Zone_Clad ring) must also be excluded so the ground beneath it
                // bakes walkable; WallSegment.Collapse's carve-drop then actually opens a hole (WO-1723).
                int marked = 0;
                int cladExcluded = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                        bool destructible = r.GetComponentInParent<WallSegment>() != null ||
                            r.GetComponentInParent<DefenseTower>() != null ||
                            IsUnderCladZone(r.transform);
                        GameObjectUtility.SetStaticEditorFlags(r.gameObject, destructible
                            ? flags & ~StaticEditorFlags.NavigationStatic
                            : flags | StaticEditorFlags.NavigationStatic);
                        if (destructible && IsUnderCladZone(r.transform))
                            cladExcluded++;
                        marked++;
                    }
                }

                // Report clad exclusion so the fix is auditable.
                if (cladExcluded > 0)
                    Debug.Log("[RaidNavBake] " + name + ": " + cladExcluded + " clad renderer(s) EXCLUDED from NavigationStatic - the ground under the visible wall now bakes walkable, so WallSegment.Collapse's carve-drop actually opens a hole.");

                UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
                UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new System.InvalidOperationException("Could not save navigation for " + scenePath);

                var tri = NavMesh.CalculateTriangulation();
                bool walkable = tri.vertices != null && tri.vertices.Length > 0;
                Debug.Log($"[RaidNavBake] {name}: ground + {marked} NavigationStatic -> " +
                          $"{(tri.vertices != null ? tri.vertices.Length : 0)} verts / " +
                          $"{(tri.indices != null ? tri.indices.Length / 3 : 0)} tris " +
                          (walkable ? "OK (troops can path)" : "EMPTY (still no walkable floor!)"));
                if (walkable) ok++;

                // WO-1749 — CONNECTIVITY, asked of the mesh that was just baked and is still live.
                // Emits the RAID_NAV_REACH / RAID_NAV_REACH_GOAL lines; the single authority for
                // those legs is RaidKeepReachRegression, so the bake and the regression can never
                // disagree about what "reaches the spire" means (no second copy — CLAUDE.md §16).
                if (!DeNelle.Editor.Regression.RaidKeepReachRegression.ProbeScene(scene, out string reachNote))
                    reachFailures.Add(name + " -> " + reachNote);
            }
            Debug.Log($"[RaidNavBake] DONE — {ok}/{RaidScenes.Length} raid scenes now have a walkable navmesh.");
            if (ok != RaidScenes.Length) throw new System.InvalidOperationException("Navigation bake did not cover every required scene.");
            if (reachFailures.Count > 0)
            {
                // THE MARKER IS WITHHELD, deliberately. WO-1749's ask, verbatim: "so
                // RAID_NAV_BAKE_OK can never again be green over a spire nobody can reach."
                // Not thrown — the reach lines above are the diagnosis and must survive to be read.
                Debug.LogError("RAID_NAV_REACH_FAIL " + reachFailures.Count + " scene(s) — " +
                               string.Join(" ;; ", reachFailures) +
                               " (RAID_NAV_BAKE_OK withheld: the mesh is non-empty but the objective is unreachable)");
                return;
            }
            Debug.Log("RAID_NAV_REACH_OK scenes=" + ok + "; courtyard, ramp foot and platform top all reach the spire");
            Debug.Log("RAID_NAV_BAKE_OK scenes=" + ok + "; wall and tower footprints use runtime carving");
        }

        private static void PrepareMovableTowers(Scene scene)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var tower in root.GetComponentsInChildren<DefenseTower>(true))
            {
                // EditMode does not run Awake: initialize the same contact collider runtime uses.
                typeof(DefenseTower).GetMethod("EnsureContactCollider", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance).Invoke(tower, null);
                Physics.SyncTransforms();
                bool measured = false;
                var localBounds = new Bounds();
                foreach (var collider in tower.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                    Bounds bounds;
                    Transform space;
                    if (collider is BoxCollider box) { bounds = new Bounds(box.center, box.size); space = box.transform; }
                    else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
                    { bounds = mesh.sharedMesh.bounds; space = mesh.transform; }
                    else if (collider is CapsuleCollider capsule)
                    {
                        var size = Vector3.one * capsule.radius * 2f;
                        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                        bounds = new Bounds(capsule.center, size); space = capsule.transform;
                    }
                    else { bounds = collider.bounds; space = null; }
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var local = tower.transform.InverseTransformPoint(space != null ? space.TransformPoint(corner) : corner);
                        if (!measured) { localBounds = new Bounds(local, Vector3.zero); measured = true; }
                        else localBounds.Encapsulate(local);
                    }
                }
                if (!measured || localBounds.size.x <= 0 || localBounds.size.z <= 0)
                    throw new System.InvalidOperationException("Tower has no measurable solid footprint: " + tower.name);
                foreach (var child in tower.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject,
                        GameObjectUtility.GetStaticEditorFlags(child.gameObject) & ~StaticEditorFlags.NavigationStatic);
                var obstacle = tower.GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = tower.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = localBounds.center;
                obstacle.size = localBounds.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                obstacle.carvingTimeToStationary = 0f;
                obstacle.enabled = tower.HpFraction > 0f;
                count++;
            }
            Debug.Log("[RaidNavBake] " + scene.name + ": " + count + " towers use collider-enclosing movable obstacles");
        }

        // Bake continuous ground beneath breakable walls. Their live carving obstacles
        // block intact walls and are disabled by WallSegment.Collapse after destruction.
        // Baking wall geometry itself leaves a permanent hole even after its collider dies.
        private static void PrepareDestructibleWalls(Scene scene)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var wall in root.GetComponentsInChildren<WallSegment>(true))
            {
                var box = wall.GetComponent<BoxCollider>();
                if (box == null || box.isTrigger)
                    throw new System.InvalidOperationException("Raid wall has no solid box for navigation: " + wall.name);
                foreach (var child in wall.GetComponentsInChildren<Transform>(true))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(child.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags & ~StaticEditorFlags.NavigationStatic);
                }
                var obstacle = wall.GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = wall.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = box.center;
                obstacle.size = box.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                obstacle.carvingTimeToStationary = 0f;
                obstacle.enabled = wall.HpFraction > 0f;
                count++;
            }
            Debug.Log("[RaidNavBake] " + scene.name + ": " + count + " destructible walls use removable carving obstacles");
        }

        // WO-1703: the continuous floor is not the dresser's sparse disk of decorative
        // tiles. Refit even an existing plane to the actual enclosing wall footprint,
        // then repeat an owned terrain texture at its authored metre scale.
        private static void EnsureGround(Scene scene)
        {
            Physics.SyncTransforms();
            GameObject ground = null;
            Bounds footprint = new Bounds(Vector3.zero, new Vector3(GroundScale * 10f, 0f, GroundScale * 10f));
            int boundaryParts = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == GroundName) ground = t.gameObject;
                    if (t.name != BoundaryName) continue;
                    // Confined to the enclosing ring: props elsewhere in the scene must
                    // not enlarge the playable floor. World bounds include rotation/offset.
                    foreach (var wall in t.GetComponentsInChildren<Renderer>(true))
                    {
                        footprint.Encapsulate(wall.bounds);
                        boundaryParts++;
                    }
                    foreach (var wall in t.GetComponentsInChildren<Collider>(true))
                        if (wall.enabled && wall.gameObject.activeInHierarchy) footprint.Encapsulate(wall.bounds);
                }
            }
            bool created = false;
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = GroundName;
                SceneManager.MoveGameObjectToScene(ground, scene);
                created = true;
            }
            else
            {
                // Keep any existing approach apron; fitting walls must not remove it.
                var priorRenderer = ground.GetComponent<Renderer>();
                if (priorRenderer != null) footprint.Encapsulate(priorRenderer.bounds);
            }

            var mesh = ground.GetComponent<MeshFilter>();
            var r = ground.GetComponent<Renderer>();
            if (mesh == null || mesh.sharedMesh == null || r == null)
                throw new System.InvalidOperationException("[RaidNavBake] RaidGround has no render mesh");
            var local = mesh.sharedMesh.bounds;
            if (local.size.x <= 0f || local.size.z <= 0f)
                throw new System.InvalidOperationException("[RaidNavBake] RaidGround mesh has empty XZ bounds");
            ground.transform.SetParent(null, true);
            ground.transform.rotation = Quaternion.identity;
            ground.transform.localScale = new Vector3(footprint.size.x / local.size.x, 1f, footprint.size.z / local.size.z);
            ground.transform.position = new Vector3(footprint.center.x, 0f, footprint.center.z) -
                                        Vector3.Scale(local.center, ground.transform.localScale);
            ground.SetActive(true);
            r.enabled = true;
            var collider = ground.GetComponent<MeshCollider>();
            if (collider == null) collider = ground.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh.sharedMesh;
            collider.enabled = true;
            collider.isTrigger = false;
            collider.convex = false;

            TerrainLayer layer = GroundLayerFor(scene.name);
            Material m = GroundMaterialFor(scene, r.sharedMaterial);
            m.SetTexture("_BaseMap", layer.diffuseTexture);
            m.SetTextureScale("_BaseMap", new Vector2(footprint.size.x / layer.tileSize.x, footprint.size.z / layer.tileSize.y));
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", layer.smoothness);
            m.SetFloat("_Metallic", layer.metallic);
            m.SetTexture("_BumpMap", layer.normalMapTexture);
            m.SetFloat("_BumpScale", layer.normalScale);
            if (layer.normalMapTexture != null) m.EnableKeyword("_NORMALMAP");
            else m.DisableKeyword("_NORMALMAP");
            if (EditorUtility.IsPersistent(m))
            {
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            r.sharedMaterial = m;
            TextureKeepSurfaces(scene, m, layer);
            MagentaGuard.ProtectPrimitiveArt(ground, "RaidNavBake.RaidGround");
            if (boundaryParts == 0) Debug.LogWarning($"[RaidNavBake] {scene.name}: no enclosing ring renderers; retained legacy approach floor.");
            Debug.Log($"[RaidNavBake] {(created ? "added" : "refitted")} {GroundName} scene={scene.name} " +
                      $"bounds={r.bounds} boundaryParts={boundaryParts} texture={AssetDatabase.GetAssetPath(layer.diffuseTexture)} " +
                      $"tileMetres={layer.tileSize} repeats={m.GetTextureScale("_BaseMap")} collider=shared-ground-mesh.");
        }

        // Keep geometry uses the same owned floor authority, with independent persisted
        // materials so its metre-sized UV repeats never mutate the enclosing ground.
        private static void TextureKeepSurfaces(Scene scene, Material ground, TerrainLayer layer)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != "KeepPlatform" && renderer.name != "KeepRamp") continue;
                string path = $"{GroundMaterials}/{scene.name}_{renderer.name}.mat";
                bool persist = !string.IsNullOrEmpty(scene.path);
                Material material = persist ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
                if (material == null)
                {
                    material = new Material(ground) { name = renderer.name + "_Ground_URP" };
                    if (persist) AssetDatabase.CreateAsset(material, path);
                }
                material.CopyPropertiesFromMaterial(ground);
                Vector3 size = renderer.transform.lossyScale;
                var repeats = new Vector2(Mathf.Abs(size.x) / layer.tileSize.x, Mathf.Abs(size.z) / layer.tileSize.y);
                material.SetTextureScale("_BaseMap", repeats);
                material.SetTextureOffset("_BaseMap", Vector2.zero);
                if (EditorUtility.IsPersistent(material))
                {
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                renderer.sharedMaterial = material;
                Debug.Log($"[RaidNavBake] {scene.name}/{renderer.name} material={AssetDatabase.GetAssetPath(material)} texture={AssetDatabase.GetAssetPath(layer.diffuseTexture)} repeats={repeats} surfaceMetres={size}");
            }
        }

        private static TerrainLayer GroundLayerFor(string sceneName)
        {
            var def = SceneConfigCatalog.FindBySceneName(sceneName);
            if (def == null && !string.IsNullOrEmpty(sceneName) && sceneName.StartsWith("RaidBase_", System.StringComparison.Ordinal))
                def = SceneConfigCatalog.Find(sceneName.Substring("RaidBase_".Length));
            string floor = def?.raidDress?.floor;
            // The undressed IronBastion template retains the dirt default. Configured
            // arenas follow their authored dirt/tile intent; no swatch atlas is stretched.
            string name = floor == "floor_tile_large" ? "Stoneback_Rock" : "Path_Dirt";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"Assets/Generated/Terrain/{name}.terrainlayer");
            if (layer == null || layer.diffuseTexture == null || layer.tileSize.x <= 0f || layer.tileSize.y <= 0f)
                throw new System.InvalidOperationException($"[RaidNavBake] missing/invalid owned ground layer {name}; refusing an untextured bake");
            if (layer.diffuseTexture.wrapModeU != TextureWrapMode.Repeat || layer.diffuseTexture.wrapModeV != TextureWrapMode.Repeat)
                throw new System.InvalidOperationException($"[RaidNavBake] {name} texture must repeat; refusing stretched/clamped ground");
            return layer;
        }

        private static Material GroundMaterialFor(Scene scene, Material previous)
        {
            bool persist = !string.IsNullOrEmpty(scene.path);
            string path = $"{GroundMaterials}/{scene.name}.mat";
            Material material = persist ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
            if (!persist && previous != null && !EditorUtility.IsPersistent(previous) && previous.name == "RaidGround_URP") material = previous;
            if (material != null)
            {
                if (!material.HasProperty("_BaseMap"))
                    throw new System.InvalidOperationException($"[RaidNavBake] existing ground material lacks URP base map: {path}");
                return material;
            }
            material = MagentaGuard.BuildUrpLitMaterial(Color.white);
            if (material == null || !material.HasProperty("_BaseMap"))
                throw new System.InvalidOperationException("[RaidNavBake] URP ground shader unavailable");
            material.name = "RaidGround_URP";
            if (persist)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
                if (!AssetDatabase.IsValidFolder(GroundMaterials)) AssetDatabase.CreateFolder("Assets/Generated", "RaidGround");
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }
    }
}
