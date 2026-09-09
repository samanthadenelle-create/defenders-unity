// =============================================================================
// RaidBaseLayoutRegression [raid-base-layout]  Marker: RAID_BASE_LAYOUT_OK / _FAIL
// -----------------------------------------------------------------------------
// WO-1608/1609/1610/1611. Source + JSON oracle (no bake, no PlayMode). Pins:
//   * Easy raidDress is authored (no empty camp)
//   * Hard has an inner keep layer
//   * Generator dresses via RaidBaseDresser and loads StructureContent, not
//     Resources/Structures (the cylinder-turret miss)
//   * Spawner prefers GarrisonSlot_* when present
//   * Gate width floor is 3.5 m
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class RaidBaseLayoutRegression
    {
        private const string ConfigsRes = "Assets/Resources/Data/Canonical/scene-configs.json";
        private const string GeneratorSrc = "Assets/Editor/WallTools/RaidBaseGenerator.cs";
        private const string DresserSrc = "Assets/Editor/WallTools/RaidBaseDresser.cs";
        private const string SpawnerSrc = "Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs";

        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? "RAID_BASE_LAYOUT_OK :: " : "RAID_BASE_LAYOUT_FAIL :: ") + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                CaseEasyDress(failures, notes);
                CaseHardKeep(failures, notes);
                CaseExtremeKeep(failures, notes);
                CaseGeneratorWiresDresser(failures, notes);
                CaseArtLoadNotResourcesStructures(failures, notes);
                CaseSpawnerSlots(failures, notes);
                CaseGateWidth(failures, notes);
                CaseGarrisonWipeWins(failures, notes);
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
            reason = "raid-base-layout " + string.Join("; ", notes);
            return true;
        }

        private static JObject LoadConfigs(List<string> failures)
        {
            if (!File.Exists(ConfigsRes))
            {
                failures.Add("missing " + ConfigsRes);
                return null;
            }
            try { return JObject.Parse(File.ReadAllText(ConfigsRes)); }
            catch (Exception ex)
            {
                failures.Add("scene-configs parse: " + ex.Message);
                return null;
            }
        }

        private static JObject Row(JObject root, string id)
        {
            var arr = root["configs"] as JArray;
            if (arr == null) return null;
            foreach (var c in arr)
            {
                var o = c as JObject;
                if (o != null && (string)o["id"] == id) return o;
            }
            return null;
        }

        private static void CaseEasyDress(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(failures);
            if (root == null) return;
            var easy = Row(root, "raider_camp_small");
            if (easy == null) { failures.Add("no raider_camp_small row"); return; }
            var dress = easy["raidDress"] as JObject;
            if (dress == null) { failures.Add("Easy has no raidDress - the camp would still be a fence."); return; }
            var props = dress["props"] as JArray;
            int n = 0;
            if (props != null)
                foreach (var p in props)
                    n += Math.Max(0, (int)(p["count"] ?? 0));
            if (n < 12)
                failures.Add("Easy raidDress.props count " + n + " < 12 - courtyard would still be empty.");
            if ((string)dress["kit"] != "hexagon-green")
                failures.Add("Easy kit is '" + dress["kit"] + "' expected hexagon-green.");
            notes.Add("easy dress tokens=" + n);
        }

        private static void CaseHardKeep(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(failures);
            if (root == null) return;
            var hard = Row(root, "fortified_garrison");
            if (hard == null) { failures.Add("no fortified_garrison row"); return; }
            int layers = hard["interiorWallLayers"] != null ? (int)hard["interiorWallLayers"] : 0;
            if (layers < 1)
                failures.Add("Hard interiorWallLayers=" + layers + " - no choke/keep.");
            var dress = hard["raidDress"] as JObject;
            if (dress == null || (string)dress["kit"] != "synty-castle")
                failures.Add("Hard kit must be synty-castle (got " + (dress != null ? dress["kit"] : "null") + ").");
            notes.Add("hard keep layers=" + layers);
        }

        private static void CaseExtremeKeep(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(failures);
            if (root == null) return;
            var ext = Row(root, "mage_enclave");
            if (ext == null) { failures.Add("no mage_enclave row"); return; }
            var dress = ext["raidDress"] as JObject;
            if (dress == null || (string)dress["kit"] != "dungeon-stone")
                failures.Add("Extreme kit must be dungeon-stone.");
            notes.Add("extreme kit=" + (dress != null ? dress["kit"] : "none"));
        }

        private static void CaseGeneratorWiresDresser(List<string> failures, List<string> notes)
        {
            string gen = TryRead(GeneratorSrc);
            string dress = TryRead(DresserSrc);
            if (gen == null) { failures.Add("cannot read generator"); return; }
            if (dress == null) { failures.Add("cannot read dresser"); return; }
            if (gen.IndexOf("RaidBaseDresser.Dress", StringComparison.Ordinal) < 0)
                failures.Add("RaidBaseGenerator does not call RaidBaseDresser.Dress.");
            if (dress.IndexOf("Zone_Gatehouse", StringComparison.Ordinal) < 0)
                failures.Add("dresser does not author Zone_Gatehouse.");
            if (dress.IndexOf("Zone_Courtyard", StringComparison.Ordinal) < 0)
                failures.Add("dresser does not author Zone_Courtyard.");
            if (dress.IndexOf("GarrisonSlot_", StringComparison.Ordinal) < 0)
                failures.Add("dresser does not author GarrisonSlot_ markers.");
            if (gen.IndexOf("def.raidDress", StringComparison.Ordinal) < 0 &&
                dress.IndexOf("def.raidDress", StringComparison.Ordinal) < 0)
                failures.Add("raidDress has no consumer.");
            if (dress.IndexOf("def.props", StringComparison.Ordinal) < 0)
                failures.Add("props has no dresser consumer.");
            notes.Add("generator wires dresser");
        }

        private static void CaseArtLoadNotResourcesStructures(List<string> failures, List<string> notes)
        {
            string gen = TryRead(GeneratorSrc);
            string dress = TryRead(DresserSrc);
            if (gen == null) { failures.Add("cannot read " + GeneratorSrc); return; }
            if (dress == null) { failures.Add("cannot read " + DresserSrc); return; }
            if (gen.IndexOf("Resources.Load<GameObject>(plan.PrefabPath)", StringComparison.Ordinal) >= 0)
                failures.Add("PlaceTowerProp still Resources.Load(plan.PrefabPath) - that is the cylinder miss.");
            if (gen.IndexOf("RaidBaseDresser.LoadVisual", StringComparison.Ordinal) < 0)
                failures.Add("generator never calls RaidBaseDresser.LoadVisual.");
            if (dress.IndexOf("AssetRoots.StructureContent", StringComparison.Ordinal) < 0)
                failures.Add("dresser does not load Assets/StructureContent.");
            notes.Add("art load via dresser");
        }

        private static void CaseSpawnerSlots(List<string> failures, List<string> notes)
        {
            string src = TryRead(SpawnerSrc);
            if (src == null) { failures.Add("cannot read spawner"); return; }
            if (src.IndexOf("GarrisonSlot_", StringComparison.Ordinal) < 0)
                failures.Add("RaidGarrisonSpawner does not look for GarrisonSlot_.");
            if (src.IndexOf("seating=", StringComparison.Ordinal) < 0)
                failures.Add("spawner does not FlowTrace seating=slots|ring.");
            if (src.IndexOf("SetDefendPost", StringComparison.Ordinal) < 0)
                failures.Add("RaidGarrisonSpawner no longer SetDefendPost - guards fall through " +
                             "FindClosestTarget and rush the hero at staging.");
            notes.Add("spawner slots + defend post");
        }

        private static void CaseGateWidth(List<string> failures, List<string> notes)
        {
            string dress = TryRead(DresserSrc);
            string gen = TryRead(GeneratorSrc);
            if (dress == null) { failures.Add("cannot read " + DresserSrc); return; }
            if (gen == null) { failures.Add("cannot read " + GeneratorSrc); return; }
            if (dress.IndexOf("MinGateWidth = 3.5f", StringComparison.Ordinal) < 0)
                failures.Add("MinGateWidth is not 3.5f.");
            if (gen.IndexOf("RaidBaseDresser.MinGateWidth", StringComparison.Ordinal) < 0)
                failures.Add("BuildRing does not widen the gate to MinGateWidth.");
            notes.Add("gate width 3.5");
        }

        private static void CaseGarrisonWipeWins(List<string> failures, List<string> notes)
        {
            const string victorySrc = "Assets/_Modules/Village/World/Camps/RaidVictoryController.cs";
            const string scoringSrc = "Assets/_Modules/Village/Troops/RaidScoring.cs";
            string victory = TryRead(victorySrc);
            string scoring = TryRead(scoringSrc);
            if (victory == null) { failures.Add("cannot read " + victorySrc); return; }
            if (scoring == null) { failures.Add("cannot read " + scoringSrc); return; }
            if (victory.IndexOf("the raid is not over", StringComparison.Ordinal) >= 0)
                failures.Add("RaidVictoryController still treats garrison wipe as a milestone - the player would be left hitting an empty camp.");
            if (victory.IndexOf("HandleVictory(\"garrison wiped\")", StringComparison.Ordinal) < 0)
                failures.Add("HandleCleared no longer calls HandleVictory on garrison wipe.");
            if (scoring.IndexOf("_spawner != null && _spawner.Cleared", StringComparison.Ordinal) < 0)
                failures.Add("RaidScoring.RaidWon no longer treats a wiped garrison as a win.");
            notes.Add("garrison wipe wins");
        }

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }
    }
}
