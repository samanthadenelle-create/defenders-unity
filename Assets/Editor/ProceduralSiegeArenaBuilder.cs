using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    // =============================================================================
    // ProceduralSiegeArenaBuilder — the STATIC siege-arena VENUE (WO-388 EVOLVED ARCH).
    // -----------------------------------------------------------------------------
    // Run from: Defenders > Scenes > Build Siege Arena
    //
    // This builds the dedicated, reusable VENUE scene that ArenaMode loads for a raid.
    // It is the STATIC half only — the guaranteed navmesh PLATE + natural cover rings +
    // an outer boundary + a central anchor marker. The per-raid castle PORT + defender
    // spawn are NOT this file (that's ArenaMode / ArenaNavMeshBaker at runtime); leave
    // those alone. The whole point of the plate is that the runtime re-bake can NEVER
    // fail to find ground → kills the "silent win / no navmesh" bug, and ONE venue
    // serves ANY opponent.
    //
    // Why a plate (WO-388 §EVOLVED ARCHITECTURE):
    //  - Big flat walkable PLATE (~140m), MeshCollider ON, so the NavMeshSurface bakes it
    //    as the guaranteed approach/battleground ring. This is the key piece.
    //  - Natural cover in polar rings (PlaceCoverRing math kept from the owner concept:
    //    radius / count / jitter) from polyperfect Nature_M prefabs — trees / rocks /
    //    bushes — colliders ON so they actually block movement + line-of-sight, giving
    //    attackers a multi-directional approach with cover.
    //  - Outer boundary ring (a low wall of large rocks) to frame the venue.
    //  - A central empty "ArenaCastleAnchor" at origin where ArenaMode later PORTS the
    //    defender's castle (own castle = drag-and-drop the self-contained unit; opponent =
    //    OutpostFoundationGenerator.Realize their BaseLayout JSON onto the plate). We do
    //    NOT build a fixed castle here — the castle is DATA, ported per-raid.
    //  - NavMeshSurface on the root + a headless bake (BatchBuildAndBakeSiegeArena) so the
    //    plate bakes ONCE. The runtime re-bake (per-raid) then FUSES the ported castle's
    //    interior plane onto this approach plate into one continuous navmesh.
    //
    // Idempotent: re-running clears the prior ArenaVenueRoot and rebuilds.
    //
    // Editor-only, menu driven, graceful prefab miss handling (primitive fallback) —
    // mirrors CastleHubBuilder / OuterWorldBuilder. The DeNelle.Editor asmdef does NOT
    // reference DeNelle.Village, so the NavMeshSurface bake goes through reflection
    // (same pattern as CastleHubBuilder.BatchAddFloorAndBakeCastle).
    //
    // Packs: polyperfect Low Poly Ultimate Pack _M tier (single-atlas, mobile friendly).
    //   Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/...
    // =============================================================================
    public static class ProceduralSiegeArenaBuilder
    {
        private const string MenuPath = "Defenders/Scenes/Build Siege Arena";
        private const string RootName = "ArenaVenueRoot";
        private const string AnchorName = "ArenaCastleAnchor";
        private const string PlateName = "ArenaPlate_Nav_Walkable";

        // Dedicated venue scene — loaded by ArenaMode/WorldSceneLoader the same way the
        // Castle hub streams OuterWorld. Its own scene file so this builder never collides
        // with VillageSceneBuilder / OuterWorldBuilder (CLAUDE.md §9 parallel-lane rule).
        private const string ArenaScenePath = "Assets/Scenes/SiegeArena.unity";

        // Venue dimensions (metres).
        private const float PlateRadius = 70f;     // ~140m diameter walkable plate
        private const float BoundaryRadius = 72f;  // outer boundary ring just past the plate edge

        // Log tag handed to the shared boundary/cover helper so its warnings still
        // self-identify as coming from THIS builder.
        private const string LogTag = "[ProceduralSiegeArenaBuilder]";

        // WO-1632: the pack root, the tree/rock palettes, the polar-ring placement math
        // and the graceful-miss instantiate now live in ArenaBoundaryRing (same assembly),
        // shared with RaidBaseGenerator's arena boundary. The RNG draw order is unchanged,
        // so this venue's saved layout is identical and needs no re-bake.
        private static string[] TreePaths => ArenaBoundaryRing.TreePaths;
        private static string[] RockPaths => ArenaBoundaryRing.RockPaths;

        private static readonly string[] ShrubPaths =
        {
            "Trees_M/Shrub.prefab",
            "Trees_M/Shrub_Round.prefab",
        };

        // Deterministic jitter so a rebuild reproduces the same venue layout.
        private const int RandomSeed = 9388;

        [MenuItem(MenuPath)]
        public static void BuildSiegeArena()
        {
            var root = BuildVenue();
            Debug.Log("[ProceduralSiegeArenaBuilder] Siege arena VENUE built (plate + cover rings + boundary + anchor).\n" +
                      "NEXT: Save under Assets/Scenes/SiegeArena.unity, then add a NavMeshSurface + bake " +
                      "(or run BatchBuildAndBakeSiegeArena headless). ArenaMode ports the castle onto " +
                      "'" + AnchorName + "' at raid start and re-bakes onto the plate.");
            Selection.activeGameObject = root;
        }

        // =====================================================================
        //  Core venue build (shared by the menu entry + the headless batch entry).
        //  Idempotent: clears the prior root, rebuilds everything under one root.
        // =====================================================================
        private static GameObject BuildVenue()
        {
            // --- Idempotent: clear prior generation in the open scene ---
            var prior = GameObject.Find(RootName);
            if (prior != null)
            {
                Debug.Log("[ProceduralSiegeArenaBuilder] Destroying prior " + RootName + " for rebuild.");
                UnityEngine.Object.DestroyImmediate(prior);
            }

            var root = new GameObject(RootName);
            root.transform.position = Vector3.zero;

            // === GROUND PLATE — the guaranteed walkable base ===
            // A large flat disc with a MeshCollider, so the NavMeshSurface bakes it from
            // Physics Colliders. THIS is the piece that makes the runtime re-bake unable to
            // fail to find ground. Built from a Unity Plane (10m/unit at scale 1) so it is
            // genuinely flat; an invisible visual is fine — the arena venue dresses with cover.
            CreatePlate(root.transform);

            // === CENTRAL ANCHOR — where ArenaMode ports the defender's castle ===
            // Empty marker at origin. We do NOT build a fixed castle here — the castle is data,
            // ported per-raid (own = drag-and-drop self-contained unit; opponent = Realize JSON).
            var anchor = new GameObject(AnchorName);
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localPosition = Vector3.zero;
            anchor.transform.localRotation = Quaternion.identity;

            // === NATURAL COVER RINGS — polar placement (owner's PlaceCoverRing math) ===
            // Multi-directional attacker approach with cover. Pieces keep their colliders so
            // they block movement + LoS. A clear inner radius keeps the castle footprint open.
            var coverRoot = new GameObject("NaturalCover_Rings");
            coverRoot.transform.SetParent(root.transform, false);

            var rng = new System.Random(RandomSeed);

            // Inner clear zone radius — keep the castle-port footprint open. Rings start outside it.
            //  Ring 1: a sparse tree/shrub belt mid-field.
            //  Ring 2: heavier mixed cover further out (the approach skirmish band).
            //  Ring 3: rocky outer cover just inside the boundary.
            int treesPlaced = 0, rocksPlaced = 0, shrubsPlaced = 0;
            PlaceCoverRing(coverRoot.transform, rng, radius: 26f, count: 8,  jitter: 3.5f, TreePaths,  "Tree",  ref treesPlaced,  scaleMin: 0.9f, scaleMax: 1.25f);
            PlaceCoverRing(coverRoot.transform, rng, radius: 38f, count: 12, jitter: 4.5f, ShrubPaths, "Shrub", ref shrubsPlaced, scaleMin: 0.8f, scaleMax: 1.4f);
            PlaceCoverRing(coverRoot.transform, rng, radius: 44f, count: 14, jitter: 5f,   TreePaths,  "Tree",  ref treesPlaced,  scaleMin: 0.9f, scaleMax: 1.3f);
            PlaceCoverRing(coverRoot.transform, rng, radius: 56f, count: 16, jitter: 5f,   RockPaths,  "Rock",  ref rocksPlaced,  scaleMin: 0.9f, scaleMax: 1.6f);

            // === OUTER BOUNDARY RING — frames the venue ===
            // A dense ring of large rocks at the plate edge. Colliders ON so it reads as a wall.
            var boundaryRoot = new GameObject("OuterBoundary_Ring");
            boundaryRoot.transform.SetParent(root.transform, false);
            int boundaryPlaced = 0;
            PlaceCoverRing(boundaryRoot.transform, rng, radius: BoundaryRadius, count: 40, jitter: 1.5f, RockPaths, "Boundary", ref boundaryPlaced, scaleMin: 1.4f, scaleMax: 2.2f);

            Debug.Log($"[ProceduralSiegeArenaBuilder] Venue: plate r={PlateRadius}m, " +
                      $"cover trees={treesPlaced} shrubs={shrubsPlaced} rocks={rocksPlaced}, " +
                      $"boundary pieces={boundaryPlaced}, anchor='{AnchorName}' at origin.");
            return root;
        }

        // =====================================================================
        //  GROUND PLATE — large flat walkable disc with MeshCollider, the guaranteed
        //  navmesh base. A Unity Plane is 10x10m at scale 1, so scale = PlateRadius/5
        //  gives a square plate of side 2*PlateRadius (covering the full circular venue).
        //  Renderer left ON (a neutral ground tint) — the venue should read as a field;
        //  the bake uses the MeshCollider regardless.
        // =====================================================================
        private static void CreatePlate(Transform parent)
        {
            var plate = GameObject.CreatePrimitive(PrimitiveType.Plane); // MeshFilter + MeshRenderer + MeshCollider
            plate.name = PlateName;
            plate.transform.SetParent(parent, false);
            plate.transform.localPosition = Vector3.zero;
            // Plane = 10m per unit-scale. side = 2*PlateRadius → scale = (2*PlateRadius)/10 = PlateRadius/5.
            float plateScale = PlateRadius / 5f;
            plate.transform.localScale = new Vector3(plateScale, 1f, plateScale);

            // Ensure a MeshCollider exists (Plane ships with one) — this is what the
            // NavMeshSurface (Use Geometry = Physics Colliders) bakes as walkable ground.
            if (plate.GetComponent<MeshCollider>() == null) plate.AddComponent<MeshCollider>();

            // Neutral ground tint so the field reads as grass/dirt (URP Lit).
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null)
            {
                var m = new Material(sh) { name = "ArenaPlate_Ground" };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.34f, 0.42f, 0.24f));
                var r = plate.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = m;
            }
        }

        // =====================================================================
        //  PlaceCoverRing — polar-ring placement (owner concept kept verbatim in spirit):
        //  drop `count` cover pieces evenly around a circle of `radius`, each nudged by up
        //  to `jitter` metres so the ring doesn't look mechanical. Pieces are instantiated
        //  as PREFABS (colliders intact → they block movement + LoS) with a graceful
        //  primitive fallback when the polyperfect pack isn't imported. Deterministic via
        //  the shared seeded rng so a rebuild reproduces the layout.
        //
        //  WO-1632: the BODY moved to ArenaBoundaryRing.PlacePolarRing so the raid arena
        //  boundary reuses this exact vocabulary instead of copying it. The RNG draw order
        //  (jx, jz, prefab index, yaw, scale) is preserved there byte-for-byte, so the
        //  saved SiegeArena venue layout is unchanged. This wrapper is kept so the four
        //  cover-ring call sites above read the same as they always did.
        // =====================================================================
        private static void PlaceCoverRing(
            Transform parent, System.Random rng, float radius, int count, float jitter,
            string[] prefabRelPaths, string label, ref int placedCounter,
            float scaleMin, float scaleMax)
        {
            ArenaBoundaryRing.PlacePolarRing(parent, rng, radius, count, jitter, prefabRelPaths,
                                             label, ref placedCounter, scaleMin, scaleMax, LogTag);
        }

        // =====================================================================
        //  HEADLESS BATCH ENTRY — create the SiegeArena scene, build the venue, add a
        //  NavMeshSurface, BAKE the plate ONCE, persist the navmesh asset + scene, and
        //  register the scene in Build Settings. Mirrors CastleHubBuilder.
        //  BatchAddFloorAndBakeCastle's reflection-based NavMeshSurface bake (the
        //  DeNelle.Editor asmdef can't reference Unity.AI.Navigation's API surface for all
        //  build contexts, so we resolve the type + drive it by reflection).
        //
        //  Invoked via run-unity-method.ps1:
        //   -executeMethod DeNelle.Editor.ProceduralSiegeArenaBuilder.BatchBuildAndBakeSiegeArena
        // =====================================================================
        public static void BatchBuildAndBakeSiegeArena()
        {
            // Fresh empty scene — saved by explicit path so batchmode never raises a Save dialog.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Log("BATCH: created empty SiegeArena scene.");

            var root = BuildVenue();

            // Add + configure a NavMeshSurface on the venue root, then bake the plate once.
            BakeVenueNavMesh(root);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ArenaScenePath);
            EnsureInBuildSettings();
            AssetDatabase.SaveAssets();
            Log("BATCH: built + baked + saved " + ArenaScenePath + ". Venue ready for ArenaMode to port a castle onto.");
        }

        // Configure + bake a single NavMeshSurface on the venue root via reflection
        // (Use Geometry = Physics Colliders, Collect = All), then persist the data asset.
        private static void BakeVenueNavMesh(GameObject root)
        {
            var surfType = FindType("Unity.AI.Navigation.NavMeshSurface");
            if (surfType == null)
            {
                Err("BATCH: NavMeshSurface type not resolved — bake skipped (add a NavMeshSurface + bake in-editor). " +
                    "Is the 'AI Navigation' package installed?");
                return;
            }

            var surface = root.GetComponent(surfType) ?? root.AddComponent(surfType);

            // useGeometry = PhysicsColliders (1) so the renderer-agnostic plate + cover colliders bake.
            var ug = surfType.GetProperty("useGeometry");
            if (ug != null) ug.SetValue(surface, Enum.ToObject(ug.PropertyType, 1));
            // collectObjects = All (0) — bake everything under the venue (plate + cover + boundary).
            var co = surfType.GetProperty("collectObjects");
            if (co != null) co.SetValue(surface, Enum.ToObject(co.PropertyType, 0));

            // BuildNavMesh() (sync) — async is NOT a NavMeshSurface method (WO-388 §3 note).
            var build = surfType.GetMethod("BuildNavMesh", Type.EmptyTypes);
            if (build != null) { build.Invoke(surface, null); Log("BATCH: BuildNavMesh() invoked on venue plate."); }
            else { Err("BATCH: NavMeshSurface.BuildNavMesh() not found via reflection — bake skipped."); return; }

            // Persist the freshly-built data as an asset so it survives the scene save.
            var dataProp = surfType.GetProperty("navMeshData");
            var data = dataProp != null ? dataProp.GetValue(surface) as UnityEngine.Object : null;
            if (data != null)
            {
                if (!System.IO.Directory.Exists("Assets/Scenes/SiegeArena"))
                    AssetDatabase.CreateFolder("Assets/Scenes", "SiegeArena");
                string assetPath = "Assets/Scenes/SiegeArena/NavMesh-ArenaPlate.asset";
                if (!AssetDatabase.Contains(data))
                {
                    var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (existing != null) AssetDatabase.DeleteAsset(assetPath);
                    AssetDatabase.CreateAsset(data, assetPath);
                    Log("BATCH: navmesh asset written -> " + assetPath);
                }
                else Log("BATCH: navmesh data already an asset (updated in place).");
            }
            else Err("BATCH: surface.navMeshData was null after bake — the plate likely produced no mesh.");
        }

        // Add SiegeArena.unity to Build Settings (enabled) if absent, so ArenaMode /
        // WorldSceneLoader can load it by name at runtime. Idempotent.
        private static void EnsureInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
                if (s.path == ArenaScenePath) return;   // already listed
            scenes.Add(new EditorBuildSettingsScene(ArenaScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Log("Added " + ArenaScenePath + " to Build Settings.");
        }

        // Reflection type resolver (mirrors CastleHubBuilder / OuterWorldBuilder.FindType).
        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void Log(string m) => Debug.Log("[ProceduralSiegeArenaBuilder] " + m);
        private static void Err(string m) => Debug.LogError("[ProceduralSiegeArenaBuilder] " + m);
    }
}
