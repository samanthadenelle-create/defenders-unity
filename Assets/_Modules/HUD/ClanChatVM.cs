// =============================================================================
// ClanChatVM — the thin state holder behind ClanChatPanel (WO-1847).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ WHAT THIS CLASS STOPPED BEING. Until WO-1847 this was a full projection VM: it
// held the message list, the phrase-chip rail with its category dividers and its
// never-blank fallback, and the four ClanService write commands. All of it is gone,
// because CHERRY OWNS THE MESSAGES NOW. The embed renders them, persists them and
// delivers them; a projection here would be a second, always-stale copy of a list this
// game no longer has. What is left is exactly what the panel cannot get from the embed:
// which room to open, whether it opened, and the unread badge for the game's own HUD.
//
// Still a PURE state holder — no UnityEngine types at all, so Assets/Tests/EditMode/
// ClanChatVMTests.cs exercises it with a fake ISource, no scene and no network.
//
// ⚠ THE ONE OPEN WIRING GAP, NAMED RATHER THAN PAPERED OVER: RoomId is the clan's
// SERVER-SIDE UUID (clans.id, minted by api/clan/create.js). The client has no remote
// clan client yet — Core's ClanService is still the local-only prototype and mints its
// own local ids, which are NOT clans.id and must never be passed as a room. So ISource
// exposes RoomId as a nullable string and this VM refuses to build an embed URL without
// one (NoRoom below). Wiring /api/clan/me into a real remote clan client is a later
// ticket in this chain; when it lands, it feeds this one property and nothing else here
// changes.
// =============================================================================

using System;

namespace DeNelle.HUD
{
    /// <summary>
    /// Thin state for the Cherry-embedded clan chat panel: the room to open, the mount
    /// state, the unread badge, and the report command. Holds no messages — Cherry does.
    /// </summary>
    public sealed class ClanChatVM : IDisposable
    {
        // ── Seam over the identity + clan + report rails (fake in tests). ──────
        public interface ISource
        {
            /// <summary>Raised when RoomId or WalletAddress changes underneath us.</summary>
            event Action Changed;

            /// <summary>The proven wallet this player acts as, or null when not connected.</summary>
            string WalletAddress { get; }

            /// <summary>
            /// The clan's SERVER-SIDE UUID (clans.id), or null when unknown. ⛔ Never a
            /// local ClanService id — that id does not exist on the server and would open a
            /// room no other member of the clan is in.
            /// </summary>
            string RoomId { get; }

            /// <summary>The host page URL, room id already appended.</summary>
            string BuildEmbedUrl(string roomId);

            /// <summary>
            /// POST the report to /api/clan/report-message, signed. Fires <paramref name="done"/>
            /// with the outcome. The VM never builds a request or touches a signature itself.
            /// </summary>
            void ReportMessage(string clanId, string messageId, Action<bool> done);
        }

        /// <summary>Machine reason for the panel's error state. Never player-facing copy.</summary>
        public const string NoWallet = "no_wallet";
        /// <summary>See the header: the clan's server-side UUID is not known to the client yet.</summary>
        public const string NoRoom = "no_room";

        private readonly ISource _source;
        private readonly Action _onClose;
        private readonly Action _changedHandler;
        private bool _disposed;

        public static ClanChatVM CreateDefault(Action onClose)
            => new ClanChatVM(new ClanChatSource(), onClose);

        public ClanChatVM(ISource source, Action onClose)
        {
            _source = source;
            _onClose = onClose;
            if (_source != null)
            {
                _changedHandler = Refresh;
                _source.Changed += _changedHandler;
            }
            Refresh();
        }

        public event Action Changed;
        public string Title => "Remnant Chat";
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_source != null && _changedHandler != null) _source.Changed -= _changedHandler;
            Changed = null;
        }

        // ── State the View reads ──────────────────────────────────────────────

        /// <summary>The proven wallet, or null. Null is an error state, not an empty embed.</summary>
        public string WalletAddress { get; private set; }

        /// <summary>The clan's server-side UUID, or null when the client does not know it.</summary>
        public string RoomId { get; private set; }

        /// <summary>True once the embed reported a successful mount.</summary>
        public bool IsMounted { get; private set; }

        /// <summary>Unread messages, straight from Cherry's own unreadState event.</summary>
        public int UnreadCount { get; private set; }

        /// <summary>Machine reason the panel is in its error state, or null when fine.</summary>
        public string ErrorReason { get; private set; }

        /// <summary>True when the panel should show its error state instead of the embed.</summary>
        public bool HasError => !string.IsNullOrEmpty(ErrorReason);

        /// <summary>
        /// True only when a wallet AND a room are both known. The panel loads nothing
        /// otherwise — an embed opened without a validated room is how two clans end up
        /// sharing one chat.
        /// </summary>
        public bool CanEmbed => !string.IsNullOrEmpty(WalletAddress) && !string.IsNullOrEmpty(RoomId);

        /// <summary>The host page URL to load, or null when <see cref="CanEmbed"/> is false.</summary>
        public string EmbedUrl { get; private set; }

        // ── Events from the embed (the View forwards the bridge payloads) ──────

        /// <summary>The embed mounted. Clears any error state.</summary>
        public void OnMounted()
        {
            IsMounted = true;
            ErrorReason = null;
            Raise();
        }

        /// <summary>The embed (or the host) failed. Records the reason and drops the mount.</summary>
        public void OnError(string reason)
        {
            IsMounted = false;
            ErrorReason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            Raise();
        }

        /// <summary>Cherry's unread count for this room. Negatives are clamped, never trusted.</summary>
        public void OnUnread(int count)
        {
            UnreadCount = count < 0 ? 0 : count;
            Raise();
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>
        /// Report one message. No-op unless a room is known — a report carrying no clan
        /// cannot be inserted (clan_id is NOT NULL) and would be a guaranteed 400.
        /// </summary>
        public void ReportMessage(string messageId, Action<bool> done = null)
        {
            if (_source == null || string.IsNullOrEmpty(RoomId) || string.IsNullOrWhiteSpace(messageId))
            {
                done?.Invoke(false);
                return;
            }
            _source.ReportMessage(RoomId, messageId, done ?? (_ => { }));
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private void Refresh()
        {
            WalletAddress = _source?.WalletAddress;
            RoomId = _source?.RoomId;

            if (string.IsNullOrEmpty(WalletAddress))
            {
                ErrorReason = NoWallet;
                EmbedUrl = null;
            }
            else if (string.IsNullOrEmpty(RoomId))
            {
                ErrorReason = NoRoom;
                EmbedUrl = null;
            }
            else
            {
                ErrorReason = null;
                EmbedUrl = _source.BuildEmbedUrl(RoomId);
                // A source that cannot produce a URL is the same failure as no room: the
                // panel must not "succeed" into loading nothing.
                if (string.IsNullOrEmpty(EmbedUrl)) ErrorReason = NoRoom;
            }
            Raise();
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}
