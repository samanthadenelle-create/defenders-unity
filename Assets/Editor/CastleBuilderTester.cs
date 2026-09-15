using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Sandbox;

namespace DeNelle.Editor
{
    /// <summary>
    /// Headless batchmode tester for the sandbox CastleBuilder. Resolves prefabs by
    /// name (with fallbacks), builds a castle into a fresh scene, and saves it to
    /// Assets/Scenes/CastleTest.unity. Run via:
    ///   Defenders/Sandbox/Test CastleBuilder
    /// or batchmode -executeMethod DeNelle.Editor.CastleBuilderTester.TestBuildCastle
    /// </summary>
    public static class CastleBuilderTester
    {
        private const string ScenePath = "Assets/Scenes/CastleTest.unity";

        [MenuItem("Defenders/Sandbox/Test CastleBuilder")]
        public static void TestBuildCastle()
        {
            var log = new StringBuilder();

            // 1. Fresh empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Host GameObject at origin + component
            var hostGo = new GameObject("CastleBuilderTest");
            hostGo.transform.position = Vector3.zero;
            var castle = hostGo.AddComponent<CastleBuilder>();

            // 3. Resolve prefabs (primary name first, then fallbacks)
            var resolved = new List<string>();
            var missing = new List<string>();

            castle.floorStonePrefab = Resolve("floorStonePrefab", log, resolved, missing,
                "Floor_Stone_3x3m", "Floor_Stone", "Floor_Medieval", "Floor_Brick", "Floor_");
            castle.wallStonePrefab = Resolve("wallStonePrefab", log, resolved, missing,
                "Wall_Stone_3x3_A", "Wall_Stone_3x3_B", "Wall_Stone", "Wall_Medieval_Stone");
            // Corner: try real corner mesh, else fall back to the straight wall we resolved.
            castle.wallCornerPrefab = Resolve("wallCornerPrefab", log, resolved, missing,
                "Wall_Stone_Corner_A", "Wall_Stone_Corner", "Corner");
            if (castle.wallCornerPrefab == null && castle.wallStonePrefab != null)
            {
                castle.wallCornerPrefab = castle.wallStonePrefab;
                log.AppendLine("  wallCornerPrefab: FALLBACK to wallStonePrefab (no corner mesh).");
                // Move it from missing -> resolved bookkeeping.
                missing.Remove("wallCornerPrefab");
                resolved.Add("wallCornerPrefab (=wallStonePrefab fallback)");
            }
            castle.spiralStairsPrefab = Resolve("spiralStairsPrefab", log, resolved, missing,
                "Stairs_House_Spiral_2m", "Spiral", "Stairs_House", "Stairs");
            castle.towerBasePrefab = Resolve("towerBasePrefab", log, resolved, missing,
                "Tower_Medieval_Big", "Tower_Medieval", "Tower");
            // Ramparts reuse the floor prefab.
            castle.rampartFloorPrefab = castle.floorStonePrefab;
            if (castle.rampartFloorPrefab != null)
                log.AppendLine("  rampartFloorPrefab: reuses floorStonePrefab.");
            else
                log.AppendLine("  rampartFloorPrefab: MISSING (no floor prefab to reuse).");
            castle.gatePrefab = Resolve("gatePrefab", log, resolved, missing,
                "Gate_Medieval_Medium", "Gate_Medieval", "Gate");

            // 4. Build
            castle.BuildCastle();

            // 5. Count spawned children under the castle root
            int childCount = 0;
            string rootName = null;
            if (hostGo.transform.childCount > 0)
            {
                var root = hostGo.transform.GetChild(0);
                rootName = root.name;
                // Total descendants under the root (excludes the root itself).
                childCount = root.GetComponentsInChildren<Transform>(true).Length - 1;
            }

            // 6. Save scene
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);

            // 7. Summary
            log.AppendLine("---- Prefab resolution ----");
            log.AppendLine("RESOLVED: " + (resolved.Count > 0 ? string.Join(", ", resolved) : "(none)"));
            log.AppendLine("MISSING:  " + (missing.Count > 0 ? string.Join(", ", missing) : "(none)"));

            bool ok = saved && childCount > 0 && missing.Count == 0;
            if (ok)
                log.AppendLine($"CASTLE_TEST_OK childCount={childCount} root='{rootName}' scene='{ScenePath}'");
            else
            {
                string reason = !saved ? "scene save failed"
                    : childCount == 0 ? "no children spawned (prefabs unresolved?)"
                    : "missing prefabs: " + string.Join(", ", missing);
                log.AppendLine($"CASTLE_TEST_FAIL {reason} (childCount={childCount})");
            }

            Debug.Log(log.ToString());
        }

        // ==================================================================================
        //  ProceduralCastleBuilder tester (NEW) — builds the owner's thick inner/outer-wall
        //  castle into a SEPARATE scene (CastleTest2.unity) with a real ground plane (the
        //  procedural script builds NO floor), then makes it walkable with the SAME village
        //  hero/camera wiring CastleWalkable uses.
        //    Defenders/Sandbox/Test ProceduralCastle
        //    batchmode -executeMethod DeNelle.Editor.CastleBuilderTester.TestBuildProceduralCastle
        // ==================================================================================

        private const string ProScenePath = "Assets/Scenes/CastleTest2.unity";

        [MenuItem("Defenders/Sandbox/Test ProceduralCastle")]
        public static void TestBuildProceduralCastle()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Test ProceduralCastle START ===");

            // 1. Fresh empty scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Host GameObject + component.
            var hostGo = new GameObject("ProceduralCastleTest");
            hostGo.transform.position = Vector3.zero;
            var castle = hostGo.AddComponent<ProceduralCastleBuilder>();

            // 2b. Dimensions are settable without editing code via EditorPrefs keys
            //     (castle.length / castle.width / castle.rampartWidth). Defaults match v3.
            int prefLength = EditorPrefs.GetInt("castle.length", 28);
            int prefWidth = EditorPrefs.GetInt("castle.width", 18);
            int prefRampart = EditorPrefs.GetInt("castle.rampartWidth", 4);
            castle.length = prefLength;
            castle.width = prefWidth;
            castle.rampartWidth = prefRampart;
            log.AppendLine($"  dims (from EditorPrefs): length={prefLength} width={prefWidth} rampartWidth={prefRampart} " +
                "(keys: castle.length / castle.width / castle.rampartWidth)");

            // 3. Resolve prefabs.
            var resolved = new List<string>();
            var missing = new List<string>();

            castle.wallPrefab = Resolve("wallPrefab", log, resolved, missing,
                "Wall_Stone_3x3_A", "Wall_Stone_3x3", "Wall_Stone", "Wall_Medieval_Stone");

            castle.damagedWallPrefab = Resolve("damagedWallPrefab", log, resolved, missing,
                "Wall_Stone_3x3_B", "Wall_Stone_Damaged", "Wall_Stone_Broken");
            if (castle.damagedWallPrefab == null && castle.wallPrefab != null)
            {
                castle.damagedWallPrefab = castle.wallPrefab;
                missing.Remove("damagedWallPrefab");
                resolved.Add("damagedWallPrefab (=wallPrefab fallback)");
                log.AppendLine("  damagedWallPrefab: FALLBACK to wallPrefab (no damaged variant).");
            }

            // barricadeWindow is OPTIONAL — leave null if nothing matches (don't flag missing).
            castle.barricadeWindow = ResolveOptional("barricadeWindow", log, resolved,
                "Wall_Stone_Window", "Window", "Crenel", "Battlement");

            castle.platformPrefab = Resolve("platformPrefab", log, resolved, missing,
                "Floor_Stone_3x3m_A", "Floor_Stone_3x3m", "Floor_Stone", "Floor_Medieval");

            castle.gatePrefab = Resolve("gatePrefab", log, resolved, missing,
                "Gate_Medieval_Medium", "Gate_Medieval", "Gate");

            // stairs: prefer a STRAIGHT (walkable) stairs; spiral is a last resort.
            bool straightStairs;
            castle.stairsPrefab = ResolveStairs(log, resolved, missing, out straightStairs);

            // debris: OPTIONAL — assign whatever Apocalypse_M items resolve; empty is fine.
            castle.debrisPrefabs = ResolveDebris(log, resolved);

            // 4. Ground plane covering the castle footprint (+ margin). The procedural
            //    script builds NO floor, so the base needs one to walk on.
            AddGroundPlane(castle, log);

            // 5. Build (auto-bakes navmesh internally).
            castle.BuildCastle();

            // 6. Make it walkable — SAME wiring as CastleWalkable: scene defaults (camera +
            //    light + ambient/fog), EventSystem, village hero, camera->hero, re-bake.
            Village2Playable.AddSceneDefaultsToActiveScene();
            Village2Playable.ImportEventSystem();

            GameObject hero = Village2Playable.ImportHero(hostGo.transform, /*heart:*/ null);
            Vector3 heroPos = Vector3.zero;
            if (hero == null)
            {
                log.AppendLine("WARN: ImportHero returned null — hero not built.");
            }
            else
            {
                // Spawn the hero on the ground near the gate. The gate sits at
                // (length*unitSize*0.5, 0, 0); drop the hero a few metres in front (+Z),
                // grounded on the plane top (Y=0).
                float gateX = castle.length * castle.unitSize * 0.5f;
                heroPos = new Vector3(gateX, 0f, 4f);
                hero.transform.position = heroPos;
                hero.transform.rotation = Quaternion.Euler(0f, 0f, 0f); // face +Z into the castle

                Village2Playable.WireCameraTargetToHero(hero);
                EnsureHeroControllable(hero, log);
            }

            // 7. Re-bake the NavMesh now that the ground + hero are in place (mark the
            //    ground + floor/stairs walkable, walls/towers/gate obstacles).
            BakeWalkable(scene, log);

            // 8. Count children + save.
            int childCount = hostGo.GetComponentsInChildren<Transform>(true).Length - 1;
            EditorSceneManager.MarkSceneDirty(scene);
            // WO-1731: a bake whose scene is never written back leaves the scene pointing at the
            // navmesh the bake replaced -- which ships as NO navmesh, silently. THROW, do not log
            // `saved=False` into a wall of text nobody reads (RaidNavBake.cs:109-111).
            bool saved = EditorSceneManager.SaveScene(scene, ProScenePath);
            if (!saved)
                throw new System.InvalidOperationException(
                    "[CastleBuilderTester] could not save navigation for " + ProScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");

            log.AppendLine("---- Prefab resolution ----");
            log.AppendLine("RESOLVED: " + (resolved.Count > 0 ? string.Join(", ", resolved) : "(none)"));
            log.AppendLine("MISSING:  " + (missing.Count > 0 ? string.Join(", ", missing) : "(none)"));
            log.AppendLine("Straight (walkable) stairs found: " + (straightStairs ? "YES" : "NO (spiral/none)"));
            log.AppendLine($"Scene saved={saved} @ {ProScenePath}");
            log.AppendLine($"PROCASTLE_TEST_OK childCount={childCount} heroAt=({heroPos.x:F2}, {heroPos.y:F2}, {heroPos.z:F2}) " +
                $"dims={castle.length}x{castle.width}x{castle.rampartWidth}");
            log.AppendLine("=== Test ProceduralCastle DONE ===");

            Debug.Log(log.ToString());
        }

        // Adds a flat ground plane at Y=0 covering the castle footprint + margin, with a real
        // collider, a neutral material, and NavigationStatic so the hero can walk on it.
        private static void AddGroundPlane(ProceduralCastleBuilder castle, StringBuilder log)
        {
            float footprintX = castle.length * castle.unitSize;
            float footprintZ = castle.width * castle.unitSize;
            const float margin = 8f;

            // Unity's built-in Plane is 10x10 units at scale 1 → scale = size / 10.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "CastleGround";
            // Centre under the castle (castle spans x:[0..footprintX], z:[0..footprintZ]).
            ground.transform.position = new Vector3(footprintX * 0.5f, 0f, footprintZ * 0.5f);
            ground.transform.localScale = new Vector3((footprintX + margin) / 10f, 1f, (footprintZ + margin) / 10f);
            ground.transform.SetParent(castle.transform, true);

            // Neutral material (URP/Lit if available, else default).
            var mat = MakeNeutralMaterial();
            if (mat != null)
            {
                var mr = ground.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            // Mark NavigationStatic (collider is already present from the primitive).
            var flags = GameObjectUtility.GetStaticEditorFlags(ground);
            GameObjectUtility.SetStaticEditorFlags(ground, flags | StaticEditorFlags.NavigationStatic);

            log.AppendLine($"  ground: CastleGround plane @ ({ground.transform.position.x:F1},0,{ground.transform.position.z:F1}) " +
                $"covering ~{footprintX + margin:F0}x{footprintZ + margin:F0}m (collider + NavigationStatic).");
        }

        private static Material MakeNeutralMaterial()
        {
            var grey = new Color(0.45f, 0.45f, 0.42f);

            // The project is URP. Under URP the built-in "Standard" shader renders MAGENTA
            // (incompatible) — that was the pink ground. ALWAYS use a URP-compatible shader:
            // prefer URP/Lit, then URP/Simple Lit, then the active pipeline's own default
            // material shader. NEVER fall back to "Standard".
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                // Last resort: clone the render pipeline's default material (guaranteed valid
                // for whatever pipeline is active) and just recolour it.
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (rp != null && rp.defaultMaterial != null)
                {
                    var clone = new Material(rp.defaultMaterial) { name = "CastleGround_Neutral" };
                    if (clone.HasProperty("_BaseColor")) clone.SetColor("_BaseColor", grey);
                    if (clone.HasProperty("_Color")) clone.SetColor("_Color", grey);
                    return clone;
                }
                Debug.LogWarning("[CastleBuilderTester] No URP shader found and no pipeline default material — ground may render pink.");
                return null;
            }

            var mat = new Material(shader) { name = "CastleGround_Neutral" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", grey);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", grey);
            return mat;
        }

        // Walkable bake mirroring CastleWalkable: ground/floor/stairs/rampart = walkable,
        // walls/towers/gate = obstacle, then a synchronous editor bake.
        private static readonly string[] ProWalkable = { "CastleGround", "Floor", "Ground", "Stairs", "Stair", "Rampart", "Platform" };
        private static readonly string[] ProObstacle = { "Wall", "Tower", "Gate" };

        private static void BakeWalkable(Scene scene, StringBuilder log)
        {
            int walkable = 0, obstacle = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    bool isWalkable = NameOrAncestorContainsAny(r.transform, ProWalkable);
                    bool isObstacle = !isWalkable && NameOrAncestorContainsAny(r.transform, ProObstacle);
                    if (!isWalkable && !isObstacle) continue;

                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags | StaticEditorFlags.NavigationStatic);
                    if (isWalkable) walkable++; else obstacle++;
                }
            }
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            log.AppendLine($"  navmesh re-baked: {walkable} walkable + {obstacle} obstacle renderer(s) marked NavigationStatic.");
            if (walkable == 0)
                log.AppendLine("  WARN: no walkable renderers matched — navmesh may be empty.");
        }

        private static bool NameOrAncestorContainsAny(Transform t, string[] fragments)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                foreach (var f in fragments)
                    if (cur.name.Contains(f)) return true;
            return false;
        }

        // Guarantee the hero is active + its HeroLocomotion is enabled (reflection — editor
        // asmdef can't reference DeNelle.Village). Same intent as CastleWalkable.EnsureHeroControllable.
        private static void EnsureHeroControllable(GameObject hero, StringBuilder log)
        {
            if (hero == null) return;
            if (!hero.activeSelf) { hero.SetActive(true); log.AppendLine("  hero was inactive — activated."); }

            System.Type locoType = System.Type.GetType("DeNelle.Village.HeroLocomotion, Assembly-CSharp");
            if (locoType == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("DeNelle.Village.HeroLocomotion", false);
                    if (t != null) { locoType = t; break; }
                }
            }
            if (locoType == null)
            {
                log.AppendLine("  WARN: HeroLocomotion type not resolvable — cannot verify it is enabled.");
                return;
            }
            var loco = hero.GetComponent(locoType) as Behaviour;
            if (loco == null) { log.AppendLine("  WARN: hero has NO HeroLocomotion component."); return; }
            if (!loco.enabled) { loco.enabled = true; log.AppendLine("  HeroLocomotion was DISABLED — re-enabled."); }
            else log.AppendLine("  HeroLocomotion present + enabled.");
        }

        // Optional resolve: returns the first match or null, never flags missing.
        private static GameObject ResolveOptional(string fieldName, StringBuilder log,
            List<string> resolved, params string[] candidates)
        {
            foreach (var c in candidates)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab " + c);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        log.AppendLine($"  {fieldName}: RESOLVED '{prefab.name}' (matched '{c}') @ {path}");
                        resolved.Add(fieldName);
                        return prefab;
                    }
                }
            }
            log.AppendLine($"  {fieldName}: none found (optional — left null).");
            return null;
        }

        // Stairs resolve preferring a STRAIGHT walkable stairs; spiral is last resort.
        private static GameObject ResolveStairs(StringBuilder log, List<string> resolved,
            List<string> missing, out bool straight)
        {
            // Straight-stairs candidates first (avoid spiral so the hero can climb tier-2).
            string[] straightCandidates = { "Stairs_Stone", "Stairs_House_A", "Stairs_House_B", "Stairs_House_C", "Stairs_Medieval", "Staircase" };
            foreach (var c in straightCandidates)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab " + c);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (name.ToLowerInvariant().Contains("spiral")) continue; // skip spiral here
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        log.AppendLine($"  stairsPrefab: RESOLVED STRAIGHT '{prefab.name}' (matched '{c}') @ {path}");
                        resolved.Add("stairsPrefab (straight)");
                        straight = true;
                        return prefab;
                    }
                }
            }
            // Fallback: spiral.
            string[] spiral = { "Stairs_House_Spiral_2m", "Spiral", "Stairs_House", "Stairs" };
            foreach (var c in spiral)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab " + c);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        log.AppendLine($"  stairsPrefab: RESOLVED SPIRAL fallback '{prefab.name}' (matched '{c}') @ {path}");
                        resolved.Add("stairsPrefab (spiral fallback)");
                        straight = false;
                        return prefab;
                    }
                }
            }
            log.AppendLine("  stairsPrefab: MISSING (no straight or spiral stairs).");
            missing.Add("stairsPrefab");
            straight = false;
            return null;
        }

        // Debris resolve — collect a few Apocalypse_M items; empty array is acceptable.
        private static GameObject[] ResolveDebris(StringBuilder log, List<string> resolved)
        {
            string[] names = { "Barrel", "Barrel_Empty", "Blood_A", "Bed_Large_Frame_Broken", "Bath_Broken", "Bed_Broken" };
            var found = new List<GameObject>();
            foreach (var n in names)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab " + n);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    // Prefer Apocalypse_M pack items.
                    if (path.IndexOf("Apocalypse", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !found.Contains(prefab))
                    {
                        found.Add(prefab);
                        log.AppendLine($"  debris: + '{prefab.name}' @ {path}");
                        break; // one per name fragment
                    }
                }
            }
            if (found.Count > 0) resolved.Add($"debrisPrefabs ({found.Count})");
            else log.AppendLine("  debrisPrefabs: none found (Apocalypse_M absent — debris optional, left empty).");
            return found.ToArray();
        }

        // ==================================================================================
        //  UpgradableOutpostBuilder tester (NEW) — builds the Grok outpost (with the added
        //  one-door front gap) into Assets/Scenes/OutpostTest.unity on a flat ground plane,
        //  upgradeLevel=1 (ramparts + stairs), then makes it walkable with the SAME village
        //  hero/camera/light wiring. Hero spawns OUTSIDE the door, facing IN (+Z).
        //    Defenders/Sandbox/Test Outpost
        //    batchmode -executeMethod DeNelle.Editor.CastleBuilderTester.TestBuildOutpost
        // ==================================================================================

        private const string OutpostScenePath = "Assets/Scenes/OutpostTest.unity";

        [MenuItem("Defenders/Sandbox/Test Outpost")]
        public static void TestBuildOutpost()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Test Outpost START ===");

            // 1. Fresh empty scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Host GameObject + component, at origin.
            var hostGo = new GameObject("OutpostTest");
            hostGo.transform.position = Vector3.zero;
            var outpost = hostGo.AddComponent<UpgradableOutpostBuilder>();
            outpost.upgradeLevel = 1; // ramparts + stairs
            log.AppendLine($"  dims: gridWidth={outpost.gridWidth} gridDepth={outpost.gridDepth} " +
                $"unitSize={outpost.unitSize} upgradeLevel={outpost.upgradeLevel}");

            // 3. Resolve prefabs.
            var resolved = new List<string>();
            var missing = new List<string>();

            // wallPrefab = a WOOD wall (wood default).
            outpost.wallPrefab = Resolve("wallPrefab", log, resolved, missing,
                "Wall_Wood", "Wall_Medieval_Wood", "Wall_Wooden", "Wall");
            // platformPrefab = a flat stone floor for the rampart walkway.
            outpost.platformPrefab = Resolve("platformPrefab", log, resolved, missing,
                "Floor_Stone_3x3m_A", "Floor_Stone_3x3m", "Floor_Stone", "Floor_Medieval");
            // towerPrefab = wooden tower first, big medieval tower as fallback.
            outpost.towerPrefab = Resolve("towerPrefab", log, resolved, missing,
                "Tower_Medieval_Wood", "Tower_Wood", "Tower_Medieval_Big", "Tower_Medieval", "Tower");
            // stairsPrefab = a STRAIGHT (walkable) stairs, prefer stone, skip spiral.
            bool straightStairs;
            outpost.stairsPrefab = ResolveStairs(log, resolved, missing, out straightStairs);

            // 4. Flat ground plane (Y=0, collider, NavigationStatic) sized to grid + margin.
            AddOutpostGroundPlane(outpost, log);

            // 5. Build.
            outpost.BuildOutpost();

            // 6. Make it walkable — SAME wiring as CastleWalkable.
            Village2Playable.AddSceneDefaultsToActiveScene();
            Village2Playable.ImportEventSystem();

            GameObject hero = Village2Playable.ImportHero(hostGo.transform, /*heart:*/ null);
            Vector3 heroPos = Vector3.zero;
            if (hero == null)
            {
                log.AppendLine("WARN: ImportHero returned null — hero not built.");
            }
            else
            {
                // The door gap is at the front-center of the bottom row: x == gridWidth/2,
                // z == 0 → world (gridWidth/2 * unitSize, _, 0). Spawn the hero a few metres
                // OUTSIDE the door (-Z), grounded on the plane top (Y=0), facing IN (+Z).
                float doorX = (outpost.gridWidth / 2) * outpost.unitSize;
                heroPos = new Vector3(doorX, 0f, -4f);
                hero.transform.position = heroPos;
                hero.transform.rotation = Quaternion.Euler(0f, 0f, 0f); // face +Z into the outpost

                Village2Playable.WireCameraTargetToHero(hero);
                EnsureHeroControllable(hero, log);
            }

            // 7. Re-bake the NavMesh now that the ground + hero are in place.
            BakeWalkable(scene, log);

            // 8. Count children + save.
            int childCount = hostGo.GetComponentsInChildren<Transform>(true).Length - 1;
            EditorSceneManager.MarkSceneDirty(scene);
            // WO-1731: see the ProScenePath save -- an unsaved post-bake scene ships with no navmesh.
            bool saved = EditorSceneManager.SaveScene(scene, OutpostScenePath);
            if (!saved)
                throw new System.InvalidOperationException(
                    "[CastleBuilderTester] could not save navigation for " + OutpostScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");

            log.AppendLine("---- Prefab resolution ----");
            log.AppendLine("RESOLVED: " + (resolved.Count > 0 ? string.Join(", ", resolved) : "(none)"));
            log.AppendLine("MISSING:  " + (missing.Count > 0 ? string.Join(", ", missing) : "(none)"));
            log.AppendLine("Straight (walkable) stairs found: " + (straightStairs ? "YES" : "NO (spiral/none)"));
            log.AppendLine($"Scene saved={saved} @ {OutpostScenePath}");
            log.AppendLine($"OUTPOST_TEST_OK childCount={childCount} " +
                $"heroAt=({heroPos.x:F2}, {heroPos.y:F2}, {heroPos.z:F2}) " +
                $"dims={outpost.gridWidth}x{outpost.gridDepth} level=1");
            log.AppendLine("=== Test Outpost DONE ===");

            Debug.Log(log.ToString());
        }

        // Flat ground plane at Y=0 covering the outpost footprint (+ margin), collider +
        // NavigationStatic, neutral material. Outpost spans x:[0..(gridWidth-1)*unitSize],
        // z:[0..(gridDepth-1)*unitSize]; extend the plane to cover the door-side approach too.
        private static void AddOutpostGroundPlane(UpgradableOutpostBuilder outpost, StringBuilder log)
        {
            float footprintX = (outpost.gridWidth - 1) * outpost.unitSize;
            float footprintZ = (outpost.gridDepth - 1) * outpost.unitSize;
            const float margin = 16f; // generous: hero spawns ~4m outside the door

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "OutpostGround";
            ground.transform.position = new Vector3(footprintX * 0.5f, 0f, footprintZ * 0.5f);
            ground.transform.localScale = new Vector3((footprintX + margin) / 10f, 1f, (footprintZ + margin) / 10f);
            ground.transform.SetParent(outpost.transform, true);

            var mat = MakeNeutralMaterial();
            if (mat != null)
            {
                var mr = ground.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            var flags = GameObjectUtility.GetStaticEditorFlags(ground);
            GameObjectUtility.SetStaticEditorFlags(ground, flags | StaticEditorFlags.NavigationStatic);

            log.AppendLine($"  ground: OutpostGround plane @ ({ground.transform.position.x:F1},0,{ground.transform.position.z:F1}) " +
                $"covering ~{footprintX + margin:F0}x{footprintZ + margin:F0}m (collider + NavigationStatic).");
        }

        // ==================================================================================
        //  EnemyOutpostBuilder tester (NEW) — builds the Grok ENEMY-outpost generator into
        //  Assets/Scenes/EnemyOutpostTest.unity on a flat ground plane. The variation is
        //  chosen by EditorPref "enemyOutpost.type" (0..3, default 0). Resolves the post-
        //  apocalyptic Apocalypse_M wall/barrier prefabs FIRST, then falls back to our
        //  medieval pack. Then makes it walkable with the SAME village hero/camera/light
        //  wiring CastleWalkable uses; hero spawns just OUTSIDE the outpost, facing IN.
        //    Defenders/Sandbox/Test Enemy Outpost
        //    batchmode -executeMethod DeNelle.Editor.CastleBuilderTester.TestBuildEnemyOutpost
        // ==================================================================================

        private const string EnemyOutpostScenePath = "Assets/Scenes/EnemyOutpostTest.unity";

        [MenuItem("Defenders/Sandbox/Test Enemy Outpost")]
        public static void TestBuildEnemyOutpost()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Test Enemy Outpost START ===");

            // 1. Fresh empty scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Host GameObject + component, at origin.
            var hostGo = new GameObject("EnemyOutpostTest");
            hostGo.transform.position = Vector3.zero;
            var outpost = hostGo.AddComponent<EnemyOutpostBuilder>();

            // Variation from EditorPref (0=Barricade, 1=Ruined, 2=Watchtower, 3=Citadel).
            int type = Mathf.Clamp(EditorPrefs.GetInt("enemyOutpost.type", 0), 0, 3);
            outpost.outpostType = type;
            log.AppendLine($"  outpostType={type} (EditorPref key: enemyOutpost.type) " +
                $"gridWidth={outpost.gridWidth} gridDepth={outpost.gridDepth} unitSize={outpost.unitSize}");

            // 3. Resolve prefabs — Apocalypse_M FIRST, then fall back to our medieval pack.
            var resolved = new List<string>();
            var missing = new List<string>();

            outpost.wallPrefab = Resolve("wallPrefab", log, resolved, missing,
                "Barrier_Concrete", "Barrier", "Wall_Stone_3x3_A", "Wall_Stone");
            outpost.damagedWall = Resolve("damagedWall", log, resolved, missing,
                "Barrier_Concrete_Demaged", "Barrier_Concrete_Damaged", "Barrier_Dam",
                "Wall_Stone_3x3_B", "Wall_Stone_Damaged");
            if (outpost.damagedWall == null && outpost.wallPrefab != null)
            {
                outpost.damagedWall = outpost.wallPrefab;
                missing.Remove("damagedWall");
                resolved.Add("damagedWall (=wallPrefab fallback)");
                log.AppendLine("  damagedWall: FALLBACK to wallPrefab (no damaged variant).");
            }

            // barricadeWindow is OPTIONAL — leave null if nothing matches.
            outpost.barricadeWindow = ResolveOptional("barricadeWindow", log, resolved,
                "Barrier_Window", "Window", "Barricade", "Crenel");

            outpost.platformPrefab = Resolve("platformPrefab", log, resolved, missing,
                "Floor_Stone_3x3m_A", "Floor_Stone_3x3m", "Floor_Stone", "Floor_Medieval");

            // stairsPrefab — only the Citadel variant uses it; prefer straight (walkable).
            bool straightStairs;
            outpost.stairsPrefab = ResolveStairs(log, resolved, missing, out straightStairs);

            // debris — OPTIONAL Apocalypse_M scatter; empty array is fine.
            outpost.debrisPrefabs = ResolveEnemyDebris(log, resolved);

            // 4. Flat ground plane (Y=0, collider, NavigationStatic) sized to grid + margin.
            AddEnemyOutpostGroundPlane(outpost, log);

            // 5. Build.
            outpost.BuildOutpost();

            // 6. Make it walkable — SAME wiring as CastleWalkable.
            Village2Playable.AddSceneDefaultsToActiveScene();
            Village2Playable.ImportEventSystem();

            GameObject hero = Village2Playable.ImportHero(hostGo.transform, /*heart:*/ null);
            Vector3 heroPos = Vector3.zero;
            if (hero == null)
            {
                log.AppendLine("WARN: ImportHero returned null — hero not built.");
            }
            else
            {
                // Spawn just OUTSIDE the front (bottom, z==0) edge, centered on X, facing IN (+Z).
                float frontX = (outpost.gridWidth / 2) * outpost.unitSize;
                heroPos = new Vector3(frontX, 0f, -6f);
                hero.transform.position = heroPos;
                hero.transform.rotation = Quaternion.Euler(0f, 0f, 0f); // face +Z into the outpost

                Village2Playable.WireCameraTargetToHero(hero);
                EnsureHeroControllable(hero, log);
            }

            // 7. Re-bake the NavMesh now the ground + hero are in place.
            BakeWalkable(scene, log);

            // 8. Count children + save.
            int childCount = hostGo.GetComponentsInChildren<Transform>(true).Length - 1;
            EditorSceneManager.MarkSceneDirty(scene);
            // WO-1731: see the ProScenePath save -- an unsaved post-bake scene ships with no navmesh.
            bool saved = EditorSceneManager.SaveScene(scene, EnemyOutpostScenePath);
            if (!saved)
                throw new System.InvalidOperationException(
                    "[CastleBuilderTester] could not save navigation for " + EnemyOutpostScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");

            log.AppendLine("---- Prefab resolution ----");
            log.AppendLine("RESOLVED: " + (resolved.Count > 0 ? string.Join(", ", resolved) : "(none)"));
            log.AppendLine("MISSING:  " + (missing.Count > 0 ? string.Join(", ", missing) : "(none)"));
            log.AppendLine("Straight (walkable) stairs found: " + (straightStairs ? "YES" : "NO (spiral/none)"));
            log.AppendLine($"Scene saved={saved} @ {EnemyOutpostScenePath}");
            log.AppendLine($"ENEMY_OUTPOST_OK type={type} childCount={childCount} " +
                $"heroAt=({heroPos.x:F2}, {heroPos.y:F2}, {heroPos.z:F2}) " +
                $"dims={outpost.gridWidth}x{outpost.gridDepth}");
            log.AppendLine("=== Test Enemy Outpost DONE ===");

            Debug.Log(log.ToString());
        }

        // Per-type batchmode wrappers — set the EditorPref then build, so any variation can
        // be built headless without manually poking EditorPrefs:
        //   batchmode -executeMethod DeNelle.Editor.CastleBuilderTester.TestEnemyOutpost0 .. 3
        [MenuItem("Defenders/Sandbox/Enemy Outpost/Type 0")]
        public static void TestEnemyOutpost0() { EditorPrefs.SetInt("enemyOutpost.type", 0); TestBuildEnemyOutpost(); }

        [MenuItem("Defenders/Sandbox/Enemy Outpost/Type 1")]
        public static void TestEnemyOutpost1() { EditorPrefs.SetInt("enemyOutpost.type", 1); TestBuildEnemyOutpost(); }

        [MenuItem("Defenders/Sandbox/Enemy Outpost/Type 2")]
        public static void TestEnemyOutpost2() { EditorPrefs.SetInt("enemyOutpost.type", 2); TestBuildEnemyOutpost(); }

        [MenuItem("Defenders/Sandbox/Enemy Outpost/Type 3")]
        public static void TestEnemyOutpost3() { EditorPrefs.SetInt("enemyOutpost.type", 3); TestBuildEnemyOutpost(); }

        // Flat ground plane at Y=0 covering the outpost footprint (+ margin), collider +
        // NavigationStatic, neutral material. Outpost spans x:[0..(gridWidth-1)*unitSize],
        // z:[0..(gridDepth-1)*unitSize]; extend to cover the front approach (hero ~6m out).
        private static void AddEnemyOutpostGroundPlane(EnemyOutpostBuilder outpost, StringBuilder log)
        {
            float footprintX = (outpost.gridWidth - 1) * outpost.unitSize;
            float footprintZ = (outpost.gridDepth - 1) * outpost.unitSize;
            const float margin = 20f; // generous: hero spawns ~6m outside the front edge

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "OutpostGround";
            ground.transform.position = new Vector3(footprintX * 0.5f, 0f, footprintZ * 0.5f);
            ground.transform.localScale = new Vector3((footprintX + margin) / 10f, 1f, (footprintZ + margin) / 10f);
            ground.transform.SetParent(outpost.transform, true);

            var mat = MakeNeutralMaterial();
            if (mat != null)
            {
                var mr = ground.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            var flags = GameObjectUtility.GetStaticEditorFlags(ground);
            GameObjectUtility.SetStaticEditorFlags(ground, flags | StaticEditorFlags.NavigationStatic);

            log.AppendLine($"  ground: OutpostGround plane @ ({ground.transform.position.x:F1},0,{ground.transform.position.z:F1}) " +
                $"covering ~{footprintX + margin:F0}x{footprintZ + margin:F0}m (collider + NavigationStatic).");
        }

        // Debris resolve for the enemy outpost — Apocalypse_M scatter items; empty is OK.
        private static GameObject[] ResolveEnemyDebris(StringBuilder log, List<string> resolved)
        {
            string[] names = { "Barrel", "Blood_A", "Blood", "Bed_Large_Frame_Broken", "Bed_Broken", "Bath_Broken", "Bike" };
            var found = new List<GameObject>();
            foreach (var n in names)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab " + n);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.IndexOf("Apocalypse", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !found.Contains(prefab))
                    {
                        found.Add(prefab);
                        log.AppendLine($"  debris: + '{prefab.name}' @ {path}");
                        break; // one per name fragment
                    }
                }
            }
            if (found.Count > 0) resolved.Add($"debrisPrefabs ({found.Count})");
            else log.AppendLine("  debrisPrefabs: none found (Apocalypse_M absent — debris optional, left empty).");
            return found.ToArray();
        }

        // ==================================================================================
        //  DeepDungeonBuilder tester (NEW) — builds the Grok multi-level "Elden Ring style"
        //  deep dungeon into Assets/Scenes/DungeonTest.unity. Resolves KayKit DUNGEON prefabs
        //  FIRST, then falls back to our medieval/stone pack. Builds ALL `levels`, then makes
        //  only LEVEL 0 (top, heightOffset 0) walkable with the SAME village hero/camera/light
        //  wiring CastleWalkable uses. Multi-level descent across the -7 drops is OUT OF SCOPE
        //  (single-surface NavMesh only) — flagged for lift into a real DungeonSceneBuilder.
        //    Defenders/Sandbox/Test Dungeon
        //    batchmode -executeMethod DeNelle.Editor.CastleBuilderTester.TestBuildDungeon
        // ==================================================================================

        private const string DungeonScenePath = "Assets/Scenes/DungeonTest.unity";

        [MenuItem("Defenders/Sandbox/Test Dungeon")]
        public static void TestBuildDungeon()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Test Dungeon START ===");

            // 1. Fresh empty scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Host GameObject + component, at origin.
            var hostGo = new GameObject("DungeonTest");
            hostGo.transform.position = Vector3.zero;
            var dungeon = hostGo.AddComponent<DeepDungeonBuilder>();
            log.AppendLine($"  dims: width={dungeon.width} depth={dungeon.depth} levels={dungeon.levels} unitSize={dungeon.unitSize}");

            // 3. Resolve prefabs — KayKit DUNGEON pack FIRST, then our medieval/stone pack.
            var resolved = new List<string>();
            var missing = new List<string>();

            // floorTile → KayKit dungeon floor, then *Floor*Dungeon*, then our stone floor.
            dungeon.floorTile = Resolve("floorTile", log, resolved, missing,
                "floor_dungeon", "Floor_Dungeon", "Dungeon_Floor", "floor_tile_large",
                "Floor_Stone_3x3m_A", "Floor_Stone_3x3m", "Floor_Stone");

            // wallStraight / barrier → KayKit dungeon wall, then *Wall*Dungeon*, then stone wall.
            dungeon.wallStraight = Resolve("wallStraight", log, resolved, missing,
                "wall_dungeon", "Wall_Dungeon", "Dungeon_Wall", "wall",
                "Wall_Stone_3x3_A", "Wall_Stone");
            // The Grok builder uses `barrier` (not wallStraight) for the actual outer/room walls.
            // Resolve it to the same dungeon-wall family so walls actually spawn.
            dungeon.barrier = Resolve("barrier", log, resolved, missing,
                "wall_dungeon", "Wall_Dungeon", "Dungeon_Wall",
                "Wall_Stone_3x3_A", "Wall_Stone");
            if (dungeon.barrier == null && dungeon.wallStraight != null)
            {
                dungeon.barrier = dungeon.wallStraight;
                missing.Remove("barrier");
                resolved.Add("barrier (=wallStraight fallback)");
                log.AppendLine("  barrier: FALLBACK to wallStraight.");
            }

            // damagedBarrier → KayKit broken dungeon wall, then our Wall_Stone_3x3_B.
            dungeon.damagedBarrier = Resolve("damagedBarrier", log, resolved, missing,
                "wall_dungeon_broken", "Wall_Dungeon_Broken", "wall_broken",
                "Wall_Stone_3x3_B", "Wall_Stone_Damaged");
            if (dungeon.damagedBarrier == null && dungeon.barrier != null)
            {
                dungeon.damagedBarrier = dungeon.barrier;
                missing.Remove("damagedBarrier");
                resolved.Add("damagedBarrier (=barrier fallback)");
                log.AppendLine("  damagedBarrier: FALLBACK to barrier.");
            }

            // wallCorner → corner mesh, else fall back to the straight wall.
            dungeon.wallCorner = Resolve("wallCorner", log, resolved, missing,
                "wall_corner_dungeon", "Wall_Dungeon_Corner", "Wall_Stone_Corner_A", "Corner");
            if (dungeon.wallCorner == null && dungeon.wallStraight != null)
            {
                dungeon.wallCorner = dungeon.wallStraight;
                missing.Remove("wallCorner");
                resolved.Add("wallCorner (=wallStraight fallback)");
                log.AppendLine("  wallCorner: FALLBACK to wallStraight.");
            }

            // archway → a dungeon arch / archway, else a medieval gate.
            dungeon.archway = Resolve("archway", log, resolved, missing,
                "archway", "arch", "Gate_Medieval_Medium", "Gate_Medieval", "Gate");

            // stairsDown → a STRAIGHT walkable stairs, prefer dungeon/stone.
            bool straightStairs;
            dungeon.stairsDown = ResolveStairs(log, resolved, missing, out straightStairs);

            // pillar → any dungeon/ionic pillar (optional-ish; flag missing but harmless).
            dungeon.pillar = Resolve("pillar", log, resolved, missing,
                "pillar_dungeon", "Pillar_Ionic", "Pillar", "Column");

            // debrisPrefabs[] → Apocalypse_M scatter; optional, empty array is fine.
            dungeon.debrisPrefabs = ResolveDebris(log, resolved);

            // healthItem / magicItem → optional small props; skip if nothing matches.
            dungeon.healthItem = ResolveOptional("healthItem", log, resolved,
                "Potion", "HealthPotion", "Health", "Apple", "Bottle");
            dungeon.magicItem = ResolveOptional("magicItem", log, resolved,
                "Crystal", "Gem", "MagicPotion", "Scroll", "Orb");

            // 4. Build ALL levels.
            dungeon.BuildDeepDungeon();

            // 5. Ground plane under LEVEL 0 (top, Y=0) so the hero has a guaranteed walk
            //    surface even if the floor pieces seat slightly off. Sized to the footprint.
            AddDungeonGroundPlane(dungeon, log);

            // 6. Make LEVEL 0 walkable — SAME wiring as CastleWalkable.
            Village2Playable.AddSceneDefaultsToActiveScene();
            Village2Playable.ImportEventSystem();

            GameObject hero = Village2Playable.ImportHero(hostGo.transform, /*heart:*/ null);
            Vector3 heroPos = Vector3.zero;
            if (hero == null)
            {
                log.AppendLine("WARN: ImportHero returned null — hero not built.");
            }
            else
            {
                // Spawn the hero in the centre of level 0's interior, grounded on Y=0.
                float cx = dungeon.width * dungeon.unitSize * 0.5f;
                float cz = dungeon.depth * dungeon.unitSize * 0.5f;
                heroPos = new Vector3(cx, 0f, cz);
                hero.transform.position = heroPos;
                hero.transform.rotation = Quaternion.identity;

                Village2Playable.WireCameraTargetToHero(hero);
                EnsureHeroControllable(hero, log);
            }

            // 7. Bake the NavMesh for LEVEL 0 ONLY (ground plane + Level_0 floor walkable,
            //    Level_0 walls obstacle). Lower levels are excluded from the bake.
            BakeDungeonLevel0Walkable(scene, log);

            // 8. Count children + save.
            int childCount = hostGo.GetComponentsInChildren<Transform>(true).Length - 1;
            EditorSceneManager.MarkSceneDirty(scene);
            // WO-1731: see the ProScenePath save -- an unsaved post-bake scene ships with no navmesh.
            bool saved = EditorSceneManager.SaveScene(scene, DungeonScenePath);
            if (!saved)
                throw new System.InvalidOperationException(
                    "[CastleBuilderTester] could not save navigation for " + DungeonScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");

            log.AppendLine("---- Prefab resolution ----");
            log.AppendLine("RESOLVED: " + (resolved.Count > 0 ? string.Join(", ", resolved) : "(none)"));
            log.AppendLine("MISSING:  " + (missing.Count > 0 ? string.Join(", ", missing) : "(none)"));
            log.AppendLine("Straight (walkable) stairs found: " + (straightStairs ? "YES" : "NO (spiral/none)"));
            log.AppendLine("NOTE: multi-level descent across the -7 drops is OUT OF SCOPE — only " +
                "LEVEL 0 is walkable (single-surface NavMesh). Lift into DungeonSceneBuilder for " +
                "OffMeshLinks / per-level NavMeshSurfaces.");
            log.AppendLine($"Scene saved={saved} @ {DungeonScenePath}");
            log.AppendLine($"DUNGEON_TEST_OK levels={dungeon.levels} childCount={childCount} " +
                $"heroAt=({heroPos.x:F2}, {heroPos.y:F2}, {heroPos.z:F2})");
            log.AppendLine("=== Test Dungeon DONE ===");

            Debug.Log(log.ToString());
        }

        // Flat ground plane at Y=0 covering LEVEL 0's footprint (+ margin), collider +
        // NavigationStatic, neutral material. Level 0 spans x:[0..(width-1)*unitSize],
        // z:[0..(depth-1)*unitSize].
        private static void AddDungeonGroundPlane(DeepDungeonBuilder dungeon, StringBuilder log)
        {
            float footprintX = (dungeon.width - 1) * dungeon.unitSize;
            float footprintZ = (dungeon.depth - 1) * dungeon.unitSize;
            const float margin = 6f;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "DungeonGround";
            ground.transform.position = new Vector3(footprintX * 0.5f, 0f, footprintZ * 0.5f);
            ground.transform.localScale = new Vector3((footprintX + margin) / 10f, 1f, (footprintZ + margin) / 10f);
            ground.transform.SetParent(dungeon.transform, true);

            var mat = MakeNeutralMaterial();
            if (mat != null)
            {
                var mr = ground.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            var flags = GameObjectUtility.GetStaticEditorFlags(ground);
            GameObjectUtility.SetStaticEditorFlags(ground, flags | StaticEditorFlags.NavigationStatic);

            log.AppendLine($"  ground: DungeonGround plane @ ({ground.transform.position.x:F1},0,{ground.transform.position.z:F1}) " +
                $"covering ~{footprintX + margin:F0}x{footprintZ + margin:F0}m (collider + NavigationStatic).");
        }

        // Bake NavMesh for LEVEL 0 ONLY. Marks the ground plane + Level_0 floors walkable and
        // Level_0 walls as obstacles; deliberately ignores Level_1+ so the bake stays on the
        // single top surface (multi-level descent is out of scope — see TestBuildDungeon note).
        private static readonly string[] DungeonWalkable = { "DungeonGround", "floor", "Floor", "Ground" };
        private static readonly string[] DungeonObstacle = { "wall", "Wall", "barrier", "Barrier", "pillar", "Pillar" };

        private static void BakeDungeonLevel0Walkable(Scene scene, StringBuilder log)
        {
            int walkable = 0, obstacle = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;

                    // Only consider the ground plane or pieces under "Level_0". Skip Level_1+.
                    bool isGround = NameOrAncestorContainsAny(r.transform, new[] { "DungeonGround" });
                    bool onLevel0 = NameOrAncestorContainsAny(r.transform, new[] { "Level_0" });
                    if (!isGround && !onLevel0) continue;

                    bool isWalkable = isGround || NameOrAncestorContainsAny(r.transform, DungeonWalkable);
                    bool isObstacle = !isWalkable && NameOrAncestorContainsAny(r.transform, DungeonObstacle);
                    if (!isWalkable && !isObstacle) continue;

                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags | StaticEditorFlags.NavigationStatic);
                    if (isWalkable) walkable++; else obstacle++;
                }
            }
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            log.AppendLine($"  navmesh (LEVEL 0 only) baked: {walkable} walkable + {obstacle} obstacle renderer(s) marked NavigationStatic.");
            if (walkable == 0)
                log.AppendLine("  WARN: no walkable renderers matched on level 0 — navmesh may be empty.");
        }

        /// <summary>
        /// Finds the first prefab whose asset name matches one of the candidate name
        /// fragments (tried in order). Logs the resolved path or marks the field MISSING.
        /// </summary>
        private static GameObject Resolve(string fieldName, StringBuilder log,
            List<string> resolved, List<string> missing, params string[] candidates)
        {
            foreach (var c in candidates)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab " + c);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        log.AppendLine($"  {fieldName}: RESOLVED '{prefab.name}' (matched '{c}') @ {path}");
                        resolved.Add(fieldName);
                        return prefab;
                    }
                }
            }
            log.AppendLine($"  {fieldName}: MISSING (tried: {string.Join(", ", candidates)})");
            missing.Add(fieldName);
            return null;
        }
    }
}
