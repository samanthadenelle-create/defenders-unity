// WO-1275 -- client progression rewards resolved into the existing persisted
// ProgressionUnlocks authority. No scene/model presence is consulted.
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Entitlements;
using DeNelle.Core.State;
using DeNelle.Core.Backend;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DeNelle.Village
{
    public static class RewardedProgression
    {
        public const string StoneWallId = "wall_wood";
        public const int StoneWallLevel = 2;
        public const string StoneGateId = "gate_stone";
        public const string HealingCaravanId = "healing_caravan";
        public const int HealingCaravanPlansWave = 7;
        public const string StoneGateLockReason = "Create a Stone Wall to unlock";
        public const string HealingCaravanLockReason = "Recover its plans after Wave 7";
        private static int s_lastUnlockWave = -1;

        public static bool AwardWaveClearUnlocks(int waveNumber)
        {
            if (waveNumber < HealingCaravanPlansWave) return false;
            if (!ProgressionUnlocks.Unlock(HealingCaravanId)) return false;
            s_lastUnlockWave = waveNumber;
            return true;
        }

        public static bool TryGetWaveUnlockFor(int waveNumber, out string displayName)
        {
            bool found = s_lastUnlockWave == waveNumber && ProgressionUnlocks.IsUnlocked(HealingCaravanId);
            displayName = found ? "Healing Caravan" : null;
            return found;
        }

        public static bool IsStoneWallCreation(string itemId, int level) =>
            string.Equals(itemId, StoneWallId, System.StringComparison.OrdinalIgnoreCase) && level >= StoneWallLevel;

        public static bool ShouldAwardHealingCaravanPlans(int wavesCompleted, bool alreadyUnlocked) =>
            wavesCompleted >= HealingCaravanPlansWave && !alreadyUnlocked;

        public static string LockReasonFor(string catalogId)
        {
            if (string.Equals(catalogId, StoneGateId, System.StringComparison.OrdinalIgnoreCase)) return StoneGateLockReason;
            if (string.Equals(catalogId, HealingCaravanId, System.StringComparison.OrdinalIgnoreCase)) return HealingCaravanLockReason;
            return null;
        }

        public static bool TryUnlockStoneGate(string upgradedItemId, int newLevel)
        {
            if (!IsStoneWallCreation(upgradedItemId, newLevel)) return false;
            bool awarded = ProgressionUnlocks.Unlock(StoneGateId);
            FlowTrace.Step("Progression", $"first Stone Wall creation -> Stone Gate unlock (new={awarded})");
            return awarded;
        }
    }

    /// <summary>Persisted wave-count scan matching the earlier Castle Defense Plans pattern.</summary>
    public sealed class HealingCaravanPlansService : MonoBehaviour
    {
        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<HealingCaravanPlansService>() != null) return;
            var host = new GameObject("HealingCaravanPlansService");
            DontDestroyOnLoad(host);
            host.AddComponent<HealingCaravanPlansService>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || !RewardedProgression.ShouldAwardHealingCaravanPlans(
                    state.WavesCompleted, ProgressionUnlocks.IsUnlocked(RewardedProgression.HealingCaravanId))) return;

            if (RewardedProgression.AwardWaveClearUnlocks(RewardedProgression.HealingCaravanPlansWave))
            {
                BuildFeedbackToast.Show("Healing Caravan Plans recovered.");
                FlowTrace.Step("Progression", "Wave 7 reward: Healing Caravan Plans learned and persisted (once-ever).");
            }
        }
    }

    /// <summary>Restores permanent progression flags from authenticated server authority.</summary>
    public sealed class RewardedProgressionEntitlementService : MonoBehaviour
    {
        private readonly SkuEntitlementService _entitlements = new SkuEntitlementService();
        private bool _refreshing;
        private float _nextRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<RewardedProgressionEntitlementService>() != null) return;
            var host = new GameObject("RewardedProgressionEntitlementService");
            DontDestroyOnLoad(host);
            host.AddComponent<RewardedProgressionEntitlementService>();
        }

        private void Update()
        {
            if (_refreshing || Time.unscaledTime < _nextRefresh) return;
            string playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrEmpty(playerId)) { _nextRefresh = Time.unscaledTime + 5f; return; }
            Refresh(playerId).Forget();
        }

        private async UniTaskVoid Refresh(string playerId)
        {
            _refreshing = true;
            _nextRefresh = Time.unscaledTime + 60f;
            try
            {
                if (!await _entitlements.RefreshAsync(playerId)) return;
                double now = Time.realtimeSinceStartupAsDouble;
                RestoreIfProgressionGrant(_entitlements.Snapshot, RewardedProgression.StoneGateId, now);
                RestoreIfProgressionGrant(_entitlements.Snapshot, RewardedProgression.HealingCaravanId, now);
            }
            finally { _refreshing = false; }
        }

        internal static bool RestoreIfProgressionGrant(SkuEntitlementSnapshot snapshot, string catalogId, double now)
        {
            return snapshot != null && snapshot.IsProgressionEntitled(catalogId, now) &&
                   ProgressionUnlocks.Unlock(catalogId);
        }
    }
}
