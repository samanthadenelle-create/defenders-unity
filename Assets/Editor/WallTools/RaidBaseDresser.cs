// =============================================================================
// RaidBaseDresser - KayKit / Synty / StructureContent dress for RaidBase_* bakes.
// -----------------------------------------------------------------------------
// WO-1608. Called by RaidBaseGenerator AFTER rings, turrets, spire and staging
// exist. Never hand-edits a .unity. Missing gitignored packs LogWarning and skip
// that piece (TGVRU). Combat stats stay on DefenseTower / WallSegment colliders.
//
// Art load order (the Resources/Structures miss is why towers shipped as cylinders):
//   1. explicit Assets/ path
//   2. KayKit token (Dungeon Remastered + Hexagon + dungeon twin)
//   3. Synty castle / battleground prefab by filename
//   4. Assets/StructureContent (tracked - always on a fresh clone)
//   5. Resources.Load of the catalog visualPrefabPath (legacy, usually empty)
// =============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;
using DeNelle.Village.World.Camps;   // RaidSpire.VisualHeight - the bake's own fitted height (WO-1820)

namespace DeNelle.Editor
{
    public static class RaidBaseDresser
    {
        public const string Sys = "RaidBase";
        public const float MinGateWidth = 3.5f;

        // -- WO-1633 courtyard cover rings ------------------------------------
        // Owner 2026-09-10, verbatim: "i have mentioned it in testing that it feels
        // incomplete and not polished" / "similar strategy as we used in battle arena".
        // The band these knobs describe replaces the single annulus the old PropSlot
        // courtyard branch used (Lerp(inner+4, radius-6, 0.45..0.75)), which put every
        // courtyard prop of every camp on ONE circle with no jitter - WO-1633 §2.2.

        /// <summary>Clear air kept around the spire so props never crowd the objective.</summary>
        private const float CourtyardSpirePad = 9f;

        /// <summary>Clear air kept outside an inner keep ring before the courtyard band starts.</summary>
        private const float CourtyardInnerPad = 3.5f;

        /// <summary>Clear air kept inside the wall line, so props do not fight the wall-band turrets.</summary>
        private const float CourtyardWallPad = 5.5f;

        /// <summary>Target metres between concentric cover rings inside the courtyard band.</summary>
        private const float CourtyardRingSpacing = 7f;

        /// <summary>Ceiling on concentric courtyard rings - three reads as a place, more reads as a car park.</summary>
        private const int CourtyardMaxRings = 3;

        /// <summary>Base cluster spread in metres; grows with the entry's instance count.</summary>
        private const float ClusterSpreadBase = 1.8f;

        /// <summary>Scale roll for props. Deliberately tighter than the arena's 0.9-1.6 - a crate
        /// at 1.6x reads as a bug, where a boulder does not.</summary>
        private const float PropScaleMin = 0.92f;
        private const float PropScaleMax = 1.12f;

        /// <summary>Metres of clear air kept around each placed turret so props never bury one.</summary>
        private const float TurretClearPad = 3.5f;

        /// <summary>Metres of clear air kept around the staging marker (WO-1520).</summary>
        private const float StagingClearPad = 10f;

        /// <summary>
        /// The staging marker's object name. MIRRORS <c>RaidBaseGenerator.StagingPointName</c>
        /// (`RaidBaseGenerator.cs:189`), which is private - this dresser reads the marker OUT OF
        /// THE BUILT TREE rather than recomputing a radius, because staging is NOT on the south
        /// axis for every camp: Builds/wave2-bake2 records both `fortified_garrison` and
        /// `mage_enclave` moved to the SOUTH-WEST diagonal.
        /// </summary>
        private const string StagingMarkerName = "RaidStagingPoint";

        /// <summary>
        /// Where props may NOT go, all of it measured off the tree the generator just built
        /// rather than off hardcoded radii. WO-1633 acceptance 3 + 4.
        /// </summary>
        private struct PropKeepout
        {
            /// <summary>Half-width of the gate -> spire assault corridor (the x ~ 0 lane).</summary>
            public float LaneHalf;
            public float SpireClear;
            public List<Vector3> Turrets;
            public Vector3 Staging;
            public bool HasStaging;
        }

        // WO-1704: actual BuildRing authority, not a guessed fraction of outer gate width.
        public struct RingLayout
        {
            public float Radius;
            public float GateWidth;
        }

        public struct LayoutContext
        {
            public float Radius;
            public float Innermost;
            public bool TwoGates;
            public int InnerLayers;
            public RingLayout[] InnerRings;
            public float GateWidth;
            public float SegmentWidth;
        }

        private static readonly string[] KayFolders =
        {
            "Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)",
            "Assets/Models/KayKit/dungeon/fbx(unity)",
            "Assets/Models/KayKit/dungeon",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/red",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/neutral",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props",
            "Assets/Models/KayKit/medieval",
            "Assets/Models/KayKit/medieval/walls",
        };

        private static readonly string[] KayExts = { ".fbx", ".gltf", ".prefab" };

        private static readonly string[] SyntyFolders =
        {
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Buildings",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/SiegeEngines",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners",
        };

        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static int _placed;
        private static int _missing;

        /// <summary>Metres of overlap applied to every clad panel so adjacent panels never show a
        /// float-precision triangle seam (WO-1704 GREEN residual). Applied at BOTH ends now that a
        /// panel is sized from the WallSegment it clads rather than from a run-relative index.</summary>
        private const float CladLap = 0.02f;

        /// <summary>
        /// Ceiling on the rubble's residual collider height, in metres. Owner ruling 2026-09-14
        /// (WO-1723 §4.3 / §7 ruling 3), verbatim: <i>"that I can step over"</i>. Deliberately
        /// close to the 0.4 m agent step height <see cref="SeatGateThreshold"/> already cites, so
        /// the ruin is a kerb, never a blocker — and deliberately NOT zero, because the ruling also
        /// says the destroyed wall is a THING that is there, not an absence.
        /// </summary>
        private const float RuinStepOverHeight = 0.45f;

        /// <summary>Radial thickness of that step collider (metres).</summary>
        private const float RuinStepThickness = 1.0f;

        public static void Dress(SceneConfigDef def, Transform root, LayoutContext ctx)
        {
            if (def == null || root == null) return;
            _placed = 0;
            _missing = 0;
            Warned.Clear();

            var dress = def.raidDress;
            string kit = KitOf(def);

            var approach = EnsureZone(root, "Zone_Approach");
            var gatehouse = EnsureZone(root, "Zone_Gatehouse");
            var courtyard = EnsureZone(root, "Zone_Courtyard");
            Transform choke = ctx.InnerLayers > 0 ? EnsureZone(root, "Zone_Choke") : null;
            Transform keep = ctx.InnerLayers > 0 ? EnsureZone(root, "Zone_Keep") : null;

            string gateTok = dress != null && !string.IsNullOrEmpty(dress.gate) ? dress.gate : DefaultGate(kit);
            string wallTok = OuterWallToken(def, kit);
            string floorTok = dress != null && !string.IsNullOrEmpty(dress.floor) ? dress.floor : DefaultFloor(kit);
            string towerTok = dress != null && !string.IsNullOrEmpty(dress.towersVisual) ? dress.towersVisual : DefaultTower(kit);

            DressAtmosphere(kit, def.id);
            HideWallRenderers(root);
            // WO-1689: CladRing now RETURNS the wall height it actually achieved, so the
            // gatehouse can report itself against the wall it stands in. Nothing compared the
            // two before, which is how a 1.41 m gate in a ~3.8 m wall could ship silently.
            // "Outer" / "Keep{n}" is the SAME ring-name token RaidBaseGenerator.BuildRing bakes
            // into every "Wall_{ringName}_S{side}_{i}" segment name — CladRing uses it to find
            // and re-height the matching WallSegment colliders it just hid the renderer of
            // (proven-cause fix, breach-tap wall-miss: see SyncWallColliderHeight).
            float wallHeight = CladRing(root, wallTok, ctx.Radius, ctx.GateWidth, ctx.TwoGates, kit, "Outer");
            if (ctx.InnerRings != null)
            {
                for (int i = 0; i < ctx.InnerRings.Length; i++)
                {
                    var ring = ctx.InnerRings[i];
                    float innerHeight = CladRing(root, InnerWall(kit, wallTok), ring.Radius,
                        ring.GateWidth, false, kit, "Keep" + (i + 1), northGate: true);
                    PlaceGatehouse(choke, gateTok, kit, new Vector3(0f, 0f, ring.Radius),
                        180f, ring.GateWidth, "keep" + (i + 1) + "_north", innerHeight);
                }
            }
            else if (ctx.InnerLayers > 0)
                FlowTrace.Warn(Sys, "inner ring reports missing; refusing guessed gate geometry");

            PlaceGatehouse(gatehouse, gateTok, kit, new Vector3(0f, 0f, -ctx.Radius), 0f, ctx.GateWidth, "south", wallHeight);
            if (ctx.TwoGates)
                PlaceGatehouse(gatehouse, gateTok, kit, new Vector3(0f, 0f, ctx.Radius), 180f, ctx.GateWidth, "north", wallHeight);

            TileApproachRoad(approach, floorTok, ctx.Radius, ctx.GateWidth);
            TileCourtyardRing(courtyard, floorTok, ctx);
            DressGateMouth(approach, gatehouse, kit, ctx);

            ReskinCombatArt(root, towerTok, def);
            // WO-1633: the keepout is gathered AFTER the generator has placed turrets, the spire
            // and the staging marker (RaidBaseGenerator.cs:403 / :401 / :422 all run before :424
            // calls Dress), so every exclusion below is read out of the built tree.
            ScatterProps(def, kit, approach, gatehouse, courtyard, choke, keep, ctx, BuildKeepout(root, ctx));
            PlaceGarrisonSlots(root, def, ctx);
            if (ctx.InnerLayers > 0)
                RaiseKeep(keep != null ? keep : root, kit, ctx);

            FlowTrace.Step(Sys,
                $"dressed '{def.id}' kit={kit} placed={_placed} missing={_missing} " +
                $"gateW={ctx.GateWidth:F2} zones=Approach,Gatehouse,Courtyard" +
                (ctx.InnerLayers > 0 ? ",Choke,Keep" : ""));
        }

        public static GameObject LoadVisual(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;

            if (token.StartsWith("Assets/") || token.StartsWith("assets/"))
            {
                var direct = AssetDatabase.LoadAssetAtPath<GameObject>(token);
                if (direct != null) return direct;
            }

            string file = token.Replace('\\', '/');
            int slash = file.LastIndexOf('/');
            if (slash >= 0) file = file.Substring(slash + 1);
            if (file.StartsWith("Structures/")) file = file.Substring("Structures/".Length);

            for (int i = 0; i < KayFolders.Length; i++)
            {
                for (int e = 0; e < KayExts.Length; e++)
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{KayFolders[i]}/{file}{KayExts[e]}");
                    if (go != null) return go;
                    string noExt = System.IO.Path.GetFileNameWithoutExtension(file);
                    go = AssetDatabase.LoadAssetAtPath<GameObject>($"{KayFolders[i]}/{noExt}{KayExts[e]}");
                    if (go != null) return go;
                }
            }

            for (int i = 0; i < SyntyFolders.Length; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{SyntyFolders[i]}/{file}.prefab");
                if (go != null) return go;
                string noExt = System.IO.Path.GetFileNameWithoutExtension(file);
                go = AssetDatabase.LoadAssetAtPath<GameObject>($"{SyntyFolders[i]}/{noExt}.prefab");
                if (go != null) return go;
            }

            string sc = AssetRoots.StructureContent + "/";
            string[] scTry =
            {
                sc + file,
                sc + file + ".prefab",
                sc + file + ".fbx",
                sc + System.IO.Path.GetFileNameWithoutExtension(file) + ".prefab",
                sc + System.IO.Path.GetFileNameWithoutExtension(file) + ".fbx",
                sc + "Tower_Medieval_Wood.prefab",
            };
            for (int i = 0; i < scTry.Length - 1; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(scTry[i]);
                if (go != null) return go;
            }

            var res = Resources.Load<GameObject>(token);
            if (res != null) return res;
            if (token.StartsWith("Structures/"))
                res = Resources.Load<GameObject>(token);
            return res;
        }

        public static GameObject InstantiateVisual(GameObject model, Transform parent, string name,
                                                   Vector3 pos, Quaternion rot, bool stripColliders)
        {
            if (model == null) return null;
            GameObject inst = null;
            Guard.Try(Sys, "instantiate " + name, () =>
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                if (inst == null) inst = Object.Instantiate(model, parent);
                inst.name = name;
                inst.transform.SetParent(parent, false);
                inst.transform.position = pos;
                inst.transform.rotation = rot;
                if (stripColliders) StripColliders(inst);
                SeatOnGround(inst);
                _placed++;
            });
            return inst;
        }

        // =====================================================================
        //  WALL MODULE AUTHORITY (WO-1723 Lane B, owner ruling Q1 2026-09-14)
        // -----------------------------------------------------------------------------
        //  Owner, verbatim: "match the PANELS" — drive the collision segments from the art
        //  module's real width so ONE visible clad panel maps 1:1 to ONE destructible
        //  WallSegment. Measured before the ruling: 78 `Wall_Outer_*` segments against 60
        //  `Clad_*` panels on the same ring (WO-1723 §11.4), so destroying one segment could
        //  never clear one visible panel.
        //
        //  ⛔ THE TOKEN RULE LIVES HERE ONCE AND IS CALLED TWICE.
        //  RaidBaseGenerator.BuildConfigLayout asks for the module WIDTH before it partitions
        //  the ring; Dress asks for the same TOKEN when it clads it. A second copy of
        //  "dress.wallModule, else DefaultWall(kit), and wall_broken resolves to wall" is the
        //  duplicated-state class CLAUDE.md §2/§5/§8/§16 each describe going stale — and it
        //  would drift into the two halves disagreeing about how wide a panel is, which is
        //  precisely the defect this ticket exists to close. Do not inline either rule.
        // =====================================================================

        /// <summary>The dress kit for a config: the authored <c>raidDress.kit</c>, else <see cref="KitFor"/>.</summary>
        public static string KitOf(SceneConfigDef def)
        {
            if (def == null) return "hexagon-green";
            var dress = def.raidDress;
            return dress != null && !string.IsNullOrEmpty(dress.kit) ? dress.kit : KitFor(def.id);
        }

        /// <summary>The OUTER ring's authored wall token (pre-clad-resolution).</summary>
        private static string OuterWallToken(SceneConfigDef def, string kit)
        {
            var dress = def != null ? def.raidDress : null;
            return dress != null && !string.IsNullOrEmpty(dress.wallModule) ? dress.wallModule : DefaultWall(kit);
        }

        /// <summary>
        /// WO-1704 actual triangle RED, kept as the ONE copy of the rule: <c>wall_broken</c> has
        /// body-level holes even when its bounds touch, so an ENCLOSING ring uses the intact
        /// sibling. (Rubble stays decor — and, since WO-1723, the ruin swap's own model.)
        /// </summary>
        private static string ResolveCladModule(string token)
        {
            return token == "wall_broken" ? "wall" : token;
        }

        /// <summary>
        /// The art module width one ring's wall panels are actually laid out at, in metres, or
        /// 0 when the pack is missing / unmeasurable (callers then keep their own fallback).
        /// This is the number <c>RaidBaseGenerator.BuildRing</c> partitions the collision ring by
        /// under the Q1 ruling.
        /// </summary>
        /// <param name="inner">True for a keep ring (<see cref="InnerWall"/> may substitute a
        /// different module there), false for the outer perimeter.</param>
        public static float WallModuleWidth(SceneConfigDef def, bool inner)
        {
            if (def == null) return 0f;
            string kit = KitOf(def);
            string token = OuterWallToken(def, kit);
            if (inner) token = InnerWall(kit, token);
            var model = LoadVisual(ResolveCladModule(token));
            if (model == null) return 0f;
            float w = MeasureLongest(model);
            return w >= 0.2f ? w : 0f;
        }

        // -- kit defaults -----------------------------------------------------

        private static string KitFor(string id)
        {
            if (id == "fortified_garrison") return "synty-castle";
            if (id == "mage_enclave") return "dungeon-stone";
            return "hexagon-green";
        }

        /// <summary>
        /// The gatehouse module, per kit. A camp may override it with `raidDress.gate`
        /// (read at <c>:143</c>), so this is the fallback.
        /// <para/>
        /// ⚠ WO-1689: the two KayKit branches moved OFF the hexagon pack's
        /// <c>wall_straight_gate</c>. Measured, that piece is <b>2.00 x 1.41 x 0.90 m</b> - a
        /// hex-TILE prop - and `PlaceGatehouse` applies no scale, so it stood 1.41 m inside a
        /// 4.00 m wall: <b>0.35 of the wall's height</b>. `wall_gated` from the KayKit Dungeon
        /// Remastered pack is <b>4.00 x 4.00 x 1.00</b> - the SAME box as `wall` / `wall_broken`
        /// / `wall_cracked`, which is what every KayKit-walled camp now uses - so the gate and
        /// its wall match with **no scaling code at all**.
        /// <para/>
        /// ⚠ `synty-castle` is DELIBERATELY UNCHANGED. Its kit was authored as a matched set and
        /// already passes: wall 5.00 m, gate `SM_Bld_Castle_Wall_Gate_01` 5.86 m (1.17 of the
        /// wall), tower `SM_Bld_Castle_Wall_Tower_S_01` 7.52 m (1.50). Do not "unify" it onto
        /// the dungeon pack - it is the one kit that was already right.
        /// <para/>
        /// Pinned by <c>RaidBaseLayoutRegression.CaseGateReadsAsAGate</c>, which reads these
        /// tokens out of this method and fails when a gate drops below 0.80 of its wall.
        /// </summary>
        private static string DefaultGate(string kit)
        {
            if (kit == "synty-castle") return "SM_Bld_Castle_Wall_Gate_01";
            if (kit == "dungeon-stone") return "wall_gated";
            return "wall_gated";
        }

        /// <summary>
        /// The base's perimeter wall module, per kit. A camp may override it with
        /// `raidDress.wallModule` (read at <c>:144</c>), so THIS is only the fallback - and both
        /// seams had to move for WO-1637.
        /// <para/>
        /// ⚠ WO-1637, owner ruling 2026-09-10 12:07 - "the knee-high barrier reads as a WALL".
        /// The hexagon-green fallback was <c>"barrier"</c>, and the mesh is why it read as a
        /// railing: measured out of
        /// `KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/barrier.fbx`, it is
        /// <b>4.00 m wide x 1.10 m tall x 0.50 m thick</b>. A 1.8 m trooper stands well over it.
        /// That is exactly WO-1607 sec.6 `:139`'s "thickness, not a stick" failing - and the whole
        /// `barrier_*` family fails it identically (barrier_half 1.10, barrier_column 1.40,
        /// barrier_corner 1.40), so no sibling token could have fixed it.
        /// <para/>
        /// <c>"wall"</c> from the same pack is <b>4.00 x 4.00 x 1.00</b>. CladRing measures the
        /// LONGEST axis (4.00, unchanged) and fits it along the run, so the ring's piece count,
        /// step and gate cut-outs are all identical to before; only the mass changes. At this
        /// camp's radius the fit factor lands near 0.95, i.e. a ~3.8 m wall about 0.95 m thick -
        /// twice a trooper's height. The material is unchanged (`dungeon_texture_URP`, a real
        /// `_BaseMap`), because it is the same atlas the `barrier` already used.
        /// <para/>
        /// REACH: this line reaches <b>iron_bastion only</b> - it is the one hexagon-green camp
        /// with no `raidDress` block at all. `raider_camp_small` authors its own `wallModule` and
        /// is moved in `scene-configs.json` instead (to `wall_broken`, the ruined variant, same
        /// 4.00 x 4.00 x 1.00 box, because that camp's own fiction is a stripped settlement).
        /// </summary>
        private static string DefaultWall(string kit)
        {
            if (kit == "synty-castle") return "SM_Bld_Castle_Wall_01";
            if (kit == "dungeon-stone") return "wall_cracked";
            return "wall";
        }

        private static string InnerWall(string kit, string outer)
        {
            if (kit == "dungeon-stone") return "wall";
            return outer;
        }

        /// <summary>
        /// The DESTROYED-wall module, per kit (WO-1723 Lane B, owner ruling 2026-09-14: <i>"replace
        /// with a destroyed wall ... that I can step over"</i>). Returned as an ordered candidate
        /// list so a kit whose first choice is missing still ships rubble instead of an empty gap.
        /// <para/>
        /// ⛔ <c>wall_broken</c> IS NOT ON THIS LIST, AND THAT IS DELIBERATE. WO-1689 measured it at
        /// <b>4.00 x 4.00 x 1.00 — the SAME box as <c>wall</c></b> (see <see cref="DefaultGate"/>'s
        /// note). It is a damaged-but-STANDING wall, not something a hero steps over, so using it
        /// would reproduce the exact symptom this ticket exists to fix: a destroyed section that
        /// still reads as a wall. `rubble_large` / `rubble_half` are the KayKit pack's actual ground
        /// debris (`DressGateMouth` already loads `rubble_large`), and the Synty castle kit ships a
        /// purpose-built destroyed-wall family.
        /// </summary>
        private static string[] RubbleTokens(string kit)
        {
            if (kit == "synty-castle")
                return new[]
                {
                    "SM_Bld_Castle_DestroyedWall_Rubble_Bottom_01",
                    "SM_Bld_Castle_DestroyedWall_RubblePile_01",
                    "SM_Bld_Castle_DestroyedWall_RubbleBlock_01",
                };
            return new[] { "rubble_large", "rubble_half" };
        }

        private static string DefaultFloor(string kit)
        {
            if (kit == "hexagon-green") return "floor_dirt_large";
            return "floor_tile_large";
        }

        private static string DefaultTower(string kit)
        {
            if (kit == "synty-castle") return "SM_Bld_Castle_Wall_Tower_M_01";
            if (kit == "dungeon-stone") return "Tower_Castle_Square";
            return "building_watchtower_green";
        }

        // -- zones ------------------------------------------------------------

        private static Transform EnsureZone(Transform root, string name)
        {
            var existing = root.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        // -- walls / gate -----------------------------------------------------

        /// <summary>
        /// WO-1637 step 1 gave this method a <paramref name="sceneId"/>. It had only `kit`, and
        /// <see cref="KitFor"/> routes BOTH `raider_camp_small` AND `iron_bastion` to
        /// "hexagon-green" (neither id matches the two named branches), so a run that bakes every
        /// camp printed nothing that could tell those two apart - the same defect WO-1619 fixed on
        /// the spire line, whose reasoning is written at RaidBaseGenerator.PlaceSpire.
        /// <para/>
        /// The trace reads the values BACK OUT of <see cref="RenderSettings"/> after the branch
        /// has run, rather than echoing the literals above it. Echoing the literal proves only
        /// that this method was compiled; reading it back proves what the scene will actually be
        /// SAVED with, which is the thing the ticket is about (CLAUDE.md sec.11B).
        /// </summary>
        private static void DressAtmosphere(string kit, string sceneId)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            if (kit == "dungeon-stone")
            {
                RenderSettings.fogColor = new Color(0.14f, 0.13f, 0.16f);
                RenderSettings.fogStartDistance = 18f;
                RenderSettings.fogEndDistance = 88f;
                RenderSettings.ambientLight = new Color(0.22f, 0.20f, 0.24f);
            }
            else if (kit == "synty-castle")
            {
                RenderSettings.fogColor = new Color(0.58f, 0.55f, 0.50f);
                RenderSettings.fogStartDistance = 28f;
                RenderSettings.fogEndDistance = 115f;
                RenderSettings.ambientLight = new Color(0.38f, 0.36f, 0.33f);
            }
            else
            {
                // ── hexagon-green: raider_camp_small AND iron_bastion (KitFor sends both here).
                //
                // ⚠ WO-1637, owner ruling 2026-09-10 12:07 - "FOG FIRST: push the fog end past the
                // ring and drop the density so the ring silhouette reads against the sky."
                //
                // THE ARITHMETIC IS THE FINDING, and it is why 22/95 had to move.
                //
                // ⚠ MEASURE FROM THE CAMERA, NOT FROM THE ARENA CENTRE. Unity's linear fog is a
                // function of distance to the CAMERA, and the hero deploys at RaidStagingPoint
                // (0, 0, -51.2) facing north - so the ring arc he is looking AT is far further
                // than the ring's own +/-68.6 m radius suggests. WO-1637 sec.1d made exactly this
                // conflation and it understated the problem. From the deploy seat:
                //     north side midpoint .... 119.8 m      E/W side midpoints ..... 85.6 m
                //     north corners .......... 138.0 m      south side (behind) .... 17.4 m
                //
                // Under 22/95 EVERY ONE of those forward distances is past the 95 m end, so the
                // whole visible arc rendered at 100% fog - it WAS the fog colour, and so was the
                // ground under it. That is why changing the palette alone could not have worked:
                // at 100% fog the material contributes nothing at all.
                // Measured on the shipped frame (build 363529,
                // Builds/device-frames/2026-09-10_0614_arena_06_wide.png, greyscale luminance):
                // ring band median 0.595 vs the fog colour's own luminance 0.585 - and vs the sky
                // immediately above it, mean 0.670 against 0.677, a delta of 0.007.
                //
                // start 22 -> 45 m  : the base itself is radius 31 m and its near wall is ~20 m
                //                     from the seat, so the walls, the gate and the courtyard now
                //                     sit in CLEAR air instead of behind a curtain.
                // end   95 -> 200 m : past the FURTHEST thing a raider can look at - the far ring
                //                     corner at 138.0 m from the deploy seat - so NOTHING in the
                //                     arena is ever fully fogged again. The visible ring arc now
                //                     renders at 26% fog (E/W) to 60% (far corners): a depth cue,
                //                     not an eraser.
                //
                // The fog COLOUR is deliberately unchanged: it is this camp's identity and the
                // ruling moved the end distance and the density, not the hue.
                RenderSettings.fogColor = new Color(0.66f, 0.58f, 0.42f);
                RenderSettings.fogStartDistance = 45f;
                RenderSettings.fogEndDistance = 200f;
                RenderSettings.ambientLight = new Color(0.42f, 0.36f, 0.26f);
            }

            TraceAtmosphere(kit, sceneId);
        }

        /// <summary>
        /// One RenderSettings line per bake (WO-1637 step 1). Read back, never echoed.
        /// </summary>
        private static void TraceAtmosphere(string kit, string sceneId)
        {
            Guard.Try(Sys, "atmosphere trace", () =>
            {
                var fogC = RenderSettings.fogColor;
                var ambC = RenderSettings.ambientLight;

                string fogOn = RenderSettings.fog ? "ON" : "OFF";
                string fogRgb = "(" + fogC.r.ToString("F3") + ", " + fogC.g.ToString("F3") + ", " +
                                fogC.b.ToString("F3") + ")";
                string ambRgb = "(" + ambC.r.ToString("F3") + ", " + ambC.g.ToString("F3") + ", " +
                                ambC.b.ToString("F3") + ")";
                string start = RenderSettings.fogStartDistance.ToString("F1");
                string end = RenderSettings.fogEndDistance.ToString("F1");
                string mode = RenderSettings.fogMode.ToString();

                FlowTrace.Step(Sys, "ATMOSPHERE '" + sceneId + "' kit=" + kit + " fog=" + fogOn +
                               " mode=" + mode + " colour=" + fogRgb + " start=" + start + "m end=" +
                               end + "m ambient=" + ambRgb);
            });
        }

        /// <summary>
        /// Switch off the WallSegment's own placeholder mesh (the one PlaceSegment sized its
        /// BoxCollider from). The wall the player sees is the clad panel built by
        /// <see cref="CladRing"/>.
        /// <para/>
        /// ⚠ WO-1723 Lane B made this method's SCOPE load-bearing. The clad panel and the baked
        /// ruin are now CHILDREN of the <c>Wall_*</c> segment, so an unguarded sweep would blank
        /// the visible wall itself. Ordering saves it today (Dress calls this BEFORE CladRing),
        /// but ordering is not a contract — a second Dress pass, or anything that re-hides walls,
        /// would silently erase the entire wall ring and read as a successful bake. The explicit
        /// skip is the contract; do not remove it in favour of "it runs first".
        /// </summary>
        private static void HideWallRenderers(Transform root)
        {
            var walls = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < walls.Length; i++)
            {
                if (walls[i] == null || walls[i].name == null) continue;
                if (!walls[i].name.StartsWith("Wall_")) continue;
                var rends = walls[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                {
                    if (rends[r] == null) continue;
                    if (IsDresserOwnedVisual(rends[r].transform)) continue;
                    rends[r].enabled = false;
                }
            }
        }

        /// <summary>
        /// True when a transform is (or sits under) a dresser-authored wall visual — the visible
        /// clad panel or the baked ruin. Both are parented to their WallSegment by WO-1723 §4.1
        /// and must never be caught by <see cref="HideWallRenderers"/>'s <c>Wall_*</c> sweep.
        /// </summary>
        private static bool IsDresserOwnedVisual(Transform t)
        {
            while (t != null)
            {
                if (t.name != null && (t.name.StartsWith("Clad_") || t.name.StartsWith("Ruin_")))
                    return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// Clad one wall ring. WO-1689: RETURNS the wall height actually achieved (the module's
        /// measured height times the fit factor), so `PlaceGatehouse` can state the gate against
        /// the wall it stands in. Returns 0 when the module could not be loaded.
        ///
        /// ⛔ PROVEN CAUSE, breach-tap wall-miss (live device capture): <see cref="HideWallRenderers"/>
        /// switches off the WallSegment's own mesh (the one <c>RaidBaseGenerator.PlaceSegment</c>
        /// sized its BoxCollider FROM), then this method builds an entirely separate "Clad_*"
        /// cosmetic mesh at the module's NATIVE height — <see cref="FitPieceAlong"/> deliberately
        /// fits only the run axis and "preserves authored height", by design. X/Z track perfectly
        /// (same footprint) but Y was left to whatever the art module's native mesh height is —
        /// captured live at 15.0m visual vs a 3.0m collider on 'Wall_Outer_SS_3', 5x apart. The
        /// player taps the tall, very-visible cosmetic wall; there is no collider above 3m to hit.
        /// <paramref name="ringPrefix"/> is the SAME token BuildRing bakes into every
        /// "Wall_{ringPrefix}_S{side}_{i}" name, so <see cref="SyncWallColliderHeight"/> below can
        /// re-derive the hidden WallSegment colliders' height from THIS method's achievedHeight —
        /// the collider now always tracks whatever height the clad visual actually ends up at,
        /// for any tier/kit/module, not just this one wall id.
        ///
        /// =====================================================================================
        /// ⛔ WO-1723 LANE B — THIS METHOD NO LONGER COMPUTES A PARTITION. IT WALKS THE SEGMENTS.
        /// =====================================================================================
        /// ROOT CAUSE it closes (WO-1723 §1, measured on the device): the panel this method used
        /// to build was instantiated under a root-level <c>Zone_Clad</c> zone — a SIBLING of the
        /// <c>WallSegment</c>, not a child. Both systems that must treat a wall as destructible
        /// walk that hierarchy and therefore missed it:
        ///   * <c>RaidNavBake</c>'s <c>GetComponentInParent&lt;WallSegment&gt;()</c> test found no
        ///     segment above a clad panel, so the entire VISIBLE ring baked in as permanent,
        ///     non-carvable navmesh geometry — 645 of 656 live probes read NOT-WALKABLE over a
        ///     collapsed wall, forever.
        ///   * <c>WallSegment.CollapseRoutine</c>'s <c>GetComponentsInChildren&lt;Renderer&gt;()</c>
        ///     walks DOWN, so the collapse sink moved only the segment's own meshes — which
        ///     <see cref="HideWallRenderers"/> had already DISABLED. 80/80 collapses logged a
        ///     successful "ruin SETTLED" line while the player watched an unbroken wall
        ///     (owner screenshot: <c>Razed 28%</c>, wall pristine edge to edge).
        ///
        /// ⛔ AND THE TWO PARTITIONS COULD NEVER HAVE BEEN MADE TO AGREE BY ARITHMETIC.
        /// The old code laid panels out over <c>-radius..+radius</c> at the art module width with
        /// its own gate cut; <c>BuildRing</c> lays segments out over
        /// <c>run = 2*halfExtent - 2*towerHalf</c>, forces the count ODD, and removes
        /// <c>gateSpan</c> CENTRE CELLS. Measured on the shipped ring that was 78 segments against
        /// 60 panels (§11.4). "Resolve the segment whose footprint contains the panel centre"
        /// (§4.1's first sketch) therefore produces straddles at every join and orphans at both
        /// ends. The 1:1 map is only guaranteed by CONSTRUCTION: the generator owns the partition
        /// (now driven by <see cref="WallModuleWidth"/> per the owner's Q1 ruling), and this method
        /// emits exactly one panel per segment it finds, sized from that segment's own collider.
        ///
        /// ⚠ THE RE-PARENT MUST PRESERVE WORLD SCALE, AND THAT IS NOT OPTIONAL.
        /// <c>RaidBaseGenerator.PlaceSegment</c> puts a NON-UNIFORM fit scale on the segment ROOT
        /// (x = segWidth/rawX, y = 3.0/rawY, z = 1.5/rawZ). Instantiating the clad with the segment
        /// as its parent would inherit that and squash every panel. So each panel is built and
        /// fitted under the unscaled <c>Zone_Clad</c> zone first and only then re-parented with
        /// <c>SetParent(segment, worldPositionStays: true)</c> — exact here because the panel and
        /// the segment carry the SAME yaw, so Unity's component-wise lossy-scale division has no
        /// shear to lose.
        ///
        /// ⚠ <paramref name="twoGates"/>, <paramref name="northGate"/> and
        /// <paramref name="gateWidth"/> NO LONGER PLACE ANYTHING. BuildRing already removed the
        /// gate cells, so the gap is simply where no segment exists — the gate cut can no longer
        /// be described twice and disagree with itself, which is the same 78-vs-60 class of bug
        /// one paragraph up. They are kept on the signature because <paramref name="gateWidth"/>
        /// is still REPORTED (the bake log has to state the opening the ring actually has), and
        /// the two flags keep the call sites readable about which ring is gated where. Do not
        /// re-derive a cut from them here.
        /// Likewise <c>piece</c> below is now MEASURED AND REPORTED ONLY: the partition it used to
        /// drive moved to <see cref="WallModuleWidth"/> and BuildRing.
        /// </summary>
        private static float CladRing(Transform root, string token, float radius, float gateWidth,
                                      bool twoGates, string kit, string ringPrefix, bool northGate = false)
        {
            string resolvedToken = ResolveCladModule(token);
            var model = LoadVisual(resolvedToken);
            if (model == null) { WarnMissing(resolvedToken); return 0f; }
            float piece = MeasureLongest(model);
            if (piece < 0.2f) { WarnMissing(resolvedToken + " measurable width"); return 0f; }
            var parent = EnsureZone(root, "Zone_Clad");
            string cladPath = AssetDatabase.GetAssetPath(model);

            // The destroyed-wall model, resolved ONCE per ring. A null here is not fatal: the
            // ring still clads, WallRuinPresenter is simply not authored, and WallSegment keeps
            // its legacy sink. The bake log says which happened.
            string ruinToken;
            var ruinModel = LoadRubble(kit, out ruinToken);
            float ruinModule = ruinModel != null ? MeasureLongest(ruinModel) : 0f;

            string namePrefix = "Wall_" + ringPrefix + "_S";
            var segments = new List<WallSegment>();
            foreach (var ws in root.GetComponentsInChildren<WallSegment>(true))
            {
                if (ws == null || ws.name == null) continue;
                if (!ws.name.StartsWith(namePrefix)) continue;
                segments.Add(ws);
            }
            if (segments.Count == 0)
            {
                FlowTrace.Warn(Sys, "CLAD ring='" + ringPrefix + "' found 0 '" + namePrefix +
                    "*' WallSegments under '" + root.name + "' - the ring is UNCLAD and nothing " +
                    "visible exists to destroy. BuildRing must run before Dress.");
                return 0f;
            }

            bool cladTraced = false;
            float achievedHeight = 0f;
            int placed = 0;
            int ruined = 0;

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var st = seg.transform;
                var box = seg.GetComponent<BoxCollider>();
                if (box == null)
                {
                    FlowTrace.Warn(Sys, "CLAD '" + seg.name + "' has no BoxCollider - its panel " +
                                        "width is unknowable, so it is left unclad.");
                    continue;
                }

                // The panel's width IS the segment's collision footprint (owner ruling Q1: one
                // panel, one destructible section). Read out of the built tree, never recomputed.
                float segW = Mathf.Abs(box.size.x * st.lossyScale.x);
                if (segW < 0.2f) continue;
                var centre = new Vector3(st.position.x, 0f, st.position.z);
                var rot = st.rotation;

                var go = InstantiateVisual(model, parent, "Clad_" + seg.name, centre, rot, true);
                if (go == null) continue;
                // WO-1704 GREEN residual: touching float bounds left sampled triangle seams. The
                // lap is now applied at BOTH ends of every panel (CladLap total), because a panel
                // is sized from its segment rather than from a run-relative index that knew which
                // ends were run endpoints. 1 cm of overhang at a gate edge is far inside the
                // MinGateWidth floor and is measured by RaidWallContinuityRegression either way.
                FitPieceAlong(go, segW + CladLap, centre);

                if (!cladTraced)
                {
                    cladTraced = true;
                    achievedHeight = MeasureHeight(go);
                    ArenaBoundaryRing.TraceMaterials(Sys, "base wall token='" + token +
                        "' resolved='" + resolvedToken + "' kit=" + kit + " radius=" + radius.ToString("F1"),
                        cladPath, go);
                }

                var ruin = BuildRuin(parent, ruinModel, ruinModule, seg.name, centre, rot, segW);
                float ruinHeight = ruin != null ? MeasureHeight(ruin) : 0f;
                var ruinStep = ruin != null ? ruin.GetComponentInChildren<BoxCollider>(true) : null;

                // ⚠ RE-PARENT LAST, WORLD TRANSFORM PRESERVED (see the header). Everything above
                // was measured and fitted under the unscaled zone.
                go.transform.SetParent(st, true);
                if (ruin != null)
                {
                    ruin.transform.SetParent(st, true);
                    ruin.SetActive(false);
                    var presenter = seg.gameObject.GetComponent<WallRuinPresenter>();
                    if (presenter == null) presenter = seg.gameObject.AddComponent<WallRuinPresenter>();
                    presenter.Author(seg, go.transform, ruin.transform, ruinStep, ruinToken, ruinHeight);
                    ruined++;
                }
                placed++;

                if (i == 0)
                {
                    // §12 — the re-parent is the whole fix, so PROVE it survived rather than
                    // assuming it: a squashed lossyScale here is the non-uniform-parent trap.
                    var ls = go.transform.lossyScale;
                    FlowTrace.Step(Sys, "CLAD REPARENT '" + go.name + "' -> '" + seg.name +
                        "' segW=" + segW.ToString("F2") + "m panelLossyScale=(" +
                        ls.x.ToString("F3") + ", " + ls.y.ToString("F3") + ", " + ls.z.ToString("F3") +
                        ") segmentLossyScale=(" + st.lossyScale.x.ToString("F3") + ", " +
                        st.lossyScale.y.ToString("F3") + ", " + st.lossyScale.z.ToString("F3") +
                        ") - GetComponentInParent<WallSegment>() now answers for this panel, so " +
                        "RaidNavBake excludes it and WallSegment owns its destruction.");
                }
            }

            int fillers = CladCorners(parent, model, radius, ringPrefix, segments);

            FlowTrace.Step(Sys, "WALL token='" + token + "' resolved='" + resolvedToken + "' kit=" + kit +
                " radius=" + radius.ToString("F1") + "m module=" + piece.ToString("F2") +
                "m achievedH=" + achievedHeight.ToString("F2") + "m panels=" + placed +
                " segments=" + segments.Count + " ruinsBaked=" + ruined + " ruinToken='" + ruinToken +
                "' cornerFillers=" + fillers + " gateWidth=" + gateWidth.ToString("F3") +
                "m joined=true panelLap=" + CladLap.ToString("F3") + "m parent=WallSegment(1:1)");
            if (achievedHeight > 0f) SyncWallColliderHeight(root, ringPrefix, achievedHeight);
            return achievedHeight;
        }

        /// <summary>
        /// Resolve the kit's destroyed-wall art, trying each candidate in
        /// <see cref="RubbleTokens"/> in order. Returns null (with the attempted tokens named in
        /// the warn) when the pack is not imported — the caller then skips the ruin swap rather
        /// than baking a presenter that points at nothing.
        /// </summary>
        private static GameObject LoadRubble(string kit, out string resolved)
        {
            var candidates = RubbleTokens(kit);
            for (int i = 0; i < candidates.Length; i++)
            {
                var go = LoadVisual(candidates[i]);
                if (go != null) { resolved = candidates[i]; return go; }
            }
            resolved = "";
            FlowTrace.Warn(Sys, "RUIN kit=" + kit + " resolved NO destroyed-wall art from [" +
                string.Join(", ", candidates) + "] - collapsed walls in this kit keep the legacy " +
                "sink instead of swapping to rubble (art pack not imported?).");
            return null;
        }

        /// <summary>
        /// Build one section's destroyed-wall stand-in: a holder at the panel footprint carrying
        /// TILED copies of the rubble module plus a single LOW step-over BoxCollider. Returns the
        /// holder (still ACTIVE, still under <paramref name="zone"/>) so the caller can measure it
        /// before re-parenting and disabling it.
        /// <para/>
        /// ⛔ THE RUBBLE IS TILED, NOT STRETCHED. Owner ruling WO-1704, restated on WO-1723 §7 Q1:
        /// do NOT squeeze art off its authored module — that is the seam defect WO-1704 was opened
        /// to fix. A single rubble pile scaled to a ~3.9 m panel reads as a smeared boulder; two or
        /// three at their authored size read as a collapsed wall.
        /// <para/>
        /// ⛔ THE STEP COLLIDER IS DELIBERATELY NOT ON THE "Structure" LAYER. Structure is the mask
        /// every tower line-of-sight linecast fires against (WallSegment.cs:35-39); putting rubble
        /// there would re-block the shot through a breach the player just paid for. It is also the
        /// reason a residual collider is safe at all: WO-1723 §11.3 measured that the hero is a
        /// kinematically-driven NavMeshAgent with NO physics casts whatsoever, so the navmesh, not
        /// this box, is what decides whether she can walk through.
        /// </summary>
        private static GameObject BuildRuin(Transform zone, GameObject ruinModel, float ruinModule,
                                            string segName, Vector3 centre, Quaternion rot, float segW)
        {
            if (ruinModel == null || zone == null) return null;

            var holder = new GameObject("Ruin_" + segName);
            holder.transform.SetParent(zone, false);
            holder.transform.SetPositionAndRotation(centre, rot);
            holder.transform.localScale = Vector3.one;

            var along = rot * Vector3.right;
            float module = ruinModule > 0.3f ? ruinModule : segW;
            int n = Mathf.Clamp(Mathf.RoundToInt(segW / module), 1, 6);
            float step = segW / n;
            int seed = StableHash(segName);
            for (int i = 0; i < n; i++)
            {
                float offset = -segW * 0.5f + (i + 0.5f) * step;
                var pos = centre + along * offset;
                // Deterministic yaw jitter (a re-bake reproduces the identical ruin), small enough
                // that the pile still reads as a wall line rather than scattered debris.
                float yaw = ((seed + i * 97) % 31) - 15f;
                InstantiateVisual(ruinModel, holder.transform, "RuinPiece_" + i, pos,
                                  rot * Quaternion.Euler(0f, yaw, 0f), true);
            }

            // The step-over box. The holder is identity-scaled and carries the segment's yaw, so a
            // child at local origin maps local box units 1:1 onto world metres along the panel.
            var stepGo = new GameObject("RuinStep");
            stepGo.transform.SetParent(holder.transform, false);
            stepGo.transform.localPosition = Vector3.zero;
            stepGo.transform.localRotation = Quaternion.identity;
            stepGo.transform.localScale = Vector3.one;
            var stepBox = stepGo.AddComponent<BoxCollider>();
            float measured = MeasureHeight(holder);
            float h = Mathf.Clamp(measured, 0.15f, RuinStepOverHeight);
            stepBox.size = new Vector3(segW, h, RuinStepThickness);
            stepBox.center = new Vector3(0f, h * 0.5f, 0f);
            stepBox.isTrigger = false;
            return holder;
        }

        /// <summary>
        /// Clad the two stubs at each side's ends — the span between the outermost WallSegment and
        /// the ring corner, which <c>BuildRing</c> deliberately leaves to the corner post
        /// (<c>run = 2*halfExtent - 2*towerHalf</c>). These pieces stay under <c>Zone_Clad</c>
        /// because NO WallSegment owns them: they are behind the corner tower and are not
        /// destructible.
        /// <para/>
        /// ⚠ THIS IS ALSO THE ANSWER TO "is RaidNavBake.IsUnderCladZone redundant now?" — it is
        /// NOT. The per-segment panels are excluded from NavigationStatic by the
        /// <c>GetComponentInParent&lt;WallSegment&gt;()</c> test alone, but these corner stubs have
        /// no segment above them and only the Zone_Clad name test reaches them.
        /// <para/>
        /// Without them <c>RaidWallContinuityRegression.CheckCladding</c>'s ray sweep — which
        /// samples the FULL <c>-radius..+radius</c> span of every ungated side — would report a
        /// ~towerHalf-wide mesh hole at all eight corners, because its probe set is built from the
        /// cladding alone and never includes the corner posts.
        /// </summary>
        private static int CladCorners(Transform zone, GameObject model, float radius,
                                       string ringPrefix, List<WallSegment> segments)
        {
            if (model == null || zone == null || segments == null) return 0;
            int made = 0;
            for (int s = 0; s < 4; s++)
            {
                var rot = Quaternion.Euler(0f, 90f * s, 0f);
                var along = rot * Vector3.right;

                // ⚠ The side's radial line is MEASURED off the segments, never recomputed from
                // `radius`. BuildRing lays the ring at `halfExtent`, which is Max(towerHalf +
                // MinSegmentWidth, targetHalfExtent) — equal to the radius on every shipped camp,
                // but a stub placed on the WRONG line would sit at a different x and read as a
                // whole-side seam to RaidWallContinuityRegression, which groups panels by x.
                bool found = false;
                var mid = Vector3.zero;
                float minEdge = float.MaxValue;
                float maxEdge = float.MinValue;
                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    if (seg == null) continue;
                    if (Quaternion.Angle(seg.transform.rotation, rot) > 1f) continue;
                    var box = seg.GetComponent<BoxCollider>();
                    if (box == null) continue;
                    var p = new Vector3(seg.transform.position.x, 0f, seg.transform.position.z);
                    float a = Vector3.Dot(p, along);
                    if (!found) { mid = p - along * a; found = true; }
                    float half = Mathf.Abs(box.size.x * seg.transform.lossyScale.x) * 0.5f;
                    minEdge = Mathf.Min(minEdge, a - half);
                    maxEdge = Mathf.Max(maxEdge, a + half);
                }
                if (!found) continue;   // no segments on this side at all

                made += CladStub(zone, model, mid, along, rot, -radius, minEdge, s, "L");
                made += CladStub(zone, model, mid, along, rot, maxEdge, radius, s, "R");
            }
            if (made > 0)
                FlowTrace.Step(Sys, "CLAD corners ring='" + ringPrefix + "' stubs=" + made +
                    " (the span each side leaves to its corner post; no WallSegment owns these, so " +
                    "they stay under Zone_Clad and RaidNavBake.IsUnderCladZone is what excludes them).");
            return made;
        }

        private static int CladStub(Transform zone, GameObject model, Vector3 mid, Vector3 along,
                                    Quaternion rot, float start, float end, int side, string label)
        {
            float length = end - start;
            if (length <= 0.05f) return 0;
            var pos = mid + along * ((start + end) * 0.5f);
            var go = InstantiateVisual(model, zone, "Clad_Corner_S" + side + "_" + label, pos, rot, true);
            if (go == null) return 0;
            FitPieceAlong(go, length + CladLap, pos);
            return 1;
        }

        /// <summary>
        /// Re-derives the hidden WallSegment BoxColliders on one ring from the clad visual's
        /// FINAL achieved height (see the "PROVEN CAUSE" note on <see cref="CladRing"/>). Finds
        /// every "Wall_{ringPrefix}_S..." object BuildRing baked, keeps each collider's ground
        /// seat (its LOCAL min-Y is untouched — only the footprint-authored X/Z, never touched
        /// here either), and stretches its Y to match the visible wall exactly. This is the fix
        /// point for ANY tier/kit/module combination, not a special case for one wall id: whatever
        /// height CladRing achieves next time, the collider is re-synced to it right here.
        /// </summary>
        private static void SyncWallColliderHeight(Transform root, string ringPrefix, float achievedHeight)
        {
            if (root == null || achievedHeight <= 0f || string.IsNullOrEmpty(ringPrefix)) return;
            string namePrefix = "Wall_" + ringPrefix + "_S";
            var xforms = root.GetComponentsInChildren<Transform>(true);
            int synced = 0;
            for (int i = 0; i < xforms.Length; i++)
            {
                var t = xforms[i];
                if (t == null || t.name == null || !t.name.StartsWith(namePrefix)) continue;
                var box = t.GetComponent<BoxCollider>();
                if (box == null) continue;

                float sy = Mathf.Abs(t.lossyScale.y) > 0.0001f ? t.lossyScale.y : 1f;
                float oldWorldHeight = box.size.y * sy;
                if (Mathf.Approximately(oldWorldHeight, achievedHeight)) continue;

                float oldMinLocalY = box.center.y - box.size.y * 0.5f;   // ground-seat offset, kept
                float newSizeY = achievedHeight / sy;
                box.size = new Vector3(box.size.x, newSizeY, box.size.z);
                box.center = new Vector3(box.center.x, oldMinLocalY + newSizeY * 0.5f, box.center.z);
                synced++;

                FlowTrace.Step("RaidBaseGenerator",
                    "WALL COLLIDER SYNC '" + t.name + "': visual(renderer) height=" +
                    achievedHeight.ToString("F2") + "m, collider height before=" +
                    oldWorldHeight.ToString("F2") + "m -> after=" + achievedHeight.ToString("F2") +
                    "m (breach-tap wall-miss fix: the WallSegment collider is re-derived from " +
                    "CladRing's FINAL achieved visual height, not the pre-clad SegSize.y fit).");
            }
            if (synced == 0)
                FlowTrace.Warn("RaidBaseGenerator",
                    "WALL COLLIDER SYNC ring='" + ringPrefix + "' achievedHeight=" +
                    achievedHeight.ToString("F2") + "m but found 0 '" + namePrefix +
                    "*' colliders under '" + root.name + "' - visual/hitbox height may still diverge.");
        }

        private static void PlaceGatehouse(Transform zone, string token, string kit, Vector3 pos,
                                           float yaw, float width, string side, float wallHeight = 0f)
        {
            string resolvedToken = token == "wall_gated" ? "wall_doorway" : token;
            var model = LoadVisual(resolvedToken);
            if (model == null)
            {
                model = LoadVisual("Gate_Medieval_Medium");
                if (model == null) WarnMissing(token);
            }
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var gate = InstantiateVisual(model, zone, "Gatehouse_" + side, pos, rot, stripColliders: false);
            if (gate != null)
            {
                OpenGateAssembly(gate, kit, width, pos);
                SeatGateThreshold(gate, pos);
                int structure = LayerMask.NameToLayer("Structure");
                if (structure >= 0) gate.layer = structure;
            }

            // ⚠ WO-1689: hexagon-green's flank moved OFF `building_watchtower_green` (1.04 x
            // 1.11 x 1.04 m) - at 0.28 of a 4.00 m wall it was a tower SHORTER than the wall it
            // flanks. `wall_pillar` (4.00 x 4.00 x 1.50) is what `dungeon-stone` already used
            // and is the same pack as the wall. `synty-castle` keeps its own matched tower.
            string flankTok = kit == "synty-castle" ? "SM_Bld_Castle_Wall_Tower_S_01"
                : kit == "dungeon-stone" ? "wall_pillar" : "wall_pillar";
            var flank = LoadVisual(flankTok);
            if (flank == null) flank = LoadVisual("Tower_Wooden_Watchtower");

            // ⛔ THE FLANK MUST STAND BESIDE THE OPENING, NEVER INSIDE IT.
            // The old offset was `Mathf.Max(3.2f, width * 0.55f)` = 4.70 m at this camp, which
            // was safe ONLY because the flank was 1.04 m wide (its inner face sat at 4.18 m,
            // outside the 4.28 m half-opening). `wall_pillar` is 4.00 m wide: at the same
            // offset its inner face would sit at 2.70 m - i.e. 1.6 m INSIDE the gate mouth,
            // narrowing the walkable slit either side of the gate from 3.28 m to 0.7 m. That is
            // below MinGateWidth and below anything a NavMeshAgent can path through, so the
            // taller tower would have SEALED the base. RaidNavBake marks these renderers
            // NavigationStatic and bakes, so the seal would have been real, not cosmetic.
            // Deriving the offset from the flank's MEASURED span keeps its inner face on the
            // opening's edge whatever module is chosen next.
            float flankSpan = flank != null ? MeasureLongest(flank) : 0f;
            float offset = Mathf.Max(Mathf.Max(3.2f, width * 0.55f), width * 0.5f + flankSpan * 0.5f);
            var right = rot * Vector3.right;
            if (flank != null)
            {
                InstantiateVisual(flank, zone, "GateFlank_" + side + "_L", pos - right * offset, rot, true);
                InstantiateVisual(flank, zone, "GateFlank_" + side + "_R", pos + right * offset, rot, true);
            }

            // WO-1689: the line now names HEIGHTS, not just the opening's width and an art name.
            // Nothing on this path reported a height before, so no log could ever have caught a
            // gate that did not fit its wall. Parts are computed into locals first - the compile
            // gate's brace scanner has no interpolated-string model (CLAUDE.md sec.1).
            string gateArt = model != null ? model.name : "MISSING";
            float gateH = MeasureHeight(gate);
            float flankH = MeasureTallest(flank);
            string gateRatio = wallHeight > 0.01f ? (gateH / wallHeight).ToString("F2") : "n/a";
            string flankRatio = wallHeight > 0.01f ? (flankH / wallHeight).ToString("F2") : "n/a";
            FlowTrace.Step(Sys, $"GATE {side} width={width:F2}m requested={token} art={gateArt} gateH={gateH:F2}m " +
                                $"wallH={wallHeight:F2}m gate/wall={gateRatio} flank={flankTok} " +
                                $"flankH={flankH:F2}m flank/wall={flankRatio} flankOffset={offset:F2}m");
        }

        /// <summary>
        /// Fill the empty plaza beside the gate (owner 2026-09-09). Spikes / crates
        /// sit on BOTH sides, outside and just inside, and never in the walkable
        /// slit (|x| &lt; MinGateWidth/2).
        /// </summary>
        private static void DressGateMouth(Transform approach, Transform gatehouse, string kit,
                                           LayoutContext ctx)
        {
            string spike = kit == "synty-castle" ? "SM_Prop_Spike_Fortification_01"
                         : kit == "dungeon-stone" ? "rubble_large" : "rubble_large";
            string crate = "crate_large";
            PlaceGateFlanks(approach, spike, crate, ctx, south: true);
            if (ctx.TwoGates) PlaceGateFlanks(gatehouse, spike, crate, ctx, south: false);
        }

        private static void PlaceGateFlanks(Transform zone, string spikeTok, string crateTok,
                                            LayoutContext ctx, bool south)
        {
            float sign = south ? -1f : 1f;
            float r = ctx.Radius;
            float flank = Mathf.Max(4.5f, ctx.GateWidth * 0.5f + 2.4f);
            float[] xs = { -flank, flank };
            float[] zs = { r * sign + 3.2f * sign, r * sign - 3.8f * sign };
            var spike = LoadVisual(spikeTok);
            var crate = LoadVisual(crateTok);
            int n = 0;
            for (int i = 0; i < xs.Length; i++)
            {
                for (int j = 0; j < zs.Length; j++)
                {
                    var pos = new Vector3(xs[i], 0f, zs[j]);
                    if (Mathf.Abs(pos.x) < MinGateWidth * 0.5f) continue;
                    var model = (i + j) % 2 == 0 ? spike : crate;
                    if (model == null) model = spike ?? crate;
                    if (model == null) continue;
                    InstantiateVisual(model, zone, "GateFlankProp", pos,
                                      Quaternion.Euler(0f, 90f * i, 0f), true);
                    n++;
                }
            }
            FlowTrace.Step(Sys, "gate mouth " + (south ? "south" : "north") + " props=" + n);
        }

        // -- floor ------------------------------------------------------------

        private static void TileApproachRoad(Transform zone, string token, float radius, float gateWidth)
        {
            // The owned continuous dirt ground supplies the camp surface. The atlas-flat
            // dirt tile hides that texture; independently authored scatter remains intact.
            if (token == "floor_dirt_large") return;
            var model = LoadVisual(token);
            if (model == null) { WarnMissing(token); return; }
            float span = MeasureLongest(model);
            if (span < 1f) span = 4f;
            float y = 0.04f;
            // Cover the south FACE, not a 12 m ribbon. Owner Seeker 2026-09-09 09:54
            // (proof/seeker-gate-empty-20260909.png): the gate sat in a grey hole
            // because the road was narrower than the opening + flanks.
            float half = Mathf.Max(14f, gateWidth * 0.5f + 8f);
            for (float z = -(radius + 10f); z <= -(radius - 4f) + 0.01f; z += span)
            {
                for (float x = -half; x <= half + 0.01f; x += span)
                    InstantiateVisual(model, zone, "Floor", new Vector3(x, y, z), Quaternion.identity, true);
            }
        }

        private static void TileCourtyardRing(Transform zone, string token, LayoutContext ctx)
        {
            if (token == "floor_dirt_large") return;
            var model = LoadVisual(token);
            if (model == null) { WarnMissing(token); return; }
            float span = MeasureLongest(model);
            if (span < 2.2f) span = 2.2f;
            float outer = ctx.Radius - 1.1f;
            // A ring of tiles left a brown hole in the middle (owner 2026-09-09
            // raid frame). Fill the disk so the pad is a camp floor, not a plane.
            float expected = ((2f * outer) / span) + 1f;
            if (expected * expected > 420f)
                span = (2f * outer) / (Mathf.Sqrt(420f) - 1f);
            float y = 0.04f;
            float outerSq = outer * outer;
            for (float x = -outer; x <= outer + 0.01f; x += span)
            {
                for (float z = -outer; z <= outer + 0.01f; z += span)
                {
                    if (x * x + z * z > outerSq) continue;
                    var pos = new Vector3(x, y, z);
                    var tile = InstantiateVisual(model, zone, "Floor", pos, Quaternion.identity, true);
                    if (tile != null)
                    {
                        // The instance budget changes grid spacing. Fit actual XZ bounds
                        // to that cell while preserving the authored thickness and art.
                        if (!VisualBounds(tile, out Bounds bounds) || bounds.size.x <= 0f || bounds.size.z <= 0f)
                            throw new System.InvalidOperationException("Floor tile has no measurable XZ footprint");
                        Vector3 scale = tile.transform.localScale;
                        tile.transform.localScale = new Vector3(scale.x * span / bounds.size.x,
                            scale.y, scale.z * span / bounds.size.z);
                        VisualBounds(tile, out bounds);
                        tile.transform.position += new Vector3(pos.x - bounds.center.x, 0f, pos.z - bounds.center.z);
                    }
                }
            }
        }

        // -- combat art reskin ------------------------------------------------

        private static void ReskinCombatArt(Transform root, string towerTok, SceneConfigDef def)
        {
            var towerModel = LoadVisual(towerTok);
            if (towerModel == null) towerModel = LoadVisual("Tower_Wooden_Watchtower");
            if (towerModel == null) towerModel = LoadVisual("Tower_Medieval_Wood");

            // WO-1817 context for the clad-pose trace: the height of the kit's OWN wall art, measured
            // once off the prefab (MeasureTallest), so one log read answers "is this tower the right
            // height for its wall". Informational ONLY - the muzzle-vs-collider GATE is
            // RaidWallColliderAuditRegression's `[raid-wall-audit] minMuzzleMargin`, and re-deriving
            // that here would be exactly the duplicated state CLAUDE.md s.2/s.5/s.16 each describe.
            float wallH = 0f;
            Guard.Try(Sys, "measure wall art height for the clad-pose trace", () =>
            {
                var wallModel = LoadVisual(ResolveCladModule(OuterWallToken(def, KitOf(def))));
                if (wallModel != null) wallH = MeasureTallest(wallModel);
            });

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.name == null) continue;
                if (t.name.StartsWith("Watchtower_") || t.name.StartsWith("CornerPost_"))
                    ReplaceChildrenWith(t.gameObject, towerModel, t.name.StartsWith("Watchtower_"), wallH);
                if (t.name == RaidSpireName())
                {
                    string spireTok = def != null ? def.centralBuilding : null;
                    var spireModel = LoadVisual(MapCatalogArt(spireTok));
                    if (spireModel == null) spireModel = LoadVisual("ArcaneSpire_1");
                    ReplaceChildrenWith(t.gameObject, spireModel, keepComponents: true, wallH: wallH);
                }
            }
        }

        private static string RaidSpireName()
        {
            return "RaidSpire";
        }

        /// <summary>
        /// Map a structures-catalog id to the art token for the SPIRE SLOT.
        ///
        /// ⚠ THIS HAS EXACTLY ONE CALLER AND IT IS THE SPIRE (ReskinCombatArt, above). WO-1617's
        /// premise that the siege branches served "the turret slots" was checked and is FALSE -
        /// the turrets go through RaidBaseGenerator.PlaceTowerProp, which never comes here. So a
        /// `siege -> Ballista` row could only ever hand siege art to the camp's architectural
        /// centrepiece, which is precisely the defect: the Easy camp rendered a Ballista as its
        /// spire ('Structures/Ballista' in Builds/raidbase-bake.log).
        ///
        /// The fix routes the id through the ONE decider that owns "is this a siege machine?" -
        /// RaidBaseGenerator.ResolveSpireArtId - BEFORE the token mapping. That keeps the model
        /// the generator MEASURED for its height fit and the model this dresser INSTANTIATES in
        /// agreement (ReplaceChildrenWith inherits the host's fitted localScale), and it means
        /// the two ids live in one place instead of being restated here.
        ///
        /// The siege/catapult rows below are consequently UNREACHABLE for today's only caller.
        /// They are kept, not deleted, so that a future turret-side caller gets a correct answer
        /// rather than falling through to the raw id - but they are no longer the spire's answer.
        /// </summary>
        private static string MapCatalogArt(string catalogId)
        {
            if (string.IsNullOrEmpty(catalogId)) return "ArcaneSpire_1";
            catalogId = RaidBaseGenerator.ResolveSpireArtId(catalogId);

            // CATALOG FIRST (WO-1619, 2026-09-10). The substring table below answered exactly
            // four ids; anything else fell through to the RAW id, which LoadVisual could not
            // resolve, so the caller's null-fallback quietly reskinned the spire back to
            // ArcaneSpire_1 - the dresser undoing the model the generator had just measured and
            // fitted. That is what happened to the owner's ruled Forsaken Camp art
            // ('tower_ruined_watchtower'). The catalog already answers this question for the
            // generator (PlaceSpire reads entry.visualPrefabPath); asking it here too means the
            // two halves cannot disagree and a new spire id needs NO edit in this file.
            //
            // Behaviour for every id that was already live is UNCHANGED, by construction:
            // tower_arcane_spire authors "Structures/ArcaneSpire_1" and tower_ground_archer
            // authors "Structures/Tower_Wooden_Watchtower" - the same two tokens the branches
            // below return - and the siege/catapult ids never reach here because
            // ResolveSpireArtId substitutes them one line above. LoadVisual strips the
            // "Structures/" prefix itself.
            string catalogPath = RaidBaseGenerator.CatalogArtPath(catalogId);
            if (!string.IsNullOrEmpty(catalogPath)) return catalogPath;

            if (catalogId.IndexOf("siege", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Ballista";
            if (catalogId.IndexOf("catapult", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Catapult";
            if (catalogId.IndexOf("arcane", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "ArcaneSpire_1";
            if (catalogId.IndexOf("archer", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Tower_Wooden_Watchtower";
            return catalogId;
        }

        /// <summary>
        /// Re-clad <paramref name="host"/>: drop its children, hang ONE "/Visual" child built from
        /// <paramref name="model"/>, and reseat.
        ///
        /// ⛔ THE NAME LIED AND IT COST THE OWNER A RAID (WO-1807, 2026-09-16). It destroyed
        /// CHILDREN only — and the three things it clads are prefab instances whose mesh sits on
        /// the ROOT, not on a child:
        ///   * Assets/StructureContent/Tower_Medieval_Wood.prefab is ONE GameObject carrying
        ///     MeshFilter + MeshRenderer + MeshCollider and `m_Children: []` — that is the corner
        ///     posts (RaidBaseGenerator.PlaceCornerTower, via FallbackTowerPath).
        ///   * RaidBaseGenerator.PlaceTowerProp instantiates the turret art the same way and then
        ///     ScaleToHeight's the ROOT — that is Watchtower_*.
        ///   * PlaceSpire does likewise for RaidSpire.
        /// So "replace the children" replaced NOTHING and the clad was STACKED on top of the
        /// original: two whole towers in one spot. Proven on the owner's 19:55 Forsaken Camp run
        /// (build 2026.09.17.372984): `[Flow:Raid] [wo1639-marker] OVERSIZED world renderer:
        /// path=RaidBase_fortified_garrison/CornerPost_Outer_E kind=MeshRenderer screenH=1608px
        /// (1.34 of screen) camDist=9.1m mat=M_10_Brown_Dark_LPUP (URP/Lit)` — the polyperfect
        /// HOST mesh — beside `[Flow:RaidArt] path='.../CornerPost_Outer_E/Visual'
        /// mesh='SM_Bld_Castle_Wall_Tower_M_01' bounds=4.3x7.5x4.3m` — the Synty clad. The baked
        /// YAML confirms `m_RemovedComponents: []` on every one of those prefab instances.
        ///
        /// ⚠ WHY THE OBVIOUS THEORY IS WRONG, so nobody re-opens it: the clad is NOT pitched. In
        /// the baked scene CornerPost_Outer_E's host rotation is pure-Y (w=-0.38268, y=0.92388 =
        /// 225°) and its /Visual child is IDENTITY (w=1) — the -90 X wall-panel correction reaches
        /// wall panels, never these hosts. Both halves stand up straight; the defect is that there
        /// are TWO of them, mismatched in size and material, which reads as an upside-down or
        /// inverted tower from the ground.
        ///
        /// Corollary the bake log shows: with the host renderer alive, <see cref="SeatOnGround"/>
        /// seated the UNION, so the host was lifted to y=2.5 to get the polyperfect mesh's belly
        /// off the floor. Removing the root renderer lets the clad seat itself, and that lift
        /// correctly disappears.
        ///
        /// ⚠ WO-1817 CORRECTION TO THAT LAST SENTENCE: it did NOT disappear. The post-fix audit
        /// (Builds/raid-post-audit-after2.log, 2026-09-16 20:59) still reads `pos=(43.89,2.5,6.95)`
        /// on every garrison watchtower, because `SeatOnGround(host)` seated the host to suit the
        /// CLAD's pivot, which on the Synty tower sits 2.5 m above its base. The host moved, and the
        /// host is where the muzzle is measured from. This method now FITS the clad
        /// (<see cref="FitCladToCadence"/>) and SEATS the clad (<see cref="SeatCladLocally"/>),
        /// leaving the host transform to the generator alone.
        /// </summary>
        private static void ReplaceChildrenWith(GameObject host, GameObject model, bool keepComponents,
                                                float wallH)
        {
            if (host == null || model == null) return;
            var doomed = new List<GameObject>();
            for (int i = 0; i < host.transform.childCount; i++)
            {
                var c = host.transform.GetChild(i);
                if (c != null) doomed.Add(c.gameObject);
            }
            for (int i = 0; i < doomed.Count; i++) Object.DestroyImmediate(doomed[i]);

            var vis = InstantiateVisual(model, host.transform, "Visual", host.transform.position,
                                        host.transform.rotation, stripColliders: !keepComponents);
            if (vis == null) return;                 // clad failed: leave the host's own art alone
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;

            // WO-1817 - FIT, then SEAT, then strip. ORDER IS LOAD-BEARING TWICE OVER:
            //   * the fit runs BEFORE StripRootArt so CladLocalBox measures the FINAL size and the
            //     BoxCollider that replaces the host's MeshCollider matches what the player sees;
            //   * the root art comes off only AFTER the clad exists, so a missing/unloadable model
            //     can never leave a post with no renderer at all (the WO-1807 rule, unchanged).
            float fitH = FitCladToCadence(host, vis);
            SeatCladLocally(vis);

            StripRootArt(host, vis);

            ReportCladPose(host, vis, fitH, wallH);
        }

        /// <summary>
        /// WO-1817 - scale the clad so the thing the player SEES stands at the host's authored turret
        /// cadence height. Returns that target, or 0 when this post has none (see below).
        ///
        /// ⛔ THE HOST IS FITTED AND THE CLAD IS WHAT RENDERS, AND UNTIL NOW NOTHING CONNECTED THEM.
        /// `RaidBaseGenerator.PlaceTowerProp` ScaleToHeight's the HOST to
        /// <c>YHeightVariable * repo.heightMul</c> (4.80 m for the archer/arcane family) and WO-1807
        /// then strips the host's renderer, so the fitted model is not even on screen. The clad hangs
        /// off the host with `SetParent(parent, false)`, so it renders at
        /// <c>hostLossyScale * cladNativeHeight</c> - the accidental product of two unrelated models.
        /// Measured in the four baked scenes, 2026-09-16 (Builds/raid-post-audit-after2.log, and the
        /// arithmetic closes to the centimetre against the 0.0479 host scale in
        /// Builds/raid-rebake-1807.log):
        ///     RaidBase_IronBastion        0.05 m  (0.0479 x 1.11 native)
        ///     RaidBase_mage_enclave       0.86 m  (0.0479 x 17.96)
        ///     RaidBase_fortified_garrison 0.36 m fitted / 7.52 m on the six unfitted catapult hosts
        ///     RaidBase_raider_camp_small  1.11 m  (unfitted catapult hosts, host scale 1)
        /// Not one of them is 4.80 m, and three of four scenes ship SUB-METRE watchtowers.
        ///
        /// THE TARGET COMES FROM <see cref="RaidBaseGenerator.TurretCadenceHeight"/> - the generator's
        /// own expression, hoisted rather than copied - keyed by the catalog id that
        /// `RaidBaseGenerator.ArmTower` already stamped onto this host's DefenseTower. So the height
        /// the generator INTENDED and the height the dresser RENDERS cannot disagree.
        ///
        /// ⚠ NOT ScaleToHeight: that helper clamps its factor to 0.125-8x (its own log prints
        /// `saturatedAt=UPPER`), and Iron Bastion needs ~x90 to lift a 0.05 m clad to 4.80 m. The
        /// factor is computed directly here, and the wanted-vs-applied pair is printed by
        /// <see cref="ReportCladPose"/> so a future saturation is visible rather than silent.
        ///
        /// SCOPE - returns 0 and changes nothing for:
        ///   * CornerPost_* / RaidSpire, which carry no DefenseTower and so no authored cadence. The
        ///     corner posts consequently still render at clad-native size (1.11 / 7.52 / 17.96 m
        ///     across the kits). That is a REAL defect and it is deliberately NOT fixed here - it
        ///     needs an authored target, which is an owner ruling, not a lane's pick (WO-1817 s.4).
        ///   * a clad with no measurable bounds, or a non-finite target.
        ///
        /// A siege-machine host is NOT exempted here, and that is deliberate. The exemption in
        /// PlaceTowerProp protects authored siege ART from being stretched to monument height; by the
        /// time this runs the dresser has already REPLACED that art with the kit's tower, so the
        /// premise is gone and what remains is a tower that must stand at a tower's height. (That the
        /// dresser overrides authored siege art on ten posts at all is its own defect - named, not
        /// fixed, WO-1817 s.4 item 1.)
        /// </summary>
        private static float FitCladToCadence(GameObject host, GameObject clad)
        {
            float target = AuthoredHeightFor(host);
            if (!(target > 0.01f) || float.IsNaN(target) || float.IsInfinity(target)) return 0f;

            float current = CladHeight(clad);
            if (!(current > 0.0001f)) return 0f;

            float factor = target / current;
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f) return 0f;

            var s = clad.transform.localScale;
            clad.transform.localScale = new Vector3(s.x * factor, s.y * factor, s.z * factor);
            return target;
        }

        /// <summary>
        /// The authored height this host's art is supposed to render at, or 0 when it has none.
        /// TWO kinds of post answer, and each answers from the value its own system already trusts:
        ///
        ///   * <c>Watchtower_*</c> (WO-1817) - the turret cadence for the catalog id
        ///     <c>RaidBaseGenerator.ArmTower</c> stamped on its DefenseTower.
        ///   * <c>RaidSpire</c> (WO-1820) - <see cref="RaidSpire.VisualHeight"/>, the height
        ///     <c>PlaceSpire</c> FITTED THE HOST TO and then recorded via <c>Configure</c>. It is not
        ///     recomputed here: the same number already sizes the spire's hero-contact collider
        ///     (<c>RaidSpire.EnsureHittable</c>), so reading it makes the art agree with the collider
        ///     by construction instead of by a second copy of the monument clamp.
        ///
        /// ⛔ WHY THE SPIRE NEEDED THIS AT ALL, AND WHY THE ONE WORKING SCENE PROVED NOTHING.
        /// `ReplaceChildrenWith` parents the clad with `SetParent(parent, false)`, so the clad keeps
        /// its OWN prefab localScale AND inherits the host's - the prefab's authored scale is applied
        /// TWICE. Measured 2026-09-17 (Builds/wo1817-rebake.log, and identical in
        /// Builds/raid-rebake-1807.log, so it long predates that ticket):
        ///     raider_camp_small  host 9.600          x clad(1.000 x 1.500) = 14.40 m  correct
        ///     the other three    host 0.010 x 14.366 x clad(0.010 x 100.2) =  0.14 m  1/100 scale
        /// Every one of the four hosts achieved 14.40 m - the FIT was never the bug. The error factor
        /// is exactly `prefabScaleBefore`, so the camp is right ONLY because its art authors
        /// localScale 1.000 and squaring 1.000 is harmless. Treating that scene as the working case
        /// to copy would have hidden the rule.
        ///
        /// CornerPost_* answers 0: it carries neither component and has no authored target at all
        /// (WO-1817 s.4 item 2). It keeps rendering at clad-native size until that is ruled.
        /// </summary>
        private static float AuthoredHeightFor(GameObject host)
        {
            var tower = host.GetComponent<DefenseTower>();
            if (tower != null && !string.IsNullOrEmpty(tower.CatalogId))
                return RaidBaseGenerator.TurretCadenceHeight(tower.CatalogId);

            var spire = host.GetComponent<RaidSpire>();
            if (spire != null) return spire.VisualHeight;

            return 0f;
        }

        /// <summary>
        /// WO-1817 - drop the CLAD so its lowest rendered point sits on y=0, leaving the HOST
        /// transform exactly where <see cref="RaidBaseGenerator"/> put it.
        ///
        /// ⛔ THIS REPLACES A `SeatOnGround(host)` CALL, AND THE DIFFERENCE IS THE MUZZLE.
        /// `DefenseTower` fires from <c>transform.position + Vector3.up * 2f</c> (DefenseTower.cs:977,
        /// matched by Fire / FireAtParty), so the HOST TRANSFORM decides where the turret shoots from.
        /// Seating the host to suit the art therefore let an art pivot move a combat value: the Synty
        /// clad's pivot sits 2.5 m above its base, so seating lifted RaidBase_fortified_garrison's
        /// hosts to y=2.5, put their muzzles at 4.5 m under a 5.00 m wall, and left
        /// `[raid-wall-audit] minMuzzleMargin=0.50` - the tightest in the game, set by nobody's
        /// decision. The other three scenes seated at y=0 and read 2.00 m, so the same code produced
        /// two different combat geometries purely from which art pack a kit names.
        ///
        /// Moving the CLAD instead keeps presentation out of the object (ARCHITECTURE_PRINCIPLES) and
        /// makes the muzzle a pure function of the generator's placement. It is also what makes the
        /// fit above safe: a scaled pivot offset would otherwise drag the muzzle with the art.
        /// </summary>
        private static void SeatCladLocally(GameObject clad)
        {
            var rends = clad.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                if (rends[i] != null) b.Encapsulate(rends[i].bounds);
            clad.transform.position += new Vector3(0f, -b.min.y, 0f);
        }

        /// <summary>World-space rendered height of a clad, 0 when it has no renderers.</summary>
        private static float CladHeight(GameObject clad)
        {
            if (clad == null) return 0f;
            var rends = clad.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return 0f;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                if (rends[i] != null) b.Encapsulate(rends[i].bounds);
            return b.size.y;
        }

        /// <summary>
        /// Remove the HOST ROOT's own mesh art (WO-1807) now that <paramref name="clad"/> renders
        /// the post. A root MeshCollider that referenced the very mesh being removed would become
        /// an invisible polyperfect-shaped blocker around a Synty tower, so it is replaced by a
        /// BoxCollider fitted to the clad — the blocker then matches what the player can see, and
        /// the corner stays solid (it is the ONLY collider on a CornerPost_, whose clad is
        /// instantiated with stripColliders: true).
        /// </summary>
        private static void StripRootArt(GameObject host, GameObject clad)
        {
            var rootRend = host.GetComponent<Renderer>();
            if (rootRend == null) return;            // nothing stacked; already clean

            var mf = host.GetComponent<MeshFilter>();
            var doomedMesh = mf != null ? mf.sharedMesh : null;
            string meshName = doomedMesh != null ? doomedMesh.name : "?";
            string matName = rootRend.sharedMaterial != null ? rootRend.sharedMaterial.name : "?";

            // Swap a mesh-collider-of-the-doomed-mesh for a box around the clad BEFORE destroying
            // the renderer, so the clad bounds are measured while nothing has moved.
            var rootMeshCol = host.GetComponent<MeshCollider>();
            string colNote = "root collider untouched";
            if (rootMeshCol != null && (doomedMesh == null || rootMeshCol.sharedMesh == doomedMesh))
            {
                if (CladLocalBox(host, clad, out Vector3 localCentre, out Vector3 localSize))
                {
                    // Guarded for the same reason the renderer strip below is: a refusal here must
                    // never propagate out and abort the whole bake over one post's collider.
                    try
                    {
                        Object.DestroyImmediate(rootMeshCol);
                        var box = host.GetComponent<BoxCollider>();
                        if (box == null) box = host.AddComponent<BoxCollider>();
                        box.center = localCentre;
                        box.size = localSize;
                        colNote = $"root MeshCollider -> BoxCollider fitted to the clad " +
                                  $"(size {localSize.x:0.##}x{localSize.y:0.##}x{localSize.z:0.##} local)";
                    }
                    catch (System.Exception ex)
                    {
                        colNote = $"root MeshCollider KEPT - the swap threw {ex.GetType().Name}: {ex.Message}";
                        FlowTrace.Warn(Sys, $"[wo1807] '{host.name}': {colNote}");
                    }
                }
                else
                {
                    colNote = "root MeshCollider KEPT - the clad had no measurable bounds to fit a box to";
                }
            }

            // DestroyImmediate on a prefab-instance component is recorded as an m_RemovedComponents
            // override, which is what we want in the baked scene. But if this editor ever refuses
            // it, DISABLING the renderer must still happen - a silent throw here would leave the
            // stacked tower in place and the bake would report success. So: try to remove, and on
            // any refusal fall back to `enabled = false`, which is the property
            // RaidPostOrientationRegression's pin 1 actually tests.
            bool removed = true;
            try
            {
                Object.DestroyImmediate(rootRend);
                if (mf != null) Object.DestroyImmediate(mf);
            }
            catch (System.Exception ex)
            {
                removed = false;
                if (rootRend != null) rootRend.enabled = false;
                FlowTrace.Warn(Sys, $"[wo1807] '{host.name}': could not DESTROY the root mesh " +
                                    $"({ex.GetType().Name}: {ex.Message}) - it is DISABLED instead, so it " +
                                    "no longer renders, but the components remain in the baked scene.");
            }

            FlowTrace.Step(Sys, $"[wo1807] '{host.name}': {(removed ? "stripped" : "disabled")} the STACKED root mesh " +
                                $"'{meshName}' (mat '{matName}') - the clad '{clad.name}' is now the " +
                                $"only art on this post; {colNote}.");
        }

        /// <summary>
        /// The clad's renderer bounds expressed in <paramref name="host"/>'s local space, for a
        /// BoxCollider. Host rotation on these posts is yaw-only (LookRotation with Vector3.up),
        /// so an axis-aligned box in local space is a faithful blocker.
        /// </summary>
        private static bool CladLocalBox(GameObject host, GameObject clad, out Vector3 centre, out Vector3 size)
        {
            centre = Vector3.zero;
            size = Vector3.zero;
            var rends = clad.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return false;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                if (rends[i] != null) b.Encapsulate(rends[i].bounds);
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f || b.size.z <= 0.0001f) return false;

            centre = host.transform.InverseTransformPoint(b.center);
            var s = host.transform.lossyScale;
            size = new Vector3(b.size.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                               b.size.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                               b.size.z / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
            return true;
        }

        /// <summary>
        /// WO-1807 acceptance instrument: print the post's FINAL world euler and the clad's
        /// bounds in Y. One read of a bake log now answers "is any corner post inverted or
        /// floating" without a device build, a screenshot or an owner playtest.
        ///
        /// WO-1817 extends it with the four numbers that answer "is this tower the right height":
        ///   fitH   - the authored turret cadence target (0 = this post has none; see
        ///            <see cref="FitCladToCadence"/> for which posts those are and why)
        ///   cladH  - what the clad ACTUALLY renders, after the fit. fitH vs cladH is the pin
        ///            RaidPostAudit and RaidPostOrientationRegression both assert at 10%.
        ///   hostY  - the host transform's y, which is where the muzzle is measured from
        ///            (DefenseTower fires at position + up*2). It must now be the generator's
        ///            value, NOT an art pivot's - see <see cref="SeatCladLocally"/>.
        ///   wallH  - the kit's own wall ART height, for context only. The muzzle-vs-wall GATE
        ///            stays RaidWallColliderAuditRegression's `[raid-wall-audit] minMuzzleMargin`.
        /// </summary>
        private static void ReportCladPose(GameObject host, GameObject clad, float fitH, float wallH)
        {
            var rends = clad.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0)
            {
                FlowTrace.Warn(Sys, $"[wo1807] '{host.name}': clad has NO renderer - nothing to measure.");
                return;
            }
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                if (rends[i] != null) b.Encapsulate(rends[i].bounds);

            var e = host.transform.rotation.eulerAngles;
            float upDot = Vector3.Dot(clad.transform.up, Vector3.up);
            float widest = Mathf.Max(b.size.x, b.size.z);
            float ratio = widest <= 0.0001f ? 0f : b.size.y / widest;

            // Built into locals first: a nested quote inside an interpolation hole reads as a string
            // terminator to CompileGate.BraceBalanced (CLAUDE.md s.1).
            string rootNote = host.GetComponent<Renderer>() != null ? "STILL PRESENT" : "none";
            string fitNote = fitH > 0.01f
                ? $"fitH={fitH:0.##} cladH={b.size.y:0.##} delta={(b.size.y - fitH):+0.##;-0.##} " +
                  $"({(Mathf.Abs(b.size.y - fitH) / fitH):P1} off)"
                : $"fitH=none cladH={b.size.y:0.##} (no authored cadence on this post - WO-1817 s.4)";
            string wallNote = wallH > 0.01f ? wallH.ToString("0.##") : "unknown";

            FlowTrace.Step(Sys, $"[wo1807] pose '{host.name}': hostEuler=({e.x:0.#},{e.y:0.#},{e.z:0.#}) " +
                                $"cladUpDot={upDot:0.###} boundsY=[{b.min.y:0.##}..{b.max.y:0.##}] " +
                                $"size={b.size.x:0.##}x{b.size.y:0.##}x{b.size.z:0.##} ratio={ratio:0.##} " +
                                $"rootRenderer={rootNote} " +
                                $"[wo1817] {fitNote} hostY={host.transform.position.y:0.##} " +
                                $"muzzleY={(host.transform.position.y + 2f):0.##} wallH={wallNote}");
        }

        // -- props ------------------------------------------------------------

        private static void ScatterProps(SceneConfigDef def, string kit, Transform approach,
                                         Transform gatehouse, Transform courtyard, Transform choke,
                                         Transform keep, LayoutContext ctx, PropKeepout keepout)
        {
            var list = new List<RaidDressPropDef>();
            if (def.raidDress != null && def.raidDress.props != null)
            {
                for (int i = 0; i < def.raidDress.props.Count; i++)
                    if (def.raidDress.props[i] != null) list.Add(def.raidDress.props[i]);
            }
            // WO-1635 acceptance #1 + #2 (2026-09-10): `raidDress.props` in scene-configs.json is
            // the ONE authority for raid props. The two legacy readers that used to sit here are
            // RETIRED, not merely disabled:
            //   * the `props.set` block (a List<string>, one instance each, ALWAYS zone Courtyard)
            //   * a kit-keyed emergency set hardcoded in C# (deleted; see the note where it stood,
            //     immediately above ZoneOf)
            // Both were silent third copies of authored content. Each fired only when the authored
            // array came back empty, so a config mid-edit dressed itself from DRIFTED content and
            // read as authored in the bake log - the same duplicated-state trap CLAUDE.md sections
            // 2, 5, 8 and 16 each describe. An unauthored raid config must now be LOUD and empty;
            // RaidBaseLayoutRegression.CaseSinglePropAuthority reds on it, and CaseOnePropReader
            // reds if either legacy reader is ever restored here.
            if (list.Count == 0)
            {
                FlowTrace.Warn(Sys, "props '" + def.id + "' kit=" + kit +
                               " authors NO raidDress.props - this courtyard dresses EMPTY. " +
                               "scene-configs.json is the only prop authority (WO-1635).");
            }

            int seed = StableHash(def.id);
            // Seeded, so a re-bake reproduces the identical courtyard (the arena's contract,
            // ProceduralSiegeArenaBuilder.cs:89 "Deterministic jitter so a rebuild reproduces
            // the same venue layout").
            var rng = new System.Random(seed);

            // The courtyard BAND: everything between the inner boundary (keep ring, or a clear
            // ring around the spire when there is no keep) and the wall line, minus the pads.
            float inner = ctx.InnerLayers > 0
                ? ctx.Innermost + CourtyardInnerPad
                : CourtyardSpirePad;
            float outer = Mathf.Max(inner + 4f, ctx.Radius - CourtyardWallPad);
            int rings = CoverRingPlacer.RingCount(inner, outer, CourtyardRingSpacing, CourtyardMaxRings);

            int placedProps = 0;
            int courtyardProps = 0;
            var tokens = new List<string>();

            // Cluster anchors must be spread over the COURTYARD entries, not over the whole prop
            // list. Indexing by the full list leaves whole quadrants bare - on the authored rows
            // that put every Extreme cluster on the east side and left Easy empty from 145 to 323
            // degrees, which is the same "empty dirt" this ticket exists to remove.
            int courtyardCount = 0;
            for (int i = 0; i < list.Count; i++)
                if (ZoneOf(list[i].zone, approach, gatehouse, courtyard, choke, keep) == courtyard)
                    courtyardCount++;
            int courtyardIndex = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                int n = Mathf.Clamp(p.count, 1, 16);
                var zone = ZoneOf(p.zone, approach, gatehouse, courtyard, choke, keep);
                var model = LoadVisual(p.token);
                if (model == null)
                {
                    // Keep the courtyard cursor moving even when the art is missing, so a missing
                    // pack does not bunch every surviving cluster into one arc.
                    if (zone == courtyard) courtyardIndex++;
                    WarnMissing(p.token);
                    continue;
                }

                int got;
                if (zone == courtyard)
                {
                    got = PlaceCourtyardCluster(model, zone, p, n, rng, inner, outer, rings,
                                                courtyardIndex, courtyardCount, keepout);
                    courtyardIndex++;
                    courtyardProps += got;
                }
                else
                {
                    got = PlaceZoneProps(model, zone, p, n, ctx, seed, i, keepout);
                }

                placedProps += got;
                if (got > 0) tokens.Add(p.token + "x" + got + (p.cover ? "*" : string.Empty));
            }

            // WO-1633 §2.4: the aggregate `dressed ... placed=N` line counts clad panels, floor
            // tiles, the gatehouse and the gate mouth too, so no bake log has ever stated a PROPS
            // count. This is that line. '*' marks a cover-class prop (colliders kept).
            Debug.Log($"[RaidBaseDresser] props '{def.id}': {placedProps} placed " +
                      $"(set={string.Join(", ", tokens)})");
            FlowTrace.Step(Sys,
                $"props '{def.id}' total={placedProps} courtyard={courtyardProps} " +
                $"rings={rings} band={inner:F1}-{outer:F1}m laneHalf={keepout.LaneHalf:F1}m " +
                $"turretsAvoided={(keepout.Turrets != null ? keepout.Turrets.Count : 0)}");
        }

        /// <summary>
        /// Gather every no-go region from the tree the generator just built. Nothing here is a
        /// hardcoded radius: turrets come from their <see cref="DefenseTower"/> components and
        /// staging from the marker's own transform, because both move per config.
        /// </summary>
        private static PropKeepout BuildKeepout(Transform root, LayoutContext ctx)
        {
            var k = new PropKeepout
            {
                // The gate mouths and the march they feed are all on the x ~ 0 axis (south gate at
                // -Radius, north gate at +Radius, inner keep gate north), so ONE corridor test
                // covers every lane. Width tracks the gate the player walks through, and clears
                // the WO-1609:110 (>= 4 m) / WO-1610:114 (>= 5 m) floors by a margin.
                LaneHalf = Mathf.Max(2.5f, ctx.GateWidth * 0.5f) + 1.0f,
                SpireClear = CourtyardSpirePad * 0.66f,
                Turrets = new List<Vector3>(),
                Staging = Vector3.zero,
                HasStaging = false,
            };

            Guard.Try(Sys, "keepout scan", () =>
            {
                var towers = root.GetComponentsInChildren<DefenseTower>(true);
                for (int i = 0; i < towers.Length; i++)
                    if (towers[i] != null) k.Turrets.Add(towers[i].transform.position);

                var staging = root.Find(StagingMarkerName);
                if (staging != null)
                {
                    k.Staging = staging.position;
                    k.HasStaging = true;
                }
            });

            return k;
        }

        /// <summary>True when a candidate slot is clear of the assault lane, the spire, every
        /// turret footprint and the staging pocket.</summary>
        private static bool AcceptPropSlot(Vector3 pos, PropKeepout k)
        {
            if (Mathf.Abs(pos.x) < k.LaneHalf) return false;
            if (new Vector2(pos.x, pos.z).magnitude < k.SpireClear) return false;

            if (k.Turrets != null)
            {
                for (int i = 0; i < k.Turrets.Count; i++)
                {
                    float dx = k.Turrets[i].x - pos.x;
                    float dz = k.Turrets[i].z - pos.z;
                    if (dx * dx + dz * dz < TurretClearPad * TurretClearPad) return false;
                }
            }

            if (k.HasStaging)
            {
                float sx = k.Staging.x - pos.x;
                float sz = k.Staging.z - pos.z;
                if (sx * sx + sz * sz < StagingClearPad * StagingClearPad) return false;
            }

            return true;
        }

        /// <summary>
        /// A cluster anchor that lands in the assault corridor is PUSHED to the corridor edge,
        /// not dropped. WO-1610:111 wants cover "along the SIDES of the south->north march, not
        /// on it", and WO-1610:46 wants the courtyard to be "a fight, not a runway" - dropping
        /// the cluster would satisfy the first and break the second.
        /// </summary>
        private static Vector3 PushOutOfLane(Vector3 pos, PropKeepout k)
        {
            if (Mathf.Abs(pos.x) >= k.LaneHalf) return pos;
            float side = pos.x >= 0f ? 1f : -1f;
            pos.x = side * (k.LaneHalf + 0.6f);
            return pos;
        }

        /// <summary>
        /// Place one authored entry as a CLUSTER on one of the concentric courtyard cover rings,
        /// using the arena's jitter + scale vocabulary through <see cref="CoverRingPlacer"/>.
        /// </summary>
        private static int PlaceCourtyardCluster(GameObject model, Transform zone, RaidDressPropDef p,
                                                 int n, System.Random rng, float inner, float outer,
                                                 int rings, int entryIndex, int entryCount,
                                                 PropKeepout keepout)
        {
            float ringR = CoverRingPlacer.BandRadius(inner, outer, entryIndex % rings, rings);
            float anchorAng = CoverRingPlacer.ClusterAnchorAngle(rng, entryIndex, entryCount, 0.42f);
            var anchor = PushOutOfLane(CoverRingPlacer.PolarPoint(ringR, anchorAng), keepout);

            float spread = ClusterSpreadBase + 0.35f * n;
            int placed = 0;

            for (int k = 0; k < n; k++)
            {
                var slot = CoverRingPlacer.Jittered(rng, anchor, spread, PropScaleMin, PropScaleMax);
                slot.Position = PushOutOfLane(slot.Position, keepout);

                // Re-roll a few times before giving a slot up, so a cluster near a turret thins
                // rather than vanishes.
                for (int attempt = 0; attempt < 5 && !AcceptPropSlot(slot.Position, keepout); attempt++)
                {
                    slot = CoverRingPlacer.Jittered(rng, anchor, spread * 1.4f, PropScaleMin, PropScaleMax);
                    slot.Position = PushOutOfLane(slot.Position, keepout);
                }
                if (!AcceptPropSlot(slot.Position, keepout)) continue;

                var go = InstantiateVisual(model, zone, "Prop_" + p.token, slot.Position,
                                           slot.Rotation, stripColliders: !p.cover);
                if (go == null) continue;

                // Scale AFTER instantiate (which already seated it), then re-seat: a scaled prop
                // that is not re-seated floats or sinks by its own bounds delta.
                go.transform.localScale *= slot.Scale;
                SeatOnGround(go);
                // Traced AFTER the re-seat, so the logged world position is the one that ships.
                TraceProp(zone != null ? zone.name : "<no zone>", p, k, model, go);
                if (p.cover) EnsureCoverCollider(go);
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// Approach / Gatehouse / Choke / Keep keep their authored, deliberate placement
        /// (<see cref="PropSlot"/>) - a gatehouse flank prop is SUPPOSED to sit beside the gate,
        /// so the courtyard's lane push must not apply here. The staging pocket is still honoured,
        /// because Approach props are the only ones that reach out towards it.
        /// </summary>
        private static int PlaceZoneProps(GameObject model, Transform zone, RaidDressPropDef p, int n,
                                          LayoutContext ctx, int seed, int entryIndex, PropKeepout keepout)
        {
            int placed = 0;
            for (int k = 0; k < n; k++)
            {
                Vector3 pos = PropSlot(p.zone, ctx, seed + entryIndex * 17 + k * 31, k, n);
                if (IsSouthLane(pos, ctx)) pos.x += 6f * ((k & 1) == 0 ? 1f : -1f);

                if (keepout.HasStaging)
                {
                    float sx = keepout.Staging.x - pos.x;
                    float sz = keepout.Staging.z - pos.z;
                    if (sx * sx + sz * sz < StagingClearPad * StagingClearPad) continue;
                }

                var go = InstantiateVisual(model, zone, "Prop_" + p.token, pos,
                                           Quaternion.Euler(0f, (seed + k * 40) % 360, 0f),
                                           stripColliders: !p.cover);
                if (go == null) continue;
                TraceProp(zone != null ? zone.name : "<no zone>", p, k, model, go);
                if (p.cover) EnsureCoverCollider(go);
                placed++;
            }
            return placed;
        }

        // =====================================================================
        //  WO-1638 STEP 1 - THE PER-PLACED-PROP TRACE.
        //
        //  WO-1638 exists because the bake log could not answer a question the device frames
        //  raised: two green slabs stand on the south gatehouse line and NOTHING said what they
        //  are. The dresser logged a COUNT ("gate mouth south props=4", "props 'id' total=N") and
        //  a count is not an identification - the ticket's own sec.4 step 1 says so. Every
        //  candidate object in that frame is authored art placed exactly where the code says, so
        //  the only missing fact was WHICH object, and the log was silent on it.
        //
        //  One line per PLACED prop: the zone GameObject it landed under, the AUTHORED zone
        //  string from scene-configs.json (which can differ - ZoneOf falls through to Courtyard
        //  for an unknown or a Choke/Keep on a camp with no inner layers), the instance index,
        //  the art that resolved, and the WORLD position. World, not local, because the frames
        //  and every other measurement in these tickets are in world metres.
        //
        //  Bake-time only, editor-only, never a frame path. PERMANENT (CLAUDE.md sec.12).
        // =====================================================================
        private static void TraceProp(string zoneName, RaidDressPropDef p, int index,
                                      GameObject model, GameObject placed)
        {
            if (p == null || placed == null) return;

            Guard.Try(Sys, "prop trace " + p.token, () =>
            {
                var w = placed.transform.position;
                string authoredZone = string.IsNullOrEmpty(p.zone) ? "<unauthored>" : p.zone;
                string art = model != null ? model.name : "<null>";
                string artPath = model != null ? AssetDatabase.GetAssetPath(model) : "<null>";
                if (string.IsNullOrEmpty(artPath)) artPath = "<not an asset>";
                string pos = "(" + w.x.ToString("F3") + ", " + w.y.ToString("F3") + ", " +
                             w.z.ToString("F3") + ")";
                string cover = p.cover ? " cover=yes" : " cover=no";

                FlowTrace.Step(Sys, "PROP zone=" + zoneName + " authoredZone=" + authoredZone +
                               " i=" + index + " token='" + p.token + "' art='" + art +
                               "' asset='" + artPath + "' world=" + pos + " name='" +
                               placed.name + "'" + cover);
            });
        }

        /// <summary>
        /// A cover prop has to actually stop a body and an arrow. WO-1607 §6 "Cover stacks":
        /// "Troops can stand behind something ... with colliders"; WO-1609:104 "Colliders on";
        /// WO-1610:112 "collider stays". Keeping the prefab's own colliders is enough when it
        /// HAS any - many KayKit FBX do not, so fit an axis-aligned box to the renderer bounds.
        /// The box is a touch generous on a yawed mesh (an AABB of a rotated bound), which is the
        /// right way to err for cover.
        /// </summary>
        private static void EnsureCoverCollider(GameObject go)
        {
            if (go == null) return;
            var existing = go.GetComponentsInChildren<Collider>(true);
            if (existing != null && existing.Length > 0) return;

            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;

            Guard.Try(Sys, "cover collider " + go.name, () =>
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                var box = go.AddComponent<BoxCollider>();
                var lossy = go.transform.lossyScale;
                float sx = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
                float sy = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
                float sz = Mathf.Max(0.0001f, Mathf.Abs(lossy.z));

                box.center = go.transform.InverseTransformPoint(b.center);
                box.size = new Vector3(b.size.x / sx, b.size.y / sy, b.size.z / sz);
            });
        }

        // WO-1635 (2026-09-10): the kit-keyed emergency prop set that used to live here is DELETED.
        // It was reached only when a config authored no props at all, and it had already drifted
        // from the shipped JSON (its hexagon-green branch still handed out a banner/weaponrack set
        // that Easy's authored 10-entry array had moved past). A fallback that silently substitutes
        // stale content for authored content is not a safety net - it hides the empty row it was
        // meant to cover. ScatterProps now FlowTrace.Warns and dresses nothing; the empty row is
        // caught in the suite, not papered over at bake time. Do not reintroduce a C# prop set:
        // RaidBaseLayoutRegression.CaseOnePropReader reds on the identifier.

        private static Transform ZoneOf(string zone, Transform approach, Transform gatehouse,
                                        Transform courtyard, Transform choke, Transform keep)
        {
            if (string.Equals(zone, "Approach", System.StringComparison.OrdinalIgnoreCase)) return approach;
            if (string.Equals(zone, "Gatehouse", System.StringComparison.OrdinalIgnoreCase)) return gatehouse;
            if (string.Equals(zone, "Choke", System.StringComparison.OrdinalIgnoreCase) && choke != null) return choke;
            if (string.Equals(zone, "Keep", System.StringComparison.OrdinalIgnoreCase) && keep != null) return keep;
            return courtyard;
        }

        private static Vector3 PropSlot(string zone, LayoutContext ctx, int seed, int k, int n)
        {
            float ang = ((seed % 360) + k * (360f / Mathf.Max(1, n))) * Mathf.Deg2Rad;
            float r;
            if (string.Equals(zone, "Approach", System.StringComparison.OrdinalIgnoreCase))
                r = ctx.Radius + 4f + (k % 3);
            else if (string.Equals(zone, "Gatehouse", System.StringComparison.OrdinalIgnoreCase))
            {
                float flank = Mathf.Max(5f, ctx.GateWidth * 0.5f + 2.2f);
                return new Vector3((k % 2 == 0 ? -1f : 1f) * flank, 0f, -ctx.Radius + 2.5f);
            }
            else if (string.Equals(zone, "Keep", System.StringComparison.OrdinalIgnoreCase) && ctx.InnerLayers > 0)
                r = Mathf.Max(5f, ctx.Innermost * 0.55f);
            else if (string.Equals(zone, "Choke", System.StringComparison.OrdinalIgnoreCase) && ctx.InnerLayers > 0)
                r = Mathf.Max(6f, ctx.Innermost * 0.85f);
            else
                r = Mathf.Lerp(ctx.InnerLayers > 0 ? ctx.Innermost + 4f : 8f, ctx.Radius - 6f, 0.45f + (k % 3) * 0.15f);
            return new Vector3(Mathf.Sin(ang) * r, 0f, Mathf.Cos(ang) * r);
        }

        private static bool IsSouthLane(Vector3 pos, LayoutContext ctx)
        {
            return Mathf.Abs(pos.x) < 4f && pos.z < -4f;
        }

        // -- garrison slots ---------------------------------------------------

        private static void PlaceGarrisonSlots(Transform root, SceneConfigDef def, LayoutContext ctx)
        {
            int n = 8;
            if (def.garrison != null && def.garrison.composition != null)
            {
                n = 0;
                for (int i = 0; i < def.garrison.composition.Count; i++)
                    if (def.garrison.composition[i] != null)
                        n += Mathf.Max(0, def.garrison.composition[i].count);
            }
            n = Mathf.Clamp(n, 4, 28);

            int idx = 0;
            PlaceSlot(root, "GarrisonSlot_Gate_S_0", new Vector3(-3.2f, 0f, -ctx.Radius + 3f));
            PlaceSlot(root, "GarrisonSlot_Gate_S_1", new Vector3(3.2f, 0f, -ctx.Radius + 3f));
            idx = 2;
            if (ctx.TwoGates)
            {
                PlaceSlot(root, "GarrisonSlot_Gate_N_0", new Vector3(0f, 0f, ctx.Radius - 3f));
                idx = 3;
            }
            int keepSlots = ctx.InnerLayers > 0 ? Mathf.Min(4, n / 4) : 0;
            int yard = n - idx - keepSlots;
            for (int i = 0; i < yard; i++, idx++)
            {
                float ang = (i / (float)Mathf.Max(1, yard)) * Mathf.PI * 2f + 0.4f;
                float r = Mathf.Max(8f, ctx.Radius * 0.55f);
                var pos = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (IsSouthLane(pos, ctx)) pos.x += 5f;
                PlaceSlot(root, "GarrisonSlot_Yard_" + i, pos);
            }
            for (int i = 0; i < keepSlots; i++, idx++)
            {
                float ang = (i / (float)Mathf.Max(1, keepSlots)) * Mathf.PI * 2f;
                float r = Mathf.Max(4f, ctx.Innermost * 0.4f);
                PlaceSlot(root, "GarrisonSlot_Keep_" + i, new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r));
            }
        }

        private static void PlaceSlot(Transform root, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
        }

        // -- keep platform ----------------------------------------------------

        private static void RaiseKeep(Transform keep, string kit, LayoutContext ctx)
        {
            float half = Mathf.Max(6f, ctx.Innermost * 0.55f);
            float height = kit == "dungeon-stone" ? 0.8f : 1.5f;
            var plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plat.name = "KeepPlatform";
            plat.transform.SetParent(keep, false);
            plat.transform.localScale = new Vector3(half * 2f, height, half * 2f);
            plat.transform.position = new Vector3(0f, height * 0.5f, 0f);
            ApplyUrp(plat, kit == "dungeon-stone"
                ? new Color(0.28f, 0.26f, 0.32f)
                : new Color(0.38f, 0.34f, 0.30f));
            MagentaGuard.ProtectPrimitiveArt(plat, "RaidBaseDresser.KeepPlatform");

            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "KeepRamp";
            ramp.transform.SetParent(keep, false);
            const float thickness = 0.35f;
            // Preserve the original south top-face footprint, but connect its foot to
            // ground and its north landing to the platform. Positive X rotation made
            // the old ramp descend toward the platform, leaving an unwalkable step.
            Quaternion originalRotation = Quaternion.Euler(18f, 0f, 0f);
            Vector3 originalFoot = new Vector3(0f, height * 0.35f, -half - 1.5f) +
                originalRotation * new Vector3(0f, thickness * 0.5f, -half * 0.9f * 0.5f);
            Vector3 foot = new Vector3(0f, 0f, originalFoot.z);
            Vector3 landing = new Vector3(0f, height, -half + 0.25f);
            Vector3 rise = landing - foot;
            Quaternion slope = Quaternion.Euler(-Mathf.Atan2(rise.y, rise.z) * Mathf.Rad2Deg, 0f, 0f);
            ramp.transform.localScale = new Vector3(4.2f, thickness, rise.magnitude);
            ramp.transform.rotation = slope;
            ramp.transform.position = (foot + landing) * 0.5f - slope * Vector3.up * (thickness * 0.5f);
            FlowTrace.Step(Sys, $"keep ramp topFace foot={foot:F3} landing={landing:F3} width=4.2 thickness={thickness:F2}");
            ApplyUrp(ramp, new Color(0.32f, 0.30f, 0.28f));
            MagentaGuard.ProtectPrimitiveArt(ramp, "RaidBaseDresser.KeepRamp");

            if (kit == "dungeon-stone")
            {
                var ceil = LoadVisual("ceiling_tile");
                if (ceil != null)
                {
                    float span = MeasureLongest(ceil);
                    if (span < 1f) span = 4f;
                    float y = 4.2f;
                    for (float x = -half + span * 0.5f; x <= half; x += span)
                    for (float z = -half + span * 0.5f; z <= half; z += span)
                    {
                        var go = InstantiateVisual(ceil, keep, "KeepCeiling", new Vector3(x, y, z),
                                                   Quaternion.identity, true);
                        if (go != null) StripColliders(go);
                    }
                }
                var col = LoadVisual("pillar_decorated");
                if (col == null) col = LoadVisual("pillar");
                if (col != null)
                {
                    float d = half * 0.7f;
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(-d, 0f, -d), Quaternion.identity, true);
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(d, 0f, -d), Quaternion.identity, true);
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(-d, 0f, d), Quaternion.identity, true);
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(d, 0f, d), Quaternion.identity, true);
                }
            }
        }

        // -- helpers ----------------------------------------------------------

        private static void ApplyUrp(GameObject go, Color c)
        {
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) return;
            var m = MagentaGuard.BuildUrpLitMaterial(c);
            if (m != null) r.sharedMaterial = m;
        }

        private static void StripColliders(GameObject go)
        {
            if (go == null) return;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) Object.DestroyImmediate(cols[i]);
        }

        private static void SeatOnGround(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            go.transform.position += new Vector3(0f, -b.min.y, 0f);
        }

        private static float MeasureLongest(GameObject model)
        {
            var tmp = Object.Instantiate(model);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            float w = 4f;
            var rends = tmp.GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                w = Mathf.Max(b.size.x, b.size.z);
            }
            Object.DestroyImmediate(tmp);
            return w;
        }

        /// <summary>
        /// World-space height of a PLACED instance (WO-1689). Sibling to
        /// <see cref="MeasureLongest"/>, which measures a PREFAB by instantiating a throwaway
        /// copy; this one reads an object already in the scene, so it reports the height AFTER
        /// any fit scale has been applied - which is the number the gate has to match.
        /// Returns 0 for a null object or one with no renderers, and every caller treats 0 as
        /// "unknown" rather than as a failure.
        /// </summary>
        private static float MeasureHeight(GameObject go)
        {
            if (go == null) return 0f;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return 0f;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.size.y;
        }

        /// <summary>
        /// Height of a PREFAB (WO-1689). Instantiates a throwaway copy and destroys it, exactly
        /// as <see cref="MeasureLongest"/> does and for the same reason: a prefab ASSET's
        /// renderer bounds are not the world bounds a placed instance reports, so measuring the
        /// asset directly would quietly read a different number than the scene will.
        /// </summary>
        private static float MeasureTallest(GameObject model)
        {
            if (model == null) return 0f;
            var tmp = Object.Instantiate(model);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            float h = MeasureHeight(tmp);
            Object.DestroyImmediate(tmp);
            return h;
        }

        private static bool VisualBounds(GameObject go, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool found = false;
            if (go == null) return false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }
            return found;
        }

        private static float AlongSize(Bounds bounds, Vector3 direction)
        {
            return Mathf.Abs(direction.x) * bounds.size.x + Mathf.Abs(direction.y) * bounds.size.y +
                Mathf.Abs(direction.z) * bounds.size.z;
        }

        private static void FitPieceAlong(GameObject go, float target, Vector3 centre)
        {
            if (!VisualBounds(go, out Bounds bounds)) return;
            float native = AlongSize(bounds, go.transform.right);
            if (native < 0.01f) return;
            // Preserve authored height/thickness; fit only the modular run axis.
            var scale = go.transform.localScale;
            scale.x *= target / native;
            go.transform.localScale = scale;
            if (!VisualBounds(go, out bounds)) return;
            go.transform.position += new Vector3(centre.x - bounds.center.x, -bounds.min.y, centre.z - bounds.center.z);
        }

        private static void OpenGateAssembly(GameObject gate, string kit, float width, Vector3 centre)
        {
            var doors = new List<Transform>();
            Transform portcullis = null;
            foreach (var child in gate.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "wall_doorway_door" || child.name.Contains("_Gate_Door_")) doors.Add(child);
                if (child.name.Contains("_Gate_Portcullis_")) portcullis = child;
            }
            if (doors.Count == 0)
            {
                FlowTrace.Warn(Sys, "GATE opener missing authored leaves on " + gate.name);
                return;
            }
            bool measured = false;
            Bounds doorBounds = default(Bounds);
            foreach (var door in doors)
                if (VisualBounds(door.gameObject, out Bounds b))
                {
                    if (!measured) doorBounds = b;
                    else doorBounds.Encapsulate(b);
                    measured = true;
                }
            if (!measured || !VisualBounds(gate, out Bounds frameBounds)) return;
            Vector3 right = gate.transform.right;
            float nativeOpening = AlongSize(doorBounds, right);
            float nativeFrame = AlongSize(frameBounds, right);
            if (nativeOpening < 0.01f || nativeFrame < 0.01f) return;
            // A little headroom above the existing 3.5m floor leaves posts/leaf bevels clear.
            // A narrow physical cut may receive wider jambs OVER its adjacent solid wall;
            // the opening stays within the physical cut and no ring generation changes.
            float targetFrame = Mathf.Max(width, nativeFrame * (MinGateWidth + 0.25f) / nativeOpening);
            FitPieceAlong(gate, targetFrame, centre);
            foreach (var door in doors)
            {
                if (!VisualBounds(door.gameObject, out Bounds b)) continue;
                bool rightLeaf = door.name.Contains("_Door_R_");
                float sign = rightLeaf ? 1f : -1f;
                Vector3 hinge = b.center + right * (AlongSize(b, right) * 0.5f * sign);
                door.RotateAround(hinge, gate.transform.up, 180f);
            }
            if (portcullis != null && VisualBounds(portcullis.gameObject, out Bounds bars))
                portcullis.position += gate.transform.up * (bars.size.y + 0.1f);
            FlowTrace.Step(Sys, "GATE OPEN kit=" + kit + " leaves=" + doors.Count +
                " frameWidth=" + targetFrame.ToString("F3") + "m requestedCut=" + width.ToString("F3") +
                "m apertureFromLeaves=" + (nativeOpening * targetFrame / nativeFrame).ToString("F3") +
                "m portcullis=" + (portcullis != null ? "raised" : "none"));
        }

        // The frame's lowest vertex is not its walkable sill. The saved garrison probe
        // measured a 0.635m sill above ground against the actual 0.4m agent step.
        // Seat the centre of the opened aperture on the authored ground, keeping the
        // mesh and collider together rather than hiding the obstruction from navigation.
        private static void SeatGateThreshold(GameObject gate, Vector3 ground)
        {
            if (!VisualBounds(gate, out Bounds bounds)) return;
            float probeHeight = Mathf.Min(2f, bounds.size.y * 0.25f);
            var ray = new Ray(ground + Vector3.up * probeHeight, Vector3.down);
            Physics.SyncTransforms();
            float sill = ground.y;
            bool found = false;
            foreach (var collider in gate.GetComponentsInChildren<Collider>(false))
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (collider.Raycast(ray, out RaycastHit hit, probeHeight + 0.1f) && hit.normal.y > 0.5f)
                {
                    sill = Mathf.Max(sill, hit.point.y);
                    found = true;
                }
            }
            if (!found)
            {
                FlowTrace.Step(Sys, "GATE threshold " + gate.name + " has no raised collider across aperture.");
                return;
            }
            float correction = Mathf.Max(0f, sill - ground.y);
            gate.transform.position -= Vector3.up * correction;
            Physics.SyncTransforms();
            FlowTrace.Step(Sys, "GATE threshold " + gate.name + " measured=" + sill.ToString("F3") +
                " ground=" + ground.y.ToString("F3") + " lowered=" + correction.ToString("F3"));
        }

        private static void WarnMissing(string token)
        {
            _missing++;
            if (Warned.Add(token))
            {
                Debug.LogWarning($"[RaidBase] art '{token}' not found (KayKit/Synty gitignored or StructureContent miss) - skipped.");
                FlowTrace.Warn(Sys, "missing token=" + token);
            }
        }

        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 17;
            unchecked
            {
                int h = 23;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h & 0x7fffffff;
            }
        }
    }
}
