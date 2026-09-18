// =============================================================================
// ClanMembershipClient — the ONE client caller of GET /api/clan/me (WO-1858).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ CLOSES THE ONE OPEN WIRING POINT ClanChatSource.cs NAMED (WO-1847): nothing called
// /api/clan/me and fed the answer into ClanRoomBinding, so the panel was permanently in
// its "no room" error state even for a wallet that IS in a clan. This file is the whole
// change — ClanChatVM / ClanChatSource / ClanChatPanel need no further edits beyond the
// one call site that triggers RefreshAsync on open (ClanChatPanel.SetVisible).
//
// RESPONSE SHAPE — read from api/clan/me.js at source, not assumed (WO-1845 header):
//   200 { ok:true, clan:null }                                    — real success, no clan
//   200 { ok:true, clan:{ clanId, code, name, tag, joinPolicy,
//                         createdAt, memberCount }, role, joinedAt } — real success, a clan
//   401 any auth refusal (fail-closed: BackendRequestSigner already refuses to send an
//       unauthed request, so this file never even reaches a 401 on a legitimate wallet)
//   500 SERVER_ERROR
//
// ⛔ NO NEW ASSEMBLY DEPENDENCY, same discipline as ClanChatSource.cs: DeNelle.HUD
// references DeNelle.Core + DeNelle.Data only, so the response is picked apart with a
// couple of narrow regexes rather than pulling in Newtonsoft. The two facts this file
// needs — "is clan null" and "what is clanId" — do not need a general JSON parser, and
// ClanRoomBinding.Set's own UUID-shape check is the final safety net if a regex ever
// captured something malformed.
//
// ⛔ A FAILED OR MALFORMED RESPONSE HOLDS THE PREVIOUS BINDING, IT NEVER CLEARS IT. Same
// rule HeartboundStatusClient.cs applies to a failed stake refresh: a transport hiccup or
// an unparseable body is "we could not ask," not "you have no clan." Only an explicit,
// well-formed `{ok:true, clan:null}` — which the server sends as a real, deliberate answer
// — clears the binding.
// =============================================================================

using System;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Backend;
using DeNelle.Core.Diagnostics;
using UnityEngine.Networking;

namespace DeNelle.HUD
{
    /// <summary>
    /// Fetches <c>GET /api/clan/me</c> for the current wallet and feeds the answer into
    /// <see cref="ClanRoomBinding"/>. Fire-and-forget from the UI (<see cref="RefreshAsync"/>
    /// returns a <see cref="UniTaskVoid"/>); every path through it is wrapped so a throw can
    /// never escape into the Unity loop.
    /// </summary>
    public static class ClanMembershipClient
    {
        private const string Sys = "ClanChat";
        private const string MeUrl = BackendRequestSigner.BackendBase + "/api/clan/me";
        private const int RequestTimeoutSeconds = 15;

        private static bool _inFlight;

        /// <summary>
        /// Ask the backend which clan (if any) the current wallet belongs to. No-op,
        /// fail-closed, for a guest or unproven identity — the clan routes require
        /// auth.mode === 'wallet' (api/_lib/clan-http.js), so a guest is a guaranteed 401
        /// and the VM's own NoWallet state already covers "no wallet" without a round trip.
        /// </summary>
        public static async UniTaskVoid RefreshAsync()
        {
            if (_inFlight)
            {
                FlowTrace.Step(Sys, "clan/me refresh already in flight; this call coalesced into it.");
                return;
            }

            string playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrWhiteSpace(playerId) || BackendRequestSigner.IsGuestIdentity(playerId))
            {
                FlowTrace.Step(Sys, "clan/me skipped — no wallet identity (fail-closed); " +
                                    "the panel's NoWallet state already names this.");
                return;
            }

            _inFlight = true;
            try
            {
                string url = MeUrl + "?playerId=" + UnityWebRequest.EscapeURL(playerId);
                using var req = UnityWebRequest.Get(url);
                req.timeout = RequestTimeoutSeconds;
                req.SetRequestHeader("Accept", "application/json");

                // GET body is empty — the wallet rail signs the literal "load" tag, same
                // shape HeartboundStatusClient.cs uses for its own read-only GET.
                bool safeToSend = await BackendRequestSigner.TryAttachAsync(req, playerId, null, false);
                if (!safeToSend)
                {
                    FlowTrace.Warn(Sys, "clan/me NOT sent — could not attach auth (fail-closed).");
                    return;
                }

                try
                {
                    await req.SendWebRequest();
                }
                catch (Exception ex)
                {
                    FlowTrace.Warn(Sys, "clan/me threw on send: " + ex.GetType().Name + ": " + ex.Message);
                    return;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    FlowTrace.Warn(Sys, "clan/me failed: http=" + req.responseCode + " result=" + req.result);
                    return;
                }

                string body = req.downloadHandler != null ? req.downloadHandler.text : null;
                Guard.Try(Sys, "ClanMembershipClient.Apply", () => ApplyResponseJson(body));
            }
            catch (Exception ex)
            {
                FlowTrace.Fail(Sys, "clan/me path threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                _inFlight = false;
            }
        }

        /// <summary>
        /// Parses one <c>GET /api/clan/me</c> response body and binds (or clears)
        /// <see cref="ClanRoomBinding"/>. Public (not internal) so
        /// Assets/Tests/EditMode/ClanMembershipClientTests.cs can drive it directly with the
        /// server's own documented response shapes — this is the ONE piece of real logic in
        /// this file worth locking, everything else is transport plumbing.
        /// </summary>
        public static void ApplyResponseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                FlowTrace.Warn(Sys, "clan/me: empty response body — holding the previous binding.");
                return;
            }
            if (!Regex.IsMatch(json, "\"ok\"\\s*:\\s*true"))
            {
                FlowTrace.Warn(Sys, "clan/me: response was not ok:true — holding the previous binding.");
                return;
            }
            if (Regex.IsMatch(json, "\"clan\"\\s*:\\s*null"))
            {
                // ⛔ A REAL, DELIBERATE SUCCESS — api/clan/me.js's own header: "NO CLAN IS A
                // 200, NOT A 404." Clearing here is correct, not a failure fallback.
                FlowTrace.Step(Sys, "clan/me: wallet is in no clan.");
                ClanRoomBinding.Set(null);
                return;
            }

            var m = Regex.Match(json, "\"clanId\"\\s*:\\s*\"([^\"]+)\"");
            if (!m.Success)
            {
                FlowTrace.Warn(Sys, "clan/me: ok:true response carried neither clan:null nor a clanId — " +
                                    "holding the previous binding rather than guessing.");
                return;
            }

            FlowTrace.Step(Sys, "clan/me: wallet's clan resolved — binding the real clans.id.");
            ClanRoomBinding.Set(m.Groups[1].Value);
        }
    }
}
