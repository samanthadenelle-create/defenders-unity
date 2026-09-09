// =============================================================================
// TroopController — one deployed friendly troop (WO-453 Step 1, combat-only).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// A lightweight friendly fighter built from a TroopDef (troops.json). It COPIES
// the proven Hostile-hunt loop from DeNelle.Pets.Pet — an OverlapSphere scan for
// the nearest CombatFaction.Hostile IDamageable, a NavMeshAgent driven by Move()
// (updateRotation off; facing manual), and a guarded Animator — and it is itself
// damageable through IDamageableStructure, exactly like StoryCompanion, so the
// enemy contact-attack lane (Enemy.ProbeForStructure / EnemyBrain.TryAttack) can
// chip it down via GetComponentInParent<IDamageableStructure>().
//
// WHY NOT EnemyBrain: that brain is hero/Heart-hardcoded with no faction param —
// it can only HUNT the hero. Troops need the opposite (hunt Hostile foes), so we
// reuse the Pet hunt loop (which already filters to Hostile) rather than flip the
// enemy AI. Troops are CombatFaction.Friendly conceptually; they only READ foes.
//
// Footman (melee) and Archer (ranged-by-reach) share this class — only the def
// stats differ (the Archer's attackRange 14 makes it a standoff fighter; the
// travelling-projectile visual is deferred). Troops are EXPENDABLE: at 0 HP the
// body plays its death anim and is destroyed (no pool / respawn).
//
// Step-1 scope is combat only — there is NO rally / deploy-point / retreat verb
// here (that is Step 4). With no foe in range the troop simply idles in place.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;   // FlowTrace - [Flow:TroopVisual]
using DeNelle.BattleATB.Engine;   // StatusKind (unlocked special-ability vocabulary)
using DeNelle.Village.World.Camps; // RaidSpire — WO-1595 push objective
using UnityEngine;
using UnityEngine.AI;

namespace DeNelle.Village
{
    /// <summary>
    /// A deployed friendly troop. Hunts the nearest hostile <see cref="IDamageable"/>
    /// within its scan radius and attacks on a cooldown; itself takes contact damage
    /// through <see cref="IDamageableStructure"/>. Configured from a <see cref="TroopDef"/>
    /// (troops.json) via <see cref="Configure"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TroopController : MonoBehaviour, IDamageableStructure
    {
        private static readonly List<TroopController> Active = new List<TroopController>();

        /// <summary>Allocation-free live roster used by raid towers and squad support AI.</summary>
        public static IReadOnlyList<TroopController> ActiveTroops => Active;
        [Header("Identity (from troops.json)")]
        [Tooltip("Stable troop id — e.g. troop-footman. Set by Configure().")]
        [SerializeField] private string _troopId;

        [Tooltip("The persisted PlayerTroop.Id this body was deployed from (WO-453 Step 4). " +
                 "Used by the retreat reconcile to tell survivors from the wounded. Empty for a " +
                 "dev/loose spawn that wasn't drawn from the army.")]
        [SerializeField] private string _ownedTroopId;

        [Header("Combat")]
        [Tooltip("Layers swept for IDamageable hostile targets. Set to the village Enemy layer; " +
                 "Awake and SetEnemyMask add the Structure layer on top (WO-853) so raid walls " +
                 "and gates are acquirable.")]
        [SerializeField] private LayerMask _enemyMask = ~0;

        [Header("Live state")]
        [Tooltip("Current HP. Set from the def's MaxHp by Configure().")]
        [SerializeField] private float _hp;

        // --- runtime, from the TroopDef (troops.json) ---
        private TroopDef _def;
        private float _maxHp = 100f;
        private float _attackDamage = 12f;
        private float _attackRange = 2.5f;
        private float _attackCooldown = 1.0f;
        private float _moveSpeed = 4.0f;
        private float _huntScanRadius = 14f;
        private DamageElement _element = DamageElement.None;
        // WO-933 siege: prefer Hostile structures; bias damage structure vs unit.
        private bool _preferStructures;
        private float _structureDamageMult = 1f;
        private float _unitDamageMult = 1f;
        private bool _isSupport;
        // WO-1595 assault job / phase (formation + breach→spire + peel).
        private RaidAssaultJob _assaultJob = RaidAssaultJob.Front;
        private RaidAssaultPhase _assaultPhase = RaidAssaultPhase.Breach;
        private float _lastHurtAt = -999f;
        private bool _routeToObjectiveOpen;
        private string _objectiveRouteStatus = "not-asked";
        private float _objectiveRouteCheckAt;

        // WO-771.9 spawn-wiring: the EFFECTIVE baseline the veterancy/perk multipliers re-base
        // from. Set to the def stats in Configure; overwritten by ApplyUpgradeStats when the
        // troop is spawned at an upgrade level, so an upgraded troop's reach/strength survive a
        // subsequent ApplyDamageMultiplier/ApplyHealthMultiplier (which re-base, never compound).
        private float _baseMaxHp = 100f;
        private float _baseAttackDamage = 12f;

        // WO-771.9: the upgrade level this troop was resolved at (1 = pure baseline) + the
        // special abilities unlocked at that level (their StatusKind is the real-unit effect
        // vocabulary; per-tick status application is the V2 sim's job — here they are attached
        // as data the combat layer reads).
        private int _upgradeLevel = 1;
        private readonly List<AbilityUnlock> _unlockedAbilities = new List<AbilityUnlock>();

        private float _attackCdRemaining;

        // ── Target-hunt throttle (mirrors Pet's _huntTimer idiom) ────────────
        // Re-run the OverlapSphere scan only on an interval; reuse the cached foe
        // between ticks. The cheap per-frame work (move + attack timing) still
        // runs every frame, so combat feel is unchanged — only the SCAN cadence
        // drops. Explicit null/alive checks (never ?? on a UnityObject/IDamageable).
        private const float HuntScanInterval = 0.2f;
        private float _huntTimer;
        private IDamageable _cachedFoe;

        // ── WO-1438 [Flow:TroopAI] — the deployed warband's target selection was the
        // ONE invisible actor in a raid. The defenders firehose 13 800 [Flow:EnemyAggro]
        // lines per raid; the player's own troops emitted nothing about WHAT they chose or
        // WHY, so "the AI didn't really fight" and "they keep chewing adjoining walls" could
        // not be told apart from a log. These fields carry the per-troop trace state.
        // PERMANENT instrumentation (CLAUDE.md §12) — flag it off, never strip it.
        private string _troopRole = "?";           // melee / ranged / siege / support / tank
        // why the last scan ran: timer / foe-died / foe-null / foe-destroyed (WO-1569: the foe's
        // GameObject was Destroy()d, which `!= null` on an interface reference cannot see)
        private string _retargetReason = "spawn";
        private int _retargetCount;                // how many times this troop has switched foe
        private Vector3 _aiLastPos;                // for the measured moved/sec in the heartbeat
        private float _aiLastPosTime;
        // Filled by NearestHostile so the retarget line can report the runner-up of the OTHER
        // kind — the falsifiable field (§1.4b): it embarrasses the selector when a 3 m wall
        // beats an 11 m live defender.
        // NOTE these hold REFERENCES, not formatted strings. NearestHostile runs 5x/second per
        // troop; DescribeTarget interpolates, so formatting here would allocate on every scan
        // even when nothing is logged (§1.3). The strings are built at EMIT time only.
        private IDamageable _lastRunnerUp;
        private float _lastRunnerUpDist = -1f;
        private int _lastAcceptedUnits, _lastAcceptedStructs, _lastRejected, _lastOverlapCount;
        // Nearest hostile of ANY kind seen by the last scan, even when it lost — so the
        // idle/rally line can say "there WAS a foe at 21 m, my radius is 14 m".
        private IDamageable _lastNearestAny;
        private float _lastNearestAnyDist = -1f;
        // Reused by TraceBreachProbe so the once-per-structure-kill path query allocates once.
        private NavMeshPath _breachPath;
        // WO-1569 - the cached foe's world position, recorded WHILE IT WAS STILL LIVE.
        // A DefenseTower is Destroy(gameObject)d on death (Destructible.NotifyBroken), so by the
        // time the breach probe wants to sample "where the structure stood" the native object is
        // gone and WorldPosition throws. Skipping the sample would be safe but would discard the
        // one measurement WO-1438 is waiting on, and would discard it for exactly the structure
        // kind that gets destroyed. Carrying the last live value forward keeps the probe honest
        // AND crash-free. The `Valid` flag exists so a never-recorded position is reported as
        // unknown rather than silently sampled at the origin.
        private Vector3 _lastFoePos;
        private bool _lastFoePosValid;

        // ── WO-1438 route gate (the FIX, not the trace) ──────────────────────
        // How often a troop may ask the NavMesh whether the defender it can SEE is a defender it
        // can REACH. NearestHostile runs 5x/s per troop; this is the only non-free query in it.
        private const float RouteCheckInterval = 0.5f;
        // A PathComplete longer than this multiple of the straight line is a detour round the
        // wall ring, not a route through the breach — and a detour is precisely the case the
        // gate exists to refuse, because steering is straight-line Move(displacement): a route
        // that has to go round is a route this troop cannot walk. Calibrated on the 09-06
        // capture, whose worst real route measured 8.1/7.1 = 1.14x (the rest within 1.01x), so
        // 1.5 leaves headroom over every measured route and still rejects a lap of the ring.
        private const float RouteDetourFactor = 1.5f;
        private NavMeshPath _routePath;
        private float _routeCheckAt;
        private bool _routeToUnitOpen;
        private string _routeStatus = "not-asked";
        private bool _lastPreferUnit;
        // Sample radii for the repaired BREACH probe. Tight where the wall stood (roughly the
        // agent radius — a hit at 1.5 m is not "the breach is walkable"), loose at the target,
        // whose WorldPosition is a body/structure centre rather than a foot position.
        private const float BreachSampleRadius = 0.6f;
        private const float ReplacementSampleRadius = 2.5f;

        // Reusable overlap buffer — avoids per-frame GC (OverlapSphereNonAlloc).
        // WO-853 raised this from 48: the hunt mask now includes the Structure layer, so a
        // sweep inside a raid base returns every wall panel in the 14 m scan radius as well as
        // the enemy bodies. OverlapSphereNonAlloc truncates at the buffer length and its result
        // order is arbitrary, so a 48-slot buffer filled with wall panels would crowd the enemy
        // colliders out and stop the troop finding a foe at all.
        private readonly Collider[] _overlap = new Collider[128];

        // ── Navigation (mirrors Pet: drive a NavMeshAgent via Move()) ─────────
        // The agent constrains the troop to the SAME baked NavMesh the hero +
        // enemies use (it can't enter walls/buildings) and grounds it on the
        // walkable surface; we feed it our own step and keep rotation manual.
        private NavMeshAgent _agent;

        // Eased locomotion (mirrors Pet.MoveToward) so the troop accelerates out
        // of rest, coasts, then damps as it arrives — no constant-velocity dash.
        private float _currentSpeed;
        private const float Acceleration = 9f;
        private const float ArrivalDamp  = 1.6f;

        // Rally arrival epsilon (WO-453 Step 4): a troop within this flat distance of the
        // global rally point is "arrived" and idles instead of jittering on the spot.
        private const float RallyArrivalEpsilon = 1.25f;

        // ── Animation (guarded — a troop with no rig still fights) ────────────
        private Animator _animator;
        private Vector3 _lastPosition;
        private static readonly int AnimSpeed    = Animator.StringToHash(AnimParams.Speed);
        private static readonly int AnimAttack   = Animator.StringToHash(AnimParams.Attack);
        private static readonly int AnimCast     = Animator.StringToHash(AnimParams.Cast);
        private static readonly int AnimInCombat = Animator.StringToHash(AnimParams.InCombat);
        private static readonly int AnimHit      = Animator.StringToHash(AnimParams.Hit);
        private static readonly int AnimDead     = Animator.StringToHash(AnimParams.Dead);
        private bool _hasSpeed, _hasAttack, _hasCast, _hasInCombat, _hasHit, _hasDead;
        /// <summary>Mage/Cleric controllers: strike fires Cast; melee/ranged fire Attack.</summary>
        private bool _useCastStrike;
        /// <summary>
        /// WO-935 Phase 3 (archer row): Ranger controllers read as a BOW SHOT - a released
        /// arrow that flies to the target - instead of the melee connect arc. Mutually
        /// exclusive with <see cref="_useCastStrike"/> by construction (one resolver,
        /// TroopFactory.ResolveRoleController, returns exactly one of Mage / Ranger / Knight).
        /// </summary>
        private bool _useBowShot;
        /// <summary>
        /// Lazily attached bow-shot presentation (WO-935 Phase 3). RangedAttackVFX is the
        /// INCUMBENT projectile launcher and is reused verbatim - Enemy.EnsureCastVfx attaches
        /// the very same component the very same way for enemy ranged casts. It owns the
        /// pooled body, the release flash and the arrival impact, so this slice writes NO
        /// second projectile mover, which is exactly what the work order forbids.
        /// </summary>
        private RangedAttackVFX _bowVfx;

        // §12 instrumentation (owner defect 2026-08-02 "troops slide / T-pose"): the LAST step of the
        // chain — proof that a parameter was actually written to a live Animator. One line per troop,
        // then never again (no per-frame log spam). If [Flow:TroopVisual] shows the controller was
        // assigned but this line never appears, the dead step is HERE (the param cache), not the rig.
        private bool _tracedFirstParamWrite;

        // How long the corpse lingers after death before it's destroyed (lets the
        // Dead anim play). EXPENDABLE — no pool / respawn.
        private const float DeathHoldSeconds = 3f;
        private bool _dead;

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        /// <summary>Stable troop id — e.g. <c>troop-footman</c>.</summary>
        public string TroopId => _troopId;

        /// <summary>
        /// The persisted <c>PlayerTroop.Id</c> this body was deployed from (WO-453 Step 4),
        /// or empty for a loose/dev spawn. Stamped by the deployer so the retreat reconcile
        /// can map a living troop back to its army record (survivor vs wounded).
        /// </summary>
        public string OwnedTroopId
        {
            get => _ownedTroopId;
            set => _ownedTroopId = value;
        }

        /// <summary>Current HP.</summary>
        public float Hp => _hp;

        /// <summary>Max HP from the def.</summary>
        public float MaxHp => _maxHp;

        /// <summary>True while the troop is alive.</summary>
        public bool IsAlive => !_dead && _hp > 0f;

        /// <summary>The static def this troop was configured from (troops.json).</summary>
        public TroopDef Def => _def;

        /// <summary>WO-771.9 — the upgrade level this troop was spawned at (1 = pure baseline).</summary>
        public int UpgradeLevel => _upgradeLevel;

        /// <summary>WO-771.9 — the special abilities unlocked at this troop's upgrade level (may be empty).</summary>
        public IReadOnlyList<AbilityUnlock> UnlockedAbilities => _unlockedAbilities;

        /// <summary>WO-771.9 — the StatusKinds this troop's unlocked abilities apply (real-unit effect vocabulary).</summary>
        public IEnumerable<StatusKind> UnlockedStatuses
        {
            get { foreach (var a in _unlockedAbilities) if (a != null) yield return a.StatusKind; }
        }

        /// <summary>
        /// Sets the LayerMask the troop sweeps for hostile targets. The deployer /
        /// factory calls this so the mask need not be authored per-instance. The Structure
        /// layer is added on top of whatever the caller passes (see
        /// <see cref="WithStructureLayer"/>) — <c>TroopDeployer.VillageEnemyMask</c> hands over
        /// the Enemy layer alone, and a troop that cannot sweep Structure can never find a wall.
        /// </summary>
        public void SetEnemyMask(LayerMask enemyMask) => _enemyMask = WithStructureLayer(enemyMask);

        /// <summary>
        /// WO-853: returns <paramref name="mask"/> with the "Structure" layer added.
        /// Walls and gates STAY on Structure — that layer is the tower line-of-sight blocker
        /// mask, so relayering them onto Enemy would make towers shoot through walls — which
        /// means the only way a sweep can find one is to include Structure in the mask.
        /// <see cref="LayerMask.GetMask"/> returns 0 for an undeclared layer, so the OR is a
        /// no-op and any caller's ~0 fallback survives unchanged.
        /// Widening is safe because <see cref="NearestHostile"/> rejects every non-Hostile
        /// faction: the player's own perimeter reports Friendly and is skipped.
        /// </summary>
        private static LayerMask WithStructureLayer(LayerMask mask) =>
            mask.value | LayerMask.GetMask("Structure");

        /// <summary>
        /// Applies a veterancy DAMAGE multiplier to this troop's per-hit damage (WO-453
        /// Step 4). Multiplies the def's base AttackDamage (resolved in Configure) so a
        /// veteran troop (PlayerTroop.DamageMultiplier = 1 + 0.05*rank) hits harder. Call
        /// AFTER Configure (the deployer does). Values &lt; 1 are clamped to 1 (a multiplier
        /// only ever buffs — it never weakens a fresh troop). Idempotent re-base: re-reads
        /// the def's base each call so repeated calls don't compound.
        /// </summary>
        public void ApplyDamageMultiplier(float multiplier)
        {
            // Re-base from the EFFECTIVE baseline (def, or the upgraded value ApplyUpgradeStats set)
            // so veterancy/perk multipliers compound on top of a WO-771.9 upgrade instead of wiping
            // it. With no upgrade applied, _baseAttackDamage == def.AttackDamage → identical to before.
            _attackDamage = _baseAttackDamage * Mathf.Max(1f, multiplier);
        }

        /// <summary>
        /// Applies a HEALTH multiplier to this troop's max HP (WO-430 city upgrades — the
        /// Armorer's troopHealthMult). BAKED AT SPAWN: re-reads the def's base MaxHp (so
        /// repeated calls don't compound) and sets HP to the new (buffed) max — a troop is
        /// born at full strength. Call AFTER Configure (the deployer does). Values &lt; 1 are
        /// clamped to 1 (a tier perk only buffs). Max HP is set ONCE at spawn, never
        /// live-scaled mid-fight (that would create current-HP exploit/death — owner-approved).
        /// </summary>
        public void ApplyHealthMultiplier(float multiplier)
        {
            // Re-base from the EFFECTIVE baseline (see ApplyDamageMultiplier) so a WO-771.9 upgrade
            // survives the perk multiply. With no upgrade applied, _baseMaxHp == def.MaxHp.
            _maxHp = _baseMaxHp * Mathf.Max(1f, multiplier);
            _hp = _maxHp;
        }

        /// <summary>
        /// WO-771.9 SPAWN-WIRING — applies a resolved <see cref="TroopRuntimeStats"/> (baseline
        /// folded with the troop's upgrade curves at its level, from
        /// <see cref="TroopStatResolver.Effective"/>) to this live unit ONCE at spawn: sets the
        /// effective HP / DPS(attack damage) / reach(attackRange) / aggro(huntScanRadius) as the
        /// new re-base baseline and refills HP, and records the unlocked special abilities
        /// (their StatusKind is applied to the real unit as effect data). Call AFTER
        /// <see cref="Configure"/> and BEFORE the veterancy/perk multipliers so those compound on
        /// the upgraded base. Null stats → no-op (pure baseline stays).
        /// </summary>
        public void ApplyUpgradeStats(TroopRuntimeStats stats)
        {
            if (stats == null) return;

            _upgradeLevel   = stats.Level < 1 ? 1 : stats.Level;
            _maxHp          = stats.MaxHp;
            _attackDamage   = stats.AttackDamage;
            _attackRange    = stats.AttackRange;
            _huntScanRadius = stats.AggroRadius;

            // The upgraded values become the new baseline the perk multipliers re-base from.
            _baseMaxHp        = stats.MaxHp;
            _baseAttackDamage = stats.AttackDamage;

            _hp = _maxHp;

            _unlockedAbilities.Clear();
            if (stats.UnlockedAbilities != null)
                foreach (var a in stats.UnlockedAbilities)
                    if (a != null) _unlockedAbilities.Add(a);
        }

        // ── IDamageableStructure (lets the enemy contact-attack lane hurt us) ──
        // Enemy.ProbeForStructure / EnemyBrain.TryAttack resolve their target via
        // GetComponentInParent<IDamageableStructure>(); implementing it here is the
        // "damageable wrapper" (same as StoryCompanion). The non-trigger collider the
        // factory attaches on a probe-visible layer is what lets that probe find us.
        bool IDamageableStructure.IsAlive => IsAlive;

        void IDamageableStructure.ApplyContactDamage(float amount) => TakeDamage(amount);

        /// <summary>
        /// WO-1439 — a deployed troop is the PLAYER's warband, always. Constant Friendly.
        /// This is the same side the file header already states ("Troops are
        /// CombatFaction.Friendly conceptually; they only READ foes") — now DECLARED rather
        /// than left as a comment, which is what lets a Hostile garrison tell the player's
        /// troops apart from its own structures at the one shared predicate.
        /// </summary>
        CombatFaction IDamageableStructure.Faction => SelfFaction;

        /// <summary>
        /// WO-1438 — this troop's own side, declared ONCE. Every faction question in this file
        /// (the explicit interface member above, the selection filter in
        /// <see cref="NearestHostile"/>, the struct/unit classifier) reads THIS and hands it to
        /// <see cref="CombatFactionRules"/>. Before today two of those sites compared against a
        /// hardcoded <c>CombatFaction.Hostile</c> inline — the copied-predicate shape
        /// CombatFactionRules' own header forbids.
        /// </summary>
        private const CombatFaction SelfFaction = CombatFaction.Friendly;

        /// <summary>
        /// Wires this troop from a <see cref="TroopDef"/> + spawn position. Called by
        /// the factory right after instantiation. HP, damage, speed and reach are read
        /// off the def.
        /// </summary>
        /// <param name="def">The troop def from <see cref="TroopCatalog"/>.</param>
        /// <param name="spawnPos">World position to seat the troop at.</param>
        public void Configure(TroopDef def, Vector3 spawnPos)
        {
            _def = def;
            if (def != null)
            {
                _troopId        = def.Id;
                _maxHp          = def.MaxHp;
                _attackDamage   = def.AttackDamage;
                _attackRange    = def.AttackRange;
                _attackCooldown = def.AttackCooldown;
                _moveSpeed      = def.MoveSpeed;
                _huntScanRadius = def.HuntScanRadius;
                _element        = ParseElement(def.Element);
                // WO-933: role "siege" → structure-prefer hunt (WC Demolisher / CoC wall-breaker).
                _preferStructures = string.Equals(def.Role, "siege", System.StringComparison.OrdinalIgnoreCase);
                // WO-1595: map role → formation job (Front / Ranged / Breaker / Support).
                _assaultJob = RaidAssaultAi.JobFromRole(def.Role);
                _troopRole = string.IsNullOrEmpty(def.Role) ? "?" : def.Role;
                _isSupport = string.Equals(def.Role, "support", System.StringComparison.OrdinalIgnoreCase);
                _structureDamageMult = def.StructureDamageMult > 0f ? def.StructureDamageMult : 1f;
                _unitDamageMult = def.UnitDamageMult > 0f ? def.UnitDamageMult : 1f;
                // Melee -> Knight Attack; archer -> Ranger Attack; mage -> Mage Cast.
                _useCastStrike = TroopFactory.UsesCastStrike(def, def.Model);
                // WO-935 Phase 3 (archer row): the same resolver decides the STRIKE READ.
                _useBowShot = TroopFactory.UsesBowShot(def, def.Model);
            }

            // WO-771.9: seed the re-base baseline from the def; ApplyUpgradeStats overwrites it
            // when the troop spawns at an upgrade level (so it is never null-reffed downstream).
            _baseMaxHp        = _maxHp;
            _baseAttackDamage = _attackDamage;
            _upgradeLevel     = 1;
            _unlockedAbilities.Clear();

            _hp = _maxHp;
            _dead = false;

            // Snap to the spawn slot. Use Warp() when the agent is live so its internal
            // position stays in sync (a raw transform set would desync it → it'd snap
            // back / refuse to Move). Warp also lands on the nearest walkable point.
            if (_agent != null && _agent.isOnNavMesh)
                _agent.Warp(spawnPos);
            else
                transform.position = spawnPos;

            _aiLastPos = transform.position;
            _aiLastPosTime = Time.time;

            // WO-1438: state this troop's SELECTOR CONTRACT once, at spawn. Every later
            // [Flow:TroopAI] line is read against these numbers — a troop that never fights
            // is usually a huntRadius that never reaches, and a troop that chews masonry is
            // usually preferStruct=False, which puts walls in the same nearest-wins bucket as
            // live defenders. Both are visible here before a single tick runs.
            //
            // Steering context for whoever reads the log (kept OUT of the line itself so the
            // line stays measurement-only): MoveToward drives _agent.Move(displacement) — a
            // straight-line push. There is no SetDestination and no path query, so nothing here
            // has a route concept that a breach could change. If that ever gains a path, the
            // agent= field below is what will show it.
            FlowTrace.Step("TroopAI",
                $"id={_troopId} role={_troopRole}: SELECTOR huntRadius={_huntScanRadius:F1}m " +
                $"attackRange={_attackRange:F1}m moveSpeed={_moveSpeed:F1} preferStruct={_preferStructures} " +
                $"support={_isSupport} mask={_enemyMask.value} " +
                $"agent={(_agent != null ? (_agent.isOnNavMesh ? "onNavMesh" : "OFF-NAVMESH") : "none")} " +
                $"steering=Move(displacement) hasDestination={(_agent != null && _agent.hasPath)}");
        }

        private void Awake()
        {
            // The Animator sits on the skinned mesh child the factory seats.
            //
            // ORDER IS LOAD-BEARING (owner defect 2026-08-02): this Awake runs SYNCHRONOUSLY inside
            // TroopFactory's AddComponent<TroopController>(), so whatever controller is bound at this
            // instant decides — for the whole life of the troop — which params are ever written.
            // TroopFactory.ApplyTroopAnimator therefore binds BEFORE that AddComponent. If that ever
            // gets reordered, every flag below stays false and the troop silently slides again, which
            // is exactly what the trace lines here exist to catch.
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == AnimSpeed)    _hasSpeed    = true;
                    if (p.nameHash == AnimAttack)   _hasAttack   = true;
                    if (p.nameHash == AnimCast)     _hasCast     = true;
                    if (p.nameHash == AnimInCombat) _hasInCombat = true;
                    if (p.nameHash == AnimHit)      _hasHit      = true;
                    if (p.nameHash == AnimDead)     _hasDead     = true;
                }
            }

            // §12: split "no animator" vs "no controller" vs "controller speaks a different
            // vocabulary" — the three distinct ways this troop ends up frozen. One line per spawn.
            if (_animator == null)
            {
                FlowTrace.Fail("TroopVisual",
                    $"id={_troopId}: NO Animator anywhere under the troop root - the body cannot animate at all " +
                    "(model missing -> tinted-capsule fallback, or a rig-less prop was skinned).");
            }
            else if (_animator.runtimeAnimatorController == null)
            {
                FlowTrace.Fail("TroopVisual",
                    $"id={_troopId}: Animator on '{_animator.gameObject.name}' has NO runtimeAnimatorController at " +
                    "Awake - every parameter write is skipped for this troop's whole life; it will slide/T-pose. " +
                    "TroopFactory.ApplyTroopAnimator must bind BEFORE AddComponent<TroopController>().");
            }
            else if (!_hasSpeed)
            {
                FlowTrace.Fail("TroopVisual",
                    $"id={_troopId}: controller '{_animator.runtimeAnimatorController.name}' declares NO '" +
                    AnimParams.Speed + "' parameter (params=" + DescribeParams(_animator) + ") - this troop " +
                    "will slide/T-pose. A vendor-pack controller (e.g. Supercyan StrafeMovement) speaks a " +
                    "different vocabulary; bind a controller built to AnimParams instead.");
            }
            else
            {
                FlowTrace.Step("TroopVisual",
                    $"id={_troopId}: driver armed on controller '{_animator.runtimeAnimatorController.name}' " +
                    $"- Speed={_hasSpeed} Attack={_hasAttack} Cast={_hasCast} InCombat={_hasInCombat} " +
                    $"Hit={_hasHit} Dead={_hasDead} useCastStrike={_useCastStrike}.");
            }

            _lastPosition = transform.position;

            // Mirror Pet's NavMeshAgent setup: drive it via Move() from our own eased
            // kinematics, manual facing (FaceToward). hero-ish radius/height so it paths
            // the shared single-agent navmesh like every other body.
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.agentTypeID = 0;            // share the hero's agent type / NavMeshLinks
            _agent.radius = 0.4f;
            _agent.height = 1.8f;
            _agent.baseOffset = 0f;
            _agent.speed = 30f;                // we drive via Move(); keep high so it never caps us
            _agent.acceleration = 200f;
            _agent.angularSpeed = 0f;
            _agent.updateRotation = false;     // facing handled manually (FaceToward)
            _agent.updateUpAxis = false;
            _agent.autoBraking = false;
            _agent.stoppingDistance = 0f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

            // WO-853: add the Structure layer to whatever mask was authored on this instance,
            // so a scene-placed / dev-spawned troop that never receives SetEnemyMask still
            // sweeps walls and gates. A no-op when the mask is already ~0 or Structure is
            // undeclared. SetEnemyMask applies the same widening to the deployer's mask.
            _enemyMask = WithStructureLayer(_enemyMask);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _attackCdRemaining = Mathf.Max(0f, _attackCdRemaining - dt);

            // Feed the Animator's Speed float from the actual per-frame displacement
            // (the agent is Move()-driven, no velocity to read). Guarded; no-op rig-less.
            if (_animator != null && _hasSpeed && dt > 0f)
            {
                float moved = (transform.position - _lastPosition).magnitude / dt;
                _animator.SetFloat(AnimSpeed, moved);
                if (!_tracedFirstParamWrite)
                {
                    // FIRST PARAM WRITTEN — the last step of the chain, proven once per troop.
                    _tracedFirstParamWrite = true;
                    FlowTrace.Step("TroopVisual",
                        $"id={_troopId}: FIRST param write - SetFloat('{AnimParams.Speed}', {moved:F2}) on " +
                        $"'{_animator.runtimeAnimatorController.name}'. The animation chain is live end to end.");
                }
            }
            _lastPosition = transform.position;

            if (!IsAlive) return;

            // Support troops form the squad's sustain layer. They heal the most-injured
            // nearby ally and follow it into range; when nobody needs healing they fall
            // through to the normal hostile hunt so they never stand inert.
            if (_isSupport && TryHealSquadmate(dt)) return;

            // THROTTLE the target-hunt scan (mirrors Pet). Drop a cached foe that died /
            // was destroyed so we never aim at a corpse. Move + attack still run per-frame.
            _huntTimer -= dt;
            // WO-1569: IsLiveTarget, not `!= null`. IsAlive is field-backed on every structure
            // (DefenseTower.cs:224 reads Hp + _broken) so a BROKEN foe already re-selected here;
            // what did not was a foe Destroy()d without ever going through the damage path -
            // scene teardown, build-mode removal - where `_cachedFoe != null` stayed true and the
            // engaged branch below read foe.WorldPosition OUTSIDE any Guard.
            bool foeValid = IsLiveTarget(_cachedFoe) && _cachedFoe.IsAlive;
            if (_huntTimer <= 0f || !foeValid)
            {
                // WO-1438: name WHY we rescanned before we rescan. "foe-died" is the
                // load-bearing one — it is the tick right after a wall segment collapses,
                // and the retarget line that follows says what replaced it.
                _retargetReason = _cachedFoe == null ? "foe-null"
                                : !IsLiveTarget(_cachedFoe) ? "foe-destroyed"
                                : !_cachedFoe.IsAlive ? "foe-died"
                                : "timer";
                var previousFoe = _cachedFoe;
                // WO-1569: the position we recorded while that foe was still LIVE. The probe
                // below must never re-read WorldPosition off it (that is the crash), and a
                // skipped read would silently discard the hole sample for exactly the structure
                // kind - a tower - that has never been measured. So we carry the value forward.
                Vector3 previousFoePos = _lastFoePos;
                bool previousFoePosValid = _lastFoePosValid;
                _lastFoePosValid = false;

                _huntTimer = HuntScanInterval;
                _cachedFoe = NearestHostile();
                foeValid = IsLiveTarget(_cachedFoe) && _cachedFoe.IsAlive;

                // Fire ONLY on an actual change of foe — not every 0.2 s scan. This is the
                // ticket's central line: it records the winner, its kind and distance, and
                // the best candidate of the OTHER kind that lost. If a raid shows
                // "won=Wall_Outer_SS_7 (struct) @2.9m | runner-up unit RaidGuard... @11.4m"
                // repeating along a wall run, the selector is proven to be plain
                // nearest-wins with no route concept — the WO-1438 hypothesis, evidenced.
                if (!ReferenceEquals(previousFoe, _cachedFoe))
                {
                    _retargetCount++;
                    bool wonIsStruct = _cachedFoe != null && IsHostileStructure(_cachedFoe);
                    float wonDist = -1f;
                    if (foeValid)
                    {
                        // WO-1569: remember it WHILE IT IS LIVE. This is the only moment the
                        // position of a foe is guaranteed readable, and it is what the breach
                        // probe uses one death later.
                        _lastFoePos = _cachedFoe.WorldPosition;
                        _lastFoePosValid = true;
                        wonDist = Vector3.Distance(transform.position, _lastFoePos);
                    }
                    // Strings are built HERE, on the change, not on every 0.2 s scan (§1.3).
                    FlowTrace.Step("TroopAI",
                        $"id={_troopId} role={_troopRole} RETARGET#{_retargetCount} reason={_retargetReason} " +
                        $"dropped='{DescribeTarget(previousFoe)}' -> won='{DescribeTarget(_cachedFoe)}' " +
                        $"kind={(_cachedFoe == null ? "none" : wonIsStruct ? "struct" : "unit")} " +
                        $"dist={wonDist:F1}m | runnerUpOtherKind='{DescribeTarget(_lastRunnerUp)}' " +
                        $"dist={_lastRunnerUpDist:F1}m | sweep colliders={_lastOverlapCount} " +
                        $"accepted[unit={_lastAcceptedUnits},struct={_lastAcceptedStructs}] rejected={_lastRejected} " +
                        $"radius={_huntScanRadius:F1}m preferStruct={_preferStructures} " +
                        // WO-1438 THE GATE'S OWN VERDICT. preferUnit=False with route=PathPartial
                        // beside a struct win is the wall still standing; preferUnit=True the tick
                        // after a segment dies is the breach being taken. Both are one read.
                        $"| preferUnit={_lastPreferUnit} route={_routeStatus}");

                    // WO-1438 THE BREACH LINE. When the thing that just died was a STRUCTURE,
                    // the player expects a hole to have opened and the warband to pour through
                    // it. This probes whether the kill actually changed the navigable world:
                    // it asks the NavMesh for a COMPLETE path to the new target and reports the
                    // status. A "breach opened" that still reports PathPartial/PathInvalid is a
                    // hole in the geometry that is NOT a hole in the navmesh — and a selector
                    // that then picks the wall segment next door has not re-evaluated a route,
                    // because there is no route to re-evaluate.
                    // NOTE: a collapsed WallSegment keeps its component and its Hostile faction
                    // (only IsAlive flips), so the dropped foe can still be classified here.
                    // WO-1569: "foe-destroyed" joins "foe-died" here. A DefenseTower is
                    // Destroy()d by Destructible.NotifyBroken, so on the next Update it reaches
                    // the rescan already gone; before this ticket that read as neither reason and
                    // the probe fired anyway on a corpse it could not touch.
                    if ((_retargetReason == "foe-died" || _retargetReason == "foe-destroyed")
                        && previousFoe is IDamageableStructure)
                        TraceBreachProbe(previousFoe, previousFoePos, previousFoePosValid, _cachedFoe);
                }
            }

            var foe = foeValid ? _cachedFoe : null;
            if (foe == null)
            {
                SetInCombat(false);
                // WO-1438: the "didn't really fight" line. It reports the nearest hostile the
                // sweep SAW at any distance, so a troop standing idle next to a live defender
                // 21 m away with a 14 m radius indicts the radius, not the troop.
                if (FlowTrace.Enabled)
                {
                    Vector3? rallyDbg = TroopRally.Point;
                    // Key is PER TROOP (instance id) — a shared key would let one idle troop
                    // suppress the other nine and hide a whole idle warband behind one line.
                    FlowTrace.Throttle("TroopAI", $"troopai-idle-{GetInstanceID()}", 1f,
                        $"id={_troopId} role={_troopRole} IDLE/RALLY: no acquirable hostile inside " +
                        $"radius={_huntScanRadius:F1}m (last sweep colliders={_lastOverlapCount}, " +
                        $"accepted[unit={_lastAcceptedUnits},struct={_lastAcceptedStructs}], rejected={_lastRejected}; " +
                        $"nearestHostileAnyKind='{DescribeTarget(_lastNearestAny)}' @{_lastNearestAnyDist:F1}m) " +
                        $"rally={(rallyDbg.HasValue ? rallyDbg.Value.ToString("F1") : "<unset>")} " +
                        $"action={(rallyDbg.HasValue ? "walk-to-rally" : "stand-still")}");
                }
                // WO-1595 (CLI review 2026-09-07): RALLY wins when the player dropped a flag.
                // Spire push only fills the idle gap when there is NO rally — never starve the
                // WO-453 rally branch because RaidSpire.Active is always alive in a raid.
                Vector3? rally = TroopRally.Point;
                bool rallySet = rally.HasValue;
                if (rallySet)
                {
                    Vector3 r = rally.Value;
                    float flatDist = Vector2.Distance(
                        new Vector2(transform.position.x, transform.position.z),
                        new Vector2(r.x, r.z));
                    if (flatDist > RallyArrivalEpsilon)
                        MoveToward(r, dt);
                    // No early return: rally-wins is now expressed through the SAME predicate the
                    // regression pins (IdleShouldPushSpire returns false whenever rallySet), so the
                    // live branch and the pure helper cannot drift apart.
                }

                var spire = RaidSpire.Active;
                if (spire != null && spire.IsAlive
                    && RaidAssaultAi.IdleShouldPushSpire(rallySet, _assaultPhase))
                {
                    Vector3 goal = RaidAssaultAi.BiasMoveDestination(
                        _assaultJob, transform.position, spire.WorldPosition, _attackRange);
                    MoveToward(goal, dt);
                }
                return;
            }

            SetInCombat(true);
            Vector3 foePos = foe.WorldPosition;
            // WO-1569: this is the hot recorder. `foe` is proven live here (foeValid gated it
            // through IsLiveTarget above), so this is the cheapest correct place to keep the
            // last-live position the breach probe reads after the kill. Two stores per frame,
            // no allocation, no query.
            _lastFoePos = foePos;
            _lastFoePosValid = true;
            float dist = Vector3.Distance(transform.position, foePos);

            // WO-1438 STEERING HEARTBEAT — ~1/s per troop (Throttle guards internally).
            // The measured field is `moved`: actual metres covered per second, taken from the
            // transform, NOT the speed we asked for. `moved~=0` while `dist > attackRange` is
            // the signature of a troop pinned against geometry it cannot path around — the
            // failure that a "commanded speed" field could never report (§1.4b).
            if (FlowTrace.Enabled)
            {
                float span = Time.time - _aiLastPosTime;
                if (span > 0.75f)
                {
                    float moved = (transform.position - _aiLastPos).magnitude / Mathf.Max(span, 0.0001f);
                    _aiLastPos = transform.position;
                    _aiLastPosTime = Time.time;
                    FlowTrace.Throttle("TroopAI", $"troopai-engaged-{GetInstanceID()}", 1f,
                        $"id={_troopId} role={_troopRole} ENGAGED foe='{DescribeTarget(foe)}' " +
                        $"kind={(IsHostileStructure(foe) ? "struct" : "unit")} dist={dist:F1}m " +
                        $"attackRange={_attackRange:F1}m inRange={(dist <= _attackRange)} " +
                        $"moved={moved:F2}m/s commanded={_moveSpeed:F1} " +
                        $"agent={(_agent != null && _agent.isOnNavMesh ? "onNavMesh" : "OFF-NAVMESH")} " +
                        $"retargets={_retargetCount}");
                }
            }

            if (dist > _attackRange)
            {
                // WO-1595: back-line jobs hold standoff; Front/Breaker close to contact.
                Vector3 moveTo = RaidAssaultAi.BiasMoveDestination(
                    _assaultJob, transform.position, foePos, _attackRange);
                // If Bias says "hold" (returned self), face and wait for range rather than jitter.
                if ((moveTo - transform.position).sqrMagnitude < 0.01f)
                    FaceToward(foePos);
                else
                    MoveToward(moveTo, dt);
            }
            else
            {
                // in range — face the foe and attack on cooldown
                FaceToward(foePos);
                if (_attackCdRemaining <= 0f)
                    Attack(foe);
            }
        }

        private void SetInCombat(bool on)
        {
            if (_animator != null && _hasInCombat)
                _animator.SetBool(AnimInCombat, on);
        }

        private bool TryHealSquadmate(float dt)
        {
            TroopController target = null;
            float lowestRatio = 1f;
            float rangeSqr = _huntScanRadius * _huntScanRadius;
            for (int i = 0; i < Active.Count; i++)
            {
                var ally = Active[i];
                if (ally == null || ally == this || !ally.IsAlive || ally._hp >= ally._maxHp) continue;
                float sqr = (ally.transform.position - transform.position).sqrMagnitude;
                if (sqr > rangeSqr) continue;
                float ratio = ally._maxHp > 0f ? ally._hp / ally._maxHp : 1f;
                if (ratio < lowestRatio) { lowestRatio = ratio; target = ally; }
            }
            if (target == null) return false;

            SetInCombat(true);
            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (distance > _attackRange)
            {
                MoveToward(target.transform.position, dt);
                return true;
            }

            FaceToward(target.transform.position);
            if (_attackCdRemaining > 0f) return true;
            _attackCdRemaining = _attackCooldown;
            target.Heal(_attackDamage);
            // A heal must read instantly in the raid scrum: warm cast at the cleric,
            // green-gold impact on the ally. These route through the pooled VFX manager.
            VFXManager.Play(VFXType.Cast_Heal, transform.position, transform.rotation, playSound: false);
            VFXManager.Play(VFXType.Impact_Heal, target.transform.position, target.transform.rotation);
            if (_animator != null)
            {
                if (_hasCast) _animator.SetTrigger(AnimCast);
                else if (_hasAttack) _animator.SetTrigger(AnimAttack);
            }
            return true;
        }

        // =====================================================================
        //  Combat — talks only to IDamageable (Hostile foes), like Pet.
        // =====================================================================

        /// <summary>
        /// The nearest living hostile <see cref="IDamageable"/> within the hunt-scan
        /// radius, or null. Discovery is via an enemy-LayerMask overlap — the troop
        /// never references the concrete Village Enemy type (copied from Pet).
        /// WO-933 siege: when <see cref="_preferStructures"/> is set, any Hostile that
        /// also implements <see cref="IDamageableStructure"/> beats pure units (nearest
        /// among structures first; else nearest unit — never freezes idle).
        /// </summary>
        private IDamageable NearestHostile()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _huntScanRadius, _overlap, _enemyMask, QueryTriggerInteraction.Collide);
            // WO-1438 trace accounting. THE SHAPE OF THIS LOOP WAS THE TICKET: when
            // _preferStructures was FALSE (every role except "siege") a hostile STRUCTURE fell
            // through to the same nearest-wins `else` as a live defender, so a wall panel 3 m
            // away beat a garrison orc 11 m away and, when it died, the next-nearest thing was
            // the panel beside it. The loop now only MEASURES; the pick happens once, below,
            // against separated buckets.
            _lastOverlapCount = count;
            _lastAcceptedUnits = 0;
            _lastAcceptedStructs = 0;
            _lastRejected = 0;
            // Tracked independently of the winner so the retarget/idle lines can name the best
            // candidate of the OTHER kind, and the nearest hostile of ANY kind.
            IDamageable nearestStructAny = null, nearestUnitAny = null;
            float nearestStructAnySqr = float.MaxValue, nearestUnitAnySqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = _overlap[i];
                if (col == null) { _lastRejected++; continue; }
                var dmg = col.GetComponentInParent<IDamageable>();
                // WO-1439's ONE predicate, not a fourth hand-copy of `Faction != Hostile`.
                // MayAttack folds in the null + alive + different-faction checks.
                if (dmg == null || !CombatFactionRules.MayAttack(SelfFaction, dmg)) { _lastRejected++; continue; }
                float sqr = (dmg.WorldPosition - transform.position).sqrMagnitude;

                if (IsHostileStructure(dmg))
                {
                    _lastAcceptedStructs++;
                    if (sqr < nearestStructAnySqr) { nearestStructAnySqr = sqr; nearestStructAny = dmg; }
                }
                else
                {
                    _lastAcceptedUnits++;
                    if (sqr < nearestUnitAnySqr) { nearestUnitAnySqr = sqr; nearestUnitAny = dmg; }
                }
            }

            // ── THE PICK (WO-1438) ───────────────────────────────────────────────────────
            // PROVEN from logs/debug/troop-ai-blind-2026-09-06.log, 14:36 on build
            // 2026.09.06.358161:
            //   id=troop-archer RETARGET#1 ... won='Wall_Outer_SS_11(WallSegment)' kind=struct
            //   dist=4.2m ... accepted[unit=1,struct=17] ... radius=23.9m preferStruct=False
            // A LIVE defender was inside the sweep and the wall won anyway; the same archer then
            // walked SS_11 -> Watchtower -> SS_12 -> SS_7 -> SS_13 -> SS_6 -> SS_14, outward
            // along the ring, which is the owner's sentence in data.
            //
            // THE RULE, and why it is gated rather than absolute. Steering here is
            // _agent.Move(displacement) with NoObstacleAvoidance and no SetDestination, and the
            // sweep sees a guard THROUGH an intact baked wall. An unconditional "always prefer
            // the unit" would therefore push a footman into a navmesh edge and freeze it —
            // strictly worse than chewing the wall. So a unit wins only when it is REACHABLE:
            // either already inside attack range, or a complete, non-detouring NavMesh route to
            // it exists. THAT is "a breach is a route" expressed in the selector: while the wall
            // still stands there is no route and the wall stays the target; the moment the hole
            // opens the route completes and the defender wins. No pathing rewrite — the route is
            // read as a FILTER, never steered by.
            // WO-1595: split the structure bucket into OBJECTIVE (RaidSpire) vs other masonry
            // so Push/Finish can drive the capture goal instead of walking the wall ring.
            IDamageable nearestObjective = null;
            IDamageable nearestOtherStruct = null;
            float nearestObjectiveSqr = float.MaxValue, nearestOtherStructSqr = float.MaxValue;
            var activeSpire = RaidSpire.Active;
            if (nearestStructAny != null)
            {
                // Re-scan accepted structs from the overlap we already paid for — cheap second pass
                // only over the colliders already in _overlap (count known).
                for (int i = 0; i < _lastOverlapCount; i++)
                {
                    var col = _overlap[i];
                    if (col == null) continue;
                    var dmg = col.GetComponentInParent<IDamageable>();
                    if (dmg == null || !IsHostileStructure(dmg)) continue;
                    if (!CombatFactionRules.MayAttack(SelfFaction, dmg)) continue;
                    float sqr = (dmg.WorldPosition - transform.position).sqrMagnitude;
                    bool isObjective = activeSpire != null
                        && (ReferenceEquals(dmg, activeSpire) || dmg is RaidSpire);
                    if (isObjective)
                    {
                        if (sqr < nearestObjectiveSqr) { nearestObjectiveSqr = sqr; nearestObjective = dmg; }
                    }
                    else if (sqr < nearestOtherStructSqr)
                    {
                        nearestOtherStructSqr = sqr;
                        nearestOtherStruct = dmg;
                    }
                }
            }

            bool hasUnit = nearestUnitAny != null;
            bool hasObjective = nearestObjective != null;
            bool hasOtherStruct = nearestOtherStruct != null;
            bool hasStruct = hasObjective || hasOtherStruct;
            bool unitInAttackRange = hasUnit && nearestUnitAnySqr <= _attackRange * _attackRange;
            bool unitInPeelLeash = hasUnit
                && nearestUnitAnySqr <= RaidAssaultAi.PeelUnitLeashMeters * RaidAssaultAi.PeelUnitLeashMeters;
            bool recentlyHurt = (Time.time - _lastHurtAt) <= RaidAssaultAi.PeelHurtWindowSeconds;
            // Owner: if aggro / being attacked, stay alive. Leash is FIXED (not attackRange) so
            // archers do not abandon the push for every unit inside bow distance.
            bool peelThreat = recentlyHurt || unitInPeelLeash;

            if (!_preferStructures && hasUnit && hasStruct && !unitInAttackRange)
            {
                RefreshRouteToUnit(nearestUnitAny, Mathf.Sqrt(nearestUnitAnySqr));
            }
            else if (unitInAttackRange)
            {
                // ⚠ DO NOT CLEAR THE CACHED VERDICT HERE. A defender inside attack range needs no
                // route query, but clearing the flag would make the boundary FLICKER: the instant
                // that orc steps 0.1 m back out of range, the refresh branch runs, the 0.5 s
                // throttle returns early, and the troop reads the just-cleared False — so it
                // swings at the wall for half a second, then flips back, on every step-back in a
                // brawl. That reads to the player as exactly "they didn't really fight". Zero the
                // throttle instead, so the first out-of-range tick asks for a fresh answer.
                _routeCheckAt = 0f;
                _routeStatus = "in-range";
            }
            else
            {
                _routeToUnitOpen = false;
                _routeStatus = "not-asked";
            }

            // Route-to-objective: while closed we Breach; once open we Push/Finish and refuse
            // the wall-ring farm (owner 2026-09-07).
            RefreshRouteToObjective(activeSpire);
            bool objectiveInAttackRange = hasObjective
                && nearestObjectiveSqr <= _attackRange * _attackRange;
            _assaultPhase = RaidAssaultAi.ResolvePhase(
                peelThreat, _routeToObjectiveOpen, objectiveInAttackRange);

            _lastPreferUnit = RaidAssaultAi.PreferUnit(
                _assaultPhase, _preferStructures, hasUnit, hasStruct,
                unitInAttackRange, _routeToUnitOpen);

            int bucket = RaidAssaultAi.PickBucket(
                _assaultPhase, _preferStructures, hasUnit, hasObjective, hasOtherStruct,
                unitInAttackRange, _routeToUnitOpen);

            IDamageable winner;
            switch (bucket)
            {
                case 0: winner = nearestUnitAny; break;
                case 1: winner = nearestObjective; break;
                case 2: winner = nearestOtherStruct; break;
                default: winner = null; break;
            }

            // WO-1595: PreferUnit lives on RaidAssaultAi (PrefersUnitOverStructure retired).
            if (FlowTrace.Enabled)
            {
                FlowTrace.Throttle("RaidAI", $"raid-ai-phase-{GetInstanceID()}", 1f,
                    $"id={_troopId} job={_assaultJob} phase={_assaultPhase} " +
                    $"peelThreat={peelThreat} routeObj={_objectiveRouteStatus} " +
                    $"bucket={bucket} preferUnit={_lastPreferUnit} " +
                    $"has[unit={hasUnit},obj={hasObjective},wall={hasOtherStruct}]");
            }

            // Runner-up is decided against the WINNER, not against the flag. The old line read
            // `structWins = _preferStructures && bestStruct != null`, so with preferStruct=False
            // — every non-siege troop — it reported nearestStructAny, i.e. THE WINNER ITSELF.
            // The 09-06 capture shows that exactly: `won='Wall_Outer_SS_11' ...
            // runnerUpOtherKind='Wall_Outer_SS_11'`. A falsifiable field that echoes the answer
            // back cannot embarrass the selector, which is the one job it had (§1.4b).
            bool winnerIsStruct = winner != null && IsHostileStructure(winner);
            IDamageable runnerUp = winnerIsStruct ? nearestUnitAny : nearestStructAny;
            float runnerUpSqr = winnerIsStruct ? nearestUnitAnySqr : nearestStructAnySqr;
            _lastRunnerUp = runnerUp;
            _lastRunnerUpDist = runnerUp != null ? Mathf.Sqrt(runnerUpSqr) : -1f;

            if (nearestUnitAnySqr <= nearestStructAnySqr && nearestUnitAny != null)
            {
                _lastNearestAny = nearestUnitAny;
                _lastNearestAnyDist = Mathf.Sqrt(nearestUnitAnySqr);
            }
            else if (nearestStructAny != null)
            {
                _lastNearestAny = nearestStructAny;
                _lastNearestAnyDist = Mathf.Sqrt(nearestStructAnySqr);
            }
            else
            {
                _lastNearestAny = null;
                _lastNearestAnyDist = -1f;
            }
            if (_preferStructures && winnerIsStruct && winner != null)
            {
                if (!ReferenceEquals(_cachedFoe, winner))
                    FlowTrace.Step("TroopSiege",
                        $"id={_troopId}: prefer structure '{DescribeTarget(winner)}' " +
                        $"(unit-fallback={(nearestUnitAny != null ? DescribeTarget(nearestUnitAny) : "none")}).");
            }
            return winner;
        }

        /// <summary>
        /// WO-1595 — same reachability filter as <see cref="RefreshRouteToUnit"/> but aimed at
        /// the RaidSpire. When open, phase becomes Push/Finish and non-objective walls are refused.
        /// </summary>
        private void RefreshRouteToObjective(RaidSpire spire)
        {
            if (spire == null || !spire.IsAlive)
            {
                _routeToObjectiveOpen = false;
                _objectiveRouteStatus = "no-spire";
                return;
            }
            if (Time.time < _objectiveRouteCheckAt) return;
            _objectiveRouteCheckAt = Time.time + RouteCheckInterval;
            _routeToObjectiveOpen = false;
            _objectiveRouteStatus = "query";

            if (_routePath == null) _routePath = new NavMeshPath();
            Vector3 goal = spire.WorldPosition;
            float straight = Vector3.Distance(transform.position, goal);
            bool computed = NavMesh.CalculatePath(transform.position, goal, NavMesh.AllAreas, _routePath);
            if (!computed || _routePath.status != NavMeshPathStatus.PathComplete)
            {
                _objectiveRouteStatus = computed ? _routePath.status.ToString() : "CalculatePath-FAILED";
                return;
            }
            float pathLen = PathLength(_routePath);
            if (straight > 0.01f && pathLen > straight * RouteDetourFactor)
            {
                _objectiveRouteStatus = $"detour:{pathLen:F1}/{straight:F1}";
                return;
            }
            _routeToObjectiveOpen = true;
            _objectiveRouteStatus = "PathComplete";
        }

        /// <summary>
        /// WO-1438 — asks the NavMesh, at most once per <see cref="RouteCheckInterval"/>, whether
        /// a complete and non-detouring route to <paramref name="unit"/> exists. Read-only: the
        /// answer is a SELECTION filter; nothing steers by this path (steering stays
        /// <c>_agent.Move(displacement)</c>, per the SELECTOR trace line).
        ///
        /// Throttled because <see cref="NearestHostile"/> runs 5x/second per troop and this is
        /// the only query in the loop that is not free. The verdict is CACHED between refreshes
        /// on purpose — a target held for half a second longer is invisible; a path query per
        /// troop per scan is not.
        /// </summary>
        private void RefreshRouteToUnit(IDamageable unit, float straightLine)
        {
            if (Time.time < _routeCheckAt) return;      // keep the cached verdict
            _routeCheckAt = Time.time + RouteCheckInterval;
            _routeToUnitOpen = false;
            _routeStatus = "no-unit";
            if (unit == null) return;

            Guard.Try("TroopAI", $"route-probe id={_troopId}", () =>
            {
                if (_routePath == null) _routePath = new NavMeshPath();
                if (!NavMesh.CalculatePath(transform.position, unit.WorldPosition, NavMesh.AllAreas, _routePath))
                {
                    _routeStatus = "CalculatePath-FAILED";
                    return;
                }
                _routeStatus = _routePath.status.ToString();
                if (_routePath.status != NavMeshPathStatus.PathComplete) return;

                float len = PathLength(_routePath);
                // A PathComplete far longer than the straight line is the squad walking the
                // whole way round the wall ring — not "through the breach". The factor is
                // measured, not assumed: every PathComplete in
                // logs/debug/troop-ai-blind-2026-09-06.log came back within ~1.05x of its
                // straightLine (20.7/20.7, 20.3/20.4, 7.1/8.1), so this rejects detours without
                // rejecting anything that capture showed as a real route.
                _routeToUnitOpen = len > 0f && straightLine > 0.01f && len <= straightLine * RouteDetourFactor;
                if (!_routeToUnitOpen) _routeStatus += "-detour";
            });
        }

        /// <summary>
        /// Hostile structures dual-implement <see cref="IDamageable"/> +
        /// <see cref="IDamageableStructure"/> (walls, towers, gates, spire). Pure
        /// garrison units typically only implement <see cref="IDamageable"/>.
        /// </summary>
        private static bool IsHostileStructure(IDamageable dmg)
        {
            // WO-1438: routed through CombatFactionRules instead of an inline
            // `Faction != Hostile`. IsFriendlyFire is used rather than MayAttack because this
            // is a CLASSIFICATION question, not an attackability one — it must keep answering
            // "that was a structure" about a wall that has just collapsed, which is exactly
            // what the BREACH line asks it after a foe dies.
            if (dmg == null || CombatFactionRules.IsFriendlyFire(SelfFaction, dmg)) return false;
            return dmg is IDamageableStructure;
        }

        /// <summary>
        /// WO-1438 (§1.4b hollow-field repair): this used to return <c>GetType().Name</c>, so
        /// every wall panel in a raid printed the identical string "WallSegment". A trace that
        /// cannot tell <c>Wall_Outer_SS_7</c> from <c>Wall_Outer_SS_8</c> cannot show a squad
        /// walking sideways along a wall run, which is the exact behaviour this ticket is about.
        /// It now returns the INSTANCE name plus the type, so adjacent segments are separable.
        /// </summary>
        /// <summary>
        /// WO-1569 - TRUE only when this target still exists as far as UNITY is concerned.
        ///
        /// THE TRAP THIS CLOSES, and it is the whole ticket. <see cref="IDamageable"/> is an
        /// INTERFACE, so `dmg != null` compiles to a plain managed reference comparison and
        /// NEVER reaches UnityEngine.Object's overloaded ==. A component whose GameObject has
        /// been Destroy()d therefore PASSES `dmg != null` while its native side is gone, and the
        /// very next `dmg.WorldPosition` throws NullReferenceException inside
        /// UnityEngine.Component.get_transform. Device capture, build 2026.09.07.358872, scene
        /// RaidBase_raider_camp_small, F8 seq 4688/4689:
        ///   [Flow:TroopAI] breach-probe id=troop-footman FAILED: NullReferenceException
        ///     at UnityEngine.Component.get_transform ()
        ///     at DeNelle.Village.DefenseTower.get_WorldPosition ()
        ///     at DeNelle.Village.TroopController+&lt;&gt;c__DisplayClass119_0.&lt;TraceBreachProbe&gt;b__0 ()
        ///
        /// Why it surfaced on a TOWER and not on the 133 measured WALL probes: a collapsed
        /// WallSegment KEEPS its component (only IsAlive flips), while DefenseTower hands off to
        /// Destructible.NotifyBroken, which Destroy(gameObject)s it (DefenseTower.cs:170, :349).
        /// Unity's destroy is deferred to end of frame and the "foe-died" rescan runs on the NEXT
        /// Update, so by the time the probe reads the felled tower the native object is already
        /// gone.
        ///
        /// Public + static so the editor regression asserts the LIVE predicate rather than a
        /// parallel re-implementation (WO-1569).
        /// </summary>
        public static bool IsLiveTarget(IDamageable dmg)
        {
            if (dmg == null) return false;
            // The cast is what buys the Unity-aware comparison; `uo != null` here IS the
            // overloaded operator, which answers false for a destroyed object.
            if (dmg is UnityEngine.Object uo) return uo != null;
            // A non-Unity implementation (test doubles, pure data foes) has no native half to
            // lose, so a live managed reference is the whole answer.
            return true;
        }

        private static string DescribeTarget(IDamageable dmg)
        {
            if (dmg == null) return "<none>";
            if (dmg is Component c && c != null) return $"{c.name}({c.GetType().Name})";
            return dmg.GetType().Name;
        }

        /// <summary>
        /// Summed corner-to-corner length of a computed path, or -1 when there is none.
        /// Paired with the straight-line distance it is the falsifiable pair: a pathLength far
        /// longer than straightLine means the route detours around the wall ring instead of
        /// crossing the breach, even when the status reads PathComplete.
        /// </summary>
        private static float PathLength(NavMeshPath path)
        {
            if (path == null || path.corners == null || path.corners.Length < 2) return -1f;
            float total = 0f;
            for (int i = 1; i < path.corners.Length; i++)
                total += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return total;
        }

        /// <summary>
        /// WO-1438: fired once, on the retarget that follows a hostile STRUCTURE dying to this
        /// troop's hunt. Read-only — it computes a path, it never steers by one.
        ///
        /// ⚠ ITS ORIGINAL PROMISE WAS WRONG AND IS CORRECTED HERE, because a stale promise on a
        /// trace is worse than no promise. It claimed <c>routeStatus</c> alone separated
        /// "a route exists and the selector ignored it" from "no route ever existed". The
        /// 2026-09-06 capture killed that: routeStatus tracked the TARGET'S KIND
        /// (133/133 struct probes CalculatePath-FAILED, 7/7 unit probes PathComplete), because a
        /// structure's WorldPosition is inside solid geometry and CalculatePath refuses an
        /// unmappable destination. The field that answers the question is now
        /// <c>holeNavmesh=</c>: a tight NavMesh sample WHERE THE WALL STOOD.
        ///   * holeNavmesh=WALKABLE  -> the collapse really did open the navmesh; the route half
        ///                              of this ticket is solved and selection is the whole fix.
        ///   * holeNavmesh=NOT-WALKABLE -> the hole in the geometry is not a hole in the navmesh
        ///                              (raid scenes carry zero NavMeshObstacle and every collapse
        ///                              logs "0 carving obstacle(s) dropped"), so a carve is
        ///                              required before anyone can walk through a breach.
        /// Neither answer has been measured yet — this line is what will measure it.
        /// </summary>
        /// <param name="destroyedPos">
        /// WO-1569 - the felled structure's position as recorded WHILE IT WAS LIVE. It is passed
        /// in rather than read off <paramref name="destroyed"/> because reading it here is the
        /// crash: a DefenseTower is Destroy()d before this probe runs, its interface reference
        /// still passes `!= null`, and `WorldPosition` then throws in get_transform.
        /// </param>
        /// <param name="destroyedPosValid">False when no live position was ever recorded for it.</param>
        private void TraceBreachProbe(IDamageable destroyed, Vector3 destroyedPos,
                                      bool destroyedPosValid, IDamageable replacement)
        {
            if (!FlowTrace.Enabled) return;
            Guard.Try("TroopAI", $"breach-probe id={_troopId}", () =>
            {
                if (_breachPath == null) _breachPath = new NavMeshPath();

                // WO-1569 - EVERY position read below goes through one of these two, and neither
                // touches a destroyed object. `replacementLive` is the Unity-aware check that
                // `replacement != null` was NOT (see IsLiveTarget); `destroyedPos` is a value
                // captured before the kill, so it stays readable however the corpse was removed.
                bool replacementLive = IsLiveTarget(replacement);

                // Probe toward the thing that just became our target. If we have no target at
                // all, probe straight through the corpse of the wall we just felled — the
                // point the player expects us to walk through.
                Vector3 probeTo = replacementLive
                    ? replacement.WorldPosition
                    : (destroyedPosValid ? destroyedPos : transform.position);

                // ⚠ THE 2026-09-06 CAPTURE PROVED THIS PROBE WAS MEASURING THE WRONG THING, and
                // the repair is why the WO's "read one field" gate could not be answered off it:
                //   133 of 133 STRUCT reacquires -> routeStatus=CalculatePath-FAILED
                //     7 of   7 UNIT   reacquires -> routeStatus=PathComplete
                // routeStatus was a function of the TARGET'S KIND, not of the breach — a wall or
                // tower's WorldPosition sits inside solid geometry, off the navmesh, so
                // CalculatePath refuses the destination outright. Sample first, then path to the
                // sampled point, and the field starts answering the question it is named for.
                Vector3 pathTo = probeTo;
                string endSample = "n/a";
                if (NavMesh.SamplePosition(probeTo, out var endHit, ReplacementSampleRadius, NavMesh.AllAreas))
                {
                    pathTo = endHit.position;
                    endSample = $"hit@{Vector3.Distance(probeTo, endHit.position):F1}m";
                }
                else endSample = $"MISS>{ReplacementSampleRadius:F1}m";

                // THE DIRECT TEST of "does the hole in the geometry become a hole in the
                // navmesh?" — sample where the wall STOOD, with a tight radius. Raid scenes carry
                // zero NavMeshObstacle components and every collapse logs "0 carving obstacle(s)
                // dropped", so the expectation is a MISS; a HIT would mean the ground under a
                // felled wall really is walkable and the route half of this ticket is already
                // solved. Neither answer has been measured before this line existed.
                string breachSample = destroyedPosValid ? "n/a" : "UNRECORDED";
                if (destroyedPosValid)
                {
                    // Sampled at OUR foot height, not the structure's own Y. WallSegment's
                    // WorldPosition is transform.position (WallSegment.cs:223), but a tower's or
                    // spire's may be a mid-height pivot, and a 0.6 m radius would then MISS on
                    // elevation and print NOT-WALKABLE over ground that is perfectly walkable —
                    // the identical measured-the-wrong-thing failure this probe was just repaired
                    // for. The troop stands on the navmesh, so its Y is the right plane to ask on.
                    // WO-1569: the LAST LIVE position, not a fresh read. Before this ticket this
                    // line was `destroyed.WorldPosition` and it is the exact source of the
                    // seq 4688/4689 NullReferenceException whenever the felled structure was a
                    // DefenseTower. Towers become measurable here for the first time.
                    Vector3 hole = destroyedPos;
                    hole.y = transform.position.y;
                    breachSample = NavMesh.SamplePosition(hole, out var holeHit, BreachSampleRadius, NavMesh.AllAreas)
                        ? $"WALKABLE@{Vector3.Distance(hole, holeHit.position):F2}m"
                        : $"NOT-WALKABLE>{BreachSampleRadius:F2}m";
                }

                // Sample our own feet too: an OFF-navmesh troop fails every path query for a
                // reason that has nothing to do with the wall, and that must not read as a bake
                // defect at the far end.
                string selfSample = NavMesh.SamplePosition(transform.position, out _, BreachSampleRadius, NavMesh.AllAreas)
                    ? "on" : "OFF";

                bool computed = NavMesh.CalculatePath(transform.position, pathTo, NavMesh.AllAreas, _breachPath);
                string status = computed ? _breachPath.status.ToString() : "CalculatePath-FAILED";
                int corners = computed && _breachPath.corners != null ? _breachPath.corners.Length : 0;

                FlowTrace.Step("TroopAI",
                    $"id={_troopId} role={_troopRole} BREACH: structure '{DescribeTarget(destroyed)}' " +
                    $"died -> reacquired '{DescribeTarget(replacement)}' " +
                    $"kind={(!replacementLive ? "none" : IsHostileStructure(replacement) ? "struct" : "unit")} " +
                    $"holeNavmesh={breachSample} selfNavmesh={selfSample} endSample={endSample} " +
                    $"routeStatus={status} corners={corners} " +
                    $"straightLine={(replacementLive ? Vector3.Distance(transform.position, probeTo) : -1f):F1}m " +
                    $"pathLength={PathLength(computed ? _breachPath : null):F1}m");
            });
        }

        /// <summary>Lands one attack on <paramref name="foe"/> and resets the attack cooldown.</summary>
        private void Attack(IDamageable foe)
        {
            _attackCdRemaining = _attackCooldown;
            float dmg = _attackDamage;
            if (_preferStructures || _structureDamageMult != 1f || _unitDamageMult != 1f)
            {
                bool structure = IsHostileStructure(foe);
                float mult = structure ? _structureDamageMult : _unitDamageMult;
                if (mult > 0f) dmg *= mult;
            }
            // WO-935: mage strike uses unified CombatCast (anim + VFX) then damage.
            Transform foeTf = (foe as Component) != null ? (foe as Component).transform : null;
            // NOTE (2026-08-15 review): Hunter's Mark scaling now lives in ONE place —
            // Enemy.TakeDamageFrom (CombatMark GameObject-key fix). Scaling here too would
            // double-apply. The cast spell id also follows the troop's element instead of
            // hardcoding Fireball, so a Holy caster no longer plays Fire VFX.
            if (_useCastStrike)
            {
                CombatCast.Play(CastSpellIdFor(_element), transform, foeTf, () =>
                {
                    if (foe != null && foe.IsAlive)
                        foe.TakeDamage(dmg, _element);
                });
            }
            else
            {
                foe.TakeDamage(dmg, _element);
                if (_animator != null)
                {
                    if (_hasAttack)
                        _animator.SetTrigger(AnimAttack);
                    else if (_hasCast)
                        _animator.SetTrigger(AnimCast);
                }

                // WO-935 Phase 3 (melee row) — THE BLOW LANDING WAS THE ONLY SILENT BEAT IN A
                // TROOP MELEE EXCHANGE. The swing anim plays above and the damage lands, but
                // nothing marked the CONTACT, so on a structure target — which has no health bar
                // in view — the player could not tell a hit from a whiff. The mage row has had
                // its cast presentation since 2026-08-15 (CombatCast, the branch above); this is
                // the same beat for everyone who swings.
                //
                // Deliberately a VERBATIM MIRROR of the enemy-side melee connect
                // (Enemies/Enemy.cs, "melee connect vfx"): the two sides of the same exchange must
                // read the same way, and copying the shipped pattern is what keeps them paired.
                //   • Impact_Physical is ALREADY cataloged (Editor/VFXCatalogGenerator.cs -> Lana
                //     Slash_stone_once) and is a ONESHOT, so it cannot consume one of the 20
                //     leak-prone loop slots — troop melee ticks fast and a loop row here would
                //     saturate the cap in seconds.
                //   • It is a stone-slash ARC: the read is silhouette and direction, not hue
                //     (colourblind law, CLAUDE.md §7 / WO-935 §2 VFX rule 5).
                //   • Placed at the TARGET's chest, not the troop's — that is where the blow
                //     resolves and where the eye already is.
                //   • playSound:false — the attack cue belongs to the animator; VFXManager must
                //     not layer a second one.
                // Guard.Try on both, so a VFX fault can never cost the damage that already landed.
                var foeComp = foe as Component;
                if (foeComp != null)
                {
                    Vector3 hitPos = foeComp.transform.position + Vector3.up * 1.0f;

                    // WO-935 Phase 3 (ARCHER ROW) - the bow shot, and the reason it forks here
                    // rather than layering on top of the melee arc.
                    //
                    // The archer's damage was, and REMAINS, instant and hit-scan: TakeDamage has
                    // already run at the top of this branch and is deliberately untouched, so DPS,
                    // threat and every downstream damage hook are byte-identical to before this
                    // change. This is option (a) of the work order - PURE PRESENTATION over the
                    // existing instant damage. Option (b), a real travel time, moves combat maths
                    // and is a different ticket.
                    //
                    // RangedAttackVFX is the INCUMBENT launcher and is reused verbatim: Enemy
                    // attaches the identical component the identical way for enemy ranged casts
                    // (Enemies/Enemy.cs, EnsureCastVfx). It already owns the muzzle/release flash,
                    // the POOLED travelling body and the arrival impact, so this slice adds NO
                    // second projectile mover - which the work order forbids by name.
                    //
                    // NO onArrive callback is passed, on purpose. An arrival payload here would
                    // re-time the damage to the flight and quietly change DPS, and it would fire
                    // after this troop can have died. The arrow is decoration over a hit that has
                    // already landed.
                    //
                    // The melee arc is REPLACED, not layered: a stone-slash arc on a bow release
                    // would read as the wrong verb, and the two must stay distinguishable at a
                    // glance in greyscale (colourblind law).
                    if (_useBowShot)
                    {
                        Guard.Try("TroopVisual", "bow shot vfx", () =>
                        {
                            var bow = EnsureBowVfx();
                            if (bow != null) bow.FireArrow(hitPos);
                        });
                        return;
                    }

                    // Structure hits (walls/gates/buildings): the Lana Slash_stone_once mesh
                    // used by Impact_Physical has a URP particle mat with NO texture
                    // (1AB_mat _BaseMap/_MainTex null). At troop cadence that reads as a
                    // screen of white rectangles (owner raid 2026-09-09). The surface burst
                    // is the right read on masonry anyway — skip the slash there.
                    var surface = HitSurfaceVfx.Resolve(foeComp);
                    bool structureHit = surface == HitSurface.Wood
                                     || surface == HitSurface.Metal
                                     || surface == HitSurface.Stone;
                    if (!structureHit)
                    {
                        Guard.Try("TroopVisual", "melee connect vfx", () =>
                            VFXManager.Play(VFXType.Impact_Physical, hitPos,
                                            Quaternion.identity, playSound: false));
                    }

                    // WHAT the blow landed on. Resolve returns None rather than guessing and
                    // Play no-ops on None.
                    Guard.Try("TroopVisual", "melee surface impact", () =>
                        HitSurfaceVfx.Play(surface, hitPos));
                }
            }
        }

        /// <summary>Cast presentation id for the troop's damage element. None keeps the
        /// owner-observed Fireball look (the only shipped cast-strike troop, SC_Mage, is
        /// element None — do not change its felt visual); Aether/Ice read as Arcane until
        /// dedicated casts exist.</summary>
        private static string CastSpellIdFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame: return CombatCast.Fireball;
                case DamageElement.Aether:
                case DamageElement.Ice:   return CombatCast.Arcane;
                default:                  return CombatCast.Fireball;
            }
        }

        /// <summary>
        /// WO-935 Phase 3 (archer row): lazily attach the INCUMBENT ranged launcher.
        /// A VERBATIM mirror of Enemy.EnsureCastVfx - same component, same lazy
        /// TryGetComponent-then-AddComponent shape - so both sides of a ranged exchange are
        /// launched by one owner rather than by two near-copies that can drift.
        /// Never returns a component on a dead body.
        /// </summary>
        private RangedAttackVFX EnsureBowVfx()
        {
            if (this == null) return null;
            if (_bowVfx == null)
                _bowVfx = TryGetComponent<RangedAttackVFX>(out var rv) ? rv : gameObject.AddComponent<RangedAttackVFX>();
            return _bowVfx;
        }

        // =====================================================================
        //  Damageable — contact damage through IDamageableStructure.
        // =====================================================================

        /// <summary>
        /// Applies <paramref name="amount"/> damage to this troop. At 0 HP it falls —
        /// plays the Dead anim and is destroyed after a short hold (EXPENDABLE; no pool
        /// or respawn). Mirrors Pet/StoryCompanion TakeDamage simplicity.
        /// </summary>
        public void TakeDamage(float amount)
        {
            if (!IsAlive) return;
            _hp = Mathf.Max(0f, _hp - Mathf.Max(0f, amount));
            // WO-1595: recent hurt latches Peel so survival beats the spire push.
            if (amount > 0f) _lastHurtAt = Time.time;

            if (_animator != null && _hasHit && _hp > 0f) _animator.SetTrigger(AnimHit);
            if (_hp <= 0f) Die();
        }

#if UNITY_EDITOR
        /// <summary>
        /// WO-1595 handback — force one hunt scan so a batchmode raid-scene capture can
        /// emit <c>[Flow:RaidAI]</c> without entering Play Mode (OverlapSphere works in Edit).
        /// Editor-only: the sole callers are Assets/Editor/Regression/RaidAssaultTraceCapture.cs.
        /// </summary>
        public void ForceAssaultRescanForTrace()
        {
            _huntTimer = 0f;
            _cachedFoe = NearestHostile();
        }
#endif

        /// <summary>Heals the troop, clamped to max HP (for a future support kit).</summary>
        public void Heal(float amount)
        {
            if (!IsAlive) return;
            _hp = Mathf.Min(_maxHp, _hp + Mathf.Max(0f, amount));
        }

        /// <summary>The troop fell at 0 HP — latch the down state and destroy it (expendable).</summary>
        private void Die()
        {
            if (_dead) return;
            _dead = true;
            if (_animator != null && _hasDead) _animator.SetBool(AnimDead, true);
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            Destroy(gameObject, DeathHoldSeconds);
        }

        // =====================================================================
        //  Movement — eased NavMeshAgent.Move() drift (copied from Pet).
        // =====================================================================

        private void MoveToward(Vector3 target, float dt)
        {
            Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);

            Vector3 toTarget = flatTarget - transform.position;
            float remaining = toTarget.magnitude;

            // Cruise speed, damped down as it nears the target so it eases to a stop.
            float desired = _moveSpeed * Mathf.Clamp01(remaining / ArrivalDamp);

            // Accelerate/decelerate toward the desired speed — launch ramp + soft stop.
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, desired, Acceleration * dt);

            // Step, capped at the distance left so we never overshoot.
            float step = Mathf.Min(_currentSpeed * dt, remaining);

            // Move on the shared NavMesh — the agent clamps the step to the walkable
            // surface (no crossing walls/buildings) and follows its height. Fall back
            // to a raw transform move when the troop isn't on a NavMesh yet.
            if (remaining > 0.0001f)
            {
                Vector3 displacement = (toTarget / remaining) * step;
                if (_agent != null && _agent.isOnNavMesh)
                    _agent.Move(displacement);
                else
                    transform.position = Vector3.MoveTowards(transform.position, flatTarget, step);
            }

            FaceToward(target);
        }

        private void FaceToward(Vector3 target)
        {
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);
        }

        /// <summary>
        /// The parameter names a bound controller actually declares, for the §12 trace. Naming them
        /// is what turns "the troop does not animate" into "this controller speaks MoveVertical/
        /// Grounded/MoveState, not Speed/Attack/Hit/Dead" in a single captured line. Capped so a
        /// 32-parameter vendor controller cannot flood the log.
        /// </summary>
        private static string DescribeParams(Animator anim)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return "<no controller>";
            var ps = anim.parameters;
            if (ps == null || ps.Length == 0) return "<none>";
            var sb = new System.Text.StringBuilder();
            int max = ps.Length < 12 ? ps.Length : 12;
            for (int i = 0; i < max; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(ps[i] != null ? ps[i].name : "<null>");
            }
            if (ps.Length > max) sb.Append("/... (+").Append(ps.Length - max).Append(" more)");
            return sb.ToString();
        }

        private static DamageElement ParseElement(string element)
        {
            switch ((element ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "aether": return DamageElement.Aether;
                case "flame":  return DamageElement.Flame;
                case "ice":    return DamageElement.Ice;
                default:       return DamageElement.None;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, _attackRange);
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, _huntScanRadius);
        }
#endif
    }
}
