// =============================================================================
// StoryIntroController — the cold-open cinematic (Week 1 -> WO-C uGUI conversion)
// -----------------------------------------------------------------------------
// Port of src/modules/onboarding/StoryIntro.tsx. Plays the opening cinematic:
// the Stone Choir beats from STORYLINE.md, one at a time, over a dark backdrop,
// then hands control back to the Title screen.
//
// WO-C part 1 (2026-07-03, coverage matrix row #16): UIDocument/UITK overlay
// -> code-built uGUI on the Blink Obsidian kit. The overlay is now its own
// ScreenSpaceOverlay canvas (ElarionUiKit.BuildModalCanvas) with CanvasGroup
// fades, a kit-typography TMP line label, and an Obsidian Skip button (the
// GameObject is named "CloseButton" — the one shared Close convention). This
// removes the controller's UIDocument dependency entirely — no more shared
// OnboardingPanelSettings panel, so the whole DEF-211 "Skip wipes every screen"
// shared-panel teardown dance is gone: the overlay owns its canvas and can be
// deactivated outright without blanking anything else.
//
// FIRST-LAUNCH GATE (preserved). The React version stored an `avalon-intro-seen`
// localStorage flag; the v2 port maps it onto GameState.Onboarded — a brand-new
// save has Onboarded=false and gets the cold open; a returning save skips it
// (Play() completes immediately, no flash).
//
// SKIPPABILITY (preserved): tap-anywhere advances one beat at a time — but the
// first ~1.25s is a grace window so the tap that LAUNCHED the player into the
// scene cannot instantly skip the whole cinematic. The explicit Skip button is
// never gated and cancels the whole cinematic (DEF-134: sets _skipRequested AND
// cancels the CTS so the beat loop breaks immediately).
//
// async UniTask throughout — never async void (port-spec Part 3 mandate).
// =============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Plays the first-launch cold-open cinematic — the Stone Choir beats, then
    /// raises <see cref="Finished"/>. Code-built uGUI — no UIDocument.
    /// </summary>
    public sealed class StoryIntroController : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds the fade-in / fade-out of the whole overlay takes.")]
        [SerializeField] private float _fadeSeconds = 0.5f;

        [Header("Debug")]
        [Tooltip("Force the cinematic to play even when the save says onboarded. Editor testing only.")]
        [SerializeField] private bool _forcePlay;

        [Tooltip("Seconds after the intro starts during which a tap-anywhere is ignored, " +
                 "so the launch tap that carried the player into the scene cannot " +
                 "instantly skip the whole cinematic. The explicit Skip button is never gated.")]
        [SerializeField] private float _pointerSkipGraceSeconds = 1.25f;

        /// <summary>
        /// Raised when the cinematic finishes — either after the last beat's hold
        /// expires, when the player skips past it, or immediately when the
        /// first-launch gate says it should not play at all. The host (the Title
        /// scene) shows the title screen after this fires.
        /// </summary>
        public event Action Finished;

        // ── Code-built uGUI overlay ──────────────────────────────────────────
        private GameObject _canvas;          // own overlay canvas (no shared panel)
        private CanvasGroup _rootGroup;      // whole-overlay fade
        private TextMeshProUGUI _lineLabel;  // the cinematic line
        private CanvasGroup _lineGroup;      // per-line fade
        private Image _imagePanel;           // per-beat perimeter image
        private RectTransform _imageRect;
        private CanvasGroup _imageGroup;     // per-image fade
        private readonly System.Collections.Generic.Dictionary<int, Sprite> _beatSprites =
            new System.Collections.Generic.Dictionary<int, Sprite>();

        private bool _skipRequested;
        private float _overlayStartTime;
        private CancellationTokenSource _cts;

        /// <summary>True once <see cref="Play"/> has run to completion.</summary>
        public bool HasFinished { get; private set; }

        private void Awake()
        {
            // WO-C: the Title scene (built before the conversion) still carries this
            // GameObject's legacy UIDocument (the old RequireComponent). It renders
            // nothing for us any more — disable it so it cannot keep a PanelRaycaster
            // on the shared OnboardingPanelSettings panel (the duplicate-doc bug).
            var doc = GetComponent<UnityEngine.UIElements.UIDocument>();
            if (doc != null && doc.enabled)
            {
                doc.enabled = false;
                FlowTrace.Step("Onboarding", "WO-C: disabled StoryIntro's legacy UIDocument — the cold-open renders via uGUI now.");
            }
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas);
        }

        /// <summary>
        /// True on a brand-new save — the cold open should auto-play. Mirrors
        /// the React <c>avalon-intro-seen</c> check, mapped onto the persisted
        /// <see cref="GameState.Onboarded"/> flag (see the file header).
        /// </summary>
        public bool ShouldAutoPlay
        {
            get
            {
                if (_forcePlay) return true;
                var svc = GameStateService.Instance;
                // No service yet (Core not bootstrapped) — treat as first launch.
                if (svc == null || svc.State == null) return true;
                return !svc.State.Onboarded;
            }
        }

        /// <summary>
        /// Runs the cold open. On first launch it fades in, plays the beats, fades
        /// out, then raises <see cref="Finished"/>. On a returning save it raises
        /// <see cref="Finished"/> immediately (no flash). Never <c>async void</c>.
        /// </summary>
        public async UniTask Play()
        {
            using var _ = FlowTrace.Enter("Onboarding", "StoryIntroController.Play (cold-open cinematic)");
            if (HasFinished) { FlowTrace.Step("Onboarding", "StoryIntro.Play: already finished — no-op."); return; }

            if (!ShouldAutoPlay)
            {
                FlowTrace.Step("Onboarding", "StoryIntro.Play: ShouldAutoPlay=false (returning player) — completing immediately, no cinematic.");
                HideImmediate();
                Complete();
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            BuildOverlay();
            _skipRequested = false;

            // Prime the intro score the instant the cinematic begins. Browsers
            // legally block audio until the first user gesture, so this may not
            // sound immediately on WebGL — WebGLAudioUnlock resumes the
            // AudioContext on first touch and AudioService re-issues the current
            // track. DEF-228: prime the TITLE theme (title.mp3) — the cold-open is
            // part of the Title scene and title.mp3 is the opening music.
            CoreServices.Audio?.PlayMusic(DeNelle.Core.Audio.MusicTrack.Title);

            try
            {
                await Fade(0f, 1f, _fadeSeconds, token);

                // Ported verbatim from React src/content/story.ts OPENING_CINEMATIC
                // (2026-05-20). Beats with per-beat image (Resources/Intro/intro-N)
                // and an emphasis flag that bumps weight + size for landings.
                var cinematic = ReactOpeningCinematic;
                foreach (var beat in cinematic)
                {
                    // DEF-211: hard early-exit at the TOP of every beat. If the CTS
                    // was cancelled (e.g. SafeStage timed out and ForceHide fired),
                    // stop rendering THIS instant — never paint another beat over the
                    // title. Awaits inside the beat also observe the token, but this
                    // guard guarantees a cancelled loop cannot start a fresh beat.
                    if (_cts == null || _cts.IsCancellationRequested) return;

                    if (_lineLabel != null)
                    {
                        _lineLabel.text = beat.Text;
                        if (_lineGroup != null) _lineGroup.alpha = 0f;
                        _lineLabel.fontStyle = beat.Emphasis
                            ? (FontStyles.Bold | FontStyles.Italic)
                            : FontStyles.Italic;
                        // Owner direction 2026-05-20: text was too small on intro —
                        // ~2x across all beats so cinematic copy reads at
                        // conversational distance.
                        _lineLabel.fontSize = beat.Emphasis ? 56 : 44;
                    }
                    if (_imagePanel != null && beat.ImageId > 0)
                    {
                        var sprite = BeatSprite(beat.ImageId);
                        if (sprite != null)
                            _imagePanel.sprite = sprite;
                        else
                            // R/U: a missing beat image plays text-only (never blank),
                            // but self-report it so a missing asset surfaces.
                            FlowTrace.Warn("Onboarding",
                                $"StoryIntro.Play: Resources/Intro/intro-{beat.ImageId} not found — beat plays text-only (no image).");
                        _imagePanel.enabled = sprite != null;
                        // Owner direction 2026-05-20: place images on the perimeter so
                        // they don't crowd the text. Deterministic per ImageId — same
                        // beat always lands in the same corner on a replay.
                        SetImagePosition(ImagePositionFor(beat.ImageId));
                    }
                    var inFade = FadeLabel(0f, 1f, 0.45f, token);
                    var imageIn = beat.ImageId > 0 ? FadeImage(0f, 1f, 0.55f, token) : UniTask.CompletedTask;
                    await UniTask.WhenAll(inFade, imageIn);
                    await WaitBeatOrSkip(beat.HoldSeconds, token);
                    var outFade = FadeLabel(1f, 0f, 0.35f, token);
                    var imageOut = beat.ImageId > 0 ? FadeImage(1f, 0f, 0.4f, token) : UniTask.CompletedTask;
                    await UniTask.WhenAll(outFade, imageOut);
                    if (_skipRequested) break;
                }

                await Fade(1f, 0f, _fadeSeconds, token);
            }
            catch (OperationCanceledException)
            {
                // Disabled mid-play — fall through to Complete so the host
                // is never left waiting on a dead cinematic.
            }

            HideImmediate();
            Complete();
        }

        /// <summary>
        /// Hard-stops the cinematic and removes its overlay NOW. The host times the
        /// cold-open out (a WebGL hang-guard) and calls this as the authoritative
        /// KILL: cancel the play loop, hide the overlay, mark finished so a later
        /// Play() is a no-op. The overlay owns its OWN canvas now (no shared
        /// PanelSettings), so deactivating it outright is safe — it cannot blank
        /// the title (the old DEF-211 shared-panel regression is structurally gone).
        /// </summary>
        public void ForceHide()
        {
            _cts?.Cancel();
            HideImmediate();
            HasFinished = true;
        }

        // ── Overlay construction (runtime uGUI, Obsidian kit) ───────────────
        private void BuildOverlay()
        {
            if (_canvas == null)
            {
                // Own overlay canvas — above the title menu (100), below system modals.
                _canvas = ElarionUiKit.BuildModalCanvas("StoryIntroUI", 900);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
                _rootGroup = _canvas.AddComponent<CanvasGroup>();

                // Semi-transparent backdrop — owner feedback 2026-05-20: alpha 0.55
                // keeps the scene (starfield / title art) readable behind the text so
                // the atmosphere persists across line changes. Tap-anywhere advances
                // one beat — gated by the grace window (the explicit Skip is not).
                var backdropGo = new GameObject("Backdrop", typeof(Image), typeof(Button));
                backdropGo.transform.SetParent(_canvas.transform, false);
                StretchFull(backdropGo);
                var backdropImg = backdropGo.GetComponent<Image>();
                backdropImg.color = new Color(0.027f, 0.016f, 0.063f, 0.55f);
                var tap = backdropGo.GetComponent<Button>();
                tap.transition = Selectable.Transition.None;
                tap.onClick.AddListener(() =>
                {
                    if (Time.unscaledTime - _overlayStartTime < _pointerSkipGraceSeconds) return;
                    _skipRequested = true;
                });

                // Per-beat scene image — perimeter-placed (owner direction 2026-05-20:
                // use the full screen, keep distance from the text band).
                var imgGo = new GameObject("BeatImage", typeof(Image), typeof(CanvasGroup));
                imgGo.transform.SetParent(_canvas.transform, false);
                _imageRect = (RectTransform)imgGo.transform;
                _imageRect.pivot = new Vector2(0f, 1f);
                _imageRect.sizeDelta = new Vector2(260f, 200f);
                _imagePanel = imgGo.GetComponent<Image>();
                _imagePanel.preserveAspect = true;   // the UITK ScaleToFit equivalent
                _imagePanel.raycastTarget = false;
                _imagePanel.enabled = false;
                _imageGroup = imgGo.GetComponent<CanvasGroup>();
                _imageGroup.alpha = 0f;
                _imageGroup.blocksRaycasts = false;

                // The cinematic line — kit title typography, centred band, wraps.
                _lineLabel = ElarionUiKit.Label(_canvas.transform, string.Empty,
                    0.36f, 0.64f, new Color(0.957f, 0.941f, 1f, 0.92f), 44,
                    TextAlignmentOptions.Center, 0.10f, 0.90f);
                ElarionUiKit.EnsureFont(_lineLabel, ElarionUiKit.FontRole.Title);
                _lineLabel.fontStyle = FontStyles.Italic;
                _lineGroup = _lineLabel.gameObject.AddComponent<CanvasGroup>();
                _lineGroup.alpha = 0f;
                _lineGroup.blocksRaycasts = false;

                // Skip — the Obsidian family button, top-right. Named "CloseButton"
                // (the one shared Close convention). Jumps straight past the whole
                // cinematic (DEF-134: set the flag AND cancel the CTS so the beat
                // loop breaks immediately and teardown is reached at once).
                // WO-1857: reuses the game-wide common.skip (COMMON_KEYS_REGISTRY names
                // this exact call site as one of its six confirmed same-job sites).
                var skip = ElarionUiKit.BuildObsidianButton(_canvas.transform,
                    new LocalizedText("common.skip").Resolve(),
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.80f, 0.925f), new Vector2(0.975f, 0.985f),
                    () => { _skipRequested = true; _cts?.Cancel(); });
                skip.gameObject.name = "CloseButton";
            }

            _canvas.SetActive(true);
            if (_rootGroup != null) _rootGroup.alpha = 0f;
            if (_lineGroup != null) _lineGroup.alpha = 0f;
            if (_imageGroup != null) _imageGroup.alpha = 0f;

            // Stamp the start so the grace window is measured from "intro began",
            // not from scene load.
            _overlayStartTime = Time.unscaledTime;
        }

        /// <summary>Loads (and caches) the Resources/Intro/intro-N beat art as a sprite.</summary>
        private Sprite BeatSprite(int imageId)
        {
            if (_beatSprites.TryGetValue(imageId, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>($"Intro/intro-{imageId}");
            Sprite sprite = null;
            if (tex != null)
                sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                       new Vector2(0.5f, 0.5f), 100f);
            _beatSprites[imageId] = sprite;   // cache misses too (warn once per beat show)
            return sprite;
        }

        /// <summary>Places the beat image at a perimeter position given as UITK-style
        /// (left%, top%) — converted to a uGUI top-left-pivot anchor.</summary>
        private void SetImagePosition(Vector2 leftTopPercent)
        {
            if (_imageRect == null) return;
            var anchor = new Vector2(leftTopPercent.x / 100f, 1f - (leftTopPercent.y / 100f));
            _imageRect.anchorMin = anchor;
            _imageRect.anchorMax = anchor;
            _imageRect.anchoredPosition = Vector2.zero;
        }

        private static void StretchFull(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ── Fades (CanvasGroup alphas — the uGUI equivalent of the UITK opacities) ──

        private async UniTask Fade(float from, float to, float seconds, CancellationToken token)
        {
            if (_rootGroup == null) return;
            if (seconds <= 0f)
            {
                _rootGroup.alpha = to;
                return;
            }
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                token.ThrowIfCancellationRequested();
                _rootGroup.alpha = Mathf.Lerp(from, to, elapsed / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
            }
            _rootGroup.alpha = to;
        }

        /// <summary>Fades only the text label, so each line eases in / out instead of cutting hard.</summary>
        private async UniTask FadeLabel(float from, float to, float seconds, CancellationToken token)
        {
            if (_lineGroup == null) return;
            if (seconds <= 0f)
            {
                _lineGroup.alpha = to;
                return;
            }
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                token.ThrowIfCancellationRequested();
                _lineGroup.alpha = Mathf.Lerp(from, to, elapsed / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
            }
            _lineGroup.alpha = to;
        }

        /// <summary>Fades the beat image panel from <paramref name="from"/> to <paramref name="to"/> alpha.</summary>
        private async UniTask FadeImage(float from, float to, float seconds, CancellationToken token)
        {
            if (_imageGroup == null) return;
            if (seconds <= 0f) { _imageGroup.alpha = to; return; }
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                token.ThrowIfCancellationRequested();
                _imageGroup.alpha = Mathf.Lerp(from, to, elapsed / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
            }
            _imageGroup.alpha = to;
        }

        /// <summary>One cinematic beat — text + per-beat hold + optional intro-N image + emphasis.</summary>
        private readonly struct CinematicBeat
        {
            public readonly string Text;
            public readonly float HoldSeconds;
            public readonly int ImageId;     // 1..8 maps to Resources/Intro/intro-N; 0 = none
            public readonly bool Emphasis;
            public CinematicBeat(string text, float hold, int imageId = 0, bool emphasis = false)
            { Text = text; HoldSeconds = hold; ImageId = imageId; Emphasis = emphasis; }
        }

        /// <summary>
        /// Stone Choir opening cinematic — canon-locked to STORYLINE.md (2026-05-27).
        /// 14 beats, total ~73 seconds; players can tap to skip ahead one beat at a time.
        ///
        /// Canon anchors:
        ///   - Town = Elarion. The Lantern motif is retired. No named heart.
        ///   - The Heart-Tree burned a hundred winters ago; the Folk raised the
        ///     Cathedral Spire over its ashes and bound its last song inside.
        ///   - The spire holds one long note (the chord). While it sings, the
        ///     valley holds. The Choir (Hollow Ones) come to silence it.
        ///   - Three heroes wait: Sir Bram (Knight), Nessa (Ranger), and the
        ///     youngest Chorister — who is you (the Keeper).
        ///   - "That one is you" / "The chord is yours now" are the closing beats.
        ///   - Class-agnostic: plays before HeroSelect, so it does not assume Mage.
        /// </summary>
        private static readonly CinematicBeat[] ReactOpeningCinematic = new[]
        {
            new CinematicBeat("A hundred winters ago, the Heart-Tree burned.", 5.0f),
            new CinematicBeat("Elarion watched from inside its walls.", 4.8f, 1),
            new CinematicBeat("The court fled south. No king ever came back.", 5.2f, 8),
            new CinematicBeat("Elarion. A village. A grief. A vow.", 5.4f, 5, true),
            new CinematicBeat("So the Folk raised a spire of pale stone over the Tree's ashes,", 5.0f),
            new CinematicBeat("and bound its last song inside. The spire has held the note ever since.", 5.4f),
            new CinematicBeat("They call the dark the Withering — it remembers no warmth,", 5.0f, 2),
            new CinematicBeat("and it forgives no green and growing thing.", 4.8f),
            new CinematicBeat("Three have kept watch over the spire's chord —", 4.8f),
            // Class-agnostic phrasing per owner direction 2026-05-20 — the intro
            // plays before HeroSelect so it does not lock the player to Mage.
            // Sir Bram (Knight) and Nessa (Ranger) are named companions; the
            // third watcher is the player — any of the three hero classes.
            new CinematicBeat("Sir Bram the knight, Nessa the ranger, and one Chorister still learning the song —", 5.8f),
            new CinematicBeat("waiting for the one the chord will answer to.", 5.0f, 7),
            new CinematicBeat("That one is you.", 4.5f, 3, true),
            new CinematicBeat("Barely a Keeper, scarcely tested — yet the spire steadies when you step beneath it.", 5.8f, 6),
            new CinematicBeat("Welcome home. The chord is yours now.", 5.4f, 4, true),
        };

        /// <summary>
        /// Perimeter placement table for the intro image panel, in UITK-style
        /// (left%, top%) coordinates (converted in <see cref="SetImagePosition"/>).
        /// Owner direction 2026-05-20: "move assets away from text, use entire
        /// screen, just keep distance from text". The text band sits centred at
        /// ~45% height, so the image always lands at a corner or the top/bottom
        /// centre, outside that band. Deterministic by ImageId so replays match.
        /// </summary>
        private static readonly Vector2[] ImagePositions =
        {
            new Vector2(  6f,   5f),   // 0 — top-left
            new Vector2( 68f,   5f),   // 1 — top-right
            new Vector2(  6f,  70f),   // 2 — bottom-left
            new Vector2( 68f,  70f),   // 3 — bottom-right
            new Vector2( 36f,   3f),   // 4 — top-centre
            new Vector2( 36f,  72f),   // 5 — bottom-centre
            new Vector2(  4f,  35f),   // 6 — mid-left
            new Vector2( 72f,  35f),   // 7 — mid-right
        };

        private static Vector2 ImagePositionFor(int imageId)
        {
            int idx = Mathf.Abs(imageId) % ImagePositions.Length;
            return ImagePositions[idx];
        }

        private async UniTask WaitBeatOrSkip(float holdSeconds, CancellationToken token)
        {
            var elapsed = 0f;
            while (elapsed < holdSeconds && !_skipRequested)
            {
                token.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
            }
            _skipRequested = false; // consume — one beat at a time
        }

        private void HideImmediate()
        {
            // Own-canvas teardown: this overlay no longer shares a PanelSettings
            // panel with the title, so deactivating it is safe and complete —
            // it renders nothing and steals no clicks.
            if (_canvas != null) _canvas.SetActive(false);
        }

        private void Complete()
        {
            if (HasFinished) return;
            HasFinished = true;
            Finished?.Invoke();
        }
    }
}
