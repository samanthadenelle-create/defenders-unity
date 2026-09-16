using System;
using System.Collections.Generic;
using System.Reflection;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class RaidLootAuthorityProof
    {
        public static bool Run(out string reason)
        {
            try { RunBatch(); reason = "RAID_LOOT_AUTHORITY_OK"; return true; }
            catch (Exception error) { reason = "RAID_LOOT_AUTHORITY_FAIL " + error; return false; }
        }

        public static void RunBatch()
        {
            if (GameStateService.Instance != null || EconomyService.Instance != null)
                throw new InvalidOperationException("Raid loot proof requires an isolated editor with no live wallet.");
            var priorProvider = GameStateService.Provider;
            bool priorCloud = GameStateService.SuppressCloudForIsolatedProof;
            GameObject serviceObject = null, raidObject = null;
            GameState fixture = null;
            try
            {
                GameStateService.Provider = new MemoryProvider();
                GameStateService.SuppressCloudForIsolatedProof = true;
                serviceObject = new GameObject("RaidLootAuthorityProof.Save");
                var service = serviceObject.AddComponent<GameStateService>();
                fixture = ScriptableObject.CreateInstance<GameState>();
                Require(HonestFeedbackFixture.InstallState(service, fixture), "Could not install isolated wallet.");
                raidObject = new GameObject("RaidLootAuthorityProof.Raid");
                raidObject.SetActive(false);
                var raid = raidObject.AddComponent<RaidVictoryController>();
                var grant = typeof(RaidVictoryController).GetMethod("GrantLoot", BindingFlags.NonPublic | BindingFlags.Instance);
                Require(grant != null, "Actual raid grant entry point missing.");
                var basket = new ResourceCost(wood: 11, stone: 13, iron: 17, crystals: 19, coins: 23);
                fixture.Wood = 0; fixture.Iron = 0; fixture.Resources = new ResourceBalance(0, 0, 0);
                Require(EconomyService.Instance == null, "Missing-component premise does not hold.");
                grant.Invoke(raid, new object[] { basket });
                var economy = EconomyService.Instance;
                Require(economy != null && EconomyService.EnsureAvailable() == economy,
                    "Missing authority must be restored once and reused.");
                Require(fixture.Wood == 11 && fixture.Resources.Stone == 13 && fixture.Iron == 17
                    && fixture.Resources.Crystals == 19 && fixture.Resources.Coins == 23,
                    "Missing-component payout must credit all five resource axes.");
                VerifyCredit(raid, basket, false);

                fixture.Wood = TownBankCapacity.MaxOf(BankResource.Wood) - 2;
                fixture.Iron = TownBankCapacity.MaxOf(BankResource.Iron) - 2;
                fixture.Resources.Stone = TownBankCapacity.MaxOf(BankResource.Stone) - 2;
                Require(fixture.Wood >= 0 && fixture.Iron >= 0 && fixture.Resources.Stone >= 0,
                    "Bank capacity fixture must leave two units of headroom.");
                int woodBefore = fixture.Wood, ironBefore = fixture.Iron, stoneBefore = fixture.Resources.Stone;
                grant.Invoke(raid, new object[] { basket });
                Require(fixture.Wood - woodBefore == 2 && fixture.Iron - ironBefore == 2
                    && fixture.Resources.Stone - stoneBefore == 2 && fixture.Resources.Crystals == 38
                    && fixture.Resources.Coins == 46, "Restored authority must preserve ordinary bank caps.");
                VerifyCredit(raid, new ResourceCost(wood: 2, stone: 2, iron: 2, crystals: 19, coins: 23), true);
                Debug.Log("RAID_LOOT_AUTHORITY_OK missing component restores all five axes; existing authority reused; caps and credited basket verified");
            }
            finally
            {
                if (raidObject != null) UnityEngine.Object.DestroyImmediate(raidObject);
                if (EconomyService.Instance != null) UnityEngine.Object.DestroyImmediate(EconomyService.Instance.gameObject);
                if (serviceObject != null) UnityEngine.Object.DestroyImmediate(serviceObject);
                HonestFeedbackFixture.SetGssInstance(null);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                GameStateService.Provider = priorProvider;
                GameStateService.SuppressCloudForIsolatedProof = priorCloud;
            }
        }

        private static void VerifyCredit(RaidVictoryController raid, ResourceCost expected, bool shortfall)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var actual = (ResourceCost)typeof(RaidVictoryController).GetField("_credited", flags).GetValue(raid);
            Require(actual.Wood == expected.Wood && actual.Stone == expected.Stone && actual.Iron == expected.Iron
                && actual.Crystals == expected.Crystals && actual.Coins == expected.Coins,
                "Raid result must report measured wallet credit on all five axes.");
            Require((bool)typeof(RaidVictoryController).GetField("_rewardShort", flags).GetValue(raid) == shortfall,
                "Raid shortfall indicator must match the actual credit.");
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }

        private sealed class MemoryProvider : ISaveProvider
        {
            private readonly Dictionary<string, string> data = new Dictionary<string, string>();
            public bool Exists(string slot) => data.ContainsKey(slot);
            public string Read(string slot) => data.TryGetValue(slot, out var value) ? value : string.Empty;
            public void Write(string slot, string json) => data[slot] = json;
            public void Delete(string slot) => data.Remove(slot);
        }
    }
}
