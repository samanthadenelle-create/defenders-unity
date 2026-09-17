// =============================================================================
// ClanChatSource — the LIVE ClanChatVM.ISource (WO-1847), plus the room binding.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// Split out of ClanChatVM.cs on purpose: the VM stays a PURE state holder with no
// UnityEngine types so the EditMode tests need no scene, and every Unity/network
// dependency the live rail actually has lives here instead.
//
// ⛔ THE SIGNED CALL LIVES ON THIS SIDE OF THE BRIDGE, ALWAYS. site/clan-chat.html
// never calls the game's backend and is never handed a key, a session or a signature —
// it bridges "the player reported this" to C#, and C# signs and sends. A page that could
// call an authenticated endpoint itself would be a signing surface inside a document
// loaded from a CDN-fed third-party embed, which is the one thing wallet-only mode exists
// to avoid.
//
// ⛔ NO NEW ASSEMBLY DEPENDENCY. The POST body is built by hand (three fields) and the
// response is not parsed at all — only the status is read — so this file needs neither
// Newtonsoft nor a change to DeNelle.HUD.asmdef, which references DeNelle.Core and
// DeNelle.Data only.
// =============================================================================

using System;
using System.Text;
// Required: the awaiter for UnityWebRequestAsyncOperation is UniTask's UnityAsyncExtensions,
// NOT a built-in. Without this using, `await req.SendWebRequest()` does not compile — the
// same using ReferralService.cs:34 carries for the identical call.
using Cysharp.Threading.Tasks;
using DeNelle.Core.Backend;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.HUD
{
    /// <summary>
    /// The clan room the chat embed should open, as a server-side clans.id UUID.
    /// </summary>
    /// <remarks>
    /// ⚠ THIS IS THE ONE OPEN WIRING POINT OF WO-1847, and it is named rather than faked.
    /// A Cherry room is scoped by the clan's SERVER-SIDE UUID (clans.id, minted by
    /// api/clan/create.js). The client has no remote clan client yet — Core's ClanService
    /// is still the local-only prototype and mints its own local ids, which do not exist
    /// on the server. Passing one as a room would open a room no other member of the clan
    /// is in: chat that looks like it works and reaches nobody.
    ///
    /// So nothing sets this today and the panel shows its error state. When a later ticket
    /// wires /api/clan/me into a real remote clan client, it calls <see cref="Set"/> with
    /// the clanId that endpoint returns and the embed starts working — that is the whole
    /// change on this side.
    /// </remarks>
    public static class ClanRoomBinding
    {
        private static string _clanId;

        /// <summary>Raised whenever the bound clan changes (including to null on leave).</summary>
        public static event Action Changed;

        /// <summary>The bound clans.id UUID, or null when the client does not know it.</summary>
        public static string ClanId => _clanId;

        /// <summary>
        /// Bind (or clear, with null) the clan room. Validates the UUID SHAPE here so a
        /// local-prototype id can never reach the embed as a room — the failure that would
        /// otherwise be invisible until two players compared empty chats.
        /// </summary>
        public static void Set(string clanId)
        {
            var next = string.IsNullOrWhiteSpace(clanId) ? null : clanId.Trim();
            if (next != null && !LooksLikeUuid(next))
            {
                FlowTrace.Warn("ClanChat", "ClanRoomBinding.Set refused a non-UUID clan id — a local " +
                                           "ClanService id is NOT clans.id and would open an empty room.");
                next = null;
            }
            if (string.Equals(_clanId, next, StringComparison.Ordinal)) return;
            _clanId = next;
            FlowTrace.Step("ClanChat", "ClanRoomBinding.Set -> " + (next == null ? "<none>" : "bound"));
            Changed?.Invoke();
        }

        /// <summary>8-4-4-4-12 hex, the shape Postgres accepts for a UUID.</summary>
        public static bool LooksLikeUuid(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 36) return false;
            for (int i = 0; i < 36; i++)
            {
                char c = s[i];
                if (i == 8 || i == 13 || i == 18 || i == 23)
                {
                    if (c != '-') return false;
                    continue;
                }
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The live <see cref="ClanChatVM.ISource"/>: wallet from the backend signer, room from
    /// <see cref="ClanRoomBinding"/>, report through a signed POST.
    /// </summary>
    public sealed class ClanChatSource : ClanChatVM.ISource
    {
        // Same origin every other client service posts to (mirrors ReferralService's
        // GenerateUrl/ClaimUrl constants rather than inventing a second base-URL scheme).
        private const string Origin = "https://defenders-of-the-realm-v2.vercel.app";
        private const string HostPageUrl = Origin + "/clan-chat.html";
        private const string ReportUrl = Origin + "/api/clan/report-message";

        public event Action Changed
        {
            add { ClanRoomBinding.Changed += value; }
            remove { ClanRoomBinding.Changed -= value; }
        }

        /// <summary>
        /// The proven wallet. ⛔ A GUEST IDENTITY IS NOT A WALLET here: every clan route
        /// requires auth.mode === 'wallet' (api/_lib/clan-http.js narrows to it because the
        /// clan foreign keys point at wallet_identity), so a guest would be a guaranteed 401.
        /// Reporting that as "no wallet" locally is legible; sending it is not.
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

        public string RoomId => ClanRoomBinding.ClanId;

        public string BuildEmbedUrl(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            return HostPageUrl + "?roomId=" + UnityWebRequest.EscapeURL(roomId);
        }

        public void ReportMessage(string clanId, string messageId, Action<bool> done)
        {
            Guard.Try("ClanChat", "ReportMessage", () => SendReport(clanId, messageId, done));
        }

        private void SendReport(string clanId, string messageId, Action<bool> done)
        {
            var playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                FlowTrace.Warn("ClanChat", "report skipped — no player identity (fail-closed).");
                done?.Invoke(false);
                return;
            }

            var payload = "{\"playerId\":" + JsonString(playerId)
                        + ",\"clanId\":" + JsonString(clanId)
                        + ",\"messageId\":" + JsonString(messageId) + "}";
            var bodyRaw = Encoding.UTF8.GetBytes(payload);

            // A coroutine host is needed to await the request; the panel is a MonoBehaviour
            // but this source must not depend on it, so the request is driven by the
            // completion callback instead of a coroutine.
            var req = new UnityWebRequest(ReportUrl, "POST");
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            AttachAndSend(req, playerId, bodyRaw, done);
        }

        private static async void AttachAndSend(UnityWebRequest req, string playerId, byte[] bodyRaw, Action<bool> done)
        {
            // async void is deliberate and contained: this is a fire-and-forget report whose
            // ONLY outcome is the callback, and every path through it is wrapped so a throw
            // can never escape to the Unity loop as an unobserved exception.
            bool ok = false;
            try
            {
                if (!await BackendRequestSigner.TryAttachAsync(req, playerId, bodyRaw))
                {
                    FlowTrace.Warn("ClanChat", "report NOT sent — could not attach auth (fail-closed).");
                }
                else
                {
                    try
                    {
                        await req.SendWebRequest();
                        ok = req.result == UnityWebRequest.Result.Success;
                        if (!ok)
                        {
                            FlowTrace.Warn("ClanChat", "report failed: http=" + req.responseCode +
                                                       " result=" + req.result);
                        }
                        else
                        {
                            FlowTrace.Step("ClanChat", "report accepted by the server (200).");
                        }
                    }
                    catch (Exception ex)
                    {
                        FlowTrace.Warn("ClanChat", "report threw on send: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("ClanChat", "report path threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { req.Dispose(); } catch (Exception) { }
                try { done?.Invoke(ok); } catch (Exception ex)
                {
                    FlowTrace.Warn("ClanChat", "report callback threw: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Minimal JSON string literal. Hand-rolled so this file needs no Newtonsoft
        /// reference; it escapes the full set JSON requires, including the control range,
        /// because a raw control byte in a body would be a 400 the player could not explain.
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
