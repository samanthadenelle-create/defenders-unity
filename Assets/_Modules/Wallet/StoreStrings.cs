// =============================================================================
// StoreStrings — legacy access for store copy not yet migrated to LocalText.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// New or migrated player-facing copy must use LocalText through a module shim such
// as StoreBuyText. This reader remains temporarily for the unmigrated Store rows;
// retire it as those cohorts move onto the shared localization authority.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Wallet
{
    /// <summary>Legacy canon-backed access for Store rows not yet migrated to LocalText.</summary>
    public static class StoreStrings
    {
        private const string CanonRelativePath = "Data/Canonical/canon-strings.json";

        /// <summary>
        /// WO-1386, second ruling the same evening (owner 2026-09-04, verbatim: <i>"mark anything for
        /// Pi as same logic based on USD"</i>). The Pi-worded card plate for the wallet rule: Pi has
        /// NO guest tier either, so the plate can no longer name a $4.99 line. Still Pi-worded per
        /// WO-1323 (a Pi player is not sent to a Solana wallet flow by a button; this is a PLATE), and
        /// still ASCII, SKR-free and distinct from the localized Solana sentence. This remaining
        /// legacy exception is inline because canon-strings.json was outside its original lane;
        /// MOVE it to LocalText later, never copy it.
        /// </summary>
        public const string PiWalletRequiredSentence =
            "Connect a wallet before buying in Pi, at any price, so this purchase is yours on every device.";

        /// <summary>The Pi card plate for the wallet rule. See <see cref="PiWalletRequiredSentence"/>.</summary>
        public static string PiWalletRequired() => PiWalletRequiredSentence;

        /// <summary>
        /// WO-1409 — THE ONE SENTENCE A WALLETLESS SHELF OWES THE PLAYER. Without a signing wallet
        /// the SKR quote cannot be taken, and the store used to answer that by printing
        /// "Price unavailable" on six cards and "UNAVAILABLE" on three more — nine refusals and not
        /// one reason. The authored USD anchor is known with or without a wallet, so every card now
        /// carries it and the REASON is said EXACTLY ONCE, here, under the wordmark.
        ///
        /// <para>⛔ ONE SOURCE, TWO READERS. PackStore renders it and both the FlowTrace line and
        /// NightMarketNoWalletRegression probe for it; a second copy of this sentence anywhere is
        /// how "exactly one banner" quietly becomes two. Same one-exception rule as
        /// <see cref="PiWalletRequiredSentence"/>: it is a sentence in code because
        /// canon-strings.json was outside this lane. MOVE it to canon later — never copy it.</para>
        ///
        /// <para>ASCII only: the separator is a HYPHEN, not an en dash. The store's copy oracle
        /// rejects non-ASCII, and an en dash here would fail the whole shelf's ASCII pass.</para>
        /// </summary>
        public const string WalletlessBrowsingBanner =
            "Connect a wallet to buy - prices shown in USD";

        /// <summary>
        /// The stable PREFIX of <see cref="WalletlessBrowsingBanner"/>, and the only thing the trace
        /// and the oracle match on. Probing the whole sentence would make every future re-wording of
        /// the tail a false red; probing this names the promise ("connect a wallet") that must
        /// survive any re-wording.
        /// </summary>
        public const string WalletlessBrowsingBannerProbe = "Connect a wallet";

        // =====================================================================
        //  WO-1050 — The Night Market presentation copy
        // ---------------------------------------------------------------------
        //  Same rule as the buy-gate block above: KEYS only, never sentences. The
        //  words live in canon-strings.json (both canonical copies, ASCII only).
        // =====================================================================

        /// <summary>The store's wordmark.</summary>
        public const string KeyWordmark = "storeWordmark";

        // Band heads — an eyebrow and a sub-label per band. The eyebrow is what makes band
        // identity survive a greyscale read; the colour is never the message (the owner is
        // red/green colourblind — CLAUDE.md house rule).
        public const string KeyBandFree          = "storeBandFree";
        public const string KeyBandFreeSub       = "storeBandFreeSub";
        public const string KeyBandGap           = "storeBandGap";
        public const string KeyBandGapSub        = "storeBandGapSub";
        public const string KeyBandBasket        = "storeBandBasket";
        public const string KeyBandBasketSub     = "storeBandBasketSub";
        public const string KeyBandPatronage     = "storeBandPatronage";
        public const string KeyBandPatronageSub  = "storeBandPatronageSub";

        // Spotlight.
        public const string KeySpotlightEmpty = "storeSpotlightEmpty";
        public const string KeyLedgerHeading  = "storeLedgerHeading";
        /// <summary>{0}=goods ratio, {1}=other pack name, {2}=good, {3}=price ratio. PURE ARITHMETIC.</summary>
        public const string KeyCompareLine    = "storeCompareLine";
        /// <summary>{0}=total granted goods per US dollar. Summed from the SAME bag the grant seam
        /// pays out, or the caption is absent — never an invented value index.</summary>
        public const string KeyValuePerDollar = "storeValuePerDollar";
        /// <summary>{0}=the player's own wallet balance minus this pack's price.</summary>
        public const string KeyBalanceAfter   = "storeBalanceAfter";

        // Card state WORDS. Every state carries a word, never a colour alone.
        public const string KeyCardOwned  = "storeCardOwned";
        public const string KeyCardAnchor = "storeCardAnchor";
        public const string KeyCardGap    = "storeCardGap";

        // ── The wallet mirror ────────────────────────────────────────────────
        // ⛔ FOUR DISTINCT SENTENCES BECAUSE THEY ARE FOUR DISTINCT FACTS. "No wallet connected"
        // is not "zero balance", and neither is "we could not read it". Collapsing them into a
        // confident "0 SKR" would launder three different truths into one number — the same defect
        // class that got keepers-satchel hidden. The game NEVER holds SKR; this is a read-only
        // mirror of the player's own wallet, which is why the copy says "your wallet".
        public const string KeyBalanceNoWallet    = "storeBalanceNoWallet";
        /// <summary>A live account is attached but not authorized. UI-002: identity is bound,
        /// authorization is not.
        /// <para>⛔ WO-1334 - NO {0}, AND THAT IS THE POINT. This sentence used to carry the
        /// shortened base58 address. Owner ruling 2026-09-03: <i>"they dont need address"</i> - a
        /// player does not verify base58 by eye, and on the device it was the biggest single
        /// contributor to the unreadable header clump. Callers use Get, not Format.</para></summary>
        public const string KeyBalanceBoundAddress  = "storeBalanceBoundAddress";
        /// <summary>A durable identity exists but no live account is attached to read a balance from.</summary>
        public const string KeyBalanceBoundIdentity = "storeBalanceBoundIdentity";
        public const string KeyBalanceChecking    = "storeBalanceChecking";
        public const string KeyBalanceUnavailable = "storeBalanceUnavailable";
        public const string KeyBalanceValue       = "storeBalanceValue";
        /// <summary>{0}=approximate USD, from a LIVE Jupiter quote. Keeps its tilde; dropped if stale.
        /// <para>⚠ WO-1334 - CURRENTLY UNRENDERED. Its one reader was the header chip's `~$12.40`
        /// tail, and the owner ruled the connected chip is <c>SKR: &lt;balance&gt;</c> and nothing
        /// else. The KEY and its canon row are kept deliberately - the sentence is still correct,
        /// and a surface with room for it (a wallet detail sheet) can adopt it without re-authoring
        /// copy. Do NOT re-add it to the header chip.</para></summary>
        public const string KeyBalanceFiat        = "storeBalanceFiat";

        // UI-002 commerce lifecycle. ASCII state words are deliberately distinct so
        // pending/success/failure remain legible with every colour removed.
        public const string KeyCommerceReady             = "storeCommerceReady";
        public const string KeyCommerceOpeningWallet     = "storeCommerceOpeningWallet";
        public const string KeyCommerceAwaitingApproval  = "storeCommerceAwaitingApproval";
        public const string KeyCommerceSubmitted         = "storeCommerceSubmitted";
        public const string KeyCommerceVerifying         = "storeCommerceVerifying";
        public const string KeyCommerceDelivering        = "storeCommerceDelivering";
        public const string KeyCommerceFulfilled         = "storeCommerceFulfilled";
        public const string KeyCommerceCancelled         = "storeCommerceCancelled";
        public const string KeyCommerceFailed            = "storeCommerceFailed";
        public const string KeyCommerceDelayed           = "storeCommerceDelayed";

        // Trust strip — four claims, each verifiable, covenant last.
        public const string KeyTrustFee        = "storeTrustFee";
        /// <summary>{0}=the shortened on-chain Rewards Distributor address.</summary>
        public const string KeyTrustTreasury   = "storeTrustTreasury";
        public const string KeyTrustNeverPower = "storeTrustNeverPower";
        public const string KeyCovenant        = "storeCovenant";

        // =====================================================================
        //  WO-1323 — THE PI SKIN'S STORE COPY
        // ---------------------------------------------------------------------
        //  ⛔ THESE ARE WHAT THE STORE SAYS INSTEAD OF A NUMBER. Under the Pi skin
        //  the client is not allowed to price anything: every Pi figure on the shelf
        //  came back from /api/pi/quote (server-side, CoinGecko low_24h, fail-closed).
        //  When there is no server figure the answer is one of these SENTENCES —
        //  never a converted USD anchor, never a cached figure, and never the SKR
        //  amount, which is a token this game has never held and the Pi player cannot
        //  spend. Keys only, as everywhere above.
        // =====================================================================

        /// <summary>Replaces the SKR wallet/balance chip in the header under the Pi skin.</summary>
        public const string KeyPiHeaderNotice = "storePiHeaderNotice";
        /// <summary>The pack is Pi-purchasable but no server quote has been taken yet.</summary>
        public const string KeyPiPriceAtCheckout = "storePiPriceAtCheckout";
        /// <summary>This pack is not on the Pi rail at all (the server would refuse to quote it).</summary>
        public const string KeyPiNotOnSale = "storePiNotOnSale";
        /// <summary>Pi itself is not reachable, so nothing here can be priced or bought.</summary>
        public const string KeyPiRailUnavailable = "storePiRailUnavailable";
        /// <summary>{0} = PurchaseGate.WalletRequiredAboveUsd. The guest ceiling, reworded for Pi.
        /// <para>⚠ STALE SINCE 2026-09-04 (WO-1386, owner: <i>"mark anything for Pi as same logic
        /// based on USD"</i>): Pi has NO guest tier any more, so the canon row this key names
        /// ("Packs over {0} are not on sale in Pi yet...") is no longer true and NOTHING renders it.
        /// PackStore's Pi plate reads <see cref="PiWalletRequired"/> instead. The key and its canon
        /// row are kept only because canon-strings.json was outside the edit lane; the follow-up is
        /// to reword the row to the <see cref="PiWalletRequiredSentence"/> text and re-point the
        /// plate at the key (MOVE the sentence, do not keep both).</para></summary>
        public const string KeyPiWalletGate = "storePiWalletGate";
        /// <summary>NOTHING on the shelf is Pi-purchasable — shown as the honest state it is.</summary>
        public const string KeyPiShelfEmpty = "storePiShelfEmpty";

        /// <summary>Every Pi-skin key, so the oracle can prove each resolves and is ASCII-clean.</summary>
        public static readonly string[] PiSkinKeys =
        {
            KeyPiHeaderNotice, KeyPiPriceAtCheckout, KeyPiNotOnSale,
            KeyPiRailUnavailable, KeyPiWalletGate, KeyPiShelfEmpty,
        };

        /// <summary>Every Night Market key, so an oracle can prove each one resolves to a real sentence.</summary>
        public static readonly string[] NightMarketKeys =
        {
            KeyWordmark,
            KeyBandFree, KeyBandFreeSub, KeyBandGap, KeyBandGapSub,
            KeyBandBasket, KeyBandBasketSub, KeyBandPatronage, KeyBandPatronageSub,
            KeySpotlightEmpty, KeyLedgerHeading, KeyCompareLine, KeyValuePerDollar, KeyBalanceAfter,
            KeyCardOwned, KeyCardAnchor, KeyCardGap,
            KeyBalanceNoWallet, KeyBalanceBoundAddress, KeyBalanceBoundIdentity,
            KeyBalanceChecking, KeyBalanceUnavailable,
            KeyBalanceValue, KeyBalanceFiat,
            KeyCommerceReady, KeyCommerceOpeningWallet, KeyCommerceAwaitingApproval,
            KeyCommerceSubmitted, KeyCommerceVerifying, KeyCommerceDelivering,
            KeyCommerceFulfilled, KeyCommerceCancelled, KeyCommerceFailed,
            KeyCommerceDelayed,
            KeyTrustFee, KeyTrustTreasury, KeyTrustNeverPower, KeyCovenant,
        };

        // =====================================================================
        //  WORK_ORDER_battle_and_monthly_packs — the Season Track (U1) and the
        //  Monthly Ledger (U2)
        // ---------------------------------------------------------------------
        //  Same rule as every block above: KEYS only, never sentences.
        //
        //  ⛔ TWO OF THESE KEYS EXIST BECAUSE OF A RULE, NOT BECAUSE OF A LAYOUT,
        //  and deleting either one breaks a promise rather than a screen:
        //    * The four seasonTrackState* / four monthlyLedgerState* words are what
        //      make both screens survive a GREYSCALE read. The owner is red/green
        //      colourblind; a state carried by a colour alone is a defect, so every
        //      cell prints its word. Strip every hue and both screens still read.
        //    * monthlyLedgerClaimsLeft says "N claims left" and there is
        //      deliberately NO countdown key anywhere in this block. Under the pool
        //      claim model nothing expires, so a ticking clock would be a lie that
        //      manufactures urgency — exactly the pressure the WO's §3.2 promises
        //      not to apply. If someone ever asks for a timer here, the answer is
        //      that there is nothing to time.
        // =====================================================================

        public const string KeySeasonTrackTitle            = "seasonTrackTitle";
        /// <summary>{0}=whole days left in the calendar month. A COUNT, never a clock.</summary>
        public const string KeySeasonTrackDaysLeft         = "seasonTrackDaysLeft";
        /// <summary>{0}=current tier, {1}=tier count.</summary>
        public const string KeySeasonTrackTierLine         = "seasonTrackTierLine";
        /// <summary>{0}=XP now, {1}=XP for the next tier.</summary>
        public const string KeySeasonTrackXpLine           = "seasonTrackXpLine";
        /// <summary>{0}=total season XP. Shown once every tier is earned.</summary>
        public const string KeySeasonTrackXpLineCapstone   = "seasonTrackXpLineCapstone";
        public const string KeySeasonTrackEarnRate         = "seasonTrackEarnRate";
        public const string KeySeasonTrackKeptForever      = "seasonTrackKeptForever";
        public const string KeySeasonTrackLaneFree         = "seasonTrackLaneFree";
        public const string KeySeasonTrackLanePremium      = "seasonTrackLanePremium";
        public const string KeySeasonTrackStateEarned      = "seasonTrackStateEarned";
        public const string KeySeasonTrackStateReady       = "seasonTrackStateReady";
        public const string KeySeasonTrackStateLocked      = "seasonTrackStateLocked";
        public const string KeySeasonTrackStatePremiumLock = "seasonTrackStatePremiumLocked";
        public const string KeySeasonTrackCapstone         = "seasonTrackCapstone";
        public const string KeySeasonTrackClaimCta         = "seasonTrackClaimCta";
        public const string KeySeasonTrackNothingToClaim   = "seasonTrackNothingToClaim";
        public const string KeySeasonTrackLaneCta          = "seasonTrackLaneCta";
        public const string KeySeasonTrackLaneNotForSale   = "seasonTrackLaneNotForSale";
        public const string KeySeasonTrackLaneRetro        = "seasonTrackLaneRetro";
        public const string KeySeasonTrackEmpty            = "seasonTrackEmpty";

        public const string KeyMonthlyLedgerTitle          = "monthlyLedgerTitle";
        /// <summary>{0}=claims remaining in the pool. The header line. There is no date here on purpose.</summary>
        public const string KeyMonthlyLedgerClaimsLeft     = "monthlyLedgerClaimsLeft";
        public const string KeyMonthlyLedgerNoCard         = "monthlyLedgerNoCard";
        public const string KeyMonthlyLedgerClaimCta       = "monthlyLedgerClaimCta";
        public const string KeyMonthlyLedgerClaimedToday   = "monthlyLedgerClaimedToday";
        public const string KeyMonthlyLedgerTodayReward    = "monthlyLedgerTodayReward";
        public const string KeyMonthlyLedgerPoolPromise    = "monthlyLedgerPoolPromise";
        public const string KeyMonthlyLedgerNoTimer        = "monthlyLedgerNoTimer";
        public const string KeyMonthlyLedgerBonusOnly      = "monthlyLedgerBonusOnly";
        public const string KeyMonthlyLedgerStateClaimed   = "monthlyLedgerStateClaimed";
        public const string KeyMonthlyLedgerStateToday     = "monthlyLedgerStateToday";
        public const string KeyMonthlyLedgerStateAvailable = "monthlyLedgerStateAvailable";
        public const string KeyMonthlyLedgerStateUpcoming  = "monthlyLedgerStateUpcoming";
        public const string KeyMonthlyLedgerExclusiveNone  = "monthlyLedgerExclusiveNone";
        public const string KeyMonthlyLedgerNotForSale     = "monthlyLedgerNotForSale";
        public const string KeyMonthlyLedgerNotForSaleCta  = "monthlyLedgerNotForSaleCta";
        public const string KeyMonthlyLedgerEmpty          = "monthlyLedgerEmpty";
        /// <summary>{0}=week number, {1}=first day, {2}=last day.</summary>
        public const string KeyMonthlyLedgerWeekTab        = "monthlyLedgerWeekTab";
        public const string KeyMonthlyLedgerWeekSelected   = "monthlyLedgerWeekSelected";
        public const string KeyMonthlyLedgerWeekClaimable  = "monthlyLedgerWeekClaimable";
        /// <summary>{0}=day number.</summary>
        public const string KeyMonthlyLedgerDay            = "monthlyLedgerDay";
        public const string KeyMonthlyLedgerMilestone      = "monthlyLedgerMilestone";

        /// <summary>Every Season Track key, so an oracle can prove each resolves to a real sentence.</summary>
        public static readonly string[] SeasonTrackKeys =
        {
            KeySeasonTrackTitle, KeySeasonTrackDaysLeft, KeySeasonTrackTierLine, KeySeasonTrackXpLine,
            KeySeasonTrackXpLineCapstone, KeySeasonTrackEarnRate, KeySeasonTrackKeptForever,
            KeySeasonTrackLaneFree, KeySeasonTrackLanePremium,
            KeySeasonTrackStateEarned, KeySeasonTrackStateReady, KeySeasonTrackStateLocked,
            KeySeasonTrackStatePremiumLock, KeySeasonTrackCapstone,
            KeySeasonTrackClaimCta, KeySeasonTrackNothingToClaim, KeySeasonTrackLaneCta,
            KeySeasonTrackLaneNotForSale, KeySeasonTrackLaneRetro, KeySeasonTrackEmpty,
        };

        /// <summary>Every Monthly Ledger key.</summary>
        public static readonly string[] MonthlyLedgerKeys =
        {
            KeyMonthlyLedgerTitle, KeyMonthlyLedgerClaimsLeft, KeyMonthlyLedgerNoCard,
            KeyMonthlyLedgerClaimCta, KeyMonthlyLedgerClaimedToday, KeyMonthlyLedgerTodayReward,
            KeyMonthlyLedgerPoolPromise, KeyMonthlyLedgerNoTimer, KeyMonthlyLedgerBonusOnly,
            KeyMonthlyLedgerStateClaimed, KeyMonthlyLedgerStateToday, KeyMonthlyLedgerStateAvailable,
            KeyMonthlyLedgerStateUpcoming, KeyMonthlyLedgerExclusiveNone, KeyMonthlyLedgerNotForSale,
            KeyMonthlyLedgerNotForSaleCta, KeyMonthlyLedgerEmpty,
            KeyMonthlyLedgerWeekTab, KeyMonthlyLedgerWeekSelected,
            KeyMonthlyLedgerWeekClaimable, KeyMonthlyLedgerDay, KeyMonthlyLedgerMilestone,
        };

        /// <summary>
        /// The four state WORDS of the Season Track and the four of the Monthly Ledger, in one place
        /// so the greyscale oracle can assert each is present, non-empty and DISTINCT from its
        /// siblings. Two states sharing a word would be two states the owner cannot tell apart with
        /// the hue removed, which is the whole failure this block exists to prevent.
        /// </summary>
        public static readonly string[] StateWordKeys =
        {
            KeySeasonTrackStateEarned, KeySeasonTrackStateReady, KeySeasonTrackStateLocked,
            KeySeasonTrackStatePremiumLock,
            KeyMonthlyLedgerStateClaimed, KeyMonthlyLedgerStateToday,
            KeyMonthlyLedgerStateAvailable, KeyMonthlyLedgerStateUpcoming,
        };

        private static Dictionary<string, string> _canon;

        /// <summary>Resolves a canon key. Returns "[[missing:key]]" (and self-reports) when absent.</summary>
        public static string Get(string key)
        {
            EnsureLoaded();
            if (_canon != null && key != null && _canon.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;
            FlowTrace.Fail("Store", $"canon-strings key '{key}' missing — the store would refuse a purchase with a " +
                                    "placeholder marker instead of a sentence.");
            return $"[[missing:{key}]]";
        }

        /// <summary>Resolves a canon key and formats it. A bad format string degrades to the raw sentence.</summary>
        public static string Format(string key, params object[] args)
        {
            string raw = Get(key);
            if (args == null || args.Length == 0) return raw;
            try { return string.Format(raw, args); }
            catch (FormatException ex)
            {
                FlowTrace.Fail("Store", $"canon-strings key '{key}' has a bad format placeholder: {ex.Message}");
                return raw;
            }
        }

        /// <summary>Test/diagnostic hook — drops the cached map so a re-read picks up an edit.</summary>
        public static void Reload() { _canon = null; }

        private static void EnsureLoaded()
        {
            if (_canon != null) return;
            try
            {
                string json = CanonicalJson.Read(CanonRelativePath);
                if (string.IsNullOrEmpty(json))
                {
                    FlowTrace.Fail("Store", $"canonical file not found (Resources or StreamingAssets): {CanonRelativePath} " +
                                            "— every buy-gate refusal would render as a placeholder.");
                    _canon = new Dictionary<string, string>();
                    return;
                }

                // Flat string->string map with some leading "_" metadata keys: deserialize
                // loosely, keep only the string entries (the CanonStrings convention).
                var raw = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                var map = new Dictionary<string, string>();
                if (raw != null)
                {
                    foreach (var kv in raw)
                        if (kv.Value is string s) map[kv.Key] = s;
                }
                _canon = map;
            }
            catch (Exception ex)
            {
                // No silent catch (§12): the screen still works, but say why it lost its words.
                FlowTrace.Fail("Store", $"failed to read {CanonRelativePath}: {ex.GetType().Name}: {ex.Message}");
                _canon = new Dictionary<string, string>();
            }
        }
    }
}
