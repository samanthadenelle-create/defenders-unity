// =============================================================================
// HeartboundEventCopy - the WORDS of the Echo Event reveal, and nothing else.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Heartbound
// WO-1678 (HEART-005) D4, the reveal surface's copy half.
//
// ⛔ FOUR WORDS ARE BANNED OUTRIGHT BY THE SPEC AT :483-491, AND THEY ARE
// DELIBERATELY NOT SPELLED IN THIS FILE. The sweep that enforces them reads this
// file RAW - it must, because SourceLint blanks string CONTENTS and the risk lives
// entirely in the player-facing strings - so a header that quoted the four words
// would fail the very pin it was describing. They are listed once, in
// HeartboundEventRegression's BannedWords, which is the file that owns the rule.
// The sense of it, in words that are safe to write here: an Echo Event is never a
// draw, a stake or a game of chance. No SKR is consumed by one, and no string here
// may suggest the player put something at risk on an outcome. The player-facing
// term is "Echo Event"; the fiction is that the world REACTED.
//
// ⛔ AND THE PLACE IS "THE HEART". Owner ruling 2026-09-10 13:20 (Q-NAME): "the
// Heart everywhere - 'Tree of Life' stays a dev synonym only; no player-facing
// string carries it." The spec says Tree of Life throughout; the strings do not.
//
// ⛔ NO REWARD VALUE IS WRITTEN HERE. Spec :947 - "No reward values hardcoded in
// presentation classes." Every number the player reads is formatted FROM the
// event descriptor, which came from the authored table. A "+15%" literal in this
// file would be a second authority on a number the config owns; the regression
// fails on a percent literal.
//
// ASCII only - every string below reaches a mobile font atlas, and non-ASCII
// renders as tofu there.
//
// #if DAPP_STORE: Seeker-only by owner ruling 2026-09-10 13:10.
// =============================================================================

#if DAPP_STORE

using System.Globalization;

namespace DeNelle.Core.Heartbound
{
    /// <summary>Player-facing words for the Echo Event reveal. Words only.</summary>
    public static class HeartboundEventCopy
    {
        /// <summary>The feature's one player-facing name.</summary>
        public const string EventTerm = "Echo Event";

        /// <summary>The reveal card's heading.</summary>
        public const string RevealTitle = "The world reacted";

        /// <summary>Shown when a pulse brought no event. A normal outcome, said plainly.</summary>
        public const string NoEventBody = "The Heart is quiet this turning. Nothing stirred.";

        /// <summary>The card's one button.</summary>
        public const string AcknowledgeButton = "Continue";

        /// <summary>
        /// The one-line body for an event. Numbers are formatted from <paramref name="evt"/>,
        /// never authored here.
        /// </summary>
        public static string BodyFor(HeartboundEchoEvent evt)
        {
            if (!evt.HasEvent) return NoEventBody;

            switch (evt.Kind)
            {
                case HeartboundRewardKind.Modifier:
                    return ModifierBody(evt);
                case HeartboundRewardKind.Scout:
                    return "Scouts return early from the roads. What they already knew, you know sooner - " +
                           "for " + Hours(evt.DurationSeconds) + ".";
                case HeartboundRewardKind.Heartfire:
                    return evt.Charges == 1
                        ? "A spark leaps from the Heart. One Heartfire is lit."
                        : "Sparks leap from the Heart. " + evt.Charges + " Heartfire are lit.";
                case HeartboundRewardKind.Item:
                    return "Something was found near the Heart.";
                default:
                    return NoEventBody;
            }
        }

        private static string ModifierBody(HeartboundEchoEvent evt)
        {
            string amount = Percent(evt.Magnitude);
            string window = Hours(evt.DurationSeconds);
            switch (evt.Modifier)
            {
                case HeartboundModifiers.LaneGatherRate:
                    return "The roots run deep. Gathering yields " + amount + " more for " + window + ".";
                case HeartboundModifiers.LaneBuildSpeed:
                    return "Unseen hands lend their strength. Construction runs " + amount +
                           " faster for " + window + ".";
                case HeartboundModifiers.LaneCraftSpeed:
                    return "The forge burns clearer. Crafting runs " + amount + " faster for " + window + ".";
                default:
                    return "The settlement feels lighter for " + window + ".";
            }
        }

        /// <summary>A fraction to a percent string. The VALUE comes from the event, always.</summary>
        public static string Percent(double fraction)
        {
            double pct = fraction * 100d;
            return pct.ToString(pct < 10d ? "0.#" : "0", CultureInfo.InvariantCulture) + " percent";
        }

        /// <summary>Seconds to a plain-words window. No clock is read.</summary>
        public static string Hours(double seconds)
        {
            if (seconds <= 0d) return "a moment";
            double hours = seconds / 3600d;
            if (hours < 1d)
            {
                int minutes = (int)(seconds / 60d);
                if (minutes <= 1) return "a minute";
                return minutes + " minutes";
            }
            if (hours < 2d) return "an hour";
            if (hours < 24d) return ((int)hours) + " hours";
            int days = (int)(hours / 24d);
            return days <= 1 ? "a day" : days + " days";
        }
    }
}

#endif
