// =============================================================================
// OnboardingFlow — the first-run guided tutorial (audit P0-11 / §2.5).
// -----------------------------------------------------------------------------
// The missing-components audit flags the Onboarding module as "built only as far
// as the cinematics": the cold open plays, but there is no first-run TEACHING,
// and GameState.Onboarded is "never set to true in normal play" — so the cold
// open re-plays on every launch (the explicit bug in §2.5 / P0-11).
//
// This component is the missing tutorial: a short, SKIPPABLE six-beat coach-mark
// sequence taught the first time the player reaches the village —
//
// WO-C conversion (2026-07-03, coverage matrix row #20): the coach-mark overlay
// was a UIDocument/UITK panel (TutorialOverlay.uxml) with a code-built UITK
// fallback for the "UXML renders empty in player builds" landmine (CLAUDE.md §8).
// This finishes the job the other front-end screens took (Title/StoryIntro/
// HeroSelect): the overlay is now a code-built uGUI card on the Blink Obsidian
// kit (ElarionUiKit) — its own ScreenSpaceOverlay canvas, kit panel chrome
// (caption in the header, narrated copy in the body, Skip + Next as Obsidian
// family buttons), CanvasGroup fades. No UIDocument, no UXML, no PanelSettings at
// runtime, so nothing scene-hosted can blank the FTUE. The editor scene builder
// still adds a legacy UIDocument to this GameObject; Awake disables it so it can
// neither render nor keep a PanelRaycaster in the click stack.
//
// The beats —
//
//   1. Welcome      — you are the Keeper; the realm is yours to hold.
//   2. The Heart    — Elarion, the Heart, is what you defend.
//   3. Force-field  — the Heart-light field on the walls/gates turns enemies
//                     back; a damaged wall thins it — keep the walls mended.
//   4. Build        — open the Build menu and raise a tower.
//   5. Place a pet  — station a starter Warden at a slot.
//   6. Wave 1       — begin the first wave.
//
// THE Onboarded GATE (audit P0-11, the headline fix):
//   - On enable, the flow reads GameStateService.Instance.State.Onboarded.
//     Onboarded == true  -> the tutorial does NOT run (returning player).
//     Onboarded == false -> the tutorial runs.
//   - On the LAST beat's advance OR on Skip at any beat, the flow calls
//     GameStateService.FinishOnboarding(), which sets Onboarded = true, raises
//     PlayerChanged and Save()s it to PlayerPrefs. It never replays after that.
//   - That same flag already gates StoryIntroController.ShouldAutoPlay
//     (`return !svc.State.Onboarded`). The cold open re-played every launch ONLY
//     because nothing ever flipped the flag — this flow is what flips it, so the
//     cold-open replay bug is fixed by completing or skipping the tutorial once.
//     No change to StoryIntroController is required.
//
// MODULE ISOLATION (v2 port-spec Part 2) — IMPORTANT, mirrors the HUD module:
//   DeNelle.Onboarding references DeNelle.Core ONLY (for GameStateService /
//   SceneRouter / canon strings) — it does NOT reference DeNelle.Village or
//   DeNelle.HUD. The flow therefore cannot call BuildMenu.Open(), cannot read
//   the wave manager and cannot place a pet itself. Instead it is a PASSIVE
//   coach-mark display, exactly like VillageHudController:
//     - It exposes UnityEvents the integrator hooks to gameplay actions
//       (OpenBuildMenuRequested, BeginWaveRequested).
//     - It exposes Notify* methods the integrator calls FROM gameplay events
//       (NotifyTowerBuilt, NotifyPetPlaced) so action-beats auto-advance when
//       the player actually does the thing — not just when they tap Next.
//   The seam is Core types + UnityEvents, never a gameplay-module reference.
//   See docs/port-notes/onboarding.md for the integrator wiring.
//
// async UniTask for the fade — never async void (v2 port-spec Part 3 mandate).
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
using UnityEngine.Events;
using UnityEngine.UI;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Which gameplay action, if any, a tutorial beat is waiting on. An
    /// <see cref="Action"/>-typed beat auto-advances when the integrator reports
    /// the action done (or when the player taps Next); a
    /// <see cref="None"/> beat advances on Next only.
    /// </summary>
    public enum TutorialGate
    {
        /// <summary>Pure-narration beat — advances on the Next button only.</summary>
        None,
        /// <summary>Beat completes when a tower is built (NotifyTowerBuilt).</summary>
        BuildTower,
        /// <summary>Beat completes when a starter pet is placed (NotifyPetPlaced).</summary>
        PlacePet,
    }

    /// <summary>
    /// Drives the first-run guided tutorial overlay — a short, skippable
    /// six-beat coach-mark sequence shown only when
    /// <see cref="GameState.Onboarded"/> is false. Marks onboarding complete
    /// (which stops the cold open re-playing — audit P0-11) on completion or
    /// skip. A passive display: gameplay is reached through Core seams and
    /// UnityEvents, never a direct gameplay-module reference (port-spec Part 2).
    /// </summary>
    public sealed class OnboardingFlow : MonoBehaviour
    {
        [Header("UI (legacy — WO-C)")]
        [Tooltip("The retired UIDocument (TutorialOverlay.uxml) the scene builder still adds to this " +
                 "GameObject. WO-C: the coach-marks render via code-built uGUI now; Awake disables this " +
                 "document so it cannot render or eat input. Kept only so the editor wiring still binds.")]
        [SerializeField] private UnityEngine.UIElements.UIDocument _document;

        [Header("Behaviour")]
        [Tooltip("Auto-run the tutorial in Start() when the save says NOT onboarded. " +
                 "Disable to drive it manually (e.g. from a 'Replay tutorial' option).")]
        [SerializeField] private bool _runOnStart = true;

        [Tooltip("Force the tutorial to run even when the save says onboarded. Editor testing only.")]
        [SerializeField] private bool _forceRun;

        [Tooltip("Seconds the overlay fade-in / fade-out takes.")]
        [SerializeField, Min(0f)] private float _fadeSeconds = 0.35f;

        [Header("Events — wired to gameplay by the integrator (port-spec Part 2)")]
        [Tooltip("Raised on the Build beat — the integrator hooks this to the village BuildMenu.Open(). " +
                 "DeNelle.Onboarding cannot reference BuildMenu, so the wiring happens in the village scene.")]
        public UnityEvent OpenBuildMenuRequested = new UnityEvent();

        [Tooltip("Raised when the tutorial finishes its last beat — the integrator hooks this to the " +
                 "village WaveManager to start Wave 1.")]
        public UnityEvent BeginWaveRequested = new UnityEvent();

        [Tooltip("Raised when the tutorial ends (completed OR skipped) — for the integrator to re-enable " +
                 "any HUD / input it suppressed while the coach-marks were up.")]
        public UnityEvent TutorialClosed = new UnityEvent();

        // ── Code-built uGUI overlay (Blink Obsidian kit) ─────────────────────
        private GameObject _canvas;                 // own overlay canvas (no shared panel)
        private CanvasGroup _group;                 // whole-overlay fade
        private ElarionUiKit.PanelChrome _chrome;   // kit panel chrome (caption in header)
        private TextMeshProUGUI _caption;           // beat kicker (the chrome header title)
        private TextMeshProUGUI _progress;          // "i / N" readout
        private TextMeshProUGUI _body;              // narrated body copy
        private Button _skipButton;
        private Button _nextButton;
        private TextMeshProUGUI _nextLabel;         // the Next button's kit label (retext per beat)

        private int _beatIndex = -1;
        private bool _running;
        private bool _built;
        private CancellationTokenSource _cts;

        // UIF: single-modal arbiter handle. Registering the coach-mark overlay makes it visible
        // to the back-button / battle-lock arbiter; its close delegate skips the tutorial and
        // isOpen reflects the live running state.
        private PanelHandle _panelHandle;

        /// <summary>True while the coach-mark sequence is on screen.</summary>
        public bool IsRunning => _running;

        /// <summary>True once the tutorial has ended this session (completed or skipped).</summary>
        public bool HasFinished { get; private set; }

        // =====================================================================
        //  The six tutorial beats
        // -----------------------------------------------------------------------
        //  Deliverable beats: welcome -> the Heart -> the force-field -> open
        //  Build menu / build a tower -> place a starter pet -> Wave 1 begins.
        //  Each beat's narration
        //  is a CANON STRING resolved at runtime from en.json (tutorial.steps.*
        //  — already present in StreamingAssets/Data/Canonical/en.json) — never
        //  typed inline (v2 port-spec Part 4).
        //  WO-1857: the kicker captions and the advance-button labels used to be
        //  "short UI labels (not narrative copy) so they stay in C#". That carve-out
        //  is RETIRED — they are words the player reads, so they are keys too, and
        //  they now follow CopyKey's own pattern: a key here, resolved at DISPLAY
        //  time in ShowBeat, never an English literal in this array.
        // =====================================================================

        /// <summary>One coach-mark beat — caption key, canon copy key, gate, controls.</summary>
        private readonly struct Beat
        {
            /// <summary>Locale key for the short UI kicker shown above the body copy.</summary>
            public readonly string CaptionKey;
            /// <summary>en.json key for the narrated body copy (tutorial.steps.*).</summary>
            public readonly string CopyKey;
            /// <summary>Which gameplay action, if any, this beat waits on.</summary>
            public readonly TutorialGate Gate;
            /// <summary>Locale key for the advance button's label on this beat.</summary>
            public readonly string NextLabelKey;

            public Beat(string captionKey, string copyKey, TutorialGate gate, string nextLabelKey)
            {
                CaptionKey = captionKey;
                CopyKey = copyKey;
                Gate = gate;
                NextLabelKey = nextLabelKey;
            }
        }

        // tutorial.steps.1         — "Welcome home, Guardian. The Heart is the Lantern…"
        // tutorial.steps.2         — "The Hollowed come from the dark beyond the walls…"
        // tutorial.steps.forceField— "The walls hold a force-field of the Heart's own light…"
        // tutorial.steps.3         — "Build your defenses with what you have…"
        // tutorial.steps.6         — "Your Wardens fight beside you. Tap one to give…"
        // tutorial.steps.5         — "When you are ready, begin a wave…"
        private static readonly Beat[] Beats =
        {
            new Beat("onboarding.tutorial.caption_welcome",
                                        "tutorial.steps.1",          TutorialGate.None,       "common.next"),
            new Beat("onboarding.tutorial.caption_heart",
                                        "tutorial.steps.2",          TutorialGate.None,       "common.next"),
            // Force-field beat — explains the Heart-light field on the walls /
            // gates that turns enemies back, and that a damaged wall thins it,
            // leading into the wall-repair mechanic another workstream builds.
            new Beat("onboarding.tutorial.caption_force_field",
                                        "tutorial.steps.forceField", TutorialGate.None,       "common.next"),
            new Beat("onboarding.tutorial.caption_raise_tower",
                                        "tutorial.steps.3",          TutorialGate.BuildTower,
                                        "onboarding.tutorial.next_open_build_menu"),
            new Beat("onboarding.tutorial.caption_wardens",
                                        "tutorial.steps.6",          TutorialGate.PlacePet,   "common.next"),
            new Beat("onboarding.tutorial.caption_hold_line",
                                        "tutorial.steps.5",          TutorialGate.None,
                                        "onboarding.tutorial.next_begin_wave_1"),
        };

        /// <summary>Number of beats in the tutorial sequence.</summary>
        public static int BeatCount => Beats.Length;

        // WO-1012 P1 (owner 2026-08-08 "wall-of-text welcome cards"): the first three
        // narration-only beats no longer render as modal CARDS — they play as guide
        // ONE-LINERS through the kit's GuideLineUi (lower-left portrait + line,
        // auto-dismissing) over the settled town view, and the card overlay only
        // appears from the first ACTION beat. Copy is untouched (same canon keys) —
        // presentation only. The ~4s camera moment named in the WO is NOT driven from
        // here: DeNelle.Onboarding has no camera seam (module isolation) — P3 owns it.
        private const int IntroOneLinerBeats = 3;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            // WO-C: the scene builder still adds the legacy UIDocument (TutorialOverlay.uxml)
            // to this GameObject. The coach-marks render via code-built uGUI now, so disable
            // it — it renders nothing for us and must not keep a PanelRaycaster in the click
            // stack (the duplicate-UIDocument input-eating landmine).
            if (_document == null)
                _document = GetComponent<UnityEngine.UIElements.UIDocument>();
            if (_document != null && _document.enabled)
            {
                _document.enabled = false;
                FlowTrace.Step("Onboarding", "WO-C: disabled OnboardingFlow's legacy UIDocument — coach-marks render via uGUI now.");
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
            // UIF: don't leak the arbiter slot if destroyed while open (scene unload).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            if (_canvas != null) Destroy(_canvas);
        }

        private void Start()
        {
            if (_runOnStart)
                TryRun();
        }

        // =====================================================================
        //  Overlay construction (code-built uGUI on the Blink Obsidian kit)
        // =====================================================================

        /// <summary>
        /// Builds the coach-mark overlay once, in code, on its own kit canvas — a
        /// dimming scrim plus a centred Obsidian panel (caption in the header zone,
        /// a "i / N" progress readout, the narrated body copy, and Skip + Next as
        /// Obsidian family buttons). No UIDocument / UXML / PanelSettings, so the
        /// FTUE renders identically in the editor and in player builds (CLAUDE.md
        /// §8). Starts hidden — <see cref="Run"/> reveals it. Idempotent.
        /// </summary>
        private void EnsureBuilt()
        {
            using var _ = FlowTrace.Enter("Onboarding", "OnboardingFlow.EnsureBuilt (uGUI coach-marks)");
            if (_built && _canvas != null) return;

            // DEF-153: the coach-mark is a MODAL that must render ABOVE the in-game
            // HUD (HUD sorts ~80-110; BuildMenu adopts HUD+5). A high sort keeps it
            // over the HUD and the build menu its "Raise a tower" beat opens, while
            // staying below system Settings (32000) / the load overlay.
            _canvas = ElarionUiKit.BuildModalCanvas("OnboardingTutorialUI", 31000);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
            _group = _canvas.AddComponent<CanvasGroup>();

            // Dimming scrim — swallows world taps while a coach-mark is up.
            var scrim = ElarionUiKit.AddImage(_canvas.transform, "Scrim",
                Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.55f), rounded: false);
            var scrimImg = scrim.GetComponent<Image>();
            if (scrimImg != null) scrimImg.raycastTarget = true;

            // Centred Obsidian panel — a compact speech-bubble card (owner DEF-153:
            // centre it so the mobile HUD cluster can't cover it), capped so it stays
            // a bubble on wide landscape. Caption lives in the chrome header; the
            // shared Close is HIDDEN (Skip is the dismiss for this forced flow).
            // withBackdrop:false — the coach-mark teaches on-screen things (the Heart, the
            // walls, the build menu), so keep the village visible behind the card; our own
            // light 0.55 scrim above dims it just enough for the copy to read (the panel's
            // default 0.94 backdrop would hide the world the coach-marks point at).
            _chrome = ElarionUiKit.BuildObsidianPanel(_canvas.transform, string.Empty,
                new Vector2(0.10f, 0.31f), new Vector2(0.90f, 0.69f), onClose: null,
                withBackdrop: false);
            if (_chrome.close != null) _chrome.close.gameObject.SetActive(false);
            _caption = _chrome.title;

            Transform body = _chrome.layout != null && _chrome.layout.body != null
                ? _chrome.layout.body.transform
                : _chrome.content.transform;

            // Progress readout — top-right of the body well.
            _progress = ElarionUiKit.Label(body, string.Empty,
                0.86f, 1.00f, ElarionUi.Gilt, ElarionUi.FontMicro,
                TextAlignmentOptions.Right, 0.55f, 0.98f, bold: true);
            _progress.raycastTarget = false;

            // Narrated body copy — centred band, wraps.
            _body = ElarionUiKit.Label(body, string.Empty,
                0.30f, 0.82f, ElarionUi.Parchment, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.06f, 0.94f);
            _body.textWrappingMode = TextWrappingModes.Normal;
            _body.raycastTarget = false;

            // Controls — Skip (Gray, left) + Next / Begin (Green primary CTA, right).
            // WO-1857: both faces reuse the game-wide shared keys the COMMON_KEYS_REGISTRY
            // names against these two exact call sites.
            _skipButton = ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("common.skip").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.04f, 0.04f), new Vector2(0.34f, 0.20f), OnSkipClicked);

            _nextButton = ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("common.next").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.60f, 0.04f), new Vector2(0.96f, 0.20f), OnNextClicked);
            _nextLabel = _nextButton != null
                ? _nextButton.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;

            // V — the core coach-mark controls MUST exist, or the tutorial is on
            // screen but un-advanceable. Fail-loud (break-log) if any is missing.
            if (_caption == null || _body == null || _nextButton == null || _skipButton == null)
            {
                FlowTrace.Fail("Onboarding",
                    $"EnsureBuilt VERIFY FAILED — coach-mark controls unresolved " +
                    $"(caption={(_caption != null)} body={(_body != null)} next={(_nextButton != null)} skip={(_skipButton != null)}). " +
                    "Tutorial may render but cannot be advanced.");
            }

            // UIF: register with the single-modal arbiter once. Close delegate = Skip (the
            // dismiss for this forced flow); isOpen reflects whether the coach-marks are live.
            if (_panelHandle == null)
                _panelHandle = PanelManager.Register("Onboarding", () => Finish(completed: false), () => _running);

            // Start hidden — Run() reveals it.
            SetOverlayVisible(false);
            _built = true;
        }

        // =====================================================================
        //  Run gate — the Onboarded check (audit P0-11)
        // =====================================================================

        /// <summary>
        /// Runs the tutorial only when it should — i.e. the save reports the
        /// player has NOT completed onboarding (or <c>_forceRun</c> is set for
        /// editor testing). A returning player (<see cref="GameState.Onboarded"/>
        /// == true) is left untouched and <see cref="TutorialClosed"/> is raised
        /// immediately so the integrator does not wait on a tutorial that will
        /// never show. Returns true when the tutorial actually started.
        /// </summary>
        public bool TryRun()
        {
            if (_running || HasFinished) return false;

            if (!ShouldRun)
            {
                // Returning player, OR Tutorial V2 owns the FTUE (one-guide lock) —
                // signal closed so the integrator's "tutorial done" continuation
                // still fires and never waits on a coach-mark that will not show.
                if (FeatureFlags.TutorialV2 && !_forceRun)
                    FlowTrace.Step("Onboarding",
                        "TryRun: STAND DOWN — ff.tutorialv2 ON; TutorialFlow + wolf {guide} owns the FTUE " +
                        "(OnboardingFlow coach-marks idle).");
                HasFinished = true;
                TutorialClosed?.Invoke();
                return false;
            }

            Run();
            return true;
        }

        /// <summary>
        /// True when the first-run tutorial should play — a brand-new save has
        /// <see cref="GameState.Onboarded"/> == false. Mirrors the gate
        /// <see cref="StoryIntroController.ShouldAutoPlay"/> uses for the cold
        /// open, so the two first-run surfaces stay in lock-step.
        /// </summary>
        public bool ShouldRun
        {
            get
            {
                // ONE-GUIDE LOCK (WO-971 / owner 2026-08-10 "should only be the wolf"):
                // Tutorial V2 (TutorialFlow + pet-Echo {guide}) is the sole FTUE when
                // ff.tutorialv2 is ON. This legacy coach-mark flow stands down so the
                // player never gets two concurrent tutorials fighting for the screen.
                // Force-run still works for editor testing of this component alone.
                if (!_forceRun && FeatureFlags.TutorialV2)
                    return false;
                if (_forceRun) return true;
                var svc = GameStateService.Instance;
                // No service yet (Core not bootstrapped) — treat as first launch
                // so a new player is never silently denied the tutorial.
                if (svc == null || svc.State == null) return true;
                return !svc.State.Onboarded;
            }
        }

        // =====================================================================
        //  Run / advance / finish
        // =====================================================================

        /// <summary>
        /// Starts the coach-mark sequence at beat 1 unconditionally — skipping
        /// the <see cref="ShouldRun"/> gate. Public so a future "Replay
        /// tutorial" settings option can re-show it on demand; normal first-run
        /// entry goes through <see cref="TryRun"/>.
        /// </summary>
        public void Run()
        {
            using var _ = FlowTrace.Enter("Onboarding", "OnboardingFlow.Run");
            if (_running) { FlowTrace.Step("Onboarding", "Run: already running — no-op."); return; }
            EnsureBuilt();

            _running = true;
            HasFinished = false;
            _beatIndex = 0;

            // V — the flow starts with a bound body label. If the body is null the
            // card beats will show no copy — surface it.
            if (_body == null)
                FlowTrace.Fail("Onboarding", "Run: tutorial started but body label is null — coach-marks will show no copy.");
            else
                FlowTrace.Step("Onboarding", $"Run: tutorial started at beat 1/{Beats.Length}.");

            // WO-1012 P1: beats 1-3 play as guide one-liners; the card overlay (and its
            // arbiter registration) waits for the first action beat.
            PlayIntroOneLiners(this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// WO-1012 P1: plays the leading narration beats as auto-dismissing guide
        /// one-liners (GuideLineUi — portrait + ONE line, lower-left, no card, no Next),
        /// then hands over to the modal card flow at the first gated/action beat.
        /// Copy unchanged (same canon keys); dwell is GuideLineUi's length-scaled
        /// reading pace. Never <c>async void</c> (port-spec Part 3).
        /// </summary>
        private async UniTask PlayIntroOneLiners(CancellationToken token)
        {
            FlowTrace.Step("Onboarding",
                "WO-1012 P1: welcome cards 1-3 RETIRED - narration plays as guide one-liners (GuideLineUi).");
            int i = 0;
            try
            {
                for (; i < IntroOneLinerBeats && i < Beats.Length && Beats[i].Gate == TutorialGate.None; i++)
                {
                    if (!_running) return;   // skipped/finished mid-sequence — stop narrating
                    _beatIndex = i;
                    string line = CanonStrings.Locale(Beats[i].CopyKey);
                    // WO-1012 P2: the narrator is the GUIDE — the player's first pet-Echo
                    // (identity seam; rotation parked). Never a hard-coded hero name.
                    float dwell = GuideLineUi.Show(DeNelle.Core.Tutorial.TutorialGuide.DisplayName, line);
                    await UniTask.Delay(TimeSpan.FromSeconds(dwell + 0.15f),
                        ignoreTimeScale: true, cancellationToken: token);
                }
            }
            catch (OperationCanceledException)
            {
                return;   // destroyed mid-narration — nothing to restore
            }
            if (!_running) return;

            // Hand over to the card flow at the first action beat.
            _beatIndex = Mathf.Min(i, Beats.Length - 1);
            SetOverlayVisible(true);
            // UIF: announce the overlay opened so the arbiter closes any prior panel + arms
            // the back button — deferred to HERE because the one-liners are non-modal.
            if (_panelHandle != null) PanelManager.NotifyOpened(_panelHandle);
            ShowBeat(_beatIndex);
            FadeOverlay(0f, 1f).Forget();
            FlowTrace.Step("Onboarding",
                $"Run: guide one-liners done - card flow resumes at beat {_beatIndex + 1}/{Beats.Length}.");
        }

        /// <summary>Renders one beat into the coach-mark card.</summary>
        private void ShowBeat(int index)
        {
            if (index < 0 || index >= Beats.Length) return;
            var beat = Beats[index];

            // WO-1857: caption and advance label resolve HERE, at display time, from the
            // beat's keys — the same seam CopyKey has always used two lines below.
            if (_caption != null) _caption.text = new LocalizedText(beat.CaptionKey).Resolve();
            if (_progress != null) _progress.text = $"{index + 1} / {Beats.Length}";

            // Body copy — canon string from en.json, never typed inline.
            if (_body != null) _body.text = CanonStrings.Locale(beat.CopyKey);

            // Retext the Next button's kit label (the Obsidian button stays the Green
            // primary CTA every beat; the final beat's label reads "Begin Wave 1").
            if (_nextLabel != null)
                _nextLabel.text = new LocalizedText(beat.NextLabelKey).Resolve();
        }

        /// <summary>
        /// Advances past the current beat. A gated beat (Build / Place pet)
        /// fires its integrator hook and then advances; the integrator may also
        /// auto-advance the same beat early via <see cref="NotifyTowerBuilt"/> /
        /// <see cref="NotifyPetPlaced"/> when the player completes the action.
        /// The last beat's advance ends the tutorial.
        /// </summary>
        private void OnNextClicked()
        {
            if (!_running) return;
            var beat = Beats[_beatIndex];

            // Fire the integrator hook for a gated beat as the player taps
            // through it — e.g. tapping "Open Build menu" opens the menu.
            switch (beat.Gate)
            {
                case TutorialGate.BuildTower:
                    OpenBuildMenuRequested?.Invoke();
                    break;
            }

            AdvanceFromBeat(_beatIndex);
        }

        /// <summary>Skip — ends the tutorial immediately from any beat.</summary>
        private void OnSkipClicked()
        {
            if (!_running) return;
            Finish(completed: false);
        }

        /// <summary>
        /// Moves on from <paramref name="fromIndex"/>: shows the next beat, or
        /// finishes the tutorial when the last beat is left.
        /// </summary>
        private void AdvanceFromBeat(int fromIndex)
        {
            if (fromIndex != _beatIndex) return; // stale call — beat already moved

            if (_beatIndex >= Beats.Length - 1)
            {
                Finish(completed: true);
                return;
            }

            _beatIndex++;
            ShowBeat(_beatIndex);
        }

        // =====================================================================
        //  Integrator notifications — gameplay reports an action done
        // -----------------------------------------------------------------------
        //  The village scene wires its gameplay events to these so an action
        //  beat advances when the player actually DOES the thing. Calling them
        //  when the tutorial is not on that beat is a harmless no-op, so the
        //  integrator can wire them unconditionally.
        // =====================================================================

        /// <summary>
        /// Integrator hook — call when the player places a building/tower.
        /// Auto-advances the "Raise a tower" beat. No-op on any other beat.
        /// </summary>
        public void NotifyTowerBuilt()
        {
            if (!_running) return;
            if (Beats[_beatIndex].Gate == TutorialGate.BuildTower)
                AdvanceFromBeat(_beatIndex);
        }

        /// <summary>
        /// Integrator hook — call when the player stations a pet at a slot.
        /// Auto-advances the "Your Wardens" beat. No-op on any other beat.
        /// </summary>
        public void NotifyPetPlaced()
        {
            if (!_running) return;
            if (Beats[_beatIndex].Gate == TutorialGate.PlacePet)
                AdvanceFromBeat(_beatIndex);
        }

        // =====================================================================
        //  Finish — set Onboarded, persist, fix the cold-open replay (P0-11)
        // =====================================================================

        /// <summary>
        /// Ends the tutorial. Whether the player completed all beats or skipped,
        /// this calls <see cref="GameStateService.FinishOnboarding"/> — which
        /// sets <see cref="GameState.Onboarded"/> = true and Save()s it — so the
        /// tutorial never replays AND the cold-open cinematic (gated on the same
        /// flag) stops re-playing every launch. On a completed run it also
        /// raises <see cref="BeginWaveRequested"/> so Wave 1 starts.
        /// </summary>
        private void Finish(bool completed)
        {
            using var _ = FlowTrace.Enter("Onboarding", $"OnboardingFlow.Finish (completed={completed})");
            if (!_running) { FlowTrace.Step("Onboarding", "Finish: not running — no-op."); return; }
            _running = false;
            HasFinished = true;
            // WO-1012 P1: a guide one-liner mid-narration must not outlive the flow.
            GuideLineUi.Hide();
            // UIF: release the arbiter slot as the coach-marks close (no-op if already swapped out).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);

            // ── THE FIX (audit P0-11) ────────────────────────────────────────
            // Persist Onboarded = true through the Core save layer. FinishOnboarding
            // sets the flag, raises PlayerChanged and writes PlayerPrefs. This is
            // the single line that was missing from "normal play" — without it
            // Onboarded stayed false forever and StoryIntroController re-played
            // the cold open on every launch.
            var svc = GameStateService.Instance;
            if (svc != null)
            {
                svc.FinishOnboarding();
                // V — confirm the flag actually flipped; if not, the cold open replays
                // every launch (the P0-11 bug). Fail-loud so a stuck flag self-reports.
                bool onboarded = svc.State != null && svc.State.Onboarded;
                if (onboarded)
                    FlowTrace.Step("Onboarding", "Finish: FinishOnboarding ran — Onboarded=true persisted (cold-open replay fixed).");
                else
                    FlowTrace.Fail("Onboarding",
                        "Finish: FinishOnboarding ran but State.Onboarded is STILL false — the flag did not flip; cold open will replay.");
            }
            else
            {
                FlowTrace.Warn("Onboarding",
                    "Finish: No GameStateService — Onboarded was NOT persisted. The tutorial / cold open may replay next launch.");
            }

            // A completed run flows straight into Wave 1; a skip just closes.
            if (completed)
                BeginWaveRequested?.Invoke();

            FadeOverlay(1f, 0f).ContinueWith(() =>
            {
                SetOverlayVisible(false);
                TutorialClosed?.Invoke();
            }).Forget();
        }

        // =====================================================================
        //  Overlay visibility + fade
        // =====================================================================

        private void SetOverlayVisible(bool visible)
        {
            if (_canvas != null) _canvas.SetActive(visible);
            if (_group != null) _group.blocksRaycasts = visible;
        }

        /// <summary>Fades the whole overlay's CanvasGroup alpha. Never <c>async void</c>.</summary>
        private async UniTask FadeOverlay(float from, float to)
        {
            if (_group == null) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            if (_fadeSeconds <= 0f)
            {
                _group.alpha = to;
                return;
            }

            var elapsed = 0f;
            try
            {
                while (elapsed < _fadeSeconds)
                {
                    token.ThrowIfCancellationRequested();
                    _group.alpha = Mathf.Lerp(from, to, elapsed / _fadeSeconds);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.deltaTime;
                }
                _group.alpha = to;
            }
            catch (OperationCanceledException)
            {
                // Disabled / superseded mid-fade — snap to the target so the
                // overlay is never left at a half-faded alpha.
                if (_group != null) _group.alpha = to;
            }
        }
    }
}

// =============================================================================
// INTEGRATOR NOTES — wiring the first-run tutorial into the Village scene.
// -----------------------------------------------------------------------------
// DeNelle.Onboarding deliberately cannot see DeNelle.Village / DeNelle.HUD, so
// the village scene builder / VillageController owns every connection below:
//
//   1. WO-C (2026-07-03): the coach-marks are code-built uGUI on the Blink
//      Obsidian kit now — this component builds its OWN ScreenSpaceOverlay canvas
//      (sort 31000, above the HUD) at runtime. The Village scene builder still
//      adds a legacy UIDocument (TutorialOverlay.uxml) to the GameObject for
//      backward compat; Awake() DISABLES it. No PanelSettings / UXML is needed at
//      runtime, so nothing scene-hosted can blank the FTUE (CLAUDE.md §8).
//
//   2. Run gate: leave _runOnStart = true — OnboardingFlow.Start() checks
//      GameState.Onboarded itself and shows the tutorial only on a first run.
//
//   3. Wire the tutorial's requests TO gameplay:
//        flow.OpenBuildMenuRequested.AddListener(buildMenu.Open);
//        flow.BeginWaveRequested.AddListener(() => waveManager.BeginLoop().Forget());
//        flow.TutorialClosed.AddListener(villageController.OnOnboardingClosed);
//      (WaveManager.BeginLoop() starts the wave loop at its _startWave — keep
//      that at 1; BeginLoop returns a UniTask, hence the .Forget().)
//
//   4. Wire gameplay events TO the tutorial so action-beats auto-advance:
//        buildMenu.BuildingPlaced += (_, _) => flow.NotifyTowerBuilt();
//        petDeployer.PetPlaced    += () => flow.NotifyPetPlaced();
//      (NotifyTowerBuilt / NotifyPetPlaced are safe no-ops off-beat — wire them
//      unconditionally. BuildMenu.BuildingPlaced already exists; PetDeployer
//      needs a similar placed-event or call NotifyPetPlaced from its placement
//      path.)
//
//   5. Hold Wave 1 until the tutorial closes — do NOT auto-call
//      WaveManager.BeginLoop() in the village scene's Start() for a first run;
//      let BeginWaveRequested be the sole kickoff so a first-run player is
//      taught before the dark arrives. A returning player (Onboarded already
//      true) gets TutorialClosed raised immediately by TryRun(), so the
//      integrator can start the loop from that listener instead.
//
// THE COLD-OPEN FIX: no StoryIntroController change is needed. Its
// ShouldAutoPlay already returns `!Onboarded`; it re-played only because the
// flag never flipped. OnboardingFlow.Finish() calls
// GameStateService.FinishOnboarding() on completion OR skip, which persists
// Onboarded = true — after the first run the cold open is correctly skipped.
//
// See docs/port-notes/onboarding.md.
// =============================================================================
