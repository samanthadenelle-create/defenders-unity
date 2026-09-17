// =============================================================================
// DefaultDungeonRoomsBuilder — forges the KEY DEFAULT room prefab library.
// -----------------------------------------------------------------------------
// Menu: Defenders/Dungeon/Build Default Room Prefabs
// Batch: DeNelle.Editor.RoomForge.DefaultDungeonRoomsBuilder.BuildAll
//
// Produces Assets/Dungeon/Rooms/*.prefab with RoomPrefabMeta + RoomSockets so
// layouts can compose: entrance, straight, turns, T, cross, dead-end, choke,
// combat, lore, reward, secret, stairs. Geometry is procedural (floor + walls)
// surfaced with the SHARED SOLID-STONE materials (RoomForgeMaterials). Those mats are
// deliberately TEXTURELESS: these shells are primitive cubes, so the KayKit atlas maps
// its whole swatch grid onto every face = the WO-1004 rainbow. The comment here used to
// call them "KayKit dungeon atlas materials", which is the opposite of what they are and
// is how a later author talks themselves into re-adding the atlas.
// Sockets match the DungeonBaker mate convention; the cell grain, wall height,
// door gap and ceiling thickness all come from RoomForgeCanon (WO-919 + WO-922) —
// NEVER re-type those numbers here, the regression oracles read the same file.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Dungeons.RoomForge;

namespace DeNelle.Editor.RoomForge
{
    public static class DefaultDungeonRoomsBuilder
    {
        private const string RoomsFolder = "Assets/Dungeon/Rooms";
        private const string CatalogPath =
            "Assets/StreamingAssets/Data/Canonical/dungeon-layouts/rooms-catalog.json";
        private const string CatalogPathRes =
            "Assets/Resources/Data/Canonical/dungeon-layouts/rooms-catalog.json";
        // WO-922: the master room-size knob now lives in the SHARED runtime canon so the
        // baker, the dresser and the regression oracles cannot drift from it. 6f -> 10f.
        private const float Cell = RoomForgeCanon.Cell;
        private const string Sys = "RoomForgeDefaults";

        /// <summary>UTF-8 WITHOUT a byte-order mark. Never use <c>Encoding.UTF8</c> to
        /// write canonical JSON: that overload emits a leading EF BB BF which fails the
        /// static check-in gate's JSON parse.</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private struct RoomSpec
        {
            public string id;
            public string archetype;
            public Vector2Int footprint; // cells
            public string[] facings;     // which cardinals get Door sockets
            public bool choke;           // narrow interior walls
            public bool secretSocket;    // mark first socket secret (or west)
            public RoomSocketType? stairType; // if set, adds a stair socket at center-north
            public bool accentFloor;         // reward/boss — warm KayKit accent mat
            public string note;
        }

        [MenuItem("Defenders/Dungeon/Build Default Room Prefabs")]
        public static void BuildAll()
        {
            using var _ = FlowTrace.Enter("RoomForge", "BuildAll");
            EnsureFolder(RoomsFolder);
            EnsureFolder("Assets/StreamingAssets/Data/Canonical/dungeon-layouts");
            EnsureFolder("Assets/Resources/Data/Canonical/dungeon-layouts");

            // One shared wall + floor + accent mat, solid stone and textureless (all rooms).
            // EnsureMenu also re-runs the exhaustive strip, so a mat asset that picked up the
            // atlas (or an atlas-era 1.5x/2.5x tiling) in an older revision is cleaned here
            // before a single prefab is written against it.
            RoomForgeMaterials.EnsureMenu();

            var specs = DefaultSpecs();
            FlowTrace.Step("RoomForge", $"BuildDefaultRoomPrefabs specs={specs.Count} folder='{RoomsFolder}' " +
                                        $"cell={Cell:F1}m wallH={RoomForgeCanon.WallHeight:F1}m " +
                                        $"chokeH={RoomForgeCanon.ChokeWallHeight:F1}m " +
                                        $"ceilingT={RoomForgeCanon.CeilingThickness:F2}m " +
                                        $"floorOccupied={RoomForgeCanon.FloorOccupiedHeight:F2}m " +
                                        $"floorSep={DungeonBakerChecks.FloorSeparationY:F1}m (WO-919 + WO-922)");
            var catalog = new RoomCatalogFile { version = 1, rooms = new List<RoomCatalogEntry>() };
            int ok = 0;
            foreach (var spec in specs)
            {
                if (BuildOne(spec, catalog)) ok++;
            }

            WriteCatalog(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            FlowTrace.Step("RoomForge", $"built {ok}/{specs.Count} default room prefabs -> {RoomsFolder} + rooms-catalog.json " +
                                        $"(shared TEXTURELESS stone mats on every surfaced slot - no atlas)");
        }

        /// <summary>Batchmode: Unity -executeMethod DeNelle.Editor.RoomForge.DefaultDungeonRoomsBuilder.BuildAll</summary>
        public static void BuildAllBatch()
        {
            BuildAll();
            // Do not EditorApplication.Exit here if invoked from a multi-method run; menu path is fine.
        }

        private static List<RoomSpec> DefaultSpecs()
        {
            return new List<RoomSpec>
            {
                // ── Spine / flow ───────────────────────────────────────────
                new RoomSpec
                {
                    id = "Entrance",
                    archetype = "hub",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S", "N" }, // S = world/approach, N = into dungeon
                    note = "Dungeon mouth — approach south, continue north",
                },
                new RoomSpec
                {
                    id = "EntryHall",
                    archetype = "hub",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S", "N" },
                    note = "Alias-friendly hub (spine sample name)",
                },
                new RoomSpec
                {
                    id = "Straight",
                    archetype = "combat",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S", "N" },
                    note = "Corridor cell — north/south",
                },
                new RoomSpec
                {
                    id = "TurnLeft",
                    archetype = "combat",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S", "W" }, // enter from S facing N → exit W = left
                    note = "Left bend (enter S, leave W)",
                },
                new RoomSpec
                {
                    id = "TurnRight",
                    archetype = "combat",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S", "E" }, // enter S → leave E = right
                    note = "Right bend (enter S, leave E)",
                },
                new RoomSpec
                {
                    id = "TJunction",
                    archetype = "combat",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S", "E", "W" }, // no north wall open — T when approached from S
                    note = "T-junction (S/E/W)",
                },
                new RoomSpec
                {
                    id = "Intersection",
                    archetype = "combat",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "N", "E", "S", "W" },
                    note = "4-way cross",
                },
                new RoomSpec
                {
                    id = "DeadEnd",
                    archetype = "lore",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S" }, // only way in/out
                    note = "Cul-de-sac (single south socket)",
                },
                new RoomSpec
                {
                    id = "ChokePoint",
                    archetype = "combat",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S", "N" },
                    choke = true,
                    note = "Narrow pass N/S — ambush / squeeze",
                },

                // ── Content rooms ──────────────────────────────────────────
                new RoomSpec
                {
                    id = "CombatChamber",
                    archetype = "combat",
                    footprint = new Vector2Int(2, 2),
                    facings = new[] { "S", "N" },
                    note = "2x2 fight room",
                },
                new RoomSpec
                {
                    id = "LoreShrine",
                    archetype = "lore",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S" },
                    note = "Shrine / lore stone (dead-end lore)",
                },
                new RoomSpec
                {
                    id = "RewardVault",
                    archetype = "reward",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S" },
                    accentFloor = true,
                    note = "Treasure end room (accent floor tint)",
                },
                new RoomSpec
                {
                    id = "SecretAlcove",
                    archetype = "secret",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S" },
                    secretSocket = true,
                    note = "Secret alcove — socket flagged isSecret",
                },

                // ── Vertical ───────────────────────────────────────────────
                new RoomSpec
                {
                    id = "StairDown",
                    archetype = "hub",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S" },
                    stairType = RoomSocketType.StairDown,
                    note = "Horizontal entry + stair down socket",
                },
                new RoomSpec
                {
                    id = "StairUp",
                    archetype = "hub",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "S" },
                    stairType = RoomSocketType.StairUp,
                    note = "Horizontal entry + stair up socket",
                },

                // ── Branches / extras ──────────────────────────────────────
                new RoomSpec
                {
                    id = "SideBranch",
                    archetype = "combat",
                    footprint = new Vector2Int(1, 1),
                    facings = new[] { "W", "E" }, // east-west spur
                    note = "East-west spur corridor",
                },
                new RoomSpec
                {
                    id = "BossKeep",
                    archetype = "boss",
                    footprint = new Vector2Int(2, 2),
                    facings = new[] { "S" },
                    accentFloor = true,
                    note = "Boss arena (enter south only, accent floor)",
                },
            };
        }

        private static bool BuildOne(RoomSpec spec, RoomCatalogFile catalog)
        {
            float wx = spec.footprint.x * Cell;
            float wz = spec.footprint.y * Cell;
            float hx = wx * 0.5f;
            float hz = wz * 0.5f;

            var root = new GameObject($"Room_{spec.id}");
            try
            {
                var meta = root.AddComponent<RoomPrefabMeta>();
                meta.roomId = spec.id;
                meta.archetype = spec.archetype;
                meta.themePalette = "default";
                meta.footprintCells = spec.footprint;
                meta.cellSize = Cell;

                // Floor. The slab's TOP face is local y = 0 (it hangs below), which is what
                // makes wall/ceiling seating arithmetic readable everywhere else.
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Floor";
                floor.transform.SetParent(root.transform, false);
                floor.transform.localPosition = new Vector3(0f, -RoomForgeCanon.FloorSlabThickness * 0.5f, 0f);
                floor.transform.localScale = new Vector3(wx, RoomForgeCanon.FloorSlabThickness, wz);
                GameObjectUtility.SetStaticEditorFlags(floor,
                    StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);

                // Perimeter walls with gaps at open facings
                BuildPerimeterWalls(root.transform, hx, hz, spec.facings);

                if (spec.choke)
                    BuildChokeInterior(root.transform, hx, hz);

                // WO-919: roof the room.
                BuildCeiling(root.transform, spec.id, wx, wz);

                // Door sockets
                var socketList = new List<RoomCatalogSocket>();
                foreach (var f in spec.facings)
                {
                    bool secret = spec.secretSocket && f == "S";
                    var sock = AddSocket(root.transform, f, RoomSocketType.Door, hx, hz, secret);
                    socketList.Add(ToCatalogSocket(sock));
                }

                if (spec.stairType.HasValue)
                {
                    var stair = AddStairSocket(root.transform, spec.stairType.Value);
                    socketList.Add(ToCatalogSocket(stair));
                }

                // Center marker for spawn / shrine later
                var marker = new GameObject("Anchor_Center");
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = Vector3.zero;

                // WO-1004 — the surfacing pass is now the LAST thing that happens before the
                // prefab is written, and it is followed by a proof.
                //
                // It used to sit in the middle of BuildOne, immediately after BuildCeiling,
                // guarded only by a comment telling the next author to keep the ceiling above
                // it. That ordering rule is exactly the kind that gets broken silently: ANY
                // piece added below the call ships with whatever material the primitive was
                // born with (the pipeline default — never stone), and nothing fails. Running
                // it here instead makes "every renderer that exists has been surfaced" true by
                // position rather than by discipline, no matter what future geometry lands
                // above it. VerifyRoomSurfaces then walks every renderer and every material
                // slot and refuses to let a textured/foreign/NULL slot reach disk unnoticed —
                // a textured surface on these un-UV'd primitive shells IS the rainbow.
                RoomForgeMaterials.ApplyToRoomRoot(root, useAccentFloor: spec.accentFloor);
                int badSlots = RoomForgeMaterials.VerifyRoomSurfaces(root, spec.id, spec.accentFloor);
                if (badSlots > 0)
                    // Band "RoomForge" (not Sys) so this lands beside the room-save + ceiling
                    // lines the F8 harvester groups.
                    FlowTrace.Warn("RoomForge", $"room '{spec.id}' had {badSlots} non-stone surface slot(s) at save " +
                                        "time - corrected, but a piece is being created outside the surfacing pass");

                string prefabPath = $"{RoomsFolder}/{spec.id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success)
                {
                    FlowTrace.Fail("RoomForge", $"failed to save prefab '{prefabPath}'");
                    return false;
                }

                catalog.rooms.Add(new RoomCatalogEntry
                {
                    id = spec.id,
                    prefabPath = prefabPath,
                    archetype = spec.archetype,
                    themePalette = "default",
                    footprintCells = new[] { spec.footprint.x, spec.footprint.y },
                    cellSize = Cell,
                    sockets = socketList,
                });

                FlowTrace.Step("RoomForge", $"room saved id='{spec.id}' archetype='{spec.archetype}' " +
                                            $"footprint={spec.footprint.x}x{spec.footprint.y} sockets={socketList.Count} -> {prefabPath}");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildPerimeterWalls(Transform parent, float hx, float hz, string[] openFacings)
        {
            var open = new HashSet<string>(openFacings);
            // WO-919: 2.8 -> 4.0. At 2.8 the wall line sat at/below the third-person camera
            // seat, so every composed room framed procedural blue sky above chest-height walls.
            float wallH = RoomForgeCanon.WallHeight;
            float thick = RoomForgeCanon.WallThickness;
            float gap = RoomForgeCanon.DoorGap; // doorway clear width (unchanged by the widen)

            // North wall (+Z)
            if (open.Contains("N"))
                BuildWallWithGap(parent, "Wall_N", new Vector3(0f, wallH * 0.5f, hz),
                    new Vector3(hx * 2f, wallH, thick), gap, alongX: true);
            else
                BuildSolidWall(parent, "Wall_N", new Vector3(0f, wallH * 0.5f, hz),
                    new Vector3(hx * 2f, wallH, thick));

            // South (-Z)
            if (open.Contains("S"))
                BuildWallWithGap(parent, "Wall_S", new Vector3(0f, wallH * 0.5f, -hz),
                    new Vector3(hx * 2f, wallH, thick), gap, alongX: true);
            else
                BuildSolidWall(parent, "Wall_S", new Vector3(0f, wallH * 0.5f, -hz),
                    new Vector3(hx * 2f, wallH, thick));

            // East (+X)
            if (open.Contains("E"))
                BuildWallWithGap(parent, "Wall_E", new Vector3(hx, wallH * 0.5f, 0f),
                    new Vector3(thick, wallH, hz * 2f), gap, alongX: false);
            else
                BuildSolidWall(parent, "Wall_E", new Vector3(hx, wallH * 0.5f, 0f),
                    new Vector3(thick, wallH, hz * 2f));

            // West (-X)
            if (open.Contains("W"))
                BuildWallWithGap(parent, "Wall_W", new Vector3(-hx, wallH * 0.5f, 0f),
                    new Vector3(thick, wallH, hz * 2f), gap, alongX: false);
            else
                BuildSolidWall(parent, "Wall_W", new Vector3(-hx, wallH * 0.5f, 0f),
                    new Vector3(thick, wallH, hz * 2f));
        }

        /// <summary>
        /// WO-919 — roof one room with a solid slab seated ON the wall top, so an in-room
        /// camera cannot see sky at ANY pitch. A slab (not a KayKit ceiling_tile retile) is the
        /// deliberate V1: the room shells are primitive cubes with no authored UVs, so a tiled
        /// atlas piece would rainbow exactly the way RoomForgeMaterials documents for the walls.
        ///
        /// Three properties this MUST hold, each of which broke something when assumed:
        ///  * It overhangs the perimeter by the wall thickness, so the wall tops are capped and
        ///    no hairline of sky shows down the seam between slab edge and wall face.
        ///  * It carries NO COLLIDER. DungeonBaker bakes its NavMesh with
        ///    NavMeshCollectGeometry.PhysicsColliders, so a collider here would voxelize into a
        ///    second WALKABLE surface on the roof - which NavMesh.SamplePosition can snap a hero
        ///    seat, an enemy spawner or a stair port onto. No collider = not collected at all,
        ///    and it can never block the agent either.
        ///  * It is NOT NavigationStatic. BatchingStatic only: geometry, never navigation.
        /// </summary>
        private static void BuildCeiling(Transform parent, string roomId, float wx, float wz)
        {
            // Band "RoomForge" (not Sys) so the ceiling lines land in the same [Flow:RoomForge]
            // band as every other room-save line the F8 harvester groups.
            bool ok = Guard.Try("RoomForge", $"build ceiling for room '{roomId}'", () =>
            {
                var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ceiling.name = "Ceiling";
                ceiling.transform.SetParent(parent, false);
                ceiling.transform.localPosition =
                    new Vector3(0f, RoomForgeCanon.WallHeight + RoomForgeCanon.CeilingThickness * 0.5f, 0f);
                ceiling.transform.localScale = new Vector3(
                    wx + RoomForgeCanon.WallThickness,
                    RoomForgeCanon.CeilingThickness,
                    wz + RoomForgeCanon.WallThickness);

                var col = ceiling.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);

                GameObjectUtility.SetStaticEditorFlags(ceiling, StaticEditorFlags.BatchingStatic);
            });

            if (!ok)
                FlowTrace.Fail("RoomForge", $"ceiling NOT built for room '{roomId}' - it will render open to sky");
            else
                FlowTrace.Step("RoomForge", $"ceiling room='{roomId}' span={wx + RoomForgeCanon.WallThickness:F1}x" +
                                    $"{wz + RoomForgeCanon.WallThickness:F1} underside y={RoomForgeCanon.WallHeight:F1} " +
                                    $"thick={RoomForgeCanon.CeilingThickness:F2} collider=none navStatic=false");
        }

        private static void BuildSolidWall(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = name;
            w.transform.SetParent(parent, false);
            w.transform.localPosition = localPos;
            w.transform.localScale = scale;
            // Material applied in bulk via RoomForgeMaterials.ApplyToRoomRoot.
            GameObjectUtility.SetStaticEditorFlags(w, StaticEditorFlags.NavigationStatic);
            // WO-1837: a room wall is a sight blocker. HeroTargetIndicator.HasLoS masks its
            // linecast to "Structure", so a wall left on Default lets the hero lock and attack
            // through it. Every baked Wall_* in Assets/Dungeon/Rooms/*.prefab read m_Layer: 0
            // before this line. ONE owner for the rule — never re-copy WallSegment.cs:646-647.
            DeNelle.Dungeons.RoomForge.DungeonStructureLayer.Apply(
                w, "room wall must block hero line-of-sight");
        }

        private static void BuildWallWithGap(Transform parent, string name, Vector3 center, Vector3 fullScale,
            float gap, bool alongX)
        {
            // Two half-walls flanking a center gap.
            if (alongX)
            {
                float total = fullScale.x;
                float side = (total - gap) * 0.5f;
                if (side < 0.2f) side = 0.2f;
                float z = center.z;
                float y = center.y;
                float thick = fullScale.z;
                float h = fullScale.y;
                // Left (-X) piece
                BuildSolidWall(parent, name + "_L",
                    new Vector3(-(gap * 0.5f + side * 0.5f), y, z),
                    new Vector3(side, h, thick));
                // Right (+X) piece
                BuildSolidWall(parent, name + "_R",
                    new Vector3(+(gap * 0.5f + side * 0.5f), y, z),
                    new Vector3(side, h, thick));
            }
            else
            {
                float total = fullScale.z;
                float side = (total - gap) * 0.5f;
                if (side < 0.2f) side = 0.2f;
                float x = center.x;
                float y = center.y;
                float thick = fullScale.x;
                float h = fullScale.y;
                BuildSolidWall(parent, name + "_A",
                    new Vector3(x, y, -(gap * 0.5f + side * 0.5f)),
                    new Vector3(thick, h, side));
                BuildSolidWall(parent, name + "_B",
                    new Vector3(x, y, +(gap * 0.5f + side * 0.5f)),
                    new Vector3(thick, h, side));
            }
        }

        private static void BuildChokeInterior(Transform parent, float hx, float hz)
        {
            // Two side masses leaving a ~2u walk lane along +Z (N/S sockets).
            // WO-919: 2.4 -> 3.8. The masses have to be un-see-over-able like the perimeter,
            // so canon pins them at WallHeight - 0.2 (the WO's stated floor).
            float wallH = RoomForgeCanon.ChokeWallHeight;
            float laneHalf = 1.05f;
            float massW = Mathf.Max(0.6f, hx - laneHalf - 0.15f);
            BuildSolidWall(parent, "Choke_W",
                new Vector3(-(laneHalf + massW * 0.5f), wallH * 0.5f, 0f),
                new Vector3(massW, wallH, hz * 1.4f));
            BuildSolidWall(parent, "Choke_E",
                new Vector3(+(laneHalf + massW * 0.5f), wallH * 0.5f, 0f),
                new Vector3(massW, wallH, hz * 1.4f));
        }

        private static RoomSocket AddSocket(Transform parent, string facing, RoomSocketType type,
            float hx, float hz, bool secret)
        {
            Vector3 local = facing switch
            {
                "N" => new Vector3(0f, 0f, hz),
                "S" => new Vector3(0f, 0f, -hz),
                "E" => new Vector3(hx, 0f, 0f),
                "W" => new Vector3(-hx, 0f, 0f),
                _ => Vector3.zero,
            };
            Quaternion rot = facing switch
            {
                "N" => Quaternion.LookRotation(Vector3.forward),
                "S" => Quaternion.LookRotation(Vector3.back),
                "E" => Quaternion.LookRotation(Vector3.right),
                "W" => Quaternion.LookRotation(Vector3.left),
                _ => Quaternion.identity,
            };

            string id = $"{facing.ToLowerInvariant()}_{type.ToString().ToLowerInvariant()}_01";
            var go = new GameObject($"Socket_{id}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = rot;
            var sock = go.AddComponent<RoomSocket>();
            sock.id = id;
            sock.type = type;
            sock.facing = facing;
            sock.isSecret = secret;
            sock.halfWidth = type == RoomSocketType.Arch ? 1.5f : 1.1f;
            return sock;
        }

        private static RoomSocket AddStairSocket(Transform parent, RoomSocketType stairType)
        {
            // WO-1001 slice 1. A stair socket has to express TWO things a door socket never
            // does: which way you travel through it, and how far down the next floor sits.
            //
            // Direction: the mate test is dot(a.Outward, -b.Outward) >= 0.25, so the pair must
            // OPPOSE. StairDown leads downward (outward -Y), StairUp leads upward (outward +Y);
            // mating them gives dot(-Y, -(+Y)) = +1. Both used to point down, which scored -1 and
            // could never mate - that, not the composer, is why no multi-level bake ever existed.
            //
            // Height: each socket sits half a floor off its own room origin, so when the composer
            // slides the child until the socket origins coincide, the rooms land exactly
            // FloorSeparationY apart with no separate elevation field in the graph schema.
            float halfFloor = DungeonBakerChecks.FloorSeparationY * 0.5f;
            bool down = stairType == RoomSocketType.StairDown;

            var go = new GameObject($"Socket_stair_{stairType}");
            go.transform.SetParent(parent, false);
            // X/Z MUST stay 0. The door helper offsets sockets 0.5u off the wall face, and this
            // socket inherited that - but a stair socket is a hole in the FLOOR and has no wall to
            // stand off from, so the 0.5 bought nothing and broke the composer's stated invariant:
            // "sockets sit on the room's HALF-CELL grid (Cell/2 — 3u when the cell was 6, 5u now
            // at WO-922's 10) ... so cell=[round(x),round(y),round(z)] is a lossless round-trip".
            // Each stairwell injected a half unit that RoundToInt quantised into a
            // FULL unit of drift, accumulating down a descent until rooms that should exactly touch
            // sat 1u too close and the bake aborted on overlap (dg_bonecrypt, dg_ember_deep).
            go.transform.localPosition = new Vector3(0f, down ? -halfFloor : halfFloor, 0f);
            // Explicit up-vector: LookRotation(up) alone is degenerate (forward parallel to the
            // default world up) and yields an arbitrary roll.
            go.transform.localRotation = down
                ? Quaternion.LookRotation(Vector3.down, Vector3.forward)
                : Quaternion.LookRotation(Vector3.up, Vector3.forward);
            var sock = go.AddComponent<RoomSocket>();
            sock.id = stairType == RoomSocketType.StairDown ? "stair_down_01" : "stair_up_01";
            sock.type = stairType;
            sock.facing = "U";
            sock.halfWidth = 1.2f;
            return sock;
        }

        private static RoomCatalogSocket ToCatalogSocket(RoomSocket s)
        {
            var lp = s.transform.localPosition;
            return new RoomCatalogSocket
            {
                id = s.id,
                type = s.type.ToString(),
                facing = s.facing,
                isSecret = s.isSecret,
                commonDoor = s.commonDoor,
                doorPolicy = s.doorPolicy.ToString(),
                localPosition = new[] { lp.x, lp.y, lp.z },
            };
        }

        private static void WriteCatalog(RoomCatalogFile catalog)
        {
            string json = JsonConvert.SerializeObject(catalog, Formatting.Indented);
            // Encoding.UTF8 EMITS A BOM - a leading EF BB BF fails the static
            // check-in gate's canonical-JSON parse and is invisible to the EditMode
            // integrity test (File.ReadAllText silently eats it). Always UTF8-no-BOM
            // on a canonical path.
            bool wrote = Guard.Try("RoomForge", "write rooms-catalog dual-copy", () =>
            {
                File.WriteAllText(CatalogPath, json, Utf8NoBom);
                File.WriteAllText(CatalogPathRes, json, Utf8NoBom);
            });
            FlowTrace.Step("RoomForge", $"catalog write entries={catalog.rooms.Count} dualCopy={(wrote ? "ok" : "FAILED")} " +
                                        $"(StreamingAssets + Resources)");
            // Also write a human README of the kit
            string readme = Path.Combine(Application.dataPath, "Dungeon/Rooms/DEFAULT_ROOMS.md");
            var sb = new StringBuilder();
            sb.AppendLine("# Default dungeon room prefabs");
            sb.AppendLine();
            sb.AppendLine("| Prefab | Archetype | Sockets | Notes |");
            sb.AppendLine("|--------|-----------|---------|-------|");
            foreach (var s in DefaultSpecs())
            {
                string socks = string.Join(",", s.facings);
                if (s.stairType.HasValue) socks += $"+{s.stairType}";
                sb.AppendLine($"| `{s.id}` | {s.archetype} | {socks} | {s.note} |");
            }
            sb.AppendLine();
            sb.AppendLine("Rebuild: `Defenders/Dungeon/Build Default Room Prefabs`");
            File.WriteAllText(readme, sb.ToString(), Encoding.UTF8);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string[] parts = assetFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
