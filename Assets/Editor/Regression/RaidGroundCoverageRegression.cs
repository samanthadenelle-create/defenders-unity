// regression-registry: standalone
// Run in a fresh empty batch scene before the fleet; other suites create scene objects.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor.Regression
{
    // WO-1703: exercise the real ground builder on a temporary scene, never scene YAML.
    public static class RaidGroundCoverageRegression
    {
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("RAID_GROUND_COVERAGE_OK - " + reason);
            else Debug.LogError("RAID_GROUND_COVERAGE_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene fixture = default;
            bool ownsScene = false;
            var ownedRoots = new List<GameObject>();
            Material generated = null;
            try
            {
                Type builder = Type.GetType("DeNelle.Editor.RaidNavBake, DeNelle.Editor");
                MethodInfo ensure = builder?.GetMethod("EnsureGround", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (ensure == null) throw new InvalidOperationException("real RaidNavBake.EnsureGround entry missing");

                // The old producer uses global GameObject.Find; do not let a RED run alter
                // an unrelated open raid. The corrected producer is scene-scoped.
                if (GameObject.Find("RaidGround") != null)
                    throw new InvalidOperationException("fixture requires no active external RaidGround; run standalone from an empty scene");

                // Unity refuses editor additive scene creation beside an unsaved scene,
                // and SceneManager.CreateScene is play-mode-only. A pristine EMPTY batch
                // startup scene can host owned fixture roots without discarding anything.
                // Never reuse an interactive, dirty, populated or multiple-scene workspace.
                if (string.IsNullOrEmpty(previous.path))
                {
                    if (!Application.isBatchMode || previous.isDirty ||
                        SceneManager.sceneCount != 1 || previous.GetRootGameObjects().Length != 0)
                        throw new InvalidOperationException("unsafe untitled workspace: fixture needs a pristine empty batch startup scene or a saved scene");
                    fixture = previous;
                }
                else
                {
                    fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    ownsScene = true;
                }
                SceneManager.SetActiveScene(fixture);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ownedRoots.Add(ground);
                ground.name = "RaidGround";
                ground.transform.localScale = new Vector3(0.2f, 1f, 0.2f);

                var boundary = new GameObject("ArenaBoundary_Ring");
                ownedRoots.Add(boundary);
                boundary.transform.position = new Vector3(40f, 0f, -20f);
                boundary.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
                AddWall(boundary.transform, new Vector3(-90f, 2f, 0f), new Vector3(4f, 4f, 104f));
                AddWall(boundary.transform, new Vector3(90f, 2f, 0f), new Vector3(4f, 4f, 104f));
                AddWall(boundary.transform, new Vector3(0f, 2f, -50f), new Vector3(184f, 4f, 4f));
                AddWall(boundary.transform, new Vector3(0f, 2f, 50f), new Vector3(184f, 4f, 4f));
                var decoration = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ownedRoots.Add(decoration);
                decoration.name = "UnrelatedDecoration";
                decoration.transform.position = new Vector3(1000f, 0f, 1000f);

                Bounds wallBounds = boundary.GetComponentInChildren<Renderer>().bounds;
                foreach (var wall in boundary.GetComponentsInChildren<Renderer>()) wallBounds.Encapsulate(wall.bounds);
                ensure.Invoke(null, new object[] { fixture });
                Physics.SyncTransforms();
                generated = ground.GetComponent<Renderer>().sharedMaterial;
                Bounds rendered = ground.GetComponent<Renderer>().bounds;
                var failures = new List<string>();
                if (!ContainsXZ(rendered, wallBounds)) failures.Add($"floor {rendered} does not cover rotated/offcenter wall bounds {wallBounds}");
                if (rendered.max.x >= 999f || rendered.max.z >= 999f) failures.Add("unrelated decoration inflated the floor");
                var collider = ground.GetComponent<MeshCollider>();
                if (collider == null || !collider.enabled || !ContainsXZ(collider.bounds, wallBounds)) failures.Add("collision does not cover the wall footprint");
                if (collider != null && (collider.bounds.size - rendered.size).sqrMagnitude > 0.01f) failures.Add("render and collision extents differ");
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Generated/Terrain/Path_Dirt.terrainlayer");
                if (layer == null || layer.diffuseTexture == null) failures.Add("owned dirt terrain layer unavailable");
                if (generated == null || !generated.HasProperty("_BaseMap") || generated.GetTexture("_BaseMap") == null)
                    failures.Add("ground has no _BaseMap texture");
                else if (layer != null)
                {
                    if (generated.GetTexture("_BaseMap") != layer.diffuseTexture) failures.Add("fixture did not reuse owned terrain texture");
                    Vector2 tiling = generated.GetTextureScale("_BaseMap");
                    if (Mathf.Abs(tiling.x * layer.tileSize.x - rendered.size.x) > 0.01f ||
                        Mathf.Abs(tiling.y * layer.tileSize.y - rendered.size.z) > 0.01f)
                        failures.Add("texture stretched: repeat period differs from terrain layer metres");
                }
                ensure.Invoke(null, new object[] { fixture });
                Bounds second = ground.GetComponent<Renderer>().bounds;
                if ((second.center - rendered.center).sqrMagnitude > 0.0001f || (second.size - rendered.size).sqrMagnitude > 0.0001f)
                    failures.Add("repeat bake changed floor extent");
                Material secondMaterial = ground.GetComponent<Renderer>().sharedMaterial;
                if (generated != secondMaterial && generated != null && !EditorUtility.IsPersistent(generated)) Object.DestroyImmediate(generated);
                generated = secondMaterial;
                reason = failures.Count == 0 ? "rotated/offcenter walls covered; decoration excluded; matching collider; terrain texture repeats in metres; rebake stable" : string.Join("; ", failures);
                return failures.Count == 0;
            }
            catch (Exception ex)
            {
                reason = "raid-ground oracle threw: " + (ex.InnerException ?? ex).Message;
                return false;
            }
            finally
            {
                foreach (var root in ownedRoots) if (root != null) Object.DestroyImmediate(root);
                if (ownsScene && fixture.IsValid()) EditorSceneManager.CloseScene(fixture, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (generated != null && !EditorUtility.IsPersistent(generated)) Object.DestroyImmediate(generated);
            }
        }

        private static void AddWall(Transform parent, Vector3 position, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = position;
            wall.transform.localScale = scale;
        }

        private static bool ContainsXZ(Bounds outer, Bounds inner)
        {
            const float epsilon = 0.01f;
            return outer.min.x <= inner.min.x + epsilon && outer.min.z <= inner.min.z + epsilon &&
                   outer.max.x >= inner.max.x - epsilon && outer.max.z >= inner.max.z - epsilon;
        }
    }
}
