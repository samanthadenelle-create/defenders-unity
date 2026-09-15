// =============================================================================
// WorldMergeBuilder — WO-608: MERGE the SAVED MainCastle_Hall + OuterWorld scenes
// into ONE continuous scene (Main_Castle_Overworld), then bake ONE navmesh.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor  (editor-only, batchmode-callable)
//
// THE PIVOT (owner 2026-07-04): MainCastle_Hall and OuterWorld are authored at the SAME
// origin (both centred at 0; terrain 1000x1000, TerrainCenterZ=0). So the merge is the
// OPPOSITE of the WO-453 un-stack: keep co-located, drop both into ONE scene, bake ONE
// navmesh — the seam dissolves because there is no second scene / second navmesh / warp.
// We MERGE THE SAVED .unity FILES (canon: never regenerate the hand-dialed castle, §3),
// via OpenScene(Single)+OpenScene(Additive) + SceneManager.MoveGameObjectToScene (zero
// offset math — co-located).
//
// OWNER FINAL GEOMETRY (2026-07-04): NO moat (water/lip/hedge/berms dropped — decorative
// seam-masking that was fragile). Castle FLUSH at y=0: the +3 island raise
// (CastleHubRoot.localPosition.y=3, baked) is removed by lowering the lifted castle roots
// to y=0 so the castle floor is coplanar with the terrain (flat at y=0 within +-62). The
// CastleBasePlinth (the raised pedestal that filled the gap under the raised castle) is
// disabled — with no raise there is no gap. The 4 drawbridges stay as PURELY DECORATIVE
// flat gateways (placed with castle.liftY=0 so they carry NO deck-collider / no pitch and
// do NOT affect the navmesh). Terrain relief OUTSIDE +-62 is UNTOUCHED (the world feels
// real). Result: one dead-flat continuous ground — castle floor = inner ring (y=0) =>
// natural undulating terrain past +-62, all one navmesh; the seam class is impossible.
//
// This file is EDIT-ONLY authoring; the CLI runs BuildMergedWorldScene + (editor CLOSED)
// BakeMergedWorldNavmesh, gates, fleet-verifies, and commits. Instrumented per CLAUDE.md
// S12: [Flow:WorldMerge] Step/Warn/Fail + Guard.Try on every risky op. Idempotent where
// possible (re-runnable). Never hand-edits a .unity file — all mutation is programmatic in
// the merged copy; SaveScene As a NEW path leaves the original MainCastle_Hall.unity intact.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Editor
{
    /// <summary>
    /// Merges the saved castle + outer-world scenes into one continuous scene and bakes a
    /// single navmesh. Two batchmode-callable entry points (also menu items).
    /// </summary>
    public static class WorldMergeBuilder
    {
        private const string CastleScenePath     = "Assets/Scenes/MainCastle_Hall.unity";
        private const string OuterWorldScenePath = "Assets/Scenes/OuterWorld.unity";
        private const string MergedScenePath     = "Assets/Scenes/Main_Castle_Overworld.unity";
        private const string MergedNavDir        = "Assets/Scenes/Main_Castle_Overworld";
        private const string MergedNavAsset      = "Assets/Scenes/Main_Castle_Overworld/NavMesh-Main_Castle_Overworld.asset";

        // Castle roots that carry the +3 island raise in the SAVED MainCastle_Hall.unity (verified
        // from the scene file: CastleHubRoot.localPosition.y=3; the 4 CastleSide_* skirts + the 2
        // dungeon-stair roots are separate lifted roots — "a root move alone strands the loose
        // CastleSide_* walls", CastleHubBuilder note). The base pedestal + hero-spawn + plinth ride
        // CastleHubRoot (its children), so they lower automatically with it.
        private const string CastleRootName = "CastleHubRoot";
        // F8-24 NOTE: this exact-name list only covers the _West/_South stair CLONES — the two
        // ORIGINAL PrefabInstances ('Dungeon_Stairs_Stone' / 'Dungeon_Stairs_Stone (1)') were never
        // listed, so they stayed floating at y≈3 in the merged scene. The gap is now MOOT for stairs:
        // the owner-ruled removal sweep (CastleWallStairsSeatFix, EditorPrefs 'castle.stairsRemoved')
        // SUPERSEDES lowering — RemoveStairsIfRuled() below destroys ALL Dungeon_Stairs* by prefix
        // after the lower pass, so a re-merge can't resurrect floaters regardless of name.
        private static readonly string[] LiftedCastleRoots =
        {
            "CastleHubRoot",
            "CastleSide_East", "CastleSide_South", "CastleSide_West", "CastleSide_North",
            "Dungeon_Stairs_Stone_West", "Dungeon_Stairs_Stone_South",
        };
        private const string PlinthName = "CastleBasePlinth";

        // ====================================================================
        //  ENTRY 1 — build the merged scene (editor open; CLI runs this).
        // ====================================================================
        [MenuItem("Defenders/World/Merge Castle + Overworld (build merged scene)")]
        public static void BuildMergedWorldScene()
        {
            using var _ = FlowTrace.Enter("WorldMerge", "BuildMergedWorldScene");
            FlowTrace.Step("WorldMerge", "START — merging SAVED scenes '" + CastleScenePath + "' + '" +
                OuterWorldScenePath + "' (co-located at origin, zero offset) -> '" + MergedScenePath + "'.");

            // 1) Open the castle SINGLE (this becomes the merged scene's home). NEVER regen it.
            Scene castleScene = default;
            if (!Guard.Try("WorldMerge", "open castle scene (Single)", () =>
                { castleScene = EditorSceneManager.OpenScene(CastleScenePath, OpenSceneMode.Single); }))
                { FlowTrace.Fail("WorldMerge", "could not open castle scene — abort."); return; }
            if (!castleScene.IsValid()) { FlowTrace.Fail("WorldMerge", "castle scene invalid after open — abort."); return; }

            // 2) Open OuterWorld ADDITIVE (co-located — no offset math).
            Scene outerScene = default;
            if (!Guard.Try("WorldMerge", "open OuterWorld scene (Additive)", () =>
                { outerScene = EditorSceneManager.OpenScene(OuterWorldScenePath, OpenSceneMode.Additive); }))
                { FlowTrace.Fail("WorldMerge", "could not open OuterWorld additively — abort."); return; }
            if (!outerScene.IsValid()) { FlowTrace.Fail("WorldMerge", "OuterWorld scene invalid after open — abort."); return; }

            // 3) Record which SINGLETONS the castle already owns BEFORE the merge (keep the castle's).
            bool castleHasCamera  = SceneHasComponent<Camera>(castleScene);
            bool castleHasAudio   = SceneHasComponent<AudioListener>(castleScene);
            bool castleHasEvent   = SceneHasComponent<EventSystem>(castleScene);
            bool castleHasDirLight = FindDirectionalLight(castleScene) != null;
            FlowTrace.Step("WorldMerge", "castle singletons — camera=" + castleHasCamera + " audioListener=" +
                castleHasAudio + " eventSystem=" + castleHasEvent + " directionalLight=" + castleHasDirLight +
                " (these are KEPT; OuterWorld duplicates are removed).");

            // 4) DISCOVER OuterWorld roots (do NOT assume names) + move each into the castle scene.
            var outerRoots = new List<GameObject>(outerScene.GetRootGameObjects());
            FlowTrace.Step("WorldMerge", "OuterWorld has " + outerRoots.Count + " root(s): " + RootNames(outerRoots) + ".");
            int moved = 0;
            foreach (var root in outerRoots)
            {
                if (root == null) continue;
                Guard.Try("WorldMerge", "move OuterWorld root '" + root.name + "'", () =>
                {
                    SceneManager.MoveGameObjectToScene(root, castleScene);
                    moved++;
                });
            }
            FlowTrace.Step("WorldMerge", "moved " + moved + "/" + outerRoots.Count + " OuterWorld roots into '" + castleScene.name + "'.");

            // 5) DEDUPE the singletons the castle already owns (2nd camera/audio/light/eventsystem).
            int dedup = DedupeMovedSingletons(outerRoots, castleHasCamera, castleHasAudio, castleHasEvent, castleHasDirLight);
            FlowTrace.Step("WorldMerge", "deduped " + dedup + " duplicate singleton component(s)/object(s) from the moved OuterWorld roots.");

            // 6) Close the now-empty additive OuterWorld scene (all its roots were moved out).
            Guard.Try("WorldMerge", "close emptied OuterWorld scene", () =>
                { EditorSceneManager.CloseScene(outerScene, /*removeScene:*/ true); });

            // 7) CASTLE FLUSH — lower the lifted castle roots to y=0 (owner final: no plinth raise).
            LowerCastleToGround();

            // 8) OWNER F8 2026-07-04 ("why is bridge still here?" + "invisible barrier beside me"): the moat
            //    is gone, so the 4 bridges are VESTIGIAL, and their stone-mesh colliders were an invisible
            //    wall beside the hero. REMOVE them entirely (no PlaceDecorativeBridges) and strip the
            //    now-dangling cross-seam NavMeshLinks baked into the old castle scene.
            StripSeamRemnants();

            // 8b) STRUCTURAL stair removal (F8-24): when the owner's removal ruling is in force
            //     (EditorPrefs 'castle.stairsRemoved'), sweep ALL Dungeon_Stairs* out of the merged
            //     scene by prefix — the LiftedCastleRoots exact-name lower above misses the two
            //     original PrefabInstances, and the ruling is removal, not lowering. This makes a
            //     future re-merge/re-bake unable to resurrect floating stairs.
            RemoveStairsIfRuled(castleScene);

            // 9) Mark dirty + SAVE AS the NEW merged path (original MainCastle_Hall.unity untouched).
            EditorSceneManager.MarkSceneDirty(castleScene);
            bool saved = false;
            Guard.Try("WorldMerge", "SaveScene As " + MergedScenePath, () =>
                { saved = EditorSceneManager.SaveScene(castleScene, MergedScenePath); });
            if (!saved) { FlowTrace.Fail("WorldMerge", "SaveScene As FAILED — merged scene not written."); return; }

            AssetDatabase.SaveAssets();
            EnsureInBuildSettings(MergedScenePath);

            FlowTrace.Step("WorldMerge", "DONE — merged scene saved to '" + MergedScenePath + "' and added to Build Settings. " +
                "NEXT (editor CLOSED): WorldMergeBuilder.BakeMergedWorldNavmesh. Original '" + CastleScenePath + "' is untouched.");
            Debug.Log("[WorldMerge] Merged scene built: " + MergedScenePath);
        }

        // ====================================================================
        //  ENTRY 2 — bake ONE continuous navmesh (CLI runs this with editor CLOSED).
        // ====================================================================
        [MenuItem("Defenders/World/Bake Merged World NavMesh")]
        public static void BakeMergedWorldNavmesh()
        {
            using var _ = FlowTrace.Enter("WorldMerge", "BakeMergedWorldNavmesh");

            Scene scene = default;
            if (!Guard.Try("WorldMerge", "open merged scene (Single)", () =>
                { scene = EditorSceneManager.OpenScene(MergedScenePath, OpenSceneMode.Single); }))
                { FlowTrace.Fail("WorldMerge", "could not open merged scene '" + MergedScenePath + "' — bake abort."); return; }
            if (!scene.IsValid()) { FlowTrace.Fail("WorldMerge", "merged scene invalid — bake abort."); return; }

            // ONE NavMeshSurface at origin. Reuse the first; destroy any extras (co-merged surfaces from
            // both source scenes would double-bake). No water NavMeshModifierVolume — the moat is GONE
            // (owner final), and the OLD +-62 blanket carve is explicitly NOT used (it would kill the
            // flush castle floor). collectObjects=All + useGeometry=PhysicsColliders (Terrain collider +
            // castle floor/wall colliders) → one continuous walkable mesh, castle floor (y=0) = inner
            // ring (y=0) = natural terrain past +-62, no height transition to weld.
            NavMeshSurface surf = null;
            Guard.Try("WorldMerge", "resolve single NavMeshSurface", () =>
            {
                var all = UnityEngine.Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
                if (all != null && all.Length > 0)
                {
                    surf = all[0];
                    for (int i = 1; i < all.Length; i++)
                        if (all[i] != null) { FlowTrace.Step("WorldMerge", "destroying extra NavMeshSurface '" + all[i].name + "'."); UnityEngine.Object.DestroyImmediate(all[i].gameObject); }
                    FlowTrace.Step("WorldMerge", "reusing NavMeshSurface '" + surf.name + "' (destroyed " + (all.Length - 1) + " extra).");
                }
                else
                {
                    var host = new GameObject("Merged_NavMeshSurface");
                    host.transform.position = Vector3.zero;
                    surf = host.AddComponent<NavMeshSurface>();
                    FlowTrace.Step("WorldMerge", "created NavMeshSurface host at origin.");
                }
            });
            if (surf == null) { FlowTrace.Fail("WorldMerge", "no NavMeshSurface — bake abort."); return; }

            surf.transform.position = Vector3.zero;
            surf.collectObjects = CollectObjects.All;
            surf.useGeometry    = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            // P0 (owner 2026-07-04, WO-608): the bake opens the SAVED merged scene fresh, which still
            // carries the old WO468_CastleNavHole_NotWalkable volume — a NavMeshModifierVolume marks its
            // AREA regardless of useGeometry, so it would carve a NotWalkable hole at r~44 into this bake
            // and re-confine the hero. Strip every NotWalkable carve BEFORE BuildNavMesh so the navmesh is
            // continuous castle-floor -> terrain. (StripSeamRemnants runs it too on the merge path.)
            StripNotWalkableCarves();

            if (!Guard.Try("WorldMerge", "BuildNavMesh", () => surf.BuildNavMesh()))
                { FlowTrace.Fail("WorldMerge", "BuildNavMesh threw — bake abort."); return; }

            var data = surf.navMeshData;
            if (data == null)
            {
                FlowTrace.Fail("WorldMerge", "navMeshData NULL after bake — nothing collected. Confirm the Terrain + castle " +
                    "floor carry PhysicsColliders (or retry useGeometry=RenderMeshes).");
                return;
            }

            Guard.Try("WorldMerge", "persist navmesh asset", () =>
            {
                if (!System.IO.Directory.Exists(MergedNavDir))
                    AssetDatabase.CreateFolder("Assets/Scenes", "Main_Castle_Overworld");
                if (!AssetDatabase.Contains(data))
                {
                    var prior = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(MergedNavAsset);
                    if (prior != null) AssetDatabase.DeleteAsset(MergedNavAsset);
                    AssetDatabase.CreateAsset(data, MergedNavAsset);
                    FlowTrace.Step("WorldMerge", "navmesh asset -> " + MergedNavAsset + ".");
                }
                else FlowTrace.Step("WorldMerge", "navMeshData already an asset (updated in place).");
            });

            // ⛔ WO-1731 — THIS IS THE SCENE THE DEFECT SHIPPED IN. Main_Castle_Overworld's
            // surface was re-baked and its navmesh asset replaced, the scene was NOT written
            // back, and the saved scene kept the guid of the asset that no longer existed: the
            // town had no navmesh, HeroLocomotion fell through to `transform.position += step`
            // (HeroLocomotion.cs:1488-1489) and the hero walked through every wall. Nothing on
            // screen said so. A bake that cannot persist its own reference THROWS
            // (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException(
                    "[WorldMerge] could not save navigation for " + MergedScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            FlowTrace.Step("WorldMerge", "BAKE DONE — one continuous navmesh saved + scene saved. Continuous walk: castle floor -> " +
                "inner ring (y=0) -> natural terrain; no seam, no warp.");
            Debug.Log("[WorldMerge] Merged navmesh baked: " + MergedNavAsset);
        }

        // --------------------------------------------------------------------
        //  CASTLE FLUSH — lower the lifted castle roots to y=0 + disable the pedestal.
        // --------------------------------------------------------------------
        private static void LowerCastleToGround()
        {
            Guard.Try("WorldMerge", "lower castle to y=0 (remove plinth raise)", () =>
            {
                var castleRoot = GameObject.Find(CastleRootName);
                if (castleRoot == null)
                {
                    FlowTrace.Warn("WorldMerge", "CastleHubRoot not found — castle NOT lowered (verify the merged scene still holds it).");
                    return;
                }

                // SOURCE the lift from the root's OWN baked Y (verified 3 in the saved scene) — never a
                // guessed literal. Everything lifted came up by this same amount.
                float lift = castleRoot.transform.position.y;
                if (lift <= 0.01f)
                {
                    FlowTrace.Step("WorldMerge", "CastleHubRoot already at y=" + lift.ToString("0.00") + " — no lower needed (idempotent).");
                }
                else
                {
                    int lowered = 0;
                    foreach (var name in LiftedCastleRoots)
                    {
                        var go = GameObject.Find(name);
                        if (go == null) { FlowTrace.Step("WorldMerge", "lifted root '" + name + "' absent — skip."); continue; }
                        float y = go.transform.position.y;
                        // Only lower roots that ARE lifted (~= lift). A root already near 0 has its geometry
                        // seated some other way — do not bury it. Log every root's Y for CLI verification.
                        if (Mathf.Abs(y - lift) <= 0.6f)
                        {
                            var p = go.transform.position;
                            go.transform.position = new Vector3(p.x, p.y - lift, p.z);
                            lowered++;
                            FlowTrace.Step("WorldMerge", "lowered '" + name + "' y " + y.ToString("0.00") + " -> " + (y - lift).ToString("0.00") + ".");
                        }
                        else
                        {
                            FlowTrace.Warn("WorldMerge", "root '" + name + "' y=" + y.ToString("0.00") + " is NOT ~= lift " +
                                lift.ToString("0.00") + " — NOT lowered (its geometry may be seated internally; CLI verify by eye).");
                        }
                    }
                    FlowTrace.Step("WorldMerge", "castle flush: lowered " + lowered + " root(s) by lift=" + lift.ToString("0.00") +
                        " so the castle floor sits at y=0, coplanar with the terrain inner ring.");
                }

                // Disable the raised pedestal (owner final: NO plinth dais). It rode CastleHubRoot down, so it
                // is now fully at/under y=0; disable it so no buried/half-slab reads. HIDE not delete (reversible).
                var plinth = GameObject.Find(PlinthName);
                if (plinth != null)
                {
                    plinth.SetActive(false);
                    FlowTrace.Step("WorldMerge", "disabled '" + PlinthName + "' (the raised pedestal is gone — castle meets the flat ground with no lip).");
                }
                else FlowTrace.Step("WorldMerge", "'" + PlinthName + "' not found — nothing to disable.");
            });
        }

        // --------------------------------------------------------------------
        //  STRUCTURAL STAIR REMOVAL (F8-24, owner "two sets of steps still in the air").
        //  When the owner-ruled removal gate (EditorPrefs 'castle.stairsRemoved', set by
        //  CastleWallStairsSeatFix) is armed, sweep every Dungeon_Stairs* object out of the
        //  merged scene by PREFIX — not by exact name, so the two original PrefabInstances
        //  ('Dungeon_Stairs_Stone' / '(1)') that LiftedCastleRoots never listed are caught too.
        //  Delegates to CastleWallStairsSeatFix.RemoveAllOnOpenScene (single source of truth
        //  for the match rule + gate). Runs on the merge path so a re-bake can't resurrect them.
        // --------------------------------------------------------------------
        private static void RemoveStairsIfRuled(Scene mergedScene)
        {
            Guard.Try("WorldMerge", "structural stair sweep (owner removal ruling)", () =>
            {
                if (!EditorPrefs.GetBool("castle.stairsRemoved", false))
                {
                    FlowTrace.Step("WorldMerge", "stair removal ruling NOT armed (EditorPrefs castle.stairsRemoved unset) — stairs left as-is.");
                    return;
                }
                int removed = CastleWallStairsSeatFix.RemoveAllOnOpenScene(mergedScene);
                Debug.Log("[StairsSweep] removed " + removed + " stair object(s) from " + MergedScenePath);
                FlowTrace.Step("WorldMerge", "structural stair sweep destroyed " + removed +
                    " Dungeon_Stairs* object(s) — removal ruling enforced in the merged scene.");
            });
        }

        // --------------------------------------------------------------------
        //  STRIP SEAM REMNANTS (owner F8 2026-07-04) — the merged world has no moat/seam, so bridge
        //  geometry (VESTIGIAL + its stone-mesh collider = an invisible wall beside the hero) and the
        //  cross-seam NavMeshLinks baked into the old castle scene are OBSOLETE. Destroy them so the ground
        //  is clean and nothing blocks movement. Guarded + logged (never silently leaves cruft).
        // --------------------------------------------------------------------
        private static void StripSeamRemnants()
        {
            Guard.Try("WorldMerge", "strip seam remnants (bridges + dangling navlinks + moat)", () =>
            {
                string[] prefixes = {
                    "NavLink_CastleToOuterWorld", "RuntimeSeam_Bridge", "RuntimeSeam_Deck",
                    "Drawbridge", "Bridge_Medieval", "CastleMoat", "MoatWater",
                    "WorldGate_ConnectToOuterWorld",
                    // P0 hero-confinement (owner 2026-07-04, WO-608): the OLD moat baked a
                    // WO468_CastleNavHole_NotWalkable NavMeshModifierVolume (area 1) at the castle
                    // footprint; left in the merged scene it carves a NotWalkable hole at r~44 that
                    // confines the NavMeshAgent hero to a ~10m disc. Name-prefix strip (suspenders);
                    // StripNotWalkableCarves() below is the area==1 belt.
                    "WO468_CastleNavHole", "CastleNavHole",
                    // owner F8 2026-07-04 (2nd pass — the ARCH the hero walks through): the overnight
                    // castle->outerworld seam-crossing structure + the raised plinth dais are obsolete in
                    // the flat merged world (no seam, no raise). Destroy them so the ground is truly flat.
                    "SeamlessOuterWorldSeam", "CastleBasePlinth"
                };
                var doomed = new System.Collections.Generic.List<GameObject>();
                foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (go == null) continue;
                    foreach (var p in prefixes)
                        if (go.name.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) { doomed.Add(go); break; }
                }
                foreach (var go in doomed)
                {
                    if (go == null) continue;   // Unity fake-null after a parent was already destroyed
                    FlowTrace.Step("WorldMerge", "strip: destroying seam remnant '" + go.name + "'.");
                    UnityEngine.Object.DestroyImmediate(go);
                }
                FlowTrace.Step("WorldMerge",
                    "STRIP SEAM REMNANTS: destroyed " + doomed.Count + " bridge/navlink/moat object(s) — merged ground clean, no invisible barrier.");
            });

            // Belt to the name-prefix suspenders above: destroy every NotWalkable carve by AREA, not name.
            StripNotWalkableCarves();
        }

        // --------------------------------------------------------------------
        //  STRIP NOTWALKABLE MOAT CARVES (P0 hero-confinement, owner 2026-07-04, WO-608).
        //  The old moat baked a WO468_CastleNavHole_NotWalkable NavMeshModifierVolume (m_Area==1)
        //  at the castle footprint (verified in Main_Castle_Overworld.unity: object "WO468_CastleNavHole
        //  _NotWalkable" carrying a NavMeshModifierVolume). Left in place it carves a NotWalkable hole at
        //  r~44 that walls the NavMeshAgent hero into a ~10m disc on all sides. The merged world has NO
        //  moat, so ANY NotWalkable volume is obsolete: destroy every NavMeshModifierVolume with area==1
        //  (robust — pose/name-independent) BEFORE the bake reads it, so the navmesh bakes continuous from
        //  the castle floor out to the terrain. Guarded + logged; runs in both merge + bake entry points.
        // --------------------------------------------------------------------
        private static void StripNotWalkableCarves()
        {
            Guard.Try("WorldMerge", "strip NotWalkable moat carves (NavMeshModifierVolume area==1)", () =>
            {
                const int NotWalkableArea = 1;   // Unity built-in NotWalkable navmesh area index
                var doomed = new List<GameObject>();

                foreach (var vol in UnityEngine.Object.FindObjectsByType<NavMeshModifierVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (vol == null) continue;
                    if (vol.area == NotWalkableArea && vol.gameObject != null && !doomed.Contains(vol.gameObject))
                        doomed.Add(vol.gameObject);
                }

                foreach (var go in doomed)
                {
                    if (go == null) continue;
                    FlowTrace.Step("WorldMerge", "strip: destroying NotWalkable carve '" + go.name +
                        "' (NavMeshModifierVolume m_Area==1) — no r~44 moat hole to confine the hero.");
                    UnityEngine.Object.DestroyImmediate(go);
                }

                FlowTrace.Step("WorldMerge", "STRIP NOTWALKABLE CARVES: destroyed " + doomed.Count +
                    " NotWalkable navmesh volume(s) — merged navmesh bakes continuous (no moat carve at r~44).");
            });
        }

        // --------------------------------------------------------------------
        //  DECORATIVE bridges — call CastleMoatBuilder.BuildBridgesOnly() via REFLECTION
        //  (DeNelle.Editor does not reference DeNelle.Village — mirror the codebase's
        //  cross-assembly reflection pattern). castle.liftY=0 so the bridges are placed FLAT
        //  (no pitch seat, no deck collider) — pure decoration that does NOT affect the navmesh.
        // --------------------------------------------------------------------
        private static void PlaceDecorativeBridges()
        {
            Guard.Try("WorldMerge", "place decorative bridges (CastleMoatBuilder.BuildBridgesOnly)", () =>
            {
                // Force flat bridges for THIS bake, then restore the pref (editor-registry hygiene).
                bool hadLift = PlayerPrefs.HasKey("castle.liftY");
                float prevLift = PlayerPrefs.GetFloat("castle.liftY", 3f);
                PlayerPrefs.SetFloat("castle.liftY", 0f);
                try
                {
                    // Defensive: if a moat root somehow already exists (stale runtime-baked water), remove it
                    // so BuildBridgesOnly's idempotent Find() doesn't early-return and leave water in the scene.
                    var stale = GameObject.Find("CastleMoat");
                    if (stale != null)
                    {
                        FlowTrace.Step("WorldMerge", "removing pre-existing 'CastleMoat' root before placing bridges-only (no leaked water).");
                        UnityEngine.Object.DestroyImmediate(stale);
                    }

                    var t = ResolveType("DeNelle.Village.World.CastleMoatBuilder");
                    if (t == null)
                    {
                        FlowTrace.Warn("WorldMerge", "CastleMoatBuilder type not found — bridges SKIPPED (decorative only; bake still valid).");
                        return;
                    }
                    var m = t.GetMethod("BuildBridgesOnly", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (m == null)
                    {
                        FlowTrace.Warn("WorldMerge", "CastleMoatBuilder.BuildBridgesOnly() not found — bridges SKIPPED.");
                        return;
                    }
                    m.Invoke(null, null);
                    FlowTrace.Step("WorldMerge", "CastleMoatBuilder.BuildBridgesOnly() invoked (castle.liftY=0 -> flat decorative bridges, no deck collider).");
                }
                finally
                {
                    if (hadLift) PlayerPrefs.SetFloat("castle.liftY", prevLift);
                    else PlayerPrefs.DeleteKey("castle.liftY");
                    PlayerPrefs.Save();
                }
            });
        }

        // --------------------------------------------------------------------
        //  DEDUPE — remove duplicate singleton components from the moved OuterWorld roots.
        // --------------------------------------------------------------------
        private static int DedupeMovedSingletons(List<GameObject> movedRoots, bool killCamera, bool killAudio,
            bool killEvent, bool killDirLight)
        {
            int removed = 0;
            foreach (var root in movedRoots)
            {
                if (root == null) continue;
                Guard.Try("WorldMerge", "dedupe singletons under '" + root.name + "'", () =>
                {
                    if (killCamera)
                        foreach (var c in root.GetComponentsInChildren<Camera>(true))
                            if (c != null) { UnityEngine.Object.DestroyImmediate(c); removed++; }
                    if (killAudio)
                        foreach (var a in root.GetComponentsInChildren<AudioListener>(true))
                            if (a != null) { UnityEngine.Object.DestroyImmediate(a); removed++; }
                    if (killDirLight)
                        foreach (var l in root.GetComponentsInChildren<Light>(true))
                            if (l != null && l.type == LightType.Directional) { UnityEngine.Object.DestroyImmediate(l); removed++; }
                    if (killEvent)
                    {
                        foreach (var es in root.GetComponentsInChildren<EventSystem>(true))
                            if (es != null) { UnityEngine.Object.DestroyImmediate(es); removed++; }
                        foreach (var im in root.GetComponentsInChildren<BaseInputModule>(true))
                            if (im != null) { UnityEngine.Object.DestroyImmediate(im); removed++; }
                    }
                });
            }

            // Clean up any moved root that is now an EMPTY husk (only a Transform, no children) — e.g. a
            // bare "Main Camera" / "EventSystem" / "Directional Light" GO whose only component we removed.
            foreach (var root in movedRoots)
            {
                if (root == null) continue;
                if (root.transform.childCount == 0 && root.GetComponents<Component>().Length <= 1)
                {
                    string n = root.name;
                    UnityEngine.Object.DestroyImmediate(root);
                    removed++;
                    FlowTrace.Step("WorldMerge", "removed empty husk GameObject '" + n + "' (its only component was a deduped singleton).");
                }
            }
            return removed;
        }

        // --------------------------------------------------------------------
        //  Helpers.
        // --------------------------------------------------------------------
        private static bool SceneHasComponent<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root != null && root.GetComponentInChildren<T>(true) != null) return true;
            return false;
        }

        private static Light FindDirectionalLight(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                    if (l != null && l.type == LightType.Directional) return l;
            }
            return null;
        }

        private static string RootNames(List<GameObject> roots)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(roots[i].name);
            }
            return sb.Length > 0 ? sb.ToString() : "<none>";
        }

        private static void EnsureInBuildSettings(string scenePath)
        {
            Guard.Try("WorldMerge", "ensure merged scene in Build Settings", () =>
            {
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (scenes.Exists(s => s.path == scenePath))
                {
                    FlowTrace.Step("WorldMerge", "'" + scenePath + "' already in Build Settings.");
                    return;
                }
                scenes.Add(new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                FlowTrace.Step("WorldMerge", "added '" + scenePath + "' to Build Settings (enabled) so it can load by name at runtime.");
            });
        }

        private static Type ResolveType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
