// =============================================================================
// SplashLoading — the DeNelle Studios studio bumper (Week 1)
// -----------------------------------------------------------------------------
// Port of src/modules/onboarding/SplashLoading.tsx + StudioBumper.tsx. Plays the
// one-time "developed by DeNelle Studios" bumper before the Title screen takes
// over: a UnityEngine.Video.VideoPlayer streams studio-bumper.mp4 (~3 seconds),
// then the overlay fades out and control passes to the Title screen.
//
// The React StudioBumper had three render states (video / static fallback /
// not-rendered-for-returning-players). Week 1 keeps the two that matter for a
// desktop build: VIDEO playback, and a STATIC FALLBACK card ("DeNelle Studios /
// presents") shown when the clip is missing, errors, or stalls. The studio name
// on the fallback card is read from canon-strings.json (key "publisher") — never
// hardcoded (v2 port-spec Part 4).
//
// async UniTask throughout — never async void (port-spec Part 3 mandate).
// =============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Plays the studio bumper video (~3s) then fades to the Title screen.
    /// Falls back to a static "DeNelle Studios presents" card if the clip is
    /// missing or errors.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SplashLoading : MonoBehaviour
    {
        [Header("Video")]
        [Tooltip("The DeNelle Studios bumper — studio-bumper.mp4 imported as a VideoClip.")]
        [SerializeField] private VideoPlayer _videoPlayer;

        [Tooltip("The RenderTexture the VideoPlayer renders into; shown on the overlay.")]
        [SerializeField] private RenderTexture _videoTexture;

        [Tooltip("Play the studio-bumper.mp4 video. OFF: the studio-bumper clip — " +
                 "tried raw AND with VideoClip transcoding — reliably crashes the " +
                 "Windows player's video decoder at the splash; the build only runs " +
                 "with the video off, showing the static 'DeNelle Studios presents' " +
                 "card. Needs a verified clean offline re-encode (H.264 baseline / " +
                 "yuv420p / explicit bt709 color) before this can be turned back ON.")]
        [SerializeField] private bool _playBumperVideo = false;

        [Header("Timing")]
        [Tooltip("Hard cap on bumper duration. Owner directive 2026-05-20: 3 s total. " +
                 "2.5 s of bumper + 0.5 s fade lands the player on Title by the 3-s mark.")]
        [SerializeField] private float _maxBumperSeconds = 2.5f;

        [Tooltip("If the video has not finished preparing within this many seconds, use the static fallback card.")]
        [SerializeField] private float _videoStartTimeoutSeconds = 1.5f;

        [Tooltip("How long the static fallback card holds before finishing.")]
        [SerializeField] private float _staticHoldSeconds = 1.0f;

        [Tooltip("Seconds the overlay's fade-out takes before handing off to the Title screen.")]
        [SerializeField] private float _fadeSeconds = 0.5f;

        /// <summary>
        /// Raised once the bumper completes — video ended, fallback timed out,
        /// or the duration cap fired. The host shows the Title screen after.
        /// </summary>
        public event Action Finished;

        private UIDocument _document;
        private VisualElement _root;
        private CancellationTokenSource _cts;

        /// <summary>True once <see cref="Play"/> has run to completion.</summary>
        public bool HasFinished { get; private set; }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>
        /// Plays the bumper. Streams the video into the overlay, waits for it
        /// to end (or the duration cap), fades out, then raises
        /// <see cref="Finished"/>. Never <c>async void</c>.
        /// </summary>
        public async UniTask Play()
        {
            if (HasFinished) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            BuildOverlay(out var videoElement, out var fallbackCard);

            try
            {
                var playedVideo = await TryPlayVideo(videoElement, fallbackCard, token);
                if (!playedVideo)
                {
                    // Static fallback path — hold the card, then move on.
                    await UniTask.Delay(TimeSpan.FromSeconds(_staticHoldSeconds),
                        DelayType.DeltaTime, PlayerLoopTiming.Update, token);
                }
                await Fade(1f, 0f, _fadeSeconds, token);
            }
            catch (OperationCanceledException)
            {
                // Disabled mid-play — fall through so the host is not stranded.
            }

            HideImmediate();
            Complete();
        }

        // =====================================================================
        //  Video playback
        // =====================================================================

        private async UniTask<bool> TryPlayVideo(
            VisualElement videoElement, VisualElement fallbackCard, CancellationToken token)
        {
            // Video disabled (see _playBumperVideo) or no clip — go straight to
            // the static "DeNelle Studios presents" card. This guard is what
            // keeps a malformed clip from deadlocking the native video decoder.
            if (!_playBumperVideo || _videoPlayer == null || _videoPlayer.clip == null)
            {
                ShowFallback(videoElement, fallbackCard);
                return false;
            }

            var errored = false;
            void OnError(VideoPlayer _, string message)
            {
                Debug.LogWarning($"[SplashLoading] Studio bumper video error: {message}");
                errored = true;
            }
            _videoPlayer.errorReceived += OnError;

            try
            {
                _videoPlayer.isLooping = false;
                // Don't drop frames the decoder is slow on — this was making
                // the bumper render its first frame, sit, then snap to the
                // last few frames as the decoder skipped ahead.
                _videoPlayer.skipOnDrop = false;
                _videoPlayer.playOnAwake = false;
                _videoPlayer.frame = 0;
                // Owner 2026-05-20: play the full bumper clip but at 3× speed
                // so the IP reveal lands in ~1 s instead of ~3 s. Full content,
                // shorter dwell.
                _videoPlayer.playbackSpeed = 3.0f;
                _videoPlayer.Prepare();

                // Wait for the clip to be ready, bounded by the start timeout.
                var waited = 0f;
                while (!_videoPlayer.isPrepared && !errored && waited < _videoStartTimeoutSeconds)
                {
                    token.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    waited += Time.deltaTime;
                }

                if (errored || !_videoPlayer.isPrepared)
                {
                    ShowFallback(videoElement, fallbackCard);
                    return false;
                }

                _videoPlayer.Play();

                // Wait for actual frames to start incrementing before trusting
                // isPlaying — WMF flips isPlaying=true while still buffering,
                // which would let us exit the loop before any frames render.
                long startFrame = _videoPlayer.frame;
                float frameWait = 0f;
                while (_videoPlayer.frame <= startFrame && !errored && frameWait < 1.5f)
                {
                    token.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    frameWait += Time.deltaTime;
                }

                // Play until the clip ends, an error fires, or the safety cap is
                // hit. The cap follows the clip's own real-time duration (clip
                // length ÷ playbackSpeed, +0.5 s margin) so a longer bumper
                // plays in full; _maxBumperSeconds is the floor.
                float speed = Mathf.Max(0.01f, _videoPlayer.playbackSpeed);
                float realDuration = _videoPlayer.length > 0d
                    ? (float)_videoPlayer.length / speed
                    : 0f;
                float playCap = realDuration > 0f
                    ? Mathf.Max(_maxBumperSeconds, realDuration + 0.5f)
                    : _maxBumperSeconds;
                var elapsed = 0f;
                long lastFrame = _videoPlayer.frame;
                int stalledFrames = 0;
                while (_videoPlayer.isPlaying && !errored && elapsed < playCap)
                {
                    token.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.deltaTime;

                    // Watchdog: if the frame counter freezes for >0.5 s the
                    // decoder has stalled — break and let the fallback card
                    // show rather than holding the player on a still frame.
                    if (_videoPlayer.frame == lastFrame) stalledFrames++;
                    else { stalledFrames = 0; lastFrame = _videoPlayer.frame; }
                    if (stalledFrames > 30) // ~0.5 s at 60 fps
                    {
                        Debug.LogWarning("[SplashLoading] Bumper video stalled — bailing to fallback.");
                        break;
                    }
                }

                if (errored)
                {
                    ShowFallback(videoElement, fallbackCard);
                    return false;
                }

                _videoPlayer.Stop();
                return true;
            }
            finally
            {
                _videoPlayer.errorReceived -= OnError;
            }
        }

        private static void ShowFallback(VisualElement videoElement, VisualElement fallbackCard)
        {
            if (videoElement != null) videoElement.style.display = DisplayStyle.None;
            if (fallbackCard != null) fallbackCard.style.display = DisplayStyle.Flex;
        }

        // =====================================================================
        //  Overlay construction (runtime UI Toolkit)
        // =====================================================================

        private void BuildOverlay(out VisualElement videoElement, out VisualElement fallbackCard)
        {
            _root = _document.rootVisualElement;
            _root.Clear();
            _root.style.flexGrow = 1;
            _root.style.backgroundColor = Color.black;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.style.opacity = 1f;

            // The video surface — shows the VideoPlayer's RenderTexture.
            videoElement = new VisualElement { name = "bumper-video" };
            videoElement.style.flexGrow = 1;
            videoElement.style.width = Length.Percent(100);
            videoElement.style.height = Length.Percent(100);
            if (_videoTexture != null)
            {
                videoElement.style.backgroundImage = Background.FromRenderTexture(_videoTexture);
                videoElement.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }
            _root.Add(videoElement);

            // The static fallback card — hidden unless the video fails.
            fallbackCard = new VisualElement { name = "bumper-fallback" };
            fallbackCard.style.position = Position.Absolute;
            fallbackCard.style.alignItems = Align.Center;
            fallbackCard.style.justifyContent = Justify.Center;
            fallbackCard.style.display = DisplayStyle.None;

            var studioLabel = new Label(CanonStrings.Publisher);
            studioLabel.style.color = new Color(1f, 0.81f, 0.4f, 0.9f);
            studioLabel.style.fontSize = 40;
            studioLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            studioLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            fallbackCard.Add(studioLabel);

            // WO-1857: the studio-bumper fallback card reads "<Publisher> presents"; the
            // publisher is a proper noun (CanonStrings.Publisher) and the verb is copy.
            var presentsLabel = new Label(
                new LocalizedText("onboarding.splash.presents").Resolve());
            presentsLabel.style.color = new Color(1f, 1f, 1f, 0.4f);
            presentsLabel.style.fontSize = 12;
            presentsLabel.style.letterSpacing = 5;
            presentsLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            presentsLabel.style.marginTop = 8;
            fallbackCard.Add(presentsLabel);

            _root.Add(fallbackCard);
        }

        private async UniTask Fade(float from, float to, float seconds, CancellationToken token)
        {
            if (_root == null) return;
            if (seconds <= 0f)
            {
                _root.style.opacity = to;
                return;
            }
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                token.ThrowIfCancellationRequested();
                _root.style.opacity = Mathf.Lerp(from, to, elapsed / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
            }
            _root.style.opacity = to;
        }

        private void HideImmediate()
        {
            // Fully tear down the overlay so it can never intercept picks on the
            // Title screen after the bumper finishes. Belt-and-braces: hide,
            // remove from panel, disable the document, deactivate the
            // GameObject. _document.enabled=false alone has been observed to
            // leave the root attached on the shared panel in this Unity 6 build.
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
                _root.pickingMode = PickingMode.Ignore;
                _root.Clear();
                _root.RemoveFromHierarchy();
            }
            if (_document != null)
            {
                _document.visualTreeAsset = null;
                _document.enabled = false;
                _document.gameObject.SetActive(false);
            }
        }

        private void Complete()
        {
            if (HasFinished) return;
            HasFinished = true;
            Finished?.Invoke();
        }
    }
}
