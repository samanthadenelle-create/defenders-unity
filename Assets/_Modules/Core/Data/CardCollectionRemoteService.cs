using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;
using DeNelle.Core.UI;

namespace DeNelle.Core
{
    /// <summary>Public catalog reader. Remote presentation data is cached; it never grants ownership.</summary>
    public sealed class CardCollectionRemoteService
    {
        private const int TimeoutSeconds = 10;
        private readonly CardCollectionCatalog _catalog;
        private readonly string _cacheDirectory;

        public CardCollectionRemoteService(CardCollectionCatalog catalog, string cacheDirectory)
        {
            _catalog = catalog;
            _cacheDirectory = cacheDirectory;
        }

        public async UniTask<CardCollectionModel> ResolveAsync(string collectionId, string clientVersion)
        {
            string body = null;
            try
            {
                string url = DeNelle.Core.Backend.BackendRequestSigner.BackendBase +
                    "/api/catalog/collection?collectionId=" + Uri.EscapeDataString(collectionId ?? "") +
                    "&clientVersion=" + Uri.EscapeDataString(clientVersion ?? "0");
                using var request = UnityWebRequest.Get(url);
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Accept", "application/json");
                await request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                    body = request.downloadHandler?.text;
            }
            catch { body = null; }

            if (!string.IsNullOrWhiteSpace(body))
            {
                var remote = _catalog.ResolveApiEnvelope(body, collectionId, out bool fallback, out _);
                if (!fallback && remote != null)
                {
                    TryWrite(collectionId, body);
                    return remote;
                }
            }

            string cached = TryRead(collectionId);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                var model = _catalog.ResolveApiEnvelope(cached, collectionId, out bool fallback, out _);
                if (!fallback && model != null) return model;
            }

            return _catalog.ResolveApiEnvelope(null, collectionId, out _, out _);
        }

        private string PathFor(string id)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) id = (id ?? "").Replace(c, '_');
            return Path.Combine(_cacheDirectory, "collection-" + id + ".json");
        }

        private string TryRead(string id)
        {
            try { string p = PathFor(id); return File.Exists(p) ? File.ReadAllText(p) : null; }
            catch { return null; }
        }

        private void TryWrite(string id, string body)
        {
            try { Directory.CreateDirectory(_cacheDirectory); File.WriteAllText(PathFor(id), body); }
            catch { }
        }
    }
}
