using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DeNelle.Commerce;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Payments;
using DeNelle.Core.State;
using DeNelle.Core.Backend;
using DeNelle.Wallet; // PackCatalog's namespace is a preserved runtime contract; its assembly is Commerce.
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.GooglePlay
{
    internal sealed class GooglePlayStorefrontVM
    {
        private const float DeletionConfirmationSeconds = 12f;
        private float _deletionConfirmUntil;
        internal readonly struct Row
        {
            internal readonly string Sku, Label;
            internal readonly bool Available;
            internal Row(string sku, string label, bool available)
            { Sku = sku; Label = label; Available = available; }
        }

        private readonly Action<string> _status;
        private readonly List<Row> _rows = new List<Row>();
        internal IReadOnlyList<Row> Rows => _rows;
        internal static GooglePlayStorefrontVM CreateDefault(Action<string> status)
            => new GooglePlayStorefrontVM(PaymentProviders.Current, status);

        private GooglePlayStorefrontVM(IPaymentProvider provider, Action<string> status)
        {
            _status = status;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !pack.StoreVisible) continue;
                DisplayPrice price = provider != null ? provider.GetDisplayPrice(pack.Sku)
                    : DisplayPrice.Unavailable("Google Play Billing is unavailable.");
                _rows.Add(new Row(pack.Sku,
                    pack.Name + "  " + (price.Available ? price.LocalizedText : "Unavailable"), price.Available));
            }
        }

        internal void Purchase(string sku)
        {
            var provider = PaymentProviders.Current;
            if (provider == null) { _status("Google Play Billing is unavailable."); return; }
            _status("Preparing secure purchase...");
            provider.Purchase(sku, result => _status(result.Succeeded ? "Purchase restored to your realm." :
                result.Pending ? "Purchase pending verification. It will retry safely." : result.Error));
        }

        internal void Restore()
        {
            var provider = PaymentProviders.Current;
            if (provider == null) { _status("Google Play Billing is unavailable."); return; }
            _status("Checking purchases...");
            provider.RestorePurchases((ok, message) =>
                _status(ok ? "Purchases checked and restored." : (message ?? "Restore failed.")));
        }

        private const string DeletionUrl =
            BackendRequestSigner.BackendBase + "/api/account/delete-request";

        internal async void RequestDeletion()
        {
            if (Time.realtimeSinceStartup > _deletionConfirmUntil)
            {
                _deletionConfirmUntil = Time.realtimeSinceStartup + DeletionConfirmationSeconds;
                _status("Tap again within 12 seconds to confirm account deletion.");
                return;
            }
            _deletionConfirmUntil = 0f;

            try
            {
                _status("Confirming your Google Play account...");
                if (!await GooglePlayIdentityClient.EnsureSignedInAsync())
                {
                    _status("Sign in is required. Opening deletion instructions...");
                    Application.OpenURL("https://echoes-of-elarion.vercel.app/delete-account");
                    return;
                }

                string playerId = BackendRequestSigner.CurrentPlayerId();
                if (!GameStateService.IsGooglePlayIdentity(playerId))
                {
                    _status("Account could not be verified. Opening deletion instructions...");
                    Application.OpenURL("https://echoes-of-elarion.vercel.app/delete-account");
                    return;
                }

                byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                    new DeletionRequest { playerId = playerId, scope = "account" }));
                using var request = new UnityWebRequest(DeletionUrl, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = 20,
                };
                request.SetRequestHeader("Content-Type", "application/json");
                if (!BackendRequestSigner.TryAttachCachedSession(request, playerId))
                {
                    _status("Session expired. Please try again.");
                    return;
                }

                _status("Submitting deletion request...");
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    FlowTrace.Warn("AccountDeletion",
                        "request failed: result=" + request.result + " http=" + request.responseCode);
                    _status("Request could not be submitted. Opening deletion instructions...");
                    Application.OpenURL("https://echoes-of-elarion.vercel.app/delete-account");
                    return;
                }

                var reply = JsonUtility.FromJson<DeletionReply>(request.downloadHandler.text);
                _status(reply != null && reply.ok && !string.IsNullOrWhiteSpace(reply.requestId)
                    ? "Deletion requested. Reference: " + reply.requestId
                    : "Deletion request received.");
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("AccountDeletion", "request failed: " + ex.GetType().Name);
                _status("Request could not be submitted. Opening deletion instructions...");
                Application.OpenURL("https://echoes-of-elarion.vercel.app/delete-account");
            }
        }

        [Serializable] private sealed class DeletionRequest
        {
            public string playerId;
            public string scope;
        }

        [Serializable] private sealed class DeletionReply
        {
            public bool ok;
            public string requestId;
        }
    }
}
