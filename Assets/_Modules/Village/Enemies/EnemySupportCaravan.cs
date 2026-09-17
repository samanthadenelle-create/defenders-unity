// =============================================================================
// EnemySupportCaravan — the ENEMY healing caravan (WO-1835, piece 3)
// -----------------------------------------------------------------------------
// OWNER RULING (2026-09-17): past wave 20 the attackers should field "healing caravans".
//
// ⛔ THIS IS NOT CaravanHealField / HealingCaravanMobility. Those two are the PLAYER's own
// defensive caravan structure (Assets/_Modules/Village/Buildings/) — they heal the
// player's side and one of them is an IDamageableStructure the enemy attacks. WO-1835
// names the collision explicitly and forbids reusing or touching them. This is the
// enemy-side mirror of the INTENT and shares no code with either, which is why it carries
// a deliberately distinct name.
//
// WHAT IT IS: a body that never attacks and continuously mends the squad around it. That
// makes it the wave's soft spot — the player who ignores it watches a push she was winning
// heal back up, and the player who kills it first collapses the push. That readability IS
// the design; it is why the heal is an AREA pulse with a visible ground ring rather than a
// quiet single-target trickle (the brain's Healer role already does the quiet version, and
// nobody can see it happening).
//
// ⚠ NON-COMBATANT BY CONSTRUCTION, NOT BY OMISSION. The body is stamped EnemyRole.Healer
// with the shared SupportTactics archetype, which is the existing "clusters with wounded
// allies instead of charging solo" posture (EnemyBrain.SupportTactics). We do not disable
// the brain to keep it passive: a disabled brain also stops it walking, and a caravan that
// stands at its spawn point for the whole wave never reaches the squad it exists to heal.
// =============================================================================
using UnityEngine;
using DeNelle.Core.Diagnostics;
// NOTE: VfxPool / PooledVfx live in the DeNelle.Village namespace itself (Vfx/VfxPool.cs:30),
// NOT in a DeNelle.Village.Vfx namespace — there is no such namespace in the tree. Same for
// WallSegment (Walls/WallSegment.cs:50). This file is already in DeNelle.Village, so no using.

namespace DeNelle.Village
{
    /// <summary>
    /// Enemy-side support unit: heals every nearby attacking enemy on a pulse and never
    /// deals damage. Added at release time by <see cref="EndlessPressure.Attach"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySupportCaravan : MonoBehaviour
    {
        // FIRST-PASS TUNING — OWNER FELT-TUNES. Deliberately gentle: the caravan is meant to
        // extend a push, not make it unkillable. At 2.5 s and 4% of max HP a caravan restores
        // roughly 1.6%/s across its band, so a player putting normal pressure on a target
        // still out-damages it and a player ignoring the whole wave does not.
        private const float PulseSeconds = 2.5f;
        private const float HealFractionPerPulse = 0.04f;
        private const float HealRadius = 9f;
        private const int ScanBufferSize = 32;

        private readonly Collider[] _scan = new Collider[ScanBufferSize];

        private Enemy _enemy;
        private int _trueWave;
        private float _pulseTimer;
        private PooledVfx _ring;
        private int _pulsesDelivered;
        private float _hpRestoredTotal;

        /// <summary>Total HP this caravan has put back into the wave (read by the oracle + captures).</summary>
        public float HpRestoredTotal => _hpRestoredTotal;

        /// <summary>
        /// Binds the component to its host body. Idempotent and safe on a POOLED body being
        /// leased for a new life — every per-life field is re-stamped, which is what lets
        /// <see cref="EndlessPressure.Attach"/> reuse an existing component rather than churn
        /// AddComponent on a recycled GameObject.
        /// </summary>
        public void Configure(Enemy enemy, int trueWave)
        {
            _enemy = enemy != null ? enemy : GetComponent<Enemy>();
            _trueWave = trueWave;
            _pulseTimer = PulseSeconds;
            _pulsesDelivered = 0;
            _hpRestoredTotal = 0f;

            if (_enemy == null)
            {
                FlowTrace.Fail(EndlessPressure.Sys,
                    $"EnemySupportCaravan on '{name}' has no Enemy component — it can neither be " +
                    "killed as a priority nor heal anything; disabling rather than standing there inert.");
                enabled = false;
                return;
            }

            // Passive posture through the EXISTING archetype, not a new one.
            var brain = GetComponent<EnemyBrain>();
            if (brain != null)
            {
                brain.Role = EnemyRole.Healer;
                EnemyBrain.ApplyRoleTactics(brain, EnemyRole.Healer);
            }
            else
            {
                // No brain means Enemy.DriveNav marches it at the Heart and ProbeForStructure
                // lets it hit things — a caravan that fights. Not fatal (it still heals), but it
                // breaks the read, so it is captured rather than swallowed (CLAUDE.md §12).
                FlowTrace.Warn(EndlessPressure.Sys,
                    $"wave {trueWave}: caravan '{_enemy.EnemyId}' has NO EnemyBrain — it cannot be held " +
                    "to the passive Healer posture and will march/strike like an ordinary body.");
            }
        }

        private void OnDisable()
        {
            VfxPool.ReturnTelegraph(_ring);
            _ring = null;
        }

        private void Update()
        {
            if (_enemy == null || _enemy.IsDead)
            {
                if (_ring != null) { VfxPool.ReturnTelegraph(_ring); _ring = null; }
                return;
            }

            _pulseTimer -= Time.deltaTime;
            if (_pulseTimer > 0f) return;
            _pulseTimer = PulseSeconds;

            Pulse();
        }

        /// <summary>
        /// One heal pulse: mends every wounded ally in the band and shows the ground ring so
        /// the player can SEE which body is doing it. Guarded — a pulse that throws must not
        /// take the caravan (or the wave) down with it.
        /// </summary>
        private void Pulse()
        {
            Guard.Try(EndlessPressure.Sys, "enemy support caravan heal pulse", () =>
            {
                int count = Physics.OverlapSphereNonAlloc(transform.position, HealRadius, _scan);
                int healed = 0;
                float restored = 0f;

                for (int i = 0; i < count; i++)
                {
                    if (_scan[i] == null) continue;
                    var ally = _scan[i].GetComponentInParent<Enemy>();
                    if (ally == null || ally == _enemy || ally.IsDead) continue;
                    if (ally.HpFraction >= 0.999f) continue;

                    float before = ally.Hp;
                    ally.Heal(ally.MaxHp * HealFractionPerPulse);
                    float gained = ally.Hp - before;
                    if (gained > 0f) { healed++; restored += gained; }
                }

                _pulsesDelivered++;
                _hpRestoredTotal += restored;

                // The ring is shown only when the caravan ACTUALLY mended something, so the tell
                // never lies about whether the unit is contributing.
                if (healed > 0)
                {
                    if (_ring == null) _ring = VfxPool.GetTelegraph(transform.position);
                    else _ring.transform.position = transform.position;
                }
                else if (_ring != null)
                {
                    VfxPool.ReturnTelegraph(_ring);
                    _ring = null;
                }

                // Throttled, not per-pulse-unconditional: a caravan lives for a whole wave and an
                // unthrottled line here would be ~24 lines/minute per caravan, which is how a
                // device logcat ring evicts the boot window (memory: logcat-ring-buffer-destroys-evidence).
                FlowTrace.Throttle(EndlessPressure.Sys, $"caravan-{GetInstanceID()}", 5f,
                    $"wave {_trueWave}: caravan '{_enemy.EnemyId}' pulse #{_pulsesDelivered} mended " +
                    $"{healed} ally(ies) for {restored:0.#} HP ({_hpRestoredTotal:0.#} HP total this life, " +
                    $"radius {HealRadius:0.#}m) — kill it to stop the push healing.");
            });
        }
    }
}
