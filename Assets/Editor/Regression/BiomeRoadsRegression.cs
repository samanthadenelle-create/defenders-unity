// =============================================================================
// BiomeRoadsRegression [biome-roads]   Marker: BIOME_ROADS_OK / BIOME_ROADS_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Registered in DataRegression.RunAll.
//
// WHAT THIS PINS (owner 2026-08-16: "place a portal to simple tunnel system that
// will drop into the new biomes", following "make simple access points at far
// corners of map"):
//
//   1. DERIVED, NOT TYPED. Every drop position is a function of the MEASURED world
//      bounds handed in. Proven by DERIVATION, not by inspection: the same resolver
//      is run against two DIFFERENT bounds and the points must MOVE proportionally.
//      A hardcoded coordinate cannot pass that - it would return the same point for
//      a 1000m world and a 2000m one. This is the case that fails if someone
//      "simplifies" ResolveDrops into a table of constants, which is exactly the
//      regression this ticket was written under (two bugs on 2026-08-15 from
//      constants that had stopped matching the geometry).
//
//   2. NO SILENT NO-OPS. Every drop resolves to a destination, every tunnel arm id
//      exists in the authored graph JSON, and every arm in the graph is claimed by a
//      region. A door with no destination, or a destination with no door, is the
//      defect class the owner hit three separate times on 2026-08-15 (the raid
//      button, the spire plans, the treasure crate).
//
//   3. AN EXPLICIT OFF-SWITCH EXISTS. FeatureFlags.BiomeRoads is real and is read by
//      BOTH ends of the spoke (the hub portal and the tunnel drops). A feature with
//      no kill switch cannot be turned off when it misbehaves in a felt-test.
//
//   4. THE EGRESS LAW IS NOT WEAKENED. dg_hollow_roads authors ZERO extracts and is
//      absent from DungeonEgressRegression's ContentLayouts AND ControlGroupLayouts -
//      i.e. the tunnel is exempt by BEING A DIFFERENT KIND OF SPACE, never by an
//      assertion being loosened to fit it in. This case reads that oracle's own
//      arrays by reflection rather than restating them, so the two cannot drift.
//
//   5. NAMES ARE AUTHORED, NOT INVENTED. The four biomes are the four RegionIds
//      already declared in RegionZone.cs and tabled in ZoneManager.Regions. This
//      suite fails if BiomeRoads ever grows its own copy of a display name, cardinal
//      or tier - the duplicated-state drift CLAUDE.md sec.2/sec.5 keep un-rotting.
//
//   6. THE RULED IDENTITY HOLDS, BOTH WAYS (WO-1044 R1/R2, owner 2026-08-17). The tunnel
//      reads to the player as "The Rootways" and its id stays "dg_hollow_roads". Case 7
//      fails if the id is "tidied" to match the name (which would silently unhook the
//      graph file, the injector and the WO-1112 hero carry) AND if the display name is
//      reverted or re-typed (which puts the player back in front of a name promising the
//      Hollowed inside a graph that authors zero encounters). A ruled player-facing word
//      with no assertion behind it survives exactly until the next person who did not
//      know it was ruled.
//
// SOURCE-LINT NOTE: the source scan below strips COMMENTS AND STRING LITERALS before
// matching. Without that, this file's own prose (and the very literals it forbids)
// would satisfy the search and the lint would pass by reading itself.
//
// NO HOLLOW PASSES (CLAUDE.md sec.12): zero drops resolved, zero arms found, or a
// graph file that will not parse => FAIL, never OK. A suite that found nothing to
// look at has not passed, it has not run.
//
// Standalone batch entry:
//   -Method DeNelle.Editor.Regression.BiomeRoadsRegression.RunStandalone
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.World;

namespace DeNelle.Editor.Regression
{
    public static class BiomeRoadsRegression
    {
        private const string GraphResources   = "Assets/Resources/Data/Canonical/dungeon-graphs/dg_hollow_roads.json";
        private const string GraphStreaming   = "Assets/StreamingAssets/Data/Canonical/dungeon-graphs/dg_hollow_roads.json";
        private const string CoreSrc          = "Assets/_Modules/Core/World/BiomeRoads.cs";
        private const string InjectorSrc      = "Assets/_Modules/Village/World/HollowRoadsDropInjector.cs";
        private const string SpawnerSrc       = "Assets/_Modules/Village/World/DungeonWorldPortalSpawner.cs";
        private const string FlagsSrc         = "Assets/_Modules/Core/FeatureFlags.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- BIOME ROADS (hub -> portal -> tunnel -> four derived biome drops) ---");

            try
            {
                Case1_DropsAreDerivedFromMeasuredBounds(failures, notes, log);
                Case2_EveryArmHasARegionAndEveryRegionHasAnArm(failures, notes, log);
                Case3_NamesComeFromTheAuthoredRegionTable(failures, notes, log);
                Case4_KillSwitchExistsAndBothEndsReadIt(failures, notes, log);
                Case5_EgressLawIsNotWeakened(failures, notes, log);
                Case6_NoTypedWorldCoordinatesInTheDerivation(failures, notes, log);
                Case7_TunnelIdentityIsTheRuledOne(failures, notes, log);
                Case8_TheDestinationIsGroundedByTheSceneThatOwnsIt(failures, notes, log);
            }
            catch (Exception ex)
            {
                // The stack is the point of a throwing suite (CLAUDE.md sec.12): without it the
                // failure line names only this catch site and the next reader has to guess.
                failures.Add($"[biome-roads] suite THREW: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }

            string noteStr = notes.Count > 0 ? " | " + string.Join("; ", notes) : "";
            if (failures.Count == 0)
            {
                reason = "biome-roads: 8 cases green" + noteStr;
                Debug.Log(log.ToString() + "BIOME_ROADS_OK");
                return true;
            }

            reason = "biome-roads: " + string.Join("; ", failures) + noteStr;
            Debug.LogError(log.ToString() + "BIOME_ROADS_FAIL: " + reason);
            return false;
        }

        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            Debug.Log(ok ? "BIOME_ROADS_OK " + reason : "BIOME_ROADS_FAIL " + reason);
        }

        // ── Case 1 — the positions MOVE with the measured world. ────────────────
        // This is the whole ticket in one assertion. Two synthetic worlds, one twice the
        // size of the other; a derived point must scale with its world, a typed one cannot.
        private static void Case1_DropsAreDerivedFromMeasuredBounds(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            var small = new Bounds(Vector3.zero, new Vector3(1000f, 42f, 1000f));
            var large = new Bounds(Vector3.zero, new Vector3(2000f, 42f, 2000f));

            // OFF-CENTRE WORLD — the case the first version of this suite could not see.
            // Both bounds above are origin-centred, so a resolver that offsets from bounds.center
            // and one that seats from the world origin produce IDENTICAL answers, and the suite
            // passes either way. It has to be asked about a world whose centre is NOT the origin,
            // because ZoneManager classifies against the ORIGIN: offsetting from the centre would
            // put every drop off the classifier's axis the moment a terrain is baked off-centre.
            // This block is here because that bug was real and shipped in the first draft.
            var offCentre = new Bounds(new Vector3(120f, 0f, -80f), new Vector3(1000f, 42f, 1000f));
            var offDrops = BiomeRoads.ResolveDrops(offCentre);
            if (offDrops.Count == 0)
            {
                failures.Add("[biome-roads-derived] ResolveDrops returned NO drops for an OFF-CENTRE world - " +
                             "a terrain that is not centred on the origin must still produce drops.");
            }
            foreach (var d in offDrops)
            {
                bool axis = (Mathf.Abs(d.Point.x) < 0.01f) != (Mathf.Abs(d.Point.z) < 0.01f);
                if (!axis)
                    failures.Add($"[biome-roads-derived] in an OFF-CENTRE world, drop '{d.Region}' landed at " +
                                 $"{d.Point}, which is not on a cardinal axis THROUGH THE ORIGIN. The resolver is " +
                                 "seating from bounds.center; ZoneManager classifies from the world origin, so " +
                                 "the drop and the classifier are using different frames of reference.");
                RegionId cls = ZoneManager.GetZone(d.Point);
                if (cls != d.Region)
                    failures.Add($"[biome-roads-derived] in an OFF-CENTRE world, drop claims '{d.Region}' but " +
                                 $"ZoneManager classifies {d.Point} as '{cls}'.");
            }

            var a = BiomeRoads.ResolveDrops(small);
            var b = BiomeRoads.ResolveDrops(large);

            if (a.Count == 0 || b.Count == 0)
            {
                failures.Add("[biome-roads-derived] ResolveDrops returned NO drops for a valid bounds - " +
                             "the derivation produced nothing, so nothing below can be trusted.");
                return;
            }
            if (a.Count != b.Count)
            {
                failures.Add($"[biome-roads-derived] ResolveDrops returned {a.Count} drops for a 1000m world " +
                             $"but {b.Count} for a 2000m world - the drop SET must not depend on world size.");
                return;
            }

            for (int i = 0; i < a.Count; i++)
            {
                Vector3 pa = a[i].Point, pb = b[i].Point;
                float ra = new Vector2(pa.x, pa.z).magnitude;
                float rb = new Vector2(pb.x, pb.z).magnitude;

                if (ra < 1f)
                {
                    failures.Add($"[biome-roads-derived] drop '{a[i].Region}' resolved to the origin ({pa}) - " +
                                 "a drop on top of the Heart reads as placed in every log line while being " +
                                 "completely wrong.");
                    continue;
                }
                // Doubling the world must double the reach. Tolerance is float noise, not slack.
                if (Mathf.Abs(rb - ra * 2f) > 0.5f)
                {
                    failures.Add($"[biome-roads-derived] drop '{a[i].Region}' sat {ra:F1}m out in a 1000m world " +
                                 $"and {rb:F1}m out in a 2000m world; a DERIVED point must double to {ra * 2f:F1}m. " +
                                 "This is what a hardcoded coordinate looks like from the outside.");
                }
                // Cardinal seat: exactly one axis carries the reach, so ZoneManager's dominant-axis
                // split can never be a coin-flip between two regions.
                bool onAxis = (Mathf.Abs(pa.x) < 0.01f) != (Mathf.Abs(pa.z) < 0.01f);
                if (!onAxis)
                {
                    failures.Add($"[biome-roads-derived] drop '{a[i].Region}' at {pa} is not seated on a single " +
                                 "cardinal axis - ZoneManager classifies by dominant axis, so an off-axis (or " +
                                 "diagonal) drop is ambiguous about which biome the player just landed in.");
                }
                // And the classification must actually agree with what the drop claims.
                RegionId landed = ZoneManager.GetZone(pa);
                if (landed != a[i].Region)
                {
                    failures.Add($"[biome-roads-derived] drop claims '{a[i].Region}' but ZoneManager classifies " +
                                 $"its own derived point {pa} as '{landed}' - the prompt would tell the player " +
                                 "something untrue.");
                }
            }

            // Degenerate bounds must derive NOTHING rather than a pile of origin points.
            var degenerate = BiomeRoads.ResolveDrops(new Bounds(Vector3.zero, Vector3.zero));
            if (degenerate.Count != 0)
                failures.Add($"[biome-roads-derived] degenerate bounds produced {degenerate.Count} drop(s); it " +
                             "must produce ZERO - four drops stacked on the origin is worse than none.");

            notes.Add($"[biome-roads-derived] {a.Count} drops scale-tested across 1000m/2000m worlds");
            log.AppendLine($"  derived: {a.Count} drops, all cardinal-seated and scale-correct");
        }

        // ── Case 2 — arms and regions are in exact 1:1 correspondence. ──────────
        private static void Case2_EveryArmHasARegionAndEveryRegionHasAnArm(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            if (!File.Exists(GraphResources) || !File.Exists(GraphStreaming))
            {
                failures.Add($"[biome-roads-arms] the tunnel graph is missing a canonical copy " +
                             $"(Resources={File.Exists(GraphResources)}, StreamingAssets={File.Exists(GraphStreaming)}). " +
                             "Resources wins at runtime, so a single-copy edit ships a tunnel nobody reviewed.");
                return;
            }

            string ra = File.ReadAllText(GraphResources);
            string rb = File.ReadAllText(GraphStreaming);
            if (!string.Equals(ra, rb, StringComparison.Ordinal))
                failures.Add("[biome-roads-arms] dg_hollow_roads.json DIFFERS between Resources and " +
                             "StreamingAssets. Resources wins at runtime; the two must be identical.");

            JObject graph;
            try { graph = JObject.Parse(ra); }
            catch (Exception e)
            {
                failures.Add($"[biome-roads-arms] dg_hollow_roads.json will not parse: {e.Message}");
                return;
            }

            // Node ids present in the authored graph.
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var nodes = graph["nodes"] as JArray;
            if (nodes == null || nodes.Count == 0)
            {
                failures.Add("[biome-roads-arms] dg_hollow_roads.json has no nodes - there is no tunnel.");
                return;
            }
            foreach (var n in nodes)
            {
                string id = (string)n["id"];
                if (!string.IsNullOrEmpty(id)) nodeIds.Add(id);
            }

            // Every region's arm must exist as a node.
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionId region in BiomeRoads.DropRegions)
            {
                string arm = BiomeRoads.ArmRoomIdFor(region);
                if (string.IsNullOrEmpty(arm))
                {
                    failures.Add($"[biome-roads-arms] region '{region}' has NO arm room id - that biome's door " +
                                 "would have nowhere to stand.");
                    continue;
                }
                if (!nodeIds.Contains(arm))
                    failures.Add($"[biome-roads-arms] region '{region}' points at tunnel arm '{arm}', which is " +
                                 "NOT a node in dg_hollow_roads.json. That is a door with no room behind it.");
                if (!claimed.Add(arm))
                    failures.Add($"[biome-roads-arms] tunnel arm '{arm}' is claimed by more than one region - " +
                                 "two biomes would fight over one door.");
            }

            // And every arm_* node in the graph must be claimed by a region (no orphan corridors
            // that look like a way out and are not).
            foreach (string id in nodeIds)
            {
                if (!id.StartsWith("arm_", StringComparison.Ordinal)) continue;
                if (!claimed.Contains(id))
                    failures.Add($"[biome-roads-arms] tunnel node '{id}' looks like a biome arm but NO region " +
                                 "claims it - it would be a corridor that dead-ends with no explanation.");
            }

            // Every EDGE must reference a declared node, or the composer silently emits a room at
            // the origin (GraphDungeonComposer warns and stacks it on the entry).
            var edges = graph["edges"] as JArray;
            if (edges == null || edges.Count == 0)
            {
                failures.Add("[biome-roads-arms] dg_hollow_roads.json has no edges - the rooms would not connect.");
            }
            else
            {
                var reached = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in edges)
                {
                    string from = (string)e["from"], to = (string)e["to"];
                    if (!nodeIds.Contains(from))
                        failures.Add($"[biome-roads-arms] edge references unknown 'from' node '{from}'.");
                    if (!nodeIds.Contains(to))
                        failures.Add($"[biome-roads-arms] edge references unknown 'to' node '{to}'.");
                    if (!string.IsNullOrEmpty(to)) reached.Add(to);
                }
                string entry = (string)graph["entry"];
                foreach (string id in nodeIds)
                {
                    if (string.Equals(id, entry, StringComparison.Ordinal)) continue;
                    if (!reached.Contains(id))
                        failures.Add($"[biome-roads-arms] node '{id}' is reached by no edge - the composer would " +
                                     "emit it stacked at the origin, overlapping the entry hall.");
                }
            }

            notes.Add($"[biome-roads-arms] {BiomeRoads.DropRegions.Length} regions <-> {claimed.Count} arms, " +
                      $"{nodeIds.Count} nodes, dual copy identical");
            log.AppendLine($"  arms: {claimed.Count} arm(s) in 1:1 correspondence with regions");
        }

        // ── Case 3 — the four names are READ from the authored table, never restated. ──
        private static void Case3_NamesComeFromTheAuthoredRegionTable(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            if (BiomeRoads.DropRegions.Length != 4)
                failures.Add($"[biome-roads-names] DropRegions carries {BiomeRoads.DropRegions.Length} regions; " +
                             "the owner asked for FOUR biomes.");

            foreach (RegionId region in BiomeRoads.DropRegions)
            {
                if (region == RegionId.Village)
                    failures.Add("[biome-roads-names] Village is in DropRegions - it is the hub the player came " +
                                 "FROM, not a biome to travel TO.");

                if (!ZoneManager.Regions.TryGetValue(region, out var zone) || zone == null)
                {
                    failures.Add($"[biome-roads-names] region '{region}' has NO record in ZoneManager.Regions - " +
                                 "BiomeRoads would be naming a biome the authored table does not know.");
                    continue;
                }
                if (!string.Equals(BiomeRoads.ZoneName(region), zone.DisplayName, StringComparison.Ordinal))
                    failures.Add($"[biome-roads-names] BiomeRoads.ZoneName('{region}') returned " +
                                 $"'{BiomeRoads.ZoneName(region)}' but the authored table says '{zone.DisplayName}' - " +
                                 "a second copy of an authored name has appeared.");
                if (!string.Equals(BiomeRoads.Cardinal(region), zone.Cardinal, StringComparison.Ordinal))
                    failures.Add($"[biome-roads-names] cardinal for '{region}' drifted from the authored table.");
                if (BiomeRoads.DangerTier(region) != zone.DangerTier)
                    failures.Add($"[biome-roads-names] danger tier for '{region}' drifted from the authored table.");

                // Player-facing strings must be ASCII (mobile font atlas).
                string label = BiomeRoads.TravelLabel(region);
                foreach (char c in label)
                {
                    if (c > 127)
                    {
                        failures.Add($"[biome-roads-names] travel label for '{region}' carries a non-ASCII " +
                                     $"character (U+{(int)c:X4}) - it will not render in the mobile font atlas.");
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(label))
                    failures.Add($"[biome-roads-names] travel label for '{region}' is empty - the prompt would " +
                                 "be a blank button, which is a silently-dead door with extra steps.");
            }

            // WO-1044 R3 (owner 2026-08-17): the SHORT names are the UI names and the long forms
            // ("Stoneback Ridge", "Corrupted Ashwood") are prose only. canon-strings.json carries
            // the ruling as the canon RECORD - and a second home for a name is only safe while
            // something fails when the two disagree, which is this loop. ZoneManager stays the
            // runtime authority; canon-strings is checked AGAINST it, never read instead of it.
            const string CanonRes = "Assets/Resources/Data/Canonical/canon-strings.json";
            foreach (RegionId region in BiomeRoads.DropRegions)
            {
                if (!ZoneManager.Regions.TryGetValue(region, out var zone) || zone == null) continue;
                string key = "region" + region;                       // regionGoldfields, regionAshwood, ...
                string canonVal = ReadCanonKey(CanonRes, key);
                if (canonVal == null)
                    failures.Add($"[biome-roads-names] canon-strings has no '{key}' - WO-1044 R3 ruled the short " +
                                 "UI name for every march, and a ruling with no record is a ruling the next " +
                                 "session re-litigates.");
                else if (!string.Equals(canonVal, zone.DisplayName, StringComparison.Ordinal))
                    failures.Add($"[biome-roads-names] canon-strings '{key}' is '{canonVal}' but the authored " +
                                 $"table says '{zone.DisplayName}'. R3 ruled the SHORT form is the UI name; if " +
                                 "the long prose form has leaked into either, they now disagree on screen.");
            }

            notes.Add($"[biome-roads-names] {BiomeRoads.DropRegions.Length} regions cross-checked against " +
                      "ZoneManager.Regions + canon-strings R3 short names");
            log.AppendLine("  names: all four read from the authored region table (R3 short forms pinned)");
        }

        // ── Case 4 — a real kill switch, read at BOTH ends. ────────────────────
        private static void Case4_KillSwitchExistsAndBothEndsReadIt(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            var prop = typeof(DeNelle.Core.FeatureFlags).GetProperty("BiomeRoads",
                BindingFlags.Public | BindingFlags.Static);
            if (prop == null)
            {
                failures.Add("[biome-roads-flag] FeatureFlags.BiomeRoads does not exist - the feature has no " +
                             "off-switch, so a bad felt-test cannot be stopped without a rebuild.");
                return;
            }

            string flagsSrc = ReadStripped(FlagsSrc);
            if (flagsSrc == null) { failures.Add($"[biome-roads-flag] cannot read {FlagsSrc}"); return; }

            // Both ends of the spoke must gate on it: the hub portal AND the tunnel drops.
            string spawner = ReadStripped(SpawnerSrc);
            string injector = ReadStripped(InjectorSrc);
            if (spawner == null || injector == null)
            {
                failures.Add("[biome-roads-flag] cannot read both ends of the spoke to verify the gate.");
                return;
            }
            if (!spawner.Contains("FeatureFlags.BiomeRoads"))
                failures.Add("[biome-roads-flag] the HUB end (DungeonWorldPortalSpawner) does not read " +
                             "FeatureFlags.BiomeRoads - turning the feature off would leave the tunnel mouth " +
                             "standing in the world.");
            if (!injector.Contains("FeatureFlags.BiomeRoads"))
                failures.Add("[biome-roads-flag] the TUNNEL end (HollowRoadsDropInjector) does not read " +
                             "FeatureFlags.BiomeRoads - turning the feature off would leave four live biome " +
                             "doors inside the tunnel.");

            notes.Add("[biome-roads-flag] ff.biomeroads present and read at both ends");
            log.AppendLine("  flag: ff.biomeroads gates the hub portal AND the tunnel drops");
        }

        // ── Case 5 — the [dungeon-egress] law is intact, not loosened. ─────────
        private static void Case5_EgressLawIsNotWeakened(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            // Read the egress oracle's OWN arrays by reflection. Restating them here would create the
            // second copy that goes stale - which is the bug this whole project keeps re-fixing.
            var t = typeof(DungeonEgressRegression);
            string[] content = ReadStringArrayField(t, "ContentLayouts");
            string[] control = ReadStringArrayField(t, "ControlGroupLayouts");

            if (content == null || control == null)
            {
                failures.Add("[biome-roads-egress] could not read DungeonEgressRegression's ContentLayouts / " +
                             "ControlGroupLayouts - cannot prove the tunnel is exempt by KIND rather than by an " +
                             "assertion having been loosened.");
                return;
            }

            string tunnel = BiomeRoads.TunnelSceneId;
            foreach (string id in content)
            {
                if (string.Equals(id, tunnel, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"[biome-roads-egress] '{tunnel}' has been added to DungeonEgressRegression's " +
                                 "ContentLayouts. A four-exit tunnel is not content and must not be judged by the " +
                                 "one-back-exit trim - and that trim must not be relaxed to accommodate it.");
            }
            foreach (string id in control)
            {
                if (string.Equals(id, tunnel, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"[biome-roads-egress] '{tunnel}' has been added to the egress CONTROL GROUP, " +
                                 "which is a fixture set with a different meaning.");
            }

            // And the tunnel itself must author ZERO extracts: its arms are OUTBOUND doors, not
            // extraction pads, so DungeonExitSpawner's single injected front exit stays the one
            // beacon and the one way home.
            if (File.Exists(GraphResources))
            {
                try
                {
                    var graph = JObject.Parse(File.ReadAllText(GraphResources));
                    var extracts = graph["extracts"] as JArray;
                    int n = extracts?.Count ?? 0;
                    if (n != 0)
                        failures.Add($"[biome-roads-egress] dg_hollow_roads.json authors {n} extract(s); it must " +
                                     "author ZERO. The biome arms are outbound doors, and authoring them as " +
                                     "extracts would push a four-pad layout against an oracle whose whole point " +
                                     "is one entry and one back exit.");

                    string exitRoom = (string)graph["exitRoomId"];
                    string entry = (string)graph["entry"];
                    if (string.IsNullOrEmpty(exitRoom))
                        failures.Add("[biome-roads-egress] dg_hollow_roads.json has no exitRoomId - the injected " +
                                     "front exit would have no authored seat and the way home would be a guess.");
                    else if (!string.Equals(exitRoom, entry, StringComparison.Ordinal))
                        notes.Add($"[biome-roads-egress] exitRoomId '{exitRoom}' differs from entry '{entry}'");
                }
                catch (Exception e)
                {
                    failures.Add($"[biome-roads-egress] could not read the tunnel graph: {e.Message}");
                }
            }

            notes.Add($"[biome-roads-egress] tunnel absent from {content.Length} content + {control.Length} " +
                      "control ids; authors 0 extracts");
            log.AppendLine("  egress: tunnel exempt by kind; the one-back-exit trim is untouched");
        }

        // ── Case 6 — the derivation carries no typed world coordinate. ─────────
        private static void Case6_NoTypedWorldCoordinatesInTheDerivation(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            string src = ReadStripped(CoreSrc);
            if (src == null)
            {
                failures.Add($"[biome-roads-typed] cannot read {CoreSrc} - the lint has not run, so it has not " +
                             "passed.");
                return;
            }

            // With comments AND string literals stripped, a bare world-scale magnitude in the
            // derivation means someone typed a coordinate back in. The forbidden shape is a numeric
            // literal of 3+ digits: every legitimate value here is a fraction or a small guard.
            var m = Regex.Matches(src, @"(?<![\w.])\d{3,}(?:\.\d+)?f?");
            var offenders = new List<string>();
            foreach (Match hit in m)
            {
                // A year in a seed/date is not a coordinate; nothing else 3+ digits belongs here.
                if (hit.Value.StartsWith("20") && hit.Value.Length == 4) continue;
                offenders.Add(hit.Value);
            }
            if (offenders.Count > 0)
                failures.Add($"[biome-roads-typed] {CoreSrc} carries world-scale numeric literal(s) " +
                             $"[{string.Join(", ", offenders)}] outside comments and strings. Every drop position " +
                             "must be a FRACTION of measured bounds - a typed metre count is the exact defect " +
                             "this feature was specified to avoid.");

            // The measurement must have NO typed fallback: an unmeasurable world must place nothing.
            if (!src.Contains("TryMeasureWorldBounds"))
                failures.Add("[biome-roads-typed] BiomeRoads no longer exposes TryMeasureWorldBounds - the " +
                             "measured-bounds entry point is gone.");

            notes.Add("[biome-roads-typed] derivation source scanned with comments + string literals stripped");
            log.AppendLine("  typed-check: no world-scale constants in the derivation");
        }

        // ── Case 7 — the RULED identity: id frozen, display name renamed. ──────
        // WO-1044 R1/R2 (owner, 2026-08-17): the tunnel the player reads is "The Rootways";
        // the id stays "dg_hollow_roads" because it is a four-way contract (ArmRoomIdFor, the
        // graph JSON's filename, HollowRoadsDropInjector, this suite).
        //
        // THIS CASE EXISTS TO FAIL IN BOTH DIRECTIONS, which is the only reason to write it:
        //   • someone "tidies" the id to match the new name  -> the graph file, the injector and
        //     the WO-1112 hero carry all lose their key, silently, at runtime;
        //   • someone reverts / re-types the display name    -> the player is back to being
        //     promised the Hollowed in a tunnel that authors zero encounters.
        // A ruled player-facing name with no assertion behind it is a name that lasts until the
        // next person who does not know it was ruled.
        private static void Case7_TunnelIdentityIsTheRuledOne(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            const string RuledId   = "dg_hollow_roads";
            const string RuledName = "The Rootways";
            const string RetiredName = "The Hollow Roads";

            // (a) The ID is frozen. Not "starts with dg_" - the exact string, because the graph
            //     file is named after it and a near-miss is a missing scene, not a typo.
            if (!string.Equals(BiomeRoads.TunnelSceneId, RuledId, StringComparison.Ordinal))
                failures.Add($"[biome-roads-identity] TunnelSceneId is '{BiomeRoads.TunnelSceneId}', not " +
                             $"'{RuledId}'. WO-1044 R1 renamed the DISPLAY NAME and froze the ID on purpose: " +
                             "the id keys ArmRoomIdFor, the authored graph JSON's filename, the drop injector " +
                             "and this suite. Renaming it here does not rename them.");

            // (b) The graph the id names must actually be on disk, in BOTH copies. This is what
            //     turns (a) from a string comparison into a proof that the contract still lands.
            foreach (string path in new[] { GraphResources, GraphStreaming })
            {
                string expected = RuledId + ".json";
                if (!path.EndsWith(expected, StringComparison.Ordinal))
                    failures.Add($"[biome-roads-identity] '{path}' no longer matches the ruled id '{RuledId}' - " +
                                 "the graph file and the id have drifted apart.");
                else if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), path)))
                    failures.Add($"[biome-roads-identity] the tunnel graph '{path}' does not exist - the id is a " +
                                 "contract with a file that is not there.");
            }

            // (c) The player-facing name is the RULED one.
            string shown = BiomeRoads.TunnelDisplayName;
            if (string.IsNullOrWhiteSpace(shown))
                failures.Add("[biome-roads-identity] TunnelDisplayName is empty - the tunnel portal would show a " +
                             "blank label, which is a nameless door.");
            else if (!string.Equals(shown, RuledName, StringComparison.Ordinal))
                failures.Add($"[biome-roads-identity] TunnelDisplayName is '{shown}', but WO-1044 R1 ruled " +
                             $"'{RuledName}' (owner 2026-08-17). If this is a NEW ruling it needs a new date in " +
                             "the WO and this line updated with it - not a quiet re-type.");

            if (string.Equals(shown, RetiredName, StringComparison.OrdinalIgnoreCase))
                failures.Add($"[biome-roads-identity] the RETIRED name '{RetiredName}' is back. It reads as " +
                             "'the Hollowed's roads' and promises enemies in a graph that authors zero " +
                             "encounters - the player finds an empty crossroads and concludes content is missing. " +
                             "That is precisely what R1 fixed.");

            if (string.Equals(shown, BiomeRoads.TunnelSceneId, StringComparison.OrdinalIgnoreCase))
                failures.Add("[biome-roads-identity] the display name has been set to the raw id - the player " +
                             "would read a database key on a portal sign.");

            foreach (char c in shown ?? string.Empty)
            {
                if (c > 127)
                {
                    failures.Add($"[biome-roads-identity] TunnelDisplayName carries a non-ASCII character " +
                                 $"(U+{(int)c:X4}) - TMP renders it as a tofu box on device.");
                    break;
                }
            }

            // (d) canon-strings.json is the CANON record of the name, in both copies, and it must
            //     agree with the Core const the portal spawner actually reads. Two homes for one
            //     player-facing word is fine; two DIFFERENT words in them is the drift CLAUDE.md
            //     sec.15 exists to stop.
            const string CanonRes  = "Assets/Resources/Data/Canonical/canon-strings.json";
            const string CanonStr  = "Assets/StreamingAssets/Data/Canonical/canon-strings.json";
            string resVal = ReadCanonKey(CanonRes, "tunnelName");
            string strVal = ReadCanonKey(CanonStr, "tunnelName");

            if (resVal == null)
                failures.Add($"[biome-roads-identity] canon-strings key 'tunnelName' is missing from {CanonRes} - " +
                             "the ruled name has no canon home, so the next reader has only a code const to " +
                             "trust and no way to know it was ruled.");
            if (strVal == null)
                failures.Add($"[biome-roads-identity] canon-strings key 'tunnelName' is missing from {CanonStr} - " +
                             "CanonStrings reads the StreamingAssets copy, so the name would resolve to " +
                             "[[missing:tunnelName]] at runtime.");
            if (resVal != null && strVal != null && !string.Equals(resVal, strVal, StringComparison.Ordinal))
                failures.Add($"[biome-roads-identity] canon-strings 'tunnelName' DIFFERS between the copies " +
                             $"('{resVal}' vs '{strVal}') - the dual copy must be identical; the Resources copy " +
                             "wins at load, so the two would disagree only on device.");
            if (resVal != null && !string.Equals(resVal, shown, StringComparison.Ordinal))
                failures.Add($"[biome-roads-identity] canon-strings 'tunnelName' is '{resVal}' but " +
                             $"BiomeRoads.TunnelDisplayName is '{shown}' - one of the two homes for the tunnel's " +
                             "name has gone stale.");

            notes.Add($"[biome-roads-identity] id '{BiomeRoads.TunnelSceneId}' frozen; display '{shown}' " +
                      "matched against canon-strings dual copy");
            log.AppendLine($"  identity: id '{BiomeRoads.TunnelSceneId}' (frozen) / name '{shown}' (WO-1044 R1)");
        }

        // ── Case 8 — the destination is grounded by the scene that OWNS it. ────
        //
        // WO-1692. The tunnel is entered SINGLE (SceneRouter.cs:323 -> SceneManager.LoadSceneAsync
        // with the default LoadSceneMode), so while dg_hollow_roads is active the hub scene and its
        // baked navmesh are UNLOADED. A NavMesh probe run from inside the tunnel therefore asks the
        // tunnel's own nine-room corridor about a point 400m out in another world, and misses every
        // time -- which is exactly what shipped: F8 seq 5003-5006 refused all four drops with
        // identical text, seq 5007 read "seated 0 of 4 biome drops", and its trailing hint blamed the
        // arm room ids (Case 2 above has been green throughout, so that hint was never true).
        //
        // THIS CASE IS A SOURCE LINT ON PURPOSE. The defect is structural -- a question asked in a
        // scene that cannot answer it -- and it leaves no artefact in data, only in where the call
        // sits. The same shape already guards HeroPlayableBoundsRegression's typed-bound rule.
        // The arrival-side probe (NavMesh.SamplePosition of the HERO's own position, in the
        // destination scene) is legitimate and deliberately NOT forbidden: it runs where the mesh it
        // queries actually lives.
        private static void Case8_TheDestinationIsGroundedByTheSceneThatOwnsIt(
            List<string> failures, List<string> notes, StringBuilder log)
        {
            string src = ReadStripped(InjectorSrc);
            if (src == null)
            {
                failures.Add($"[biome-roads-grounding] cannot read {InjectorSrc} - the lint has not run, so it " +
                             "has not passed.");
                return;
            }

            // (a) A drop point may be probed in EXACTLY ONE place: the hub-side helper. Not "never" --
            //     WO-1091 requires the fail-closed probe to exist, and it does; it must simply run
            //     where the destination scene is loaded. So the lint is positional, not prohibitive.
            int groundStart = src.IndexOf("bool TryGroundDrop", StringComparison.Ordinal);
            int groundEnd = groundStart >= 0
                ? src.IndexOf("Vector3 GroundY", groundStart, StringComparison.Ordinal)
                : -1;
            if (groundStart < 0 || groundEnd < 0)
            {
                failures.Add("[biome-roads-grounding] the hub-side TryGroundDrop helper is gone (or no longer sits " +
                             "above GroundY). It is the ONE place a drop point may be probed - in the hub, where " +
                             "the destination scene's navmesh is loaded.");
            }
            else
            {
                string hubProbe = src.Substring(groundStart, groundEnd - groundStart);
                if (!Regex.IsMatch(hubProbe, @"NavMesh\s*\.\s*SamplePosition\s*\(\s*drop\s*\."))
                    failures.Add("[biome-roads-grounding] TryGroundDrop no longer probes the drop point. Without it " +
                                 "an ungrounded drop is remembered and seated at the derived bounds-CENTRE height " +
                                 "(WO-1091 item 1, fail-closed, is exactly this branch).");

                string outsideHub = src.Remove(groundStart, groundEnd - groundStart);
                if (Regex.IsMatch(outsideHub, @"NavMesh\s*\.\s*SamplePosition\s*\(\s*drop\s*\."))
                    failures.Add("[biome-roads-grounding] a DROP point is probed outside TryGroundDrop. Every other " +
                                 "site in this file runs while the tunnel is active, and the tunnel load is SINGLE " +
                                 "(SceneRouter.cs:323) - the hub and its navmesh are unloaded, so such a probe " +
                                 "interrogates the corridor's own mesh and refuses every road (F8 seq 5003-5007).");
            }

            // (b) And none inside the seating method itself, whatever the local is called.
            int seatStart = src.IndexOf("bool TrySeatDrop", StringComparison.Ordinal);
            int seatEnd = src.IndexOf("Transform FindComposeRoot", StringComparison.Ordinal);
            if (seatStart >= 0 && seatEnd > seatStart)
            {
                string seatBody = src.Substring(seatStart, seatEnd - seatStart);
                if (seatBody.Contains("NavMesh.SamplePosition") || seatBody.Contains("NavMesh .SamplePosition"))
                    failures.Add("[biome-roads-grounding] TrySeatDrop contains a NavMesh.SamplePosition call. " +
                                 "Nothing seated in the tunnel may validate a destination against the tunnel's " +
                                 "navmesh - the destination scene is not loaded.");
            }
            else
            {
                notes.Add("[biome-roads-grounding] TrySeatDrop/FindComposeRoot boundary not found - the " +
                          "method-scoped half of the lint did not run (check (a) still did).");
            }

            // (c) The grounding must EXIST on the hub side, and be consumed on the tunnel side.
            //     Absence of the probe alone would pass a version that seats an ungrounded 17m-in-the-
            //     air point, which is the WO-1606 defect this must not regress into.
            if (!src.Contains("GroundDropsInHub"))
                failures.Add("[biome-roads-grounding] HollowRoadsDropInjector has no GroundDropsInHub pass - the " +
                             "four destinations would be seated at the derived bounds-CENTRE height (17m of open " +
                             "air on the live world), which is the WO-1606 defect.");
            if (!src.Contains("TryRecallGroundedDrop"))
                failures.Add("[biome-roads-grounding] the tunnel does not recall a hub-grounded destination " +
                             "(TryRecallGroundedDrop is gone) - a seated drop would promise an unvalidated point.");
            if (!Regex.IsMatch(src, @"RememberHubBounds[\s\S]{0,600}?GroundDropsInHub"))
                failures.Add("[biome-roads-grounding] RememberHubBounds no longer runs the grounding pass. That " +
                             "call is the ONLY moment the destination world is loaded; without it every road is " +
                             "refused for want of a memo.");

            // (d) The hub-side call site still exists at all.
            string spawner = ReadStripped(SpawnerSrc);
            if (spawner == null)
                failures.Add($"[biome-roads-grounding] cannot read {SpawnerSrc} - the hub-side half of the lint " +
                             "has not run.");
            else if (!spawner.Contains("RememberHubBounds"))
                failures.Add("[biome-roads-grounding] the hub no longer calls HollowRoadsDropInjector." +
                             "RememberHubBounds, so nothing measures OR grounds the world the tunnel drops into.");

            notes.Add("[biome-roads-grounding] destination grounded in the hub, recalled in the tunnel; no " +
                      "drop-point navmesh probe in the tunnel scene");
            log.AppendLine("  grounding: hub grounds the four destinations; the tunnel never probes for them");
        }

        // ── helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Read one top-level STRING value out of a canon-strings copy. Returns null when the file
        /// or the key is absent, or when the value is not a string - all three are reported by the
        /// caller as the same defect class (the ruled word is not reachable), which is what the
        /// player would experience.
        /// </summary>
        private static string ReadCanonKey(string relPath, string key)
        {
            try
            {
                string full = Path.Combine(Directory.GetCurrentDirectory(), relPath);
                if (!File.Exists(full)) return null;
                var o = JObject.Parse(File.ReadAllText(full));
                var tok = o[key];
                return tok?.Type == JTokenType.String ? (string)tok : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Read a source file with COMMENTS AND STRING LITERALS REMOVED. Both, not just comments:
        /// this suite's own prose names the very literals it forbids, and a comment-only strip would
        /// let a file pass by talking about the rule instead of following it.
        /// </summary>
        private static string ReadStripped(string relPath)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), relPath);
            if (!File.Exists(full)) return null;
            string s = File.ReadAllText(full);
            s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);   // block comments
            s = Regex.Replace(s, @"//[^\n]*", " ");                              // line comments
            s = Regex.Replace(s, @"@""(?:[^""]|"""")*""", "\"\"");               // verbatim strings
            s = Regex.Replace(s, @"""(?:\\.|[^""\\])*""", "\"\"");               // normal strings
            s = Regex.Replace(s, @"'(?:\\.|[^'\\])'", "' '");                    // char literals
            return s;
        }

        private static string[] ReadStringArrayField(Type t, string fieldName)
        {
            var f = t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            return f?.GetValue(null) as string[];
        }
    }
}
