// =============================================================================
// ArmyReadiness — THE one army-readiness formula (owner review 2026-08-01).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WO-820 computed "is the army full" in THREE places (BuildTimerService.
// PublishArmyStatus, RaidSelectionScreen.Open, BarracksService.EnqueueTraining)
// and PM review flagged the triple-drift risk. This static class is now the
// SINGLE source of that math — every consumer calls Compute() and reads the
// Snapshot; NEVER re-roll the formula locally. The formula:
//
//   deployable = sum of SlotOf over Army.GetDeployable()   (wounded excluded)
//   roster     = Army.SlotsUsed(SlotOf)                    (ALL roster incl. wounded)
//   queued     = BarracksService.CommittedTrainingSlots()  (in-flight Train jobs)
//   cap        = Army.MaxArmySize
//   required   = cap, OR FirstRaidMinDeployableSlots (3) while the save has never
//                finished a raid (WO-823 Phase E soft gate, GameState.EverCompletedRaid)
//   Ready      = deployable + queued >= required
//
// WO-823 Phase E also made this the ONLY readiness opinion again: RaidDeployScreen
// used to gate on a raw HEADCOUNT (_vm.DeployableCount) while this file is
// slot-weighted, so the two disagreed about what "enough army" means. Both of those
// sites now read this Snapshot. Never add a third.
//
// Pure aside from two service reads: BuildTimerService.Instance (via
// CommittedTrainingSlots, 0 when absent) and ModifierService (via MaxArmySize).
// All same assembly (DeNelle.Village) — no cross-assembly pull.
// =============================================================================

using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>
    /// The single army-readiness formula (owner review 2026-08-01). Consumers:
    /// BuildTimerService.PublishArmyStatus (HUD Raids-button grey state),
    /// RaidSelectionScreen.Open (authoritative entry gate), and
    /// BarracksService.EnqueueTraining (over-queue cap seed).
    /// </summary>
    public static class ArmyReadiness
    {
        /// <summary>
        /// WO-823 Phase E - the softened FIRST-RAID bar, in SLOTS (owner ruling 2026-08-24:
        /// "soften the first raid. THE NUMBER IS 3 OF 10", and "3 of 10" means slots).
        ///
        /// THE ONE DEFINITION AND THE ONE READ BOTH LIVE IN THIS FILE, deliberately. Phase E
        /// exists because readiness had grown a second opinion inside the raid screen; a copy
        /// of this constant anywhere else would be a third. Surfaces that need the number for
        /// COPY read Snapshot.RequiredSlots instead.
        /// </summary>
        public const int FirstRaidMinDeployableSlots = 3;

        /// <summary>One computed readiness snapshot — see class header for the formula.</summary>
        public struct Snapshot
        {
            /// <summary>Healthy roster slots (wounded excluded — GetDeployable skips them).</summary>
            public int DeployableSlots;
            /// <summary>Slots committed to in-flight Train-channel jobs (active + pending).</summary>
            public int QueuedSlots;
            /// <summary>Army cap (Army.MaxArmySize — base + perk bonus).</summary>
            public int CapSlots;
            /// <summary>ALL roster slots incl. wounded (SlotsUsed) — the enqueue-cap seed.</summary>
            public int RosterSlots;
            /// <summary>True when DeployableSlots + QueuedSlots cover RequiredSlots.</summary>
            public bool Ready;
            /// <summary>
            /// The slot bar this snapshot was judged against (WO-823 Phase E). Normally
            /// <see cref="CapSlots"/>; on a save that has never finished a raid it is the
            /// softened <see cref="FirstRaidMinDeployableSlots"/>. Surfaces that SAY the
            /// number ("Army 4 of 10") must read this, not CapSlots, or the copy will
            /// disagree with the gate that produced it.
            /// </summary>
            public int RequiredSlots;
            /// <summary>
            /// True when this snapshot was judged against the softened first-raid bar
            /// (WO-823 Phase E). PRESENTATION ONLY - it may WORD the copy and drive the
            /// slot meter; it must NEVER re-decide whether the raid door opens. That
            /// decision is <see cref="Ready"/> and it is already made here.
            /// </summary>
            public bool FirstRaidSoftGate;
            /// <summary>
            /// WO-1641: TRUE when this save has already finished a raid (the WO-823 latch).
            /// PRESENTATION ONLY - like <see cref="FirstRaidSoftGate"/> it may WORD the copy and
            /// must never re-decide the door; <see cref="Ready"/> is already that decision.
            ///
            /// IT IS NOT THE SAME BIT AS <see cref="FirstRaidSoftGate"/> AND MUST NOT BE DERIVED
            /// FROM IT. Soft is "required &lt; cap", so an army whose cap is at or below the
            /// softened floor reads soft=false on a save that has never raided - exactly the
            /// player this bit has to tell apart.
            ///
            /// NAMED "PastFirstRaid", NOT the obvious name, ON PURPOSE: FirstRaidSoftGateRegression
            /// gate 7 sweeps every runtime file outside RaidDeployController and Core/State for an
            /// ASSIGNMENT of the flag's own name and reds on it, because a second writer forks the
            /// one-owner latch. This field only ever COPIES the value in, so it must not wear that
            /// name (the initializer below would read as a write).
            /// </summary>
            public bool PastFirstRaid;
        }

        /// <summary>
        /// Compute readiness from <paramref name="st"/>. Null st/Army publishes READY with
        /// zeros so headless/AutoPilot never false-blocks (mirrors the WO-813 / WO-820
        /// stateless bypass — a missing GameState must never dim or gate the raid door).
        /// </summary>
        public static Snapshot Compute(GameState st)
        {
            if (st == null || st.Army == null)
                return new Snapshot { Ready = true };   // headless never-false-block rule

            var army = st.Army;
            int deployable = 0;
            foreach (var t in army.GetDeployable())
                deployable += TroopDialogueCommands.SlotOf(t.TroopDefId);

            return Compute(army, deployable, BarracksService.CommittedTrainingSlots(), st.EverCompletedRaid);
        }

        /// <summary>
        /// Seam overload for EditMode tests (BuildTimerService is a runtime MonoBehaviour,
        /// so CommittedTrainingSlots is 0 headless): same formula, injected queued count.
        /// Public because the test assembly is separate; runtime callers use Compute(GameState).
        /// </summary>
        public static Snapshot Compute(ArmyStorage army, int deployableSlots, int queuedSlots,
            bool everCompletedRaid = true)
        {
            int cap = army.MaxArmySize;

            // WO-823 Phase E - the FIRST RAID is softened, and only the first.
            // The owner ruling is literal: "soften the first raid. THE NUMBER IS 3 OF 10",
            // and "3 of 10" means SLOTS, not a headcount - so three 1-slot basics fit, one
            // 2-slot elite plus a basic fits, and a 4-slot catapult does not fit at all.
            // That cost trade IS the decision the player gets to make. On a fresh save at
            // Barracks tier 1 only footman and archer are trainable and both cost 1 slot,
            // so the bar is three tier-1 basics by construction.
            // The bar reverts to the full cap the moment EverCompletedRaid is stamped by
            // RaidDeployController.ReconcileRaidEnd, and never softens again.
            // ONE read of the constant, on purpose - see its doc comment.
            int required = everCompletedRaid ? cap : FirstRaidMinDeployableSlots;
            // A small-cap army is never made HARDER by the soft gate: the softened bar
            // can only ever LOWER the requirement, never raise it.
            if (required > cap) required = cap;
            bool soft = required < cap;

            return new Snapshot
            {
                DeployableSlots = deployableSlots,
                QueuedSlots = queuedSlots,
                CapSlots = cap,
                RosterSlots = army.SlotsUsed(TroopDialogueCommands.SlotOf),
                RequiredSlots = required,
                FirstRaidSoftGate = soft,
                PastFirstRaid = everCompletedRaid,
                Ready = deployableSlots + queuedSlots >= required
            };
        }
    }
}
