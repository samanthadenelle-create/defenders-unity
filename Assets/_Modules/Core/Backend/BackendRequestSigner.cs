// =============================================================================
// BackendRequestSigner — the ONE client-side auth-header attach for backend POSTs
// -----------------------------------------------------------------------------
// The backend's identity gate (api/_lib/wallet-auth.js `authenticate`) routes by
// the SHAPE of the playerId being acted on, never by which headers arrive:
//
//   WALLET RAIL  base58 32-44 id  -> X-Wallet + X-Nonce + X-Signature, an ed25519
//                signature over the canonical message
//                    dotr-save:v1:<wallet>:<nonce>:<sha256-hex-of-body>
//                with a single-use, server-burned nonce.
//   GUEST RAIL   "guest-local-<64 hex>" -> X-Guest-Id only. The id IS the
//                credential; the server rate-limits it and marks the row guest.
//
// WHY THIS FILE EXISTS: that attach logic previously lived ONLY inside
// GameStateService.TryAttachAuthHeaders, private to the save/load pipeline. Every
// OTHER route that acts on a player identity (promo redeem, referral generate,
// referral claim) therefore posted a bare playerId with no proof at all, so any
// caller could name a victim's id. Rather than a second auth scheme, those routes
// now call THIS — the identical protocol, the identical canonical message, the
// identical fail-closed rule.
//
// FAIL CLOSED: every method returns false when it cannot produce the proof the
// rail demands, and the caller MUST abort the request rather than send it
// unauthed. An unauthed request on a gated route is a guaranteed 401 anyway; the
// difference is that aborting never leaves the player wondering why nothing
// happened while a forged-identity path stays open.
//
// Mirrors: api/_lib/wallet-auth.js (authenticate / buildSignedMessage / verifyGuest)
//          GameStateService.TryAttachAuthHeaders (save+load; unchanged)
// =============================================================================

using System;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Diagnostics;   // FlowTrace - the connect-time session warm-up traces its outcome
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace DeNelle.Core.Backend
{
    /// <summary>
    /// Attaches the backend's identity headers to an outgoing UnityWebRequest.
    /// Shared by every route that acts on a player identity.
    /// </summary>
    public static class BackendRequestSigner
    {
        /// <summary>Root of the deployed serverless API.</summary>
        public const string BackendBase = "https://defenders-of-the-realm-v2.vercel.app";

        private const string NonceUrl = BackendBase + "/api/auth/nonce";
        private const string SessionUrl = BackendBase + "/api/auth/session";

        // ── WO-1157: the cached session ──────────────────────────────────────────────
        // Buying one pack used to prompt the wallet THREE times: MWA connect, an auth
        // signature per backend call, and the transfer. Only the transfer should ever be
        // seen. The other two are session setup, and a session is what we were missing.
        //
        // ⛔ IN MEMORY ONLY, DELIBERATELY. This is a bearer credential: whoever holds it
        // speaks for the wallet until it expires. MwaSessionStore had to seal its token
        // with AES-GCM precisely because PlayerPrefs on Android is readable by a backup;
        // rather than repeat that machinery for a 15-minute token, we simply never write
        // it down. The cost is one extra signature after an app restart. That is the
        // right trade for a credential this powerful.
        //
        // ⛔ AND IT IS SCOPED TO ONE WALLET. Switching accounts must never inherit the
        // previous account's session - hence _sessionWallet, checked on every use.
        private static string   _sessionToken;
        private static string   _sessionWallet;
        private static DateTime _sessionExpiresUtc = DateTime.MinValue;

        // Re-sign a little BEFORE the server would reject: a token that expires in flight
        // becomes a 401 the player experiences as a failed purchase.
        private static readonly TimeSpan SessionSkew = TimeSpan.FromSeconds(60);

        // WO-1454: TRANSIENT renewal failures keep the token and back off instead of destroying it.
        // These two are the backoff ONLY - they never gate the credential itself.
        private const double RenewBackoffBaseSeconds = 5;
        private const int    RenewBackoffMaxSteps    = 5;   // 5,10,20,40,80s ceiling
        private static int      _renewFailureStreak;
        private static DateTime _renewBackoffUntilUtc = DateTime.MinValue;

        private static bool SessionUsable(string wallet)
            => !string.IsNullOrEmpty(_sessionToken)
            && string.Equals(_sessionWallet, wallet, StringComparison.Ordinal)
            && DateTime.UtcNow + SessionSkew < _sessionExpiresUtc;

        /// <summary>
        /// Why a live session cannot be reused. Values are only <c>missing</c> or
        /// <c>expired</c> — never a wallet address (WO-1157: do not log wallets).
        /// </summary>
        private static string SessionGapWhy(string wallet)
        {
            if (string.IsNullOrEmpty(_sessionToken)) return "missing";
            if (!string.Equals(_sessionWallet, wallet, StringComparison.Ordinal)) return "missing";
            if (DateTime.UtcNow + SessionSkew >= _sessionExpiresUtc) return "expired";
            return "missing";
        }

        /// <summary>
        /// True when a cached wallet session is usable NOW. Does not mint, does not
        /// attach, does not change TryAttachAsync. HUD uses this to tell a missing
        /// session from a transport fail (WO-1875).
        /// </summary>
        public static bool HasLiveWalletSession()
        {
            return SessionUsable(CurrentPlayerId());
        }

        /// <summary>
        /// Public wrapper over <see cref="SessionGapWhy"/> for the current player.
        /// Safe: returns only <c>missing</c> or <c>expired</c>, never a wallet.
        /// </summary>
        public static string SessionGapWhyPublic()
        {
            return SessionGapWhy(CurrentPlayerId());
        }

        /// <summary>
        /// Why the most recent <c>TryAttachSession</c> aborted. Set when that helper
        /// returns false; cleared when a session attaches. HUD reads this without
        /// minting (WO-1875).
        /// </summary>
        public static string LastSessionGapWhy { get; private set; }

        /// <summary>Drop the cached session. Call on wallet change, sign-out, or a 401.</summary>
        public static void ClearSession()
        {
            _sessionToken = null;
            _sessionWallet = null;
            _sessionExpiresUtc = DateTime.MinValue;
            LastSessionGapWhy = "missing";
            // WO-1454: the backoff belongs to the token that just went away. A new wallet must not
            // inherit the previous one's penalty box.
            ClearRenewalBackoff();
        }

        /// <summary>
        /// Installs a bearer session returned by a server endpoint that already verified an external
        /// identity credential. This never accepts guest ids and never verifies credentials locally;
        /// the only intended caller is the GOOGLE_PLAY identity assembly after /api/auth/google-session.
        /// </summary>
        public static bool InstallVerifiedSession(string playerId, string token, string expiresAt)
        {
            if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(token) ||
                !DateTime.TryParse(expiresAt, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var expiry) ||
                expiry <= DateTime.UtcNow + SessionSkew)
                return false;

            _sessionWallet = playerId.Trim();
            _sessionToken = token.Trim();
            _sessionExpiresUtc = expiry.ToUniversalTime();
            FlowTrace.Step("Auth", "verified external identity session installed in memory.");
            return true;
        }
        private const int    RequestTimeoutSeconds = 15;
        private const string GuestWalletPrefix = "guest-local-";

        /// <summary>
        /// The identity the backend will key on. Empty when there is no account at
        /// all — callers must treat that as "cannot authenticate", NOT as
        /// "anonymous": the server rejects an unshaped id with PLAYER_ID_BAD_SHAPE.
        /// </summary>
        public static string CurrentPlayerId()
        {
            var id = GameStateService.Instance?.State?.BoundWallet;
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }

        /// <summary>True for the "guest-local-&lt;64 hex&gt;" shape the client mints offline.</summary>
        public static bool IsGuestIdentity(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!id.StartsWith(GuestWalletPrefix, StringComparison.Ordinal)) return false;
            if (id.Length != GuestWalletPrefix.Length + 64) return false;
            for (int i = GuestWalletPrefix.Length; i < id.Length; i++)
            {
                char c = id[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>Lowercase hex SHA-256 — byte-identical to Node's crypto sha256 hex digest.</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// Attach the headers the backend needs to believe <paramref name="playerId"/>.
        /// </summary>
        /// <param name="req">the request being prepared (headers set in place)</param>
        /// <param name="playerId">the id the request acts on — must match the rail</param>
        /// <param name="bodyRaw">the EXACT bytes that will be uploaded (the signature covers them)</param>
        /// <param name="allowInteractiveSessionMint">True only for an explicit player action that may
        /// open the wallet's SignMessage sheet (for example, pressing Redeem). Background save/load
        /// and passive service calls must leave this false.</param>
        /// <returns>true when it is safe to send; false means ABORT the request.</returns>
        public static async UniTask<bool> TryAttachAsync(UnityWebRequest req, string playerId, byte[] bodyRaw,
            bool allowInteractiveSessionMint = false)
        {
            if (req == null) return false;

            if (string.IsNullOrWhiteSpace(playerId))
            {
                Debug.LogWarning("[BackendAuth] No player identity available - aborting request (fail-closed).");
                return false;
            }

            // GUEST RAIL first: a guest has no signer at all and would otherwise
            // fail closed below and never reach the backend.
            if (IsGuestIdentity(playerId))
            {
                req.SetRequestHeader("X-Guest-Id", playerId);
                return true;
            }

            // A play-* id is minted only after /api/auth/google-session verifies Google's token.
            // Compile-scope this exception: non-GOOGLE_PLAY artifacts retain the invariant that
            // every non-guest identity must be backed by the current real wallet signer.
#if GOOGLE_PLAY
            if (GameStateService.IsGooglePlayIdentity(playerId))
            {
                if (TryAttachCachedSession(req, playerId)) return true;
                Debug.LogWarning("[BackendAuth] Google Play identity has no live session - aborting request.");
                return false;
            }
#endif

            var signer = CoreServices.WalletSigner;
            if (signer == null || !signer.CanSign)
            {
                Debug.LogWarning("[BackendAuth] Wallet identity but no real signer available - " +
                                 "aborting request (fail-closed; refusing to send unauthed).");
                return false;
            }

            var wallet = signer.WalletAddress;
            if (string.IsNullOrEmpty(wallet))
            {
                Debug.LogWarning("[BackendAuth] Signer reports CanSign but has no address - aborting (fail-closed).");
                return false;
            }
            if (!string.Equals(wallet, playerId, StringComparison.Ordinal))
            {
                // The server enforces this too (AUTH_WALLET_MISMATCH); refusing here
                // makes the reason legible instead of an opaque 401.
                Debug.LogWarning("[BackendAuth] Signing wallet does not match the acted-on player id - aborting.");
                return false;
            }

            // ── WO-1157 Fail bounce 2026-08-27 ───────────────────────────────────────────
            // Device: every extra sheet was MintSessionAsync (MWA SignMessage) on Title
            // after CONTINUE, and again ~15 min later walking into a dungeon (TTL). Those
            // callers are cloud SAVE, not a purchase. Authed calls that are not a purchase
            // reuse a live session or WAIT — they must not pop SignMessage while walking.
            //
            // Purchase keeps the till prompt: if a live session exists, attach it and the
            // only wallet sheet is the transfer. If not, mint then transfer (first purchase
            // of a cold session). Per-request signature stays as a purchase-only fallback
            // when the session endpoint is down — never for Title CONTINUE / dungeon-enter.
            bool purchaseRoute = IsPurchaseRoute(req);
            bool allowMint = purchaseRoute || allowInteractiveSessionMint;
            if (await TryAttachSession(req, wallet, allowMint)) return true;
            if (!purchaseRoute) return false;

            FlowTrace.Warn("Wallet",
                $"purchase session mint failed; falling back to per-request signature. " +
                $"scene={CurrentSceneName()} caller={DescribeCaller(req)}");

            // 1. Fresh single-use nonce bound to this wallet.
            var nonce = await FetchNonceAsync(wallet);
            if (string.IsNullOrEmpty(nonce))
            {
                Debug.LogWarning("[BackendAuth] Could not obtain an auth nonce - aborting (fail-closed).");
                return false;
            }

            // 2. The EXACT canonical message the backend reconstructs.
            var payloadTag = (bodyRaw != null && bodyRaw.Length > 0) ? Sha256Hex(bodyRaw) : "load";
            var message = $"dotr-save:v1:{wallet}:{nonce}:{payloadTag}";

            string signature;
            try
            {
                signature = await signer.SignMessageBase58(message);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BackendAuth] Wallet signing failed ({ex.GetType().Name}) - aborting (fail-closed).");
                return false;
            }

            if (string.IsNullOrEmpty(signature))
            {
                Debug.LogWarning("[BackendAuth] Wallet returned an empty signature - aborting (fail-closed).");
                return false;
            }

            req.SetRequestHeader("X-Wallet", wallet);
            req.SetRequestHeader("X-Nonce", nonce);
            req.SetRequestHeader("X-Signature", signature);
            return true;
        }

        // =====================================================================================
        //  ⛔ WarmUpSessionAsync IS GONE (WO-1441, 2026-09-06). READ THIS BEFORE REINSTATING IT.
        // -------------------------------------------------------------------------------------
        //  It was the connect/auto-resume hook that checked for a live session and, by WO-1211's
        //  rule, DELIBERATELY DID NOT MINT ONE. Its trace said "first authenticated action will
        //  mint", which had been false since the WO-1157 fail-bounce (2026-08-27): only
        //  the /api/purchases routes and allowInteractiveSessionMint callers mint, and cloud save
        //  is neither. So both connect paths called this, warmed up, minted nothing, and every cloud
        //  save failed fail-closed with why=missing for the whole session. Device proof
        //  (pid 7170, 2026-09-06, logs/debug/raid-no-abilities-2026-09-06.log):
        //
        //      12:50:06.956 [Flow:Wallet] Connect OK - CHKK…sfkC (Solana Wallet).
        //      12:50:06.960 [Flow:Wallet] session warm-up deferred - first authenticated action will mint...
        //      12:50:11.556 [Flow:Wallet] authed call has no live session ... why=missing /api/game/save
        //
        //  with "MintSessionAsync" appearing ZERO times in 76 MB of that day's captures.
        //
        //  ⛔ AND THE 09-06 RULING BELOW IS ITSELF SUPERSEDED (2026-09-07, WO-1583): AUTO-RESUME NO
        //  LONGER MINTS. One handshake on EVERY launch is the thing the owner objected to. Boot now
        //  calls TryResumeSessionWithoutSigningAsync - reuse or renew, never sign. WarmUpSessionAsync
        //  still stays deleted: this is not a route back to a no-op warm-up, it is a method that
        //  actually does the free half of the work and SAYS SO in the log.
        //
        //  ⭐ OWNER RULING 2026-09-06 REVERSED WO-1211: auto-resume now MINTS - one handshake on
        //  boot. Auto-resume shows no connect prompt of its own, so that handshake is the only
        //  wallet sheet of the session, under her stated two-prompt shape rather than over it.
        //  With both connect paths minting, this method had NO CALLERS LEFT.
        //
        //  ⚠ IT IS DELETED RATHER THAN LEFT UNCALLED **BECAUSE AN UNCALLED METHOD IS WHAT CAUSED
        //  THIS OUTAGE**: MintSessionForExplicitConnectAsync sat here with zero call sites and
        //  nothing noticed, because dead code fails silently and forever. Leaving a second one
        //  behind "in case" would repeat the exact mistake. Its only useful behaviour - "already
        //  live? do nothing" - is inside MintSessionForExplicitConnectAsync's SessionUsable
        //  short-circuit, so nothing was lost.
        // =====================================================================================

        /// <summary>
        /// Mint the backend session at connect time. A live session is a no-op, so this is safe to
        /// call on every connect path and needs no "already warmed" variant.
        /// <para>
        /// ⛔ WO-1441: this had ZERO CALL SITES from the day it was written until 2026-09-06. It is
        /// now called from both EXPLICIT connect paths — <c>WalletSkinBootstrap.ConnectForLoginAsync</c>
        /// when <c>explicitConnect</c> is true (the login-surface tap) and
        /// <c>WalletSkinBootstrap.ConnectAsync</c> (the SKR corner button).
        /// ⛔ NOT from boot auto-resume any more — owner ruling 2026-09-07, WO-1583: boot never signs
        /// and takes <see cref="TryResumeSessionWithoutSigningAsync"/> instead.
        /// If you ever find this uncalled again, cloud save is dark for every wallet holder
        /// who has not bought a pack or redeemed a promo. A regression pins the call site
        /// (BackendSaveAuthRegression).
        /// </para>
        /// <para>
        /// ⚠ THIS BUYS 15 MINUTES, NOT A FIX. api/_lib/wallet-auth.js SESSION_TTL_SECONDS = 900 and
        /// there is no refresh path, so <c>why</c> becomes <c>expired</c> a quarter-hour after the
        /// mint and nothing re-mints (save still passes allowMint:false, correctly — WO-1157 banned
        /// the mid-walk SignMessage sheet). The load-bearing fix is a signature-free renewal on the
        /// SERVER (POST /api/auth/session accepting a still-valid X-Session, sliding window);
        /// <see cref="InstallVerifiedSession"/> is the client-side precedent for a session issued
        /// without a fresh wallet signature. That is a server lane, deliberately not done here.
        /// </para>
        /// </summary>
        public static UniTask<bool> MintSessionForExplicitConnectAsync(string wallet)
        {
            if (string.IsNullOrEmpty(wallet)) return UniTask.FromResult(false);
            if (SessionUsable(wallet))
            {
                FlowTrace.Step("Wallet",
                    $"MintSessionAsync why=explicit-connect scene={CurrentSceneName()} caller=explicit-connect (already live)");
                return UniTask.FromResult(true);
            }
            return MintSessionAsync(wallet, "explicit-connect", "explicit-connect");
        }

        /// <summary>
        /// WO-1583 (owner ruling 2026-09-07): the SIGNATURE-FREE half of the connect handshake.
        /// Reuses a live session, renews an expired one over the wire, and otherwise gives up
        /// QUIETLY - it never calls MintSessionAsync, so it can NEVER raise a wallet SignMessage
        /// sheet. This is what the BOOT / auto-resume path calls.
        /// <para>
        /// Owner, verbatim: <i>"everytime i play now im forced to authenticate ... I would think the
        /// authentication would only be needed for purchases (and codes)"</i>. WO-1441 had made both
        /// connect paths mint, which fixed cloud save by charging the player a wallet sheet on every
        /// single launch. That trade is reversed here: boot never signs.
        /// </para>
        /// <para>
        /// ⛔ AND IT WILL USUALLY RETURN FALSE ON A COLD BOOT - THAT IS THE HONEST, ACCEPTED COST,
        /// NOT A BUG. The session is three in-memory statics and is DELIBERATELY never written to
        /// disk (see the WO-1157 note at the top of this file: it is a bearer credential, and
        /// PlayerPrefs on Android is readable by a backup). A fresh process therefore holds no
        /// token, so <c>SessionUsable</c> is false, <c>SessionGapWhy</c> is <c>missing</c>, and
        /// there is nothing for <see cref="TryRenewSessionAsync"/> to present. Cloud save then
        /// queues offline (GameStateService.EnqueueOffline) until a purchase, a promo redeem, or an
        /// explicit Connect tap mints - at which point the queue drains in one upload.
        /// The only way to get BOTH no boot sheet AND cloud save at boot is a sealed persisted
        /// token (the MwaSessionStore AES-GCM shape). That is a separate ruling, not this lane.
        /// </para>
        /// </summary>
        public static async UniTask<bool> TryResumeSessionWithoutSigningAsync(string wallet)
        {
            if (string.IsNullOrEmpty(wallet)) return false;

            if (SessionUsable(wallet))
            {
                FlowTrace.Step("Wallet",
                    "boot never signs (ruling 2026-09-07) - a live backend session was already held " +
                    "in memory, so this reconnect reused it and showed no wallet sheet. scene=" +
                    CurrentSceneName());
                return true;
            }

            string why = SessionGapWhy(wallet);
            if (why == "expired" && await TryRenewSessionAsync(wallet, "boot-resume"))
            {
                FlowTrace.Step("Wallet",
                    "boot never signs (ruling 2026-09-07) - the expired backend session was RENEWED " +
                    "over the wire with no signature, so cloud save continues and no wallet sheet " +
                    "was shown. scene=" + CurrentSceneName());
                return true;
            }

            FlowTrace.Step("Wallet",
                "boot never signs (ruling 2026-09-07) - the wallet reauthorize restored IDENTITY " +
                "without a signature, and no wallet sheet was shown ON PURPOSE. There is no backend " +
                "session to reuse (why=" + why + "; the token is memory-only by design, so a fresh " +
                "process always starts without one) and nothing to renew. This is EXPECTED, not a " +
                "failure: cloud saves queue offline until a purchase, a promo code, or an explicit " +
                "Connect tap mints a session - the queue then drains in one upload. scene=" +
                CurrentSceneName());
            return false;
        }

        /// <summary>Attach proof already available in memory without minting or signing.</summary>
        public static bool TryAttachCachedSession(UnityWebRequest req, string playerId)
        {
            if (req == null || string.IsNullOrWhiteSpace(playerId)) return false;
            if (IsGuestIdentity(playerId))
            {
                req.SetRequestHeader("X-Guest-Id", playerId);
                return true;
            }
            if (!SessionUsable(playerId)) return false;
            req.SetRequestHeader("X-Session", _sessionToken);
            req.SetRequestHeader("X-Wallet", playerId);
            return true;
        }

        /// <summary>
        /// WO-1157. Attach a cached session if we hold a usable one. Minting is opt-in
        /// (<paramref name="allowMint"/>) and reserved for purchase / explicit-connect —
        /// Title CONTINUE and dungeon-enter saves pass false so they never raise SignMessage.
        /// </summary>
        private static async UniTask<bool> TryAttachSession(UnityWebRequest req, string wallet, bool allowMint)
        {
            if (SessionUsable(wallet))
            {
                LastSessionGapWhy = null;
                AttachSessionHeaders(req, wallet);
                return true;
            }

            string why = SessionGapWhy(wallet);
            string scene = CurrentSceneName();
            string caller = DescribeCaller(req);

            // ── WO-1441: RENEW BEFORE GIVING UP, AND WITHOUT A WALLET SHEET ──────────────
            // A 15-minute server TTL with no renewal meant a session minted at boot died
            // mid-play and `why` flipped missing -> expired, with nothing able to re-mint
            // (save may not raise SignMessage - WO-1157). Renewal needs NO signature, so it
            // is legal on exactly the routes that may not mint. This is the difference
            // between "cloud save works for 15 minutes" and "cloud save works".
            //
            // ⚠ ONLY for `expired`. `missing` means we hold no token at all, so there is
            // nothing to present and a renewal would be a wasted round trip on every single
            // authed call before the first mint.
            if (why == "expired" && await TryRenewSessionAsync(wallet, caller))
            {
                LastSessionGapWhy = null;
                AttachSessionHeaders(req, wallet);
                return true;
            }

            if (!allowMint)
            {
                // WO-1441: was FlowTrace.Step, so it never reached F8 and the only visible symptom
                // was GameStateService's LogError two lines later ("[Sync] Wallet cloud SAVE
                // aborted"), which names the effect and not the cause. Warn, and say plainly what
                // WOULD mint - "waiting" implied something was coming, and for a save nothing ever is.
                // WO-1583 (ruling 2026-09-07): why=missing on this route is now the NORMAL state for
                // most of a session, not a defect report. Boot no longer signs, so nothing mints
                // until the player buys, redeems a code, or taps Connect. The old text called
                // why=missing "NEVER MINTED" in the tone of an outage, which under the new ruling
                // would send the next triage after working code (CLAUDE.md section 11B).
                LastSessionGapWhy = why;
                FlowTrace.Warn("Wallet",
                    $"authed call has no live session and this route may NOT mint (no SignMessage here). " +
                    $"why={why} scene={scene} caller={caller}. " +
                    "EXPECTED under the 2026-09-07 ruling: the session is minted only by a purchase " +
                    "(any /api/purchases route), a promo redeem, or an explicit Connect tap - never at boot. " +
                    "The caller must fail closed and queue; the queue drains once a session exists. " +
                    "why=missing means no token is held at all (memory-only credential, so a fresh process " +
                    "starts this way); why=expired means the 15-minute server TTL lapsed and the " +
                    "signature-free renewal did not restore it.");
                return false;
            }

            var minted = await MintSessionAsync(wallet, why, caller);
            if (!minted)
            {
                LastSessionGapWhy = why;
                return false;
            }
            LastSessionGapWhy = null;
            AttachSessionHeaders(req, wallet);
            return true;
        }

        private static void AttachSessionHeaders(UnityWebRequest req, string wallet)
        {
            req.SetRequestHeader("X-Session", _sessionToken);
            // ⛔ X-Wallet travels alongside so the server can still bind the session to the player
            // being acted on. The session names the wallet; this lets the server CHECK that claim
            // rather than take it. Never send a session without it.
            req.SetRequestHeader("X-Wallet", wallet);
        }

        /// <summary>
        /// POST /api/auth/session with one nonce+signature, cache the returned bearer token.
        /// This is the ONLY place a wallet-signature prompt happens for backend auth.
        /// <paramref name="why"/> is missing / expired / explicit-connect.
        /// ⛔ Never logs a wallet address.
        /// </summary>
        private static async UniTask<bool> MintSessionAsync(string wallet, string why, string caller)
        {
            string scene = CurrentSceneName();
            FlowTrace.Step("Wallet",
                $"MintSessionAsync why={why} scene={scene} caller={caller}");

            var signer = CoreServices.WalletSigner;
            if (signer == null || !signer.CanSign)
            {
                FlowTrace.Warn("Wallet",
                    $"MintSessionAsync aborted (no signer) why={why} scene={scene} caller={caller}");
                return false;
            }

            var nonce = await FetchNonceAsync(wallet);
            if (string.IsNullOrEmpty(nonce))
            {
                FlowTrace.Warn("Wallet",
                    $"MintSessionAsync aborted (no nonce) why={why} scene={scene} caller={caller}");
                return false;
            }

            // Bodyless request, so the canonical message uses the 'load' payload tag - exactly
            // the shape the server rebuilds for a null payload. These two must not drift.
            var message = $"dotr-save:v1:{wallet}:{nonce}:load";

            string signature;
            try { signature = await signer.SignMessageBase58(message); }
            catch (Exception ex)
            {
                FlowTrace.Warn("Wallet",
                    $"MintSessionAsync signing failed ({ex.GetType().Name}) why={why} scene={scene} caller={caller}");
                Debug.LogWarning($"[BackendAuth] Session signing failed ({ex.GetType().Name}) - falling back to per-request signing.");
                return false;
            }
            if (string.IsNullOrEmpty(signature))
            {
                FlowTrace.Warn("Wallet",
                    $"MintSessionAsync empty signature why={why} scene={scene} caller={caller}");
                return false;
            }

            using var req = new UnityWebRequest(SessionUrl, UnityWebRequest.kHttpVerbPOST);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("X-Wallet", wallet);
            req.SetRequestHeader("X-Nonce", nonce);
            req.SetRequestHeader("X-Signature", signature);

            try { await req.SendWebRequest(); }
            catch (Exception e)
            {
                FlowTrace.Warn("Wallet",
                    $"MintSessionAsync threw ({req.responseCode}/{e.GetType().Name}) why={why} scene={scene} caller={caller}");
                Debug.LogWarning($"[BackendAuth] Session mint threw ({req.responseCode}): {e.GetType().Name}");
                return false;
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                FlowTrace.Warn("Wallet",
                    $"MintSessionAsync http {req.responseCode} why={why} scene={scene} caller={caller}");
                Debug.LogWarning($"[BackendAuth] Session mint failed ({req.responseCode}).");
                return false;
            }

            try
            {
                var res = JsonConvert.DeserializeObject<SessionResponse>(req.downloadHandler.text);
                if (res == null || !res.Ok || string.IsNullOrEmpty(res.Token))
                {
                    FlowTrace.Warn("Wallet",
                        $"MintSessionAsync empty token why={why} scene={scene} caller={caller}");
                    return false;
                }

                _sessionToken = res.Token;
                _sessionWallet = wallet;
                // Prefer the server's own expiry; fall back to ttlSeconds. ⛔ Never invent one -
                // a client-guessed expiry that outlives the server's produces a 401 the player
                // sees as a failed purchase.
                _sessionExpiresUtc = DateTime.TryParse(res.ExpiresAt, null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var parsed)
                    ? parsed
                    : DateTime.UtcNow.AddSeconds(res.TtlSeconds > 0 ? res.TtlSeconds : 60);
                FlowTrace.Step("Wallet",
                    $"MintSessionAsync held why={why} scene={scene} caller={caller}");
                return true;
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Wallet",
                    $"MintSessionAsync parse {ex.GetType().Name} why={why} scene={scene} caller={caller}");
                Debug.LogWarning($"[BackendAuth] Session parse error: {ex.GetType().Name}");
                return false;
            }
        }

        /// <summary>
        /// WO-1441. Trade a still-valid (or barely-lapsed) session for a fresh one by presenting the
        /// token itself — NO wallet signature, so this is legal on routes that may not mint.
        /// <para>
        /// ⛔ THIS IS WHY CLOUD SAVE SURVIVES PAST FIFTEEN MINUTES. Without it, the boot handshake
        /// bought exactly one TTL and then every save failed again with why=expired for the rest of
        /// the session, which is the same outage as why=missing with a slower fuse.
        /// </para>
        /// <para>
        /// ⚠ IT DROPS THE TOKEN ONLY ON A REAL REFUSAL — 401/403 (WO-1454). A refusal means the
        /// chain is over (past the server's absolute cap, or revoked); clearing then turns the next
        /// <c>why</c> into <c>missing</c>, which is HONEST, and the player re-signs on their next
        /// explicit connect or purchase — never silently, never unauthenticated.
        /// </para>
        /// <para>
        /// ⛔ EVERYTHING ELSE KEEPS THE TOKEN. This method used to clear on ANY non-Success result,
        /// so a single 500/503/timeout — the server saying "try again" — permanently darkened cloud
        /// save for that install, because save passes <c>allowMint:false</c> and nothing re-mints.
        /// Transient failures now back off (<see cref="ScheduleRenewalRetry"/>) and leave the
        /// credential exactly where it was. See <see cref="IsCredentialRefusal"/>.
        /// </para>
        /// </summary>
        private static async UniTask<bool> TryRenewSessionAsync(string wallet, string caller)
        {
            if (string.IsNullOrEmpty(_sessionToken) || string.IsNullOrEmpty(wallet)) return false;

            string scene = CurrentSceneName();

            // WO-1454: a transient failure left the token in place on purpose; do not re-present it
            // to an unwell server on every authed call. The token stays valid either way.
            if (DateTime.UtcNow < _renewBackoffUntilUtc)
            {
                FlowTrace.Warn("Wallet",
                    $"RenewSessionAsync action=keep reason=backoff streak={_renewFailureStreak} - skipping the " +
                    $"attempt for another {(int)(_renewBackoffUntilUtc - DateTime.UtcNow).TotalSeconds}s after a " +
                    $"transient failure; the token is UNTOUCHED. scene={scene} caller={caller}");
                return false;
            }
            FlowTrace.Step("Wallet", $"RenewSessionAsync (no signature required) scene={scene} caller={caller}");

            using var req = new UnityWebRequest(SessionUrl, UnityWebRequest.kHttpVerbPOST);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("X-Wallet", wallet);
            // ⛔ NO X-Nonce. The server routes to renewal on the ABSENCE of nonce material, so
            // sending any would take the verifying path and fail for want of a signature.
            req.SetRequestHeader("X-Session", _sessionToken);

            try { await req.SendWebRequest(); }
            catch (Exception e)
            {
                // A transport failure is NOT proof the session is dead — the player may simply be
                // in a tunnel. Keep the token and let the next call try again; only a real refusal
                // from the server (below) clears it.
                ScheduleRenewalRetry();
                FlowTrace.Warn("Wallet",
                    $"RenewSessionAsync action=keep status={req.responseCode} threw={e.GetType().Name} - a " +
                    $"transport failure is NOT proof the session is dead (the player may simply be in a " +
                    $"tunnel); the token is KEPT. scene={scene} caller={caller}");
                return false;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                // ⛔ WO-1454: THE STATUS DECIDES, NOT THE MERE FACT OF FAILURE. This used to clear
                // on ANY non-Success result, so one 500 or 503 - the server saying "try again",
                // which is the opposite of "you are not who you say" - destroyed a still-valid
                // token. Save passes allowMint:false, so nothing re-minted it: from that instant
                // every save reported why=missing PERMANENTLY, until the player re-authenticated
                // by hand. A transient server hiccup must never cost the session.
                if (IsCredentialRefusal(req.responseCode))
                {
                    FlowTrace.Warn("Wallet",
                        $"RenewSessionAsync action=clear status={req.responseCode} - the server REFUSED the " +
                        $"credential (401/403), so the chain is genuinely over; the next authed call reads " +
                        $"why=missing until something mints. scene={scene} caller={caller}");
                    ClearSession();
                    return false;
                }

                ScheduleRenewalRetry();
                FlowTrace.Warn("Wallet",
                    $"RenewSessionAsync action=keep status={req.responseCode} result={req.result} - transient " +
                    $"(5xx / timeout / transport), NOT a refusal; the token is KEPT and the next attempt is " +
                    $"backed off {(int)(_renewBackoffUntilUtc - DateTime.UtcNow).TotalSeconds}s. scene={scene} caller={caller}");
                return false;
            }

            try
            {
                var res = JsonConvert.DeserializeObject<SessionResponse>(req.downloadHandler.text);
                if (res == null || !res.Ok || string.IsNullOrEmpty(res.Token))
                {
                    // WO-1454: a 2xx that carries no token is NOT a refusal - our server only ever
                    // returns 200 WITH a token (api/auth/session.js:119-126); a bodyless 200 is far
                    // more likely a captive portal or proxy than the server revoking anything. Keep.
                    ScheduleRenewalRetry();
                    FlowTrace.Warn("Wallet",
                        $"RenewSessionAsync action=keep status={req.responseCode} reason=empty-token - a 2xx " +
                        $"with no token is not a credential refusal. scene={scene} caller={caller}");
                    return false;
                }

                _sessionToken = res.Token;
                _sessionWallet = wallet;
                _sessionExpiresUtc = DateTime.TryParse(res.ExpiresAt, null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal |
                        System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var parsed)
                    ? parsed
                    : DateTime.UtcNow.AddSeconds(res.TtlSeconds > 0 ? res.TtlSeconds : 60);

                ClearRenewalBackoff();
                FlowTrace.Step("Wallet",
                    $"RenewSessionAsync held - session extended with NO wallet prompt. scene={scene} caller={caller}");
                return true;
            }
            catch (Exception ex)
            {
                // WO-1454: a body we could not parse proves nothing about the credential.
                ScheduleRenewalRetry();
                FlowTrace.Warn("Wallet",
                    $"RenewSessionAsync action=keep status={req.responseCode} parse={ex.GetType().Name} - an " +
                    $"unreadable body is not a refusal. scene={scene} caller={caller}");
                return false;
            }
        }

        /// <summary>
        /// WO-1454. The ONLY statuses that mean "you are not who you say" and therefore justify
        /// destroying a cached session.
        /// <para>
        /// ⛔ 5xx IS NOT ON THIS LIST AND MUST NEVER BE ADDED. `api/auth/session.js:104` returns
        /// **500 SERVER_ERROR** when the renewal query itself throws - e.g. while the `signed_at`
        /// column is missing from a database that has not had `api/schema.sql` applied. That is a
        /// DEPLOYMENT state, not a verdict on the player's credential, and clearing on it turns a
        /// server hiccup into a permanent cloud-save outage for that install (save passes
        /// allowMint:false, so nothing re-mints). The refusals the server actually issues are
        /// `quietFail(res, 401, ...)` for a wrong-wallet or absolute-cap token (`:117`, `:135`).
        /// </para>
        /// </summary>
        private static bool IsCredentialRefusal(long status)
        {
            return status == 401 || status == 403;
        }

        /// <summary>Back off after a TRANSIENT renewal failure so a kept token is not re-presented
        /// on every authed call while the server is unwell (WO-1454). Never clears anything.</summary>
        private static void ScheduleRenewalRetry()
        {
            _renewFailureStreak = _renewFailureStreak < RenewBackoffMaxSteps
                ? _renewFailureStreak + 1
                : RenewBackoffMaxSteps;
            double seconds = RenewBackoffBaseSeconds * Math.Pow(2, _renewFailureStreak - 1);
            _renewBackoffUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
        }

        /// <summary>Clears the transient-failure backoff after a renewal succeeds (WO-1454).</summary>
        private static void ClearRenewalBackoff()
        {
            _renewFailureStreak = 0;
            _renewBackoffUntilUtc = DateTime.MinValue;
        }

        private static bool IsPurchaseRoute(UnityWebRequest req)
        {
            string path = RequestPath(req);
            return path.IndexOf("/api/purchases/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CurrentSceneName()
        {
            try
            {
                string n = SceneManager.GetActiveScene().name;
                return string.IsNullOrEmpty(n) ? "none" : n;
            }
            catch
            {
                return "none";
            }
        }

        /// <summary>Path-only request id. Query is stripped so a wallet never lands in a log.</summary>
        private static string RequestPath(UnityWebRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.url)) return "no-url";
            string url = req.url;
            int q = url.IndexOf('?');
            if (q >= 0) url = url.Substring(0, q);
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                int pathStart = url.IndexOf('/', scheme + 3);
                if (pathStart >= 0) url = url.Substring(pathStart);
            }
            return string.IsNullOrEmpty(url) ? "no-url" : url;
        }

        private static string DescribeCaller(UnityWebRequest req)
        {
            string path = RequestPath(req);
            string frame = FirstExternalFrame();
            return string.IsNullOrEmpty(frame) ? path : frame + " " + path;
        }

        /// <summary>
        /// Names the first frame OUTSIDE this class, so a trace says who asked.
        /// <para>
        /// ⛔ WO-1441 — THIS WAS BLIND TO ASYNC AND NAMED THE WRONG THING FOR MONTHS. The device
        /// capture read <c>caller=&lt;TryAttachSession&gt;d__20.MoveNext /api/game/save</c>: an
        /// async method compiles to a generated state-machine struct nested INSIDE this class, so
        /// <c>DeclaringType</c> is <c>BackendRequestSigner+&lt;TryAttachSession&gt;d__20</c> — never
        /// equal to <c>typeof(BackendRequestSigner)</c>, so the skip never fired and the walk stopped
        /// on our OWN frame. Every caller token this method ever produced from an async path named
        /// this file instead of the real caller, which is why the save trace could not say
        /// GameStateService. Unwrapping the state machine restores the whole point of the field.
        /// </para>
        /// </summary>
        private static string FirstExternalFrame()
        {
            try
            {
                var st = new System.Diagnostics.StackTrace(2, false);
                for (int i = 0; i < st.FrameCount; i++)
                {
                    var m = st.GetFrame(i)?.GetMethod();
                    var t = m?.DeclaringType;
                    if (t == null) continue;

                    // Compiler-generated async/iterator state machines and lambda closures are
                    // named "<Method>d__N" / "<>c__DisplayClassN" and are NESTED in their owner.
                    // Resolve to the owner before deciding whether this frame is ours.
                    var owner = t;
                    string method = m.Name;
                    if (owner.Name.Length > 0 && owner.Name[0] == '<' && owner.DeclaringType != null)
                    {
                        method = MethodNameFromGeneratedType(owner.Name) ?? method;
                        owner = owner.DeclaringType;
                    }

                    if (owner == typeof(BackendRequestSigner)) continue;

                    // Between our frame and the real caller sit the async PLUMBING frames -
                    // AsyncUniTaskMethodBuilder<T>.Start, AsyncMethodBuilderCore, MoveNextRunner.
                    // Their type names do NOT start with '<' and they are not this class, so without
                    // this skip the very first thing returned is "AsyncUniTaskMethodBuilder`1.Start"
                    // - which names the compiler, not the caller, and is exactly as useless as the
                    // state-machine name it replaced.
                    string ns = owner.Namespace ?? string.Empty;
                    if (ns.StartsWith("Cysharp.", StringComparison.Ordinal) ||
                        ns.StartsWith("System.Runtime.CompilerServices", StringComparison.Ordinal) ||
                        ns.StartsWith("System.Threading", StringComparison.Ordinal))
                        continue;

                    return owner.Name + "." + method;
                }
            }
            catch { /* IL2CPP may omit frames */ }
            return string.Empty;
        }

        /// <summary>"&lt;SendCurrentSnapshot&gt;d__214" -&gt; "SendCurrentSnapshot"; null when unparseable.</summary>
        private static string MethodNameFromGeneratedType(string generatedTypeName)
        {
            if (string.IsNullOrEmpty(generatedTypeName) || generatedTypeName[0] != '<') return null;
            int close = generatedTypeName.IndexOf('>');
            if (close <= 1) return null;
            return generatedTypeName.Substring(1, close - 1);
        }

        private sealed class SessionResponse
        {
            [JsonProperty("ok")]         public bool   Ok         { get; set; }
            [JsonProperty("token")]      public string Token      { get; set; }
            [JsonProperty("expiresAt")]  public string ExpiresAt  { get; set; }
            [JsonProperty("ttlSeconds")] public int    TtlSeconds { get; set; }
        }

        /// <summary>
        /// GET /api/auth/nonce?wallet=&lt;base58&gt; -> the issued one-time nonce, or
        /// null on any failure (the caller then aborts rather than sending unauthed).
        /// </summary>
        private static async UniTask<string> FetchNonceAsync(string wallet)
        {
            var url = $"{NonceUrl}?wallet={Uri.EscapeDataString(wallet)}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");

            try
            {
                await req.SendWebRequest();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackendAuth] Nonce fetch threw ({req.responseCode}): {e.GetType().Name}");
                return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BackendAuth] Nonce fetch failed ({req.responseCode}).");
                return null;
            }

            try
            {
                var resp = JsonConvert.DeserializeObject<NonceResponse>(req.downloadHandler.text);
                return resp != null && resp.Success ? resp.Nonce : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BackendAuth] Nonce parse error: {ex.GetType().Name}");
                return null;
            }
        }

        private sealed class NonceResponse
        {
            [JsonProperty("success")]    public bool   Success    { get; set; }
            [JsonProperty("nonce")]      public string Nonce      { get; set; }
            [JsonProperty("expiresAt")]  public string ExpiresAt  { get; set; }
            [JsonProperty("ttlSeconds")] public int    TtlSeconds { get; set; }
        }
    }
}
