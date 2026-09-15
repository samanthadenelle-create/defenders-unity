using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;          // NavMeshLink (direct) — DeNelle.Editor asmdef references this (EnemyStrongholdBuilder proves it compiles)
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;     // FlowTrace / Guard — TGVRU (CLAUDE.md §12), same as EnemyStrongholdBuilder
using DeNelle.Village;              // VisualFactory / SkinOptions — upright LocalRotation BEFORE Fit

namespace DeNelle.Editor
{
    // =============================================================================
    // CastleHubBuilder — Central Castle Hub (CastleHubRoot) scene automation.
    // -----------------------------------------------------------------------------
    // Run from: Defenders > Scenes > Build CastleHub_MainKeep
    //
    // Designed to run AFTER you create a blank/empty scene (File > New Scene > Empty).
    // Requires a new empty scene; refuses to overwrite a populated or saved castle.
    //
    // OwnerCastleStorefrontLayout supplies the approved fourteen-object ring, including
    // RealmStore and Cathedral of Learning. RealmStorePlacer preserves that authored door.
    //
    // Implements the Central Castle Hub spec:
    // - Separate scene for home + hub (distinct from Village2 primary).
    // - Beautifully designed castle using Quaternius (modular beauty, walls/floors/stairs/props)
    //   + polyperfect Low Poly Ultimate Pack _M tier (performant, single-atlas, mobile friendly).
    // - 4 corner towers + main south gate (connection marker to OuterWorld). ⛔ NO OUTER
    //   DEFENSIVE WALLS — owner ruling 2026-09-14 (WO-1711 B): walls belong to a base that has
    //   FLIPPED into a raid target, not to the home castle. This line read "Outer defensive
    //   walls + 4 corner towers" until that ruling; the wall runs it described are deleted at
    //   BuildCastleHub's perimeter block, which explains why they are gone.
    // - Central courtyard with the owner's thirteen saved objects plus the original
    //   Cathedral, preserving recipe geometry, relative transforms, and semantic markers.
    // - Main Keep (Castle_Medieval) with Player Hero Hall / Personal Quarters (home space).
    // - Upper battlements (wide ~42m platform at height, 4 defensive towers, LOS down to courtyard
    //   for player-placed defenses via existing base-building system).
    // - Stairs/ramps for 2-level access.
    // - Connection point south for additive load with OuterWorld + NavMeshLink (bake NavMesh after).
    // - Space/comments for roaming NPCs/animals (Bella), mobile touch interaction points,
    //   integration with Economy/Yarn/NPCUpgradeStation/BuildModeController/WorldSceneLoader.
    // - Low-poly from packs → good mobile perf (static batch, LODs, URP lighting).
    //
    // After run:
    //   1. Save the scene under Assets/Scenes/ (e.g. MainCastle_Hall.unity or CastleHub_MainKeep.unity).
    //   2. Window > AI > Navigation (or add NavMeshSurface + bake for modern multi-scene use; include upper level).
    //   3. Add NavMeshLink at south gate for adjacency to OuterWorld (position root accordingly).
    //   4. Wire the 8 NPC_*_Interactable points with existing systems (Yarn dialogue for shops,
    //      NPCUpgradeStation for blacksmith/forge, pet roaming for Echo Hollow, etc.).
    //   5. Add SmartMobileCamera + Lean Touch setup for overview/follow + touch nav.
    //   6. Optional: extend WorldSceneLoader or add a gate trigger for seamless transition.
    //
    // Packs used (read docs before editing):
    // - polyperfect: Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Medieval_M/...
    // - Quaternius:  Assets/Quaternius/Medieval Village MegaKit/Modules/Prefabs/(Wall|Prop|...)/...
    // See: docs/INSTALLED_PACKS_INDEX.md, POLYPERFECT_NOTES.md, QUATERNIUS_NOTES.md,
    //      polyperfect-asset-catalog.md.
    //
    // No .unity hand-edits. Matches VillageSceneBuilder pattern (Editor-only, menu driven,
    // PrefabUtility.InstantiatePrefab, graceful miss handling).
    // =============================================================================
    public static class CastleHubBuilder
    {
        private const string MenuPath = "Defenders/Scenes/Build CastleHub_MainKeep";
        private const string RootName = "CastleHubRoot";
        public const string OwnerLayoutPrefabPath = "Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab";
        private const string NavFloorName = "NavMeshFloor_Invisible_Walkable";

        // WO-593 castle-island raise height. A tunable VARIABLE (owner directive: NOT a const) so the
        // castle can be raised/lowered without a recompile — set PlayerPrefs "castle.liftY" then re-bake.
        // Default 3. Applied at EVERY authoring site (walls, floor, nav, Heart, gate strips, inner ring,
        // contents) — a root move alone strands the loose CastleSide_* walls and the world-space
        // .position sites re-stamp at y=0 on the next rebake. One value, reproduced by every regen.
        public static float CastleFootprintLiftY = 3f;

        /// <summary>Refresh the island-raise height from PlayerPrefs "castle.liftY" (default 3).
        /// Called at the top of every build/bake entry so the tuned value drives the whole footprint.</summary>
        private static void LoadFootprintLiftY()
        {
            CastleFootprintLiftY = PlayerPrefs.GetFloat("castle.liftY", 3f);
        }

        // WO-593 base PLINTH — a raised stone platform that IS the "castle base = footprint". It fills
        // the gap under the raised castle so it sits on solid ground instead of floating above the y=0
        // terrain, and its outer face is the inner moat wall (the moat water plane at -0.4 laps against
        // it). GEOMETRY only — NO terrain regen. Top rides CastleFootprintLiftY; sides drop past the
        // waterline. Footprint just inside the square moat ring (MoatCentreRadius ~46). (WO-594 will
        // make PlinthHalf a measured value instead of this const.)
        private const float PlinthHalf     = 44f;   // just inside the moat (46); covers the +-42 walls + lip
        private const float PlinthBottomY  = -3f;    // below WaterY (-0.4) so the moat reads against the base

        private static void BuildBasePlinth(Transform parent)
        {
            var existing = GameObject.Find("CastleBasePlinth");
            if (existing != null) Object.DestroyImmediate(existing);

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "CastleBasePlinth";
            if (parent != null) plinth.transform.SetParent(parent, false);

            float topY    = CastleFootprintLiftY;
            float centreY = (topY + PlinthBottomY) * 0.5f;
            float height  = topY - PlinthBottomY;
            plinth.transform.position   = new Vector3(0f, centreY, 0f);   // WORLD absolute (root offset must not double it)
            plinth.transform.rotation   = Quaternion.identity;
            plinth.transform.localScale = new Vector3(PlinthHalf * 2f, height, PlinthHalf * 2f);

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh != null)
            {
                var mat = new Material(sh) { name = "CastleBasePlinth_Stone" };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.57f));
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     new Color(0.55f, 0.55f, 0.57f));
                var r = plinth.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = mat;
            }
            Log("WO-593: base plinth built — top y=" + topY + ", half=" + PlinthHalf +
                " (fills the gap under the raised castle; moat wall; no terrain regen).");
        }

        // Pack roots (validated against catalogs + on-disk)
        private const string PolyRoot =
            "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Medieval_M/";
        private const string QuatRoot =
            "Assets/Quaternius/Medieval Village MegaKit/Modules/Prefabs/";

        [MenuItem(MenuPath)]
        public static void BuildCastleHub()
        {
            // Owner 2026-09-13: existing castle layouts are authored work, never regeneration input.
            var current = SceneManager.GetActiveScene();
            if (current.rootCount != 0)
                throw new System.InvalidOperationException("Castle generation requires a new empty scene. Existing scene objects are preserved.");
            if (current.GetRootGameObjects().Any(g => g.GetComponentsInChildren<Transform>(true)
                .Any(t => t.name == RootName || t.GetComponent<AuthoredCastleStorefront>() != null)))
                throw new System.InvalidOperationException("Existing castle preserved. Use OwnerCastleLayoutRepair for additive repairs; create a new empty scene for generation.");
            var ownerLayout = AssetDatabase.LoadAssetAtPath<GameObject>(OwnerLayoutPrefabPath);
            if (ownerLayout == null || ownerLayout.transform.childCount != 14)
                throw new System.InvalidOperationException("Validated owner castle layout prefab is absent/incomplete. Run OwnerCastleLayoutRepair.Apply first.");

            LoadFootprintLiftY();   // WO-593: refresh the base height from PlayerPrefs before authoring
            var root = new GameObject(RootName);
            root.transform.position = new Vector3(0f, CastleFootprintLiftY, 0f);   // WO-593 island raise (base)

            Debug.Log("[CastleHubBuilder] Building Central Castle Hub (CastleHubRoot) from Quaternius + polyperfect packs...");

            // === Load prefabs (null = will skip or placeholder) ===
            GameObject castleCore   = LoadPoly("Castle_Medieval.prefab");
            GameObject towerRound   = LoadPoly("Tower_Castle_Round.prefab");
            GameObject towerSquare  = LoadPoly("Tower_Castle_Square.prefab");
            GameObject towerBig     = LoadPoly("Tower_Medieval_Big.prefab");
            GameObject wallStone    = LoadPoly("Wall_Medieval_Stone.prefab");
            GameObject gateMedium   = LoadPoly("Gate_Medieval_Medium.prefab");
            GameObject drawbridge   = LoadPoly("Drawbridge_Medieval.prefab");
            GameObject stairsStone  = LoadPoly("Stairs_Medieval_Stone.prefab");

            // 8 structures sources
            GameObject houseMed     = LoadPoly("House_Medieval_Medium.prefab");
            GameObject houseLarge   = LoadPoly("House_Medieval_Large.prefab");
            GameObject houseSmall   = LoadPoly("House_Medieval_Small.prefab");
            GameObject windmill     = LoadPoly("Windmill_Medieval.prefab");
            GameObject watermill    = LoadPoly("Watermill_Medieval.prefab");
            GameObject stables      = LoadPoly("Stables_Medieval.prefab");
            GameObject marketStand  = LoadPoly("Marketplace_Stand_Simple.prefab");

            // Quaternius modular beauty pieces (for walls, floors, stairs, vines, details)
            GameObject qWallStraight = LoadQuat("Wall/Wall_Plaster_Straight.prefab");
            GameObject qStairsExt    = LoadQuat("Prop/Stairs_Exterior_Straight.prefab");
            GameObject qFloorWood    = LoadQuat("Wall/Floor_WoodDark.prefab"); // floors live under Wall/ in this pack tree
            GameObject qVine1        = LoadQuat("Prop/Prop_Vine1.prefab");
            GameObject qCrate        = LoadQuat("Prop/Prop_Crate.prefab");
            GameObject qBalcony      = LoadQuat("Prop/Balcony_Simple_Straight.prefab");

            // === OUTER WALLS + TOWERS + BATTLEMENTS (defensive perimeter) ===
            var wallsRoot = new GameObject("OuterWalls_Towers_Battlements");
            wallsRoot.transform.SetParent(root.transform, false);

            // 4 corner towers (polyperfect round for silhouette)
            if (towerRound != null)
            {
                Vector3[] corners = {
                    new Vector3(-42, 0, -42), new Vector3(42, 0, -42),
                    new Vector3(-42, 0, 42),  new Vector3(42, 0, 42)
                };
                for (int i = 0; i < corners.Length; i++)
                {
                    var t = (GameObject)PrefabUtility.InstantiatePrefab(towerRound);
                    t.transform.SetParent(wallsRoot.transform, false);
                    t.transform.localPosition = corners[i];
                    t.name = $"CornerTower_{i+1}";
                }
            }

            // ⛔ NO PERIMETER WALLS ON THE HOME CASTLE — owner ruling 2026-09-14 (WO-1711
            // ruling B), verbatim: "let's remove adding the walls let's only add walls when
            // they get to a player flipped base. I think it makes more sense."
            //
            // WALLS ARE A RAID-ARENA CONCEPT. A home town is not a defended perimeter; a town
            // that has FLIPPED into a raid target is. That conversion is the existing raid-base
            // pipeline (RaidBaseGenerator / RaidBaseDresser / ArenaBoundaryRing / RaidNavBake,
            // WO-1703/1704), which builds its own walls for the RaidBase_* / OwnedTown_* scenes
            // and is deliberately untouched here.
            //
            // WHAT WAS REMOVED: two poly-stone runs (Wall_South_-3..3 skipping the gate cell,
            // Wall_North_-3..3) and two Quaternius plaster lines (QWall_West_0..6,
            // QWall_East_0..6) — 26 wall segments. The `wallStone` and `qWallStraight` prefab
            // loads above are KEPT: other authoring entry points in this file still take
            // `wallStone` (BuildInnerWallRing_RETIRED / BuildRingSideRun / MeasureWallBaseLength
            // and the two batch rebuild paths). `qWallStraight` is now unused BY THIS METHOD and
            // is deliberately still loaded: LoadQuat is the pack-presence probe that warns when
            // the Quaternius kit is missing, and deleting a load to tidy a now-unused local is
            // how a sibling path silently loses its prefab.
            // The CoC inner courtyard ring needs no removal here — BuildInnerWallRing (:705) was
            // already made DESTROY-ONLY by the owner's own F8s ("you added walls inside, not at
            // the gates", 2026-06-27 / "Wall in middle?", 2026-07-02), so the call left below
            // only clears a stray ring and rebuilds nothing. That call STAYS for exactly that
            // reason: it is the thing that keeps the rejected ring from coming back.
            //
            // ⚠ AND READ THIS BEFORE BELIEVING THE CASTLE LOST ITS WALLS. These runs were the
            // LEGACY PLACEHOLDER SHELL, not the shipping geometry — the T-007 comment that used
            // to sit here said so itself: "the SHIPPING walls come from
            // CastleWallsFromRecipe.Recreate() (the recipe-driven CastleSide_* geometry;
            // outside this method's legacy path)". Verified at source 2026-09-14:
            // Main_Castle_Overworld.unity contains CastleSide_North/East/South/West and ZERO
            // objects named OuterWalls_Towers_Battlements / Wall_South_* / QWall_*, and
            // BuildCastleHub() throws on any non-empty scene, so it has never authored the
            // shipped hub. REMOVING THESE LOOPS THEREFORE CHANGES NOTHING THE PLAYER SEES.
            // The walls she is looking at are baked CastleSide_* scene objects and stripping
            // them needs an editor tool + a re-bake — see the WO-1711 RESULT for why that half
            // is NOT done here and what has to be ruled on first (the CastleSide_* roots carry
            // the gates, the OuterWorld exit seam and the Gate_* nav markers, which is exactly
            // the "load-bearing for nav" case WO-1711 section 2 says to confirm with the owner).
            //
            // The 4 corner towers, the south gate and the drawbridge below are KEPT: the ruling
            // removes walls, towers are the one thing that stays buildable (ruling A), and the
            // gate/drawbridge are the OuterWorld connection seam, not perimeter defence.

            // Main South Gate + Drawbridge (connection to OuterWorld)
            if (gateMedium != null)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(gateMedium);
                g.transform.SetParent(wallsRoot.transform, false);
                g.transform.localPosition = new Vector3(0, 0, -50);
                g.name = "MainGate_South_ToOuterWorld";
            }
            if (drawbridge != null)
            {
                var d = (GameObject)PrefabUtility.InstantiatePrefab(drawbridge);
                d.transform.SetParent(wallsRoot.transform, false);
                d.transform.localPosition = new Vector3(0, 0, -58);
                d.name = "Drawbridge_Approach";
            }

            // === CENTRAL COURTYARD / PLAZA (mobile-friendly open space) ===
            var courtyard = new GameObject("CentralCourtyard_Plaza");
            courtyard.transform.SetParent(root.transform, false);

            // Owner 2026-09-12: do NOT spawn courtyard floor tiles or Courtyard_PlazaFallback.
            // The injector's 80x80 plane (same name) overwrote the ring. Grass is the baked
            // ExteriorTerrain. CentralCourtyard_Plaza stays as a folder for The8Structures only.
            // if (qFloorWood != null) { ... CourtyardFloor_x_z ... }
            // else { Plane named Courtyard_PlazaFallback scale 8 }

            // === THE 8 STRUCTURES (ring around plaza, storefronts face center, NPC points) ===
            // Reproduce the owner's saved selection, poses, material repairs, and capabilities.
            // The prefab is captured by OwnerCastleLayoutRepair, preserving original source references.
            var structuresRoot = (GameObject)PrefabUtility.InstantiatePrefab(ownerLayout, current);
            structuresRoot.transform.SetParent(courtyard.transform, false);
            structuresRoot.name = "The8Structures_Storefronts_NPCPoints";

            // === MAIN KEEP + PLAYER HOME (2 levels: ground hall + upper private quarters) ===
            var keepRoot = new GameObject("MainKeep_CastleWithTwoLevels_Home");
            keepRoot.transform.SetParent(root.transform, false);

            // OWNER 2026-06-12: the keep PREFAB + its interior floor are REMOVED (owner deleted them
            // in the scene). The keepRoot, home space, and HeroStartPoint below are KEPT (the hero
            // spawns under them). castleCore is no longer instantiated here.
            _ = castleCore;

            // Labeled home space (ground + upper access). Dress further with Quaternius floors/walls + KayKit furniture bits.
            var home = new GameObject("PlayerHeroHall_PersonalQuarters_HomeSpace");
            home.transform.SetParent(keepRoot.transform, false);
            home.transform.localPosition = new Vector3(0, 0, -12);

            // Hero start point inside the personal quarters — this becomes the project start location
            // when we move the entry point from Village2 to the Castle Hub.
            var heroStart = new GameObject("HeroStartPoint_InsidePersonalQuarters");
            heroStart.transform.SetParent(home.transform, false);
            heroStart.transform.localPosition = new Vector3(0, 0.1f, 0);
            heroStart.transform.localRotation = Quaternion.Euler(0, 0, 0);

            // === SINGLE-LEVEL PIVOT (owner 2026-06-12): NO UPPER BATTLEMENTS / SECOND TIER ===
            // The wide elevated platform (y=11), its crenellation walls, the 4 upper defensive
            // towers, and the home-accent balcony are REMOVED. Reasons (owner + playtest flags):
            //   • "AI struggled to get to 2nd level" — the elevated platform fragmented the nav.
            //   • "tree shows through floor of level 2" — the Heart tree poked through the platform.
            // The castle is now a flat, single-layer CoC-style base: everything walkable at y≈0,
            // one connected nav plane through the gates to the Heart. Defensive interest comes from
            // the CONCENTRIC WALL RINGS (outer + inner), not verticality. The unused upper-only
            // prefabs (towerBig for upper towers, qBalcony) are suppressed below.
            _ = towerBig; _ = qBalcony;

            // === GRAND STAIR — REMOVED (no second level to climb to) ===
            // BuildGrandStair is now an idempotent teardown (RemoveSecondLevel): it destroys any
            // pre-existing upper-tier elements baked into the saved scene and builds NOTHING new.
            // The old decorative QuaterniusStairs_UpperAccess + MainStairs_Poly are also stripped
            // by RemoveSecondLevel. Suppress unused-local warnings for the now-unused upper props.
            BuildGrandStair(root.transform);
            _ = qStairsExt;

            // === CoC-STYLE SECOND INNER WALL RING (owner: "second inner wall like coc") ===
            // A concentric ground-level wall ring INSIDE the outer perimeter, around the Heart/
            // core, with 4 gate gaps aligned to the outer gates so enemies path outer-gate ->
            // inner-gate gap -> Heart. NOT a second level — a ground obstacle. Built after the
            // outer walls so it shares the same Wall_Medieval_Stone prefab/material.
            BuildInnerWallRing(wallsRoot.transform, wallStone);

            // === BEAUTY + DEFENSIVE DRESSING (vines from Quaternius) ===
            if (qVine1 != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    var v = (GameObject)PrefabUtility.InstantiatePrefab(qVine1);
                    v.transform.SetParent(wallsRoot.transform, false);
                    v.transform.localPosition = new Vector3(-38 + i * 11f, 4f, -40);
                    v.name = $"VineDecor_{i}";
                }
            }
            _ = qBalcony; // balcony accent removed with the second level

            // === CONNECTION TO OUTERWORLD (NavMesh seam + player transition) ===
            // South gate seam between Castle Hub (home) and OuterWorld regions.
            // - NavMeshLink: AI can path across when both scenes additive.
            // - SceneTransitionTrigger: on player cross, ensures OuterWorld loaded additive + teleports hero to target pos.
            // Alignment tip: When loading CastleHub additive, position its root so the marker's world pos
            // matches the desired "castle approach" point in OuterWorld (e.g. place OuterWorld root at (0,0,0)
            // and set Castle root offset, or vice versa). The marker local pos is south of hub.
            var gateMarker = new GameObject("WorldGate_ConnectToOuterWorld_Marker");
            gateMarker.transform.SetParent(root.transform, false);
            gateMarker.transform.localPosition = new Vector3(0, 0, -68);

            // Actually wire the components (NavMeshLink + trigger with the new SceneTransitionTrigger).
            // Also exposed via the one-off menu for already-saved scenes (e.g. your MainCastle_Hall).
            WireOuterWorldConnection(gateMarker);

            // NOTE: outposts are NOT reached by walk-to gate seams (retired — CANON_GROUND_TRUTH
            // 2026-06-28 / WO-584). They are entered via cave -> loading-zone -> OutpostResolver.
            // The old WireOutpostConnectors(W/N/E) machinery has been removed (it was inert behind
            // ff.outposttravel which defaults OFF). RemoveInTownOutpostConnectors() still strips any
            // stale OutpostConnector_* baked into older saved scenes.

            // === ROAMING / AMBIENCE (Bella + NPCs) ===
            var ambience = new GameObject("RoamingNPCs_Animals_Bella_Ambience");
            ambience.transform.SetParent(root.transform, false);
            // Populate later with People_M / Animals_M from polyperfect or existing injectors (StoryCompanionInjector etc.).
            // Echo Hollow (stables) gets extra radius for pet roaming.

            // === LIGHTING / PERF HINTS (do not add heavy lights here — use scene lighting) ===
            // All pieces are low-poly single-atlas or URP ShaderGraph → excellent batching.
            // After placing: mark static where appropriate, add light probes / reflection probes for upper/lower contrast.

            // GROK_BRIEF 2026-08-19 / owner: upright axis-baked Tripo storefronts (X=90, Y=ground)
            // and flag catalog rows fixed — MUST run BEFORE the nav floor / bake so footprints shrink
            // into the mesh the navmesh will see. Do NOT skip this; a bake of lying-down placeholders
            // locks the oversized claim into the scene.
            // Owner layout already carries verified pose/material corrections. Never open another scene here.

            // Invisible, continuous walkable NavMesh floor (interior + gate bridge) so the
            // NavMeshAgent hero can traverse the WHOLE castle and cross the gate. The visual
            // qFloorWood tiles only cover the central ~±16 plaza; this fills the rest.
            BuildNavMeshFloor(root.transform);
            BuildBasePlinth(root.transform);   // WO-593: raised stone base fills the gap under the castle (no terrain regen)

            // T-002/T-005: Heart of Elarion — the enemy target + lose condition. Without it
            // WaveManager._heart is null and Enemy.DriveNav() early-returns (enemies freeze /
            // "no enemy engagement"). Clean scale-1 anchor at the north-centre plaza.
            WireCastleHeart(root.transform);

            // WO-1711 ruling B (owner 2026-09-14): NO perimeter walls and NO inner ring on the
            // home castle — walls are seeded only when a town flips into a raid target.
            Debug.Log("[CastleHubBuilder] CastleHubRoot complete (SINGLE-LEVEL). 8 structures + keep + 4 corner towers + gate/drawbridge marker placed; NO perimeter walls, NO inner CoC ring, NO second level.\n" +
                      "Next: Save under Assets/Scenes/ (e.g. MainCastle_Hall.unity), Bake NavMesh (NavMeshSurface recommended), wire NPC points + connection via existing systems (WorldSceneLoader, Economy, Yarn).");
            Selection.activeGameObject = root;
        }

        // =====================================================================
        //  OWNER UPRIGHT CORRECTIONS (GROK_BRIEF 2026-08-19) — BEFORE BAKE
        // -----------------------------------------------------------------------------
        //  Tripo storefronts with bakeAxisConversion:1 import at root (90,0,0) which
        //  VisualFactory then zeros. Owner-dialed fix is LocalRotation Euler(90,0,0)
        //  BEFORE Fit so height = 4.00 m and footprints shrink. Catalog rows flagged
        //  manual:true + euler [90,0,0]. RealmStore placed via RealmStorePlacer with
        //  the same correction. The eight bake:0 rows (lumbermill/farm/store/…) are
        //  NOT touched — negative control.
        //  Menu: Defenders > Scenes > Apply Owner Upright Structure Corrections (pre-bake)
        //  Batch: DeNelle.Editor.CastleHubBuilder.ApplyOwnerUprightCorrectionsBeforeBake
        // =====================================================================
        private static readonly (string hostName, string modelPath, float yawDeg, float pitchDeg)[] OwnerUprightSkins =
        {
            ("Blacksmith_Weapons_Storefront", "Structures/Forge",           180f, 90f),
            ("Forge_Armor_Storefront",        "Structures/armorer",         90f,  90f),
            ("Jeweler_Gems_Storefront",       "Structures/jeweler",         0f,    90f),  // owner felt: X=90 Y=0 Z=0 (render-PROVEN 2026-08-20)
            // ALL FOUR ROWS ARE +90, AND THE JEWELER IS NOT AN EXCEPTION - PROVEN, not assumed.
            // +90 and -90 about X are AABB-identical, so nothing measurable separates them; only a
            // render can. Builds/StorefrontCaps (2026-08-20, StorefrontOrientationCapture) shot the
            // jeweler at both signs from one camera: +90 is roof-up with the sign readable, -90 is
            // upside down. An earlier session inverted this row to -90 chasing an upside-down report
            // from the device; that report's real cause was the post-Fit non-uniform scaleX in
            // HubStructureVisualInjector, cleared in cd0d109b8. If the jeweler ever looks inverted
            // again, RE-RENDER IT before touching this sign - the scale/Fit path is the likelier
            // culprit and flipping the sign hides it while breaking the mesh.
            ("CastleBarracks",                "Structures/barracks",        180f, 90f),
        };

        // Catalog ids owner named + ShopAndCrafting (= workshop). armorer included (same FBX family).
        // PER-ID SIGN (the tuple carries its own eulerX) so a future per-mesh exception costs one
        // number, not a refactor - but as of 2026-08-20 every row is +90, jeweler included, and that
        // is render-proven (see OwnerUprightSkins above). Do not invert a row on a felt report alone.
        // This MUST stay in step with OwnerUprightSkins above: the baked hub object and a
        // player-PLACED copy of the same structure have to agree, or one of them is inverted.
        private static readonly (string id, float eulerX)[] OwnerUprightCatalogIds =
        {
            ("forge", 90f), ("workshop", 90f), ("jeweler", 90f), ("barracks", 90f), ("armorer", 90f),
        };

        private const string HubScenePath = "Assets/Scenes/Main_Castle_Overworld.unity";
        private const string UprightBakeOkMarker = "OWNER_UPRIGHT_PREBAKE_OK";

        [MenuItem("Defenders/Scenes/Apply Owner Upright Structure Corrections (pre-bake)")]
        public static void ApplyOwnerUprightCorrectionsBeforeBake()
        {
            Log("=== ApplyOwnerUprightCorrectionsBeforeBake START (X=90, Y=ground, flag fixed) ===");

            if (!File.Exists(HubScenePath))
            {
                Debug.LogError($"[CastleHubBuilder] hub scene missing: {HubScenePath}");
                return;
            }
            EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
            Log($"opened {HubScenePath}");

            int skinned = 0;
            foreach (var (hostName, modelPath, yawDeg, pitchDeg) in OwnerUprightSkins)
            {
                var host = GameObject.Find(hostName);
                if (host == null)
                {
                    Debug.Log($"[CastleHubBuilder] upright skip — host '{hostName}' not in scene (ok if player-placeable only).");
                    continue;
                }
                if (SkinHostUpright(host, modelPath, yawDeg, pitchDeg))
                    skinned++;
            }

            int flagged = FlagCatalogUprightFixed(OwnerUprightCatalogIds);

            // Persist host skins before RealmStorePlacer re-opens the same scene.
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            // Realm Store is scene-root — placer applies Euler(90,0,0) pre-fit and saves.
            try
            {
                RealmStorePlacer.Run();
                Log("RealmStorePlacer.Run OK (AuthoredCorrection Euler(90,0,0)).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[CastleHubBuilder] RealmStorePlacer.Run failed: {ex.Message}");
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CastleHubBuilder] {UprightBakeOkMarker} skinned={skinned} catalogFlagged={flagged}");
            Log($"=== ApplyOwnerUprightCorrectionsBeforeBake DONE — skinned={skinned}, catalogFlagged={flagged} ===");
        }

        /// <summary>
        /// Batchmode: upright corrections THEN navmesh (owner: rotate before bake).
        /// -executeMethod DeNelle.Editor.CastleHubBuilder.BakeOwnerUprightThenNavMesh
        /// </summary>
        public static void BakeOwnerUprightThenNavMesh()
        {
            ApplyOwnerUprightCorrectionsBeforeBake();
            NavMeshBakeFinal.Run();
            VerifyJewelerUprightAfterBake();
            Debug.Log("[CastleHubBuilder] OWNER_UPRIGHT_AND_NAVMESH_BAKE_OK");
        }

        /// <summary>
        /// FINAL STAGE, owner 2026-08-19: "if you can add it at the end just to flip it, then that
        /// would be absolutely perfect, and there would be no reason to rebake it."
        ///
        /// <para>Runs AFTER the navmesh bake and ASSERTS the jeweler ended up UPRIGHT (+90). If some other
        /// path left it at +90 it is corrected here and the scene re-saved, so no second bake is ever
        /// needed to get the jeweler right. Idempotent: when the skin table already produced +90 this
        /// logs and changes nothing.</para>
        ///
        /// <para>WHY THE JEWELER SPECIFICALLY: it is the one mesh in the family that bakes upside down
        /// at +90. +90 and -90 are AABB-identical, so no gate and no measurement can catch a
        /// regression here - only the screen can. This stage is the closest thing to a guard we have,
        /// so it prints the value it FOUND rather than merely announcing success.</para>
        /// </summary>
        private static void VerifyJewelerUprightAfterBake()
        {
            var host = GameObject.Find("Jeweler_Gems_Storefront");
            if (host == null)
            {
                Debug.LogWarning("[CastleHubBuilder] JEWELER_UPRIGHT_UNVERIFIED - Jeweler_Gems_Storefront " +
                                 "not in the open scene after bake; nothing checked.");
                return;
            }

            // The skinned visual is the child the injector/skin path creates, not the baked host.
            Transform visual = null;
            for (int i = 0; i < host.transform.childCount; i++)
            {
                var c = host.transform.GetChild(i);
                if (c.GetComponentInChildren<MeshRenderer>(true) != null ||
                    c.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) { visual = c; break; }
            }
            if (visual == null)
            {
                Debug.LogWarning("[CastleHubBuilder] JEWELER_UPRIGHT_UNVERIFIED - no skinned visual under " +
                                 "Jeweler_Gems_Storefront; nothing checked.");
                return;
            }

            Vector3 e = visual.localEulerAngles;
            float x = Mathf.DeltaAngle(0f, e.x);          // -180..180, so 270 reads as -90
            if (Mathf.Abs(x - 90f) < 1f)
            {
                Debug.Log($"[CastleHubBuilder] JEWELER_UPRIGHT_OK - visual '{visual.name}' localEuler=" +
                          $"({x:F1},{Mathf.DeltaAngle(0f, e.y):F1},{Mathf.DeltaAngle(0f, e.z):F1}) - already upright, no change.");
                return;
            }

            visual.localRotation = Quaternion.Euler(90f, Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[CastleHubBuilder] JEWELER_UPRIGHT_FLIPPED - found x={x:F1}, forced to +90 and re-saved. " +
                      "Something upstream wrote a different pitch for this mesh; this stage covered it.");
        }

        /// <summary>
        /// Owner felt 2026-08-19: jeweler only — Rotation X=90 Y=0 Z=0. Does not retouch other storefronts.
        /// -executeMethod DeNelle.Editor.CastleHubBuilder.BakeJewelerUprightOnly
        /// </summary>
        public static void BakeJewelerUprightOnly()
        {
            if (!File.Exists(HubScenePath))
            {
                Debug.LogError($"[CastleHubBuilder] hub scene missing: {HubScenePath}");
                return;
            }
            EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
            var host = GameObject.Find("Jeweler_Gems_Storefront");
            if (host == null)
            {
                Debug.LogError("[CastleHubBuilder] Jeweler_Gems_Storefront not in scene — nothing baked.");
                return;
            }
            if (!SkinHostUpright(host, "Structures/jeweler", yawDeg: 0f, pitchDeg: 90f))    // render-PROVEN upright 2026-08-20
                return;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            NavMeshBakeFinal.Run();
            Debug.Log("[CastleHubBuilder] JEWELER_UPRIGHT_BAKE_OK LocalRotation=(90,0,0)");
        }

        /// <summary>
        /// Replace a hub host's visual with the Tripo model, LocalRotation = (pitch, yaw, 0)
        /// BEFORE Fit/Seat so Y = ground and height hits the 4 m cadence.
        /// </summary>
        private static bool SkinHostUpright(GameObject host, string modelPath, float yawDeg, float pitchDeg)
        {
            // Strip prior skinned / placeholder mesh children (keep NPC_* interact points).
            var toDestroy = new List<GameObject>();
            for (int i = 0; i < host.transform.childCount; i++)
            {
                var c = host.transform.GetChild(i);
                if (c.name.StartsWith("NPC_")) continue;
                if (c.name.StartsWith("Anvil")) continue;
                if (c.GetComponentInChildren<Renderer>(true) != null)
                    toDestroy.Add(c.gameObject);
            }
            // Also strip renderers on the host itself (polyperfect prefab root often carries mesh).
            //
            // WO-1716 — THIS STRIP USED TO BE THE WHOLE STORY, and that is how
            // `Main_Castle_Overworld` ended up with an invisible, solid, navmesh-carving
            // `CastleBarracks`: the polyperfect MeshCollider shaped by the mesh destroyed here stayed
            // live with nothing rendering over it, and NavMeshBakeFinal.PrepareBakedTwinsForDynamicCarving
            // later sized a carving NavMeshObstacle (16.9 x 5.08 x 14.5) off that orphan. The owner
            // deleted that one object in the Editor and re-baked: the oversized hole was gone.
            //
            // The close is EnsureNoHusk, below, AFTER the new visual resolves — not here. The host's
            // collider must survive a SUCCESSFUL skin, because SkinOptions.Structure sets
            // StripColliders=true ("the host owns its collider", VisualFactory.cs:53,112,368-370):
            // clearing it here would let the player walk through every re-skinned structure.
            string stripReason = "SkinHostUpright re-skin of '" + host.name + "'";
            DeNelle.Village.World.StructureVisualStrip.StripHostVisual(host, stripReason);
            foreach (var go in toDestroy)
                Object.DestroyImmediate(go);

            var opts = SkinOptions.Structure(0f);
            opts.FitHeight = StructureFactory.YHeightVariable; // 4 m cadence
            opts.LocalRotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            opts.TraceId = host.name;

            var visual = VisualFactory.Skin(host.transform, modelPath, opts);
            if (visual == null)
            {
                // WO-1716: the strip above already ran, so the host is INVISIBLE from here on. THIS is
                // the branch that minted the CastleBarracks ghost, and it used to end at a silent
                // LogWarning. Close the pair: with nothing rendering, the host's solid mesh collision
                // and any carve sized from it must go too, or the scene keeps an invisible wall.
                DeNelle.Village.World.StructureVisualStrip.EnsureNoHusk(host, stripReason + " (Skin FAILED)");
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Hub",
                    $"upright Skin FAILED for '{host.name}' model '{modelPath}' - the host is now " +
                    "INVISIBLE (its visual was stripped first); its mesh collision + carve were " +
                    "cleared so it cannot block. Re-skin it or destroy the object (WO-1716).");
                Debug.LogWarning($"[CastleHubBuilder] upright Skin failed for '{host.name}' model '{modelPath}'.");
                return false;
            }

            // Pin host to ground Y (owner: y should always equal ground height). Local Y under
            // courtyard parent stays 0; world Y follows CastleFootprintLiftY via root.
            var lp = host.transform.localPosition;
            host.transform.localPosition = new Vector3(lp.x, 0f, lp.z);

            // WO-1716 invariant assert. A NO-OP on the normal path (the skinned visual renders, so
            // the host is not a husk and keeps the collider SkinOptions.Structure expects it to own).
            // It only fires if Skin returned a non-null visual that nevertheless renders nothing —
            // which is precisely the silent case nobody was watching for.
            DeNelle.Village.World.StructureVisualStrip.EnsureNoHusk(host, stripReason + " (post-skin assert)");

            Log($"upright skinned '{host.name}' ← {modelPath} LocalRotation=({pitchDeg},{yawDeg},0) FitHeight={opts.FitHeight:0.##}m");
            return true;
        }

        /// <summary>
        /// Write euler [90,0,0] + manual:true + corrected:true into BOTH catalog copies (byte-equal).
        /// StructureFactory.OptsFor feeds this into LocalRotation BEFORE Fit.
        /// </summary>
        private static int FlagCatalogUprightFixed((string id, float eulerX)[] ids)
        {
            string[] paths =
            {
                "Assets/StreamingAssets/Data/Canonical/structures-catalog.json",
                "Assets/Resources/Data/Canonical/structures-catalog.json",
            };
            string src = paths[0];
            if (!File.Exists(src))
            {
                Debug.LogError($"[CastleHubBuilder] catalog missing: {src}");
                return 0;
            }

            JObject root = JObject.Parse(File.ReadAllText(src));
            var entries = root["entries"] as JArray;
            if (entries == null) return 0;

            var want = new Dictionary<string, float>();
            foreach (var t in ids) want[t.id] = t.eulerX;
            int n = 0;
            foreach (var e in entries)
            {
                var entry = e as JObject;
                if (entry == null) continue;
                string id = entry.Value<string>("id");
                if (id == null || !want.TryGetValue(id, out float eulerX)) continue;

                entry["orientation"] = new JObject
                {
                    ["corrected"] = true,
                    ["manual"] = true,
                    ["euler"] = new JArray(eulerX, 0f, 0f),
                    ["offset"] = new JArray(0f, 0f, 0f),
                    ["scale"] = 1,
                    ["note"] = $"owner 2026-08-19 upright X={eulerX} flagged fixed — applied PRE-fit via StructureFactory.OptsFor LocalRotation (GROK_BRIEF). Was [0,0,0] after 2026-08-18 axis-bake pass that discarded the imported root rotation.",
                };
                n++;
                Log($"catalog '{id}' → euler [{eulerX},0,0] manual=true corrected=true");
            }

            string outText = root.ToString(Formatting.Indented);
            foreach (var p in paths)
            {
                if (!File.Exists(p)) continue;
                File.WriteAllText(p, outText);
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return n;
        }

        // =====================================================================
        //  CoC-STYLE SECOND INNER WALL RING (owner 2026-06-12: "second inner wall like coc")
        // -----------------------------------------------------------------------------
        //  A concentric, GROUND-LEVEL wall ring INSIDE the outer perimeter, around the keep +
        //  Heart core. This is the Clash-of-Clans layered-defense read: an attacker breaches the
        //  outer gate, crosses the courtyard, then must funnel through the INNER gate gap to reach
        //  the Heart. It is an OBSTACLE on the flat nav floor — NOT a second level. Enemies path
        //  AROUND the wall segments and THROUGH the 4 gate gaps (one per cardinal side, aligned to
        //  the outer S/W/N/E gates), so the single connected nav plane still reaches the Heart.
        //
        //  GEOMETRY:
        //   • Square ring, half-extent R = 18m from world origin (the keep sits at origin, the
        //     Heart at (0,0,12) — both inside; the 8 storefronts at radius 22-32 stay OUTSIDE the
        //     ring, in the courtyard band between the two walls, exactly like CoC's outer rings).
        //     ~18m is roughly half the ~44m outer perimeter (owner's "~half the outer" guidance).
        //   • Each of the 4 sides is a line of Wall_Medieval_Stone segments perpendicular to its
        //     outward cardinal, with a centered ~14m GAP for the gate opening. Segments are scaled
        //     to tile the side cleanly so the run reads as one connected wall (no seams), same
        //     prefab + material as the outer walls.
        //   • The gate-gap is achieved simply by NOT placing a segment over the center span, so the
        //     opening is open geometry (nothing to exclude-from-bake there). The wall segments are
        //     real colliders the NavMesh voxelizes as un-walkable, carving the obstacle; the gap
        //     stays walkable because no collider sits in it. (Matches the outer-wall approach where
        //     the gate is a geometric opening, not a bake-excluded solid.)
        //
        //  Idempotent: destroys any prior ring first, so a re-run rebuilds exactly one ring.
        // =====================================================================
        private const string InnerRingName = "InnerWallRing_CoC";
        private const float  InnerRingHalfExtent = 18f;   // square half-side from origin
        // SOUTH-GATE INVISIBLE-WALL FIX (2026-06-13): 8m half => 16m clear opening. Must be >= the
        // outer gate exit strip (BuildGateExitStrips halfWidth 7.5 => 15m) PLUS margin so the inner
        // ring never pinches the gate lane narrower than the strip — a 14m inner gap vs a 15m strip
        // would re-introduce the pinch the strip widening just removed. 16m clears the 15m strip + 1m.
        private const float  InnerRingGateGapHalf = 8f;

        private static void BuildInnerWallRing(Transform parent, GameObject wallStone)
        {
            // RING RETIRED (owner F8 2026-06-27 "you added walls inside, not at the gates" +
            // F8 2026-07-02 "Wall in middle?"): the concentric courtyard ring is REJECTED design.
            // The owner hand-removed it once; this builder kept resurrecting it on every batch
            // rebake (the 07-02 recurrence). This method is now destroy-only so ANY rebuild path
            // (BuildCastleHub or either batch entry) permanently clears it instead of re-adding it.
            var prior = GameObject.Find(InnerRingName);
            if (prior != null) Object.DestroyImmediate(prior);
            Log("BuildInnerWallRing: inner CoC ring RETIRED (owner F8 2026-06-27 / 2026-07-02) — prior ring " +
                (prior != null ? "removed" : "absent") + ", nothing rebuilt.");
        }

        private static void BuildInnerWallRing_RETIRED(Transform parent, GameObject wallStone)
        {
            // Original ring construction kept for reference only — NOT called anywhere.
            if (wallStone == null)
            {
                Debug.LogWarning("[CastleHubBuilder] Wall_Medieval_Stone prefab missing — inner CoC wall ring skipped (LogWarning, not fatal).");
                return;
            }

            var ring = new GameObject(InnerRingName);
            ring.transform.SetParent(parent, false);

            // Measure the base wall footprint (un-scaled X span) from a temp instance so we tile
            // the side with right-sized scaled segments (MEASURE-not-guess, like CastleWallsFromRecipe).
            float baseLen = MeasureWallBaseLength(wallStone);
            if (baseLen < 0.5f) baseLen = 13f; // safety fallback

            float R = InnerRingHalfExtent;

            // --- ALIGN INNER GATE GAPS TO THE OUTER GATES (fix: hero was stopped 17.4m out) ---
            // FlowTrace proof: the seam trigger at the perimeter gate runs, but the hero's closest
            // approach was 17.4m (trigger radius 14m) — the inner ring (half-extent ~18m) blocked
            // him. The inner gaps were CENTERED on each side (lateral 0), but the OUTER cardinal
            // gates are laterally OFFSET (south gate.x ~ -4.37), so the inner gap and the outer
            // gate / 12m exit strip did NOT line up — the hero hit inner wall instead of pathing
            // THROUGH to the seam. Fix: center each side's gap on the SAME recipe-derived lateral
            // offset the outer gates use, so courtyard -> inner gap -> exit strip -> seam is one
            // continuous walkable lane after the bake.
            //
            // Derivation (recipe, NOT hardcoded): outer gate world pos for a side =
            // Quaternion.Euler(0,yaw,0) * southGate (see BuildGateExitStrips/MakeGatePose). The
            // lateral offset of that gate along this side's 'along' axis (= rot * +X) is
            //   Dot(rot*southGate, rot*right) = Dot(southGate, Vector3.right) = southGate.x.
            // i.e. by the x4 rotational symmetry every side's lateral offset is the SAME value,
            // the recipe south gate's X. Read it from the recipe so it tracks any re-author.
            float gateLateral = ReadSouthGatePos().x;

            // 4 sides: outward cardinal + yaw so the wall faces along the side. South faces -Z.
            var sides = new (string label, float yaw)[] { ("South", 0f), ("West", 90f), ("North", 180f), ("East", 270f) };

            int placed = 0;
            foreach (var (label, yaw) in sides)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 outward = rot * Vector3.back;            // -Z rotated to this side
                Vector3 along   = rot * Vector3.right;           // +X rotated — runs along the side
                Vector3 sideCenter = outward * R;                // midpoint of this side, on the ring

                // The side spans [-R, +R] along 'along'. The gate gap is centered on gateLateral
                // (the outer gate's lateral offset), NOT on 0. Two wall runs flank that gap:
                //   left  run: from -R               to (gateLateral - gapHalf)
                //   right run: from (gateLateral + gapHalf) to +R
                // No segment spans the gap lane, so the bake leaves it walkable and the inner gap
                // sits directly in front of the outer gate / exit strip.
                // The wall prefab tiles on its local X; 'along' = rot * +X, so the segment rotation
                // is simply the side yaw (identity for South, where the wall faces ±Z as authored).
                float gapMin = gateLateral - InnerRingGateGapHalf;
                float gapMax = gateLateral + InnerRingGateGapHalf;
                BuildRingSideRun(ring.transform, wallStone, sideCenter, along, rot, -R,     gapMin, baseLen, label + "_L", ref placed);
                BuildRingSideRun(ring.transform, wallStone, sideCenter, along, rot,  gapMax, R,      baseLen, label + "_R", ref placed);
            }

            // WO-449: put the inner CoC wall ring (+ its MeshCollider children) on the "Structure"
            // layer so HeroTargetIndicator's line-of-sight linecast occludes targeting through it,
            // same as the perimeter walls. Gate GAPS are absent geometry (no segment spans them),
            // so they stay passable. Layer != navmesh — the gate-gap bake exclusion is unaffected.
            int structureLayer = LayerMask.NameToLayer("Structure");
            if (structureLayer >= 0) SetLayerRecursively(ring, structureLayer);
            else Debug.LogWarning("[CastleHubBuilder] WO-449: 'Structure' layer missing — inner ring " +
                                  "left on Default (LoS gate will degrade off for the inner ring).");

            Log($"BuildInnerWallRing: CoC inner ring built — half-extent {R}m, 4 sides, " +
                $"{InnerRingGateGapHalf * 2f}m gate gap per side centered on recipe gate lateral {gateLateral:F2}m " +
                $"(aligned to outer gates), {placed} wall segment(s). Path: courtyard -> inner gap -> outer gate strip -> seam.");
        }

        // One straight wall run along a ring side, from 'a' to 'b' (offsets along 'along' from the
        // side center), filled with a single right-sized scaled Wall_Medieval_Stone (a low-poly
        // wall scales cleanly to span any width as one connected segment). y=0 (ground obstacle).
        private static void BuildRingSideRun(Transform parent, GameObject wallStone, Vector3 sideCenter,
                                             Vector3 along, Quaternion rot, float a, float b, float baseLen, string suffix, ref int placed)
        {
            float span = b - a;
            if (span < 1f) return;
            float mid = (a + b) * 0.5f;
            Vector3 pos = sideCenter + along * mid;
            pos.y = CastleFootprintLiftY;   // WO-593: inner ring rises with the island (was world 0, re-stamped on rebake)

            var w = (GameObject)PrefabUtility.InstantiatePrefab(wallStone);
            w.name = "InnerWall_" + suffix;
            w.transform.SetParent(parent, false);
            w.transform.position = pos;
            w.transform.rotation = rot;                          // wall length axis (local X) aligns with 'along'
            w.transform.localScale = new Vector3(span / baseLen, 1f, 1f);
            placed++;
        }

        // WO-449: set this GameObject and ALL descendants (which carry the MeshColliders the LoS
        // linecast hits) onto the given layer. Recursive so child renderer/collider GOs are covered.
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        // Measure a wall prefab's un-scaled X footprint from a throwaway instance (renderer bounds /
        // scale). Used so ring side-runs tile cleanly. Cleans up the temp instance.
        private static float MeasureWallBaseLength(GameObject wallStone)
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(wallStone);
            float len = 0f;
            var rends = temp.GetComponentsInChildren<MeshRenderer>(true);
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float sx = Mathf.Abs(temp.transform.localScale.x);
                len = sx > 0.001f ? b.size.x / sx : b.size.x;
            }
            Object.DestroyImmediate(temp);
            return len;
        }

        // --- Helpers (graceful loading like VillageSceneBuilder pattern) ---
        private static GameObject LoadPoly(string fileName)
        {
            string path = PolyRoot + fileName;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
            {
                Debug.LogWarning($"[CastleHubBuilder] Missing polyperfect prefab (using placeholder behavior): {path}");
            }
            return go;
        }

        private static GameObject LoadQuat(string relative)
        {
            string path = QuatRoot + relative;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
            {
                Debug.LogWarning($"[CastleHubBuilder] Missing Quaternius prefab: {path}");
            }
            return go;
        }

        // =====================================================================
        //  T-008 — interior stone material. Reuses the EXACT palette material the exterior
        //  Wall_Medieval_Stone prefab renders with (polyperfect single-atlas palette
        //  M_21_Grey_Light_LPUP) so interior surfaces match the outside. No new art authored.
        //  Idempotent + graceful: LogWarning (not error) if the material is absent (pack not
        //  imported on this machine) and leave the default material in place.
        // =====================================================================
        private const string InteriorStoneMatPath =
            "Assets/polyperfect/Low Poly Ultimate Pack/Materials/Colors/M_21_Grey_Light_LPUP.mat";

        private static Material _interiorStoneMat;

        private static Material LoadInteriorStoneMaterial()
        {
            if (_interiorStoneMat != null) return _interiorStoneMat;
            _interiorStoneMat = AssetDatabase.LoadAssetAtPath<Material>(InteriorStoneMatPath);
            if (_interiorStoneMat == null)
                Debug.LogWarning("[CastleHubBuilder] Interior stone material not found (pack not imported?): " +
                                 InteriorStoneMatPath + " — interior left untextured.");
            return _interiorStoneMat;
        }

        private static void ApplyInteriorStoneMaterial(GameObject go)
        {
            if (go == null) return;
            var mat = LoadInteriorStoneMaterial();
            if (mat == null) return;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null) r.sharedMaterial = mat;
        }

        // Apply the interior stone material to the interior shell surfaces present in the OPEN
        // scene (the battlements platform primitive — the large flat interior surface). Used by
        // the batch rebake so the committed scene's interior is textured without a full rebuild.
        private static void ApplyInteriorStoneToScene()
        {
            var platform = GameObject.Find("BattlementsPlatform_Wide42m_ForPlayerTowers_LOS_DownToCourtyard");
            if (platform != null) { ApplyInteriorStoneMaterial(platform); Log("BATCH-RECIPE: interior stone material applied to battlements platform (T-008)."); }
            else Log("BATCH-RECIPE: battlements platform not found — interior texture pass skipped (T-008).");
        }

        // =====================================================================
        //  Invisible walkable NavMesh floor — ONE continuous collider surface so the
        //  NavMeshAgent hero can traverse the WHOLE castle and cross the gate. The
        //  visual qFloorWood tiles only cover the central ~±16 plaza, leaving the rest
        //  of the ±44 interior + the gate/drawbridge with no walkable ground -> the
        //  "fragmented islands" + uncrossable gate. These planes are renderer-OFF
        //  (invisible) but keep their MeshCollider, so the NavMeshSurface bakes them
        //  when Use Geometry = Physics Colliders. Generated, not hand-placed, so a
        //  rebuild reproduces the walkable floor every time.
        // =====================================================================
        private static void BuildNavMeshFloor(Transform parent)
        {
            // Idempotent: drop any prior generated floor first.
            var priorFloor = GameObject.Find(NavFloorName);
            if (priorFloor != null) Object.DestroyImmediate(priorFloor);

            var floorRoot = new GameObject(NavFloorName);
            floorRoot.transform.SetParent(parent, false);

            // Main interior floor (owner 2026-06-12: "make the entire zone larger, 15m+ each side
            // so it triggers the world transition"): scale 13 = 130x130m, covering ±65 — ~22m PAST
            // every gate (gates ±43) so the hero walks out far enough to trip the OuterWorld seam.
            // y=0 (gate base) + forced walkable (CreateInvisibleFloor) so the gate arch can't seal it.
            CreateInvisibleFloor(floorRoot.transform, "CourtyardFloor_Nav", new Vector3(0f, 0f, 0f), new Vector3(13f, 1f, 13f));

            // Gate bridges: ONE oriented walkable strip CENTERED ON EACH of the 4 recipe gate
            // openings (S/W/N/E), spanning from the courtyard THROUGH the opening and >=10m OUT
            // onto the OuterWorld terrain. The gates are no longer at fixed (0,0,-50): the owner
            // re-authored them via Resources/Data/castle-south-recipe.json (south gate ~(-4.37,
            // 0,-40.6)) mirrored 90/180/270 around world origin (same math as CastleWallsFromRecipe).
            // A single hardcoded (0,0,-51) bridge missed the real opening (x off by ~4.4m) and never
            // covered W/N/E at all -> uncrossable gates. Drive the strips off the recipe so a regen
            // stays correct even if the owner re-authors the gates.
            BuildGateExitStrips(floorRoot.transform);

            // WO-168 technique: exclude the gate arch meshes from the bake so they don't seal
            // the opening (they voxelize solid across it). The walkable floor stays continuous.
            ExcludeGatesFromNavBake();

            // OWNER 2026-06-12: the keep prefab was removed, so there is no keep interior to bridge.
            // The KeepInterior_Nav floor plane is removed — the main CourtyardFloor_Nav covers the
            // origin where the keep used to sit.

            // SINGLE-LEVEL PIVOT (owner 2026-06-12): NO upper-battlements nav plane. The second
            // level was removed (enemy AI could not reach it), so there is no y~11.5 walkable
            // surface to bake. The castle is one flat nav sheet at y≈0. Also strip any prior
            // UpperBattlements_Nav plane baked into the saved scene so the navmesh is clean.
            var priorUpperNav = GameObject.Find("UpperBattlements_Nav");
            if (priorUpperNav != null) { Object.DestroyImmediate(priorUpperNav); Log("BuildNavMeshFloor: removed stale UpperBattlements_Nav plane (single-layer pivot)."); }
        }

        // =====================================================================
        //  GATE EXIT STRIPS — recipe-driven walkable nav strips through all 4 gates.
        // -----------------------------------------------------------------------------
        //  Reads the SAME recipe CastleWallsFromRecipe builds the walls from
        //  (Resources/Data/castle-south-recipe.json) to find the SOUTH gate opening,
        //  then mirrors it 90/180/270 around world origin (identical rotation math:
        //  worldPos = Quaternion.Euler(0,angle,0) * southLocalPos, parent at origin) to
        //  get the W/N/E gate openings. For EACH gate it drops one invisible, renderer-off
        //  walkable plane (MeshCollider kept) oriented so its long axis points OUTWARD
        //  through the wall: it starts INSIDE the courtyard (overlapping the main
        //  CourtyardFloor_Nav so the two fuse), passes THROUGH the gate opening, and
        //  extends >=10m OUT onto the OuterWorld terrain so spawn/load/blend + the
        //  NavMeshLink endpoint land on walkable mesh. Parameterized off the recipe so a
        //  re-author of the gates keeps the strips aligned automatically.
        //
        //  Strip footprint (per gate, in the gate's own outward frame BEFORE rotation):
        //   • width  (across the opening) 15m — matches the masonry clear span (GateClearHalf)
        //   • length (courtyard -> out) ~11.4m — ~8m inside the wall to overlap the courtyard
        //     floor + outward only AS FAR AS THE PLINTH EDGE (PlinthHalf). Post-WO-593 raise the
        //     strip rides at y=liftY, so any reach past the plinth edge bakes a walkable catwalk
        //     in mid-air (F8 2026-07-02 flag_14); the descent beyond the edge belongs to the
        //     bridge/ramps + the RuntimeRegionGate sloped approach deck.
        //  A Unity Plane is 10x10m at scale 1, centered on its transform, so we size it
        //  via localScale and rotate it by the side angle so the length axis runs radially
        //  outward from origin.
        // =====================================================================
        private const string GateStripRootName = "GateExitStrips_Nav";

        private struct GatePose { public Vector3 worldPos; public float yaw; public string label; }

        private static void BuildGateExitStrips(Transform parent)
        {
            // Recipe south gate (local under a parent at origin -> world == local).
            Vector3 southGate = ReadSouthGatePos();

            // 4 sides: south is the authored side (0deg), W/N/E are it rotated around origin.
            // Match CastleWallsFromRecipe: world = Quaternion.Euler(0,angle,0) * southPos.
            var poses = new[]
            {
                MakeGatePose(southGate,   0f,   "South"),
                MakeGatePose(southGate,  90f,   "West"),
                MakeGatePose(southGate, 180f,   "North"),
                MakeGatePose(southGate, 270f,   "East"),
            };

            // SOUTH-GATE INVISIBLE-WALL FIX (2026-06-13): widen the walkable strip 12m -> 15m so
            // the BAKED nav lane through the gate is as wide as the masonry opening. The perimeter
            // seam-fill protects a 15m clear opening (GateClearHalf 7.5) and the inner ring leaves a
            // 14m gap, but this exit strip only carved a 12m walkable lane through the mouth. After
            // agent-radius erosion + the flanking-wall/seam-filler voxelization eating inward, the
            // 12m lane pinched to a sliver offset from the masonry centre -> the hero's NavMeshAgent
            // had to veer around the pinch on the way back into town ("invisible wall near the south
            // gate, walk around it", owner — seen a few times). 7.5 half-width matches GateClearHalf
            // so the nav lane fills the full opening. Strip is invisible (renderer off) — walls stay
            // visually intact; only the walk lane widens.
            const float halfWidth   = 7.5f; // 15m across the opening (matches GateClearHalf masonry clear)
            const float insideReach = 8f;   // overlap courtyard floor (inside the wall)
            // F8 2026-07-02 flag_14 "walking in the air off the bridge": the strip is stamped at
            // y=CastleFootprintLiftY (WO-593 raise), so its old 18m outward reach (to r≈58.6) baked a
            // 15m-wide WALKABLE CATWALK hovering liftY above the outer ground past the plinth edge —
            // the hero could nav-walk beside/over the bridge in mid-air and off its sides. DERIVE the
            // reach so the strip ends AT the plinth edge (r=PlinthHalf): the descent beyond it is the
            // bridge/ramps' job (CastleMoatBuilder) + the RuntimeRegionGate sloped approach.
            float outsideReach = Mathf.Max(2f, PlinthHalf - Mathf.Abs(southGate.z)); // 44 - 40.6 = 3.4m, ends at the plinth edge
            float length = insideReach + outsideReach; // ~11.4m total along the radial axis
            float centerOffset = (outsideReach - insideReach) * 0.5f; // shift strip center outward

            foreach (var g in poses)
            {
                // Outward direction in world XZ for this side (south faces -Z at yaw 0).
                Quaternion yawRot = Quaternion.Euler(0f, g.yaw, 0f);
                Vector3 outward = yawRot * Vector3.back; // south(-Z) rotated to this side

                // Center the strip between the courtyard-overlap end and the terrain end.
                Vector3 center = g.worldPos + outward * centerOffset;
                center.y = CastleFootprintLiftY; // WO-593: gate strip rises with the island (was world 0, re-stamped on rebake)

                // Plane is 10m square at scale 1: X = width axis, Z = length axis. The
                // length axis must run OUTWARD (along 'outward'); a plane's local +Z maps to
                // world via the yaw rotation, and at yaw 0 local +Z = world +Z while outward
                // = world -Z, so the |length| sizing is symmetric and orientation-agnostic.
                var strip = GameObject.CreatePrimitive(PrimitiveType.Plane);
                strip.name = $"GateExit_{g.label}_Nav";
                strip.transform.SetParent(parent, false);
                strip.transform.position = center;
                strip.transform.rotation = yawRot; // align the strip's length axis radially
                strip.transform.localScale = new Vector3(halfWidth * 2f / 10f, 1f, length / 10f);

                var r = strip.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = false;
                if (strip.GetComponent<MeshCollider>() == null) strip.AddComponent<MeshCollider>();

                // T-006: FORCE each of the 4 gate strips walkable so the gate arch (which
                // voxelizes solid across the opening) cannot carve/seal the strip on bake —
                // same protection CreateInvisibleFloor applies to the main courtyard floor.
                // Without this, a quadrant's strip could bake un-walkable -> that side's gate
                // is uncrossable even though the geometry is present.
                AddWalkableNavMeshModifier(strip);
            }
        }

        // Build a gate pose by rotating the authored south gate around world origin (parent at 0).
        private static GatePose MakeGatePose(Vector3 southGate, float angle, string label)
        {
            Vector3 world = Quaternion.Euler(0f, angle, 0f) * southGate;
            return new GatePose { worldPos = world, yaw = angle, label = label };
        }

        // Read the SOUTH gate position from the recipe (the SAME data the walls are built from),
        // so the nav strips track any re-author. Falls back to the captured default if absent.
        private static Vector3 ReadSouthGatePos()
        {
            Vector3 fallback = new Vector3(-4.37f, 0f, -40.6f);
            var ta = Resources.Load<TextAsset>("Data/castle-south-recipe");
            if (ta == null)
            {
                Debug.LogWarning("[CastleHubBuilder] castle-south-recipe not found — using fallback south gate " + fallback);
                return fallback;
            }

            // Reuse the recipe shape (same fields CastleWallsFromRecipe parses).
            var recipe = JsonUtility.FromJson<GateRecipe>(ta.text);
            if (recipe != null && recipe.pieces != null)
            {
                foreach (var p in recipe.pieces)
                {
                    if (p != null && p.name == "Gate_South" && p.pos != null && p.pos.Length == 3)
                        return new Vector3(p.pos[0], p.pos[1], p.pos[2]);
                }
            }
            Debug.LogWarning("[CastleHubBuilder] Gate_South not found in recipe — using fallback " + fallback);
            return fallback;
        }

        [System.Serializable] private class GatePiece  { public string name; public string prefab; public float[] pos; public float[] rot; public float[] scale; }
        [System.Serializable] private class GateRecipe { public GatePiece[] pieces; public float[] parentPos; public float[] parentRot; }

        // =====================================================================
        //  GRAND STAIR (WO-384) — consume the reusable DeNelle.Village.StairwayBuilder
        //  to build the REAL climbable nav path from the courtyard up to the upper
        //  battlement EDGE. Replaces the old MainStairs_Poly prefab + the invisible
        //  UpperRamp_Nav. Cross-assembly (Editor cannot reference DeNelle.Village at
        //  compile time) so we invoke the builder via reflection — same pattern as
        //  FindType/WireOuterWorldConnection. Composition, not copy-paste.
        // -----------------------------------------------------------------------------
        //  ANCHORS (world space; root is at origin):
        //   • Start  = courtyard, east of the keep, y≈0  → (16, 0, 0)
        //   • End    = upper-battlement EDGE on the east side, y≈11.5. The platform /
        //              UpperBattlements_Nav plane spans ±22 around the keep origin, so the
        //              east edge is x≈+22. We aim the top tread at (21, 11.5, 0) so it
        //              sits ON / OVERLAPPING the nav plane (not past it, not short of it).
        //  The builder fits step count + per-step rise to the measured 11.5 m climb, keeps
        //  a ≥8 m walkable band (width 9, radius 12 → no inner pinch), and drops a chord
        //  NavMeshLink (start→end) as the reliability backup. NO runtime bake — the castle
        //  re-bakes its persisted NavMeshSurface via BatchAddFloorAndBakeCastle.
        // =====================================================================
        // SINGLE-LEVEL PIVOT (owner 2026-06-12): the second level is REMOVED — enemy AI
        // could not navigate up to it ("AI struggled to get to 2nd level") and the tree
        // showed through the level-2 floor. The grand stair existed ONLY to connect the
        // courtyard to the (now-deleted) upper battlements, so it is no longer built. This
        // method is retained as an IDEMPOTENT CLEANUP that destroys any pre-existing stair
        // baked into the saved scene (same pattern as the stray-stair removal). The castle is
        // now a flat, single-layer CoC-style base — everything walkable at y≈0.
        private const string GrandStairName = "GrandStair_CourtyardToBattlements";

        private static void BuildGrandStair(Transform parent)
        {
            // No second level -> no stair. Remove any prior instance instead of building one.
            RemoveSecondLevel();
        }

        // Idempotent teardown of EVERY second-tier / elevated element so the saved scene
        // becomes a single flat ground plane. Safe to call repeatedly (Find returns null when
        // already gone). Covers: the grand stair, the upper battlements group (platform +
        // crenellation walls + upper defensive towers), the home-accent balcony, the old
        // floating decorative stair, and any stray legacy poly stair.
        private static void RemoveSecondLevel()
        {
            foreach (var n in new[]
            {
                GrandStairName,
                "UpperBattlements_SecondLevel_Defenses_LOS",
                "BattlementsPlatform_Wide42m_ForPlayerTowers_LOS_DownToCourtyard",
                "KeepBalcony_HomeAccent",
                "QuaterniusStairs_UpperAccess",
                "MainStairs_Poly_ToUpperBattlements",
            })
            {
                var go = GameObject.Find(n);
                if (go != null) { Object.DestroyImmediate(go); Log("RemoveSecondLevel: destroyed '" + n + "' (single-layer pivot)."); }
            }
        }

        private static void CreateInvisibleFloor(Transform parent, string name, Vector3 localPos, Vector3 localScale)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); // Plane = MeshFilter + MeshRenderer + MeshCollider
            plane.name = name;
            plane.transform.SetParent(parent, false);
            plane.transform.localPosition = localPos;
            plane.transform.localScale = localScale;

            // Invisible but bakeable: hide the renderer; the NavMesh bakes from the MeshCollider
            // (Use Geometry = Physics Colliders), so visibility doesn't matter to the bake.
            var r = plane.GetComponent<MeshRenderer>();
            if (r != null) r.enabled = false;
            if (plane.GetComponent<MeshCollider>() == null) plane.AddComponent<MeshCollider>();

            // WO-168 lesson + owner 2026-06-12: FORCE the floor walkable so the gate arch
            // (which voxelizes solid across the opening) cannot carve/seal it on bake.
            AddWalkableNavMeshModifier(plane);
        }

        // Force a GameObject's baked navmesh area to Walkable (overrideArea) so a forced floor
        // plane cannot be carved/sealed by overlapping gate geometry. Reflection on
        // Unity.AI.Navigation.NavMeshModifier (matches this file's no-hard-dep style).
        private static void AddWalkableNavMeshModifier(GameObject go)
        {
            var t = FindType("Unity.AI.Navigation.NavMeshModifier");
            if (t == null) return;
            var mod = go.GetComponent(t);
            if (mod == null) mod = go.AddComponent(t);
            var pOverride = t.GetProperty("overrideArea");
            if (pOverride != null) pOverride.SetValue(mod, true);
            var pArea = t.GetProperty("area");
            if (pArea != null) pArea.SetValue(mod, 0); // 0 = Walkable
        }

        // WO-168 technique (mirrors VillageSceneBuilder's gate-nav fix): exclude the gate arch
        // meshes from the NavMesh bake so they don't voxelize SOLID across the opening and seal
        // the gate. NavMeshAgent ignores physics colliders, so excluding the gate from the BAKE
        // leaves the opening walkable while the hero still passes through. Adds a NavMeshModifier
        // with ignoreFromBuild=true to each CastleSide_*/Gate_* object.
        private static void ExcludeGatesFromNavBake()
        {
            var t = FindType("Unity.AI.Navigation.NavMeshModifier");
            if (t == null) { Err("ExcludeGatesFromNavBake: NavMeshModifier type not found — gates may seal."); return; }
            var pIgnore = t.GetProperty("ignoreFromBuild");
            int excluded = 0;
            foreach (var side in new[] { "South", "West", "North", "East" })
            {
                var sideRoot = GameObject.Find("CastleSide_" + side);
                if (sideRoot == null) continue;
                foreach (var tr in sideRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name.IndexOf("Gate", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var go = tr.gameObject;
                    var mod = go.GetComponent(t);
                    if (mod == null) mod = go.AddComponent(t);
                    if (pIgnore != null) pIgnore.SetValue(mod, true);
                    excluded++;
                }
            }
            Log("ExcludeGatesFromNavBake: excluded " + excluded + " gate object(s) from the bake (WO-168 technique).");
        }

        // Non-destructive: add ONLY the invisible floor to whatever castle scene is open,
        // without rebuilding the whole hub (preserves the wired hero/camera/gate). Run this,
        // then set the NavMeshSurface Use Geometry = Physics Colliders and Bake.
        [MenuItem("Defenders/Scenes/Add NavMesh Floor to Current Castle")]
        public static void AddNavMeshFloorToCurrentCastle()
        {
            var root = GameObject.Find(RootName);
            Transform parent;
            if (root != null) { parent = root.transform; }
            else
            {
                var holder = new GameObject(RootName + "_FloorHost");
                parent = holder.transform;
                Debug.LogWarning("[CastleHubBuilder] No CastleHubRoot found — floor added under a new host object.");
            }
            BuildNavMeshFloor(parent);
            Debug.Log("[CastleHubBuilder] Added invisible NavMesh floor (interior + gate bridge). " +
                      "NEXT: select your NavMeshSurface, set Use Geometry = Physics Colliders, then Bake. " +
                      "The blue should become ONE connected sheet through the gate.");
            Selection.activeGameObject = parent.gameObject;
        }

        // =====================================================================
        //  OuterWorld connection wiring (NavMeshLink + transition trigger)
        //  Called from BuildCastleHub and exposed as a menu for existing scenes
        //  (e.g. open MainCastle_Hall.unity then run the menu to wire without rebuild).
        // =====================================================================

        [MenuItem("Defenders/Scenes/Wire Current Castle to OuterWorld")]
        public static void WireCurrentCastleToOuterWorld()
        {
            var marker = GameObject.Find("WorldGate_ConnectToOuterWorld_Marker");
            if (marker == null)
            {
                Debug.LogError("[CastleHubBuilder] Could not find 'WorldGate_ConnectToOuterWorld_Marker' in the current scene. " +
                               "Run the main Build CastleHub first (or create the marker manually at your south gate).");
                return;
            }

            WireOuterWorldConnection(marker);
            Debug.Log("[CastleHubBuilder] Wired current scene's gate marker to OuterWorld (NavMeshLink + transition trigger). " +
                      "Make sure 'OuterWorld' is in Build Settings. Align world positions of the two scenes for seam to match.");
            Selection.activeGameObject = marker;
        }

        /// <summary>
        /// Adds a NavMeshLink across the gate for AI pathing + a trigger that loads OuterWorld
        /// additively and moves the player (uses the SceneTransitionTrigger we created to match WorldSceneLoader patterns).
        /// Reflection is used for the custom component (Editor asm cannot directly reference DeNelle.Village).
        /// </summary>
        private static void WireOuterWorldConnection(GameObject gateMarker)
        {
            if (gateMarker == null) return;

            // 0. IDEMPOTENCY — this is called on every wire/build (safe to call again). Without a
            //    cleanup pass it piles up duplicate NavMeshLinks + OuterWorldTransitionTriggers on
            //    each run (3 triggers were found triple-firing the scene transition). Strip any prior
            //    wiring first so we always end with EXACTLY one link + one trigger.
            foreach (var t in gateMarker.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t != gateMarker.transform && t.name == "OuterWorldTransitionTrigger")
                    Object.DestroyImmediate(t.gameObject);
            }
            var priorNavType = System.Type.GetType("Unity.AI.Navigation.NavMeshLink, Unity.AI.Navigation");
            if (priorNavType != null)
            {
                foreach (var oldLink in gateMarker.GetComponents(priorNavType))
                    if (oldLink != null) Object.DestroyImmediate(oldLink);
            }

            // 1. NavMeshLink — bridges the navmesh seam when Castle + OuterWorld are both loaded additive.
            //    Tune start/end/width to the actual gate opening width (Quaternius/poly gate ~12-15m).
            // Use reflection / SerializedObject to add the component and configure it without a hard
            // compile-time reference to the type (the AI Navigation package may not be in the Editor asmdef
            // references in a way that resolves the type for all build contexts). This matches the project's
            // pattern for optional/cross-package Editor scripting.
            var navType = System.Type.GetType("Unity.AI.Navigation.NavMeshLink, Unity.AI.Navigation");
            if (navType != null)
            {
                var comp = gateMarker.AddComponent(navType);
                if (comp != null)
                {
                    var so = new SerializedObject(comp);
                    // The new Unity.AI.Navigation.NavMeshLink serializes as m_StartPoint/m_EndPoint/m_Width
                    // (NOT the legacy startPoint/endPoint/width) — using the wrong names silently left a
                    // zero-width, mis-oriented link. Try m_-prefixed first, fall back to the legacy names.
                    var p = so.FindProperty("m_StartPoint") ?? so.FindProperty("startPoint"); if (p != null) p.vector3Value = new Vector3(-7f, 0f, -1f);
                    p = so.FindProperty("m_EndPoint") ?? so.FindProperty("endPoint"); if (p != null) p.vector3Value = new Vector3(7f, 0f, -1f);
                    p = so.FindProperty("m_Width") ?? so.FindProperty("width"); if (p != null) p.floatValue = 12f;
                    p = so.FindProperty("m_Bidirectional") ?? so.FindProperty("bidirectional"); if (p != null) p.boolValue = true;
                    p = so.FindProperty("m_Area") ?? so.FindProperty("area"); if (p != null) p.intValue = 0;
                    so.ApplyModifiedProperties();
                }
            }
            else
            {
                Debug.LogWarning("[CastleHubBuilder] Could not resolve NavMeshLink type at runtime. Install the 'AI Navigation' package (Window > Package Manager) and ensure the Editor asmdef references 'Unity.AI.Navigation' for seamless multi-scene NavMesh.");
            }

            // 2. Transition trigger (player crosses the gate).
            //    Box covers the gate area. On enter (Player/HeroTarget tag) it ensures OuterWorld is loaded
            //    and teleports the player to the corresponding spot in OuterWorld world space.
            var triggerGo = new GameObject("OuterWorldTransitionTrigger");
            triggerGo.transform.SetParent(gateMarker.transform, false);
            // Sit the trigger slightly NORTH of the gate marker (toward the reachable courtyard),
            // not 2m south of it — the hero stops at the navmesh edge well short of the marker.
            triggerGo.transform.localPosition = new Vector3(0, 1.5f, 6f);

            var col = triggerGo.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(16f, 6f, 14f);

            // Add the transition behaviour via reflection (matches MineNode / other builder patterns).
            var transType = FindType("DeNelle.Village.SceneTransitionTrigger");
            if (transType != null)
            {
                var comp = triggerGo.AddComponent(transType);

                // Configure via reflection (public fields on the trigger script).
                var fScene = transType.GetField("targetSceneName");
                if (fScene != null) fScene.SetValue(comp, "OuterWorld");

                // Target position in OuterWorld space.
                // SOUTH-GATE SEAM FIX (2026-06-13): OLD (0,0.5,-80) landed the hero deep in / past
                // the SOUTH region (Mirewood @ (0,0,-90)) = "yanked to the middle" (owner). OuterWorld's
                // four regions fan out from world ORIGIN (300x300 terrain centered on 0); land just
                // SOUTH of origin so the hero faces into the world from where they entered. Mirrors
                // EnsureExitSeamAtRecipeGate. (If OuterWorld gets a real castle-gate art marker later,
                // measure its mouth and target that + a few metres inward.)
                var fPos = transType.GetField("targetPosition");
                if (fPos != null) fPos.SetValue(comp, new Vector3(0f, 0.5f, -12f));

                var fAdditive = transType.GetField("loadAdditive");
                if (fAdditive != null) fAdditive.SetValue(comp, true);

                // Generous proximity so it fires as the hero APPROACHES the south gate — the hero
                // stops at the navmesh edge ~10-18m short of the marker, so the default 6m never fired.
                var fRadius = transType.GetField("ProximityRadius");
                if (fRadius != null) fRadius.SetValue(comp, 18f);

                // OWNER 2026-06-17: AUTO-CROSS, no F-prompt — the hub exit seam crosses on
                // proximity (the F-key confirm felt unnatural). Null-safe.
                transType.GetField("requireConfirm")?.SetValue(comp, false);
            }
            else
            {
                Debug.LogWarning("[CastleHubBuilder] DeNelle.Village.SceneTransitionTrigger not found (is the script compiled?). " +
                                 "Trigger collider was still added — you can attach the behaviour manually or re-run after compile.");
            }

            Debug.Log("[CastleHubBuilder] Wired gate marker with NavMeshLink + SceneTransitionTrigger (target OuterWorld near-origin hub ~ (0, 0.5, -12)).");
        }

        // =====================================================================
        //  OUTPOST CONNECTORS — RETIRED (CANON_GROUND_TRUTH 2026-06-28 / WO-584).
        // -----------------------------------------------------------------------------
        //  The old castle->garrison-outpost walk-to gate seams (W/N/E) have been REMOVED.
        //  Outposts are NOT reached by gate-seams or walking — they're entered via
        //  cave -> loading-zone -> OutpostResolver. The removed machinery (OutpostConnectors
        //  array + WireOutpostConnectors + BatchWireOutpostsAndSave + the "Wire Castle to
        //  Outposts" menu) was already inert at runtime (gated behind ff.outposttravel, which
        //  defaults OFF, so it stripped itself every bake). RemoveInTownOutpostConnectors()
        //  below still scrubs any stale OutpostConnector_* baked into older saved scenes.
        // =====================================================================

        /// <summary>
        /// Cross-assembly type lookup (same pattern used by OuterWorldBuilder / VillageSceneBuilder).
        /// </summary>
        private static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        // =====================================================================
        //  Make Castle the primary project start (move from Village2)
        //  + wire hero, camera, defaults, OuterWorld streaming (via generalized loader),
        //  and other core items (gate already wired by previous step).
        //
        //  Run on your saved MainCastle_Hall.unity (or any Castle scene) while it is open.
        //  Reuses Village2Playable (same Editor assembly) for hero + camera setup.
        //  This + the updated WorldSceneLoader means playing the Castle scene now streams
        //  OuterWorld, spawns you in the personal quarters, and gives full controls.
        // =====================================================================

        [MenuItem("Defenders/Scenes/Make CastleHub Primary Start (current scene) + Wire Everything")]
        public static void MakeCastleHubPrimaryStartAndWire()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene == null || !scene.path.Contains("Castle") && !scene.path.Contains("MainCastle"))
            {
                Debug.LogWarning("[CastleHubBuilder] Open a Castle Hub scene (MainCastle_Hall or similar) first.");
                return;
            }

            Log("=== Make CastleHub Primary Start + Full Wiring START ===");

            // 1. Scene defaults (camera with SmartMobileCamera, light, ambient/fog) + EventSystem.
            //    Idempotent.
            Village2Playable.AddSceneDefaultsToActiveScene();
            Village2Playable.ImportEventSystem();
            Log("Scene defaults + EventSystem wired.");

            // 2. Find a good parent for the hero: prefer the personal quarters / home space inside the keep.
            Transform heroParent = FindHeroParentInCastle(scene);
            if (heroParent == null)
            {
                heroParent = scene.GetRootGameObjects().FirstOrDefault(r => r.name.Contains("Castle") || r.name.Contains("Root"))?.transform;
            }
            if (heroParent == null && scene.rootCount > 0)
                heroParent = scene.GetRootGameObjects()[0].transform;

            // 3. Import the hero (Mage rig by default, with locomotion, abilities, gear, body swapper, tagged Player).
            //    No Heart for hub (castle is safe home, not the defend-the-tower core).
            GameObject hero = Village2Playable.ImportHero(heroParent, heart: null);
            if (hero == null)
            {
                Err("ImportHero returned null. Check that Village2Playable and hero assets are present.");
                return;
            }
            Log("Hero imported (locomotion + abilities + visuals).");

            // 4. Place the hero inside the personal quarters (use the HeroStartPoint we placed during build, or fallback).
            PlaceHeroInCastleHome(scene, hero);

            // 5. Wire the smart camera to follow the hero.
            Village2Playable.WireCameraTargetToHero(hero);
            Log("Camera wired to hero.");

            // 5b. Disable the legacy VillageCamera so SmartMobileCamera is the SOLE follower.
            //     Both enabled lets VillageCamera's top-down offset (0,8.5,-6.5) bleed through over
            //     SmartMobileCamera's close 3rd-person (0,2.6,-4.5). Reflection by type-name avoids
            //     an asmdef dependency on the Village type from this Editor assembly.
            var mainCamForLegacy = Camera.main;
            if (mainCamForLegacy != null)
            {
                foreach (var comp in mainCamForLegacy.GetComponents<MonoBehaviour>())
                {
                    if (comp != null && comp.GetType().Name == "VillageCamera" && comp.enabled)
                    {
                        comp.enabled = false;
                        Log("VillageCamera (legacy top-down) DISABLED — SmartMobileCamera is sole follower.");
                    }
                }
            }

            // 6. Ensure HeroLocomotion is enabled (HeroControlEnsurer is often hardcoded to "Village2").
            EnsureHeroControllable(hero);

            // 7. Make sure the OuterWorld gate connection is wired (NavMeshLink + transition trigger).
            //    Safe to call again.
            var gateMarker = GameObject.Find("WorldGate_ConnectToOuterWorld_Marker");
            if (gateMarker != null)
            {
                WireOuterWorldConnection(gateMarker);
            }
            else
            {
                Log("No gate marker found — run the Build or Wire menu first for OuterWorld connection.");
            }

            // 8. Mark dirty + save reminder.
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Log("=== CASTLE IS NOW THE PRIMARY START ===");
            Log("Open/play this scene directly. Hero spawns in the personal quarters.");
            Log("OuterWorld will stream in additively (via updated WorldSceneLoader).");
            Log("South gate is wired for seamless transition to the open world.");
            Log("Other items (NPC points in the 8 structures, build on upper battlements, Echo Hollow pets area) are present — wire specific Yarn/Economy/Pet systems as needed.");
            Log("Save the scene. Add it (and OuterWorld) to Build Settings if not already.");

            Selection.activeGameObject = hero;
        }

        // =====================================================================
        //  Batchmode entry — open MainCastle_Hall, wire it as primary start, SAVE.
        //  Runs the interactive "Make CastleHub Primary Start" pass headless so the
        //  scene comes back ready to Play (hero on the spawn marker + follow camera +
        //  controls + OuterWorld gate). Invoked via run-unity-method.ps1. No dialogs.
        // =====================================================================
        public static void BatchWireCastleAndSave()
        {
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Log("BATCH: opened " + scenePath);

            // Promote the owner's placeholder capsule to the canonical spawn marker so the
            // NavMeshAgent hero spawns on the verified (first-floor) NavMesh — not the upper
            // quarters anchor, which may sit off-mesh. STRIP the visible mesh so it's an
            // invisible transform-only marker (keep the GameObject + its transform/function).
            // Disabling the renderer alone proved fragile (the pill reappeared in a live
            // build); remove the MeshRenderer + MeshFilter outright and destroy the collider
            // so it can never render OR block the hero. The transform stays as the marker.
            // Catch the marker whether it's still the raw primitive ("Capsule") OR was already
            // promoted in a prior pass (its capsule mesh was renamed but never stripped — the
            // lingering pill). Either way we re-strip the visible mesh below.
            var pill = GameObject.Find("Capsule") ?? GameObject.Find("HeroStartPoint_PlayerSpawn");
            if (pill != null)
            {
                pill.name = "HeroStartPoint_PlayerSpawn";
                var mr = pill.GetComponent<MeshRenderer>();
                if (mr != null) Object.DestroyImmediate(mr);
                var mf = pill.GetComponent<MeshFilter>();
                if (mf != null) Object.DestroyImmediate(mf);
                var col = pill.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                Log("BATCH: promoted 'Capsule' -> HeroStartPoint_PlayerSpawn (mesh + collider stripped; invisible marker).");
            }
            else
            {
                Log("BATCH: no 'Capsule' pill found — hero will fall back to the personal-quarters anchor.");
            }

            MakeCastleHubPrimaryStartAndWire();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("BATCH: wired + saved MainCastle_Hall. Ready to Play.");
        }

        // =====================================================================
        //  WO-384 — FORCE a fresh grand-stair rebuild (after a code change like the
        //  pivot/rotation/width), then bake. Unlike BatchAddFloorAndBakeCastle (which
        //  PRESERVES an existing GrandStair so a manual in-editor rotation survives),
        //  this removes the existing stair so BuildGrandStair regenerates it from code.
        // =====================================================================
        public static void BatchRebuildGrandStairAndBake()
        {
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var existing = GameObject.Find("GrandStair_CourtyardToBattlements");
            if (existing != null) { Object.DestroyImmediate(existing); Log("BATCH-REBUILD: removed existing GrandStair for a fresh code rebuild."); }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // The normal bake now builds it fresh (build-if-absent) + bakes + saves.
            BatchAddFloorAndBakeCastle();
        }

        // =====================================================================
        //  Scene debris cleanup shared by both batch entry points. Removes:
        //  - The owner's leftover hand-planes "Plane".."Plane (3)" — they render (z-fight
        //    "ground flash"), over-extend the navmesh, AND "Plane (2)"/"Plane (3)" carry
        //    hand-added NavMeshLinks authored in the PRE-RAISE (y=0) castle frame: the
        //    fleet's dangling-link oracle proved them off-mesh after the WO-593 island
        //    raise (break-log 2026-07-02: "NAVMESH-LINK DANGLING: 'Plane (2)' ... start
        //    on-mesh=False @ (-12.78, 5.02, -36.21), end on-mesh=False @ (-12.78, 1.48,
        //    -32.67)"). The old loop only removed "Plane"/"Plane (1)".
        //  - The stale "SeamlessOuterWorldSeam" subtree (WO-468 Phase 2 side-by-side WALK
        //    experiment) whose serialized NavLink_CastleToOuterWorld ends at z=-76 in the
        //    long-gone adjacent-OuterWorld frame — off-mesh every run (break-log 2026-07-02:
        //    "NAVMESH-LINK DANGLING: 'NavLink_CastleToOuterWorld' ... end on-mesh=False @
        //    (-4.37, 3.00, -76.00)"). The crossing is WARP by design now (RuntimeRegionGate
        //    builds the per-gate thresholds + AI links at runtime — CANON_GROUND_TRUTH
        //    2026-07-01); the serialized link bridges nothing and is retired here. Re-running
        //    "Defenders/World/Build Seamless OuterWorld Seam" recreates it deliberately.
        // =====================================================================
        private static void RemoveSupersededSeamDebris(string logPrefix)
        {
            foreach (var n in new[] { "Plane", "Plane (1)", "Plane (2)", "Plane (3)" })
            {
                var stray = GameObject.Find(n);
                if (stray != null) { Object.DestroyImmediate(stray); Log(logPrefix + ": removed leftover hand-plane '" + n + "'."); }
            }
            var staleSeam = GameObject.Find("SeamlessOuterWorldSeam");
            if (staleSeam != null)
            {
                Object.DestroyImmediate(staleSeam);
                Log(logPrefix + ": removed stale 'SeamlessOuterWorldSeam' (NavLink_CastleToOuterWorld) — superseded by the RuntimeRegionGate warp crossing.");
            }
        }

        // =====================================================================
        //  Batchmode entry — open MainCastle_Hall, ensure the invisible floor
        //  (courtyard + gate + keep entrance), force the NavMeshSurface to
        //  Physics Colliders, BAKE, and persist the navmesh asset + scene.
        //  Reflection on NavMeshSurface (no hard package dep). Invoked headless.
        // =====================================================================
        public static void BatchAddFloorAndBakeCastle()
        {
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Log("BATCH-BAKE: opened " + scenePath);

            // Remove the owner's leftover hand-placed planes — they still render (z-fight "ground
            // flash") and over-extend the navmesh way past the walls. The generated invisible
            // floor (renderer-off) replaces them cleanly.
            RemoveSupersededSeamDebris("BATCH-BAKE");

            // Ensure the generated invisible floor (courtyard + gate + keep entrance) exists.
            var root = GameObject.Find(RootName);
            Transform parent = root != null ? root.transform
                : (GameObject.Find(RootName + "_FloorHost") ?? new GameObject(RootName + "_FloorHost")).transform;
            BuildNavMeshFloor(parent);
            Log("BATCH-BAKE: floor ensured (courtyard + gate + keep interior).");

            // SINGLE-LEVEL PIVOT (owner 2026-06-12): strip EVERY second-tier element + the grand
            // stair from the saved scene so the bake produces ONE flat walkable sheet at y≈0 (the
            // second level was removed because enemy AI could not reach it). Idempotent.
            RemoveSecondLevel();
            Log("BATCH-BAKE: second level removed — single-layer bake.");

            // CoC inner wall ring (concentric ground obstacle, 4 gate gaps aligned to the outer
            // gates). Idempotent. Same Wall_Medieval_Stone prefab/material as the outer walls.
            BuildInnerWallRing(parent, AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Medieval_M/Wall_Medieval_Stone.prefab"));

            // Configure + bake every NavMeshSurface via reflection so renderer-off planes are
            // collected (Use Geometry = Physics Colliders). RenderMeshes=0, PhysicsColliders=1.
            var surfType = FindType("Unity.AI.Navigation.NavMeshSurface");
            if (surfType == null)
            {
                Err("BATCH-BAKE: NavMeshSurface type not resolved — bake skipped (do it in-editor).");
            }
            else
            {
                var surfaces = Object.FindObjectsByType(surfType, FindObjectsSortMode.None);
                Log("BATCH-BAKE: NavMeshSurface count = " + surfaces.Length);
                foreach (var s in surfaces)
                {
                    var ug = surfType.GetProperty("useGeometry");
                    if (ug != null) ug.SetValue(s, System.Enum.ToObject(ug.PropertyType, 1)); // PhysicsColliders
                    var co = surfType.GetProperty("collectObjects");
                    if (co != null) co.SetValue(s, System.Enum.ToObject(co.PropertyType, 0)); // All

                    var build = surfType.GetMethod("BuildNavMesh", System.Type.EmptyTypes);
                    if (build != null) { build.Invoke(s, null); Log("BATCH-BAKE: BuildNavMesh() invoked."); }

                    // Persist the freshly-built data as an asset so it survives the scene save.
                    var dataProp = surfType.GetProperty("navMeshData");
                    var data = dataProp != null ? dataProp.GetValue(s) as Object : null;
                    if (data != null)
                    {
                        if (!System.IO.Directory.Exists("Assets/Scenes/MainCastle_Hall"))
                            AssetDatabase.CreateFolder("Assets/Scenes", "MainCastle_Hall");
                        string assetPath = "Assets/Scenes/MainCastle_Hall/NavMesh-NavMeshSurface.asset";
                        if (!AssetDatabase.Contains(data))
                        {
                            var existing = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                            if (existing != null) AssetDatabase.DeleteAsset(assetPath);
                            AssetDatabase.CreateAsset(data, assetPath);
                            Log("BATCH-BAKE: navmesh asset written -> " + assetPath);
                        }
                        else Log("BATCH-BAKE: navmesh data already an asset (updated in place).");
                    }
                    else Err("BATCH-BAKE: surface.navMeshData was null after bake — bake likely produced nothing.");
                }
            }

            // WO-1731: the bake above re-pointed every surface's m_NavMeshData and deleted the
            // asset each replaced. An unsaved scene keeps the DELETED guid and ships with NO
            // navmesh -- silently. A bake that cannot persist its own reference THROWS
            // (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException(
                    "BATCH-BAKE: could not save navigation for the castle hub scene -- the bake would " +
                    "leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Log("BATCH-BAKE: saved scene + assets. Done.");
        }

        // =====================================================================
        //  BATCH-RECIPE — the "make the castle from the script" pipeline (owner 2026-06-12).
        //  Rebuilds the 4 wall sides FROM castle-south-recipe.json (the source of truth for
        //  the owner's hand-dialed offsets), RE-PLACES the OuterWorld exit seam ONTO the recipe
        //  south gate (the committed trigger sat at z=-83.77, off-mesh & 43m past the opening
        //  -> unreachable -> "can't exit", PROVEN by CastleGateNavVerify), rebuilds the
        //  recipe-driven walkable floor + gate strips + stair, bakes the NavMesh, saves, then
        //  VERIFIES the hero can path spawn -> gate within ProximityRadius. Fully reproducible:
        //  no hand-editing; a re-run reproduces the owner's layout AND a crossable gate.
        // =====================================================================
        public static void BatchRebuildCastleFromRecipeAndBake()
        {
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Log("BATCH-RECIPE: opened " + scenePath);
            LoadFootprintLiftY();   // WO-593: base height from PlayerPrefs, before walls/floor/heart author

            // 1. Walls from the recipe (+ mirror x4 around world origin).
            CastleWallsFromRecipe.Recreate();
            Log("BATCH-RECIPE: walls rebuilt from castle-south-recipe.json + mirrored x4.");

            // 2. Exit seam ONTO the recipe gate so the hero can actually reach + trip it.
            EnsureExitSeamAtRecipeGate();

            // 3. Recipe-driven walkable floor + gate exit strips + keep interior.
            RemoveSupersededSeamDebris("BATCH-RECIPE");
            var floorRoot = GameObject.Find(RootName);
            Transform parent = floorRoot != null ? floorRoot.transform
                : (GameObject.Find(RootName + "_FloorHost") ?? new GameObject(RootName + "_FloorHost")).transform;
            // WO-593: lift the BASE (footprint root) to the island height so the nav floor + every
            // localPosition child (courtyard, the 8 structures, hero home) rides it. The world-.position
            // sites (Heart, inner ring, gate strips) set base height absolutely below, so no double-count.
            parent.position = new Vector3(parent.position.x, CastleFootprintLiftY, parent.position.z);
            BuildNavMeshFloor(parent);
            BuildBasePlinth(parent);   // WO-593: raised stone base fills the gap under the castle (no terrain regen)

            // SINGLE-LEVEL PIVOT (owner 2026-06-12): strip EVERY second-tier element baked into
            // the saved scene (grand stair, upper battlements platform + walls + towers, balcony,
            // floating decorative stair, stray poly stair). RemoveSecondLevel is idempotent.
            RemoveSecondLevel();
            Log("BATCH-RECIPE: second level removed — castle is now single-layer (flat y≈0).");

            // 3b. CoC-STYLE SECOND INNER WALL RING — concentric ground wall around the Heart core,
            //     4 gate gaps aligned to the outer gates, excluded-from-bake at the gaps so enemies
            //     path outer gate -> inner gap -> Heart. Idempotent (rebuilds clean each run).
            BuildInnerWallRing(parent, AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Medieval_M/Wall_Medieval_Stone.prefab"));
            Log("BATCH-RECIPE: floor + recipe gate strips + inner CoC wall ring ensured.");

            // 3b. T-002/T-005: place the Heart of Elarion (lose-condition + enemy target) so
            //     WaveManager._heart is non-null and Enemy.DriveNav() advances (enemies were
            //     frozen with no target). Clean scale-1 anchor (no collider-scale trap).
            WireCastleHeart(parent);

            // 3c. OWNER 2026-06-12 town cleanup on the ALREADY-SAVED scene: this batch path does NOT
            //     rebuild the 8 structures, so capture the owner's manual edits (deleted keep+floor,
            //     stray test tree, stray anvil) and ensure the Anvil sits at the blacksmith. Idempotent.
            EnsureCastleTownProps();

            // 3d. WO-593 F8 "Missing Prefab Asset: 'HeroBody'": strip every missing-source prefab
            //     instance (the stale HeroBody references the size-cut-deleted Mage.fbx, guid
            //     be1690ec…) so the rebuilt scene saves CLEAN and the scene-open warning is gone.
            //     Generic + idempotent — future asset deletions can't leave the same debris.
            int stripped = MissingPrefabInstanceCleaner.CleanOpenScene(scene);
            Log("BATCH-RECIPE: missing-prefab instance cleanup removed " + stripped + " stale instance(s).");

            // 3e. WO-593 F8 "steps inside the wall": re-seat the 4 perimeter wall stairs flush to the
            //     wall plane (measures Wall_* runs only) — must run AFTER the recipe rebuild recreates
            //     them and BEFORE the bake so the navmesh includes the seated stairs.
            CastleWallStairsSeatFix.RunOnOpenScene(scene);
            Log("BATCH-RECIPE: wall stairs re-seated flush to the wall plane.");

            // 4. Bake every NavMeshSurface (Physics Colliders, All) + persist the asset.
            //    (Mirrors BatchAddFloorAndBakeCastle's proven bake block.)
            var surfType = FindType("Unity.AI.Navigation.NavMeshSurface");
            if (surfType == null)
            {
                Err("BATCH-RECIPE: NavMeshSurface type not resolved — bake skipped.");
            }
            else
            {
                var surfaces = Object.FindObjectsByType(surfType, FindObjectsSortMode.None);
                Log("BATCH-RECIPE: NavMeshSurface count = " + surfaces.Length);
                foreach (var s in surfaces)
                {
                    var ug = surfType.GetProperty("useGeometry");
                    if (ug != null) ug.SetValue(s, System.Enum.ToObject(ug.PropertyType, 1)); // PhysicsColliders
                    var co = surfType.GetProperty("collectObjects");
                    if (co != null) co.SetValue(s, System.Enum.ToObject(co.PropertyType, 0)); // All

                    var build = surfType.GetMethod("BuildNavMesh", System.Type.EmptyTypes);
                    if (build != null) { build.Invoke(s, null); Log("BATCH-RECIPE: BuildNavMesh() invoked."); }

                    var dataProp = surfType.GetProperty("navMeshData");
                    var data = dataProp != null ? dataProp.GetValue(s) as Object : null;
                    if (data != null)
                    {
                        if (!System.IO.Directory.Exists("Assets/Scenes/MainCastle_Hall"))
                            AssetDatabase.CreateFolder("Assets/Scenes", "MainCastle_Hall");
                        string assetPath = "Assets/Scenes/MainCastle_Hall/NavMesh-NavMeshSurface.asset";
                        if (!AssetDatabase.Contains(data))
                        {
                            var existingData = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                            if (existingData != null) AssetDatabase.DeleteAsset(assetPath);
                            AssetDatabase.CreateAsset(data, assetPath);
                            Log("BATCH-RECIPE: navmesh asset written -> " + assetPath);
                        }
                        else Log("BATCH-RECIPE: navmesh data already an asset (updated in place).");
                    }
                    else Err("BATCH-RECIPE: surface.navMeshData null after bake.");
                }
            }

            // 5. Save scene + assets. WO-1731: an unsaved post-bake scene keeps the guid of the
            //    asset the bake deleted and ships with NO navmesh, silently. THROW instead
            //    (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException(
                    "BATCH-RECIPE: could not save navigation for the castle hub scene -- the bake would " +
                    "leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Log("BATCH-RECIPE: saved scene + assets.");

            // 6. PROVE the gate is crossable (behavioral, in-session, post-bake).
            bool ok = CastleGateNavVerify.VerifyOpenScene(out string detail);
            Log("BATCH-RECIPE: gate-nav verify -> " + (ok ? "GATE_NAV_OK" : "GATE_NAV_FAIL") + " :: " + detail);
        }

        // =====================================================================
        //  REWIRE + REBAKE (owner 2026-06-17) — LEAST-INVASIVE base-loop exit fix.
        // -----------------------------------------------------------------------------
        //  Does NOT regenerate the castle (the owner has tuned walls/structures). It only:
        //   1. Re-runs the EXIT seam wiring (EnsureExitSeamAtRecipeGate) which stamps
        //      requireConfirm=false so the saved SceneTransitionTrigger AUTO-CROSSES (no
        //      F-prompt, owner directive). (Outpost gate-seam wiring retired — WO-584.)
        //   2. Rebuilds the invisible NavMesh floor + re-bakes every NavMeshSurface (3/4 gates
        //      were unreachable from a stale/fragmented bake) and persists the navmesh asset.
        //   3. Saves MainCastle_Hall + verifies spawn->gate reachability (CastleGateNavVerify).
        //  Reuses the existing wiring + floor + bake helpers (no duplicated logic; the bake is
        //  factored into BakeAllCastleSurfacesAndPersist, shared with this method).
        // =====================================================================
        public static void RewireAndRebakeCurrentCastle()
        {
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Log("REWIRE-REBAKE: opened " + scenePath);

            // 1. Re-apply the exit seam wiring so requireConfirm=false lands on the saved
            //    component (auto-cross, no F-prompt — owner 2026-06-17). Idempotent. (The old
            //    outpost-connector wiring was removed — outposts are entered via cave ->
            //    loading-zone -> OutpostResolver, not walk-to gate seams. WO-584.)
            EnsureExitSeamAtRecipeGate();
            var root = GameObject.Find(RootName);
            if (root == null) Err("REWIRE-REBAKE: " + RootName + " not found.");

            // 2. Rebuild the walkable floor (courtyard + gate strips + keep interior) so the
            //    bake collects a connected sheet, then re-bake + persist. Mirrors the proven
            //    BatchAddFloorAndBakeCastle flow WITHOUT rebuilding walls/structures.
            Transform parent = root != null ? root.transform
                : (GameObject.Find(RootName + "_FloorHost") ?? new GameObject(RootName + "_FloorHost")).transform;
            BuildNavMeshFloor(parent);
            Log("REWIRE-REBAKE: floor ensured (courtyard + gate + keep interior).");

            BakeAllCastleSurfacesAndPersist("REWIRE-REBAKE");

            // 3. Save scene + assets. WO-1731: an unsaved post-bake scene keeps the guid of the
            //    asset the bake deleted and ships with NO navmesh, silently. THROW instead
            //    (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException(
                    "REWIRE-REBAKE: could not save navigation for the castle hub scene -- the bake would " +
                    "leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Log("REWIRE-REBAKE: saved scene + assets.");

            // 4. PROVE spawn->gate reachability (behavioral, post-bake).
            bool ok = CastleGateNavVerify.VerifyOpenScene(out string detail);
            Log("REWIRE-REBAKE: gate-nav verify -> " + (ok ? "GATE_NAV_OK" : "GATE_NAV_FAIL") + " :: " + detail);

            // 5. BRIDGE SEAM — add the real walkable castle↔OuterWorld crossing (replaces the dead
            //    reflection warp). AddCastleBridgeSeam re-opens MainCastle_Hall, drops the
            //    CastleBridgeSeam subtree (ADD-ONLY — no regen), relocates the exit trigger to the
            //    bridge far end, then re-bakes + saves + verifies. Folded here so the existing
            //    rewire path always leaves the bridge wired.
            AddCastleBridgeSeam();
        }

        // Shared bake: configure every NavMeshSurface to Physics Colliders / All, BuildNavMesh,
        // and persist the data asset. Factored out of the inline copies so RewireAndRebakeCurrentCastle
        // reuses the exact proven block (BatchAddFloorAndBakeCastle / BATCH-RECIPE keep their copies).
        private static void BakeAllCastleSurfacesAndPersist(string tag)
        {
            var surfType = FindType("Unity.AI.Navigation.NavMeshSurface");
            if (surfType == null) { Err(tag + ": NavMeshSurface type not resolved — bake skipped."); return; }

            var surfaces = Object.FindObjectsByType(surfType, FindObjectsSortMode.None);
            Log(tag + ": NavMeshSurface count = " + surfaces.Length);
            foreach (var s in surfaces)
            {
                var ug = surfType.GetProperty("useGeometry");
                if (ug != null) ug.SetValue(s, System.Enum.ToObject(ug.PropertyType, 1)); // PhysicsColliders
                var co = surfType.GetProperty("collectObjects");
                if (co != null) co.SetValue(s, System.Enum.ToObject(co.PropertyType, 0)); // All

                var build = surfType.GetMethod("BuildNavMesh", System.Type.EmptyTypes);
                if (build != null) { build.Invoke(s, null); Log(tag + ": BuildNavMesh() invoked."); }

                var dataProp = surfType.GetProperty("navMeshData");
                var data = dataProp != null ? dataProp.GetValue(s) as Object : null;
                if (data != null)
                {
                    if (!System.IO.Directory.Exists("Assets/Scenes/MainCastle_Hall"))
                        AssetDatabase.CreateFolder("Assets/Scenes", "MainCastle_Hall");
                    string assetPath = "Assets/Scenes/MainCastle_Hall/NavMesh-NavMeshSurface.asset";
                    if (!AssetDatabase.Contains(data))
                    {
                        var existing = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                        if (existing != null) AssetDatabase.DeleteAsset(assetPath);
                        AssetDatabase.CreateAsset(data, assetPath);
                        Log(tag + ": navmesh asset written -> " + assetPath);
                    }
                    else Log(tag + ": navmesh data already an asset (updated in place).");
                }
                else Err(tag + ": surface.navMeshData was null after bake — bake produced nothing.");
            }
        }

        // =====================================================================
        //  ENSURE CASTLE TOWN PROPS (owner 2026-06-12) — idempotent cleanup applied to the
        //  ALREADY-SAVED scene. BatchRebuildCastleFromRecipeAndBake does NOT rebuild the 8
        //  structures, so the owner's manual edits must be captured here so a re-bake reproduces
        //  her layout: she deleted the keep prefab + its interior floor, dropped a test Tree_Of_Life
        //  south of the Heart, and dropped a stray Anvil in the courtyard. This:
        //   1. Destroys leftover GroundLevel_Keep_Hall_Entry + KeepInterior_Nav, and an EMPTY keep
        //      root (only if it holds no meaningful children — the hero spawn lives under it).
        //   2. Removes orphan Tree_Of_Life objects NOT parented to the Heart anchor (her south test
        //      placement). The real tree is TreeOfLife_Visual under HeartOfElarion — left intact.
        //   3. Ensures the Anvil is at the blacksmith: destroys any stray Anvil not under the
        //      Blacksmith storefront, then adds one at local (0,0,5) if the storefront lacks it.
        //  Guards all nulls; safe to run repeatedly.
        // =====================================================================
        private static void EnsureCastleTownProps()
        {
            // 1. Leftover keep prefab + its nav floor (owner deleted the keep).
            var keepPrefab = GameObject.Find("GroundLevel_Keep_Hall_Entry");
            if (keepPrefab != null) { Object.DestroyImmediate(keepPrefab); Log("EnsureCastleTownProps: destroyed leftover GroundLevel_Keep_Hall_Entry."); }
            var keepNav = GameObject.Find("KeepInterior_Nav");
            if (keepNav != null) { Object.DestroyImmediate(keepNav); Log("EnsureCastleTownProps: destroyed leftover KeepInterior_Nav."); }

            // 1b. WO-593 / owner F8 2026-07-02 "Wall in middle?": the wood/iron/steel WALL PREVIEW
            //     demo row (WallPreview.cs) still sits at courtyard centre in the saved scene
            //     (MainCastle_Hall.unity 'WallPreview_Row' @ (0,0,0); its steel tier carries the
            //     pale-blue emissive rune material — the floating pale-blue block in the F8 shot).
            //     WallPreview.cs's own header says "a castle rebake will wipe this row", but no
            //     rebuild path ever removed it — do it here so every regen clears the demo debris.
            //     (The InnerWallRing_CoC — the beige battlemented wall run in the same F8 — is
            //     cleared by the retired destroy-only BuildInnerWallRing in the same rebuild.)
            var wallPreview = GameObject.Find("WallPreview_Row");
            if (wallPreview != null) { Object.DestroyImmediate(wallPreview); Log("EnsureCastleTownProps: destroyed WallPreview_Row demo debris (owner F8 2026-07-02 'Wall in middle?')."); }

            // An EMPTY keep root — only destroy if it has no meaningful children. The hero spawn
            // (HeroStartPoint...) lives under it via the home space, so if that subtree exists we
            // leave the root and only strip the keep prefab/floor (handled above).
            var keepRoot = GameObject.Find("MainKeep_CastleWithTwoLevels_Home");
            if (keepRoot != null)
            {
                bool hasHeroStart = keepRoot.GetComponentsInChildren<Transform>(true)
                    .Any(t => t != null && t.name.StartsWith("HeroStartPoint"));
                bool hasMeaningfulChildren = keepRoot.transform.childCount > 0;
                if (!hasHeroStart && !hasMeaningfulChildren)
                {
                    Object.DestroyImmediate(keepRoot);
                    Log("EnsureCastleTownProps: destroyed EMPTY MainKeep_CastleWithTwoLevels_Home (no children, no hero spawn).");
                }
                else
                    Log("EnsureCastleTownProps: kept MainKeep root (hero spawn / children present).");
            }

            // 2. Orphan south test tree(s): any GameObject named Tree_Of_Life NOT under the Heart
            //    anchor. The real centerpiece is TreeOfLife_Visual under HeartOfElarion — untouched.
            var heartAnchor = GameObject.Find(HeartAnchorName);
            Transform heartTf = heartAnchor != null ? heartAnchor.transform : null;
            foreach (var t in CollectAllTransforms())
            {
                if (t == null || t.name != "Tree_Of_Life") continue;
                if (heartTf != null && t.IsChildOf(heartTf)) continue; // safety; real tree is named TreeOfLife_Visual anyway
                Log("EnsureCastleTownProps: destroyed orphan Tree_Of_Life at " + t.position + " (owner's test placement).");
                Object.DestroyImmediate(t.gameObject);
            }

            // 2b. Dedup TreeOfLife_Visual: a prior Resources placeholder tree (plain instance) can
            //     survive WireCastleHeart's direct-child force-replace, leaving TWO trees. Keep the
            //     Art-fbx prefab instance, destroy every other "TreeOfLife_Visual".
            var allTreeTf = CollectAllTransforms();
            Transform keepTree = null;
            foreach (var t in allTreeTf)
            {
                if (t == null || t.name != "TreeOfLife_Visual") continue;
                bool isPrefabInst = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) != null;
                bool keepIsPrefab = keepTree != null && PrefabUtility.GetCorrespondingObjectFromSource(keepTree.gameObject) != null;
                if (keepTree == null || (isPrefabInst && !keepIsPrefab)) keepTree = t;
            }
            foreach (var t in allTreeTf)
            {
                if (t == null || t.name != "TreeOfLife_Visual" || t == keepTree) continue;
                Log("EnsureCastleTownProps: destroyed duplicate TreeOfLife_Visual at " + t.position + ".");
                Object.DestroyImmediate(t.gameObject);
            }

            // 3. Anvil at the blacksmith. Destroy any stray Anvil not under the Blacksmith storefront,
            //    then ensure the storefront has exactly one.
            var blacksmith = GameObject.Find("Blacksmith_Weapons_Storefront");
            Transform blacksmithTf = blacksmith != null ? blacksmith.transform : null;
            foreach (var t in CollectAllTransforms())
            {
                if (t == null || t.name != "Anvil") continue;
                if (blacksmithTf != null && t.IsChildOf(blacksmithTf)) continue;
                Log("EnsureCastleTownProps: destroyed stray Anvil at " + t.position + " (not under the Blacksmith).");
                Object.DestroyImmediate(t.gameObject);
            }

            if (blacksmith != null)
            {
                bool hasAnvil = false;
                foreach (Transform c in blacksmith.transform)
                    if (c != null && c.name == "Anvil") { hasAnvil = true; break; }
                if (!hasAnvil)
                {
                    var anvilPrefab = Resources.Load<GameObject>("Structures/Anvil");
                    if (anvilPrefab == null)
                        Debug.LogWarning("[CastleHubBuilder] EnsureCastleTownProps: Resources/Structures/Anvil not found — Blacksmith Anvil not added.");
                    else
                    {
                        var anvil = (GameObject)PrefabUtility.InstantiatePrefab(anvilPrefab);
                        anvil.transform.SetParent(blacksmith.transform, false);
                        anvil.transform.localPosition = new Vector3(0f, 0f, 5f);
                        anvil.name = "Anvil";
                        Log("EnsureCastleTownProps: added Anvil at the Blacksmith storefront (local (0,0,5)).");
                    }
                }
                else Log("EnsureCastleTownProps: Blacksmith already has an Anvil — idempotent skip.");
            }
            else Log("EnsureCastleTownProps: Blacksmith_Weapons_Storefront not found — Anvil placement skipped.");
        }

        // Collect every Transform in the open scene (all root objects + descendants), so cleanup
        // can find orphan props regardless of where the owner dropped them in the hierarchy.
        private static List<Transform> CollectAllTransforms()
        {
            var all = new List<Transform>();
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                all.AddRange(root.GetComponentsInChildren<Transform>(true));
            }
            return all;
        }

        // =====================================================================
        //  CASTLE BRIDGE SEAM (WO bridge) — a REAL walkable crossing from the castle
        //  south gate out to OuterWorld, replacing the broken/masked warp.
        // -----------------------------------------------------------------------------
        //  ADD-ONLY: this NEVER calls BuildCastleHub / regenerates MainCastle_Hall (the
        //  owner's hand-dialed wall/structure/Heart/hero-spawn/recipe offsets must survive).
        //  It opens the saved scene, drops a CastleBridgeSeam subtree under CastleHubRoot,
        //  and bakes — nothing else is touched.
        //
        //  Pieces (all under a CastleBridgeSeam host @ origin so local == world):
        //   • Bridge_Deck_Visual  — polyperfect stone bridge prefab at (gateX,0,-61.3)
        //   • Floor_Bridge_Nav     — invisible forced-walkable plane spanning the deck
        //   • NavLink_CastleBridge — DIRECT Unity.AI.Navigation.NavMeshLink (the real seam
        //     crossing), mirroring EnemyStrongholdBuilder.BuildNavLink verbatim (UpdateLink()
        //     + Guard.Try + loud FlowTrace.Fail). start=(gateX,0,-57) end=(gateX,0,-65) w=7.
        //  The exit SceneTransitionTrigger is RELOCATED to the FAR end of the bridge so the
        //  fade-to-OuterWorld fires after the hero has walked the crossing; targetPosition is
        //  set to the SAME spot the hero already stands (gateX,0.5,-66) so the post-fade
        //  WarpTo lands where they are → no visible jump.
        //
        //  WHY the direct link supersedes WireOuterWorldConnection's reflection block (~1005):
        //  that block writes m_StartPoint/m_EndPoint via SerializedObject and never calls
        //  UpdateLink(), so the link's runtime nav geometry was never built — a dead no-op.
        // =====================================================================
        private const string BridgeSeamRootName = "CastleBridgeSeam";

        [MenuItem("Defenders/World/Add Castle Bridge Seam")]
        public static void AddCastleBridgeSeam()
        {
            // ARCHITECTURE (proven by the 2026-06-19 bake data): a cross-scene NavMeshLink CANNOT
            // work here. MainCastle_Hall and OuterWorld are two scenes baked at the SAME origin; in
            // this single-scene batch bake OuterWorld is not loaded, so the link's far endpoint has no
            // navmesh to attach to — it dangles. Per the world-architecture canon (stacked regions,
            // diegetic gates), the crossing into OuterWorld is a MASKED WARP, not a continuous walk.
            // So BridgeLinkWalkable=false: we do NOT build the dangling link. What we DO build is a
            // CONTINUOUS walkable deck from the south gate out to the far-end trigger (one fused castle
            // navmesh — the player genuinely walks the bridge), then the trigger masked-warps across.
            const bool BridgeLinkWalkable = false;

            FlowTrace.Step("BridgeSeam", "AddCastleBridgeSeam: begin — ADD-ONLY bridge crossing.");

            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single); // NEVER BuildCastleHub
            Log("BRIDGE-SEAM: opened " + scenePath + " (no regen — hand-dialed offsets preserved).");

            var root = GameObject.Find(RootName);
            if (root == null)
            {
                FlowTrace.Fail("BridgeSeam", "CastleHubRoot not found — cannot add bridge seam. Scene may be empty/unbuilt.");
                Err("BRIDGE-SEAM: " + RootName + " not found — aborting (ADD-ONLY requires an existing hub).");
                return;
            }

            // Idempotent: destroy any prior CastleBridgeSeam subtree so a re-run leaves exactly one.
            var priorSeam = GameObject.Find(BridgeSeamRootName);
            if (priorSeam != null) { Object.DestroyImmediate(priorSeam); Log("BRIDGE-SEAM: removed prior " + BridgeSeamRootName + " subtree."); }

            var seamRoot = new GameObject(BridgeSeamRootName);
            seamRoot.transform.SetParent(root.transform, false);
            seamRoot.transform.position = Vector3.zero; // host at origin → endpoint local == world

            float gateX = ReadSouthGatePos().x;        // recipe Gate_South x ≈ -4.37
            const float deckZ = -61.3f;                 // bridge centre, just outside the south gate

            // --- 1. Visual bridge deck (polyperfect stone bridge). Skip-safe on a missing pack. ---
            const string bridgePrefabPath =
                "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Medieval_M/Bridge_Medieval_Stone.prefab";
            var bridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bridgePrefabPath);
            var deckPos = new Vector3(gateX, 0f, deckZ);
            if (bridgePrefab != null)
            {
                var deck = (GameObject)PrefabUtility.InstantiatePrefab(bridgePrefab, seamRoot.transform);
                deck.name = "Bridge_Deck_Visual";
                deck.transform.position = deckPos;
                MarkStatic(deck);
                Log("BRIDGE-SEAM: visual bridge placed at " + deckPos + ".");
            }
            else
            {
                Debug.LogWarning("[CastleHubBuilder] Bridge prefab not found (" + bridgePrefabPath +
                                 ") — pack may not be imported. Falling back to a tinted primitive deck.");
                var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
                deck.name = "Bridge_Deck_Visual";
                deck.transform.SetParent(seamRoot.transform, false);
                deck.transform.position = deckPos;
                deck.transform.localScale = new Vector3(7f, 0.4f, 16f); // ~7m wide × ~16m long deck
                var dr = deck.GetComponent<MeshRenderer>();
                if (dr != null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (shader != null) dr.sharedMaterial = new Material(shader) { color = new Color(0.40f, 0.37f, 0.33f) };
                }
                MarkStatic(deck);
            }

            // --- 2. Walkable deck nav plane (invisible, forced walkable). Clone CreateInvisibleFloor. ---
            // CRITICAL FIX (2026-06-19): the deck must be CONTINUOUS from the south gate out to the
            // far-end trigger so it FUSES with the courtyard navmesh on bake — a deck floating only at
            // z=-61 left a ~13m void to the gate (z≈-40.6) and never connected (trigger read off-mesh,
            // GATE_NAV_FAIL). Span the gate→far-end strip: centre z = midpoint, length covers gate to
            // the trigger with overlap into the courtyard so the bake welds them into ONE surface.
            float gateZ      = ReadSouthGatePos().z;           // recipe Gate_South z ≈ -40.6
            const float farEndZ = -63.0f;                       // walkable deck far end (trigger sits here)
            float deckCentreZ = (gateZ + farEndZ) * 0.5f;       // ≈ -51.8
            float deckLenZ    = Mathf.Abs(farEndZ - gateZ) + 6f; // +6m overlap into the courtyard for the weld
            // Unity plane is 10×10m @ scale 1 → scale.z = lenZ/10, scale.x = width(7m)/10 = 0.7.
            CreateInvisibleFloor(seamRoot.transform, "Floor_Bridge_Nav",
                new Vector3(gateX, 0f, deckCentreZ), new Vector3(0.7f, 1f, deckLenZ / 10f));
            Log("BRIDGE-SEAM: CONTINUOUS walkable deck Floor_Bridge_Nav centre=(" + gateX + ",0," +
                deckCentreZ.ToString("F1") + ") len≈" + deckLenZ.ToString("F1") + "m (gate " + gateZ.ToString("F1") +
                " → far end " + farEndZ + ") — welds to courtyard.");

            // --- 3. The DIRECT NavMeshLink crossing (the real seam). Built only when walkable. ---
            if (BridgeLinkWalkable)
            {
                BuildBridgeNavLink(seamRoot.transform, "NavLink_CastleBridge",
                    new Vector3(gateX, 0f, -57.0f),   // start: courtyard side of the gate
                    new Vector3(gateX, 0f, -65.0f),   // end: out on the bridge far end
                    7f);
            }
            else
            {
                FlowTrace.Warn("BridgeSeam",
                    "BridgeLinkWalkable=false — NavMeshLink SKIPPED (MINIMAL fallback: visual bridge + far-end trigger + masked warp).");
            }

            // --- 4. Exit seam: re-wire via the existing helper, then RELOCATE to the bridge far end. ---
            // EnsureExitSeamAtRecipeGate rebuilds the marker + SceneTransitionTrigger with the existing
            // (proven) reflection-field wiring. We then nudge ONLY position + targetPosition via the
            // SAME SerializedObject/reflection approach so we never break that field wiring.
            EnsureExitSeamAtRecipeGate();
            RelocateExitSeamToBridge(gateX, BridgeLinkWalkable);

            // --- 5. Rebuild the nav floor so Floor_Bridge_Nav is collected, then bake + persist + save. ---
            BuildNavMeshFloor(root.transform);
            Log("BRIDGE-SEAM: nav floor rebuilt (Floor_Bridge_Nav collected into the bake set).");
            BakeAllCastleSurfacesAndPersist("BRIDGE-SEAM");

            // WO-1731: BakeAllCastleSurfacesAndPersist re-pointed m_NavMeshData; an unsaved scene
            // keeps the deleted guid and ships with NO navmesh, silently. THROW
            // (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException(
                    "BRIDGE-SEAM: could not save navigation for the castle hub scene -- the bake would " +
                    "leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Log("BRIDGE-SEAM: saved scene + assets.");

            // --- 6. Verify LOUD: the wired exit seam path AND the courtyard→bridge-end path complete. ---
            bool seamOk = CastleGateNavVerify.VerifyOpenScene(out string detail);
            Log("BRIDGE-SEAM: gate-nav verify -> " + (seamOk ? "GATE_NAV_OK" : "GATE_NAV_FAIL") + " :: " + detail);
            if (!seamOk)
                FlowTrace.Fail("BridgeSeam", "VerifyOpenScene reports the exit seam is NOT exitable :: " + detail);

            // HONEST walkable-approach assert: the hero must be able to WALK from a courtyard point to
            // the far-end trigger ON the castle deck. Target the trigger (z=-63), NOT z=-66 — the old
            // -66 target snapped onto OuterWorld's same-origin stacked navmesh and FALSE-GREENED a
            // "walkable" crossing that did not exist. Tight end tolerance (1.0m) so a snap can't lie:
            // the trigger must sit on genuinely-connected castle navmesh for this to pass.
            var courtyard = new Vector3(0f, 0f, 0f);
            var deckTrigger = new Vector3(gateX, 0f, -63f);
            bool sStart = NavMesh.SamplePosition(courtyard, out NavMeshHit hStart, 5f, NavMesh.AllAreas);
            bool sEnd   = NavMesh.SamplePosition(deckTrigger, out NavMeshHit hEnd, 1f, NavMesh.AllAreas);
            if (sStart && sEnd)
            {
                var path = new NavMeshPath();
                NavMesh.CalculatePath(hStart.position, hEnd.position, NavMesh.AllAreas, path);
                if (path.status == NavMeshPathStatus.PathComplete)
                    FlowTrace.Step("BridgeSeam",
                        "PATH-COMPLETE courtyard(0,0,0) -> deckTrigger(" + gateX + ",0,-63) — the continuous deck welds to the " +
                        "courtyard; the hero walks the bridge to the trigger, then masked-warps to OuterWorld. Approach is walkable.");
                else
                    FlowTrace.Fail("BridgeSeam",
                        "courtyard -> deckTrigger path is " + path.status + " — the bridge deck did NOT weld to the courtyard " +
                        "navmesh (gap between the south gate and the deck). FIX: widen the deck overlap into the courtyard so the bake fuses them.");
            }
            else
            {
                FlowTrace.Fail("BridgeSeam",
                    "could not sample courtyard(onMesh=" + sStart + ") or deckTrigger(onMesh=" + sEnd + ") onto the navmesh — " +
                    "the bridge deck did not bake walkable (check Floor_Bridge_Nav is in the NavMeshSurface collect bounds).");
            }

            Selection.activeGameObject = seamRoot;
            FlowTrace.Step("BridgeSeam", "AddCastleBridgeSeam: done.");
        }

        // =====================================================================
        //  REMOVE CASTLE BRIDGE SEAM (WO-468 cleanup) — the INVERSE of AddCastleBridgeSeam.
        // -----------------------------------------------------------------------------
        //  Two playtest flags fixed here:
        //   1. The ugly misplaced "bridge tile" the player saw leaving the castle — the whole
        //      CastleBridgeSeam subtree (Bridge_Deck_Visual / Floor_Bridge_Nav / the relocated
        //      WorldGate_ConnectToOuterWorld_Marker) is destroyed.
        //   2. The legacy in-town OutpostConnector_* seams ([SeamTrace] caught them active
        //      16-41m from the hero INSIDE MainCastle_Hall) — stripped (WO-453: outposts are
        //      reached by walking; ff.outposttravel is OFF).
        //
        //  Then the CLEAN exit seam is RESTORED to the recipe south gate by reusing
        //  EnsureExitSeamAtRecipeGate (rebuilds the marker + SceneTransitionTrigger with the
        //  proven field wiring: targetSceneName="OuterWorld", targetPosition=(0,0.5,-12),
        //  loadAdditive=true, radius 12, requireConfirm=false) — we do NOT duplicate that
        //  field-wiring logic, we call it. Re-bake + persist + save, then verify LOUD.
        //
        //  ADD/EDIT-ONLY: opens MainCastle_Hall Single (NEVER BuildCastleHub — the hand-dialed
        //  castle offsets must be preserved). Idempotent: a no-op (clean restore) if the bridge
        //  seam + connectors are already gone.
        //  Mirrors AddCastleBridgeSeam's structure/idioms so it reads as its inverse.
        // =====================================================================
        [MenuItem("Defenders/World/Remove Castle Bridge Seam")]
        public static void RemoveCastleBridgeSeam()
        {
            FlowTrace.Step("BridgeSeam", "RemoveCastleBridgeSeam: begin — INVERSE of AddCastleBridgeSeam (restore clean exit seam).");

            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single); // NEVER BuildCastleHub
            Log("REMOVE-BRIDGE-SEAM: opened " + scenePath + " (no regen — hand-dialed offsets preserved).");

            var root = GameObject.Find(RootName);
            if (root == null)
            {
                FlowTrace.Fail("BridgeSeam", "CastleHubRoot not found — cannot restore the exit seam. Scene may be empty/unbuilt.");
                Err("REMOVE-BRIDGE-SEAM: " + RootName + " not found — aborting (ADD/EDIT-ONLY requires an existing hub).");
                return;
            }

            // --- 1. Destroy the entire CastleBridgeSeam subtree (idempotent). ---
            Guard.Try("BridgeSeam", "destroy CastleBridgeSeam subtree", () =>
            {
                var priorSeam = GameObject.Find(BridgeSeamRootName);
                if (priorSeam != null)
                {
                    Object.DestroyImmediate(priorSeam);
                    Log("REMOVE-BRIDGE-SEAM: destroyed " + BridgeSeamRootName + " subtree (Bridge_Deck_Visual / Floor_Bridge_Nav / relocated marker).");
                    FlowTrace.Step("BridgeSeam", "CastleBridgeSeam subtree destroyed — the misplaced bridge tile is gone.");
                }
                else
                {
                    FlowTrace.Step("BridgeSeam", "no CastleBridgeSeam subtree present — already clean (idempotent).");
                    Log("REMOVE-BRIDGE-SEAM: no " + BridgeSeamRootName + " subtree to remove (idempotent).");
                }
            });

            // --- 2. Strip the legacy in-town OutpostConnector_* seams (WO-453 walk-to). ---
            RemoveInTownOutpostConnectors(root.transform);

            // --- 3. Restore the clean exit seam at the recipe south gate (reuse the proven helper). ---
            // EnsureExitSeamAtRecipeGate rebuilds WorldGate_ConnectToOuterWorld_Marker + its
            // SceneTransitionTrigger with the proven field wiring (targetSceneName="OuterWorld",
            // targetPosition=(0,0.5,-12), loadAdditive=true). It also clears any prior marker/trigger
            // first, so any marker the bridge relocated is replaced cleanly. We do NOT re-wire fields here.
            Guard.Try("BridgeSeam", "restore clean exit seam at recipe gate", () =>
            {
                EnsureExitSeamAtRecipeGate();
                FlowTrace.Step("BridgeSeam",
                    "legacy exit seam purged + plain nav marker restored at the recipe south gate via " +
                    "EnsureExitSeamAtRecipeGate (trigger RETIRED 2026-07-02 — crossing = RuntimeRegionGate warp gates).");
            });

            // --- 4. Rebuild the nav floor (drop Floor_Bridge_Nav from the bake set), bake + persist. ---
            Guard.Try("BridgeSeam", "rebuild nav floor + bake + persist", () =>
            {
                BuildNavMeshFloor(root.transform);
                Log("REMOVE-BRIDGE-SEAM: nav floor rebuilt (bridge deck nav no longer collected).");
                BakeAllCastleSurfacesAndPersist("REMOVE-BRIDGE-SEAM");
            });

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Log("REMOVE-BRIDGE-SEAM: saved scene + assets.");

            // --- 5. Verify LOUD: the restored exit seam path completes (GATE_NAV_OK / FAIL). ---
            bool seamOk = CastleGateNavVerify.VerifyOpenScene(out string detail);
            Log("REMOVE-BRIDGE-SEAM: gate-nav verify -> " + (seamOk ? "GATE_NAV_OK" : "GATE_NAV_FAIL") + " :: " + detail);
            if (!seamOk)
                FlowTrace.Fail("BridgeSeam",
                    "VerifyOpenScene reports the restored exit seam is NOT exitable :: " + detail +
                    " (the south-gate seam must reach the courtyard navmesh after the bridge removal).");
            else
                FlowTrace.Step("BridgeSeam", "GATE_NAV_OK — restored south-gate exit seam is reachable.");

            FlowTrace.Step("BridgeSeam", "RemoveCastleBridgeSeam: done.");
        }

        // Strip every in-town OutpostConnector_* seam from the castle (WO-468 / WO-453 / WO-584).
        // These were baked by the now-REMOVED WireOutpostConnectors as castle->garrison fast-travel
        // triggers; outposts are NOT reached by walk-to gate seams (entered via cave -> loading-zone
        // -> OutpostResolver), so any stale OutpostConnector_* baked into an older saved scene must be
        // scrubbed (a [SeamTrace] caught them active 16-41m from the hero inside MainCastle_Hall).
        // Scans the WHOLE scene (not just under root) so a connector parented elsewhere is still
        // caught. Idempotent + null-safe; logs each removal. (The wiring source is gone, so a re-bake
        // will not re-introduce them.)
        private static void RemoveInTownOutpostConnectors(Transform root)
        {
            Guard.Try("Seam", "strip in-town OutpostConnector_* seams", () =>
            {
                int removed = 0;
                // Snapshot first (we DestroyImmediate while iterating). Whole-scene sweep.
                var toRemove = new List<GameObject>();
                foreach (var t in CollectAllTransforms())
                {
                    if (t == null) continue;
                    if (t.name != null && t.name.StartsWith("OutpostConnector_"))
                        toRemove.Add(t.gameObject);
                }
                foreach (var go in toRemove)
                {
                    if (go == null) continue;
                    string n = go.name;
                    Vector3 p = go.transform.position;
                    Object.DestroyImmediate(go);
                    removed++;
                    FlowTrace.Step("Seam", "removed in-town outpost connector '" + n + "' @ " + p + " (WO-453 walk-to; ff.outposttravel OFF).");
                }
                if (removed > 0)
                    Log("REMOVE-BRIDGE-SEAM: stripped " + removed + " in-town OutpostConnector_* seam(s) — outposts reached by walking (WO-453).");
                else
                    Log("REMOVE-BRIDGE-SEAM: no in-town OutpostConnector_* seams present (idempotent).");
            });
        }

        // Relocate the exit SceneTransitionTrigger to the bridge FAR end and (when the link is
        // walkable) land its post-fade WarpTo on the SAME spot the hero already stands → no jump.
        // Uses the SAME SerializedObject/reflection-field approach the existing seam code uses so the
        // proven field wiring (targetSceneName / loadAdditive / requireConfirm / ProximityRadius) is
        // preserved; we only adjust transform position + targetPosition here.
        private static void RelocateExitSeamToBridge(float gateX, bool bridgeLinkWalkable)
        {
            var marker = GameObject.Find("WorldGate_ConnectToOuterWorld_Marker");
            if (marker == null)
            {
                FlowTrace.Warn("BridgeSeam", "exit-seam marker not found after EnsureExitSeamAtRecipeGate — relocation skipped.");
                return;
            }

            // Seat the trigger host EXACTLY on the proven-walkable deck point (world (gateX,0,-63)).
            // The PATH-COMPLETE assert proves courtyard->(gateX,0,-63) is a complete walkable route at
            // deck-navmesh level (y≈0.06). A y=0.5 host floated above that mesh so the verify's tight
            // 1.0m sample missed at the far edge. y=0 puts the trigger on the navmesh the hero walks —
            // proximity (6m+) still fires, and the strict spawn->trigger CalculatePath now resolves.
            marker.transform.position = new Vector3(gateX, 0f, -63f);

            var transType = FindType("DeNelle.Village.SceneTransitionTrigger");
            if (transType == null)
            {
                FlowTrace.Warn("BridgeSeam", "SceneTransitionTrigger type not found — targetPosition not re-pointed.");
                return;
            }
            var comp = marker.GetComponent(transType);
            if (comp == null)
            {
                FlowTrace.Warn("BridgeSeam", "no SceneTransitionTrigger on the marker — targetPosition not re-pointed.");
                return;
            }

            // Keep the destination scene additive (mirror the existing wiring).
            transType.GetField("targetSceneName")?.SetValue(comp, "OuterWorld");
            transType.GetField("loadAdditive")?.SetValue(comp, true);

            // When the link bridges, land the WarpTo where the hero already is (gateX,0.5,-66) so the
            // post-fade reposition is invisible. When falling back (no link), leave the existing near-
            // origin masked-warp target untouched (MINIMAL) — only the trigger MOVED.
            if (bridgeLinkWalkable)
            {
                transType.GetField("targetPosition")?.SetValue(comp, new Vector3(gateX, 0.5f, -66f));
                Log("BRIDGE-SEAM: exit seam relocated to bridge far end (" + gateX + ",0,-63), deck-seated; WarpTo same-spot (" + gateX + ",0.5,-66) → no jump.");
            }
            else
            {
                Log("BRIDGE-SEAM: exit seam relocated to bridge far end (" + gateX + ",0,-63), deck-seated; masked-warp target left intact (BridgeLinkWalkable=false).");
            }
        }

        // =====================================================================
        //  SEAMLESS OUTERWORLD SEAM (WO-468 Phase 2 — un-stack + WALK, no warp)
        //  OuterWorld is now built SIDE-BY-SIDE (south of, not stacked under) the castle
        //  (ExteriorTerrainBuilder TerrainCenterZ=-572, north edge z=-72), so a real
        //  NavMeshLink can bridge the two ADJACENT navmeshes at runtime (both load
        //  additively; WorldSceneLoader pulls OuterWorld on castle entry). This REPLACES
        //  the confirm-to-cross warp with a WALK:
        //   - strip the WorldGate warp trigger (no prompt, no teleport),
        //   - a VISUAL-ONLY ground filler across the ~10 m seam (no collider -> no navmesh
        //     overlap; the link carries the agent across the gap),
        //   - a direct NavMeshLink castle-floor-edge -> OuterWorld north edge.
        //  Editor must be CLOSED; rebakes the castle nav. Run AFTER the un-stacked OuterWorld
        //  is built + baked.
        // =====================================================================
        [MenuItem("Defenders/World/Build Seamless OuterWorld Seam")]
        public static void BuildSeamlessOuterWorldSeam()
        {
            FlowTrace.Step("BridgeSeam", "BuildSeamlessOuterWorldSeam: begin — WALK into the un-stacked OuterWorld (no warp).");
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                FlowTrace.Fail("BridgeSeam", "CastleHubRoot not found — cannot build the seamless seam.");
                return;
            }

            float gateX = ReadSouthGatePos().x;

            // 1. Idempotent: clear any prior seamless-seam subtree.
            var prior = GameObject.Find("SeamlessOuterWorldSeam");
            if (prior != null) Object.DestroyImmediate(prior);
            var seamRoot = new GameObject("SeamlessOuterWorldSeam");
            seamRoot.transform.SetParent(root.transform, false);

            // 2. Strip the confirm-to-cross WARP trigger — crossing is a WALK now, so no
            //    prompt/teleport competes with the link. (WorldSceneLoader still loads
            //    OuterWorld additively on castle entry, so removing this never breaks loading.)
            var marker = GameObject.Find("WorldGate_ConnectToOuterWorld_Marker");
            if (marker != null)
            {
                var transType = FindType("DeNelle.Village.SceneTransitionTrigger");
                var comp = transType != null ? marker.GetComponent(transType) : null;
                if (comp != null)
                {
                    Object.DestroyImmediate(comp);
                    FlowTrace.Step("BridgeSeam", "removed the confirm-warp SceneTransitionTrigger — the castle->OuterWorld crossing is a WALK.");
                }
            }

            // 3. NO visual ground filler. The earlier Seam_Ground_Filler primitive used a runtime
            //    `Shader.Find("Universal Render Pipeline/Lit")` which returns NULL in batchmode, so the
            //    plane kept its default Standard material — which renders MAGENTA in URP ("pink ground",
            //    owner flag 2026-06-20). The seam gap is small and the hero slides across it in <1s, so
            //    we drop the filler entirely rather than ship a magenta plane. (If a visible gap proves
            //    objectionable, re-add a filler using an EXISTING URP material asset, not Shader.Find.)

            // 4. The real crossing: a direct cross-moat NavMeshLink PER GATE (castle-floor-edge ->
            //    OuterWorld edge). Each connects at RUNTIME when OuterWorld is additively loaded (the
            //    EnemyStronghold pattern); its far end is off-mesh at THIS solo castle bake — expected,
            //    it binds live. The SOUTH pair below is the proven-working crossing (AssertHeroCrossing
            //    OK); the other three gates ARE the south gate rotated about the world Y origin (the
            //    SAME {S:0,W:90,N:180,E:270} symmetry the bridges/gates use — CastleMoatBuilder.
            //    BuildDrawbridges), so we rotate the two south endpoints per side and bake ONE link
            //    each. This is the §12 fix for the captured `[Flow:MoatVerify] CHECK5 'West'/'North'/
            //    'East': path PathPartial — crossing NOT traversable` / `MOAT_INCOMPLETE`: W/N/E had NO
            //    baked cross-moat link (only South did) and the runtime BuildAiLink kept PathPartialing
            //    (`RUNTIME_SEAM_NAV_FAIL`). One baked link per side makes all four crossings traversable.
            Vector3 southInner = new Vector3(gateX, 0f, -63f);   // on castle navmesh (CourtyardFloor_Nav edge ~-65)
            Vector3 southOuter = new Vector3(gateX, 0f, -76f);   // on OuterWorld navmesh (north edge -72)
            var seamSides = new (string tag, float yaw)[] { ("S", 0f), ("W", 90f), ("N", 180f), ("E", 270f) };
            foreach (var (tag, yaw) in seamSides)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);   // about world Y origin — mirrors the bridge yaw
                BuildBridgeNavLink(seamRoot.transform, "NavLink_CastleToOuterWorld_" + tag,
                    rot * southInner,   // castle-inner endpoint, rotated to this side's radial
                    rot * southOuter,   // OuterWorld-outer endpoint, rotated to this side's radial
                    10f);
            }

            // 5. Rebake the castle nav (filler has no collider, so the castle surface is unchanged)
            //    + persist + save.
            BakeAllCastleSurfacesAndPersist("SEAMLESS-SEAM");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            FlowTrace.Step("BridgeSeam", "BuildSeamlessOuterWorldSeam: done — WALK via NavLink_CastleToOuterWorld (runtime-connected to additively-loaded OuterWorld).");
        }

        // DIRECT NavMeshLink for the bridge crossing — copied verbatim from
        // EnemyStrongholdBuilder.BuildNavLink (UpdateLink() + Guard.Try + loud FlowTrace.Fail).
        // Uses Unity.AI.Navigation.NavMeshLink DIRECTLY (the DeNelle.Editor asmdef references the
        // package; EnemyStrongholdBuilder proves it resolves). LOUD FAIL on any miss — no silent link.
        private static void BuildBridgeNavLink(Transform parent, string name, Vector3 worldStart,
            Vector3 worldEnd, float width)
        {
            bool ok = Guard.Try("BridgeSeam", $"add NavMeshLink '{name}'", () =>
            {
                var host = new GameObject(name);
                host.transform.SetParent(parent, false);
                host.transform.position = Vector3.zero;   // endpoints below are absolute (link space == host local @ origin)

                var link = host.AddComponent<NavMeshLink>();
                if (link == null)
                    throw new System.Exception("AddComponent<NavMeshLink> returned null.");

                link.startPoint    = worldStart;
                link.endPoint      = worldEnd;
                link.width         = width;
                link.bidirectional = true;
                link.area          = 0;          // Walkable
                link.UpdateLink();
                FlowTrace.Step("BridgeSeam",
                    $"NavMeshLink '{name}' start={worldStart} end={worldEnd} width={width} — castle↔OuterWorld seam bridged.");
            });

            if (!ok)
                FlowTrace.Fail("BridgeSeam",
                    $"NavMeshLink '{name}' FAILED to create — the bridge crossing ({worldStart} -> {worldEnd}) " +
                    "will NOT be pathable. (Unity.AI.Navigation must resolve; do NOT ship this silently.)");
        }

        // Mark a GameObject NavigationStatic + batching/occlusion static so the bake collects it
        // (mirrors EnemyStrongholdBuilder.MarkStatic — this file had no such helper).
        private static void MarkStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        // =====================================================================
        //  LEGACY INTERIOR EXIT SEAM — RETIRED (owner F8 2026-07-02 "travel to outer
        //  world should not show").
        // -----------------------------------------------------------------------------
        //  §12 captured cause: "BATCH-RECIPE: exit seam placed INTERIOR of recipe gate
        //  (-4.37, 1.50, -37.60) (gate.z+3, radius 12, target OuterWorld (0,0.5,-12))".
        //  This method used to bake a SceneTransitionTrigger 3m INSIDE the courtyard.
        //  SceneTransitionTrigger floors every non-walk-up confirm seam's radius to
        //  ConfirmMinRadius=40m (SceneTransitionTrigger.cs EffRadius), so the baked
        //  radius-12 trigger actually blanketed the courtyard — the purple "Travel to
        //  the Outer World" prompt showed at the FOUNTAIN, nowhere near a gate.
        //
        //  RETIRE, not gate, because the crossing is fully covered without it (canon
        //  CANON_GROUND_TRUTH_2026-07-01): the four RuntimeRegionGate warp gates build
        //  their OWN per-gate SceneTransitionTrigger (suppressPrompt=true, threshold-
        //  seated OUTSIDE the gate) + HeroLinkCrossing warp pair at runtime. Nothing
        //  else consumed the baked trigger:
        //   - the GATE_NAV_OK bake verify (CastleGateNavVerify.VerifyOpenScene) now
        //     falls back to path-testing the MARKER kept below (assertion preserved);
        //   - AutoPilot's SEAM-REACHABLE probes whatever triggers exist at runtime
        //     (the RuntimeRegionGate ones) — unaffected.
        //
        //  This method now: (a) PURGES any prior marker + EVERY baked
        //  SceneTransitionTrigger (so a rebake also scrubs the stale trigger out of
        //  the saved scene), and (b) leaves a PLAIN nav marker (no trigger, no
        //  collider, no prompt) at the same interior lane point, seated on the raised
        //  courtyard floor (CastleFootprintLiftY), so the bake's nav-reachability
        //  assertion still has its target.
        // =====================================================================
        private static void EnsureExitSeamAtRecipeGate()
        {
            Vector3 gate = ReadSouthGatePos(); // recipe Gate_South, e.g. (-4.37,0,-40.6)

            var priorMarker = GameObject.Find("WorldGate_ConnectToOuterWorld_Marker");
            if (priorMarker != null) Object.DestroyImmediate(priorMarker);
            var transTypeClean = FindType("DeNelle.Village.SceneTransitionTrigger");
            int purged = 0;
            if (transTypeClean != null)
                foreach (var c in Object.FindObjectsByType(transTypeClean, FindObjectsSortMode.None))
                {
                    var mb = c as MonoBehaviour;
                    if (mb != null) { Object.DestroyImmediate(mb.gameObject); purged++; }
                }

            var root = GameObject.Find(RootName);
            var marker = new GameObject("WorldGate_ConnectToOuterWorld_Marker");
            if (root != null) marker.transform.SetParent(root.transform, false);
            // Same interior lane point the retired trigger used (gate.z + 3, solid
            // CourtyardFloor_Nav), but seated at the raised floor height (WO-593 island
            // raise) so the verify's tight 1.0m navmesh sample lands on the real sheet.
            marker.transform.position = new Vector3(gate.x, CastleFootprintLiftY, gate.z + 3f);

            Log("BATCH-RECIPE: legacy interior exit seam RETIRED (owner F8 2026-07-02) — purged " +
                purged + " baked SceneTransitionTrigger(s); plain nav marker kept at " +
                marker.transform.position + " for the GATE_NAV verify. Crossing = the four " +
                "RuntimeRegionGate warp gates (CANON_GROUND_TRUTH_2026-07-01).");
        }

        // =====================================================================
        //  HEART OF ELARION (T-002 / T-005) — the enemy target + lose condition.
        // -----------------------------------------------------------------------------
        //  CastleHubBuilder never placed a Heart, so WaveManager._heart stayed null and
        //  Enemy.DriveNav() early-returned (enemies froze -> "no enemy engagement" T-005,
        //  "no tree of life / what does the enemy target" T-002).
        //
        //  COLLIDER-SCALE TRAP (memory heart-collider-scale-trap; mirrors
        //  Village2Playable.B1_WireHeart): HeartController.Awake() auto-adds a CapsuleCollider
        //  (r2, h8 LOCAL). Hosting it on a SCALED visible tree turns that into a giant plaza
        //  blocker (the Tree-of-Life-blocked-the-whole-plaza bug). So the HeartController lives
        //  on a CLEAN scale-1 anchor "HeartOfElarion"; an optional decorative tree mesh is a
        //  collider-stripped child for looks only.
        //
        //  POSITION: (0,0,12) — the north-centre plaza in front of the keep (the keep occupies
        //  origin). y=0 sits on the courtyard floor / baked navmesh. Tag "HeartTarget" (the tag
        //  EnemyBrain.FindClosestTarget searches). _useAuthoredTransform=true so Awake() does not
        //  re-scale/re-snap it to origin.
        //
        //  Idempotent: re-running reuses the existing anchor + skips a duplicate controller, so
        //  there is EXACTLY ONE HeartController in the scene.
        // =====================================================================
        private const string HeartAnchorName = "HeartOfElarion";
        /// <summary>Single planar authority for the merged home's Heart seat (X/Z).</summary>
        public static Vector2 AuthoredHeartPlanarSeat => new Vector2(0f, 12f);

        private static void WireCastleHeart(Transform parent)
        {
            var heartType = FindType("DeNelle.Village.HeartController");
            if (heartType == null)
            {
                Err("WireCastleHeart: DeNelle.Village.HeartController not found (is it compiled?). " +
                    "Heart NOT placed — WaveManager._heart stays null and enemies will freeze. Re-run after compile.");
                return;
            }

            // Reuse an existing anchor (idempotent) so we never create a second HeartController.
            var existing = GameObject.Find(HeartAnchorName);
            GameObject anchor = existing != null ? existing : new GameObject(HeartAnchorName);
            if (existing == null && parent != null) anchor.transform.SetParent(parent, false);

            // North-centre plaza, in front of the keep. CLEAN scale-1 anchor (no collider-scale trap).
            anchor.transform.position = new Vector3(
                AuthoredHeartPlanarSeat.x, CastleFootprintLiftY, AuthoredHeartPlanarSeat.y);
            anchor.transform.localScale = Vector3.one;

            // Tag "HeartTarget" so EnemyBrain.FindClosestTarget can find it. The tag may not be
            // defined in TagManager (setting an undefined tag throws) — guard like
            // PlaceCastleSpawnPoints. EnemyBrain.TryFindByTag also guards undefined tags, and the
            // WaveManager wires _heart by component reference, so an unset tag is non-fatal.
            try { anchor.tag = "HeartTarget"; }
            catch { Err("WireCastleHeart: tag 'HeartTarget' is not defined in TagManager — add it (ProjectSettings/TagManager.asset) so EnemyBrain tag-search finds the Heart. WaveManager still wires _heart by reference."); }

            if (anchor.GetComponent(heartType) == null)
            {
                var heart = anchor.AddComponent(heartType);
                var so = new SerializedObject(heart);
                var prop = so.FindProperty("_useAuthoredTransform");
                if (prop != null) { prop.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Err("WireCastleHeart: HeartController._useAuthoredTransform not found — Heart may snap to origin at Play.");
                Log($"WireCastleHeart: HeartController added on '{HeartAnchorName}' at {anchor.transform.position} (scale 1).");
            }
            else Log("WireCastleHeart: HeartController already on the anchor — idempotent skip.");

            // T-002 "no tree of life": the anchor itself is an invisible scale-1 node, so the
            // owner saw NO centerpiece even though the Heart is functional. Add the visible Tree
            // of Life as a COLLIDER-STRIPPED child (the gameplay blocker stays on the clean
            // anchor — no collider-scale trap), bounds-scaled to a ~9m visible height (raw Tripo
            // fbx native size is unknown/large) and ground-seated at Play so it can't float or
            // displace (tripo-mesh-displacement-trap). Idempotent by child name.
            // OWNER 2026-06-12: "Tree IS the Heart at center." Swap the placeholder for the owner's
            // authored fbx (Assets/Art/Tree_Of_Life.fbx) at her exact transform — explicit scale 7,
            // upright via Euler(-90,0,0), riding the Heart anchor at the world center (0,0,12). This
            // is editor code, so AssetDatabase is fine. FORCE-REPLACE the old TreeOfLife_Visual so a
            // re-run swaps the placeholder out instead of skip-if-exists.
            const string TreeChildName = "TreeOfLife_Visual";
            var priorTree = anchor.transform.Find(TreeChildName);
            if (priorTree != null) Object.DestroyImmediate(priorTree.gameObject);

            // OWNER F8 2026-07-02 "tree of life in ground not on ground": SeatOnGroundOnStart now
            // derives the tree's visual base from MESH VERTICES (the fbx's authored renderer AABB is
            // skewed below the root ball, which buried the trunk). Vertex reads in a PLAYER build
            // require Read/Write Enabled on the import — enforce it here (editor/bake time) so the
            // robust seat derivation works in the build too, not just in-editor.
            var treeImporter = AssetImporter.GetAtPath("Assets/Art/Tree_Of_Life.fbx") as ModelImporter;
            if (treeImporter != null && !treeImporter.isReadable)
            {
                treeImporter.isReadable = true;
                treeImporter.SaveAndReimport();
                Log("WireCastleHeart: enabled Read/Write on Tree_Of_Life.fbx so SeatOnGroundOnStart's " +
                    "mesh-vertex base derivation works in player builds.");
            }

            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Tree_Of_Life.fbx");
            if (treePrefab == null)
                Debug.LogWarning("[CastleHubBuilder] WireCastleHeart: Assets/Art/Tree_Of_Life.fbx not found — Heart is functional but has no visible mesh.");
            else
            {
                var tree = Object.Instantiate(treePrefab);
                tree.name = TreeChildName;
                tree.transform.SetParent(anchor.transform, false);
                tree.transform.localPosition = Vector3.zero;
                tree.transform.localScale = Vector3.one * 7f;            // owner's explicit scale
                tree.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // owner's orientation — stands the fbx upright
                foreach (var c in tree.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var matFix = FindType("DeNelle.Core.TreeOfLifeMaterialFixer");
                if (matFix != null && tree.GetComponent(matFix) == null) tree.AddComponent(matFix);
                var seat = FindType("DeNelle.Village.SeatOnGroundOnStart");
                if (seat != null && tree.GetComponent(seat) == null) tree.AddComponent(seat);
                // OWNER F8 2026-07-02 "tree STILL reads buried": the derived seat (lowest
                // vertex on floor) passed the PROP_SEATING oracle, but the fbx's authored
                // root FLARE belongs ABOVE grade. Author a base lift: default 0.4m, and the
                // PlayerPrefs key "tree.baseLift" lets the owner felt-tune it live without a
                // rebake. NOTE: AutoPilotProbes.PropSeatTolerance = 0.75m — a tuned lift
                // above that must raise the oracle tolerance to match.
                var seatComp = seat != null ? tree.GetComponent(seat) : null;
                if (seatComp != null)
                {
                    var seatSo = new SerializedObject(seatComp);
                    var liftProp = seatSo.FindProperty("_baseLiftOverride");
                    var keyProp  = seatSo.FindProperty("_baseLiftPrefsKey");
                    if (liftProp != null && keyProp != null)
                    {
                        liftProp.floatValue  = 0.4f;
                        keyProp.stringValue  = "tree.baseLift";
                        seatSo.ApplyModifiedPropertiesWithoutUndo();
                        Log("WireCastleHeart: tree base lift authored (0.4m default, PlayerPrefs 'tree.baseLift' tunable).");
                    }
                    else Err("WireCastleHeart: SeatOnGroundOnStart._baseLiftOverride/_baseLiftPrefsKey not found — tree base lift NOT authored (stale compile?).");
                }
                Log("WireCastleHeart: visible TreeOfLife added from Assets/Art/Tree_Of_Life.fbx (scale 7, Euler(-90,0,0), colliders stripped, ground-seated).");
            }

            // Defensive: ensure EXACTLY one HeartController in the scene.
            var hearts = Object.FindObjectsByType(heartType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (hearts.Length != 1)
                Err($"WireCastleHeart: expected 1 HeartController, found {hearts.Length}. Remove the extras.");
        }

        // Standalone wire-in for an ALREADY-SAVED MainCastle_Hall (mirrors
        // WireCurrentCastleToOuterWorld): adds the Heart idempotently without a full rebuild,
        // so the CLI can apply T-002/T-005 on the committed scene then rebake.
        [MenuItem("Defenders/Scenes/Add Heart To Current Castle")]
        public static void AddHeartToCurrentCastle()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var rootGo = GameObject.Find(RootName);
            Transform parent = rootGo != null ? rootGo.transform
                : (GameObject.Find(RootName + "_FloorHost") ?? new GameObject(RootName + "_FloorHost")).transform;

            WireCastleHeart(parent);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[CastleHubBuilder] Added Heart of Elarion (HeartTarget) at (0,0,12). " +
                      "Save the scene; a NavMesh REBAKE is recommended so the Heart's blocker capsule is reflected. " +
                      "WaveManager now has a non-null _heart -> enemies engage.");
            var heartGo = GameObject.Find(HeartAnchorName);
            if (heartGo != null) Selection.activeGameObject = heartGo;
        }

        // =====================================================================
        //  BATCH-WAVE — add the COMBAT CORE to the castle (owner 2026-06-12): a WaveManager
        //  + 4 WaveSpawnPoints 12m outside each gate, so the wave timer + DEFEND/Start button
        //  actually work. MainCastle_Hall had NO WaveManager, so the timer read Wave 0 and the
        //  Start button never wired (StartWaveHudBridge only attaches when a WaveManager exists).
        //  Mirrors Village2Playable.ImportWaveManager. Run AFTER the navmesh bake — spawn points
        //  must sit on the baked floor (now ±65). No re-bake needed (gameplay objects, not geometry).
        // =====================================================================
        public static void BatchAddCastleWaveSystem()
        {
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Log("BATCH-WAVE: opened " + scenePath);

            var rootGo = GameObject.Find(RootName);
            Transform root = rootGo != null ? rootGo.transform
                : (GameObject.Find(RootName + "_FloorHost") ?? new GameObject(RootName + "_FloorHost")).transform;

            // T-002/T-005: ensure the Heart exists FIRST so WaveManager._heart is non-null
            // (a null heart freezes Enemy.DriveNav -> "no enemy engagement"). Idempotent.
            WireCastleHeart(root);

            // Heart = lose condition + enemy target. Now resolvable (placed above).
            Component heart = null;
            var heartType = FindType("DeNelle.Village.HeartController");
            if (heartType != null)
            {
                var hs = Object.FindObjectsByType(heartType, FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (hs != null && hs.Length > 0) heart = hs[0] as Component;
            }

            PlaceCastleSpawnPoints(root);
            var wm = Village2Playable.ImportWaveManager(root, heart);
            Log("BATCH-WAVE: WaveManager " + (wm != null ? "wired" : "FAILED") +
                ", heart " + (heart != null ? "wired" : "null (no lose-condition yet)"));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("BATCH-WAVE: castle combat core added + saved.");
        }

        // 4 WaveSpawnPoints, 12m OUTSIDE each gate (recipe-driven, mirrored x4) — on the ±65
        // walkable floor so spawned enemies aren't stranded. WaveManager finds them by COMPONENT
        // type at Start; Configure(spawnId, gateIndex, direction, gatePosition) heads enemies
        // from the spawn through the gate toward the interior.
        private static void PlaceCastleSpawnPoints(Transform root)
        {
            var spType = FindType("DeNelle.Village.WaveSpawnPoint");
            if (spType == null) { Err("PlaceCastleSpawnPoints: WaveSpawnPoint type not found."); return; }
            var cfg = spType.GetMethod("Configure");

            Vector3 south = ReadSouthGatePos(); // recipe Gate_South center
            var sides = new (string dir, float angle)[] { ("S", 0f), ("W", 90f), ("N", 180f), ("E", 270f) };
            int i = 0, made = 0;
            foreach (var s in sides)
            {
                Quaternion rot = Quaternion.Euler(0f, s.angle, 0f);
                Vector3 gate = rot * south;            // gate center (mirror of south)
                Vector3 outward = rot * Vector3.back;  // south outward = -Z, rotated per side
                Vector3 spawn = gate + outward * 12f;
                spawn.y = 0f;

                string name = "WaveSpawnPoint-" + s.dir;
                var existing = root.Find(name);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.position = spawn;
                try { go.tag = "SpawnPoint"; } catch { /* tag optional; discovery is by component */ }
                var sp = go.AddComponent(spType);
                if (cfg != null) cfg.Invoke(sp, new object[] { "spawn-" + i, i, s.dir, gate });
                made++; i++;
            }
            Log("PlaceCastleSpawnPoints: placed " + made + " WaveSpawnPoints 12m outside each gate.");
        }

        private static Transform FindHeroParentInCastle(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                // Prefer the labeled home quarters.
                var home = root.transform.Find("MainKeep_CastleWithTwoLevels_Home/PlayerHeroHall_PersonalQuarters_HomeSpace")
                           ?? root.transform.Find("PlayerHeroHall_PersonalQuarters_HomeSpace");
                if (home != null) return home;

                // Fallbacks
                if (root.name.Contains("PlayerHeroHall") || root.name.Contains("PersonalQuarters") || root.name.Contains("HomeSpace"))
                    return root.transform;

                var keep = root.transform.Find("MainKeep_CastleWithTwoLevels_Home");
                if (keep != null) return keep;
            }
            return null;
        }

        private static void PlaceHeroInCastleHome(Scene scene, GameObject hero)
        {
            // Prefer the marker we (optionally) placed during build.
            var start = GameObject.Find("HeroStartPoint_InsidePersonalQuarters") ?? GameObject.Find("HeroStartPoint_PlayerSpawn");
            if (start != null)
            {
                hero.transform.position = start.transform.position;
                hero.transform.rotation = start.transform.rotation;
                Log("Hero placed at HeroStartPoint inside personal quarters.");
                return;
            }

            // Fallback: place relative to the home quarters using similar logic to CastleWalkable.
            var home = FindHeroParentInCastle(scene);
            if (home != null)
            {
                hero.transform.SetParent(home, false);
                hero.transform.localPosition = new Vector3(0, 0.1f, 0);
                hero.transform.localRotation = Quaternion.identity;
                Log("Hero placed inside home quarters (fallback local pos).");
            }
            else
            {
                hero.transform.position = new Vector3(0, 0.1f, -10); // rough inside keep area
                Log("Hero placed with rough fallback position (no home quarters found).");
            }
        }

        private static void EnsureHeroControllable(GameObject hero)
        {
            if (hero == null) return;
            if (!hero.activeSelf) { hero.SetActive(true); Log("Hero activated."); }

            // HeroLocomotion enable (the control ensurer is frequently hardcoded to Village2).
            var locoType = FindType("DeNelle.Village.HeroLocomotion");
            if (locoType != null)
            {
                var loco = hero.GetComponent(locoType) as Behaviour;
                if (loco != null && !loco.enabled)
                {
                    loco.enabled = true;
                    Log("HeroLocomotion enabled (prevents silent input eat).");
                }
            }

            // Also try to make HeroControlEnsurer happy if present (set its target scene or enable).
            var ensurerType = FindType("DeNelle.Village.HeroControlEnsurer");
            if (ensurerType != null)
            {
                var ensurer = hero.GetComponent(ensurerType) as Behaviour;
                if (ensurer != null && !ensurer.enabled) ensurer.enabled = true;
            }
        }

        private static void Log(string m)  => Debug.Log("[CastleHubBuilder] " + m);
        private static void Err(string m)  => Debug.LogError("[CastleHubBuilder] " + m);
    }
}
