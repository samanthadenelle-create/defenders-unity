// =============================================================================
// HeartboundStatusClient — the ONE client-side reader of the backend-verified
// native SKR stake, and the ONE writer of VerifiedStakeSnapshot.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
// WO-1674 (HEART-001). Owner ruling 2026-09-10 13:10: BACKEND ONLY; an RPC
// outage serves last-known verified state within a bounded grace window;
// Heartbound is Seeker-only.
//
// ⛔ TWO INDEPENDENT COMPILE-TIME GATES, AND THE OUTER ONE IS THE STRONG ONE.
//
//   1. DeNelle.Wallet.asmdef:21-23 — "defineConstraints": ["!GOOGLE_PLAY"].
//      The WHOLE ASSEMBLY is absent from a Google Play artifact. That is a
//      stronger guarantee than any feature flag, because a flag is a PlayerPrefs
//      value a stored entry can beat (FeatureFlags.cs:691-693) and an absent
//      assembly is absent.
//   2. #if DAPP_STORE below — the owner's Seeker-only ruling, stated in code.
//      DAPP_STORE is a DISTRIBUTION define (FeatureFlags.cs:1006-1008): it says
//      where the binary is going, which is the only thing that answers the
//      compliance question. AndroidBuild.cs:237-238 stamps exactly one of
//      DAPP_STORE / GOOGLE_PLAY per artifact and forbids the other.
//
//   ⚠ THE SECOND GATE IS NOT REDUNDANT WITH THE FIRST. The asmdef excludes this
//   from a PLAY build; the define excludes it from every build that is not the
//   Seeker/dApp artifact — a plain Windows or WebGL build carries neither
//   define, so without #if DAPP_STORE this driver would poll a Heartbound
//   endpoint on surfaces the owner ruled out of scope.
//
// ⛔ WHAT THIS FILE MAY NOT BECOME. It reads a number the SERVER produced and
// hands it, verbatim, to VerifiedStakeSnapshot. It must never compute a stake,
// derive a tier, read an on-chain account, or accept an amount from any other
// caller. The chain read lives in api/_lib/skr-staking.js and nowhere else —
// that relocation IS work order 1674.
// =============================================================================

using System;
using System.Globalization;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;
using DeNelle.Core.Web3;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Wallet
{
    /// <summary>
    /// Fetches <c>GET /api/heartbound/status</c> and installs the answer into
    /// <see cref="VerifiedStakeSnapshot"/>. Read-only: no signature is requested
    /// beyond the standard save/load auth challenge, and no token moves.
    /// </summary>
    public static class HeartboundStatusClient
    {
        private const string Sys = "Heartbound";
        private const string StatusPath = "/api/heartbound/status";
        private const int RequestTimeoutSeconds = 15;

#if DAPP_STORE
        /// <summary>Serialises concurrent callers so a screen open plus a login do not double-fetch.</summary>
        /// <remarks>Inside the define with its only users: an unconditionally-declared private field
        /// that nothing touches on a non-Seeker build is a CS0169 "never used" warning, and a warning
        /// introduced by a compliance gate is how the gate gets removed.</remarks>
        private static bool _inFlight;
#endif

        /// <summary>
        /// Ask the backend what this wallet has staked, and install the answer.
        ///
        /// <paramref name="manualRefresh"/> is true ONLY for an explicit player
        /// action (a pull-to-refresh on the Heartbound screen). The server applies
        /// the 60-second manual cooldown and answers from its row when it is inside
        /// it — the client does not need a second timer, and a second timer would be
        /// a second authority.
        /// </summary>
        public static async UniTask<bool> RefreshAsync(bool manualRefresh = false)
        {
#if !DAPP_STORE
            // Not the Seeker/dApp artifact — Heartbound does not exist here.
            // Compiled out rather than flagged off, per the owner's ruling.
            await UniTask.CompletedTask;
            return false;
#else
            if (_inFlight)
            {
                FlowTrace.Step(Sys, "stake verification already in flight; this request coalesced into it.");
                return false;
            }

            string playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrWhiteSpace(playerId))
            {
                VerifiedStakeSnapshot.NoteAttemptFailed("no player identity yet");
                return false;
            }

            // A guest has no wallet and no mechanism to attach one
            // (api/_lib/wallet-auth.js:27-29). Do not spend a round trip to be
            // told WALLET_NOT_LINKED; record it locally and move on.
            if (BackendRequestSigner.IsGuestIdentity(playerId))
            {
                VerifiedStakeSnapshot.AcceptServerVerification(
                    "WALLET_NOT_LINKED", 0L, 0L, false, 0L, 0L, 0L);
                // ⚠ A DEFINITIVE ZERO, NOT A HELD ANSWER. Everywhere else an absent answer
                // holds the previous one, because "we could not ask" is not "you have
                // nothing". Here we CAN answer: this rail has no wallet and no mechanism to
                // attach one, so the player is tier 0 as a matter of fact, not of ignorance.
                HeartboundBonuses.AcceptServerBenefits(0, 0f, 0, 0, new string[0]);
                return false;
            }

            _inFlight = true;
            try
            {
                string url = BackendRequestSigner.BackendBase + StatusPath +
                             "?playerId=" + UnityWebRequest.EscapeURL(playerId) +
                             (manualRefresh ? "&refresh=1" : string.Empty);

                FlowTrace.Step(Sys, "requesting backend stake verification (manual=" + manualRefresh +
                                    "). The CLIENT SENDS NO AMOUNT - the server reads the chain itself.");

                using var req = UnityWebRequest.Get(url);
                req.timeout = RequestTimeoutSeconds;
                req.SetRequestHeader("Accept", "application/json");

                // The GET body is empty, so the wallet rail signs the literal
                // "load" tag — the same challenge api/game/load.js takes.
                // allowInteractiveSessionMint stays FALSE: this is a passive
                // service call and must never raise a wallet signing sheet.
                bool safeToSend = await BackendRequestSigner.TryAttachAsync(req, playerId, null, false);
                if (!safeToSend)
                {
                    VerifiedStakeSnapshot.NoteAttemptFailed("could not authenticate the request (fail-closed)");
                    return false;
                }

                try
                {
                    await req.SendWebRequest();
                }
                catch (Exception ex)
                {
                    // ⛔ A TRANSPORT FAILURE IS NOT A STAKE OF ZERO. This is the
                    // precise mistake NativeSkrStakeQuery.cs:87-94 makes today, and
                    // the one acceptance criterion 4 forbids. Hold the previous
                    // answer; the server's grace window decides how long it counts.
                    VerifiedStakeSnapshot.NoteAttemptFailed("transport: " + ex.Message);
                    return false;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    VerifiedStakeSnapshot.NoteAttemptFailed("http " + req.responseCode + " " + req.error);
                    return false;
                }

                // Explicit Func<bool> rather than a bare lambda: Guard exposes both
                // Try(string,string,Action) and Try<T>(string,string,Func<T>,T), and a
                // value-returning lambda is convertible to BOTH — the overload that
                // wins would be a compiler detail deciding whether the parse result is
                // observed at all. §12: one bad response logs and is skipped, never
                // silently blanks the stake.
                string body = req.downloadHandler != null ? req.downloadHandler.text : null;
                Func<bool> parse = () => Apply(body);
                return Guard.Try(Sys, "parse heartbound status", parse, false);
            }
            finally
            {
                _inFlight = false;
            }
#endif
        }

#if DAPP_STORE
        /// <summary>
        /// Install a server response. Every field is taken from the SERVER's
        /// snapshot object; nothing is derived here.
        /// </summary>
        private static bool Apply(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                VerifiedStakeSnapshot.NoteAttemptFailed("empty response body");
                return false;
            }

            JObject root = JObject.Parse(json);
            JObject snap = root["snapshot"] as JObject;
            if (snap == null)
            {
                VerifiedStakeSnapshot.NoteAttemptFailed("response carried no snapshot object");
                return false;
            }

            string status = (string)snap["verificationStatus"] ?? string.Empty;

            // ⚠ RAW u128 VALUES ARRIVE AS STRINGS ON PURPOSE (they do not fit a
            // JSON number, and a double would silently round a real balance).
            // Parse to long; a value too large for a long is reported rather than
            // wrapped, because a wrapped stake is a fabricated stake.
            long activeRaw = ParseRaw(snap["activeStakedRaw"], "activeStakedRaw");
            long unstakingRaw = ParseRaw(snap["unstakingRaw"], "unstakingRaw");
            bool unstakingReady = (bool?)snap["unstakingReady"] ?? false;
            long verifiedAtMs = (long?)snap["verifiedAtMs"] ?? 0L;
            long ageSeconds = (long?)snap["ageSeconds"] ?? 0L;
            long graceSeconds = (long?)snap["graceWindowSeconds"] ?? 0L;

            VerifiedStakeSnapshot.AcceptServerVerification(
                status, activeRaw, unstakingRaw, unstakingReady,
                verifiedAtMs, ageSeconds, graceSeconds);

            AcceptBenefits(root);
            return true;
        }

        /// <summary>
        /// Install the server's PASSIVE BENEFIT block (WO-1679 / HEART-006) into
        /// <see cref="HeartboundBonuses"/>.
        ///
        /// ⛔ THIS METHOD DERIVES NOTHING. It reads the numbers the server sent and hands
        /// them over verbatim. There is no tier ladder here, no threshold, no percentage
        /// table and no arithmetic that turns a stake into a tier — the backend owns all of
        /// that (api/_lib/heartbound-tiers.js), because product rule 6 makes it the
        /// authority and a client-side copy would be a second one.
        ///
        /// ⚠ AN ABSENT `benefits` BLOCK IS NOT AN ERROR AND IS NOT A ZERO. A backend that
        /// has not yet been taught to send it (the block lands with the HEART-002 status
        /// wiring) leaves the previous answer standing, exactly as a failed refresh does.
        /// Overwriting with zeros here would take a player's passives away every time an
        /// older endpoint answered — which is the same fail-to-zero mistake
        /// VerifiedStakeSnapshot's header spends a paragraph refusing.
        ///
        /// ⚠ AND IT IS A SEPARATE CALL FROM AcceptServerVerification, DELIBERATELY. That
        /// method keeps its "exactly one caller" property (StakingComplianceRegression §A4)
        /// untouched: this is a different method on a different type, invoked from the same
        /// single client, immediately after.
        /// </summary>
        private static void AcceptBenefits(JObject root)
        {
            JObject block = root["benefits"] as JObject;
            if (block == null)
            {
                // ⚠ ONCE, NOT Warn-PER-REFRESH. This is the EXPECTED shape until HEART-002 wires
                // readHeartboundStatus into the endpoint, so it fires on EVERY successful refresh
                // on every device — at the Driver's cadence. A Warn here would be a firehose that
                // evicts the boot window out of the logcat ring and destroys the evidence the
                // instrumentation exists to preserve (CLAUDE.md §12; memory
                // `logcat-ring-buffer-destroys-evidence`). NoteAttemptFailed stays for a GENUINE
                // failure, which this is not: the previous answer is held either way.
                FlowTrace.Once(Sys, "benefits-block-absent",
                                    "the status response carried no benefits block - this endpoint " +
                                    "predates HEART-006's benefits wiring. Holding the previous " +
                                    "answer; nothing was zeroed. Expected until the status route " +
                                    "calls readHeartboundStatus with a tier resolver.");
                return;
            }

            int tier = (int?)block["tier"] ?? 0;
            float rate = (float?)block["productionRateSum"] ?? 0f;

            int rerolls = 0;
            int rollCap = 0;
            JObject polish = block["polish"] as JObject;
            if (polish != null)
            {
                rerolls = (int?)polish["extraWeeklyRerolls"] ?? 0;
                rollCap = (int?)polish["rollCapDelta"] ?? 0;
            }

            string[] events;
            JArray unlocked = block["unlockedEvents"] as JArray;
            if (unlocked == null)
            {
                events = new string[0];
            }
            else
            {
                events = new string[unlocked.Count];
                for (int i = 0; i < unlocked.Count; i++) events[i] = (string)unlocked[i] ?? string.Empty;
            }

            HeartboundBonuses.AcceptServerBenefits(tier, rate, rerolls, rollCap, events);
        }

        /// <summary>
        /// A decimal STRING of SKR base units -> long. Returns 0 on anything it
        /// cannot represent exactly, and says so — an unparseable amount must
        /// never become a plausible-looking one.
        /// </summary>
        private static long ParseRaw(JToken token, string field)
        {
            if (token == null || token.Type == JTokenType.Null) return 0L;
            string s = token.Type == JTokenType.String ? (string)token : token.ToString();
            if (string.IsNullOrWhiteSpace(s)) return 0L;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) && v >= 0L)
                return v;
            FlowTrace.Warn(Sys, "server " + field + " '" + s + "' did not parse as a non-negative " +
                                "integer of SKR base units; treating it as UNKNOWN (0) rather than " +
                                "as a number this build made up.");
            return 0L;
        }

        /// <summary>
        /// Drives the refresh cadence (spec :155-160): once a wallet session
        /// exists, and thereafter on the cache interval. The SERVER still decides
        /// whether a request reaches the chain; this only decides when to ask.
        /// </summary>
        internal sealed class Driver : MonoBehaviour
        {
            private const float PollSeconds = 300f;      // matches the server's 5-minute cache
            private const float IdleRetrySeconds = 5f;   // no wallet yet — cheap re-check
            private float _nextPoll;
            private string _wallet;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
            private static void Bootstrap()
            {
                var go = new GameObject("[HeartboundStatus]");
                DontDestroyOnLoad(go);
                go.AddComponent<Driver>();
            }

            private void Update()
            {
                if (Time.unscaledTime < _nextPoll) return;

                string current = WalletPreferenceStore.CurrentSessionWalletAddress;
                if (string.IsNullOrWhiteSpace(current))
                {
                    _nextPoll = Time.unscaledTime + IdleRetrySeconds;
                    return;
                }

                // A wallet CHANGE is one of the spec's refresh triggers
                // ("following wallet-link changes", :158) and must re-verify at
                // once rather than wait out the poll interval.
                bool walletChanged = !string.Equals(current, _wallet, StringComparison.Ordinal);
                if (walletChanged)
                {
                    FlowTrace.Step(Sys, "session wallet changed - re-verifying the stake immediately " +
                                        "instead of serving the previous wallet's snapshot.");
                    _wallet = current;
                }

                _nextPoll = Time.unscaledTime + PollSeconds;
                RefreshAsync(false).Forget();
            }
        }
#endif
    }
}
