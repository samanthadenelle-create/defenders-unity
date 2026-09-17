// =============================================================================
// WaveDifficultyTunables - the ONE reader of the WO-1773 TOWN-WAVE difficulty knobs,
// and the owner of their clamps.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Rail: DeNelle.Core.Ops.RemoteTunables - reused end to end, not re-invented, exactly
// as RaidDifficultyTunables (WO-1763) reuses it. Same ResolveFrom-style
// knob-values-supplied shape, same ClampAndReport, same identity short-circuit.
//
// -----------------------------------------------------------------------------
// WHY THIS EXISTS: WAVE 176 IS NUMERICALLY IDENTICAL TO WAVE 60.
// -----------------------------------------------------------------------------
// Owner direction, 2026-09-16, verbatim: "enemies need to really start scaling with
// wave i thought, or massive swarms at all sides".
//
// DEAD STEP B, measured by WO-1773 off an external tester's wave-176 recording:
//   - WaveScalingCurve's three curves end at a keyframe at wave 20 with
//     postWrapMode = WrapMode.Clamp, so every wave past 20 evaluates as wave 20. The
//     ticket proved this to the DIGIT: three enemies' on-screen max HP matched their
//     authored catalog HP times the clamped multiplier exactly.
//   - The roster total clamps at WaveCompositionBuilder.MaxCount from wave 21.
//   - The endless count multiplier clamps at waves.json's countCap, binding from
//     wave 60.
//   - The concurrency cap never varied with the wave at all.
//
// So a player 116 waves past the last thing that got harder is fighting wave 60.
//
// -----------------------------------------------------------------------------
// THE SHAPE IS THE DRAGON'S OWN, BECAUSE THE DRAGON IS THE ONE THING STILL GROWING.
// -----------------------------------------------------------------------------
// WaveManager's recurring-dragon block grows the dragon's HP and damage LINEARLY and
// UNCAPPED per return, while the entire regular roster is clamped. The pattern the
// owner asked for already exists in this codebase - it was only ever applied to one
// unit. This file applies that same shape to the rest of the wave.
//
// (!) ADDITIVE, NOT MULTIPLICATIVE, AND THAT IS A RULING - read it before seeding a
// number. The growth is added to the CLAMPED multiplier:
//
//       effective(w) = clamped + (pct / 100) * (w - bandStart)     for w > bandStart
//
// NOT clamped * (1 + k*(w - bandStart)). The two read identically at small numbers
// and diverge hard at wave 176: with a clamped damage multiplier of 2.0 and pct=5,
// additive gives 9.8 and multiplicative gives 17.6. WO-1773's own arithmetic table -
// the one the owner will pick a seed off - is the ADDITIVE column, and WO-1763's
// REPLACE-vs-ADD paragraph records why a ruling that can be read two ways is itself
// the failure. If the owner wants the multiplicative shape she says so and this
// method changes; nobody infers it from a number.
//
// -----------------------------------------------------------------------------
// bandStart IS READ OFF THE CURVE, NEVER WRITTEN HERE.
// -----------------------------------------------------------------------------
// The band starts at the curve's LAST KEYFRAME TIME. Writing 20 into this file would
// be exactly the duplicated state that rotted CLAUDE.md sections 2 / 5 / 8 - and the
// curves are public and Inspector-editable, so a tuned asset would silently disagree
// with the literal. WaveScalingCurve.GrowthBandStart does the read, and it returns
// "never" unless postWrapMode is Clamp, because a Loop or PingPong curve is already
// growing on its own and adding to it would double-count.
//
// -----------------------------------------------------------------------------
// EVERY DEFAULT IS EXACT IDENTITY. The code ships INERT.
// -----------------------------------------------------------------------------
// Growth percents default to 0 and every count/concurrency percent defaults to 100,
// and each of those cases SHORT-CIRCUITS and returns the supplied value itself rather
// than value*100/100f. An empty client_tunables table is today's town, bit for bit.
// Only a database row changes the feel - no rebuild, per the WO-1763 rail.
//
// NO NUMBER FROM WaveScalingCurve, WaveCompositionBuilder, waves.json OR THE SCENE IS
// RESTATED ANYWHERE IN THIS FILE. Every baseline arrives as a PARAMETER.
//
// Pure and static: no scene, no save, no network, no MonoBehaviour. An oracle asserts
// the whole table with nothing loaded.
//
// ASCII only. FlowTrace tag "Waves". Never stripped (CLAUDE.md section 12).
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;

namespace DeNelle.Village
{
    /// <summary>
    /// Resolves and clamps the town-wave difficulty knobs: post-clamp STRENGTH growth (HP and
    /// contact damage, separately), roster COUNT ceilings, and the on-screen concurrency cap.
    /// Pure static; with no row, no network and no parse every method answers the value it was
    /// PASSED, unchanged.
    /// </summary>
    public static class WaveDifficultyTunables
    {
        // ── STRENGTH growth, in HUNDREDTHS OF A MULTIPLIER PER WAVE ──────────────────────
        // The knob unit is "percent points of multiplier per wave": 5 means +0.05 on the
        // multiplier for every wave past the band start. Int because the whole rail is int
        // (RemoteTunables.TunableKind.Int) and a percent is the unit every other knob on it uses.

        /// <summary>No growth. The shipping default on both strength axes, and exact identity.</summary>
        public const int MinGrowthPctPerWave = 0;

        /// <summary>
        /// Ceiling on per-wave growth. 100 = +1.00 on the multiplier per wave, which at 156 waves
        /// past the band is already a 150x enemy. Anything beyond is not a difficulty curve, it is
        /// an arithmetic overflow waiting for a bigger number.
        /// </summary>
        public const int MaxGrowthPctPerWave = 100;

        /// <summary>
        /// Absolute ceiling on the EFFECTIVE strength multiplier, whatever the growth and the wave.
        /// A hard stop exists because this is an operator surface on an UNBOUNDED input (the wave
        /// number has no clamp - WaveManager's own comment says BestWave is only floored at 0), so
        /// without it a single typo produces enemies with more HP than a float can usefully carry
        /// and a wave that can never be cleared. Loud when it bites.
        /// </summary>
        public const float MaxEffectiveMultiplier = 200f;

        // ── COUNT / CONCURRENCY percents ─────────────────────────────────────────────────

        /// <summary>Lowest count percent (a quarter of the authored ceiling).</summary>
        public const int MinCountPct = 25;

        /// <summary>
        /// Highest count percent (ten times the authored ceiling). Generous on purpose: the ROSTER
        /// is the cheap half of "massive swarms" - bodies arrive as reinforcements inside the
        /// concurrency budget, so a bigger roster costs arrival time, not frame time.
        /// </summary>
        public const int MaxCountPct = 1000;

        /// <summary>Lowest concurrency percent.</summary>
        public const int MinSimultaneousPct = 25;

        /// <summary>
        /// Highest concurrency percent. Four times, NOT ten - and the reason is a frame budget, not
        /// a design opinion. SmartEnemySpawner's header records that PartitionCount exists precisely
        /// so N sides can never exceed the one-side cap, because of the WO-1113 phone frame-rate
        /// cliff. This is the ONE knob in this file that can regress the Seeker build's frame rate,
        /// and CLAUDE.md section 12 requires FlowTrace.Measure("Perf", ..., 4f, 1f) evidence naming
        /// the dominant cost in ms before and after it is raised.
        /// </summary>
        public const int MaxSimultaneousPct = 400;

        /// <summary>The percent at which every count/concurrency method hands back its input untouched.</summary>
        public const int IdentityPct = RemoteTunables.WaveCountPctIdentity;

        // =====================================================================
        //  STRENGTH - the post-clamp growth
        // =====================================================================

        /// <summary>
        /// The EFFECTIVE enemy HP multiplier for <paramref name="wave"/>, given the curve's own
        /// (already clamped) evaluation and where the clamp band begins. Reads the knob off the rail.
        /// </summary>
        public static float FoldHpMultiplier(float clampedMultiplier, int wave, int bandStartWave)
            => FoldStrengthFrom(clampedMultiplier, wave, bandStartWave,
                                RemoteTunables.Int(RemoteTunables.KeyWaveHpGrowthPctPerWave),
                                RemoteTunables.KeyWaveHpGrowthPctPerWave);

        /// <summary>
        /// The EFFECTIVE enemy CONTACT-DAMAGE multiplier for <paramref name="wave"/>. A separate
        /// knob from HP on purpose: scaling HP alone lengthens fights without adding threat, and
        /// scaling damage alone makes enemies into fragile glass cannons. They are different feels
        /// and the owner needs to move one without the other.
        /// </summary>
        public static float FoldDamageMultiplier(float clampedMultiplier, int wave, int bandStartWave)
            => FoldStrengthFrom(clampedMultiplier, wave, bandStartWave,
                                RemoteTunables.Int(RemoteTunables.KeyWaveDmgGrowthPctPerWave),
                                RemoteTunables.KeyWaveDmgGrowthPctPerWave);

        /// <summary>
        /// The pure arithmetic, with the knob value SUPPLIED, so an oracle drives every branch with
        /// nothing loaded and no row written.
        ///
        /// <para>ADDITIVE on the clamped multiplier - see the ruling in this file's header. Returns
        /// the supplied multiplier UNCHANGED (not recomputed) whenever the growth is 0, the wave is
        /// at or below the band start, or the band start says "never" - so identity is exact.</para>
        /// </summary>
        /// <param name="clampedMultiplier">What WaveScalingCurve's own Evaluate already produced.</param>
        /// <param name="wave">The TRUE wave number, unbounded above.</param>
        /// <param name="bandStartWave">
        /// The wave at which the curve stops growing on its own. <see cref="int.MaxValue"/> means
        /// "the curve is not clamped, so never add anything" - see WaveScalingCurve.GrowthBandStart.
        /// </param>
        public static float FoldStrengthFrom(float clampedMultiplier, int wave, int bandStartWave,
                                            int rawGrowthPctPerWave, string keyForLog = null)
        {
            int pct = ClampAndReport(keyForLog, rawGrowthPctPerWave,
                                     MinGrowthPctPerWave, MaxGrowthPctPerWave, "growth percent per wave");

            // Identity, and it is a SHORT-CIRCUIT rather than an arithmetic no-op: the caller gets
            // back the exact float the curve produced, so "no row => today's wave, bit for bit" is
            // provable and not a float-rounding argument.
            if (pct <= MinGrowthPctPerWave) return clampedMultiplier;
            if (bandStartWave == int.MaxValue) return clampedMultiplier;
            if (wave <= bandStartWave) return clampedMultiplier;

            float grown = clampedMultiplier + (pct / 100f) * (wave - bandStartWave);

            if (grown > MaxEffectiveMultiplier)
            {
                // Once per (key, wave-band) rather than per spawn: this fires on every enemy of
                // every wave once it bites, and a per-spawn Warn would evict the boot window out of
                // the device logcat ring (CLAUDE.md section 12).
                FlowTrace.Once("Waves", "wavediff-mult-ceiling-" + (keyForLog ?? "supplied"),
                    "town-wave strength growth '" + (keyForLog ?? "(supplied directly)") + "' at " +
                    pct + " per wave reached x" + grown.ToString("F1") + " by wave " + wave +
                    ", above the x" + MaxEffectiveMultiplier.ToString("F0") + " ceiling - CLAMPED. " +
                    "Enemies below use the CLAMPED multiplier. This is a ceiling on an UNBOUNDED " +
                    "input (the wave number never clamps), not a design opinion: lower the growth " +
                    "percent if the curve is meant to keep climbing past here.");
                grown = MaxEffectiveMultiplier;
            }

            return grown;
        }

        // =====================================================================
        //  COUNT - the roster ceilings
        // =====================================================================

        /// <summary>
        /// The EFFECTIVE roster ceiling, given WaveCompositionBuilder's authored MaxCount. A PERCENT
        /// rather than a replacement value, so no literal from that file is restated here.
        /// </summary>
        public static int FoldMaxCount(int authoredMaxCount)
            => FoldMaxCountFrom(authoredMaxCount,
                                RemoteTunables.Int(RemoteTunables.KeyWaveMaxCountPct),
                                RemoteTunables.KeyWaveMaxCountPct);

        /// <summary>Pure. <paramref name="rawPct"/> supplied, for the oracle.</summary>
        public static int FoldMaxCountFrom(int authoredMaxCount, int rawPct, string keyForLog = null)
        {
            int pct = ClampAndReport(keyForLog, rawPct, MinCountPct, MaxCountPct, "roster count percent");
            if (pct == IdentityPct) return authoredMaxCount;
            if (authoredMaxCount <= 0) return authoredMaxCount;   // nothing to scale; never invent a roster
            // Floored at 1: a percent that rounds a real ceiling to 0 would produce an EMPTY wave,
            // which the clear-count logic reads as "already cleared" - a silent stall, not a nerf.
            return Mathf.Max(1, Mathf.RoundToInt(authoredMaxCount * (pct / 100f)));
        }

        /// <summary>
        /// The EFFECTIVE endless count CAP, given waves.json's authored countCap. This is the cap
        /// that binds first in practice - WO-1773 measured it binding from wave 60, a hundred and
        /// sixteen waves before the tester's run.
        /// </summary>
        public static float FoldCountCap(float authoredCountCap)
            => FoldCountCapFrom(authoredCountCap,
                                RemoteTunables.Int(RemoteTunables.KeyWaveCountCapPct),
                                RemoteTunables.KeyWaveCountCapPct);

        /// <summary>Pure. <paramref name="rawPct"/> supplied, for the oracle.</summary>
        public static float FoldCountCapFrom(float authoredCountCap, int rawPct, string keyForLog = null)
        {
            int pct = ClampAndReport(keyForLog, rawPct, MinCountPct, MaxCountPct, "endless count cap percent");
            if (pct == IdentityPct) return authoredCountCap;
            // A non-positive authored cap means "uncapped" to EndlessCountScale. Scaling it would
            // turn uncapped into a finite number, which is a behaviour change with no row asking
            // for one, so it is handed back untouched.
            if (!(authoredCountCap > 0f)) return authoredCountCap;
            return authoredCountCap * (pct / 100f);
        }

        // =====================================================================
        //  CONCURRENCY - the frame-budget knob, and it is labelled as one
        // =====================================================================

        /// <summary>
        /// The EFFECTIVE on-screen concurrency cap, given the scene's serialized value.
        ///
        /// <para>(!) This is the ONE knob here that can regress phone frame rate - see
        /// <see cref="MaxSimultaneousPct"/>. Raising it needs measured evidence, not an opinion.</para>
        /// </summary>
        public static int FoldMaxSimultaneous(int authoredCap)
            => FoldMaxSimultaneousFrom(authoredCap,
                                       RemoteTunables.Int(RemoteTunables.KeyWaveMaxSimultaneousPct),
                                       RemoteTunables.KeyWaveMaxSimultaneousPct);

        /// <summary>Pure. <paramref name="rawPct"/> supplied, for the oracle.</summary>
        public static int FoldMaxSimultaneousFrom(int authoredCap, int rawPct, string keyForLog = null)
        {
            int pct = ClampAndReport(keyForLog, rawPct, MinSimultaneousPct, MaxSimultaneousPct,
                                     "concurrency percent");
            if (pct == IdentityPct) return authoredCap;

            // ZERO MEANS UNCAPPED, NOT "NO ENEMIES" - WaveManager's own comment at the field says so,
            // and every read site tests `> 0` before using it. A percent must never turn the cap OFF,
            // because that releases the whole roster at once and is the exact frame-rate cliff the
            // cap was added to prevent (WO-1113). So 0 is handed back as 0, untouched.
            if (authoredCap <= 0) return authoredCap;

            // And a real cap must never ROUND DOWN TO 0, which would read as uncapped - the opposite
            // of what an operator lowering the percent asked for. Floored at 1.
            return Mathf.Max(1, Mathf.RoundToInt(authoredCap * (pct / 100f)));
        }

        // =====================================================================
        //  Clamps. Loud, once per key per bad value per process, never silent.
        // =====================================================================

        private static int ClampAndReport(string key, int raw, int min, int max, string what)
        {
            int clamped = Mathf.Clamp(raw, min, max);
            if (clamped == raw) return clamped;

            FlowTrace.Once("Waves", "wavediff-clamp-" + (key ?? what) + "-" + raw,
                "town-wave " + what + " '" + (key ?? "(supplied directly)") + "' resolved to " +
                raw + ", outside " + min + ".." + max + " - CLAMPED to " + clamped + ". The wave " +
                "below uses the CLAMPED value. " +
                (min == MinGrowthPctPerWave && max == MaxGrowthPctPerWave
                    ? "0 is identity - the clamped curve unchanged, which is what ships."
                    : "100 is identity - the authored value unchanged."));
            return clamped;
        }
    }
}
