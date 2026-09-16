// =============================================================================
// NestedTypes — the nested persisted data shapes (spec §1.4)
// -----------------------------------------------------------------------------
// Plain [Serializable] data containers — the C# port of the nested objects the
// persisted GameState carries: PetData, ResourceBalance, PendingTowerBuild,
// AtbInventory, ChatContact, ChatMessage, DungeonProgress, QuestProgress /
// QuestState, ActiveDungeonRun, LootStash, RegionProgress.
//
// They are serialized by Newtonsoft for the save file (handles Dictionary<,>
// natively) and are marked [Serializable] so the live GameState SO survives an
// editor domain reload. Newtonsoft maps C# PascalCase members to the React
// camelCase JSON keys via [JsonProperty]. Enum members serialize via
// StringEnumConverter (registered globally in SaveSchema.JsonSettings).
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DeNelle.Core.State
{
    /// <summary>A single owned pet — mirrors the <c>Pet</c> interface in gameDesign.ts.</summary>
    [Serializable]
    public sealed class PetData
    {
        [JsonProperty("id")] public string Id;
        /// <summary>Always "local-player" in the no-auth client.</summary>
        [JsonProperty("ownerId")] public string OwnerId = "local-player";
        [JsonProperty("species")] public PetSpecies Species;
        /// <summary>Optional player-given nickname.</summary>
        [JsonProperty("nickname", NullValueHandling = NullValueHandling.Ignore)]
        public string Nickname;
        [JsonProperty("level")] public int Level;
        [JsonProperty("xp")] public int Xp;
        [JsonProperty("unlockedSkillIds")] public List<string> UnlockedSkillIds = new List<string>();
        [JsonProperty("equippedActiveIds")] public List<string> EquippedActiveIds = new List<string>();
    }

    /// <summary>Player wallet — crystals / food / coins (resourcesSlice).</summary>
    [Serializable]
    public struct ResourceBalance
    {
        [JsonProperty("crystals")] public int Crystals;
        // Save migration slice 1: Stone is the runtime authority. Keep the shipped
        // JSON key until the backend/old-client protocol migration is coordinated.
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("food")] public int Stone;

        [JsonProperty("coins")] public int Coins;

        public ResourceBalance(int crystals, int stone, int coins)
        {
            Crystals = crystals;
            Stone = stone;
            Coins = coins;
        }

        /// <summary>STARTER_RESOURCES — { crystals:250, food:80, coins:15 }.</summary>
        public static ResourceBalance Starter => new ResourceBalance(250, 80, 15);

        /// <summary>ZERO_RESOURCES — an empty wallet.</summary>
        public static ResourceBalance Zero => new ResourceBalance(0, 0, 0);
    }

    /// <summary>
    /// Strategic-placement NEW-GAME BUDGET — ZERO (owner ruling 2026-07-13 evening):
    /// the per-id FREE-FIRST-BUILD flags (<c>GameState.FreeBuildsUsed</c>, save schema
    /// v32) REPLACE the resource seed entirely. The FIRST placement of each catalog
    /// building id costs nothing (the whole cost, crystals included); the flag burns
    /// at the committed placement and never resets. Players earn everything beyond
    /// that one-free-each kit from production — "we make them earn what they have",
    /// and the shape prevents building all defense then having no town structures.
    /// Wood/Iron are GameState scalar fields (not part of <see cref="ResourceBalance"/>),
    /// so the seed lives here as the ONE authoritative constant pair;
    /// <c>GameStateService.ResetToNewGame</c> applies it unconditionally.
    /// (Superseded, same day: the WO-707 650w/385i "one-of-each + 3 containers"
    /// arithmetic; earlier the WO-673 "core kit + one leftover" 260w/210i seed.)
    /// </summary>
    public static class StartingBudget
    {
        /// <summary>New-game Wood seed — 0; the free-first-build flags replace it (see class remarks).</summary>
        public const int StrategicWood = 0;
        /// <summary>New-game Iron seed — 0; the free-first-build flags replace it (see class remarks).</summary>
        public const int StrategicIron = 0;

        /// <summary>
        /// WO-1217 Slice B — NEW-GAME GOLD SEED, 200 (owner ruling 2026-08-26, verbatim:
        /// "so start gold at 200"). Gold is <c>GameState.Resources.Coins</c> (the shop/sell/
        /// research wallet), NOT a GameState scalar like Wood/Iron — so
        /// <c>GameStateService.ResetToNewGame</c> applies this AFTER seeding
        /// <c>Resources = ResourceBalance.Starter</c>, overriding that struct's coins:15.
        /// The seed lives HERE, beside the wood/iron pair, so the founding budget stays ONE
        /// authoritative place and no literal 200 is scattered into ResetToNewGame.
        ///
        /// ⛔ THIS IS NOT A REVERSAL OF THE 2026-07-13 ZERO-SEED RULING. That ruling zeroed
        /// WOOD and IRON specifically, and replaced them with the per-id free-first-build
        /// flags, to prevent all-defense-no-town. Gold buys none of the town's structures
        /// (regular baskets are wood+iron, magical are crystals+iron — WO-947), so a gold
        /// seed cannot re-open that failure mode. <c>StrategicWood</c> and
        /// <c>StrategicIron</c> above MUST STAY 0.
        ///
        /// ⚠ CORRECTION TO WO-1217's OWN SLICE B TEXT, recorded so it is not re-derived:
        /// the ticket states "Gold is never seeded at all, so it starts at 0". It was
        /// actually seeded at 15, via ResourceBalance.Starter (crystals 250 / food 80 /
        /// coins 15). The ruling (200) is applied regardless; only the stated baseline was
        /// wrong. ResourceBalance.Starter is deliberately LEFT ALONE — it is also the
        /// SaveMigrator's default for a resource-less legacy save, so editing its coins
        /// would silently grant 200 gold to migrating saves too, which nobody ruled.
        /// </summary>
        public const int StrategicGold = 200;

        /// <summary>
        /// WO-949 (owner F8 2026-08-10 "Can we start the user with some potions"): the founding
        /// grant of Minor Healing Draughts, seeded into <c>GameState.GearInventory</c> (the
        /// persisted VillageInventory larder) by <c>GameStateService.ResetToNewGame</c> under the
        /// canonical <c>HudCommands.HpPotionId</c> key. OWNER-TUNABLE here, the same one-constant
        /// pattern as the wood/iron pair above (the founding kit is code-constant, not data-driven).
        /// Founding-only: existing saves gain nothing (no read-migration grant).
        /// </summary>
        public const int FoundingHealPotions = 3;

        /// <summary>
        /// WO-1235 (owner 2026-08-26, verbatim: "can we start with 3 mana potions and let the
        /// users get a crafting scroll like the defensive plans"): the founding grant of Mana
        /// Draughts, seeded into <c>GameState.GearInventory</c> beside the healing draughts by
        /// the SAME dictionary in <c>GameStateService.ResetToNewGame</c>, under the canonical
        /// <c>HudCommands.ManaPotionId</c> key.
        ///
        /// ⛔ THESE POTIONS ARE NOT A GIFT, THEY ARE THE SETUP. WO-1235's loop is
        /// have -> use -> RUN OUT -> find the recipe -> craft more, and the scroll's trigger is
        /// the player's mana stack falling to 0 or 1 for the first time. Raising this number
        /// delays the teaching moment; lowering it to 0 destroys it, because the player never
        /// feels the resource before the recipe is offered. Retune only with that in mind.
        ///
        /// Founding-only, exactly like the heal grant above: existing saves gain nothing (no
        /// read-migration grant). Their access to the RECIPE is a separate concern and is
        /// handled by SaveMigrator.MigrateToV40, not here.
        /// </summary>
        public const int FoundingManaPotions = 3;
    }

    /// <summary>An in-flight pet-assisted tower build (villageSlice PendingTowerBuild).</summary>
    [Serializable]
    public struct PendingTowerBuild
    {
        [JsonProperty("slot")] public int Slot;
        [JsonProperty("ability")] public int Ability;
        /// <summary>ms timestamp the build finishes.</summary>
        [JsonProperty("finishAt")] public double FinishAt;
    }

    /// <summary>Shared consumable inventory carried into an ATB battle (combatSlice).</summary>
    [Serializable]
    public struct AtbInventory
    {
        [JsonProperty("potions")] public int Potions;
        [JsonProperty("manaCrystals")] public int ManaCrystals;
        [JsonProperty("cleanses")] public int Cleanses;
        /// <summary>
        /// Dungeon lantern-mechanic consumable. OPTIONAL — a save written before
        /// the lantern feature omits the key; readers default it to 0. Added
        /// without a save-schema bump (rides inside the existing inventory object).
        /// </summary>
        [JsonProperty("torches", NullValueHandling = NullValueHandling.Ignore)]
        public int Torches;

        /// <summary>Empty inventory — { potions:0, manaCrystals:0, cleanses:0 }.</summary>
        public static AtbInventory Empty => new AtbInventory();
    }

    /// <summary>A locally-saved chat contact (socialSlice / services/chat).</summary>
    [Serializable]
    public sealed class ChatContact
    {
        [JsonProperty("code")] public string Code;
        [JsonProperty("nickname", NullValueHandling = NullValueHandling.Ignore)]
        public string Nickname;
    }

    /// <summary>A cached inbox message (socialSlice / services/chat).</summary>
    [Serializable]
    public sealed class ChatMessage
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("senderCode")] public string SenderCode;
        [JsonProperty("recipientCode")] public string RecipientCode;
        [JsonProperty("phraseId")] public string PhraseId;
        [JsonProperty("sentAt")] public double SentAt;
        /// <summary>Unix ms read time, or null when unread.</summary>
        [JsonProperty("readAt")] public double? ReadAt;
    }

    /// <summary>A resolved dungeon loot stash (mirrors LootStash in types/loot.ts).</summary>
    [Serializable]
    public sealed class LootStash
    {
        [JsonProperty("crystals")] public int Crystals;
        // Historical dungeon payload only; keep separate from its existing Stone value.
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("food")] public int LegacyFood;
        [JsonProperty("coins")] public int Coins;
        [JsonProperty("stone")] public int Stone;
        [JsonProperty("iron")] public int Iron;
        [JsonProperty("wood")] public int Wood;
        [JsonProperty("petBondShards")] public Dictionary<string, double> PetBondShards = new Dictionary<string, double>();
        [JsonProperty("skillPoints")] public Dictionary<string, double> SkillPoints = new Dictionary<string, double>();
    }

    /// <summary>A mid-run dungeon save — resumes where the player left off (dungeonsSlice).</summary>
    [Serializable]
    public sealed class ActiveDungeonRun
    {
        [JsonProperty("dungeonId")] public string DungeonId;
        [JsonProperty("avatarNodeId")] public string AvatarNodeId;
        [JsonProperty("visitedNodes")] public List<string> VisitedNodes = new List<string>();
        [JsonProperty("clearedEncounters")] public List<string> ClearedEncounters = new List<string>();
        [JsonProperty("openedChests")] public List<string> OpenedChests = new List<string>();
        [JsonProperty("readLore")] public List<string> ReadLore = new List<string>();
        [JsonProperty("loot")] public LootStash Loot = new LootStash();
        /// <summary>ms timestamp the run started.</summary>
        [JsonProperty("startedAt")] public double StartedAt;
    }

    /// <summary>
    /// The seed-and-stone clay jar planted at the village Farm — Alduin's boss
    /// reward (COTT-6). OPTIONAL: absent on a save written before COTT-6.
    /// </summary>
    [Serializable]
    public sealed class SeedTree
    {
        [JsonProperty("plantedAtWave")] public int PlantedAtWave;
    }

    /// <summary>The persisted dungeon-progress ledger (dungeonsSlice DungeonProgress).</summary>
    [Serializable]
    public sealed class DungeonProgress
    {
        /// <summary>dungeonId → true once discovered.</summary>
        [JsonProperty("discovered")] public Dictionary<string, bool> Discovered = new Dictionary<string, bool>();
        /// <summary>dungeonId → number of times cleared.</summary>
        [JsonProperty("cleared")] public Dictionary<string, int> Cleared = new Dictionary<string, int>();
        /// <summary>dungeonId → fastest clear in seconds.</summary>
        [JsonProperty("bestTime")] public Dictionary<string, double> BestTime = new Dictionary<string, double>();
        /// <summary>dungeonId → true once cleared without taking a hit.</summary>
        [JsonProperty("noHitClear")] public Dictionary<string, bool> NoHitClear = new Dictionary<string, bool>();
        /// <summary>
        /// dungeonId → Keeper-defeat count. OPTIONAL — added without a schema bump;
        /// a save without it null-coalesces to {}.
        /// </summary>
        [JsonProperty("deathsByDungeon", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, int> DeathsByDungeon;
        /// <summary>
        /// dungeonId → { loreId → true } 3D lore-stone read ledger. OPTIONAL —
        /// added without a schema bump.
        /// </summary>
        [JsonProperty("loreReadByDungeon", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, bool>> LoreReadByDungeon;
        /// <summary>Boss-reward seed jar (COTT-6). OPTIONAL — absent before COTT-6.</summary>
        [JsonProperty("seedTree", NullValueHandling = NullValueHandling.Ignore)]
        public SeedTree SeedTree;

        /// <summary>
        /// Fresh dungeon progress — the v6 migration seed. Seeds the starter
        /// dungeon (The Healer's Cottage) as discovered so the Dungeon Select
        /// screen always shows ≥1 playable dungeon.
        /// </summary>
        public static DungeonProgress Empty()
        {
            return new DungeonProgress
            {
                Discovered = new Dictionary<string, bool> { { SaveSchema.StarterDungeonId, true } },
                Cleared = new Dictionary<string, int>(),
                BestTime = new Dictionary<string, double>(),
                NoHitClear = new Dictionary<string, bool>(),
            };
        }
    }

    /// <summary>One live quest's state (saveSchema questStateSchema).</summary>
    [Serializable]
    public sealed class QuestState
    {
        [JsonProperty("beatIndex")] public int BeatIndex;
        [JsonProperty("flags")] public Dictionary<string, bool> Flags = new Dictionary<string, bool>();
        // WO-290: current stage id within the quest's stage chain (QuestCatalog).
        // Additive — defaults to null on read of an older save, no version bump.
        [JsonProperty("stageId")] public string StageId;
        // WO-854 Phase 2: per-stage progress tallies for counting completion
        // conditions (a QuestStage.completeOn whose count is > 1 -- e.g. "clear 3
        // waves"). Keyed by QuestCompletion.CounterKey(stageId), so a later stage
        // never inherits an earlier one's tally. Written by the Village-side
        // StoryQuestSignalBridge and erased when the stage it belongs to advances.
        // Additive -- the field initializer supplies an empty dictionary on read of an
        // older save that has no "counters" member, so NO version bump / migrator
        // step is required (same convention as stageId above and keystones /
        // trackedId on QuestProgress below).
        [JsonProperty("counters")] public Dictionary<string, int> Counters = new Dictionary<string, int>();
    }

    /// <summary>The persisted quest ledger (dungeonsSlice QuestProgress).</summary>
    [Serializable]
    public sealed class QuestProgress
    {
        [JsonProperty("active")] public Dictionary<string, QuestState> Active = new Dictionary<string, QuestState>();
        [JsonProperty("completed")] public Dictionary<string, bool> Completed = new Dictionary<string, bool>();
        [JsonProperty("available")] public Dictionary<string, bool> Available = new Dictionary<string, bool>();
        // WO-290: aggregate keystone ledger (story-progression macguffins earned
        // across quests). Additive — defaults to an empty list on read of an older
        // save, so NO version bump / migrator step is required.
        [JsonProperty("keystones")] public List<string> Keystones = new List<string>();
        // WO-454: the player-selected quest pinned to the far-right HUD slot. Additive +
        // nullable — defaults to null on read of an older save, so NO version bump / migrator
        // step is required (same convention as keystones above / stageId on QuestState).
        [JsonProperty("trackedId")] public string TrackedId;

        /// <summary>Fresh empty quest progress — the v6 migration seed.</summary>
        public static QuestProgress Empty()
        {
            return new QuestProgress
            {
                Active = new Dictionary<string, QuestState>(),
                Completed = new Dictionary<string, bool>(),
                Available = new Dictionary<string, bool>(),
                Keystones = new List<string>(),
            };
        }
    }

    /// <summary>The persisted Realm Map region-progress ledger (regionsSlice).</summary>
    [Serializable]
    public sealed class RegionProgress
    {
        /// <summary>regionId → true once discovered (gate met).</summary>
        [JsonProperty("discovered")] public Dictionary<string, bool> Discovered = new Dictionary<string, bool>();
        /// <summary>regionId → true once the region's main objective is cleared.</summary>
        [JsonProperty("cleared")] public Dictionary<string, bool> Cleared = new Dictionary<string, bool>();

        /// <summary>Fresh empty region progress — the v10 migration seed.</summary>
        public static RegionProgress Empty()
        {
            return new RegionProgress
            {
                Discovered = new Dictionary<string, bool>(),
                Cleared = new Dictionary<string, bool>(),
            };
        }
    }
}
