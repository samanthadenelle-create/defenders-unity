using System;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Backend;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Core.Entitlements
{
    public readonly struct EntitlementTransportResult
    {
        public readonly bool Success;
        public readonly string Body;
        public EntitlementTransportResult(bool success, string body) { Success = success; Body = body; }
    }

    public interface ISkuEntitlementTransport
    {
        UniTask<EntitlementTransportResult> GetAsync(string playerId);
    }

    /// <summary>Authenticated restore consumer. It never mints a session or grants ownership.</summary>
    public sealed class SkuEntitlementService
    {
        public SkuEntitlementSnapshot Snapshot { get; } = new SkuEntitlementSnapshot();
        private readonly ISkuEntitlementTransport _transport;

        public SkuEntitlementService(ISkuEntitlementTransport transport = null)
        {
            _transport = transport ?? new BackendTransport();
        }

        public async UniTask<bool> RefreshAsync(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) { Snapshot.FailClosed(); return false; }
            try
            {
                var result = await _transport.GetAsync(playerId);
                if (!result.Success || !Snapshot.ApplyPayload(result.Body, Time.realtimeSinceStartupAsDouble))
                {
                    Snapshot.FailClosed();
                    return false;
                }
                return true;
            }
            catch
            {
                Snapshot.FailClosed();
                return false;
            }
        }

        private sealed class BackendTransport : ISkuEntitlementTransport
        {
            private const int TimeoutSeconds = 10;

            public async UniTask<EntitlementTransportResult> GetAsync(string playerId)
            {
                string url = BackendRequestSigner.BackendBase + "/api/entitlements?playerId=" +
                             Uri.EscapeDataString(playerId);
                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = TimeoutSeconds;
                    request.SetRequestHeader("Accept", "application/json");
                    // Restore is silent. A missing cached session fails closed and never raises SignMessage.
                    if (!BackendRequestSigner.TryAttachCachedSession(request, playerId))
                        return new EntitlementTransportResult(false, null);
                    try { await request.SendWebRequest(); }
                    catch { return new EntitlementTransportResult(false, null); }
                    if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                        return new EntitlementTransportResult(false, null);
                    return new EntitlementTransportResult(true,
                        request.downloadHandler != null ? request.downloadHandler.text : null);
                }
            }
        }
    }
}
