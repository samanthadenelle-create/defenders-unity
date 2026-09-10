#if UNITY_EDITOR
using System;
using System.IO;
using DeNelle.Core.HudModel;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1404: Journey subtitles carry actionable state and fit one line.</summary>
    public static class JourneyDeckSubtitleRegression
    {
        public static bool Run(out string result)
        {
            try
            {
                var fixture = new JourneyDeckSubtitleVM(2, 1, 3, 10, 1);

                // RED recipe: revert PlayerDeckWorkspace to the old literal / RaidsCardPurpose.
                string deck = File.ReadAllText("Assets/_Modules/HUD/PlayerDeckWorkspace.cs");
                RequireContains(deck, "TraceJourneySubtitle(\"Quests\", journey.QuestsSubtitle)", "Quests binding");
                RequireContains(deck, "TraceJourneySubtitle(\"Raids\", journey.RaidsSubtitle)", "Raids binding");
                Forbid(deck, "\"Read active quests and realm rumors\"");
                Forbid(deck, "\"Choose a camp and deploy your army\"");

                string timer = File.ReadAllText("Assets/_Modules/Village/Buildings/BuildTimerService.cs");
                // RED recipe: remove the IsLocked guard from the open-camp loop.
                RequireContains(timer, "if (_journeyRaidProjection.IsLocked(id)) continue;", "camp escalation lock");
                // RED recipe: remove the unchanged-input early return in PublishJourneyOpenCamps.
                RequireContains(timer,
                    "if (!projectionChanged && _journeyRaidDeployableInput == deployableBodies) return;",
                    "camp projection cache");
                // RED recipe: change <= back to < in the newly authored open-camp predicate.
                RequireContains(timer, "GarrisonCount(def) <= deployableBodies", "camp deployable predicate");

                // RED recipe: remove the Journey fixture's SetArmyFill(0, 10) publish.
                string capture = File.ReadAllText("Assets/Editor/UICaptureLaunch.cs");
                RequireContains(capture, "PostureSignals.SetArmyFill(0, 10)", "Journey capture army cap");

                // RED recipe: remove armyUsed/armyCap from the Raids composer.
                RequireContains(fixture.RaidsSubtitle, "3 / 10", "Raids army state");
                // RED recipe: replace the open-camp branch with the old training verb phrase.
                RequireContains(fixture.RaidsSubtitle, "1 camp", "Raids open-camp state");
                // RED recipe: restore "Read active quests and realm rumors".
                RequireContains(fixture.QuestsSubtitle, "active", "Quests active state");

                // RED recipe: append "..." in either composer.
                ForbidBoth(fixture, "...");
                // RED recipe: restore either old card verb.
                ForbidBoth(fixture, "Choose");
                ForbidBoth(fixture, "Read");

                // RED recipe: return blank when no camp passes the deployment predicate.
                var zero = new JourneyDeckSubtitleVM(0, 0, 0, 10, 0);
                RequireContains(zero.RaidsSubtitle, "Army 0 / 10", "zero-army state");
                // WO-1643: RE-POINTED, NOT DELETED. This assertion pinned "train to open a
                // camp" - the clause that made the card contradict an OPEN raid door. The
                // no-camp clause now states the camp fact alone. The fraction assertions above
                // (:39 "3 / 10", :53 "Army 0 / 10") and the plural pin (:41 "1 camp") are
                // deliberately UNCHANGED: which numerator the fraction should carry is a
                // separate open ruling and this lane did not touch the seed.
                RequireContains(zero.RaidsSubtitle, "no camp in reach", "zero-camp state");

                // WO-1643 RED recipe: restore the training verb in the no-camp branch.
                // Pins the OLD shape OUT so it cannot come back silently.
                ForbidBoth(zero, "train");

                // WO-1643 acceptance 3 - THE CONTRADICTION FIXTURE.
                // The device state that produced the ticket: RaidEntryGate had published
                // ready=True (deployable=8, queued=0, required=3) and the card still said
                // "train". This composer takes no readiness input by design, so the pin is that
                // NO no-camp sentence may carry army advice at all - which holds for every
                // readiness state rather than only the one that was captured.
                var readyNoCamp = new JourneyDeckSubtitleVM(0, 0, 8, 10, 0);
                RequireContains(readyNoCamp.RaidsSubtitle, "Army 8 / 10 . no camp in reach",
                    "ready-but-no-camp state");
                ForbidBoth(readyNoCamp, "train");

                result = "JOURNEY_DECK_SUBTITLE_OK fixture='" + fixture.QuestsSubtitle +
                         "' / '" + fixture.RaidsSubtitle + "'";
                return true;
            }
            catch (Exception ex)
            {
                result = "JOURNEY_DECK_SUBTITLE_FAIL " + ex.Message;
                return false;
            }
        }

        private static void RequireContains(string value, string token, string label)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf(token, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException(label + " missing '" + token + "' in '" + value + "'.");
        }

        private static void ForbidBoth(JourneyDeckSubtitleVM vm, string token)
        {
            if (vm.QuestsSubtitle.IndexOf(token, StringComparison.Ordinal) >= 0 ||
                vm.RaidsSubtitle.IndexOf(token, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Legacy Journey subtitle token returned: " + token);
        }

        private static void Forbid(string source, string token)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Legacy Journey source token returned: " + token);
        }
    }
}
#endif
