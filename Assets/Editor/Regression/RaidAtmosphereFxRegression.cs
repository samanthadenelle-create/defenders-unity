// =============================================================================
// RaidAtmosphereFxRegression [raid-atmosphere-fx]  Marker: RAID_ATMOSPHERE_FX_OK / _FAIL
// -----------------------------------------------------------------------------
// WO-1868 redirect. Source oracle (no PlayMode, no bake). Pins the bounce fixes:
//   * RaidGarrisonSpawner fog/storm PlayKey calls pass visibilityExempt: true
//   * VFXManager LoopRecord + Decide/ShouldHaveReleased carry visibilityExempt
//   * ArenaBoundaryRing.RockPaths does not list Dungeon_Pillar_Stone_Square /
//     M_21_Grey_Light_LPUP as palette picks; uses Assets/Resources/Arena/Rock_*
//   * RaidBaseGenerator no longer passes Dungeon_Wall_Stone as boundary backing
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class RaidAtmosphereFxRegression
    {
        private const string SpawnerSrc = "Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs";
        private const string VfxMgrSrc = "Assets/_Modules/Village/Vfx/VFXManager.cs";
        private const string BoundarySrc = "Assets/Editor/ArenaBoundaryRing.cs";
        private const string GeneratorSrc = "Assets/Editor/WallTools/RaidBaseGenerator.cs";

        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? "RAID_ATMOSPHERE_FX_OK :: " : "RAID_ATMOSPHERE_FX_FAIL :: ") + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                CaseSpawnerVisibilityExempt(failures);
                CaseVfxPolicyVisibilityExempt(failures);
                CaseRockPathsNoGreyPillar(failures);
                CaseNoDungeonWallBacking(failures);
            }
            catch (Exception ex)
            {
                failures.Add("threw: " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures);
                return false;
            }
            reason = "raid-atmosphere-fx visibilityExempt fog/storm + KayKit RockPaths + no Wall_Stone backing";
            return true;
        }

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static void CaseSpawnerVisibilityExempt(List<string> failures)
        {
            string src = TryRead(SpawnerSrc);
            if (src == null) { failures.Add("missing " + SpawnerSrc); return; }
            if (src.IndexOf("visibilityExempt: true", StringComparison.Ordinal) < 0)
                failures.Add("RaidGarrisonSpawner no longer passes visibilityExempt: true — " +
                             "PP_GroundFog would pool-SUSPEND off-camera again (WO-1868 bounce).");
            if (src.IndexOf("PP_GroundFog", StringComparison.Ordinal) < 0)
                failures.Add("RaidGarrisonSpawner dropped PP_GroundFog");
            if (src.IndexOf("PP_LightnigStormCloud", StringComparison.Ordinal) < 0)
                failures.Add("RaidGarrisonSpawner dropped PP_LightnigStormCloud");
            if (src.IndexOf("ApplyRaidSky", StringComparison.Ordinal) < 0 ||
                src.IndexOf("RestoreRaidSky", StringComparison.Ordinal) < 0)
                failures.Add("RaidGarrisonSpawner missing ApplyRaidSky/RestoreRaidSky " +
                             "(raid-only camera+ambient darken with hub restore).");
            // Comments may name WeatherManager as dormant; a live call is forbidden.
            if (src.IndexOf("WeatherManager.", StringComparison.Ordinal) >= 0)
                failures.Add("RaidGarrisonSpawner wires WeatherManager — forbidden (WO-992 dormant).");
        }

        private static void CaseVfxPolicyVisibilityExempt(List<string> failures)
        {
            string src = TryRead(VfxMgrSrc);
            if (src == null) { failures.Add("missing " + VfxMgrSrc); return; }
            if (src.IndexOf("VisibilityExempt", StringComparison.Ordinal) < 0)
                failures.Add("VFXManager.LoopRecord missing VisibilityExempt (WO-1868).");
            if (src.IndexOf("visibilityExempt", StringComparison.Ordinal) < 0)
                failures.Add("VfxLoopReleasePolicy.Decide missing visibilityExempt parameter.");
        }

        private static void CaseRockPathsNoGreyPillar(List<string> failures)
        {
            string src = TryRead(BoundarySrc);
            if (src == null) { failures.Add("missing " + BoundarySrc); return; }

            int pathsIdx = src.IndexOf("public static readonly string[] RockPaths",
                                       StringComparison.Ordinal);
            int pathsEnd = pathsIdx >= 0 ? src.IndexOf("};", pathsIdx, StringComparison.Ordinal) : -1;
            if (pathsIdx < 0 || pathsEnd < 0)
            {
                failures.Add("ArenaBoundaryRing.RockPaths block not found");
                return;
            }
            string block = src.Substring(pathsIdx, pathsEnd - pathsIdx);
            if (block.IndexOf("Dungeon_Pillar_Stone_Square", StringComparison.Ordinal) >= 0)
                failures.Add("RockPaths still lists Dungeon_Pillar_Stone_Square (WO-1758 grey box).");
            if (block.IndexOf("Dungeon_Pillar_Stone_Round", StringComparison.Ordinal) >= 0)
                failures.Add("RockPaths still lists Dungeon_Pillar_Stone_Round.");
            if (block.IndexOf("M_21_Grey_Light_LPUP", StringComparison.Ordinal) >= 0)
                failures.Add("RockPaths references M_21_Grey_Light_LPUP as a palette pick.");
            if (block.IndexOf("Assets/Resources/Arena/Rock_", StringComparison.Ordinal) < 0)
                failures.Add("RockPaths missing Assets/Resources/Arena/Rock_* (WO-1868 textured rim).");
        }

        private static void CaseNoDungeonWallBacking(List<string> failures)
        {
            string src = TryRead(GeneratorSrc);
            if (src == null) { failures.Add("missing " + GeneratorSrc); return; }
            // PlaceSquarePerimeter's last arg was "Fantasy_M/Dungeon_Wall_Stone.prefab".
            if (src.IndexOf("Dungeon_Wall_Stone.prefab", StringComparison.Ordinal) >= 0)
                failures.Add("RaidBaseGenerator still passes Dungeon_Wall_Stone backing " +
                             "(continuous grey panel behind the rim — WO-1868).");
        }
    }
}
