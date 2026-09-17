// =============================================================================
// HeroHealth — the hero's HP, contact damage from nearby enemies, and a visible
// health bar. Restores the "hero can take damage + has a health bar" loop the
// owner asked for (DEF playtest 2026-05-28).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// DESIGN (deliberately self-contained + low-risk):
//   • The hero is transform-driven with a manual CapsuleCast and NO physical
//     collider (HeroLocomotion). Adding a collider would make the hero collide
//     with itself, so instead HeroHealth pulls damage IN: each interval it scans
//     for living enemies within EngageRadius (Enemy layer) and takes contact
//     damage. Combined with EnemyBrain's hero-engage targeting, enemies that
//     reach the hero now actually hurt it.
//   • The bar is drawn with IMGUI (OnGUI) — no UIDocument / PanelSettings / uGUI
//     dependency, so it always renders in player builds (UI-Toolkit HUDs have
//     repeatedly come up empty in this project).
//   • Self-bootstraps: a tiny persistent manager attaches HeroHealth to the hero
//     (the HeroAbilities GameObject) whenever a scene with a hero loads.
//
// Tuning constants are first-pass — tune for feel.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Combat;

namespace DeNelle.Village
{
    /// <summary>Hero hit points + contact-damage intake + an IMGUI health bar.</summary>
    [DisallowMultipleComponent]
    public sealed class HeroHealth : MonoBehaviour, IDamageableStructure
    {
        public static HeroHealth Instance { get; private set; }

        [SerializeField] private float _maxHp = 100f;

        // Gear v1: equipped armor (fractional damage reduction). Lazily resolved in TakeDamage.
        private GearLoadout _gear;

        // ── Contact-damage tuning (first-pass) ────────────────────────────────
        private const float EngageRadius   = 1.5f;  // enemy must be this close to strike
        private const float DamageInterval = 1.0f;  // seconds between contact ticks
        private const float DamagePerEnemy = 6f;    // FALLBACK only — used if an attacker's real
                                                    // ContactDamage is non-positive (mis-authored def).
        private const int   MaxEnemiesPerTick = 4;  // cap so a swarm can't one-shot

        private float _hp;

        // Last world position that dealt damage — drives directional death clips (owner 2026-07-03).
        private Vector3? _lastDamageSourceWorld;

        // ── WO-1773: the two numbers nobody could answer ──────────────────────────────────
        // An external tester's report — "at the level he is nothing can damage him he can just
        // stand there" — cost a pixel scan of 146 one-second video frames to investigate, because
        // NOTHING in the game recorded whether the hero had ever actually lost HP. These three
        // members are that record, and they exist for two consumers:
        //   1. TownRegenTunables' out-of-combat rule needs "how long since HP actually dropped".
        //      Note ACTUALLY DROPPED, after mitigation — a hit that armour, a shield, a parry, a
        //      dodge or a full block erased is not damage taken, and gating regen on it would make
        //      a fully-mitigating build regen LESS than one that takes hits.
        //   2. WaveManager's per-wave summary reports the running total, so a wave that cost the
        //      hero nothing says so in one log line instead of needing a video.
        // Session-scoped and deliberately NOT reset by RestoreToFull / respawn: the question these
        // answer is "has anything in this session ever hurt him", which a heal does not change.
        private float _totalDamageTaken;
        private float _lastDamageTakenTime = float.NegativeInfinity;
        private string _lastAttackerName;

        /// <summary>
        /// WO-1773: total HP the hero has LOST to damage this session, post-mitigation. Never reset
        /// by a heal or a respawn — see the field comment for why.
        /// </summary>
        public float TotalDamageTaken => _totalDamageTaken;

        /// <summary>
        /// WO-1773: seconds since the hero last actually LOST HP, or
        /// <see cref="float.PositiveInfinity"/> if that has never happened this session.
        /// <para>Infinity rather than 0 on the never-hit case is load-bearing: TownRegenTunables
        /// compares this against a suppression window, and 0 would read as "hit this very frame" and
        /// suppress town regen forever for a hero who has never been touched — turning a combat gate
        /// into a permanent no-heal bug on a fresh save.</para>
        /// </summary>
        public float SecondsSinceDamageTaken
            => float.IsNegativeInfinity(_lastDamageTakenTime)
                ? float.PositiveInfinity
                : Time.time - _lastDamageTakenTime;
        private float _cooldown;
        private int   _enemyMask;
        private float _nearMissProbeTimer;   // WO-792: throttles the adjacent-but-out-of-sphere probe
        private bool  _isDead;
        private bool  _hudBlocking;
        private const float HudBlockDamageMultiplier = 0.45f;
        private readonly Collider[] _buf = new Collider[24];

        // ── v2 talent behavioural state (WO-566 effect interpreter) ───────────────
        // Reusable buffer of the enemies struck this contact tick — the reflect handler
        // bounces a share of the damage taken back to them. Sized to match _buf.
        private readonly Enemy[] _attackerBuf = new Enemy[24];

        // Last Stand capstone: an active low-HP defensive window (+DR +reflect) on cd.
        private bool  _lastStandActive;
        private float _lastStandUntil;     // Time.time the active window ends
        private float _lastStandReadyAt;   // Time.time the cooldown frees the next trigger
        private float _lastStandDr;        // extra fractional DR during the window
        private float _lastStandReflect;   // extra reflect fraction during the window

        // Eternal Aegis capstone: an auto-emergency full-invuln window on a long cd.
        // (Capstone exclusivity means a Knight holds Last Stand OR Eternal Aegis, never both.)
        private float _aegisReadyAt;       // Time.time the cooldown frees the next trigger
        private const float AegisAutoThreshold = 0.25f;  // auto-fires below this projected HP fraction

        // Legendary Resolve (shared): one cheat-death per run.
        private bool _revivedThisRun;

        /// <summary>True while the Last Stand window is live (auto-expires when the timer passes).</summary>
        private bool LastStandActive
        {
            get
            {
                if (_lastStandActive && Time.time >= _lastStandUntil) _lastStandActive = false;
                return _lastStandActive;
            }
        }

        // ── Respawn (DEF-102) ─────────────────────────────────────────────────
        // The hero is NOT the lose condition — the Heart is (a Heart breach
        // escalates to the ATB / Defend-the-Tower flow; there is no game-over
        // screen for the wave loop). So when the hero falls it enters a brief
        // "down" beat then RESPAWNS at its start point rather than reloading the
        // scene. Tunables are SerializeField so feel can be dialled in-editor.
        [Header("Death / Respawn (DEF-102)")]
        [Tooltip("Seconds the hero stays down (no control, death pose) before respawning.")]
        [SerializeField] private float _downSeconds = 1.75f;
        [Tooltip("Fraction of max HP restored on respawn (1 = full).")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _respawnHpFraction = 1f;
        [Tooltip("Seconds of damage immunity after respawn so the hero isn't instantly re-killed.")]
        [SerializeField] private float _respawnInvulnSeconds = 1.5f;

        private Vector3 _spawnPosition;          // captured in Awake — respawn anchor
        private float   _invulnUntil;            // Time.time at which post-respawn invuln ends

        // ── F8-15 death forensic window (owner 2026-07-08) ────────────────────
        // Catch-all HERO-MOVED monitor: while DeathTrace's window is live, LateUpdate
        // compares this frame's position to last frame's; a single-frame jump > 2m is
        // non-locomotive (max walk speed 6 m/s -> ~0.1m/frame) and gets logged even if
        // no warp chokepoint attributed it. Zero cost outside the window (one static check).
        private Vector3 _deathTraceLastPos;
        private bool    _deathTraceHasPos;
        private const float DeathTraceJumpMeters = 2f;

        // -- Death-pin (F8 2026-07-16 "on death I shake back and forth, no death sequence") --
        // The prior fix (EnterDeathFreeze) STOPPED the NavMeshAgent, yet the owner still sees the
        // body shake. Statically the frozen agent (isStopped + updatePosition=false) cannot write
        // the transform, so a SECOND mover is shaking the dead hero and hiding the death pose.
        // Rather than guess which mover (agent / root motion / lock-face / a stray component), we
        // PIN the root transform to the death pose for the down-beat: LateUpdate is the LAST writer
        // each frame (after the agent's internal update, HeroLocomotion.Update, and OnAnimatorMove
        // root motion), so re-asserting the pinned pose there wins over any mover and the body holds
        // still. The visible death clip animates the HeroBody CHILD mesh (applyRootMotion=false), so
        // pinning the ROOT never touches the death animation. LateUpdate also FAIL-logs the residual
        // delta a mover tried to apply, so the next device capture NAMES the culprit on [Flow:HeroDeath].
        private bool       _deathPinActive;
        private Vector3    _deathPinPos;
        private Quaternion _deathPinRot;
        private int        _deathPinResidualLogs;

        // WO-284/285: death/revive animation routes through the canonical ActorAnimator
        // driver (Dead bool latch + DeathDir). Guarded internally — a controller without
        // a Death state is a silent no-op, never the per-frame param-spam pitfall.
        private ActorAnimator _actor;
        private Animator _deathAnimator;
        private AnimatorUpdateMode _deathAnimatorPriorUpdateMode;

        // Cached siblings for death-stop + haptics. All optional — resolved in
        // Awake and only used through null-safe calls, so a hero missing any of
        // them simply skips that bit of feedback.
        private HeroLocomotion     _locomotion;
        private HeroAbilities      _abilities;
        private HeroImpactFeedback _impactFeedback;
        private PlayerAttackController _pac;   // perfect-parry source (same GameObject)

        // WO-543: equipped armor + accessories add a flat HP bonus folded into the EFFECTIVE max.
        // GearLoadout.GearHpBonus is the single source; resolved lazily + synced in Update so
        // equipping a +HP ring grows the bar and tops the hero up by the delta (and unequipping
        // shrinks it + clamps). 0 when no GearLoadout / no HP gear, so existing combat is unchanged.
        private int GearHpBonus => _gear != null ? _gear.GearHpBonus : 0;
        private int _appliedEffectiveHpBonus;

        // v2 talents (WO talent-tree): Vitality / Elarion's Blessing fold a fractional max-HP
        // bonus into the effective max, the SAME way gear HP does (so the bar grows + the hero
        // tops up by the delta when a +HP node is learned mid-run, and clamps when respec'd).
        private string HeroClassOrDefault
        {
            get
            {
                if (_abilities == null) _abilities = GetComponent<HeroAbilities>();
                return _abilities != null ? _abilities.HeroClass : "knight";
            }
        }
        private int TalentHpBonus
        {
            get
            {
                float m = DeNelle.Village.Talents.HeroTalentModifiers.MaxHpMultiplier(HeroClassOrDefault);
                return Mathf.RoundToInt(_maxHp * Mathf.Max(0f, m - 1f));
            }
        }
        // Cathedral mage HP is an additive fraction of BASE max HP. Keep it separate
        // from the talent multiplier and flat gear so it can never compound either.
        private int CathedralMageHpBonus => Mathf.RoundToInt(_maxHp *
            DeNelle.Village.Talents.HeroTalentModifiers.MageMaxHpBonusPct(HeroClassOrDefault));
        private int EffectiveBonus => GearHpBonus + TalentHpBonus + CathedralMageHpBonus;

        public float MaxHp    => _maxHp + EffectiveBonus;
        public float Hp       => _hp;
        public bool PracticeDefeated { get; private set; }

        public void RestoreAfterPractice(float hp)
        {
            if (gameObject.scene.name != DeNelle.Core.Combat.PracticeCombatPolicy.SceneName) return;
            PracticeDefeated = false;
            _hp = Mathf.Clamp(hp, 1f, MaxHp);
            OnHealthChanged?.Invoke(_hp, MaxHp);
            UpdateInjuredState();
        }
        public float Fraction => MaxHp > 0f ? Mathf.Clamp01(_hp / MaxHp) : 0f;
        public bool  IsAlive  => _hp > 0f;

        // ── WO-493 #5 / WO-497: HERO injured stance (the hero half; the ENEMY half is
        //    Enemy.DriveAnimator). Below the low-HP cutoff the hero reads "wounded":
        //    the Injured locomotion swap (ActorAnimator.SetInjured), a breathing red
        //    screen-edge vignette, a slight move slow, and an optional heartbeat cue.
        //    All flag-gated by FeatureFlags.HeroInjuredStance. ─────────────────────
        /// <summary>
        /// The wounded cutoff. PUBLIC since WO-888 so the world-space HP aura
        /// (<see cref="HeroHpStateAura"/>) drives its severity ramp off THIS number rather
        /// than a second copy of 0.30 that could drift away from the stance/vignette.
        /// </summary>
        public const float InjuredFraction = 0.30f;  // enter injured below this HP fraction

        /// <summary>
        /// The near-death cutoff, WO-888. Deliberately the SAME number as
        /// <see cref="AegisAutoThreshold"/>: "about to die" means one thing in this game, and
        /// the emergency capstone and the near-death aura must agree on it or the player gets
        /// a rescue at a moment the screen never warned them about. Aliased, never re-typed.
        /// </summary>
        public const float NearDeathFraction = AegisAutoThreshold;

        private bool  _injured;                        // current injured latch (set on threshold cross)
        private HeroInjuredVignette _vignette;         // optional edge vignette (resolved in Awake)

        // WO-888 (ACCESSIBILITY): the world-space HP aura - the PRIMARY low-HP tell. The red
        // edge vignette below it is now a SECONDARY, redundant cue: the owner is red/green
        // colourblind, so a colour-only danger signal is a bug, but a colour signal ALONGSIDE
        // a shape/rhythm signal is good redundancy and is kept for players who can see it.
        private HeroHpStateAura _hpAura;
        private float _heartbeatCooldown;              // throttles the optional heartbeat cue
        private static AudioClip s_heartbeatClip;      // generated once, shared

        // Movement slow seam: a global multiplier the hero's locomotion can read to
        // ease the felt move speed while wounded. Defaults to 1 (no change). Kept as a
        // public static so HeroLocomotion can consume it WITHOUT a hard reference back
        // to HeroHealth (and so this WO touches only HeroHealth/vignette/factory).
        public static float MoveSpeedMultiplier { get; private set; } = 1f;
        private const float InjuredMoveScale = 0.85f;  // ~15% slower while wounded

        /// <summary>True while the hero is below the low-HP injured cutoff.</summary>
        public bool IsInjured => _injured;

        /// <summary>Fired whenever HP changes — args = (current, max).</summary>
        public event Action<float, float> OnHealthChanged;
        /// <summary>Fired once when HP reaches zero.</summary>
        public event Action OnDied;

        private void Awake()
        {
            Instance = this;
            // FIX 2: start at the EFFECTIVE max (base + gear + talent maxHpPct), not the bare
            // serialized base. With a talent like Vitality the effective max can be ~195 vs a
            // base 100, so seeding _hp from _maxHp made the hero spawn at 100/195 (~0.51 frac) —
            // the bar read half-empty. MaxHp is the same effective max the Fraction calc uses.
            _hp = MaxHp;
            _enemyMask = LayerMask.GetMask("Enemy");
            if (_enemyMask == 0) _enemyMask = ~0;   // "Enemy" layer missing — scan all

            _locomotion     = GetComponent<HeroLocomotion>();
            _abilities      = GetComponent<HeroAbilities>();
            _impactFeedback = GetComponent<HeroImpactFeedback>();
            if (!TryGetComponent(out _actor)) _actor = gameObject.AddComponent<ActorAnimator>();

            // WO-493 #5 / WO-497: the hero's low-HP screen-edge vignette. Self-attached so
            // it needs no prefab wiring (mirrors HeroHitReaction). Resolved up-front here.
            if (!TryGetComponent(out _vignette)) _vignette = gameObject.AddComponent<HeroInjuredVignette>();

            // WO-888: the world-space HP aura, self-attached beside the vignette (same
            // no-prefab-wiring pattern). It owns ONE loop handle and stops it on every exit
            // path; see HeroHpStateAura's header for the full list.
            _hpAura = HeroHpStateAura.Ensure(gameObject);

            MoveSpeedMultiplier = 1f;   // start un-slowed every fresh hero

            // Capture the spawn point as the respawn anchor. Resolved later in
            // HandleDeath against the Heart if the recorded point is unsafe.
            _spawnPosition = transform.position;
        }

        private void OnDestroy()
        {
            SetBlocking(false);
            if (Instance == this) Instance = null;
        }

        /// <summary>Mobile press-and-hold guard state. The HUD owns gesture lifetime; health owns
        /// the authoritative mitigation and animation verb. Releasing, disabling, or destroying
        /// the input surface always calls this with false.</summary>
        public bool IsBlocking => _hudBlocking;

        public void SetBlocking(bool blocking)
        {
            bool next = blocking && !_isDead && _hp > 0f;
            if (_hudBlocking == next) return;
            _hudBlocking = next;
            if (_actor == null) TryGetComponent(out _actor);
            _actor?.SetBlocking(_hudBlocking);
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroHealth",
                "mobile block " + (_hudBlocking ? "HELD" : "RELEASED"));
        }

        private void Start()
        {
            // Resolve gear up-front so the starting bar reflects any persisted HP gear, then
            // top the hero to the effective full so a +HP loadout doesn't read as "missing HP".
            if (_gear == null) _gear = GetComponent<GearLoadout>();
            _appliedEffectiveHpBonus = EffectiveBonus;
            _hp = MaxHp;
            // HP-desync ticket 2026-07-02: prove the resolved max + its composition at spawn so the
            // next capture names the one pool (base + gear + talent + Cathedral = N).
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroHealth",
                $"max resolved: base {_maxHp:F0} + gear {GearHpBonus} + talent {TalentHpBonus} + cathedral {CathedralMageHpBonus} = {MaxHp:F0} " +
                $"(id={GetInstanceID()} scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}')");
            OnHealthChanged?.Invoke(_hp, MaxHp);
        }

        // WO-543: keep the effective max in sync with the equipped HP gear. On a bonus INCREASE
        // (equipped a +HP ring), top the hero up by the delta so the new HP is usable; on a
        // DECREASE (unequipped), clamp current HP to the smaller max. Cheap; runs each frame.
        private void SyncGearHp()
        {
            if (_gear == null) _gear = GetComponent<GearLoadout>();
            int now = EffectiveBonus;   // gear + talent + Cathedral HP folded together
            if (now == _appliedEffectiveHpBonus) return;
            int delta = now - _appliedEffectiveHpBonus;
            _appliedEffectiveHpBonus = now;
            if (delta > 0) _hp += delta;           // grow with the new max
            _hp = Mathf.Min(_hp, MaxHp);           // clamp to the (possibly smaller) max
            // HP-desync ticket 2026-07-02: the effective max just CHANGED (gear equip/unequip or a
            // talent learn/respec) — re-log the composition so every capture can name the live pool.
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroHealth",
                $"max resolved: base {_maxHp:F0} + gear {GearHpBonus} + talent {TalentHpBonus} + cathedral {CathedralMageHpBonus} = {MaxHp:F0} " +
                $"(id={GetInstanceID()} changed by {delta:+#;-#;0})");
            OnHealthChanged?.Invoke(_hp, MaxHp);
        }

        // In Defend-the-Tower the hero is a safe turret on the stand — the TOWER is
        // what enemies attack, not the hero. Resolve once, then skip contact damage.
        private bool _modeChecked;
        private bool _safeTurretMode;

        // ═════════════════════════════════════════════════════════════════════════════════
        // WO-1750 — "DEAD BY THE HEALTH MODEL, STILL FIGHTING" (§12 instrument-first)
        // ---------------------------------------------------------------------------------
        // CAPTURED SYMPTOM (owner felt-test, Seeker tester build 2026.09.15.371127, scene
        // RaidBase_IronBastion): the HP bar rendered EMPTY while the hero was mid-swing on a
        // live target, and the enemy brain's own guard reported the contradiction twice —
        //   09-15 13:46:29.381 [Flow:EnemyAggro] raidboss-iron_bastion: still steered at the
        //   hero via Enemy.DriveNav/brain while HeroHealth.IsAlive=false ...
        // IsAlive is `_hp > 0f` (:196), so "IsAlive=false" is exactly "HP is at zero", and the
        // empty bar agrees with it. What the capture did NOT settle is WHICH of two shapes:
        //
        //   (A) HP reached zero through TakeDamage, the death path ran, and something still
        //       let input through afterwards; or
        //   (B) HP reached zero WITHOUT TakeDamage — in which case `_isDead` was never set,
        //       OnDeath/OnDied never fired, HandleDeath never ran, and every consumer that
        //       reads IsAlive (enemy aggro, the HP bar, the target indicator) sees a corpse
        //       while every consumer that reads component state sees a living hero.
        //
        // (B) has real reachable seams: SyncGearHp's `_hp = Mathf.Min(_hp, MaxHp)` (:324) can
        // clamp to zero if the effective max ever resolves to zero, and the `_hp = MaxHp`
        // seeds in Awake (:251) / Start (:304) inherit whatever MaxHp resolves to at that
        // instant. This watchdog is the discriminator: it fires ONLY in the (B) shape — HP at
        // or below zero with no death latch — which is a state the game has no legitimate way
        // to be in outside the practice scene. One Fail, once per entry into the state.
        //
        // ⚠ Fail IS THE CORRECT SEVERITY HERE and it does NOT contradict
        // HeroDeathSeverityRegression. That suite bans Fail on NORMAL-LIFECYCLE prose only —
        // its banned list is "death freeze armed" / "death pin rebased" / "revive" /
        // "respawn" — and its own scope note keeps the LateUpdate residual watchdog's Fail for
        // exactly this reason: a Fail that fires only when something is genuinely broken is a
        // working alarm. A normal death sets _isDead on the same frame HP hits zero, so a
        // normal death never reaches this line.
        // ═════════════════════════════════════════════════════════════════════════════════
        private bool  _zeroHpNoDeathReported;
        private float _zeroHpNoDeathSince = -1f;

        /// <summary>
        /// WO-1750. How long the anomaly must PERSIST before it is converted into a death.
        /// <para>
        /// Not a design delay — a debounce. The effective max is assembled across three frames'
        /// worth of seams (<c>Awake</c> :251, <c>Start</c> :304 and <c>SyncGearHp</c> :323-324,
        /// which runs one line above this watchdog every frame), and a rig whose
        /// <c>GearLoadout</c> has not resolved yet can read a transient zero. Killing the hero
        /// off a single frame of rig assembly would be a far worse defect than the one this
        /// closes. A quarter of a second is far longer than any assembly transient and far
        /// shorter than a player could notice.
        /// </para>
        /// </summary>
        private const float ZeroHpNoDeathGraceSeconds = 0.25f;

        private void WatchZeroHpWithoutDeath()
        {
            if (_isDead || PracticeDefeated)
            {
                _zeroHpNoDeathReported = false;
                _zeroHpNoDeathSince    = -1f;
                return;
            }
            // The practice scene deliberately floors HP at 1 and never dies; nothing to watch.
            if (gameObject.scene.name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName) return;
            if (_zeroHpNoDeathReported) return;

            // Debounce (see ZeroHpNoDeathGraceSeconds): start the clock on the first frame of the
            // anomaly and do nothing until it has held. The clock is cleared on the alive path in
            // Update, so a transient that resolves never reaches the line below.
            if (_zeroHpNoDeathSince < 0f) { _zeroHpNoDeathSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _zeroHpNoDeathSince < ZeroHpNoDeathGraceSeconds) return;

            _zeroHpNoDeathReported = true;

            string stack;
            try { stack = new System.Diagnostics.StackTrace(1, false).ToString(); }
            catch { stack = "(stack unavailable)"; }

            DeNelle.Core.Diagnostics.FlowTrace.Fail("HeroDeath",
                "ZERO HP WITH NO DEATH LATCH (WO-1750 shape B): hp=" + _hp.ToString("F2") +
                "/" + MaxHp.ToString("F2") + " (base=" + _maxHp.ToString("F0") +
                " gear=" + GearHpBonus + " talent=" + TalentHpBonus + " cathedral=" + CathedralMageHpBonus + ")" +
                " isDead=false IsAlive=false" +
                " scene='" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "'" +
                " goScene='" + gameObject.scene.name + "'" +
                " id=" + GetInstanceID() +
                " raidInProgress=" + DeNelle.Village.RaidScoring.RaidInProgress +
                " enemyOwned=" + DeNelle.Village.SceneOwnership.IsEnemyOwned +
                " | OnDeath listeners=[" + ListenerNames(OnDeath) + "]" +
                " | OnDied listeners=[" + ListenerNames(OnDied) + "]" +
                " | HP reached zero WITHOUT passing through TakeDamage's lethal branch, so no " +
                "death event fired and no handler ran. Every IsAlive consumer now reads a corpse " +
                "while the hero is still under player control. Held for >=" +
                ZeroHpNoDeathGraceSeconds.ToString("F2") + "s, so this is not an assembly " +
                "transient. Reached from:\n" + stack);

            // ── RECOVERY: RUN THE RULE CANON ALREADY HAS, RATHER THAN INVENTING ONE ──────────
            // Owner ruling WO-1526 already says what a hero at zero HP inside a live raid means:
            // the raid CONTINUES, capped at 2 stars, with "HERO DOWN - your army fights on" on
            // screen. That outcome is produced by BeginDeathSequence's own
            // RaidScoring.NotifyHeroDied latch plus HandleDeath's RaidDeployController
            // .NotifyHeroDown (line numbers deliberately NOT quoted here - WO-1768 moved the
            // latch out of HandleDeath and the old ":1270" citation went stale the same day),
            // and the ONLY thing wrong in this state is that the sequence which starts
            // HandleDeath never ran. So run it.
            //
            // ⛔ NOT A SECOND DEATH PATH. BeginDeathSequence is the SAME body TakeDamage calls -
            // the lethal block, moved, not copied - so the town rule, the arena deferral, the
            // raid branch, the evac branch and every listener behave identically to a hero who
            // died to damage. Nothing here decides what death MEANS; it only stops the game from
            // sitting in a state no ruling describes.
            //
            // ⛔ AND IT CANNOT BE DONE VIA TakeDamage. `if (_hp <= 0f || amount <= 0f) return;`
            // (:685) refuses every call once HP is at zero, which is precisely why this state is
            // terminal and why the entry has to be direct.
            //
            // FIRES EXACTLY ONCE. _zeroHpNoDeathReported is already true above, and
            // BeginDeathSequence sets _isDead as its first mutation - after which the guard at
            // the top of this method returns before reaching any of this. Both latches are set
            // before the first one could be re-read.
            bool ran = BeginDeathSequence(DeathCauseZeroHpNoLatch);
            DeNelle.Core.Diagnostics.FlowTrace.Warn("HeroDeath",
                "zero-hp recovery: BeginDeathSequence(" + DeathCauseZeroHpNoLatch + ") " +
                (ran ? "RAN - the hero is now properly down and the canon rule for this state " +
                       "(WO-1526 in a live raid; the town/evac branches elsewhere) is executing."
                     : "DECLINED - an exemption above refused it (FTUE peace window or practice " +
                       "scene). The hero stays at zero HP and the input refusal is what holds; " +
                       "that is the intended outcome for those two states, not a failure."));
        }

        /// <summary>
        /// WO-1750. True once the lethal branch has latched this hero as down. Public so the
        /// direct-call input surfaces can refuse on the SAME state the death path sets, rather
        /// than on a second copy of the rule.
        /// </summary>
        public bool IsDeathLatched => _isDead;

        // ═════════════════════════════════════════════════════════════════════════════════
        // WO-1750 — THE INPUT-REFUSAL SEAM, AND WHY `enabled = false` WAS NOT ONE.
        // ---------------------------------------------------------------------------------
        // EnterDeathFreeze (:~1430) turns the hero's input surfaces OFF by component:
        //     _pac.enabled = false;   // PlayerAttackController
        // and HandleDeath does the same for _locomotion and _abilities. That contract holds for
        // anything driven by Unity's Update loop — which is the ONLY path a keyboard/mouse
        // build ever takes, and is why this was never felt on desktop.
        //
        // IT DOES NOT HOLD FOR A DIRECT CALL. `enabled = false` suppresses Unity's own
        // callbacks; it does not make a public method unreachable. The phone's one attack
        // button goes through HudKitCommandBridge, which resolves its target with
        //     Object.FindAnyObjectByType<PlayerAttackController>()      (HudKitCommandBridge.cs:108)
        //     Object.FindAnyObjectByType<HeroAbilities>()               (HudKitCommandBridge.cs:109)
        // and then calls `abilities.TryCast(AbilitySlot.Q)` and `atk.TriggerBasicAttack()`
        // DIRECTLY. FindAnyObjectByType filters on GameObject ACTIVE state, not on component
        // ENABLED state, so a disabled component on a living GameObject is found exactly as
        // before and its methods run exactly as before. Neither method had a dead-hero gate.
        //
        // So on mobile the death freeze disabled three components and changed nothing about
        // what the attack button does. That is a located candidate for the owner's mid-swing
        // screenshot, not a proven cause — the refusal below is BOTH the guard and the
        // instrument: when it fires, the Throttle line names which surface was being driven
        // and the next capture settles it in one read.
        //
        // The predicate is a PURE STATIC so a headless regression can test its truth table
        // with no scene and no play mode — the same shape as HeroLocomotion's
        // EvaluateInputSuppressed, which DialogueInputGateRegression tests the same way.
        //
        // ⛔ DELIBERATELY NOT HeroLocomotion.InputSuppressed. That latch is owned by the
        // dialogue/tutorial beat and carries a STUCK-GATE WATCHDOG (WO-1714,
        // HeroLocomotion.cs:412-420) that Fails when it stays raised. A hero who is down for
        // the rest of a live raid (WO-1526: the raid continues, the hero stays down) would
        // hold it for minutes and trip that alarm every time. Two owners, one flag = the
        // duplicated state CLAUDE.md §2/§5/§16 each warn about.
        // ═════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// WO-1750. Pure predicate — "should a direct hero-input call be refused because the
        /// hero is down?". <paramref name="heroPresent"/> false means there is no health model
        /// at all (test scenes, headless rigs): that reads as ALIVE and never refuses, the same
        /// conservative reading BattleArena takes ("bool heroAlive = hh == null || hh.IsAlive").
        /// </summary>
        public static bool EvaluateInputRefusedForDeath(bool heroPresent, bool heroIsAlive, bool deathLatched)
        {
            if (!heroPresent) return false;
            return !heroIsAlive || deathLatched;
        }

        /// <summary>
        /// WO-1750. The predicate evaluated against a specific hero rig. Resolves the health
        /// model from <paramref name="heroGo"/> first (the component sitting beside the input
        /// surface) and falls back to <see cref="Instance"/>, so it is correct both for a
        /// component asking about itself and for a bridge holding only a found reference.
        /// </summary>
        public static bool InputRefusedForDeath(GameObject heroGo)
        {
            HeroHealth hh = null;
            if (heroGo != null) heroGo.TryGetComponent(out hh);
            if (hh == null) hh = Instance;
            if (hh == null) return false;
            return EvaluateInputRefusedForDeath(true, hh.IsAlive, hh.IsDeathLatched);
        }

        private void Update()
        {
            SyncGearHp();   // WO-543: fold equipped HP gear into the effective max (top-up / clamp on change)
            if (_hp <= 0f) { WatchZeroHpWithoutDeath(); UpdateInjuredState(); return; }
            // WO-1750: HP is back above zero, so re-arm the shape-B alarm. This MUST live on the
            // alive path: WatchZeroHpWithoutDeath only runs while HP is at zero, so a latch that
            // could only be cleared inside it would stay set for the lifetime of the hero and the
            // SECOND occurrence - the one that proves the state is recurring rather than a one-off
            // - would be silent. An alarm that fires once per process is barely an alarm.
            _zeroHpNoDeathReported = false;
            _zeroHpNoDeathSince    = -1f;   // and the debounce clock, so a transient that resolved leaves no residue

            // WO-493 #5 / WO-497: re-evaluate the wounded stance every frame off the single
            // HP-fraction source of truth. Cheap: the latch only flips the visuals/anim/slow
            // on an actual threshold CROSS; while injured it just pulses the optional heartbeat.
            UpdateInjuredState();

            if (!_modeChecked)
            {
                _modeChecked = true;
                _safeTurretMode = false;   // Defend-the-Tower mode removed — always normal village contact damage
            }
            if (_safeTurretMode) return;   // enemies target the tower, not the hero

            _cooldown -= Time.deltaTime;
            if (_cooldown > 0f) return;

            Vector3 centre = transform.position + Vector3.up * 0.9f;
            // Use Collide (not Ignore): PatriciaLight ("Defend the Tower") spawns its
            // enemies with TRIGGER colliders, so an Ignore sweep finds nothing and the
            // hero never takes damage there. Collide matches the hero/pet attack sweeps.
            int n = Physics.OverlapSphereNonAlloc(centre, EngageRadius, _buf, _enemyMask,
                                                  QueryTriggerInteraction.Collide);
            int attackers = 0;
            for (int i = 0; i < n; i++)
            {
                var en = _buf[i] != null ? _buf[i].GetComponentInParent<Enemy>() : null;
                if (en != null && !en.IsDead)
                {
                    if (attackers < _attackerBuf.Length) _attackerBuf[attackers] = en;
                    attackers++;
                }
            }

            if (attackers > 0)
            {
                _cooldown = DamageInterval;
                // WO-419: the actual "enemy attacks hero" beat — an enemy entered the 1.5 m
                // engage ring and the hero self-applies the contact tick. Tracing it here (the
                // ground truth for the seam bug) shows in a headless run that OuterWorld enemies
                // now reach + damage the hero, not just path toward it. Throttled ~1/sec.
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("EnemyAggro", "hero-hit", 1f,
                    $"hero struck by {attackers} adjacent enemy(s) within {EngageRadius:F2}m " +
                    $"(scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}').");
                float hpBeforeTick = _hp;
                // WO-591 RCA: honour each adjacent enemy's REAL authored ContactDamage (from
                // enemies.json, post wave-scaling) instead of a single flat number — so berserker
                // (15) vs necromancer (18) vs walker (8) finally differ, and ApplyWaveScaling's
                // damageMult reaches the hero. Capped at MaxEnemiesPerTick so a swarm can't one-shot.
                int counted = Mathf.Min(attackers, Mathf.Min(MaxEnemiesPerTick, _attackerBuf.Length));
                float tickDamage = 0f;
                for (int i = 0; i < counted; i++)
                {
                    var atk = _attackerBuf[i];
                    float dmg = atk != null ? atk.ContactDamage : 0f;
                    tickDamage += dmg > 0f ? dmg : DamagePerEnemy; // fallback if a def authored 0
                }
                // Primary attacker sets the death-direction bucket if this tick is lethal.
                if (_attackerBuf[0] != null)
                {
                    _lastDamageSourceWorld = _attackerBuf[0].transform.position;
                    // WO-1773: name the attacker for the damage-taken trace. Best-effort and for
                    // READING ONLY — the melee tick SUMS up to MaxEnemiesPerTick attackers into one
                    // TakeDamage call, so this is the primary of a group, which is why the line
                    // reports it alongside the count-bearing [Flow:EnemyAggro] line above rather
                    // than instead of it. TakeDamage's signature is deliberately not widened: it is
                    // also reached through IDamageableStructure.ApplyContactDamage, and changing
                    // either signature would touch every ranged and structure damage source.
                    _lastAttackerName = counted > 1
                        ? _attackerBuf[0].name + " +" + (counted - 1) + " more"
                        : _attackerBuf[0].name;
                }
                TakeDamage(tickDamage);
                // WO-566: v2 talent reflect (Retaliation Surge) + the Last Stand reflect portion
                // bounce a fraction of the damage ACTUALLY taken (post block/DR) back onto the
                // contact attackers. Identity (0) until a reflect node is learned.
                ApplyReflect(hpBeforeTick - _hp, Mathf.Min(attackers, _attackerBuf.Length));
            }
            else
            {
                // WO-792 probe (leave in until the outpost fight is felt-proven, s12): an enemy
                // that is visually adjacent but OUTSIDE the 1.5m engage sphere - e.g. a
                // floating/mis-seated body whose collider hovers above its ground slot - is
                // exactly the felt "enemy attacks do zero damage". Name it in the trace instead
                // of silence. Throttled to one wide probe per 2s; no gameplay effect.
                _nearMissProbeTimer -= Time.deltaTime;
                if (_nearMissProbeTimer <= 0f)
                {
                    _nearMissProbeTimer = 2f;
                    int wide = Physics.OverlapSphereNonAlloc(centre, 3.5f, _buf, _enemyMask,
                                                             QueryTriggerInteraction.Collide);
                    for (int i = 0; i < wide; i++)
                    {
                        var en = _buf[i] != null ? _buf[i].GetComponentInParent<Enemy>() : null;
                        if (en == null || en.IsDead) continue;
                        Vector3 d = en.transform.position - transform.position;
                        float dy = d.y; d.y = 0f;
                        if (d.magnitude <= 2.2f && Mathf.Abs(dy) > 1.0f)
                            DeNelle.Core.Diagnostics.FlowTrace.Warn("EnemyAggro",
                                $"NEAR-MISS: '{en.name}' is {d.magnitude:F2}m away planar but OUT of the 1.5m " +
                                $"engage sphere (dy={dy:F2}m) - a mis-seated/floating body lands ZERO damage.");
                        break;   // first live near enemy is enough for the probe
                    }
                }
            }
        }

        // F8-15: the catch-all hero-jump monitor for the death forensic window. LateUpdate so
        // it samples AFTER every mover this frame (warps, agent, coroutines) has run. Dark
        // outside the window: the first check is one static property read.
        private void LateUpdate()
        {
            // Death-pin (F8 2026-07-16): while pinned, re-assert the death pose AFTER every other
            // mover has run this frame so nothing can shake the body — and FAIL-log the residual a
            // mover tried to apply so the next capture NAMES it on [Flow:HeroDeath]. Runs independent
            // of the DeathTrace window below.
            if (_deathPinActive)
            {
                Vector3 residual = transform.position - _deathPinPos;
                float dPos = residual.magnitude;
                float dYaw = Quaternion.Angle(transform.rotation, _deathPinRot);
                if ((dPos > 0.001f || dYaw > 0.05f) && _deathPinResidualLogs < 5)
                {
                    _deathPinResidualLogs++;
                    var a = GetComponent<UnityEngine.AI.NavMeshAgent>();
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("HeroDeath",
                        "RESIDUAL move fought the death pin (re-pinned): dPos=" + dPos.ToString("F3") +
                        "m dir=" + residual + " dYaw=" + dYaw.ToString("F2") + "deg | agent=" +
                        (a != null ? "present" : "none") + " updatePosition=" +
                        (a != null ? a.updatePosition.ToString() : "n/a") + " isStopped=" +
                        (a != null && a.enabled && a.isOnNavMesh ? a.isStopped.ToString() : "n/a") +
                        " rootMotion=" + (_actor != null && _actor.Animator != null ? _actor.Animator.applyRootMotion.ToString() : "n/a") +
                        " locoEnabled=" + (_locomotion != null && _locomotion.enabled) +
                        " -> a mover OTHER than the frozen agent is writing a dead hero's transform.");
                }
                transform.position = _deathPinPos;   // hold the death pose - no shake
                transform.rotation = _deathPinRot;
            }

            if (!DeNelle.Core.Diagnostics.DeathTrace.Active) { _deathTraceHasPos = false; return; }
            // F8-15: LateUpdate runs even at Time.timeScale==0, so it is the ticker that catches a
            // hub game-over pause that was set and never restored (GameOverScreen freeze). Self-reports once.
            DeNelle.Core.Diagnostics.DeathTrace.PollFreezeStuck();
            Vector3 now = transform.position;
            if (_deathTraceHasPos &&
                (now - _deathTraceLastPos).sqrMagnitude > DeathTraceJumpMeters * DeathTraceJumpMeters)
            {
                // A chokepoint (WarpTo / WarpHero / Respawn) should ALSO have logged this move
                // with its caller; this line firing ALONE means an unattributed mover exists.
                DeNelle.Core.Diagnostics.DeathTrace.HeroMoved(_deathTraceLastPos, now,
                    "<frame-jump monitor — see adjacent chokepoint line for the mover, or NONE = unattributed>",
                    "single-frame jump > " + DeathTraceJumpMeters + "m during death window");
            }
            _deathTraceLastPos = now;
            _deathTraceHasPos  = true;
        }

        /// <summary>
        /// WO-566: bounce a fraction of the damage just taken back to the contact attackers
        /// (Retaliation Surge reflect + Last Stand reflect window). Data-driven — the fraction
        /// comes from <see cref="HeroTalentModifiers.ReflectFraction"/> (+ the active Last Stand
        /// reflect), so a hero with no reflect node reflects nothing. Split evenly across the
        /// enemies that struck this tick; each share routes through Enemy.TakeDamageFrom so the
        /// hit shows a number + flinches toward the hero.
        /// </summary>
        private void ApplyReflect(float damageTaken, int attackerCount)
        {
            if (damageTaken <= 0f || attackerCount <= 0) return;
            string heroClass = HeroClassOrDefault;
            float frac = DeNelle.Village.Talents.HeroTalentModifiers.ReflectFraction(heroClass);
            if (LastStandActive) frac += _lastStandReflect;
            if (frac <= 0f) return;
            float total = damageTaken * frac;
            if (total <= 0f) return;
            float share = total / attackerCount;
            int reflectedTo = 0;
            for (int i = 0; i < attackerCount; i++)
            {
                var en = _attackerBuf[i];
                _attackerBuf[i] = null;   // release the reference
                if (en == null || en.IsDead) continue;
                en.TakeDamageFrom(share, transform.position + Vector3.up * 1.0f);
                reflectedTo++;
            }
            if (reflectedTo > 0)
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroTalents", "reflect", 1f,
                    $"reflected {total:F0} dmg ({frac:P0} of {damageTaken:F0}) across {reflectedTo} attacker(s)" +
                    (LastStandActive ? " [Last Stand window]" : "") + ".");
        }

        /// <summary>Applies <paramref name="amount"/> damage; fires events; handles death.</summary>
        /// <summary>
        /// Records the world position of the attacker about to deal damage so a lethal
        /// hit can pick a directional death clip. Called by <see cref="Enemy"/> contact/ranged
        /// paths before <see cref="IDamageableStructure.ApplyContactDamage"/>.
        /// </summary>
        public void NoteDamageSource(Vector3 worldPosition) => _lastDamageSourceWorld = worldPosition;

        public void TakeDamage(float amount)
        {
            if (PracticeDefeated && gameObject.scene.name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName) return;
            // WO-triage 2026-06-27 (HP-desync): owner saw stagger/limp + DEFEAT while the HUD read
            // 100/100. Log WHICH HeroHealth instance + scene actually takes damage — if this id/scene
            // differs from the one the HUD binds (the [Flow:HUD] HP line), the arena spawns a SECOND
            // hero and the overworld HUD stays bound to the untouched 100/100 body. Proves it from data.
            // NOTE (HP-desync ticket 2026-07-02): log the EFFECTIVE max (base + gear + talent) —
            // the previous line logged the bare serialized _maxHp (100), which read as a third
            // "scale" next to the HUD's effective 155/120 and mis-diagnosed a desync that wasn't.
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroHealth",
                $"TakeDamage id={GetInstanceID()} scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' " +
                $"amount={amount:F1} hpBefore={_hp:F1}/{MaxHp:F1} (base={_maxHp:F0}) invuln={(Time.time < _invulnUntil)}");
            if (_hp <= 0f || amount <= 0f) return;
            // WO-1773: remember the RAW incoming amount and the pre-hit HP before the mitigation
            // chain below rewrites `amount` in place. The throttled trace at the bottom reports raw
            // vs mitigated, which is the pair that tells a reader whether a hero is not being HIT or
            // is being hit and not being HURT — two completely different defects that the existing
            // entry-time line cannot distinguish, because it logs `amount` before any mitigation.
            float rawIncoming = amount;
            float hpBeforeHit = _hp;
            // WO-1773: CONSUME-AND-CLEAR the attacker label. TakeDamage is public and has callers
            // beyond the two paths that set a label (dragon fire, burn, anything calling it
            // directly), and a field left standing would hand those callers the PREVIOUS labelled
            // attacker - a troll from three seconds ago, named confidently in a log. Reading it once
            // and nulling it means an unlabelled caller reports "(direct/unknown)", which is true.
            string attackerLabel = string.IsNullOrEmpty(_lastAttackerName)
                ? "(direct/unknown)" : _lastAttackerName;
            _lastAttackerName = null;
            // DEF-102: post-respawn grace — ignore damage during the invuln window
            // so a hero respawning into a lingering melee isn't instantly re-killed.
            if (Time.time < _invulnUntil) return;

            // WO-910: dodge talent — full miss (no DR stack); identity when chance is 0.
            if (_abilities == null) _abilities = GetComponent<HeroAbilities>();
            string dodgeClass = _abilities != null ? _abilities.HeroClass : null;
            float dodge = DeNelle.Village.Talents.HeroTalentModifiers.DodgeChance(dodgeClass);
            if (dodge > 0f && UnityEngine.Random.value < dodge)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroHealth",
                    $"DODGE id={GetInstanceID()} chance={dodge:F2} amount={amount:F1} (WO-910)");
                return;
            }

            // Perfect parry — a hit landing inside the player's parry window is NEGATED and turned
            // into the riposte payoff (Knight block now; the caster's magical deflect reuses the
            // same OpenParryWindow seam). Same-GameObject lookup, lazily cached.
            if (_pac == null) _pac = GetComponent<PlayerAttackController>();
            if (_pac != null && _pac.TryConsumeParry())
            {
                _pac.OnParrySuccess(transform.position + Vector3.up);
                return;   // fully negate the parried hit
            }

            // Gear v1: equipped armor reduces incoming damage (fractional). Lazily-resolved;
            // graceful — no GearLoadout / no armor = no reduction, so combat is unchanged.
            if (_gear == null) _gear = GetComponent<GearLoadout>();
            if (_gear != null && _gear.ArmorDefense > 0f)
                amount *= (1f - _gear.ArmorDefense);

            // WO-861: the ABILITY-DRIVEN timed damage shield (Thrain's Arcane Shell -40%/4s AND
            // the Knight's Warden's Grace -20%). THIS LINE IS THE CONSUMER, and until it existed
            // BOTH were INERT.
            // Found 2026-08-02 while building Arcane Shell: WO-750 declared
            // GraceDamageReduction = 0.20f but only ever used the const inside a LOG STRING that
            // read "-20% DR PENDING HeroHealth seam" - so Warden's Grace has reduced exactly
            // nothing since it shipped, while both the log and the tooltip claimed otherwise.
            // HeroAbilities now owns ONE timed-mitigation store (ApplyDamageShield) that Grace and
            // Arcane Shell both write, so there is one producer and one reader rather than two
            // mitigation systems. Seated AFTER gear armor and BEFORE the talent block/DR chain so
            // it composes multiplicatively with armor exactly as the talent DR below does.
            // Identity (1f) whenever no shield is active, so baseline combat is unchanged.
            if (_abilities == null) _abilities = GetComponent<HeroAbilities>();
            if (_abilities != null)
            {
                float shieldMult = _abilities.DamageTakenMultiplier;
                if (shieldMult < 1f)
                {
                    amount *= shieldMult;
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroTalents", "dmg-shield", 1f,
                        $"damage shield active: incoming x{shieldMult:0.##} (ability timed mitigation).");
                }
            }

            // Mobile Block is a deliberate held guard, not a random talent proc. It composes
            // multiplicatively with armor and timed shields and never fully negates a hit; the
            // perfect-parry seam above remains the only skill-timed full negate.
            if (_hudBlocking)
            {
                amount *= HudBlockDamageMultiplier;
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroHealth", "mobile-block", 0.5f,
                    "held Block reduced incoming damage to 45%. Release restores normal damage.");
                VFXManager.Play(VFXType.Impact_Physical, transform.position + Vector3.up * 1.0f);
            }

            // v2 talents (Knight V1): Guardian Stance can fully BLOCK a hit; Iron Resolve /
            // Resilience / defense nodes reduce the rest. Identity (no block, 0 DR) until a
            // defensive node is learned, so combat is unchanged at baseline.
            string heroClass = HeroClassOrDefault;

            // WO-566: arm the emergency low-HP capstones (Last Stand / Eternal Aegis) BEFORE the
            // hit lands so their window protects against this very blow. Capstone exclusivity
            // means at most one of these is ever owned at a time, so they never stack.
            UpdateEmergencyTalents(heroClass, amount);

            // An Eternal Aegis (or respawn) invuln window may have just opened above — re-honor it.
            if (Time.time < _invulnUntil) return;

            if (DeNelle.Village.Talents.HeroTalentModifiers.RollBlock(heroClass))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroTalents", "block", 1f,
                    "Guardian Stance blocked a hit (full negate).");
                VFXManager.Play(VFXType.Impact_Physical, transform.position + Vector3.up * 1.0f);
                return;
            }
            float talentDr = DeNelle.Village.Talents.HeroTalentModifiers.IncomingDamageReduction(heroClass);
            // WO-566: Last Stand folds an extra DR slice on top while its window is live.
            if (LastStandActive) talentDr += _lastStandDr;
            talentDr = Mathf.Clamp(talentDr, 0f, 0.95f);
            if (talentDr > 0f) amount *= (1f - talentDr);

            float newHp = Mathf.Max(0f, _hp - amount);

            // FTUE SAFETY NET (F8 2026-07-08 "died in tutorial"): belt-and-suspenders for the
            // ambient-spawn suppression — even if a pre-placed / stray hostile lands a hit, the
            // hero can be HURT (the scripted teaching wave still reads as real) but can NEVER die
            // while the first-time tutorial is active: a would-be-lethal blow is floored at 1 HP.
            // Gated on the SAME condition the spawners use, so it LIFTS the instant onboarding
            // completes (TutorialFlow.HostilesSuppressedForTutorial -> !Onboarded flips false).
            if (newHp <= 0f && TutorialFlow.HostilesSuppressedForTutorial)
            {
                newHp = 1f;
                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroHealth",
                    "FTUE guard: would-be-lethal hit floored at 1 HP — tutorial death suppressed.");
            }

            _hp = newHp;
            OnHealthChanged?.Invoke(_hp, MaxHp);

            // ── WO-1773: THE DECISIVE LINE, and the damage-taken record it is built on ────────
            // Recorded HERE — after the whole mitigation chain, at the point HP actually moved —
            // and not at entry. "Took damage" means lost HP: a dodge, a parry, a full talent block
            // or a shield that erased the hit all return above this point, so none of them arms the
            // out-of-combat timer. That ordering is what stops a fully-mitigating build from
            // regenerating LESS than a build that actually gets hurt.
            float hpLost = hpBeforeHit - _hp;
            if (hpLost > 0f)
            {
                _totalDamageTaken += hpLost;
                _lastDamageTakenTime = Time.time;

                // Throttled ~1/sec, NOT per hit. The contact tick fires once a second per attacker
                // group and ranged fire is unbounded, so an unthrottled line here would flood the
                // device logcat ring and evict the boot window — the exact evidence-destroying
                // failure CLAUDE.md section 12 records (memory logcat-ring-buffer-destroys-evidence).
                // The unthrottled entry-time Step above is UNTOUCHED: WO-1773 section 7 tells the
                // owner to grep for it, and section 12 forbids removing instrumentation.
                // The attacker label (consumed-and-cleared at entry) and the blocked share are
                // LOCALS, never inline. A nested quote inside an interpolation hole is precisely the
                // shape CLAUDE.md section 1 records the compile gate's brace scanner cannot model —
                // it has no interpolated-string state, so the inner quote ends the string and the
                // rest of the file scans as code, withholding COMPILE_GATE_OK on a clean file.
                float blockedShare = rawIncoming > 0f ? (1f - hpLost / rawIncoming) : 0f;
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroHealth", "damage-taken", 1f,
                    $"TakeDamage attacker='{attackerLabel}' " +
                    $"raw={rawIncoming:F1} mitigated={hpLost:F1} (blocked {blockedShare:P0}) " +
                    $"hp={_hp:F0}/{MaxHp:F0} sessionTotalTaken={_totalDamageTaken:F0}. " +
                    "Pair this with the [Flow:SafeZone] town regen line: a hit followed immediately " +
                    "by a regen tick of comparable size IS WO-1773's dead step A on one screen.");
            }

            // WO-566: Legendary Resolve (shared) — cheat death ONCE per run. When a hit would drop
            // the hero to 0 and the revive is still available, restore to a fraction of max HP with
            // a brief grace window instead of dying. Identity until the node is learned.
            if (_hp <= 0f && !_isDead && !_revivedThisRun
                && DeNelle.Village.Talents.HeroTalentModifiers.TryGetRevive(heroClass, out float reviveFrac))
            {
                _revivedThisRun = true;
                _hp = MaxHp * reviveFrac;
                _invulnUntil = Time.time + 1.5f;   // brief grace so the revive isn't instantly re-killed
                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroTalents",
                    $"Legendary Resolve REVIVE: cheat death — restored to {reviveFrac:P0} HP (once per run).");
                VFXManager.Play(VFXType.Impact_Heal, transform.position + Vector3.up * 1.0f);
                OnHealthChanged?.Invoke(_hp, MaxHp);
                return;
            }

            // ── Combat feel (additive) ────────────────────────────────────────
            // VFXManager.Play and HitStopManager.DoImpact are static + null-safe,
            // so absent managers are a silent no-op. Contact ticks use the Light
            // tier (shake only, no time-freeze) so the 1 s cadence never stutters.
            VFXManager.Play(VFXType.Impact_Physical, transform.position + Vector3.up * 1.0f);
            _impactFeedback?.PlayHaptic(0.25f, 0.12f);
            GameSfx.PlayHeroHit();   // hero took a hit — audible grunt/impact (was silent)

            if (_hp <= 0f && !_isDead) BeginDeathSequence(DeathCauseLethalHit);
            else HitStopManager.DoImpact(HitTier.Light);   // subtle shake per hit
        }

        /// <summary>WO-1750: the ordinary death — a hit took HP to zero.</summary>
        internal const string DeathCauseLethalHit = "damage";

        /// <summary>
        /// WO-1750: the recovery death — HP was found at zero with no death latch, a state
        /// <see cref="TakeDamage"/> can no longer resolve on its own (see the watchdog's header).
        /// </summary>
        internal const string DeathCauseZeroHpNoLatch = "zero-hp-no-latch recovery";

        // ═════════════════════════════════════════════════════════════════════════════════
        // WO-1750 — THE ONE DEATH SEQUENCE, NOW REACHABLE FROM TWO PLACES.
        // ---------------------------------------------------------------------------------
        // WHAT MOVED AND WHAT DID NOT. This method is the lethal block that used to live inline
        // in TakeDamage, MOVED VERBATIM — every line, comment and ordering below is the original.
        // It is kept inside its original brace block deliberately, so the diff reads as a move
        // rather than a rewrite and a reviewer can see that nothing in the sequence changed.
        // NOTHING WAS COPIED: TakeDamage now calls this, and so does the zero-HP watchdog. There
        // is exactly one body, which is the whole point — a second hand-written death sequence
        // would be the duplicated state CLAUDE.md sec.2/5/16 each describe in their own words.
        //
        // WHY THE WATCHDOG CANNOT SIMPLY CALL TakeDamage INSTEAD — proven at source, and it is
        // the same line that makes the defect terminal:
        //     TakeDamage(float amount):  if (_hp <= 0f || amount <= 0f) return;   (:741)
        // Once HP is at zero with no latch, EVERY subsequent call to TakeDamage returns at that
        // line. The lethal branch is permanently unreachable, no matter how many enemies hit the
        // hero. That is why shape B is self-sustaining for a whole raid and why the recovery has
        // to enter the sequence directly.
        //
        // IDEMPOTENCE IS THE LATCH, AND IT IS SET FIRST. `_isDead = true` is the first mutation
        // in the block below, before the trace, the VFX, the freeze or the coroutine. So:
        //   * a second call re-entering here returns at the `_isDead` guard on the first line;
        //   * the watchdog cannot call twice — WatchZeroHpWithoutDeath returns immediately once
        //     `_isDead` is set, and it is the only caller besides TakeDamage;
        //   * even if both somehow fired, RaidScoring.NotifyHeroDied latches on `_heroDied`
        //     (RaidScoring.cs:622-624) and RaidDeployController.NotifyHeroDown latches on
        //     `_heroDownAcknowledged` (RaidDeployController.cs:1417-1418).
        // Three independent latches for one death; the first of them is set here.
        //
        // RETURNS true only when the full sequence ran (so a caller can trace the difference
        // between "died" and "declined"). Never throws by design — every risky step inside the
        // moved body is already Guard-wrapped or null-safe, which was a precondition of the
        // original inline block too.
        // ═════════════════════════════════════════════════════════════════════════════════
        private bool BeginDeathSequence(string cause)
        {
            // IDEMPOTENCE, first and unconditional.
            if (_isDead) return false;
            // Only a hero actually at zero dies here. Protects against a caller that raced a heal.
            if (_hp > 0f) return false;

            // ── EXEMPTION 1: THE FTUE PEACE WINDOW ───────────────────────────────────────
            // TakeDamage already floors a would-be-lethal blow at 1 HP while the first-time
            // tutorial is active (the "died in tutorial" F8 safety net), so the game has a
            // STANDING RULE that the hero cannot die during onboarding. A recovery death that
            // ignored it would reintroduce exactly the defect that net was built for — by a new
            // door. Gated on the same condition the spawners and that net use, so it lifts the
            // instant onboarding completes.
            if (TutorialFlow.HostilesSuppressedForTutorial)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("HeroDeath",
                    "BeginDeathSequence DECLINED (cause=" + cause + "): the FTUE peace window is " +
                    "open, and the hero may never die during onboarding (the same rule TakeDamage's " +
                    "1-HP floor enforces). HP is at " + _hp.ToString("F2") + " and is being left " +
                    "there rather than converted into a tutorial death.");
                return false;
            }

            // ── EXEMPTION 2: THE PRACTICE SCENE ──────────────────────────────────────────
            // Original behaviour, unchanged: local sparring has no death, no penalties and no
            // global death events. It floors at 1 HP and latches PracticeDefeated instead.
            {
                if (gameObject.scene.name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName)
                {
                    // Local sparring defeat: no death penalties, evacuation or global death events.
                    _hp = 1f; PracticeDefeated = true;
                    OnHealthChanged?.Invoke(_hp, MaxHp);
                    return false;   // WO-1750: was a bare `return` when this block was inline in TakeDamage
                }
                // Idempotent: _isDead guards re-entry so a swarm landing several
                // lethal ticks in one frame can't start multiple death coroutines.
                _isDead = true;
                Debug.Log("[HeroHealth] Hero defeated.");
                // F8-15 SLOW TRACE ON DIE (owner 2026-07-08: "three separate pop ups" + "stay on
                // screen so we can see hero fall"): name every death listener at the lethal moment
                // — the popup spam RCA is these invocation lists + the [Flow:ScreenOpen] lines that
                // follow. downSeconds = how long the fallen hero holds before respawn/evac.
                DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                    // WO-1750: the phrase "lethal hit:" is KEPT verbatim - captures and at least one
                    // regression header already grep for it - and `cause=` is appended so the SAME
                    // line now says which door the death came through.
                    "lethal hit: cause=" + cause + " downSeconds=" + _downSeconds.ToString("F1") +
                    " | hero state: hp=" + _hp.ToString("F0") + "/" + MaxHp.ToString("F0") +
                    " pos=" + transform.position + " lastDmgFrom=" + _lastDamageSourceWorld +
                    " enemyOwnedScene=" + DeNelle.Village.SceneOwnership.IsEnemyOwned +
                    " | OnDeath listeners=[" + ListenerNames(OnDeath) + "]" +
                    " | OnDied listeners=[" + ListenerNames(OnDied) + "]" +
                    // WO-1750: NAME THE CALLER. The 09-15 IronBastion capture could not tell
                    // shape (A) "the death path ran and input survived it" from shape (B) "HP
                    // reached zero without ever entering this branch", because the only death
                    // evidence in the log was its absence. The top frame makes this line's
                    // PRESENCE self-describing: if it appears, (A); if the Update watchdog's
                    // "ZERO HP WITH NO DEATH LATCH" appears instead, (B). Frames-only, no file
                    // info — cheap, and this runs once per death, never per frame.
                    " | lethalFrom=" + TopCallerFrame());

                // ── WO-1768 — LATCH THE RAID'S HERO-DEATH CAP *AT THE LETHAL HIT* ────────────
                // This call used to live in HandleDeath, AFTER its 1.75s down-beat
                // `yield return new WaitForSeconds(...)`. The comment there claimed the scorer
                // was told "on EVERY path, before any branch is chosen" - true of the BRANCH,
                // false of the DEATH, and the gap is 1.75 seconds wide.
                //
                // MEASURED COST (owner Seeker, build 2026.09.16.371701, logcat
                // pull-20260916-143101, raid IronBastion):
                //   13:26:03.396  [HeroHealth] Hero defeated.
                //   13:26:05.337  [Flow:Raid] stars settled: 2 (earned=3 heroDied=False cap=none
                //                 honor=2 clamped=min(3,2))
                // The spire fell 1.93s after the hero died - INSIDE the down-beat - so
                // RaidScoring.Finalize ran ApplyHeroDeathCap with _heroDied still false and
                // cap=none. The WO-1526 2-star cap was NOT applied to a raid the hero died in.
                // It landed on 2 only because honor independently clamped 3 -> 2; with honor=3
                // that capture pays 3 stars AND veterancy ranks to a player who fell, which is
                // the owner ruling inverted.
                //
                // THERE IS STILL EXACTLY ONE LATCH AND ONE CALL SITE. NotifyHeroDied latches on
                // RaidScoring._heroDied (RaidScoring.cs:624 `if (_heroDied) return;`) and does
                // nothing else but that and its own trace (read at source 2026-09-16), so it is
                // safe to run this early and on every death, raid or not - Instance is null
                // outside a raid and `?.` no-ops. The former HandleDeath call site is DELETED,
                // not duplicated (RaidScoring.cs:616-620: "There must never be two latches for
                // one death").
                //
                // It sits here, synchronously, before OnDeath/OnDied fire and before
                // HandleDeath is even started, so no listener and no raid conclusion can
                // observe a dead hero whose scorer has not been told.
                //
                // ⚠ THE SPELLING IS LOAD-BEARING, NOT STYLE. HeroDownInputRefusalRegression
                // case 6 [raid-hud] asserts the literal text `raidScorer.NotifyHeroDied()` in
                // this file (HeroDownInputRefusalRegression.cs:268), so the local keeps the name
                // the retired call site used. A `RaidScoring.Instance?.NotifyHeroDied()` one-liner
                // is identical at runtime and turns that suite RED for a cosmetic reason.
                var raidScorer = DeNelle.Village.RaidScoring.Instance;
                if (raidScorer != null) raidScorer.NotifyHeroDied();

                // F8-15 extension (owner 2026-07-08 "capture why so many screens + moving character
                // location"): open the DEATH FORENSIC WINDOW. For the next 15s every screen open
                // (PanelManager / EndStateView), every hero warp/jump (>2m per frame, see
                // TraceDeathWindowJumps), and every camera takeover logs [Flow:DeathTrace] with
                // WHO did it. Window baseline = the death position.
                DeNelle.Core.Diagnostics.DeathTrace.OpenWindow(
                    DeNelle.Core.Diagnostics.DeathTrace.DefaultWindowSeconds,
                    $"hero lethal hit at {transform.position} scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' downSeconds={_downSeconds:F1}");
                _deathTraceLastPos = transform.position;
                _deathTraceHasPos  = true;
                HitStopManager.DoImpact(HitTier.Heavy);   // one dramatic beat on death

                // VFX-FREE-WIN-4: the player's OWN death was the least-marked event in the
                // game — hit-stop plus a death animation, no burst at all, while every enemy
                // kill gets one. The hit that killed already played Impact_Physical above
                // (:584); this is the second beat, the fall itself, so the two read as
                // "struck" then "down" rather than one ambiguous stumble.
                //
                // Death_Generic is ALREADY wired (VFXCatalog.asset row Type:32 ->
                // Lana/Burst/Poof_generic, IsLoop:0 — a ONESHOT; nothing here may take one of
                // the 20 leak-prone loop slots). No new catalog row, no new prefab. Meaning is
                // carried by the outward poof SHAPE at the hero's own body, not by a colour
                // (colourblind law). playSound:false is REQUIRED, not cosmetic: VfxToSfx maps
                // every Death_* to SfxId.EnemyDeath, and firing the ENEMY death sound on the
                // hero's death would misreport who just died. Guarded + null-safe — a missing
                // catalog row or manager degrades to nothing and must never throw inside the
                // lethal-hit path, which is mid-way through setting _isDead.
                DeNelle.Core.Diagnostics.Guard.Try("Death", "hero death burst vfx", () =>
                    VFXManager.Play(VFXType.Death_Generic,
                                    transform.position + Vector3.up * 1.0f,
                                    Quaternion.identity, playSound: false));

                // WO-888: DEATH IS AN EXIT PATH FOR THE HELD HP AURA. The next UpdateInjuredState
                // would stop it anyway (Drive(alive:false)), but a persistent loop must never
                // depend on a later frame arriving - HandleDeath can disable components, warp the
                // hero or hand off to the arena. Stopping here means the near-death gutter is gone
                // on the SAME frame the death burst plays, which is also the right read: the
                // "about to die" signal ends the instant it resolves.
                _hpAura?.StopAll();

                PlayDeathAnim();
                // Freeze the NavMeshAgent IMMEDIATELY (before HandleDeath's down-beat / the
                // deferred-battle wait) so the death pose settles instead of the agent shaking
                // the body in place. Covers every HandleDeath branch (defer / respawn / evac).
                EnterDeathFreeze();
                OnDeath?.Invoke();
                OnDied?.Invoke();   // legacy event kept for existing listeners
                StartCoroutine(HandleDeath());
            }
            // ⚠ StartCoroutine above requires THIS component enabled and its GameObject active.
            // Both callers satisfy that by construction: TakeDamage is driven from the hero's own
            // Update, and the watchdog IS in that same Update. HeroHealth itself is never disabled
            // by the death path - HandleDeath disables _locomotion / _abilities and EnterDeathFreeze
            // disables _pac, never this component - so the coroutine always starts.
            return true;
        }

        /// <summary>
        /// WO-566: arm the low-HP EMERGENCY capstones just before a hit resolves.
        /// <para>
        /// Last Stand — when the projected post-hit HP drops below its threshold and the
        /// cooldown is free, opens a window granting extra DR + reflect (consumed in TakeDamage
        /// / ApplyReflect). Eternal Aegis — an "active" capstone modelled in V1 as an AUTO
        /// emergency: below a small projected HP fraction it triggers a full-invuln window on a
        /// long cooldown (reuses the existing _invulnUntil grace). Both are data-driven (params
        /// from the node) and identity (no-op) until the respective capstone is learned.
        /// </para>
        /// OWNER-DECISION FLAG: Eternal Aegis is authored as a PLAYER-ACTIVATED active. V1 has no
        /// free hotkey / HUD button for a non-slot capstone (keyboard 1-4 are removed, mobile-first),
        /// so it auto-fires here. If the owner wants player-activation, call <see cref="ActivateInvuln"/>
        /// from a HUD button / bound input instead and drop the auto-trigger branch.
        /// </summary>
        private void UpdateEmergencyTalents(string heroClass, float incomingApprox)
        {
            float maxHp = MaxHp;
            if (maxHp <= 0f) return;
            float projectedFrac = Mathf.Clamp01((_hp - Mathf.Max(0f, incomingApprox)) / maxHp);

            // Last Stand
            if (!LastStandActive && Time.time >= _lastStandReadyAt
                && DeNelle.Village.Talents.HeroTalentModifiers.TryGetLastStand(
                       heroClass, out float lsTh, out float lsDr, out float lsRef, out float lsDur, out float lsCd)
                && projectedFrac < lsTh)
            {
                _lastStandActive  = true;
                _lastStandDr      = lsDr;
                _lastStandReflect = lsRef;
                _lastStandUntil   = Time.time + lsDur;
                _lastStandReadyAt = Time.time + lsCd;
                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroTalents",
                    $"Last Stand TRIGGERED: -{lsDr:P0} dmg + reflect {lsRef:P0} for {lsDur:F0}s (cd {lsCd:F0}s).");
                VFXManager.Play(VFXType.Impact_ShockwaveRing, transform.position + Vector3.up * 1.0f);
            }

            // Eternal Aegis (auto-emergency invuln; capstone-exclusive with Last Stand)
            if (Time.time >= _aegisReadyAt
                && DeNelle.Village.Talents.HeroTalentModifiers.TryGetInvuln(heroClass, out float aeDur, out float aeCd)
                && projectedFrac < AegisAutoThreshold)
            {
                _aegisReadyAt = Time.time + aeCd;
                ActivateInvuln(aeDur);
                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroTalents",
                    $"Eternal Aegis TRIGGERED: {aeDur:F0}s invulnerability (cd {aeCd:F0}s).");
                VFXManager.Play(VFXType.Impact_Heal, transform.position + Vector3.up * 1.0f);
            }
        }

        /// <summary>
        /// WO-566: open a damage-immunity window for <paramref name="seconds"/> (reuses the same
        /// _invulnUntil grace the respawn flow uses — every TakeDamage early-returns while it is
        /// live). Public so a future HUD button / bound input can drive Eternal Aegis as a
        /// player-activated active. Extends (never shortens) any existing window.
        /// </summary>
        public void ActivateInvuln(float seconds)
        {
            if (seconds <= 0f) return;
            _invulnUntil = Mathf.Max(_invulnUntil, Time.time + seconds);
        }

        /// <summary>WO-566: clear the per-run talent state — re-arms Legendary Resolve's one
        /// cheat-death and ends any lingering Last Stand window. Cooldowns (Last Stand / Eternal
        /// Aegis) are intentionally NOT reset here; they free on their own timers.</summary>
        private void ResetTalentRunState()
        {
            _revivedThisRun  = false;
            _lastStandActive = false;
        }

        /// <summary>Event fired the moment the hero dies (before the coroutine delay).</summary>
        public event System.Action OnDeath;

        /// <summary>
        /// Death → timed respawn. Disables locomotion + abilities immediately so
        /// the hero is no longer controllable, holds a brief "down" beat, then
        /// revives the hero at its spawn point (near the Heart) at full HP.
        /// <para>
        /// DESIGN (DEF-102): the hero is NOT the lose condition — a Heart breach
        /// is what escalates the run (WaveManager.TriggerBreach → ATB / Defend-
        /// the-Tower). Reloading the scene on hero death would be jarring and
        /// design-wrong, so the hero respawns instead. The old reflection-driven
        /// GameOverUI path is removed: GameOverUI is not placed in the Village
        /// scene, so that path always fell through to a hard scene reload.
        /// </para>
        /// </summary>
        /// <summary>
        /// WO-1750: the first frame OUTSIDE HeroHealth, as "Type.Method". Never throws — a
        /// stack walk that fails degrades to a marker string rather than breaking the lethal
        /// path it is instrumenting.
        /// </summary>
        private static string TopCallerFrame()
        {
            try
            {
                var st = new System.Diagnostics.StackTrace(1, false);
                for (int i = 0; i < st.FrameCount; i++)
                {
                    var m = st.GetFrame(i)?.GetMethod();
                    if (m == null) continue;
                    var t = m.DeclaringType;
                    if (t == null) continue;
                    // A coroutine / lambda frame lives in a compiler-generated NESTED type
                    // (<HandleDeath>d__57, <>c__DisplayClass...), so the declaring type of the
                    // frame is the closure, not the class. Walk out one level before both the
                    // skip test and the name, or a HeroHealth coroutine frame slips the filter
                    // and an enemy's coroutine prints as "<AttackLoop>d__12.MoveNext".
                    var owner = t.IsNested && t.DeclaringType != null ? t.DeclaringType : t;
                    if (owner == typeof(HeroHealth)) continue;
                    return owner.Name + "." + m.Name;
                }
                // Nothing outside HeroHealth on the stack = the hero's OWN contact-damage tick
                // in Update drove this (HeroHealth.cs:~546), which is the ordinary melee death.
                return "(HeroHealth self-tick)";
            }
            catch { return "(stack unavailable)"; }
        }

        /// <summary>F8-15: readable method names of a death event's subscribers (the popup RCA data).</summary>
        private static string ListenerNames(Action evt)
        {
            if (evt == null) return "none";
            var parts = new List<string>();
            foreach (var d in evt.GetInvocationList())
                parts.Add((d.Target != null ? d.Target.GetType().Name : "static") + "." + d.Method.Name);
            return string.Join(", ", parts);
        }

        private IEnumerator HandleDeath()
        {
            // Disable control immediately so a dead hero can't be walked or cast.
            if (_locomotion != null) _locomotion.enabled = false;
            if (_abilities  != null) _abilities.enabled  = false;

            // ARENA OWNS THE DEATH (F8 "Regroup breaks the death cycle", RCA 2026-07-12):
            // while a BattleArena fight is resolving, its loss-return revives the hero at the
            // home anchor — a respawn/evac HERE double-fires (two HeroMoved warps in one death
            // window: this coroutine's town-anchor respawn racing the arena's SafeLossReturn
            // warp). Defer to the arena; SAFETY NET: if nothing has revived the hero within
            // 10s (stuck resolve / torn-down return coroutine), fall through to the normal
            // cycle below so the hero is never left dead+frozen.
            if (DeNelle.Village.Arena.BattleArena.AnyBattleInProgress)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                    "HandleDeath: DEFER — battle in progress; arena loss-return owns recovery (10s net).");
                float netDeadline = Time.time + 10f;
                while (Time.time < netDeadline && _isDead)
                    yield return null;
                if (!_isDead)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                        "HandleDeath: arena recovered the hero — deferred cycle complete, no second warp.");
                    yield break;
                }
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Death",
                    "HandleDeath: arena never recovered the hero within 10s (safety net) — " +
                    "falling through to the normal respawn cycle.");
            }

            DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                "HandleDeath: down-beat starts (" + Mathf.Max(0.1f, _downSeconds).ToString("F1") +
                "s) — the fall animation window; any panel opening before this elapses hides the fall.");

            // Brief "down" beat. WaitForSeconds is scaled time, but the lethal
            // HitStop above restores Time.timeScale within ~0.1s, so this elapses.
            yield return new WaitForSeconds(Mathf.Max(0.1f, _downSeconds));

            // WO-1526 SCOPE NOTE: this header describes the branch that survives BELOW the new
            // live-raid guard — a SETTLED raid, or an enemy-owned non-raid scene (Village2, an
            // enemy dungeon). A death inside a LIVE raid no longer reaches it; the raid continues
            // and the army fights on.
            // RAID-DEATH EVAC: dying in an enemy-owned base ends the raid — retreat
            // to the home hub (MainCastle_Hall) instead of respawning in place. The
            // hub load resets the hero fresh on the far side. Player-owned scenes
            // keep the normal in-place respawn below.
            // WO-1437 — WHICH SIGNAL ANSWERS "am I in a raid?".
            //
            // THE BUG THIS CLOSES, proven by capture, not inferred. This branch used to read
            // `SceneOwnership.IsEnemyOwned` ALONE. That is a FACTION flag, and the victory path
            // DELIBERATELY FLIPS IT: RaidClaimService turns the razed camp player-owned at the
            // win. So the same build, the same scene and the same death produced two outcomes
            // five minutes apart (device log logs/debug/raid-*-2026-09-06.log):
            //
            //   12:59:45  hero death in non-hub scene 'RaidBase_raider_camp_small'
            //             (enemyOwned=True)  -> EVAC -> "hero death settle: partial loot for
            //             32% razed."                                          <- ruled behaviour
            //   13:02:42  "CLAIM - 'raider_camp_small' flipped ENEMY -> PLAYER-owned"
            //   13:02:47  hero death in non-hub scene 'RaidBase_raider_camp_small'
            //             (enemyOwned=False) -> fell through to the in-place respawn below
            //   13:02:49  "HERO MOVED ... by HeroHealth.Respawn reason=in-place respawn at
            //             spawn anchor"                                        <- THE SOFTLOCK
            //
            // The player stood up inside a raid that had already paid out, and kills kept
            // scoring into it ("KILL MATERIALS SUPPRESSED (raid active)" at 13:02:55, :58 and
            // 13:03:00) with no exit left but Retreat. Note the respawn came from THIS coroutine
            // 4s BEFORE the end-state's "action=respawn" at 13:02:53 - the screen was narration;
            // this branch is the mover.
            //
            // RaidScoring.RaidInProgress is the repo's OWN documented "THE ONE 'am I inside a
            // raid' ANSWER" (WO-1227): the scorer's lifetime, with HubScenes.IsRaid as the
            // fallback. It does not move when ownership flips, which is exactly the property
            // this branch needed. Reading it here removes a SECOND answer to one question
            // rather than adding one - the drift RaidScoring's own docs warn against.
            //
            // IsEnemyOwned is KEPT as an OR, not replaced: Village2 and enemy-owned dungeons
            // are not raids and must keep evacuating exactly as they do today.
            bool enemyOwnedScene = DeNelle.Village.SceneOwnership.IsEnemyOwned;
            bool raidInProgress  = DeNelle.Village.RaidScoring.RaidInProgress;
            var  raidScorer      = DeNelle.Village.RaidScoring.Instance;
            bool raidSettled     = raidScorer != null && raidScorer.Finalized;

            // ── WO-1526 — HERO DEATH NO LONGER ENDS A LIVE RAID ──────────────────────────
            // Owner ruling 2026-09-06, verbatim: "Do not let hero death instantly terminate the
            // raid... let the raid continue, but cap the result at 2 stars if the hero dies. That
            // makes hero survival matter without turning the hero into a giant red self-destruct
            // button."
            //
            // MEASURED COST OF THE OLD SHAPE (logs/debug/raid-no-abilities-2026-09-06.log):
            //   12:59:47  [Flow:Raid] hero death settle: partial loot for 32% razed
            // 45 seconds into a 180-second raid, with a full army still standing on the field.
            //
            // LATCH FIRST, BRANCH SECOND — AND THE LATCH NO LONGER LIVES HERE (WO-1768).
            // `raidScorer.NotifyHeroDied()` used to be called on this line. That is AFTER the
            // 1.75s `WaitForSeconds` above, so the cap landed 1.75s after the lethal hit and a
            // raid that concluded inside the down-beat settled with heroDied=False cap=none
            // (capture pull-20260916-143101: hero defeated 13:26:03.396, "stars settled: 2
            // (earned=3 heroDied=False cap=none ...)" 13:26:05.337). The call now lives in
            // BeginDeathSequence, beside `_isDead = true`, where it is latched at the lethal hit
            // itself - which is what "before any branch is chosen" was always meant to mean.
            // ONE latch, ONE call site (RaidScoring.cs:616-620); do not re-add one here.
            // (COMPOSE NOTE: WO-1594 on branch grok/raid-1593-1595 adds the same call for its
            // honor-star snuff. Same seam - on merge take his BODY inside RaidScoring and keep
            // the single BeginDeathSequence call site.)

            // ⛔ THIS TEST MUST PRECEDE THE `enemyOwnedScene ||` BELOW, AND THAT IS THE WHOLE FIX.
            // RaidScoring.RaidDeathEndsRaid's own doc used to claim "flipping it is the whole
            // change"; it was wrong. A LIVE raid base IS enemy-owned (RaidClaimService only flips
            // it at the win), so `enemyOwnedScene` is true and the evac below fired regardless of
            // the constant's value. Flipping the constant alone would have changed nothing.
            //
            // ⛔ `raidScorer != null` IS LOAD-BEARING, NOT DEFENSIVE PADDING. RaidInProgress
            // answers TRUE off a scene-name fallback when the scorer failed to install
            // (RaidScoring.RaidInProgress: "the scene test is a FALLBACK, not the definition").
            // In that degenerate raid there is no clock, no cap to apply, and no
            // StrandingWatchdog either - that lives on RaidDeployController, which is installed
            // beside the scorer. Before WO-1526 the hero's death was the rescue exit from a raid
            // like that; "the raid continues" is only meaningful when there IS a session to
            // continue, so a scorer-less raid keeps the old EVAC and stays escapable.
            bool liveRaidContinues = raidInProgress && raidScorer != null && !raidSettled &&
                                     !DeNelle.Village.RaidScoring.RaidDeathEndsRaid;

            if (liveRaidContinues)
            {
                // Step, never Warn/Fail: a normal hero death must not raise an F8 severity
                // (HeroDeathSeverityRegression pins that, audit 2026-08-15).
                DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                    "HandleDeath: down-beat elapsed -> HERO STAYS DOWN, RAID CONTINUES (WO-1526). " +
                    $"Signal: enemyOwned={enemyOwnedScene} raidInProgress={raidInProgress} " +
                    $"raidSettled={raidSettled} raidDeathEndsRaid={DeNelle.Village.RaidScoring.RaidDeathEndsRaid}. " +
                    "No respawn (WO-1526 sec.3: the 2-star clamp IS the cost and a respawn removes " +
                    "it), no loot settle (sec.3: loot settles when the raid actually ends), no army " +
                    "reconcile, no route home. Locomotion and abilities are already disabled at the " +
                    "top of this coroutine, so the hero is simply out of the fight; the army fights " +
                    "on and the raid ends by objective, Retreat or the clock.");

                var liveRaidDeploy = FindAnyObjectByType<DeNelle.Village.RaidDeployController>();
                if (liveRaidDeploy != null)
                    DeNelle.Core.Diagnostics.Guard.Try("Raid", "hero down - army fights on",
                        () => liveRaidDeploy.NotifyHeroDown());
                else
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                        "HandleDeath: live raid continues but no RaidDeployController was found, so " +
                        "nobody tells the player 'HERO DOWN - your army fights on'. The star cap is " +
                        "still latched on the scorer above; only the status line is lost.");
                yield break;
            }

            // A SETTLED raid always goes home (see RaidScoring.RaidDeathEndsRaid): the loot is
            // paid, the camp is claimed and the clock is stopped, so there is nothing left to
            // fight on inside. An UNSETTLED raid took the branch above.
            bool raidDeathExit = raidInProgress &&
                                 (raidSettled || DeNelle.Village.RaidScoring.RaidDeathEndsRaid);

            // ── WO-1768 — A WON RAID'S VICTORY SCREEN OWNS THE ROUTE HOME ────────────────────
            // The EVAC branch below assumes (its own header says so) that a SETTLED raid "always
            // goes home ... there is nothing left to fight on inside". That is true of the RAID
            // and false of the UI: the victory screen is up, WO-1543 gave it a 30s hold that ANY
            // touch re-arms, and its one primary action is the player's own Return to Castle.
            //
            // MEASURED COST (owner Seeker, build 2026.09.16.371701, logcat
            // pull-20260916-143101, raid IronBastion - the hero died 1.93s BEFORE the spire fell,
            // i.e. inside this coroutine's down-beat):
            //   13:26:05.398  [Flow:DeathTrace] SCREEN OPENED: EndState 'Victory!'
            //   13:26:05.594  the owner's finger lands on it; WO-1543 re-arms the full 30s
            //   13:26:05.617  [Flow:DeathTrace] HERO MOVED: SceneRouter.GoCastle() by
            //                 HeroHealth.HandleDeath              <- THIS BRANCH, over the top
            //   13:26:06.180  SCREEN CLOSED: EndState 'Victory!' by EndStateView.OnDestroy
            //                 (torn down without firing)
            // The victory screen was readable for 0.78 SECONDS, mid-touch. spoils=4 was never
            // read and the CTA never fired.
            //
            // ⛔ THE GATE IS "THE SCREEN IS ACTUALLY UP", NEVER `_handled` AND NEVER `_returning`
            // ALONE - the reasoning is written at source on VictoryOwnsTheReturn, and both of the
            // wrong gates would strand a dead hero on an enemy field (WO-1437).
            //
            // ⛔ AND IT IS A YIELD WITH A BELT, NOT A `yield break`. RaidVictoryController
            // .ReturnHome opens `if (!CanEnterCapturedTown()) return;`, which legitimately
            // REFUSES and toasts when the captured-town census commit has to be retried - so a
            // route home is not unconditional even with the screen up. If the return has not
            // happened within the watchdog, this falls through to the EVAC below exactly as it
            // does today and SAYS WHY. A dead hero is never stranded on an enemy-owned field.
            var victory = FindAnyObjectByType<DeNelle.Village.World.Camps.RaidVictoryController>();
            if (victory != null && victory.VictoryOwnsTheReturn)
            {
                // 45s = comfortably above the 30s auto-dismiss observed in the capture
                // ("'Victory!' auto-dismiss armed at 30s WITH HOLD-ON-TOUCH", logcat 3059807)
                // plus a grace for one re-arm's worth of slack. Deliberately NOT derived from
                // RaidVictoryController._autoReturnSeconds, which is private and serialized -
                // a hold-on-touch screen can outlive ANY fixed window anyway, so the belt is
                // sized to "the guard has plainly failed", not to the guard.
                const float VictoryYieldWatchdogSeconds = 45f;
                string raidSceneName = SceneManager.GetActiveScene().name;

                // Step, never Warn: yielding to a won raid's screen is the CORRECT outcome, and
                // a normal hero death must not raise an F8 severity.
                //
                // ⛔ THE PROSE IN THE NEXT THREE TRACES MAY NOT NAME `GoCastle`,
                // `SettlePartialLoot`, `ReconcileRaidEnd` OR `Respawn(` — AND THAT IS A REAL
                // CONSTRAINT, NOT PEDANTRY. RaidScoringRegression's WO-1526 case takes the FLAT
                // TEXT SPAN from the first `liveRaidContinues` to the first
                // `enemyOwnedScene || raidDeathExit` (RaidScoringRegression.cs:362) and fails if
                // that span CONTAINS any of those four tokens. The span is not brace-matched, so
                // this whole block sits inside it even though it is not the live-raid branch, and
                // a string literal reads to the lint exactly like a call (it strips comments, not
                // strings). The first draft said "no GoCastle from here" and turned the suite RED
                // at 548/550 with the live-raid-branch message. Say "scene route" instead; put
                // anything that must name a method in a COMMENT, which the lint does strip.
                DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                    "HandleDeath: down-beat elapsed -> STAND DOWN, THE VICTORY SCREEN OWNS THE RETURN " +
                    "(WO-1768). VictoryOwnsTheReturn=True on the raid's RaidVictoryController, so the " +
                    "raid was WON while the hero lay in the down-beat. No loot settle, no army " +
                    "reconcile, no Save, no scene route from here - the player reads their own victory " +
                    "screen and leaves by its own button or its own 30s guard. Watchdog armed at " +
                    VictoryYieldWatchdogSeconds.ToString("F0") + "s realtime; scene='" + raidSceneName +
                    "'. Signal: enemyOwned=" + enemyOwnedScene + " raidInProgress=" + raidInProgress +
                    " raidSettled=" + raidSettled + ".");

                float victoryDeadline = Time.realtimeSinceStartup + VictoryYieldWatchdogSeconds;
                while (Time.realtimeSinceStartup < victoryDeadline)
                {
                    // Either the controller went away with its scene, or the active scene changed:
                    // both mean the victory path completed the return and there is nothing left
                    // for the death path to do. realtime + `yield return null` on purpose - an
                    // end-state screen may zero Time.timeScale, which would freeze a scaled wait
                    // forever.
                    if (victory == null || SceneManager.GetActiveScene().name != raidSceneName)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                            "HandleDeath: the victory path completed the return (controller gone or " +
                            "scene left) - the death path stood down with nothing to settle. The hub " +
                            "load's safe-zone recovery revives the hero, exactly as it does on the " +
                            "EVAC route.");
                        yield break;
                    }
                    yield return null;
                }

                DeNelle.Core.Diagnostics.FlowTrace.Warn("Death",
                    "VICTORY YIELD WATCHDOG EXPIRED after " + VictoryYieldWatchdogSeconds.ToString("F0") +
                    "s realtime: the victory screen claimed the return and no return happened, so the " +
                    "hero is still dead in raid scene '" + raidSceneName + "'. Most likely the " +
                    "captured-town census commit is refusing (RaidVictoryController.CanEnterCapturedTown " +
                    "toasts ownedTown.captureRetry and ReturnHome returns early). FALLING THROUGH to the " +
                    "EVAC branch below - a dead hero is never left stranded on an enemy-owned field " +
                    "(WO-1437 / WO-1768).");
            }

            if (enemyOwnedScene || raidDeathExit)
            {
                // Name the deciding signal: a future divergence between these two must be
                // readable straight off a capture instead of re-derived from source.
                DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                    "HandleDeath: down-beat elapsed -> EVAC branch (GoCastle). Signal: " +
                    $"enemyOwned={enemyOwnedScene} raidInProgress={raidInProgress} " +
                    $"raidSettled={raidSettled} raidDeathEndsRaid={DeNelle.Village.RaidScoring.RaidDeathEndsRaid} " +
                    $"-> chose={(enemyOwnedScene ? "enemy-owned scene" : raidSettled ? "SETTLED raid (no session to respawn into)" : "live raid + owner ruling")}. " +
                    "WO-1437: enemyOwned alone used to gate this, and a claimed camp flipped it " +
                    "false mid-raid, which stranded the player inside a won raid.");
                Debug.Log("[HeroHealth] Hero down in enemy territory — raid ends, retreating to home hub.");

                // SETTLE THE ARMY (owner ruling 2026-07-30). Hero death is the THIRD raid exit,
                // and it used to be the only FREE one: Win and Retreat both cost troops, dying
                // cost nothing. That is a perverse incentive - with a raid going badly you were
                // better off dying than pressing Retreat. All three exits are honest now.
                //
                // Death settles EXACTLY like a retreat: 0 stars, so troops still standing when
                // you fall come home intact, troops that already fell become WOUNDED on the
                // recovery timer, nobody is deleted, and no veterancy is granted. The surviving
                // troops "break and flee back to the castle" is pure flavor over that same
                // model - it needs no new troop logic.
                //
                // Null-safe by construction: RaidDeployController only self-installs in
                // RaidBase* scenes, so a non-raid enemy-owned scene finds none and this no-ops.
                // ReconcileRaidEnd is LATCHED, so if a victory or retreat already settled this
                // raid the call is a logged no-op and cannot double-wound.
                var raidDeploy = FindAnyObjectByType<DeNelle.Village.RaidDeployController>();
                if (raidDeploy != null)
                {
                    // WO-1110 §3 — DEATH PAYS WHAT RETREAT PAYS. Death used to reconcile the
                    // army and stop there, never calling RaidScoring.Finalize/LootFor, so a
                    // player who razed two thirds of a base and then FELL got less than one who
                    // razed the same and tapped Retreat. That inverted the incentive the retreat
                    // -loot block exists to remove, and it punished the more committed play.
                    // Owner default (WO-1110, flagged as unruled): the loot is credit for damage
                    // already done, so death settles through the SAME SettlePartialLoot the
                    // retreat/timeout exit uses. It runs BEFORE the reconcile, matching
                    // DoRetreat's order, and is idempotent (RaidScoring.Finalized latch) so a
                    // victory or retreat that already settled makes this a logged no-op.
                    // WO-1768 — MEASURE, THEN NARRATE. Both settles below are LATCHED and both
                    // announce their own no-op; what was unguarded was the NARRATION. On the
                    // 2026-09-16 IronBastion capture this block ran against an already-won raid,
                    // both calls no-oped ("raid already finalized - loot was paid by the first
                    // exit." / "raid-end reconcile already ran for this raid - ignoring the
                    // duplicate call.") and the line after them still announced an army settled
                    // as a failure. The army was intact - army 10 / deployable=10, read 0.09s
                    // later. A trace that asserts work that did not happen turns a clean capture
                    // into a false lead, and it cost that RCA lane its first hour (CLAUDE.md
                    // sec.12). Both traces are KEPT; the false one is now conditional.
                    bool lootAlreadySettled = raidScorer != null && raidScorer.Finalized;
                    bool armyAlreadySettled = raidDeploy.Reconciled;

                    DeNelle.Core.Diagnostics.Guard.Try("Raid", "settle partial loot on hero death",
                        () => raidDeploy.SettlePartialLoot("hero death"));

                    DeNelle.Core.Diagnostics.Guard.Try("Raid", "settle army on hero death",
                        () => raidDeploy.ReconcileRaidEnd(0));

                    bool reconcileRan   = !armyAlreadySettled && raidDeploy.Reconciled;
                    bool lootSettledHere = !lootAlreadySettled && raidScorer != null && raidScorer.Finalized;
                    string lootNote = lootSettledHere ? "settled by this exit"
                                    : lootAlreadySettled ? "already paid by an earlier exit"
                                    : "nothing to settle (no scorer)";

                    if (reconcileRan)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                            "hero DOWN in an enemy-owned scene - army settled as a failure (0 stars); " +
                            // WO-1810 - CORRECTED COPY. This said "the troops still standing break and
                            // flee home, the fallen are wounded", which stopped being true the moment the
                            // owner ruled "any troop killed is dead" (2026-09-16): this exit declares
                            // nothing, so the settlement prices it as a FAIL - the fallen are removed from
                            // the roster and so is every troop still standing. A trace that describes a
                            // settlement the code no longer performs is exactly the false lead WO-1768 paid
                            // an hour for. The NUMBERS are on the reconcile's own casualties line.
                            "the fallen are DEAD and the warband does not come home - the player retrains " +
                            "at the barracks. Partial loot on this exit: " + lootNote + ".");
                    }
                    else
                    {
                        // The truthful opposite line. Deliberately a Step, not a Warn: reaching a
                        // settled raid's EVAC is the ordinary shape of "the hero fell, the raid was
                        // already over", not an anomaly.
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                            "hero DOWN after the raid had already settled - the army was NOT re-settled " +
                            "(reconcile was latched, so this call was a no-op) and nothing new was " +
                            "written: the earlier exit's survivors stand, its wounded stay wounded and " +
                            "its veterancy stands. Partial loot on this exit: " + lootNote +
                            ". (WO-1768)");
                    }

                    // The Save follows the MEASUREMENT, not the branch: it is issued only when
                    // this exit actually changed something - the army reconcile ran, or this exit
                    // was the one that settled the score. A no-op settle writes nothing, so it
                    // must not claim a write either.
                    if (reconcileRan || lootSettledHere)
                        DeNelle.Core.State.GameStateService.Instance?.Save();
                    else
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                            "hero-death EVAC: no Save issued - neither the loot nor the army was " +
                            "settled by this exit, so there is no new state to persist.");
                }
                // F8-15: a scene route is a HERO MOVE (the hub load relocates the hero) — name it.
                DeNelle.Core.Diagnostics.DeathTrace.Note(
                    $"HERO MOVED (pending scene route): SceneRouter.GoCastle() by HeroHealth.HandleDeath from {transform.position} — hub load will relocate the hero");
                DeNelle.Core.SceneRouter.GoCastle();
                yield break;
            }

            // OVERWORLD / HUB death -> respawn at the TOWN (castle courtyard), NOT the frozen
            // per-hero _spawnPosition anchor (F8 2026-07-16 "rspawned in world not town").
            // Main_Castle_Overworld is ONE merged scene holding BOTH the town (castle courtyard
            // at origin) AND the surrounding open world; _spawnPosition is captured ONCE in Awake
            // (the DDOL hero's HeroHealth Awake runs a single time), so it can be anywhere the hero
            // first got HeroHealth -- out in the field -- and an in-place respawn there drops the
            // hero in the WORLD. Route hub/overworld deaths to the canonical town spawn instead
            // (HeroStartPoint_PlayerSpawn marker, else the courtyard centre (0, castle.liftY, 0) --
            // the same navmesh-proven point HomeReturnPortalInjector warps home to). Enemy-owned
            // scenes already EVAC'd above; Village2 (enemy-owned hub) took that branch, so it never
            // reaches here -- only the player-owned home hub / merged overworld does.
            string activeScene = SceneManager.GetActiveScene().name;
            if (DeNelle.Core.HubScenes.IsHub(activeScene))
            {
                Vector3 townSpawn = ResolveTownSpawn();
                DeNelle.Core.Diagnostics.FlowTrace.Step("Respawn",
                    "HandleDeath: down-beat elapsed -> TOWN respawn (hub/overworld '" + activeScene +
                    "') target=" + townSpawn + " (was in-place _spawnPosition=" + _spawnPosition +
                    ") -- hero returns to the castle courtyard, not the world.");
                Respawn(townSpawn);
                yield break;
            }

            DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                "HandleDeath: down-beat elapsed -> in-place respawn branch.");

            // Respawn at the recorded spawn point, falling back to the Heart's
            // position if that point is no longer meaningful (e.g. it was captured
            // at origin before the hero had been placed in the scene).
            Vector3 target = _spawnPosition;
            if (target == Vector3.zero)
            {
                // OWNER RULING 2026-08-05 ("don't spawn inside the tree like we have since day one"):
                // heart.position + forward*4 resolves to (0,0,16) — 4 m from the trunk CENTRE, the
                // deepest-inside value in the codebase. Prefer HubSpawnInjector's tree-edge + 2 m,
                // navmesh-seated point; keep the old expression as the fallback so nothing regresses
                // when the injector has not run (non-hub scene / renderers or navmesh missing).
                if (DeNelle.Village.World.HubSpawnInjector.TryGetHubSpawn(out Vector3 hubSpawn))
                    target = hubSpawn;
                else
                {
                    var heart = FindAnyObjectByType<HeartController>();
                    if (heart != null)
                        target = heart.transform.position + heart.transform.forward * 4f;
                }
            }

            // ── OWNER F8 seq 638/640: "dead in air" + "on death respawned where i died not
            //    back at town". PROVEN from the capture, not inferred:
            //      [Flow:DeathTrace] HERO MOVED: (-24.26, 0.13, 105.65) -> (-24.26, 0.13, 105.65)
            //                        (0.0m) by HeroHealth.Respawn reason=in-place respawn at spawn anchor
            //    A respawn that relocates the hero ZERO METRES. _spawnPosition is captured ONCE in
            //    Awake on the DDOL hero, so in any scene the hub branch above does not claim it can
            //    already equal where the hero is standing when they die - and then "respawning"
            //    puts them straight back on their own corpse. The death freeze pins the body at
            //    pinPos, so the player also just watches themselves lie there: the "dead in air"
            //    half of the same report.
            //    NOTE which branch this is: the capture had NO "TOWN respawn" line, so
            //    HubScenes.IsHub was FALSE - the arena (and any scene not in HubScenes.Names)
            //    falls through to here. Modes that own their own recovery (the arena's
            //    loss-return) DEFER long before this point, so widening the fallback here cannot
            //    fight them.
            //    THE RULE: a respawn must MOVE you. If the resolved target is essentially where
            //    we died, it is not a spawn point - resolve a real one instead of no-oping.
            const float MinRespawnMoveM = 1.5f;
            if ((target - transform.position).sqrMagnitude < MinRespawnMoveM * MinRespawnMoveM)
            {
                Vector3 fallback = ResolveTownSpawn();
                if ((fallback - transform.position).sqrMagnitude < MinRespawnMoveM * MinRespawnMoveM)
                {
                    // Same owner ruling as above: heart.forward*4 is (0,0,16), INSIDE the canopy.
                    // Tree-edge + 2 m first; the old heart-relative expression stays as the fallback.
                    if (DeNelle.Village.World.HubSpawnInjector.TryGetHubSpawn(out Vector3 hubSpawn))
                        fallback = hubSpawn;
                    else
                    {
                        var heart = FindAnyObjectByType<HeartController>();
                        if (heart != null)
                            fallback = heart.transform.position + heart.transform.forward * 4f;
                    }
                }

                DeNelle.Core.Diagnostics.FlowTrace.Warn("Respawn",
                    "in-place respawn target " + target + " is within " + MinRespawnMoveM + "m of the death spot " +
                    transform.position + " (scene '" + activeScene + "', isHub=" +
                    DeNelle.Core.HubScenes.IsHub(activeScene) + ") - that is a respawn ON THE CORPSE. " +
                    "Falling back to " + fallback + ".");
                target = fallback;
            }

            Respawn(target);
        }

        /// <summary>
        /// The canonical TOWN spawn in a hub/overworld scene: the baked
        /// <c>HeroStartPoint_PlayerSpawn</c> marker (its renderer is hidden but the transform
        /// is kept -- CastleSpawnMarkerHider) if present, else the castle courtyard centre
        /// (0, castle.liftY, 0) -- the same navmesh-proven point HomeReturnPortalInjector warps
        /// home to. <see cref="Respawn"/>'s agent.Warp re-samples this onto the courtyard navmesh.
        /// PUBLIC since WO-949 (owner F8 2026-08-10 "On Death I should respawn in town not where
        /// I died"): BattleArena's death-loss return targets THIS same resolver, so every death
        /// context lands on the ONE town anchor rather than a second drifting copy of it.
        /// </summary>
        public static Vector3 ResolveTownSpawn()
        {
            var marker = GameObject.Find("HeroStartPoint_PlayerSpawn");
            if (marker == null) marker = GameObject.Find("HeroStartPoint_InsidePersonalQuarters");
            if (marker != null) return marker.transform.position;   // HubSpawnInjector repoints this at runtime
            // No marker: prefer the injector's resolved tree-edge point over the raw courtyard-centre
            // literal — (0, liftY, 0) is 12 m from the trunk centre and INSIDE the canopy (owner ruling
            // 2026-08-05). Falls back to the literal when the injector has not run (non-hub scene).
            if (DeNelle.Village.World.HubSpawnInjector.TryGetHubSpawn(out Vector3 hubSpawn)) return hubSpawn;
            float liftY = UnityEngine.PlayerPrefs.GetFloat("castle.liftY", 3f);
            return new Vector3(0f, liftY, 0f);
        }

        /// <summary>
        /// Revives the hero at <paramref name="position"/> at full HP and restores
        /// control. Uses NavMeshAgent.Warp when the hero is agent-driven so the
        /// teleport isn't fought by the agent (HeroLocomotion drives a kinematic
        /// NavMeshAgent); also clears the death flag so contact damage resumes.
        /// </summary>
        public void Respawn(Vector3 position)
        {
            // F8-15: attribute the respawn placement in the death forensic window (always logs
            // for this explicit warp, throttled outside the window).
            DeNelle.Core.Diagnostics.DeathTrace.HeroMoved(transform.position, position,
                "HeroHealth.Respawn", "in-place respawn at spawn anchor", always: true);
            // Undo the death freeze first (restore updatePosition) so the warp + resumed
            // locomotion drive the agent normally again.
            ExitDeathFreeze();
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.Warp(position);
            else
                transform.position = position;

            _isDead   = false;
            _hp       = MaxHp * Mathf.Clamp01(_respawnHpFraction <= 0f ? 1f : _respawnHpFraction);
            _cooldown = 0f;
            ResetTalentRunState();   // WO-566: a fresh life re-arms revive / clears Last Stand
            // DEF-102: short grace so the hero isn't re-killed the instant it lands
            // back in a melee. Consumed in TakeDamage.
            _invulnUntil = Time.time + Mathf.Max(0f, _respawnInvulnSeconds);
            // Clear any death pose so the revived hero animates normally again.
            ClearDeathAnim();
            if (_locomotion != null) _locomotion.enabled = true;
            if (_abilities  != null) _abilities.enabled  = true;
            OnHealthChanged?.Invoke(_hp, MaxHp);
            VFXManager.Play(VFXType.Impact_Heal, transform.position + Vector3.up * 1.0f);
            Debug.Log($"[HeroHealth] Hero respawned at {position} (hp={Mathf.CeilToInt(_hp)}, " +
                      $"invuln={_respawnInvulnSeconds:F1}s).");
        }

        // ── Death animation (WO-284/285, fully guarded) ───────────────────────
        // Latches the hero controller's Death state via the canonical Dead bool
        // (ActorAnimator). The death clip holds its last frame and never flickers
        // back to idle; Revive() clears it on respawn. Safe no-op on a controller
        // with no Death state.
        private void PlayDeathAnim()
        {
            if (_actor == null) return;
            var dir = CombatDeathDirection.Resolve(
                transform.position, transform.forward, _lastDamageSourceWorld);
            var anim = _actor.Animator;
            bool hasDead = AnimatorHasParam(anim, "Dead");

            // Death has to advance while hit-stop / arena presentation owns timeScale.
            // Establish that before selecting the state so the first visible death frame
            // cannot be reduced to the lethal-hit camera shake.
            if (anim != null && hasDead)
            {
                _deathAnimator = anim;
                _deathAnimatorPriorUpdateMode = anim.updateMode;
                anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            }

            _actor.Die(dir);

            // DEVICE RCA 2026-08-29: KnightMocap declared valid AnyState transitions and the
            // static regression therefore passed, but the Seeker trace only proved Dead=true;
            // it never proved that the live animator ENTERED a death state. The player saw the
            // lethal hit shake and then the arena return removed the upright body. Select the
            // authored full-body directional state explicitly for this controller. Dead remains
            // latched, so the state's existing !Dead exit still owns revive and the final pose
            // holds until recovery. Other hero controllers keep their existing parameter path.
            string forcedState = ForceKnightDeathState(anim, dir);
            // Prove the death CLIP will actually play (complaint "see death sequence"): name the
            // live animator + its controller and whether the canonical Dead bool is declared. If
            // hasDeadParam is false the controller has no Death latch -> Die() no-ops and the body
            // holds idle (reads as "no death sequence"); every hero controller (incl. KnightMocap)
            // declares Dead + a Death state, so this should log WILL-play on the next capture.
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroDeath",
                "PlayDeathAnim: DeathDir=" + (int)dir +
                " animator=" + (anim != null ? anim.name : "NONE") +
                " ctrl=" + (anim != null && anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "NONE") +
                " hasDeadParam=" + hasDead +
                " forcedState=" + (string.IsNullOrEmpty(forcedState) ? "parameter-route" : forcedState) +
                " -> " + (hasDead ? "death state requested" : "NO Dead param -> death anim NO-OP (body holds idle)") +
                " source=" + (_lastDamageSourceWorld.HasValue ? _lastDamageSourceWorld.Value.ToString() : "none"));

            // DEATH-SHAKE WATCH (owner felt-report 2026-08-30, permanent per CLAUDE.md §12). Every
            // earlier fix targeted the ROOT transform (EnterDeathFreeze, the LateUpdate death-pin)
            // and the shake survived, because the mover was the ANIMATOR: two AnyState death
            // transitions were simultaneously satisfiable while Dead/DeathDir stayed latched, so the
            // base layer ping-ponged between two death states every ~0.06s and every death clip
            // restarted at frame 0. Nothing in the trace could say that, which is why it took three
            // attempts. This watch samples the base layer for the death window and FAILs the moment
            // the state re-enters — one capture now NAMES a recurrence instead of another guess.
            if (anim != null && isActiveAndEnabled) StartCoroutine(DeathStateWatch(anim));
        }

        /// <summary>
        /// Watches the base layer for the first seconds of death and proves whether the death clip
        /// actually PLAYED (one state, normalizedTime advancing to its held final frame) or
        /// RE-ENTERED (the shake signature). Unscaled time — the death animator runs unscaled so it
        /// survives lethal hit-stop. Cheap and bounded: one state read per frame for ~1.5s, once
        /// per death.
        /// </summary>
        private IEnumerator DeathStateWatch(Animator anim)
        {
            if (anim == null) yield break;
            int lastHash = anim.GetCurrentAnimatorStateInfo(0).fullPathHash;
            int changes = 0;
            bool reported = false;
            float elapsed = 0f;
            // NAME THE COLLIDING STATES (2026-09-02). The first cut of this watch reported only a
            // COUNT, and its suggested fix (the DeathDir != N guard on the generic fallback) was
            // ALREADY LIVE in every built controller — so seq 4647 pointed the next seat at code that
            // was already correct. The real collision was death-vs-ACTION (the killing blow's own Hit
            // trigger re-entering the base layer), and a count can never say that. The watch now
            // records the actual state SEQUENCE, so the capture names both sides of the collision.
            var visited = new System.Collections.Generic.List<string>(8) { ResolveBaseStateName(lastHash) };
            while (elapsed < 1.5f)
            {
                yield return null;
                if (anim == null) yield break;
                elapsed += Time.unscaledDeltaTime;
                int hash = anim.GetCurrentAnimatorStateInfo(0).fullPathHash;
                if (hash != lastHash)
                {
                    lastHash = hash;
                    changes++;
                    if (visited.Count < 8) visited.Add(ResolveBaseStateName(hash));
                }
                if (changes >= 3 && !reported)
                {
                    reported = true;
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("HeroDeath",
                        "DEATH ANIMATION IS RE-ENTERING: " + changes + " base-layer state changes in " +
                        elapsed.ToString("F2") + "s on ctrl='" +
                        (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "NONE") +
                        "' | state sequence = " + string.Join(" -> ", visited) +
                        " | two AnyState transitions are simultaneously satisfiable while Dead " +
                        "and DeathDir stay latched, so each death clip restarts at frame 0 and the body " +
                        "reads as a SHAKE. READ THE SEQUENCE ABOVE before theorising: if it names two " +
                        "DEATH states the generic Death fallback is missing a DeathDir != N guard for a " +
                        "directional state; if it names an ACTION state (Hit/Cast/Cast_q..r/Attack*/" +
                        "Victory) then that state's AnyState transition is missing its Dead == false " +
                        "guard. Both guards are authored in HeroAnimatorFactory.");
                }
            }
            var st = anim != null ? anim.GetCurrentAnimatorStateInfo(0) : default;
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroDeath",
                "death state watch: " + changes + " base-layer state change(s) in 1.5s, normalizedTime=" +
                st.normalizedTime.ToString("F2") + " -> " +
                (changes <= 1 ? "death clip played through and holds its final frame." : "UNSTABLE death state."));
        }

        /// <summary>
        /// Maps a base-layer fullPathHash back to a readable state name. Animator exposes no
        /// hash->name lookup at runtime, so this hashes the known "Base Layer.&lt;name&gt;" paths that
        /// HeroAnimatorFactory / KnightPackageControllerBuilder actually author and matches. An
        /// unknown hash reports as "state#&lt;hash&gt;" rather than throwing — a partially-named
        /// sequence still beats a bare count.
        /// </summary>
        private static string ResolveBaseStateName(int fullPathHash)
        {
            for (int i = 0; i < BaseLayerStateNames.Length; i++)
            {
                if (Animator.StringToHash("Base Layer." + BaseLayerStateNames[i]) == fullPathHash)
                    return BaseLayerStateNames[i];
            }
            return "state#" + fullPathHash;
        }

        // Every base-layer state name HeroAnimatorFactory can build (plus the mocap-only extras).
        // Additive only: a name missing here degrades to "state#N", it never breaks the watch.
        private static readonly string[] BaseLayerStateNames =
        {
            "Death", "DeathLeft", "DeathRight", "DeathFront", "DeathBack",
            "Hit", "Cast", "Cast_q", "Cast_w", "Cast_e", "Cast_r",
            "Attack", "Attack0", "Attack1", "Attack2",
            "Locomotion", "CombatLocomotion", "InjuredLocomotion",
            "Block", "Victory", "Unsheathe",
            "TurnLeft", "TurnRight", "TurnLeft180", "TurnRight180",
        };

        private static string ForceKnightDeathState(Animator anim, DeathDirection dir)
        {
            if (anim == null || anim.runtimeAnimatorController == null ||
                !string.Equals(anim.runtimeAnimatorController.name, "KnightMocap", StringComparison.Ordinal))
                return null;

            string stateName = dir switch
            {
                DeathDirection.Left  => "DeathLeft",
                DeathDirection.Right => "DeathRight",
                DeathDirection.Front => "DeathFront",
                DeathDirection.Back  => "DeathBack",
                _                    => "Death"
            };
            int hash = Animator.StringToHash(stateName);
            if (!anim.HasState(0, hash))
            {
                stateName = "Death";
                hash = Animator.StringToHash(stateName);
            }
            if (!anim.HasState(0, hash)) return null;

            anim.CrossFadeInFixedTime(hash, 0.06f, 0, 0f);
            return stateName;
        }

        /// <summary>True if <paramref name="anim"/> declares an animator parameter named
        /// <paramref name="name"/> -- used to PROVE the Death latch exists before claiming the
        /// death clip plays (no per-frame cost; called once on death).</summary>
        private static bool AnimatorHasParam(Animator anim, string name)
        {
            if (anim == null) return false;
            var ps = anim.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i] != null && ps[i].name == name) return true;
            return false;
        }

        private void ClearDeathAnim()
        {
            _lastDamageSourceWorld = null;
            _actor?.Revive();
            if (_deathAnimator != null)
                _deathAnimator.updateMode = _deathAnimatorPriorUpdateMode;
            _deathAnimator = null;
        }

        // ── Death freeze (F8 on-device "hero dies -> stands in place and shakes") ──
        // ROOT CAUSE: the hero is a kinematically-driven NavMeshAgent (HeroLocomotion
        // calls agent.Move each frame; the agent keeps updatePosition=true, Unity's
        // default). On death HandleDeath disables the HeroLocomotion COMPONENT, but the
        // agent itself is left ENABLED and still owns the transform — so while the death
        // pose/clip tries to settle (and any residual root motion / adjacent enemy nudges
        // the body), the agent snaps the transform back to its nextPosition every frame.
        // That agent-vs-pose tug is the visible "shakes in place" — and it hides the death
        // animation because the body never comes to rest. Freezing the agent (stop the
        // path, zero velocity, and stop it writing the transform) hands the body cleanly to
        // ActorAnimator.Die's Death state. Also suppress the attack controller so a dead
        // hero can't swing. Restored on revive (ExitDeathFreeze).
        private void EnterDeathFreeze()
        {
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                if (agent.isOnNavMesh)
                {
                    agent.ResetPath();
                    agent.velocity  = Vector3.zero;
                    agent.isStopped = true;
                }
                agent.updatePosition = false;   // let the death animation own the transform (kills the jitter)
            }

            // Suppress the primary-attack input on the same GameObject so a downed hero
            // can't keep swinging during the down-beat (locomotion/abilities are disabled
            // in HandleDeath; this covers the remaining input surface).
            if (_pac == null) _pac = GetComponent<PlayerAttackController>();
            if (_pac != null) _pac.enabled = false;

            // Belt-and-suspenders (F8 2026-07-16): stopping the agent alone did NOT end the shake, so
            // ALSO neutralize the other candidate movers on a dead hero, then PIN the root pose.
            // 1) root motion must never drive the ROOT here (it should already be off — assert it).
            if (_actor != null && _actor.Animator != null) _actor.Animator.applyRootMotion = false;
            // 2) drop any lock-face yaw slew so nothing keeps re-facing a target on a downed hero.
            _locomotion?.ClearLockFace();
            // 3) arm the death-pin: LateUpdate re-asserts this pose after every mover (see LateUpdate).
            _deathPinPos          = transform.position;
            _deathPinRot          = transform.rotation;
            _deathPinActive       = true;
            _deathPinResidualLogs = 0;

            // Decisive, pullable line: captures the freeze state so a later capture proves the agent
            // was frozen and the pin armed.
            //
            // ⚠ USE Capture, NEVER Fail (audit 2026-08-15). This used to be a FlowTrace.Fail, with the
            // comment "break-log is errors-only on device — use Fail" — true at the time, and the cost
            // was that the MOST COMMON EVENT IN THE GAME raised a permanent, expected F8 ERROR. The
            // owner's triage stream filled with her own deaths and seats learned to ignore Hero
            // failures. FlowTrace.Capture is the severity that was missing: the dump still lands in
            // break-log.jsonl (kind "note") for post-hoc reading, but nothing reads it as a failure
            // and the F8 daemon does not wake on it. Dying is not a bug.
            DeNelle.Core.Diagnostics.FlowTrace.Capture("HeroDeath",
                "death freeze armed: agent=" + (agent != null ? "present" : "none") +
                " isOnNavMesh=" + (agent != null && agent.isOnNavMesh) +
                " updatePosition=" + (agent != null ? agent.updatePosition.ToString() : "n/a") +
                " rootMotion=" + (_actor != null && _actor.Animator != null ? _actor.Animator.applyRootMotion.ToString() : "n/a") +
                " pinPos=" + _deathPinPos +
                " scene='" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "'.");

            DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                "EnterDeathFreeze: agent stopped (updatePosition=false, velocity=0, path reset) + attack input off + root pinned " +
                $"- death pose now owns the transform (agent={(agent != null ? "present" : "none")}).");
        }

        /// <summary>
        /// F8 2026-08-10 (seq 2253/2254/2255, "shakes then dies"): a SANCTIONED warp must REBASE
        /// the death pin, never fight it. The hero died inside the arena warp-space and the pin
        /// held the corpse there (correct); then BattleArena.ReturnHomeWithFade warped the hero
        /// ~7km home and LateUpdate's watchdog read that legitimate move as a residual and
        /// re-pinned the corpse back at the STALE arena spot — while VerifyReturnPose re-asserted
        /// town. Two writers alternating = the visible death shake, and the hero could rest at
        /// the wrong position. Called by <see cref="HeroLocomotion.WarpTo"/> (the ONE sanctioned
        /// teleport authority — arena stage/return warps, seam crossings, gate traversals and the
        /// hub spawn injector all route through it), so after any legitimate teleport the pin
        /// holds the NEW pose and exactly one system decides where a dead hero rests. No-op while
        /// no pin is armed (the common, living-hero case). The watchdog itself stays untouched:
        /// an UNsanctioned mover writing a dead hero's transform is still fought and named.
        /// </summary>
        public void RebaseDeathPin(Vector3 position, Quaternion rotation, string reason)
        {
            if (!_deathPinActive) return;
            Vector3 oldPos = _deathPinPos;
            _deathPinPos          = position;
            _deathPinRot          = rotation;
            _deathPinResidualLogs = 0;   // fresh log budget: a rogue mover at the NEW rest pose still gets named
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroDeath",
                "death pin REBASED by sanctioned warp (" + reason + "): " + oldPos + " -> " + position +
                " — the dead hero now rests at the warp target instead of fighting the mover that moved it.");
        }

        // Revive counterpart to EnterDeathFreeze -- hand the transform back to the agent so
        // the revived hero walks again. Warps the agent's internal position to the (possibly
        // animation-moved) transform BEFORE re-enabling writes so there is no snap.
        private void ExitDeathFreeze()
        {
            // Release the death-pin FIRST so the revive warp + resumed locomotion below are not
            // fought by LateUpdate re-asserting the (now stale) death pose.
            _deathPinActive = false;
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                agent.updatePosition = true;
                if (agent.isOnNavMesh)
                {
                    agent.Warp(transform.position);   // resync internal pos to the transform (no snap-back)
                    agent.isStopped = false;
                }
            }
            if (_pac == null) _pac = GetComponent<PlayerAttackController>();
            if (_pac != null) _pac.enabled = true;

            DeNelle.Core.Diagnostics.FlowTrace.Step("Death",
                "ExitDeathFreeze: agent resumed (updatePosition=true) + attack input on - hero controllable again.");
        }

        /// <summary>Heals up to max (for repair pads / potions / wave-clear).</summary>
        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            _hp = Mathf.Min(MaxHp, _hp + amount);
            OnHealthChanged?.Invoke(_hp, MaxHp);
            // WO-888: the RISING restoration read. A discrete heal already fires the Impact_Heal
            // contact burst below; this stamps the short-lived rising column too so "mending"
            // reads by upward MOTION (the opposite direction to the wounded gutter and to the
            // inward stab of damage) rather than by a green tint. Held only while restoration
            // is actually happening, and outranked by either danger read - see HeroHpStateAura.
            _hpAura?.NotifyRegen();
            UpdateInjuredState();   // T-HP fix (owner 2026-06-27): clear the limp/injured stance once healed back above the cutoff
            VFXManager.Play(VFXType.Impact_Heal, transform.position + Vector3.up * 1.0f);
        }

        /// <summary>
        /// TOWN-FOOTPRINT tick regen (owner 2026-07-08 felt-test: "when in town, life should
        /// recover ... the longer you're in the town/castle footprint"). Called every frame by
        /// <see cref="SafeZoneRecovery"/> while the hero stands inside the town/castle safe ring.
        /// Unlike <see cref="Heal"/> this does NOT fire the heal VFX (a burst every frame would
        /// strobe) and it no-ops when dead or already full. Accumulated small fractional amounts
        /// still climb because <c>_hp</c> is a float. Recovers from the FTUE 1-HP floor upward.
        /// </summary>
        public void RegenTick(float amount)
        {
            if (amount <= 0f || _isDead || _hp <= 0f) return;
            if (_hp >= MaxHp) return;
            // WO-676 G3 (wire-or-hide): Swift Recovery (shared.n7, healthRegen) — fold the
            // talent bonus into every regen tick. Both callers (SafeZoneRecovery town-footprint
            // tick + the Oathmend HP-over-time drip) are out-of-combat regen paths, matching the
            // node's "out of combat" note. Same registry read + hero-class resolution as
            // TakeDamage's IncomingDamageReduction; identity (×1) until the node is learned.
            float regenBonus = DeNelle.Village.Talents.HeroTalentModifiers.HealthRegenBonus(HeroClassOrDefault);
            if (regenBonus > 0f)
            {
                amount *= (1f + regenBonus);
                DeNelle.Core.Diagnostics.FlowTrace.Once("HeroTalents", "healthRegen",
                    $"Swift Recovery applied: +{regenBonus:P0} HP regen per tick (shared.n7).");
            }
            _hp = Mathf.Min(MaxHp, _hp + amount);
            OnHealthChanged?.Invoke(_hp, MaxHp);
            // WO-888 (registry 6b, "Aura_HealingInProgress <- RegenTick"): a calm RISING column
            // while the town footprint is topping the hero up. This method is called EVERY FRAME
            // while standing in the ring, so it must never START a loop per call - it stamps a
            // short keep-alive instead and HeroHpStateAura stops the loop on its own once the
            // stamp lapses. That makes "regen ended" a guaranteed stop with no second call site.
            _hpAura?.NotifyRegen();
            UpdateInjuredState();   // clears the injured vignette once regen climbs back above the cutoff
        }

        /// <summary>
        /// Restores the hero to FULL HP (the "heal up between fights at home base" beat —
        /// called when the hero returns to town after an arena battle, win, flee, OR death).
        /// Works from ANY HP including 0: a hero that DIED in the arena must come back to town
        /// at full HP, not 0 (which one-shot it on the next fight). Also clears the death latch
        /// so contact damage + control resume, mirroring Respawn's revive (without moving the
        /// hero — the arena owns the town warp). Fires the HP-changed event the HUD/bar listen
        /// to + the heal VFX so the top-off reads on screen.
        /// </summary>
        public void RestoreToFull()
        {
            bool wasDown = _isDead || _hp <= 0f;
            _appliedEffectiveHpBonus = EffectiveBonus;   // re-sync so SyncGearHp doesn't double-apply after a full restore
            _hp = MaxHp;
            ResetTalentRunState();   // WO-566: town return = a fresh run — re-arm revive / clear Last Stand
            // If the hero had gone down, clear the death state so it isn't stuck "dead" on the
            // town return (Respawn does this on its own path; we mirror it here without warping).
            if (wasDown)
            {
                _isDead = false;
                _cooldown = 0f;
                ClearDeathAnim();
                ExitDeathFreeze();   // re-enable the agent + attack input frozen on death
                if (_locomotion != null) _locomotion.enabled = true;
                if (_abilities  != null) _abilities.enabled  = true;
            }
            OnHealthChanged?.Invoke(_hp, MaxHp);
            // WO-888: a restore to FULL leaves fraction == 1, so Drive resolves to Slot.None and
            // whatever wounded aura was being held is stopped on this same call. No stamp here -
            // a completed top-off has nothing left to show as "in progress".
            UpdateInjuredState();   // T-HP fix (owner 2026-06-27): a town-return restore to full must CLEAR the limp/injured stance carried out of the fight (was lingering -> "health full but still limping")
            VFXManager.Play(VFXType.Impact_Heal, transform.position + Vector3.up * 1.0f);
        }

        // ── WO-493 #5 / WO-497: HERO injured stance ───────────────────────────
        // Single source of truth for "wounded": HP fraction vs the cutoff. On a
        // threshold CROSS it drives the animator's Injured swap, toggles the red
        // edge vignette, and sets the move-speed multiplier; while injured it pulses
        // the optional heartbeat cue. All flag-gated (FeatureFlags.HeroInjuredStance);
        // when the flag is off the hero is forced healthy (no swap, full speed, dark
        // vignette) so the feature can be disabled cleanly without a rebuild.
        //
        // ── WO-888 (ACCESSIBILITY) ────────────────────────────────────────────
        // The PRIMARY low-HP tell is now the world-space aura driven below, which reads by
        // PULSE RATE + GUTTERING SHAPE and therefore survives greyscale. The red vignette is
        // DEMOTED to a secondary, redundant cue - it is still useful to players who can see
        // red, and redundancy is good accessibility; colour-ONLY was the bug (owner is
        // red/green colourblind, registry section 8 item 7).
        //
        // The aura is driven OUTSIDE the HeroInjuredStance flag on purpose: that flag exists to
        // switch off the injured stance + vignette, and WO-888's acceptance criterion is that
        // low HP stays legible with the vignette disabled. A survival read must not sit behind
        // the switch that turns off the thing it replaced.
        private void UpdateInjuredState()
        {
            // Primary read first, and unconditionally: HP fraction in, one aura out.
            // Null-safe - a hero without the component simply keeps the secondary cues.
            _hpAura?.Drive(_hp > 0f && !_isDead, Fraction);

            bool flagOn = DeNelle.Core.FeatureFlags.HeroInjuredStance;
            // Injured only while alive + below the cutoff + the flag is on. A dead hero
            // is "not injured" — the Death anim/respawn owns that beat, not the limp.
            bool injured = flagOn && _hp > 0f && Fraction < InjuredFraction;

            if (injured != _injured)
            {
                _injured = injured;
                // OWNER DIRECTIVE 2026-07-04: the injured LOCOMOTION/stance animation looked wrong and
                // is RETIRED for the hero — the wounded state is signalled by the red screen-edge
                // vignette instead (HeroInjuredVignette). We explicitly force the hero animator OUT of
                // the Injured swap (SetInjured(false)) rather than driving it in, so the hero always
                // keeps its normal locomotion. The Injured param/state stays intact in the controller
                // for enemies (Enemy.DriveAnimator) / future use — we simply never drive the HERO into it.
                _actor?.SetInjured(false);
                _vignette?.SetInjured(injured);   // WO-888: SECONDARY cue now (aura is primary)
                MoveSpeedMultiplier = injured ? InjuredMoveScale : 1f;
                _heartbeatCooldown = 0f;   // let the first beat land promptly on entry
                Debug.Log($"[HeroHealth] Injured feedback {(injured ? "ON" : "OFF")} " +
                          $"(hp={Mathf.CeilToInt(_hp)}/{Mathf.CeilToInt(MaxHp)}, frac={Fraction:F2}) - " +
                          $"primary=world HP aura (pulse rate + guttering shape), secondary=red edge vignette.");
            }

            // Optional heartbeat cue while wounded — paced ~1/sec, routed through the
            // audio service (null-safe). Generated once so it works with no audio asset.
            if (_injured)
            {
                // Attention-needed: deepen the red edge vignette as HP falls from the injured cutoff
                // toward zero (0 at the threshold, 1 at empty). Presentation-only — reads HP, never mutates.
                _vignette?.SetSeverity(Mathf.InverseLerp(InjuredFraction, 0f, Fraction));
                _heartbeatCooldown -= Time.deltaTime;
                if (_heartbeatCooldown <= 0f)
                {
                    _heartbeatCooldown = 1.0f;
                    if (s_heartbeatClip == null) s_heartbeatClip = GenerateHeartbeat();
                    DeNelle.Core.CoreServices.Audio?.PlaySfx(s_heartbeatClip, 0.35f);
                }
            }
        }

        /// <summary>
        /// Builds a short two-thump "lub-dub" heartbeat clip procedurally so the cue
        /// works with no authored audio asset (mirrors GameSfx's generated SFX). One
        /// low sine burst, a gap, then a softer second burst.
        /// </summary>
        private static AudioClip GenerateHeartbeat()
        {
            const int rate = 44100;
            const float dur = 0.55f;
            int n = Mathf.CeilToInt(rate * dur);
            var data = new float[n];
            // Two thumps: lub at ~0.00s (loud), dub at ~0.18s (softer).
            AddThump(data, rate, 0.00f, 0.10f, 55f, 0.9f);
            AddThump(data, rate, 0.18f, 0.10f, 48f, 0.6f);
            var clip = AudioClip.Create("HeroHeartbeat", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void AddThump(float[] data, int rate, float start, float len,
                                     float freq, float gain)
        {
            int s = Mathf.Clamp(Mathf.RoundToInt(start * rate), 0, data.Length - 1);
            int e = Mathf.Min(data.Length, s + Mathf.RoundToInt(len * rate));
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)rate;
                float env = Mathf.Exp(-t * 24f);   // fast percussive decay
                data[i] += Mathf.Sin(2f * Mathf.PI * freq * t) * env * gain;
            }
        }

        // ── IDamageableStructure ─────────────────────────────────────────────
        bool IDamageableStructure.IsAlive => IsAlive;
        // WO-1773: the SECOND entry path into TakeDamage, and the one the ticket flagged as
        // unbounded — it is NOT paced by the 1 s contact tick and NOT capped by MaxEnemiesPerTick,
        // so every ranged attacker and every structure-damage source reaches the hero through here.
        // It carries no attacker reference (the interface passes an amount and nothing else), so the
        // damage-taken trace is told so explicitly rather than reporting a stale melee attacker from
        // some earlier tick, which would be a plausible-looking lie in a log.
        void IDamageableStructure.ApplyContactDamage(float amount)
        {
            _lastAttackerName = "(ranged/structure source, no attacker reference)";
            TakeDamage(amount);
        }

        /// <summary>
        /// WO-1439 — the hero is the player. Constant Friendly, so a Hostile enemy's contact
        /// probe still finds and hits the hero exactly as before (the hero is the ONE
        /// IDamageableStructure the enemy is supposed to swing at in a raid).
        /// </summary>
        CombatFaction IDamageableStructure.Faction => CombatFaction.Friendly;

        // ── IMGUI health bar (no UIDocument dependency) ───────────────────────
        private static Texture2D Px => Texture2D.whiteTexture;

        // Reference resolution the bar is laid out against. IMGUI draws in raw
        // pixels with no auto-scaling, so on a high-DPI / high-resolution player
        // build the same fixed Rect lands at the wrong size & place — which is how
        // this bar ended up "HUGE and floating mid-screen". We scale the whole pass
        // by GUI.matrix against this reference so the bar is a CONSISTENT compact
        // size and stays anchored top-left, just under the Heart bar, on any screen.
        private const float RefWidth  = 1920f;
        private const float RefHeight = 1080f;

        private void OnGUI()
        {
            // WO-411 #2 (duplicate hero bar): this legacy IMGUI bar is suppressed whenever the
            // real uGUI village HUD is present (VillageHudController registers CoreServices.Hud and
            // now owns hero vitals). It remains only as a FALLBACK for HUD-less scenes.
            if (DeNelle.Core.CoreServices.Hud != null) return;

            // Compact bar, anchored top-left under the top-left Heart HP bar.
            const float w = 200f, h = 18f, x = 24f, y = 92f;

            // Uniform reference-resolution scale (letterbox-safe: use the smaller
            // axis ratio so the bar never balloons on ultrawide / tall screens).
            float scale = Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);
            if (scale <= 0f) scale = 1f;
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));

            // Backdrop + empty track.
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), Px);
            GUI.color = new Color(0.16f, 0.16f, 0.20f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, w, h), Px);

            // Fill — red → green by fraction.
            float frac = Fraction;
            GUI.color = Color.Lerp(new Color(0.85f, 0.18f, 0.18f),
                                   new Color(0.30f, 0.85f, 0.40f), frac);
            GUI.DrawTexture(new Rect(x, y, w * frac, h), Px);

            // Label.
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            GUI.Label(new Rect(x, y, w, h),
                      $"Hero   {Mathf.CeilToInt(_hp)} / {Mathf.CeilToInt(MaxHp)}", style);

            GUI.color = Color.white;
            GUI.matrix = prevMatrix;
        }
    }

    /// <summary>
    /// Persistent bootstrap that attaches <see cref="HeroHealth"/> to the hero
    /// (the HeroAbilities GameObject) whenever a scene containing a hero is loaded.
    /// Polls briefly because the hero may spawn a frame or two after scene load.
    /// </summary>
    internal sealed class HeroHealthBootstrap : MonoBehaviour
    {
        private float _retry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("HeroHealthBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<HeroHealthBootstrap>();
        }

        private void Update()
        {
            if (HeroHealth.Instance != null) return;   // already attached
            _retry -= Time.deltaTime;
            if (_retry > 0f) return;
            _retry = 0.5f;

            var hero = FindAnyObjectByType<HeroAbilities>();
            if (hero != null && hero.GetComponent<HeroHealth>() == null)
            {
                var health = hero.gameObject.AddComponent<HeroHealth>();
                // Combat feel: screen flash on damage + death slow-mo (additive).
                if (hero.GetComponent<HeroHitReaction>() == null)
                    hero.gameObject.AddComponent<HeroHitReaction>();

                // NOTE: the hero deliberately gets NO over-the-head FloatingHealthBar.
                // The hero's HP is already shown in the HUD (VillageHudController hero
                // HP bar) plus the top-left IMGUI readout (HeroHealth.OnGUI), so a
                // floating world-space bar over the player just rendered as a green
                // pill edge-on from the over-shoulder camera. FloatingHealthBar is for
                // enemies/units only (Enemy.EnsureHealthBar) — the hero is excluded
                // here on purpose. Do not re-add an Attach() call for the hero.
            }
        }
    }
}
