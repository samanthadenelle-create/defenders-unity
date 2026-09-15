using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Entitlements;
using DeNelle.Core.Backend;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Core.Social
{
    /// <summary>
    /// Client presentation seam for WO-1277. Shipping builds remain off until the product and
    /// abuse-policy gates are ruled; there is intentionally no remote-config or PlayerPrefs switch.
    /// </summary>
    public static class CommunityShowcaseVotingFeature
    {
        public const bool Enabled = false;
    }

    public enum ShowcaseVoteState { Unavailable, Cast, AlreadyCast, Rejected }

    public readonly struct ShowcaseVoteResult
    {
        public readonly ShowcaseVoteState State;
        public readonly string Error;
        public bool Succeeded => State == ShowcaseVoteState.Cast || State == ShowcaseVoteState.AlreadyCast;
        public ShowcaseVoteResult(ShowcaseVoteState state, string error = null)
        { State = state; Error = error; }
    }

    public readonly struct ShowcaseVoteCount
    {
        public readonly string ShowcaseId;
        public readonly long Votes;
        public readonly int Rank;
        public ShowcaseVoteCount(string showcaseId, long votes, int rank = 0)
        { ShowcaseId = showcaseId; Votes = Math.Max(0L, votes); Rank = Math.Max(0, rank); }
    }

    /// <summary>A blinded discovery row: intentionally no votes, rank, owner, or player id.</summary>
    public readonly struct ShowcaseDiscoveryCandidate
    {
        public readonly string ShowcaseId;
        public ShowcaseDiscoveryCandidate(string showcaseId) { ShowcaseId = showcaseId; }
    }

    public readonly struct ShowcaseTransportResult
    {
        public readonly bool Success;
        public readonly long StatusCode;
        public readonly string Body;
        public ShowcaseTransportResult(bool success, long statusCode, string body)
        { Success = success; StatusCode = statusCode; Body = body; }
    }

    public interface ICommunityShowcaseTransport
    {
        UniTask<ShowcaseTransportResult> DiscoverAsync(string playerId, string contestId, string categoryId);
        UniTask<ShowcaseTransportResult> GetCountsAsync(string contestId, string categoryId);
        UniTask<ShowcaseTransportResult> CastVoteAsync(
            string playerId, string contestId, string categoryId, string showcaseId);
    }

    /// <summary>
    /// Authenticated voting facade. The player identifier is used only to sign the request and is
    /// never returned in presentation data. Anonymous voting fails before transport is invoked.
    /// </summary>
    public sealed class CommunityShowcaseVotingService
    {
        private readonly ICommunityShowcaseTransport _transport;
        private readonly bool _enabled;

        public CommunityShowcaseVotingService(ICommunityShowcaseTransport transport = null)
            : this(transport ?? new BackendTransport(), CommunityShowcaseVotingFeature.Enabled) { }

        internal CommunityShowcaseVotingService(ICommunityShowcaseTransport transport, bool enabled)
        { _transport = transport ?? throw new ArgumentNullException(nameof(transport)); _enabled = enabled; }

        public async UniTask<IReadOnlyList<ShowcaseDiscoveryCandidate>> DiscoverAsync(
            string authenticatedPlayerId, string contestId, string categoryId)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(authenticatedPlayerId) ||
                !ValidContestId(contestId) || !ValidCategoryId(categoryId))
                return Array.Empty<ShowcaseDiscoveryCandidate>();
            try
            {
                var result = await _transport.DiscoverAsync(authenticatedPlayerId, contestId, categoryId);
                if (!result.Success) return Array.Empty<ShowcaseDiscoveryCandidate>();
                var reply = JsonConvert.DeserializeObject<DiscoveryReply>(result.Body);
                if (reply?.Success != true || reply.ContestId != contestId || reply.CategoryId != categoryId ||
                    reply.Candidates == null) return Array.Empty<ShowcaseDiscoveryCandidate>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var rows = new List<ShowcaseDiscoveryCandidate>(Math.Min(reply.Candidates.Count, 100));
                foreach (var row in reply.Candidates)
                {
                    if (rows.Count >= 100) break;
                    if (row != null && ValidShowcaseId(row.ShowcaseId) && seen.Add(row.ShowcaseId))
                        rows.Add(new ShowcaseDiscoveryCandidate(row.ShowcaseId));
                }
                // Preserve server-authored blinded order. Sorting here would undo discovery randomization.
                return rows;
            }
            catch { return Array.Empty<ShowcaseDiscoveryCandidate>(); }
        }

        /// <summary>
        /// Fetches deterministic post-close rankings. The server withholds this endpoint during
        /// voting; callers must never use it as discovery input.
        /// </summary>
        public async UniTask<IReadOnlyList<ShowcaseVoteCount>> FetchCountsAsync(
            string contestId, string categoryId)
        {
            if (!_enabled || !ValidContestId(contestId) || !ValidCategoryId(categoryId))
                return Array.Empty<ShowcaseVoteCount>();
            try
            {
                var result = await _transport.GetCountsAsync(contestId, categoryId);
                if (!result.Success) return Array.Empty<ShowcaseVoteCount>();
                var reply = JsonConvert.DeserializeObject<CountsReply>(result.Body);
                if (reply?.Success != true || reply.ContestId != contestId || reply.CategoryId != categoryId ||
                    reply.Candidates == null) return Array.Empty<ShowcaseVoteCount>();
                var counts = new List<ShowcaseVoteCount>(reply.Candidates.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in reply.Candidates)
                    if (row != null && ValidShowcaseId(row.ShowcaseId) && seen.Add(row.ShowcaseId))
                        counts.Add(new ShowcaseVoteCount(row.ShowcaseId, row.Votes));
                counts.Sort((a, b) =>
                {
                    int byVotes = b.Votes.CompareTo(a.Votes);
                    return byVotes != 0 ? byVotes : string.CompareOrdinal(a.ShowcaseId, b.ShowcaseId);
                });
                var ranked = new List<ShowcaseVoteCount>(counts.Count);
                for (int i = 0; i < counts.Count; i++)
                    ranked.Add(new ShowcaseVoteCount(counts[i].ShowcaseId, counts[i].Votes, i + 1));
                return ranked;
            }
            catch { return Array.Empty<ShowcaseVoteCount>(); }
        }

        public async UniTask<ShowcaseVoteResult> CastVoteAsync(
            string authenticatedPlayerId, string contestId, string categoryId, string showcaseId)
        {
            if (!_enabled) return new ShowcaseVoteResult(ShowcaseVoteState.Unavailable, "FEATURE_DISABLED");
            if (string.IsNullOrWhiteSpace(authenticatedPlayerId))
                return new ShowcaseVoteResult(ShowcaseVoteState.Rejected, "AUTH_REQUIRED");
            if (!ValidContestId(contestId) || !ValidCategoryId(categoryId) || !ValidShowcaseId(showcaseId))
                return new ShowcaseVoteResult(ShowcaseVoteState.Rejected, "BAD_PAYLOAD");
            try
            {
                var result = await _transport.CastVoteAsync(
                    authenticatedPlayerId, contestId, categoryId, showcaseId);
                if (!result.Success) return new ShowcaseVoteResult(ShowcaseVoteState.Rejected, ReadError(result.Body));
                var reply = JsonConvert.DeserializeObject<VoteReply>(result.Body);
                if (reply?.Success != true || reply.CategoryId != categoryId || reply.ShowcaseId != showcaseId)
                    return new ShowcaseVoteResult(ShowcaseVoteState.Rejected, "VOTE_REJECTED");
                if (reply.State == "already_cast")
                    return new ShowcaseVoteResult(ShowcaseVoteState.AlreadyCast);
                if (reply.State == "cast") return new ShowcaseVoteResult(ShowcaseVoteState.Cast);
                return new ShowcaseVoteResult(ShowcaseVoteState.Rejected, "VOTE_REJECTED");
            }
            catch { return new ShowcaseVoteResult(ShowcaseVoteState.Rejected, "NETWORK_ERROR"); }
        }

        private static string ReadError(string body)
        {
            try { return JsonConvert.DeserializeObject<ErrorReply>(body)?.Error ?? "VOTE_REJECTED"; }
            catch { return "VOTE_REJECTED"; }
        }

        private static bool ValidContestId(string value) => ValidSlug(value, 3, 64);
        private static bool ValidCategoryId(string value) => ValidSlug(value, 2, 32);
        private static bool ValidShowcaseId(string value) => ValidId(value, true);
        private static bool ValidSlug(string value, int min, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length < min || value.Length > max ||
                value[0] < 'a' || value[0] > 'z') return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                    return false;
            }
            return true;
        }
        private static bool ValidId(string value, bool showcase)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96) return false;
            int start = 0;
            if (showcase) { if (!value.StartsWith("sh_", StringComparison.Ordinal) || value.Length < 19) return false; start = 3; }
            else if (value.Length < 3 || value[0] < 'a' || value[0] > 'z') return false;
            for (int i = start; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            }
            return true;
        }

        private sealed class CountsReply
        {
            [JsonProperty("success")] public bool Success;
            [JsonProperty("contestId")] public string ContestId;
            [JsonProperty("categoryId")] public string CategoryId;
            [JsonProperty("candidates")] public List<CountWire> Candidates;
        }
        private sealed class DiscoveryReply
        {
            [JsonProperty("success")] public bool Success;
            [JsonProperty("contestId")] public string ContestId;
            [JsonProperty("categoryId")] public string CategoryId;
            [JsonProperty("candidates")] public List<DiscoveryWire> Candidates;
        }
        private sealed class DiscoveryWire { [JsonProperty("showcaseId")] public string ShowcaseId; }
        private sealed class CountWire
        {
            [JsonProperty("showcaseId")] public string ShowcaseId;
            [JsonProperty("votes")] public long Votes;
        }
        private sealed class VoteReply
        {
            [JsonProperty("success")] public bool Success;
            [JsonProperty("state")] public string State;
            [JsonProperty("categoryId")] public string CategoryId;
            [JsonProperty("showcaseId")] public string ShowcaseId;
        }
        private sealed class ErrorReply { [JsonProperty("error")] public string Error; }

        private sealed class BackendTransport : ICommunityShowcaseTransport
        {
            public UniTask<ShowcaseTransportResult> DiscoverAsync(
                string playerId, string contestId, string categoryId)
            {
                string json = JsonConvert.SerializeObject(new { playerId, contestId, categoryId });
                return SendAsync("/api/showcase/discover", playerId, json);
            }

            public UniTask<ShowcaseTransportResult> GetCountsAsync(string contestId, string categoryId) =>
                SendAsync("/api/showcase/vote-counts?contestId=" + Uri.EscapeDataString(contestId) +
                    "&categoryId=" + Uri.EscapeDataString(categoryId), null, null);

            public UniTask<ShowcaseTransportResult> CastVoteAsync(
                string playerId, string contestId, string categoryId, string showcaseId)
            {
                string json = JsonConvert.SerializeObject(new
                { playerId, contestId, categoryId, showcaseId });
                return SendAsync("/api/showcase/vote", playerId, json);
            }

            private static async UniTask<ShowcaseTransportResult> SendAsync(string path, string playerId, string json)
            {
                using var request = json == null
                    ? UnityWebRequest.Get(BackendRequestSigner.BackendBase + path)
                    : new UnityWebRequest(BackendRequestSigner.BackendBase + path, "POST")
                    {
                        uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                        downloadHandler = new DownloadHandlerBuffer(),
                    };
                request.timeout = 10;
                request.SetRequestHeader("Accept", "application/json");
                if (json != null)
                {
                    request.SetRequestHeader("Content-Type", "application/json");
                    if (!BackendRequestSigner.TryAttachCachedSession(request, playerId))
                        return new ShowcaseTransportResult(false, 401, "{\"error\":\"AUTH_REQUIRED\"}");
                }
                try { await request.SendWebRequest(); }
                catch { return new ShowcaseTransportResult(false, request.responseCode, null); }
                string body = request.downloadHandler != null ? request.downloadHandler.text : null;
                return new ShowcaseTransportResult(request.result == UnityWebRequest.Result.Success,
                    request.responseCode, body);
            }
        }
    }

    /// <summary>Wallet-free, cosmetic-only projection shared by leaderboard and showcase cards.</summary>
    public readonly struct ShowcaseCardPresentation
    {
        public readonly string ShowcaseId;
        public readonly long Votes;
        public readonly string WinningBadgeSku;
        public bool HasWinningBadge => !string.IsNullOrEmpty(WinningBadgeSku);
        public ShowcaseCardPresentation(string showcaseId, long votes, string winningBadgeSku)
        { ShowcaseId = showcaseId; Votes = Math.Max(0L, votes); WinningBadgeSku = winningBadgeSku; }
    }

    public static class ShowcaseCardPresenter
    {
        public static ShowcaseCardPresentation Create(string showcaseId, long votes, string winningCosmeticSku,
            SkuEntitlementSnapshot entitlements, double monotonicSeconds)
        {
            string badge = entitlements != null && entitlements.IsEntitled(winningCosmeticSku, monotonicSeconds)
                ? winningCosmeticSku : null;
            return new ShowcaseCardPresentation(showcaseId, votes, badge);
        }
    }
}
