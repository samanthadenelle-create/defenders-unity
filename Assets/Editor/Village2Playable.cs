// Village2Playable.cs - turn the Village2 ART shell into a playable village by
// adding gameplay components ONE AT A TIME, each step idempotent + verbose so a
// batchmode log confirms it before the next step runs. Owner-directed cadence
// (2026-06-04): "copy components one at a time, methodical is key."
//
// Editor-only (DeNelle.Editor asmdef). Like Village2Build, it CANNOT reference
// Assembly-CSharp (where the runtime gameplay types live), so it adds those
// components by REFLECTION via FindType + AddComponent(System.Type). This is
// build tooling, not a runtime cross-module bridge, so it does not violate the
// "no reflection bridges" rule (same exemption as VillageSceneBuilder).
//
// IMPORTANT: this NEVER touches Village.unity. It only authors Village2.unity.
// Village.unity stays the live, shipping scene until Phase E unhooks it.

using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DeNelle.Editor
{
    public static class Village2Playable
    {
        const string SourceScenePath = "Assets/Scenes/Village2Test.unity";
        const string Village2ScenePath = "Assets/Scenes/Village2.unity";
        const string VillageScenePath  = "Assets/Scenes/Village.unity";

        // Runtime gameplay types (Assembly-CSharp / DeNelle.Village / DeNelle.HUD)
        // resolved by reflection (build tooling exemption, same as VillageSceneBuilder).
        const string TypeHeartController   = "DeNelle.Village.HeartController";
        const string TypeSeatOnGround      = "DeNelle.Village.SeatOnGroundOnStart";
        const string TypeVillageCamera     = "DeNelle.Village.VillageCamera";
        const string TypeSmartMobileCamera = "DeNelle.Village.SmartMobileCamera";
        const string TypeVillageController = "DeNelle.Village.VillageController";
        const string TypeVillageHud        = "DeNelle.HUD.VillageHudController";
        const string TypeEventSystem       = "UnityEngine.EventSystems.EventSystem";
        const string TypeInputUIModule     = "UnityEngine.InputSystem.UI.InputSystemUIInputModule";
        const string VillageHudUxmlPath    = "Assets/_Modules/HUD/VillageHud.uxml";

        // Hero rig types + assets (mirror VillageSceneBuilder.Characters.BuildHero).
        const string TypeHeroBodySwapper   = "DeNelle.Village.HeroBodySwapper";
        const string TypeHeroLocomotion    = "DeNelle.Village.HeroLocomotion";
        const string TypeHeroAbilities     = "DeNelle.Village.HeroAbilities";
        const string TypeHeroAbilityInput  = "DeNelle.Village.HeroAbilityInput";
        const string HeroMeshPath          = "Assets/Resources/Heroes/Mage.fbx";
        const string HeroAnimatorPath      = "Assets/Resources/Heroes/Mage.controller";
        const int    EnemyLayer            = 8;

        // Wave/combat-core types + enemy prefab (B5 — the missing WaveManager that drives
        // waves, the Heart-breach -> Last Stand/Defend-the-Tower choice, and the ATB hand-off).
        const string TypeWaveManager       = "DeNelle.Village.WaveManager";
        const string TypeWaveSpawnPoint    = "DeNelle.Village.WaveSpawnPoint";
        const string TypeEnemy             = "DeNelle.Village.Enemy";
        const string TypeWaveHudBridge     = "DeNelle.Village.WaveHudBridge";
        const string EnemyPrefabPath       = "Assets/Prefabs/Village/Generated/Enemy_HollowWalker.prefab";
        // Village2 footprint (generator townHalfWidth/Depth) -> spawns 12 m outside each gate (DEF-256).
        const float  TownHalfWidth         = 42f;
        const float  TownHalfDepth         = 33f;

        // =====================================================================
        // PHASE A - promote the generated shell to a real, build-registered scene
        // =====================================================================
        [MenuItem("Defenders/Village2/A. Promote Shell To Village2.unity")]
        public static void A_PromoteScene()
        {
            Log("=== PHASE A: Promote shell -> Village2.unity START ===");

            string src = File.Exists(SourceScenePath) ? SourceScenePath
                       : (File.Exists(Village2ScenePath) ? Village2ScenePath : null);
            if (src == null)
            {
                Err($"No source scene found ({SourceScenePath} or {Village2ScenePath}). " +
                    "Run Defenders/Village2/2. Setup + Generate Village2 first. Aborting.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(src, OpenSceneMode.Single);
            Log($"Opened source scene '{src}'. Root objects: {scene.rootCount}");

            GameObject villageRoot = FindRoot(scene, "Village2");
            if (villageRoot == null)
            {
                Err("No 'Village2' root GameObject in the scene - the art shell is missing. Aborting.");
                return;
            }
            Log($"Village2 art root found: {villageRoot.transform.childCount} child groups/objects.");

            // Save AS Village2.unity (does NOT modify the source if source was the Test scene).
            bool ok = EditorSceneManager.SaveScene(scene, Village2ScenePath, /*saveAsCopy:*/ false);
            Log(ok ? $"Saved '{Village2ScenePath}'." : $"FAILED to save '{Village2ScenePath}'.");
            if (!ok) { Err("SaveScene failed. Aborting."); return; }

            // Register Village2 in Build Settings (after Village, keep Village present).
            EnsureInBuildSettings(Village2ScenePath, afterPath: VillageScenePath);

            Log("=== PHASE A DONE - Village2.unity exists, in Build Settings, Village.unity untouched ===");
        }

        // =====================================================================
        // PHASE B0 - scene defaults the generator skipped. Village2 was generated
        // into an EMPTY scene (NewSceneSetup.EmptyScene), so unlike a normal new
        // scene it has NO Main Camera and NO Directional Light -> black Game view +
        // unlit geometry (owner 2026-06-04: "we built on a blank scene, defaults
        // are missing"). This adds them in C#, MIRRORING the live VillageSceneBuilder
        // (Scene.cs CreateCamera / Wiring.cs CreateDirectionalLight +
        // ConfigureSceneLighting) so the Phase D swap into Village.unity carries the
        // SAME smart camera + lighting the live village shipped with ("we used a
        // smart camera"). Idempotent: skips anything already present.
        // =====================================================================
        [MenuItem("Defenders/Village2/B0. Add Scene Defaults (Camera + Light)")]
        public static void B0_AddSceneDefaults()
        {
            Log("=== PHASE B0: Add scene defaults (camera + light + ambient) START ===");
            if (!OpenVillage2(out Scene scene)) return;

            // --- Main Camera (mirror VillageSceneBuilder.Scene.cs CreateCamera) ---
            var existingCam = Object.FindAnyObjectByType<Camera>();
            if (existingCam == null)
            {
                var cameraGo = new GameObject("Main Camera");
                var camera = cameraGo.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.74f, 0.66f, 0.72f); // dawn pink-violet horizon
                camera.farClipPlane = 600f;
                camera.fieldOfView = 60f;
                cameraGo.tag = "MainCamera";
                cameraGo.transform.position = new Vector3(2.8f, 2.6f, 1.9f);
                cameraGo.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
                cameraGo.AddComponent<AudioListener>();
                // The live village uses the adaptive follow camera ("we used a smart
                // camera"). Attach both exactly like live; the hero target is wired in
                // the later gameplay-wiring phase (WO-280). At Play, SmartMobileCamera's
                // EnforceSoleCamera disables VillageCamera and takes over.
                AddByType(cameraGo, TypeVillageCamera);
                AddByType(cameraGo, TypeSmartMobileCamera);
                Log("Added Main Camera (SolidColor dawn tint, FOV 60, far 600, AudioListener, " +
                    "VillageCamera + SmartMobileCamera) — mirrors live village.");
            }
            else Log($"Camera already present ('{existingCam.name}') — idempotent skip.");

            // --- Directional Light (mirror Wiring.cs CreateDirectionalLight, DEF-109) ---
            var existingLight = Object.FindAnyObjectByType<Light>();
            if (existingLight == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.96f, 0.89f);
                light.intensity = 1.55f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.6f;
                lightGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f); // mid-morning sun
                lightGo.transform.position = new Vector3(0f, 40f, 0f);        // off-origin so its gizmo isn't dead-center (directional: position is cosmetic only)
                Log("Added Directional Light (warm mid-morning sun, soft shadows) — mirrors live village.");
            }
            else Log($"Light already present ('{existingLight.name}') — idempotent skip.");

            // --- Gradient ambient + linear fog (mirror Wiring.cs ConfigureSceneLighting) ---
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.52f, 0.50f);
            RenderSettings.ambientGroundColor  = new Color(0.34f, 0.33f, 0.30f);
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.74f, 0.80f);
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance   = 220f;
            Log("Configured gradient ambient + linear fog — matches live village lighting.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village2ScenePath);

            int camCount   = Object.FindObjectsByType<Camera>().Length;
            int lightCount = Object.FindObjectsByType<Light>().Length;
            Log($"VERIFY: cameras={camCount} (expect 1), directional lights={lightCount} (expect >=1). " +
                $"Saved {Village2ScenePath}.");
            Log("=== PHASE B0 DONE — open Village2.unity and the Game view should now render (no longer black) ===");
        }

        // =====================================================================
        // DIAGNOSTIC - report transform.position vs renderer-bounds center for the
        // Village2 root + the tree, so we can see the tripo-displacement reality
        // before anchoring gameplay. Read-only (does not save).
        // =====================================================================
        [MenuItem("Defenders/Village2/0. Inspect Layout")]
        public static void Inspect()
        {
            Log("=== INSPECT Village2 layout START ===");
            if (!OpenVillage2(out Scene scene)) return;

            GameObject root = FindRoot(scene, "Village2");
            if (root != null)
            {
                Log($"Village2 root transform.position = {root.transform.position}");
                if (TryWorldBounds(root, out Bounds rb))
                    Log($"Village2 root RENDERER bounds: center={rb.center} size={rb.size}");
            }

            // Tree: report the clone root transform AND its true rendered center.
            GameObject tree = FindByNameContains(scene, "TreeOfLife");
            if (tree == null) tree = FindByNameContains(scene, "Tree");
            if (tree != null)
            {
                Log($"TREE node '{tree.name}' transform.position = {tree.transform.position}");
                if (TryWorldBounds(tree, out Bounds tb))
                    Log($"TREE RENDERER bounds: center={tb.center} size={tb.size}  <-- this is where it VISUALLY sits");
                else
                    Log("TREE has no renderers?!");
            }

            // Where did B1 put the Heart?
            System.Type heartType = FindType(TypeHeartController);
            if (heartType != null)
            {
                var hearts = Object.FindObjectsByType(heartType);
                foreach (var h in hearts)
                {
                    var mb = h as MonoBehaviour;
                    if (mb != null) Log($"HEART currently on '{mb.gameObject.name}' at {mb.transform.position}");
                }
            }

            // Quick sanity: list the direct children of the Village2 root with bounds centers.
            if (root != null)
            {
                Log("--- direct children (name : renderer-bounds center) ---");
                int n = 0;
                foreach (Transform c in root.transform)
                {
                    if (n++ > 18) { Log("  ...(truncated)"); break; }
                    string bc = TryWorldBounds(c.gameObject, out Bounds cb) ? cb.center.ToString() : "no-rend";
                    Log($"  {c.name} : tf={c.position} bounds={bc}");
                }
            }
            Log("=== INSPECT DONE ===");
        }

        static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            bool have = false;
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (!have) { bounds = r.bounds; have = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return have;
        }

        // =====================================================================
        // PHASE B1 - HeartController onto the Tree of Life at origin
        // (mirrors VillageSceneBuilder.Content.cs heart wiring: authored transform)
        // =====================================================================
        [MenuItem("Defenders/Village2/B1. Wire Heart")]
        public static void B1_WireHeart()
        {
            Log("=== PHASE B1: Wire HeartController START ===");
            if (!OpenVillage2(out Scene scene)) return;

            GameObject root = FindRoot(scene, "Village2");
            if (root == null) { Err("No 'Village2' root. Aborting."); return; }

            // The visible Tree of Life is scaled up ~14x (ScaleToHeight). Putting the
            // HeartController on it would make Awake()'s auto blocker capsule (r=2,h=8
            // LOCAL) a ~28m plaza-blocking capsule -> heart-collider-scale-trap. So we
            // host the Heart on a CLEAN, scale-1 child anchor at the tree's x/z (origin),
            // leaving the scaled tree purely decorative. Same gameplay anchor (lose
            // condition + enemy target at village centre), no giant collider.
            GameObject tree = FindByNameContains(scene, "TreeOfLife");
            if (tree == null) tree = FindByNameContains(scene, "Tree");
            Vector3 centre = tree != null
                ? new Vector3(tree.transform.position.x, 0f, tree.transform.position.z)
                : Vector3.zero;
            Log(tree != null
                ? $"Tree found '{tree.name}' at {tree.transform.position} scale {tree.transform.localScale}; Heart anchor x/z from it."
                : "Tree not found; defaulting Heart anchor to origin.");

            // The visible centerpiece (SM_Tree_Round(Clone), scale ~2.769×) FLOATS:
            // its mesh pivot sits above the model base, so at that scale the rendered
            // mesh lifts off the ground at raw y=0. Attach SeatOnGroundOnStart so at
            // Play it measures its live renderer bounds and drops the bottom onto the
            // ground — robust to scale/pivot, no scene re-save needed. Idempotent (the
            // component is [DisallowMultipleComponent]); re-running B1 / a Village2
            // rebuild re-attaches it. Attached to the SCALED visible tree, not the
            // clean scale-1 Heart anchor below.
            if (tree != null)
            {
                if (AddByType(tree, TypeSeatOnGround) != null)
                    Log($"Attached SeatOnGroundOnStart to centerpiece '{tree.name}' (fixes floating tree at Play).");
                else
                    Warn("SeatOnGroundOnStart type not found (is DeNelle.Village compiled?) — centerpiece may still float.");
            }

            System.Type heartType = FindType(TypeHeartController);
            if (heartType == null)
            {
                Err($"Type '{TypeHeartController}' not found (is DeNelle.Village compiled?). Aborting.");
                return;
            }

            const string AnchorName = "HeartOfElarion";
            Transform existingAnchor = root.transform.Find(AnchorName);
            GameObject anchor = existingAnchor != null ? existingAnchor.gameObject : new GameObject(AnchorName);
            if (existingAnchor == null)
            {
                anchor.transform.SetParent(root.transform, false);
                Log($"Created '{AnchorName}' anchor under Village2 root.");
            }
            anchor.transform.position = centre;          // village centre, on the ground
            anchor.transform.localScale = Vector3.one;   // clean scale -> clean blocker

            if (anchor.GetComponent(heartType) == null)
            {
                var heart = anchor.AddComponent(heartType);
                var so = new SerializedObject(heart);
                var prop = so.FindProperty("_useAuthoredTransform");
                if (prop != null) { prop.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Warn("HeartController._useAuthoredTransform not found - Elarion may snap to origin at Play.");
                Log($"HeartController added on '{AnchorName}' at {centre} (scale 1).");
            }
            else Log("HeartController already on the anchor - idempotent skip.");

            EditorSceneManager.SaveScene(scene, Village2ScenePath);
            var hearts = Object.FindObjectsByType(heartType);
            Log($"VERIFY: HeartController instances = {hearts.Length} (expect 1).");
            foreach (var h in hearts)
            {
                var mb = h as MonoBehaviour;
                if (mb != null) Log($"VERIFY: Heart on '{mb.gameObject.name}' at {mb.transform.position} scale {mb.transform.lossyScale}.");
            }
            Log("=== PHASE B1 DONE ===");
        }

        // =====================================================================
        // PHASE B2 - core systems: EventSystem (UI input routing) + VillageController
        // (the orchestrator). The Import* methods below are GENERIC, scene-agnostic
        // component importers — the menu phase just wraps them (open scene -> import
        // -> save). The SAME methods are what a future CityFactory calls to wire a
        // city from a layout/recipe ("pass the layout -> new city, already wired").
        // =====================================================================
        [MenuItem("Defenders/Village2/B2. Wire Core Systems (EventSystem + Controller)")]
        public static void B2_WireCoreSystems()
        {
            Log("=== PHASE B2: Wire core systems START ===");
            if (!OpenVillage2(out Scene scene)) return;
            GameObject root = FindRoot(scene, "Village2");
            if (root == null) { Err("No 'Village2' root. Aborting."); return; }

            ImportEventSystem();
            ImportVillageController(root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village2ScenePath);
            Log("=== PHASE B2 DONE ===");
        }

        /// Generic importer: ensures ONE EventSystem (+ the new-Input-System UI module)
        /// in the active scene. Without it, UI Toolkit button clicks are dead.
        /// Idempotent. Mirrors VillageSceneBuilder.EnsureEventSystem.
        public static void ImportEventSystem()
        {
            var esType = FindType(TypeEventSystem);
            if (esType == null) { Warn("EventSystem type not resolvable — UI clicks will be dead."); return; }
            if (Object.FindAnyObjectByType(esType) != null) { Log("EventSystem already present — idempotent skip."); return; }

            var go = new GameObject("EventSystem");
            go.AddComponent(esType);
            var moduleType = FindType(TypeInputUIModule);
            if (moduleType != null) go.AddComponent(moduleType);
            else Warn("InputSystemUIInputModule not resolvable — falling back to EventSystem-only routing.");
            Log("Imported EventSystem (+ InputSystemUIInputModule).");
        }

        /// Generic importer: the VillageController orchestrator under the city root,
        /// with `_heart` wired to the HeartController (lose condition). Wall/gate/
        /// building roots are left for the mesh->role mapping pass (wiring a wrong
        /// group is worse than null; the controller null-guards them). Idempotent.
        public static Component ImportVillageController(Transform cityRoot)
        {
            var ctrlType = FindType(TypeVillageController);
            if (ctrlType == null) { Err("VillageController type not found (is DeNelle.Village compiled?)."); return null; }

            Transform existing = cityRoot.Find("VillageController");
            GameObject go = existing != null ? existing.gameObject : new GameObject("VillageController");
            if (existing == null) go.transform.SetParent(cityRoot, false);
            Component controller = go.GetComponent(ctrlType) ?? go.AddComponent(ctrlType);

            var so = new SerializedObject(controller);
            var heartType = FindType(TypeHeartController);
            Component heart = heartType != null ? Object.FindAnyObjectByType(heartType) as Component : null;
            if (heart != null) SetObjectField(so, "_heart", heart);
            else Warn("No HeartController in scene — run B1 (Wire Heart) first; controller _heart left null.");
            so.ApplyModifiedPropertiesWithoutUndo();

            Log($"Imported VillageController on '{go.name}' (_heart {(heart != null ? "wired" : "NULL")}).");
            return controller;
        }

        // =====================================================================
        // PHASE B3 - the HUD (VillageHudController on its own UIDocument). The HUD
        // hosts the wave timer, build button, ability bar + repair prompt. Bridges
        // (build/ability/wave) are wired in later phases once their partners exist
        // (BuildMenu / hero / WaveManager). Mirrors WallRepairSceneSetup.BuildVillageHud.
        // =====================================================================
        [MenuItem("Defenders/Village2/B3. Wire HUD (VillageHudController)")]
        public static void B3_WireHud()
        {
            Log("=== PHASE B3: Wire HUD START ===");
            if (!OpenVillage2(out Scene scene)) return;
            GameObject root = FindRoot(scene, "Village2");
            if (root == null) { Err("No 'Village2' root. Aborting."); return; }

            ImportVillageHud(root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village2ScenePath);
            Log("=== PHASE B3 DONE ===");
        }

        /// Generic importer: a "VillageHud" GameObject = UIDocument (VillageHud.uxml +
        /// a PanelSettings) + VillageHudController with `_document` wired. Idempotent:
        /// reuses an existing HUD. Mirrors WallRepairSceneSetup.BuildVillageHud.
        public static Component ImportVillageHud(Transform cityRoot)
        {
            var hudType = FindType(TypeVillageHud);
            if (hudType == null) { Err("VillageHudController not found (is DeNelle.HUD compiled?)."); return null; }
            var existing = Object.FindAnyObjectByType(hudType) as Component;
            if (existing != null) { Log("VillageHudController already present — idempotent skip."); return existing; }

            var go = new GameObject("VillageHud");
            go.transform.SetParent(cityRoot, false);

            var uiDoc = go.AddComponent<UIDocument>();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(VillageHudUxmlPath);
            if (uxml != null) uiDoc.visualTreeAsset = uxml;
            else Warn($"VillageHud.uxml not found at '{VillageHudUxmlPath}' — assign the HUD source by hand.");
            WirePanelSettings(uiDoc);

            Component hud = go.AddComponent(hudType);
            var so = new SerializedObject(hud);
            SetObjectField(so, "_document", uiDoc);
            so.ApplyModifiedPropertiesWithoutUndo();
            Log("Imported VillageHud (UIDocument + VillageHudController).");
            return hud;
        }

        // =====================================================================
        // PHASE B4 - the hero rig (keystone: the smart camera needs a target and you
        // can't walk/test without it). Mirrors VillageSceneBuilder.Characters.BuildHero:
        // People Mage body (Resources/Heroes/Mage.fbx) + Mage.controller, then
        // HeroBodySwapper/Locomotion/Abilities/AbilityInput, _heart + _enemyMask wired,
        // then the camera's VillageCamera + SmartMobileCamera _target -> the hero.
        // =====================================================================
        [MenuItem("Defenders/Village2/B4. Build Hero + Camera Target")]
        public static void B4_BuildHero()
        {
            Log("=== PHASE B4: Build hero + wire camera target START ===");
            if (!OpenVillage2(out Scene scene)) return;
            GameObject root = FindRoot(scene, "Village2") ?? FindRoot(scene, "StrongholdRoot");
            if (root == null) { Err("No 'Village2' or 'StrongholdRoot' root. Aborting."); return; }

            var heartType = FindType(TypeHeartController);
            Component heart = heartType != null ? Object.FindAnyObjectByType(heartType) as Component : null;

            GameObject hero = ImportHero(root.transform, heart);
            WireCameraTargetToHero(hero);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village2ScenePath);
            Log($"VERIFY: hero='{(hero != null ? hero.name : "NULL")}' tag={(hero != null ? hero.tag : "?")}.");
            Log("=== PHASE B4 DONE ===");
        }

        /// Generic importer: the player hero rig under the city root. Idempotent —
        /// reuses an existing "Player"-tagged hero. Mirrors BuildHero.
        public static GameObject ImportHero(Transform cityRoot, Component heart)
        {
            var existing = GameObject.FindWithTag("Player");
            if (existing != null) { Log("Hero ('Player' tag) already present — idempotent skip."); return existing; }

            var go = new GameObject("Hero (Blaise)");
            go.transform.SetParent(cityRoot, false);
            go.tag = "Player";
            // The old spawn (6,0,4) is only ~7m from the (0,0,0) centerpiece — INSIDE
            // its scaled footprint, so the hero spawned embedded in the tree. Spawn OUT
            // past the centerpiece radius on the open +Z road toward the N gate, facing
            // back at the centerpiece. Drive the distance from the centerpiece's actual
            // world-bounds radius (robust to its ~2.769× scale) + a hero clearance pad;
            // fall back to a safe constant if no centerpiece/renderers are found.
            go.transform.position = ComputeHeroSpawn(cityRoot);   // open road toward N gate, clear of centerpiece

            var collider = go.AddComponent<CapsuleCollider>();
            collider.height = 2f;
            collider.radius = 0.4f;
            collider.center = new Vector3(0f, 1f, 0f);

            var heroModel = LoadModel(HeroMeshPath);
            GameObject body;
            if (heroModel != null)
                body = (GameObject)PrefabUtility.InstantiatePrefab(heroModel);
            else
            {
                Warn($"Hero mesh '{HeroMeshPath}' not found — capsule placeholder.");
                body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            }
            body.name = "HeroBody";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = Vector3.zero;
            if (heroModel != null) NormalizeToHeight(body, 2.0f);
            StripColliders(body);     // hero-root capsule is the sole collision source
            StripRigidbodies(body);   // a stray Rigidbody dropped the hero through the floor

            if (heroModel != null)
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HeroAnimatorPath);
                var anim = body.GetComponentInChildren<Animator>();
                if (ctrl != null && anim != null) { anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; }
                else if (ctrl == null) Warn($"Mage.controller not found at '{HeroAnimatorPath}' — hero won't animate.");
            }

            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // spawned on +Z road, face -Z back toward the centerpiece

            AddByType(go, TypeHeroBodySwapper);   // swaps to the chosen class body at runtime
            AddByType(go, TypeHeroLocomotion);    // WASD / stick movement
            var abilities = AddByType(go, TypeHeroAbilities);
            AddByType(go, TypeHeroAbilityInput);  // 1/2/3/4 -> TryCast

            if (abilities != null)
            {
                var so = new SerializedObject(abilities);
                if (heart != null) SetObjectField(so, "_heart", heart);       // Healing Beacon target
                SetLayerMaskField(so, "_enemyMask", 1 << EnemyLayer);          // ability hit-tests the Enemy layer
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Log($"Imported hero rig '{go.name}' at {go.transform.position} " +
                $"(body={(heroModel != null ? "Mage.fbx" : "placeholder")}).");
            return go;
        }

        // Hero spawn on the open +Z road toward the N gate, just OUTSIDE the
        // centerpiece's footprint so the hero never spawns embedded in the tree.
        // Bounds-driven: spawnZ = centerpiece world-bounds radius (max of its X/Z
        // extents) + a hero clearance pad. Robust to the centerpiece's ~2.769× scale
        // and any art swap. Falls back to a safe constant (0,0,11) if the centerpiece
        // or its renderers can't be found. The N spawn stays inside the town
        // (TownHalfDepth=33), so it's on the plaza road, not out a gate.
        static Vector3 ComputeHeroSpawn(Transform cityRoot)
        {
            const float heroPad = 3f;          // clearance past the centerpiece edge
            const float fallbackZ = 11f;       // safe constant if bounds unavailable

            GameObject tree = null;
            if (cityRoot != null)
            {
                foreach (var t in cityRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.Contains("TreeOfLife")) { tree = t.gameObject; break; }
                    if (tree == null && t.name.Contains("Tree")) tree = t.gameObject;
                }
            }

            if (tree != null && TryWorldBounds(tree, out Bounds tb))
            {
                float radius = Mathf.Max(tb.extents.x, tb.extents.z);
                float z = Mathf.Max(fallbackZ, radius + heroPad);
                // Keep inside the town footprint (don't spawn out past the N gate).
                z = Mathf.Min(z, TownHalfDepth - 2f);
                Log($"Hero spawn: centerpiece bounds radius={radius:F2} -> spawn z={z:F2} (bounds-driven).");
                return new Vector3(0f, 0f, z);
            }

            Log($"Hero spawn: no centerpiece bounds found -> safe constant (0,0,{fallbackZ}).");
            return new Vector3(0f, 0f, fallbackZ);
        }

        // =====================================================================
        // SCENE-AGNOSTIC scene defaults (camera + light + ambient/fog) — the SAME
        // body as B0_AddSceneDefaults, but operating on the ACTIVE scene without the
        // Village2.unity path coupling, so a different builder (Village3) can wire an
        // identical shell. B0_AddSceneDefaults is UNTOUCHED; this is additive.
        // =====================================================================
        public static void AddSceneDefaultsToActiveScene()
        {
            var existingCam = Object.FindAnyObjectByType<Camera>();
            if (existingCam == null)
            {
                var cameraGo = new GameObject("Main Camera");
                var camera = cameraGo.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.74f, 0.66f, 0.72f);
                camera.farClipPlane = 600f;
                camera.fieldOfView = 60f;
                cameraGo.tag = "MainCamera";
                cameraGo.transform.position = new Vector3(2.8f, 2.6f, 1.9f);
                cameraGo.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
                cameraGo.AddComponent<AudioListener>();
                AddByType(cameraGo, TypeVillageCamera);
                AddByType(cameraGo, TypeSmartMobileCamera);
                Log("AddSceneDefaults: Main Camera (VillageCamera + SmartMobileCamera) added.");
            }
            else Log($"AddSceneDefaults: camera already present ('{existingCam.name}') — idempotent skip.");

            var existingLight = Object.FindAnyObjectByType<Light>();
            if (existingLight == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.96f, 0.89f);
                light.intensity = 1.55f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.6f;
                lightGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                lightGo.transform.position = new Vector3(0f, 40f, 0f);
                Log("AddSceneDefaults: Directional Light added.");
            }
            else Log($"AddSceneDefaults: light already present ('{existingLight.name}') — idempotent skip.");

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.52f, 0.50f);
            RenderSettings.ambientGroundColor  = new Color(0.34f, 0.33f, 0.30f);
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.74f, 0.80f);
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance   = 220f;
            Log("AddSceneDefaults: gradient ambient + linear fog configured.");
        }

        /// Wires the Main Camera's VillageCamera + SmartMobileCamera `_target` to the
        /// hero so the adaptive follow camera tracks it. Mirrors WireVillageCameraTarget.
        public static void WireCameraTargetToHero(GameObject hero)
        {
            if (hero == null) return;
            var cam = Camera.main;
            if (cam == null) { Warn("No Camera.main — run B0 first; camera target not wired."); return; }
            foreach (var typeName in new[] { TypeVillageCamera, TypeSmartMobileCamera })
            {
                var t = FindType(typeName);
                if (t == null) continue;
                var follow = cam.GetComponent(t);
                if (follow == null) continue;
                var so = new SerializedObject(follow);
                SetObjectField(so, "_target", hero.transform);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Log("Wired camera _target -> hero (VillageCamera + SmartMobileCamera).");
        }

        // =====================================================================
        // PHASE B5 - the COMBAT CORE: WaveSpawnPoints (12 m outside each gate) +
        // WaveManager (wired to Heart + enemy prefab) + WaveHudBridge. Village2 had
        // the HUD START WAVE button but NO WaveManager behind it, so: no wave ran, the
        // Heart died undefended ("boom tree died, game over"), and the Heart-breach
        // choice (Last Stand / Defend the Tower) + ATB hand-off — all owned by
        // WaveManager — never fired. Mirrors VillageSceneBuilder.Systems.BuildWaveManager
        // + Dressing spawn points. Needs the Phase C combined navmesh bake so enemies path.
        // =====================================================================
        [MenuItem("Defenders/Village2/B5. Wire Waves (WaveManager + spawns)")]
        public static void B5_WireWaves()
        {
            Log("=== PHASE B5: Wire waves (combat core) START ===");
            if (!OpenVillage2(out Scene scene)) return;
            GameObject root = FindRoot(scene, "Village2") ?? FindRoot(scene, "StrongholdRoot");
            if (root == null) { Err("No 'Village2' or 'StrongholdRoot' root. Aborting."); return; }

            var heartType = FindType(TypeHeartController);
            Component heart = heartType != null ? Object.FindAnyObjectByType(heartType) as Component : null;

            ImportSpawnPoints(root.transform);
            ImportWaveManager(root.transform, heart);
            WireWaveHud();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village2ScenePath);
            Log("=== PHASE B5 DONE — needs Phase C bake so enemies path spawn->gate->Heart ===");
        }

        // 4 WaveSpawnPoints, 12 m OUTSIDE each gate (enemies spawn here + path through the gate
        // to the Heart). Derived from the town footprint (DEF-256: 42/33 -> N/S ±45, E/W ±54).
        public static void ImportSpawnPoints(Transform cityRoot)
        {
            var spType = FindType(TypeWaveSpawnPoint);
            if (spType == null) { Err("WaveSpawnPoint type not found (DeNelle.Village compiled?)."); return; }

            (string dir, Vector3 gate, Vector3 spawn)[] gates =
            {
                ("N", new Vector3(0f, 0f,  TownHalfDepth), new Vector3(0f, 0f,  TownHalfDepth + 12f)),
                ("S", new Vector3(0f, 0f, -TownHalfDepth), new Vector3(0f, 0f, -TownHalfDepth - 12f)),
                ("E", new Vector3( TownHalfWidth, 0f, 0f), new Vector3( TownHalfWidth + 12f, 0f, 0f)),
                ("W", new Vector3(-TownHalfWidth, 0f, 0f), new Vector3(-TownHalfWidth - 12f, 0f, 0f)),
            };
            int i = 0, made = 0;
            foreach (var g in gates)
            {
                string name = $"WaveSpawnPoint-{g.dir}";
                Transform existing = cityRoot.Find(name);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                var go = new GameObject(name);
                go.transform.SetParent(cityRoot, false);
                go.transform.position = g.spawn;
                try { go.tag = "SpawnPoint"; } catch { Warn("'SpawnPoint' tag missing in TagManager — spawn left untagged."); }
                var sp = go.AddComponent(spType);
                InvokeMethod(sp, "Configure", $"spawn-{i}", i, g.dir, g.gate);   // (spawnId, gateIndex, direction, gatePosition)
                made++; i++;
            }
            Log($"Imported {made} WaveSpawnPoints (12 m outside N/S/E/W gates).");
        }

        public static Component ImportWaveManager(Transform cityRoot, Component heart)
        {
            var wmType = FindType(TypeWaveManager);
            if (wmType == null) { Err("WaveManager type not found (DeNelle.Village compiled?)."); return null; }

            Transform existing = cityRoot.Find("WaveManager");
            GameObject go = existing != null ? existing.gameObject : new GameObject("WaveManager");
            if (existing == null) go.transform.SetParent(cityRoot, false);
            Component wm = go.GetComponent(wmType) ?? go.AddComponent(wmType);

            var so = new SerializedObject(wm);
            if (heart != null) SetObjectField(so, "_heart", heart);
            // DEFEND-GATED START: the wave must NOT pre-spawn / pre-countdown at load.
            // Force _autoStart OFF on the wired scene so the manager sits Idle (zero
            // enemies) until the player presses the HUD DEFEND button (StartWaveHudBridge
            // -> WaveManager.ForceBeginNextWave). Set explicitly here so a previously-baked
            // scene that serialized _autoStart=true is corrected on the next B5 wiring pass.
            var autoStartProp = so.FindProperty("_autoStart");
            if (autoStartProp != null) autoStartProp.boolValue = false;
            Transform er = cityRoot.Find("WaveEnemies");
            if (er == null) { var erGo = new GameObject("WaveEnemies"); erGo.transform.SetParent(cityRoot, false); er = erGo.transform; }
            SetObjectField(so, "_enemyRoot", er);

            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            var enemyType = FindType(TypeEnemy);
            if (enemyPrefab != null && enemyType != null)
            {
                var ec = enemyPrefab.GetComponent(enemyType);
                if (ec != null) SetObjectField(so, "_enemyPrefab", ec);
                else Warn($"Enemy prefab '{EnemyPrefabPath}' has no Enemy component — waves spawn nothing.");
            }
            else Warn($"Enemy prefab not found at '{EnemyPrefabPath}' — waves spawn nothing until set.");
            so.ApplyModifiedPropertiesWithoutUndo();

            Log("Imported WaveManager (_heart + _enemyRoot + _enemyPrefab wired; spawn points auto-found at Start).");
            return wm;
        }

        // WaveManager -> VillageHudController via WaveHudBridge (wave timer + START WAVE actually run).
        static void WireWaveHud()
        {
            var wmType = FindType(TypeWaveManager);
            var hudType = FindType(TypeVillageHud);
            var bridgeType = FindType(TypeWaveHudBridge);
            if (wmType == null || hudType == null || bridgeType == null) { Warn("WaveHudBridge wiring skipped — a type was missing."); return; }
            var wm = Object.FindAnyObjectByType(wmType) as Component;
            if (wm == null) { Warn("WaveHudBridge wiring skipped — WaveManager missing."); return; }
            var bridge = wm.GetComponent(bridgeType) ?? wm.gameObject.AddComponent(bridgeType);
            var so = new SerializedObject(bridge);
            SetObjectField(so, "_wave", wm);   // HUD resolved at runtime via CoreServices.Hud — no _hud field
            so.ApplyModifiedPropertiesWithoutUndo();
            _ = hudType;
            Log("Wired WaveHudBridge (_wave; HUD via CoreServices.Hud at runtime).");
        }

        // Invokes a public method by name (matched on arg count) via reflection. Mirrors InvokeConfigure.
        static void InvokeMethod(Component target, string method, params object[] args)
        {
            if (target == null) return;
            foreach (var m in target.GetType().GetMethods())
            {
                if (m.Name != method || m.GetParameters().Length != args.Length) continue;
                try { m.Invoke(target, args); } catch (System.Exception e) { Warn($"InvokeMethod '{method}' threw: {e.Message}"); }
                return;
            }
            Warn($"Method '{method}' (x{args.Length} args) not found on {target.GetType().Name}.");
        }

        // =====================================================================
        // ORCHESTRATOR - "pass the layout -> wired city". Runs every gameplay phase in
        // order against the already-generated Village2 shell. THIS is the factory proof:
        // open scene -> run the generic-component importers in sequence -> a playable
        // city. Each phase is idempotent + saves, so re-running is safe.
        // =====================================================================
        // =====================================================================
        // REBUILD ALL — the owner's "fix in script, simply rerun" entry point. ONE
        // command: regenerate the shell from the (fixed) generator -> promote ->
        // wire the playable city. Never hand-edit the scene; change the generator/
        // importers and re-run this. (This is the dev/build-time path; clear-rebuild
        // is deterministic — the runtime player-camp path is the idempotent replay.)
        // =====================================================================
        [MenuItem("Defenders/Village2/REBUILD ALL (generate + promote + wire)")]
        public static void RebuildAll()
        {
            Log("=== REBUILD ALL: regenerate shell -> promote -> wire playable city ===");
            // Fresh empty scene so the generator builds clean (it saves the active scene as Village2Test).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Village2Build.SetupAndGenerateVillage2();   // regenerate Village2Test from the fixed generator
            A_PromoteScene();                            // Village2Test -> Village2.unity
            B_BuildPlayableCity();                       // B0..B4 gameplay wiring
            Log("=== REBUILD ALL DONE — open Village2.unity and press Play ===");
        }

        [MenuItem("Defenders/Village2/B. Build Playable City (all phases)")]
        public static void B_BuildPlayableCity()
        {
            Log("=== BUILD PLAYABLE CITY: running all gameplay phases ===");
            B0_AddSceneDefaults();
            B1_WireHeart();
            B2_WireCoreSystems();
            B3_WireHud();
            B4_BuildHero();
            B5_WireWaves();
            Log("=== BUILD PLAYABLE CITY DONE — open Village2.unity and press Play ===");
        }

        // ── Mesh helpers (mirror VillageSceneBuilder.Helpers) ────────────────
        static GameObject LoadModel(string assetPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model != null) return model;
            string asPrefab = Path.ChangeExtension(assetPath, ".prefab")?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(asPrefab))
                model = AssetDatabase.LoadAssetAtPath<GameObject>(asPrefab);
            return model;
        }

        static void NormalizeToHeight(GameObject go, float target)
        {
            if (go == null || target <= 0.001f) return;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float maxExtent = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (maxExtent < 0.0001f) return;
            float factor = Mathf.Clamp(target / maxExtent, 0.05f, 40f);
            go.transform.localScale *= factor;
        }

        static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        static void StripRigidbodies(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(r);
        }

        static void SetLayerMaskField(SerializedObject so, string field, int mask)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Warn($"LayerMask field '{field}' not found on {so.targetObject.GetType().Name} — not set.");
                return;
            }
            prop.intValue = mask;
        }

        // =====================================================================
        // CAPTURE-AUTHORED PLACEMENT (owner 2026-06-04: "spatial awareness is the one
        // part AI gets wrong — let me correct the Z in-editor and capture the exact
        // position so it always matches"). YOU position/correct pieces by hand in the
        // scene; Capture records their EXACT prefab + world transform to a JSON recipe;
        // Replay re-instantiates them verbatim. Human does the spatial dial ONCE → the
        // recipe is canonical → the factory replays it (every city, player camps).
        // For PRECISE pieces (gate↔wall fit, building anchors); procedural scatter
        // (nature/props) stays in the generator.
        // =====================================================================
        const string RecipePath = "Assets/_Village2/Village2PlacementRecipe.json";

        [System.Serializable]
        class PlacementEntry { public string prefab; public Vector3 pos; public Vector3 euler; public Vector3 scale; }
        [System.Serializable]
        class PlacementRecipe { public System.Collections.Generic.List<PlacementEntry> entries = new System.Collections.Generic.List<PlacementEntry>(); }

        [MenuItem("Defenders/Village2/Capture Selected -> Recipe")]
        public static void CaptureSelectedToRecipe()
        {
            var sel = Selection.gameObjects;
            if (sel == null || sel.Length == 0) { Warn("Capture: nothing selected. Select the pieces you positioned, then Capture."); return; }

            var recipe = LoadRecipe();
            int added = 0, skipped = 0;
            foreach (var go in sel)
            {
                string prefabPath = ResolvePrefabPath(go);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    Warn($"Capture: '{go.name}' has no prefab source — skipped (select a prefab instance, not a plain GameObject).");
                    skipped++; continue;
                }
                var t = go.transform;
                recipe.entries.Add(new PlacementEntry { prefab = prefabPath, pos = t.position, euler = t.eulerAngles, scale = t.localScale });
                added++;
            }
            SaveRecipe(recipe);
            Log($"Capture: +{added} placement(s) (skipped {skipped}) -> {RecipePath}. Total now {recipe.entries.Count}.");
        }

        [MenuItem("Defenders/Village2/Replay Recipe Into Scene")]
        public static void ReplayRecipeIntoScene()
        {
            var recipe = LoadRecipe();
            if (recipe.entries.Count == 0) { Warn($"Replay: recipe is empty ({RecipePath})."); return; }
            if (!OpenVillage2(out Scene scene)) return;
            GameObject root = FindRoot(scene, "Village2");

            var holderExisting = root != null ? root.transform.Find("CapturedPlacements") : null;
            if (holderExisting != null) Object.DestroyImmediate(holderExisting.gameObject);   // idempotent re-replay
            var holder = new GameObject("CapturedPlacements").transform;
            if (root != null) holder.SetParent(root.transform, false);

            int placed = 0;
            foreach (var e in recipe.entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefab);
                if (prefab == null) { Warn($"Replay: prefab not found '{e.prefab}' — skipped."); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
                go.transform.position = e.pos;
                go.transform.eulerAngles = e.euler;
                go.transform.localScale = e.scale;
                placed++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village2ScenePath);
            Log($"Replay: instantiated {placed}/{recipe.entries.Count} captured placement(s) under 'CapturedPlacements' — exact transforms.");
        }

        [MenuItem("Defenders/Village2/Clear Placement Recipe")]
        public static void ClearPlacementRecipe()
        {
            SaveRecipe(new PlacementRecipe());
            Log($"Cleared placement recipe: {RecipePath}.");
        }

        static string ResolvePrefabPath(GameObject go)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return src != null ? AssetDatabase.GetAssetPath(src) : null;
        }

        static PlacementRecipe LoadRecipe()
        {
            if (File.Exists(RecipePath))
            {
                try
                {
                    var r = JsonUtility.FromJson<PlacementRecipe>(File.ReadAllText(RecipePath));
                    if (r != null) { if (r.entries == null) r.entries = new System.Collections.Generic.List<PlacementEntry>(); return r; }
                }
                catch (System.Exception e) { Warn($"Recipe parse failed ({e.Message}) — starting fresh."); }
            }
            return new PlacementRecipe();
        }

        static void SaveRecipe(PlacementRecipe recipe)
        {
            File.WriteAllText(RecipePath, JsonUtility.ToJson(recipe, true));
            AssetDatabase.Refresh();
        }

        // =====================================================================
        // PHASE C - combined Village2 + OuterWorld navmesh (the walled-in risk)
        // Mirrors OuterWorldBuilder.BakeWorldNavMesh but for Village2: flags
        // Village2 walls/houses/towers NavigationStatic as OBSTACLES, keeps gate
        // arches + roads walkable, levels the OuterWorld terrain to Y=0 flush, and
        // bakes ONE surface so the hero walks Village2 <-> terrain through the gates.
        // =====================================================================
        const string OuterWorldScenePath = "Assets/Scenes/OuterWorld.unity";

        // Names whose renderers must STAY walkable (NOT flagged obstacle): the gate
        // arch openings + the ground roads. Everything else under the village root
        // (walls, ramparts, corner towers, houses, the tree) becomes an obstacle.
        static readonly string[] KeepWalkable = { "Wall_Arch", "Gate_Medieval", "Floor_Brick" };

        [MenuItem("Defenders/Village2/C. Bake Combined NavMesh")]
        public static void C_BakeNavMesh()
        {
            Log("=== PHASE C: Bake combined Village2 + OuterWorld navmesh START ===");
            if (!System.IO.File.Exists(Village2ScenePath) || !System.IO.File.Exists(OuterWorldScenePath))
            {
                Err($"Missing {Village2ScenePath} or {OuterWorldScenePath}. Run Phase A + the world build first. Aborting.");
                return;
            }

            // Open Village2 (active) + OuterWorld additive — both contribute to one bake.
            Scene village2 = EditorSceneManager.OpenScene(Village2ScenePath, OpenSceneMode.Single);
            Scene outer = EditorSceneManager.OpenScene(OuterWorldScenePath, OpenSceneMode.Additive);
            Log($"Opened '{Village2ScenePath}' (active) + '{OuterWorldScenePath}' (additive).");

            // --- Flag Village2 obstacle geometry NavigationStatic --------------------
            GameObject root = FindRoot(village2, "Village2");
            int obstacles = 0, skipped = 0;
            if (root != null)
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (NameOrAncestorContainsAny(r.transform, KeepWalkable)) { skipped++; continue; }
                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags | StaticEditorFlags.NavigationStatic);
                    obstacles++;
                }
            }
            Log($"Flagged {obstacles} Village2 renderer(s) NavigationStatic (obstacles); kept {skipped} walkable (gate arches/roads).");

            // --- Level the OuterWorld terrain to Y=0 (flush) + flag it static --------
            const float villageFloorY = 0f;
            int terrains = 0;
            foreach (var go in outer.GetRootGameObjects())
                foreach (var terr in go.GetComponentsInChildren<Terrain>(true))
                {
                    float edgeSurface = terr.transform.position.y + terr.SampleHeight(new Vector3(42f, 0f, 0f));
                    float delta = villageFloorY - edgeSurface;
                    terr.transform.position += new Vector3(0f, delta, 0f);
                    var flags = GameObjectUtility.GetStaticEditorFlags(terr.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(terr.gameObject, flags | StaticEditorFlags.NavigationStatic);
                    EditorUtility.SetDirty(terr.gameObject);
                    terrains++;
                    Log($"Terrain '{terr.name}' leveled by {delta:F3} -> Y=0 flush; flagged NavigationStatic.");
                }
            if (terrains == 0)
                Err("No Terrain found in OuterWorld — the hero will have NO walkable ground outside the gates. " +
                    "Run Defenders/World/Build Outer World + Build Exterior Terrain first.");

            // --- Bake ONE combined surface ------------------------------------------
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            // WO-1731: the combined bake above rewrote both scenes' navmesh references. An
            // unsaved post-bake scene keeps the reference the bake replaced and ships with no
            // navmesh -- silently. THROW (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(village2);
            EditorSceneManager.MarkSceneDirty(outer);
            if (!EditorSceneManager.SaveScene(village2, Village2ScenePath))
                throw new System.InvalidOperationException(
                    "[Village2Playable] could not save navigation for " + Village2ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            if (!EditorSceneManager.SaveScene(outer, OuterWorldScenePath))
                throw new System.InvalidOperationException(
                    "[Village2Playable] could not save navigation for " + OuterWorldScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();

            Log($"VERIFY: obstacles={obstacles} walkableKept={skipped} terrains={terrains}. " +
                "Baked ONE combined navmesh across Village2 + OuterWorld; saved both scenes.");
            Log("=== PHASE C DONE ===");
        }

        // =====================================================================
        // PHASE D - THE SWAP (Option 2, owner-approved 2026-06-04): keep the scene
        // NAMED "Village" so the 15+ systems that gate on scene.name=="Village" all
        // keep working with ZERO code edits. Back up the live hand-authored
        // Village.unity -> Village_Legacy.unity (rollback, kept on disk), then write
        // the generated Village2 content (Heart + nav flags already in it) into
        // Village.unity. NEVER deletes; backup is verified BEFORE the overwrite.
        // After this: run OuterWorldBuilder.BakeWorldNavMesh (targets Village.unity)
        // then D2 verify.
        // =====================================================================
        const string LegacyScenePath = "Assets/Scenes/Village_Legacy.unity";

        [MenuItem("Defenders/Village2/D. Swap Generated Village Into Village.unity")]
        public static void D_SwapIntoLiveVillage()
        {
            Log("=== PHASE D: Swap generated village INTO Village.unity START ===");

            // Guard 1: source must exist AND be wired (has a Heart) — never swap a bare shell.
            if (!File.Exists(Village2ScenePath)) { Err($"{Village2ScenePath} missing. Aborting."); return; }
            if (!File.Exists(VillageScenePath)) { Err($"{VillageScenePath} missing — nothing to back up. Aborting."); return; }

            Scene src = EditorSceneManager.OpenScene(Village2ScenePath, OpenSceneMode.Single);
            System.Type heartType = FindType(TypeHeartController);
            int srcHearts = heartType != null ? Object.FindObjectsByType(heartType).Length : 0;
            GameObject srcRoot = FindRoot(src, "Village2");
            int srcObjs = srcRoot != null ? srcRoot.transform.childCount : 0;
            if (srcHearts < 1) { Err($"Source {Village2ScenePath} has no HeartController — refusing to swap a bare shell. Run B1 first. Aborting."); return; }
            Log($"Source verified: {srcObjs} objects, {srcHearts} Heart. OK to swap.");

            // Guard 2: BACK UP the live Village.unity FIRST and confirm the copy exists.
            if (File.Exists(LegacyScenePath))
            {
                Log($"{LegacyScenePath} already exists (prior backup) — leaving it as the original rollback, not overwriting it.");
            }
            else
            {
                bool copied = AssetDatabase.CopyAsset(VillageScenePath, LegacyScenePath);
                AssetDatabase.SaveAssets();
                if (!copied || !File.Exists(LegacyScenePath))
                {
                    Err($"Backup CopyAsset {VillageScenePath} -> {LegacyScenePath} FAILED. Refusing to overwrite Village.unity without a rollback. Aborting.");
                    return;
                }
                Log($"BACKED UP live village -> {LegacyScenePath} (rollback preserved).");
            }

            // Now safe: write the generated content into Village.unity (keeps its GUID/path,
            // so Build Settings + every asset reference stay valid; scene.name stays "Village").
            bool ok = EditorSceneManager.SaveScene(src, VillageScenePath, /*saveAsCopy:*/ false);
            if (!ok) { Err($"SaveScene to {VillageScenePath} FAILED. Aborting (Village_Legacy.unity holds the original)."); return; }
            Log($"Wrote generated village -> {VillageScenePath} (scene.name stays 'Village').");

            // Remove the now-redundant Village2.unity from Build Settings (keep Village.unity).
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int removed = scenes.RemoveAll(s => s.path == Village2ScenePath);
            if (removed > 0) { EditorBuildSettings.scenes = scenes.ToArray(); Log($"Removed redundant {Village2ScenePath} from Build Settings."); }

            // Verify Village.unity now holds the generated village.
            Scene check = EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Single);
            int liveHearts = heartType != null ? Object.FindObjectsByType(heartType).Length : -1;
            GameObject liveRoot = FindRoot(check, "Village2");
            Log($"VERIFY: Village.unity scene='{check.name}' rootObj='{(liveRoot != null ? liveRoot.name : "?")}' heart={liveHearts}. Village_Legacy.unity = rollback.");
            Log("=== PHASE D DONE — next: OuterWorldBuilder.BakeWorldNavMesh, then D2 verify ===");
        }

        [MenuItem("Defenders/Village2/D2. Verify Live Village Walkable")]
        public static void D2_VerifyLiveVillage()
        {
            Log("=== PHASE D2: Verify Village.unity navmesh connectivity START ===");
            EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(OuterWorldScenePath, OpenSceneMode.Additive);
            VerifyConnectivityFromPlaza();
            Log("=== PHASE D2 DONE ===");
        }

        // =====================================================================
        // PHASE C-verify - headless walled-in check. Opens the baked scenes, samples
        // the navmesh in the plaza + far out on the terrain in all 4 directions, and
        // runs CalculatePath. A COMPLETE path = the hero can walk village -> terrain
        // through that side. Confirms the owner-flagged risk WITHOUT a playtest.
        // =====================================================================
        [MenuItem("Defenders/Village2/C2. Verify Walkable (navmesh connectivity)")]
        public static void C2_VerifyWalkable()
        {
            Log("=== PHASE C2: Verify navmesh connectivity START ===");
            EditorSceneManager.OpenScene(Village2ScenePath, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(OuterWorldScenePath, OpenSceneMode.Additive);
            VerifyConnectivityFromPlaza();
            Log("=== PHASE C2 DONE ===");
        }

        // Samples the navmesh near the plaza + far out on the terrain in all 4 cardinals,
        // runs CalculatePath, logs how many sides are COMPLETE (hero can walk out). Assumes
        // the village scene + OuterWorld are already open with the combined navmesh baked.
        static void VerifyConnectivityFromPlaza()
        {
            if (!NavMesh.SamplePosition(new Vector3(6f, 0f, 0f), out NavMeshHit plaza, 12f, NavMesh.AllAreas))
            {
                Err("No navmesh near the village plaza (sample @ (6,0,0) r=12 failed). Bake produced no walkable ground inside.");
                return;
            }
            Log($"Plaza navmesh point: {plaza.position}");

            (string dir, Vector3 p)[] farPoints =
            {
                ("EAST",  new Vector3( 90f, 0f,   0f)),
                ("WEST",  new Vector3(-90f, 0f,   0f)),
                ("NORTH", new Vector3(  0f, 0f,  90f)),
                ("SOUTH", new Vector3(  0f, 0f, -90f)),
            };

            int complete = 0;
            foreach (var (dir, p) in farPoints)
            {
                if (!NavMesh.SamplePosition(p, out NavMeshHit far, 30f, NavMesh.AllAreas))
                {
                    Warn($"  {dir}: no terrain navmesh near {p} (r=30) — terrain may not extend here.");
                    continue;
                }
                var path = new NavMeshPath();
                bool ok = NavMesh.CalculatePath(plaza.position, far.position, NavMesh.AllAreas, path);
                Log($"  {dir}: target {far.position} -> path status={path.status} corners={path.corners.Length} (ok={ok})");
                if (path.status == NavMeshPathStatus.PathComplete) complete++;
            }

            Log($"VERIFY: {complete}/4 cardinal directions reachable village -> terrain (COMPLETE path).");
            if (complete == 0) Err("WALLED IN: hero cannot reach the terrain on any side. Gate openings/terrain bake need work.");
            else if (complete < 4) Warn($"Partial: {complete}/4 sides open — usable, but some gates may be sealed.");
            else Log("ALL 4 sides open — hero can walk out every gate onto the terrain.");
        }

        static bool NameOrAncestorContainsAny(Transform t, string[] fragments)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                foreach (var f in fragments)
                    if (cur.name.Contains(f)) return true;
            return false;
        }

        // =====================================================================
        // Helpers
        // =====================================================================
        static bool OpenVillage2(out Scene scene)
        {
            scene = default;
            if (!File.Exists(Village2ScenePath))
            {
                Err($"{Village2ScenePath} does not exist yet - run Phase A first. Aborting.");
                return false;
            }
            scene = EditorSceneManager.OpenScene(Village2ScenePath, OpenSceneMode.Single);
            Log($"Opened '{Village2ScenePath}' ({scene.rootCount} roots).");
            return true;
        }

        static GameObject FindRoot(Scene scene, string exactName)
        {
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == exactName) return r;
            return null;
        }

        static GameObject FindByNameContains(Scene scene, string fragment)
        {
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name.Contains(fragment)) return r;
                var all = r.GetComponentsInChildren<Transform>(true);
                foreach (var t in all)
                    if (t.name.Contains(fragment)) return t.gameObject;
            }
            return null;
        }

        static void EnsureInBuildSettings(string scenePath, string afterPath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath))
            {
                Log($"Build Settings already contains '{scenePath}' - no change.");
                return;
            }
            int insertAt = scenes.FindIndex(s => s.path == afterPath);
            var entry = new EditorBuildSettingsScene(scenePath, true);
            if (insertAt >= 0) scenes.Insert(insertAt + 1, entry);
            else scenes.Add(entry);
            EditorBuildSettings.scenes = scenes.ToArray();
            Log($"Added '{scenePath}' to Build Settings (enabled). Total scenes: {scenes.Count}.");
        }

        static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        // Adds a runtime gameplay component by full type name (resolved by reflection,
        // same exemption as VillageSceneBuilder — build tooling, not a runtime bridge).
        // Idempotent: returns the existing component if already attached.
        static Component AddByType(GameObject go, string fullTypeName)
        {
            var t = FindType(fullTypeName);
            if (t == null)
            {
                Warn($"Type '{fullTypeName}' not found (is DeNelle.Village compiled?) — skipping that component.");
                return null;
            }
            var existing = go.GetComponent(t);
            return existing != null ? existing : go.AddComponent(t);
        }

        // Wires a serialized object-reference field by name (no compile-time dep on
        // the target type). Mirrors VillageSceneBuilder.SetObjectField.
        static void SetObjectField(SerializedObject so, string field, Object value)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Warn($"Serialized field '{field}' not found on {so.targetObject.GetType().Name} — not wired.");
                return;
            }
            prop.objectReferenceValue = value;
        }

        // Assigns the first PanelSettings asset in the project to a UIDocument so its
        // UXML actually renders. Mirrors WallRepairSceneSetup.WirePanelSettings.
        static void WirePanelSettings(UIDocument uiDoc)
        {
            if (uiDoc == null || uiDoc.panelSettings != null) return;
            var guids = AssetDatabase.FindAssets("t:PanelSettings");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                if (panel != null) { uiDoc.panelSettings = panel; return; }
            }
            Warn("No PanelSettings asset found — the HUD UIDocument will not render until one is assigned.");
        }

        static void Log(string m)  => Debug.Log("[Village2Playable] " + m);
        static void Warn(string m) => Debug.LogWarning("[Village2Playable] " + m);
        static void Err(string m)  => Debug.LogError("[Village2Playable] " + m);
    }
}
