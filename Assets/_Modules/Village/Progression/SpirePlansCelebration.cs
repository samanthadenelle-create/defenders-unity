// =============================================================================
// SpirePlansCelebration -- WO-1104 SS3+SS4: the ONE authored moment that fires
// when the player collects the Arcane Spire plans after holding three waves.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT IS (owner rulings 2026-08-16, WO-1104 sec.4a + sec.4b, BINDING):
//   * "the wolf was a one time idea to make it tangible, then rest can just be the
//     tutorial image" -- there is NO world actor here, NO summoned guide body. The
//     physical guide (the wolf = Aldwin, Echo #1) is a ONE-TIME device for the
//     opening FTUE; every LATER teaching beat is a SCREEN.
//   * "i dont need to see it, can be a dialogue scre[e]n right like the
//     introduction screen" -- so this is a full-screen dialogue beat built in the
//     COLD-OPEN idiom of Assets/_Modules/Onboarding/StoryIntroController.cs: its
//     own ElarionUiKit.BuildModalCanvas ScreenSpaceOverlay, CanvasGroup fades,
//     kit-typography TMP line label, beats ONE AT A TIME over a dark backdrop,
//     tap-anywhere to advance behind a short grace window, and an Obsidian Skip
//     button whose GameObject is named "CloseButton" (the one shared Close
//     convention).
//
// WHY NOT REUSE StoryIntroController: its Play() is gated on GameState.Onboarded
// == false (the first-launch cold open) and it lives in the TITLE scene. This beat
// fires in the hub, post-wave-3, on an ONBOARDED save. Same PATTERN, separate
// controller -- StoryIntroController is deliberately NOT touched.
//
// SPEAKER: Aldwin, Echo #1, read from EchoRosterCatalog (the authority) --
// EchoRosterCatalog.ByCount(1).DisplayName, trimmed at the comma. There is NO Echo
// name literal anywhere in this file -- not the roster name, and not the invented
// element-word name a SpeakerName() helper once hardcoded (WO-1031 is deleting it).
// SpirePlansCelebrationRegression source-lints that absence, so a "just inline it"
// edit fails the gate rather than quietly re-forking the naming authority.
//
// PURELY PRESENTATIONAL (WO-1104 sec.3, load-bearing): the unlock flag and the
// funding grant are ALREADY committed by CastleDefensePlansPickup.TryCollect before
// PlansCollected is raised. This screen reads that state and shows it; it writes
// NOTHING except its own once-ever seen flag. Skipping it -- or failing to build it
// -- can never cost the player the Spire.
//
// ONCE EVER: the seen flag lives in the SAME GameState.SeenTutorials keyed store
// the pickup's unlock flag uses (ProgressionUnlocks idiom, one-shot key +
// MarkTutorialSeen which Save()s). No second store, no schema bump. The prop itself
// deterministically re-spawns from state on scene re-entry; the CELEBRATION must
// not, so re-entry is covered by this flag AND by the fact that a collected drop
// never raises PlansCollected again.
//
// Instrumented per CLAUDE.md sec.12: [Flow:Progression] on shown (with beat count),
// every beat advance, skip, close, and a Warn on any path where the screen could
// not be built or could not be opened. No silent failure.
// =============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// Self-arming listener for <see cref="CastleDefensePlansPickup.PlansCollected"/>.
    /// The pickup header has said since WO-1013 that the beat pipeline subscribes
    /// here; until WO-1104 NOTHING did, so the plans landed in silence. This is that
    /// subscriber -- and the only one this file needs.
    /// </summary>
    internal static class SpirePlansCelebrationInstaller
    {
        private const string Sys = "Progression";
        private static bool s_hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_hooked) return;
            s_hooked = true;
            CastleDefensePlansPickup.PlansCollected += OnPlansCollected;
            FlowTrace.Step(Sys, "spire-plans celebration ARMED (subscribed to PlansCollected, WO-1104 SS3+SS4)");
        }

        private static void OnPlansCollected()
        {
            // Guard the whole presentation: a throw here must never escape into the
            // pickup's Guard.Try("plans-collected beat seam") and read as a mechanics
            // failure. The unlock + grant are already committed at this point.
            Guard.Try(Sys, "spire-plans celebration show", () => SpirePlansCelebration.Show());
        }
    }

    /// <summary>
    /// The full-screen, three-beat celebration + call-to-arms dialogue screen for the
    /// Arcane Spire plans (WO-1104). Built in the StoryIntroController cold-open idiom
    /// through <see cref="ElarionUiKit"/>. Shows ONCE, ever.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpirePlansCelebration : MonoBehaviour
    {
        private const string Sys = "Progression";

        /// <summary>
        /// The one-shot key this screen persists under, in the SAME
        /// GameState.SeenTutorials store the plans unlock flag uses (ProgressionUnlocks
        /// idiom). Never a second store.
        /// </summary>
        public const string SeenKey = "spire_plans_celebration";

        /// <summary>Top-band modal: above the town HUD, matching the WO-987 dungeon
        /// exit confirm's band so the arbiter orders them consistently.</summary>
        private const int SortingOrder = 34000;

        private const float FadeSeconds = 0.4f;
        private const float LineFadeSeconds = 0.35f;
        /// <summary>Tap-anywhere is ignored for this long after the screen appears, so
        /// the walk-over tap that collected the plans cannot instantly skip the moment
        /// (the StoryIntroController grace window). The Skip face is never gated.</summary>
        private const float PointerGraceSeconds = 1.25f;
        /// <summary>A battle-locked arbiter can REJECT the open. Rather than lose the
        /// moment silently, retry on this cadence for up to <see cref="OpenRetryMaxSeconds"/>.</summary>
        private const float OpenRetrySeconds = 1.0f;
        private const float OpenRetryMaxSeconds = 60f;

        // -- overlay (code-built uGUI through the kit; no UXML, no hand-rolled plates) --
        private GameObject _canvas;
        private CanvasGroup _rootGroup;
        private TextMeshProUGUI _lineLabel;
        private CanvasGroup _lineGroup;
        private TextMeshProUGUI _speakerLabel;
        private Image _portrait;
        private CanvasGroup _portraitGroup;

        private bool _open;
        private bool _advanceRequested;
        private bool _skipRequested;
        private float _shownAt;
        private PanelHandle _handle;
        private CancellationTokenSource _cts;

        /// <summary>True once the persisted once-ever flag is set (the screen has played).</summary>
        public static bool HasBeenSeen
        {
            get
            {
                var svc = GameStateService.Instance;
                var state = svc != null ? svc.State : null;
                if (state == null || state.SeenTutorials == null) return false;
                return state.SeenTutorials.TryGetValue(SeenKey, out bool seen) && seen;
            }
        }

        /// <summary>
        /// Play the moment, if it has never played. Safe to call more than once and
        /// safe to call with no GameStateService (it then refuses, traced). Returns the
        /// live instance when a screen was started, else null.
        /// </summary>
        public static SpirePlansCelebration Show()
        {
            if (HasBeenSeen)
            {
                FlowTrace.Step(Sys,
                    "spire-plans celebration SKIPPED: once-ever flag '" + SeenKey +
                    "' already set (scene re-entry re-spawns the prop, never the moment)");
                return null;
            }
            if (s_live != null)
            {
                FlowTrace.Step(Sys, "spire-plans celebration already on screen -- second raise ignored");
                return s_live;
            }

            var go = new GameObject("SpirePlansCelebration");
            var screen = go.AddComponent<SpirePlansCelebration>();
            s_live = screen;
            screen.Play().Forget();
            return screen;
        }

        private static SpirePlansCelebration s_live;

        // =====================================================================
        //  THE BEATS -- ASCII only (a non-ASCII glyph renders as tofu on device),
        //  and meaning never carried by colour (the owner is red/green colourblind):
        //  the emphasis axes here are SIZE, WEIGHT and POSITION.
        //
        //  Three beats, exactly as the owner framed the moment:
        //   1. the win  -- they HELD three waves,
        //   2. the earn -- the plans are theirs, the Arcane Spire is unlocked,
        //   3. Aldwin   -- hurry and raise this new defense.
        // =====================================================================
        private readonly struct Beat
        {
            public readonly string Text;
            public readonly float HoldSeconds;
            public readonly bool Emphasis;
            /// <summary>True = the Echo speaks it (portrait + name shown).</summary>
            public readonly bool Speaker;
            public Beat(string text, float hold, bool emphasis = false, bool speaker = false)
            { Text = text; HoldSeconds = hold; Emphasis = emphasis; Speaker = speaker; }
        }

        private static Beat[] BuildBeats()
        {
            return new[]
            {
                new Beat(LocalText.Get("raid.spire_plans.beat_one"), 4.6f, true),
                new Beat(LocalText.Get("raid.spire_plans.beat_two"), 6.0f),
                new Beat(LocalText.Get("raid.spire_plans.beat_three"), 6.4f, false, true),
            };
        }

        /// <summary>
        /// The speaker, read from the roster catalog -- NEVER a name literal. Echo #1 is
        /// the founding Echo (WO-1104 sec.4); the roster's DisplayName is authored as
        /// "&lt;name&gt;, the &lt;element&gt; Echo", so the bare name is everything before
        /// the first comma. An empty roster falls back to no attribution (the line still
        /// reads) with a Warn -- never a hardcoded fallback name.
        /// </summary>
        private static string ResolveSpeakerName(out EchoRosterEntry entry)
        {
            entry = EchoRosterCatalog.ByCount(1);
            if (entry == null || string.IsNullOrEmpty(entry.DisplayName))
            {
                FlowTrace.Warn(Sys,
                    "spire-plans celebration: EchoRosterCatalog.ByCount(1) gave no display name -- " +
                    "the call-to-arms beat plays unattributed (never a hardcoded name).");
                return string.Empty;
            }
            int comma = entry.DisplayName.IndexOf(',');
            return comma > 0 ? entry.DisplayName.Substring(0, comma).Trim() : entry.DisplayName.Trim();
        }

        // =====================================================================
        //  Play
        // =====================================================================
        private async UniTaskVoid Play()
        {
            using var _ = FlowTrace.Enter(Sys, "SpirePlansCelebration.Play (WO-1104 plans moment)");

            string speaker = ResolveSpeakerName(out var echo);
            Beat[] beats = BuildBeats();

            bool built = Guard.Try(Sys, "build spire-plans celebration overlay",
                () => { BuildOverlay(speaker, echo); return _canvas != null; }, false);
            if (!built)
            {
                FlowTrace.Warn(Sys,
                    "spire-plans celebration COULD NOT BUILD its overlay -- the moment is lost for this " +
                    "collection, but the unlock and the funding were already committed by TryCollect " +
                    "(presentation only). Seen flag NOT set.");
                Teardown();
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _open = true;

            if (!await TryOpenWithArbiter(token))
            {
                // TryOpenWithArbiter already emitted the Warn with the reason.
                Teardown();
                return;
            }

            // The moment counts as delivered the instant it is ON SCREEN -- so a skip
            // (explicit, and always allowed) can never make it replay later.
            MarkSeen();
            _shownAt = Time.unscaledTime;
            FlowTrace.Step(Sys,
                "spire-plans celebration SHOWN beats=" + beats.Length +
                " speaker='" + (string.IsNullOrEmpty(speaker) ? "(unattributed)" : speaker) +
                "' (roster echo #1, no world actor -- WO-1104 sec.4a/4b)");

            try
            {
                await Fade(_rootGroup, 0f, 1f, FadeSeconds, token);

                for (int i = 0; i < beats.Length; i++)
                {
                    if (_cts == null || _cts.IsCancellationRequested) break;
                    var beat = beats[i];

                    ApplyBeat(beat, speaker);
                    await Fade(_lineGroup, 0f, 1f, LineFadeSeconds, token);
                    if (beat.Speaker) await Fade(_portraitGroup, 0f, 1f, LineFadeSeconds, token);

                    await WaitBeatOrTap(beat.HoldSeconds, token);
                    FlowTrace.Step(Sys,
                        "spire-plans celebration BEAT " + (i + 1) + "/" + beats.Length +
                        " advanced (" + (_skipRequested ? "skip" : "tap-or-hold") + ")");

                    await Fade(_lineGroup, 1f, 0f, LineFadeSeconds, token);
                    if (beat.Speaker) await Fade(_portraitGroup, 1f, 0f, LineFadeSeconds, token);

                    if (_skipRequested)
                    {
                        FlowTrace.Step(Sys,
                            "spire-plans celebration SKIPPED at beat " + (i + 1) + "/" + beats.Length +
                            " (explicit Skip is never gated; unlock + funding already committed)");
                        break;
                    }
                }

                await Fade(_rootGroup, 1f, 0f, FadeSeconds, token);
            }
            catch (OperationCanceledException)
            {
                // Cancelled (destroyed / arbiter close) -- fall through to teardown so
                // the screen can never be left standing over the town.
            }

            FlowTrace.Step(Sys, "spire-plans celebration CLOSED -- control returns to the town");
            Teardown();
        }

        /// <summary>
        /// Register + NotifyOpened with <see cref="PanelManager"/>. An unregistered
        /// top-band modal is invisible to the back-button / battle-lock arbiter, which
        /// is the exact defect [modal-registration] fails the gate on. A REJECTION
        /// (battle in progress) is retried rather than silently dropped.
        /// </summary>
        private async UniTask<bool> TryOpenWithArbiter(CancellationToken token)
        {
            if (_handle == null)
                _handle = PanelManager.Register("SpirePlansCelebration", CloseFromArbiter, () => _open);

            float waited = 0f;
            while (true)
            {
                if (PanelManager.NotifyOpened(_handle)) return true;

                if (waited >= OpenRetryMaxSeconds)
                {
                    FlowTrace.Warn(Sys,
                        "spire-plans celebration NOT SHOWN: PanelManager refused the open for " +
                        OpenRetryMaxSeconds.ToString("0") + "s (battle-lock?). The unlock and funding " +
                        "stand; the seen flag was NOT set, so nothing is falsely marked delivered.");
                    return false;
                }

                FlowTrace.Once(Sys, "spire-plans-open-rejected",
                    "spire-plans celebration open REJECTED by PanelManager (battle-lock) -- " +
                    "retrying every " + OpenRetrySeconds.ToString("0.#") + "s until the arbiter frees up.");
                try { await UniTask.Delay(TimeSpan.FromSeconds(OpenRetrySeconds), DelayType.UnscaledDeltaTime,
                                          PlayerLoopTiming.Update, token); }
                catch (OperationCanceledException) { return false; }
                waited += OpenRetrySeconds;
            }
        }

        /// <summary>The arbiter's close hook (back button / a higher panel opening).</summary>
        private void CloseFromArbiter()
        {
            FlowTrace.Step(Sys, "spire-plans celebration closed BY THE ARBITER (back button or panel swap)");
            _skipRequested = true;
            _cts?.Cancel();
            _open = false;
        }

        private static void MarkSeen()
        {
            var svc = GameStateService.Instance;
            if (svc == null)
            {
                FlowTrace.Warn(Sys,
                    "spire-plans celebration seen flag DROPPED: no GameStateService -- the moment played " +
                    "but could replay on a later collection. (Presentation only; nothing gameplay-bearing.)");
                return;
            }
            svc.MarkTutorialSeen(SeenKey);   // one-shot key + Save(), the house idiom
            FlowTrace.Step(Sys, "spire-plans celebration seen flag persisted: '" + SeenKey + "' (once ever)");
        }

        // =====================================================================
        //  Overlay construction -- ALL through ElarionUiKit (the [ui-obsidian]
        //  ratchet hard-fails hand-rolled Image/Text/Canvas widgets).
        // =====================================================================
        private void BuildOverlay(string speaker, EchoRosterEntry echo)
        {
            _canvas = ElarionUiKit.BuildModalCanvas("SpirePlansCelebrationUI", SortingOrder);
            _rootGroup = _canvas.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 0f;

            // Dark backdrop -- deeper than the cold open's 0.55 because this plays over a
            // lit town, and the beat has to read as its own authored screen. Tap-anywhere
            // advances one beat, behind the grace window.
            var backdrop = ElarionUiKit.AddImage(_canvas.transform, "Backdrop",
                Vector2.zero, Vector2.one, new Color(0.027f, 0.016f, 0.063f, 0.88f), rounded: false);
            var tap = backdrop.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(() =>
            {
                if (Time.unscaledTime - _shownAt < PointerGraceSeconds) return;
                _advanceRequested = true;
            });

            // The Echo's portrait -- shown only on the beat the Echo speaks. Sprite comes
            // from the roster catalog; a missing image simply leaves the plate empty
            // (LoadPortrait already Warns), never blocks the beat.
            var portraitGo = ElarionUiKit.AddImage(_canvas.transform, "SpeakerPortrait",
                new Vector2(0.34f, 0.60f), new Vector2(0.66f, 0.90f),
                new Color(1f, 1f, 1f, 1f), rounded: false);
            _portrait = portraitGo.GetComponent<Image>();
            if (_portrait != null)
            {
                _portrait.preserveAspect = true;
                _portrait.raycastTarget = false;
                var sprite = echo != null ? EchoRosterCatalog.LoadPortrait(echo.PortraitName) : null;
                _portrait.sprite = sprite;
                _portrait.enabled = sprite != null;
            }
            _portraitGroup = portraitGo.AddComponent<CanvasGroup>();
            _portraitGroup.alpha = 0f;
            _portraitGroup.blocksRaycasts = false;

            // Speaker name plate. TEXT carries the attribution (never a colour cue).
            _speakerLabel = ElarionUiKit.Label(_canvas.transform, string.Empty,
                0.545f, 0.595f, new Color(0.957f, 0.941f, 1f, 0.95f), 40,
                TextAlignmentOptions.Center, 0.10f, 0.90f);
            ElarionUiKit.EnsureFont(_speakerLabel, ElarionUiKit.FontRole.Title);
            _speakerLabel.raycastTarget = false;
            _speakerLabel.gameObject.name = "SpeakerName";
            _speakerLabel.gameObject.SetActive(false);

            // The beat line -- kit title typography, centred band, wraps.
            _lineLabel = ElarionUiKit.Label(_canvas.transform, string.Empty,
                0.30f, 0.52f, new Color(0.957f, 0.941f, 1f, 0.92f), 44,
                TextAlignmentOptions.Center, 0.08f, 0.92f);
            ElarionUiKit.EnsureFont(_lineLabel, ElarionUiKit.FontRole.Title);
            _lineLabel.fontStyle = FontStyles.Italic;
            _lineLabel.raycastTarget = false;
            _lineLabel.gameObject.name = "BeatLine";
            _lineGroup = _lineLabel.gameObject.AddComponent<CanvasGroup>();
            _lineGroup.alpha = 0f;
            _lineGroup.blocksRaycasts = false;

            // Skip -- the Obsidian family button, top-right, GameObject named
            // "CloseButton" (the one shared Close convention). Never gated: the unlock
            // and the funding were committed before this screen existed, so skipping
            // cannot cost the player the Spire.
            var skip = ElarionUiKit.BuildObsidianButton(_canvas.transform, "Skip",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.78f, 0.925f), new Vector2(0.97f, 0.985f),
                () => { _skipRequested = true; _advanceRequested = true; });
            if (skip != null) skip.gameObject.name = "CloseButton";

            // Optional one-time safety net. This is a repair offer, not a reassignment:
            // Echo repairs remain passive/count-driven, and the entitlement is persisted
            // before the shared repair backend restores current damage for no materials.
            var repair = ElarionUiKit.BuildObsidianButton(_canvas.transform, "LET ECHOES REPAIR - FREE",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.25f, 0.055f), new Vector2(0.75f, 0.13f),
                () =>
                {
                    int repaired = EchoRepairService.ClaimComplimentaryPlansRepair();
                    if (_lineLabel != null)
                        _lineLabel.text = repaired > 0
                            ? "The Echoes restored " + repaired + " damaged structure" + (repaired == 1 ? "." : "s.")
                            : "Your structures are already fully repaired.";
                });
            if (repair != null) repair.gameObject.name = "ComplimentaryEchoRepairButton";

            // Stamp the grace window from BUILD as well as from show: the canvas exists
            // (and blocks raycasts) before the arbiter has said yes, and an unstamped
            // _shownAt of 0 reads as "grace long over" -- so a stray tap in that gap
            // would eat beat one before it was ever visible.
            _shownAt = Time.unscaledTime;
        }

        private void ApplyBeat(Beat beat, string speaker)
        {
            if (_lineLabel != null)
            {
                _lineLabel.text = beat.Text;
                _lineLabel.fontStyle = beat.Emphasis ? (FontStyles.Bold | FontStyles.Italic) : FontStyles.Italic;
                _lineLabel.fontSize = beat.Emphasis ? 56 : 44;
            }
            if (_lineGroup != null) _lineGroup.alpha = 0f;

            bool showSpeaker = beat.Speaker && !string.IsNullOrEmpty(speaker);
            if (_speakerLabel != null)
            {
                _speakerLabel.text = showSpeaker ? speaker : string.Empty;
                _speakerLabel.gameObject.SetActive(showSpeaker);
            }
            if (_portraitGroup != null) _portraitGroup.alpha = 0f;
            if (_portrait != null) _portrait.gameObject.SetActive(beat.Speaker);
        }

        // =====================================================================
        //  Timing helpers (unscaled -- the town can be time-paused behind a modal)
        // =====================================================================
        private async UniTask Fade(CanvasGroup group, float from, float to, float seconds, CancellationToken token)
        {
            if (group == null) return;
            if (seconds <= 0f) { group.alpha = to; return; }
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                token.ThrowIfCancellationRequested();
                group.alpha = Mathf.Lerp(from, to, elapsed / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.unscaledDeltaTime;
            }
            group.alpha = to;
        }

        private async UniTask WaitBeatOrTap(float holdSeconds, CancellationToken token)
        {
            float elapsed = 0f;
            while (elapsed < holdSeconds && !_advanceRequested)
            {
                token.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.unscaledDeltaTime;
            }
            _advanceRequested = false;   // consume -- one beat at a time
        }

        // =====================================================================
        //  Teardown
        // =====================================================================
        private void Teardown()
        {
            _open = false;
            if (_handle != null)
            {
                PanelManager.NotifyClosed(_handle);
                _handle = null;
            }
            if (_canvas != null)
            {
                Destroy(_canvas);
                _canvas = null;
            }
            if (s_live == this) s_live = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            if (_handle != null)
            {
                PanelManager.NotifyClosed(_handle);
                _handle = null;
            }
            if (s_live == this) s_live = null;
        }
    }
}
