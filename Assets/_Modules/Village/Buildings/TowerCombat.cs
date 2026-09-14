// =============================================================================
// TowerCombat — WO-82. Auto-fire: a built tower acquires the nearest live enemy
// in range and fires a pooled projectile at it, scaling with the tower's level.
// -----------------------------------------------------------------------------
// Reconciled to this project (per WO-82 key reconciliations):
//   • Targets via WaveManager.LiveEnemies (zero-GC, authoritative) — NOT
//     OverlapSphere; WaveManager is not a singleton here, so we cache a ref.
//   • Each live Enemy's IDamageable is its EnemyDamageable adapter; validity via
//     IsAlive, faction via Faction (enemies are Hostile), position via WorldPosition.
//   • Range/damage come from the tower's per-level TowerData (CurrentRange /
//     CurrentDamage); fire rate speeds up with level (cooldown / CurrentLevel).
//   • Damage is dealt by the pooled projectile via TakeDamage(amount, DamageElement).
//   • Detection is throttled (only runs on the fire tick / a short idle re-scan),
//     never an OverlapSphere every frame.
// Added at runtime by Tower.EnsureCombat once the tower is built; the FirePoint
// child it creates is resolved here in Awake.
// =============================================================================

using System.Collections;
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Data;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>Per-tower auto-fire controller. Attached by Tower once built.</summary>
    [RequireComponent(typeof(Tower))]
    public sealed class TowerCombat : MonoBehaviour
    {
        [Header("Combat (level scales range/damage via TowerData; fire rate via level)")]
        [SerializeField] private float _baseCooldown  = 1.1f;   // seconds between shots at level 1
        [SerializeField] private float _idleRescan     = 0.2f;  // re-scan cadence when no target
        [SerializeField] private float _fallbackRange  = 12f;   // used only if TowerData has no range
        [SerializeField] private float _fallbackDamage = 22f;   // used only if TowerData has no damage

        private Tower _tower;
        private Transform _firePoint;
        private int _structureMask = -1;   // lazily-resolved "Structure" LoS mask (0 = layer absent -> degrade open)
        private WaveManager _wave;
        private float _nextAttackTime;

        // ── Empowerment state ────────────────────────────────────────────────

        private EmpowermentAbility _empowerment = EmpowermentAbility.None;
        private bool _isEmpowered;

        // ManaSurge: count shots; every 5th fires a 3-bolt burst.
        private int _shotsSinceLastBurst;
        private const int ManaSurgeBurstInterval = 5;
        private const float ManaSurgeBurstDamageMult = 0.6f;

        // GlacialCore: pulse AoE slow on all enemies in range.
        private const float GlacialPulseInterval = 2.5f;   // seconds between pulses
        private const float GlacialSlowDuration  = 3.0f;   // how long each slow lasts
        private const float GlacialAngle         = 120f;    // not used (full circle)

        // EternalEmber: burn DoT on every hit.
        private const float EternalEmberBurnDps      = 4f;  // damage per second
        private const float EternalEmberBurnDuration = 4f;  // seconds

        private void Awake()
        {
            _tower = GetComponent<Tower>();
            _firePoint = transform.Find("FirePoint");   // created by Tower.EnsureCombat
            if (_firePoint == null) _firePoint = transform;
            ResolveWave();
        }

        private void ResolveWave()
        {
            var found = FindObjectsByType<WaveManager>();
            _wave = found.Length > 0 ? found[0] : null;
        }

        // ── Empowerment public entry point ────────────────────────────────────

        /// <summary>
        /// Called by <see cref="Tower.TryEmpower"/> after the crystal cost is deducted.
        /// Stores the ability and starts any persistent loops (GlacialCore slow field).
        /// </summary>
        public void OnEmpowered(EmpowermentAbility ability)
        {
            _empowerment = ability;
            _isEmpowered = true;
            _shotsSinceLastBurst = 0;

            if (ability == EmpowermentAbility.GlacialCore)
                StartCoroutine(GlacialCoreSlowLoop());

            Debug.Log($"[TowerCombat] Empowerment activated — {ability} on '{(_tower != null ? _tower.Data?.towerName : name)}'.");
        }

        // ── Update — fire loop ────────────────────────────────────────────────

        private void Update()
        {
            if (Time.time < _nextAttackTime) return;

            float range = _tower != null && _tower.CurrentRange > 0f ? _tower.CurrentRange : _fallbackRange;

            // TrueAim: acquire secondary (highest-HP) target before primary sweep.
            IDamageable secondary = null;
            if (_isEmpowered && _empowerment == EmpowermentAbility.TrueAim)
                secondary = FindHighestHpTarget(range);

            IDamageable target = FindNearestTarget(range);
            if (target == null)
            {
                _nextAttackTime = Time.time + _idleRescan;
                return;
            }

            // ── TowerAI OBSERVABILITY (behavior-NEUTRAL — adds NO selection logic) ──
            // Records, per acquisition: chosen target name, distance, and whether a
            // wall/structure sits on the line tower->target. The Linecast is computed
            // ONLY for this trace boolean and is NOT used to pick/reject the target —
            // targeting today is pure closest-living-hostile (no LoS / no defense
            // priority). Gated on FlowTrace.Enabled (cheap when off) and only runs on
            // the fire tick (not every frame). Throttled ~1/sec per tower (hot loop).
            if (FlowTrace.Enabled)
            {
                Vector3 fPos = _firePoint != null ? _firePoint.position : transform.position;
                Vector3 tPos = target.WorldPosition;
                float dist = Vector3.Distance(fPos, tPos);
                bool structureBetween =
                    Physics.Linecast(fPos, tPos, out RaycastHit losHit, ~0, QueryTriggerInteraction.Ignore)
                    && losHit.collider != null
                    && losHit.collider.GetComponentInParent<IDamageableStructure>() != null;
                string tName = (target as MonoBehaviour) != null ? (target as MonoBehaviour).name : "(boss/seam)";
                FlowTrace.Throttle("TowerAI", "acquire", 1f,
                    $"'{(_tower != null && _tower.Data != null ? _tower.Data.towerName : name)}' picked target='{tName}' dist={dist:F1} structureBetween={structureBetween} (LoS gate ACTIVE: Structure-layer walls block fire; selection still closest-first, no Heart-priority)");
            }

            FireAt(target, secondary);
            // WO-432 — fire rate is now DATA-DRIVEN via the TowerPerkTable (cooldown * fireRateMult),
            // not the old implicit cooldown/level rule. The tier is the tower's EffectiveTier (placed
            // level 1..3, or the capstone tier 4 once Empowered), so upgrading visibly speeds up fire.
            // WO-676 BULWARK: Standing Orders (towerAttackSpeed) — divide by Tower's TTL-cached
            // talent multiplier (Tower.TalentAttackSpeedMult, the same cache/class-resolve as
            // TalentDamageMult/TalentRangeAdd — no second pattern here). Placed towers are ALWAYS
            // player-owned (Tower.cs: spawned by TowerPlacementSystem; garrison turrets are
            // DefenseTower with Allegiance.EnemyOwned), so the read applies unconditionally.
            // ÷1 at Σ=0, so baseline cadence is unchanged.
            int tier = _tower != null ? _tower.EffectiveTier : 1;
            float cooldown = TowerPerkTable.EffectiveCooldown(_baseCooldown, tier)
                             / (_tower != null ? _tower.TalentAttackSpeedMult() : 1f);
            _nextAttackTime = Time.time + cooldown;
        }

        // ── Target selection ──────────────────────────────────────────────────

        /// <summary>
        /// HORIZONTAL (XZ-plane) squared distance — the range gate ignores the Y drop.
        /// Owner 2026-06-28: a wall-mounted tower sits high above the ground enemies, so a
        /// full 3D range check spent the budget on the height drop and shrank the tower's
        /// ground footprint (anything on the floor "landed out of range"). Gating on the
        /// horizontal distance lets an elevated tower reach as far across the ground as a
        /// ground tower would — "natural physics would create a larger arc." Applies
        /// uniformly to every tower, so no per-tower wall-mounted tag is needed.
        /// </summary>
        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// The air/ground targeting-matrix gate (TD rock-paper-scissors). Returns
        /// true when THIS tower may fire at <paramref name="target"/>: an anti-ground
        /// tower skips flyers, an anti-air tower skips ground, "both" hits all. A
        /// target that doesn't declare an <see cref="ICombatLayered"/> layer is
        /// treated as Ground (back-compat — an all-ground board is unchanged), and a
        /// tower with no TowerData defaults to Ground. This is the single central
        /// check every acquisition path routes through.
        /// </summary>
        private bool CanHit(IDamageable target)
        {
            if (target == null) return false;
            var targets = _tower != null && _tower.Data != null
                ? _tower.Data.targets
                : TowerTargets.Ground;
            CombatLayer layer = (target is ICombatLayered layered)
                ? layered.Layer
                : CombatLayer.Ground;
            return TowerData.CanTarget(targets, layer);
        }

        // LoS gate (owner 2026-06-27 — "walls block tower fire"): true when a wall on the "Structure"
        // layer sits between the fire point and the target, so the shot is blocked. DEGRADE OPEN: if the
        // Structure layer is absent (mask 0) or there's no fire point, never block — a misconfigured
        // scene must not make towers inert. Mirrors the LoS layer the castle/stronghold walls carry.
        private bool BlockedByWall(IDamageable target)
        {
            if (target == null) return true;
            // A FLYER (the apex dragon) is engaged from above — a ground wall on the
            // "Structure" layer does NOT block a shot arcing up to the sky. The recent
            // wall-LoS gate (owner 2026-06-27 "walls block tower fire") was authored for
            // GROUND creeps; for a high flyer the tower->target Linecast clips the castle
            // wall/roof and wrongly rejects it, the "towers cannot target dragon, can't
            // see dragon as too high" F8 (owner 2026-06-28). Exempt flyers from the gate.
            if (target is ICombatLayered layered && layered.Layer == CombatLayer.Flying) return false;
            if (_structureMask < 0) _structureMask = LayerMask.GetMask("Structure");
            if (_structureMask == 0) return false;
            Vector3 fPos = _firePoint != null ? _firePoint.position : transform.position;
            Vector3 tPos = target.WorldPosition;
            // WO-1720 — LoS DECISION POINT. Capture the hit collider (not just the bool) so a
            // future "fires through a wall the player can see" capture is provable from ONE log
            // read: pair blocked=false with fPos/tPos Y and the known WallSegment collider height
            // (WO-1719 found colliderBounds 3m vs rendererBounds 15m on an intact wall) instead of
            // re-deriving it from scratch. Throttled — this runs on the fire tick per tower.
            bool blocked = Physics.Linecast(fPos, tPos, out RaycastHit losHit, _structureMask, QueryTriggerInteraction.Ignore);
            if (FlowTrace.Enabled)
            {
                FlowTrace.Throttle("TowerLoS", $"TowerCombat:{GetInstanceID()}", 1f,
                    $"'{(_tower != null && _tower.Data != null ? _tower.Data.towerName : name)}' BlockedByWall fPos={fPos} tPos={tPos} blocked={blocked}" +
                    (blocked && losHit.collider != null
                        ? $" hit='{losHit.collider.name}' hitColliderBoundsY=[{losHit.collider.bounds.min.y:F2}..{losHit.collider.bounds.max.y:F2}] hitPoint={losHit.point}"
                        : " (no Structure collider on the line — if a wall is visually there, its collider is undersized/absent)"));
            }
            return blocked;
        }

        /// <summary>
        /// WO-1524 — the side these target scans strike FROM, handed to
        /// <see cref="CombatFactionRules"/> so friend-or-foe is answered by the ONE authority
        /// instead of the four inline <c>Faction != CombatFaction.Hostile</c> copies that used
        /// to live in this file (that predicate's header forbids the copy).
        ///
        /// A LITERAL, and here it is uncontroversial: <see cref="TowerCombat"/> is the
        /// player-tower firing brain and carries no faction of its own — the EnemyOwned
        /// garrison path never runs through it. <see cref="CombatFaction"/> has exactly two
        /// members (IDamageable.cs:28-34), so <c>MayAttack(Friendly, t)</c> is bit-for-bit the
        /// old <c>t != null &amp;&amp; t.IsAlive &amp;&amp; t.Faction != Hostile</c>.
        /// </summary>
        private const CombatFaction ScanSide = CombatFaction.Friendly;

        private IDamageable FindNearestTarget(float range)
        {
            if (_wave == null) { ResolveWave(); if (_wave == null) return null; }

            var list = _wave.LiveEnemies;
            if (list == null) return null;

            Vector3 myPos = transform.position;
            float maxSq = range * range;
            float bestSq = float.MaxValue;
            IDamageable best = null;

            for (int i = 0; i < list.Count; i++)
            {
                var enemy = list[i];
                if (enemy == null) continue;
                float sq = HorizontalSqr(enemy.transform.position, myPos);
                if (sq > maxSq || sq >= bestSq) continue;
                var dmg = enemy.GetComponent<EnemyDamageable>();
                // WO-1524: one authority. MayAttack folds null + IsAlive + faction. `dmg` is
                // EnemyDamageable, which implements IDamageable only — no overload ambiguity.
                if (!CombatFactionRules.MayAttack(ScanSide, dmg)) continue;
                // Air/ground matrix: skip an enemy this tower's layer can't reach.
                if (!CanHit(dmg)) continue;
                // LoS gate: a wall between the tower and the enemy blocks the shot.
                if (BlockedByWall(dmg)) continue;
                bestSq = sq;
                best = dmg;
            }

            // WO-125 Bug 2: the apex dragon is NOT in LiveEnemies (it owns kinematic
            // flight, not a NavMesh agent) and carries no EnemyDamageable adapter — it
            // implements IDamageable directly. So the ground-roster scan above can never
            // see it. Consider it here through the Core seam (Village->Core is allowed).
            var boss = _wave?.LiveApexBoss;
            // WO-1524: one authority — MayAttack folds the null + IsAlive + faction triple that
            // was spelled out inline here. The (IDamageable) cast is kept from the original.
            if (CombatFactionRules.MayAttack(ScanSide, (IDamageable)boss)
                && CanHit(boss)   // air/ground matrix: the dragon flies — anti-air / both only
                && !BlockedByWall((IDamageable)boss))   // LoS: don't shoot the boss through a wall
            {
                float bsq = HorizontalSqr(((IDamageable)boss).WorldPosition, myPos);
                if (bsq <= maxSq && bsq < bestSq)
                {
                    bestSq = bsq;
                    best = boss;
                }
            }
            return best;
        }

        // Kept for backward compat (EnemyBrain + test callers used FindBestTarget).
        private IDamageable FindBestTarget(float range) => FindNearestTarget(range);

        /// <summary>
        /// TrueAim secondary targeting — returns the highest-HP enemy in range
        /// (the most dangerous target). Returns null when only one enemy is in range.
        /// </summary>
        private IDamageable FindHighestHpTarget(float range)
        {
            if (_wave == null) { ResolveWave(); if (_wave == null) return null; }

            var list = _wave.LiveEnemies;
            if (list == null) return null;

            Vector3 myPos = transform.position;
            float maxSq = range * range;
            float bestHp = -1f;
            IDamageable best = null;

            for (int i = 0; i < list.Count; i++)
            {
                var enemy = list[i];
                if (enemy == null) continue;
                float sq = HorizontalSqr(enemy.transform.position, myPos);
                if (sq > maxSq) continue;
                var dmg = enemy.GetComponent<EnemyDamageable>();
                // WO-1524: one authority (see FindNearestTarget) — null + IsAlive + faction.
                if (!CombatFactionRules.MayAttack(ScanSide, dmg)) continue;
                // Air/ground matrix: don't pick a target this tower can't actually hit.
                if (!CanHit(dmg)) continue;
                // LoS gate: a wall between the tower and the enemy blocks the shot.
                if (BlockedByWall(dmg)) continue;
                if (dmg.Hp > bestHp) { bestHp = dmg.Hp; best = dmg; }
            }

            // WO-125 Bug 2 (mirror): also weigh the apex dragon for TrueAim's
            // highest-HP pick — it's the biggest health pool on the field by far.
            var boss = _wave?.LiveApexBoss;
            // WO-1524: one authority — MayAttack folds null + IsAlive + faction.
            if (CombatFactionRules.MayAttack(ScanSide, (IDamageable)boss)
                && CanHit(boss))   // air/ground matrix: anti-air / both only
            {
                float bsq = HorizontalSqr(((IDamageable)boss).WorldPosition, myPos);
                if (bsq <= maxSq && ((IDamageable)boss).Hp > bestHp)
                {
                    bestHp = ((IDamageable)boss).Hp;
                    best = boss;
                }
            }
            return best;
        }

        // ── Fire ──────────────────────────────────────────────────────────────

        private void FireAt(IDamageable target, IDamageable trueAimSecondary = null)
        {
            if (ProjectilePool.Instance == null)
            {
                // A tower that can't fire must SELF-REPORT (§12) — not silently no-op.
                // Throttled: this is on the fire tick, once/sec is enough to surface it.
                FlowTrace.Throttle("TowerCombat", "no-pool-fireat", 1f,
                    $"FireAt: ProjectilePool.Instance is null on '{(_tower != null && _tower.Data != null ? _tower.Data.towerName : name)}' — tower cannot fire (no projectile spawned).");
                return;
            }

            float damage  = _tower != null && _tower.CurrentDamage > 0f ? _tower.CurrentDamage : _fallbackDamage;
            int   level   = _tower != null ? _tower.CurrentLevel : 1;
            Vector3 firePos = _firePoint != null ? _firePoint.position : transform.position;

            // ── ManaSurge: every 5th shot → 3-bolt burst at 60 % each ────────
            if (_isEmpowered && _empowerment == EmpowermentAbility.ManaSurge)
            {
                _shotsSinceLastBurst++;
                if (_shotsSinceLastBurst >= ManaSurgeBurstInterval)
                {
                    _shotsSinceLastBurst = 0;
                    float burstDmg = damage * ManaSurgeBurstDamageMult;
                    for (int i = 0; i < 3; i++)
                        FireSingleProjectile(target, burstDmg, DamageElement.Aether, firePos);
                    VFXManager.Play(VFXType.Cast_MageCharge, firePos);
                    GameSfx.PlayTowerFire();   // DEF-183: burst is not silent
                    HitStopManager.DoImpact(HitTier.Medium);
                    return;  // burst REPLACES the standard shot this tick
                }
            }

            // ── Standard shot ─────────────────────────────────────────────────
            DamageElement element = _isEmpowered
                ? AbilityToElement(_empowerment)
                : DamageElement.None;

            FireSingleProjectile(target, damage, element, firePos);

            // ── EternalEmber: apply Burn status + start DoT coroutine ─────────
            if (_isEmpowered && _empowerment == EmpowermentAbility.EternalEmber)
            {
                target.ApplyStatus(StatusEffect.Burn, EternalEmberBurnDuration);
                StartCoroutine(BurnDoTCoroutine(target, EternalEmberBurnDps, EternalEmberBurnDuration));
            }

            // ── TrueAim: fire at secondary target simultaneously ───────────────
            if (_isEmpowered && _empowerment == EmpowermentAbility.TrueAim && trueAimSecondary != null)
                FireSingleProjectile(trueAimSecondary, damage, DamageElement.None, firePos);

            // ── VFX: muzzle flash ─────────────────────────────────────────────
            // WO-887 "ranged release (any) -> MuzzleFlash": the low/mid tier branch used to play
            // VFXType.Projectile_TowerArcane here - a PROJECTILE BODY row (catalog IsLoop=true,
            // Resources/VFX/Projectiles/Projectile_Arcane) parked at the barrel and left to run out
            // its detected duration. It is the wrong FAMILY for a release beat: a travel loop has no
            // flash shape and its lifetime is meant to be the FLIGHT, not a frame or two of muzzle.
            // Cast_MuzzleFlash is the tracked mirror of the pack's MuzzleFlash recipe, MEASURED
            // (2026-08-05) as a genuine one-shot at the derivation authority: root system
            // rateOverTime 0 / rateOverDistance 0 with a single count-1 burst at t=0, second layer
            // FlashHeadon likewise, so the catalog row is IsLoop=false and it can never book one of
            // the 20 global loop slots at the rate a field of towers fires. MinQuality 0 - the
            // release flash is how the player reads that a shot LEFT the barrel, which is combat
            // legibility, not dressing.
            //
            // playSound:false: GameSfx.PlayTowerFire() already fires the shot cue a few lines below,
            // and VfxToSfx has no Cast_MuzzleFlash mapping today - passing false makes the intent
            // STRUCTURAL, so a later mapping cannot silently double the tower's fire sound.
            VFXManager.Play(VFXType.Cast_MuzzleFlash, firePos, Quaternion.identity, playSound: false);
            // The empowered / L3 charge read is kept and LAYERED on top (extra layer = the
            // colourblind-safe tier escalation the owner felt-tested on 2026-07-17), not replaced.
            if (level >= 3 || _isEmpowered)
                VFXManager.Play(VFXType.Cast_MageCharge, firePos);

            // WO-VFX-TOWERS: Hovl cast burst at the muzzle, LAYERED on top of the legacy
            // VFXType muzzle flash (fallback stays). Keyed off the shot's element (Aether when
            // empowered/base). Null-safe — PlayKey no-ops on a null/unknown key (Ice/None have
            // no cast key, so those fall back to the VFXType flash alone). Reads by motion.
            //
            // TIER ESCALATION (owner felt-test 2026-07-17: "more/better VFX at higher tower levels"):
            // the muzzle burst SCALES with the tower level (bigger flash as it upgrades), and at L3
            // an extra arcane cast layer is stacked so a maxed tower's shot reads as dramatically
            // stronger. Reads by SIZE + an extra layer (colorblind-safe), never hue. Guarded so a
            // bad key logs + skips (never blanks the shot).
            float muzzleScale = TierVfxScale(level);
            Guard.Try("TowerVfx", "muzzle cast", () =>
                VFXManager.PlayKey(CastKeyFor(element), firePos, default, null, null, muzzleScale));
            if (level >= 3)
                Guard.Try("TowerVfx", "muzzle L3 extra", () =>
                    VFXManager.PlayKey("Arcane_Cast", firePos, default, null, null, muzzleScale * 0.8f));
            FlowTrace.Throttle("TowerVfx", "fire", 1f,
                $"level={level} fire cast='{CastKeyFor(element)}' scale={muzzleScale:0.0}{(level >= 3 ? " +L3 arcane layer" : "")}");

            // DEF-183: tower fire SFX through the existing audio surface
            // (CoreServices.Audio, null-guarded inside). Mixed low — many towers
            // fire at once.
            GameSfx.PlayTowerFire();

            HitStopManager.DoImpact(_isEmpowered ? HitTier.Medium : HitTier.Light);
        }

        private void FireSingleProjectile(IDamageable target, float damage, DamageElement element, Vector3 firePos)
        {
            if (ProjectilePool.Instance == null)
            {
                FlowTrace.Throttle("TowerCombat", "no-pool-single", 1f,
                    $"FireSingleProjectile: ProjectilePool.Instance is null on '{(_tower != null && _tower.Data != null ? _tower.Data.towerName : name)}' — no bolt spawned.");
                return;
            }

            var proj = ProjectilePool.Instance.GetProjectile();
            // POOL-GET reset-verify (mirror VerifyArmorRendersNow): a pooled projectile that
            // comes back null, or with no enabled renderer/mesh, fires SILENTLY (the "no-fire /
            // invisible bolt" symptom). Self-report and bail rather than spawn an invisible shot.
            if (proj == null)
            {
                FlowTrace.Throttle("TowerCombat", "pool-null-proj", 1f,
                    $"FireSingleProjectile: pool returned a null projectile on '{name}' — no bolt fired.");
                return;
            }

            proj.transform.position = firePos;

            // DEF-PROJ: the projectile's ART look. When the shot carries an element
            // (empowered), the sprite matches it. An un-empowered shot is physical
            // (element None) but should still LOOK like its tower type — an Archer
            // fires an arrow, a Frost tower an ice bolt, a Mage an arcane bolt — so
            // we derive a VISUAL element from the tower name without changing the
            // damage typing.
            DamageElement visual = element != DamageElement.None
                ? element
                : ProjectileArtCatalog.ElementForTowerName(_tower != null && _tower.Data != null ? _tower.Data.towerName : name);

            proj.Initialize(target, damage, element, visual);

            // RESET-VERIFY (post-Initialize, mirror VerifyArmorRendersNow): a pooled projectile
            // re-issued from the pool can come back with its renderer disabled / mesh stripped from
            // a prior life — it would fly INVISIBLE (the silent "no-fire" symptom). Prove it carries
            // a live renderer with a mesh; if not, self-report (throttled) so a dead pool surfaces.
            VerifyProjectileRenders(proj.gameObject);
        }

        // RENDER-VERIFY for a pooled projectile (TGVRU): >=1 ENABLED Renderer carrying a mesh
        // (MeshRenderer/SkinnedMeshRenderer with a sharedMesh, or a SpriteRenderer/Trail/Line/
        // ParticleSystem that draws meshlessly). A bolt that renders nothing is the invisible-fire
        // symptom — log it (throttled, hot loop) rather than fire a ghost shot. Never throws.
        private void VerifyProjectileRenders(GameObject proj)
        {
            if (proj == null) return;

            int total = 0, enabled = 0, withMesh = 0;
            foreach (var r in proj.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                total++;
                if (r.enabled) enabled++;

                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                // Sprite/Line/Trail/Particle renderers draw without a MeshFilter mesh — count them.
                bool drawsWithoutMesh = !(r is MeshRenderer) && !(r is SkinnedMeshRenderer);
                if (mesh != null || drawsWithoutMesh) withMesh++;
            }

            bool renders = enabled > 0 && withMesh > 0;
            if (!renders)
            {
                FlowTrace.Throttle("TowerCombat", "proj-no-render", 1f,
                    $"VerifyProjectileRenders: pooled bolt on '{name}' has no visible renderer " +
                    $"(total={total}, enabled={enabled}, withMesh={withMesh}) — bolt fires INVISIBLE.");
            }
        }

        // ── WO-VFX-TOWERS: DamageElement → Hovl catalog key (muzzle cast / impact) ──
        // Maps the shot's element to the authored Hovl keys. Returns null where no key exists
        // (Ice/None have no cast burst) — PlayKey no-ops on null, so the legacy VFXType flash
        // remains the sole visual there. Kept beside AbilityToElement for the element mapping.
        private static string CastKeyFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame:  return "Fire_Cast";
                case DamageElement.Aether: return "SimpleCast_Cast";
                case DamageElement.Ice:    return "Freezing_Projectile";
                default:                   return "PP_MuzzleFlash";
            }
        }

        // TIER -> Hovl VFX scale multiplier (owner felt-test 2026-07-17: bigger firing/impact FX at
        // higher tower levels). L1 = 1.0 (baseline, unchanged), L2 = 1.3, L3 = 1.7. Escalation reads
        // by SIZE (+ extra L3 layers at the call sites), colorblind-safe. Kept beside the key tables
        // so the whole tower-vfx tier mapping lives in one place.
        private static float TierVfxScale(int level) =>
            level >= 3 ? 1.7f : level == 2 ? 1.3f : 1.0f;

        private static string ImpactKeyFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame:  return "FireImpact_Impact";
                case DamageElement.Ice:    return "Freezing_Impact";
                case DamageElement.Aether: return "PP_PlasmaExplosionEffect";
                default:                   return "Spear_Impact";   // None / Physical
            }
        }

        private static DamageElement AbilityToElement(EmpowermentAbility ability) =>
            ability switch
            {
                EmpowermentAbility.ManaSurge    => DamageElement.Aether,
                EmpowermentAbility.GlacialCore  => DamageElement.Ice,
                EmpowermentAbility.EternalEmber => DamageElement.Flame,
                EmpowermentAbility.TrueAim      => DamageElement.None,    // Physical — intentional
                _                               => DamageElement.None,
            };

        // ── Empowerment coroutines ────────────────────────────────────────────

        /// <summary>
        /// GlacialCore — pulses an AoE slow field every <see cref="GlacialPulseInterval"/>
        /// seconds. Runs for the tower's lifetime (stops when the component is destroyed).
        /// </summary>
        private IEnumerator GlacialCoreSlowLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(GlacialPulseInterval);

                float range = _tower != null && _tower.CurrentRange > 0f ? _tower.CurrentRange : _fallbackRange;
                if (_wave == null) { ResolveWave(); continue; }

                var list = _wave.LiveEnemies;
                if (list == null) continue;

                Vector3 myPos = transform.position;
                float maxSq   = range * range;

                for (int i = 0; i < list.Count; i++)
                {
                    var enemy = list[i];
                    if (enemy == null) continue;
                    float sq = HorizontalSqr(enemy.transform.position, myPos);
                    if (sq > maxSq) continue;
                    var dmg = enemy.GetComponent<EnemyDamageable>();
                    if (dmg == null || !dmg.IsAlive) continue;
                    // Air/ground matrix: a ground tower can't field-slow a flyer.
                    if (!CanHit(dmg)) continue;
                    // LoS gate: a wall between the tower and the enemy blocks the slow field.
                    if (BlockedByWall(dmg)) continue;
                    dmg.ApplyStatus(StatusEffect.Slow, GlacialSlowDuration);
                }
            }
        }

        /// <summary>
        /// EternalEmber — ticks 4 damage per second for <paramref name="duration"/>
        /// seconds on <paramref name="target"/>. Automatically stops if the target dies.
        /// </summary>
        private IEnumerator BurnDoTCoroutine(IDamageable target, float dps, float duration)
        {
            float elapsed = 0f;
            const float tickInterval = 1f;
            while (elapsed < duration)
            {
                yield return new WaitForSeconds(tickInterval);
                elapsed += tickInterval;
                if (target == null || !target.IsAlive) yield break;
                target.TakeDamage(dps * tickInterval, DamageElement.Flame);
            }
        }

        // ── Impact callback (called by PooledProjectile.OnHit) ───────────────

        /// <summary>
        /// Called externally (e.g. by PooledProjectile.OnHit) to trigger an
        /// impact VFX at the hit position. Scales by level and empowerment state.
        /// </summary>
        public void OnProjectileImpact(Vector3 hitPosition)
        {
            int level = _tower != null ? _tower.CurrentLevel : 1;

            // The shot's element mirrors FireAt exactly (Aether/Ice/Flame when empowered, else
            // None). HOISTED above the impact pick - it used to be read only by the Hovl key
            // below, which is why the typed impact could not see it. This is the ONE real
            // element signal at a tower's hit resolution: EmpowermentAbility -> DamageElement.
            DamageElement element = _isEmpowered ? AbilityToElement(_empowerment) : DamageElement.None;

            // WO-887 "element procs override the physical hit". TIER decides SIZE, ELEMENT decides
            // FLAVOUR - and only the ELEMENT half is new. Every non-empowered tower is
            // DamageElement.None and keeps its exact existing ladder (L1 physical / L2 aether /
            // L3 aether detonation), so nothing the owner has already felt-tuned moves.
            //
            // What was WRONG: an empowered tower ALWAYS detonated Impact_ExplosionAether. An
            // EternalEmber (Flame) tower's bolt therefore burst in violet arcane light, and a
            // GlacialCore (Ice) tower's did too - the shot's own element was computed eight lines
            // further down and never reached the visual. AbilityToElement is the authority:
            // EternalEmber->Flame, GlacialCore->Ice, ManaSurge->Aether, TrueAim->None (physical,
            // intentional), so TrueAim and ManaSurge land on exactly the type they land on today.
            //
            // NOTE for the PO: there is no large/tier-3 ICE impact VFXType (the enum has one
            // Impact_Ice and appending is Grok's single-owner edit, WO-884 section 0.2), so a
            // GlacialCore tower reads element-correct but does NOT grow with tier on the typed
            // layer. The Hovl "Freezing_Impact" layer below still carries its tier SCALE, so the
            // escalation is not lost - it is just carried by the other layer.
            var impactType = (_isEmpowered || level >= 3)
                ? (element == DamageElement.Flame ? VFXType.Impact_ExplosionFire
                  : element == DamageElement.Ice  ? VFXType.Impact_Ice
                                                  : VFXType.Impact_ExplosionAether)
                : level == 2
                    ? (element == DamageElement.Flame ? VFXType.Impact_Flame
                      : element == DamageElement.Ice  ? VFXType.Impact_Ice
                                                      : VFXType.Impact_Aether)
                    : (element == DamageElement.Flame ? VFXType.Impact_Flame
                      : element == DamageElement.Ice  ? VFXType.Impact_Ice
                                                      : VFXType.Impact_Physical);
            // playSound left at its default (true) ON PURPOSE, unlike the muzzle above: routing by
            // element also routes the PAIRED SfxId, which is the fix. VfxToSfx sends
            // Impact_ExplosionFire/Impact_Flame -> FireExplosion and Impact_Ice -> None, so a fire
            // tower stops playing the ARCANE bang it has been playing and an ice tower stops
            // playing one at all. GameSfx.PlayTowerArrowHit() below is the separate CONNECT cue and
            // is unchanged - the two have always layered.
            VFXManager.Play(impactType, hitPosition);

            // WO-VFX-TOWERS: Hovl impact burst LAYERED on top of the legacy VFXType impact.
            // Null-safe — no-ops on an unknown key. Reads by shape/motion.
            //
            // TIER ESCALATION (owner felt-test 2026-07-17): the impact SCALES with tower level, and
            // at L3 a heavier Cleave detonation is stacked on top so an upgraded tower's hits land
            // harder. Reads by SIZE + an extra blast layer (colorblind-safe). Guarded per spawn.
            float impactScale = TierVfxScale(level);
            Guard.Try("TowerVfx", "impact", () =>
                VFXManager.PlayKey(ImpactKeyFor(element), hitPosition, default, null, null, impactScale));
            if (level >= 3)
                Guard.Try("TowerVfx", "impact L3 cleave", () =>
                    VFXManager.PlayKey("Cleave_Impact", hitPosition, default, null, null, 0.9f));
            FlowTrace.Throttle("TowerVfx", "impact", 1f,
                $"level={level} impact key='{ImpactKeyFor(element)}' scale={impactScale:0.0}{(level >= 3 ? " +L3 cleave" : "")}");

            // WO-371: projectile impact SFX through the existing audio surface
            // (CoreServices.Audio, null-guarded inside GameSfx). The tower-fire "pew"
            // already plays on the shot (FireAt); this is the arrow/bolt CONNECT
            // sound on the enemy, mixed low so a wall of towers doesn't drown out.
            GameSfx.PlayTowerArrowHit();

            HitStopManager.DoImpact((_isEmpowered || level >= 3) ? HitTier.Medium : HitTier.Light);
        }

        private void OnDrawGizmosSelected()
        {
            float r = _tower != null && _tower.CurrentRange > 0f ? _tower.CurrentRange : _fallbackRange;
            Gizmos.color = _isEmpowered ? Color.cyan : Color.red;
            Gizmos.DrawWireSphere(transform.position, r);
        }
    }
}
