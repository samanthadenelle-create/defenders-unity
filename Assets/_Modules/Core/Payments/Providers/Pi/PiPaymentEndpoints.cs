// =============================================================================
// PiPaymentEndpoints - the ONE place the Pi U2A payment rail talks to our backend.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.PaymentProviders.Pi   Namespace: DeNelle.Core.Payments.Providers
//
// WO-1318. THREE calls take the money; WO-1797 added a FOURTH that takes nothing and
// only tells the ledger the goods landed:
//
//   POST /api/pi/quote     { sku, uid }                  -> { ok, quoteId, amount, memo, sku, rate,
//                                                            rateSource }
//                                                        | 503 { ok:false, code:'PURCHASE_RATE_UNAVAILABLE' }
//   POST /api/pi/approve   { paymentId, quoteId }
//   POST /api/pi/complete  { paymentId, txid, quoteId }
//   POST /api/pi/fulfill   { paymentId, txid }            -> { ok, state:'fulfilled', replay }
//                          ⛔ WO-1797. NOT part of taking money: it flips the purchase row
//                          'verified' -> 'fulfilled' AFTER the local grant landed. A failure
//                          here costs the player NOTHING and must never fail a purchase.
//
// ⛔ THE CLIENT NEVER DECIDES THE AMOUNT. It asks /quote and uses what comes back
//    VERBATIM. There is deliberately NO local fallback price: the SKR rail's
//    fetchSkrUsdRate already fails closed ("never a stale or invented price") and Pi
//    inherits that ruling. Charging a wrong price is worse than not charging.
//
// ⛔ THE HOST IS ABSOLUTE ON PURPOSE - do NOT "fix" it to a relative path. Under Pi the
//    app is served through Pi's proxy at <app>.pinet.com, so a relative "/api/..." would
//    POST to the PROXY, not to Vercel, and the request would never reach our backend.
//    Same reasoning, same literal, as PiSignInController.VerifyUrl.
//
// ⛔ NO API KEY EVER APPEARS HERE. PI_NETWORK_API_KEY authorises the server-to-server
//    calls to api.minepi.com and lives ONLY in the Vercel environment. If a future edit
//    needs a key on this side, the design is wrong.
//
// Every call is traced (CLAUDE.md sec.12). This rail is verified on a PHONE, inside Pi
// Browser, with no debugger attachable - the web_trace sink is the only evidence there
// will ever be, so the instrumentation is the feature, not decoration.
// =============================================================================

using System;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Payments.Providers
{
    /// <summary>Server-owned quote for one Pi purchase. Never constructed from local data.</summary>
    public sealed class PiQuote
    {
        public string QuoteId;
        public double Amount;      // Pi, server-computed from CoinGecko low_24h. Verbatim.
        public string Memo;
        public string Sku;
        public string RateSource;

        public bool IsUsable =>
            !string.IsNullOrEmpty(QuoteId) && Amount > 0d && !string.IsNullOrEmpty(Sku);

        public string AmountText => Amount.ToString("0.#######", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The result of asking for a quote. Either a usable server quote, or a refusal with the sentence
    /// the player is shown. There is deliberately no third state - "we could not price it" always
    /// means "nothing is charged", never "use a local number".
    /// </summary>
    public readonly struct PiQuoteAttempt
    {
        public readonly PiQuote Quote;
        public readonly string Code;
        public readonly string Message;

        private PiQuoteAttempt(PiQuote quote, string code, string message)
        {
            Quote = quote; Code = code ?? string.Empty; Message = message ?? string.Empty;
        }

        public bool Ok => Quote != null;

        public static PiQuoteAttempt Ready(PiQuote quote) => new PiQuoteAttempt(quote, string.Empty, string.Empty);
        public static PiQuoteAttempt Refused(string code, string message) => new PiQuoteAttempt(null, code, message);
    }

    /// <summary>Outcome of a backend call: succeeded, or refused with a player-readable reason.</summary>
    public readonly struct PiBackendResult
    {
        public readonly bool Ok;
        public readonly string Code;     // machine code, e.g. PURCHASE_RATE_UNAVAILABLE
        public readonly string Message;  // player-readable
        public readonly long HttpStatus;

        private PiBackendResult(bool ok, string code, string message, long status)
        {
            Ok = ok; Code = code ?? string.Empty; Message = message ?? string.Empty; HttpStatus = status;
        }

        public static PiBackendResult Success() => new PiBackendResult(true, string.Empty, string.Empty, 200);
        public static PiBackendResult Refused(string code, string message, long status) =>
            new PiBackendResult(false, code, message, status);
    }

    internal static class PiPaymentEndpoints
    {
        internal const string TraceSystem = "PiPay";

        // See the header: absolute by design. Same host as PiSignInController.VerifyUrl.
        private const string BackendBase = "https://defenders-of-the-realm-v2.vercel.app";
        private const string QuoteUrl    = BackendBase + "/api/pi/quote";
        private const string ApproveUrl  = BackendBase + "/api/pi/approve";
        private const string CompleteUrl = BackendBase + "/api/pi/complete";
        // WO-1797. The FOURTH call, and the only one that is not part of taking money:
        // it tells the ledger the local grant landed. See AcknowledgeFulfilmentAsync.
        private const string FulfillUrl  = BackendBase + "/api/pi/fulfill";

        private const int TimeoutSeconds = 20;

        /// <summary>
        /// Worded refusal shown when the server cannot price the purchase. Mirrors the SKR rail's
        /// PurchaseQuoteService.RateUnavailableMessage in intent: say what happened, never guess a price.
        /// ASCII only - TMP renders anything else as tofu.
        /// </summary>
        internal const string RateUnavailableMessage =
            "Pi pricing is unavailable right now, so nothing was charged. Please try again in a moment.";

        internal const string QuoteFailedMessage =
            "Could not reach the store to price this pack. Nothing was charged.";

        // -----------------------------------------------------------------
        //  /api/pi/quote
        // -----------------------------------------------------------------

        /// <summary>
        /// Asks the server for a BINDING Pi amount. On any refusal <see cref="PiQuoteAttempt.Quote"/>
        /// is null and <see cref="PiQuoteAttempt.Message"/> carries a player-readable reason.
        /// A null quote must ALWAYS abort the purchase - never substitute a local price.
        /// </summary>
        internal static async UniTask<PiQuoteAttempt> RequestQuoteAsync(string sku, string uid)
        {
            FlowTrace.Step(TraceSystem, $"quote requested sku={sku} uid={Mask(uid)}");

            string body = "{\"sku\":" + Json(sku) + ",\"uid\":" + Json(uid) + "}";
            var http = await PostAsync(QuoteUrl, body, "quote");

            if (string.IsNullOrEmpty(http.Text))
            {
                FlowTrace.Fail(TraceSystem,
                    $"quote FAILED: no body (HTTP {http.Status}). Purchase aborted - the client will NOT " +
                    "invent a price.");
                return PiQuoteAttempt.Refused("NO_BODY", QuoteFailedMessage);
            }

            QuoteWire wire;
            try { wire = JsonUtility.FromJson<QuoteWire>(http.Text); }
            catch (Exception e)
            {
                FlowTrace.Fail(TraceSystem, $"quote FAILED: unparseable response ({e.GetType().Name}: {e.Message}).");
                return PiQuoteAttempt.Refused("BAD_JSON", QuoteFailedMessage);
            }

            if (wire == null || !wire.ok)
            {
                string code = wire != null && !string.IsNullOrEmpty(wire.code) ? wire.code : "HTTP_" + http.Status;
                string msg  = wire != null ? wire.message : string.Empty;
                FlowTrace.Fail(TraceSystem,
                    $"quote REFUSED by server: HTTP {http.Status} code={code} message={msg}. Failing closed " +
                    "(no fallback price, by ruling).");
                return PiQuoteAttempt.Refused(code, RefusalMessageFor(code));
            }

            var quote = new PiQuote
            {
                QuoteId    = wire.quoteId,
                Amount     = wire.amount,
                Memo       = wire.memo,
                Sku        = wire.sku,
                RateSource = wire.rateSource,
            };

            if (!quote.IsUsable)
            {
                FlowTrace.Fail(TraceSystem,
                    $"quote UNUSABLE: quoteId='{quote.QuoteId}' amount={quote.AmountText} sku='{quote.Sku}'. " +
                    "A zero or missing amount is refused rather than sent to Pi.");
                return PiQuoteAttempt.Refused("QUOTE_UNUSABLE", QuoteFailedMessage);
            }

            if (!string.Equals(quote.Sku, sku, StringComparison.Ordinal))
            {
                FlowTrace.Fail(TraceSystem,
                    $"quote SKU MISMATCH: asked '{sku}', server quoted '{quote.Sku}'. Refusing - a quote for " +
                    "another pack must never be charged against this one.");
                return PiQuoteAttempt.Refused("QUOTE_SKU_MISMATCH", QuoteFailedMessage);
            }

            FlowTrace.Step(TraceSystem,
                $"quote received sku={quote.Sku} amount={quote.AmountText} Pi quoteId={quote.QuoteId} " +
                $"rateSource={quote.RateSource}");
            return PiQuoteAttempt.Ready(quote);
        }

        /// <summary>Maps a server refusal code to the sentence the player sees. Never leaks a code.</summary>
        internal static string RefusalMessageFor(string code) =>
            string.Equals(code, "PURCHASE_RATE_UNAVAILABLE", StringComparison.Ordinal)
                ? RateUnavailableMessage
                : QuoteFailedMessage;

        // -----------------------------------------------------------------
        //  /api/pi/approve  and  /api/pi/complete
        // -----------------------------------------------------------------

        /// <summary>onReadyForServerApproval -> our backend -> Pi /approve. Fails closed and says so.</summary>
        internal static async UniTask<PiBackendResult> ApproveAsync(string piPaymentId, string quoteId)
        {
            FlowTrace.Step(TraceSystem, $"POST /api/pi/approve paymentId={piPaymentId} quoteId={quoteId}");
            string body = "{\"paymentId\":" + Json(piPaymentId) + ",\"quoteId\":" + Json(quoteId) + "}";
            var http = await PostAsync(ApproveUrl, body, "approve");
            var res = Interpret(http, "approve");
            if (res.Ok) FlowTrace.Step(TraceSystem, $"approve OK paymentId={piPaymentId}");
            else FlowTrace.Fail(TraceSystem,
                $"approve FAILED paymentId={piPaymentId} HTTP {res.HttpStatus} code={res.Code} msg={res.Message}");
            return res;
        }

        /// <summary>onReadyForServerCompletion -> our backend -> Pi /complete, then the grant.</summary>
        internal static async UniTask<PiBackendResult> CompleteAsync(string piPaymentId, string txid, string quoteId)
        {
            FlowTrace.Step(TraceSystem,
                $"POST /api/pi/complete paymentId={piPaymentId} txid={txid} quoteId={quoteId}");
            string body = "{\"paymentId\":" + Json(piPaymentId) +
                          ",\"txid\":" + Json(txid) +
                          ",\"quoteId\":" + Json(quoteId) + "}";
            var http = await PostAsync(CompleteUrl, body, "complete");
            var res = Interpret(http, "complete");
            if (res.Ok) FlowTrace.Step(TraceSystem, $"complete OK paymentId={piPaymentId} txid={txid}");
            else FlowTrace.Fail(TraceSystem,
                $"complete FAILED paymentId={piPaymentId} HTTP {res.HttpStatus} code={res.Code} msg={res.Message} " +
                "-- the player MAY have paid. onIncompletePaymentFound retries this on next launch.");
            return res;
        }

        // -----------------------------------------------------------------
        //  /api/pi/fulfill
        // -----------------------------------------------------------------

        /// <summary>
        /// WO-1797. Tells the purchase ledger that THIS DEVICE persisted the grant, flipping the
        /// entitlement from `verified` to `fulfilled`. Before this existed, every Pi purchase sat
        /// `verified` forever and the one column that answers "was the player actually served" could
        /// not tell a working Pi purchase from a broken one.
        ///
        /// ⛔ THIS IS A LEDGER COURTESY, NOT A STEP OF THE PURCHASE. The pack is already in the save
        ///    by the time this is called, and PiGrantApplier's journal is idempotent. A failure here
        ///    must NEVER fail the purchase, never re-grant, never throw, never retry-loop and never
        ///    block the store closing - it is a Warn and nothing more. Pi re-presents unsettled
        ///    payments through onIncompletePaymentFound, and that path acks again, so a missed ack
        ///    heals itself on a later launch with no timer.
        /// </summary>
        internal static async UniTask<PiBackendResult> AcknowledgeFulfilmentAsync(string piPaymentId, string txid)
        {
            if (string.IsNullOrEmpty(piPaymentId) || string.IsNullOrEmpty(txid))
            {
                // Not a throw: an ack we cannot address is a traced no-op, because the player
                // already holds the goods and nothing about their purchase depends on this call.
                FlowTrace.Warn(TraceSystem,
                    $"fulfil ack SKIPPED - missing {(string.IsNullOrEmpty(piPaymentId) ? "paymentId" : "txid")}. " +
                    "The pack is granted locally; the ledger row stays 'verified' until a later launch re-acks.");
                return PiBackendResult.Refused("ACK_NOT_ADDRESSABLE", string.Empty, 0);
            }

            FlowTrace.Step(TraceSystem, $"POST /api/pi/fulfill paymentId={piPaymentId} txid={txid}");
            string body = "{\"paymentId\":" + Json(piPaymentId) + ",\"txid\":" + Json(txid) + "}";
            var http = await PostAsync(FulfillUrl, body, "fulfill");
            var res = Interpret(http, "fulfill");
            if (res.Ok)
                FlowTrace.Step(TraceSystem, $"fulfil ack OK paymentId={piPaymentId} - ledger now 'fulfilled'.");
            else
                FlowTrace.Warn(TraceSystem,
                    $"fulfil ack FAILED paymentId={piPaymentId} HTTP {res.HttpStatus} code={res.Code} - " +
                    "THE PLAYER STILL HAS THE PACK. Only the ledger's delivery flag is behind; " +
                    "onIncompletePaymentFound re-acks on a later launch.");
            return res;
        }

        // -----------------------------------------------------------------
        //  transport
        // -----------------------------------------------------------------

        private readonly struct HttpText
        {
            public readonly long Status;
            public readonly string Text;
            public HttpText(long status, string text) { Status = status; Text = text; }
        }

        private static async UniTask<HttpText> PostAsync(string url, string body, string what)
        {
            byte[] raw = Encoding.UTF8.GetBytes(body ?? "{}");
            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(raw),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");

            // No silent failures (CLAUDE.md sec.12): a transport throw is logged, then the caller
            // still reads the (empty) body and fails closed.
            try { await req.SendWebRequest(); }
            catch (Exception ex)
            {
                FlowTrace.Warn(TraceSystem,
                    $"{what}: transport {ex.GetType().Name} (HTTP {req.responseCode}) - {ex.Message}");
            }

            string text = req.downloadHandler != null ? req.downloadHandler.text : null;
            return new HttpText(req.responseCode, text);
        }

        private static PiBackendResult Interpret(HttpText http, string what)
        {
            AckWire wire = null;
            if (!string.IsNullOrEmpty(http.Text))
            {
                try { wire = JsonUtility.FromJson<AckWire>(http.Text); }
                catch (Exception e)
                {
                    FlowTrace.Warn(TraceSystem, $"{what}: unparseable response ({e.GetType().Name}).");
                }
            }

            // ⛔ THE HTTP STATUS IS THE AUTHORITY, NOT wire.ok.
            // JsonUtility cannot tell an ABSENT `ok` from `ok:false` - both deserialise to false - so
            // reading wire.ok as the verdict would turn a perfectly good 200 with a `{}` or
            // `{"paymentId":...}` body into a refusal, i.e. a payment the server DID settle and the
            // client then reports as failed. That is the "paid and got nothing" failure this whole WO
            // exists to prevent. The status line is unambiguous; the body only supplies the reason.
            bool httpOk = http.Status >= 200 && http.Status < 300;
            if (httpOk) return PiBackendResult.Success();

            string code = wire != null && !string.IsNullOrEmpty(wire.code) ? wire.code : "HTTP_" + http.Status;
            string msg  = wire != null && !string.IsNullOrEmpty(wire.message) ? wire.message : string.Empty;
            return PiBackendResult.Refused(code, msg, http.Status);
        }

        // -----------------------------------------------------------------
        //  helpers
        // -----------------------------------------------------------------

        /// <summary>Minimal JSON string literal. Escapes the two characters that can break a body.</summary>
        internal static string Json(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// A Pi uid is an account identifier and the trace sink is a shared database, so it is
        /// shortened before it is written. Enough to correlate two lines, not enough to be a handle.
        /// </summary>
        internal static string Mask(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "<none>";
            return uid.Length <= 8 ? uid : uid.Substring(0, 8) + "...";
        }

        // Wire types. Fields NOT declared here (expiresAt, rate) are ignored by JsonUtility, which is
        // deliberate: a field whose JSON type we are not certain of (a number vs an ISO string) would
        // break parsing of an otherwise good quote, and nothing on the client needs them.
        [Serializable] private class QuoteWire
        {
            public bool ok;
            public string quoteId;
            public double amount;
            public string memo;
            public string sku;
            public string rateSource;
            public string code;
            public string message;
        }

        [Serializable] private class AckWire
        {
            public bool ok;
            public string code;
            public string message;
        }
    }
}
