// =============================================================================
// WaveScalingCurve (DEF-59) — ScriptableObject that defines how enemy stats
// scale as the wave number increases.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT DOES:
//   Three AnimationCurves (HP, speed, contact-damage) keyed on wave number
//   (x-axis). WaveManager samples them in SpawnOne() after Configure() and
//   calls Enemy.ApplyWaveScaling() to boost the fresh instance. Because the
//   scaling is applied AFTER Configure the enemy's base values always come from
//   enemies.json, and the curve is a pure multiplier on top — easy to tune in
//   the SO inspector without touching data files.
//
// DEFAULT CURVES (set in Reset so a freshly-created SO is immediately usable):
//   HP     : 1.0× at wave 1 → 2.5× at wave 20 (linear, clamped to 20)
//   Speed  : 1.0× at wave 1 → 1.4× at wave 20
//   Damage : 1.0× at wave 1 → 2.0× at wave 20
//
//   Beyond wave 20 all three curves clamp at their final value (WrapMode.Clamp).
//   Tune freely in the Inspector — the curves are read at runtime with
//   AnimationCurve.Evaluate(waveNumber), so any shape works.
//
// ⛔ WO-1773 (2026-09-16): THAT CLAMP IS WHY WAVE 176 IS NUMERICALLY WAVE 60.
//   An external tester stood still at wave 176 and could not be damaged. The clamp
//   was measured to the DIGIT off his screen: three enemies' on-screen max HP matched
//   their authored catalog HP times the clamped multiplier exactly. HpMultiplier and
//   DamageMultiplier now fold a REMOTE-TUNABLE post-clamp growth term
//   (WaveDifficultyTunables, keys wave.hpGrowthPctPerWave / wave.dmgGrowthPctPerWave)
//   on top of the clamped value. The growth DEFAULTS TO ZERO, so with no database row
//   this file behaves exactly as it did before - identity, bit for bit. The band it
//   grows from is read off each curve's LAST KEYFRAME (GrowthBandStart), never written
//   down, so a tuned asset cannot disagree with a pasted literal.
//   SpeedMultiplier is deliberately NOT folded - see its own summary.
//
// USAGE:
//   1. Create an asset: Assets → Create → Defenders / Waves / Wave Scaling Curve
//   2. Assign it to WaveManager._scalingCurve in the Inspector.
//   3. Done — WaveManager will sample it on every enemy spawn.
// =============================================================================

using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// Defines how enemy stats (HP, speed, contact damage) scale with the wave
    /// number. Assign to <see cref="WaveManager"/> and tune the curves freely.
    /// </summary>
    [CreateAssetMenu(
        fileName = "WaveScalingCurve",
        menuName  = "Defenders/Waves/Wave Scaling Curve")]
    public sealed class WaveScalingCurve : ScriptableObject
    {
        [Tooltip("HP multiplier as a function of wave number (x = wave, y = ×). " +
                 "Default: 1.0 at wave 1 → 2.5 at wave 20.")]
        public AnimationCurve HpCurve = DefaultHp();

        [Tooltip("Move-speed multiplier as a function of wave number. " +
                 "Default: 1.0 at wave 1 → 1.4 at wave 20.")]
        public AnimationCurve SpeedCurve = DefaultSpeed();

        [Tooltip("Contact-damage multiplier as a function of wave number. " +
                 "Default: 1.0 at wave 1 → 2.0 at wave 20.")]
        public AnimationCurve DamageCurve = DefaultDamage();

        // ── Evaluate helpers — called by WaveManager ──────────────────────────

        /// <summary>
        /// HP multiplier for <paramref name="wave"/> (1-based wave number), with the WO-1773
        /// post-clamp growth folded in. Identity (this curve's own value, unchanged) until a
        /// <c>wave.hpGrowthPctPerWave</c> row exists.
        /// </summary>
        public float HpMultiplier(int wave)
            => Mathf.Max(1f, WaveDifficultyTunables.FoldHpMultiplier(
                   HpCurve.Evaluate(wave), wave, GrowthBandStart(HpCurve)));

        /// <summary>
        /// Speed multiplier for <paramref name="wave"/> (1-based wave number).
        ///
        /// <para>DELIBERATELY NOT TUNABLE, and that is a scope decision rather than an omission.
        /// WO-1773 asked for STRENGTH (HP, damage) and COUNT growth. Enemy move speed is a
        /// navigation and animation property as much as a difficulty one - past a point it reads
        /// as a bug, outruns the hero's own locomotion, and interacts with the NavMeshAgent
        /// avoidance the concurrency cap exists to protect. If the owner wants it, it gets its own
        /// knob and its own ceiling, not a share of the HP one.</para>
        /// </summary>
        public float SpeedMultiplier(int wave) => Mathf.Max(0.5f, SpeedCurve.Evaluate(wave));

        /// <summary>
        /// Contact-damage multiplier for <paramref name="wave"/>, with the WO-1773 post-clamp
        /// growth folded in. Identity until a <c>wave.dmgGrowthPctPerWave</c> row exists.
        /// </summary>
        public float DamageMultiplier(int wave)
            => Mathf.Max(1f, WaveDifficultyTunables.FoldDamageMultiplier(
                   DamageCurve.Evaluate(wave), wave, GrowthBandStart(DamageCurve)));

        /// <summary>
        /// The wave at which <paramref name="c"/> stops growing on its own, i.e. its LAST KEYFRAME
        /// TIME - the boundary WO-1773's post-clamp growth is measured from.
        ///
        /// <para>⛔ READ OFF THE CURVE, NEVER WRITTEN DOWN. These curves are public and
        /// Inspector-editable, and a tuned asset would silently disagree with a literal 20 pasted
        /// into a consumer - the duplicated-state failure CLAUDE.md sections 2 / 5 / 8 each record a
        /// scar from.</para>
        ///
        /// <para>Returns <see cref="int.MaxValue"/> ("never grow") when the curve is EMPTY or when
        /// its <c>postWrapMode</c> is not <see cref="WrapMode.Clamp"/>: a Loop or PingPong curve is
        /// already producing new values past its last key, so adding growth on top would
        /// double-count. Rounded UP so a fractional keyframe time cannot let one wave slip a tiny
        /// growth term in before the clamp actually binds.</para>
        /// </summary>
        public static int GrowthBandStart(AnimationCurve c)
        {
            if (c == null) return int.MaxValue;
            int n = c.length;
            if (n <= 0) return int.MaxValue;
            if (c.postWrapMode != WrapMode.Clamp && c.postWrapMode != WrapMode.ClampForever)
                return int.MaxValue;
            float lastTime = c[n - 1].time;
            if (float.IsNaN(lastTime) || float.IsInfinity(lastTime)) return int.MaxValue;
            return Mathf.Max(1, Mathf.CeilToInt(lastTime));
        }

        // ── Default curve factories ───────────────────────────────────────────

        private static AnimationCurve DefaultHp()
        {
            var c = new AnimationCurve(
                new Keyframe(0f,  1.0f),
                new Keyframe(20f, 2.5f));
            c.preWrapMode  = WrapMode.Clamp;
            c.postWrapMode = WrapMode.Clamp;
            return c;
        }

        private static AnimationCurve DefaultSpeed()
        {
            var c = new AnimationCurve(
                new Keyframe(0f,  1.0f),
                new Keyframe(20f, 1.4f));
            c.preWrapMode  = WrapMode.Clamp;
            c.postWrapMode = WrapMode.Clamp;
            return c;
        }

        private static AnimationCurve DefaultDamage()
        {
            var c = new AnimationCurve(
                new Keyframe(0f,  1.0f),
                new Keyframe(20f, 2.0f));
            c.preWrapMode  = WrapMode.Clamp;
            c.postWrapMode = WrapMode.Clamp;
            return c;
        }

        private void Reset()
        {
            HpCurve     = DefaultHp();
            SpeedCurve  = DefaultSpeed();
            DamageCurve = DefaultDamage();
        }
    }
}
