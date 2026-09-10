// =============================================================================
// EchoUnlockFeedback -- unmissable in-view feedback when a new Echo is granted.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// PROBLEM (owner F8 2026-07-15): clearing every 5 waves DOES grant a new Echo
// (EchoService.OnWaveCleared -> EchoUnlocked fires, count 1->2, a 2nd wisp spawns
// and persists). But the ONLY "New Echo joined!" feedback was a label INSIDE the
// Echo Harvest panel, which is HIDDEN by default (EchoWorkforceHud, opened by the
// harvest button). During Overworld defense the owner saw NOTHING -- no banner, no
// SFX, no visible counter -- and concluded the unlock never happened.
//
// FIX (this file): a feedback surface INDEPENDENT of the hidden harvest panel,
// subscribing to EchoService.EchoUnlocked (carries the new count):
//   1. a persistent "Echoes N/M" COUNT that updates on every EchoService.Changed /
//      EchoUnlocked. WO-867 (2026-08-04): this used to be a free-floating ToastCard
//      pinned at (20,-150) top-left, which landed in the ~7 ref-px seam BETWEEN the
//      HudAreasHost Vitals band (0.800..0.985) and HeartStatus band (0.700..0.792) --
//      "Echoes 1/6 floats between the plates with a stray gold rule" in the
//      2026-08-04 Seeker review. The card is GONE; the count now rides the ONE
//      right-column Echoes chip (item 4), a real fixed-pixel band;
//   2. an unmissable CENTER banner ("A new Echo has joined Elarion!  (N/M)") on its
//      own overlay canvas -- colorblind-safe (text + a scale pop-in, not hue), and
//   3. a positive unlock SFX (GameSfx.PlayLevelUp -> the rising-chime reward burst,
//      routed through CoreServices.Audio, null-guarded);
//   4. the persistent "Echoes N/M" CHIP on the right column, docked in the one free
//      band HudAreasHost leaves between ActionRail (tops 0.420) and QueueStatus
//      (bottoms 0.530), at a FIXED MinTouchPx height. Opens the Echo roster.
//
// REUSE, not greenfield: the banner + pip are built from the ONE shared obsidian
// ElarionUiKit.ToastCard (DeNelle.Core.UI) -- the same surface GearGrantToast uses --
// and the SFX is the existing GameSfx wrapper. No new toast/banner framework.
//
// Lives on the EchoService DDOL host (installed by EchoWorkforceBootstrap next to
// EchoWorkforceHud). Sec.5: DeNelle.Village references Core only; it resolves state
// through EchoService (same assembly) + Core.UI/Core services -- no Village<->HUD ref.
// =============================================================================
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>Persistent "Echoes N/M" pip + unmissable center banner + reward SFX on
    /// <see cref="EchoService.EchoUnlocked"/>. Independent of the hidden harvest panel.</summary>
    [DisallowMultipleComponent]
    public sealed class EchoUnlockFeedback : MonoBehaviour
    {
        /// <summary>WO-867: vertical centre of this chip's band on HudAreasHost's right column.
        /// <para>⚠ THE JUSTIFICATION THAT USED TO SIT HERE WAS STALE, AND BOTH NUMBERS IT CITED
        /// ARE GONE (corrected WO-1642, 2026-09-10). It read "the ONE free band ... between
        /// ActionRail's top (0.420) and QueueStatus's bottom (0.530)". Re-read at source this
        /// session, HudAreasHost.cs authors ActionRail at 0.770..0.965 (`:130`) and QueueStatus at
        /// 0.510..0.750 (`:175`) — so neither 0.420 nor 0.530 exists, and the band this constant
        /// claims to be docked in does not either. That is the duplicated-state rot CLAUDE.md §2,
        /// §5 and §16 each describe: a number copied into a second file goes stale where it
        /// stands.</para>
        /// <para>WHAT IS TRUE TODAY, MEASURED NOT ASSUMED: the chip is a FIXED 112 ref px tall box
        /// centred on this fraction, so on the owner's 2670x1200 Seeker frame (canvas 1080x1920,
        /// match 0.5 → scale 1.243, band 139.2 device px) it occupies 0.418..0.532 of screen
        /// height — its top edge sits about <b>0.022 INSIDE QueueStatus's 0.510 bottom</b>. No
        /// pixel overlap has been observed: on Builds/device-frames/2026-09-10_0602_town.png this
        /// chip's plate measures device rows 585..658 and the attack-report chip's plate above it
        /// ends at 545. The encroachment is RECORDED, not fixed here — moving a placement the
        /// owner felt-tested (2026-07-24) is a ruling, not a comment repair.</para>
        /// <para>⛔ DO NOT WRITE A MOUNT RECT INTO THIS FILE AGAIN. The only cure for the copy is
        /// deleting it: this chip lives in DeNelle.Village and may not reference DeNelle.HUD
        /// (CLAUDE.md §5), so having it READ the band needs a shared table in
        /// DeNelle.Core.UI.HudLayoutBands — the same seam WO-1436 and WO-1464 already cut for the
        /// raid deploy bar and the move stick. That is a structural change with an owner ruling
        /// attached; it is raised, not smuggled in here.</para></summary>
        private const float EchoChipBandCentreY = 0.475f;

        /// <summary>WO-867: chip width, reference px — fits "Echoes 6/6" on one line at the kit's
        /// button label size without wrapping. Height is <see cref="ElarionUiKit.MinTouchPx"/>.</summary>
        private const float EchoChipWidthPx = 220f;

        private GameObject _pipCanvas;
        /// <summary>The right-column Echoes chip's label — carries the word AND the count
        /// ("Echoes 3/6"), so state is text-encoded and there is ONE Echoes surface (WO-867).</summary>
        private TMP_Text _chipLabel;

        // Founding-echo teaching queue (WO: wire founding-echo teaching): TRUE while the
        // founding card is DUE but deferred behind Build Mode or a menu scene. Re-evaluated
        // on the builder-close EDGE (Update) + on scene change until it can show cleanly.
        private bool _foundingPending;
        private bool _buildWasActive;   // prior-frame Build Mode state -> detect the close edge

        private void Start()
        {
            BuildPip();
            BuildPetBoxButton();     // owner 2026-07-17: "add the pet box somewhere" -> opens the Echo roster
            RefreshPip();
            var svc = EchoService.Instance;
            if (svc != null)
            {
                svc.Changed += RefreshPip;
                svc.EchoUnlocked += OnEchoUnlocked;
            }

            // HUD-LEAK GATE (owner F8 2026-07-18): this host is a DontDestroyOnLoad object
            // spawned by EchoWorkforceBootstrap on cold launch (first scene = Title), so the
            // pip + Pets button would paint over the TITLE / HeroSelect / PetSelect menus and
            // persist. EchoService stays global + ticking; only the VISUAL is scene-gated to
            // gameplay scenes. Set initial visibility from the active scene, then follow scene
            // changes.
            ApplySceneVisibility(SceneManager.GetActiveScene().name);
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            // Founding-echo teaching (WO): the wave path only raises EchoUnlocked at count>=2, so
            // the FOUNDING spirit (granted via the pet path, EchoCount==1) never got the portrait
            // card and its fragile FTUE line got watchdog-swallowed. Fire the SAME card here,
            // tutorial-INDEPENDENT (this host is installed unconditionally by EchoWorkforceBootstrap).
            EvaluateFoundingTeach();
            FlowTrace.Step("Echo", "EchoUnlockFeedback built (persistent pip + Pets pet-box button + unlock dialogue armed; scene-gated; founding teaching armed)");
        }

        private void OnDestroy()
        {
            var svc = EchoService.Instance;
            if (svc != null)
            {
                svc.Changed -= RefreshPip;
                svc.EchoUnlocked -= OnEchoUnlocked;
            }
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void Update()
        {
            // Founding-echo teaching queue. Fire on the builder-close EDGE only (not every frame)
            // so a deferred card shows on the FIRST frame the builder closes, without hammering
            // AnnounceFoundingEcho (and its SFX) if a render ever faults. Scene-entry attempts are
            // covered by OnActiveSceneChanged; the initial in-gameplay attempt by Start.
            bool buildActive = DeNelle.Core.BuildModeState.IsActive;
            if (_foundingPending && _buildWasActive && !buildActive) EvaluateFoundingTeach();
            _buildWasActive = buildActive;

            // WO-1010 D18: the Echoes chip hides while the builder owns the screen. Driven
            // from the SAME per-frame BuildModeState read above rather than a paired
            // hide/restore call, deliberately -- see ApplyChipVisibility for why.
            ApplyChipVisibility();

            // OWNER RCA 2026-08-01: the new holds (Onboarded / no-other-modal) have no edge
            // event to ride, so while the teach is pending re-evaluate at 1 Hz — the card
            // fires on the first quiet second after the tutorial closes.
            if (_foundingPending && Time.unscaledTime >= _nextFoundingPollAt)
            {
                _nextFoundingPollAt = Time.unscaledTime + 1f;
                EvaluateFoundingTeach();
            }
        }

        private float _nextFoundingPollAt;

        // REVIEW 2026-08-01 softlock cap: if the tutorial never completes (Onboarded never
        // flips) or a modal never closes, the soft holds would defer the founding card
        // FOREVER. Track when the pending card first became eligible-except-for-holds
        // (EchoCount >= 1, not yet taught); per WO-823 Phase B: after 120s of waiting --
        // or just 30s when the tutorial IS complete and only a sticky modal blocks --
        // bypass ONLY the Onboarded / AnyOpen holds and show it anyway. The HARD holds
        // (gameplay scene + not Build Mode) always apply -- the card must never paint
        // over a menu scene or the builder. -1 = clock not running.
        private const float FoundingHoldCapSeconds = 120f;
        private const float FoundingModalOnlyCapSeconds = 30f;   // Onboarded, AnyOpen is the sole soft hold
        private float _foundingHeldSince = -1f;

        // -- HUD-leak scene gate (visibility only; service stays global) -----------
        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            ApplySceneVisibility(to.name);
            // Entering a gameplay scene may make the founding teaching showable.
            EvaluateFoundingTeach();
        }

        // -- founding-echo teaching (the SAME card as echoes #2-6, decoupled from FTUE) -----
        /// <summary>Fire the founding-echo teaching card once, on the SAME portrait-card path as
        /// echoes #2-6. Defers behind Build Mode and the menu scenes; the persisted one-shot flag
        /// (set by <see cref="EchoService.AnnounceFoundingEcho"/> only AFTER the card renders) makes
        /// it idempotent, so an app-quit mid-build never consumes the teaching.</summary>
        private void EvaluateFoundingTeach()
        {
            var svc = EchoService.Instance;
            if (svc == null) return;

            // Already taught this save -> done for good; stop the pending retry loop.
            if (FoundingTaught()) { _foundingPending = false; _foundingHeldSince = -1f; return; }

            // No founding echo yet (defensive; EchoCount is >=1 via the property once a save exists).
            if (svc.EchoCount < 1) { _foundingPending = false; _foundingHeldSince = -1f; return; }

            // Hold until we can show cleanly: not over a menu scene, not behind the builder.
            // OWNER RCA 2026-08-01 (Start New "flashes to the pet screen ... a second later
            // moves along"): the card fired on the FIRST FRAME of castle entry — mid-fade,
            // under the boot storm and the tutorial coach — visible ~1s, buried, and the
            // one-shot burned. Two more holds: (a) the TUTORIAL must be complete (Onboarded —
            // canon: onboarding teaches the claim-loop first, the founding tale lands on a
            // quiet screen after); (b) no OTHER modal may be open (the card would force-close
            // it or vice versa). The pending retry loop below re-evaluates until clear.
            bool onboarded = DeNelle.Core.State.GameStateService.Instance != null
                && DeNelle.Core.State.GameStateService.Instance.State != null
                && DeNelle.Core.State.GameStateService.Instance.State.Onboarded;
            bool gameplayScene = IsGameplayScene(SceneManager.GetActiveScene().name);
            bool buildActive = DeNelle.Core.BuildModeState.IsActive;
            bool anyOpen = DeNelle.Core.UI.PanelManager.AnyOpen;
            if (!gameplayScene || buildActive || !onboarded || anyOpen)
            {
                // Start the softlock-cap clock the first time the card is held (EchoCount>=1
                // and untaught are already proven above).
                if (_foundingHeldSince < 0f) _foundingHeldSince = Time.unscaledTime;

                // Cap expired + the HARD holds are clear -> bypass the SOFT holds
                // (Onboarded / AnyOpen) and force the show below. A tutorial softlock must
                // not eat the founding tale forever. Still hard-held (menu scene / builder
                // open) -> keep waiting regardless of the clock.
                float held = Time.unscaledTime - _foundingHeldSince;
                // WO-823 Phase B: 120s general cap; fast 30s cap when the tutorial is done
                // and a sticky modal is the ONLY thing holding the card.
                float cap = (onboarded && anyOpen) ? FoundingModalOnlyCapSeconds : FoundingHoldCapSeconds;
                bool capExpired = held > cap;
                if (!capExpired || !gameplayScene || buildActive)
                {
                    _foundingPending = true;   // Update() + OnActiveSceneChanged re-evaluate until clear
                    return;
                }
                // OWNER F8 seq 627 (2026-08-02): this fired as an ERROR ticket after a 170s
                // hold (Onboarded=False - she was still exploring pre-tutorial). But the cap
                // EXPIRING IS THE DESIGNED BEHAVIOUR (WO-823 Phase B): the valve exists so a
                // slow/absent tutorial can never eat the founding tale. Reporting a working
                // fallback through FlowTrace.Fail raises a false error ticket in the F8
                // harness and trains us to ignore real ones. Warn keeps the full trace line
                // (still captured in break-log/Player.log) without crying wolf.
                FlowTrace.Warn("Echo", "founding card held " + (int)held + "s > cap " + (int)cap
                    + "s (Onboarded=" + onboarded + ", AnyOpen=" + anyOpen + ") - forcing show "
                    + "(designed WO-823 safety valve, not a failure)");
            }

            // Clear to teach: AnnounceFoundingEcho raises EchoUnlocked(1) -> OnEchoUnlocked renders
            // the same card as echoes #2-6, and persists the one-shot flag ONLY on a confirmed render.
            svc.AnnounceFoundingEcho();
            _foundingPending = !FoundingTaught();   // if the card failed to render, keep retrying
            if (!_foundingPending) _foundingHeldSince = -1f;   // taught -> stop the cap clock
        }

        /// <summary>Read the persisted founding-taught one-shot flag (the idempotency gate).</summary>
        private static bool FoundingTaught()
        {
            var s = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return s != null && s.SeenTutorials.TryGetValue(EchoService.FoundingTaughtKey, out bool seen) && seen;
        }

        /// <summary>TRUE while the active scene is a gameplay scene (the scene half of the
        /// chip's visibility rule). Cached so the per-frame build-mode half does not have to
        /// re-read the scene name every frame.</summary>
        private bool _sceneAllowsChip;

        /// <summary>Show the pip + Pets button only in gameplay scenes; hide them on the
        /// menu / front-door scenes so gameplay HUD chrome never leaks onto the title.</summary>
        private void ApplySceneVisibility(string sceneName)
        {
            _sceneAllowsChip = IsGameplayScene(sceneName);
            // WO-1436: the REASON is printed, not just the verdict. The old line said only
            // "gate=allows chip for scene 'RaidBase_raider_camp_small'" - true, and useless
            // for telling a deliberate allow from an unasked question.
            string why = _sceneAllowsChip
                ? "gameplay scene, town HUD permitted here"
                : (DeNelle.Core.HubScenes.SuppressTownHud(sceneName)
                    ? "enemy-owned ground (HubScenes.SuppressTownHud) - town chrome stands down"
                    : "menu/front-end scene");
            FlowTrace.Step("Echo", $"EchoUnlockFeedback scene gate={(_sceneAllowsChip ? "allows" : "blocks")} " +
                                   $"chip for scene '{sceneName ?? "<null>"}' - {why}");
            ApplyChipVisibility();
        }

        // =====================================================================
        // WO-1010 D18 (owner 2026-08-08, prompted "where is the value of adding echos on
        // this screen" -- answer: there is none): the Echoes chip is town-HUD carryover and
        // is HIDDEN for the whole of build mode. Nothing in the builder acts on the Echo
        // count (Echo awakening is not reachable there, and the Echo-gated extra build slot
        // explains itself on the Manage screen), while the chip's right-edge band is exactly
        // where the lean placement rail now lives. This also dissolves D7 -- with the chip
        // gone there is no reserved zone to negotiate; the right edge belongs to the rail.
        //
        // MECHANISM -- reused, not invented. This is the same shape the rest of the HUD uses
        // to yield to the builder: POLL the Core seam DeNelle.Core.BuildModeState.IsActive
        // (Village writes it in BuildModeController.Enter/Exit/OnDestroy; HUD + Village read
        // it) and drive the surface from that, exactly as DeNelle.HUD.DialogueView does each
        // frame for the dialogue plate. This file ALREADY polled that seam in Update for the
        // founding-teach hold, so the read is free.
        //
        // WHY POLLED AND NOT A PAIRED HIDE/RESTORE CALL: a restore that has to be *called* is
        // the part that breaks silently -- one exit route that forgets it leaves the chip gone
        // FOREVER, which is worse than the chip being in the wrong place. Deriving visibility
        // from the live flag every frame means EVERY exit route restores it by construction:
        // the compact Done button and any other Exit() path (BuildModeController.cs:567), a
        // controller torn down by a scene swap (:412, which clears the flag precisely so it
        // cannot stick), and a domain-reload-free Play session (BuildModeState.ResetStatics).
        // There is no code path that can hide this chip without also being the code path that
        // shows it again.
        // =====================================================================
        /// <summary>Apply the chip's full visibility rule: a gameplay scene AND the builder
        /// closed. Idempotent and cheap -- SetActive only fires on a real edge.</summary>
        private void ApplyChipVisibility()
        {
            if (_pipCanvas == null) return;
            bool show = _sceneAllowsChip && !DeNelle.Core.BuildModeState.IsActive;
            if (_pipCanvas.activeSelf == show) return;      // no edge -> no work, no log spam
            _pipCanvas.SetActive(show);
            FlowTrace.Step("Echo", $"EchoUnlockFeedback pip/Pets visibility={(show ? "SHOWN" : "hidden")} (gameplayScene={_sceneAllowsChip}, buildMode={DeNelle.Core.BuildModeState.IsActive})");
        }

        /// <summary>FALSE for the menu / non-HUD scenes (Title, HeroSelect, PetSelect,
        /// ATBBattle) AND for enemy-owned ground, TRUE for everything else.
        ///
        /// <para>WO-1436. The deny-list half is unchanged and still right: a small list of
        /// MENU scenes (not an allow-list) so a gameplay scene baked tomorrow defaults to
        /// showing the pip. But "not a menu" was doing double duty as "is a place the town
        /// HUD belongs", and those are different questions. A raid base is not a menu, so the
        /// chip said yes:</para>
        /// <code>[Flow:Echo] EchoUnlockFeedback scene gate=allows chip for scene 'RaidBase_raider_camp_small'</code>
        /// <para>...and <c>Echoes 4/6</c> rendered over a battlefield during the owner's
        /// felt-test. Echoes are awakened at the Heart and assigned to harvest lanes; there is
        /// nothing to act on mid-assault, which is the same argument WO-1010 D18 already used
        /// to hide this chip for the whole of build mode.</para>
        ///
        /// <para>The second half REUSES <see cref="DeNelle.Core.HubScenes.SuppressTownHud"/>
        /// -- the WO-550 town-HUD chokepoint, already gating ~14 panel bootstraps -- rather
        /// than adding "RaidBase" to the deny-list. Typing a scene prefix here would be a
        /// third private copy of scene-family naming, which is the drift HubScenes exists to
        /// end (WO-411/920), and it would miss Village2 as a raid target.</para></summary>
        private static bool IsGameplayScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            switch (sceneName)
            {
                case "Title":
                case "HeroSelect":
                case "PetSelect":
                case "ATBBattle":
                    return false;
            }
            return !DeNelle.Core.HubScenes.SuppressTownHud(sceneName);
        }

        // -- the unlock moment (event -> portrait dialogue + SFX + pip) ------------
        private void OnEchoUnlocked(int newCount)
        {
            // Guard so a missing font/clip logs + skips -- never blocks the unlock itself.
            Guard.Try("Echo", "unlock-feedback", () =>
            {
                // The full "Echoes of Elarion" portrait card, data-driven by the unlocked
                // spirit (REPLACES the old plain center banner -- no double banner). The SFX
                // + persistent pip stay.
                EchoUnlockDialogue.Show(EchoRosterCatalog.ByCount(newCount), newCount);
                GameSfx.PlayLevelUp();   // rising-chime reward burst via CoreServices.Audio (guarded)
                RefreshPip();
                FlowTrace.Step("Echo", $"unlock feedback shown count={newCount}");
            });
        }

        // -- persistent count (logic -> view) -------------------------------------
        // WO-867: the count no longer has a surface of its own. It is written onto the
        // ONE right-column Echoes chip (see BuildPetBoxButton), so there is a single
        // Echoes entry point instead of a floating card plus a separate button.
        private void RefreshPip()
        {
            var svc = EchoService.Instance;
            if (svc == null || _chipLabel == null) return;
            _chipLabel.text = $"Echoes {svc.EchoCount}/{svc.MaxEchoes}";   // ASCII only
        }

        // WO-867 — "Echoes 1/6" WAS A FLOATER.
        // Measured from the device captures (docs/ui-review/2026-08-04-seeker/
        // 03-town.png + 06-combat-hud.png) and from the two owning layouts:
        //   • the old pip was an ElarionUiKit.ToastCard pinned at (20, -150), 230x56,
        //     on THIS canvas -- i.e. 150..206 ref px from the top of the screen;
        //   • HudAreasHost puts Vitals at 0.800..0.985 y (its bottom lands ~196 ref px
        //     from the top at 2340x1080) and HeartStatus at 0.700..0.792 (its top lands
        //     ~203 ref px from the top).
        // So the card sat in the ~7 px seam BETWEEN the hero plate and the Heart plate,
        // in no band at all, and its ToastCard `accentLeft: true` strip is the "stray
        // gold rule" the review flagged. The card is retired: the count now rides the
        // Echoes chip, which is a real fixed-pixel band on the right column.
        private void BuildPip()
        {
            var go = new GameObject("EchoCountPip");
            go.transform.SetParent(transform, false);
            _pipCanvas = go;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 700;   // above gameplay HUD, below the toasts (720) + panels

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        // -- Pet-box entry point (owner 2026-07-17: "add the pet box somewhere") ---
        // A small persistent "Echoes" HUD button under the count pip that opens the Echo
        // roster grid. Colorblind-safe (a paw glyph + the word "Echoes", never hue). Lives
        // on the pip's own overlay canvas so it needs no HUD-kit area wiring (the town
        // HUD button row was pruned) -- lightweight + self-contained.
        private void BuildPetBoxButton()
        {
            if (_pipCanvas == null) return;

            // The pip canvas is display-only (ToastCard is non-interactive); a button on it
            // needs a raycaster + an EventSystem in the scene.
            if (_pipCanvas.GetComponent<GraphicRaycaster>() == null)
                _pipCanvas.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            // Presentation-separation law (MVVM): the button is a DUMB, kit-styled view.
            // It comes from the presentation kit's Obsidian button factory -- frame, face,
            // label ink, font, and hover feedback all live in the kit -- and the ONLY thing
            // this call site injects is the onClick Action (EchoRoster.Open). No hand-rolled
            // GameObject/Image/Button assembly, no per-caller styling. Style1/Gray = the
            // quiet obsidian face standardized across the HUD (matches ObsidianCloseButton).
            var btn = ElarionUiKit.BuildObsidianButton(
                _pipCanvas.transform, "Echoes 1/6",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                onClick: () =>
                {
                    FlowTrace.Step("Echo", "Pets pet-box button tapped -> open Echo roster.");
                    EchoRoster.Open();
                });
            if (btn == null) return;
            // Retire the last silver/grey HUD plate while preserving the shared Button
            // behavior. Text/state remain data-bound on an empty medieval face.
            var chipFace = btn.GetComponent<Image>();
            var medievalFace = Resources.Load<Sprite>("UI/ElarionMedieval/buttons/button-normal-empty");
            if (chipFace != null && medievalFace != null)
            {
                chipFace.sprite = medievalFace;
                chipFace.type = Image.Type.Simple;
                chipFace.color = Color.white;
            }
            // WO-867: the chip is the ONE Echoes surface — word + count on one face, so the
            // count is text-encoded and no longer needs a card of its own. RefreshPip rewrites it.
            _chipLabel = btn.GetComponentInChildren<TMP_Text>();
            if (_chipLabel != null)
            {
                _chipLabel.color = ElarionUi.Parchment;
                _chipLabel.fontStyle |= FontStyles.Bold;
                ElarionUiKit.FitSingleLine(_chipLabel, ElarionUiKit.FontFloor, ElarionUi.FontLabel);
            }
            if (_chipLabel == null)
                FlowTrace.Warn("Echo", "Echoes chip: no TMP label — the Echo count will not render.");

            // Owner 2026-07-24 felt-test placement, preserved: RIGHT screen edge, vertically
            // centred (the LEFT edge is the HudKit gear slide-dock, so RIGHT is the free edge).
            // A square touch target that meets the mobile MinTouchPx ~112 standard. The roster it
            // opens stays the full-screen 31000 single-arbiter modal (z-fix preserved). The kit
            // anchored the button at (1,0.5) with zero offsets; collapse that to a fixed-size box
            // pinned to the right edge (pivot 1,0.5 + inset), then clamp to the touch floor.
            var rt = btn.transform as RectTransform;
            if (rt != null)
            {
                // WO-867 — DOCK IT IN A REAL BAND ON THE RIGHT COLUMN.
                // HudAreasHost reserves 0.780..0.995 x for the right column. Anchor the chip on
                // EchoChipBandCentreY at a FIXED 112-px height, so it occupies 0.418..0.532.
                // Fixed pixels, never a fraction of parent.
                // ⚠ The two mount fractions this comment used to quote as the band's edges no
                // longer exist — read the correction (and the live 0.510 / 0.770 values) on
                // EchoChipBandCentreY above rather than restating any of them here.
                rt.anchorMin = new Vector2(1f, EchoChipBandCentreY);
                rt.anchorMax = new Vector2(1f, EchoChipBandCentreY);
                rt.pivot = new Vector2(1f, 0.5f);
                // SAFE-AREA INSET (measured off the headless capture 2026-07-30): the old raw
                // -16f resolved to only ~18 device px at 2340x1080 (~7 dp, ~1.15mm on the Seeker)
                // -- reads as flush, and sits inside the rounded-corner / landscape-cutout /
                // gesture band. 3 x PadPanel = 54 ref px ~= 24 dp (~60 device px), 1.5x the
                // Material 16 dp screen margin, using the dp scale in
                // docs/SME/VISUAL_TOUCH_CONTRAST_AUDIT_2026-07-14.md (1 dp ~= 2.21 ref px on
                // this 1080x1920 / match-0.5 canvas). Authored as a deliberate multiple of
                // PadPanel, never a raw literal (WO-779 spacing rule).
                // TODO(WO-779 s5.6): replace with the shared Screen.safeArea helper once it exists.
                rt.anchoredPosition = new Vector2(-(ElarionUi.PadPanel * 3f), 0f);  // 54 ref px right-edge inset
                // "Echoes 6/6" needs a wider face than the old square; height stays AT the touch
                // floor so ClampMinTouch has nothing to grow (the growth is what pushed WO-868's
                // corner button off-screen).
                rt.sizeDelta = new Vector2(EchoChipWidthPx, ElarionUiKit.MinTouchPx);
            }
            ElarionUiKit.ClampMinTouch(btn);                   // kit touch floor guard (never shrinks)
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                DontDestroyOnLoad(es);
            }
        }
    }
}
