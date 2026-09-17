// =============================================================================
// BattlePlansReveal -- WO-1804: the full-screen reveal for BOTH new plans kinds,
// with ONE call-to-action.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Built in the SpirePlansCelebration idiom, which is itself the StoryIntroController
// cold-open idiom: its own ElarionUiKit.BuildModalCanvas ScreenSpaceOverlay, CanvasGroup
// fades, kit-typography TMP line label, beats ONE AT A TIME over a dark backdrop,
// tap-anywhere to advance behind a short grace window, and an Obsidian Skip button whose
// GameObject is named "CloseButton" (the one shared Close convention).
//
// NO WORLD ACTOR, NO SUMMONED GUIDE BODY (memory tutorial-guide-body-one-time-then-images;
// owner 2026-08-16, verbatim: "the wolf was a one time idea to make it tangible, then rest
// can just be the tutorial image" / "i dont need to see it, can be a dialogue scre[e]n
// right like the introduction screen"). This is a SCREEN. The speaker is Aldwin, Echo #1,
// read from EchoRosterCatalog -- there is NO Echo name literal anywhere in this file.
//
// SpirePlansCelebration is NOT reused and NOT edited: its Show() is hard-gated on its own
// once-ever key, its beats are the spire's words, and it carries a complimentary-repair
// button that belongs to that moment only. Same PATTERN, separate controller -- the exact
// reason WO-1104 did not reuse StoryIntroController either.
//
// PURELY PRESENTATIONAL: the HELD flag is ALREADY committed by
// BattlePlansPickup.TryCollect before this screen exists. Skipping this screen -- or failing
// to build it -- can never cost the player the plans or the unlock.
//
// ⛔ AND IT IS A DUMB SKIN. THIS FILE READS NO GAME STATE. Every read (the once-ever flag,
// the live army roster, whether a Barracks stands, where the player is standing, the camp
// row, the spoils projection, the Echo roster) lives in BattlePlansRevealVM; this View binds
// that ViewModel, renders it, and routes the tap as a COMMAND (vm.InvokeCta / vm.MarkSeen).
// The first cut reached for the save-state singleton directly and
// UiMvvmConformanceRegression FAILED it as a NEW state-reading View -- correctly. Not one of
// that oracle's banned symbols is named anywhere in this file now, so it passes the
// BANNED-SYMBOL half of the rule and not merely the VM-routing half. That distinction is the
// point: the oracle's routing check is FILE-LEVEL by its own admission, so adding the token
// and leaving the reads in place would have turned the gate green while the defect stayed.
//
// ⚠ WHICH IS ALSO WHY THIS COMMENT DOES NOT SPELL THOSE SYMBOLS. The oracle is a plain
// source scan with no comment or string stripping, so prose naming a banned type re-flags the
// very file it is vouching for -- the identical trap that made case 8's own lint match
// the raid-route call's PARENTHESISED form rather than the bare word. Describe a banned
// symbol; never type it -- and that rule binds this very paragraph, which is why the call
// shape is described here instead of quoted. (Cost three iterations to learn: prose about a
// lint is scanned by that lint.)
//
// ONE CTA, NOT A MENU (owner: "click here to you build your barracks and let's step into
// that"). The branch is BattlePlans.ResolveCta, a pure function of whether a Barracks stands
// AND whether this is a hub, so the regression pins it without a scene. Offering both faces
// would reproduce the WO-1542 shape: a locked word under a lit door.
//
// WHERE IT PLAYS (owner ruling 2026-09-16, verbatim: "keep the reveal in the boss room"): the
// camp beat is a town beat; the BASTION beat plays AT THE BOSS. So the CTA is scene-aware and
// NEVER loads a scene itself -- in a dungeon it sets BattlePlans' two latches, the dungeon's
// own exit confirm carries the player home through DungeonController.ExitToVillage (which
// BANKS the run), and the grid opens on the first hub frame. ⛔ No SceneRouter.GoRaid is
// reachable from this file, by construction: grep it and there is none.
//
// Instrumented per section 12: shown (with the beat count and the CTA branch), every beat
// advance, skip, the CTA tap, the hand-off, and a Warn on any path where the screen could
// not be built or could not be opened. No silent failure.
//
// ASCII only (a non-ASCII glyph renders as tofu on device); no meaning carried by colour.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// The WO-1804 reveal screen. One instance at a time, once ever per kind.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattlePlansReveal : MonoBehaviour
    {
        /// <summary>Top-band modal, the same band SpirePlansCelebration and the WO-987 dungeon
        /// exit confirm use, so the arbiter orders them consistently.</summary>
        private const int SortingOrder = 34000;

        private const float FadeSeconds = 0.4f;
        private const float LineFadeSeconds = 0.35f;
        /// <summary>Tap-anywhere is ignored for this long after the screen appears, so the
        /// walk-over tap that collected the plans cannot instantly skip the moment. The Skip
        /// face is never gated.</summary>
        private const float PointerGraceSeconds = 1.25f;
        /// <summary>A battle-locked arbiter can REJECT the open. Rather than lose the moment,
        /// retry on this cadence for up to <see cref="OpenRetryMaxSeconds"/>.</summary>
        private const float OpenRetrySeconds = 1.0f;
        private const float OpenRetryMaxSeconds = 60f;

        private static BattlePlansReveal s_live;

        private BattlePlansKind _kind;
        /// <summary>The ONE source of everything this screen shows. Composed in Play(), disposed
        /// in Teardown(); the View never reaches past it for a value.</summary>
        private BattlePlansRevealVM _vm;

        private GameObject _canvas;
        private CanvasGroup _rootGroup;
        private TextMeshProUGUI _lineLabel;
        private CanvasGroup _lineGroup;
        private TextMeshProUGUI _speakerLabel;
        private TextMeshProUGUI _ctaLabel;
        private Image _portrait;
        private CanvasGroup _portraitGroup;

        private bool _open;
        private bool _advanceRequested;
        private bool _skipRequested;
        private bool _ctaRequested;
        private float _shownAt;
        private PanelHandle _handle;
        private CancellationTokenSource _cts;

        // =====================================================================
        //  ONCE-EVER -- delegated, so the persisted flag lives in the ViewModel
        // =====================================================================

        /// <summary>True once this kind's reveal has played. Straight delegation to
        /// <see cref="BattlePlansRevealVM.HasBeenSeen"/>: the scheduler and the regression both
        /// already call this name, and the FLAG itself is the ViewModel's business.</summary>
        public static bool HasBeenSeen(BattlePlansKind kind)
            => BattlePlansRevealVM.HasBeenSeen(kind);

        // =====================================================================
        //  SHOW
        // =====================================================================

        /// <summary>
        /// Play the moment for <paramref name="kind"/>, if it has never played. Safe to call
        /// repeatedly (the scan does, every second, until it opens). Returns the live instance
        /// when a screen was started, else null.
        /// </summary>
        public static BattlePlansReveal Show(BattlePlansKind kind)
        {
            if (HasBeenSeen(kind))
            {
                FlowTrace.Step(BattlePlans.Sys,
                    "plans reveal SKIPPED kind=" + kind + ": once-ever flag '" +
                    BattlePlans.RevealSeenKeyFor(kind) + "' already set");
                return null;
            }
            if (s_live != null)
            {
                FlowTrace.Step(BattlePlans.Sys,
                    "plans reveal already on screen (kind=" + s_live._kind + ") -- kind=" + kind +
                    " waits for the next scan");
                return s_live;
            }

            var go = new GameObject("BattlePlansReveal_" + kind);
            var screen = go.AddComponent<BattlePlansReveal>();
            screen._kind = kind;
            s_live = screen;
            screen.Play().Forget();
            return screen;
        }

        // =====================================================================
        //  PLAY
        // =====================================================================

        private async UniTaskVoid Play()
        {
            using var _ = FlowTrace.Enter(BattlePlans.Sys, "BattlePlansReveal.Play kind=" + _kind);

            // ONE compose, ONE read of the world. The active scene NAME is the only thing the
            // View is allowed to notice, and it is handed straight to the VM rather than
            // interpreted here -- HubScenes lives on the VM side of the seam.
            _vm = BattlePlansRevealVM.CreateDefault(
                _kind, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            _vm.CloseRequested += CloseFromArbiter;
            _vm.Changed += OnVmChanged;

            IReadOnlyList<BattlePlans.Beat> beats = _vm.Beats;

            bool built = Guard.Try(BattlePlans.Sys, "build plans reveal overlay",
                () => { BuildOverlay(); return _canvas != null; }, false);
            if (!built)
            {
                FlowTrace.Warn(BattlePlans.Sys,
                    "plans reveal COULD NOT BUILD its overlay kind=" + _kind + " -- the moment is " +
                    "lost for now, but the plans are HELD and the unlock stands (presentation only). " +
                    "Seen flag NOT set, so the scan retries.");
                Teardown();
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _open = true;

            if (!await TryOpenWithArbiter(token)) { Teardown(); return; }

            // The moment counts as delivered the instant it is ON SCREEN -- so a skip (explicit,
            // and always allowed) can never make it replay later.
            _vm.MarkSeen();
            _shownAt = Time.unscaledTime;
            FlowTrace.Step(BattlePlans.Sys,
                "plans reveal SHOWN kind=" + _kind + " beats=" + beats.Count +
                " cta=" + _vm.Cta + " ctaLabel='" + _vm.CtaLabel + "' inHub=" + _vm.InHub +
                " (every value bound from BattlePlansRevealVM; this View read none) -- WO-1804");

            try
            {
                await Fade(_rootGroup, 0f, 1f, FadeSeconds, token);

                for (int i = 0; i < beats.Count; i++)
                {
                    if (_cts == null || _cts.IsCancellationRequested) break;
                    var beat = beats[i];

                    ApplyBeat(beat);
                    await Fade(_lineGroup, 0f, 1f, LineFadeSeconds, token);
                    if (beat.Speaker) await Fade(_portraitGroup, 0f, 1f, LineFadeSeconds, token);

                    await WaitBeatOrTap(beat.HoldSeconds, token);
                    FlowTrace.Step(BattlePlans.Sys,
                        "plans reveal BEAT " + (i + 1) + "/" + beats.Count + " advanced (" +
                        (_ctaRequested ? "cta" : _skipRequested ? "skip" : "tap-or-hold") + ")");

                    if (_ctaRequested || _skipRequested) break;

                    await Fade(_lineGroup, 1f, 0f, LineFadeSeconds, token);
                    if (beat.Speaker) await Fade(_portraitGroup, 1f, 0f, LineFadeSeconds, token);
                }

                await Fade(_rootGroup, 1f, 0f, FadeSeconds, token);
            }
            catch (OperationCanceledException)
            {
                // Cancelled (destroyed / arbiter close) -- fall through to teardown so the screen
                // can never be left standing over the town.
            }

            bool fire = _ctaRequested;
            FlowTrace.Step(BattlePlans.Sys,
                "plans reveal CLOSED kind=" + _kind + " (" + (fire ? "CTA tapped" : "read or skipped") + ")");
            // The door opens AFTER the overlay is gone, never before: ManageScreenPanel
            // .OpenPlacementFor Close()s first for the same reason -- a modal still standing over
            // build mode eats the placement drag. The VM is captured before Teardown disposes it.
            var command = _vm;
            Teardown();
            if (fire && command != null)
                Guard.Try(BattlePlans.Sys, "plans reveal CTA", () => command.InvokeCta());
        }

        // =====================================================================
        //  THE CTA -- ROUTED AS A COMMAND, never performed here
        // =====================================================================

        /// <summary>
        /// The View's whole job on the CTA: hand the tap to the ViewModel. Which door opens, and
        /// whether it is a direct open or the two-latch route through the dungeon's banked exit,
        /// is the VM's decision and the VM's call (BattlePlansRevealVM.InvokeCta).
        ///
        /// <para>⛔ DO NOT RE-INLINE THE DOORS HERE. A View that opens build mode or the raid grid
        /// itself is calling a service directly, which IPanelViewModel's own contract forbids in as
        /// many words, and it is what UiMvvmConformanceRegression exists to stop. Case 8's source
        /// lint also fails the gate if a raid-route call or a scene load appears in this file.</para>
        /// </summary>
        private void InvokeCta()
        {
            if (_vm == null)
            {
                FlowTrace.Fail(BattlePlans.Sys,
                    "plans reveal CTA tapped kind=" + _kind + " but the ViewModel is gone -- no door " +
                    "opened. The plans are HELD and the unlock stands; never a silent dead button.");
                return;
            }
            _vm.InvokeCta();
        }

        // =====================================================================
        //  ARBITER
        // =====================================================================

        private async UniTask<bool> TryOpenWithArbiter(CancellationToken token)
        {
            if (_handle == null)
                _handle = PanelManager.Register("BattlePlansReveal", CloseFromArbiter, () => _open);

            float waited = 0f;
            while (true)
            {
                if (PanelManager.NotifyOpened(_handle)) return true;

                if (waited >= OpenRetryMaxSeconds)
                {
                    FlowTrace.Warn(BattlePlans.Sys,
                        "plans reveal NOT SHOWN kind=" + _kind + ": PanelManager refused the open for " +
                        OpenRetryMaxSeconds.ToString("0") + "s (battle-lock?). The plans are HELD and " +
                        "the unlock stands; the seen flag was NOT set, so nothing is falsely marked " +
                        "delivered and the scan retries.");
                    return false;
                }

                FlowTrace.Once(BattlePlans.Sys, "bp-reveal-open-rejected",
                    "plans reveal open REJECTED by PanelManager (battle-lock) -- retrying every " +
                    OpenRetrySeconds.ToString("0.#") + "s until the arbiter frees up.");
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(OpenRetrySeconds), DelayType.UnscaledDeltaTime,
                                        PlayerLoopTiming.Update, token);
                }
                catch (OperationCanceledException) { return false; }
                waited += OpenRetrySeconds;
            }
        }

        private void CloseFromArbiter()
        {
            FlowTrace.Step(BattlePlans.Sys,
                "plans reveal closed BY THE ARBITER kind=" + _kind + " (back button or panel swap)");
            _skipRequested = true;
            _cts?.Cancel();
            _open = false;
        }

        // =====================================================================
        //  OVERLAY -- ALL through ElarionUiKit (the [ui-obsidian] ratchet hard-fails
        //  hand-rolled Image/Text/Canvas widgets).
        // =====================================================================

        private void BuildOverlay()
        {
            _canvas = ElarionUiKit.BuildModalCanvas("BattlePlansRevealUI", SortingOrder);
            _rootGroup = _canvas.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 0f;

            var backdrop = ElarionUiKit.AddImage(_canvas.transform, "Backdrop",
                Vector2.zero, Vector2.one, new Color(0.027f, 0.016f, 0.063f, 0.88f), rounded: false);
            var tap = backdrop.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(() =>
            {
                if (Time.unscaledTime - _shownAt < PointerGraceSeconds) return;
                _advanceRequested = true;
            });

            var portraitGo = ElarionUiKit.AddImage(_canvas.transform, "SpeakerPortrait",
                new Vector2(0.34f, 0.60f), new Vector2(0.66f, 0.90f), new Color(1f, 1f, 1f, 1f),
                rounded: false);
            _portrait = portraitGo.GetComponent<Image>();
            if (_portrait != null)
            {
                _portrait.preserveAspect = true;
                _portrait.raycastTarget = false;
                // The VM hands over a NAME (it may not name a UnityEngine type); turning it into
                // a Sprite is the View's job and the only art decision on this screen.
                var sprite = !string.IsNullOrEmpty(_vm.PortraitName)
                    ? EchoRosterCatalog.LoadPortrait(_vm.PortraitName) : null;
                _portrait.sprite = sprite;
                _portrait.enabled = sprite != null;
            }
            _portraitGroup = portraitGo.AddComponent<CanvasGroup>();
            _portraitGroup.alpha = 0f;
            _portraitGroup.blocksRaycasts = false;

            _speakerLabel = ElarionUiKit.Label(_canvas.transform, string.Empty,
                0.545f, 0.595f, new Color(0.957f, 0.941f, 1f, 0.95f), 40,
                TextAlignmentOptions.Center, 0.10f, 0.90f);
            ElarionUiKit.EnsureFont(_speakerLabel, ElarionUiKit.FontRole.Title);
            _speakerLabel.raycastTarget = false;
            _speakerLabel.gameObject.name = "SpeakerName";
            _speakerLabel.gameObject.SetActive(false);

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

            // Skip -- never gated: the plans are already held, so skipping cannot cost anything.
            var skip = ElarionUiKit.BuildObsidianButton(_canvas.transform, "Skip",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.78f, 0.925f), new Vector2(0.97f, 0.985f),
                () => { _skipRequested = true; _advanceRequested = true; });
            if (skip != null) skip.gameObject.name = "CloseButton";

            // THE ONE CTA. Green face = the affirmative family the reveal's sibling screens use;
            // the MEANING is carried by the WORDS on it, never by the hue (owner is red/green
            // colourblind), and the words name the exact next action.
            var ctaButton = ElarionUiKit.BuildObsidianButton(_canvas.transform,
                _vm.CtaLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.28f, 0.055f), new Vector2(0.72f, 0.145f),
                () =>
                {
                    if (_ctaRequested) return;
                    _ctaRequested = true;
                    _advanceRequested = true;
                    FlowTrace.Step(BattlePlans.Sys,
                        "plans reveal CTA TAPPED kind=" + _kind + " cta=" + _vm.Cta +
                        " label='" + _vm.CtaLabel + "'");
                });
            if (ctaButton != null)
            {
                ctaButton.gameObject.name = "BattlePlansCtaButton";
                _ctaLabel = ctaButton.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Stamp the grace window from BUILD as well as from show: the canvas blocks raycasts
            // before the arbiter has said yes, and an unstamped _shownAt of 0 reads as "grace
            // long over", so a stray tap in that gap would eat beat one before it was visible.
            _shownAt = Time.unscaledTime;
        }

        private void ApplyBeat(BattlePlans.Beat beat)
        {
            string speaker = _vm != null ? _vm.SpeakerName : string.Empty;
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
        //  TIMING (unscaled -- the town can be time-paused behind a modal)
        // =====================================================================

        private async UniTask Fade(CanvasGroup group, float from, float to, float seconds,
                                   CancellationToken token)
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
        //  TEARDOWN
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
            DisposeVm();
            if (s_live == this) s_live = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        /// <summary>The View re-renders when the ViewModel says data moved. Composed-once today, so
        /// this is the contract's seam rather than a live loop -- it re-applies nothing it cannot
        /// prove changed, which is why it only refreshes the CTA face.</summary>
        private void OnVmChanged()
        {
            if (_vm == null || _ctaLabel == null) return;
            _ctaLabel.text = _vm.CtaLabel;
        }

        private void DisposeVm()
        {
            if (_vm == null) return;
            _vm.CloseRequested -= CloseFromArbiter;
            _vm.Changed -= OnVmChanged;
            _vm.Dispose();
            _vm = null;
        }

        private void OnDestroy()
        {
            DisposeVm();
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
