// =============================================================================
// StakeRewardsVM -- the Seekerthon stake-rewards ViewModel (MVVM, Silo F).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI.Mvvm
//
// Projects a read-only StakeRewardsResolver standing (active native SKR stake -> tier
// -> unlocked rewards) into pure display data for StakeRewardsPanel. It moves the
// StakeRewardsResolver.Resolve() call + every StakeStanding read OUT of the View body;
// the panel becomes a dumb skin that binds these strings + the reward-row list.
//
// Read-only + non-custodial by construction (mirrors the resolver): the VM mutates NO
// game state and holds NO SKR. A static snapshot -- the resolver is read once at
// construction, so <see cref="Changed"/> is effectively inert (the panel is read-once).
// =============================================================================
using System;
using System.Collections.Generic;
using DeNelle.Core.Platform;

namespace DeNelle.Core.UI.Mvvm
{
    /// <summary>One unlocked-reward row, projected for the View (kind tag + colour key carried
    /// as the enum; the View maps the enum to a chip colour).</summary>
    public readonly struct StakeRewardRowVM
    {
        public readonly string Label;
        public readonly string Detail;
        public readonly StakeRewardKind Kind;
        /// <summary>Short uppercase kind tag ("BADGE" / "TITLE" / "COSMETIC" / "TRICKLE" / "PERK").</summary>
        public readonly string KindTag;

        public StakeRewardRowVM(string label, string detail, StakeRewardKind kind, string kindTag)
        {
            Label = label;
            Detail = detail;
            Kind = kind;
            KindTag = kindTag;
        }
    }

    /// <summary>ViewModel for the read-only stake-rewards panel. Projects a <see cref="StakeStanding"/>.</summary>
    public sealed class StakeRewardsVM : IPanelViewModel, IDisposable
    {
        private readonly Action _onClose;

        public StakeRewardsVM(StakeStanding standing, Action onClose = null)
        {
            _onClose = onClose;
            Project(standing);
        }

        /// <summary>The ONLY resolution site: read the LIVE resolver standing.</summary>
        public static StakeRewardsVM CreateDefault(Action onClose = null)
        {
            return new StakeRewardsVM(StakeRewardsResolver.Resolve(), onClose);
        }

        /// <summary>Build for the explicit un-staked standing (the Open(null) fallback), resolver out of the View.</summary>
        public static StakeRewardsVM CreateUnstaked(Action onClose = null)
        {
            return new StakeRewardsVM(StakeRewardsResolver.Resolve(0), onClose);
        }

        // -- IPanelViewModel ----------------------------------------------------
        public event Action Changed;
        public string Title => new DeNelle.Core.UI.LocalizedText("common.stake_rewards").Resolve();
        public void Close() => _onClose?.Invoke();
        public void Dispose() { Changed = null; }

        // -- Projected read-only data -------------------------------------------

        public bool HasStake { get; private set; }
        public string CurrencySymbol { get; private set; } = StakeStanding.DefaultCurrencySymbol;
        /// <summary>"Active Stake:  N SKR" (N is 0 when un-staked).</summary>
        public string ActiveStakeText { get; private set; }
        public bool HasTier { get; private set; }
        public string TierName { get; private set; }
        public string TierTagline { get; private set; }
        /// <summary>The cumulative unlocked-reward rows (never null; empty when none unlocked).</summary>
        public IReadOnlyList<StakeRewardRowVM> Rewards { get; private set; } = Array.Empty<StakeRewardRowVM>();
        public bool IsEmpty => Rewards == null || Rewards.Count == 0;

        private void Project(StakeStanding standing)
        {
            string sym = standing != null ? standing.CurrencySymbol : StakeStanding.DefaultCurrencySymbol;
            CurrencySymbol = sym;
            long stake = standing != null ? standing.ActiveStake : 0;
            HasStake = standing != null && standing.HasStake;
            ActiveStakeText = "Active Stake:  " + stake.ToString("N0") + " " + sym;

            HasTier = HasStake && standing.CurrentTier != null;
            TierName = HasTier ? standing.CurrentTier.Name : null;
            TierTagline = HasTier ? standing.CurrentTier.Tagline : null;

            var rewards = standing != null ? standing.UnlockedRewards : null;
            if (rewards == null || rewards.Count == 0)
            {
                Rewards = Array.Empty<StakeRewardRowVM>();
                return;
            }
            var rows = new List<StakeRewardRowVM>(rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                if (r == null) continue;
                rows.Add(new StakeRewardRowVM(r.Label, r.Detail, r.Kind, KindTag(r.Kind)));
            }
            Rewards = rows;
        }

        /// <summary>Short uppercase kind tag (was the View's KindLabel — pure data mapping).</summary>
        public static string KindTag(StakeRewardKind kind)
        {
            switch (kind)
            {
                case StakeRewardKind.Badge:    return "BADGE";
                case StakeRewardKind.Title:    return "TITLE";
                case StakeRewardKind.Cosmetic: return "COSMETIC";
                case StakeRewardKind.Trickle:  return "TRICKLE";
                default:                       return "PERK";
            }
        }
    }
}
