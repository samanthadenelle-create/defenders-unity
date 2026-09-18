// =============================================================================
// PauseController — the pause overlay + Time.timeScale handling (audit P0-10).
// -----------------------------------------------------------------------------
// WO-F conversion (2026-07-03, coverage matrix row #47b): the UXML overlay
// (PauseOverlay.uxml — canon §8: UXML does not render in builds) is RETIRED.
// The overlay is now a code-built kit modal (FrameOptions, narrow portrait):
// Resume / Settings / Quit to Title; the chrome's shared Close = Resume.
//
// WHAT IT DOES (unchanged):
//   * Pause via the HUD PAUSE/BACK button through Core PauseGate
//     (PauseToggleRequested when no modal is open) + public TogglePause().
//   * On pause: the world freezes (wave timers, the ATB tick, enemy movement) + show.
//     On resume: the captured pre-pause timeScale is restored.
//     ⚠ WO-1149: the freeze is no longer PERFORMED here. Time.timeScale has exactly one
//     owner now — DeNelle.Core.UI.WorldHold — and this controller is a client that takes
//     a named, reference-counted hold. The move was forced by the money path: the code
//     that charges the player (DeNelle.Wallet) must be able to stop the world during a
//     transaction and cannot reference this assembly. Behaviour for the pause MENU is
//     unchanged, with one deliberate exception: Resume closes the menu but leaves the
//     world frozen while a TRANSACTION hold is still outstanding.
//   * Settings opens SettingsController over the top; the pause panel hides
//     and re-shows on SettingsClosed. Quit restores timeScale FIRST, then
//     SceneRouter.GoTitle().
//   * OnApplicationPause(true) auto-pauses (platform compliance §2.3);
//     never auto-resumes.
//
// MODULE ISOLATION unchanged: DeNelle.Settings references DeNelle.Core only.
// =============================================================================

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using DeNelle.Core;
using DeNelle.Core.UI;

namespace DeNelle.Settings
{
    /// <summary>
    /// Drives the pause overlay: PauseGate-driven toggle, <see cref="Time.timeScale"/>
    /// freeze, and the Resume / Settings / Quit menu (code-built kit modal).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class PauseController : MonoBehaviour
    {
        [Header("Settings screen")]
        [Tooltip("The settings screen the Settings button opens. Optional — if unassigned the " +
                 "Settings button is hidden. SettingsController lives in this same module.")]
        [SerializeField] private SettingsController _settings;

        [Header("Input")]
        [Tooltip("Auto-pause when the app is backgrounded (incoming call / task switch). " +
                 "Platform-compliance behaviour — recommended on for mobile builds.")]
        [SerializeField] private bool _pauseOnApplicationPause = true;

        [Header("Events")]
        [Tooltip("Raised whenever the pause state changes — argument is the new IsPaused value.")]
        public UnityEvent<bool> PauseStateChanged = new UnityEvent<bool>();

        private ElarionUiKit.ObsidianModal _modal;
        private bool _paused;

        // Single-modal arbiter handle. The pause menu is a SYSTEM modal that may open during
        // an active battle, so it registers battle-allowed (a plain gameplay panel would be
        // rejected + force-closed by the battle-lock). Close delegate = Resume; isOpen = _paused.
        private PanelHandle _panelHandle;

        // WO-1149: THE FREEZE IS NO LONGER PERFORMED HERE. Time.timeScale has exactly one owner
        // now — DeNelle.Core.UI.WorldHold — and the pause menu is a CLIENT of it, holding this
        // token while the menu is up. It moved because the money path (PackStore.Purchase, in
        // DeNelle.Wallet) must be able to stop the world during a transaction and cannot reference
        // this assembly; the alternative was a SECOND owner of Time.timeScale, which is exactly the
        // shape of the WO-1016 permanent-invisible-freeze bug. The capture-the-pre-pause-scale rule
        // and its "<= 0 means somebody else already froze it" guard moved into WorldHold verbatim,
        // so they now protect every caller instead of only this one.
        //
        // Consequence worth knowing: with a purchase hold outstanding, Resume() closes the MENU but
        // deliberately does NOT unfreeze the world — that would drop the player back into a live
        // battle in the middle of a signed transaction.
        private WorldHold.Handle _hold;

        /// <summary>True while the game is paused and the overlay is showing.</summary>
        public bool IsPaused => _paused;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void OnEnable()
        {
            // Mobile-first: the HUD's on-screen PAUSE/BACK button drives the back/pause
            // decision through the Core PauseGate — when no modal is open the gate raises
            // PauseToggleRequested (the modal-close branch lives in PauseGate/PanelManager).
            PauseGate.PauseToggleRequested -= OnPauseToggleRequested;
            PauseGate.PauseToggleRequested += OnPauseToggleRequested;

            if (_settings != null)
            {
                _settings.SettingsClosed.RemoveListener(OnSettingsClosed);
                _settings.SettingsClosed.AddListener(OnSettingsClosed);
            }
        }

        private void OnDisable()
        {
            PauseGate.PauseToggleRequested -= OnPauseToggleRequested;
            if (_settings != null) _settings.SettingsClosed.RemoveListener(OnSettingsClosed);

            // STOP: THE HOLE THE CEILING USED TO PAPER OVER (WO-1360). The pause hold is now
            // PLAYER-OWNED and no timer will ever drop it, so THIS is the net: a controller that
            // is deactivated while paused can no longer process Resume, and OnDestroy does NOT
            // fire for a merely-disabled component. Release the hold AND hide the panel together
            // so the world and the screen never disagree - an orphaned PAUSED panel over a
            // running world is the WO-1016 shape.
            if (!_paused) return;
            _paused = false;
            if (_hold != null) { _hold.Dispose(); _hold = null; }
            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(false);
            DeNelle.Core.Diagnostics.FlowTrace.Warn("Pause",
                "PauseController was DISABLED while paused. Released the player-owned pause hold " +
                "and hid the panel - a disabled controller cannot Resume, and nothing else would " +
                $"ever drop this hold. Remaining holds [{WorldHold.Describe()}].");
        }

        private void OnPauseToggleRequested() => TogglePause();

        /// <summary>
        /// Runtime wiring seam (WO-714 W8): the serialized <c>_settings</c> reference only
        /// works for scene-placed instances, but Settings/Pause are installed by
        /// <see cref="PauseHudBootstrap"/> via AddComponent — Awake/OnEnable run with the
        /// field still null. This attaches the settings screen after construction and
        /// (re)wires the SettingsClosed listener so the pause panel re-shows on Back.
        /// Call before the first Pause(); the Settings button builds only if attached.
        /// </summary>
        public void AttachSettings(SettingsController settings)
        {
            if (_settings == settings) return;
            if (_settings != null) _settings.SettingsClosed.RemoveListener(OnSettingsClosed);
            _settings = settings;
            if (_settings != null)
            {
                _settings.SettingsClosed.RemoveListener(OnSettingsClosed);
                _settings.SettingsClosed.AddListener(OnSettingsClosed);
            }
        }

        private void OnDestroy()
        {
            // Safety net: never leave the engine frozen if this object dies (scene unload, domain
            // reload) while paused. Releasing the HOLD rather than stamping the clock keeps any
            // other outstanding hold (a live transaction) intact — dying must not unfreeze the
            // world under a purchase, and must not leave it frozen under nothing.
            if (_hold != null) { _hold.Dispose(); _hold = null; }
            _paused = false;
            // Don't leak the arbiter slot if destroyed while paused (scene unload).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        /// <summary>Auto-pauses when the OS backgrounds the app — platform-compliance
        /// behaviour (audit §2.3). Only ever pauses; never auto-resumes.</summary>
        private void OnApplicationPause(bool isBackgrounded)
        {
            // A native rewarded ad backgrounds Unity on Android. The ad caller's registered
            // panel must remain the owner underneath it; opening Pause here would swap-close that
            // caller and manufacture a return to Pause/Settings when the ad closes.
            if (_pauseOnApplicationPause && PauseGate.ShouldAutoPause(isBackgrounded, _paused))
                Pause();
        }

        // =====================================================================
        //  Build (kit modal, lazy on first pause)
        // =====================================================================

        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;

            // Chrome Close = Resume. Sits above gameplay panels (31000), below
            // Settings (32000) so the settings screen opens over the top.
            //
            // STACKING FIX (owner 2026-07-19 "pause button has options stacked"): the option
            // buttons are laid out by the common VerticalLayoutGroup column (below), which cannot
            // self-overlap. The overlap was the shared frame CLOSE button (== Resume): FrameOptions
            // seats it at the bottom footer band and SeatSharedCloseInside grows it UPWARD ~132px
            // INTO the body. On the previous short modal (frac height 0.64) the Close button's top
            // reached ~content-frac 0.247 while the button column's body floor sat at 0.150 -- so the
            // Close control climbed ~0.10 of the body (~50px) into the column's lower region and
            // collided with the bottom option ("Quit to Title"). Fix, all inside this one file:
            //   (a) a TALLER modal (frac height 0.78) for comfortable vertical room, and
            //   (b) a bigger column bottomInset (0.18) so the stack floor clears the Close band --
            // guaranteeing disjoint, gapped, >=MinTouchPx bands that always fit inside the frame at
            // any screen size (verified headless: Builds/ui-capture/PauseMenu_<res>.png).
            // WO-1857: "PauseUI" is the canvas/host name (never rendered); the second argument is
            // the panel-chrome title, which this screen then hides (the header is SetActive(false)
            // below) in favour of the big gold Label further down - both carry the SAME meaning, so
            // both resolve the one settings.pause.title row.
            _modal = ElarionUiKit.BuildObsidianModal("PauseUI",
                new LocalizedText("settings.pause.title").Resolve(),
                new Vector2(0.29f, 0.12f), new Vector2(0.71f, 0.88f), Resume,
                sortingOrder: 31500,
                frameName: RpgUiCatalog.FrameOptions, medallionIcon: "settings");
            MedievalUiSkin.ApplyShell(_modal.chrome, compact: true);

            var aspect = _modal.chrome.root.GetComponent<AspectRatioFitter>();
            if (aspect == null) aspect = _modal.chrome.root.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            aspect.aspectRatio = 0.96f;

            if (_modal.chrome.layout != null && _modal.chrome.layout.header != null)
                _modal.chrome.layout.header.gameObject.SetActive(false);
            if (_modal.chrome.layout != null && _modal.chrome.layout.body != null)
                _modal.chrome.layout.body.gameObject.SetActive(false);
            if (_modal.chrome.layout != null && _modal.chrome.layout.footer != null)
                _modal.chrome.layout.footer.gameObject.SetActive(false);
            if (_modal.chrome.layout != null && _modal.chrome.layout.subHeader != null)
                _modal.chrome.layout.subHeader.gameObject.SetActive(false);

            var body = _modal.chrome.content.transform;
            var title = ElarionUiKit.Label(body,
                new LocalizedText("settings.pause.title").Resolve(), 0.79f, 0.89f,
                ElarionUi.Gold, 64, TMPro.TextAlignmentOptions.Center,
                0.16f, 0.84f, bold: true);
            ElarionUiKit.EnsureFont(title, ElarionUiKit.FontRole.Title);

            // WO-1857: the three approved pause actions are player copy and now resolve
            // settings.pause.resume / settings.title / settings.pause.quit_to_title.
            // ⚠ PauseMedievalSkinRegression.cs:17-19 asserts the three ENGLISH button literals by raw
            // source substring; it must be re-pointed at those three keys (reported by this lane, not
            // edited - Assets/Editor/Regression is off-limits here). This comment deliberately does
            // NOT quote the old literals: doing so would satisfy that substring search and leave the
            // oracle green while it is actually asserting nothing.
            var resume = ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("settings.pause.resume").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.12f, 0.605f), new Vector2(0.88f, 0.765f), Resume);
            MedievalUiSkin.ApplyButton(resume, primary: true);
            // Settings button only when a settings screen is wired — never a dead control.
            if (_settings != null)
            {
                // Reuses the existing settings.title row (the Settings screen's own name).
                var settings = ElarionUiKit.BuildObsidianButton(body,
                    new LocalizedText("settings.title").Resolve(),
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.12f, 0.43f), new Vector2(0.88f, 0.59f), OnSettingsClicked);
                MedievalUiSkin.ApplyButton(settings);
            }
            var quit = ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("settings.pause.quit_to_title").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Red,
                new Vector2(0.12f, 0.255f), new Vector2(0.88f, 0.415f), OnQuitClicked);
            MedievalUiSkin.ApplyButton(quit);

            if (_modal.chrome.close != null)
            {
                var closeRect = _modal.chrome.close.GetComponent<RectTransform>();
                closeRect.anchorMin = new Vector2(0.27f, 0.065f);
                closeRect.anchorMax = new Vector2(0.73f, 0.225f);
                closeRect.offsetMin = closeRect.offsetMax = Vector2.zero;
            }

            // Register with the single-modal arbiter (battle-allowed — a pause menu must be
            // openable mid-combat and never rejected). The back button / arbiter close = Resume.
            if (_panelHandle == null)
                _panelHandle = PanelManager.RegisterBattleAllowed("Pause", Resume, () => _paused);

            _modal.canvas.SetActive(false);   // built hidden; Pause shows it
        }

        // =====================================================================
        //  Public API — pause control (HUD pause button calls these)
        // =====================================================================

        /// <summary>Pauses if running, resumes if paused.</summary>
        public void TogglePause()
        {
            if (_paused) Resume();
            else Pause();
        }

        /// <summary>Pauses the game: captures + zeroes <see cref="Time.timeScale"/>
        /// and shows the overlay. Idempotent.</summary>
        public void Pause()
        {
            if (_paused) return;
            EnsureBuilt();

            // WO-1149: take a NAMED HOLD instead of writing the clock here. WorldHold is now the
            // single owner of Time.timeScale (see the _hold field's note). The WO-1016/P0 capture
            // guard — "capture the pre-pause scale, but NEVER capture a FROZEN one", the fix for the
            // permanent-invisible-freeze the owner hit on 2026-08-10 when the OS backgrounded the app
            // over an F8 note box — moved into WorldHold.Acquire verbatim and still applies here.
            // WO-1360: PLAYER-OWNED. A pause is an open state the human ends, not a beat the code
            // times, so no watchdog ceiling may judge it stuck. The 180s ceiling force-released
            // this hold after 507s on the owner's device (F8 seq 4679) and the world ran under a
            // screen that still said PAUSED. Release is owned by Resume/OnDisable/OnDestroy below.
            // WO-1369: the REQUIRED liveness probe. ⛔ It asks "does this controller still exist
            // and can it still process Resume", NEVER "how long has the player been paused" - an
            // eight-minute pause with a live controller is legitimate and must be untouched
            // (that is the WO-1353 regression). Resume/OnDisable/OnDestroy remain the normal
            // step-outs; this is the net for the paths none of them can reach.
            _hold = WorldHold.AcquirePlayerOwned(WorldHold.ReasonPauseMenu,
                () => this != null && isActiveAndEnabled);
            _paused = true;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Pause",
                $"PAUSE MENU -> WorldHold taken. Outstanding: [{WorldHold.Describe()}]. " +
                $"Resume will restore {WorldHold.CapturedScale:F2}.");

            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(true);
            // Announce the pause modal opened so the arbiter arms the back button + closes any prior panel.
            if (_panelHandle != null) PanelManager.NotifyOpened(_panelHandle);
            PauseStateChanged?.Invoke(true);
        }

        // WO-1149: the clock-reassert that lived here (several combat/VFX effects also write the
        // engine-global timeScale, and an unscaled cleanup can finish after Pause() and stamp 1,
        // leaving a Paused screen over live gameplay) MOVED WITH THE OWNERSHIP, into
        // WorldHold.ReassertTick — driven by WorldHold's own hidden ticker. It is not lost and it is
        // not weaker: it now guards the TRANSACTION hold as well as the pause menu, in every scene,
        // including the ones with no PauseController in them. Leaving a copy here would have been a
        // second writer racing the first for the same frame's clock.

        /// <summary>Resumes: restores the pre-pause <see cref="Time.timeScale"/> and
        /// hides the overlay (and the settings screen, if open). Idempotent.</summary>
        public void Resume()
        {
            if (!_paused) return;

            // Release THIS menu's hold. The world unfreezes only if no other hold is outstanding —
            // deliberately: resuming the menu during a signed transaction must not drop the player
            // back into a live battle (WO-1149). The restore itself, and its never-write-a-
            // non-positive-scale guard, live in WorldHold.
            _paused = false;
            if (_hold != null) { _hold.Dispose(); _hold = null; }
            DeNelle.Core.Diagnostics.FlowTrace.Step("Pause",
                $"RESUME -> timeScale {Time.timeScale:F2} (captured {WorldHold.CapturedScale:F2}); " +
                $"remaining holds [{WorldHold.Describe()}].");

            if (_settings != null && _settings.IsOpen)
                _settings.Close();

            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(false);
            // Release the arbiter slot as the pause menu closes (no-op if already swapped out).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            PauseStateChanged?.Invoke(false);
        }

        // =====================================================================
        //  Button handlers
        // =====================================================================

        /// <summary>Opens settings over the pause panel; the pause panel hides while
        /// settings is up (the game stays frozen) and re-shows on SettingsClosed.</summary>
        private void OnSettingsClicked()
        {
            if (_settings == null) return;
            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(false);
            // Relinquish the arbiter slot BEFORE opening Settings so the settings modal does not
            // swap-close the pause menu (whose close delegate is Resume — that would unfreeze the
            // game). The pause panel stays paused + hidden and re-shows on SettingsClosed.
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            _settings.Open();
        }

        /// <summary>Re-shows the pause panel when the settings screen closes (still paused).</summary>
        private void OnSettingsClosed()
        {
            if (_paused && _modal != null && _modal.canvas != null)
                _modal.canvas.SetActive(true);
        }

        /// <summary>Quits to Title. Restores <see cref="Time.timeScale"/> FIRST — the
        /// next scene must never load frozen — then routes via SceneRouter.</summary>
        private void OnQuitClicked()
        {
            // The next scene must NEVER load frozen, so this is the one path that drops EVERY hold,
            // not just the menu's own — abandoning a transaction by quitting to title is already a
            // bad outcome; abandoning it into a frozen title screen is a worse one.
            _hold = null;
            _paused = false;
            WorldHold.ForceReleaseAll("quit to title");

            if (_settings != null && _settings.IsOpen)
                _settings.Close();
            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(false);
            // Release the arbiter slot as we leave for the title (no-op if already swapped out).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);

            SceneRouter.GoTitle();
        }
    }
}

// =============================================================================
// INTEGRATOR NOTES — the pause overlay is fully code-built now.
//   1. Add PauseController to any GameObject (no UIDocument needed). The kit
//      modal builds lazily on first Pause() at sortingOrder 31500 (below the
//      Settings screen's 32000 — settings opens on top).
//   2. Settings: assign a SettingsController; if left empty the Settings button
//      never builds (no dead control).
//   3. HUD pause button: raises PauseGate.RequestBack() — when no modal is open
//      the gate raises PauseToggleRequested, handled here. REQUIRED for mobile.
//   4. Time.timeScale = 0 freezes everything reading Time.deltaTime. Systems
//      that must run while paused use Time.unscaledDeltaTime; uGUI input works
//      frozen (EventSystem is unscaled).
// =============================================================================
