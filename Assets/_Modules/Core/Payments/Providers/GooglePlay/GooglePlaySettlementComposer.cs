// =============================================================================
// GooglePlaySettlementComposer — the CALLER of ConfigureSettlement (WO-1282).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.PaymentProviders.GooglePlay   Namespace: DeNelle.Core.Payments.Providers
//
// ⛔ THE DEFECT THIS CLOSES. GooglePlayBillingProvider.ConfigureSettlement had NO
//    CALLER anywhere in the tree — a grep returned only its own declaration. The Play
//    settlement path was therefore inert: VerifyAndGrantAsync stayed null, every
//    PendingOrder fell into the AwaitingSettlement branch, and nothing ever granted.
//    Google would have taken the money and the player would have received nothing.
//
// WHAT IT COMPOSES. The one safe order WO-1255 specified:
//    GooglePlayBackendTransport (authenticated server verify + fulfill + HMAC account
//    binding)  ->  GooglePlayGrantApplier (durable, token-idempotent local grant)
//    ->  GooglePlayReceiptSettlement  ->  provider.ConfigureSettlement.
//
// ── FAIL CLOSED, IN THREE PLACES, ON PURPOSE ─────────────────────────────────
//  1. NO IDENTITY -> NO SETTLEMENT. The transport keys every call on the player id
//     (BackendRequestSigner.CurrentPlayerId, i.e. GameState.BoundWallet — on the Play
//     rail a 'play-<64 hex>' id minted by api/auth/google-session.js). Identity arrives
//     LATE (guest by default, Google sign-in at first purchase, WO-1282 PIN-1b), so the
//     id is resolved PER CALL by IdentityScopedTransport rather than captured once at
//     boot. With no id, every call returns "cannot authenticate" and nothing is bought.
//  2. NO ACCOUNT BINDING -> NO CHARGE. GooglePlayBillingProvider.BeginPurchaseAsync
//     fetches the 64-hex HMAC binding BEFORE calling PurchaseProduct. Without a session
//     the binding fetch returns null and the purchase is refused BEFORE Google is ever
//     asked to charge. This is the hard gate: the store cannot sell what it cannot settle.
//  3. NO LOCAL GRANT -> NO CONFIRM. GooglePlayGrantApplier refuses when
//     PackGrantBridge has no applier, SettleAsync returns false, and the provider leaves
//     the Unity order PENDING so Google re-delivers. Nothing is consumed, nothing is lost.
//
// ⛔ THIS ENABLES NOTHING BY ITSELF. It runs only when PaymentChannelResolver.Current
//    is GooglePlay, which is compile-stamped by the GOOGLE_PLAY define. On a
//    Seeker/dApp-Store (DAPP_STORE) artifact the resolver returns SolanaDappStore, the
//    bootstrap returns immediately, and not one line of this file executes. The server
//    rail stays dormant behind its own env flags; composing a client that can talk to it
//    does not arm it.
// =============================================================================

using System;
using System.Threading.Tasks;
using UnityEngine.Networking;
using DeNelle.Commerce;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Backend;

namespace DeNelle.Core.Payments.Providers
{
    /// <summary>
    /// Builds the Google Play settlement chain and hands it to the billing provider. The single
    /// composition root for <see cref="GooglePlayBillingProvider.ConfigureSettlement"/>.
    /// </summary>
    public static class GooglePlaySettlementComposer
    {
        private const string TraceSystem = "PlayBilling";

        /// <summary>
        /// Configures settlement on the provider. Returns false and leaves the provider UNCONFIGURED
        /// (so <see cref="GooglePlayBillingProvider.CanBuy"/> refuses every SKU) when the chain
        /// cannot be built. Never throws into the boot path.
        /// </summary>
        public static bool TryConfigure(GooglePlayBillingProvider provider)
        {
            using var _ = FlowTrace.Enter(TraceSystem, "compose settlement");

            if (provider == null)
            {
                FlowTrace.Fail(TraceSystem, "settlement not configured: no billing provider instance.");
                return false;
            }

            GooglePlayReceiptSettlement settlement = null;
            IdentityScopedTransport transport = null;
            bool built = Guard.Try(TraceSystem, "build Google Play settlement chain", () =>
            {
                transport = new IdentityScopedTransport();
                settlement = new GooglePlayReceiptSettlement(transport, new GooglePlayGrantApplier());
            });

            if (!built || settlement == null || transport == null)
            {
                // ConfigureSettlement is deliberately NOT called on this path: a partially built
                // chain must leave VerifyAndGrantAsync null so the store refuses to sell, rather
                // than sell against a settlement that cannot complete.
                FlowTrace.Fail(TraceSystem,
                    "settlement chain could not be built — the provider stays UNCONFIGURED and " +
                    "CanBuy will refuse every SKU. The Play store cannot sell.");
                return false;
            }

            provider.ConfigureSettlement(settlement, transport);

            // Both readings on the one ambiguous line, so a device capture is never guesswork.
            FlowTrace.Step(TraceSystem, PackGrantBridge.HasApplier
                ? "settlement configured: server verify/fulfill + durable token-idempotent local grant."
                : "settlement configured, but NO local pack grant applier is registered. EXPECTED on a " +
                  "Google Play artifact (DeNelle.Wallet, which owns PackStoreVM, is compiled out) — a " +
                  "purchase will verify and then REFUSE to confirm, leaving the Play order pending " +
                  "rather than charging for nothing. On a build that DOES include DeNelle.Wallet this " +
                  "is a DEFECT: PackStoreBootstrap did not run.");
            return true;
        }

        /// <summary>
        /// Transport + binding source that resolves the player identity on EVERY call instead of
        /// capturing it at boot.
        /// </summary>
        /// <remarks>
        /// ⚠ LOAD-BEARING, NOT A STYLE CHOICE. <see cref="GooglePlayBackendTransport"/> throws when
        /// constructed without a player id, and on the Play rail a player is a GUEST until they sign
        /// in at their first purchase (WO-1282 PIN-1b). Capturing the id at BeforeSceneLoad would
        /// either throw during boot or freeze a guest id that the player has since replaced by
        /// signing in — and a frozen id is the one thing the WO forbids outright, because
        /// google-play-purchases.js HMACs the player id into Play's obfuscatedAccountId and a changed
        /// id makes every past purchase permanently unverifiable.
        /// Each call therefore builds its transport against the CURRENT id, or fails closed.
        /// </remarks>
        private sealed class IdentityScopedTransport : IGooglePlaySettlementTransport,
            IGooglePlayAccountBindingSource
        {
            public Task<GooglePlayVerifyReply> VerifyAsync(string sku, string productId, string purchaseToken)
                => TryResolve("verify", out var t)
                    ? t.VerifyAsync(sku, productId, purchaseToken)
                    : Task.FromResult<GooglePlayVerifyReply>(null);

            public Task<bool> FulfillAsync(string sku, string productId, string purchaseToken)
                => TryResolve("fulfill", out var t)
                    ? t.FulfillAsync(sku, productId, purchaseToken)
                    : Task.FromResult(false);

            public Task<string> FetchAccountBindingAsync()
                => TryResolve("account-binding", out var t)
                    ? t.FetchAccountBindingAsync()
                    : Task.FromResult<string>(null);

            /// <summary>
            /// Builds a transport for the CURRENT identity, or reports why it cannot. A false here
            /// means the caller must behave as "unavailable" — never as "allowed".
            /// </summary>
            private static bool TryResolve(string call, out GooglePlayBackendTransport transport)
            {
                transport = null;
                string playerId = null;
                Guard.Try(TraceSystem, "resolve player identity", () =>
                {
                    playerId = BackendRequestSigner.CurrentPlayerId();
                });

                if (string.IsNullOrWhiteSpace(playerId))
                {
                    FlowTrace.Warn(TraceSystem,
                        $"Google Play {call} refused: there is no player identity yet. This is the " +
                        "ordinary state for a guest who has not signed in, and it correctly stops the " +
                        "purchase BEFORE Google is asked to charge. If it persists for a SIGNED-IN " +
                        "player, GameState.BoundWallet was never stamped with the play-<hex> id.");
                    return false;
                }

                var built = new GooglePlayBackendTransport[1];
                bool ok = Guard.Try(TraceSystem, "build authenticated transport", () =>
                {
                    // TryAttachCachedSession attaches proof already held in memory and NEVER mints.
                    // Minting is a signing operation on the wallet rail; the Play rail's session
                    // comes from api/auth/google-session.js, so an absent session must read as
                    // "not authenticated" here rather than trigger a wallet signature request.
                    built[0] = new GooglePlayBackendTransport(playerId,
                        (request, id) => AttachSession(request, id, call));
                });
                if (!ok || built[0] == null) return false;
                transport = built[0];
                return true;
            }

            private static bool AttachSession(UnityWebRequest request, string playerId, string call)
            {
                bool attached = BackendRequestSigner.TryAttachCachedSession(request, playerId);
                if (!attached)
                    FlowTrace.Warn(TraceSystem,
                        $"Google Play {call} refused: no live authenticated session for this player. " +
                        "The endpoint is NOT called, so an unverified grant is impossible. On the Play " +
                        "rail a session is minted by api/auth/google-session.js at sign-in; on any other " +
                        "rail this path should never be reached at all.");
                return attached;
            }
        }
    }
}
