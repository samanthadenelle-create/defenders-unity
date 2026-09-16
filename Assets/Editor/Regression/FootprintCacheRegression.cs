// regression-registry: standalone
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Village;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class FootprintCacheRegression
    {
        public static void RunStandalone()
        {
            var failures = new List<string>();
            var owned = new List<GameObject>();
            var resident = (IDictionary)typeof(StructureContentWarmer).GetField("s_resident", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var absent = (HashSet<string>)typeof(StructureContentWarmer).GetField("s_deadAddresses", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            string prefix = "footprint-fixture-" + Guid.NewGuid().ToString("N");
            string address = prefix + "/art", alternate = prefix + "/alternate", missing = prefix + "/late";
            var previous = SceneManager.GetActiveScene();
            bool ownsScene = !string.IsNullOrEmpty(previous.path);
            if (!ownsScene && (previous.isDirty || previous.rootCount != 0))
                throw new InvalidOperationException("Requires an empty batch scene or saved scene; no unsaved work is discarded.");
            var scene = ownsScene ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive) : previous;
            SceneManager.SetActiveScene(scene);
            Material material = null;
            try
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                GameObject Make(string name, Vector3 size)
                {
                    var root = new GameObject(name); owned.Add(root);
                    root.hideFlags = HideFlags.DontSave;
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.transform.SetParent(root.transform, false);
                    body.transform.localScale = size;
                    body.transform.localPosition = new Vector3(.7f, .2f, -.3f);
                    Object.DestroyImmediate(body.GetComponent<Collider>());
                    body.GetComponent<Renderer>().sharedMaterial = material;
                    return root;
                }
                var source = Make("AsymmetricSource", new Vector3(4, 2, 1));
                var other = Make("DifferentSource", new Vector3(1, 2, 5));
                source.transform.rotation = Quaternion.Euler(0, 23, 0);
                resident["GameObject:" + address] = source;
                resident["GameObject:" + alternate] = other;
                CatalogEntry Entry(string suffix, string path) => new CatalogEntry
                {
                    id = prefix + suffix, visualPrefabPath = path,
                    orientation = new OrientationFix { manual = true, euler = new float[] {0, 0, 0} }
                };
                Vector2 Created(CatalogEntry entry)
                {
                    var root = StructureFactory.Create(entry, new Pose(Vector3.zero, Quaternion.identity), null);
                    if (root == null) throw new Exception("Create returned null");
                    try
                    {
                        var renderers = root.GetComponentsInChildren<Renderer>();
                        if (renderers.Length == 0) throw new Exception("Create has no renderers");
                        Bounds bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        return new Vector2(bounds.size.x, bounds.size.z);
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                void Check(string label, Vector2 actual, Vector2 expected, float tolerance = .0002f)
                {
                    bool ok = float.IsFinite(actual.x) && float.IsFinite(actual.y) && Vector2.Distance(actual, expected) <= tolerance;
                    Debug.Log($"FOOTPRINT_CACHE_CASE {label} actual={actual:F6} expected={expected:F6} pass={ok}");
                    if (!ok) failures.Add(label);
                }
                var rotated = Entry("-rotation", address);
                rotated.orientation.euler[1] = 45;
                Check("manual-rotation-parity", StructureFactory.MeasureUprightFootprintXZ(rotated), Created(rotated));

                // A unique known-absent address avoids real network requests. The later insertion
                // exercises the real loader's resident lookup, not a replacement measurement API.
                var late = Entry("-late", missing);
                absent.Add(missing);
                Check("missing-fallback", StructureFactory.MeasureUprightFootprintXZ(late), new Vector2(3, 3));
                absent.Remove(missing);
                resident["GameObject:" + missing] = source;
                Check("residency-recovery", StructureFactory.MeasureUprightFootprintXZ(late), Created(late));

                var tuning = Entry("-tuning", address);
                Check("initial", StructureFactory.MeasureUprightFootprintXZ(tuning), Created(tuning));
                tuning.repo.heightMul = .5f;
                Check("height-change", StructureFactory.MeasureUprightFootprintXZ(tuning), Created(tuning));
                tuning.repo.maxFootprint = 1.5f;
                Check("cap-change", StructureFactory.MeasureUprightFootprintXZ(tuning), Created(tuning));
                tuning.visualPrefabPath = alternate;
                Check("address-change", StructureFactory.MeasureUprightFootprintXZ(tuning), Created(tuning));
                tuning.repo.maxFootprint = 0;
                tuning.orientation.manual = false;
                tuning.visualPrefabPath = address;
                StructureFactory.MeasureUprightFootprintXZ(tuning);
                tuning.repo.preservePrefabRotation = true;
                Check("preserve-change", StructureFactory.MeasureUprightFootprintXZ(tuning), Created(tuning));

                var precision = Entry("-precision", address);
                StructureFactory.MeasureUprightFootprintXZ(precision);
                precision.orientation.scale = 1.004f;
                Check("sub-rounding-scale", StructureFactory.MeasureUprightFootprintXZ(precision), Created(precision));
                resident["GameObject:" + address] = other;
                Check("resident-replacement", StructureFactory.MeasureUprightFootprintXZ(precision), Created(precision));

                var gridRoot = new GameObject("GridFixture"); owned.Add(gridRoot);
                var grid = gridRoot.AddComponent<PlacementGrid>();
                grid.Occupy(new Vector2Int(2, 2), new Vector2Int(2, 1), prefix);
                tuning.repo.maxFootprint = .5f;
                StructureFactory.MeasureClaimFootprintXZ(tuning);
                if (grid.OccupantAt(new Vector2Int(2, 2)) != prefix || grid.OccupantAt(new Vector2Int(3, 2)) != prefix || grid.OccupantAt(new Vector2Int(4, 2)) != null)
                    failures.Add("existing-occupancy-mutated");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally
            {
                resident.Remove("GameObject:" + address); resident.Remove("GameObject:" + alternate); resident.Remove("GameObject:" + missing);
                absent.Remove(missing);
                foreach (var go in owned) if (go != null) Object.DestroyImmediate(go);
                if (material != null) Object.DestroyImmediate(material);
                SceneManager.SetActiveScene(previous);
                if (ownsScene) EditorSceneManager.CloseScene(scene, true);
            }
            if (failures.Count > 0) Debug.LogError("FOOTPRINT_CACHE_FAIL " + string.Join("; ", failures));
            else Debug.Log("FOOTPRINT_CACHE_OK rotation, residency, sizing inputs, address, precision, resident replacement; existing grid unchanged. EditMode only; no save migration proof.");
        }
    }
}
