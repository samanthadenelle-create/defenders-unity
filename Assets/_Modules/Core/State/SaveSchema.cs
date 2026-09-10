// =============================================================================
// SaveSchema — the strongly-typed save shape + validation (spec §2.5)
// -----------------------------------------------------------------------------
// The C# analog of src/store/saveSchema.ts (the Zod schema). Defines:
//   - the SaveFile envelope  (the React `SaveExport` — format/storeVersion/...)
//   - PersistedState         (the persisted payload, ~60 fields — every field
//                             nullable so a partial save deserializes, mirroring `.partial()`)
//   - Validate()             (the C# port of `safeParse` — rejects NaN/Infinity,
//                             clamps numerics via NonNegInt/FiniteInt rules)
//
// CurrentVersion: DO NOT restate it here — read the const below (it is the authority).
// This header said "CurrentVersion = 36" while the const read 38, two versions stale
// (audit F78, 2026-08-09): a doc-comment lying about the value 25 lines beneath it, in
// the most load-bearing constant in the save system. FileFormat = 1.
// (v38 = WO-934 army loadout bank; v37 = WO-911 the per-job PAID BASKET;
//  v36 appended everBuiltStructureIds — the
// WO-834 blank-founding baked-standdown ledger; v35 appended obsidianQueue — the
// WO-773 common multi-channel work queue — see the CurrentVersion const changelog.)
// PlayerPrefs key
// `dotr-save` replaces the React localStorage key (storage layer mandated by the
// port spec — improvement #4 NOT adopted).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using DeNelle.Core.Jobs;

namespace DeNelle.Core.State
{
    /// <summary>
    /// Save-file constants, the persisted shape, the envelope and the validator.
    /// </summary>
    public static class SaveSchema
    {
        // ── Versioning ───────────────────────────────────────────────────────
        /// <summary>CURRENT_SCHEMA_VERSION — bumped whenever the persisted shape changes.</summary>
        public const int CurrentVersion = 41;  // v41 - WO-823 Phase E, the FIRST-RAID soft gate: everCompletedRaid (bool, additive, append-only at the END of PersistedState). While false the raid door asks for ArmyReadiness.FirstRaidMinDeployableSlots (3) deployable SLOTS instead of the full army cap; the flag is stamped by RaidDeployController.ReconcileRaidEnd on any raid exit and the requirement is the full cap from then on, permanently. The bump exists because the field must be DERIVED for existing players, not defaulted: a veteran save loading false would be handed a softened first raid it already earned past, and only the migrator can distinguish an existing save from a new one. MigrateToV41 derives it from the evidence a completed raid leaves behind - any troop at veterancyRank >= 1 (granted only by GrantVeterancy at a 3-star clear) or any non-empty raidCooldowns record (written only by RaidCooldownService at a clear). TWO DOCUMENTED GAPS, both FAIL-OPEN and self-healing after one raid: a veteran whose raids were all sub-3-star or lost AND whose camp cooldowns have expired and been pruned derives false and gets ONE extra softened raid; and Village2RaidController does not call ReconcileRaidEnd, so a first-ever stronghold raid does not clear the flag. Both cost a player nothing and are recorded rather than guessed at. New games start AT CurrentVersion, never run the migrator, and correctly begin at false. // v40 — WO-1235 the RECIPE UNLOCK GATE: no new wire field. The record rides the existing seenTutorials map under RecipeUnlockKeys.KeyPrefix, so the SHAPE is unchanged and the bump exists for ONE reason - the migrator is the only reliable way to tell an EXISTING player from a new one. MigrateToV40 GRANDFATHERS every RecipeUnlockKeys.GatedRecipeIds entry onto any save at v<=39, because those recipes were craftable by everyone before the gate and a live game must never have crafting taken away. New games start AT CurrentVersion, never run the migrator, and are correctly gated. // v39 — WO-911 paidCoins on BuildJobData, additive, defaults zero on legacy jobs. // v38 — WO-934 army loadout bank: ArmyStorage.loadouts (3 named composition presets) + activeLoadout index. Additive on nested Army JSON; MigrateToV38 EnsureLoadouts for empty slots. // v37 — WO-911 (M2) the PAID BASKET on a queue job: BuildJobData gains paidWood/paidFood/paidIron/paidCrystals/paidMagic — the resources actually CHARGED at commit, stamped by BuildTimerService at every enqueue seam. Precondition for the owner's Q1 ruling (cancel refunds 100% of what was paid, FLAT, regardless of elapsed time): the cost is NOT re-derivable at cancel time because BuildModeController.Place charges SoftcappedCostFor against the LIVE tower count and a first-build freebie charges nothing at all — re-deriving after the census moves refunds the wrong number (the exploit TowerPlacementSystem._prepaidCost was introduced to close). Additive default-on-read: absent on a pre-v37 save -> 0, so an in-flight legacy job cancels cleanly with a ZERO refund (traced, never silent). MigrateToV37 is a documented no-op that only records the default so the CORE_SAVE version-triple stays aligned (SaveMigrator top step == CurrentVersion). NOTE the queue DEPTH cap (5 per line, ruling Q4) is authored in BuildTimerConfig.queueDepthPerLine, NOT persisted — it is config, not save state — and the Echo-gated purchased slot reuses the EXISTING ChannelState.boughtSlots int (no expiry field: the TEMPORARY ad-unlocked slot of Q7 is deliberately NOT built here, because its expiry timestamp is shared SaveSchema territory with WO-912's rolling-window ad state and the two must be designed in ONE coordinated bump, not two competing rollover mechanisms). // v36 — WO-834 blank-founding baked standdown: everBuiltStructureIds (catalog ids the player has EVER committed a placement of; monotonic — selling never removes an id, which is what keeps the WO-819 sell->baked-twin-resurface contract alive). The single persisted input to StructureSingleton.MayBakedTwinSurface: on a StrategicPlacementMigrated save, a baked twin may only surface for ids in this set, so a "Build Your Own" founding (ResetToNewGame: marker true + this set empty) shows a truly BLANK town while a Default-Town/legacy save (granted its template ids by the StrategicPlacementMigration writer / the v35->v36 migrator) is unchanged. MigrateToV36 seeds it for existing saves: BaseLayout itemIds UNION FreeBuildsUsed (an id there was placed at least once — covers placed-then-sold singletons) UNION, when BaseLayout is non-empty (an established town), the frozen default-town template snapshot (census storefronts/stations + barracks) so existing towns keep today's Lever-1 pre-stand + barracks-at-unlock verbatim. Additive nullable on the wire; absent -> GameState's empty-list initializer. Append-only field at the END so older saves stay loadable. // v35 — WO-773 common "Obsidian" multi-channel work queue: obsidianQueue (ObsidianQueueState — per-channel Builder/Train/Research pools, each with active jobs ≤ slots + a FIFO pending queue + purchased-slot count). The single home ALL timed work flows through (build/repair/upgrade/tier-unlock/learn-magic/train-troop/tower/wall). The v34→v35 migrator (MigrateToV35) FOLDS the legacy timed-state into the BUILDER channel — existing buildJobs (Kind backfilled from jobType), any pendingBuilds (→ TowerBuild jobs) and future-dated buildingCooldowns (→ Build jobs) become ObsidianJobs (BuildJobData carries a new kind + channel), then the legacy lists are cleared — so no in-flight build is lost and the queue is the single source of truth going forward (buildJobs is retained on the wire for back-compat but no longer read at runtime). BuildJobData gains kind + channel (both additive default-on-read: absent → Build / Builder). Additive-default-on-read (nullable obsidianQueue on the wire; absent → GameState's Empty() initializer). Append-only field at the END of PersistedState so older saves stay loadable. // v34 — REDS #3/#4 persistence gaps closed (one coordinated bump): four in-memory-only fields now round-trip to the signed/migrated save. (a) tribes (WO-160 roaming-raider records: members-remaining / cleared / clear-count / last-seen) — List<TribeState>; (b) wards (WO-112 relit ward-stones + earned exploration reach) — List<WardStoneState>; (c) arena (ARENA MVP W/L ledger: wins/losses/streak/totalPurse) — nullable ArenaProgress struct, distinct from the already-persisted arenaDefense placed-defender layout (v19); (d) petActiveSlots (flag_17 — PetAcquisitionService's slotIndex->species deploy map, previously runtime-only and rebuilt from StarterPetId alone in SyncSlotsFromState, so a multi-slot pet roster reset on reload) — List<string> (null entry = an empty slot). All four are additive default-on-read (nullable on the wire; absent -> GameState's initializer, so an old save loads empty tribes/wards/slots + a zeroed arena = exactly the prior in-memory-only behaviour) and the v33->v34 migrator step seeds them for a clean round-trip + to keep the CORE_SAVE version-triple aligned (SaveMigrator top step == CurrentVersion). Append-only fields at the END of PersistedState so older saves stay loadable. v33 — WO-738 echo per-echo agency + specialization: the echoLanes per-echo token grammar is enriched from a bare lane ("wood,iron,idle") to "lane:level" ("harvest:3,idle,crafting:1"). SAME wire field (echoLanes, string) so this is a NO-MIGRATOR bump — additive default-on-read: a bare legacy token reads as its functional lane at level 1 (wood/iron/food -> Harvest; idle -> Idle; any token with no :level -> level 1), so an old save's echoLanes="wood" loads Harvest/Lv1 unchanged. New Game seeds the starter echo "harvest:1". v32 — first-build freebies (owner ruling 2026-07-13 evening): freeBuildsUsed (List<string> of catalog itemIds whose one-time FREE first placement has been consumed; the flag burns at the committed placement and never resets — selling/destroying does not restore it). REPLACES the 650w/385i founding resource seed (StartingBudget zeroed): players earn everything beyond the one-free-each kit from production, which prevents all-defense-no-town. Additive default-on-read, no migrator step (nullable on the wire; absent → GameState's empty-list initializer = an old save gains its FULL freebies, the partyMemberIds precedent). v31 — WO-681/658 echo gather-lane assignments: echoLanes (per-Echo lane CSV, index 0 = the starter Echo; additive default-on-read, migrator seeds "wood" when null = exactly the prior hardwired starter-Echo behaviour). v30 — WO-673 strategic-placement migration marker: strategicPlacementMigrated (bool, default false) records that the ONE-SHOT migration writer has converted the auto-placed functional structures (baked ring storefronts + runtime crafting stations) into BaseLayout records — the marker gates BOTH the injector/bake standdown AND the BaseLayoutLoader replay of those records (mutual exclusion, no double-spawn; docs/WO673_ARCHITECTURE_REVIEW.md §3). Additive default-on-read: an old save loads false = bakes/injectors own everything, exactly the prior behaviour; the flag-gated migration flips it once. v29 — F8-47 hero level/XP persistence: heroLevel + heroXp + heroLifetimeXp survive save/load (root cause: the hero's level lived ONLY on the in-memory HeroProgression MonoBehaviour, so clearing the challenge outpost and porting home re-attached a fresh level-1 component; all additive default-on-read — an old save loads level = 1 / xp = 0, exactly the prior fresh-run behaviour); v28 — WO-587 Population & Echo growth: populationXp + populationQuests + populationOutposts + populationEchoSlots survive save/load (the milestone-driven Echo workforce slot unlocks; all additive nonNegInt default-on-read — an old save loads xp/quests/outposts = 0 and echoSlots = 1 = the starter Wood echo, unchanged); v27 — wall-mounted defense seating: PlacedStructureData gains worldY (seat height) + wallMounted (high-ground perk flag) so a defense placed on a wall-walk persists on the wall TOP, not y=0 (both additive default-on-read; an old baseLayout record loads worldY=0/wallMounted=false = ground placement, unchanged); v26 — WO-543 accessory equip persistence: equippedRingId + equippedAmuletId (rings/amulets) survive save/load (empty = none); v25 — Echo Workforce V1 (ECHO_WORKFORCE_SPEC): echoCount + siloResources + wavesCompleted survive save/load → the farm faucet (Echoes auto-fill a pooled silo online+offline via OfflineHarvestService's clock, Dump banks to bins, beating 5 waves unlocks the next Echo ≤4); v24 — Village/Stronghold tier + owned building-research perks (WO-432: WC3 tech-gate at the Heart + per-building Gold-cost research survives save/load → compiled into GameModifiers); v23 — building upgrade tiers (WO-430: per-building tier 0-4 survives save/load → compiled into GameModifiers + dialogue level title); v22 — army roster persistence (WO-453: owned troops + cap + wounded/recovery/veterancy survive save/load); v21 — node-settlement persistence (WO-159: claim/HP/phase + 3-day razed lockout survive save/load); v20 — gear inventory persistence (shop purchases survive reload + Neon sync); v19 — arenaDefense placed-defender layout (WO-389); v18 — fold AetherCrystals into Resources.Crystals (single-source-of-truth); v17 — zone graph persistence (WO-164); v16 — party roster (WO-301); v15 — magic tech-axis currency (DEF-121/WO-230); v14 — baseLayout (WO-108); v13 — buildJobs + adSkip (WO-172)
        /// <summary>SaveExport.format — bumped only if the envelope shape changes.</summary>
        public const int FileFormat = 1;

        // ── Storage keys ─────────────────────────────────────────────────────
        /// <summary>PlayerPrefs key holding the live save (React localStorage 'dotr-save').</summary>
        public const string PlayerPrefsKey = "dotr-save";
        /// <summary>Legacy standalone audio/difficulty store key — read+removed by v8→v9.</summary>
        public const string LegacySettingsKey = "realm-defenders-settings";

        // ── Engine constants the save layer needs ────────────────────────────
        /// <summary>Id of the starter dungeon, seeded discovered from a fresh save.</summary>
        public const string StarterDungeonId = "healers_cottage";

        /// <summary>
        /// Shared Newtonsoft settings — registers the global string-enum converter
        /// and the TutorialStep converter so every save round-trips to the EXACT
        /// React wire strings. Used by both Save() and Load() in the service.
        /// </summary>
        public static JsonSerializerSettings JsonSettings
        {
            get
            {
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include,
                    Formatting = Formatting.None,
                    // LB-3 hardening: cap nesting depth so a maliciously deep
                    // JSON blob (save OR backend load) can't blow the stack /
                    // pin the CPU during parse. 64 is far beyond any real save
                    // shape (deepest real nesting is a few levels).
                    MaxDepth = 64,
                };
                // StringEnumConverter honours [EnumMember] — kebab/lowercase wire values.
                settings.Converters.Add(new StringEnumConverter());
                settings.Converters.Add(new TutorialStepConverter());
                return settings;
            }
        }

        // =====================================================================
        //  Save integrity (LB-3) — keyed HMAC-SHA256 over the serialized save
        // ---------------------------------------------------------------------
        //  The local save is plaintext PlayerPrefs JSON; nothing stopped a player
        //  from editing the blob (resources.*, ownedItemIds, …) and relaunching to
        //  load it as truth. We now write a keyed HMAC ALONGSIDE the payload (a
        //  sibling PlayerPrefs key "<slot>.sig") and verify it on load. A mismatch
        //  is rejected — the tampered blob never reaches ApplyPersisted.
        //
        //  THREAT-MODEL HONESTY: the HMAC key is EMBEDDED in the client binary, so
        //  this is best-effort OBFUSCATION, not cryptographic authority. A
        //  determined attacker who reverse-engineers the build can extract the key
        //  and forge a signature. The REAL anti-cheat authority is the server-side
        //  record (LB-1) — this layer raises the bar past trivial PlayerPrefs
        //  editing for the offline-only player and catches accidental corruption.
        // =====================================================================

        /// <summary>Sibling-key suffix holding a save slot's integrity signature.</summary>
        public const string SignatureKeySuffix = ".sig";

        // ── WO-1688: the ONE-GENERATION WIPE-UNDO SLOT ────────────────────────
        // Declared HERE, beside SignatureKeySuffix, for the reason the WO states
        // outright: the backup slot's name must be DERIVED from PlayerPrefsKey, never
        // written as a second literal key somewhere else. A second "dotr-save.prev"
        // literal in another file is the same duplicated-state failure that has cost
        // this repo a stale WO-number block and a retired dependency table - it goes
        // stale the moment PlayerPrefsKey is ever repointed, and the wipe-undo then
        // writes to a slot nothing reads. SaveBackupService composes
        // PlayerPrefsKey + BackupKeySuffix and is the only place that does.
        /// <summary>Suffix of the one-generation pre-reset backup slot (WO-1688).
        /// Compose as <c>PlayerPrefsKey + BackupKeySuffix</c> - never hardcode the key.</summary>
        public const string BackupKeySuffix = ".prev";

        /// <summary>
        /// The client-embedded HMAC key. Assembled from fragments so it is not a
        /// single grep-able literal in the binary (obfuscation only — see the
        /// integrity-block header; the server is the real authority).
        /// </summary>
        private static byte[] IntegrityKey()
        {
            // Fragments interleaved so the assembled key never appears verbatim.
            var parts = new[] { "dotr", "save", "integrity", "v1", "9f3c7a", "Elarion", "hmac" };
            return Encoding.UTF8.GetBytes(string.Join(":", parts));
        }

        /// <summary>Lowercase-hex HMAC-SHA256 of <paramref name="payload"/> under the embedded key.</summary>
        public static string ComputeSignature(string payload)
        {
            using var h = new HMACSHA256(IntegrityKey());
            var hash = h.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// True when <paramref name="sig"/> is the valid signature for
        /// <paramref name="payload"/>. Constant-time compare (no early-out leak).
        /// An empty/missing signature is NOT valid (caller handles the legacy
        /// unsigned-save migration separately).
        /// </summary>
        public static bool VerifySignature(string payload, string sig)
        {
            if (string.IsNullOrEmpty(sig)) return false;
            var expected = ComputeSignature(payload);
            if (expected.Length != sig.Length) return false;
            var diff = 0;
            for (var i = 0; i < expected.Length; i++) diff |= expected[i] ^ sig[i];
            return diff == 0;
        }

        // ── Atomic single-key integrity envelope (replaces the LB-3 sibling ".sig"
        //    key). The HMAC was originally written to a SEPARATE PlayerPrefs slot,
        //    so a crash/power-loss BETWEEN the two writes (or two concurrent writers)
        //    could leave the payload and signature out of sync → a VALID save then
        //    rejected as "tampered" → silent save loss. Folding the 64-hex signature
        //    into the FRONT of the stored value makes the write a SINGLE atomic
        //    Provider.Write, so payload+sig can never tear apart.
        //    Layout:  <64-hex-sig>\n<json-payload>
        //    A legacy unsigned save (raw JSON, no hex+newline prefix) is detected by
        //    the absence of this prefix and migrated once on load.

        private const int SignatureHexLen = 64; // HMAC-SHA256 => 32 bytes => 64 hex chars

        /// <summary>Wrap a JSON payload with its HMAC as a single atomically-writable value.</summary>
        public static string EmbedSignature(string json)
        {
            return ComputeSignature(json) + "\n" + (json ?? string.Empty);
        }

        /// <summary>
        /// Split a stored value written by <see cref="EmbedSignature"/>. Returns the
        /// inner JSON payload. <paramref name="signaturePresent"/> is false for a
        /// legacy unsigned save (raw JSON) — the caller loads it once and re-signs.
        /// <paramref name="signatureValid"/> is true only when a present signature
        /// verifies against the payload (false = tamper/corruption → reject).
        /// </summary>
        public static string TryExtractSigned(string stored, out bool signaturePresent, out bool signatureValid)
        {
            signaturePresent = false;
            signatureValid = false;
            if (string.IsNullOrEmpty(stored)) return stored;

            // A signed value is exactly: 64 lowercase-hex chars, a '\n', then JSON.
            if (stored.Length > SignatureHexLen + 1 && stored[SignatureHexLen] == '\n')
            {
                var sig = stored.Substring(0, SignatureHexLen);
                if (IsLowerHex(sig))
                {
                    signaturePresent = true;
                    var json = stored.Substring(SignatureHexLen + 1);
                    signatureValid = VerifySignature(json, sig);
                    return json;
                }
            }
            // Legacy unsigned save (raw JSON) — return as-is for one-time migration.
            return stored;
        }

        private static bool IsLowerHex(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        // =====================================================================
        //  SaveFile — the SaveExport envelope (the live save + downloadable file)
        // =====================================================================

        /// <summary>
        /// The save-file envelope — the React <c>SaveExport</c>. Stored verbatim
        /// in PlayerPrefs (wallet/exportedAt are harmless extra metadata on the
        /// live save, and keep one shape across live-save and exported-file).
        /// </summary>
        [Serializable]
        public sealed class SaveFile
        {
            /// <summary>File format version — NOT the schema version. Always 1.</summary>
            [JsonProperty("format")] public int Format = FileFormat;
            /// <summary>Mirrors CURRENT_SCHEMA_VERSION so imports can pick the migrate target.</summary>
            [JsonProperty("storeVersion")] public int StoreVersion = CurrentVersion;
            /// <summary>ISO-8601 timestamp the save was written.</summary>
            [JsonProperty("exportedAt")] public string ExportedAt;
            /// <summary>Wallet the save is tagged to, or null.</summary>
            [JsonProperty("wallet")] public string Wallet;
            /// <summary>The persisted payload (~60 fields; was 41 at v10).</summary>
            [JsonProperty("state")] public PersistedState State = new PersistedState();
        }

        // =====================================================================
        //  PersistedState — the persisted payload, ~60 fields (persistedStateSchema)
        // =====================================================================

        /// <summary>
        /// The persisted slice of GameState — every field nullable so a partial
        /// save (older/newer build) still deserializes, mirroring Zod's
        /// <c>.partial()</c>. Newtonsoft drops unknown extra keys (e.g. the stale
        /// <c>prepTimerLocked</c>), matching <c>.partial()</c>'s tolerance.
        /// </summary>
        [Serializable]
        public sealed class PersistedState
        {
            [JsonProperty("pets")] public List<PetData> Pets;
            [JsonProperty("starterPetId")] public string StarterPetId;
            // Audit P2 (save-load): the player-named starter pet (WO-277) must round-trip;
            // additive-default-on-read — null on old saves keeps the SO's value.
            [JsonProperty("petName")] public string PetName;
            [JsonProperty("onboarded")] public bool? Onboarded;
            [JsonProperty("bestWave")] public double? BestWave;
            [JsonProperty("resources")] public ResourceBalance? Resources;
            [JsonProperty("ownedItemIds")] public List<string> OwnedItemIds;
            [JsonProperty("petBonds")] public List<double> PetBonds;
            [JsonProperty("voidshards")] public double? Voidshards;
            [JsonProperty("towers")] public List<double> Towers;
            [JsonProperty("towerAbilities")] public List<double> TowerAbilities;
            [JsonProperty("wallLevel")] public double? WallLevel;
            [JsonProperty("stone")] public double? Stone;
            [JsonProperty("iron")] public double? Iron;
            [JsonProperty("wood")] public double? Wood;
            [JsonProperty("buildingCooldowns")] public Dictionary<string, double> BuildingCooldowns;
            [JsonProperty("pendingBuilds")] public List<PendingTowerBuild> PendingBuilds;
            [JsonProperty("tutorialStep")] public TutorialStep? TutorialStep;
            [JsonProperty("joystickSensitivity")] public double? JoystickSensitivity;
            [JsonProperty("movementStyle")] public MovementStyle? MovementStyle;
            [JsonProperty("muted")] public bool? Muted;
            [JsonProperty("musicVolume")] public double? MusicVolume;
            [JsonProperty("sfxVolume")] public double? SfxVolume;
            [JsonProperty("difficulty")] public Difficulty? Difficulty;
            [JsonProperty("voiceOvers")] public bool? VoiceOvers;
            [JsonProperty("ownedPets")] public List<PetSpecies> OwnedPets;
            [JsonProperty("seenTutorials")] public Dictionary<string, bool> SeenTutorials;
            [JsonProperty("boundWallet")] public string BoundWallet;
            [JsonProperty("heroClass")] public HeroClass? HeroClass;
            [JsonProperty("inventory")] public AtbInventory? Inventory;
            [JsonProperty("gearInventory")] public Dictionary<string, int> GearInventory;  // v20 — shop-bought gear counts (persists + Neon-syncs); null on old saves → defaults empty
            [JsonProperty("atbLossStreak")] public double? AtbLossStreak;
            [JsonProperty("breachStyle")] public BreachStyle? BreachStyle;
            [JsonProperty("buildingDamage")] public Dictionary<string, double> BuildingDamage;
            [JsonProperty("dungeons")] public DungeonProgress Dungeons;
            [JsonProperty("activeDungeonRun")] public ActiveDungeonRun ActiveDungeonRun;
            [JsonProperty("quests")] public QuestProgress Quests;
            [JsonProperty("regions")] public RegionProgress Regions;
            [JsonProperty("myInviteCode")] public string MyInviteCode;
            [JsonProperty("contacts")] public List<ChatContact> Contacts;
            [JsonProperty("blockedCodes")] public List<string> BlockedCodes;
            [JsonProperty("inbox")] public List<ChatMessage> Inbox;
            [JsonProperty("lastInboxSyncAt")] public double? LastInboxSyncAt;

            // ── v11 — Tower Empowerment (DEPRECATED v18) ─────────────────────────
            /// <summary>
            /// DEPRECATED (save v18) — the legacy Aether Crystals balance was folded into
            /// <c>resources.crystals</c> (the single source of truth) by SaveMigrator
            /// MigrateToV18. The JsonProperty is KEPT (removing it is a breaking change)
            /// but is no longer written meaningfully — it serializes as 0. Read crystals
            /// from <c>resources.crystals</c>.
            /// </summary>
            [JsonProperty("aetherCrystals")] public double? AetherCrystals;

            // ── v12 — Offline harvest accrual (WO-115) ───────────────────────────
            /// <summary>
            /// Unix-ms of the last offline-harvest accrual claim. Nullable per the
            /// <c>.partial()</c> convention; absent on an older save → defaults to 0 on
            /// load (no retroactive haul), so no explicit migration step is needed
            /// (same additive-default-on-read pattern as <c>aetherCrystals</c>).
            /// </summary>
            [JsonProperty("lastHarvestClaimMs")] public double? LastHarvestClaimMs;

            // ── v13 — Build/upgrade timers + ad-skip (WO-172) ────────────────────
            /// <summary>
            /// In-flight construction/upgrade jobs. Absent on an older save → defaults
            /// to an empty list on load (no jobs), so no explicit migration step is
            /// needed (same additive-default-on-read pattern as <c>lastHarvestClaimMs</c>).
            /// </summary>
            [JsonProperty("buildJobs")] public List<BuildJobData> BuildJobs;

            /// <summary>Rewarded-ad build-skips used in the current local day (daily cap). Absent → 0.</summary>
            [JsonProperty("adSkipsUsedToday")] public double? AdSkipsUsedToday;

            /// <summary>Local-day key the ad-skip counter belongs to. Absent → null (counter resets on first claim).</summary>
            [JsonProperty("adSkipDayKey")] public string AdSkipDayKey;

            /// <summary>UTC day key of the last daily chest claim. Additive; absent means eligible.</summary>
            [JsonProperty("dailyChestDayKey")] public string DailyChestDayKey;

            // ── v14 — Player build mode base layout (WO-108) ─────────────────────
            /// <summary>
            /// The player's placed-structure base layout. Nullable per the
            /// <c>.partial()</c> convention; absent on an older save → the v13→v14
            /// migration step seeds it to an empty list (existing players keep the
            /// default VillageSceneBuilder village until they first build + save).
            /// </summary>
            [JsonProperty("baseLayout")] public List<PlacedStructureData> BaseLayout;

            // ── v15 — Magic tech-axis currency (DEF-121 / WO-230) ────────────────
            /// <summary>
            /// Magic — the building-upgrade tech-tree gating currency (NOT a harvestable).
            /// Nullable per the <c>.partial()</c> convention; absent on an older save →
            /// defaults to 0 on load (no retroactive grant), so no explicit migration
            /// step is needed (same additive-default-on-read pattern as <c>aetherCrystals</c>).
            /// </summary>
            [JsonProperty("magic")] public double? Magic;

            // ── v16 — Party roster (WO-301) ──────────────────────────────────────
            /// <summary>
            /// The persisted party roster — companion ids in join order. Nullable per
            /// the <c>.partial()</c> convention; absent on an older save → defaults to an
            /// empty party on load (no companions until one joins), so no explicit
            /// migration step is needed (same additive-default-on-read pattern as
            /// <c>baseLayout</c>/<c>magic</c>).
            /// </summary>
            [JsonProperty("partyMemberIds")] public List<string> PartyMemberIds;

            // ── v17 — Zone graph persistence (WO-164) ────────────────────────────
            /// <summary>
            /// Per-region zone records — discovery/clear flags, the neighbor graph, and
            /// the City/Horde destination tag (WO-164). Seeded from
            /// <see cref="DeNelle.Core.World.ZoneManager.DefaultZoneGraph"/> on a fresh
            /// save (or backfilled on load for a pre-v17 save). Append-only field at the
            /// END so older saves stay loadable. ZoneState stores its enum keys as NAMES
            /// (strings) deliberately, so it survives enum renumbering.
            /// </summary>
            [JsonProperty("zones")] public List<DeNelle.Core.World.ZoneState> Zones;

            // ── v19 — Arena defense placed-defender layout (WO-389) ──────────────
            /// <summary>
            /// The player's pre-placed Arena DEFENDERS — the CoC-style defense layer
            /// (a List of <see cref="PlacedDefenderData"/>, SEPARATE from the
            /// <c>baseLayout</c> base buildings). Nullable per the <c>.partial()</c>
            /// convention; absent on an older save → defaults to an empty list on load
            /// (no pre-placed defense until the player sets one up), so no explicit
            /// migration step is needed (same additive-default-on-read pattern as
            /// <c>magic</c>/<c>partyMemberIds</c>). Append-only field at the END so
            /// older saves stay loadable.
            /// </summary>
            [JsonProperty("arenaDefense")] public List<PlacedDefenderData> ArenaDefense;

            // ── v21 — Node-settlement persistence (WO-159) ───────────────────────
            /// <summary>
            /// Per-site node-settlement records (WO-159) — the claim/harvest/defend/
            /// deplete loop's persisted state: claim phase, defence HP, and the
            /// razed-site game-day lockout. Nullable per the <c>.partial()</c>
            /// convention; absent on an older save → the v20→v21 migration step seeds
            /// it to an empty list (no claimed settlements until the player builds one),
            /// so claims/HP/3-day lockout survive a save/load round-trip instead of
            /// evaporating. Append-only field at the END so older saves stay loadable.
            /// <see cref="DeNelle.Core.World.SettlementState"/> stores its region as the
            /// enum NAME (string) so it survives enum renumbering.
            /// </summary>
            [JsonProperty("settlements")] public List<DeNelle.Core.World.SettlementState> Settlements;

            // ── v22 — Army roster persistence (WO-453) ───────────────────────────
            /// <summary>
            /// The player's persisted ARMY (WO-453) — the owned-troop roster + cap +
            /// wounded/recovery/veterancy state. Nullable per the <c>.partial()</c>
            /// convention; absent on an older save → the v21→v22 migration step seeds it
            /// to a fresh empty cap-10 <see cref="ArmyStorage"/> (no owned troops until
            /// the player trains one), so the roster survives a save/load round-trip.
            /// Append-only field at the END so older saves stay loadable. Loss model is
            /// wounded-recovery (no permadeath) — a downed troop is marked wounded and
            /// recovers, never deleted.
            /// </summary>
            [JsonProperty("army")] public ArmyStorage Army;

            // ── v23 — Building upgrade tiers (WO-430) ────────────────────────────
            /// <summary>
            /// Per-building upgrade TIER (WO-430 city upgrades), keyed by building id →
            /// tier 0-4 (e.g. {"armorer":2,"lumbermill":3}). 0/absent = not yet unlocked.
            /// The single source of truth that <see cref="DeNelle.Core.State.GameModifiers"/>
            /// is compiled from (ModifierService) and that the dialogue title + Yarn
            /// <c>$&lt;id&gt;_Level</c> vars read. Nullable per the <c>.partial()</c> convention;
            /// absent on an older save → the v22→v23 migration seeds an empty dict. Append-only
            /// field at the END so older saves stay loadable. (Folds in the resource-building
            /// levels previously kept loose in PlayerPrefs → one persisted source of truth.)
            /// </summary>
            [JsonProperty("buildingTiers")] public System.Collections.Generic.Dictionary<string, int> BuildingTiers;

            /// <summary>WO-432 (v24) — the global Village/Stronghold Tier (tech-gate). Absent on older
            /// saves → defaults to 0 (no research gated open). Append-only at the END.</summary>
            [JsonProperty("villageTier")] public int VillageTier;

            /// <summary>WO-432 (v24) — owned building-research perks, keyed "buildingId:perkId". Absent
            /// on older saves → the v23→v24 migration seeds an empty list. Append-only at the END.</summary>
            [JsonProperty("ownedBuildingPerks")] public System.Collections.Generic.List<string> OwnedBuildingPerks;

            // ── v25 — Echo Workforce V1 (ECHO_WORKFORCE_SPEC) ────────────────────
            /// <summary>
            /// Number of owned Echo workers (the farm faucet). Nullable per the
            /// <c>.partial()</c> convention; absent on an older save → defaults to 1 on
            /// load (the starter Echo) via the v24→v25 migration. The silo clock reuses
            /// <c>lastHarvestClaimMs</c>. Append-only at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("echoCount")] public double? EchoCount;

            /// <summary>
            /// Pooled silo buffer (fractional resources accrued, pre-Dump). Nullable;
            /// absent on an older save → defaults to 0 on load (empty silo). Append-only.
            /// </summary>
            [JsonProperty("siloResources")] public double? SiloResources;

            /// <summary>
            /// Total waves cleared across the save (the Echo-unlock counter). Nullable;
            /// absent on an older save → defaults to 0 on load. Append-only at the END.
            /// </summary>
            [JsonProperty("wavesCompleted")] public double? WavesCompleted;

            // ── v26 — WO-543 accessory equip persistence ─────────────────────────
            /// <summary>
            /// The equipped RING accessory id ("" / null = nothing equipped). Nullable per the
            /// <c>.partial()</c> convention; absent on an older save → the v25→v26 migration seeds
            /// "". Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("equippedRingId")] public string EquippedRingId;

            /// <summary>
            /// The equipped AMULET accessory id ("" / null = nothing equipped). See
            /// <see cref="EquippedRingId"/>. Append-only field at the END.
            /// </summary>
            [JsonProperty("equippedAmuletId")] public string EquippedAmuletId;

            // ── v28 — WO-587 Population & Echo growth ────────────────────────────
            /// <summary>
            /// Accumulated Population XP. Nullable per the <c>.partial()</c> convention; absent on
            /// an older save → defaults to 0 on load via the v27→v28 migration. Append-only at the END.
            /// </summary>
            [JsonProperty("populationXp")] public double? PopulationXP;

            /// <summary>Cumulative completed quests counted toward Population milestones (v28; absent → 0).</summary>
            [JsonProperty("populationQuests")] public double? PopulationQuests;

            /// <summary>Cumulative cleared outposts counted toward Population milestones (v28; absent → 0).</summary>
            [JsonProperty("populationOutposts")] public double? PopulationOutposts;

            /// <summary>
            /// Highest Echo workforce slot unlocked by Population milestones (1..5). Absent on an
            /// older save → defaults to 1 (the starter echo) via the v27→v28 migration. Append-only at the END.
            /// </summary>
            [JsonProperty("populationEchoSlots")] public double? PopulationEchoSlots;

            // ── v29 — F8-47 hero level/XP persistence ────────────────────────────
            /// <summary>
            /// The hero's current LEVEL (F8-47). Previously lived only on the in-memory
            /// HeroProgression MonoBehaviour, so it reset to 1 whenever a scene load
            /// attached a fresh component (e.g. porting home from the challenge outpost).
            /// Nullable per the <c>.partial()</c> convention; absent on an older save →
            /// defaults to 1 (a fresh hero) via the v28→v29 migration. Append-only at the END.
            /// </summary>
            [JsonProperty("heroLevel")] public double? HeroLevel;

            /// <summary>
            /// XP banked toward the hero's next level (F8-47; fractional — kill-XP shares are
            /// floats, like <see cref="SiloResources"/>). Absent on an older save → 0. Append-only.
            /// </summary>
            [JsonProperty("heroXp")] public double? HeroXp;

            /// <summary>
            /// Total XP the hero has ever earned (F8-47; telemetry/UI counter). Absent on an
            /// older save → 0. Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("heroLifetimeXp")] public double? HeroLifetimeXp;

            // ── v30 — WO-673 strategic-placement migration marker ────────────────
            /// <summary>
            /// True once the ONE-SHOT WO-673 migration writer has converted the auto-placed
            /// functional structures (baked ring storefronts + runtime crafting stations) into
            /// BaseLayout records. The marker gates BOTH sides of the ownership handover:
            /// false → bakes/injectors own the structures (today's behaviour) and the loader
            /// withholds migration-managed records; true → the records replay and the bakes
            /// stand down (SetActive(false) / injector skip). Nullable per the <c>.partial()</c>
            /// convention; absent on an older save → the v29→v30 migration seeds false.
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("strategicPlacementMigrated")] public bool? StrategicPlacementMigrated;

            // ── WO-681/658 — Echo assignments (WO-738 lane:level, WO-830 resource,
            //    WO-811 repair task) ─
            /// <summary>
            /// Per-Echo assignment CSV by echo index. TOKEN GRAMMAR (v33 base, extended
            /// additively by WO-830 and WO-811 — SAME wire shape, NO schema bump, NO migrator):
            /// <c>idle</c> (no level, no resource); <c>&lt;lane&gt;:&lt;level&gt;</c> with lane in
            /// harvest/crafting/defense/exploration/repair (repair = the WO-811 structure-repair
            /// task; an OLDER build reads the then-unknown token as Idle — the standing
            /// unknown-token default, no crash/corruption); or the WO-830 PRIMARY form
            /// <c>&lt;resource&gt;:&lt;level&gt;</c> with resource in wood/iron/food/gold/crystals —
            /// a HARVEST assignment carrying the player-picked resource (e.g.
            /// <c>"wood:3,idle,crystals:1"</c>). Written by the Echo resource picker + level
            /// API (EchoAssignments); the rate/dump split + EchoLaneBonuses recompute consume
            /// the same field. Nullable per the <c>.partial()</c> convention; absent on an
            /// older save → GameState's initializer default applies on read.
            /// BACKWARD-COMPATIBLE READ (default-on-read, no migrator):
            /// a pre-v33 bare <c>wood</c>/<c>iron</c>/<c>food</c> → Harvest at that resource,
            /// level 1 (the resource vocabulary predates v33 and is first-class again);
            /// a v33 generic <c>harvest:N</c> → Harvest at the echo's AFFINITY resource
            /// (EchoRosterCatalog default-on-read); <c>idle</c> → Idle; any token with no
            /// <c>:level</c> suffix → level 1; unknown tokens → Idle. So an old save's
            /// <c>echoLanes="wood"</c> loads Harvest/Wood/L1 unchanged. Append-only
            /// field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("echoLanes")] public string EchoLanes;

            // ── v32 — first-build freebies (owner ruling 2026-07-13 evening) ─────
            /// <summary>
            /// Catalog itemIds whose ONE-TIME free first build has been consumed
            /// (the flag burns at the committed placement, never resets). Nullable
            /// per the <c>.partial()</c> convention; absent on an older save →
            /// GameState's empty-list initializer applies on read = the old save
            /// gains its FULL freebies (correct — the freebies replace the retired
            /// wood/iron founding seed), so no migrator step is required (same
            /// additive-default-on-read pattern as <c>partyMemberIds</c>).
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("freeBuildsUsed")] public List<string> FreeBuildsUsed;

            // ── v34 — REDS #4: world-content persistence (tribes / wards / arena) ─
            /// <summary>
            /// Per-tribe roaming-raider records (WO-160) — members-remaining / cleared /
            /// clear-count / last-seen. In-memory-only until v34; now round-trips inside the
            /// signed save (mirrors <c>settlements</c>/<c>zones</c>: a JsonUtility-safe list of
            /// records whose region key is the enum NAME, so it survives enum renumbering).
            /// Nullable per the <c>.partial()</c> convention; absent on an older save → the
            /// v33→v34 migration seeds an empty list (no claimed tribe progress), so a
            /// half-cleared tribe no longer resets on reload. Append-only field at the END.
            /// </summary>
            [JsonProperty("tribes")] public List<DeNelle.Core.World.TribeState> Tribes;

            /// <summary>
            /// Per-ward relight records (WO-112) — the earned exploration reach. Each carries
            /// its ward id / region / granted-reach and whether the Keeper has relit it; the
            /// set of lit wards drives per-march reach (WardReach) and the forgetting effect.
            /// In-memory-only until v34; now round-trips (same shape/precedent as <c>tribes</c>).
            /// Nullable per the <c>.partial()</c> convention; absent on an older save → the
            /// v33→v34 migration seeds an empty list, so relit wards no longer reset on reload
            /// (the forgetting effect stops firing spuriously). Append-only field at the END.
            /// </summary>
            [JsonProperty("wards")] public List<DeNelle.Core.World.WardStoneState> Wards;

            /// <summary>
            /// The Arena (async-PvP) win/loss ledger (ARENA MVP) — wins / losses / streak /
            /// totalPurse. DISTINCT from <c>arenaDefense</c> (the placed-defender layout, v19):
            /// this is the W/L scoreboard, previously living ONLY in the loose
            /// <c>ArenaProgressStore</c> PlayerPrefs mirror, outside the signed/migrated/
            /// validated save. Nullable struct per the <c>.partial()</c> convention; absent on
            /// an older save → the v33→v34 migration seeds a zeroed <see cref="ArenaProgress.Empty"/>
            /// record. The int fields carry no NaN risk, so the validator leaves them untouched.
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("arena")] public ArenaProgress? Arena;

            /// <summary>
            /// The pet ACTIVE-SLOT deploy map (flag_17) — one entry per deploy slot in slot
            /// order, holding the canonical species id occupying it or <c>null</c> for an empty
            /// slot. PetAcquisitionService held this ONLY at runtime and rebuilt it from
            /// <c>starterPetId</c> alone on load (SyncSlotsFromState), so a multi-slot pet roster
            /// reset every reload. Now persisted so the exact slot→species assignment survives.
            /// Nullable per the <c>.partial()</c> convention; absent on an older save → the
            /// v33→v34 migration seeds an empty list and PetAcquisitionService falls back to the
            /// legacy starter-in-slot-0 rebuild (unchanged for pre-v34 saves). Append-only field
            /// at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("petActiveSlots")] public List<string> PetActiveSlots;

            // ── v35 — WO-773 common "Obsidian" multi-channel work queue ──────────
            /// <summary>
            /// The single multi-channel work queue (WO-773) — per-channel Builder/Train/Research
            /// pools, each with active jobs (≤ slots) + a FIFO pending queue + a purchased-slot
            /// count. The JOB RECORD is <see cref="BuildJobData"/> (the WO-172 offline-fair timer,
            /// now with a kind + channel). Nullable per the <c>.partial()</c> convention; absent on
            /// an older save → the v34→v35 migration builds it from the legacy
            /// buildJobs/pendingBuilds/buildingCooldowns (folded into the Builder channel), so no
            /// in-flight work is lost. Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("obsidianQueue")] public ObsidianQueueState ObsidianQueue;

            // ── WO-771.9 — Barracks & troop upgrade progression (additive, NO schema bump) ──
            /// <summary>
            /// The player's current Barracks LEVEL (WO-771.9) — drives troop unlock gating.
            /// Nullable per the <c>.partial()</c> convention; absent on an older save →
            /// GameState's initializer (1) applies on read, so NO migrator step + NO schema
            /// bump is needed (rides the committed v35 — same additive-default-on-read pattern
            /// as <c>freeBuildsUsed</c>/<c>echoLanes</c>). Append-only field at the END.
            /// </summary>
            [JsonProperty("barracksLevel")] public int? BarracksLevel;

            /// <summary>
            /// Per-troop upgrade LEVEL by troop id (WO-771.9). Nullable per the <c>.partial()</c>
            /// convention; absent on an older save → GameState's empty-dict initializer applies on
            /// read (every troop at baseline), so NO migrator step + NO schema bump. Append-only
            /// field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("troopLevels")] public Dictionary<string, int> TroopLevels;

            /// <summary>
            /// Per-instance gear power LEVEL by gear id (WO-808 Option A). Nullable per the
            /// <c>.partial()</c> convention; absent on an older save → GameState's empty-dict
            /// initializer applies on read (all gear at authored baseline), so NO migrator
            /// step + NO schema bump. Append-only field at the END.
            /// </summary>
            [JsonProperty("gearLevels")] public Dictionary<string, int> GearLevels;

            // ── v36 — WO-834 blank-founding baked standdown ──────────────────────
            /// <summary>
            /// Catalog itemIds the player has EVER committed a placement of (WO-834).
            /// MONOTONIC — ids are added at the BaseLayout commit seams
            /// (BuildModeController.Place, the StrategicPlacementMigration template grant)
            /// and never removed: selling keeps the id, which is exactly what keeps the
            /// WO-819 sell-&gt;baked-twin-resurface contract working. The persisted input to
            /// <c>StructureSingleton.MayBakedTwinSurface</c>: on a migrated save a baked
            /// twin may surface only for ids in this set, so a Build-Your-Own founding
            /// (empty set) is truly blank. Nullable per the <c>.partial()</c> convention;
            /// absent on an older save → the v35→v36 migration seeds it (BaseLayout ∪
            /// FreeBuildsUsed ∪ the template snapshot when the town is established).
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("everBuiltStructureIds")] public List<string> EverBuiltStructureIds;
            [JsonProperty("everAcquiredItemIds")] public List<string> EverAcquiredItemIds;

            // ── WO-1026 — PvE siege / defence reports (additive, NO schema bump) ──
            /// <summary>
            /// The ring-buffered defence-report ledger (WO-1026) — one record per attack ON
            /// THE PLAYER'S TOWN, capped at <c>DefenseReportLedger.MaxRetained</c>.
            ///
            /// ⚠ DELIBERATELY **NO SCHEMA BUMP**. Both WO-1026 fields are nullable on the wire
            /// per the <c>.partial()</c> convention, so an older save simply loads GameState's
            /// empty-list / 0 initializers — an older save loads as "this town has never been
            /// attacked", which is exactly what it means. (WO-1139 added <c>StakesLedger.Applied</c>
            /// on the nested record by the same rule: additive, default-on-read, false on an old
            /// wire, which is correct because nothing was ever taken under the interim.) This rides
            /// the committed v38 exactly like <c>barracksLevel</c>/<c>troopLevels</c>/
            /// <c>gearLevels</c> (WO-771.9/WO-808) and needs no migrator step. A version bump on
            /// a LIVE published game is an owner decision, and nothing here requires one:
            /// there is no field to rewrite and no old shape to reinterpret.
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("defenseReports")] public List<DeNelle.Core.Defense.DefenseOutcomeRecord> DefenseReports;

            /// <summary>
            /// Unix-ms of the last siege (WO-1026) — the siege cadence clock.
            ///
            /// ⛔ This is a SEPARATE clock from <c>lastHarvestClaimMs</c> ON PURPOSE, and the
            /// siege scheduler MUST NOT read or write that one. WO-1147 recorded what happens
            /// when three systems share the offline clock: a frame-order coin-flip in which
            /// offline Echo repair never accrued once. SiegeScheduler registers as an
            /// <c>IOfflineClaimConsumer</c> and converts the coordinator's window into siege
            /// PRESSURE, stamping only this field. Guarded finite below, next to
            /// <c>lastHarvestClaimMs</c>. Nullable; absent → GameState's 0 (a fresh cadence
            /// clock that seeds forward on first evaluation and produces NO retroactive siege).
            /// </summary>
            [JsonProperty("lastSiegeUnixMs")] public double? LastSiegeUnixMs;

            // ── WO-728 — per-camp raid cooldown (additive, NO schema bump) ────────
            /// <summary>
            /// One record per raid camp currently RECOVERING from a clear (WO-728): the
            /// scene-config id, the instant the window opened (unix-ms from the SERVER-ANCHORED
            /// clock seam, never DateTime.UtcNow), and how long it runs. Absence of a record =
            /// the camp is raidable now.
            ///
            /// ⚠ DELIBERATELY **NO SCHEMA BUMP**, exactly like the WO-1026 pair above and the
            /// <c>barracksLevel</c>/<c>troopLevels</c>/<c>gearLevels</c> precedent: nullable on
            /// the wire per the <c>.partial()</c> convention, so an older save loads GameState's
            /// empty-list initializer — every camp raidable, which is byte-identical to the
            /// behaviour before the cooldown existed. There is no field to rewrite and no old
            /// shape to reinterpret, so no migrator step is needed. A version bump on a LIVE
            /// published game is an OWNER decision and nothing here requires one.
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("raidCooldowns")] public List<RaidCooldownRecord> RaidCooldowns;

            // -- WO-823 Phase E -- the first-raid soft gate signal (v41) ---------
            /// <summary>
            /// True once this save has finished at least one raid by ANY exit (victory,
            /// retreat, hero death). Consumed ONLY by
            /// <c>DeNelle.Village.ArmyReadiness</c>: while false the raid door asks for
            /// 3 deployable slots instead of the full army cap; once true it asks for the
            /// full cap permanently.
            ///
            /// Nullable per the <c>.partial()</c> convention -- absent on a v40 payload
            /// simply leaves GameState's <c>false</c> initializer, which is FAIL-OPEN for
            /// the player (an easier first raid), never a lockout. <c>MigrateToV41</c>
            /// derives a better answer for existing saves from raid evidence.
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            [JsonProperty("everCompletedRaid")] public bool? EverCompletedRaid;

            // -- WO-1375 / PROGRAM_RAID_ECONOMY section 4 -- the raid victory counter -----
            /// <summary>
            /// Monotonic count of raids WON on this save. The single input to the section-4
            /// escalation ladder (target 2 after 3 wins, target 3 after 10, Iron Bastion after
            /// 20). Written in exactly one place -
            /// <c>RaidVictoryController.HandleVictory</c>, after its de-duplicating latch.
            ///
            /// <para>⛔ NO VERSION BUMP, and the reason is the <c>raidCooldowns</c> precedent
            /// directly above: this is a NEW nullable field with no old shape to reinterpret
            /// and no field to rewrite. Absent on an older payload simply leaves GameState's
            /// <c>0</c> initializer. A bump on a LIVE published game is an owner decision and
            /// nothing here requires one.</para>
            ///
            /// <para>⚠ AND A MIGRATOR STEP COULD NOT HELP ANYWAY. The evidence a veteran's wins
            /// leave behind is the <c>RaidClaimService</c> claim set, and that set lives in
            /// PlayerPrefs, not on this wire - a step here would have nothing to read. The
            /// backfill therefore runs on the Village side and latches on
            /// <c>raidVictoriesBackfilled</c>.</para>
            /// Append-only field at the END so older saves stay loadable.
            /// </summary>
            /// <remarks>
            /// <c>double?</c> and not <c>int?</c> deliberately, matching every other counter on
            /// this wire (<c>bestWave</c> :241, <c>wavesCompleted</c> :436, both re-read at
            /// source this session): the JSON layer must be able to ACCEPT a non-integer number
            /// and have <see cref="Validate"/> floor it, rather than throw during deserialization
            /// and lose the whole save.
            /// </remarks>
            [JsonProperty("raidVictories")] public double? RaidVictories;

            /// <summary>
            /// One-shot latch: the claim-flag backfill of <see cref="RaidVictories"/> has run on
            /// this save. Absent -> GameState's <c>false</c>, so an existing save backfills once
            /// at its next raid victory and never again. Append-only at the END.
            /// </summary>
            [JsonProperty("raidVictoriesBackfilled")] public bool? RaidVictoriesBackfilled;

            // -- WO-1598 -- the NEW GAME reset epoch ---------------------------------
            /// <summary>
            /// Monotonic reset counter — see <see cref="GameState.ResetEpoch"/> for the full
            /// reasoning. It rides the state document so it round-trips through Snapshot /
            /// ApplyPersisted like every other field; <c>GameStateService.BuildSaveBody</c>
            /// ALSO stamps it at the TOP level of the POST body, because the server's sanity
            /// guard reads the body, not the nested state, and <c>api/game/load.js</c> returns
            /// it top-level so a client can tell it is behind.
            ///
            /// <para>⛔ NO VERSION BUMP (the <c>raidVictories</c> precedent): a new nullable
            /// field, absent -> GameState's 0, which is exactly what a row that never declared
            /// an epoch stores. Old client + old row = equal epochs = today's behaviour.</para>
            ///
            /// <para><c>double?</c> and not <c>int?</c> for the same reason as
            /// <see cref="RaidVictories"/> and <c>bestWave</c>: the JSON layer must ACCEPT a
            /// non-integer number and let <see cref="Validate"/> floor it, rather than throw
            /// during deserialization and lose the whole save. Append-only at the END.</para>
            /// </summary>
            [JsonProperty("resetEpoch")] public double? ResetEpoch;
        }

        // =====================================================================
        //  Numeric clamp helpers — the C# port of the Zod nonNegInt / finiteInt
        // =====================================================================

        /// <summary>
        /// <c>nonNegInt</c> — requires a finite number, transforms to
        /// <c>max(0, floor(n))</c>. Throws <see cref="SaveValidationException"/>
        /// for NaN / ±Infinity.
        /// </summary>
        public static int NonNegInt(double n, string fieldPath)
        {
            RequireFinite(n, fieldPath);
            var floored = Math.Floor(n);
            return (int)Math.Max(0.0, floored);
        }

        /// <summary>
        /// <c>finiteInt</c> — requires a finite number, transforms to
        /// <c>floor(n)</c> (may be negative).
        /// </summary>
        public static long FiniteInt(double n, string fieldPath)
        {
            RequireFinite(n, fieldPath);
            return (long)Math.Floor(n);
        }

        /// <summary>Rejects NaN / ±Infinity (the Zod <c>Number.isFinite</c> refine).</summary>
        public static double RequireFinite(double n, string fieldPath)
        {
            if (double.IsNaN(n) || double.IsInfinity(n))
                throw new SaveValidationException(fieldPath, "must be a finite number");
            return n;
        }

        // =====================================================================
        //  Validate — the C# port of persistedStateSchema.safeParse
        // =====================================================================

        /// <summary>
        /// Validates and clamps a parsed <see cref="PersistedState"/> in place,
        /// mirroring <c>persistedStateSchema.safeParse</c>: rejects NaN/Infinity
        /// numerics, clamps via the NonNegInt/FiniteInt rules (§2.1), and reports
        /// the first bad field path on failure (mirrors the React
        /// "invalid field: &lt;path&gt;" toast). Volumes / joystick sensitivity
        /// are finite-checked only — the React schema does NOT clamp them on load.
        /// </summary>
        public static SaveValidationResult Validate(PersistedState raw)
        {
            if (raw == null)
                return SaveValidationResult.Failure("state", "missing payload");

            try
            {
                // ── Resources / currencies → nonNegInt ───────────────────────
                if (raw.Resources.HasValue)
                {
                    var r = raw.Resources.Value;
                    r.Crystals = NonNegInt(r.Crystals, "resources.crystals");
                    r.Food = NonNegInt(r.Food, "resources.food");
                    r.Coins = NonNegInt(r.Coins, "resources.coins");
                    raw.Resources = r;
                }
                if (raw.BestWave.HasValue) raw.BestWave = NonNegInt(raw.BestWave.Value, "bestWave");
                if (raw.Voidshards.HasValue) raw.Voidshards = NonNegInt(raw.Voidshards.Value, "voidshards");
                if (raw.Stone.HasValue) raw.Stone = NonNegInt(raw.Stone.Value, "stone");
                if (raw.Iron.HasValue) raw.Iron = NonNegInt(raw.Iron.Value, "iron");
                if (raw.Wood.HasValue) raw.Wood = NonNegInt(raw.Wood.Value, "wood");
                if (raw.WallLevel.HasValue) raw.WallLevel = NonNegInt(raw.WallLevel.Value, "wallLevel");
                if (raw.AtbLossStreak.HasValue) raw.AtbLossStreak = NonNegInt(raw.AtbLossStreak.Value, "atbLossStreak");
                if (raw.AetherCrystals.HasValue) raw.AetherCrystals = NonNegInt(raw.AetherCrystals.Value, "aetherCrystals");
                if (raw.Magic.HasValue) raw.Magic = NonNegInt(raw.Magic.Value, "magic");

                // ── Integer arrays → nonNegInt per entry ─────────────────────
                ClampNonNegList(raw.PetBonds, "petBonds");
                ClampNonNegList(raw.Towers, "towers");
                ClampNonNegList(raw.TowerAbilities, "towerAbilities");

                // ── Pets → level / xp nonNegInt ──────────────────────────────
                if (raw.Pets != null)
                {
                    for (var i = 0; i < raw.Pets.Count; i++)
                    {
                        var p = raw.Pets[i];
                        if (p == null) continue;
                        p.Level = NonNegInt(p.Level, $"pets.{i}.level");
                        p.Xp = NonNegInt(p.Xp, $"pets.{i}.xp");
                    }
                }

                // ── Inventory → nonNegInt ────────────────────────────────────
                if (raw.Inventory.HasValue)
                {
                    var inv = raw.Inventory.Value;
                    inv.Potions = NonNegInt(inv.Potions, "inventory.potions");
                    inv.ManaCrystals = NonNegInt(inv.ManaCrystals, "inventory.manaCrystals");
                    inv.Cleanses = NonNegInt(inv.Cleanses, "inventory.cleanses");
                    inv.Torches = NonNegInt(inv.Torches, "inventory.torches");
                    raw.Inventory = inv;
                }

                // ── Pending builds → slot/ability nonNegInt, finishAt finiteInt ─
                if (raw.PendingBuilds != null)
                {
                    for (var i = 0; i < raw.PendingBuilds.Count; i++)
                    {
                        var pb = raw.PendingBuilds[i];
                        pb.Slot = NonNegInt(pb.Slot, $"pendingBuilds.{i}.slot");
                        pb.Ability = NonNegInt(pb.Ability, $"pendingBuilds.{i}.ability");
                        pb.FinishAt = FiniteInt(pb.FinishAt, $"pendingBuilds.{i}.finishAt");
                        raw.PendingBuilds[i] = pb;
                    }
                }

                // ── ActiveDungeonRun → LootStash ints nonNegInt, startedAt finiteInt ─
                if (raw.ActiveDungeonRun != null)
                {
                    raw.ActiveDungeonRun.StartedAt =
                        FiniteInt(raw.ActiveDungeonRun.StartedAt, "activeDungeonRun.startedAt");
                    var loot = raw.ActiveDungeonRun.Loot;
                    if (loot != null)
                    {
                        loot.Crystals = NonNegInt(loot.Crystals, "activeDungeonRun.loot.crystals");
                        loot.Food = NonNegInt(loot.Food, "activeDungeonRun.loot.food");
                        loot.Coins = NonNegInt(loot.Coins, "activeDungeonRun.loot.coins");
                        loot.Stone = NonNegInt(loot.Stone, "activeDungeonRun.loot.stone");
                        loot.Iron = NonNegInt(loot.Iron, "activeDungeonRun.loot.iron");
                        loot.Wood = NonNegInt(loot.Wood, "activeDungeonRun.loot.wood");
                    }
                }

                // ── Chat messages → sentAt / readAt finiteInt ────────────────
                if (raw.Inbox != null)
                {
                    for (var i = 0; i < raw.Inbox.Count; i++)
                    {
                        var m = raw.Inbox[i];
                        if (m == null) continue;
                        m.SentAt = FiniteInt(m.SentAt, $"inbox.{i}.sentAt");
                        if (m.ReadAt.HasValue)
                            m.ReadAt = FiniteInt(m.ReadAt.Value, $"inbox.{i}.readAt");
                    }
                }
                if (raw.LastInboxSyncAt.HasValue)
                    raw.LastInboxSyncAt = FiniteInt(raw.LastInboxSyncAt.Value, "lastInboxSyncAt");
                if (raw.LastHarvestClaimMs.HasValue)
                    raw.LastHarvestClaimMs = FiniteInt(raw.LastHarvestClaimMs.Value, "lastHarvestClaimMs");
                // WO-1026 — the siege cadence clock rides the SAME finite guard as the harvest
                // clock (a NaN here would make every cadence comparison false forever = "the base
                // is never attacked", silently, which is the exact bug this WO closes).
                if (raw.LastSiegeUnixMs.HasValue)
                    raw.LastSiegeUnixMs = FiniteInt(raw.LastSiegeUnixMs.Value, "lastSiegeUnixMs");

                // ── Echo Workforce (v25) → counts nonNegInt; silo finite (float pool) ─
                if (raw.EchoCount.HasValue)
                    raw.EchoCount = NonNegInt(raw.EchoCount.Value, "echoCount");
                if (raw.SiloResources.HasValue)
                    RequireFinite(raw.SiloResources.Value, "siloResources");
                if (raw.WavesCompleted.HasValue)
                    raw.WavesCompleted = NonNegInt(raw.WavesCompleted.Value, "wavesCompleted");

                // -- Raid victory counter (WO-1375) -> nonNegInt -----------------------
                // Clamped like every other counter: a hand-edited or corrupted negative would
                // otherwise let the ladder run backwards, and NonNegInt also rejects NaN/Inf.
                if (raw.RaidVictories.HasValue)
                    raw.RaidVictories = NonNegInt(raw.RaidVictories.Value, "raidVictories");

                // -- Reset epoch (WO-1598) -> nonNegInt --------------------------------
                // Clamped for a reason the other counters do not have: this number decides
                // whether a SERVER row may overwrite the local town, so a NaN/Inf or a negative
                // arriving from a hand-edited or hostile payload must be floored to a real
                // integer BEFORE ApplyBackendState compares it, never compared as-is (every
                // comparison against NaN is false, which would silently disable the guard).
                if (raw.ResetEpoch.HasValue)
                    raw.ResetEpoch = NonNegInt(raw.ResetEpoch.Value, "resetEpoch");

                // ── Population growth (v28) → all counters nonNegInt ─────────────────
                if (raw.PopulationXP.HasValue)
                    raw.PopulationXP = NonNegInt(raw.PopulationXP.Value, "populationXp");
                if (raw.PopulationQuests.HasValue)
                    raw.PopulationQuests = NonNegInt(raw.PopulationQuests.Value, "populationQuests");
                if (raw.PopulationOutposts.HasValue)
                    raw.PopulationOutposts = NonNegInt(raw.PopulationOutposts.Value, "populationOutposts");
                if (raw.PopulationEchoSlots.HasValue)
                    raw.PopulationEchoSlots = NonNegInt(raw.PopulationEchoSlots.Value, "populationEchoSlots");

                // ── Hero level/XP (v29, F8-47) → level nonNegInt; xp finite (float pool) ─
                if (raw.HeroLevel.HasValue)
                    raw.HeroLevel = NonNegInt(raw.HeroLevel.Value, "heroLevel");
                if (raw.HeroXp.HasValue)
                    RequireFinite(raw.HeroXp.Value, "heroXp");
                if (raw.HeroLifetimeXp.HasValue)
                    RequireFinite(raw.HeroLifetimeXp.Value, "heroLifetimeXp");

                // ── Build jobs (WO-172) → startMs/durationMs finiteInt; counter nonNegInt ─
                if (raw.BuildJobs != null)
                {
                    for (var i = 0; i < raw.BuildJobs.Count; i++)
                    {
                        var j = raw.BuildJobs[i];
                        j.StartMs = FiniteInt(j.StartMs, $"buildJobs.{i}.startMs");
                        j.DurationMs = Math.Max(0, FiniteInt(j.DurationMs, $"buildJobs.{i}.durationMs"));
                        raw.BuildJobs[i] = j;
                    }
                }
                if (raw.AdSkipsUsedToday.HasValue)
                    raw.AdSkipsUsedToday = NonNegInt(raw.AdSkipsUsedToday.Value, "adSkipsUsedToday");

                // ── Obsidian queue (WO-773, v35) → per-channel job times finiteInt;
                //    boughtSlots nonNeg. Mirrors the buildJobs clamp above for every
                //    channel's active + pending list. ──────────────────────────────
                if (raw.ObsidianQueue != null && raw.ObsidianQueue.Channels != null)
                {
                    foreach (var kv in raw.ObsidianQueue.Channels)
                    {
                        var ch = kv.Value;
                        if (ch == null) continue;
                        ch.BoughtSlots = NonNegInt(ch.BoughtSlots, $"obsidianQueue.{kv.Key}.boughtSlots");
                        ch.TemporarySlotEndsAtUnixMs = Math.Max(0d,
                            FiniteInt(ch.TemporarySlotEndsAtUnixMs, $"obsidianQueue.{kv.Key}.temporarySlotEndsAtUnixMs"));
                        ClampJobList(ch.ActiveJobs, $"obsidianQueue.{kv.Key}.active");
                        ClampJobList(ch.PendingQueue, $"obsidianQueue.{kv.Key}.pending");
                    }
                }

                // ── Zones (WO-164) → default empty list (never null on disk) ─
                if (raw.Zones == null)
                    raw.Zones = new List<DeNelle.Core.World.ZoneState>();

                // ── Settlements (WO-159) → default empty list (never null on disk) ─
                if (raw.Settlements == null)
                    raw.Settlements = new List<DeNelle.Core.World.SettlementState>();

                // ── Volumes / joystick → finite-only (NOT clamped on load) ───
                if (raw.JoystickSensitivity.HasValue)
                    RequireFinite(raw.JoystickSensitivity.Value, "joystickSensitivity");
                if (raw.MusicVolume.HasValue) RequireFinite(raw.MusicVolume.Value, "musicVolume");
                if (raw.SfxVolume.HasValue) RequireFinite(raw.SfxVolume.Value, "sfxVolume");

                return SaveValidationResult.Success(raw);
            }
            catch (SaveValidationException ex)
            {
                return SaveValidationResult.Failure(ex.FieldPath, ex.Reason);
            }
        }

        private static void ClampNonNegList(List<double> list, string fieldPath)
        {
            if (list == null) return;
            for (var i = 0; i < list.Count; i++)
                list[i] = NonNegInt(list[i], $"{fieldPath}.{i}");
        }

        /// <summary>WO-773 — clamp a list of <see cref="BuildJobData"/> (startMs/durationMs finiteInt),
        /// mirroring the buildJobs clamp so the Obsidian queue channels round-trip safely.</summary>
        private static void ClampJobList(List<BuildJobData> list, string fieldPath)
        {
            if (list == null) return;
            for (var i = 0; i < list.Count; i++)
            {
                var j = list[i];
                j.StartMs = FiniteInt(j.StartMs, $"{fieldPath}.{i}.startMs");
                j.DurationMs = Math.Max(0, FiniteInt(j.DurationMs, $"{fieldPath}.{i}.durationMs"));

                // WO-911 v37 — the PAID BASKET is a refund input, so a corrupt/negative value would
                // mint resources on cancel. Clamp it exactly like every other economy number.
                j.PaidWood = NonNegInt(j.PaidWood, $"{fieldPath}.{i}.paidWood");
                j.PaidFood = NonNegInt(j.PaidFood, $"{fieldPath}.{i}.paidFood");
                j.PaidIron = NonNegInt(j.PaidIron, $"{fieldPath}.{i}.paidIron");
                j.PaidCrystals = NonNegInt(j.PaidCrystals, $"{fieldPath}.{i}.paidCrystals");
                j.PaidMagic = NonNegInt(j.PaidMagic, $"{fieldPath}.{i}.paidMagic");
                j.PaidCoins = NonNegInt(j.PaidCoins, $"{fieldPath}.{i}.paidCoins");

                list[i] = j;
            }
        }
    }

    /// <summary>Thrown internally by the clamp helpers; carries the bad field path.</summary>
    public sealed class SaveValidationException : Exception
    {
        public string FieldPath { get; }
        public string Reason { get; }

        public SaveValidationException(string fieldPath, string reason)
            : base($"invalid field: {fieldPath} ({reason})")
        {
            FieldPath = fieldPath;
            Reason = reason;
        }
    }

    /// <summary>The result of <see cref="SaveSchema.Validate"/> — the C# safeParse return.</summary>
    public sealed class SaveValidationResult
    {
        /// <summary>True when the payload validated cleanly.</summary>
        public bool Ok { get; private set; }
        /// <summary>The validated, clamped payload (only when <see cref="Ok"/>).</summary>
        public SaveSchema.PersistedState Data { get; private set; }
        /// <summary>First bad field path (only when not <see cref="Ok"/>).</summary>
        public string FieldPath { get; private set; }
        /// <summary>Why the field was rejected (only when not <see cref="Ok"/>).</summary>
        public string Reason { get; private set; }

        /// <summary>A human-readable failure reason mirroring the React import toast.</summary>
        public string Message => Ok
            ? null
            : $"Save file is corrupt or tampered (invalid field: {FieldPath}).";

        public static SaveValidationResult Success(SaveSchema.PersistedState data)
            => new SaveValidationResult { Ok = true, Data = data };

        public static SaveValidationResult Failure(string fieldPath, string reason)
            => new SaveValidationResult { Ok = false, FieldPath = fieldPath, Reason = reason };
    }
}
