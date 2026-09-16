// =============================================================================
// Village3Builder — PHASE 1 of the "Village3 = authored shell + recipe-driven
// buildings" migration.
// -----------------------------------------------------------------------------
// Village3 is a fresh scene that is a SHELL (terrain/ground + walls + gates +
// Heart + camera/HUD/hero/waves + NavMesh) whose GAMEPLAY BUILDINGS are placed
// from a DATA RECIPE (Village3BuildingRecipe.json) exported from the hard-coded
// Buildings[] layout — instead of the hard-coded Buildings[] inject.
//
// It is PURELY ADDITIVE: Village2, Village2.unity, and
// InjectGameplayBuildingsIntoVillage2 are NEVER touched. Village3 reuses:
//   - Village2Build.SetupAndGenerateVillage2 for the generated art shell
//     (terrain/ground, walls, gates from Village2Generator),
//   - Village2Playable's scene-agnostic Import* methods for the gameplay wiring
//     (scene defaults, EventSystem + Controller, HUD, hero, waves),
//   - VillageSceneBuilder.InjectBuildingsFromRecipe for the DATA-DRIVEN buildings,
//   - a Phase-C-style combined Village3 + OuterWorld NavMesh bake to finish.
//
//   Defenders > Village3 > Build Village3
//   (batchmode: DeNelle.Editor.Village3Builder.BuildVillage3)
//
//   Defenders > Village3 > Build Village3 (Empty Shell)
//   (batchmode: DeNelle.Editor.Village3Builder.BuildVillage3Shell)
//   Same flow MINUS the recipe building inject — walls/gates/terrain/Heart/
//   hero/camera/HUD/waves/NavMesh only, NO gameplay buildings.
//
// The NavMesh bake is the FINAL step INSIDE BuildVillage3 (so one call yields a
// walkable Village3). It is also exposed as BakeVillage3NavMesh for a separate run.
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class Village3Builder
    {
        const string ScenesDir          = "Assets/Scenes";
        const string Village3ScenePath  = ScenesDir + "/Village3.unity";
        const string VillageScenePath   = ScenesDir + "/Village.unity";
        const string OuterWorldScenePath = ScenesDir + "/OuterWorld.unity";

        const string TypeHeartController = "DeNelle.Village.HeartController";

        // Names whose renderers must STAY walkable in the bake (gate openings + roads);
        // everything else under the village root becomes a NavigationStatic obstacle.
        // Mirrors Village2Playable.KeepWalkable.
        static readonly string[] KeepWalkable = { "Wall_Arch", "Gate_Medieval", "Floor_Brick" };

        // =====================================================================
        // BUILD VILLAGE3 — generate shell -> wire gameplay -> recipe buildings ->
        // bake. One command, batchmode-callable.
        // =====================================================================
        [MenuItem("Defenders/Village3/Build Village3")]
        public static void BuildVillage3() => BuildVillage3Internal(populate: true);

        // =====================================================================
        // BUILD VILLAGE3 SHELL (EMPTY) — identical flow to BuildVillage3 but
        // SKIPS the recipe building inject, so the result is a SHELL ONLY:
        // terrain/ground + walls + gates + Heart + camera/HUD/hero/waves +
        // NavMesh, with NO gameplay buildings. Lets the player/build-mode place
        // structures into a clean town. Batchmode-callable.
        // =====================================================================
        [MenuItem("Defenders/Village3/Build Village3 (Empty Shell)")]
        public static void BuildVillage3Shell() => BuildVillage3Internal(populate: false);

        // =====================================================================
        // BUILD VILLAGE3 (BONES ONLY) — a truly BARE canvas. Identical wiring to
        // BuildVillage3Shell (no recipe buildings) AND generates the art shell in
        // BONES-ONLY mode: KEEPS terrain/ground (roads) + the 4 perimeter walls +
        // gates + towers + the Heart/Tree-of-Life + camera/HUD/hero/spawns/waves +
        // NavMesh, but SKIPS the decorative houses + specialty buildings + the
        // nature ring (trees/bushes/rocks). Batchmode-callable.
        // =====================================================================
        [MenuItem("Defenders/Village3/Build Village3 (Bones Only)")]
        public static void BuildVillage3Bones() => BuildVillage3Internal(populate: false, bonesOnly: true);

        // =====================================================================
        // BUILD VILLAGE3 (EMPTY / TERRAIN ONLY) — the BAREST authoring canvas.
        // Same gameplay wiring as the shell (hero/camera/HUD/spawns/waves/NavMesh)
        // and the Heart/Tree-of-Life, but the generated art shell is TERRAIN-ONLY:
        // ground/roads only — NO walls, NO gates, NO towers, NO moat, NO torches,
        // NO houses, NO nature ring. The player authors the ENTIRE perimeter from
        // scratch. Ground is painted with a proper URP material (no magenta/blue).
        // Batchmode-callable.
        // =====================================================================
        [MenuItem("Defenders/Village3/Build Village3 (Empty / Terrain Only)")]
        public static void BuildVillage3Empty() => BuildVillage3Internal(populate: false, bonesOnly: true, terrainOnly: true);

        // populate = true: place the recipe gameplay buildings (full Village3).
        // populate = false: stop after the shell wiring (empty-shell Village3).
        // bonesOnly = true: generate the art shell with NO decorative houses/nature ring.
        // terrainOnly = true: ALSO strip the perimeter (walls/gates/towers/moat/torches) — bare canvas.
        static void BuildVillage3Internal(bool populate, bool bonesOnly = false, bool terrainOnly = false)
        {
            Log(terrainOnly
                ? "=== BUILD VILLAGE3 START (EMPTY / TERRAIN ONLY — no perimeter) ==="
                : bonesOnly
                    ? "=== BUILD VILLAGE3 START (BONES ONLY) ==="
                    : populate
                        ? "=== BUILD VILLAGE3 START (populated) ==="
                        : "=== BUILD VILLAGE3 START (EMPTY SHELL) ===");

            // --- 1. Fresh empty scene + generate the art shell ------------------
            // SetupAndGenerateVillage2 builds the generated town (terrain/ground,
            // walls, gates) into a root named "Village2" in the active scene. We
            // build it fresh, then SAVE it as Village3.unity (its own scene file,
            // so Village2.unity is never touched).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Village2Build.SetupAndGenerateVillage2(bonesOnly, terrainOnly);

            Scene scene = EditorSceneManager.GetActiveScene();
            GameObject root = FindGeneratedRoot(scene);
            if (root == null)
            {
                Err("No generated village root found after SetupAndGenerateVillage2 — aborting.");
                return;
            }
            Log($"Generated shell root '{root.name}' with {root.transform.childCount} child groups.");

            bool saved = EditorSceneManager.SaveScene(scene, Village3ScenePath, /*saveAsCopy:*/ false);
            if (!saved) { Err($"Failed to save {Village3ScenePath}. Aborting."); return; }
            EnsureInBuildSettings(Village3ScenePath, afterPath: VillageScenePath);
            Log($"Saved generated shell -> {Village3ScenePath} (Village2.unity untouched).");

            // --- 2. Gameplay wiring (the shell), reusing Village2Playable -------
            // SKIP the hard-coded Buildings[] inject — buildings come from the recipe.
            Village2Playable.AddSceneDefaultsToActiveScene();   // camera + light + ambient/fog
            WireHeart(root);                                    // HeartController on a clean anchor
            Village2Playable.ImportEventSystem();
            Village2Playable.ImportVillageController(root.transform);
            Village2Playable.ImportVillageHud(root.transform);

            var heartType = FindType(TypeHeartController);
            Component heart = heartType != null ? Object.FindAnyObjectByType(heartType) as Component : null;

            GameObject hero = Village2Playable.ImportHero(root.transform, heart);
            Village2Playable.WireCameraTargetToHero(hero);

            Village2Playable.ImportSpawnPoints(root.transform);
            Village2Playable.ImportWaveManager(root.transform, heart);

            // --- 3. DATA-DRIVEN buildings from the recipe (NOT Buildings[]) -----
            // EMPTY-SHELL mode skips this entirely so Village3 ships with no
            // gameplay buildings (walls/gates/terrain/Heart/hero/camera/HUD/waves only).
            if (populate)
            {
                int placed = VillageSceneBuilder.InjectBuildingsFromRecipe(null);
                Log($"Recipe-placed {placed} gameplay building(s) into Village3.");
            }
            else
            {
                Log("EMPTY SHELL: skipped InjectBuildingsFromRecipe — no gameplay buildings placed.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village3ScenePath);
            Log($"Saved wired Village3 (pre-bake) -> {Village3ScenePath}.");

            // --- 4. NavMesh bake (final step, same as Village2's flow) ----------
            BakeVillage3NavMesh();

            Log(terrainOnly
                ? "=== BUILD VILLAGE3 DONE (EMPTY / TERRAIN ONLY) — open Village3.unity and press Play. VILLAGE3_BUILD_OK ==="
                : bonesOnly
                    ? "=== BUILD VILLAGE3 DONE (BONES ONLY) — open Village3.unity and press Play. VILLAGE3_BUILD_OK ==="
                    : populate
                        ? "=== BUILD VILLAGE3 DONE (populated) — open Village3.unity and press Play. VILLAGE3_BUILD_OK ==="
                        : "=== BUILD VILLAGE3 DONE (EMPTY SHELL) — open Village3.unity and press Play. VILLAGE3_BUILD_OK ===");
        }

        // =====================================================================
        // NAVMESH BAKE — combined Village3 + OuterWorld surface. Mirrors
        // Village2Playable.C_BakeNavMesh but targets Village3.unity. Exposed as its
        // own batchmode entry so it can be re-run independently.
        // =====================================================================
        [MenuItem("Defenders/Village3/Bake Village3 NavMesh")]
        public static void BakeVillage3NavMesh()
        {
            Log("=== BAKE VILLAGE3 NAVMESH START ===");
            if (!File.Exists(Village3ScenePath))
            {
                Err($"{Village3ScenePath} missing — run Build Village3 first. Aborting.");
                return;
            }

            Scene village3 = EditorSceneManager.OpenScene(Village3ScenePath, OpenSceneMode.Single);
            bool haveOuter = File.Exists(OuterWorldScenePath);
            Scene outer = default;
            if (haveOuter)
            {
                outer = EditorSceneManager.OpenScene(OuterWorldScenePath, OpenSceneMode.Additive);
                Log($"Opened '{Village3ScenePath}' (active) + '{OuterWorldScenePath}' (additive).");
            }
            else Warn($"{OuterWorldScenePath} not found — baking Village3 alone (no terrain seam).");

            GameObject root = FindGeneratedRoot(village3);
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
            Log($"Flagged {obstacles} Village3 renderer(s) NavigationStatic; kept {skipped} walkable.");

            int terrains = 0;
            if (haveOuter)
            {
                const float villageFloorY = 0f;
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
                    Warn("No Terrain found in OuterWorld — hero will have no walkable ground outside the gates.");
            }

            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            // WO-1731: the combined bake above rewrote these scenes' navmesh references. An
            // unsaved post-bake scene keeps the reference the bake replaced and ships with no
            // navmesh -- silently. THROW (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(village3);
            if (!EditorSceneManager.SaveScene(village3, Village3ScenePath))
                throw new System.InvalidOperationException(
                    "[Village3Builder] could not save navigation for " + Village3ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            if (haveOuter)
            {
                EditorSceneManager.MarkSceneDirty(outer);
                if (!EditorSceneManager.SaveScene(outer, OuterWorldScenePath))
                    throw new System.InvalidOperationException(
                        "[Village3Builder] could not save navigation for " + OuterWorldScenePath +
                        " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            }
            AssetDatabase.SaveAssets();

            Log($"VERIFY: obstacles={obstacles} walkableKept={skipped} terrains={terrains}. " +
                "Baked combined Village3 + OuterWorld navmesh. VILLAGE3_BAKE_OK");
            Log("=== BAKE VILLAGE3 NAVMESH DONE ===");
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        // Hosts HeartController on a clean scale-1 anchor at the tree's x/z (avoids the
        // heart-collider-scale-trap on the scaled tree). Mirrors Village2Playable.B1_WireHeart.
        static void WireHeart(GameObject root)
        {
            var heartType = FindType(TypeHeartController);
            if (heartType == null) { Err("HeartController type not found (DeNelle.Village compiled?)."); return; }

            GameObject tree = FindByNameContains(root, "TreeOfLife") ?? FindByNameContains(root, "Tree");
            Vector3 centre = tree != null
                ? new Vector3(tree.transform.position.x, 0f, tree.transform.position.z)
                : Vector3.zero;

            const string AnchorName = "HeartOfElarion";
            Transform existingAnchor = root.transform.Find(AnchorName);
            GameObject anchor = existingAnchor != null ? existingAnchor.gameObject : new GameObject(AnchorName);
            if (existingAnchor == null) anchor.transform.SetParent(root.transform, false);
            anchor.transform.position = centre;
            anchor.transform.localScale = Vector3.one;

            if (anchor.GetComponent(heartType) == null)
            {
                var heart = anchor.AddComponent(heartType);
                var so = new SerializedObject(heart);
                var prop = so.FindProperty("_useAuthoredTransform");
                if (prop != null) { prop.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Warn("HeartController._useAuthoredTransform not found — Heart may snap to origin at Play.");
                Log($"WireHeart: HeartController on '{AnchorName}' at {centre} (scale 1).");
            }
            else Log("WireHeart: HeartController already on the anchor — idempotent skip.");
        }

        // The generated town root. SetupAndGenerateVillage2 names it "Village2"; accept
        // that or a "Village3" rename, else the first root that has child geometry.
        static GameObject FindGeneratedRoot(Scene scene)
        {
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "Village2" || r.name == "Village3") return r;
            foreach (var r in scene.GetRootGameObjects())
                if (r.GetComponentInChildren<Renderer>(true) != null) return r;
            return null;
        }

        static GameObject FindByNameContains(GameObject root, string fragment)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains(fragment)) return t.gameObject;
            return null;
        }

        static bool NameOrAncestorContainsAny(Transform t, string[] fragments)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                foreach (var f in fragments)
                    if (cur.name.Contains(f)) return true;
            return false;
        }

        static void EnsureInBuildSettings(string scenePath, string afterPath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) { Log($"Build Settings already contains '{scenePath}'."); return; }
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

        static void Log(string m)  => Debug.Log("[Village3Builder] " + m);
        static void Warn(string m) => Debug.LogWarning("[Village3Builder] " + m);
        static void Err(string m)  => Debug.LogError("[Village3Builder] " + m);
    }
}
