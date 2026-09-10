// =============================================================================
// RaidEntryGate — the Core seam between the HudKit "Raids" button and the
// Village-side raid selection screen (owner F8 2026-07-30 "there is no raid
// option": the old VillageHudController crossed-swords icon raised RaidRequested,
// but the live HudKit HUD renders no raid widget at all — the raid loop had no
// visible door).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// Mirrors ObsidianQueueGate's toggle seam: the HUD (DeNelle.HUD, Core-only per
// the asmdef law) fires RequestOpen; the Village-side RaidEntryBridge subscribes
// and opens RaidSelectionScreen (whose Open() carries the WO-813 zero-troops
// safety net). No cross-assembly reference either direction.
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.UI
{
    /// <summary>Core seam: the HUD's Raids button requests; Village opens the screen.</summary>
    public static class RaidEntryGate
    {
        /// <summary>Raised by the HUD Raids button. Village-side RaidEntryBridge subscribes.</summary>
        public static event Action OpenRequested;

        /// <summary>Fired by the HudKit Raids button. Warns via the subscriber count being
        /// zero only implicitly — the bridge logs its own subscription, so a dead tap is
        /// visible as fire-with-no-open in the [Flow:Raid] trace.</summary>
        public static void RequestOpen() => OpenRequested?.Invoke();

        // ── Full-army status snapshot (owner ruling: Raids button greys unless the
        // army is full counting ready + queued troops; a dimmed tap redirects to the
        // drillmaster). Village publishes (BuildTimerService, ~1 Hz + on queue change);
        // the HUD polls ArmyStatus.Version — the ObsidianQueueGate.PublishStatus
        // precedent, no cross-assembly read. Pure Core: no Village references. ──

        /// <summary>Army fullness snapshot for the HUD Raids-button grey state.</summary>
        public struct RaidArmyStatus
        {
            public bool Ready;           // deployable + queued slots cover the cap
            public int DeployableSlots;  // healthy roster slots (wounded excluded)
            public int QueuedSlots;      // slots committed to in-flight Train jobs
            public int CapSlots;         // army.MaxArmySize
            public int Version;          // bumps ONLY on a value change (HUD change-detect)
            /// <summary>
            /// WO-1407: the slot bar Ready was judged against - ArmyReadiness.Snapshot.RequiredSlots
            /// relayed verbatim (the WO-823 first-raid soft gate: 3 while the save has never
            /// finished a raid, the cap afterwards). Surfaces that SAY a number ("Train 3 troops
            /// to unlock Raids") read THIS, never CapSlots, or the copy disagrees with the gate
            /// that produced it. 0 = never published (the Ready=true default).
            /// </summary>
            public int RequiredSlots;
            /// <summary>
            /// WO-1641: TRUE when the save has ALREADY finished a raid (ArmyReadiness.Snapshot.
            /// PastFirstRaid relayed verbatim). The bar this snapshot was judged against tells you
            /// the NUMBER; this bit tells you what falling short of it MEANS. Under the softened
            /// floor on a save that has never raided, raids are genuinely still locked; over it,
            /// raids are open and the army is merely short of the full-cap party the door now asks
            /// for - the same "Train N" sentence read as an UNLOCK in both, which is the defect.
            ///
            /// Never derive this from RequiredSlots == CapSlots: a small-cap army makes those equal
            /// on a save that has never raided.
            ///
            /// FALSE on the pre-WO-1641 4-arg publish and on the Ready=true default, so an
            /// un-migrated / headless / pre-publish surface keeps the pre-first-raid wording.
            /// </summary>
            public bool PastFirstRaid;
        }

        private static int _armyStatusVersion;

        /// <summary>Latest published army snapshot. Defaults READY (Version 0) so
        /// headless / pre-publish scenes never false-dim the button.</summary>
        public static RaidArmyStatus ArmyStatus { get; private set; } =
            new RaidArmyStatus { Ready = true };

        /// <summary>Village-side publisher (BuildTimerService). Bumps Version only when a
        /// field actually changed, so the HUD poll repaints on transitions alone.</summary>
        public static void PublishArmyStatus(bool ready, int deployableSlots, int queuedSlots, int capSlots)
        {
            // Pre-WO-1407 callers judged against the cap; keep that as the relayed bar.
            PublishArmyStatus(ready, deployableSlots, queuedSlots, capSlots, capSlots);
        }

        /// <summary>WO-1407 overload: also relays the slot bar <paramref name="ready"/> was
        /// judged against (ArmyReadiness.Snapshot.RequiredSlots) so copy can say the number.</summary>
        public static void PublishArmyStatus(bool ready, int deployableSlots, int queuedSlots, int capSlots,
                                             int requiredSlots)
        {
            // Pre-WO-1641 callers carried no first-raid bit; FALSE keeps the pre-first-raid wording.
            PublishArmyStatus(ready, deployableSlots, queuedSlots, capSlots, requiredSlots, false);
        }

        /// <summary>WO-1641 overload: also relays whether the save has ALREADY finished a raid
        /// (ArmyReadiness.Snapshot.PastFirstRaid) so the Heart plate can say what being short of
        /// the bar MEANS, instead of claiming an unlock the player already earned.</summary>
        public static void PublishArmyStatus(bool ready, int deployableSlots, int queuedSlots, int capSlots,
                                             int requiredSlots, bool pastFirstRaid)
        {
            var cur = ArmyStatus;
            if (cur.Ready == ready && cur.DeployableSlots == deployableSlots &&
                cur.QueuedSlots == queuedSlots && cur.CapSlots == capSlots &&
                cur.RequiredSlots == requiredSlots && cur.PastFirstRaid == pastFirstRaid)
                return;   // unchanged — Version holds, HUD stays quiet
            if (cur.Ready != ready)
                FlowTrace.Step("Raid", "army status -> " + (ready ? "READY" : "NOT READY") +
                    " (deployable " + deployableSlots + " + queued " + queuedSlots + " / cap " + capSlots +
                    ", required " + requiredSlots + ")");
            ArmyStatus = new RaidArmyStatus
            {
                Ready = ready,
                DeployableSlots = deployableSlots,
                QueuedSlots = queuedSlots,
                CapSlots = capSlots,
                RequiredSlots = requiredSlots,
                PastFirstRaid = pastFirstRaid,
                Version = ++_armyStatusVersion
            };
        }
    }
}
