// =============================================================================
// PromoCodeService — WO5: Promo Code Redemption.
// -----------------------------------------------------------------------------
// Manages the full redemption lifecycle for operator-issued promo codes:
//   1. Client validates the code has not already been redeemed locally.
//   2. POSTs to api/promo/redeem — backend enforces one-time-use globally.
//   3. On 200 OK: applies reward (crystals + coins) + fires analytics event.
//
// BACKEND CONTRACT:
//   POST api/promo/redeem   (IDENTITY-GATED — see BackendRequestSigner)
//   Headers: X-Guest-Id, or X-Wallet + X-Nonce + X-Signature
//   Body:    { playerId, code }
//   Success: { success: true, reward: { crystals, coins }, message }
//   Failure: { success: false, error: "INVALID_CODE" | "ALREADY_REDEEMED"
//                                   | "EXPIRED" | "PLAYER_LIMIT_REACHED" }
//
// LOCAL DEDUP:
//   Redeemed codes are stored in PlayerPrefs key "dotr-redeemed-promos" as a
//   comma-separated list. This is a UX guard only — the backend is the source
//   of truth for one-time enforcement.
//
// INSPECTOR SETUP: none — add to a persistent scene GO or bootstrap via
//   PromoCodeService.EnsureExists() from GameStateService.Awake().
//
// ── UI DOOR (promo-redeem door WO) ───────────────────────────────────────────
//   The player-facing entry is RedeemCodePanel (Assets/_Modules/Wallet/
//   RedeemCodePanel.cs), opened from the Realm Store. It is a code-built Obsidian
//   uGUI panel and it drives THIS service — it never speaks HTTP itself.
//   ⛔ PromoCodeUI.cs (same folder) is a UIDocument panel and is DEAD — UXML does
//   not render in player builds (CLAUDE.md §8). Do not wire it.
//
// ── THREE RULES THIS FILE NOW KEEPS (promo-redeem door WO) ───────────────────
//   1. ⛔ THE CODE STRING IS NEVER LOGGED. Not Debug.Log, not FlowTrace, not
//      analytics. F8 captures get shared; a live promo code inside one is a leak
//      anybody who reads the capture can spend. Trace the OUTCOME, never the input.
//   2. Every player-facing sentence comes from canon-strings.json via PromoStrings
//      (CLAUDE.md §7) — no sentence is typed inline here, and each documented
//      failure has its own, so a redeem screen never says a bare "invalid code".
//   3. The reward lands through the SAME seam a purchased pack uses —
//      EconomyService.GrantSpendablePurchased / AddCoins (BankGrantKind.
//      PurchasedOrPromised), which is deliberately NEVER clamped by the town bank
//      cap (WO-857 Phase F: a pack once advertised 5,000 food and delivered 1,920).
//      A promo grant must land in full for exactly the same reason. There is no
//      second grant path — see ApplyReward.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Analytics;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.Backend;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Core.Promo
{
    /// <summary>
    /// Singleton MonoBehaviour that drives promo code redemption.
    /// Subscribe to <see cref="OnRedeemed"/> or <see cref="OnRedeemFailed"/>
    /// to react in UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PromoCodeService : MonoBehaviour
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const string BackendRedeemUrl = "https://defenders-of-the-realm-v2.vercel.app/api/promo/redeem";
        private const string PlayerPrefsKey   = "dotr-redeemed-promos";

        // ── Singleton ─────────────────────────────────────────────────────────

        private static PromoCodeService _instance;
        public  static PromoCodeService Instance => _instance;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("[PromoCodeService]");
            DontDestroyOnLoad(go);
            go.AddComponent<PromoCodeService>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadRedeemedSet();
        }

        // ── State ─────────────────────────────────────────────────────────────

        private readonly HashSet<string> _redeemedLocally = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when a code is successfully redeemed. Carries the reward.</summary>
        public event Action<PromoReward> OnRedeemed;

        /// <summary>
        /// Fired only after a confirmed server success and local grant. The code is kept in memory
        /// for code-specific UI such as the First Watch thank-you letter; subscribers must never log it.
        /// </summary>
        public event Action<string, PromoReward> OnCodeRedeemed;

        /// <summary>Fired when redemption fails. Carries a human-readable reason.</summary>
        public event Action<string>      OnRedeemFailed;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Returns true when the code is already in the local dedup set.</summary>
        public bool IsAlreadyRedeemedLocally(string code) =>
            !string.IsNullOrWhiteSpace(code) && _redeemedLocally.Contains(code.Trim().ToUpperInvariant());

        /// <summary>
        /// Attempts to redeem <paramref name="code"/>. Fires <see cref="OnRedeemed"/>
        /// or <see cref="OnRedeemFailed"/> on completion.
        /// </summary>
        public async UniTask RedeemAsync(string code)
        {
            // Uppercased here so the whole pipeline (local dedup set, request body, server
            // compare) agrees on one casing — the endpoint stores and compares uppercase.
            code = code?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(code))
            {
                Refuse("empty-input", PromoStrings.KeyErrEmpty);
                return;
            }

            if (_redeemedLocally.Contains(code))
            {
                Refuse("already-redeemed (local dedup)", PromoStrings.KeyErrAlreadyUsed);
                return;
            }

            // The backend keys the redemption on this id and now PROVES it (see
            // BackendRequestSigner / api/_lib/wallet-auth.js). "anonymous" is not a
            // shape the server accepts, so a player with no identity cannot redeem
            // — which is correct: an unproven id let anyone burn a victim's code.
            var playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                Refuse("no-identity", PromoStrings.KeyErrSignIn);
                return;
            }

            // Keep the legacy flag during the transition for older server surfaces, but only the
            // explicitly named inline flag authorizes a DB contents response in the new endpoint.
            var payload = JsonConvert.SerializeObject(new
                { playerId, code, supportsPackRewards = true, supportsInlinePackRewards = true });
            var bodyRaw = Encoding.UTF8.GetBytes(payload);

            using var req = new UnityWebRequest(BackendRedeemUrl, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            // Identity proof over the EXACT bytes above. Fail-closed: on refusal we
            // abort rather than send a request the server will (rightly) 401.
            // Redeem is an explicit player press, so a cold/expired auth session may open the
            // wallet handshake once. Passive/background callers keep the default false and remain
            // silent; the code is not sent or consumed unless the handshake succeeds.
            if (!await BackendRequestSigner.TryAttachAsync(req, playerId, bodyRaw,
                    allowInteractiveSessionMint: true))
            {
                Refuse("identity-proof-refused (signer)", PromoStrings.KeyErrIdentity);
                return;
            }

            try
            {
                await req.SendWebRequest();
            }
            catch (Exception ex)
            {
                // The exception TEXT is diagnostics, the code is not — trace one, never the other.
                Refuse($"network-exception {ex.GetType().Name}: {ex.Message}", PromoStrings.KeyErrOffline);
                return;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                // The route is identity-gated now, so a refusal is a distinct case
                // from an unreachable server - say which so nobody hunts the wrong bug.
                bool refused = req.responseCode == 401 || req.responseCode == 400;
                Refuse($"transport http {req.responseCode} ({req.result})",
                    refused ? PromoStrings.KeyErrIdentity : PromoStrings.KeyErrOffline);
                return;
            }

            RedeemResponse resp;
            try
            {
                resp = JsonConvert.DeserializeObject<RedeemResponse>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                // No silent catch (§12) — and the body is NOT echoed: it is the server's reply to a
                // request that carried the code, so quoting it risks quoting the code back into a log.
                Refuse($"unparseable-response {ex.GetType().Name}", PromoStrings.KeyErrUnknown);
                return;
            }

            if (resp == null || !resp.Success)
            {
                string errorCode = resp?.Error;
                Refuse($"server {errorCode ?? "<no error field>"}", MapErrorKey(errorCode));
                return;
            }

            // ── Apply reward ──────────────────────────────────────────────────
            var reward = resp.Reward ?? new PromoReward();
            if (!ApplyReward(reward))
            {
                Refuse("reward-application-failed-after-server-success", PromoStrings.KeyErrUnknown);
                return;
            }

            // ── Local dedup ───────────────────────────────────────────────────
            _redeemedLocally.Add(code);
            PersistRedeemedSet();

            // ── Analytics ─────────────────────────────────────────────────────
            // ⛔ The code string is NOT in this payload and must never be added back. Analytics
            // events land in logs and captures; the server already knows which code it burned.
            EventTracker.Track("promo_redeemed", new
            {
                crystals = reward.Crystals,
                coins    = reward.Coins,
                hasPack  = !string.IsNullOrEmpty(reward.PackSku),
            });

            FlowTrace.Step("Promo", $"redeem OUTCOME=redeemed — crystals:{reward.Crystals} coins:{reward.Coins} (entry withheld by design).");
            OnRedeemed?.Invoke(reward);
            OnCodeRedeemed?.Invoke(code, reward);
        }

        /// <summary>
        /// The single refusal exit: traces the OUTCOME (never the code) and raises
        /// <see cref="OnRedeemFailed"/> with the canon sentence for that cause.
        /// </summary>
        private void Refuse(string outcome, string canonKey)
        {
            FlowTrace.Warn("Promo", $"redeem OUTCOME=refused [{outcome}] (entry withheld by design).");
            OnRedeemFailed?.Invoke(PromoStrings.Get(canonKey));
        }

        // ── Reward application ────────────────────────────────────────────────

        /// <summary>
        /// Lands the reward through the SAME seam a purchased pack uses:
        /// <c>EconomyService.GrantSpendablePurchased(wood,food,iron,crystals)</c> for crystals and
        /// <c>EconomyService.AddCoins(int)</c> for coins — the <c>BankGrantKind.PurchasedOrPromised</c>
        /// path that is deliberately NOT clamped by the town bank cap (WO-857 Phase F). A promised
        /// quantity must arrive in full, and a promo code promises exactly as loudly as a pack card.
        /// <para>⛔ Do NOT "simplify" this back to writing <c>state.Resources</c> directly (what it did
        /// before the promo-redeem door WO). That bypassed the cap logic, the persist and the HUD
        /// refresh alike — it happened to add the numbers, but it was a SECOND grant path, and the
        /// pack seam is the one the regressions pin.</para>
        /// <para>EconomyService lives in DeNelle.Village, which DeNelle.Core may not reference (read
        /// the .asmdef — CLAUDE.md §5), so it is resolved by reflection exactly the way PackStoreVM
        /// resolves it for the paid path. Reflection here is EVIDENCE of the assembly rule, not a
        /// violation of it. Every failure is FAIL-level: the server has already burned the code, so a
        /// lost grant means the player spent a one-time code for nothing.</para>
        /// </summary>
        private static bool ApplyReward(PromoReward reward)
        {
            int crystals = Mathf.Max(0, reward != null ? reward.Crystals : 0);
            int coins    = Mathf.Max(0, reward != null ? reward.Coins    : 0);
            string packSku = reward != null ? reward.PackSku?.Trim() : null;
            bool hasInlineContents = reward?.Contents != null && reward.Contents.Type == JTokenType.Object;
            if (crystals <= 0 && coins <= 0 && string.IsNullOrEmpty(packSku))
            {
                FlowTrace.Warn("Promo", "reward carried no crystals and no coins — nothing to grant.");
                return false;
            }

            if (!string.IsNullOrEmpty(packSku))
            {
                // Pack responses are all-or-nothing. Top-level crystals/coins are audit mirrors of
                // contents.economy, so applying both would double-grant. Missing contents fails closed;
                // never fall back to the APK's baked PackCatalog.
                if (!hasInlineContents || !TryApplyInlinePack(packSku, reward.Contents)) return false;
                return true;
            }
            if (crystals <= 0 && coins <= 0) return true;

            var econ = ResolveEconomyService(out var type);
            if (econ == null || type == null)
            {
                FlowTrace.Fail("Promo",
                    $"grant (crystals {crystals} / coins {coins}) FAILED: EconomyService not resolvable — " +
                    "the redemption is ALREADY BURNED server-side, so this reward is LOST.");
                return false;
            }

            if (crystals > 0)
            {
                // Signature order is (wood, food, iron, crystals) — see EconomyService.
                var m = type.GetMethod("GrantSpendablePurchased",
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
                if (m == null)
                    FlowTrace.Fail("Promo",
                        "grant crystals FAILED: GrantSpendablePurchased(int,int,int,int) not found. " +
                        "NOT falling back to the CAPPED GrantSpendable — a promised promo reward that " +
                        "silently under-delivers is the WO-857 Phase F defect. Reward LOST, redemption already burned.");
                else
                    try { m.Invoke(econ, new object[] { 0, 0, 0, crystals }); }
                    catch (Exception ex) { FlowTrace.Fail("Promo", $"grant crystals THREW: {ex.GetType().Name}: {ex.Message} — reward LOST, redemption already burned."); }
            }

            if (coins > 0)
            {
                var m = type.GetMethod("AddCoins", new[] { typeof(int) });
                if (m == null)
                    FlowTrace.Fail("Promo", "grant coins FAILED: AddCoins(int) not found — reward LOST, redemption already burned.");
                else
                    try { m.Invoke(econ, new object[] { coins }); }
                    catch (Exception ex) { FlowTrace.Fail("Promo", $"grant coins THREW: {ex.GetType().Name}: {ex.Message} — reward LOST, redemption already burned."); }
            }
            return true;
        }

        private static bool TryApplyInlinePack(string packSku, JObject contents)
        {
            Type contentsType = null, vmType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (contentsType == null) contentsType = asm.GetType("DeNelle.Wallet.PackContents");
                if (vmType == null) vmType = asm.GetType("DeNelle.Wallet.PackStoreVM");
                if (contentsType != null && vmType != null) break;
            }
            if (contentsType == null || vmType == null)
            {
                FlowTrace.Fail("Promo", "inline pack reward FAILED: Wallet pack types are not loaded; redemption already burned.");
                return false;
            }
            try
            {
                var hydrated = contents.ToObject(contentsType, JsonSerializer.CreateDefault());
                if (hydrated == null) return false;
                var vm = vmType.GetMethod("CreateDefault", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
                var apply = vmType.GetMethod("ApplyPackContents", new[] { typeof(string), contentsType });
                if (vm == null || apply == null) return false;
                apply.Invoke(vm, new[] { (object)packSku, hydrated });
                var isOwned = vmType.GetMethod("IsOwned", new[] { typeof(string) });
                return isOwned != null && (bool)isOwned.Invoke(vm, new object[] { packSku });
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("Promo", $"inline pack reward THREW: {ex.GetType().Name}: {ex.Message} - redemption already burned.");
                return false;
            }
        }

        /// <summary>Mirrors PackStoreVM.ResolveServiceInstance — the Village service seen from Core.</summary>
        private static object ResolveEconomyService(out Type type)
        {
            type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("DeNelle.Village.EconomyService");
                if (type != null) break;
            }
            if (type == null) return null;
            return type.GetProperty("Instance",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)?.GetValue(null);
        }

        // ── Error mapping ─────────────────────────────────────────────────────

        /// <summary>
        /// Maps a documented backend error to its OWN canon key. Every branch is distinct on purpose:
        /// a redeem screen that answers every failure with one vague line reads as a scam, and the
        /// player cannot tell a typo from a spent code from an outage.
        /// </summary>
        private static string MapErrorKey(string errorCode) => errorCode switch
        {
            "INVALID_CODE"         => PromoStrings.KeyErrInvalid,
            "ALREADY_REDEEMED"     => PromoStrings.KeyErrAlreadyUsed,
            "EXPIRED"              => PromoStrings.KeyErrExpired,
            "PLAYER_LIMIT_REACHED" => PromoStrings.KeyErrPlayerLimit,
            _                      => PromoStrings.KeyErrUnknown,
        };

        // ── PlayerPrefs persistence ───────────────────────────────────────────

        private void LoadRedeemedSet()
        {
            _redeemedLocally.Clear();
            var raw = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var code in raw.Split(','))
                if (!string.IsNullOrWhiteSpace(code))
                    _redeemedLocally.Add(code.Trim());
        }

        private void PersistRedeemedSet()
        {
            PlayerPrefs.SetString(PlayerPrefsKey, string.Join(",", _redeemedLocally));
            PlayerPrefs.Save();
        }

        // ── DTOs ──────────────────────────────────────────────────────────────

        private sealed class RedeemResponse
        {
            [JsonProperty("success")] public bool         Success { get; set; }
            [JsonProperty("reward")]  public PromoReward  Reward  { get; set; }
            [JsonProperty("message")] public string       Message { get; set; }
            [JsonProperty("error")]   public string       Error   { get; set; }
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Defenders/Debug/Simulate Promo Redeem (TEST10)")]
        private static void EditorSimulateRedeem()
        {
            if (_instance == null) { Debug.LogWarning("[PromoCodeService] No instance in scene."); return; }
            _instance.RedeemAsync("TEST10").Forget();
        }
#endif
    }

    /// <summary>Reward payload returned by the backend on successful redemption.</summary>
    [Serializable]
    public sealed class PromoReward
    {
        [JsonProperty("crystals")] public int Crystals { get; set; }
        [JsonProperty("coins")]    public int Coins    { get; set; }
        [JsonProperty("packSku")]  public string PackSku { get; set; }
        [JsonProperty("contents")] public JObject Contents { get; set; }
        [JsonProperty("message")]  public string Message { get; set; }
    }
}
