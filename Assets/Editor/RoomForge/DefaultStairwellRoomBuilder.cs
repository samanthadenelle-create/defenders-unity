// =============================================================================
// DefaultStairwellRoomBuilder — WO-930. THE STAIRWELL IS ONE ROOM.
// -----------------------------------------------------------------------------
// Menu:  Defenders/Dungeon/Build Stairwell Room Prefab
// Batch: DeNelle.Editor.RoomForge.DefaultStairwellRoomBuilder.BuildAll
//
// THE OWNER'S DESIGN (2026-08-08, from her section drawing). One room volume:
//
//   +---------------------------------------------------------------+
//   |   S=========+           [ GAP ]           +==========S        |  <- UPPER: two partial
//   |    floor     \             |             /   floor            |     floors, gap between
//   |               \        staircase        /                     |
//   |   S=======================================================S   |  <- LOWER: full length
//   +---------------------------------------------------------------+
//        S = socket, on a floor EDGE, at EITHER level
//
// WHY THIS REPLACES THE _Up/_Down PAIR (WO-927 root cause, found by the owner by eye):
// the pair model needed THREE things in TWO prefabs to agree — a hole cut in one
// room's floor, a shaft cut in another's ceiling, and a flight in a third frame.
// Nothing enforced that agreement, and on 2026-08-08 it was measured broken: the
// flight was yawed 180 while the openings were not, so half the stairs in the game
// pointed at solid floor while every gate read green (matesFail=0).
//
// Here there is nothing to agree WITH:
//   * no floor hole      — the lower floor is solid and the stair lands ON it
//   * no ceiling shaft   — the upper level is PARTIAL; the stair rises through the GAP
//   * no pair, no vertical mate, no delta-yaw, no placement order
//   * no slab over the stair — clearance is the room, not a 0.36 m squeeze
//
// AND THE COMPOSER NEEDS NO CHANGE. A socket already carries its own local position
// INCLUDING Y, and SolveMate solves pos = pPos - rotatedSocket, so height resolves
// for free. An upper-level socket mates by the ordinary planar door path.
//
// RUN IS DERIVED, NEVER AUTHORED. Slope = atan(FloorSeparationY / run). The 45 deg
// agent maximum is a CLIFF, not a target — DefaultStairConnectorRoomsBuilder's own
// header records that at 45.0 "the ramp stops carving at all". We target ~27 deg.
//
// WALK SURFACE (same proven contract as the connector kit):
//   Visual steps = cubes, colliders DESTROYED.
//   Ramp = thin Cube on the nose line, BoxCollider KEPT, MeshRenderer stripped.
//   NEVER PrimitiveType.Plane.
//
// Cell / wall / door / floor metrics come from RoomForgeCanon and DungeonBakerChecks.
// NEVER re-typed here.
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Dungeons.RoomForge;

namespace DeNelle.Editor.RoomForge
{
    public static class DefaultStairwellRoomBuilder
    {
        private const string RoomsFolder = "Assets/Dungeon/Rooms";
        private const string Sys = "RoomForgeStairwell";

        /// <summary>The prefab stem, the root GameObject name, and RoomPrefabMeta.roomId — ONE source.
        /// Every shipping graph/layout references this exact string ("prefab": "StairwellRoom"), and
        /// DungeonBaker resolves a placement by that stem, so the three must never be able to drift
        /// apart. The siblings get this for free by threading spec.id through all of them.</summary>
        private const string RoomId = "StairwellRoom";

        // ── Footprint ────────────────────────────────────────────────────────
        // Claims TWO cells on the long axis and one on the short. The owner's rule:
        // "it is still limited to a room, but that room is owned as a stairs object,
        // which itself is four subrooms that you can make as large as you want."
        // So the GRID does not change — RoomForgeCanon.Cell stays 10 and stays EVEN
        // (GraphDungeonComposer's header: an odd cell puts sockets on halves and
        // RoundToInt quantises a unit of drift per stairwell) — the ROOM claims more.
        private const int CellsX = 2;
        private const int CellsZ = 1;

        /// <summary>Depth of each partial upper floor, measured in from its end wall.</summary>
        private const float UpperFloorDepth = 5f;

        /// <summary>Flat lower-floor pad between the bottom nose and the east doorway. Without it the
        /// flight terminates IN the wall and the door, with no walkable span between them.</summary>
        private const float BottomPadDepth = 3f;

        /// <summary>Tread width. Matches the connector kit so the two read as one family.</summary>
        private const float StairWidth = 2.4f;
        private const float RampThickness = 0.15f;
        private const int StepCount = 16;
        /// <summary>Ramp overshoot past each nose so the walk surface OVERLAPS its landing (the nav seam).</summary>
        private const float LandingOverlap = 0.35f;

        /// <summary>Hard ceiling on derived slope. The agent max is 45; we refuse anything near it.</summary>
        private const float MaxSlopeDeg = 40f;

        // ── Stair lighting. Colour and intensity are DungeonDresser's own values (:93, :73) so the
        // stairwell reads as the same space as the rooms it joins. Count is held low against the
        // URP per-object realtime cap of 4 (DeNelle-URP.asset L48).
        private static readonly Color FlameColor = new Color(1f, 0.62f, 0.28f);
        private const float TorchIntensity = 0.85f;
        private const int StairLightCount = 3;
        private const float StairLightRange = 7f;
        /// <summary>How far above the tread line each candle rides - head height, so it lights the
        /// steps ahead of you rather than glaring off the one you are standing on.</summary>
        private const float StairLightHeight = 1.8f;

        [MenuItem("Defenders/Dungeon/Build Stairwell Room Prefab")]
        public static void BuildAll()
        {
            using var _ = FlowTrace.Enter(Sys, "BuildAll");

            float hx = CellsX * RoomForgeCanon.Cell * 0.5f;   // 10
            float hz = CellsZ * RoomForgeCanon.Cell * 0.5f;   // 5
            float rise = DungeonBakerChecks.FloorSeparationY; // 6

            // ── The derivation. Run is what is LEFT once both upper floors are seated,
            // plus the reach under them. The flight starts at the inner edge of the WEST
            // upper floor and lands on the lower floor to the EAST, passing UNDER the east
            // upper floor — which is exactly what the owner's section drawing shows, and is
            // where the shallow angle comes from. Nothing here is a magic number: change the
            // footprint or UpperFloorDepth and the slope moves with it.
            float startX = -hx + UpperFloorDepth;             // inner edge of the west upper floor
            // Land CLEAR of the east wall, leaving a flat pad between the bottom nose and the doorway.
            //
            // The first cut of this ran to exactly +hx - i.e. the flight terminated IN the east wall,
            // in the same place as the s_lower_e socket - so the bottom of the stair and the door
            // occupied one spot with no floor between them. A walkable span needs somewhere to BE
            // before it becomes a doorway; the owner's own rule from the top of the flight applies
            // just as much at the bottom ("we need that edge").
            float run = (hx - BottomPadDepth) - startX;
            float slopeDeg = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

            if (slopeDeg > MaxSlopeDeg)
            {
                FlowTrace.Fail(Sys, $"STAIRWELL_BUILD_FAIL: derived slope {slopeDeg:F1} deg exceeds the " +
                    $"{MaxSlopeDeg} deg limit (run {run:F2} m over rise {rise:F2} m). The 45 deg agent " +
                    "maximum is a carve CLIFF, not a target - widen the footprint (CellsX) or shrink " +
                    "UpperFloorDepth. Refusing to build a stair the navmesh will fragment.");
                return;
            }

            // Same material path the rest of the kit uses. REUSED, never re-authored: a stairwell
            // that skins itself would drift from the room it opens into the first time the atlas
            // changes, and the owner's whole point is that this reads as one continuous space.
            RoomForgeMaterials.EnsureMenu();

            var go = new GameObject(RoomId);

            // ── CATALOG META — stamped FIRST, exactly as both sibling builders do
            //    (DefaultDungeonRoomsBuilder:269, DefaultStairConnectorRoomsBuilder:247).
            //
            //    WHY THIS IS NOT COSMETIC. DungeonBakerChecks.HalfExtents falls back to ONE canon
            //    cell when a placed room carries no meta (DungeonBakerChecks.cs:204). This room is
            //    TWO cells on X, so without the stamp the overlap gate measured a 20x10 m room as
            //    10x10 - and it is the only room in the kit that was missing it, in all four
            //    shipping dungeons. A neighbour could be seated straight through the west half of
            //    the stairwell and RoomsOverlap would never object. GraphDungeonComposer:574 was
            //    also emitting archetype=null for every stairwell node off the same gap.
            //
            //    footprintCells is DERIVED from the same two constants the shell is built from, so
            //    the declaration cannot drift from the geometry - re-typing (2,1) here is precisely
            //    the copy that goes stale the day CellsX changes and leaves the gate under-measuring
            //    again, silently.
            //
            //    archetype "hub": the existing value every vertical-circulation room in the kit
            //    already uses (StairDown/StairUp at DefaultDungeonRoomsBuilder:222/:231, all six
            //    stair connectors at DefaultStairConnectorRoomsBuilder:249). It is circulation, not
            //    content - DungeonBaker.LintPacing counts it as "other" rather than inflating the
            //    combat/lore/reward ratios, and RoomForgeMaterials only accents "reward"/"boss", so
            //    the skin is byte-identical to what this room already ships.
            //
            //    floorShafts / ceilingShafts stay EMPTY by default and must stay that way: the WO-930
            //    design cuts nothing, the flight rises through the GAP between the two upper floors.
            //
            //    ⚠ THIS DOES NOT FULLY FIX OVERLAP FOR THIS ROOM. RoomPrefabMeta has no vertical
            //    extent field, so DungeonBakerChecks.cs:190 still early-outs any two rooms more than
            //    half a floor apart in Y as non-overlapping. That is right for a one-storey room and
            //    WRONG for this one, which IS two storeys - a room seated on the upper level inside
            //    this stairwell's own volume is still invisible to the gate. Fixing it needs a
            //    vertical-extent API that does not exist yet (WO-930 §6 owns it). Do not assume the
            //    stamp below closed that hole.
            var meta = go.AddComponent<RoomPrefabMeta>();
            meta.roomId = RoomId;                       // the stem the graphs reference, by construction
            meta.archetype = "hub";
            meta.themePalette = "default";
            meta.footprintCells = new Vector2Int(CellsX, CellsZ);
            meta.cellSize = RoomForgeCanon.Cell;
            meta.occupiedLevels = 2;

            BuildLowerFloor(go.transform, hx, hz);
            BuildUpperFloors(go.transform, hx, hz, rise);
            BuildPerimeter(go.transform, hx, hz, rise);
            BuildFlight(go.transform, startX, run, rise);
            BuildStairLighting(go.transform, startX, run, rise);
            BuildSockets(go.transform, hx, rise);

            // Walls/floors/ceiling take the shared KayKit stone. The ramp is skipped for free -
            // its MeshRenderer is destroyed, and ApplyToRoomRoot only walks renderers.
            RoomForgeMaterials.ApplyToRoomRoot(go, useAccentFloor: false);

            int badSurfaces = RoomForgeMaterials.VerifyRoomSurfaces(go, RoomId, false);
            if (badSurfaces > 0)
                FlowTrace.Warn(Sys, $"{badSurfaces} surface(s) did not take the shared material - " +
                    "the stairwell will read as untextured next to the rooms it connects.");

            // The meta was added ABOVE, before this call. SaveAsPrefabAsset serialises the object
            // tree as it stands right now; a component added after this line would live only on the
            // throwaway scene object and be destroyed on the next line without ever reaching disk.
            string path = $"{RoomsFolder}/{RoomId}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();

            // Read the meta back OFF THE SAVED ASSET, not off the builder's own variable. The
            // variable would prove only that AddComponent was called - and it is a destroyed
            // UnityEngine.Object by this point anyway, so touching it here throws. Reading the asset
            // proves the thing that actually matters: the stamp SURVIVED serialisation onto the
            // prefab the composer will instantiate. That is the difference between a log line and a
            // proof. (Guarded, never assumed: a null here is the defect coming straight back.)
            var savedMeta = saved != null ? saved.GetComponent<RoomPrefabMeta>() : null;
            if (savedMeta == null)
            {
                FlowTrace.Fail(Sys, $"STAIRWELL_BUILD_FAIL: '{path}' saved with NO RoomPrefabMeta on its root. " +
                    "DungeonBakerChecks.HalfExtents would fall back to ONE cell for this TWO-cell room, so the " +
                    "overlap gate could not see a neighbour placed through it, and the composer would emit " +
                    "archetype=null for every stairwell node. Do NOT bake dungeons against this prefab.");
                return;
            }

            // The stamped footprint is stated here on purpose: an unlogged stamp is one nobody can
            // confirm landed from a bake log, and this metadata is invisible in play - it only ever
            // shows up as an overlap the gate failed to catch.
            FlowTrace.Step(Sys, $"STAIRWELL_BUILD_OK: {path} - footprint {hx * 2:F0}x{hz * 2:F0} m, " +
                $"meta roomId='{savedMeta.roomId}' archetype='{savedMeta.archetype}' " +
                $"footprintCells ({savedMeta.footprintCells.x},{savedMeta.footprintCells.y}) @ " +
                $"{savedMeta.cellSize:F0} m/cell = {savedMeta.FootprintWorld.x:F0}x" +
                $"{savedMeta.FootprintWorld.y:F0} m declared to the overlap gate, " +
                $"rise {rise:F1} m over run {run:F1} m = {slopeDeg:F1} deg " +
                $"({MaxSlopeDeg - slopeDeg:F1} deg of margin to the {MaxSlopeDeg} limit, " +
                $"{45f - slopeDeg:F1} to the agent cliff), {StepCount} steps, " +
                $"skinned with the shared RoomForge stone ({badSurfaces} bad surface(s)).");
        }

        /// <summary>Solid, FULL footprint, top face at local y = 0 — the same contract every
        /// other room honours, which is what lets an ordinary socket mate to it.</summary>
        private static void BuildLowerFloor(Transform parent, float hx, float hz)
        {
            AddBox(parent, "Floor_Lower",
                new Vector3(0f, -RoomForgeCanon.FloorSlabThickness * 0.5f, 0f),
                new Vector3(hx * 2f, RoomForgeCanon.FloorSlabThickness, hz * 2f), keepCollider: true);
        }

        /// <summary>TWO partial floors, one at each end, with the GAP between them. The gap is a
        /// deliberate structural element - it is the stairwell void, and the reason the stair
        /// never needs a hole cut through anything.</summary>
        private static void BuildUpperFloors(Transform parent, float hx, float hz, float rise)
        {
            float half = UpperFloorDepth * 0.5f;
            float y = rise - RoomForgeCanon.FloorSlabThickness * 0.5f;

            AddBox(parent, "Floor_Upper_W", new Vector3(-hx + half, y, 0f),
                new Vector3(UpperFloorDepth, RoomForgeCanon.FloorSlabThickness, hz * 2f), keepCollider: true);

            AddBox(parent, "Floor_Upper_E", new Vector3(hx - half, y, 0f),
                new Vector3(UpperFloorDepth, RoomForgeCanon.FloorSlabThickness, hz * 2f), keepCollider: true);
        }

        /// <summary>Perimeter walls tall enough to enclose BOTH levels, plus the ceiling above the
        /// upper floor. Door gaps are cut at BOTH levels on the two end walls, because a socket may
        /// sit at either one.</summary>
        private static void BuildPerimeter(Transform parent, float hx, float hz, float rise)
        {
            float top = rise + RoomForgeCanon.WallHeight;   // 10 m of enclosed volume
            float t = RoomForgeCanon.WallThickness;

            // Long side walls: solid, full height.
            AddBox(parent, "Wall_N", new Vector3(0f, top * 0.5f, hz), new Vector3(hx * 2f, top, t), true);
            AddBox(parent, "Wall_S", new Vector3(0f, top * 0.5f, -hz), new Vector3(hx * 2f, top, t), true);

            // End walls carry the door gaps - built as pillars either side of the gap, at each level.
            BuildEndWall(parent, "Wall_W", -hx, hz, t, top, rise);
            BuildEndWall(parent, "Wall_E", hx, hz, t, top, rise);

            AddBox(parent, "Ceiling", new Vector3(0f, top + RoomForgeCanon.CeilingThickness * 0.5f, 0f),
                new Vector3(hx * 2f, RoomForgeCanon.CeilingThickness, hz * 2f), keepCollider: false);
        }

        /// <summary>One end wall with a door gap at the LOWER level and another at the UPPER level.
        /// Built as flanking pillars + lintels rather than one slab, so both gaps are real openings.</summary>
        private static void BuildEndWall(Transform parent, string name, float x, float hz,
                                         float t, float top, float rise)
        {
            float gapHalf = RoomForgeCanon.DoorGap * 0.5f;
            float side = hz - gapHalf;                       // width of each flanking pillar
            float doorH = RoomForgeCanon.DoorGap;            // door opening height at each level

            // Flanking pillars, full height, both sides of the door line.
            AddBox(parent, $"{name}_L", new Vector3(x, top * 0.5f, hz - side * 0.5f),
                new Vector3(t, top, side), true);
            AddBox(parent, $"{name}_R", new Vector3(x, top * 0.5f, -hz + side * 0.5f),
                new Vector3(t, top, side), true);

            // Between the pillars: lintel above the lower door, the slab between the two doors,
            // and the lintel above the upper door. The two voids left are the openings.
            float lowerTop = doorH;
            float upperBottom = rise;
            float upperTop = rise + doorH;

            AddBox(parent, $"{name}_Mid", new Vector3(x, (lowerTop + upperBottom) * 0.5f, 0f),
                new Vector3(t, upperBottom - lowerTop, RoomForgeCanon.DoorGap), true);
            AddBox(parent, $"{name}_Head", new Vector3(x, (upperTop + top) * 0.5f, 0f),
                new Vector3(t, top - upperTop, RoomForgeCanon.DoorGap), true);
        }

        /// <summary>Visual steps (NO colliders) plus ONE invisible ramp Cube that carries the
        /// BoxCollider. This split is what makes the flight walkable: NavMeshSurface collects
        /// PhysicsColliders, so a stepped visual would rasterise as a saw and fragment.</summary>
        private static void BuildFlight(Transform parent, float startX, float run, float rise)
        {
            var flight = new GameObject("Flight");
            flight.transform.SetParent(parent, false);

            // NOTE: no rotation is applied to this container. WO-927's root cause was a 180 deg
            // yaw on exactly such a container, applied AFTER the openings were derived from the
            // same plan - so the flight turned and the openings did not. There is nothing to
            // rotate against here, and nothing should ever be added.

            float stepRun = run / StepCount;
            float stepRise = rise / StepCount;

            for (int i = 0; i < StepCount; i++)
            {
                float x = startX + stepRun * (i + 0.5f);
                float y = rise - stepRise * (i + 0.5f);
                AddBox(flight.transform, $"Step_{i:00}",
                    new Vector3(x, y, 0f), new Vector3(stepRun, stepRise, StairWidth),
                    keepCollider: false);            // VISUAL ONLY - the ramp is the walk surface
            }

            // The walk surface: one slab on the nose line, overshooting both ends so it OVERLAPS
            // each landing rather than meeting it exactly (an exact meeting is a seam the
            // voxeliser can drop).
            float len = Mathf.Sqrt(run * run + rise * rise) + LandingOverlap * 2f;
            var ramp = AddBox(flight.transform, "RampCollider",
                new Vector3(startX + run * 0.5f, rise * 0.5f, 0f),
                new Vector3(len, RampThickness, StairWidth), keepCollider: true);
            ramp.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-rise, run) * Mathf.Rad2Deg);

            var mr = ramp.GetComponent<MeshRenderer>();
            if (mr != null) Object.DestroyImmediate(mr);      // invisible, but solid
        }

        /// <summary>
        /// Candle lights down the flight itself (owner ask 2026-08-08: "add some candle lighting and
        /// stuff like that ... that with the candles would give a good ambiance").
        /// </summary>
        /// <remarks>
        /// WHY THE STAIRWELL LIGHTS ITSELF. DungeonDresser already seats torches in this room - the
        /// bake reports flameLights=8 for the probe - but it is FOOTPRINT-based: it derives corner
        /// anchors from the room's plan and knows nothing about a flight or an upper level. So the
        /// corners get lit and the descent, which is the whole point of the room, does not. A room
        /// that owns geometry no dresser can see has to own its lighting too.
        ///
        /// VALUES ARE THE DRESSER'S, NOT NEW ONES. FlameColor (1, 0.62, 0.28) and intensity 0.85 are
        /// read off DungeonDresser:93 and :73. Re-typing a different warm orange here is how a
        /// stairwell ends up looking like a different game to the corridor it opens into - the same
        /// reason the shell reuses RoomForgeMaterials instead of skinning itself.
        ///
        /// ⚠ COUNT IS DELIBERATELY SMALL. The URP per-object realtime cap is 4 (DeNelle-URP.asset
        /// L48) and every bake already reports "suppressed=4" against it. Three lights down a 12 m
        /// flight stay inside the budget; a candle per step would blow it and silently drop the ones
        /// that matter.
        /// </remarks>
        private static void BuildStairLighting(Transform parent, float startX, float run, float rise)
        {
            var root = new GameObject("StairLights");
            root.transform.SetParent(parent, false);

            for (int i = 0; i < StairLightCount; i++)
            {
                // Spread along the flight, inset from both noses so the landings stay lit by the
                // room's own corner torches rather than doubling up here.
                float t = (i + 1f) / (StairLightCount + 1f);
                float x = startX + run * t;
                float y = rise - rise * t + StairLightHeight;   // rides above the treads

                var go = new GameObject($"StairCandle_{i:00}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(x, y, 0f);

                var lt = go.AddComponent<Light>();
                lt.type = LightType.Point;
                lt.color = FlameColor;
                lt.intensity = TorchIntensity;
                lt.range = StairLightRange;
                lt.shadows = LightShadows.None;   // budget is for illumination, not shadow casting
            }

            FlowTrace.Step(Sys, $"stair lighting: {StairLightCount} candle point light(s) down the " +
                $"flight at intensity {TorchIntensity:F2}, range {StairLightRange:F1} m - colour and " +
                "intensity matched to DungeonDresser so the stairwell reads as the same space.");
        }

        /// <summary>Four sockets: both ends of the LOWER floor, both ends of the UPPER floors.
        /// Each carries its own Y, which SolveMate resolves for free - so an upper-level door
        /// mates through the ORDINARY planar path. This is why the composer needs no change.</summary>
        private static void BuildSockets(Transform parent, float hx, float rise)
        {
            // Outward points AWAY from the room (west sockets face -X = yaw 270, east +X = yaw 90),
            // because SolveMate mates a child's outward against the opposite of the parent's.
            AddSocket(parent, "s_lower_w", new Vector3(-hx, 0f, 0f), 270f, "W");
            AddSocket(parent, "s_lower_e", new Vector3(hx, 0f, 0f), 90f, "E");
            AddSocket(parent, "s_upper_w", new Vector3(-hx, rise, 0f), 270f, "W");
            AddSocket(parent, "s_upper_e", new Vector3(hx, rise, 0f), 90f, "E");
        }

        private static void AddSocket(Transform parent, string id, Vector3 localPos, float yaw, string facing)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var sock = go.AddComponent<RoomSocket>();
            sock.id = id;
            sock.type = RoomSocketType.Door;   // an ORDINARY door. No vertical socket type is used.
            sock.facing = facing;
            // Half the canon door gap, not the field's 1.0 default - a socket that under-reports its
            // width lets the mate checks accept a join narrower than the door actually is.
            sock.halfWidth = RoomForgeCanon.DoorGap * 0.5f;
        }

        private static GameObject AddBox(Transform parent, string name, Vector3 localPos,
                                         Vector3 localScale, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            else
            {
                // WO-1837: every solid box this builder keeps a collider on is a candidate sight
                // blocker, but only the WALLS may go on "Structure" — the discriminator rejects
                // Floor_*, Ceiling, Step_* and RampCollider by name, because the camera occluder
                // mask reads Structure too (SmartMobileCamera.cs:1323) and a horizontal occluder
                // would make the camera fade the ground the hero stands on. Matches Wall_N /
                // Wall_S and BuildEndWall's Wall*_L/_R/_Mid/_Head.
                if (DeNelle.Dungeons.RoomForge.DungeonStructureLayer.IsWallName(name))
                    DeNelle.Dungeons.RoomForge.DungeonStructureLayer.Apply(
                        go, "stairwell wall must block hero line-of-sight");
            }
            return go;
        }
    }
}
