// =============================================================================
// WaveCounterMath — WO-1864: the ONE place the player-facing "N enemies remain"
// number is derived, and the only reason it is testable without a live wave.
// -----------------------------------------------------------------------------
// ⛔ THE FIELD COUNT IS NOT THE REMAINING COUNT.
//
// WaveManager meters the field with a concurrency cap (WO-1113,
// _maxSimultaneousEnemies — a deliberate pacing + phone frame-budget mechanism,
// NOT a bug and NOT to be changed): everything over the cap is HELD in the
// wave's composition and released as reinforcements the moment a body dies. So
// the number of enemies ON THE FIELD is pinned AT the cap for almost the whole
// wave. It is a constant, not a countdown.
//
// A HUD that reads the field list therefore publishes that constant. Captured
// proof, from the owner's own device pull
// Logs/device/raid-trace-20260918-080200.txt:
//
//   07:26:39.667  [Flow:Wave] wave 26: concurrency cap 8 released 8 now,
//                 HOLDING 26 for reinforcement (total roster unchanged at 34).
//   07:26:39.774  [Flow:HUD]  Active wave 26/0 live 8/8 cd0.0
//   ... no further HUD wave line for 101 seconds, while the wave loop traced
//       "field has 8 live, N still HELD" with N walking 26 -> 25 -> ... -> 1 ...
//   07:28:20.833  [Flow:HUD]  Active wave 26/0 live 7/7 cd0.0
//
// i.e. the counter sat on "8 enemies remain" for the entire drain and only
// started moving once the TRUE remaining count fell below the cap — exactly the
// owner's report (2026-09-18): "every wave shows 8 remaining troops till it gets
// lower than 8 can we reflect actual troop counts?"
//
// The honest number already existed, in two parts the wave loop tracks
// separately: LiveEnemies.Count + HeldReinforcements. This file is that sum plus
// the roster high-water mark the progress bar needs as its denominator, held as
// pure arithmetic in Core so a regression can drive the real captured sequence
// through it with no scene, no Unity loop and no wave.
// =============================================================================

namespace DeNelle.Core.HudModel
{
    /// <summary>
    /// WO-1864: derives the player-facing wave counter from the wave loop's two buckets.
    /// Pure arithmetic — no Unity types, no state beyond the caller's own tracker instance.
    /// </summary>
    public static class WaveCounterMath
    {
        /// <summary>
        /// The TRUE remaining enemy count for a wave: the bodies alive on the field PLUS the ones
        /// the concurrency cap is still holding back as reinforcements.
        /// <para>
        /// ⛔ Never publish <paramref name="liveOnField"/> on its own — see the file header for the
        /// captured lines showing what that reads like to a player. Negatives are floored at 0 so a
        /// transient bad read can never render a negative count.
        /// </para>
        /// </summary>
        public static int Remaining(int liveOnField, int heldByCap)
        {
            int live = liveOnField > 0 ? liveOnField : 0;
            int held = heldByCap   > 0 ? heldByCap   : 0;
            return live + held;
        }
    }

    /// <summary>
    /// WO-1864: tracks a wave's ROSTER total as the high-water mark of
    /// <see cref="WaveCounterMath.Remaining"/> for the current wave number.
    /// <para>
    /// Why a peak rather than a value read off WaveManager: the roster total is display-only (it is
    /// the progress bar's denominator and nothing else), and <c>remaining</c> is at its maximum on
    /// the wave's FIRST observation — the moment the composition has released its first chunk and
    /// deferred the rest — so the peak equals the composed roster the wave loop traces as "total
    /// roster unchanged at N" (34 for wave 26, 35 for wave 27 in the capture above). Keeping it here
    /// avoids a fourth piece of wave-loop state with four reset sites to keep in sync, which is the
    /// duplicated-state failure CLAUDE.md §2/§5/§8 each describe in their own words.
    /// </para>
    /// <para>
    /// The reset is keyed on the wave NUMBER changing, not on the phase, so the bar can still read
    /// full-cleared through the Cleared phase of the wave that just ended.
    /// </para>
    /// </summary>
    public struct WaveRosterTracker
    {
        private int _wave;
        private int _peak;

        /// <summary>The wave number this tracker is currently accumulating for.</summary>
        public int Wave { get { return _wave; } }

        /// <summary>The roster total (peak remaining) observed for <see cref="Wave"/> so far.</summary>
        public int RosterTotal { get { return _peak; } }

        /// <summary>
        /// Feeds one observation in and answers the roster total to publish. A new
        /// <paramref name="waveNumber"/> starts a fresh peak.
        /// </summary>
        public int Observe(int waveNumber, int remaining)
        {
            if (waveNumber != _wave)
            {
                _wave = waveNumber;
                _peak = 0;
            }
            if (remaining > _peak) _peak = remaining;
            return _peak;
        }
    }
}
