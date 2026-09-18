// =============================================================================
// ClanService — local-only stub for the Clans + Team-Chat feature ported from
// the React `docs/clans-and-team-chat-design.md`.
// -----------------------------------------------------------------------------
// SCOPE: LOCAL SINGLE-PLAYER STUB. The React repo talks to a real Vercel /
// Postgres backend; the Unity port does not yet. This service owns the same
// data model (clan, member, message, role) in PlayerPrefs so the HUD loop
// works in single-player and a network bridge can swap in later.
//
// Pattern model: Assets/_Modules/Core/Quests/DailyQuests.cs (singleton
// MonoBehaviour, [RuntimeInitializeOnLoadMethod] bootstrap, PlayerPrefs
// persistence, Changed event for UI repaint).
//
// What we PORT from the React spec:
//   • Clan { id, code, name, tag, joinPolicy, createdAt }
//   • ClanMember { id, displayName, role, joinedAt }
//   • ChatMessage { id, senderId, senderName, phraseId, text, sentAtUnix, isCustom }
//   • Role enum: leader | officer | member
//   • Ring-buffer chat log capped at 100 (matches §6.2 "last 100 messages per clan").
//   • A 140-char limit on custom text — same cap as the existing 1:1 mailbox.
//
// What we DO NOT port (network-dependent / out of stub scope):
//   • Server-side rank checks, rate limits, join cooldowns (§5.3, §8.2).
//   • Vercel-Postgres tables, clan invites, the public clan-lookup endpoint.
//   • Pending/Resting member states — single-player has one Account.
//   • Leader-succession logic — the local Account is always the leader.
//
// Spec deviations:
//   • The design says "no free text, ever" (§6.4). The owner asked for an
//     AddCustomMessage(text) entry point on this stub so a single-player can
//     try a real input box during playtest. The cap is 140 chars; profanity
//     filtering is intentionally OFF for the stub. When the network bridge
//     lands, AddCustomMessage will be removed or gated by a server flag —
//     the wire schema already supports `isCustom = false` only.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Core.Services
{
    public enum ClanRole
    {
        Leader,
        Officer,
        Member,
    }

    [Serializable]
    public sealed class ClanMember
    {
        public string Id;            // Account id (GUID for local) or invite code in the future.
        public string DisplayName;
        public ClanRole Role;
        public long JoinedAtUnix;
    }

    /// <summary>
    /// One team-chat message. Public mutable fields so Newtonsoft.Json round-
    /// trips the ring-buffer cleanly. `IsCustom = false` means the message
    /// came from the phrase catalogue (PhraseId set); `IsCustom = true` means
    /// the player typed it.
    /// </summary>
    [Serializable]
    public sealed class ChatMessage
    {
        public string Id;
        public string SenderId;
        public string SenderName;
        public string PhraseId;      // empty when IsCustom == true
        public string Text;          // resolved text (catalogue lookup or custom string)
        public long   SentAtUnix;
        public bool   IsCustom;
    }

    [Serializable]
    public sealed class ClanState
    {
        public string Id;
        public string Code;          // 6-char shareable code (placeholder generator)
        public string Name;
        public string Tag;           // 2–4 char clan tag shown in chat / HUD
        public string JoinPolicy;    // 'invite' | 'open' (server vocabulary, WO-1851; stub: always 'invite')
        public long   CreatedAtUnix;
        public List<ClanMember> Members = new List<ClanMember>();
        public List<ChatMessage> Messages = new List<ChatMessage>();
    }

    /// <summary>
    /// Singleton MonoBehaviour that holds the player's local clan + chat log
    /// and persists them to PlayerPrefs. Auto-bootstrapped before the first
    /// scene so any panel can read <see cref="Instance"/> safely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClanService : MonoBehaviour
    {
        // PlayerPrefs keys — versioned so a future schema bump can migrate.
        public const string PrefKeyClan      = "dotr-clans-v1";
        public const string PrefKeyAccountId = "dotr-account-id-v1";

        // Limits — match the React spec (§6.2 + 1:1 mailbox cap).
        public const int MessageBufferCap   = 100;
        public const int CustomTextMaxChars = 140;

        public static ClanService Instance { get; private set; }
        public event Action Changed;

        private ClanState _clan;          // null when not in a clan
        private string _accountId;        // local Account.Id GUID, persisted

        public bool InClan => _clan != null;

        public string AccountId
        {
            get
            {
                EnsureAccountId();
                return _accountId;
            }
        }

        public ClanState Current => _clan;

        /// <summary>Read-only view of the ring buffer (oldest first).</summary>
        public IReadOnlyList<ChatMessage> Messages
        {
            get
            {
                if (_clan == null) return System.Array.Empty<ChatMessage>();
                return _clan.Messages;
            }
        }

        // ── Bootstrap ────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("ClanService");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<ClanService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            EnsureAccountId();
            TryLoadClan();
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void CreateClan(string name, string tag)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Ember Wardens";
            if (string.IsNullOrWhiteSpace(tag))  tag  = "EMBR";
            tag = tag.Trim().ToUpperInvariant();
            if (tag.Length > 4) tag = tag.Substring(0, 4);

            EnsureAccountId();
            var now = NowUnix();
            _clan = new ClanState
            {
                Id            = Guid.NewGuid().ToString("N"),
                Code          = GenerateClanCode(),
                Name          = name.Trim(),
                Tag           = tag,
                JoinPolicy    = "invite",
                CreatedAtUnix = now,
                Members = new List<ClanMember>
                {
                    new ClanMember
                    {
                        Id           = _accountId,
                        DisplayName  = "You",
                        Role         = ClanRole.Leader,
                        JoinedAtUnix = now,
                    },
                },
                Messages = new List<ChatMessage>(),
            };
            Save();
            Changed?.Invoke();
        }

        public void LeaveClan()
        {
            if (_clan == null) return;
            _clan = null;
            Save();
            Changed?.Invoke();
        }

        /// <summary>
        /// Append a templated phrase by id. No-ops gracefully when the
        /// catalogue does not know the id, so a stale chip cannot crash the
        /// HUD.
        /// </summary>
        public void AddTemplatedMessage(string phraseId)
        {
            if (_clan == null) return;
            if (string.IsNullOrEmpty(phraseId)) return;
            var phrase = ChatPhraseCatalog.FindPhrase(phraseId);
            if (phrase == null)
            {
                Debug.LogWarning($"[ClanService] Unknown phraseId '{phraseId}' — ignoring.");
                return;
            }
            AppendMessage(new ChatMessage
            {
                Id          = NewMessageId(),
                SenderId    = _accountId,
                SenderName  = LocalDisplayName(),
                PhraseId    = phrase.Id,
                Text        = phrase.Text,
                SentAtUnix  = NowUnix(),
                IsCustom    = false,
            });
        }

        /// <summary>
        /// Append a custom (free-text) message. Capped at 140 chars; profanity
        /// filtering is intentionally OFF for the stub. The wire schema
        /// already has an `IsCustom` flag so the server can refuse customs
        /// when the network bridge lands.
        /// </summary>
        public void AddCustomMessage(string text)
        {
            if (_clan == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            var trimmed = text.Trim();
            if (trimmed.Length > CustomTextMaxChars)
                trimmed = trimmed.Substring(0, CustomTextMaxChars);

            AppendMessage(new ChatMessage
            {
                Id          = NewMessageId(),
                SenderId    = _accountId,
                SenderName  = LocalDisplayName(),
                PhraseId    = string.Empty,
                Text        = trimmed,
                SentAtUnix  = NowUnix(),
                IsCustom    = true,
            });
        }

        // ── Internals ────────────────────────────────────────────────────────

        private void AppendMessage(ChatMessage msg)
        {
            if (_clan == null || msg == null) return;
            _clan.Messages.Add(msg);
            // Ring buffer: drop oldest until we are back under the cap.
            while (_clan.Messages.Count > MessageBufferCap)
                _clan.Messages.RemoveAt(0);
            Save();
            Changed?.Invoke();
        }

        private string LocalDisplayName()
        {
            if (_clan == null) return "You";
            foreach (var m in _clan.Members)
                if (m != null && m.Id == _accountId) return m.DisplayName ?? "You";
            return "You";
        }

        private void EnsureAccountId()
        {
            if (!string.IsNullOrEmpty(_accountId)) return;
            if (PlayerPrefs.HasKey(PrefKeyAccountId))
            {
                _accountId = PlayerPrefs.GetString(PrefKeyAccountId);
                if (!string.IsNullOrEmpty(_accountId)) return;
            }
            _accountId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PrefKeyAccountId, _accountId);
            PlayerPrefs.Save();
        }

        private void TryLoadClan()
        {
            if (!PlayerPrefs.HasKey(PrefKeyClan)) return;
            try
            {
                var raw = PlayerPrefs.GetString(PrefKeyClan);
                if (string.IsNullOrEmpty(raw)) return;
                _clan = JsonConvert.DeserializeObject<ClanState>(raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ClanService] load failed: " + ex.Message);
                _clan = null;
            }
        }

        private void Save()
        {
            try
            {
                if (_clan == null)
                {
                    PlayerPrefs.DeleteKey(PrefKeyClan);
                }
                else
                {
                    PlayerPrefs.SetString(PrefKeyClan, JsonConvert.SerializeObject(_clan));
                }
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ClanService] save failed: " + ex.Message);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static long NowUnix() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string NewMessageId() =>
            "cmsg_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        // 6-char clan code from the spec alphabet (no O/0/I/1).
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly System.Random _codeRng = new System.Random();
        private static string GenerateClanCode()
        {
            var buf = new char[6];
            lock (_codeRng)
            {
                for (int i = 0; i < buf.Length; i++)
                    buf[i] = CodeAlphabet[_codeRng.Next(CodeAlphabet.Length)];
            }
            return new string(buf);
        }
    }
}
