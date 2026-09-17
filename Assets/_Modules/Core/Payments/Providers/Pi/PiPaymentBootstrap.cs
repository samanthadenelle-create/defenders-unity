// =============================================================================
// PiPaymentBootstrap - composition root for the Pi U2A payment rail.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.PaymentProviders.Pi   Namespace: DeNelle.Core.Payments.Providers
//
// WO-1318. Mirrors GooglePlayPaymentBootstrap exactly: it runs ONLY on an artifact
// whose resolved channel is PiBrowser (PaymentChannelResolver, which already returned
// PaymentChannel.PiBrowser long before there was anything to register), and it returns
// on the first line everywhere else.
//
// ⛔ THE INCOMPLETE-PAYMENT HANDLER IS WIRED HERE, AT BOOT, AND THAT IS THE POINT.
//    Pi delivers onIncompletePaymentFound through Pi.authenticate - i.e. it can fire
//    at SIGN-IN, long before the store is ever opened, for a payment a PREVIOUS
//    session took money for and never settled. If the only subscriber were the store
//    panel, that callback would arrive with nobody listening and the player would have
//    paid and got nothing. Subscribing at boot is what makes the recovery real.
// =============================================================================

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;

namespace DeNelle.Core.Payments.Providers
{
    internal static class PiPaymentBootstrap
    {
        private const string TraceSystem = PiPaymentEndpoints.TraceSystem;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterForPiArtifact()
        {
            // No silent failures, and no boot-breaking throw either: a payment rail that cannot
            // register must leave the GAME running and the store refusing, never a dead splash.
            try
            {
                if (PaymentChannelResolver.Current != PaymentChannel.PiBrowser) return;

                var pi = PiPlatform.Current;
                if (pi == null)
                {
                    FlowTrace.Fail(TraceSystem, "Pi channel resolved but PiPlatform.Current is null - no rail registered.");
                    return;
                }

                var provider = new PiBrowserPaymentProvider(pi);
                PaymentProviders.Register(provider, PaymentChannel.PiBrowser);

                // Wired at BOOT, not at store-open. See the header.
                pi.OnIncompletePaymentFound -= OnIncompletePaymentFound;
                pi.OnIncompletePaymentFound += OnIncompletePaymentFound;

                FlowTrace.Step(TraceSystem,
                    $"Pi payment rail registered (env={PiEnvironment.Label}, sku={PiBrowserPaymentProvider.EnabledSku}); " +
                    "onIncompletePaymentFound is armed.");
            }
            catch (Exception e)
            {
                FlowTrace.Fail(TraceSystem,
                    $"Pi payment rail FAILED to register ({e.GetType().Name}: {e.Message}). The store will " +
                    "refuse Pi purchases; the game is unaffected.");
            }
        }

        /// <summary>
        /// The player already paid; nothing was delivered. Drive it to completion through OUR backend.
        /// Never ignored, never swallowed - the whole reason this handler is mandatory.
        /// </summary>
        private static void OnIncompletePaymentFound(PiIncompletePayment payment)
        {
            ResumeAsync(payment).Forget();
        }

        private static async UniTaskVoid ResumeAsync(PiIncompletePayment payment)
        {
            using var _ = FlowTrace.Enter(TraceSystem, "resume incomplete payment");

            if (string.IsNullOrEmpty(payment.PiPaymentId))
            {
                FlowTrace.Fail(TraceSystem,
                    "incomplete payment arrived with NO payment id - cannot settle it. Reported so it is " +
                    "visible in the capture rather than lost.");
                return;
            }

            if (!payment.HasTxid)
            {
                // No txid means the transaction never reached the chain: the payment is still waiting
                // for SERVER APPROVAL. Approve it; the SDK then drives it forward (or the backend
                // cancels it, which is also a settled outcome). We cannot fabricate a txid here, and
                // calling /complete without one would be a lie to the Pi API.
                FlowTrace.Warn(TraceSystem,
                    $"incomplete payment {payment.PiPaymentId} has NO txid - it never reached the chain. " +
                    "Sending it to /approve; completion follows only if Pi produces a transaction.");
                await PiPaymentEndpoints.ApproveAsync(payment.PiPaymentId, payment.QuoteId);
                return;
            }

            var settled = await PiPaymentEndpoints.CompleteAsync(payment.PiPaymentId, payment.Txid, payment.QuoteId);
            if (!settled.Ok)
            {
                FlowTrace.Fail(TraceSystem,
                    $"incomplete payment {payment.PiPaymentId} could NOT be completed (code={settled.Code}). " +
                    "It stays incomplete on purpose, so Pi presents it again next launch. Nothing granted.");
                return;
            }

            // ⛔ NEVER INVENT THE SKU. Without one we cannot know what to deliver, and guessing would
            // grant contents the player did not buy. The backend grants server-side at /complete; this
            // local apply is the client mirror, and it is skipped rather than guessed.
            if (string.IsNullOrEmpty(payment.Sku))
            {
                FlowTrace.Warn(TraceSystem,
                    $"incomplete payment {payment.PiPaymentId} completed, but its metadata carried no sku, " +
                    "so no LOCAL grant is applied. The server-side grant still ran. If the player reports " +
                    "missing contents, this line is the reason.");
                return;
            }

            if (PiGrantApplier.ApplyExactlyOnce(payment.Sku, payment.PiPaymentId))
            {
                FlowTrace.Step(TraceSystem,
                    $"incomplete payment {payment.PiPaymentId} RECOVERED: '{payment.Sku}' completed and granted.");

                // WO-1797. THIS IS THE SELF-HEALING HALF. Pi re-presents an unsettled payment on every
                // authenticate, and /api/pi/complete short-circuits a replay to state:'granted' - so
                // acking HERE means a purchase whose ack was lost (offline, crash, closed tab) flips to
                // 'fulfilled' on a later launch with no timer and no bespoke poll. A failure is a Warn:
                // the player already holds the pack.
                try { await PiPaymentEndpoints.AcknowledgeFulfilmentAsync(payment.PiPaymentId, payment.Txid); }
                catch (Exception ackEx)
                {
                    FlowTrace.Warn(TraceSystem,
                        $"fulfil ack threw for recovered payment {payment.PiPaymentId} " +
                        $"({ackEx.GetType().Name}: {ackEx.Message}) - IGNORED ON PURPOSE; the pack is granted.");
                }
            }
            else
                FlowTrace.Fail(TraceSystem,
                    $"incomplete payment {payment.PiPaymentId} completed on the server but the LOCAL grant of " +
                    $"'{payment.Sku}' did not take. The player has paid and holds nothing on this device.");
        }
    }
}
