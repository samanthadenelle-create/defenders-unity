// =============================================================================
// GameState — the persisted-state ScriptableObject (spec §1.6)
// -----------------------------------------------------------------------------
// The Unity analog of the Zustand store's DATA. A pure data container — NO
// logic, NO events — holding the persisted fields the React `partialize`
// selects (originally 41; now ~64 after many append-only additions through
// schema v34), plus SchemaVersion. One asset instance lives at
// Assets/_Modules/Core/State/GameState.asset and is the in-memory live state.
//
// Mutators / subscribers / load+save live in GameStateService. Fresh defaults
// here = a brand-new game; the service overwrites them on Load() when a save
// exists. The save layer (SaveSchema / SaveMigrator) round-trips these fields.
//
// Slices are a React code-organisation device only — at runtime the state is
// one FLAT object, so this SO is flat too (the `── Region ──` comments only
// echo the slice grouping for readers).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Jobs;

namespace DeNelle.Core.State
{
    /// <summary>
    /// The persisted slice of game state — the fields selected by the React
    /// store's <c>partialize</c> (originally 41; ~60 today after append-only
    /// growth through schema v34). A pure data container.
    /// </summary>
    [CreateAssetMenu(menuName = "Defenders/Core/Game State", fileName = "GameState")]
    public sealed class GameState : ScriptableObject
    {
        /// <summary>Persisted-save schema version. = SaveSchema.CurrentVersion (36).</summary>
        public int SchemaVersion = SaveSchema.CurrentVersion;

        // ── Player (playerSlice) ─────────────────────────────────────────────
        /// <summary>#3 — true once pet creation + tutorial are complete.</summary>
        public bool Onboarded;
        /// <summary>#4 — highest wave cleared. Clamped ≥0.</summary>
        public int BestWave;
        /// <summary>
        /// #28 — chosen hero class. Inspector-friendly nullable wrapper
        /// (HeroClassOpt.None = the React null). Improvement #5 NOT adopted.
        /// </summary>
        public HeroClassOpt HeroClass = HeroClassOpt.None;
        /// <summary>#27 — wallet the save is tagged to. null when unbound.</summary>
        public string BoundWallet;

        // ── Resources (resourcesSlice) ───────────────────────────────────────
        /// <summary>#5 — main wallet. Fresh = STARTER_RESOURCES {250,80,15}.</summary>
        public ResourceBalance Resources = ResourceBalance.Starter;
        /// <summary>#8 — rare Voidshard currency. Fresh = 5. Clamped ≥0.</summary>
        public int Voidshards = 5;
        /// <summary>DEPRECATED (save v18) — the legacy Aether Crystals pool was unified
        /// into <see cref="Resources"/>.Crystals (the single source of truth). Kept at 0
        /// for save back-compat (the aetherCrystals JsonProperty still round-trips); no
        /// code writes a meaningful value here anymore. Use Resources.Crystals.</summary>
        public int AetherCrystals = 0;
        // WO-1212 (2026-08-26) - `public int Stone = 20;` LIVED HERE AND IS RETIRED.
        // There were TWO Stone balances. The one the player sees, spends and is granted
        // into is Resources.Stone: WO-1163 reused the legacy Food slot for the Stone
        // vocabulary, and EconomyService.Food (Assets/_Modules/Village/EconomyService.cs:162)
        // is its only reader and spender. This field was the OTHER one - no HUD read it, no
        // cost spent it, and nothing but the new-game seed and a dev top-up ever wrote it -
        // yet it was guarded AND time-derived-reconciled on both client and server. The
        // obvious `food -> stone` rename would therefore have routed real, purchasable value
        // into a balance the player can never see, with no error and no red test.
        //
        // ONE authority now: Resources.Stone. The `stone` WIRE key survives on
        // SaveSchema.PersistedState as an INBOUND ALIAS ONLY (GameStateService.FromPersisted,
        // BackendLoadResponse) so a sender that speaks only `stone` is not dropped on the
        // floor - never as a second balance. A STORED value is DISCARDED, not migrated:
        // every writer was the seed, a dev top-up or an echo of those two, so summing it
        // would invent value nobody earned on a build that takes real money.
        /// <summary>#13 — gathered Iron. Fresh = 5. Clamped ≥0.</summary>
        public int Iron = 5;
        /// <summary>#14 — gathered Wood. Fresh = 15. Clamped ≥0.</summary>
        public int Wood = 15;
        /// <summary>#6 — ids of purchased store items.</summary>
        public List<string> OwnedItemIds = new List<string>();

        // ── Pets (petsSlice) ─────────────────────────────────────────────────
        /// <summary>#1 — owned pet roster.</summary>
        public List<PetData> Pets = new List<PetData>();
        /// <summary>#2 — id of the onboarding starter pet. null until chosen.</summary>
        public string StarterPetId;
        /// <summary>#7 — guardian Bond Ranks [Aether, Flame, Ice]. Fresh = [0,0,0].</summary>
        public List<int> PetBonds = new List<int> { 0, 0, 0 };
        /// <summary>#25 — unlocked Warden species.</summary>
        public List<PetSpecies> OwnedPets = new List<PetSpecies>();
        /// <summary>
        /// REDS #3 (flag_17) — the pet ACTIVE-SLOT deploy map: one entry per deploy slot in
        /// slot order, holding the canonical species id occupying it (e.g. "ice-wolf") or null
        /// for an empty slot. The persisted mirror of PetAcquisitionService's runtime
        /// <c>_activeSlots</c>: previously that map was rebuilt from <see cref="StarterPetId"/>
        /// alone on load (SyncSlotsFromState), so a multi-slot pet roster reset every reload.
        /// PetAcquisitionService now rebuilds from this list on load and writes it back on every
        /// slot change. Empty on a fresh save (the acquisition service falls back to its legacy
        /// starter-in-slot-0 rebuild). Round-trips through SaveSchema (v34) — additive at the END
        /// so older saves load with an empty map.
        /// </summary>
        public List<string> PetActiveSlots = new List<string>();

        // ── Village (villageSlice) ───────────────────────────────────────────
        /// <summary>#9 — tower tier per slot. Length = TowerSlots (9). Fresh = [0]×9.</summary>
        public List<int> Towers = NewZeroed(Constants.TowerSlots);
        /// <summary>#10 — tower ability per slot. Length = 9. Fresh = [0]×9.</summary>
        public List<int> TowerAbilities = NewZeroed(Constants.TowerSlots);
        /// <summary>#11 — wall level 0..WALL_MAX_LEVEL (3). Fresh = 0.</summary>
        public int WallLevel;
        /// <summary>#15 — buildingId → ready-at ms timestamp.</summary>
        public SerializableDict<string, double> BuildingCooldowns = new SerializableDict<string, double>();
        /// <summary>#16 — in-flight pet-assisted tower builds.</summary>
        public List<PendingTowerBuild> PendingBuilds = new List<PendingTowerBuild>();
        /// <summary>#32 — buildingId → 0..100 damage. Keys incl. gate-0..gate-3, "heart".</summary>
        public SerializableDict<string, double> BuildingDamage = new SerializableDict<string, double>();

        // ── Settings (settingsSlice) ─────────────────────────────────────────
        /// <summary>#18 — movement joystick sensitivity. Fresh = 1. Setter clamps 0.3..1.5.</summary>
        public float JoystickSensitivity = 1f;
        /// <summary>#19 — hero movement control style. Fresh = Auto.</summary>
        public MovementStyle MovementStyle = MovementStyle.Auto;
        /// <summary>#31 — breach battle system. Fresh = Ask. Survives New Game.</summary>
        public BreachStyle BreachStyle = BreachStyle.Ask;
        /// <summary>#20 — global audio mute. Fresh = false (owner directive 2026-05-20:
        /// the a11y-muted default hid the new music wiring; players see-it-and-mute-it
        /// if they need silence, but the default expectation now is "music plays").</summary>
        public bool Muted;
        /// <summary>#21 — music volume 0..100. Fresh = 70.</summary>
        public float MusicVolume = 70f;
        /// <summary>#22 — sfx volume 0..100. Fresh = 80.</summary>
        public float SfxVolume = 80f;
        /// <summary>#23 — gameplay difficulty. Fresh = Normal.</summary>
        public Difficulty Difficulty = Difficulty.Normal;
        /// <summary>#24 — voice-over toggle. Stored, not yet wired.</summary>
        public bool VoiceOvers;

        // ── Tutorial (tutorialSlice) ─────────────────────────────────────────
        /// <summary>#17 — first-time tutorial step. Fresh = Step1; migrated saves = Done.</summary>
        public TutorialStep TutorialStep = TutorialStep.Step1;
        /// <summary>#26 — one-shot tutorial-seen flags.</summary>
        public SerializableDict<string, bool> SeenTutorials = new SerializableDict<string, bool>();

        // ── ATB / combat (combatSlice) ───────────────────────────────────────
        /// <summary>#29 — shared ATB consumable inventory. Fresh = {0,0,0}.</summary>
        public AtbInventory Inventory = AtbInventory.Empty;
        /// <summary>Shop-bought gear counts (id → qty). Source of truth for VillageInventory;
        /// persists + Neon-syncs (v20). Village writes here via GameStateService (Village→Core).</summary>
        public System.Collections.Generic.Dictionary<string, int> GearInventory = new System.Collections.Generic.Dictionary<string, int>();
        /// <summary>#30 — consecutive ATB defeats. Fresh = 0. Clamped ≥0.</summary>
        public int AtbLossStreak;

        // ── Dungeons / quests / regions ──────────────────────────────────────
        /// <summary>#33 — discovered/cleared/best-time/no-hit dungeon ledger.</summary>
        public DungeonProgress Dungeons = DungeonProgress.Empty();
        /// <summary>#34 — mid-run dungeon save. null when no run is in progress.</summary>
        public ActiveDungeonRun ActiveDungeonRun;
        /// <summary>#35 — active/completed/available quest ledger.</summary>
        public QuestProgress Quests = QuestProgress.Empty();
        /// <summary>#36 — Realm Map discovered/cleared region ledger.</summary>
        public RegionProgress Regions = RegionProgress.Empty();

        // ── Social (socialSlice) ─────────────────────────────────────────────
        /// <summary>#37 — this player's 6-char invite code. Stable once generated.</summary>
        public string MyInviteCode;
        /// <summary>#38 — locally-added chat contacts.</summary>
        public List<ChatContact> Contacts = new List<ChatContact>();
        /// <summary>#38a — codes whose incoming messages are dropped.</summary>
        public List<string> BlockedCodes = new List<string>();
        /// <summary>#38b — cached inbox, newest-first.</summary>
        public List<ChatMessage> Inbox = new List<ChatMessage>();
        /// <summary>#38c — unix ms of the last inbox sync. Fresh = 0.</summary>
        public double LastInboxSyncAt;

        // ── Offline harvest accrual (WO-115) ─────────────────────────────────
        /// <summary>
        /// Unix-ms of the last offline-harvest accrual claim (WO-115). Fresh = 0
        /// (never claimed → seeded to now on first load, no retroactive haul). The
        /// accrual clock: OfflineHarvestService integrates each active harvest source's
        /// rate over (now − this) on resume/load, banks the capped haul, then advances
        /// this to now. Mirrors <see cref="LastInboxSyncAt"/> exactly (a double unix-ms
        /// field that round-trips through the save layer). Append-only at the END so
        /// older saves stay loadable.
        /// </summary>
        public double LastHarvestClaimMs;

        // ── Echo Workforce V1 (ECHO_WORKFORCE_SPEC) ──────────────────────────
        /// <summary>
        /// Number of Echo workers the player owns (the farm faucet). Starts at 1,
        /// unlocks +1 every 5 waves cleared, hard-capped at 4. Reuses
        /// <see cref="LastHarvestClaimMs"/> as the silo clock (the same persisted
        /// Unix-ms accrual timestamp OfflineHarvestService advances), so EchoService
        /// integrates echoCount x ratePerHour over (now - LastHarvestClaimMs).
        /// Round-trips through SaveSchema v25 (additive at the END).
        /// </summary>
        public int EchoCount = 1;

        /// <summary>
        /// Pooled silo buffer — fractional resources accrued by the Echoes but not yet
        /// dumped into the spendable wallet (one shared pool for V1). DumpSilos() splits
        /// this across the configured resource types via EconomyService.GrantSpendable
        /// then resets it to 0. Clamped to the silo HOUR cap. Append-only at the END.
        /// </summary>
        public double SiloResources;

        /// <summary>
        /// Total waves cleared across the save (the Echo-unlock counter). Distinct from
        /// <see cref="BestWave"/> (highest reached in one run) — this counts EVERY wave
        /// clear, so it survives a defeat+retry and drives "+1 Echo every 5 waves".
        /// Append-only at the END.
        /// </summary>
        public int WavesCompleted;

        // ── Build/upgrade timers + ad-skip (WO-172) ──────────────────────────
        /// <summary>
        /// In-flight construction/upgrade jobs (WO-172) — the CoC time-sink. Each
        /// counts down in REAL wall-clock time (offline too), keyed by structure id;
        /// the structure completes at job.FinishMs. Managed by BuildTimerService.
        /// Mirrors <see cref="PendingBuilds"/>'s "small serializable struct in a list"
        /// persistence shape. Append-only field at the END so older saves stay loadable.
        /// </summary>
        public List<BuildJobData> BuildJobs = new List<BuildJobData>();

        /// <summary>
        /// Rewarded-ad build-skips used in the current local day (WO-172 daily cap).
        /// Reset to 0 when <see cref="AdSkipDayKey"/> rolls to a new day. Clamped ≥0.
        /// </summary>
        public int AdSkipsUsedToday;

        /// <summary>
        /// Local-day key ("yyyy-MM-dd", device-local) the <see cref="AdSkipsUsedToday"/>
        /// counter belongs to. When today's key differs, the counter resets (daily cap).
        /// </summary>
        public string AdSkipDayKey;

        /// <summary>UTC day key (yyyy-MM-dd) of the last claimed post-tutorial daily chest.</summary>
        public string DailyChestDayKey;

        // ── World / zones (WO-164) ────────────────────────────────────────────
        /// <summary>Per-region zone records — discovery/clear flags, neighbor graph,
        /// and City/Horde destination tag (WO-164). Seeded from
        /// <see cref="DeNelle.Core.World.ZoneManager.DefaultZoneGraph"/> on a fresh save.
        /// Append-only field — added at the END so older saves stay loadable. Round-trips
        /// through SaveSchema (v17, WO-164): seeded on New Game + backfilled on load via
        /// GameStateService.EnsureZoneGraph, and pre-v17 saves are seeded by SaveMigrator
        /// MigrateToV17.</summary>
        public List<DeNelle.Core.World.ZoneState> Zones = new List<DeNelle.Core.World.ZoneState>();

        // ── World content (WO-159 settlements / WO-160 tribes) ───────────────
        /// <summary>Per-tribe roaming-raider records (WO-160) — members remaining, cleared
        /// flag, clear-count for reduced respawn, last-seen. Append-only field at the END so
        /// older saves stay loadable. Round-trips through SaveSchema (v34, REDS #4): serialized
        /// in GameStateService.Snapshot, restored in ApplyPersisted, seeded to an empty list on
        /// older saves by SaveMigrator MigrateToV34 — so a half-cleared tribe no longer resets on
        /// reload. (Like <see cref="Zones"/>/<see cref="Settlements"/>/<see cref="Wards"/>.)</summary>
        public List<DeNelle.Core.World.TribeState> Tribes = new List<DeNelle.Core.World.TribeState>();

        /// <summary>Per-site node-settlement records (WO-159) — claim phase, defence HP, and
        /// the razed-site game-day lockout. Append-only field at the END. Round-trips through
        /// SaveSchema (v21): serialized in GameStateService.Snapshot, restored in ApplyPersisted,
        /// and null-defaulted to an empty list on load. (Unlike <see cref="Tribes"/>/<see cref="Wards"/>,
        /// which remain in-memory only.)</summary>
        public List<DeNelle.Core.World.SettlementState> Settlements = new List<DeNelle.Core.World.SettlementState>();

        /// <summary>Per-ward relight records (WO-112) — the earned exploration reach. Each
        /// carries its ward id/region/granted-reach and whether the Keeper has relit it; the
        /// set of lit wards drives per-march reach (WardReach) and the forgetting effect.
        /// Append-only field at the END so older saves stay loadable. Round-trips through
        /// SaveSchema (v34, REDS #4): serialized in GameStateService.Snapshot, restored in
        /// ApplyPersisted, seeded to an empty list on older saves by SaveMigrator MigrateToV34 —
        /// so relit wards no longer reset on reload. (<see cref="Settlements"/>/<see cref="Zones"/>/
        /// <see cref="Tribes"/> DO round-trip.)</summary>
        public List<DeNelle.Core.World.WardStoneState> Wards = new List<DeNelle.Core.World.WardStoneState>();

        // ── Build Mode — the player's base layout (WO-108 P0 data spine) ──────
        /// <summary>
        /// The player's placed-structure layout — the CREATE-verb spine (WO-108).
        /// One <see cref="PlacedStructureData"/> per player-placed structure (grid
        /// cell + discrete yaw + level). Empty on a fresh save → the runtime
        /// <c>BaseLayoutLoader</c> falls through to the default VillageSceneBuilder
        /// village (the seed), and the first build-mode entry seeds this from that
        /// default so the player edits their familiar town, not a blank plot.
        /// Round-trips through SaveSchema (v14) — additive at the END so older
        /// saves load empty. This is exactly the payload that becomes
        /// server-authoritative when async raids land (principle #2).
        /// </summary>
        public List<PlacedStructureData> BaseLayout = new List<PlacedStructureData>();

        // ── Pets — onboarding-named starter pet (WO-277) ─────────────────────
        /// <summary>
        /// The player-chosen name for their starter pet, set in the tutorial's pet
        /// introduction (WO-277). Empty/null until the player names it. Round-trips
        /// through SaveSchema (PersistedState.PetName, [JsonProperty("petName")]):
        /// serialized in GameStateService.Snapshot and restored in ApplyPersisted.
        /// Append-only field at the END of the class so older saves stay loadable.
        /// </summary>
        public string PetName;

        // ── Magic — building-upgrade TECH axis (DEF-121 / WO-230) ─────────────
        /// <summary>
        /// Magic — the tech-tree gating currency (DEF-121). NOT a harvestable: there
        /// is no Magic MineNode / pickup. It is earned through progression (boss/dungeon
        /// tech rewards) and spent on Magic-gated building-upgrade tiers that unlock
        /// tech-tree nodes (see <c>ResourceBuildingProgression</c>'s Magic tier). Fresh = 0.
        /// Clamped &gt;= 0. Round-trips through SaveSchema (v15) — append-only field at the
        /// END so older saves load with Magic = 0.
        /// </summary>
        public int Magic = 0;

        // ── Party — persisted companion roster (WO-301) ───────────────────────
        /// <summary>
        /// The persisted party roster (WO-301) — companion ids in JOIN ORDER (the
        /// canon roster is Sylas→Elara→Grom→Thrain; ids are the companion's
        /// <see cref="HeroClass"/> name, e.g. "Ranger"). This is the backbone the
        /// companion spawn, follow and party-UI all read: a spawn reads the roster
        /// to know WHO to place with the hero, and the HUD shows one party frame per
        /// member. Mutated via <see cref="GameStateService.AddToParty"/> /
        /// <c>RemoveFromParty</c>; round-trips through SaveSchema (v16) — append-only
        /// field at the END so older saves load with an empty party. The hero itself
        /// is HUD party slot 0 and is NOT stored here (this is the COMPANION roster).
        /// </summary>
        public List<string> PartyMemberIds = new List<string>();

        /// <summary>Number of companions in the party (derived from the roster). 0 when alone.</summary>
        public int PartySize => PartyMemberIds != null ? PartyMemberIds.Count : 0;

        // ── Arena — async-PvP raid record (ARENA MVP) ─────────────────────────
        /// <summary>
        /// The Arena (async-PvP) win/loss ledger — the SKR-wager raid loop's record
        /// (ARENA MVP). A small append-only struct (Wins/Rows/Streak/TotalPurse),
        /// mirroring the "small serializable record on GameState" shape that Zones /
        /// Tribes / BaseLayout use. Append-only field at the END so older saves load
        /// with a zeroed record. Round-trips through SaveSchema (v34, REDS #4):
        /// serialized in GameStateService.Snapshot (as the nullable <c>arena</c> wire
        /// field), restored in ApplyPersisted, seeded to <see cref="ArenaProgress.Empty"/>
        /// on older saves by SaveMigrator MigrateToV34 — so the W/L ledger now lives in
        /// the signed/migrated/validated save, not only the loose ArenaProgressStore
        /// PlayerPrefs mirror. The SKR wager balance itself is a SEPARATE client stub
        /// (ArenaWalletService) — this is only the W/L scoreboard.
        /// </summary>
        public ArenaProgress Arena = ArenaProgress.Empty;

        // ── Arena — pre-placed defenders (CoC defense layer, WO-389) ──────────
        /// <summary>
        /// The player's pre-placed Arena DEFENDERS — the CoC-style defense layer
        /// (WO-389). One <see cref="PlacedDefenderData"/> per pre-placed troop/structure
        /// (grid cell + discrete yaw), bought from a limited point pool
        /// (<c>ArenaDefenseCatalog.DefensePointPool</c>). SEPARATE from
        /// <see cref="BaseLayout"/> (the normal base buildings) — this is the defense
        /// layout: when the castle is raided in the Arena these spawn FRIENDLY and
        /// auto-fight the raiders. Empty on a fresh save (no pre-placed defense).
        /// Round-trips through SaveSchema (v19) — additive at the END so older saves
        /// load empty. This is exactly the payload that travels WITH the base recipe
        /// when async raids load the defender's castle (principle: the defense ports
        /// alongside the city).
        /// </summary>
        public List<PlacedDefenderData> ArenaDefense = new List<PlacedDefenderData>();

        // ── Army — persisted troop roster (WO-453) ────────────────────────────
        /// <summary>
        /// The player's persisted ARMY (WO-453) — the owned-troop roster, the army cap
        /// (10, expandable later via a barracks), and the train/wound-recover/veterancy
        /// state over them. Owned by GameState the SAME way <see cref="BaseLayout"/> /
        /// <see cref="ArenaDefense"/> are (a plain saveable class on the state object,
        /// NOT a singleton/PlayerPrefs). Loss model = wounded-recovery, NO permadeath:
        /// a downed troop is marked wounded + recovers, never deleted. Never null
        /// (constructed empty on a fresh save / backfilled on load by the v22 migration
        /// so older saves load with an empty cap-10 army). Round-trips through SaveSchema
        /// (v22) — additive at the END so older saves stay loadable.
        /// </summary>
        public ArmyStorage Army = new ArmyStorage();

        // ── Building upgrade tiers (WO-430) ───────────────────────────────────
        /// <summary>
        /// Per-building upgrade TIER (0-4), keyed by building id (e.g. {"armorer":2}).
        /// The single source of truth the city-upgrade system reads: ModifierService
        /// compiles it into <see cref="GameModifiers"/>, the dialogue title shows the
        /// level, and the Yarn <c>$&lt;id&gt;_Level</c> vars mirror it. Owned by GameState
        /// like <see cref="Army"/>; never null (empty on fresh save / backfilled by the
        /// v23 migration). Round-trips through SaveSchema v23 (additive at the END).
        /// </summary>
        public System.Collections.Generic.Dictionary<string, int> BuildingTiers
            = new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>
        /// WO-432 — the global Village/Stronghold Tier (the WC3 tech-gate, owner-decided anchor:
        /// upgraded at the Heart of Elarion). A building's research level N requires VillageTier &gt;= N.
        /// 0 on a fresh save. Round-trips through SaveSchema v24 (additive at the END).
        /// </summary>
        public int VillageTier = 0;

        /// <summary>
        /// WO-432 — owned building-research perks, keyed <c>"buildingId:perkId"</c> (e.g.
        /// <c>"forge:forge-damage-2"</c>). ModifierService compiles each owned perk's GameModifiers into
        /// the active set, like a tier. Never null (empty on fresh save). Round-trips through SaveSchema v24.
        /// </summary>
        public System.Collections.Generic.List<string> OwnedBuildingPerks
            = new System.Collections.Generic.List<string>();

        // ── WO-587 — Population & Echo growth (schema v28) ────────────────────
        /// <summary>
        /// WO-587 — accumulated Population XP (earned from quests / outposts / waves). Drives the
        /// milestone-based Echo workforce slot unlocks. 0 on a fresh save. Round-trips through
        /// SaveSchema v28 (additive at the END so older saves stay loadable).
        /// </summary>
        public int PopulationXP = 0;

        /// <summary>WO-587 — cumulative completed quests counted toward Population milestones (v28).</summary>
        public int PopulationQuests = 0;

        /// <summary>WO-587 — cumulative cleared enemy outposts counted toward Population milestones (v28).</summary>
        public int PopulationOutposts = 0;

        /// <summary>
        /// WO-587 — highest Echo workforce SLOT unlocked by Population milestones (1..5; 1 = the
        /// starter Wood echo). Guards "unlock exactly once". Round-trips through SaveSchema v28.
        /// </summary>
        public int PopulationEchoSlots = 1;

        // ── F8-47 — Hero level/XP persistence (schema v29) ────────────────────
        /// <summary>
        /// F8-47 — the hero's current LEVEL. The persisted mirror of
        /// <c>HeroProgression._level</c> (which is per-run in-memory): HeroProgression
        /// restores from here on attach and writes back on every XP change, so a scene
        /// load (e.g. porting home from the challenge outpost) can no longer reset the
        /// hero to level 1. 1 on a fresh save. Round-trips through SaveSchema v29
        /// (additive at the END so older saves stay loadable).
        /// </summary>
        public int HeroLevel = 1;

        /// <summary>F8-47 — XP banked toward the hero's next level (fractional kill-XP shares; v29).</summary>
        public float HeroXp = 0f;

        /// <summary>F8-47 — total XP the hero has ever earned (telemetry/UI counter; v29).</summary>
        public float HeroLifetimeXp = 0f;

        // ── WO-673 — strategic-placement migration marker (v30) ──────────────
        /// <summary>
        /// True once the player owns the town layout: set by the ONE-SHOT WO-673 migration
        /// (StrategicPlacementMigration.RunIfNeeded — converts a pre-existing save's
        /// auto-placed functional structures into <see cref="BaseLayout"/> records), or by
        /// ResetToNewGame (WO-682 blank-template new game: nothing to migrate, marker set
        /// so the writer never runs). Gates BOTH the injector/bake standdown AND the
        /// loader's replay of managed records — mutual exclusion, never a double-spawn
        /// (docs/WO673_ARCHITECTURE_REVIEW.md §3). False on a pre-v30 save =
        /// bakes/injectors own everything until the one-shot writer runs.
        /// Round-trips through SaveSchema v30 — additive at the END so older saves load false.
        /// </summary>
        public bool StrategicPlacementMigrated = false;

        // ── WO-681/658 — Echo lane assignments ───────────────────────────────
        /// <summary>
        /// Per-Echo lane + level assignments as a CSV of "lane:level" tokens by echo
        /// index (e.g. "harvest:3,idle,crafting:1"). WO-738 evolved the vocabulary from
        /// resource lanes (wood/iron/food) to FUNCTIONAL lanes: "harvest" / "crafting" /
        /// "defense" / "exploration" / "idle" (idle carries no level suffix). Read/written
        /// via <see cref="DeNelle.Village.EchoAssignments"/>. Backward-compatible,
        /// default-on-read (NO migrator): a legacy bare token wood/iron/food normalizes
        /// forward to Harvest, and any bare token with no ":level" reads level 1, so the
        /// shipped "wood" default below still loads as Harvest / level 1. An index beyond
        /// the CSV reads as "idle" (index 0 defaults to Harvest, the starter Echo). New
        /// Game seeds the starter Echo as "harvest:1". Additive default-on-read (nullable
        /// on the wire; absent → this initializer). Append-only at the END.
        /// </summary>
        public string EchoLanes = "wood";

        // ── First-build freebies (v32) ───────────────────────────────────────
        /// <summary>
        /// Catalog itemIds whose ONE-TIME free first build has been CONSUMED (owner
        /// ruling 2026-07-13 evening: the FIRST placement of each catalog building id
        /// is FREE; the flag burns at the committed placement and can NEVER reset —
        /// destroying/selling the building does not restore it). Per-save: a New Game
        /// gets a fresh empty list = all freebies live again. The free-first-build
        /// flags REPLACE the old 650w/385i founding resource seed (StartingBudget is
        /// now 0/0): players earn everything beyond the free kit from production.
        /// Consumed by BuildModeController.Place(); read by EffectiveCostFor.
        /// Additive default-on-read (nullable on the wire; absent → this initializer,
        /// so an old save gains its full freebies — correct). Append-only at the END.
        /// </summary>
        public List<string> FreeBuildsUsed = new List<string>();

        // ── WO-773 — the common "Obsidian" multi-channel work queue (v35) ─────
        /// <summary>
        /// WO-773 — the single job queue every timed action flows through, split into
        /// independent CHANNELS (Builder / Train / Research) that never share slots (CoC
        /// feel: builders and the barracks run in parallel). Each channel holds its ACTIVE
        /// jobs (≤ derived slot count) + a FIFO PENDING queue + a purchased-slot count. The
        /// JOB RECORD is <see cref="BuildJobData"/> (the WO-172 offline-fair timer, now with a
        /// Kind + Channel). Managed by BuildTimerService via <see cref="DeNelle.Core.Jobs.ObsidianQueueEngine"/>.
        /// Round-trips through SaveSchema (v35); the v34→v35 migration folds the legacy
        /// <see cref="BuildJobs"/>/<see cref="PendingBuilds"/>/<see cref="BuildingCooldowns"/> into
        /// the Builder channel so no in-flight work is lost (<see cref="BuildJobs"/> is retained
        /// for wire back-compat but is no longer read at runtime). Never null (fresh = Empty()).
        /// </summary>
        public ObsidianQueueState ObsidianQueue = ObsidianQueueState.Empty();

        // ── WO-771.9 — Barracks & troop upgrade progression (additive, NO schema bump) ──
        /// <summary>
        /// WO-771.9 — the LEGACY stored barracks level.
        ///
        /// <para>⛔ RETIRED AS A GATE (owner ruling 21, 2026-09-06: <i>"Merge them - the building tier
        /// gates troops."</i>). Troop unlocks now read the barracks BUILDING TIER,
        /// <c>BuildingTiers["barracks"]</c>, through
        /// <c>BarracksProgression.EffectiveBarracksLevelOf</c> = MAX(this field, that tier) floored
        /// to 1. This field is <b>READ-MIGRATED, never deleted and never written back</b> — a live
        /// save key is never dropped without a migration (CLAUDE.md §8), and MAX-on-read means an
        /// existing save can only move up. The doc here used to say this field "drives which troops
        /// are unlocked", and it did — which is exactly the defect: its only writer was the
        /// completion effect of a BarracksUpgrade job composed only by BarracksPanelVM, reachable
        /// only from BarracksPanel.ShowBarracksUI, which had ZERO CALLERS. So it sat at 1 forever
        /// and seven of the nine troops were unreachable while the player upgraded a barracks
        /// BUILDING that did nothing for the army. ⛔ Both of those files were DELETED on 2026-09-06
        /// (WO-1430 Group A) — they are named here as HISTORY, not as paths to open. THE FIELD
        /// SURVIVES THEM: it is a live save key and is read-migrated, never dropped.</para>
        ///
        /// Day-one = 1. Raised (on the now-dead path only) when a
        /// <see cref="DeNelle.Core.Jobs.JobKind.BarracksUpgrade"/> job completes on
        /// the Builder channel (BarracksService / BarracksProgression). ADDITIVE default-on-read:
        /// nullable on the wire (SaveSchema.barracksLevel), absent → this initializer (1), so no
        /// schema bump — rides the committed v35. Append-only field at the END.
        /// </summary>
        public int BarracksLevel = 1;

        /// <summary>
        /// WO-771.9 — per-troop UPGRADE LEVEL, keyed by troop id (e.g. {"troop-footman":3}).
        /// A troop absent from the map (or level &lt;= 1) resolves to its pure baseline via
        /// <see cref="DeNelle.Village.TroopStatResolver"/>. Raised when a
        /// <see cref="DeNelle.Core.Jobs.JobKind.TroopUpgrade"/> job completes on the Research
        /// channel. ADDITIVE default-on-read: nullable on the wire (SaveSchema.troopLevels),
        /// absent → this empty initializer, so no schema bump. Append-only field at the END.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, int> TroopLevels
            = new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>
        /// WO-808 Option A — per-instance GEAR power level, keyed by gear id (e.g.
        /// {"weapon_iron_longsword":3}). The SAME owned weapon/armor levels up in place
        /// (owner-locked 2026-07-30: instance reforge — not tier-swap, not rarity).
        /// Absent id (or level &lt;= 1) resolves to the authored baseline via
        /// <c>DeNelle.Village.GearStatResolver</c>. Raised by GearProgression.Improve
        /// (instant, ResourceLedger-charged). ADDITIVE default-on-read: nullable on the
        /// wire (SaveSchema.gearLevels), absent → this empty initializer, so no schema
        /// bump — the troopLevels precedent. Append-only field at the END.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, int> GearLevels
            = new System.Collections.Generic.Dictionary<string, int>();

        // ── WO-834 — blank-founding baked standdown (v36) ────────────────────
        /// <summary>
        /// Catalog itemIds the player has EVER committed a placement of (WO-834).
        /// MONOTONIC — written via <see cref="MarkEverBuilt"/> at the BaseLayout commit
        /// seams (BuildModeController.Place; the StrategicPlacementMigration template
        /// grant for Default-Town/legacy saves) and NEVER removed: selling keeps the id,
        /// which is what keeps the WO-819 sell→baked-twin-resurface contract alive.
        /// The persisted input to <c>StructureSingleton.MayBakedTwinSurface</c>: on a
        /// <see cref="StrategicPlacementMigrated"/> save, a baked twin may surface only
        /// for ids in this set — so a Build-Your-Own founding (marker true + empty set)
        /// shows a truly BLANK town. Round-trips through SaveSchema v36 (MigrateToV36
        /// seeds existing saves: BaseLayout ∪ FreeBuildsUsed ∪ template-if-established).
        /// Additive default-on-read (nullable on the wire; absent → this initializer).
        /// Append-only field at the END.
        /// </summary>
        public List<string> EverBuiltStructureIds = new List<string>();

        // ── WO-1026 — PvE siege / the defence consequence loop ────────────────
        /// <summary>
        /// The ring-buffered defence-report ledger (WO-1026): one record per attack ON THE
        /// PLAYER'S TOWN, newest last, capped at <c>DefenseReportLedger.MaxRetained</c>.
        /// <see cref="DeNelle.Core.Defense.DefenseReportLedger"/> is the ONE writer — never
        /// mutate this list directly.
        /// Additive default-on-read, NO schema bump (nullable on the wire; absent → this
        /// initializer = today's exact behaviour, since the loop is FeatureFlags.Siege-gated).
        /// </summary>
        public List<DeNelle.Core.Defense.DefenseOutcomeRecord> DefenseReports
            = new List<DeNelle.Core.Defense.DefenseOutcomeRecord>();

        /// <summary>
        /// WO-1026 — Unix-ms of the last siege. The siege CADENCE clock, deliberately separate
        /// from <see cref="LastHarvestClaimMs"/>: the offline coordinator owns that one and a
        /// second writer on it is the WO-1147 bug (three systems sharing the clock made offline
        /// Echo repair never accrue). 0 = never sieged; the first evaluation seeds it forward
        /// and produces NO retroactive assault.
        /// </summary>
        public double LastSiegeUnixMs;

        // ── WO-728 — per-camp raid cooldown (the repeatable-raid gate) ────────
        /// <summary>
        /// One record per raid camp currently RECOVERING from a clear (WO-728). A camp with
        /// no record here is raidable right now; the list is pruned as windows elapse, so it
        /// never grows a tail of dead entries.
        /// <see cref="DeNelle.Village.World.Camps.RaidCooldownService"/> is the ONE writer —
        /// never mutate this list directly, because it is also the one place that reads the
        /// SERVER-ANCHORED clock (a second writer stamping DateTime.UtcNow would silently
        /// re-open the device-clock exploit the service exists to close).
        /// Additive default-on-read, NO schema bump (nullable on the wire; absent → this
        /// initializer = every camp raidable, which is exactly today's behaviour).
        /// </summary>
        public List<RaidCooldownRecord> RaidCooldowns = new List<RaidCooldownRecord>();

        // -- WO-823 Phase E -- the FIRST-RAID soft gate signal (v41) ----------
        /// <summary>
        /// True once this save has finished at least one raid, by ANY exit -- victory,
        /// retreat or hero death. The ONE persisted input to the WO-823 Phase E soft gate:
        /// while it is false, <see cref="DeNelle.Village.ArmyReadiness"/> asks for only
        /// FirstRaidMinDeployableSlots (3) deployable slots instead of the full army cap,
        /// so a brand-new player is not made to fill ten slots before ever seeing a raid.
        /// The moment it flips true the requirement is the full cap, permanently.
        ///
        /// Written in exactly ONE place -- RaidDeployController.ReconcileRaidEnd, the
        /// latched seam every raid exit already funnels through. Never written by a raid
        /// screen, panel or VM: a second writer would fork the one-owner seam, which is
        /// the same defect Phase E exists to remove from RaidDeployScreen.
        ///
        /// Round-trips through SaveSchema v41. Additive default-on-read (nullable on the
        /// wire; absent -> this initializer). MigrateToV41 derives it for existing saves
        /// from the evidence a raid leaves behind (veterancy / camp cooldowns) -- see that
        /// migrator for the two documented gaps.
        /// </summary>
        public bool EverCompletedRaid = false;

        /// <summary>Monotonic item-possession history; spending an ingredient never erases discovery.</summary>
        public List<string> EverAcquiredItemIds = new List<string>();

        // -- WO-1375 / PROGRAM_RAID_ECONOMY section 4 -- the RAID VICTORY COUNTER ------
        /// <summary>
        /// MONOTONIC count of raids this save has WON. The input the escalation ladder does
        /// not have yet: PROGRAM_RAID_ECONOMY_2026-09-04 section 4 unlocks target 2 after 3
        /// victories, target 3 after 10 and the Iron Bastion after 20, and nothing in the tree
        /// counted wins. Distinct from <see cref="EverCompletedRaid"/> (a first-raid BOOL that
        /// any exit sets, including a retreat or a death) and from the per-camp
        /// <c>RaidClaimService</c> claim flags (a SET of "have I ever taken THIS camp", which
        /// cannot answer "how many wins do I have" because re-clearing one camp twice adds
        /// nothing to it).
        ///
        /// <para>ONE WRITER: <c>DeNelle.Village.World.Camps.RaidVictoryController.HandleVictory</c>,
        /// after its <c>_handled</c> latch — the same de-duplicated settle seam the loot grant,
        /// the daily-quest report and the Season publish fire from. A second writer would
        /// double-count a win, which is exactly the ladder skipping a tier.</para>
        ///
        /// <para>Additive default-on-read, NO schema bump (nullable on the wire; absent -> this
        /// initializer). An existing save is BACKFILLED once from the claim flags rather than
        /// defaulted — see <see cref="RaidVictoriesBackfilled"/> — because a veteran loading 0
        /// would have the ladder taken away from them.</para>
        /// </summary>
        public int RaidVictories = 0;

        // Separate personal property; old saves have none. Never backfilled from raid counts.
        public OwnedBaseState OwnedBase;
        public PendingTownCapture PendingTownCapture;

        /// <summary>
        /// Latch for the ONE-SHOT backfill of <see cref="RaidVictories"/> from the persisted
        /// per-camp claim flags. It exists because the claim set lives in PlayerPrefs, NOT on
        /// the save wire, so a <c>SaveMigrator</c> step physically cannot see it — the backfill
        /// has to run on the Village side where <c>RaidClaimService</c> is visible, and it needs
        /// its own persisted "already done" mark so it cannot run twice and inflate the count.
        ///
        /// <para>The backfill is deliberately a FLOOR, not a reconstruction: one claimed camp
        /// proves at least one win, so a veteran with all three camps claimed seeds 3. It cannot
        /// recover repeat clears (nothing ever recorded them), and that under-count is recorded
        /// here rather than guessed at. Additive default-on-read, no schema bump.</para>
        /// </summary>
        public bool RaidVictoriesBackfilled = false;

        // -- WO-1598 -- the NEW GAME reset epoch (the cloud save's reset declaration) -----
        /// <summary>
        /// MONOTONIC count of times this save has been reset by <c>ResetToNewGame</c>. It is
        /// the single fact that lets a server tell a legitimate NEW GAME apart from a rollback
        /// or a stolen/replayed old device.
        ///
        /// <para>⛔ WHY IT EXISTS (WO-1598). <c>api/game/save.js</c> runs a sanity guard that
        /// REJECTS a save whose balances drop implausibly or whose <c>bestWave</c> goes
        /// backwards. A new game does exactly that on purpose: the owner's 2026-09-07 reset
        /// posted 36 crystals against a stored 901 and was refused eleven times, so the cloud
        /// row kept the OLD town and would have handed it back on the next load. The client
        /// now DECLARES the reset by sending this number; the server bypasses the guard once
        /// when the incoming epoch is NEWER than the stored one, applies the guard when they
        /// are equal, and refuses <c>SAVE_RESET_STALE</c> when it is older. A bare "this is a
        /// reset" flag would be forgeable and replayable, which is why it is monotonic.</para>
        ///
        /// <para>Written in exactly ONE place — <see cref="GameStateService.ResetToNewGame"/>,
        /// which raises it past its prior value. It is deliberately NOT wiped by that reset
        /// (a reset that zeroed its own counter could never be distinguished from the reset
        /// before it), and it is NOT a carve-out either: ResetToNewGame assigns it, so
        /// ResetToNewGameFullClearRegression Case 1 still sees the field covered.</para>
        ///
        /// <para>⛔ NO VERSION BUMP, on the <see cref="RaidVictories"/> precedent directly
        /// above: a NEW nullable wire field with no old shape to reinterpret and no field to
        /// rewrite. Absent on an older payload leaves this <c>0</c> initializer, which equals
        /// what the server stores for a row that never declared one — so an old client and an
        /// old row read as "equal epochs" and the guard behaves EXACTLY as it does today. A
        /// migrator step could add nothing: there is no evidence on the wire from which a past
        /// reset could be derived, and inventing one would forge a guard bypass. A bump on a
        /// LIVE published game is an owner decision and nothing here requires one.</para>
        ///
        /// <para>int, not long: the server contract is an integer, and
        /// <c>SaveSchema.NonNegInt</c> (which floors this field on read) returns int.</para>
        /// </summary>
        public int ResetEpoch = 0;

        public bool MarkEverAcquired(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            if (EverAcquiredItemIds == null) EverAcquiredItemIds = new List<string>();
            for (int i = 0; i < EverAcquiredItemIds.Count; i++)
                if (string.Equals(EverAcquiredItemIds[i], itemId, System.StringComparison.OrdinalIgnoreCase))
                    return false;
            EverAcquiredItemIds.Add(itemId);
            return true;
        }

        public bool HasEverAcquired(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || EverAcquiredItemIds == null) return false;
            for (int i = 0; i < EverAcquiredItemIds.Count; i++)
                if (string.Equals(EverAcquiredItemIds[i], itemId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// WO-834 — record that <paramref name="itemId"/> has been player-built at least
        /// once (idempotent set-add, OrdinalIgnoreCase — catalog-id convention). Returns
        /// true when the id was newly added. Caller owns persistence (the same Save()
        /// that commits the BaseLayout append carries this).
        /// </summary>
        public bool MarkEverBuilt(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            if (EverBuiltStructureIds == null) EverBuiltStructureIds = new List<string>();
            for (int i = 0; i < EverBuiltStructureIds.Count; i++)
                if (string.Equals(EverBuiltStructureIds[i], itemId, System.StringComparison.OrdinalIgnoreCase))
                    return false;
            EverBuiltStructureIds.Add(itemId);
            return true;
        }

        /// <summary>WO-834 — true when <paramref name="itemId"/> has ever been player-built on this save.</summary>
        public bool HasEverBuilt(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || EverBuiltStructureIds == null) return false;
            for (int i = 0; i < EverBuiltStructureIds.Count; i++)
                if (string.Equals(EverBuiltStructureIds[i], itemId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>A fresh List&lt;int&gt; of <paramref name="count"/> zeros.</summary>
        public static List<int> NewZeroed(int count)
        {
            var list = new List<int>(count);
            for (var i = 0; i < count; i++) list.Add(0);
            return list;
        }
    }
}
