// =============================================================================
// TownWaveDifficultyRegression [town-wave-difficulty]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
// Markers: TOWN_WAVE_DIFFICULTY_OK / TOWN_WAVE_DIFFICULTY_FAIL.
//
// Pins WO-1773: the town-regen COMBAT GATE and post-clamp TOWN WAVE growth are
// REMOTE-tunable, and they ship INERT.
//
// -----------------------------------------------------------------------------
// WHAT THIS TICKET WAS, SO THE CASES BELOW READ AS ANSWERS AND NOT AS BOXES.
// -----------------------------------------------------------------------------
// An external tester's report, relayed by the owner verbatim on 2026-09-16: "at the
// level he is nothing can damage him he can just stand there". WO-1773 measured the
// hero plate across 146 one-second frames and 80 consecutive tenth-second frames of a
// wave-176 run: the red bar never moved, with a Cave Troll in melee contact. It named
// TWO independent dead steps, and fixing either one alone closes nothing:
//
//   A. The town safe-zone regen had NO combat check of any kind. It is a FRACTION OF
//      MaxHp per second, so it scales with gear while incoming contact damage is
//      capped by WaveScalingCurve's clamp - the gap widens with every upgrade and can
//      never be outgrown, at any level, on any wave.
//   B. Nothing got harder after wave 60. Wave 176 IS wave 60 on every axis.
//
// The whole RISK of that change is the mirror image of the defect: a knob family whose
// defaults were not EXACTLY identity would silently re-tune the town for every player
// with no row and no network, and it would do it INVISIBLY - nothing errors, the game
// is simply a different game. This suite is what makes that impossible.
//
// -----------------------------------------------------------------------------
// WHAT IS PINNED, AND WHY EACH CASE EXISTS.
// -----------------------------------------------------------------------------
//   1 [identity]        With the shipping defaults, every fold hands back the value it
//                       was passed - BIT-IDENTICALLY, asserted with == and not with a
//                       tolerance, because the whole claim is "no float round-trip".
//                       Driven through the REAL WaveScalingCurve at waves 20, 60 and
//                       176, and through the REAL regen gate with a wave live.
//   2 [regen-gate]      The out-of-combat rule: suppressed inside the window, restored
//                       outside it, the window WINS over the in-wave percent, a hero
//                       who has NEVER been hit is never suppressed, and the FTUE is a
//                       never-gate carve-out. That last one is the case that stops a
//                       new player being stranded at 1 HP forever.
//   3 [growth-folds]    Beyond the band, growth FOLDS: waves 20, 60 and 176 are no
//                       longer equal, wave 20 itself is UNMOVED (existing balance is
//                       not silently re-tuned), and the arithmetic is ADDITIVE - the
//                       shape the owner's seed table was computed in.
//   4 [count-folds]     The three count/concurrency percents scale their authored
//                       inputs; an authored 0 concurrency cap stays 0 (it means
//                       UNCAPPED); and no percent can round a real value down to 0.
//   5 [band-fallback]   The unknown-wave fallback. The band start is READ off the
//                       curve, a non-clamped or empty curve means "never grow", and
//                       waves at or below the band - including 0 and negatives - are
//                       returned untouched.
//   6 [defaults]        The seven registry defaults are exactly identity, so a fresh
//                       install and an empty table are the same town.
//
// -----------------------------------------------------------------------------
// NO AUTHORED BASELINE IS RESTATED HERE, AND THAT IS THE POINT OF THE ...From SHAPE.
// -----------------------------------------------------------------------------
// The regen fraction, the curve keyframes, MaxCount, countCap and the scene's
// concurrency cap are all CONTENT. A literal copied into this file would go red the
// first time the owner tuned any of them - the opposite of useful, and the exact
// stale-copy failure CLAUDE.md sections 2 / 5 / 8 / 16 each record a scar from. What
// is pinned is the RELATIONSHIP between a supplied baseline and the folded value, so
// every case drives the pure ...From entry points with values it chose itself.
//
// Zero scene, zero save, zero network, zero PlayMode, zero PlayerPrefs.
// ASCII only. Never throws.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.Ops;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pins the WO-1773 town-regen combat gate and town-wave difficulty rail: identity by
    /// default, additive growth past the curve's clamp band, percent-scaled counts, clamped,
    /// and never able to strand the FTUE hero at 1 HP. Returns true (summary) / false (detail).
    /// </summary>
    public static class TownWaveDifficultyRegression
    {
        /// <summary>Float tolerance for the GROWN values, which are single-precision sums.</summary>
        private const float Eps = 0.0005f;

        /// <summary>
        /// A regen fraction this suite invents. Deliberately NOT SafeZoneRecovery's shipping
        /// fraction: a copy of that const here would go red the day the owner re-tuned it, and the
        /// claim under test is about the RELATIONSHIP, not about its value.
        /// </summary>
        private const float ProbeFraction = 0.2f;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("TOWN_WAVE_DIFFICULTY_OK - " + reason);
            else Debug.LogError("TOWN_WAVE_DIFFICULTY_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- TOWN WAVE DIFFICULTY (WO-1773: regen gate + post-clamp growth on the remote rail) ---");

            // The rail is process-global. Clear it first so an ambient payload from an earlier suite
            // in the same batch cannot make [identity] red on correct code, and clear it again in the
            // finally so this suite cannot make the NEXT one red either.
            try
            {
                RemoteTunables.Clear();
                Case(failures, "identity", () => Case1_Identity(failures, log));
                Case(failures, "regen-gate", () => Case2_RegenGate(failures, log));
                Case(failures, "growth-folds", () => Case3_GrowthFolds(failures, log));
                Case(failures, "count-folds", () => Case4_CountFolds(failures, log));
                Case(failures, "band-fallback", () => Case5_BandFallback(failures, log));
                Case(failures, "defaults", () => Case6_DefaultsAreIdentity(failures, log));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                RemoteTunables.Clear();
            }

            if (failures.Count == 0)
            {
                reason = "TOWN WAVE DIFFICULTY OK - with no row, no network and no parse the town " +
                         "regen runs exactly as it does today (the supplied fraction back " +
                         "bit-identically, even with a wave live) and WaveScalingCurve's HP and " +
                         "damage multipliers are unchanged at waves 20, 60 and 176; the " +
                         "out-of-combat rule suppresses regen only inside the window, the window " +
                         "wins over the in-wave percent, a hero who has never been hit is never " +
                         "suppressed, and the first-time tutorial is NEVER gated (so the 1-HP FTUE " +
                         "recovery the tick was added for cannot be stranded); post-band growth is " +
                         "ADDITIVE and makes waves 20, 60 and 176 differ while leaving wave 20 " +
                         "itself unmoved; the roster ceiling, the endless count cap and the " +
                         "concurrency cap all scale by percent with an authored 0 concurrency cap " +
                         "preserved as UNCAPPED and no percent able to round a real value to 0; the " +
                         "growth band is READ off the curve's last keyframe and a non-clamped or " +
                         "empty curve grows nothing; and all seven registry defaults are exactly " +
                         "identity, so a fresh install and an empty table are the same town";
                Debug.Log(log.ToString() + "TOWN_WAVE_DIFFICULTY_OK");
                return true;
            }

            reason = "town-wave-difficulty FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            Debug.LogError(log.ToString() + "TOWN_WAVE_DIFFICULTY_FAIL: " + reason);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// A fresh runtime-default WaveScalingCurve - the SAME object WaveManager's
        /// EnsureScalingCurve falls back to when no asset is assigned, which WO-1773 verified is
        /// what actually ships (the hub scene's _scalingCurve is a null fileID and no .asset
        /// overrides it). Driving the real type means the curve's own ease-in/ease-out tangents and
        /// its wrap modes are part of what is under test, not re-implemented beside it.
        /// </summary>
        private static WaveScalingCurve FreshCurve()
        {
            var c = ScriptableObject.CreateInstance<WaveScalingCurve>();
            c.name = "WaveScalingCurve (regression)";
            return c;
        }

        // =====================================================================
        //  Case 1 - IDENTITY. The invariant the whole family lives or dies by.
        // =====================================================================
        /// <summary>
        /// ⭐ ASSERTED WITH == AND NOT WITH A TOLERANCE, ON PURPOSE. The claim is not "close to
        /// today" but "today, bit for bit": both consumers SHORT-CIRCUIT at identity and hand back
        /// the caller's own float rather than computing value*100/100f. A tolerance here would hide
        /// exactly the round-trip the short-circuit exists to remove.
        /// </summary>
        private static void Case1_Identity(List<string> failures, StringBuilder log)
        {
            RemoteTunables.Clear();

            // -- the regen gate, including WITH A WAVE LIVE, which is the interesting half --
            bool[] waveStates = { false, true };
            foreach (bool waveActive in waveStates)
            {
                var g = TownRegenTunables.ResolveFrom(
                    ProbeFraction, waveActive, 0.1f, false,
                    RemoteTunables.TownRegenSuppressSecondsAfterHitDefault,
                    RemoteTunables.TownRegenPctDuringWaveDefault);

                log.AppendLine("  regen identity waveActive=" + waveActive + " -> rate=" +
                               g.RateFraction.ToString("F4") + " reason=" + g.Reason);

                if (g.RateFraction != ProbeFraction)
                    failures.Add("[identity] with the shipping defaults and waveActive=" + waveActive +
                                 " the regen gate returned " + g.RateFraction + ", not the " +
                                 ProbeFraction + " it was passed. An empty client_tunables table MUST " +
                                 "be today's town bit for bit - a player who is offline, whose fetch " +
                                 "times out or who gets malformed JSON has to get the behaviour this " +
                                 "build hardcodes, and a drift here would change the game silently.");

                if (g.Suppressed)
                    failures.Add("[identity] the regen gate SUPPRESSED at the shipping defaults " +
                                 "(waveActive=" + waveActive + "). The default is a 0-second window " +
                                 "and a 100 percent in-wave rate, i.e. no gate at all; suppressing " +
                                 "here would stop town healing for every player with no row.");

                // Note secondsSinceHit=0.1 above: a hero hit THIS FRAME. At the shipping 0-second
                // window even that must not suppress, which is what makes the default truly inert.
            }

            // -- the curve, at the three waves the ticket's arithmetic is quoted at --
            var curve = FreshCurve();
            int band = WaveScalingCurve.GrowthBandStart(curve.HpCurve);
            int[] waves = { 20, 60, 176 };
            foreach (int w in waves)
            {
                float rawHp = curve.HpCurve.Evaluate(w);
                float rawDmg = curve.DamageCurve.Evaluate(w);
                float foldedHp = WaveDifficultyTunables.FoldStrengthFrom(
                    rawHp, w, band, RemoteTunables.WaveHpGrowthPctPerWaveDefault);
                float foldedDmg = WaveDifficultyTunables.FoldStrengthFrom(
                    rawDmg, w, band, RemoteTunables.WaveDmgGrowthPctPerWaveDefault);

                log.AppendLine("  curve identity wave " + w + " -> hp x" + foldedHp.ToString("F3") +
                               " dmg x" + foldedDmg.ToString("F3") + " (band starts " + band + ")");

                if (foldedHp != rawHp)
                    failures.Add("[identity] wave " + w + " HP multiplier folded to " + foldedHp +
                                 " from the curve's own " + rawHp + " at the shipping default of 0 " +
                                 "growth. Zero growth must be a SHORT-CIRCUIT that returns the " +
                                 "curve's float itself.");
                if (foldedDmg != rawDmg)
                    failures.Add("[identity] wave " + w + " damage multiplier folded to " + foldedDmg +
                                 " from the curve's own " + rawDmg + " at the shipping default of 0 growth.");
            }

            // And through the PUBLIC surface WaveManager actually calls, so the wiring is pinned too
            // and not only the pure arithmetic. Clamped at 1 by the accessor, so compare to the same.
            foreach (int w in waves)
            {
                float viaAccessor = curve.HpMultiplier(w);
                float expected = Mathf.Max(1f, curve.HpCurve.Evaluate(w));
                if (viaAccessor != expected)
                    failures.Add("[identity] WaveScalingCurve.HpMultiplier(" + w + ") returned " +
                                 viaAccessor + " but the curve's own floored evaluation is " + expected +
                                 ". The accessor is what every spawn path calls, so an identity break " +
                                 "here reaches live enemies even though the pure fold is clean.");
            }

            // 176 must EQUAL 60 at the defaults. That equality IS the reported defect, and pinning it
            // is what proves the suite would notice if growth were accidentally switched on.
            if (curve.HpMultiplier(176) != curve.HpMultiplier(60))
                failures.Add("[identity] wave 176 and wave 60 no longer share an HP multiplier at the " +
                             "SHIPPING defaults. That equality is WO-1773's measured defect and must " +
                             "persist until a row exists - if it broke here, growth is on by default " +
                             "and every existing save just got harder with nobody asking.");

            UnityEngine.Object.DestroyImmediate(curve);
        }

        // =====================================================================
        //  Case 2 - THE OUT-OF-COMBAT RULE, AND THE FTUE CARVE-OUT.
        // =====================================================================
        /// <summary>
        /// The shape WO-Sect.6 recommended and the owner's brief ruled: suppress for N seconds after
        /// the hero LOSES HP. The four sub-claims are the ones a wrong implementation gets wrong:
        /// the window boundary, the precedence over the in-wave percent, the never-hit hero, and the
        /// tutorial.
        /// </summary>
        private static void Case2_RegenGate(List<string> failures, StringBuilder log)
        {
            RemoteTunables.Clear();

            const int Window = 6;

            // (a) inside the window -> suppressed, and it does NOT need a wave to be live: the same
            //     Update runs in scenes that have no WaveManager at all.
            var inside = TownRegenTunables.ResolveFrom(ProbeFraction, false, 2.5f, false, Window, 100);
            log.AppendLine("  gate inside window (2.5s of " + Window + "s, no wave) -> rate=" +
                           inside.RateFraction.ToString("F4") + " reason=" + inside.Reason);
            if (!inside.Suppressed || inside.RateFraction != 0f)
                failures.Add("[regen-gate] a hero who lost HP 2.5s ago with a " + Window +
                             "s window was NOT suppressed (rate=" + inside.RateFraction + "). This is " +
                             "the whole fix: without it a wave-176 roster ten times heavier still " +
                             "cannot move a bar refilling faster than it drains.");
            if (inside.Reason != TownRegenTunables.ReasonRecentDamage)
                failures.Add("[regen-gate] suppression inside the window reported reason '" +
                             inside.Reason + "' instead of '" + TownRegenTunables.ReasonRecentDamage +
                             "'. The reason is what the one [Flow:SafeZone] line prints, and a " +
                             "mislabelled reason sends the next reader down the wrong branch.");

            // (b) outside the window -> full rate, bit-identical. The between-waves top-up survives.
            var outside = TownRegenTunables.ResolveFrom(ProbeFraction, false, Window + 0.5f, false, Window, 100);
            log.AppendLine("  gate outside window -> rate=" + outside.RateFraction.ToString("F4") +
                           " reason=" + outside.Reason);
            if (outside.Suppressed || outside.RateFraction != ProbeFraction)
                failures.Add("[regen-gate] a hero out of combat for longer than the window did not get " +
                             "the FULL rate back (rate=" + outside.RateFraction + "). The between-waves " +
                             "top-up is the reason this tick exists; breaking it trades one defect for a " +
                             "worse one with no on-screen symptom.");

            // (c) THE WINDOW WINS OVER THE IN-WAVE PERCENT. Ordering is a ruling, not an accident.
            var both = TownRegenTunables.ResolveFrom(ProbeFraction, true, 1f, false, Window, 50);
            if (both.Reason != TownRegenTunables.ReasonRecentDamage || both.RateFraction != 0f)
                failures.Add("[regen-gate] with BOTH knobs live and the hero hit 1s ago, the gate " +
                             "resolved '" + both.Reason + "' at rate " + both.RateFraction +
                             ". The recent-damage window must WIN: a hero being hit right now is in " +
                             "combat whatever the wave phase says.");

            // (d) the in-wave percent alone, when nothing recent hit the hero.
            var inWave = TownRegenTunables.ResolveFrom(ProbeFraction, true, 999f, false, Window, 50);
            log.AppendLine("  gate in-wave 50pct, no recent hit -> rate=" + inWave.RateFraction.ToString("F4") +
                           " reason=" + inWave.Reason);
            if (Mathf.Abs(inWave.RateFraction - ProbeFraction * 0.5f) > Eps)
                failures.Add("[regen-gate] the in-wave percent did not halve the rate: got " +
                             inWave.RateFraction + ", expected " + (ProbeFraction * 0.5f) + ".");
            if (inWave.Reason != TownRegenTunables.ReasonInWave)
                failures.Add("[regen-gate] the in-wave branch reported reason '" + inWave.Reason + "'.");

            // A 0 percent in-wave rate must report SUPPRESSED, not merely a zero rate - the caller
            // takes a different logging branch on it and a silent zero is the failure section 12 bans.
            var zeroInWave = TownRegenTunables.ResolveFrom(ProbeFraction, true, 999f, false, 0, 0);
            if (!zeroInWave.Suppressed || zeroInWave.RateFraction != 0f)
                failures.Add("[regen-gate] an in-wave percent of 0 gave rate " + zeroInWave.RateFraction +
                             " suppressed=" + zeroInWave.Suppressed + ". Zero must read as SUPPRESSED so " +
                             "the caller logs WHY nothing healed.");

            // (e) A HERO WHO HAS NEVER BEEN HIT. PositiveInfinity, never 0.
            var never = TownRegenTunables.ResolveFrom(ProbeFraction, true, float.PositiveInfinity, false, Window, 100);
            if (never.Suppressed || never.RateFraction != ProbeFraction)
                failures.Add("[regen-gate] a hero who has NEVER been hit (seconds-since = +Infinity) was " +
                             "suppressed. If HeroHealth ever reported 0 for 'never hit' instead of " +
                             "Infinity, this is the case that catches it - and the symptom would be a " +
                             "fresh save that never heals in town, with no error anywhere.");

            // (f) THE FTUE CARVE-OUT. The case that matters most, and the cheapest to get wrong.
            var ftue = TownRegenTunables.ResolveFrom(ProbeFraction, true, 0f, true, 120, 0);
            log.AppendLine("  gate FTUE carve-out (worst-case knobs) -> rate=" +
                           ftue.RateFraction.ToString("F4") + " reason=" + ftue.Reason);
            if (ftue.Suppressed || ftue.RateFraction != ProbeFraction)
                failures.Add("[regen-gate] the FTUE carve-out did not hold: with the tutorial live and " +
                             "the harshest legal knobs, the rate was " + ftue.RateFraction +
                             ". SafeZoneRecovery's own header records that this tick exists BECAUSE the " +
                             "teaching wave floors the hero at 1 HP and OnSceneLoaded never re-fires - " +
                             "gating it there strands a NEW PLAYER at 1 HP with no recovery path at all.");
            if (ftue.Reason != TownRegenTunables.ReasonFtue)
                failures.Add("[regen-gate] the FTUE branch reported reason '" + ftue.Reason + "'.");

            // (g) clamps, and the caller receives the CLAMPED value.
            var clamped = TownRegenTunables.ResolveFrom(ProbeFraction, true, 999f, false, 999999, 400);
            if (clamped.SuppressSeconds != TownRegenTunables.MaxSuppressSeconds)
                failures.Add("[regen-gate] a suppression window of 999999 did not clamp to " +
                             TownRegenTunables.MaxSuppressSeconds + " (got " + clamped.SuppressSeconds + ").");
            if (clamped.DuringWavePct != TownRegenTunables.MaxDuringWavePct)
                failures.Add("[regen-gate] an in-wave percent of 400 did not clamp to " +
                             TownRegenTunables.MaxDuringWavePct + " (got " + clamped.DuringWavePct +
                             "). Above 100 is refused on purpose - regenerating FASTER inside a wave " +
                             "than between waves is the opposite of the defect.");
            if (clamped.RateFraction != ProbeFraction)
                failures.Add("[regen-gate] the clamped in-wave percent (100) did not restore the full " +
                             "rate: got " + clamped.RateFraction + ".");
        }

        // =====================================================================
        //  Case 3 - GROWTH FOLDS BEYOND THE BAND, AND WAVE 20 DOES NOT MOVE.
        // =====================================================================
        /// <summary>
        /// ⛔ ADDITIVE, NOT MULTIPLICATIVE - and this case is where that ruling is enforced. With a
        /// clamped damage multiplier of 2.0 and 5 hundredths per wave, additive gives 9.8 at wave
        /// 176 and multiplicative gives 17.6. The owner picks a seed off the ADDITIVE table, so a
        /// silent change of shape would make her ruling mean a different number than she chose - the
        /// same class of failure WO-1763's REPLACE-vs-ADD paragraph exists to prevent.
        ///
        /// <para>The second half is just as load-bearing: WAVE 20 ITSELF MUST NOT MOVE. Growth that
        /// leaked backwards into the authored band would re-tune the balance of the first twenty
        /// waves - the part of the game every player sees - while nobody was looking at it.</para>
        /// </summary>
        private static void Case3_GrowthFolds(List<string> failures, StringBuilder log)
        {
            RemoteTunables.Clear();

            const float ClampedMult = 2f;   // this suite's own number, not a copy of the curve's
            const int Band = 20;            // this suite's own band, supplied - never read as canon
            const int Pct = 5;

            float at20 = WaveDifficultyTunables.FoldStrengthFrom(ClampedMult, Band, Band, Pct);
            float at60 = WaveDifficultyTunables.FoldStrengthFrom(ClampedMult, 60, Band, Pct);
            float at176 = WaveDifficultyTunables.FoldStrengthFrom(ClampedMult, 176, Band, Pct);

            log.AppendLine("  growth pct=" + Pct + " band=" + Band + " -> w20 x" + at20.ToString("F2") +
                           "  w60 x" + at60.ToString("F2") + "  w176 x" + at176.ToString("F2"));

            if (at20 != ClampedMult)
                failures.Add("[growth-folds] wave " + Band + " (the band boundary itself) moved to " +
                             at20 + " from " + ClampedMult + ". Growth must apply STRICTLY past the " +
                             "band: leaking it backwards re-tunes the first twenty waves, which is " +
                             "the part of the game every player sees.");

            // The additive expectation, written out so a shape change cannot pass quietly.
            float expect60 = ClampedMult + (Pct / 100f) * (60 - Band);
            float expect176 = ClampedMult + (Pct / 100f) * (176 - Band);
            if (Mathf.Abs(at60 - expect60) > Eps)
                failures.Add("[growth-folds] wave 60 folded to " + at60 + ", but the ADDITIVE shape " +
                             "gives " + expect60 + ". If this is a multiplicative result, the owner's " +
                             "seed number now means something different from the table she picked it off.");
            if (Mathf.Abs(at176 - expect176) > Eps)
                failures.Add("[growth-folds] wave 176 folded to " + at176 + ", but the ADDITIVE shape " +
                             "gives " + expect176 + ".");

            // The reported defect, inverted: 176 must no longer equal 60 once growth is on.
            if (at176 <= at60 || Mathf.Abs(at176 - at60) <= Eps)
                failures.Add("[growth-folds] with growth ON, wave 176 (" + at176 + ") is not strictly " +
                             "harder than wave 60 (" + at60 + "). 'Wave 176 is numerically wave 60' is " +
                             "the measured defect; a growth knob that leaves them equal fixes nothing.");

            // HP and DAMAGE are independent axes. A shared knob would be the duplicated-state failure
            // and would also make 'tougher' and 'more dangerous' impossible to separate.
            if (RemoteTunables.KeyWaveHpGrowthPctPerWave == RemoteTunables.KeyWaveDmgGrowthPctPerWave)
                failures.Add("[growth-folds] the HP and damage growth knobs share a key. They are " +
                             "deliberately separate: HP alone lengthens fights without adding threat, " +
                             "damage alone makes glass cannons, and the owner needs to move one " +
                             "without the other.");

            // The ceiling, on an input (the wave number) that genuinely has no clamp anywhere.
            float runaway = WaveDifficultyTunables.FoldStrengthFrom(
                ClampedMult, 1000000, Band, WaveDifficultyTunables.MaxGrowthPctPerWave);
            if (runaway > WaveDifficultyTunables.MaxEffectiveMultiplier + Eps)
                failures.Add("[growth-folds] the effective multiplier reached " + runaway +
                             ", above the x" + WaveDifficultyTunables.MaxEffectiveMultiplier +
                             " ceiling. The wave number is unbounded (BestWave is only floored at 0), " +
                             "so without this ceiling one typo produces an unclearable wave.");

            // And the growth percent itself clamps.
            float overPct = WaveDifficultyTunables.FoldStrengthFrom(ClampedMult, Band + 1, Band, 999999);
            float maxPctResult = WaveDifficultyTunables.FoldStrengthFrom(
                ClampedMult, Band + 1, Band, WaveDifficultyTunables.MaxGrowthPctPerWave);
            if (Mathf.Abs(overPct - maxPctResult) > Eps)
                failures.Add("[growth-folds] a growth percent of 999999 did not clamp to " +
                             WaveDifficultyTunables.MaxGrowthPctPerWave + ": got " + overPct +
                             " vs the clamped " + maxPctResult + ".");
        }

        // =====================================================================
        //  Case 4 - THE COUNT / CONCURRENCY PERCENTS.
        // =====================================================================
        /// <summary>
        /// Three percents, and three distinct ways to get them wrong. The 0-means-UNCAPPED trap is
        /// the sharpest: every read site in WaveManager tests the cap with <c>&gt; 0</c>, so a
        /// percent that produced 0 would release the WHOLE roster at once - the exact WO-1113 phone
        /// frame-rate cliff the cap was added to prevent, reached by a dial meant to be safe.
        /// </summary>
        private static void Case4_CountFolds(List<string> failures, StringBuilder log)
        {
            RemoteTunables.Clear();

            const int AuthoredMax = 22;     // supplied, not read - see this file's header
            const float AuthoredCap = 3f;
            const int AuthoredSim = 8;

            // Identity first, and bit-identical for the float one.
            if (WaveDifficultyTunables.FoldMaxCountFrom(AuthoredMax, RemoteTunables.WaveMaxCountPctDefault) != AuthoredMax)
                failures.Add("[count-folds] the roster ceiling moved at the shipping default.");
            if (WaveDifficultyTunables.FoldCountCapFrom(AuthoredCap, RemoteTunables.WaveCountCapPctDefault) != AuthoredCap)
                failures.Add("[count-folds] the endless count cap moved at the shipping default, and " +
                             "not even bit-identically - identity must be a SHORT-CIRCUIT here.");
            if (WaveDifficultyTunables.FoldMaxSimultaneousFrom(AuthoredSim, RemoteTunables.WaveMaxSimultaneousPctDefault) != AuthoredSim)
                failures.Add("[count-folds] the concurrency cap moved at the shipping default.");

            // Then the folds.
            int doubled = WaveDifficultyTunables.FoldMaxCountFrom(AuthoredMax, 200);
            float capped = WaveDifficultyTunables.FoldCountCapFrom(AuthoredCap, 200);
            int simUp = WaveDifficultyTunables.FoldMaxSimultaneousFrom(AuthoredSim, 200);
            log.AppendLine("  count folds at 200pct -> roster " + doubled + " cap x" +
                           capped.ToString("F2") + " concurrency " + simUp);
            if (doubled != AuthoredMax * 2)
                failures.Add("[count-folds] 200 percent of a roster ceiling of " + AuthoredMax +
                             " gave " + doubled + ".");
            if (Mathf.Abs(capped - AuthoredCap * 2f) > Eps)
                failures.Add("[count-folds] 200 percent of an endless cap of " + AuthoredCap +
                             " gave " + capped + ".");
            if (simUp != AuthoredSim * 2)
                failures.Add("[count-folds] 200 percent of a concurrency cap of " + AuthoredSim +
                             " gave " + simUp + ".");

            // ⛔ 0 MEANS UNCAPPED AND MUST SURVIVE UNTOUCHED.
            int zeroSim = WaveDifficultyTunables.FoldMaxSimultaneousFrom(0, 400);
            if (zeroSim != 0)
                failures.Add("[count-folds] an AUTHORED concurrency cap of 0 (which means UNCAPPED to " +
                             "every read site in WaveManager) was scaled to " + zeroSim +
                             ". Scaling it turns 'no cap' into a finite one with no row asking for it.");

            // ⛔ AND A REAL CAP MUST NEVER ROUND DOWN TO 0, WHICH WOULD READ AS UNCAPPED.
            int tinySim = WaveDifficultyTunables.FoldMaxSimultaneousFrom(1, WaveDifficultyTunables.MinSimultaneousPct);
            if (tinySim < 1)
                failures.Add("[count-folds] a real concurrency cap of 1 at the minimum percent rounded " +
                             "to " + tinySim + ". That reads as UNCAPPED to every call site, releasing " +
                             "the whole roster at once - the precise opposite of what lowering the " +
                             "percent asked for, and the WO-1113 frame-rate cliff.");

            int tinyRoster = WaveDifficultyTunables.FoldMaxCountFrom(1, WaveDifficultyTunables.MinCountPct);
            if (tinyRoster < 1)
                failures.Add("[count-folds] a roster ceiling of 1 at the minimum percent rounded to " +
                             tinyRoster + ". An empty wave is read as ALREADY CLEARED by the clear " +
                             "logic, so this is a silent stall and not a nerf.");

            // A non-positive authored endless cap already means 'uncapped' to EndlessCountScale.
            if (WaveDifficultyTunables.FoldCountCapFrom(0f, 400) != 0f)
                failures.Add("[count-folds] an authored endless cap of 0 (uncapped) was scaled. It must " +
                             "be handed back untouched.");

            // The concurrency percent is clamped TIGHTER than the roster percents, because it is a
            // frame budget rather than a difficulty dial. If that stops being true, say so loudly.
            if (WaveDifficultyTunables.MaxSimultaneousPct >= WaveDifficultyTunables.MaxCountPct)
                failures.Add("[count-folds] the concurrency percent ceiling (" +
                             WaveDifficultyTunables.MaxSimultaneousPct + ") is no longer tighter than " +
                             "the roster ceiling (" + WaveDifficultyTunables.MaxCountPct + "). " +
                             "Concurrency is a PHONE FRAME BUDGET, not a difficulty dial - the tighter " +
                             "ceiling is the guard rail on the one knob here that can regress the " +
                             "Seeker build's frame rate.");
        }

        // =====================================================================
        //  Case 5 - THE BAND START IS READ, AND THE UNKNOWN-WAVE FALLBACK.
        // =====================================================================
        /// <summary>
        /// The band start is derived from the curve's LAST KEYFRAME rather than written down, so a
        /// tuned asset can never disagree with a pasted literal. The fallbacks matter as much as the
        /// happy path: a curve that is empty, or whose postWrapMode is Loop or PingPong, is already
        /// producing new values past its last key, so growth must add NOTHING on top of it.
        /// </summary>
        private static void Case5_BandFallback(List<string> failures, StringBuilder log)
        {
            RemoteTunables.Clear();

            var curve = FreshCurve();
            int band = WaveScalingCurve.GrowthBandStart(curve.HpCurve);
            int lastKey = Mathf.CeilToInt(curve.HpCurve[curve.HpCurve.length - 1].time);
            log.AppendLine("  band start read off the curve -> " + band + " (last keyframe ceil " + lastKey + ")");
            if (band != lastKey)
                failures.Add("[band-fallback] GrowthBandStart returned " + band + " but the curve's own " +
                             "last keyframe (rounded up) is " + lastKey + ". The band MUST be read off " +
                             "the curve: a literal in a consumer would silently disagree with a tuned " +
                             "asset, which is the duplicated-state failure this repo keeps paying for.");

            // A NULL curve, and an EMPTY one. Both mean 'never grow', never 'grow from wave 0'.
            if (WaveScalingCurve.GrowthBandStart(null) != int.MaxValue)
                failures.Add("[band-fallback] a null curve did not resolve to 'never grow'.");
            if (WaveScalingCurve.GrowthBandStart(new AnimationCurve()) != int.MaxValue)
                failures.Add("[band-fallback] an EMPTY curve did not resolve to 'never grow'. Growing " +
                             "from wave 0 on an empty curve would multiply enemies by the wave number.");

            // A LOOPING curve is already growing on its own - adding to it double-counts.
            var looped = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(20f, 2.5f));
            looped.postWrapMode = WrapMode.Loop;
            if (WaveScalingCurve.GrowthBandStart(looped) != int.MaxValue)
                failures.Add("[band-fallback] a Loop-wrapped curve did not resolve to 'never grow'. It " +
                             "already produces new values past its last key, so a growth term on top " +
                             "would double-count.");

            // THE UNKNOWN-WAVE FALLBACK: at, below, zero and negative waves are returned untouched
            // even with growth ON. Nothing in the town path guarantees a positive wave reaches here.
            const float Probe = 2f;
            int[] belowBand = { 20, 19, 1, 0, -5 };
            foreach (int w in belowBand)
            {
                float folded = WaveDifficultyTunables.FoldStrengthFrom(Probe, w, 20, 50);
                if (folded != Probe)
                    failures.Add("[band-fallback] wave " + w + " (at or below the band) folded to " +
                                 folded + " with growth ON. A wave at or below the band, including a " +
                                 "zero or negative one, must be returned UNTOUCHED - a negative wave " +
                                 "would otherwise SUBTRACT and could drive the multiplier below 1.");
            }

            // And with the band at 'never', even a huge wave grows nothing.
            if (WaveDifficultyTunables.FoldStrengthFrom(Probe, 1000000, int.MaxValue, 100) != Probe)
                failures.Add("[band-fallback] a 'never grow' band still grew at wave 1000000.");

            UnityEngine.Object.DestroyImmediate(curve);
        }

        // =====================================================================
        //  Case 6 - THE SEVEN REGISTRY DEFAULTS ARE EXACTLY IDENTITY.
        // =====================================================================
        /// <summary>
        /// RemoteTunablesDefaultsRegression pins these against its own literals and against the doc
        /// table. This case pins them against the CONSUMERS' own identity constants, which is a
        /// different question: it asks whether the shipped default is the value the consumer
        /// short-circuits on. A default of 99 would pass a literal pin written as 99 and still
        /// re-tune the town for every offline player.
        /// </summary>
        private static void Case6_DefaultsAreIdentity(List<string> failures, StringBuilder log)
        {
            AssertDefault(failures, log, RemoteTunables.KeyTownRegenSuppressSecondsAfterHit,
                          TownRegenTunables.MinSuppressSeconds,
                          "a zero-second window is 'no gate at all', which is today");
            AssertDefault(failures, log, RemoteTunables.KeyTownRegenPctDuringWave,
                          TownRegenTunables.IdentityDuringWavePct,
                          "the percent the consumer short-circuits on, so the fraction is never round-tripped");
            AssertDefault(failures, log, RemoteTunables.KeyWaveHpGrowthPctPerWave,
                          WaveDifficultyTunables.MinGrowthPctPerWave,
                          "zero growth, so the curve's clamped value is returned as-is");
            AssertDefault(failures, log, RemoteTunables.KeyWaveDmgGrowthPctPerWave,
                          WaveDifficultyTunables.MinGrowthPctPerWave,
                          "zero growth on the damage axis");
            AssertDefault(failures, log, RemoteTunables.KeyWaveMaxCountPct,
                          WaveDifficultyTunables.IdentityPct,
                          "the authored roster ceiling unchanged");
            AssertDefault(failures, log, RemoteTunables.KeyWaveCountCapPct,
                          WaveDifficultyTunables.IdentityPct,
                          "the authored endless cap unchanged");
            AssertDefault(failures, log, RemoteTunables.KeyWaveMaxSimultaneousPct,
                          WaveDifficultyTunables.IdentityPct,
                          "the scene's concurrency cap unchanged");
        }

        private static void AssertDefault(List<string> failures, StringBuilder log,
                                          string key, int expected, string why)
        {
            var registry = RemoteTunables.Registry;
            for (int i = 0; i < registry.Length; i++)
            {
                if (registry[i].Key != key) continue;
                log.AppendLine("  default " + key + " = " + registry[i].Default);
                if (registry[i].Default != expected)
                    failures.Add("[defaults] '" + key + "' ships a default of " + registry[i].Default +
                                 " but the consumer's identity value is " + expected + " (" + why +
                                 "). A non-identity default re-tunes the town for every player with " +
                                 "no row and no network, INVISIBLY - nothing errors, the game is " +
                                 "simply a different game.");
                return;
            }
            failures.Add("[defaults] '" + key + "' is not in RemoteTunables.Registry at all. An " +
                         "unregistered key reads as 0 from RemoteTunables.Int, which for the count " +
                         "and concurrency percents means a silently EMPTY or UNCAPPED wave.");
        }
    }
}
