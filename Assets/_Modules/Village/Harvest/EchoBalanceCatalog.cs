// =============================================================================
// EchoBalanceCatalog -- typed loader for echoes-balance.json (WO-738).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The TUNABLE half of the Echo specialization model. Identity (name / element /
// preferred-lane / harvest-resource) is the FIXED code table in EchoRosterCatalog;
// the NUMBERS the owner re-tunes in playtest (level cap, match bonus, base
// contribution, set bonus, per-echo base rate) live in data so a tune needs NO
// recompile. Mirrors BuildingTierCatalog's strategy: reads
// Data/Canonical/echoes-balance.json via DeNelle.Core.CanonicalJson (the Resources
// dual-copy wins -- WebGL-safe -- then StreamingAssets) and caches a typed def.
//
// Guard-wrapped with SENSIBLE FALLBACKS: a missing/invalid file logs a [Flow:Echo]
// Warn and returns the built-in defaults so NOTHING hard-fails (the EchoAssignments
// level clamp still has a valid MaxLevel, the bonus math still has valid knobs).
// =============================================================================

using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>WO-830: one pair-synergy definition. The pair RUNS when both member echoes are
    /// owned AND harvest-assigned to their own affinity resources (EchoBonusCalculator.PairActive);
    /// a running pair adds <see cref="Bonus"/> (additive fraction) to the global harvest spec sum
    /// -- DISCLOSED in the card UI. (The legacy "mult" field is kept parse-compatible but unused.)</summary>
    [System.Serializable]
    public sealed class EchoCrossBonusDef
    {
        /// <summary>Display name of the synergy ("Provisions"/"Forge"/"Fortune"). ASCII.</summary>
        [JsonProperty("name")] public string Name = "";
        [JsonProperty("a")] public string A;
        [JsonProperty("b")] public string B;
        /// <summary>Additive spec-sum fraction granted while the pair runs (e.g. 0.10 = +10%).</summary>
        [JsonProperty("bonus")] public float Bonus = 0f;
        /// <summary>Legacy pre-830 field (was never populated); kept so old data parses. Unused.</summary>
        [JsonProperty("mult")] public float Mult = 1f;
    }

    /// <summary>WO-1474: the three WORKFORCE per-hour harvest rates that
    /// <see cref="EchoBonusCalculator.HarvestRatePerHour"/> returns, one per rate CLASS.
    /// They were C# literals (3600 / 900 / 4) until 2026-09-06, which put them out of reach
    /// of the WO-1331 remote-retune seam even though this file is on its allowlist. The
    /// defaults below are the EXACT literals they replaced -- keep them in step with the
    /// authored json so an absent file cannot silently change the split.</summary>
    [System.Serializable]
    public sealed class EchoHarvestRateDef
    {
        /// <summary>Wood / Iron / Food, per hour at level 1 (5 every 5 seconds).</summary>
        [JsonProperty("common")] public float Common = 3600f;
        /// <summary>Gold per hour at level 1 -- valuable, but not premium.</summary>
        [JsonProperty("gold")] public float Gold = 900f;
        /// <summary>Crystals per hour -- the deliberately tiny drip, exactly 1 per 15 minutes.</summary>
        [JsonProperty("crystals")] public float Crystals = 4f;
    }

    /// <summary>The parsed echoes-balance.json root. Field defaults ARE the built-in fallback.</summary>
    [System.Serializable]
    public sealed class EchoBalanceData
    {
        [JsonProperty("version")] public int Version = 1;
        /// <summary>Max echo level (1..maxLevel). Bounds the EchoAssignments level clamp.</summary>
        [JsonProperty("maxLevel")] public int MaxLevel = 8;
        /// <summary>Bonus weight added to the assigned lane when element matches the preferred lane.</summary>
        [JsonProperty("preferredLaneMatchBonus")] public float PreferredLaneMatchBonus = 0.75f;
        /// <summary>Floor weight every echo adds to ALL lanes (so no assignment is ever wasted).</summary>
        [JsonProperty("baseContributionPerEcho")] public float BaseContributionPerEcho = 0.15f;
        /// <summary>Flat global Harvest bonus when all 6 spirits are owned (the set bonus).</summary>
        [JsonProperty("sixSetBonusGlobalHarvest")] public float SixSetBonusGlobalHarvest = 0.20f;
        // RETIRED 2026-09-10 by owner ruling on WO-1430 (fields 3-5): the levelCurve member and
        // the echoes-balance.json key it bound were DROPPED. It authored the name of a curve
        // EchoBonusCalculator never asked for, so authoring a second name would have changed
        // nothing and said nothing -- an unkept promise, not a knob. The per-level term is
        // linear by construction in PerLevelBonus below; if a second curve is ever wanted,
        // re-add the field IN THE SAME CHANGE as the formula that honours it. Its absence is
        // pinned by AuthoredFieldReaderRegression case [retired-field-stays-retired], which
        // REDS if either the declaration or the json key comes back.
        /// <summary>Per-level bonus increment (the per-level term is linear by construction).</summary>
        [JsonProperty("perLevelBonus")] public float PerLevelBonus = 0.05f;
        /// <summary>Per-echo base contribution rate, keyed by echo id ("echo-frosthowl").</summary>
        [JsonProperty("perEchoBaseRate")] public Dictionary<string, float> PerEchoBaseRate = new Dictionary<string, float>();
        /// <summary>WO-830 pair-synergy definitions (the 3 disclosed pairs).</summary>
        [JsonProperty("crossBonuses")] public List<EchoCrossBonusDef> CrossBonuses = new List<EchoCrossBonusDef>();
        /// <summary>WO-830 Sec.3d: the HIDDEN tri-synergy bonus (additive fraction) applied when
        /// ALL pair-synergies run at once. NEVER disclosed player-facing -- applied path only.</summary>
        [JsonProperty("hiddenTriSynergyBonus")] public float HiddenTriSynergyBonus = 0.25f;

        /// <summary>WO-811 / WO-1108: structure-FRACTIONS of repair work ONE OWNED Echo advances
        /// per hour at level 1, BEFORE the shared contribution terms (EchoBonusCalculator.
        /// RepairFractionsPerSecond folds BaseContributionPerEcho + PerLevelBonus on top).
        ///
        /// WO-1108 D3: repair went PASSIVE (summed over the WHOLE roster instead of the echoes
        /// assigned to a repair task), which multiplies the aggregate by roster size. The knob
        /// was therefore MOVED INTO echoes-balance.json (it was a code-only default before) and
        /// re-tuned 2.0 -> 0.35 so a FULL 6-Echo roster lands at ~2.14 fractions/h -- within
        /// ~5% of the old ONE-assigned-Echo felt rate of 2.04 -- instead of 6x-ing it.
        /// This default now MIRRORS the authored json value; keep them in step so an absent
        /// file cannot silently reintroduce the 6x. ADDITIVE knob: absent in an older
        /// echoes-balance.json, Newtonsoft leaves this default -- no version bump.</summary>
        [JsonProperty("repairFractionPerHour")] public float RepairFractionPerHour = 0.35f;

        /// <summary>WO-1474: the three workforce per-hour harvest rates (see
        /// <see cref="EchoHarvestRateDef"/>). ADDITIVE knob -- absent in an older
        /// echoes-balance.json, Newtonsoft leaves this default, which equals the literals
        /// it replaced, so no version bump and no split change.</summary>
        [JsonProperty("harvestRatePerHour")] public EchoHarvestRateDef HarvestRatePerHour = new EchoHarvestRateDef();
    }

    /// <summary>Static surface over echoes-balance.json -- load + cache + typed getters (WO-738).</summary>
    public static class EchoBalanceCatalog
    {
        private const string StreamingRelativePath = "Data/Canonical/echoes-balance.json";
        private const int ExpectedVersion = 1;
        private static EchoBalanceData _data;

        /// <summary>The full parsed balance data (never null -- defaults if the file is absent).</summary>
        public static EchoBalanceData Data { get { EnsureLoaded(); return _data; } }

        /// <summary>Max echo level (>= 1). Bounds the EchoAssignments level clamp.</summary>
        public static int MaxLevel { get { EnsureLoaded(); return Mathf.Max(1, _data.MaxLevel); } }

        /// <summary>Bonus weight for an element+lane match.</summary>
        public static float PreferredLaneMatchBonus { get { EnsureLoaded(); return _data.PreferredLaneMatchBonus; } }

        /// <summary>Floor weight every echo adds to all lanes.</summary>
        public static float BaseContributionPerEcho { get { EnsureLoaded(); return _data.BaseContributionPerEcho; } }

        /// <summary>The 6-of-6 set bonus (flat global Harvest mult addend).</summary>
        public static float SixSetBonusGlobalHarvest { get { EnsureLoaded(); return _data.SixSetBonusGlobalHarvest; } }

        /// <summary>Per-level bonus increment (linear curve).</summary>
        public static float PerLevelBonus { get { EnsureLoaded(); return _data.PerLevelBonus; } }

        /// <summary>WO-830: the pair-synergy definitions (never null; may be empty).</summary>
        public static List<EchoCrossBonusDef> CrossBonuses
        {
            get { EnsureLoaded(); return _data.CrossBonuses ?? (_data.CrossBonuses = new List<EchoCrossBonusDef>()); }
        }

        /// <summary>WO-830 Sec.3d: the hidden tri-synergy bonus (applied-only, never disclosed).</summary>
        public static float HiddenTriSynergyBonus { get { EnsureLoaded(); return Mathf.Max(0f, _data.HiddenTriSynergyBonus); } }

        /// <summary>WO-811: base repair work (structure fractions/hour) per repair-assigned Echo
        /// at level 1 (EchoBonusCalculator.RepairFractionsPerSecond is the ONE consumer).</summary>
        public static float RepairFractionPerHour { get { EnsureLoaded(); return Mathf.Max(0f, _data.RepairFractionPerHour); } }

        /// <summary>WO-1474: Wood/Iron/Food workforce rate per hour at level 1 (was the
        /// C# literal 3600). Clamped >= 0 so a bad authored row cannot go negative.</summary>
        public static float CommonResourcePerHour
        {
            get { EnsureLoaded(); return Mathf.Max(0f, RatesOrDefault().Common); }
        }

        /// <summary>WO-1474: Gold workforce rate per hour at level 1 (was the literal 900).</summary>
        public static float GoldPerHour
        {
            get { EnsureLoaded(); return Mathf.Max(0f, RatesOrDefault().Gold); }
        }

        /// <summary>WO-1474: Crystals per hour -- level-flat by design (was the literal 4).</summary>
        public static float CrystalPerHour
        {
            get { EnsureLoaded(); return Mathf.Max(0f, RatesOrDefault().Crystals); }
        }

        private static EchoHarvestRateDef RatesOrDefault()
        {
            return _data.HarvestRatePerHour ?? (_data.HarvestRatePerHour = new EchoHarvestRateDef());
        }

        /// <summary>The per-echo base contribution rate for an echo id (1.0 fallback when absent).
        /// WO-1474: this is the DumpSilos split WEIGHT MULTIPLIER -- EchoBonusCalculator.
        /// HarvestTargetWeights is the production consumer (it had none before 2026-09-06).</summary>
        public static float BaseRateFor(string echoId)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(echoId) && _data.PerEchoBaseRate != null
                && _data.PerEchoBaseRate.TryGetValue(echoId, out var rate))
                return rate;
            return 1f;
        }

        /// <summary>Force a re-read (test / hot-reload).</summary>
        public static void Reload() { _data = null; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            _data = LoadData();
        }

        private static EchoBalanceData LoadData()
        {
            var parsed = Guard.Try("Echo", "load echoes-balance.json", () =>
            {
                string json = DeNelle.Core.CanonicalJson.Read(StreamingRelativePath);
                if (string.IsNullOrEmpty(json))
                {
                    FlowTrace.Warn("Echo", "echoes-balance.json not found (Resources or StreamingAssets) -- using built-in default balance.");
                    return (EchoBalanceData)null;
                }
                var d = JsonConvert.DeserializeObject<EchoBalanceData>(json);
                if (d == null)
                {
                    FlowTrace.Warn("Echo", "echoes-balance.json parsed null -- using built-in default balance.");
                    return (EchoBalanceData)null;
                }
                if (d.Version != ExpectedVersion)
                    FlowTrace.Warn("Echo", $"echoes-balance.json version {d.Version} != expected {ExpectedVersion} -- loading anyway (additive).");
                int rates = d.PerEchoBaseRate != null ? d.PerEchoBaseRate.Count : 0;
                FlowTrace.Step("Echo", $"EchoBalanceCatalog loaded (version {d.Version}, maxLevel {d.MaxLevel}, {rates} per-echo rates).");
                return d;
            }, fallback: null);

            if (parsed != null) return parsed;
            FlowTrace.Warn("Echo", "EchoBalanceCatalog falling back to built-in default balance (file missing/invalid).");
            return new EchoBalanceData();
        }
    }
}
