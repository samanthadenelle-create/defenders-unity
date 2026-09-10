using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;

namespace DeNelle.Core
{
    [Serializable]
    public sealed class CardCollectionDocument
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("minimumClientVersion")] public string MinimumClientVersion = "";
        [JsonProperty("collections")] public List<CardCollectionDefinition> Collections = new List<CardCollectionDefinition>();
    }

    [Serializable]
    public sealed class CardCollectionDefinition
    {
        [JsonProperty("collectionId")] public string CollectionId = "";
        [JsonProperty("context")] public string Context = "";
        [JsonProperty("title")] public string Title = "";
        [JsonProperty("subtitle")] public string Subtitle = "";
        [JsonProperty("iconKey")] public string IconKey = "";
        [JsonProperty("iconCdnUrl")] public string IconCdnUrl = "";
        [JsonProperty("iconSha256")] public string IconSha256 = "";
        [JsonProperty("active")] public bool Active = true;
        [JsonProperty("startsAtUnixMs")] public double StartsAtUnixMs;
        [JsonProperty("endsAtUnixMs")] public double EndsAtUnixMs;
        [JsonProperty("fallbackCollectionId")] public string FallbackCollectionId = "";
        [JsonProperty("items")] public List<CardCollectionItemPointer> Items = new List<CardCollectionItemPointer>();
    }

    [Serializable]
    public sealed class CardCollectionItemPointer
    {
        [JsonProperty("itemId")] public string ItemId = "";
        [JsonProperty("order")] public int Order;
        [JsonProperty("badge")] public string Badge = "";
        // RETIRED 2026-09-10 by owner ruling on WO-1430 (fields 3-5): the visibilityRule member
        // was DROPPED. It declared a show/hide rule as a bare string while the server emits a
        // visibility OBJECT (see CardCollectionApiItem.Visibility below) -- two shapes that never
        // agreed -- and it was authored on ZERO rows of card-collections.json in either canonical
        // twin. Re-add it only in the same change as the code that applies it, and in the shape
        // the server actually sends. Absence pinned by AuthoredFieldReaderRegression case
        // [retired-field-stays-retired].
        [JsonProperty("asset")] public CardAssetPointer Asset;
    }

    [Serializable]
    public sealed class CardAssetPointer
    {
        [JsonProperty("cdnUrl")] public string CdnUrl = "";
        [JsonProperty("version")] public int Version;
        [JsonProperty("sizeBytes")] public long SizeBytes;
        [JsonProperty("sha256")] public string Sha256 = "";
        [JsonProperty("minimumClientVersion")] public string MinimumClientVersion = "";
        [JsonProperty("packagedFallbackKey")] public string PackagedFallbackKey = "";
        [JsonProperty("safeFallbackItemId")] public string SafeFallbackItemId = "";
    }

    [Serializable]
    internal sealed class CardCollectionApiEnvelope
    {
        [JsonProperty("success")] public bool Success;
        [JsonProperty("serverNowMs")] public double ServerNowMs;
        [JsonProperty("code")] public string Code;
        [JsonProperty("collection")] public CardCollectionApiDefinition Collection;
    }

    [Serializable]
    internal sealed class CardCollectionApiDefinition
    {
        [JsonProperty("collection_id")] public string CollectionId;
        [JsonProperty("requested_collection_id")] public string RequestedCollectionId;
        [JsonProperty("used_fallback")] public bool UsedFallback;
        [JsonProperty("context")] public string Context;
        [JsonProperty("title")] public string Title;
        [JsonProperty("subtitle")] public string Subtitle;
        [JsonProperty("icon")] public CardCollectionApiIcon Icon;
        [JsonProperty("version")] public int Version;
        [JsonProperty("min_client_version")] public string MinimumClientVersion;
        [JsonProperty("items")] public List<CardCollectionApiItem> Items;
    }

    [Serializable] internal sealed class CardCollectionApiIcon
    {
        [JsonProperty("key")] public string Key;
        [JsonProperty("url")] public string Url;
        [JsonProperty("sha256")] public string Sha256;
    }

    [Serializable] internal sealed class CardCollectionApiAsset
    {
        [JsonProperty("url")] public string Url;
        [JsonProperty("sha256")] public string Sha256;
        [JsonProperty("size_bytes")] public long SizeBytes;
        [JsonProperty("version")] public int Version;
    }

    [Serializable] internal sealed class CardCollectionApiItem
    {
        [JsonProperty("sku")] public string Sku;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("version")] public int Version;
        [JsonProperty("definition")] public JObject Definition;
        [JsonProperty("packaged_fallback_key")] public string PackagedFallbackKey;
        [JsonProperty("fallback_sku")] public string FallbackSku;
        // RETIRED 2026-09-10 by owner ruling on WO-1430 (fields 3-5): the CLIENT-side
        // expiry_behavior member was DROPPED. It was parse-and-discard -- nothing under
        // Assets/ ever named it, so every expiry behaved identically whatever the server said.
        // ⚠ THE SERVER SIDE IS UNTOUCHED AND STILL LIVE: api/schema.sql:1621 constrains the
        // column to (hide|lock|fallback), api/_lib/catalog-read.js:87 rejects a row whose value
        // is not one of them and :106 emits it, and api/admin/showcase-finalize.js:83 filters on
        // it. So live responses STILL carry the key; it is now an unknown member, which the
        // plain JsonConvert.DeserializeObject call below (no JsonSerializerSettings, so
        // Newtonsoft's default MissingMemberHandling.Ignore) discards without error. The
        // CardCollectionFoundationRegression API fixture deliberately KEEPS the key for exactly
        // that reason: it is now the proof the parser tolerates it. Absence of the client member
        // is pinned by AuthoredFieldReaderRegression case [retired-field-stays-retired].
        [JsonProperty("asset")] public CardCollectionApiAsset Asset;
        [JsonProperty("display_order")] public int DisplayOrder;
        [JsonProperty("badge")] public string Badge;
        [JsonProperty("visibility")] public JObject Visibility;
    }

    public interface ICardCollectionCache
    {
        string Read();
        void Write(string json);
    }

    public sealed class FileCardCollectionCache : ICardCollectionCache
    {
        private readonly string _path;
        public FileCardCollectionCache(string path) { _path = path; }
        public string Read()
        {
            try { return File.Exists(_path) ? File.ReadAllText(_path) : null; }
            catch (Exception ex) { FlowTrace.Warn("CardCollections", "cache read failed: " + ex.Message); return null; }
        }
        public void Write(string json)
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, json ?? "");
            }
            catch (Exception ex) { FlowTrace.Warn("CardCollections", "cache write failed: " + ex.Message); }
        }
    }

    /// <summary>Validated collection metadata resolver. Item definitions and ownership stay elsewhere.</summary>
    public sealed class CardCollectionCatalog
    {
        public const string PackagedPath = "Data/Canonical/card-collections.json";
        private readonly Func<string> _packagedRead;
        private readonly ICardCollectionCache _cache;
        private readonly string _clientVersion;

        public CardCollectionCatalog(Func<string> packagedRead, ICardCollectionCache cache, string clientVersion)
        {
            _packagedRead = packagedRead;
            _cache = cache;
            _clientVersion = clientVersion ?? "0";
        }

        public static CardCollectionCatalog CreateDefault(string persistentDataPath, string clientVersion) =>
            new CardCollectionCatalog(() => CanonicalJson.Read(PackagedPath),
                new FileCardCollectionCache(Path.Combine(persistentDataPath, "card-collections-cache.json")), clientVersion);

        public CardCollectionDocument Resolve()
        {
            TryParseValidated(_packagedRead != null ? _packagedRead() : null, _clientVersion, out var packaged, out _);
            TryParseValidated(_cache != null ? _cache.Read() : null, _clientVersion, out var cached, out _);
            if (packaged != null && (cached == null || packaged.Version >= cached.Version)) return packaged;
            return cached ?? packaged ?? new CardCollectionDocument();
        }

        public bool AcceptRemote(string json, string expectedSha256, out string reason) =>
            AcceptRemote(json, expectedSha256, Encoding.UTF8.GetByteCount(json ?? ""), out reason);

        public bool AcceptRemote(string json, string expectedSha256, long expectedSizeBytes, out string reason)
        {
            reason = null;
            long actualSize = Encoding.UTF8.GetByteCount(json ?? "");
            if (expectedSizeBytes < 0 || actualSize != expectedSizeBytes)
            { reason = "size mismatch"; return false; }
            string actual = Sha256(json ?? "");
            if (string.IsNullOrEmpty(expectedSha256) || !string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            { reason = "hash mismatch"; return false; }
            if (!TryParseValidated(json, _clientVersion, out var remote, out reason)) return false;
            var standing = Resolve();
            if (standing != null && remote.Version < standing.Version) { reason = "remote version is older than standing data"; return false; }
            _cache?.Write(json);
            return true;
        }

        /// <summary>
        /// Adapts the exact `/api/catalog/collection` snake_case envelope to the neutral client
        /// model. Transport remains injectable/outside this class. Any unavailable or rejected
        /// response resolves the named packaged/cache collection; cache presence grants nothing.
        /// </summary>
        public CardCollectionModel ResolveApiEnvelope(string envelopeJson, string requestedCollectionId,
                                                       out bool usedPackagedFallback, out string reason)
        {
            usedPackagedFallback = false; reason = null;
            if (TryParseApiEnvelope(envelopeJson, _clientVersion, out var api, out reason))
                return ToPresentation(api);

            usedPackagedFallback = true;
            var standing = Resolve();
            var fallback = standing.Collections.Find(c => c != null &&
                string.Equals(c.CollectionId, requestedCollectionId, StringComparison.Ordinal));
            if (fallback == null) { reason = (reason ?? "remote unavailable") + "; packaged fallback missing"; return null; }
            return ToPresentation(fallback);
        }

        internal static bool TryParseApiEnvelope(string json, string clientVersion,
                                                 out CardCollectionApiDefinition collection, out string reason)
        {
            collection = null; reason = null;
            CardCollectionApiEnvelope env;
            try { env = JsonConvert.DeserializeObject<CardCollectionApiEnvelope>(json ?? ""); }
            catch (Exception ex) { reason = "invalid API envelope: " + ex.Message; return false; }
            if (env == null || !env.Success || env.Collection == null) { reason = env?.Code ?? "collection unavailable"; return false; }
            var c = env.Collection;
            if (string.IsNullOrWhiteSpace(c.CollectionId) || string.IsNullOrWhiteSpace(c.Title) || c.Version <= 0)
            { reason = "invalid API collection identity"; return false; }
            if (CompareVersions(clientVersion, c.MinimumClientVersion) < 0) { reason = "client is incompatible"; return false; }
            if (c.Icon != null && !string.IsNullOrEmpty(c.Icon.Url) &&
                (!IsSafeHttps(c.Icon.Url) || !IsSha256(c.Icon.Sha256)))
            { reason = "invalid API icon metadata"; return false; }
            c.Items ??= new List<CardCollectionApiItem>();
            c.Items.Sort((a, b) => (a?.DisplayOrder ?? int.MaxValue).CompareTo(b?.DisplayOrder ?? int.MaxValue));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in c.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Sku) || !ids.Add(item.Sku) || item.Version <= 0)
                { reason = "invalid API item identity"; return false; }
                if (item.Asset != null && (!IsSafeHttps(item.Asset.Url) || !IsSha256(item.Asset.Sha256) ||
                                           item.Asset.SizeBytes <= 0 || item.Asset.Version <= 0))
                { reason = "invalid API asset metadata"; return false; }
            }
            collection = c;
            return true;
        }

        private static CardCollectionModel ToPresentation(CardCollectionApiDefinition c)
        {
            var cards = new List<GenericCardModel>(c.Items.Count);
            foreach (var i in c.Items)
            {
                string title = (string)i.Definition?["title"] ?? i.Sku;
                string purpose = (string)i.Definition?["purpose"] ?? (string)i.Definition?["description"] ?? "";
                string art = (string)i.Definition?["card_art_key"] ?? (string)i.Definition?["icon_key"] ??
                             i.PackagedFallbackKey ?? "";
                string detail = i.Definition?["contents"]?.ToString(Formatting.None) ??
                                i.Definition?["cost"]?.ToString(Formatting.None) ?? "";
                cards.Add(new GenericCardModel { StableId = i.Sku, Title = title, Purpose = purpose,
                    ArtworkKey = art, Badge = i.Badge ?? "", ContentsOrCost = detail });
            }
            return new CardCollectionModel { CollectionId = c.CollectionId, Title = c.Title ?? "",
                Subtitle = c.Subtitle ?? "", IconKey = c.Icon?.Key ?? "", Cards = cards };
        }

        private static CardCollectionModel ToPresentation(CardCollectionDefinition c)
        {
            var cards = new List<GenericCardModel>(c.Items.Count);
            foreach (var i in c.Items) cards.Add(new GenericCardModel { StableId = i.ItemId, Badge = i.Badge ?? "" });
            return new CardCollectionModel { CollectionId = c.CollectionId, Title = c.Title,
                Subtitle = c.Subtitle, IconKey = c.IconKey, Cards = cards };
        }

        private static bool IsSha256(string value) => !string.IsNullOrEmpty(value) &&
            value.Length == 64 && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-fA-F]+$");
        private static bool IsSafeHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);

        public static bool TryParseValidated(string json, string clientVersion, out CardCollectionDocument document, out string reason)
        {
            document = null; reason = null;
            if (string.IsNullOrWhiteSpace(json)) { reason = "empty document"; return false; }
            try { document = JsonConvert.DeserializeObject<CardCollectionDocument>(json); }
            catch (Exception ex) { reason = "invalid json: " + ex.Message; return false; }
            if (document == null || document.Version <= 0) { reason = "version must be positive"; document = null; return false; }
            if (CompareVersions(clientVersion, document.MinimumClientVersion) < 0) { reason = "client is incompatible"; document = null; return false; }
            if (document.Collections == null) { reason = "collections missing"; document = null; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in document.Collections)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.CollectionId) || !ids.Add(c.CollectionId))
                { reason = "collection ids must be non-empty and unique"; document = null; return false; }
                if (c.Items == null) c.Items = new List<CardCollectionItemPointer>();
                c.Items.Sort((a, b) => (a?.Order ?? int.MaxValue).CompareTo(b?.Order ?? int.MaxValue));
                var itemIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in c.Items)
                    if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || !itemIds.Add(item.ItemId))
                    { reason = "item pointers must be non-empty and unique within a collection"; document = null; return false; }
            }
            return true;
        }

        public static string Sha256(string text)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static int CompareVersions(string a, string b)
        {
            var aa = (a ?? "0").Split('.'); var bb = (b ?? "0").Split('.');
            int n = Math.Max(aa.Length, bb.Length);
            for (int i = 0; i < n; i++)
            {
                int.TryParse(i < aa.Length ? aa[i] : "0", out int av);
                int.TryParse(i < bb.Length ? bb[i] : "0", out int bv);
                if (av != bv) return av.CompareTo(bv);
            }
            return 0;
        }
    }
}
