using System;
using DeNelle.Core.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace DeNelle.Editor
{
    public static class StoneSaveMigrationProof
    {
        public static void RunIntegrated()
        {
            RunBatch();
            if (!DeNelle.Editor.Regression.OwnedBaseStateRegression.Run(out string owned)) throw new Exception(owned);
            if (!ObsidianQueueRegression.Run(out string queue)) throw new Exception(queue);
            if (!BuildEconomyRegression.Run(out string build)) throw new Exception(build);
            if (!DeNelle.Editor.Regression.ResourceAuthorityRegression.Run(out string authority)) throw new Exception(authority);
            if (!DeNelle.Editor.Regression.EconomySweepRegression.Run(out string sweep)) throw new Exception(sweep);
            if (!HonestFeedbackGrantRegression.Run(out string feedback)) throw new Exception(feedback);
            if (!RaidScoringRegression.Run(out string raid)) throw new Exception(raid);
            if (!EchoSpecializationRegression.Run(out string echoes)) throw new Exception(echoes);
            if (!OfflineHarvestRegression.Run(out string offline)) throw new Exception(offline);
            if (!PackGrantRegression.Run(out string packs)) throw new Exception(packs);
            if (!SiegeLossStakesRegression.Run(out string stakes)) throw new Exception(stakes);
            Debug.Log("STONE_ECONOMY_INTEGRATION_OK save compatibility, owned repair, queue refunds and build economy");
        }

        public static void RunBatch()
        {
            var balance = JsonConvert.DeserializeObject<ResourceBalance>(
                "{\"crystals\":17,\"food\":83,\"coins\":29}", SaveSchema.JsonSettings);
            Require(balance.Stone == 83 && balance.Crystals == 17 && balance.Coins == 29,
                "Legacy wallet must load its exact Stone balance.");
            balance.Stone += 7;
            Require(balance.Stone == 90, "Stone must hold the granted amount.");
            balance.Stone -= 4;
            Require(balance.Stone == 86 && typeof(ResourceBalance).GetMember("Food").Length == 0,
                "The wallet must expose Stone without a legacy Food runtime member.");
            string json = JsonConvert.SerializeObject(balance, SaveSchema.JsonSettings);
            var wire = JObject.Parse(json);
            Require((int)wire["food"] == 86 && wire["stone"] == null && wire["Food"] == null,
                "Shipped protocol must contain exactly one Stone balance under its legacy key.");
            for (int i = 0; i < 3; i++)
                balance = JsonConvert.DeserializeObject<ResourceBalance>(
                    JsonConvert.SerializeObject(balance, SaveSchema.JsonSettings), SaveSchema.JsonSettings);
            Require(balance.Stone == 86 && balance.Crystals == 17 && balance.Coins == 29,
                "Repeated save/load must not duplicate or lose balances.");
            VerifyUnityAsset();
            var delta = JsonConvert.DeserializeObject<GameStateService.SyncDeltaPayload>(
                "{\"Food\":123,\"Crystals\":7,\"Coins\":9}", SaveSchema.JsonSettings);
            Require(delta.Stone == 123, "Existing offline queue must retain its Stone grant.");
            var deltaWire = JObject.Parse(JsonConvert.SerializeObject(delta, SaveSchema.JsonSettings));
            Require((int)deltaWire["Food"] == 123 && deltaWire["Stone"] == null,
                "Offline queue must retain the deployed key without emitting the retired second balance.");
            var job = JsonConvert.DeserializeObject<BuildJobData>(
                "{\"paidWood\":41,\"paidFood\":67,\"paidCoins\":19}", SaveSchema.JsonSettings);
            Require(job.Paid.Stone == 67 && job.Paid.Wood == 41 && job.Paid.Coins == 19,
                "Existing queued jobs must retain the exact Stone refund basket.");
            var jobWire = JObject.Parse(JsonConvert.SerializeObject(job, SaveSchema.JsonSettings));
            Require((int)jobWire["paidFood"] == 67 && jobWire["paidStone"] == null,
                "Job basket must remain readable by the shipped save contract.");
            Require(new BuildJobData().Paid.IsZero, "Unrecorded legacy costs must not invent refunds.");
            var oldBasket = JsonConvert.DeserializeObject<JobCost>("{\"Food\":37}", SaveSchema.JsonSettings);
            Require(oldBasket.Stone == 37 && oldBasket.Describe().Contains("37 stone"),
                "Serialized paid basket and its displayed refund must agree on Stone.");
            var repair = JsonConvert.DeserializeObject<DeNelle.Core.Catalog.ResourceCost>(
                "{\"wood\":11,\"food\":23,\"iron\":7,\"crystals\":0}", SaveSchema.JsonSettings);
            Require(repair.stone == 23 && repair.wood == 11 && repair.iron == 7 && !repair.IsZero,
                "Saved capture allowance and catalog repair costs must preserve Stone.");
            var costWire = JObject.Parse(JsonConvert.SerializeObject(repair, SaveSchema.JsonSettings));
            Require((int)costWire["food"] == 23 && costWire["stone"] == null,
                "Saved capture/repair costs must retain one backward-compatible Stone axis.");
            var raidCost = JsonConvert.DeserializeObject<DeNelle.Village.ResourceCost>(
                "{\"Wood\":11,\"Food\":23,\"Iron\":7,\"Crystals\":3,\"Coins\":19}", SaveSchema.JsonSettings);
            Require(raidCost.Stone == 23 && raidCost.Wood == 11 && raidCost.Coins == 19,
                "Legacy raid reward baskets must retain the exact Stone amount.");
            var raidWire = JObject.Parse(JsonConvert.SerializeObject(raidCost, SaveSchema.JsonSettings));
            Require((int)raidWire["Food"] == 23 && raidWire["Stone"] == null,
                "Raid reward serialization must preserve its shipped key without a second resource.");
            Require((int)DeNelle.Core.Economy.BankResource.Stone == 2,
                "Storage resource enum must retain its serialized numeric value.");
            foreach (string spelling in new[] { "food", "Food", "grain", "stone", "Stone" })
                Require(DeNelle.Core.Economy.TownBankCapacity.TryParseResource(spelling, out var bank)
                    && bank == DeNelle.Core.Economy.BankResource.Stone,
                    "Legacy and current storage names must resolve to the same Stone axis: " + spelling);
            var oldBank = JsonConvert.DeserializeObject<DeNelle.Core.Economy.BankResource>("\"Food\"", SaveSchema.JsonSettings);
            Require(oldBank == DeNelle.Core.Economy.BankResource.Stone &&
                JsonConvert.SerializeObject(oldBank, SaveSchema.JsonSettings) == "\"Food\"",
                "String-enum save compatibility must retain the old key without a Food runtime member.");
            VerifyEnum(typeof(DeNelle.Core.ResourceType), 2);
            VerifyEnum(typeof(DeNelle.Village.MineResource), 2);
            VerifyEnum(typeof(DeNelle.Village.Buildings.Progression.HarvestResource), 1);
            VerifyEnum(typeof(DeNelle.Village.HarvestTarget), 2);
            VerifyEnum(typeof(DeNelle.Core.UI.ElarionUiKit.CurrencyKind), 4);
            VerifyStoneField(typeof(DeNelle.Core.Defense.StakesLedger), "f");
            VerifyStoneField(typeof(DeNelle.Core.World.RealmClearReward), "food");
            VerifyStoneField(typeof(DeNelle.Village.OfflineHarvestResult), "Food");
            VerifyStoneField(typeof(DeNelle.Village.WaveClearPayout), "Food");
            VerifyStoneField(typeof(DeNelle.Village.Crafting.GearRecipeCost), "food");
            VerifyStoneField(typeof(DeNelle.Village.Crafting.JewelerRecipeCost), "food");
            VerifyStoneField(typeof(DeNelle.Wallet.PackEconomy), "stone");
            VerifyRenamedField(typeof(DeNelle.Core.Quests.DailyQuestSlotReward), "RewardStone", "RewardFood", "rewardFood");
            VerifyRenamedField(typeof(GameModifiers), "StoneProductionMult", "FoodProductionMult", "foodProductionMult");
            VerifyRenamedField(typeof(DeNelle.Core.Defense.StructureOutcome), "RepairStone", "RepairFood", "rf");
            VerifyRenamedField(typeof(DeNelle.Core.Catalog.RepairCrystalRate), "perStone", "perFood", "perFood");
            VerifyRenamedField(typeof(DeNelle.Village.AccessoryDef), "buyStone", "buyFood", "buyFood");
            VerifyRenamedField(typeof(DeNelle.Village.WeaponDef), "buyStone", "buyFood", "buyFood");
            VerifyRenamedField(typeof(DeNelle.Village.ArmorDef), "buyStone", "buyFood", "buyFood");
            var stash = JsonConvert.DeserializeObject<LootStash>("{\"food\":20,\"stone\":5}", SaveSchema.JsonSettings);
            var stashWire = JObject.Parse(JsonConvert.SerializeObject(stash, SaveSchema.JsonSettings));
            Require(stash.LegacyFood == 20 && stash.Stone == 5 && (int)stashWire["food"] == 20
                && (int)stashWire["stone"] == 5 && typeof(LootStash).GetMember("Food").Length == 0,
                "Historical dungeon payload must preserve both original values without merging or awarding them.");
            foreach (string word in new[] { "food", "Food", "grain", "stone", "Stone" })
                Require(DeNelle.Village.Buildings.Progression.HarvestResourceNames.TryParse(word, out var harvest)
                    && harvest == DeNelle.Village.Buildings.Progression.HarvestResource.Stone,
                    "Legacy storage visual resource name must resolve to Stone: " + word);
            if (!CoreSaveContractRegression.Run(out string reason)) throw new Exception(reason);
            Debug.Log("STONE_SAVE_MIGRATION_OK legacy JSON and Unity field, single authority, repeated round-trip, core save contract");
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new Exception(reason);
        }

        private static void VerifyEnum(Type type, int numericValue)
        {
            object current = Enum.Parse(type, "Stone");
            Require(Convert.ToInt32(current) == numericValue && !Enum.IsDefined(type, "Food"),
                type.Name + " must retain its numeric identity without the retired runtime name.");
            object legacy = JsonConvert.DeserializeObject("\"Food\"", type, SaveSchema.JsonSettings);
            Require(current.Equals(legacy) && JsonConvert.SerializeObject(current, SaveSchema.JsonSettings) == "\"Food\"",
                type.Name + " must round-trip the legacy string-enum save key.");
            Require(JsonConvert.SerializeObject(current) == numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                type.Name + " default numeric serialization must remain unchanged.");
        }

        private static void VerifyStoneField(Type type, string wireKey)
        {
            object value = JsonConvert.DeserializeObject("{\"" + wireKey + "\":37}", type, SaveSchema.JsonSettings);
            var field = type.GetField("Stone");
            Require(field != null && Convert.ToInt32(field.GetValue(value)) == 37 && type.GetMember("Food").Length == 0,
                type.Name + " must load the legacy payload into Stone without a Food runtime member.");
            var serialized = JObject.Parse(JsonConvert.SerializeObject(value, SaveSchema.JsonSettings));
            Require((int?)serialized[wireKey] == 37, type.Name + " must retain its original JSON key and amount.");
        }

        private static void VerifyRenamedField(Type type, string member, string retired, string wireKey)
        {
            object value = JsonConvert.DeserializeObject("{\"" + wireKey + "\":37}", type, SaveSchema.JsonSettings);
            var field = type.GetField(member);
            Require(field != null && Convert.ToDouble(field.GetValue(value)) == 37 && type.GetMember(retired).Length == 0,
                type.Name + " must load its legacy key into " + member + " without the retired member.");
            var serialized = JObject.Parse(JsonConvert.SerializeObject(value, SaveSchema.JsonSettings));
            Require((double?)serialized[wireKey] == 37 && (member == wireKey || serialized[member] == null),
                type.Name + " must retain exactly the existing serialized key and amount.");
        }

        private static void VerifyUnityAsset()
        {
            string path = "Assets/Editor/Regression/StoneSaveProof_" + Guid.NewGuid().ToString("N") + ".asset";
            GameState fixture = null;
            var oldMode = EditorSettings.serializationMode;
            try
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Resources = new ResourceBalance(4, 73, 5);
                AssetDatabase.CreateAsset(fixture, path);
                AssetDatabase.SaveAssetIfDirty(fixture);
                string serialized = File.ReadAllText(path);
                Require(serialized.Contains("Stone: 73"), "Fixture must contain the new serialized Stone field.");
                Resources.UnloadAsset(fixture);
                fixture = null;
                File.WriteAllText(path, serialized.Replace("Stone: 73", "Food: 73"));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                fixture = AssetDatabase.LoadAssetAtPath<GameState>(path);
                Require(fixture != null && fixture.Resources.Stone == 73
                    && fixture.Resources.Crystals == 4 && fixture.Resources.Coins == 5,
                    "Unity legacy serialized asset must migrate its exact balance to Stone.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                EditorSettings.serializationMode = oldMode;
            }
        }
    }
}
