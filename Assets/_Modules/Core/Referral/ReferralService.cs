// =============================================================================
// ReferralService — WO6: Referral System.
// -----------------------------------------------------------------------------
// Manages the dual-sided referral flow:
//   • GENERATE: Fetches (or reuses) the player's unique referral code from the
//     backend. Code is cached in PlayerPrefs so it survives app restarts.
//   • SHARE: Opens the X (Twitter) share sheet with a pre-composed message
//     containing the referral link. Fires "referral_shared" event.
//   • CLAIM: Submits a referral code to api/referral/claim. Applies the
//     claimer reward (crystals) and triggers the referrer's backend reward.
//     Fires "referral_claimed" event.
//
// ABUSE PREVENTION (backend-enforced):
//   • Self-referral rejected: backend compares playerId == referrerPlayerId.
//   • One claim per player: backend checks referral_claims table.
//   • Referrer reward cap: backend caps referral rewards per referrer per period.
//   • Local guard: claimed code stored in PlayerPrefs to prevent double-submit.
//
// BACKEND CONTRACT (both routes are IDENTITY-GATED — see BackendRequestSigner;
// headers are X-Guest-Id, or X-Wallet + X-Nonce + X-Signature):
//   POST api/referral/generate
//     Body:    { playerId }
//     Success: { success: true, code, referralUrl }
//
//   POST api/referral/claim
//     Body:    { playerId, code }
//     Success: { success: true, reward: { crystals }, message }
//     Failure: { success: false, error: "SELF_REFERRAL" | "ALREADY_CLAIMED"
//                                      | "INVALID_CODE" | "CAP_REACHED" }
// =============================================================================

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Analytics;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.Backend;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Core.Referral
{
    [DisallowMultipleComponent]
    public sealed class ReferralService : MonoBehaviour
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const string GenerateUrl      = "https://defenders-of-the-realm-v2.vercel.app/api/referral/generate";
        private const string ClaimUrl         = "https://defenders-of-the-realm-v2.vercel.app/api/referral/claim";
        private const string CodePrefKey      = "dotr-referral-code";
        private const string UrlPrefKey       = "dotr-referral-url";
        private const string ClaimedPrefKey   = "dotr-referral-claimed"; // "1" when player has claimed someone else's code
        private const string ShareTextTemplate =
            "Join me in Defenders of the Realm! Use my code {CODE} to earn bonus Aether Crystals. {URL} #DefendersOfTheRealm";

        // ── Singleton ─────────────────────────────────────────────────────────

        private static ReferralService _instance;
        public  static ReferralService Instance => _instance;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("[ReferralService]");
            DontDestroyOnLoad(go);
            go.AddComponent<ReferralService>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadCachedCode();
        }

        // ── State ─────────────────────────────────────────────────────────────

        private string _myCode;
        private string _myReferralUrl;
        private bool   _hasClaimedReferral;

        public string MyCode        => _myCode;
        public string MyReferralUrl => _myReferralUrl;
        public bool   HasClaimed    => _hasClaimedReferral;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when the player's referral code/URL is ready.</summary>
        public event Action<string, string> OnCodeReady;        // (code, url)

        /// <summary>Fired when a referral code claim succeeds. Carries crystals awarded.</summary>
        public event Action<int, string>    OnClaimSuccess;     // (crystals, message)

        /// <summary>Fired when a referral code claim fails. Carries reason.</summary>
        public event Action<string>         OnClaimFailed;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Ensures the player has a referral code. Returns immediately if already
        /// cached; otherwise fetches from backend. Fires <see cref="OnCodeReady"/>.
        /// </summary>
        public async UniTask EnsureCodeAsync()
        {
            if (!string.IsNullOrEmpty(_myCode))
            {
                OnCodeReady?.Invoke(_myCode, _myReferralUrl);
                return;
            }

            // Identity-gated: the code minted here belongs to this player, so the
            // server must be able to PROVE who asked. No identity => no request.
            var playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogWarning("[ReferralService] No player identity - skipping code generation.");
                return;
            }
            var payload  = JsonConvert.SerializeObject(new { playerId });
            var bodyRaw  = Encoding.UTF8.GetBytes(payload);

            using var req = new UnityWebRequest(GenerateUrl, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (!await BackendRequestSigner.TryAttachAsync(req, playerId, bodyRaw))
            {
                Debug.LogWarning("[ReferralService] Could not authenticate the generate request - aborting.");
                return;
            }

            try { await req.SendWebRequest(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ReferralService] Generate failed: {ex.Message}");
                return;
            }

            if (req.result != UnityWebRequest.Result.Success) return;

            GenerateResponse resp;
            try { resp = JsonConvert.DeserializeObject<GenerateResponse>(req.downloadHandler.text); }
            catch (Exception e) { FlowTrace.Warn("Referral", $"Generate response deserialize threw: {e.GetType().Name}: {e.Message}"); return; }

            if (resp == null || !resp.Success) return;

            _myCode        = resp.Code;
            _myReferralUrl = resp.ReferralUrl;
            PlayerPrefs.SetString(CodePrefKey, _myCode);
            PlayerPrefs.SetString(UrlPrefKey,  _myReferralUrl);
            PlayerPrefs.Save();

            EventTracker.Track("referral_code_generated", new { code = _myCode });
            Debug.Log($"[ReferralService] Referral code ready: {_myCode}");
            OnCodeReady?.Invoke(_myCode, _myReferralUrl);
        }

        /// <summary>
        /// Opens X (Twitter) with a pre-composed referral share message.
        /// Fires "referral_shared" analytics event.
        /// </summary>
        public void ShareOnX()
        {
            if (string.IsNullOrEmpty(_myCode))
            {
                Debug.LogWarning("[ReferralService] No referral code — call EnsureCodeAsync first.");
                return;
            }

            string text = ShareTextTemplate
                .Replace("{CODE}", _myCode)
                .Replace("{URL}", _myReferralUrl ?? string.Empty);

            string encoded = Uri.EscapeDataString(text);
            Application.OpenURL($"https://x.com/intent/tweet?text={encoded}");

            EventTracker.Track("referral_shared", new { code = _myCode, platform = "X" });
        }

        /// <summary>
        /// Attempts to claim a referral code entered by the player.
        /// Fires <see cref="OnClaimSuccess"/> or <see cref="OnClaimFailed"/>.
        /// </summary>
        public async UniTask ClaimAsync(string code)
        {
            code = code?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(code))
            {
                OnClaimFailed?.Invoke("Enter a referral code.");
                return;
            }

            if (_hasClaimedReferral)
            {
                OnClaimFailed?.Invoke("You've already used a referral code.");
                return;
            }

            // Self-referral local guard (server also rejects this).
            if (!string.IsNullOrEmpty(_myCode) &&
                string.Equals(code, _myCode, StringComparison.OrdinalIgnoreCase))
            {
                OnClaimFailed?.Invoke("You can't use your own referral code.");
                return;
            }

            // A claim permanently consumes this player's ONE claim (UNIQUE(claimer_id)),
            // so the server must prove the claimer is who the body says.
            var playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                OnClaimFailed?.Invoke("Sign in before claiming a referral code.");
                return;
            }
            var payload  = JsonConvert.SerializeObject(new { playerId, code });
            var bodyRaw  = Encoding.UTF8.GetBytes(payload);

            using var req = new UnityWebRequest(ClaimUrl, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (!await BackendRequestSigner.TryAttachAsync(req, playerId, bodyRaw))
            {
                OnClaimFailed?.Invoke("Could not verify your account. Try again later.");
                return;
            }

            try { await req.SendWebRequest(); }
            catch (Exception ex)
            {
                OnClaimFailed?.Invoke($"Network error: {ex.Message}");
                return;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                OnClaimFailed?.Invoke(req.responseCode == 401 || req.responseCode == 400
                    ? "Could not verify your account. Try again later."
                    : "Could not reach server. Try again later.");
                return;
            }

            ClaimResponse resp;
            try { resp = JsonConvert.DeserializeObject<ClaimResponse>(req.downloadHandler.text); }
            catch (Exception e) { FlowTrace.Warn("Referral", $"Claim response deserialize threw: {e.GetType().Name}: {e.Message}"); OnClaimFailed?.Invoke("Unexpected server response."); return; }

            if (resp == null || !resp.Success)
            {
                OnClaimFailed?.Invoke(MapError(resp?.Error));
                return;
            }

            // Apply claimer reward. Crystals are unified onto Resources.Crystals (the
            // single wallet); route through GameStateService.AddCrystals (Core-internal —
            // CrystalEconomy lives in DeNelle.Village which Core cannot reference). This
            // clamps >= 0, persists, and raises ResourcesChanged.
            int crystals = resp.Reward?.Crystals ?? 0;
            if (crystals > 0)
                GameStateService.Instance?.AddCrystals(crystals);

            // Mark claimed.
            _hasClaimedReferral = true;
            PlayerPrefs.SetString(ClaimedPrefKey, "1");
            PlayerPrefs.Save();

            EventTracker.Track("referral_claimed", new
            {
                code,
                crystals,
                message = resp.Message,
            });

            Debug.Log($"[ReferralService] Referral claimed — code:{code} crystals:{crystals}");
            OnClaimSuccess?.Invoke(crystals, resp.Message ?? $"You received {crystals} Aether Crystals!");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void LoadCachedCode()
        {
            _myCode             = PlayerPrefs.GetString(CodePrefKey,    string.Empty);
            _myReferralUrl      = PlayerPrefs.GetString(UrlPrefKey,     string.Empty);
            _hasClaimedReferral = PlayerPrefs.GetString(ClaimedPrefKey, string.Empty) == "1";
        }

        private static string MapError(string errorCode) => errorCode switch
        {
            "SELF_REFERRAL"    => "You can't use your own referral code.",
            "ALREADY_CLAIMED"  => "You've already used a referral code.",
            "INVALID_CODE"     => "That referral code doesn't exist.",
            "CAP_REACHED"      => "This referral code has reached its limit.",
            _                  => "Claim failed. Please try again.",
        };

        // ── DTOs ──────────────────────────────────────────────────────────────

        private sealed class GenerateResponse
        {
            [JsonProperty("success")]     public bool   Success     { get; set; }
            [JsonProperty("code")]        public string Code        { get; set; }
            [JsonProperty("referralUrl")] public string ReferralUrl { get; set; }
        }

        private sealed class ClaimResponse
        {
            [JsonProperty("success")] public bool          Success { get; set; }
            [JsonProperty("reward")]  public ReferralReward Reward  { get; set; }
            [JsonProperty("message")] public string         Message { get; set; }
            [JsonProperty("error")]   public string         Error   { get; set; }
        }

        private sealed class ReferralReward
        {
            [JsonProperty("crystals")] public int Crystals { get; set; }
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Defenders/Debug/Log My Referral Code")]
        private static void EditorLogCode()
        {
            if (_instance == null) { Debug.LogWarning("[ReferralService] No instance in scene."); return; }
            Debug.Log($"[ReferralService] Code='{_instance._myCode}'  URL='{_instance._myReferralUrl}'");
        }
#endif
    }
}
