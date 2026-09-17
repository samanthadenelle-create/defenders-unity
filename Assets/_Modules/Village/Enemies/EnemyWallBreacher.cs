// =============================================================================
// EnemyWallBreacher — "troops breaking through walls" (WO-1835, piece 2)
// -----------------------------------------------------------------------------
// OWNER RULING (2026-09-17): past wave 20 the player should face "troops breaking
// through walls" rather than a wave that treats the wall as scenery on the way to the
// Heart.
//
// ⛔ THIS COMPONENT DOES NOT MOVE THE ENEMY. EnemyBrain is the ONE MOVER — it owns every
// call to Enemy.SetBrainTarget / SetBrainTargetPosition, and its own header says so. A
// second component writing those seams every frame is two authorities fighting over one
// NavMeshAgent destination, which presents as an enemy vibrating between two targets and
// is unfixable from a trace because both writers look correct in isolation. So this class
// does exactly ONE job: it CHOOSES the wall and hands it to the brain through
// EnemyBrain.SetStructureFocus, an opt-in added for this ticket that mirrors the existing
// TauntTo override. The brain still paths, still closes, still swings, still respects room
// confinement and the faction rules. Damage is dealt by the brain's normal TryAttack into
// IDamageableStructure.ApplyContactDamage — this file applies no damage of its own.
//
// FALLBACK for a brain-less body: Enemy.DriveNav follows _brainTarget directly and
// Enemy.ProbeForStructure deals its own contact damage on arrival (Enemy.cs logs
// "no brain => structure awareness = forward ProbeForStructure only" for exactly this
// case), so a body with no EnemyBrain is steered through Enemy.SetBrainTarget instead.
// Both paths end in the same ApplyContactDamage sink.
// =============================================================================
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
// NOTE: WallSegment is in the DeNelle.Village namespace itself (Walls/WallSegment.cs:50), not in
// DeNelle.Village.Walls (which holds only WallTierData). This file is already in DeNelle.Village.

namespace DeNelle.Village
{
    /// <summary>
    /// Re-tasks one endless-wave melee body from the Heart march onto the nearest standing
    /// wall panel. Added at release time by <see cref="EndlessPressure.Attach"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyWallBreacher : MonoBehaviour
    {
        /// <summary>
        /// Re-pick cadence. Half a second: fast enough that a breacher whose panel collapses
        /// under it re-targets before it looks confused, slow enough that the shared wall cache
        /// (one scene scan per 0.5 s for ALL breachers) absorbs the cost.
        /// </summary>
        private const float RepickSeconds = 0.5f;

        private Enemy _enemy;
        private EnemyBrain _brain;
        private int _trueWave;
        private float _repickTimer;
        private WallSegment _focus;
        private bool _reportedFocus;

        /// <summary>The panel this breacher is currently committed to (null before its first pick).</summary>
        public WallSegment Focus => _focus;

        /// <summary>
        /// Binds the component to its host body. Idempotent and safe to call on a POOLED body
        /// being leased for a new life — every per-life field is re-stamped here, which is what
        /// lets <see cref="EndlessPressure.Attach"/> reuse an existing component instead of
        /// churning AddComponent/Destroy on a recycled GameObject.
        /// </summary>
        public void Configure(Enemy enemy, int trueWave)
        {
            _enemy = enemy != null ? enemy : GetComponent<Enemy>();
            _brain = GetComponent<EnemyBrain>();
            _trueWave = trueWave;
            _focus = null;
            _reportedFocus = false;
            // Re-pick on the very first live frame rather than after a full interval, so the
            // body peels toward the wall from its spawn instead of marching at the Heart first.
            _repickTimer = 0f;

            if (_enemy == null)
            {
                FlowTrace.Fail(EndlessPressure.Sys,
                    $"EnemyWallBreacher on '{name}' has no Enemy component — it cannot be steered; " +
                    "disabling so it falls back to ordinary wave behaviour rather than silently doing nothing.");
                enabled = false;
            }
        }

        private void OnDisable()
        {
            // Hand the target authority back. A pooled body reused as an ordinary marcher must
            // not inherit a pinned wall — the brain would refuse to score the Heart forever.
            if (_brain != null) _brain.ClearStructureFocus();
            _focus = null;
            _reportedFocus = false;
        }

        private void Update()
        {
            if (_enemy == null || _enemy.IsDead) return;

            _repickTimer -= Time.deltaTime;
            bool focusValid = _focus != null && _focus.IsAlive;
            if (focusValid && _repickTimer > 0f) return;

            if (!focusValid && _focus != null)
            {
                // The panel came down. That is the SUCCESS case for this unit, so it is a Step
                // and not a Warn — and it is captured, because "did the breachers actually
                // break anything" is the first question a felt-test raises.
                FlowTrace.Step(EndlessPressure.Sys,
                    $"wave {_trueWave}: breacher '{_enemy.EnemyId}' brought its wall panel DOWN — re-picking.");
                _focus = null;
                _reportedFocus = false;
            }

            _repickTimer = RepickSeconds;

            WallSegment next = EndlessPressure.NearestBreachTarget(
                transform.position, _enemy.SelfFaction);

            if (next == null)
            {
                // No standing wall the town over. Release the focus so the brain resumes its
                // normal scoring (Heart / towers / hero) — a breacher with nothing to breach is
                // an ordinary attacker, not a stalled one. Once-per-body so a wall-less town
                // (or a raid scene) cannot flood the log.
                if (_brain != null) _brain.ClearStructureFocus();
                if (_focus != null || !_reportedFocus)
                {
                    FlowTrace.Once(EndlessPressure.Sys, $"nobreach-{GetInstanceID()}",
                        $"wave {_trueWave}: breacher '{_enemy.EnemyId}' found NO attackable wall panel " +
                        "(town has none standing, or every panel is same-faction) — reverting to normal targeting.");
                    _reportedFocus = true;
                }
                _focus = null;
                return;
            }

            if (ReferenceEquals(next, _focus)) return;

            _focus = next;

            if (_brain != null)
            {
                _brain.SetStructureFocus(next.transform);
            }
            else
            {
                // Brain-less body: Enemy.DriveNav follows _brainTarget and
                // Enemy.ProbeForStructure lands the contact damage on arrival.
                _enemy.SetBrainTarget(next.transform);
            }

            // The acceptance criterion for this piece is "targets a WallSegment, NOT the Heart",
            // so the captured line names the picked type explicitly — that is the proving line a
            // felt-test or a headless run is read against, per CLAUDE.md §12.
            float dist = Vector3.Distance(transform.position, next.transform.position);
            string via = _brain != null ? "EnemyBrain.SetStructureFocus" : "Enemy.SetBrainTarget (brain-less)";
            FlowTrace.Step(EndlessPressure.Sys,
                $"wave {_trueWave}: breacher '{_enemy.EnemyId}' TARGETS WallSegment " +
                $"'{next.SegmentId}' at {dist:0.0}m (hp {next.Hp:0}/{WallSegment.MaxHp:0}, " +
                $"tier {next.Tier}) via {via} — NOT the Heart.");
            _reportedFocus = true;
        }
    }
}
