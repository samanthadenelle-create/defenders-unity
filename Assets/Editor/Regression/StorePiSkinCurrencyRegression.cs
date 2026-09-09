// =============================================================================
// StorePiSkinCurrencyRegression - WO-1323.
// -----------------------------------------------------------------------------
// THE DEFECT THIS PINS, read off the owner's own felt-test in REAL Pi Browser
// (2026-09-02). She was signed in as Pi - the title chip read "Pi: samanthadenelle"
// and the session log read:
//
//     Currency skin resolved: 'pi' (auth=PiSdk, symbol=pi, identity=PiUid)
//
// and the Night Market quoted her "1022 SKR", "2555 SKR", "511 SKR",
// "BUY - 255 SKR", offered "Connect a wallet to see your balance", and showed a
// Mainnet/SKR readiness chip.
//
// SKR is Solana Mobile's governance token. This game has never minted it, never held
// it and does not own it - PackStore says so in its own header - and a Pi player
// cannot spend it. Quoting it at her is the same error that comment forbids, aimed at
// the wrong audience.
//
// ── WHAT THIS ORACLE PROVES, AND WHY EACH HALF IS NEEDED ─────────────────────
//   A. UNDER THE PI SKIN, NO SKR STRING IS REACHABLE IN THE STORE SURFACE. Every
//      site that can print an SKR figure or Solana wallet furniture is behind the
//      PiDisplay hinge, and that hinge is resolved from the SKIN (which demonstrably
//      resolved correctly in the field) rather than from the payment CHANNEL (which
//      demonstrably did not register).
//   B. UNDER THE SKR SKIN NOTHING CHANGES. Proved by the shape of every guard: each
//      one is an ADDED `PiDisplay` test in front of behaviour that is otherwise
//      byte-identical, and the SKR strings/paths themselves are asserted STILL PRESENT.
//      A "fix" that deleted the SKR path would fail this suite, not pass it.
//   C. THE CLIENT NEVER PRICES ANYTHING. No `pi` constant in packs.json, no second
//      HTTP client, and a refused/expired quote CLEARS the cached figure instead of
//      leaving the last one on the shelf.
//   D. THE EMPTY PI SHELF IS AN HONEST STATE, NOT A FLAG FLIP. hearth-spark is the
//      only Pi-quotable sku and it is storeVisible:false by WO-1069; this suite pins
//      that flag DOWN, so nobody can resolve an empty Pi shelf by reversing a pricing
//      ruling with a display change.
//   E. THE SPOTLIGHT REACHES IT ANYWAY (owner ruling 2026-09-02, section 6 below).
//      Asked directly, she chose to surface the ONE Pi-priced pack through the EXISTING
//      StoreFocusRequest latch rather than widen Pi pricing - proving quote -> approve ->
//      complete -> grant on one sku beats a full shelf where one rail bug hits all 28.
//      So this suite now pins BOTH halves at once, and they are the halves that pull in
//      opposite directions: the pack is REACHABLE (a latch, the real gates, a Pi price
//      requested, the empty-shelf notice stood down) and STILL NOT SHELVED (storeVisible
//      present and false, in both canonical copies, one sku literal in the store).
//
// Marker: STORE_PI_SKIN_OK / STORE_PI_SKIN_FAIL (unique repo-wide).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Wallet;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1323: the Pi skin's store never speaks SKR, and the SKR skin is untouched.</summary>
    public static class StorePiSkinCurrencyRegression
    {
        public static void RunAll()
        {
            if (Run(out var reason)) Debug.Log("STORE_PI_SKIN_OK - " + reason);
            else Debug.LogError("STORE_PI_SKIN_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var fail = new List<string>();
            int checks = 0;

            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;

                string storePath = Path.Combine(root, "Assets/_Modules/Wallet/PackStore.cs");
                string piPath = Path.Combine(root,
                    "Assets/_Modules/Core/Payments/Providers/Pi/PiBrowserPaymentProvider.cs");
                string canonAPath = Path.Combine(root, "Assets/Resources/Data/Canonical/canon-strings.json");
                string canonBPath = Path.Combine(root, "Assets/StreamingAssets/Data/Canonical/canon-strings.json");
                string packsAPath = Path.Combine(root, "Assets/Resources/Data/Canonical/packs.json");
                string packsBPath = Path.Combine(root, "Assets/StreamingAssets/Data/Canonical/packs.json");

                foreach (string p in new[] { storePath, piPath, canonAPath, canonBPath, packsAPath, packsBPath })
                    if (!File.Exists(p)) fail.Add("missing file " + p);
                if (fail.Count > 0)
                {
                    // An unreadable input is a FAIL, never a quiet green (WO-1138).
                    reason = string.Join("; ", fail);
                    return false;
                }

                string store = File.ReadAllText(storePath);
                string pi = File.ReadAllText(piPath);
                string canonA = File.ReadAllText(canonAPath);
                string canonB = File.ReadAllText(canonBPath);

                // =============================================================
                //  1. THE COPY
                // =============================================================
                if (!string.Equals(canonA, canonB, StringComparison.Ordinal))
                    fail.Add("the two canonical canon-strings.json copies differ");
                checks++;

                var canon = JObject.Parse(canonA);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                string[] piKeys = StoreStrings.PiSkinKeys;   // the class's OWN list - never retyped here
                if (piKeys == null || piKeys.Length < 6)
                    fail.Add("StoreStrings.PiSkinKeys is missing or shrank below the six authored Pi keys");
                checks++;

                foreach (string key in piKeys ?? new string[0])
                {
                    string value = (string)canon[key];
                    if (string.IsNullOrWhiteSpace(value)) { fail.Add("missing Pi-skin key " + key); continue; }

                    // ASCII only - TMP renders anything else as tofu.
                    foreach (char c in value)
                        if (c > 127) { fail.Add(key + " is not ASCII-clean"); break; }

                    // ⛔ THE POINT OF THE WHOLE WORK ORDER: the Pi player's copy may not name SKR.
                    if (value.IndexOf("SKR", StringComparison.OrdinalIgnoreCase) >= 0)
                        fail.Add(key + " names SKR in copy shown to a Pi player: " + value);

                    if (!seen.Add(value))
                        fail.Add("two Pi-skin keys share the same sentence: " + value);
                    checks++;
                }

                // B: the SKR copy is STILL THERE, unchanged. A pass earned by deleting the SKR
                // strings would be the opposite of "the SKR skin is byte-for-byte unchanged".
                RequireCanon(canon, "storeBalanceValue", "SKR", fail, ref checks);
                RequireCanon(canon, "storeBalanceAfter", "SKR", fail, ref checks);
                RequireCanon(canon, "storeBalanceNoWallet", "Connect a wallet", fail, ref checks);

                // =============================================================
                //  2. THE HINGE IS THE SKIN, NOT THE CHANNEL
                // =============================================================
                Require(store, "private static bool PiDisplay => PiSkinActive || PiRailOwnsTheStore;",
                    "the Pi display hinge is gone or renamed", fail, ref checks);
                Require(store, "skin.AuthMode == SkinAuthMode.PiSdk",
                    "PiSkinActive no longer reads the resolved SKIN auth mode - the field session proved the " +
                    "payment CHANNEL is the signal that fails, so a channel-only hinge reopens this defect",
                    fail, ref checks);
                Require(store, "string.Equals(skin.SkinId, \"pi\", StringComparison.Ordinal)",
                    "PiSkinActive no longer pins skin id 'pi' - the generic 'wallet' skin also carries PiSdk " +
                    "auth and would be dragged into Pi wording", fail, ref checks);
                Require(store, "provider.Channel == PaymentChannel.PiBrowser",
                    "PiRailOwnsTheStore no longer asks who takes the money - who-is-looking and who-charges " +
                    "are two questions and both must survive", fail, ref checks);

                // =============================================================
                //  3. EVERY SKR SITE IS BEHIND THE HINGE
                // =============================================================
                // The header chip. Guarded BEFORE the switch, so no state added later can reach the
                // "  SKR" identity line at the tail of RenderBalanceLabel.
                RequireOrdered(store,
                    "private void RenderBalanceLabel()",
                    "if (PiDisplay)",
                    "switch (_balanceState)",
                    "RenderBalanceLabel does not test PiDisplay BEFORE its balance-state switch",
                    fail, ref checks);
                Require(store, "StoreStrings.Get(StoreStrings.KeyPiHeaderNotice)",
                    "the Pi header notice never replaces the SKR wallet chip", fail, ref checks);

                // ...and the SKR chip itself is still there for the SKR skin.
                //
                // ⚠ RE-POINTED 2026-09-03 (WO-1334 owner ruling), AND THIS IS A STRENGTHENING, NOT
                // A RELAXATION - read the whole note before touching it.
                //
                // This law used to require the SOURCE LITERAL:
                //         _wallet.NetworkLabel + "  SKR"
                // which was the old chip's identity line, e.g. `GHKK...sfkC  Devnet  SKR`. The owner
                // then ruled the connected chip is one line and NOTHING else - no address,
                // no "Your Wallet", no "Ready" pill - so that exact expression cannot be written in
                // this file any more without rendering a string she ruled out. The two SKR-skin
                // facts the law was actually protecting are both still true and are now pinned
                // where they LIVE, one apiece, instead of by one literal that happened to contain
                // both:
                //   (1) the chip still NAMES SKR, and it does so through the canon sentence
                //       storeBalanceValue - which the RequireCanon above already pins
                //       by name, in BOTH shipped copies;
                //   (2) the LIVE network word still reaches the chip.
                //
                // ⛔ WHY THIS IS STRICTER THAN WHAT IT REPLACES: the old Require was an unordered
                // substring test - it passed on a chip line sitting ANYWHERE in the file, including
                // one moved above the PiDisplay guard. Both replacements are ORDERED from the
                // method anchor, so an SKR chip that escapes the guard now fails, which is the
                // defect this whole section exists to prevent and which the retired literal could
                // not have caught.
                //
                // ⛔ AND THE DELETION-PASS IS STILL CLOSED. A seat that deletes the chip to make a
                // Pi test green fails on the guarded line being MISSING (RequireOrdered reports
                // exactly that), and a seat that deletes the network word fails the second one.
                RequireOrdered(store,
                    "private void RenderBalanceLabel()",
                    "if (PiDisplay)",
                    "StoreStrings.Format(StoreStrings.KeyBalanceValue",
                    "the SKR wallet chip was DELETED rather than guarded - that breaks the SKR skin. " +
                    "The connected chip must still render storeBalanceValue ('Balance: {0} SKR') and the " +
                    "PiDisplay test must still precede it", fail, ref checks);
                RequireOrdered(store,
                    "private void RenderBalanceLabel()",
                    "if (PiDisplay)",
                    "_wallet.NetworkLabel.ToUpperInvariant()",
                    "the LIVE network word was DELETED from the SKR chip rather than guarded. On " +
                    "devnet the SKR is free and a purchase settles for nothing, so this is a " +
                    "money-safety signal, not polish - and it must stay behind the Pi guard, " +
                    "because a Pi player has no Solana network to be told about", fail, ref checks);
                Require(store, "_wallet.Network != WalletNetwork.Mainnet",
                    "the network word is no longer conditioned on the LIVE network. Silence must " +
                    "mean mainnet and the WORD must mean 'these tokens are free' - an unconditional " +
                    "label is the baked network-frame.png plate all over again, which printed " +
                    "'Mainnet' over a DEVNET session", fail, ref checks);

                // The async read that produces it.
                RequireOrdered(store,
                    "private async UniTaskVoid RefreshWalletMirror()",
                    "if (PiDisplay)",
                    "WalletEndpoints.SkrMint(_wallet.Network)",
                    "RefreshWalletMirror still reads the SKR mint under the Pi skin", fail, ref checks);

                // The balance-after promise ("Wallet after: {0} SKR").
                Require(store, "if (!PiDisplay && roomForBalance && _balanceState == BalanceState.Known",
                    "the balance-after preview is not excluded by name under the Pi skin", fail, ref checks);

                // The Solana network marker.
                Require(store, "if (!PiDisplay && roomForNetwork && _wallet != null && _wallet.Network == WalletNetwork.Devnet)",
                    "the DEVNET marker is still drawn under the Pi skin", fail, ref checks);
                Require(store, "DEVNET - TEST TOKEN",
                    "the DEVNET marker was DELETED rather than guarded - that breaks the SKR skin", fail, ref checks);

                // The Solana connect door.
                Require(store, "if (walletIsTheBlocker && PiDisplay)",
                    "a Pi player can still be sent into the Solana wallet-connect flow", fail, ref checks);
                // ⚠ RE-PINNED 2026-09-04 (WO-1386), TWICE IN ONE EVENING - read both rulings.
                //   23:12 - "nothing should be guest buyable on a crypto account otherwise we can
                //           never persist change" -> Solana requires a wallet at every price.
                //   later - "mark anything for Pi as same logic based on USD" -> Pi is the SAME:
                //           no guest tier, wallet at every price, USD stays the one authority.
                // So the Pi plate can no longer say "Packs over $4.99 are not on sale in Pi yet"
                // - that names a guest tier Pi does not have. The two pins this block REPLACES
                // (`StoreStrings.KeyPiWalletGate` + `PurchaseGate.FormatUsd(WalletRequiredAboveUsd)`
                // in PackStore) pinned exactly that stale sentence, and a pin that keeps a lie on
                // screen is not a pin. What still holds under the Pi skin, each pinned below:
                //   (1) the Pi plate SAYS "wallet" and names any-price (no {0}: there is no
                //       threshold left to format) - the reverse of the pin that stood here for
                //       one hour;
                //   (2) it is Pi-worded and DISTINCT from the Solana connect sentence (two
                //       audiences, two plates), ASCII, SKR-free;
                //   (3) the plate is what PackStore renders under PiDisplay, not the stale key;
                //   (4) the WO-1386 shortfall route into the SOLANA connect door still stands down
                //       on PiDisplay BEFORE it asks PurchaseGate - a Pi player cannot complete a
                //       _wallet.Connect() handshake, so the plate is the refusal, not the button.
                // PROVEN RED (one line each): (1) drop "wallet" from PiWalletRequiredSentence;
                // (2) set PiWalletRequiredSentence to the localized Solana wallet sentence;
                // (3) restore `StoreStrings.Format(StoreStrings.KeyPiWalletGate, ...)` in the plate;
                // (4) delete the `if (PiDisplay)` block in RouteGuestShortfallToWalletConnect.
                string piGateCopy = StoreStrings.PiWalletRequiredSentence;
                checks++;
                if (string.IsNullOrWhiteSpace(piGateCopy) || piGateCopy.IndexOf("wallet", StringComparison.OrdinalIgnoreCase) < 0)
                    fail.Add("the Pi wallet plate no longer says 'wallet' - Pi has no guest tier (owner 2026-09-04: " +
                             "'mark anything for Pi as same logic based on USD'), so the remedy must be NAMED: " + piGateCopy);
                checks++;
                if (!string.IsNullOrEmpty(piGateCopy) && piGateCopy.IndexOf("{0}", StringComparison.Ordinal) >= 0)
                    fail.Add("the Pi wallet plate formats {0} - there is no $4.99 guest tier on Pi to name any more");
                checks++;
                if (!string.IsNullOrEmpty(piGateCopy) && piGateCopy.IndexOf("Pi", StringComparison.Ordinal) < 0)
                    fail.Add("the Pi wallet plate is not Pi-worded (WO-1323 kept the Pi audience its own words)");
                checks++;
                foreach (char c in piGateCopy ?? "")
                    if (c > 127) { fail.Add("the Pi wallet plate is not ASCII-clean"); break; }
                checks++;
                if (!string.IsNullOrEmpty(piGateCopy) && piGateCopy.IndexOf("SKR", StringComparison.OrdinalIgnoreCase) >= 0)
                    fail.Add("the Pi wallet plate names SKR");
                checks++;
                if (string.Equals(StoreBuyText.WalletRequiredCrypto.Resolve(), piGateCopy, StringComparison.Ordinal))
                    fail.Add("the Solana connect sentence and the Pi wallet plate are the SAME sentence - " +
                             "two audiences, two plates (WO-1386 / WO-1323)");
                RequireOrdered(store,
                    "if (walletIsTheBlocker && PiDisplay)",
                    "StoreStrings.PiWalletRequired()",
                    "FlowTrace.Step(\"Store\", $\"BuildSpotlightCta '{pack.Sku}': wallet-rule refusal, PI wording",
                    "the Pi plate no longer renders StoreStrings.PiWalletRequired() - it would be back on the " +
                    "stale storePiWalletGate row that names a $4.99 guest tier Pi no longer has", fail, ref checks);
                checks++;
                if (store.IndexOf("StoreStrings.Format(StoreStrings.KeyPiWalletGate", StringComparison.Ordinal) >= 0)
                    fail.Add("PackStore formats the STALE storePiWalletGate row again - it says 'Packs over $4.99 are " +
                             "not on sale in Pi yet', which the 2026-09-04 ruling made false");
                RequireOrdered(store,
                    "private void RouteGuestShortfallToWalletConnect()",
                    "if (PiDisplay)",
                    "PurchaseGate.WalletIsTheBlocker(offer.Pack)",
                    "the WO-1386 shortfall route can send a Pi player into the Solana wallet-connect door - " +
                    "the PiDisplay stand-down must precede the wallet predicate", fail, ref checks);

                // No Pi rail -> no Buy control, and NO fallback to the Solana path.
                Require(store, "if (PiDisplay && !PiRailOwnsTheStore)",
                    "a Pi-skinned session with no Pi rail can still fall through to the Solana Buy path",
                    fail, ref checks);
                Require(store, "StoreStrings.KeyPiRailUnavailable",
                    "there is no worded refusal for a Pi session with no Pi rail", fail, ref checks);

                // The SKR price label survives, and is only reached when PiDisplay is false.
                RequireOrdered(store,
                    "private string StorePriceMajor(PackDef pack)",
                    "if (PiDisplay)",
                    "return pack != null ? pack.AmountLabel(_defaultCurrency) : string.Empty;",
                    "StorePriceMajor can still reach the SKR amount label under the Pi skin", fail, ref checks);
                RequireOrdered(store,
                    "private string StorePriceMinor(PackDef pack)",
                    "if (PiDisplay)",
                    "return pack != null ? pack.UsdApprox() : string.Empty;",
                    "StorePriceMinor is not Pi-aware", fail, ref checks);

                // The empty Pi shelf is SHOWN, not papered over.
                Require(store, "BuildPiShelfNoticeIfNothingIsBuyable();",
                    "the empty-Pi-shelf notice is never called from Render", fail, ref checks);
                Require(store, "StoreStrings.KeyPiShelfEmpty",
                    "there is no sentence for a shelf on which nothing is Pi-purchasable", fail, ref checks);

                // =============================================================
                //  4. THE CLIENT NEVER PRICES ANYTHING
                // =============================================================
                Require(store, "RefreshPiDisplayPrices();",
                    "the store never asks the server for the shelf's Pi figures", fail, ref checks);
                Require(store, "PaymentProviders.Current as IDisplayPriceRefresher",
                    "the Pi display-price refresh no longer goes through the provider seam", fail, ref checks);
                if (store.IndexOf("UnityWebRequest", StringComparison.Ordinal) >= 0)
                    fail.Add("PackStore opened its own HTTP client - the ONE Pi endpoint client is " +
                             "PiPaymentEndpoints and there must never be a second");
                checks++;

                Require(pi, "PiPaymentEndpoints.RequestQuoteAsync(sku, uid)",
                    "the Pi display refresh does not use the existing /api/pi/quote client", fail, ref checks);
                Require(pi, "if (_displayQuotes.Remove(sku)) changed = true;",
                    "a refused quote no longer CLEARS the cached shelf figure - the store would keep drawing " +
                    "a price the server just refused", fail, ref checks);
                Require(pi, "DisplayQuoteTtlSeconds",
                    "the shelf's Pi figure no longer expires, so a stale rate can sit on the shelf",
                    fail, ref checks);
                if (pi.IndexOf("UnityWebRequest", StringComparison.Ordinal) >= 0)
                    fail.Add("PiBrowserPaymentProvider opened a second HTTP client");
                checks++;
                // ⛔ The display refresh must NEVER raise a Pi consent sheet: it runs on store OPEN,
                // unattended. Read the method's OWN body (definition -> the next section rule) rather
                // than the whole file, so the legitimate purchase-time call cannot mask a new one here.
                checks++;
                string refreshBody = Between(pi,
                    "private async UniTaskVoid RefreshDisplayPricesAsync",
                    "// -----------------------------------------------------------------");
                if (refreshBody == null)
                    fail.Add("RefreshDisplayPricesAsync is gone - the shelf has no server price source");
                else if (refreshBody.IndexOf("EnsurePaymentsScope", StringComparison.Ordinal) >= 0)
                    fail.Add("the display-price refresh asks for the Pi payments scope - that puts a consent " +
                             "sheet in front of a player who only browsed");
                else if (refreshBody.IndexOf("PiPaymentEndpoints.RequestQuoteAsync", StringComparison.Ordinal) < 0)
                    fail.Add("the display-price refresh no longer asks the server for the price");

                // =============================================================
                //  5. NO AUTHORED PI PRICE, AND NO FLIPPED SHELF FLAG
                // =============================================================
                foreach (string packsPath in new[] { packsAPath, packsBPath })
                {
                    // packs.json is an OBJECT whose "packs" member holds the array -
                    // {"version":..,"packs":[...]}. Parsing the file as a JArray threw
                    // JsonReaderException("Current JsonReader item is not an array: StartObject")
                    // on the suite's FIRST real run, which Guard.Try caught and reported as an
                    // unlabelled failure - the whole suite never reached its OK/FAIL marker, so it
                    // was neither green nor legibly red. Read the member, and refuse loudly if the
                    // shape is not what we expect rather than throwing again.
                    var packsDoc = JObject.Parse(File.ReadAllText(packsPath));
                    var packs = packsDoc["packs"] as JArray;
                    if (packs == null)
                    {
                        fail.Add("packs.json at '" + packsPath + "' has no 'packs' array - the shape " +
                                 "this suite reads changed, and a shape change here silently stops " +
                                 "every assertion below from running");
                        continue;
                    }
                    bool sawHearthSpark = false;
                    foreach (var packToken in packs)
                    {
                        var pack = packToken as JObject;
                        if (pack == null) continue;
                        string sku = (string)pack["sku"];

                        var pricing = pack["pricing"] as JObject;
                        if (pricing != null && pricing["pi"] != null)
                            fail.Add("packs.json authored a static 'pi' price on '" + sku +
                                     "' - the Pi amount is a live server derivation (CoinGecko low_24h), " +
                                     "never a constant that drifts the moment the rate moves");

                        if (string.Equals(sku, "hearth-spark", StringComparison.Ordinal))
                        {
                            sawHearthSpark = true;
                            var visible = pack["storeVisible"];
                            // ⛔ PRESENT **AND** FALSE. The old assertion passed when the field was
                            // ABSENT, and absence is not the ruling - PackDef's own default decides
                            // what a missing flag means, so a row that quietly lost the field would
                            // have read as green here while the pack walked onto the shelf. WO-1323's
                            // spotlight makes this pack reachable WITHOUT the flag, so the flag has
                            // to be pinned harder, not softer.
                            if (visible == null)
                                fail.Add("hearth-spark has NO storeVisible field in " +
                                         Path.GetFileName(packsPath) + " - WO-1069 shelved it explicitly, " +
                                         "and the WO-1323 spotlight is built on that flag staying FALSE");
                            else if ((bool)visible)
                                fail.Add("hearth-spark storeVisible was flipped TRUE - WO-1069 shelved it as " +
                                         "dominated by starters-hand, and an empty Pi shelf must not be " +
                                         "resolved by reversing a pricing ruling");
                        }
                    }
                    if (!sawHearthSpark)
                        fail.Add("hearth-spark is absent from " + Path.GetFileName(packsPath) +
                                 " - it is the only Pi-quotable sku");
                    // One assertion per FILE, not per pack: 28 rows must not inflate the count and
                    // let the source half of this suite rot behind a big number.
                    checks += 2;
                }

                // =============================================================
                //  6. THE PI SPOTLIGHT (owner ruling 2026-09-02)
                // -------------------------------------------------------------
                //  She was shown that a Pi player sees 28 packs priced in USD with NO BUY
                //  CONTROL ANYWHERE, and chose: surface the ONE Pi-priced pack through the
                //  EXISTING StoreFocusRequest latch, WITHOUT touching storeVisible. Proving
                //  quote -> approve -> complete -> grant on one sku is worth more than a full
                //  shelf where one rail bug hits all 28. This section pins that shape.
                // =============================================================

                // (a) ONE SKU, ONE COPY OF ITS NAME. A second literal is the duplicated state that
                // drifts the day the rail widens - and it is also how a "spotlight everything"
                // regression would enter without anyone noticing.
                checks++;
                if (CountOf(store, "\"hearth-spark\"") != 1)
                    fail.Add("PackStore.cs holds " + CountOf(store, "\"hearth-spark\"") + " copies of the " +
                             "\"hearth-spark\" literal - there must be exactly ONE (PiEnabledSkuHint). A second " +
                             "copy is a second opinion about which sku the Pi rail sells");

                Require(store, "private static PackDef ResolvePiSpotlightPack()",
                    "the single oracle that decides the Pi spotlight is gone or renamed - the latch and the " +
                    "empty-shelf notice would then answer 'can a Pi player buy anything' separately",
                    fail, ref checks);
                Require(store, "var pack = PackCatalog.Find(PiEnabledSkuHint);",
                    "the spotlight no longer resolves its pack from the one Pi sku constant", fail, ref checks);

                // (b) THE SPOTLIGHT IS EARNED BY THE REAL GATES, not by a display flag. These are the
                // SAME two questions BuildSpotlightCta asks before it builds a Buy control, so a
                // spotlighted pack is always a genuinely buyable one.
                RequireOrdered(store,
                    "private static PackDef ResolvePiSpotlightPack()",
                    "if (!PiCanSell(pack)) return null;",
                    "return pack;",
                    "the Pi spotlight no longer asks the RAIL whether it will sell this sku", fail, ref checks);
                RequireOrdered(store,
                    "private static PackDef ResolvePiSpotlightPack()",
                    "if (!PurchaseGate.CanBuy(pack, out _)) return null;",
                    "return pack;",
                    "the Pi spotlight no longer asks PurchaseGate - it could spotlight a pack the gate " +
                    "(kill switch, feature flag, wallet ceiling) refuses, which is a Buy face over a refusal",
                    fail, ref checks);

                // (c) THE SKR SKIN IS UNCHANGED, and the proof is the FIRST line of the oracle: with
                // PiDisplay false there is no spotlight, no latch write and no notice suppression, so
                // every SKR-skin open behaves exactly as before.
                RequireOrdered(store,
                    "private static PackDef ResolvePiSpotlightPack()",
                    "if (!PiDisplay) return null;",
                    "PackCatalog.Find(PiEnabledSkuHint)",
                    "the Pi spotlight is not gated on PiDisplay FIRST - it would reach into the SKR skin's " +
                    "store open, which this work order must leave byte-for-byte alone", fail, ref checks);

                // (d) A PACK ALREADY ON THE SHELF NEEDS NO RESCUE. This is what makes widening the Pi
                // rail later a server-side change with no edit here.
                Require(store, "if (PackCatalog.IsOnBrowsableShelf(pack)) return null;",
                    "the spotlight would fire for a pack the shelf already shows - the spotlight exists only " +
                    "to reach a pack the browsable shelf cannot", fail, ref checks);

                // (e) IT USES THE EXISTING LATCH. No second focus mechanism, and the latch is written
                // BEFORE the Render that consumes it.
                Require(store, "StoreFocusRequest.RequestFocusSku(pack.Sku);",
                    "the Pi spotlight does not go through the existing StoreFocusRequest latch", fail, ref checks);
                RequireOrdered(store,
                    "AdoptLiveWalletIfBetter(\"open\");",
                    "LatchPiSpotlightOnOpen();",
                    "Render();",
                    "the Pi spotlight is not latched BEFORE the first Render - Render is where " +
                    "ResolveFocusSku consumes the latch, so a latch written after it is never honoured",
                    fail, ref checks);

                // (f) A DEFAULT NEVER BEATS AN EXPLICIT REQUEST. The Manage 'Buy builder' route and a
                // shortfall remedy both name a sku the player just asked about.
                Require(store, "if (StoreFocusRequest.HasPending)",
                    "the Pi spotlight would overwrite a caller's own focus request", fail, ref checks);
                RequireOrdered(store,
                    "private void LatchPiSpotlightOnOpen()",
                    "_pendingShortfallMissing > 0",
                    "StoreFocusRequest.RequestFocusSku(pack.Sku);",
                    "the Pi spotlight would overwrite a pending shortfall remedy", fail, ref checks);

                // (g) THE EMPTY-SHELF NOTICE STANDS DOWN WHEN THE SPOTLIGHT IS LIVE. Otherwise the
                // store says 'nothing here can be bought with Pi' beside a live Buy control.
                RequireOrdered(store,
                    "private void BuildPiShelfNoticeIfNothingIsBuyable()",
                    "var spotlight = ResolvePiSpotlightPack();",
                    "StoreStrings.KeyPiShelfEmpty",
                    "the empty-Pi-shelf notice is still drawn when the spotlight carries a buyable pack - " +
                    "the store would contradict itself on one screen", fail, ref checks);

                // (h) AND THE SPOTLIGHT SKU IS ACTUALLY QUOTED. The display refresh walks the
                // BROWSABLE shelf, which by construction excludes the one Pi-quotable sku; without
                // this line the spotlight shows 'Priced in Pi at checkout' forever while
                // /api/pi/quote sits live and answering.
                RequireOrdered(store,
                    "private void RefreshPiDisplayPrices()",
                    "skus.Add(spotlight.Sku);",
                    "refresher.RefreshDisplayPrices(skus,",
                    "the spotlight sku is never added to the Pi display-price request, so the one pack a " +
                    "Pi player can buy would never show a Pi price", fail, ref checks);

                // (i) THE RAIL STILL SELLS EXACTLY ONE SKU, and the CLIENT cannot widen it. The
                // provider filters both its quote loop and its gate to its own EnabledSku.
                Require(pi, "public const string EnabledSku = \"hearth-spark\";",
                    "the Pi rail's one-sku allowlist moved or widened - widening is a reviewed server+client " +
                    "change, never a side effect of a display work order", fail, ref checks);
                Require(pi, "if (!string.Equals(sku, EnabledSku, StringComparison.Ordinal)) continue;",
                    "the Pi display refresh no longer filters to the one enabled sku", fail, ref checks);

                if (checks < 45)
                    fail.Add("only " + checks + " assertions ran - the suite degenerated into a hollow pass");
            }
            catch (Exception e)
            {
                reason = "threw " + e.GetType().Name + ": " + e.Message;
                return false;
            }

            if (fail.Count > 0)
            {
                reason = fail.Count + " failure(s): " + string.Join(" | ", fail);
                return false;
            }

            reason = checks + " assertions: Pi skin reaches no SKR string in the store surface; " +
                     "prices come from /api/pi/quote and expire; SKR skin paths and copy intact; " +
                     "no authored 'pi' price and no flipped storeVisible; the WO-1323 spotlight " +
                     "makes exactly ONE sku buyable under Pi through the existing focus latch, is " +
                     "quoted by the server, and stands the empty-shelf notice down.";
            return true;
        }

        // -----------------------------------------------------------------
        //  helpers
        // -----------------------------------------------------------------

        /// <summary>How many times <paramref name="needle"/> occurs in <paramref name="body"/>.
        /// Used where the COUNT is the assertion: one sku literal, not two.</summary>
        private static int CountOf(string body, string needle)
        {
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(needle)) return 0;
            int count = 0;
            int at = body.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = body.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }
            return count;
        }

        private static void Require(string body, string needle, string why, List<string> fail, ref int checks)
        {
            checks++;
            if (body.IndexOf(needle, StringComparison.Ordinal) < 0) fail.Add(why + " (missing: " + needle + ")");
        }

        private static void RequireCanon(JObject canon, string key, string needle, List<string> fail, ref int checks)
        {
            checks++;
            string value = (string)canon[key];
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf(needle, StringComparison.Ordinal) < 0)
                fail.Add("canon key " + key + " no longer reads '" + needle +
                         "' - the SKR skin's own copy must be untouched by this work order");
        }

        /// <summary>
        /// Proves <paramref name="guard"/> appears between <paramref name="from"/> and
        /// <paramref name="guarded"/>. ORDER is the assertion: a PiDisplay test that sits AFTER the
        /// SKR line it is meant to prevent is not a guard, and a plain "both strings exist" check
        /// cannot tell the two apart.
        /// </summary>
        private static void RequireOrdered(string body, string from, string guard, string guarded,
                                           string why, List<string> fail, ref int checks)
        {
            checks++;
            int start = body.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) { fail.Add(why + " (anchor missing: " + from + ")"); return; }
            int guardAt = body.IndexOf(guard, start, StringComparison.Ordinal);
            int guardedAt = body.IndexOf(guarded, start, StringComparison.Ordinal);
            if (guardedAt < 0) { fail.Add(why + " (guarded line missing: " + guarded + ")"); return; }
            if (guardAt < 0 || guardAt > guardedAt) fail.Add(why + " (guard '" + guard + "' does not precede it)");
        }

        /// <summary>
        /// The text from <paramref name="from"/> up to the next <paramref name="until"/>, or null if
        /// the opening anchor is gone. Used to read ONE method's body so a legitimate call elsewhere
        /// in the file cannot mask (or manufacture) a finding inside it.
        /// </summary>
        private static string Between(string body, string from, string until)
        {
            int start = body.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = body.IndexOf(until, start, StringComparison.Ordinal);
            return end < 0 ? body.Substring(start) : body.Substring(start, end - start);
        }
    }
}
