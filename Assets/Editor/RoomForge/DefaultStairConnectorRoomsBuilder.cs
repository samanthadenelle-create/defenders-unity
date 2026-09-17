// =============================================================================
// DefaultStairConnectorRoomsBuilder — snap-on stair CONNECTOR rooms (WO-923).
// -----------------------------------------------------------------------------
// Menu:  Defenders/Dungeon/Build Stair Connector Room Prefabs
// Batch: DeNelle.Editor.RoomForge.DefaultStairConnectorRoomsBuilder.BuildAll
//
// Three SHAPES the owner asked for, each as a normal RoomForge room (same shell
// style as DefaultDungeonRoomsBuilder: floor + walls + door gap + stone mats):
//
//   StairConnector_Vertical  — one straight flight (rise 6 m over ~10 m run)
//   StairConnector_Left      — half-flight → landing → turn left → half-flight
//   StairConnector_Right     — mirror of Left
//
// SNAP-ON CONTRACT
//   • Door socket on SOUTH (id s_door_01) — the "middle door" corridors mate to.
//     Composer rotates the whole room so that door faces the connection.
//   • Stair socket at local (0, ±FloorSeparationY/2, 0) — X/Z MUST stay 0 (grid
//     invariant). StairDown on the UPPER floor room, StairUp on the LOWER.
//
// Each shape is saved TWICE: *_Down (StairDown socket) and *_Up (StairUp socket)
// so the graph can mate vertical pairs the same way StairDown/StairUp rooms do.
//
// ⚠ THE TWO VARIANTS ARE NOT THE SAME ROOM. ONE OWNER OWNS THE FLIGHT.
//   AddStairSocket seats StairDown FloorSeparationY/2 BELOW its room origin and
//   StairUp the same distance ABOVE, and GraphDungeonComposer.SolveMate slides the
//   child until the socket origins coincide — so the pair stacks as
//       Y_down = Y_up + FloorSeparationY.
//   A flight authored from the room origin therefore spans:
//       _Up  : Y_up      -> Y_up + 6  == Y_down     <- IS the inter-floor connection
//       _Down: Y_up + 6  -> Y_up + 12               <- climbs through its own ceiling
//   Building it in BOTH gave two interpenetrating staircases plus a stray flight
//   into open air above the dungeon (STAIR_PREFAB_SCRIPT_CONTRACT §5's named failure).
//   Resolution = contract §5 "one owner":
//       _Up   owns the WHOLE flight  + a SOLID floor to stand it on + a SHAFT in its
//             CEILING, which the flight crosses on its way up (WallHeight = 4.0).
//       _Down is a bare UPPER LANDING — no steps, no ramp — whose FLOOR carries the
//             HOLE the arriving flight comes up through (contract §4 item 3), and whose
//             CEILING is SOLID, because the flight stops at this room's floor plane.
//   Flight, floor hole and ceiling shaft all come out of ONE FlightPlan() via PlaneCuts()
//   — the floor against y=rise, the ceiling against y=WallHeight — so no opening can
//   drift off the stair it exists to clear. The floor hole always falls INSIDE the
//   ceiling shaft, which is what makes the stairwell one continuous void.
//
// WALK SURFACE (owner: steps + plane for nav)
//   Visual steps = cubes, NO colliders.
//   Ramp = thin Cube on the nose line, BoxCollider kept, MeshRenderer stripped
//   (HideMesh). NEVER PrimitiveType.Plane. Ramp overlaps both landings.
//
// NAV AGENT metrics (head clearance / capsule radius / max slope) are READ LIVE from
// ProjectSettings/NavMeshAreas.asset via NavMesh.GetSettingsByID(0) — never re-typed.
//
// MERGES into rooms-catalog.json (does not wipe the main kit).
// Cell / wall / door metrics: RoomForgeCanon only — never re-type.
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
    public static class DefaultStairConnectorRoomsBuilder
    {
        private const string RoomsFolder = "Assets/Dungeon/Rooms";
        private const string CatalogPath =
            "Assets/StreamingAssets/Data/Canonical/dungeon-layouts/rooms-catalog.json";
        private const string CatalogPathRes =
            "Assets/Resources/Data/Canonical/dungeon-layouts/rooms-catalog.json";
        private const float Cell = RoomForgeCanon.Cell;
        private const string Sys = "RoomForgeStairConn";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ── Stair PLAN constants ─────────────────────────────────────────────
        // These are this connector's OWN design numbers (tread width, leg runs, pad
        // depths). Every CANON metric — cell, wall height/thickness, floor slab,
        // ceiling, floor separation — is read from RoomForgeCanon / DungeonBakerChecks
        // and is NEVER re-typed here; likewise the nav agent, read live in ReadNavAgent.

        /// <summary>Tread width of a flight (metres).</summary>
        private const float StairWidth = 2.4f;
        /// <summary>Ramp extension past each nose so the walk surface OVERLAPS the landing (the nav seam).</summary>
        private const float LandingOverlap = 0.35f;
        /// <summary>Ramp collider slab thickness.</summary>
        private const float RampThickness = 0.15f;
        /// <summary>Visual steps in a full-rise flight (halved per leg on the turn shapes).</summary>
        private const int StepsPerFlight = 12;
        /// <summary>Solid floor depth inside the south door before the well opens.</summary>
        private const float EntryPadDepth = 2.0f;
        /// <summary>Solid floor depth beyond the top nose on the straight shape.</summary>
        private const float TopLandingDepth = 1.5f;
        /// <summary>
        /// Planar run of ONE leg of a Left/Right turn shape.
        ///
        /// <para>4.0 -> 3.5 (2026-08-07). At 4.0 the second leg's top nose landed at x = ∓4.0,
        /// and the wall inner face sits at 4.8 — leaving a <b>0.80 m</b> landing against a
        /// <b>1.000 m</b> minimum walkable slot (2 × agentRadius 0.5, read live from
        /// <c>NavMesh.GetSettingsByID(0)</c>). NavMesh erosion takes 0.5 m off each side, so
        /// 0.80 − 1.00 &lt; 0: <b>no polygon survives at the top of the flight</b>, the stair top
        /// becomes an island, and every Left/Right descent reports PathPartial. The builder has
        /// been printing that diagnosis at itself since the landing check was added — it simply
        /// had nothing listening.</para>
        ///
        /// <para>3.5 puts the nose at ∓3.5 for a <b>1.30 m</b> landing — parity with the straight
        /// shape, which is the one variant that clears today. The cost is slope: a 3.0 m half-rise
        /// over 3.5 m instead of 4.0 m is <b>40.6°</b> rather than 36.9°, still under the agent's
        /// 45° maximum but with less margin. Shortening the leg is the right lever because the
        /// landing is a hard navmesh threshold while the slope has 4.4° of headroom.</para>
        ///
        /// <para>⚠ Do NOT push this lower to gain more landing. At 3.0 the slope reaches 45.0° —
        /// exactly the agent maximum, i.e. the ramp stops carving at all. The usable band is
        /// narrow and it is bounded on both sides.</para>
        /// </summary>
        private const float TurnRun = 3.5f;
        /// <summary>Opening widened this far past the stair each side so the agent capsule never brushes the slab edge.</summary>
        private const float ShaftMargin = 0.6f;
        /// <summary>
        /// Rect-subtraction remainders thinner than this are float noise and dropped. Kept at a true
        /// epsilon on purpose: a dropped remainder is a SLIT of missing floor/ceiling, and two cuts
        /// whose edges land 0.03 apart (Left/Right ceiling) would otherwise leave a hairline of sky.
        /// </summary>
        private const float MinSlabExtent = 0.005f;

        /// <summary>Stair plan shape inside the 1×1 connector cell.</summary>
        private enum StairShape
        {
            Vertical, // straight flight along +Z (into the room from the S door)
            Left,     // half + landing + turn west (−X)
            Right,    // half + landing + turn east (+X)
        }

        private struct ConnectorSpec
        {
            public string id;                 // prefab / catalog id
            public StairShape shape;
            public RoomSocketType stairType;  // StairDown (upper floor) or StairUp (lower)
            public string note;
        }

        [MenuItem("Defenders/Dungeon/Build Stair Connector Room Prefabs")]
        public static void BuildAll()
        {
            using var _ = FlowTrace.Enter(Sys, "BuildAll");
            EnsureFolder(RoomsFolder);
            EnsureFolder("Assets/StreamingAssets/Data/Canonical/dungeon-layouts");
            EnsureFolder("Assets/Resources/Data/Canonical/dungeon-layouts");

            RoomForgeMaterials.EnsureMenu();

            var specs = ConnectorSpecs();
            float rise = DungeonBakerChecks.FloorSeparationY;
            var nav = ReadNavAgent();
            FlowTrace.Step(Sys,
                $"BuildStairConnectorRooms specs={specs.Count} cell={Cell:F1}m rise={rise:F1}m " +
                $"wallH={RoomForgeCanon.WallHeight:F1}m doorGap={RoomForgeCanon.DoorGap:F1}m " +
                $"agent(r={nav.radius:F2} h={nav.height:F2} slope={nav.slopeDeg:F0}) " +
                "(snap door=S; stair sockets XZ=0; ONE OWNER: Up=flight, Down=landing+hole)");

            int ok = 0;
            var built = new List<RoomCatalogEntry>();
            foreach (var spec in specs)
            {
                if (BuildOne(spec, built)) ok++;
            }

            MergeCatalog(built);
            WriteReadme(specs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            FlowTrace.Step(Sys,
                $"built {ok}/{specs.Count} stair connector prefabs -> {RoomsFolder} " +
                $"(merged into rooms-catalog.json)");
        }

        /// <summary>Batchmode: DeNelle.Editor.RoomForge.DefaultStairConnectorRoomsBuilder.BuildAllBatch</summary>
        public static void BuildAllBatch() => BuildAll();

        // ── Specs: 3 shapes × Down/Up ────────────────────────────────────────

        private static List<ConnectorSpec> ConnectorSpecs()
        {
            return new List<ConnectorSpec>
            {
                new ConnectorSpec
                {
                    id = "StairConnector_Vertical_Down",
                    shape = StairShape.Vertical,
                    stairType = RoomSocketType.StairDown,
                    note = "Snap door S; UPPER LANDING (no flight) over a straight shaft",
                },
                new ConnectorSpec
                {
                    id = "StairConnector_Vertical_Up",
                    shape = StairShape.Vertical,
                    stairType = RoomSocketType.StairUp,
                    note = "Snap door S; OWNS the straight flight; lower floor (StairUp mate)",
                },
                new ConnectorSpec
                {
                    id = "StairConnector_Left_Down",
                    shape = StairShape.Left,
                    stairType = RoomSocketType.StairDown,
                    note = "Snap door S; UPPER LANDING (no flight) over a left-turn shaft",
                },
                new ConnectorSpec
                {
                    id = "StairConnector_Left_Up",
                    shape = StairShape.Left,
                    stairType = RoomSocketType.StairUp,
                    note = "Snap door S; OWNS the half-flight, landing, turn left (−X); lower floor",
                },
                new ConnectorSpec
                {
                    id = "StairConnector_Right_Down",
                    shape = StairShape.Right,
                    stairType = RoomSocketType.StairDown,
                    note = "Snap door S; UPPER LANDING (no flight) over a right-turn shaft",
                },
                new ConnectorSpec
                {
                    id = "StairConnector_Right_Up",
                    shape = StairShape.Right,
                    stairType = RoomSocketType.StairUp,
                    note = "Snap door S; OWNS the half-flight, landing, turn right (+X); lower floor",
                },
            };
        }

        // ── Build one room prefab ────────────────────────────────────────────

        private static bool BuildOne(ConnectorSpec spec, List<RoomCatalogEntry> built)
        {
            float wx = Cell;
            float wz = Cell;
            float hx = wx * 0.5f;
            float hz = wz * 0.5f;
            float rise = DungeonBakerChecks.FloorSeparationY;

            var root = new GameObject($"Room_{spec.id}");
            try
            {
                var meta = root.AddComponent<RoomPrefabMeta>();
                meta.roomId = spec.id;
                meta.archetype = "hub";
                meta.themePalette = "default";
                meta.footprintCells = new Vector2Int(1, 1);
                meta.cellSize = Cell;

                // ── DECLARE THE SHAFTS (architect review §3.2) ────────────────
                //  The floor hole and the ceiling shaft are cut from the SAME FlightPlan that
                //  builds the flight, so they cannot drift off the stair. Declaring them here
                //  means the [room-shell] oracle can assert two things instead of tolerating
                //  one: that the surface covers everything OUTSIDE the opening, and that the
                //  opening is genuinely OPEN.
                //
                //  Without this, the oracle correctly reports the stairwell as a HOLE - because
                //  from the outside that is exactly what an undeclared opening is. The previous
                //  answer was to weaken the check; this is the answer that keeps its teeth.
                //
                //  Derived from the same call the geometry uses. A second hand-typed rect here
                //  would be a copy that drifts the first time a leg run changes.
                meta.floorShafts = DeclareShafts(spec.shape, spec.stairType, rise, hx, hz, isCeiling: false);
                meta.ceilingShafts = DeclareShafts(spec.shape, spec.stairType, rise, hx, hz, isCeiling: true);

                // Floor. TOP face = local y = 0 (same contract as DefaultDungeonRoomsBuilder).
                // _Up  -> solid (the flight stands on it).
                // _Down -> solid MINUS the shaft the arriving flight comes up through.
                BuildFloor(root.transform, hx, hz, spec.shape, spec.stairType, rise);

                // Perimeter: snap door on SOUTH only; other three walls solid.
                // (Composer rotates the room so S faces the corridor connection.)
                BuildPerimeterWallsSnapSouth(root.transform, hx, hz);

                // Ceiling (WO-919 enclose). _Down -> solid; _Up -> shaft for the flight.
                BuildCeiling(root.transform, hx, hz, spec.shape, spec.stairType, rise);

                // Visual steps + invisible ramp — StairUp ONLY (see the ownership note in
                // the file header). StairDown is a bare landing and builds nothing here.
                BuildStairGeometry(root.transform, spec.shape, spec.stairType, rise, hx, hz);

                // Snap-on door (middle of south wall).
                var socketList = new List<RoomCatalogSocket>();
                var door = AddDoorSocket(root.transform, "S", hx, hz);
                socketList.Add(ToCatalogSocket(door));

                // Vertical mate socket — X/Z = 0 always.
                var stair = AddStairSocket(root.transform, spec.stairType);
                socketList.Add(ToCatalogSocket(stair));

                var marker = new GameObject("Anchor_Center");
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = Vector3.zero;

                // Shape tag for baker / designer (empty GO name).
                var tag = new GameObject($"StairShape_{spec.shape}");
                tag.transform.SetParent(root.transform, false);

                RoomForgeMaterials.ApplyToRoomRoot(root, useAccentFloor: false);
                int bad = RoomForgeMaterials.VerifyRoomSurfaces(root, spec.id, false);
                if (bad > 0)
                    FlowTrace.Warn(Sys, $"room '{spec.id}' had {bad} non-stone surface slot(s) — corrected");

                string prefabPath = $"{RoomsFolder}/{spec.id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success)
                {
                    FlowTrace.Fail(Sys, $"failed to save prefab '{prefabPath}'");
                    return false;
                }

                built.Add(new RoomCatalogEntry
                {
                    id = spec.id,
                    prefabPath = prefabPath,
                    archetype = "hub",
                    themePalette = "default",
                    footprintCells = new[] { 1, 1 },
                    cellSize = Cell,
                    sockets = socketList,
                });

                FlowTrace.Step(Sys,
                    $"room saved id='{spec.id}' shape={spec.shape} stair={spec.stairType} " +
                    $"sockets={socketList.Count} rise={rise:F1} -> {prefabPath}");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── Floor / ceiling with shaft ───────────────────────────────────────

        /// <summary>
        /// Ownership-aware floor (contract §4 item 3 — "cut the hole").
        ///
        /// <para><b>_Up (lower room)</b> → SOLID. Contract §4.3 cuts the LOWER room's CEILING and
        /// the UPPER room's FLOOR; nothing passes through the lower floor, so a hole here is only
        /// a fall into void and a nav island.</para>
        ///
        /// <para><b>_Down (upper room)</b> → solid MINUS the shaft the mating flight arrives
        /// through. The cut rects are derived from the SAME <see cref="FlightPlan"/> the _Up room
        /// builds its legs from, so the hole cannot drift off the stair. A solid-floored landing
        /// is still a sealed floor — that is the part this comment exists to stop anyone
        /// "simplifying" away.</para>
        /// </summary>
        private static void BuildFloor(Transform parent, float hx, float hz,
            StairShape shape, RoomSocketType stairType, float rise)
        {
            float t = RoomForgeCanon.FloorSlabThickness;
            float y = -t * 0.5f;   // slab TOP face at local y = 0

            if (stairType != RoomSocketType.StairDown)
            {
                // Named exactly "Floor" and sized exactly to the footprint, matching
                // DefaultDungeonRoomsBuilder — that is the shape RoomForgeRegression case 11
                // [room-shell] asserts (FindChild "Floor", localScale == FootprintWorld).
                AddFloorSlab(parent, "Floor",
                    new Vector3(0f, y, 0f), new Vector3(hx * 2f, t, hz * 2f));
                FlowTrace.Step(Sys,
                    $"floor shape={shape} variant=Up -> SOLID {hx * 2f:F1}x{hz * 2f:F1}m " +
                    "(the flight stands on it; nothing passes through a lower room's floor)");
                return;
            }

            var cuts = PlaneCuts(shape, rise, hx, hz, rise);
            var pieces = new List<FloorRect> { new FloorRect { x0 = -hx, z0 = -hz, x1 = hx, z1 = hz } };
            foreach (var cut in cuts)
            {
                var next = new List<FloorRect>();
                foreach (var p in pieces) SubtractRect(p, cut, next);
                pieces = next;
            }

            int n = 0;
            float openArea = 0f;
            foreach (var p in pieces)
            {
                float w = p.x1 - p.x0;
                float d = p.z1 - p.z0;
                if (w < MinSlabExtent || d < MinSlabExtent) continue;
                AddFloorSlab(parent, $"Floor_Landing_{n:00}",
                    new Vector3((p.x0 + p.x1) * 0.5f, y, (p.z0 + p.z1) * 0.5f),
                    new Vector3(w, t, d));
                n++;
            }
            foreach (var c in cuts) openArea += (c.x1 - c.x0) * (c.z1 - c.z0);

            // The landing has to survive navmesh erosion or the stair top is an island.
            var nav = ReadNavAgent();
            float minWalk = nav.valid ? nav.radius * 2f : 0f;
            var legs = FlightPlan(shape, rise, hx, hz);
            Vector3 top = legs[legs.Count - 1].topNose;
            float clear = LandingClearance(top, hx, hz);
            string line =
                $"floor shape={shape} variant=Down -> LANDING slabs={n} shaftCuts={cuts.Count} " +
                $"openArea={openArea:F1}m2 topNose=(x{top.x:F2},z{top.z:F2}) " +
                $"landingClear={clear:F2}m minWalkable={minWalk:F2}m";
            if (minWalk > 0f && clear < minWalk)
                FlowTrace.Fail(Sys, line +
                    " -- LANDING TOO NARROW: navmesh erosion removes it, so the stair top will not " +
                    "connect to the rest of the room (expect PathPartial). The top nose sits too " +
                    "close to the wall -- shorten the flight's last leg.");
            else if (minWalk > 0f && clear < minWalk * 2f)
                FlowTrace.Warn(Sys, line + " -- landing is thin (under 2x the walkable slot)");
            else
                FlowTrace.Step(Sys, line);

            // Mirror of the ceiling note in BuildCeiling — say it in the log, not in someone's head.
            FlowTrace.Warn(Sys,
                $"floor shape={shape} variant=Down is MULTI-PIECE ('Floor_Landing_*'), so " +
                "RoomForgeRegression case 11 [room-shell] still reports \"has no 'Floor' child\". " +
                "That oracle resolves ONE child named exactly 'Floor' and asserts localScale == the " +
                "footprint exactly — which a floor with a stairwell shaft in it cannot be. Naming a " +
                "CONTAINER 'Floor' would not help: a container's localScale is 1x1, so it trades the " +
                "'no Floor child' failure for a 'floor spans 1x1' failure.");
        }

        /// <summary>Axis-aligned floor rectangle in room-local XZ.</summary>
        private struct FloorRect
        {
            public float x0, z0, x1, z1;
        }

        /// <summary>
        /// The openings a horizontal slab at <paramref name="planeY"/> must have so the flight can
        /// pass. ONE derivation, used against TWO planes:
        ///
        /// <list type="bullet">
        /// <item><b>the _Down room's FLOOR</b> — planeY = <c>rise</c> (its floor sits one floor
        /// above the _Up room's, which is where the flight terminates).</item>
        /// <item><b>the _Up room's CEILING</b> — planeY = <c>RoomForgeCanon.WallHeight</c>, which
        /// the flight crosses roughly two thirds of the way up and then leaves below it.</item>
        /// </list>
        ///
        /// <para>A leg only needs an opening once it has climbed to within one agent HEIGHT of the
        /// plane; below that the slab is more than head clearance away and blocks nothing (for the
        /// ceiling that is the player MODEL clipping through it — the ceiling carries no collider,
        /// so it never carves navmesh). Pulled back one more agent RADIUS so the capsule clears the
        /// slab edge.</para>
        ///
        /// <para>Past the LAST nose the flight is over, so the cut stops there — that is what
        /// preserves the landing (floor) and the roof (ceiling), and the ramp's LandingOverlap
        /// continues past it ABOVE the plane as the nav seam. At an INTERMEDIATE nose the flight
        /// turns, so the opening carries on around the corner or the outer corner of the turn is
        /// left capped over the walkable surface.</para>
        /// </summary>
        /// <summary>
        /// The openings this variant actually cuts, as <see cref="Rect"/> in local XZ, for
        /// <see cref="RoomPrefabMeta"/>. Calls the SAME <see cref="PlaneCuts"/> the geometry uses,
        /// against the SAME plane, so the declaration and the cut cannot disagree.
        ///
        /// <para>Which surface is cut depends on the variant, and getting this backwards is how a
        /// declaration silently stops matching:</para>
        /// <list type="bullet">
        /// <item><b>_Down</b> — a bare landing. Its FLOOR carries the hole the arriving flight
        /// comes up through (plane = <c>rise</c>). Its ceiling is SOLID: the flight terminates at
        /// this room's floor plane and nothing crosses above.</item>
        /// <item><b>_Up</b> — owns the flight. Its FLOOR is solid (the flight stands on it) and its
        /// CEILING carries the shaft (plane = <c>WallHeight</c>) because the flight climbs through
        /// its own roof on the way to the room above.</item>
        /// </list>
        /// </summary>
        private static List<Rect> DeclareShafts(StairShape shape, RoomSocketType stairType,
                                                float rise, float hx, float hz, bool isCeiling)
        {
            var declared = new List<Rect>();
            bool isDown = stairType == RoomSocketType.StairDown;

            // _Down cuts its FLOOR only; _Up cuts its CEILING only.
            if (isDown == isCeiling) return declared;

            float planeY = isCeiling ? RoomForgeCanon.WallHeight : rise;
            foreach (var c in PlaneCuts(shape, rise, hx, hz, planeY))
                declared.Add(new Rect(c.x0, c.z0, c.x1 - c.x0, c.z1 - c.z0));

            return declared;
        }

        private static List<FloorRect> PlaneCuts(StairShape shape, float rise, float hx, float hz, float planeY)
        {
            var nav = ReadNavAgent();
            float halfOpen = StairWidth * 0.5f + ShaftMargin;
            var legs = FlightPlan(shape, rise, hx, hz);
            var cuts = new List<FloorRect>();

            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                Vector3 d = leg.topNose - leg.bottomNose;
                float run = new Vector2(d.x, d.z).magnitude;
                if (run <= 0.001f || d.y <= 0.001f) continue;

                float back;
                if (!nav.valid)
                {
                    // SAFE fallback: open the whole leg. An UNDER-cut slab seals the stairwell,
                    // an over-cut one only opens more of a well that is open anyway.
                    back = run + LandingOverlap;
                }
                else
                {
                    float yClear = planeY - nav.height;     // below this the slab is out of the way
                    if (leg.topNose.y < yClear) continue;   // leg never reaches head clearance
                    float s = Mathf.Clamp01((yClear - leg.bottomNose.y) / d.y);
                    back = Mathf.Min(run * (1f - s) + nav.radius, run + LandingOverlap);
                }

                float forward = (i + 1 < legs.Count) ? halfOpen : 0f;

                Vector3 dir = new Vector3(d.x, 0f, d.z) / run;
                Vector3 perp = new Vector3(-dir.z, 0f, dir.x) * halfOpen;
                Vector3 from = leg.topNose - dir * back;
                Vector3 to = leg.topNose + dir * forward;
                Vector3 c0 = from + perp, c1 = from - perp;
                Vector3 c2 = to + perp, c3 = to - perp;
                cuts.Add(new FloorRect
                {
                    x0 = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x)),
                    x1 = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x)),
                    z0 = Mathf.Min(Mathf.Min(c0.z, c1.z), Mathf.Min(c2.z, c3.z)),
                    z1 = Mathf.Max(Mathf.Max(c0.z, c1.z), Mathf.Max(c2.z, c3.z)),
                });
            }
            return cuts;
        }

        /// <summary>
        /// Subtract <paramref name="cut"/> from <paramref name="r"/>, appending the (up to four)
        /// remaining rectangles. Splits north/south bands first, then the west/east flanks of the
        /// overlapping band, so the pieces never overlap each other.
        /// </summary>
        private static void SubtractRect(FloorRect r, FloorRect cut, List<FloorRect> outList)
        {
            if (cut.x1 <= r.x0 || cut.x0 >= r.x1 || cut.z1 <= r.z0 || cut.z0 >= r.z1)
            {
                outList.Add(r);
                return;
            }
            float cx0 = Mathf.Max(r.x0, cut.x0), cx1 = Mathf.Min(r.x1, cut.x1);
            float cz0 = Mathf.Max(r.z0, cut.z0), cz1 = Mathf.Min(r.z1, cut.z1);

            if (r.z0 < cz0) outList.Add(new FloorRect { x0 = r.x0, z0 = r.z0, x1 = r.x1, z1 = cz0 });
            if (cz1 < r.z1) outList.Add(new FloorRect { x0 = r.x0, z0 = cz1, x1 = r.x1, z1 = r.z1 });
            if (r.x0 < cx0) outList.Add(new FloorRect { x0 = r.x0, z0 = cz0, x1 = cx0, z1 = cz1 });
            if (cx1 < r.x1) outList.Add(new FloorRect { x0 = cx1, z0 = cz0, x1 = r.x1, z1 = cz1 });
        }

        /// <summary>
        /// Clear floor between the top nose and the wall it faces. Perimeter walls are centred on
        /// ±h with <see cref="RoomForgeCanon.WallThickness"/>, so their inner faces sit at
        /// ±(h − WallThickness/2).
        /// </summary>
        private static float LandingClearance(Vector3 topNose, float hx, float hz)
        {
            float inner = RoomForgeCanon.WallThickness * 0.5f;
            float byX = (hx - inner) - Mathf.Abs(topNose.x);
            float byZ = (hz - inner) - Mathf.Abs(topNose.z);
            return Mathf.Min(byX, byZ);
        }

        /// <summary>Live nav agent metrics — read, never re-typed.</summary>
        private struct NavAgent
        {
            public float radius;
            public float height;
            public float slopeDeg;
            public bool valid;
        }

        /// <summary>
        /// Read the Humanoid agent straight out of the project's nav settings
        /// (ProjectSettings/NavMeshAreas.asset, agentTypeID 0). The shaft hole is sized from
        /// agentHeight (head clearance) and agentRadius, so raising either widens every hole on the
        /// next BuildAll instead of silently leaving a head-height carve over the top of the
        /// flight. NEVER copy these numbers into this file — a copied oracle constant is exactly
        /// what RoomForgeCanon exists to prevent.
        /// </summary>
        private static NavAgent ReadNavAgent()
        {
            var a = new NavAgent();
            float radius = 0f, height = 0f, slope = 0f;
            Guard.Try(Sys, "read Humanoid nav agent settings (agentTypeID 0)", () =>
            {
                var s = UnityEngine.AI.NavMesh.GetSettingsByID(0);
                radius = s.agentRadius;
                height = s.agentHeight;
                slope = s.agentSlope;
            });
            a.radius = radius;
            a.height = height;
            a.slopeDeg = slope;
            a.valid = radius > 0f && height > 0f && slope > 0f;
            if (!a.valid)
                FlowTrace.Warn(Sys,
                    "nav agent settings unreadable (agentTypeID 0) — cutting the FULL flight " +
                    "footprint as the SAFE fallback (an under-cut floor seals the stairwell)");
            return a;
        }

        private static void AddFloorSlab(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = name;
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = localPos;
            floor.transform.localScale = scale;
            GameObjectUtility.SetStaticEditorFlags(floor,
                StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
        }

        /// <summary>
        /// WO-919 enclose pass, ownership-aware. The old version built a RING with a permanently
        /// open centre for BOTH variants — so every connector was open to sky over its middle, and
        /// the pieces were named <c>Ceil_*</c>, which is not the <c>Ceiling</c> child the shell
        /// oracle looks for. Both variants are wrong for different reasons, so they split:
        ///
        /// <list type="bullet">
        /// <item><b>_Down (the landing)</b> → SOLID. The arriving flight terminates AT this room's
        /// floor plane (local y = 0) and goes no higher, so nothing crosses this ceiling. One full
        /// slab named <c>Ceiling</c>, exactly the shape DefaultDungeonRoomsBuilder.BuildCeiling
        /// makes.</item>
        /// <item><b>_Up (the flight)</b> → SHAFT. The flight climbs local y = 0 → rise and passes
        /// straight through this plane at WallHeight, so the slab has to open along it.</item>
        /// </list>
        ///
        /// <para>No collider and BatchingStatic only, both variants — the NavMesh bakes from
        /// PhysicsColliders, so a collider here would voxelize into a walkable roof.</para>
        /// </summary>
        private static void BuildCeiling(Transform parent, float hx, float hz,
            StairShape shape, RoomSocketType stairType, float rise)
        {
            Guard.Try(Sys, "build stair-connector ceiling", () =>
            {
                float thick = RoomForgeCanon.CeilingThickness;
                float y = RoomForgeCanon.WallHeight + thick * 0.5f;   // underside flush with wall top
                float fullW = hx * 2f + RoomForgeCanon.WallThickness;
                float fullD = hz * 2f + RoomForgeCanon.WallThickness;

                if (stairType == RoomSocketType.StairDown)
                {
                    AddCeilingSlab(parent, "Ceiling",
                        new Vector3(0f, y, 0f), new Vector3(fullW, thick, fullD));
                    FlowTrace.Step(Sys,
                        $"ceiling shape={shape} variant=Down -> SOLID {fullW:F1}x{fullD:F1}m " +
                        $"underside y={RoomForgeCanon.WallHeight:F1} " +
                        "(the arriving flight stops at this room's floor plane, nothing crosses it)");
                    return;
                }

                var cuts = PlaneCuts(shape, rise, hx, hz, RoomForgeCanon.WallHeight);
                var pieces = new List<FloorRect>
                {
                    new FloorRect
                    {
                        x0 = -fullW * 0.5f, z0 = -fullD * 0.5f,
                        x1 = fullW * 0.5f,  z1 = fullD * 0.5f,
                    },
                };
                foreach (var cut in cuts)
                {
                    var next = new List<FloorRect>();
                    foreach (var p in pieces) SubtractRect(p, cut, next);
                    pieces = next;
                }

                int n = 0;
                float open = 0f;
                foreach (var p in pieces)
                {
                    float w = p.x1 - p.x0;
                    float d = p.z1 - p.z0;
                    if (w < MinSlabExtent || d < MinSlabExtent) continue;
                    AddCeilingSlab(parent, $"Ceiling_Shaft_{n:00}",
                        new Vector3((p.x0 + p.x1) * 0.5f, y, (p.z0 + p.z1) * 0.5f),
                        new Vector3(w, thick, d));
                    n++;
                }

                var rects = new StringBuilder();
                foreach (var c in cuts)
                {
                    open += (c.x1 - c.x0) * (c.z1 - c.z0);
                    rects.Append($" cut=x[{c.x0:F2}..{c.x1:F2}]z[{c.z0:F2}..{c.z1:F2}]");
                }
                FlowTrace.Step(Sys,
                    $"ceiling shape={shape} variant=Up -> SHAFT slabs={n} cuts={cuts.Count}{rects} " +
                    $"openArea={open:F1}m2 underside y={RoomForgeCanon.WallHeight:F1} " +
                    "(the flight passes through this plane)");

                // Say it in the bake log rather than leaving the next reader to rediscover it.
                FlowTrace.Warn(Sys,
                    $"ceiling shape={shape} variant=Up is MULTI-PIECE ('Ceiling_Shaft_*'), so " +
                    "RoomForgeRegression case 11 [room-shell] still reports \"has NO 'Ceiling' child\". " +
                    "That oracle resolves ONE child named exactly 'Ceiling' and asserts its localScale " +
                    "covers the footprint — which no ceiling with a stairwell in it can satisfy. The " +
                    "geometry here is correct; the oracle needs a coverage check, not a single-slab check.");
            });
        }

        private static void AddCeilingSlab(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = name;
            c.transform.SetParent(parent, false);
            c.transform.localPosition = localPos;
            c.transform.localScale = scale;
            var col = c.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            GameObjectUtility.SetStaticEditorFlags(c, StaticEditorFlags.BatchingStatic);
        }

        // ── Walls (snap door on South) ───────────────────────────────────────

        private static void BuildPerimeterWallsSnapSouth(Transform parent, float hx, float hz)
        {
            float wallH = RoomForgeCanon.WallHeight;
            float thick = RoomForgeCanon.WallThickness;
            float gap = RoomForgeCanon.DoorGap;

            // S = door gap (snap-on)
            BuildWallWithGap(parent, "Wall_S", new Vector3(0f, wallH * 0.5f, -hz),
                new Vector3(hx * 2f, wallH, thick), gap, alongX: true);
            // N / E / W solid
            BuildSolidWall(parent, "Wall_N", new Vector3(0f, wallH * 0.5f, hz),
                new Vector3(hx * 2f, wallH, thick));
            BuildSolidWall(parent, "Wall_E", new Vector3(hx, wallH * 0.5f, 0f),
                new Vector3(thick, wallH, hz * 2f));
            BuildSolidWall(parent, "Wall_W", new Vector3(-hx, wallH * 0.5f, 0f),
                new Vector3(thick, wallH, hz * 2f));
        }

        private static void BuildSolidWall(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = name;
            w.transform.SetParent(parent, false);
            w.transform.localPosition = localPos;
            w.transform.localScale = scale;
            GameObjectUtility.SetStaticEditorFlags(w, StaticEditorFlags.NavigationStatic);
            // WO-1837: same sight-blocker rule as DefaultDungeonRoomsBuilder.BuildSolidWall —
            // a stair-connector wall on Default is invisible to HeroTargetIndicator.HasLoS's
            // Structure-masked linecast. Note the RampCollider and Step_* pieces below are
            // DELIBERATELY left off Structure: they are horizontal/walkable, and the camera
            // occluder mask reads Structure too (SmartMobileCamera.cs:1323).
            DeNelle.Dungeons.RoomForge.DungeonStructureLayer.Apply(
                w, "stair-connector wall must block hero line-of-sight");
        }

        private static void BuildWallWithGap(Transform parent, string name, Vector3 center, Vector3 fullScale,
            float gap, bool alongX)
        {
            if (alongX)
            {
                float total = fullScale.x;
                float side = Mathf.Max(0.2f, (total - gap) * 0.5f);
                float z = center.z;
                float y = center.y;
                float thick = fullScale.z;
                float h = fullScale.y;
                BuildSolidWall(parent, name + "_L",
                    new Vector3(-(gap * 0.5f + side * 0.5f), y, z),
                    new Vector3(side, h, thick));
                BuildSolidWall(parent, name + "_R",
                    new Vector3(+(gap * 0.5f + side * 0.5f), y, z),
                    new Vector3(side, h, thick));
            }
        }

        // ── Stair geometry: visual steps + invisible ramp ────────────────────

        /// <summary>One leg of a flight, in ROOM-LOCAL space. y = 0 is the room's floor top.</summary>
        private struct FlightLeg
        {
            public string name;
            public Vector3 bottomNose;
            public Vector3 topNose;
            public int steps;
        }

        /// <summary>
        /// THE single source of the flight's plan geometry. The _Up room BUILDS these legs; the
        /// _Down room CUTS its floor hole from the same list. Two readers, one definition — which
        /// is the only reason the hole and the stair can be trusted to line up after an edit.
        /// </summary>
        private static List<FlightLeg> FlightPlan(StairShape shape, float rise, float hx, float hz)
        {
            var legs = new List<FlightLeg>();
            float z0 = -hz + EntryPadDepth;

            if (shape == StairShape.Vertical)
            {
                // Straight: bottom at the south entry pad, top at the north landing.
                legs.Add(new FlightLeg
                {
                    name = "Flight_Straight",
                    bottomNose = new Vector3(0f, 0f, z0),
                    topNose = new Vector3(0f, rise, hz - TopLandingDepth),
                    steps = StepsPerFlight,
                });
                return legs;
            }

            // Left / Right: half-rise up +Z to a mid landing, then half-rise out along ∓X.
            float halfRise = rise * 0.5f;
            float zMid = z0 + TurnRun;
            float xTop = shape == StairShape.Left ? -TurnRun : TurnRun;
            legs.Add(new FlightLeg
            {
                name = "Flight_A",
                bottomNose = new Vector3(0f, 0f, z0),
                topNose = new Vector3(0f, halfRise, zMid),
                steps = StepsPerFlight / 2,
            });
            legs.Add(new FlightLeg
            {
                name = "Flight_B",
                bottomNose = new Vector3(0f, halfRise, zMid),
                topNose = new Vector3(xTop, rise, zMid),
                steps = StepsPerFlight / 2,
            });
            return legs;
        }

        /// <summary>Planar slope of one leg, degrees.</summary>
        private static float LegSlopeDeg(FlightLeg leg)
        {
            Vector3 d = leg.topNose - leg.bottomNose;
            float run = new Vector2(d.x, d.z).magnitude;
            return run <= 0.001f ? 90f : Mathf.Atan2(Mathf.Abs(d.y), run) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// ONE OWNER (contract §5): only the StairUp room builds a flight.
        ///
        /// <para>The mate arithmetic leaves the pair stacked <c>Y_down = Y_up + FloorSeparationY</c>
        /// (see the file header). A flight authored from the room origin ascends one full floor, so
        /// the _Up room's flight lands exactly on the _Down room's floor plane — it IS the
        /// connection. The identical flight in the _Down room started one floor higher and climbed
        /// through its own ceiling into open air, while interpenetrating the first.</para>
        ///
        /// <para>So StairDown builds NOTHING here. It is a bare upper landing; the hole its arriving
        /// flight needs is cut by <see cref="BuildFloor"/>.</para>
        /// </summary>
        private static void BuildStairGeometry(Transform parent, StairShape shape,
            RoomSocketType stairType, float rise, float hx, float hz)
        {
            if (stairType == RoomSocketType.StairDown)
            {
                FlowTrace.Step(Sys,
                    $"stair geometry shape={shape} variant=Down -> LANDING, NO FLIGHT " +
                    $"(0 steps, 0 ramps; the mating StairUp room owns the {rise:F1}m flight, " +
                    "and this room's floor carries the hole it arrives through)");
                return;
            }

            var root = new GameObject("StairAssembly");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;

            // ── OWNER OFFSET, walked in the editor 2026-08-07 ─────────────────
            //  "the first floor needs entire stair assembly Y rotated 180"
            //  "so v_up stair assembly is y rotation 180"
            //
            //  NOTE THE SCOPE IS ALREADY CORRECT BY CONSTRUCTION: StairDown returns above
            //  without building an assembly at all (ONE OWNER, contract §5), so this rotation
            //  can only ever reach an _Up room. The owner's "v_up" and this code path are the
            //  same set — no per-variant guard is needed to honour it.
            //
            //  Applied to the ASSEMBLY ROOT, not to individual flights, so every part that
            //  belongs to the stair — steps, ramp collider, landing pads — turns together and
            //  stays registered to each other. Rotating the flights individually would spin
            //  each one about its own origin and pull the ramp off its own nose line.
            //
            //  WHY THE ROOT AND NOT THE PLAN: the FlightPlan is also what derives the floor
            //  and ceiling shaft cuts. Rotating the plan would move the stair and leave the
            //  holes behind. Rotating the root turns geometry and openings as one — the shafts
            //  are cut from the same plan and inherit this transform.
            //
            //  ⚠ THIS IS A UNIFORM CHANGE, and the owner verified it on the VERTICAL pair only
            //  (the first descent in dg_stair_rig). Left/Right could not be reached on that
            //  walk — their top landing is 0.80 m against a 1.00 m minimum walkable slot, so
            //  navmesh erosion removes it. If Left/Right turn out to need a DIFFERENT yaw, this
            //  becomes per-shape rather than a single constant. Do not assume it generalises
            //  until someone has actually walked them.
            const float AssemblyYaw = 180f;
            root.transform.localRotation = Quaternion.Euler(0f, AssemblyYaw, 0f);

            var legs = FlightPlan(shape, rise, hx, hz);
            float steepest = 0f;
            int steps = 0;
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                BuildFlight(root.transform, leg.name, leg.bottomNose, leg.topNose,
                    StairWidth, leg.steps, LandingOverlap, RampThickness);
                steepest = Mathf.Max(steepest, LegSlopeDeg(leg));
                steps += leg.steps;

                // Mid landing pad visual between two legs of a turn shape.
                if (i + 1 < legs.Count)
                {
                    AddVisualBox(root.transform, "Landing",
                        new Vector3(leg.topNose.x, leg.topNose.y + 0.05f, leg.topNose.z),
                        new Vector3(StairWidth + 0.4f, 0.1f, StairWidth + 0.4f),
                        collider: false);
                }
            }

            var nav = ReadNavAgent();
            string line =
                $"stair geometry shape={shape} variant=Up -> FLIGHT, rise={rise:F1}m legs={legs.Count} " +
                $"steps={steps} ramps={legs.Count} steepest={steepest:F1}deg " +
                $"(agent max {nav.slopeDeg:F0}deg) steps visual-only + ramp BoxCollider (no Plane)";
            if (nav.valid && steepest > nav.slopeDeg)
                FlowTrace.Fail(Sys, line +
                    " -- SLOPE EXCEEDS THE AGENT MAX: no navmesh will generate on this flight");
            else if (nav.valid && steepest > nav.slopeDeg - 5f)
                FlowTrace.Warn(Sys, line + " -- slope is within 5deg of the agent max");
            else
                FlowTrace.Step(Sys, line);
        }

        /// <summary>
        /// One flight from bottom nose to top nose. Visual steps under; invisible ramp on nose line.
        /// </summary>
        private static void BuildFlight(Transform parent, string name,
            Vector3 bottomNose, Vector3 topNose,
            float width, int stepCount, float landingOverlap, float rampThick)
        {
            var flight = new GameObject(name);
            flight.transform.SetParent(parent, false);

            Vector3 delta = topNose - bottomNose;
            float runLen = new Vector2(delta.x, delta.z).magnitude;
            float rise = delta.y;
            if (runLen < 0.1f) runLen = 0.1f;
            if (stepCount < 2) stepCount = 2;

            Vector3 runDir = new Vector3(delta.x, 0f, delta.z).normalized;
            if (runDir.sqrMagnitude < 0.01f) runDir = Vector3.forward;

            // Visual steps (no colliders).
            for (int i = 0; i < stepCount; i++)
            {
                float t0 = (i + 0f) / stepCount;
                float t1 = (i + 1f) / stepCount;
                float y = bottomNose.y + rise * t1; // tread top at end of step
                Vector3 mid = bottomNose + (topNose - bottomNose) * ((t0 + t1) * 0.5f);
                mid.y = y - 0.08f;
                float stepDepth = runLen / stepCount;
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"Step_{i:00}";
                step.transform.SetParent(flight.transform, false);
                step.transform.localPosition = mid;
                // Orient along run
                step.transform.localRotation = Quaternion.LookRotation(runDir, Vector3.up);
                step.transform.localScale = new Vector3(width, 0.16f, stepDepth * 0.95f);
                var sc = step.GetComponent<Collider>();
                if (sc != null) Object.DestroyImmediate(sc);
            }

            // Invisible ramp on nose line — extend past both ends for landing seam.
            Vector3 nose = (topNose - bottomNose);
            float noseLen = nose.magnitude;
            Vector3 noseDir = nose / Mathf.Max(0.01f, noseLen);
            float totalLen = noseLen + landingOverlap * 2f;
            Vector3 rampCenter = (bottomNose + topNose) * 0.5f;

            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "RampCollider";
            ramp.transform.SetParent(flight.transform, false);
            ramp.transform.localPosition = rampCenter;
            ramp.transform.localRotation = Quaternion.LookRotation(noseDir, Vector3.up);
            // LookRotation puts local Z along the nose line; cube depth (Z) = ramp length.
            ramp.transform.localScale = new Vector3(width * 0.98f, rampThick, totalLen);

            // Keep BoxCollider, strip render (HideMesh pattern).
            HideMesh(ramp);
            GameObjectUtility.SetStaticEditorFlags(ramp, StaticEditorFlags.NavigationStatic);

            // Bottom / top nose anchors (empty).
            var bn = new GameObject("BottomNose");
            bn.transform.SetParent(flight.transform, false);
            bn.transform.localPosition = bottomNose;
            var tn = new GameObject("TopNose");
            tn.transform.SetParent(flight.transform, false);
            tn.transform.localPosition = topNose;
        }

        private static void AddVisualBox(Transform parent, string name, Vector3 localPos, Vector3 scale, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (!collider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
        }

        private static void HideMesh(GameObject go)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) Object.DestroyImmediate(mr);
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) Object.DestroyImmediate(mf);
        }

        // ── Sockets ──────────────────────────────────────────────────────────

        private static RoomSocket AddDoorSocket(Transform parent, string facing, float hx, float hz)
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

            string id = $"{facing.ToLowerInvariant()}_door_01";
            var go = new GameObject($"Socket_{id}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = rot;
            var sock = go.AddComponent<RoomSocket>();
            sock.id = id;
            sock.type = RoomSocketType.Door;
            sock.facing = facing;
            sock.isSecret = false;
            sock.halfWidth = 1.1f;
            return sock;
        }

        private static RoomSocket AddStairSocket(Transform parent, RoomSocketType stairType)
        {
            float halfFloor = DungeonBakerChecks.FloorSeparationY * 0.5f;
            bool down = stairType == RoomSocketType.StairDown;

            var go = new GameObject($"Socket_stair_{stairType}");
            go.transform.SetParent(parent, false);
            // X/Z MUST stay 0 — grid invariant (see DefaultDungeonRoomsBuilder.AddStairSocket).
            go.transform.localPosition = new Vector3(0f, down ? -halfFloor : halfFloor, 0f);
            go.transform.localRotation = down
                ? Quaternion.LookRotation(Vector3.down, Vector3.forward)
                : Quaternion.LookRotation(Vector3.up, Vector3.forward);
            var sock = go.AddComponent<RoomSocket>();
            sock.id = down ? "stair_down_01" : "stair_up_01";
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

        // ── Catalog merge (do not wipe main kit) ─────────────────────────────

        private static void MergeCatalog(List<RoomCatalogEntry> built)
        {
            RoomCatalogFile file = null;
            if (File.Exists(CatalogPath))
            {
                try
                {
                    file = JsonConvert.DeserializeObject<RoomCatalogFile>(
                        File.ReadAllText(CatalogPath, Utf8NoBom));
                }
                catch (System.Exception ex)
                {
                    FlowTrace.Warn(Sys, $"rooms-catalog parse failed — starting fresh merge: {ex.Message}");
                }
            }

            if (file == null) file = new RoomCatalogFile { version = 1, rooms = new List<RoomCatalogEntry>() };
            if (file.rooms == null) file.rooms = new List<RoomCatalogEntry>();

            foreach (var entry in built)
            {
                file.rooms.RemoveAll(r => r != null && r.id == entry.id);
                file.rooms.Add(entry);
            }

            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            Guard.Try(Sys, "write rooms-catalog dual-copy (merge)", () =>
            {
                File.WriteAllText(CatalogPath, json, Utf8NoBom);
                File.WriteAllText(CatalogPathRes, json, Utf8NoBom);
            });
            FlowTrace.Step(Sys, $"catalog merge entries total={file.rooms.Count} added/updated={built.Count}");
        }

        private static void WriteReadme(List<ConnectorSpec> specs)
        {
            string path = Path.Combine(Application.dataPath, "Dungeon/Rooms/STAIR_CONNECTORS.md");
            var sb = new StringBuilder();
            sb.AppendLine("# Stair connector room prefabs (snap-on)");
            sb.AppendLine();
            sb.AppendLine("Built by `DefaultStairConnectorRoomsBuilder`.");
            sb.AppendLine();
            sb.AppendLine("| Prefab | Shape | Vertical socket | Snap door | Stair geometry |");
            sb.AppendLine("|--------|-------|-----------------|-----------|----------------|");
            foreach (var s in specs)
            {
                string geo = s.stairType == RoomSocketType.StairDown
                    ? "**none** — upper landing + floor hole"
                    : "**owns the full flight** + solid floor";
                sb.AppendLine($"| `{s.id}` | {s.shape} | {s.stairType} | S (`s_door_01`) | {geo} |");
            }
            sb.AppendLine();
            sb.AppendLine("## ⚠ One owner — do not put a flight in both");
            sb.AppendLine();
            sb.AppendLine("A StairDown socket sits `FloorSeparationY/2` BELOW its room origin and a StairUp");
            sb.AppendLine("socket the same distance ABOVE, so the composer stacks the mated pair as");
            sb.AppendLine("`Y_down = Y_up + FloorSeparationY`. A flight authored from the room origin ascends");
            sb.AppendLine("exactly one floor, so:");
            sb.AppendLine();
            sb.AppendLine("- the **`_Up`** room's flight spans `Y_up → Y_down` — it **is** the connection;");
            sb.AppendLine("- the same flight in the **`_Down`** room would span `Y_down → Y_down + 6`, i.e.");
            sb.AppendLine("  through its own ceiling into open air, interpenetrating the first.");
            sb.AppendLine();
            sb.AppendLine("So `_Up` owns the whole flight and gets a SOLID floor; `_Down` has no steps and no");
            sb.AppendLine("ramp, and its floor carries the HOLE the arriving flight comes up through. Both the");
            sb.AppendLine("flight and the hole are generated from one `FlightPlan()`.");
            sb.AppendLine();
            sb.AppendLine("## Snap-on use");
            sb.AppendLine("- Mate any corridor door to `s_door_01` (composer rotates the room).");
            sb.AppendLine("- Mate `stair_down_01` on an upper connector to `stair_up_01` on the lower.");
            sb.AppendLine("- **Pair the SAME shape** (`Vertical_Down` over `Vertical_Up`, …): the landing's hole");
            sb.AppendLine("  is cut for that shape's arrival point.");
            sb.AppendLine("- Walk surface = invisible ramp (BoxCollider); steps are visual only.");
            sb.AppendLine();
            sb.AppendLine("Rebuild: **Defenders → Dungeon → Build Stair Connector Room Prefabs**");
            sb.AppendLine("Batch: `DeNelle.Editor.RoomForge.DefaultStairConnectorRoomsBuilder.BuildAllBatch`");
            File.WriteAllText(path, sb.ToString(), Utf8NoBom);
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
