// =============================================================================
// EnemySupportMage — AoE heal + regen + rage caster (WO-1835, piece 4)
// -----------------------------------------------------------------------------
// OWNER RULING (2026-09-17, verbatim): past wave 20 the attackers field "mages using AoE
// large heal spells, rage spells".
// OWNER ADDITION (2026-09-17, same day, relayed mid-implementation): "their healers should
// cast AoE heal spells and regen". REGEN IS A SEPARATE ASK FROM THE HEAL CAST, and this
// class implements it as a separate mechanism rather than folding it into the burst:
//
//   • THE CAST  — a big, telegraphed, instantaneous AoE. Loud, interruptible-looking,
//                 and the thing the player learns to punish. Alternates heal / rage.
//   • THE REGEN — a quiet always-on heal-over-time aura on a 1 s tick. Small per tick,
//                 meaningful over a wave. This is the "and regen" half; a player who
//                 trades slowly with a pack under a mage loses the trade even between casts.
//
// Folding regen into the burst would have satisfied neither half: one big number every nine
// seconds is not regeneration, and a continuous trickle is not a spell you can see coming.
//
// TELEGRAPH FIRST, per WO-1835 and the project's existing pattern: VfxPool.GetTelegraph is
// the same ground-ring reservation SpawnBatch uses for its DEF-52 spawn warning. The player
// gets a wind-up ring at the caster's feet before either spell lands, so a punished cast is
// the player's skill and an unpunished one is her choice — never a surprise.
//
// §16 ART NOTE: rides the existing orc-shaman def (real R2 bundle, no new content hash).
// See the header of EndlessPressure.cs for why no new enemies.json def is introduced.
// =============================================================================
using UnityEngine;
using DeNelle.Core.Diagnostics;
// NOTE: VfxPool / PooledVfx live in the DeNelle.Village namespace itself (Vfx/VfxPool.cs:30),
// NOT in a DeNelle.Village.Vfx namespace — there is no such namespace in the tree.

namespace DeNelle.Village
{
    /// <summary>
    /// Enemy support caster: telegraphed AoE heal burst, alternating telegraphed rage buff,
    /// and a continuous low-rate regen aura. Added by <see cref="EndlessPressure.Attach"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySupportMage : MonoBehaviour
    {
        // ── FIRST-PASS TUNING — OWNER FELT-TUNES (WO-1835 flags every number here as an
        //    estimate for her to correct, not a final balance figure). ──────────────────────
        /// <summary>Wind-up the player can see and react to before a spell lands.</summary>
        private const float TelegraphSeconds = 1.2f;
        /// <summary>AoE burst heal, as a share of each ally's max HP. "Large", per the ruling.</summary>
        private const float BurstHealFraction = 0.22f;
        /// <summary>Continuous regen, as a share of max HP per second.</summary>
        private const float RegenFractionPerSecond = 0.012f;
        /// <summary>Outgoing-damage multiplier granted by the rage cast.</summary>
        private const float RageDamageMultiplier = 1.5f;
        /// <summary>How long a rage buff lasts. Shorter than the cast cycle, so it has downtime.</summary>
        private const float RageSeconds = 6f;
        /// <summary>Band for both spells and the regen aura.</summary>
        private const float SpellRadius = 11f;
        private const float RegenTickSeconds = 1f;
        private const int ScanBufferSize = 32;

        private readonly Collider[] _scan = new Collider[ScanBufferSize];

        private Enemy _enemy;
        private int _trueWave;
        private float _castSeconds = 9f;
        private float _castTimer;
        private float _regenTimer;
        private bool _telegraphing;
        private PooledVfx _telegraph;
        private int _castsFired;

        /// <summary>Number of completed casts this life (read by captures + the oracle).</summary>
        public int CastsFired => _castsFired;

        /// <summary>
        /// Which spell the cast at <paramref name="castIndex"/> is. Pure + deterministic so the
        /// alternation is assertable without a scene: even casts HEAL, odd casts RAGE. The mage
        /// opens on a heal because a pack that arrives already wounded is the common case, and
        /// opening on rage would make the unit read as a damage buffer rather than a healer.
        /// </summary>
        public static bool CastIsHeal(int castIndex) => (castIndex & 1) == 0;

        /// <summary>
        /// Binds the component to its host body. Idempotent and safe on a POOLED body being
        /// leased for a new life — every per-life field is re-stamped here.
        /// </summary>
        public void Configure(Enemy enemy, int trueWave, EndlessPressureTuning tuning)
        {
            _enemy = enemy != null ? enemy : GetComponent<Enemy>();
            _trueWave = trueWave;
            _castSeconds = Mathf.Max(2f, tuning != null ? tuning.MageCastSeconds : 9f);
            _castTimer = _castSeconds;
            _regenTimer = RegenTickSeconds;
            _telegraphing = false;
            _castsFired = 0;

            if (_enemy == null)
            {
                FlowTrace.Fail(EndlessPressure.Sys,
                    $"EnemySupportMage on '{name}' has no Enemy component — it can cast nothing; " +
                    "disabling rather than standing there inert.");
                enabled = false;
                return;
            }

            // Stand off and support rather than charge, through the EXISTING archetype.
            var brain = GetComponent<EnemyBrain>();
            if (brain != null)
            {
                brain.Role = EnemyRole.Healer;
                EnemyBrain.ApplyRoleTactics(brain, EnemyRole.Healer);
            }
            else
            {
                FlowTrace.Warn(EndlessPressure.Sys,
                    $"wave {trueWave}: support mage '{_enemy.EnemyId}' has NO EnemyBrain — it will still " +
                    "cast, but it marches and strikes like an ordinary body instead of holding the back line.");
            }
        }

        private void OnDisable()
        {
            VfxPool.ReturnTelegraph(_telegraph);
            _telegraph = null;
            _telegraphing = false;
        }

        private void Update()
        {
            if (_enemy == null || _enemy.IsDead)
            {
                if (_telegraph != null) { VfxPool.ReturnTelegraph(_telegraph); _telegraph = null; }
                _telegraphing = false;
                return;
            }

            TickRegen();
            TickCast();
        }

        /// <summary>
        /// The "and regen" half of the ruling: a quiet heal-over-time on every nearby ally,
        /// running independently of the cast cycle so the pack is mending even between spells.
        /// </summary>
        private void TickRegen()
        {
            _regenTimer -= Time.deltaTime;
            if (_regenTimer > 0f) return;
            _regenTimer = RegenTickSeconds;

            Guard.Try(EndlessPressure.Sys, "support mage regen tick", () =>
            {
                float restored = HealBand(RegenFractionPerSecond * RegenTickSeconds, out int healed);
                if (healed <= 0) return;

                // Throttled hard: this is a 1 Hz tick per mage and an unthrottled line would
                // flood a device logcat and evict the boot window
                // (memory: logcat-ring-buffer-destroys-evidence).
                FlowTrace.Throttle(EndlessPressure.Sys, $"mageregen-{GetInstanceID()}", 6f,
                    $"wave {_trueWave}: mage '{_enemy.EnemyId}' REGEN aura mending {healed} ally(ies) " +
                    $"(+{restored:0.#} HP this tick, {RegenFractionPerSecond * 100f:0.#}%/s, {SpellRadius:0.#}m).");
            });
        }

        /// <summary>
        /// The cast cycle: wind up a visible telegraph, then land the spell. Split across two
        /// Update passes rather than a coroutine so a body returned to the pool mid-wind-up
        /// cannot leave a coroutine (and a held telegraph) running — the same failure shape
        /// Enemy.ResetForPool documents for RootedCast.
        /// </summary>
        private void TickCast()
        {
            _castTimer -= Time.deltaTime;

            if (!_telegraphing && _castTimer <= TelegraphSeconds)
            {
                _telegraphing = true;
                _telegraph = VfxPool.GetTelegraph(transform.position);
                // CLAUDE.md §1: computed into a LOCAL, not a nested string literal inside an
                // interpolation hole — the compile gate's brace checker has no interpolated-string
                // model and a `"` inside a `{ cond ? "a" : "b" }` hole makes it mis-scan the rest of
                // the file as code, withholding COMPILE_GATE_OK on valid C#.
                string spellWord = CastIsHeal(_castsFired) ? "AoE HEAL" : "RAGE";
                FlowTrace.Step(EndlessPressure.Sys,
                    $"wave {_trueWave}: mage '{_enemy.EnemyId}' TELEGRAPHING its " +
                    $"{spellWord} cast — {TelegraphSeconds:0.0}s wind-up, " +
                    $"{SpellRadius:0.#}m band. Punish window open.");
                return;
            }

            if (_castTimer > 0f) return;

            _castTimer = _castSeconds;
            _telegraphing = false;
            VfxPool.ReturnTelegraph(_telegraph);
            _telegraph = null;

            if (CastIsHeal(_castsFired)) CastAoeHeal();
            else CastRage();

            _castsFired++;
        }

        /// <summary>The "AoE large heal" cast: one big instantaneous mend across the band.</summary>
        private void CastAoeHeal()
        {
            Guard.Try(EndlessPressure.Sys, "support mage AoE heal cast", () =>
            {
                float restored = HealBand(BurstHealFraction, out int healed);
                FlowTrace.Step(EndlessPressure.Sys,
                    $"wave {_trueWave}: mage '{_enemy.EnemyId}' CAST AoE HEAL — mended {healed} ally(ies) " +
                    $"for {restored:0.#} HP ({BurstHealFraction * 100f:0.#}% of max each, {SpellRadius:0.#}m).");
            });
        }

        /// <summary>The "rage" cast: a transient outgoing-damage buff on every nearby attacker.</summary>
        private void CastRage()
        {
            Guard.Try(EndlessPressure.Sys, "support mage rage cast", () =>
            {
                int count = Physics.OverlapSphereNonAlloc(transform.position, SpellRadius, _scan);
                int buffed = 0;
                for (int i = 0; i < count; i++)
                {
                    if (_scan[i] == null) continue;
                    var ally = _scan[i].GetComponentInParent<Enemy>();
                    if (ally == null || ally.IsDead) continue;
                    ally.ApplyRage(RageDamageMultiplier, RageSeconds);
                    buffed++;
                }

                FlowTrace.Step(EndlessPressure.Sys,
                    $"wave {_trueWave}: mage '{_enemy.EnemyId}' CAST RAGE — x{RageDamageMultiplier:0.0#} " +
                    $"outgoing damage on {buffed} ally(ies) for {RageSeconds:0.#}s ({SpellRadius:0.#}m band).");
            });
        }

        /// <summary>
        /// Heals every wounded living ally in the band by <paramref name="fractionOfMax"/> of
        /// its own max HP. Shared by the burst and the regen tick so there is ONE band scan and
        /// ONE heal rule — the two differ only in magnitude and cadence, which is exactly what
        /// "a heal spell and a regen" means.
        /// </summary>
        private float HealBand(float fractionOfMax, out int healed)
        {
            healed = 0;
            float restored = 0f;
            if (fractionOfMax <= 0f) return 0f;

            int count = Physics.OverlapSphereNonAlloc(transform.position, SpellRadius, _scan);
            for (int i = 0; i < count; i++)
            {
                if (_scan[i] == null) continue;
                var ally = _scan[i].GetComponentInParent<Enemy>();
                if (ally == null || ally.IsDead) continue;
                if (ally.HpFraction >= 0.999f) continue;

                float before = ally.Hp;
                ally.Heal(ally.MaxHp * fractionOfMax);
                float gained = ally.Hp - before;
                if (gained > 0f) { healed++; restored += gained; }
            }
            return restored;
        }
    }
}
