// =============================================================================
// DungeonDoorShapeRegression [dungeon-door-shape] -- WO-1568.
// Marker: DUNGEON_DOOR_SHAPE_OK / DUNGEON_DOOR_SHAPE_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Dungeons, so it drives the
// REAL seam rather than a copy of it).
//
// WHAT THIS PINS AND WHY IT EXISTS
// The composed dungeon's working door used to be ONE PrimitiveType.Cube - the same
// primitive family the walls are made of - hung in an untrimmed full-height gap it
// was 1.6 m too short to fill. It read as a moving wall, and the single cue that it
// was not one was its COLOUR, which the owner (colourblind) cannot use. WO-1568
// replaced it with a framed doorway and an inset leaf.
//
// This oracle drives CommonDungeonDoor.BuildDoorVisual - the same static seam the
// runtime Start path and DungeonSceneCapture call - and asserts the SHAPE:
//   1. the frame exists (two jambs + a lintel) and is render-only,
//   2. the lintel closes the letterbox: it starts at the top of the leaf that was
//      actually built and reaches RoomForgeCanon.WallHeight,
//   3. the leaf is INSET - narrower than RoomForgeCanon.DoorGap,
//   4. the hinge pivot still sits on the jamb line at -halfWidth,
//   5. exactly ONE collider in the whole door, and it is the leaf's blocker,
//   6. the open build reaches CommonDungeonDoor.OpenAngle,
//   7. a door built on a ROTATED socket (E/W facings are yawed 90 degrees by
//      DefaultDungeonRoomsBuilder.AddSocket) measures the same - the trap a
//      post-parenting world-bounds read would fall into.
// It fails if the leaf ever returns to a bare full-gap-width slab.
//
// Every dimension is READ from RoomForgeCanon. A copied oracle constant is not an
// oracle (RoomForgeCanon.cs header) - nothing here re-types 2.2 or 4.0.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-door-shape suite", () => { if (!DeNelle.Editor.Regression.DungeonDoorShapeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-door-shape] " + r); });
// =============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Dungeons.RoomForge;

namespace DeNelle.Editor.Regression
{
    public static class DungeonDoorShapeRegression
    {
        private const float Eps = 0.02f;

        public static bool Run(out string reason)
        {
            var log = new StringBuilder();
            log.AppendLine("--- DUNGEON DOOR SHAPE (framed doorway, inset leaf, closed letterbox) ---");
            var failures = new List<string>();

            GameObject closedSocket = null;
            GameObject openSocket = null;
            GameObject rotatedSocket = null;

            try
            {
                float half = RoomForgeCanon.DoorGap * 0.5f;

                // -- Case 1-6: the closed door ------------------------------------
                closedSocket = NewSocket("~DoorShape_Closed", Quaternion.identity);
                var closed = CommonDungeonDoor.BuildDoorVisual(closedSocket.transform, half, false);

                Transform hinge = closedSocket.transform.Find(CommonDungeonDoor.HingeName);
                Transform jambL = closedSocket.transform.Find(CommonDungeonDoor.JambLeftName);
                Transform jambR = closedSocket.transform.Find(CommonDungeonDoor.JambRightName);
                Transform lintel = closedSocket.transform.Find(CommonDungeonDoor.LintelName);

                if (hinge == null) failures.Add("no hinge child - the door has no pivot");
                if (jambL == null || jambR == null)
                    failures.Add("frame incomplete: expected both " + CommonDungeonDoor.JambLeftName +
                                 " and " + CommonDungeonDoor.JambRightName +
                                 " - without jambs the opening is a hole, not a doorway");
                if (lintel == null)
                    failures.Add("no " + CommonDungeonDoor.LintelName +
                                 " - the 1.6 m see-through letterbox above the closed leaf is back");
                if (closed.Leaf == null) failures.Add("no leaf built");

                // The old defect, named so a revert cannot pass quietly.
                if (closedSocket.transform.Find("CommonDoor_Slab") != null)
                    failures.Add("CommonDoor_Slab is back - the door is a bare wall-family cube again");

                if (hinge != null && Mathf.Abs(hinge.localPosition.x + half) > Eps)
                    failures.Add($"hinge pivot moved off the jamb line: x={hinge.localPosition.x:0.###} " +
                                 $"expected {-half:0.###} (the leaf must swing from the frame edge)");

                float leafTop = closed.LeafTop;
                if (leafTop <= 0.1f) failures.Add($"leaf has no height (leafTop={leafTop:0.###})");

                // Inset: strictly narrower than the clear opening, or there is no reveal
                // and the leaf reads flush wall-to-wall exactly as it used to.
                if (closed.LeafWidth >= RoomForgeCanon.DoorGap - 0.05f)
                    failures.Add($"leaf is not inset: width={closed.LeafWidth:0.###} against " +
                                 $"DoorGap={RoomForgeCanon.DoorGap:0.###} - a flush leaf is the sliding-wall silhouette");

                if (lintel != null)
                {
                    float lintelBottom = lintel.localPosition.y - (lintel.localScale.y * 0.5f);
                    float lintelTop = lintel.localPosition.y + (lintel.localScale.y * 0.5f);

                    // MEASURED, not reported. lintelBottom is derived FROM DoorVisual.LeafTop, so
                    // comparing the two would be a tautology - the copied-oracle-constant shape in
                    // disguise. Read where the leaf's geometry actually tops out (socket is at
                    // identity, so world == socket-local) and pin the lintel to THAT.
                    float measuredTop = MeasuredTop(closed.Leaf);
                    if (measuredTop <= 0f) failures.Add("could not measure the leaf top - it has no renderers");
                    else
                    {
                        if (lintelBottom > measuredTop + Eps)
                            failures.Add($"gap above the leaf: lintel starts at {lintelBottom:0.###} but the " +
                                         $"leaf geometry tops out at {measuredTop:0.###} - the letterbox is open");
                        if (measuredTop > lintelBottom + Eps)
                            failures.Add($"the leaf tops out at {measuredTop:0.###}, above the lintel's " +
                                         $"{lintelBottom:0.###} - the leaf is buried in the header");
                    }
                    log.AppendLine($"  measured leaf top {measuredTop:0.###} m vs lintel bottom {lintelBottom:0.###} m");
                    if (lintelTop < RoomForgeCanon.WallHeight - Eps)
                        failures.Add($"lintel stops at {lintelTop:0.###}, short of RoomForgeCanon.WallHeight " +
                                     $"{RoomForgeCanon.WallHeight:0.###} - the opening can still be seen over");
                    if (lintel.localScale.x < RoomForgeCanon.DoorGap - Eps)
                        failures.Add($"lintel spans {lintel.localScale.x:0.###}, narrower than the " +
                                     $"{RoomForgeCanon.DoorGap:0.###} opening");
                    log.AppendLine($"  lintel {lintelBottom:0.##} -> {lintelTop:0.##} m over a leaf topping at {leafTop:0.##} m");
                }

                // Colliders: render-only frame, exactly one blocker on the leaf.
                int frameColliders = CountColliders(jambL) + CountColliders(jambR) + CountColliders(lintel);
                if (frameColliders != 0)
                    failures.Add($"{frameColliders} collider(s) on frame pieces - decorative trim must never " +
                                 "block the hero or demand a NavMesh re-bake");

                int leafColliders = closed.Leaf != null ? closed.Leaf.GetComponentsInChildren<Collider>(true).Length : -1;
                if (leafColliders != 1)
                    failures.Add($"leaf carries {leafColliders} collider(s), expected exactly 1 (the blocker SetOpen toggles)");
                if (closed.Blocker == null) failures.Add("DoorVisual.Blocker is null - SetOpen would toggle nothing");

                // WO-1829: leaf must be on Structure layer so LoS checks (HeroTargetIndicator.HasLoS)
                // can be blocked by the closed door. When SetOpen disables the collider, the layer
                // assignment stays static and the door becomes transparent to linecast checks.
                int structureLayer = LayerMask.NameToLayer("Structure");
                if (closed.Leaf != null && structureLayer >= 0)
                {
                    if (closed.Leaf.layer != structureLayer)
                        failures.Add($"leaf is on layer {closed.Leaf.layer} ('{LayerMask.LayerToName(closed.Leaf.layer)}'), " +
                                     $"expected Structure layer {structureLayer} (WO-1829 LoS gate)");
                }

                // A fallback leaf still has to be door-SHAPED: body + stile + two panels + handle.
                if (closed.LeafSource == "primitive-fallback" && closed.Leaf != null)
                {
                    int parts = closed.Leaf.GetComponentsInChildren<Renderer>(true).Length;
                    if (parts < 4)
                        failures.Add($"primitive fallback leaf has {parts} part(s) - a fallback that reproduces " +
                                     "the old flat slab is a failed fallback (needs stile + panel relief + handle)");
                }
                log.AppendLine($"  leaf source='{closed.LeafSource}' width={closed.LeafWidth:0.##} m " +
                               $"(gap {RoomForgeCanon.DoorGap:0.##} m), colliders leaf={leafColliders} frame={frameColliders}");

                // -- Case 7: the open door still reads as a doorway ---------------
                openSocket = NewSocket("~DoorShape_Open", Quaternion.identity);
                CommonDungeonDoor.BuildDoorVisual(openSocket.transform, half, true);
                Transform openHinge = openSocket.transform.Find(CommonDungeonDoor.HingeName);
                if (openHinge == null) failures.Add("open build produced no hinge");
                else
                {
                    float yaw = Mathf.DeltaAngle(0f, openHinge.localEulerAngles.y);
                    if (Mathf.Abs(Mathf.Abs(yaw) - CommonDungeonDoor.OpenAngle) > 1f)
                        failures.Add($"open build seated the hinge at {yaw:0.#} deg, expected " +
                                     $"{CommonDungeonDoor.OpenAngle:0.#}");
                }
                if (openSocket.transform.Find(CommonDungeonDoor.LintelName) == null ||
                    openSocket.transform.Find(CommonDungeonDoor.JambLeftName) == null)
                    failures.Add("open door lost its frame - the opening must still read as a doorway when open");

                // -- Case 8: a yawed socket (E/W facings) measures the same -------
                rotatedSocket = NewSocket("~DoorShape_Yawed", Quaternion.Euler(0f, 90f, 0f));
                var yawed = CommonDungeonDoor.BuildDoorVisual(rotatedSocket.transform, half, false);
                // MEASURED, not merely reported: on the unrotated socket the leaf spans world X;
                // on the 90-degree socket it spans world Z. If the builder ever reads bounds AFTER
                // parenting it hands back the leaf's DEPTH here and these two stop agreeing.
                float spanClosed = MeasureSpan(closed.Leaf, 0);
                float spanYawed = MeasureSpan(yawed.Leaf, 2);
                if (spanClosed <= 0f || spanYawed <= 0f)
                    failures.Add("could not measure the leaf - it has no renderers");
                else if (Mathf.Abs(spanYawed - spanClosed) > 0.05f)
                    failures.Add($"a yawed socket built a different leaf ({spanYawed:0.###} m vs " +
                                 $"{spanClosed:0.###} m) - bounds are being read after parenting");
                else if (spanClosed >= RoomForgeCanon.DoorGap - 0.05f)
                    failures.Add($"measured leaf spans {spanClosed:0.###} m against a " +
                                 $"{RoomForgeCanon.DoorGap:0.###} m opening - no reveal");
                if (Mathf.Abs(yawed.LeafTop - closed.LeafTop) > Eps)
                    failures.Add($"a yawed socket built a different leaf height ({yawed.LeafTop:0.###} vs " +
                                 $"{closed.LeafTop:0.###})");
                log.AppendLine($"  measured leaf span: unrotated {spanClosed:0.###} m / yawed {spanYawed:0.###} m");
            }
            catch (System.Exception ex)
            {
                failures.Add($"THREW {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Cleanup(closedSocket);
                Cleanup(openSocket);
                Cleanup(rotatedSocket);
            }

            if (failures.Count > 0)
            {
                reason = "dungeon-door-shape: " + string.Join(" | ", failures);
                Debug.LogError(log.ToString() + "DUNGEON_DOOR_SHAPE_FAIL: " + reason);
                return false;
            }

            reason = "framed doorway pinned: jambs + lintel to WallHeight, leaf inset inside DoorGap, " +
                     "hinge on the jamb line, one blocker, open angle reached, yaw-invariant.";
            Debug.Log(log.ToString() + "DUNGEON_DOOR_SHAPE_OK");
            return true;
        }

        private static GameObject NewSocket(string name, Quaternion rot)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetPositionAndRotation(Vector3.zero, rot);
            return go;
        }

        /// <summary>World-space span of the leaf along one world axis (0=x, 1=y, 2=z).</summary>
        private static float MeasureSpan(GameObject leaf, int axis)
        {
            if (leaf == null) return -1f;
            var renderers = leaf.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return -1f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.size[axis];
        }

        /// <summary>Top of the leaf's actual geometry, world space (== socket-local at identity).</summary>
        private static float MeasuredTop(GameObject leaf)
        {
            if (leaf == null) return -1f;
            var renderers = leaf.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return -1f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.max.y;
        }

        private static int CountColliders(Transform t) =>
            t == null ? 0 : t.GetComponentsInChildren<Collider>(true).Length;

        private static void Cleanup(GameObject go)
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
