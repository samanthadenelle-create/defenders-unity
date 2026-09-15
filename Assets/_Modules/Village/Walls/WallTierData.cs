// =============================================================================
// WallTierData — the cosmetic + economic data for the CoC wall ladder.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Walls
//
// RECONCILE-FIRST (CLAUDE.md): WallSegment ALREADY owns the durability half of the
// tier system — `_tier` (1..3) + `s_tierToughness {_,1,1.6,2.56}` (incoming contact
// damage is DIVIDED by toughness, ~x1.6 effective-HP per tier) + StructureTierVisual
// (accent tint) + GameState.WallLevel (persisted). This file does NOT duplicate any
// of that. It adds only what was MISSING for the owner's 2026-06-13 ladder:
//   - the tier NAMING (Wood -> Iron -> Reinforced Steel, replacing the old
//     wood/stone/reinforced labels),
//   - the per-tier SEGMENT MESH prefab path (owner art, pending import), and
//   - the per-tier UPGRADE COST (Iron, then Iron + Crystals for the rune-temper).
// Tier index matches WallSegment: 1 = Wood, 2 = Iron, 3 = Reinforced Steel.
// Durability stays in WallSegment.s_tierToughness (single source) — exposed here
// as ToughnessFor() for read-only convenience, NOT redefined.
//
// Pure data — no scene, no save-schema, no mesh dependency (prefab paths are
// placeholders until the owner's meshes land; see docs/PLAN_grid_coc_base_walls.md).
// =============================================================================

using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Village.Walls
{
    /// <summary>The three wall material tiers. Index matches WallSegment._tier (1..3).</summary>
    public enum WallTier
    {
        Wood = 1,
        Iron = 2,
        ReinforcedSteel = 3,
    }

    /// <summary>A tier's display name, segment mesh, and the cost to upgrade INTO it.</summary>
    public readonly struct WallTierDef
    {
        public readonly WallTier Tier;
        public readonly string DisplayName;
        // Cost to UPGRADE INTO this tier (per segment). Wood is the base tier (no
        // upgrade-in cost; placing a fresh wood wall uses BuildWoodCost).
        public readonly int UpgradeWood;
        public readonly int UpgradeIron;
        public readonly int UpgradeCrystals;
        // Resources.Load path to this tier's straight wall-segment prefab (owner art, pending).
        public readonly string SegmentPrefabPath;

        public WallTierDef(WallTier tier, string name,
                           int upWood, int upIron, int upCrystals, string prefabPath)
        {
            Tier = tier; DisplayName = name;
            UpgradeWood = upWood; UpgradeIron = upIron; UpgradeCrystals = upCrystals;
            SegmentPrefabPath = prefabPath;
        }
    }

    /// <summary>Curated wall-tier ladder (cosmetic + cost). Durability = WallSegment toughness.</summary>
    public static class WallTierData
    {
        public const int MinTier = 1;                 // Wood
        public const int MaxTier = 3;                 // Reinforced Steel
        public const int TierCount = 3;
        /// <summary>Cost (Wood) to BUILD a fresh Wood wall segment (Phase 2 player placement).</summary>
        public const int BuildWoodCost = 25;

        // WO-1737 — the MIRROR TABLE IS GONE. This used to be
        // `{ 1f, 1f, 1.6f, 2.56f }`, described in its own comment as a read-only convenience
        // copy of WallSegment's toughness "kept here for UI". It was duplicated state of exactly
        // the kind CLAUDE.md §2/§5/§8 each describe in their own words: the moment the owner's
        // 2026-09-15 durability ruling moved the real divisor, this copy would have gone on
        // reporting the old numbers, and any UI that ever read it would have quietly lied about
        // how tough the player's wall is. Nothing consumed it (ToughnessFor below had zero
        // callers when it was removed), so the copy is deleted rather than re-synced — deleting
        // the copy is the cure, never a better copy.

        private static readonly WallTierDef[] _tiers =
        {
            // index 0 unused (tiers are 1..3, matching WallSegment._tier).
            default,
            new WallTierDef(WallTier.Wood, "Wood Palisade",
                            upWood: 0, upIron: 0, upCrystals: 0,
                            prefabPath: "Walls/wood_wall"),      // owner art, imported 2026-06-12
            new WallTierDef(WallTier.Iron, "Iron-Banded Wall",
                            upWood: 0, upIron: 120, upCrystals: 0,
                            prefabPath: "Walls/iron_wall"),      // owner art, imported 2026-06-12
            // Reinforced Steel — rune-tempered. Iron + Crystals (the magic-temper arc).
            new WallTierDef(WallTier.ReinforcedSteel, "Reinforced Steel",
                            upWood: 0, upIron: 200, upCrystals: 40,
                            // ⚠ THIS COMMENT USED TO READ "PENDING owner art (runic steel)" AND WAS
                            // STALE (corrected 2026-08-21, WO-838). steel_wall.fbx landed 2026-07-14
                            // alongside Walls/Textures/steel_*.JPEG; a seat reading the old note would
                            // have treated 86 white wall slabs in RaidBase_mage_enclave as "art not
                            // delivered yet" instead of the import-wiring defect it actually is.
                            prefabPath: "Walls/steel_wall"),     // owner art, imported 2026-07-14
        };

        /// <summary>The tier def for a 1..3 index (clamped).</summary>
        public static WallTierDef Get(int tier) => _tiers[Mathf.Clamp(tier, MinTier, MaxTier)];

        /// <summary>The tier def for a WallTier.</summary>
        public static WallTierDef Get(WallTier tier) => Get((int)tier);

        /// <summary>True if this tier can still be upgraded (not yet at the top).</summary>
        public static bool CanUpgrade(int tier) => tier < MaxTier;

        /// <summary>The next tier up (clamped at the max).</summary>
        public static WallTierDef Next(int tier) => Get(Mathf.Min(tier + 1, MaxTier));

        /// <summary>
        /// Effective-HP multiplier at a tier. WO-1737: DELEGATES to the single authority
        /// (<see cref="DeNelle.Village.WallSegment.ToughnessFor"/>) instead of reading a local
        /// copy of the curve, so this can never disagree with what actually divides the damage.
        /// Same assembly (DeNelle.Village), so there is no reference to add and no cycle.
        /// </summary>
        public static float ToughnessFor(int tier) =>
            DeNelle.Village.WallSegment.ToughnessFor(Mathf.Clamp(tier, MinTier, MaxTier));
    }

    /// <summary>
    /// CITY-02/03: the RUNTIME consumer of walls.json. Before this, walls.json was fully
    /// orphaned - no loader existed and <c>heartDamageMultiplier</c> / <c>spikeDamagePerSecond</c>
    /// had ZERO readers, so upgrading the perimeter gave the Heart no protection and the
    /// Spiked Steel top tier did nothing (the upgrade UI's mitigation copy was a lie).
    ///
    /// This loads walls.json ONCE through the WebGL-safe <see cref="DeNelle.Core.CanonicalJson"/>
    /// seam (Resources first, StreamingAssets fallback) and exposes the per-wall-level values
    /// the <c>Enemy</c> strike path applies when it damages the Heart. Wall level is
    /// <c>GameState.WallLevel</c> (0..3), which matches walls.json <c>tiers[].level</c>.
    /// Fails safe to a code table with the SAME numbers as walls.json, so mitigation never
    /// silently disappears if the JSON is missing.
    /// </summary>
    public static class WallDefense
    {
        private const string WallsRelativePath = "Data/Canonical/walls.json";

        [System.Serializable]
        private sealed class WallTierJson
        {
            [JsonProperty("level")] public int Level;
            [JsonProperty("heartDamageMultiplier")] public float HeartDamageMultiplier = 1f;
            [JsonProperty("spikeDamagePerSecond")]  public float SpikeDamagePerSecond  = 0f;
            [JsonProperty("targetHeight")]          public float TargetHeight          = 0f;   // WO-948
        }

        [System.Serializable]
        private sealed class WallsFileJson
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("tiers")]   public List<WallTierJson> Tiers = new List<WallTierJson>();
        }

        // Levels 0..3 (== walls.json). Seeded with the walls.json numbers so a missing/failed
        // read is divergence-free (keep these in sync with walls.json).
        private static float[] s_heartMult = { 1.0f, 0.85f, 0.70f, 0.70f };
        private static float[] s_spikeDps  = { 0f,   0f,    0f,    9f    };
        private static float[] s_targetHeight = { 3.0f, 3.8f, 4.5f, 5.2f };   // WO-948
        private static bool s_loaded;

        /// <summary>
        /// WO-948 (owner ruling 2026-08-10): the wall ladder reachable through BUILD MODE tops
        /// out at STONE (walls.json level 1) — walls build at L1 wood and climb ONE rung via
        /// the upgrade verb. Steel/Spiked (levels 2..3) are WO-904's, gated behind raid-steal;
        /// raising this constant is that WO's move, nothing else's.
        /// </summary>
        public const int MaxReachableWallLevel = 1;

        private static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;   // one attempt per session; a failed read keeps the code fallback
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(WallsRelativePath);
                if (string.IsNullOrEmpty(json))
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Wall",
                        "walls.json not found -> using code fallback wall-defense table " +
                        "(heartMult 1.0/0.85/0.70/0.70, spikeDps L3=9).");
                    return;
                }
                var file = JsonConvert.DeserializeObject<WallsFileJson>(json);
                if (file == null || file.Tiers == null || file.Tiers.Count == 0)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Wall",
                        "walls.json parsed to 0 tiers -> keeping code fallback wall-defense table.");
                    return;
                }

                int count = s_heartMult.Length;      // 4 levels (0..3)
                var hm = new float[count];
                var sp = new float[count];
                var th = new float[count];
                for (int i = 0; i < count; i++) { hm[i] = s_heartMult[i]; sp[i] = s_spikeDps[i]; th[i] = s_targetHeight[i]; } // seed w/ fallback
                foreach (var t in file.Tiers)
                {
                    if (t == null || t.Level < 0 || t.Level >= count) continue;
                    if (t.HeartDamageMultiplier > 0f) hm[t.Level] = t.HeartDamageMultiplier;
                    sp[t.Level] = Mathf.Max(0f, t.SpikeDamagePerSecond);
                    if (t.TargetHeight > 0f) th[t.Level] = t.TargetHeight;   // WO-948
                }
                s_heartMult = hm;
                s_spikeDps  = sp;
                s_targetHeight = th;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Wall",
                    $"walls.json loaded: {file.Tiers.Count} tiers; heartMult L0..L3 = " +
                    $"{s_heartMult[0]:0.00}/{s_heartMult[1]:0.00}/{s_heartMult[2]:0.00}/{s_heartMult[3]:0.00}, " +
                    $"spikeDps L3={s_spikeDps[3]:0.#}. Wall upgrades now protect the Heart.");
            }
            catch (System.Exception ex)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Wall",
                    $"walls.json read/parse failed ({ex.GetType().Name}: {ex.Message}) -> code fallback wall-defense table.");
            }
        }

        /// <summary>Incoming-damage multiplier the wall grants the Heart at a wall level (0..3). &lt;1 = protection.</summary>
        public static float HeartDamageMultiplier(int wallLevel)
        {
            EnsureLoaded();
            return s_heartMult[Mathf.Clamp(wallLevel, 0, s_heartMult.Length - 1)];
        }

        /// <summary>Spike damage/sec a wall of this level deals to enemies crossing it (0 below the spiked tier).</summary>
        public static float SpikeDamagePerSecond(int wallLevel)
        {
            EnsureLoaded();
            return s_spikeDps[Mathf.Clamp(wallLevel, 0, s_spikeDps.Length - 1)];
        }

        /// <summary>WO-948 — the wall height (world metres) walls.json authors for a wall level (0..3).</summary>
        public static float TargetHeight(int wallLevel)
        {
            EnsureLoaded();
            return s_targetHeight[Mathf.Clamp(wallLevel, 0, s_targetHeight.Length - 1)];
        }

        // ── WO-948: the derived wall level (the heartDamageMultiplier WRITER at last) ──
        // Before this, GameState.WallLevel had NO gameplay writer (only save-load and reset
        // touched it), so the walls.json ladder was consumable but never climbable. The level
        // is now DERIVED from the player's placed walls in GameState.BaseLayout — the weakest
        // placed wall defines the perimeter (min rule, CoC "your worst wall is the breach"):
        //   walls.json level of one placed wall = repo.wallTierBase + (placed level - 1)
        //   (wall_wood L1 -> 0 wood · wall_wood L2 -> 1 stone · legacy wall_stone L1 -> 1)
        // capped at MaxReachableWallLevel (WO-948; WO-904 lifts). No walls placed = level 0.
        // Derive-at-read keeps ONE model (no second persisted copy to drift, and placement /
        // sell / upgrade / load all self-heal); the 1s cache keeps the enemy strike path cheap.

        private static int   s_derivedLevel;
        private static float s_derivedAt = float.NegativeInfinity;
        private static int   s_lastTraced = -1;

        /// <summary>
        /// WO-948 — PURE min-rule derive over (wallTierBase, placedLevel) pairs, exposed so
        /// the regression can drive it without a live GameState. Empty = 0 (no walls, no
        /// protection); otherwise min over clamped per-wall levels, capped at
        /// <see cref="MaxReachableWallLevel"/>.
        /// </summary>
        public static int DeriveWallLevel(IEnumerable<(int wallTierBase, int placedLevel)> placedWalls)
        {
            if (placedWalls == null) return 0;
            bool any = false;
            int min = int.MaxValue;
            foreach (var w in placedWalls)
            {
                any = true;
                int lvl = Mathf.Clamp(w.wallTierBase + Mathf.Max(1, w.placedLevel) - 1, 0, 3);
                if (lvl < min) min = lvl;
            }
            return any ? Mathf.Min(min, MaxReachableWallLevel) : 0;
        }

        /// <summary>
        /// The player's CURRENT wall level: the WO-948 derive over the placed walls in
        /// GameState.BaseLayout, floored by the persisted GameState.WallLevel (real saves
        /// persist 0 — the floor exists for dev/test states that set the field directly).
        /// 0 when no save is up yet.
        /// </summary>
        public static int CurrentWallLevel()
        {
            var s = DeNelle.Core.State.GameStateService.Instance?.State;
            if (s == null) return 0;
            int persisted = Mathf.Clamp(s.WallLevel, 0, s_heartMult.Length - 1);
            return Mathf.Max(persisted, DerivedWallLevel(s));
        }

        /// <summary>The min-rule derive over the live BaseLayout, cached ~1s (strike-path cheap).</summary>
        private static int DerivedWallLevel(DeNelle.Core.State.GameState s)
        {
            if (s.BaseLayout == null || s.BaseLayout.Count == 0) return 0;
            if (Application.isPlaying && Time.unscaledTime - s_derivedAt < 1f) return s_derivedLevel;

            bool any = false;
            int min = int.MaxValue;
            int walls = 0;
            foreach (var p in s.BaseLayout)
            {
                var e = DeNelle.Core.Catalog.CatalogRegistry.Get(p.itemId);
                if (e == null || e.type != DeNelle.Core.Catalog.CatalogType.Wall) continue;
                any = true;
                walls++;
                int baseLvl = e.repo != null ? e.repo.wallTierBase : 0;
                int lvl = Mathf.Clamp(baseLvl + Mathf.Max(1, p.level) - 1, 0, 3);
                if (lvl < min) min = lvl;
            }
            int result = any ? Mathf.Min(min, MaxReachableWallLevel) : 0;

            if (result != s_lastTraced)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Wall",
                    $"wall-level derived: {result} from {walls} placed wall(s) (min rule -- the weakest " +
                    $"placed wall defines the perimeter; cap {MaxReachableWallLevel} per WO-948, WO-904 lifts). " +
                    $"heartMult now {HeartDamageMultiplier(Mathf.Max(result, Mathf.Clamp(s.WallLevel, 0, 3))):0.00}.");
                s_lastTraced = result;
            }
            s_derivedLevel = result;
            s_derivedAt = Application.isPlaying ? Time.unscaledTime : float.NegativeInfinity;
            return result;
        }

        /// <summary>Heart-damage multiplier for the player's current wall level (Enemy strike-path convenience).</summary>
        public static float CurrentHeartDamageMultiplier() => HeartDamageMultiplier(CurrentWallLevel());

        /// <summary>Spike damage/sec at the player's current wall level (0 unless the spiked top tier is built).</summary>
        public static float CurrentSpikeDamagePerSecond() => SpikeDamagePerSecond(CurrentWallLevel());
    }
}
