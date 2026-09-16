using System;
using System.Collections.Generic;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedBaseConstructionProof
    {
        public static void RunBatch()
        {
            var property = new OwnedBaseState {
                baseId = "construction-proof", sourceRaidId = "iron_bastion", templateVersion = "test-v1",
                captureReceiptId = "receipt", suppliesReceiptId = "receipt",
                milestoneFlags = OwnedBaseMilestones.OwnershipRevealed | OwnedBaseMilestones.EssentialRepairCompleted,
                structures = new List<OwnedBaseStructure> {
                    new OwnedBaseStructure { instanceId = "old-tower", placement = new PlacedStructureData("tower_ground_archer", 0, 0, 0, 1) }
                }
            };
            string before = JsonConvert.SerializeObject(property);
            Require(OwnedBaseConstruction.TryAdd(property, 1, "new-tower",
                new PlacedStructureData("tower_ground_archer", 3, 4, 0, 1), out var added, out var reason), reason);
            Require(before == JsonConvert.SerializeObject(property) && added.structures.Count == 2 && added.revision == 2,
                "Add must detach the edit and advance one revision.");
            Require(!OwnedBaseConstruction.TryAdd(added, 1, "another", new PlacedStructureData("wall_stone", 1, 2, 0, 1), out _, out _),
                "A stale confirmation must not add another structure.");
            Require(!OwnedBaseConstruction.TryAdd(added, 2, "new-tower", new PlacedStructureData("wall_stone", 1, 2, 0, 1), out _, out _),
                "A construction identity cannot be reused.");
            Require(OwnedBaseConstruction.TryUpgrade(added, 2, "new-tower", 1, 3, out var upgraded, out reason), reason);
            Require(added.structures[1].placement.level == 1 && upgraded.structures[1].placement.level == 2 && upgraded.revision == 3,
                "Upgrade must preserve the prior layout and increment only one level.");
            Require(!OwnedBaseConstruction.TryUpgrade(upgraded, 3, "new-tower", 1, 3, out _, out _), "A repeated stale-level upgrade must refuse.");
            Require(!OwnedBaseConstruction.TryUpgrade(upgraded, 3, "new-tower", 2, 2, out _, out _), "Catalog maximum must refuse.");
            Require(OwnedBaseConstruction.TryRetire(upgraded, 3, "new-tower", out var sold, out reason), reason);
            Require(sold.structures.Count == 2 && sold.structures[1].retired && sold.structures[1].condition01 == 0f &&
                sold.structures[1].placement.level == 2 && !upgraded.structures[1].retired,
                "Sale must retain identity and earned level without changing the previous revision.");
            Require(!OwnedBaseConstruction.TryRetire(sold, 4, "new-tower", out _, out _), "Repeated sale must refuse.");
            Require(!OwnedBaseProgression.TryRepair(sold, "new-tower", default, out _, out _), "A sold structure must not become a repairable ruin.");
            var restored = JsonConvert.DeserializeObject<OwnedBaseState>(JsonConvert.SerializeObject(sold));
            Require(OwnedBaseProgression.Validate(restored, out reason) && restored.Clone().structures[1].retired,
                "Serialization and detached cloning must retain the sale tombstone.");
            var oldRecord = JsonConvert.DeserializeObject<OwnedBaseStructure>("{\"instanceId\":\"legacy\",\"condition01\":0}");
            Require(!oldRecord.retired, "Missing additive field must preserve old repairable ruins.");
            var damaged = property.Clone(); damaged.structures[0].condition01 = .5f;
            Require(!OwnedBaseConstruction.TryUpgrade(damaged, 1, "old-tower", 1, 3, out _, out _), "Damage must not be erased through an upgrade.");
            VerifyCommit(property);
            if (!DeNelle.Editor.Regression.OwnedBaseStateRegression.Run(out reason)) throw new Exception(reason);
            Debug.Log("OWNED_BASE_CONSTRUCTION_OK detached add/upgrade/retire, stale/duplicate refusal, tombstone compatibility, atomic payment/refund rollback/retry/reload; gameplay adapter still required");
        }

        private static void VerifyCommit(OwnedBaseState property)
        {
            Require(GameStateService.Instance == null, "Construction proof requires an isolated editor.");
            var oldProvider = GameStateService.Provider;
            bool oldCloud = GameStateService.SuppressCloudForIsolatedProof;
            var provider = new MemoryProvider();
            var state = ScriptableObject.CreateInstance<GameState>();
            var host = new GameObject("ConstructionCommitProof");
            try
            {
                GameStateService.Provider = provider;
                GameStateService.SuppressCloudForIsolatedProof = true;
                state.OwnedBase = property.Clone();
                state.Wood = state.Iron = 100;
                state.Resources = new ResourceBalance(100, 100, 73);
                state.BaseLayout = new List<PlacedStructureData> { new PlacedStructureData("wall_wood", 8, 9, 0, 2) };
                var service = host.AddComponent<GameStateService>();
                Require(HonestFeedbackFixture.InstallState(service, state), "Cannot install construction save fixture.");
                Require(service.TrySave(out var reason), reason);
                string durable = provider.Read(SaveSchema.PlayerPrefsKey);
                string original = JsonConvert.SerializeObject(state.OwnedBase);
                double stamp = service.LastLocalSaveUnixMs;
                int signals = 0; service.ResourcesChanged.AddListener(() => signals++);
                Require(OwnedBaseConstruction.TryAdd(state.OwnedBase, 1, "paid-tower",
                    new PlacedStructureData("tower_ground_archer", 3, 4, 0, 1), out var proposal, out reason), reason);
                var price = new DeNelle.Core.Catalog.ResourceCost { wood = 7, iron = 11, stone = 13, crystals = 17 };
                provider.Fail = true;
                Require(!service.TryCommitOwnedBaseConstruction(1, proposal, price, default, out _, out _), "Failed save must refuse construction.");
                Require(provider.Read(SaveSchema.PlayerPrefsKey) == durable && JsonConvert.SerializeObject(state.OwnedBase) == original &&
                    state.Wood == 100 && state.Iron == 100 && state.Resources.Stone == 100 && state.Resources.Crystals == 100 &&
                    service.LastLocalSaveUnixMs == stamp && signals == 0, "Failed construction changed state, durability or notifications.");
                provider.Fail = false;
                Require(service.TryCommitOwnedBaseConstruction(1, proposal, price, default, out _, out reason), reason);
                Require(!service.TryCommitOwnedBaseConstruction(1, proposal, price, default, out _, out _), "Repeated construction must not charge twice.");
                Require(service.Load(), "Construction save did not reload.");
                Require(service.State.Wood == 93 && service.State.Iron == 89 && service.State.Resources.Stone == 87 &&
                    service.State.Resources.Crystals == 83 && service.State.Resources.Coins == 73 && service.State.BaseLayout[0].cellX == 8 &&
                    service.State.OwnedBase.structures.Count == 2, "Cold reload lost the edit/payment or changed story layout and coins.");
                Require(OwnedBaseConstruction.TryRetire(service.State.OwnedBase, 2, "paid-tower", out var sale, out reason), reason);
                provider.Fail = true;
                Require(!service.TryCommitOwnedBaseConstruction(2, sale, default, price, out _, out _) && !service.State.OwnedBase.structures[1].retired,
                    "Failed sale must not remove a structure or grant its refund.");
                provider.Fail = false;
                Require(service.TryCommitOwnedBaseConstruction(2, sale, default, price, out var refund, out reason), reason);
                Require(refund.wood == 7 && refund.iron == 11 && refund.stone == 13 && refund.crystals == 17 &&
                    service.State.Wood == 100 && service.State.Iron == 100 && service.State.Resources.Stone == 100 && service.State.Resources.Crystals == 100,
                    "Sale did not persist the exact affordable refund.");
                Require(!service.TryCommitOwnedBaseConstruction(2, sale, default, price, out _, out _), "Repeated sale must not refund twice.");
                Require(service.Load() && service.State.OwnedBase.structures[1].retired, "Sale tombstone did not survive a cold reload.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                HonestFeedbackFixture.SetGssInstance(null);
                UnityEngine.Object.DestroyImmediate(state);
                GameStateService.Provider = oldProvider;
                GameStateService.SuppressCloudForIsolatedProof = oldCloud;
            }
        }

        private sealed class MemoryProvider : ISaveProvider
        {
            private readonly Dictionary<string, string> _data = new Dictionary<string, string>();
            public bool Fail;
            public bool Exists(string key) => _data.ContainsKey(key);
            public string Read(string key) => _data.TryGetValue(key, out var value) ? value : "";
            public void Write(string key, string value) { if (Fail) throw new System.IO.IOException("Injected construction failure"); _data[key] = value; }
            public void Delete(string key) => _data.Remove(key);
        }

        private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    }
}
