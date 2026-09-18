// =============================================================================
// IntroSequencePlayer — the ~30s skippable VIDEO opening (WO-569).
// -----------------------------------------------------------------------------
// OWNER DECISION (2026-06-28): the boot intro is now a real ~30s cinematic VIDEO
// (Assets/StreamingAssets/Video/Defenders.mp4 — ends on the gold title
// "ECHOES OF ELARION"), replacing the WO-561 image-slate sequence. The video
// plays FULL-SCREEN at boot and is SKIPPABLE; on natural end it hands off to
// hero select, exactly as the slate sequence did.
//
// HOW IT PLAYS (mirrors the proven SplashLoading.cs VideoPlayer pattern):
//   - UnityEngine.Video.VideoPlayer, source = URL (NOT a VideoClip import) —
//     System.IO.Path.Combine(Application.streamingAssetsPath, "Video/Defenders.mp4").
//     A StreamingAssets URL plays in Windows + WebGL builds with no import step
//     (SplashLoading uses a VideoClip; we use a URL so the .mp4 needs no importer).
//   - renderMode = RenderTexture → drawn onto a full-screen RawImage on a top-most
//     ElarionUiKit.BuildModalCanvas. (SplashLoading renders its RenderTexture onto a
//     UI-Toolkit element; we use uGUI RawImage to match this file's existing kit.)
//   - audioOutputMode = AudioSource (the video's own audio track plays directly via
//     a dedicated AudioSource on the driver — see OWNER FLAG: audio bus, below).
//   - Prepare() then wait (bounded by a timeout) before Play(); skipOnDrop=false so
//     a slow decoder doesn't snap frames (the SplashLoading lesson, lines 150-160).
//
// SKIPPABLE: a visible "Skip >" gold button + a full-screen invisible tap target +
// ANY keyboard key all end the intro immediately. On natural end (loopPointReached)
// it also advances. Advance = SceneRouter.GoHeroSelect() — the SAME next step the
// slate sequence used, so TitleController's "Play Intro" call site is unchanged.
//
// ROBUST FALLBACK (never hard-blocks boot): if the video URL is missing, errors
// (errorReceived), or fails to prepare within the timeout, we LogWarning and fall
// back to the original WO-561 five-slate caption-on-black sequence (kept below).
// The intro therefore always reaches hero select.
//
// Decoupled trigger: registers on Core's IntroLauncher.Play at startup so the Title
// screen's "Play Intro" button (DeNelle.Onboarding) fires it without a hard ref.
//
// Code-built, NO UXML, NO Yarn (Yarn removed WO-557).
// =============================================================================

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using DeNelle.Core;
using DeNelle.Core.UI;
using DeNelle.Core.Audio;
using DeNelle.Core.Diagnostics;

namespace DeNelle.DialogueUI
{
    /// <summary>Registers + launches the skippable video intro.</summary>
    public static class IntroSequencePlayer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            DeNelle.Core.IntroLauncher.Play = Play;
        }

        /// <summary>Play the cinematic intro. Spawns a self-contained, DontDestroyOnLoad
        /// driver so it survives the hand-off frame to hero select.</summary>
        public static void Play()
        {
            // If one is somehow already running, don't double-spawn.
            if (UnityEngine.Object.FindAnyObjectByType<IntroSequenceDriver>() != null) return;

            var go = new GameObject("IntroSequence");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<IntroSequenceDriver>();
            Debug.Log("[IntroSequencePlayer] Playing video intro (Defenders.mp4, Yarn-free).");
        }
    }

    /// <summary>One image slate: a background sprite, a caption, and how long it holds.
    /// (FALLBACK PATH only — used when the video can't play.)</summary>
    internal struct IntroSlate
    {
        public string Image;     // Resources path (no extension) to the slate sprite
        public string Caption;   // evocative narration line
        public float Hold;       // seconds to hold before auto-advancing
        public bool TitleCard;   // last slate overlays the game title

        public IntroSlate(string image, string caption, float hold, bool titleCard = false)
        { Image = image; Caption = caption; Hold = hold; TitleCard = titleCard; }
    }

    /// <summary>Plays Defenders.mp4 full-screen + skippable; falls back to the
    /// five-slate caption sequence if the video can't play. Routes to hero select on
    /// finish. Code-built, no UXML.</summary>
    [DisallowMultipleComponent]
    internal sealed class IntroSequenceDriver : MonoBehaviour
    {
        // ── Video config ──────────────────────────────────────────────────────
        private const string VideoRelPath = "Video/Defenders.mp4";   // under StreamingAssets
        private const float PrepareTimeoutSeconds = 6f;              // bail to fallback if not prepared by here

        private GameObject _canvas;
        private VideoPlayer _videoPlayer;
        private AudioSource _audioSource;
        private RenderTexture _rt;
        private RawImage _videoSurface;
        private AspectRatioFitter _videoFitter;   // WO-704: envelope-crop the video surface, never stretch
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool _fullscreenRequested;        // WO-704: WebGL browser-fullscreen entered via the tap gesture
#endif
        private bool _ending;
        private bool _videoErrored;
        private Coroutine _run;

        // ── Fallback slates (WO-561) — only used if the video fails ─────────────
        private static readonly IntroSlate[] Slates =
        {
            new IntroSlate("Intro/intro-heart-ablaze",
                "Once, the Heart of Elarion blazed — a world-tree whose light was the breath of all living things.", 5.5f),
            new IntroSlate("Intro/intro-dimming",
                "Then came the Dimming: a grief older than memory, and the Heart's light began to fail.", 5.5f),
            new IntroSlate("Intro/intro-hollow-ones",
                "The Hollow Ones rose — not monsters, but the broken, drawn to the last warmth they could feel.", 5.5f),
            new IntroSlate("Intro/intro-knight-call",
                "One answered. A knight, Grom, sworn to carry a single ember back into the dark.", 5.5f),
            new IntroSlate("Intro/intro-reclaim",
                "Drive back the dark. Let the Heart grow. Reclaim the light of Elarion.", 6f, titleCard: true),
        };
        private const float DipSeconds = 0.35f;

        private Image _slateImage;
        private Image _captionBand;
        private TextMeshProUGUI _caption;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _subtitle;
        private Image _dip;
        private int _index = -1;

        // =====================================================================
        //  Boot — try video, fall back to slates
        // =====================================================================
        private void Start()
        {
            BuildCanvas();
            _run = StartCoroutine(BootVideo());
        }

        private void Update()
        {
            // Any keyboard key ends the intro (skip straight in). Tap/Skip are uGUI buttons.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) EndIntro();
        }

        /// <summary>Tries to play the StreamingAssets video full-screen. On any failure
        /// (missing URL / errorReceived / prepare timeout) LogWarnings and falls back to
        /// the slate sequence so boot never blocks.</summary>
        private IEnumerator BootVideo()
        {
            string url = Path.Combine(Application.streamingAssetsPath, VideoRelPath);

            // Wire the VideoPlayer (URL source — no VideoClip import needed).
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = url;
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.skipOnDrop = false;   // SplashLoading lesson — don't snap frames on a slow decoder
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;

            // Pre-prepare placeholder RenderTexture -> RawImage surface. WO-704: this is
            // only the placeholder; once the video is prepared the RT is reallocated at
            // the SOURCE dimensions (see below) so nothing is squashed into a portrait RT.
            _rt = new RenderTexture(1080, 1920, 0) { name = "IntroVideoRT" };
            _videoPlayer.targetTexture = _rt;
            _videoSurface.texture = _rt;

            // Audio: play the video's own track directly via a dedicated AudioSource.
            // (OWNER FLAG — audio bus: this is NOT routed through the SFX/music
            //  mixer bus; it plays at the AudioSource's own output. Direct playback
            //  was chosen because mixer routing here is non-trivial. Flagged for the
            //  owner if the intro should sit on the music bus / respect its volume.)
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL does NOT support VideoAudioOutputMode.AudioSource — it raises errorReceived, which
            // drops the intro into the (imageless) slate fallback so the VIDEO NEVER SHOWS on web. Use
            // Direct (matches the working OnboardingSceneBuilder VideoPlayer, :149). 2026-07-01 fix:
            // "intro not streaming on web — players get test boxes". _audioSource stays created (kept
            // non-null so later refs are safe) but is not the video's output on WebGL.
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _videoPlayer.EnableAudioTrack(0, true);
#else
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.EnableAudioTrack(0, true);
            _videoPlayer.SetTargetAudioSource(0, _audioSource);
#endif

            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.loopPointReached += OnVideoEnded;

            // Missing file → straight to fallback (don't even Prepare). But ONLY for a real LOCAL
            // path. RCA 2026-07-16 (owner "video not playing on web" — proving line from her prod
            // session: "Video not found at 'https://.../StreamingAssets/Video/Defenders.mp4'"):
            // on WebGL, Application.streamingAssetsPath is an HTTP URL, and File.Exists("https://...")
            // returns FALSE WITHOUT THROWING — so the old guard wrongly concluded "missing" and
            // skipped Prepare() entirely, dropping every web player to the slate even though the mp4
            // serves 200. The catch-based escape never fired because File.Exists doesn't throw here.
            // Fix: only File.Exists a genuine local path; for a remote (http/https) URL, let
            // VideoPlayer.Prepare() decide (its errorReceived path already handles a true failure).
            bool isRemote = url.StartsWith("http://") || url.StartsWith("https://") || url.Contains("://");
            bool urlMissing = false;
            if (!isRemote)
            {
                try { urlMissing = !File.Exists(url); }
                catch { urlMissing = false; }
            }
            if (urlMissing)
            {
                Debug.LogWarning($"[IntroSequence] Video not found at '{url}' — falling back to slate intro.");
                StartFallback();
                yield break;
            }

            _videoPlayer.Prepare();

            float waited = 0f;
            while (!_videoPlayer.isPrepared && !_videoErrored && waited < PrepareTimeoutSeconds)
            {
                if (_ending) yield break;
                waited += Time.deltaTime;
                yield return null;
            }

            if (_videoErrored || !_videoPlayer.isPrepared)
            {
                Debug.LogWarning($"[IntroSequence] Video failed to prepare " +
                                 $"(errored={_videoErrored}, prepared={_videoPlayer.isPrepared}) — falling back to slate intro.");
                StartFallback();
                yield break;
            }

            // WO-704: size the RT + aspect fitter from the PREPARED source dimensions so
            // the trailer renders at its native aspect (envelope-cropped, never stretched).
            ApplySourceDimensions();

            // Show the surface and play. loopPointReached → EndIntro (natural end).
            _videoSurface.enabled = true;
            _videoPlayer.Play();
            // Owner 2026-06-29 ("only use the video"): the boot/title music was overlapping the
            // video's own voiceover. Fade it out so the intro plays on the video's audio alone.
            // Only on the VIDEO path — the fallback slate sequence (no VO) keeps its music.
            CoreServices.Audio?.StopMusic();
            // Real video is playing — the fallback caption band (+ its gold rule child)
            // must never overlay the video (owner 2026-06-28). Hide it explicitly.
            if (_captionBand != null) _captionBand.gameObject.SetActive(false);
            Debug.Log("[IntroSequence] Video playing full-screen.");
        }

        /// <summary>WO-704: once the video is prepared, reallocate the RenderTexture at
        /// the source's native dimensions and point the AspectRatioFitter at the source
        /// aspect (EnvelopeParent = full-bleed crop, never letterbox, never distortion).
        /// Guards zero/absurd reported dimensions by keeping the portrait placeholder.</summary>
        private void ApplySourceDimensions()
        {
            uint w = _videoPlayer != null ? _videoPlayer.width : 0u;
            uint h = _videoPlayer != null ? _videoPlayer.height : 0u;

            if (w == 0u || h == 0u || w > 8192u || h > 8192u)
            {
                FlowTrace.Warn("Intro", $"prepared video reported absurd dimensions {w}x{h} -> keeping 1080x1920 placeholder RT");
                return;
            }

            if (_rt == null || _rt.width != (int)w || _rt.height != (int)h)
            {
                if (_rt != null) { _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture((int)w, (int)h, 0) { name = "IntroVideoRT" };
                _videoPlayer.targetTexture = _rt;
                if (_videoSurface != null) _videoSurface.texture = _rt;
                FlowTrace.Step("Intro", $"RT reallocated to source dimensions {w}x{h}");
            }

            if (_videoFitter != null)
            {
                _videoFitter.aspectRatio = (float)w / h;
                FlowTrace.Step("Intro", $"video surface aspect fitter set to {(float)w / h:F4} (EnvelopeParent full-bleed)");
            }
        }

        private void OnVideoError(VideoPlayer _, string message)
        {
            Debug.LogWarning($"[IntroSequence] VideoPlayer error: {message}");
            _videoErrored = true;
        }

        private void OnVideoEnded(VideoPlayer _)
        {
            EndIntro();
        }

        // =====================================================================
        //  Canvas — full-screen surface + skip/tap controls
        // =====================================================================
        private void BuildCanvas()
        {
            _canvas = ElarionUiKit.BuildModalCanvas("IntroCanvas", sortingOrder: 6000);
            _canvas.transform.SetParent(transform, false);
            Transform root = _canvas.transform;

            // Black backdrop (covers letterbox + shows while the video prepares).
            var bg = NewImage(root, "Backdrop", Color.black);
            Stretch(bg.rectTransform, 0f, 0f, 1f, 1f);

            // Full-screen video surface (RawImage shows the RenderTexture). Hidden
            // until the video is actually playing; the fallback uses _slateImage instead.
            var surfGo = new GameObject("VideoSurface", typeof(RawImage));
            surfGo.transform.SetParent(root, false);
            _videoSurface = surfGo.GetComponent<RawImage>();
            _videoSurface.color = Color.white;
            _videoSurface.raycastTarget = false;
            Stretch(_videoSurface.rectTransform, 0f, 0f, 1f, 1f);
            // WO-704: envelope the parent (fill the screen, crop overflow) at the video's
            // native aspect — cinematic full-bleed, never letterbox bars, never stretch.
            // Ratio starts at the portrait placeholder; ApplySourceDimensions() sets the
            // real ratio once the video is prepared. VIDEO SURFACE ONLY — the fallback
            // slate keeps its own full-stretch layout untouched.
            _videoFitter = surfGo.AddComponent<AspectRatioFitter>();
            _videoFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _videoFitter.aspectRatio = 1080f / 1920f;
            _videoSurface.enabled = false;

            // Fallback slate image (also full-screen, used only on the fallback path).
            _slateImage = NewImage(root, "Slate", Color.white);
            Stretch(_slateImage.rectTransform, 0f, 0f, 1f, 1f);
            _slateImage.preserveAspect = false;
            _slateImage.enabled = false;

            // Full-screen invisible tap target — advances/skips (click straight through).
            var tap = ElarionUiKit.Button(root, "", ElarionUiKit.ButtonKind.Quiet,
                Vector2.zero, Vector2.one, OnTap);
            var tapImg = tap.GetComponent<Image>();
            if (tapImg != null) tapImg.color = new Color(0f, 0f, 0f, 0f);   // invisible, still raycasts

            // Fallback caption band (only populated/shown on the fallback path).
            var band = NewImage(root, "CaptionBand", new Color(0.02f, 0.02f, 0.025f, 0.72f));
            Stretch(band.rectTransform, 0.06f, 0.07f, 0.94f, 0.24f);
            band.raycastTarget = false;
            var rule = NewImage(band.transform, "GoldRule", ElarionUi.Gold);
            var rr = rule.rectTransform;
            rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
            rr.offsetMin = new Vector2(0f, -3f); rr.offsetMax = new Vector2(0f, 0f);
            rule.raycastTarget = false;
            _captionBand = band;
            // Hide via the GameObject, not band.enabled — disabling only the band's own
            // Image leaves the GoldRule child Image still drawing a gold line ~2/3 down
            // the screen over the playing video (owner 2026-06-28). SetActive hides children.
            band.gameObject.SetActive(false);

            // Kit tokens: parchment body text + gold title (obsidian/gold language).
            _caption = ElarionUiKit.Label(band.transform, "", 0.10f, 0.92f,
                ElarionUi.Parchment, 40, TextAlignmentOptions.Center,
                0.05f, 0.95f, spacing: 0.5f);

            _title = ElarionUiKit.Label(root, "", 0.40f, 0.62f, ElarionUi.Gold, 92,
                TextAlignmentOptions.Center, 0.05f, 0.95f, spacing: 2f, bold: true);
            _title.text = "";
            _subtitle = ElarionUiKit.Label(root, "", 0.34f, 0.40f,
                ElarionUi.Parchment, 40, TextAlignmentOptions.Center);
            _subtitle.name = "Subtitle";
            _subtitle.text = "";

            // Visible Skip button (top-right) — ends the intro immediately. No icon glyphs; the caption
            // is a localized word plus an ASCII chevron (per-locale, so non-en values are not ASCII).
            // WO-1857: its own row rather than common.skip, because the caption carries a
            // DIRECTIONAL affordance ("Skip  >") that an RTL locale has to mirror - the translator
            // owns where the chevron sits, which a bare shared word cannot express.
            ElarionUiKit.Button(root,
                new LocalizedText("dialogue.intro.skip").Resolve(), ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.74f, 0.92f), new Vector2(0.96f, 0.975f), EndIntro);

            // Dip overlay on top — starts opaque so the first frame/slate fades in.
            _dip = NewImage(root, "Dip", Color.black);
            Stretch(_dip.rectTransform, 0f, 0f, 1f, 1f);
            _dip.raycastTarget = false;
            SetAlpha(_dip, 1f);
            // Fade the opening black out shortly after boot so the video/slate reveals.
            StartCoroutine(FadeDip(1f, 0f, DipSeconds));
        }

        // Tap: in video mode, skip the whole intro; in slate mode, advance one beat.
        // WO-704 (WebGL only): the tap is the user gesture browser fullscreen APIs
        // require, so a best-effort Screen.fullScreen request rides it BEFORE the skip
        // fires — skip semantics unchanged. Quiet-fail per the web-errors law: if the
        // browser refuses, nothing is shown to the player.
        private void OnTap()
        {
            if (_ending) return;
            if (_videoSurface != null && _videoSurface.enabled)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                try
                {
                    Screen.fullScreen = true;
                    _fullscreenRequested = true;
                    FlowTrace.Step("Intro", "WebGL browser-fullscreen requested on tap (best-effort; rides the skip gesture)");
                }
                catch (Exception e)
                {
                    FlowTrace.Warn("Intro", $"WebGL fullscreen request refused/threw (quiet-fail): {e.Message}");
                }
#endif
                EndIntro();
                return;
            }
            AdvanceSlate();
        }

        // =====================================================================
        //  Fallback slate sequence (WO-561) — only on video failure
        // =====================================================================
        private void StartFallback()
        {
            // Tear down the (failed) video pieces so nothing lingers.
            ReleaseVideo();
            if (_captionBand != null) _captionBand.gameObject.SetActive(true);
            if (_run != null) StopCoroutine(_run);
            _run = StartCoroutine(RunSlateSequence());
        }

        private IEnumerator RunSlateSequence()
        {
            for (int i = 0; i < Slates.Length; i++)
            {
                if (_ending) yield break;
                ShowSlate(i);
                yield return FadeDip(1f, 0f, DipSeconds);
                float t = 0f;
                while (t < Slates[i].Hold && !_ending && _index == i)
                { t += Time.deltaTime; yield return null; }
                if (_ending) yield break;
                if (_index == i && i < Slates.Length - 1)
                    yield return FadeDip(0f, 1f, DipSeconds);
            }
            EndIntro();
        }

        private void ShowSlate(int i)
        {
            _index = i;
            var s = Slates[i];

            var sprite = Resources.Load<Sprite>(s.Image);
            if (sprite != null) { _slateImage.sprite = sprite; _slateImage.enabled = true; }
            else
            {
                _slateImage.enabled = false;
                Debug.LogWarning($"[IntroSequence] slate sprite '{s.Image}' not found — caption-on-black.");
            }

            if (_caption != null) _caption.text = s.Caption;
            if (s.TitleCard)
            {
                // WO-1857: the intro title card is BRANDING - both rows carry the identical English
                // in every locale (proper nouns, same convention as common.echoes_elarion). They are
                // keyed rather than hardcoded so a locale that must transliterate CAN, and they do
                // NOT reuse heroSelect.title / common.echoes_elarion: the classification doc §6b
                // parks that cross-screen reuse for an owner yes/no, and common.echoes_elarion is
                // all-caps, which would silently restyle this sub-line.
                if (_title != null)
                    _title.text = new LocalizedText("dialogue.intro.title_card").Resolve();
                if (_subtitle != null)
                    _subtitle.text = new LocalizedText("dialogue.intro.title_card_sub").Resolve();
            }
        }

        private void AdvanceSlate()
        {
            if (_ending) return;
            if (_index < 0) return;   // not in slate mode yet
            if (_index >= Slates.Length - 1) { EndIntro(); return; }
            int next = _index + 1;
            if (_run != null) StopCoroutine(_run);
            _run = StartCoroutine(JumpTo(next));
        }

        private IEnumerator JumpTo(int next)
        {
            yield return FadeDip(0f, 1f, 0.18f);
            for (int i = next; i < Slates.Length; i++)
            {
                if (_ending) yield break;
                ShowSlate(i);
                yield return FadeDip(1f, 0f, DipSeconds);
                float t = 0f;
                while (t < Slates[i].Hold && !_ending && _index == i)
                { t += Time.deltaTime; yield return null; }
                if (_ending) yield break;
                if (_index == i && i < Slates.Length - 1)
                    yield return FadeDip(0f, 1f, DipSeconds);
            }
            EndIntro();
        }

        private IEnumerator FadeDip(float from, float to, float seconds)
        {
            float t = 0f;
            SetAlpha(_dip, from);
            while (t < seconds && !_ending)
            { t += Time.deltaTime; SetAlpha(_dip, Mathf.Lerp(from, to, t / seconds)); yield return null; }
            SetAlpha(_dip, to);
        }

        // =====================================================================
        //  End / hand-off + cleanup
        // =====================================================================
        private void EndIntro()
        {
            if (_ending) return;
            _ending = true;
            if (_run != null) StopCoroutine(_run);
            if (_dip != null) SetAlpha(_dip, 1f);   // black out instantly so the scene swap is unseen
#if UNITY_WEBGL && !UNITY_EDITOR
            // WO-704: intro over — restore/cancel the best-effort browser fullscreen.
            // Quiet-fail: never a player-visible error if the browser objects.
            if (_fullscreenRequested)
            {
                try
                {
                    Screen.fullScreen = false;
                    FlowTrace.Step("Intro", "WebGL browser-fullscreen restored to windowed on intro end");
                }
                catch (Exception e)
                {
                    FlowTrace.Warn("Intro", $"WebGL fullscreen restore threw (quiet-fail): {e.Message}");
                }
                _fullscreenRequested = false;
            }
#endif
            ReleaseVideo();
            Debug.Log("[IntroSequence] Intro complete — routing to hero select.");
            SceneRouter.GoHeroSelect();
            Destroy(gameObject);                     // also destroys the canvas (child) + RawImage
        }

        /// <summary>Stops + tears down the VideoPlayer/AudioSource and releases the
        /// RenderTexture. Idempotent — safe to call on fallback and on end.</summary>
        private void ReleaseVideo()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.errorReceived -= OnVideoError;
                _videoPlayer.loopPointReached -= OnVideoEnded;
                try { _videoPlayer.Stop(); } catch { /* never throw on teardown */ }
                _videoPlayer.targetTexture = null;
            }
            if (_videoSurface != null) _videoSurface.texture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        private void OnDestroy()
        {
            // Belt-and-braces: release the RT even if EndIntro was bypassed.
            ReleaseVideo();
        }

        // ── uGUI helpers ──────────────────────────────────────────────────────
        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static void Stretch(RectTransform r, float x0, float y0, float x1, float y1)
        {
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        private static void SetAlpha(Image img, float a)
        {
            if (img == null) return;
            var c = img.color; c.a = a; img.color = c;
        }
    }
}
