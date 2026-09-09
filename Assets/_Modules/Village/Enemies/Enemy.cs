// =============================================================================
// Enemy — one Hollow One marching on Elarion (Week-4 wave-loop slice).
// -----------------------------------------------------------------------------
// Port spec Part 3 row: src/modules/village/enemies/ -> Enemy.cs.
// Port spec Part 5 Week 4: "KayKit skeleton mesh, NavMeshAgent, walks toward the
// Heart, attacks buildings/walls on contact, dies on HP zero."
//
// One Enemy MonoBehaviour drives the nav, HP and on-contact attack of a single
// wave enemy. It is configured from an EnemyDef (the deserialised enemies.json
// stat block) by the WaveManager right after instantiation.
//
// NAVMESH: the enemy uses a UnityEngine.AI.NavMeshAgent (the legacy AI module —
// com.unity.modules.ai, already in the manifest). The agent walks toward the
// Heart's world position. ** The village scene MUST have a baked NavMesh for
// this to move ** — see docs/port-notes/week4-waves.md. This script assumes one
// exists and degrades gracefully (logs once, holds position) if it does not.
//
// CONTACT ATTACK: the enemy raycasts/overlaps for an IDamageableStructure ahead
// of it (a building / wall / gate). On contact it stops and deals contactDamage
// every attackInterval seconds. IDamageableStructure is defined here so Enemy
// has NO compile dependency on a specific Building/Gate damage API — the
// integrator adds the interface to those MonoBehaviours when their HP gameplay
// lands. Until then enemies simply path to the Heart.
//
// BREACH: the WaveManager owns inner-ring breach detection (it knows the ring
// radius). Enemy just exposes its EnemyId / EnemyDefId / EngineDefId so the
// breach trigger can hand the breaching roster to the ATB scene.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.AI;
using DeNelle.Core.UI;
using DeNelle.Core.Combat;   // IDamageableStructure — moved to Core so all assemblies can reference it

namespace DeNelle.Village
{
    /// <summary>
    /// WO-1232 (owner ruling 2026-08-26): an enemy's AUTHORED classification — the only
    /// thing the HUD is allowed to say about "what am I facing". IDENTITY, never DIFFICULTY:
    /// a Necromancer being a boss is an authored fact (<c>enemies.json boss:true</c> /
    /// <c>role:"elite"</c>); a level number was not. Deliberately THREE members — an APEX
    /// tier is RESERVED until one is authored on purpose, because inventing a third badge
    /// re-creates the fake precision this ruling removed.
    /// </summary>
    public enum EnemyTier
    {
        /// <summary>No authored classification. The HUD shows NOTHING — silence is the default.</summary>
        Ordinary = 0,
        /// <summary><c>role:"elite"</c> on the def. Shown as the word ELITE.</summary>
        Elite = 1,
        /// <summary><c>boss:true</c> on the def (necromancer, troll-overlord). Shown as the word BOSS.</summary>
        Boss = 2,
    }

    /// <summary>
    /// One Hollow One in the village wave loop. Drives a <see cref="NavMeshAgent"/>
    /// toward the Heart, takes HP damage, attacks the structure in front of it on
    /// contact, and dies at zero HP. Configured by <see cref="WaveManager"/> from
    /// an <see cref="EnemyDef"/>. Instantiated per spawn; pooling is a later pass.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    // Targeting fix (open world): every Enemy auto-gets the EnemyDamageable adapter so
    // the hero's melee + ability OverlapSphere sweeps (GetComponentInParent<IDamageable>,
    // Faction.Hostile) can actually hit it on EVERY spawn path. Previously only
    // WaveManager / PatriciaLight AddComponent'd it, so the open-world RegionMobSpawner /
    // TribeManager roaming mobs had no IDamageable and could not be targeted or damaged.
    [RequireComponent(typeof(EnemyDamageable))]
    public sealed class Enemy : MonoBehaviour
    {
        // ── Inspector tuning (overridden by Configure from the EnemyDef) ──────

        [Header("Identity")]
        [Tooltip("Stable per-instance id — e.g. 'wave1-hollow-walker-3'. The breach roster key.")]
        [SerializeField] private string _enemyId;

        [Tooltip("enemies.json def id this enemy was spawned from — e.g. 'hollow-walker'.")]
        [SerializeField] private string _enemyDefId;

        [Header("Stats (set by Configure from enemies.json)")]
        [Tooltip("Current hit points.")]
        [SerializeField] private float _hp = 52f;

        [Tooltip("Max hit points.")]
        [SerializeField] private float _maxHp = 52f;

        [Tooltip("Display level shown on the target frame ('Lv N'). Set from the def in Configure; " +
                 "1 for a hand-placed enemy with no def. See the Level property (WO-611 F3).")]
        [SerializeField] private int _level = 1;

        [Tooltip("NavMeshAgent speed — world units/sec.")]
        [SerializeField] private float _moveSpeed = 2.5f;

        [Tooltip("Damage dealt to a structure per melee hit.")]
        [SerializeField] private float _contactDamage = 6f;

        /// <summary>The authored per-hit contact damage (from enemies.json <c>def.ContactDamage</c>,
        /// post wave-scaling). HeroHealth's contact tick reads THIS per adjacent enemy so each
        /// enemy hits for its real stat (berserker 15 vs necromancer 18 vs walker 8) instead of a
        /// single hardcoded flat value — turns the authored contactDamage column live for hero combat.</summary>
        public float ContactDamage => _contactDamage;

        // =====================================================================
        //  DYNAMIC-DIFFICULTY BASE STATS -- READ THIS BEFORE TOUCHING THE SPAWN PATH
        // ---------------------------------------------------------------------
        //  *** THIS BODY IS POOLED AND ITS STATE SURVIVES Release/Get. ***
        //  EnemyPool.Get hands the SAME Enemy component out over and over;
        //  PrepareForReuse revives it, it does NOT rebuild it. Every field on this
        //  class therefore carries over from the previous life unless something
        //  explicitly clears it.
        //
        //  THAT IS WHY DIFFICULTY SCALING IS NEVER APPLIED IN PLACE. A line like
        //  `_maxHp *= mult` on a body that has been reused five times applies the
        //  multiplier FIVE TIMES -- mult^5, exponential, and invisible, because each
        //  individual application looks perfectly correct in isolation.
        //  <see cref="ApplyDifficulty"/> always computes base * mult from a base that
        //  was CAPTURED FRESH for THIS spawn (<see cref="SetBaseStats"/>) and never
        //  reads the current value.
        //
        //  -1 means "uncaptured": ApplyDifficulty is a NO-OP until the spawner has
        //  called SetBaseStats for this spawn, so a body can never be scaled off a
        //  stale base. Both fields are cleared on BOTH pool-reset sides through
        //  ClearPooledLatches (called by ResetForPool AND PrepareForReuse).
        // =====================================================================
        private float _baseMaxHp = -1f;
        private float _baseContactDamage = -1f;

        /// <summary>The contact damage captured by <see cref="SetBaseStats"/> for this spawn
        /// (-1 = uncaptured). Exposed so the boss-HP-pin site can RE-base on the pinned HP
        /// while keeping the ORIGINAL damage base -- re-basing on the live
        /// <see cref="ContactDamage"/> after a difficulty pass would compound it.</summary>
        public float BaseContactDamage => _baseContactDamage;

        /// <summary>The max HP captured by <see cref="SetBaseStats"/> for this spawn (-1 = uncaptured).</summary>
        public float BaseMaxHp => _baseMaxHp;

        [Tooltip("Seconds between melee hits while in contact.")]
        [SerializeField] private float _attackInterval = 1.3f;

        [Tooltip("AI archetype — Walker marches straight; Charger / Skirmisher are later waves.")]
        [SerializeField] private EnemyAiKind _ai = EnemyAiKind.Walker;

        [Tooltip("Air/ground targeting matrix: when true this enemy FLIES — only anti-air " +
                 "(or 'both') towers can hit it. Set from the enemies.json 'movement' field " +
                 "in Configure; defaults false (ground) so a hand-placed enemy with no def " +
                 "is ground unless this is ticked in the prefab.")]
        [SerializeField] private bool _isFlying;

        [Header("Contact attack tuning")]
        [Tooltip("Distance ahead the enemy probes for an attackable structure (world units).")]
        [SerializeField] private float _contactProbeDistance = 1.1f;

        [Tooltip("Structure-awareness fix (ff.enemystructureaware): radius of the all-direction sweep a " +
                 "BRAIN-LESS enemy uses to acquire a nearby live structure (side tower/wall, or the Heart " +
                 "tree) it would otherwise march past — the forward-only probe missed ~99.7% of them. " +
                 "Kept short so a locked structure is within striking range. Suppressed while the hero is " +
                 "in aggro range (hero stays primary).")]
        [SerializeField, Range(1f, 8f)] private float _structureSweepRadius = 3f;

        [Tooltip("Distance from the Heart at which the enemy considers itself 'arrived'.")]
        [SerializeField] private float _heartArrivalRadius = 2.5f;

        // WO-419: the agent's stoppingDistance is normally _heartArrivalRadius (2.5 m) so a
        // siege enemy halts a body-length off the Heart/structure it batters. But the HERO
        // has NO physical collider (HeroControlEnsurer destroys it so HeroLocomotion's
        // CapsuleCast can't self-block) and is NOT struck by the enemy's forward contact
        // probe — instead HeroHealth.Update scans for enemies within its own EngageRadius
        // (1.5 m) and self-applies the contact tick. So a hero-CHASING enemy that stops at
        // 2.5 m never enters the 1.5 m damage ring and deals ZERO damage. This is invisible
        // in the castle (enemies batter the gate/Heart/walls — real colliders) but in
        // the overworld the hero is the ONLY target, so the seam reads as "enemies don't attack
        // after I cross into the overworld" (WO-419). When actively chasing the hero we tighten
        // stoppingDistance to this melee value so the enemy closes INSIDE the hero's engage
        // ring; it is restored to _heartArrivalRadius the moment the chase ends.
        private const float HeroChaseStoppingDistance = 1.1f;
        // CORE-LOOP RCA (EnemyAggro 2026-06-18): a brain-driven enemy whose Rush path to the
        // hero went PARTIAL steers to the last reachable corner (a few metres short of the
        // hero), so the override no longer sits exactly on the hero. We still treat it as a
        // hero-chase — and keep the tight stoppingDistance so it enters the 1.5 m damage ring
        // — whenever the ENEMY ITSELF is within this radius of the hero and its destination
        // points hero-ward. Generous enough to cover the partial-path standoff, tight enough
        // not to mis-fire on a far-off siege march.
        private const float HeroChaseProximity = 4f;
        private bool _stopTightenedForHero;   // tracks which stoppingDistance is currently applied

        [Header("Hero aggro (DEF-224)")]
        [Tooltip("When the hero comes within this radius the enemy breaks off its Heart-siege " +
                 "march and closes on the hero to attack it, then returns to the Heart-siege " +
                 "path once the hero leaves. 0 disables hero-aggro entirely. " +
                 "This is ADDITIVE — when an EnemyBrain is actively steering this enemy " +
                 "(role/tactical/retaliation override set) the brain wins and this stays out of " +
                 "the way, so it only governs the plain wave/roamer enemies that carry no brain.")]
        [SerializeField, Range(0f, 20f)] private float _heroAggroRadius = 7f;

        [Tooltip("Hysteresis: once aggro'd, the enemy keeps chasing the hero until the hero " +
                 "moves THIS much further than _heroAggroRadius — stops the enemy flickering " +
                 "between chase and march at the radius edge.")]
        [SerializeField, Range(0f, 6f)] private float _heroAggroDropMargin = 2.5f;

        /// <summary>
        /// Seconds the dead enemy GameObject lingers so its death animation can
        /// play before <see cref="Die"/> destroys it. Only applied when the enemy
        /// has an Animator; with none it is destroyed immediately.
        /// </summary>
        private const float DeathHoldSeconds = 3.5f;   // owner 2026-06-23: was 1.6f (cut the death anim) -> let the death cycle play through. (The dramatic ~10s camera linger on the BATTLE-WINNING kill is a separate death-cam, WO-493.)

        /// <summary>
        /// Lifetime cap for an authored per-type hit/death VFX prefab spawned via
        /// <see cref="EnemyTypeVfxSet"/>. These prefabs were previously Instantiated
        /// with NO Destroy (a hard GameObject leak per hit / per kill); they now
        /// self-destruct after this window — long enough for the burst to finish,
        /// short enough that nothing accumulates.
        /// </summary>
        private const float TypeVfxSelfDestructSeconds = 3f;

        // felt-tune knob — owner slowing battle a touch 2026-07-03. A SINGLE central dial that
        // lengthens EVERY enemy's attack interval by a small factor (1.0 = no change). 1.12 = ~+12%
        // between strikes, so the battle breathes a touch more without an overhaul. Every enemy
        // (roamer/wave/tribe/arena) routes through Configure, so all slow together. Fully reversible
        // — set back to 1.0f to restore the prior cadence. Does NOT touch the hero.
        private const float EnemyAttackIntervalScale = 1.12f;

        // ── Runtime refs / state ──────────────────────────────────────────────

        private NavMeshAgent _agent;
        private Transform _heart;
        private EnemyDef _def;
        private float _attackCooldown;
        private bool _dead;
        private bool _navWarned;

        // ── Pooling (EnemyPool) ───────────────────────────────────────────────
        // The key the EnemyPool files this body under (EnemyDef model id / prefab
        // name) so Release routes it back to the queue it can be reused from. Set
        // once by the pool when the body is first built; survives reuse.
        private string _poolKey;

        /// <summary>The EnemyPool key this body is filed under (set by the pool).</summary>
        public string PoolKey => _poolKey;

        /// <summary>Stamps the pool key (called by <see cref="EnemyPool"/> on first build).</summary>
        public void SetPoolKey(string key) => _poolKey = key;

        // POOL-RESET AUDIT (2026-08-02): the sibling EnemyBrain carries its OWN latch set
        // (tactics / role / leash / room / flank / provoke) that Enemy's reset never touched,
        // so a pooled body kept the previous life's brain state forever. Cached once so the
        // reset path does not GetComponent on every release/acquire.
        private EnemyBrain _brain;
        private bool       _brainResolved;

        // POOL-RESET AUDIT: Configure only OVERWRITES _heroAggroRadius when the incoming def
        // authors AggroRadius > 0. A body that once carried a 14 m outpost-guard def and is
        // then reused for a def with no authored radius would keep 14 m for the rest of the
        // session. Captured at Awake (the prefab/inspector-authored value) and restored on
        // every pool reset so the dial can never ratchet across reuses.
        private float _authoredHeroAggroRadius = -1f;

        private bool _telegraphing;   // DEF-48: true during wind-up — blocks double-trigger
        private IDamageableStructure _currentTarget;

        // ── Structure-probe CADENCE gate (WO-1450 + WO-1459 §2 suspect 3) ─────────────
        // ProbeForStructure runs a forward SphereCast AND (flag-on, hero not near) an
        // all-direction OverlapSphere on mask ~0 — EVERY LAYER. Until this gate it ran
        // once PER FRAME for every enemy holding no target, which is the third named
        // suspect behind the captured device floor:
        //   LOW fps=11 ms=87.4 mem=427MB scene=RaidBase_raider_camp_small towers=0 enemies=13
        // (WO-1459, 2026-09-06 device session, timeScale=1.00 — a real frame cost).
        // Thirteen enemies x 2 physics queries x 60 fps is 1,560 all-layer queries a second.
        //
        // ⚠ THE GATE IS ON THE RETRY CADENCE ONLY — NOT ON SELECTION. It is consulted
        // solely on the branch where _currentTarget is already null; a HELD target still
        // drops the same frame it dies or flees, and ProbeForStructure's own logic
        // (forward cast first, hero-primary suppression, CombatFactionRules.MayAttack)
        // is untouched. A skipped frame takes the identical `_attackCooldown = 0f; return;`
        // path a null probe result already took, so the observable behaviour on those
        // frames is byte-for-byte what a failed probe produced.
        private const float ProbeIntervalSeconds = 0.25f;   // <= 4 probes/sec/enemy
        private float _nextProbeAt;

        // WO-1450: the acquire trace fired on EVERY probe hit — 38,018 lines between
        // 12:59:05 and 14:37:52 (~320/sec), each carrying a managed stack walk. The
        // Android main ring is 256 KiB, so that one line evicted the boot window and
        // every other trace in under two seconds (memory: logcat-ring-buffer-destroys-
        // evidence). Holding the last acquired target's instance id lets the trace fire
        // on a target CHANGE — the event anyone actually reads — instead of per hit.
        private int _lastProbeTargetId;
        private bool _attackTokenHeld;
        private bool _contactCommitPending;
        private bool _contactCommitInterrupted;
        private IDamageableStructure _contactCommitTarget;
        private const float ContactHitFallbackSeconds = 0.72f;
        private const float ContactRecoverSeconds = 0.48f;

        // ── Smooth target/attack facing (anti-snap) ──────────────────────────
        // The old target-facing did `transform.rotation = LookRotation(toTarget)`
        // ONCE per shot/attack — an instant snap that read as the Wights jerking
        // left/right. We instead record a desired (Y-flattened) facing direction
        // here and slerp the root toward it every frame in TickFacing(), exactly
        // mirroring the path-facing slerp (~691). Upright/Y-only is preserved by
        // flattening the direction before building the LookRotation, so the enemy
        // never tips. Turn rate is tunable: a Slerp factor that reads natural for
        // a ground unit pivoting to face. (Velocity path-facing still drives while
        // moving; this fills in when the agent is stopped to attack.)
        private bool _hasFaceTarget;
        private Vector3 _faceTargetDir = Vector3.forward;
        private const float FaceTurnSlerp = 9f; // deg-rate feel ~matches 691's 10f

        private ActorAnimator _actor; // guarded driver (Core) for Speed/Attack/Hit/Dead on the visual child controller

        // ── Position-delta locomotion fallback (anti-slide) ──────────────────
        // DEF-56 / formation-slot drift: during throttled SetDestination + formation
        // slot moves the NavMeshAgent's velocity reads ~0 even while the transform
        // actually slides. That left Speed=0 -> idle pose gliding. We fall back to
        // a position delta (horizontal only) when agent.velocity is near-zero, so
        // the blend tree blends to walk/run in BOTH the arena (formation) and the
        // overworld (solo brain) contexts.
        private Vector3 _lastAnimPos;
        private bool _hasLastAnimPos;
        // ANTI-CHOP (owner 2026-07-02 "enemy anims off/choppy"): exponentially smoothed
        // Speed feed for the Animator. Raw NavMeshAgent velocity fluctuates every frame
        // (avoidance / accel / formation-slot drift / the delta-estimator below), so an
        // undamped feed makes the 1-D locomotion blend flicker across its thresholds
        // (idle<->walk<->run pops). Only the ANIM feed is damped; gameplay reads raw.
        private float _animSpeedSmoothed;
        private const float AnimSpeedDampSecs = 0.12f; // ~response time of the smoothing
        private bool _presentationCombat;   // overworld rep / external alert — braced combat locomotion

        // ── DEF-21 / DEF-72: EnemyBrain nav-target override ──────────────────
        // DEF-21: EnemyBrain.Update() calls SetBrainTarget each frame with a role-
        //   specific Transform destination. When non-null DriveNav() follows this
        //   instead of _heart. When null (default, or Role == DPS), Heart-march resumes.
        // DEF-72: EnemyBrain.Update() calls SetBrainTargetPosition each frame with
        //   a computed Vector3 destination (flank offset, retreat vector, etc.).
        //   When non-null it overrides both _brainTarget and _heart. This is the
        //   tactical-overlay path; _brainTarget is the role-only (no-tactics) path.
        private Transform _brainTarget;
        private Vector3?  _brainPositionOverride;   // DEF-72

        // ── DEF-224: hero aggro ──────────────────────────────────────────────
        // Plain wave/roamer enemies carry no EnemyBrain (the factory + wave path
        // never AddComponent one), so the brain's hero-engage / retaliation never
        // ran and the owner saw enemies "ignore the hero at point-blank". This is a
        // self-contained, brain-independent aggro: when the hero is inside
        // _heroAggroRadius the enemy steers at it (and ProbeForStructure's SphereCast
        // then hits the hero's CapsuleCollider → the existing contact-attack lands,
        // since HeroHealth implements IDamageableStructure). When the hero leaves
        // (with hysteresis) the Heart-siege march resumes unchanged.
        private Transform _heroTransform;
        private bool      _heroAggroEngaged;     // sticky once in range (hysteresis)
        private float     _heroResolveTimer;     // periodic re-resolve (hero may spawn late / respawn)

        // ── Dungeon/outpost in-scene battle-lock (2026-06-30 "0 damage in dungeon" fix) ──
        // The hero's attack input (PlayerAttackController / HeroAbilityInput) is gated on
        // BattleLock.IsInBattle(), which only the STAGED battle owners raise. The in-place
        // OutpostEnemyGroupSpawner hollows (heart==null, EnemyBrain.HeroOnlyTarget) stage NO
        // BattleArena, so the lock stayed FALSE and every hero swing/cast was suppressed — the
        // ONLY damage that landed was the passive reflect (dealtByHero=false). We raise
        // HeroCombatEngagement (a BattleLock source) while such a duelist has the hero in aggro
        // range, so the hero can actually fight them. Scoped to HeroOnlyTarget combatants, so it
        // never trips on overworld roamers (they pop the arena) or heart-siege wave enemies.
        private EnemyBrain _engageBrain;         // cached hero-only brain (null for non-duelists)
        private bool       _engageBrainResolved; // resolve-once latch (re-armed in Configure for pooling)
        private bool       _engagedLatched;      // current membership in HeroCombatEngagement (edge-triggered)

        // Structure-awareness fix (ff.enemystructureaware): reused buffer for the brain-less
        // all-direction structure sweep (OverlapSphereNonAlloc) so it never allocs per tick.
        private readonly Collider[] _structureScanBuffer = new Collider[16];

        // ── DEF-56: path throttle ─────────────────────────────────────────────
        // SetDestination is O(navmesh) — calling it every frame for 20+ enemies
        // is the main NavMesh CPU cost. Throttle to _pathRefreshInterval seconds
        // AND only when the heart has moved more than _pathMinMoveDelta world units.
        private float   _pathRefreshTimer;
        private Vector3 _lastPathedDestination;

        /// <summary>DEF-56: Minimum seconds between NavMesh path requests.</summary>
        [SerializeField, Range(0.1f, 1f)] private float _pathRefreshInterval = 0.25f;

        /// <summary>
        /// DEF-56: Minimum world-unit delta the Heart must move before a new path
        /// request fires early. Prevents redundant requests when the Heart is idle.
        /// </summary>
        [SerializeField, Range(0.1f, 2f)] private float _pathMinMoveDelta = 0.5f;

        // ── DEF-46: per-type VFX / audio + directional hit reactions ─────────

        [Header("Type VFX + Audio (DEF-46)")]
        [Tooltip("Per-archetype hit/death/attack VFX and audio. " +
                 "Leave blank to use the built-in VfxPool fallbacks.")]
        [SerializeField] private EnemyTypeVfxSet _typeVfxSet;

        /// <summary>
        /// Latched in <see cref="Awake"/>: TRUE when a prefab actually authored
        /// <c>_typeVfxSet</c>, so <see cref="EnsureTypeVfxSet"/> never overwrites hand-
        /// authored art with the library floor. Pool reuse keeps the latch (Awake runs
        /// once per instance, and the serialized value cannot change after that).
        /// </summary>
        private bool _typeVfxSetAuthored;

        // AudioSource is optional — Enemy does not require one, but needs it to
        // actually play the clip. Resolved in Awake if not set in Inspector.
        [Tooltip("AudioSource used to play hit / death / attack clips. " +
                 "Resolved in Awake from the same or child GameObjects if blank.")]
        [SerializeField] private AudioSource _audioSource;

        [Header("Death VFX Override (WO-84)")]
        [Tooltip("Override the death VFX type per prefab — leave Death_Generic to use " +
                 "the VfxPool fallback. Elite/Boss always delegate to EliteVFXController.")]
        [SerializeField] private VFXType _deathVFXOverride = VFXType.Death_Generic;

        [Header("Heavy Hit (WO-84)")]
        [Tooltip("Damage at or above this value triggers the heavy-hit path: " +
                 "larger VFX spawn + stronger camera shake.")]
        [SerializeField] private float _heavyHitThreshold = 20f;

        /// <summary>
        /// The cardinal quadrant a hit came from, relative to the enemy's facing.
        /// Drives the directional flinch sub-state in the Animator.
        /// </summary>
        private enum HitDirection { Front = 0, Left = 1, Right = 2, Back = 3 }

        // ── Animation ─────────────────────────────────────────────────────────
        // The KayKit skeleton mesh carries an Animator (the AnimatorSetup editor
        // script builds HumanoidEnemy/LargeEnemy/Boss.controller; the integrator
        // assigns one to the enemy prefab — see docs/port-notes/animation-setup.md).
        // Enemy DRIVES it: Speed float from movement, Attack/Hit triggers on the
        // contact strike + damage, Dead bool on death. All parameter sets are
        // null-guarded so an enemy with no Animator still runs its gameplay.
        private Animator _animator;
        // Controllers may arrive after spawn through EnemyAnimatorLateBinder. Track
        // which controller supplied the cached parameter set so a late bind cannot
        // leave a moving enemy permanently classified as having no Speed parameter.
        private RuntimeAnimatorController _scannedAnimatorController;

        // Combat-feel (additive): red hit-flash on each non-lethal hit. Auto-added
        // in Awake so it needs no prefab wiring; flashed from the hit branch below.
        private EnemyHitReaction _hitReaction;

        // WO-178: floating world-space HP bar. Auto-added in Awake (no prefab
        // wiring); reads HpFraction / IsDead and tears itself down on death.
        private FloatingHealthBar _healthBar;

        // Animator parameter hashes — must match AnimatorSetup.cs's parameter
        // names ("Speed" / "Attack" / "Hit" / "Dead" / "HitDir").
        private static readonly int AnimSpeed   = Animator.StringToHash("Speed");
        private static readonly int AnimAttack  = Animator.StringToHash("Attack");
        private static readonly int AnimWindUp  = Animator.StringToHash("WindUp");  // DEF-48 telegraph
        private static readonly int AnimHit     = Animator.StringToHash("Hit");
        private static readonly int AnimDead    = Animator.StringToHash("Dead");

        /// <summary>
        /// DEF-46: int parameter (0=Front, 1=Left, 2=Right, 3=Back) set BEFORE
        /// the Hit trigger fires so the sub-state can blend to the right flinch.
        /// </summary>
        private static readonly int AnimHitDir = Animator.StringToHash("HitDir");

        // WO-163: cached once when the Animator resolves — whether THIS enemy's
        // controller actually declares each param. Driving an absent param logs an
        // error EVERY frame (the 3,351-error spam). Guard every SetFloat/SetBool/
        // SetTrigger/SetInteger with these. A controller with no
        // runtimeAnimatorController has no parameters → all stay false.
        private bool _hasSpeedParam;
        private bool _hasAttackParam;
        private bool _hasWindUpParam;
        private bool _hasHitParam;
        private bool _hasDeadParam;
        private bool _hasHitDirParam;

        /// <summary>Raised when this enemy's HP reaches zero. Arg = this enemy.</summary>
        public event Action<Enemy> Died;

        /// <summary>
        /// Raised when this enemy reaches the Heart without being killed. The
        /// WaveManager listens to escalate the Heart's threat state.
        /// </summary>
        public event Action<Enemy> ReachedHeart;

        /// <summary>
        /// Raised when this enemy takes (non-lethal) damage. Arg = world-space
        /// position of the damage source. EnemyBrain listens to RETALIATE — a
        /// struck enemy turns on its attacker (the hero/pet) instead of marching
        /// past to the Heart (owner 2026-06-02: "they just walked on past me").
        /// </summary>
        public event Action<Vector3> Damaged;

        /// <summary>Stable per-instance id — the breach-roster key.</summary>
        public string EnemyId => _enemyId;

        /// <summary>The <c>enemies.json</c> def id this enemy was spawned from.</summary>
        public string EnemyDefId => _enemyDefId;

        /// <summary>Authored affinity; missing and unknown values are neutral.</summary>
        public DeNelle.Core.Combat.DamageElement Affinity =>
            DeNelle.Core.Combat.ElementalDamageResolver.ParseElement(_def != null ? _def.Affinity : null);

        /// <summary>Authored vulnerabilities converted without identity inference.</summary>
        public System.Collections.Generic.IReadOnlyList<DeNelle.Core.Combat.DamageElement> ElementalVulnerabilities
        {
            get
            {
                var result = new System.Collections.Generic.List<DeNelle.Core.Combat.DamageElement>(1);
                if (_def?.VulnerableTo == null) return result;
                for (int i = 0; i < _def.VulnerableTo.Count; i++)
                {
                    var parsed = DeNelle.Core.Combat.ElementalDamageResolver.ParseElement(_def.VulnerableTo[i]);
                    if (parsed != DeNelle.Core.Combat.DamageElement.None && !result.Contains(parsed)) result.Add(parsed);
                }
                return result;
            }
        }

        /// <summary>
        /// The PRECISE catalog display name for this enemy ("Orcish Mage"), sourced from
        /// the def it was configured with (enemies.json / BuildEncounterDef / garrison
        /// blocks). HUD surfaces (target frame, target-cycle list) read THIS field —
        /// never a GameObject-name parse + role concatenation (owner: the target frame
        /// showed "Orc Mage Wizard", two stacked titles). Null/empty when no def was
        /// supplied; callers fall back to their friendly-parse.
        /// </summary>
        public string DisplayName =>
            _def == null ? null
            : !string.IsNullOrEmpty(_def.DisplayName) ? _def.DisplayName
            : _def.Name;

        /// <summary>Current hit points.</summary>
        public float Hp => _hp;

        // WO-1439 — this enemy's OWN side, read from the required EnemyDamageable adapter
        // (the class that already declares it) rather than hardcoded here. Cached lazily
        // with a fake-null-safe re-resolve, exactly like EnemyDamageable.E does, because
        // [RequireComponent] can add the adapter BEFORE Enemy on a runtime AddComponent and
        // an Awake-only cache would then be null forever (the 2026-06-02 root fix recorded
        // in EnemyDamageable.cs). Falls back to Hostile — an Enemy that somehow lost its
        // adapter is still a Hollow One, and the safe default must never turn a defender
        // Friendly-to-the-garrison and re-open this ticket.
        private EnemyDamageable _selfDamageable;

        /// <summary>
        /// WO-1439 — the faction this enemy fights FOR. Every structure/body selection it
        /// makes is arbitrated against this through
        /// <see cref="DeNelle.Core.Combat.CombatFactionRules.MayAttack"/>; nothing compares
        /// factions inline.
        /// </summary>
        public DeNelle.Core.Combat.CombatFaction SelfFaction
        {
            get
            {
                if (_selfDamageable == null) _selfDamageable = GetComponent<EnemyDamageable>();
                return _selfDamageable != null
                    ? _selfDamageable.Faction
                    : DeNelle.Core.Combat.CombatFaction.Hostile;
            }
        }

        /// <summary>Max hit points.</summary>
        public float MaxHp => _maxHp;

        /// <summary>HP as a 0..1 fraction — drives the floating HP bar.</summary>
        public float HpFraction => _maxHp > 0f ? Mathf.Clamp01(_hp / _maxHp) : 0f;

        /// <summary>
        /// ⚠ NOT A LEVEL SYSTEM, AND NO LONGER PLAYER-FACING (WO-1232, owner ruling 2026-08-26).
        /// <para>
        /// This is <c>round(def.Hp / 25)</c> — see <see cref="Configure"/>. There is NO authored level
        /// field on <c>EnemyDef</c>; every "level" in the game was this one division. The doc comment
        /// that used to sit here claimed an "authored per-archetype band", and that claim misled a
        /// whole work order (CLAUDE.md §12: comments lie, read the code). Owner verbatim:
        /// <i>"HP / 25 is not a level system. Dressing it up as one just produces very confident
        /// nonsense."</i>
        /// </para>
        /// <para>
        /// NO player-facing surface reads this any more — the HUD target frame shows the authored
        /// <see cref="Tier"/> word instead (see <see cref="EnemyTier"/>). It survives only as a
        /// diagnostic magnitude for <see cref="ThreatSkullPlate"/>'s display-off trace. Do not
        /// re-surface it; a real replacement is a Combat Rating derived from HP, damage, cadence,
        /// armour, abilities and encounter role, and that is a separate, unbuilt spec.
        /// </para>
        /// </summary>
        public int Level => Mathf.Max(1, _level);

        /// <summary>
        /// WO-1232: the enemy's AUTHORED classification — the single accessor the HUD reads to decide
        /// whether to print BOSS, ELITE, or nothing at all. Resolves off <c>_def</c> (the only species
        /// signal every spawn path sets), so a def-less hand-placed enemy is <see cref="EnemyTier.Ordinary"/>
        /// and silent. Boss OUTRANKS elite, matching <see cref="IsEliteTier"/>'s own exclusion.
        /// </summary>
        public EnemyTier Tier =>
            IsBossTier() ? EnemyTier.Boss :
            IsEliteTier() ? EnemyTier.Elite :
                            EnemyTier.Ordinary;

        /// <summary>True once the enemy has died (HP hit zero).</summary>
        public bool IsDead => _dead;

        /// <summary>Alive and a valid target — used by <see cref="TargetManager"/>'s
        /// registry queries (the reticle / towers).</summary>
        public bool IsAlive => !_dead && _hp > 0f;

        /// <summary>AI archetype this enemy runs.</summary>
        public EnemyAiKind Ai => _ai;

        /// <summary>
        /// True when this enemy flies — the air/ground targeting matrix gate.
        /// Anti-ground towers skip a flyer; anti-air (or "both") towers can hit it.
        /// Set from the enemies.json "movement" field in <see cref="Configure"/>;
        /// false (ground) by default. Read by <c>EnemyDamageable</c> (ICombatLayered)
        /// so a tower's acquisition loop can gate on it via the Core seam.
        /// </summary>
        public bool IsFlying => _isFlying;

        /// <summary>The air/ground <see cref="DeNelle.Core.Combat.CombatLayer"/> this enemy occupies.</summary>
        public DeNelle.Core.Combat.CombatLayer CombatLayer =>
            _isFlying ? DeNelle.Core.Combat.CombatLayer.Flying
                      : DeNelle.Core.Combat.CombatLayer.Ground;

        // ── DEF-21: EnemyBrain integration ────────────────────────────────────

        /// <summary>
        /// DEF-21: Override the nav destination used by <see cref="DriveNav"/> with
        /// a scene Transform (role-based path, no TacticalData). Pass null to revert
        /// to the Heart-march (the default and the DPS path).
        /// </summary>
        public void SetBrainTarget(Transform target) => _brainTarget = target;

        /// <summary>
        /// DEF-72: Override the nav destination with an explicit world-space
        /// <see cref="Vector3"/> computed by <see cref="EnemyBrain"/>'s tactical
        /// overlay (flank arc, retreat vector, etc.). Supersedes both
        /// <see cref="_brainTarget"/> and <see cref="_heart"/> when non-null.
        /// Pass null to clear the override and revert to the role/Heart-march path.
        /// </summary>
        public void SetBrainTargetPosition(Vector3? pos) => _brainPositionOverride = pos;

        /// <summary>Braced combat locomotion (InCombat) for overworld packs / alert hooks.</summary>
        public void SetCombatPresentation(bool on) => _presentationCombat = on;

        /// <summary>Cosmetic gesture while idle-roaming — wind-up, cast pose, or swing by role.</summary>
        public void PlayAmbientGesture()
        {
            var brain = GetComponent<EnemyBrain>();
            if (brain != null && brain.Role == EnemyRole.Ranged)
                _actor?.PlayCast();
            else if (brain != null && brain.Role == EnemyRole.Tank)
                _actor?.PlayWindUp();
            else
                _actor?.PlayAttack();
        }

        /// <summary>
        /// DEF-21: Restore HP by <paramref name="amount"/> up to <see cref="MaxHp"/>.
        /// Called by <see cref="EnemyBrain"/> (Healer role) on adjacent wounded allies.
        /// </summary>
        public void Heal(float amount)
        {
            if (_dead || amount <= 0f) return;
            _hp = Mathf.Min(_maxHp, _hp + amount);
        }

        /// <summary>
        /// The engine def id the breach trigger maps this enemy to when handing
        /// the ATB scene a battle. Maps village enemies.json ids to the ATB engine's
        /// <see cref="DeNelle.BattleATB.Engine.Defs.ENEMY_DEFS"/> keys (WO-94).
        /// <list type="bullet">
        ///   <item>hollow-warrior  → "hollow-warrior" (standard melee — AccuRig warrior)</item>
        ///   <item>hollow-rogue    → "skeleton"     (fast skirmisher — closest grunt)</item>
        ///   <item>hollow-walker   → "skeleton"     (basic grunt)</item>
        ///   <item>necromancer     → "necromancer"  (exact match)</item>
        ///   <item>anything else   → "skeleton"     (safe fallback)</item>
        /// </list>
        /// </summary>
        public string EngineDefId
        {
            get
            {
                switch (_enemyDefId)
                {
                    case "necromancer":   return "necromancer";
                    case "hollow-warrior": return "hollow-warrior";
                    // hollow-walker and hollow-rogue both map to the standard grunt.
                    default:              return "skeleton";
                }
            }
        }

        // ---------------------------------------------------------------------
        // Configuration — called by WaveManager right after Instantiate
        // ---------------------------------------------------------------------

        /// <summary>
        /// Wires this enemy from its stat block and the scene context. Called by
        /// <see cref="WaveManager"/> immediately after instantiation.
        /// </summary>
        /// <param name="enemyId">Stable per-instance id (the breach-roster key).</param>
        /// <param name="def">The deserialised <c>enemies.json</c> stat block.</param>
        /// <param name="heart">The Heart transform — the enemy's march goal.</param>
        public void Configure(string enemyId, EnemyDef def, Transform heart)
        {
            _enemyId = enemyId;
            _heart = heart;
            _def = def;

            // WO-889: attach the persistent species-aura driver here, because Configure is
            // the ONE place every spawn path (wave / roamer / tribe / arena) sets the stat
            // block - the same reasoning SpeciesDeathVfx gives for reading _def at all.
            // The component is inert (no loop, no registration) for an archetype whose
            // SpeciesAuraVfx is None, so attaching it to every enemy costs nothing.
            EnemyAuraVFX.Ensure(gameObject);

            // Per-type cue set, resolved from the stat block's FAMILY. Configure is the
            // one place every spawn path (wave / roamer / tribe / arena) sets _def, and
            // it is also the pooled-reuse entry point, so a recycled body re-resolves
            // exactly as a fresh one does. Non-null by contract - see EnsureTypeVfxSet.
            EnsureTypeVfxSet(def);

            if (def != null)
            {
                _enemyDefId = def.Id;
                _maxHp = Mathf.Max(1f, def.Hp);
                _hp = _maxHp;
                // ⚠ WO-1232: this is HP/25 and nothing more, and NOTHING PLAYER-FACING READS IT any
                // more (owner ruling 2026-08-26 — the numeric enemy level is removed from the HUD;
                // the target frame shows the authored Tier word). It stays off def.Hp (pre-scaling)
                // rather than the runtime maxHp only so the diagnostic magnitude does not also creep
                // with wave scaling. Do NOT re-point a display at it; see Enemy.Level's remarks.
                _level = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1f, def.Hp) / 25f));
                // Owner 2026-06-02: global -5% enemy speed — early-game generosity so new
                // players get the "winning while I learn" feel as they scale up + learn the
                // movement. One central dial; every enemy (roamer/wave/tribe) routes through
                // Configure, so all of them slow together.
                _moveSpeed = Mathf.Max(0.1f, def.MoveSpeed) * 0.95f;
                _contactDamage = Mathf.Max(0f, def.ContactDamage);
                _attackInterval = Mathf.Max(0.1f, def.AttackInterval);
                _ai = def.AiKind;
                // Air/ground targeting matrix — flyers can only be hit by anti-air
                // (or "both") towers. Sourced from the enemies.json "movement" field.
                _isFlying = def.IsFlying;
                // WO-397: honour the per-def aggro radius. EnemyDef.AggroRadius was
                // authored on every roster (enemies.json + the code-built outpost /
                // camp defs: 14 m guards, 16 m boss) but was NEVER applied — the field
                // silently fell back to the 7 m inspector default, so a guard with an
                // intended 14 m aggro only woke at 7 m. Map it here (clamped to the
                // field's 0–20 inspector range) so the data dial actually governs how
                // far an enemy detects the hero. A def value <= 0 keeps the prefab/
                // inspector default (legacy-safe: a hand-placed enemy with no def, or a
                // def that deliberately opts out of hero aggro, is untouched).
                if (def.AggroRadius > 0f)
                    _heroAggroRadius = Mathf.Clamp(def.AggroRadius, 0f, 20f);
            }

            // DPS-MAGE (owner 2026-06-13): caster-role enemies (orc-shaman, hollow-acolyte,
            // orc-necromancer, future overboss) should HOLD DISTANCE and fire ranged, not charge.
            // Spawned enemies carry no inspector TacticalData, so everyone defaulted to Rush (even
            // casters). Assign the shared runtime Kiter archetype → EnemyBrain enters the Kite
            // state (standoff band ~10 m, back off at 6 m) and fires Enemy.RangedAttack on
            // cooldown. The spawner wires the EnemyBrain before Configure, so it's present.
            if (def != null && def.Role == "caster")
            {
                var brain = GetComponent<EnemyBrain>();
                if (brain != null) brain.SetTactics(EnemyBrain.KiterTactics);
            }

            EnsureAgent();
            if (_agent != null)
            {
                _agent.speed            = _moveSpeed;
                _agent.stoppingDistance = _heartArrivalRadius;
                // DEF-56: disable Unity's automatic re-path so Enemy.DriveNav()'s
                // throttle is the sole trigger for SetDestination calls. Without
                // this the agent silently re-paths on NavMesh topology changes,
                // partially defeating the ~80% CPU saving the throttle provides.
                _agent.autoRepath = false;
                // DEF-56: randomise avoidance priority so swarm enemies don't all
                // push the same direction when crowding. Range 50–79 leaves 0–49
                // free for bosses / high-priority agents.
                _agent.avoidancePriority = UnityEngine.Random.Range(50, 80);
            }

            _dead = false;
            _attackCooldown = 0f;
            _navWarned = false;
            // Re-arm the in-scene battle-lock brain resolve for pooled reuse (the spawner adds the
            // EnemyBrain AFTER Configure, so this resolves lazily on the first Update post-spawn).
            _engageBrainResolved = false;
            _engageBrain = null;

            // Animation + turning fixes for core TD battle characters.
            // Attach guarded driver (so Enemy.cs can call SetLocomotion/PlayAttack/Die
            // and the HeroAnimatorFactory-style or Enemy shared controllers play).
            if (!TryGetComponent(out ActorAnimator actor)) actor = gameObject.AddComponent<ActorAnimator>();
            _actor = actor;

            // WO-VFX-WEAPON-TRAILS: shared blade-trail flash on every enemy swing (owner: "both hero
            // and enemy"; enemies share the rig + ActorAnimator). Self-drives off AttackStarted; catches
            // enemies not built via EnemyFactory (e.g. WaveManager fallback). Safe re-add (pooled reuse).
            if (GetComponent<WeaponTrailController>() == null) gameObject.AddComponent<WeaponTrailController>();

            if (_agent != null)
            {
                _agent.updateRotation = false; // we control facing (to target on attack, or path dir)
            }

            // DEF-56: reset path throttle and stagger the initial SetDestination
            // call through NavPathCoordinator so a 20-enemy spawn doesn't spike
            // NavMesh pathing in a single frame.
            _pathRefreshTimer      = 0f;
            _lastPathedDestination = Vector3.zero;
            if (_agent != null && _heart != null)
                NavPathCoordinator.RequestInitialPath(_agent, _heart.position);

            // EnemyAggro observability (no-brain hypothesis): record at init whether this
            // enemy carries an EnemyBrain (ranged tower/structure awareness) or is a plain
            // wave/roamer relying ONLY on the narrow forward contact probe. The spawner wires
            // the brain before Configure, so GetComponent here is authoritative.
            var aggroBrain = GetComponent<EnemyBrain>();
            DeNelle.Core.Diagnostics.FlowTrace.Once("EnemyAggro", $"brain-{_enemyId}",
                $"{_enemyId}: init hasEnemyBrain={(aggroBrain != null)} " +
                $"role={(def != null ? def.Role : "<no-def>")} ai={_ai} " +
                $"(no brain => structure awareness = forward ProbeForStructure only)");

            // WO-893: arm the ARRIVAL tell. Configure is the ONE place every spawn path
            // sets the stat block (the same reasoning SpeciesDeathVfx and SpeciesAuraVfx
            // give for reading _def here), and it is also the pooled-reuse entry point, so
            // a recycled enemy re-announces itself exactly as a fresh one does.
            _spawnTellPending = true;

            // WO-874 - THE ATTACH. Same reasoning as the line above, which is why it sits
            // here and not in Awake: _def is the only tier signal the pool/factory spawn
            // path sets, and it is set by Configure.
            EnsureEliteVfx();
        }

        // ---------------------------------------------------------------------
        // WO-874 - ELITE / BOSS VFX: THE COMPONENT IS ATTACHED, NOT SHORTCUT
        // ---------------------------------------------------------------------
        //
        // OWNER RULING 2026-08-04, RECONFIRMED VERBATIM 2026-08-21 ("874 wire ruling
        // stands"): WIRE EliteVFXController - do not kill it, and do not deliver its
        // effect some other way.
        //
        // ⛔ THE FAILURE MODE THIS REPLACES ALREADY HAPPENED ONCE. Commit 4c1da079
        //    promoted SpawnVfxFor / PlayDeathShake to STATICS and called them from this
        //    file instead of attaching the component. That delivered the spawn tell and
        //    the tiered kill shake - so the ticket READ as progressed - while
        //    AddComponent<EliteVFXController> stayed at zero hits repo-wide and the two
        //    behaviours the component alone owns, the PULSING AURA and OnEliteAttack, had
        //    still never run in the shipped game. Routing around a ruling with no reversal
        //    recorded is the shape; the fix is the attach itself.
        //
        // WHY A COMPONENT AND NOT MORE STATICS: the aura is STATEFUL and PER-BODY - a
        // sine over a cached base light intensity, running for as long as that enemy
        // lives. A static cannot hold that, and OnEliteAttack likewise needs the instance
        // to know its own tier at the moment the blow lands.
        //
        // POOL-SAFE BY CONSTRUCTION: AddComponent runs at most ONCE per pooled body (the
        // GetComponent below adopts it on every later reuse), and ArmForTier re-arms it
        // for the life that is starting - stopping whatever the previous life left
        // running, so a body reused a hundred times carries one aura coroutine, not a
        // hundred. A body re-Configured to a PLAIN tier keeps the component but is armed
        // with both flags false, which stands its routines down.

        /// <summary>Cached for the frame-rate paths (attack/death); re-resolved on Configure.</summary>
        private EliteVFXController _eliteVfx;

        /// <summary>
        /// WO-874: attach + arm <see cref="EliteVFXController"/> when this enemy's
        /// enemies.json stat block reads elite or boss. Returns the component, or null for
        /// a plain-tier enemy that has never been an elite (nothing is attached
        /// speculatively - a trash mob must not carry a component it will never use).
        /// </summary>
        private EliteVFXController EnsureEliteVfx()
        {
            bool boss  = IsBossTier();
            bool elite = IsEliteTier();

            _eliteVfx = GetComponent<EliteVFXController>();

            if (_eliteVfx == null)
            {
                if (!boss && !elite) return null;   // plain tier: nothing to attach.
                _eliteVfx = gameObject.AddComponent<EliteVFXController>();
                DeNelle.Core.Diagnostics.FlowTrace.Step("EliteVFX",
                    $"attached EliteVFXController to '{_enemyId}' (boss={boss} elite={elite}) - " +
                    "WO-874 wire ruling; aura + OnEliteAttack now have an owner on this body.");
            }

            _eliteVfx.ArmForTier(boss, elite);

            // The component now owns the arrival tell for this tier: its DramaticSpawnRoutine
            // plays EXACTLY the type FireSpawnTell would have played (both go through
            // EliteVFXController.SpawnVfxFor) and fires the same tier shake, but after the
            // authored dramatic delay. Leaving both armed would double the burst and the
            // shake on the same spawn - ONE owner, and for an elite it is the component.
            if (boss || elite) _spawnTellPending = false;

            return _eliteVfx;
        }

        // ---------------------------------------------------------------------
        // WO-893 - THE ARRIVAL TELL ("mobs no longer pop from nothing")
        // ---------------------------------------------------------------------
        //
        // THE GAP, verified at source: a standard enemy had NO spawn VFX whatsoever. The
        // only spawn tell in the codebase was EliteVFXController.DramaticSpawnRoutine - and
        // WO-886 already established, by grepping every .prefab/.unity/.asset in the tree,
        // that EliteVFXController is attached to NOTHING, so Elite_Spawn and Boss_Spawn had
        // never played either. All three tiers arrived in silence.
        //
        // ⚠ THAT LAST CLAUSE IS NO LONGER TRUE, and the difference is load-bearing here.
        //   WO-874 (owner ruling, reconfirmed 2026-08-21) attaches EliteVFXController on
        //   the elite/boss spawn path, so for those two tiers the component's
        //   DramaticSpawnRoutine IS the arrival tell - the same VFXType (both sides call
        //   SpawnVfxFor) and the same tier shake, after the authored dramatic delay.
        //   EnsureEliteVfx therefore CLEARS _spawnTellPending for boss and elite, and this
        //   method is now the STANDARD tier's tell only. Two owners would mean two bursts
        //   and two shakes on one spawn.
        //
        // The fix mirrors WO-886's exactly rather than inventing a second pattern: the tier
        // rule lives in ONE place (EliteVFXController.SpawnVfxFor, beside the death rule it
        // matches), Enemy drives it off its enemies.json stat block, and a hand-placed
        // prefab that DOES carry EliteVFXController still behaves identically.
        //
        // Enemy_Spawn has no catalogued prefab on purpose (the ratified Respawn recipe is a
        // SCRIPTED pack effect carrying a demo mesh and a pack MonoBehaviour - see
        // ParticlePackVfxBatchBuilder.DeferredTypes for the re-measurement), so it resolves
        // through VFXManager's name-driven procedural fallback today and upgrades for free
        // the day the material-cutoff component is authored. No call site changes then.

        /// <summary>Set by <see cref="Configure"/>; consumed by the first <c>Update</c>.</summary>
        private bool _spawnTellPending;

        /// <summary>
        /// Play this enemy's arrival burst once, at its FINAL spawn position. Family B
        /// one-shot for every tier, so an arrival can never consume one of the 20 global
        /// loop slots - a wave arrives by the dozen.
        /// </summary>
        private void FireSpawnTell()
        {
            _spawnTellPending = false;

            VFXType tell = EliteVFXController.SpawnVfxFor(IsBossTier(), IsEliteTier());
            if (tell == VFXType.None) return;

            VFXManager.Play(tell, transform.position);

            // Shake ONLY for the two tiers that are a warning. A standard spawn happens
            // constantly, and a camera that shakes on every trash mob is noise, not signal.
            if (IsBossTier())       CameraShakeBridge.Shake(0.5f, 0.5f);
            else if (IsEliteTier()) CameraShakeBridge.Shake(0.25f, 0.3f);

            DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemySpawn", "spawn-tell", 2f,
                $"arrival tell '{tell}' for '{_enemyId}' (boss={IsBossTier()}, elite={IsEliteTier()}) " +
                "at the final spawn position.");
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        /// <summary>
        /// Applies wave-scaling multipliers after <see cref="Configure"/>. Called by
        /// <see cref="WaveManager"/> immediately after Configure when a
        /// <see cref="WaveScalingCurve"/> is assigned. Multipliers of 1 are no-ops.
        /// </summary>
        public void ApplyWaveScaling(float hpMult, float speedMult, float damageMult)
        {
            if (hpMult > 1f)
            {
                _maxHp = Mathf.Max(1f, _maxHp * hpMult);
                _hp    = _maxHp;
            }
            if (speedMult != 1f)
            {
                _moveSpeed = Mathf.Max(0.1f, _moveSpeed * speedMult);
                if (_agent != null && _agent.isOnNavMesh)
                    _agent.speed = _moveSpeed;
                else if (_agent != null)
                    _agent.speed = _moveSpeed;
            }
            if (damageMult > 1f)
            {
                _contactDamage = Mathf.Max(0f, _contactDamage * damageMult);
            }
        }

        /// <summary>
        /// WO-789: pins max/current HP to an exact authored value (waves.json bossHp),
        /// REPLACING whatever <see cref="Configure"/> + wave scaling produced — a
        /// deliberate, visible wave-level exception to the enemies.json stat SSOT
        /// (mirrors apexBoss.hp). Called AFTER ApplyWaveScaling so the pin wins.
        /// No-op for values &lt;= 0. Pool-safe: Configure re-seeds _maxHp from the def on
        /// every reuse, so a pin never leaks into a later normal spawn.
        /// </summary>
        public void OverrideMaxHp(float maxHp)
        {
            if (maxHp <= 0f) return;
            _maxHp = Mathf.Max(1f, maxHp);
            _hp    = _maxHp;
        }

        /// <summary>
        /// Captures the stats <see cref="ApplyDifficulty"/> multiplies FROM, for THIS spawn.
        /// Called by the spawner immediately after <see cref="ApplyWaveScaling"/> (and again,
        /// with the pinned HP, at the boss-HP-pin site) so the dynamic-difficulty multiplier
        /// always lands on a freshly captured base rather than on an already-scaled value.
        /// See the pooling warning block above the base fields -- this method is the whole
        /// reason the multiplier cannot compound across pooled reuses.
        /// </summary>
        /// <param name="maxHp">The base max HP (&lt;= 0 leaves the HP base uncaptured).</param>
        /// <param name="contactDamage">The base contact damage (&lt; 0 leaves it uncaptured; 0 is valid).</param>
        public void SetBaseStats(float maxHp, float contactDamage)
        {
            _baseMaxHp         = maxHp > 0f && !float.IsNaN(maxHp) && !float.IsInfinity(maxHp) ? maxHp : -1f;
            _baseContactDamage = contactDamage >= 0f && !float.IsNaN(contactDamage) && !float.IsInfinity(contactDamage)
                               ? contactDamage : -1f;
        }

        /// <summary>
        /// Applies the dynamic-difficulty multipliers as <c>base * mult</c> -- NEVER
        /// <c>current *= mult</c>. No-op for a stat whose base was not captured this spawn.
        /// <para>
        /// DELIBERATELY UNGATED. <see cref="ApplyWaveScaling"/> gates on
        /// <c>if (hpMult &gt; 1f)</c> / <c>if (damageMult &gt; 1f)</c>, which silently discards
        /// every multiplier BELOW 1.0 -- if dynamic difficulty were routed through that method
        /// the entire "make it easier for a struggling player" half of the feature would be
        /// dead code. This method has no such gate: a multiplier of 0.80 makes the enemy
        /// weaker, exactly as authored.
        /// </para>
        /// </summary>
        public void ApplyDifficulty(float hpMult, float damageMult)
        {
            if (_baseMaxHp > 0f && hpMult > 0f && !float.IsNaN(hpMult) && !float.IsInfinity(hpMult))
            {
                _maxHp = Mathf.Max(1f, _baseMaxHp * hpMult);
                _hp    = _maxHp;
            }

            if (_baseContactDamage >= 0f && damageMult >= 0f && !float.IsNaN(damageMult) && !float.IsInfinity(damageMult))
            {
                _contactDamage = Mathf.Max(0f, _baseContactDamage * damageMult);
            }
        }

        private void Awake()
        {
            EnsureAgent();
            EnsureAnimator();
            EnsureAudio();
            // Per-type cue floor. Latch an AUTHORED prefab reference first (it wins
            // forever), then resolve the library floor so even an enemy that never
            // reaches Configure (hand-placed, family test spawner) has a telegraph.
            _typeVfxSetAuthored = _typeVfxSet != null;
            EnsureTypeVfxSet(null);
            EnsureHitReaction();
            EnsureHealthBar();
            // POOL-RESET AUDIT: snapshot the authored hero-aggro radius BEFORE any def
            // overlay runs, so ResetForPool can restore it (see the field comment).
            if (_authoredHeroAggroRadius < 0f) _authoredHeroAggroRadius = _heroAggroRadius;
        }

        /// <summary>
        /// Cached sibling <see cref="EnemyBrain"/> (may legitimately be null — plain wave
        /// roamers carry none). Resolved once; the pool reset path calls this every
        /// release/acquire, so it must not GetComponent per call.
        /// </summary>
        private EnemyBrain ResolveBrain()
        {
            // Only LATCH once a brain is actually found: SmartEnemySpawner/WaveManager add the
            // EnemyBrain AFTER Configure, so a latch-on-null would blind this body to a brain
            // that appears later in the same spawn.
            if (_brainResolved && _brain != null) return _brain;
            _brain = GetComponent<EnemyBrain>();
            _brainResolved = _brain != null;
            return _brain;
        }

        // Registry membership (TargetManager): every enemy — wave, roamer, tribe,
        // ward — auto-joins on enable and leaves on disable, so the reticle/towers
        // query a clean live list instead of an overflow-prone physics sweep. Covers
        // pooling too (re-enable re-registers; Register dedups).
        private void OnEnable()  => TargetManager.Register(this);
        private void OnDisable()
        {
            TargetManager.Unregister(this);
            ReleaseAttackToken();
            _contactCommitPending = false;
            _contactCommitInterrupted = false;
            // Release the in-scene battle-lock membership so a despawned/pooled/destroyed enemy
            // can never wedge BattleLock.IsInBattle() true (a stale token would keep combat input
            // locked in town). OnDisable runs before OnDestroy and on pool release — covers all exits.
            if (_engagedLatched)
            {
                _engagedLatched = false;
                DeNelle.Core.Combat.HeroCombatEngagement.SetEngaged(this, false);
            }

            // =============================================================================
            // WO-1337 — THE SECOND BATTLE-LOCK CLAIM THIS BODY RAISES, RELEASED THE SAME WAY.
            // -----------------------------------------------------------------------------
            // The block directly above releases this enemy's HeroCombatEngagement token on
            // EVERY exit, and its own comment states the invariant: "a despawned/pooled/
            // destroyed enemy can never wedge BattleLock.IsInBattle() true". But an enemy
            // raises the battle-lock through TWO owners, not one — the engagement token, and
            // the PURSUIT PULSE it stamps every frame while chasing (DriveNav, ~line 1578),
            // which PursuitBattleProbe returns verbatim as a BattleLock probe. Only the first
            // was released here. The pursuit pulse was revoked in exactly ONE place — Die()
            // (~line 2986) — so an enemy removed WITHOUT DYING left a live pulse behind it for
            // PostureSignals.PursuitTtl (1.5 s) past its own destruction.
            //
            // ⛔ THE CAPTURED DEFECT (device SM02G4061955851, build 2026.09.03.353593,
            //    F8 seq 4677, scene Main_Castle_Overworld):
            //
            //      [Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (retreat) - 2 invariant(s) NOT
            //        restored after the battle:
            //        - battle-lock: still HELD after the battle ended. … HOLDER(S):
            //          PursuitBattleProbe.Probe (of 3 registered: PursuitBattleProbe.Probe,
            //          BattleArena.<Awake>b__84_0, WaveManager.<OnEnable>b__116_0).
            //
            // Read against the retreat path, the arithmetic is deterministic and uses only
            // constants that live in this tree:
            //   * BattleArena.Resolve announces the session end synchronously, and
            //     BattleSessionEnd.Release clears the whole pursuit ring there (t=0).
            //   * The arena's SURVIVORS are not torn down at t=0. Resolve captures them and
            //     hands them to ReturnHomeWithFade, which despawns them only AFTER
            //     HomeFadeOutSeconds = 0.35 s (BattleArena.cs:181-184, :2919-2922) — and it
            //     despawns them with Destroy(e.gameObject), a path that never reaches Die().
            //   * So every survivor keeps stamping ReportPursuit for 0.35 s past the clear,
            //     and its LAST pulse then stayed live to t = 0.35 + 1.5 = 1.85 s.
            //   * BattleQuiescenceGate judges a retreat at SettleSeconds = 0.75 s.
            //     0.75 < 1.85, every time — which is why WO-1233's own header already recorded
            //     that "the RETREAT case fails deterministically" while the win case (which
            //     waits out the reward screen) did not.
            //
            // This is the WO-1308 shape exactly, one door over: a release seam that existed
            // (RevokePursuit) and an owner that never reached for it on the path it mattered.
            // Fixing it HERE rather than in the arena's despawn loop is deliberate — the pulse
            // is keyed by this instance id, so this body is its only honest owner, and doing it
            // in OnDisable covers all three removal paths at once (Destroy, pool release, scene
            // unload) instead of adding an Nth per-caller release.
            //
            // ⚠ IT CANNOT SUPPRESS A REAL CHASE. Pursuit is PULSE-based: a live chaser re-stamps
            // on its next DriveNav tick, so revoking here only drops the pulse of a body that is
            // gone. A disabled enemy is not chasing anyone. Idempotent alongside Die()'s revoke.
            // =============================================================================
            DeNelle.Core.HudModel.PostureSignals.RevokePursuit(GetInstanceID());
        }

        private void EnsureHitReaction()
        {
            // Auto-attach so every enemy gets the hit-flash with zero prefab wiring.
            _hitReaction = GetComponent<EnemyHitReaction>();
            if (_hitReaction == null) _hitReaction = gameObject.AddComponent<EnemyHitReaction>();
        }

        /// <summary>
        /// WO-178: auto-attach the floating world-space HP bar (zero prefab wiring).
        /// Height is taken from the rendered mesh bounds so the bar clears the
        /// model regardless of import scale; it reads HpFraction / IsDead and hides
        /// itself until the enemy first takes damage.
        /// </summary>
        private void EnsureHealthBar()
        {
            float headOffset = 2.4f;
            Renderer rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                // Local-space height of the mesh top above this transform, plus a gap.
                float worldTop = rend.bounds.max.y - transform.position.y;
                if (worldTop > 0.1f) headOffset = worldTop + 0.4f;
            }
            // DEF-206: hideAtFull:true — enemies start with NO bar (the "raw green
            // bar on everything" noise the owner flagged). The bar reveals only once
            // the enemy is ENGAGED: it takes damage, OR HeroTargetIndicator flags it
            // as the player's current/locked target (FloatingHealthBar.SetTargeted),
            // then fades out a few seconds after combat ends. Full-HP idle mobs in
            // the distance now read clean.
            _healthBar = FloatingHealthBar.Attach(
                gameObject,
                fraction: () => HpFraction,
                isDead:   () => _dead,
                heightOffset: headOffset,
                hideAtFull: true);
        }

        private void EnsureAudio()
        {
            if (_audioSource == null)
                _audioSource = GetComponentInChildren<AudioSource>();
            // Don't add one automatically — AudioSource requires spatial settings
            // the integrator should configure (3D falloff, volume rolloff, etc.).
            // If null, PlayTypeSound is a no-op and the enemy runs silently until
            // an AudioSource is added to the prefab.
        }

        /// <summary>
        /// Guarantees <c>_typeVfxSet</c> is non-null (2026-08-16 combat-cue fix).
        ///
        /// The per-prefab assignment this field was designed around NEVER LANDED - the
        /// only EnemyTypeVfxSet asset's GUID appears nowhere but its own .meta, and the
        /// live enemies are not prefab instances at all (EnemyFactory builds them with
        /// AddComponent&lt;Enemy&gt;). Every telegraph / per-type sound / hit-VFX branch in
        /// this file therefore took its hardcoded fallback forever: no readable wind-up.
        ///
        /// Resolution is ADDRESS-based (<see cref="EnemyTypeVfxLibrary"/>, a Resources
        /// path), never a serialized edge, so it cannot silently un-assign again.
        /// Called twice: from <see cref="Awake"/> with no def (the floor), and from
        /// <see cref="Configure"/> once the stat block names a family (the upgrade).
        /// An AUTHORED prefab reference is never overwritten.
        /// </summary>
        private void EnsureTypeVfxSet(EnemyDef def)
        {
            if (_typeVfxSetAuthored) return;                                   // prefab art wins
            if (_typeVfxSet != null && !EnemyTypeVfxLibrary.IsLibrarySet(_typeVfxSet))
            {
                // Assigned after Awake by something other than this library - treat it
                // as authored from here on.
                _typeVfxSetAuthored = true;
                return;
            }

            EnemyTypeVfxSet resolved = EnemyTypeVfxLibrary.Resolve(def);
            if (resolved == null)
            {
                // EnemyTypeVfxLibrary.Resolve is contractually non-null; if that ever
                // changes, the enemy would silently lose its telegraph again. Say so.
                DeNelle.Core.Diagnostics.FlowTrace.Fail("EnemyVfx",
                    $"'{_enemyId}': EnemyTypeVfxLibrary.Resolve returned NULL for family " +
                    $"'{(def != null ? def.Family : "<no-def>")}' - this enemy has NO wind-up " +
                    "telegraph, no per-type sound and no hit VFX.");
                return;
            }

            _typeVfxSet = resolved;
        }

        /// <summary>The kind of combat beat a fallback SFX covers (WO-220).</summary>
        private enum CombatSfxFallback { None, Hit, Death }

        /// <summary>
        /// Plays a clip via the enemy's AudioSource (one-shot, non-interrupting).
        /// No-op when either the source or the clip is null.
        ///
        /// WO-220: when the type-set provided NO clip (clip == null) and a
        /// <paramref name="fallback"/> kind is given, play a generated fallback SFX
        /// through the EXISTING audio surface (CoreServices.Audio) so the enemy is
        /// never silent on a hit / death — even before any EnemyTypeVfxSet clips are
        /// authored. The type-set clip is always preferred when present.
        /// </summary>
        private void PlayTypeSound(AudioClip clip, CombatSfxFallback fallback = CombatSfxFallback.None)
        {
            if (clip != null)
            {
                if (_audioSource != null) _audioSource.PlayOneShot(clip);
                return;
            }

            // No authored clip — fill the gap via the central audio service.
            switch (fallback)
            {
                case CombatSfxFallback.Hit:   EnemyCombatAudio.PlayHit();   break;
                case CombatSfxFallback.Death: EnemyCombatAudio.PlayDeath(); break;
            }
        }

        private void Update()
        {
            if (_dead) return;

            // WO-893: the ARRIVAL tell. Deferred to the first Update after Configure rather
            // than fired inside it, because a spawner is free to seat/nudge the enemy after
            // configuring it (SmartEnemySpawner, EnemyGroupSpawner, WaveManager and the
            // tutorial all call Configure separately from placement) - one frame later the
            // transform is final for every path, so the burst cannot land at a stale
            // position. One bool test per frame per enemy.
            if (_spawnTellPending) FireSpawnTell();

            TickContactAttack();
            DriveNav();
            TickFacing();
            DriveAnimator();
            UpdateHeroCombatEngagement();
        }

        // ---------------------------------------------------------------------
        // In-scene battle-lock — let the hero ATTACK the in-place dungeon/outpost
        // hollows (2026-06-30 "0 damage in dungeon" root fix). See the fields block
        // above + DeNelle.Core.Combat.HeroCombatEngagement for the full RCA.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Raises/clears this enemy's membership in <see cref="DeNelle.Core.Combat.HeroCombatEngagement"/>
        /// (a <see cref="DeNelle.Core.Combat.BattleLock"/> source) so the hero's attack input is LIVE
        /// while an in-place hero-only duelist (dungeon/outpost hollow) is engaging her. Scoped to
        /// heart-less <see cref="EnemyBrain.HeroOnlyTarget"/> combatants and edge-triggered so it only
        /// touches the shared set on a change. Staged battles keep their own BattleLock probes; this
        /// only adds the previously-missing "in-place real-time fight" source.
        /// </summary>
        private void UpdateHeroCombatEngagement()
        {
            bool engaged = false;
            // Only heart-less duelists (the OutpostEnemyGroupSpawner hollows + arena orcs) can ever
            // hold this lock — a heart-siege wave enemy or a plain overworld roamer never does.
            if (!_dead && _heart == null)
            {
                if (!_engageBrainResolved) { _engageBrain = GetComponent<EnemyBrain>(); _engageBrainResolved = true; }
                if (_engageBrain != null && _engageBrain.HeroOnlyTarget)
                    engaged = IsHeroWithinAggro();   // hero inside this enemy's aggro band => a live fight
            }

            if (engaged != _engagedLatched)
            {
                _engagedLatched = engaged;
                DeNelle.Core.Combat.HeroCombatEngagement.SetEngaged(this, engaged);
                DeNelle.Core.Diagnostics.FlowTrace.Step("Combat",
                    $"{_enemyId}: hero-combat engagement -> {engaged} " +
                    $"(heart-less hero-only duelist; BattleLock.IsInBattle now={DeNelle.Core.Combat.BattleLock.IsInBattle()}, " +
                    $"engagedCount={DeNelle.Core.Combat.HeroCombatEngagement.EngagedCount}). " +
                    "engaged=True is what lets the hero's swings/casts fire in the dungeon/outpost.");
            }
        }

        // ---------------------------------------------------------------------
        // Facing — smooth pivot toward a requested target direction (anti-snap)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Records a desired facing direction for the smooth pivot. The vector is
        /// Y-flattened so the enemy turns upright only (never tips). Replaces the
        /// old instant <c>transform.rotation = LookRotation(...)</c> snap used by
        /// the target/attack facing — call this instead; the actual rotation is
        /// integrated frame-by-frame in <see cref="TickFacing"/>.
        /// </summary>
        private void RequestFacing(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude <= 0.0001f) return;
            _faceTargetDir = worldDir.normalized;
            _hasFaceTarget = true;
        }

        /// <summary>
        /// Slerps the root toward the requested facing direction each frame, at a
        /// natural ground-unit turn rate. Only drives when the agent is effectively
        /// stopped — while moving, the velocity path-facing (DriveNav, ~707) owns
        /// rotation and following travel is correct. Y-only/upright is guaranteed
        /// because <see cref="RequestFacing"/> flattens the direction. Observable via
        /// a throttled FlowTrace (~1/sec).
        /// </summary>
        private void TickFacing()
        {
            if (!_hasFaceTarget || _dead) return;

            // While the agent is genuinely moving, velocity-facing drives — defer.
            if (_agent != null && _agent.isOnNavMesh)
            {
                Vector3 v = _agent.velocity; v.y = 0f;
                if (v.sqrMagnitude > 0.1f * 0.1f) return;
            }

            Quaternion face = Quaternion.LookRotation(_faceTargetDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, face, FaceTurnSlerp * Time.deltaTime);

            float deg = Quaternion.Angle(transform.rotation, face);
            DeNelle.Core.Diagnostics.FlowTrace.Throttle(
                "EnemyFacing", $"turn-{_enemyId}", 1f,
                $"smooth-pivot id={_enemyId} remaining={deg:0.0}deg rate={FaceTurnSlerp}");

            // Settled — stop re-slerping a tiny residual (avoids endless micro-turn).
            if (deg < 0.5f) _hasFaceTarget = false;
        }

        // ---------------------------------------------------------------------
        // Animation — push the locomotion speed to the Animator each frame
        // ---------------------------------------------------------------------

        /// <summary>
        /// Feeds the Animator's <c>Speed</c> float from the agent's actual
        /// velocity so the controller blends idle &lt;-&gt; move. No-op when the
        /// enemy has no Animator (parameter sets are all null-guarded).
        /// </summary>
        private void DriveAnimator()
        {
            // Cheap in steady state; rescans only when a late-downloaded controller
            // has actually replaced the controller observed during Awake.
            EnsureAnimator();
            if (_animator == null || !_hasSpeedParam) return;
            float speed = (_agent != null && _agent.isOnNavMesh)
                ? _agent.velocity.magnitude
                : 0f;

            // Anti-slide fallback: NavMeshAgent velocity reads ~0 during throttled
            // SetDestination (DEF-56) + formation-slot drift, so estimate speed from
            // the horizontal transform delta when velocity is near-zero but we're
            // actually moving. Y is zeroed so a vertical settle never reads as motion.
            // Clamp to _moveSpeed so a teleport/warp (e.g. NavMesh respawn) can't spike
            // the blend into a sprint. Covers arena (formation) AND overworld (solo brain).
            if (speed < 0.05f && _hasLastAnimPos && Time.deltaTime > 0f)
            {
                Vector3 cur = transform.position;
                Vector3 d = cur - _lastAnimPos;
                d.y = 0f;
                float est = d.magnitude / Time.deltaTime;
                speed = Mathf.Min(est, Mathf.Max(0.1f, _moveSpeed));
            }
            _lastAnimPos = transform.position;
            _hasLastAnimPos = true;

            // ANTI-CHOP (2026-07-02): smooth the anim feed (~0.12s response) before it
            // drives the Speed float. The raw value jumps frame-to-frame (agent accel /
            // avoidance / the estimator above), and both enemy controller families read
            // Speed against hard bands (OrcHumanoid 1-D tree walk@1.5/run@3.5; the KayKit
            // HumanoidEnemy Idle<->Move gate at 0.1) — an undamped feed pops the blend.
            // Framerate-independent exponential smoothing; gameplay keeps the raw speed.
            _animSpeedSmoothed = Mathf.Lerp(_animSpeedSmoothed, speed,
                1f - Mathf.Exp(-Time.deltaTime / AnimSpeedDampSecs));
            float animSpeed = _animSpeedSmoothed < 0.02f ? 0f : _animSpeedSmoothed; // settle to true idle

            // Drive the (new) ActorAnimator for locomotion (idle/walk/run blendtree in the
            // shared enemy controllers). Also keep the legacy direct _animator.SetFloat
            // for any old listeners. Guarded inside ActorAnimator.
            _actor?.SetLocomotion(animSpeed);
            _animator.SetFloat(AnimSpeed, animSpeed);

            // WO-491: wounded stance below the low-HP cutoff — the orc reads "hurt" (limp /
            // stagger locomotion sub-tree). Flag-gated + guarded inside ActorAnimator
            // (no-op on controllers without an Injured param). Drives every frame; the
            // Animator only transitions on a change of the bool.
            if (DeNelle.Core.FeatureFlags.EnemyInjuredStance)
                _actor?.SetInjured(HpFraction < 0.3f);

            // Combat-stance locomotion (InCombat bool): braced idle + weapon gait while alert,
            // chasing, or stopped on a target. PresentationCombat covers overworld rep packs;
            // EnemyBrain covers wave/arena fighters.
            bool inCombat = _presentationCombat;
            if (!inCombat)
            {
                var brain = GetComponent<EnemyBrain>();
                if (brain != null && brain.enabled)
                    inCombat = brain.WantsCombatPresentation;
                else if (speed < 0.1f && _currentTarget != null)
                    inCombat = true;
            }
            _actor?.SetCombatStance(inCombat);

            // FOOT-SKATE MEASURE (owner 2026-07-04, gates the KnightMocap builder) — mirrors
            // HeroLoco: emit each ACTIVE locomotion clip's name + blend weight + authored length
            // beside the enemy's ACTUAL travel speed (agent velocity magnitude, `speed` above) and
            // the smoothed anim feed. The AUTHORED stride m/s side comes from AnimClipSpeedDump; the
            // gap = foot-skate. GetCurrentAnimatorClipInfo allocates, so the whole block is gated on
            // FlowTrace.Enabled -> zero cost when tracing is off.
            if (_animator != null && DeNelle.Core.Diagnostics.FlowTrace.Enabled)
            {
                var st = _animator.GetCurrentAnimatorStateInfo(0);
                var clips = _animator.GetCurrentAnimatorClipInfo(0);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < clips.Length; i++)
                {
                    var ci = clips[i];
                    if (ci.clip == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"{ci.clip.name}(w={ci.weight:F2},len={ci.clip.length:F2}s)");
                }
                if (sb.Length == 0) sb.Append("<none>");
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyLoco", $"loco-{_enemyId}", 1f,
                    $"vel={speed:F2} m/s | animSpeed={animSpeed:F2} | clips=[{sb}] | " +
                    $"baseState hash={st.shortNameHash} nt={st.normalizedTime % 1f:F2} | " +
                    $"controller={(_animator.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "<null>")}");
            }
        }

        // ---------------------------------------------------------------------
        // Navigation — march toward the Heart
        // ---------------------------------------------------------------------

        /// <summary>
        /// Steers the agent toward the Heart. While the enemy is locked onto a
        /// structure (contact attack) the agent is held in place. Logs ONCE if
        /// the agent is not on a baked NavMesh — the village scene needs baking.
        /// </summary>
        private void DriveNav()
        {
            if (_agent == null) return;

            // F8-38 money gate: DriveNav runs EVERY frame with NO _casting awareness. RootedCast sets
            // agent.isStopped=true, but unless a live contact-target lock holds it (the branch below),
            // this method un-stops + re-paths within the same frame -> the caster WALKS while channeling.
            // This trace PROVES it: velocity>0 (or isStopped flips to False) while _casting == the bug.
            if (_casting)
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyCast", $"drivenav-casting-{_enemyId}", 0.5f,
                    $"{_enemyId}: DriveNav TICK mid-cast — " +
                    $"isStopped={((_agent.isOnNavMesh) ? _agent.isStopped.ToString() : "n/a")} " +
                    $"vel={((_agent.isOnNavMesh) ? _agent.velocity.magnitude.ToString("F2") : "n/a")}m/s " +
                    $"contactLock={(_currentTarget != null && _currentTarget.IsAlive)} " +
                    "(DriveNav has NO _casting guard -> it may override the cast root this frame)");

            // F8-38 FIX: while a RootedCast is channeling, HOLD position -- do NOT re-issue movement
            // or clear isStopped. Without this guard the per-frame nav re-path (heartless branch and
            // the heart-march path below) un-stops the agent + re-SetDestination within the same frame,
            // overriding the cast root so the caster WALKS while channeling. Respect the existing
            // _casting state consistently: keep the agent stopped on the NavMesh and return.
            if (_casting)
            {
                if (_agent.isOnNavMesh && !_agent.isStopped) _agent.isStopped = true;
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyCast", $"drivenav-hold-{_enemyId}", 0.5f,
                    $"{_enemyId}: DriveNav HOLD - cast root respected, movement not re-issued this frame");
                return;
            }

            // HEARTLESS HOOK (overworld encounter rep) — DATA-PROVEN root of "no chase" 2026-06-23:
            // a rep has NO Heart (Configure(...,null)), so the old `_heart == null` bail returned
            // IMMEDIATELY and the agent was NEVER driven -> the rep stood still (no roam, no chase)
            // despite RepEngageWatcher setting _brainPositionOverride every frame. Drive a heartless
            // agent straight to its override (roam point while idle, hero while aggro'd) and RETURN,
            // never touching the Heart-based logic below (zero risk to normal Heart-driven enemies).
            if (_heart == null)
            {
                if (_agent.isOnNavMesh && _brainPositionOverride.HasValue)
                {
                    if (_agent.isStopped) _agent.isStopped = false;
                    Vector3 hv = _agent.velocity; hv.y = 0f;
                    if (hv.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(hv.normalized), 10f * Time.deltaTime);
                    _agent.SetDestination(_brainPositionOverride.Value);
                }
                else if (_agent.isOnNavMesh && !_agent.isStopped) _agent.isStopped = true;
                return;
            }

            // Locked onto a structure — stand and fight, do not path past it.
            if (_currentTarget != null && _currentTarget.IsAlive)
            {
                if (_agent.isOnNavMesh && !_agent.isStopped) _agent.isStopped = true;
                return;
            }

            if (!_agent.isOnNavMesh)
            {
                if (!_navWarned)
                {
                    Debug.LogWarning(
                        $"[Enemy:{_enemyId}] NavMeshAgent is not on a baked NavMesh — " +
                        "the enemy cannot move. The village scene needs NavMesh baking " +
                        "(see docs/port-notes/week4-waves.md).");
                    _navWarned = true;
                }
                return;
            }

            if (_agent.isStopped) _agent.isStopped = false;

            // WO-315: face the travel direction. _agent.updateRotation is OFF (so
            // RangedAttack / contact facing can override), but with no driver the
            // enemy kept its spawn orientation and "walked backwards". We are here
            // only when NOT locked to a contact target (that path returns above), so
            // slerp the root toward flattened agent velocity. Guard near-zero velocity
            // so a stopped/arriving enemy doesn't jitter. Mirrors HeroLocomotion's
            // root-facing (LookRotation on velocity, no extra Euler — the visual child's
            // rig-forward correction is applied at skin time in EnemyFactory).
            Vector3 vel = _agent.velocity; vel.y = 0f;
            if (vel.sqrMagnitude > 0.1f * 0.1f)
            {
                Quaternion face = Quaternion.LookRotation(vel.normalized);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, face, 10f * Time.deltaTime);
            }

            // DEF-72 / DEF-21 / DEF-224 / WO-397: resolve the nav destination in
            // priority order:
            //   1. _brainPositionOverride  — tactical Vector3 (flank, retreat, etc.)
            //                                A LIVE EnemyBrain drives this EVERY frame
            //                                (Rush returns target.position), so a
            //                                brain-steered enemy is decided here and
            //                                never reaches the steps below.
            //   2. hero aggro              — DEF-224: chase the hero when it is in
            //                                range. WO-397: moved ABOVE the static
            //                                _brainTarget tether. Brain-less enemies
            //                                that carry only a STATIC tether transform
            //                                (EnemyOutpost garrison guards tethered to
            //                                their stand-ring anchor) previously stood
            //                                idle on the anchor at point-blank because
            //                                the anchor (step 3) shadowed hero aggro —
            //                                the "brute idle at melee range" P1. Hero
            //                                aggro now wins over a static tether, so a
            //                                tethered guard breaks off to fight the hero
            //                                when she closes, then (hysteresis) returns
            //                                to its tether when she leaves. A live brain
            //                                is unaffected (it decided at step 1).
            //   3. _brainTarget Transform  — role/tether target (Tank/Healer/outpost
            //                                anchor / roam anchor) when the hero is out
            //                                of aggro range.
            //   4. _heart                  — default Heart-march.
            Vector3 destPos;
            bool chasingHero = false;
            // WO-1603: WHICH of the two chase paths set the flag. The pursuit pulse is stamped
            // from ONE site below but reached from TWO branches with DIFFERENT guarantees (the
            // hero-aggro branch refuses a dead hero, the brain-override branch cannot see one),
            // and a capture that cannot tell them apart cannot name the pulser. Tagged, not
            // logged: the tag rides the ring and is rendered on demand.
            string chaseVia = null;
            if (_brainPositionOverride.HasValue)
            {
                destPos = _brainPositionOverride.Value;
                // A LIVE EnemyBrain steers here (provoke/taunt/role-on-hero). Detect a
                // hero chase so we still close to melee range and keep the TIGHT
                // stoppingDistance (HeroChaseStoppingDistance) — otherwise the agent
                // parks at the 2.5 m siege radius, a metre OUTSIDE HeroHealth's 1.5 m
                // contact ring, and never lands a hit (the "enemies won't engage" RCA,
                // EnemyAggro 2026-06-18). ResolveHeroTransform is throttled (≈1/sec).
                ResolveHeroTransform();
                if (_heroTransform != null)
                {
                    // (a) the override sits ON the hero (brain Rush — complete path), OR
                    // (b) the ENEMY itself is near the hero AND the override points
                    //     hero-ward (brain Rush whose path went PARTIAL now steers to the
                    //     last reachable corner short of the hero — EnemyBrain.TryGetPartial-
                    //     Approach). Either way we are converging on the hero and must hold
                    //     the melee stopping distance to enter the damage ring.
                    Vector3 heroPlanar = Vector3.ProjectOnPlane(
                        _heroTransform.position - destPos, Vector3.up);
                    bool overrideOnHero = heroPlanar.sqrMagnitude <= 1.5f * 1.5f;

                    Vector3 selfToHero = Vector3.ProjectOnPlane(
                        _heroTransform.position - transform.position, Vector3.up);
                    Vector3 selfToDest = Vector3.ProjectOnPlane(
                        destPos - transform.position, Vector3.up);
                    bool nearHero = selfToHero.sqrMagnitude <= HeroChaseProximity * HeroChaseProximity;
                    bool destHeroward =
                        selfToHero.sqrMagnitude < 0.01f ||
                        selfToDest.sqrMagnitude < 0.01f ||
                        Vector3.Dot(selfToHero.normalized, selfToDest.normalized) > 0.5f;

                    chasingHero = overrideOnHero || (nearHero && destHeroward);
                    if (chasingHero) chaseVia = "Enemy.DriveNav/brain";
                }
            }
            else if (TryGetHeroAggroDestination(out Vector3 heroDest))
            {
                destPos = heroDest;
                chasingHero = true;
                chaseVia = "Enemy.DriveNav/aggro";
            }
            else if (_brainTarget != null && _brainTarget.gameObject.activeInHierarchy)
            {
                destPos = _brainTarget.position;
            }
            else
            {
                destPos = _heart.position;
            }

            // WO-419: tighten stoppingDistance to a melee value while chasing the hero so the
            // agent closes INSIDE HeroHealth's 1.5 m engage ring (the hero has no collider /
            // is not hit by the forward contact probe — see HeroChaseStoppingDistance). Restore
            // the siege arrival radius the moment the chase ends. Only writes on a change.
            if (chasingHero != _stopTightenedForHero)
            {
                _stopTightenedForHero = chasingHero;
                _agent.stoppingDistance = chasingHero ? HeroChaseStoppingDistance : _heartArrivalRadius;
                // Force the next frame to re-issue the path so the agent acts on the new
                // stoppingDistance immediately (an enemy already halted at 2.5 m must resume
                // closing to 1.1 m even though the destination hasn't moved).
                _pathRefreshTimer = 0f;
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"stop-{_enemyId}", 2f,
                    $"{_enemyId}: hero-chase={chasingHero} -> stoppingDistance={_agent.stoppingDistance:F2} " +
                    $"(scene='{gameObject.scene.name}')");
            }

            // WO-419: per-enemy closest-approach trace while chasing — shows in a headless run
            // whether a hero-aggro'd enemy actually reaches the hero's 1.5 m damage ring (where
            // HeroHealth.Update applies the contact tick) or stalls short. Throttled ~1/sec.
            if (chasingHero && _heroTransform != null)
            {
                float planar = Vector3.ProjectOnPlane(
                    _heroTransform.position - transform.position, Vector3.up).magnitude;
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"reach-{_enemyId}", 1f,
                    $"{_enemyId}: chasing hero, planarDist={planar:F2}m (engageRing=1.50m, " +
                    $"inRange={(planar <= 1.5f)})");
            }

            // Owner F8 (2026-07-04): surface THIS enemy's hero-pursuit to the HUD posture arc
            // so the combat bar (potion + heal/ability row + health) shows while the hero is
            // being chased in the OVERWORLD — not only for RegionMobSpawner roamers (previously
            // the sole ReportPursuit producer) or inside a staged arena battle. `chasingHero`
            // covers BOTH the brain-driven (override-on-hero) and brain-less (hero-aggro
            // destination) chase paths, so a stronghold/garrison/seam pursuer now drives the
            // A4.5 engagement window too. The report self-expires after PostureSignals.PursuitTtl
            // (1.5 s) once the chase ends by ANY path (leash / death / despawn / out-of-range),
            // giving a built-in linger so the prebattle posture never flickers or sticks on.
            //
            // =================================================================================
            // ⛔ WO-1603 — A BODY STANDING OVER A CORPSE IS NOT PURSUING THE PLAYER.
            // ---------------------------------------------------------------------------------
            // THE ASYMMETRY, INSIDE THIS ONE METHOD, IS THE DEFECT. The hero-aggro branch that
            // sets chaseVia="Enemy.DriveNav/aggro" already refuses a dead hero at source —
            // TryGetHeroAggroDestination: "The hero may have died (HeroHealth.IsAlive false) —
            // don't chase a downed/invulnerable hero" (Enemy.cs, ~:1731). The BRAIN-OVERRIDE
            // branch has no such test and cannot acquire one from here: EnemyBrain scores the
            // hero as a candidate on `!= null && activeInHierarchy` with NO IsAlive gate
            // (EnemyBrain.ConsiderCandidate(_heroTransform, …, HeroHpFraction(), …) at
            // EnemyBrain.cs:1596, FindHighestThreatTarget at :1604-1612, and the _heroOnlyTarget
            // validity test at :1458-1470), and a DEAD hero reports Fraction 0 — which the
            // low-HP weight reads as the single most attractive target on the field. So the
            // brain keeps steering onto the body, DriveNav reads overrideOnHero, and this line
            // re-stamps the pursuit pulse EVERY FRAME, for as long as the hero stays down.
            //
            // That is precisely the shape F8 seq 4702 could not name:
            //     "battle-lock STILL HELD after the self-heal (retreat): [PursuitBattleProbe.Probe]
            //      … either a LIVE chase re-pulsing every aggro tick, or an owner whose probe is
            //      latched true with no battle behind it."
            // The self-heal had just run a full ClearPursuits and the lock was back inside ONE
            // frame — which only a live producer can do — while "retreat" is the context
            // BattleArena.Resolve passes for EVERY won==false outcome, the hero's own death
            // included (BattleArena.cs:2228 "hero down - loss." -> Resolve(false)).
            //
            // ⚠ WHY THE GUARD IS ON THE STAMP AND NOT ON THE STEERING. Whether defenders should
            // keep mobbing a downed hero is a COMBAT-FEEL question and it belongs to WO-1526's
            // own watch item ("EnemyBrain has no dead-hero check, defenders may keep mobbing the
            // body", commit 2b3d8e9af) — not to this ticket, and not to this file. What is not a
            // question is the SIGNAL: PursuitActive exists to keep the hero's combat inputs live
            // while she is being chased (F8-46, owner OPTION A). A hero who is DOWN has no inputs
            // to serve, so a pulse stamped over her corpse buys nothing and costs a stuck
            // battle-lock — suppressed combat input and a HUD that cannot return to town.
            //
            // ⚠ AND IT CANNOT SUPPRESS A REAL CHASE. The predicate is the hero's own IsAlive, the
            // same one the sibling branch uses; a live hero being chased stamps exactly as before,
            // and a null HeroHealth (test scenes / headless) counts as ALIVE — the same
            // conservative reading BattleArena's own outcome arbitration takes (BattleArena.cs:
            // "bool heroAlive = hh == null || hh.IsAlive;"). Nothing here narrows
            // PursuitBattleProbe, forces BattleLock false, or adds a release call.
            // =================================================================================
            if (chasingHero)
            {
                var chasedHero = HeroHealth.Instance;
                bool heroAliveToChase = chasedHero == null || chasedHero.IsAlive;
                if (heroAliveToChase)
                {
                    DeNelle.Core.HudModel.PostureSignals.ReportPursuit(GetInstanceID(), chaseVia);
                }
                else
                {
                    // Stand this body's own claim down the moment the hero goes down, rather than
                    // letting the last stamp ride out PursuitTtl — the gate judges a retreat at
                    // SettleSeconds (0.75 s), which is INSIDE that 1.5 s window (WO-1337's
                    // arithmetic, same numbers). Idempotent: RevokePursuit no-ops on an absent key.
                    DeNelle.Core.HudModel.PostureSignals.RevokePursuit(GetInstanceID());
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"deadchase-{_enemyId}", 5f,
                        $"{_enemyId}: still steered at the hero via {chaseVia} while HeroHealth.IsAlive=false - " +
                        "pursuit pulse NOT stamped and this body's own claim revoked (WO-1603). The steering " +
                        "itself belongs to EnemyBrain (WO-1526 watch item), not to the battle-lock signal.");
                }
            }

            // DEF-56: throttle SetDestination — only re-path when the timer expires
            // OR the destination has moved significantly. This cuts NavMesh CPU by
            // ~80% on a 20-enemy wave without visible path quality regression.
            _pathRefreshTimer -= Time.deltaTime;
            float distMoved = (destPos - _lastPathedDestination).sqrMagnitude;
            bool destMoved = distMoved > _pathMinMoveDelta * _pathMinMoveDelta;
            if (_pathRefreshTimer <= 0f || destMoved)
            {
                _pathRefreshTimer = _pathRefreshInterval;
                _lastPathedDestination = destPos;
                _agent.SetDestination(destPos);
            }

            // Arrived at the Heart without being repelled — report the breach.
            float planarDist = Vector3.ProjectOnPlane(
                _heart.position - transform.position, Vector3.up).magnitude;
            if (planarDist <= _heartArrivalRadius)
            {
                ReachedHeart?.Invoke(this);
            }
        }

        // ---------------------------------------------------------------------
        // DEF-224: hero aggro — break off the Heart-siege to attack a nearby hero
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns true (and the hero's world position) when this enemy should be
        /// chasing the hero this frame: the hero exists, is alive, and is within
        /// <see cref="_heroAggroRadius"/> (sticky out to + <see cref="_heroAggroDropMargin"/>
        /// once engaged, so the enemy doesn't flicker at the edge). Returns false —
        /// and clears the engaged latch — when aggro is disabled (radius 0) or the
        /// hero is out of range / gone, so the caller falls through to the
        /// Heart-siege march. Brain-driven enemies never reach here (DriveNav gives
        /// the brain override priority), so this is purely additive.
        /// </summary>
        private bool TryGetHeroAggroDestination(out Vector3 heroPos)
        {
            heroPos = default;
            if (_heroAggroRadius <= 0f) { _heroAggroEngaged = false; return false; }

            ResolveHeroTransform();
            if (_heroTransform == null) { _heroAggroEngaged = false; return false; }

            // The hero may have died (HeroHealth.IsAlive false) — don't chase a
            // downed/invulnerable hero; resume the Heart-siege so the wave keeps
            // pressuring the win condition.
            var heroHealth = _heroTransform.GetComponentInParent<HeroHealth>();
            if (heroHealth != null && !heroHealth.IsAlive) { _heroAggroEngaged = false; return false; }

            float planarSqr = Vector3.ProjectOnPlane(
                _heroTransform.position - transform.position, Vector3.up).sqrMagnitude;

            // Hysteresis: enter at _heroAggroRadius, leave only past the drop margin.
            // ⚠ THE DROP MARGIN IS THE WORLD-SIDE BAITING LEASH (owner: "we need to allow aggro
            // targets to extend leash alot more", 2026-08-16). At the old 2.5m default a wave
            // enemy lost interest at ~9.5m — INSIDE bow range — so a ranger could not hold aggro
            // long enough to pull one body off a pack, and the enemy simply turned back to its
            // Heart-march. A chase that survives 2.5m past the notice ring is not a chase.
            // AggroTuning.WorldChaseDropMargin (aggro-tuning.json "world") raises the FLOOR to
            // ~18m: break-off now sits just past bow range, so a shot holds aggro, and the enemy
            // still gives up well before it reaches town. Max() so a def that authored a LARGER
            // margin keeps it. Data, not a constant — tune without a rebuild; 0 restores stock.
            float engageR = _heroAggroRadius;
            float dropR   = _heroAggroRadius + Mathf.Max(
                _heroAggroDropMargin,
                DeNelle.Village.AggroTuning.WorldChaseDropMargin);
            float threshold = _heroAggroEngaged ? dropR : engageR;
            if (planarSqr > threshold * threshold) { _heroAggroEngaged = false; return false; }

            _heroAggroEngaged = true;
            heroPos = _heroTransform.position;
            return true;
        }

        /// <summary>
        /// Lazily resolves (and periodically refreshes) the hero transform.
        /// WO-450: resolves by COMPONENT (HeroLocomotion — the one component every hero
        /// variant carries) rather than the (undeclared) "HeroTarget" tag; falls back to
        /// the built-in "Player" tag the village hero now carries. The result is re-checked
        /// on an interval so an enemy that spawned before the hero (or after a hero respawn)
        /// still acquires it.
        /// </summary>
        private void ResolveHeroTransform()
        {
            _heroResolveTimer -= Time.deltaTime;
            bool valid = _heroTransform != null && _heroTransform.gameObject.activeInHierarchy;
            if (valid && _heroResolveTimer > 0f) return;

            _heroResolveTimer = 1f;   // cheap: at most once/sec per enemy
            if (valid) return;

            var loco = FindAnyObjectByType<HeroLocomotion>();   // WO-450: component lookup
            _heroTransform = loco != null ? loco.transform : SafeFindByTag("Player");

            // WO-419: trace the acquire across the seam — confirms a brain-less overworld
            // guard finds the (additively-loaded) hero by component, not an empty tag scan.
            if (_heroTransform == null)
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"acq-miss-{_enemyId}", 2f,
                    $"{_enemyId}: hero acquire MISS (no HeroLocomotion / 'Player' tag in any loaded scene).");
            else
                DeNelle.Core.Diagnostics.FlowTrace.Once("EnemyAggro", $"acq-{_enemyId}",
                    $"{_enemyId}: acquired hero '{_heroTransform.name}' in scene " +
                    $"'{_heroTransform.gameObject.scene.name}'.");
        }

        /// <summary>Null-safe tag lookup tolerating an undefined tag (Unity throws otherwise).</summary>
        private static Transform SafeFindByTag(string tag)
        {
            try
            {
                var go = GameObject.FindWithTag(tag);
                return go != null ? go.transform : null;
            }
            catch (UnityEngine.UnityException)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------------
        // Contact attack — strike the structure directly ahead
        // ---------------------------------------------------------------------

        /// <summary>
        /// Probes for an <see cref="IDamageableStructure"/> directly ahead; when
        /// one is in reach the enemy stops and deals <see cref="_contactDamage"/>
        /// every <see cref="_attackInterval"/> seconds until it falls.
        /// </summary>
        private void TickContactAttack()
        {
            // Drop a dead / destroyed target.
            if (_currentTarget != null && !_currentTarget.IsAlive)
            {
                // WO-1450 §2: PERMANENT drop-path trace. Three different lines assign
                // _currentTarget = null and the capture could not tell them apart, so a
                // re-acquisition thrash read as "the probe is noisy" with no way to name
                // WHICH release fed it. Throttled per enemy — the cadence is the defect
                // this ticket exists to fix, so the diagnostic must not reintroduce it.
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"drop-dead-{_enemyId}", 1f,
                    $"{_enemyId}: target RELEASED — path=not-alive (IsAlive false)");
                _currentTarget = null;
                _lastProbeTargetId = 0;
            }

            // DEF-224: drop a still-alive target that has MOVED out of reach (the
            // hero runs away). Without this the enemy would stay frozen in place
            // attacking nothing because the lock only released on the target's
            // death. Static structures never move past this radius, so they keep
            // their lock; only a fleeing hero/mobile target is released — at which
            // point the agent resumes chasing (hero aggro) or marching (Heart).
            if (_currentTarget != null)
            {
                var heldMb = _currentTarget as MonoBehaviour;
                if (heldMb != null)
                {
                    float dropSqr = (_contactProbeDistance + 1.5f) * (_contactProbeDistance + 1.5f);
                    float heldSqr = (heldMb.transform.position - transform.position).sqrMagnitude;
                    if (heldSqr > dropSqr)
                    {
                        // WO-1450 §2: PERMANENT drop-path trace — names the DEF-224 distance
                        // release and prints the measurement that fired it, so a future capture
                        // proves whether a thrash is a target oscillating across the drop ring
                        // (this line repeating) or a target dying (the not-alive line above).
                        DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"drop-dist-{_enemyId}", 1f,
                            $"{_enemyId}: target RELEASED — path=out-of-reach (DEF-224) " +
                            $"dist={Mathf.Sqrt(heldSqr):F1}m > drop={(_contactProbeDistance + 1.5f):F1}m " +
                            $"held='{heldMb.name}'");
                        _currentTarget = null;
                        _lastProbeTargetId = 0;
                    }
                }
            }

            if (_currentTarget == null)
            {
                // ── CADENCE GATE (WO-1450 / WO-1459 §2 suspect 3) ──────────────────
                // Only the RETRY rate is limited. A skipped frame falls through the exact
                // `_attackCooldown = 0f; return;` path a null probe already took, so this
                // is behaviour-identical to a frame on which the probe found nothing —
                // no change to selection order, the hero-primary rule or the faction rule.
                if (Time.time < _nextProbeAt)
                {
                    _attackCooldown = 0f;
                    return;
                }
                _nextProbeAt = Time.time + ProbeIntervalSeconds;

                _currentTarget = ProbeForStructure();

                // EnemyAggro observability: trace BOTH outcomes of structure acquisition so a
                // headless run shows whether the forward-only SphereCast ever finds defenses /
                // the Heart, or returns null (off-axis miss) leaving the enemy on a Heart-march.
                if (_currentTarget == null)
                {
                    _lastProbeTargetId = 0;
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"probe-fail-{_enemyId}", 1f,
                        $"{_enemyId}: ProbeForStructure null -> no structure target (Heart-march / roam only)");
                }
                else
                {
                    // WO-1450: this was FlowTrace.Step — unthrottled, once per frame per enemy,
                    // 38,018 lines at ~320/sec with a managed stack walk each. It is now gated
                    // TWICE: it fires only when the acquired target CHANGED (the event worth
                    // reading — a re-acquire of the same wall is not news), and even then at
                    // most 1/sec per enemy. The trace is KEPT, not stripped (CLAUDE.md §12:
                    // instrumentation is permanent — the defect was the cadence, not the line).
                    var hitMb = _currentTarget as MonoBehaviour;
                    int hitId = hitMb != null ? hitMb.GetInstanceID() : 0;
                    if (hitId != _lastProbeTargetId)
                    {
                        _lastProbeTargetId = hitId;
                        DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"probe-hit-{_enemyId}", 1f,
                            $"{_enemyId}: ProbeForStructure ACQUIRED '{(hitMb != null ? hitMb.name : "?")}' " +
                            "-> stopping agent to attack (target CHANGE)");
                    }
                }
            }

            if (_currentTarget == null)
            {
                _attackCooldown = 0f;
                return;
            }

            _attackCooldown -= Time.deltaTime;
            if (_attackCooldown <= 0f && !_telegraphing)
            {
                var targetObject = _currentTarget as UnityEngine.Object;
                int maxCommitters = Tier == EnemyTier.Ordinary ? 2 : 1;
                if (!EnemyAttackDirector.TryAcquire(this, targetObject, maxCommitters))
                {
                    _attackCooldown = UnityEngine.Random.Range(0.18f, 0.42f);
                    return;
                }
                _attackTokenHeld = true;
                _contactCommitTarget = _currentTarget;
                _attackCooldown = _attackInterval;

                // DEF-48 / WO-560: ALWAYS telegraph the contact strike. Arena orcs are
                // built by EnemyFactory with a synthesized EnemyDef and never receive a
                // _typeVfxSet, so the legacy "telegraphDuration==0 -> instant hit" path
                // gave the V1 fight NO readable wind-up (owner F8: enemies hit with no
                // tell). We now floor the wind-up at ContactTelegraphFloor (>=1.0s) so the
                // melee read is reactable even with no SO configured, and the ground-ring
                // warning is drawn unconditionally in TelegraphThenAttack.
                float telegraphDuration = _typeVfxSet != null && _typeVfxSet.TelegraphDuration > 0f
                    ? _typeVfxSet.TelegraphDuration : ContactTelegraphFloor;
                telegraphDuration = Mathf.Max(telegraphDuration, ContactTelegraphFloor);
                StartCoroutine(TelegraphThenAttack(telegraphDuration));
            }
        }

        // ── DEF-48 / WO-560: telegraph → attack ───────────────────────────────

        /// <summary>
        /// WO-560: minimum readable wind-up for a contact (melee) strike. Floors the
        /// telegraph so arena orcs (no _typeVfxSet) still show a reactable tell. Matches
        /// the rooted-cast floor (RootedCast uses 1.0s) so melee and cast read alike.
        /// </summary>
        private const float ContactTelegraphFloor = 1.0f;

        /// <summary>
        /// The ROOTED-CAST wind-up every ranged caster used before 2026-08-16 (when
        /// <c>_typeVfxSet</c> was universally null). Kept as a FLOOR in
        /// <see cref="RootedCast"/> so making the type set resolve can only lengthen a
        /// tell, never shorten one - the cue fix must not quietly retune difficulty.
        /// </summary>
        private const float RangedCastWindUpFloor = 1.2f;

        /// <summary>
        /// DEF-48 / WO-560: Plays the wind-up animation + a ground-ring danger tell at
        /// the target's feet, then deals damage after <paramref name="duration"/> seconds.
        /// The ground ring is drawn UNCONDITIONALLY (procedural <see cref="VFXManager"/>
        /// shockwave ring) so the warning shows even when no <see cref="EnemyTypeVfxSet"/>
        /// is assigned (arena path). An authored TelegraphVFXPrefab, when present, is
        /// spawned in addition. Guards against double-trigger via <see cref="_telegraphing"/>.
        /// </summary>
        private System.Collections.IEnumerator TelegraphThenAttack(float duration)
        {
            _telegraphing = true;
            _contactCommitInterrupted = false;

            // Wind-up animation trigger — Animator must have a "WindUp" state.
            if (_animator != null && _hasWindUpParam) _animator.SetTrigger(AnimWindUp);

            // WO-560: ALWAYS draw a ground-ring danger tell at the target's feet so the
            // player can read + react to the incoming melee strike. Uses the procedural
            // Impact_ShockwaveRing fallback (committed asset / AbilityVfxKit) — no pack
            // prefab, no SO required. FlowTrace each fire so the telegraph is observable
            // in a headless run (acceptance: telegraph window > 0).
            var targetMb = _currentTarget as MonoBehaviour;
            if (targetMb != null)
            {
                Vector3 feet = targetMb.transform.position;
                VFXManager.Play(VFXType.Impact_ShockwaveRing, feet,
                    Quaternion.identity, playSound: false);
                DeNelle.Core.Diagnostics.FlowTrace.Step("VFXTelegraph",
                    $"{_enemyId}: MELEE telegraph fired (dur={duration:F2}s) ground-ring @ {feet} target='{targetMb.name}'");
            }

            // Authored ground-ring warning VFX at the target's position (in addition).
            GameObject telegraphVFX = null;
            if (_typeVfxSet != null && _typeVfxSet.TelegraphVFXPrefab != null
                && targetMb != null)
            {
                telegraphVFX = Instantiate(
                    _typeVfxSet.TelegraphVFXPrefab,
                    targetMb.transform.position,
                    Quaternion.identity);
            }

            yield return new WaitForSeconds(duration);

            // Clean up the VFX regardless of whether the attack lands.
            if (telegraphVFX != null) Destroy(telegraphVFX);

            // Re-check viability after the delay — target may have died or moved.
            if (!_dead && !_contactCommitInterrupted && _contactCommitTarget != null && _contactCommitTarget.IsAlive)
            {
                _contactCommitPending = true;
                _actor?.PlayAttack();
                // Let the trigger transition enter its attack state, then put the
                // compatibility fallback AFTER the reviewed clip event. This keeps
                // legacy controllers functional without racing a real HitFrame.
                yield return null;
                float fallbackDelay = ResolveContactHitFallbackDelay();
                yield return new WaitForSeconds(fallbackDelay);
                if (_contactCommitPending)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAttack", "fallback-hitframe", 2f,
                        "Enemy contact used legacy HitFrame fallback; author a reviewed clip event.");
                    OnAnimationHitFrame();
                }
            }

            yield return new WaitForSeconds(ContactRecoverSeconds);
            _contactCommitPending = false;
            _telegraphing = false;
            ReleaseAttackToken();
        }

        /// <summary>Animation Event seam. Consumes a pending contact hit exactly once.</summary>
        public void OnAnimationHitFrame()
        {
            if (!_contactCommitPending || _contactCommitInterrupted || _dead) return;
            _contactCommitPending = false;
            ExecuteContactAttack(_contactCommitTarget);
        }

        private float ResolveContactHitFallbackDelay()
        {
            if (_animator == null) return ContactHitFallbackSeconds;
            float latestReviewedHit = -1f;
            var current = _animator.GetCurrentAnimatorClipInfo(0);
            var next = _animator.IsInTransition(0) ? _animator.GetNextAnimatorClipInfo(0) : null;
            FindReviewedHitTime(current, ref latestReviewedHit);
            FindReviewedHitTime(next, ref latestReviewedHit);
            return latestReviewedHit >= 0f
                ? Mathf.Max(ContactHitFallbackSeconds, latestReviewedHit + 0.12f)
                : ContactHitFallbackSeconds;
        }

        private static void FindReviewedHitTime(AnimatorClipInfo[] infos, ref float latest)
        {
            if (infos == null) return;
            for (int i = 0; i < infos.Length; i++)
            {
                AnimationClip clip = infos[i].clip;
                if (clip == null) continue;
                AnimationEvent[] events = clip.events;
                if (events == null) continue;
                for (int e = 0; e < events.Length; e++)
                    if (events[e] != null && events[e].functionName == "HitFrame")
                        latest = Mathf.Max(latest, events[e].time);
            }
        }

        private void ReleaseAttackToken()
        {
            if (!_attackTokenHeld) return;
            _attackTokenHeld = false;
            EnemyAttackDirector.Release(this);
            _contactCommitTarget = null;
        }

        /// <summary>
        /// DEF-48: The actual damage + animation + audio tick, extracted from
        /// <see cref="TickContactAttack"/> so both the instant and telegraph paths
        /// share one call site.
        /// </summary>
        private void ExecuteContactAttack(IDamageableStructure strikeTarget)
        {
            if (strikeTarget == null || !strikeTarget.IsAlive) return;

            // Smooth pivot to face the struck target so the melee read is correct
            // (anti-snap: request the facing; TickFacing slerps over frames). The
            // contact path runs while the agent is stopped, so velocity-facing is
            // silent and this is the only facing driver — but it turns, never snaps.
            var targetMb = strikeTarget as MonoBehaviour;
            if (targetMb != null)
                RequestFacing(targetMb.transform.position - transform.position);

            NoteHeroDamageSource(strikeTarget);
            DealStructureDamage(strikeTarget, _contactDamage, contact: true);
            PlayTypeSound(_typeVfxSet != null ? _typeVfxSet.RandomAttackClip() : null);

            // VFX-FREE-WIN-3: the BLOW LANDING was the only silent beat in the melee
            // exchange. TelegraphThenAttack (:1507) draws the wind-up ground ring and this
            // method plays the swing anim + grunt, but nothing marked the CONTACT — so the
            // player learned they had been hit from a health bar, one full beat late. On a
            // structure target there is not even a health bar in view.
            //
            // Impact_Physical is ALREADY wired (VFXCatalog.asset row Type:1 ->
            // Lana/Burst/Slash_stone_once, IsLoop:0 — a ONESHOT, so it cannot consume one of
            // the 20 leak-prone loop slots; enemy melee ticks every ~1.3s per attacker and a
            // loop row here would saturate the cap in seconds). It is a stone-slash ARC:
            // meaning carried by silhouette and direction, not by hue (colourblind law).
            //
            // Placed at the TARGET's chest, not the enemy's, because that is where the blow
            // resolves and where the eye is. playSound:false — the attack grunt is already
            // played by PlayTypeSound above; VFXManager must not layer a second cue.
            if (targetMb != null)
            {
                Vector3 hitPos = targetMb.transform.position + Vector3.up * 1.0f;

                DeNelle.Core.Diagnostics.Guard.Try("Enemy", "melee connect vfx", () =>
                    VFXManager.Play(VFXType.Impact_Physical, hitPos,
                                    Quaternion.identity, playSound: false));

                // WO-887 (surface half) - WHAT THE BLOW LANDED ON, layered ON TOP of the
                // generic Impact_Physical above rather than replacing it. The generic slash
                // arc is the CONTACT read (it fires no matter what, so a hit is never
                // silent); the surface burst is the MATERIAL read - splatter vs spark vs
                // chip vs splinter, carried by debris shape and motion, not by hue.
                // HitSurfaceVfx.Resolve returns None rather than guessing when it cannot
                // tell, and Play no-ops on None, so an unrecognised target degrades to
                // exactly today's behaviour.
                DeNelle.Core.Diagnostics.Guard.Try("Enemy", "melee surface impact", () =>
                    HitSurfaceVfx.ResolveAndPlay(targetMb, hitPos));

                // WO-874 - the elite/boss ATTACK tell. This is one of the two behaviours
                // that the 4c1da079 static shortcut could not deliver (the other is the
                // aura): OnEliteAttack needs the INSTANCE to know its own tier at the
                // moment the blow lands. _eliteVfx is null for every plain-tier enemy, so
                // this is a null check on the hot path and nothing more.
                if (_eliteVfx != null)
                {
                    DeNelle.Core.Diagnostics.Guard.Try("Enemy", "elite attack vfx", () =>
                        _eliteVfx.OnEliteAttack(hitPos));
                }
            }
        }

        /// <summary>Feeds directional-death source position when the target is the hero.</summary>
        private void NoteHeroDamageSource(IDamageableStructure target)
        {
            if (target is HeroHealth hh)
                hh.NoteDamageSource(transform.position);
        }

        /// <summary>
        /// CITY-02/03: single damage-to-structure sink. When the struck structure is the
        /// Heart, the village WALL mitigates the blow - the incoming damage is multiplied
        /// by walls.json <c>heartDamageMultiplier</c> for the player's current wall level
        /// (upgrades now actually protect the Heart; walls.json was previously orphaned and
        /// every hit landed at full value). At the spiked top tier the wall also BITES a
        /// MELEE breacher back (<c>spikeDamagePerSecond</c> over one attack interval), so
        /// the top tier is no longer a cosmetic no-op. Walls, gates, buildings and the hero
        /// take the raw hit unchanged. All three enemy strike paths (melee / ranged / caster)
        /// route through here so mitigation is applied once, consistently.
        /// </summary>
        private void DealStructureDamage(IDamageableStructure target, float damage, bool contact = false)
        {
            if (target == null || damage <= 0f) return;

            // ═══ WO-1439 §6 — THE SEAM ORACLE: no actor may damage an asset of its own faction.
            // Every part of this system worked in the owner's raid — probing probed, scoring
            // scored, damage applied — and NOTHING asserted that a combatant only attacks things
            // it should. This is the one assertion that would have caught it on the day it
            // shipped, and it lives at the DAMAGE SINK (all three enemy strike paths — melee,
            // ranged and caster — funnel here) rather than at a selection site, so it holds even
            // if a future selection path forgets to call CombatFactionRules. FlowTrace.Fail, not
            // a silent return: §12 forbids swallowing, and a Fail line names the offender.
            if (DeNelle.Core.Combat.CombatFactionRules.IsFriendlyFire(SelfFaction, target))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("EnemyAggro",
                    $"{_enemyId}: FRIENDLY FIRE REFUSED — tried to deal {damage:0.#} to " +
                    $"'{(target as MonoBehaviour)?.name ?? "<non-MB>"}' which is {target.Faction}, " +
                    $"the same faction as the attacker (contact={contact}). Target selection let a " +
                    "same-faction asset through; fix the SELECTION site, this sink only stops the blow.");
                return;
            }

            if (target is HeartController)
            {
                float mult = DeNelle.Village.Walls.WallDefense.CurrentHeartDamageMultiplier();
                float mitigated = damage * mult;
                if (mult < 1f)
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("Wall", $"heart-mit-{_enemyId}", 1f,
                        $"heart hit mitigated x{mult:0.00}: {damage:0.#}->{mitigated:0.#} " +
                        $"(wallLevel={DeNelle.Village.Walls.WallDefense.CurrentWallLevel()}).");
                target.ApplyContactDamage(mitigated);

                // CITY-03: at the spiked top tier the wall wounds a MELEE breacher.
                if (contact && !_dead)
                {
                    float spikeDps = DeNelle.Village.Walls.WallDefense.CurrentSpikeDamagePerSecond();
                    if (spikeDps > 0f)
                    {
                        float bite = spikeDps * Mathf.Max(0.25f, _attackInterval);
                        DeNelle.Core.Diagnostics.FlowTrace.Throttle("Wall", $"spike-{_enemyId}", 1f,
                            $"spiked wall bites breacher {_enemyId}: {bite:0.#} dmg (dps={spikeDps:0.#}).");
                        TakeDamageFrom(bite, transform.position + transform.forward * 2f);
                    }
                }
                return;
            }

            target.ApplyContactDamage(damage);
        }

        /// <summary>
        /// WO-145 (Tactic B): deals hit-scan ranged damage to <paramref name="target"/>
        /// without closing to contact. Resolves an <see cref="IDamageableStructure"/>
        /// on the target (HeroHealth / Tower / Heart all implement it) and routes the
        /// hit through the same <c>ApplyContactDamage</c> path as melee, so HP,
        /// floating numbers and death are handled identically. Fires the Attack
        /// animator trigger (null-safe). No projectile / VFX — instant damage only
        /// (juice is a follow-on WO). Called by <see cref="EnemyBrain"/> while kiting.
        /// </summary>
        /// <param name="target">The transform to strike (its root may host the interface).</param>
        /// <param name="damage">Damage to apply this shot.</param>
        /// <returns>True when a live damageable target was hit.</returns>
        public bool RangedAttack(Transform target, float damage)
        {
            if (_dead || target == null || damage <= 0f) return false;

            // The interface may live on the target or a parent (collider is often a child).
            var structure = target.GetComponentInParent<IDamageableStructure>();
            if (structure == null || !structure.IsAlive) return false;

            // Face the target so the shot reads (smooth pivot, anti-snap).
            Vector3 toTarget = target.position - transform.position; toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f)
                RequestFacing(toTarget);

            // WO-491: ROOTED + TELEGRAPHED cast (flag-gated). The caster plays a WindUp ->
            // Cast animation + an audio charge cue and ROOTS the NavMeshAgent for the cast
            // window so the strike is readable/dodgeable and the caster does NOT slide while
            // casting. Damage lands at the END of the wind-up (re-checked for viability).
            // When the flag is OFF (or already mid-cast) the legacy instant hit runs.
            if (DeNelle.Core.FeatureFlags.EnemyRootedCast && !_casting)
            {
                StartCoroutine(RootedCast(target, damage));
                return true;
            }

            // ── Legacy instant ranged hit (flag OFF) ─────────────────────────
            NoteHeroDamageSource(structure);
            DealStructureDamage(structure, damage);   // CITY-02: wall mitigates Heart hits
            _actor?.PlayCast();
            PlayTypeSound(_typeVfxSet != null ? _typeVfxSet.RandomAttackClip() : null);
            return true;
        }

        // WO-491: true while a rooted cast wind-up is in flight — blocks a second cast
        // (and the TickContactAttack telegraph) from overlapping it.
        private bool _casting;

        // ── P4 cast-telegraph push seam (HUD_OBSIDIAN_ARCHITECTURE_2026-07-03 §3.4) ──
        // The rooted cast had NO push seam (all state private to the RootedCast coroutine),
        // so the HUD CastModel producer could not observe it. Smallest additive event pair
        // on the existing static-event pattern (BuildModeController.BuildModeChanged):
        // fired only from RootedCast, no behaviour change. NOTE: if the caster is destroyed
        // mid-cast the coroutine dies and CastEnded never fires — subscribers must ALSO
        // self-expire on (start + windUpSeconds) / a dead caster.
        /// <summary>Raised when a rooted telegraphed cast begins: (caster, abilityName, windUpSeconds).</summary>
        public static event System.Action<Enemy, string, float> CastStarted;
        /// <summary>Raised when that cast's wind-up completes or is released.</summary>
        public static event System.Action<Enemy> CastEnded;

        // Display name for the rooted-cast ability (the visible arcane orb the cast fires).
        private const string RootedCastAbilityName = "Arcane Orb";

        // WO-VFX-RANGED / owner VfxManualPicks (2026-07): fallback keys when this enemy has no
        // _typeVfxSet (arena orcs, wave casters, etc.). Per-type sets still override.
        // Owner 2026-07-24: upgraded from the flattened placeholder keys to the FULL MULTI-LAYER
        // prefabs (WO-758: prefab = recipe, don't flatten layers) so a caster VISIBLY hurls a rich
        // fireball. Fire_Cast = a full Hovl fire gather at the hands (oneshot); PP_FireBall = the full
        // ParticlePack fireball body that TRAVELS, followed by the projectile mover and soft-stopped on
        // arrival (like the old SimpleCast_Projectile, also a loop); FireballImpact_Impact = the full
        // multi-layer Hovl fireball explosion on land. LIFECYCLE NOTE: the impact is fired
        // fire-and-forget (no handle captured in the land closure below), so it MUST be a ONESHOT
        // (IsLoop:0) prefab that self-lifetimes — a loop impact (e.g. PP_EnergyExplosion) would never
        // stop, pin the active-loop cap, and starve every other loop/aura. FireballImpact_Impact is a
        // full-layer explosion that is also oneshot, so it reads rich AND self-cleans. All three
        // resolve to full prefabs in HovlVfxCatalog and route through the ONE VFXManager pool (PlayKey).
        // ONE declaration, shared with EnemyTypeVfxSet's field initializers (2026-08-16):
        // an un-authored set now resolves for EVERY enemy, so if these constants and the
        // SO's defaults ever disagreed the library would silently re-skin every caster.
        private const string DefaultCastVfxKey       = EnemyTypeVfxSet.DefaultCastVfxKey;
        private const string DefaultProjectileVfxKey = EnemyTypeVfxSet.DefaultProjectileVfxKey;
        private const string DefaultImpactVfxKey     = EnemyTypeVfxSet.DefaultImpactVfxKey;
        private static readonly Color DefaultRangedVfxTint = EnemyTypeVfxSet.DefaultRangedVfxTint; // fire orange (recolorable rows only; shape/motion reads regardless)

        // Visible-cast VFX for ranged/mage casters (owner F8: "could not tell he was casting").
        // Lazily added so the enemy fires a real arcane orb that the player SEES leave + land.
        private RangedAttackVFX _castVfx;
        private RangedAttackVFX EnsureCastVfx()
        {
            if (_castVfx == null)
                _castVfx = TryGetComponent<RangedAttackVFX>(out var rv) ? rv : gameObject.AddComponent<RangedAttackVFX>();
            return _castVfx;
        }

        /// <summary>
        /// WO-491: rooted, telegraphed ranged cast (mirrors <see cref="TelegraphThenAttack"/>'s
        /// shape). Stops the agent, plays WindUp -> charge audio -> Cast, waits the wind-up,
        /// then applies the damage if the target is still viable, then resumes the agent.
        /// </summary>
        private System.Collections.IEnumerator RootedCast(Transform target, float damage)
        {
            _casting = true;

            // Telegraph window — readable wind-up before the strike. Reuse the type-set's
            // configured telegraph duration when present, else a sane default.
            // NO-SHORTENING RULE (2026-08-16): the type set may only LENGTHEN this tell.
            // Before today _typeVfxSet was always null here, so every cast used the 1.2s
            // default; now that the set always resolves (EnemyTypeVfxLibrary), a plain
            // "use the set's value" read would have SILENTLY CUT the cast tell to the
            // default asset's 0.5s -> 1.0s floor. Shortening a wind-up is a balance change
            // and this fix is a plumbing fix, so the previous default is kept as the floor.
            float windUp = Mathf.Max(
                _typeVfxSet != null ? _typeVfxSet.TelegraphDuration : 0f,
                RangedCastWindUpFloor);
            // Readable-telegraph floor (owner F8: "animations from enemy very boring, could
            // not tell he was casting"). A sub-second wind-up doesn't register; hold >=1.0s so
            // the WindUp pose reads as a deliberate, reactable channel before the strike.
            windUp = Mathf.Max(windUp, 1.0f);

            // Owner ruling 2026-08-16: the school-matched Spells Pack Casting_* loop plays ON
            // the caster during the wind-up, replacing the HUD cast bar as the telegraph.
            // School comes from the same VFX key the release will fire (the strongest element
            // signal - e.g. the default Fire_Cast => Casting_Fire); ability name is the tiebreak.
            // Spawn BEFORE CastStarted so the CastProducer sees IsTelegraphed and suppresses
            // the bar for this cast; a failed spawn (missing mirror) leaves the bar showing.
            string windupSchool = CastingTelegraphVfx.ResolveSchool(
                _enemyId,
                RootedCastAbilityName,
                (_typeVfxSet != null && !string.IsNullOrEmpty(_typeVfxSet.CastVfxKey)) ? _typeVfxSet.CastVfxKey : DefaultCastVfxKey);
            GameObject windupTelegraph = CastingTelegraphVfx.TryBegin(this, windupSchool, RootedCastAbilityName, windUp);

            // Second owner pick 2026-08-16: the Marker 2 Pointer Loop hovers on the CAST'S
            // TARGET (the hero/structure this cast will strike) for the wind-up window -
            // parented, so a target that dies tears it down; additive presentation only.
            GameObject windupTargetMarker = CastingTelegraphVfx.TryBeginTargetMarker(
                this, target, null, RootedCastAbilityName, windUp);

            // P4 cast seam — announce the telegraph so the HUD cast bar can track it.
            CastStarted?.Invoke(this, RootedCastAbilityName, windUp);

            // Root the agent for the cast (commit to it — no slide while casting).
            bool wasStopped = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                wasStopped = _agent.isStopped;
                _agent.isStopped = true;
            }

            // F8-38 gate-in (cast-start): record that the cast ASKED the agent to stop. The next
            // per-frame DriveNav tick has NO _casting awareness, so pair this with the DriveNav
            // mid-cast trace to see whether the root actually holds for the whole channel.
            DeNelle.Core.Diagnostics.FlowTrace.Step("EnemyCast",
                $"{_enemyId}: CAST-START windUp={windUp:F2}s -> agent.isStopped set TRUE " +
                $"(wasStopped={wasStopped}, onNavMesh={(_agent != null && _agent.isOnNavMesh)}, " +
                $"contactTarget={((_currentTarget as MonoBehaviour)?.name ?? "<null>")}). Root must hold until CAST-END.");

            // Telegraph: wind-up pose + audio charge cue.
            _actor?.PlayWindUp();
            EnemyCombatAudio.PlayCastCharge();

            // WO-560: draw a ground-ring danger tell at the AIM POINT (target's feet) so
            // the incoming cast is readable + dodgeable, mirroring the melee tell. Uses the
            // procedural Impact_ShockwaveRing fallback (committed asset, no pack prefab).
            if (target != null)
            {
                Vector3 castFeet = target.position;
                VFXManager.Play(VFXType.Impact_ShockwaveRing, castFeet,
                    Quaternion.identity, playSound: false);
                DeNelle.Core.Diagnostics.FlowTrace.Step("VFXTelegraph",
                    $"{_enemyId}: CAST telegraph fired (windUp={windUp:F2}s) ground-ring @ {castFeet}");
            }

            yield return new WaitForSeconds(windUp * 0.6f);

            // The cast itself (rooted): cast animation pose at the strike moment.
            if (!_dead) _actor?.PlayCast();

            yield return new WaitForSeconds(windUp * 0.4f);

            // Release: fire a VISIBLE arcane orb from the caster to the target so the cast
            // READS (owner F8: "could not tell he was casting"). Damage lands on orb ARRIVAL
            // (re-checked for viability), syncing the hit to the visible impact. Falls back to
            // instant damage only if the VFX component can't be resolved.
            if (!_dead && target != null)
            {
                Vector3 aim = target.position + Vector3.up * 1.0f;
                var vfx = EnsureCastVfx();

                // WO-VFX-RANGED: resolve this enemy's ranged Hovl keys (+ tint); fall back to the
                // Arcane constants when there is no _typeVfxSet (arena orcs) so the cast still reads.
                string castKey    = (_typeVfxSet != null && !string.IsNullOrEmpty(_typeVfxSet.CastVfxKey))       ? _typeVfxSet.CastVfxKey       : DefaultCastVfxKey;
                string projKey    = (_typeVfxSet != null && !string.IsNullOrEmpty(_typeVfxSet.ProjectileVfxKey)) ? _typeVfxSet.ProjectileVfxKey : DefaultProjectileVfxKey;
                string impactKey  = (_typeVfxSet != null && !string.IsNullOrEmpty(_typeVfxSet.ImpactVfxKey))     ? _typeVfxSet.ImpactVfxKey     : DefaultImpactVfxKey;
                Color  castTint   = _typeVfxSet != null ? _typeVfxSet.RangedVfxTint : DefaultRangedVfxTint;
                // WO-956 faction gate: an ENEMY cast never flies the safe green hue (owner is
                // red/green colourblind). A data-authored green EnemyTypeVfxSet tint is
                // substituted with the hostile-palette placeholder + a FlowTrace warn inside.
                castTint = HostilePalette.EnforceOnTint(castTint, $"{_enemyId} ranged-cast tint");

                // Muzzle flash at the caster's hands (chest height, slightly ahead) as the orb releases.
                VFXManager.PlayKey(castKey,
                    transform.position + Vector3.up * 1.2f + transform.forward * 0.6f,
                    transform.rotation, null, castTint);

                System.Action land = () =>
                {
                    // The orb (ProjectileMover) can outlive this Enemy — despawn/teardown destroys
                    // the caster without setting _dead, and a destroyed component's transform throws
                    // (fleet 9200 NRE via NoteHeroDamageSource). Unity fake-null check catches it.
                    if (this == null || _dead || target == null) return;
                    // WO-VFX-RANGED: Hovl impact where the orb lands (reads even if the target just died).
                    VFXManager.PlayKey(impactKey, aim, Quaternion.identity, null, castTint);
                    var s = target.GetComponentInParent<IDamageableStructure>();
                    if (s != null && s.IsAlive)
                    {
                        NoteHeroDamageSource(s);
                        DealStructureDamage(s, damage);   // CITY-02: wall mitigates Heart hits
                        PlayTypeSound(_typeVfxSet != null ? _typeVfxSet.RandomAttackClip() : null);
                    }
                };
                // WO-VFX-RANGED: fly the Hovl orb muzzle→target (projKey travel); impactKey present
                // suppresses the old SpawnImpact inside RangedAttackVFX (the land closure fires it).
                if (vfx != null) vfx.FireSpellOrb(aim, land, projKey, impactKey, castTint);
                else land();
            }

            // Resume movement (only if WE rooted it — don't un-stop a contact-locked agent).
            bool casterResumed = _agent != null && _agent.isOnNavMesh && !wasStopped && _currentTarget == null;
            if (casterResumed)
                _agent.isStopped = false;

            // F8-38 gate-out (cast-end): record the final root state so the channel window is
            // bounded in the trace (pair with CAST-START + the DriveNav mid-cast line).
            DeNelle.Core.Diagnostics.FlowTrace.Step("EnemyCast",
                $"{_enemyId}: CAST-END resumedByCast={casterResumed} " +
                $"finalIsStopped={((_agent != null && _agent.isOnNavMesh) ? _agent.isStopped.ToString() : "n/a")}");

            // 2026-08-16: tear down the Casting_* wind-up loop + the target marker (a caster
            // destroyed mid-cast needs no call - the loop is parented and dies with the
            // hierarchy, and the marker carries a windup+1s auto-destroy safety net).
            CastingTelegraphVfx.End(this, windupTelegraph, "cast-released");
            CastingTelegraphVfx.EndTargetMarker(windupTargetMarker, "cast-released");

            // P4 cast seam — the wind-up is complete (orb released / damage committed).
            CastEnded?.Invoke(this);

            _casting = false;
        }

        /// <summary>
        /// Acquires the structure (or hero) this enemy should stop and strike.
        /// 1. FORWARD probe (legacy): the short SphereCast ahead — this is ALSO how a
        ///    brain-less enemy lands contact damage on the HERO (HeroHealth implements
        ///    IDamageableStructure), so it always runs and is returned first.
        /// 2. STRUCTURE-AWARENESS sweep (ff.enemystructureaware, DATA-PROVEN fix): when the
        ///    forward lane is clear AND the hero is NOT in aggro range, a short all-direction
        ///    sweep returns the nearest live SIEGE structure (side tower/wall, or the Heart
        ///    tree) so a brain-less enemy attacks a defence it would otherwise march straight
        ///    past — the [Flow:EnemyAggro] "no brain => forward ProbeForStructure only" root,
        ///    where the forward-only cast missed ~99.7%. HERO-PRIMARY is preserved: the sweep
        ///    is suppressed while the hero is engageable, and it never targets the hero.
        /// </summary>
        private IDamageableStructure ProbeForStructure()
        {
            // F8-41 gate-in: name the acquisition params so a capture shows the mask/radius the
            // whole structure-target probe uses (fwd SphereCast dist, all-direction sweep radius,
            // the layer mask, and whether the awareness flag routes us into the sweep at all).
            DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"probe-in-{_enemyId}", 2f,
                $"{_enemyId}: ProbeForStructure ENTRY fwdProbeDist={_contactProbeDistance:F1}m " +
                $"sweepRadius={_structureSweepRadius:F1}m mask=~0(all layers) " +
                $"awareFlag={DeNelle.Core.FeatureFlags.EnemyStructureAwareness} heart={(_heart != null)} " +
                // WO-1439: the acquisition params never named the ONE input that decides
                // friend from foe. Printing it here means a future capture answers
                // "did the faction test even have a faction?" without a code read.
                $"selfFaction={SelfFaction}");

            // 1. Legacy forward probe — keeps hero contact damage + path-blocking structures.
            IDamageableStructure forward = ProbeForStructureForward();
            if (forward != null) return forward;

            // Flag OFF => exact legacy (forward-only) behaviour — fully reversible.
            if (!DeNelle.Core.FeatureFlags.EnemyStructureAwareness)
            {
                // F8-41 root candidate: with the flag OFF the all-direction sweep never runs, so a
                // brain-less wave enemy only ever hits a structure it is LITERALLY facing -> marches
                // past every side defence. Once (not per-frame) — one line per enemy is enough proof.
                DeNelle.Core.Diagnostics.FlowTrace.Once("EnemyAggro", $"awareflag-off-{_enemyId}",
                    $"{_enemyId}: ff.enemystructureaware OFF -> forward-probe-only, structure sweep SKIPPED " +
                    "(F8-41 root candidate: no off-axis defence acquisition)");
                return null;
            }

            // HERO-PRIMARY: while the hero is near, keep chasing it — do NOT peel onto a
            // side structure. (A structure literally ahead was already returned above.)
            // SKIP-REASON instrumentation (§12, behaviour-NEUTRAL): the verify-capture showed
            // 0 sweep acquires; these traces PROVE which branch suppressed it so we read data,
            // not theory — "hero in aggro" vs "swept but nothing in range".
            if (IsHeroWithinAggro())
            {
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"skip-heroaggro-{_enemyId}", 1f,
                    $"{_enemyId}: structure sweep SUPPRESSED — hero within aggro (hero stays primary); forward-probe lane was clear");
                return null;
            }

            // 2. Hero not near => short all-direction sweep for the nearest live structure.
            var swept = SweepForNearestStructure();
            if (swept == null)
                // Once (not Throttle 1/sec): this inert-sweep signal was emitting ~3000 lines/run
                // (one per rep per second). Once collapses it to one line per enemy while keeping the
                // "feature still acquires nothing" diagnostic needed to finish verifying ff.enemystructureaware.
                DeNelle.Core.Diagnostics.FlowTrace.Once("EnemyAggro", $"skip-norange-{_enemyId}",
                    $"{_enemyId}: structure sweep RAN (hero not in aggro) but found NO live structure within " +
                    $"{Mathf.Max(_contactProbeDistance, _structureSweepRadius):F1}m");
            return swept;
        }

        /// <summary>
        /// Legacy forward SphereCast (extracted): the first live
        /// <see cref="IDamageableStructure"/> directly ahead, or null. Skirmishers probe
        /// slightly wider so they peel toward walls. This is the flag-OFF path AND the
        /// hero-chase path (the cast hits the hero's collider so contact damage lands).
        /// </summary>
        private IDamageableStructure ProbeForStructureForward()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 forward = transform.forward;
            float radius = _ai == EnemyAiKind.Skirmisher ? 0.6f : 0.4f;

            if (Physics.SphereCast(origin, radius, forward, out RaycastHit hit,
                    _contactProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                // The structure may host the interface on the collider's object
                // or a parent (the collider is often a child blocker).
                var structure = hit.collider.GetComponentInParent<IDamageableStructure>();
                // WO-1439 — FRIEND-OR-FOE. This lane used to accept on `!= null && IsAlive`
                // alone, which is how 11,620 `ProbeForStructure hit 'RaidSpire'` lines happened:
                // a Hostile garrison walked into its own Hostile objective and the probe said
                // yes. CombatFactionRules.MayAttack folds in the null + alive checks, so this is
                // the SAME three conditions plus the missing one — never a second copy of the
                // comparison (see CombatFactionRules' header on why that matters here).
                if (DeNelle.Core.Combat.CombatFactionRules.MayAttack(SelfFaction, structure))
                    return structure;
                // F8-41 gate: the forward cast HIT geometry but it carried no live structure —
                // name why (no interface on parent vs dead vs OUR OWN SIDE) so the null return
                // is not silent. The same-faction arm is WO-1439's proving line: before the fix
                // there was no branch that could ever print it.
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"fwd-reject-{_enemyId}", 2f,
                    $"{_enemyId}: forward SphereCast HIT '{hit.collider.name}' but rejected " +
                    // NOTE (CLI, at the gate 2026-09-06): this ternary was authored across three
                    // lines INSIDE a non-verbatim interpolated string, which is CS8967 under the
                    // project's C# 9 language version. Folded onto one line - the emitted text is
                    // byte-identical, only the source layout changed.
                    $"({(structure == null ? "no IDamageableStructure on parent" : !structure.IsAlive ? "structure is DEAD" : $"SAME FACTION ({structure.Faction}) as attacker - friendly fire refused")}) " +
                    "-> no forward target");
            }
            return null;
        }

        /// <summary>
        /// True when the hero is currently within this enemy's aggro range (generous: out
        /// to the drop margin so we don't peel onto a structure right at the chase edge).
        /// Reads the cached hero ref (refreshed by DriveNav.ResolveHeroTransform ~1/sec);
        /// re-resolves cheaply if it's gone so a late/streamed hero still suppresses the
        /// sweep. Used to keep the hero the PRIMARY target over the structure sweep.
        /// </summary>
        private bool IsHeroWithinAggro()
        {
            if (_heroAggroRadius <= 0f) return false;          // aggro disabled => sweep is free to fire
            if (_heroTransform == null) ResolveHeroTransform();
            if (_heroTransform == null || !_heroTransform.gameObject.activeInHierarchy) return false;

            float r = _heroAggroRadius + _heroAggroDropMargin; // don't peel near the chase edge
            float planarSqr = Vector3.ProjectOnPlane(
                _heroTransform.position - transform.position, Vector3.up).sqrMagnitude;
            return planarSqr <= r * r;
        }

        /// <summary>
        /// Structure-awareness fix: nearest live SIEGE <see cref="IDamageableStructure"/>
        /// within <see cref="_structureSweepRadius"/> in ANY direction (the "short sweep"
        /// that replaces the forward-only miss). Excludes the hero (handled by the aggro
        /// path) so the enemy never treats the hero as a siege target here. Reuses
        /// <see cref="_structureScanBuffer"/> (no per-tick alloc). Traces the acquire on
        /// [Flow:EnemyAggro] so a capture PROVES the fix (probe-fail count drops + a
        /// "ranged sweep acquired" line appears).
        /// </summary>
        private IDamageableStructure SweepForNearestStructure()
        {
            float radius = Mathf.Max(_contactProbeDistance, _structureSweepRadius);
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            int count = Physics.OverlapSphereNonAlloc(
                origin, radius, _structureScanBuffer, ~0, QueryTriggerInteraction.Ignore);

            IDamageableStructure nearest = null;
            float bestScore = float.MinValue;
            // F8-41 reject tally — split silent `continue`s into named reasons so a capture shows
            // WHY the sweep found nothing: count=0 (radius too small / no colliders) vs all-filtered
            // (only hero/dead/no-component in range). Behaviour-neutral: same accepts, same `nearest`.
            // WO-1439 adds rejFaction. That tally is what PROVED this ticket: the pre-fix
            // capture read `rejected[null=0,noStructComp=1,dead=0,hero=0] nearest=RaidSpire`,
            // and because the tally enumerates every filter the loop has, the absence of a
            // faction bucket IS the absence of the test. Keep new filters visible here.
            int rejNull = 0, rejNoComp = 0, rejDead = 0, rejHero = 0, rejFaction = 0, accepted = 0;
            for (int i = 0; i < count; i++)
            {
                var c = _structureScanBuffer[i];
                if (c == null) { rejNull++; continue; }
                var structure = c.GetComponentInParent<IDamageableStructure>();
                if (structure == null) { rejNoComp++; continue; }
                if (!structure.IsAlive) { rejDead++; continue; }
                // The hero implements IDamageableStructure — never grab it via the siege
                // sweep (the verified hero-aggro path owns hero engagement).
                if (structure is HeroHealth) { rejHero++; continue; }
                // WO-1439 — FRIEND-OR-FOE, the filter this loop never had. Checked AFTER the
                // hero arm deliberately: the hero is Friendly to a Hostile sweeper and would
                // otherwise be counted as a faction reject, which would blur the one signal
                // this tally exists to give. Same predicate as the forward lane; no second copy.
                if (!DeNelle.Core.Combat.CombatFactionRules.MayAttack(SelfFaction, structure))
                { rejFaction++; continue; }
                accepted++;
                float sqr = (c.transform.position - transform.position).sqrMagnitude;
                float dist = Mathf.Sqrt(sqr);
                float normDist = radius > 0.01f ? Mathf.Clamp01(dist / radius) : 0f;
                var loot = structure as ISiegeLootTarget;
                float role = loot != null && loot.IsLootTargetAlive ? loot.SiegeRoleValue : 0.3f;
                float score = role * (1f - normDist * 0.35f);
                if (score > bestScore) { bestScore = score; nearest = structure; }
            }

            // F8-41 gate: name the scan outcome every ~2s per enemy. This is the line that PROVES
            // whether "no structure target" is data-empty (colliders=0) or all-rejected, and which
            // filter did the rejecting.
            DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"sweep-scan-{_enemyId}", 2f,
                $"{_enemyId}: sweep OverlapSphere r={radius:F1}m colliders={count} -> accepted={accepted} " +
                $"rejected[null={rejNull},noStructComp={rejNoComp},dead={rejDead},hero={rejHero}," +
                $"sameFaction={rejFaction}] self={SelfFaction} " +
                $"nearest={((nearest as MonoBehaviour)?.name ?? "<null>")}");

            if (nearest != null)
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"sweep-{_enemyId}", 1f,
                    $"{_enemyId}: ranged sweep acquired structure '{(nearest as MonoBehaviour)?.name}' " +
                    $"within {radius:F1}m (hero not in aggro) -> stopping to attack " +
                    "(was: ProbeForStructure forward-only miss)");
            return nearest;
        }

        // ---------------------------------------------------------------------
        // HP / death
        // ---------------------------------------------------------------------

        /// <summary>
        /// Applies <paramref name="amount"/> damage. At zero HP the enemy dies,
        /// raises <see cref="Died"/> and is destroyed. Hero abilities, pets and
        /// towers route their damage through here.
        /// </summary>
        // Colour stamped by the damage source (hero / pet) for the NEXT number only;
        // consumed and cleared in TakeDamageFrom. Null = magnitude-based default colour.
        private Color? _nextNumberTint;

        /// <summary>Source-tint the next floating damage number (see IDamageTintable).</summary>
        public void SetNextDamageTint(Color color) => _nextNumberTint = color;

        // WO-219: element stamped by the damage source (spell / weapon) for the NEXT hit
        // only; consumed and cleared in TakeDamageFrom so the impact burst is tinted by
        // element (flame / ice / aether) instead of the generic grey physical spark.
        // Null = physical (the existing VfxPool / Impact_Physical fallback).
        private DamageElement? _nextImpactElement;

        /// <summary>WO-219: element-tint the impact VFX for the NEXT hit (spell hits read
        /// flame/ice/aether; melee stays physical). Set by EnemyDamageable.TakeDamage.</summary>
        public void SetNextImpactElement(DamageElement element) => _nextImpactElement = element;

        // Ticket #61: stamped TRUE by the HERO's attack/ability paths (via
        // EnemyDamageable.MarkNextHitFromHero) for the NEXT hit only; consumed in
        // TakeDamageFrom. Gates the combo / kill-streak / RAMPAGE feedback so tower,
        // pet, DoT and environmental damage NEVER drive the combo. Default false =
        // non-hero (only the hero stamps it true).
        private bool _nextDealtByHero;

        /// <summary>Ticket #61: mark the NEXT hit as hero-dealt so its combo/streak/RAMPAGE
        /// feedback fires. Set by EnemyDamageable.MarkNextHitFromHero (hero paths only).</summary>
        public void SetNextDealtByHero(bool value) => _nextDealtByHero = value;

        /// <summary>
        /// MONOTONIC running total of damage the HERO has dealt to enemies this play session.
        /// <para>
        /// There was no aggregate anywhere for "damage the player dealt" -- DamageAttribution
        /// only sees the ability paths that call Record() (basic melee never does) and is
        /// drained per-target on death, so it can never answer "how much did the player deal
        /// during THIS wave". The one seam that already knows a hit came from the hero, on
        /// every hero path (HeroAbilities AND PlayerAttackController, both via
        /// EnemyDamageable.MarkNextHitFromHero), is the <c>_nextDealtByHero</c> stamp consumed
        /// one line into TakeDamageFrom. This counter hangs off that existing stamp; no new
        /// event plumbing, no per-hit allocation.
        /// </para>
        /// Consumers take a SNAPSHOT and subtract (WaveManager's per-wave encounter sample),
        /// so the absolute value never matters -- only the delta. double, not float, so a long
        /// session cannot lose precision in the low bits. Damage dealt to the apex DragonBoss
        /// is NOT counted (it is not an Enemy and has no hero stamp).
        /// </summary>
        public static double HeroDamageDealtTotal { get; private set; }

        // Domain reload may be disabled (WO-139 #12): statics survive Play sessions. Zero the
        // counter at each play start so a fresh session starts from a clean absolute value.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHeroDamageDealtTotal() => HeroDamageDealtTotal = 0d;

        public void TakeDamage(float amount)
        {
            // No source position — default flinch direction is Front (attacker
            // presumed to be in front of the enemy; the Nav destination is always
            // ahead, so most hits do come from that direction).
            TakeDamageFrom(amount, transform.position + transform.forward * 5f);
        }

        /// <summary>
        /// DEF-46: Directional overload — takes the world-space position of the
        /// damage source so the flinch animation matches the hit direction.
        /// Called directly by anything that knows its own world position.
        /// </summary>
        public void TakeDamageFrom(float amount, Vector3 sourceWorldPos)
        {
            if (_dead || amount <= 0f) return;

            if (_telegraphing)
            {
                _contactCommitInterrupted = true;
                _contactCommitPending = false;
            }

            // WO-910 Hunter's Mark: marked foes take amplified damage.
            amount = CombatMark.ScaleDamage(this, amount);

            // Ticket #61: consume the hero-source stamp ONCE for this hit. Only the hero's
            // attack/ability paths stamp it true (via EnemyDamageable.MarkNextHitFromHero);
            // tower / pet / DoT / environment leave it false. Gates the combo / kill-streak /
            // RAMPAGE feedback below (and the kill feedback inside Die) to hero strikes only.
            bool dealtByHero = _nextDealtByHero;
            _nextDealtByHero = false;

            // ENCOUNTER TELEMETRY (dynamic difficulty): accumulate the damage the HERO
            // actually landed. Clamped to the HP that was really there, so overkill on the
            // killing blow cannot inflate the player's "dominating" reading. Reads the same
            // stamp the combo gate above consumes -- one seam, two consumers.
            if (dealtByHero)
            {
                float applied = amount < _hp ? amount : _hp;
                if (applied > 0f) HeroDamageDealtTotal += applied;
            }

            // Floating combat text — pop the damage number at the enemy's head so
            // the player can see the hit (and watch it rise after a damage talent).
            // Spawned BEFORE death so the killing blow still shows its number even
            // though this GameObject may be destroyed below. A source may have
            // stamped a tint (hero vs pet) for this hit — consume it once.
            if (_nextNumberTint.HasValue)
            {
                DamageNumberSpawner.Spawn(amount, HeadWorldPosition(), _nextNumberTint.Value);
                _nextNumberTint = null;
            }
            else
            {
                DamageNumberSpawner.Spawn(amount, HeadWorldPosition());
            }

            _hp = Mathf.Max(0f, _hp - amount);
            if (_hp <= 0f)
            {
                Die(killed: true, dealtByHero: dealtByHero);
            }
            else
            {
                // Retaliate: notify the brain it was struck so it aggros the
                // attacker (regardless of engage radius / role / behaviour tree).
                Damaged?.Invoke(sourceWorldPos);

                // DEF-46: compute cardinal hit direction from source and drive the
                // directional flinch sub-state before firing the Hit trigger.
                HitDirection dir = ComputeHitDirection(sourceWorldPos);
                if (_animator != null)
                {
                    if (_hasHitDirParam) _animator.SetInteger(AnimHitDir, (int)dir);
                    if (_hasHitParam)    _animator.SetTrigger(AnimHit);
                }

                // DEF-46: per-type hit VFX — use SO prefab when assigned, fall
                // back to the procedural VfxPool burst otherwise.
                // WO-219: when the damage source stamped an element (a spell hit),
                // route through the existing VFXManager element impacts so flame /
                // ice / aether hits read distinctly. Melee (null element) keeps the
                // original SO-prefab / VfxPool grey physical spark. Consume once.
                Vector3 hitPos = transform.position + Vector3.up * 0.6f;
                DamageElement? impactElement = _nextImpactElement;
                _nextImpactElement = null;
                VFXType elementImpact = ImpactVfxFor(impactElement);
                if (elementImpact != VFXType.None)
                {
                    VFXManager.Play(elementImpact, hitPos);
                }
                else
                {
                    GameObject hitPrefab = _typeVfxSet != null ? _typeVfxSet.RandomHitVfxPrefab() : null;
                    if (hitPrefab != null)
                    {
                        // LEAK FIX (#3): the SO hit-VFX prefab was Instantiated with NO
                        // Destroy — every non-lethal hit leaked a GameObject forever
                        // (a HARD leak; the procedural VfxPool path self-returns). Give
                        // the spawned VFX a bounded lifetime so it self-destructs.
                        var hitGo = Instantiate(hitPrefab, hitPos, Quaternion.identity);
                        if (hitGo != null) Destroy(hitGo, TypeVfxSelfDestructSeconds);
                    }
                    else
                        VfxPool.SpawnHitImpact(hitPos);
                }

                // Combat feel: blink the enemy red on the hit (additive, null-safe).
                _hitReaction?.Flash();

                // WO-84: heavy-hit path — bigger shake + explosion VFX on large hits.
                if (amount >= _heavyHitThreshold)
                    CameraShakeBridge.Shake(0.32f, 0.22f);   // heavier than the per-hit default

                // DEF-46: per-type hit audio. WO-220: fall back to a generated hit
                // SFX (via CoreServices.Audio) when no type-set clip is authored.
                PlayTypeSound(_typeVfxSet != null ? _typeVfxSet.RandomHitClip() : null,
                              CombatSfxFallback.Hit);

                // DEF-44/45: hit-stop + combo counter. Ticket #61: HERO hits ONLY — tower /
                // pet / DoT / environment hits must NOT drive the combo / RAMPAGE feedback.
                DeNelle.Core.Diagnostics.FlowTrace.Step("Combat",
                    "CombatFeedback Hit gated: dealtByHero=" + dealtByHero + " amount=" + amount);
                if (dealtByHero)
                    CombatFeedbackManager.Hit(hitPos, amount);
            }
        }

        /// <summary>
        /// WO-566: talent on-hit DoT (Knight Emberbrand Strike burn / ranger Poison Tip bleed).
        /// Applies <paramref name="dps"/> damage per second for <paramref name="duration"/> seconds
        /// as 1-second ticks, each routed through <see cref="TakeDamageFrom"/> so every tick shows a
        /// number + flinches toward the source. Data-driven (the caller passes the node's value /
        /// duration). No-op on a dead enemy / non-positive params. A re-proc simply runs a second
        /// concurrent burn (cheap; bounded by duration).
        /// </summary>
        public void ApplyDamageOverTime(float dps, float duration, Vector3 sourceWorldPos)
        {
            if (_dead || dps <= 0f || duration <= 0f) return;
            StartCoroutine(DamageOverTimeRoutine(dps, duration, sourceWorldPos));
        }

        private System.Collections.IEnumerator DamageOverTimeRoutine(float dps, float duration, Vector3 sourceWorldPos)
        {
            float remaining = duration;
            while (remaining > 0f && !_dead)
            {
                yield return new WaitForSeconds(1f);
                if (_dead) yield break;
                remaining -= 1f;
                TakeDamageFrom(dps, sourceWorldPos);
            }
        }

        /// <summary>
        /// DEF-46: Maps a world-space source position onto the enemy's local axes
        /// to determine which of the four cardinal quadrants the hit came from.
        /// </summary>
        private HitDirection ComputeHitDirection(Vector3 sourceWorldPos)
        {
            Vector3 toSource = (sourceWorldPos - transform.position);
            toSource.y = 0f;
            if (toSource.sqrMagnitude < 0.01f) return HitDirection.Front;

            // Project onto the enemy's forward/right to get local [-1..1] coords.
            float fwdDot   = Vector3.Dot(toSource.normalized, transform.forward);
            float rightDot = Vector3.Dot(toSource.normalized, transform.right);

            // Dominant axis picks Front/Back/Left/Right.
            if (Mathf.Abs(fwdDot) >= Mathf.Abs(rightDot))
                return fwdDot >= 0f ? HitDirection.Front : HitDirection.Back;
            else
                return rightDot >= 0f ? HitDirection.Right : HitDirection.Left;
        }

        /// <summary>Kills the enemy immediately (e.g. consumed into an ATB breach).</summary>
        public void Kill()
        {
            if (!_dead) Die(killed: false);
        }

        // =====================================================================
        //  Pooling reset contract (EnemyPool) — EXHAUSTIVE on purpose.
        //  A missed reset = an enemy that spawns dead / untargetable / with stale
        //  HP / double-subscribed events. RELEASE tears the body down to a clean
        //  dormant state; REUSE re-arms it for a fresh spawn (the spawner then
        //  calls Configure(), which re-seeds stats / agent / id as it always has).
        // =====================================================================

        /// <summary>
        /// RELEASE side — called by <see cref="Die"/> right before the body returns
        /// to the <see cref="EnemyPool"/>. Drops EVERYTHING that must not survive into
        /// the next reuse: live coroutines, the targeting registry membership, the
        /// damage-attribution ledger (keyed on BOTH this Enemy and its EnemyDamageable
        /// — the hero/pet record against the adapter, ProgressionManager drains against
        /// the Enemy, so both buckets must be forgotten or they leak across reuses),
        /// the brain nav overrides, the contact target, status timers and VFX/tint
        /// state. The GameObject is left ACTIVE here; the pool deactivates it (so the
        /// death-hold animation has already played by the time Die calls us).
        /// </summary>
        public void ResetForPool()
        {
            // 1. Stop every coroutine this body started (telegraph wind-up, secondary
            //    death burst, hit-flash is owned by EnemyHitReaction's OnDisable).
            StopAllCoroutines();
            _telegraphing = false;
            // POOL-RESET AUDIT (2026-08-02, P0-1): StopAllCoroutines KILLS RootedCast before its
            // last line (_casting = false) ever runs, so a caster killed mid-wind-up (>= 1.0s, the
            // common case) was pooled with _casting == true. On reuse DriveNav's `if (_casting)
            // { isStopped = true; return; }` fired EVERY frame FOREVER -> a permanent statue that
            // never moved or sieged, and RangedAttack's `&& !_casting` silently reverted it to the
            // un-telegraphed instant hit. Clear it here (and in Die + PrepareForReuse).
            _casting = false;

            // 2. Leave the targeting registry so the reticle/towers/pets can't pick a
            //    pooled body. (Die already unregistered on death; idempotent here.)
            TargetManager.Unregister(this);

            // 3. Forget the damage ledger under BOTH keys (see summary) so a reused
            //    body never inherits a previous life's attribution. ReportKill already
            //    Drained this Enemy on a real kill; Forget is idempotent + also clears
            //    the adapter-keyed bucket that Record() actually wrote to.
            DeNelle.Core.Combat.DamageAttribution.Forget(this);
            var dmg = GetComponent<EnemyDamageable>();
            if (dmg != null) DeNelle.Core.Combat.DamageAttribution.Forget(dmg);

            // 4. Halt + detach the agent so a dormant body never paths.
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;

            // 5. POOL-RESET AUDIT (2026-08-02): EVERY per-life latch/cache on this class —
            //    the AI/nav overrides, the hero-aggro seam, the next-hit VFX/tint stamps and
            //    the motion latches — in ONE place. They used to be spelled out separately here
            //    AND again in PrepareForReuse, and that duplicated-list-that-drifts is EXACTLY
            //    the defect that produced P0-1 (_casting was added to the class and to neither
            //    list). One method, both callers, one thing for the oracle to pin.
            ClearPooledLatches();

            // 6. POOL-RESET AUDIT (P0-2): the sibling EnemyBrain owns its own latch set
            //    (tactics / role / leash / room AABB / coordinated flank / provoke / taunt)
            //    that NOTHING reset — a body that once served as a caster kept KiterTactics
            //    forever and, reused as a Tank, stood off at 10 m and refused to close.
            ResolveBrain()?.ResetForPool();

            // 7. F8 seq 652: drop the world-space FX ribbons on the RELEASE side too, so a body
            //    parked in the pool carries no vertices from its previous life even if it is later
            //    re-homed by a path that does not run PrepareForReuse.
            ClearFxTrails();
        }

        /// <summary>
        /// F8 seq 652 (giant pale "rod"): wipes the world-space vertex history of every
        /// <see cref="TrailRenderer"/> under this body. TrailRenderer records in WORLD space, so a
        /// pooled body that teleports to a new spawn point draws one continuous ribbon spanning the
        /// jump. Called from BOTH pool sides (release <see cref="ResetForPool"/> and acquire
        /// <see cref="PrepareForReuse"/>, after the Warp) — one method, both callers, matching the
        /// ClearPooledLatches doctrine so the two lists cannot drift apart.
        /// </summary>
        private void ClearFxTrails()
        {
            var trails = GetComponentsInChildren<TrailRenderer>(true);
            if (trails == null) return;
            for (int i = 0; i < trails.Length; i++) trails[i]?.Clear();
        }

        /// <summary>
        /// POOL-RESET AUDIT (2026-08-02): the latches shared by BOTH pool-reset sides. Every
        /// field here is state that a body accumulated during its previous life and that the
        /// spawner does NOT re-stamp on reuse, so leaving any of them set is a live bug:
        ///   • <c>_casting</c>            — permanent statue (see ResetForPool step 1).
        ///   • <c>_telegraphing</c>       — blocks every future melee wind-up (double-trigger guard).
        ///   • <c>_stopTightenedForHero</c> — Configure re-writes agent.stoppingDistance to the 2.5 m
        ///     siege radius but this latch stayed TRUE, so the "chasingHero != latch" edge in
        ///     DriveNav never fired again: the reused body halted 2.5 m from the hero, OUTSIDE
        ///     HeroHealth's 1.5 m engage ring, and never landed a hit ("enemies just mill around").
        ///   • <c>_presentationCombat</c> — an overworld rep's braced combat locomotion leaking
        ///     into a calm village marcher.
        ///   • <c>_hasFaceTarget</c>      — a stale facing request from the previous life.
        ///   • <c>_hasLastAnimPos</c>     — the anim speed feed differencing against a position on
        ///     the OTHER side of the map produces a one-frame sprint spike on every reuse.
        ///   • <c>_heroAggroRadius</c>    — see <see cref="_authoredHeroAggroRadius"/>.
        ///   • the AI/nav seam, hero-aggro seam and next-hit stamps, which used to be spelled
        ///     out separately in ResetForPool AND PrepareForReuse (two lists, drifting apart).
        /// NOT reset here, deliberately: <c>_poolKey</c> (the queue identity — clearing it is the
        /// P1-7 leak), <c>_authoredHeroAggroRadius</c> (the snapshot itself), <c>_hp</c>/<c>_maxHp</c>
        /// (PrepareForReuse revives to full, then Configure re-seeds from the def), and the
        /// same-GameObject component caches (_agent/_animator/_actor/_brain/... — they survive
        /// pooling by design, which is the whole point of pooling the body).
        /// </summary>
        private void ClearPooledLatches()
        {
            // WO-893 spawn tell. Set by Configure, consumed on the first Update after it,
            // so a recycled body that was returned to the pool BEFORE its tell fired would
            // otherwise carry the pending flag into its next life and play a spawn burst
            // for an enemy that did not just spawn. This is the exact latch-survives-pooling
            // shape the coverage guard exists to catch, and it caught this one.
            _spawnTellPending      = false;

            // Combat / motion latches.
            // A pooled committer must surrender its director token before its local
            // bit is cleared, otherwise the director permanently believes a dead body
            // still owns capacity and future packs stop attacking.
            ReleaseAttackToken();
            _attackTokenHeld        = false;
            _contactCommitPending   = false;
            _contactCommitInterrupted = false;
            _contactCommitTarget    = null;
            _casting               = false;
            _telegraphing          = false;
            _stopTightenedForHero  = false;
            _presentationCombat    = false;
            _hasFaceTarget         = false;
            _faceTargetDir         = Vector3.forward;
            _hasLastAnimPos        = false;
            _lastAnimPos           = Vector3.zero;
            _animSpeedSmoothed     = 0f;
            _scannedAnimatorController = null; // force a fresh parameter scan on reuse
            _navWarned             = false;
            _attackCooldown        = 0f;

            // WO-1439 — the SelfFaction cache. A GetComponent result held on a POOLED object is
            // the textbook latch: PrepareForReuse revives an instance rather than rebuilding it,
            // so this reference would ride into whatever enemy takes the slot next. Two ways it
            // bites, and the second is the bad one: a stale ref to a destroyed adapter (fake-null,
            // so SelfFaction silently falls back to Hostile), or the WRONG FACTION ANSWER carried
            // across a reuse — which would silently re-open the very friendly-fire hole this
            // ticket closed, and re-open it INTERMITTENTLY, only on reused bodies. Cleared, not
            // exempted: the property re-resolves lazily on first read, so the cost of clearing is
            // one GetComponent per life and the cost of not clearing is a returning P0.
            _selfDamageable        = null;

            // AI / nav seam — the reused body re-acquires from scratch (the brain re-scores
            // on its own interval; these clear the Enemy-side half of that seam).
            _brainTarget           = null;
            _brainPositionOverride = null;
            _currentTarget         = null;
            // WO-1450: a reused body probes on its FIRST live frame (0 is always <= Time.time)
            // and its first acquire is a genuine CHANGE — never inherit the dead body's target
            // id, which would swallow the acquire trace for the new one.
            _nextProbeAt           = 0f;
            _lastProbeTargetId     = 0;

            // Hero-aggro seam + the hero-only-duel battle-lock membership. _engagedLatched is
            // primarily released by OnDisable (which the pool triggers via SetActive(false)),
            // but a Release that never deactivates would otherwise wedge BattleLock true.
            _heroTransform         = null;
            _heroAggroEngaged      = false;
            _heroResolveTimer      = 0f;
            _engageBrainResolved   = false;
            _engageBrain           = null;
            _engagedLatched        = false;
            if (_authoredHeroAggroRadius >= 0f) _heroAggroRadius = _authoredHeroAggroRadius;

            // Next-hit VFX / tint stamps — a reused body starts neutral.
            _nextNumberTint        = null;
            _nextImpactElement     = null;
            _nextDealtByHero       = false;   // ticket #61: don't leak a hero stamp across pooling

            // Path throttle — the first live frame re-paths immediately.
            _pathRefreshTimer      = 0f;
            _lastPathedDestination = Vector3.zero;

            // Dynamic-difficulty base capture — the single most important thing on this
            // list. A base left set from the PREVIOUS life would be multiplied again on
            // the next spawn, which is the compounding bug the base+apply contract exists
            // to prevent. -1 = uncaptured, so ApplyDifficulty no-ops until the spawner
            // re-captures for the new spawn. Cleared on BOTH pool-reset sides because both
            // ResetForPool (release) and PrepareForReuse (acquire) call this method.
            _baseMaxHp             = -1f;
            _baseContactDamage     = -1f;
        }

        /// <summary>
        /// ACQUIRE side — called by <see cref="EnemyPool.Get"/> on a REUSED body right
        /// after it is re-enabled + re-placed. Re-arms the body to a live, full-HP,
        /// targetable, animating state. Stat-specific re-seeding (HP from def, agent
        /// speed, id) is then done by the spawner's <see cref="Configure"/> call (and
        /// <see cref="ApplyWaveScaling"/>) exactly as on a fresh spawn — so this method
        /// only undoes the DEATH state the body was pooled in.
        /// </summary>
        /// <param name="pos">Re-spawn position (already NavMesh-snapped by the pool).</param>
        /// <param name="rot">Re-spawn rotation.</param>
        public void PrepareForReuse(Vector3 pos, Quaternion rot)
        {
            // Resolve refs (the body's components persist across pooling, but re-cache
            // defensively in case anything was lost — mirrors Awake).
            EnsureAgent();
            EnsureAnimator();
            EnsureAudio();
            EnsureHitReaction();

            // 1. Clear the dead latch FIRST so Update()/targeting see a live enemy.
            _dead = false;
            // POOL-RESET AUDIT (2026-08-02): the ACQUIRE side must clear the same latch set as the
            // release side. A body can reach the pool without ResetForPool having taken (a forced
            // removal, a Release from a future call site, an editor-time re-Get), and this is the
            // side a spawner re-stamps immediately after — so it is the one the oracle pins.
            // Covers _casting (the permanent-statue latch), _telegraphing, _stopTightenedForHero,
            // _presentationCombat, the anim-feed and face-target caches, and the hero-aggro radius.
            ClearPooledLatches();

            // 2. Restore HP to full so the body isn't handed out at 0 HP / instantly
            //    re-dead. Configure() re-seeds _maxHp from the def right after this;
            //    we set _hp = _maxHp so there is never a 1-frame zero-HP window.
            _hp = _maxHp;

            // 3. Reset the Animator out of its latched Death state (the Dead bool is a
            //    sticky latch — without clearing it the reused body stays collapsed).
            _actor?.Revive();
            if (_animator != null)
            {
                if (_hasDeadParam) _animator.SetBool(AnimDead, false);
                if (_hasSpeedParam) _animator.SetFloat(AnimSpeed, 0f);
                _animSpeedSmoothed = 0f; // anti-chop feed restarts clean on revive
                // Rebind drops any in-flight death-clip pose so the controller restarts
                // clean at its default (idle) state on the next frame.
                if (_animator.runtimeAnimatorController != null && _animator.isActiveAndEnabled)
                    _animator.Rebind();
            }

            // 4. Re-place + warp the agent onto the NavMesh and re-enable pathing so a
            //    reused body actually moves (Warp is the supported way to teleport a
            //    NavMeshAgent; SetPosition alone desyncs the internal agent state).
            if (_agent != null)
            {
                // #55: Die() disabled the agent + released transform ownership so the corpse could
                // settle onto the ground. Restore both BEFORE re-placing it (Warp requires the agent
                // enabled + on navmesh) so the reused body owns its transform and paths normally again.
                if (!_agent.enabled) _agent.enabled = true;
                _agent.updatePosition = true;
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                _agent.Warp(pos);
                if (_agent.isOnNavMesh) _agent.isStopped = false;
            }

            // 4b. F8 seq 652 (giant pale "rod" on ArenaEnemy_orc-shaman_1): a TrailRenderer stores
            //     its vertices in WORLD space, so the Warp above (or the pool's own re-place on an
            //     agent-less body) leaves the previous life's vertices behind and the ribbon draws
            //     across the whole teleport — the captured footGap oscillated 47->95 m frame to
            //     frame, which no static mesh can do. Clear AFTER the re-home so the next recorded
            //     vertex starts at the new position. Runs outside the _agent block because the pool
            //     re-places agent-less bodies too. Same release contract MoverProjectilePool applies.
            ClearFxTrails();

            // 5. Status timers (freeze/slow/burn) live on EnemyDamageable and key off
            //    Time.time + seconds; by the time a body is reused, Time.time has long
            //    passed any expiry set on its previous life, so IsFrozen/IsSlowed/
            //    IsBurning already read false. No explicit clear needed for the normal
            //    case. (Edge case: a multi-minute status applied the frame before death
            //    could carry over — flagged for the owner playtest.)

            // 6. Re-register with the targeting registry. OnEnable already fired
            //    Register when the pool SetActive(true)'d us, but Register dedups, so
            //    this belt-and-braces a body whose OnEnable ran before _dead cleared.
            TargetManager.Register(this);

            // 7. (The path throttle is reset by ClearPooledLatches above — single authority.)

            // 8. POOL-RESET AUDIT (P0-2): wipe the sibling EnemyBrain's per-life state too. Done
            //    on the ACQUIRE side (as well as release) because this runs BEFORE the spawner's
            //    Configure + Role/tactics stamp, so the spawner's stamp always lands on a clean
            //    brain — a body that once carried a dungeon leash or arena hero-only flag can
            //    never drag it into a village wave.
            ResolveBrain()?.ResetForPool();
        }

        /// <param name="killed">
        /// True when HP reached zero (a real defender kill — grants shared XP);
        /// false when force-removed (ATB breach) — no XP, just drop its ledger.
        /// </param>
        /// <param name="dealtByHero">
        /// Ticket #61: true only when the KILLING blow came from the player's HERO. Gates
        /// the combo / kill-streak / RAMPAGE feedback (CombatFeedbackManager.Kill) to hero
        /// kills only — tower / pet / DoT / environment kills still die + drop loot + grant
        /// XP, they just don't feed the combo. Forced removals default to non-hero.
        /// </param>
        private void Die(bool killed, bool dealtByHero = false)
        {
            _dead = true;
            _telegraphing = false;   // audit 2026-05-30: clear the wind-up latch on death (safe for future pooling)
            // POOL-RESET AUDIT (2026-08-02, P0-1): the 2026-05-30 audit cleared the wind-up latch
            // here but MISSED its twin. _casting is set by RootedCast and cleared ONLY on that
            // coroutine's last line — so a caster killed mid-wind-up died with _casting still true,
            // and the DEATH-HOLD frames (up to DeathHoldSeconds) then ran DriveNav's
            // `if (_casting) { isStopped = true; return; }` on a corpse. Cleared here as well as in
            // ResetForPool so the latch is dead the instant HP hits zero, not a second later.
            _casting = false;
            // HUD posture (owner 2026-07-09): drop THIS enemy's pursuit pulse immediately so
            // town/overworld chrome returns as soon as the last threat dies — not after PursuitTtl.
            // Other live pursuers keep hostile(prebattle) up via their own pulses.
            DeNelle.Core.HudModel.PostureSignals.RevokePursuit(GetInstanceID());
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;

            // #55: a live NavMeshAgent OWNS the transform (updatePosition) and re-writes the
            // corpse to navmesh/agent height every frame — reverting SnapBodyToGround so the body
            // "floats" at agent height through the death hold. Release transform ownership + disable
            // the agent on death so the grounded snap AND the per-frame 1/2-raycast settle (in
            // ReturnToPoolAfterDeathHold) hold. Restored in PrepareForReuse before the pool re-Warps.
            if (_agent != null)
            {
                _agent.updatePosition = false;
                if (_agent.isActiveAndEnabled) _agent.enabled = false;
            }

            // WO-531: snap the body down to the ground the instant it dies so the corpse
            // (and every position-derived effect below — deathPos VFX, scorch decal,
            // CombatFeedbackManager.Kill) sits ON the ground instead of floating at the
            // Y it died at (e.g. mid-air over a wall top). Done FIRST so all downstream
            // transform.position reads pick up the grounded position.
            SnapBodyToGround();
            // WO-1450 §2: PERMANENT drop-path trace, third and last of the three
            // `_currentTarget = null` sites. This one is TERMINAL — the enemy itself died,
            // so it can never feed a re-acquisition thrash. Naming it anyway is the point:
            // a capture that shows this line is proof the thrash is NOT coming from here,
            // which is exactly the elimination a static read could not make.
            DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", $"drop-death-{_enemyId}", 1f,
                $"{_enemyId}: target RELEASED — path=self-death (terminal, no re-acquire)");
            _currentTarget = null;
            _lastProbeTargetId = 0;
            TargetManager.Unregister(this);   // drop from targeting the instant it dies

            // Drive death anim (latches Dead bool so last frame holds; see ActorAnimator + controllers).
            _actor?.Die();
            // PERMANENT live instrumentation (owner steer 2026-06-23 "enemy animation in effect"):
            // emit AT the point the Dead state is driven so a kill self-PROVES the death animation
            // FIRED at runtime (headless encounter run / F8 break-log) — not inferred from the asset existing.
            DeNelle.Core.Diagnostics.FlowTrace.Step("Enemy", "DEATH ANIM playing actor=" + gameObject.name + " state=Dead");
            Died?.Invoke(this);

            // DEF-52 / DEF-46: death burst VFX + audio + micro screen shake.
            // DEF-45: kill streak tracked via CombatFeedbackManager.
            if (killed)
            {
                Vector3 deathPos = transform.position + Vector3.up * 0.5f;

                // WO-66: elite/boss enemies use EliteVFXController for death VFX.
                // Falls through to the normal per-type SO / VfxPool path for regular enemies.
                var eliteVfx = GetComponent<EliteVFXController>();
                if (eliteVfx != null)
                {
                    eliteVfx.OnEliteDeath();
                }
                else
                {
                    // WO-84: use the per-prefab deathVFXOverride when set; otherwise fall
                    // back to the SO prefab pool, then the procedural VfxPool burst.
                    if (_deathVFXOverride != VFXType.Death_Generic && _deathVFXOverride != VFXType.None)
                    {
                        VFXManager.Play(_deathVFXOverride, deathPos);
                    }
                    else
                    {
                        // DEF-46: per-type death VFX — SO prefab when assigned, else VfxPool.
                        GameObject deathPrefab = _typeVfxSet != null ? _typeVfxSet.RandomDeathVfxPrefab() : null;
                        if (deathPrefab != null)
                        {
                            // LEAK FIX (#3): the SO death-VFX prefab was Instantiated with
                            // NO Destroy — every kill leaked a GameObject forever. Bound
                            // its lifetime so it self-destructs (the VfxPool path already
                            // self-returns; this matches that behaviour).
                            var deathGo = Instantiate(deathPrefab, deathPos, Quaternion.identity);
                            if (deathGo != null) Destroy(deathGo, TypeVfxSelfDestructSeconds);
                        }
                        else
                        {
                            // VFX-FREE-WIN-2: before conceding to the one procedural grey poof,
                            // ask the enemy's OWN species. Both fields consulted above
                            // (_deathVFXOverride, _typeVfxSet) are per-PREFAB serialized fields,
                            // and the pool/factory spawn path (EnemyFactory -> Configure) sets
                            // NEITHER — so every wave, roam and camp enemy in the game reached
                            // SpawnDeathBurst and a golem died exactly like a skeleton. _def is
                            // the one thing that path DOES set (Configure, :567), so derive from
                            // it. playSound:false — the death SFX is already fired by the
                            // PlayTypeSound(CombatSfxFallback.Death) call below, and VfxToSfx
                            // maps every Death_* to SfxId.EnemyDeath, which would double it.
                            VFXType speciesDeath = SpeciesDeathVfx();
                            if (speciesDeath != VFXType.None)
                                VFXManager.Play(speciesDeath, deathPos,
                                                Quaternion.identity, playSound: false);
                            else
                                VfxPool.SpawnDeathBurst(deathPos);
                        }
                    }

                    // WO-84: secondary burst 0.28 s after the primary death VFX.
                    StartCoroutine(SecondaryDeathBurst(deathPos));
                }

                // DEF-46: per-type death audio (always plays, even for elite kills).
                // WO-220: fall back to a generated death SFX (via CoreServices.Audio)
                // when no type-set clip is authored.
                PlayTypeSound(_typeVfxSet != null ? _typeVfxSet.RandomDeathClip() : null,
                              CombatSfxFallback.Death);

                // WO-886: the kill shake is TIERED (boss 0.7 / elite 0.3 / regular 0.18),
                // driven off this enemy's own enemies.json stat block and routed through the
                // ONE home of that rule so the component path and this data path cannot
                // drift. Until now every kill got the flat 0.18 punch - including a boss -
                // because the component that owns the 0.7 boss shake is attached to no
                // prefab, scene or asset anywhere in the tree, so its branch never ran.
                if (eliteVfx == null)
                    EliteVFXController.PlayDeathShake(IsBossTier(), IsEliteTier());

                // Ticket #61: combo / kill-streak / RAMPAGE + crystal feedback fires for HERO
                // kills ONLY. A tower / pet / DoT / environmental kill still bursts VFX, shakes,
                // drops loot and grants XP (all above/below) — it just must NOT feed the combo.
                DeNelle.Core.Diagnostics.FlowTrace.Step("Combat",
                    "CombatFeedback Kill gated: dealtByHero=" + dealtByHero + " enemy=" + gameObject.name);
                if (dealtByHero)
                    CombatFeedbackManager.Kill(transform.position);

                // DEF-178: a brief hit-stop "punch" on the kill — the satisfying
                // weight beat that was missing (kills only shook, never froze). Just
                // the freeze (not DoImpact, which would add a second shake on top of
                // the CameraShakeBridge one above); HitStopManager is null-safe + its
                // own quality gate skips this on Low. Short + capped (mobile-safe).
                // HitStopManager dedups overlaps, so a multi-kill frame won't stack.
                HitStopManager.Instance?.HitStop(0.05f, 0.04f);

                // Combat feel: leave a persistent ground scorch where the enemy
                // fell. Null-safe — no DecalSpawner in the scene = no-op.
                DecalSpawner.Instance?.SpawnScorch(transform.position);
            }

            // Kill-XP attribution: a genuine kill shares this enemy's XP across
            // the combatants that damaged it; a forced removal (breach) just
            // discards its damage ledger so nothing leaks and no XP is granted.
            if (killed) DeNelle.Village.Progression.ProgressionManager.ReportKill(this);
            else DeNelle.Core.Combat.DamageAttribution.Forget(this);

            // WO-1103: per-enemy BASE + bounded VARIANCE kill grants (owner directive
            // 2026-08-16 "each enemy should have a base value with some random on it").
            // DEF-88 XP + WO-432/433 GOLD both roll through the ONE authority
            // (EnemyDef.RollReward); variance is the def's data-driven rewardVariance
            // trickle, not part of the combat-economy variance surface).
            if (killed && _def != null)
            {
                float variance = _def.RewardVariance;

                // DEF-88: per-enemy XP directly to the hero, now variance-rolled.
                // WO-1104: the amount is MEASURED from HeroProgression's own lifetime total
                // either side of the grant, never assumed from the roll. A grant that is
                // rejected downstream (null carrier, clamp, non-positive) then reports 0 and
                // shows nothing, instead of a label promising XP the player never banked.
                int rolledXp = 0, creditedXp = 0;
                var heroProg = HeroProgression.Instance;
                if (heroProg != null)
                {
                    rolledXp = EnemyDef.RollReward(_def.XpReward, variance);
                    if (rolledXp > 0)
                    {
                        float xpBefore = heroProg.LifetimeXp;
                        heroProg.AddXp(rolledXp);
                        creditedXp = Mathf.RoundToInt(heroProg.LifetimeXp - xpBefore);
                    }
                }

                // WO-432/433: GOLD (Coins) on kill so the Gold-cost building research has a
                // kill-driven source. Data-driven (EnemyDef.CoinReward) with the XP-derived
                // fallback so EVERY enemy pays out; the resolved base is variance-rolled.
                // EconomyService.AddCoins is the single Coins grant + HUD/save path.
                int goldBase = _def.CoinReward > 0
                    ? _def.CoinReward
                    : Mathf.Max(4, Mathf.RoundToInt(_def.XpReward * 0.4f));
                // WO-1104: gold is MEASURED the same way — the wallet balance either side of
                // the mover. AddCoins clamps at zero and no-ops when GameState is absent, so
                // the requested delta is NOT proof anything was banked.
                int rolledGold = EnemyDef.RollReward(goldBase, variance);
                int creditedGold = 0;
                var econ = EconomyService.Instance;
                if (rolledGold > 0 && econ != null)
                {
                    int goldBefore = econ.Coins;
                    econ.AddCoins(rolledGold);
                    creditedGold = Mathf.Max(0, econ.Coins - goldBefore);
                }

                // WO-1216: WOOD / IRON / STONE on EVERY kill, riding THIS one seam.
                // Owner ruling 2026-08-26 — "the drop is any kill, not just waves but in the
                // world the encounters" + "wood iron gold stone, balance it so i can afford to
                // repair by grinding some kills". This is deliberately here and NOT on
                // WaveManager._ironPerKill: that grant is gated on WavePhase.Active and so pays
                // for wave kills only, silently missing every world encounter / outpost / arena
                // kill — precisely the scope the owner corrected. Leave those fields alone.
                //
                // The material base derives from the SAME resolved goldBase above (so every
                // enemy pays, via the same XP fallback, and the payout scales with difficulty),
                // through the DATA constants in Data/Canonical/kill-rewards.json —
                // ⛔ never a code literal — and is then variance-rolled through the ONE roll
                // authority (EnemyDef.RollReward) with this enemy's own rewardVariance, so all
                // four materials read as ONE drop rather than four systems.
                //
                // ⛔⛔ THE STONE TRAP (WO-1212, CLOSED 2026-08-26): there USED to be two Stone
                // balances and only ONE of them was the player's. The HUD chip labelled "Stone"
                // (HudKitController.cs:1596 pairs CurrencyKind.Food with the name "Stone") reads
                // GameState.Resources.Food via EconomyService.Food — DEF-121 repurposed the
                // retired Stone axis onto Food, and that is the balance every cost actually
                // spends. GameState.Stone WAS a second persisted balance displayed NOWHERE and
                // spent by NOTHING; granting there meant the player was told they earned Stone
                // and received nothing, silently. WO-1212 RETIRED that field (2026-08-26), so
                // there is now exactly ONE Stone balance and this grant already rides it:
                // EconomyService.Grant's `food` slot below.
                //
                // EconomyService.Grant(ResourceCost) is the EARNED-INCOME path: it is the single
                // choke every income source flows through and it applies the town bank cap
                // (clamp-and-warn, WO-901 §5). Deliberately NOT GrantUncapped (a cheat seam) and
                // NOT GrantPurchased (a paid entitlement) — a kill reward is earned income.
                //
                // ⛔ WO-1227 — A RAID PAYS ONCE, AT THE END. Owner ruling 2026-08-26, verbatim:
                // "raids only pay at end of raid". WO-1216 put the material faucet on THIS seam
                // (correctly — it is the one every kill flows through), and the unintended
                // consequence was that a raid banked materials TWICE: once per defender the
                // player's troops cut down, and again in the victory summary
                // (RaidVictoryController.GrantLoot -> RaidScoring.ComputeLoot). The summary grant
                // is the one the owner wants, so the PER-KILL half is suppressed while a raid is
                // live — and ONLY the material half: XP and the WO-432/433 gold above are
                // untouched, because the ruling is about the raid's resource payout, not about
                // stripping a kill of its progression.
                //
                // The raid test is RaidScoring.RaidInProgress — the scorer's own lifetime, which
                // is what every other raid system already treats as "a raid is running". It can
                // ONLY be true inside a RaidBase_* scene, so an open-world encounter, a wave kill
                // and a dungeon kill all take the else-branch and pay exactly the WO-1216 amount
                // they pay today. Those three are the felt-verified behaviour and are the main
                // risk in this change; the gate is written so they are literally unreachable
                // from it.
                bool raidInProgress = RaidScoring.RaidInProgress;
                int rolledWood = 0, rolledIron = 0, rolledStone = 0;
                int creditedWood = 0, creditedIron = 0, creditedStone = 0;
                // WO-1590 — the APPLIED basket EconomyService.Grant returns (post town-bank
                // clamp). It is NOT a second measurement of the wallet; the credited deltas
                // above remain the truth about what landed. This exists solely to NAME the
                // cause when they disagree: inside GrantInternal the only thing that can
                // reduce an axis is TownBankCapacity.ClampGrant, so applied < rolled means
                // "the bank is full", and applied == rolled with credited < rolled means the
                // grant left the economy service in full and still did not reach the wallet.
                // Seeded to -1 = "no grant was attempted" so a suppressed/econ-null kill is
                // distinguishable from a grant that applied 0.
                int appliedWood = -1, appliedIron = -1, appliedStone = -1;
                int matBaseWood = KillRewardBalanceCatalog.KillMaterialBase(goldBase, "wood", raidInProgress);
                int matBaseIron = KillRewardBalanceCatalog.KillMaterialBase(goldBase, "iron", raidInProgress);
                int matBaseStone = KillRewardBalanceCatalog.KillMaterialBase(goldBase, "stone", raidInProgress);

                // §12: a SILENT suppression is indistinguishable from a broken faucet, and this
                // repo has been burned by exactly that. One line per suppressed kill naming the
                // reason AND the amount withheld, so "my raid kills pay nothing" is answered by
                // the log instead of by a code-read.
                if (raidInProgress)
                {
                    int withheldWood  = KillRewardBalanceCatalog.MaterialBaseFromGold(goldBase, "wood");
                    int withheldIron  = KillRewardBalanceCatalog.MaterialBaseFromGold(goldBase, "iron");
                    int withheldStone = KillRewardBalanceCatalog.MaterialBaseFromGold(goldBase, "stone");
                    string scorer = RaidScoring.Instance != null ? "live" : "absent(scene-fallback)";
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Reward",
                        $"KILL MATERIALS SUPPRESSED (raid active) id={_def.Id} baseGold={goldBase} " +
                        $"scorer={scorer} " +
                        $"withheldBase={withheldWood}/{withheldIron}/{withheldStone} wood/iron/stone " +
                        "- WO-1227 owner ruling \"raids only pay at end of raid\"; the payout comes " +
                        "ONCE from RaidVictoryController.GrantLoot at the summary. XP and gold are " +
                        "deliberately unaffected.");
                }

                if (econ != null)
                {
                    rolledWood  = EnemyDef.RollReward(matBaseWood,  variance);
                    rolledIron  = EnemyDef.RollReward(matBaseIron,  variance);
                    rolledStone = EnemyDef.RollReward(matBaseStone, variance);
                    if (rolledWood > 0 || rolledIron > 0 || rolledStone > 0)
                    {
                        // WO-1104 discipline, per material: the wallet is read either side of
                        // the mover. Grant returns the APPLIED basket, but the state delta is
                        // the stronger proof (it also catches a wallet that never existed), and
                        // it is what the shortfall Warn below is judged on. A single combined
                        // number would be a hollow assertion — it could not show WHICH material
                        // failed to bank.
                        int woodBefore  = econ.Wood;
                        int ironBefore  = econ.Iron;
                        int stoneBefore = econ.Food;
                        var applied = econ.Grant(new ResourceCost(
                            wood: rolledWood, food: rolledStone, iron: rolledIron));
                        appliedWood  = applied.Wood;
                        appliedIron  = applied.Iron;
                        appliedStone = applied.Food;   // WO-1212: Stone rides the Food axis
                        creditedWood  = Mathf.Max(0, econ.Wood - woodBefore);
                        creditedIron  = Mathf.Max(0, econ.Iron - ironBefore);
                        creditedStone = Mathf.Max(0, econ.Food - stoneBefore);
                    }
                }

                // §12 permanent trace: base/variance/ROLLED (asked) vs CREDITED (measured
                // state delta) per grant. Kills are cold-path — one line per kill is cheap and
                // makes both the roll AND the landing provable. Printing rolled as if it were
                // final would be a hollow assertion; the two are logged separately on purpose,
                // and a mismatch is the signal that a grant was swallowed downstream.
                // WO-1216 extends the SAME shape to wood/iron/stone — one line per material,
                // never one combined figure, so a shortfall names the material that was
                // swallowed (a full store clamps ONE axis at a time).
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reward",
                    $"KILL GRANT id={_def.Id} baseXp={_def.XpReward} baseGold={goldBase} " +
                    $"var={variance:0.00} rolledXp={rolledXp} rolledGold={rolledGold} " +
                    $"creditedXp={creditedXp} creditedGold={creditedGold} packBodies={_def.PackBodies} " +
                    $"| WO-1216 mult={KillRewardBalanceCatalog.GoldToMaterialMultiplier:0.00} " +
                    $"floor={KillRewardBalanceCatalog.MaterialFloorPerKill} " +
                    $"cap={KillRewardBalanceCatalog.MaterialCapPerKill} " +
                    $"baseWood={matBaseWood} baseIron={matBaseIron} baseStone={matBaseStone} " +
                    $"rolledWood={rolledWood} rolledIron={rolledIron} rolledStone={rolledStone} " +
                    $"creditedWood={creditedWood} creditedIron={creditedIron} creditedStone={creditedStone}");
                if (creditedXp != rolledXp || creditedGold != rolledGold)
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Reward",
                        $"KILL GRANT SHORTFALL id={_def.Id} askedXp={rolledXp} bankedXp={creditedXp} " +
                        $"askedGold={rolledGold} bankedGold={creditedGold} - a grant did not land " +
                        "(missing HeroProgression / EconomyService / clamped wallet).");
                if (creditedWood != rolledWood || creditedIron != rolledIron || creditedStone != rolledStone)
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Reward",
                        DescribeMaterialShortfall(
                            _def.Id,
                            rolledWood, creditedWood, appliedWood,
                            rolledIron, creditedIron, appliedIron,
                            rolledStone, creditedStone, appliedStone));

                // WO-1103 item 3+4, on the CREDITED amounts (WO-1104): never announce an award
                // that did not bank.
                //  - kill INSIDE a live staged arena -> bank into the battle's per-enemy stream
                //    so the victory SUMMARY reports the TOTAL actually banked, AND pop the same
                //    earned label at the corpse. WO-1104 (owner felt-test 2026-08-16: "if you
                //    fight five enemies that experience should be much larger... I couldn't tell
                //    that"): the arena used to bank SILENTLY, so a five-kill fight looked exactly
                //    like a one-kill fight until the end screen. One label per body is what makes
                //    five kills read as five awards.
                //  - kill OUTSIDE the arena (field kill — ranged pick-off, wave, camp) -> ONE
                //    aggregate earned label at the corpse (a pack leader carries the whole family
                //    payout, so this is one toast per pack, never per body).
                if (creditedXp > 0 || creditedGold > 0)
                {
                    bool arenaKill = DeNelle.Village.Arena.BattleArena.AnyBattleInProgress
                                     && DeNelle.Village.Arena.BattleArena.IsArenaPosition(transform.position);
                    if (arenaKill)
                        DeNelle.Village.Arena.BattleArena.Instance?.ReportArenaKillGrant(creditedXp, creditedGold);
                    ShowFieldKillReward(creditedXp, creditedGold);
                }
            }

            // Play the death (collapse) animation, then RETURN TO THE POOL (no longer
            // Destroy — pooling reuses the body to kill the per-spawn GameObject churn
            // / stray accumulation). The Dead bool latches the controller's Death state;
            // the body is held DeathHoldSeconds so the collapse clip is visible, then
            // ResetForPool tears it down and EnemyPool.Release parks it dormant. With no
            // Animator there is nothing to hold for, so it is released this frame.
            if (_animator != null)
            {
                if (_hasDeadParam) _animator.SetBool(AnimDead, true);
                StartCoroutine(ReturnToPoolAfterDeathHold());
            }
            else
            {
                ResetForPool();
                EnemyPool.Release(this);
            }
        }

        /// <summary>
        /// Holds the dead body <see cref="DeathHoldSeconds"/> so its collapse clip is
        /// visible, then resets it and returns it to the <see cref="EnemyPool"/>. This
        /// replaces the old <c>Destroy(gameObject, DeathHoldSeconds)</c>. If the body
        /// were re-killed in that window the dead latch prevents a second Die; and if
        /// the pool is gone (shutdown) Release falls back to Destroy.
        /// </summary>
        private System.Collections.IEnumerator ReturnToPoolAfterDeathHold()
        {
            // #55 1/2-raycast settle: each frame ease the corpse's Y halfway onto the surface
            // directly below it (down-ray = "ground is the area below placement"), so it conforms
            // to slopes/ledges/tiers as the collapse clip plays instead of hanging at agent height.
            // Exponential convergence reaches the surface within a few frames; a final lerp=1 beat
            // hard-snaps it exactly flush so there is zero gap to perceive. The agent was disabled in
            // Die(), so nothing re-pins the body while we settle it.
            float t = 0f;
            while (t < DeathHoldSeconds)
            {
                SnapBodyToGround(0.5f);
                t += Time.deltaTime;
                yield return null;
            }
            SnapBodyToGround(1f);   // final hard-snap: rest exactly on the surface
            ResetForPool();
            EnemyPool.Release(this);
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// WO-1590 — compose the "materials did not land in full" warn so it NAMES the reason
        /// per material instead of guessing one for all three.
        /// <para>
        /// WHY THIS EXISTS. The retired text ended "(missing EconomyService/GameState, or the
        /// town bank cap clamped that axis)" — one sentence offering two causes for a line that
        /// always reports three materials at once. On the owner's 2026-09-07 Seeker session it
        /// fired on EVERY dungeon kill with `askedStone=8 bankedStone=0` beside `bankedWood=8`
        /// and `bankedIron=8`, and the ticket that was minted from it (WO-1590) spent its first
        /// three hypotheses on causes the log had already ruled out. The bank had ALREADY said
        /// which it was, unthrottled, on the adjacent line:
        /// `[Flow:Bank] BANK FULL [Grant] Stone: requested 8, banked 0, LOST 8 (wallet
        /// 34000/34000)`. A full Stoneyard is WO-837 working as ruled; the DEFECT was that this
        /// warn re-guessed instead of reading what it was handed.
        /// </para>
        /// <para>
        /// HOW THE REASON IS DERIVED — from the two numbers already in hand, with NO second cap
        /// walk (<c>TownBankCapacity.ClampGrant</c>'s own comment forbids re-walking the layout
        /// on this hot path):
        /// <list type="bullet">
        /// <item><description><c>applied &lt; asked</c> — <c>ClampGrant</c> is the ONLY reducer
        /// inside <c>EconomyService.GrantInternal</c>, so this IS the town bank cap. Say so, and
        /// point at the [Flow:Bank] line that carries the ceiling.</description></item>
        /// <item><description><c>applied == asked</c> but <c>banked &lt; asked</c> — the grant
        /// left the economy service in full and still did not reach the wallet. THIS is the
        /// missing-GameState / swallowed-write case, and it is the only branch that earns the
        /// old wording.</description></item>
        /// <item><description><c>applied &lt; 0</c> — no grant was attempted at all (sentinel).
        /// </description></item>
        /// </list>
        /// Materials that landed in full are named as such rather than omitted, so the line can
        /// never be misread as "all three failed".
        /// </para>
        /// <para>PURE + <c>static</c> on purpose: the warn lives on the death path, which no
        /// EditMode suite can drive. Pulling the wording out here makes it directly testable —
        /// <c>KillGrantShortfallReasonRegression</c> drives both branches.</para>
        /// </summary>
        public static string DescribeMaterialShortfall(
            string enemyId,
            int askedWood, int bankedWood, int appliedWood,
            int askedIron, int bankedIron, int appliedIron,
            int askedStone, int bankedStone, int appliedStone)
        {
            var sb = new System.Text.StringBuilder(320);
            sb.Append("KILL GRANT SHORTFALL (materials) id=").Append(enemyId ?? "?").Append(' ');
            sb.Append("askedWood=").Append(askedWood).Append(" bankedWood=").Append(bankedWood).Append(' ');
            sb.Append("askedIron=").Append(askedIron).Append(" bankedIron=").Append(bankedIron).Append(' ');
            sb.Append("askedStone=").Append(askedStone).Append(" bankedStone=").Append(bankedStone);
            sb.Append(" - ");
            sb.Append(MaterialReason("Wood", askedWood, bankedWood, appliedWood)).Append("; ");
            sb.Append(MaterialReason("Iron", askedIron, bankedIron, appliedIron)).Append("; ");
            sb.Append(MaterialReason("Stone", askedStone, bankedStone, appliedStone)).Append('.');
            return sb.ToString();
        }

        /// <summary>
        /// One material's clause for <see cref="DescribeMaterialShortfall"/>. See that method for
        /// why the three cases are split this way. Never returns an empty string — a silent
        /// material is exactly the ambiguity this replaced.
        /// </summary>
        private static string MaterialReason(string name, int asked, int banked, int applied)
        {
            if (asked <= 0) return name + ": none rolled";
            if (banked >= asked) return name + $": banked {banked}/{asked} in full";
            if (applied < 0)
                return name + $": no grant was attempted ({banked}/{asked}) - EconomyService was absent";
            if (applied < asked)
                return name + $": BANK FULL - the town bank cap clamped {asked} to {applied} " +
                       "(the adjacent [Flow:Bank] warn carries the wallet/ceiling and the container " +
                       "to upgrade). WO-837/WO-901 working as ruled, not a lost grant";
            return name + $": the economy service applied {applied} but only {banked} reached the " +
                   "wallet - a write was swallowed downstream (missing GameStateService, or a " +
                   "second wallet reader)";
        }

        /// <summary>
        /// WO-1103 item 4 (B2) + WO-1104: ONE aggregate earned-rewards label per KILL
        /// ("+N XP  +M gold" at the corpse). It uses the shared screen-space
        /// <see cref="CombatText"/> layer: font capped at 44 reference pixels, pooled,
        /// deduped and outlined. The old world-space <see cref="DamageNumberSpawner.SpawnLabel"/>
        /// path scaled with camera distance and painted this line across the fight on Seeker.
        /// WO-1104 widened it from field-only to EVERY kill: an arena kill banked silently
        /// before, so a five-body fight and a one-body fight looked identical while they
        /// were being fought (owner felt-test 2026-08-16).
        /// A pack-carrying leader (def.PackBodies &gt; 1 — leader-carry payout KEPT,
        /// owner default) is worded "Pack bounty" so the oversized grant reads as the
        /// whole family's payout, not a bug. Followers pay 0 and never reach here, so
        /// a pack is one toast, never per-body spam.
        /// The amounts passed in are the MEASURED credited deltas, so the label can never
        /// promise an award the player did not actually bank.
        /// <para>
        /// ⛔ WO-1590 — THE LABEL CARRIES XP AND GOLD ONLY, AND MATERIALS ARE DELIBERATELY NOT
        /// ADDED. Verified 2026-09-07 against the owner's Seeker session, where every dungeon
        /// kill clamped Stone to 0 on a full 34000/34000 bank: the toast promised nothing it did
        /// not bank, because it never mentions Stone at all. Do NOT "fix" that by printing the
        /// materials here — the amounts would be right, but a per-kill "Stone full" scold is the
        /// exact noise WO-1207 ruling 3 forbids ("a grant made OUTSIDE a scope is silent on
        /// screen by design: the player did not time it, so a scold is noise she cannot act on").
        /// A kill grant is not player-timed, so this path must never open a
        /// <c>BankOverflowToastPresenter</c> warn scope either. If the owner later rules that a
        /// dungeon run SHOULD tell her the bank is full, the seam is one opt-in scope around the
        /// run — not a label change here.
        /// </para>
        /// </summary>
        private void ShowFieldKillReward(int xp, int gold)
        {
            var sb = new System.Text.StringBuilder(32);
            if (_def != null && _def.PackBodies > 1) sb.Append("Pack bounty  ");
            if (xp > 0) sb.Append("+").Append(xp).Append(" XP");
            if (gold > 0)
            {
                if (xp > 0) sb.Append("  ");
                sb.Append("+").Append(gold).Append(" gold");
            }
            string label = sb.ToString();
            CombatText.Show(CombatTextKind.Reward, label, transform.position + Vector3.up * 1.6f);
            // §12 permanent trace: the notification call-site fires provably (B2 was
            // "no call site at all" — this line is the captured evidence it now exists).
            // CombatText is camera-null-safe and owns the bounded presentation contract.
            DeNelle.Core.Diagnostics.FlowTrace.Step("Reward",
                $"KILL REWARD TOAST '{label}' id={(_def != null ? _def.Id : "?")} " +
                $"routed=CombatText(Reward) at {transform.position}");
        }

        /// <summary>
        /// WO-84: Small secondary impact burst 0.28 s after the primary death VFX —
        /// gives the kill a two-beat punch. Null-safe if VFXManager is absent.
        /// </summary>
        private System.Collections.IEnumerator SecondaryDeathBurst(Vector3 pos)
        {
            yield return new WaitForSeconds(0.28f);
            VFXManager.Play(VFXType.Impact_Physical, pos + Vector3.up * 0.3f);
        }

        /// <summary>
        /// VFX-FREE-WIN-2: derive this enemy's death burst from its SPECIES, read off the
        /// enemies.json stat block (<see cref="EnemyDef"/>) that <see cref="Configure"/>
        /// stores in <c>_def</c> — the only species signal present on the pool/factory
        /// spawn path, which sets neither <c>_deathVFXOverride</c> nor <c>_typeVfxSet</c>.
        /// <para>
        /// Every VFXType returned here is ALREADY wired to a prefab in
        /// <c>Assets/Resources/VFX/VFXCatalog.asset</c> and every one of those rows is
        /// <c>IsLoop: 0</c> (a ONESHOT) — deaths fire at high frequency during a wave, and a
        /// loop-flagged row played fire-and-forget permanently burns one of the 20 global
        /// loop slots (see the leak documented at the ranged-attack call site below).
        /// </para>
        /// <para>
        /// Mapping is taken from the VFXType doc-comments verbatim, NOT invented:
        /// Death_Boss = "Boss death"; Death_Brute = "Heavy brute / golem death";
        /// Death_Skeleton = "Standard Hollow One death". Anything with no documented
        /// species match returns <see cref="VFXType.None"/> so the caller keeps the exact
        /// pre-existing generic burst — this is additive, never a substitution.
        /// </para>
        /// <para>
        /// WO-886 added the ELITE rung. <c>role: "elite"</c> is a real, populated value in
        /// enemies.json (hollow-reaper, hollow-apprentice, orc-necromancer, plus the
        /// necromancer which is also <c>boss</c>) and <see cref="VFXType.Elite_Death"/>'s
        /// own doc reads "on elite enemy death" — so this is a data read, not a creative
        /// pick. It is tested BEFORE the family fallback or three of those four would have
        /// died as plain Hollow trash.
        /// </para>
        /// <para>
        /// NOT mapped, deliberately: <c>Death_Wolf</c> and <c>Death_Tiefling</c> are wired
        /// to real prefabs but enemies.json contains no wolf and no tiefling (families are
        /// hollow / orc / troll; "Ice Wolf" is a PET, not an enemy). Assigning them to orc
        /// or troll would be a creative pick, which is the owner's call — leave them unused
        /// rather than guess. (WO-886 re-confirmed this against the live roster and left
        /// the mapping untouched.)
        /// </para>
        /// </summary>
        private VFXType SpeciesDeathVfx()
        {
            if (_def == null) return VFXType.None;

            // Boss flag first — it outranks family and role. Death_Boss is the legacy alias
            // of Boss_Death; WO-886 points both catalog rows at the SAME prefab, so which
            // of the two names is returned here can no longer change what the player sees.
            if (_def.Boss) return VFXType.Death_Boss;

            string role = (_def.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (role == "elite") return VFXType.Elite_Death;
            if (role == "brute") return VFXType.Death_Brute;

            string family = (_def.Family ?? string.Empty).Trim().ToLowerInvariant();
            if (family == "hollow") return VFXType.Death_Skeleton;

            return VFXType.None;
        }

        /// <summary>
        /// WO-889: the PERSISTENT species aura this enemy holds while alive, or
        /// <see cref="VFXType.None"/> for an archetype with no aura. Read by
        /// <see cref="EnemyAuraVFX"/>, which owns the loop's lifecycle.
        /// <para>
        /// Every mapping is a DATA READ against the live enemies.json roster (verified at
        /// source, 16 rows), never a creative pick - the same discipline
        /// <see cref="SpeciesDeathVfx"/> follows:
        /// </para>
        /// <list type="bullet">
        /// <item><c>role: "caster"</c> is a real, populated value (hollow-acolyte,
        /// hollow-mage, orc-shaman) and <c>Aura_EnemyCaster</c>'s own name states the
        /// relationship.</item>
        /// <item>The necromancer and reaper rows match the enum's OWN NAME against real
        /// roster ids (necromancer, orc-necromancer, hollow-reaper). Tested by id rather
        /// than by role because both necromancers carry <c>role: "elite"</c>, which they
        /// share with hollow-apprentice - an elite APPRENTICE is not a necromancer.</item>
        /// </list>
        /// <para>
        /// NOT mapped, deliberately: <c>Aura_Dust</c>. Its recipe is built and catalogued,
        /// but "which enemies kick up foot dust" is a creative call (the honest candidates
        /// are the four brutes), and this method already sets the precedent of leaving
        /// Death_Wolf / Death_Tiefling wired-but-unassigned rather than guessing. One line
        /// here turns it on once the owner rules.
        /// </para>
        /// </summary>
        public VFXType SpeciesAuraVfx()
        {
            if (_def == null) return VFXType.None;

            // Id first: it is the most specific signal, and both necromancers would
            // otherwise be swallowed by the elite role they share with hollow-apprentice.
            string id = (_def.Id ?? string.Empty).Trim().ToLowerInvariant();
            if (id.Contains("necromancer")) return VFXType.Aura_Necromancer;
            if (id.Contains("reaper"))      return VFXType.Aura_SmokeReaper;

            string role = (_def.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (role == "caster") return VFXType.Aura_EnemyCaster;

            return VFXType.None;
        }

        /// <summary>
        /// True when this enemy's stat block flags it as a boss. WO-886: drives the tiered
        /// death shake. Read off <c>_def</c> because that is the only species signal the
        /// pool/factory spawn path sets (see <see cref="SpeciesDeathVfx"/>).
        /// </summary>
        private bool IsBossTier() => _def != null && _def.Boss;

        /// <summary>
        /// True when this enemy's stat block gives it the <c>elite</c> role (and it is not
        /// already a boss, which outranks it). WO-886: drives the tiered death shake.
        /// </summary>
        private bool IsEliteTier() =>
            _def != null && !_def.Boss &&
            string.Equals((_def.Role ?? string.Empty).Trim(), "elite",
                          System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// WO-219: maps a damage element to its existing impact VFXType. Returns
        /// <see cref="VFXType.None"/> for a null element (melee / physical) so the
        /// caller keeps the original SO-prefab / VfxPool grey-spark path. No new
        /// VFXType added — these all already exist in VFXType.cs.
        /// </summary>
        private static VFXType ImpactVfxFor(DamageElement? element)
        {
            if (!element.HasValue) return VFXType.None;
            switch (element.Value)
            {
                case DamageElement.Flame: return VFXType.Impact_Flame;
                case DamageElement.Ice:    return VFXType.Impact_Ice;
                case DamageElement.Aether: return VFXType.Impact_Aether;
                // Physical / None spell damage uses the existing per-type / VfxPool path.
                default:                   return VFXType.None;
            }
        }

        private void EnsureAgent()
        {
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
        }

        /// <summary>
        /// WO-531: snap the dead body straight down onto the ground so a corpse never
        /// floats at the Y it died at (e.g. mid-air over a wall top). Owner directive:
        /// on death the body falls/snaps to ground regardless of where it died.
        /// (DragonBoss is its OWN class with its own death spiral and never runs this
        /// <see cref="Die"/>, so apex bosses are already excluded.)
        ///
        /// Primary: raycast DOWN against the ground/terrain layers and snap Y to the hit.
        /// Fallback: <see cref="NavMesh.SamplePosition"/> and snap Y to the sampled point.
        /// If neither resolves, the position is left unchanged and a Warn is logged.
        /// </summary>
        /// <param name="lerp">
        /// 1 = hard-snap the body exactly onto the surface below it (the one-shot death snap and
        /// the final settle beat). 0..1 = ease the Y that fraction toward the surface — the #55
        /// per-frame "1/2-raycast settle" passes 0.5 so the corpse conforms smoothly to whatever
        /// is directly below it (slope / ledge / tier) as the collapse clip plays.
        /// </param>
        private void SnapBodyToGround(float lerp = 1f)
        {
            try
            {
                Vector3 pos = transform.position;
                lerp = Mathf.Clamp01(lerp);

                // F8 2026-07-11 (floating corpses in the outpost): the old mask named
                // "Terrain" and "Ground" — layers that DO NOT EXIST in TagManager (project
                // layers: Default, TransparentFX, Ignore Raycast, Tower, Water, UI, Building,
                // Enemy, Structure) — so GetMask silently collapsed to Default-only and the
                // KayKit outpost floor (Structure/Building surfaces) was never hit; the corpse
                // settled on the elevated navmesh baseline instead. Use the layers that exist.
                // Still excludes the enemy's own collider (on the "Enemy" layer) — no self-hit.
                int groundMask = LayerMask.GetMask("Default", "Structure", "Building", "Water");
                if (groundMask == 0) groundMask = Physics.DefaultRaycastLayers;

                // #55 (capture-proven 2026-06-29): grounding the TRANSFORM PIVOT to the surface
                // is NOT enough — the captured trace showed SnapBodyToGround never reported "no
                // ground" (the pivot WAS snapping) yet the body still read as floating, because the
                // visible mesh sits ABOVE the pivot (rig/agent vertical offset). So ground the
                // VISIBLE BOTTOM (combined renderer bounds.min.y), not the pivot: measure how far the
                // pivot rides above the lowest rendered point (footGap) and snap so that bottom lands
                // on the surface. footGap==0 (pivot already at the feet) degrades to the old behaviour.
                float footGap = PivotToVisibleBottomGap(out Renderer lowestRend);

                // F8 2026-07-11 sky corpse (proof: 'SnapBodyToGround(ArenaEnemy_orc-shaman_0)
                // ground=0.00 footGap=53.58 -> pivotY=53.58'): a corrupt child-renderer bound
                // ~50m below the pivot made the "rest visible bottom on surface" math LAUNCH
                // the corpse skyward. A ground-snap may only move DOWN or barely up — cap the
                // addend and the lift.
                const float MaxFootGap = 3f;
                if (footGap > MaxFootGap)
                {
                    // F8 seq 652: the old line printed the gap but NOT which renderer produced it,
                    // which turned a one-grep diagnosis into a full trace walk. Name the culprit
                    // renderer, its type and its bounds.min.y. The '[Flow:Enemy] SnapBodyToGround('
                    // prefix and the 'footGap' token are unchanged — watchers grep on those.
                    string culprit = lowestRend != null
                        ? $"{lowestRend.name} ({lowestRend.GetType().Name}) bounds.min.y={lowestRend.bounds.min.y:0.00}"
                        : "no renderer captured";
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Enemy",
                        $"SnapBodyToGround({gameObject.name}): footGap {footGap:0.00}m is absurd " +
                        $"(corrupt renderer bounds?) — lowest={culprit}, pivotY={pos.y:0.00} — capped to {MaxFootGap}m.");
                    footGap = MaxFootGap;
                }

                Vector3 origin = pos + Vector3.up * 2f;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 50f,
                                    groundMask, QueryTriggerInteraction.Ignore))
                {
                    // A death settle may descend, but it must never lift. The old
                    // `pos.y + 1.5f` ceiling was applied again on every coroutine frame,
                    // so a large animated renderer gap (the wave Ogre reports 2.27 m)
                    // ratcheted an already-grounded corpse into the air.
                    float target = DeathGroundTargetY(hit.point.y, footGap, pos.y);
                    pos.y = (lerp >= 1f) ? target : Mathf.Lerp(pos.y, target, lerp);
                    transform.position = pos;
                    if (lerp >= 1f)
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Enemy",
                            $"SnapBodyToGround({gameObject.name}) ground={hit.point.y:0.00} footGap={footGap:0.00} -> pivotY={pos.y:0.00} (visible bottom rests on surface)");
                    return;
                }

                // Fallback: nearest point on the navmesh (the walkable surface the enemy
                // traverses) when no physics ground collider answered.
                if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
                {
                    float target = DeathGroundTargetY(navHit.position.y, footGap, pos.y);
                    pos.y = (lerp >= 1f) ? target : Mathf.Lerp(pos.y, target, lerp);
                    transform.position = pos;
                    // F8 2026-07-11 (floating corpses): this branch used to settle SILENTLY,
                    // which hid the Default-only mask bug for weeks. Name the fallback so a
                    // future float is provable in one grep.
                    if (lerp >= 1f)
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Enemy",
                            $"SnapBodyToGround: raycast MISS in '{gameObject.scene.name}' -> navmesh settle y={pos.y:0.00} (raycast layers missed the floor?)");
                    return;
                }

                DeNelle.Core.Diagnostics.FlowTrace.Warn("Enemy",
                    $"SnapBodyToGround({gameObject.name}) found no ground (raycast + navmesh both missed) at {pos} — body left in place");
            }
            catch (Exception e)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Enemy",
                    $"SnapBodyToGround({gameObject.name}) threw (best-effort, death path unaffected): {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Pure death-seat rule: renderer bounds may lower the corpse to put its visible
        /// bottom on a surface, but animation bounds can never raise the actor root.
        /// Repeated settle frames therefore cannot accumulate an upward ratchet.
        /// </summary>
        public static float DeathGroundTargetY(float surfaceY, float visibleBottomGap, float currentY)
        {
            return Mathf.Min(surfaceY + Mathf.Clamp(visibleBottomGap, 0f, 3f), currentY);
        }

        /// <summary>
        /// #55: the vertical distance the transform PIVOT rides ABOVE the lowest visibly-rendered
        /// point (combined child-renderer world bounds.min.y). Used by SnapBodyToGround so it can
        /// land the VISIBLE bottom of the body on the surface rather than the pivot — the rig/agent
        /// offset means the pivot is often well above the feet, which read as "floating" when only
        /// the pivot was grounded. Returns 0 when no renderers exist or the pivot is already at/below
        /// the visible bottom (so the snap degrades to grounding the pivot — never lifts the body).
        /// </summary>
        /// <param name="lowest">
        /// F8 seq 652: the renderer that actually defined the visible bottom, so an absurd footGap
        /// names its culprit in the Warn instead of costing a full trace walk. Null when no renderer
        /// qualified.
        /// </param>
        private float PivotToVisibleBottomGap(out Renderer lowest)
        {
            lowest = null;
            var rends = GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return 0f;
            float bottom = float.PositiveInfinity;
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || !r.enabled) continue;
                // Skip non-body renderers (VFX/particles/UI) that would skew the floor.
                // F8 seq 652: TrailRenderer/LineRenderer join ParticleSystemRenderer here. Both
                // hold WORLD-space vertices, so a pooled body's leftover ribbon reported a
                // bounds.min.y tens of metres away and defined the "visible bottom" — the shaman's
                // 36.56 m footGap. FX ribbons are not body geometry.
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                float min = r.bounds.min.y;
                if (min < bottom) { bottom = min; lowest = r; }
            }
            if (float.IsInfinity(bottom)) return 0f;
            float gap = transform.position.y - bottom;
            return gap > 0f ? gap : 0f;
        }

        /// <summary>
        /// World point just above the enemy's head, where floating damage numbers
        /// spawn. Uses the rendered mesh bounds when available so the number clears
        /// the model's actual height; falls back to a fixed offset above the
        /// transform when the enemy has no Renderer yet.
        /// </summary>
        private Vector3 HeadWorldPosition()
        {
            Renderer rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Bounds b = rend.bounds;
                return new Vector3(b.center.x, b.max.y + 0.4f, b.center.z);
            }
            return transform.position + Vector3.up * 2.0f;
        }

        /// <summary>
        /// Resolves the Animator on the enemy rig (it sits on the KayKit skeleton
        /// mesh child, so search children too). Null when the prefab has no rig /
        /// no controller assigned — every Animator call is null-guarded.
        /// </summary>
        private void EnsureAnimator()
        {
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();

            if (_animator != null)
            {
                var relay = _animator.GetComponent<EnemyAnimationEventRelay>();
                if (relay == null) relay = _animator.gameObject.AddComponent<EnemyAnimationEventRelay>();
                relay.Configure(this);
            }

            RuntimeAnimatorController controller = _animator != null
                ? _animator.runtimeAnimatorController
                : null;
            if (controller == _scannedAnimatorController) return;

            _scannedAnimatorController = controller;
            _hasSpeedParam = false;
            _hasAttackParam = false;
            _hasWindUpParam = false;
            _hasHitParam = false;
            _hasDeadParam = false;
            _hasHitDirParam = false;

            // WO-163: cache which params the controller actually declares so the
            // per-frame DriveAnimator (and the trigger/bool calls) never drive an
            // absent param — that spams "Parameter does not exist" every frame.
            if (_animator != null && controller != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == AnimSpeed)  _hasSpeedParam  = true;
                    if (p.nameHash == AnimAttack) _hasAttackParam = true;
                    if (p.nameHash == AnimWindUp) _hasWindUpParam = true;
                    if (p.nameHash == AnimHit)    _hasHitParam    = true;
                    if (p.nameHash == AnimDead)   _hasDeadParam   = true;
                    if (p.nameHash == AnimHitDir) _hasHitDirParam = true;
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.86f, 0.27f, 0.27f, 0.9f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f,
                transform.forward * _contactProbeDistance);
        }
#endif
    }

    // =========================================================================
    // WO-1216 — kill-rewards.json: the DATA behind "every kill pays the four
    // materials".
    // -------------------------------------------------------------------------
    // Owner ruling 2026-08-26: "lets do around 20 per enemy per kill". The three
    // numbers that produce it (multiplier / floor / cap) are DATA, never code
    // literals, so the owner retunes them in one edit with NO recompile. The
    // field defaults below MIRROR the shipped json values on purpose: they are
    // the fallback when the file is missing/invalid, and a fallback that differs
    // from the authored value is how a silent balance drift starts (the lesson
    // EchoBalanceData.RepairFractionPerHour records in as many words).
    //
    // Lives beside its ONE consumer (Enemy's death grant) rather than in a new
    // file, so the formula and the seam that uses it cannot drift apart.
    // Loaded through DeNelle.Core.CanonicalJson: the Resources/Data/Canonical
    // dual-copy WINS at runtime (WebGL-safe), StreamingAssets is the desktop
    // source — keep the two byte-identical.
    //
    // Guard-wrapped with sensible fallbacks (§12): a missing or malformed file
    // logs a [Flow:Reward] Warn and returns the built-in defaults, so kills keep
    // paying and nothing hard-fails. No silent failure — every miss is traced.
    // =========================================================================

    /// <summary>The parsed kill-rewards.json root. Field defaults ARE the built-in fallback,
    /// and are kept equal to the authored json values.</summary>
    [System.Serializable]
    public sealed class KillRewardBalanceData
    {
        [Newtonsoft.Json.JsonProperty("version")] public int Version = 1;

        /// <summary>Shared constant: material base = goldBase * this (before per-material
        /// override, floor and cap). 1.1 puts the MEDIAN enemy (gold 18) at 20 — the ruled
        /// number — while keeping the payout scaled to difficulty.</summary>
        [Newtonsoft.Json.JsonProperty("goldToMaterialMultiplier")] public float GoldToMaterialMultiplier = 1.1f;

        /// <summary>Minimum a single kill pays of each material. Without it a cellar-hollow
        /// (gold 3) pays 3 and the kill reads as broken.</summary>
        [Newtonsoft.Json.JsonProperty("materialFloorPerKill")] public int MaterialFloorPerKill = 6;

        /// <summary>Maximum a single kill pays of each material. Without it a necromancer
        /// (gold 120) pays 132 of every material and one boss out-earns a whole wave.</summary>
        [Newtonsoft.Json.JsonProperty("materialCapPerKill")] public int MaterialCapPerKill = 40;

        /// <summary>Optional per-material override on the shared constant, keyed "wood" /
        /// "iron" / "stone". Absent or 0 = 1.0 (no override). GOLD is deliberately NOT a key:
        /// it keeps its own WO-432/433 grant and is the reference the others derive from —
        /// adding it here would double-pay.</summary>
        [Newtonsoft.Json.JsonProperty("perMaterialMultiplier")]
        public System.Collections.Generic.Dictionary<string, float> PerMaterialMultiplier
            = new System.Collections.Generic.Dictionary<string, float>();
    }

    /// <summary>Static surface over kill-rewards.json — load + cache + the ONE material-base
    /// formula (WO-1216). Every material grant site calls <see cref="MaterialBaseFromGold"/>;
    /// none re-implements the arithmetic.</summary>
    public static class KillRewardBalanceCatalog
    {
        private const string StreamingRelativePath = "Data/Canonical/kill-rewards.json";
        private const int ExpectedVersion = 1;
        private static KillRewardBalanceData _data;

        /// <summary>The full parsed balance data (never null — defaults if the file is absent).</summary>
        public static KillRewardBalanceData Data { get { EnsureLoaded(); return _data; } }

        /// <summary>Shared gold-to-material constant (never negative).</summary>
        public static float GoldToMaterialMultiplier
        {
            get { EnsureLoaded(); return Mathf.Max(0f, _data.GoldToMaterialMultiplier); }
        }

        /// <summary>Per-kill material floor (at least 1 — a kill that pays zero of a material
        /// reads as broken, so the floor can be tuned down but never to nothing).</summary>
        public static int MaterialFloorPerKill
        {
            get { EnsureLoaded(); return Mathf.Max(1, _data.MaterialFloorPerKill); }
        }

        /// <summary>Per-kill material cap, never below the floor (a bad data row that inverts
        /// the two would otherwise clamp every payout to the smaller number).</summary>
        public static int MaterialCapPerKill
        {
            get { EnsureLoaded(); return Mathf.Max(MaterialFloorPerKill, _data.MaterialCapPerKill); }
        }

        /// <summary>The per-material override for "wood" / "iron" / "stone" (1.0 when absent
        /// or non-positive, so a blank/zeroed row can never silently kill a faucet).</summary>
        public static float PerMaterialMultiplier(string material)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(material) && _data.PerMaterialMultiplier != null
                && _data.PerMaterialMultiplier.TryGetValue(material, out var m) && m > 0f)
                return m;
            return 1f;
        }

        /// <summary>
        /// THE ONE FORMULA: <c>clamp(round(goldBase * multiplier * perMaterial), floor, cap)</c>.
        /// <para>Returns 0 for a non-paying base (goldBase &lt;= 0) so a def that pays nothing
        /// stays paying nothing — the floor must never MINT a reward from a zero base, exactly
        /// as EnemyDef.RollReward refuses to mint one from variance.</para>
        /// <para>The result is a BASE, not a payout: the caller variance-rolls it through
        /// EnemyDef.RollReward so the four materials feel like one drop.</para>
        /// </summary>
        public static int MaterialBaseFromGold(int goldBase, string material)
        {
            if (goldBase <= 0) return 0;
            float raw = goldBase * GoldToMaterialMultiplier * PerMaterialMultiplier(material);
            int rounded = Mathf.RoundToInt(raw);
            return Mathf.Clamp(rounded, MaterialFloorPerKill, MaterialCapPerKill);
        }

        /// <summary>
        /// WO-1227 — the RAID-AWARE material base, and the ONE decision point for
        /// "does this kill pay materials at all".
        /// <para>Owner ruling 2026-08-26: <i>"raids only pay at end of raid"</i>. A kill taken
        /// while a raid is running pays ZERO materials; the raid's whole resource payout is the
        /// single end-of-raid grant in <c>RaidVictoryController.GrantLoot</c>. Every other kill —
        /// open world, wave, dungeon, arena — is passed straight through to
        /// <see cref="MaterialBaseFromGold"/> UNCHANGED, so the WO-1216 balance the owner has
        /// already felt-verified is bit-for-bit what it was.</para>
        /// <para>PURE and static on purpose: the raid state is an ARGUMENT, not a lookup, so a
        /// regression can assert both branches with no scene, no raid and no enemy
        /// (<c>KillRewardRaidSuppressionRegression</c>). The caller — Enemy's death grant — is the
        /// only place that reads the live raid state, so there is exactly one place that can be
        /// wrong about it.</para>
        /// </summary>
        public static int KillMaterialBase(int goldBase, string material, bool raidInProgress)
        {
            if (raidInProgress) return 0;
            return MaterialBaseFromGold(goldBase, material);
        }

        /// <summary>Force a re-read (test / hot-reload).</summary>
        public static void Reload() { _data = null; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            _data = LoadData();
        }

        private static KillRewardBalanceData LoadData()
        {
            var parsed = DeNelle.Core.Diagnostics.Guard.Try("Reward", "load kill-rewards.json", () =>
            {
                string json = DeNelle.Core.CanonicalJson.Read(StreamingRelativePath);
                if (string.IsNullOrEmpty(json))
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Reward",
                        "kill-rewards.json not found (Resources or StreamingAssets) -- using built-in " +
                        "default kill-reward balance (mult 1.1 / floor 6 / cap 40).");
                    return (KillRewardBalanceData)null;
                }
                var d = Newtonsoft.Json.JsonConvert.DeserializeObject<KillRewardBalanceData>(json);
                if (d == null)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Reward",
                        "kill-rewards.json parsed null -- using built-in default kill-reward balance.");
                    return (KillRewardBalanceData)null;
                }
                if (d.Version != ExpectedVersion)
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Reward",
                        $"kill-rewards.json version {d.Version} != expected {ExpectedVersion} -- loading anyway (additive).");
                int overrides = d.PerMaterialMultiplier != null ? d.PerMaterialMultiplier.Count : 0;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reward",
                    $"KillRewardBalanceCatalog loaded (version {d.Version}, mult {d.GoldToMaterialMultiplier:0.00}, " +
                    $"floor {d.MaterialFloorPerKill}, cap {d.MaterialCapPerKill}, {overrides} per-material overrides).");
                return d;
            }, fallback: null);

            if (parsed != null) return parsed;
            DeNelle.Core.Diagnostics.FlowTrace.Warn("Reward",
                "KillRewardBalanceCatalog falling back to built-in default balance (file missing/invalid).");
            return new KillRewardBalanceData();
        }
    }
}
