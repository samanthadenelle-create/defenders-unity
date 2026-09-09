// =============================================================================
// RaidStrings — the ONE home for every word the raid COOLDOWN says (WO-728).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// WHY THIS FILE EXISTS
// A camp the player cannot raid must SAY SO. Before this, a cooldown did not
// exist at all; the failure mode we are designing away from is the one where it
// does exist and the card simply refuses to respond — a dead tap reads to the
// player as a frozen game (the exact defect WO-1110 §2 recorded on this very
// screen).
//
// ⛔ AND IT MUST SAY SO IN WORDS, NOT IN COLOUR.
//   The owner is red/green colourblind (memory: owner-colorblind-delegate-visual-
//   creative). A greyed card, a red badge, or a green "ready" tint carries ZERO
//   information for her. Every state below therefore has a SENTENCE, and the
//   sentence is the primary signal — any tint is decoration on top of it. Do not
//   "simplify" the card by dropping the label and keeping the colour.
//
// This compatibility catalog names keys only. English lives in en.json/GameStrings
// and every lookup forwards to LocalText, the one runtime authority.
// =============================================================================

using System;

namespace DeNelle.Core.UI
{
    /// <summary>Canon-backed copy for the per-camp raid cooldown. Keys only — no sentences.</summary>
    public static class RaidStrings
    {
        /// <summary>Card badge while a camp is on cooldown (a WORD, not a colour).</summary>
        public const string KeyCooldownBadge = "raidCooldownBadge";

        /// <summary>Card badge when a camp can be raided right now.</summary>
        public const string KeyReadyBadge = "raidReadyBadge";

        /// <summary>Card line while on cooldown; {0} = the humanised remaining time.</summary>
        public const string KeyCooldownCardLine = "raidCooldownCardLine";

        /// <summary>Card line when raidable.</summary>
        public const string KeyReadyCardLine = "raidReadyCardLine";

        /// <summary>Toast on tapping a camp that is still recovering; {0} = remaining time.</summary>
        public const string KeyCooldownBlocked = "raidCooldownBlocked";

        /// <summary>Shown on the victory screen after a clear; {0} = the full cooldown length.</summary>
        public const string KeyCooldownStarted = "raidCooldownStarted";

        /// <summary>Duration part: hours + minutes. {0} = hours, {1} = minutes.</summary>
        public const string KeyDurationHm = "raidDurationHm";
        /// <summary>Duration part: minutes only. {0} = minutes.</summary>
        public const string KeyDurationM = "raidDurationM";
        /// <summary>Duration part: under a minute (no number — "less than a minute").</summary>
        public const string KeyDurationSub = "raidDurationSub";

        /// <summary>Every key this class names, so the oracle can prove each one resolves.</summary>
        public static readonly string[] AllKeys =
        {
            KeyCooldownBadge, KeyReadyBadge, KeyCooldownCardLine, KeyReadyCardLine,
            KeyCooldownBlocked, KeyCooldownStarted,
            KeyDurationHm, KeyDurationM, KeyDurationSub,
        };

        /// <summary>Resolves through the one global localization authority.</summary>
        public static string Get(string key)
        {
            return LocalText.Get(key);
        }

        /// <summary>Resolves a canon key and formats it. A bad format string degrades to the raw sentence.</summary>
        public static string Format(string key, params object[] args)
        {
            return LocalText.Format(key, args);
        }

        /// <summary>
        /// Humanises a remaining-seconds count into the player's words ("2h 15m" / "45m" /
        /// "less than a minute"), every fragment from canon. Rounds UP to the next whole
        /// minute deliberately: a card that says "1m" and is still refusing taps reads as
        /// broken, so the number the player sees is never optimistic.
        /// </summary>
        public static string Humanise(double remainingSeconds)
        {
            // Under a minute says so in words rather than rounding up to a bare "1m": at that
            // range the exact number is noise, and "less than a minute" is the honest reading.
            if (double.IsNaN(remainingSeconds) || remainingSeconds < 60d) return Get(KeyDurationSub);
            long totalMinutes = (long)Math.Ceiling(remainingSeconds / 60d);
            if (totalMinutes <= 0L) return Get(KeyDurationSub);
            long hours = totalMinutes / 60L;
            long minutes = totalMinutes % 60L;
            if (hours > 0L) return Format(KeyDurationHm, hours, minutes);
            return Format(KeyDurationM, minutes);
        }

        /// <summary>Test/diagnostic hook — drops the cached map so a re-read picks up an edit.</summary>
        [Obsolete("LocalText owns locale table lifetime; Reload is no longer required.")]
        public static void Reload() { }
    }
}
