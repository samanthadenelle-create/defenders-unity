// =============================================================================
// Canonical data — GearCatalog loader tests (EditMode)
// -----------------------------------------------------------------------------
// Regression guard for the STORE / gear pipeline: weapons.json + armor.json must
// load via the typed GearCatalog and hydrate the canonical gear set. If the
// CanonicalJson load + Newtonsoft parse chain silently breaks (empty/missing JSON,
// renamed field, broken WebGL Resources copy), the vendor store goes empty with no
// compile error — exactly the kind of silent regression this test turns RED.
//
// GearCatalog reads StreamingAssets/Data/Canonical/{weapons,armor}.json via
// CanonicalJson (Resources first, StreamingAssets fallback) — both resolve in the
// Editor, so the loader runs in EditMode with no stub. Reload() in SetUp defeats
// the static cache so a prior test's data can never leak in.
//
// GearCatalog lives in DeNelle.Village, which DeNelle.Data.Tests already references
// (see DeNelle.Data.Tests.asmdef) — so this test sits alongside the other catalog
// tests with no new asmdef reference.
// =============================================================================

using NUnit.Framework;
using DeNelle.Village;

namespace DeNelle.Data.Tests
{
    [TestFixture]
    public class GearCatalogTest
    {
        [SetUp]
        public void SetUp()
        {
            // Defeat the static cache so each test re-reads the canonical files.
            GearCatalog.Reload();
        }

        [Test]
        public void weapons_json_loads_a_non_empty_catalog()
        {
            var weapons = GearCatalog.AllWeapons();
            Assert.That(weapons, Is.Not.Null, "GearCatalog.AllWeapons() must never be null.");
            // Current canonical content = 16 weapons. >= 16 catches the JSON load/parse
            // chain silently emptying (the store-goes-blank regression) without forcing
            // a brittle exact count when content grows.
            Assert.That(weapons.Count, Is.GreaterThanOrEqualTo(16),
                "weapons.json must hydrate at least the 16 canonical weapons — an empty " +
                "catalog means the CanonicalJson load/parse chain broke (store goes blank).");
        }

        [Test]
        public void armor_json_loads_a_non_empty_catalog()
        {
            var armors = GearCatalog.AllArmors();
            Assert.That(armors, Is.Not.Null, "GearCatalog.AllArmors() must never be null.");
            // Current canonical content = 5 armors.
            Assert.That(armors.Count, Is.GreaterThanOrEqualTo(5),
                "armor.json must hydrate at least the 5 canonical armors — an empty " +
                "catalog means the CanonicalJson load/parse chain broke (store goes blank).");
        }

        [Test]
        public void every_weapon_has_an_id_and_a_positive_damage_multiplier()
        {
            foreach (var w in GearCatalog.AllWeapons())
            {
                Assert.That(w, Is.Not.Null, "no null weapon entry in the catalog.");
                Assert.That(string.IsNullOrEmpty(w.id), Is.False, "every weapon has an id.");
                Assert.That(w.damageMult, Is.GreaterThan(0f), $"{w.id} damageMult must be > 0.");
            }
        }

        [Test]
        public void every_armor_has_an_id_and_a_sane_defense_value()
        {
            foreach (var a in GearCatalog.AllArmors())
            {
                Assert.That(a, Is.Not.Null, "no null armor entry in the catalog.");
                Assert.That(string.IsNullOrEmpty(a.id), Is.False, "every armor has an id.");
                // defense is a 0..0.9 fractional damage reduction.
                Assert.That(a.defense, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(0.9f),
                    $"{a.id} defense must be a 0..0.9 fraction.");
            }
        }

        // verify-then-regress: gear must be PRICED, not all "Free" (the WO-406 content gap).
        // Catches a regression that strips the buy* costs back to 0 -> free shop.
        [Test]
        public void every_weapon_is_priced_not_free()
        {
            foreach (var w in GearCatalog.AllWeapons())
                Assert.That(w.buyWood + w.buyStone + w.buyIron + w.buyCrystals, Is.GreaterThan(0),
                    $"{w.id} must have a buy cost (regression to all-Free shop).");
        }

        [Test]
        public void every_armor_is_priced_not_free()
        {
            foreach (var a in GearCatalog.AllArmors())
                Assert.That(a.buyWood + a.buyStone + a.buyIron + a.buyCrystals, Is.GreaterThan(0),
                    $"{a.id} must have a buy cost (regression to all-Free shop).");
        }
    }
}
