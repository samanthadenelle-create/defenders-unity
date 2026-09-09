// =============================================================================
// PromoStrings — the ONE home for every word the promo-code door says.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Promo
//
// WHY THIS FILE EXISTS
// The redeem screen is the one place in the game where a vague failure reads as a
// SCAM. "Invalid code." on a screen the player just typed a real code into leaves
// them unable to tell whether they mistyped, whether the code was already spent,
// whether it expired, or whether WE lost their reward. So every documented server
// error gets its OWN sentence, each sentence says whether the code was consumed,
// and none of them is reused for a second cause.
//
// Those sentences are player-facing copy, so this compatibility catalog names
// stable keys and forwards resolution to LocalText, the one runtime localization
// authority. Nothing here owns, parses, or caches an English sentence.
//
// A missing key returns the visible "[[missing:key]]" marker (the house
// convention) AND self-reports through FlowTrace — never a silent blank on a
// screen that is handing out rewards.
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;

namespace DeNelle.Core.Promo
{
    /// <summary>Localization-backed compatibility catalog for the promo-code redeem door.</summary>
    public static class PromoStrings
    {
        // ── Chrome ───────────────────────────────────────────────────────────
        /// <summary>Label of the store's entry button ("Redeem a Code").</summary>
        public const string KeyEntry       = "promo.redeem.entry";
        /// <summary>Panel title.</summary>
        public const string KeyTitle       = "promo.redeem.title";
        /// <summary>One-line explanation under the title.</summary>
        public const string KeyBlurb       = "promo.redeem.blurb";
        /// <summary>Input-field placeholder.</summary>
        public const string KeyPlaceholder = "promo.redeem.placeholder";
        /// <summary>Submit-button label.</summary>
        public const string KeyAction      = "promo.redeem.action";
        /// <summary>Static hint under the field (case-insensitivity).</summary>
        public const string KeyHint        = "promo.redeem.hint";
        /// <summary>Status line while the request is in flight.</summary>
        public const string KeyBusy        = "promo.redeem.busy";

        // ── Success ──────────────────────────────────────────────────────────
        /// <summary>Success line; {0} = the composed reward summary.</summary>
        public const string KeySuccess          = "promo.redeem.success";
        /// <summary>Success line when the code carried no reward at all.</summary>
        public const string KeySuccessNoReward  = "promo.redeem.successNoReward";
        /// <summary>Reward part; {0} = crystal amount.</summary>
        public const string KeyRewardCrystals   = "promo.redeem.rewardCrystals";
        /// <summary>Reward part; {0} = coin amount.</summary>
        public const string KeyRewardCoins      = "promo.redeem.rewardCoins";
        /// <summary>Reward part; {0} = the store pack name.</summary>
        public const string KeyRewardPack       = "promo.redeem.rewardPack";

        // ── Failures — ONE distinct sentence per documented cause ────────────
        /// <summary>The player submitted an empty field.</summary>
        public const string KeyErrEmpty       = "promo.redeem.error.empty";
        /// <summary>Server: INVALID_CODE.</summary>
        public const string KeyErrInvalid     = "promo.redeem.error.invalid";
        /// <summary>Server: ALREADY_REDEEMED (also the local dedup set).</summary>
        public const string KeyErrAlreadyUsed = "promo.redeem.error.alreadyUsed";
        /// <summary>Server: EXPIRED.</summary>
        public const string KeyErrExpired     = "promo.redeem.error.expired";
        /// <summary>Server: PLAYER_LIMIT_REACHED.</summary>
        public const string KeyErrPlayerLimit = "promo.redeem.error.playerLimit";
        /// <summary>No connection / unreachable endpoint — the code was NOT spent.</summary>
        public const string KeyErrOffline     = "promo.redeem.error.offline";
        /// <summary>Identity proof refused (401/400 from the wallet-auth rail).</summary>
        public const string KeyErrIdentity    = "promo.redeem.error.identity";
        /// <summary>No player identity at all — nothing to key the redemption to.</summary>
        public const string KeyErrSignIn      = "promo.redeem.error.signIn";
        /// <summary>Anything else (unparseable body, unnamed error code).</summary>
        public const string KeyErrUnknown     = "promo.redeem.error.unknown";

        /// <summary>Every failure key, in one place, so the oracle can prove they are distinct.</summary>
        public static readonly string[] FailureKeys =
        {
            KeyErrEmpty, KeyErrInvalid, KeyErrAlreadyUsed, KeyErrExpired,
            KeyErrPlayerLimit, KeyErrOffline, KeyErrIdentity, KeyErrSignIn, KeyErrUnknown,
        };

        /// <summary>Resolves a localization key and retains the visible missing-key contract.</summary>
        public static string Get(string key)
        {
            string value = LocalText.Get(key);
            ReportMissing(key, value);
            return value;
        }

        /// <summary>Resolves and formats through the localization authority.</summary>
        public static string Format(string key, params object[] args)
        {
            string value = args == null || args.Length == 0
                ? LocalText.Get(key)
                : LocalText.Format(key, args);
            ReportMissing(key, value);
            return value;
        }

        /// <summary>
        /// Compatibility hook retained for callers compiled against the former local cache.
        /// LocalText owns cache lifetime now, so PromoStrings has nothing to invalidate.
        /// </summary>
        public static void Reload() { }

        private static void ReportMissing(string key, string value)
        {
            string marker = "[[missing:" + (key ?? string.Empty) + "]]";
            if (string.Equals(value, marker, StringComparison.Ordinal))
                FlowTrace.Fail("Promo", "localization key '" + key +
                    "' missing - the redeem screen would show a placeholder instead of a sentence.");
        }
    }
}
