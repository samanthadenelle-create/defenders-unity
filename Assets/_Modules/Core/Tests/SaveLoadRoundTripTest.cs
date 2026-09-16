// =============================================================================
// Core State - Save/Load round-trip tests (EditMode)
// -----------------------------------------------------------------------------
// The spec's Week-1 acceptance criterion (core-state-port.md §6):
//   "launch → New Game → quit → relaunch → save restored".
//
// Each test mutates the live GameState SO, calls Save() (writes the SaveFile
// envelope to PlayerPrefs 'dotr-save'), then SIMULATES A RESTART - discards the
// in-memory SO + service, spawns a fresh service, and calls Load() to rehydrate
// from PlayerPrefs. All 41 persisted fields must come back byte-for-byte,
// including the nested shapes and the dictionaries.
// =============================================================================

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Core.Tests
{
    [TestFixture]
    public class SaveLoadRoundTripTest
    {
        private GameStateService _service;

        [SetUp]
        public void SetUp()
        {
            TestSupport.ClearSave();
        }

        [TearDown]
        public void TearDown()
        {
            TestSupport.DestroyService(_service);
            _service = null;
            TestSupport.ClearSave();
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>
        /// Simulates a quit + relaunch: destroys the current service and SO,
        /// spawns a fresh service, and Loads from PlayerPrefs.
        /// </summary>
        private GameState RestartAndLoad(out bool loaded)
        {
            TestSupport.DestroyService(_service);
            _service = TestSupport.SpawnService(out var fresh);
            loaded = _service.Load();
            return fresh;
        }

        // =====================================================================
        //  Round-trip - all 41 persisted fields
        // =====================================================================

        [Test]
        public void save_then_relaunch_restores_a_fully_populated_state()
        {
            // -- Author a rich save through the service's own Snapshot path --
            _service = TestSupport.SpawnService(out var state);
            ApplyRichStateOntoSO(state, TestSupport.MakeRichState());
            _service.Save();
            Assert.That(PlayerPrefs.HasKey(SaveSchema.PlayerPrefsKey), Is.True,
                "Save() must write the dotr-save PlayerPrefs key.");

            // -- Quit + relaunch ---------------------------------------------
            var reloaded = RestartAndLoad(out var loaded);
            Assert.That(loaded, Is.True, "Load() must report an existing save was restored.");

            var expected = TestSupport.MakeRichState();
            AssertAll41FieldsMatch(expected, reloaded);
        }

        [Test]
        public void load_reports_false_when_no_save_exists()
        {
            _service = TestSupport.SpawnService(out _);
            Assert.That(_service.Load(), Is.False,
                "A brand-new game (no PlayerPrefs key) must Load() to false.");
        }

        [Test]
        public void fresh_game_reset_then_relaunch_restores_starter_state()
        {
            // launch → New Game → quit → relaunch (the literal Week-1 path).
            _service = TestSupport.SpawnService(out _);
            _service.ResetToNewGame();

            var reloaded = RestartAndLoad(out var loaded);
            Assert.That(loaded, Is.True);
            Assert.That(reloaded.Onboarded, Is.False);
            Assert.That(reloaded.BestWave, Is.EqualTo(0));
            Assert.That(reloaded.Resources.Crystals, Is.EqualTo(250));
            Assert.That(reloaded.Resources.Stone, Is.EqualTo(80));
            Assert.That(reloaded.Resources.Coins, Is.EqualTo(StartingBudget.StrategicGold),
                "reset/relaunch preserves the owner-ruled founding gold seed");
            Assert.That(reloaded.Voidshards, Is.EqualTo(5));
            // WO-1212: GameState.Stone is retired. The live Stone slot (Resources.Stone = 80)
            // is asserted above and is deliberately UNCHANGED by the retirement.
            // WO-682: strategic placement is always on - New Game seeds the core-kit budget.
            Assert.That(reloaded.Iron, Is.EqualTo(StartingBudget.StrategicIron));
            Assert.That(reloaded.Wood, Is.EqualTo(StartingBudget.StrategicWood));
            Assert.That(reloaded.TutorialStep, Is.EqualTo(TutorialStep.Step1));
            Assert.That(reloaded.SchemaVersion, Is.EqualTo(SaveSchema.CurrentVersion));
        }

        [Test]
        public void mutators_persist_immediately_and_survive_a_relaunch()
        {
            _service = TestSupport.SpawnService(out _);
            _service.FinishOnboarding();
            _service.RecordRun(13);
            _service.ChooseHero(HeroClass.Knight);
            _service.SetMuted(false);
            _service.SetDifficulty(Difficulty.Hard);
            _service.AdvanceTutorial(); // Step1 -> Step2

            var reloaded = RestartAndLoad(out var loaded);
            Assert.That(loaded, Is.True);
            Assert.That(reloaded.Onboarded, Is.True);
            Assert.That(reloaded.BestWave, Is.EqualTo(13));
            Assert.That(reloaded.HeroClass, Is.EqualTo(HeroClassOpt.Knight));
            Assert.That(reloaded.Muted, Is.False);
            Assert.That(reloaded.Difficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(reloaded.TutorialStep, Is.EqualTo(TutorialStep.Step2));
        }

        [Test]
        public void tutorial_done_round_trips_as_the_string_done()
        {
            // TutorialStep.Done serializes as "done", not the int 99.
            _service = TestSupport.SpawnService(out var state);
            state.TutorialStep = TutorialStep.Done;
            _service.Save();

            var json = PlayerPrefs.GetString(SaveSchema.PlayerPrefsKey);
            Assert.That(json, Does.Contain("\"tutorialStep\":\"done\""),
                "Done must serialize as the literal string \"done\".");
            Assert.That(json, Does.Not.Contain("\"tutorialStep\":99"));

            var reloaded = RestartAndLoad(out _);
            Assert.That(reloaded.TutorialStep, Is.EqualTo(TutorialStep.Done));
        }

        [Test]
        public void numeric_tutorial_step_round_trips_as_a_raw_number()
        {
            _service = TestSupport.SpawnService(out var state);
            state.TutorialStep = TutorialStep.Step4;
            _service.Save();

            var json = PlayerPrefs.GetString(SaveSchema.PlayerPrefsKey);
            Assert.That(json, Does.Contain("\"tutorialStep\":4"),
                "Steps 1..7 must serialize as raw numbers.");

            var reloaded = RestartAndLoad(out _);
            Assert.That(reloaded.TutorialStep, Is.EqualTo(TutorialStep.Step4));
        }

        [Test]
        public void string_enums_round_trip_through_their_kebab_wire_values()
        {
            _service = TestSupport.SpawnService(out var state);
            state.MovementStyle = MovementStyle.Tap;
            state.BreachStyle = BreachStyle.TowerSim;
            state.Difficulty = Difficulty.Easy;
            _service.Save();

            var json = PlayerPrefs.GetString(SaveSchema.PlayerPrefsKey);
            Assert.That(json, Does.Contain("\"breachStyle\":\"tower-sim\""),
                "BreachStyle.TowerSim must serialize to the kebab string.");
            Assert.That(json, Does.Contain("\"movementStyle\":\"tap\""));
            Assert.That(json, Does.Contain("\"difficulty\":\"easy\""));

            var reloaded = RestartAndLoad(out _);
            Assert.That(reloaded.MovementStyle, Is.EqualTo(MovementStyle.Tap));
            Assert.That(reloaded.BreachStyle, Is.EqualTo(BreachStyle.TowerSim));
            Assert.That(reloaded.Difficulty, Is.EqualTo(Difficulty.Easy));
        }

        [Test]
        public void null_hero_class_round_trips_as_json_null()
        {
            _service = TestSupport.SpawnService(out var state);
            state.HeroClass = HeroClassOpt.None;
            _service.Save();

            var reloaded = RestartAndLoad(out _);
            Assert.That(reloaded.HeroClass, Is.EqualTo(HeroClassOpt.None),
                "An unset hero class must survive a round-trip as None.");
        }

        [Test]
        public void empty_save_envelope_keeps_fresh_defaults()
        {
            // A save whose 'state' payload is null must not crash Load().
            PlayerPrefs.SetString(SaveSchema.PlayerPrefsKey, "");
            PlayerPrefs.Save();

            _service = TestSupport.SpawnService(out var state);
            Assert.That(_service.Load(), Is.False);
            Assert.That(state.Voidshards, Is.EqualTo(5),
                "An empty save must leave the fresh SO defaults intact.");
        }

        // =====================================================================
        //  Round-trip - the NEWER persisted fields (schema v14..v33)
        // -----------------------------------------------------------------------------
        //  AssertAll41FieldsMatch (below) only covers the original v1-13 ~41-field
        //  table. Everything appended since (build mode, army, tiers, echo workforce,
        //  population, hero level/xp, party, zones, settlements, gear, freebies, the
        //  strategic-placement marker, the named pet) is exercised here: author a
        //  NON-DEFAULT value onto the SO, Save(), quit+relaunch, assert it came back.
        //  Authored straight onto the SO (like ApplyRichStateOntoSO) so it drives the
        //  service's own Snapshot() -> Save() -> Load() -> ApplyPersisted() path.
        //
        //  NOW PERSISTED as of save v34 (REDS #3/#4) - asserted below:
        //    - GameState.Tribes  (List<TribeState>)     - roaming-raider progress.
        //    - GameState.Wards   (List<WardStoneState>)  - relit wards + earned reach.
        //    - GameState.Arena   (ArenaProgress W/L ledger) - wins/losses/streak/purse
        //      (DISTINCT from ArenaDefense, the placed-defender layout, persisted v19).
        //    - GameState.PetActiveSlots (List<string>)   - the pet slot->species deploy
        //      map (flag_17); PetAcquisitionService rebuilds its runtime slots from it.
        //
        //  GENUINELY NOT PERSISTED (verified against Snapshot()/ApplyPersisted() - NOT
        //  asserted here, deliberately):
        //    - equippedRingId / equippedAmuletId - declared on SaveSchema.PersistedState
        //      and SEEDED by SaveMigrator v26, but there is NO matching GameState field
        //      and NO line in Snapshot()/ApplyPersisted(), so they do NOT round-trip
        //      through the live GameStateService. They survive a raw-PersistedState
        //      migrate (covered by CoreSaveRegression), never a SO save/load. Any
        //      accessory-equip persistence would need those two fields wired into the
        //      SO + Snapshot + ApplyPersisted first.
        // =====================================================================

        [Test]
        public void save_then_relaunch_restores_the_newer_persisted_fields()
        {
            _service = TestSupport.SpawnService(out var state);
            ApplyNewerFieldsOntoSO(state);
            _service.Save();

            var a = RestartAndLoad(out var loaded);
            Assert.That(loaded, Is.True, "Load() must restore the saved newer fields.");

            // -- Pets - the player-named starter (Snapshot: PetName) ----------
            Assert.That(a.PetName, Is.EqualTo("Sparky"), "petName");

            // -- ATB / gear (v20) ---------------------------------------------
            Assert.That(a.GearInventory, Is.Not.Null, "gearInventory not null");
            Assert.That(a.GearInventory.Count, Is.EqualTo(2), "gearInventory count");
            Assert.That(a.GearInventory["sword"], Is.EqualTo(2), "gearInventory[sword]");
            Assert.That(a.GearInventory["shield"], Is.EqualTo(1), "gearInventory[shield]");

            // -- Magic tech-axis (v15) ----------------------------------------
            Assert.That(a.Magic, Is.EqualTo(25), "magic");

            // -- Base layout (v14/v27) ----------------------------------------
            Assert.That(a.BaseLayout, Is.Not.Null, "baseLayout not null");
            Assert.That(a.BaseLayout.Count, Is.EqualTo(1), "baseLayout count");
            var bl = a.BaseLayout[0];
            Assert.That(bl.itemId, Is.EqualTo("forge"), "baseLayout[0].itemId");
            Assert.That(bl.cellX, Is.EqualTo(3), "baseLayout[0].cellX");
            Assert.That(bl.cellZ, Is.EqualTo(4), "baseLayout[0].cellZ");
            Assert.That(bl.yawSteps, Is.EqualTo(2), "baseLayout[0].yawSteps");
            Assert.That(bl.level, Is.EqualTo(1), "baseLayout[0].level");
            Assert.That(bl.worldY, Is.EqualTo(5f).Within(1e-3), "baseLayout[0].worldY (v27)");
            Assert.That(bl.wallMounted, Is.True, "baseLayout[0].wallMounted (v27)");

            // -- Party roster (v16) -------------------------------------------
            Assert.That(a.PartyMemberIds, Is.EqualTo(new List<string> { "Ranger", "Cleric" }),
                "partyMemberIds");

            // -- Zones (v17) --------------------------------------------------
            Assert.That(a.Zones, Is.Not.Null, "zones not null");
            Assert.That(a.Zones.Count, Is.EqualTo(1), "zones count (not backfilled over a saved list)");
            Assert.That(a.Zones[0].RegionKey, Is.EqualTo("Ashwood"), "zones[0].regionKey");
            Assert.That(a.Zones[0].Discovered, Is.True, "zones[0].discovered");
            Assert.That(a.Zones[0].Cleared, Is.True, "zones[0].cleared");
            Assert.That(a.Zones[0].Destination, Is.EqualTo(DeNelle.Core.World.NodeType.Horde),
                "zones[0].destination");
            Assert.That(a.Zones[0].Neighbors, Is.EqualTo(new List<string> { "Mirewood" }),
                "zones[0].neighbors");

            // -- Settlements (v21) --------------------------------------------
            Assert.That(a.Settlements, Is.Not.Null, "settlements not null");
            Assert.That(a.Settlements.Count, Is.EqualTo(1), "settlements count");
            var st = a.Settlements[0];
            Assert.That(st.SiteId, Is.EqualTo("site-1"), "settlements[0].siteId");
            Assert.That(st.RegionKey, Is.EqualTo("Goldfields"), "settlements[0].regionKey");
            Assert.That(st.Phase, Is.EqualTo(DeNelle.Core.World.SettlementPhase.Outpost),
                "settlements[0].phase");
            Assert.That(st.Hp, Is.EqualTo(150f).Within(1e-3), "settlements[0].hp");
            Assert.That(st.MaxHp, Is.EqualTo(200f).Within(1e-3), "settlements[0].maxHp");
            Assert.That(st.RazedUntilDay, Is.EqualTo(5), "settlements[0].razedUntilDay");

            // -- Army roster (v22) --------------------------------------------
            Assert.That(a.Army, Is.Not.Null, "army not null");
            Assert.That(a.Army.Owned.Count, Is.EqualTo(1), "army.owned count");
            Assert.That(a.Army.NextId, Is.EqualTo(2), "army.nextId");
            var troop = a.Army.Owned[0];
            Assert.That(troop.Id, Is.EqualTo("troop-1"), "army.owned[0].id");
            Assert.That(troop.TroopDefId, Is.EqualTo("troop-footman"), "army.owned[0].troopDefId");
            Assert.That(troop.VeterancyRank, Is.EqualTo(2), "army.owned[0].veterancyRank");
            Assert.That(troop.Wounded, Is.True, "army.owned[0].wounded");
            Assert.That(troop.RecoveryRemaining, Is.EqualTo(30f).Within(1e-3),
                "army.owned[0].recoveryRemaining");

            // -- Building tiers / village tier / perks (v23/v24) --------------
            Assert.That(a.BuildingTiers, Is.Not.Null, "buildingTiers not null");
            Assert.That(a.BuildingTiers["armorer"], Is.EqualTo(2), "buildingTiers[armorer]");
            Assert.That(a.BuildingTiers["lumbermill"], Is.EqualTo(3), "buildingTiers[lumbermill]");
            Assert.That(a.VillageTier, Is.EqualTo(4), "villageTier");
            Assert.That(a.OwnedBuildingPerks, Is.EqualTo(new List<string> { "forge:forge-damage-2" }),
                "ownedBuildingPerks");

            // -- Echo workforce (v25) -----------------------------------------
            Assert.That(a.EchoCount, Is.EqualTo(3), "echoCount");
            Assert.That(a.SiloResources, Is.EqualTo(12.5).Within(1e-6), "siloResources");
            Assert.That(a.WavesCompleted, Is.EqualTo(17), "wavesCompleted");

            // -- Population growth (v28) --------------------------------------
            Assert.That(a.PopulationXP, Is.EqualTo(500), "populationXp");
            Assert.That(a.PopulationQuests, Is.EqualTo(7), "populationQuests");
            Assert.That(a.PopulationOutposts, Is.EqualTo(3), "populationOutposts");
            Assert.That(a.PopulationEchoSlots, Is.EqualTo(4), "populationEchoSlots");

            // -- Hero level / XP (v29) ----------------------------------------
            Assert.That(a.HeroLevel, Is.EqualTo(9), "heroLevel");
            Assert.That(a.HeroXp, Is.EqualTo(42.5f).Within(1e-3), "heroXp");
            Assert.That(a.HeroLifetimeXp, Is.EqualTo(1234.5f).Within(1e-3), "heroLifetimeXp");

            // -- Strategic-placement marker (v30) -----------------------------
            Assert.That(a.StrategicPlacementMigrated, Is.True, "strategicPlacementMigrated");

            // -- Echo lanes (v31/v33 richer token) ----------------------------
            Assert.That(a.EchoLanes, Is.EqualTo("harvest:3,idle,crafting:1"), "echoLanes");

            // -- First-build freebies (v32) -----------------------------------
            Assert.That(a.FreeBuildsUsed, Is.EqualTo(new List<string> { "forge", "armorer" }),
                "freeBuildsUsed");

            // -- Tribes (v34, REDS #4) ----------------------------------------
            Assert.That(a.Tribes, Is.Not.Null, "tribes not null");
            Assert.That(a.Tribes.Count, Is.EqualTo(1), "tribes count");
            Assert.That(a.Tribes[0].Id, Is.EqualTo("ashwood-cult-1"), "tribes[0].id");
            Assert.That(a.Tribes[0].MembersRemaining, Is.EqualTo(2), "tribes[0].membersRemaining");
            Assert.That(a.Tribes[0].Cleared, Is.True, "tribes[0].cleared");
            Assert.That(a.Tribes[0].ClearCount, Is.EqualTo(1), "tribes[0].clearCount");

            // -- Wards (v34, REDS #4) -----------------------------------------
            Assert.That(a.Wards, Is.Not.Null, "wards not null");
            Assert.That(a.Wards.Count, Is.EqualTo(1), "wards count");
            Assert.That(a.Wards[0].Id, Is.EqualTo("ward_goldfields_1"), "wards[0].id");
            Assert.That(a.Wards[0].Lit, Is.True, "wards[0].lit");
            Assert.That(a.Wards[0].ReachRadiusGranted, Is.EqualTo(40f).Within(1e-3), "wards[0].reach");

            // -- Arena W/L ledger (v34, REDS #4) ------------------------------
            Assert.That(a.Arena.Wins, Is.EqualTo(3), "arena.wins");
            Assert.That(a.Arena.Losses, Is.EqualTo(1), "arena.losses");
            Assert.That(a.Arena.Streak, Is.EqualTo(2), "arena.streak");
            Assert.That(a.Arena.TotalPurse, Is.EqualTo(500L), "arena.totalPurse");

            // -- Pet active-slot deploy map (v34, REDS #3, flag_17) -----------
            Assert.That(a.PetActiveSlots, Is.EqualTo(new List<string> { "ice-wolf", null, "flame-pup" }),
                "petActiveSlots (incl. the empty middle slot)");
        }

        /// <summary>
        /// Authors a NON-DEFAULT value onto every persisted field added after the
        /// original v1-13 table (schema v14..v33), so a Save()/Load() round-trip proves
        /// each survives. Values are chosen to differ from BOTH the fresh GameState
        /// defaults and the ResetToNewGame seeds, so an unwired field is caught.
        /// </summary>
        private static void ApplyNewerFieldsOntoSO(GameState s)
        {
            s.PetName = "Sparky";
            s.GearInventory = new Dictionary<string, int> { { "sword", 2 }, { "shield", 1 } };
            s.Magic = 25;
            s.BaseLayout = new List<PlacedStructureData>
            {
                new PlacedStructureData("forge", 3, 4, 2, 1, yawOffset: 0f, worldY: 5f, wallMounted: true),
            };
            s.PartyMemberIds = new List<string> { "Ranger", "Cleric" };
            s.Zones = new List<DeNelle.Core.World.ZoneState>
            {
                new DeNelle.Core.World.ZoneState(
                    DeNelle.Core.World.RegionId.Ashwood,
                    DeNelle.Core.World.NodeType.Horde,
                    DeNelle.Core.World.RegionId.Mirewood)
                {
                    Discovered = true,
                    Cleared = true,
                },
            };
            s.Settlements = new List<DeNelle.Core.World.SettlementState>
            {
                new DeNelle.Core.World.SettlementState(
                    "site-1",
                    DeNelle.Core.World.RegionId.Goldfields,
                    new DeNelle.Core.World.WorldPoint(1f, 2f, 3f),
                    200f)
                {
                    Phase = DeNelle.Core.World.SettlementPhase.Outpost,
                    Hp = 150f,
                    RazedUntilDay = 5,
                },
            };
            s.Army = new ArmyStorage
            {
                Owned = new List<PlayerTroop>
                {
                    new PlayerTroop("troop-1", "troop-footman")
                    {
                        VeterancyRank = 2,
                        Wounded = true,
                        RecoveryRemaining = 30f,
                    },
                },
                NextId = 2,
            };
            s.BuildingTiers = new Dictionary<string, int> { { "armorer", 2 }, { "lumbermill", 3 } };
            s.VillageTier = 4;
            s.OwnedBuildingPerks = new List<string> { "forge:forge-damage-2" };
            s.EchoCount = 3;
            s.SiloResources = 12.5;
            s.WavesCompleted = 17;
            s.PopulationXP = 500;
            s.PopulationQuests = 7;
            s.PopulationOutposts = 3;
            s.PopulationEchoSlots = 4;
            s.HeroLevel = 9;
            s.HeroXp = 42.5f;
            s.HeroLifetimeXp = 1234.5f;
            s.StrategicPlacementMigrated = true;
            s.EchoLanes = "harvest:3,idle,crafting:1";
            s.FreeBuildsUsed = new List<string> { "forge", "armorer" };
            s.Tribes = new List<DeNelle.Core.World.TribeState>
            {
                new DeNelle.Core.World.TribeState
                {
                    Id = "ashwood-cult-1",
                    RegionKey = "Ashwood",
                    MembersRemaining = 2,
                    Cleared = true,
                    ClearCount = 1,
                },
            };
            s.Wards = new List<DeNelle.Core.World.WardStoneState>
            {
                new DeNelle.Core.World.WardStoneState
                {
                    Id = "ward_goldfields_1",
                    RegionKey = "Goldfields",
                    ReachRadiusGranted = 40f,
                    Lit = true,
                },
            };
            s.Arena = new ArenaProgress { Wins = 3, Losses = 1, Streak = 2, TotalPurse = 500 };
            s.PetActiveSlots = new List<string> { "ice-wolf", null, "flame-pup" };
        }

        // =====================================================================
        //  Field-by-field assertions
        // =====================================================================

        /// <summary>
        /// Verifies every one of the 41 persisted fields survived the round-trip.
        /// Grouped to mirror core-state-port.md §1.3's numbered table.
        /// </summary>
        private static void AssertAll41FieldsMatch(SaveSchema.PersistedState e, GameState a)
        {
            // #1 pets
            Assert.That(a.Pets.Count, Is.EqualTo(e.Pets.Count), "#1 pets count");
            for (var i = 0; i < e.Pets.Count; i++)
            {
                var ep = e.Pets[i];
                var ap = a.Pets[i];
                Assert.That(ap.Id, Is.EqualTo(ep.Id), $"#1 pets[{i}].id");
                Assert.That(ap.OwnerId, Is.EqualTo(ep.OwnerId), $"#1 pets[{i}].ownerId");
                Assert.That(ap.Species, Is.EqualTo(ep.Species), $"#1 pets[{i}].species");
                Assert.That(ap.Nickname, Is.EqualTo(ep.Nickname), $"#1 pets[{i}].nickname");
                Assert.That(ap.Level, Is.EqualTo(ep.Level), $"#1 pets[{i}].level");
                Assert.That(ap.Xp, Is.EqualTo(ep.Xp), $"#1 pets[{i}].xp");
                Assert.That(ap.UnlockedSkillIds, Is.EqualTo(ep.UnlockedSkillIds),
                    $"#1 pets[{i}].unlockedSkillIds");
                Assert.That(ap.EquippedActiveIds, Is.EqualTo(ep.EquippedActiveIds),
                    $"#1 pets[{i}].equippedActiveIds");
            }

            // #2..#4 player
            Assert.That(a.StarterPetId, Is.EqualTo(e.StarterPetId), "#2 starterPetId");
            Assert.That(a.Onboarded, Is.EqualTo(e.Onboarded.Value), "#3 onboarded");
            Assert.That(a.BestWave, Is.EqualTo((int)e.BestWave.Value), "#4 bestWave");

            // #5 resources
            Assert.That(a.Resources.Crystals, Is.EqualTo(e.Resources.Value.Crystals),
                "#5 resources.crystals");
            Assert.That(a.Resources.Stone, Is.EqualTo(e.Resources.Value.Stone), "#5 resources.food");
            Assert.That(a.Resources.Coins, Is.EqualTo(e.Resources.Value.Coins), "#5 resources.coins");

            // #6..#8
            Assert.That(a.OwnedItemIds, Is.EqualTo(e.OwnedItemIds), "#6 ownedItemIds");
            Assert.That(a.PetBonds, Is.EqualTo(new List<int> { 4, 2, 1 }), "#7 petBonds");
            Assert.That(a.Voidshards, Is.EqualTo((int)e.Voidshards.Value), "#8 voidshards");

            // #9..#11 village
            Assert.That(a.Towers, Is.EqualTo(new List<int> { 2, 0, 1, 3, 0, 0, 0, 0, 0 }), "#9 towers");
            Assert.That(a.TowerAbilities, Is.EqualTo(new List<int> { 1, 0, 2, 0, 0, 0, 0, 0, 0 }),
                "#10 towerAbilities");
            Assert.That(a.WallLevel, Is.EqualTo((int)e.WallLevel.Value), "#11 wallLevel");

            // #12..#14
            // WO-1212: #12 `stone` is a wire-only legacy key now - there is no GameState field
            // to compare it against. Round-tripping of the live Stone slot is #5 resources.food.
            Assert.That(a.Iron, Is.EqualTo((int)e.Iron.Value), "#13 iron");
            Assert.That(a.Wood, Is.EqualTo((int)e.Wood.Value), "#14 wood");

            // #15 buildingCooldowns (dictionary)
            AssertDictEqual(e.BuildingCooldowns, a.BuildingCooldowns, "#15 buildingCooldowns");

            // #16 pendingBuilds
            Assert.That(a.PendingBuilds.Count, Is.EqualTo(e.PendingBuilds.Count),
                "#16 pendingBuilds count");
            for (var i = 0; i < e.PendingBuilds.Count; i++)
            {
                Assert.That(a.PendingBuilds[i].Slot, Is.EqualTo(e.PendingBuilds[i].Slot),
                    $"#16 pendingBuilds[{i}].slot");
                Assert.That(a.PendingBuilds[i].Ability, Is.EqualTo(e.PendingBuilds[i].Ability),
                    $"#16 pendingBuilds[{i}].ability");
                Assert.That(a.PendingBuilds[i].FinishAt, Is.EqualTo(e.PendingBuilds[i].FinishAt),
                    $"#16 pendingBuilds[{i}].finishAt");
            }

            // #17 tutorialStep
            Assert.That(a.TutorialStep, Is.EqualTo(e.TutorialStep.Value), "#17 tutorialStep");

            // #18..#24 settings
            Assert.That(a.JoystickSensitivity,
                Is.EqualTo((float)e.JoystickSensitivity.Value).Within(1e-4),
                "#18 joystickSensitivity");
            Assert.That(a.MovementStyle, Is.EqualTo(e.MovementStyle.Value), "#19 movementStyle");
            Assert.That(a.Muted, Is.EqualTo(e.Muted.Value), "#20 muted");
            Assert.That(a.MusicVolume, Is.EqualTo((float)e.MusicVolume.Value).Within(1e-4),
                "#21 musicVolume");
            Assert.That(a.SfxVolume, Is.EqualTo((float)e.SfxVolume.Value).Within(1e-4),
                "#22 sfxVolume");
            Assert.That(a.Difficulty, Is.EqualTo(e.Difficulty.Value), "#23 difficulty");
            Assert.That(a.VoiceOvers, Is.EqualTo(e.VoiceOvers.Value), "#24 voiceOvers");

            // #25 ownedPets
            Assert.That(a.OwnedPets, Is.EqualTo(e.OwnedPets), "#25 ownedPets");

            // #26 seenTutorials (dictionary)
            AssertDictEqual(e.SeenTutorials, a.SeenTutorials, "#26 seenTutorials");

            // #27..#28
            Assert.That(a.BoundWallet, Is.EqualTo(e.BoundWallet), "#27 boundWallet");
            Assert.That(a.HeroClass, Is.EqualTo(HeroClassOpt.Ranger), "#28 heroClass");

            // #29 inventory
            Assert.That(a.Inventory.Potions, Is.EqualTo(e.Inventory.Value.Potions),
                "#29 inventory.potions");
            Assert.That(a.Inventory.ManaCrystals, Is.EqualTo(e.Inventory.Value.ManaCrystals),
                "#29 inventory.manaCrystals");
            Assert.That(a.Inventory.Cleanses, Is.EqualTo(e.Inventory.Value.Cleanses),
                "#29 inventory.cleanses");
            Assert.That(a.Inventory.Torches, Is.EqualTo(e.Inventory.Value.Torches),
                "#29 inventory.torches (optional)");

            // #30..#31
            Assert.That(a.AtbLossStreak, Is.EqualTo((int)e.AtbLossStreak.Value), "#30 atbLossStreak");
            Assert.That(a.BreachStyle, Is.EqualTo(e.BreachStyle.Value), "#31 breachStyle");

            // #32 buildingDamage (dictionary)
            AssertDictEqual(e.BuildingDamage, a.BuildingDamage, "#32 buildingDamage");

            // #33 dungeons (nested ledger)
            AssertDictEqual(e.Dungeons.Discovered, a.Dungeons.Discovered, "#33 dungeons.discovered");
            AssertDictEqual(e.Dungeons.Cleared, a.Dungeons.Cleared, "#33 dungeons.cleared");
            AssertDictEqual(e.Dungeons.BestTime, a.Dungeons.BestTime, "#33 dungeons.bestTime");
            AssertDictEqual(e.Dungeons.NoHitClear, a.Dungeons.NoHitClear, "#33 dungeons.noHitClear");
            AssertDictEqual(e.Dungeons.DeathsByDungeon, a.Dungeons.DeathsByDungeon,
                "#33 dungeons.deathsByDungeon (optional)");
            Assert.That(a.Dungeons.LoreReadByDungeon, Is.Not.Null,
                "#33 dungeons.loreReadByDungeon (optional)");
            Assert.That(a.Dungeons.LoreReadByDungeon["healers_cottage"]["lore-1"], Is.True,
                "#33 dungeons.loreReadByDungeon nested value");

            // #34 activeDungeonRun (nested)
            Assert.That(a.ActiveDungeonRun, Is.Not.Null, "#34 activeDungeonRun");
            Assert.That(a.ActiveDungeonRun.DungeonId, Is.EqualTo(e.ActiveDungeonRun.DungeonId),
                "#34 activeDungeonRun.dungeonId");
            Assert.That(a.ActiveDungeonRun.AvatarNodeId, Is.EqualTo(e.ActiveDungeonRun.AvatarNodeId),
                "#34 activeDungeonRun.avatarNodeId");
            Assert.That(a.ActiveDungeonRun.VisitedNodes, Is.EqualTo(e.ActiveDungeonRun.VisitedNodes),
                "#34 activeDungeonRun.visitedNodes");
            Assert.That(a.ActiveDungeonRun.ClearedEncounters,
                Is.EqualTo(e.ActiveDungeonRun.ClearedEncounters),
                "#34 activeDungeonRun.clearedEncounters");
            Assert.That(a.ActiveDungeonRun.OpenedChests, Is.EqualTo(e.ActiveDungeonRun.OpenedChests),
                "#34 activeDungeonRun.openedChests");
            Assert.That(a.ActiveDungeonRun.ReadLore, Is.EqualTo(e.ActiveDungeonRun.ReadLore),
                "#34 activeDungeonRun.readLore");
            Assert.That(a.ActiveDungeonRun.StartedAt, Is.EqualTo(e.ActiveDungeonRun.StartedAt),
                "#34 activeDungeonRun.startedAt");
            Assert.That(a.ActiveDungeonRun.Loot.Crystals, Is.EqualTo(e.ActiveDungeonRun.Loot.Crystals),
                "#34 activeDungeonRun.loot.crystals");
            Assert.That(a.ActiveDungeonRun.Loot.Wood, Is.EqualTo(e.ActiveDungeonRun.Loot.Wood),
                "#34 activeDungeonRun.loot.wood");
            AssertDictEqual(e.ActiveDungeonRun.Loot.PetBondShards,
                a.ActiveDungeonRun.Loot.PetBondShards, "#34 activeDungeonRun.loot.petBondShards");
            AssertDictEqual(e.ActiveDungeonRun.Loot.SkillPoints,
                a.ActiveDungeonRun.Loot.SkillPoints, "#34 activeDungeonRun.loot.skillPoints");

            // #35 quests (nested ledger)
            AssertDictEqual(e.Quests.Completed, a.Quests.Completed, "#35 quests.completed");
            AssertDictEqual(e.Quests.Available, a.Quests.Available, "#35 quests.available");
            Assert.That(a.Quests.Active.ContainsKey("quest-1"), Is.True, "#35 quests.active key");
            Assert.That(a.Quests.Active["quest-1"].BeatIndex, Is.EqualTo(2),
                "#35 quests.active.quest-1.beatIndex");
            Assert.That(a.Quests.Active["quest-1"].Flags["met-healer"], Is.True,
                "#35 quests.active.quest-1.flags");

            // #36 regions
            AssertDictEqual(e.Regions.Discovered, a.Regions.Discovered, "#36 regions.discovered");
            AssertDictEqual(e.Regions.Cleared, a.Regions.Cleared, "#36 regions.cleared");

            // #37 myInviteCode
            Assert.That(a.MyInviteCode, Is.EqualTo(e.MyInviteCode), "#37 myInviteCode");

            // #38 contacts
            Assert.That(a.Contacts.Count, Is.EqualTo(e.Contacts.Count), "#38 contacts count");
            for (var i = 0; i < e.Contacts.Count; i++)
            {
                Assert.That(a.Contacts[i].Code, Is.EqualTo(e.Contacts[i].Code),
                    $"#38 contacts[{i}].code");
                Assert.That(a.Contacts[i].Nickname, Is.EqualTo(e.Contacts[i].Nickname),
                    $"#38 contacts[{i}].nickname");
            }

            // #38a blockedCodes
            Assert.That(a.BlockedCodes, Is.EqualTo(e.BlockedCodes), "#38a blockedCodes");

            // #38b inbox
            Assert.That(a.Inbox.Count, Is.EqualTo(e.Inbox.Count), "#38b inbox count");
            for (var i = 0; i < e.Inbox.Count; i++)
            {
                Assert.That(a.Inbox[i].Id, Is.EqualTo(e.Inbox[i].Id), $"#38b inbox[{i}].id");
                Assert.That(a.Inbox[i].SenderCode, Is.EqualTo(e.Inbox[i].SenderCode),
                    $"#38b inbox[{i}].senderCode");
                Assert.That(a.Inbox[i].RecipientCode, Is.EqualTo(e.Inbox[i].RecipientCode),
                    $"#38b inbox[{i}].recipientCode");
                Assert.That(a.Inbox[i].PhraseId, Is.EqualTo(e.Inbox[i].PhraseId),
                    $"#38b inbox[{i}].phraseId");
                Assert.That(a.Inbox[i].SentAt, Is.EqualTo(e.Inbox[i].SentAt),
                    $"#38b inbox[{i}].sentAt");
                Assert.That(a.Inbox[i].ReadAt, Is.EqualTo(e.Inbox[i].ReadAt),
                    $"#38b inbox[{i}].readAt");
            }

            // #38c lastInboxSyncAt
            Assert.That(a.LastInboxSyncAt, Is.EqualTo(e.LastInboxSyncAt), "#38c lastInboxSyncAt");

            // SchemaVersion is stamped current after a load.
            Assert.That(a.SchemaVersion, Is.EqualTo(SaveSchema.CurrentVersion), "schemaVersion");
        }

        private static void AssertDictEqual<TV>(
            IDictionary<string, TV> expected, IDictionary<string, TV> actual, string label)
        {
            Assert.That(actual, Is.Not.Null, $"{label} - dictionary must not be null");
            Assert.That(actual.Count, Is.EqualTo(expected.Count), $"{label} - count");
            foreach (var kv in expected)
            {
                Assert.That(actual.ContainsKey(kv.Key), Is.True, $"{label} - missing key '{kv.Key}'");
                Assert.That(actual[kv.Key], Is.EqualTo(kv.Value), $"{label}['{kv.Key}']");
            }
        }

        /// <summary>
        /// Copies a <see cref="SaveSchema.PersistedState"/> onto the live SO so a
        /// test can author a rich save through the service's own Save() path.
        /// </summary>
        private static void ApplyRichStateOntoSO(GameState s, SaveSchema.PersistedState p)
        {
            s.Pets = p.Pets;
            s.StarterPetId = p.StarterPetId;
            s.Onboarded = p.Onboarded.Value;
            s.BestWave = (int)p.BestWave.Value;
            s.Resources = p.Resources.Value;
            s.OwnedItemIds = p.OwnedItemIds;
            s.PetBonds = new List<int> { 4, 2, 1 };
            s.Voidshards = (int)p.Voidshards.Value;
            s.Towers = new List<int> { 2, 0, 1, 3, 0, 0, 0, 0, 0 };
            s.TowerAbilities = new List<int> { 1, 0, 2, 0, 0, 0, 0, 0, 0 };
            s.WallLevel = (int)p.WallLevel.Value;
            // WO-1212: no GameState.Stone to hydrate - the wire key is an inbound alias only.
            s.Iron = (int)p.Iron.Value;
            s.Wood = (int)p.Wood.Value;
            s.BuildingCooldowns = new SerializableDict<string, double>(p.BuildingCooldowns);
            s.PendingBuilds = p.PendingBuilds;
            s.TutorialStep = p.TutorialStep.Value;
            s.JoystickSensitivity = (float)p.JoystickSensitivity.Value;
            s.MovementStyle = p.MovementStyle.Value;
            s.Muted = p.Muted.Value;
            s.MusicVolume = (float)p.MusicVolume.Value;
            s.SfxVolume = (float)p.SfxVolume.Value;
            s.Difficulty = p.Difficulty.Value;
            s.VoiceOvers = p.VoiceOvers.Value;
            s.OwnedPets = p.OwnedPets;
            s.SeenTutorials = new SerializableDict<string, bool>(p.SeenTutorials);
            s.BoundWallet = p.BoundWallet;
            s.HeroClass = p.HeroClass.ToOpt();
            s.Inventory = p.Inventory.Value;
            s.AtbLossStreak = (int)p.AtbLossStreak.Value;
            s.BreachStyle = p.BreachStyle.Value;
            s.BuildingDamage = new SerializableDict<string, double>(p.BuildingDamage);
            s.Dungeons = p.Dungeons;
            s.ActiveDungeonRun = p.ActiveDungeonRun;
            s.Quests = p.Quests;
            s.Regions = p.Regions;
            s.MyInviteCode = p.MyInviteCode;
            s.Contacts = p.Contacts;
            s.BlockedCodes = p.BlockedCodes;
            s.Inbox = p.Inbox;
            s.LastInboxSyncAt = p.LastInboxSyncAt.Value;
        }
    }
}
