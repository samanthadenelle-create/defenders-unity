// =============================================================================
// Core State — EditMode test support
// -----------------------------------------------------------------------------
// Shared helpers for the Core save/load + migration tests:
//   - SpawnService()       — builds a GameStateService MonoBehaviour with a
//                            fresh GameState SO and _loadOnAwake disabled, so a
//                            test can drive Load() / Reset() / Save() manually.
//   - ClearSave()          — wipes the PlayerPrefs keys the save layer touches.
//   - MakeRichState()      — a fully-populated PersistedState exercising every
//                            one of the 41 persisted fields (nested shapes,
//                            dictionaries, optional fields).
//   - WriteSaveFile()      — writes a SaveFile envelope straight to PlayerPrefs.
//
// GameStateService is a MonoBehaviour whose Awake() auto-loads. Tests need a
// deterministic instance, so SpawnService builds the GameObject, activates it,
// and only THEN injects the [SerializeField] private fields via reflection.
//
// Injection order matters. EditMode tests run in EDIT mode, where Unity never
// calls Awake/OnEnable for a normal MonoBehaviour — so there is no auto-load to
// gate. The real hazard is the inactive->active serialization sync: activating a
// GameObject deserializes the native (C++) serialized field data back onto the
// managed (C#) object, which OVERWRITES any managed-only writes (i.e. reflection
// assignments) made to [SerializeField] fields while the object was inactive.
// Injecting BEFORE activation therefore loses the writes — _state reverts to
// null and _loadOnAwake reverts to its `true` default. So SpawnService injects
// AFTER activation, when nothing further can clobber the managed fields.
// See docs/port-notes/core-test-fix.md for the full root-cause analysis.
// =============================================================================

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Core.Tests
{
    /// <summary>Shared fixtures + helpers for the Core save/load tests.</summary>
    public static class TestSupport
    {
        // ── PlayerPrefs isolation ────────────────────────────────────────────

        /// <summary>Deletes every PlayerPrefs key the save layer reads or writes.</summary>
        public static void ClearSave()
        {
            PlayerPrefs.DeleteKey(SaveSchema.PlayerPrefsKey);        // "dotr-save"
            PlayerPrefs.DeleteKey(SaveSchema.LegacySettingsKey);     // "realm-defenders-settings"
            PlayerPrefs.Save();
        }

        // ── Service construction ─────────────────────────────────────────────

        /// <summary>
        /// Builds a fresh <see cref="GameStateService"/> on its own GameObject
        /// with a brand-new <see cref="GameState"/> SO and <c>_loadOnAwake</c>
        /// disabled. The caller drives <c>Load()</c> / <c>Reset()</c> manually.
        /// </summary>
        public static GameStateService SpawnService(out GameState state)
        {
            // Test isolation: clear the static singleton so a prior test's
            // service cannot make this one's Awake guard bail early.
            ResetSingleton();

            var go = new GameObject("GameStateService (test)");
            var service = go.AddComponent<GameStateService>();
            state = ScriptableObject.CreateInstance<GameState>();

            // Inject the private [SerializeField] fields AFTER the GameObject is
            // active. Doing it before activation loses the writes: the
            // inactive->active serialization sync deserializes the native
            // defaults back over the managed object, nulling _state and resetting
            // _loadOnAwake. EditMode never runs Awake, so injecting last is safe.
            SetPrivateField(service, "_state", state);
            SetPrivateField(service, "_loadOnAwake", false);

            return service;
        }

        /// <summary>Destroys a service GameObject created by <see cref="SpawnService"/>.</summary>
        public static void DestroyService(GameStateService service)
        {
            if (service != null) Object.DestroyImmediate(service.gameObject);
            ResetSingleton();
        }

        /// <summary>Clears the GameStateService static singleton between tests.</summary>
        private static void ResetSingleton()
        {
            var f = typeof(GameStateService).GetField(
                "_instance", BindingFlags.Static | BindingFlags.NonPublic);
            f?.SetValue(null, null);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"expected private field '{name}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        /// <summary>
        /// A fully-populated <see cref="SaveSchema.PersistedState"/> that touches
        /// every one of the 41 persisted fields — every nested shape, every
        /// dictionary, and the optional fields. Used by the round-trip tests.
        /// </summary>
        public static SaveSchema.PersistedState MakeRichState()
        {
            return new SaveSchema.PersistedState
            {
                Pets = new List<PetData>
                {
                    new PetData
                    {
                        Id = "pet-flame",
                        OwnerId = "local-player",
                        Species = PetSpecies.FlamePup,
                        Nickname = "Cinder",
                        Level = 3,
                        Xp = 120,
                        UnlockedSkillIds = new List<string> { "skill-a", "skill-b" },
                        EquippedActiveIds = new List<string> { "skill-a" },
                    },
                    new PetData
                    {
                        Id = "pet-ice",
                        OwnerId = "local-player",
                        Species = PetSpecies.IceWolf,
                        Level = 1,
                        Xp = 5,
                        UnlockedSkillIds = new List<string>(),
                        EquippedActiveIds = new List<string>(),
                    },
                },
                StarterPetId = "pet-flame",
                Onboarded = true,
                BestWave = 7,
                Resources = new ResourceBalance(900, 320, 64),
                OwnedItemIds = new List<string> { "item-1", "item-2" },
                PetBonds = new List<double> { 4, 2, 1 },
                Voidshards = 11,
                Towers = new List<double> { 2, 0, 1, 3, 0, 0, 0, 0, 0 },
                TowerAbilities = new List<double> { 1, 0, 2, 0, 0, 0, 0, 0, 0 },
                WallLevel = 2,
                Stone = 88,
                Iron = 33,
                Wood = 47,
                BuildingCooldowns = new Dictionary<string, double>
                {
                    { "crystal-mine", 1716000000000.0 },
                    { "lumber-yard", 1716000300000.0 },
                },
                PendingBuilds = new List<PendingTowerBuild>
                {
                    new PendingTowerBuild { Slot = 0, Ability = 2, FinishAt = 1716000300000.0 },
                    new PendingTowerBuild { Slot = 4, Ability = 1, FinishAt = 1716000600000.0 },
                },
                TutorialStep = TutorialStep.Done,
                JoystickSensitivity = 1.2,
                MovementStyle = MovementStyle.Joystick,
                Muted = false,
                MusicVolume = 55,
                SfxVolume = 65,
                Difficulty = Difficulty.Hard,
                VoiceOvers = true,
                OwnedPets = new List<PetSpecies> { PetSpecies.AetherSprite, PetSpecies.FlamePup },
                SeenTutorials = new Dictionary<string, bool>
                {
                    { "firstVillageEntry", true },
                    { "firstDungeon", true },
                },
                BoundWallet = "BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV",
                HeroClass = State.HeroClass.Ranger,
                Inventory = new AtbInventory { Potions = 3, ManaCrystals = 2, Cleanses = 1, Torches = 4 },
                AtbLossStreak = 2,
                BreachStyle = BreachStyle.Atb,
                BuildingDamage = new Dictionary<string, double>
                {
                    { "gate-2", 40 },
                    { "heart", 0 },
                },
                Dungeons = new DungeonProgress
                {
                    Discovered = new Dictionary<string, bool> { { "healers_cottage", true } },
                    Cleared = new Dictionary<string, int> { { "healers_cottage", 2 } },
                    BestTime = new Dictionary<string, double> { { "healers_cottage", 184.5 } },
                    NoHitClear = new Dictionary<string, bool> { { "healers_cottage", true } },
                    DeathsByDungeon = new Dictionary<string, int> { { "healers_cottage", 1 } },
                    LoreReadByDungeon = new Dictionary<string, Dictionary<string, bool>>
                    {
                        { "healers_cottage", new Dictionary<string, bool> { { "lore-1", true } } },
                    },
                },
                ActiveDungeonRun = new ActiveDungeonRun
                {
                    DungeonId = "healers_cottage",
                    AvatarNodeId = "node-3",
                    VisitedNodes = new List<string> { "node-1", "node-2", "node-3" },
                    ClearedEncounters = new List<string> { "enc-1" },
                    OpenedChests = new List<string> { "chest-1" },
                    ReadLore = new List<string> { "lore-1" },
                    Loot = new LootStash
                    {
                        Crystals = 50, LegacyFood = 20, Coins = 10, Stone = 5, Iron = 2, Wood = 8,
                        PetBondShards = new Dictionary<string, double> { { "flame-pup", 3 } },
                        SkillPoints = new Dictionary<string, double> { { "skill-a", 1 } },
                    },
                    StartedAt = 1716000000000.0,
                },
                Quests = new QuestProgress
                {
                    Active = new Dictionary<string, QuestState>
                    {
                        {
                            "quest-1",
                            new QuestState
                            {
                                BeatIndex = 2,
                                Flags = new Dictionary<string, bool> { { "met-healer", true } },
                            }
                        },
                    },
                    Completed = new Dictionary<string, bool> { { "quest-0", true } },
                    Available = new Dictionary<string, bool> { { "quest-2", true } },
                },
                Regions = new RegionProgress
                {
                    Discovered = new Dictionary<string, bool> { { "region-1", true } },
                    Cleared = new Dictionary<string, bool> { { "region-1", true } },
                },
                MyInviteCode = "ABC123",
                Contacts = new List<ChatContact>
                {
                    new ChatContact { Code = "XYZ789", Nickname = "Ally" },
                    new ChatContact { Code = "QRS456" },
                },
                BlockedCodes = new List<string> { "BAD111" },
                Inbox = new List<ChatMessage>
                {
                    new ChatMessage
                    {
                        Id = "msg-1",
                        SenderCode = "XYZ789",
                        RecipientCode = "ABC123",
                        PhraseId = "phrase-7",
                        SentAt = 1716000000000.0,
                        ReadAt = 1716000050000.0,
                    },
                    new ChatMessage
                    {
                        Id = "msg-2",
                        SenderCode = "QRS456",
                        RecipientCode = "ABC123",
                        PhraseId = "phrase-2",
                        SentAt = 1716000100000.0,
                        ReadAt = null,
                    },
                },
                LastInboxSyncAt = 1716000200000.0,
            };
        }
    }
}
