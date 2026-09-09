// =============================================================================
// WaveManager — the Elarion village wave loop (Week-4 slice).
// -----------------------------------------------------------------------------
// Port spec Part 3 row: src/modules/village/waves/ -> WaveManager.cs.
// Port spec Part 5 Week 4: "countdown timer between waves, then spawn per
// data/waves.json. Wave 1 = 8 Hollow Walkers from the north gate."
//
// RESPONSIBILITIES
//   1. Load the canonical waves.json + enemies.json (WaveDataLoader).
//   2. Run a Prepare-Phase countdown before each wave (WaveDef.CountdownSeconds).
//   3. On countdown-zero, spawn the wave's batches at the WaveSpawnPoints — one
//      Enemy MonoBehaviour per spawned enemy, configured from the EnemyDef.
//   4. Watch every live enemy for an INNER-WALL-RING BREACH; when one or more
//      enemies cross the ring, hand the breaching roster to the ATB scene via
//      SceneRouter.GoBattle(BattleParams) and pause the wave loop.
//   5. When the ATB scene returns to the Village, resume the loop.
//
// This is a SUB-MonoBehaviour of the Village scene (port spec Part 3:
// "controller orchestrates ... wave manager ... Sub-systems each have their own
// MonoBehaviour"). VillageController WIRING IS THE INTEGRATOR'S JOB — this file
// does NOT touch VillageController. The integrator adds a WaveManager component
// to the Village scene, drops the WaveSpawnPoints + Heart into its serialized
// fields (or lets it auto-discover them), and the loop runs itself.
//
// NAVMESH: spawned enemies use NavMeshAgents. ** The village scene needs a baked
// NavMesh ** for enemies to move — see docs/port-notes/week4-waves.md.
//
// BREACH HAND-OFF: matches the REAL SceneRouter API —
//   SceneRouter.GoBattle(new BattleParams { Wave, BreachedIds, ParticipatingPetIds })
// GoBattle stashes the params on SceneRouter.PendingBattle and fades into the
// ATBBattle scene; BattleController reads PendingBattle on the far side. After
// the battle BattleController.ReturnAfterResult fades back to the Village scene,
// at which point a fresh WaveManager.Start() resumes the loop.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;   // FlowTrace — wave-start flow instrumentation (§12)
using DeNelle.Core.Adaptive;    // DynamicDifficulty — encounter telemetry + spawn-time multipliers
using DeNelle.Core.State;
using DeNelle.Core.Combat;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;   // singleton "active-scene wins" rule (TriggerWave RCA)
using DeNelle.Data;       // WaveData SO — WO-86

namespace DeNelle.Village
{
    /// <summary>The phase the wave loop is currently in.</summary>
    public enum WavePhase
    {
        /// <summary>Data not loaded yet / loop not started.</summary>
        Idle = 0,
        /// <summary>Counting down the calm Prepare Phase before the next wave.</summary>
        Countdown = 1,
        /// <summary>Spawning + fighting the active wave's enemies.</summary>
        Active = 2,
        /// <summary>A breach handed off to the ATB scene — the loop is paused.</summary>
        Breached = 3,
        /// <summary>Every wave in the schedule has been cleared.</summary>
        Complete = 4,
        /// <summary>The Heart fell (HP 0) — the run is lost. Terminal; the loop halts here.</summary>
        Defeated = 5,
    }

    /// <summary>A UnityEvent carrying the countdown seconds remaining (HUD binds this).</summary>
    [System.Serializable]
    public sealed class WaveCountdownEvent : UnityEvent<float> { }

    /// <summary>A UnityEvent carrying a wave ordinal (1-based).</summary>
    [System.Serializable]
    public sealed class WaveNumberEvent : UnityEvent<int> { }

    /// <summary>A UnityEvent carrying the apex flying boss that just spawned.</summary>
    [System.Serializable]
    public sealed class WaveBossEvent : UnityEvent<DragonBoss> { }

    /// <summary>
    /// WHAT THE LAST CLEARED WAVE ACTUALLY BANKED — the exact integers
    /// <c>WaveManager.AwardWaveResources</c> handed <c>EconomyService.Grant</c> and
    /// <c>WaveManager.AwardWaveCrystals</c> handed <c>GameStateService.AddCrystals</c>,
    /// captured AT the grant site.
    ///
    /// WHY A RECORD AND NOT A RE-DERIVATION (owner felt-test 2026-08-08, "I'm not
    /// seeing rewards after waves"): the payout is a RANDOM ROLL
    /// (<c>ScaledRoll</c> — <c>Random.Range</c> inside a wave-scaled band) folded
    /// through a talent multiplier. Any presentation layer that re-computed it
    /// would roll DIFFERENT numbers than the wallet received, and the banner would
    /// quietly lie about the balance the player is about to spend. So the brain
    /// publishes what it paid; presentation only reads.
    ///
    /// <see cref="WaveId"/> is the ANTI-STALENESS KEY: a reader must ask for a
    /// specific wave (<see cref="WaveManager.TryGetPayoutFor"/>), so wave 4's
    /// banner can never render wave 3's spoils. A wave that paid nothing still
    /// stamps its id with zero amounts — that is a recorded "paid nothing", not a
    /// missing record.
    /// </summary>
    public readonly struct WaveClearPayout
    {
        /// <summary>The wave this payout belongs to; -1 = nothing recorded yet.</summary>
        public readonly int WaveId;
        public readonly int Wood;
        public readonly int Iron;
        public readonly int Food;
        public readonly int Crystals;

        public WaveClearPayout(int waveId, int wood, int iron, int food, int crystals)
        {
            WaveId = waveId;
            Wood = wood; Iron = iron; Food = food; Crystals = crystals;
        }

        /// <summary>True when at least one resource actually landed in the wallet.</summary>
        public bool Any => Wood > 0 || Iron > 0 || Food > 0 || Crystals > 0;

        /// <summary>How many DISTINCT resource lines this payout would render (0..4).
        /// The end-state banner budgets its rows against this before it builds any.</summary>
        public int LineCount =>
            (Wood > 0 ? 1 : 0) + (Iron > 0 ? 1 : 0) + (Food > 0 ? 1 : 0) + (Crystals > 0 ? 1 : 0);

        /// <summary>Same payout with the boss/event crystal faucet folded in (that faucet
        /// runs after the wood/iron/food grant, on the same wave, in the same breath).</summary>
        public WaveClearPayout WithCrystals(int crystals) =>
            new WaveClearPayout(WaveId, Wood, Iron, Food, crystals);
    }

    /// <summary>
    /// Drives the village wave loop: countdown, spawn, breach detection, ATB
    /// hand-off. A self-contained sub-system MonoBehaviour for the Village scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaveManager : MonoBehaviour
    {
        // ── Inspector wiring ──────────────────────────────────────────────────

        [Header("Scene refs (wire in the inspector, or leave blank to auto-find)")]
        [Tooltip("The Heart — enemies march toward it; the breach ring is centred on it. " +
                 "Left blank: WaveManager finds the HeartController in the scene.")]
        [SerializeField] private HeartController _heart;

        [Tooltip("The wave spawn markers beyond each gate. Left empty: WaveManager " +
                 "finds every WaveSpawnPoint in the scene at Start.")]
        [SerializeField] private List<WaveSpawnPoint> _spawnPoints = new List<WaveSpawnPoint>();

        [Tooltip("Parent transform spawned enemies are parented under (keeps the hierarchy tidy). " +
                 "Left blank: enemies parent under this WaveManager's transform.")]
        [SerializeField] private Transform _enemyRoot;

        [Tooltip("Prefab instantiated per spawned enemy. Must carry an Enemy + NavMeshAgent. " +
                 "Left blank: WaveManager builds a primitive-capsule placeholder so the loop " +
                 "still runs before the KayKit skeleton prefab exists.")]
        [SerializeField] private Enemy _enemyPrefab;

        [Tooltip("Apex flying-boss prefab (Boss_Dragon — carries a DragonBoss). Spawned for a " +
                 "wave whose waves.json entry declares an 'apexBoss'. Left blank: an apex wave " +
                 "logs an error and is treated as cleared (the loop never stalls).")]
        [SerializeField] private DragonBoss _apexBossPrefab;

        [Header("Wave scaling (DEF-59)")]
        [Tooltip("Optional SO that scales enemy HP/speed/damage as wave number increases. " +
                 "Create via Assets → Create → Defenders/Waves/Wave Scaling Curve. " +
                 "Left blank: all enemies spawn at their base stats from enemies.json.")]
        [SerializeField] private WaveScalingCurve _scalingCurve;

        [Header("Group spawner (DEF-21)")]
        [Tooltip("Optional EnemyGroupSpawner component (child of this object). " +
                 "When assigned alongside _waveGroupSequence, each wave also spawns " +
                 "the matching WaveEnemyGroup asset — complementing (not replacing) " +
                 "the JSON batch system. Leave blank to use JSON batches only.")]
        [SerializeField] private EnemyGroupSpawner _groupSpawner;

        [Tooltip("One WaveEnemyGroup asset per wave slot (index 0 = wave 1, index 1 = wave 2, …). " +
                 "Shorter than the wave count is fine — missing slots are skipped. " +
                 "Requires _groupSpawner to be wired.")]
        [SerializeField] private System.Collections.Generic.List<WaveEnemyGroup> _waveGroupSequence
            = new System.Collections.Generic.List<WaveEnemyGroup>();

        [Tooltip("WO-316: COMPOSE each wave's batches into runtime role-mix FAMILY squads " +
                 "(a tank + healer + a few DPS, advancing + charging together via the " +
                 "EnemyGroupSpawner / EnemyGroupCoordinator) instead of releasing the flat " +
                 "single-type batch stream. ON = composed groups (auto-builds a spawner if " +
                 "none is wired); OFF = legacy flat WaveBatch spawning (back-compat).")]
        [SerializeField] private bool _composeFamilyGroups = true;

        [Tooltip("WO-316: the formation runtime-composed family squads spawn in.")]
        [SerializeField] private SpawnFormation _composedFormation = SpawnFormation.Wedge;

        [Tooltip("WO-362: SMART per-wave composition + tactical positioning. ON = ignore the " +
                 "flat waves.json batches and instead GENERATE each wave's roster from the wave " +
                 "number (tiered weak/medium/strong mix, an elite every 5th wave, no two " +
                 "consecutive waves identical), then place each enemy by tactical role (tanks " +
                 "front-centre, archers backline, weak trailing) at a gate that ROTATES N→E→S→W. " +
                 "OFF = the legacy compose-family / flat-batch paths (full back-compat). Takes " +
                 "priority over _composeFamilyGroups when both are on.")]
        [SerializeField] private bool _smartComposition = true;

        [Header("Wave SO Authoring (WO-86)")]
        [Tooltip("Optional list of WaveData ScriptableObjects for SO-driven authoring. The existing JSON-driven loop runs independently and is unaffected.")]
        [SerializeField] private List<WaveData> _soWaves
            = new List<WaveData>();

        [Header("Breach detection")]
        [Tooltip("Radius (world units) of the inner wall ring around the Heart. An enemy that " +
                 "crosses INSIDE this ring counts as a breach. Tune to sit just inside the " +
                 "curtain wall (WallLayout.WallHalfZ ~ 21u; the inner ring sits well within).")]
        [SerializeField] private float _innerRingRadius = 9f;

        [Tooltip("Seconds the manager waits after the wave starts before arming breach " +
                 "detection — lets enemies clear the spawn point first.")]
        [SerializeField] private float _breachArmDelay = 0.5f;

        [Header("Loop control")]
        [Tooltip("Start the wave loop automatically on Start(). OFF by default: the wave " +
                 "must NOT pre-spawn / pre-countdown at scene load — the player gets a calm " +
                 "build/prep phase first, then presses the HUD DEFEND button, which kicks " +
                 "the loop via ForceBeginNextWave() -> BeginLoop(). On: legacy auto-countdown " +
                 "at load (dev/standalone-wave-scene use only).")]
        [SerializeField] private bool _autoStart = false;

        [Tooltip("Start the loop from this wave id (1 = the first wave). Dev override.")]
        [SerializeField, Min(1)] private int _startWave = 1;

        [Header("Wave-clear resource reward (WO-330 — defend → earn → build)")]
        [Tooltip("Award build resources (Wood/Iron) when a wave is fully cleared. This is the " +
                 "primary economy income: defend the city → defeat the wave → earn the resources " +
                 "you spend on building/upgrading defenses. OFF = no wave-clear resource grant " +
                 "(the legacy crystal drop still runs).")]
        [SerializeField] private bool _awardResourcesOnWaveClear = true;

        [Tooltip("Base Wood granted on a wood-payout wave (before scaling). WO-361: wood pays out " +
                 "every Nth wave (WoodInterval), in the range [Base .. Base+Spread], scaled by wave.")]
        [SerializeField, Min(0)] private int _woodRewardBase = 20;

        [Tooltip("Random spread added on top of the wood base (0 = flat). Final = Random[Base..Base+Spread] * scale.")]
        [SerializeField, Min(0)] private int _woodRewardSpread = 10;

        [Tooltip("WO-330 wiring contract: extra Wood added to the wood base per wave number (linear ramp). " +
                 "Folded into the effective wood base BEFORE the random spread + WO-361 scaling, so a " +
                 "later wood-payout wave starts from a higher floor. 0 = no per-wave wood ramp.")]
        [SerializeField, Min(0)] private int _woodRewardPerWave = 0;

        [Tooltip("Wood pays out every Nth wave (WO-361 default: every 3rd). 0/1 = every wave.")]
        [SerializeField, Min(0)] private int _woodRewardInterval = 3;

        [Tooltip("Base Iron granted on an iron-payout wave (before scaling). WO-361: iron pays out " +
                 "every Nth wave (IronInterval), in the range [Base .. Base+Spread], scaled by wave.")]
        [SerializeField, Min(0)] private int _ironRewardBase = 15;

        [Tooltip("Random spread added on top of the iron base (0 = flat). Final = Random[Base..Base+Spread] * scale.")]
        [SerializeField, Min(0)] private int _ironRewardSpread = 10;

        [Tooltip("WO-330 wiring contract: extra Iron added to the iron base per wave number (linear ramp). " +
                 "Folded into the effective iron base BEFORE the random spread + WO-361 scaling, so a " +
                 "later iron-payout wave starts from a higher floor. 0 = no per-wave iron ramp.")]
        [SerializeField, Min(0)] private int _ironRewardPerWave = 0;

        [Tooltip("Iron pays out every Nth wave (WO-361 default: every 4th). 0/1 = every wave.")]
        [SerializeField, Min(0)] private int _ironRewardInterval = 4;

        [Tooltip("Base Food granted on a food-payout wave (before scaling). WO-361: food pays out " +
                 "every Nth wave (FoodInterval), in the range [Base .. Base+Spread], scaled by wave.")]
        [SerializeField, Min(0)] private int _foodRewardBase = 30;

        [Tooltip("Random spread added on top of the food base (0 = flat). Final = Random[Base..Base+Spread] * scale.")]
        [SerializeField, Min(0)] private int _foodRewardSpread = 20;

        [Tooltip("Food pays out every Nth wave (WO-361 default: every 2nd). 0/1 = every wave.")]
        [SerializeField, Min(0)] private int _foodRewardInterval = 2;

        [Tooltip("Reward scaling: amounts grow by this fraction every ScalePer waves (WO-361: +20% per 5 waves). " +
                 "e.g. 0.2 with ScalePer 5 → wave 6 pays 1.2×, wave 11 pays 1.4×, …")]
        [SerializeField, Min(0f)] private float _rewardScalePerStep = 0.2f;

        [Tooltip("Number of waves per scaling step (WO-361: every 5 waves). Min 1.")]
        [SerializeField, Min(1)] private int _rewardScaleWaveStep = 5;

        [Tooltip("Clamps the scaling step count so reward growth never runs away on very " +
                 "late waves. 0 = uncapped. e.g. 6 caps scaling after 6 steps.")]
        [SerializeField, Min(0)] private int _rewardScalingStepCap = 0;

        [Header("Per-kill resource trickle (WO-330 — optional, secondary)")]
        [Tooltip("Grant a small Wood/Iron trickle per enemy killed during the wave (the wave-clear " +
                 "bonus is the primary reward). OFF = only the wave-clear bonus pays out.")]
        [SerializeField] private bool _awardResourcesPerKill = true;

        [Tooltip("Wood granted per enemy killed.")]
        [SerializeField, Min(0)] private int _woodPerKill = 1;

        [Tooltip("Iron granted per enemy killed.")]
        [SerializeField, Min(0)] private int _ironPerKill = 0;

        [Header("Performance budget (DEF-48)")]
        [Tooltip("Hard cap on simultaneously live enemies. 0 = no cap. Enforced on BOTH spawn " +
                 "paths (WO-1113): the legacy SpawnBatch stalls until an enemy dies, and the " +
                 "live smart-composed path releases up to the cap then HOLDS the rest as " +
                 "reinforcements (total wave count is unchanged; only the arrival schedule is). " +
                 "Recommended values: 4 (early), 6 (mid), 8 (late), 5 (boss wave).")]
        [SerializeField, Min(0)] private int _maxSimultaneousEnemies = 8;

        // ── Events — HUD / Heart subscribe in OnEnable, unsubscribe in OnDisable ──

        [Header("Events")]
        [Tooltip("Fires every frame during the Prepare Phase with the seconds remaining.")]
        public WaveCountdownEvent OnCountdownTick = new WaveCountdownEvent();

        [Tooltip("Fires when a wave's enemies begin spawning. Arg = the wave id.")]
        public WaveNumberEvent OnWaveStarted = new WaveNumberEvent();

        [Tooltip("Fires when every enemy of a wave is dead / consumed. Arg = the wave id.")]
        public WaveNumberEvent OnWaveCleared = new WaveNumberEvent();

        [Tooltip("Fires when one or more enemies breach the inner ring. Arg = the wave id.")]
        public WaveNumberEvent OnBreach = new WaveNumberEvent();

        [Tooltip("Fires when an apex wave releases its flying boss. Arg = the spawned DragonBoss " +
                 "— bind the boss HP bar / camera framing / Heart threat state to it.")]
        public WaveBossEvent OnApexBossSpawned = new WaveBossEvent();

        [Tooltip("Fires once when the Heart of Elarion falls (HP 0) — the village LOSE condition " +
                 "(WO-125 Bug 3). The loop halts (phase Defeated); bind a defeat screen here.")]
        public UnityEvent OnDefeat = new UnityEvent();

        // ── The wave-clear PAYOUT record (owner felt-test 2026-08-08) ─────────
        //
        // THE DEFECT this closes: rewards were being paid and NOTHING showed it. Every
        // OnWaveCleared listener in the tree is persistence / quests / dialogue / tutorial /
        // audio / pose — no UI. The one presentation attempt, ShowRewardToast below, resolves
        // "ShowBanner(string)" / "ShowToast(string)" on the live HUD by reflection and
        // VillageHudController declares NEITHER (it has ShowWaveClearBanner(int,int,string)
        // only), so `m` was always null and `m?.Invoke` was a SILENT no-op. Defend -> earn ->
        // build lost its "earn" beat entirely.
        //
        // STATIC on purpose: the reader is the presentation layer (EndStateVM.FromWaveClear,
        // built by WaveCelebrationManager from an OnWaveCleared listener), which must not have
        // to find and hold a WaveManager reference just to read a number the brain already
        // knows. Staleness is impossible by construction — every read is keyed on a wave id.
        //
        // WRITE ORDER (CompleteWave): AwardWaveResources stamps -> AwardWaveCrystals folds in
        // -> OnWaveCleared.Invoke. So the record is COMPLETE before any listener runs.
        private static WaveClearPayout s_lastPayout = new WaveClearPayout(-1, 0, 0, 0, 0);

        /// <summary>The last recorded wave-clear payout (WaveId -1 = none yet this session).
        /// Prefer <see cref="TryGetPayoutFor"/> — it enforces the wave-id match.</summary>
        public static WaveClearPayout LastPayout => s_lastPayout;

        /// <summary>
        /// The payout banked for <paramref name="waveId"/>, if any. Returns FALSE when no
        /// payout was recorded for that exact wave (a different wave, nothing recorded yet, or
        /// a wave that genuinely paid nothing) — a caller that respects the bool can never
        /// render another wave's numbers.
        /// </summary>
        public static bool TryGetPayoutFor(int waveId, out WaveClearPayout payout)
        {
            payout = s_lastPayout;
            return waveId >= 0 && payout.WaveId == waveId && payout.Any;
        }

        // ── Runtime state ─────────────────────────────────────────────────────

        private WaveSchedule _schedule;
        private EnemyCatalog _enemyCatalog;
        private readonly List<Enemy> _liveEnemies = new List<Enemy>();
        private readonly List<Enemy> _breachRoster = new List<Enemy>();

        // WO-1113: enemies this wave's roster still owes the field, held back by the
        // _maxSimultaneousEnemies concurrency cap and released as slots free. NON-ZERO means the
        // wave is NOT clear even with an empty field — see the clear gate in TickActiveWave.
        private int _heldSmartReinforcements;

        // WO-1308 — THE DRAIN HEARTBEAT. _heldSmartReinforcements is owned entirely by the
        // fire-and-forget DrainSmartReinforcements(...).Forget(); every bail zeroes it, but an
        // EXCEPTION inside that UniTask cannot, because nobody is awaiting it. The counter then
        // stays non-zero forever and TickActiveWave's clear gate returns before the clear test on
        // every subsequent frame — the wave can never complete, the phase is latched Active, and
        // the battle-lock probe registered in OnEnable holds the lock for the rest of the session.
        //
        // A counter alone cannot tell "the drain is working through a queue" from "the drain is
        // dead", and that distinction is the whole difference between a wave that must stay open
        // and a softlock. So the drain stamps this every time round its loop. Stale + held > 0 =>
        // the task is gone; fresh + held > 0 => reinforcements really are coming.
        private float _reinforcementDrainUnscaled = -1f;

        /// <summary>
        /// WO-1308: how long (unscaled) the drain heartbeat may go unstamped before the held count
        /// is treated as ORPHANED. The drain stamps it from inside its cap-wait PREDICATE as well
        /// as its loop body, so a long legitimate wait for a free slot keeps stamping every frame
        /// and can never be mistaken for a dead task: a stale heartbeat means the UniTask is not
        /// running at all. Still generous, because the cost of being early here is a wave short of
        /// bodies and the cost of being never is a session-long softlock.
        /// </summary>
        private const float ReinforcementDrainStaleSeconds = 20f;

        // Failsafe against a stuck enemy freezing the wave's clear gate (the recurring
        // "wave won't advance" bug — clear requires _liveEnemies.Count == 0). Tracks each
        // enemy's best distance toward the Heart; culls one that makes no progress for
        // StuckTimeout so an off-mesh / boxed-in Hollow One can't hang the wave forever.
        private readonly Dictionary<Enemy, float> _enemyBestSqr   = new Dictionary<Enemy, float>();
        private readonly Dictionary<Enemy, float> _enemyStuckTime = new Dictionary<Enemy, float>();
        private const float StuckTimeout = 12f;

        // WO-430: per-enemy spawn scatter around the marker (metres, applied ±). Lateral was
        // ±4.5 — too wide, it pushed the pre-NavMesh-sample XZ off the baked mesh boundary and
        // the silent SamplePosition miss then stranded the enemy at the raw spawn Y (sky/underground).
        // Tightened to ±3 to lower the miss probability; depth kept at ±3.
        private const float SpawnLateralSpread = 3f;
        private const float SpawnDepthSpread   = 3f;

        /// <summary>The apex flying boss for the current wave (null when not an apex wave / dead).</summary>
        private DragonBoss _liveApexBoss;
        // Holds an apex wave open while the correctly-typed GameObject address arrives.
        private bool _apexSpawnPending;
        private const float ApexLoadTimeoutSeconds = 30f;

        /// <summary>WO-362: lazily-built tactical spawner (no inspector wiring required).</summary>
        private SmartEnemySpawner _smartSpawner;

        /// <summary>True once we've subscribed to the Heart's OnHeartDestroyed — fire-once subscribe guard.</summary>
        private bool _heartDeathHooked;

        private WavePhase _phase = WavePhase.Idle;
        private int _currentWaveId;
        private float _countdownRemaining;
        private bool  _forceSpawnNow;   // dev/bot "jump to wave": zero the countdown on the next BeginLoop

        // ---------------------------------------------------------------------
        // WO-1308 INSTRUMENTATION - the last phase transition, recorded centrally.
        //
        // WHY: _phase is private with no external writer, and once it reaches Active the ONLY
        // routine exit is TickActiveWave -> CompleteWave -> EnterCountdown. TickActiveWave is
        // reached from exactly ONE place (the switch at the bottom of Update), which sits behind
        // two early returns (the FTUE stand-down and the TownSuspension stand-down). If Update
        // cannot reach that switch, Active is PERMANENT and the battle-lock probe registered in
        // OnEnable holds the lock forever - which is the owner's "the wolf is still here and
        // sitting in fight" (F8 seq 4663/4665).
        //
        // These fields answer "who set Active, when, and has the loop ticked since" without
        // changing a single behaviour. Per CLAUDE.md sec.12 this instrumentation is PERMANENT.
        private WavePhase _lastPhaseFrom      = WavePhase.Idle;
        private WavePhase _lastPhaseTo        = WavePhase.Idle;
        private string    _lastPhaseSite      = "<none since load>";
        private float     _lastPhaseUnscaled  = -1f;
        private int       _lastPhaseFrame     = -1;

        // The frame/unscaled-time of the last Update that actually REACHED the phase switch,
        // i.e. that got past BOTH early returns. A stale value here while _phase == Active is
        // direct proof that an early return is eating the tick that would clear the wave.
        private int   _lastSwitchFrame    = -1;
        private float _lastSwitchUnscaled = -1f;

        /// <summary>
        /// WO-1308: the SINGLE writer of <see cref="_phase"/>. Every one of the nine assignment
        /// sites routes through here so the last transition is always on the record when the
        /// battle-quiescence gate asks why the lock is still held.
        ///
        /// It is a pure recorder: the assignment is identical to the one it replaced, no site is
        /// gated, refused or reordered, and no trace is emitted here (the sites keep their own
        /// FlowTrace lines). Behaviour is unchanged by construction.
        /// </summary>
        private void SetPhase(WavePhase next, string site)
        {
            _lastPhaseFrom     = _phase;
            _lastPhaseTo       = next;
            _lastPhaseSite     = string.IsNullOrEmpty(site) ? "<unnamed site>" : site;
            _lastPhaseUnscaled = Time.unscaledTime;
            _lastPhaseFrame    = Time.frameCount;
            _phase             = next;
        }

        // ENDLESS MODE (owner ruling 2026-07-11: "after 20 rounds continue to allow the user to
        // start waves manually and increase difficulty and mobs every level up"). Past the last
        // authored wave the loop does NOT auto-run the prepare countdown — it parks in phase
        // Countdown with _countdownRemaining held at 0 and this flag set, WAITING for the player's
        // DEFEND button (ForceBeginNextWave / ForceSpawnNextWaveNow). The HUD needs no change:
        // StartWaveHudBridge already shows the DEFEND button in phase Countdown, and the
        // HudKit wave-timer label only renders while CountdownRemaining > 0 (so it stays blank).
        private bool _awaitingPlayerStart;

        // Endless cycling: waves beyond the schedule replay the authored waves from this id
        // upward IN ORDER (4..20 by default — the escalating family squads), so every full
        // cycle ends on the authored apex wave (the dragon returns as the cycle capstone at
        // true waves 37, 54, …). Clamped into the schedule range at runtime.
        private const int EndlessCycleStartWaveId = 4;

        private const int FirstDragonWave = 20;
        private const int DragonWaveInterval = 5;
        private const float DragonHpGrowthPerReturn = 0.25f;
        private const float DragonDamageGrowthPerReturn = 0.12f;
        private const float DragonCadenceGainPerReturn = 0.05f;
        private const float DragonMinimumIntervalMultiplier = 0.65f;

        /// <summary>True for the recurring dragon cadence: 20, 25, 30, 35, ...</summary>
        public static bool IsRecurringDragonWave(int waveId)
            => waveId >= FirstDragonWave && (waveId - FirstDragonWave) % DragonWaveInterval == 0;

        /// <summary>Zero for wave 20, one for 25, two for 30, and so on.</summary>
        public static int DragonReturnTier(int waveId)
            => IsRecurringDragonWave(waveId) ? (waveId - FirstDragonWave) / DragonWaveInterval : -1;

        /// <summary>Each return carries 25% more max HP than the wave-20 dragon.</summary>
        public static float DragonHpForWave(float baseHp, int waveId)
        {
            int tier = Mathf.Max(0, DragonReturnTier(waveId));
            return Mathf.Max(1f, baseHp) * (1f + DragonHpGrowthPerReturn * tier);
        }

        /// <summary>Each return deals 12% more damage than the base encounter.</summary>
        public static float DragonDamageMultiplierForWave(int waveId)
        {
            int tier = Mathf.Max(0, DragonReturnTier(waveId));
            return 1f + DragonDamageGrowthPerReturn * tier;
        }

        /// <summary>Attack gaps shorten 5% per return, capped at 65% of authored timing.</summary>
        public static float DragonAttackIntervalMultiplierForWave(int waveId)
        {
            int tier = Mathf.Max(0, DragonReturnTier(waveId));
            return Mathf.Max(DragonMinimumIntervalMultiplier, 1f - DragonCadenceGainPerReturn * tier);
        }

        /// <summary>
        /// True while an endless wave (beyond the authored schedule) is armed and the loop is
        /// waiting for the player to start it via the HUD DEFEND button. Read-only seam for
        /// HUD/bot producers; before the schedule is exhausted this is always false.
        /// </summary>
        public bool IsAwaitingPlayerStart => _awaitingPlayerStart;
        private float _breachArmTimer;
        private bool _breachArmed;
        private int _spawnInstanceCounter;

        // =====================================================================
        //  ENCOUNTER TELEMETRY — the INPUT to Dynamic Difficulty (Task A)
        // ---------------------------------------------------------------------
        //  DynamicDifficulty's math was oracle-proven but INERT because nothing in
        //  the game ever called RecordEncounter: not one of the six fields
        //  EncounterSample needs was measured anywhere, so the multiplier returned
        //  exactly 1.0 forever. These four measurements are that missing input.
        //
        //  WAVE-START TIME IS COMBAT-START, NOT COUNTDOWN-START. The build-window
        //  countdown (45s first wave, 300s later, further scaled by the player's
        //  Easy/Normal/Hard setting) is not part of the fight. Stamping the clock at
        //  EnterCountdown instead of StartWave would inflate every clear time by up
        //  to five minutes and corrupt the clear-time ratio outright.
        //
        //  All four are reset in BeginEncounterTelemetry, which StartWave calls the
        //  moment the phase turns Active.
        // =====================================================================

        /// <summary>Time.time when COMBAT began for the live wave (-1 = no encounter armed).</summary>
        private float _encounterStartTime = -1f;

        /// <summary>Latched by <see cref="HandleTelemetryHeroDied"/> off HeroHealth.OnDied.</summary>
        private bool _encounterHeroDied;

        /// <summary>Running sum of HP the hero LOST during this encounter (post-mitigation).</summary>
        private float _encounterDamageTaken;

        /// <summary>Snapshot of <see cref="Enemy.HeroDamageDealtTotal"/> at combat start; the
        /// encounter's damage dealt is the delta against it.</summary>
        private double _encounterDamageDealtBase;

        /// <summary>The HeroHealth instance the telemetry is currently bound to (re-bound if the
        /// hero instance is ever swapped mid-encounter).</summary>
        private HeroHealth _telemetryHero;

        /// <summary>Previous sampled hero HP; a NEGATIVE delta is damage taken. -1 = no baseline
        /// yet. A respawn / heal / gear top-up moves this UP, which is ignored by design.</summary>
        private float _telemetryLastHeroHp = -1f;

        /// <summary>True once this manager has subscribed to GameStateService.StateReplaced.</summary>
        private bool _newGameHookArmed;

        // WO-579 (#5 "resets to wave 1") — cross-reload wave RESUME. The WaveManager is rebuilt on
        // every hub (re)load (it is NOT DontDestroyOnLoad), so without a resume point BeginLoop always
        // restarts at _startWave (=1). This static survives a scene reload WITHIN a play session, and is
        // seeded once from the save (GameState.BestWave + 1) for cross-session continuation.
        // CompleteWave advances it; ResetResumeStatic clears it at each play start so a new game / save
        // reset re-seeds from the (possibly reset) BestWave instead of carrying a stale wave number.
        private static int s_resumeWaveId = 0;   // 0 = unseeded

        // DYNAMIC DIFFICULTY: false until this play session has cleared the encounter history
        // once. See EnsureDifficultySessionReset — the per-session half of "one player's history
        // never scales another's run" (the other half is the StateReplaced hook below).
        private static bool s_difficultySessionReset;

        // WO-139 #12 pattern: with domain reload disabled, statics persist across Play sessions. Reset
        // the resume seed at each play start so it re-derives from the save (handles new game / reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResumeStatic()
        {
            s_resumeWaveId = 0;
            // Only ARM the reset here; do not perform it. DynamicDifficulty.State lazily loads the
            // profile through DifficultyProfileCatalog (Resources/StreamingAssets), which has no
            // business running at SubsystemRegistration. The reset itself happens on the first wave
            // kickoff of the session (EnsureDifficultySessionReset).
            s_difficultySessionReset = false;
            // Same WO-139 #12 hazard for the wave-clear payout record: with domain reload
            // disabled a stale wave-1 payout from the PREVIOUS play session would still match
            // the id of this session's wave 1 and render spoils that were never banked here.
            s_lastPayout = new WaveClearPayout(-1, 0, 0, 0, 0);
        }

        /// <summary>The phase the wave loop is in.</summary>
        public WavePhase Phase => _phase;

        /// <summary>The wave currently counting down / active (1-based; 0 before the loop starts).</summary>
        public int CurrentWaveId => _currentWaveId;

        /// <summary>Seconds remaining in the Prepare-Phase countdown (0 when not counting down).</summary>
        public float CountdownRemaining => _countdownRemaining;

        /// <summary>True while the loop is in the Prepare-Phase countdown (the wave timer is live).</summary>
        public bool IsCountingDown => _phase == WavePhase.Countdown;

        /// <summary>
        /// T-022 (HUD wave-timer bind): seconds until the next wave begins. Returns the
        /// live countdown value ONLY while actually counting down, and 0 otherwise (Idle /
        /// Active / Complete), so a HUD timer label binding this reads a clean, unambiguous
        /// value — no need to also test <see cref="Phase"/>. Additive read-only accessor;
        /// existing <see cref="CountdownRemaining"/> / <see cref="OnCountdownTick"/> are
        /// unchanged, so existing bindings keep working.
        /// </summary>
        public float SecondsUntilNextWave => _phase == WavePhase.Countdown ? _countdownRemaining : 0f;

        /// <summary>Live enemies currently on the field.</summary>
        public IReadOnlyList<Enemy> LiveEnemies => _liveEnemies;

        /// <summary>The apex flying boss on the field, or null when no apex wave is live.</summary>
        public DragonBoss LiveApexBoss => _liveApexBoss;

        /// <summary>The Heart the wave loop marches enemies at (resolved at BeginLoop).</summary>
        public HeartController Heart => _heart;

        // ── Patricia Light hooks (WO-47) ──────────────────────────────────────
        // The breach-time "Defend the Tower" shooter (PatriciaLightMode) needs to
        // spawn real Enemy instances using the SAME prefab + roster path the wave
        // loop uses. These thin accessors + the public SpawnEnemyForExternalMode
        // helper let it reuse SpawnOne's instantiate / NavMesh-sample / Configure
        // logic without duplicating it or making fields public. PatriciaLightMode
        // lives in DeNelle.Village too, so no asmdef boundary is crossed.

        /// <summary>
        /// The enemy prefab the wave loop instantiates (null = the loop builds a
        /// primitive-capsule placeholder via <see cref="BuildPlaceholderEnemy"/>).
        /// Exposed so the breach-time Defend-the-Tower mode can reuse the same body.
        /// </summary>
        public Enemy EnemyPrefab => _enemyPrefab;

        /// <summary>
        /// Loads the canonical enemy catalog if it is not already loaded and
        /// returns it (null on load failure). PatriciaLightMode uses this to pull
        /// an <see cref="EnemyDef"/> from the same enemies.json the wave loop uses.
        /// </summary>
        public async UniTask<EnemyCatalog> GetEnemyCatalogAsync()
        {
            if (_enemyCatalog == null)
                _enemyCatalog = await WaveDataLoader.LoadEnemiesAsync();
            return _enemyCatalog;
        }

        /// <summary>
        /// Spawns one Enemy at <paramref name="worldPos"/> for an external mode
        /// (the breach-time Defend-the-Tower shooter), reusing the wave loop's
        /// instantiate / NavMesh-sample / Configure path. The caller owns the
        /// returned enemy's lifecycle (this does NOT add it to the wave loop's
        /// live-enemy roster or breach watch). Returns null if no def is given.
        /// </summary>
        /// <param name="def">The enemy stat block (from <see cref="GetEnemyCatalogAsync"/>).</param>
        /// <param name="worldPos">Where to spawn — snapped to the nearest NavMesh.</param>
        /// <param name="heart">The transform the enemy marches at.</param>
        /// <param name="instanceId">Stable per-instance id for attribution / events.</param>
        public Enemy SpawnEnemyForExternalMode(EnemyDef def, Vector3 worldPos, Transform heart, string instanceId)
        {
            if (def == null) return null;

            Vector3 pos = worldPos;
            if (UnityEngine.AI.NavMesh.SamplePosition(
                    pos, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position;

            Vector3 toHeart = heart != null ? (heart.position - pos) : Vector3.forward;
            toHeart.y = 0f;
            Quaternion rot = Quaternion.LookRotation(
                toHeart.sqrMagnitude > 0.0001f ? toHeart : Vector3.forward);

            // POOLED (same path as SpawnOne): reuse a dead body instead of churning a
            // fresh GameObject. The external caller owns this enemy's lifecycle; on its
            // death Enemy.Die returns it to the pool like any other.
            //
            // T-011: prefer the model-keyed FACTORY body (varied per def) over the single
            // serialized _enemyPrefab so this external-mode spawn matches the variety of
            // the wave loop. _enemyPrefab is only a fallback when no model resolves.
            string model = EnemyFactory.ModelForEnemy(def);
            bool useFactory = !string.IsNullOrEmpty(model);
            if (!useFactory && _enemyPrefab == null)
            {
                Debug.LogError($"[WaveManager] No model resolves for enemy data '{def.Id}' and no _enemyPrefab fallback — using primitive placeholder.");
            }
            string poolKey = useFactory ? "model:" + model : "prefab:" + _enemyPrefab.name;
            Enemy enemy = EnemyPool.Get(poolKey, useFactory ? null : _enemyPrefab, def, pos, rot, _enemyRoot);
            if (enemy == null) return null;

            // The hero/pet target sweeps find enemies via GetComponentInParent
            // <IDamageable>, which resolves to EnemyDamageable. The placeholder
            // capsule (and some prefabs) may not carry it — add it so the
            // Defend-the-Tower hero can actually acquire + damage this enemy.
            if (enemy.GetComponent<EnemyDamageable>() == null)
                enemy.gameObject.AddComponent<EnemyDamageable>();

            enemy.Configure(instanceId, def, heart);
            return enemy;
        }

        // ── Singleton (TriggerWave-timeout RCA) ───────────────────────────────
        //
        // WaveManager is NOT a DontDestroyOnLoad global — there is a WaveManager
        // baked into BOTH MainCastle_Hall (the home hub / start scene) and
        // Village2 (the raid target). With those scenes loaded additively,
        // FindAnyObjectByType<WaveManager>() enumerates in a NON-deterministic order,
        // so a consumer (BattleMusic / TowerSwap / CameraMode / the AutoPilot
        // TriggerWave probe) could resolve a DIFFERENT instance than the one being
        // triggered/watched — the "works ~5/12, fails ~9/12" race that surfaced as
        // the intermittent "TriggerWave timeout".
        //
        // CANONICAL RULE — the WaveManager in the ACTIVE scene wins. On Awake/OnEnable:
        //   • if Instance is null → claim it (first one home).
        //   • else if THIS object lives in the active scene → claim it (active wins,
        //     even if a hub instance claimed first while it was the active scene).
        // We never destroy the loser (both managers still run their own scene's loop);
        // we only steer Find-based consumers at the canonically-correct one. Cleared
        // in OnDestroy ONLY if we are still the current Instance (never clobber a
        // newer claimant). Consumers prefer WaveManager.Instance, falling back to a
        // Find when Instance is null (pre-Awake / between scene loads) for safety.
        public static WaveManager Instance { get; private set; }

        // A live village siege is combat even though it is not an ATB/Arena session.
        // Keep one cached delegate because BattleLock unregisters by delegate identity.
        private Func<bool> _waveBattleProbe;

        /// <summary>
        /// Claims <see cref="Instance"/> per the "active-scene wins" rule above.
        /// Idempotent + safe to call from both Awake and OnEnable (a scene becoming
        /// active after load re-asserts the claim on the next enable).
        /// </summary>
        private void ClaimInstanceIfCanonical()
        {
            bool inActiveScene = gameObject.scene.IsValid()
                                 && gameObject.scene == SceneManager.GetActiveScene();
            if (Instance == null || inActiveScene)
            {
                if (Instance != this)
                    FlowTrace.Step("Wave", $"WaveManager.Instance claimed by '{name}' in scene '{gameObject.scene.name}' (active={inActiveScene}).");
                Instance = this;
            }
        }

        private void Awake()  => ClaimInstanceIfCanonical();
        private void OnEnable()
        {
            ClaimInstanceIfCanonical();
            if (_waveBattleProbe == null)
                _waveBattleProbe = () => isActiveAndEnabled && Instance == this && _phase == WavePhase.Active;
            BattleLock.RegisterProbe(_waveBattleProbe);

            // WO-1308: hand the battle-quiescence gate the ONE thing it could not see - WHY the
            // wave probe above is returning true. BattleQuiescenceGate.Register replaces by name,
            // so re-enabling (or a second WaveManager enabling) cannot accumulate duplicates, and
            // the delegate is STATIC so it never binds to one instance and then goes stale.
            RegisterWavePhaseQuiescenceProbe();

            // WO-1308: and the wave loop now UNWINDS ITS OWN state at the end of a battle session,
            // the same way HitStopManager unwinds the clock. See RegisterWavePhaseSessionUnwind.
            RegisterWavePhaseSessionUnwind();
        }

        // =====================================================================
        //  WO-1308 - the "wave-phase" quiescence probe
        // =====================================================================
        //
        // THE CAPTURE THIS EXISTS FOR (owner felt-test 2026-09-02, F8 seq 4663-4665):
        //   BATTLE_QUIESCENCE_FAIL (retreat) - battle-lock: still HELD ...
        //     HOLDER(S): PursuitBattleProbe.Probe, WaveManager.<OnEnable>b__106_0
        //   battle-lock STILL HELD after the self-heal (retreat): [WaveManager.<OnEnable>b__106_0]
        // PursuitBattleProbe released; the wave probe did not, because _phase was still Active.
        //
        // ⛔ THE PROBE ABOVE IS NOT THE BUG AND IS NOT TO BE "FIXED". A live village siege genuinely
        // IS combat, and a retreat from an overworld wolf must NOT cancel a siege. Making it return
        // false during a real wave would trade a stuck lock for a combat state the game does not
        // know it is in - strictly worse, and invisible. The open question is whether _phase was
        // Active with NO LIVE WAVE BEHIND IT, and that is a question only DATA can answer
        // (CLAUDE.md sec.12). This probe captures that data and changes nothing else.
        //
        // ⚠ DELIBERATE SCOPE, recorded loudly because the owner is mid-felt-test and could not be
        // asked: this probe reports ONLY when a WaveManager satisfies the EXACT holder predicate
        // (isActiveAndEnabled && Instance == wm && _phase == Active). That is the same predicate as
        // the battle-lock probe, so the gate's battle-lock invariant is ALREADY failing whenever
        // this speaks. It therefore CANNOT manufacture a new BATTLE_QUIESCENCE_FAIL on a clean
        // battle - it only annotates one that was going to fail anyway. The cost of that choice: a
        // phase stranded Active on a NON-canonical (loser) WaveManager stays silent. The RCA rates
        // that direction inert precisely because the lock probe's `Instance == this` clause
        // neutralises it, and the live-instance count printed below still surfaces the Q4 shape.

        /// <summary>
        /// WO-1308. Registers the "wave-phase" <see cref="QuiescenceProbe"/> with
        /// <see cref="BattleQuiescenceGate"/> so the wave loop's state prints INSIDE the same
        /// BATTLE_QUIESCENCE_FAIL block that names the lock holder.
        ///
        /// Core cannot reference DeNelle.Village (the gate's own header says so), so the knowledge
        /// arrives as a delegate through the existing registration seam - exactly the way
        /// BattleArena registers "arena-actors" / "hero-owner". No new assembly reference, no new
        /// registry, no second gate.
        /// </summary>
        private static void RegisterWavePhaseQuiescenceProbe()
        {
            BattleQuiescenceGate.Register(new QuiescenceProbe
            {
                Name  = "wave-phase",
                Check = CheckWavePhaseQuiescence
            });
        }

        /// <summary>
        /// Returns null unless a WaveManager is holding the battle lock through
        /// <see cref="WavePhase.Active"/>; otherwise returns the full latched-phase dump.
        /// Static and Find-based on purpose: it must survive an instance being destroyed and it
        /// must be able to SEE a second WaveManager rather than assume there is one.
        /// </summary>
        private static string CheckWavePhaseQuiescence()
        {
            var all = Guard.Try("Quiescence", "wave-phase enumerate WaveManagers",
                () => FindObjectsByType<WaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                System.Array.Empty<WaveManager>());
            if (all == null || all.Length == 0) return null;

            WaveManager holder = null;
            for (int i = 0; i < all.Length; i++)
            {
                WaveManager wm = all[i];
                if (wm == null) continue;
                if (wm.isActiveAndEnabled && Instance == wm && wm._phase == WavePhase.Active)
                {
                    holder = wm;
                    break;
                }
            }
            if (holder == null) return null;   // no wave is holding the lock; nothing to report

            return Guard.Try("Quiescence", "wave-phase dump",
                () => holder.DescribeLatchedWavePhase(all.Length),
                "phase=Active but the state dump itself threw - see the Guard line above.");
        }

        /// <summary>
        /// WO-1308: the diagnostic sentence the gate prints. Every value the RCA named, in one
        /// line, with the reading key appended so the next occurrence is self-diagnosing and does
        /// not cost another felt-test to reproduce.
        /// </summary>
        private string DescribeLatchedWavePhase(int waveManagerCount)
        {
            // Count non-null enemies WITHOUT mutating _liveEnemies. A probe must observe, never
            // edit: pruning here would change what TickActiveWave sees on the very next frame and
            // could clear the wave as a SIDE EFFECT of looking at it - destroying the evidence.
            int liveEnemies = 0;
            for (int i = 0; i < _liveEnemies.Count; i++)
                if (_liveEnemies[i] != null) liveEnemies++;
            int nulls = _liveEnemies.Count - liveEnemies;

            bool apexUp = _liveApexBoss != null && !_liveApexBoss.IsDead;

            string sceneName = "?";
            try { sceneName = gameObject.scene.name; } catch { sceneName = "<torn-down>"; }
            string activeScene = "?";
            try { activeScene = SceneManager.GetActiveScene().name; } catch { activeScene = "<none>"; }

            int   frameNow   = Time.frameCount;
            float unscaledNow = Time.unscaledTime;

            string lastTransition = _lastPhaseFrame < 0
                ? "NONE RECORDED since load (the phase has never moved through SetPhase - a serialized/default Active would look like this)"
                : $"{_lastPhaseFrom} -> {_lastPhaseTo} at '{_lastPhaseSite}' " +
                  $"(t={_lastPhaseUnscaled:F2}s unscaled, frame {_lastPhaseFrame}, " +
                  $"{unscaledNow - _lastPhaseUnscaled:F2}s / {frameNow - _lastPhaseFrame} frames ago)";

            string lastSwitch = _lastSwitchFrame < 0
                ? "NEVER - Update has not reached the phase switch once since load"
                : $"frame {_lastSwitchFrame} (t={_lastSwitchUnscaled:F2}s unscaled, " +
                  $"{frameNow - _lastSwitchFrame} frames / {unscaledNow - _lastSwitchUnscaled:F2}s ago)";

            bool suspendedForMe = Guard.Try("Quiescence", "wave-phase SuspendedFor",
                () => TownSuspension.SuspendedFor(this), false);

            return
                "the wave loop is LATCHED at phase=Active, which is the whole reason the battle-lock " +
                "is still held (the lock probe is WaveManager.OnEnable's lambda and it is CORRECT: a " +
                "live siege is combat). The question is whether a live wave is really behind it. " +
                $"phase={_phase} wave={_currentWaveId} awaitingPlayerStart={_awaitingPlayerStart} " +
                $"countdownRemaining={_countdownRemaining:F2}s | " +
                $"liveEnemies={liveEnemies} (+{nulls} null slot(s) not yet pruned) apexBossAlive={apexUp} " +
                $"heart={(_heart == null ? "NULL" : "present")} | " +
                $"heldSmartReinforcements={_heldSmartReinforcements} | " +
                $"lastPhaseTransition: {lastTransition} | " +
                $"lastUpdateReachedSwitch: {lastSwitch} | " +
                $"townSuspension: IsSuspended={TownSuspension.IsSuspended} reason='{TownSuspension.Reason}' " +
                $"graceRemaining={TownSuspension.ReturnGraceRemaining:F2}s held={TownSuspension.Held} " +
                $"suspendedForThisManager={suspendedForMe} | " +
                $"scene='{sceneName}' activeScene='{activeScene}' isCanonicalInstance={(Instance == this)} " +
                $"isActiveAndEnabled={isActiveAndEnabled} liveWaveManagers={waveManagerCount} | " +
                "HOW TO READ THIS (WO-1308 RCA): " +
                "heldSmartReinforcements>0 with liveEnemies==0 => the dropped-async wedge - an " +
                "exception inside the fire-and-forget DrainSmartReinforcements left the counter " +
                "non-zero, so TickActiveWave returns before the clear test and the wave can NEVER " +
                "complete. " +
                "liveEnemies>0 with heart=NULL => the stuck-enemy failsafe is heart-gated and can " +
                "never cull the survivor. " +
                "held==0 and liveEnemies==0 and suspendedForThisManager=true with a grace around " +
                "2.7s and a STALE lastUpdateReachedSwitch => the TownSuspension early return is " +
                "eating the tick; this one self-clears once the return grace elapses. " +
                "A stale lastUpdateReachedSwitch with suspendedForThisManager=false points at the " +
                "FTUE stand-down instead. " +
                "scene != activeScene, or liveWaveManagers>1 => the two-manager shape is also in " +
                "play and the Instance claim needs reading alongside the above.";
        }

        // =====================================================================
        //  WO-1308 - THE FIX. The wave loop unwinds its own latched phase at battle end.
        // =====================================================================
        //
        // THE PROVING LINE (owner felt-test 2026-09-02, F8 seq 4664, Main_Castle_Overworld):
        //
        //   [Flow:Quiescence] battle-lock STILL HELD after the self-heal (retreat):
        //     [WaveManager.<OnEnable>b__106_0] (was [PursuitBattleProbe.Probe,
        //     WaveManager.<OnEnable>b__106_0]).
        //
        // Read it exactly: BattleSessionEnd.Release ran, PursuitBattleProbe RELEASED, and the wave
        // probe did not. So this is not a pursuit-window leak (that was WO-1233) and it is not the
        // probe misreporting - _phase really was Active. The lock therefore survives a full session
        // release, for the rest of the session, and the owner is left with a wolf "still here and
        // sitting in fight".
        //
        // ⛔ WHY THE PROBE IS NOT TOUCHED. A live village siege genuinely IS combat and a retreat
        // from an overworld wolf must NOT cancel it. Making the probe return false during a real
        // wave, or unregistering it, would trade a stuck lock for a combat state the game does not
        // know it is in - strictly worse, because it is invisible. The probe is correct; the PHASE
        // was wrong, and the phase is what this repairs.
        //
        // THE STRUCTURAL POINT - this is WO-1233's own lesson applied one layer down. That ticket
        // found the release hanging off OUTCOMES and moved it to the ONE lifecycle end, with each
        // owner of a global registering its own unwind. WaveManager owns a global (the battle-lock
        // claim it raises through _phase) and was the one such owner that had never registered an
        // unwind - so every battle in a hub that is also running the auto-armed village wave loop
        // (FeatureFlags.WaveAutoStart is ON and Main_Castle_Overworld IS a hub) ended with nobody
        // asking the wave loop whether its claim was still true. It does now, by name, through the
        // same door.
        //
        // ⚠ AND IT DRIVES THE LOOP'S OWN TICK RATHER THAN RE-DECIDING. The clear rule lives in
        // TickActiveWave and must live in exactly one place: a second copy of "is this wave over"
        // is a second answer waiting to disagree. Active is unreachable-from-here by construction -
        // one tick either completes the wave through CompleteWave, exactly as the loop would have,
        // or declines because enemies are alive, exactly as the loop would have. A genuine siege is
        // preserved for free, and nothing here can end a fight the loop would have kept open.

        private const string WaveSessionUnwindOwner = "WaveManager.phase";

        /// <summary>
        /// WO-1308. Registers the wave loop's battle-end unwind. Static delegate + name-keyed
        /// registration (<see cref="BattleSessionEnd.RegisterUnwind"/> REPLACES by owner), so a
        /// re-enable, a scene reload or a second WaveManager can never accumulate duplicates and
        /// the delegate can never go stale against a destroyed instance.
        /// </summary>
        private static void RegisterWavePhaseSessionUnwind()
        {
            BattleSessionEnd.RegisterUnwind(WaveSessionUnwindOwner, ReconcileLatchedWavePhaseOnSessionEnd);
        }

        /// <summary>
        /// Runs at every battle-session end. Finds the manager that is actually holding the
        /// battle-lock - the EXACT predicate the lock probe uses, so we can only ever act on the
        /// claim that is really raised - and gives the loop the tick it could not take.
        /// </summary>
        private static void ReconcileLatchedWavePhaseOnSessionEnd(string context)
        {
            var all = Guard.Try("Quiescence", "wave-phase unwind: enumerate WaveManagers",
                () => FindObjectsByType<WaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                System.Array.Empty<WaveManager>());
            if (all == null) return;

            for (int i = 0; i < all.Length; i++)
            {
                WaveManager wm = all[i];
                if (wm == null) continue;
                if (!wm.isActiveAndEnabled || Instance != wm || wm._phase != WavePhase.Active) continue;

                Guard.Try("Quiescence", "wave-phase unwind: reconcile latched Active",
                    () => wm.ReconcileLatchedWavePhase(context));
                return;
            }
        }

        /// <summary>
        /// WO-1308. The instance half of the unwind: report the state, then drive ONE
        /// <see cref="TickActiveWave"/> so the loop reaches its own clear test.
        /// </summary>
        private void ReconcileLatchedWavePhase(string context)
        {
            // The one case we must NOT drive: the town is deliberately suspended for this manager
            // (the player is away in a dungeon/raid and the owner ruled the town holds still). The
            // wave is frozen ON PURPOSE there, and TownSuspension owns that decision, not us. It is
            // also the one shape the RCA rated SELF-CLEARING, because the loop resumes the moment
            // the return grace elapses - so leaving it is a wait, not a softlock.
            bool suspended = Guard.Try("Quiescence", "wave-phase unwind: SuspendedFor",
                () => TownSuspension.SuspendedFor(this), false);
            if (suspended)
            {
                FlowTrace.Warn("Wave",
                    $"battle session ended ({context}) with wave {_currentWaveId} latched Active, but the " +
                    $"town is SUSPENDED for this manager ({TownSuspension.Reason}, grace " +
                    $"{TownSuspension.ReturnGraceRemaining:F2}s). The freeze is deliberate and owned by " +
                    "TownSuspension, so the loop is NOT driven here; it resumes on its own when the grace " +
                    "elapses. If the battle-lock is still held after that, this is not the holder.");
                return;
            }

            int live = 0;
            for (int i = 0; i < _liveEnemies.Count; i++)
                if (_liveEnemies[i] != null && !_liveEnemies[i].IsDead) live++;
            bool apexUp = _liveApexBoss != null && !_liveApexBoss.IsDead;

            FlowTrace.Warn("Wave",
                $"battle session ended ({context}) with wave {_currentWaveId} still at phase=Active - the " +
                $"battle-lock probe is TRUE because of it. State: live={live} apexBossAlive={apexUp} " +
                $"heldReinforcements={_heldSmartReinforcements} heart=" +
                $"{(_heart == null ? "NULL" : "present")} lastUpdateReachedSwitch=frame {_lastSwitchFrame} " +
                $"({Time.frameCount - _lastSwitchFrame} frames ago) lastPhaseTransition='{_lastPhaseFrom} -> " +
                $"{_lastPhaseTo} at {_lastPhaseSite}'. Driving ONE TickActiveWave so the loop reaches its " +
                "own clear test: a live siege KEEPS the lock (correct), an empty one completes.");

            WavePhase before = _phase;
            TickActiveWave();

            if (_phase != before)
            {
                FlowTrace.Warn("Wave",
                    $"wave {_currentWaveId}: the forced tick moved the phase {before} -> {_phase}, so the " +
                    "battle-lock claim is released. This is a SAFETY NET, not a fix - the loop should have " +
                    "reached that tick itself, and the state line above says why it could not.");
                return;
            }

            int liveAfter = 0;
            for (int i = 0; i < _liveEnemies.Count; i++)
                if (_liveEnemies[i] != null && !_liveEnemies[i].IsDead) liveAfter++;

            FlowTrace.Step("Wave",
                $"wave {_currentWaveId}: the forced tick left the phase at Active - live={liveAfter} " +
                $"apexBossAlive={_liveApexBoss != null && !_liveApexBoss.IsDead} " +
                $"heldReinforcements={_heldSmartReinforcements}. A GENUINE siege is " +
                "still running, so the battle-lock is correctly still held and nothing is cancelled. " +
                "Retreating from an overworld encounter must never end a village siege.");
        }

        private void OnDestroy()
        {
            if (_waveBattleProbe != null) BattleLock.UnregisterProbe(_waveBattleProbe);
            // Only relinquish if we still hold it — never clobber a newer claimant.
            if (Instance == this) Instance = null;
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Start()
        {
            // DEFEND-GATED START: the wave loop must NOT pre-spawn or pre-countdown at
            // scene load. At load there are ZERO enemies and the manager sits Idle; the
            // player gets the calm build/prep phase, then presses the HUD DEFEND button
            // (VillageHudController.StartWaveRequested -> StartWaveHudBridge ->
            // ForceBeginNextWave() -> BeginLoop()), which is the SOLE kickoff. _autoStart
            // therefore defaults OFF and stays off on the live village scene.
            //
            // WO-133 (FTUE): even with _autoStart left ON (dev / standalone wave scene),
            // a FIRST run (GameState.Onboarded == false) still must NOT auto-start — the
            // tutorial's BeginWaveRequested is the kickoff there. Core-not-bootstrapped
            // (no service) is treated as a returning player so a missing Core never
            // strands a dev auto-start.
            // WO-579 (#2/#3 — owner felt-test 2026-06-28 "start wave should AUTO attack; Start Wave is
            // an OVERRIDE"): AUTO-ARM the prepare-phase countdown in the player's HOME HUB so the
            // top-left "next wave in MM:SS" clock ticks (VillageHudController.PollWaveTimer surfaces
            // CountdownRemaining) and the wave AUTO-starts at zero (towers + hero auto-defend in-hub).
            // The baked MainCastle_Hall WaveManager has _autoStart serialized OFF, so we also auto-arm
            // when ff.waveautostart is on AND this is the home hub (a hub scene that is NOT enemy-owned;
            // excludes the Village2 enemy stronghold, which runs its own garrison loop). IsFirstRun
            // (FTUE) still blocks it — the tutorial owns the first kickoff. The HUD "Start Wave" button
            // (ForceBeginNextWave) remains the manual EARLY override that skips the remaining countdown.
            bool autoArm = _autoStart || (FeatureFlags.WaveAutoStart && IsHomeHubScene());
            if (autoArm && !IsFirstRun()) GuardedKickoff("Start/auto-arm");
        }

        /// <summary>
        /// WO-579: true when this WaveManager lives in the player-owned HOME HUB — a hub scene that is
        /// NOT enemy-owned (MainCastle_Hall / CastleHub / CastleHub_MainKeep). The Village2 enemy
        /// stronghold is a hub name too but is enemy-owned, so it is excluded from the home-defense
        /// auto-countdown (it drives its own garrison roster).
        /// </summary>
        private bool IsHomeHubScene()
        {
            string scene = gameObject.scene.IsValid()
                ? gameObject.scene.name
                : SceneManager.GetActiveScene().name;
            return HubScenes.IsHub(scene) && !HubScenes.IsEnemyOwnedScene(scene);
        }

        /// <summary>
        /// True when this is a brand-new save still in onboarding
        /// (<see cref="GameState.Onboarded"/> == false). Mirrors
        /// <c>OnboardingFlow.ShouldRun</c> so the wave loop and the FTUE agree on
        /// who is a first-run player. No Core service yet ⇒ NOT first-run, so the
        /// loop is never blocked when Core has not bootstrapped.
        /// </summary>
        private static bool IsFirstRun()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null) return false;
            return !svc.State.Onboarded;
        }

        // Audit 2026-05-30 (WO-139 #4): unsubscribe from all live enemy/boss
        // events so stale callbacks don't fire into a torn-down manager across
        // the breach->reload loop. Mirrors the subs in SpawnEnemy/SpawnApexBoss.
        private void OnDisable()
        {
            if (_waveBattleProbe != null) BattleLock.UnregisterProbe(_waveBattleProbe);
            foreach (Enemy e in _liveEnemies)
            {
                if (e == null) continue;
                e.Died -= HandleEnemyDied;
                e.ReachedHeart -= HandleEnemyReachedHeart;
            }
            _liveEnemies.Clear();
            // WO-1113: the drain UniTask outlives this disable; drop the held count so a
            // re-enabled manager can never start with a clear gate wedged by a dead wave.
            _heldSmartReinforcements = 0;
            _reinforcementDrainUnscaled = -1f;

            // WO-1308 — AND THE PHASE MUST COME DOWN WITH THE ROSTER.
            //
            // This block already deletes the wave: every live enemy is unsubscribed and dropped,
            // the held reinforcement count is zeroed, the apex boss handle is released. What it did
            // NOT do was lower _phase, so a manager disabled mid-wave came back at OnEnable still
            // claiming Active over a field it had just emptied — and re-registered the battle-lock
            // probe against that claim. The only routine exit from Active is
            // TickActiveWave -> CompleteWave, which is reachable ONLY from the switch at the bottom
            // of Update, behind two early returns; a phase left describing a wave whose bodies are
            // gone is therefore a latch that outlives the fight, holds combat input suppressed and
            // pins the HUD out of town. That is the shape the owner hit (F8 seq 4663-4665).
            //
            // Idle, not Complete: nothing was cleared and nothing is owed. The loop re-arms the
            // normal way (BeginLoop -> EnterCountdown re-derives from the resume seed), exactly as
            // _awaitingPlayerStart below is dropped for the same reason.
            if (_phase == WavePhase.Active || _phase == WavePhase.Countdown)
            {
                FlowTrace.Warn("Wave",
                    $"OnDisable with phase={_phase} (wave {_currentWaveId}): the roster this phase " +
                    "describes has just been cleared, so the phase is stood down to Idle with it. " +
                    "Leaving it Active is what latches the battle-lock past the end of the fight.");
                SetPhase(WavePhase.Idle, "OnDisable/roster-cleared");
                _countdownRemaining = 0f;
            }

            if (_liveApexBoss != null)
            {
                _liveApexBoss.Died -= HandleApexBossDied;
                _liveApexBoss = null;
            }

            // WO-125 Bug 3: drop the Heart lose-condition subscription so a stale
            // delegate can't fire into a torn-down manager across the breach/reload loop.
            if (_heart != null && _heartDeathHooked)
                _heart.OnHeartDestroyed -= HandleHeartDestroyed;
            _heartDeathHooked = false;

            // Drop the stall watchdog handle — Unity stops coroutines on disable, but
            // clearing the field keeps a stale handle from being StopCoroutine'd later.
            _stallWatchdogRoutine = null;

            // ENDLESS: drop the awaiting-player latch — a re-enabled/reloaded manager
            // re-derives it from the resume seed via BeginLoop -> EnterCountdown.
            _awaitingPlayerStart = false;

            // Encounter telemetry: drop the HeroHealth.OnDied + GameState.StateReplaced
            // subscriptions so a torn-down manager can never be called into (same rule as the
            // enemy/boss/Heart unsubscribes above). The in-flight encounter is abandoned.
            UnbindEncounterTelemetry();
            _encounterStartTime = -1f;
        }

        private void Update()
        {
            // WO-1483 frame budget. FIRST line so every early-return path is still timed.
            // This EXTENDS the file's existing Measure (the "wave data load" scope, a one-shot
            // LOAD cost) onto the FRAME path. Accumulating 4-arg overload — no per-frame log;
            // PerfReporter rolls it up 1/s.
            using var _perf = FlowTrace.Measure("Perf", "WaveManager.Update", 4f, 1f);

            // FTUE PER-TICK STAND-DOWN (F8 2026-08-05: "wave 1's enemies attacked me while I was
            // still paused on the tutorial screen" — captured cd29.9 -> cd6.8 with the tutorial
            // LIVE). The FTUE guard used to be checked at the DOOR only (BeginLoop /
            // GuardedKickoff); once EnterCountdown had set phase=Countdown the clock ran to zero
            // and spawned regardless of what the peace window said afterwards. WaveManager was the
            // ONLY consumer that asked once — every other one (RegionMobSpawner,
            // OverworldEncounterSpawner, HeroHealth) re-checks every tick. So we re-check here.
            //
            // Countdown ONLY, never Active: at Countdown no enemy exists yet, so nothing is
            // despawned and no live wave is yanked out from under the player. A countdown is tens
            // of seconds and cannot cross into Active within one frame, so with this gate in place
            // Active is unreachable during the FTUE anyway.
            //
            // The loop is NOT left stranded: TutorialFlow.FinishFlow sets Phase.Finished +
            // FinishOnboarding() and THEN calls BeginLoop() — both of which close this predicate
            // first — so the legitimate post-tutorial handoff re-arms the clock normally.
            if (_phase == WavePhase.Countdown && TutorialFlow.WaveLoopSuppressedForTutorial)
            {
                FlowTrace.Warn("Wave", $"FTUE stand-down: wave {_currentWaveId} countdown was armed while the " +
                    $"tutorial is live (cd {_countdownRemaining:F1}s) - returning the loop to Idle and " +
                    "retracting the kickoff watchdogs (this Idle is deliberate, not a stall).");
                _countdownRemaining = 0f;
                _awaitingPlayerStart = false;
                SetPhase(WavePhase.Idle, "Update/FTUE-stand-down");

                // RETRACT THE KICKOFF WATCHDOGS -- otherwise this stand-down manufactures a FALSE
                // P0 in the owner's first-ever run.
                //
                // WHY a kickoff is in flight at all on a fresh save: WaveManager.Start's auto-arm is
                // gated by !IsFirstRun(), but IsFirstRun() fails OPEN when GameStateService has not
                // bootstrapped yet (line ~617: no service => "not first run"), and so do BOTH FTUE
                // predicates (null svc => false). So on a genuinely fresh save that wins the race
                // against Core bootstrap, GuardedKickoff DOES fire, arms RetryTillActive +
                // StallWatchdog, and the async BeginLoop reaches EnterCountdown a few frames later
                // -- which is exactly the cd29.9 the owner captured with the tutorial live.
                //
                // WHY they must be cancelled: both watchdogs only ever ask "is the phase STILL
                // Idle?". Neither can tell a DELIBERATE stand-down from a silent stall. Left armed
                // they would re-fire BeginLoop StartRetryCap(3) times -- each correctly refused at
                // the door -- then emit FlowTrace.Fail, and StallWatchdog would add a Debug.LogError
                // at StallWatchdogWindow(9s). Per the F8 daemon contract those surface as a captured
                // error mid-tutorial: a false alarm on the one run that must look perfect. This is
                // the SAME regression GuardedKickoff's early-return below was written to prevent
                // (see its comment), reached from the other side -- there the kickoff never starts;
                // here it started legitimately and we forced it back. Idle is the CORRECT state, so
                // retract the alarms. The loop still re-arms normally: TutorialFlow.FinishFlow calls
                // BeginLoop() directly (never through these watchdogs) after Phase.Finished +
                // FinishOnboarding, so cancelling them costs the post-tutorial handoff nothing.
                if (_retryRoutine != null) { StopCoroutine(_retryRoutine); _retryRoutine = null; }
                if (_stallWatchdogRoutine != null) { StopCoroutine(_stallWatchdogRoutine); _stallWatchdogRoutine = null; }

                // Clear both countdown surfaces so neither keeps drawing its own clock after the
                // stand-down: the HUD band (WaveHudBridge listens to OnCountdownTick — 0f is the
                // same "clear it" value OnWaveStart pushes, and WaveFeedbackDirector ignores 0f so
                // no imminent-alert fires), and the world-space label (WaveCountdownUI has no
                // Hide() seam, but StartCountdown(0f) takes its seconds <= 0 branch: stop the
                // routine + HideLabel — no new API invented in a third file).
                OnCountdownTick.Invoke(0f);
                WaveCountdownUI.Instance?.StartCountdown(0f);
                return;
            }

            // ── TOWN-SUSPENSION PER-TICK STAND-DOWN (owner ruling 2026-08-07) ────────
            // "everything pauses except harvesting, while player is active." The player
            // being in a dungeon does not mean they are ABSENT - they are present, just
            // elsewhere in their own game - so the town holds still until they are back.
            // Offline is the OPPOSITE case and is deliberately untouched here: an offline
            // town stays exposed, because that pressure is the reason to fortify it.
            //
            // PER-TICK, not at the door, for exactly the reason the FTUE stand-down above
            // is per-tick: a gate checked only in BeginLoop lets an already-armed countdown
            // run to zero and spawn anyway. This is the same lesson, applied to the same
            // clock. The precedent seam is RepEngageWatcher.PauseAll/ResumeAll.
            //
            // SuspendedFor(this) carries the active-scene exemption, so this can never
            // freeze a wave belonging to the scene the player is standing in - which is why
            // this is a deliberate per-system suspend and NOT Time.timeScale.
            if (TownSuspension.SuspendedFor(this))
            {
                if (_phase == WavePhase.Countdown || _phase == WavePhase.Active)
                {
                    // OPEN QUESTION, owner has not ruled: what happens to a wave that is
                    // ALREADY IN PROGRESS when the player leaves. Both answers are built.
                    if (TownSuspension.WavePolicy == InProgressWavePolicy.CancelOnEntry &&
                        _phase == WavePhase.Active)
                    {
                        FlowTrace.Warn("Wave",
                            $"town suspended ({TownSuspension.Reason}) with wave {_currentWaveId} ACTIVE - " +
                            "policy=CancelOnEntry, returning the loop to Idle (the in-flight wave is abandoned).");
                        SetPhase(WavePhase.Idle, "Update/town-suspend-CancelOnEntry");
                        _countdownRemaining = 0f;
                        OnCountdownTick.Invoke(0f);
                        WaveCountdownUI.Instance?.StartCountdown(0f);
                        return;
                    }

                    // DEFAULT policy=SuspendAndResume: freeze in place. Nothing is zeroed and
                    // no phase is rewritten, so the wave resumes exactly as the player left it
                    // once the return grace elapses. Throttled - this runs every frame.
                    FlowTrace.Throttle("Wave", "town-suspend-hold", 5f,
                        $"town suspended ({TownSuspension.Reason}) - wave {_currentWaveId} HELD at " +
                        $"phase={_phase} cd={_countdownRemaining:F1}s. Harvesting continues; this clock does not.");
                }
                return;
            }

            // WO-1308 INSTRUMENTATION: stamp the frame that got PAST both early returns above.
            // This is the only line in Update that proves the loop is still being ticked. While
            // _phase == Active, a _lastSwitchFrame far behind Time.frameCount means an early
            // return (FTUE or TownSuspension) is eating the tick, and Active can never be lowered
            // because TickActiveWave is reached from nowhere else. Two ints; no behaviour change.
            _lastSwitchFrame    = Time.frameCount;
            _lastSwitchUnscaled = Time.unscaledTime;

            switch (_phase)
            {
                case WavePhase.Countdown:
                    TickCountdown();
                    break;
                case WavePhase.Active:
                    TickActiveWave();
                    break;
            }
        }

        // =====================================================================
        //  Loop entry
        // =====================================================================

        /// <summary>
        /// Loads the canonical wave data and starts the loop at <see cref="_startWave"/>.
        /// Safe to call again after an ATB return to resume (re-loads are cheap +
        /// the loop simply re-enters the countdown for the un-cleared wave).
        /// Returns a <see cref="UniTask"/> — never <c>async void</c> (port spec Part 3).
        /// </summary>
        public async UniTask BeginLoop()
        {
            // §12 wave-start instrumentation (TriggerWave TIMEOUT RCA). ADDITIVE only —
            // no logic/timing/async change. Traces every step + each await result so a
            // headless fleet run shows EXACTLY where the start flow stalls.
            FlowTrace.Step("Wave", $"BeginLoop ENTRY phase={_phase} forceSpawn={_forceSpawnNow} startWave={_startWave} scheduleCached={_schedule != null} catalogCached={_enemyCatalog != null}");

            // FTUE GUARD (F8 2026-07-08 "died in tutorial — nothing should spawn"): while the
            // first-time tutorial is active the ambient wave loop must NOT arm — a countdown/wave
            // could otherwise kill the player mid-tutorial. This mirrors (and back-stops) the
            // Start/auto-arm !IsFirstRun gate for ANY BeginLoop caller (manual DEFEND button, dev
            // seams, resume). The tutorial's OWN scripted teaching wave uses TutorialWaveSpawner ->
            // SpawnEnemyForExternalMode (not this loop), so it is unaffected. The intended
            // post-tutorial kick from TutorialFlow.FinishFlow runs AFTER FinishOnboarding sets
            // Onboarded=true, so this never blocks the legitimate handoff.
            //
            // PREDICATE (F8 2026-08-05): this door now consults the WAVE-CLOCK window
            // (WaveLoopSuppressedForTutorial — FTUE live, zone-independent), NOT the zone-scoped
            // AMBIENT window. Strict superset of the old check, so no path loses a guard, and the
            // town clock no longer arms just because the hero stepped outside the village bounds.
            // The ambient spawners (RegionMobSpawner / OverworldEncounterSpawner / HeroHealth)
            // deliberately STAY on HostilesSuppressedForTutorial (owner ruling 2026-07-24).
            if (TutorialFlow.WaveLoopSuppressedForTutorial)
            {
                FlowTrace.Step("Wave", "BeginLoop suppressed — tutorial (FTUE) active; ambient wave loop stays closed until onboarding completes.");
                return;
            }

            ResolveSceneRefs();

            using (FlowTrace.Measure("Wave", "wave data load", warnAboveMs: 2000f))
            {
                // LOAD-GUARD (layer 3): a faulted/null load must NOT escape this async
                // (it would never reach EnterCountdown and the loop would silently stay
                // at Idle). We catch, FlowTrace.Fail, and LEAVE the cache field null so
                // the null-check below forces _phase=Idle — which RetryTillActive then
                // re-attempts (re-running these loads), so a transient fault self-heals
                // instead of permanently stalling. A successful load caches and is never
                // re-fetched.
                if (_schedule == null)
                {
                    FlowTrace.Step("Wave", "BeginLoop awaiting LoadWavesAsync…");
                    try
                    {
                        _schedule = await WaveDataLoader.LoadWavesAsync();
                        FlowTrace.Step("Wave", $"BeginLoop LoadWavesAsync returned — waves loaded: {_schedule?.Waves?.Count ?? -1}");
                    }
                    catch (Exception e)
                    {
                        _schedule = null;
                        FlowTrace.Fail("Wave", $"LoadWavesAsync threw: {e.Message} — leaving schedule null for retry");
                        Debug.LogError($"[WaveManager] LoadWavesAsync threw: {e}");
                    }
                }
                if (_enemyCatalog == null)
                {
                    FlowTrace.Step("Wave", "BeginLoop awaiting LoadEnemiesAsync…");
                    try
                    {
                        _enemyCatalog = await WaveDataLoader.LoadEnemiesAsync();
                        FlowTrace.Step("Wave", $"BeginLoop LoadEnemiesAsync returned — enemies loaded: {_enemyCatalog?.Enemies?.Count ?? -1}");
                    }
                    catch (Exception e)
                    {
                        _enemyCatalog = null;
                        FlowTrace.Fail("Wave", $"LoadEnemiesAsync threw: {e.Message} — leaving catalog null for retry");
                        Debug.LogError($"[WaveManager] LoadEnemiesAsync threw: {e}");
                    }
                }
            }

            if (_schedule == null || _enemyCatalog == null)
            {
                FlowTrace.Fail("Wave", $"wave data null — loop cannot run (schedule={_schedule != null}, catalog={_enemyCatalog != null}) — phase forced to Idle — {StallStateDump(-1)}");
                Debug.LogError("[WaveManager] Wave data failed to load — the wave loop cannot run.");
                SetPhase(WavePhase.Idle, "BeginLoop/wave-data-null");
                return;
            }

            // WO-579 (#5): resume from the persisted run progress so a hub reload does not reset to 1.
            int startAt = ResolveStartWave();
            FlowTrace.Step("Wave", $"BeginLoop data OK — calling EnterCountdown(startWave={startAt}) [resume={s_resumeWaveId}, dev_startWave={_startWave}], forceSpawn={_forceSpawnNow}");
            EnterCountdown(startAt);
            FlowTrace.Step("Wave", $"BeginLoop EXIT — phase={_phase} countdownRemaining={_countdownRemaining:F2}s");
            Debug.Log($"[WaveManager] Loop armed — wave {startAt}, countdown {_countdownRemaining:F1}s.");
        }

        /// <summary>
        /// WO-579: the wave the loop should (re)start at. Resumes from the in-session/saved run
        /// progress so returning to the hub does not reset to wave 1. Seeds <see cref="s_resumeWaveId"/>
        /// once from <c>GameState.BestWave + 1</c> (cross-session) the first time it is needed; the dev
        /// <see cref="_startWave"/> is the floor (a dev override above the resume still wins).
        /// </summary>
        private int ResolveStartWave()
        {
            if (s_resumeWaveId <= 0)
            {
                var svc = GameStateService.Instance;
                int best = (svc != null && svc.State != null) ? svc.State.BestWave : 0;
                s_resumeWaveId = Mathf.Max(1, best + 1);
            }
            return Mathf.Max(_startWave, s_resumeWaveId);
        }

        // ── Robust start (TriggerWave-timeout RCA, layers 2 + 3) ──────────────
        //
        // A wave kickoff goes through the async BeginLoop(): it awaits the
        // WaveDataLoader loads, then EnterCountdown moves the phase off Idle. Two
        // things could leave the loop stranded at Idle and surface as a "wave never
        // started" timeout: (a) BeginLoop throws mid-flight (a load faults), or
        // (b) a transient null load forces _phase back to Idle. Both are now caught
        // and RETRIED a bounded number of times.
        //
        // GuardedKickoff wraps the start in try/catch (no silent failure — §12) and
        // arms RetryTillActive, a capped coroutine that re-fires the start only if
        // the phase has NOT left Idle within a short window. The NO-DOUBLE-START
        // guard: it re-fires ONLY from Idle (Countdown/Active/Breached/Complete/
        // Defeated all mean a wave already took, so it stops) — a real player's wave
        // that started normally is never re-kicked or stacked.

        private const int   StartRetryCap      = 3;     // bounded — never infinite
        private const float StartRetryWindow   = 1.5f;  // seconds to wait for Idle→off before a retry
        private Coroutine   _retryRoutine;

        // ── Stall watchdog (§12 "instrument + guard critical logic") ──────────
        //
        // The retry path above re-FIRES a stuck start, but a *silent stall* — the
        // phase never leaving Idle even after the retries, OR an async BeginLoop that
        // never resolves — is NOT a thrown exception, so Guard.Try / the GuardedKickoff
        // catch can never see it. The break-log only captures errors/exceptions, and the
        // headless Player.log is clobbered/shared, so a lost FlowTrace.Step at Idle leaves
        // us BLIND to where the start died.
        //
        // This watchdog DETECTS "didn't advance": armed alongside every kickoff, it waits
        // a generous window (well under any real countdown, generous for the async loads)
        // and, if the phase is STILL Idle (never reached Countdown/Active), emits ONE
        // captured FlowTrace.Fail (→ LogError → break-log.jsonl + WebTrace-able) with the
        // full state dump — which pinpoints the dead step: Idle+schedule==-1 ⇒ load never
        // completed; Idle+schedule>0 ⇒ loaded but countdown never entered; OTHER instance
        // ⇒ we armed the wrong (un-triggered) manager; etc. It fires AT MOST once per
        // kickoff and cancels the instant the phase advances (no false Fail on a slow-OK
        // start). Additive-only: it never touches wave balance/timing/spawn/retry logic.
        private const float StallWatchdogWindow = 9f;   // s before a still-Idle kickoff is declared STALLED
        private Coroutine   _stallWatchdogRoutine;

        /// <summary>
        /// Full wave-start state dump shared by every captured Fail (stall, throw,
        /// null-load, retry-exhaustion) so each surfaces the EXACT same diagnostic
        /// snapshot. Null-safe on every field. <paramref name="retries"/> = -1 when
        /// the caller has no retry count to report.
        /// </summary>
        private string StallStateDump(int retries)
        {
            string scene = "?";
            try { scene = gameObject.scene.name; } catch { /* torn-down */ }
            string which = (Instance == this) ? "self"
                         : (Instance == null ? "NULL" : "OTHER");
            return $"phase={_phase} "
                 + $"schedule={(_schedule?.Waves?.Count ?? -1)} "
                 + $"enemyCat={(_enemyCatalog?.Enemies?.Count ?? -1)} "
                 + $"retries={retries} forceSpawn={_forceSpawnNow} "
                 + $"instance={which} scene={scene}";
        }

        /// <summary>
        /// Watchdog coroutine: if the phase is STILL <see cref="WavePhase.Idle"/> after
        /// <see cref="StallWatchdogWindow"/>, emit ONE captured <see cref="FlowTrace.Fail"/>
        /// with the full <see cref="StallStateDump"/> — the silent stall announces itself
        /// instead of being a lost Step. Cancelled the instant the phase advances (yields
        /// each frame, checks Idle), so a normal slow-but-OK start never false-Fails.
        /// Fires at most once per armed kickoff.
        /// </summary>
        private IEnumerator StallWatchdog(string source)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < StallWatchdogWindow)
            {
                // Phase advanced past Idle → a wave took. No stall — clear + done.
                if (_phase != WavePhase.Idle) { _stallWatchdogRoutine = null; yield break; }
                yield return null;
            }

            // Window elapsed and STILL Idle → a silent stall. Announce it LOUDLY, once.
            if (_phase == WavePhase.Idle)
            {
                string dump = StallStateDump(-1);
                FlowTrace.Fail("Wave",
                    $"STALLED: kickoff '{source}' never left Idle after {StallWatchdogWindow:F0}s — {dump}");
                Debug.LogError($"[WaveManager] STALL WATCHDOG ({source}): {dump}");
            }
            _stallWatchdogRoutine = null;
        }

        /// <summary>Arms (re-arms) the stall watchdog for a kickoff. Null-safe; one at a time.</summary>
        private void ArmStallWatchdog(string source)
        {
            if (!isActiveAndEnabled) return;   // can't StartCoroutine on a disabled manager
            if (_stallWatchdogRoutine != null) StopCoroutine(_stallWatchdogRoutine);
            _stallWatchdogRoutine = StartCoroutine(StallWatchdog(source));
        }

        /// <summary>
        /// Fires BeginLoop() guarded by try/catch and arms the retry watchdog.
        /// Used by the player "Defend!" path and the bot/jump path so a faulted or
        /// stalled start self-heals instead of stranding the loop at Idle.
        /// </summary>
        private void GuardedKickoff(string source)
        {
            // FTUE GUARD (regression fix 2026-07-08): while the first-time tutorial is active the
            // ambient wave loop stays closed — BeginLoop() returns early on
            // TutorialFlow.HostilesSuppressedForTutorial. Firing the force-start path here would
            // arm RetryTillActive, which re-fires BeginLoop StartRetryCap times (each returns
            // suppressed → phase never leaves Idle) and then emits a FlowTrace.Fail to the
            // break-log for an EXPECTED state (the exact regression from the spawn-suppression fix).
            // Exit CLEANLY here — a Step, NOT a retry loop and NOT a Fail — so ANY force-start caller
            // (DEFEND button, dev seams, bot jump) is safe during the FTUE. The tutorial's OWN scripted
            // teaching wave uses TutorialWaveSpawner -> SpawnEnemyForExternalMode, NOT this loop, so it
            // is unaffected. This keys off !Onboarded exactly like the BeginLoop guard, so it LIFTS the
            // instant onboarding completes and post-tutorial DEFEND / force-start works normally.
            // (F8 2026-08-05: same predicate swap as BeginLoop — the wave-CLOCK window, not the
            // zone-scoped ambient one. Strict superset; no path loses a guard.)
            if (TutorialFlow.WaveLoopSuppressedForTutorial)
            {
                FlowTrace.Step("Wave", $"GuardedKickoff ({source}) — force-start suppressed: tutorial (FTUE) active; ambient wave loop stays closed until onboarding completes. No retry, no fail.");
                return;
            }

            try
            {
                FlowTrace.Step("Wave", $"GuardedKickoff ({source}) — BeginLoop().Forget() phase={_phase}");
                BeginLoop().Forget();
            }
            catch (Exception e)
            {
                FlowTrace.Fail("Wave", $"wave start threw ({source}): {e.Message} — {StallStateDump(-1)}");
                Debug.LogError($"[WaveManager] Wave start threw ({source}): {e}");
            }

            // Arm the watchdog (only one at a time). isActiveAndEnabled guards a
            // StartCoroutine on a disabled manager (e.g. mid scene-teardown).
            if (isActiveAndEnabled)
            {
                if (_retryRoutine != null) StopCoroutine(_retryRoutine);
                _retryRoutine = StartCoroutine(RetryTillActive(source));
            }

            // Arm the STALL watchdog: detect "phase never left Idle" (a silent stall
            // that no try/catch can see) and announce it as ONE captured Fail with state.
            ArmStallWatchdog(source);
        }

        /// <summary>
        /// Watchdog: if the phase is still Idle after <see cref="StartRetryWindow"/>,
        /// re-fire BeginLoop — up to <see cref="StartRetryCap"/> times, yielding
        /// between attempts (NEVER a busy/infinite loop). Re-fires ONLY from Idle so a
        /// wave that already started (any non-Idle phase) is never double-started; a
        /// transient null load that bounced the phase back to Idle gets another go,
        /// which also re-attempts the WaveDataLoader loads (load-guard, layer 3).
        /// </summary>
        private IEnumerator RetryTillActive(string source)
        {
            for (int attempt = 1; attempt <= StartRetryCap; attempt++)
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < StartRetryWindow)
                {
                    // Left Idle → a wave took. Done — do NOT re-fire (no double-start).
                    if (_phase != WavePhase.Idle) { _retryRoutine = null; yield break; }
                    yield return null;
                }

                // Still Idle after the window — only Idle is re-firable.
                if (_phase != WavePhase.Idle) { _retryRoutine = null; yield break; }

                FlowTrace.Warn("Wave", $"RetryTillActive ({source}) — phase still Idle after {StartRetryWindow:F1}s, retry {attempt}/{StartRetryCap}");
                Debug.LogWarning($"[WaveManager] Wave start did not leave Idle — retry {attempt}/{StartRetryCap} ({source}).");
                try { BeginLoop().Forget(); }
                catch (Exception e)
                {
                    FlowTrace.Fail("Wave", $"wave start retry {attempt} threw ({source}): {e.Message} — {StallStateDump(attempt)}");
                    Debug.LogError($"[WaveManager] Wave start retry {attempt} threw ({source}): {e}");
                }
            }

            if (_phase == WavePhase.Idle)
                FlowTrace.Fail("Wave", $"RetryTillActive ({source}) — phase STILL Idle after {StartRetryCap} retries; giving up — {StallStateDump(StartRetryCap)}");
            _retryRoutine = null;
        }

        /// <summary>
        /// Finds the Heart + spawn points in the scene when the inspector left
        /// them blank. The integrator may instead wire them by hand.
        /// </summary>
        private void ResolveSceneRefs()
        {
            if (_heart == null)
                _heart = FindAnyObjectByType<HeartController>();

            // WO-125 Bug 3: hook the Heart's lose condition once the Heart is known.
            // Idempotent — BeginLoop can re-run (e.g. after a Defend-the-Tower return),
            // and the guard keeps us from stacking duplicate handlers on the same Heart.
            if (_heart != null && !_heartDeathHooked)
            {
                _heart.OnHeartDestroyed += HandleHeartDestroyed;
                _heartDeathHooked = true;
            }

            if (_spawnPoints == null || _spawnPoints.Count == 0)
            {
                _spawnPoints = new List<WaveSpawnPoint>(
                    FindObjectsByType<WaveSpawnPoint>());
            }

            // A wave with no spawn markers can spawn ZERO enemies and then "clear"
            // itself instantly (each spawn path no-ops on an empty list), so the
            // countdown/loop appears to run but nothing ever attacks. Warn LOUDLY
            // once at loop-start so a missing-markers scene (e.g. MainCastle_Hall
            // before the spawn points are placed) is obvious instead of silent.
            if (_spawnPoints == null || _spawnPoints.Count == 0)
            {
                Debug.LogWarning(
                    "[WaveManager] No WaveSpawnPoint markers found in the scene — waves " +
                    "will spawn NO enemies and each wave will self-clear instantly. Place " +
                    "WaveSpawnPoint markers (tag 'SpawnPoint', ~12 m outside each gate) so " +
                    "the wave loop has somewhere to spawn. See docs/port-notes/week4-waves.md.");
            }

            if (_enemyRoot == null) _enemyRoot = transform;

            // Dynamic difficulty: arm the once-per-session history clear and subscribe to the
            // new-game signal. Both are idempotent and both retry on the next BeginLoop if Core
            // has not bootstrapped yet, so a late GameStateService is never missed.
            EnsureDifficultySessionReset();
            HookNewGameReset();
        }

        // =====================================================================
        //  Countdown phase
        // =====================================================================

        /// <summary>Enters the Prepare-Phase countdown for wave <paramref name="waveId"/>.</summary>
        private void EnterCountdown(int waveId)
        {
            FlowTrace.Step("Wave", $"EnterCountdown(waveId={waveId}) phaseBefore={_phase} forceSpawn={_forceSpawnNow}");

            WaveDef wave = _schedule.Find(waveId);
            if (wave == null)
            {
                // ENDLESS MODE (owner ruling 2026-07-11): past the authored schedule the loop
                // does NOT end — arm the next endless wave (cycled def, manual player start).
                if (TryArmEndlessWave(waveId)) return;

                // No such wave and endless couldn't resolve (empty/degenerate schedule, or a
                // gap INSIDE the authored range) — the schedule is exhausted.
                SetPhase(WavePhase.Complete, "EnterCountdown/schedule-exhausted");
                FlowTrace.Step("Wave", $"EnterCountdown: no WaveDef for waveId={waveId} — schedule exhausted, phase->Complete");
                Debug.Log($"[WaveManager] All {_schedule.Waves.Count} waves cleared — schedule complete.");
                return;
            }

            _currentWaveId = waveId;
            _enemyBestSqr.Clear();    // fresh stuck-tracking per wave
            _enemyStuckTime.Clear();

            // The lookout spots the incoming raid — blow the horn. This is the very
            // warning the FTUE teaches ("when you hear the horn, get to the gate").
            GameSfx.PlayLookoutHorn();

            // The between-wave build window scales with the player's chosen
            // difficulty: the canonical WaveDef.CountdownSeconds (45 s first
            // wave, 300 s later) is multiplied by the DifficultyTuning factor so
            // the owner targets land — Easy ~10 min, Normal ~5 min, Hard ~3 min
            // between waves. The multiplier is derived from the base seconds,
            // never hard-coded (see DifficultyTuning).
            _countdownRemaining = Mathf.Max(0f, ScaledCountdown(wave.CountdownSeconds));
            // DEV/bot immediate-spawn: ForceSpawnNextWaveNow set this from Idle — zero the
            // countdown so TickCountdown() spawns the wave on the next tick (no race with the
            // async BeginLoop setting the countdown above).
            bool zeroedByForce = _forceSpawnNow;
            if (_forceSpawnNow) { _forceSpawnNow = false; _countdownRemaining = 0f; }
            SetPhase(WavePhase.Countdown, "EnterCountdown");
            FlowTrace.Step("Wave", $"EnterCountdown -> phase=Countdown wave={_currentWaveId} countdown={_countdownRemaining:F2}s zeroedByForceSpawn={zeroedByForce}");
            OnCountdownTick.Invoke(_countdownRemaining);
            WaveCountdownUI.Instance?.StartCountdown(_countdownRemaining);
        }

        // =====================================================================
        //  Endless mode (owner ruling 2026-07-11 — past the authored schedule)
        // =====================================================================

        /// <summary>
        /// Arms endless wave <paramref name="waveId"/> (a wave BEYOND the authored schedule):
        /// resolves its cycled authored def, then parks the loop in phase Countdown with the
        /// countdown held at 0 and <see cref="_awaitingPlayerStart"/> set — the wave will not
        /// spawn until the player presses the HUD DEFEND button (ForceBeginNextWave) or a
        /// dev/bot force-start fires. A pending <see cref="_forceSpawnNow"/> (bot jump) skips
        /// the wait and spawns on the next tick, exactly like the authored-wave force path.
        /// Returns false when no endless def resolves (empty schedule / waveId inside the
        /// authored range) so the caller falls through to the legacy Complete handling.
        /// </summary>
        private bool TryArmEndlessWave(int waveId)
        {
            WaveDef src = ResolveEndlessWaveDef(waveId, out int sourceWaveId);
            if (src == null) return false;

            _currentWaveId = waveId;
            _enemyBestSqr.Clear();    // fresh stuck-tracking per wave (parity with EnterCountdown)
            _enemyStuckTime.Clear();

            float countScale = EndlessCountScale(waveId);

            bool zeroedByForce = _forceSpawnNow;
            if (_forceSpawnNow) { _forceSpawnNow = false; _awaitingPlayerStart = false; }
            else _awaitingPlayerStart = true;

            // Phase Countdown + remaining 0 + waiting flag: TickCountdown holds (never
            // decrements past the flag), the HUD DEFEND button shows (StartWaveHudBridge
            // treats Countdown as available), and the HudKit "Next wave in Ns" label stays
            // blank (it only renders while CountdownRemaining > 0). No horn yet — the calm
            // build phase is open-ended; the lookout horn blows when the wave actually starts.
            _countdownRemaining = 0f;
            SetPhase(WavePhase.Countdown, "TryArmEndlessWave");
            FlowTrace.Step("Wave",
                $"endless wave {waveId}: def={sourceWaveId} countScale=x{countScale:F2} " +
                (zeroedByForce ? "force-spawning now (bot/jump)" : "awaiting player start"));
            OnCountdownTick.Invoke(0f);
            Debug.Log($"[WaveManager] Endless mode — wave {waveId} armed (cycled authored wave {sourceWaveId}, " +
                      $"mob count x{countScale:F2}). " +
                      (zeroedByForce ? "Force-spawning now." : "Waiting for the player's DEFEND."));
            return true;
        }

        /// <summary>
        /// Resolves the authored WaveDef an endless wave replays. Only waves STRICTLY beyond
        /// the last authored wave resolve (a gap inside the authored range stays a real gap).
        /// Cycling rule: waves <see cref="EndlessCycleStartWaveId"/>..max replay IN ORDER, so
        /// with the 20-wave schedule true wave 21 → def 4, 22 → def 5, …, 37 → def 20 (the
        /// apex dragon returns as every cycle's capstone), 38 → def 4 again. The TRUE wave
        /// number keeps counting so WaveScalingCurve keeps scaling HP/speed/damage past 20.
        /// </summary>
        private WaveDef ResolveEndlessWaveDef(int waveId, out int sourceWaveId)
        {
            sourceWaveId = waveId;
            if (_schedule == null) return null;
            int max = _schedule.MaxWaveId;
            if (max <= 0 || waveId <= max) return null;

            int cycleStart = Mathf.Clamp(EndlessCycleStartWaveId, 1, max);
            int cycleLen = max - cycleStart + 1;
            sourceWaveId = cycleStart + (waveId - max - 1) % cycleLen;
            return _schedule.Find(sourceWaveId);
        }

        /// <summary>
        /// The endless mob-count multiplier for <paramref name="waveId"/>: 1 within the
        /// authored schedule, then 1 + growth × (waveId − lastAuthored), capped. Growth/cap
        /// are DATA-DRIVEN from the waves.json "endless" block (defaults +5%/wave, cap 3×)
        /// so the owner tunes balance without a code edit.
        /// </summary>
        private float EndlessCountScale(int waveId)
        {
            if (_schedule == null) return 1f;
            int max = _schedule.MaxWaveId;
            if (max <= 0 || waveId <= max) return 1f;

            float growth = _schedule.Endless != null ? _schedule.Endless.CountGrowthPerWave : 0.05f;
            float cap    = _schedule.Endless != null ? _schedule.Endless.CountCap : 3f;
            if (growth <= 0f) return 1f;
            float scale = 1f + growth * (waveId - max);
            return cap > 0f ? Mathf.Min(scale, cap) : scale;
        }

        /// <summary>
        /// Clones an authored WaveDef for an endless wave with every batch count multiplied by
        /// <paramref name="countScale"/> (rounded UP, never below the authored count). A CLONE —
        /// never mutates the cached schedule, since cycled defs are replayed every cycle.
        /// Boss / apexBoss declarations carry over unchanged (stat growth is WaveScalingCurve's job).
        /// </summary>
        private static WaveDef CloneWaveWithScaledCounts(WaveDef src, int waveId, float countScale)
        {
            var clone = new WaveDef
            {
                WaveId           = waveId,
                Name             = src.Name,
                CountdownSeconds = src.CountdownSeconds,
                Boss             = src.Boss,
                BossHp           = src.BossHp,   // WO-789: keep the ground-boss HP pin in endless replays
                ApexBoss         = src.ApexBoss,
                Enemies          = new List<WaveBatch>(src.Enemies != null ? src.Enemies.Count : 0),
            };
            if (src.Enemies != null)
            {
                foreach (WaveBatch b in src.Enemies)
                {
                    if (b == null) continue;
                    clone.Enemies.Add(new WaveBatch
                    {
                        Type       = b.Type,
                        Count      = Mathf.Max(b.Count, Mathf.CeilToInt(b.Count * countScale)),
                        SpawnPoint = b.SpawnPoint,
                        Delay      = b.Delay,
                        Interval   = b.Interval,
                    });
                }
            }
            return clone;
        }

        /// <summary>
        /// Scales a wave's authored <paramref name="baseCountdown"/> by the
        /// difficulty the save records. Reads <see cref="GameState.Difficulty"/>
        /// through <see cref="GameStateService"/>; if Core is not bootstrapped
        /// (no service) it falls back to Normal so the loop is never blocked.
        /// </summary>
        private static float ScaledCountdown(float baseCountdown)
        {
            var svc = GameStateService.Instance;
            Difficulty difficulty = (svc != null && svc.State != null)
                ? svc.State.Difficulty
                : Difficulty.Normal;
            return baseCountdown * DifficultyTuning.CountdownMultiplier(difficulty);
        }

        private void TickCountdown()
        {
            // ENDLESS manual start: the countdown is parked (held at 0, never ticking) until
            // the player fires the DEFEND button / a force-start seam. StartWave clears the flag.
            if (_awaitingPlayerStart) return;

            _countdownRemaining -= Time.deltaTime;
            if (_countdownRemaining <= 0f)
            {
                _countdownRemaining = 0f;
                OnCountdownTick.Invoke(0f);
                FlowTrace.Step("Wave", $"TickCountdown: countdown hit 0 for wave={_currentWaveId} — calling StartWave (phase Countdown->Active)");
                StartWave(_currentWaveId);
            }
            else
            {
                OnCountdownTick.Invoke(_countdownRemaining);
            }
        }

        /// <summary>
        /// Owner-facing: skip the remaining Prepare-Phase and start the
        /// current wave immediately. Used by the HUD's "Trigger Wave" button
        /// and the AdminOverlay's debug shortcut. Idle / Complete phases
        /// route into the next countdown so the call is always meaningful.
        /// </summary>
        public void ForceBeginNextWave()
        {
            FlowTrace.Step("Wave", $"ForceBeginNextWave (PLAYER 'Defend!' path) phase={_phase}");
            switch (_phase)
            {
                case WavePhase.Countdown:
                    _countdownRemaining = 0f;
                    OnCountdownTick.Invoke(0f);
                    StartWave(_currentWaveId);
                    break;
                case WavePhase.Active:
                    Debug.Log("[WaveManager] ForceBeginNextWave called during active wave — ignored (current wave already running).");
                    break;
                case WavePhase.Idle:
                case WavePhase.Complete:
                default:
                    Debug.Log("[WaveManager] ForceBeginNextWave kicking the wave loop from " + _phase);
                    GuardedKickoff("ForceBeginNextWave");
                    break;
            }
        }

        /// <summary>
        /// DEV / bot / "Jump to wave" button: spawn the next wave IMMEDIATELY — skip the
        /// prepare-phase countdown. Distinct from <see cref="ForceBeginNextWave"/> (the normal
        /// kickoff, which keeps the calm countdown). Fixes the AutoPilot 'TriggerWave timeout'
        /// (the bot triggered from Idle → got a 45s countdown → timed out at 20s) and makes the
        /// owner's "Trigger next wave" button actually jump to the wave instead of starting a timer.
        /// </summary>
        public void ForceSpawnNextWaveNow()
        {
            FlowTrace.Step("Wave", $"ForceSpawnNextWaveNow (BOT/jump path) phase={_phase} forceSpawn-set");
            switch (_phase)
            {
                case WavePhase.Countdown:
                    FlowTrace.Step("Wave", $"ForceSpawnNextWaveNow: from Countdown — zero countdown + StartWave({_currentWaveId})");
                    _countdownRemaining = 0f;
                    OnCountdownTick.Invoke(0f);
                    StartWave(_currentWaveId);
                    break;
                case WavePhase.Active:
                    FlowTrace.Step("Wave", "ForceSpawnNextWaveNow during active wave — ignored.");
                    Debug.Log("[WaveManager] ForceSpawnNextWaveNow during active wave — ignored.");
                    break;
                default: // Idle / Complete — kick the loop but zero the countdown so it spawns now.
                    FlowTrace.Step("Wave", $"ForceSpawnNextWaveNow: from {_phase} — setting _forceSpawnNow=true + GuardedKickoff (try/catch + retry-till-active)");
                    _forceSpawnNow = true;
                    GuardedKickoff("ForceSpawnNextWaveNow");
                    break;
            }
        }

        // =====================================================================
        //  Active wave — spawn + breach watch
        // =====================================================================

        /// <summary>Begins spawning wave <paramref name="waveId"/>'s enemies.</summary>
        private void StartWave(int waveId)
        {
            WaveDef wave = _schedule.Find(waveId);
            if (wave == null)
            {
                // ENDLESS MODE: replay the cycled authored def with the endless mob-count
                // multiplier baked into a CLONE (the cached schedule is never mutated).
                // The TRUE waveId is what _currentWaveId carries, so WaveScalingCurve keeps
                // scaling HP/speed/damage by the real wave number in every spawn path.
                WaveDef src = ResolveEndlessWaveDef(waveId, out int sourceWaveId);
                if (src != null)
                {
                    float countScale = EndlessCountScale(waveId);
                    wave = CloneWaveWithScaledCounts(src, waveId, countScale);
                    _awaitingPlayerStart = false;
                    // The lookout horn was held back while the endless build phase waited on
                    // the player — blow it now the raid is actually released.
                    GameSfx.PlayLookoutHorn();
                    FlowTrace.Step("Wave",
                        $"endless wave {waveId}: START — def={sourceWaveId} countScale=x{countScale:F2} (player/force released)");
                }
            }
            if (wave == null) { FlowTrace.Step("Wave", $"StartWave: no WaveDef for {waveId} — rolling to next countdown"); EnterCountdown(waveId + 1); return; }

            // Recurring five-wave skill check from wave 20 onward. Endless composition can be
            // replaying any authored family wave, so overlay a fresh declaration rather than
            // tying the dragon to the old authored replay-cycle length.
            ApexBossDef recurringDragon = ResolveRecurringDragon(waveId, wave.ApexBoss);
            if (recurringDragon != null)
            {
                wave.ApexBoss = recurringDragon;
                FlowTrace.Step("Waves",
                    $"wave {waveId}: recurring dragon tier {DragonReturnTier(waveId)} armed " +
                    $"(hp={recurringDragon.Hp:0}, damage=x{DragonDamageMultiplierForWave(waveId):F2}, " +
                    $"attackInterval=x{DragonAttackIntervalMultiplierForWave(waveId):F2}).");
            }

            // WO-1308: THE ONLY WRITER OF Active in the whole class. If a stuck battle-lock is
            // ever traced back to a latched Active phase, this line is where it was raised.
            SetPhase(WavePhase.Active, "StartWave");
            FlowTrace.Step("Wave", $"StartWave({waveId}) -> phase=Active (spawning begins)");

            // Prep screens may be open during the countdown. At Active they must yield
            // before the first spawn: the siege keeps damaging the Heart behind a modal.
            // The probe above then rejects ordinary panels until the wave is over; Pause
            // and EndState remain available through RegisterBattleAllowed.
            PanelManager.CloseAll();
            FlowTrace.Step("Wave", $"StartWave({waveId}) -> ordinary modals closed before spawn");

            // ENCOUNTER TELEMETRY: stamp COMBAT start + reset the four measurements HERE — the
            // frame the phase turns Active — NOT at EnterCountdown. The countdown is the build
            // window; conflating the two would add up to 300s (further scaled by the player's
            // difficulty SETTING) to every measured clear time.
            BeginEncounterTelemetry(waveId);

            _breachArmed = false;
            _breachArmTimer = 0f;
            _breachRoster.Clear();
            _liveApexBoss = null;
            _apexSpawnPending = false;
            OnWaveStarted.Invoke(waveId);

            // An apex (flying-boss) wave drives the Heart's Boss threat state;
            // a normal wave only raises it to Vigilant.
            if (_heart != null)
                _heart.SetState(wave.IsApexBossWave ? HeartState.Boss : HeartState.Vigilant);

            // WO-362: SMART composition + tactical positioning takes priority. When on,
            // GENERATE the wave's ground roster from the wave number (tiered mix + elite
            // cadence + anti-repeat) and place each enemy by role at a ROTATING gate,
            // instead of releasing the flat waves.json batches. Falls through to the
            // legacy paths if it spawns nothing (e.g. no spawn points / catalog).
            // WO-1113: a fresh wave owes the field nothing until its own composition defers.
            // (A previous wave's drain, if any is still awake, bails on the phase/roster checks.)
            _heldSmartReinforcements = 0;

            bool composed = false;
            if (_smartComposition)
            {
                composed = SpawnSmartComposedWave(waveId, wave);
                if (composed) WarnAuthoredBatchesDiscarded(wave, waveId);
            }

            // WO-316: compose the wave's batches into runtime role-mix FAMILY squads
            // (tank + healer + a few DPS that hold then charge together) routed through
            // the EnemyGroupSpawner / EnemyGroupCoordinator. Falls back to the flat
            // per-batch SpawnBatch stream when composition is off or yields nothing
            // (so the legacy path is always available for back-compat).
            if (!composed && _composeFamilyGroups && wave.Enemies != null && wave.Enemies.Count > 0)
                composed = SpawnComposedFamilyGroups(wave);

            if (!composed && wave.Enemies != null)
            {
                // Legacy flat path: each batch spawns on its own delayed UniTask.
                foreach (WaveBatch batch in wave.Enemies)
                {
                    if (batch != null) SpawnBatch(batch).Forget();
                }
            }

            // DEF-21: group spawner — if a WaveEnemyGroup asset is assigned for
            // this wave slot, spawn it alongside the JSON batches. Both systems
            // complement each other; leave waves.json entries empty for group-only waves.
            int groupIdx = waveId - 1;
            if (_groupSpawner != null
                && _waveGroupSequence != null
                && groupIdx >= 0
                && groupIdx < _waveGroupSequence.Count
                && _waveGroupSequence[groupIdx] != null)
            {
                WaveSpawnPoint pt = (_spawnPoints != null && _spawnPoints.Count > 0)
                    ? _spawnPoints[0] : null;
                Vector3 spawnPos = pt != null
                    ? pt.transform.position
                    : transform.position;

                List<Enemy> groupEnemies = _groupSpawner.SpawnGroup(
                    _waveGroupSequence[groupIdx],
                    spawnPos,
                    _heart != null ? _heart.transform : null,
                    _enemyRoot,
                    _currentWaveId,
                    ref _spawnInstanceCounter);

                foreach (Enemy e in groupEnemies)
                {
                    if (e == null) continue;
                    e.Died          += HandleEnemyDied;
                    e.ReachedHeart  += HandleEnemyReachedHeart;
                    _liveEnemies.Add(e);
                }
            }

            // ROBUSTNESS (castle / missing-markers): if every ground-spawn path produced
            // ZERO enemies and this is not a boss/apex wave, the wave would otherwise
            // self-clear on the next TickActiveWave (LiveEnemies == 0) and silently
            // advance — the timer/loop "runs" but no enemy ever appears. Surface it with
            // one clear warning so the cause (no spawn points / empty roster) is obvious.
            bool willHaveBoss = WaveHasAuthoredHeavy(wave);
            if (_liveEnemies.Count == 0 && !willHaveBoss)
            {
                Debug.LogWarning(
                    $"[WaveManager] Wave {waveId} started but spawned ZERO enemies — it will " +
                    "self-clear instantly. Likely cause: no WaveSpawnPoint markers in the " +
                    "scene (place them, tag 'SpawnPoint', ~12 m outside each gate) or an empty " +
                    "enemy roster/catalog.");
            }

            // A boss, if the wave names one, releases immediately.
            // WO-789: wave.BossHp > 0 pins the boss's HP to exactly that value
            // (applied in SpawnOne AFTER the WaveScalingCurve pass — see the pin there).
            //
            // 2026-08-16: this used to pass a hardcoded SpawnPoint = "spawn-0" while the
            // comment claimed "at the north spawn". No live producer emits that id — the
            // only one, CastleSpawnPointInjector, emits "spawn-castle-{dir}-{i}" — so the
            // lookup ALWAYS missed and fell through to the first element of an UNORDERED
            // FindObjectsByType list: the boss walked in from a random side every session,
            // announced only by a Debug.LogWarning the F8 harness never sees. The id is now
            // RESOLVED from the markers that actually exist, and a miss is loud.
            if (!string.IsNullOrEmpty(wave.Boss))
            {
                WaveSpawnPoint bossSpawn = WaveSpawnResolver.ResolveBossSpawn(
                    _spawnPoints, out string bossSpawnReason, out bool bossSpawnExact);

                if (bossSpawn == null)
                {
                    FlowTrace.Fail("Wave",
                        $"wave {waveId} boss '{wave.Boss}': NO spawn point resolved ({bossSpawnReason}). " +
                        "The boss will materialise at the WaveManager transform instead of a gate — " +
                        "place WaveSpawnPoint markers (CastleSpawnPointInjector emits " +
                        "'spawn-castle-<dir>-<i>' in the castle hub).");
                }
                else if (!bossSpawnExact)
                {
                    FlowTrace.Warn("Wave",
                        $"wave {waveId} boss '{wave.Boss}': {bossSpawnReason}. The boss enters from a " +
                        "side nobody authored — expected the " +
                        $"'{WaveSpawnResolver.PreferredBossDirection}' approach.");
                }
                else
                {
                    FlowTrace.Step("Wave",
                        $"wave {waveId} boss '{wave.Boss}': {bossSpawnReason} " +
                        $"(direction '{bossSpawn.Direction}', gate {bossSpawn.GateIndex}).");
                }

                SpawnBatch(new WaveBatch
                {
                    Type = wave.Boss,
                    Count = 1,
                    // Empty when nothing resolved: FindSpawnPoint then takes its deterministic
                    // fallback WITHOUT re-reporting a miss that was already Failed above.
                    SpawnPoint = bossSpawn != null ? bossSpawn.SpawnId : string.Empty,
                    Delay = 0f,
                    Interval = 0f,
                }, wave.BossHp).Forget();
            }

            // An APEX wave fields the kinematic flying boss (the dragon). Unlike
            // wave.Boss above, this is NOT a NavMesh enemy from enemies.json — it
            // is the Boss_Dragon prefab driven by DragonBoss. Released at once so
            // the dragon is aloft the moment the apex wave begins.
            if (wave.IsApexBossWave)
                SpawnApexBoss(wave.ApexBoss);
        }

        private ApexBossDef ResolveRecurringDragon(int waveId, ApexBossDef current)
        {
            // Endless replay can hand us wave-20's apex declaration at true wave 37. Strip
            // that legacy cycle artifact: after 20, ONLY the five-wave cadence is authoritative.
            if (!IsRecurringDragonWave(waveId)) return waveId > FirstDragonWave ? null : current;

            ApexBossDef template = _schedule != null ? _schedule.Find(FirstDragonWave)?.ApexBoss : null;
            template ??= current;
            if (template == null)
            {
                FlowTrace.Fail("Waves",
                    $"wave {waveId}: recurring dragon cadence fired but wave {FirstDragonWave} has no apexBoss template.");
                return null;
            }

            return new ApexBossDef
            {
                Id = $"{(string.IsNullOrEmpty(template.Id) ? "boss-dragon" : template.Id)}-wave{waveId}",
                Hp = DragonHpForWave(template.Hp > 0f ? template.Hp : 4200f, waveId),
                NameKey = template.NameKey,
            };
        }

        /// <summary>Fire-once-per-session guard for the authored-batch discard warning
        /// (see <see cref="WarnAuthoredBatchesDiscarded"/>).</summary>
        private bool _warnedAuthoredBatchesDiscarded;

        /// <summary>
        /// DATA-ROT GUARD (2026-07-30). The WO-362 smart path just GENERATED this wave's
        /// roster, so the authored waves.json enemies[] batches were DISCARDED -- type,
        /// count, spawnPoint, delay and interval all had ZERO effect. That supersession is
        /// BY DESIGN (see the _smartComposition tooltip and WO-362), but it is INVISIBLE to
        /// whoever authored the schedule: a designer edits waves.json and gets silence.
        ///
        /// It already bit us. WO-362 landed mid-June; the 20-wave schedule in waves.json was
        /// authored 2026-07-11 -- about four weeks AFTER the batches went inert -- against a
        /// port that no longer runs. waves.json's own comments still describe the dead
        /// consumption path. Today 19 waves / 55 batch entries / 148 authored enemies are
        /// thrown away every session, and nothing said so.
        ///
        /// Say it ONCE per session, loud, with the numbers, so any capture self-reports the
        /// dead authoring instead of a designer discovering it by absence.
        /// OPEN OWNER RULING (WO-783): which authority wins -- set _smartComposition=0 so the
        /// authored schedule is live again, or strip enemies[] from waves.json and keep
        /// generation. Until then this warning is the only witness.
        /// </summary>
        private void WarnAuthoredBatchesDiscarded(WaveDef wave, int waveId)
        {
            if (_warnedAuthoredBatchesDiscarded) return;
            if (wave == null || wave.Enemies == null || wave.Enemies.Count == 0) return;
            _warnedAuthoredBatchesDiscarded = true;

            FlowTrace.Warn("Wave",
                $"authored waves.json batches IGNORED: wave {waveId} declares {wave.Enemies.Count} batch(es) " +
                $"totalling {wave.TotalEnemyCount} enemies (types / counts / spawnPoints / delays), but " +
                "_smartComposition=ON generated the roster instead (WaveCompositionBuilder pools + rotating gate). " +
                "Only countdownSeconds, boss and apexBoss still take effect. EVERY later wave is the same -- " +
                "warned once per session. Reconcile waves.json or set _smartComposition=0 (WO-783).");
        }

        /// <summary>
        /// WO-316: composes the wave's flat batches into runtime role-mix FAMILY
        /// squads and releases each as ONE coordinated group via the existing
        /// <see cref="EnemyGroupSpawner.SpawnComposedGroup"/> (formation spread,
        /// NavMesh snap, per-member <see cref="EnemyRole"/>, and the
        /// <see cref="EnemyGroupCoordinator"/> "hold then charge together" release).
        ///
        /// Each batch's <c>type</c> resolves to an <see cref="EnemyDef"/>; the def's
        /// <see cref="EnemyDef.Family"/> buckets the batch, its <see cref="EnemyDef.RoleKind"/>
        /// assigns the tactical role, and its <c>count</c> sets how many. So a wave
        /// of "3 hollow-walker + 1 hollow-warrior + 2 hollow-rogue" becomes ONE
        /// Hollow squad of 3 DPS + 1 Tank + 2 Ranged that advance and charge as a
        /// unit, instead of three single-type conga lines.
        ///
        /// Returns true if at least one group was spawned (caller skips the flat
        /// path); false when nothing resolved (caller falls back to flat batches).
        /// </summary>
        private bool SpawnComposedFamilyGroups(WaveDef wave)
        {
            if (wave?.Enemies == null || _enemyCatalog == null) return false;

            // Lazily build a spawner so composition works even when the inspector
            // left _groupSpawner blank (the common case on the live scene).
            if (_groupSpawner == null)
            {
                _groupSpawner = GetComponentInChildren<EnemyGroupSpawner>();
                if (_groupSpawner == null)
                {
                    var go = new GameObject("[EnemyGroupSpawner]");
                    go.transform.SetParent(transform, false);
                    _groupSpawner = go.AddComponent<EnemyGroupSpawner>();
                }
            }

            // Bucket the batches by family, preserving the first spawn point seen
            // for each family so the squad materialises at the gate its batch named.
            var byFamily   = new Dictionary<string, List<ComposedGroupMember>>();
            var familySpawn = new Dictionary<string, string>();

            foreach (WaveBatch batch in wave.Enemies)
            {
                if (batch == null) continue;
                EnemyDef def = _enemyCatalog.Find(batch.Type);
                if (def == null)
                {
                    Debug.LogWarning($"[WaveManager] WO-316 compose: unknown enemy '{batch.Type}' in wave {wave.WaveId} — skipped.");
                    continue;
                }

                string family = string.IsNullOrEmpty(def.Family) ? "hollow" : def.Family;
                if (!byFamily.TryGetValue(family, out var members))
                {
                    members = new List<ComposedGroupMember>();
                    byFamily[family] = members;
                    familySpawn[family] = batch.SpawnPoint;
                }
                members.Add(new ComposedGroupMember(def, def.RoleKind, Mathf.Max(1, batch.Count)));
            }

            if (byFamily.Count == 0) return false;

            Transform heartT = _heart != null ? _heart.transform : null;
            bool spawnedAny = false;

            foreach (var kv in byFamily)
            {
                WaveSpawnPoint pt = FindSpawnPoint(familySpawn[kv.Key]);
                Vector3 spawnPos = pt != null ? pt.transform.position
                                 : (_spawnPoints != null && _spawnPoints.Count > 0 && _spawnPoints[0] != null
                                        ? _spawnPoints[0].transform.position
                                        : transform.position);

                List<Enemy> squad = _groupSpawner.SpawnComposedGroup(
                    kv.Value,
                    spawnPos,
                    heartT,
                    _enemyRoot,
                    _composedFormation,
                    $"{kv.Key}-squad",
                    _currentWaveId,
                    ref _spawnInstanceCounter);

                foreach (Enemy e in squad)
                {
                    if (e == null) continue;

                    // DEF-59 / CITY-01: apply wave-scaling parity with the flat SpawnOne
                    // path. EnsureScalingCurve NEVER returns null (falls back to a runtime
                    // DEFAULT curve when no asset is wired), so enemies always escalate per
                    // wave even in a scene that ships no WaveScalingCurve.asset.
                    var composedCurve = EnsureScalingCurve();
                    e.ApplyWaveScaling(
                        composedCurve.HpMultiplier(_currentWaveId),
                        composedCurve.SpeedMultiplier(_currentWaveId),
                        composedCurve.DamageMultiplier(_currentWaveId));

                    // PATCH 2 — DYNAMIC DIFFICULTY, applied as base*mult on a base captured
                    // FRESH for this spawn. The body is pooled: `stat *= mult` here would
                    // compound exponentially across reuses (see Enemy's base-stat block).
                    e.SetBaseStats(e.MaxHp, e.ContactDamage);
                    e.ApplyDifficulty(DynamicDifficulty.EnemyHpMultiplier,
                                      DynamicDifficulty.EnemyDamageMultiplier);

                    e.Died         += HandleEnemyDied;
                    e.ReachedHeart += HandleEnemyReachedHeart;
                    _liveEnemies.Add(e);
                    spawnedAny = true;
                }
            }

            return spawnedAny;
        }

        /// <summary>
        /// CITY-01: returns the wave-scaling curve, lazily creating a DEFAULT runtime
        /// instance when none is wired in the Inspector. The wave scenes ship NO
        /// WaveScalingCurve.asset, so <see cref="_scalingCurve"/> deserializes null and
        /// every enemy used to spawn at wave-1 stats forever (wave 19 == wave 1). This
        /// guarantees a non-null curve: <see cref="ScriptableObject.CreateInstance"/> runs
        /// WaveScalingCurve's field initializers, which seed the default HP/speed/damage
        /// curves (1.0x at wave 1 -> 2.5x/1.4x/2.0x by wave 20, clamped after). Created
        /// once and cached, so the applied multiplier is provably &gt;1 past wave 1
        /// (headless: DataRegression [wave-scaling]). Never returns null.
        /// </summary>
        private WaveScalingCurve EnsureScalingCurve()
        {
            if (_scalingCurve == null)
            {
                _scalingCurve = ScriptableObject.CreateInstance<WaveScalingCurve>();
                _scalingCurve.name = "WaveScalingCurve (runtime default)";
                FlowTrace.Warn("Wave",
                    "no WaveScalingCurve asset wired -> created runtime DEFAULT curve " +
                    "(HP 1.0->2.5, speed 1.0->1.4, dmg 1.0->2.0 across waves 1..20); " +
                    "enemies now escalate per wave.");
            }
            return _scalingCurve;
        }

        /// <summary>
        /// WO-362: generates this wave's ground roster via
        /// <see cref="WaveCompositionBuilder.Build"/> (tiered weak/medium/strong mix,
        /// an elite every 5th wave UNLESS waves.json already authors that wave's heavy
        /// — see <see cref="WaveHasAuthoredHeavy"/> — no two consecutive waves identical, count + difficulty
        /// scaling with the wave number) and releases it through the
        /// <see cref="SmartEnemySpawner"/>, which positions each enemy by tactical role
        /// (tanks front-centre, archers backline, weak trailing) at a gate that ROTATES
        /// N→E→S→W across waves. Subscribes Died / ReachedHeart and applies wave-scaling
        /// with full parity to the legacy <see cref="SpawnOne"/> / compose paths.
        ///
        /// Returns true if at least one enemy spawned (caller skips the legacy paths);
        /// false when nothing resolved (caller falls back to compose / flat batches).
        /// </summary>
        private bool SpawnSmartComposedWave(int waveId, WaveDef wave)
        {
            if (_enemyCatalog == null) return false;

            // Lazily build the spawner (no inspector wiring needed on the live scene).
            if (_smartSpawner == null)
            {
                _smartSpawner = GetComponentInChildren<SmartEnemySpawner>();
                if (_smartSpawner == null)
                {
                    var go = new GameObject("[SmartEnemySpawner]");
                    go.transform.SetParent(transform, false);
                    _smartSpawner = go.AddComponent<SmartEnemySpawner>();
                }
            }

            // ONE HEAVY AUTHORITY PER WAVE (2026-08-16): tell the builder whether waves.json
            // already authors this wave's heavy, so its every-5th-wave elite cadence defers
            // instead of stacking a second boss-class enemy on top of the authored one.
            EnemyWaveComposition composition =
                WaveCompositionBuilder.Build(waveId, WaveHasAuthoredHeavy(wave), _enemyCatalog);
            if (composition == null || composition.Entries.Count == 0) return false;

            // ENDLESS MODE: the smart path generates its roster from the TRUE wave number
            // (so its own count/difficulty ramp continues past the schedule), but the
            // owner-tunable endless mob-count multiplier from waves.json applies ON TOP —
            // each slot's count is scaled (rounded up, never below the generated count),
            // exactly like the batch paths. No-op (x1) within the authored schedule.
            float endlessScale = EndlessCountScale(waveId);
            if (endlessScale > 1f)
            {
                for (int i = 0; i < composition.Entries.Count; i++)
                {
                    WaveCompositionEntry entry = composition.Entries[i];
                    entry.Count = Mathf.Max(entry.Count, Mathf.CeilToInt(entry.Count * endlessScale));
                    composition.Entries[i] = entry;   // struct — write back
                }
                FlowTrace.Step("Wave",
                    $"endless wave {waveId}: smart composition countScale=x{endlessScale:F2} total={composition.TotalCount}");
            }

            Transform heartT = _heart != null ? _heart.transform : null;

            // ── DEF-48 CONCURRENCY CAP, NOW ENFORCED ON THE LIVE PATH (WO-1113) ──────
            //
            // THE DEFECT this closes: _maxSimultaneousEnemies was read in EXACTLY ONE place —
            // SpawnBatch, the LEGACY flat path. _smartComposition ships ON, so every wave the
            // player actually meets came through here, where the cap did not exist: the whole
            // composition was released in one frame, up to WaveCompositionBuilder.MaxCount = 22
            // bodies (more in endless, where each slot is scaled up). On a phone that is the
            // difference between a budgeted fight and a frame-rate cliff, and the serialized
            // field promised a ceiling it could not deliver.
            //
            // ⚠ THE CAP HOLDS COUNT CONSTANT, IT DOES NOT THIN THE WAVE. Everything over the
            // budget is HELD in `deferred` and released as reinforcements the moment a slot
            // frees, so the wave's total roster — and its clear condition — are byte-identical
            // to before. What changes is PACING: a late wave arrives as a sustained pressure
            // front instead of one 22-body dump. That is a felt change and the owner should
            // felt-verify it; set _maxSimultaneousEnemies = 0 to restore the old all-at-once
            // release without touching code.
            var deferred = new List<WaveCompositionEntry>();
            int budget = SmartSpawnBudget();

            // The field is ALREADY full (a straggler survived into this wave). BudgetFor answers
            // 0 for "no cap" as well as for "no room", and SpawnWave reads 0 as UNLIMITED — so
            // calling it here would dump the entire roster, i.e. the exact opposite of the cap.
            // Hold everything and let the drain release it as slots free.
            if (_maxSimultaneousEnemies > 0 && budget <= 0)
            {
                for (int i = 0; i < composition.Entries.Count; i++) deferred.Add(composition.Entries[i]);
                _heldSmartReinforcements = CountOf(deferred);
                _reinforcementDrainUnscaled = Time.unscaledTime;   // WO-1308: a fresh hold is not a stale one
                FlowTrace.Warn("Wave",
                    $"wave {waveId}: field is already at the concurrency cap " +
                    $"({_liveEnemies.Count}/{_maxSimultaneousEnemies}) — releasing NOTHING now and " +
                    $"holding all {_heldSmartReinforcements} for reinforcement. WARNING: the " +
                    $"{SmartEnemySpawner.SideCountForWave(waveId)}-side escalation for this wave " +
                    "therefore arrives ENTIRELY as reinforcements, side by side as slots free - " +
                    "it will read as a trickle, not a simultaneous multi-side assault.");
                DrainSmartReinforcements(deferred, waveId, composition).Forget();
                return true;   // composed: the legacy batch paths must NOT also fire
            }

            // ⛔ WO-1179 — THIS IS **ONE** CALL, AND IT MUST STAY ONE CALL.
            // Waves now attack from 1 / 2 / 4 SIDES (SmartEnemySpawner.SideCountForWave), but the
            // side split happens INSIDE SpawnWave, under the SINGLE `budget` computed above.
            // Calling SpawnWave once per side would hand EACH call the full budget and DOUBLE the
            // field on screen — silently defeating _maxSimultaneousEnemies, a cap that exists
            // because of a measured phone frame-rate cliff (WO-1113). WaveManager also stays the
            // SINGLE spawn authority: side escalation is this wave loop getting harder, never a
            // second system on its own difficulty curve (SiegeSpawnAuthorityRegression enforces it).
            int sidesThisWave = SmartEnemySpawner.SideCountForWave(waveId);
            FlowTrace.Step("Wave",
                $"wave {waveId}: side ladder wants {sidesThisWave} side(s); ONE SpawnWave call with a " +
                $"SHARED budget of {budget} (cap {_maxSimultaneousEnemies}, live {_liveEnemies.Count}, " +
                $"roster {composition.TotalCount}).");

            List<Enemy> squad = _smartSpawner.SpawnWave(
                composition,
                _enemyCatalog,
                _spawnPoints,
                heartT,
                _enemyRoot,
                waveId,
                ref _spawnInstanceCounter,
                budget,
                deferred);

            bool spawnedAny = RegisterSmartSquad(squad);

            // MEASURED, not intended: the live count AFTER registration, against the cap it is
            // supposed to respect. If a side split ever did double the field, this is the line
            // that says so — a value above the cap is impossible under a correct shared budget.
            int liveNow = _liveEnemies.Count;
            if (_maxSimultaneousEnemies > 0 && liveNow > _maxSimultaneousEnemies)
                FlowTrace.Fail("Wave",
                    $"wave {waveId}: CONCURRENCY CAP BREACHED — {liveNow} live enemies against a cap of " +
                    $"{_maxSimultaneousEnemies} after ONE SpawnWave call across {sidesThisWave} side(s). " +
                    "A shared budget cannot produce this; something released outside the budget " +
                    "(a per-side SpawnWave call, or a second spawn authority).");
            else
                FlowTrace.Step("Wave",
                    $"wave {waveId}: post-spawn live={liveNow}/{(_maxSimultaneousEnemies > 0 ? _maxSimultaneousEnemies.ToString() : "uncapped")} " +
                    $"released={squad?.Count ?? 0} across up to {sidesThisWave} side(s) " +
                    "(see the SmartSpawner MEASURED partition line for the per-side split).");

            if (deferred.Count > 0)
            {
                int heldTotal = CountOf(deferred);
                _heldSmartReinforcements = heldTotal;
                _reinforcementDrainUnscaled = Time.unscaledTime;   // WO-1308: a fresh hold is not a stale one
                FlowTrace.Step("Wave",
                    $"wave {waveId}: concurrency cap {_maxSimultaneousEnemies} released {squad.Count} now, " +
                    $"HOLDING {heldTotal} for reinforcement (total roster unchanged at " +
                    $"{squad.Count + heldTotal}). The drain re-calls SpawnWave with the SAME waveId, so " +
                    "held bodies return to the SAME sides.");
                DrainSmartReinforcements(deferred, waveId, composition).Forget();
            }

            return spawnedAny;
        }

        /// <summary>
        /// WO-1113: how many more bodies the live spawn path may release RIGHT NOW without
        /// breaking <see cref="_maxSimultaneousEnemies"/>. 0 = uncapped (the field is off), which
        /// is the same convention SpawnBatch uses, so the number is authored in exactly one place.
        /// </summary>
        private int SmartSpawnBudget()
            => SmartEnemySpawner.BudgetFor(_maxSimultaneousEnemies, _liveEnemies.Count);

        /// <summary>
        /// WO-1113: applies the shared post-spawn treatment (wave scaling, dynamic difficulty,
        /// Died / ReachedHeart hooks, live-roster add) to a squad the SmartEnemySpawner just
        /// released. Extracted so the FIRST release and every REINFORCEMENT release go through
        /// one implementation — a reinforcement that skipped scaling would be a free kill and a
        /// second code path to keep in sync.
        /// </summary>
        private bool RegisterSmartSquad(List<Enemy> squad)
        {
            bool any = false;
            if (squad == null) return false;

            foreach (Enemy e in squad)
            {
                if (e == null) continue;

                // DEF-59 / CITY-01: wave-scaling parity with the flat SpawnOne / compose
                // paths. EnsureScalingCurve never returns null (runtime DEFAULT fallback).
                var smartCurve = EnsureScalingCurve();
                e.ApplyWaveScaling(
                    smartCurve.HpMultiplier(_currentWaveId),
                    smartCurve.SpeedMultiplier(_currentWaveId),
                    smartCurve.DamageMultiplier(_currentWaveId));

                // PATCH 2 — DYNAMIC DIFFICULTY (base*mult on a freshly captured base; the
                // body is pooled, so an in-place multiply would compound across reuses).
                e.SetBaseStats(e.MaxHp, e.ContactDamage);
                e.ApplyDifficulty(DynamicDifficulty.EnemyHpMultiplier,
                                  DynamicDifficulty.EnemyDamageMultiplier);

                e.Died         += HandleEnemyDied;
                e.ReachedHeart += HandleEnemyReachedHeart;
                _liveEnemies.Add(e);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// WO-1113: releases the slots the concurrency cap HELD back, a slot at a time, as live
        /// enemies die — the smart-path twin of SpawnBatch's cap stall. The wave's roster is
        /// therefore unchanged; only the arrival schedule is.
        ///
        /// Bails on the same three conditions SpawnBatch does: the wave is no longer Active, the
        /// town is suspended (the player left for a dungeon/raid — a fire-and-forget UniTask
        /// outlives component disable AND a scene change, so this MUST be checked here and not
        /// only in Update), or the spawner/catalog went away. Every bail is traced: an abandoned
        /// reinforcement means the wave is short bodies, and a wave that can never reach its
        /// clear count is exactly the silent stall §12 exists to prevent.
        /// </summary>
        private async UniTask DrainSmartReinforcements(
            List<WaveCompositionEntry> deferred, int waveId, EnemyWaveComposition source)
        {
            if (deferred == null || deferred.Count == 0) return;

            int released = 0;
            while (deferred.Count > 0)
            {
                // WO-1308: proof-of-life for the ONE thing that can wedge the clear gate. See the
                // _reinforcementDrainUnscaled field block — a held count with a stale stamp is an
                // orphaned drain, and TickActiveWave is allowed to release the wave from it.
                _reinforcementDrainUnscaled = Time.unscaledTime;

                // The wave moved on under this fire-and-forget task (cleared, then the NEXT wave
                // started). Releasing here would push wave N's leftovers into wave N+1 AND
                // clobber N+1's own held count — bail and say so.
                if (_currentWaveId != waveId)
                {
                    FlowTrace.Warn("Wave",
                        $"reinforcement drain wave {waveId}: the live wave is now {_currentWaveId} — " +
                        $"ABANDONING {CountOf(deferred)} held enemy(s) after releasing {released} " +
                        "(they belong to a wave that is over).");
                    return;   // do NOT touch _heldSmartReinforcements: it belongs to the new wave
                }

                if (_phase != WavePhase.Active)
                {
                    FlowTrace.Warn("Wave",
                        $"reinforcement drain wave {waveId}: phase is {_phase}, not Active — ABANDONING " +
                        $"{CountOf(deferred)} held enemy(s) after releasing {released}.");
                    _heldSmartReinforcements = 0;   // never wedge the clear gate on a dead drain
                    return;
                }
                if (TownSuspension.SuspendedFor(this))
                {
                    FlowTrace.Warn("Wave",
                        $"reinforcement drain wave {waveId}: town suspended ({TownSuspension.Reason}) — " +
                        $"ABANDONING {CountOf(deferred)} held enemy(s) after releasing {released}.");
                    _heldSmartReinforcements = 0;
                    return;
                }
                if (_smartSpawner == null || _enemyCatalog == null)
                {
                    FlowTrace.Fail("Wave",
                        $"reinforcement drain wave {waveId}: spawner/catalog gone — {CountOf(deferred)} " +
                        "held enemy(s) can never be released; the wave will be short.");
                    _heldSmartReinforcements = 0;
                    return;
                }

                // The cap can be turned OFF mid-wave (_maxSimultaneousEnemies = 0). BudgetFor
                // answers 0 for "no cap" as well as for "full", so translate the off case to
                // "release everything now" explicitly — otherwise the drain would wait on a
                // budget that can never arrive and the held enemies would never appear.
                int budget = _maxSimultaneousEnemies <= 0 ? int.MaxValue : SmartSpawnBudget();
                if (budget <= 0)
                {
                    // At capacity — wait for a slot (a death, a breach, or the wave ending).
                    // WO-1308: the predicate is polled every frame while this task is alive, so
                    // stamping the heartbeat here makes an arbitrarily long legitimate wait
                    // indistinguishable from an active drain — and a DEAD task the only thing that
                    // can ever go stale.
                    await UniTask.WaitUntil(
                        () =>
                        {
                            _reinforcementDrainUnscaled = Time.unscaledTime;
                            return _maxSimultaneousEnemies <= 0
                                   || SmartSpawnBudget() > 0
                                   || _phase != WavePhase.Active
                                   || TownSuspension.SuspendedFor(this);
                        });
                    continue;
                }

                // Release the next chunk into the free slots. The spawner writes whatever it
                // could not fit back into a fresh sink, which becomes the new held list.
                var batch = new EnemyWaveComposition { WaveId = waveId };
                for (int i = 0; i < deferred.Count; i++) batch.Entries.Add(deferred[i]);

                var stillHeld = new List<WaveCompositionEntry>();
                Transform heartT = _heart != null ? _heart.transform : null;
                List<Enemy> squad = _smartSpawner.SpawnWave(
                    batch,
                    _enemyCatalog,
                    _spawnPoints,
                    heartT,
                    _enemyRoot,
                    waveId,
                    ref _spawnInstanceCounter,
                    budget,
                    stillHeld);

                RegisterSmartSquad(squad);
                released += squad != null ? squad.Count : 0;

                if (squad == null || squad.Count == 0)
                {
                    // Nothing came out despite a free budget — the spawner is refusing (no gate,
                    // unknown ids, pool starved). Looping would spin forever; stop LOUD instead.
                    FlowTrace.Fail("Wave",
                        $"reinforcement drain wave {waveId}: budget was {budget} but the spawner released " +
                        $"ZERO — {CountOf(deferred)} held enemy(s) dropped to avoid an infinite drain " +
                        "(check the SmartSpawner warnings above for the gate/id that refused).");
                    _heldSmartReinforcements = 0;
                    return;
                }

                deferred = stillHeld;
                _heldSmartReinforcements = CountOf(deferred);
                await UniTask.Yield();
            }

            _heldSmartReinforcements = 0;
            FlowTrace.Step("Wave",
                $"reinforcement drain wave {waveId}: COMPLETE — all {released} held enemy(s) released " +
                $"(source roster {source?.TotalCount ?? 0}).");
        }

        /// <summary>Total enemies across a held-slot list (WO-1113 trace helper).</summary>
        private static int CountOf(List<WaveCompositionEntry> entries)
        {
            int n = 0;
            if (entries != null)
                for (int i = 0; i < entries.Count; i++) n += entries[i].Count;
            return n;
        }

        /// <summary>
        /// Spawns the apex flying boss for an apex wave: instantiates the
        /// <see cref="_apexBossPrefab"/> over the Heart and calls
        /// <see cref="DragonBoss.Configure"/> with the Heart anchor + the wave's
        /// HP. The dragon owns its own kinematic flight — no NavMesh, no spawn
        /// point. A missing prefab logs an error; the wave then clears normally
        /// (its enemy batches, if any) so the loop never stalls.
        /// </summary>
        private void SpawnApexBoss(ApexBossDef boss)
        {
            if (boss == null) return;

            if (_apexBossPrefab == null)
            {
                // The live Village.unity predates the builder's _apexBossPrefab wiring
                // (WireApexBossPrefab), so the serialized reference is null and no dragon
                // ever spawns. Corruption-safe fallback (no risky village rebake): load the
                // Boss_Dragon prefab via EnemyAssetLoader (Addressables-first, Resources/Enemies-fallback).
                // Addressables publishes this key as a GameObject. The 2026-09-09 Wave-20
                // trace proved that requesting DragonBoss throws InvalidKeyException even
                // though the prefab exists under the requested address.
                GameObject prefabObject = DeNelle.Core.EnemyAssetLoader.LoadEnemyPrefab("Boss_Dragon");
                _apexBossPrefab = prefabObject != null
                    ? prefabObject.GetComponentInChildren<DragonBoss>(true)
                    : null;
                if (_apexBossPrefab == null)
                {
                    _apexSpawnPending = true;
                    AwaitApexBossPrefab(boss, _currentWaveId).Forget();
                    FlowTrace.Warn("Waves",
                        "SpawnApexBoss: correctly requested GameObject address 'Enemies/Boss_Dragon'; " +
                        "asset is not resident yet, so the apex clear gate is held while it loads.");
                    return;
                }
                FlowTrace.Warn("Waves",
                    "SpawnApexBoss: _apexBossPrefab was null (stale scene) — using the " +
                    "Resources/Enemies/Boss_Dragon fallback so the apex dragon flies.");
            }

            // Spawn the dragon at cruise height above the Heart so it begins its
            // orbit immediately; DragonBoss.Configure re-seeds its anchor + HP.
            Transform heartT = _heart != null ? _heart.transform : null;
            // #66: lower the entry drop from +22 to +10 to match the lowered _orbitHeight (was 22 -> 10)
            // so the smaller (scale 0.3) dragon reads in-frame instead of starting far overhead.
            Vector3 spawnPos = (heartT != null ? heartT.position : transform.position)
                               + new Vector3(0f, 10f, 0f);

            // G(uard the Instantiate): the prefab instantiation can throw on a corrupt/missing
            // asset; an unguarded throw here aborts the whole wave-start coroutine (every later
            // batch is lost). Build under Guard.Try, then NULL-CHECK the result before any deref.
            DragonBoss dragon = null;
            Guard.Try("Waves", "instantiate apex dragon",
                () => dragon = Instantiate(_apexBossPrefab, spawnPos, Quaternion.identity, _enemyRoot));
            if (dragon == null)
            {
                // R(eturn-fallback never silent): no boss body — the wave continues (its batches
                // still clear) but the apex threat is missing. Fail-loud so it self-reports.
                FlowTrace.Fail("Waves",
                    $"SpawnApexBoss: Instantiate returned null for the apex dragon (wave {_currentWaveId}) — " +
                    "no boss this wave (batches still run so the loop never stalls).");
                return;
            }

            string bossId = !string.IsNullOrEmpty(boss.Id)
                ? boss.Id
                : $"wave{_currentWaveId}-apex-boss";
            dragon.Configure(bossId, heartT, boss.Hp);
            dragon.ApplyEncounterDifficulty(
                DragonDamageMultiplierForWave(_currentWaveId),
                DragonAttackIntervalMultiplierForWave(_currentWaveId));

            dragon.Died += HandleApexBossDied;
            _liveApexBoss = dragon;
            OnApexBossSpawned.Invoke(dragon);

            FlowTrace.Step("Waves",
                $"SpawnApexBoss: Apex wave {_currentWaveId} — released flying boss '{bossId}' " +
                $"(maxHp {(boss.Hp > 0f ? boss.Hp.ToString() : "prefab default")}).");
        }

        private async UniTaskVoid AwaitApexBossPrefab(ApexBossDef boss, int waveId)
        {
            float started = Time.unscaledTime;
            while (this != null && _phase == WavePhase.Active && _currentWaveId == waveId &&
                   Time.unscaledTime - started < ApexLoadTimeoutSeconds)
            {
                GameObject prefabObject = DeNelle.Core.EnemyAssetLoader.LoadEnemyPrefab("Boss_Dragon");
                var component = prefabObject != null
                    ? prefabObject.GetComponentInChildren<DragonBoss>(true)
                    : null;
                if (component != null)
                {
                    _apexBossPrefab = component;
                    _apexSpawnPending = false;
                    FlowTrace.Step("Waves",
                        $"SpawnApexBoss: GameObject prefab became resident after " +
                        $"{Time.unscaledTime - started:F1}s; releasing Syndrath for wave {waveId}.");
                    SpawnApexBoss(boss);
                    return;
                }
                await UniTask.Delay(System.TimeSpan.FromMilliseconds(100), ignoreTimeScale: true);
            }

            if (this == null || _currentWaveId != waveId) return;
            _apexSpawnPending = false;
            FlowTrace.Fail("Waves",
                $"SpawnApexBoss: GameObject address 'Enemies/Boss_Dragon' did not yield a DragonBoss " +
                $"within {ApexLoadTimeoutSeconds:F0}s; releasing the clear gate for wave {waveId}.");
        }

        /// <summary>
        /// Spawns one batch — waits the batch delay, then releases
        /// <see cref="WaveBatch.Count"/> enemies at the named spawn point,
        /// <see cref="WaveBatch.Interval"/> seconds apart.
        /// </summary>
        private async UniTask SpawnBatch(WaveBatch batch, float pinnedBossHp = 0f)
        {
            EnemyDef def = _enemyCatalog.Find(batch.Type);
            if (def == null)
            {
                // U + R: an unknown enemy type means this WHOLE batch never spawns — silent under
                // a bare LogError. Fail-loud so the missing batch self-reports in a capture.
                FlowTrace.Fail("Waves",
                    $"SpawnBatch: wave batch references unknown enemy '{batch.Type}' — batch skipped (0 spawned).");
                return;
            }

            WaveSpawnPoint point = FindSpawnPoint(batch.SpawnPoint);
            if (point == null)
            {
                FlowTrace.Fail("Waves",
                    $"SpawnBatch: no WaveSpawnPoint '{batch.SpawnPoint}' in the scene — " +
                    "batch skipped (0 spawned). Place the spawn markers (see docs/port-notes/week4-waves.md).");
                return;
            }

            // DEF-52: telegraph ring on the ground while the batch delay ticks so
            // the player gets a "something's coming" warning at the spawn point.
            // Only shown when there is a meaningful delay to warn about (≥0.5 s).
            PooledVfx telegraph = null;
            if (batch.Delay >= 0.5f)
                telegraph = VfxPool.GetTelegraph(point.transform.position);

            if (batch.Delay > 0f)
                await UniTask.Delay(System.TimeSpan.FromSeconds(batch.Delay));

            VfxPool.ReturnTelegraph(telegraph);
            telegraph = null;

            for (int i = 0; i < Mathf.Max(0, batch.Count); i++)
            {
                // The wave may have been breached / cleared while this batch
                // was still draining — stop releasing if so.
                if (_phase != WavePhase.Active) return;

                // TOWN SUSPENSION, checked HERE and not only in Update(). SpawnBatch is a
                // fire-and-forget UniTask with no CancellationToken: it runs on the PlayerLoop
                // and outlives both component disable AND a scene change (the same property
                // that produced the captured SpawnOne NRE documented below). So a stand-down
                // that lived only in Update() would still let an already-queued batch keep
                // releasing enemies into the town while the player is in a dungeon - the
                // suspension would look correct in the trace and leak in the world.
                if (TownSuspension.SuspendedFor(this))
                {
                    FlowTrace.Warn("Wave",
                        $"town suspended ({TownSuspension.Reason}) mid-batch - ABANDONING the remaining " +
                        $"{Mathf.Max(0, batch.Count) - i} spawn(s) of this batch (fire-and-forget UniTask, " +
                        "cannot be held). The wave phase itself is preserved by the Update stand-down.");
                    return;
                }

                // DEF-48: simultaneous enemy cap — stall the spawn if we're at capacity.
                // _liveEnemies is pruned in TickActiveWave so the count is current.
                if (_maxSimultaneousEnemies > 0 && _liveEnemies.Count >= _maxSimultaneousEnemies)
                {
                    await UniTask.WaitUntil(
                        () => _liveEnemies.Count < _maxSimultaneousEnemies
                              || _phase != WavePhase.Active
                              || TownSuspension.SuspendedFor(this));
                    if (_phase != WavePhase.Active) return;
                    if (TownSuspension.SuspendedFor(this)) return;
                }

                SpawnOne(def, point, pinnedBossHp);

                if (batch.Interval > 0f && i < batch.Count - 1)
                    await UniTask.Delay(System.TimeSpan.FromSeconds(batch.Interval));
            }
        }

        /// <summary>Instantiates + configures one enemy at <paramref name="point"/>.</summary>
        private void SpawnOne(EnemyDef def, WaveSpawnPoint point, float pinnedBossHp = 0f)
        {
            // F8 2026-07-30 (captured NRE): SpawnBatch is a fire-and-forget UniTask, so a
            // queued batch can outlive a scene change — its WaveSpawnPoint is then DESTROYED
            // and point.HeadingToGate throws (WaveSpawnPoint.cs:61 via get_transform). Unity's
            // fake-null == covers destroyed components; skip loudly instead of throwing.
            if (point == null)
            {
                FlowTrace.Warn("Wave",
                    $"SpawnOne: WaveSpawnPoint is gone (scene changed under a queued batch?) — " +
                    $"skipping spawn of '{(def != null ? def.Id : "enemy")}'.");
                return;
            }
            Vector3 heading = point.HeadingToGate.sqrMagnitude > 0.0001f
                ? point.HeadingToGate.normalized : Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(heading);

            // Spread each enemy around the spawn marker (lateral + a little depth) so a
            // batch advances as a loose MOB toward the gate/tree instead of stacking on
            // one point and marching single-file. Perpendicular = lateral to the heading.
            // WO-430: lateral spread tightened 4.5→3 m — the wider spread pushed the
            // pre-sample XZ OUTSIDE the baked NavMesh boundary more often, and a miss there
            // used to strand the enemy at the raw spawn-point Y (floating/underground).
            Vector3 lateral = Vector3.Cross(Vector3.up, heading);
            Vector3 rawPoint = point.transform.position;
            Vector3 pos = rawPoint
                        + lateral * UnityEngine.Random.Range(-SpawnLateralSpread, SpawnLateralSpread)
                        + heading * UnityEngine.Random.Range(-SpawnDepthSpread, SpawnDepthSpread);

            // Snap the spawn position to the nearest NavMesh sample so a
            // slightly-off-mesh spawn point doesn't strand the enemy off-mesh
            // (NavMeshAgent.isOnNavMesh would stay false → enemy never moves).
            // Sample within a generous 8 m radius; on a miss, DON'T silently keep the
            // spread XZ at the raw spawn-point Y (WO-430: that was the sky/underground
            // spawn — the miss was silent). Warn (§12: never silent), then ground-snap
            // via a downward raycast so the enemy at least lands on the visible surface.
            if (UnityEngine.AI.NavMesh.SamplePosition(
                    pos, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
            {
                pos = hit.position;
            }
            else
            {
                FlowTrace.Warn("Waves",
                    $"SpawnOne: NavMesh.SamplePosition MISS (no mesh within 8 m) for def " +
                    $"'{def?.Id ?? "<null>"}' — spawnPoint={rawPoint}, attemptedPos={pos}. " +
                    $"Falling back to ground-snap raycast instead of the raw spawn Y (would float/sink).");

                // Ground/terrain/default layers only (mirrors Enemy.cs SnapBodyToGround) — this
                // naturally excludes the enemy's own collider (on the "Enemy" layer). Cast DOWN
                // from well above the attempted XZ onto the visible surface and spawn at that Y.
                int groundMask = LayerMask.GetMask("Default", "Terrain", "Ground");
                if (groundMask == 0) groundMask = Physics.DefaultRaycastLayers;
                Vector3 rayOrigin = new Vector3(pos.x, pos.y + 50f, pos.z);
                if (Physics.Raycast(rayOrigin, Vector3.down, out var groundHit, 200f,
                        groundMask, QueryTriggerInteraction.Ignore))
                {
                    pos.y = groundHit.point.y;
                }
                else
                {
                    // Last resort: keep the raw spawn-point Y (never worse than pre-WO-430).
                    FlowTrace.Warn("Waves",
                        $"SpawnOne: ground-snap raycast ALSO missed at XZ=({pos.x:F1},{pos.z:F1}) for def " +
                        $"'{def?.Id ?? "<null>"}' — using spawn-point Y {rawPoint.y:F1} as last resort.");
                    pos.y = rawPoint.y;
                }
            }

            // POOLED: route through EnemyPool so a dead enemy's body is reused instead
            // of Instantiate-per-spawn / Destroy-on-death (the per-spawn GameObject churn
            // that was the main GC / stray-accumulation source). The pool keys on the
            // prefab name (prefab path) or the EnemyDef model id (factory path) so a
            // reused body matches the kind asked for; it builds fresh on a drain. The
            // CALLER still Configures + scales + hooks events below, exactly as before.
            //
            // T-011 ("all enemies one type / friendly enemies"): the single serialized
            // _enemyPrefab used to OVERRIDE every def here — keying the pool on the one
            // prefab name and handing the SAME body out for hollow-walker / -warrior /
            // -rogue / necromancer alike, so a mixed wave read as clones. The varied
            // family bodies come from EnemyFactory.Build (model-keyed per ModelForEnemy);
            // route any def that resolves to a real model through the FACTORY path so the
            // flat batch stream is as varied as the smart/family paths already are. The
            // _enemyPrefab is kept ONLY as a genuine fallback for a def with no usable
            // model (it never overrides a valid def again).
            string model = EnemyFactory.ModelForEnemy(def);
            bool useFactory = !string.IsNullOrEmpty(model);
            string poolKey = useFactory ? "model:" + model : "prefab:" + _enemyPrefab.name;
            Enemy enemy = EnemyPool.Get(poolKey, useFactory ? null : _enemyPrefab, def, pos, rot, _enemyRoot);
            if (enemy == null)
            {
                // R(eturn-fallback never silent): the pool couldn't lease/build a body for this
                // def — the wave is now SHORT one enemy and the loop's clear-count can never be
                // met (a silent stall). Fail-loud naming the def + pool key so a capture pinpoints
                // which spawn died instead of a blank "enemy never appeared".
                FlowTrace.Fail("Waves",
                    $"SpawnOne: EnemyPool.Get returned null for def '{def?.Id ?? "<null>"}' (poolKey='{poolKey}', " +
                    $"useFactory={useFactory}, model='{model}') — enemy NOT spawned; wave is short one body.");
                return;
            }

            // V(erify the spawned enemy actually RENDERS + is ON the NavMesh): a leased body with
            // no enabled renderer is an invisible enemy; an off-mesh NavMeshAgent never moves
            // (isOnNavMesh==false) and so never reaches the Heart — the wave then stalls with a
            // live-but-frozen enemy. Both are Warn (skip-not-abort: the enemy still counts toward
            // the wave + can still be configured) so a capture splits "invisible" vs "stranded".
            VerifySpawnedEnemy(enemy, def, pos);

            // The hero/pet target sweeps find enemies via GetComponentInParent<IDamageable>,
            // which resolves to EnemyDamageable. The placeholder capsule (and some prefabs)
            // don't carry it — add it so the hero/pets can actually ACQUIRE + DAMAGE this
            // wave enemy. Without it the enemy marches + attacks but is INVULNERABLE to you.
            // (EnemyPool also guarantees this on a fresh build; kept for prefab parity.)
            if (enemy.GetComponent<EnemyDamageable>() == null)
                enemy.gameObject.AddComponent<EnemyDamageable>();

            string instanceId = $"wave{_currentWaveId}-{def.Id}-{_spawnInstanceCounter++}";
            Transform heartT = _heart != null ? _heart.transform : null;
            enemy.Configure(instanceId, def, heartT);

            // DEF-59 / CITY-01: apply wave-scaling multipliers. EnsureScalingCurve NEVER
            // returns null (falls back to a runtime DEFAULT curve when no asset is wired in
            // the scene), so enemies escalate per wave in EVERY scene / spawn path.
            {
                var flatCurve = EnsureScalingCurve();
                enemy.ApplyWaveScaling(
                    flatCurve.HpMultiplier(_currentWaveId),
                    flatCurve.SpeedMultiplier(_currentWaveId),
                    flatCurve.DamageMultiplier(_currentWaveId));
            }

            // WO-789: wave-level ground-boss HP pin (waves.json bossHp — mirrors apexBoss.hp).
            // Applied AFTER ApplyWaveScaling so the pin REPLACES the scaled value and the boss
            // lands at exactly the authored HP (wave 5 troll = 1050, not 320 x curve).
            if (pinnedBossHp > 0f)
            {
                enemy.OverrideMaxHp(pinnedBossHp);
                FlowTrace.Step("Waves",
                    $"SpawnOne: bossHp pin — '{def.Id}' wave {_currentWaveId} maxHp pinned to " +
                    $"{pinnedBossHp:0} (post-scaling override; WaveScalingCurve bypassed for this boss).");
            }

            enemy.Died += HandleEnemyDied;
            enemy.ReachedHeart += HandleEnemyReachedHeart;
            _liveEnemies.Add(enemy);
        }

        /// <summary>
        /// V(erify) a just-spawned wave enemy can actually be SEEN and can MOVE: it must carry
        /// at least one enabled Renderer (else it's invisible) and, if it drives a NavMeshAgent,
        /// that agent must be on the NavMesh (else it's stranded and never reaches the Heart —
        /// the wave silently stalls). Anomalies are Warn'd (skip-not-abort) so a capture pinpoints
        /// the broken spawn; the enemy is left live (it still counts toward the wave).
        /// </summary>
        private void VerifySpawnedEnemy(Enemy enemy, EnemyDef def, Vector3 pos)
        {
            if (enemy == null) return;

            int total = 0, enabledR = 0;
            var renderers = enemy.GetComponentsInChildren<Renderer>(true);
            if (renderers != null)
            {
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    total++;
                    if (r.enabled) enabledR++;
                }
            }
            if (enabledR == 0)
                FlowTrace.Warn("Waves",
                    $"VerifySpawnedEnemy: enemy '{def?.Id ?? "<null>"}' on '{enemy.gameObject.name}' has " +
                    $"NO enabled Renderer ({total} found) — it will spawn INVISIBLE.");

            var agent = enemy.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled && !agent.isOnNavMesh)
                FlowTrace.Warn("Waves",
                    $"VerifySpawnedEnemy: enemy '{def?.Id ?? "<null>"}' on '{enemy.gameObject.name}' is OFF the NavMesh " +
                    $"at {pos} (agent.isOnNavMesh==false) — it will NOT move toward the Heart; wave may stall.");
        }

        /// <summary>
        /// Builds a primitive-capsule stand-in when no KayKit enemy prefab is
        /// wired yet, so the wave loop is testable before the skeleton prefab
        /// exists. The integrator should assign a real prefab built from
        /// Assets/Models/KayKit/enemies/Skeleton_Minion.glb (see week4-waves.md).
        /// </summary>
        private Enemy BuildPlaceholderEnemy(Vector3 pos, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Enemy (placeholder)";
            go.transform.SetParent(_enemyRoot, false);
            go.transform.SetPositionAndRotation(pos, rot);
            // The capsule's own collider would block its own contact probe — make
            // it a trigger so SphereCast ignores it (QueryTriggerInteraction.Ignore).
            if (go.TryGetComponent(out Collider col)) col.isTrigger = true;
            go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            return go.AddComponent<Enemy>();
        }

        /// <summary>
        /// Runs every frame while a wave is Active: arms the breach window, then
        /// watches each live enemy for an inner-ring crossing and detects clear.
        /// </summary>
        private void TickActiveWave()
        {
            // ENCOUNTER TELEMETRY: sample the hero's HP delta for this frame (damage taken).
            // Only runs while a wave is ACTUALLY fighting, which is exactly the window the
            // difficulty sample is supposed to cover.
            TickEncounterTelemetry();

            // Arm breach detection a beat after the wave starts.
            if (!_breachArmed)
            {
                _breachArmTimer += Time.deltaTime;
                if (_breachArmTimer >= _breachArmDelay) _breachArmed = true;
            }

            // Prune destroyed enemies (Die() destroys the GameObject -> null ref).
            _liveEnemies.RemoveAll(e => e == null);

            // FAILSAFE: cull a stuck enemy (off-mesh / boxed-in / unpathable) that makes
            // no progress toward the Heart for StuckTimeout, so a single Hollow One can't
            // hang the wave forever and block the next wave's countdown.
            if (_heart != null)
            {
                Vector3 hpos = _heart.transform.position;
                for (int i = _liveEnemies.Count - 1; i >= 0; i--)
                {
                    Enemy e = _liveEnemies[i];
                    if (e == null || e.IsDead) continue;
                    float sqr = Vector3.ProjectOnPlane(e.transform.position - hpos, Vector3.up).sqrMagnitude;
                    if (!_enemyBestSqr.TryGetValue(e, out float best) || sqr < best - 0.25f)
                    {
                        _enemyBestSqr[e]   = sqr;   // got closer → reset the stuck timer
                        _enemyStuckTime[e] = 0f;
                    }
                    else
                    {
                        float t = (_enemyStuckTime.TryGetValue(e, out float tv) ? tv : 0f) + Time.deltaTime;
                        _enemyStuckTime[e] = t;
                        if (t >= StuckTimeout)
                        {
                            Debug.LogWarning($"[WaveManager] Culling stuck enemy '{e.name}' (no progress to the Heart for {StuckTimeout:F0}s) so wave {_currentWaveId} can advance.");
                            _enemyBestSqr.Remove(e);
                            _enemyStuckTime.Remove(e);
                            e.Kill();   // fires Died -> HandleEnemyDied removes it from _liveEnemies
                        }
                    }
                }
                _liveEnemies.RemoveAll(e => e == null);
            }

            // WO-1026 — SIEGE OBSERVER. One null-safe line: when a SiegeSession is open (the
            // scheduler opened it just before ForceBeginNextWave), it unions the fielded roster
            // and records inner-ring crossings for the persisted Defence Report. It is an
            // OBSERVER of this loop, not a second detector: it changes no phase, cancels nothing,
            // and spawns nothing. It is passed THIS manager's own heart/radius/armed flag so the
            // breach geometry has exactly one source of truth.
            //   Why not hook the detector below instead? Because that whole block is behind
            //   FeatureFlags.WaveBreachToAtb, which WO-579 turned OFF by default — on the live
            //   path it never runs, so hooking it would have recorded nothing, forever, silently.
            //   (Full reasoning in SiegeSession.cs's header. Do not "simplify" this back.)
            if (SiegeSession.Current != null && _heart != null)
                SiegeSession.Current.ObserveTick(_heart.transform.position, _innerRingRadius,
                                                 _breachArmed, _liveEnemies);

            // WO-579 (#4 — owner felt-test 2026-06-28): the village wave resolves IN-HUB (towers + hero
            // auto-defend; enemies contact-attack the Heart : IDamageableStructure; Heart at 0 = defeat).
            // The legacy breach-ring → ATBBattle hand-off (the "wave ~3 auto-launches ATB" symptom — an
            // escalating brute reaches the tree ring) is gated OFF by default. With it off, no scene swap
            // ever happens, so placed towers + the wave counter never reset across the (removed) detour.
            if (FeatureFlags.WaveBreachToAtb && _breachArmed && _heart != null)
            {
                Vector3 heartPos = _heart.transform.position;
                float ringSqr = _innerRingRadius * _innerRingRadius;
                for (int i = 0; i < _liveEnemies.Count; i++)
                {
                    Enemy e = _liveEnemies[i];
                    if (e == null || e.IsDead || _breachRoster.Contains(e)) continue;

                    float planarSqr = Vector3.ProjectOnPlane(
                        e.transform.position - heartPos, Vector3.up).sqrMagnitude;
                    if (planarSqr <= ringSqr)
                        _breachRoster.Add(e);
                }

                if (_breachRoster.Count > 0)
                {
                    TriggerBreach();
                    return;
                }
            }

            // Wave is cleared when no enemies remain on the field. An apex wave
            // additionally holds open until the flying boss is down — the dragon
            // is not in _liveEnemies (it owns kinematic flight, not a NavMesh
            // agent), so its life is tracked separately via _liveApexBoss.
            bool apexBossStillUp = _apexSpawnPending || (_liveApexBoss != null && !_liveApexBoss.IsDead);

            // WO-1113: a wave whose bodies are being METERED by the concurrency cap can hit
            // zero-on-field while reinforcements are still queued (kill the last 8 with one AoE
            // in a single frame and the field is empty before the drain's next tick). Clearing
            // there would hand the player a wave-clear for a roster they never fought and skip
            // the rest of the enemies entirely — the cap would silently become a wave THINNER,
            // which is exactly what it must not be. Held bodies keep the wave open.
            if (_heldSmartReinforcements > 0)
            {
                // WO-1308 — THE WEDGE, SELF-HEALED. The held count is owned by a fire-and-forget
                // UniTask; an exception inside it cannot zero the counter because nobody awaits it,
                // and from that frame on this early return runs forever. The wave never completes,
                // the phase stays Active, and the OnEnable battle-lock probe holds combat state for
                // the rest of the session — the owner's "the wolf is still here and sitting in
                // fight" (F8 seq 4663-4665). The heartbeat is what makes this decidable: a drain
                // that is merely WAITING stamps it every frame from inside its own cap-wait
                // predicate, so only a task that is GONE can go stale.
                float since = _reinforcementDrainUnscaled < 0f
                    ? float.PositiveInfinity
                    : Time.unscaledTime - _reinforcementDrainUnscaled;

                if (since <= ReinforcementDrainStaleSeconds)
                {
                    FlowTrace.Throttle("Wave", "held-reinforcements", 2f,
                        $"wave {_currentWaveId}: field has {_liveEnemies.Count} live, " +
                        $"{_heldSmartReinforcements} still HELD by the concurrency cap — wave stays open " +
                        $"(drain alive, last heartbeat {since:F1}s ago).");
                    return;
                }

                FlowTrace.Fail("Wave",
                    $"wave {_currentWaveId}: {_heldSmartReinforcements} reinforcement(s) are held but the " +
                    $"drain has not stamped its heartbeat for {since:F1}s (limit " +
                    $"{ReinforcementDrainStaleSeconds:F0}s) — the fire-and-forget DrainSmartReinforcements " +
                    "UniTask is GONE (an exception inside it cannot zero the counter, because nobody " +
                    "awaits it). RELEASING the clear gate so the wave can finish: this wave is SHORT " +
                    "those bodies, which is a visible content bug and is exactly what this line is for. " +
                    "Do not silence it — find why the drain threw.");
                _heldSmartReinforcements = 0;
            }

            if (_liveEnemies.Count == 0 && !apexBossStillUp)
            {
                CompleteWave();
            }
        }

        // =====================================================================
        //  Encounter telemetry — the four measurements DynamicDifficulty consumes
        // =====================================================================

        /// <summary>
        /// Resets all four encounter measurements and stamps COMBAT start. Called by
        /// <see cref="StartWave"/> the instant the phase turns Active — deliberately NOT at
        /// EnterCountdown (see the field block: the build window is not part of the fight).
        /// </summary>
        private void BeginEncounterTelemetry(int waveId)
        {
            EnsureDifficultySessionReset();

            _encounterStartTime       = Time.time;
            _encounterHeroDied        = false;
            _encounterDamageTaken     = 0f;
            _encounterDamageDealtBase = Enemy.HeroDamageDealtTotal;

            BindTelemetryHero(HeroHealth.Instance, resetHpBaseline: true);

            FlowTrace.Step("Difficulty",
                $"encounter ARMED for wave {waveId} at t={_encounterStartTime:F2}s " +
                $"(hero bound={(_telemetryHero != null)}) — {DynamicDifficulty.Describe()}");
        }

        /// <summary>
        /// Binds the death + damage-taken measurements to a HeroHealth instance. The hero can be
        /// rebuilt (DDOL swap / scene reload) mid-run, so the bind is re-checked every active
        /// frame rather than assumed for the life of the wave.
        /// </summary>
        private void BindTelemetryHero(HeroHealth hero, bool resetHpBaseline)
        {
            if (_telemetryHero == hero)
            {
                if (resetHpBaseline) _telemetryLastHeroHp = hero != null ? hero.Hp : -1f;
                return;
            }

            if (_telemetryHero != null) _telemetryHero.OnDied -= HandleTelemetryHeroDied;
            _telemetryHero = hero;
            if (_telemetryHero != null)
            {
                // HeroHealth's EXISTING death signal (public event Action OnDied, raised once in
                // TakeDamage the frame HP reaches zero, alongside OnDeath). No new plumbing.
                _telemetryHero.OnDied += HandleTelemetryHeroDied;
                _telemetryLastHeroHp = _telemetryHero.Hp;
            }
            else _telemetryLastHeroHp = -1f;
        }

        /// <summary>Latches "the player died during this encounter". Only a death while the wave is
        /// ACTUALLY fighting counts — a death in the calm build window is not this encounter's.</summary>
        private void HandleTelemetryHeroDied()
        {
            if (_phase != WavePhase.Active || _encounterStartTime < 0f) return;
            if (_encounterHeroDied) return;
            _encounterHeroDied = true;
            FlowTrace.Step("Difficulty", $"encounter telemetry: hero DIED during wave {_currentWaveId}.");
        }

        /// <summary>
        /// Per-frame half of the telemetry: accumulates damage TAKEN as the negative deltas of the
        /// hero's HP. Sampled off HeroHealth's existing public Hp rather than a new event, so it
        /// captures the POST-mitigation number the player actually felt (armor, shields, talent DR,
        /// parry and blocks have all already been applied by the time Hp changes) and cannot be
        /// desynced by a missed subscription. Upward moves (respawn, heal, gear top-up) are ignored.
        /// </summary>
        private void TickEncounterTelemetry()
        {
            HeroHealth hero = HeroHealth.Instance;
            if (hero != _telemetryHero)
            {
                // Instance swapped (or appeared for the first time) — rebind and restart the
                // baseline. Never charge the delta between two DIFFERENT bodies as damage.
                BindTelemetryHero(hero, resetHpBaseline: true);
                return;
            }
            if (hero == null) return;

            float hp = hero.Hp;
            if (_telemetryLastHeroHp >= 0f && hp < _telemetryLastHeroHp)
                _encounterDamageTaken += _telemetryLastHeroHp - hp;
            _telemetryLastHeroHp = hp;
        }

        /// <summary>
        /// PATCH 5 — hands the finished encounter to DynamicDifficulty. Called from
        /// <see cref="CompleteWave"/> right after OnWaveCleared so a listener that mutates state
        /// cannot change what was measured.
        /// </summary>
        private void RecordEncounterSample(int clearedWaveId)
        {
            if (_encounterStartTime < 0f)
            {
                FlowTrace.Warn("Difficulty",
                    $"wave {clearedWaveId} cleared with NO armed encounter (telemetry never began) — not recorded.");
                return;
            }

            float duration = Time.time - _encounterStartTime;
            _encounterStartTime = -1f;   // consume — a wave is recorded exactly once

            // A wave that "clears" in under a second never happened: it is the no-spawn-points
            // self-clear StartWave already warns about. Recording it would read as a flawless
            // instant victory (fast clear, zero damage taken) and shove difficulty upward off a
            // scene misconfiguration.
            if (duration < 1f)
            {
                FlowTrace.Warn("Difficulty",
                    $"wave {clearedWaveId} cleared in {duration:F2}s — degenerate self-clear " +
                    "(no enemies ever fought); NOT recorded, so a missing spawn point cannot scale difficulty.");
                return;
            }

            WaveDef def = ResolveWaveDefForTelemetry(clearedWaveId);
            bool wasBoss = def != null && (!string.IsNullOrEmpty(def.Boss) || def.IsApexBossWave);

            float expected = def != null ? def.ExpectedCombatSeconds : 0f;
            bool expectedAuthored = expected > 0f && !float.IsNaN(expected) && !float.IsInfinity(expected);
            if (!expectedAuthored)
            {
                // "NO SAMPLE" for the clear-time signal, expressed the only way the immutable
                // EncounterSample contract allows: hand it the expected duration that makes
                // ClearRatio land EXACTLY on the profile's authored pivot (TargetClearRatio), so
                // DifficultyMath scores this encounter's time component at precisely 0 — neutral,
                // contributing nothing. Passing 0 instead would NOT be neutral: ClearRatio falls
                // back to 1.0, and 1.0 against a 0.65 par reads as a STRUGGLING clear. The death
                // and damage signals of the encounter still count in full.
                float pivot = DynamicDifficulty.State.Profile.TargetClearRatio;
                expected = pivot > 0f ? duration / pivot : 0f;
                FlowTrace.Warn("Difficulty",
                    $"wave {clearedWaveId} has no authored expectedCombatSeconds in waves.json — " +
                    $"clear-time signal NEUTRALISED (pivot {pivot:0.###}); death + damage still count. " +
                    "Author expectedCombatSeconds for this wave to make its clear time real.");
            }

            float dealt = (float)(Enemy.HeroDamageDealtTotal - _encounterDamageDealtBase);
            if (dealt < 0f) dealt = 0f;

            var sample = new EncounterSample(
                durationSeconds:         duration,
                expectedDurationSeconds: expected,
                playerDied:              _encounterHeroDied,
                damageTaken:             _encounterDamageTaken,
                damageDealt:             dealt,
                wasBoss:                 wasBoss);

            FlowTrace.Step("Difficulty",
                $"wave {clearedWaveId} encounter: dur={duration:F1}s exp={expected:F1}s" +
                (expectedAuthored ? "" : " (neutralised)") +
                $" died={_encounterHeroDied} taken={_encounterDamageTaken:F0} dealt={dealt:F0} boss={wasBoss}");

            DynamicDifficulty.RecordEncounter(sample);
        }

        /// <summary>
        /// The authored WaveDef for a cleared wave ordinal — the schedule entry, or the cycled
        /// endless source def past the authored schedule (so an endless boss capstone is still
        /// sampled as a boss and still carries its authored combat budget).
        /// </summary>
        private WaveDef ResolveWaveDefForTelemetry(int waveId)
        {
            if (_schedule == null) return null;
            WaveDef w = _schedule.Find(waveId);
            if (w != null) return w;
            return ResolveEndlessWaveDef(waveId, out _);
        }

        /// <summary>
        /// Clears the encounter history ONCE per play session, and brings the telemetry host
        /// online. This is half of "one player's history never scales another's run"; the other
        /// half is <see cref="HookNewGameReset"/>, which catches a New Game made mid-session.
        /// Deferred out of the RuntimeInitializeOnLoadMethod because the first touch of
        /// DynamicDifficulty.State loads the profile JSON.
        /// </summary>
        private static void EnsureDifficultySessionReset()
        {
            if (s_difficultySessionReset) return;
            s_difficultySessionReset = true;
            DynamicDifficulty.ResetForNewGame();
            DynamicDifficultyHost.Bootstrap();
        }

        /// <summary>
        /// PATCH 5 (new-game half). GameStateService raises <c>StateReplaced</c> after a full
        /// <c>ResetToNewGame()</c> or <c>Load()</c> — that IS where a new game is signalled, and it
        /// is a read-only seam (GameStateService itself is untouched). Clearing on a Load too is
        /// deliberate and harmless: the difficulty history is in-memory only and is never persisted,
        /// so a load always begins a fresh run's worth of history.
        /// </summary>
        private void HookNewGameReset()
        {
            if (_newGameHookArmed) return;
            var svc = GameStateService.Instance;
            if (svc == null) return;   // Core not bootstrapped yet — retried on the next BeginLoop
            svc.StateReplaced.AddListener(HandleStateReplacedForDifficulty);
            _newGameHookArmed = true;
        }

        private void HandleStateReplacedForDifficulty()
        {
            DynamicDifficulty.ResetForNewGame();
            s_difficultySessionReset = true;   // the session reset is now satisfied
            FlowTrace.Step("Difficulty",
                "GameState replaced (new game / load) — encounter history cleared so the previous " +
                "run's performance cannot scale this one.");
        }

        /// <summary>Drops the telemetry subscriptions (mirrors OnDisable's other unsubscribes).</summary>
        private void UnbindEncounterTelemetry()
        {
            if (_telemetryHero != null) _telemetryHero.OnDied -= HandleTelemetryHeroDied;
            _telemetryHero = null;
            _telemetryLastHeroHp = -1f;

            if (_newGameHookArmed)
            {
                var svc = GameStateService.Instance;
                svc?.StateReplaced.RemoveListener(HandleStateReplacedForDifficulty);
                _newGameHookArmed = false;
            }
        }

        // =====================================================================
        //  Wave clear
        // =====================================================================

        /// <summary>Wave cleared without a breach — award crystals then advance.</summary>
        private void CompleteWave()
        {
            int cleared = _currentWaveId;
            if (_heart != null) _heart.SetState(HeartState.Serene);

            // WO-579 (#5): advance + PERSIST the run progress so a hub reload / scene return resumes at
            // the next wave instead of restarting at 1. The static carries it within the play session;
            // RecordRun writes BestWave to the save (the cross-session resume seed) and saves.
            s_resumeWaveId = cleared + 1;
            GameStateService.Instance?.RecordRun(cleared);

            // ENDLESS: an endless clear persists exactly like an authored clear (BestWave has
            // no upper clamp — SaveSchema only floors it at 0), and the resume seed lands the
            // returning player back in the endless awaiting-player state, never Complete.
            if (_schedule != null && cleared > _schedule.MaxWaveId)
                FlowTrace.Step("Wave", $"endless wave {cleared}: CLEARED — resume seed -> {cleared + 1}");

            AwardWaveResources(cleared);   // WO-330: build resources (Wood/Iron) — the core economy income
            AwardWaveCrystals(cleared);
            // Persist progression grants before OnWaveCleared presentation listeners run.
            RewardedProgression.AwardWaveClearUnlocks(cleared);

            OnWaveCleared.Invoke(cleared);

            // PATCH 5 — the RECORDING SITE. Everything above this line is what the encounter
            // WAS; this is where it becomes the input DynamicDifficulty adapts on. Seated after
            // OnWaveCleared so a listener cannot alter what was measured.
            RecordEncounterSample(cleared);

            // WO2: analytics.
            DeNelle.Core.Analytics.EventTracker.Track("wave_completed", new
            {
                waveId     = cleared,
                liveEnemiesKilled = 0, // filled by future combat telemetry pass
            });

            EnterCountdown(cleared + 1);
        }

        /// <summary>
        /// WO-330 — the city-defense fundamental loop's payout: clearing a wave grants
        /// BUILD RESOURCES (Wood/Iron, optional Food) into the player's wallet via
        /// <see cref="EconomyService.Grant(ResourceCost)"/> — the same pool the BuildMenu /
        /// upgrade paths spend from. This is the primary economy income: defend the city →
        /// defeat the wave → earn the resources you build/upgrade defenses with → harder
        /// waves → repeat.
        ///
        /// WO-361 — the payout is STAGGERED by interval rather than every wave:
        ///   • Food  every <see cref="_foodRewardInterval"/>th wave (default 2nd), 30–50
        ///   • Wood  every <see cref="_woodRewardInterval"/>th wave (default 3rd), 20–30
        ///   • Iron  every <see cref="_ironRewardInterval"/>th wave (default 4th), 15–25
        /// Each amount is randomized in [Base .. Base+Spread] and SCALES with the wave
        /// number — +<see cref="_rewardScalePerStep"/> (default +20%) every
        /// <see cref="_rewardScaleWaveStep"/> waves (default 5), optionally clamped by
        /// <see cref="_rewardScalingStepCap"/>. All amounts/intervals are inspector-tunable
        /// so balance changes need no code edit. Crystals stay on the separate
        /// <see cref="AwardWaveCrystals"/> path.
        ///
        /// No-ops cleanly when <see cref="_awardResourcesOnWaveClear"/> is off, when nothing
        /// is due this wave, or when no EconomyService exists yet (early boot).
        /// </summary>
        private void AwardWaveResources(int waveId)
        {
            // ARM THE RECORD FIRST, before any early return. Stamping the wave id with zero
            // amounts is what makes staleness structurally impossible: from this line on,
            // TryGetPayoutFor(waveId) answers for THIS wave and can never hand a presentation
            // layer the previous wave's spoils, no matter which branch below returns.
            s_lastPayout = new WaveClearPayout(waveId, 0, 0, 0, 0);

            if (!_awardResourcesOnWaveClear) return;

            var economy = EconomyService.Instance;
            if (economy == null) return;

            // Scaling multiplier: +scalePerStep every scaleWaveStep waves, capped.
            int step = waveId / Mathf.Max(1, _rewardScaleWaveStep);
            if (_rewardScalingStepCap > 0) step = Mathf.Min(step, _rewardScalingStepCap);
            float scale = 1f + _rewardScalePerStep * step;

            // WO-676 STEWARD capstone (Bountiful Banners): ONE HeroTalentModifiers read at
            // this existing reward grant — `waveReward` folds into the same scale multiplier
            // the wood/iron/food rolls already apply, so every wave-clear payout grows
            // together. StatSum is internally null-safe (0 with no service/tree/nodes), so
            // rewards are byte-identical to baseline at sum 0.
            float rewardBonus = DeNelle.Village.Talents.HeroTalentModifiers.StatSum(
                HeroTalentClassReader.Slug(), "waveReward");
            if (rewardBonus > 0f)
            {
                scale *= 1f + rewardBonus;
                FlowTrace.Once("Talent", "waveReward",
                    $"waveReward x{1f + rewardBonus:0.###} applied to wave-clear rewards (WO-676 Bountiful Banners).");
            }

            // WO-330 wiring contract: the per-wave ramp fields raise the effective base
            // floor by (perWave * waveId) before the random spread + scaling are applied,
            // so they stay LIVE alongside the WO-361 staggered/scaled payout.
            int woodBase = _woodRewardBase + _woodRewardPerWave * waveId;
            int ironBase = _ironRewardBase + _ironRewardPerWave * waveId;

            int wood = DueThisWave(waveId, _woodRewardInterval)
                ? ScaledRoll(woodBase, _woodRewardSpread, scale) : 0;
            int iron = DueThisWave(waveId, _ironRewardInterval)
                ? ScaledRoll(ironBase, _ironRewardSpread, scale) : 0;
            int food = DueThisWave(waveId, _foodRewardInterval)
                ? ScaledRoll(_foodRewardBase, _foodRewardSpread, scale) : 0;

            // FTUE recovery floor: the legacy stagger paid no iron before wave four,
            // then only 15-25 against repair bills in the hundreds. The first three
            // clears now guarantee 480 iron in total. Max preserves stronger authored
            // or talent payouts; wave four onward keeps the established economy curve.
            EarlyWaveReward floor = EarlyWaveRewardFloor(waveId);
            wood = Mathf.Max(wood, floor.Wood);
            iron = Mathf.Max(iron, floor.Iron);
            food = Mathf.Max(food, floor.Stone);

            if (wood <= 0 && iron <= 0 && food <= 0) return;

            economy.Grant(new ResourceCost(wood: wood, food: food, iron: iron));

            // PUBLISH WHAT WAS BANKED — the same three integers Grant just took, captured on
            // the line after the grant so the record and the wallet can never disagree. The
            // wave-clear banner (EndStateVM.FromWaveClear) reads THIS; it never re-rolls.
            s_lastPayout = new WaveClearPayout(waveId, wood, iron, food, 0);
            FlowTrace.Step("Wave",
                $"wave {waveId} payout RECORDED for the clear banner: wood={wood} iron={iron} food={food} " +
                $"(x{scale:0.0##} scale) - presentation reads these, it never re-derives them.");

            // Brief on-victory loot toast (reuses the existing combat feedback if present).
            ShowRewardToast(wood, iron, food);

            Debug.Log(
                $"[WaveManager] Wave {waveId} cleared — granted build resources (×{scale:0.0} scale): " +
                string.Join(", ", BuildRewardParts(wood, iron, food)) +
                " (defend → earn → build).");
        }

        public readonly struct EarlyWaveReward
        {
            public readonly int Wood;
            public readonly int Iron;
            public readonly int Stone;

            public EarlyWaveReward(int wood, int iron, int stone)
            { Wood = wood; Iron = iron; Stone = stone; }
        }

        /// <summary>Pure balance authority for the guaranteed onboarding runway.</summary>
        public static EarlyWaveReward EarlyWaveRewardFloor(int waveId)
        {
            switch (waveId)
            {
                case 1: return new EarlyWaveReward(180, 120, 80);
                case 2: return new EarlyWaveReward(240, 160, 120);
                case 3: return new EarlyWaveReward(320, 200, 160);
                default: return new EarlyWaveReward(0, 0, 0);
            }
        }

        /// <summary>True when a payout with the given interval is due on this wave.
        /// Interval 0/1 = every wave; otherwise every Nth wave (waveId % N == 0).</summary>
        private static bool DueThisWave(int waveId, int interval)
        {
            if (interval <= 1) return true;
            return waveId % interval == 0;
        }

        /// <summary>Randomized, wave-scaled amount: Round(Random[base..base+spread] * scale).</summary>
        private static int ScaledRoll(int baseAmount, int spread, float scale)
        {
            if (baseAmount <= 0 && spread <= 0) return 0;
            int rolled = baseAmount + (spread > 0 ? UnityEngine.Random.Range(0, spread + 1) : 0);
            return Mathf.Max(0, Mathf.RoundToInt(rolled * scale));
        }

        private static System.Collections.Generic.List<string> BuildRewardParts(int wood, int iron, int food)
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (wood > 0) parts.Add($"{wood} Wood");
            if (iron > 0) parts.Add($"{iron} Iron");
            if (food > 0) parts.Add($"{food} Stone");
            if (parts.Count == 0) parts.Add("nothing");
            return parts;
        }

        /// <summary>
        /// Best-effort floating loot/notification on wave victory. Resolves the HUD's
        /// banner setter by cached reflection (HUD → Core only; Village cannot reference
        /// DeNelle.HUD), exactly like the town-HUD bridges. Fully guarded — a missing HUD
        /// or method simply skips the toast (never throws; WebGL halts on an uncaught throw).
        /// </summary>
        private void ShowRewardToast(int wood, int iron, int food)
        {
            try
            {
                var hud = DeNelle.Core.CoreServices.Hud as object;
                if (hud == null) return;
                var parts = BuildRewardParts(wood, iron, food);
                string msg = "Wave cleared! +" + string.Join("  +", parts);

                // Prefer a dedicated banner/toast setter if the HUD exposes one.
                var t = hud.GetType();
                var m = t.GetMethod("ShowBanner",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                            null, new[] { typeof(string) }, null)
                     ?? t.GetMethod("ShowToast",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                            null, new[] { typeof(string) }, null);
                if (m == null)
                {
                    // §12, NO SILENT FAILURE. This is the line that should have existed since
                    // WO-330: the live HUD (VillageHudController) declares NEITHER
                    // ShowBanner(string) NOR ShowToast(string) — only
                    // ShowWaveClearBanner(int,int,string) — so this reflection has ALWAYS
                    // resolved null and `m?.Invoke` swallowed the whole reward announcement.
                    // The reward moment now lives on the wave-clear end-state banner (fed by
                    // s_lastPayout above); this toast stays a best-effort SECOND surface, but
                    // it no longer disappears without saying so.
                    FlowTrace.Once("Wave", "reward-toast-no-seam",
                        "reward toast has NO seam on the live HUD (" + t.Name + " exposes neither " +
                        "ShowBanner(string) nor ShowToast(string)) - the wave-clear banner is the " +
                        "surface that tells the player what they earned. Message was: " + msg);
                    return;
                }
                m.Invoke(hud, new object[] { msg });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WaveManager] reward toast skipped: " + e.Message);
            }
        }

        /// <summary>
        /// Evaluates boss-wave drops and active-event bonuses from <see cref="ServerConfig"/>
        /// and credits the won crystals to the BUILD-SPEND pool
        /// (<see cref="GameState.Resources"/>.Crystals) via
        /// <see cref="GameStateService.AddCrystals"/> — the exact balance the
        /// BuildMenu spends from and the village HUD top-bar displays (WO-131
        /// follow-up, owner decision (a): "win waves → build towers" on one
        /// currency the player sees). THERE IS ONLY ONE CRYSTAL WALLET (canon fix,
        /// WO-856 section 12): this comment used to describe a "separate AetherCrystals
        /// empower pool" - that pool was folded into GameState.Resources.Crystals in
        /// save v18 (SaveMigrator.MigrateToV18), and CrystalEconomy has been a thin
        /// facade over the same field ever since (CrystalEconomy.cs:14-19). Every
        /// crystal source (CrystalMine, MineNode, tower-empower, TalentTree,
        /// BattlePass, Referral, Promo) and this boss-drop faucet all credit the SAME
        /// balance. This method stays a SEPARATE FAUCET (boss drops + event bonuses),
        /// not a separate wallet - do not merge it with the mine's per-wave payout.
        /// Drop chance and ranges are all backend-controlled — no rebuild needed to tune.
        /// </summary>
        private void AwardWaveCrystals(int waveId)
        {
            var cfg = GameStateService.Instance != null
                ? GameStateService.Instance.ServerConfig
                : ServerConfig.Default;

            int totalAward = 0;

            // ── Boss-wave drop (every Nth wave, chance-based) ─────────────────
            // Guard the interval: a backend ServerConfig can deliver BossWaveInterval = 0
            // (non-null, so the ?? fallback to 5 does NOT apply), which would make
            // `waveId % 0` throw DivideByZeroException and abort the wave-clear reward
            // path right after OnWaveCleared fired. Clamp to ≥1 so the modulo is safe.
            int bossInterval = Mathf.Max(1, cfg.BossInterval);
            if (waveId % bossInterval == 0)
            {
                if (UnityEngine.Random.value <= cfg.DropChance)
                {
                    int drop = UnityEngine.Random.Range(cfg.DropMin, cfg.DropMax + 1);
                    totalAward += drop;
                    Debug.Log($"[WaveManager] Boss-wave crystal drop — wave {waveId} awarded {drop} Crystal(s) to the build-spend pool. " +
                              $"(chance={cfg.DropChance:P0}, range={cfg.DropMin}–{cfg.DropMax})");
                }
                else
                {
                    Debug.Log($"[WaveManager] Boss-wave crystal drop — wave {waveId} missed. " +
                              $"(chance={cfg.DropChance:P0})");
                }
            }

            // ── Special event bonus (every wave while event is active) ────────
            if (cfg.IsEventActive() && cfg.EventBonus > 0)
            {
                totalAward += cfg.EventBonus;
                Debug.Log($"[WaveManager] Event bonus '{cfg.ActiveEventName}' — +{cfg.EventBonus} crystal(s) on wave {waveId}.");
            }

            if (totalAward > 0)
            {
                GameStateService.Instance?.AddCrystals(totalAward);
                // Fold the crystal faucet into THIS wave's payout record (guarded on the id so
                // a future caller outside CompleteWave cannot graft crystals onto another
                // wave's line). AwardWaveResources always armed the id first, so the match
                // holds on the normal path.
                if (s_lastPayout.WaveId == waveId)
                    s_lastPayout = s_lastPayout.WithCrystals(totalAward);
            }
        }

        // =====================================================================
        //  Defeat — the Heart fell (village lose condition, WO-125 Bug 3)
        // =====================================================================

        /// <summary>
        /// WO-125 Bug 3 — the Heart of Elarion fell (HP 0). This is the village's
        /// real LOSE condition: the dragon (or any source) drained the Heart, and
        /// previously nothing reacted. Halt the wave loop into the terminal
        /// <see cref="WavePhase.Defeated"/> state (no further countdown/spawn ticks),
        /// stop the live boss so it can't keep striking a dead Heart, and raise
        /// <see cref="OnDefeat"/> so a bound defeat screen can present "Elarion has
        /// fallen." Fires at most once (HeartController guards the source event).
        /// </summary>
        private void HandleHeartDestroyed()
        {
            if (_phase == WavePhase.Defeated) return;   // already lost — idempotent
            SetPhase(WavePhase.Defeated, "HandleHeartDestroyed");

            // Stop the apex boss mid-encounter — a dead Heart should not keep taking
            // swoop/breath hits, and the boss's death-fall would otherwise read oddly.
            if (_liveApexBoss != null)
            {
                _liveApexBoss.Died -= HandleApexBossDied;
                _liveApexBoss.Kill();
                _liveApexBoss = null;
            }

            // Show the Heart at its terminal critical state for the defeat beat.
            _heart?.SetState(HeartState.Critical);

            Debug.Log("[WaveManager] The Heart of Elarion has fallen — village defeat. Wave loop halted.");
            OnDefeat?.Invoke();
        }

        // =====================================================================
        //  Breach -> ATB hand-off
        // =====================================================================

        /// <summary>
        /// One or more enemies crossed the inner ring. Pauses the loop and hands
        /// off to the ATB "Last Stand" battle. (The real-time "Defend the Tower"
        /// shooter / PatriciaLight branch has been retired — the breach now routes
        /// straight to the ATB path so a breach is never a dead end.)
        /// </summary>
        private void TriggerBreach()
        {
            SetPhase(WavePhase.Breached, "TriggerBreach");
            OnBreach.Invoke(_currentWaveId);
            if (_heart != null) _heart.SetState(HeartState.Critical);

            // Snapshot the breaching roster's count BEFORE EnterAtbBattle consumes
            // the roster (it keeps using the breach roster verbatim).
            int breachCount = _breachRoster.Count;

            Debug.Log(
                $"[WaveManager] Wave {_currentWaveId} breached with {breachCount} enemies — " +
                "handing off to the ATB Last Stand.");

            EnterAtbBattle();
        }

        /// <summary>
        /// Consumes the breaching roster and hands off to the ATB "Last Stand"
        /// scene via the real <see cref="SceneRouter.GoBattle"/> API. This is the
        /// unchanged pre-WO-47 breach behaviour, extracted so the breach choice
        /// prompt (and its fallback) can invoke it directly.
        /// </summary>
        private void EnterAtbBattle()
        {
            // BreachedIds: the 3D-layer ids of the breaching enemies. The ATB
            // BattleController maps these onto engine combatant defs (today via
            // its fallback; the per-enemy mapper is its own follow-up).
            // BUG-009: hand the ATB the ENGINE def id per breacher (Enemy.EngineDefId
            // → a valid ENEMY_DEFS key like "skeleton"/"necromancer"), not the
            // per-instance EnemyId — so the battle roster matches who actually
            // breached instead of always using the single fallback enemy.
            var breachedIds = new List<string>(_breachRoster.Count);
            foreach (Enemy e in _breachRoster)
            {
                if (e != null && !string.IsNullOrEmpty(e.EngineDefId))
                    breachedIds.Add(e.EngineDefId);
            }

            var battleParams = new BattleParams
            {
                Wave = _currentWaveId,
                BreachedIds = breachedIds.ToArray(),
                // Pets are wired in the Week-4 pet pass; an empty array is valid.
                ParticipatingPetIds = System.Array.Empty<string>(),
            };

            // Consume the breaching enemies out of the village field — they have
            // "left" the 3D layer and now exist only as ATB combatants. The rest
            // of the wave is abandoned: when the ATB scene returns to the Village
            // a fresh WaveManager.Start() re-runs the loop from the same wave.
            foreach (Enemy e in _breachRoster)
                if (e != null) e.Kill();
            _liveEnemies.Clear();
            _breachRoster.Clear();
            // WO-1113: the rest of the wave is abandoned by design here, held reinforcements
            // included — zero the count so the abandoned wave cannot hold the clear gate open.
            _heldSmartReinforcements = 0;

            // If an apex wave also fielded the flying boss, it leaves the 3D
            // layer with the rest of the wave — destroy it so it does not orbit
            // an empty village while the ATB scene is up. The dragon is its own
            // encounter; ground enemies breaching abandons the apex wave too.
            if (_liveApexBoss != null)
            {
                _liveApexBoss.Died -= HandleApexBossDied;
                Destroy(_liveApexBoss.gameObject);
                _liveApexBoss = null;
            }

            Debug.Log(
                $"[WaveManager] Wave {_currentWaveId} breached with " +
                $"{battleParams.BreachedIds.Length} enemies — handing off to ATBBattle.");

            // SceneRouter.GoBattle stashes the params on SceneRouter.PendingBattle
            // and fades into ATBBattle. BattleController reads PendingBattle and,
            // on resolve, fades back to the Village scene (BattleController
            // .ReturnAfterResult). Fire-and-forget — never await in a hot path.
            SceneRouter.GoBattle(battleParams).Forget();
        }

        // =====================================================================
        //  Enemy event handlers
        // =====================================================================

        private void HandleEnemyDied(Enemy enemy)
        {
            if (enemy != null)
            {
                enemy.Died -= HandleEnemyDied;
                enemy.ReachedHeart -= HandleEnemyReachedHeart;
                _enemyBestSqr.Remove(enemy);     // bug-triage P1: prune stuck-tracking on normal death (was leaking dead keys)
                _enemyStuckTime.Remove(enemy);
            }
            _liveEnemies.Remove(enemy);

            // WO-330: optional small per-kill resource trickle (the wave-clear bonus is
            // the primary reward). Only paid while the wave is still ACTIVE so the forced
            // removals on a breach hand-off (phase Breached) / defeat don't pay out, and
            // only for a real death (the enemy actually reached zero HP, not a stuck-cull
            // Kill()). Routes through the same EconomyService wallet as the wave-clear bonus.
            if (_awardResourcesPerKill
                && _phase == WavePhase.Active
                && enemy != null && enemy.Hp <= 0f
                && (_woodPerKill > 0 || _ironPerKill > 0))
            {
                EconomyService.Instance?.Grant(wood: _woodPerKill, iron: _ironPerKill);
            }
        }

        /// <summary>
        /// The apex flying boss died. Unsubscribes and drops the reference — the
        /// dragon destroys its own GameObject after the death-fall, and
        /// <see cref="TickActiveWave"/> then sees the wave as clear.
        /// </summary>
        private void HandleApexBossDied(DragonBoss boss)
        {
            if (boss != null) boss.Died -= HandleApexBossDied;
            if (_liveApexBoss == boss) _liveApexBoss = null;
        }

        /// <summary>
        /// An enemy walked all the way to the Heart without crossing the breach
        /// ring earlier (e.g. ring radius smaller than the Heart arrival radius).
        /// Treat it as a breach so the Heart is never silently overrun.
        /// </summary>
        private void HandleEnemyReachedHeart(Enemy enemy)
        {
            if (_phase != WavePhase.Active) return;
            // WO-579 (#4): in-hub resolution (default) — an enemy that reaches the Heart simply
            // contact-attacks it (Enemy.TickContactAttack strikes HeartController : IDamageableStructure),
            // draining its HP toward the defeat condition. The wave does NOT pause/load the deprecated
            // ATBBattle scene. Only the legacy breach→ATB route (ff.wavebreachtoatb) still hands off.
            if (!FeatureFlags.WaveBreachToAtb) return;
            if (enemy != null && !_breachRoster.Contains(enemy))
            {
                _breachRoster.Add(enemy);
                TriggerBreach();
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>
        /// TRUE when waves.json AUTHORS this wave's heavy — a named <c>boss</c> id or an
        /// <c>apexBoss</c> block. The single predicate behind the one-heavy-authority rule
        /// (2026-08-16): it both suppresses WaveCompositionBuilder's every-5th-wave elite
        /// cadence and tells the zero-spawn guard a heavy is still coming. Kept as ONE
        /// method so those two consumers can never disagree about what "has a boss" means.
        /// </summary>
        private static bool WaveHasAuthoredHeavy(WaveDef wave)
            => wave != null && (!string.IsNullOrEmpty(wave.Boss) || wave.IsApexBossWave);

        /// <summary>
        /// Everything a TOWN-SIDE look-ahead needs to compute the roster the player will meet
        /// next, without reaching into the loop's private state (added 2026-08-20 for
        /// <see cref="UpcomingWaveWarmPlanner"/>, which warms enemy art in encounter order while
        /// the player is placing buildings).
        /// <para>READ-ONLY AND SIDE-EFFECT FREE by construction: it returns cached fields and
        /// never loads, never awaits, never advances the loop. It answers FALSE (rather than
        /// guessing) when the schedule or the catalog has not landed yet — a caller that warms
        /// the wrong wave's families is worse than one that warms none.</para>
        /// <para>"Upcoming" is the wave already counting down or active when the loop is running
        /// one, and <c>CurrentWaveId + 1</c> otherwise: during Build Mode the loop is frozen, so
        /// the wave the player will next meet is the one after the last-known wave.</para>
        /// </summary>
        public bool TryDescribeUpcomingWave(out int waveId, out bool hasAuthoredHeavy, out EnemyCatalog catalog)
        {
            waveId = Mathf.Max(1, _currentWaveId);
            hasAuthoredHeavy = false;
            catalog = _enemyCatalog;

            if (_schedule == null || _enemyCatalog == null) return false;

            bool loopHoldingAWave = _phase == WavePhase.Countdown || _phase == WavePhase.Active;
            waveId = Mathf.Max(1, loopHoldingAWave ? _currentWaveId : _currentWaveId + 1);

            // A wave past the authored schedule (endless) has no WaveDef and therefore no
            // authored heavy — false is the correct answer, not a failure.
            hasAuthoredHeavy = WaveHasAuthoredHeavy(_schedule.Find(waveId));
            return true;
        }

        private WaveSpawnPoint FindSpawnPoint(string spawnId)
        {
            if (_spawnPoints == null) return null;
            if (!string.IsNullOrEmpty(spawnId))
                foreach (WaveSpawnPoint p in _spawnPoints)
                    if (p != null && p.SpawnId == spawnId) return p;

            // Bug-fix (audit 2026-05-30): a missing named id used to skip the whole batch, so
            // the boss/apex never spawned. Fall back to a spawn point — but 2026-08-16 made
            // that fallback DETERMINISTIC (ordinal by SpawnId, not FindObjectsByType order)
            // and LOUD: the old Debug.LogWarning never reached break-log.jsonl, so an id that
            // could never match (the boss's hardcoded "spawn-0") looked like a working spawn
            // for months while the enemy entered from an arbitrary side.
            WaveSpawnPoint fallback = WaveSpawnResolver.FirstDeterministic(_spawnPoints);
            if (fallback == null) return null;
            if (!string.IsNullOrEmpty(spawnId))
                FlowTrace.Fail("Wave",
                    $"spawn id '{spawnId}' does not exist in this scene — the batch will release " +
                    $"from '{fallback.SpawnId}' (direction '{fallback.Direction}') instead. Live ids " +
                    "are produced by CastleSpawnPointInjector as 'spawn-castle-<dir>-<i>'; an " +
                    "authored id that never matches is a wrong-gate defect, not a harmless default.");
            return fallback;
        }

#if UNITY_EDITOR
        // Draws the inner breach ring in the Scene view while authoring.
        private void OnDrawGizmosSelected()
        {
            Vector3 center = _heart != null ? _heart.transform.position : transform.position;
            Gizmos.color = new Color(0.86f, 0.27f, 0.27f, 0.6f);
            const int steps = 48;
            Vector3 prev = center + new Vector3(_innerRingRadius, 0f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(
                    Mathf.Cos(a) * _innerRingRadius, 0f, Mathf.Sin(a) * _innerRingRadius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
