// =============================================================================
// IronBastionHardestRegression [iron-bastion-hardest]  Marker: IRON_BASTION_HARDEST_OK / _FAIL
// -----------------------------------------------------------------------------
// WO-1878. Source + JSON oracle (no bake, no PlayMode). Pins the hardest-raid
// identity so a spire-only towers[] / missing Landscape outer / empty Bastion
// dress cannot ship again:
//   * iron_bastion towers[] weights tower_ground_archer AND tower_arcane_spire
//   * ResolveTowerTypes filters by TurretRole (archer vs mage palettes)
//   * Extreme max-tier visual uses upgradeVisualPath + RepoProps.MaxStructureLevel
//   * outerEnclosure Landscape skips Outer WallSegment (ArenaBoundaryRing only)
//   * raidDress authored, kit != synty-castle (not garrison leftovers)
//   * simulated TypeSummary for 7+3 OverlappingFire names both real types
//   * revert towers[] to spire-only would leave the archer palette empty -> fallback
//     is still named, but the AUTHORING pin (both types present) goes RED
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class IronBastionHardestRegression
    {
        private const string ConfigsRes = "Assets/Resources/Data/Canonical/scene-configs.json";
        private const string ConfigsStream = "Assets/StreamingAssets/Data/Canonical/scene-configs.json";
        private const string GeneratorSrc = "Assets/Editor/WallTools/RaidBaseGenerator.cs";
        private const string DresserSrc = "Assets/Editor/WallTools/RaidBaseDresser.cs";
        private const string CatalogSrc = "Assets/_Modules/Village/World/SceneConfigCatalog.cs";

        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? "IRON_BASTION_HARDEST_OK :: " : "IRON_BASTION_HARDEST_FAIL :: ") + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                CaseBastionTowerPalette(failures, notes);
                CaseRoleSplitResolve(failures, notes);
                CaseMaxTierVisual(failures, notes);
                CaseLandscapeOuter(failures, notes);
                CaseBastionDress(failures, notes);
                CaseTwinParity(failures, notes);
                CaseSimulatedTypeSummary(failures, notes);
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
            reason = "iron-bastion-hardest " + string.Join("; ", notes);
            return true;
        }

        private static JObject LoadConfigs(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("missing " + path);
                return null;
            }
            try { return JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                failures.Add(path + " parse: " + ex.Message);
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

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static void CaseBastionTowerPalette(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(ConfigsRes, failures);
            if (root == null) return;
            var row = Row(root, "iron_bastion");
            if (row == null) { failures.Add("no iron_bastion row"); return; }

            if ((string)row["difficulty"] != "Extreme")
                failures.Add("iron_bastion difficulty='" + row["difficulty"] + "' expected Extreme");
            if ((string)row["towerPlacementStyle"] != "OverlappingFire")
                failures.Add("iron_bastion towerPlacementStyle must stay OverlappingFire");
            int archers = row["archerTowerCount"] != null ? (int)row["archerTowerCount"] : 0;
            int mages = row["mageTowerCount"] != null ? (int)row["mageTowerCount"] : 0;
            if (archers != 7 || mages != 3)
                failures.Add("iron_bastion placed totals must stay 7 archer + 3 mage (got " +
                             archers + "+" + mages + ")");

            var towers = row["towers"] as JArray;
            if (towers == null || towers.Count == 0)
            {
                failures.Add("iron_bastion towers[] empty - DefaultArcherTowerId only fires when empty, " +
                             "and a spire-only list fed BOTH palettes (WO-1878 defect)");
                return;
            }

            bool hasArcher = false, hasWizard = false;
            int archerWeight = 0, wizardWeight = 0;
            foreach (var t in towers)
            {
                string type = (string)t["type"];
                int count = Math.Max(0, (int)(t["count"] ?? 0));
                if (string.Equals(type, "tower_ground_archer", StringComparison.OrdinalIgnoreCase))
                {
                    hasArcher = true;
                    archerWeight += count;
                }
                if (string.Equals(type, "tower_arcane_spire", StringComparison.OrdinalIgnoreCase))
                {
                    hasWizard = true;
                    wizardWeight += count;
                }
            }
            if (!hasArcher)
                failures.Add("iron_bastion towers[] has no tower_ground_archer - Extreme would bake spire-only");
            if (!hasWizard)
                failures.Add("iron_bastion towers[] has no tower_arcane_spire - Extreme would bake archer-only");
            if (hasArcher && archerWeight < 1)
                failures.Add("tower_ground_archer weight < 1");
            if (hasWizard && wizardWeight < 1)
                failures.Add("tower_arcane_spire weight < 1");

            // Spire-only revert detector: a single-type towers[] that is ONLY the spire REDS.
            if (towers.Count == 1 && hasWizard && !hasArcher)
                failures.Add("iron_bastion towers[] is spire-only again - the WO-1878 palette defect");

            notes.Add("palette archerW=" + archerWeight + " wizardW=" + wizardWeight +
                      " place=" + archers + "+" + mages);
        }

        private static void CaseRoleSplitResolve(List<string> failures, List<string> notes)
        {
            string gen = TryRead(GeneratorSrc);
            if (gen == null) { failures.Add("cannot read " + GeneratorSrc); return; }
            if (gen.IndexOf("enum TurretRole", StringComparison.Ordinal) < 0)
                failures.Add("RaidBaseGenerator lost TurretRole - archer/mage palettes would merge again");
            if (gen.IndexOf("MatchesTurretRole", StringComparison.Ordinal) < 0)
                failures.Add("ResolveTowerTypes no longer Filters by MatchesTurretRole");
            if (gen.IndexOf("IsMageTowerType", StringComparison.Ordinal) < 0)
                failures.Add("IsMageTowerType missing - wizard ids cannot be classified");
            if (gen.IndexOf("ResolveTowerTypes(def, TurretRole.Archer", StringComparison.Ordinal) < 0)
                failures.Add("PlaceTowers does not resolve an Archer palette");
            if (gen.IndexOf("ResolveTowerTypes(def, TurretRole.Mage", StringComparison.Ordinal) < 0)
                failures.Add("PlaceTowers does not resolve a Mage palette");
            // Guard the old two-arg form coming back.
            if (gen.IndexOf("ResolveTowerTypes(def, DefaultArcherTowerId)", StringComparison.Ordinal) >= 0 ||
                gen.IndexOf("ResolveTowerTypes(def, DefaultMageTowerId)", StringComparison.Ordinal) >= 0)
                failures.Add("ResolveTowerTypes still called with the unfiltered two-arg form");
            notes.Add("role-split ResolveTowerTypes");
        }

        private static void CaseMaxTierVisual(List<string> failures, List<string> notes)
        {
            string gen = TryRead(GeneratorSrc);
            if (gen == null) { failures.Add("cannot read " + GeneratorSrc); return; }
            if (gen.IndexOf("RepoProps.MaxStructureLevel", StringComparison.Ordinal) < 0)
                failures.Add("Extreme max visual does not clamp to RepoProps.MaxStructureLevel");
            if (gen.IndexOf("VisualPathForLevel", StringComparison.Ordinal) < 0)
                failures.Add("ResolveTowerStats does not pick upgradeVisualPath via VisualPathForLevel");
            if (gen.IndexOf("upgradeVisualPath", StringComparison.Ordinal) < 0)
                failures.Add("StructRepo does not deserialize upgradeVisualPath");
            if (gen.IndexOf("AddComponent<PlacedStructure>", StringComparison.Ordinal) < 0)
                failures.Add("ArmTower does not stamp PlacedStructure.level - archer Tier VFX stays L1");
            if (gen.IndexOf("tier.Name == \"Extreme\"", StringComparison.Ordinal) < 0)
                failures.Add("max-tier visual is not gated on Extreme");
            notes.Add("max-tier Extreme visual");
        }

        private static void CaseLandscapeOuter(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(ConfigsRes, failures);
            if (root == null) return;
            var row = Row(root, "iron_bastion");
            if (row == null) { failures.Add("no iron_bastion row"); return; }
            if (!string.Equals((string)row["outerEnclosure"], "Landscape", StringComparison.OrdinalIgnoreCase))
                failures.Add("iron_bastion outerEnclosure must be Landscape (got '" +
                             row["outerEnclosure"] + "') - Wall_Outer_* would be targetable again");

            string gen = TryRead(GeneratorSrc);
            string dress = TryRead(DresserSrc);
            string catalog = TryRead(CatalogSrc);
            if (gen == null) { failures.Add("cannot read " + GeneratorSrc); return; }
            if (dress == null) { failures.Add("cannot read " + DresserSrc); return; }
            if (catalog == null) { failures.Add("cannot read " + CatalogSrc); return; }

            if (catalog.IndexOf("UsesLandscapeOuterEnclosure", StringComparison.Ordinal) < 0)
                failures.Add("SceneConfigDef lost UsesLandscapeOuterEnclosure");
            if (gen.IndexOf("def.outerEnclosure", StringComparison.Ordinal) < 0)
                failures.Add("RaidBaseGenerator does not skip Outer BuildRing for Landscape");
            if (gen.IndexOf("ArenaBoundaryRing", StringComparison.Ordinal) < 0)
                failures.Add("generator no longer builds ArenaBoundaryRing");
            if (dress.IndexOf("LandscapeOuter", StringComparison.Ordinal) < 0)
                failures.Add("RaidBaseDresser LayoutContext lost LandscapeOuter");
            if (dress.IndexOf("no Outer WallSegment clad", StringComparison.Ordinal) < 0)
                failures.Add("dresser does not skip Outer clad for Landscape");
            notes.Add("landscape outer enclosure");
        }

        private static void CaseBastionDress(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(ConfigsRes, failures);
            if (root == null) return;
            var row = Row(root, "iron_bastion");
            if (row == null) { failures.Add("no iron_bastion row"); return; }
            var dress = row["raidDress"] as JObject;
            if (dress == null)
            {
                failures.Add("iron_bastion has no raidDress - top tier would bake empty / kit-fallback");
                return;
            }
            string kit = (string)dress["kit"];
            if (string.IsNullOrEmpty(kit))
                failures.Add("iron_bastion raidDress.kit empty");
            if (string.Equals(kit, "synty-castle", StringComparison.OrdinalIgnoreCase))
                failures.Add("iron_bastion kit is synty-castle - that is garrison wood leftovers");
            var props = dress["props"] as JArray;
            int n = 0;
            if (props != null)
                foreach (var p in props)
                    n += Math.Max(0, (int)(p["count"] ?? 0));
            if (n < 12)
                failures.Add("iron_bastion raidDress.props count " + n + " < 12 - courtyard cover empty");
            if ((string)row["sceneName"] != "RaidBase_IronBastion")
                failures.Add("iron_bastion sceneName='" + row["sceneName"] +
                             "' expected RaidBase_IronBastion (not garrison)");
            notes.Add("dress kit=" + kit + " props=" + n);
        }

        private static void CaseTwinParity(List<string> failures, List<string> notes)
        {
            var a = LoadConfigs(ConfigsRes, failures);
            var b = LoadConfigs(ConfigsStream, failures);
            if (a == null || b == null) return;
            var ra = Row(a, "iron_bastion");
            var rb = Row(b, "iron_bastion");
            if (ra == null || rb == null)
            {
                failures.Add("iron_bastion missing from one of the Canonical twins");
                return;
            }
            string ta = ra["towers"] != null ? ra["towers"].ToString(Newtonsoft.Json.Formatting.None) : "";
            string tb = rb["towers"] != null ? rb["towers"].ToString(Newtonsoft.Json.Formatting.None) : "";
            if (!string.Equals(ta, tb, StringComparison.Ordinal))
                failures.Add("iron_bastion towers[] differs between Resources and StreamingAssets twins");
            if (!string.Equals((string)ra["outerEnclosure"], (string)rb["outerEnclosure"], StringComparison.Ordinal))
                failures.Add("iron_bastion outerEnclosure twin mismatch");
            bool dressA = ra["raidDress"] != null;
            bool dressB = rb["raidDress"] != null;
            if (dressA != dressB)
                failures.Add("iron_bastion raidDress present on only one twin");
            notes.Add("canonical twins match");
        }

        /// <summary>
        /// Mirror PlaceTowers' role-split indexing: 7 archer slots walk the archer palette,
        /// 3 mage slots walk the mage palette. TypeSummary must name BOTH real types.
        /// </summary>
        private static void CaseSimulatedTypeSummary(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(ConfigsRes, failures);
            if (root == null) return;
            var row = Row(root, "iron_bastion");
            if (row == null)
            {
                failures.Add("[A-missing-dependency] iron_bastion row missing from " + ConfigsRes);
                return;
            }

            var archerPal = new List<string>();
            var magePal = new List<string>();
            var towers = row["towers"] as JArray;
            if (towers != null)
            {
                foreach (var t in towers)
                {
                    string type = (string)t["type"];
                    int count = Math.Max(1, Math.Min(16, (int)(t["count"] ?? 1)));
                    if (string.IsNullOrEmpty(type)) continue;
                    bool mage = string.Equals(type, "tower_arcane_spire", StringComparison.OrdinalIgnoreCase);
                    var pal = mage ? magePal : archerPal;
                    for (int c = 0; c < count; c++) pal.Add(type);
                }
            }
            if (archerPal.Count == 0) archerPal.Add("tower_ground_archer");
            if (magePal.Count == 0) magePal.Add("tower_arcane_spire");

            int archers = row["archerTowerCount"] != null ? (int)row["archerTowerCount"] : 0;
            int mages = row["mageTowerCount"] != null ? (int)row["mageTowerCount"] : 0;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < archers + mages; i++)
            {
                bool isMage = i >= archers;
                int kindIndex = isMage ? (i - archers) : i;
                var palette = isMage ? magePal : archerPal;
                string typeId = palette[kindIndex % palette.Count];
                counts.TryGetValue(typeId, out int n);
                counts[typeId] = n + 1;
            }

            var summary = new StringBuilder();
            foreach (var kv in counts)
            {
                if (summary.Length > 0) summary.Append(", ");
                summary.Append(kv.Value).Append('x').Append(kv.Key);
            }
            string typeSummary = summary.ToString();
            if (typeSummary.IndexOf("tower_ground_archer", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("simulated TypeSummary missing tower_ground_archer: [" + typeSummary + "]");
            if (typeSummary.IndexOf("tower_arcane_spire", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("simulated TypeSummary missing tower_arcane_spire: [" + typeSummary + "]");
            if (!counts.ContainsKey("tower_ground_archer") || counts["tower_ground_archer"] != 7)
                failures.Add("expected 7x tower_ground_archer in TypeSummary, got [" + typeSummary + "]");
            if (!counts.ContainsKey("tower_arcane_spire") || counts["tower_arcane_spire"] != 3)
                failures.Add("expected 3x tower_arcane_spire in TypeSummary, got [" + typeSummary + "]");
            notes.Add("TypeSummary [" + typeSummary + "]");
        }
    }
}
