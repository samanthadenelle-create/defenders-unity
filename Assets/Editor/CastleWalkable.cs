// CastleWalkable.cs — drop a controllable, human-scale hero + follow camera + input
// into Assets/Scenes/CastleTest.unity so the owner can WALK THROUGH the castle at
// human scale. Owner-directed (2026-06-07): "I want to walk through the CastleTest
// scene at human scale."
//
// REUSES the proven Village playable wiring verbatim — no reinvented locomotion or
// camera. It calls Village2Playable's public, scene-agnostic builders directly:
//   - Village2Playable.AddSceneDefaultsToActiveScene() — Main Camera (VillageCamera +
//     SmartMobileCamera) + Directional Light + ambient/fog
//   - Village2Playable.ImportEventSystem()             — EventSystem (+ Input UI module)
//   - Village2Playable.ImportHero(root, heart)         — People Mage body (~2 m) +
//     HeroBodySwapper/HeroLocomotion/HeroAbilities/HeroAbilityInput, tagged "Player"
//   - Village2Playable.WireCameraTargetToHero(hero)    — smart camera _target -> hero
//
// Editor-only (DeNelle.Editor asmdef). Like Village2Playable it cannot reference
// Assembly-CSharp, so it leans on Village2Playable for the reflection-based component
// adds (build tooling exemption, same as VillageSceneBuilder).
//
// Run:
//   In-editor : Defenders/Sandbox/Make CastleTest Walkable
//   Batchmode : -executeMethod DeNelle.Editor.CastleWalkable.MakeWalkable

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class CastleWalkable
    {
        const string CastleScenePath = "Assets/Scenes/CastleTest.unity";

        [MenuItem("Defenders/Sandbox/Make CastleTest Walkable")]
        public static void MakeWalkable()
        {
            Log("=== Make CastleTest walkable START ===");

            // --- 1. Open CastleTest (Single) — or reuse it if it's already active. ---
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != CastleScenePath)
            {
                if (!File.Exists(CastleScenePath))
                {
                    Fail($"{CastleScenePath} does not exist — nothing to make walkable.");
                    return;
                }
                scene = EditorSceneManager.OpenScene(CastleScenePath, OpenSceneMode.Single);
            }
            Log($"Active scene '{scene.name}' ({scene.rootCount} roots).");

            // --- 2. Scene defaults: Main Camera (SmartMobileCamera) + Directional Light
            //        + ambient/fog. Idempotent (skips anything already present). ---
            Village2Playable.AddSceneDefaultsToActiveScene();

            // --- 3. EventSystem (+ new-Input-System UI module) so input routes. ---
            Village2Playable.ImportEventSystem();

            // --- 4. Build/import the SAME village hero (human-scale ~2 m) with its
            //        locomotion + abilities, tagged "Player". CastleTest has no Heart,
            //        so the Healing-Beacon target is null (ImportHero null-guards it). ---
            Transform parent = ResolveHeroParent(scene);
            GameObject hero = Village2Playable.ImportHero(parent, /*heart:*/ null);
            if (hero == null) { Fail("ImportHero returned null — hero not built."); return; }

            // --- 5. Spawn the hero in the courtyard, just inside the gate, ON the floor.
            //        Measure the REAL floor bounds (robust to whatever was built) rather
            //        than assume the default 8x3 layout: ground on floor-top, sit near
            //        the -Z (gate) edge one tile in, facing +Z toward the courtyard centre. ---
            PlaceHeroInCourtyard(scene, hero);

            // --- 6. Wire the smart follow camera's _target -> the hero. ---
            Village2Playable.WireCameraTargetToHero(hero);

            // --- 6a. ROOT-CAUSE FIX (owner playtest "I move a CAMERA, not the hero").
            //         HeroLocomotion is NavMesh-FIRST: in Awake it adds a NavMeshAgent and
            //         every move frame does `if (agent.isOnNavMesh) agent.Move(step); else
            //         transform += step`. CastleTest had NO baked NavMesh (m_NavMeshData:
            //         {fileID:0}), so the hero ran the raw-transform fallback only — fragile
            //         on the building-floor prefab, and the only thing the player could
            //         reliably drive was the camera (CameraPanInput self-bootstraps in every
            //         non-DTT scene + SmartMobileCamera orbit is force-on, so slide/right-drag
            //         pans the view). Village walks fine because Village BAKES a NavMesh.
            //         Replicate the village's minimum here: mark the floor walkable + bake.
            BakeCastleNavMesh(scene);

            // --- 6b. HeroControlEnsurer (the runtime guard that keeps the village hero
            //         controllable + enabled) is HARDCODED to TargetScene "Village2", so it
            //         is INERT in CastleTest — nothing guarantees HeroLocomotion is enabled.
            //         Do the same guarantee at author time: make sure the hero is active and
            //         its HeroLocomotion behaviour is enabled (a disabled behaviour silently
            //         eats all WASD). Reflection-only (editor asmdef can't ref DeNelle.Village). ---
            EnsureHeroControllable(hero);

            // --- 7. Save. ---
            // WO-1731: a bake that cannot PERSIST its own navmesh reference must THROW, not
            // warn. The bake at step 6a writes a navmesh asset and rewrites the scene's
            // m_NavMeshData; if the scene is never written back, the asset on disk and the
            // reference in the scene diverge and the scene ships with NO navmesh -- silently,
            // because HeroLocomotion falls back to raw transform movement off-mesh. Same
            // shape as RaidNavBake.cs:109-111.
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, CastleScenePath))
                throw new System.InvalidOperationException(
                    "[CastleWalkable] could not save navigation for " + CastleScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");

            Vector3 p = hero.transform.position;
            Log($"CASTLE_WALKABLE_OK heroAt=({p.x:F2}, {p.y:F2}, {p.z:F2})");
            Log("=== Make CastleTest walkable DONE — open CastleTest.unity and press Play (WASD + mouse) ===");
        }

        // The hero parent: reuse a top-level castle root if present, else parent to the
        // first scene root, else leave at scene root. ImportHero positions via the parent
        // (SetParent(..., false)), then PlaceHeroInCourtyard sets the final world position.
        static Transform ResolveHeroParent(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            foreach (var r in roots)
                if (r.name.Contains("Castle") || r.name.Contains("Courtyard"))
                    return r.transform;
            return roots.Length > 0 ? roots[0].transform : null;
        }

        // Grounds the hero on the real floor and seats it just inside the gate.
        // Spawn = (floorCentreX, floorTopY, floorMinZ + tile), facing +Z toward centre.
        // Falls back to the default-layout spawn (0, 0, -9) if no floor renderers found.
        static void PlaceHeroInCourtyard(Scene scene, GameObject hero)
        {
            const float defaultTile = 3f;

            if (TryFloorBounds(scene, out Bounds fb))
            {
                float tile = Mathf.Max(defaultTile, fb.size.x / 17f); // 8x3 layout = 17 tiles wide; clamp to >= 3
                float floorTopY = fb.max.y;                            // ground ON the floor surface
                float centreX = fb.center.x;
                float entryZ = fb.min.z + tile;                        // one tile in from the gate (-Z) edge
                // Keep the entry inside the courtyard span.
                entryZ = Mathf.Min(entryZ, fb.center.z);

                hero.transform.position = new Vector3(centreX, floorTopY, entryZ);
                hero.transform.rotation = Quaternion.Euler(0f, 0f, 0f); // face +Z toward the courtyard centre
                Log($"Hero placed on floor: bounds center={fb.center} size={fb.size} top={floorTopY:F3} " +
                    $"-> spawn=({centreX:F2}, {floorTopY:F2}, {entryZ:F2}), tile~{tile:F2}.");
            }
            else
            {
                hero.transform.position = new Vector3(0f, 0f, -9f);
                hero.transform.rotation = Quaternion.Euler(0f, 0f, 0f); // face +Z
                Warn("No floor renderers found — using fallback spawn (0, 0, -9) facing +Z.");
            }
        }

        // Union of renderer bounds for the castle FLOOR tiles (named "Floor"), so the
        // hero grounds on the real floor surface regardless of how the scene was built.
        static bool TryFloorBounds(Scene scene, out Bounds bounds)
        {
            bounds = default;
            bool have = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (!NameOrAncestorContains(r.transform, "Floor")) continue;
                    if (!have) { bounds = r.bounds; have = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            return have;
        }

        static bool NameOrAncestorContains(Transform t, string fragment)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                if (cur.name.Contains(fragment)) return true;
            return false;
        }

        // Geometry whose renderers must be WALKABLE in the bake (the floor the hero stands
        // + walks on). Stairs are included so the hero can climb to the ramparts. Matched by
        // name-or-ancestor so it's robust to the castle builder's grouping.
        static readonly string[] WalkableFragments = { "Floor", "Ground", "Stairs", "Stair", "Rampart" };

        // Geometry that should be a NavMesh OBSTACLE (bounds the walkable surface so the
        // hero can't path through it). Walls/towers/gate get carved out of the bake.
        static readonly string[] ObstacleFragments = { "Wall", "Tower", "Gate" };

        // --- 6a impl: bake a NavMesh over the castle floor exactly like the village does ---
        // HeroLocomotion's NavMeshAgent (radius 0.4, height 1.8) sits inside Unity's default
        // Humanoid bake agent, so the project-default bake is valid for it. Marking floor
        // NavigationStatic + UnityEditor.AI.NavMeshBuilder.BuildNavMesh() is the SAME legacy
        // synchronous bake VillageSceneBuilder.NavMesh / Village3Builder / OuterWorldBuilder
        // use. ClearAllNavMeshes first keeps the re-run idempotent (no duplicate/stale bake).
        static void BakeCastleNavMesh(Scene scene)
        {
            int walkable = 0, obstacle = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    bool isWalkable = NameOrAncestorContainsAny(r.transform, WalkableFragments);
                    bool isObstacle = !isWalkable && NameOrAncestorContainsAny(r.transform, ObstacleFragments);
                    if (!isWalkable && !isObstacle) continue;

                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                        flags | StaticEditorFlags.NavigationStatic);
                    if (isWalkable) walkable++; else obstacle++;
                }
            }

            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            Log($"NavMesh baked — {walkable} walkable renderer(s) (Floor/Ground/Stairs/Rampart) + " +
                $"{obstacle} obstacle renderer(s) (Wall/Tower/Gate) marked NavigationStatic. " +
                "Hero's NavMeshAgent now has a surface, so WASD walks via agent.Move (the " +
                "primary HeroLocomotion path) instead of the fragile transform fallback.");

            if (walkable == 0)
                Warn("No walkable renderers matched — the NavMesh may be empty; the hero will " +
                     "fall back to raw transform movement (still moves, but ungrounded on slopes).");
        }

        static bool NameOrAncestorContainsAny(Transform t, string[] fragments)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                foreach (var f in fragments)
                    if (cur.name.Contains(f)) return true;
            return false;
        }

        // --- 6b impl: guarantee the hero is active + its HeroLocomotion is ENABLED ---
        // HeroControlEnsurer does this at runtime in Village2 only (hardcoded TargetScene),
        // so it never runs in CastleTest. A present-but-disabled HeroLocomotion silently
        // swallows all input — the hero just stands there while the camera is the only thing
        // that responds. We re-assert it here at author time (reflection: the editor asmdef
        // cannot reference DeNelle.Village). Idempotent.
        static void EnsureHeroControllable(GameObject hero)
        {
            if (hero == null) return;
            if (!hero.activeSelf) { hero.SetActive(true); Log("Hero was inactive — activated."); }

            var locoType = System.Type.GetType("DeNelle.Village.HeroLocomotion, Assembly-CSharp")
                           ?? FindTypeAnywhere("DeNelle.Village.HeroLocomotion");
            if (locoType == null)
            {
                Warn("HeroLocomotion type not resolvable — cannot verify it is enabled. " +
                     "If WASD still does nothing, confirm DeNelle.Village compiled.");
                return;
            }

            var loco = hero.GetComponent(locoType) as Behaviour;
            if (loco == null)
            {
                Warn("Hero has NO HeroLocomotion component — ImportHero should have added it. " +
                     "WASD will not move the hero until it is present.");
                return;
            }
            if (!loco.enabled) { loco.enabled = true; Log("HeroLocomotion was DISABLED — re-enabled (this is what eats WASD)."); }
            else Log("HeroLocomotion present + enabled — hero will respond to WASD.");
        }

        // Resolve a runtime type across all loaded assemblies (HeroLocomotion lives in
        // DeNelle.Village, which may be a named asmdef rather than Assembly-CSharp).
        static System.Type FindTypeAnywhere(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        static void Log(string m)  => Debug.Log("[CastleWalkable] " + m);
        static void Warn(string m) => Debug.LogWarning("[CastleWalkable] " + m);
        static void Fail(string m) => Debug.LogError("[CastleWalkable] CASTLE_WALKABLE_FAIL " + m);
    }
}
