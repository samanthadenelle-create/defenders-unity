// =============================================================================
// RaidAssaultAi — WO-1595 pure assault rules (phase + job + formation).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Owner north star (2026-09-07): breach walls → push / capture the spire; if
// aggro or being attacked prioritize staying alive; deploy and move as a
// formation (Front ahead, ranged/DPS behind, healers safe).
//
// Pure helpers live here so EditMode regressions can assert without a scene.
// TroopController / TroopDeployer call these; they do not replace NavMesh.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Combat;

namespace DeNelle.Village
{
    /// <summary>Assault phase — Survive/Peel beats Breach beats Push/Finish.</summary>
    public enum RaidAssaultPhase
    {
        Peel = 0,
        Breach = 1,
        Push = 2,
        Finish = 3,
    }

    /// <summary>Formation job mapped from <see cref="TroopDef.Role"/>.</summary>
    public enum RaidAssaultJob
    {
        Front = 0,
        Ranged = 1,
        Breaker = 2,
        Support = 3,
    }

    /// <summary>
    /// WO-1595 — pure selector + formation math for the raid assault loop.
    /// </summary>
    public static class RaidAssaultAi
    {
        /// <summary>Seconds after taking damage that count as "under attack" for Peel.</summary>
        public const float PeelHurtWindowSeconds = 2.5f;

        /// <summary>Leash (m) inside which a hostile unit forces Peel even without recent hurt.</summary>
        public const float PeelUnitLeashMeters = 6f;

        /// <summary>Front line sits this far ahead of the deploy point along the march axis.</summary>
        public const float FrontForwardMeters = 2.0f;

        /// <summary>Ranged / DPS hold this far behind the deploy point along the march axis.</summary>
        public const float RangedBackMeters = 3.5f;

        /// <summary>Support / healers hold farther back than ranged.</summary>
        public const float SupportBackMeters = 5.0f;

        /// <summary>Breaker sits slightly ahead of Front while cracking the approach.</summary>
        public const float BreakerForwardMeters = 1.25f;

        /// <summary>Lateral spacing between same-role slots (m).</summary>
        public const float LateralSpreadMeters = 1.4f;

        /// <summary>Map authored <c>TroopDef.Role</c> → assault job (no new JSON field).</summary>
        public static RaidAssaultJob JobFromRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return RaidAssaultJob.Front;
            switch (role.Trim().ToLowerInvariant())
            {
                case "ranged":
                case "caster":
                case "mage":
                    return RaidAssaultJob.Ranged;
                case "siege":
                    return RaidAssaultJob.Breaker;
                case "support":
                case "healer":
                    return RaidAssaultJob.Support;
                case "tank":
                case "melee":
                default:
                    return RaidAssaultJob.Front;
            }
        }

        /// <summary>
        /// Signed meters along the march axis (toward objective). Positive = ahead of deploy.
        /// </summary>
        public static float ForwardOffsetMeters(RaidAssaultJob job)
        {
            switch (job)
            {
                case RaidAssaultJob.Ranged: return -RangedBackMeters;
                case RaidAssaultJob.Support: return -SupportBackMeters;
                case RaidAssaultJob.Breaker: return BreakerForwardMeters;
                case RaidAssaultJob.Front:
                default: return FrontForwardMeters;
            }
        }

        /// <summary>
        /// World-space formation offset from a tap/deploy origin.
        /// <paramref name="marchForward"/> should be flat (y=0) and normalized toward the goal.
        /// </summary>
        public static Vector3 FormationWorldOffset(
            RaidAssaultJob job, int stackIndex, Vector3 marchForward, float lateralSpread = LateralSpreadMeters)
        {
            Vector3 forward = marchForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();

            float along = ForwardOffsetMeters(job);
            // Alternate left/right: 0 → 0, 1 → +1, 2 → -1, 3 → +2, …
            float lane = 0f;
            if (stackIndex > 0)
            {
                int n = (stackIndex + 1) / 2;
                lane = ((stackIndex % 2) == 1 ? 1f : -1f) * n;
            }

            return forward * along + right * (lane * lateralSpread);
        }

        /// <summary>
        /// Bias a push/breach destination so back-line jobs hold standoff behind the objective
        /// approach (Front closes; Ranged/Support stop short).
        /// </summary>
        public static Vector3 BiasMoveDestination(
            RaidAssaultJob job, Vector3 selfPos, Vector3 objectiveOrFoePos, float attackRange)
        {
            if (job == RaidAssaultJob.Front || job == RaidAssaultJob.Breaker)
                return objectiveOrFoePos;

            Vector3 flatSelf = new Vector3(selfPos.x, 0f, selfPos.z);
            Vector3 flatGoal = new Vector3(objectiveOrFoePos.x, 0f, objectiveOrFoePos.z);
            Vector3 toGoal = flatGoal - flatSelf;
            float dist = toGoal.magnitude;
            if (dist < 0.01f) return objectiveOrFoePos;

            float standoff = job == RaidAssaultJob.Support
                ? Mathf.Max(attackRange + SupportBackMeters - RangedBackMeters, attackRange)
                : Mathf.Max(attackRange * 0.85f, attackRange - 0.5f);

            if (dist <= standoff) return selfPos; // already far enough — hold

            float stopAt = dist - standoff;
            Vector3 hold = flatSelf + (toGoal / dist) * stopAt;
            hold.y = objectiveOrFoePos.y;
            return hold;
        }

        /// <summary>
        /// Priority stack: Peel → Breach → Finish → Push.
        /// </summary>
        public static RaidAssaultPhase ResolvePhase(
            bool peelThreat,
            bool routeToObjectiveOpen,
            bool objectiveInAttackRange)
        {
            if (peelThreat) return RaidAssaultPhase.Peel;
            if (!routeToObjectiveOpen) return RaidAssaultPhase.Breach;
            if (objectiveInAttackRange) return RaidAssaultPhase.Finish;
            return RaidAssaultPhase.Push;
        }

        /// <summary>
        /// Idle (no foe) destination rule — CLI review 2026-09-07.
        /// Rally flag beats spire push. Spire push only on Push/Finish (never Breach —
        /// marching into an intact wall with NoObstacleAvoidance freezes the troop).
        /// </summary>
        public static bool IdleShouldPushSpire(bool rallyPointSet, RaidAssaultPhase phase)
        {
            if (rallyPointSet) return false;
            return phase == RaidAssaultPhase.Push || phase == RaidAssaultPhase.Finish;
        }

        /// <summary>
        /// Owner 2026-09-12: troops must WALK TO THE RALLY, not chew the nearest exterior
        /// wall on the way. While a rally is set and the troop has not arrived, wall-ring
        /// picks are suppressed (Peel still wins if they are under attack).
        /// </summary>
        public static bool RallyHoldsMarch(bool rallySet, bool arrivedAtRally, bool peelThreat)
        {
            return rallySet && !arrivedAtRally && !peelThreat;
        }

        /// <summary>
        /// WO-1719: an EXPLICIT player breach order is not held by the rally march.
        /// </summary>
        /// <remarks>
        /// ⚠ WITHOUT THIS THE TICKET'S OWN ACCEPTANCE BULLET CANNOT PASS, and the reason is
        /// that phase and rally are INDEPENDENT axes. <see cref="ResolvePhase"/> reads
        /// peel / route / objective-range - never the rally - so a troop walking to a flag
        /// IS <see cref="RaidAssaultPhase.Breach"/>. The 3-arg rule above then nulls its
        /// other-structure bucket (TroopController), so "every Breach-phase troop retargets
        /// to the tapped panel" would silently fail for the whole warband any time a rally
        /// was set - which, mid-raid, is most of the time. The implicit ring-farm this
        /// suppression exists to stop (owner 2026-09-12) is untouched: only a panel the
        /// player explicitly tapped releases the march, and WO-1717 sec.6A asks for exactly
        /// that ("RallyHoldsMarch does not suppress an EXPLICIT breach order").
        /// </remarks>
        public static bool RallyHoldsMarch(
            bool rallySet, bool arrivedAtRally, bool peelThreat, bool hasExplicitBreachOrder)
        {
            if (hasExplicitBreachOrder) return false;
            return RallyHoldsMarch(rallySet, arrivedAtRally, peelThreat);
        }

        /// <summary>
        /// Owner 2026-09-12: the warband focuses ONE breach — the most damaged living
        /// wall. Ties (all full HP) stack on the panel nearest the muster/rally.
        /// </summary>
        public static IDamageable SelectFocusBreach(
            System.Collections.Generic.IList<IDamageable> walls, Vector3 muster)
        {
            return SelectFocusBreach(walls, muster, null);
        }

        /// <summary>
        /// WO-1719 — the same rule, with the player's EXPLICIT pick winning outright.
        /// </summary>
        /// <remarks>
        /// Owner ruling 2026-09-14: a wall tapped in Breach mode overrides the automatic
        /// most-damaged / nearest-muster computation, and that computation stays the
        /// FALLBACK for when no pick stands (or once the picked panel collapses).
        ///
        /// ⭐ THE OVERRIDE IS AN EARLY RETURN, DELIBERATELY - the selection body below is
        /// NOT restructured. WO-1717 sec.3d proved the auto pick is what overrides the
        /// local scan unconditionally, so the cheapest correct seam was to put one gate in
        /// FRONT of the existing rule rather than teach the loop about priorities. The
        /// fallback is then literally the same code it always was, which is why a
        /// regression can pin "auto still picks most-damaged" against an unchanged body.
        ///
        /// ⚠ The explicit pick is NOT required to appear in <paramref name="walls"/>. The
        /// player tapped that collider; a candidate list built from a cached scene scan
        /// (TroopController.SharedBreachFocus, 0.4 s) can trail the tap by a frame, and
        /// dropping the order for that would be an invisible, intermittent refusal. Its
        /// liveness is checked here instead.
        /// </remarks>
        public static IDamageable SelectFocusBreach(
            System.Collections.Generic.IList<IDamageable> walls, Vector3 muster,
            IDamageable explicitFocus)
        {
            if (explicitFocus != null && explicitFocus.IsAlive) return explicitFocus;
            if (walls == null || walls.Count == 0) return null;
            IDamageable best = null;
            float bestHp = float.MaxValue;
            float bestMusterSqr = float.MaxValue;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null || !w.IsAlive) continue;
                float hp = w.Hp;
                Vector3 d = w.WorldPosition - muster;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (best == null
                    || hp < bestHp - 0.5f
                    || (Mathf.Abs(hp - bestHp) <= 0.5f && sqr < bestMusterSqr))
                {
                    best = w;
                    bestHp = hp;
                    bestMusterSqr = sqr;
                }
            }
            return best;
        }

        /// <summary>
        /// Whether a hostile UNIT should beat structures for this phase.
        /// Peel always takes the unit when one exists. Push/Finish only peel units already
        /// in attack range (stay alive locally) — otherwise the objective wins over walls.
        /// Breach keeps the WO-1438 reachability gate for non-siege.
        /// </summary>
        public static bool PreferUnit(
            RaidAssaultPhase phase,
            bool preferStructures,
            bool hasUnit,
            bool hasStruct,
            bool unitInAttackRange,
            bool routeToUnitOpen)
        {
            if (!hasUnit) return false;
            if (phase == RaidAssaultPhase.Peel) return true;

            if (phase == RaidAssaultPhase.Push || phase == RaidAssaultPhase.Finish)
            {
                // Local survival only — do not chase distant units instead of the spire.
                return unitInAttackRange;
            }

            // Breach: siege stays on masonry; others use the existing reachability rule.
            if (preferStructures) return false;
            if (!hasStruct) return true;
            return unitInAttackRange || routeToUnitOpen;
        }

        /// <summary>
        /// After a hole exists (Push/Finish), non-siege must not farm the wall ring.
        /// Breach (and siege Breaker in Breach) may still pick approach structures.
        /// The objective (spire) is never "wall ring" — callers pass hasObjective separately.
        /// </summary>
        public static bool AllowNonObjectiveStructure(
            RaidAssaultPhase phase, bool preferStructures)
        {
            if (phase == RaidAssaultPhase.Breach) return true;
            // Siege may keep structure bias in Peel only if no unit (PreferUnit already handled);
            // in Push/Finish even siege joins the push — no ring farming.
            if (preferStructures && phase == RaidAssaultPhase.Peel) return true;
            return false;
        }

        /// <summary>
        /// Pick among unit / objective / other-structure / none after buckets are measured.
        /// Returns which bucket wins: 0=unit, 1=objective, 2=otherStruct, -1=none.
        /// </summary>
        public static int PickBucket(
            RaidAssaultPhase phase,
            bool preferStructures,
            bool hasUnit,
            bool hasObjective,
            bool hasOtherStruct,
            bool unitInAttackRange,
            bool routeToUnitOpen)
        {
            bool preferUnit = PreferUnit(
                phase, preferStructures, hasUnit, hasOtherStruct || hasObjective,
                unitInAttackRange, routeToUnitOpen);

            if (preferUnit && hasUnit) return 0;

            // Live gate for wall-ring farm: Push/Finish refuse non-objective masonry unless
            // AllowNonObjectiveStructure says otherwise (Breach / peel-siege only).
            bool mayWall = hasOtherStruct
                && AllowNonObjectiveStructure(phase, preferStructures);

            if (phase == RaidAssaultPhase.Push || phase == RaidAssaultPhase.Finish)
            {
                if (hasObjective) return 1;
                if (hasUnit) return 0; // nothing else — engage any unit rather than idle
                if (mayWall) return 2;
                return -1; // do not pick ring walls
            }

            if (phase == RaidAssaultPhase.Peel)
            {
                if (hasUnit) return 0;
                if (hasObjective) return 1;
                // Hurt by a tower with no unit in leash — do NOT resume wall-ring farming.
                return -1;
            }

            // Breach
            if (preferStructures)
            {
                if (mayWall) return 2;
                if (hasObjective) return 1;
                return hasUnit ? 0 : -1;
            }

            if (hasUnit && unitInAttackRange) return 0;
            if (mayWall) return 2;
            if (hasObjective) return 1;
            return hasUnit ? 0 : -1;
        }
    }
}
