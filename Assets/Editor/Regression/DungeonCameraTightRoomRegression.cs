// =============================================================================
// DungeonCameraTightRoomRegression [dungeon-cam-958] - locks the WO-958 contract.
// -----------------------------------------------------------------------------
// Owner F8 seq 2289 ("the camera is fighting me hard in here ... its auto rotating
// and it needs to keep more focus to the room as well as my direction"). WO-958's
// answer has three seams, and this suite guards each so a later tuning pass cannot
// silently reopen the fight:
//
//   A. NUMBERS (compile-checked, read straight off DungeonCameraProfile - the one
//      authority): the recenter stays a lazy idle drift (input owns yaw), the
//      small-room seat is genuinely tighter, every seat rotated to the pitch cap
//      still clears the WO-919 ceiling slab, and the seat transition is smoothed.
//   B. BINDING (source-lint on SmartMobileCamera): the room-aware seat / ceiling
//      clamp / heartbeat / recenter override + exact village restore are all
//      dungeon-gated and actually wired into LateUpdate.
//   C. ROOM SENSE (source-lint): the Dungeons-side publisher feeds the Core
//      blackboard using the ONE shared bounds math (DungeonRoomBounds.Compute,
//      WO-797) - never a second copy of the room measurement.
//
// FEEL is owner felt-verify (canon 08-09) - this only guards wiring + numbers.
// Edit-mode source-lint + const math, no PlayMode. Wired into DataRegression.RunAll.
// Never throws.
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using P = DeNelle.Core.World.DungeonCameraProfile;

namespace DeNelle.Editor
{
    public static class DungeonCameraTightRoomRegression
    {
        public static bool Run(out string reason)
        {
            var fails = new List<string>();

            // -- A. The numbers (live consts - recompiles keep this honest) ----
            float ceilingBudget = P.CeilingHeightRef - P.CeilingClearance;

            // (1) Every dungeon seat, rotated up to the pitch cap, stays under the
            //     ceiling budget - the geometric core of "no wall-clip pops vs the
            //     WO-919 slab". Same rotation math the camera applies (Euler pitch
            //     about X on an offset (0, h, -d)).
            float rad = P.PanPitchMax * Mathf.Deg2Rad;
            float stdSeatY   = P.CameraHeight * Mathf.Cos(rad) + P.CameraDistance * Mathf.Sin(rad);
            float smallSeatY = P.SmallRoomCameraHeight * Mathf.Cos(rad) + P.SmallRoomCameraDistance * Mathf.Sin(rad);
            if (stdSeatY > ceilingBudget)
                fails.Add($"standard seat at PanPitchMax rises to {stdSeatY:F2}m > ceiling budget {ceilingBudget:F2}m - pitch cap or seat must come down");
            if (smallSeatY > ceilingBudget)
                fails.Add($"small-room seat at PanPitchMax rises to {smallSeatY:F2}m > ceiling budget {ceilingBudget:F2}m");
            if (P.CeilingClearance <= 0f)
                fails.Add("CeilingClearance must be positive - 0 lets the lens touch the slab");

            // (2) The small-room seat is TIGHTER, not just different, and it fits a
            //     one-cell room (boom shorter than the half-extent of the smallest room).
            if (P.SmallRoomCameraDistance >= P.CameraDistance)
                fails.Add($"SmallRoomCameraDistance {P.SmallRoomCameraDistance} must be < CameraDistance {P.CameraDistance}");
            if (P.SmallRoomCameraDistance >= P.CellSizeRef * 0.5f)
                fails.Add($"SmallRoomCameraDistance {P.SmallRoomCameraDistance} does not fit a {P.CellSizeRef}m cell (needs < half-extent)");
            if (P.SmallRoomCameraHeight <= P.LookAtHeight)
                fails.Add("SmallRoomCameraHeight must sit above LookAtHeight or the raised-pitch downtilt inverts");

            // (3) The small-room classifier catches 1-cell rooms and NOT 2x2 halls.
            if (P.SmallRoomMaxExtent < P.CellSizeRef)
                fails.Add($"SmallRoomMaxExtent {P.SmallRoomMaxExtent} misses a 1-cell ({P.CellSizeRef}m) room");
            if (P.SmallRoomMaxExtent >= P.CellSizeRef * 2f)
                fails.Add($"SmallRoomMaxExtent {P.SmallRoomMaxExtent} would classify a 2x2 hall as small");

            // (4) Input owns yaw: the dungeon recenter must stay a LAZY IDLE DRIFT -
            //     the village whip was 0.4s / 220deg/s / stiffness 4 (the exact numbers
            //     the owner felt as "auto rotating"). Bands, not equalities, so tuning
            //     inside the calm zone never trips this.
            if (P.FacingRecenterDelay < 1f)
                fails.Add($"FacingRecenterDelay {P.FacingRecenterDelay}s < 1s - a pause would swing the seat again (the seq-2289 fight)");
            if (P.FacingRecenterMaxSpeed > 120f)
                fails.Add($"FacingRecenterMaxSpeed {P.FacingRecenterMaxSpeed}deg/s > 120 - back into whip territory");
            if (P.FacingRecenterStiffness > 2.5f)
                fails.Add($"FacingRecenterStiffness {P.FacingRecenterStiffness} > 2.5 - back into whip territory");

            // (5) Room seat changes are TRANSITIONS, and the facing focus stays a
            //     sub-metre bias (whipping guard) inside a narrow pitch band.
            if (P.RoomSeatSmoothTime < 0.2f)
                fails.Add($"RoomSeatSmoothTime {P.RoomSeatSmoothTime}s < 0.2 - a room change would read as a snap");
            if (P.FacingLookAhead >= 1.5f)
                fails.Add($"FacingLookAhead {P.FacingLookAhead}m >= 1.5 - a quick spin would whip the aim");
            if (P.PanPitchMax > 30f || P.PanPitchMin < -10f)
                fails.Add($"dungeon pitch band [{P.PanPitchMin},{P.PanPitchMax}] wider than the enclosed-room envelope");

            // -- B. The binding (SmartMobileCamera actually uses all of it) ----
            string smc = ReadOrFail("_Modules/Village/Hero/SmartMobileCamera.cs", fails);
            string pub = ReadOrFail("_Modules/Dungeons/DungeonRoomSensePublisher.cs", fails);
            string sense = ReadOrFail("_Modules/Core/World/DungeonRoomSense.cs", fails);
            if (fails.Count > 0) return Verdict(fails, out reason);

            // ⛔ WO-1772: THE TWO GATES BELOW TEST DUNGEON-GATING, NOT THE FLAG'S SPELLING. They used to
            // require the literal `_dungeonProfileActive`, which is why WO-1765 could not perform its R1
            // rename and had to write the heartbeat call site as two identical branches rather than one
            // `||` — the lint pinned the exact dungeon-only gate that ruling widened to raids. The flag
            // is now resolved from `CameraSceneProfile.Dungeon` (DungeonFpvRegression.DungeonActiveFlagName,
            // one copy, called not re-typed), so a rename is free and a behaviour change is still red.
            string dgFlag = DungeonFpvRegression.DungeonActiveFlagName(smc);
            if (dgFlag == null)
            {
                fails.Add("SmartMobileCamera has no dungeon-active flag derived from CameraSceneProfile.Dungeon " +
                          "(expected `<flag> = <want> == CameraSceneProfile.Dungeon;`) — the room-seat and " +
                          "heartbeat gates below cannot be judged, so this suite fails rather than pass vacuously");
                return Verdict(fails, out reason);
            }
            string F = Regex.Escape(dgFlag);

            // The room-aware seat stays a SINGLE-flag dungeon gate: an open raid arena publishes no rooms,
            // so widening this to `||` is the leak CameraRaidFramingRegression.cs:381-393 forbids.
            if (!Regex.IsMatch(smc, @"if\s*\(\s*" + F + @"\s*\)\s*\n\s*zoomOffset\s*=\s*DungeonRoomSeat\(dt\)"))
                fails.Add($"SmartMobileCamera: room-aware seat (DungeonRoomSeat) is not gated into LateUpdate " +
                          $"behind the single dungeon flag ({dgFlag})");
            if (!smc.Contains("DungeonCam.CeilingHeightRef - DungeonCam.CeilingClearance"))
                fails.Add("SmartMobileCamera: the ceiling backstop clamp is gone");

            // The heartbeat must be GATED (never unconditional — it would flood the town log and evict the
            // boot window from the logcat ring), and the dungeon must be one of the gates. Every call site
            // is collected, so the two-branch shape, a single `||`, and either operand order all pass;
            // deleting the guard, or dropping the dungeon from it, does not.
            var beats = Regex.Matches(smc, @"(?:else\s+)?if\s*\(([^)]*)\)\s*\n\s*Emit\w*Heartbeat\(dt\)");
            if (beats.Count == 0)
                fails.Add("SmartMobileCamera: the [Flow:Camera] WO-958 heartbeat is not wired into LateUpdate " +
                          "behind an `if (<profile flag>)` guard — an ungated heartbeat floods the town log");
            else
            {
                bool dungeonGated = false;
                foreach (Match b in beats)
                    if (Regex.IsMatch(b.Groups[1].Value, @"\b" + F + @"\b")) dungeonGated = true;
                if (!dungeonGated)
                    fails.Add($"SmartMobileCamera: the [Flow:Camera] WO-958 heartbeat is wired, but no call " +
                              $"site is gated on the dungeon flag ({dgFlag}) — dungeons would go silent, " +
                              "which is the WO-958 evidence gap this suite exists to prevent");
            }
            if (!Regex.IsMatch(smc, @"_facingRecenterDelay\s*=\s*DungeonCam\.FacingRecenterDelay"))
                fails.Add("SmartMobileCamera: the dungeon profile no longer re-tunes the facing-recenter (auto-rotate fight returns)");
            if (!Regex.IsMatch(smc, @"_facingRecenterDelay\s*=\s*_villageFacingRecenterDelay"))
                fails.Add("SmartMobileCamera: leaving a dungeon no longer restores the village recenter tuning (town camera contaminated)");
            if (!Regex.IsMatch(smc, @"_panPitch\s*=\s*Mathf\.Clamp\(_panPitch,\s*_panPitchMin,\s*_panPitchMax\)"))
                fails.Add("SmartMobileCamera: entering a dungeon does not clamp a carried-over pitch into the dungeon band");
            if (!smc.Contains("leadTarget += face.normalized * DungeonCam.FacingLookAhead"))
                fails.Add("SmartMobileCamera: dungeon facing-focus look-ahead is gone");
            if (Regex.IsMatch(smc, @"FacingLookAhead[^\n]*heroVelFlat|heroVelFlat[^\n]*FacingLookAhead"))
                fails.Add("SmartMobileCamera: facing look-ahead must bias by FACING, never velocity (the curl/spiral edge)");

            // -- C. The room sense seam (one math, one blackboard) -------------
            if (!pub.Contains("DungeonRoomBounds.Compute"))
                fails.Add("DungeonRoomSensePublisher must measure rooms with the ONE shared DungeonRoomBounds.Compute (WO-797) - no second copy");
            if (!pub.Contains("DungeonRoomSense.Publish") || !pub.Contains("DungeonRoomSense.Clear"))
                fails.Add("DungeonRoomSensePublisher must both Publish room sets and Clear them on leaving");
            if (!sense.Contains("namespace DeNelle.Core.World"))
                fails.Add("DungeonRoomSense must live in Core (the only assembly both Village and Dungeons may reference)");
            // A real reference is a using-directive or a fully-qualified member (dot after
            // the namespace) - prose mentions in comments must not trip this.
            if (Regex.IsMatch(smc, @"using\s+DeNelle\.Dungeons|DeNelle\.Dungeons\."))
                fails.Add("SmartMobileCamera must NEVER reference DeNelle.Dungeons (circular asmdef) - room data flows through Core");

            return Verdict(fails, out reason);
        }

        private static string ReadOrFail(string rel, List<string> fails)
        {
            string p = Path.Combine(Application.dataPath, rel);
            if (!File.Exists(p)) { fails.Add("source not found: " + rel + " - re-point this oracle"); return string.Empty; }
            return File.ReadAllText(p);
        }

        private static bool Verdict(List<string> fails, out string reason)
        {
            if (fails.Count == 0)
            {
                Debug.Log("DUNGEON_CAM_958_OK");
                reason = "TIGHT-ROOM CAMERA OK - recenter is a lazy idle drift (input owns yaw), " +
                         "small-room seat tighter and ceiling-safe at the pitch cap, room-aware seat + " +
                         "ceiling clamp + heartbeat wired dungeon-gated with exact village restore, " +
                         "room sense published through Core via the one WO-797 bounds math " +
                         "(FEEL = owner felt-verify)";
                return true;
            }
            reason = "dungeon-cam-958: " + string.Join("; ", fails);
            Debug.LogError("DUNGEON_CAM_958_FAIL: " + reason);
            return false;
        }
    }
}
