// =============================================================================
// PurchaseGate — WO-1121. The honest Buy gate + the idempotent-grant ledger.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// WO-1121 sec.0: "A live store that cannot take money, or takes money and grants
// nothing, is worse than no store." Most of that ticket is a SHIP CHECKLIST -
// a mainnet decision, an SKR mint, a settled test transfer - and none of those
// are code. This file is the part that IS code, and it is exactly three things:
//
//   1. ONE PLACE that answers "may this build take money for THIS pack?", with a
//      PLAYER-READABLE reason attached. Today the answer is no, and the reason is
//      not "the flag is off" - it is that the flag is off BECAUSE the rails
//      underneath it are not finished. A dead Buy button with no explanation is the
//      failure mode WO-1121 sec.3.5 names; so is a silent no-op on a broke-case
//      Finish Now.
//
//   2. THE WALLET RULE (owner ruling 2026-08-21, made CHANNEL-AWARE by WO-1386 on
//      2026-09-04). On the Solana dApp Store rail EVERY purchase needs a connected,
//      ATTESTED wallet - owner, verbatim: "nothing should be guest buyable on a
//      crypto account otherwise we can never persist change" - and Pi is the same
//      ("mark anything for Pi as same logic based on USD"), as is an Unknown channel
//      (fail-closed). ONLY Google Play keeps the 2026-08-21 threshold: a pack priced
//      ABOVE $4.99 needs the wallet, $4.99 and under stays guest-buyable, because
//      Play's account is the durable key. See WalletRequiredAboveUsd and
//      RequiresWallet(double, PaymentChannel); the reasoning is about DURABILITY of
//      the entitlement, not about trust.
//
//   3. AN IDEMPOTENT GRANT LEDGER keyed by paymentId, so a retried or duplicated
//      settlement can NEVER grant a pack twice. This is the half of "charged and
//      granted" that has no owner today: the charge is the wallet's, the contents
//      are PackStore's, and nothing sat between them remembering what had already
//      been paid out.
//
// ⚠ WHY THIS FILE LIVES IN DeNelle.Wallet AND NOT IN DeNelle.Village (moved
// 2026-08-21, WO-1121). It was authored earlier the same day in
// Village/Monetization, which reads fine until you ask the only question that
// matters for a money gate: CAN THE CODE THAT CHARGES THE PLAYER CALL IT? It could
// not. The charge happens in PackStore.Purchase (DeNelle.Wallet), and the asmdef
// dependency runs Village -> Wallet, one way (read the .asmdef - CLAUDE.md sec.5).
// A gate the payment path cannot reach is a UI gate, and a UI gate is bypassed by
// any other call path - which is precisely what the owner's ruling forbids. Moving
// it here costs one namespace and buys the guarantee: BOTH the card builder and the
// Purchase() entry now call the same CanBuy. Village callers still reach it, because
// Village references Wallet.
//
// ⛔ WHAT THIS FILE DELIBERATELY DOES NOT DO. It does NOT flip
// FeatureFlags.RealmStorePurchase, and no future edit here should. That default is
// a PO decision with three named preconditions (a resolvable mint, a lifted mainnet
// block with a settling path behind it, and the closed stub-wallet free-grant hole)
// written out in FeatureFlags.cs. This gate READS the flag and reports the state
// honestly; deciding the state is not its job. See ChecklistReport() - it prints
// exactly which preconditions are still red, so the owner can judge from data
// rather than from a summary.
// =============================================================================
using System;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Payments;   // WO-1386 - PaymentChannel / PaymentChannelResolver, read, never re-derived
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Wallet
{
    /// <summary>
    /// The single authority on whether this build may take money for a given pack, and the memory
    /// that stops one payment from granting twice.
    /// </summary>
    public static class PurchaseGate
    {
        /// <summary>
        /// MON-1147 devnet launch allowlist. The backend currently has one independently pinned
        /// SKU/price contract; charging any other row would create a transaction the verifier must
        /// reject after the player paid. Expand client and server allowlists in the same reviewed WO.
        /// </summary>
        public const string DevnetCanarySku = "hearth-spark";
        public const string MainnetCanarySku = "mainnet-wood-canary";
        private const string GrantedPrefix = "purchase.granted.";
        private const string GrantedIndex = "purchase.granted.index";

        /// <summary>
        /// How many recent paymentIds the ledger remembers. Bounded because PlayerPrefs is not a
        /// database: an unbounded key set would grow forever on a whale's device. 64 is far past any
        /// realistic retry horizon - a duplicate settlement arrives seconds later, not 64 purchases
        /// later - and the ledger is defence against RETRY, not an entitlement record. The
        /// entitlement record is the save (and, eventually, the server).
        /// </summary>
        private const int LedgerCapacity = 64;

        // =====================================================================
        //  The wallet rule (owner ruling 2026-08-21)
        // =====================================================================

        /// <summary>
        /// Above this USD price a purchase requires a connected, provider-ATTESTED wallet. At or
        /// below it, a guest may buy.
        ///
        /// <para>THIS IS ABOUT DURABILITY, NOT TRUST. A guest's save key is
        /// <c>guest-local-&lt;sha256(deviceId)&gt;</c> (GameStateService.EnsureAccount) - derived from
        /// the device, with no proven restore path after a reinstall or a new phone. At $4.99 a lost
        /// entitlement is an annoyance we can eat. At $49.99 it is a chargeback on a LIVE dApp Store
        /// listing, and the player is right to file it. A connected wallet is a durable key that
        /// survives both, which is why the line sits exactly where the old early-access ceiling
        /// used to: everything that was already guest-buyable stays guest-buyable.</para>
        ///
        /// <para>⛔ THE THRESHOLD LIVES HERE AND NOWHERE ELSE. It is deliberately NOT a
        /// <c>requiresWallet</c> field on the pack: that would be a second copy of a decision the
        /// price already makes, and the two would drift the first time a pack is repriced (the same
        /// duplicated-state failure as the stale WO-number block and the retired dependency table,
        /// CLAUDE.md sec.2/sec.5). The player-facing sentence formats this same number, so the copy
        /// cannot drift from the rule either.</para>
        ///
        /// <para>⚠ WO-1386 (owner ruling 2026-09-04): THIS CONSTANT NO LONGER GATES THE SOLANA RAIL.
        /// On <see cref="PaymentChannel.SolanaDappStore"/> <see cref="RequiresWallet(double, PaymentChannel)"/>
        /// is TRUE at every price - owner, verbatim: <i>"nothing should be guest buyable on a crypto
        /// account otherwise we can never persist change"</i>. The number is kept as the documented
        /// HISTORY of the 2026-08-21 ruling and as the LIVE threshold for Google Play ONLY (its
        /// account is the durable key, so its guest tier stays; Pi and Unknown take the strict rule
        /// too - owner, same evening: "mark anything for Pi as same logic based on USD"). It is still the one
        /// authority for that threshold - the channel switch lives in the predicate, not in a
        /// second number and not in a per-pack field.</para>
        /// </summary>
        public const double WalletRequiredAboveUsd = 4.99d;

        /// <summary>
        /// True when a purchase at <paramref name="priceUsd"/> needs an attested wallet on the
        /// channel this artifact resolved (<see cref="PaymentChannelResolver.Current"/>). Delegates
        /// to <see cref="RequiresWallet(double, PaymentChannel)"/> - ONE predicate, two entry points.
        /// </summary>
        public static bool RequiresWallet(double priceUsd) =>
            RequiresWallet(priceUsd, PaymentChannelResolver.Current);

        /// <summary>
        /// THE wallet predicate, channel-aware (WO-1386, owner ruling 2026-09-04).
        /// <list type="bullet">
        /// <item><see cref="PaymentChannel.SolanaDappStore"/>: ALWAYS true. A guest save on the
        /// crypto rail is a device-derived key with no restore path, and the owner ruled that no
        /// price is small enough to accept losing: <i>"nothing should be guest buyable on a crypto
        /// account otherwise we can never persist change"</i>.</item>
        /// <item><see cref="PaymentChannel.PiBrowser"/>: ALWAYS true. Owner, same evening
        /// (2026-09-04), verbatim: <i>"mark anything for Pi as same logic based on USD"</i>.</item>
        /// <item><see cref="PaymentChannel.Unknown"/>: ALWAYS true - FAIL-CLOSED. A channel this
        /// artifact cannot name gets the strict rule, never the lenient one.</item>
        /// <item><see cref="PaymentChannel.GooglePlay"/> ONLY: the 2026-08-21 threshold - true only
        /// ABOVE <see cref="WalletRequiredAboveUsd"/>, because Play's account is the durable key. A
        /// tiny epsilon guards the binary-float representation of 4.99 so an exactly-$4.99 pack can
        /// never tip over the line by a rounding hair and refuse a purchase that ruling allows.</item>
        /// </list>
        /// The USD price is the one authority on every channel - there is no per-rail price logic,
        /// only a per-rail answer to "is there a guest tier at all".
        /// </summary>
        public static bool RequiresWallet(double priceUsd, PaymentChannel channel)
        {
            if (channel != PaymentChannel.GooglePlay) return true;
            return priceUsd > WalletRequiredAboveUsd + 0.0001d;
        }

        /// <summary>
        /// The player-facing refusal for the wallet rule on <paramref name="channel"/> - ONE place,
        /// so the card banner, the charge path and the shortfall door can never word it three ways.
        /// <para>Every channel but Google Play (Solana, Pi, Unknown - owner 2026-09-04: <i>"mark
        /// anything for Pi as same logic based on USD"</i>): the WO-1386 sentence, which says WHY in
        /// the owner's sense (the purchase is yours on every device) and never a bare "wallet
        /// required". Google Play: the threshold sentence, with {0} formatted from the constant so it
        /// cannot drift.</para>
        /// </summary>
        public static string WalletRefusalSentence(PaymentChannel channel) =>
            channel != PaymentChannel.GooglePlay
                ? StoreBuyText.WalletRequiredCrypto.Resolve()
                : StoreBuyText.WalletRequiredFor(FormatUsd(WalletRequiredAboveUsd));

        /// <summary>
        /// True when this device has a REAL, provider-attested wallet keying the save - the same
        /// test cloud sync uses, read at source rather than re-derived here.
        /// <para>Not "a wallet object exists": an unattested or wallet-SHAPED string is exactly what
        /// GameStateService's allowlist was written to reject, and accepting one here would hand a
        /// $49.99 entitlement to a key the backend will 401.</para>
        /// </summary>
        public static bool HasDurableIdentity =>
            (GameStateService.Instance?.HasAttestedWalletIdentity ?? false)
            || HasVerifiedPiIdentity;

        /// <summary>
        /// WO-1700 (owner on the Seeker in Pi Browser, 2026-09-10): on the Pi channel the durable
        /// key is the SERVER-VERIFIED Pi uid, not a Solana pubkey. WO-1386 made every Pi price
        /// require an attested identity nine hours after WO-1318's felt-test pass, and the only
        /// identity test it consulted was <see cref="GameStateService.HasAttestedWalletIdentity"/>,
        /// which needs a base58 pubkey a signing wallet vouched for - a shape a Pi uid can never
        /// take. Captured 21:46:36Z: "Signed in as samanthadenelle" then, on every card,
        /// "this save has NO attested wallet identity". Nothing had been buyable on Pi since 09-04.
        /// <para><see cref="DeNelle.Core.Platform.PiSignInController.IsSignedIn"/> is true ONLY after
        /// <c>/api/pi/verify</c> validated the access token against api.minepi.com and returned the
        /// uid (PiSignInController.cs, VerifyWithBackend) - the same bar the wallet path sets. Off
        /// WebGL the controller is the inert stub and this is false, so the SKR / Play rails are
        /// byte-identical. Channel-gated so a Pi sign-in can never unlock a Solana-rail price.</para>
        /// </summary>
        public static bool HasVerifiedPiIdentity =>
            PaymentChannelResolver.Current == PaymentChannel.PiBrowser
            && DeNelle.Core.Platform.PiSignInController.IsSignedIn;

        // =====================================================================
        //  The gate
        // =====================================================================

        /// <summary>
        /// May this build present a working Buy CTA at all? Returns false with a PLAYER-READABLE
        /// <paramref name="reason"/> - never null, never "flag off", never blank. A caller that
        /// gets false shows the reason instead of a dead button.
        /// <para>This is the BUILD-WIDE half only. Anything that names a specific pack must call
        /// <see cref="CanBuy(PackDef, out string)"/>, which adds the price-gated wallet rule.</para>
        /// </summary>
        public static bool CanBuy(out string reason)
        {
            // WO-1243 OPERATOR KILL SWITCH: store.
            // FIRST, before every other check, because a sealed store is the owner's
            // deliberate act and outranks a flag or a rail readiness question. Placed on
            // the BUILD-WIDE overload so it closes BOTH the CTA builder and the charge
            // path in one edit - CanBuy(pack, out reason) calls straight through here.
            // !! COURTESY HALF. The real seal is api/purchases/quote.js, which refuses to
            // ISSUE A QUOTE while `store` is closed - that is the pre-payment gate and it
            // binds a modified client too. (DO NOT: It is deliberately NOT on verify/fulfill/
            // reconcile: those run after the chain has settled, and sealing them would
            // take real money and then refuse to record it.)
            // Fail-OPEN when the toggle table is unreachable (owner ruling 2026-08-27).
            if (DeNelle.Core.Ops.MaintenanceCatalog.Refuses(
                    DeNelle.Core.Ops.MaintenanceArea.Store, "purchase-gate", out string storeSealedMsg))
            {
                reason = storeSealedMsg;
                return false;
            }

            if (!FeatureFlags.RealmStorePurchase)
            {
                reason = StoreBuyText.Closed.Resolve();
                FlowTrace.Once("Store", "buy-gated",
                    "PurchaseGate: Buy is CLOSED (ff.realmstorepurchase OFF). This is the shipping state - " +
                    "the payment rails underneath are not finished (see PurchaseGate.ChecklistReport). " +
                    "The store still browses; only the CTA refuses, and it refuses in words.");
                return false;
            }

            // Flag ON but the rails still incomplete: refuse anyway rather than take a tap we
            // cannot settle. The flag is a PO decision; this is a factual check of the rail, and a
            // factual check must be able to veto an optimistic flag.
            if (!SkrMintResolvable() && !string.Equals(PrimaryRail(), "USDC/SOL", StringComparison.Ordinal))
            {
                reason = StoreBuyText.RailNotReady.Resolve();
                FlowTrace.Fail("Store",
                    "PurchaseGate: Buy is ON but the default rail has NO RESOLVABLE MINT " +
                    "(WalletEndpoints.SkrMint is empty for this network). Refusing at the gate rather " +
                    "than letting a tap dead-end inside the wallet.");
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// May this build take money for THIS pack? The build-wide gate PLUS the owner's wallet rule
        /// (<see cref="WalletRequiredAboveUsd"/>).
        ///
        /// <para>⛔ EVERY PATH THAT CHARGES MUST CALL THIS, not just the button builder. A gate the
        /// UI alone enforces is bypassed by any other caller - a deep link, a shortfall offer, a
        /// future server-driven promo - and the whole point of the ruling is that the $49.99 tier
        /// cannot be bought onto a key that cannot survive a reinstall.</para>
        ///
        /// <para>The refusal is ACTIONABLE and never implies the player cannot buy at all: it names
        /// the wallet as the remedy and says what is still buyable without one.</para>
        /// </summary>
        public static bool CanBuy(PackDef pack, out string reason)
        {
            if (!CanBuy(out reason)) return false;

            if (pack == null)
            {
                // Not a player error, so it does not get a player sentence dressed as one - but it
                // must still refuse rather than fall through into a charge with no SKU.
                reason = StoreBuyText.RailNotReady.Resolve();
                FlowTrace.Fail("Store", "PurchaseGate.CanBuy(pack): pack is NULL - refusing. A charge with no SKU " +
                                        "could not be granted, refunded or supported.");
                return false;
            }

#if MAINNET_CANARY_TEST
            if (!string.Equals(pack.Sku, MainnetCanarySku, StringComparison.Ordinal))
            {
                reason = "This owner test build can purchase only Mainnet Verification.";
                return false;
            }
            if (string.IsNullOrEmpty(WalletRegistry.MainnetPurchaseRecipientAddress))
            {
                reason = "Mainnet verification is waiting for the approved treasury address.";
                FlowTrace.Fail("Store", "MON002 refused before wallet approval: no owner-approved Mainnet recipient is configured.");
                return false;
            }
#else
            if (WalletService.DefaultNetwork == WalletNetwork.Devnet &&
                !string.Equals(pack.Sku, DevnetCanarySku, StringComparison.Ordinal))
            {
                reason = "This pack is not in today's verified devnet canary. Hearth Spark is the active test purchase.";
                FlowTrace.Step("Store",
                    $"PurchaseGate: '{pack.Sku}' refused because the MON-1147 backend contract currently " +
                    $"authorizes only '{DevnetCanarySku}' on devnet.");
                return false;
            }
#endif

            double usd = pack.Pricing != null ? pack.Pricing.Usd : 0d;
            PaymentChannel channel = PaymentChannelResolver.Current;
            if (RequiresWallet(usd, channel) && !HasDurableIdentity)
            {
                reason = WalletRefusalSentence(channel);
                string why = channel != PaymentChannel.GooglePlay
                    ? $"the channel is {channel}, where NO price is guest-buyable (WO-1386, owner 2026-09-04)"
                    : $"it is above the ${WalletRequiredAboveUsd:0.00} guest ceiling (owner 2026-08-21)";
                FlowTrace.Warn("Store",
                    $"PurchaseGate: '{pack.Sku}' is ${usd:0.00} on channel {channel}; {why}, and this save has " +
                    "NO attested wallet identity (it is a guest/device key with no proven restore path). " +
                    "Refusing BEFORE any charge, with the connect-a-wallet remedy named. This is an owner " +
                    "ruling, not a rail failure.");
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// The right-rail BUTTON FACE for a pack the player may not currently buy - a short label,
        /// not a sentence. Returns "Connect Wallet" when the wallet rule is the blocker (an action
        /// the player can take right now) and "Coming soon" when the whole rail is closed.
        /// <para>Text, never colour: the owner is red/green colourblind, so the state must be
        /// readable from the words alone.</para>
        /// </summary>
        public static string BlockedCtaLabel(PackDef pack) =>
            WalletIsTheBlocker(pack)
                ? StoreBuyText.WalletRequiredCta.Resolve()
                : StoreBuyText.ComingSoon.Resolve();

        /// <summary>
        /// True when the ONLY thing standing between this player and this pack is a connected
        /// wallet - i.e. the refusal has a remedy the player can act on right now.
        /// <para>ONE predicate, asked by both the label and the shelf, so a card can never show
        /// "Connect Wallet" over a rail that is closed anyway (a promise we could not keep) or
        /// "Coming soon" over a pack that connecting would unlock (a door we hid).</para>
        /// </summary>
        public static bool WalletIsTheBlocker(PackDef pack) =>
            FeatureFlags.RealmStorePurchase &&
            pack != null && pack.Pricing != null &&
            RequiresWallet(pack.Pricing.Usd) && !HasDurableIdentity;

        /// <summary>Player-readable line for a shelf that is browsable but not buyable.</summary>
        public static string ClosedShelfLine() => StoreBuyText.ShelfClosed.Resolve();

        // =====================================================================
        //  Idempotent grant ledger
        // =====================================================================

        /// <summary>
        /// True when <paramref name="paymentId"/> has ALREADY been granted on this device. A caller
        /// that sees true must NOT grant again - it must report success (the player already has the
        /// goods) rather than pay out twice or claim a failure.
        /// </summary>
        public static bool WasGranted(string paymentId)
        {
            if (string.IsNullOrEmpty(paymentId)) return false;
            return PlayerPrefs.GetInt(GrantedPrefix + Slug(paymentId), 0) == 1;
        }

        /// <summary>
        /// Claims <paramref name="paymentId"/> for granting. Returns TRUE exactly once per payment:
        /// the FIRST caller wins and must proceed with the grant, every later caller gets false and
        /// must skip it.
        ///
        /// <para>THE CLAIM IS MADE BEFORE THE GRANT, ON PURPOSE. Claiming afterwards leaves a window
        /// in which a crash or a racing retry pays out twice, and a double grant is unrecoverable
        /// (you cannot take a pack back). Claiming first risks the opposite - a crash mid-grant
        /// leaves the payment marked with the goods undelivered - which is recoverable by support
        /// and is loudly logged by <see cref="ReportGrantFailed"/>. Between an unrecoverable error
        /// and a recoverable one, take the recoverable one.</para>
        /// </summary>
        public static bool TryClaimGrant(string paymentId)
        {
            if (string.IsNullOrEmpty(paymentId))
            {
                FlowTrace.Fail("Store",
                    "TryClaimGrant with an EMPTY paymentId - refusing. An unidentifiable payment cannot " +
                    "be made idempotent, so granting on it would be a double-grant waiting to happen.");
                return false;
            }

            string key = GrantedPrefix + Slug(paymentId);
            if (PlayerPrefs.GetInt(key, 0) == 1)
            {
                FlowTrace.Warn("Store",
                    $"DUPLICATE settlement for payment '{paymentId}' - already granted on this device. " +
                    "Skipping the grant. This is the retry path working, not an error.");
                return false;
            }

            PlayerPrefs.SetInt(key, 1);
            RememberInIndex(key);
            PlayerPrefs.Save();
            FlowTrace.Step("Store", $"grant CLAIMED for payment '{paymentId}'.");
            return true;
        }

        /// <summary>
        /// Call when the grant FAILED after the payment settled - the "charged + empty inventory"
        /// case (WO-1121 sec.1). Releases the claim so a retry can pay out, and logs LOUDLY: this
        /// is the one failure in the whole store that costs a real player real money, so it must
        /// never be a swallowed exception.
        /// </summary>
        public static void ReportGrantFailed(string paymentId, string what)
        {
            if (!string.IsNullOrEmpty(paymentId))
            {
                PlayerPrefs.DeleteKey(GrantedPrefix + Slug(paymentId));
                PlayerPrefs.Save();
            }
            FlowTrace.Fail("Store",
                $"⛔ PAID BUT NOT GRANTED - payment '{paymentId}' settled and '{what}' failed to deliver. " +
                "The idempotency claim has been RELEASED so a retry can complete it. This line is the " +
                "support trail: a player is out real money until it does.");
        }

        /// <summary>QA reset. Never called by gameplay.</summary>
        public static void ClearLedgerForTests()
        {
            string index = PlayerPrefs.GetString(GrantedIndex, "");
            if (!string.IsNullOrEmpty(index))
            {
                string[] keys = index.Split('|');
                for (int i = 0; i < keys.Length; i++)
                    if (!string.IsNullOrEmpty(keys[i])) PlayerPrefs.DeleteKey(keys[i]);
            }
            PlayerPrefs.DeleteKey(GrantedIndex);
            PlayerPrefs.Save();
        }

        // =====================================================================
        //  Checklist reporting (WO-1121 sec.2) — data, not a summary
        // =====================================================================

        /// <summary>
        /// One-shot boot report of every payment-rail precondition, each with its live value read
        /// AT SOURCE. It exists so the go-live decision is made from a capture rather than from
        /// somebody's recollection of what was true last week - the same reason CLAUDE.md sec.16
        /// insists a push is judged by a marker on a fresh log.
        /// </summary>
        public static string ChecklistReport()
        {
            string rail = PrimaryRail();
            bool mint = SkrMintResolvable();
            bool buy = FeatureFlags.RealmStorePurchase;
            PaymentChannel channel = PaymentChannelResolver.Current;
            bool cryptoRail = channel != PaymentChannel.GooglePlay;
            string walletRule = cryptoRail
                ? "EVERY price (WO-1386: channel is " + channel + ", nothing is guest-buyable)"
                : "above $" + WalletRequiredAboveUsd.ToString("0.00") + " (channel " + channel + ")";
            string guestCanBuy = cryptoRail
                ? "NOTHING is buyable until a wallet is connected"
                : "only <= $" + WalletRequiredAboveUsd.ToString("0.00") + " is buyable";

            return
                "PurchaseGate checklist (WO-1121 sec.2):\n" +
                $"  Buy CTA gate (ff.realmstorepurchase) : {(buy ? "ON" : "OFF")}\n" +
                $"  Default network                      : {WalletService.DefaultNetwork}\n" +
                $"  SKR mint resolvable on that network  : {(mint ? "YES" : "NO - WalletEndpoints.SkrMint is empty")}\n" +
                $"  Declared primary rail                : {rail}\n" +
                $"  Idempotent grant by paymentId        : YES (PurchaseGate.TryClaimGrant)\n" +
                $"  Mainnet policy                       : blocked in SolanaWalletProvider.SendPayment (deliberate)\n" +
                $"  Price ceiling                        : ${49.99d:0.00} (owner 2026-08-21 - the ${WalletRequiredAboveUsd:0.00} cap was EARLY-ACCESS, not permanent)\n" +
                $"  Payment channel                      : {channel}\n" +
                $"  Wallet required                      : {walletRule}\n" +
                $"  This save has an attested wallet     : {(HasDurableIdentity ? "YES" : "NO - guest/device key, so " + guestCanBuy)}\n" +
                "  NOT VERIFIABLE FROM CODE (owner actions): one settled transfer test, the mainnet\n" +
                "  decision itself, and the pay -> grant -> save -> relaunch device proof.";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ReportOnBoot()
        {
            Guard.Try("Store", "PurchaseGate boot report",
                () => FlowTrace.Once("Store", "purchase-checklist", ChecklistReport()));
        }

        // =====================================================================
        //  Internals
        // =====================================================================

        /// <summary>
        /// True when the SKR mint for the build's default network is non-empty. Read from
        /// WalletEndpoints at source - never cached into a doc or a second constant, which is how
        /// the two copies drift and a ship decision gets made against a stale one.
        /// </summary>
        private static bool SkrMintResolvable() =>
            !string.IsNullOrEmpty(WalletEndpoints.SkrMint(WalletService.DefaultNetwork));

        /// <summary>
        /// Which rail this build claims as primary. WO-1121 sec.2 asks for this to be DOCUMENTED
        /// rather than assumed: if SKR cannot resolve a mint, SKR is not the primary rail no matter
        /// what the storefront copy says.
        /// </summary>
        private static string PrimaryRail() => SkrMintResolvable() ? "SKR" : "USDC/SOL";

        /// <summary>Invariant-culture "$4.99" for the refusal sentence's {0}. Never the device
        /// locale: the price on the card is authored in USD, so the threshold must read the same.
        /// <para>⚠ WIDENED private -> internal by WO-1323, and ONLY the accessor moved: the rule, the
        /// threshold and every caller behave identically. PackStore used to word the SAME refusal for
        /// the Pi skin (storePiWalletGate) through it - retired 2026-09-04 by WO-1386 (Pi has no guest
        /// tier to name) - and it stays internal because a second copy of this one-line formatter is
        /// exactly the duplicated state that lets one of the two drift into a device locale.</para></summary>
        internal static string FormatUsd(double usd) =>
            "$" + usd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        // Bounded ring so the ledger cannot grow without limit on a device.
        private static void RememberInIndex(string key)
        {
            string index = PlayerPrefs.GetString(GrantedIndex, "");
            string[] keys = string.IsNullOrEmpty(index) ? Array.Empty<string>() : index.Split('|');
            int start = keys.Length >= LedgerCapacity ? keys.Length - LedgerCapacity + 1 : 0;
            for (int i = 0; i < start; i++) PlayerPrefs.DeleteKey(keys[i]);

            var sb = new System.Text.StringBuilder();
            for (int i = start; i < keys.Length; i++)
            {
                if (string.IsNullOrEmpty(keys[i])) continue;
                sb.Append(keys[i]).Append('|');
            }
            sb.Append(key);
            PlayerPrefs.SetString(GrantedIndex, sb.ToString());
        }

        // PlayerPrefs keys are free-form, but a paymentId is a signature and may carry anything.
        // Normalise so a key can never collide with the index key or break the '|' index format.
        private static string Slug(string paymentId)
        {
            var sb = new System.Text.StringBuilder(paymentId.Length);
            for (int i = 0; i < paymentId.Length; i++)
            {
                char c = paymentId[i];
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            }
            return sb.ToString();
        }
    }
}
