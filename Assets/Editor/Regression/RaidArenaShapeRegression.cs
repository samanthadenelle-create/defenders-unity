// =============================================================================
// RaidArenaShapeRegression [raid-arena-shape]   Marker: RAID_ARENA_SHAPE_OK / _FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// Pins the owner complaint of 2026-08-02 - "the raid is just a square room with 1
// enemy" - and the four defects behind it, so none of them can silently return:
//
//   DEFECT 1 (shape)     The built base occupied ~2.4% of the authored 140 m floor
//                        (a ~21.6 m square). The ring size fell out of
//                        wallSegmentsPerSide * 1.5 m; the authored `baseRadius` was
//                        read by NOTHING. -> Case 1.
//   DEFECT 2 (dead keys) `centralBuilding`, `towers[]` (and formerly `eliteCount`) were declared
//                        on SceneConfigDef and authored in the JSON, and NOTHING in
//                        the repo read them. That whole CLASS of rot is what Case 2
//                        catches - generally, for every key, not just those three.
//   DEFECT 3 (objective) There was no win condition other than corpse-count, and any
//                        objective would have been unkillable if it implemented only
//                        IDamageableStructure: the hero's swing resolves IDamageable
//                        (PlayerAttackController :592-611) and rejects non-Hostile.
//                        -> Case 3, which asserts BOTH ends of that seam.
//   DEFECT 4 (lethality) A big arena full of turrets is trivially fatal (the sketched
//                        10 x 12 dmg x 1.4/s = 168 DPS kills a 100 HP hero in 0.6 s).
//                        -> Case 5 asserts the builder's DPS ceiling still exists and
//                        still leaves a survivable time-to-death.
//   Plus Case 4: a raid scene with no baked NavMesh is a room nobody can walk in.
//
// Contract: public static bool Run(out string reason) - DataRegression-shaped, true
// = pass + one-line summary, false = fail + the offending detail. NEVER throws (all
// I/O and reflection is guarded). No PlayMode, no scene load, no bake.
//
// Standalone: run-unity-method DeNelle.Editor.Regression.RaidArenaShapeRegression.RunAll
// Wiring into DataRegression.RunAll is left to the committer (that file is lane-fenced) -
// the exact line is in the REPORT.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class RaidArenaShapeRegression
    {
        // ---- Canonical data ---------------------------------------------------
        private const string ConfigsRes = "Assets/Resources/Data/Canonical/scene-configs.json";
        private const string ConfigsSA = "Assets/StreamingAssets/Data/Canonical/scene-configs.json";

        // ---- Source under test ------------------------------------------------
        private const string GeneratorSrc = "Assets/Editor/WallTools/RaidBaseGenerator.cs";
        private const string SpireSrc = "Assets/_Modules/Village/World/Camps/RaidSpire.cs";
        private const string ScoringSrc = "Assets/_Modules/Village/Troops/RaidScoring.cs";
        private const string HudSrc = "Assets/_Modules/Village/Troops/RaidHudController.cs";
        private const string VictorySrc = "Assets/_Modules/Village/World/Camps/RaidVictoryController.cs";
        private const string HeroAttackSrc = "Assets/_Modules/Village/Enemies/PlayerAttackController.cs";
        private const string TroopSrc = "Assets/_Modules/Village/Troops/TroopController.cs";
        private const string NavBakeSrc = "Assets/Editor/RaidNavBake.cs";
        private const string BoundaryHelperSrc = "Assets/Editor/ArenaBoundaryRing.cs";
        private const string SiegeArenaSrc = "Assets/Editor/ProceduralSiegeArenaBuilder.cs";

        private const string ScenesDir = "Assets/Scenes";
        /// <summary>Where the batchmode runners write their logs. Gitignored - absence is never a failure.</summary>
        private const string BuildsDir = "Builds";

        /// <summary>Half-extent of the RaidGround plane RaidNavBake authors (GroundScale 14 -> 140 m).</summary>
        private const float MapHalfExtent = 70f;

        /// <summary>
        /// The floor that makes the 2.4% regression impossible: a raid arena must occupy at
        /// least this share of the ground plane's area. 0.10 is well under the easiest tier's
        /// 20% target and miles above the 0.024 the owner measured.
        /// </summary>
        private const float MinFootprintFraction = 0.10f;

        /// <summary>Ceiling - past this the arena spills off the plane (builder clamps at 0.9 radius = 0.81 area).</summary>
        private const float MaxFootprintFraction = 0.81f;

        /// <summary>Hero base max HP (HeroHealth._maxHp) - the floor case for time-to-death.</summary>
        private const float HeroBaseHp = 100f;

        /// <summary>A tower budget must leave a base-HP hero at least this many seconds under the worst fire.</summary>
        private const float MinTimeToDeathSeconds = 4f;

        /// <summary>
        /// scene-config keys that are AUTHORED but deliberately have no consumer yet. Every
        /// entry is a debt with a named home - not "we forgot". Anything NOT on this list
        /// that loses its consumer FAILS Case 2, which is the whole point.
        /// </summary>
        private static readonly Dictionary<string, string> KnownDeadKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "faction",     "cosmetic/lore tag; no gameplay home yet (banner art lane)." },
                { "themeColor",  "banner/accent hex; the raid selection UI does not tint yet." },
                { "oneStarTime", "documented as 'no upper bound' (always 0) - informational only." },
                // WO-932: eliteCount REMOVED from this ledger — RaidGarrisonSpawner.ExpandComposition
                // now appends eliteCount copies of the strongest composition / boss id.
            };

        // =====================================================================
        //  Entry points
        // =====================================================================

        /// <summary>Standalone batchmode entry - logs the marker line.</summary>
        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? "RAID_ARENA_SHAPE_OK :: " : "RAID_ARENA_SHAPE_FAIL :: ") + reason);
        }

        /// <summary>DataRegression-shaped contract. Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RAID ARENA SHAPE / OBJECTIVE / BALANCE ---");

            try
            {
                var configs = LoadRaidConfigs(failures, log);

                CaseFootprint(configs, failures, log);
                CaseKeyConsumers(failures, log);
                CaseSpireContract(failures, log);
                CaseNavMesh(configs, failures, log);
                CaseBalanceAndScoring(failures, log);
                CaseArenaBoundary(configs, failures, log);
            }
            catch (Exception ex)
            {
                // The contract is "never throws" - a bug in the oracle is a FAIL, not a crash.
                failures.Add("oracle threw: " + ex.GetType().Name + " " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures.Take(8)) +
                         (failures.Count > 8 ? $" (+{failures.Count - 8} more)" : "");
                Debug.Log(log.ToString());
                return false;
            }

            reason = log.ToString().Replace("\r", "").Replace("\n", " ").Trim();
            if (reason.Length > 900) reason = reason.Substring(0, 900) + " ...";
            return true;
        }

        // =====================================================================
        //  Data load
        // =====================================================================

        private sealed class RaidCfg
        {
            public string Id;
            public string SceneName;
            public string Difficulty;
            public float BaseRadius;
            public int MinSlots;
            public int Towers;
            public string CentralBuilding;
            public bool HasTowersArray;
        }

        private static List<RaidCfg> LoadRaidConfigs(List<string> failures, StringBuilder log)
        {
            var list = new List<RaidCfg>();

            string resText = TryRead(ConfigsRes);
            string saText = TryRead(ConfigsSA);
            if (resText == null || saText == null)
            {
                failures.Add("scene-configs.json missing from " +
                             (resText == null ? "Resources" : "StreamingAssets"));
                return list;
            }
            if (!string.Equals(resText, saText, StringComparison.Ordinal))
                failures.Add("scene-configs.json dual copies DIFFER (Resources vs StreamingAssets) - " +
                             "the Resources copy wins at load, so the shipped raid would not match the authored one.");

            JObject root;
            try { root = JObject.Parse(resText); }
            catch (Exception ex) { failures.Add("scene-configs.json does not parse: " + ex.Message); return list; }

            var configs = root["configs"] as JArray;
            if (configs == null) { failures.Add("scene-configs.json has no configs[] array."); return list; }

            foreach (var c in configs.OfType<JObject>())
            {
                string scene = (string)c["sceneName"] ?? "";
                // Only the GENERATED raid bases carry geometry; Village2 / player_outpost do not.
                if (!scene.StartsWith("RaidBase", StringComparison.OrdinalIgnoreCase)) continue;

                list.Add(new RaidCfg
                {
                    Id = (string)c["id"] ?? "<no id>",
                    SceneName = scene,
                    Difficulty = (string)c["difficulty"] ?? "",
                    BaseRadius = c["baseRadius"] != null ? (float)c["baseRadius"] : 0f,
                    MinSlots = c["wallSegmentsPerSide"] != null ? (int)c["wallSegmentsPerSide"] : 0,
                    Towers = (c["archerTowerCount"] != null ? (int)c["archerTowerCount"] : 0) +
                             (c["mageTowerCount"] != null ? (int)c["mageTowerCount"] : 0),
                    CentralBuilding = (string)c["centralBuilding"] ?? "",
                    HasTowersArray = c["towers"] is JArray ta && ta.Count > 0,
                });
            }

            if (list.Count == 0)
                failures.Add("no RaidBase_* configs found in scene-configs.json - the raid lane has no data.");
            return list;
        }

        // =====================================================================
        //  Case 1 [arena-footprint] - the base must FILL its space, from data.
        // =====================================================================

        private static void CaseFootprint(List<RaidCfg> configs, List<string> failures, StringBuilder log)
        {
            foreach (var c in configs)
            {
                if (c.BaseRadius <= 0f)
                {
                    failures.Add($"[footprint] '{c.Id}' authors no baseRadius - the generator would fall back " +
                                 "to a tier default and the data would not be in charge.");
                    continue;
                }

                float fraction = (c.BaseRadius * c.BaseRadius) / (MapHalfExtent * MapHalfExtent);
                if (fraction < MinFootprintFraction)
                    failures.Add($"[footprint] '{c.Id}' baseRadius {c.BaseRadius:F1}m = only {fraction:P1} of the " +
                                 $"{MapHalfExtent * 2f:F0}m floor (floor is {MinFootprintFraction:P0}). This IS the " +
                                 "'square room' regression - the raid does not fill its space.");
                if (fraction > MaxFootprintFraction)
                    failures.Add($"[footprint] '{c.Id}' baseRadius {c.BaseRadius:F1}m = {fraction:P1} of the floor - " +
                                 "past the plane's usable area; the builder would clamp it and the data would lie.");

                if (c.Towers < 3)
                    failures.Add($"[footprint] '{c.Id}' authors only {c.Towers} turret(s) " +
                                 "(archerTowerCount + mageTowerCount) - the concept calls for 4 / 7 / 10 by tier.");
                if (c.MinSlots < 3)
                    failures.Add($"[footprint] '{c.Id}' wallSegmentsPerSide {c.MinSlots} is below the builder's " +
                                 "floor of 3 - the gate could not centre.");
                if (string.IsNullOrEmpty(c.CentralBuilding))
                    failures.Add($"[footprint] '{c.Id}' authors no centralBuilding - it would have NO SPIRE, " +
                                 "and with no spire the raid falls back to the corpse-count win condition.");

                log.AppendLine($"[footprint] {c.Id} ({c.Difficulty}): r={c.BaseRadius:F0}m = {fraction:P0} of floor, " +
                               $"{c.Towers} turrets, spire art '{c.CentralBuilding}', " +
                               $"towers[] palette={(c.HasTowersArray ? "yes" : "NO (fallback type)")}.");
            }

            // Source-lint: the ring must be RADIUS-driven. A revert to the slot-driven
            // maths is exactly how the 21.6 m square came back.
            string gen = TryRead(GeneratorSrc);
            if (gen == null)
            {
                failures.Add("[footprint] cannot read " + GeneratorSrc);
                return;
            }
            RequireAll(failures, "[footprint]", gen, GeneratorSrc,
                "def.baseRadius",        // the authored radius is actually consumed
                "targetHalfExtent",      // BuildRing is radius-driven, not slot-driven
                "MapHalfExtent");        // and it is measured against the real ground plane
        }

        // =====================================================================
        //  Case 2 [config-key-consumers] - THE general dead-key check.
        //  Every key authored on a scene-config must be READ by a real (non-test,
        //  non-regression) consumer of SceneConfigDef, or be on the documented
        //  KnownDeadKeys ledger. This is the class of rot that hid centralBuilding
        //  and eliteCount for months.
        // =====================================================================

        private static void CaseKeyConsumers(List<string> failures, StringBuilder log)
        {
            string resText = TryRead(ConfigsRes);
            if (resText == null) { failures.Add("[keys] cannot read " + ConfigsRes); return; }

            JObject root;
            try { root = JObject.Parse(resText); }
            catch { failures.Add("[keys] scene-configs.json does not parse."); return; }

            var configs = root["configs"] as JArray;
            if (configs == null) { failures.Add("[keys] no configs[]."); return; }

            // Every TOP-LEVEL key authored on any config (underscore keys are notes).
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var c in configs.OfType<JObject>())
                foreach (var p in c.Properties())
                    if (!p.Name.StartsWith("_", StringComparison.Ordinal))
                        keys.Add(p.Name);

            // The consumer corpus: every .cs that touches SceneConfigDef / SceneConfigCatalog,
            // EXCLUDING the declaration itself and EXCLUDING tests + regressions (an oracle
            // must never be able to satisfy itself).
            var corpus = new List<string>();
            foreach (var file in EnumerateScripts())
            {
                string norm = file.Replace('\\', '/');
                if (norm.EndsWith("/SceneConfigCatalog.cs", StringComparison.Ordinal)) continue;
                if (norm.Contains("/Editor/Regression/")) continue;
                if (norm.Contains("/Tests/")) continue;
                string text = TryRead(file);
                if (text == null) continue;
                if (text.IndexOf("SceneConfigDef", StringComparison.Ordinal) < 0 &&
                    text.IndexOf("SceneConfigCatalog", StringComparison.Ordinal) < 0) continue;
                corpus.Add(text);
            }

            if (corpus.Count == 0)
            {
                failures.Add("[keys] found NO SceneConfigDef consumers at all - the scan is broken or the " +
                             "raid data pipeline was deleted.");
                return;
            }

            var dead = new List<string>();
            var live = 0;
            foreach (var key in keys)
            {
                var rx = new Regex(@"\." + Regex.Escape(key) + @"\b", RegexOptions.CultureInvariant);
                bool found = corpus.Any(t => rx.IsMatch(t));
                if (found) { live++; continue; }

                if (KnownDeadKeys.TryGetValue(key, out string why))
                {
                    dead.Add($"{key} (known: {why})");
                    continue;
                }
                failures.Add($"[keys] scene-configs.json authors '{key}' and NOTHING reads it. Either give it a " +
                             "consumer or add it to RaidArenaShapeRegression.KnownDeadKeys with the reason. " +
                             "(This is the check that would have caught centralBuilding + eliteCount.)");
            }

            // The ledger must not rot the other way either: a key that GAINED a consumer
            // should come off the list, otherwise the list stops meaning anything.
            foreach (var kv in KnownDeadKeys)
            {
                if (!keys.Contains(kv.Key)) continue;
                var rx = new Regex(@"\." + Regex.Escape(kv.Key) + @"\b", RegexOptions.CultureInvariant);
                if (corpus.Any(t => rx.IsMatch(t)))
                    failures.Add($"[keys] '{kv.Key}' is on the KnownDeadKeys ledger but now HAS a consumer - " +
                                 "remove it from the ledger so the list keeps meaning something.");
            }

            log.AppendLine($"[keys] {keys.Count} authored key(s): {live} with a live consumer, " +
                           $"{dead.Count} on the documented dead ledger [{string.Join("; ", dead)}], " +
                           $"scanned {corpus.Count} consumer file(s).");
        }

        // =====================================================================
        //  Case 3 [spire-contract] - the objective must be KILLABLE BY THE HERO.
        // =====================================================================

        private static void CaseSpireContract(List<string> failures, StringBuilder log)
        {
            Type spire = null;
            try { spire = typeof(DeNelle.Village.World.Camps.RaidSpire); }
            catch (Exception ex) { failures.Add("[spire] RaidSpire type unavailable: " + ex.Message); }

            if (spire != null)
            {
                bool structure = typeof(DeNelle.Core.Combat.IDamageableStructure).IsAssignableFrom(spire);
                bool damageable = typeof(DeNelle.Core.Combat.IDamageable).IsAssignableFrom(spire);

                if (!structure)
                    failures.Add("[spire] RaidSpire does NOT implement IDamageableStructure - the enemy contact / " +
                                 "burn / siege seam cannot touch it.");
                if (!damageable)
                    failures.Add("[spire] RaidSpire does NOT implement IDamageable - THE OBJECTIVE WOULD BE " +
                                 "UNKILLABLE BY THE HERO AND BY EVERY TROOP, so the raid would be unwinnable. " +
                                 "The hero's swing resolves GetComponentInParent<IDamageable>() on the Enemy " +
                                 "layer and rejects anything whose Faction != Hostile.");

                log.AppendLine($"[spire] RaidSpire implements IDamageableStructure={structure} IDamageable={damageable}.");
            }

            string src = TryRead(SpireSrc);
            if (src == null) failures.Add("[spire] cannot read " + SpireSrc);
            else
                RequireAll(failures, "[spire]", src, SpireSrc,
                    "CombatFaction.Hostile",          // the hero's faction gate accepts it
                    "LayerMask.NameToLayer(\"Enemy\")", // it sits on the mask the sweep queries
                    "ApplyContactDamage",
                    "OnDestroyedEvent");              // the win signal

            // BOTH ENDS OF THE SEAM. If the hero's attack path is ever changed to resolve a
            // different interface, this fires - which is the failure mode that would silently
            // make the objective unkillable again.
            string hero = TryRead(HeroAttackSrc);
            if (hero == null) failures.Add("[spire] cannot read " + HeroAttackSrc);
            else
                RequireAll(failures, "[spire]", hero, HeroAttackSrc,
                    "GetComponentInParent<IDamageable>()",
                    "CombatFaction.Hostile");

            string troop = TryRead(TroopSrc);
            if (troop == null) failures.Add("[spire] cannot read " + TroopSrc);
            else RequireAll(failures, "[spire]", troop, TroopSrc, "GetComponentInParent<IDamageable>()");

            string victory = TryRead(VictorySrc);
            if (victory == null) failures.Add("[spire] cannot read " + VictorySrc);
            else
                RequireAll(failures, "[spire]", victory, VictorySrc,
                    "OnDestroyedEvent",      // the victory path listens to the spire
                    "HandleVictory");        // and routes it through the one victory flow

            // The builder must actually PLACE one.
            string gen = TryRead(GeneratorSrc);
            if (gen != null) RequireAll(failures, "[spire]", gen, GeneratorSrc, "RaidSpire", "def.centralBuilding");
        }

        // =====================================================================
        //  Case 4 [navmesh] - a built raid scene must have something to walk on.
        // =====================================================================

        private static void CaseNavMesh(List<RaidCfg> configs, List<string> failures, StringBuilder log)
        {
            int checkedScenes = 0;
            foreach (var c in configs)
            {
                string scenePath = Path.Combine(ScenesDir, c.SceneName + ".unity").Replace('\\', '/');
                if (!File.Exists(scenePath))
                {
                    failures.Add($"[navmesh] '{c.Id}' declares sceneName '{c.SceneName}' but {scenePath} does not " +
                                 "exist - the level was never baked.");
                    continue;
                }

                // NavMeshSettings sits near the top of a .unity; read a bounded head so a huge
                // scene never costs the gate anything.
                string head;
                try { head = string.Join("\n", File.ReadLines(scenePath).Take(400)); }
                catch (Exception ex) { failures.Add($"[navmesh] cannot read {scenePath}: {ex.Message}"); continue; }

                // NOTE the trailing '\}': the brace-balance gate counts raw braces in the file,
                // so an unpaired '\{' in a regex literal trips it. Matching the whole {...} keeps
                // the pattern correct AND the file balanced.
                var m = Regex.Match(head, @"m_NavMeshData:\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-f]+))?.*?\}");
                bool hasData = m.Success && m.Groups[1].Value != "0" && m.Groups[2].Success;
                if (!hasData)
                    failures.Add($"[navmesh] {c.SceneName}.unity has NO baked NavMesh (NavMeshSettings.m_NavMeshData " +
                                 "is empty). The hero and every troop/enemy is a NavMeshAgent - this is a room " +
                                 "nobody can walk in. Run DeNelle.Editor.RaidNavBake.BakeAll.");

                string assetPath = Path.Combine(ScenesDir, c.SceneName, "NavMesh.asset").Replace('\\', '/');
                if (!File.Exists(assetPath))
                    failures.Add($"[navmesh] {assetPath} is missing - the scene references navmesh data that is " +
                                 "not on disk.");
                checkedScenes++;
            }

            // ONE baker, and its ground must still cover the (now much larger) arenas.
            string bake = TryRead(NavBakeSrc);
            if (bake == null)
            {
                failures.Add("[navmesh] cannot read " + NavBakeSrc + " - the raid scenes have no baker.");
                return;
            }

            float maxRadius = configs.Count > 0 ? configs.Max(c => c.BaseRadius) : 0f;
            var gs = Regex.Match(bake, @"GroundScale\s*=\s*([0-9.]+)f");
            if (!gs.Success)
                failures.Add("[navmesh] RaidNavBake.GroundScale not found - cannot prove the ground still covers " +
                             "the arenas.");
            else
            {
                float scale = float.Parse(gs.Groups[1].Value, CultureInfo.InvariantCulture);
                float halfExtent = scale * 10f * 0.5f;     // a Unity Plane is 10 m per unit of scale
                float needed = maxRadius + 10f;            // arena + the hero-entry apron outside the gate
                if (halfExtent < needed)
                    failures.Add($"[navmesh] RaidNavBake.GroundScale {scale} gives a +/-{halfExtent:F0}m floor, but " +
                                 $"the biggest arena needs +/-{needed:F0}m (baseRadius {maxRadius:F0} + entry apron). " +
                                 "The hero would spawn off the navmesh. Raise GroundScale AND " +
                                 "RaidBaseGenerator.MapHalfExtent together.");
                log.AppendLine($"[navmesh] {checkedScenes} raid scene(s) carry baked data; RaidNavBake floor " +
                               $"+/-{halfExtent:F0}m covers the biggest arena (+/-{needed:F0}m needed).");
            }

            foreach (var c in configs)
                if (bake.IndexOf(c.SceneName, StringComparison.Ordinal) < 0)
                    failures.Add($"[navmesh] RaidNavBake does not list '{c.SceneName}' - that level would ship " +
                                 "without a navmesh after a re-generate.");
        }

        // =====================================================================
        //  Case 5 [balance + objective-scoring] - threatening, not instantly fatal,
        //  and a readout that agrees with the win condition.
        // =====================================================================

        private static void CaseBalanceAndScoring(List<string> failures, StringBuilder log)
        {
            string gen = TryRead(GeneratorSrc);
            if (gen == null) { failures.Add("[balance] cannot read " + GeneratorSrc); return; }

            // The ceiling must still be ENFORCED, not just declared.
            RequireAll(failures, "[balance]", gen, GeneratorSrc,
                "WorstCaseDps(",                       // the arena is actually sampled
                "tier.TowerDpsBudget / worstRawDps",   // and the damage is scaled to the budget
                "TowerRangeFractionOfRadius");         // and no turret can blanket the arena

            var budgets = Regex.Matches(gen, @"TowerDpsBudget\s*=\s*([0-9.]+)f")
                               .Cast<Match>()
                               .Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                               .ToList();
            if (budgets.Count < 3)
                failures.Add($"[balance] expected a DPS budget for each of the 3 difficulty tiers, found " +
                             $"{budgets.Count} - a tier lost its ceiling.");

            foreach (float b in budgets)
            {
                if (b <= 0f) { failures.Add("[balance] a tower DPS budget is <= 0 (turrets would be inert)."); continue; }
                float ttd = HeroBaseHp / b;
                if (ttd < MinTimeToDeathSeconds)
                    failures.Add($"[balance] a tower DPS budget of {b:F0} kills a {HeroBaseHp:F0} HP hero in " +
                                 $"{ttd:F1}s - under the {MinTimeToDeathSeconds:F0}s floor. Aggressive must mean " +
                                 "threatening, not instantly fatal.");
            }
            if (budgets.Count > 0)
                log.AppendLine($"[balance] tower DPS budgets [{string.Join(", ", budgets.Select(b => b.ToString("F0")))}] " +
                               $"=> worst-case hero time-to-death " +
                               $"[{string.Join(", ", budgets.Select(b => (HeroBaseHp / b).ToString("F1") + "s"))}] at " +
                               $"{HeroBaseHp:F0} HP.");

            // The readout must agree with the objective. "Razed N%" fed by a corpse count
            // while the win condition is a spire is exactly the lie this pins.
            string scoring = TryRead(ScoringSrc);
            if (scoring == null) failures.Add("[scoring] cannot read " + ScoringSrc);
            else
                RequireAll(failures, "[scoring]", scoring, ScoringSrc,
                    "RaidWon",                 // the objective is the win condition
                    "ComputeStars(RaidWon",    // and it is what feeds the star ladder
                    "SpireWeight",             // destruction% is objective-weighted
                    "HasObjective");           // legacy spire-less bases degrade, not break

            string hud = TryRead(HudSrc);
            if (hud == null) failures.Add("[scoring] cannot read " + HudSrc);
            else
                RequireAll(failures, "[scoring]", hud, HudSrc,
                    "SPIRE",                   // the HUD names the objective
                    "ObjectiveHpFraction",     // and shows its real HP
                    "DestructionPct");         // the scoring number is still shown, just demoted
        }

        // =====================================================================
        //  Helpers - all I/O guarded; the contract is "never throws".
        // =====================================================================

        private static void RequireAll(List<string> failures, string tag, string text, string path, params string[] tokens)
        {
            foreach (var t in tokens)
                if (text.IndexOf(t, StringComparison.Ordinal) < 0)
                    failures.Add($"{tag} {Path.GetFileName(path)} no longer contains '{t}'.");
        }

        // =====================================================================
        //  Case 6 [arena-boundary] - the raid arena is ENCLOSED (WO-1632).
        //
        //  Owner 2026-09-10: "exterior walls around entire arena" / "similar strategy as
        //  we used in battle arena". Before this ticket the ONLY walls a raid scene carried
        //  were the base's own rings - the 2026-09-10 bake log printed exactly 'Outer' and
        //  'Keep1' - so the 140 m plane's outer 15-40 m was open ground with nothing at the
        //  edge. This case pins that the enclosure exists, that it is SHARED with the battle
        //  arena rather than copied, and that it never fences the player's own staging
        //  marker out.
        //
        //  It is RED-first BY CONSTRUCTION: the per-config scene check reads the BAKED
        //  .unity, so it fails on the current tree and only goes green after the lead runs
        //  RaidBaseGenerator.BuildAllRaidScenes. That is the point - a source-only lint
        //  would have passed the moment the code compiled, and proved nothing about the
        //  scenes the player loads.
        // =====================================================================

        /// <summary>Ring root object name authored by RaidBaseGenerator.BuildArenaBoundary.</summary>
        private const string BoundaryRootName = "ArenaBoundary_Ring";
        /// <summary>Per-piece name prefix; one per side, so all four sides are provable.</summary>
        private const string BoundaryPiecePrefix = "ArenaBoundary_";
        /// <summary>Root name the builder uses INSTEAD when its containment assert fires.</summary>
        private const string BoundaryUnsafeRootName = "ArenaBoundary_Ring_CONTAINMENT_FAIL";
        /// <summary>The builder's own error token - what a bake log carries when containment fails.</summary>
        private const string BoundaryAssertToken = "ARENA BOUNDARY ASSERT";
        /// <summary>The builder's ring line - identifies a log as a raid bake worth judging.</summary>
        private const string BoundaryRingLogToken = "ring 'Arena'";
        /// <summary>Batchmode entry that bakes the parked flagship (it is NOT a scene-config).</summary>
        private const string IronBastionEntry = "DeNelle.Editor.RaidBaseGenerator.BuildToNewScene";

        private static void CaseArenaBoundary(List<RaidCfg> configs, List<string> failures, StringBuilder log)
        {
            // -- 1. The builder wires the shared ring, with no gate. -------------
            string gen = TryRead(GeneratorSrc);
            if (gen == null) { failures.Add("[arena-boundary] cannot read " + GeneratorSrc); return; }

            RequireAll(failures, "[arena-boundary]", gen, GeneratorSrc,
                "ArenaBoundaryHalfExtent",                  // the ring sits at the PLANE edge, not the base
                "ArenaBoundaryRing.PlaceSquarePerimeter",   // square, because staging uses the corners
                "ArenaBoundaryRing.RockPaths",              // the battle arena's own boundary palette
                "AssertBoundaryContainsStaging",            // and it is proven to contain the markers
                "boundary.InwardReach",                     // from a MEASURED reach, not a scale guess
                "ArenaBoundaryBandHalf",                    // and the ring is fitted INTO a band
                "ArenaBoundaryContainmentSlack",            // with a NAMED margin, not a knife edge
                "ArenaBoundaryContainmentHeadroom",         // designed to BEAT the margin, not equal it
                "ArenaBoundaryContainmentEpsilon",          // and compared with an explicit epsilon
                BoundaryUnsafeRootName,                     // a failed containment marks the SCENE
                "gates=[none]");                            // an exterior boundary is never a gate

            // -- 2. It is SHARED with the battle arena, not copied. --------------
            string helper = TryRead(BoundaryHelperSrc);
            if (helper == null)
                failures.Add("[arena-boundary] " + BoundaryHelperSrc + " is missing - the raid boundary would " +
                             "have to carry its own copy of the battle arena's ring vocabulary, which is the " +
                             "duplicated-state failure this ticket exists to avoid.");
            else
                RequireAll(failures, "[arena-boundary]", helper, BoundaryHelperSrc,
                    "PlaceSquarePerimeter", "PlacePolarRing", "MeasureMinFootprint");

            string siege = TryRead(SiegeArenaSrc);
            if (siege == null)
                failures.Add("[arena-boundary] cannot read " + SiegeArenaSrc);
            else if (siege.IndexOf("ArenaBoundaryRing.PlacePolarRing", StringComparison.Ordinal) < 0)
                failures.Add("[arena-boundary] " + Path.GetFileName(SiegeArenaSrc) + " no longer routes through " +
                             "ArenaBoundaryRing - the battle arena and the raid arena have drifted back into two " +
                             "copies of the same ring code.");

            // -- 3. The constants still contain the staging clamp. ---------------
            //    PlaceStagingMarker's diagonal fallback clamps at (MapHalfExtent -
            //    StagingPlaneEdgeMargin) PER AXIS, so the ring's inner face must sit further
            //    out than that or a marker could land on / outside the boundary.
            //
            //    THE BAND IS THE INVARIANT, and it is checkable WITHOUT opening a mesh - which
            //    is why it replaced the old scale-priced floor bound. The first bake proved a
            //    lint that prices a piece at (scaleMax x 1 m) is worthless: the real mesh was
            //    3.38 m across, so the assert fired at bake while the lint sat green. What IS
            //    provable here is the band's own arithmetic: the ring line must be the midpoint
            //    of the band, the band must be positive, and the fill must leave slack. The
            //    piece-vs-band fit is measured at bake and asserted there
            //    (RaidBaseGenerator.AssertBoundaryContainsStaging + the step-4/5 pins).
            float edgeMargin = ReadConstF(gen, "StagingPlaneEdgeMargin");
            float tolerance = ReadConstF(gen, "ArenaBoundaryEdgeTolerance");
            float fill = ReadConstF(gen, "ArenaBoundaryBandFill");
            float slack = ReadConstF(gen, "ArenaBoundaryContainmentSlack");
            float headroom = ReadConstF(gen, "ArenaBoundaryContainmentHeadroom");
            if (float.IsNaN(edgeMargin) || float.IsNaN(tolerance) || float.IsNaN(fill) ||
                float.IsNaN(slack) || float.IsNaN(headroom))
            {
                failures.Add("[arena-boundary] could not read StagingPlaneEdgeMargin / " +
                             "ArenaBoundaryEdgeTolerance / ArenaBoundaryBandFill / " +
                             "ArenaBoundaryContainmentSlack / ArenaBoundaryContainmentHeadroom out of " +
                             Path.GetFileName(GeneratorSrc) + " - the band invariant cannot be proven, " +
                             "so it is treated as broken.");
            }
            else
            {
                float bandHalf = (edgeMargin + tolerance) * 0.5f;
                float ringLine = MapHalfExtent + (tolerance - edgeMargin) * 0.5f;
                float clampCorner = MapHalfExtent - edgeMargin;
                float edgeLimit = MapHalfExtent + tolerance;

                if (bandHalf <= 0.25f)
                    failures.Add($"[arena-boundary] the band is {bandHalf * 2f:F2}m wide - there is no room " +
                                 "for a boundary piece between the staging clamp and the plane edge.");
                if (fill <= 0f || fill >= 1f)
                    failures.Add($"[arena-boundary] ArenaBoundaryBandFill is {fill:F2}; it must sit strictly " +
                                 "between 0 and 1 or the widest piece is allowed to fill (or overflow) the " +
                                 "whole band, leaving the containment assert no slack and the jitter nothing " +
                                 "to spend.");

                // THE FILL MUST LEAVE ROOM FOR THE MARGIN. This is the second-bake defect made
                // un-repeatable: the ring built exactly to spec, the jitter bound consumed the
                // whole remaining half-band, and the inner face landed EXACTLY on the staging
                // clamp - zero clearance, asserting on all three configs. The live invariant is
                // bandHalf*fill + jitter + slack <= bandHalf; with jitter >= 0 the checkable part
                // is fill <= 1 - slack/bandHalf.
                if (slack <= 0f)
                    failures.Add($"[arena-boundary] ArenaBoundaryContainmentSlack is {slack:F2}. Zero is not a " +
                                 "margin: an inner face that merely EQUALS the staging clamp puts the marker " +
                                 "on a boulder, and that is exactly what shipped from the second bake.");
                else if (bandHalf > 0.25f && fill > 1f - ((slack + headroom) / bandHalf) + 0.0001f)
                    failures.Add($"[arena-boundary] ArenaBoundaryBandFill {fill:F2} leaves no room for the " +
                                 $"{slack:F2}m containment margin plus its {headroom:F2}m headroom in a " +
                                 $"{bandHalf:F2}m half-band: the widest piece alone reaches " +
                                 $"{bandHalf * fill:F2}m, so reach + slack + headroom = " +
                                 $"{bandHalf * fill + slack + headroom:F2}m > {bandHalf:F2}m and the radial " +
                                 $"jitter has nothing left. The ceiling is fill <= " +
                                 $"{1f - ((slack + headroom) / bandHalf):F2}.");
                if (ringLine - bandHalf < clampCorner - 0.001f || ringLine + bandHalf > edgeLimit + 0.001f)
                    failures.Add($"[arena-boundary] the ring line +/-{ringLine:F2}m +/- a {bandHalf:F2}m half-band " +
                                 $"does not sit inside [{clampCorner:F2}, {edgeLimit:F2}] - the ring is no longer " +
                                 "the band's midpoint, so a piece can reach the staging clamp or hang off the world.");
                else
                    log.AppendLine($"[arena-boundary] band OK: ring line +/-{ringLine:F2}m is the midpoint of " +
                                   $"[{clampCorner:F2}, {edgeLimit:F2}] (half-band {bandHalf:F2}m, fill {fill:F2} " +
                                   $"<= {1f - ((slack + headroom) / bandHalf):F2}, margin {slack:F2}m + " +
                                   $"headroom {headroom:F2}m -> the measured fit is asserted at bake).");
            }

            // -- 4. EVERY baked raid scene actually carries the ring, all four sides.
            foreach (var c in configs)
            {
                string scenePath = Path.Combine(ScenesDir, c.SceneName + ".unity").Replace('\\', '/');
                if (!File.Exists(scenePath))
                {
                    failures.Add($"[arena-boundary] '{c.Id}' scene {scenePath} does not exist.");
                    continue;
                }

                bool hasRoot = false;
                bool hasUnsafeRoot = false;
                var sides = new SortedSet<string>(StringComparer.Ordinal);
                try
                {
                    foreach (var line in File.ReadLines(scenePath))
                    {
                        int idx = line.IndexOf(BoundaryPiecePrefix, StringComparison.Ordinal);
                        if (idx < 0) continue;
                        if (line.IndexOf(BoundaryUnsafeRootName, StringComparison.Ordinal) >= 0)
                        { hasUnsafeRoot = true; continue; }
                        if (line.IndexOf(BoundaryRootName, StringComparison.Ordinal) >= 0) { hasRoot = true; continue; }

                        int sideAt = idx + BoundaryPiecePrefix.Length;
                        if (sideAt < line.Length)
                        {
                            string side = line.Substring(sideAt, 1);
                            if (side == "S" || side == "E" || side == "N" || side == "W") sides.Add(side);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"[arena-boundary] cannot read {scenePath}: {ex.Message}");
                    continue;
                }

                // The builder renames its own root when containment fails, so the SCENE carries
                // the finding and this reds with no log parsing at all.
                if (hasUnsafeRoot)
                    failures.Add($"[arena-boundary] '{c.Id}' ({c.SceneName}.unity) carries " +
                                 $"'{BoundaryUnsafeRootName}' - the builder's containment assert FIRED on the " +
                                 "bake that produced this scene: a staging marker, the hero entry or the ring " +
                                 "itself falls outside the legal band. Read the ARENA BOUNDARY ASSERT line in " +
                                 "that bake log, fix the band, re-bake. This scene must not ship.");
                else if (!hasRoot)
                    failures.Add($"[arena-boundary] '{c.Id}' ({c.SceneName}.unity) has NO '{BoundaryRootName}' - " +
                                 "the arena is not enclosed. The hero and the troops reach bare plane edge on " +
                                 "every side. Re-bake: the three scene-config raids come from " +
                                 "DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes, and RaidBase_IronBastion " +
                                 "is NOT one of them - it has its own parameterless batchmode entry, " +
                                 IronBastionEntry + ". Both are needed for this case to go green.");
                else if (sides.Count < 4)
                    failures.Add($"[arena-boundary] '{c.Id}' has a boundary ring on only {sides.Count}/4 side(s) " +
                                 $"[{string.Join(",", sides)}] - 'entire arena' means all four.");
                else
                    log.AppendLine($"[arena-boundary] {c.Id}: {BoundaryRootName} present, all 4 sides.");
            }

            // -- 5. The BAKE LOG itself must be clean of the builder's assert. ---
            CaseArenaBoundaryBakeLogs(failures, log);

            // -- 6. THE DESIGNED FIT, evaluated here, with no bake. --------------
            CaseArenaBoundaryDesignedFit(gen, failures, log);
        }

        // ---- The palette, as MEASURED by the bakes -----------------------------
        // Back-solved from the builder's own printed values, twice, and consistent both times:
        //   Builds/wave3-bake3: "piece 4.93m" at scaleMin 2.20  -> thinnest 4.93/2.20 = 2.24 m
        //                       "MEASURED 5.75m inward reach" at scaleMax 3.40
        //                                                      -> widest (5.75*2)/3.40 = 3.38 m
        //   Builds/wave3-bake5: "piece 2.42m ... reach 1.82m" at applied scale 1.08 - same mesh.
        // These are the ONLY numbers in this oracle that come from outside the source tree, and
        // they are the reason it can predict a bake. If the boundary palette is ever changed
        // these go stale - the bake-time assert stays the authority and would still catch it;
        // this case exists so the knife edge is caught BEFORE a 20-minute bake, not instead of it.
        private const float MeasuredThinnestPieceXZ = 2.24f;
        private const float MeasuredWidestPieceXZ = 3.38f;

        /// <summary>
        /// Run the BUILDER'S OWN ARITHMETIC against the measured palette and require the DESIGNED
        /// inner clearance to beat the required one by the headroom.
        /// <para/>
        /// ⚠ WHY THIS CASE EXISTS. The containment assert fired on three consecutive bakes. Bake 3
        /// was the instructive one: the ring was built EXACTLY to the predicted spec and the assert
        /// still failed, because the layout reserved a clearance equal to the requirement and the
        /// strict compare lost to float error (0.2999... &lt; 0.30). Every one of those rounds cost a
        /// full bake to discover. The arithmetic is pure - band, fill, reach, jitter, slack - so
        /// there is no reason a bake should ever be the thing that finds it again.
        /// </summary>
        private static void CaseArenaBoundaryDesignedFit(string gen, List<string> failures, StringBuilder log)
        {
            float edgeMargin = ReadConstF(gen, "StagingPlaneEdgeMargin");
            float tolerance = ReadConstF(gen, "ArenaBoundaryEdgeTolerance");
            float fill = ReadConstF(gen, "ArenaBoundaryBandFill");
            float slack = ReadConstF(gen, "ArenaBoundaryContainmentSlack");
            float headroom = ReadConstF(gen, "ArenaBoundaryContainmentHeadroom");
            float jitterCap = ReadConstF(gen, "ArenaBoundaryRadialJitterCap");
            float scaleMax = ReadConstF(gen, "ArenaBoundaryScaleMax");

            if (float.IsNaN(edgeMargin) || float.IsNaN(tolerance) || float.IsNaN(fill) ||
                float.IsNaN(slack) || float.IsNaN(headroom) || float.IsNaN(jitterCap) ||
                float.IsNaN(scaleMax))
            {
                failures.Add("[arena-boundary] the designed-fit case could not read every constant it " +
                             "needs out of " + Path.GetFileName(GeneratorSrc) + " (StagingPlaneEdgeMargin, " +
                             "ArenaBoundaryEdgeTolerance, ArenaBoundaryBandFill, " +
                             "ArenaBoundaryContainmentSlack, ArenaBoundaryContainmentHeadroom, " +
                             "ArenaBoundaryRadialJitterCap, ArenaBoundaryScaleMax) - the fit cannot be " +
                             "predicted, so it is treated as broken.");
                return;
            }

            // ---- ArenaBoundaryRing.PlaceSquarePerimeter, line for line ----
            float bandHalf = (edgeMargin + tolerance) * 0.5f;
            float ringLine = MapHalfExtent + (tolerance - edgeMargin) * 0.5f;
            float clampCorner = MapHalfExtent - edgeMargin;
            float edgeLimit = MapHalfExtent + tolerance;

            float allowedFootprint = 2f * bandHalf * fill;
            float appliedScaleMax = Mathf.Min(scaleMax, allowedFootprint / MeasuredWidestPieceXZ);
            float maxPieceFootprint = MeasuredWidestPieceXZ * appliedScaleMax;
            float reach = maxPieceFootprint * 0.5f;
            float jitterRoom = bandHalf - slack - headroom - reach;
            float jitter = Mathf.Min(jitterCap, Mathf.Max(0f, jitterRoom));

            float innerFace = ringLine - (reach + jitter);
            float outerFace = ringLine + (reach + jitter);
            float innerSlack = innerFace - clampCorner;
            float outerSlack = edgeLimit - outerFace;
            float wanted = slack + headroom;

            // The DESIGN must beat the requirement, not equal it. Equalling it is precisely the
            // bake-3 defect, and no epsilon in the assert can rescue a layout that aims at the line.
            if (innerSlack < wanted - 0.0005f)
                failures.Add($"[arena-boundary] DESIGNED FIT: the layout reserves only {innerSlack:F3}m of " +
                             $"inner clearance where slack {slack:F3} + headroom {headroom:F3} = " +
                             $"{wanted:F3}m is designed for (ring +/-{ringLine:F3}m, reach {reach:F3}m, " +
                             $"jitter {jitter:F3}m, clamp +/-{clampCorner:F3}m). A design that aims AT the " +
                             "required margin lands on it and the bake-time assert loses to float error - " +
                             "that is the defect this case exists to stop repeating. Lower " +
                             "ArenaBoundaryBandFill or ArenaBoundaryRadialJitterCap.");
            else if (outerSlack < wanted - 0.0005f)
                failures.Add($"[arena-boundary] DESIGNED FIT: the layout reserves only {outerSlack:F3}m of " +
                             $"outer clearance where {wanted:F3}m is designed for (outer face " +
                             $"+/-{outerFace:F3}m vs edge limit +/-{edgeLimit:F3}m). Lower " +
                             "ArenaBoundaryBandFill.");
            else if (jitterRoom <= 0f)
                failures.Add($"[arena-boundary] DESIGNED FIT: the fitted piece leaves {jitterRoom:F3}m for " +
                             "the radial jitter, so the ring would be perfectly mechanical and any future " +
                             "widening of the palette goes straight through the margin. Lower " +
                             "ArenaBoundaryBandFill.");
            else
                log.AppendLine($"[arena-boundary] designed fit OK (no bake needed): palette " +
                               $"{MeasuredThinnestPieceXZ:F2}/{MeasuredWidestPieceXZ:F2}m -> applied scale " +
                               $"{appliedScaleMax:F3}, reach {reach:F3}m + jitter {jitter:F3}m of a " +
                               $"{bandHalf:F3}m half-band; inner {innerFace:F3} vs clamp {clampCorner:F3} = " +
                               $"{innerSlack:F3}m, outer {outerFace:F3} vs limit {edgeLimit:F3} = " +
                               $"{outerSlack:F3}m, both >= slack+headroom {wanted:F3}m.");
        }

        /// <summary>
        /// A raid bake log that contains the builder's ring line must NOT also contain its
        /// containment assert. This is the second half of the WO-1632 step-2 pin: the scene
        /// token (step 4) catches a shipped bad scene, this catches the bake that produced it
        /// even when someone re-baked over the evidence in the scene.
        /// <para/>
        /// Best-effort BY DESIGN, and it never fails for absence: `Builds/` is gitignored, so
        /// on a fresh clone there is nothing to read and that is not a defect. Unity logs are
        /// UTF-16 with embedded NULs, so the bytes are read and the NULs stripped - the same
        /// thing `tr -d '\000'` does at the shell.
        /// </summary>
        private static void CaseArenaBoundaryBakeLogs(List<string> failures, StringBuilder log)
        {
            const long MaxLogBytes = 64L * 1024L * 1024L;
            string[] candidates;
            try
            {
                candidates = Directory.Exists(BuildsDir)
                    ? Directory.GetFiles(BuildsDir, "*bake*", SearchOption.TopDirectoryOnly)
                    : new string[0];
            }
            catch (Exception ex)
            {
                log.AppendLine("[arena-boundary] bake-log scan skipped: " + ex.Message);
                return;
            }

            int scanned = 0, judged = 0;
            foreach (var path in candidates)
            {
                string text;
                try
                {
                    var fi = new FileInfo(path);
                    if (!fi.Exists || fi.Length > MaxLogBytes || fi.Length == 0) continue;
                    var bytes = File.ReadAllBytes(path);
                    var sb = new StringBuilder(bytes.Length);
                    foreach (var b in bytes) if (b != 0 && b < 128) sb.Append((char)b);
                    text = sb.ToString();
                }
                catch { continue; }

                scanned++;
                if (text.IndexOf(BoundaryRingLogToken, StringComparison.Ordinal) < 0) continue;
                judged++;

                if (text.IndexOf(BoundaryAssertToken, StringComparison.Ordinal) >= 0)
                    failures.Add($"[arena-boundary] the raid bake log '{Path.GetFileName(path)}' carries a " +
                                 $"'{BoundaryAssertToken}' line alongside its ring line - the boundary did not " +
                                 "contain the staging clamp, the hero entry or the plane edge on that bake. " +
                                 "Read the full line, fix the band, re-bake. Delete the stale log only after " +
                                 "a clean bake exists.");
            }

            log.AppendLine($"[arena-boundary] bake-log scan: {scanned} readable file(s) under {BuildsDir}, " +
                           $"{judged} carried a raid ring line, 0 tolerated asserts.");
        }

        /// <summary>Read a <c>const float Name = 12.5f;</c> out of source. NaN when absent.</summary>
        private static float ReadConstF(string src, string name)
        {
            var m = Regex.Match(src, @"\b" + Regex.Escape(name) + @"\s*=\s*(-?[0-9]*\.?[0-9]+)f");
            if (!m.Success) return float.NaN;
            return float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                ? v : float.NaN;
        }

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static IEnumerable<string> EnumerateScripts()
        {
            var roots = new[] { "Assets/_Modules", "Assets/Editor", "Assets/_Village2" };
            foreach (var root in roots)
            {
                string[] files;
                try { files = Directory.Exists(root) ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories) : new string[0]; }
                catch { files = new string[0]; }
                foreach (var f in files) yield return f;
            }
        }
    }
}
