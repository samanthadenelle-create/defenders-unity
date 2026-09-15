// =============================================================================
// EventTracker — player behaviour analytics (WO2, WO3, WO4).
// -----------------------------------------------------------------------------
// WO2: Basic event tracking — session_start, wave_completed, purchase_completed,
//      bundle_viewed. Fire-and-forget POST to api/events/track.
// WO3: Batching + offline — events are queued locally; flushed in batches of
//      up to BatchSize events every FlushIntervalSeconds. Queue survives scene
//      changes and app restarts via PlayerPrefs. Max 200 events queued (oldest
//      dropped when cap hit).
// WO4: Retry + circuit breaker — failed flushes retry with exponential backoff
//      (1s → 2s → 4s → 8s, max 4 attempts). After MaxConsecutiveFailures (5)
//      the circuit opens; it half-opens after CircuitCooldownSeconds (60) and
//      closes on the next successful flush. No infinite loops; no excessive
//      network during outages.
//
// USAGE:
//   EventTracker.Track("session_start");
//   EventTracker.Track("wave_completed", new { waveId = 3, duration = 47.2f });
//   EventTracker.Track("purchase_completed", new { packId = "founders_spark", price = 4.99f });
//   EventTracker.Track("bundle_viewed", new { bundleId = "hearth_bundle" });
//
// BACKEND CONTRACT:
//   POST api/events/track
//   Body: { "events": [ { "playerId", "eventName", "properties", "clientTs" }, ... ] }
//   Response: { "success": true }
//
// SETUP:
//   EventTracker.EnsureExists() is called by GameStateService.Awake.
//   No manual scene wiring needed — it persists across all scenes.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Core.Analytics
{
    /// <summary>
    /// Persistent analytics event tracker. DontDestroyOnLoad singleton.
    /// Call <see cref="Track"/> from anywhere; the bridge handles batching,
    /// offline persistence, and resilient delivery.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EventTracker : MonoBehaviour
    {
        // ── Configuration ─────────────────────────────────────────────────────

        private const string BackendBase       = "https://defenders-of-the-realm-v2.vercel.app";
        private const string TrackUrl          = BackendBase + "/api/events/track";
        private const string QueuePlayerPrefsKey = "dotr-event-queue";

        [Tooltip("Events are sent in batches of this size.")]
        [SerializeField] private int BatchSize = 10;

        [Tooltip("Flush the queue every N seconds even if BatchSize hasn't been reached.")]
        [SerializeField] private float FlushIntervalSeconds = 30f;

        [Tooltip("Max events held in the local queue. Oldest are dropped when exceeded.")]
        [SerializeField] private int MaxQueueSize = 200;

        // ── Circuit breaker ───────────────────────────────────────────────────

        private const int   MaxConsecutiveFailures = 5;
        private const float CircuitCooldownSeconds = 60f;
        private const int   MaxRetryAttempts       = 4;

        private enum CircuitState { Closed, Open, HalfOpen }

        private CircuitState _circuit          = CircuitState.Closed;
        private int          _consecutiveFails  = 0;
        private float        _circuitOpenedAt   = 0f;

        // ── Queue ─────────────────────────────────────────────────────────────

        private readonly List<TrackedEvent> _queue = new List<TrackedEvent>(32);
        private float _lastFlushTime;
        private bool  _flushing;
        private static EventTracker _instance;

        // ── Event record ──────────────────────────────────────────────────────

        [Serializable]
        private sealed class TrackedEvent
        {
            [JsonProperty("playerId")]   public string PlayerId   { get; set; }
            [JsonProperty("eventName")]  public string EventName  { get; set; }
            [JsonProperty("properties")] public string Properties { get; set; } // JSON string
            [JsonProperty("clientTs")]   public long   ClientTs   { get; set; } // unix ms
        }

        // ── Static API ────────────────────────────────────────────────────────

        /// <summary>
        /// Queues a named event with optional properties for delivery to the backend.
        /// Safe to call from any thread / any time — enqueues then returns immediately.
        /// </summary>
        /// <param name="eventName">
        /// Canonical snake_case event name: session_start, wave_completed,
        /// purchase_completed, bundle_viewed, or any custom name.
        /// </param>
        /// <param name="properties">
        /// Anonymous object of key/value properties, e.g.
        /// <c>new { waveId = 3, score = 120 }</c>. Serialized to JSON. May be null.
        /// </param>
        public static void Track(string eventName, object properties = null)
        {
            if (_instance == null)
            {
                Debug.LogWarning($"[EventTracker] Track('{eventName}') called before EnsureExists — event dropped.");
                return;
            }
            _instance.Enqueue(eventName, properties);
        }

        /// <summary>
        /// Bootstraps the singleton. Called by GameStateService.Awake.
        /// No-op if already alive.
        /// </summary>
        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("[EventTracker]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<EventTracker>();
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadQueueFromPrefs();
        }

        private void Start()
        {
            // Fire session_start immediately on boot.
            Enqueue("session_start", new
            {
                platform     = Application.platform.ToString(),
                appVersion   = Application.version,
                unityVersion = Application.unityVersion,
            });

            StartCoroutine(FlushLoop());
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveQueueToPrefs();
        }

        private void OnApplicationQuit()
        {
            SaveQueueToPrefs();
        }

        // ── Enqueue ───────────────────────────────────────────────────────────

        private void Enqueue(string eventName, object properties)
        {
            // WO-1735. ONE identity source for this file: the same accessor the save rail
            // uses (BackendRequestSigner.CurrentPlayerId -> GameState.BoundWallet, trimmed,
            // empty when there is no account at all). Reading BoundWallet directly here was
            // a SECOND copy of the identity rule; CLAUDE.md §2/§5/§16 all name duplicated
            // state as the defect. "anonymous" is preserved for the no-account case because
            // the server's guest-shape regex rejects it either way, so nothing changes for
            // events queued before EnsureAccount mints the guest id.
            string resolvedId = DeNelle.Core.Backend.BackendRequestSigner.CurrentPlayerId();
            string playerId   = string.IsNullOrEmpty(resolvedId) ? "anonymous" : resolvedId;

            string propsJson = properties != null
                ? JsonConvert.SerializeObject(properties)
                : "{}";

            var ev = new TrackedEvent
            {
                PlayerId   = playerId,
                EventName  = eventName,
                Properties = propsJson,
                ClientTs   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            lock (_queue)
            {
                // Cap: drop oldest when full.
                if (_queue.Count >= MaxQueueSize)
                {
                    _queue.RemoveAt(0);
                    Debug.LogWarning("[EventTracker] Queue cap reached — oldest event dropped.");
                }
                _queue.Add(ev);
            }

#if UNITY_EDITOR
            Debug.Log($"[EventTracker] Queued '{eventName}' | props={propsJson}");
#endif

            // Flush immediately when batch is full.
            if (_queue.Count >= BatchSize && !_flushing)
                FlushAsync().Forget();
        }

        // ── Flush loop (WO3) ─────────────────────────────────────────────────

        private IEnumerator FlushLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(FlushIntervalSeconds);
                if (_queue.Count > 0 && !_flushing)
                    FlushAsync().Forget();
            }
        }

        // ── Flush with retry + circuit breaker (WO4) ─────────────────────────

        private async UniTaskVoid FlushAsync()
        {
            if (_flushing) return;
            _flushing = true;

            try
            {
                await FlushWithRetry();
            }
            finally
            {
                _flushing = false;
            }
        }

        private async UniTask FlushWithRetry()
        {
            // Circuit breaker — open: refuse immediately; half-open: allow one probe.
            if (_circuit == CircuitState.Open)
            {
                if (Time.realtimeSinceStartup - _circuitOpenedAt < CircuitCooldownSeconds)
                {
                    Debug.LogWarning("[EventTracker] Circuit open — flush skipped.");
                    return;
                }
                _circuit = CircuitState.HalfOpen;
                Debug.Log("[EventTracker] Circuit half-open — probing backend.");
            }

            List<TrackedEvent> batch;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                int take = Mathf.Min(_queue.Count, BatchSize);
                batch = new List<TrackedEvent>(_queue.GetRange(0, take));
            }

            bool success = false;
            int  delayMs = 1000;

            for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    Debug.Log($"[EventTracker] Retry {attempt}/{MaxRetryAttempts} in {delayMs}ms…");
                    await UniTask.Delay(delayMs);
                    delayMs = Mathf.Min(delayMs * 2, 8000); // cap at 8s
                }

                success = await SendBatch(batch);
                if (success) break;
            }

            if (success)
            {
                // Remove delivered events from the queue.
                lock (_queue)
                    _queue.RemoveRange(0, Mathf.Min(batch.Count, _queue.Count));

                SaveQueueToPrefs();
                OnFlushSuccess();
            }
            else
            {
                SaveQueueToPrefs(); // persist for next session
                OnFlushFailure();
            }
        }

        private async UniTask<bool> SendBatch(List<TrackedEvent> batch)
        {
            var payload = JsonConvert.SerializeObject(new { events = batch });
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(payload);

            using var req = new UnityWebRequest(TrackUrl, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            // ── WO-1735: IDENTITY HEADERS ────────────────────────────────────────────
            // Until now this request carried Content-Type and nothing else, so
            // api/events/track.js had no header to resolve an identity from and EVERY
            // player collapsed into the single row id "unverified" (2026-09-07 onward).
            //
            // The seam REUSED (never a second copy of the header names):
            //   BackendRequestSigner.TryAttachCachedSession(req, playerId)
            //   (Assets/_Modules/Core/Backend/BackendRequestSigner.cs:421-434)
            // It attaches X-Guest-Id for a guest id, or X-Session + X-Wallet for a wallet
            // that already holds a live session. It is the NON-MINTING, NON-SIGNING
            // variant: it never awaits, never opens a wallet SignMessage sheet, never
            // touches the network, and never spends the guest_rate_limit budget that
            // game/save and game/load share (WO-1735 §4).
            //
            // ⛔ UNLIKE EVERY OTHER CALLER OF THIS SEAM, WE DO NOT ABORT ON false.
            //    SkuEntitlementService, CommunityShowcaseVoting and GameStateService all
            //    fail closed because an unauthenticated read/write of player state is a
            //    security question. Analytics is fire-and-forget telemetry: a wallet
            //    player with no session yet (the DOCUMENTED boot state - the token is
            //    memory-only by design) must still deliver the batch, exactly as it did
            //    before this change. Returning false here would turn a fixed attribution
            //    bug into a dropped-telemetry bug.
            string identityPlayerId = ResolveIdentityPlayerId();
            bool   identityAttached =
                DeNelle.Core.Backend.BackendRequestSigner.TryAttachCachedSession(req, identityPlayerId);
            ReportIdentityMode(identityPlayerId, identityAttached);

            try
            {
                await req.SendWebRequest();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventTracker] Send exception: {ex.Message}");
                return false;
            }

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[EventTracker] Flushed {batch.Count} event(s) OK.");
                return true;
            }

            Debug.LogWarning($"[EventTracker] Flush failed ({req.responseCode}): {req.error}");
            return false;
        }

        // ── WO-1735: identity resolution + instrumentation ────────────────────

        /// <summary>
        /// The id the identity headers are attached FOR. Same accessor as the save rail
        /// (BackendRequestSigner.CurrentPlayerId); empty means there is no account at all,
        /// which the seam correctly refuses rather than inventing an anonymous identity.
        /// </summary>
        private static string ResolveIdentityPlayerId()
        {
            try { return DeNelle.Core.Backend.BackendRequestSigner.CurrentPlayerId(); }
            catch { return string.Empty; }
        }

        /// <summary>
        /// CLAUDE.md §12. Name the identity mode this tracker is actually sending, ONCE per
        /// mode per session, so the next reader of a break-log never has to infer from the
        /// row counts which rail the client used. Keyed on the MODE (not a fixed string) so a
        /// wallet that mints a session mid-run logs the none -> wallet transition instead of
        /// staying silent behind the first Once.
        /// </summary>
        /// <summary>
        /// One-shot keys for the 'none' rail. FlowTrace has Once (Step level) and Throttle,
        /// but NO Fail-level once variant — read at source, FlowTrace.cs:157-238. Without
        /// this set the Fail below would fire on EVERY flush (every 30s, or every 10 events,
        /// and again on each of up to 4 retries) for the whole session, which is the log
        /// firehose CLAUDE.md §12 warns about: it evicts the boot window out of the device
        /// logcat ring and destroys the very evidence the trace exists to preserve.
        /// </summary>
        private static readonly HashSet<string> _identityModeReported = new HashSet<string>();

        private static void ReportIdentityMode(string playerId, bool attached)
        {
            try
            {
                bool guest = DeNelle.Core.Backend.BackendRequestSigner.IsGuestIdentity(playerId);
                string mode = attached ? (guest ? "guest" : "wallet") : "none";

                if (attached)
                {
                    string header = guest ? "X-Guest-Id" : "X-Session + X-Wallet";
                    DeNelle.Core.Diagnostics.FlowTrace.Once(
                        "Analytics", "identity-mode:" + mode,
                        "EventTracker is sending identity mode '" + mode + "' (" + header +
                        ") on every /api/events/track POST, via the save rail's own seam " +
                        "BackendRequestSigner.TryAttachCachedSession. Rows should land as _auth:'" +
                        (guest ? "guest" : "session") + "', not 'unverified' (WO-1735).");
                    return;
                }

                // No header attached. Two DIFFERENT causes, and conflating them is how the
                // original bug hid for eight days - so the trace names which one it is.
                bool noAccount = string.IsNullOrEmpty(playerId);

                // Once per CAUSE, for the reason on _identityModeReported. Keyed on the cause
                // and not on "none" so that a player who starts with no account and later
                // binds a wallet still gets the second, different, Fail.
                string causeKey = noAccount ? "none:no-account" : "none:wallet-without-session";
                lock (_identityModeReported)
                {
                    if (!_identityModeReported.Add(causeKey)) return;
                }

                string why = noAccount
                    ? "there is no account id at all yet (BoundWallet empty - events queued " +
                      "before GameStateService.EnsureAccount mints the guest id)"
                    : "a WALLET identity is bound but no backend session is held in memory. " +
                      "This is the DOCUMENTED boot state, not a defect: the session token is " +
                      "memory-only by design and boot never signs (ruling 2026-09-07), so a " +
                      "fresh process legitimately starts here until a purchase, a promo code " +
                      "or an explicit Connect tap mints one";
                if (noAccount)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Fail(
                        "Analytics",
                        "EventTracker is sending identity mode 'none' - no X-Guest-Id and no " +
                        "X-Session on /api/events/track, so these rows land under the shared id " +
                        "'unverified' and this player is not separable in the dashboard. Cause: " +
                        why + ". The batch is still delivered ON PURPOSE (analytics is " +
                        "fire-and-forget; it must never fail closed).");
                }
                else
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn(
                        "Analytics",
                        "EventTracker is sending identity mode 'none' - no X-Guest-Id and no " +
                        "X-Session on /api/events/track, so these rows land under the shared id " +
                        "'unverified' and this player is not separable in the dashboard. Cause: " +
                        why + ". The batch is still delivered ON PURPOSE (analytics is " +
                        "fire-and-forget; it must never fail closed).");
                }
            }
            catch (Exception ex)
            {
                // Instrumentation must never be the thing that breaks a flush.
                Debug.LogWarning("[EventTracker] identity-mode trace failed: " + ex.Message);
            }
        }

        // ── Circuit breaker state transitions ─────────────────────────────────

        private void OnFlushSuccess()
        {
            _consecutiveFails = 0;
            if (_circuit != CircuitState.Closed)
            {
                _circuit = CircuitState.Closed;
                Debug.Log("[EventTracker] Circuit closed — backend healthy.");
            }
        }

        private void OnFlushFailure()
        {
            _consecutiveFails++;
            if (_consecutiveFails >= MaxConsecutiveFailures && _circuit == CircuitState.Closed)
            {
                _circuit        = CircuitState.Open;
                _circuitOpenedAt = Time.realtimeSinceStartup;
                Debug.LogWarning($"[EventTracker] Circuit opened after {_consecutiveFails} failures. " +
                                 $"Retrying in {CircuitCooldownSeconds}s.");
            }
            else if (_circuit == CircuitState.HalfOpen)
            {
                // Probe failed — reopen.
                _circuit         = CircuitState.Open;
                _circuitOpenedAt  = Time.realtimeSinceStartup;
                Debug.LogWarning("[EventTracker] Circuit re-opened — probe failed.");
            }
        }

        // ── Offline persistence (WO3) ─────────────────────────────────────────

        private void SaveQueueToPrefs()
        {
            lock (_queue)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_queue);
                    PlayerPrefs.SetString(QueuePlayerPrefsKey, json);
                    PlayerPrefs.Save();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EventTracker] Failed to persist queue: {ex.Message}");
                }
            }
        }

        private void LoadQueueFromPrefs()
        {
            var raw = PlayerPrefs.GetString(QueuePlayerPrefsKey, "[]");
            try
            {
                var loaded = JsonConvert.DeserializeObject<List<TrackedEvent>>(raw);
                if (loaded != null && loaded.Count > 0)
                {
                    lock (_queue)
                    {
                        // Merge — avoid duplicates by ClientTs. Simpler than full dedup.
                        var existingTs = new HashSet<long>();
                        foreach (var e in _queue) existingTs.Add(e.ClientTs);
                        foreach (var e in loaded)
                            if (!existingTs.Contains(e.ClientTs)) _queue.Add(e);

                        // Re-cap if merged queue is too large.
                        while (_queue.Count > MaxQueueSize)
                            _queue.RemoveAt(0);
                    }
                    Debug.Log($"[EventTracker] Loaded {loaded.Count} queued event(s) from PlayerPrefs.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventTracker] Failed to load persisted queue: {ex.Message}");
                PlayerPrefs.DeleteKey(QueuePlayerPrefsKey);
            }
        }
    }
}
