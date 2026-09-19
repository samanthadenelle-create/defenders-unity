// =============================================================================
// VigilCeremonyWords — the Circle's five vigil-weight words (WO-1874).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Circle
//
// PURE. No UnityEngine, no HUD, no Village. The five words are the missing name
// column on ballot tiers 1..5. They are NOT HeartState and NOT Heartbound
// resonance — never reuse either ladder, never speak a number first.
//
// HighestUnlockedTier matches api/_lib/clan-ballot.js meetsTier: Tier 1 is
// weight > 0; every other tier is weight >= TIER_THRESHOLDS[tier].
// =============================================================================

namespace DeNelle.Core.Circle
{
    /// <summary>
    /// Ember / Flame / Beacon / Pyre / Dawn, and the one ceremony line per word.
    /// Thresholds are the server's <c>TIER_THRESHOLDS</c> constants.
    /// </summary>
    public static class VigilCeremonyWords
    {
        public const int MinTier = 1;
        public const int MaxTier = 5;

        /// <summary>Index is the ballot tier. Unused slot 0 keeps tier==index.</summary>
        public static readonly long[] TierThresholds =
        {
            0L,
            0L,
            86400L,
            604800L,
            2592000L,
            7776000L
        };

        public const string Ember = "Ember";
        public const string Flame = "Flame";
        public const string Beacon = "Beacon";
        public const string Pyre = "Pyre";
        public const string Dawn = "Dawn";

        public const string LineEmber = "A spark moves beneath the roots. The Circle has begun.";
        public const string LineFlame = "The Circle keeps its watch. The Heart is warm.";
        public const string LineBeacon = "The Heart answers. Light climbs the trunk.";
        public const string LinePyre = "The crown blooms. The ancestors are near.";
        public const string LineDawn = "The canopy shifts. The Circle has been heard.";

        public const string WordKeyEmber = "circle.ceremony.word.ember";
        public const string WordKeyFlame = "circle.ceremony.word.flame";
        public const string WordKeyBeacon = "circle.ceremony.word.beacon";
        public const string WordKeyPyre = "circle.ceremony.word.pyre";
        public const string WordKeyDawn = "circle.ceremony.word.dawn";

        public const string LineKeyEmber = "circle.ceremony.line.ember";
        public const string LineKeyFlame = "circle.ceremony.line.flame";
        public const string LineKeyBeacon = "circle.ceremony.line.beacon";
        public const string LineKeyPyre = "circle.ceremony.line.pyre";
        public const string LineKeyDawn = "circle.ceremony.line.dawn";

        /// <summary>The word for a ballot tier, or empty when the tier is not 1..5.</summary>
        public static string WordForTier(int tier)
        {
            switch (tier)
            {
                case 1: return Ember;
                case 2: return Flame;
                case 3: return Beacon;
                case 4: return Pyre;
                case 5: return Dawn;
                default: return string.Empty;
            }
        }

        /// <summary>The one ceremony line for a ballot tier, or empty when not 1..5.</summary>
        public static string LineForTier(int tier)
        {
            switch (tier)
            {
                case 1: return LineEmber;
                case 2: return LineFlame;
                case 3: return LineBeacon;
                case 4: return LinePyre;
                case 5: return LineDawn;
                default: return string.Empty;
            }
        }

        /// <summary>Locale key for the word, or empty when the tier is not 1..5.</summary>
        public static string WordKeyForTier(int tier)
        {
            switch (tier)
            {
                case 1: return WordKeyEmber;
                case 2: return WordKeyFlame;
                case 3: return WordKeyBeacon;
                case 4: return WordKeyPyre;
                case 5: return WordKeyDawn;
                default: return string.Empty;
            }
        }

        /// <summary>Locale key for the ceremony line, or empty when the tier is not 1..5.</summary>
        public static string LineKeyForTier(int tier)
        {
            switch (tier)
            {
                case 1: return LineKeyEmber;
                case 2: return LineKeyFlame;
                case 3: return LineKeyBeacon;
                case 4: return LineKeyPyre;
                case 5: return LineKeyDawn;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Highest ballot tier whose bar <paramref name="weight"/> clears, or 0 when
        /// none. Fail-closed on NaN / inf.
        /// </summary>
        public static int HighestUnlockedTier(double weight)
        {
            if (double.IsNaN(weight) || double.IsInfinity(weight)) return 0;
            for (int tier = MaxTier; tier >= MinTier; tier--)
            {
                if (MeetsTier(weight, tier)) return tier;
            }
            return 0;
        }

        /// <summary>
        /// Same comparison as server <c>meetsTier</c>: Tier 1 is <c>weight &gt; 0</c>,
        /// every other tier is <c>weight &gt;= bar</c>.
        /// </summary>
        public static bool MeetsTier(double weight, int tier)
        {
            if (tier < MinTier || tier > MaxTier) return false;
            if (double.IsNaN(weight) || double.IsInfinity(weight)) return false;
            long bar = TierThresholds[tier];
            return tier == 1 ? weight > 0.0 : weight >= bar;
        }

        /// <summary>Map a word (any case) back to 1..5, or 0 when unknown.</summary>
        public static int TierForWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return 0;
            if (string.Equals(word, Ember, System.StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(word, Flame, System.StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(word, Beacon, System.StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(word, Pyre, System.StringComparison.OrdinalIgnoreCase)) return 4;
            if (string.Equals(word, Dawn, System.StringComparison.OrdinalIgnoreCase)) return 5;
            return 0;
        }

        /// <summary>Clamp a dressing / sequence tier into 0..5. Invalid input becomes 0.</summary>
        public static int ClampTier(int tier)
        {
            if (tier < 0) return 0;
            if (tier > MaxTier) return MaxTier;
            return tier;
        }
    }
}
