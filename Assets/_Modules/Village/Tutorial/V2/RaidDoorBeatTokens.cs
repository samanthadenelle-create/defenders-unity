// =============================================================================
// RaidDoorBeatTokens - the LIVE numbers inside the raid helper chain's copy (WO-1802).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Owner direction 2026-09-16, verbatim: *"we need to create a desire to raid so we
// need to announce early what the rewards are and we should start them with a starter
// army so they can at least do one for free"*.
//
// So the chain's copy must say what a raid PAYS and that the army is already FREE. Two
// facts, both of which are numbers, and numbers are the thing this repo keeps shipping
// wrong. The sentence is authored in dialogues.json; every number is a "{token}"
// resolved here at surface time (DeNelle.Core.Dialogue.DialogueTextTokens, read by
// DialogueViewModel.OnLine) - the exact PostRaidBeatTokens shape, for the exact reason
// recorded in that file's header: WO-1389's own draft copy carried "stone walls, 12
// defenders" for a camp whose data reads Iron / 15.
//
// -----------------------------------------------------------------------------
// ⛔ THE TWO NUMBERS THIS FILE EXISTS TO NOT HARDCODE.
// -----------------------------------------------------------------------------
//  1. THE STARTER ARMY SIZE IS MOVING WHILE THIS TICKET IS OPEN. It was 3 (WO-1374,
//     "the first army is free"); the owner ruled on 2026-09-16 that it becomes TEN -
//     five footmen and five archers - and a SIBLING LANE (WO-1803) owns the grant and
//     the raid.starterArmySize default. Any literal in this lane's copy would have been
//     wrong within the day. {raid.army.deployable} reads the published projection, so
//     the sentence follows the knob with no edit here. RaidDoorPromptRegression's
//     [no-hardcoded-numbers] case reds the build if a digit ever appears in the copy.
//  2. THE SPOILS ARE A PROJECTION, NOT A PRICE LIST. {raid.first.spoils} is composed by
//     RaidSelectionVM.FormatSpoils(EstimateSpoils(def)) - the SETTLE PAYOUT'S OWN
//     formula (RaidScoring.EstimateSpoils -> ProjectLoot -> ComputeLoot), which is what
//     the raid card already prints ("Spoils: ~1800 wood, ~1100 iron, ~2200 gold",
//     WO-1402). So the promise the prompt makes is the quote the grid shows and the
//     payout the settle pays. A second arithmetic here would eventually advertise a
//     rate the game does not honour, which is worse than saying nothing.
//
// AND THE CAMP IS NOT NAMED EITHER. {raid.first.camp} resolves through
// RaidSelectionVM.FirstOpenCamp(victories) - the ONE ladder authority, whose
// FlagshipRaidIds is private precisely so nobody keeps a second copy. It returns "The
// Forsaken Camp" (raider_camp_small, unlockVictories 0) on a fresh save TODAY; if the
// ladder is re-ordered the copy moves with it.
//
// FALLBACKS ARE NAMED SENTENCES, NEVER BLANKS. A resolver that cannot answer returns a
// sentence that still reads as English and says so in the trace - because the
// alternative on a device is a line with a hole in it, and DialogueTextTokens leaves a
// THROWING resolver's token visible on screen ("{raid.first.spoils}"), which is at
// least greppable. A fail-closed reward clause degrades to "resources Elarion needs"
// rather than to "~ gold".
//
// Boots itself once per process, like PostRaidBeatTokens / TutorialSignalAdapters /
// DialogueCommandSink. Pure reads; no state of its own. ASCII only.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Dialogue;
using DeNelle.Core.State;
using DeNelle.Village.Hero;

namespace DeNelle.Village
{
    /// <summary>Registers the WO-1802 raid-chain dialogue text tokens.</summary>
    public static class RaidDoorBeatTokens
    {
        // Token keys (no braces). The dialogue authors "{raid.first.camp}" etc.; the
        // regression scans all three raid-chain records for every "{...}" and asserts each
        // key is registered here.

        /// <summary>The camp the player can raid FIRST, by display name.</summary>
        public const string FirstCamp = "raid.first.camp";

        /// <summary>The spoils estimate WITHOUT the card's "Spoils: " prefix - so it can sit
        /// inside a sentence ("pays ~1800 wood, ~1100 iron, ~2200 gold").</summary>
        public const string FirstSpoils = "raid.first.spoils";

        /// <summary>The gold clause alone, for the short badge/headline form.</summary>
        public const string FirstGold = "raid.first.gold";

        /// <summary>How many troop slots are deployable RIGHT NOW (the free starter squad on a
        /// fresh save). Reads the published projection so the WO-1803 knob change needs no edit
        /// here.</summary>
        public const string ArmyDeployable = "raid.army.deployable";

        /// <summary>Fallback reward clause when the projection cannot be read. Still English,
        /// still true, and deliberately number-free.</summary>
        public const string SpoilsFallback = "resources Elarion needs";

        /// <summary>Fallback camp name. Never a blank, never an id.</summary>
        public const string CampFallback = "the nearest hostile camp";

        private static bool _registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot() => Register();

        /// <summary>Idempotent. Public so a headless oracle can register without a scene load.</summary>
        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            DialogueTextTokens.Register(FirstCamp, CampNameToken);
            DialogueTextTokens.Register(FirstSpoils, SpoilsToken);
            DialogueTextTokens.Register(FirstGold, GoldToken);
            DialogueTextTokens.Register(ArmyDeployable, DeployableToken);
            FlowTrace.Step("RaidDoor", "RaidDoorBeatTokens registered 4 dialogue text tokens (" +
                FirstCamp + ", " + FirstSpoils + ", " + FirstGold + ", " + ArmyDeployable +
                ") - every number in the raid helper chain's copy is resolved, never authored.");
        }

        /// <summary>Every key this file owns, for the regression's "all registered" case.</summary>
        public static readonly string[] Keys = { FirstCamp, FirstSpoils, FirstGold, ArmyDeployable };

        // -- The camp: the ONE ladder authority --------------------------------

        /// <summary>
        /// The first OPEN flagship camp at the player's victory count. Via
        /// RaidSelectionVM.FirstOpenCamp so there is no second copy of FlagshipRaidIds and no
        /// hardcoded "raider_camp_small" anywhere in this lane.
        /// </summary>
        private static SceneConfigDef FirstOpen()
        {
            var st = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            int victories = st != null ? st.RaidVictories : 0;
            return RaidSelectionVM.FirstOpenCamp(victories);
        }

        private static string CampNameToken()
        {
            var d = FirstOpen();
            if (d == null)
            {
                FlowTrace.Warn("RaidDoor", "token " + FirstCamp + ": no flagship camp is open - " +
                    "answering the generic '" + CampFallback + "'. A named camp would be invented.");
                return CampFallback;
            }
            string name = !string.IsNullOrEmpty(d.displayName) ? d.displayName : d.id;
            if (string.IsNullOrEmpty(name)) return CampFallback;
            FlowTrace.Step("RaidDoor", "token " + FirstCamp + " -> \"" + name + "\" (id '" + d.id + "').");
            return name;
        }

        // -- The reward: the SETTLE PAYOUT'S OWN formula -----------------------

        /// <summary>
        /// The spoils estimate as a sentence fragment. Composed by the raid card's own pair -
        /// RaidSelectionVM.EstimateSpoils then FormatSpoils - with the card's "Spoils: " prefix
        /// STRIPPED so the clause reads inside a sentence.
        ///
        /// <para>⛔ THE PREFIX IS REMOVED BY NAME (RaidSelectionVM.SpoilsPrefix), never by a
        /// character count. A literal Substring(8) would silently start clipping the first digit
        /// the day that prefix is reworded, and the copy would still look plausible.</para>
        /// </summary>
        private static string SpoilsToken()
        {
            var d = FirstOpen();
            if (d == null) return SpoilsFallback;

            string line = null;
            bool ok = Guard.Try("RaidDoor", "estimate first-camp spoils", () =>
            {
                var est = RaidSelectionVM.EstimateSpoils(d);
                line = RaidSelectionVM.FormatSpoils(est);
            });
            if (!ok || string.IsNullOrEmpty(line))
            {
                FlowTrace.Warn("RaidDoor", "token " + FirstSpoils + ": the spoils projection for '" +
                    d.id + "' was unavailable or all-zero - answering the number-free '" +
                    SpoilsFallback + "'. The prompt still promises a reward, just not a figure it " +
                    "cannot stand behind.");
                return SpoilsFallback;
            }

            string prefix = RaidSelectionVM.SpoilsPrefix;
            if (!string.IsNullOrEmpty(prefix) && line.StartsWith(prefix, System.StringComparison.Ordinal))
                line = line.Substring(prefix.Length);
            FlowTrace.Step("RaidDoor", "token " + FirstSpoils + " -> \"" + line +
                "\" (from RaidScoring.EstimateSpoils, the settle payout's own formula - the same " +
                "figure the raid card prints).");
            return line;
        }

        /// <summary>
        /// The gold clause alone ("~2200 gold") for the short headline form. Derived from the
        /// SAME estimate as the full clause, so the two can never quote different golds.
        /// </summary>
        private static string GoldToken()
        {
            var d = FirstOpen();
            if (d == null) return SpoilsFallback;

            string gold = null;
            bool ok = Guard.Try("RaidDoor", "estimate first-camp gold", () =>
            {
                var est = RaidSelectionVM.EstimateSpoils(d);
                string full = RaidSelectionVM.FormatSpoils(est);
                if (string.IsNullOrEmpty(full)) return;
                // Pull the gold clause out of the ONE formatted line rather than re-formatting
                // the number here: FormatSpoils owns the "~" range-feel grammar and the rounding
                // (owner ruling WO-1402, "a range or estimate, never exact"), and a second
                // formatter would drift from it in exactly the visible digits.
                foreach (string part in full.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.EndsWith(" gold", System.StringComparison.Ordinal))
                    { gold = trimmed; return; }
                }
            });
            if (!ok || string.IsNullOrEmpty(gold))
            {
                FlowTrace.Warn("RaidDoor", "token " + FirstGold + ": no gold clause in the projection " +
                    "for '" + d.id + "' - answering '" + SpoilsFallback + "'.");
                return SpoilsFallback;
            }
            FlowTrace.Step("RaidDoor", "token " + FirstGold + " -> \"" + gold + "\".");
            return gold;
        }

        // -- The free army: the published projection, never the knob -----------

        /// <summary>
        /// Deployable troop slots right now, off RaidEntryGate.ArmyStatus - the projection the
        /// raid door itself was judged against (published by BuildTimerService.PublishArmyStatus
        /// from ArmyReadiness.Compute).
        ///
        /// <para>⛔ NOT RemoteTunables.KeyRaidStarterArmySize. The knob is what the grant INTENDED
        /// to give; this is what the player HAS. They differ whenever a troop is wounded, lost, or
        /// the grant ran short (StarterArmyGrant itself logs a Fail and breaks out of its loop on a
        /// short grant). A prompt that says "10 troops are ready" over a roster of 7 is the small
        /// lie StarterArmyGrant.GrantToastFor was already written to avoid - its own comment says
        /// it builds the count "from the ACTUAL number granted rather than a hardcoded 3".</para>
        ///
        /// <para>"0" when the publisher has never run. The chain's copy only asks for this token on
        /// the rung where the army is known good, so a zero here is a defect worth seeing rather
        /// than one worth hiding.</para>
        /// </summary>
        private static string DeployableToken()
        {
            int slots = 0;
            bool published = false;
            Guard.Try("RaidDoor", "read deployable slots", () =>
            {
                var army = DeNelle.Core.UI.RaidEntryGate.ArmyStatus;
                published = army.Version > 0;
                slots = Mathf.Max(0, army.DeployableSlots);
            });
            if (!published)
                FlowTrace.Warn("RaidDoor", "token " + ArmyDeployable + ": ArmyStatus was never " +
                    "published (Version 0) - answering 0. The rung that quotes this token is only " +
                    "raised when the army is known READY, so a 0 here means the poll and the copy " +
                    "disagree and that is the thing to look at.");
            else
                FlowTrace.Step("RaidDoor", "token " + ArmyDeployable + " -> " + slots +
                    " deployable slot(s) (published projection, not the starterArmySize knob).");
            return slots.ToString();
        }
    }
}
