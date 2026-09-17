// =============================================================================
// RaidDoorReadiness - ONE predicate for "the raid door is open and this player has
// never walked through it" (WO-1802).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.HudModel
//
// Owner ruling 2026-09-16, verbatim: "make the raid door obvious after founding".
//
// Evidence that produced this file (docs/LIVE_PLAYERS_TRIAGE_2026-09-16.md): ZERO
// players launched a raid that day and three in seven days, while FOUR ids reached
// founding_path_selected and the starter-army grant fired
// raid_funnel_barracks_unlocked + raid_funnel_army_trained in the same second for
// them. Not one emitted raid_funnel_first_raid_attempted. The door was open, the
// army was in the barracks, and nobody found it.
//
// -----------------------------------------------------------------------------
// (!) WHY A PURE STATIC AND NOT A CHECK AT EACH SURFACE.
// -----------------------------------------------------------------------------
// Three surfaces need this exact answer - the contextual tutorial beat's emitter
// (TutorialSignalAdapters), the dock JOURNEY face badge (HudKitController) and the
// Journey deck's Raids card badge (PlayerDeckWorkspace) - and they live in three
// assemblies. A check written three times is the duplicated-state class CLAUDE.md
// sections 2/5/16 each describe in their own words, and PlayerDeckWorkspace.cs
// already records the specific instance: the Raids card carried
// `Available = () => true` while the action bar honoured PostureSignals.RaidCapable,
// "and the drift is the actual defect". So the rule lives here once.
//
// -----------------------------------------------------------------------------
// ⛔ THE HARD PART IS THAT EVERY RAIL THIS READS DEFAULTS *OPEN*. FAIL CLOSED.
// -----------------------------------------------------------------------------
// Read at source 2026-09-16:
//   * PostureSignals.RaidCapable  = TRUE   before any publish (PostureSignals.cs:237)
//   * PostureSignals.HeartfireLit = 3      before any publish (:311, HeartfireMaxDefault)
//   * RaidEntryGate.ArmyStatus    = { Ready = true, Version = 0 } (RaidEntryGate.cs:78-79)
// Each default is DELIBERATE and correct for its own consumer: none of them may
// "imply a gate nobody has evaluated", so a pre-publish HUD must not paint a lock.
// But an ALWAYS-ON BADGE is the opposite polarity. A badge that inherits those
// defaults would light up in every headless frame, every pre-publish frame, on the
// title screen and on a save with no barracks and no troops - and a badge that
// promises a raid the player cannot start is worse than no badge, because the next
// thing they learn is that the HUD lies.
//
// So this predicate requires a PUBLISHER TO HAVE SPOKEN before it will say yes:
//   * army:      ArmyStatus.Version > 0 (the Village publisher has run at least once)
//                AND ArmyStatus.Ready.
//                ⚠ AN EARLIER DRAFT OF THIS FILE JUDGED THE ARMY BY `DeployableSlots > 0`
//                AND THAT WAS WRONG IN THE WORST AVAILABLE DIRECTION. The door's own bar is
//                RaidSelectionScreen.Open:342-402, which recomputes ArmyReadiness.Compute and,
//                when !Ready, TOASTS AND REDIRECTS TO THE DRILLMASTER rather than opening the
//                grid - and the bar is RequiredSlots, which the WO-823 soft gate sets to 3 on a
//                save that has never raided. One deployable troop satisfies "> 0" and gets
//                BOUNCED. A badge that walks a new player into a refusal does not merely fail to
//                help: it teaches that the HUD lies, and then the real prompt is ignored too.
//                DeployableSlots is kept, but only to WORD the reason - it never decides.
//                (Both call sites read at source 2026-09-16.)
//   * heartfire: PostureSignals.HeartfirePublished AND HeartfireLit > 0.
//   * barracks:  RaidCapable. This one is read as-is and the reason is specific:
//                SetRaidCapable early-returns when the value is unchanged, so a
//                genuinely-capable save may never publish it at all (the live value
//                equals the default), and demanding a witness here would hide the
//                badge from exactly the players it is for. The army witness above
//                already refuses every pre-publish frame, and a save with no
//                barracks gets an explicit SetRaidCapable(false, NoBarracks) from
//                RaidCapabilityHudBridge - so the pair is closed without it.
//
// -----------------------------------------------------------------------------
// "HAS NEVER RAIDED" IS THE FUNNEL'S OWN LATCH, NOT EverCompletedRaid.
// -----------------------------------------------------------------------------
// GameState.EverCompletedRaid means FINISHED. The thing measured - and the thing
// missing from the live data - is ATTEMPTED. A player who launched a raid and died
// has found the door; the badge must stop nagging them. So this reads
// RaidFunnel.FirstRaidAttempted, which is the very latch behind
// raid_funnel_first_raid_attempted, so the badge and the metric can never disagree
// about whether this install has been through the door.
//
// ASCII only. FlowTrace tag "RaidDoor". Never stripped (CLAUDE.md section 12).
// =============================================================================

using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.HudModel
{
    /// <summary>
    /// "Is the raid door open to a player who has never used it?" One rule, read by the
    /// tutorial beat's emitter and by both badge surfaces.
    /// </summary>
    public static class RaidDoorReadiness
    {
        /// <summary>FlowTrace system tag.</summary>
        public const string Sys = "RaidDoor";

        /// <summary>
        /// The dock JOURNEY face badge glyph. A SHAPE, not a hue: the owner is red/green
        /// colourblind (memory owner-colorblind-delegate-visual-creative), so an attention
        /// marker may never be carried by colour. One ASCII character because
        /// ElarionUiKit.StyleAsStackBadge's plate is 52x40 reference px - authored for up to
        /// three DIGITS - and a word would ellipsise to nothing there. The words live on the
        /// deck card, which has a whole text band for them (<see cref="CardBadgeWord"/>).
        /// </summary>
        public const string DockBadgeGlyph = "!";

        /// <summary>
        /// The Journey deck Raids card badge. Bracketed literal in the shape
        /// PlayerDeckWorkspace already draws for "[ LOCKED ]" - same plate, same band, same
        /// reasoning (a word reads identically under any hue loss). Deliberately NOT a
        /// HudStrings key: the sibling "[ LOCKED ]" badge is a bare literal in the same
        /// method, and minting one key for one of a matched pair is how the two drift.
        /// </summary>
        public const string CardBadgeWord = "[ RAID READY ]";

        /// <summary>
        /// WHAT IS BETWEEN THIS PLAYER AND A RAID, as one ordered answer. Owner direction
        /// 2026-09-16: *"we should have some kind of helper that says try building a barracks or
        /// click here to put your barracks - something that we should assist them."* The helper
        /// chain shows ONE step at a time, ordered by what is missing, and this enum IS that
        /// order - so the chain cannot disagree with the badge about which blocker is current.
        ///
        /// <para>⛔ <see cref="Unpublished"/> IS NOT A BLOCKER, IT IS "WE DO NOT KNOW YET", and
        /// it must produce SILENCE - no helper, no badge. Every rail this reads defaults open
        /// (see the header), so a state we cannot see is the one state where guessing is
        /// guaranteed to be wrong in the player's face.</para>
        /// </summary>
        public enum RaidBlocker
        {
            /// <summary>A rail has not published. Say nothing at all.</summary>
            Unpublished = 0,
            /// <summary>This install has raided. The chain is finished forever.</summary>
            Attempted,
            /// <summary>No Barracks. Helper 1: build one (the CTA places it).</summary>
            NoBarracks,
            /// <summary>Barracks, but the army is under the door's own bar. Helper 2: train.</summary>
            ArmyShort,
            /// <summary>Everything but a charge. No helper: it is a clock, not an action.</summary>
            NoHeartfire,
            /// <summary>Nothing is missing. The raid-door prompt itself.</summary>
            Ready,
        }

        // =====================================================================
        //  PURE - the whole rule, with no rails, no scene and no PlayerPrefs
        // =====================================================================

        /// <summary>
        /// The ordered diagnosis, from primitives. Static + pure so a regression can assert the
        /// entire truth table - including every fail-closed row - with nothing loaded (the
        /// RaidFunnel.IsWithinSecondRaidWindow shape).
        /// </summary>
        /// <param name="raidCapable">PostureSignals.RaidCapable (the barracks gate).</param>
        /// <param name="heartfirePublished">PostureSignals.HeartfirePublished - HAS the
        /// Village service published at all. Without this the 3-charge default reads as a
        /// full Heart on the title screen.</param>
        /// <param name="heartfireLit">PostureSignals.HeartfireLit.</param>
        /// <param name="armyStatusVersion">RaidEntryGate.ArmyStatus.Version - 0 means the
        /// Village publisher has never run, so every other army field is a default.</param>
        /// <param name="armyReady">
        /// RaidEntryGate.ArmyStatus.Ready - ⛔ THE DOOR'S OWN BAR, and the reason this parameter
        /// exists instead of a slot count. RaidSelectionScreen.Open:342-402 recomputes
        /// ArmyReadiness.Compute and, when !Ready, TOASTS AND REDIRECTS TO THE DRILLMASTER
        /// instead of opening the grid. The bar is RequiredSlots, which under the WO-823 soft
        /// gate is 3 on a save that has never raided - NOT "more than zero". A predicate that
        /// said yes on one deployable troop would point the player at a door that bounces them,
        /// which is the single worst thing this feature could do: it would teach that the badge
        /// lies. (Read at source 2026-09-16.)
        /// </param>
        /// <param name="deployableSlots">RaidEntryGate.ArmyStatus.DeployableSlots. DIAGNOSTIC
        /// ONLY - it words the reason, it never decides. See <paramref name="armyReady"/>.</param>
        /// <param name="firstRaidAttempted">RaidFunnel.FirstRaidAttempted.</param>
        /// <param name="why">Always set: the reason, whichever way the answer went. A
        /// predicate that can only explain its "no" leaves a wrong "yes" unexplainable.</param>
        public static RaidBlocker Diagnose(bool raidCapable, bool heartfirePublished, int heartfireLit,
                                           int armyStatusVersion, bool armyReady, int deployableSlots,
                                           bool firstRaidAttempted, out string why)
        {
            // ORDER MATTERS AND THIS IS THE ORDER. "Already raided" outranks everything: a
            // veteran whose barracks was destroyed must not be handed the FTUE helper chain.
            if (firstRaidAttempted)
            {
                why = "this install has already attempted a raid (RaidFunnel step 3 is latched) - " +
                      "the door has been found, so nothing needs to point at it.";
                return RaidBlocker.Attempted;
            }

            // ── THE TWO WITNESSES. Checked before any blocker is NAMED, because an
            //    unpublished rail cannot tell a blocker from its own default.
            if (armyStatusVersion <= 0)
            {
                why = "RaidEntryGate.ArmyStatus.Version is 0 - the Village publisher has never run, " +
                      "so Ready and DeployableSlots are struct defaults (Ready DEFAULTS TRUE) and not " +
                      "a roster. Also true headless: ArmyReadiness.Compute(null) returns READY by " +
                      "design so it can never false-block. SAYING NOTHING.";
                return RaidBlocker.Unpublished;
            }
            if (!heartfirePublished)
            {
                why = "Heartfire has NEVER been published (PostureSignals.HeartfirePublished false) - " +
                      "HeartfireLit is still the pre-publish ceiling default, not a measurement. " +
                      "SAYING NOTHING rather than promising a charge nobody has counted.";
                return RaidBlocker.Unpublished;
            }

            // ── Now the real blockers, in the order the player must solve them.
            if (!raidCapable)
            {
                why = "PostureSignals.RaidCapable is false - there is no Barracks (or it was lost). " +
                      "This is the FIRST thing to assist with: the owner's direction is a helper that " +
                      "says build one and puts it in the player's hand.";
                return RaidBlocker.NoBarracks;
            }
            if (!armyReady)
            {
                why = "the army is under the DOOR'S OWN BAR (ArmyStatus.Ready false; " +
                      deployableSlots + " deployable slot(s), required " +
                      "RaidEntryGate.ArmyStatus.RequiredSlots). RaidSelectionScreen.Open would toast " +
                      "and redirect to the drillmaster, so the helper trains instead of pointing.";
                return RaidBlocker.ArmyShort;
            }
            if (heartfireLit <= 0)
            {
                why = "Heartfire is 0 - the one gate on WHEN a raid may start (WO-1379) is shut. NO " +
                      "helper for this one: it is a clock, not an action, and a coach mark that asks " +
                      "the player to wait is noise.";
                return RaidBlocker.NoHeartfire;
            }

            why = "raid door OPEN and unused: barracks present, army over the door's own bar (" +
                  deployableSlots + " deployable slot(s)), Heartfire " + heartfireLit +
                  " lit, and this install has never attempted a raid.";
            return RaidBlocker.Ready;
        }

        /// <summary>
        /// The badge/prompt predicate: nothing at all is missing. Kept as its own method because
        /// the two badge surfaces want a bool and must not each re-derive "Ready" from the enum.
        /// </summary>
        public static bool Evaluate(bool raidCapable, bool heartfirePublished, int heartfireLit,
                                    int armyStatusVersion, bool armyReady, int deployableSlots,
                                    bool firstRaidAttempted, out string why) =>
            Diagnose(raidCapable, heartfirePublished, heartfireLit, armyStatusVersion, armyReady,
                     deployableSlots, firstRaidAttempted, out why) == RaidBlocker.Ready;

        // =====================================================================
        //  LIVE - the same rule over the published rails
        // =====================================================================

        /// <summary>
        /// The live diagnosis, read off the rails. Never throws: any rail read that faults
        /// answers <see cref="RaidBlocker.Unpublished"/> - i.e. SILENCE - because the failure
        /// mode of this predicate must be a missing badge, never a lying one.
        /// </summary>
        public static RaidBlocker CurrentBlocker(out string why)
        {
            var got = RaidBlocker.Unpublished;
            string reason = null;
            bool guarded = Guard.Try(Sys, "diagnose raid-door readiness", () =>
            {
                var army = UI.RaidEntryGate.ArmyStatus;
                got = Diagnose(
                    PostureSignals.RaidCapable,
                    PostureSignals.HeartfirePublished,
                    PostureSignals.HeartfireLit,
                    army.Version,
                    army.Ready,
                    army.DeployableSlots,
                    Analytics.RaidFunnel.FirstRaidAttempted,
                    out reason);
            });
            if (!guarded)
            {
                why = "a rail read threw while diagnosing the raid door - answering Unpublished, " +
                      "i.e. saying nothing (a missing helper is recoverable; a helper pointing at " +
                      "the wrong blocker is not).";
                return RaidBlocker.Unpublished;
            }
            why = reason;
            return got;
        }

        /// <summary>
        /// The live answer for the two badge surfaces and the raid-door prompt.
        /// </summary>
        public static bool Current(out string why) => CurrentBlocker(out why) == RaidBlocker.Ready;

        /// <summary>Convenience for a caller that only wants the bool.</summary>
        public static bool Current() => Current(out _);

        /// <summary>
        /// A stable key for change-detection on a polling surface, so a badge repaints on a
        /// TRANSITION and never every frame. Folds in the numbers the answer depends on, not
        /// just the answer, so a surface can tell "still no, for a new reason" from "no".
        /// </summary>
        public static int StateKey()
        {
            int key = 17;
            bool ok = Guard.Try(Sys, "raid-door state key", () =>
            {
                var army = UI.RaidEntryGate.ArmyStatus;
                unchecked
                {
                    key = key * 31 + (PostureSignals.RaidCapable ? 1 : 0);
                    key = key * 31 + (PostureSignals.HeartfirePublished ? 1 : 0);
                    key = key * 31 + PostureSignals.HeartfireLit;
                    key = key * 31 + (army.Version > 0 ? 1 : 0);
                    key = key * 31 + (army.Ready ? 1 : 0);
                    key = key * 31 + army.DeployableSlots;
                    key = key * 31 + (Analytics.RaidFunnel.FirstRaidAttempted ? 1 : 0);
                }
            });
            // A throwing read must not look like a stable state: hand back a value that
            // differs from any real one so the next successful poll repaints.
            return ok ? key : int.MinValue;
        }
    }
}
