// WO-1704/1703: inspect SAVED raid scenes after generation + nav bake. Never saves or bakes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DeNelle.Village;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class RaidPolishSavedProof
    {
        private const int Width = 1920, Height = 1080;
        private sealed class Gate
        {
            public string Name;
            public float Left, Right, Z, Ground, Depth, HeaderTop;
            public Transform Art;
            public Vector3 Outside, Inside;
            public bool Sampled;
        }
        private sealed class Shot
        {
            public string Name;
            public Vector3 Position, Target;
            public float OrthoSize;
            public Shot(string name, Vector3 position, Vector3 target, float orthoSize = 0f)
            { Name = name; Position = position; Target = target; OrthoSize = orthoSize; }
        }
        private struct AgentSettings
        {
            public int Type;
            public float Radius, Height, Climb, Cell;
            public NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = Type, areaMask = NavMesh.AllAreas };
        }

        [MenuItem("Defenders/Art/Verify Saved Raid Polish")]
        public static void Run()
        {
            var failures = new List<string>();
            var report = new StringBuilder();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool restore = false, pristine = false;
            string output = Path.GetFullPath(Path.Combine("Builds", "raid-polish-saved-proof", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
            int expectedImages = 0, written = 0, visited = 0, expectedConfigs = 0;
            try
            {
                Scene initial = SceneManager.GetActiveScene();
                pristine = Application.isBatchMode && initial.IsValid() && initial.isLoaded &&
                    string.IsNullOrEmpty(initial.path) && !initial.isDirty && SceneManager.sceneCount == 1 &&
                    initial.GetRootGameObjects().Length == 0;
                if (!pristine)
                    foreach (var entry in setup)
                    {
                        var scene = SceneManager.GetSceneByPath(entry.path);
                        if (string.IsNullOrEmpty(entry.path) || (scene.IsValid() && scene.isDirty))
                            throw new InvalidOperationException("Saved proof refuses dirty/untitled workspace; use a pristine batch startup or saved clean scene setup.");
                    }
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Visual proof requires a graphics device; remove -nographics.");
                var field = typeof(RaidBaseGenerator).GetField("RaidConfigIds", BindingFlags.NonPublic | BindingFlags.Static);
                var ids = field != null ? field.GetValue(null) as string[] : null;
                if (ids == null || ids.Length == 0 || new HashSet<string>(ids).Count != ids.Length)
                    throw new InvalidOperationException("Actual generator config list missing, empty, or duplicated.");
                expectedConfigs = ids.Length;
                Directory.CreateDirectory(output);
                SceneConfigCatalog.Invalidate();
                restore = true;
                foreach (string id in ids)
                {
                    var def = SceneConfigCatalog.Find(id);
                    if (def == null) { failures.Add(id + " actual config missing"); continue; }
                    int expectedGates = (def.entranceCount >= 2 ? 2 : 1) + Mathf.Max(0, def.interiorWallLayers);
                    int sceneExpected = 7 + 2 * expectedGates;
                    expectedImages += sceneExpected;
                    string path = "Assets/Scenes/RaidBase_" + id + ".unity";
                    if (!File.Exists(path)) { failures.Add(id + " saved scene missing: " + path); continue; }
                    string hash = HashFile(path);
                    GameObject cameraGo = null;
                    try
                    {
                        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        visited++;
                        Physics.SyncTransforms();
                        var roots = scene.GetRootGameObjects();
                        Transform boundary = Find(roots, "ArenaBoundary_Ring");
                        Transform ground = Find(roots, "RaidGround");
                        Transform staging = Find(roots, "RaidStagingPoint");
                        Transform boss = Find(roots, "BossSpawn");
                        if (boundary == null || ground == null || staging == null || boss == null)
                            throw new InvalidOperationException("Saved scene lacks boundary/ground/staging/boss markers; generate and bake first.");
                        AgentSettings agent = ReadAgentSettings(report, id);
                        var gates = ReadGates(roots, failures, id);
                        if (gates.Count != expectedGates)
                            failures.Add(id + " gate count=" + gates.Count + " expected from actual config=" + expectedGates);
                        CheckNavigation(id, gates, staging.position, boss.position, agent, report, failures);
                        var groundRenderer = ground.GetComponent<Renderer>();
                        Material groundMaterial = groundRenderer != null ? groundRenderer.sharedMaterial : null;
                        if (groundMaterial == null || !groundMaterial.HasProperty("_BaseMap") || groundMaterial.GetTexture("_BaseMap") == null)
                            failures.Add(id + " saved ground lacks actual textured _BaseMap");
                        else Record(report, id + " GROUND texture=" + AssetDatabase.GetAssetPath(groundMaterial.GetTexture("_BaseMap")) +
                            " repeats=" + groundMaterial.GetTextureScale("_BaseMap") + " bounds=" + groundRenderer.bounds);
                        CheckKeepTextures(id, roots, groundMaterial, def.interiorWallLayers > 0, report, failures);
                        CheckSavedFloor(id, roots, def.raidDress?.floor, report, failures);
                        Bounds boundaryBounds = BoundsOf(boundary);
                        var shots = PlanShots(boundaryBounds, gates, staging.position, ground);
                        if (shots.Count != sceneExpected)
                            failures.Add(id + " planned images=" + shots.Count + " expected=" + sceneExpected);
                        cameraGo = new GameObject("WO1704_SavedProofCamera");
                        cameraGo.hideFlags = HideFlags.HideAndDontSave;
                        Camera camera = cameraGo.AddComponent<Camera>();
                        Camera source = null;
                        foreach (var root in roots)
                            foreach (var candidate in root.GetComponentsInChildren<Camera>(true))
                                if (source == null || candidate.CompareTag("MainCamera")) source = candidate;
                        if (source == null) throw new InvalidOperationException("Saved scene has no authored camera to copy.");
                        camera.CopyFrom(source);
                        camera.enabled = false;
                        camera.allowMSAA = false;
                        camera.nearClipPlane = 0.05f;
                        camera.farClipPlane = Mathf.Max(source.farClipPlane, boundaryBounds.size.magnitude * 2f);
                        Record(report, id + " SCENE sha256=" + hash + " fog=" + RenderSettings.fog +
                            " fogRange=" + RenderSettings.fogStartDistance + "/" + RenderSettings.fogEndDistance +
                            " ambient=" + RenderSettings.ambientLight + " expectedImages=" + sceneExpected);
                        foreach (var shot in shots)
                        {
                            try { written += Capture(camera, shot, id, output, report, failures); }
                            catch (Exception ex) { failures.Add(id + "/" + shot.Name + " capture: " + ex.Message); }
                        }
                    }
                    catch (Exception ex) { failures.Add(id + " saved proof: " + ex); }
                    finally
                    {
                        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
                        if (HashFile(path) != hash) failures.Add(id + " saved scene bytes changed during read-only proof");
                    }
                }
            }
            catch (Exception ex) { failures.Add("Setup: " + ex); }
            finally
            {
                if (restore)
                {
                    try
                    {
                        if (pristine) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        else EditorSceneManager.RestoreSceneManagerSetup(setup);
                    }
                    catch (Exception ex) { failures.Add("Scene setup restoration: " + ex.Message); }
                }
            }
            if (visited != expectedConfigs || written != expectedImages || expectedImages == 0)
                failures.Add("Ledger incomplete: scenes=" + visited + "/" + expectedConfigs + " images=" + written + "/" + expectedImages);
            string summary = "scenes=" + visited + "/" + expectedConfigs + " images=" + written + "/" + expectedImages + " output=" + output;
            Record(report, summary);
            foreach (string failure in failures) Record(report, "FAIL " + failure);
            if (Directory.Exists(output)) File.WriteAllText(Path.Combine(output, "manifest.txt"), report.ToString());
            if (failures.Count == 0) Debug.Log("RAID_POLISH_SAVED_PROOF_OK " + summary);
            else Debug.LogError("RAID_POLISH_SAVED_PROOF_FAIL " + failures.Count + " " + summary + "\n" + string.Join("\n", failures));
        }

        private static AgentSettings ReadAgentSettings(StringBuilder report, string id)
        {
            var serialized = new SerializedObject(UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject);
            var build = serialized.FindProperty("m_BuildSettings");
            if (build == null) throw new InvalidOperationException("Loaded legacy nav build settings missing");
            var settings = new AgentSettings
            {
                Type = build.FindPropertyRelative("agentTypeID").intValue,
                Radius = build.FindPropertyRelative("agentRadius").floatValue,
                Height = build.FindPropertyRelative("agentHeight").floatValue,
                Climb = build.FindPropertyRelative("agentClimb").floatValue,
                Cell = build.FindPropertyRelative("cellSize").floatValue,
            };
            var data = serialized.FindProperty("m_NavMeshData");
            if (data == null || data.objectReferenceValue == null) throw new InvalidOperationException("Saved scene has no baked NavMeshData");
            string navPath = AssetDatabase.GetAssetPath(data.objectReferenceValue);
            if (settings.Radius <= 0f || settings.Height <= 0f || settings.Cell <= 0f)
                throw new InvalidOperationException("Invalid actual baked agent dimensions");
            Record(report, id + " AGENT actualSceneBake type=" + settings.Type + " radius=" + settings.Radius +
                " height=" + settings.Height + " step=" + settings.Climb + " cell=" + settings.Cell +
                " navData=" + navPath + " sha256=" + HashFile(navPath));
            return settings;
        }

        private static List<Gate> ReadGates(GameObject[] roots, List<string> failures, string id)
        {
            var rows = new Dictionary<int, List<Bounds>>();
            var arts = new List<Transform>();
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("Gatehouse_", StringComparison.Ordinal)) arts.Add(t);
                    if (!t.name.StartsWith("Wall_", StringComparison.Ordinal) ||
                        (!t.name.Contains("_SS_") && !t.name.Contains("_SN_"))) continue;
                    var box = t.GetComponent<BoxCollider>();
                    if (box == null || !box.enabled || box.isTrigger) continue;
                    int key = Mathf.RoundToInt(t.position.z * 100f);
                    if (!rows.TryGetValue(key, out var list)) rows[key] = list = new List<Bounds>();
                    list.Add(box.bounds);
                }
            var gates = new List<Gate>();
            foreach (var pair in rows)
            {
                pair.Value.Sort((a, b) => a.min.x.CompareTo(b.min.x));
                for (int i = 1; i < pair.Value.Count; i++)
                {
                    Bounds left = pair.Value[i - 1], right = pair.Value[i];
                    if (right.min.x - left.max.x < RaidBaseDresser.MinGateWidth - 0.01f) continue;
                    float z = pair.Key / 100f;
                    Transform art = arts.Find(t => Mathf.Abs(t.position.z - z) < 0.25f);
                    if (art == null) { failures.Add(id + " physical gate z=" + z + " lacks saved gate assembly"); continue; }
                    Bounds bounds = BoundsOf(art);
                    float header = float.MaxValue;
                    foreach (var r in art.GetComponentsInChildren<Renderer>(true))
                        if (r.name != "wall_doorway_door" && !r.name.Contains("_Door_") && !r.name.Contains("Portcullis"))
                            header = Mathf.Min(header, r.bounds.max.y);
                    if (header == float.MaxValue) header = bounds.max.y;
                    gates.Add(new Gate { Name = art.name, Left = left.max.x, Right = right.min.x, Z = z,
                        Ground = Mathf.Min(left.min.y, right.min.y), Depth = bounds.extents.z, HeaderTop = header, Art = art });
                }
            }
            gates.Sort((a, b) => a.Z.CompareTo(b.Z));
            return gates;
        }

        private static void CheckNavigation(string id, List<Gate> gates, Vector3 staging, Vector3 boss,
                                            AgentSettings agent, StringBuilder report, List<string> failures)
        {
            foreach (var gate in gates)
            {
                float offset = Mathf.Max(2f, gate.Depth + agent.Radius + 2f * agent.Cell);
                float sign = Mathf.Sign(gate.Z), x = (gate.Left + gate.Right) * 0.5f;
                Vector3 before = FloorAt(new Vector3(x, gate.Ground, gate.Z + sign * offset));
                Vector3 after = FloorAt(new Vector3(x, gate.Ground, gate.Z - sign * offset));
                bool a = Sample(id + "/" + gate.Name + "/outside", before, agent, out gate.Outside, report, failures);
                bool b = Sample(id + "/" + gate.Name + "/inside", after, agent, out gate.Inside, report, failures);
                gate.Sampled = a && b;
                if (!gate.Sampled) continue;
                if ((gate.Outside.z - gate.Z) * sign <= 0f || (gate.Inside.z - gate.Z) * sign >= 0f)
                { failures.Add(id + "/" + gate.Name + " sampling crossed the gate plane"); continue; }
                var path = Calculate(id + "/" + gate.Name, gate.Outside, gate.Inside, agent, report, failures);
                bool crossed = false;
                for (int i = 1; i < path.corners.Length; i++)
                {
                    Vector3 p = path.corners[i - 1], q = path.corners[i];
                    if ((p.z - gate.Z) * (q.z - gate.Z) > 0f || Mathf.Abs(q.z - p.z) < 0.0001f) continue;
                    Vector3 crossing = Vector3.Lerp(p, q, (gate.Z - p.z) / (q.z - p.z));
                    if (crossing.x >= gate.Left + agent.Radius && crossing.x <= gate.Right - agent.Radius &&
                        crossing.y < gate.HeaderTop - agent.Height)
                    { crossed = true; Record(report, id + "/" + gate.Name + " APERTURE_CROSS " + crossing.ToString("F3")); }
                }
                if (!crossed) failures.Add(id + "/" + gate.Name + " complete local path did not cross this ground-level aperture (detour/roof is not gate traversal)");
            }
            Gate south = gates.Find(g => g.Z < 0f && g.Sampled);
            if (south == null) { failures.Add(id + " no sampled south gate for staging/courtyard route"); return; }
            if (Sample(id + "/staging", FloorAt(staging), agent, out Vector3 start, report, failures))
                Calculate(id + "/staging_to_south", start, south.Outside, agent, report, failures);
            var keeps = gates.FindAll(g => g.Sampled && g.Name.Contains("keep"));
            keeps.Sort((a, b) => Mathf.Abs(b.Z).CompareTo(Mathf.Abs(a.Z)));
            Vector3 previousInside = south.Inside;
            foreach (var gate in keeps)
            {
                Calculate(id + "/courtyard_to_" + gate.Name, previousInside, gate.Outside, agent, report, failures);
                previousInside = gate.Inside;
            }
            if (Sample(id + "/boss_approach", FloorAt(boss), agent, out Vector3 target, report, failures))
                Calculate(id + "/gate_to_keep", previousInside, target, agent, report, failures);
        }

        private static bool Sample(string label, Vector3 requested, AgentSettings agent, out Vector3 sampled,
                                   StringBuilder report, List<string> failures)
        {
            float limit = agent.Radius + 2f * agent.Cell;
            bool ok = NavMesh.SamplePosition(requested, out NavMeshHit hit, limit, agent.Filter);
            sampled = ok ? hit.position : requested;
            float horizontal = Vector2.Distance(new Vector2(requested.x, requested.z), new Vector2(sampled.x, sampled.z));
            Record(report, label + " SAMPLE requested=" + requested.ToString("F3") + " actual=" + sampled.ToString("F3") +
                " found=" + ok + " delta=" + Vector3.Distance(requested, sampled).ToString("F3") +
                " horizontalClamp=" + horizontal.ToString("F3") + " maxSearch=" + limit.ToString("F3"));
            if (!ok || horizontal > agent.Radius)
            { failures.Add(label + " missing nav endpoint or excessive horizontal clamping"); return false; }
            return true;
        }

        private static NavMeshPath Calculate(string label, Vector3 from, Vector3 to, AgentSettings agent,
                                             StringBuilder report, List<string> failures)
        {
            var path = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(from, to, agent.Filter, path);
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            Record(report, label + " PATH calculated=" + calculated + " status=" + path.status + " corners=" + path.corners.Length +
                " length=" + length.ToString("F3") + " direct=" + Vector3.Distance(from, to).ToString("F3") +
                " from=" + from.ToString("F3") + " to=" + to.ToString("F3"));
            if (!calculated || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2 ||
                Vector3.Distance(path.corners[path.corners.Length - 1], to) > agent.Cell)
                failures.Add(label + " path did not completely reach its actual endpoint");
            return path;
        }

        private static void CheckSavedFloor(string id, GameObject[] roots, string token, StringBuilder report, List<string> failures)
        {
            Transform courtyard = Find(roots, "Zone_Courtyard");
            Transform approach = Find(roots, "Zone_Approach");
            int count = 0, pairs = 0; float gap = 0f, overlap = 0f;
            var rows = new Dictionary<int, List<Bounds>>();
            foreach (Transform zone in new[] { courtyard, approach })
            {
                if (zone == null) continue;
                foreach (Transform child in zone)
                {
                    if (child.name != "Floor") continue;
                    count++;
                    if (zone != courtyard) continue;
                    int key = Mathf.RoundToInt(child.position.x * 1000f);
                    if (!rows.TryGetValue(key, out var row)) rows[key] = row = new List<Bounds>();
                    row.Add(BoundsOf(child));
                }
            }
            foreach (var row in rows.Values)
            {
                row.Sort((a, b) => a.center.z.CompareTo(b.center.z));
                for (int i = 1; i < row.Count; i++)
                {
                    float delta = row[i].min.z - row[i - 1].max.z;
                    gap = Mathf.Max(gap, delta); overlap = Mathf.Max(overlap, -delta); pairs++;
                }
            }
            Record(report, id + " SAVED_FLOOR token=" + token + " tiles=" + count + " pairs=" + pairs + " maxGap=" + gap + " maxOverlap=" + overlap);
            if (token == "floor_dirt_large" && count != 0) failures.Add(id + " flat dirt overlay still hides textured ground");
            if (token == "floor_tile_large" && (pairs < 100 || gap > 0.01f || overlap > 0.025f))
                failures.Add(id + " saved stone floor footprints have gaps/overlap or missing rows");
        }

        private static void CheckKeepTextures(string id, GameObject[] roots, Material ground, bool expected,
                                              StringBuilder report, List<string> failures)
        {
            if (!expected) return;
            var def = SceneConfigCatalog.Find(id);
            string layerName = def?.raidDress?.floor == "floor_tile_large" ? "Stoneback_Rock" : "Path_Dirt";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"Assets/Generated/Terrain/{layerName}.terrainlayer");
            if (layer == null) { failures.Add(id + " missing owned keep texture layer"); return; }
            foreach (string name in new[] { "KeepPlatform", "KeepRamp" })
            {
                Transform surface = Find(roots, name);
                var renderer = surface != null ? surface.GetComponent<Renderer>() : null;
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material == null || material == ground || !EditorUtility.IsPersistent(material) ||
                    !material.HasProperty("_BaseMap") || material.GetTexture("_BaseMap") != layer.diffuseTexture)
                { failures.Add(id + "/" + name + " lacks independent persisted owned floor texture"); continue; }
                Vector3 size = surface.lossyScale;
                var expectedRepeats = new Vector2(Mathf.Abs(size.x) / layer.tileSize.x, Mathf.Abs(size.z) / layer.tileSize.y);
                Vector2 actual = material.GetTextureScale("_BaseMap");
                if (Vector2.Distance(actual, expectedRepeats) > 0.001f)
                    failures.Add(id + "/" + name + " texture metre scale mismatch " + actual + " expected=" + expectedRepeats);
                Record(report, id + "/" + name + " TEXTURE material=" + AssetDatabase.GetAssetPath(material) +
                    " texture=" + AssetDatabase.GetAssetPath(layer.diffuseTexture) + " repeats=" + actual + " metres=" + size);
            }
        }

        private static Vector3 FloorAt(Vector3 position)
        {
            if (Physics.Raycast(new Vector3(position.x, position.y + 100f, position.z), Vector3.down,
                out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) position.y = hit.point.y + 0.05f;
            return position;
        }

        private static List<Shot> PlanShots(Bounds boundary, List<Gate> gates, Vector3 staging, Transform ground)
        {
            var shots = new List<Shot>();
            Vector3 centre = new Vector3(boundary.center.x, 1f, boundary.center.z);
            float extent = Mathf.Max(boundary.extents.x, boundary.extents.z);
            shots.Add(new Shot("overview", centre + new Vector3(0f, 50f, -10f), centre, extent * 1.08f));
            string[] sides = { "south", "east", "north", "west" };
            for (int side = 0; side < 4; side++)
            {
                Vector3 outward = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.back;
                Vector3 target = centre + outward * (extent - 1f) + Vector3.up * 2f;
                shots.Add(new Shot("boundary_" + sides[side], target - outward * 12f + Vector3.up * 2f, target,
                    extent / ((float)Width / Height) * 1.06f));
            }
            Vector3 wallBody = new Vector3(centre.x + extent * 0.35f,
                boundary.min.y + boundary.size.y * 0.45f, centre.z + extent - 1f);
            shots.Add(new Shot("boundary_north_oblique", wallBody + new Vector3(8f, 3f, -14f), wallBody));
            foreach (var gate in gates)
            {
                float sign = Mathf.Sign(gate.Z);
                Vector3 target = new Vector3((gate.Left + gate.Right) * 0.5f, gate.Ground + 1.8f, gate.Z);
                float distance = Mathf.Min(14f, Mathf.Max(6f, extent - Mathf.Abs(gate.Z) - 2f));
                shots.Add(new Shot(gate.Name + "_approach", target + new Vector3(0f, 1.5f, sign * distance), target));
                Vector3 sill = new Vector3(target.x, gate.Ground + 0.35f, gate.Z);
                shots.Add(new Shot(gate.Name + "_sill", sill + new Vector3((gate.Right - gate.Left) * 0.55f, 0.65f, sign * 7f), sill));
            }
            Vector3 floor = ClearGroundPatch(staging, ground);
            // Top-down framing prevents a nearby mage pillar from masking the floor.
            // A slight Z offset keeps LookRotation away from a collinear up vector.
            shots.Add(new Shot("textured_ground", floor + new Vector3(0f, 14f, -0.01f), floor, 3f));
            return shots;
        }

        private static Vector3 ClearGroundPatch(Vector3 staging, Transform ground)
        {
            Bounds bounds = BoundsOf(ground);
            var obstacles = new List<Bounds>();
            foreach (var root in ground.gameObject.scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy &&
                        renderer.transform != ground && !renderer.transform.IsChildOf(ground))
                        obstacles.Add(renderer.bounds);
            // Inspect the full 10.7 x 6m orthographic patch, not just its centre.
            // Every first hit must be the actual saved RaidGround, so walls/props cannot
            // silently turn the texture proof into a shot of an obstruction. Physics
            // alone misses render-only wall art: reject conservative renderer AABB
            // overlap throughout the visible prism as well, including the image edges.
            foreach (float dz in new[] { 0f, -8f, 8f, -16f, 16f })
                foreach (float dx in new[] { 0f, 8f, -8f, 16f, -16f, 24f, -24f })
                {
                    Vector3 centre = new Vector3(staging.x + dx, bounds.max.y, staging.z + dz);
                    bool clear = true;
                    // OrthoSize=3, aspect=1920/1080, camera at14m. Small padding
                    // includes its .01m tilt, near plane, and raster edge coverage.
                    var visiblePrism = new Bounds(centre + Vector3.up * 7.05f, new Vector3(10.9f, 14.2f, 6.3f));
                    foreach (Bounds obstacle in obstacles)
                        if (visiblePrism.Intersects(obstacle)) { clear = false; break; }
                    if (!clear) continue;
                    for (int z = -1; z <= 1 && clear; z++)
                        for (int x = -1; x <= 1; x++)
                        {
                            Vector3 sample = centre + new Vector3(x * 5.4f, 30f, z * 3.1f);
                            if (!Physics.Raycast(sample, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore) ||
                                (hit.transform != ground && !hit.transform.IsChildOf(ground)))
                            { clear = false; break; }
                        }
                    if (clear)
                    {
                        Debug.Log("[RaidPolishSavedProof] CLEAR_GROUND_RENDER_PRISM centre=" + centre +
                            " size=" + visiblePrism.size + " testedRendererBounds=" + obstacles.Count + " overlaps=0 physicsSamples=9");
                        return centre + Vector3.up * 0.02f;
                    }
                }
            throw new InvalidOperationException("No unobstructed saved RaidGround patch for texture proof");
        }

        private static int Capture(Camera camera, Shot shot, string id, string output, StringBuilder report, List<string> failures)
        {
            camera.transform.SetPositionAndRotation(shot.Position, Quaternion.LookRotation(shot.Target - shot.Position));
            camera.orthographic = shot.OrthoSize > 0f;
            camera.orthographicSize = shot.OrthoSize > 0f ? shot.OrthoSize : 5f;
            camera.fieldOfView = 55f;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                Color32[] pixels = texture.GetPixels32();
                double sum = 0, square = 0;
                int low = 255, high = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    int luma = (pixels[i].r * 54 + pixels[i].g * 183 + pixels[i].b * 19) / 256;
                    low = Math.Min(low, luma); high = Math.Max(high, luma);
                    sum += luma; square += luma * luma;
                }
                double variance = square / pixels.Length - Math.Pow(sum / pixels.Length, 2);
                bool blank = high - low < 5 || variance < 1.0;
                string stem = Path.Combine(output, id + "_" + shot.Name);
                string colorPath = stem + "_color.png";
                File.WriteAllBytes(colorPath, texture.EncodeToPNG());
                bool present = File.Exists(colorPath) && new FileInfo(colorPath).Length > 100;
                Record(report, id + "/" + shot.Name + " IMAGE position=" + shot.Position + " target=" + shot.Target +
                    " ortho=" + shot.OrthoSize + " lumaRange=" + low + "-" + high + " variance=" + variance.ToString("F3") +
                    " blank=" + blank + " color=" + colorPath + " sha256=" + HashFile(colorPath));
                if (blank || !present) failures.Add(id + "/" + shot.Name + " missing/blank capture; retained for diagnosis");
                return present ? 1 : 0;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                if (texture != null) Object.DestroyImmediate(texture);
                rt.Release(); Object.DestroyImmediate(rt);
            }
        }

        private static Transform Find(GameObject[] roots, string name)
        {
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
        private static Bounds BoundsOf(Transform root)
        {
            bool any = false; Bounds bounds = default(Bounds);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!any) bounds = renderer.bounds; else bounds.Encapsulate(renderer.bounds);
                any = true;
            }
            if (!any) throw new InvalidOperationException("No visible saved geometry under " + root.name);
            return bounds;
        }
        private static void Record(StringBuilder report, string line)
        {
            report.AppendLine(line);
            Debug.Log("[RaidPolishSavedProof] " + line);
        }
        private static string HashFile(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
