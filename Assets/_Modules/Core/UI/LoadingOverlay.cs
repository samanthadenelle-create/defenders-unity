// =============================================================================
// LoadingOverlay — a reusable "hang on, we're loading" screen (owner felt-test
// 2026-07-24: "when I click Load Default / Design My Own there's a long delay —
// add a loading screen").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// THE PROBLEM: the founding choice (FoundingChoiceController) tears its overlay
// DOWN and then fires SceneRouter.GoCastle — a fade-load of the big Castle/hub
// scene (which also streams OuterWorld additively). Because the founding overlay
// is destroyed FIRST, the screen goes blank/frozen for the whole scene load. This
// overlay fills that gap: a dark cover + a centred message + a small spinner that
// SURVIVES the scene load (DontDestroyOnLoad) and auto-dismisses once the first
// hub frame has rendered.
//
// CODE-BUILT uGUI on its OWN ScreenSpaceOverlay Canvas — NOT UXML (UIDocuments come
// up EMPTY in player builds, PIPELINE_STATE Section 8). Built the same way as
// BuildFeedbackToast (Canvas + CanvasScaler + CanvasGroup on its own root object)
// so it is fresh-clone-safe, WebGL-safe, and needs no scene wiring. One at a time.
// ASCII-only structural strings; the caller passes the display message.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;
using System.Collections;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// A self-contained, code-built uGUI loading cover on its own ScreenSpaceOverlay
    /// canvas. Call <see cref="Show(string)"/> immediately BEFORE kicking off a scene
    /// load; it <c>DontDestroyOnLoad</c>s itself so it stays up while the scene loads,
    /// then auto-dismisses a short settle after the first scene load (with a
    /// minimum-show floor to avoid a flash and a hard max-show failsafe so it can
    /// never stick). Only one shows at a time. <see cref="Hide"/> dismisses early.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadingOverlay : MonoBehaviour
    {
        // ── Timing (all in UNSCALED seconds so a paused timescale never freezes us) ──
        /// <summary>Minimum time the overlay stays up, so a fast load doesn't just flash.</summary>
        private const float MinShowSeconds = 0.4f;
        /// <summary>Settle after the first scene load, to let the first hub frame render.</summary>
        private const float SettleSeconds = 0.5f;
        /// <summary>Hard failsafe: never stick longer than this even if no scene ever loads.</summary>
        private const float MaxShowSeconds = 30f;
        /// <summary>Fade-out duration when dismissing.</summary>
        private const float FadeSeconds = 0.3f;
        /// <summary>Time for one full 0->100% sweep of the indeterminate loading bar.</summary>
        private const float BarSweepSeconds = 1.1f;

        // One on-screen at a time.
        private static LoadingOverlay s_active;

        private CanvasGroup _group;
        private RectTransform _barFill;   // the animated fill of the standard loading bar
        private TMPro.TextMeshProUGUI _messageLabel;
        private Button _retryButton;
        private Transform _cardHost;
        private bool _connectionRequired;
        private bool _retrying;
        private float _barT;              // 0..1 sweep progress (indeterminate)

        private float _shownAt;          // unscaled time the overlay was shown
        private bool _sceneLoaded;       // has a scene loaded since Show?
        private float _sceneLoadedAt;    // unscaled time of that first scene load

        private bool _dismissing;
        private float _dismissStartedAt;

        /// <summary>
        /// Put the loading cover up (idempotent: a second call while one is live is a
        /// no-op). Survives the next scene load and auto-dismisses once the first hub
        /// frame has settled. Call this immediately BEFORE starting the scene load.
        /// </summary>
        // WO-1857: the default cannot be a localized value — a C# optional parameter must be a
        // compile-time constant, so a key resolved here would be baked at the CALL SITE in
        // English. `null` means "use the localized default", resolved below at display time.
        public static void Show(string message = null)
        {
            if (s_active != null) return;   // one at a time

            if (string.IsNullOrWhiteSpace(message))
                message = new LocalizedText("loading.overlay.realm").Resolve();

            FlowTrace.Step("LoadingOverlay", $"Show '{message}'");

            var go = new GameObject("LoadingOverlay");
            var ui = go.AddComponent<LoadingOverlay>();
            ui.Build(message);
            Object.DontDestroyOnLoad(go);   // survive the scene load
            s_active = ui;

            SceneManager.sceneLoaded += ui.OnSceneLoaded;
        }

        /// <summary>
        /// Blocks a disconnected first run whose content is not cached for this build.
        /// Retry re-enters <see cref="OfflineContentService.ResolveContentSource"/>;
        /// this overlay never guesses whether content is usable.
        /// </summary>
        /// <summary>
        /// A caller-supplied Retry behaviour for the connection barrier (WO-1187). When set, the
        /// Retry button runs THIS coroutine instead of re-resolving the first-run content source.
        /// It must yield until its work is finished and return true via <paramref name="_"/>… see
        /// <see cref="SetRetryOverride"/>. Null = the original OfflineContentService behaviour.
        /// </summary>
        private static System.Func<System.Action<bool>, IEnumerator> s_retryOverride;

        /// <summary>
        /// Install a Retry behaviour for the connection barrier. WHY THIS EXISTS: the barrier's
        /// built-in Retry re-resolves the offline CONTENT SOURCE, which is the right recovery for a
        /// first-run cache miss but the WRONG one for "your hero's art failed to download" — that
        /// retry would report success and dismiss while the hero art was still missing, i.e. a button
        /// that lies. A caller that raises the barrier for its own reason supplies its own recovery.
        /// The callback reports whether recovery SUCCEEDED; only then is the barrier dismissed.
        /// Pass null to restore the default behaviour.
        /// </summary>
        public static void SetRetryOverride(System.Func<System.Action<bool>, IEnumerator> retry)
        {
            s_retryOverride = retry;
        }

        public static void ShowConnectionRequired(string message, string retryLabel)
        {
            if (s_active != null)
            {
                s_active.EnterConnectionRequired(message, retryLabel);
                return;
            }

            var go = new GameObject("FirstRunConnectionRequired");
            var ui = go.AddComponent<LoadingOverlay>();
            ui._connectionRequired = true;
            ui.Build(message, retryLabel);
            Object.DontDestroyOnLoad(go);
            s_active = ui;
            FlowTrace.Warn("LoadingOverlay", "first-run content unavailable - persistent connection screen shown");
        }

        private void EnterConnectionRequired(string message, string retryLabel)
        {
            _connectionRequired = true;
            _dismissing = false;
            ApplyConnectionBarrierState(_group);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_messageLabel != null) _messageLabel.text = message;
            if (_retryButton == null)
            {
                _retryButton = ElarionUiKit.ButtonPack(_cardHost != null ? _cardHost : transform,
                    string.IsNullOrWhiteSpace(retryLabel)
                        ? new LocalizedText("offlineFirstRunRetry").Resolve() : retryLabel,
                    ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.22f, 0.20f), new Vector2(0.78f, 0.40f), OnRetryConnection,
                    RpgUiCatalog.ButtonFrame);
                MedievalUiSkin.ApplyButton(_retryButton, primary: true);
            }
        }

        // Kept as one operation because this method can convert an overlay that is already
        // fading out. Restoring only blocksRaycasts leaves alpha at zero and the CanvasGroup
        // non-interactable, producing a visible Retry button that can never receive a click.
        private static void ApplyConnectionBarrierState(CanvasGroup group)
        {
            if (group == null) return;
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = true;
        }

        /// <summary>Dismiss the loading cover early (fades out, then destroys). No-op if none is up.</summary>
        public static void Hide()
        {
            if (s_active != null) s_active.BeginDismiss();
        }

        private void Build(string message, string retryLabel = null)
        {
            // Own ScreenSpaceOverlay canvas, sorted just BELOW the modal band (31000) so it
            // covers all normal game UI during the load but is deliberately NOT a top-band
            // "modal": a transient DontDestroyOnLoad loading cover is not a user-dismissable
            // panel and must NOT be owned by the scene-scoped back-button / battle-lock arbiter
            // (which would tear down across the very scene load this cover exists to span, and
            // whose [modal-registration] contract only applies to real >=31000 modals). During
            // the founding->hub transition no modal is open (the founding panel is destroyed
            // first), so 30990 fully covers the screen.
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30990;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;
            _group.interactable = _connectionRequired;
            _group.blocksRaycasts = true;   // cover the load (no stray taps to whatever is behind)

            // Dark, near-opaque backdrop (raycast-blocking full cover).
            ElarionUiKit.AddImage(transform, "LoadingBg", Vector2.zero, Vector2.one,
                new Color(0.02f, 0.02f, 0.03f, 0.96f), rounded: false);

            // One restrained medieval focus card keeps loading/error copy in the same visual
            // language as every modal without pretending this transient cover is dismissible.
            var card = ElarionUiKit.AddImage(transform, "LoadingCard",
                new Vector2(0.25f, 0.34f), new Vector2(0.75f, 0.66f), Color.white, rounded: false);
            var cardImage = card != null ? card.GetComponent<Image>() : null;
            if (cardImage != null)
            {
                var sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
                if (sprite != null) cardImage.sprite = sprite;
                cardImage.type = Image.Type.Simple;
                cardImage.color = Color.white;
            }
            _cardHost = card != null ? card.transform : transform;

            var brand = ElarionUiKit.Label(_cardHost, new LocalizedText("common.echoes_elarion").Resolve(),
                0.72f, 0.88f, ElarionUi.Gilt, 40,
                TMPro.TextAlignmentOptions.Center, 0.08f, 0.92f, bold: true);
            ElarionUiKit.FitSingleLine(brand, 28f, 40f);

            // Centred message (parchment on the dark cover, no meaning in colour).
            var label = ElarionUiKit.Label(_cardHost, message,
                0.46f, 0.70f, ElarionUi.Parchment, ElarionUi.FontBody,
                TMPro.TextAlignmentOptions.Center, 0.08f, 0.92f);
            _messageLabel = label;
            if (label != null)
            {
                label.raycastTarget = false;
                ElarionUiKit.FitBlock(label, ElarionUi.FontFloorMobile, ElarionUi.FontBody);
            }

            if (_connectionRequired)
            {
                _retryButton = ElarionUiKit.ButtonPack(_cardHost,
                    string.IsNullOrWhiteSpace(retryLabel)
                        ? new LocalizedText("offlineFirstRunRetry").Resolve() : retryLabel,
                    ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.22f, 0.20f), new Vector2(0.78f, 0.40f), OnRetryConnection,
                    RpgUiCatalog.ButtonFrame);
                MedievalUiSkin.ApplyButton(_retryButton, primary: true);
                _shownAt = Time.unscaledTime;
                return;
            }

            // Standard loading BAR (owner 2026-07-26 "can we do a standard loading bar"): a dark rounded
            // track with a gold fill that sweeps 0->100% and repeats — indeterminate, since the async scene
            // load has no reliable progress value to bind. Centred just below the message.
            var track = ElarionUiKit.AddImage(_cardHost, "LoadingBarTrack",
                new Vector2(0.16f, 0.24f), new Vector2(0.84f, 0.32f),
                new Color(1f, 1f, 1f, 0.14f), rounded: true);
            var trackImg = track != null ? track.GetComponent<Image>() : null;
            if (trackImg != null) trackImg.raycastTarget = false;

            // The fill is left-anchored with zero initial width; Update drives anchorMax.x = sweep.
            var fill = ElarionUiKit.AddImage(track != null ? track.transform : transform, "LoadingBarFill",
                Vector2.zero, new Vector2(0f, 1f), ElarionUi.Gold, rounded: true);
            _barFill = fill != null ? fill.transform as RectTransform : null;
            if (_barFill != null) { _barFill.offsetMin = Vector2.zero; _barFill.offsetMax = Vector2.zero; }
            var fillImg = fill != null ? fill.GetComponent<Image>() : null;
            if (fillImg != null) fillImg.raycastTarget = false;

            _shownAt = Time.unscaledTime;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_sceneLoaded) return;                 // only the FIRST load after Show
            _sceneLoaded = true;
            _sceneLoadedAt = Time.unscaledTime;
            FlowTrace.Step("LoadingOverlay", $"first scene loaded '{scene.name}' — settling {SettleSeconds:F2}s then dismissing.");
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (_connectionRequired) return; // player-owned Retry; never timeout into a broken town

            // Animate the loading bar fill (unscaled so a paused game still animates it): sweep the
            // fill 0->100% then reset — an indeterminate "working" bar.
            if (_barFill != null)
            {
                _barT += Time.unscaledDeltaTime / Mathf.Max(0.01f, BarSweepSeconds);
                if (_barT >= 1f) _barT -= 1f;
                _barFill.anchorMax = new Vector2(Mathf.Clamp01(_barT), 1f);
            }

            if (_dismissing)
            {
                float fadeElapsed = Time.unscaledTime - _dismissStartedAt;
                if (_group != null)
                    _group.alpha = Mathf.Clamp01(1f - fadeElapsed / FadeSeconds);
                if (fadeElapsed >= FadeSeconds) Destroy(gameObject);
                return;
            }

            float elapsed = Time.unscaledTime - _shownAt;

            // WO-1187: do NOT let the failsafe fire while the chosen hero's art is still downloading.
            // The scene load is deliberately parked behind that download (SceneRouter), and the fade is
            // already OUT — so dismissing here would uncover a black screen and leave the player staring
            // at nothing until the fetch finished. A 43 MB first fetch on a slow connection genuinely
            // exceeds this 30s budget, so the failsafe would fire on a perfectly healthy download.
            // The prewarm has its own bounded retry + worded failure, so this cannot hang forever.
            if (HeroContentPrewarmer.State == HeroPrewarmState.Downloading)
            {
                if (_messageLabel != null && !string.IsNullOrEmpty(HeroContentPrewarmer.StatusText))
                    _messageLabel.text = HeroContentPrewarmer.StatusText;
                return;
            }

            // Hard failsafe — never stick, even if no scene ever loads.
            if (elapsed >= MaxShowSeconds)
            {
                FlowTrace.Warn("LoadingOverlay", $"max-show {MaxShowSeconds:F0}s hit (no settle) — force-dismissing.");
                BeginDismiss();
                return;
            }

            // Dismiss once the first scene has loaded AND settled AND the min-show floor is met.
            bool settled = _sceneLoaded && (Time.unscaledTime - _sceneLoadedAt) >= SettleSeconds;
            if (settled && elapsed >= MinShowSeconds)
                BeginDismiss();
        }

        private void OnRetryConnection()
        {
            if (_retrying) return;
            _retrying = true;
            if (_retryButton != null) _retryButton.interactable = false;
            FlowTrace.Step("LoadingOverlay", "Retry -> resolving first-run content source again");
            StartCoroutine(RetryConnection());
        }

        private IEnumerator RetryConnection()
        {
            // WO-1187: a caller that raised this barrier for its OWN reason (e.g. the hero art
            // failed to download) supplies its own recovery. Re-resolving the offline content
            // source here would report success and dismiss with the hero art still missing.
            if (s_retryOverride != null)
            {
                bool recovered = false;
                yield return s_retryOverride(ok => recovered = ok);

                _retrying = false;
                if (recovered)
                {
                    FlowTrace.Step("LoadingOverlay", "Retry override recovered - dismissing barrier.");
                    _connectionRequired = false;
                    s_retryOverride = null;
                    BeginDismiss();
                    yield break;
                }

                if (_retryButton != null) _retryButton.interactable = true;
                FlowTrace.Warn("LoadingOverlay", "Retry override still failing - barrier remains.");
                yield break;
            }

            ContentSource resolved = ContentSource.Unknown;
            yield return OfflineContentService.ResolveContentSource(source => resolved = source);

            _retrying = false;
            if (resolved == ContentSource.Online || resolved == ContentSource.LocalCache)
            {
                FlowTrace.Step("LoadingOverlay", $"Retry recovered content source={resolved} -> dismissing");
                _connectionRequired = false;
                BeginDismiss();
                yield break;
            }

            if (_retryButton != null) _retryButton.interactable = true;
            FlowTrace.Warn("LoadingOverlay", $"Retry still unavailable (source={resolved}) - screen remains");
        }

        private void BeginDismiss()
        {
            if (_dismissing) return;
            _dismissing = true;
            _dismissStartedAt = Time.unscaledTime;
            if (_group != null) _group.blocksRaycasts = false;   // let the hub take input during the fade
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;   // idempotent even if already removed
            if (s_active == this) s_active = null;
        }
    }
}
