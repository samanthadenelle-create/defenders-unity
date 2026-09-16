// =============================================================================
// KayKitChallengeOutpostBuilder — large script-built enemy outpost / dungeon yard.
// -----------------------------------------------------------------------------
// 100% editor script (no hand-edited scene, CLAUDE.md §3). The gameplay shell is
// collidable boxes + a NavMeshSurface bake; the VISIBLE shell is KayKit Dungeon
// Remastered modular pieces laid at their AUTHORED scale over those boxes.
// Challenging layout: triple ring, multiple chokes, 8 aggro groups, loot props.
//
//   Menu: Defenders/World/Build KayKit Challenge Outpost
//   Batch: DeNelle.Editor.KayKitChallengeOutpostBuilder.Build
//
// -----------------------------------------------------------------------------
// WO-1000 (2026-08-07) — VISUAL OVERHAUL. Everything the owner called out was in
// this one file, and the fix is a TRANSPLANT of the two already-shipped dungeon
// pipelines, not new art direction. What changed and where each number came from:
//
//  1. RELIGHT + SKY KILL (ConfigureAtmosphere). Was: a directional "Sun" at 1.1 and
//     RenderSettings NEVER touched, so the scene inherited EmptyScene's bright
//     procedural skybox and skybox ambient — the daylight wash and the blue sky
//     over the walls. Now flat ambient 0.05, linear fog #0a0a10 14->42 m,
//     skybox null, a faint #39414f directional at 0.18 with shadows OFF.
//     Values copied, not invented:
//       Assets/Editor/RoomForge/DungeonBaker.cs L212-252 (the live composed relight)
//       Assets/Editor/DungeonSceneBuilder.cs L170 AmbientIntensity = 0.05f
//       Assets/Editor/DungeonSceneBuilder.cs L1987-2000 ConfigureAmbient (fog 14->42)
//       Assets/Editor/DungeonSceneBuilder.cs L2018-2029 CreateDirectionalLight (#39414f @ 0.18)
//     Shadows are None, NOT DungeonSceneBuilder's Soft: with a ceiling overhead a
//     shadow-casting directional is occluded completely and the yard goes black
//     (DungeonBaker L244-248 records the same finding).
//
//  2. CEILING (BuildCeiling). Was: none — floor + ring walls only, so the sky leaked
//     and the yard read as a pit in a field. Now the full 56 m span is tiled with
//     KayKit ceiling_tile at wall-top height, colliders stripped so the NavMesh is
//     untouched. Mirrors DungeonSceneBuilder.BuildCeiling L643-658.
//
//  3. TEXTURED STONE SHELL (BuildFloorTiles / CladBox). Was: CreatePrimitive cubes
//     with flat URP/Lit colours. Now every wall box KEEPS its collider (so the nav
//     bake is bit-for-bit the same authority it always was) but LOSES its mesh, and
//     real KayKit wall pieces are laid along it at authored scale. The floor slab
//     likewise stays as the nav floor and is covered with floor_tile_large.
//     The KayKit atlas is NEVER stretched over a primitive — dungeon_texture.png is
//     a grid of solid-colour swatches and a cube maps the whole 0..1 UV per face,
//     which renders as rainbow stripes (WO-1000 §2.2). Only the kit's own pieces,
//     which carry authored UVs, are used.
//
//  4. TORCHES THAT ACTUALLY LIGHT (DressWallTorches). Was: 6 torch FBX floating at
//     y=2.2 in open floor with NO Light component — pure decoration. Now wall
//     brackets on the inner face of every ring, each with a warm point light and an
//     Env_Candle seat. Model choice is NOT free: measured glTF POSITION bounds say
//       torch_mounted  y -0.381..0.682  z 0.000..0.616  -> back plate at z=0, a WALL BRACKET
//       torch_lit      y -0.395..0.731  z -0.275..0.275 -> radially symmetric, FLOOR-STANDING
//       torch          y -0.395..0.647  z -0.275..0.275 -> same, and unlit
//     so only torch_mounted may be seated at wall height; the others would float in
//     mid-air (Assets/Editor/RoomForge/DungeonDresser.cs L42-63). Light dial copied
//     from DungeonDresser L73/L86-88/L259-267: intensity 0.85, colour (1,0.62,0.28),
//     shadows None, range derived from the lit band rather than a re-typed literal.
//
//  5. REAL PROPS (PlaceBreakables). Was: 1 m brown `_crate` cubes. Now real
//     barrel_large / crates_stacked / chest_gold meshes on a bounds-fitted
//     BoxCollider, with the BreakableContainer + lootTableId wiring untouched.
//     ⛔ UPDATED BY WO-1132: these are NO LONGER on the "Enemy" layer, and that
//     collider is NO LONGER a hit seam — the container became an OPENABLE chest
//     (owner ruling 2026-08-21), so the collider is just the prop's physical body
//     and the interaction is a proximity prompt. The old Enemy relayer is what let
//     the hostile reticle lock onto a crate (WO-1047). See
//     Assets/_Modules/Village/World/BreakableContainer.cs.
//
// DELIBERATELY NOT DONE HERE (reported, not faked):
//  • WO-1000 §2.3 asks for an "Env_Candle VfxEmitter". There is NO VfxEmitter type
//    anywhere in this tree (the name exists only inside WORK_ORDER_884), and
//    Env_Candle is a POOLED LOOPING effect (VFXCatalogGenerator L287: isLoop true,
//    poolSize 6) owned by VFXManager in DeNelle.Village — an assembly DeNelle.Editor
//    does not reference and whose global loop-slot budget baking ~27 instances would
//    blow outright. So the bake contributes the one thing only the bake knows: a
//    "CandleAnchor" empty at the measured flame tip. Same call DungeonDresser
//    L273-297 made. The runtime consumer is the remaining seam.
//  • WO-1000 §2.4 ground fog: same shape. PP_GroundFog is a ParticlePack prefab
//    driven through the same pooled VFXManager facade, so this seats "GroundFogAnchor"
//    empties on the floor and leaves the play-site to a runtime pass. The DEPTH half
//    of §2.4 is already delivered by the linear fog in (1).
//  • No Camera is created. This scene has never had one; the runtime rig owns it
//    (HeroControlEnsurer creates "GameplayCamera (ensured)"), and two owners of one
//    camera field is how clear-flags drift. WO-1000 §2.1's "near-black camera bg" is
//    therefore a Village-side edit, reported rather than smuggled in here — exactly
//    the scope call DungeonBaker L230-237 made for the composed pipeline.
// -----------------------------------------------------------------------------
// WO-1000 FOLLOW-UP (2026-08-07) — two defects the first pass left, both measured:
//
//  A. FLOOR Z-FIGHT. MakeFloor's nav slab put its TOP FACE at y=0 (center -0.25,
//     size 0.5) and BuildFloorTiles drops every KayKit tile so ITS top face is also
//     y=0. Two coplanar surfaces at identical depth — the pale blue-grey wash with
//     brown floor bleeding through in Builds/dungeon-capture/KayKitChallengeOutpost_eye.png.
//     The walls never had this because they pass clad:true -> CladBox -> HideMesh.
//     The floor now gets the SAME treatment (HideFloorSlabIfTiled), CONDITIONALLY:
//     MakeFloor runs before BuildFloorTiles, and BuildFloorTiles bails when the
//     gitignored pack is absent, so the slab only loses its mesh once tiles landed.
//     Not fixed by lifting the tiles an epsilon — that leaves the slab renderable.
//
//  B. NAV_FAIL, 4/5 probes. NOT a regression from the visual pass: BuildChoke's
//     centres/dimensions are byte-identical to 6c740b08^ and the ring walls only lost
//     0.5 m of HEIGHT, which cannot move a horizontal pinch. VerifyNav did not exist
//     before, so the check REVEALED a pre-existing seal.
//     Root cause, from the measured box AABBs: BuildChoke split its wall on the WRONG
//     AXIS. It offset two FULL-WIDTH boxes +/- `gap` along the wall's THIN axis instead
//     of splitting the wall along its LONG axis and leaving `gap` as the opening — so a
//     "chokepoint" was really two parallel barriers with no door. Choke_SouthMid_L
//     landed at z -10.675..-10.325, a bare 0.725 m north of Ring_Inner_S (z -9.6..-8.4),
//     and that slot is the ONLY approach to the inner ring's south doorway. Agent
//     radius is 0.5 (ProjectSettings/NavMeshAreas.asset, the single 'Humanoid' type),
//     so a walkable slot needs 2*0.5 = 1.0 m. 0.725 < 1.0 -> no navmesh -> arena
//     sealed. The four quadrant probes sit at (+/-20, +/-20), OUTSIDE the mid ring, so
//     they never needed that slot — exactly the 4/5 pattern captured.
//     Fix: BuildChoke now delegates to SpanWall, which already implements "wall with a
//     central opening" correctly and is what the ring walls use. Every authored
//     parameter (centre, w, d, h, the 0.45 gap factor) is preserved verbatim; only the
//     buggy interpretation changed. Choke_SouthMid becomes one wall at its authored
//     z=-6 with a 4.5 m door, and the 0.725 m slot ceases to exist.
//     ReportNavClearances() now prints the agent settings and EVERY sub-3 m slot
//     between two collision boxes with PASS/FAIL against 2*agentRadius, so a sealed
//     yard is a measured line in the bake log and never a guessing game again.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Editor
{
    public static class KayKitChallengeOutpostBuilder
    {
        private const string Sys = "KayKitOutpost";
        private const string ScenePath = "Assets/Scenes/KayKitChallengeOutpost.unity";
        private const string KayFolder = "Assets/Models/KayKit/dungeon";

        // The pack ships the SAME meshes twice: <KayFolder>/*.fbx|*.gltf and
        // <KayFolder>/fbx(unity)/*.fbx (verified identical by MD5 for wall / floor /
        // ceiling / torch_mounted / barrel_large). The old substring FindKay walked a
        // GUID-ordered scan of ALL of them, so "torch" could resolve to torch.fbx,
        // torch_lit.fbx or torch_mounted.fbx depending on GUID order — a different
        // dungeon on a different machine. Exact-name resolution in a fixed preference
        // order makes the bake deterministic. fbx(unity) first because that is the
        // variant DungeonSceneBuilder loads (its PackRoot ends "/Assets/fbx(unity)/").
        private static readonly string[] KayProbeFolders = { KayFolder + "/fbx(unity)", KayFolder };
        private static readonly string[] KayProbeExts = { ".fbx", ".gltf" };

        private const float Outer = 56f;
        private const float Mid   = 36f;
        private const float Inner = 18f;

        private static readonly Vector3 Entry = new Vector3(0f, 0f, -Outer * 0.5f + 4f);

        // ── Relight constants (see the header for the source line of each) ──────
        private const float Ambient = 0.05f;                     // DungeonSceneBuilder L170
        private const float FogStart = 14f, FogEnd = 42f;        // DungeonSceneBuilder L1998-1999
        private static readonly Color FogColor = new Color(0.039f, 0.039f, 0.063f);   // #0a0a10
        private static readonly Color DirColor = new Color(0.224f, 0.255f, 0.310f);   // #39414f
        private const float DirIntensity = 0.18f;                // DungeonSceneBuilder L2026

        // ── Torch dial — copied verbatim from DungeonDresser (do NOT retune here) ─
        private static readonly string[] TorchTokens = { "torch_mounted", "torch_lit", "torch" };
        private const float TorchIntensity = 0.85f;                                   // L73
        private static readonly Color TorchColor = new Color(1f, 0.62f, 0.28f);       // L261
        private const float TorchRangeFactor = 1.2f;                                  // L86
        private const float TorchRangeMin = 4.5f;                                     // L87
        private const float TorchRangeMax = 12f;                                      // L88
        private const float TorchMountHeight = 2.2f;                                  // L244

        // Torches per wall side, per ring. The single dial if the light count needs
        // trimming for the mobile target — nothing else has to move.
        private const int TorchesPerSideOuter = 3;
        private const int TorchesPerSideMid = 2;
        private const int TorchesPerSideInner = 2;

        private static Material _floor, _wall, _crate, _accent;
        private static List<string> _kayPaths;
        private static readonly HashSet<string> _warned = new HashSet<string>();

        // ── Measured kit geometry. Fallbacks are the glTF POSITION bounds read off
        //    Assets/Models/KayKit/dungeon/*.gltf on 2026-08-07:
        //      wall            x -2..2   y 0..4        z -0.5..0.5
        //      floor_tile_large x -2..2  y -0.10..0.05 z -2..2
        //      ceiling_tile     x -2..2  y -0.25..0.10 z -2..2
        //    but MeasureKit() re-measures the ACTUAL imported prefabs every bake and
        //    logs what it found, so a repack / rescale can never silently open slits
        //    in the shell. Nothing is ever scaled — the atlas UVs are authored for
        //    these exact spans (WO-1000 §2.2).
        private static GameObject _wallModel, _floorModel, _ceilModel, _torchModel;
        private static string _torchToken = "torch_mounted";
        private static float _wallLen = 4f, _wallHeight = 4f, _wallThick = 1f;
        private static float _floorSpan = 4f, _floorTopY = 0.05f;
        private static float _ceilSpan = 4f;

        // Running tallies -> the bake log (CLAUDE.md §12: the bake reports its own data).
        private static int _floorTiles, _ceilTiles, _cladPieces, _torches, _torchLights,
                           _props, _fogAnchors, _missingModels;

        // Every collision box that feeds the NavMeshSurface, with its world AABB, so
        // ReportNavClearances can MEASURE the shell's pinch points instead of anyone
        // eyeballing coordinates out of the source. Filled by MakeBox(nav:true).
        private static readonly List<(string name, Bounds box)> _navBoxes =
            new List<(string name, Bounds box)>();

        // Only slots NARROWER than this are worth a log line — anything wider is
        // trivially walkable and would drown the interesting ones in noise.
        private const float ClearanceReportBelow = 3f;

        [MenuItem("Defenders/World/Build KayKit Challenge Outpost")]
        public static void Build()
        {
            FlowTrace.Step(Sys, "=== KAYKIT CHALLENGE OUTPOST BUILD START ===");
            _floorTiles = _ceilTiles = _cladPieces = _torches = _torchLights =
                _props = _fogAnchors = _missingModels = 0;
            _navBoxes.Clear();

            EnsureMats();
            EnsureKayPaths();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Everything hangs off ONE root left at the origin with an identity
            // rotation + unit scale, so local space == world space for every helper
            // below. Nothing here may reparent or move it.
            var root = new GameObject("KayKitChallengeOutpostRoot").transform;

            // Resolve + MEASURE the kit before any geometry is laid: the collision
            // boxes take their height from the real wall piece, so a mismatch cannot
            // leave a band of bare box above the cladding.
            MeasureKit();

            ConfigureAtmosphere(root);

            var surface = root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.overrideTileSize = true;
            surface.tileSize = 1024;

            // ORDER IS LOAD-BEARING: the slab is laid first (it is the nav floor), the
            // KayKit tiles are laid on top of it, and only THEN can the slab's mesh be
            // retired — see HideFloorSlabIfTiled for why that last step is conditional.
            var floorSlab = MakeFloor(root, "Floor_Outer", 0f, 0f, Outer, Outer);
            BuildFloorTiles(root);
            HideFloorSlabIfTiled(floorSlab);

            BuildRingWalls(root, "Ring_Outer", Outer, gapSouth: true, gapNorth: false);
            BuildRingWalls(root, "Ring_Mid", Mid, gapSouth: false, gapNorth: true);
            BuildRingWalls(root, "Ring_Inner", Inner, gapSouth: true, gapNorth: false);

            float chokeY = _wallHeight * 0.5f;
            BuildChoke(root, "Choke_SouthMid", new Vector3(0f, chokeY, -6f), 10f, 1f, _wallHeight);
            BuildChoke(root, "Choke_NorthMid", new Vector3(0f, chokeY, 10f), 8f, 1f, _wallHeight);
            BuildChoke(root, "Choke_EastInner", new Vector3(7f, chokeY, 0f), 1f, 6f, _wallHeight, rotate90: true);

            BuildCeiling(root);

            DressCornerTowers(root, Outer);
            DressWallTorches(root);
            SeatGroundFogAnchors(root);

            MakeMarker(root, "Outpost_Entry", Entry);

            PlaceEnemyGroups(root, new[]
            {
                new Vector3(-18f, 0f, -18f),
                new Vector3( 18f, 0f, -18f),
                new Vector3(-18f, 0f,  18f),
                new Vector3( 18f, 0f,  18f),
                new Vector3(  0f, 0f,  -8f),
                new Vector3(  0f, 0f,   8f),
                new Vector3( -8f, 0f,   0f),
                new Vector3(  8f, 0f,   0f),
            });

            PlaceBreakables(root, new[]
            {
                (new Vector3(-20f, 0f, -10f), "crate", "crate-common"),
                (new Vector3( 20f, 0f, -10f), "crate", "crate-common"),
                (new Vector3(-20f, 0f,  10f), "barrel", "barrel-common"),
                (new Vector3( 20f, 0f,  10f), "barrel", "barrel-common"),
                (new Vector3(-12f, 0f, -20f), "crate", "crate-common"),
                (new Vector3( 12f, 0f,  20f), "chest", "chest-rare"),
                (new Vector3( -4f, 0f,  14f), "crate", "crate-common"),
                (new Vector3(  4f, 0f, -14f), "crate", "crate-common"),
            });

            // MEASURE the shell before baking it: the clearance report tells us WHY a
            // probe will fail, VerifyNav below only tells us THAT it failed.
            ReportNavClearances(surface);

            NavMesh.RemoveAllNavMeshData();
            surface.BuildNavMesh();
            VerifyNav();

            FlowTrace.Step(Sys,
                $"BUILD TALLY floorTiles={_floorTiles} ceilTiles={_ceilTiles} wallPieces={_cladPieces} " +
                $"torches={_torches} torchLights={_torchLights} props={_props} fogAnchors={_fogAnchors} " +
                $"missingModels={_missingModels}");

            // WO-1731: surface.BuildNavMesh() above wrote this scene's m_NavMeshData. An unsaved
            // post-bake scene keeps the reference the bake replaced and ships with no navmesh --
            // silently, with nothing on screen saying so. THROW (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new System.InvalidOperationException(
                    "[KayKitChallengeOutpostBuilder] could not save navigation for " + ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            EnsureBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            FlowTrace.Step(Sys, $"SAVED {ScenePath} (KayKit challenge outpost — {Outer}m yard, 8 groups)");
            FlowTrace.Step(Sys, "=== KAYKIT CHALLENGE OUTPOST BUILD COMPLETE ===");
        }

        // =====================================================================
        //  Kit resolution + measurement (WO-1000 §2.2 — authored scale, never stretched)
        // =====================================================================

        /// <summary>
        /// Resolves the shell/torch models once and reads their REAL imported bounds.
        /// The tiling step, the wall run step and the collision-box height are all
        /// derived from these numbers instead of re-typed literals, so the shell
        /// stays closed even if the pack is re-exported at another scale.
        /// </summary>
        private static void MeasureKit()
        {
            _wallModel  = LoadKay("wall");
            _floorModel = LoadKay("floor_tile_large");
            _ceilModel  = LoadKay("ceiling_tile");
            _torchToken = PickTorchToken();
            _torchModel = LoadKay(_torchToken);

            var wb = ModelBounds(_wallModel);
            if (wb.size.x > 0.01f && wb.size.y > 0.01f)
            {
                _wallLen = wb.size.x;
                _wallHeight = wb.size.y;
                _wallThick = Mathf.Max(0.05f, wb.size.z);
            }

            var fb = ModelBounds(_floorModel);
            if (fb.size.x > 0.01f)
            {
                _floorSpan = Mathf.Min(fb.size.x, fb.size.z);
                _floorTopY = fb.max.y;
            }

            var cb = ModelBounds(_ceilModel);
            if (cb.size.x > 0.01f) _ceilSpan = Mathf.Min(cb.size.x, cb.size.z);

            var tb = ModelBounds(_torchModel);

            FlowTrace.Step(Sys,
                $"KIT MEASURED wall={_wallLen:F2}L x {_wallHeight:F2}H x {_wallThick:F2}T " +
                $"floorTile={_floorSpan:F2} (topY {_floorTopY:F3}) ceilTile={_ceilSpan:F2} " +
                $"torch='{_torchToken}' bounds y {tb.min.y:F3}..{tb.max.y:F3} z {tb.min.z:F3}..{tb.max.z:F3} " +
                $"[wall={(_wallModel != null ? "OK" : "MISSING")} floor={(_floorModel != null ? "OK" : "MISSING")} " +
                $"ceil={(_ceilModel != null ? "OK" : "MISSING")} torch={(_torchModel != null ? "OK" : "MISSING")}]");
        }

        /// <summary>
        /// First torch token that actually resolves, IN PREFERENCE ORDER. Only
        /// torch_mounted is a wall bracket (back plate at z=0, arm to +z); the other
        /// two are radially symmetric floor torches and would float at bracket
        /// height. See DungeonDresser L42-63 for the measured bounds behind this.
        /// </summary>
        private static string PickTorchToken()
        {
            foreach (var t in TorchTokens)
                if (LoadKay(t) != null) return t;
            return TorchTokens[0];
        }

        /// <summary>
        /// Combined mesh bounds of an imported model asset, in the asset root's own
        /// space. Read straight off the sharedMeshes — no instantiate, no scene churn.
        /// </summary>
        private static Bounds ModelBounds(GameObject asset)
        {
            if (asset == null) return new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            Bounds acc = default;
            var toRoot = asset.transform.worldToLocalMatrix;
            foreach (var mf in asset.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                var m = toRoot * mf.transform.localToWorldMatrix;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? mb.min.x : mb.max.x,
                        (i & 2) == 0 ? mb.min.y : mb.max.y,
                        (i & 4) == 0 ? mb.min.z : mb.max.z);
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                    else acc.Encapsulate(p);
                }
            }
            return any ? acc : new Bounds(Vector3.zero, Vector3.zero);
        }

        // =====================================================================
        //  §2.1 Atmosphere — kill the daylight, kill the sky
        // =====================================================================

        /// <summary>
        /// The WO-1000 relight, transplanted from the composed pipeline so both
        /// dungeons light identically. See the file header for the source line of
        /// every constant. RenderSettings persist into the saved .unity, which is
        /// why setting them at BAKE time is what makes the scene ship moody.
        /// </summary>
        private static void ConfigureAtmosphere(Transform root)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(Ambient, Ambient, Ambient * 1.1f);
            RenderSettings.ambientIntensity = Ambient;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;   // a KNOWN clear near field + a swallowed far end
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;

            // An EmptyScene inherits the PROCEDURAL SKYBOX in its lighting settings,
            // and RenderSettings persist into the saved .unity — that inherited dome
            // IS the "bright blue sky over the walls". Nulling it is safe with
            // ambientMode=Flat above: flat ambient never samples the skybox, so no
            // light changes; only the sky stops being drawn.
            RenderSettings.skybox = null;

            // Was a 1.1 white "Sun". Now a faint cold FILL — a legibility floor from
            // above, not a key. Shadows deliberately None: with shadows on, the new
            // ceiling occludes this light completely and the yard goes near-black
            // (and a whole-yard real-time directional shadow map is a cost the mobile
            // target cannot pay).
            var go = new GameObject("DirLight_Fill");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = DirColor;
            light.intensity = DirIntensity;
            light.shadows = LightShadows.None;

            FlowTrace.Step(Sys, $"RELIGHT: ambient=Flat {Ambient:F2} fog=Linear #0a0a10 " +
                                $"{FogStart:F0}->{FogEnd:F0}m skybox=null dirLight=#39414f @{DirIntensity:F2} " +
                                "shadows=None (no Camera is created here — the runtime rig owns it)");
        }

        // =====================================================================
        //  §2.2 Textured stone shell — floor + ceiling tiling
        // =====================================================================

        /// <summary>
        /// Covers the collision slab with real KayKit floor tiles at authored scale.
        /// The slab underneath keeps its collider and stays the NavMesh authority, so
        /// the walkable surface is unchanged; the tiles are cosmetic (colliders
        /// stripped) and are dropped so their TOP face lands exactly on y=0.
        /// </summary>
        private static void BuildFloorTiles(Transform root)
        {
            if (_floorModel == null)
            {
                WarnMissing("floor_tile_large");
                return;
            }
            var holder = NewChild(root, "FloorTiles");
            float half = Outer * 0.5f;
            float y = -_floorTopY;
            for (float x = -half + _floorSpan * 0.5f; x < half; x += _floorSpan)
            for (float z = -half + _floorSpan * 0.5f; z < half; z += _floorSpan)
            {
                if (KayInstantiate(_floorModel, holder, new Vector3(x, y, z), Quaternion.identity) != null)
                    _floorTiles++;
            }
            FlowTrace.Step(Sys, $"FLOOR TILED {_floorTiles} x floor_tile_large @ {_floorSpan:F2}m (top face at y=0)");
        }

        /// <summary>
        /// Closes the yard's top so no sky can show — the #2 fault in WO-1000 §0.
        /// Mirrors DungeonSceneBuilder.BuildCeiling L643-658: tile the footprint at
        /// wall-top height and STRIP the colliders (a ceiling that carries colliders
        /// is geometry the NavMeshSurface has to reason about for nothing).
        /// The tile's own bounds put its underside slightly BELOW the placement Y, so
        /// it laps the wall tops instead of meeting them edge to edge.
        /// </summary>
        private static void BuildCeiling(Transform root)
        {
            if (_ceilModel == null)
            {
                WarnMissing("ceiling_tile");
                return;
            }
            var holder = NewChild(root, "Ceiling");
            float half = Outer * 0.5f;
            float y = _wallHeight;
            for (float x = -half + _ceilSpan * 0.5f; x < half; x += _ceilSpan)
            for (float z = -half + _ceilSpan * 0.5f; z < half; z += _ceilSpan)
            {
                if (KayInstantiate(_ceilModel, holder, new Vector3(x, y, z), Quaternion.identity) != null)
                    _ceilTiles++;
            }
            FlowTrace.Step(Sys, $"CEILING TILED {_ceilTiles} x ceiling_tile @ {_ceilSpan:F2}m, y={y:F2} " +
                                "(colliders stripped — nav untouched)");
        }

        // =====================================================================
        //  Ring walls / chokes — unchanged COLLISION, new SKIN
        // =====================================================================

        private static void BuildRingWalls(Transform root, string prefix, float size, bool gapSouth, bool gapNorth)
        {
            float hw = size * 0.5f;
            // Height now tracks the measured KayKit wall piece (was a hardcoded 4.5,
            // which would have left a bare 0.5 m band of box above the cladding).
            float h = _wallHeight, t = 1.2f, y = h * 0.5f, gap = 5f;
            SpanWall(root, prefix + "_N", new Vector3(0f, y,  hw), new Vector3(size, h, t), gapNorth ? gap : 0f, alongX: true);
            SpanWall(root, prefix + "_S", new Vector3(0f, y, -hw), new Vector3(size, h, t), gapSouth ? gap : 0f, alongX: true);
            MakeBox(root, prefix + "_W", new Vector3(-hw, y, 0f), new Vector3(t, h, size), _wall, nav: true, clad: true);
            MakeBox(root, prefix + "_E", new Vector3( hw, y, 0f), new Vector3(t, h, size), _wall, nav: true, clad: true);
        }

        private static void SpanWall(Transform root, string name, Vector3 center, Vector3 size, float gap, bool alongX)
        {
            if (gap <= 0f) { MakeBox(root, name, center, size, _wall, nav: true, clad: true); return; }
            float span = alongX ? size.x : size.z;
            float seg = (span - gap) * 0.5f;
            float off = (span * 0.5f - seg * 0.5f);
            if (alongX)
            {
                MakeBox(root, name + "_L", center + new Vector3(-off, 0f, 0f), new Vector3(seg, size.y, size.z), _wall, nav: true, clad: true);
                MakeBox(root, name + "_R", center + new Vector3( off, 0f, 0f), new Vector3(seg, size.y, size.z), _wall, nav: true, clad: true);
            }
            else
            {
                MakeBox(root, name + "_L", center + new Vector3(0f, 0f, -off), new Vector3(size.x, size.y, seg), _wall, nav: true, clad: true);
                MakeBox(root, name + "_R", center + new Vector3(0f, 0f,  off), new Vector3(size.x, size.y, seg), _wall, nav: true, clad: true);
            }
        }

        /// <summary>
        /// A chokepoint: ONE wall at <paramref name="center"/>, split along its LONG
        /// axis around a central opening of <c>0.45 * span</c>.
        ///
        /// It used to build something else entirely, and that was the NAV_FAIL. The old
        /// body offset two FULL-SPAN boxes by +/-<c>gap</c> along the wall's THIN axis —
        /// two parallel barriers with no door between them, and <c>gap</c> read as a
        /// displacement instead of as the opening. For Choke_SouthMid (centre z=-6,
        /// w=10, d=1) that put the southern box at z -10.675..-10.325, which is 0.725 m
        /// from Ring_Inner_S (z -9.6..-8.4). That slot is the ONLY approach to the inner
        /// ring's single doorway, the agent radius is 0.5, and 0.725 &lt; 2*0.5 — so no
        /// navmesh generated there and the arena was sealed. Pre-existing: the same
        /// centres/dimensions are in 6c740b08^; VerifyNav merely made it visible.
        ///
        /// <see cref="SpanWall"/> already implements "wall with a central opening"
        /// correctly and is what every ring wall uses, so this now delegates to it
        /// rather than keeping a second, broken copy of the idea. Every authored
        /// parameter is passed through verbatim — the centre, the extents, the height
        /// and the 0.45 factor are unchanged; only the (buggy) interpretation is.
        /// </summary>
        private static void BuildChoke(Transform root, string name, Vector3 center, float w, float d, float h, bool rotate90 = false)
        {
            // rotate90 == the wall runs along Z, so its span (and its opening) is d.
            float gap = rotate90 ? d * 0.45f : w * 0.45f;
            SpanWall(root, name, center, new Vector3(w, h, d), gap, alongX: !rotate90);
        }

        /// <summary>
        /// Skins one collision box with KayKit wall pieces laid along its LONG
        /// horizontal axis and centred on it, then removes the box's own mesh so no
        /// flat-colour primitive can render. The box KEEPS its collider — it remains
        /// the sole NavMesh authority, so this whole visual pass is nav-neutral by
        /// construction. Pieces are never scaled (WO-1000 §2.2) and their colliders
        /// are stripped.
        ///
        /// Segment count CEILS, it does not round. DungeonSceneBuilder L592 rounds
        /// because its rooms are authored on the 4 m grid; these rings are not — a
        /// gapped 56 m run is 25.5 m per side, which rounds to 6 segments of 4.25 m
        /// and opens a 0.25 m vertical slit six times over. Ceiling guarantees the
        /// pieces OVERLAP instead, which is the only safe direction for a shell.
        /// </summary>
        private static int CladBox(Transform root, GameObject box)
        {
            if (_wallModel == null)
            {
                // Pack not imported (KayKit is gitignored). Leave the tinted box
                // rendering — a grey shell beats an invisible one.
                WarnMissing("wall");
                return 0;
            }

            Vector3 size = box.transform.localScale;
            Vector3 c = box.transform.position;
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z;
            float baseY = c.y - size.y * 0.5f;

            int segs = Mathf.Max(1, Mathf.CeilToInt(length / _wallLen - 0.001f));
            float step = length / segs;
            int rows = Mathf.Max(1, Mathf.CeilToInt(size.y / _wallHeight - 0.001f));
            // Euler(0,0,0) puts the piece's 4 m span on world X; Euler(0,90,0) puts it
            // on world Z. Its ~1 m thickness follows on the perpendicular, sitting
            // inside the 1.2 m collision box — so the visible face is ~0.1 m proud of
            // nothing and ~0.1 m shy of the collider. Imperceptible, and it means no
            // two surfaces are ever coplanar (no z-fighting).
            var rot = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f);

            var holder = NewChild(root, box.name + "_Clad");
            int placed = 0;
            for (int r = 0; r < rows; r++)
            for (int i = 0; i < segs; i++)
            {
                float along = (i + 0.5f) * step - length * 0.5f;
                var pos = alongX
                    ? new Vector3(c.x + along, baseY + r * _wallHeight, c.z)
                    : new Vector3(c.x, baseY + r * _wallHeight, c.z + along);
                if (KayInstantiate(_wallModel, holder, pos, rot) != null) placed++;
            }

            HideMesh(box);
            _cladPieces += placed;
            return placed;
        }

        // =====================================================================
        //  §2.3 Wall torches that actually light the room
        // =====================================================================

        /// <summary>
        /// Seats torch_mounted brackets on the INNER face of every ring — the face
        /// that looks at the band the player walks — each carrying a warm point light.
        /// Lighting every ring's inner face lights all three zones: the outer band
        /// from the outer ring, the middle band from the mid ring, the arena from the
        /// inner ring.
        ///
        /// Placement is on the wall SURFACE with the bracket's back plate flush to it
        /// and its +Z yawed INTO the room. That geometry is not optional: the model's
        /// plate sits at local z=0 and its arm projects to +z, so a wrong yaw buries
        /// the bracket in the wall (DungeonDresser L161-165 records the same defect).
        /// </summary>
        private static void DressWallTorches(Transform root)
        {
            var holder = NewChild(root, "Torches");
            // ring half-extent, torches per side, width of the band this ring lights
            var rings = new[]
            {
                (half: Outer * 0.5f, perSide: TorchesPerSideOuter, band: (Outer - Mid) * 0.5f,
                 gapSouth: true,  gapNorth: false),
                (half: Mid * 0.5f,   perSide: TorchesPerSideMid,   band: (Mid - Inner) * 0.5f,
                 gapSouth: false, gapNorth: true),
                (half: Inner * 0.5f, perSide: TorchesPerSideInner, band: Inner * 0.5f,
                 gapSouth: true,  gapNorth: false),
            };
            // Half the 5 m doorway gap SpanWall leaves, plus a metre of clearance, so
            // a bracket is never seated in thin air across an opening.
            const float gapClear = 5f * 0.5f + 1f;
            // Same shape as DungeonDresser L151-154: range is DERIVED from the
            // distance it actually has to cover, never a re-typed literal, then held
            // inside the reviewed 4.5-12 m band.
            foreach (var ring in rings)
            {
                float range = Mathf.Clamp(ring.band * TorchRangeFactor, TorchRangeMin, TorchRangeMax);
                // The visible wall surface: the cladding is centred in the box, so its
                // inner face is half a piece-thickness in from the ring radius.
                float face = ring.half - _wallThick * 0.5f;
                float side = ring.half * 2f;

                for (int s = 0; s < 4; s++)
                {
                    // 0=N 1=S 2=E 3=W. Inward is the wall NORMAL, not the radial
                    // direction to the centre.
                    Vector3 inward = s == 0 ? Vector3.back
                                   : s == 1 ? Vector3.forward
                                   : s == 2 ? Vector3.left
                                   : Vector3.right;
                    bool gapped = (s == 0 && ring.gapNorth) || (s == 1 && ring.gapSouth);

                    for (int k = 0; k < ring.perSide; k++)
                    {
                        float u = (k + 1f) / (ring.perSide + 1f);
                        float along = -ring.half + u * side;
                        if (gapped && Mathf.Abs(along) < gapClear) continue;

                        Vector3 pos = s == 0 ? new Vector3(along, TorchMountHeight,  face)
                                    : s == 1 ? new Vector3(along, TorchMountHeight, -face)
                                    : s == 2 ? new Vector3( face, TorchMountHeight, along)
                                    :          new Vector3(-face, TorchMountHeight, along);
                        SeatTorch(holder, pos, inward, range);
                    }
                }
            }
            FlowTrace.Step(Sys, $"TORCHES {_torches} seated ('{_torchToken}'), lights={_torchLights} " +
                                $"intensity={TorchIntensity:F2} shadows=None + {_torches} CandleAnchor markers");
        }

        /// <summary>
        /// One wall bracket: the mesh, a warm cosmetic point light, and an Env_Candle
        /// seat. The light is COSMETIC ONLY — no collider, no trigger, no damage path
        /// touches it; hazard fire is a separate recipe and the two must not blur.
        /// Shadows off for the same reason DungeonDresser L264-267 gives: an accent
        /// light casts nothing worth seeing and N shadow-casting point lights is a
        /// per-frame pass the mobile target cannot pay for.
        /// </summary>
        private static void SeatTorch(Transform parent, Vector3 pos, Vector3 inward, float range)
        {
            var holder = new GameObject($"Torch_{_torches}");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = pos;
            holder.transform.localRotation = Quaternion.LookRotation(inward, Vector3.up);

            if (_torchModel != null) KayInstantiate(_torchModel, holder.transform, Vector3.zero, Quaternion.identity);
            else WarnMissing(_torchToken);

            SeatCandleAnchor(holder.transform, _torchToken);

            var lt = holder.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = TorchColor;
            lt.intensity = TorchIntensity;
            lt.range = range;
            lt.shadows = LightShadows.None;

            _torches++;
            _torchLights++;
        }

        /// <summary>
        /// An empty marker at the torch's FLAME TIP for the <c>Env_Candle</c> wick
        /// flame. ANCHOR ONLY — it deliberately instantiates no VFX. Env_Candle is a
        /// LOOPING, POOLED runtime effect (VFXCatalogGenerator L287: isLoop true,
        /// poolSize 6) owned by VFXManager in DeNelle.Village, which DeNelle.Editor
        /// cannot reference and which enforces a global loop-slot cap; baking one
        /// instance per torch would bypass the pool and the cap outright. So the bake
        /// contributes the one thing only the bake knows — WHERE the flame is — and
        /// the runtime consumer stays a reported seam.
        ///
        /// Offsets are DungeonDresser L293-295 verbatim (measured glTF bounds: the
        /// bracket's flame sits atop an arm projecting to +z; the floor torches' atop
        /// a radially symmetric stick).
        /// </summary>
        private static void SeatCandleAnchor(Transform holder, string token)
        {
            var anchor = new GameObject("CandleAnchor");
            anchor.transform.SetParent(holder, false);
            anchor.transform.localPosition = token == "torch_mounted"
                ? new Vector3(0f, 0.70f, 0.30f)
                : new Vector3(0f, 0.75f, 0f);
            anchor.transform.localRotation = Quaternion.identity;
        }

        // =====================================================================
        //  §2.4 Ground fog — anchors only (see the header for why)
        // =====================================================================

        /// <summary>
        /// Seats "GroundFogAnchor" empties across the three bands. Same reasoning as
        /// <see cref="SeatCandleAnchor"/>: PP_GroundFog is a pooled ParticlePack
        /// effect played through the VFXManager facade in DeNelle.Village, so an
        /// editor bake cannot legitimately instantiate it. The DEPTH half of §2.4 is
        /// already delivered by the linear fog in ConfigureAtmosphere.
        /// </summary>
        private static void SeatGroundFogAnchors(Transform root)
        {
            var holder = NewChild(root, "GroundFogAnchors");
            float[] radii = { (Outer + Mid) * 0.25f, (Mid + Inner) * 0.25f, Inner * 0.25f };
            foreach (float r in radii)
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * 0.25f;
                    var go = new GameObject($"GroundFogAnchor_{_fogAnchors}");
                    go.transform.SetParent(holder, false);
                    go.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r);
                    _fogAnchors++;
                }
            }
            FlowTrace.Step(Sys, $"FOG ANCHORS {_fogAnchors} seated (markers only — PP_GroundFog is a POOLED " +
                                "VFXManager effect in DeNelle.Village; the runtime play-site is the open seam)");
        }

        // =====================================================================
        //  Corner structures + props
        // =====================================================================

        /// <summary>
        /// Corner verticality. The old code asked <c>FindKay("tower")</c> — and the
        /// pack ships NO asset whose filename contains "tower", so every corner has
        /// been silently empty (one warning, then skipped). It also asked for "wall",
        /// which the substring scan could resolve to any of ~30 wall_* variants
        /// depending on GUID order. Both are now exact, existing pieces: a pillar that
        /// runs floor-to-ceiling (its measured height is 4.0 — the same span as the
        /// wall piece, so it reads as a real roof support) plus a little supply
        /// clutter. Everything is seated at a bounded INWARD offset: the long,
        /// origin-at-one-end rubble meshes were rejected here because at a 26 m corner
        /// a diagonal yaw pushes their far end to ~28.8 m and straight through the
        /// 28 m shell. Cosmetic — colliders stripped, so nav is unaffected as before.
        /// </summary>
        private static void DressCornerTowers(Transform root, float span)
        {
            float h = span * 0.5f - 2f;
            var corners = new[]
            {
                new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h),
                new Vector3(-h, 0f,  h), new Vector3(h, 0f,  h),
            };
            var pillar = LoadKay("pillar");
            var boxProp = LoadKay("box_large");
            var barrel = LoadKay("barrel_large");
            if (pillar == null) WarnMissing("pillar");
            for (int i = 0; i < corners.Length; i++)
            {
                var holder = NewChild(root, $"KayCorner_{i}");
                holder.localPosition = corners[i];
                // Local frame pointing at the yard centre, so every offset below is
                // guaranteed to move AWAY from the shell.
                Vector3 inward = new Vector3(-corners[i].x, 0f, -corners[i].z).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, inward);
                float yaw = Quaternion.LookRotation(inward, Vector3.up).eulerAngles.y;

                KayInstantiate(pillar, holder, Vector3.zero, Quaternion.identity);
                KayInstantiate(boxProp, holder, inward * 1.6f + side * 1.2f, Quaternion.Euler(0f, yaw + 20f, 0f));
                KayInstantiate(barrel, holder, inward * 1.7f - side * 1.1f, Quaternion.Euler(0f, yaw - 35f, 0f));
            }
        }

        private static void PlaceEnemyGroups(Transform root, Vector3[] spots)
        {
            var spType = FindType("DeNelle.Village.OutpostEnemyGroupSpawner");
            for (int i = 0; i < spots.Length; i++)
            {
                var go = MakeMarker(root, $"EnemyGroup_{i}", spots[i]);
                if (spType != null) go.AddComponent(spType);
            }
            FlowTrace.Step(Sys, $"ENEMY_GROUPS {spots.Length} markers placed.");
        }

        /// <summary>
        /// Real KayKit loot props: a solid bounds-fitted BoxCollider carrying the
        /// BreakableContainer with its lootTableId, plus a small collider-free companion
        /// prop clustered beside each one so the yard reads as dressed rather than dotted.
        /// <para>
        /// ⛔ WO-1132: the "Enemy" layer assignment is REMOVED. It used to be here so the
        /// hero's enemy-mask OverlapSphere could sweep the prop and damage it, but owner
        /// ruling 2026-08-21 made the container an OPENABLE chest, not an attackable one -
        /// and that same relayer is what let the HOSTILE RETICLE lock onto a crate (WO-1047)
        /// and what made the combat camera frame one. The chest is now driven by a proximity
        /// prompt, so it needs no layer trick. Do not re-add one.
        /// </para>
        /// <para>
        /// The BoxCollider stays: it is now the prop's physical body (it stops the hero
        /// walking through the crate), not a hit seam. The real KayKit models are left
        /// intact - BreakableContainer.EnsureChest detects authored art and skips its own
        /// runtime coffer body, so these props keep their proper KayKit look.
        /// </para>
        /// </summary>
        private static void PlaceBreakables(Transform root, (Vector3 pos, string token, string table)[] spots)
        {
            var bcType = FindType("DeNelle.Village.BreakableContainer");
            var holder = NewChild(root, "Breakables");

            for (int i = 0; i < spots.Length; i++)
            {
                var (pos, token, table) = spots[i];
                var go = new GameObject($"Breakable_{i}_{token}");
                go.transform.SetParent(holder, false);
                go.transform.localPosition = pos;
                // Deterministic per-index yaw — varied, but the same on every bake.
                go.transform.localRotation = Quaternion.Euler(0f, (i * 47f) % 360f, 0f);
                // WO-1132: deliberately left on the DEFAULT layer - see the summary above.

                string modelName = BreakableModel(token);
                var model = LoadKay(modelName);
                Bounds b;
                if (model != null)
                {
                    KayInstantiate(model, go.transform, Vector3.zero, Quaternion.identity);
                    b = ModelBounds(model);
                }
                else
                {
                    WarnMissing(modelName);
                    // Pack not imported: fall back to the old tinted cube so the loot
                    // prop still EXISTS and the lane still works.
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Fallback_{token}";
                    cube.transform.SetParent(go.transform, false);
                    cube.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                    var mr = cube.GetComponent<MeshRenderer>();
                    if (mr != null && _crate != null) mr.sharedMaterial = _crate;
                    StripColliders(cube);
                    b = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
                }

                // A degenerate measurement (no MeshFilter under the asset) must NOT
                // shrink the box to nothing — this collider is the prop's physical body
                // (WO-1132: no longer a hit seam), so it falls back to the 1 m cube the
                // old builder shipped rather than to zero and letting the hero walk through.
                if (b.size.y < 0.2f) b = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);

                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = Vector3.Max(b.size, new Vector3(0.4f, 0.4f, 0.4f));
                box.isTrigger = false;

                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.NavigationStatic);

                if (bcType != null)
                {
                    var comp = go.AddComponent(bcType);
                    bcType.GetField("lootTableId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(comp, table);
                }
                _props++;

                // Cluster companion — cosmetic, collider-free, so it changes nothing
                // about nav or the hit seam.
                var buddy = LoadKay((i % 2 == 0) ? "barrel_small" : "box_small");
                if (buddy != null)
                {
                    float a = (i * 71f) * Mathf.Deg2Rad;
                    KayInstantiate(buddy, holder,
                        pos + new Vector3(Mathf.Cos(a) * 1.6f, 0f, Mathf.Sin(a) * 1.6f),
                        Quaternion.Euler(0f, (i * 33f) % 360f, 0f));
                }
            }
            FlowTrace.Step(Sys, $"BREAKABLES {_props} real KayKit props (bounds-fitted BoxCollider, DEFAULT " +
                                $"layer - NOT Enemy, WO-1132; BreakableContainer={(bcType != null ? "wired" : "TYPE MISSING")})");
        }

        private static string BreakableModel(string token)
        {
            switch ((token ?? "crate").ToLowerInvariant())
            {
                case "barrel": return "barrel_large";
                case "chest":  return "chest_gold";
                default:       return "crates_stacked";
            }
        }

        // =====================================================================
        //  Nav verification (CLAUDE.md §12 — the bake proves itself with data)
        // =====================================================================

        /// <summary>
        /// Prints the agent the bake is actually about to bake FOR, then every slot
        /// narrower than <see cref="ClearanceReportBelow"/> between two collision boxes,
        /// PASS/FAIL against <c>2 * agentRadius</c>.
        ///
        /// This exists because "the centre is unreachable" was a dead end to debug: the
        /// bake reported a failed probe and nothing about WHY. A NavMesh slot has to be
        /// at least a full agent DIAMETER wide — Recast erodes the walkable surface by
        /// agentRadius from every obstacle edge, so a corridor of width W yields
        /// W - 2*radius of floor and anything at or below zero simply is not there.
        /// Choke_SouthMid_L vs Ring_Inner_S measured 0.725 m against a required 1.0 and
        /// that one line is the whole root cause. The radius is READ from the surface's
        /// own agent type, never assumed to be Unity's 0.5 default.
        ///
        /// Two boxes form a "slot" when they overlap in Y (so they block at the same
        /// height) and overlap on exactly ONE horizontal axis — they then face each
        /// other across the other axis. Overlapping in both means they intersect;
        /// overlapping in neither means they are diagonal and enclose nothing.
        /// Doorways register here too, which is the point: the ring gaps SHOULD show up
        /// as comfortable PASSes, so a shell that closes one is immediately obvious.
        /// </summary>
        private static void ReportNavClearances(NavMeshSurface surface)
        {
            var settings = NavMesh.GetSettingsByID(surface.agentTypeID);
            float radius = settings.agentRadius;
            float need = radius * 2f;

            FlowTrace.Step(Sys,
                $"NAV_AGENT id={surface.agentTypeID} '{NavMesh.GetSettingsNameFromID(surface.agentTypeID)}' " +
                $"radius={radius:F3} height={settings.agentHeight:F2} climb={settings.agentClimb:F2} " +
                $"slope={settings.agentSlope:F0} minRegionArea={settings.minRegionArea:F2} " +
                $"=> MIN WALKABLE SLOT = 2*radius = {need:F3}m");

            int slots = 0, fails = 0;
            for (int i = 0; i < _navBoxes.Count; i++)
            for (int j = i + 1; j < _navBoxes.Count; j++)
            {
                var a = _navBoxes[i];
                var b = _navBoxes[j];
                if (a.box.min.y >= b.box.max.y || b.box.min.y >= a.box.max.y) continue;

                bool ovX = a.box.min.x < b.box.max.x && b.box.min.x < a.box.max.x;
                bool ovZ = a.box.min.z < b.box.max.z && b.box.min.z < a.box.max.z;
                if (ovX == ovZ) continue;          // intersecting, or diagonal — no slot

                bool acrossX = ovZ;                // they overlap in Z, so they face across X
                float aLo = acrossX ? a.box.min.x : a.box.min.z;
                float aHi = acrossX ? a.box.max.x : a.box.max.z;
                float bLo = acrossX ? b.box.min.x : b.box.min.z;
                float bHi = acrossX ? b.box.max.x : b.box.max.z;

                bool aFirst = aHi <= bLo;
                float slotLo = aFirst ? aHi : bHi;
                float slotHi = aFirst ? bLo : aLo;
                float gap = slotHi - slotLo;
                if (gap > ClearanceReportBelow) continue;

                float runLo = acrossX ? Mathf.Max(a.box.min.z, b.box.min.z) : Mathf.Max(a.box.min.x, b.box.min.x);
                float runHi = acrossX ? Mathf.Min(a.box.max.z, b.box.max.z) : Mathf.Min(a.box.max.x, b.box.max.x);

                slots++;
                bool pass = gap >= need;
                if (!pass) fails++;
                string line =
                    $"NAV_SLOT {(pass ? "PASS" : "FAIL")} {gap:F3}m (need {need:F3}) " +
                    $"'{(aFirst ? a.name : b.name)}' -> '{(aFirst ? b.name : a.name)}' " +
                    $"across {(acrossX ? "X" : "Z")} {slotLo:F3}..{slotHi:F3}, " +
                    $"runs {(acrossX ? "Z" : "X")} {runLo:F2}..{runHi:F2}";
                if (pass) FlowTrace.Step(Sys, line);
                else FlowTrace.Warn(Sys, line);
            }

            FlowTrace.Step(Sys,
                $"NAV_CLEARANCE navBoxes={_navBoxes.Count} slots<{ClearanceReportBelow:F0}m={slots} " +
                $"belowAgentDiameter={fails} (a FAIL is only a SEAL if that slot is the sole route — " +
                "read it next to the NAV_PROBE lines below)");
        }

        /// <summary>
        /// The shell was re-skinned, the wall boxes lost 0.5 m of height and the loot
        /// props grew from 1 m cubes to real meshes — all of which touch what the
        /// NavMeshSurface sees. So the bake no longer asserts one path; it walks the
        /// entry to the centre AND to a probe in each quadrant and prints every
        /// result, so a sealed yard is a captured line rather than a felt-test.
        /// </summary>
        private static void VerifyNav()
        {
            bool entryOk = NavMesh.SamplePosition(Entry, out _, 3f, NavMesh.AllAreas);
            var probes = new[]
            {
                ("center", Vector3.zero),
                ("nw", new Vector3(-20f, 0f,  20f)),
                ("ne", new Vector3( 20f, 0f,  20f)),
                ("sw", new Vector3(-20f, 0f, -20f)),
                ("se", new Vector3( 20f, 0f, -20f)),
            };
            int ok = 0;
            var path = new NavMeshPath();
            foreach (var (name, target) in probes)
            {
                bool reached = NavMesh.CalculatePath(Entry, target, NavMesh.AllAreas, path)
                               && path.status == NavMeshPathStatus.PathComplete;
                if (reached) ok++;
                else FlowTrace.Warn(Sys, $"NAV_PROBE_FAIL entry->{name} {target} status={path.status}");
            }
            if (entryOk && ok == probes.Length)
                FlowTrace.Step(Sys, $"NAV_OK entry sampled + {ok}/{probes.Length} probes reachable");
            else
                FlowTrace.Fail(Sys, $"NAV_FAIL entrySampled={entryOk} probesReached={ok}/{probes.Length}");
        }

        // =====================================================================
        //  KayKit resolution + instantiation
        // =====================================================================

        /// <summary>
        /// EXACT-name model load in a fixed folder/extension preference order. Replaces
        /// the substring scan for anything load-bearing: the pack ships the same mesh
        /// under two folders and two extensions, so a substring match over a
        /// GUID-ordered list was free to pick a different asset on a different machine.
        /// </summary>
        private static GameObject LoadKay(string exactName)
        {
            if (string.IsNullOrEmpty(exactName)) return null;
            foreach (var folder in KayProbeFolders)
            foreach (var ext in KayProbeExts)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{exactName}{ext}");
                if (model != null) return model;
            }
            // Last resort: the legacy substring scan, so a re-organised pack still
            // dresses rather than silently emptying the scene.
            string path = FindKay(exactName);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Instantiates a resolved KayKit model under a parent at LOCAL pos/rot with
        /// its colliders stripped (dressing must never fragment or trap the NavMesh)
        /// and NO scaling (the atlas UVs are authored for the piece's own span).
        /// Guard-wrapped: one bad asset logs and is skipped, never throws out of the bake.
        /// </summary>
        private static Transform KayInstantiate(GameObject model, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            if (model == null) return null;
            GameObject inst = null;
            Guard.Try(Sys, $"instantiate '{model.name}'", () =>
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                if (inst == null) inst = UnityEngine.Object.Instantiate(model, parent);
                inst.transform.localPosition = localPos;
                inst.transform.localRotation = localRot;
                inst.transform.localScale = Vector3.one;
                StripColliders(inst);
            });
            return inst != null ? inst.transform : null;
        }

        private static void WarnMissing(string token)
        {
            _missingModels++;
            if (_warned.Add(token))
                Debug.LogWarning($"[{Sys}] KayKit '{token}' not found under {KayFolder} — " +
                                 "cosmetic skipped (pack is gitignored; re-import on a fresh clone).");
        }

        private static string FindKay(string token)
        {
            EnsureKayPaths();
            token = token.ToLowerInvariant();
            foreach (var p in _kayPaths)
            {
                if (Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(token))
                    return p;
            }
            return null;
        }

        private static void EnsureKayPaths()
        {
            if (_kayPaths != null) return;
            _kayPaths = new List<string>();
            if (!AssetDatabase.IsValidFolder(KayFolder)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { KayFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string ext = System.IO.Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".gltf") _kayPaths.Add(p);
            }
        }

        // =====================================================================
        //  Primitive / scene helpers
        // =====================================================================

        /// <summary>
        /// The nav floor. clad:false deliberately — a floor is not a wall run — which is
        /// exactly why it never got CladBox's HideMesh and why its TOP FACE (y=0) was
        /// left fighting the KayKit tiles' top faces (also y=0). The slab is returned so
        /// <see cref="HideFloorSlabIfTiled"/> can retire its mesh AFTER the tiles land.
        /// </summary>
        private static GameObject MakeFloor(Transform root, string name, float cx, float cz, float w, float d)
        {
            return MakeBox(root, name, new Vector3(cx, -0.25f, cz), new Vector3(w, 0.5f, d), _floor, nav: true, clad: false);
        }

        /// <summary>
        /// Retires the nav slab's MESH once real floor tiles cover it — the same cure
        /// the walls already get (CladBox -> <see cref="HideMesh"/>): collider kept, and
        /// with it the NavigationStatic flag, so the slab stays the sole NavMesh
        /// authority. It never was a renderer question for nav anyway: Build() sets
        /// <c>useGeometry = PhysicsColliders</c>, so destroying the MeshRenderer/MeshFilter
        /// cannot move a single navmesh triangle.
        ///
        /// Lifting the tiles by an epsilon instead was rejected: that leaves the slab
        /// renderable, so the two surfaces stay one careless edit apart from fighting
        /// again. Removing the surface is the fix; separating it is a delay.
        ///
        /// CONDITIONAL, and that guard is the whole safety story. MakeFloor runs BEFORE
        /// BuildFloorTiles, and BuildFloorTiles bails when the gitignored KayKit pack is
        /// not imported. Hiding an untiled slab would ship an INVISIBLE floor — strictly
        /// worse than a z-fight — so the mesh only goes away once tiles actually landed.
        /// </summary>
        private static void HideFloorSlabIfTiled(GameObject slab)
        {
            if (slab == null) return;
            if (_floorTiles > 0)
            {
                HideMesh(slab);
                FlowTrace.Step(Sys, $"FLOOR SLAB '{slab.name}' mesh RETIRED — {_floorTiles} KayKit tiles cover it; " +
                                    "collider + NavigationStatic kept (nav authority unchanged), y=0 z-fight gone");
            }
            else
            {
                FlowTrace.Warn(Sys, $"FLOOR SLAB '{slab.name}' mesh KEPT — 0 floor tiles were placed " +
                                    "(KayKit pack not imported?). A hidden slab here would be an INVISIBLE floor, " +
                                    "so the flat-colour slab renders instead and the z-fight cannot occur.");
            }
        }

        private static GameObject MakeBox(Transform root, string name, Vector3 center, Vector3 size,
                                          Material mat, bool nav, bool clad = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
            if (nav)
            {
                var f = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go, f | StaticEditorFlags.NavigationStatic);
                // World AABB for ReportNavClearances. Every box here is axis-aligned and
                // the root is identity at the origin, so position/lossyScale IS the AABB
                // of the unit cube's collider — no bounds query, no renderer dependency
                // (this must keep working after HideMesh strips the MeshRenderer).
                _navBoxes.Add((name, new Bounds(go.transform.position, go.transform.lossyScale)));
            }
            if (clad) CladBox(root, go);
            return go;
        }

        private static GameObject MakeMarker(Transform root, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            return go;
        }

        private static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>
        /// Turns a collision box into collision ONLY — the collider (and therefore the
        /// NavMesh contribution) is untouched, the mesh is gone.
        /// </summary>
        private static void HideMesh(GameObject go)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) UnityEngine.Object.DestroyImmediate(mr);
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) UnityEngine.Object.DestroyImmediate(mf);
        }

        private static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);
        }

        private static void EnsureMats()
        {
            // Rebuilt every run: these are runtime-created materials that get
            // serialised INTO the previous scene, so a cached reference across two
            // builds in one editor domain is a destroyed object.
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            // Dark stone, not the old mid-brown: these now only ever back the KayKit
            // tiles (or stand in when the gitignored pack is not imported), so they
            // must read as unlit stone against ambient 0.05, never as a lit surface.
            _floor  = MakeMat(lit, new Color(0.10f, 0.10f, 0.11f));
            _wall   = MakeMat(lit, new Color(0.09f, 0.09f, 0.10f));
            _crate  = MakeMat(lit, new Color(0.34f, 0.24f, 0.15f));
            _accent = MakeMat(lit, new Color(0.30f, 0.16f, 0.50f));
        }

        private static Material MakeMat(Shader lit, Color c)
        {
            var m = new Material(lit);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        private static void EnsureBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
                if (s.path == path) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
