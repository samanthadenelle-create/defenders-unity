// SCAFFOLD — CLI must build-verify under Unity Test Framework
// =============================================================================
// GameStateRoundtripTests (EditMode) — WO-329 regression suite.
// -----------------------------------------------------------------------------
// A lightweight persistence-layer roundtrip that complements (does NOT
// duplicate) the authoritative service-level test in DeNelle.Core.Tests
// (SaveLoadRoundTripTest). It serializes a populated SaveSchema.SaveFile through
// the shared SaveSchema.JsonSettings and deserializes it, asserting that a
// representative spread of scalar, enum, struct, and collection fields survive
// the wire format byte-for-byte. This guards the JSON contract itself (enum
// converters, kebab wire strings, nullable wrappers) without PlayerPrefs or a
// MonoBehaviour service.
// =============================================================================

using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using DeNelle.Core.State;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class GameStateRoundtripTests
    {
        private static SaveSchema.SaveFile RoundTrip(SaveSchema.SaveFile file)
        {
            string json = JsonConvert.SerializeObject(file, SaveSchema.JsonSettings);
            return JsonConvert.DeserializeObject<SaveSchema.SaveFile>(json, SaveSchema.JsonSettings);
        }

        [Test]
        public void fresh_gamestate_has_expected_defaults()
        {
            var s = UnityEngine.ScriptableObject.CreateInstance<GameState>();
            Assert.That(s.SchemaVersion, Is.EqualTo(SaveSchema.CurrentVersion));
            Assert.That(s.Resources.Crystals, Is.EqualTo(250));
            Assert.That(s.Resources.Stone, Is.EqualTo(80));
            Assert.That(s.Resources.Coins, Is.EqualTo(15));
            Assert.That(s.Voidshards, Is.EqualTo(5));
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void scalar_and_struct_fields_survive_roundtrip()
        {
            var file = new SaveSchema.SaveFile();
            file.State.Onboarded = true;
            file.State.BestWave = 13;
            file.State.Wood = 47;
            file.State.Iron = 33;
            file.State.Stone = 88;
            file.State.Resources = new ResourceBalance(900, 320, 64);

            var back = RoundTrip(file);
            Assert.That(back.State.Onboarded, Is.True);
            Assert.That(back.State.BestWave, Is.EqualTo(13));
            Assert.That(back.State.Wood, Is.EqualTo(47));
            Assert.That(back.State.Iron, Is.EqualTo(33));
            Assert.That(back.State.Stone, Is.EqualTo(88));
            Assert.That(back.State.Resources.HasValue, Is.True);
            Assert.That(back.State.Resources.Value.Crystals, Is.EqualTo(900));
            Assert.That(back.State.Resources.Value.Stone, Is.EqualTo(320));
            Assert.That(back.State.Resources.Value.Coins, Is.EqualTo(64));
        }

        [Test]
        public void enum_fields_survive_roundtrip()
        {
            var file = new SaveSchema.SaveFile();
            file.State.HeroClass = HeroClass.Ranger;
            file.State.Difficulty = Difficulty.Hard;
            file.State.MovementStyle = MovementStyle.Tap;
            file.State.BreachStyle = BreachStyle.TowerSim;

            var back = RoundTrip(file);
            Assert.That(back.State.HeroClass, Is.EqualTo(HeroClass.Ranger));
            Assert.That(back.State.Difficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(back.State.MovementStyle, Is.EqualTo(MovementStyle.Tap));
            Assert.That(back.State.BreachStyle, Is.EqualTo(BreachStyle.TowerSim));
        }

        [Test]
        public void collection_fields_survive_roundtrip()
        {
            var file = new SaveSchema.SaveFile();
            file.State.OwnedItemIds = new List<string> { "item-1", "item-2" };
            file.State.Towers = new List<double> { 2, 0, 1, 3, 0, 0, 0, 0, 0 };
            file.State.BuildingDamage = new Dictionary<string, double>
            {
                { "gate-2", 40 },
                { "heart", 0 },
            };
            file.State.GearInventory = new Dictionary<string, int>   // v20 — shop-bought gear counts
            {
                { "sword-iron", 2 },
                { "armor-leather", 1 },
            };

            var back = RoundTrip(file);
            Assert.That(back.State.OwnedItemIds, Is.EqualTo(new List<string> { "item-1", "item-2" }));
            Assert.That(back.State.Towers.Count, Is.EqualTo(9));
            Assert.That(back.State.Towers[3], Is.EqualTo(3));
            Assert.That(back.State.BuildingDamage["gate-2"], Is.EqualTo(40));
            Assert.That(back.State.BuildingDamage.ContainsKey("heart"), Is.True);
            Assert.That(back.State.GearInventory["sword-iron"], Is.EqualTo(2), "v20 gear inventory must survive the wire format.");
            Assert.That(back.State.GearInventory["armor-leather"], Is.EqualTo(1));
        }

        [Test]
        public void envelope_carries_current_schema_version()
        {
            var back = RoundTrip(new SaveSchema.SaveFile());
            Assert.That(back.Format, Is.EqualTo(SaveSchema.FileFormat));
            Assert.That(back.StoreVersion, Is.EqualTo(SaveSchema.CurrentVersion));
        }
    }
}
