// =============================================================================
// HeartboundModifiers - the time-boxed economic modifiers an Echo Event grants,
// and the ONLY place they live. Pure state + arithmetic; no clock of its own.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Heartbound
// WO-1678 (HEART-005). Owner rulings 2026-09-10 13:10 (economic acceleration is
// allowed, combat power is not) and 13:20 (Echo Labor is a MODIFIER - no world
// actors; the Echo appearance owner stays the one owner).
//
// ⛔ THREE LANES, AND ALL THREE ARE ECONOMIC. Gather rate, build speed, craft
// speed. There is no lane for damage, armour, health or troop strength, and a
// future edit that adds one is the defect the allow-list exists to make visible.
// HeartboundEventRegression fails on a combat-stat lane name.
//
// ⚠ WHAT IS HONESTLY NOT DONE HERE, SO NOBODY READS MORE INTO IT.
// This bag is WRITTEN by the Echo Event applier and, as of WO-1678, is READ by
// nothing in the economy. The consumers are the passive benefit table (WO-1679)
// and the acceleration meter (WO-1682), and the meter is the whole point: the
// owner's ruling of 2026-09-10 13:36 puts a 10% ceiling on the SUM of production-
// rate modifiers, computed server-side. Wiring these numbers into EchoService or
// the build queue BEFORE that meter exists would be shipping an uncapped
// acceleration and calling it capped. So the seam lands here, instrumented, and
// the wiring lands with its ceiling. Stated rather than implied.
//
// ⛔ TIME COMES FROM THE CALLER. Same reason as HeartfireCharges: a modifier
// stamped off the device clock is extended forever by anyone who opens Settings >
// Date & Time. DeNelle.Core cannot see TimeSource (it lives in DeNelle.Village),
// and that asymmetry makes reading the wrong clock from this file impossible.
//
// #if DAPP_STORE: Seeker-only by owner ruling 2026-09-10 13:10.
// =============================================================================

#if DAPP_STORE

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Heartbound
{
    /// <summary>
    /// The active Heartbound economic modifiers. Every entry expires; nothing here
    /// is permanent, and nothing here touches combat.
    /// </summary>
    public static class HeartboundModifiers
    {
        /// <summary>Gathering / production rate. Spec's "Resource Surge".</summary>
        public const string LaneGatherRate = "gather_rate";

        /// <summary>Construction acceleration. Spec's "Echo Labor" (Tier II identity).</summary>
        public const string LaneBuildSpeed = "build_speed";

        /// <summary>Crafting convenience. Spec's "Crafting Inspiration".</summary>
        public const string LaneCraftSpeed = "craft_speed";

        /// <summary>
        /// The ONLY lanes that may exist. An allow-list rather than a deny-list, because
        /// the ways to add an economic lane are few and named while the ways to smuggle
        /// in a combat stat are many.
        /// </summary>
        public static readonly string[] AllowedLanes = { LaneGatherRate, LaneBuildSpeed, LaneCraftSpeed };

        /// <summary>An active modifier: how big, and when it stops.</summary>
        public struct Entry
        {
            /// <summary>Fraction, not percent: 0.15 means +15%.</summary>
            public double Magnitude;

            /// <summary>Unix-ms the modifier expires. Supplied by the caller's clock.</summary>
            public double ExpiresUnixMs;
        }

        private static readonly Dictionary<string, Entry> Active = new Dictionary<string, Entry>();

        /// <summary>True when <paramref name="lane"/> is one this feature may move.</summary>
        public static bool IsAllowedLane(string lane)
        {
            if (string.IsNullOrWhiteSpace(lane)) return false;
            for (int i = 0; i < AllowedLanes.Length; i++)
            {
                if (string.Equals(AllowedLanes[i], lane, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Grant a time-boxed modifier. REFUSES an unknown lane rather than creating one:
        /// a lane the allow-list does not name is either a typo or a smuggled stat, and
        /// both must fail loudly.
        ///
        /// <para>Re-granting a lane that is already running takes the LATER expiry and
        /// the LARGER magnitude rather than stacking them. Stacking would let a run of
        /// pulses compound past any ceiling, which is the failure the acceleration meter
        /// (WO-1682) exists to bound.</para>
        /// </summary>
        public static bool Grant(string lane, double magnitude, double durationSeconds, double nowUnixMs)
        {
            if (!IsAllowedLane(lane))
            {
                FlowTrace.Warn(HeartboundEventInbox.Sys, "refused a Heartbound modifier for lane '" +
                                                         (lane ?? "null") + "' - only " +
                                                         string.Join("/", AllowedLanes) + " exist. An " +
                                                         "unknown lane is a typo or a smuggled stat.");
                return false;
            }

            if (magnitude <= 0d || durationSeconds <= 0d)
            {
                FlowTrace.Warn(HeartboundEventInbox.Sys, "refused a Heartbound modifier for '" + lane +
                                                         "' with magnitude " + magnitude + " and duration " +
                                                         durationSeconds + "s - a non-positive reward is " +
                                                         "an authoring error, not a zero-value grant.");
                return false;
            }

            double expiry = nowUnixMs + (durationSeconds * 1000d);
            if (Active.TryGetValue(lane, out var existing))
            {
                double keptMagnitude = existing.Magnitude > magnitude ? existing.Magnitude : magnitude;
                double keptExpiry = existing.ExpiresUnixMs > expiry ? existing.ExpiresUnixMs : expiry;
                Active[lane] = new Entry { Magnitude = keptMagnitude, ExpiresUnixMs = keptExpiry };
                FlowTrace.Step(HeartboundEventInbox.Sys, "Heartbound modifier '" + lane + "' RENEWED to +" +
                                                          (keptMagnitude * 100d).ToString("F1") + "% - the " +
                                                          "larger magnitude and later expiry win. Modifiers " +
                                                          "do NOT stack; stacking would compound past the " +
                                                          "acceleration ceiling.");
                return true;
            }

            Active[lane] = new Entry { Magnitude = magnitude, ExpiresUnixMs = expiry };
            FlowTrace.Step(HeartboundEventInbox.Sys, "Heartbound modifier '" + lane + "' granted: +" +
                                                      (magnitude * 100d).ToString("F1") + "% for " +
                                                      durationSeconds.ToString("F0") + "s. Economic only - " +
                                                      "no Heartbound reward touches combat.");
            return true;
        }

        /// <summary>
        /// The live magnitude for a lane at <paramref name="nowUnixMs"/>, or 0 when nothing
        /// is running. Expired entries are dropped on read - there is no ticker, for the
        /// same reason Heartfire has none: a total function of (state, now) is what an
        /// oracle can drive with no scene loaded.
        /// </summary>
        public static double MagnitudeFor(string lane, double nowUnixMs)
        {
            if (string.IsNullOrWhiteSpace(lane)) return 0d;
            if (!Active.TryGetValue(lane, out var entry)) return 0d;
            if (nowUnixMs >= entry.ExpiresUnixMs)
            {
                Active.Remove(lane);
                FlowTrace.Step(HeartboundEventInbox.Sys, "Heartbound modifier '" + lane + "' expired.");
                return 0d;
            }
            return entry.Magnitude;
        }

        /// <summary>Seconds left on a lane, or 0. For a reveal surface's countdown.</summary>
        public static double SecondsRemaining(string lane, double nowUnixMs)
        {
            if (string.IsNullOrWhiteSpace(lane)) return 0d;
            if (!Active.TryGetValue(lane, out var entry)) return 0d;
            double left = (entry.ExpiresUnixMs - nowUnixMs) / 1000d;
            return left > 0d ? left : 0d;
        }

        /// <summary>Test/dev hook: drop every active modifier. Never called by gameplay.</summary>
        public static void DebugClear()
        {
            Active.Clear();
        }
    }
}

#endif
