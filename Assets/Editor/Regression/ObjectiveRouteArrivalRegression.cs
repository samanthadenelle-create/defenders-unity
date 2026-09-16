// =============================================================================
// ObjectiveRouteArrivalRegression [objective-route-arrival] — WO-1730 §3B.
// -----------------------------------------------------------------------------
// Pins the ARRIVED rule that decides whether a raid troop's route to the spire is
// OPEN — the rule whose absence kept `routeOpen=True` at ZERO forever.
//
// ⛔ WHY THIS SUITE EXISTS, IN ONE PARAGRAPH. `TroopController.RefreshRouteToObjective`
// used to require `NavMesh.CalculatePath(... spire.WorldPosition ...) == PathComplete`.
// A spire wide enough to carve its own footprint out of the navmesh has NO polygon at
// its centre, so that query can only ever return PathPartial with the last corner on
// the carve edge — the criterion was UNSATISFIABLE. Consequence: ResolvePhase never
// left Breach, and troops ground the wall after the breach was already open, which is
// the owner's report that opened WO-1730 §3B (*"once the breach is through the troops
// should continue towards the spire or aggressive mobs not coninute to work down the
// wall"*).
//
// ⭐ CAPTURED PROOF, NOT INFERENCE (CLAUDE.md §12): `Builds/wo1730-assault-trace.log`
// (2026-09-15 21:46, RaidAssaultTraceCapture on RaidBase_raider_camp_small) — every
// sampled troop `routeGap=[last=4.7 straight=48.1..55.1 corners=4]`. They walked the
// full ~50 m and stopped 4.7 m from the spire's CENTRE, against the 4.30 m carve
// radius WO-1749 measured. Arrived; refused by the criterion. A path dying at the wall
// ring would have read tens of metres — that alternative is ruled OUT by the same line.
//
// ⚠ THE SAME DEFECT WAS ALREADY FIXED ONCE, ON THE OTHER SIDE. WO-1749 hit it in the
// BAKE probe and replaced the criterion there (RaidKeepReachRegression.ArrivalRadius),
// in its own words: *"PathComplete to an objective's CENTRE is unsatisfiable for
// anything wide enough to carve its own footprint"*. The runtime kept the old rule for
// another day. THAT is what this suite guards against recurring: the two sides must
// answer the same question the same way, or a bake can certify a scene the troops then
// refuse to path through — a green marker over a broken game (CLAUDE.md §8/§16).
//
// RED PROOF (run it before trusting this file):
//   * Case_ArrivedAtCarveEdge_IsOpen — revert RouteArrived to `return false;` (i.e.
//     restore "PathComplete or nothing") and the case fails with the captured 4.7 m.
//   * Case_SlackMatchesBakeProbe — change RaidAssaultAi.ArrivalSlackMeters away from
//     0.5 and the case fails, naming the bake probe it must agree with. This is the
//     one that catches a well-meaning "let's loosen it a bit" from re-opening the
//     bake/runtime disagreement.
//   * Case_StoppedAtWallRing_IsNotOpen — change RouteArrived to `return true;` and a
//     10 m gap reads as arrived, i.e. the phase machine would leave Breach while the
//     ring is still sealed. That is the failure mode WO-1730's own decision table
//     forbids papering over.
//
// Marker: OBJECTIVE_ROUTE_ARRIVAL_OK / OBJECTIVE_ROUTE_ARRIVAL_FAIL.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class ObjectiveRouteArrivalRegression
    {
        /// <summary>
        /// The spire footprint radius WO-1749 measured on the fixed bake, and the agent radius
        /// implied by it. Held here as the SCENARIO the captured log describes — not as a second
        /// copy of a live value. The live terms are measured at runtime
        /// (TroopController.ObjectiveFootprintRadius / LiveAgentRadius); these two numbers only
        /// reproduce the geometry the capture was taken in, so the assertions below are about the
        /// RULE rather than about today's art.
        /// </summary>
        private const float CapturedCarveRadius = 4.30f;

        /// <summary>The `last=` reading from every troop in Builds/wo1730-assault-trace.log.</summary>
        private const float CapturedLastCornerDistance = 4.7f;

        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log("OBJECTIVE_ROUTE_ARRIVAL_OK " + report);
            else Debug.LogError("OBJECTIVE_ROUTE_ARRIVAL_FAIL " + report);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== ObjectiveRouteArrivalRegression (WO-1730 §3B) ===\n");
            try
            {
                Case_ArrivedAtCarveEdge_IsOpen(failures, log);
                Case_StoppedAtWallRing_IsNotOpen(failures, log);
                Case_NoCorners_IsNeverArrived(failures, log);
                Case_SlackMatchesBakeProbe(failures, log);
                Case_ArrivalRadiusIsMeasuredNotHardcoded(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("threw: " + ex.Message);
            }

            reason = failures.Count == 0
                ? log.ToString() + "cases=5 all green"
                : log.ToString() + "FAILURES:\n - " + string.Join("\n - ", failures);
            return failures.Count == 0;
        }

        /// <summary>
        /// THE TICKET. The captured 4.7 m against the captured 4.30 m carve radius must read OPEN.
        /// </summary>
        private static void Case_ArrivedAtCarveEdge_IsOpen(List<string> failures, StringBuilder log)
        {
            // Agent radius 0.5 is the NavMesh default and the value WO-1749's probe falls back to.
            float arrival = RaidAssaultAi.ArrivalRadius(CapturedCarveRadius, 0.5f);
            bool arrived = RaidAssaultAi.RouteArrived(CapturedLastCornerDistance, arrival);
            log.AppendLine($"[arrived-at-carve-edge] last={CapturedLastCornerDistance:F1} " +
                           $"carve={CapturedCarveRadius:F2} arrivalRadius={arrival:F2} arrived={arrived}");
            if (!arrived)
            {
                failures.Add("WO-1730 §3B: the CAPTURED route (last=" + CapturedLastCornerDistance
                    + " m from the spire centre, carve radius " + CapturedCarveRadius
                    + " m, arrivalRadius " + arrival.ToString("F2")
                    + " m) must count as ARRIVED, i.e. routeOpen=True. It did not, which is the "
                    + "pre-WO-1730 behaviour: ResolvePhase never leaves Breach and troops grind the "
                    + "wall after the breach is open. Proof line: Builds/wo1730-assault-trace.log.");
            }
        }

        /// <summary>
        /// The other branch of WO-1730 §3B's decision table: a path that dies out at the wall ring
        /// is NOT an arrival, and must keep the troop in Breach.
        /// </summary>
        private static void Case_StoppedAtWallRing_IsNotOpen(List<string> failures, StringBuilder log)
        {
            float arrival = RaidAssaultAi.ArrivalRadius(CapturedCarveRadius, 0.5f);
            const float ringGap = 10f;
            bool arrived = RaidAssaultAi.RouteArrived(ringGap, arrival);
            log.AppendLine($"[stopped-at-ring] last={ringGap:F1} arrivalRadius={arrival:F2} arrived={arrived}");
            if (arrived)
            {
                failures.Add("WO-1730 §3B: a route whose last corner is " + ringGap + " m from the "
                    + "spire (arrivalRadius " + arrival.ToString("F2") + " m) must NOT count as "
                    + "arrived - that is the path dying at the WALL RING, and opening the route "
                    + "there would let the phase machine leave Breach while the ring is still "
                    + "sealed. WO-1730's own table forbids papering over a pathing gap.");
            }
        }

        /// <summary>A path with no corners is a failed query, never an arrival at the objective.</summary>
        private static void Case_NoCorners_IsNeverArrived(List<string> failures, StringBuilder log)
        {
            float arrival = RaidAssaultAi.ArrivalRadius(CapturedCarveRadius, 0.5f);
            bool arrived = RaidAssaultAi.RouteArrived(-1f, arrival);
            log.AppendLine($"[no-corners] last=-1 arrived={arrived}");
            if (arrived)
            {
                failures.Add("WO-1730 §3B: a corner-less path (-1 sentinel: invalid path or failed "
                    + "CalculatePath) must never read as arrived. Returning 0 instead of -1 for "
                    + "that case would read as 'standing exactly on the spire' and open the route "
                    + "on a broken query.");
            }
        }

        /// <summary>
        /// THE BAKE/RUNTIME AGREEMENT. The runtime slack must equal the bake probe's, or a bake can
        /// certify a scene the troops refuse to path through.
        /// </summary>
        private static void Case_SlackMatchesBakeProbe(List<string> failures, StringBuilder log)
        {
            // WO-1749's RaidKeepReachRegression.ArrivalSlack is private, so this pins the VALUE it
            // documents (0.5 m) rather than reaching into it by reflection - CLAUDE.md §10 forbids
            // new reflection in bridge code, and a reflected private const would break silently on
            // a rename anyway. The comment on RaidAssaultAi.ArrivalSlackMeters names the file and
            // the line, which is the pointer a reader needs.
            log.AppendLine($"[slack-agreement] RaidAssaultAi.ArrivalSlackMeters={RaidAssaultAi.ArrivalSlackMeters:F2}");
            if (Mathf.Abs(RaidAssaultAi.ArrivalSlackMeters - 0.5f) > 0.0001f)
            {
                failures.Add("WO-1730 §3B: RaidAssaultAi.ArrivalSlackMeters is "
                    + RaidAssaultAi.ArrivalSlackMeters.ToString("F2") + " but MUST be 0.50 to match "
                    + "RaidKeepReachRegression.ArrivalSlack (Assets/Editor/Regression/"
                    + "RaidKeepReachRegression.cs). The bake probe withholds RAID_NAV_BAKE_OK using "
                    + "that number; if the runtime uses a different one, a bake can certify a scene "
                    + "whose objective the troops then refuse to path to - the green-marker lie "
                    + "CLAUDE.md §8/§16 are written against. Change BOTH, deliberately, or neither.");
            }
        }

        /// <summary>
        /// The arrival radius must GROW with the objective's footprint. A rule that ignored the
        /// footprint would work on raider_camp_small and fail on a wider spire — exactly the
        /// hardcoded-constant failure WO-1749 avoided by measuring the spire's lift.
        /// </summary>
        private static void Case_ArrivalRadiusIsMeasuredNotHardcoded(List<string> failures, StringBuilder log)
        {
            float narrow = RaidAssaultAi.ArrivalRadius(2f, 0.5f);
            float wide = RaidAssaultAi.ArrivalRadius(8f, 0.5f);
            log.AppendLine($"[measured-radius] footprint2->{narrow:F2} footprint8->{wide:F2}");
            if (wide <= narrow)
            {
                failures.Add("WO-1730 §3B: ArrivalRadius must scale with the objective's footprint "
                    + "(2 m -> " + narrow.ToString("F2") + ", 8 m -> " + wide.ToString("F2")
                    + "). A fixed radius would pass on the narrow spire the capture was taken on "
                    + "and strand troops at a wider one.");
            }
            // And the agent's own radius counts too - a fat unit needs to stop further out.
            if (RaidAssaultAi.ArrivalRadius(4f, 1.2f) <= RaidAssaultAi.ArrivalRadius(4f, 0.3f))
            {
                failures.Add("WO-1730 §3B: ArrivalRadius must scale with the AGENT radius as well "
                    + "as the footprint - both terms are measured live for that reason.");
            }
        }
    }
}
