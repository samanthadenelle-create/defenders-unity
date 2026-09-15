using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Core.Social
{
    [Serializable]
    public sealed class TopTownVisitEntry
    {
        [JsonProperty("rank")] public int Rank;
        [JsonProperty("username")] public string Username;
        [JsonProperty("score")] public long Score;
        [JsonProperty("showcaseId")] public string ShowcaseId;
        [JsonIgnore] public bool CanVisit => TownShowcaseIds.IsShowcaseId(ShowcaseId);
        [JsonIgnore] public string VisitLabel => CanVisit ? "Visit Town" : "Town not shared";
    }

    public static class TownShowcaseIds
    {
        public static bool IsShowcaseId(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("sh_", StringComparison.Ordinal) ||
                value.Length < 19 || value.Length > 96) return false;
            for (int i = 3; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
            }
            return true;
        }
    }

    public enum TownShowcaseLoadStatus
    {
        Ready, NotPublished, NetworkUnavailable, InvalidSnapshot, ClientUpdateRequired
    }

    public sealed class TownShowcaseLoadResult
    {
        public TownShowcaseLoadStatus Status;
        public string Message;
        public PublicTownSnapshot Snapshot;
        public bool IsReady => Status == TownShowcaseLoadStatus.Ready && Snapshot != null;
    }

    /// <summary>GET-only transport for the two public showcase routes. It never authenticates or sends a body.</summary>
    public sealed class TownShowcaseClient
    {
        private const int TimeoutSeconds = 8;
        private readonly string _baseUrl;

        public TownShowcaseClient(string baseUrl = null)
        {
            _baseUrl = (baseUrl ?? DeNelle.Core.Backend.BackendRequestSigner.BackendBase).TrimEnd('/');
        }

        public async UniTask<IReadOnlyList<TopTownVisitEntry>> FetchTopTenAsync(
            string metric = "highest_wave", string period = "alltime")
        {
            string url = _baseUrl + "/api/showcase/top?metric=" + Uri.EscapeDataString(metric ?? "") +
                         "&period=" + Uri.EscapeDataString(period ?? "");
            try
            {
                using var request = UnityWebRequest.Get(url);
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Accept", "application/json");
                await request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                    return Array.Empty<TopTownVisitEntry>();
                var envelope = JsonConvert.DeserializeObject<TopEnvelope>(request.downloadHandler.text);
                if (envelope?.success != true || envelope.top == null) return Array.Empty<TopTownVisitEntry>();
                envelope.top.RemoveAll(x => x == null || x.Rank < 1 || x.Rank > 10);
                return envelope.top;
            }
            catch { return Array.Empty<TopTownVisitEntry>(); }
        }

        public async UniTask<TownShowcaseLoadResult> FetchSnapshotAsync(string showcaseId, string clientVersion)
        {
            if (!TownShowcaseIds.IsShowcaseId(showcaseId))
                return Failure(TownShowcaseLoadStatus.NotPublished, "This town is not available to visit.");
            try
            {
                using var request = UnityWebRequest.Get(_baseUrl + "/api/showcase/get?id=" + Uri.EscapeDataString(showcaseId));
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Accept", "application/json");
                await request.SendWebRequest();
                if (request.responseCode == 404)
                    return Failure(TownShowcaseLoadStatus.NotPublished, "This town is no longer shared.");
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                    return Failure(TownShowcaseLoadStatus.NetworkUnavailable, "Town visits are temporarily unavailable.");

                var envelope = JsonConvert.DeserializeObject<SnapshotEnvelope>(request.downloadHandler.text);
                var snapshot = envelope?.success == true ? envelope.snapshot : null;
                if (!PublicTownSnapshotPolicy.Validate(snapshot).IsValid || snapshot.SnapshotId != showcaseId)
                    return Failure(TownShowcaseLoadStatus.InvalidSnapshot, "This town snapshot could not be displayed safely.");
                if (CompareVersions(clientVersion, snapshot.MinimumClientVersion) < 0)
                    return Failure(TownShowcaseLoadStatus.ClientUpdateRequired, "Update the game to visit this town.");
                return new TownShowcaseLoadResult { Status = TownShowcaseLoadStatus.Ready, Snapshot = snapshot };
            }
            catch { return Failure(TownShowcaseLoadStatus.NetworkUnavailable, "Town visits are temporarily unavailable."); }
        }

        public static int CompareVersions(string installed, string required)
        {
            Version a, b;
            return Version.TryParse(installed, out a) && Version.TryParse(required, out b) ? a.CompareTo(b) :
                string.Equals(installed, required, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
        }

        private static TownShowcaseLoadResult Failure(TownShowcaseLoadStatus status, string message) =>
            new TownShowcaseLoadResult { Status = status, Message = message };

        private sealed class TopEnvelope { public bool success; public List<TopTownVisitEntry> top; }
        private sealed class SnapshotEnvelope { public bool success; public PublicTownSnapshot snapshot; }
    }

    public interface IReadOnlyTownCatalog
    {
        bool ContainsStructure(string itemId);
    }

    public sealed class ReadOnlyTownStructure
    {
        public string RequestedItemId;
        public string PresentationItemId;
        public Vector3 Position;
        public Quaternion Rotation;
        public int DisplayLevel;
        public bool IsFallback;
        public string FallbackLabel;
    }

    /// <summary>
    /// Isolated visitor projection. This owns presentation descriptors only: no GameState,
    /// PlayerPrefs, collectors, build commands, economy services, or progression callbacks.
    /// </summary>
    public sealed class ReadOnlyTownShowcaseView
    {
        public const string MissingStructurePlaceholder = "showcase_missing_structure";
        private readonly List<ReadOnlyTownStructure> _structures = new List<ReadOnlyTownStructure>();
        public IReadOnlyList<ReadOnlyTownStructure> Structures => _structures;

        public bool Reconstruct(PublicTownSnapshot snapshot, IReadOnlyTownCatalog catalog)
        {
            _structures.Clear();
            if (!PublicTownSnapshotPolicy.Validate(snapshot).IsValid) return false;
            foreach (var item in snapshot.Structures)
            {
                bool present = catalog != null && catalog.ContainsStructure(item.ItemId);
                _structures.Add(new ReadOnlyTownStructure
                {
                    RequestedItemId = item.ItemId,
                    PresentationItemId = present ? item.ItemId : MissingStructurePlaceholder,
                    Position = new Vector3(item.CellX, item.WorldY, item.CellZ),
                    Rotation = Quaternion.Euler(0f, item.YawSteps * 90f + item.YawOffset, 0f),
                    DisplayLevel = item.Level,
                    IsFallback = !present,
                    FallbackLabel = present ? null : "Missing structure: " + item.ItemId,
                });
            }
            return true;
        }
    }

    /// <summary>Bounded, deterministic ambient placeholder paths; never starts combat or AI.</summary>
    public static class TownShowcaseAmbient
    {
        public const int MaxAmbientEntities = 32;

        public static IReadOnlyList<Vector3> Sample(string snapshotId, int requestedCount, float elapsedSeconds)
        {
            int count = Mathf.Clamp(requestedCount, 0, MaxAmbientEntities);
            var result = new List<Vector3>(count);
            uint seed = StableHash(snapshotId ?? "");
            for (int i = 0; i < count; i++)
            {
                uint h = seed ^ ((uint)i * 2654435761u);
                float radius = 1.5f + (h % 500u) / 100f;
                float speed = .12f + ((h >> 9) % 20u) / 100f;
                float phase = ((h >> 16) % 628u) / 100f;
                float angle = phase + Mathf.Max(0f, elapsedSeconds) * speed;
                result.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            return result;
        }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++) hash = (hash ^ value[i]) * 16777619u;
            return hash;
        }
    }

    public sealed class TownVisitNavigation
    {
        private readonly IReadOnlyList<TopTownVisitEntry> _top;
        public int LeaderboardRow { get; }
        public float LeaderboardScrollPosition { get; }
        public int CurrentIndex { get; private set; }

        public TownVisitNavigation(IReadOnlyList<TopTownVisitEntry> top, int currentIndex,
            int leaderboardRow, float leaderboardScrollPosition)
        {
            _top = top ?? Array.Empty<TopTownVisitEntry>();
            CurrentIndex = Mathf.Clamp(currentIndex, 0, Math.Max(0, _top.Count - 1));
            LeaderboardRow = Math.Max(0, leaderboardRow);
            LeaderboardScrollPosition = Mathf.Clamp01(leaderboardScrollPosition);
        }

        public TopTownVisitEntry Next() => Move(1);
        public TopTownVisitEntry Previous() => Move(-1);
        private TopTownVisitEntry Move(int direction)
        {
            for (int i = CurrentIndex + direction; i >= 0 && i < _top.Count; i += direction)
                if (_top[i] != null && _top[i].CanVisit) { CurrentIndex = i; return _top[i]; }
            return null;
        }
    }
}
