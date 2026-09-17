// =============================================================================
// EnemySiegeCatapult — flanking siege engines (WO-1835, piece 5)
// -----------------------------------------------------------------------------
// OWNER RULING (2026-09-17, verbatim): "catapults blasting the walls from the sides at the
// same time".
//
// THREE THINGS THE RULING ACTUALLY DEMANDS, and each is a separate mechanism here:
//   1. "catapults blasting the walls" — damage dealt to a WallSegment through
//      IDamageableStructure.ApplyContactDamage, not to the Heart and not to the hero.
//   2. "from the sides"  — the target panel is chosen by EndlessPressure.FlankBreachTarget,
//      which scores panels by how LATERAL they are to the approach lane and hands opposing
//      sides to even/odd engines. A second engine on the same face is not a flank.
//   3. "at the same time" — every engine quantizes its fire to the shared volley index
//      EndlessPressure.VolleyIndex(Time.time, interval). They therefore release TOGETHER
//      with NO coordinator object, NO static clock and NO second owner of the cadence:
//      synchronization is a property of the arithmetic, so it cannot fail because some
//      manager component was absent from the scene. EndlessPressure.CatapultCount's floor
//      of TWO exists for the same clause — one engine cannot bombard two faces at once.
//
// ⛔ IT DOES NOT MOVE ITSELF. EnemyBrain remains the ONE MOVER (see EnemyWallBreacher's
// header for the full reasoning). The standoff comes from the EXISTING Kiter archetype —
// EnemyBrain.KiterTactics is literally "hold ~10 m, back off inside 6 m", which is the
// behaviour a siege engine wants and which already ships, tested, in this tree. This class
// nominates the panel and lands the volley; the brain walks and holds.
//
// §16 ART NOTE: rides the existing `ogre` def (a real, already-pushed R2 bundle) because
// there is no catapult model in the project. FIRST-PASS ART — a siege-engine body is an
// enemies.json + R2 push ticket for later, not a change to this logic. See the header of
// EndlessPressure.cs for why no new def is minted here.
// =============================================================================
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
// NOTE: VfxPool / PooledVfx (Vfx/VfxPool.cs:30) and WallSegment (Walls/WallSegment.cs:50) are
// both in the DeNelle.Village namespace itself — there is no DeNelle.Village.Vfx namespace at
// all, and DeNelle.Village.Walls holds only WallTierData. This file is already in
// DeNelle.Village, so both types resolve with no using.

namespace DeNelle.Village
{
    /// <summary>
    /// Ranged siege engine: holds at standoff on a flank and bombards a wall panel in volleys
    /// synchronized with every other engine in the scene. Added by
    /// <see cref="EndlessPressure.Attach"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySiegeCatapult : MonoBehaviour
    {
        /// <summary>
        /// How far an engine can lob. Comfortably beyond the Kiter standoff (~10 m) so the
        /// engine is firing while it holds, and beyond tower reach so it reads as a siege
        /// weapon rather than a melee unit that happens to hit walls.
        /// FIRST-PASS, OWNER FELT-TUNES.
        /// </summary>
        private const float BombardRange = 24f;
        private const float RepickSeconds = 1f;

        /// <summary>
        /// Monotonic counter handing each engine released this session its flank index, which
        /// decides which SIDE it is assigned. Deliberately a plain counter and not a random
        /// draw: a pair must reliably split onto two faces, and randomness gives same-side
        /// pairs often enough that the owner would felt-test a flank that never happened.
        /// </summary>
        private static int s_flankCounter;

        private Enemy _enemy;
        private int _trueWave;
        private int _flankIndex;
        private float _volleySeconds = 6f;
        private float _damagePerHit = 9f;
        private int _lastVolleyIndex = int.MinValue;
        private float _repickTimer;
        private Vector3 _approachHeading = Vector3.forward;
        private WallSegment _target;
        private int _stonesLanded;
        private float _damageDealtTotal;

        /// <summary>The panel this engine is bombarding (null before its first pick).</summary>
        public WallSegment Target => _target;

        /// <summary>Total structure damage this engine has dealt (captures + oracle).</summary>
        public float DamageDealtTotal => _damageDealtTotal;

        /// <summary>Which lateral side this engine was assigned (even/odd = opposing sides).</summary>
        public int FlankIndex => _flankIndex;

        /// <summary>
        /// Binds the component to its host body. Idempotent and safe on a POOLED body being
        /// leased for a new life — every per-life field is re-stamped here.
        /// </summary>
        public void Configure(Enemy enemy, int trueWave, EndlessPressureTuning tuning)
        {
            _enemy = enemy != null ? enemy : GetComponent<Enemy>();
            _trueWave = trueWave;
            _volleySeconds = Mathf.Max(1f, tuning != null ? tuning.CatapultVolleySeconds : 6f);
            _damagePerHit = Mathf.Max(0.5f, tuning != null ? tuning.CatapultDamagePerHit : 9f);
            _flankIndex = s_flankCounter++;
            _target = null;
            _stonesLanded = 0;
            _damageDealtTotal = 0f;
            _repickTimer = 0f;

            // The spawner faces a released body along its approach heading (SpawnOne builds the
            // rotation from WaveSpawnPoint.HeadingToGate), so the body's own forward IS the
            // approach lane at the moment of release. Latched here rather than read per-tick,
            // because the engine turns to face its target and would otherwise re-derive the
            // "approach" from whatever it is currently looking at — which is the panel it picked,
            // making every subsequent pick agree with the first by construction.
            _approachHeading = transform.forward;

            // Fire on the NEXT shared volley boundary, never immediately: an engine that fired
            // the instant it spawned would break the synchrony the ruling asks for.
            _lastVolleyIndex = EndlessPressure.VolleyIndex(Time.time, _volleySeconds);

            if (_enemy == null)
            {
                FlowTrace.Fail(EndlessPressure.Sys,
                    $"EnemySiegeCatapult on '{name}' has no Enemy component — it cannot be positioned " +
                    "or killed; disabling rather than bombarding from an unkillable body.");
                enabled = false;
                return;
            }

            // Standoff posture through the EXISTING Kiter archetype (hold ~10 m, back off inside 6 m).
            var brain = GetComponent<EnemyBrain>();
            if (brain != null)
            {
                brain.Role = EnemyRole.Ranged;
                brain.SetTactics(EnemyBrain.KiterTactics);
            }
            else
            {
                FlowTrace.Warn(EndlessPressure.Sys,
                    $"wave {trueWave}: catapult '{_enemy.EnemyId}' has NO EnemyBrain — it cannot hold the " +
                    "Kiter standoff and will walk into contact instead of besieging from range " +
                    "(volleys still land; the read is wrong, not the damage).");
            }
        }

        private void OnDisable()
        {
            var brain = GetComponent<EnemyBrain>();
            if (brain != null) brain.ClearStructureFocus();
            _target = null;
        }

        private void Update()
        {
            if (_enemy == null || _enemy.IsDead) return;

            AcquireTarget();
            TickVolley();
        }

        /// <summary>
        /// Re-picks a flanking panel on a 1 s cadence (and immediately when the current one
        /// collapses). Hands the pick to the brain so the engine walks to standoff on that side.
        /// </summary>
        private void AcquireTarget()
        {
            _repickTimer -= Time.deltaTime;
            bool valid = _target != null && _target.IsAlive;
            if (valid && _repickTimer > 0f) return;

            if (!valid && _target != null)
            {
                FlowTrace.Step(EndlessPressure.Sys,
                    $"wave {_trueWave}: catapult '{_enemy.EnemyId}' (flank {_flankIndex}) razed its panel — re-picking.");
                _target = null;
            }
            _repickTimer = RepickSeconds;

            WallSegment next = EndlessPressure.FlankBreachTarget(
                transform.position, _approachHeading, _enemy.SelfFaction, _flankIndex);

            if (next == null)
            {
                var brainNoWall = GetComponent<EnemyBrain>();
                if (brainNoWall != null) brainNoWall.ClearStructureFocus();
                FlowTrace.Once(EndlessPressure.Sys, $"nocat-{GetInstanceID()}",
                    $"wave {_trueWave}: catapult '{_enemy.EnemyId}' found NO attackable wall panel — " +
                    "no volley to fire; reverting to normal targeting.");
                _target = null;
                return;
            }

            if (ReferenceEquals(next, _target)) return;
            _target = next;

            var brain = GetComponent<EnemyBrain>();
            if (brain != null) brain.SetStructureFocus(next.transform);
            else _enemy.SetBrainTarget(next.transform);

            float flankness = EndlessPressure.FlankScore(
                next.transform.position - transform.position, _approachHeading);
            // CLAUDE.md §1: the side word is computed into a LOCAL rather than written as a nested
            // string literal inside an interpolation hole. The compile gate's brace checker has no
            // interpolated-string model — a `"` inside a `{ cond ? "a" : "b" }` hole ends the string
            // as far as it is concerned, the remainder scans as code, and the gate then withholds
            // COMPILE_GATE_OK on a file that is perfectly valid C#.
            string sideWord = (_flankIndex & 1) == 0 ? "right" : "left";
            FlowTrace.Step(EndlessPressure.Sys,
                $"wave {_trueWave}: catapult '{_enemy.EnemyId}' (flank {_flankIndex}, " +
                $"{sideWord} side) TARGETS WallSegment " +
                $"'{next.SegmentId}' — flankScore {flankness:0.00} (1.0 = square to the approach lane), " +
                $"volley every {_volleySeconds:0.#}s.");
        }

        /// <summary>
        /// Fires when the SHARED volley index advances, so every engine in the scene releases on
        /// the same boundary. Reads the index rather than counting down its own timer: a private
        /// countdown started at spawn would drift apart between engines released seconds apart,
        /// which is precisely the "at the same time" clause failing.
        /// </summary>
        private void TickVolley()
        {
            int index = EndlessPressure.VolleyIndex(Time.time, _volleySeconds);
            if (index == _lastVolleyIndex) return;
            _lastVolleyIndex = index;

            if (_target == null || !_target.IsAlive) return;

            float dist = Vector3.Distance(transform.position, _target.transform.position);
            if (dist > BombardRange)
            {
                FlowTrace.Throttle(EndlessPressure.Sys, $"catrange-{GetInstanceID()}", 5f,
                    $"wave {_trueWave}: catapult '{_enemy.EnemyId}' held volley {index} — panel " +
                    $"'{_target.SegmentId}' is {dist:0.0}m away, beyond the {BombardRange:0.#}m range " +
                    "(still closing to standoff).");
                return;
            }

            Guard.Try(EndlessPressure.Sys, "catapult volley", () =>
            {
                // THE ONE PREDICATE (WO-1439). Never re-implement the friend-or-foe comparison at
                // a damage site: an enemy-owned raid base also has WallSegments, and a siege
                // engine must not raze the camp it spawned in. Enemy.DealStructureDamage carries
                // the same assertion at the melee sink; this is the ranged siege equivalent.
                IDamageableStructure structure = _target;
                if (!CombatFactionRules.MayAttack(_enemy.SelfFaction, structure))
                {
                    FlowTrace.Fail(EndlessPressure.Sys,
                        $"wave {_trueWave}: catapult '{_enemy.EnemyId}' REFUSED volley at " +
                        $"'{_target.SegmentId}' — it is {structure.Faction}, the same side as the engine. " +
                        "Fix the SELECTION site (FlankBreachTarget); this sink only stops the stone.");
                    return;
                }

                float before = _target.Hp;
                structure.ApplyContactDamage(_damagePerHit);
                float dealt = Mathf.Max(0f, before - _target.Hp);
                _stonesLanded++;
                _damageDealtTotal += dealt;

                // Impact tell at the panel so the player can see WHERE the wall is being chewed
                // from — reuses the pooled ground ring, no new art (§16).
                PooledVfx impact = VfxPool.GetTelegraph(_target.transform.position);
                VfxPool.ReturnTelegraph(impact);

                // VOLLEY INDEX IS IN THE LINE ON PURPOSE. Two engines firing on the same index is
                // the proof that "at the same time" actually holds; a capture with two matching
                // volley numbers one line apart settles it without a rerun (CLAUDE.md §12).
                FlowTrace.Step(EndlessPressure.Sys,
                    $"wave {_trueWave}: catapult '{_enemy.EnemyId}' (flank {_flankIndex}) VOLLEY {index} " +
                    $"hit WallSegment '{_target.SegmentId}' for {dealt:0.#} structure damage " +
                    $"(panel {_target.Hp:0}/{WallSegment.MaxHp:0}, {dist:0.0}m, stone #{_stonesLanded}, " +
                    $"{_damageDealtTotal:0.#} total this life).");
            });
        }
    }
}
