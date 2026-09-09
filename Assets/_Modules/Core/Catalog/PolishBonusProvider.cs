// =============================================================================
// IPolishBonusProvider / PolishBonuses — the "extra ATTEMPTS" seam (WO-1042; owner
// ruling 2026-08-16).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Catalog
//
// ⛔⛔ THE ONE RULE THIS SEAM EXISTS TO ENFORCE:
//
//        **STAKING BUYS ATTEMPTS, NEVER OUTCOMES.**
//        A staker's roll is EXACTLY as likely as a free player's.
//
// The owner's first proposal was +5% odds for native SKR stakers. It was flagged as
// breaking the fairness property she had just set — that free, ad-funded, paying and
// staking players all roll the IDENTICAL per-roll table — and she agreed immediately.
// The final ruling grants ATTEMPTS instead:
//
//   • native SKR staker        -> +1 free re-roll per week
//   • staker with 10k+ SKR     -> +1 free re-roll per week AND +1 to the roll cap (6, not 5)
//
// This interface therefore exposes ONLY attempt-shaped grants. There is deliberately no
// member for odds, weights, luck, tier bias or a bonus table, and adding one would break
// the property the whole economy rests on. DungeonGemExclusivityRegression fails if the
// roll or the disclosed odds ever consult a provider.
//
// ⚠ NO CHAIN QUERY IN THIS LANE. The default provider returns ZERO for everyone, so the
// loop ships and is fully testable with no wallet, no RPC and no Solana dependency. The
// real staking provider plugs in later behind the existing wallet work by calling
// PolishBonuses.Install.
//
// ⚠ PLATFORM-FLAGGED. Apple and Google both restrict gating gameplay functionality on
// token holdings and have been actively enforcing. The hook is gated on
// FeatureFlags.StakingPolishBonus, which stays OFF so the Play build ships the seam
// returning zero; a Seeker/dApp-store build can turn it on. The flag is read HERE, once,
// so no call site ever hardcodes a platform check.
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Core.Catalog
{
    /// <summary>
    /// Supplies EXTRA ATTEMPTS to the polish economy. ⚠ Attempts only — never odds. See the file
    /// header; an odds-shaped member here would break the fairness invariant by design.
    /// </summary>
    public interface IPolishBonusProvider
    {
        /// <summary>Extra free re-rolls granted this week (0 for everyone by default).</summary>
        int ExtraWeeklyRerolls { get; }

        /// <summary>Amount to ADD to the per-stone roll cap (0 for everyone by default).</summary>
        int RollCapDelta { get; }
    }

    /// <summary>The zero provider: what every player gets until a staking provider is installed.</summary>
    public sealed class NoPolishBonus : IPolishBonusProvider
    {
        public int ExtraWeeklyRerolls => 0;
        public int RollCapDelta => 0;
    }

    /// <summary>
    /// Reads the active native SKR stake through the single read-only staking seam. A positive,
    /// verified active stake grants one weekly re-roll; 10,000+ SKR also raises the per-stone cap.
    /// The provider never owns a wallet, RPC client, probability, or outcome table.
    /// </summary>
    public sealed class NativeSkrPolishBonus : IPolishBonusProvider
    {
        public const long ExpandedRollCapStake = 10_000L;

        private static StakeStanding Standing => StakeRewardsResolver.Resolve();

        public int ExtraWeeklyRerolls => Standing.HasStake ? 1 : 0;
        public int RollCapDelta => Standing.HasStake && Standing.ActiveStake >= ExpandedRollCapStake ? 1 : 0;
    }

    /// <summary>
    /// The installed provider, plus the flag gate. Query through here, never through a provider
    /// reference held at a call site — that is what keeps the platform flag in exactly one place.
    /// </summary>
    public static class PolishBonuses
    {
        private const string Sys = "JewelPolish";
        private const string WeeklyPeriodKey = "jewelpolish.stake.week";
        private const string WeeklyUsedKey = "jewelpolish.stake.used";
        private const double WeekMs = 7d * 24d * 60d * 60d * 1000d;
        private static readonly IPolishBonusProvider Zero = new NoPolishBonus();
        private static IPolishBonusProvider _installed;

        /// <summary>
        /// Install the real provider (the staking lane does this behind the wallet work). Idempotent;
        /// pass null to uninstall. Never changes any probability — see the header.
        /// </summary>
        public static void Install(IPolishBonusProvider provider)
        {
            _installed = provider;
            FlowTrace.Step(Sys, provider == null
                ? "polish bonus provider UNINSTALLED - everyone is back to the zero baseline."
                : $"polish bonus provider installed ({provider.GetType().Name}) - ATTEMPTS only, odds untouched.");
        }

        /// <summary>
        /// The effective provider. Returns the zero provider whenever the platform flag is OFF, so a
        /// Play-store build behaves exactly as if no staking existed even if one were installed.
        /// </summary>
        private static IPolishBonusProvider Active
        {
            get
            {
                if (!FeatureFlags.StakingPolishBonus) return Zero;
                return _installed ?? Zero;
            }
        }

        /// <summary>Extra free re-rolls this week. 0 unless a provider is installed AND the flag is on.</summary>
        public static int ExtraWeeklyRerolls => Active.ExtraWeeklyRerolls;

        /// <summary>Roll-cap bonus. 0 unless a provider is installed AND the flag is on.</summary>
        public static int RollCapDelta => Active.RollCapDelta;

        /// <summary>Unspent native-staker re-rolls in the current fixed seven-day period.</summary>
        public static int WeeklyRerollsRemaining
        {
            get
            {
                int allowance = Math.Max(0, ExtraWeeklyRerolls);
                if (allowance == 0) return 0;
                RefreshWeeklyPeriod();
                return Math.Max(0, allowance - Math.Max(0, PlayerPrefs.GetInt(WeeklyUsedKey, 0)));
            }
        }

        /// <summary>
        /// Consumes one weekly attempt only after the normal per-stone allowance is exhausted.
        /// The period uses the server-anchored clock when available and is reconciled by the same
        /// backend clock used by other timed allowances; offline falls back to device UTC.
        /// </summary>
        public static bool TryConsumeWeeklyReroll()
        {
            if (WeeklyRerollsRemaining <= 0) return false;
            int used = Math.Max(0, PlayerPrefs.GetInt(WeeklyUsedKey, 0)) + 1;
            PlayerPrefs.SetInt(WeeklyUsedKey, used);
            PlayerPrefs.Save();
            FlowTrace.Step(Sys, $"native SKR weekly re-roll CONSUMED: {used}/{Math.Max(0, ExtraWeeklyRerolls)} used.");
            return true;
        }

        private static void RefreshWeeklyPeriod()
        {
            double nowMs;
            if (!ServerClock.TryNowUnixMs(out nowMs))
                nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int period = (int)Math.Floor(Math.Max(0d, nowMs) / WeekMs);
            if (PlayerPrefs.GetInt(WeeklyPeriodKey, -1) == period) return;
            PlayerPrefs.SetInt(WeeklyPeriodKey, period);
            PlayerPrefs.SetInt(WeeklyUsedKey, 0);
            PlayerPrefs.Save();
            FlowTrace.Step(Sys, $"native SKR weekly re-roll period advanced to {period}; usage reset.");
        }
    }

    /// <summary>Installs the real attempt-only adapter. The distribution flag remains authoritative.</summary>
    public static class NativeSkrPolishBonusBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => PolishBonuses.Install(new NativeSkrPolishBonus());
    }
}
