// =============================================================================
// HonestFeedbackService (WO-1432) - the OFFER GATE and the BACKEND CALL for the
// honest-feedback thank-you.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Feedback
//
// Two jobs, and nothing else:
//   1. decide WHEN the one-time offer may appear, and open it through the
//      single-modal arbiter (PanelRouter -> PanelManager). It never
//      AddComponents a panel to show it.
//   2. POST the player's words to the backend this project already owns and,
//      only on a response that says the server STORED them, call the one grant
//      seam (HonestFeedbackGrant.TryApply).
//
// It is also HonestFeedbackPanel's D1 DOOR: this file is outside the panel's
// PanelDoorRegression "home set" and names the type in real code, so the panel
// is provably reachable from something that is not its own View/VM loop and is
// not a capture harness. Do not remove the FindFirstObjectByType<HonestFeedbackPanel>
// guard in TryOpenOffer to "tidy up" - it is a real diagnostic (host-missing vs
// arbiter-refused are different bugs) AND the door the oracle reads.
//
// -----------------------------------------------------------------------------
// WHY THE OFFER IS NOT A TIMER
// -----------------------------------------------------------------------------
// WO-1432 section 2d: a wall-clock timer can fire mid-raid, mid-tutorial, or at a
// player who is losing. All three gates must hold at once:
//   (1) a POSITIVE BEAT just completed - first wave cleared or first building
//       finished, whichever lands first (both are subscribed; either one arms it);
//   (2) cumulative session time past HonestFeedbackTuning.MinSessionSeconds
//       (authored in JSON - the owner said "first few minutes" and never ruled a
//       number, so it is tuned, not recompiled);
//   (3) onboarding done (GameState.Onboarded - the real FTUE gate; TutorialStep
//       is NOT it) and no other modal open, and the arbiter accepts the open.
//
// ⚠ THE BEAT IS NOT CONSUMED BY A FAILED ATTEMPT. If a modal is open when the
// beat lands, the offer would be lost forever on a fire-once design - so the beat
// ARMS a latch and the gate re-asks itself every RecheckIntervalSeconds until it
// gets in. "Offered" is recorded only after PanelRouter.Open actually returns true.
//
// -----------------------------------------------------------------------------
// SESSION TIME IS IN-SESSION, AND THAT IS DELIBERATE
// -----------------------------------------------------------------------------
// No cumulative-playtime service exists in this repo (searched 2026-09-06:
// SessionTime / PlayTime / TimePlayed / PlaySeconds / SessionClock - zero hits;
// GameState carries no elapsed field). Adding a persisted total would mean a new
// GameState field, which means a save-schema bump, which WO-1432 section 2c
// explicitly forbids. So this counts UNSCALED time in the current app session,
// which is also the plain reading of "a screen after the first few minutes".
//
// -----------------------------------------------------------------------------
// ⛔ WHAT THIS FILE MUST NEVER GROW (WO-1432 section 3, absolute)
// -----------------------------------------------------------------------------
//   * NO sentiment gate. There is one text box and one Send button. No "how are
//     you enjoying it?" fork routing happy players to the store and unhappy ones
//     to a suggestion box - that is review-gating, it violates Play and Apple
//     policy on its own, and it contradicts the owner's ruling ("its contingent
//     on an honest review", not a positive one).
//   * NO claim that a review happened or was verified - in code, copy, analytics
//     or telemetry. No app store tells a client that a review was left or what it
//     said; anything asserting otherwise is a lie in the build. Note that no
//     identifier here contains the word "review".
//   * NO second grant path. HonestFeedbackGrant.TryApply is the only one.
//
// -----------------------------------------------------------------------------
// WHAT "VERIFIED BY OUR OWN BACKEND" HONESTLY MEANS HERE - read before trusting it
// -----------------------------------------------------------------------------
// The grant hangs on api/bug-report.js answering 200 with success:true. That
// endpoint genuinely CHECKS something: it rejects a report with no note and no
// capture ("Empty report" -> 400) and it returns the row id it actually stored.
// So "the player wrote something and the server has it" is a real, server-made
// statement, not a client assertion.
//
// THREE LIMITS, NAMED RATHER THAN PAPERED OVER:
//   (a) the endpoint does NOT enforce one-grant-per-player. The repeat-claim
//       guard is entirely client-side (HonestFeedbackKeys.GrantClaimedKey). A
//       modified client could re-post and re-grant into its own local save.
//   (b) identity is fail-CLOSED here (no identity -> no post -> no grant, the
//       ReferralService shape), but only the WALLET rail is server-verified:
//       BackendRequestSigner attaches X-Session for a wallet, which bug-report.js
//       verifies via verifySession. A guest's X-Guest-Id is not read by that
//       route at all.
//   (c) a wallet player with no live session cannot post at all - TryAttachAsync
//       fails closed on a non-purchase route rather than raising a SignMessage
//       sheet mid-walk (WO-1157/WO-1211). That is correct behaviour and it means
//       some players will see "could not verify your account".
// Closing (a) and (b) properly is a backend change (an identity-gated feedback
// route with a one-per-player constraint) and is deliberately NOT smuggled into
// this player-facing lane.
//
// Instrumentation: FlowTrace tag "HonestFeedback". Permanent - CLAUDE.md sec.12.
// ASCII only.
// =============================================================================

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Analytics;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Core.Web3;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.Feedback
{
    /// <summary>How a feedback submit ended. Presentation maps this to a sentence.</summary>
    public enum FeedbackSubmitResult
    {
        /// <summary>Server stored it AND the thank-you landed.</summary>
        StoredAndGranted = 0,
        /// <summary>Server stored it, but the grant was a no-op (already claimed).
        /// The words still reached us - that is not a failure to report.</summary>
        StoredAlreadyClaimed = 1,
        /// <summary>The player typed too little for the server to accept.</summary>
        TooShort = 2,
        /// <summary>No usable identity, so nothing was sent (fail-closed).</summary>
        NoIdentity = 3,
        /// <summary>The request never reached the server, or it refused it.</summary>
        NetworkFailed = 4,
        /// <summary>Server answered, but not with a stored-it response.</summary>
        ServerRefused = 5,
        /// <summary>Stored, but the grant could not resolve the economy. The flag is NOT
        /// burned, so a later attempt can still pay.</summary>
        StoredGrantUnavailable = 6,
    }

    [DisallowMultipleComponent]
    public sealed class HonestFeedbackService : MonoBehaviour
    {
        private const string Sys = HonestFeedbackGrant.Sys;

        /// <summary>The feedback sink. Same endpoint BugReportVM posts to; the row is told
        /// apart by its SessionIdPrefix, so feedback is never triaged as a crash report.</summary>
        private const string Endpoint = BackendRequestSigner.BackendBase + "/api/bug-report";

        /// <summary>Marks the row as WO-1432 feedback rather than a bug report
        /// (BugReportVM uses "br-"). Lands in the stored context blob.</summary>
        public const string SessionIdPrefix = "hf-";

        private const int RequestTimeoutSeconds = 20;

        public static HonestFeedbackService Instance { get; private set; }

        // ── Offer-gate state (all in-session; nothing here is persisted) ─────────
        private float _sessionSeconds;
        private float _nextCheckAt;
        private bool _beatLanded;
        private string _beatName = "none";

        private WaveManager _subscribedWaves;
        private BuildTimerService _subscribedTimers;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            FlowTrace.Step(Sys, "HonestFeedbackService awake - offer gate armed (minSessionSeconds " +
                                HonestFeedbackTuning.MinSessionSeconds.ToString("0.#") + ").");
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            _sessionSeconds += Time.unscaledDeltaTime;
            if (Time.unscaledTime < _nextCheckAt) return;
            _nextCheckAt = Time.unscaledTime + HonestFeedbackTuning.RecheckIntervalSeconds;

            // Cheap and idempotent: the wave manager and the build timer are per-scene
            // singletons that come and go, so the subscription is re-established here
            // rather than assumed to survive a scene load.
            EnsureSubscribed();

            // Nothing left to do once the offer has been shown or the grant claimed.
            if (HonestFeedbackGrant.HasClaimed() || HonestFeedbackGrant.HasBeenOffered())
            {
                Unsubscribe();
                enabled = false;
                FlowTrace.Step(Sys, "offer gate stood down - already " +
                    (HonestFeedbackGrant.HasClaimed() ? "claimed" : "offered") + " on this save.");
                return;
            }

            if (IsEligible(out string why)) TryOpenOffer();
            else FlowTrace.Throttle(Sys, "offer-gate", 30f, "offer not yet eligible: " + why);
        }

        // =====================================================================
        //  The positive beat
        // =====================================================================

        private void EnsureSubscribed()
        {
            var waves = WaveManager.Instance;
            if (waves != _subscribedWaves)
            {
                if (_subscribedWaves != null && _subscribedWaves.OnWaveCleared != null)
                    _subscribedWaves.OnWaveCleared.RemoveListener(OnWaveCleared);
                _subscribedWaves = waves;
                if (_subscribedWaves != null && _subscribedWaves.OnWaveCleared != null)
                {
                    _subscribedWaves.OnWaveCleared.AddListener(OnWaveCleared);
                    FlowTrace.Step(Sys, "subscribed to WaveManager.OnWaveCleared (positive-beat source 1 of 2).");
                }
            }

            var timers = BuildTimerService.Instance;
            if (timers != _subscribedTimers)
            {
                if (_subscribedTimers != null) _subscribedTimers.JobCompleted -= OnJobCompleted;
                _subscribedTimers = timers;
                if (_subscribedTimers != null)
                {
                    _subscribedTimers.JobCompleted += OnJobCompleted;
                    FlowTrace.Step(Sys, "subscribed to BuildTimerService.JobCompleted (positive-beat source 2 of 2).");
                }
            }
        }

        private void Unsubscribe()
        {
            if (_subscribedWaves != null && _subscribedWaves.OnWaveCleared != null)
                _subscribedWaves.OnWaveCleared.RemoveListener(OnWaveCleared);
            if (_subscribedTimers != null) _subscribedTimers.JobCompleted -= OnJobCompleted;
            _subscribedWaves = null;
            _subscribedTimers = null;
        }

        private void OnWaveCleared(int wave) => ArmBeat("wave " + wave + " cleared");

        // BuildJobData is a STRUCT in DeNelle.Core.State (not a class, and not in Core.Jobs) -
        // so there is nothing to null-check here, and its id field is StructureId.
        private void OnJobCompleted(DeNelle.Core.State.BuildJobData job)
            => ArmBeat("build job finished (" + (string.IsNullOrEmpty(job.StructureId) ? "unknown" : job.StructureId) + ")");

        /// <summary>
        /// Arms the latch. It is a LATCH, not a fire-once: if the offer cannot open right now
        /// (a modal is up, a battle is live, the session is still young) the beat is remembered
        /// and the gate re-asks. A consumed beat is how this offer would silently never fire.
        /// </summary>
        private void ArmBeat(string what)
        {
            if (_beatLanded) return;
            _beatLanded = true;
            _beatName = what;
            FlowTrace.Step(Sys, "positive beat armed: " + what + ". The offer may now open once the " +
                                "session-time and modal gates also pass.");
        }

        // =====================================================================
        //  Eligibility + open
        // =====================================================================

        /// <summary>
        /// Every gate, with the FAILING one named. A bare bool here would make "the panel never
        /// showed up" an unanswerable question.
        /// </summary>
        public bool IsEligible(out string why)
        {
            why = null;
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null) { why = "no live GameState"; return false; }
            if (!state.Onboarded) { why = "onboarding not finished (GameState.Onboarded false)"; return false; }
            if (HonestFeedbackGrant.HasClaimed()) { why = "thank-you already claimed"; return false; }
            if (HonestFeedbackGrant.HasBeenOffered()) { why = "offer already shown once"; return false; }
            if (!_beatLanded) { why = "no positive beat yet (need a wave cleared or a build finished)"; return false; }
            if (_sessionSeconds < HonestFeedbackTuning.MinSessionSeconds)
            {
                why = $"session {_sessionSeconds:0}s < authored minimum {HonestFeedbackTuning.MinSessionSeconds:0}s";
                return false;
            }
            if (DeNelle.Core.Combat.BattleLock.IsInBattle()) { why = "a battle is live"; return false; }
            // Build Mode is not a PanelManager modal: it owns its own HUD and pointer
            // contract, so AnyOpen remains false while a placement/move is active. An
            // automatic offer here would cover the PLACE rail and steal the confirming
            // click while leaving the ghost alive underneath (WO-1612 device repro).
            if (DeNelle.Core.BuildModeState.IsActive) { why = "build mode is active"; return false; }
            if (PanelManager.AnyOpen) { why = "another modal is open (" + PanelManager.OpenPanelName + ")"; return false; }
            if (PanelManager.InCloseGrace) { why = "inside the arbiter close-grace window"; return false; }
            return true;
        }

        /// <summary>
        /// Opens the offer THROUGH THE ARBITER. Never AddComponents a panel to show it - the
        /// host is installed at scene load by HonestFeedbackPanelBootstrap; this only routes.
        /// "Offered" is recorded ONLY when the router confirms the panel actually became
        /// visible, so a refused open is retried instead of silently burned.
        /// </summary>
        public bool TryOpenOffer()
        {
            // ⛔ This lookup is load-bearing twice over: it separates "no host in this scene"
            // from "the arbiter said no" (two different bugs with two different fixes), and it
            // is HonestFeedbackPanel's D1 door for PanelDoorRegression. Do not delete it.
            var host = UnityEngine.Object.FindFirstObjectByType<HonestFeedbackPanel>(FindObjectsInactive.Include);
            if (host == null)
            {
                FlowTrace.Warn(Sys, "offer is eligible but no HonestFeedbackPanel host exists in any loaded " +
                                    "scene - HonestFeedbackPanelBootstrap did not install one here. Retrying " +
                                    "on the next check rather than dropping the offer.");
                return false;
            }

            if (!PanelRouter.Open(PanelId.HonestFeedback))
            {
                FlowTrace.Warn(Sys, "PanelRouter.Open(PanelId.HonestFeedback) returned false (unregistered, or " +
                                    "the arbiter refused / nothing became visible). The offer is NOT recorded as " +
                                    "shown, so it will be retried.");
                return false;
            }

            HonestFeedbackGrant.MarkOffered();
            EventTracker.Track("honest_feedback_offer_shown", new
            {
                beat = _beatName,
                sessionSeconds = Mathf.RoundToInt(_sessionSeconds),
            });
            FlowTrace.Step(Sys, $"offer OPENED after beat '{_beatName}' at {_sessionSeconds:0}s of session time.");
            return true;
        }

        // =====================================================================
        //  The backend call + the grant
        // =====================================================================

        /// <summary>
        /// POSTs the player's words, then - ONLY on a response that says the server stored
        /// them - calls the one grant seam.
        /// <para><paramref name="onApplied"/> is handed what actually landed in the wallet so the
        /// panel can show the real numbers rather than the promised ones.</para>
        /// </summary>
        public async UniTask<FeedbackSubmitResult> SubmitAsync(string note, Action<ResourceCost> onApplied = null)
        {
            note = (note ?? string.Empty).Trim();
            if (note.Length < HonestFeedbackTuning.MinCharacters)
            {
                FlowTrace.Step(Sys, $"submit refused locally: {note.Length} chars < authored minimum " +
                                    $"{HonestFeedbackTuning.MinCharacters}. The server would answer 400 " +
                                    "'Empty report' for a blank one anyway.");
                return FeedbackSubmitResult.TooShort;
            }
            if (note.Length > HonestFeedbackTuning.MaxCharacters)
                note = note.Substring(0, HonestFeedbackTuning.MaxCharacters);

            // Identity-gated, fail-closed - the ReferralService shape. No identity, no post,
            // no grant. See the file header for exactly how much of this the server verifies.
            var playerId = BackendRequestSigner.CurrentPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                FlowTrace.Warn(Sys, "no player identity - the feedback post is ABORTED (fail-closed) and " +
                                    "nothing is granted.");
                return FeedbackSubmitResult.NoIdentity;
            }

            string payload = BuildPayloadJson(note, playerId);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);

            using var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = RequestTimeoutSeconds;

            if (!await BackendRequestSigner.TryAttachAsync(req, playerId, bodyRaw))
            {
                FlowTrace.Warn(Sys, "BackendRequestSigner could not authenticate the feedback post - " +
                                    "ABORTED rather than sent unauthed. Nothing granted.");
                return FeedbackSubmitResult.NoIdentity;
            }

            try { await req.SendWebRequest(); }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, $"feedback POST threw {ex.GetType().Name}: {ex.Message}. Nothing granted.");
                return FeedbackSubmitResult.NetworkFailed;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                FlowTrace.Warn(Sys, $"feedback POST failed: result={req.result} http={req.responseCode}. " +
                                    "Nothing granted.");
                return FeedbackSubmitResult.NetworkFailed;
            }

            StoreResponse resp = null;
            try { resp = JsonConvert.DeserializeObject<StoreResponse>(req.downloadHandler.text); }
            catch (Exception e)
            {
                FlowTrace.Warn(Sys, $"feedback response deserialize threw {e.GetType().Name}: {e.Message}. " +
                                    "Treating as NOT stored - nothing granted.");
                return FeedbackSubmitResult.ServerRefused;
            }

            // ⭐ THE VERIFIED-RESPONSE TEST. Not "the call returned" - the SERVER SAID it stored
            // the row. api/bug-report.js answers 400 'Empty report' when there is nothing to
            // store, so a success here is a statement the server made about real content.
            if (resp == null || !resp.Success)
            {
                FlowTrace.Warn(Sys, "server answered without success:true (" +
                                    (resp == null ? "unparsed" : ("error=" + (resp.Error ?? "none"))) +
                                    ") - nothing granted.");
                return FeedbackSubmitResult.ServerRefused;
            }

            FlowTrace.Step(Sys, $"feedback STORED by the server (http {req.responseCode}, reportId=" +
                                $"{(resp.ReportId.HasValue ? resp.ReportId.Value.ToString() : "none")}, " +
                                $"shape={resp.Shape ?? "unknown"}, {note.Length} chars). Applying the thank-you.");
            EventTracker.Track("honest_feedback_submitted", new { characters = note.Length, shape = resp.Shape });

            var outcome = HonestFeedbackGrant.TryApply(out var applied);
            onApplied?.Invoke(applied);

            switch (outcome)
            {
                case ThankYouGrantOutcome.Applied:
                    EventTracker.Track("thank_you_grant_applied", new
                    {
                        wood = applied.Wood,
                        stone = applied.Food,
                        iron = applied.Iron,
                    });
                    return FeedbackSubmitResult.StoredAndGranted;
                case ThankYouGrantOutcome.AlreadyClaimed:
                    return FeedbackSubmitResult.StoredAlreadyClaimed;
                default:
                    return FeedbackSubmitResult.StoredGrantUnavailable;
            }
        }

        /// <summary>
        /// The request body. Deliberately carries NO screenshot and NO trace tail - this is a
        /// player writing to us, not a crash capture, and shipping a screenshot of whatever was
        /// on screen would be collecting more than was offered.
        /// <para>⛔ The raw identity is NOT sent. On the guest rail "the id IS the credential"
        /// (BackendRequestSigner.cs:14-16), so putting it in the body would land a live
        /// credential in a queryable row. A SALTED hash goes instead - the BugReportVM rule.</para>
        /// </summary>
        public static string BuildPayloadJson(string note, string playerId)
        {
            string idHash = string.IsNullOrEmpty(playerId)
                ? null
                : BackendRequestSigner.Sha256Hex(Encoding.UTF8.GetBytes("eoa-honestfeedback-v1|" + playerId));

            return JsonConvert.SerializeObject(new
            {
                note,
                sceneName = SceneManager.GetActiveScene().name,
                sessionId = SessionIdPrefix + (idHash != null ? idHash.Substring(0, 12) : "anon"),
                version = Application.version,
                platform = Application.platform.ToString(),
                language = DeNelle.Core.UI.LocalText.LanguageCode,
                systemLanguage = Application.systemLanguage.ToString(),
                playerId = idHash,
                traceTail = Array.Empty<string>(),
            });
        }

        private sealed class StoreResponse
        {
            [JsonProperty("success")] public bool Success { get; set; }
            [JsonProperty("reportId")] public long? ReportId { get; set; }
            [JsonProperty("shape")] public string Shape { get; set; }
            [JsonProperty("error")] public string Error { get; set; }
        }
    }
}
