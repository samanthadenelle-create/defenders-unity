// =============================================================================
// StakeRewardsResolver — READ-ONLY "active native SKR stake -> in-game rewards"
// -----------------------------------------------------------------------------
// Seekerthon surface. CANON (memory skr-separate-ingame-currency-real-token-readonly):
// SKR is the REAL Solana/Seeker token. We NEVER mint it, NEVER custody it, NEVER hold
// a withdrawable in-game balance. The player STAKES SKR *natively* via Solana Mobile /
// Seeker native staking (Stake.solanamobile); the game only READS their ACTIVE stake
// (server- / chain-verified, read-only) and grants SMALL cosmetic/flavor in-game perks
// as a thank-you. This class does NO balance mutation and NO transfer — it maps a
// staked amount to a TIER + a list of unlocked rewards, nothing else.
//
// DATA-DRIVEN (owner thinks in data structures, memory owner-thinks-in-data-structures):
// the tier->reward mapping is a TABLE in stake-rewards.json (Resources/Data/Canonical),
// owner-adjustable without code. A thin interpreter here matches the highest tier whose
// minStake <= the player's active stake, and a tier unlocks its own rewards PLUS every
// lower tier's (cumulative). Hardcoded defaults mirror the JSON so the surface never
// boots empty if the file is missing/garbled.
//
// The Stake.solanamobile QUERY is behind the IStakeQuery seam. The default is an
// UnavailableStakeQuery (no live wallet -> no stake). The Seekerthon DEMO injects a
// MockStakeQuery seeded with a real-looking Genesis-holder amount (~1M) — see
// StakeRewardsDemoBootstrap. Production ships the unavailable query until a real
// read-only chain/stake reader is wired, so no crafted state can appear.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Platform
{
    /// <summary>Coarse classification of an in-game stake reward (drives the panel's chip/icon).</summary>
    public enum StakeRewardKind
    {
        /// <summary>A cosmetic profile badge.</summary>
        Badge = 0,
        /// <summary>A cosmetic name title.</summary>
        Title = 1,
        /// <summary>A cosmetic flourish (banner sigil / aura / tint).</summary>
        Cosmetic = 2,
        /// <summary>A small non-premium resource trickle.</summary>
        Trickle = 3,
        /// <summary>Anything else / unclassified.</summary>
        Other = 4,
    }

    /// <summary>One unlocked in-game reward. Immutable value record. Carries which tier granted it
    /// so the panel can group/attribute rewards.</summary>
    public sealed class StakeReward
    {
        public string Label { get; }
        public string Detail { get; }
        public StakeRewardKind Kind { get; }
        public string TierId { get; }
        public string TierName { get; }

        public StakeReward(string label, string detail, StakeRewardKind kind, string tierId, string tierName)
        {
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            Kind = kind;
            TierId = tierId ?? string.Empty;
            TierName = tierName ?? string.Empty;
        }
    }

    /// <summary>One stake tier from the table: a name, the minimum active stake to reach it, a
    /// flavour tagline, and the rewards it grants (on top of every lower tier's). Immutable.</summary>
    public sealed class StakeTier
    {
        public string Id { get; }
        public string Name { get; }
        public long MinStake { get; }
        public string Tagline { get; }
        public IReadOnlyList<StakeReward> Rewards { get; }

        public StakeTier(string id, string name, long minStake, string tagline, IReadOnlyList<StakeReward> rewards)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            MinStake = minStake < 0 ? 0 : minStake;
            Tagline = tagline ?? string.Empty;
            Rewards = rewards ?? Array.Empty<StakeReward>();
        }
    }

    /// <summary>The resolved standing for a given active stake: the current tier, the next tier up
    /// (for a "stake N more to reach X" nudge), and the CUMULATIVE unlocked rewards. Read-only view.</summary>
    public sealed class StakeStanding
    {
        /// <summary>
        /// The fallback display symbol, resolved at COMPILE time per channel (WO-1363). It used to
        /// be an inline literal in three places, so "SKR" reached the Google Play artifact's
        /// global-metadata.dat even though this whole surface is unreachable there. The Seeker /
        /// dApp build is unchanged.
        /// </summary>
#if GOOGLE_PLAY
        public const string DefaultCurrencySymbol = "pts";
#else
        public const string DefaultCurrencySymbol = "SKR";
#endif

        /// <summary>The player's active native SKR stake this standing was resolved from.</summary>
        public long ActiveStake { get; }
        /// <summary>Display currency symbol (from the table, e.g. "SKR").</summary>
        public string CurrencySymbol { get; }
        /// <summary>The highest tier reached, or null when the stake is below the first tier (no stake).</summary>
        public StakeTier CurrentTier { get; }
        /// <summary>The next tier up, or null when already at the top tier.</summary>
        public StakeTier NextTier { get; }
        /// <summary>Every reward unlocked at the current stake (current tier + all lower tiers).</summary>
        public IReadOnlyList<StakeReward> UnlockedRewards { get; }
        /// <summary>The full tier ladder, ascending by minStake (for the panel to render the ladder).</summary>
        public IReadOnlyList<StakeTier> AllTiers { get; }

        /// <summary>True when the player has a stake that reaches at least the first tier.</summary>
        public bool HasStake => ActiveStake > 0 && CurrentTier != null;

        public StakeStanding(long activeStake, string currencySymbol, StakeTier currentTier,
            StakeTier nextTier, IReadOnlyList<StakeReward> unlocked, IReadOnlyList<StakeTier> allTiers)
        {
            ActiveStake = activeStake < 0 ? 0 : activeStake;
            CurrencySymbol = string.IsNullOrEmpty(currencySymbol) ? DefaultCurrencySymbol : currencySymbol;
            CurrentTier = currentTier;
            NextTier = nextTier;
            UnlockedRewards = unlocked ?? Array.Empty<StakeReward>();
            AllTiers = allTiers ?? Array.Empty<StakeTier>();
        }
    }

    /// <summary>
    /// The read-only seam onto Solana Mobile / Seeker NATIVE staking (Stake.solanamobile).
    /// An implementation answers "how much SKR does this player have ACTIVELY staked?" — a pure
    /// READ (chain/RPC or a server that verifies the stake account). It NEVER moves funds. Wire a
    /// real reader here when the on-chain path lands; until then production uses
    /// <see cref="StakeRewardsResolver.UnavailableStakeQuery"/> and the demo uses
    /// <see cref="StakeRewardsResolver.MockStakeQuery"/>.
    /// </summary>
    public interface IStakeQuery
    {
        /// <summary>Try to read the active staked SKR. Returns false when no stake is known
        /// (no wallet connected / reader unavailable) — the caller then shows the un-staked state,
        /// never a fabricated amount.</summary>
        bool TryGetActiveStake(out long stakedSkr);
    }

    /// <summary>
    /// Resolves a player's ACTIVE native SKR stake into a tier + unlocked in-game rewards, from the
    /// owner-adjustable stake-rewards.json table. Read-only / non-custodial by construction.
    /// </summary>
    public static class StakeRewardsResolver
    {
        /// <summary>Resources/StreamingAssets-relative path to the owner-adjustable reward table.</summary>
        private const string TablePath = "Data/Canonical/stake-rewards.json";

        /// <summary>The seeded stake used by the Seekerthon demo (owner is a Genesis holder, ~1M SKR).
        /// Lives here as the single tunable so the mock reads real-looking in the ~90s capture.</summary>
        public const long DemoMockStakeSkr = 1_000_000L;

        private static List<StakeTier> _tiers;
        private static string _currencySymbol = StakeStanding.DefaultCurrencySymbol;

        /// <summary>
        /// The active stake reader. DEFAULTS to <see cref="UnavailableStakeQuery"/> (no live wallet =
        /// no stake) so production never shows a fabricated amount. The demo bootstrap swaps in a
        /// <see cref="MockStakeQuery"/>. Assigning a real read-only chain reader here lights up the
        /// live path with zero call-site changes.
        /// </summary>
        private static IStakeQuery _query = new UnavailableStakeQuery();

        /// <summary>Raised when the wallet-backed read completes or its cached amount changes.</summary>
        public static event Action StakeChanged;

        public static IStakeQuery Query
        {
            get => _query;
            set
            {
                _query = value ?? new UnavailableStakeQuery();
                StakeChanged?.Invoke();
            }
        }

        /// <summary>Lets an asynchronous query announce that its cached on-chain value changed.</summary>
        public static void NotifyStakeChanged() => StakeChanged?.Invoke();

        // =====================================================================
        //  Public resolve API
        // =====================================================================

        /// <summary>Resolve the standing for the CURRENT <see cref="Query"/> (reads it once). When the
        /// query has no stake, resolves the un-staked standing (stake 0 -> no current tier).</summary>
        public static StakeStanding Resolve()
        {
            long staked = 0;
            var q = Query;
            if (q != null && q.TryGetActiveStake(out long s) && s > 0) staked = s;
            else FlowTrace.Step("Stake", "No active stake from query (unavailable / no wallet) — resolving un-staked standing.");
            return Resolve(staked);
        }

        /// <summary>Resolve the standing for an explicit active stake amount (SKR). The core mapping —
        /// used by <see cref="Resolve()"/> and directly by tests / the demo.</summary>
        public static StakeStanding Resolve(long activeStakeSkr)
        {
            if (activeStakeSkr < 0) activeStakeSkr = 0;
            EnsureTable();

            StakeTier current = null;
            StakeTier next = null;
            var unlocked = new List<StakeReward>();

            // Tiers are held ascending by MinStake. Current = highest whose gate is met; every met
            // tier's rewards are cumulative; next = first un-met tier (the nudge target).
            for (int i = 0; i < _tiers.Count; i++)
            {
                var t = _tiers[i];
                if (activeStakeSkr >= t.MinStake && t.MinStake >= 0)
                {
                    current = t;
                    for (int r = 0; r < t.Rewards.Count; r++) unlocked.Add(t.Rewards[r]);
                }
                else if (next == null)
                {
                    next = t;
                }
            }

            FlowTrace.Step("Stake",
                $"Resolved stake={activeStakeSkr} {_currencySymbol} -> tier='{current?.Name ?? "(none)"}', " +
                $"{unlocked.Count} reward(s) unlocked, next='{next?.Name ?? "(max)"}'.");

            return new StakeStanding(activeStakeSkr, _currencySymbol, current, next, unlocked, _tiers);
        }

        /// <summary>Force a re-read of the table (tests / a hot edit of stake-rewards.json).</summary>
        public static void Reload() { _tiers = null; EnsureTable(); }

        // =====================================================================
        //  Table load (owner-adjustable JSON, generated byte-exact fallback)
        // =====================================================================

        private static void EnsureTable()
        {
            if (_tiers != null) return;
            _tiers = LoadFromJson() ?? LoadGeneratedFallback();
            // Guarantee ascending order so the resolve loop's current/next logic holds regardless of
            // author ordering in the JSON.
            _tiers.Sort((a, b) => a.MinStake.CompareTo(b.MinStake));
        }

        private static List<StakeTier> LoadFromJson()
        {
            string json;
            try { json = CanonicalJson.Read(TablePath); }
            catch (Exception ex)
            {
                FlowTrace.Warn("Stake", $"stake-rewards.json read threw ({ex.Message}) — using generated fallback.");
                return null;
            }
            if (string.IsNullOrEmpty(json))
            {
                FlowTrace.Warn("Stake", "stake-rewards.json not found — using generated fallback.");
                return null;
            }

            try
            {
                var root = JObject.Parse(json);
                string sym = root["currencySymbol"]?.ToString();
                if (!string.IsNullOrEmpty(sym)) _currencySymbol = sym;

                var tiersJson = root["tiers"] as JArray;
                if (tiersJson == null || tiersJson.Count == 0)
                {
                    FlowTrace.Warn("Stake", "stake-rewards.json has no 'tiers' array — using generated fallback.");
                    return null;
                }

                var list = new List<StakeTier>();
                foreach (var tj in tiersJson)
                {
                    string id = tj["id"]?.ToString() ?? string.Empty;
                    string name = tj["name"]?.ToString() ?? id;
                    long min = tj["minStake"]?.Type == JTokenType.Integer ? tj["minStake"].Value<long>() : 0L;
                    string tagline = tj["tagline"]?.ToString() ?? string.Empty;

                    var rewards = new List<StakeReward>();
                    if (tj["rewards"] is JArray rj)
                    {
                        foreach (var r in rj)
                        {
                            rewards.Add(new StakeReward(
                                r["label"]?.ToString() ?? string.Empty,
                                r["detail"]?.ToString() ?? string.Empty,
                                ParseKind(r["kind"]?.ToString()),
                                id, name));
                        }
                    }
                    list.Add(new StakeTier(id, name, min, tagline, rewards));
                }
                FlowTrace.Step("Stake", $"Loaded {list.Count} stake tier(s) from stake-rewards.json (currency={_currencySymbol}).");
                return list;
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Stake", $"stake-rewards.json parse failed ({ex.Message}) — using generated fallback.");
                return null;
            }
        }

        private static StakeRewardKind ParseKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return StakeRewardKind.Other;
            switch (s.Trim().ToLowerInvariant())
            {
                case "badge":    return StakeRewardKind.Badge;
                case "title":    return StakeRewardKind.Title;
                case "cosmetic": return StakeRewardKind.Cosmetic;
                case "trickle":  return StakeRewardKind.Trickle;
                default:         return StakeRewardKind.Other;
            }
        }

        /// <summary>Parse the generated byte-exact copy through the same DTO path as canonical JSON.</summary>
        private static List<StakeTier> LoadGeneratedFallback()
        {
            try
            {
                var root = JObject.Parse(StakeRewardsFallbackData.Json);
                string sym = root["currencySymbol"]?.ToString();
                if (!string.IsNullOrEmpty(sym)) _currencySymbol = sym;
                var tiersJson = root["tiers"] as JArray;
                if (tiersJson == null || tiersJson.Count == 0)
                    throw new InvalidOperationException("generated table has no tier rows");

                var list = new List<StakeTier>();
                foreach (var tj in tiersJson)
                {
                    string id = tj["id"]?.ToString() ?? string.Empty;
                    string name = tj["name"]?.ToString() ?? id;
                    long min = tj["minStake"]?.Type == JTokenType.Integer
                        ? tj["minStake"].Value<long>() : 0L;
                    string tagline = tj["tagline"]?.ToString() ?? string.Empty;
                    var rewards = new List<StakeReward>();
                    if (tj["rewards"] is JArray rows)
                    {
                        foreach (var reward in rows)
                        {
                            rewards.Add(new StakeReward(
                                reward["label"]?.ToString() ?? string.Empty,
                                reward["detail"]?.ToString() ?? string.Empty,
                                ParseKind(reward["kind"]?.ToString()), id, name));
                        }
                    }
                    list.Add(new StakeTier(id, name, min, tagline, rewards));
                }
                FlowTrace.Warn("Stake", "Using generated stake reward fallback " +
                    $"(tiers={list.Count}, sha256={StakeRewardsFallbackData.SourceSha256}).");
                return list;
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("Stake", "Generated stake reward fallback failed to parse; " +
                    $"refusing to invent reward values ({ex.Message}).");
                return new List<StakeTier>();
            }
        }

        // =====================================================================
        //  Built-in query implementations
        // =====================================================================

        /// <summary>The production default: no live stake reader wired, so NO stake is ever reported
        /// (the surface shows the un-staked state). Guarantees production can never fabricate an amount.</summary>
        public sealed class UnavailableStakeQuery : IStakeQuery
        {
            public bool TryGetActiveStake(out long stakedSkr) { stakedSkr = 0; return false; }
        }

        /// <summary>The Seekerthon DEMO reader: reports a fixed, real-looking staked amount WITHOUT any
        /// wallet connection, so the panel shows a live-looking stake + unlocked rewards in the capture.
        /// Injected only behind the ff.stakedemo flag by <c>StakeRewardsDemoBootstrap</c>.</summary>
        public sealed class MockStakeQuery : IStakeQuery
        {
            private readonly long _amount;
            public MockStakeQuery(long amount) { _amount = amount < 0 ? 0 : amount; }
            public bool TryGetActiveStake(out long stakedSkr) { stakedSkr = _amount; return _amount > 0; }
        }
    }
}
