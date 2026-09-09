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

        private const string ScenesDir = "Assets/Scenes";

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
