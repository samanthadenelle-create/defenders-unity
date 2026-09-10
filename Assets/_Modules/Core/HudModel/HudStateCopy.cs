// =============================================================================
// HudStateCopy - WO-1407: the town HUD's STATE sentences, resolved in Core.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.HudModel
//
// Merged UI review 2026-09-05 row 6: "nothing tells a non-raid-capable player how
// to become one; the Heart plate line 'Prepare the realm for the next wave.' is
// static; no idle-builders surface". The words are now DERIVED from the Core
// posture rail + the Core army seam, here, and the View (HudKitController) only
// paints what these return - so a regression suite (DeNelle.EditorRegression may
// reference Core, never DeNelle.HUD) can drive every state with a fixture and
// read the exact sentence the plate will show.
//
// WHY THE INPUTS ARE WHAT THEY ARE (read at source, 2026-09-05):
//   * PostureSignals.RaidCapable / RaidLock - Village-published by
//     RaidCapabilityHudBridge.ComputeCapable: capable = FeatureFlags.Raid AND a
//     Barracks stands (StructureSingleton.IsBuilt("barracks")). Since WO-1008 the
//     TROOP COUNT IS NOT A CAPABILITY CLAUSE: a standing Barracks with an empty
//     army is CAPABLE (the face shows, dimmed). So "has a Barracks" == RaidCapable
//     here, and the lock reason names the remedy (NoBarracks vs BarracksLost).
//   * RaidEntryGate.ArmyStatus - Village-published by BuildTimerService.
//     PublishArmyStatus from ArmyReadiness.Compute: Ready, Deployable/Queued/Cap
//     slots and (WO-1407) RequiredSlots = the WO-823 soft gate (3 on a save that
//     has never finished a raid, the cap afterwards). "Train N" is
//     Required - (Deployable + Queued), the same arithmetic the raid door refuses on.
//     (WO-1641) PastFirstRaid rides the same snapshot: the bar gives the NUMBER, this
//     bit gives its MEANING - genuinely locked before the first raid, merely short of
//     the full-cap party after it. It is a wording input; Ready is still the door.
//
// Player copy resolves through LocalText. GlyphCoverageRegression validates every
// enabled-locale character against the tracked static runtime font.
// =============================================================================

using System;
using DeNelle.Core.UI;

namespace DeNelle.Core.HudModel
{
    /// <summary>
    /// The Heart of Elarion plate's line 2 - one sentence that carries STATE. Pure:
    /// no statics are read here, so a fixture can drive every branch.
    /// </summary>
    public static class HeartObjectiveCopy
    {
        public const string KeyTitle = HeartHudText.KeyTitle;
        public const string KeyDefend = HeartHudText.KeyDefend;
        public const string KeyPrepareWave = HeartHudText.KeyPrepareWave;
        public const string KeyBuildBarracks = HeartHudText.KeyBuildBarracks;
        public const string KeyTrainOne = HeartHudText.KeyTrainOne;
        public const string KeyTrainOther = HeartHudText.KeyTrainOther;
        public const string KeyTrainNextRaidOne = HeartHudText.KeyTrainNextRaidOne;
        public const string KeyTrainNextRaidOther = HeartHudText.KeyTrainNextRaidOther;

        public static string Title => HeartHudText.Title.Resolve();
        /// <summary>The hostile-posture line (unchanged from the pre-WO-1407 View).</summary>
        public static string Defend => HeartHudText.Defend.Resolve();
        /// <summary>The raid-capable, army-ready line (the pre-WO-1407 static sentence,
        /// now reachable only when the player has nothing to unlock).</summary>
        public static string PrepareWave => HeartHudText.PrepareWave.Resolve();
        /// <summary>No Barracks stands (never built, or lost) - the door is the Build
        /// screen's Realm collection. The same line for NoBarracks and BarracksLost: the
        /// remedy is identical on this plate (build one) and the Journey card already
        /// distinguishes the two (PostureSignals.RaidLockCopy).</summary>
        public static string BuildBarracks => HeartHudText.BuildBarracks.Resolve();

        /// <summary>The train line for <paramref name="troops"/> more slots.</summary>
        public static string TrainTroops(int troops)
        {
            if (troops < 1) troops = 1;
            var arguments = new HeartTroopsArguments(troops);
            return troops == 1
                ? HeartHudText.TrainOne.Resolve(arguments)
                : HeartHudText.TrainOther.Resolve(arguments);
        }

        /// <summary>
        /// WO-1641 - the train line for a player who has ALREADY finished a raid.
        ///
        /// The number and the arithmetic are identical to <see cref="TrainTroops"/>; only the
        /// PROMISE changes. Measured on the device 2026-09-10 (build 363529): 108 ms after
        /// RaidDeployController stamped the first-raid latch, RequiredSlots moved 3 -> 10 and this
        /// plate repainted, correctly and on time, to "Train 2 troops to unlock Raids" - at a
        /// player who had just come back from a raid. Nothing was stale and nothing had failed;
        /// the one sentence under !Ready simply had no way to say "the door is open, your party is
        /// short". Now it does.
        /// </summary>
        public static string TrainNextRaid(int troops)
        {
            if (troops < 1) troops = 1;
            var arguments = new HeartTroopsArguments(troops);
            return troops == 1
                ? HeartHudText.TrainNextRaidOne.Resolve(arguments)
                : HeartHudText.TrainNextRaidOther.Resolve(arguments);
        }

        /// <summary>
        /// Resolve the plate's line 2.
        /// hostile -> <see cref="Defend"/>;
        /// !raidCapable with a Barracks lock (NoBarracks / BarracksLost) -> <see cref="BuildBarracks"/>;
        /// raidCapable and the army is short of the WO-823 bar -> <see cref="TrainTroops"/> before
        /// the first raid, <see cref="TrainNextRaid"/> after it (WO-1641);
        /// otherwise (ready, or the flag is off and nothing the player does can open the
        /// door) -> <see cref="PrepareWave"/>.
        /// <paramref name="troopsNeeded"/> is the N the train line names (0 when not that state).
        /// </summary>
        public static string Resolve(bool hostile, bool raidCapable, PostureSignals.RaidLockReason lockReason,
                                     RaidEntryGate.RaidArmyStatus army, out int troopsNeeded)
        {
            troopsNeeded = 0;
            if (hostile) return Defend;
            if (!raidCapable)
            {
                if (lockReason == PostureSignals.RaidLockReason.NoBarracks ||
                    lockReason == PostureSignals.RaidLockReason.BarracksLost)
                    return BuildBarracks;
                // FlagOff (or an unnamed lock): there is no player action to name.
                return PrepareWave;
            }
            if (!army.Ready)
            {
                // RequiredSlots 0 = a pre-WO-1407 publish (or none) - fall back to the cap so
                // the number is never negative or invented; the ready bit is still the gate.
                int required = army.RequiredSlots > 0 ? army.RequiredSlots : army.CapSlots;
                int have = Math.Max(0, army.DeployableSlots) + Math.Max(0, army.QueuedSlots);
                troopsNeeded = Math.Max(1, required - have);
                // WO-1641: same shortfall, two meanings. Before the first raid the door really is
                // locked and "unlock Raids" is the truth; after it the door is open and the army is
                // short of the full-cap party it now asks for. The bit rides the snapshot
                // (RaidArmyStatus.PastFirstRaid) precisely so this stays Core presentation - the
                // HUD may not reach GameState, and this branch must not re-decide the door.
                return army.PastFirstRaid ? TrainNextRaid(troopsNeeded) : TrainTroops(troopsNeeded);
            }
            return PrepareWave;
        }
    }

    /// <summary>
    /// The right-column Builders chip's words (WO-778 chip, WO-1407 idle state). The chip
    /// is a STATUS GLANCE (CLAUDE.md s7: the Manage bar face is the one Queues door), so
    /// it must still SAY something when nothing is building - "Builders idle 2" names the
    /// free hands the player is not using.
    /// </summary>
    public static class BuildersChipCopy
    {
        /// <summary>The chip's text for a published queue status. Never empty.</summary>
        public static string Format(ObsidianQueueGate.WorkQueueStatus s)
        {
            if (!s.Available) return "Builders";
            string line;
            if (s.BuilderBusy <= 0)
            {
                // WO-1407: idle is a STATE worth a word. "Builders idle 2" = two free hands.
                line = "Builders idle " + Math.Max(0, s.BuilderSlots);
            }
            else
            {
                line = "Builders " + s.BuilderBusy + "/" + s.BuilderSlots;
            }
            // "Train", not "Training": at 1920x1080 the longer word ellipsized to
            // "Trainin..." in the 2026-08-03 capture. The counts are the load-bearing part.
            // NEWLINE, NOT " | " (2026-08-05): the Body font's numeral 1 is a bare stroke and
            // "Builders 1/2 | Train 1" read as three identical marks. The chip is MinTouchPx
            // tall and its label wraps, so the second line is already paid for.
            if (s.TrainBusy > 0) line += "\nTrain " + s.TrainBusy;
            return line;
        }
    }
}
