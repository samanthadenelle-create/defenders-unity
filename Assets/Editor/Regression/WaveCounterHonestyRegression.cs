// =============================================================================
// WaveCounterHonestyRegression [wave-counter-honesty]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core).
//
// WO-1864. THE WAVE COUNTER READ "8 ENEMIES REMAIN" FOR 101 SECONDS WHILE 26
// ENEMIES WERE STILL COMING.
//
// Owner, verbatim (2026-09-18): "every wave shows 8 remaining troops till it gets
// lower than 8 can we reflect actual troop counts?"
//
// THE MEASURED DEFECT - her own device pull,
// Logs/device/raid-trace-20260918-080200.txt, three lines that settle it:
//
//   L3399  07:26:39.667  [Flow:Wave] wave 26: concurrency cap 8 released 8 now,
//                        HOLDING 26 for reinforcement (total roster unchanged at 34).
//   L3486  07:26:39.774  [Flow:HUD]  Active wave 26/0 live 8/8 cd0.0
//   L15452 07:28:20.833  [Flow:HUD]  Active wave 26/0 live 7/7 cd0.0
//
// 101 seconds between the two HUD lines, during which [Flow:Wave] traced
// "field has 8 live, N still HELD" with N walking 26 -> 25 -> 24 ... -> 1. The
// HUD model's change-gate had nothing to publish because the number it read - the
// FIELD list's length - is pinned AT the concurrency cap by design (WO-1113). The
// counter was true about the field and a lie about the wave.
//
// !! THE CAP IS NOT THE BUG AND IS NOT TOUCHED. Metering the field is a deliberate
// pacing + phone frame-budget mechanism; the owner asked for an honest COUNTER, not
// an easier wave. This suite asserts the DISPLAY arithmetic only.
//
// !! IT DRIVES THE PURE SEAM - DeNelle.Core.HudModel.WaveCounterMath /
// WaveRosterTracker / WaveModel. No scene, no PlayMode, no WaveManager, nothing to
// restore. The producer (WaveProducer) is a thin adapter over these and is asserted
// only through the numbers it would publish.
//
// Cases:
//   1 [remaining-is-live-plus-held]  Remaining(8, 26) == 34 - the field count alone is
//                                    NOT the answer, and 8 is specifically rejected.
//   2 [captured-sequence-moves]      the owner's real wave-26 sequence (live pinned at
//                                    8, held 26 -> 0) publishes a STRICTLY NON-INCREASING
//                                    count that starts at 34 and actually moves while the
//                                    field is still full. RED against HEAD, where every
//                                    frame of that sequence published 8.
//   3 [roster-is-the-denominator]    the tracker's total is 34 for wave 26 and 35 for wave
//                                    27 - the composed rosters WaveManager traced - and the
//                                    progress bar's numerator/denominator are not the same
//                                    number. RED against HEAD (denominator was the field list).
//   4 [new-wave-resets-roster]       a new wave number starts a fresh peak, so wave 27 does
//                                    not inherit wave 26's 34, and the Cleared phase of the
//                                    wave that just ended keeps its roster.
//   5 [model-publishes-remaining]    WaveModel.Set stores the remaining count in
//                                    EnemiesRemaining (the property HudKitController formats
//                                    into hud.hud_kit.enemies_remain) and never clamps it to
//                                    the field count.
//   6 [never-negative]              a transient bad read cannot render a negative count.
//
// Markers: WAVE_COUNTER_HONESTY_OK / WAVE_COUNTER_HONESTY_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.WaveCounterHonestyRegression.RunAll
//
// OWED, and deliberately not claimed: this lane ran NO Unity (no gate, per its brief).
// Cases 2, 3 and 5 were authored RED against HEAD - WaveCounterMath, WaveRosterTracker
// and WaveModel.EnemiesRemaining did not exist there, and the HEAD behaviour each case
// rejects is quoted from the captured log above - but the RED was never OBSERVED.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.HudModel;   // WaveCounterMath / WaveRosterTracker / WaveModel / WavePhase

namespace DeNelle.Editor.Regression
{
    public static class WaveCounterHonestyRegression
    {
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("WAVE_COUNTER_HONESTY_OK - " + reason);
            else Debug.LogError("WAVE_COUNTER_HONESTY_FAIL: " + reason);
        }

        /// <summary>The concurrency cap the owner's session ran (traced "concurrencyCap=8").</summary>
        private const int Cap = 8;

        /// <summary>Wave 26's composed roster - traced "total roster unchanged at 34".</summary>
        private const int Wave26Roster = 34;

        /// <summary>Wave 27's composed roster - traced "total roster unchanged at 35".</summary>
        private const int Wave27Roster = 35;

        /// <summary>
        /// THE OWNER'S WAVE 26, VERBATIM off the device pull: the held counts traced by
        /// [Flow:Wave] "field has 8 live, N still HELD", in order, while the field stayed at 8.
        /// (L3414 through L14859 of Logs/device/raid-trace-20260918-080200.txt.)
        /// </summary>
        private static readonly int[] Wave26HeldWalk =
        {
            26, 26, 26, 26, 26, 26, 26, 25, 25, 25, 24, 24, 23, 23, 23, 23, 23, 23, 23, 23,
            22, 22, 22, 21, 18, 16, 16, 16, 16, 16, 15, 15, 15, 14, 13, 9, 9, 8, 8, 8,
            7, 7, 6, 1, 1, 1, 1, 1,
        };

        /// <summary>
        /// The tail the owner DID see move, once the truth fell below the cap: the [Flow:HUD]
        /// lines at 07:28:20.833 onward, live 7 / 5 / 4 / 3 / 2, held exhausted.
        /// </summary>
        private static readonly int[] Wave26LiveTail = { 7, 5, 4, 3, 2, 1, 0 };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            try
            {
                Case1_RemainingIsLivePlusHeld(failures);
                Case2_CapturedSequenceMoves(failures);
                Case3_RosterIsTheDenominator(failures);
                Case4_NewWaveResetsRoster(failures);
                Case5_ModelPublishesRemaining(failures);
                Case6_NeverNegative(failures);
            }
            catch (Exception ex)
            {
                failures.Add("threw: " + ex.GetType().Name + " " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = string.Join(" | ", failures);
                return false;
            }

            reason = "6/6 cases: remaining = live + held (" + Cap + " + 26 = " + Wave26Roster +
                     "), the owner's captured wave-26 sequence publishes a moving count instead of a " +
                     "constant " + Cap + ", the roster denominator is " + Wave26Roster + "/" +
                     Wave27Roster + " per wave and resets on a new wave, WaveModel exposes it as " +
                     "EnemiesRemaining, and no frame can go negative.";
            return true;
        }

        // ── 1 [remaining-is-live-plus-held] ───────────────────────────────────

        private static void Case1_RemainingIsLivePlusHeld(List<string> failures)
        {
            int r = WaveCounterMath.Remaining(Cap, 26);
            if (r != Wave26Roster)
                failures.Add("[remaining-is-live-plus-held] Remaining(" + Cap + ", 26) = " + r +
                             ", expected " + Wave26Roster + " (the traced roster).");

            // The specific HEAD behaviour: the field count published on its own.
            if (r == Cap)
                failures.Add("[remaining-is-live-plus-held] Remaining() answered the FIELD count (" +
                             Cap + ") - that is the defect, not the fix: the cap pins the field, so " +
                             "held bodies must be added in.");

            // Uncapped / legacy SpawnBatch path: held is 0, so this degrades to the field count.
            if (WaveCounterMath.Remaining(5, 0) != 5)
                failures.Add("[remaining-is-live-plus-held] with nothing held the count must equal " +
                             "the field (the legacy uncapped spawn path must be unchanged).");
        }

        // ── 2 [captured-sequence-moves] ───────────────────────────────────────

        private static void Case2_CapturedSequenceMoves(List<string> failures)
        {
            var published = new List<int>();
            for (int i = 0; i < Wave26HeldWalk.Length; i++)
                published.Add(WaveCounterMath.Remaining(Cap, Wave26HeldWalk[i]));
            for (int i = 0; i < Wave26LiveTail.Length; i++)
                published.Add(WaveCounterMath.Remaining(Wave26LiveTail[i], 0));

            if (published[0] != Wave26Roster)
                failures.Add("[captured-sequence-moves] the wave's FIRST published count was " +
                             published[0] + ", expected the full roster " + Wave26Roster + ".");

            // Monotonic: a remaining count that ever RISES mid-wave would read as reinforcements
            // arriving from nowhere. (Held only ever falls, and the field is capped, so the sum
            // cannot rise on this path.)
            for (int i = 1; i < published.Count; i++)
            {
                if (published[i] > published[i - 1])
                {
                    failures.Add("[captured-sequence-moves] the count ROSE at step " + i + " (" +
                                 published[i - 1] + " -> " + published[i] + ").");
                    break;
                }
            }

            // THE DEFECT ITSELF: on HEAD every one of the first 48 frames published exactly 8.
            // Count how many DISTINCT values the capped phase produces - HEAD produced one.
            var distinctWhileCapped = new HashSet<int>();
            for (int i = 0; i < Wave26HeldWalk.Length; i++)
                distinctWhileCapped.Add(published[i]);
            if (distinctWhileCapped.Count < 2)
                failures.Add("[captured-sequence-moves] the counter published ONE value (" +
                             published[0] + ") for all " + Wave26HeldWalk.Length + " captured " +
                             "frames of the capped phase - that is the owner's exact symptom.");

            // And it must never have sat on the cap while the truth was higher.
            for (int i = 0; i < Wave26HeldWalk.Length; i++)
            {
                if (Wave26HeldWalk[i] > 0 && published[i] == Cap)
                {
                    failures.Add("[captured-sequence-moves] frame " + i + " published " + Cap +
                                 " while " + Wave26HeldWalk[i] + " enemy(s) were still held.");
                    break;
                }
            }

            if (published[published.Count - 1] != 0)
                failures.Add("[captured-sequence-moves] the wave's last published count was " +
                             published[published.Count - 1] + ", expected 0.");
        }

        // ── 3 [roster-is-the-denominator] ─────────────────────────────────────

        private static void Case3_RosterIsTheDenominator(List<string> failures)
        {
            var tracker = new WaveRosterTracker();

            int total26 = 0, firstRemaining26 = 0;
            for (int i = 0; i < Wave26HeldWalk.Length; i++)
            {
                int remaining = WaveCounterMath.Remaining(Cap, Wave26HeldWalk[i]);
                if (i == 0) firstRemaining26 = remaining;
                total26 = tracker.Observe(26, remaining);
            }
            for (int i = 0; i < Wave26LiveTail.Length; i++)
                total26 = tracker.Observe(26, WaveCounterMath.Remaining(Wave26LiveTail[i], 0));

            if (total26 != Wave26Roster)
                failures.Add("[roster-is-the-denominator] wave 26's roster total read " + total26 +
                             ", expected the traced " + Wave26Roster + ".");

            // HEAD's denominator WAS the numerator (both were the field list), so the bar sat
            // pinned at full for the whole wave and conveyed nothing. Assert they differ once
            // the wave is underway.
            int midRemaining = WaveCounterMath.Remaining(Cap, 13);
            if (midRemaining >= total26)
                failures.Add("[roster-is-the-denominator] mid-wave the numerator (" + midRemaining +
                             ") was not below the denominator (" + total26 + ") - the progress bar " +
                             "would read full for the entire wave, which is the same defect in the " +
                             "other widget.");

            if (firstRemaining26 != total26)
                failures.Add("[roster-is-the-denominator] the peak was not established on the " +
                             "wave's FIRST observation (first=" + firstRemaining26 + ", peak=" +
                             total26 + ") - the denominator would climb mid-wave.");

            int total27 = new WaveRosterTracker().Observe(27, WaveCounterMath.Remaining(Cap, 27));
            if (total27 != Wave27Roster)
                failures.Add("[roster-is-the-denominator] wave 27's roster total read " + total27 +
                             ", expected the traced " + Wave27Roster + ".");
        }

        // ── 4 [new-wave-resets-roster] ────────────────────────────────────────

        private static void Case4_NewWaveResetsRoster(List<string> failures)
        {
            var tracker = new WaveRosterTracker();
            tracker.Observe(26, Wave26Roster);

            // Cleared phase of wave 26: the field empties but the wave NUMBER has not changed, so
            // the roster must survive - otherwise the "cleared" bar would have no denominator.
            int stillWave26 = tracker.Observe(26, 0);
            if (stillWave26 != Wave26Roster)
                failures.Add("[new-wave-resets-roster] wave 26's roster was lost when the field " +
                             "emptied (read " + stillWave26 + ", expected " + Wave26Roster + ").");

            // Wave 27 starts: it must NOT inherit 34.
            int wave27First = tracker.Observe(27, WaveCounterMath.Remaining(Cap, 27));
            if (wave27First != Wave27Roster)
                failures.Add("[new-wave-resets-roster] wave 27 inherited a stale roster (read " +
                             wave27First + ", expected " + Wave27Roster + ").");

            if (tracker.Wave != 27)
                failures.Add("[new-wave-resets-roster] the tracker did not follow the wave number " +
                             "(reads " + tracker.Wave + ").");
        }

        // ── 5 [model-publishes-remaining] ─────────────────────────────────────

        private static void Case5_ModelPublishesRemaining(List<string> failures)
        {
            var model = new WaveModel();
            int fired = 0;
            model.Changed += () => fired++;

            model.Set(WavePhase.Active, 26, 0, 0f, false, null,
                      WaveCounterMath.Remaining(Cap, 26), Wave26Roster, null);

            if (model.EnemiesRemaining != Wave26Roster)
                failures.Add("[model-publishes-remaining] EnemiesRemaining read " +
                             model.EnemiesRemaining + ", expected " + Wave26Roster +
                             " (this is the property HudKitController formats into " +
                             "hud.hud_kit.enemies_remain).");

            if (model.EnemiesRemaining == Cap)
                failures.Add("[model-publishes-remaining] the model published the FIELD count (" +
                             Cap + ") - the defect reached the property the HUD reads.");

            if (model.EnemiesTotal != Wave26Roster)
                failures.Add("[model-publishes-remaining] EnemiesTotal read " + model.EnemiesTotal +
                             ", expected the roster " + Wave26Roster + ".");

            if (fired != 1)
                failures.Add("[model-publishes-remaining] Changed fired " + fired +
                             " time(s) on one Set, expected 1 (the HUD is event-driven).");
        }

        // ── 6 [never-negative] ────────────────────────────────────────────────

        private static void Case6_NeverNegative(List<string> failures)
        {
            if (WaveCounterMath.Remaining(-3, -9) != 0)
                failures.Add("[never-negative] two bad reads produced a negative count.");
            if (WaveCounterMath.Remaining(-3, 5) != 5)
                failures.Add("[never-negative] a bad field read must not subtract from the held count.");
            if (WaveCounterMath.Remaining(5, -3) != 5)
                failures.Add("[never-negative] a bad held read must not subtract from the field count.");
        }
    }
}
