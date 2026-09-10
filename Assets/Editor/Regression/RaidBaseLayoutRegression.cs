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
//   * WO-1633: courtyard props are authored COVER (colliders kept) and placed as
//     clusters on concentric cover rings, and the bake states a per-config props count
//   * WO-1635: raid props have exactly ONE authority. The dresser reads raidDress.props
//     and nothing else (the props.set fallback + the hardcoded kit set are retired), and
//     no scene-config row may author props in both schemas or leave raidDress.props empty
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
                CaseOnePropReader(failures, notes);
                CaseSinglePropAuthority(failures, notes);
                CaseArtLoadNotResourcesStructures(failures, notes);
                CaseSpawnerSlots(failures, notes);
                CaseGateWidth(failures, notes);
                CaseGarrisonWipeWins(failures, notes);
                CaseCourtyardCoverRings(failures, notes);
                CaseGateReadsAsAGate(failures, notes);      // WO-1689
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
            notes.Add("generator wires dresser");
        }

        /// <summary>
        /// WO-1635 acceptance #1/#2 - the INVERSE of what this suite used to demand.
        /// Until 2026-09-10 CaseGeneratorWiresDresser reded when the string "def.props" was ABSENT
        /// from the dresser ("props has no dresser consumer"), i.e. the suite REQUIRED the legacy
        /// reader that the ticket exists to retire. Deleting the fallback would have reded the
        /// suite and read as the implementing lane's own regression, so the pin moves here and
        /// flips: the dresser must have exactly ONE prop reader, `raidDress.props`, and neither
        /// legacy reader may come back.
        /// </summary>
        private static void CaseOnePropReader(List<string> failures, List<string> notes)
        {
            string dress = TryRead(DresserSrc);
            if (dress == null) { failures.Add("cannot read dresser"); return; }

            if (dress.IndexOf("def.raidDress.props", StringComparison.Ordinal) < 0)
                failures.Add("dresser no longer reads def.raidDress.props - the ONE authored prop " +
                             "authority has no consumer, so every raid courtyard dresses empty.");
            if (dress.IndexOf("def.props", StringComparison.Ordinal) >= 0)
                failures.Add("dresser reads the legacy props.set block again (found \"def.props\") - " +
                             "WO-1635 retired it. Two authorities let one prop be authored twice in " +
                             "two schemas with different zones and counts; scene-configs.json " +
                             "raidDress.props is the only authority.");
            if (dress.IndexOf("DefaultProps", StringComparison.Ordinal) >= 0)
                failures.Add("dresser carries a hardcoded kit prop set again (found \"DefaultProps\") - " +
                             "WO-1635 deleted it. A C# fallback silently substitutes drifted content " +
                             "for an unauthored row instead of exposing it.");
            notes.Add("one prop reader");
        }

        /// <summary>
        /// WO-1635 acceptance #3 - the JSON half of the same invariant. Every row that authors a
        /// `raidDress` block must author props there and ONLY there. Iterates the live rows rather
        /// than a hardcoded id list, so a raid row added mid-edit is covered the day it lands.
        /// </summary>
        private static void CaseSinglePropAuthority(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(failures);
            if (root == null) return;
            var arr = root["configs"] as JArray;
            if (arr == null) { failures.Add("scene-configs has no configs array"); return; }

            int rows = 0;
            foreach (var c in arr)
            {
                var o = c as JObject;
                if (o == null) continue;
                var dress = o["raidDress"] as JObject;
                if (dress == null) continue;   // not a dressed raid row
                rows++;
                string id = (string)o["id"];

                var authored = dress["props"] as JArray;
                int instances = 0;
                if (authored != null)
                    foreach (var p in authored)
                        instances += Math.Max(0, (int)(p["count"] ?? 0));
                if (instances <= 0)
                    failures.Add("raid row '" + id + "' has a raidDress block but authors no " +
                                 "raidDress.props - since WO-1635 there is no fallback, so its " +
                                 "courtyard bakes EMPTY dirt.");

                var legacy = o["props"] as JObject;
                var set = legacy != null ? legacy["set"] as JArray : null;
                if (set != null && set.Count > 0)
                    failures.Add("raid row '" + id + "' authors props in BOTH schemas: legacy " +
                                 "props.set carries " + set.Count + " token(s) (" + (string)set[0] +
                                 " ...) while raidDress.props carries " + instances + " instance(s). " +
                                 "The legacy block has had no reader since WO-1635 - delete it from " +
                                 "scene-configs.json so the row states one intent.");
            }
            if (rows == 0) failures.Add("no scene-config row authors a raidDress block at all.");
            notes.Add("prop authority rows=" + rows);
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

        // =====================================================================
        //  WO-1633 - the courtyard is COVER on RINGS, not decor on a circle.
        //
        //  Owner, 2026-09-10, verbatim: "i have mentioned it in testing that it feels
        //  incomplete and not polished" (raid arena / bases), and on the fix shape:
        //  "similar strategy as we used in battle arena".
        //
        //  RED-FIRST, and it really was red on the tree this was written against:
        //   * no raid row authored `cover` at all - the field did not exist, so every
        //     prop in every camp shipped with `stripColliders: true` (RaidBaseDresser
        //     :567 as it stood), against WO-1609:104 "Colliders on" and WO-1610:112
        //     "collider stays";
        //   * the dresser had no CoverRingPlacer call - courtyard props sat on ONE
        //     annulus with no jitter and no scale variance, against WO-1609:99
        //     "Place as clusters, not a ring of singles";
        //   * no bake log had ever stated a props count, because the only dresser line
        //     is an aggregate that also counts clad panels, floor tiles and the gate.
        //
        //  DELIBERATELY NOT ASSERTED: a courtyard DENSITY floor. WO-1611:96 makes the
        //  Extreme enclave "spare on purpose" - a blanket density rule would red a
        //  camp that is correct by its own spec. Hard's thin census is WO-1634's.
        // =====================================================================
        private static void CaseCourtyardCoverRings(List<string> failures, List<string> notes)
        {
            var root = LoadConfigs(failures);
            if (root == null) return;

            string[] raidIds = { "raider_camp_small", "fortified_garrison", "mage_enclave" };
            int coverRows = 0;
            for (int i = 0; i < raidIds.Length; i++)
            {
                var row = Row(root, raidIds[i]);
                if (row == null) { failures.Add("no " + raidIds[i] + " row"); continue; }
                var dress = row["raidDress"] as JObject;
                var props = dress != null ? dress["props"] as JArray : null;
                if (props == null || props.Count == 0)
                {
                    failures.Add(raidIds[i] + " authors no raidDress.props - the courtyard would be empty dirt.");
                    continue;
                }

                int cover = 0;
                foreach (var p in props)
                    if (p["cover"] != null && (bool)p["cover"]) cover++;
                if (cover == 0)
                    failures.Add(raidIds[i] + " authors " + props.Count + " prop row(s) but NONE with cover:true - " +
                                 "every crate and barrel in that camp is walk-through scenery, not cover " +
                                 "(WO-1607 section 6 'Cover stacks ... with colliders').");
                coverRows += cover;
            }

            string dresser = TryRead(DresserSrc);
            if (string.IsNullOrEmpty(dresser))
            {
                failures.Add("cannot read " + DresserSrc);
                return;
            }

            if (!dresser.Contains("CoverRingPlacer."))
                failures.Add("RaidBaseDresser does not call CoverRingPlacer - courtyard props are still " +
                             "evenly spaced on one annulus with no jitter (WO-1609:99 'clusters, not a ring of singles').");

            if (!dresser.Contains("stripColliders: !p.cover"))
                failures.Add("RaidBaseDresser does not gate stripColliders on the authored cover flag - " +
                             "props are still stripped unconditionally.");

            if (!dresser.Contains("[RaidBaseDresser] props '"))
                failures.Add("RaidBaseDresser emits no per-config props count line - the only dresser log is the " +
                             "aggregate that also counts clad panels and floor tiles, so a courtyard that placed " +
                             "ZERO props reads identically to one that worked.");

            if (!dresser.Contains("StagingMarkerName") || !dresser.Contains("DefenseTower"))
                failures.Add("RaidBaseDresser does not read the staging marker and the turrets out of the built tree - " +
                             "prop exclusions would be hardcoded radii, and staging is on the SOUTH-WEST diagonal " +
                             "for two of the three camps.");

            notes.Add("courtyard cover rows=" + coverRows);
        }

        // =====================================================================
        //  WO-1689 - THE GATE MUST READ AS A GATE IN THE WALL IT SITS IN.
        //
        //  ⚠ WHY THIS CASE EXISTS. WO-1637 raised the hexagon-green base wall from the
        //  1.10 m `barrier` rail to the 4.00 m dungeon `wall` box, and NOTHING anywhere
        //  compared the gate to the wall - so a 1.41 m gate in a ~3.8 m wall, with 1.11 m
        //  flank towers now SHORTER than the wall they flank, would have shipped silently.
        //  The gate's own bake line printed the OPENING's width and the art's NAME, never a
        //  height, so no log could have caught it either.
        //
        //  It also found a defect WO-1637 did NOT cause: `mage_enclave` has been pairing the
        //  same 1.41 m hexagon gate with a 4.00 m `wall_cracked` wall all along.
        //
        //  SHAPE: pure arithmetic on measured mesh boxes + the tokens read out of source and
        //  JSON. No bake, no PlayMode - the same idiom as
        //  RaidArenaShapeRegression.CaseArenaBoundaryDesignedFit, whose header explains how a
        //  measured-mesh constant is sourced and why it must be re-pointed in the same edit as
        //  any module change. Follow that, not a threshold tweak, if this ever reds.
        // =====================================================================

        /// <summary>
        /// Mesh heights in metres, measured 2026-09-10 from the FBX vertex extents with the
        /// importer's unit conversion applied (<c>UnitScaleFactor/100 * scaleFactor</c> when
        /// <c>useFileScale</c>), times the prefab's own transform scale.
        /// <para/>
        /// ⚠ These are the ONLY numbers in this case that come from outside the source tree.
        /// The measuring script's CONTROL run reproduces the 2026-09-10 raid bake log exactly
        /// (that log printed <c>piece 2.42m ... reach 1.82m</c> at applied scale 1.08 for a
        /// palette the script measures at 2.24 / 3.38 m), so the method is validated against a
        /// real bake rather than asserted. **Re-point them in the same edit as any module swap.**
        /// <para/>
        /// A token that is NOT in this table is NOTED and SKIPPED, never failed: the art packs
        /// are gitignored (CLAUDE.md sec.4) and a fresh clone must not red on absent art.
        /// </summary>
        private static readonly Dictionary<string, float> MeasuredHeightM =
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                // KayKit Dungeon Remastered 1.1 - the wall vocabulary with mass
                { "wall",                          4.00f },
                { "wall_broken",                   4.00f },
                { "wall_cracked",                  4.00f },
                { "wall_gated",                    4.00f },
                { "wall_doorway",                  4.00f },
                { "wall_pillar",                   4.00f },
                { "wall_half",                     4.00f },
                // KayKit Dungeon Remastered 1.1 - the barrier family, ALL of it knee-high
                { "barrier",                       1.10f },
                { "barrier_half",                  1.10f },
                { "barrier_column",                1.40f },
                { "barrier_corner",                1.40f },
                // KayKit Medieval Hexagon 1.0.1 - a hex-TILE kit, authored at tile scale
                { "wall_straight",                 1.10f },
                { "wall_straight_gate",            1.41f },
                { "building_watchtower_green",     1.11f },
                { "building_tower_A_green",        2.19f },
                // Synty PolygonFantasyKingdom - authored as a matched set, and it passes
                { "SM_Bld_Castle_Wall_01",         5.00f },
                { "SM_Bld_Castle_Wall_Gate_01",    5.86f },
                { "SM_Bld_Castle_Wall_Tower_S_01", 7.52f },
                { "SM_Bld_Castle_Wall_Tower_M_01", 7.52f },
            };

        /// <summary>
        /// The gate must reach this share of the wall module's height. Not 1.0: a gate arch
        /// legitimately sits a little under its wall, and the Synty set (5.86 / 5.00 = 1.17)
        /// shows a kit author going the other way. 0.80 fails the 0.35 the hexagon gate
        /// actually scores by a wide margin and would still pass a deliberately squat gate.
        /// </summary>
        private const float GateHeightFloorOfWall = 0.80f;

        /// <summary>
        /// A flank tower must be at LEAST the wall's height. A tower shorter than its own wall
        /// is the specific thing the owner's frame showed. Synty scores 1.50 here.
        /// </summary>
        private const float FlankHeightFloorOfWall = 1.00f;

        /// <summary>
        /// Pull the token a kit-branch returns out of source. Handles BOTH shapes the dresser
        /// uses: <c>if (kit == "x") return "tok";</c> and the ternary chain
        /// <c>kit == "x" ? "tok" : ...</c>. The LAST quoted string in the body is the
        /// fall-through default, which is what `hexagon-green` always takes - neither
        /// `DefaultGate` nor `DefaultWall` names it explicitly.
        /// </summary>
        private static string KitToken(string body, string kit)
        {
            if (string.IsNullOrEmpty(body)) return null;
            int at = body.IndexOf("kit == \"" + kit + "\"", StringComparison.Ordinal);
            if (at >= 0)
            {
                // Start PAST the kit literal's own closing quote: `kit == "` is 8 chars,
                // then the kit name, then the closing quote. Starting one short of that
                // finds the closing quote itself and returns the text between the branches
                // (`) return ` / ` ? `) instead of the token - which is how the first draft
                // of this helper silently mis-read every explicit branch.
                int after = at + 8 + kit.Length + 1;
                int q = after <= body.Length ? body.IndexOf('"', after) : -1;
                if (q >= 0)
                {
                    int e = body.IndexOf('"', q + 1);
                    if (e > q) return body.Substring(q + 1, e - q - 1);
                }
                return null;
            }
            // fall-through default = the last quoted string in the body
            int last = body.LastIndexOf('"');
            if (last <= 0) return null;
            int start = body.LastIndexOf('"', last - 1);
            return start >= 0 ? body.Substring(start + 1, last - start - 1) : null;
        }

        /// <summary>Slice out a method or statement body by a start marker and a terminator.</summary>
        private static string Slice(string src, string from, string to)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(to, a, StringComparison.Ordinal);
            return b < 0 ? src.Substring(a) : src.Substring(a, b - a);
        }

        private static void CaseGateReadsAsAGate(List<string> failures, List<string> notes)
        {
            string dress = TryRead(DresserSrc);
            if (dress == null) { failures.Add("cannot read " + DresserSrc); return; }
            var cfg = LoadConfigs(failures);
            if (cfg == null) return;

            // ⚠ Slice each body from its SIGNATURE to its own closing brace, never "to the
            // next method". The first draft ran DefaultGate -> "private static string
            // DefaultWall(", which swallowed DefaultWall's XML doc comment - and that comment
            // quotes a token, so the fall-through default read out of the COMMENT instead of
            // the code. A doc comment must never be able to change what a source lint sees.
            string wallBody = Slice(dress, "private static string DefaultWall(string kit)", "\n        }");
            string gateBody = Slice(dress, "private static string DefaultGate(string kit)", "\n        }");
            string flankBody = Slice(dress, "string flankTok =", ";");

            if (wallBody == null || gateBody == null || flankBody == null)
            {
                failures.Add("[gate-scale] could not slice DefaultWall / DefaultGate / flankTok out of " +
                             Path.GetFileName(DresserSrc) + " - the gate-vs-wall pin cannot be evaluated, " +
                             "so it is treated as broken rather than skipped.");
                return;
            }

            // id -> kit, mirroring RaidBaseDresser.KitFor. iron_bastion authors no raidDress
            // at all, so it takes every default; that is exactly why it is listed here.
            var camps = new[]
            {
                new[] { "raider_camp_small", "hexagon-green" },
                new[] { "iron_bastion",      "hexagon-green" },
                new[] { "fortified_garrison", "synty-castle" },
                new[] { "mage_enclave",       "dungeon-stone" },
            };

            int judged = 0;
            foreach (var camp in camps)
            {
                string id = camp[0], kit = camp[1];
                var row = Row(cfg, id);
                var dressRow = row != null ? row["raidDress"] as JObject : null;

                // DATA wins over the code default - that is the live precedence at
                // RaidBaseDresser.Dress (the wallTok / gateTok ternaries).
                string wallTok = dressRow != null && dressRow["wallModule"] != null
                    ? (string)dressRow["wallModule"] : KitToken(wallBody, kit);
                string gateTok = dressRow != null && dressRow["gate"] != null
                    ? (string)dressRow["gate"] : KitToken(gateBody, kit);
                string flankTok = KitToken(flankBody, kit);   // code-only, no data override

                if (wallTok == null || gateTok == null || flankTok == null)
                {
                    notes.Add("gate-scale " + id + ": token unresolved, skipped");
                    continue;
                }

                float wallH, gateH, flankH;
                if (!MeasuredHeightM.TryGetValue(wallTok, out wallH) ||
                    !MeasuredHeightM.TryGetValue(gateTok, out gateH) ||
                    !MeasuredHeightM.TryGetValue(flankTok, out flankH))
                {
                    notes.Add("gate-scale " + id + ": unmeasured module, skipped");
                    continue;
                }
                if (wallH <= 0.01f) continue;
                judged++;

                float gateRatio = gateH / wallH;
                float flankRatio = flankH / wallH;

                if (gateRatio < GateHeightFloorOfWall)
                    failures.Add("[gate-scale] '" + id + "' (" + kit + "): the gate '" + gateTok +
                                 "' is " + gateH.ToString("F2") + "m against a '" + wallTok + "' wall of " +
                                 wallH.ToString("F2") + "m - it reaches " + gateRatio.ToString("F2") +
                                 " of the wall where " + GateHeightFloorOfWall.ToString("F2") +
                                 " is required. A gate that short reads as a doll's door in a giant's " +
                                 "wall. Fix the MODULE (a matched-box gate from the wall's own pack), " +
                                 "not this threshold.");

                if (flankRatio < FlankHeightFloorOfWall)
                    failures.Add("[gate-scale] '" + id + "' (" + kit + "): the flank tower '" + flankTok +
                                 "' is " + flankH.ToString("F2") + "m against a '" + wallTok + "' wall of " +
                                 wallH.ToString("F2") + "m - " + flankRatio.ToString("F2") + " of the wall, " +
                                 "so the tower is SHORTER than the wall it flanks. Required >= " +
                                 FlankHeightFloorOfWall.ToString("F2") + ".");
            }

            if (judged == 0)
            {
                notes.Add("gate-scale: nothing measurable (art packs absent?)");
                return;
            }
            notes.Add("gate-scale " + judged + " camp(s) judged");
        }

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }
    }
}
