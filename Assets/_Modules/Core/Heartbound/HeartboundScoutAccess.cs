// =============================================================================
// HeartboundScoutAccess - the Scout's Whisper flag. It moves WHEN the existing
// scout report is offered, and nothing else.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Heartbound
// WO-1678 (HEART-005). Owner ruling 2026-09-10 13:36, verbatim:
//
//   "Q-SCOUT: existing report, earlier - stakers see RaidDeployVM.ScoutReport
//    before committing troops; the information itself stays equal for everyone;
//    no new intel."
//
// ⛔ THAT RULING IS THE WHOLE CONTRACT AND IT IS A NEGATIVE ONE. This file holds a
// BOOLEAN AND A DEADLINE. It has no report, no lines, no composition, no
// resistance, no recommended troop type and no reward preview - every one of which
// the spec suggested (:541-547) and the ruling removed. A field of intel added
// here is new information bought with a financial position, which is the exact
// thing the ruling declined.
//
// ⛔ AND IT NEVER BUILDS A REPORT. The report is built ONCE, by
// RaidDeployVM.BuildScoutReport, and its last line must remain the spoils estimate
// (pinned by RaidDeployZeroArmyRegression [zero-army-spoils]). A second builder
// would be a second report; this flag exists precisely so there is no second one.
//
// ⚠ HONEST LIMIT - THE CONSUMER IS NOT WIRED BY WO-1678, AND SAYING SO IS THE
// POINT. The deploy screen already paints vm.ScoutIntel unconditionally, so
// "earlier" means a surface BEFORE the deploy screen - the raid selection card.
// That surface belongs to the raid lane, not to this ticket's files. This flag is
// therefore WRITTEN here and READ by nobody yet. A lane that wires it reads
// IsRevealEarly(now) at that one visibility site and touches nothing else.
//
// #if DAPP_STORE: Seeker-only by owner ruling 2026-09-10 13:10.
// =============================================================================

#if DAPP_STORE

using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Heartbound
{
    /// <summary>
    /// Whether the player may see the EXISTING scout report earlier than usual, and
    /// until when. No intel lives here.
    /// </summary>
    public static class HeartboundScoutAccess
    {
        private static double _revealUntilUnixMs;

        /// <summary>
        /// Grant early access for <paramref name="durationSeconds"/>. A later grant
        /// EXTENDS, never shortens - a second whisper must not cancel the first.
        /// </summary>
        public static void GrantEarlyReveal(double durationSeconds, double nowUnixMs)
        {
            if (durationSeconds <= 0d)
            {
                FlowTrace.Warn(HeartboundEventInbox.Sys, "Scout's Whisper granted with a non-positive " +
                                                          "duration (" + durationSeconds + "s) - refused. " +
                                                          "An authoring error must not read as a reward.");
                return;
            }

            double until = nowUnixMs + (durationSeconds * 1000d);
            if (until > _revealUntilUnixMs) _revealUntilUnixMs = until;

            FlowTrace.Step(HeartboundEventInbox.Sys, "Scout's Whisper: the EXISTING scout report is offered " +
                                                      "earlier for " + durationSeconds.ToString("F0") + "s. " +
                                                      "No new intel is created - the same walls, garrison, " +
                                                      "boss and spoils every player already sees, sooner.");
        }

        /// <summary>True while the early reveal is live. A total function of (state, now).</summary>
        public static bool IsRevealEarly(double nowUnixMs)
        {
            return nowUnixMs < _revealUntilUnixMs;
        }

        /// <summary>Seconds left on the early reveal, or 0.</summary>
        public static double SecondsRemaining(double nowUnixMs)
        {
            double left = (_revealUntilUnixMs - nowUnixMs) / 1000d;
            return left > 0d ? left : 0d;
        }

        /// <summary>Test/dev hook: forget the grant. Never called by gameplay.</summary>
        public static void DebugClear()
        {
            _revealUntilUnixMs = 0d;
        }
    }
}

#endif
