using System;
using System.Collections.Generic;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;
using Cost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Editor
{
    public static class OwnedTownRepairPaymentProof
    {
        public static bool Run(out string reason)
        {
            try { RunBatch(); reason = "OWNED_TOWN_REPAIR_PAYMENT_OK"; return true; }
            catch (Exception error) { reason = "OWNED_TOWN_REPAIR_PAYMENT_FAIL " + error; return false; }
        }

        public static void RunBatch()
        {
            if (GameStateService.Instance != null) throw new InvalidOperationException("Repair proof requires an isolated editor.");
            var priorProvider = GameStateService.Provider;
            bool priorCloud = GameStateService.SuppressCloudForIsolatedProof;
            var provider = new MemoryProvider();
            GameObject host = null;
            GameState fixture = null;
            try
            {
                GameStateService.Provider = provider;
                GameStateService.SuppressCloudForIsolatedProof = true;
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Wood = fixture.Iron = 10;
                fixture.Resources = new ResourceBalance(17, 10, 19);
                fixture.BaseLayout = new List<PlacedStructureData> { new PlacedStructureData("wall_wood", 8, 9, 0, 2) };
                fixture.OwnedBase = new OwnedBaseState {
                    baseId = "repair-proof", sourceRaidId = "iron_bastion", templateVersion = "repair-proof-v1",
                    captureReceiptId = "repair-receipt", suppliesReceiptId = "repair-receipt",
                    milestoneFlags = OwnedBaseMilestones.OwnershipRevealed,
                    repairSupplies = new Cost { wood = 2, iron = 3, stone = 4 },
                    structures = new List<OwnedBaseStructure> {
                        new OwnedBaseStructure { instanceId = "ruin-1", placement = new PlacedStructureData("wall_wood", 0, 0, 0, 1), condition01 = 0f },
                        new OwnedBaseStructure { instanceId = "ruin-2", placement = new PlacedStructureData("wall_wood", 1, 0, 0, 1), condition01 = .5f }
                    }
                };
                host = new GameObject("OwnedTownRepairPaymentProof");
                var service = host.AddComponent<GameStateService>();
                Require(HonestFeedbackFixture.InstallState(service, fixture), "Cannot install isolated state.");
                Require(service.TrySave(out var reason), reason);
                string before = JsonConvert.SerializeObject(fixture.OwnedBase);
                string durable = provider.Read(SaveSchema.PlayerPrefsKey);
                double stamp = service.LastLocalSaveUnixMs;
                int notifications = 0;
                service.ResourcesChanged.AddListener(() => notifications++);
                var quote = new Cost { wood = 5, iron = 7, stone = 9 };
                provider.FailWrites = true;
                Require(!service.TryCommitOwnedBaseRepair("ruin-1", quote, out _), "Injected failed save must refuse repair.");
                Require(JsonConvert.SerializeObject(fixture.OwnedBase) == before && fixture.Wood == 10 && fixture.Iron == 10
                    && fixture.Resources.Stone == 10 && notifications == 0 && service.LastLocalSaveUnixMs == stamp
                    && provider.Read(SaveSchema.PlayerPrefsKey) == durable, "Failed repair must restore payment, supplies, condition and durable state.");
                provider.FailWrites = false;
                Require(service.TryCommitOwnedBaseRepair("ruin-1", quote, out reason), reason);
                Require(fixture.Wood == 7 && fixture.Iron == 6 && fixture.Resources.Stone == 5
                    && fixture.OwnedBase.repairSupplies.IsZero && fixture.OwnedBase.structures[0].condition01 == 1f
                    && fixture.OwnedBase.structures[1].condition01 == .5f && notifications == 1,
                    "Repair must consume supplies first, charge only the exact shortfall, and change only the chosen ruin.");
                int revision = fixture.OwnedBase.revision;
                Require(!service.TryCommitOwnedBaseRepair("ruin-1", quote, out _) && fixture.OwnedBase.revision == revision
                    && fixture.Wood == 7, "Repeated repair must not debit twice.");
                Require(service.Load(), "Paid repair did not reload.");
                var loaded = service.State;
                Require(loaded.Wood == 7 && loaded.Iron == 6 && loaded.Resources.Stone == 5
                    && loaded.Resources.Crystals == 17 && loaded.Resources.Coins == 19
                    && loaded.OwnedBase.structures[0].condition01 == 1f && loaded.OwnedBase.repairSupplies.IsZero
                    && loaded.BaseLayout.Count == 1 && loaded.BaseLayout[0].cellX == 8,
                    "Reload must preserve repair, exact wallet and unrelated story layout/premium balances.");
                before = JsonConvert.SerializeObject(loaded.OwnedBase);
                Require(!service.TryCommitOwnedBaseRepair("ruin-2", new Cost { wood = 8 }, out _)
                    && JsonConvert.SerializeObject(loaded.OwnedBase) == before && loaded.Wood == 7,
                    "Unaffordable repair must leave all state unchanged.");
                Require(!service.TryCommitOwnedBaseRepair("ruin-2", new Cost { crystals = 1 }, out _),
                    "Ordinary repair must never silently spend crystals.");
                Require(service.TryCommitOwnedBaseRepair("ruin-2", new Cost { wood = 7, iron = 6, stone = 5 }, out reason), reason);
                Require(loaded.Wood == 0 && loaded.Iron == 0 && loaded.Resources.Stone == 0
                    && loaded.OwnedBase.structures[1].condition01 == 1f, "Repair must remain possible after capture supplies are exhausted.");
            }
            finally
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                HonestFeedbackFixture.SetGssInstance(null);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                GameStateService.Provider = priorProvider;
                GameStateService.SuppressCloudForIsolatedProof = priorCloud;
            }
            if (!DeNelle.Editor.Regression.OwnedBaseStateRegression.Run(out string owned)) throw new Exception(owned);
            Debug.Log("OWNED_TOWN_REPAIR_PAYMENT_OK supply-first split, exact wallet debit, failure rollback, retry, reload and continued repairs");
        }

        private static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        private sealed class MemoryProvider : ISaveProvider
        {
            private readonly Dictionary<string, string> data = new Dictionary<string, string>();
            public bool FailWrites;
            public bool Exists(string slot) => data.ContainsKey(slot);
            public string Read(string slot) => data.TryGetValue(slot, out var value) ? value : "";
            public void Write(string slot, string value) { if (FailWrites) throw new System.IO.IOException("Injected repair save failure"); data[slot] = value; }
            public void Delete(string slot) => data.Remove(slot);
        }
    }
}
