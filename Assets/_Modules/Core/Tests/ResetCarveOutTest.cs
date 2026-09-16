// =============================================================================
// Core State — Reset() carve-out tests (EditMode)
// -----------------------------------------------------------------------------
// core-state-port.md §1.6: GameStateService.Reset() (the "New Game" action) wipes
// progression back to starter values but DELIBERATELY PRESERVES:
//   - boundWallet   (the save stays tagged to its wallet)
//   - breachStyle   (a player preference — survives New Game)
//   - myInviteCode / contacts / blockedCodes / inbox / lastInboxSyncAt
//                   (social identity — reset() never touches it)
//
// These tests mutate state, populate the preserved + wiped fields, call Reset(),
// and assert the carve-out: the preserved fields are untouched and every
// progression field is back at its starter value.
// =============================================================================

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DeNelle.Core.State;

namespace DeNelle.Core.Tests
{
    [TestFixture]
    public class ResetCarveOutTest
    {
        private GameStateService _service;
        private GameState _state;

        [SetUp]
        public void SetUp()
        {
            TestSupport.ClearSave();
            _service = TestSupport.SpawnService(out _state);
        }

        [TearDown]
        public void TearDown()
        {
            TestSupport.DestroyService(_service);
            _service = null;
            _state = null;
            TestSupport.ClearSave();
        }

        // =====================================================================
        //  Preserved fields — the carve-out
        // =====================================================================

        [Test]
        public void reset_preserves_the_bound_wallet()
        {
            _state.BoundWallet = "BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV";
            _service.ResetToNewGame();
            Assert.That(_state.BoundWallet,
                Is.EqualTo("BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV"),
                "Reset() must NOT unbind the wallet.");
        }

        [Test]
        public void reset_preserves_the_breach_style_preference()
        {
            _state.BreachStyle = BreachStyle.TowerSim;
            _service.ResetToNewGame();
            Assert.That(_state.BreachStyle, Is.EqualTo(BreachStyle.TowerSim),
                "Reset() must NOT clear the breachStyle preference.");
        }

        [Test]
        public void reset_preserves_every_social_field()
        {
            _state.MyInviteCode = "ABC123";
            _state.Contacts = new List<ChatContact>
            {
                new ChatContact { Code = "XYZ789", Nickname = "Ally" },
            };
            _state.BlockedCodes = new List<string> { "BAD111" };
            _state.Inbox = new List<ChatMessage>
            {
                new ChatMessage
                {
                    Id = "msg-1",
                    SenderCode = "XYZ789",
                    RecipientCode = "ABC123",
                    PhraseId = "phrase-1",
                    SentAt = 1716000000000.0,
                },
            };
            _state.LastInboxSyncAt = 1716000200000.0;

            _service.ResetToNewGame();

            Assert.That(_state.MyInviteCode, Is.EqualTo("ABC123"), "reset must keep myInviteCode");
            Assert.That(_state.Contacts.Count, Is.EqualTo(1), "reset must keep contacts");
            Assert.That(_state.Contacts[0].Code, Is.EqualTo("XYZ789"));
            Assert.That(_state.BlockedCodes, Is.EqualTo(new List<string> { "BAD111" }),
                "reset must keep blockedCodes");
            Assert.That(_state.Inbox.Count, Is.EqualTo(1), "reset must keep inbox");
            Assert.That(_state.Inbox[0].Id, Is.EqualTo("msg-1"));
            Assert.That(_state.LastInboxSyncAt, Is.EqualTo(1716000200000.0),
                "reset must keep lastInboxSyncAt");
        }

        // =====================================================================
        //  Wiped fields — progression returns to starter values
        // =====================================================================

        [Test]
        public void reset_wipes_progression_back_to_starter_values()
        {
            // Mutate every progression field away from its starter value.
            _state.Pets = new List<PetData> { new PetData { Id = "p1" } };
            _state.StarterPetId = "p1";
            _state.Onboarded = true;
            _state.BestWave = 42;
            _state.Resources = new ResourceBalance(9999, 9999, 9999);
            _state.OwnedItemIds = new List<string> { "item-1" };
            _state.PetBonds = new List<int> { 4, 4, 4 };
            _state.Voidshards = 99;
            _state.Towers = new List<int> { 3, 3, 3, 3, 3, 3, 3, 3, 3 };
            _state.TowerAbilities = new List<int> { 2, 2, 2, 2, 2, 2, 2, 2, 2 };
            _state.WallLevel = 3;
            _state.Iron = 500;
            _state.Wood = 500;
            _state.BuildingCooldowns["crystal-mine"] = 123;
            _state.PendingBuilds = new List<PendingTowerBuild>
            {
                new PendingTowerBuild { Slot = 1, Ability = 1, FinishAt = 1 },
            };
            _state.TutorialStep = TutorialStep.Done;
            _state.JoystickSensitivity = 1.5f;
            _state.SeenTutorials["firstVillageEntry"] = true;
            _state.HeroClass = HeroClassOpt.Ranger;
            _state.Inventory = new AtbInventory { Potions = 9, ManaCrystals = 9, Cleanses = 9 };
            _state.AtbLossStreak = 5;
            _state.BuildingDamage["gate-2"] = 80;
            _state.Dungeons.Cleared["healers_cottage"] = 9;
            _state.ActiveDungeonRun = new ActiveDungeonRun { DungeonId = "healers_cottage" };
            _state.Quests.Completed["quest-0"] = true;
            _state.Regions.Discovered["region-1"] = true;

            _service.ResetToNewGame();

            Assert.That(_state.Pets, Is.Empty, "pets wiped");
            Assert.That(_state.StarterPetId, Is.Null, "starterPetId wiped");
            Assert.That(_state.Onboarded, Is.False, "onboarded wiped");
            Assert.That(_state.BestWave, Is.EqualTo(0), "bestWave wiped");
            Assert.That(_state.Resources.Crystals, Is.EqualTo(250), "resources -> STARTER");
            Assert.That(_state.Resources.Stone, Is.EqualTo(80));
            Assert.That(_state.Resources.Coins, Is.EqualTo(StartingBudget.StrategicGold),
                "gold -> owner-ruled founding seed");
            Assert.That(_state.OwnedItemIds, Is.Empty, "ownedItemIds wiped");
            Assert.That(_state.PetBonds, Is.EqualTo(new List<int> { 0, 0, 0 }), "petBonds -> [0,0,0]");
            Assert.That(_state.Voidshards, Is.EqualTo(5), "voidshards -> 5");
            Assert.That(_state.Towers, Is.EqualTo(new List<int> { 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                "towers -> [0]x9");
            Assert.That(_state.TowerAbilities,
                Is.EqualTo(new List<int> { 0, 0, 0, 0, 0, 0, 0, 0, 0 }), "towerAbilities -> [0]x9");
            Assert.That(_state.WallLevel, Is.EqualTo(0), "wallLevel -> 0");
            // WO-1212: the retired GameState.Stone field is gone. The live Stone the player
            // sees is Resources.Stone, pinned at the STARTER 80 a few lines above - and it must
            // stay 80: the invisible seed was DISCARDED, never folded in.
            // WO-682: strategic placement is always on — New Game seeds the core-kit
            // budget (StartingBudget constants), not the legacy 5 iron / 15 wood.
            Assert.That(_state.Iron, Is.EqualTo(StartingBudget.StrategicIron), "iron -> core-kit seed");
            Assert.That(_state.Wood, Is.EqualTo(StartingBudget.StrategicWood), "wood -> core-kit seed");
            // WO-949: the founding kit grants starter healing potions in the persisted larder
            // (GearInventory IS VillageInventory's backing dict), keyed by the canonical belt
            // potion id — so a fresh hero can heal from turn one.
            Assert.That(_state.GearInventory, Is.Not.Null, "gearInventory seeded (WO-949)");
            Assert.That(_state.GearInventory.ContainsKey(DeNelle.Core.HUD.HudCommands.HpPotionId),
                Is.True, "founding potions present under the canonical belt id (WO-949)");
            Assert.That(_state.GearInventory[DeNelle.Core.HUD.HudCommands.HpPotionId],
                Is.EqualTo(StartingBudget.FoundingHealPotions),
                "founding potion count -> StartingBudget.FoundingHealPotions (WO-949)");
            Assert.That(_state.BuildingCooldowns, Is.Empty, "buildingCooldowns wiped");
            Assert.That(_state.PendingBuilds, Is.Empty, "pendingBuilds wiped");
            Assert.That(_state.TutorialStep, Is.EqualTo(TutorialStep.Step1), "tutorialStep -> Step1");
            Assert.That(_state.JoystickSensitivity, Is.EqualTo(1f).Within(1e-4),
                "joystickSensitivity -> 1");
            Assert.That(_state.SeenTutorials, Is.Empty, "seenTutorials wiped");
            Assert.That(_state.HeroClass, Is.EqualTo(HeroClassOpt.None), "heroClass wiped");
            Assert.That(_state.Inventory.Potions, Is.EqualTo(0), "inventory -> empty");
            Assert.That(_state.Inventory.ManaCrystals, Is.EqualTo(0));
            Assert.That(_state.Inventory.Cleanses, Is.EqualTo(0));
            Assert.That(_state.AtbLossStreak, Is.EqualTo(0), "atbLossStreak -> 0");
            Assert.That(_state.BuildingDamage, Is.Empty, "buildingDamage wiped");
            Assert.That(_state.ActiveDungeonRun, Is.Null, "activeDungeonRun -> null");
            Assert.That(_state.Quests.Completed, Is.Empty, "quests wiped");
            Assert.That(_state.Regions.Discovered, Is.Empty, "regions wiped");
        }

        // =====================================================================
        //  World state — the coverage hole that let two fields ship unwiped
        // =====================================================================
        //
        // AUDIT 2026-08-02. This fixture asserted ~15 fields and had ZERO coverage of
        // Zones or Settlements, which is exactly why both shipped broken:
        //   * Settlements was simply ABSENT from ResetToNewGame's body (Tribes and Wards,
        //     added in the same v34 batch, were there).
        //   * Zones was worse than absent: the reset called EnsureZoneGraph, a BACKFILL
        //     helper that early-returns once Zones is non-empty, so it could never reseed.
        // Net effect: "Start New" opened on the PREVIOUS save's explored/cleared realm,
        // still holding its claimed nodes and their 3-day razed lockouts.

        [Test]
        public void reset_reseeds_the_zone_graph_instead_of_inheriting_it()
        {
            // A "previous save" zone graph: fewer entries than the default AND carrying
            // progress flags. Both properties matter - a reset that merely topped the list
            // up to 5, or that kept the objects and their flags, would be the shipped bug.
            _state.Zones = new List<DeNelle.Core.World.ZoneState>
            {
                new DeNelle.Core.World.ZoneState { RegionKey = "not-a-real-zone", Discovered = true, Cleared = true },
            };

            _service.ResetToNewGame();

            var fresh = new List<DeNelle.Core.World.ZoneState>(DeNelle.Core.World.ZoneManager.DefaultZoneGraph());
            Assert.That(_state.Zones, Is.Not.Null, "reset must leave a zone graph, never null");
            Assert.That(_state.Zones.Count, Is.EqualTo(fresh.Count),
                "reset must RESEED the default zone graph, not backfill around the old save's zones");
            Assert.That(_state.Zones.Exists(z => z.RegionKey == "not-a-real-zone"), Is.False,
                "the previous save's zone survived New Game - the reset is still backfilling");
            Assert.That(_state.Zones.TrueForAll(z => !z.Discovered && !z.Cleared), Is.True,
                "a new game must start on an UNEXPLORED realm - discovery/clear flags were inherited");
        }

        [Test]
        public void reset_clears_claimed_and_razed_settlements()
        {
            _state.Settlements = new List<DeNelle.Core.World.SettlementState>
            {
                new DeNelle.Core.World.SettlementState { SiteId = "node-1" },
                // A RAZED site with a live lockout - the worst thing to inherit into a new game.
                new DeNelle.Core.World.SettlementState { SiteId = "node-2", RazedUntilDay = 9 },
            };

            _service.ResetToNewGame();

            Assert.That(_state.Settlements, Is.Not.Null, "settlements must be a list, never null");
            Assert.That(_state.Settlements, Is.Empty,
                "New Game inherited the previous save's node settlements - including any 3-day razed lockout");
        }

        [Test]
        public void reset_seeds_the_starter_dungeon_discovered()
        {
            _service.ResetToNewGame();
            // DungeonProgress.Empty() seeds healers_cottage so Dungeon Select has >=1 entry.
            Assert.That(_state.Dungeons.Discovered.ContainsKey(SaveSchema.StarterDungeonId), Is.True,
                "Reset() must leave the starter dungeon discovered.");
            Assert.That(_state.Dungeons.Discovered[SaveSchema.StarterDungeonId], Is.True);
        }

        [Test]
        public void reset_stamps_the_current_schema_version()
        {
            _state.SchemaVersion = 3;
            _service.ResetToNewGame();
            Assert.That(_state.SchemaVersion, Is.EqualTo(SaveSchema.CurrentVersion),
                "Reset() must stamp SchemaVersion to CurrentVersion.");
        }

        // =====================================================================
        //  WO-1220 — a New Game must reset the progression that is NOT in the save
        // =====================================================================
        //
        // Owner felt-test 2026-08-26 (Seeker 2026.08.26.341419): the town reset perfectly
        // and the hero did not. A brand-new RANGER came up at Lv 4 with a level-4 MAGE's
        // talent applied. Two stores caused it, and neither is a GameState field, which is
        // why every existing reset oracle was green:
        //   * WisdomCurrencyService's own PlayerPrefs blob (Wisdom + unlocked node ids),
        //   * the DontDestroyOnLoad runtime singletons holding the same values in memory.

        [Test]
        public void reset_clears_the_talent_store_so_a_new_class_inherits_no_talents()
        {
            // A level-4 Mage's tree, including a shared.* node — shared nodes carry no hero
            // prefix at all, which is precisely why the carryover crossed classes rather
            // than staying with the Mage that earned it.
            string key = GameStateService.TalentPrefKey;
            string restore = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            try
            {
                PlayerPrefs.SetString(key, "{\"Wisdom\":170,\"Unlocked\":[\"mage.n1\",\"shared.n5\"]}");
                PlayerPrefs.Save();

                _service.ResetToNewGame();

                Assert.That(PlayerPrefs.HasKey(key), Is.False,
                    "A New Game must erase the talent store. While it survives, the NEXT hero — of " +
                    "any class — starts with the previous hero's Wisdom and unlocked nodes applied.");
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                if (restore != null) PlayerPrefs.SetString(key, restore);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void reset_zeroes_the_hero_progression_fields()
        {
            _state.HeroLevel = 4;
            _state.HeroXp = 3531.9f;
            _state.HeroLifetimeXp = 7531.9f;

            _service.ResetToNewGame();

            Assert.That(_state.HeroLevel, Is.EqualTo(1), "A New Game starts at hero level 1.");
            Assert.That(_state.HeroXp, Is.EqualTo(0f), "A New Game starts with no banked XP.");
            Assert.That(_state.HeroLifetimeXp, Is.EqualTo(0f), "A New Game starts with no lifetime XP.");
        }

        [Test]
        public void reset_notifies_the_live_progression_singletons()
        {
            // The save half of the reset cannot reach HeroProgression / WisdomCurrencyService /
            // SkillSystem: they are DontDestroyOnLoad and outlive both the scene and the save
            // write. The event is the ONLY seam that does, so its absence is the whole defect.
            int fired = 0;
            Action handler = () => fired++;
            GameStateService.NewGameStarted += handler;
            try
            {
                _service.ResetToNewGame();
                Assert.That(fired, Is.EqualTo(1),
                    "ResetToNewGame must raise NewGameStarted exactly once so every live " +
                    "progression singleton drops the previous run's in-memory state.");
            }
            finally { GameStateService.NewGameStarted -= handler; }
        }

        [Test]
        public void reset_survives_a_throwing_new_game_subscriber()
        {
            // One bad subscriber must never abort the rest of the reset — the town clearing
            // and the hero not clearing is exactly the half-reset this ticket is about.
            Action thrower = () => throw new InvalidOperationException("WO-1220 fixture");
            int after = 0;
            Action counter = () => after++;
            GameStateService.NewGameStarted += thrower;
            GameStateService.NewGameStarted += counter;
            // The throw is REPORTED (FlowTrace.Fail -> Debug.LogError) on purpose — §12 forbids
            // a silent swallow — so the runner must be told this LogError is the expected
            // outcome rather than an unhandled one.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                _state.HeroLevel = 4;
                Assert.DoesNotThrow(() => _service.ResetToNewGame());
                Assert.That(after, Is.EqualTo(1), "a later subscriber must still be notified");
                Assert.That(_state.HeroLevel, Is.EqualTo(1), "the reset must still complete");
            }
            finally
            {
                GameStateService.NewGameStarted -= thrower;
                GameStateService.NewGameStarted -= counter;
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void reset_then_relaunch_keeps_the_preserved_fields_persisted()
        {
            // Reset writes the save — the preserved fields must survive a reload.
            // The wallet fixture must be a REAL base58 address (audit 2026-08-02): Save() ->
            // EnsureAccount -> RetireLegacyIdentity now retires any bound key that could never
            // authenticate against the backend (a Firebase UID, a debug string, the old
            // "WALLET-XYZ" placeholder), so a fake id here would be cleared and re-minted as a
            // guest key and this test would be asserting the OLD, broken behaviour.
            _state.BoundWallet = "BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV";
            _state.BreachStyle = BreachStyle.Atb;
            _state.MyInviteCode = "KEEP01";
            _state.Contacts = new List<ChatContact> { new ChatContact { Code = "FRIEND" } };
            _state.LastInboxSyncAt = 555;

            _service.ResetToNewGame(); // ResetToNewGame() also Save()s.

            TestSupport.DestroyService(_service);
            _service = TestSupport.SpawnService(out _state);
            Assert.That(_service.Load(), Is.True);

            Assert.That(_state.BoundWallet, Is.EqualTo("BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV"));
            Assert.That(_state.BreachStyle, Is.EqualTo(BreachStyle.Atb));
            Assert.That(_state.MyInviteCode, Is.EqualTo("KEEP01"));
            Assert.That(_state.Contacts.Count, Is.EqualTo(1));
            Assert.That(_state.Contacts[0].Code, Is.EqualTo("FRIEND"));
            Assert.That(_state.LastInboxSyncAt, Is.EqualTo(555));
        }
    }
}
