// =============================================================================
// DefenseTower — the auto-firing defensive structure (defensive-catalog v0 test).
// -----------------------------------------------------------------------------
// Proves "placement = role": an Archer tower on the GROUND (CanHitAir = false,
// short range) can't touch a flying dragon; a Wizard tower on the WALL-WALK
// (CanHitAir = true, long range, elevated) can. Targeting is by ROLE PRIORITY —
// the owner's "scamper to the DPS and healers" — squishy backline first.
//
// Reuses: IDamageable (DeNelle.Core.Combat) for find+damage, EnemyBrain.Role for
// priority, ProjectileMover for the visual bolt. All data-tunable.
//
// WHY IT IMPLEMENTS *TWO* DAMAGE CONTRACTS (WO-853 — mirrors RaidSpire.cs, the shipped
// precedent. This line also cited BreakableContainer.cs:38 until WO-1132 made that
// container an OPENABLE CHEST with neither damage contract — see IDamageableStructure.cs):
//
//   IDamageableStructure  is the seam ENEMIES use (Enemy.TickContactAttack /
//                         Enemy.RangedAttack / StructureBurn -> ApplyContactDamage).
//   IDamageable           is the seam the PLAYER uses. PlayerAttackController.
//                         ResolveAttack and TroopController.NearestHostile do a masked
//                         Physics.OverlapSphere, then GetComponentInParent<IDamageable>(),
//                         and REJECT anything whose Faction != CombatFaction.Hostile.
//
// A tower carrying ONLY IDamageableStructure can be hit but never FOUND by a search —
// which is why an EnemyOwned garrison turret was unkillable by hero and troops. Both
// contracts now land on ONE HP bucket: ApplyContactDamage and TakeDamage both route
// into ApplyDamage(). Faction is DERIVED from Allegiance, never serialized — an
// EnemyOwned turret reports Hostile (attackable), a PlayerOwned one reports Friendly
// (so the player's own defences are rejected by the same faction filters).
//
// The ONE place the two contracts answer DIFFERENTLY is IsAlive, and it is deliberate:
// the player seam reports liveness only, the enemy seam also requires PlayerOwned (an
// EXPLICIT interface member). See the long comment above the two properties before
// touching either — collapsing them back into one property reintroduces a softlock.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// Who a tower fights. <see cref="PlayerOwned"/> is the default and the legacy
    /// behaviour — every player-built / arena defender tower shoots Hostile enemies
    /// exactly as before. <see cref="EnemyOwned"/> flips the allegiance: a garrison
    /// turret targets the PLAYER PARTY (hero + companions) instead, damaging them
    /// through the same <see cref="IDamageableStructure"/> seam the enemy
    /// contact/ranged attacks already use on the hero.
    /// </summary>
    public enum TowerAllegiance
    {
        /// <summary>Default — shoots <see cref="CombatFaction.Hostile"/> enemies (legacy).</summary>
        PlayerOwned = 0,
        /// <summary>Garrison turret — shoots the player party (hero + companions).</summary>
        EnemyOwned = 1,
    }

    public sealed class DefenseTower : MonoBehaviour, IDamageable, IDamageableStructure
    {
        public float Range       = 14f;
        public float Damage      = 8f;
        public float FireRate    = 1.2f;   // shots per second
        public bool  CanHitAir   = false;  // ground archers: false · wall wizards: true
        // ANTI-AIR SPECIALIST (owner 2026-07-08 — the strategic counter to the flying dragon):
        // when true this tower is a DEDICATED anti-air Ballista. Acquire() acquires ONLY targets
        // whose ICombatLayered.Layer == CombatLayer.Flying and SKIPS all ground traffic — the exact
        // inverse of a normal tower. Implies CanHitAir (it must reach the air). A non-airOnly tower
        // is unchanged (ground behaviour + its existing CanHitAir gate). Set by StructureFactory
        // from the catalog row's optional "airOnly" flag (RepoProps.airOnly).
        public bool  AirOnly     = false;
        public float AirThreshold = 3.5f;  // target above this Y counts as "flying"
        public Color BoltColor   = Color.white;
        public DamageElement Element = DamageElement.None;

        // TOWER IDENTITY (owner 2026-07-08 "ballista shoots arrows not round pellet" /
        // "arcane casts spells"): per-catalog-entry projectile VISUAL style. Set by
        // StructureFactory from RepoProps.projectileStyle ("pellet"|"bolt"|"spell";
        // null/empty/unknown -> pellet, the legacy sphere). Visual only — damage,
        // targeting and travel logic are untouched.
        public string ProjectileStyle = null;

        // CATALOG IDENTITY (WO-870). The structures-catalog entries[].id this tower was built
        // from ("tower_ground_archer" / "tower_wall_wizard" / "tower_siege_tower" / ...), set by
        // StructureFactory.AttachBehaviorImpl. Used ONLY to select owner-tagged per-tower VFX
        // keys in ProjectileKeyFor - NEVER for gameplay (range / damage / fire rate / targeting
        // all stay data-driven off RepoProps). Null on a garrison turret or any tower not built
        // through the catalog, which simply falls through to the element/style mapping.
        public string CatalogId = null;

        // ---------------------------------------------------------------------
        // RANGE-DERIVED PROJECTILE SIZING (owner 2026-08-04: "make sure they are sized
        // appropriately for distance and not stupidly large or tiny").
        // Tower ranges span 14 m (Archer) to 36 m (Sky Ballista) - 2.6x - so one fixed
        // scale cannot be right for all of them. THE RULE: a travelling projectile should
        // read as roughly this fraction of its own flight path, so it looks the same at
        // every tower range. 0.06 * 14 = 0.84 m at the Archer; 0.06 * 36 = 2.16 m at the
        // Sky Ballista. Same shape as the DEF-208 / WO-751 repo.visualHeight fit-to-height
        // pass: MEASURE the authored art, NORMALIZE it, then scale by the gameplay number
        // (every owner-tagged pick in VfxManualPicks.json is authored at scale 1.0, so the
        // size cannot be read off the picks - it has to be derived).
        // ---------------------------------------------------------------------
        private const float ProjectileVisualFraction = 0.06f;

        // Clamp band on the derived scale - stops a pathological measurement (a prefab
        // authored at 0.01 m, or at 40 m) from shipping a speck or a comet.
        private const float ProjectileFitMin = 0.35f;
        private const float ProjectileFitMax = 3.0f;

        // Authored world size of each CODE-BUILT primitive visual below, so each is
        // normalized against its OWN authored size and all three land near targetSize:
        // pellet = a 0.4 m sphere, bolt = a ~1.1 m shaft, spell orb = a 0.5 m sphere.
        private const float PelletAuthoredSize = 0.4f;
        private const float BoltAuthoredSize   = 1.1f;
        private const float SpellAuthoredSize  = 0.5f;

        /// <summary>Resolved projectile visual archetype (see <see cref="ProjectileStyle"/>).</summary>
        private enum BoltStyle { Pellet, Bolt, Spell }
        private BoltStyle _style;
        private bool _styleResolved;

        // TOWER TIER (owner VfxManualPicks per-tier archer keys, 2026-07): the placed upgrade
        // level (1..3) read from this tower's OWN PlacedStructure marker — the SAME level
        // BuildModeController.ApplyTierStats scales range/damage from (both live on this
        // GameObject). The component ref is resolved once and cached; .level is a cheap field
        // read, safe in the fire hot-loop. Null (an EnemyOwned garrison turret or an un-placed
        // tower) -> tier 1. Purely a VISUAL selector: picks the per-tier archer arrow key in
        // ProjectileKeyFor; damage/targeting/travel are untouched.
        private PlacedStructure _placed;
        private bool _placedResolved;
        private int Tier
        {
            get
            {
                if (!_placedResolved) { _placed = GetComponent<PlacedStructure>(); _placedResolved = true; }
                return _placed != null ? Mathf.Clamp(_placed.level, 1, 3) : 1;
            }
        }

        // Allegiance — PlayerOwned (default) preserves every existing tower's
        // behaviour byte-for-byte; EnemyOwned garrison turrets target the player
        // party. Set by the spawner (GarrisonController) for garrison towers.
        public TowerAllegiance Allegiance = TowerAllegiance.PlayerOwned;

        // Elevation perk (wall-mounted defense): a tower seated on a wall-walk TOP gets the
        // high-ground range/LOS advantage. 1 = ground (no bonus); BaseLayoutLoader.Spawn sets
        // it (e.g. 1.25) when the structure is wall-mounted (PlacedStructureData.wallMounted).
        // A MULTIPLIER on EffectiveRange, so it survives tier upgrades (ApplyTierStats recomputes
        // the base Range from the catalog, never touching this factor). Bounded by the spawner.
        public float ElevationRangeMult = 1f;

        // ── IDamageableStructure — the marching-enemy siege target (F8-41) ─────
        // ROOT of F8-41 (DefenseTargetableRegression): this component did NOT implement
        // IDamageableStructure, so Enemy.SweepForNearestStructure's
        // collider.GetComponentInParent<IDamageableStructure>() returned null for every
        // tower collider — enemies could NEVER acquire a defensive tower and marched
        // straight past it to the Heart. Implementing the interface (mirrors WallSegment /
        // Gate) makes the tower a real siege target the Hollow Ones attack.
        //
        // HP: no per-entry hp is authored in structures-catalog.json / RepoProps, so this
        // is a serialized default. 200 mirrors the DEF-74 Tower.cs (_maxHp = 200f) precedent
        // and is sturdier than a wall (WallSegment's shared 0-100 damage track). Tunable.
        [Header("Durability (IDamageableStructure — enemy siege target)")]
        [Tooltip("Max HP. Enemies deal contact damage to towers they path to. No catalog hp is " +
                 "authored, so this default (200, mirrors Tower.cs DEF-74) makes a tower sturdier than a wall.")]
        [SerializeField, Min(10f)] private float _maxHp = 200f;
        private float _hp = -1f;   // <0 = not yet initialised; set to _maxHp on first use / Awake

        // Set at 0 HP. WO-672 Slice A originally kept the tower standing as an inoperable shell
        // awaiting Repair(); WO-753 SUPERSEDED that — ApplyDamage hands off to
        // Destructible.NotifyBroken, which Destroy(gameObject)s it (Destructible.cs:193). So this
        // flag gates the last frame of behaviour (Update early-outs, Repair() refuses) between the
        // break and Unity's end-of-frame destroy, and reads true on the dying object.
        private bool _broken;

        /// <summary>True once this tower was destroyed at hp 0 (it is being removed this frame; a
        /// destroyed tower is LOST — see <see cref="Repair"/>). (WO-672 / WO-753)</summary>
        public bool IsBroken => _broken;

        /// <summary>Health 0..1 — the wave damage-report fraction (WO-672; mirrors ResourceCollector.HpFraction).</summary>
        public float HpFraction => _maxHp > 0f ? Mathf.Clamp01(Hp / _maxHp) : 0f;

        /// <summary>Max HP (WO-761: lets StructureBurn size a percent-of-max fire tick).</summary>
        public float MaxHp => _maxHp;

        /// <summary>Fired once when this tower is destroyed (HP reaches 0). Observers
        /// (persistence / target-release, e.g. DragonBoss) can subscribe. It fires immediately
        /// AFTER Destructible.NotifyBroken, i.e. after Destroy(gameObject) has been requested but
        /// while Unity still has the object live (destroy is deferred to end of frame) — so a
        /// listener can still read this instance. WO-753 (owner 2026-07-19): a destroyed tower is
        /// NOT re-placed in-world; it returns ONLY via full-cost build-mode placement.</summary>
        public event System.Action<DefenseTower> Destroyed;

        // ─────────────────────────────────────────────────────────────────────
        //  THE TWO IsAlive's ARE DELIBERATELY DIFFERENT (WO-853 — DO NOT "SIMPLIFY"
        //  THESE INTO ONE PROPERTY). Both IDamageable and IDamageableStructure declare
        //  `bool IsAlive { get; }`; this type answers them differently ON PURPOSE, so it
        //  departs from the RaidSpire precedent (one shared IsAlive). (BreakableContainer
        //  was named here too; WO-1132 removed both its damage contracts entirely.)
        //  RaidSpire can share one because it has no friendly variant — a tower does.
        //
        //  PLAYER-facing (IDamageable, the implicit public member below) = LIVENESS ONLY.
        //  This is the seam that needed opening: it is what lets the hero and troops find
        //  and kill an EnemyOwned garrison turret.
        //
        //  ENEMY-facing (IDamageableStructure, the EXPLICIT member below) = liveness AND
        //  player ownership — byte-identical to the pre-WO-853 behaviour. It must stay that
        //  way: Enemy.SweepForNearestStructure / Enemy.ProbeForStructureForward / EnemyBrain
        //  acquire targets through this seam, and ApplyContactDamage still REFUSES damage to
        //  a non-PlayerOwned tower. If this seam reported an EnemyOwned turret alive, hostile
        //  mobs would path to their own garrison turret and flail at an invulnerable target
        //  forever. Dropping the ApplyContactDamage gate is NOT the alternative — that makes
        //  the garrison demolish itself.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="IDamageable"/> (the PLAYER/troop seam) — LIVENESS ONLY: true while this
        /// tower has HP left and has not broken, whatever its <see cref="Allegiance"/>. Before
        /// WO-853 the single IsAlive also required <c>Allegiance == PlayerOwned</c>, so an
        /// EnemyOwned garrison turret read as permanently dead and no player attack could ever
        /// acquire it. A tower that has already broken reports false even on the frame before
        /// Unity's deferred Destroy lands, so nothing re-targets a corpse.
        /// See the block above for why the <see cref="IDamageableStructure"/> answer differs.
        /// </summary>
        public bool IsAlive => Hp > 0f && !_broken;

        /// <summary>
        /// <see cref="IDamageableStructure"/> (the ENEMY siege / contact / burn seam) — liveness
        /// AND player ownership, exactly as the single IsAlive read before WO-853. EXPLICIT so it
        /// does not leak onto the public surface: only a caller holding an
        /// <c>IDamageableStructure</c> reference sees it, which is precisely the enemy-side
        /// acquisition path. Reporting false for an EnemyOwned turret keeps hostile mobs from
        /// acquiring a target that <see cref="ApplyContactDamage"/> refuses to damage, and keeps
        /// StructureBurn from igniting a fire that could never burn it down.
        /// The ownership tests on the other callers that meant ownership are unchanged:
        /// <see cref="ApplyContactDamage"/>, the <see cref="EffectiveDamage"/>/
        /// <see cref="EffectiveRange"/>/<see cref="EffectiveFireRate"/> stat gates, the
        /// <see cref="Update"/> allegiance fork, and WaveDamageReport's row filter.
        /// </summary>
        bool IDamageableStructure.IsAlive =>
            Allegiance == TowerAllegiance.PlayerOwned && Hp > 0f && !_broken;

        // ── IDamageable — the PLAYER / TROOP attack seam (WO-853) ─────────────
        // Everything that searches for a target to attack (PlayerAttackController.ResolveAttack,
        // TroopController.NearestHostile, HeroAbilities, pets) resolves IDamageable and rejects
        // Faction != Hostile. Implementing it here is what makes an enemy garrison turret a
        // findable, killable object instead of scenery.

        /// <summary>
        /// <see cref="IDamageable"/> — DERIVED from <see cref="Allegiance"/>, never serialized:
        /// an EnemyOwned garrison turret is <see cref="CombatFaction.Hostile"/> (the hero and
        /// troops accept it); a PlayerOwned defence is <see cref="CombatFaction.Friendly"/>, so
        /// the same faction filters REJECT it and the player can never attack their own tower.
        /// </summary>
        public CombatFaction Faction => Allegiance == TowerAllegiance.EnemyOwned
            ? CombatFaction.Hostile
            : CombatFaction.Friendly;

        /// <summary>
        /// <see cref="IDamageable"/> — this tower's world position, used by the range and
        /// nearest-target queries of every attacker that acquires through the interface.
        /// </summary>
        /// <remarks>
        /// WO-1569 - DESTROY-SAFE, and it has to be. A tower dies through
        /// <c>Destructible.NotifyBroken</c>, which <c>Destroy(gameObject)</c>s it, but callers
        /// hold it through <see cref="IDamageable"/> - an INTERFACE, where `!= null` is a plain
        /// managed comparison that never reaches UnityEngine.Object's overloaded ==. So a
        /// destroyed tower sails through every caller's null check and the bare
        /// `transform.position` below threw NullReferenceException in
        /// <c>UnityEngine.Component.get_transform</c> (device capture, build 2026.09.07.358872,
        /// scene RaidBase_raider_camp_small, F8 seq 4688/4689 - it killed TroopController's
        /// WO-1438 breach probe outright).
        ///
        /// The cache refreshes ON READ rather than in Awake/OnDestroy: a tower is static, so the
        /// last read is always current, and refresh-on-read needs no lifecycle hook and works in
        /// EditMode where Awake ordering is not guaranteed. The `this` test is Unity's own
        /// alive check; it runs once per read, and the hottest reader
        /// (TroopController.NearestHostile, per candidate at 5 Hz) is nowhere near a budget.
        ///
        /// This is a SAFETY NET, not the fix. Callers that must not aim at a corpse still filter
        /// on liveness (TroopController.IsLiveTarget); this only guarantees that reading the
        /// property can never take down the caller's whole code path.
        /// </remarks>
        public Vector3 WorldPosition
        {
            get
            {
                if (this != null) _lastWorldPosition = transform.position;
                return _lastWorldPosition;
            }
        }

        // Last position observed while this tower still existed natively. See WorldPosition.
        private Vector3 _lastWorldPosition;

        /// <summary>
        /// <see cref="IDamageable"/> hero / troop / pet / ability damage entry. Routes into the
        /// SAME <see cref="ApplyDamage"/> as the enemy contact seam, so a tower has exactly one
        /// HP bucket and one death path. The element is ignored — stone carries no resists.
        /// Carries NO allegiance gate: the Faction filter at the caller already decided that a
        /// PlayerOwned tower is not a valid target.
        /// </summary>
        public void TakeDamage(float amount, DamageElement element) => ApplyDamage(amount, "attack");

        /// <summary>
        /// <see cref="IDamageable"/> — a no-op. A tower models no movement or status track, so
        /// slow / freeze / burn statuses have nothing to apply to (StructureBurn drives its own
        /// damage-over-time through <see cref="ApplyContactDamage"/> instead).
        /// </summary>
        public void ApplyStatus(StatusEffect effect, float seconds) { /* a tower cannot be slowed or frozen */ }

        /// <summary>
        /// WO-672 (F8-42): full restore — HP back to max, broken cleared; the Update fire
        /// loop resumes on its own (it early-outs only while <see cref="_broken"/>). Cost
        /// enforcement lives with the caller, mirroring ResourceCollector.Repair.
        /// </summary>
        public void RestoreOwnedTownCondition(float condition)
        {
            if (gameObject.scene.name != DeNelle.Village.World.Camps.OwnedTownScenePose.SceneName &&
                gameObject.scene.name != DeNelle.Core.Combat.PracticeCombatPolicy.SceneName)
                throw new System.InvalidOperationException("Owned-town restoration requires its isolated scene.");
            if (float.IsNaN(condition) || float.IsInfinity(condition) || condition < 0 || condition > 1 || _broken)
                throw new System.InvalidOperationException("Owned-town condition requires a fresh valid structure.");
            Allegiance = TowerAllegiance.PlayerOwned;
            _hp = _maxHp * condition;
            if (condition == 0)
            {
                _broken = true;
                Destructible.Ensure(gameObject);
                Destructible.For(gameObject)?.NotifyBroken("Owned-town saved ruin");
            }
        }

        public void Repair()
        {
            // WO-753 ruling (owner 2026-07-19, SUPERSEDES WO-672's repair-back-online): a DESTROYED
            // tower is LOST - it returns ONLY via a full-cost build-mode placement, never an in-place
            // repair. Mirrors the guard Building.Repair already carries.
            if (_broken) return;
            _hp = _maxHp;
            FlowTrace.Step("Structure", $"'{name}' REPAIRED (hp {_maxHp:0})");
        }

        /// <summary>
        /// <see cref="IDamageable"/> — current HP, lazy-initialised to <see cref="_maxHp"/> on
        /// first read. Public because the IDamageable contract exposes it (low-HP target scoring
        /// reads it); it was private while this component carried only IDamageableStructure.
        /// </summary>
        public float Hp
        {
            get { if (_hp < 0f) _hp = _maxHp; return _hp; }
        }

        /// <summary>
        /// <see cref="IDamageableStructure"/> contact-attack entry point — a Hollow One in melee
        /// contact, a ranged enemy strike, or a StructureBurn tick routes its hit here (the SAME
        /// seam WallSegment / Gate / the Heart use). Routes into <see cref="ApplyDamage"/>.
        ///
        /// KEEPS its ownership gate: this seam is driven by the enemy side, and a garrison turret
        /// is their own asset. The gate is paired with the explicit
        /// <c>IDamageableStructure.IsAlive</c> above — together they keep the enemy seam
        /// byte-identical to pre-WO-853, so a hostile mob neither acquires nor damages an
        /// EnemyOwned turret. Keeping only one of the two would strand mobs on a target they
        /// cannot hurt. The PLAYER destroys an enemy turret through <see cref="TakeDamage"/>
        /// instead, which carries no such gate.
        /// </summary>
        public void ApplyContactDamage(float amount)
        {
            if (Allegiance != TowerAllegiance.PlayerOwned) return;   // garrison turrets aren't sieged by their own side
            ApplyDamage(amount, "contact");
        }

        /// <summary>
        /// The ONE damage path both contracts land on. Reduces HP; at zero the tower breaks,
        /// its VFX are torn down and <see cref="Destroyed"/> fires. Traces the hit + the kill (§12).
        /// </summary>
        /// <param name="amount">Damage to apply. Non-positive is ignored.</param>
        /// <param name="via">Which seam delivered it ("contact" | "attack") — trace text only.</param>
        private void ApplyDamage(float amount, string via)
        {
            if (amount <= 0f) return;
            if (Hp <= 0f || _broken) return;

            _hp = Hp - amount;
            FlowTrace.Throttle("DefenseTower", $"hurt:{GetInstanceID()}", 1f,
                $"'{name}' took {amount:0.#} {via} dmg -> HP {_hp:0.#}/{_maxHp:0.#} " +
                $"(allegiance={Allegiance}, faction={Faction}).");

            if (_hp <= 0f)
            {
                _hp = 0f;
                _broken = true;
                FlowTrace.Step("Structure", $"'{name}' BROKE (hp 0) — inoperable, and removed by Destructible below");
                // WO-753 (owner 2026-07-19 destroyed-items-...-vfx-cleanup): tear this tower's VFX
                // down WITH it, synchronously, through the ONE-owner Destructible - no aura/effect
                // outlives the dead tower (the "i see a vfx but no tower" orphan).
                //
                // NotifyBroken DOES Destroy(gameObject) (Destructible.cs:193) — it supersedes the
                // WO-672 "persistent inoperable shell" design, so this tower is GONE at end of
                // frame, not a standing husk. Unity defers the destroy, so the two lines after this
                // one still run against a live object. A tower with no PlacedStructure (every
                // EnemyOwned garrison turret) skips NotifyBroken's layout/free-build/rebuild-prompt
                // block, which is guarded on `placed != null` — only the VFX teardown and the
                // removal apply to it.
                Destructible.For(gameObject)?.NotifyBroken("DefenseTower hp0");
                Destroyed?.Invoke(this);
                if (_aimBeam != null) _aimBeam.enabled = false;   // drop the lock-on beam at the break
            }
        }

        private void Awake()
        {
            if (_hp < 0f) _hp = _maxHp;
            EnsureContactCollider();
            // WO-753: compose the ONE-owner VFX-teardown lifecycle onto this tower so a destroy /
            // break tears every held effect down in one place (no orphaned VFX).
            Destructible.Ensure(gameObject);
            // Covers a tower BAKED as EnemyOwned (RaidBaseGenerator.ArmTower serialises Allegiance
            // into the scene, so it is already correct here). Start() covers the runtime-armed case.
            EnsureEnemyOwnedHittable();
        }

        private void Start()
        {
            // SECOND attempt, and the one that actually fires for garrison camps.
            // GarrisonTurretArmer.cs:61-62 does `AddComponent<DefenseTower>()` and only THEN sets
            // `dt.Allegiance = EnemyOwned`. AddComponent runs Awake SYNCHRONOUSLY, so the Awake
            // call above still saw the PlayerOwned default and correctly declined to move anything.
            // Start runs after that assignment, so it sees the real allegiance. Idempotent — a
            // tower already moved in Awake no-ops here.
            EnsureEnemyOwnedHittable();
            EnsureRaidCadenceHeight();
        }

        /// <summary>
        /// Owner 2026-09-12: Iron Bastion turrets shipped at native FBX scale (PlaceTowerProp
        /// never ScaleToHeight). Town cadence is YHeightVariable * 1.2 = 4.8 m. Fit EnemyOwned
        /// raid watchtowers shorter than that so they read as towers, not props.
        /// Combat Range/Damage unchanged.
        /// </summary>
        private void EnsureRaidCadenceHeight()
        {
            if (Allegiance != TowerAllegiance.EnemyOwned) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene) || !scene.StartsWith("RaidBase_", System.StringComparison.Ordinal))
                return;
            if (name != null && name.IndexOf("Watchtower_", System.StringComparison.Ordinal) < 0
                && name.IndexOf("CornerPost_", System.StringComparison.Ordinal) < 0)
                return;

            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                if (rends[i] != null) b.Encapsulate(rends[i].bounds);
            float raw = b.size.y;
            const float cadence = StructureFactory.YHeightVariable * 1.2f;
            if (raw >= cadence * 0.85f) return;
            if (raw <= 0.05f) return;
            float f = cadence / raw;
            if (f > 16f) f = 16f;
            transform.localScale *= f;
            string msg = "cadence-fit " + name + " raw=" + raw.ToString("F2") + "m x" + f.ToString("F2")
                         + " scene=" + scene;
            FlowTrace.Once("RaidTower", "cadence-fit", msg);
        }

        /// <summary>Set once <see cref="EnsureEnemyOwnedHittable"/> has reached a verdict, so the
        /// Awake+Start double call moves layers (and warns) at most once.</summary>
        private bool _hittabilityResolved;

        /// <summary>
        /// Puts an ENEMY-OWNED turret's solid colliders on the "Enemy" physics layer so the
        /// player's masked sweeps can actually RETURN it. Directly models
        /// <c>RaidSpire.EnsureHittable</c> (RaidSpire.cs:160-203), including its loud warn when the
        /// project has no "Enemy" layer; the collider guarantee itself already lives in
        /// <see cref="EnsureContactCollider"/>, which Awake runs first, so a freshly-built capsule
        /// is moved too.
        ///
        /// WHY IT IS NEEDED (WO-853): PlayerAttackController.ResolveAttack, TroopController.
        /// NearestHostile, HeroAbilities and HeroTargetIndicator all do a LAYER-MASKED
        /// Physics.OverlapSphere and then GetComponentInParent&lt;IDamageable&gt;(). Baked turrets
        /// carry no layer assignment at all (RaidBaseGenerator.ArmTower sets none, so they sit on
        /// Default), which means implementing IDamageable buys nothing on its own — the sweep never
        /// returns the collider, so the faction filter is never even reached. Widening those masks
        /// to Default instead is NOT the alternative: HeroTargetIndicator's dated "2026-06-02
        /// targeting fix" note (above its candidate scan) records that a ~0-masked scan into a fixed
        /// buffer already failed once because the hub's ~2,900 colliders crowded the real target out
        /// of the buffer, and Default is exactly where the ground and hub props live.
        ///
        /// WHY MOVING A TOWER IS SAFE, THOUGH §4 FORBIDS IT FOR WALLS: the WO-853 §4 constraint is
        /// that WALLS must stay on "Structure" because that layer IS the line-of-sight blocker mask
        /// (DefenseTower.BlockedByWall, TowerCombat, ArcaneTower, PlayerAttackController,
        /// HeroTargetIndicator all linecast against it) — moving a wall would let towers shoot
        /// through walls again, regressing 2cb3c40d. A TOWER is not a LoS blocker: every one of
        /// those linecasts is masked to "Structure" only, so a tower sitting on "Enemy" neither
        /// occludes anything nor self-blocks its own BlockedByWall check. No wall layering is
        /// touched anywhere by this method.
        ///
        /// PLAYER-OWNED TOWERS NEVER MOVE — the early return below is what keeps the player's own
        /// defences off the layer their own hero sweeps for.
        /// </summary>
        private void EnsureEnemyOwnedHittable()
        {
            if (_hittabilityResolved) return;
            // A PlayerOwned tower is left exactly where it is. Note this may be reached while the
            // allegiance is still the default (see Start's note); returning WITHOUT setting the
            // resolved flag is deliberate so the later call re-decides.
            if (Allegiance != TowerAllegiance.EnemyOwned) return;

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer < 0)
            {
                _hittabilityResolved = true;   // retrying cannot conjure the layer; warn once.
                FlowTrace.Warn("DefenseTower",
                    $"'{name}': project has no 'Enemy' layer - an EnemyOwned turret cannot be moved onto " +
                    "the mask the hero's/troops' sweeps use, so it stays UNTARGETABLE by the player. Layer left untouched.");
                return;
            }

            int moved = 0;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger) continue;          // triggers are not swept
                if (c.gameObject.layer == enemyLayer) continue;  // idempotent
                c.gameObject.layer = enemyLayer;
                moved++;
            }
            if (gameObject.layer != enemyLayer) { gameObject.layer = enemyLayer; moved++; }

            _hittabilityResolved = true;
            FlowTrace.Step("DefenseTower",
                $"'{name}': EnemyOwned turret moved onto layer 'Enemy' ({moved} object(s)) - the hero's and " +
                "troops' masked sweeps can now return it, and its derived Faction=Hostile passes their filter.");
        }

        /// <summary>
        /// Guarantees a NON-TRIGGER collider exists so the enemy sweep's
        /// Physics.OverlapSphere(..., QueryTriggerInteraction.Ignore) can actually RETURN this
        /// tower (a trigger-only or collider-less structure is never hit). Idempotent: skips if a
        /// solid collider already exists in the hierarchy (the skinned visual usually carries one).
        /// Sized from the visual's renderer bounds — mirrors Tower.EnsureBodyCollider (DEF-74).
        /// The IDamageableStructure lives on this root, so GetComponentInParent from any child
        /// collider resolves it.
        /// </summary>
        private void EnsureContactCollider()
        {
            float height = 4.5f, radius = 0.9f;
            Vector3 worldCenter = transform.position + Vector3.up * (height * .5f);
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                height = Mathf.Max(1f, b.size.y);
                radius = Mathf.Max(0.4f, Mathf.Max(b.size.x, b.size.z) * 0.5f);
                worldCenter = b.center;
            }

            CapsuleCollider cap = null;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger) continue;
                var rootCapsule = c as CapsuleCollider;
                // Migrate only the old generated signature: world dimensions copied verbatim
                // to a scaled root, with the old bottom-pivot center. Authored colliders stay put.
                bool oldGenerated = rootCapsule != null && rootCapsule.transform == transform && rootCapsule.direction == 1 &&
                    Mathf.Abs(rootCapsule.height - height) < .002f && Mathf.Abs(rootCapsule.radius - radius) < .002f &&
                    Vector3.Distance(rootCapsule.center, new Vector3(0, height * .5f, 0)) < .002f &&
                    (rootCapsule.bounds.size.y < height * .25f || rootCapsule.bounds.size.y > height * 4f);
                if (!oldGenerated) return;
                cap = rootCapsule;
            }
            if (cap == null) cap = gameObject.AddComponent<CapsuleCollider>();
            cap.isTrigger = false;
            Vector3 scale = transform.lossyScale;
            cap.height = height / Mathf.Max(.0001f, Mathf.Abs(scale.y));
            cap.radius = radius / Mathf.Max(.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            cap.center = transform.InverseTransformPoint(worldCenter);
        }

        private float _cd;
        private float _scan;
        private readonly List<IDamageable> _hostiles = new List<IDamageable>();

        // EnemyOwned target list — the player party (hero HeroHealth + companions
        // StoryCompanion), both IDamageableStructure (the seam enemies already hit
        // the hero through). Only populated/used in EnemyOwned mode.
        private readonly List<IDamageableStructure> _partyTargets = new List<IDamageableStructure>();

        // ── Targeting indicator (cheap persistent LineRenderer aim-beam) ──────
        // A single, reused LineRenderer drawn from the muzzle to the locked
        // target while one is acquired. Created lazily, never per-shot — towers
        // fire often, so this stays allocation-free in the hot loop. Hidden
        // (disabled) whenever there is no valid target.
        private LineRenderer _aimBeam;
        private Material      _aimBeamMat;

        // WO-430 — PLAYER-owned towers get the Arcane Tower tier perks (towerDamageMult /
        // towerRangeMult). LIVE-READ (cheap, no current-HP problem) so the tier-4 Arcane
        // Overload temp-empower can buff dynamically. Enemy garrison turrets use base stats.
        //
        // WO-676 (BULWARK talents) — PLAYER-owned towers ALSO read the hero's strategic tree
        // at this same choke point: Keen Ballistics (towerDamage, fractional damage bonus),
        // Farsight Emplacements (towerRange, flat metres), Standing Orders (towerAttackSpeed,
        // fractional fire-rate bonus). Sums are refreshed on the existing 0.4s Rescan tick
        // (never per frame) via HeroTalentModifiers.StatSum — the SAME Σ-registry pattern
        // HeroHealth.TakeDamage consumes. Σ=0 (no nodes / no service) keeps every stat
        // byte-identical. EnemyOwned garrison turrets NEVER read the tree: the Allegiance
        // gates below hand them raw base stats, and RefreshTalentSums only runs from the
        // PlayerOwned Rescan path.
        private float EffectiveDamage => Allegiance == TowerAllegiance.PlayerOwned
            ? Damage * DeNelle.Core.State.ModifierService.Active.TowerDamageMult * _talentDamageMult : Damage;
        private float EffectiveRange => (Allegiance == TowerAllegiance.PlayerOwned
            ? Range * DeNelle.Core.State.ModifierService.Active.TowerRangeMult + _talentRangeAdd : Range) * ElevationRangeMult;
        private float EffectiveFireRate => Allegiance == TowerAllegiance.PlayerOwned
            ? FireRate * _talentFireRateMult : FireRate;

        // WO-676 — cached talent sums (identity until the first Rescan; refreshed every 0.4s).
        private float _talentDamageMult   = 1f;
        private float _talentRangeAdd     = 0f;
        private float _talentFireRateMult = 1f;

        /// <summary>WO-676 — the active hero's class slug for the talent Σ-registry read
        /// (mirrors PlayerAttackController's `_abilities.HeroClass : "knight"` resolution).</summary>
        private static string ActiveHeroClass()
        {
            var hero = HeroHealth.Instance;
            var abilities = hero != null ? hero.GetComponent<HeroAbilities>() : null;
            return abilities != null ? abilities.HeroClass : "knight";
        }

        /// <summary>
        /// WO-676 — one Σ-registry read per BULWARK type at the tower's existing stat seam.
        /// Called from the PlayerOwned <see cref="Rescan"/> tick only (EnemyOwned exclusion
        /// is structural). Zero unlocked nodes → identity (1 / 0 / 1), no behaviour change.
        /// </summary>
        private void RefreshTalentSums()
        {
            string heroClass = ActiveHeroClass();
            float dmg  = Talents.HeroTalentModifiers.StatSum(heroClass, "towerDamage");
            float rng  = Talents.HeroTalentModifiers.StatSum(heroClass, "towerRange");
            float rate = Talents.HeroTalentModifiers.StatSum(heroClass, "towerAttackSpeed");
            _talentDamageMult   = 1f + Mathf.Max(0f, dmg);
            _talentRangeAdd     = Mathf.Max(0f, rng);
            _talentFireRateMult = 1f + Mathf.Max(0f, rate);

            if (dmg > 0f)  FlowTrace.Once("DefenseTower", "talent-towerDamage",
                $"BULWARK towerDamage applied to player towers: +{dmg:P0} (Keen Ballistics).");
            if (rng > 0f)  FlowTrace.Once("DefenseTower", "talent-towerRange",
                $"BULWARK towerRange applied to player towers: +{rng:0.#}m (Farsight Emplacements).");
            if (rate > 0f) FlowTrace.Once("DefenseTower", "talent-towerAttackSpeed",
                $"BULWARK towerAttackSpeed applied to player towers: +{rate:P0} fire rate (Standing Orders).");
        }

        private void Update()
        {
            // WO-672 Slice C: a broken tower is INOPERABLE until repaired — no scan,
            // no acquire, no fire, no aim-beam. Repair() clears the flag and the loop
            // resumes on the next frame.
            if (_broken)
            {
                if (_aimBeam != null) _aimBeam.enabled = false;
                return;
            }

            // EnemyOwned garrison turret — target the player party instead of
            // Hostile enemies. Fully separate path so PlayerOwned stays identical.
            if (Allegiance == TowerAllegiance.EnemyOwned)
            {
                UpdateEnemyOwned();
                return;
            }

            _scan -= Time.deltaTime;
            if (_scan <= 0f) { Rescan(); _scan = 0.4f; }   // refresh target list a few times/sec

            // Acquire once per frame so the aim-beam tracks the current target
            // even between shots (the indicator reads "this tower is locked on").
            var target = Acquire();
            UpdateAimBeam(target);

            _cd -= Time.deltaTime;
            if (_cd > 0f) return;
            if (target == null) return;
            // WO-676: Standing Orders (towerAttackSpeed) — player towers only; the
            // EnemyOwned path below uses raw FireRate (garrison turrets read no talents).
            _cd = 1f / Mathf.Max(0.1f, EffectiveFireRate);
            Fire(target);
        }

        // ─────────────────────────────────────────────────────────────────────
        // EnemyOwned (garrison turret) — mirror of the PlayerOwned tick but the
        // target set is the PLAYER PARTY (hero + companions) and damage routes
        // through IDamageableStructure.ApplyContactDamage — the SAME seam the
        // enemy melee/ranged attacks use on the hero (Enemy.RangedAttack →
        // structure.ApplyContactDamage). No new damage API.
        // ─────────────────────────────────────────────────────────────────────
        private void UpdateEnemyOwned()
        {
            _scan -= Time.deltaTime;
            if (_scan <= 0f) { RescanParty(); _scan = 0.4f; }

            IDamageableStructure target = AcquireParty(out Vector3 targetPos);
            UpdateAimBeamAt(targetPos, target != null);

            _cd -= Time.deltaTime;
            if (_cd > 0f) return;
            if (target == null) return;
            _cd = 1f / Mathf.Max(0.1f, FireRate);
            FireAtParty(target, targetPos);
        }

        /// <summary>Rebuilds the player-party target list (hero + companions).</summary>
        private void RescanParty()
        {
            _partyTargets.Clear();
            // Raid towers are the defending base's main threat. Deployed troops screen the
            // hero and must therefore be valid targets (the previous hero/companion-only
            // list let towers ignore the attacking army entirely).
            var troops = TroopController.ActiveTroops;
            for (int i = 0; i < troops.Count; i++)
            {
                var troop = troops[i];
                if (troop != null && troop.IsAlive) _partyTargets.Add(troop);
            }
            var hero = HeroHealth.Instance;
            if (hero != null) _partyTargets.Add(hero);
            foreach (var c in FindObjectsByType<StoryCompanion>())
                if (c != null) _partyTargets.Add(c);
        }

        /// <summary>
        /// Nearest in-range, alive party member. Reuses the same range + air gate
        /// as the player path; party members are ground units so the air gate is
        /// effectively a no-op for them (a downed hero is skipped via IsAlive).
        /// </summary>
        private IDamageableStructure AcquireParty(out Vector3 bestPos)
        {
            IDamageableStructure best = null;
            bestPos = default;
            float bestSqr = float.MaxValue;
            bool bestIsTank = false;
            for (int i = 0; i < _partyTargets.Count; i++)
            {
                var d = _partyTargets[i];
                var mb = d as MonoBehaviour;
                if (mb == null || d == null || !d.IsAlive) continue;
                Vector3 p = mb.transform.position;
                float sqr = (p - transform.position).sqrMagnitude;
                if (sqr > Range * Range) continue;
                if (p.y > AirThreshold && !CanHitAir) continue;
                var troop = mb as TroopController;
                bool isTank = troop != null && troop.Def != null &&
                    string.Equals(troop.Def.Role, "tank", System.StringComparison.OrdinalIgnoreCase);
                // Tanks deliberately draw tower fire for the formation. Among equal roles,
                // retain nearest-target behavior so targeting stays readable.
                if ((isTank && !bestIsTank) || (isTank == bestIsTank && sqr < bestSqr))
                {
                    bestSqr = sqr;
                    best = d;
                    bestPos = p;
                    bestIsTank = isTank;
                }
            }
            return best;
        }

        /// <summary>
        /// Fire on a party member: identical bolt visual + muzzle flash as the
        /// player path, but damage lands via ApplyContactDamage (the hero/companion
        /// IDamageableStructure seam).
        /// </summary>
        private void FireAtParty(IDamageableStructure target, Vector3 targetPos)
        {
            using var _ = FlowTrace.Enter("DefenseTower", "FireAtParty (EnemyOwned)");
            Vector3 muzzle = transform.position + Vector3.up * 2f;

            // GUARD the bolt spawn: a thrown CreatePrimitive/material step must NOT abort the
            // shot (and orphan a half-built bolt). On failure we still apply damage below so the
            // turret stays functional — never silently dead.
            GameObject bolt = SpawnProjectileVisual(muzzle, targetPos + Vector3.up * 1f, "party");
            VerifyBoltRenders(bolt, "party");

            PlayFireVfx(muzzle, targetPos + Vector3.up * 1f);

            target?.ApplyContactDamage(Damage);   // same seam the enemy attacks use on the hero
        }

        private void OnDisable()
        {
            // Don't leave a stale beam pointing at a dead target when the tower
            // is disabled (pooled, swapped, destroyed-soon).
            if (_aimBeam != null) _aimBeam.enabled = false;
        }

        /// <summary>
        /// WO-1524 — the side this scan strikes FROM, handed to <see cref="CombatFactionRules"/>
        /// so friend-or-foe is answered by the ONE authority rather than an inline
        /// <c>Faction != CombatFaction.Hostile</c> copy (forbidden by that file's header).
        ///
        /// ⚠ A LITERAL, NOT <see cref="Faction"/>, AND THAT ASYMMETRY IS A FINDING, NOT AN
        /// OVERSIGHT — the same one recorded on <c>ArcaneTower.ScanSide</c>. This turret's
        /// <see cref="Faction"/> is derived from ownership and reads Hostile on an EnemyOwned
        /// garrison, yet this sweep has always admitted only Hostile bodies, i.e. a garrison
        /// turret's roster is its OWN side. (In practice the EnemyOwned path uses RescanParty,
        /// not this method — which is precisely why the latent contradiction survived here
        /// unnoticed.) Passing <see cref="Faction"/> would change behaviour; WO-1524 forbids
        /// that, so it is reported rather than absorbed. The literal is exact:
        /// <see cref="CombatFaction"/> has only two members (IDamageable.cs:28-34), so
        /// <c>IsFriendlyFire(Friendly, t)</c> ≡ <c>t.Faction != Hostile</c>.
        /// </summary>
        private const CombatFaction ScanSide = CombatFaction.Friendly;

        private void Rescan()
        {
            // WO-676: refresh the BULWARK talent sums on the same 0.4s cadence as the
            // target scan (never per frame). PlayerOwned path only — UpdateEnemyOwned
            // uses RescanParty, so garrison turrets structurally never read the tree.
            RefreshTalentSums();

            // PERF (overworld 1fps fix): the old scan was FindObjectsByType<MonoBehaviour>,
            // which enumerates EVERY MonoBehaviour in ALL loaded scenes (the additive
            // overworld terrain/props/NPCs = tens of thousands) on every 0.4s tick, per
            // tower — hundreds of ms/frame regardless of enemy count. Scan only the two
            // CONCRETE hostile IDamageable implementors instead (engine-filtered, returns
            // just the live enemy bodies + the dragon). Identical target set, no full-scene
            // enumeration. Same Faction==Hostile gate preserved.
            _hostiles.Clear();
            foreach (var d in FindObjectsByType<EnemyDamageable>())
            {
                // WO-1524: the ONE authority answers "is this one of ours?" — skip if so.
                // IsFriendlyFire (not MayAttack) is deliberate: it is liveness-BLIND, exactly
                // like the inline copy it replaces, so the scan set is unchanged. Acquire()
                // below still owns the IsAlive filter.
                if (d == null || CombatFactionRules.IsFriendlyFire(ScanSide, d)) continue;
                // TOWERS DEFEND THE TOWN AUTONOMOUSLY (owner 2026-06-28): roaming overworld
                // encounter reps (RepEngageWatcher) used to be SKIPPED here as "un-killable
                // Hp=9999 hooks". That is no longer true — reps are now killable (Hp=150,
                // OverworldEncounterSpawner) and tower damage does NOT trigger the arena
                // (RepEngageWatcher.RangedHitsEngage=false; only near-CONTACT with the HERO
                // pops the battle scene). So towers SHOULD target + damage + kill reps in
                // range: that IS the automated town defense (player focuses on leveling/
                // foraging; the town defends itself). The arena still fires only when a rep
                // engages the hero. Every hostile faction in range is acquired — no skip.
                _hostiles.Add(d);
            }
            foreach (var d in FindObjectsByType<DragonBoss>())
                // WO-1524: same authority, same liveness-blind shape as the sweep above.
                // DragonBoss implements IDamageable only (DragonBoss.cs:158), never
                // IDamageableStructure, so this overload resolves unambiguously.
                if (d != null && !CombatFactionRules.IsFriendlyFire(ScanSide, d))
                    _hostiles.Add(d);
        }

        private IDamageable Acquire()
        {
            IDamageable best = null;
            int   bestPri = int.MaxValue;
            float bestSqr = float.MaxValue;
            float range = EffectiveRange;   // WO-430 — Arcane Tower range perk (player towers)
            foreach (var d in _hostiles)
            {
                if (d == null || !d.IsAlive) continue;
                Vector3 p = d.WorldPosition;
                float sqr = (p - transform.position).sqrMagnitude;
                if (sqr > range * range) continue;

                // ANTI-AIR SPECIALIST filter (owner 2026-07-08 — the Ballista is the counter to
                // the flying dragon). An airOnly tower fires ONLY at FLYING targets and ignores ALL
                // ground traffic — the inverse of the normal path. The flying hook is the Core
                // contract: a candidate is a flier iff it implements ICombatLayered and reports
                // CombatLayer.Flying (anything NOT implementing it defaults to Ground). DragonBoss
                // returns Flying; every ground Hollow One is skipped. AirOnly implies CanHitAir, so
                // it bypasses the ground-tower air gate below (which stays for non-airOnly towers).
                if (AirOnly)
                {
                    var layered = d as ICombatLayered;
                    if (layered == null || layered.Layer != CombatLayer.Flying)
                    {
                        // Skip a ground target (throttled — the hot scan touches many enemies/sec).
                        FlowTrace.Throttle("DefenseTower", $"aa-skip:{GetInstanceID()}", 1f,
                            $"'{name}' (anti-air Ballista) SKIPS ground target '{(d as MonoBehaviour)?.name ?? "<t>"}' (layer={(layered != null ? layered.Layer.ToString() : "Ground/none")}).");
                        continue;
                    }
                    // Acquired a flier — this is the shot the Ballista exists for.
                    FlowTrace.Throttle("DefenseTower", $"aa-hit:{GetInstanceID()}", 1f,
                        $"'{name}' (anti-air Ballista) ACQUIRES flyer '{(d as MonoBehaviour)?.name ?? "<t>"}' (CombatLayer.Flying) at {Mathf.Sqrt(sqr):0.#}m.");
                }
                else if (p.y > AirThreshold && !CanHitAir) continue;   // ground tower can't reach a flier
                // LoS gate ("towers shoot through walls" fix, owner 2026-07) — a wall on the
                // "Structure" layer between the muzzle and the target blocks the shot. Mirrors
                // TowerCombat.BlockedByWall exactly (flyer-exempt, degrade-open). DefenseTower's
                // Acquire had NO LoS check, so it fired through every perimeter wall.
                if (BlockedByWall(d)) continue;
                int pri = Priority(d);
                if (pri < bestPri || (pri == bestPri && sqr < bestSqr))
                {
                    bestPri = pri; bestSqr = sqr; best = d;
                }
            }
            return best;
        }

        // LoS gate ("towers shoot through walls" fix, owner 2026-07) — a DIRECT mirror of
        // TowerCombat.BlockedByWall: true when a wall on the "Structure" layer sits between the
        // muzzle and the target, so the shot is blocked. DEGRADE OPEN — if the Structure layer is
        // absent (mask 0), never block (a misconfigured scene must not make towers inert). FLYER
        // EXEMPTION — a flier (the apex dragon) is engaged from above; a ground "Structure" wall
        // does NOT block the arcing shot (same exemption TowerCombat uses, so a high flyer is not
        // wrongly rejected by a muzzle→sky line clipping the castle roof). Muzzle matches Fire()'s
        // `transform.position + up*2`.
        private int _structureMask = -1;
        private bool BlockedByWall(IDamageable target)
        {
            if (target == null) return true;
            if (target is ICombatLayered layered && layered.Layer == CombatLayer.Flying) return false;
            if (_structureMask < 0) _structureMask = LayerMask.GetMask("Structure");
            if (_structureMask == 0) return false;
            Vector3 fPos = transform.position + Vector3.up * 2f;
            Vector3 tPos = target.WorldPosition;
            // WO-1720 — LoS DECISION POINT. Capture the hit collider so a future "Ballista fires
            // through a standing wall" capture is provable from ONE log read (pair blocked=false
            // with fPos/tPos Y against the known WallSegment collider-vs-renderer height gap;
            // WO-1719 measured colliderBounds 3m vs rendererBounds 15m on an intact wall).
            bool blocked = Physics.Linecast(fPos, tPos, out RaycastHit losHit, _structureMask, QueryTriggerInteraction.Ignore);
            if (FlowTrace.Enabled)
            {
                FlowTrace.Throttle("TowerLoS", $"DefenseTower:{GetInstanceID()}", 1f,
                    $"'{name}' BlockedByWall fPos={fPos} tPos={tPos} blocked={blocked}" +
                    (blocked && losHit.collider != null
                        ? $" hit='{losHit.collider.name}' hitColliderBoundsY=[{losHit.collider.bounds.min.y:F2}..{losHit.collider.bounds.max.y:F2}] hitPoint={losHit.point}"
                        : " (no Structure collider on the line — if a wall is visually there, its collider is undersized/absent)"));
            }
            return blocked;
        }

        // "Scamper to the DPS and healers" — squishy backline first, tanks last.
        private static int Priority(IDamageable d)
        {
            var mb = d as MonoBehaviour;
            var brain = mb != null ? mb.GetComponent<EnemyBrain>() : null;
            if (brain == null) return 2;   // bosses / unknown — middling
            switch (brain.Role)
            {
                case EnemyRole.Healer:   return 0;   // kill the healer first
                case EnemyRole.Ranged:   return 1;
                case EnemyRole.DPS:      return 1;
                case EnemyRole.MiniBoss: return 2;
                case EnemyRole.Tank:     return 3;   // tanks last
                default:                 return 2;
            }
        }

        private void Fire(IDamageable target)
        {
            // Hot loop (towers fire often): Throttle the entry trace to ~1/sec so it pinpoints
            // a live-firing tower without flooding the break-log.
            FlowTrace.Throttle("DefenseTower", $"fire:{GetInstanceID()}", 1f,
                $"Fire on '{(target as MonoBehaviour)?.name ?? "<target>"}' (dmg={EffectiveDamage:0.#}, element={Element}).");
            Vector3 muzzle = transform.position + Vector3.up * 2f;

            // GUARD the bolt spawn: a thrown CreatePrimitive/material step must NOT abort the
            // shot (and orphan a half-built bolt). Damage still lands below — the tower never
            // silently stops firing because one bolt's visual threw.
            GameObject bolt = SpawnProjectileVisual(muzzle, target.WorldPosition + Vector3.up * 1f, "hostile");
            VerifyBoltRenders(bolt, "hostile");

            // ── Muzzle flash / cast ───────────────────────────────────────────
            // Brief pooled burst at the fire point each shot. Reuses the shared
            // VFXManager (object-pooled, quality-gated) — element-tinted; a Spell-
            // style tower plays a caster wind-up + an impact burst at the target
            // instead, so its shot reads as a CAST, not a turret muzzle flash.
            // Null-safe via the static API: a no-op if VFXManager isn't booted.
            PlayFireVfx(muzzle, target.WorldPosition + Vector3.up * 1f);

            target.TakeDamage(EffectiveDamage, Element);   // hitscan damage on fire (WO-430: + Arcane Tower damage perk)
        }

        // RENDER-VERIFY (TGVRU "V"; mirrors Tower.VerifyVisualRendersNow): a spawned bolt MUST
        // carry >=1 ENABLED Renderer with a sharedMesh, else it's an invisible projectile (the
        // "tower fires but nothing visible" symptom). Throttled (hot loop) + Fail-loud on a miss
        // so a capture self-reports a dud bolt; the shot's damage already landed regardless.
        private void VerifyBoltRenders(GameObject bolt, string kind)
        {
            if (bolt == null)
            {
                FlowTrace.Fail("DefenseTower", $"VerifyBolt ({kind}): bolt is null — spawn threw; no visible projectile (damage still applied).");
                return;
            }
            int enabled = 0, withMesh = 0;
            foreach (var r in bolt.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (r.enabled) enabled++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) withMesh++;
            }
            if (enabled == 0 || withMesh == 0)
            {
                FlowTrace.Fail("DefenseTower",
                    $"VerifyBolt ({kind}) FAILED on '{bolt.name}': enabledRenderers={enabled} withMesh={withMesh} — invisible bolt.");
                return;
            }
            FlowTrace.Throttle("DefenseTower", $"boltok:{GetInstanceID()}", 1f,
                $"VerifyBolt ({kind}) ok: enabledRenderers={enabled} withMesh={withMesh}.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Projectile VISUAL styles (owner 2026-07-08 tower-identity pass).
        // Data-driven off the catalog row's optional "projectileStyle" string;
        // pure presentation — travel (ProjectileMover), damage and targeting
        // are byte-identical across styles.
        //   Pellet — legacy emissive sphere (default / fallback).
        //   Bolt   — elongated shaft + tip, oriented along velocity (ballista
        //            arrow; ProjectileMover already faces travel each frame).
        //   Spell  — glowing arcane orb; PlayFireVfx adds cast + impact bursts.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Resolve the catalog style string once (Step on resolution, Warn +
        /// pellet fallback on an unknown value — per docs/INSTRUMENTATION_STANDARD.md).</summary>
        private BoltStyle ResolveStyle()
        {
            if (_styleResolved) return _style;
            _styleResolved = true;

            string s = ProjectileStyle != null ? ProjectileStyle.Trim().ToLowerInvariant() : string.Empty;
            switch (s)
            {
                case "":
                case "pellet": _style = BoltStyle.Pellet; break;
                case "bolt":   _style = BoltStyle.Bolt;   break;
                case "spell":  _style = BoltStyle.Spell;  break;
                default:
                    FlowTrace.Warn("DefenseTower",
                        $"'{name}': unknown projectileStyle '{ProjectileStyle}' — falling back to pellet (valid: pellet|bolt|spell).");
                    _style = BoltStyle.Pellet;
                    return _style;
            }
            FlowTrace.Step("DefenseTower",
                $"'{name}': projectile style resolved -> {_style} (catalog projectileStyle='{ProjectileStyle ?? "<null>"}', element={Element}).");
            return _style;
        }

        /// <summary>
        /// Build + launch the per-shot projectile visual for the resolved style.
        /// Guarded: a thrown CreatePrimitive/material step logs + returns whatever was
        /// built (possibly null) — the caller's damage still lands, and VerifyBoltRenders
        /// self-reports an invisible shot. Same non-pooled ProjectileMover path as ever.
        /// </summary>
        private GameObject SpawnProjectileVisual(Vector3 muzzle, Vector3 targetPos, string kind)
        {
            GameObject bolt = null;
            Guard.Try("DefenseTower", $"spawn {kind} projectile", () =>
            {
                // Range-derived target size: this shot should read as ProjectileVisualFraction
                // of its own flight path, so a 14 m archer arrow and a 36 m ballista spear look
                // equally legible from the same camera. Range (not EffectiveRange) is the
                // catalog number, so the visual does not shimmy as perks/elevation move the reach.
                float targetSize   = ProjectileVisualFraction * Mathf.Max(1f, Range);
                float authoredSize = PelletAuthoredSize;

                switch (ResolveStyle())
                {
                    case BoltStyle.Bolt:  bolt = BuildBoltVisual();  authoredSize = BoltAuthoredSize;  break;
                    case BoltStyle.Spell: bolt = BuildSpellVisual(); authoredSize = SpellAuthoredSize; break;
                    default:              bolt = BuildPelletVisual(); authoredSize = PelletAuthoredSize; break;
                }

                // Normalize the CODE-BUILT primitive against its own authored size, so the
                // pellet / bolt / spell orb all land near targetSize instead of the bolt being
                // a speck at range 36. Same clamp band as the Hovl fit below. Travel speed,
                // damage and targeting are untouched - this is the visual root scale only.
                float primFit = Mathf.Clamp(targetSize / Mathf.Max(0.0001f, authoredSize),
                                            ProjectileFitMin, ProjectileFitMax);
                bolt.transform.localScale = bolt.transform.localScale * primFit;

                bolt.transform.position = muzzle;
                // Face the target immediately so the first rendered frame of an elongated
                // bolt already lies along the flight line (ProjectileMover re-faces per frame).
                Vector3 dir = targetPos - muzzle;
                if (dir.sqrMagnitude > 0.0001f) bolt.transform.rotation = Quaternion.LookRotation(dir);

                // Owner VfxManualPicks (2026-07): Hovl/PP projectile FOLLOWS the mover so Archer /
                // Ballista / elemental bolts read as real shots, not bare pellets. Loop key returns
                // a VFXHandle — StopSoft on arrive so the trail does not linger. Null-safe if the
                // catalog row is missing (primitive bolt still flies). Impact fires on ARRIVAL so
                // hitscan damage + visual connect still line up with the bolt land.
                string projKey = ProjectileKeyFor(Element, ResolveStyle());
                VFXHandle boltFx = null;
                if (!string.IsNullOrEmpty(projKey))
                {
                    // FIT-TO-RANGE: measure the owner-tagged prefab once, then scale it so it
                    // reads at targetSize. The picks are all authored at scale 1.0, so passing
                    // 0f (row DefaultScale) shipped every tower the same size regardless of range.
                    float measured = VFXManager.MeasureKeyVisualSize(projKey);
                    float fit      = VFXManager.ResolveFitScale(projKey, targetSize,
                                                                ProjectileFitMin, ProjectileFitMax);
                    // CAPTURED DATA (owner asked to see the numbers): one line per tower+key with
                    // the range, the derived target, what the prefab actually measured, and the
                    // scale that shipped. If measured=0 the fit falls back to 1.0 and VFXManager
                    // has already warned which key could not be measured.
                    // The Once key carries the measured/unmeasured state so a very first shot fired
                    // before VFXManager finished loading its catalog (measured=0 -> fit 1.0) does
                    // not BURN the trace slot: the first genuinely measured shot still reports.
                    FlowTrace.Once("DefenseTower", $"projfit:{CatalogId ?? name}:{projKey}:{(measured > 0f ? "m" : "u")}",
                        $"projectile FIT '{projKey}' on tower '{CatalogId ?? name}': range={Range:0.#}m " +
                        $"fraction={ProjectileVisualFraction:0.###} targetSize={targetSize:0.###}m " +
                        $"measuredPrefab={measured:0.###}m -> fitScale={fit:0.###} " +
                        $"(band {ProjectileFitMin:0.##}..{ProjectileFitMax:0.##}, primitive x{primFit:0.###}).");
                    boltFx = VFXManager.PlayKey(projKey, muzzle, bolt.transform.rotation, null,
                                                BoltColor, fit, 0f, bolt.transform);
                }
                string impactKey = ImpactKeyFor(Element);
                var impactType = ImpactVfxFor(Element);
                bolt.AddComponent<ProjectileMover>().Launch(targetPos, 40f, CanHitAir ? 0.1f : 0.35f,
                    () =>
                    {
                        VFXManager.Play(impactType, targetPos);   // legacy procedural fallback
                        if (!string.IsNullOrEmpty(impactKey))
                            VFXManager.PlayKey(impactKey, targetPos, default, null, BoltColor);
                    },
                    // WO-1155: the trail's StopSoft is the mover's RELEASE, not part of the arrival
                    // payload. A bolt destroyed in flight (scene unload / teardown) never arrives,
                    // and the followed Hovl host is a POOLED instance that is never destroyed — so
                    // VFXManager's destroyed-host sweep could never reclaim that loop slot.
                    () => boltFx?.StopSoft());
            });
            return bolt;
        }

        /// <summary>Legacy pellet — the emissive 0.4 m sphere (default style).</summary>
        private GameObject BuildPelletVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Bolt";
            go.transform.localScale = Vector3.one * 0.4f;
            StripCollider(go);
            ApplyBoltMaterial(go, BoltColor, 2f);
            return go;
        }

        /// <summary>
        /// Ballista/archer BOLT — a thin elongated shaft (cylinder laid along +Z) with a
        /// short steel tip cube at the nose. Code-built primitives, one shared parent the
        /// mover rotates along velocity. Reads as an arrow, not a pellet.
        /// </summary>
        private GameObject BuildBoltVisual()
        {
            var root = new GameObject("Bolt");

            // Shaft: cylinder's long axis is local Y — rotate X+90 so it lies along the
            // parent's forward (+Z), the direction ProjectileMover faces.
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localScale    = new Vector3(0.08f, 0.55f, 0.08f);   // 1.1 m long, thin
            StripCollider(shaft);
            // Wooden-dark shaft: the tower's BoltColor darkened so element tint still reads.
            ApplyBoltMaterial(shaft, BoltColor * 0.55f, 0.6f);

            // Tip: a small cube at the nose, yaw/pitched 45° so its corner leads — a
            // cheap diamond arrowhead silhouette.
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tip.name = "Tip";
            tip.transform.SetParent(root.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.62f);
            tip.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            tip.transform.localScale    = Vector3.one * 0.12f;
            StripCollider(tip);
            ApplyBoltMaterial(tip, new Color(0.75f, 0.78f, 0.82f), 1.2f);   // steel glint

            return root;
        }

        /// <summary>
        /// Arcane SPELL orb — a larger, strongly emissive sphere that reads as a glowing
        /// projectile (the cast/impact VFX around it come from PlayFireVfx).
        /// </summary>
        private GameObject BuildSpellVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SpellOrb";
            go.transform.localScale = Vector3.one * 0.5f;
            StripCollider(go);
            // Hot emissive violet-leaning glow; BoltColor still tints per tower.
            ApplyBoltMaterial(go, BoltColor, 4f);
            return go;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        }

        /// <summary>Shared URP/Lit emissive material apply (the legacy pellet look, tunable).</summary>
        private static void ApplyBoltMaterial(GameObject go, Color color, float emission)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return;
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * emission); }
            var r = go.GetComponent<Renderer>(); if (r != null) r.sharedMaterial = m;
        }

        /// <summary>
        /// Per-shot fire VFX. Pellet/Bolt keep the legacy element-tinted muzzle burst;
        /// Spell style reads as a CAST — wind-up at the muzzle + an element impact burst
        /// at the target point. All through the pooled, null-safe VFXManager static API.
        /// </summary>
        private void PlayFireVfx(Vector3 muzzle, Vector3 targetPos)
        {
            // Muzzle / cast only. Travelling Hovl projectile + impact-on-arrive are owned by
            // SpawnProjectileVisual (owner VfxManualPicks wire) so spell/bolt/pellet all share
            // one land beat — no double impact flash at fire time.
            //
            // DELIBERATELY *NOT* RANGE-SCALED (owner ruling, WO-870): a muzzle flash / cast burst
            // belongs to the TOWER, not to the flight path - it is read at the tower's own scale
            // right where the player is looking, so growing it with range would just make a long-
            // range tower's barrel look oversized. Only the TRAVELLING projectile is fitted to
            // range (see ProjectileVisualFraction in SpawnProjectileVisual). Same for the impact
            // burst, which is sized by what it hits. Do not fold these into that constant.
            if (ResolveStyle() == BoltStyle.Spell)
            {
                VFXManager.Play(VFXType.Cast_MageCharge, muzzle);
                VFXManager.PlayKey(CastKeyFor(Element), muzzle, default, null, BoltColor);
                return;
            }
            VFXManager.Play(MuzzleVfxFor(Element), muzzle);
            VFXManager.PlayKey(CastKeyFor(Element), muzzle, default, null, BoltColor);
        }

        /// <summary>Maps the tower's damage element to a point-impact VFXType (spell style).</summary>
        private static VFXType ImpactVfxFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame: return VFXType.Impact_Flame;
                case DamageElement.Ice:   return VFXType.Impact_Ice;
                case DamageElement.None:  return VFXType.Impact_Physical;
                default:                  return VFXType.Impact_Aether;   // Aether
            }
        }

        /// <summary>Maps the tower's damage element to its tower-bolt muzzle VFXType.</summary>
        private static VFXType MuzzleVfxFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame: return VFXType.Projectile_TowerFire;
                case DamageElement.Ice:   return VFXType.Projectile_TowerIce;
                default:                  return VFXType.Projectile_TowerArcane;   // None / Aether
            }
        }

        // ── WO-VFX-TOWERS / owner VfxManualPicks (2026-07): element + style → catalog keys ──
        // Keys resolve through HovlVfxCatalog (manual overlay wins). Null = PlayKey no-op;
        // legacy VFXType muzzle/impact still layers underneath for a safe fallback.

        private static string CastKeyFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame:  return "Fire_Cast";
                case DamageElement.Aether: return "SimpleCast_Cast";
                case DamageElement.Ice:    return "Freezing_Projectile"; // cold muzzle flash
                default:                   return "PP_MuzzleFlash";      // physical bolt / ballista
            }
        }

        private static string ImpactKeyFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame:  return "FireImpact_Impact";
                case DamageElement.Ice:    return "Freezing_Impact";
                case DamageElement.Aether: return "PP_PlasmaExplosionEffect";
                default:                   return "Spear_Impact";   // None / Physical arrow
            }
        }

        /// <summary>
        /// Travelling projectile key from owner picks. Bolt style = archer/ballista family;
        /// Spell = arcane/fire orbs; AirOnly ballista prefers the ranger spear bolt so AA
        /// spears read heavier than ground archer arrows.
        /// </summary>
        private string ProjectileKeyFor(DamageElement element, BoltStyle style)
        {
            // OWNER-TAGGED PER-TOWER PICKS FIRST (the owner tags the key; this maps it VERBATIM).
            // Needed because several towers are indistinguishable by (element, style, tier,
            // airOnly) alone: the ground Archer (range 14) and the Ballista (range 22) are BOTH
            // bolt / None / not-airOnly, so the Ballista used to borrow the archer's per-tier
            // arrow. Catalog identity is the only thing that tells them apart.
            string ownerTagged = OwnerTaggedProjectileKey(CatalogId);
            if (!string.IsNullOrEmpty(ownerTagged))
            {
                FlowTrace.Once("DefenseTower", $"projtag:{CatalogId}",
                    $"owner-tagged projectile: tower '{CatalogId}' -> key '{ownerTagged}' " +
                    "(per-tower table; overrides the element/style mapping).");
                return ownerTagged;
            }

            if (style == BoltStyle.Bolt || AirOnly)
            {
                switch (element)
                {
                    case DamageElement.Flame:  return "ArcherTower-Fire_Projectile";
                    case DamageElement.Ice:    return "ArcherTower-Ice_Projectile";
                    case DamageElement.Aether: return "RangerTowerUpgraded_Projectile";
                    default:
                        // Sky Ballista (airOnly) → ranger base spear (element variants + this
                        // AA path are UNCHANGED — owner-mapped verbatim).
                        if (AirOnly) return "RangerTowerBaseProjectile_Projectile";
                        // Ground Archer, per-tier arrow (owner VfxManualPicks 2026-07, keys named
                        // by tier — the names ARE the mapping): tier 1 red arrow, tier 2 pink arrow,
                        // tier 3 (max/top) the base ArcherTower red-laser bolt.
                        switch (Tier)
                        {
                            case 1:  return "ArcherTowerLevel1_Projectile";
                            case 2:  return "ArcherTowerLevel2_Projectile";
                            default: return "ArcherTower_Projectile";
                        }
                }
            }
            if (style == BoltStyle.Spell)
            {
                switch (element)
                {
                    case DamageElement.Flame: return "FireballTower_Projectile";
                    case DamageElement.Ice:   return "icebasedprojectile_Projectile";
                    default:                  return "ARcaneTower_Projectile";
                }
            }
            // Pellet fallback — still prefer a real Hovl projectile over a bare sphere when possible.
            switch (element)
            {
                case DamageElement.Flame:  return "FireballTower_Projectile";
                case DamageElement.Ice:    return "ArcherTower-Ice_Projectile";
                case DamageElement.Aether: return "ARcaneTower_Projectile";
                default:                   return "ArcherTower_Projectile";
            }
        }

        /// <summary>
        /// OWNER-TAG TABLE: catalog entry id -> the projectile VFX key the owner tagged for
        /// THAT tower. One row per owner tag, mapped verbatim - never a creative substitution.
        /// Returns null when the tower has no tag, which falls through to the element/style
        /// mapping above (the legacy behaviour, byte-for-byte).
        ///
        /// DELIBERATELY ABSENT (do not fill these in without an owner tag):
        ///   tower_siege_tower  (Sky Ballista, range 36, bolt/airOnly) - UNTAGGED. It keeps its
        ///                      existing AirOnly ranger-spear fallthrough. Do NOT borrow the
        ///                      Ballista's or the Archer's pick for it; it awaits an owner tag.
        ///   tower_catapult     (range 28) - authored but not wired to the build menu; the owner
        ///                      intends it as future OFFENSIVE siege content. Wire nothing.
        /// </summary>
        private static string OwnerTaggedProjectileKey(string catalogId)
        {
            switch (catalogId)
            {
                // Ballista (owner 2026-08-04: "Use the SimpleCast projectile for the ballista").
                case "tower_ballista":
                case "tower_wall_wizard": // WO-989 legacy id (alias until saves migrate)
                    return "SimpleCast_Projectile";
                default:                  return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Targeting indicator — a thin aim-beam from the muzzle to the locked
        // target. Cheap (one reused LineRenderer + one emissive material), and
        // readable (faint, element-tinted, slightly transparent so it doesn't
        // clutter the screen when many towers are firing).
        // ─────────────────────────────────────────────────────────────────────
        private void UpdateAimBeam(IDamageable target)
        {
            if (target == null || !target.IsAlive)
            {
                if (_aimBeam != null) _aimBeam.enabled = false;
                return;
            }

            EnsureAimBeam();
            if (_aimBeam == null) return;   // (defensive — EnsureAimBeam always builds it)

            Vector3 muzzle = transform.position + Vector3.up * 2f;
            Vector3 hit    = target.WorldPosition + Vector3.up * 1f;
            _aimBeam.enabled = true;
            _aimBeam.SetPosition(0, muzzle);
            _aimBeam.SetPosition(1, hit);
        }

        /// <summary>
        /// Position-based aim-beam update for the EnemyOwned party-target path
        /// (the party uses IDamageableStructure, which has no WorldPosition).
        /// Mirrors <see cref="UpdateAimBeam"/> exactly but takes an explicit point.
        /// </summary>
        private void UpdateAimBeamAt(Vector3 targetWorldPos, bool hasTarget)
        {
            if (!hasTarget)
            {
                if (_aimBeam != null) _aimBeam.enabled = false;
                return;
            }

            EnsureAimBeam();
            if (_aimBeam == null) return;

            Vector3 muzzle = transform.position + Vector3.up * 2f;
            Vector3 hit    = targetWorldPos + Vector3.up * 1f;
            _aimBeam.enabled = true;
            _aimBeam.SetPosition(0, muzzle);
            _aimBeam.SetPosition(1, hit);
        }

        private void EnsureAimBeam()
        {
            if (_aimBeam != null) return;

            using var _ = FlowTrace.Enter("DefenseTower", "EnsureAimBeam (build lock-on LineRenderer)");
            var beamGo = new GameObject("AimBeam");
            beamGo.transform.SetParent(transform, false);
            _aimBeam = beamGo.AddComponent<LineRenderer>();
            _aimBeam.useWorldSpace   = true;
            _aimBeam.positionCount   = 2;
            _aimBeam.numCapVertices  = 0;
            _aimBeam.alignment       = LineAlignment.View;
            _aimBeam.textureMode     = LineTextureMode.Stretch;
            _aimBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _aimBeam.receiveShadows  = false;

            // Thin, tapered, subtle — a hint of a lock-on laser, not a fat beam.
            _aimBeam.startWidth = 0.05f;
            _aimBeam.endWidth   = 0.02f;

            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                _aimBeamMat = new Material(sh);
                Color tint = BoltColor; tint.a = 0.35f;   // faint, see-through
                if (_aimBeamMat.HasProperty("_BaseColor")) _aimBeamMat.SetColor("_BaseColor", tint);
                if (_aimBeamMat.HasProperty("_Color"))     _aimBeamMat.SetColor("_Color", tint);
                _aimBeam.sharedMaterial = _aimBeamMat;
            }

            Color c = BoltColor; c.a = 0.45f;
            _aimBeam.startColor = c;
            Color e = BoltColor; e.a = 0.15f;   // fades toward the target end
            _aimBeam.endColor = e;

            _aimBeam.enabled = false;   // off until a target is locked
        }

        private void OnDestroy()
        {
            if (_aimBeamMat != null) Destroy(_aimBeamMat);
        }
    }
}
