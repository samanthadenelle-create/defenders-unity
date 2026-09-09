// =============================================================================
// SceneConfigCatalog — the FULL typed reader for scene-configs.json.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Where SceneOwnership reads only the {id, sceneName, ownership} slice it needs
// for the runtime ownership flag, THIS reads the WHOLE contract — geometry
// (wallTier/baseRadius/wallSegmentsPerSide/entranceCount/interiorWallLayers/
// towerPlacementStyle/archer+mageTowerCount), garrison, scoring (recommendedClear
// /twoStar/oneStar times, rewardMultiplier, eliteCount, shardDropChance) and
// theme. It is the single typed source the RAID GENERATOR consumes to build a
// level from data alone.
//
// Same WebGL-safe path as SceneOwnership: CanonicalJson.Read (Resources dual-copy
// wins) + Newtonsoft.Json. Parse failures LogWarning and leave an empty catalog —
// no silent failure (CLAUDE.md §12). SceneOwnership can later read ownership off
// this instead of its own private slice; for now it is the generator's source.
//
// NOTE: garrison composition + eliteCount → RaidGarrisonSpawner. rewardMultiplier →
// RaidScoring.ComputeLoot (paid). recommendedClearTime should match the live raid
// clock (180s default). shardDropChance is NOT a live grant yet — selection UI must
// not present it as a drop. Geometry generator ignores all of the above.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core;

namespace DeNelle.Village
{
    /// <summary>One garrison-composition entry (enemyId + count).</summary>
    [Serializable]
    public sealed class GarrisonUnitDef
    {
        public string enemyId;
        public int count;
    }

    /// <summary>Enemy garrison block (spawner/scoring data — not geometry).</summary>
    [Serializable]
    public sealed class GarrisonDef
    {
        public List<GarrisonUnitDef> composition;
        public int baseEnemyLevel;
        public float difficultyMultiplier = 1f;
        public string boss;
        public int levelOffset;
    }

    /// <summary>One tower-prop request ({ type, count }) from the towers[] array.</summary>
    [Serializable]
    public sealed class TowerDef
    {
        public string type;
        public int count;
    }

    /// <summary>The props block ({ set:[ids], count }).</summary>
    [Serializable]
    public sealed class PropsDef
    {
        public List<string> set;
        public int count;
    }

    /// <summary>WO-1608 - one KayKit/Synty/StructureContent dress instance.</summary>
    [Serializable]
    public sealed class RaidDressPropDef
    {
        public string token;
        public int count = 1;
        public string zone;
    }

    /// <summary>
    /// WO-1608 - bake-time raid dress. Tokens resolve via RaidBaseDresser (KayKit /
    /// Synty / StructureContent). Combat stats stay on DefenseTower / WallSegment.
    /// </summary>
    [Serializable]
    public sealed class RaidDressDef
    {
        public string kit;
        public string gate;
        public string wallModule;
        public string floor;
        public string towersVisual;
        public List<RaidDressPropDef> props;
    }

    /// <summary>
    /// The FULL scene-config record — every field in scene-configs.json (existing
    /// + the WO raid-enrichment fields). Geometry fields drive the generator; the
    /// garrison/scoring fields are carried for the spawner + scoring layers.
    /// </summary>
    [Serializable]
    public sealed class SceneConfigDef
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string id;
        public string displayName;

        /// <summary>
        /// The ONE line the raid card shows under the fortress name — the creative canon's
        /// "Line on the target card" (docs/CREATIVE_CANON_ELARION_2026-09-04.md §3).
        ///
        /// <para>⚠ THIS FIELD DID NOT EXIST BEFORE 2026-09-04, in EITHER the class or the JSON.
        /// The creative canon's implementation rule says to "fill the empty description field",
        /// and it was not empty — it was ABSENT (measured this session: no `description` key on
        /// any of the five rows in scene-configs.json, and no such field on this class). That is
        /// why the target card had a slot and nothing in it. Recorded here rather than silently
        /// fixed, per CLAUDE.md §15.</para>
        ///
        /// <para>⛔ Player-facing copy: it comes from the creative canon, never from an
        /// implementer. ASCII only (the build TMP font has no glyph for smart punctuation).</para>
        /// </summary>
        public string description;

        /// <summary>
        /// How many raid VICTORIES the player must have banked before this target unlocks —
        /// the escalation ladder in docs/PROGRAM_RAID_ECONOMY_2026-09-04.md §4
        /// (Camp 1 = 0 / Camp 2 = 3 / Camp 3 = 10 / Iron Bastion = 20).
        ///
        /// <para>0 / absent = always available, which keeps every non-raid row (the player
        /// outpost, Village2) unlocked with no authoring change.</para>
        ///
        /// <para>Authored HERE, in the same catalog that already authors
        /// <see cref="raidCooldownSeconds"/>, rather than as a code literal or a second rail:
        /// the pacing knobs for a camp live on the camp's row. <c>RaidSelectionVM</c> is
        /// the only reader; it turns this into ItemVM.Locked + ItemVM.LockReason.</para>
        /// </summary>
        public int unlockVictories;

        public string sceneName;
        public string ownership;          // "Player" | "Enemy"
        public string faction;            // orc | hollow | troll | mixed | none
        public string difficulty;         // "Regular" | "Hard" | "Extreme" (enrichment)
        public string themeColor;         // hex banner/accent

        // ── Perimeter geometry (generator-consumed) ──────────────────────────
        public string wallTier;           // "Wood" | "Iron" | "ReinforcedSteel"
        public float baseRadius;
        public int wallSegmentsPerSide;   // forced ODD by the generator (gate centring)
        public int entranceCount;         // 1 = single south gate, 2 = south + north
        public int interiorWallLayers;    // extra concentric inner keep rings (0 / 1)
        public string towerPlacementStyle;// "Cardinal" | "OverlappingFire"
        public int archerTowerCount;
        public int mageTowerCount;

        // ── Central / props ──────────────────────────────────────────────────
        public string centralBuilding;
        public List<TowerDef> towers;
        public PropsDef props;
        public RaidDressDef raidDress;

        // ── Garrison + scoring (spawner/scoring layers — NOT geometry) ───────
        public GarrisonDef garrison;
        public float recommendedClearTime;  // 3-star threshold (s)
        public float twoStarTime;            // 2-star threshold (s)
        public float oneStarTime;            // any clear (0 = no upper bound)
        public float rewardMultiplier = 1f;
        public int eliteCount;
        public float shardDropChance;

        /// <summary>
        /// WO-728 - per-camp raid cooldown, in seconds: the recovery RECORD window a clear
        /// stamps on this camp. Authored per camp in scene-configs.json (owner ruling 2026-08-21:
        /// Regular 14400 / Hard 28800 / Extreme 43200 = 4h / 8h / 12h). 0 / absent falls back
        /// to the identical difficulty table in <c>RaidCooldownService.DurationForDifficulty</c>.
        ///
        /// <para>(!) RETIRED AS A GATE - WO-1379 (owner ruling 2026-09-04, landed 2026-09-05):
        /// "Heartfire replaces the camp wall." The raid door (RaidSelectionScreen.OnCardTapped)
        /// no longer refuses on this window; HeartfireService is the one gate on WHEN you may
        /// raid. The field is RETAINED, not deleted: RaidCooldownService.BeginAfterClear still
        /// stamps the record (save evidence - SaveMigrator v41 derives everCompletedRaid from
        /// it), and scene-configs.json marks each row _raidCooldownSecondsSuperseded so the next
        /// seat reads why. DO NOT SHORTEN OR DELETE - retired is not retuned.</para>
        ///
        /// <para>(This doc used to say "THIS IS THE CRYSTAL BOUND, NOT A PACING KNOB". That was
        /// already false before WO-1379: the UTC-day crystal stamp (RaidClaimService.
        /// CrystalsPaidToday, WO-1134) is the crystal bound, and RaidScoring.ComputeLoot now
        /// pays wood and iron too (WO-1374). Read the ruling recorded in RaidCooldownService's
        /// BALANCE block before changing a value here.)</para>
        /// </summary>
        public float raidCooldownSeconds;

        // ── Modifier override (WO-430) — "all scene creations accept an override JSON" ──
        /// <summary>
        /// Optional GameModifiers JSON applied as the active perk override BEFORE this scene
        /// spawns its content (RaidGarrisonSpawner sets it, so troops/garrison are born with
        /// these stats — deterministic + testable). Empty/absent → no override, so the player's
        /// REAL upgrade tiers apply (the normal raid path). Authoring/test affordance.
        /// </summary>
        public string modifierOverride;

        /// <summary>True when this config is an enemy garrison/outpost.</summary>
        public bool IsEnemy =>
            string.Equals(ownership, "Enemy", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Typed catalog of all scene-configs. Loaded once (lazy) from the dual-copy
    /// canonical JSON. <see cref="Find"/> by id; <see cref="All"/> for the list.
    /// </summary>
    public static class SceneConfigCatalog
    {
        /// <summary>StreamingAssets-relative path (CanonicalJson strips the ext for Resources).</summary>
        public const string StreamingRelativePath = "Data/Canonical/scene-configs.json";

        private static List<SceneConfigDef> _all;
        private static Dictionary<string, SceneConfigDef> _byId;

        /// <summary>All scene-config defs (empty list if the JSON is missing/unparseable).</summary>
        public static IReadOnlyList<SceneConfigDef> All
        {
            get { EnsureLoaded(); return _all; }
        }

        /// <summary>The def for an id (case-insensitive), or null if not found.</summary>
        public static SceneConfigDef Find(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id) || _byId == null) return null;
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        /// <summary>
        /// First def whose <see cref="SceneConfigDef.sceneName"/> matches
        /// <paramref name="sceneName"/> (ordinal ignore case), or null.
        /// Used by raid loot to resolve rewardMultiplier when only the loaded scene is known.
        /// </summary>
        public static SceneConfigDef FindBySceneName(string sceneName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(sceneName) || _all == null) return null;
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d != null && !string.IsNullOrEmpty(d.sceneName)
                    && string.Equals(d.sceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return null;
        }

        /// <summary>Force a fresh reload on next access (e.g. after an editor JSON edit).</summary>
        public static void Invalidate()
        {
            _all = null;
            _byId = null;
        }

        private static void EnsureLoaded()
        {
            if (_all != null) return;
            _all = new List<SceneConfigDef>();
            _byId = new Dictionary<string, SceneConfigDef>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string text = CanonicalJson.Read(StreamingRelativePath);
                if (string.IsNullOrEmpty(text))
                {
                    Debug.LogWarning($"[Flow:World] SceneConfigCatalog: {StreamingRelativePath} not found — empty catalog.");
                    return;
                }

                var file = JsonConvert.DeserializeObject<SceneConfigFile>(text);
                if (file == null || file.configs == null)
                {
                    Debug.LogWarning("[Flow:World] SceneConfigCatalog: scene-configs.json parsed empty — empty catalog.");
                    return;
                }

                foreach (var c in file.configs)
                {
                    if (c == null || string.IsNullOrEmpty(c.id)) continue;
                    _all.Add(c);
                    _byId[c.id] = c;
                }
                Debug.Log($"[Flow:World] SceneConfigCatalog loaded {_all.Count} config(s).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Flow:World] SceneConfigCatalog: failed to read scene-configs.json " +
                                 $"({ex.Message}) — empty catalog.");
            }
        }

        // JSON shape: { version, configs:[ SceneConfigDef ] }. Underscore keys
        // (_note/_schema) are ignored by Newtonsoft (no matching field).
        [Serializable]
        private sealed class SceneConfigFile
        {
            public int version;
            public List<SceneConfigDef> configs;
        }
    }
}
