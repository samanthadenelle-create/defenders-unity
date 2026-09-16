// =============================================================================
// DungeonComposer (v1) — the first slice of the scene/dungeon CREATOR (WO-479).
//
// Builds a DARK, torch-lit, walkable dungeon from a RECIPE (rooms + corridors +
// per-room encounters), in scripted chunks — proving the composable-chunk
// architecture end to end. v1 = a built-in 3-room demo recipe (Entry -> Choke
// encounter -> Keep boss) so it works out of the box; later versions read the
// recipe from JSON + a seed-budget generator (the AI-sculpt path).
//
// REUSES (does NOT reinvent, CLAUDE.md §9): Village2Playable's public scene-wiring
// (AddSceneDefaultsToActiveScene / ImportEventSystem / ImportHero / WireCamera /
// ImportVillageHud), GarrisonController (encounter spawner, by reflection — same
// build-tooling exemption), and UnityEditor.AI.NavMeshBuilder for the bake.
//
// READ/WRITE: creates + SAVES a NEW scene (Assets/Scenes/Dungeon_Demo.unity) +
// registers it in Build Settings. Never touches a shipping scene.
//
// Run: DeNelle.Editor.DungeonComposer.BuildDemo  (run-unity-method, editor closed)
// =============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class DungeonComposer
    {
        private const string ScenePath = "Assets/Scenes/Dungeon_Demo.unity";
        private const string TypeGarrison = "DeNelle.Village.World.Camps.GarrisonController";

        // ---- Recipe model (v1: built-in; v2 reads this from JSON) -----------------
        private class Room
        {
            public string id; public float cx, cz, w, d;
            public bool isEncounter; public string[] roster; public bool isBoss;
        }
        private class Corridor { public string fromId, toId; public float width; }

        // The v1 demo recipe — a linear gauntlet up +Z: Entry -> Choke (healer+DPS) -> Keep (boss).
        private static List<Room> DemoRooms() => new List<Room>
        {
            new Room { id="Entry",  cx=0, cz=0,  w=14, d=14, isEncounter=false },
            new Room { id="Choke",  cx=0, cz=30, w=10, d=16, isEncounter=true,
                       roster=new[]{ "hollow-acolyte", "hollow-warrior", "hollow-walker", "hollow-walker" } }, // caster(heal-ish)+brute+grunts
            new Room { id="Keep",   cx=0, cz=58, w=20, d=20, isEncounter=true, isBoss=true,
                       roster=new[]{ "orc-berserker", "orc-shaman", "hollow-warrior", "hollow-walker", "hollow-walker" } },
        };
        private static List<Corridor> DemoCorridors() => new List<Corridor>
        {
            new Corridor { fromId="Entry", toId="Choke", width=4 },
            new Corridor { fromId="Choke", toId="Keep",  width=4 },
        };

        private static Material _floorMat, _wallMat;

        [MenuItem("Defenders/Dungeon/Build Demo (v1 composer)")]
        public static void BuildDemo()
        {
            Log("=== DungeonComposer v1: build demo START ===");
            var rooms = DemoRooms();
            var corridors = DemoCorridors();

            // Fresh empty scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("DungeonRoot").transform;

            BuildMaterials();

            // --- Geometry: room floors + corridor floors (walkable) + side rails (dark walls) ---
            var roomById = new Dictionary<string, Room>();
            foreach (var r in rooms)
            {
                roomById[r.id] = r;
                MakeFloor(root, $"Floor_{r.id}", r.cx, r.cz, r.w, r.d);
            }
            foreach (var c in corridors)
            {
                if (!roomById.TryGetValue(c.fromId, out var a) || !roomById.TryGetValue(c.toId, out var b)) continue;
                BuildCorridor(root, a, b, c.width);
            }
            // Perimeter rails per room (leave open — rooms are connected by corridors; rails are dark dressing).
            foreach (var r in rooms) BuildRoomRails(root, r);

            // --- Dark lighting profile (the "keep it darker") -------------------------
            ApplyDarkLighting();

            // --- Torches (warm point lights along the rooms/corridors) ----------------
            int torches = 0;
            foreach (var r in rooms) torches += PlaceRoomTorches(root, r);
            Log($"Placed {torches} torch light(s).");

            // --- Playable wiring: camera + light defaults, EventSystem, hero, HUD -----
            // (reuse Village2Playable's public importers — same DeNelle.Editor assembly)
            Village2Playable.AddSceneDefaultsToActiveScene();   // camera (smart-follow) + a dim dir light + ambient
            ApplyDarkLighting();                                 // re-assert dark AFTER defaults (defaults set bright ambient)
            Village2Playable.ImportEventSystem();
            var hero = Village2Playable.ImportHero(root, null);  // no Heart in a dungeon; hero is a NavMeshAgent
            if (hero != null)
            {
                hero.transform.position = new Vector3(rooms[0].cx, 0f, rooms[0].cz); // seat in the Entry room
                Village2Playable.WireCameraTargetToHero(hero);
            }
            Village2Playable.ImportVillageHud(root);

            // --- Encounters: a GarrisonController per encounter room (role/type mix) --
            int encounters = 0;
            foreach (var r in rooms) if (r.isEncounter) { BuildEncounter(root, r); encounters++; }
            Log($"Wired {encounters} encounter room(s).");

            // --- Bake the navmesh over the flagged floors/walls ----------------------
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            bool navOk = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(rooms[0].cx, 0f, rooms[0].cz), out _, 4f, UnityEngine.AI.NavMesh.AllAreas);
            Log($"NavMesh baked; entry-room sample walkable={navOk}.");

            // --- Save + register ------------------------------------------------------
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            // WO-1731: the bake above wrote this scene's navmesh reference. A save that only
            // reports `ok=False` into a log line leaves the scene pointing at a navmesh it never
            // persisted, and the scene ships with nothing walkable and no error. THROW
            // (RaidNavBake.cs:109-111).
            if (!saved)
                throw new System.InvalidOperationException(
                    "[DungeonComposer] could not save navigation for " + ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            EnsureInBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Log($"Saved '{ScenePath}' (ok={saved}). Rooms={rooms.Count} corridors={corridors.Count} encounters={encounters} torches={torches}.");
            Log("=== DungeonComposer v1 DONE — open Dungeon_Demo.unity (or --scene=Dungeon_Demo) and Play ===");
        }

        // ---- geometry helpers ----------------------------------------------------
        private static void BuildMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _floorMat = new Material(lit) { name = "DungeonFloor" };
            if (_floorMat.HasProperty("_BaseColor")) _floorMat.SetColor("_BaseColor", new Color(0.18f, 0.17f, 0.16f, 1f)); // dark stone
            _wallMat = new Material(lit) { name = "DungeonWall" };
            if (_wallMat.HasProperty("_BaseColor")) _wallMat.SetColor("_BaseColor", new Color(0.12f, 0.11f, 0.11f, 1f));   // darker stone
        }

        private static GameObject MakeBox(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool obstacle)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
            // Box collider stays (primitive cube has one) — geometry is solid.
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.NavigationStatic);
            return go;
        }

        private static void MakeFloor(Transform parent, string name, float cx, float cz, float w, float d)
        {
            MakeBox(parent, name, new Vector3(cx, -0.25f, cz), new Vector3(w, 0.5f, d), _floorMat, false);
        }

        // A corridor floor strip between two rooms (axis-aligned along the dominant axis).
        private static void BuildCorridor(Transform parent, Room a, Room b, float width)
        {
            float dz = Mathf.Abs(b.cz - a.cz), dx = Mathf.Abs(b.cx - a.cx);
            if (dz >= dx)
            {
                float z0 = a.cz + Mathf.Sign(b.cz - a.cz) * (a.d * 0.5f);
                float z1 = b.cz - Mathf.Sign(b.cz - a.cz) * (b.d * 0.5f);
                float cz = (z0 + z1) * 0.5f, len = Mathf.Abs(z1 - z0);
                MakeBox(parent, $"Corridor_{a.id}_{b.id}", new Vector3(a.cx, -0.25f, cz), new Vector3(width, 0.5f, len), _floorMat, false);
                // side rails
                MakeBox(parent, $"CorrRailW_{a.id}_{b.id}", new Vector3(a.cx - width * 0.5f - 0.25f, 1.25f, cz), new Vector3(0.5f, 3f, len), _wallMat, true);
                MakeBox(parent, $"CorrRailE_{a.id}_{b.id}", new Vector3(a.cx + width * 0.5f + 0.25f, 1.25f, cz), new Vector3(0.5f, 3f, len), _wallMat, true);
            }
            else
            {
                float x0 = a.cx + Mathf.Sign(b.cx - a.cx) * (a.w * 0.5f);
                float x1 = b.cx - Mathf.Sign(b.cx - a.cx) * (b.w * 0.5f);
                float cx = (x0 + x1) * 0.5f, len = Mathf.Abs(x1 - x0);
                MakeBox(parent, $"Corridor_{a.id}_{b.id}", new Vector3(cx, -0.25f, a.cz), new Vector3(len, 0.5f, width), _floorMat, false);
                MakeBox(parent, $"CorrRailN_{a.id}_{b.id}", new Vector3(cx, 1.25f, a.cz - width * 0.5f - 0.25f), new Vector3(len, 3f, 0.5f), _wallMat, true);
                MakeBox(parent, $"CorrRailS_{a.id}_{b.id}", new Vector3(cx, 1.25f, a.cz + width * 0.5f + 0.25f), new Vector3(len, 3f, 0.5f), _wallMat, true);
            }
        }

        // Dark perimeter walls around a room, leaving the long sides open enough for corridor mouths.
        private static void BuildRoomRails(Transform parent, Room r)
        {
            float hw = r.w * 0.5f, hd = r.d * 0.5f;
            // E/W full walls (corridors run along Z, so leave N/S open for them)
            MakeBox(parent, $"WallW_{r.id}", new Vector3(r.cx - hw - 0.25f, 1.5f, r.cz), new Vector3(0.5f, 3.5f, r.d), _wallMat, true);
            MakeBox(parent, $"WallE_{r.id}", new Vector3(r.cx + hw + 0.25f, 1.5f, r.cz), new Vector3(0.5f, 3.5f, r.d), _wallMat, true);
        }

        // ---- lighting ------------------------------------------------------------
        private static void ApplyDarkLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.08f);   // near-black ambient — torches do the lighting
            RenderSettings.ambientIntensity = 0.4f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.03f, 0.03f, 0.04f);
            RenderSettings.fogDensity = 0.025f;
            // Dim any directional sun the defaults added.
            foreach (var l in Object.FindObjectsByType<Light>())
                if (l != null && l.type == LightType.Directional) l.intensity = 0.12f;
        }

        // ---- torches -------------------------------------------------------------
        private static int PlaceRoomTorches(Transform parent, Room r)
        {
            float hw = r.w * 0.5f - 0.6f, hd = r.d * 0.5f - 0.6f;
            Vector3[] spots =
            {
                new Vector3(r.cx - hw, 2.4f, r.cz - hd), new Vector3(r.cx + hw, 2.4f, r.cz - hd),
                new Vector3(r.cx - hw, 2.4f, r.cz + hd), new Vector3(r.cx + hw, 2.4f, r.cz + hd),
            };
            int n = 0;
            foreach (var p in spots)
            {
                var go = new GameObject($"Torch_{r.id}_{n}");
                go.transform.SetParent(parent, false);
                go.transform.position = p;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.28f);   // warm flame
                light.intensity = 2.6f;
                light.range = 12f;
                light.shadows = LightShadows.Soft;
                // reuse TorchFlicker if the type resolves (it lives in Assets/_Village2)
                var tf = FindType("TorchFlicker") ?? FindType("DeNelle.Village.TorchFlicker") ?? FindType("DeNelle.TorchFlicker");
                if (tf != null && go.GetComponent(tf) == null) go.AddComponent(tf);
                n++;
            }
            return n;
        }

        // ---- encounter (GarrisonController per room, by reflection) --------------
        private static void BuildEncounter(Transform parent, Room r)
        {
            var gType = FindType(TypeGarrison);
            if (gType == null) { Warn($"GarrisonController not found — encounter for '{r.id}' skipped (is DeNelle.Village compiled?)."); return; }

            var roomGo = new GameObject($"Encounter_{r.id}");
            roomGo.transform.SetParent(parent, false);
            roomGo.transform.position = new Vector3(r.cx, 0f, r.cz);

            // EnemySpawnPoints group — GarrisonController.ResolveSpawnPoints auto-finds these children.
            var grp = new GameObject("EnemySpawnPoints");
            grp.transform.SetParent(roomGo.transform, false);
            int count = r.roster != null ? r.roster.Length : 3;
            for (int i = 0; i < count; i++)
            {
                var sp = new GameObject($"Spawn_{i}");
                sp.transform.SetParent(grp.transform, false);
                float ang = (i / (float)Mathf.Max(1, count)) * Mathf.PI * 2f;
                float rad = Mathf.Min(r.w, r.d) * 0.28f;
                sp.transform.position = new Vector3(r.cx + Mathf.Cos(ang) * rad, 0f, r.cz + Mathf.Sin(ang) * rad);
            }

            var garr = roomGo.AddComponent(gType);
            var so = new SerializedObject(garr);
            SetBool(so, "activateOnStart", true);
            SetInt(so, "threatLevel", r.isBoss ? 3 : 2);
            SetInt(so, "minLevel", r.isBoss ? 4 : 2);
            SetInt(so, "maxLevel", r.isBoss ? 6 : 3);
            SetStringArray(so, "enemyTypeIds", r.roster ?? new[] { "hollow-walker", "hollow-warrior", "hollow-walker" });
            so.ApplyModifiedPropertiesWithoutUndo();
            Log($"Encounter '{r.id}': {count} spawn(s), roster=[{string.Join(",", r.roster ?? new string[0])}], boss={r.isBoss}.");
        }

        // ---- serialized-field helpers (no compile-time dep on DeNelle.Village) ----
        private static void SetBool(SerializedObject so, string f, bool v) { var p = so.FindProperty(f); if (p != null) p.boolValue = v; }
        private static void SetInt(SerializedObject so, string f, int v) { var p = so.FindProperty(f); if (p != null) p.intValue = v; }
        private static void SetStringArray(SerializedObject so, string f, string[] v)
        {
            var p = so.FindProperty(f);
            if (p == null) return;
            p.arraySize = v.Length;
            for (int i = 0; i < v.Length; i++) p.GetArrayElementAtIndex(i).stringValue = v[i];
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            // also try a loose by-name match (TorchFlicker may be in a namespace we don't know)
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                foreach (var t in asm.GetTypes())
                    if (t.Name == fullName) return t;
            return null;
        }

        private static void EnsureInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Log($"Added '{scenePath}' to Build Settings.");
        }

        private static void Log(string m)  => Debug.Log("[DungeonComposer] " + m);
        private static void Warn(string m) => Debug.LogWarning("[DungeonComposer] " + m);
    }
}
