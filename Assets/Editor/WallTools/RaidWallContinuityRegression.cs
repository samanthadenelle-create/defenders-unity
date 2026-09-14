// WO-1704: actual builder geometry, never a copied placement formula or source-token pass.
// Lives in EditorWallTools so it can invoke that assembly without a reverse reference.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using DeNelle.Village;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class RaidWallContinuityRegression
    {
        private const float SeamTolerance = 0.01f;
        private const float RayStep = 0.10f;

        [MenuItem("Tools/Regression/Raid Wall Continuity")]
        public static void RunHeadless()
        {
            var failures = new List<string>();
            var notes = new StringBuilder();
            Scene prior = SceneManager.GetActiveScene();
            Scene fixture = default(Scene);
            bool ownsScene = false;
            bool atmosphereCaptured = false;
            AtmosphereSnapshot atmosphere = default(AtmosphereSnapshot);
            var ownedRoots = new List<GameObject>();
            bool priorBackfaces = Physics.queriesHitBackfaces;
            try
            {
                // Unity refuses additive scene creation beside an unsaved startup scene.
                // Match the proven RaidGroundCoverageRegression lifecycle: only pristine
                // empty batch startup may be reused; interactive/dirty untitled work is refused.
                if (!prior.IsValid() || !prior.isLoaded)
                    throw new InvalidOperationException("fixture needs a loaded pristine batch startup scene or saved scene");
                if (string.IsNullOrEmpty(prior.path))
                {
                    if (!Application.isBatchMode || prior.isDirty || SceneManager.sceneCount != 1 ||
                        prior.GetRootGameObjects().Length != 0)
                        throw new InvalidOperationException("unsafe untitled workspace: fixture needs a pristine empty batch startup scene or a saved scene");
                    fixture = prior;
                }
                else
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                            throw new InvalidOperationException("unsaved companion scene: fixture will not alter the open workspace");
                    fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    ownsScene = true;
                }
                SceneManager.SetActiveScene(fixture);
                atmosphere = AtmosphereSnapshot.Capture();
                atmosphereCaptured = true;
                Physics.queriesHitBackfaces = true;
                SceneConfigCatalog.Invalidate();
                foreach (string id in new[] { "raider_camp_small", "fortified_garrison", "mage_enclave" })
                {
                    var root = new GameObject("WO1704_Fixture_" + id);
                    ownedRoots.Add(root);
                    try
                    {
                        var def = SceneConfigCatalog.Find(id);
                        if (def == null) throw new InvalidOperationException("Missing actual config " + id);
                        // BuildFromConfig destroys a globally named raid root. Call its actual
                        // layout producer on a unique root instead, preserving any open scene.
                        var build = typeof(RaidBaseGenerator).GetMethod("BuildConfigLayout",
                            BindingFlags.Static | BindingFlags.NonPublic);
                        if (build == null) throw new MissingMethodException("BuildConfigLayout");
                        build.Invoke(null, new object[] { def, root.transform });
                        CheckCladding(root.transform, id, failures, notes);
                        CheckInnerGate(root.transform, id, def.interiorWallLayers, failures, notes);
                        CheckGatePassages(root.transform, id, failures, notes);
                        CheckBoundary(root.transform, id, failures, notes);
                    }
                    catch (Exception ex)
                    {
                        Exception actual = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                        failures.Add(id + " fixture exception: " + actual);
                    }
                    finally { Object.DestroyImmediate(root); }
                }
            }
            catch (Exception ex) { failures.Add("Fixture setup: " + ex); }
            finally
            {
                foreach (var root in ownedRoots) if (root != null) Object.DestroyImmediate(root);
                Physics.queriesHitBackfaces = priorBackfaces;
                if (atmosphereCaptured && fixture.IsValid() && fixture.isLoaded)
                {
                    SceneManager.SetActiveScene(fixture);
                    atmosphere.Restore();
                }
                // Only close a scene this fixture created. The reused pristine batch scene
                // stays open, with precisely owned roots removed and atmosphere restored.
                if (ownsScene && fixture.IsValid() && fixture.isLoaded)
                    EditorSceneManager.CloseScene(fixture, true);
                if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
            }
            string report = notes + "\n" + string.Join("\n", failures);
            if (failures.Count > 0) Debug.LogError("RAID_WALL_CONTINUITY_FAIL " + failures.Count + "\n" + report);
            else Debug.Log("RAID_WALL_CONTINUITY_OK\n" + report);
        }

        // DressAtmosphere writes exactly these six RenderSettings properties. Capture
        // in the fixture scene and restore before switching scenes, including reuse mode.
        private struct AtmosphereSnapshot
        {
            private bool fog;
            private FogMode mode;
            private Color color, ambient;
            private float start, end;

            public static AtmosphereSnapshot Capture()
            {
                return new AtmosphereSnapshot
                {
                    fog = RenderSettings.fog,
                    mode = RenderSettings.fogMode,
                    color = RenderSettings.fogColor,
                    ambient = RenderSettings.ambientLight,
                    start = RenderSettings.fogStartDistance,
                    end = RenderSettings.fogEndDistance,
                };
            }

            public void Restore()
            {
                RenderSettings.fog = fog;
                RenderSettings.fogMode = mode;
                RenderSettings.fogColor = color;
                RenderSettings.ambientLight = ambient;
                RenderSettings.fogStartDistance = start;
                RenderSettings.fogEndDistance = end;
            }
        }

        private static void CheckCladding(Transform root, string id, List<string> failures, StringBuilder notes)
        {
            var clad = root.Find("Zone_Clad");
            if (clad == null) { failures.Add(id + " missing actual cladding"); return; }
            // East/west walls have no authored gates. Group by actual radial coordinate,
            // allowing outer and every inner ring to be measured independently.
            var groups = new Dictionary<int, List<Bounds>>();
            foreach (Transform piece in clad)
            {
                if (!piece.name.StartsWith("Clad_", StringComparison.Ordinal)) continue;
                Vector3 along = piece.right;
                if (Mathf.Abs(along.z) < 0.9f) continue;
                int key = Mathf.RoundToInt(piece.position.x * 100f);
                if (!TryBounds(piece, out Bounds b)) { failures.Add(id + " unmeasurable " + piece.name); continue; }
                if (!groups.TryGetValue(key, out var rows)) groups[key] = rows = new List<Bounds>();
                rows.Add(b);
            }
            if (groups.Count < 2) failures.Add(id + " insufficient nongated wall groups");
            foreach (var pair in groups)
            {
                var rows = pair.Value;
                rows.Sort((a, b) => a.min.z.CompareTo(b.min.z));
                float worst = float.MinValue;
                for (int i = 1; i < rows.Count; i++) worst = Mathf.Max(worst, rows[i].min.z - rows[i - 1].max.z);
                notes.AppendLine(id + " clad x=" + (pair.Key / 100f).ToString("F2") +
                    " panels=" + rows.Count + " actualBoundsGap=" + worst.ToString("F4"));
                if (rows.Count < 2 || worst > SeamTolerance)
                    failures.Add(id + " CLADDING SEAM x=" + (pair.Key / 100f).ToString("F2") +
                        " measured=" + worst.ToString("F4") + "m");
            }
            var probes = CreateMeshProbes(clad, root, out var colliders);
            try
            {
                Physics.SyncTransforms();
                foreach (var pair in groups)
                {
                    float x = pair.Key / 100f, extent = Mathf.Abs(x), sign = Mathf.Sign(x);
                    foreach (float height in new[] { 0.30f, 1.50f, 2.00f })
                    {
                        int gaps = 0;
                        var firstGaps = new List<string>();
                        for (float z = -extent + RayStep * 0.5f; z < extent; z += RayStep)
                        {
                            var ray = new Ray(new Vector3(x - sign * 5f, height, z), new Vector3(sign, 0f, 0f));
                            if (!HitsMesh(colliders, ray))
                            {
                                gaps++;
                                if (firstGaps.Count < 8) firstGaps.Add(z.ToString("F4"));
                            }
                        }
                        notes.AppendLine(id + " clad x=" + x.ToString("F2") + " y=" + height.ToString("F2") +
                            " actualTriangleUncoveredSamples=" + gaps + " firstGapZ=[" + string.Join(",", firstGaps) + "]");
                        if (gaps > 0) failures.Add(id + " CLADDING MESH HOLE x=" + x.ToString("F2") +
                            " y=" + height.ToString("F2") + " samples=" + gaps);
                    }
                }
            }
            finally { Object.DestroyImmediate(probes); }
        }

        private static void CheckInnerGate(Transform root, string id, int expected, List<string> failures, StringBuilder notes)
        {
            int count = 0;
            var choke = root.Find("Zone_Choke");
            if (choke != null)
                foreach (Transform t in choke.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Gatehouse_", StringComparison.Ordinal) && TryBounds(t, out _)) count++;
            notes.AppendLine(id + " inner gate assemblies=" + count + " actual authored inner layers=" + expected);
            if (count != expected) failures.Add(id + " INNER GATE: expected " + expected + " measured assemblies=" + count);
        }

        private static void CheckGatePassages(Transform root, string id, List<string> failures, StringBuilder notes)
        {
            var groups = new Dictionary<int, List<Bounds>>();
            foreach (Transform segment in root)
            {
                if (!segment.name.StartsWith("Wall_", StringComparison.Ordinal) ||
                    (!segment.name.Contains("_SS_") && !segment.name.Contains("_SN_"))) continue;
                var box = segment.GetComponent<BoxCollider>();
                if (box == null || !box.enabled || box.isTrigger) continue;
                int key = Mathf.RoundToInt(segment.position.z * 100f);
                if (!groups.TryGetValue(key, out var rows)) groups[key] = rows = new List<Bounds>();
                rows.Add(box.bounds);
            }
            var probes = CreateMeshProbes(root, root, out var colliders);
            try
            {
                Physics.SyncTransforms();
                int gates = 0;
                foreach (var pair in groups)
                {
                    var rows = pair.Value;
                    rows.Sort((a, b) => a.min.x.CompareTo(b.min.x));
                    float gap = 0f, left = 0f, right = 0f;
                    for (int i = 1; i < rows.Count; i++)
                    {
                        float candidate = rows[i].min.x - rows[i - 1].max.x;
                        if (candidate <= gap) continue;
                        gap = candidate; left = rows[i - 1].max.x; right = rows[i].min.x;
                    }
                    // Ungated sides have adjoining physical panels, not a skipped run.
                    if (gap < RaidBaseDresser.MinGateWidth - SeamTolerance) continue;
                    gates++;
                    float z = pair.Key / 100f, sign = Mathf.Sign(z);
                    foreach (float height in new[] { 0.30f, 1.50f, 2.00f })
                    {
                        int longest = 0, run = 0, covered = 0;
                        for (float x = left + RayStep * 0.5f; x < right; x += RayStep)
                        {
                            var ray = new Ray(new Vector3(x, height, z - sign * 4f), new Vector3(0f, 0f, sign));
                            if (HitsMesh(colliders, ray, 8f)) { covered++; run = 0; }
                            else { run++; longest = Mathf.Max(longest, run); }
                        }
                        float clear = longest * RayStep;
                        notes.AppendLine(id + " gate z=" + z.ToString("F2") + " y=" + height.ToString("F2") +
                            " colliderCut=" + gap.ToString("F3") + " visibleClearRun=" + clear.ToString("F2") +
                            " blockedSamples=" + covered);
                        // Body-level opening floor comes from the existing MinGateWidth law.
                        // Ankle/head cuts are reported separately; nav step-height/agent-height
                        // verification belongs to the subsequent actual bake, not this ray sweep.
                        if (Mathf.Approximately(height, 1.50f) && clear + SeamTolerance < RaidBaseDresser.MinGateWidth)
                            failures.Add(id + " GATE CLEARANCE z=" + z.ToString("F2") + " actualBodyClear=" +
                                clear.ToString("F2") + "m < " + RaidBaseDresser.MinGateWidth.ToString("F2") + "m");
                    }
                }
                if (gates == 0) failures.Add(id + " no physical gate openings measured");
            }
            finally { Object.DestroyImmediate(probes); }
        }

        private static void CheckBoundary(Transform root, string id, List<string> failures, StringBuilder notes)
        {
            var boundary = root.Find("ArenaBoundary_Ring");
            if (boundary == null) { failures.Add(id + " missing actual exterior boundary"); return; }
            var probes = CreateMeshProbes(boundary, root, out var colliders);
            try
            {
                if (colliders.Count == 0) { failures.Add(id + " no actual boundary meshes to measure"); return; }
                Physics.SyncTransforms();
                float extent = RaidBaseGenerator.ArenaBoundaryHalfExtent;
                // Measure the original skyline independently of the backing under test.
                // A 3m backplane can pass ankle/head probes while leaving most of a 7m
                // exterior open. Probe through the upper body, leaving decorative caps.
                float skylineTop = 0f;
                foreach (Transform piece in boundary)
                    if (piece.name != "BoundaryBacking" && TryBounds(piece, out Bounds skyline))
                        skylineTop = Mathf.Max(skylineTop, skyline.max.y);
                if (skylineTop <= 2f) { failures.Add(id + " original boundary skyline unmeasurable"); return; }
                notes.AppendLine(id + " boundary originalSkylineTop=" + skylineTop.ToString("F3"));
                // Whole-prefab XZ envelopes cannot prove wall closure: probe actual triangles
                // at ankle, torso, head and upper wall levels. This is not a NavMesh claim.
                foreach (float height in new[] { 0.30f, 1.50f, 2.00f, skylineTop * 0.5f, skylineTop * 0.75f, skylineTop * 0.9f })
                {
                    for (int side = 0; side < 4; side++)
                    {
                        Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                        Vector3 outward = rot * Vector3.back;
                        int gaps = 0, run = 0, longest = 0;
                        for (float along = -extent + RayStep * 0.5f; along < extent; along += RayStep)
                        {
                            Vector3 start = rot * new Vector3(along, height, -extent + 5f);
                            var ray = new Ray(start, outward);
                            bool hit = HitsMesh(colliders, ray);
                            if (!hit) { gaps++; run++; longest = Mathf.Max(longest, run); }
                            else run = 0;
                        }
                        notes.AppendLine(id + " boundary side=" + side + " y=" + height.ToString("F2") +
                            " meshes=" + colliders.Count + " uncoveredSamples=" + gaps +
                            " longestSampledHole=" + (longest * RayStep).ToString("F2") + "m");
                        if (gaps > 0) failures.Add(id + " EXTERIOR MESH HOLE side=" + side + " y=" +
                            height.ToString("F2") + " sampledRun=" + (longest * RayStep).ToString("F2") + "m");
                    }
                }
            }
            finally { Object.DestroyImmediate(probes); }
        }

        private static GameObject CreateMeshProbes(Transform source, Transform parent, out List<MeshCollider> colliders)
        {
            var probes = new GameObject("WO1704_MeshProbes");
            probes.transform.SetParent(parent, false);
            colliders = new List<MeshCollider>();
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
                var go = new GameObject("Probe_" + filter.name);
                go.transform.SetParent(probes.transform, false);
                go.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                go.transform.localScale = filter.transform.lossyScale;
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                colliders.Add(collider);
            }
            return probes;
        }

        private static bool HitsMesh(List<MeshCollider> colliders, Ray ray, float length = 10f)
        {
            foreach (var collider in colliders)
            {
                if (!collider.bounds.IntersectRay(ray, out float distance) || distance > length) continue;
                if (collider.Raycast(ray, out _, length)) return true;
            }
            return false;
        }

        private static bool TryBounds(Transform root, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any;
        }
    }
}
