// =============================================================================
// CircleSource — the LIVE CircleScreenVM.ISource (WO-1870, Lane A).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// Split out of CircleScreenVM.cs for the same reason ClanChatSource is split out of
// ClanChatVM: the VM stays a pure state holder the EditMode suite can drive with a fake,
// and every Unity/network dependency the live rail actually has lives HERE.
//
// ⛔ THE PLAYER'S NAME IS THE EXISTING username, NOT A NEW display_name — LEAD RULING,
// 2026-09-18, which overrides WO-1870's own plan (rows 15/16 of its API table).
// player_profiles.username already exists, is already written by POST /api/profile/username
// (WO-129), is already unique (case-insensitive, api/schema.sql:1010-1012) and already
// carries a profanity gate (api/_lib/username-policy.js). Both gates STAY: the demo does not
// remove a live safety gate. So this file adds NO migration, NO display_name column and NO
// second write endpoint — it calls the route that is already there. The seam name the View
// compiles against is still SetDisplayName / FetchDisplayNames, because that is the A->B
// contract; which route it lands on is this file's business.
//
//   CONSEQUENCE THE VM MIRRORS: the server's real rule is 3..16 chars of [A-Za-z0-9_]
//   (username-policy.js:16-21), not the plan's "1..24, unfiltered". CircleScreenVM's
//   CanSubmitName is computed from the SERVER's rule so no button promises what the route
//   will refuse.
//
//   AND THE ONE WIRE SHAPE THAT IS NOT THE CLAN ENVELOPE: api/profile/username.js answers
//   business failures as HTTP 200 { success:false, error:'USERNAME_TAKEN' | ... }, not the
//   clan routes' { ok:false, code:CODE }. CircleWire.RefusalCode reads BOTH, so the VM's
//   error table needs no second path — but the difference is real and is written down here
//   rather than rediscovered from a "successful" rename that never happened.
//
// ⛔ THE SIGNATURE IS NEVER HAND-ROLLED. Every request goes through
// BackendRequestSigner.TryAttachAsync, and a FALSE return means ABORT — the request is never
// sent (BackendRequestSigner.cs:183-249, the ClanMembershipClient.cs:89-94 /
// ClanChatSource.cs:183-186 fail-closed precedent). Reads pass allowInteractiveSessionMint
// = false; every WRITE is an explicit player press and passes true, which is the difference
// between a phone demo that works and a button that silently does nothing.
//
// ⛔ NO NEWTONSOFT. Bodies are built by hand (JsonString, copied from ClanChatSource) and
// parsed through CircleWire's JsonUtility DTOs — DeNelle.HUD.asmdef is not edited.
// =============================================================================

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Backend;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.HUD
{
    /// <summary>
    /// The live rail behind the Circle screen: identity from the backend signer, five signed
    /// reads, ten signed writes, the clipboard and the chat door.
    /// </summary>
    public sealed class CircleSource : CircleScreenVM.ISource
    {
        private const string Sys = "Circle";
        private const int RequestTimeoutSeconds = 15;

        private const string Base = BackendRequestSigner.BackendBase;
        private const string MeUrl = Base + "/api/clan/me";
        private const string VigilUrl = Base + "/api/clan/vigil";
        private const string LeaderboardUrl = Base + "/api/clan/leaderboard";
        private const string BallotCurrentUrl = Base + "/api/clan/ballot/current";
        private const string BallotProposeUrl = Base + "/api/clan/ballot/propose";
        private const string BallotVoteUrl = Base + "/api/clan/ballot/vote";
        private const string CreateUrl = Base + "/api/clan/create";
        private const string JoinUrl = Base + "/api/clan/join";
        private const string LeaveUrl = Base + "/api/clan/leave";
        private const string PromoteUrl = Base + "/api/clan/promote";
        private const string DemoteUrl = Base + "/api/clan/demote";
        private const string KickUrl = Base + "/api/clan/kick";
        private const string VaultRegisterUrl = Base + "/api/clan/vault/register";
        private const string UsernameUrl = Base + "/api/profile/username";
        private const string UsernamesUrl = Base + "/api/profile/usernames";

        /// <summary>The bound clan room moves with create / join / leave, so the screen re-reads.</summary>
        public event Action Changed
        {
            add { ClanRoomBinding.Changed += value; }
            remove { ClanRoomBinding.Changed -= value; }
        }

        /// <summary>
        /// The proven wallet, or null. ⛔ A GUEST IS NOT A WALLET here: every clan route
        /// narrows to auth.mode === 'wallet' (api/_lib/clan-http.js), so a guest is a
        /// guaranteed 401 — the VM's NoWallet state names that without a round trip.
        /// </summary>
        public string WalletAddress
        {
            get
            {
                var id = BackendRequestSigner.CurrentPlayerId();
                if (string.IsNullOrEmpty(id)) return null;
                if (BackendRequestSigner.IsGuestIdentity(id)) return null;
                return id;
            }
        }

        public bool IsGuest
        {
            get
            {
                var id = BackendRequestSigner.CurrentPlayerId();
                return !string.IsNullOrEmpty(id) && BackendRequestSigner.IsGuestIdentity(id);
            }
        }

        // =====================================================================
        // READS — allowInteractiveSessionMint is the literal false on every one
        // =====================================================================

        public void FetchMe(Action<string, long> done)
        {
            FlowTrace.Step(Sys, "fetch: GET /api/clan/me");
            SendGet("clan/me", MeUrl + "?playerId=" + Escape(WalletAddress), false, done);
        }

        public void FetchVigil(Action<string, long> done)
        {
            FlowTrace.Step(Sys, "fetch: GET /api/clan/vigil");
            SendGet("clan/vigil", VigilUrl + "?playerId=" + Escape(WalletAddress), false, done);
        }

        public void FetchLeaderboard(int limit, Action<string, long> done)
        {
            if (limit < 1) limit = 1;
            FlowTrace.Step(Sys, "fetch: GET /api/clan/leaderboard limit=" + limit);
            SendGet("clan/leaderboard",
                LeaderboardUrl + "?playerId=" + Escape(WalletAddress) + "&limit=" + limit, false, done);
        }

        public void FetchBallot(Action<string, long> done)
        {
            FlowTrace.Step(Sys, "fetch: GET /api/clan/ballot/current");
            SendGet("clan/ballot/current",
                BallotCurrentUrl + "?playerId=" + Escape(WalletAddress), false, done);
        }

        public void FetchDisplayNames(string[] playerIds, Action<string, long> done)
        {
            var ids = JoinIds(playerIds);
            FlowTrace.Step(Sys, "fetch: GET /api/profile/usernames (the Remnant names for the rows)");
            SendGet("profile/usernames",
                UsernamesUrl + "?playerId=" + Escape(WalletAddress) + "&playerIds=" + Escape(ids), false, done);
        }

        // =====================================================================
        // WRITES — every one is an explicit press, so the literal is true
        // =====================================================================

        public void SetDisplayName(string name, Action<string, long> done)
        {
            // ⛔ THE EXISTING ROUTE, AND ITS EXISTING BODY SHAPE: { wallet, username }
            // (api/profile/username.js:20). Not { playerId, displayName } — that endpoint
            // was never built and, per the lead ruling, never will be.
            FlowTrace.Step(Sys, "verb: POST /api/profile/username");
            var payload = "{\"wallet\":" + JsonString(WalletAddress)
                        + ",\"username\":" + JsonString(name) + "}";
            SendPost("profile/username", UsernameUrl, payload, true, done);
        }

        public void Create(string name, string tag, string joinPolicy, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/create");
            var payload = "{\"playerId\":" + JsonString(WalletAddress)
                        + ",\"name\":" + JsonString(name)
                        + ",\"tag\":" + JsonString(tag)
                        + ",\"joinPolicy\":" + JsonString(joinPolicy) + "}";
            SendPost("clan/create", CreateUrl, payload, true, WithRoomRefresh(done));
        }

        public void Join(string code, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/join");
            var payload = "{\"playerId\":" + JsonString(WalletAddress)
                        + ",\"code\":" + JsonString(code) + "}";
            SendPost("clan/join", JoinUrl, payload, true, WithRoomRefresh(done));
        }

        public void Leave(Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/leave");
            var payload = "{\"playerId\":" + JsonString(WalletAddress) + "}";
            SendPost("clan/leave", LeaveUrl, payload, true, WithRoomRefresh(done));
        }

        public void Promote(string targetPlayerId, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/promote");
            SendPost("clan/promote", PromoteUrl, TargetPayload(targetPlayerId), true, done);
        }

        public void Demote(string targetPlayerId, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/demote");
            SendPost("clan/demote", DemoteUrl, TargetPayload(targetPlayerId), true, done);
        }

        public void Kick(string targetPlayerId, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/kick");
            SendPost("clan/kick", KickUrl, TargetPayload(targetPlayerId), true, done);
        }

        public void ProposeBallot(int tier, string[] optionIds, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/ballot/propose tier=" + tier);
            var payload = "{\"playerId\":" + JsonString(WalletAddress)
                        + ",\"tier\":" + tier
                        + ",\"optionIds\":" + JsonStringArray(optionIds) + "}";
            SendPost("clan/ballot/propose", BallotProposeUrl, payload, true, done);
        }

        public void Vote(string ballotId, string optionId, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/ballot/vote");
            var payload = "{\"playerId\":" + JsonString(WalletAddress)
                        + ",\"ballotId\":" + JsonString(ballotId)
                        + ",\"optionId\":" + JsonString(optionId) + "}";
            SendPost("clan/ballot/vote", BallotVoteUrl, payload, true, done);
        }

        public void RegisterVault(string vaultAddress, int vaultIndex, Action<string, long> done)
        {
            FlowTrace.Step(Sys, "verb: POST /api/clan/vault/register");
            var payload = "{\"playerId\":" + JsonString(WalletAddress)
                        + ",\"vaultAddress\":" + JsonString(vaultAddress)
                        + ",\"vaultIndex\":" + vaultIndex + "}";
            SendPost("clan/vault/register", VaultRegisterUrl, payload, true, done);
        }

        // =====================================================================
        // Local affordances
        // =====================================================================

        public void CopyToClipboard(string text)
        {
            Guard.Try(Sys, "CircleSource.CopyToClipboard", () =>
            {
                GUIUtility.systemCopyBuffer = text ?? string.Empty;
                FlowTrace.Step(Sys, "invite code copied to the system clipboard.");
            });
        }

        public void OpenChat()
        {
            Guard.Try(Sys, "CircleSource.OpenChat", () =>
            {
                // The VIEW may never do this lookup — that symbol is banned in a View by
                // UiMvvmConformanceRegression. It lives here, behind the seam, on purpose.
                var panel = UnityEngine.Object.FindAnyObjectByType<ClanChatPanel>();
                if (panel == null)
                {
                    FlowTrace.Warn(Sys, "Circle chat panel is not in the scene — nothing to open.");
                    return;
                }
                FlowTrace.Step(Sys, "handing off to Circle chat.");
                panel.Toggle();
            });
        }

        // =====================================================================
        // Transport
        // =====================================================================

        /// <summary>
        /// After create / join / leave the bound room moves, and ONE parser owns that:
        /// ClanMembershipClient.ApplyResponseJson. This wrapper re-asks /api/clan/me and
        /// feeds the answer straight into it — no second binding is introduced.
        /// </summary>
        private Action<string, long> WithRoomRefresh(Action<string, long> done)
        {
            return (body, status) =>
            {
                try { done?.Invoke(body, status); }
                finally
                {
                    SendGet("clan/me (room refresh)", MeUrl + "?playerId=" + Escape(WalletAddress), false,
                        (meBody, meStatus) =>
                        {
                            if (meStatus != 200) return;
                            Guard.Try(Sys, "CircleSource.ApplyRoom",
                                () => ClanMembershipClient.ApplyResponseJson(meBody));
                        });
                }
            };
        }

        private void SendGet(string what, string url, bool allowInteractiveSessionMint, Action<string, long> done)
        {
            var playerId = WalletAddress;
            if (string.IsNullOrEmpty(playerId))
            {
                FlowTrace.Warn(Sys, what + " NOT sent — no wallet identity (fail-closed).");
                Deliver(done, null, 0);
                return;
            }

            Guard.Try(Sys, "CircleSource.Get." + what, () =>
            {
                var req = UnityWebRequest.Get(url);
                req.timeout = RequestTimeoutSeconds;
                req.SetRequestHeader("Accept", "application/json");
                AttachAndSend(what, req, playerId, null, allowInteractiveSessionMint, done);
            });
        }

        private void SendPost(string what, string url, string payload, bool allowInteractiveSessionMint,
                              Action<string, long> done)
        {
            var playerId = WalletAddress;
            if (string.IsNullOrEmpty(playerId))
            {
                FlowTrace.Warn(Sys, what + " NOT sent — no wallet identity (fail-closed).");
                Deliver(done, null, 0);
                return;
            }

            Guard.Try(Sys, "CircleSource.Post." + what, () =>
            {
                var bodyRaw = Encoding.UTF8.GetBytes(payload ?? "{}");
                var req = new UnityWebRequest(url, "POST");
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = RequestTimeoutSeconds;
                req.SetRequestHeader("Content-Type", "application/json");
                AttachAndSend(what, req, playerId, bodyRaw, allowInteractiveSessionMint, done);
            });
        }

        private static async void AttachAndSend(string what, UnityWebRequest req, string playerId, byte[] bodyRaw,
                                                bool allowInteractiveSessionMint, Action<string, long> done)
        {
            // async void is deliberate and contained, exactly as ClanChatSource.AttachAndSend
            // documents: the ONLY outcome is the callback, and every path is wrapped so a
            // throw can never escape to the Unity loop as an unobserved exception.
            string body = null;
            long status = 0;
            try
            {
                bool safeToSend = await BackendRequestSigner.TryAttachAsync(
                    req, playerId, bodyRaw, allowInteractiveSessionMint);
                if (!safeToSend)
                {
                    // ⛔ FALSE MEANS ABORT. The request is never sent; the screen says
                    // "not signed in" rather than leaving a button that does nothing.
                    FlowTrace.Warn(Sys, what + " NOT sent — could not attach auth (fail-closed).");
                }
                else
                {
                    try
                    {
                        await req.SendWebRequest();
                    }
                    catch (Exception ex)
                    {
                        FlowTrace.Warn(Sys, what + " threw on send: " + ex.GetType().Name + ": " + ex.Message);
                    }

                    body = req.downloadHandler != null ? req.downloadHandler.text : null;
                    if (req.result == UnityWebRequest.Result.Success ||
                        req.result == UnityWebRequest.Result.ProtocolError)
                    {
                        // A 4xx carries the refusal JSON in the body — that IS the answer.
                        status = req.responseCode;
                    }
                    else
                    {
                        // Transport failure: status 0, which the VM reads as "we could not
                        // ask" and renders by HOLDING the previous state.
                        FlowTrace.Warn(Sys, what + " transport failure: result=" + req.result);
                        status = 0;
                        body = null;
                    }
                }
            }
            catch (Exception ex)
            {
                FlowTrace.Fail(Sys, what + " path threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { req.Dispose(); } catch (Exception) { }
                Deliver(done, body, status);
            }
        }

        private static void Deliver(Action<string, long> done, string body, long status)
        {
            try { done?.Invoke(body, status); }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "Circle callback threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // =====================================================================
        // Payload helpers
        // =====================================================================

        /// <summary>
        /// The role verbs name their target as targetWallet, never a bare `wallet`:
        /// api/_lib/clan-http.js:224-236 prefers target/targetWallet, and a bare `wallet`
        /// is ALSO the claimed-identity field — the caller would target themselves.
        /// </summary>
        private string TargetPayload(string targetPlayerId)
        {
            return "{\"playerId\":" + JsonString(WalletAddress)
                 + ",\"targetWallet\":" + JsonString(targetPlayerId) + "}";
        }

        private static string JoinIds(string[] playerIds)
        {
            if (playerIds == null || playerIds.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            int count = 0;
            foreach (var id in playerIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (count >= 64) break;          // the route's own cap; asking for more is a 400
                if (count > 0) sb.Append(',');
                sb.Append(id);
                count++;
            }
            return sb.ToString();
        }

        private static string JsonStringArray(string[] values)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonString(values[i]));
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                // ⚠ A REAL REFUSAL PATH, NOT A CEREMONY. An empty id here builds
                // "?playerId=" — a URL the server answers with 400 PLAYER_ID_MISSING
                // (api/_lib/clan-http.js's preamble refuses before it ever looks at the
                // route). Traced HERE because this is the last place the client can still
                // see WHY the query was malformed; one line downstream it is just a 400 with
                // no cause attached, and that is a morning spent reading server logs.
                // No Step on the success path: this runs on every request and a per-call
                // trace would flood the boot window out of the device logcat ring
                // (CLAUDE.md §12, memory `logcat-ring-buffer-destroys-evidence`).
                FlowTrace.Warn(Sys, "a query parameter was built EMPTY — if it is playerId the " +
                                    "server refuses the call with PLAYER_ID_MISSING. (An empty " +
                                    "playerIds list is legitimate and answers an empty roster.)");
                return string.Empty;
            }
            return UnityWebRequest.EscapeURL(value);
        }

        /// <summary>
        /// Minimal JSON string literal, copied from ClanChatSource.JsonString so this file
        /// needs no Newtonsoft. Escapes the full control range: a raw control byte in a body
        /// is a 400 the player could never explain.
        /// </summary>
        private static string JsonString(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
