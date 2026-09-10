using DeNelle.Core.Diagnostics;
using DeNelle.Core.Quests;

namespace DeNelle.Core.HudModel
{
    /// <summary>Pure, single-line state copy for the two actionable Journey cards.</summary>
    public sealed class JourneyDeckSubtitleVM
    {
        public string QuestsSubtitle { get; }
        public string RaidsSubtitle { get; }

        public JourneyDeckSubtitleVM(int activeQuests, int readyToClaim,
                                     int armyUsed, int armyCap, int openCamps)
        {
            activeQuests = NonNegative(activeQuests);
            readyToClaim = NonNegative(readyToClaim);
            armyUsed = NonNegative(armyUsed);
            armyCap = NonNegative(armyCap);
            openCamps = NonNegative(openCamps);

            QuestsSubtitle = activeQuests + " active . " + readyToClaim + " ready to claim";

            // WO-1643 - THE SECOND CLAUSE IS ABOUT CAMPS. IT MUST NOT READ AS ARMY ADVICE.
            // This branch is `openCamps > 0` and takes NO army input at all, yet it lands
            // immediately after an army fraction. The retired remedy phrase was
            // "train to open a camp": structurally correct English about camps that a player
            // reads as "your army is 8 of 10, train more". On build 363529 this card painted
            // "Army 8 / 10 . train to open a camp" (logcat :14265, 05:59:28) while the raid
            // door was OPEN - ready=True, deployable=8, required=3 (:28080, 06:02:18).
            // ⚠ The two samples are ~3 min apart, NOT one atomic read; they are joined only
            // because both feeds are change-only and neither re-published in between. Two true
            // facts welded into one false sentence.
            //
            // The replacement clause states the camp fact and ONLY the camp fact, so the
            // sentence is true in every readiness state and this composer needs no readiness
            // input to stay honest. ⛔ Do not restore a training verb here: the remedy for
            // "no camp in reach" may be training OR unlocking the next camp, and this composer
            // cannot tell which. The army half of the line is the fraction, and WHICH numerator
            // that fraction should carry is a separate open ruling (WO-1643 sec.3) seeded at
            // BuildTimerService.PublishArmyStatus - never re-derived here.
            RaidsSubtitle = "Army " + armyUsed + " / " + armyCap + " . " +
                (openCamps > 0
                    ? openCamps + (openCamps == 1 ? " camp open" : " camps open")
                    : "no camp in reach");
        }

        public static JourneyDeckSubtitleVM FromCurrentState()
        {
            int active = 0;
            int ready = 0;
            int used = 0;
            int cap = 0;
            int camps = 0;

            Guard.Try("Journey", "read Journey deck subtitle state", () =>
            {
                var quests = QuestService.Instance;
                if (quests != null) active = quests.ActiveQuestIds().Count;

                // WO-1521 - READ THE ONE AUTHORITY, never a second copy of the predicate.
                // This used to inline `quest.Completed && quest.ClaimedAtUnix == 0` over
                // today's set. That copy is why this card could say "1 ready to claim" while
                // Brom's Rumor Board said "The board is quiet": two surfaces, two lists, no
                // shared fact. DailyQuestService.ClaimableCount is now the single count and
                // RumorBoardVM projects a row from the SAME predicate.
                var daily = DailyQuestService.Instance;
                if (daily != null) ready = daily.ClaimableCount;

                used = PostureSignals.ArmyFillUsed;
                cap = PostureSignals.ArmyFillCap;
                camps = PostureSignals.RaidOpenCampCount;
            });

            // WO-1643 STEP 1 - THE PAINT-TIME INPUTS, NAMED. INSTRUMENTATION IS PERMANENT
            // (CLAUDE.md sec.12 - never strip this).
            //
            // ⚠ THE PRODUCER TRACES WERE ALREADY THERE AND STILL PROVED NOTHING, which is the
            // finding this line exists for. PostureSignals.SetArmyFill (:405) and
            // SetRaidOpenCampCount (:423) both emit - but BOTH ARE CHANGE-ONLY, and the camp
            // count's default is 0. A genuinely-zero count therefore publishes nothing and
            // traces nothing, so "no camps are in reach" and "the producer never ran" are the
            // SAME silence on the wire. Measured, not assumed: the 11.4 MB device logcat
            // Builds/device-frames/2026-09-10_raid_logcat.txt contains ZERO occurrences of
            // "raid camps open ->" across the whole session, while :14265 carries the painted
            // sentence. That is why WO-1643 sec.2 had to record the 0603 camp count as unproven.
            //
            // This Step fires on the READ side, unconditionally, so the value the card actually
            // composed from is on the wire whether or not it ever changed. It sits AFTER the
            // Guard so a guard failure still logs the 0-sentinel inputs it fell back to. Cadence
            // is one call site (PlayerDeckWorkspace.cs:800, Journey deck build), the same
            // cadence as the existing TraceJourneySubtitle Step beside it - not a frame path, so
            // a plain Step is correct here and the 4-arg Measure form is not.
            string inputs = "journey subtitle inputs -> armyUsed=" + used + " armyCap=" + cap +
                            " openCamps=" + camps + " activeQuests=" + active +
                            " readyToClaim=" + ready;
            FlowTrace.Step("Journey", inputs);

            return new JourneyDeckSubtitleVM(active, ready, used, cap, camps);
        }

        private static int NonNegative(int value) => value < 0 ? 0 : value;
    }
}
