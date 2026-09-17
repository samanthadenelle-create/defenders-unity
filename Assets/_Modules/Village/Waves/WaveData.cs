// =============================================================================
// WaveData — typed records + loaders for the canonical waves.json + enemies.json
// -----------------------------------------------------------------------------
// Port spec Part 4 (data-extraction protocol): the canonical wave schedule lives
// in StreamingAssets/Data/Canonical/waves.json and the village-enemy stat blocks
// in .../enemies.json. This file is the strongly-typed C# mirror — Newtonsoft.Json
// deserialises the JSON into these PLAIN records, and the WaveManager hydrates
// the wave loop from them.
//
// JSON is the source of truth (port spec Part 4 "JSON wins over ScriptableObject").
// These records are plain C# — no MonoBehaviour, no ScriptableObject — so they
// unit-test cleanly and the same shapes can be reused by future content.
//
// LOADER PATTERN: mirrors DeNelle.Dungeons.DungeonLayoutLoader and
// DeNelle.Wallet.PackCatalog — canonical JSON under Application.streamingAssetsPath,
// read via UniTask (async because Android packs StreamingAssets inside the APK),
// parsed by Newtonsoft.Json. See unity-decisions.md 2026-05-18 (Newtonsoft over
// JsonUtility) and 2026-05-22.
// =============================================================================

using System;
using System.IO;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    // =========================================================================
    //  enemies.json — the in-village wave roster (the Hollow Ones)
    // =========================================================================

    /// <summary>
    /// The AI archetype of a village enemy. Mirrors the port spec Part 3 enemy
    /// row ("Walker (default), Charger, Skirmisher"). v2-foundation Wave 1 ships
    /// <see cref="Walker"/> only; the others are authored ahead for later waves.
    /// </summary>
    public enum EnemyAiKind
    {
        /// <summary>Default — straight march toward the Heart, melee on contact.</summary>
        Walker = 0,
        /// <summary>Faster; shoulders past buildings rather than pathing around.</summary>
        Charger = 1,
        /// <summary>Fast; peels off the line to strike the weakest wall / gate.</summary>
        Skirmisher = 2,
    }

    /// <summary>
    /// One village-enemy stat block — the deserialised shape of an
    /// <c>enemies.json</c> entry. The Hollow Ones that march on Elarion.
    /// </summary>
    [Serializable]
    public sealed class EnemyDef
    {
        /// <summary>Stable id — the wave-data <c>type</c> field keys into this.</summary>
        [JsonProperty("id")] public string Id;
        /// <summary>Short canon name (e.g. "Hollow Walker").</summary>
        [JsonProperty("name")] public string Name;

        // ── Family / role / spawn (WO-316) ───────────────────────────────────
        /// <summary>
        /// Faction the enemy belongs to ("hollow" / "orc" / "troll"). Drives the
        /// runtime group composition — WaveManager builds a role-mix squad from the
        /// members of a family. Absent in JSON ⇒ "hollow" (back-compat default).
        /// </summary>
        [JsonProperty("family")] public string Family = "hollow";
        /// <summary>
        /// Combat archetype within the family ("grunt" / "brute" / "skirmisher" /
        /// "caster" / "elite"). Read by <see cref="EnemyRoleKind"/> to assign the
        /// spawned enemy's <see cref="EnemyRole"/>. Absent ⇒ "grunt".
        /// </summary>
        [JsonProperty("role")] public string Role = "grunt";
        /// <summary>
        /// Where this enemy appears ("wave" / "roam" / "camp" / "world"). A list;
        /// an enemy can do more than one. Absent ⇒ ["wave"] for back-compat.
        /// </summary>
        // ObjectCreationHandling.Replace is LOAD-BEARING, not style. Json.NET's default
        // (Auto) REUSES this field's existing list instance and APPENDS to it, so the
        // initializer's "wave" was never cleared: `["wave"]` deserialised to
        // ["wave","wave"] and `["roam","camp"]` to ["wave","roam","camp"]. Every enemy in
        // the catalog silently gained "wave" as a declared spawn context, so the data said
        // something no author wrote. Found 2026-08-09 when a spawn-context oracle printed
        // `declares spawn[wave,wave]` for a row whose JSON reads ["wave"].
        // Harmless at the time ONLY because nothing consumes the field - MembersOf's
        // spawnContext parameter has no caller that passes one - but a defaulted value
        // masquerading as authored data is exactly the class that bites later.
        [JsonProperty("spawn", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Spawn = new List<string> { "wave" };
        /// <summary>Display name shown on the HP bar / codex.</summary>
        [JsonProperty("displayName")] public string DisplayName;
        /// <summary>KayKit mesh key — resolved to Assets/Models/KayKit/enemies/&lt;key&gt;.glb.</summary>
        [JsonProperty("modelKey")] public string ModelKey;
        /// <summary>AI archetype token ("walker" / "charger" / "skirmisher").</summary>
        [JsonProperty("ai")] public string Ai = "walker";
        /// <summary>
        /// Air/ground traversal layer ("ground" / "flying") — the enemy side of the
        /// TD targeting matrix. Flyers can only be hit by anti-air (or "both") towers.
        /// Absent in JSON ⇒ "ground" (back-compat default — every existing enemy stays
        /// ground, so an all-ground board behaves exactly as before).
        /// </summary>
        [JsonProperty("movement")] public string Movement = "ground";
        /// <summary>Resolved max hit points (React global buffs already baked in).</summary>
        [JsonProperty("hp")] public float Hp = 50f;
        /// <summary>NavMeshAgent speed — world units per second.</summary>
        [JsonProperty("moveSpeed")] public float MoveSpeed = 2.5f;
        /// <summary>Damage one melee hit deals to a building / wall / gate.</summary>
        [JsonProperty("contactDamage")] public float ContactDamage = 6f;
        /// <summary>Seconds between melee hits while in contact.</summary>
        [JsonProperty("attackInterval")] public float AttackInterval = 1.3f;
        /// <summary>Approximate mesh height (world units) — sizes the agent + HP bar.</summary>
        [JsonProperty("height")] public float Height = 1.7f;
        /// <summary>True for the Necromancer — leads boss waves.</summary>
        [JsonProperty("boss")] public bool Boss;
        /// <summary>Codex flavour line — narrative-bible voice.</summary>
        [JsonProperty("flavor")] public string Flavor;

        // WO-1065: authored combat truth. Empty/unknown stays neutral; never infer
        // affinity from ids, names, models, families, biomes or regions.
        [JsonProperty("affinity")] public string Affinity;
        [JsonProperty("vulnerableTo", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> VulnerableTo = new List<string>();
        [JsonProperty("traits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Traits = new List<string>();

        // ── Tactical (DEF-83) ─────────────────────────────────────────────────
        /// <summary>Override NavMeshAgent speed (world units/sec). Defaults to MoveSpeed when absent in JSON.</summary>
        [Header("Tactical")]
        [JsonProperty("aggroRadius")] public float AggroRadius = 8f;
        /// <summary>Per-enemy stagger offset (seconds) the spawner reads when releasing a group.</summary>
        [JsonProperty("groupStaggerDelay")] public float GroupStaggerDelay = 0.1f;

        // ── Rewards (DEF-88 / DEF-32) ────────────────────────────────────────
        /// <summary>XP awarded directly to the hero on killing this enemy.</summary>
        [Header("Rewards")]
        [JsonProperty("xpReward")]    public int XpReward    = 15;
        /// <summary>WO-432/433 — GOLD (economy Coins) dropped on kill, the source that funds building
        /// research. 0 = use the XP-derived fallback in Enemy's death reward (so every enemy still pays).</summary>
        [JsonProperty("coinReward")]    public int CoinReward    = 0;
        /// <summary>
        /// WO-1103 — bounded kill-reward VARIANCE as a fraction (0.15 = +/-15%). At grant
        /// time the reward is rolled through <see cref="RollReward"/>:
        /// <c>value * (1 + Random.Range(-v, +v))</c>, rounded. Absent in JSON =&gt; 0 =
        /// deterministic (exact base value, no behavior change for un-migrated rows).
        /// TUNABLE, never a lock: the seeded 0.15 (regular) / 0.10 (boss) values in
        /// enemies.json are an opening balance the owner re-tunes per row in data.
        /// </summary>
        [JsonProperty("rewardVariance")] public float RewardVariance = 0f;

        /// <summary>
        /// WO-1103 RUNTIME-ONLY (never serialized): how many pack bodies this def's kill
        /// reward CARRIES. 1 for a normal enemy. The overworld hook leader carries the
        /// whole family's payout (followers pay 0 — owner ruling WO-1103 item 5, leader-
        /// carry KEPT), so its field-kill toast reads "Pack bounty" instead of a plain
        /// earned line. Set by OverworldEncounterSpawner.BuildOverworldHookDef.
        /// </summary>
        [JsonIgnore] public int PackBodies = 1;

        /// <summary>
        /// WO-1103 — the ONE reward-roll authority (single home for the base+variance
        /// formula; every grant site calls this, never re-implements it).
        /// Rolls <c>baseValue * (1 + Random.Range(-v, +v))</c> and rounds.
        /// <list type="bullet">
        /// <item><c>baseValue &lt;= 0</c> returns 0 (a non-paying def stays non-paying —
        /// variance can never mint a reward from nothing).</item>
        /// <item><c>variance &lt;= 0</c> returns the exact base (deterministic legacy path).</item>
        /// <item>Variance is clamped to [0, 0.9] so a bad data row can never zero or
        /// double a reward; a positive base never rolls below 1.</item>
        /// </list>
        /// </summary>
        public static int RollReward(int baseValue, float variance)
        {
            if (baseValue <= 0) return 0;
            float v = Mathf.Clamp(variance, 0f, 0.9f);
            if (v <= 0f) return baseValue;
            float rolled = baseValue * (1f + UnityEngine.Random.Range(-v, v));
            return Mathf.Max(1, Mathf.RoundToInt(rolled));
        }

        /// <summary>
        /// True when this enemy flies (<see cref="Movement"/> == "flying", case-
        /// insensitive). Drives the air/ground targeting matrix — only anti-air or
        /// "both" towers can fire at a flyer. Anything else (incl. an absent field)
        /// is ground.
        /// </summary>
        public bool IsFlying =>
            string.Equals((Movement ?? "ground").Trim(), "flying",
                System.StringComparison.OrdinalIgnoreCase);

        /// <summary>The typed air/ground <see cref="DeNelle.Core.Combat.CombatLayer"/> for this enemy.</summary>
        public DeNelle.Core.Combat.CombatLayer Layer =>
            IsFlying ? DeNelle.Core.Combat.CombatLayer.Flying
                     : DeNelle.Core.Combat.CombatLayer.Ground;

        /// <summary>The <see cref="Ai"/> token parsed to the typed enum.</summary>
        public EnemyAiKind AiKind
        {
            get
            {
                switch ((Ai ?? "walker").Trim().ToLowerInvariant())
                {
                    case "charger": return EnemyAiKind.Charger;
                    case "skirmisher": return EnemyAiKind.Skirmisher;
                    default: return EnemyAiKind.Walker;
                }
            }
        }

        /// <summary>
        /// WO-316: the JSON <see cref="Role"/> token mapped to the tactical
        /// <see cref="EnemyRole"/> the spawner assigns to <c>EnemyBrain.Role</c>.
        /// brute → Tank (soaks + screens), caster → Healer (the support to kill
        /// first), skirmisher → Ranged (fast flanker), elite → MiniBoss, everything
        /// else (grunt / unknown) → DPS. Drives the "tank + healer + DPS" mix.
        /// </summary>
        public EnemyRole RoleKind
        {
            get
            {
                switch ((Role ?? "grunt").Trim().ToLowerInvariant())
                {
                    case "brute":      return EnemyRole.Tank;
                    case "caster":     return EnemyRole.Healer;
                    case "skirmisher": return EnemyRole.Ranged;
                    case "elite":      return EnemyRole.MiniBoss;
                    default:           return EnemyRole.DPS;   // grunt / warrior / unknown
                }
            }
        }
    }

    /// <summary>The deserialised root of <c>enemies.json</c>.</summary>
    [Serializable]
    public sealed class EnemyCatalog
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("enemies")] public List<EnemyDef> Enemies = new List<EnemyDef>();

        /// <summary>Finds an enemy def by id, or null when the catalog has none.</summary>
        public EnemyDef Find(string id)
        {
            if (string.IsNullOrEmpty(id) || Enemies == null) return null;
            foreach (var e in Enemies)
                if (e != null && e.Id == id) return e;
            return null;
        }

        // ── WO-316: family / role composition helpers ─────────────────────────

        /// <summary>
        /// WO-316: every enemy def belonging to <paramref name="family"/> (case-
        /// insensitive). Used by WaveManager to compose a runtime role-mix squad —
        /// a tank + healer + a few DPS drawn from the same faction. Optionally
        /// filters to a given <paramref name="spawnContext"/> ("wave" / "roam" /
        /// "camp") so the village siege never pulls a camp-only elite. Never null.
        /// </summary>
        public List<EnemyDef> MembersOf(string family, string spawnContext = null)
        {
            var result = new List<EnemyDef>();
            if (Enemies == null || string.IsNullOrEmpty(family)) return result;

            foreach (var e in Enemies)
            {
                if (e == null) continue;
                string fam = string.IsNullOrEmpty(e.Family) ? "hollow" : e.Family;
                if (!string.Equals(fam, family, System.StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrEmpty(spawnContext))
                {
                    bool ok = false;
                    var spawn = e.Spawn ?? new List<string> { "wave" };
                    foreach (var s in spawn)
                        if (string.Equals(s, spawnContext, System.StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                    if (!ok) continue;
                }

                result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// WO-316: the first member of <paramref name="family"/> whose JSON role
        /// matches <paramref name="role"/> (e.g. brute → the tank, caster → the
        /// healer), or null when the family fields no such role. Lets a composer
        /// ask "give me this family's healer" without hard-coding ids.
        /// </summary>
        public EnemyDef FindByRole(string family, string role)
        {
            if (Enemies == null || string.IsNullOrEmpty(role)) return null;
            foreach (var e in MembersOf(family))
            {
                string r = string.IsNullOrEmpty(e.Role) ? "grunt" : e.Role;
                if (string.Equals(r, role, System.StringComparison.OrdinalIgnoreCase)) return e;
            }
            return null;
        }
    }

    // =========================================================================
    //  waves.json — the wave-spawn schedule
    // =========================================================================

    /// <summary>
    /// One spawn batch within a wave — a group of one enemy type released at a
    /// single spawn point. The deserialised shape of a <c>waves.json</c>
    /// <c>enemies[]</c> entry.
    /// </summary>
    [Serializable]
    public sealed class WaveBatch
    {
        /// <summary>Enemy id — keys into <see cref="EnemyCatalog"/>.</summary>
        [JsonProperty("type")] public string Type;
        /// <summary>How many enemies of <see cref="Type"/> this batch releases.</summary>
        [JsonProperty("count")] public int Count = 1;
        /// <summary>The <see cref="WaveSpawnPoint.SpawnId"/> the batch materialises at.</summary>
        [JsonProperty("spawnPoint")] public string SpawnPoint = "spawn-0";
        /// <summary>Seconds after the wave starts before this batch begins releasing.</summary>
        [JsonProperty("delay")] public float Delay;
        /// <summary>Seconds between successive enemy spawns within the batch.</summary>
        [JsonProperty("interval")] public float Interval = 1f;
    }

    /// <summary>
    /// The apex flying-boss declaration on a wave — the deserialised shape of a
    /// <c>waves.json</c> <c>apexBoss</c> object. This is DISTINCT from
    /// <see cref="WaveDef.Boss"/>: <see cref="WaveDef.Boss"/> names a ground
    /// <see cref="EnemyDef"/> released as a normal NavMesh enemy, whereas the
    /// apex boss is the kinematic-flight <c>DragonBoss</c> prefab — it does not
    /// path a NavMesh and is not in <c>enemies.json</c>. WaveManager spawns the
    /// Boss_Dragon prefab and calls <c>DragonBoss.Configure</c> for an apex wave.
    /// See docs/port-notes/dragon-boss.md §2 + dragon-wave-wiring.md.
    /// </summary>
    [Serializable]
    public sealed class ApexBossDef
    {
        /// <summary>Stable per-instance boss id — e.g. "boss-dragon-syndrath".</summary>
        [JsonProperty("id")] public string Id = "boss-dragon";
        /// <summary>
        /// Max hit points for the apex boss. ≤0 keeps the prefab's inspector HP
        /// (the DragonBoss default is 4200 — well above the Necromancer's 1700).
        /// </summary>
        [JsonProperty("hp")] public float Hp;
        /// <summary>
        /// canon-strings.json key for the boss display name (the proper noun —
        /// e.g. "bossSyndrath" -> "Syndrath the Devourer"). Resolved via
        /// <see cref="VillageStrings.Canon"/> by the HUD / boss bar.
        /// </summary>
        [JsonProperty("nameKey")] public string NameKey;
    }

    /// <summary>
    /// One wave — a Prepare-Phase countdown followed by a set of spawn batches.
    /// The deserialised shape of a <c>waves.json</c> <c>waves[]</c> entry.
    /// </summary>
    [Serializable]
    public sealed class WaveDef
    {
        /// <summary>1-based wave ordinal.</summary>
        [JsonProperty("waveId")] public int WaveId;
        /// <summary>Wave name (narrative flavour — e.g. "First Light").</summary>
        [JsonProperty("name")] public string Name;
        /// <summary>Seconds of calm Prepare Phase BEFORE this wave spawns.</summary>
        [JsonProperty("countdownSeconds")] public float CountdownSeconds = 45f;

        /// <summary>
        /// The COMBAT budget for this wave, in seconds: roughly how long the fight itself is
        /// designed to take, measured from the first spawn to the last enemy down. Feeds
        /// <c>EncounterSample.ExpectedDurationSeconds</c>, i.e. the dynamic-difficulty clear-time
        /// signal (actualDuration / expectedDuration vs the authored par).
        /// <para>
        /// THIS IS DELIBERATELY NOT DERIVED FROM <see cref="CountdownSeconds"/>. That field is the
        /// BUILD WINDOW between waves, not a combat budget, and DifficultyTuning's Easy/Normal/Hard
        /// CountdownMultiplier scales it -- so deriving from it would feed the player's chosen
        /// difficulty SETTING into the adaptive system and couple two difficulty authorities that
        /// were deliberately kept disjoint (see DifficultyProfile's header).
        /// </para>
        /// <para>
        /// 0 / absent means NO CLEAR-TIME SAMPLE: WaveManager still records the encounter (its
        /// death and damage signals count) but neutralises the clear ratio onto the authored pivot
        /// so an unauthored wave contributes nothing to the time signal instead of a garbage ratio.
        /// </para>
        /// </summary>
        [JsonProperty("expectedCombatSeconds")] public float ExpectedCombatSeconds;
        /// <summary>The ordered spawn batches that make up the wave.</summary>
        [JsonProperty("enemies")] public List<WaveBatch> Enemies = new List<WaveBatch>();
        /// <summary>Optional boss enemy id released alongside the wave (null = no boss).</summary>
        [JsonProperty("boss")] public string Boss;
        /// <summary>
        /// WO-789: optional pinned max-HP for the ground <see cref="Boss"/>. When &gt; 0 the
        /// spawned boss's max/current HP is set to EXACTLY this value AFTER the
        /// WaveScalingCurve pass (the pin REPLACES the scaled value, so it bypasses per-wave
        /// HP escalation). &lt;= 0 / absent keeps the normal path: enemies.json stat SSOT
        /// multiplied by the curve. Mirrors <see cref="ApexBossDef.Hp"/> — a deliberate,
        /// visible wave-level exception to the enemies.json stat SSOT.
        /// </summary>
        [JsonProperty("bossHp")] public float BossHp;
        /// <summary>
        /// Optional apex flying-boss declaration (null = not an apex wave). When
        /// present this wave spawns the kinematic <c>DragonBoss</c> prefab instead
        /// of — or alongside — the normal enemy batches. See <see cref="ApexBossDef"/>.
        /// </summary>
        [JsonProperty("apexBoss")] public ApexBossDef ApexBoss;

        /// <summary>True when this wave fields the apex flying boss (the dragon).</summary>
        public bool IsApexBossWave => ApexBoss != null;

        /// <summary>Total enemy count across every batch (boss excluded).</summary>
        public int TotalEnemyCount
        {
            get
            {
                int n = 0;
                if (Enemies != null)
                    foreach (var b in Enemies) if (b != null) n += Mathf.Max(0, b.Count);
                return n;
            }
        }
    }

    /// <summary>
    /// ENDLESS-MODE tuning (owner ruling 2026-07-11: "after 20 rounds continue to
    /// allow the user to start waves manually and increase difficulty and mobs
    /// every level up"). The deserialised shape of the optional <c>waves.json</c>
    /// <c>endless</c> block. Past the last authored wave the WaveManager cycles
    /// the authored waves and multiplies each batch's count by
    /// <c>1 + CountGrowthPerWave * (waveId - lastAuthoredWaveId)</c>, capped at
    /// <see cref="CountCap"/>. Absent block ⇒ the defaults below (5%/wave, 3×).
    /// Stat (HP/speed/damage) growth stays WaveScalingCurve's job — it already
    /// scales by the TRUE wave number, which keeps counting past the schedule.
    /// </summary>
    [Serializable]
    public sealed class EndlessDef
    {
        /// <summary>Fractional mob-count growth per wave beyond the authored schedule (0.05 = +5%/wave).</summary>
        [JsonProperty("countGrowthPerWave")] public float CountGrowthPerWave = 0.05f;
        /// <summary>Cap on the count multiplier (3 = never more than 3× the authored counts). ≤0 = uncapped.</summary>
        [JsonProperty("countCap")] public float CountCap = 3f;

        // ── WO-1835 — POST-WAVE-20 ESCALATION TUNABLES (owner ruling 2026-09-17) ───────────
        // "after level 20, i want it to get difficult. THey need a real challenge so maybe 2
        //  dragons spawn and troops breaking through walls, or healing caravans or mages using
        //  AoE large heal spells, rage spells" / "catapults blasting the walls from the sides at
        //  the same time".
        //
        // EVERY VALUE HERE IS A FIRST-PASS ESTIMATE FOR THE OWNER TO FELT-CORRECT, not a final
        // balance figure — WO-1835 says so explicitly. They live in waves.json rather than in
        // code so she can re-tune the endless difficulty curve without a rebuild, which is the
        // whole point of the canonical-data layer. A field absent from the JSON keeps the default
        // below (Newtonsoft leaves unmapped members at their initialiser), so an older waves.json
        // stays valid and no schema bump is needed.
        // ⛔ COUNTS ARE NOT AUTHORED HERE. How MANY dragons / breachers / caravans / mages /
        // catapults a given wave fields is derived from the true wave number by the pure
        // predicates in EndlessPressure, so the escalation curve has ONE owner and cannot drift
        // between a JSON file and the code that reads it.

        /// <summary>Share of an endless wave's melee bodies re-tasked to break walls (0.35 = 35%).</summary>
        [JsonProperty("breacherFraction")] public float BreacherFraction = 0.35f;

        /// <summary>Max HP each twin dragon keeps, as a share of the solo dragon's (0.7 = 70% each).</summary>
        [JsonProperty("twinDragonHpShare")] public float TwinDragonHpShare = 0.7f;

        /// <summary>Seconds between the synchronized flanking-catapult volleys.</summary>
        [JsonProperty("catapultVolleySeconds")] public float CatapultVolleySeconds = 6f;

        /// <summary>Structure damage one catapult stone deals to one wall panel.</summary>
        [JsonProperty("catapultDamagePerHit")] public float CatapultDamagePerHit = 9f;

        /// <summary>Seconds between support-mage casts (the telegraph wind-up runs inside this).</summary>
        [JsonProperty("mageCastSeconds")] public float MageCastSeconds = 9f;

        /// <summary>
        /// Projects the authored block onto the runtime tuning object the endless pressure units
        /// read. Kept as a mapping method rather than having the components read EndlessDef
        /// directly, so the Village runtime never takes a dependency on the deserialiser shape and
        /// the oracle can build a tuning without touching JSON.
        /// </summary>
        public EndlessPressureTuning ToPressureTuning()
        {
            return new EndlessPressureTuning
            {
                BreacherFraction      = BreacherFraction,
                TwinDragonHpShare     = TwinDragonHpShare,
                CatapultVolleySeconds = CatapultVolleySeconds,
                CatapultDamagePerHit  = CatapultDamagePerHit,
                MageCastSeconds       = MageCastSeconds,
            };
        }
    }

    /// <summary>The deserialised root of <c>waves.json</c>.</summary>
    [Serializable]
    public sealed class WaveSchedule
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("waves")] public List<WaveDef> Waves = new List<WaveDef>();

        /// <summary>Optional endless-mode tuning (null ⇒ WaveManager uses <see cref="EndlessDef"/> defaults).</summary>
        [JsonProperty("endless")] public EndlessDef Endless;

        /// <summary>The highest authored <see cref="WaveDef.WaveId"/> (0 for an empty schedule).
        /// Endless mode begins at MaxWaveId + 1.</summary>
        public int MaxWaveId
        {
            get
            {
                int max = 0;
                if (Waves != null)
                    foreach (var w in Waves)
                        if (w != null && w.WaveId > max) max = w.WaveId;
                return max;
            }
        }

        /// <summary>Finds a wave by its 1-based id, or null when none matches.</summary>
        public WaveDef Find(int waveId)
        {
            if (Waves == null) return null;
            foreach (var w in Waves)
                if (w != null && w.WaveId == waveId) return w;
            return null;
        }
    }

    // =========================================================================
    //  Loader — async read from StreamingAssets (DungeonLayoutLoader pattern)
    // =========================================================================

    /// <summary>
    /// Loads the canonical <c>waves.json</c> and <c>enemies.json</c> from
    /// <c>StreamingAssets/Data/Canonical/</c>. Reading from StreamingAssets is
    /// async on Android (the files live inside the compressed APK), so every
    /// load returns a <see cref="UniTask"/> — never <c>async void</c> (port spec
    /// Part 3 mandate). Mirrors <c>DeNelle.Dungeons.DungeonLayoutLoader</c>.
    /// </summary>
    public static class WaveDataLoader
    {
        /// <summary>StreamingAssets-relative path to the canonical wave schedule.</summary>
        public const string WavesRelativePath = "Data/Canonical/waves.json";

        /// <summary>StreamingAssets-relative path to the village-enemy catalog.</summary>
        public const string EnemiesRelativePath = "Data/Canonical/enemies.json";

        /// <summary>
        /// Loads and deserialises the wave schedule. Returns null and logs an
        /// error when the file is missing or malformed.
        /// </summary>
        public static async UniTask<WaveSchedule> LoadWavesAsync()
        {
            string json = await ReadTextAsync(WavesRelativePath);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[WaveDataLoader] Could not read '{WavesRelativePath}'.");
                return null;
            }

            try
            {
                var schedule = JsonConvert.DeserializeObject<WaveSchedule>(json);
                if (schedule == null || schedule.Waves == null || schedule.Waves.Count == 0)
                {
                    // EW-3 (CLAUDE.md section 12, NO SILENT FAILURE): a renamed/removed top-level
                    // "waves" key (or an empty array) deserialises to a WaveSchedule whose Waves list
                    // is empty -- the wave loop would then run a SILENT 0-wave loop and every gate stays
                    // green. Scream it into the trace (break-log.jsonl) so a future key rename is caught,
                    // not shipped. Expected key: WaveSchedule.Waves <- [JsonProperty("waves")] (WaveData.cs:363).
                    FlowTrace.Fail("WaveData",
                        $"waves.json produced 0 waves (schedule={schedule != null}, " +
                        $"waves={(schedule?.Waves?.Count.ToString() ?? "null")}) -- the expected top-level " +
                        "\"waves\" key is missing/renamed or the array is empty. The village wave loop " +
                        "would run a 0-wave loop. Fix waves.json (top-level \"waves\": [ ... ]).");
                    Debug.LogError("[WaveDataLoader] waves.json deserialised to an empty schedule.");
                    return null;
                }
                return schedule;
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[WaveDataLoader] Malformed JSON in waves.json: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads and deserialises the village-enemy catalog. Returns null and
        /// logs an error when the file is missing or malformed.
        /// </summary>
        public static async UniTask<EnemyCatalog> LoadEnemiesAsync()
        {
            string json = await ReadTextAsync(EnemiesRelativePath);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[WaveDataLoader] Could not read '{EnemiesRelativePath}'.");
                return null;
            }

            try
            {
                var catalog = JsonConvert.DeserializeObject<EnemyCatalog>(json);
                if (catalog == null || catalog.Enemies == null || catalog.Enemies.Count == 0)
                {
                    Debug.LogError("[WaveDataLoader] enemies.json deserialised to an empty catalog.");
                    return null;
                }
                return catalog;
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[WaveDataLoader] Malformed JSON in enemies.json: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads canonical JSON for a StreamingAssets-relative path. Tries the
        /// WebGL-safe Resources copy FIRST (Resources.Load, synchronous on every
        /// platform incl. WebGL — File.ReadAllText / a StreamingAssets URL is
        /// unreliable in a browser). Falls back to a direct file read on desktop /
        /// editor, then a UnityWebRequest for packed StreamingAssets (Android).
        /// </summary>
        private static async UniTask<string> ReadTextAsync(string relativePath)
        {
            // 1) Resources-first (WebGL-safe). Never throws.
            string fromResources = DeNelle.Core.CanonicalJson.Read(relativePath);
            if (!string.IsNullOrEmpty(fromResources)) return fromResources;

            // 2) StreamingAssets fallback — desktop/editor plain file, else web request.
            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
            bool needsWebRequest = fullPath.Contains("://") || fullPath.Contains(":///");

            if (!needsWebRequest)
            {
                try
                {
                    if (!File.Exists(fullPath)) return null;
                    return await File.ReadAllTextAsync(fullPath);
                }
                catch (Exception e) { FlowTrace.Warn("WaveData", $"ReadTextAsync('{relativePath}') file read threw: {e.GetType().Name}: {e.Message}"); return null; }
            }

            using var req = UnityWebRequest.Get(fullPath);
            await req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                FlowTrace.Fail("WaveData", $"ReadTextAsync('{relativePath}') UnityWebRequest failed: {req.error}");
                return null;
            }
            return req.downloadHandler.text;
        }
    }
}
