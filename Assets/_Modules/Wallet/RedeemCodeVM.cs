// =============================================================================
// RedeemCodeVM — the "Redeem a Code" overlay's ViewModel (WO-1512).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// WHY: RedeemCodePanel resolved PromoCodeService itself, subscribed to its two
// events itself, normalised the entered code itself and invoked RedeemAsync
// itself. That is a View performing a transaction against a live backend that
// mints currency — the §2 breach, on the one surface where getting it wrong reads
// to a player as a scam.
//
// The redeem VERB, the service resolution and the code normalisation now live
// here; the panel binds, types, taps and paints. The VM re-raises the service's
// outcomes as its own events so the View never touches PromoCodeService at all.
//
// ⛔ THE CODE ITSELF IS NEVER LOGGED. A promo code is a bearer secret: a trace
// line carrying one hands it to anyone with the log. Outcomes are traced; the
// entry is withheld by design, and the exception TYPE is traced rather than
// swallowed (no silent catch, CLAUDE.md §12).
//
// PURE C#: implements IPanelViewModel, no UnityEngine UI types (§2).
// =============================================================================

using System;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Promo;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Wallet
{
    /// <summary>
    /// ViewModel for <see cref="RedeemCodePanel"/>: owns the promo service seam and the redeem verb.
    /// </summary>
    public sealed class RedeemCodeVM : IPanelViewModel
    {
        private const string Sys = "Promo";

        private bool _subscribed;
        private bool _busy;

        public event Action Changed;

        /// <summary>A code redeemed. Carries the reward the View turns into a receipt line.</summary>
        public event Action<PromoReward> Redeemed;

        /// <summary>A redeem attempt failed. Carries the service's already-resolved player sentence
        /// (which may be blank — the View substitutes the canon unknown-error string).</summary>
        public event Action<string> Failed;

        /// <summary>The three refusals the VM itself can produce before any call is made.</summary>
        public enum RedeemRefusal { None, Empty, ServiceUnavailable, AlreadyBusy }

        // WO-1857: the panel title comes from the promo catalog, exactly as the View's own
        // heading already does (RedeemCodePanel.cs:155 uses PromoStrings.KeyTitle). No new key -
        // this is the SAME job (the redeem screen's own title), so it reuses that row rather
        // than minting a synonym for the English words that happened to be typed here.
        public string Title => PromoStrings.Get(PromoStrings.KeyTitle);

        /// <summary>TRUE while a redeem is in flight; the View disables its entry controls on it.</summary>
        public bool Busy => _busy;

        public static RedeemCodeVM CreateDefault() => new RedeemCodeVM();

        // ── lifecycle ─────────────────────────────────────────────────────────

        /// <summary>Bind to the promo service's outcomes. Idempotent, mirroring the panel's own
        /// subscribe discipline.</summary>
        public void Attach()
        {
            if (_subscribed) return;
            var svc = ResolveService();
            if (svc == null) return;                   // ResolveService self-reports
            svc.OnRedeemed     += RaiseRedeemed;
            svc.OnRedeemFailed += RaiseFailed;
            _subscribed = true;
        }

        public void Detach()
        {
            if (!_subscribed) return;
            var svc = PromoCodeService.Instance;
            if (svc != null)
            {
                svc.OnRedeemed     -= RaiseRedeemed;
                svc.OnRedeemFailed -= RaiseFailed;
            }
            _subscribed = false;
        }

        public void Close() { /* the View owns its own hide. */ }

        public void Dispose()
        {
            Detach();
            Redeemed = null;
            Failed = null;
            Changed = null;
        }

        // ── the verb ──────────────────────────────────────────────────────────

        /// <summary>
        /// Normalise and submit one entered code. Returns a refusal the View can word BEFORE any
        /// network call; a submitted attempt's outcome arrives on <see cref="Redeemed"/> /
        /// <see cref="Failed"/> instead, because the service is the thing that decides it.
        /// </summary>
        /// <param name="entry">Raw text from the input field.</param>
        /// <param name="normalised">The code as submitted — the View writes it back into the field
        /// so the player sees exactly what was sent.</param>
        public async UniTask<RedeemRefusal> RedeemAsync(string entry, Action<string> normalised = null)
        {
            if (_busy) return RedeemRefusal.AlreadyBusy;

            // UPPERCASED, and that is a CONTRACT not a cosmetic: the endpoint stores and compares
            // uppercase, so a player typing their real code in lower case must not be told it does
            // not exist. It belongs with the call, not with the text field.
            string code = string.IsNullOrWhiteSpace(entry) ? string.Empty : entry.Trim().ToUpperInvariant();
            normalised?.Invoke(code);
            if (code.Length == 0) return RedeemRefusal.Empty;

            var svc = ResolveService();
            if (svc == null) return RedeemRefusal.ServiceUnavailable;

            SetBusy(true);
            try
            {
                // The service raises OnRedeemed / OnRedeemFailed; those become our events.
                await svc.RedeemAsync(code);
                return RedeemRefusal.None;
            }
            catch (Exception ex)
            {
                // No silent catch (§12). The exception TYPE is traced; the code never is.
                FlowTrace.Fail(Sys, $"redeem OUTCOME=threw {ex.GetType().Name}: {ex.Message} (entry withheld by design).");
                RaiseFailed(null);
                return RedeemRefusal.ServiceUnavailable;
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ── seam ──────────────────────────────────────────────────────────────

        private static PromoCodeService ResolveService()
        {
            // The service self-bootstraps its own [PromoCodeService] GameObject (AddComponent runs
            // Awake synchronously, so Instance is live on return).
            PromoCodeService.EnsureExists();
            var svc = PromoCodeService.Instance;
            if (svc == null)
                FlowTrace.Fail(Sys, "PromoCodeService.EnsureExists did not produce an Instance — the redeem button has nothing to call.");
            return svc;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Raise();
        }

        private void RaiseRedeemed(PromoReward reward)
        {
            var handler = Redeemed;
            if (handler != null) handler(reward);
            Raise();
        }

        private void RaiseFailed(string sentence)
        {
            var handler = Failed;
            if (handler != null) handler(sentence);
            Raise();
        }

        private void Raise()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
