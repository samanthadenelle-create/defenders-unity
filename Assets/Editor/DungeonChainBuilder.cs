// =============================================================================
// DungeonChainBuilder — bakes the THIN WALKABLE CHAIN of three loadable KayKit
// scenes: Outpost1 -> Dungeon -> Outpost2 (WO follow-up to the dungeon/outpost/
// arena consolidation). PRIORITY = a WALKABLE chain (breadth over depth):
// flat floor + collidable walls + ONE continuous navmesh + entry spawn markers +
// working walk-through transitions + a boss-end portal + Build-Settings registration.
// Enemies / drops / crafting are a LATER work order — minimal dressing only here.
//
// REUSES (does NOT reinvent, CLAUDE.md §9):
//   - DungeonComposer's NewScene/SaveScene/EnsureInBuildSettings + flat-CUBE-floor idiom.
//   - EnemyStrongholdBuilder's NavMeshSurface bake idiom (collectObjects=All,
//     useGeometry=PhysicsColliders, layerMask=~0, overrideTileSize + tileSize=1024,
//     RemoveAllNavMeshData() then surface.BuildNavMesh()) — ONE connected region.
//   - SceneTransitionTrigger (DeNelle.Village) added BY REFLECTION (the Editor asmdef
//     cannot reference DeNelle.Village), mirroring CastleHubBuilder.WireOuterWorldConnection.
//
// CANON: the hero is an INPUT-DRIVEN NavMeshAgent that CANNOT cross NavMeshLinks. Any
// vertical the HERO climbs is a walkable RAMP (a tilted thin Cube, pitch <= ~14°) the
// agent walks up. NO NavMeshLink is created for hero paths. The chain is kept FLAT.
//
// READ/WRITE: creates + SAVES three NEW scenes (Assets/Scenes/Outpost1|Dungeon|Outpost2)
// and registers them in Build Settings. Never touches a shipping scene.
//
// Run: DeNelle.Editor.DungeonChainBuilder.BuildChain  (run-unity-method, editor closed)
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.AI;                 // NavMesh / NavMeshHit / NavMeshPath — runtime nav verify
using UnityEngine.SceneManagement;    // Scene
using Unity.AI.Navigation;            // NavMeshSurface — referenced directly (asmdef)
using DeNelle.Core.Diagnostics;       // FlowTrace / Guard — TGVRU (CLAUDE.md §12)

namespace DeNelle.Editor
{
    public static class DungeonChainBuilder
    {
        private const string Sys = "DungeonChain";
        private const string KayDungeonFolder = "Assets/Models/KayKit/dungeon";

        private const string Outpost1Path = "Assets/Scenes/Outpost1.unity";
        private const string DungeonPath   = "Assets/Scenes/Dungeon.unity";
        private const string Outpost2Path  = "Assets/Scenes/Outpost2.unity";

        // The chain entry positions are HARD-CODED + symmetric (entry on the -Z side at
        // (0,0,-12)); every transition references its target by NAME + this fixed entry,
        // so the three scenes can be built in ANY order (each NewScene is Single).
        private static readonly Vector3 EntryPos = new Vector3(0f, 0f, -12f);

        private static Material _floorMat, _wallMat, _rampMat, _portalMat, _crateMat;
        private static List<string> _kayPaths;       // cached dungeon model asset paths
        private static readonly HashSet<string> _warnedTokens = new HashSet<string>();

        // =====================================================================
        // MENU + BATCH ENTRY
        // =====================================================================
        [MenuItem("Defenders/World/Build Dungeon Chain (Outpost1+Dungeon+Outpost2)")]
        public static void BuildChain()
        {
            FlowTrace.Step(Sys, "=== DUNGEON CHAIN BUILD START ===");
            // Order does not matter (transitions reference by NAME + fixed entry); wrap each
            // in Guard.Try so one scene failing does NOT abort the rest of the chain.
            Guard.Try(Sys, "build Outpost1", BuildOutpost1);
            Guard.Try(Sys, "build Outpost2", BuildOutpost2);
            Guard.Try(Sys, "build Dungeon",  BuildDungeon);
            FlowTrace.Step(Sys, "=== DUNGEON CHAIN BUILD COMPLETE ===");
        }

        // =====================================================================
        // 1. OUTPOST1 — entry outpost. Exit (+Z) warps into the Dungeon.
        // =====================================================================
        [MenuItem("Defenders/World/Build Dungeon Chain/Outpost1 only")]
        public static void BuildOutpost1()
        {
            var (scene, root, surface) = NewChainScene("Outpost1");

            // Flat 30x30 floor (top face at y=0), perimeter walls with a +Z doorway gap.
            MakeFloor(root, "Floor", 0f, 0f, 30f, 30f);
            BuildPerimeter(root, 30f, 30f, gapNorth: true, gapSouth: false);

            // Entry marker (hero seats here on arrival from the world / chain).
            MakeMarker(root, "Outpost1_Entry", EntryPos);

            // One interior CHOKE wall with a ~4m centre gap — foreshadows the Phase-2 enemy posts.
            MakeBox(root, "ChokeWall_W", new Vector3(-8.5f, 2f, 0f), new Vector3(13f, 4f, 1f), _wallMat, navStatic: true);
            MakeBox(root, "ChokeWall_E", new Vector3( 8.5f, 2f, 0f), new Vector3(13f, 4f, 1f), _wallMat, navStatic: true);

            // KayKit dressing (cosmetic only — colliders stripped so they never fragment nav).
            DressTorches(root, new[] { new Vector3(-13f, 0f, -13f), new Vector3(13f, 0f, -13f), new Vector3(-13f, 0f, 13f), new Vector3(13f, 0f, 13f) });
            DressWalls(root, 30f);

            // EXIT (+Z) -> Dungeon. Single-load (these isolated spaces never co-exist).
            AddTransition(root, "Outpost1Exit_ToDungeon", new Vector3(0f, 0f, 12f),
                          targetScene: "Dungeon", targetPos: EntryPos, prompt: "Enter the Dungeon");

            // PHASE-2 CONTENT — breakable loot crates (scattered OFF the central spine so the
            // entry->exit path stays clear) + a skeleton group at the choke gap (hero-aggro).
            PlaceBreakables(root, new[]
            {
                (new Vector3(-10f, 0f,  -8f), "crate",  "crate-common"),
                (new Vector3( 10f, 0f,  -8f), "crate",  "crate-common"),
                (new Vector3(-11f, 0f,   6f), "barrel", "barrel-common"),
                (new Vector3( 11f, 0f,   6f), "barrel", "barrel-common"),
                (new Vector3( -6f, 0f,  -4f), "crate",  "crate-common"),
                (new Vector3(  6f, 0f,   4f), "crate",  "crate-common"),
                (new Vector3(-10f, 0f,  11f), "chest",  "chest-rare"),
                (new Vector3( 10f, 0f, -11f), "crate",  "crate-common"),
            });
            PlaceSkeletonGroupMarker(root, new Vector3(0f, 0f, 3f));

            BakeAndVerify(surface, "Outpost1", EntryPos, new Vector3(0f, 0f, 12f));
            SaveChainScene(scene, Outpost1Path);
        }

        // =====================================================================
        // 2. DUNGEON — linear: entry zone -> choke corridor -> raised boss chamber.
        //    A gentle RAMP proves hero verticality; an end PORTAL warps to Outpost2.
        // =====================================================================
        [MenuItem("Defenders/World/Build Dungeon Chain/Dungeon only")]
        public static void BuildDungeon()
        {
            var (scene, root, surface) = NewChainScene("Dungeon");

            const float raisedY = 1.5f;   // boss-chamber platform height (hero walks the ramp up to it)

            // Main floor: entry + choke zone. 24 wide x 32 deep, spans z[-16..+16] (top at y=0).
            MakeFloor(root, "Floor_Main", 0f, 0f, 24f, 32f);

            // Side rails along the length (E/W) + a south end wall. North end stays OPEN (portal there).
            MakeBox(root, "RailW", new Vector3(-12f, 2f, 6f), new Vector3(1f, 4f, 44f), _wallMat, navStatic: true);
            MakeBox(root, "RailE", new Vector3( 12f, 2f, 6f), new Vector3(1f, 4f, 44f), _wallMat, navStatic: true);
            MakeBox(root, "EndWall_S", new Vector3(0f, 2f, -16f), new Vector3(24f, 4f, 1f), _wallMat, navStatic: true);

            // CHOKE corridor: an interior wall near z=0 with a ~4m centre gap.
            MakeBox(root, "ChokeWall_W", new Vector3(-7f, 2f, 0f), new Vector3(10f, 4f, 1f), _wallMat, navStatic: true);
            MakeBox(root, "ChokeWall_E", new Vector3( 7f, 2f, 0f), new Vector3(10f, 4f, 1f), _wallMat, navStatic: true);

            // RAMP up into the raised boss chamber: tilted thin cube z[8..16] rising 0 -> 1.5m.
            // run=8, rise=1.5 => pitch ~10.6° (<= 14°). The agent walks straight up it.
            float pitch = Mathf.Atan2(raisedY, 8f) * Mathf.Rad2Deg;   // ~10.6°
            float rampLen = Mathf.Sqrt(8f * 8f + raisedY * raisedY);  // along-slope length
            var ramp = MakeBox(root, "BossRamp", new Vector3(0f, raisedY * 0.5f, 12f),
                               new Vector3(10f, 0.5f, rampLen), _rampMat, navStatic: true);
            ramp.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);  // +Z end tilts UP

            // Raised boss-chamber platform: 24 wide x 12 deep, spans z[16..28], top at y=raisedY.
            MakeBox(root, "BossPlatform", new Vector3(0f, raisedY - 0.25f, 22f),
                    new Vector3(24f, 0.5f, 12f), _floorMat, navStatic: true);
            // Platform side rails + back wall to enclose the chamber (back stays solid behind the portal).
            MakeBox(root, "BossRailW", new Vector3(-12f, raisedY + 2f, 22f), new Vector3(1f, 4f, 12f), _wallMat, navStatic: true);
            MakeBox(root, "BossRailE", new Vector3( 12f, raisedY + 2f, 22f), new Vector3(1f, 4f, 12f), _wallMat, navStatic: true);

            // Entry marker (hero arrives here from Outpost1).
            MakeMarker(root, "Dungeon_Entry", EntryPos);
            // Boss anchor (a LATER WO spawns the boss here).
            MakeMarker(root, "Boss_Anchor", new Vector3(0f, raisedY, 18f));

            // KayKit dressing.
            DressTorches(root, new[] { new Vector3(-11f, 0f, -14f), new Vector3(11f, 0f, -14f),
                                       new Vector3(-11f, raisedY, 20f), new Vector3(11f, raisedY, 20f) });
            DressWalls(root, 32f);

            // PORTAL at the innermost point past the boss anchor -> Outpost2.
            var portalGo = new GameObject("DungeonPortal_ToOutpost2");
            portalGo.transform.SetParent(root, false);
            portalGo.transform.position = new Vector3(0f, raisedY, 24f);
            // KayKit arch tile (or tinted-cube fallback) as the portal visual.
            var arch = KayTile("doorway", portalGo.transform, Vector3.zero, Quaternion.identity);
            if (arch == null)
            {
                var pillar = MakeBox(portalGo.transform, "PortalFrame", new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 0.5f), _portalMat, navStatic: false);
                StripColliders(pillar);
            }
            // The walk-through transition lives on the portal object (Single-load to Outpost2).
            AddTransition(portalGo.transform, "PortalTrigger", new Vector3(0f, raisedY, 24f),
                          targetScene: "Outpost2", targetPos: EntryPos, prompt: "Take the Portal");

            // PHASE-2 CONTENT — breakable loot crates in the main room + boss chamber (OFF the
            // central spine + portal line so paths stay clear) + a skeleton group at the choke.
            PlaceBreakables(root, new[]
            {
                (new Vector3(-9f, 0f,    -10f), "crate",  "crate-common"),
                (new Vector3( 9f, 0f,    -10f), "crate",  "crate-common"),
                (new Vector3(-10f, 0f,    -4f), "barrel", "barrel-common"),
                (new Vector3( 10f, 0f,    -4f), "barrel", "barrel-common"),
                (new Vector3(-8f, 0f,      8f), "crate",  "crate-common"),
                (new Vector3( 8f, 0f,      8f), "crate",  "crate-common"),
                (new Vector3(-9f, raisedY, 20f), "chest", "chest-rare"),
                (new Vector3( 9f, raisedY, 20f), "chest", "chest-rare"),
            });
            PlaceSkeletonGroupMarker(root, new Vector3(0f, 0f, 4f));

            BakeAndVerify(surface, "Dungeon", EntryPos, new Vector3(0f, raisedY, 24f));
            SaveChainScene(scene, DungeonPath);
        }

        // =====================================================================
        // 3. OUTPOST2 — exit outpost. Optional return exit (+Z) -> OuterWorld.
        // =====================================================================
        [MenuItem("Defenders/World/Build Dungeon Chain/Outpost2 only")]
        public static void BuildOutpost2()
        {
            var (scene, root, surface) = NewChainScene("Outpost2");

            MakeFloor(root, "Floor", 0f, 0f, 30f, 30f);
            BuildPerimeter(root, 30f, 30f, gapNorth: true, gapSouth: false);

            MakeMarker(root, "Outpost2_Entry", EntryPos);

            DressTorches(root, new[] { new Vector3(-13f, 0f, -13f), new Vector3(13f, 0f, -13f), new Vector3(-13f, 0f, 13f), new Vector3(13f, 0f, 13f) });
            DressWalls(root, 30f);

            // Return exit -> the castle HUB (MainCastle_Hall), NOT OuterWorld standalone. OuterWorld
            // streams ADDITIVE over a hub via WorldSceneLoader; single-loading it leaves no
            // MainCastle_Hall -> no castle walls + no hub navmesh (owner F8 2026-06-30: "spawned home,
            // no navmesh, no castle walls"). Loading the hub restores the castle + its navmesh, and
            // WorldSceneLoader re-adds OuterWorld additive. Land in the courtyard; WarpTo snaps to mesh.
            AddTransition(root, "Outpost2Exit_ToWorld", new Vector3(0f, 0f, 12f),
                          targetScene: "MainCastle_Hall", targetPos: new Vector3(0f, 0.5f, -12f), prompt: "Return Home");

            BakeAndVerify(surface, "Outpost2", EntryPos, new Vector3(0f, 0f, 12f));
            SaveChainScene(scene, Outpost2Path);
        }

        // =====================================================================
        // SCENE LIFECYCLE
        // =====================================================================
        private static (Scene scene, Transform root, NavMeshSurface surface) NewChainScene(string name)
        {
            EnsureMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(name + "Root").transform;

            // A dim directional light so the baked scene is not pitch-black when opened.
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.0f;
            sun.color = new Color(1f, 0.96f, 0.86f);

            // NavMeshSurface bake idiom (EnemyStrongholdBuilder — robust single connected region).
            var surface = root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.overrideTileSize = true;
            surface.tileSize = 1024;   // one tile (~170m > our floors) => NO internal tile borders

            FlowTrace.Step(Sys, $"NEW_SCENE {name} (root='{name}Root', NavMeshSurface configured)");
            return (scene, root, surface);
        }

        private static void BakeAndVerify(NavMeshSurface surface, string sceneName, Vector3 entry, Vector3 exit)
        {
            // Clear any stale navmesh instance first, then bake ONE clean connected region.
            NavMesh.RemoveAllNavMeshData();
            surface.BuildNavMesh();

            bool entryOk = NavMesh.SamplePosition(entry, out NavMeshHit _, 2f, NavMesh.AllAreas);
            var path = new NavMeshPath();
            bool calc = NavMesh.CalculatePath(entry, exit, NavMesh.AllAreas, path);
            bool complete = calc && path.status == NavMeshPathStatus.PathComplete;

            if (entryOk && complete)
                FlowTrace.Step(Sys, $"NAV_OK {sceneName} PathComplete (entry sampled, entry->exit corners={path.corners.Length})");
            else
                FlowTrace.Fail(Sys, $"NAV_FAIL {sceneName} entrySampled={entryOk} pathStatus={(calc ? path.status.ToString() : "CalculatePath=false")}");
        }

        private static void SaveChainScene(Scene scene, string path)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, path);
            // WO-1731: BakeAndVerify wrote this scene's navmesh reference. A save that silently
            // failed would leave the scene pointing at a navmesh that is not there, and the
            // scene would ship with nothing constraining anything -- with no error on screen.
            // A bake that cannot persist its own reference THROWS (RaidNavBake.cs:109-111).
            if (!saved)
                throw new System.InvalidOperationException(
                    "[DungeonChainBuilder] could not save navigation for " + path +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            EnsureInBuildSettings(path);
            // NOTE (proven by data 2026-06-30): in BATCHMODE, EditorSceneManager.SaveScene writes a
            // freshly-created scene in BINARY even though the project is ForceText (mode reads
            // ForceText, firstByte still 0x00). ForceReserializeAssets / OpenScene+SaveScene do NOT
            // convert it headlessly — only the interactive GUI editor does. So these three scenes are
            // committed BINARY; .gitattributes marks them `binary` (like TerrainData/NavMesh) so git
            // never EOL-mangles them. They are builder-generated + never hand-edited (§3), so a
            // non-diffable blob is fine. See memory: gitattributes-binary-asset-eol-corruption.
            FlowTrace.Step(Sys, $"SAVED '{path}' (ok={saved}, binary — git-safe via .gitattributes)");
        }

        // =====================================================================
        // GEOMETRY HELPERS
        // =====================================================================
        private static void EnsureMaterials()
        {
            if (_floorMat != null) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _floorMat  = MakeMat(lit, "ChainFloor",  new Color(0.20f, 0.19f, 0.18f));
            _wallMat   = MakeMat(lit, "ChainWall",   new Color(0.13f, 0.12f, 0.12f));
            _rampMat   = MakeMat(lit, "ChainRamp",   new Color(0.24f, 0.22f, 0.18f));
            _portalMat = MakeMat(lit, "ChainPortal", new Color(0.35f, 0.18f, 0.55f));
            _crateMat  = MakeMat(lit, "ChainCrate",  new Color(0.55f, 0.40f, 0.24f));
        }

        private static Material MakeMat(Shader lit, string name, Color c)
        {
            var m = new Material(lit) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        // Flat CUBE floor — top face at y=0 (NOT a Plane: a Plane bakes fragmented coplanar
        // sheets; a Cube bakes ONE connected region — canon).
        private static void MakeFloor(Transform parent, string name, float cx, float cz, float w, float d)
        {
            MakeBox(parent, name, new Vector3(cx, -0.25f, cz), new Vector3(w, 0.5f, d), _floorMat, navStatic: true);
        }

        private static GameObject MakeBox(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool navStatic)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
            if (navStatic)
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.NavigationStatic);
            }
            return go;
        }

        // Empty marker GameObject at world position (entry/spawn/anchor).
        private static GameObject MakeMarker(Transform parent, string name, Vector3 worldPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            return go;
        }

        // Perimeter walls (height 4, thickness 1) on the floor edges. Optional doorway GAP
        // (~4m centred) on the +Z (north) and/or -Z (south) side.
        private static void BuildPerimeter(Transform parent, float w, float d, bool gapNorth, bool gapSouth)
        {
            const float h = 4f, t = 1f, wallY = 2f, gap = 4f;
            float hw = w * 0.5f, hd = d * 0.5f;

            // E/W full walls (run along Z).
            MakeBox(parent, "Wall_W", new Vector3(-hw, wallY, 0f), new Vector3(t, h, d), _wallMat, navStatic: true);
            MakeBox(parent, "Wall_E", new Vector3( hw, wallY, 0f), new Vector3(t, h, d), _wallMat, navStatic: true);

            // N wall (+Z).
            BuildSpanWall(parent, "Wall_N", wallY, hd, w, t, h, alongX: true, gap: gapNorth ? gap : 0f);
            // S wall (-Z).
            BuildSpanWall(parent, "Wall_S", wallY, -hd, w, t, h, alongX: true, gap: gapSouth ? gap : 0f);
        }

        // Build a wall span along X at a fixed Z (alongX). If gap>0, split into two segments
        // leaving a centred opening of width `gap`.
        private static void BuildSpanWall(Transform parent, string name, float wallY, float fixedZ,
                                          float spanLen, float thickness, float height, bool alongX, float gap)
        {
            float half = spanLen * 0.5f;
            if (gap <= 0f)
            {
                MakeBox(parent, name, new Vector3(0f, wallY, fixedZ), new Vector3(spanLen, height, thickness), _wallMat, navStatic: true);
                return;
            }
            float segLen = half - gap * 0.5f;          // each side segment length
            float segCenter = (half + gap * 0.5f) * 0.5f;
            MakeBox(parent, name + "_L", new Vector3(-segCenter, wallY, fixedZ), new Vector3(segLen, height, thickness), _wallMat, navStatic: true);
            MakeBox(parent, name + "_R", new Vector3( segCenter, wallY, fixedZ), new Vector3(segLen, height, thickness), _wallMat, navStatic: true);
        }

        // =====================================================================
        // KAYKIT DRESSING (cosmetic only — colliders stripped, never NavigationStatic)
        // =====================================================================
        private static void DressTorches(Transform parent, Vector3[] spots)
        {
            for (int i = 0; i < spots.Length; i++)
            {
                var holder = new GameObject($"TorchDressing_{i}");
                holder.transform.SetParent(parent, false);
                holder.transform.position = spots[i] + new Vector3(0f, 2.2f, 0f);
                KayTile("torch", holder.transform, Vector3.zero, Quaternion.identity);
                // A warm point light regardless of whether the model resolved.
                var lt = holder.AddComponent<Light>();
                lt.type = LightType.Point;
                lt.color = new Color(1f, 0.62f, 0.28f);
                lt.intensity = 2.2f;
                lt.range = 12f;
            }
        }

        private static void DressWalls(Transform parent, float length)
        {
            // A few decorative wall tiles dropped along the -X rail (cosmetic; no nav impact).
            int count = Mathf.Max(1, Mathf.FloorToInt(length / 8f));
            float start = -length * 0.5f + 4f;
            for (int i = 0; i < count; i++)
            {
                float z = start + i * 8f;
                var holder = new GameObject($"WallDressing_{i}");
                holder.transform.SetParent(parent, false);
                holder.transform.position = new Vector3(-14.4f, 0f, z);
                KayTile("wall", holder.transform, Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
            }
        }

        // Find the first dungeon asset whose filename CONTAINS the token; instantiate it.
        // Falls back to a tinted primitive Cube + a one-time LogWarning if not found.
        private static GameObject KayTile(string nameContains, Transform parent, Vector3 localPos, Quaternion rot)
        {
            string path = FindKayPath(nameContains);
            if (!string.IsNullOrEmpty(path))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model != null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                    if (inst == null) inst = Object.Instantiate(model, parent);
                    inst.transform.localPosition = localPos;
                    inst.transform.localRotation = rot;
                    StripColliders(inst);   // dressing must NEVER fragment / trap the navmesh
                    return inst;
                }
            }
            if (_warnedTokens.Add(nameContains))
                Debug.LogWarning($"[{Sys}] KayKit dungeon tile containing '{nameContains}' not found under {KayDungeonFolder} — using primitive fallback.");
            // Tiny tinted cube fallback (cosmetic placeholder; collider stripped, not nav-static).
            var fb = MakeBox(parent, $"Fallback_{nameContains}", parent.position + localPos, new Vector3(1f, 1f, 0.3f), _portalMat, navStatic: false);
            fb.transform.localRotation = rot;
            StripColliders(fb);
            return fb;
        }

        private static string FindKayPath(string token)
        {
            EnsureKayPaths();
            token = token.ToLowerInvariant();
            foreach (var p in _kayPaths)
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (file.Contains(token)) return p;
            }
            return null;
        }

        private static void EnsureKayPaths()
        {
            if (_kayPaths != null) return;
            _kayPaths = new List<string>();
            if (!AssetDatabase.IsValidFolder(KayDungeonFolder))
            {
                Debug.LogWarning($"[{Sys}] KayKit dungeon folder missing: {KayDungeonFolder} (dressing will use primitives).");
                return;
            }
            // GUID search across the folder, keep only .gltf/.fbx model assets.
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { KayDungeonFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string ext = System.IO.Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".gltf" || ext == ".fbx") _kayPaths.Add(p);
            }
        }

        private static void StripColliders(GameObject go)
        {
            if (go == null) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
        }

        // =====================================================================
        // TRANSITION (SceneTransitionTrigger via REFLECTION — Editor asm can't ref DeNelle.Village)
        // =====================================================================
        private static void AddTransition(Transform parent, string name, Vector3 worldPos,
                                          string targetScene, Vector3 targetPos, string prompt)
        {
            var triggerGo = new GameObject(name);
            triggerGo.transform.SetParent(parent, false);
            triggerGo.transform.position = worldPos;

            // Trigger volume (~6x4x6).
            var col = triggerGo.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(6f, 4f, 6f);

            var transType = FindType("DeNelle.Village.SceneTransitionTrigger");
            if (transType == null)
            {
                Debug.LogWarning($"[{Sys}] DeNelle.Village.SceneTransitionTrigger not found (is the script compiled?). " +
                                 "Trigger collider added; behaviour can be attached after compile + re-run.");
                FlowTrace.Warn(Sys, $"TRANSITION {name}: SceneTransitionTrigger type unresolved — behaviour NOT attached.");
                return;
            }

            var comp = triggerGo.AddComponent(transType);
            transType.GetField("targetSceneName")?.SetValue(comp, targetScene);
            transType.GetField("targetPosition")?.SetValue(comp, targetPos);
            transType.GetField("loadAdditive")?.SetValue(comp, false);   // isolated spaces — single-load
            transType.GetField("ProximityRadius")?.SetValue(comp, 6f);
            transType.GetField("requireConfirm")?.SetValue(comp, false);
            transType.GetField("promptOverride")?.SetValue(comp, prompt);

            FlowTrace.Step(Sys, $"TRANSITION {name} -> '{targetScene}'@{targetPos} (single-load, prompt='{prompt}')");
        }

        // =====================================================================
        // PHASE-2 CONTENT (breakables + skeleton group — DeNelle.Village components
        // attached BY REFLECTION; the Editor asmdef cannot reference DeNelle.Village).
        // =====================================================================

        /// <summary>Scatter loot chests: a NavigationStatic box carrying a reflection-attached
        /// DeNelle.Village.BreakableContainer with its lootTableId set.
        /// <para>
        /// ⛔ WO-1132: these are NO LONGER placed on the "Enemy" layer. Owner ruling
        /// 2026-08-21 made the loot container an OPENABLE chest rather than an attackable
        /// prop. The old relayer existed so the hero's enemy-mask OverlapSphere would hit
        /// the crate and damage it - but it also made every crate a valid target for the
        /// HOSTILE RETICLE, which is the whole of WO-1047, and it made the combat camera
        /// frame a crate. The chest is now interacted with by proximity prompt, so it needs
        /// no layer trick at all. Do not re-add one.
        /// </para></summary>
        private static void PlaceBreakables(Transform root, (Vector3 pos, string token, string table)[] spots)
        {
            var bcType = FindType("DeNelle.Village.BreakableContainer");
            if (bcType == null)
                FlowTrace.Warn(Sys, "BreakableContainer type unresolved — crates placed as inert cubes (re-run after compile to attach behaviour).");

            for (int i = 0; i < spots.Length; i++)
            {
                var (pos, token, table) = spots[i];
                // Box sits ON the floor (centre lifted +0.5 so a 1m box rests at pos.y).
                var go = MakeBox(root, $"Breakable_{token}_{i}",
                                 pos + new Vector3(0f, 0.5f, 0f), Vector3.one, _crateMat, navStatic: true);

                if (bcType != null)
                {
                    var comp = go.AddComponent(bcType);
                    SetPrivateField(bcType, comp, "lootTableId", string.IsNullOrEmpty(table) ? "crate-common" : table);
                }
            }
            FlowTrace.Step(Sys, $"BREAKABLES placed {spots.Length} chests (default layer - NOT Enemy, WO-1132; navStatic).");
        }

        /// <summary>Place a "SkeletonGroup_Spawn" marker carrying a reflection-attached
        /// DeNelle.Village.OutpostEnemyGroupSpawner (self-spawns its hero-aggro group on Start).</summary>
        private static void PlaceSkeletonGroupMarker(Transform root, Vector3 pos)
        {
            var go = MakeMarker(root, "SkeletonGroup_Spawn", pos);
            var spType = FindType("DeNelle.Village.OutpostEnemyGroupSpawner");
            if (spType != null)
            {
                go.AddComponent(spType);
                FlowTrace.Step(Sys, $"SKELETON_GROUP marker + spawner at {pos}.");
            }
            else
            {
                FlowTrace.Warn(Sys, $"OutpostEnemyGroupSpawner type unresolved — marker placed WITHOUT spawner at {pos} (re-run after compile).");
            }
        }

        // Set a private/serialized field by name (the BreakableContainer lootTableId is [SerializeField] private).
        private static void SetPrivateField(System.Type t, object obj, string field, object val)
        {
            var f = t.GetField(field, System.Reflection.BindingFlags.Instance
                                      | System.Reflection.BindingFlags.NonPublic
                                      | System.Reflection.BindingFlags.Public);
            if (f != null) f.SetValue(obj, val);
            else FlowTrace.Warn(Sys, $"SetPrivateField: '{field}' not found on {t.Name}.");
        }

        // =====================================================================
        // SHARED HELPERS (copied from DungeonComposer / CastleHubBuilder)
        // =====================================================================
        private static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static void EnsureInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            FlowTrace.Step(Sys, $"BUILD_SETTINGS added '{scenePath}'");
        }
    }
}
