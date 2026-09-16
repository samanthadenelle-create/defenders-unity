// =============================================================================
// Core State - SaveMigrator tests (EditMode)
// -----------------------------------------------------------------------------
// core-state-port.md §2.4: the ORIGINAL nine-step migration chain v1->v2 ... v9->v10.
// Every step is ADDITIVE - it seeds new fields with empty defaults and never
// mutates data a save already carries.
//
// One test per migration step verifies a save authored at version N migrates to
// CurrentVersion with the correct seeded defaults; cumulative tests confirm an
// ancient (v1) save runs every step in order. The version-gate tests verify
// MigrateForImport rejects a save newer than this build / a non-finite version.
//
// The chain has grown well past v10 (now v34). The LATER-STEP region below covers
// v14/17/18/21/22/23/24/25/26/27/28/29/30/31/32/33/34: the two DATA-MOVING steps
// (v8 gate-id rename + v18 crystal-fold) are asserted end-to-end, each additive
// seed is spot-checked (incl. the v34 tribes/wards/arena/petActiveSlots gap-closers),
// and a full v1->v34 smoke confirms an ancient save reaches the current shape without
// losing the data it carried.
// =============================================================================

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Core.Tests
{
    [TestFixture]
    public class SaveMigratorTest
    {
        [SetUp]
        public void SetUp()
        {
            TestSupport.ClearSave();
        }

        [TearDown]
        public void TearDown()
        {
            TestSupport.ClearSave();
        }

        // =====================================================================
        //  v1 -> v2 - seed resources + ownedItemIds
        // =====================================================================

        [Test]
        public void migrate_v1_to_v10_seeds_starter_resources_and_owned_items()
        {
            var s = new SaveSchema.PersistedState();
            var migrated = SaveMigrator.Migrate(s, 1);

            Assert.That(migrated.Resources.HasValue, Is.True, "v1->v2 seeds resources");
            Assert.That(migrated.Resources.Value.Crystals, Is.EqualTo(250));
            Assert.That(migrated.Resources.Value.Stone, Is.EqualTo(80));
            Assert.That(migrated.Resources.Value.Coins, Is.EqualTo(15));
            Assert.That(migrated.OwnedItemIds, Is.Not.Null.And.Empty,
                "v1->v2 seeds ownedItemIds = []");
        }

        // =====================================================================
        //  v2 -> v3 - seed heroClass = heroClass ?? Mage
        // =====================================================================

        [Test]
        public void migrate_v2_to_v10_defaults_missing_hero_class_to_mage()
        {
            var s = new SaveSchema.PersistedState(); // no heroClass
            var migrated = SaveMigrator.Migrate(s, 2);

            Assert.That(migrated.HeroClass.HasValue, Is.True, "v2->v3 seeds heroClass");
            Assert.That(migrated.HeroClass.Value, Is.EqualTo(HeroClass.Mage),
                "pre-hero-select saves default to Mage");
        }

        [Test]
        public void migrate_v2_to_v10_keeps_an_existing_hero_class()
        {
            var s = new SaveSchema.PersistedState { HeroClass = HeroClass.Ranger };
            var migrated = SaveMigrator.Migrate(s, 2);

            Assert.That(migrated.HeroClass.Value, Is.EqualTo(HeroClass.Ranger),
                "v2->v3 must NOT overwrite a hero class the save already carries.");
        }

        // =====================================================================
        //  v3 -> v4 - seed wood, buildingCooldowns, tutorialStep = done
        // =====================================================================

        [Test]
        public void migrate_v3_to_v10_seeds_wood_cooldowns_and_done_tutorial()
        {
            var s = new SaveSchema.PersistedState();
            var migrated = SaveMigrator.Migrate(s, 3);

            Assert.That(migrated.Wood.HasValue, Is.True, "v3->v4 seeds wood");
            Assert.That((int)migrated.Wood.Value, Is.EqualTo(15));
            Assert.That(migrated.BuildingCooldowns, Is.Not.Null.And.Empty,
                "v3->v4 seeds buildingCooldowns = {}");
            Assert.That(migrated.TutorialStep.HasValue, Is.True);
            Assert.That(migrated.TutorialStep.Value, Is.EqualTo(TutorialStep.Done),
                "an in-progress (v3) save skips the first-time tutorial");
        }

        // =====================================================================
        //  v4 -> v5 - seed towerAbilities = [0]x9
        // =====================================================================

        [Test]
        public void migrate_v4_to_v10_seeds_nine_zero_tower_abilities()
        {
            var s = new SaveSchema.PersistedState();
            var migrated = SaveMigrator.Migrate(s, 4);

            Assert.That(migrated.TowerAbilities, Is.Not.Null, "v4->v5 seeds towerAbilities");
            Assert.That(migrated.TowerAbilities.Count, Is.EqualTo(Constants.TowerSlots),
                "towerAbilities length = TOWER_SLOTS (9)");
            foreach (var v in migrated.TowerAbilities)
                Assert.That(v, Is.EqualTo(0.0), "every seeded tower ability is 0");
        }

        // =====================================================================
        //  v5 -> v6 - seed the whole ATB + dungeon block
        // =====================================================================

        [Test]
        public void migrate_v5_to_v10_seeds_the_atb_and_dungeon_block()
        {
            var s = new SaveSchema.PersistedState();
            var migrated = SaveMigrator.Migrate(s, 5);

            Assert.That(migrated.Inventory.HasValue, Is.True, "v5->v6 seeds inventory");
            Assert.That(migrated.Inventory.Value.Potions, Is.EqualTo(0));
            Assert.That(migrated.Inventory.Value.ManaCrystals, Is.EqualTo(0));
            Assert.That(migrated.Inventory.Value.Cleanses, Is.EqualTo(0));
            Assert.That(migrated.AtbLossStreak.HasValue, Is.True, "v5->v6 seeds atbLossStreak");
            Assert.That((int)migrated.AtbLossStreak.Value, Is.EqualTo(0));
            Assert.That(migrated.BreachStyle.HasValue, Is.True, "v5->v6 seeds breachStyle");
            Assert.That(migrated.BreachStyle.Value, Is.EqualTo(BreachStyle.Ask));
            Assert.That(migrated.BuildingDamage, Is.Not.Null.And.Empty,
                "v5->v6 seeds buildingDamage = {}");
            Assert.That(migrated.Dungeons, Is.Not.Null, "v5->v6 seeds dungeons");
            Assert.That(migrated.Quests, Is.Not.Null, "v5->v6 seeds quests");
            Assert.That(migrated.ActiveDungeonRun, Is.Null, "activeDungeonRun stays null");
        }

        // =====================================================================
        //  v6 -> v7 - merge the starter dungeon into dungeons.discovered
        // =====================================================================

        [Test]
        public void migrate_v6_to_v10_discovers_the_starter_dungeon()
        {
            var s = new SaveSchema.PersistedState
            {
                Dungeons = new DungeonProgress
                {
                    Discovered = new Dictionary<string, bool>(),
                    Cleared = new Dictionary<string, int>(),
                    BestTime = new Dictionary<string, double>(),
                    NoHitClear = new Dictionary<string, bool>(),
                },
            };
            var migrated = SaveMigrator.Migrate(s, 6);

            Assert.That(
                migrated.Dungeons.Discovered.ContainsKey(SaveSchema.StarterDungeonId), Is.True,
                "v6->v7 merges healers_cottage into dungeons.discovered");
            Assert.That(migrated.Dungeons.Discovered[SaveSchema.StarterDungeonId], Is.True);
        }

        [Test]
        public void migrate_v6_to_v10_preserves_existing_discovered_dungeons()
        {
            var s = new SaveSchema.PersistedState
            {
                Dungeons = new DungeonProgress
                {
                    Discovered = new Dictionary<string, bool> { { "crystal_caverns", true } },
                    Cleared = new Dictionary<string, int>(),
                    BestTime = new Dictionary<string, double>(),
                    NoHitClear = new Dictionary<string, bool>(),
                },
            };
            var migrated = SaveMigrator.Migrate(s, 6);

            Assert.That(migrated.Dungeons.Discovered.ContainsKey("crystal_caverns"), Is.True,
                "v6->v7 merge is non-destructive");
            Assert.That(
                migrated.Dungeons.Discovered.ContainsKey(SaveSchema.StarterDungeonId), Is.True);
        }

        // =====================================================================
        //  v7 -> v8 - gate-0 -> gate-2 rename
        // =====================================================================

        [Test]
        public void migrate_v7_to_v10_renames_gate0_damage_to_gate2()
        {
            var s = new SaveSchema.PersistedState
            {
                BuildingDamage = new Dictionary<string, double> { { "gate-0", 40 } },
            };
            var migrated = SaveMigrator.Migrate(s, 7);

            Assert.That(migrated.BuildingDamage.ContainsKey("gate-0"), Is.False,
                "v7->v8 deletes the orphan gate-0 key");
            Assert.That(migrated.BuildingDamage.ContainsKey("gate-2"), Is.True,
                "v7->v8 copies the value to gate-2");
            Assert.That(migrated.BuildingDamage["gate-2"], Is.EqualTo(40.0));
        }

        [Test]
        public void migrate_v7_to_v10_is_a_no_op_when_no_gate0_key_exists()
        {
            var s = new SaveSchema.PersistedState
            {
                BuildingDamage = new Dictionary<string, double> { { "heart", 12 } },
            };
            var migrated = SaveMigrator.Migrate(s, 7);

            Assert.That(migrated.BuildingDamage.ContainsKey("gate-2"), Is.False,
                "v7->v8 must not invent a gate-2 key when there was no gate-0");
            Assert.That(migrated.BuildingDamage["heart"], Is.EqualTo(12.0));
        }

        // =====================================================================
        //  v8 -> v9 - seed pendingBuilds + migrate legacy audio settings
        // =====================================================================

        [Test]
        public void migrate_v8_to_v10_seeds_pending_builds_and_audio_defaults()
        {
            // No legacy 'realm-defenders-settings' key - fall back to defaults.
            var s = new SaveSchema.PersistedState();
            var migrated = SaveMigrator.Migrate(s, 8);

            Assert.That(migrated.PendingBuilds, Is.Not.Null.And.Empty,
                "v8->v9 seeds pendingBuilds = []");
            Assert.That(migrated.Muted.HasValue, Is.True);
            Assert.That(migrated.Muted.Value, Is.False,
                "v8 players fall back to muted=false (only new players are muted-by-default)");
            Assert.That((int)migrated.MusicVolume.Value, Is.EqualTo(70), "music default 70");
            Assert.That((int)migrated.SfxVolume.Value, Is.EqualTo(80), "sfx default 80");
            Assert.That(migrated.Difficulty.Value, Is.EqualTo(Difficulty.Normal),
                "difficulty default normal");
            Assert.That(migrated.VoiceOvers.Value, Is.False, "voiceOvers default false");
        }

        [Test]
        public void migrate_v8_to_v10_reads_audio_prefs_from_the_legacy_store()
        {
            // Seed the legacy standalone settings store the v8->v9 step migrates.
            const string legacyJson =
                "{\"muted\":true,\"musicVolume\":33,\"sfxVolume\":44," +
                "\"difficulty\":\"hard\",\"voiceOvers\":true}";
            PlayerPrefs.SetString(SaveSchema.LegacySettingsKey, legacyJson);
            PlayerPrefs.Save();

            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 8);

            Assert.That(migrated.Muted.Value, Is.True, "v8->v9 reads muted from legacy store");
            Assert.That((int)migrated.MusicVolume.Value, Is.EqualTo(33));
            Assert.That((int)migrated.SfxVolume.Value, Is.EqualTo(44));
            Assert.That(migrated.Difficulty.Value, Is.EqualTo(Difficulty.Hard));
            Assert.That(migrated.VoiceOvers.Value, Is.True);

            Assert.That(PlayerPrefs.HasKey(SaveSchema.LegacySettingsKey), Is.False,
                "v8->v9 must DELETE the legacy 'realm-defenders-settings' key.");
        }

        [Test]
        public void migrate_v8_to_v10_prefers_state_value_over_legacy_store()
        {
            PlayerPrefs.SetString(SaveSchema.LegacySettingsKey,
                "{\"musicVolume\":10}");
            PlayerPrefs.Save();

            var s = new SaveSchema.PersistedState { MusicVolume = 90 };
            var migrated = SaveMigrator.Migrate(s, 8);

            Assert.That((int)migrated.MusicVolume.Value, Is.EqualTo(90),
                "state.<f> ?? legacy.<f> - the state value wins.");
        }

        // =====================================================================
        //  v9 -> v10 - seed the Realm Map region progress
        // =====================================================================

        [Test]
        public void migrate_v9_to_v10_seeds_empty_region_progress()
        {
            var s = new SaveSchema.PersistedState();
            var migrated = SaveMigrator.Migrate(s, 9);

            Assert.That(migrated.Regions, Is.Not.Null, "v9->v10 seeds regions");
            Assert.That(migrated.Regions.Discovered, Is.Not.Null.And.Empty,
                "regions.discovered = {}");
            Assert.That(migrated.Regions.Cleared, Is.Not.Null.And.Empty, "regions.cleared = {}");
        }

        // =====================================================================
        //  Later steps - v14..v33 (schema grew well past the original v10)
        // =====================================================================

        // -- v13 -> v14 - seed baseLayout = [] --------------------------------
        [Test]
        public void migrate_v13_to_current_seeds_an_empty_base_layout()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 13);
            Assert.That(migrated.BaseLayout, Is.Not.Null.And.Empty,
                "v13->v14 seeds baseLayout = []");
        }

        // -- v16 -> v17 - seed the default zone graph -------------------------
        [Test]
        public void migrate_v16_to_current_seeds_the_default_zone_graph()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 16);
            Assert.That(migrated.Zones, Is.Not.Null.And.Not.Empty,
                "v16->v17 seeds the default zone graph (a pre-v17 save had none)");
        }

        // -- v17 -> v18 - DATA MOVE: fold aetherCrystals into resources.crystals -
        [Test]
        public void migrate_v17_to_current_folds_aether_crystals_into_resources()
        {
            // A v17 save carrying a legacy orphan AetherCrystals balance + a resources wallet.
            var s = new SaveSchema.PersistedState
            {
                Resources = new ResourceBalance(100, 0, 0),
                AetherCrystals = 40,
            };
            var migrated = SaveMigrator.Migrate(s, 17);

            Assert.That(migrated.Resources.HasValue, Is.True, "resources survives the fold");
            Assert.That(migrated.Resources.Value.Crystals, Is.EqualTo(140),
                "v17->v18 ADDS the orphan aetherCrystals (40) onto resources.crystals (100)");
            Assert.That((int)migrated.AetherCrystals.Value, Is.EqualTo(0),
                "v17->v18 zeroes aetherCrystals after folding (single source of truth)");
        }

        [Test]
        public void migrate_v17_crystal_fold_is_a_noop_without_an_aether_balance()
        {
            var s = new SaveSchema.PersistedState { Resources = new ResourceBalance(77, 0, 0) };
            var migrated = SaveMigrator.Migrate(s, 17);

            Assert.That(migrated.Resources.Value.Crystals, Is.EqualTo(77),
                "no aetherCrystals balance -> resources.crystals is untouched");
            Assert.That((int)migrated.AetherCrystals.Value, Is.EqualTo(0),
                "aetherCrystals is still normalised to 0");
        }

        // -- DATA MOVE combined - v8 gate rename + v18 crystal-fold across the full chain -
        [Test]
        public void migrate_from_v7_applies_both_data_moving_steps()
        {
            // A single old (v7) save that will exercise BOTH data-moving steps as the
            // chain runs: the v7->v8 gate-0 -> gate-2 rename AND the v17->v18 crystal-fold.
            var s = new SaveSchema.PersistedState
            {
                BuildingDamage = new Dictionary<string, double> { { "gate-0", 55 } },
                Resources = new ResourceBalance(10, 0, 0),
                AetherCrystals = 5,
            };
            var migrated = SaveMigrator.Migrate(s, 7);

            // v8 gate rename
            Assert.That(migrated.BuildingDamage.ContainsKey("gate-0"), Is.False,
                "v7->v8 removes the orphan gate-0 key");
            Assert.That(migrated.BuildingDamage["gate-2"], Is.EqualTo(55.0),
                "v7->v8 moves the damage onto gate-2");
            // v18 crystal-fold
            Assert.That(migrated.Resources.Value.Crystals, Is.EqualTo(15),
                "v17->v18 folds aetherCrystals(5) into resources.crystals(10)");
            Assert.That((int)migrated.AetherCrystals.Value, Is.EqualTo(0),
                "v17->v18 zeroes aetherCrystals");
        }

        // -- v20 -> v21 - seed settlements = [] -------------------------------
        [Test]
        public void migrate_v20_to_current_seeds_empty_settlements()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 20);
            Assert.That(migrated.Settlements, Is.Not.Null.And.Empty,
                "v20->v21 seeds settlements = []");
        }

        // -- v21 -> v22 - seed an empty army ----------------------------------
        [Test]
        public void migrate_v21_to_current_seeds_an_empty_army()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 21);
            Assert.That(migrated.Army, Is.Not.Null, "v21->v22 seeds army");
            Assert.That(migrated.Army.Owned, Is.Not.Null.And.Empty, "a fresh army owns no troops");
        }

        // -- v22 -> v23 - seed empty buildingTiers ----------------------------
        [Test]
        public void migrate_v22_to_current_seeds_empty_building_tiers()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 22);
            Assert.That(migrated.BuildingTiers, Is.Not.Null.And.Empty,
                "v22->v23 seeds buildingTiers = {} (every building reads tier 0)");
        }

        // -- v23 -> v24 - seed empty ownedBuildingPerks -----------------------
        [Test]
        public void migrate_v23_to_current_seeds_empty_owned_perks()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 23);
            Assert.That(migrated.OwnedBuildingPerks, Is.Not.Null.And.Empty,
                "v23->v24 seeds ownedBuildingPerks = []");
        }

        // -- v24 -> v25 - seed the starter Echo workforce ---------------------
        [Test]
        public void migrate_v24_to_current_seeds_the_starter_echo_workforce()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 24);
            Assert.That((int)migrated.EchoCount.Value, Is.EqualTo(1),
                "v24->v25 seeds echoCount = 1 (the starter Echo)");
            Assert.That(migrated.SiloResources.Value, Is.EqualTo(0), "v24->v25 seeds an empty silo");
            Assert.That((int)migrated.WavesCompleted.Value, Is.EqualTo(0),
                "v24->v25 seeds wavesCompleted = 0");
        }

        // -- v25 -> v26 - seed empty accessory equip --------------------------
        [Test]
        public void migrate_v25_to_current_seeds_empty_accessory_equip()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 25);
            Assert.That(migrated.EquippedRingId, Is.EqualTo(""),
                "v25->v26 seeds equippedRingId = \"\" (nothing equipped)");
            Assert.That(migrated.EquippedAmuletId, Is.EqualTo(""),
                "v25->v26 seeds equippedAmuletId = \"\"");
        }

        // -- v27 -> v28 - seed Population growth -------------------------------
        [Test]
        public void migrate_v27_to_current_seeds_population_growth()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 27);
            Assert.That((int)migrated.PopulationXP.Value, Is.EqualTo(0), "v27->v28 seeds populationXp = 0");
            Assert.That((int)migrated.PopulationQuests.Value, Is.EqualTo(0), "populationQuests = 0");
            Assert.That((int)migrated.PopulationOutposts.Value, Is.EqualTo(0), "populationOutposts = 0");
            Assert.That((int)migrated.PopulationEchoSlots.Value, Is.EqualTo(1),
                "v27->v28 seeds populationEchoSlots = 1 (the starter Wood echo slot)");
        }

        // -- v28 -> v29 - seed a fresh hero level/XP --------------------------
        [Test]
        public void migrate_v28_to_current_seeds_a_fresh_hero_level()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 28);
            Assert.That((int)migrated.HeroLevel.Value, Is.EqualTo(1),
                "v28->v29 seeds heroLevel = 1 (a pre-v29 save never persisted the level)");
            Assert.That(migrated.HeroXp.Value, Is.EqualTo(0), "heroXp = 0");
            Assert.That(migrated.HeroLifetimeXp.Value, Is.EqualTo(0), "heroLifetimeXp = 0");
        }

        // -- v29 -> v30 - seed the strategic-placement marker false -----------
        [Test]
        public void migrate_v29_to_current_seeds_strategic_placement_marker_false()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 29);
            Assert.That(migrated.StrategicPlacementMigrated.HasValue, Is.True,
                "v29->v30 seeds strategicPlacementMigrated");
            Assert.That(migrated.StrategicPlacementMigrated.Value, Is.False,
                "a pre-v30 save has never run the one-shot bake->BaseLayout migration");
        }

        // -- v30 -> v31 - seed the starter echo lane --------------------------
        [Test]
        public void migrate_v30_to_current_seeds_the_wood_starter_echo_lane()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 30);
            Assert.That(migrated.EchoLanes, Is.EqualTo("wood"),
                "v30->v31 seeds echoLanes = \"wood\" (the prior hardwired starter-Echo behaviour)");
        }

        // -- v31 -> v32 - seed empty freeBuildsUsed ---------------------------
        [Test]
        public void migrate_v31_to_current_seeds_empty_free_builds()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 31);
            Assert.That(migrated.FreeBuildsUsed, Is.Not.Null.And.Empty,
                "v31->v32 seeds freeBuildsUsed = [] (a pre-v32 save has burned no freebies)");
        }

        // -- v32 -> v33 - echoLanes token grammar bump is a pass-through ------
        [Test]
        public void migrate_v32_to_current_preserves_existing_echo_lanes()
        {
            // v33 (WO-738) is a no-data-transform bump (read-migrated at parse time), so a
            // v32 save's echoLanes token must pass through the last step unchanged.
            var s = new SaveSchema.PersistedState { EchoLanes = "harvest:2,idle" };
            var migrated = SaveMigrator.Migrate(s, 32);
            Assert.That(migrated.EchoLanes, Is.EqualTo("harvest:2,idle"),
                "v32->v33 must not rewrite an existing echoLanes token");
        }

        // -- v33 -> v34 - seed world-content + pet-slot persistence (REDS #3/#4) --
        [Test]
        public void migrate_v33_to_current_seeds_empty_world_content_and_pet_slots()
        {
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 33);
            Assert.That(migrated.Tribes, Is.Not.Null.And.Empty,
                "v33->v34 seeds tribes = [] (a pre-v34 save never persisted tribe progress)");
            Assert.That(migrated.Wards, Is.Not.Null.And.Empty,
                "v33->v34 seeds wards = [] (a pre-v34 save never persisted relit wards)");
            Assert.That(migrated.Arena.HasValue, Is.True, "v33->v34 seeds an arena W/L record");
            Assert.That(migrated.Arena.Value.Wins, Is.EqualTo(0), "the seeded arena record is zeroed");
            Assert.That(migrated.Arena.Value.TotalPurse, Is.EqualTo(0L), "the seeded arena purse is 0");
            Assert.That(migrated.PetActiveSlots, Is.Not.Null.And.Empty,
                "v33->v34 seeds petActiveSlots = [] (the acquisition service then uses the legacy starter-slot-0 rebuild)");
        }

        [Test]
        public void migrate_v33_to_current_preserves_existing_world_content()
        {
            // A v33 save already carrying tribe/ward/arena data must keep it through v34
            // (the step is additive-only — seeds ONLY when null).
            var s = new SaveSchema.PersistedState
            {
                Tribes = new List<DeNelle.Core.World.TribeState>
                {
                    new DeNelle.Core.World.TribeState { Id = "ashwood-cult-1", MembersRemaining = 2, ClearCount = 1 },
                },
                Wards = new List<DeNelle.Core.World.WardStoneState>
                {
                    new DeNelle.Core.World.WardStoneState { Id = "ward_goldfields_1", Lit = true, ReachRadiusGranted = 40f },
                },
                Arena = new ArenaProgress { Wins = 3, Losses = 1, Streak = 2, TotalPurse = 500 },
                PetActiveSlots = new List<string> { "ice-wolf", null, "flame-pup" },
            };
            var migrated = SaveMigrator.Migrate(s, 33);
            Assert.That(migrated.Tribes.Count, Is.EqualTo(1), "v34 must not clobber carried tribes");
            Assert.That(migrated.Tribes[0].MembersRemaining, Is.EqualTo(2), "carried tribe members-remaining kept");
            Assert.That(migrated.Wards[0].Lit, Is.True, "carried lit ward kept");
            Assert.That(migrated.Arena.Value.Wins, Is.EqualTo(3), "carried arena wins kept");
            Assert.That(migrated.PetActiveSlots, Is.EqualTo(new List<string> { "ice-wolf", null, "flame-pup" }),
                "carried pet slot map kept (including the empty middle slot)");
        }

        // -- Full-chain smoke - an ancient v1 save reaches v34 without loss ---
        [Test]
        public void migrate_from_v1_reaches_current_with_every_later_field_seeded()
        {
            // Carry a distinctive value so we can prove the chain never clobbers it.
            var s = new SaveSchema.PersistedState { BestWave = 5 };
            var migrated = SaveMigrator.Migrate(s, 1);

            // Original v2..v10 steps
            Assert.That(migrated.Resources.HasValue, Is.True, "v2 seeded resources");
            Assert.That(migrated.HeroClass.Value, Is.EqualTo(HeroClass.Mage), "v3 seeded heroClass");
            Assert.That(migrated.Wood.HasValue, Is.True, "v4 seeded wood");
            Assert.That(migrated.TowerAbilities, Is.Not.Null, "v5 seeded towerAbilities");
            Assert.That(migrated.Inventory.HasValue, Is.True, "v6 seeded inventory");
            Assert.That(migrated.Dungeons.Discovered.ContainsKey(SaveSchema.StarterDungeonId),
                Is.True, "v7 discovered the starter dungeon");
            Assert.That(migrated.PendingBuilds, Is.Not.Null, "v9 seeded pendingBuilds");
            Assert.That(migrated.Regions, Is.Not.Null, "v10 seeded regions");

            // Later steps v14..v33
            Assert.That(migrated.BaseLayout, Is.Not.Null, "v14 seeded baseLayout");
            Assert.That(migrated.Zones, Is.Not.Null.And.Not.Empty, "v17 seeded the zone graph");
            Assert.That((int)migrated.AetherCrystals.Value, Is.EqualTo(0), "v18 normalised aetherCrystals");
            Assert.That(migrated.Settlements, Is.Not.Null, "v21 seeded settlements");
            Assert.That(migrated.Army, Is.Not.Null, "v22 seeded army");
            Assert.That(migrated.BuildingTiers, Is.Not.Null, "v23 seeded buildingTiers");
            Assert.That(migrated.OwnedBuildingPerks, Is.Not.Null, "v24 seeded ownedBuildingPerks");
            Assert.That((int)migrated.EchoCount.Value, Is.EqualTo(1), "v25 seeded echoCount = 1");
            Assert.That(migrated.EquippedRingId, Is.EqualTo(""), "v26 seeded equippedRingId");
            Assert.That((int)migrated.PopulationEchoSlots.Value, Is.EqualTo(1), "v28 seeded echo slots");
            Assert.That((int)migrated.HeroLevel.Value, Is.EqualTo(1), "v29 seeded heroLevel = 1");
            Assert.That(migrated.StrategicPlacementMigrated.Value, Is.False, "v30 seeded the marker false");
            Assert.That(migrated.EchoLanes, Is.EqualTo("wood"), "v31 seeded the wood echo lane");
            Assert.That(migrated.FreeBuildsUsed, Is.Not.Null.And.Empty, "v32 seeded freeBuildsUsed");
            Assert.That(migrated.Tribes, Is.Not.Null, "v34 seeded tribes");
            Assert.That(migrated.Wards, Is.Not.Null, "v34 seeded wards");
            Assert.That(migrated.Arena.HasValue, Is.True, "v34 seeded the arena record");
            Assert.That(migrated.PetActiveSlots, Is.Not.Null, "v34 seeded petActiveSlots");

            // No loss: the carried value survives every step.
            Assert.That((int)migrated.BestWave.Value, Is.EqualTo(5),
                "the full v1->v33 chain must preserve carried data (bestWave = 5)");
        }

        [Test]
        public void migrate_across_the_full_chain_never_clobbers_carried_data()
        {
            // A recent-ish save (v10) already carrying values every later step could touch -
            // none of the additive seeds may overwrite data the save already holds.
            var s = new SaveSchema.PersistedState
            {
                Resources = new ResourceBalance(1, 2, 3),
                Wood = 999,
                HeroClass = HeroClass.Knight,
                TutorialStep = TutorialStep.Step2,
                EchoLanes = "crafting:4",
            };
            var migrated = SaveMigrator.Migrate(s, 10);

            Assert.That(migrated.Resources.Value.Crystals, Is.EqualTo(1), "resources.crystals not clobbered");
            Assert.That(migrated.Resources.Value.Stone, Is.EqualTo(2), "resources.food not clobbered");
            Assert.That(migrated.Resources.Value.Coins, Is.EqualTo(3), "resources.coins not clobbered");
            Assert.That((int)migrated.Wood.Value, Is.EqualTo(999), "wood not clobbered");
            Assert.That(migrated.HeroClass.Value, Is.EqualTo(HeroClass.Knight), "heroClass not clobbered");
            Assert.That(migrated.TutorialStep.Value, Is.EqualTo(TutorialStep.Step2),
                "tutorialStep not clobbered (would skip the player past the FTUE)");
            Assert.That(migrated.EchoLanes, Is.EqualTo("crafting:4"),
                "an existing echoLanes assignment is not reset to the wood default");
        }

        // =====================================================================
        //  Cumulative + no-op behaviour
        // =====================================================================

        [Test]
        public void migrate_from_v1_runs_every_step_in_order()
        {
            // An ancient v1 save must end up fully shaped for v10.
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 1);

            Assert.That(migrated.Resources.HasValue, Is.True, "v2 step ran");
            Assert.That(migrated.HeroClass.HasValue, Is.True, "v3 step ran");
            Assert.That(migrated.Wood.HasValue, Is.True, "v4 step ran");
            Assert.That(migrated.TowerAbilities, Is.Not.Null, "v5 step ran");
            Assert.That(migrated.Inventory.HasValue, Is.True, "v6 step ran");
            Assert.That(migrated.Dungeons.Discovered.ContainsKey(SaveSchema.StarterDungeonId),
                Is.True, "v7 step ran");
            Assert.That(migrated.PendingBuilds, Is.Not.Null, "v9 step ran");
            Assert.That(migrated.Regions, Is.Not.Null, "v10 step ran");
        }

        [Test]
        public void migrate_at_current_version_is_a_no_op_passthrough()
        {
            var s = new SaveSchema.PersistedState { BestWave = 12 };
            var migrated = SaveMigrator.Migrate(s, SaveSchema.CurrentVersion);

            Assert.That(ReferenceEquals(migrated, s), Is.True,
                "a save already at CurrentVersion must pass straight through.");
            Assert.That((int)migrated.BestWave.Value, Is.EqualTo(12));
        }

        // =====================================================================
        //  Version gate - MigrateForImport
        // =====================================================================

        [Test]
        public void migrate_for_import_rejects_a_save_newer_than_this_build()
        {
            var s = new SaveSchema.PersistedState();
            var result = SaveMigrator.MigrateForImport(s, SaveSchema.CurrentVersion + 1);

            Assert.That(result.Ok, Is.False,
                "a save with storeVersion > CurrentVersion must be rejected.");
            Assert.That(result.Reason, Does.Contain("newer"));
        }

        [Test]
        public void migrate_for_import_rejects_a_non_finite_version()
        {
            var result = SaveMigrator.MigrateForImport(new SaveSchema.PersistedState(), double.NaN);
            Assert.That(result.Ok, Is.False, "a NaN storeVersion must be rejected.");
        }

        [Test]
        public void migrate_for_import_accepts_and_migrates_an_older_save()
        {
            var result = SaveMigrator.MigrateForImport(new SaveSchema.PersistedState(), 6);

            Assert.That(result.Ok, Is.True, "an older save must be accepted.");
            Assert.That(result.Data.Regions, Is.Not.Null,
                "an older save must come back fully migrated to v10.");
        }

        [Test]
        public void migrate_for_import_accepts_a_current_version_save_unchanged()
        {
            var s = new SaveSchema.PersistedState { BestWave = 3 };
            var result = SaveMigrator.MigrateForImport(s, SaveSchema.CurrentVersion);

            Assert.That(result.Ok, Is.True);
            Assert.That((int)result.Data.BestWave.Value, Is.EqualTo(3),
                "an equal-version save is a no-op pass-through.");
        }
    }
}
