// =============================================================================
// RaidAssaultAiRegression [raid-assault-ai] — WO-1595.
// -----------------------------------------------------------------------------
// Pins the pure assault rules the live TroopController / TroopDeployer call:
//   * Peel beats Push (stay alive under aggro)
//   * Post-breach Push refuses non-objective wall farming
//   * Formation: Front ahead of Ranged on the march axis
//   * Day-one role map: melee→Front, ranged→Ranged, siege→Breaker, support→Support
//
// RED PROOF: Case_PostBreach_DoesNotPickWall — change PickBucket's Push branch to
// `if (hasOtherStruct) return 2` (ignore AllowNonObjectiveStructure) and the case
// fails. AllowNonObjectiveStructure is called from PickBucket (mayWall); mutating
// Allow to always-true alone is not enough unless PickBucket consults it — which it
// now does.
//
// Marker: RAID_ASSAULT_AI_OK / RAID_ASSAULT_AI_FAIL.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Combat;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class RaidAssaultAiRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== RaidAssaultAiRegression (WO-1595) ===\n");
            try
            {
                Case_JobFromRole_DayOneMap(failures, log);
                Case_Peel_UnderThreat_BeatsPush(failures, log);
                Case_PostBreach_DoesNotPickWall(failures, log);
                Case_Breach_MayPickWall(failures, log);
                Case_SiegeBreach_PrefersStructure(failures, log);
                Case_Push_LocalUnitInRange_Peels(failures, log);
                Case_Formation_FrontAheadOfRanged(failures, log);
                Case_Formation_Bias_RangedHoldsStandoff(failures, log);
                Case_IdleRallyBeatsSpirePush(failures, log);
                Case_AllowNonObjectiveWiredIntoPickBucket(failures, log);
                Case_RallyHoldsMarch_UntilArrival(failures, log);
                Case_FocusBreach_MostDamagedWins(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = log.ToString().TrimEnd() + "\nRAID_ASSAULT_AI_OK";
                return true;
            }

            reason = log.ToString() + string.Join("\n", failures) + "\nRAID_ASSAULT_AI_FAIL";
            return false;
        }

        private static void Case_JobFromRole_DayOneMap(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_JobFromRole_DayOneMap");
            if (RaidAssaultAi.JobFromRole("melee") != RaidAssaultJob.Front)
                failures.Add("melee must map to Front");
            if (RaidAssaultAi.JobFromRole("ranged") != RaidAssaultJob.Ranged)
                failures.Add("ranged must map to Ranged");
            if (RaidAssaultAi.JobFromRole("siege") != RaidAssaultJob.Breaker)
                failures.Add("siege must map to Breaker");
            if (RaidAssaultAi.JobFromRole("support") != RaidAssaultJob.Support)
                failures.Add("support must map to Support");
            if (RaidAssaultAi.JobFromRole("tank") != RaidAssaultJob.Front)
                failures.Add("tank must map to Front");
        }

        private static void Case_Peel_UnderThreat_BeatsPush(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_Peel_UnderThreat_BeatsPush");
            var phase = RaidAssaultAi.ResolvePhase(
                peelThreat: true, routeToObjectiveOpen: true, objectiveInAttackRange: false);
            if (phase != RaidAssaultPhase.Peel)
                failures.Add("peelThreat must resolve Peel even when route to spire is open");

            int bucket = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Peel, preferStructures: false,
                hasUnit: true, hasObjective: true, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false);
            if (bucket != 0)
                failures.Add("Peel must pick unit bucket (0), got " + bucket);
        }

        private static void Case_PostBreach_DoesNotPickWall(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_PostBreach_DoesNotPickWall");
            // Route open, Push, only a wall in sweep — must NOT pick the wall (ring farm ban).
            if (RaidAssaultAi.AllowNonObjectiveStructure(RaidAssaultPhase.Push, preferStructures: false))
                failures.Add("Push must not allow non-objective structures for non-siege");

            int bucket = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Push, preferStructures: false,
                hasUnit: false, hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false);
            if (bucket != -1)
                failures.Add("Push with only a wall must pick none (-1), got " + bucket
                    + " (this is the SS_11→SS_14 ring-walk defect)");
        }

        private static void Case_Breach_MayPickWall(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_Breach_MayPickWall");
            if (!RaidAssaultAi.AllowNonObjectiveStructure(RaidAssaultPhase.Breach, preferStructures: false))
                failures.Add("Breach must allow approach structures");

            int bucket = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: false,
                hasUnit: false, hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false);
            if (bucket != 2)
                failures.Add("Breach with only a wall must pick otherStruct (2), got " + bucket);
        }

        private static void Case_SiegeBreach_PrefersStructure(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_SiegeBreach_PrefersStructure");
            bool preferUnit = RaidAssaultAi.PreferUnit(
                RaidAssaultPhase.Breach, preferStructures: true,
                hasUnit: true, hasStruct: true, unitInAttackRange: true, routeToUnitOpen: true);
            if (preferUnit)
                failures.Add("siege in Breach must not PreferUnit even when unit is in range");

            int bucket = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, preferStructures: true,
                hasUnit: true, hasObjective: false, hasOtherStruct: true,
                unitInAttackRange: true, routeToUnitOpen: true);
            if (bucket != 2)
                failures.Add("siege Breach must pick otherStruct (2), got " + bucket);
        }

        private static void Case_Push_LocalUnitInRange_Peels(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_Push_LocalUnitInRange_Peels");
            // PreferUnit in Push with unit in attack range → true (local survival).
            bool prefer = RaidAssaultAi.PreferUnit(
                RaidAssaultPhase.Push, preferStructures: false,
                hasUnit: true, hasStruct: true, unitInAttackRange: true, routeToUnitOpen: false);
            if (!prefer)
                failures.Add("Push + unit in attack range must PreferUnit (stay alive)");

            // Distant unit, not in range → prefer objective path (PreferUnit false).
            bool distant = RaidAssaultAi.PreferUnit(
                RaidAssaultPhase.Push, preferStructures: false,
                hasUnit: true, hasStruct: true, unitInAttackRange: false, routeToUnitOpen: true);
            if (distant)
                failures.Add("Push must NOT chase a distant unit when route-to-unit is open — push the spire");

            int bucket = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Push, preferStructures: false,
                hasUnit: false, hasObjective: true, hasOtherStruct: true,
                unitInAttackRange: false, routeToUnitOpen: false);
            if (bucket != 1)
                failures.Add("Push with objective + wall must pick objective (1), got " + bucket);
        }

        private static void Case_Formation_FrontAheadOfRanged(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_Formation_FrontAheadOfRanged");
            Vector3 march = Vector3.forward;
            Vector3 front = RaidAssaultAi.FormationWorldOffset(RaidAssaultJob.Front, 0, march);
            Vector3 ranged = RaidAssaultAi.FormationWorldOffset(RaidAssaultJob.Ranged, 0, march);
            if (front.z <= ranged.z)
                failures.Add($"Front.z ({front.z:F2}) must be ahead of Ranged.z ({ranged.z:F2}) on +Z march");

            if (RaidAssaultAi.ForwardOffsetMeters(RaidAssaultJob.Front)
                <= RaidAssaultAi.ForwardOffsetMeters(RaidAssaultJob.Ranged))
                failures.Add("Front forward meters must exceed Ranged (Ranged is negative back)");
        }

        private static void Case_Formation_Bias_RangedHoldsStandoff(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_Formation_Bias_RangedHoldsStandoff");
            Vector3 self = Vector3.zero;
            Vector3 foe = new Vector3(0f, 0f, 20f);
            float attackRange = 14f;
            Vector3 hold = RaidAssaultAi.BiasMoveDestination(
                RaidAssaultJob.Ranged, self, foe, attackRange);
            float holdDist = Vector3.Distance(new Vector3(hold.x, 0f, hold.z), self);
            // Ranged should stop short of the foe (standoff), not march to contact.
            if (holdDist >= 19.5f)
                failures.Add($"Ranged bias should hold standoff, got holdDist={holdDist:F1} toward foe at 20");

            Vector3 frontDest = RaidAssaultAi.BiasMoveDestination(
                RaidAssaultJob.Front, self, foe, 2.5f);
            if (Vector3.Distance(frontDest, foe) > 0.01f)
                failures.Add("Front bias must return the foe position (close to contact)");
        }

        private static void Case_IdleRallyBeatsSpirePush(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_IdleRallyBeatsSpirePush");
            if (RaidAssaultAi.IdleShouldPushSpire(rallyPointSet: true, RaidAssaultPhase.Push))
                failures.Add("rally set must block idle spire push (CLI review blocking defect)");
            if (RaidAssaultAi.IdleShouldPushSpire(rallyPointSet: true, RaidAssaultPhase.Finish))
                failures.Add("rally set must block idle spire push in Finish");
            if (RaidAssaultAi.IdleShouldPushSpire(rallyPointSet: false, RaidAssaultPhase.Breach))
                failures.Add("Breach must NOT idle-push the spire (NoObstacleAvoidance into intact wall)");
            if (!RaidAssaultAi.IdleShouldPushSpire(rallyPointSet: false, RaidAssaultPhase.Push))
                failures.Add("Push with no rally must idle-push the spire");
            if (!RaidAssaultAi.IdleShouldPushSpire(rallyPointSet: false, RaidAssaultPhase.Finish))
                failures.Add("Finish with no rally must idle-push the spire");
        }

        private static void Case_AllowNonObjectiveWiredIntoPickBucket(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_AllowNonObjectiveWiredIntoPickBucket");
            // Live wiring: Push + only wall → -1 because AllowNonObjectiveStructure(Push)=false.
            if (RaidAssaultAi.AllowNonObjectiveStructure(RaidAssaultPhase.Push, false))
                failures.Add("AllowNonObjectiveStructure(Push) must be false");
            int bucket = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Push, false, false, false, true, false, false);
            if (bucket != -1)
                failures.Add("PickBucket must consult AllowNonObjectiveStructure — Push+wall got " + bucket);
            // Breach still allows walls via the same helper.
            if (!RaidAssaultAi.AllowNonObjectiveStructure(RaidAssaultPhase.Breach, false))
                failures.Add("AllowNonObjectiveStructure(Breach) must be true");
            int breach = RaidAssaultAi.PickBucket(
                RaidAssaultPhase.Breach, false, false, false, true, false, false);
            if (breach != 2)
                failures.Add("Breach+wall must pick otherStruct via mayWall, got " + breach);
        }

        private static void Case_RallyHoldsMarch_UntilArrival(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_RallyHoldsMarch_UntilArrival");
            if (!RaidAssaultAi.RallyHoldsMarch(true, false, false))
                failures.Add("rally set + not arrived + not peel must HOLD the march (no wall chew)");
            if (RaidAssaultAi.RallyHoldsMarch(true, true, false))
                failures.Add("arrived at rally must RELEASE so they can stack the breach wall");
            if (RaidAssaultAi.RallyHoldsMarch(true, false, true))
                failures.Add("peel must beat rally march");
            if (RaidAssaultAi.RallyHoldsMarch(false, false, false))
                failures.Add("no rally must not hold the march");
        }

        private static void Case_FocusBreach_MostDamagedWins(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_FocusBreach_MostDamagedWins");
            var fullNear = new StubWall { HpValue = 100f, Pos = Vector3.zero };
            var hurtFar = new StubWall { HpValue = 40f, Pos = new Vector3(20f, 0f, 0f) };
            var fullFar = new StubWall { HpValue = 100f, Pos = new Vector3(8f, 0f, 0f) };
            var pick = RaidAssaultAi.SelectFocusBreach(
                new IDamageable[] { fullNear, hurtFar, fullFar }, Vector3.zero);
            if (!ReferenceEquals(pick, hurtFar))
                failures.Add("most-damaged wall must win even when farther than a full-HP neighbour");

            var a = new StubWall { HpValue = 100f, Pos = new Vector3(10f, 0f, 0f) };
            var b = new StubWall { HpValue = 100f, Pos = new Vector3(3f, 0f, 0f) };
            var tie = RaidAssaultAi.SelectFocusBreach(new IDamageable[] { a, b }, Vector3.zero);
            if (!ReferenceEquals(tie, b))
                failures.Add("all-full HP must stack the panel nearest the muster, not wander the ring");
        }

        private sealed class StubWall : IDamageable
        {
            public float HpValue;
            public Vector3 Pos;
            public CombatFaction Faction => CombatFaction.Hostile;
            public Vector3 WorldPosition => Pos;
            public float Hp => HpValue;
            public bool IsAlive => HpValue > 0f;
            public void TakeDamage(float amount, DamageElement element) { }
            public void ApplyStatus(StatusEffect effect, float seconds) { }
        }
    }
}
