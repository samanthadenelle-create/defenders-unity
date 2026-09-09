// Repairs the Unity AssetBundle cache before Addressables can open a stale bundle.
// A failed cache replacement can leave a valid-looking cache entry whose native
// deserialization aborts the player (observed entering raider_camp_small on 2026-09-08).

using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core
{
    public static class AddressablesCacheHealth
    {
        private const string RepairEpoch = "2026-09-08-raid-cache-v1";
        private const string PrefRepairEpoch = "addressables.cache.repairEpoch";

        /// <summary>False by default. A managed download failure raises this so later gameplay
        /// degrades to placeholders instead of asking Unity to deserialize a possibly partial bundle.</summary>
        public static bool UnsafeThisSession { get; private set; }

#if !UNITY_EDITOR && !UNITY_WEBGL
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RepairBeforeAddressablesStarts()
        {
            if (PlayerPrefs.GetString(PrefRepairEpoch, string.Empty) == RepairEpoch) return;

            bool cleared = false;
            try { cleared = Caching.ClearCache(); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Flow:AddressablesCache] cache repair threw {ex.GetType().Name}: {ex.Message}");
            }

            if (cleared)
            {
                PlayerPrefs.SetString(PrefRepairEpoch, RepairEpoch);
                PlayerPrefs.Save();
                Debug.Log("[Flow:AddressablesCache] stale AssetBundle cache cleared before Addressables initialization; repair epoch recorded.");
            }
            else
            {
                // Do not stamp a failed repair. The next launch must try again.
                UnsafeThisSession = true;
                Debug.LogError("[Flow:AddressablesCache] Unity refused the pre-load cache repair. Remote bundle loads are disabled this launch to prevent native deserialization crashes.");
            }
        }
#endif

        public static void ReportDownloadFailure(string context)
        {
            UnsafeThisSession = true;
            PlayerPrefs.DeleteKey(PrefRepairEpoch);
            PlayerPrefs.Save();
            FlowTrace.Fail("AddressablesCache",
                $"bundle download/cache write failed ({context}). Cache repair is armed for next launch; " +
                "new remote loads are disabled for this launch so a partial bundle cannot crash Unity's native serializer.");
        }
    }
}
