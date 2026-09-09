// =============================================================================
// BuyGateAndPriceLadderRegression [buy-gate] -- WO-1121, the two owner rulings of
// 2026-08-21 that changed what this store may sell and to whom.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Contract mirrors the sibling
// oracles:
//   public static bool Run(out string reason)   -- NEVER throws
//   markers: BUY_GATE_OK (Debug.Log) / BUY_GATE_FAIL (LogError)
//   registered ONCE inside DataRegression.RunAll's fenced registry region.
//
// THE TWO RULINGS
//
//   1. THE PRICE CEILING IS $49.99, NOT $4.99. The $4.99 cap was an EARLY-ACCESS
//      constraint that WO-1118 applied as though it were permanent, and it hid the
//      top three rungs of the ladder monetization-v2-spec §4 has always authored.
//      ⛔ THE COVENANT IS UNCHANGED AND IS ABOUT CONTENT, NOT PRICE: a $49.99 pack
//      that sells time and beauty is fine; a $0.99 pack that sells damage is not.
//      So this suite does NOT police price at all -- it polices what the shelf
//      ADVERTISES, which is the thing that can actually lie to a payer.
//
//   2. WALLET REQUIRED -- PER CHANNEL (re-pinned 2026-09-04, WO-1386). A guest's
//      save key is guest-local-<sha256(deviceId)> -- device-derived, with no proven
//      restore path after a reinstall or a new phone.
//        * PaymentChannel.SolanaDappStore: a wallet is required at EVERY price.
//          Owner, verbatim (2026-09-04 23:12): "nothing should be guest buyable on
//          a crypto account otherwise we can never persist change". $1.99 included.
//        * PaymentChannel.PiBrowser: the same. Owner, same evening, verbatim:
//          "mark anything for Pi as same logic based on USD".
//        * PaymentChannel.Unknown: the same, FAIL-CLOSED.
//        * PaymentChannel.GooglePlay ONLY: the 2026-08-21 threshold holds -- ABOVE
//          $4.99. Play's account is the durable key, so its guest tier stays:
//          $1.99 does not need a wallet, $9.99 does.
//      The threshold constant is kept as history and as the live off-chain rule;
//      the channel switch lives in ONE predicate, RequiresWallet(usd, channel).
//
// WHY THE STRUCTURAL CASES EXIST, AND WHY THEY ARE THE POINT.
// A value check ("this pack is refused today") is cheap to satisfy and cheap to
// break: it passes for the WRONG REASON the moment purchases are enabled, because
// the whole rail is closed right now and EVERY pack is refused. So the load-bearing
// cases here are structural:
//   * [charge-path] PackStore.Purchase() -- the ONLY method in the project that
//     reaches WalletService.Pay -- must consult PurchaseGate.CanBuy(pack, ...), not
//     FeatureFlags.RealmStorePurchase. A rule enforced only where the button is
//     drawn is bypassed by every caller that never drew one (the shortfall offer, a
//     deep link, a promo). That is the exact defect the ruling names, and no
//     value-based assertion can detect it.
//   * [single-threshold] the threshold exists ONCE, as a code constant, and is NOT
//     re-authored as a per-pack `requiresWallet` field. Two copies of one decision
//     is how this repo's worst drift bugs are built (CLAUDE.md §2/§5).
//
// AND THE VAPOR RULE (WO-1118, restated by the owner the same day when glimmer was
// stripped): a browsable pack may not advertise anything this build cannot deliver.
// Cosmetics must exist in cosmetics.json; convenience kinds must pass
// PackCatalog.IsRedeemableConvenience; and NO pack may carry `glimmer` at all,
// because its only sink is cosmetics and no CosmeticApplier runs.
//
// Wire (DataRegression.RunAll, inside the fence):
//   Guard.Try("Regression", "buy-gate suite", () => { if (!BuyGateAndPriceLadderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[buy-gate] " + r); });
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.Payments;   // WO-1386 - PaymentChannel + PaymentChannelResolver.OverrideForTests
using DeNelle.Wallet;

namespace DeNelle.Editor.Regression
{
    /// <summary>Pins the WO-1121 price-ladder ruling and the per-channel wallet buy gate
    /// (WO-1386: every price on Solana, above $4.99 elsewhere).</summary>
    public static class BuyGateAndPriceLadderRegression
    {
        private const string PacksRelPath  = "Data/Canonical/packs.json";
        private const string EnglishRelPath = "Data/Canonical/en.json";
        private const string CosmeticsRel  = "Data/Canonical/cosmetics.json";

        private const string BuyClosedKey = "storeBuyClosed";
        private const string BuyRailNotReadyKey = "storeBuyRailNotReady";
        private const string BuyWalletRequiredKey = "storeBuyWalletRequired";
        private const string BuyWalletRequiredCryptoKey = "storeBuyWalletRequiredCrypto";
        private const string BuyWalletRequiredCtaKey = "storeBuyWalletRequiredCta";
        private const string BuyComingSoonKey = "storeBuyComingSoon";
        private const string ShelfClosedKey = "storeShelfClosed";

        private static readonly KeyValuePair<string, string>[] BuyGateCopy =
        {
            new KeyValuePair<string, string>(BuyClosedKey,
                "Purchases are not open yet. Everything here is still earned in-game."),
            new KeyValuePair<string, string>(BuyRailNotReadyKey,
                "The payment rail is not ready in this build, so nothing was charged. Try again after the next update."),
            new KeyValuePair<string, string>(BuyWalletRequiredKey,
                "Packs over {Threshold} need a connected wallet, so what you buy stays yours if you reinstall or change phones. Connect a wallet to buy this one - anything {Threshold} and under you can buy right now."),
            new KeyValuePair<string, string>(BuyWalletRequiredCryptoKey,
                "Connect a wallet so this purchase is yours on every device."),
            new KeyValuePair<string, string>(BuyWalletRequiredCtaKey, "Connect Wallet"),
            new KeyValuePair<string, string>(BuyComingSoonKey, "Coming soon"),
            new KeyValuePair<string, string>(ShelfClosedKey,
                "Coming soon - purchases are not open in this build. Nothing here is required to play."),
        };

        /// <summary>PlayerPrefs key behind FeatureFlags.RealmStorePurchase (FeatureFlags.Get: "ff." + name).</summary>
        private const string BuyFlagPrefKey = "ff.realmstorepurchase";

        /// <summary>
        /// The full ladder monetization-v2-spec §4 authors, and the ruling that put the top three
        /// rungs back on the shelf. sku -> usd. Hardcoded HERE on purpose: an oracle that read the
        /// ladder out of the same file it is checking would assert nothing about it.
        /// </summary>
        private static readonly KeyValuePair<string, double>[] Ladder =
        {
            // ⭐ ENTRY RUNG IS $4.99 (owner ruling 2026-08-24). It was `hearth-spark` at $1.99
            // until WO-1069 repriced that pack to 4.99 to stop it dominating impulse-wood-small.
            // ⛔ hearth-spark did NOT move to this row: at 4.99 `starters-hand` STRICTLY DOMINATES
            // it (more of all five resources, same price), so hearth-spark left the SHELF entirely
            // rather than sitting on the entry rung as the bad buy. It stays quotable as
            // DEVNET_CANARY_SKU. ⚠ This amends WO-1121's "$1.99..$49.99" to "$4.99..$49.99".
            new KeyValuePair<string, double>("starters-hand",     4.99d),
            new KeyValuePair<string, double>("folks-thanks",      9.99d),
            new KeyValuePair<string, double>("patron-of-elarion", 19.99d),
            new KeyValuePair<string, double>("founders-vow",      49.99d),
        };

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("BUY_GATE_OK - " + reason);
            else Debug.LogError("BUY_GATE_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== BuyGateAndPriceLadderRegression [buy-gate] (WO-1121: $49.99 ceiling; WO-1386: wallet at EVERY price on Solana, above $4.99 elsewhere) ===");

            try
            {
                CaseDualCopy(failures, log);

                JArray packs = ReadPacks(failures, log);
                if (packs != null)
                {
                    CaseLadderIsOnTheShelf(packs, failures, log);
                    CaseNoGlimmerAnywhere(packs, failures, log);
                    CaseShelfAdvertisesOnlyDeliverables(packs, failures, log);
                    CaseSingleThreshold(packs, failures, log);
                    CaseWalletRuleRefusesEveryUpperTier(packs, failures, log);
                }

                CaseThresholdBoundary(failures, log);
                CaseSolanaGuestRefusedWithConnectSentence(failures, log);
                CaseLocalizedRefusalSentences(failures, log);
                CaseChargePathConsultsTheGate(failures, log);
            }
            catch (Exception ex)
            {
                // NEVER throws (the suite contract): a throw here would take the whole gate down
                // and tell nobody which rule broke.
                failures.Add("[buy-gate] BuyGateAndPriceLadderRegression THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "BUY GATE OK - the full $1.99..$49.99 ladder is on the shelf; no pack advertises a " +
                         "cosmetic, a convenience kind or a glimmer line this build cannot deliver; the wallet " +
                         "threshold exists exactly once (PurchaseGate.WalletRequiredAboveUsd = $" +
                         PurchaseGate.WalletRequiredAboveUsd.ToString("0.00", CultureInfo.InvariantCulture) +
                         ") and is never re-authored per pack; on GooglePlay every pack above it is refused " +
                         "while this save has no attested wallet, on SolanaDappStore EVERY pack is (WO-1386) " +
                         "with the connect-a-wallet sentence; and the CHARGE PATH itself (PackStore.Purchase) " +
                         "consults the gate, so the rule is not UI-only.";
                Debug.Log("BUY_GATE_OK\n" + log);
                return true;
            }

            reason = "buy-gate: " + failures.Count + " failure(s): " + string.Join(" | ", failures);
            Debug.LogError("BUY_GATE_FAIL: " + failures.Count + " failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }

        // =====================================================================
        //  [dual-copy] -- runs FIRST. If the two copies differ, every later case
        //  is measuring a file the shipped build may never load, and the price a
        //  player SEES would not be the price they are CHARGED.
        // =====================================================================
        private static void CaseDualCopy(List<string> failures, StringBuilder log)
        {
            AssertCopiesIdentical(PacksRelPath, failures, log);
            AssertCopiesIdentical(EnglishRelPath, failures, log);
        }

        private static void AssertCopiesIdentical(string rel, List<string> failures, StringBuilder log)
        {
            string res = Application.dataPath + "/Resources/" + rel;
            string sa  = Application.dataPath + "/StreamingAssets/" + rel;
            if (!File.Exists(res) || !File.Exists(sa))
            {
                failures.Add("[dual-copy] " + rel + " is missing " +
                             (File.Exists(res) ? "" : "the Resources copy ") +
                             (File.Exists(sa) ? "" : "the StreamingAssets copy") +
                             " - CanonicalJson reads Resources first and falls back to StreamingAssets, so one " +
                             "missing copy silently changes what a shipped build loads.");
                return;
            }

            byte[] a = File.ReadAllBytes(res), b = File.ReadAllBytes(sa);
            bool equal = a.Length == b.Length;
            if (equal)
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { equal = false; break; }

            if (!equal)
                failures.Add("[dual-copy] " + rel + " Resources and StreamingAssets copies DIVERGED (" +
                             a.Length + " vs " + b.Length + " bytes).");
            else
                log.AppendLine("  [dual-copy] " + rel + " byte-identical across both copies (" + a.Length + " bytes)");
        }

        // =====================================================================
        //  [ladder] -- RULING 1. All five rungs priced as the spec authors them,
        //  and all five BROWSABLE. The old $4.99 ceiling hid the top three.
        // =====================================================================
        private static void CaseLadderIsOnTheShelf(JArray packs, List<string> failures, StringBuilder log)
        {
            foreach (var rung in Ladder)
            {
                JObject p = FindPack(packs, rung.Key);
                if (p == null)
                {
                    failures.Add("[ladder] packs.json has no pack '" + rung.Key + "' - the monetization-v2-spec §4 " +
                                 "ladder is incomplete. `sku` is a LIVE save key, so a missing rung is either a " +
                                 "deletion or a rename without a legacySkus alias; both orphan entitlements.");
                    continue;
                }

                double usd = p["pricing"]?["usd"]?.Value<double>() ?? -1d;
                if (Math.Abs(usd - rung.Value) > 0.0001d)
                    failures.Add("[ladder] '" + rung.Key + "' is priced $" + usd.ToString("0.00", CultureInfo.InvariantCulture) +
                                 ", spec §4 says $" + rung.Value.ToString("0.00", CultureInfo.InvariantCulture) + ".");

                bool visible = p["storeVisible"] == null || p["storeVisible"].Type != JTokenType.Boolean
                             || p["storeVisible"].Value<bool>();
                if (!visible)
                    failures.Add("[ladder] '" + rung.Key + "' is storeVisible:false. The owner ruled the FULL " +
                                 "$1.99..$49.99 ladder back onto the shelf on 2026-08-21 - the $4.99 cap was an " +
                                 "EARLY-ACCESS constraint, not a permanent one. Re-hiding a rung is an OWNER " +
                                 "decision; if one was genuinely re-hidden, this list is what to re-rule.");

                if (visible && p["_hiddenReason"] != null)
                    failures.Add("[ladder] '" + rung.Key + "' is VISIBLE but still carries a _hiddenReason - the " +
                                 "row and its own explanation disagree, and the next reader will believe the note.");
            }
            log.AppendLine("  [ladder] 5 rungs checked ($1.99 / $4.99 / $9.99 / $19.99 / $49.99), all browsable");
        }

        // =====================================================================
        //  [no-glimmer] -- owner ruling 2026-08-21, verbatim: "remove all glimmer
        //  from packs as its nothing real and money has never been active".
        //  Glimmer's only sink is cosmetics and CosmeticApplier is called from
        //  nowhere, so a glimmer line on a paid card buys nothing visible.
        //  ⚠ This is about pack CONTENTS. Glimmer the CURRENCY is untouched and is
        //  still earned and spent elsewhere - do not "fix" a failure here by
        //  deleting the currency.
        // =====================================================================
        private static void CaseNoGlimmerAnywhere(JArray packs, List<string> failures, StringBuilder log)
        {
            int scanned = 0;
            foreach (var tok in packs)
            {
                if (!(tok is JObject p)) continue;
                scanned++;
                if (p["contents"]?["economy"]?["glimmer"] != null)
                    failures.Add("[no-glimmer] pack '" + Sku(p) + "' carries a `glimmer` key. Owner ruling " +
                                 "2026-08-21: no pack may. Its only sink is cosmetics, and no CosmeticApplier " +
                                 "runs, so it is a line on a paid card that buys nothing the player can see.");
            }
            log.AppendLine("  [no-glimmer] " + scanned + " packs scanned, none carrying a glimmer line");
        }

        // =====================================================================
        //  [no-vapor] -- the WO-1118 shelf-honesty rule, applied to whatever is
        //  browsable TODAY. Only storeVisible rows are policed: a hidden row is
        //  kept loadable so an existing owner's entitlement still resolves, and
        //  holding it to the shelf's standard would force a delete instead.
        // =====================================================================
        private static void CaseShelfAdvertisesOnlyDeliverables(JArray packs, List<string> failures, StringBuilder log)
        {
            HashSet<string> cosmeticIds = ReadCosmeticIds();
            int shelf = 0;

            foreach (var tok in packs)
            {
                if (!(tok is JObject p)) continue;
                bool visible = p["storeVisible"] == null || p["storeVisible"].Type != JTokenType.Boolean
                             || p["storeVisible"].Value<bool>();
                if (!visible) continue;
                shelf++;
                string sku = Sku(p);

                // Cosmetics: every advertised id must exist in cosmetics.json, or it is a dangling
                // entitlement the player can never equip.
                var cos = p["contents"]?["cosmetics"] as JArray;
                if (cos != null && cosmeticIds != null)
                    foreach (var c in cos)
                    {
                        string id = c?.Value<string>();
                        if (!string.IsNullOrEmpty(id) && !cosmeticIds.Contains(id))
                            failures.Add("[no-vapor] shelf pack '" + sku + "' advertises cosmetic '" + id +
                                         "' which has NO row in cosmetics.json - unredeemable.");
                    }

                // Convenience: LEGAL is not REDEEMABLE. PackCatalog.IsRedeemableConvenience is the
                // live statement about THIS build; asking it (rather than re-listing kinds here)
                // means the day a redeemer ships, this oracle updates itself.
                var conv = p["contents"]?["convenience"] as JArray;
                if (conv != null)
                    foreach (var item in conv)
                    {
                        string kind = (item as JObject)?["kind"]?.Value<string>();
                        if (string.IsNullOrEmpty(kind)) continue;
                        if (!PackCatalog.IsRedeemableConvenience(kind))
                            failures.Add("[no-vapor] shelf pack '" + sku + "' advertises convenience kind '" + kind +
                                         "' which NOTHING in this build spends (PackCatalog.IsRedeemableConvenience " +
                                         "== false). Ship the redeemer first, then re-add the line - that order is " +
                                         "the whole of WO-1118.");
                    }

                // A pack must grant SOMETHING. An all-empty contents bag on a sellable row is the
                // limit case of the vapor rule: money in, nothing out.
                bool anyEconomy = false;
                var econ = p["contents"]?["economy"] as JObject;
                if (econ != null)
                    foreach (var kv in econ)
                        if (kv.Value != null && kv.Value.Type == JTokenType.Integer && kv.Value.Value<long>() > 0)
                        { anyEconomy = true; break; }

                bool anyCosmetic = cos != null && cos.Count > 0;
                bool anyConv = conv != null && conv.Count > 0;
                if (!anyEconomy && !anyCosmetic && !anyConv)
                    failures.Add("[no-vapor] shelf pack '" + sku + "' grants NOTHING (empty economy, cosmetics and " +
                                 "convenience) and is still sellable.");
            }

            log.AppendLine("  [no-vapor] " + shelf + " browsable packs: every advertised cosmetic exists, every " +
                           "convenience kind has a live redeemer, none grants nothing");
        }

        // =====================================================================
        //  [single-threshold] -- STRUCTURAL. The wallet rule is derived from the
        //  authored PRICE, in ONE code constant. A per-pack `requiresWallet` field
        //  would be a second copy of a decision the price already makes, and the
        //  two would drift the first time a pack is repriced.
        // =====================================================================
        private static void CaseSingleThreshold(JArray packs, List<string> failures, StringBuilder log)
        {
            foreach (var tok in packs)
            {
                if (!(tok is JObject p)) continue;
                foreach (var kv in p)
                {
                    string k = kv.Key;
                    if (k.IndexOf("requireswallet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        k.IndexOf("walletrequired", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        k.IndexOf("walletonly", StringComparison.OrdinalIgnoreCase) >= 0)
                        failures.Add("[single-threshold] pack '" + Sku(p) + "' authors a '" + k + "' field. The " +
                                     "wallet rule is DERIVED from pricing.usd in PurchaseGate.RequiresWallet and " +
                                     "must not be re-authored per pack - two copies of one decision is the drift " +
                                     "bug CLAUDE.md §2/§5 documents.");
                }
            }
            log.AppendLine("  [single-threshold] no per-pack wallet override authored; the rule lives only in " +
                           "PurchaseGate.WalletRequiredAboveUsd");
        }

        // =====================================================================
        //  [threshold] -- RULING 2's boundary, asserted on the predicate itself,
        //  PER CHANNEL (re-pinned 2026-09-04, WO-1386).
        //
        //  GooglePlay keeps the 2026-08-21 shape: $4.99 EXACTLY must stay
        //  guest-buyable (the rule is ABOVE the old ceiling, so nothing that was
        //  already guest-buyable may be taken away by a float rounding hair), and
        //  $9.99 / $19.99 / $49.99 need the wallet.
        //
        //  SolanaDappStore has NO guest tier at all. Owner, verbatim (2026-09-04):
        //  "nothing should be guest buyable on a crypto account otherwise we can
        //  never persist change". So $1.99 -- the cheapest impulse pack on the
        //  shelf -- requires a wallet, and so does everything above it.
        //
        //  PROVEN RED (mutations named, one line each, in PurchaseGate.RequiresWallet
        //  (double, PaymentChannel)):
        //    * delete `if (channel == PaymentChannel.SolanaDappStore) return true;`
        //      -> "[threshold/solana] $1.99 must require a wallet" fails.
        //    * change that line to `if (channel == PaymentChannel.GooglePlay) return true;`
        //      -> "[threshold/play] $1.99 must be guest-buyable" fails.
        //    * change the old-signature delegate to pass PaymentChannel.GooglePlay
        //      -> "[threshold/delegate] RequiresWallet(double) must follow the RESOLVED channel" fails.
        // =====================================================================
        private static void CaseThresholdBoundary(List<string> failures, StringBuilder log)
        {
            if (Math.Abs(PurchaseGate.WalletRequiredAboveUsd - 4.99d) > 0.0001d)
                failures.Add("[threshold] PurchaseGate.WalletRequiredAboveUsd is " +
                             PurchaseGate.WalletRequiredAboveUsd.ToString("0.0000", CultureInfo.InvariantCulture) +
                             ", the owner ruled $4.99 (2026-08-21).");

            // -- GooglePlay: the threshold, unchanged. Play's account is the durable key.
            const PaymentChannel play = PaymentChannel.GooglePlay;
            if (PurchaseGate.RequiresWallet(1.99d, play)) failures.Add("[threshold/play] $1.99 must be guest-buyable on GooglePlay.");
            if (PurchaseGate.RequiresWallet(4.99d, play)) failures.Add("[threshold/play] $4.99 EXACTLY must stay guest-buyable on GooglePlay - " +
                                                                      "the rule is ABOVE $4.99, not from $4.99.");
            if (!PurchaseGate.RequiresWallet(9.99d, play))  failures.Add("[threshold/play] $9.99 must require a wallet on GooglePlay.");
            if (!PurchaseGate.RequiresWallet(19.99d, play)) failures.Add("[threshold/play] $19.99 must require a wallet on GooglePlay.");
            if (!PurchaseGate.RequiresWallet(49.99d, play)) failures.Add("[threshold/play] $49.99 must require a wallet on GooglePlay - this is " +
                                                                        "the chargeback case the 2026-08-21 ruling exists for.");

            // -- SolanaDappStore: EVERY price. "nothing should be guest buyable on a crypto account".
            const PaymentChannel sol = PaymentChannel.SolanaDappStore;
            if (!PurchaseGate.RequiresWallet(0.99d, sol))  failures.Add("[threshold/solana] $0.99 must require a wallet on SolanaDappStore - no price is guest-buyable on the crypto rail (owner 2026-09-04).");
            if (!PurchaseGate.RequiresWallet(1.99d, sol))  failures.Add("[threshold/solana] $1.99 must require a wallet on SolanaDappStore - the cheapest impulse pack is NOT guest-buyable (owner 2026-09-04, WO-1386).");
            if (!PurchaseGate.RequiresWallet(4.99d, sol))  failures.Add("[threshold/solana] $4.99 must require a wallet on SolanaDappStore - the old ceiling no longer gates the crypto rail.");
            if (!PurchaseGate.RequiresWallet(9.99d, sol))  failures.Add("[threshold/solana] $9.99 must require a wallet on SolanaDappStore.");
            if (!PurchaseGate.RequiresWallet(49.99d, sol)) failures.Add("[threshold/solana] $49.99 must require a wallet on SolanaDappStore.");

            // -- PiBrowser: EVERY price. Owner, same evening (2026-09-04), verbatim: "mark anything
            //    for Pi as same logic based on USD". RED: `channel != GooglePlay` -> `channel == SolanaDappStore`.
            const PaymentChannel pi = PaymentChannel.PiBrowser;
            if (!PurchaseGate.RequiresWallet(0.99d, pi))  failures.Add("[threshold/pi] $0.99 must require a wallet on PiBrowser - 'mark anything for Pi as same logic based on USD' (owner 2026-09-04).");
            if (!PurchaseGate.RequiresWallet(1.99d, pi))  failures.Add("[threshold/pi] $1.99 must require a wallet on PiBrowser (owner 2026-09-04, WO-1386).");
            if (!PurchaseGate.RequiresWallet(4.99d, pi))  failures.Add("[threshold/pi] $4.99 must require a wallet on PiBrowser - Pi has no guest tier.");
            if (!PurchaseGate.RequiresWallet(49.99d, pi)) failures.Add("[threshold/pi] $49.99 must require a wallet on PiBrowser.");

            // -- Unknown: EVERY price, FAIL-CLOSED. A channel the artifact cannot name gets the strict
            //    rule. RED: add `|| channel == PaymentChannel.Unknown` to the GooglePlay branch.
            const PaymentChannel unk = PaymentChannel.Unknown;
            if (!PurchaseGate.RequiresWallet(0.99d, unk))  failures.Add("[threshold/unknown] $0.99 must require a wallet on an Unknown channel - fail-closed (WO-1386).");
            if (!PurchaseGate.RequiresWallet(1.99d, unk))  failures.Add("[threshold/unknown] $1.99 must require a wallet on an Unknown channel - fail-closed (WO-1386).");
            if (!PurchaseGate.RequiresWallet(4.99d, unk))  failures.Add("[threshold/unknown] $4.99 must require a wallet on an Unknown channel - fail-closed (WO-1386).");
            if (!PurchaseGate.RequiresWallet(49.99d, unk)) failures.Add("[threshold/unknown] $49.99 must require a wallet on an Unknown channel - fail-closed (WO-1386).");

            // -- The old one-argument signature must follow the RESOLVED channel, not a baked one.
            //    Both directions, so a delegate hardwired to either channel fails.
            try
            {
                PaymentChannelResolver.OverrideForTests(sol);
                if (!PurchaseGate.RequiresWallet(1.99d))
                    failures.Add("[threshold/delegate] RequiresWallet(double) must follow the RESOLVED channel: with " +
                                 "SolanaDappStore resolved, $1.99 must require a wallet.");
                PaymentChannelResolver.OverrideForTests(play);
                if (PurchaseGate.RequiresWallet(1.99d))
                    failures.Add("[threshold/delegate] RequiresWallet(double) must follow the RESOLVED channel: with " +
                                 "GooglePlay resolved, $1.99 must stay guest-buyable.");
            }
            finally
            {
                PaymentChannelResolver.ClearTestOverride();
            }

            log.AppendLine("  [threshold] GooglePlay: $1.99/$4.99 guest-buyable, $9.99/$19.99/$49.99 wallet-gated; " +
                           "SolanaDappStore / PiBrowser / Unknown: $0.99..$49.99 ALL wallet-gated; the 1-arg predicate follows the resolved channel");
        }

        // =====================================================================
        //  [solana-guest] -- BEHAVIOURAL + COPY (WO-1386, owner 2026-09-04).
        //  With the channel resolved as SolanaDappStore, the Buy flag forced ON,
        //  and no attested wallet on this save, the ONE pack that survives every
        //  earlier gate in CanBuy (the devnet canary `hearth-spark`, $4.99 -- a
        //  price the OLD rule let a guest buy) must be refused, and refused with
        //  the connect-a-wallet sentence EXACTLY:
        //      "Connect a wallet so this purchase is yours on every device."
        //  Never a bare "wallet required", never the $4.99 threshold sentence,
        //  because on this rail there is no threshold left to name.
        //
        //  PROVEN RED (one line each):
        //    * storeBuyWalletRequiredCrypto reworded ("Wallet required.")
        //      -> "[solana-guest] the Solana refusal sentence is not the owner's" fails.
        //    * PurchaseGate.WalletRefusalSentence: drop the channel test so it always
        //      resolves StoreBuyText.WalletRequired -> "[solana-guest] hearth-spark was refused with
        //      the THRESHOLD sentence" fails.
        //    * PurchaseGate.CanBuy: `RequiresWallet(usd, channel)` -> `RequiresWallet(usd,
        //      PaymentChannel.GooglePlay)` -> "[solana-guest] hearth-spark ($4.99) is
        //      PURCHASABLE by a guest on SolanaDappStore" fails.
        // =====================================================================
        private static void CaseSolanaGuestRefusedWithConnectSentence(List<string> failures, StringBuilder log)
        {
            const string owner = "Connect a wallet so this purchase is yours on every device.";

            // The copy is a localized semantic row, not an inline StoreStrings exception.
            string localizedOwner = StoreBuyText.WalletRequiredCrypto.Resolve();
            if (string.IsNullOrEmpty(localizedOwner) || localizedOwner.StartsWith("[[missing:", StringComparison.Ordinal))
                failures.Add("[solana-guest] localized '" + BuyWalletRequiredCryptoKey + "' did not resolve through StoreBuyText.");
            foreach (char c in owner)
                if (c > 127) { failures.Add("[solana-guest] the Solana refusal sentence is not ASCII - TMP renders it as tofu."); break; }
            if (owner.IndexOf("wallet", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("[solana-guest] the Solana refusal never says 'wallet' - the remedy must be NAMED.");
            if (owner.IndexOf("{Threshold}", StringComparison.Ordinal) >= 0 || owner.IndexOf("{0}", StringComparison.Ordinal) >= 0)
                failures.Add("[solana-guest] the Solana refusal formats a threshold - there is no guest tier on this rail to name.");

            string thresholdSentence = ResolveWalletThreshold();
            if (!string.Equals(PurchaseGate.WalletRefusalSentence(PaymentChannel.SolanaDappStore), localizedOwner, StringComparison.Ordinal))
                failures.Add("[solana-guest] PurchaseGate.WalletRefusalSentence(SolanaDappStore) does not use StoreBuyText.WalletRequiredCrypto.");
            // Owner 2026-09-04: "mark anything for Pi as same logic based on USD" - Pi and an Unknown
            // channel get the SAME connect sentence from the gate. RED: `channel != GooglePlay` ->
            // `channel == SolanaDappStore` in WalletRefusalSentence.
            if (!string.Equals(PurchaseGate.WalletRefusalSentence(PaymentChannel.PiBrowser), localizedOwner, StringComparison.Ordinal))
                failures.Add("[solana-guest] PurchaseGate.WalletRefusalSentence(PiBrowser) is not the owner's connect sentence - Pi is 'same logic based on USD' (2026-09-04).");
            if (!string.Equals(PurchaseGate.WalletRefusalSentence(PaymentChannel.Unknown), localizedOwner, StringComparison.Ordinal))
                failures.Add("[solana-guest] PurchaseGate.WalletRefusalSentence(Unknown) is not the owner's connect sentence - an unnamed channel is fail-closed.");
            if (!string.Equals(PurchaseGate.WalletRefusalSentence(PaymentChannel.GooglePlay), thresholdSentence, StringComparison.Ordinal))
                failures.Add("[solana-guest] PurchaseGate.WalletRefusalSentence(GooglePlay) is not the $4.99 threshold sentence - " +
                             "Play's guest tier must keep its own words.");

            if (PurchaseGate.HasDurableIdentity)
            {
                log.AppendLine("  [solana-guest] " + RegressionOutcome.PartialSkipToken +
                               " this save has an ATTESTED wallet, so the guest refusal cannot be exercised here. " +
                               "The predicate and copy pins above still bind.");
                return;
            }

            bool hadPref = PlayerPrefs.HasKey(BuyFlagPrefKey);
            int prevPref = hadPref ? PlayerPrefs.GetInt(BuyFlagPrefKey, -1) : -1;

            // ⚠ THE GATE THAT MASKED THIS ARM (regression pass 7, 2026-09-04 23:26, RED):
            // PurchaseGate.CanBuy(out reason) asks MaintenanceCatalog.Refuses(Store, ...) FIRST,
            // and MaintenanceCatalog.For (Assets/_Modules/Core/Ops/MaintenanceCatalog.cs:229-234)
            // fails CLOSED for the store on a NULL table - the headless resting state, no toggle
            // fetch ever ran - with UnreadableStoreMessage (:154, "The store is closed because we
            // cannot reach the server..."). So the case never reached the wallet layer.
            //
            // The seam is the one the maintenance suite itself uses (MaintenanceTogglesRegression
            // Case1b): MarkFeatureAbsent() (:463) swaps in an EMPTY, READABLE table - "the toggle
            // system is not deployed here", which the owner ruled is NOT an outage - so the store
            // resolves OPEN with no seal invented. Clear() (:430) puts the null table back in the
            // finally. Guarded: if a LIVE table is already standing (Loaded == true) we leave it
            // alone - a real seal then refuses, and the arm reports THAT by name below rather
            // than clobbering a live catalog to get a green.
            bool touchedMaintenance = false;
            try
            {
                PlayerPrefs.SetInt(BuyFlagPrefKey, 1);
                PackCatalog.Reload();
                PaymentChannelResolver.OverrideForTests(PaymentChannel.SolanaDappStore);
                if (!DeNelle.Core.Ops.MaintenanceCatalog.Loaded)
                {
                    DeNelle.Core.Ops.MaintenanceCatalog.MarkFeatureAbsent();
                    touchedMaintenance = true;
                }

                PackDef canary = PackCatalog.Find(PurchaseGate.DevnetCanarySku);
                if (canary == null)
                {
                    failures.Add("[solana-guest] PackCatalog has no '" + PurchaseGate.DevnetCanarySku + "' - the one sku " +
                                 "that reaches the wallet rule on devnet is gone, so the refusal cannot be exercised.");
                    return;
                }
                double usd = canary.Pricing != null ? canary.Pricing.Usd : 0d;
                bool allowed = PurchaseGate.CanBuy(canary, out string why);
                if (allowed)
                    failures.Add("[solana-guest] " + canary.Sku + " ($" + usd.ToString("0.00", CultureInfo.InvariantCulture) +
                                 ") is PURCHASABLE by a guest on SolanaDappStore. Owner 2026-09-04: \"nothing should be " +
                                 "guest buyable on a crypto account otherwise we can never persist change\".");
                else if (string.Equals(why, thresholdSentence, StringComparison.Ordinal))
                    failures.Add("[solana-guest] " + canary.Sku + " was refused with the THRESHOLD sentence on SolanaDappStore - " +
                                 "that names a $4.99 guest tier the crypto rail no longer has.");
                else if (!string.Equals(why, localizedOwner, StringComparison.Ordinal))
                    failures.Add("[solana-guest] " + canary.Sku + " was refused, but not by the wallet rule with the owner's " +
                                 "sentence. Reason given: \"" + why + "\". An earlier gate (kill switch / rail) is " +
                                 "masking the wallet rule, so this case proves nothing about it.");
                else
                    log.AppendLine("  [solana-guest] " + canary.Sku + " ($" + usd.ToString("0.00", CultureInfo.InvariantCulture) +
                                   ") refused for a guest on SolanaDappStore with: \"" + why + "\"");
            }
            finally
            {
                if (touchedMaintenance) DeNelle.Core.Ops.MaintenanceCatalog.Clear();
                PaymentChannelResolver.ClearTestOverride();
                if (hadPref) PlayerPrefs.SetInt(BuyFlagPrefKey, prevPref);
                else PlayerPrefs.DeleteKey(BuyFlagPrefKey);
                PlayerPrefs.Save();
                PackCatalog.Reload();
            }
        }

        // =====================================================================
        //  [wallet-rule] -- BEHAVIOURAL. With the Buy flag forced ON and no
        //  attested wallet on this machine, EVERY pack above the threshold must be
        //  refused by PurchaseGate.CanBuy(pack, ...), with a non-empty reason.
        //
        //  The flag is forced ON deliberately: with it OFF (the shipping default)
        //  every pack is refused anyway, and the case would pass for a reason that
        //  has nothing to do with the wallet rule - a green that means nothing.
        //  The prior value is restored in a finally, always.
        //
        //  WO-1386 (2026-09-04): this case is the THRESHOLD half, so it runs with
        //  the channel pinned to GooglePlay - the editor resolves Unknown, which
        //  happens to share the threshold today, but a pin that leans on "happens
        //  to" is not a pin. The Solana half is [solana-guest].
        // =====================================================================
        private static void CaseWalletRuleRefusesEveryUpperTier(JArray packs, List<string> failures, StringBuilder log)
        {
            if (PurchaseGate.HasDurableIdentity)
            {
                // A machine whose save carries a real attested wallet cannot demonstrate the refusal.
                // Say so as a PARTIAL SKIP rather than pass silently - a hollow green here would be
                // the exact arithmetic bug RegressionOutcome exists to end.
                log.AppendLine("  [wallet-rule] " + RegressionOutcome.PartialSkipToken +
                               " this save has an ATTESTED wallet, so the without-a-wallet refusal cannot be " +
                               "exercised here. The structural cases below still bind.");
                return;
            }

            bool hadPref = PlayerPrefs.HasKey(BuyFlagPrefKey);
            int prevPref = hadPref ? PlayerPrefs.GetInt(BuyFlagPrefKey, -1) : -1;
            int checkedUpper = 0, checkedGuest = 0;
            try
            {
                PlayerPrefs.SetInt(BuyFlagPrefKey, 1);   // force the rail flag ON for this case only
                PackCatalog.Reload();
                PaymentChannelResolver.OverrideForTests(PaymentChannel.GooglePlay);   // WO-1386: the threshold half

                foreach (var tok in packs)
                {
                    if (!(tok is JObject po)) continue;
                    string sku = Sku(po);
                    PackDef pack = PackCatalog.Find(sku);
                    if (pack == null) continue;

                    double usd = pack.Pricing != null ? pack.Pricing.Usd : 0d;
                    bool allowed = PurchaseGate.CanBuy(pack, out string why);

                    if (PurchaseGate.RequiresWallet(usd, PaymentChannel.GooglePlay))
                    {
                        checkedUpper++;
                        if (allowed)
                            failures.Add("[wallet-rule] '" + sku + "' ($" + usd.ToString("0.00", CultureInfo.InvariantCulture) +
                                         ") is PURCHASABLE with no attested wallet on this save. A guest key is " +
                                         "device-derived with no proven restore path - at this price a lost " +
                                         "entitlement is a chargeback on a live listing.");
                        else if (string.IsNullOrEmpty(why))
                            failures.Add("[wallet-rule] '" + sku + "' was refused with an EMPTY reason - that is a " +
                                         "dead button, which the ruling forbids as explicitly as the sale itself.");
                    }
                    else
                    {
                        checkedGuest++;
                        // The guest tier must NOT be refused BY THE WALLET RULE. It may still be
                        // refused by the rail (no resolvable mint today), so assert on the sentence
                        // rather than on the bool: a guest-tier pack must never be told to connect
                        // a wallet, because connecting one would not change anything for it.
                        if (!allowed && string.Equals(why, ResolveWalletThreshold(),
                                StringComparison.Ordinal))
                            failures.Add("[wallet-rule] '" + sku + "' ($" + usd.ToString("0.00", CultureInfo.InvariantCulture) +
                                         ") was refused with the WALLET sentence, but it is at or under the $" +
                                         PurchaseGate.WalletRequiredAboveUsd.ToString("0.00", CultureInfo.InvariantCulture) +
                                         " ceiling and must stay guest-buyable.");
                    }
                }
            }
            finally
            {
                // Restore EXACTLY what was there, including "no key at all" - leaving a stored 1
                // behind would silently arm the purchase rail on this machine (CLAUDE.md notes a
                // stored ff.realmstorepurchase BEATS the compiled default).
                PaymentChannelResolver.ClearTestOverride();
                if (hadPref) PlayerPrefs.SetInt(BuyFlagPrefKey, prevPref);
                else PlayerPrefs.DeleteKey(BuyFlagPrefKey);
                PlayerPrefs.Save();
                PackCatalog.Reload();
            }

            log.AppendLine("  [wallet-rule/play] " + checkedUpper + " above-threshold packs all refused without a wallet; " +
                           checkedGuest + " guest-tier packs never shown the wallet sentence (channel pinned GooglePlay)");
        }

        // =====================================================================
        //  [copy] -- the refusal must be HONEST AND ACTIONABLE, and authored in
        //  canon-strings.json rather than typed inline (CLAUDE.md §7).
        // =====================================================================
        private static void CaseLocalizedRefusalSentences(List<string> failures, StringBuilder log)
        {
            JObject english = ReadEnglishTable(failures);
            string[] keyConstants =
            {
                StoreBuyText.KeyClosed,
                StoreBuyText.KeyRailNotReady,
                StoreBuyText.KeyWalletRequired,
                StoreBuyText.KeyWalletRequiredCrypto,
                StoreBuyText.KeyWalletRequiredCta,
                StoreBuyText.KeyComingSoon,
                StoreBuyText.KeyShelfClosed,
            };
            var declaredKeys = new HashSet<string>(keyConstants, StringComparer.Ordinal);
            var allKeys = new HashSet<string>(StoreBuyText.AllKeys, StringComparer.Ordinal);
            var wrapperKeys = new HashSet<string>(StringComparer.Ordinal)
            {
                StoreBuyText.Closed.Key,
                StoreBuyText.RailNotReady.Key,
                StoreBuyText.WalletRequired.Key,
                StoreBuyText.WalletRequiredCrypto.Key,
                StoreBuyText.WalletRequiredCta.Key,
                StoreBuyText.ComingSoon.Key,
                StoreBuyText.ShelfClosed.Key,
            };
            if (declaredKeys.Count != BuyGateCopy.Length || allKeys.Count != BuyGateCopy.Length || !declaredKeys.SetEquals(allKeys))
                failures.Add("[copy/authority] StoreBuyText key constants and AllKeys must name the same seven BUY GATE semantic keys exactly once.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var expected in BuyGateCopy)
            {
                string key = expected.Key;
                string authored = english?[key]?.Value<string>();
                if (!declaredKeys.Contains(key))
                    failures.Add("[copy/authority] StoreBuyText has no public key constant for '" + key + "'.");
                if (!wrapperKeys.Contains(key))
                    failures.Add("[copy/authority] StoreBuyText has no LocalizedText field for '" + key + "'.");

                if (!string.Equals(authored, expected.Value, StringComparison.Ordinal))
                    failures.Add("[copy/semantics] English '" + key + "' drifted. Expected \"" + expected.Value +
                                 "\", got \"" + (authored ?? "<missing>") + "\".");
                if (!string.IsNullOrEmpty(authored) && !seen.Add(authored))
                    failures.Add("[copy] '" + key + "' reuses another key's sentence. Each refusal has a different " +
                                 "remedy; sharing one sentence tells the player the wrong thing about at least one.");
            }

            string walletLine = english?[BuyWalletRequiredKey]?.Value<string>() ?? string.Empty;
            if (CountOccurrences(walletLine, "{Threshold}") != 2 || walletLine.IndexOf("{0}", StringComparison.Ordinal) >= 0)
                failures.Add("[copy] 'storeBuyWalletRequired' must contain named {Threshold} exactly twice and no positional {0}; " +
                             "WalletThresholdArguments is the sole runtime source of the threshold.");
            if (walletLine.IndexOf("wallet", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("[copy] 'storeBuyWalletRequired' never says 'wallet' - the refusal must NAME the remedy, " +
                             "not just decline.");

            string formatted = ResolveWalletThreshold();
            string threshold = "$" + PurchaseGate.WalletRequiredAboveUsd.ToString("0.00", CultureInfo.InvariantCulture);
            if (formatted.IndexOf("{Threshold}", StringComparison.Ordinal) >= 0 ||
                CountOccurrences(formatted, threshold) != 2)
                failures.Add("[copy/runtime] StoreBuyText wallet refusal did not resolve named WalletThresholdArguments; got \"" + formatted + "\".");

            string gateSourcePath = Application.dataPath + "/_Modules/Wallet/PurchaseGate.cs";
            string gateSource = File.Exists(gateSourcePath) ? File.ReadAllText(gateSourcePath) : string.Empty;
            if (gateSource.IndexOf("StoreBuyText.", StringComparison.Ordinal) < 0 ||
                gateSource.IndexOf("StoreStrings.", StringComparison.Ordinal) >= 0)
                failures.Add("[copy/authority] PurchaseGate must resolve BUY GATE copy through StoreBuyText/LocalText and must not retain StoreStrings calls.");

            log.AppendLine("  [copy] 7 BUY GATE semantic rows exactly pinned in en.json; StoreBuyText exposes a key and LocalizedText wrapper for each; " +
                           "the Google Play refusal uses named {Threshold} twice and PurchaseGate no longer reads StoreStrings");
        }

        // =====================================================================
        //  [charge-path] -- THE STRUCTURAL CASE THAT MATTERS MOST.
        //  PackStore.Purchase is the only method that reaches WalletService.Pay.
        //  It must consult PurchaseGate, because a rule enforced only where the
        //  button is drawn is bypassed by every caller that never drew one.
        // =====================================================================
        private static void CaseChargePathConsultsTheGate(List<string> failures, StringBuilder log)
        {
            string path = Application.dataPath + "/_Modules/Wallet/PackStore.cs";
            if (!File.Exists(path))
            {
                failures.Add("[charge-path] PackStore.cs not found at " + path + " - the charge path cannot be " +
                             "verified, so this is a FAIL, not an unknown.");
                return;
            }

            string src = File.ReadAllText(path);
            int purchaseAt = src.IndexOf("UniTask<PaymentResult> Purchase(", StringComparison.Ordinal);
            if (purchaseAt < 0)
            {
                failures.Add("[charge-path] PackStore.Purchase(PackDef, CurrencyKind) not found. If it was renamed, " +
                             "this oracle must be re-pointed at the new charge entry point in the SAME change - " +
                             "otherwise the gate silently stops being checked.");
                return;
            }

            string body = src.Substring(purchaseAt);
            int payAt = body.IndexOf("_wallet.Pay(", StringComparison.Ordinal);
            int gateAt = body.IndexOf("PurchaseGate.CanBuy(pack", StringComparison.Ordinal);

            if (gateAt < 0)
                failures.Add("[charge-path] PackStore.Purchase does NOT call PurchaseGate.CanBuy(pack, ...). The " +
                             "wallet rule and the rail gate would then be UI-only, and any caller that never drew " +
                             "a Buy button (the shortfall offer, a deep link, a promo) would charge straight past " +
                             "them. This is the defect the owner's ruling names.");
            else if (payAt >= 0 && gateAt > payAt)
                failures.Add("[charge-path] PackStore.Purchase reaches _wallet.Pay BEFORE consulting PurchaseGate - " +
                             "a gate downstream of the charge is not a gate.");
            else
                log.AppendLine("  [charge-path] PackStore.Purchase consults PurchaseGate.CanBuy(pack, ...) before " +
                               "_wallet.Pay");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        private static string ResolveWalletThreshold()
        {
            string threshold = "$" + PurchaseGate.WalletRequiredAboveUsd.ToString("0.00", CultureInfo.InvariantCulture);
            var arguments = new WalletThresholdArguments(threshold);
            return StoreBuyText.WalletRequired.Resolve(arguments);
        }

        private static JObject ReadEnglishTable(List<string> failures)
        {
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(EnglishRelPath);
                if (string.IsNullOrEmpty(json))
                {
                    failures.Add("[copy/authority] en.json could not be read through CanonicalJson.");
                    return null;
                }
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                failures.Add("[copy/authority] en.json parse failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static int CountOccurrences(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) return 0;
            int count = 0;
            for (int at = 0; (at = value.IndexOf(token, at, StringComparison.Ordinal)) >= 0; at += token.Length)
                count++;
            return count;
        }

        private static JArray ReadPacks(List<string> failures, StringBuilder log)
        {
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(PacksRelPath);
                if (string.IsNullOrEmpty(json))
                {
                    failures.Add("[buy-gate] packs.json could not be read through CanonicalJson.");
                    return null;
                }
                var packs = JObject.Parse(json)["packs"] as JArray;
                if (packs == null || packs.Count == 0)
                {
                    failures.Add("[buy-gate] packs.json has no `packs` array.");
                    return null;
                }
                log.AppendLine("  packs.json: " + packs.Count + " rows enumerated");
                return packs;
            }
            catch (Exception ex)
            {
                failures.Add("[buy-gate] packs.json parse failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static HashSet<string> ReadCosmeticIds()
        {
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(CosmeticsRel);
                if (string.IsNullOrEmpty(json)) return null;
                var items = JObject.Parse(json)["items"] as JArray;
                if (items == null) return null;
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in items)
                {
                    string id = (t as JObject)?["id"]?.Value<string>();
                    if (!string.IsNullOrEmpty(id)) set.Add(id);
                }
                return set;
            }
            catch
            {
                // Returning null degrades the cosmetic check to "not asserted" rather than throwing
                // the whole suite; the caller skips it. Swallowing is acceptable ONLY because the
                // fallback is visible in the log line, not silent.
                return null;
            }
        }

        private static JObject FindPack(JArray packs, string sku)
        {
            foreach (var tok in packs)
                if (tok is JObject p && string.Equals(Sku(p), sku, StringComparison.Ordinal))
                    return p;
            return null;
        }

        private static string Sku(JObject p) => p["sku"]?.Value<string>() ?? "<no-sku>";
    }
}
