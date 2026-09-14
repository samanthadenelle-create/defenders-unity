// =============================================================================
// ArcaneTower — WO-113. A buildable MAGIC / AoE defence tower.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Sibling to DefenseTower (the single-target archer/wizard tower) but with a
// distinct ROLE: a slow-firing arcane spire that lobs an arcane pulse at the
// best target and detonates an AoE BLAST on impact — every Hostile inside the
// blast radius takes Aether damage and is SLOWED (the debuff). One shot hits a
// whole cluster, so it trades single-target DPS for crowd control + splash.
//
// SAME PATH as every other catalog structure: registered by a JSON row
// (behaviorId "ArcaneTower"), built by StructureFactory.AttachBehavior, charged
// + placed by BuildModeController, replayed by BaseLayoutLoader. The factory
// copies the RepoProps stat block straight onto the serialized fields below, so
// it stays fully data-tunable (range / damage / fireRate / element come from the
// catalog row; the AoE radius + slow live in the optional aoeRadius / slowSeconds
// repo fields, falling back to the sensible serialized defaults here when 0).
//
// Reuses: IDamageable (DeNelle.Core.Combat) for find + AoE damage + ApplyStatus,
// EnemyBrain.Role for the same backline-first priority as DefenseTower, and the
// shared VFXManager for the cast + blast feel. Targeting mirrors DefenseTower's
// FindObjectsByType<MonoBehaviour> IDamageable scan (works for ground roster AND
// the apex dragon, which implements IDamageable directly) so the arcane tower
// has the same reach the wizard tower does — no WaveManager coupling needed.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// Slow-firing AoE magic tower: pulses an arcane blast that damages + SLOWS
    /// every Hostile in a radius around the impact. Stats are copied off the
    /// catalog RepoProps by StructureFactory; all fields are tunable in-Inspector.
    /// </summary>
    public sealed class ArcaneTower : MonoBehaviour, IDamageableStructure
    {
        [Header("Core combat (set from the catalog RepoProps by StructureFactory)")]
        public float Range     = 22f;
        public float Damage    = 16f;    // damage applied to EVERY enemy in the blast
        public float FireRate  = 0.6f;   // shots per second — intentionally slow (AoE trade-off)
        public bool  CanHitAir = true;   // arcane bolts arc — reach fliers like the wizard tower
        public float AirThreshold = 3.5f;
        public DamageElement Element = DamageElement.Aether;

        [Header("AoE blast (radius + the SLOW debuff — the arcane tower's identity)")]
        [Tooltip("Radius (metres) of the splash detonation around the impact point.")]
        public float AoeRadius = 6f;
        [Tooltip("Seconds of Slow applied to every enemy caught in the blast (0 = no slow).")]
        public float SlowSeconds = 2.5f;
        [Tooltip("Fraction of full Damage dealt to splash victims other than the primary target (0-1).")]
        [Range(0f, 1f)] public float SplashDamageFraction = 0.7f;

        [Header("Look")]
        public Color BlastColor = new Color(0.6f, 0.4f, 1f, 1f);   // arcane violet

        // VFX CHAIN (visual only — gameplay damage stays on <see cref="Element"/>).
        //
        // ⚠ OWNER RULING (WO-870/872 §3.3, applied 2026-08-07): "do NOT ship 'deals Aether, looks
        // Fire'." This tower's gameplay Element is AETHER, so its visuals must be Aether too. It
        // was shipping DamageElement.Flame — a fire bolt, a fire detonation, a fire cast wind-up
        // and a fire swirl — which is exactly the mismatch the ruling forbids, and it survived
        // because TowerProjectileMapRegression gated only the Hovl string key and reported GREEN.
        //
        // The Aether TRAVEL + IMPACT art was already on disk and already mapped the whole time:
        //   ProjectileVFXCatalog: Aether -> Projectile_Arcane, Explosion_Arcane. Both verified
        //   present under Assets/Resources/VFX/Projectiles/.
        //
        // The CAST wind-up and the extra IMPACT SWIRL are now EMPTY, deliberately. The only art of
        // those kinds on disk is Casting_Fire / Casting_Fire_2 / Spell_Fire_6 — there is no
        // Casting_Arcane and no Spell_Arcane (verified by listing the folder, not assumed). Keeping
        // the fire ones would re-commit the very "deals Aether, looks Fire" mismatch this change
        // exists to end, and substituting some other pack effect would be a CREATIVE PICK, which is
        // the owner's call, never an implementer's (memory: vfx-map-owner-tags-no-creative-pick).
        // So the tower fires a real arcane bolt with a real arcane detonation and simply has no
        // wind-up flourish until the owner tags Aether art for those two hooks.
        [Header("VFX (visual only)")]
        [Tooltip("Element for travel + base impact burst (Explosion_*). MUST match the gameplay Element.")]
        public DamageElement BoltVisualElement = DamageElement.Aether;
        [Tooltip("Resources/VFX/Projectiles/<name> cast wind-up. EMPTY until Aether cast art is tagged.")]
        public string BoltCastVfx = "";
        [Tooltip("Resources/VFX/Projectiles/<name> extra AoE detonation. EMPTY until Aether swirl art is tagged.")]
        public string BoltImpactExtraVfx = "";

        // Elevation perk (wall-mounted): a spire seated on a wall-walk TOP gets the high-ground
        // range/LOS bonus. 1 = ground (no bonus); set by BaseLayoutLoader.Spawn (e.g. 1.25) when
        // wall-mounted. A MULTIPLIER on EffectiveRange so it survives tier upgrades. Bounded.
        public float ElevationRangeMult = 1f;

        // ── Tower-VFX tier (owner felt-test 2026-07-17: "more/better VFX at higher tower levels") ──
        // The arcane spire has no runtime level field (its stats come from Modifier/perk services,
        // not a per-instance level), so StructureFactory.ReskinForLevel pushes the upgrade level in
        // here via SetVfxLevel. Scales the cast + detonation bursts so an upgraded ornate spire's
        // shot reads as stronger. 1 until first upgrade. Reads by SIZE + an L3 extra blast layer
        // (colorblind-safe, never hue). The idle aura escalates in parallel via ArcaneAura.ApplyLevel.
        private int _vfxLevel = 1;

        /// <summary>Set the spire's firing-VFX tier (1..3). Called by StructureFactory.ReskinForLevel
        /// on placement/upgrade. Bigger cast + impact bursts at higher tiers.</summary>
        public void SetVfxLevel(int level) => _vfxLevel = Mathf.Clamp(level, 1, 3);

        /// <summary>Uniform Hovl VFX scale for the current firing tier (L1 1.0, L2 1.3, L3 1.7).</summary>
        private float VfxScale => _vfxLevel >= 3 ? 1.7f : _vfxLevel == 2 ? 1.3f : 1.0f;

        // ── IDamageableStructure — the marching-enemy siege target (F8-41) ─────
        // ROOT of F8-41 (DefenseTargetableRegression): like DefenseTower, this component did NOT
        // implement IDamageableStructure, so Enemy.SweepForNearestStructure's
        // collider.GetComponentInParent<IDamageableStructure>() returned null for the spire —
        // enemies marched straight past it. Implementing the interface (mirrors WallSegment / Gate)
        // makes the arcane spire a real siege target.
        //
        // ⚠ "The arcane tower is always player-owned" USED TO BE ASSERTED HERE and was retired by
        // WO-1439 (2026-09-06). It was an ASSUMPTION about placement, not a fact about the class:
        // nothing stops a baked enemy-owned scene carrying one, and that assumption is the same
        // species of missing-ownership thinking that let a raid garrison spend a whole raid
        // destroying its own RaidSpire. Ownership is now DERIVED from SceneOwnership (see Faction
        // below), which answers correctly in both scenes instead of being right by habit.
        //
        // HP: no per-entry hp is authored in structures-catalog.json / RepoProps, so this is a
        // serialized default. 160 is sturdier than a wall (WallSegment's 0-100 track) but a touch
        // squishier than the single-target DefenseTower (200) — the AoE spire reads as the softer,
        // higher-value backline target. Tunable.
        [Header("Durability (IDamageableStructure — enemy siege target)")]
        [Tooltip("Max HP. Enemies deal contact damage to the spire they path to. No catalog hp is " +
                 "authored; this default (160) is sturdier than a wall, slightly softer than DefenseTower (200).")]
        [SerializeField, Min(10f)] private float _maxHp = 160f;
        private float _hp = -1f;   // <0 = not yet initialised; set to _maxHp on first use / Awake

        // WO-672 Slice A (owner rulings F8-39 "either they exist or do not" + F8-42
        // broken = inoperable until repaired): at 0 HP the spire BREAKS instead of
        // Destroy(gameObject)ing — an inoperable in-world shell until Repair().
        // Mirrors the ResourceCollector Broken model.
        private bool _broken;

        /// <summary>True once enemies broke this spire (hp 0) — inoperable until <see cref="Repair"/>. (WO-672)</summary>
        public bool IsBroken => _broken;

        /// <summary>Health 0..1 — the wave damage-report fraction (WO-672; mirrors ResourceCollector.HpFraction).</summary>
        public float HpFraction => _maxHp > 0f ? Mathf.Clamp01(Hp / _maxHp) : 0f;

        /// <summary>Max HP (WO-761: lets StructureBurn size a percent-of-max fire tick).</summary>
        public float MaxHp => _maxHp;

        /// <summary>Fired once when enemies destroy this spire (HP reaches 0). Observers
        /// (persistence / target-release) can subscribe. WO-672: fires at the BREAK moment
        /// (the spire persists as an inoperable shell) — listeners release targets exactly as
        /// before. WO-753 (owner 2026-07-19): a destroyed spire is NOT auto-re-placed in-world;
        /// it returns ONLY via full-cost build-mode placement — no in-place respawn observer.</summary>
        public event System.Action<ArcaneTower> Destroyed;

        /// <summary>Current HP (lazy-initialised to <see cref="_maxHp"/>).</summary>
        private float Hp
        {
            get { if (_hp < 0f) _hp = _maxHp; return _hp; }
        }

        /// <summary><see cref="IDamageableStructure"/> — true while the spire still stands
        /// (hp &gt; 0 and not broken, WO-672).</summary>
        public bool IsAlive => Hp > 0f && !_broken;

        /// <summary>
        /// WO-1439 — DERIVED from scene ownership, same expression as WallSegment/Gate/Building/Tower.
        /// One answer to "whose is this?", never a per-class invention.
        /// </summary>
        public CombatFaction Faction =>
            SceneOwnership.IsEnemyOwned ? CombatFaction.Hostile : CombatFaction.Friendly;

        /// <summary>
        /// WO-672 (F8-42): full restore — HP back to max, broken cleared; the Update fire
        /// loop resumes on its own (it early-outs only while <see cref="_broken"/>). Cost
        /// enforcement lives with the caller, mirroring ResourceCollector.Repair.
        /// </summary>
        public void Repair()
        {
            // WO-753 ruling (owner 2026-07-19, SUPERSEDES WO-672's repair-back-online): a DESTROYED
            // spire is LOST - it returns ONLY via a full-cost build-mode placement, never an in-place
            // repair. Mirrors the guard Building.Repair already carries.
            if (_broken) return;
            _hp = _maxHp;
            FlowTrace.Step("Structure", $"'{name}' REPAIRED (hp {_maxHp:0})");
        }

        /// <summary>
        /// <see cref="IDamageableStructure"/> contact-attack entry point — a Hollow One in melee
        /// contact routes its hit here (the SAME seam WallSegment / Gate / the Heart use). Reduces
        /// HP; at zero the spire is destroyed and <see cref="Destroyed"/> fires. Traces the hit +
        /// the kill (§12).
        /// </summary>
        public void ApplyContactDamage(float amount)
        {
            if (amount <= 0f || Hp <= 0f) return;

            _hp = Hp - amount;
            FlowTrace.Throttle("ArcaneTower", $"hurt:{GetInstanceID()}", 1f,
                $"'{name}' took {amount:0.#} contact dmg -> HP {_hp:0.#}/{_maxHp:0.#} (enemy siege).");

            if (_hp <= 0f)
            {
                _hp = 0f;
                _broken = true;
                // WO-672 Slice A: no Destroy(gameObject) — the spire persists as an
                // inoperable shell ("either they exist or do not", F8-39) until Repair().
                FlowTrace.Step("Structure", $"'{name}' BROKE (hp 0) — inoperable until repaired");
                // WO-753 (owner 2026-07-19 destroyed-items-...-vfx-cleanup): tear this spire's VFX
                // down WITH it, synchronously, through the ONE-owner Destructible - the persistent
                // Arcane_Aura loop (and any other held effect) is pool-returned in one place instead
                // of looping over the dead shell (the ROOT "i see a vfx but no tower" orphan). Because
                // the root stays active on a broken shell, no Unity lifecycle event fires, so this
                // explicit teardown is the guarantee. Repair re-enables the aura (symmetric).
                Destructible.For(gameObject)?.NotifyBroken("ArcaneTower hp0");
                Destroyed?.Invoke(this);
            }
        }

        private void Awake()
        {
            if (_hp < 0f) _hp = _maxHp;
            EnsureContactCollider();
            // Owner 2026-07-15 "arcane towers should have an aura" - a persistent magic-circle
            // aura loop (colorblind-safe: motion/luminance, not hue). Idempotent + self-managing.
            // Owner 2026-07-24: the combat Arcane Spire gets its OWN subtle, DISTINCT aura
            // ("Aura_HeartPulse" — gentle pulse) so it no longer shares the one "Magic circle sun
            // loop" prefab with the harvest nodes + the Cathedral of Magic. SWAPPABLE default.
            //
            // ⭐ WO-1346 - THE OWNER-TAGGED KEY ARRIVED, AND THIS IS THE TODO IT CLOSES.
            // The 2026-07-24 note that used to sit here said, verbatim: "a DEDICATED
            // owner-tagged arcane-tower-aura key is not yet tagged in the VFX Caster. When she
            // supplies it, wire a persistent striking loop here through the ONE pool ... Do NOT
            // pick a key here." She has now supplied it. Assets/Editor/VfxManualPicks.json:
            //     ArcaneTower_Aura -> Lana Studio/Casual RPG VFX/Prefabs/Fog/Fog_electric.prefab
            //     isLoop true, scale 1.0    her words: "arcane tower vfx (after built) softly"
            // The key is mapped VERBATIM. Nothing here picks, substitutes or rescales a prefab
            // (memory vfx-map-owner-tags-no-creative-pick), and Fog_electric.prefab is NOT
            // modified on disk - it is a shared pack asset.
            //
            // ⛔ THIS REPLACES "Aura_HeartPulse" ON THIS TOWER - IT DOES NOT JOIN IT. Two ambient
            // auras on one structure is "one owner, one lifecycle" broken (CLAUDE.md section 7)
            // and reads as a muddy double effect that no amount of "softly" fixes. The single
            // ArcaneAura component is retargeted rather than a second component added, which is
            // also what keeps the destruction teardown, the orphan guard, the max-level crown and
            // the per-level escalation working unchanged - and what stops
            // StructureFactory.ReskinForLevel's ArcaneAura.EscalateTo(ensure:true) from
            // re-Ensuring a SECOND aura at the next upgrade.
            //
            // "(after built)": requireBuilt gates the aura on UnderConstructionVisual's derived
            // built state, so it does not play on a scaffold or while the Obsidian job is in
            // flight, and - the case most likely to be missed - it IS present on a reload of an
            // already-built tower, because the gate reads STATE and not the completion event.
            // Teardown on destruction is already owned by Destructible.TeardownVfx below.
            //
            // "softly": an instance-level emission-density dial, defaulted subdued and readable
            // from a database row (ArcaneTowerAuraTuning). Density and not hue - the owner is
            // red/green colourblind.
            //
            // ⚠ THE KEY IS PASSED AS A STRING LITERAL ON PURPOSE. VfxAuraDifferentiationRegression
            // source-lints it back out of this file with the regex
            // ArcaneAura\.Ensure\(\s*gameObject\s*,\s*"([^"]+)" - a const would read as a missing
            // spire aura key and red the build gate.
            ArcaneAura.Ensure(gameObject, "ArcaneTower_Aura",
                requireBuilt: true,
                softEmissionMul: ArcaneTowerAuraTuning.SoftEmissionMul());
            // WO-753: compose the ONE-owner VFX-teardown lifecycle so the aura (and any held effect)
            // is torn down WITH the spire on break / destroy — in one place, no orphans.
            Destructible.Ensure(gameObject);
        }

        /// <summary>
        /// Guarantees a NON-TRIGGER collider exists so the enemy sweep's
        /// Physics.OverlapSphere(..., QueryTriggerInteraction.Ignore) can actually RETURN this
        /// spire. Idempotent: skips if a solid collider already exists in the hierarchy (the
        /// skinned visual usually carries one). Sized from the visual's renderer bounds — mirrors
        /// Tower.EnsureBodyCollider (DEF-74). The IDamageableStructure lives on this root, so
        /// GetComponentInParent from any child collider resolves it.
        /// </summary>
        private void EnsureContactCollider()
        {
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c != null && !c.isTrigger) return;   // already hittable by the sweep

            float height = 4.5f, radius = 0.9f;
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                height = Mathf.Max(1f, b.size.y);
                radius = Mathf.Max(0.4f, Mathf.Max(b.size.x, b.size.z) * 0.5f);
            }

            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.isTrigger = false;
            cap.height = height;
            cap.radius = radius;
            cap.center = new Vector3(0f, height * 0.5f, 0f);
        }

        private float _cd;
        private float _scan;
        private readonly List<IDamageable> _hostiles = new List<IDamageable>();

        // WO-430 — the Arcane Tower upgrade buffs ITS OWN damage/range (towerDamageMult /
        // towerRangeMult). Always player-owned, so the perk always applies. LIVE-READ.
        //
        // WO-676 (BULWARK talents) — the hero's strategic tree is read at this SAME choke
        // point: Keen Ballistics (towerDamage, fractional), Farsight Emplacements (towerRange,
        // flat metres), Standing Orders (towerAttackSpeed, fractional). Sums refresh on the
        // existing 0.4s Rescan tick via HeroTalentModifiers.StatSum (the Σ-registry pattern
        // HeroHealth.TakeDamage consumes). Σ=0 → identity, byte-identical baseline. The spire
        // is ALWAYS player-owned (no EnemyOwned variant), so no allegiance gate is needed.
        private float EffectiveDamage => Damage * DeNelle.Core.State.ModifierService.Active.TowerDamageMult * _talentDamageMult;
        private float EffectiveRange  => (Range * DeNelle.Core.State.ModifierService.Active.TowerRangeMult + _talentRangeAdd) * ElevationRangeMult;
        private float EffectiveFireRate => FireRate * _talentFireRateMult;

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

        /// <summary>WO-676 — one Σ-registry read per BULWARK type at the spire's existing stat
        /// seam (called from the 0.4s Rescan tick). Zero unlocked nodes → identity (1/0/1).</summary>
        private void RefreshTalentSums()
        {
            string heroClass = ActiveHeroClass();
            float dmg  = Talents.HeroTalentModifiers.StatSum(heroClass, "towerDamage");
            float rng  = Talents.HeroTalentModifiers.StatSum(heroClass, "towerRange");
            float rate = Talents.HeroTalentModifiers.StatSum(heroClass, "towerAttackSpeed");
            _talentDamageMult   = 1f + Mathf.Max(0f, dmg);
            _talentRangeAdd     = Mathf.Max(0f, rng);
            _talentFireRateMult = 1f + Mathf.Max(0f, rate);

            if (dmg > 0f)  FlowTrace.Once("ArcaneTower", "talent-towerDamage",
                $"BULWARK towerDamage applied to the arcane spire: +{dmg:P0} (Keen Ballistics).");
            if (rng > 0f)  FlowTrace.Once("ArcaneTower", "talent-towerRange",
                $"BULWARK towerRange applied to the arcane spire: +{rng:0.#}m (Farsight Emplacements).");
            if (rate > 0f) FlowTrace.Once("ArcaneTower", "talent-towerAttackSpeed",
                $"BULWARK towerAttackSpeed applied to the arcane spire: +{rate:P0} fire rate (Standing Orders).");
        }

        private void Update()
        {
            // WO-672 Slice C: a broken spire is INOPERABLE until repaired — no scan,
            // no acquire, no blast. Repair() clears the flag; the loop resumes next frame.
            if (_broken) return;

            _scan -= Time.deltaTime;
            if (_scan <= 0f) { Rescan(); _scan = 0.4f; }

            _cd -= Time.deltaTime;
            if (_cd > 0f) return;

            var target = Acquire();
            if (target == null) return;

            // WO-676: Standing Orders (towerAttackSpeed) folds into the fire cadence.
            _cd = 1f / Mathf.Max(0.1f, EffectiveFireRate);
            FireBlast(target);
        }

        /// <summary>
        /// WO-1524 — the side this scan strikes FROM, handed to <see cref="CombatFactionRules"/>
        /// so friend-or-foe is answered by the ONE authority rather than an inline
        /// <c>Faction != CombatFaction.Hostile</c> copy (forbidden by that file's header).
        ///
        /// ⚠ A LITERAL, NOT <see cref="Faction"/>, AND THAT ASYMMETRY IS A FINDING, NOT AN
        /// OVERSIGHT. <see cref="Faction"/> is derived from <c>SceneOwnership</c>, so an
        /// ENEMY-OWNED spire reports Hostile — yet this scan has always admitted only Hostile
        /// bodies, i.e. a garrison spire acquires ITS OWN SIDE. That is the WO-1439 defect
        /// class verbatim. Passing <see cref="Faction"/> here would fix it AND change
        /// behaviour, which WO-1524 explicitly forbids ("no behaviour change intended — if any
        /// site changes behaviour, that is a finding to report, not to absorb silently").
        /// Reported, not absorbed; the literal preserves today's behaviour exactly, since
        /// <see cref="CombatFaction"/> has only two members (IDamageable.cs:28-34) and so
        /// <c>IsFriendlyFire(Friendly, t)</c> ≡ <c>t.Faction != Hostile</c>.
        /// </summary>
        private const CombatFaction ScanSide = CombatFaction.Friendly;

        private void Rescan()
        {
            // WO-676: refresh the BULWARK talent sums on the same 0.4s cadence as the
            // target scan (never per frame).
            RefreshTalentSums();

            // PERF (overworld 1fps fix): mirrors DefenseTower. The old scan was
            // FindObjectsByType<MonoBehaviour>, which enumerates EVERY MonoBehaviour in ALL
            // loaded scenes (the additive overworld = tens of thousands) every 0.4s tick,
            // per tower — hundreds of ms/frame independent of enemy count. Scan only the two
            // CONCRETE hostile IDamageable implementors instead (engine-filtered to the live
            // enemy bodies + the dragon). Identical target set; no full-scene enumeration.
            _hostiles.Clear();
            foreach (var d in FindObjectsByType<EnemyDamageable>())
            {
                // WO-1524: the ONE authority answers "is this one of ours?" — skip if so.
                // IsFriendlyFire (not MayAttack) is deliberate: it is liveness-BLIND, exactly
                // like the inline copy it replaces, so the scan set is unchanged. Acquire()
                // below still owns the IsAlive filter.
                if (d == null || CombatFactionRules.IsFriendlyFire(ScanSide, d)) continue;
                // TOWERS DEFEND THE TOWN AUTONOMOUSLY (owner 2026-06-28, mirrors DefenseTower):
                // roaming encounter reps (RepEngageWatcher) are no longer skipped — they are now
                // killable (Hp=150) and tower damage does NOT trigger the arena
                // (RangedHitsEngage=false; only near-CONTACT with the HERO pops the battle). So
                // the arcane spire SHOULD blast + kill reps in range as automated town defense;
                // the arena still fires only when a rep engages the hero. No skip — every hostile
                // faction in range is acquired.
                _hostiles.Add(d);
            }
            foreach (var d in FindObjectsByType<DragonBoss>())
                // WO-1524: same authority, same liveness-blind shape as the sweep above.
                // DragonBoss implements IDamageable only (DragonBoss.cs:158), never
                // IDamageableStructure, so this overload resolves unambiguously.
                if (d != null && !CombatFactionRules.IsFriendlyFire(ScanSide, d))
                    _hostiles.Add(d);
        }

        // Same backline-first priority as DefenseTower (healers > ranged/dps > rest > tanks).
        private IDamageable Acquire()
        {
            IDamageable best = null;
            int   bestPri = int.MaxValue;
            float bestSqr = float.MaxValue;
            float range = EffectiveRange;   // WO-430 — Arcane Tower range perk
            foreach (var d in _hostiles)
            {
                if (d == null || !d.IsAlive) continue;
                Vector3 p = d.WorldPosition;
                float sqr = (p - transform.position).sqrMagnitude;
                if (sqr > range * range) continue;
                if (p.y > AirThreshold && !CanHitAir) continue;
                // LoS gate ("towers shoot through walls" fix, owner 2026-07) — a wall on the
                // "Structure" layer between the spire muzzle and the target blocks the shot.
                // Mirrors TowerCombat.BlockedByWall (flyer-exempt, degrade-open). ArcaneTower's
                // Acquire had NO LoS check, so it lobbed blasts through every perimeter wall.
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
        // spire muzzle and the target. DEGRADE OPEN — no Structure layer (mask 0) → never block.
        // FLYER EXEMPTION — a flier is engaged from above, a ground wall does not block the arcing
        // lob (same exemption TowerCombat uses). Muzzle matches FireBlast()'s `up*2.5`.
        private int _structureMask = -1;
        private bool BlockedByWall(IDamageable target)
        {
            if (target == null) return true;
            if (target is ICombatLayered layered && layered.Layer == CombatLayer.Flying) return false;
            if (_structureMask < 0) _structureMask = LayerMask.GetMask("Structure");
            if (_structureMask == 0) return false;
            Vector3 fPos = transform.position + Vector3.up * 2.5f;
            Vector3 tPos = target.WorldPosition;
            // WO-1720 — LoS DECISION POINT. Capture the hit collider so a future "Arcane spire
            // lobs through a standing wall" capture is provable from ONE log read (pair
            // blocked=false with fPos/tPos Y against the known WallSegment collider-vs-renderer
            // height gap; WO-1719 measured colliderBounds 3m vs rendererBounds 15m on an intact wall).
            bool blocked = Physics.Linecast(fPos, tPos, out RaycastHit losHit, _structureMask, QueryTriggerInteraction.Ignore);
            if (FlowTrace.Enabled)
            {
                FlowTrace.Throttle("TowerLoS", $"ArcaneTower:{GetInstanceID()}", 1f,
                    $"'{name}' BlockedByWall fPos={fPos} tPos={tPos} blocked={blocked}" +
                    (blocked && losHit.collider != null
                        ? $" hit='{losHit.collider.name}' hitColliderBoundsY=[{losHit.collider.bounds.min.y:F2}..{losHit.collider.bounds.max.y:F2}] hitPoint={losHit.point}"
                        : " (no Structure collider on the line — if a wall is visually there, its collider is undersized/absent)"));
            }
            return blocked;
        }

        private static int Priority(IDamageable d)
        {
            var mb = d as MonoBehaviour;
            var brain = mb != null ? mb.GetComponent<EnemyBrain>() : null;
            if (brain == null) return 2;
            switch (brain.Role)
            {
                case EnemyRole.Healer:   return 0;
                case EnemyRole.Ranged:   return 1;
                case EnemyRole.DPS:      return 1;
                case EnemyRole.MiniBoss: return 2;
                case EnemyRole.Tank:     return 3;
                default:                 return 2;
            }
        }

        /// <summary>
        /// Detonates an arcane blast centred on <paramref name="primary"/>: the
        /// primary takes full <see cref="Damage"/>; every OTHER live Hostile within
        /// <see cref="AoeRadius"/> of the impact takes a splash fraction and all
        /// affected enemies are SLOWED for <see cref="SlowSeconds"/>.
        /// </summary>
        private void FireBlast(IDamageable primary)
        {
            // Hot loop (slow-firing, but still per-shot): Throttle entry so a live tower is
            // pinpointed in a capture without flooding the break-log.
            FlowTrace.Throttle("ArcaneTower", $"blast:{GetInstanceID()}", 1f,
                $"FireBlast (radius={AoeRadius}, dmg={EffectiveDamage:0.#}, slow={SlowSeconds}s).");
            Vector3 muzzle  = transform.position + Vector3.up * 2.5f;
            Vector3 impact  = primary.WorldPosition;

            // ── SPELL CAST (Casting_Fire_2 ember gather at spire top) ──────────
            ProjectileVFXCatalog.SpawnNamedOneShot(muzzle, BoltCastVfx);

            // WO-VFX-TOWERS: Hovl arcane cast burst at the muzzle, layered on top of the
            // legacy Casting_Fire_2 (null-safe no-op if the key/prefab is missing). Reads by
            // MOTION (violet gather-and-flash) so it's colorblind-legible; BlastColor is a hint.
            // TIER ESCALATION: scale the cast by the upgrade tier so a maxed spire winds up bigger.
            // Owner VfxManualPicks: SimpleCast_Cast (muzzle). The TRAVEL hook is HELD awaiting an
            // Aether-matching tag (owner 2026-08-04 "Aether wins") - see the block at FireBlast.
            Guard.Try("TowerVfx", "arcane muzzle cast", () =>
                VFXManager.PlayKey("SimpleCast_Cast", muzzle, default, null, BlastColor, VfxScale));
            FlowTrace.Throttle("TowerVfx", $"arcane-fire:{GetInstanceID()}", 1f,
                $"arcane spire level={_vfxLevel} fire cast='SimpleCast_Cast' scale={VfxScale:0.0}");

            // Flying body: Projectile_Fire_3 (hero fireball bolt) via SpawnFlying(Flame).
            // URP heal runs at spawn (FixUrpShaders).
            // Fall back to emissive orb if the mirrored Resources prefab is missing.
            GameObject bolt = null;
            Guard.Try("ArcaneTower", "spawn spell bolt", () =>
            {
                bolt = new GameObject("ArcaneSpellBolt");
                bolt.transform.position = muzzle;

                var fx = ProjectileVFXCatalog.SpawnFlying(bolt.transform, BoltVisualElement);
                if (fx == null)
                {
                    FlowTrace.Warn("ArcaneTower",
                        $"FireBlast: SpawnFlying({BoltVisualElement}) unresolved — using fallback emissive orb.");
                    var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    orb.name = "ArcaneSpellOrb";
                    orb.transform.SetParent(bolt.transform, false);
                    orb.transform.localScale = Vector3.one * 0.55f;
                    var col = orb.GetComponent<Collider>(); if (col != null) Destroy(col);
                    var sh = Shader.Find("Universal Render Pipeline/Lit");
                    if (sh != null)
                    {
                        var m = new Material(sh);
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", BlastColor);
                        if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", BlastColor * 4f); }
                        var r = orb.GetComponent<Renderer>(); if (r != null) r.sharedMaterial = m;
                    }
                }

                // WO-VFX-TOWERS: a Hovl arcane bolt (loop key) FOLLOWS the mover transform, so the
                // travelling shot reads as an arcane projectile in motion (colorblind-legible by its
                // TRAIL/MOTION, not tint). Loop keys return a VFXHandle — Stop() it on arrival so the
                // trail doesn't linger after detonation. Null-safe if the key/prefab is missing.
                // -- OWNER RULING 2026-08-04, FINAL: this hook is HELD, awaiting a tag ---------
                // The ruling history matters, because it is the reason this deliberately spawns
                // NO travelling effect rather than falling back to something:
                //   1. Earlier tonight: "fireball can go from arcane tower" -> FireballTower_Projectile
                //      was wired here for every tier, retiring the old two-rung ladder.
                //   2. Then WO-872's audit found this tower deals AETHER damage but RENDERS FIRE,
                //      and ruled the visual must match the damage. An orange fireball is exactly
                //      the Fire visual that ruling exists to remove, so the two rulings asked
                //      opposite things of this one tower.
                //   3. Owner resolved it: "Aether wins, retire the fireball mapping, and use
                //      fireball in casting magic from DPS mages."
                //
                // So tower_arcane_spire now has NO tagged projectile: its element is Aether and no
                // Aether-looking pick is tagged for it. Per the standing VFX law the owner tags the
                // key and the CLI maps it VERBATIM - an untagged hook is HELD, never filled with
                // whatever looks aetheric. That explicitly rules out Arcane_Projectile and the two
                // retired ARcaneTower_Projectile / ArcaneTower-Baselevel_Projectile rows.
                //
                // The fireball rows are NOT dead - they are REASSIGNED to the hero magic-cast lane
                // (WO-875). They stay authored in VfxManualPicks.json + the catalog; this file just
                // stops referencing them. Do not "restore" any of it here.
                //
                // The shot still reads: the code-built emissive orb above flies, and the pooled
                // Impact_ExplosionAether detonation lands in ApplyBlast. Only the travelling trail
                // is absent, and it lights up with no code change the moment a key is tagged.
                string travelKey = null;   // HELD - see the block above. Do NOT substitute a pick.
                if (string.IsNullOrEmpty(travelKey))
                    FlowTrace.Once("TowerVfx", "arcane-travel-untagged",
                        "ArcaneTower travelling-projectile hook is HELD: tower_arcane_spire deals Aether " +
                        "but has NO owner-tagged Aether projectile (owner 2026-08-04 'Aether wins, retire " +
                        "the fireball mapping'). No effect is substituted by design - tag a key in the " +
                        "VfxCaster and this lights up with no code change. Orb + Aether detonation still play.");
                var boltFx = !string.IsNullOrEmpty(travelKey)
                    ? VFXManager.PlayKey(travelKey, muzzle, default, null, BlastColor, 0f, 0f, bolt.transform)
                    : null;

                // Arcing lob; blast applies ON ARRIVAL (the un-pooled mover self-destroys, taking
                // its visual child with it). The AoE blast VFX (pooled Impact_ExplosionAether) +
                // damage/slow land in ApplyBlast, so the shot reads as a cast spell. Stop the
                // following bolt trail as the shot arrives, then ApplyBlast plays the Hovl impact.
                // WO-VFX #3: the trail finishes instead of popping — and WO-1155 binds that
                // StopSoft as the mover's RELEASE (fired by arrival OR teardown), so a bolt
                // destroyed in flight cannot strand the loop slot. travelKey is HELD today, so
                // boltFx is null and this costs nothing; it is wired now so the hook lights up
                // leak-free the moment the owner tags a key.
                bolt.AddComponent<ProjectileMover>().Launch(impact + Vector3.up * 0.5f, 26f, 0.35f,
                    () => ApplyBlast(primary, impact),
                    () => boltFx?.StopSoft());
            });

            if (bolt == null)
            {
                // Bolt spawn threw — never let the spire go silently dead:
                // fall back to the legacy instant blast (Warn per INSTRUMENTATION_STANDARD).
                FlowTrace.Warn("ArcaneTower",
                    "FireBlast: spell-bolt spawn failed — applying blast instantly (visual-less fallback).");
                ApplyBlast(primary, impact);
            }
        }

        /// <summary>
        /// Detonate the blast at <paramref name="impact"/>: explosion VFX + full damage to
        /// the primary, splash + Slow to every other live Hostile in <see cref="AoeRadius"/>.
        /// Called on spell-orb ARRIVAL (or instantly by the fallback path above).
        /// </summary>
        private void ApplyBlast(IDamageable primary, Vector3 impact)
        {
            if (this == null) return;   // tower destroyed while the orb was in flight

            ProjectileVFXCatalog.SpawnImpact(impact, BoltVisualElement);
            ProjectileVFXCatalog.SpawnNamedOneShot(impact, BoltImpactExtraVfx);

            // WO-VFX-TOWERS: Hovl catalog detonation burst at the impact point, layered on top of
            // the legacy explosion (null-safe no-op on a missing key). The following-bolt trail is
            // stopped by the arrival closure in FireBlast before this runs.
            // Owner pick (2026-07): the arcane spire's impact-on-target burst is the Unity Particle
            // Pack PlasmaExplosionEffect (catalog key PP_PlasmaExplosionEffect) — a one-shot plasma
            // detonation, tinted to BlastColor (Recolorable) so it reads arcane-violet. Requires the
            // catalog row to be IsLoop:false (a looping impact never auto-returns → leaks the pool).
            // TIER ESCALATION: scale the detonation by tier, and stack a heavier Cleave blast at L3
            // so an upgraded spire's hits land harder (reads by SIZE + extra layer, colorblind-safe).
            Guard.Try("TowerVfx", "arcane impact", () =>
                VFXManager.PlayKey("PP_PlasmaExplosionEffect", impact, default, null, BlastColor, VfxScale));
            if (_vfxLevel >= 3)
                Guard.Try("TowerVfx", "arcane impact L3 cleave", () =>
                    VFXManager.PlayKey("Cleave_Impact", impact, default, null, BlastColor, 0.9f));

            float aoeSq = AoeRadius * AoeRadius;
            float splash = Mathf.Clamp01(SplashDamageFraction);

            // The scan list was refreshed in Update; reuse it for the splash sweep so we don't
            // pay a second FindObjectsByType this frame. GUARD EACH victim independently: one
            // bad enemy (a destroyed body mid-iteration, a thrown TakeDamage/ApplyStatus) is
            // logged + skipped, never aborting the splash for the rest of the cluster (a silent
            // half-applied blast would read as "the AoE didn't hit everyone").
            int affected = 0;
            Guard.TryEach("ArcaneTower", "apply blast to enemy", _hostiles, d =>
            {
                if (d == null || !d.IsAlive) return;

                bool isPrimary = ReferenceEquals(d, primary);
                if (!isPrimary && (d.WorldPosition - impact).sqrMagnitude > aoeSq) return;

                float ed  = EffectiveDamage;   // WO-430 — Arcane Tower damage perk
                float dmg = isPrimary ? ed : ed * splash;
                d.TakeDamage(dmg, Element);

                if (SlowSeconds > 0f)
                    d.ApplyStatus(StatusEffect.Slow, SlowSeconds);
                affected++;
            });

            if (affected == 0)
            {
                // The primary resolved but nothing took the blast — the cluster vanished between
                // Acquire and detonation, or every victim was already dead. Self-report (not a
                // hard fail): the shot fired but landed on no one.
                // Fleet NRE 4/4 (break-log run0 t=60.8, ApplyBlast:249): `?.` does not see Unity's
                // fake-null — a DESTROYED primary passes and .name throws on the dead native
                // object. Explicit Unity-null check (project rule: no ?. on UnityEngine.Object).
                var pmb = primary as MonoBehaviour;
                string pname = pmb != null ? pmb.name : "<primary destroyed>";
                FlowTrace.Warn("ArcaneTower",
                    $"FireBlast: 0 enemies affected (primary='{pname}') — cluster gone/all dead at detonation.");
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, Range);
            Gizmos.color = new Color(0.8f, 0.5f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, AoeRadius);
        }
    }
}
