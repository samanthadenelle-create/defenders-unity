// =============================================================================
// DialogueView (DeNelle.HUD) — the dumb uGUI skin for OUR dialogue (WO-455).
// -----------------------------------------------------------------------------
// Code-built uGUI (canon: NOT UIDocument), styled with ElarionUiKit so it matches
// every other panel. Binds to a DialogueViewModel and renders it: a centered reading
// panel (owner ruling 2026-07-06 — raised clear of the HUD control zones, scrollable
// body) with speaker + text (tap to advance), and an option list at a choice.
// The VIEW holds no game state — it reads the VM and calls Advance/Choose only.
// Self-bootstraps DDOL behind FeatureFlags.CustomDialogue.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using DeNelle.Core.Dialogue;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class DialogueView : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!DeNelle.Core.FeatureFlags.CustomDialogue)
            {
                // Instrumentation standard: a declined gate must trace, never skip silently —
                // with the flag off NOTHING can render a dialogue, and that must be readable.
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                    "DialogueView.Bootstrap: ff.customdialogue OFF — view not spawned (no dialogue can render).");
                return;
            }
            var go = new GameObject("DialogueView");
            DontDestroyOnLoad(go);
            go.AddComponent<DialogueView>();
        }

        private DialogueViewModel _vm;
        private GameObject _ui;
        private TMPro.TextMeshProUGUI _speaker;
        private TMPro.TextMeshProUGUI _affiliation;   // guild/shop sub-line under the name (card standard)
        private TMPro.TextMeshProUGUI _body;
        private RectTransform _box;       // the dialogue box (tap to advance)
        private RectTransform _optionsCol;
        private RectTransform _bodyWell;    // WO-1030: scrollable TEXT well host (ends above the option band)
        private RectTransform _optionsHost; // WO-1030: fixed-pixel OPTION band at the body zone bottom edge
        private GameObject _tapHint;
        private Button _close;            // the factory's shared Close — arbitrated per Repaint (F8-22)
        private string _lastActionArb;    // one FlowTrace arbitration line per state change, not per repaint
        private ElarionUiKit.PortraitHandle _portrait;   // medallion portrait disc (refreshed per Repaint)

        // ── CONTENT-FIT SIZING (owner F8 2026-07-16: short lines left a tall black void) ──
        // The kit zones (header / body / footer / medallion) are all PANEL-FRACTION anchored,
        // so we first RE-PIN them to FIXED-PIXEL bands off the panel edges (ResizeToContent),
        // then drive the PANEL height from the measured body content. These hold the zone rects
        // + the resize bookkeeping.
        private RectTransform _headerZone;
        private RectTransform _bodyZone;
        private RectTransform _footerZone;
        private RectTransform _portraitHost;
        private bool _pixelBandsApplied;
        private float _maxBodyPx = 460f;   // recomputed from the original rect height on first paint
        private float _lastPanelH = -1f;
        private bool _compactPresentation;
        private float _regularHeaderXMin;
        private float _regularHeaderXMax;

        // -- WO-1030 option-band metrics (owner screenshot 2026-08-16: 'Repair structures'
        // sliced by the panel edge). Options are NOT OPTIONAL -- an unreachable choice is a
        // dead end, so ResizeToContent reserves the option band FIRST and the text scrolls.
        private const float OptionRowGapPx = 8f;   // spacing between option rows (kit scroll column)
        private const float OptionsPadPx = 6f;     // MakeScrollZone padding (top+bottom each)
        private const float OptionsGapPx = 12f;    // gap between the text well and the option band

        // Full dialogue geometry contract. EchoEngageDialogueRegression reads these constants
        // directly to prove the standard (non-compact) surface fits every capture aspect. The
        // compact helper variant derives smaller locals in ResizeToContent, but must not erase
        // the measurable baseline contract from source.
        private const float TopPad = 18f;
        private const float HeaderPx = 108f;
        private const float Gap = 10f;
        private const float BottomMarginPx = 24f;
        // Two wrapped 30px prose lines plus the scroll well's 20px vertical padding. The old
        // 54px floor could only hold one line and visibly sliced the opening sentence whenever
        // a four-choice node reserved most of the body for touch rows.
        private const float MinBodyPx = 96f;
        private bool _reserveCloseBand;    // set per-Repaint: true only when the shared Close is visible

        // F8 2026-07-06 (t=328): the dialogue now routes through the modal arbiter
        // (mirrors RumorBoardPanel) so the click-guard classifies its TapAdvance
        // catcher as an intentional modal cover (was 7x false CLICK-BLOCKED per
        // fleet) and world prompts suppress while a conversation owns the screen.
        // Registered BATTLE-ALLOWED (WO-437): dialogue is scripted narrative
        // (tutorial intro/outro around fights, companion meetings, vendor talk) —
        // the battle-lock must never silently tear a conversation down mid-script.
        private PanelHandle _handle;
        private bool _arbiterNotified;

        /// <summary>Probe/observability surface (AutoPilot AssertDialogueChain): TRUE while a
        /// dialogue panel is built and visible (_ui alive + active). This is the P0 re-entrancy
        /// fix's testable invariant — after a Closed-callback synchronously chains into a
        /// successor dialogue, this must stay TRUE; a stale close tearing the successor's panel
        /// down (the frozen-build-mode root) flips it false.</summary>
        public bool IsShowing => _ui != null && _ui.activeSelf;

        private void OnEnable() { DialogueService.Opened += OnOpened; }
        private void OnDisable() { DialogueService.Opened -= OnOpened; }

        // P0 RE-ENTRANCY FIX (owner "still cant do the tower", RCA 2026-07-08): when a dialogue's
        // Closed invocation-list SYNCHRONOUSLY chains into the NEXT dialogue (the tutorial's
        // dialogue.ended -> STEP-ENTER -> Play), the STALE dialogue's Closed handler runs AFTER
        // this view has already rebound to the successor — it destroyed the successor's just-built
        // panel and unbound its handlers, leaving the new VM alive-but-headless: Ended never fired,
        // HeroLocomotion.InputSuppressed stayed TRUE forever, and BuildModeController.Update froze
        // at its first gate (zero PlaceConfirm evaluations — her captured session). The Closed
        // handler is now bound PER-VM and ignores any close for a VM this view no longer shows.
        private System.Action _vmClosedHandler;

        // ── WO-702 dialogue/builder truce (owner F8 2026-07-13: "pause the sylas
        // dialogue till either action asked is completed or closed builder") ─────
        // While Build Mode is open (Core seam BuildModeState.IsActive — Village writes,
        // HUD reads; never a HUD->Village reference) a live dialogue is HIDDEN, never
        // Closed: closing fires Ended and would falsely complete a dialogue-gated
        // tutorial step (the captured STEP-STUCK :: founding_town gate is
        // dialogue.ended). On builder exit the panel re-shows and the player reads it.
        // WO-744 MVVM: the truce STATE + the BuildModeState.DialogueHiddenForBuilder write are
        // RELOCATED into DialogueViewModel (VM owns HiddenForBuilder + the publish). This View
        // now only POLLS BuildModeState.IsActive each frame and forwards it via
        // _vm.SetBuilderActive, then reads _vm.HiddenForBuilder to hide/show its panel.

        // ── WO-795 dialogue/modal truce (16-panel audit: the Sylas box sat OVER the
        // Rumor Board's Accept and bled through Hot-Swap Skills / Bug Report) ─────
        // While a DIFFERENT arbiter-tracked modal owns the screen (PanelManager.AnyOpen
        // and this view is not the notified owner), a live dialogue is HIDDEN, never
        // Closed -- closing fires Ended and would falsely complete a dialogue-gated
        // tutorial step (same law as the WO-702 builder truce above). The state is
        // VIEW-local (pure presentation: which surface wins the screen), polled
        // version-cheaply each Update. On modal close the panel re-shows, re-notifies
        // the arbiter, and the conversation resumes exactly where it was.
        private bool _hiddenForModal;
        private bool _hiddenForCombat;

        private void OnOpened(DialogueViewModel vm)
        {
            if (_vm != null) Unbind();
            _lastCardKey = null;
            _vm = vm;
            // Opened while the builder is already up (e.g. a step outro riding a build action):
            // seed the VM-owned truce from the live builder state so it starts HIDDEN — no
            // one-frame flash, no arbiter registration yet. (The DialoguePortrait.Forced reset
            // now lives in DialogueViewModel.Begin — WO-744.)
            _vm.SetBuilderActive(DeNelle.Core.BuildModeState.IsActive);
            if (_vm.HiddenForBuilder)
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                    "opened while the builder is up — starting HIDDEN (WO-702 truce); reshown on builder exit.");
            // WO-795: opened while ANOTHER modal already owns the arbiter (e.g. a step
            // dialogue riding a Rumor Board accept): start HIDDEN -- no overlap flash,
            // no arbiter registration yet; the reshow Repaint registers on modal close.
            _hiddenForModal = PanelManager.AnyOpen && !_arbiterNotified;
            _hiddenForCombat = DeNelle.Core.Combat.BattleLock.IsInBattle();
            if (_hiddenForCombat)
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                    "opened during combat -- starting hidden at current line; resumes after combat.");
            if (_hiddenForModal)
                DeNelle.Core.Diagnostics.FlowTrace.Step("UI",
                    "Dialogue suppressed - modal open ('" + (PanelManager.OpenPanelName ?? "?") +
                    "' at dialogue open, WO-795 truce); restored on modal close.");
            _vm.Changed += Repaint;
            var closedFor = vm;   // identity capture — this handler belongs to THIS vm only
            _vmClosedHandler = () => OnClosedFor(closedFor);
            _vm.Closed += _vmClosedHandler;
            BuildUi();
            Repaint();
            _openedAt = Time.unscaledTime;   // min-hold: the key/tap that OPENED this can't skip line 1
        }

        private void OnClosedFor(DialogueViewModel vm)
        {
            if (!ReferenceEquals(vm, _vm))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Dialogue",
                    "stale Closed from a superseded dialogue IGNORED — the successor's panel survives " +
                    "(re-entrancy guard; was the frozen-build-mode root).");
                return;
            }
            OnClosed();
        }

        private void OnClosed()
        {
            Unbind();
            _hiddenForModal = false;   // WO-795: a truce never outlives its conversation
            _hiddenForCombat = false;
            _portrait = null;
            if (_ui != null) { Destroy(_ui); _ui = null; }
            if (_arbiterNotified) { PanelManager.NotifyClosed(_handle); _arbiterNotified = false; }
        }

        private void Unbind()
        {
            if (_vm != null)
            {
                _vm.Changed -= Repaint;
                if (_vmClosedHandler != null) _vm.Closed -= _vmClosedHandler;
                _vmClosedHandler = null;
                _vm = null;
            }
        }

        // ── Build the bottom dialogue box ────────────────────────────────────────
        private void BuildUi()
        {
            _pixelBandsApplied = false;   // content-fit re-pins the fresh zones on the first paint
            _lastPanelH = -1f;
            if (_ui != null) Destroy(_ui);
            _ui = new GameObject("DialogueViewUI");
            _ui.transform.SetParent(transform, false);

            var canvas = _ui.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // F8 2026-07-06 (t=328, "dialogue comes up under hud"): 900 sat BELOW the
            // HUD kit chrome (HudAreasHost canvas = 4000), so the panel rendered under
            // the bottom action bar. Deliberate band: above the gameplay HUD kit (4000)
            // and the Echo workforce HUD (4600), below the battle overlay (5000) and
            // hard modals (30000+).
            canvas.sortingOrder = 4800;
            var scaler = _ui.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            _ui.AddComponent<GraphicRaycaster>();

            // DIALOGUE TEMPLATE — F8-1/F8-5 fix (RCA_DIALOGUE_DOUBLE_FRAME_2026-07-07, OPTION A):
            // the panel is built on the WINDOW-family FrameCore (Core_Panel, portrait 1210x1815)
            // instead of the landscape FrameDialogue STRIP — the strip art stretched tall was
            // rectangle #1 of the proven "frame inside a frame" read. FrameCore is drawn for a
            // tall reading window, so the frame SUPPLIES the chrome (canon §4): its measured kit
            // zones + the factory's ObsidianFill body plate replace the deleted per-screen
            // DialogueInterior patch plate (rectangle #2), and the factory's close-band
            // reservation + footer relocation keep content geometrically above the ONE shared
            // Close (rectangle #3 no longer collides). NOTE: panel_window/panel_window_dark are
            // `panel/` role sprites — BuildObsidianPanel resolves frameName under RoleFrame, so
            // they'd fall to the zone-less PROCEDURAL path; FrameCore IS the window frame here.
            // FrameDialogue stays in the catalog for any true bottom-strip use.
            // OWNER RULING 2026-07-06 ("not over top of HUD controls; moved up, readable on
            // mobile; larger; scrollable"): a centered READING PANEL that clears every HUD
            // control zone (HudAreasHost actuals — ActionBar top y=0.150 x0.28-0.72,
            // MoveCluster top y=0.330 x<=0.270, Dock top y=0.430 x<=0.230, ActionRail top
            // y=0.420 x>=0.780, TargetInfo bottom y=0.660). x 0.29-0.71 clears the side
            // thumb clusters with margin; y 0.20 clears the action bar by 0.05; y 0.62
            // stays under TargetInfo. Both HUD areas and this panel anchor by fraction of
            // screen, so the clearance holds at 16:9 and 19.5:9 alike.
            // Choice conversations are decisions, not transient HUD callouts. Give the regular
            // presentation a true mobile reading canvas so four touch-sized choices can remain
            // visible together. The compact townsfolk/helper presentation retains the smaller
            // HUD-safe footprint.
            Vector2 dialogueMin = _compactPresentation
                ? new Vector2(0.29f, 0.20f) : new Vector2(0.18f, 0.10f);
            Vector2 dialogueMax = _compactPresentation
                ? new Vector2(0.71f, 0.62f) : new Vector2(0.82f, 0.90f);
            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, "",
                dialogueMin, dialogueMax,
                () => _vm?.Close(), withBackdrop: false, frameName: RpgUiCatalog.FrameCore);
            MedievalUiSkin.ApplyShell(chrome, compact: _compactPresentation);
            _box = chrome.root.GetComponent<RectTransform>();
            // The entire visible frame is the advance surface. Previously only the prose
            // viewport carried a Button, so tapping the title, lower chrome, or empty reading
            // space appeared broken to players. Child choice/Close buttons still win their own
            // raycasts; OnBoxTapped is also state-gated and never chooses an option.
            var panelTap = _box.GetComponent<Button>() ?? _box.gameObject.AddComponent<Button>();
            panelTap.targetGraphic = _box.GetComponent<Image>();
            panelTap.transition = Selectable.Transition.None;
            panelTap.onClick.AddListener(OnBoxTapped);

            var contentRoot = chrome.content.transform;

            // FrameCore supplies ornamental transparent chrome. Paint one continuous
            // black-iron field behind every dialogue band so the world never shows
            // through between the header, prose well and lower rail.
            var dialogueFill = ElarionUiKit.AddImage(contentRoot, "DialogueBodyFill",
                Vector2.zero, Vector2.one, ElarionUiKit.ObsidianFill, rounded: false);
            dialogueFill.transform.SetAsFirstSibling();
            var dialogueFillImage = dialogueFill.GetComponent<Image>();
            if (dialogueFillImage != null) dialogueFillImage.raycastTarget = false;

            // Kit drop-zones (the protected class — the factory's close-band reservation only
            // guards content INSIDE layout.*; laying custom fractions on chrome.content is the
            // documented "unprotected class" failure). Fallback fractions mirror the FrameCore
            // anatomy for the PROCEDURAL path (frame art absent → chrome.layout is null).
            var layout = chrome.layout;
            var headerZone   = (layout != null && layout.header != null) ? layout.header
                : MakeZone(contentRoot, "SpeakerZone",  new Vector2(0.24f, 0.90f),  new Vector2(0.88f, 0.972f));
            var bodyZone     = (layout != null && layout.body != null) ? layout.body
                : MakeZone(contentRoot, "BodyZone",     new Vector2(0.055f, 0.41f), new Vector2(0.945f, 0.855f));
            var footerZone   = (layout != null && layout.footer != null) ? layout.footer
                : MakeZone(contentRoot, "ContinueZone", new Vector2(0.08f, 0.33f),  new Vector2(0.92f, 0.395f));
            var portraitHost = (layout != null && layout.medallion != null) ? layout.medallion
                : MakeZone(contentRoot, "PortraitHost", new Vector2(0.037f, 0.868f), new Vector2(0.220f, 0.988f));

            // HEADER-BAND HEIGHT FIX (F8 2026-07-08 "text too small to read on mobile"):
            // FrameCore's stock header band is only ~0.072 of the panel (~31px) — far too thin to
            // seat a 36px Speaker over a 26px Affiliation. The guard measured the Speaker sub-rect
            // at 17px / the Affiliation at 13px and CULLED BOTH lines (0 visible glyphs). Grow the
            // header band DOWN into the empty gap above the body (FrameCore body top = 0.855, header
            // bottom = 0.900 → dead space), and pull the body top below the taller band so the two
            // never overlap. These are THIS panel's own per-instance zones (Zone()/MakeZone each mint
            // a fresh RectTransform), so no other FrameCore screen is affected. Absolute anchors keep
            // the fix identical on the frame-art path and the procedural fallback path.
            // ── WO-1690 (OWNER RULING 2026-09-10 13:02: "the dialogue Affiliation line goes UP
            //    to the 30 px floor"). The 0.790 band above was sized for a 26 px affiliation and
            //    resolved to 66.7 ref px on device (measured: the Affiliation's 0.45 share logged
            //    rect 1264x30 in Builds/device-frames/2026-09-10_1235_363866_raid_logcat.txt).
            //    A 30 px line needs (30+1)*1.1499+2 = 37.65 px, so the 0.45 share needs 37.65 and
            //    the ZONE needs 37.65/0.45 = 83.7 px = 0.195 * (83.7/66.7) = 0.2447 of the panel.
            //    ⛔ THE 0.45/0.55 SPLIT IS DELIBERATELY UNCHANGED: re-splitting would seat the tag
            //    by MOVING THE SPEAKER NAME, which the ruling forbids. The band grows DOWN into
            //    the body instead — the body is a scroll zone, so it loses height, not content.
            //    Speaker's 0.55 share becomes ~46 px, which seats its authored 36 (needs ~44.6);
            //    it was ~36.7 px before and had not relaxed only because fitMin landed exactly on 30.
            headerZone.anchorMin = new Vector2(headerZone.anchorMin.x, 0.740f);   // was 0.790 (WO-1690), ~0.900 originally
            headerZone.anchorMax = new Vector2(headerZone.anchorMax.x, 0.985f);   // was ~0.972
            if (bodyZone.anchorMax.y > 0.730f)                                    // keep body top clear of the taller band
                bodyZone.anchorMax = new Vector2(bodyZone.anchorMax.x, 0.730f);

            // Content-fit hooks: cache the zone rects so ResizeToContent can re-pin them to
            // fixed-pixel bands (decoupling them from the now-variable panel height).
            _headerZone = headerZone; _bodyZone = bodyZone; _footerZone = footerZone; _portraitHost = portraitHost;
            _regularHeaderXMin = headerZone.anchorMin.x;
            _regularHeaderXMax = headerZone.anchorMax.x;

            // The medallion socket hosts the SPEAKER PORTRAIT (refreshed per Repaint), not the
            // factory's generic crest emblem — hide the fallback emblem so the two never stack.
            var emblem = portraitHost.Find("MedallionEmblem");
            if (emblem != null) emblem.gameObject.SetActive(false);

            // WO-1078: the prose viewport below already owns OnBoxTapped and its geometry
            // ends above the options band. A second body-sized catcher is redundant and
            // covers every choice row, so keep one hit surface: the prose viewport.

            // Keep the established Close ordering from the earlier TapAdvance repair.
            // OPTION A (F8-1/F8-5): the per-view anchor override is REMOVED — the factory's
            // SeatSharedCloseInside owns the seat (canonical 360x120, bottom-centre band), and
            // the factory's close-band reservation already ends layout.body/footer above it.
            if (chrome.close != null)
                chrome.close.transform.SetAsLastSibling();
            // F8-22 (one-action-one-button): keep the factory's shared Close handle so Repaint
            // can arbitrate it against the Continue chip — exactly ONE primary action visible.
            _close = chrome.close;
            _lastActionArb = null;

            // Speaker name → the kit HEADER zone (left, gilt) with the guild/shop AFFILIATION
            // as a dim sub-line beneath it (owner-ratified card standard: name + affiliation +
            // portrait on every NPC card). Body text → body zone. (Drop, no re-style.)
            // FrameCore's header band is thin (~7% of the panel) — FitSingleLine bounds both
            // lines (auto-size + ellipsis, §1.14) so they can never clip in the band.
            // Authored on the mobile ladder (owner F8 2026-07-08 "text too small to read on
            // mobile"): Speaker 36 / Affiliation 30 — was 24/13, BELOW the 30px mobile floor, so
            // FitSingleLine's minSize clamped DOWN to the authored max and the guard then shrank
            // them to 13/12 in the thin header band. Authoring on the ladder lets auto-size use
            // the room; the guard's FontHardFloor(20) is now the readable last resort, never 12.
            //
            // ⚠ THE AFFILIATION WAS 26 UNTIL 2026-09-10 AND THAT WAS NOT A TYPO — it was an
            // authored ladder choice, and it is why this label was the ONE entry in WO-1690 whose
            // relaxation had floorFrom=26 instead of 30: FitSingleLine takes maxSize = the label's
            // CURRENT fontSize, so a 26 px authoring silently clamps its own 30 px minSize down to
            // 26. The guard did not drag it under the floor; the author placed it there. The
            // OWNER RULED at 2026-09-10 13:02 that it goes UP to the floor — verbatim: "the
            // dialogue Affiliation line goes UP to the 30 px floor". The band above was re-authored
            // in the same change so 30 seats without cutting and without moving the speaker name.
            _speaker = MakeLabel(headerZone, "Speaker", new Vector2(0f, 0.45f), Vector2.one,
                36, ElarionUi.Gilt, TMPro.FontStyles.Bold, TMPro.TextAlignmentOptions.Bottom);
            ElarionUiKit.FitSingleLine(_speaker);
            _affiliation = MakeLabel(headerZone, "Affiliation", Vector2.zero, new Vector2(1f, 0.45f),
                30, ElarionUi.ParchmentDim, TMPro.FontStyles.Italic, TMPro.TextAlignmentOptions.Top);
            ElarionUiKit.FitSingleLine(_affiliation);
            // SCROLLABLE BODY (owner 2026-07-06: "in case there is more text, scrollable"):
            // the body zone hosts the §1.14 kit scroll zone (vertical, clamped, auto-hide
            // scrollbar). Longer passages scroll instead of overflowing.
            var wellGo = new GameObject("BodyWell", typeof(RectTransform));
            wellGo.transform.SetParent(bodyZone, false);
            var wellRt = wellGo.GetComponent<RectTransform>();
            // Full body zone — the old 0.18 hint sliver is retired (the Continue chip below
            // has its own band between the body and the Close).
            wellRt.anchorMin = Vector2.zero; wellRt.anchorMax = Vector2.one;
            wellRt.offsetMin = Vector2.zero; wellRt.offsetMax = Vector2.zero;
            _bodyWell = wellRt;   // WO-1030: ResizeToContent ends this well above the option band
            var scrollZone = ElarionUiKit.MakeScrollZone(wellGo.transform, spacing: 0f, padding: 8);

            _body = MakeLabel(scrollZone.content, "Body", Vector2.zero, Vector2.one,
                30, ElarionUi.Parchment, TMPro.FontStyles.Normal, TMPro.TextAlignmentOptions.Center);
            // The scroll column deliberately does NOT control child height (§1.14 kit note —
            // the captured PartyShop collapse, runs 9400/9401), so the label carries its own:
            // a vertical ContentSizeFitter grows it with its text, and the column's own
            // fitter sums that into a scrollable content height.
            var bodyFit = _body.gameObject.AddComponent<ContentSizeFitter>();
            bodyFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bodyFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            // §1.14 belt-and-braces: wrap + truncate protection on the block. min=max keeps
            // the reading size deterministic (the scroll well, not shrinking text, absorbs
            // long passages) — 30px is the mobile reading size (was 17, sub-legible on a phone;
            // F8 2026-07-08). Longer passages scroll in BodyWell, they never shrink this.
            ElarionUiKit.FitBlock(_body, minSize: 30f, maxSize: 30f);

            // Tap-to-advance INSIDE the scrolling well: the viewport's raycast surface
            // doubles as the click target (Button = click, ScrollRect = drag; uGUI splits
            // them at the drag threshold) — tapping the text advances, dragging it scrolls.
            var vpBtn = scrollZone.viewport.gameObject.AddComponent<Button>();
            vpBtn.transition = Selectable.Transition.None;
            vpBtn.onClick.AddListener(OnBoxTapped);

            // OWNER 2026-07-08 ("instead of continue button, maybe tap to continue?"): the
            // advance affordance is a passive HINT, not a button — the whole panel already
            // advances (the viewport Button above), so a chip was
            // a duplicate control. The no-dead-interaction law (2026-07-06 sweep) demands a
            // VISIBLE affordance: this label renders in the kit FOOTER zone (the factory's
            // close-band reservation keeps it clear of the shared Close), gold + italic so
            // it reads as guidance, raycast OFF so taps on it fall through to the catcher.
            // Repaint keeps driving its visibility through _tapHint (hidden while options show).
            // OWNER F8 2026-07-10 ("remove the continue and press any key"): the visible
            // "Tap to continue ▸" chip is removed. Advance is now ANY key (Update, keyboard-only)
            // OR a tap on the prose viewport. _tapHint stays null;
            // its later uses are null-guarded (contRt at ~368, SetActive at ~430) so no dead control.
            _tapHint = null;

            // Speaker portrait → the frame's MEDALLION socket (layout.medallion — FrameCore's
            // top-left circle socket; the factory's crest emblem is hidden above so the two
            // never stack). The actual sprite is resolved + REFRESHED every Repaint
            // (RefreshPortrait), because a per-node `portrait` command can change the speaker
            // portrait mid-conversation. Built once here, repainted live.
            _portrait = ElarionUiKit.Portrait(portraitHost,
                ResolveSpeakerPortrait(out _), active: false);
            if (_portrait != null && _portrait.image != null) _portrait.image.raycastTarget = false;
            // OWNER F8 t=322: "can we lose yellow circle around image?" — the kit's
            // Portrait always adds a gold Ring overlay; hide it here (kit untouched —
            // HUD/battle portraits keep theirs). The portrait reads plain on the plate.
            if (_portrait != null && _portrait.ring != null) _portrait.ring.gameObject.SetActive(false);

            // Options band -- INSIDE the kit BODY zone (the protected class: the factory
            // reservation already ends this zone above the Close band, so option plates can
            // never collide with the shared Close).
            // WO-1030 (owner screenshot 2026-08-16: 'Repair structures' sliced by the panel
            // edge): the column is no longer a FRACTION overlay of the body zone's lower 0.60
            // -- that both painted over the text and let the content-fit ceiling push the
            // bottom rows off the panel. It is now a FIXED-PIXEL BAND hugging the body zone's
            // bottom edge whose height ResizeToContent reserves FIRST (the TEXT scrolls;
            // choices never do). The band hosts a kit scroll zone (kit 1.14) so a many-option
            // node scrolls WITH the kit's visible scrollbar instead of silently clipping, and
            // every row seats at ElarionUiKit.MinTouchPx -- a real touch target on a phone.
            var optHost = new GameObject("Options", typeof(RectTransform));
            optHost.transform.SetParent(bodyZone, false);
            _optionsHost = (RectTransform)optHost.transform;
            _optionsHost.anchorMin = new Vector2(0.05f, 0f);
            _optionsHost.anchorMax = new Vector2(0.95f, 0f);
            _optionsHost.offsetMin = Vector2.zero;
            _optionsHost.offsetMax = Vector2.zero;   // height driven per-paint by ResizeToContent
            var optZone = ElarionUiKit.MakeScrollZone(optHost.transform,
                spacing: OptionRowGapPx, padding: (int)OptionsPadPx);
            _optionsCol = optZone.content;
            _optionsHost.gameObject.SetActive(false);   // shown only while a choice is up

            // F8-1/F8-5 instrumentation (kept per the RCA): log the REAL post-layout geometry
            // of the new kit zones — panel / body / footer / close / continue — once per build.
            // Runs at the END of BuildUi so `continue` is a real rect (the old call fired
            // before the chip existed and honestly logged `<null>`). SYNCHRONOUS
            // (ForceUpdateCanvases) — headless-safe under -nographics.
            TraceDialogueLayout(chrome, bodyZone, footerZone);
        }

        // F8-1/F8-5 (kept per the RCA, re-pointed at the OPTION A anatomy): dump the REAL
        // post-layout geometry of the panel / kit body zone / kit footer zone / Close /
        // Continue, in fractions of the PANEL rect — proves the factory's close-band
        // reservation is protecting the dropped content. ForceUpdateCanvases makes the world
        // corners valid synchronously (headless-safe — no reliance on a render frame).
        private void TraceDialogueLayout(ElarionUiKit.PanelChrome chrome,
            RectTransform bodyZone, RectTransform footerZone)
        {
            if (chrome == null || chrome.root == null) return;
            Canvas.ForceUpdateCanvases();
            var panelRt = chrome.root.GetComponent<RectTransform>();
            var corners = new Vector3[4];
            panelRt.GetWorldCorners(corners);
            Vector3 pMin = corners[0], pMax = corners[2];
            float pw = Mathf.Max(0.001f, pMax.x - pMin.x), ph = Mathf.Max(0.001f, pMax.y - pMin.y);

            string Frac(RectTransform rt)
            {
                if (rt == null) return "<null>";
                rt.GetWorldCorners(corners);
                return string.Format("x {0:F3}-{1:F3} y {2:F3}-{3:F3}",
                    (corners[0].x - pMin.x) / pw, (corners[2].x - pMin.x) / pw,
                    (corners[0].y - pMin.y) / ph, (corners[2].y - pMin.y) / ph);
            }

            var closeRt = chrome.close != null ? chrome.close.transform as RectTransform : null;
            var contRt = _tapHint != null ? _tapHint.transform as RectTransform : null;
            DeNelle.Core.Diagnostics.FlowTrace.Step("DlgLayout",
                "panel worldY " + pMin.y.ToString("F1") + ".." + pMax.y.ToString("F1") +
                " | body " + Frac(bodyZone) +
                " | footer " + Frac(footerZone) +
                " | close " + Frac(closeRt) +
                (closeRt != null
                    ? " (pivot=" + closeRt.pivot.ToString("F2") + " anchors=" + closeRt.anchorMin.ToString("F3") +
                      " sizeDelta=" + closeRt.sizeDelta.ToString("F0") + " parent='" + closeRt.parent.name + "')"
                    : "") +
                " | continue " + Frac(contRt));
        }

        // OWNER F8 2026-07-10: any KEY advances the dialogue (the "press any key" ask). Mouse/touch
        // already advance via the prose viewport Button, so this is keyboard-only —
        // a click firing BOTH the Button and this would skip two lines. Min-hold guards the opening
        // input; keyboard-only via legacy Input (DeNelle.HUD does not reference the Input System).
        private float _openedAt;
        private const float AdvanceMinHold = 0.25f;

        private void Update()
        {
            TickBuilderTruce();
            TickCombatTruce();
            TickModalTruce();
            if (_vm != null && _vm.HiddenForBuilder) return;   // WO-702: no any-key advance on an invisible dialogue
            if (_hiddenForCombat) return;
            if (_hiddenForModal) return;                       // WO-795: same law while a modal owns the screen
            if (_vm == null || !_vm.IsOpen || _vm.ShowingOptions) return;
            if (Time.unscaledTime - _openedAt < AdvanceMinHold) return;
            if (UnityEngine.Input.anyKeyDown &&
                !UnityEngine.Input.GetMouseButtonDown(0) &&
                !UnityEngine.Input.GetMouseButtonDown(1) &&
                !UnityEngine.Input.GetMouseButtonDown(2))
            {
                _vm.Advance();
            }
        }

        // A combat start temporarily yields the whole screen to the combat HUD. The dialogue VM
        // remains open at the same line: no Ended event, no tutorial step falsely completed.
        // WaveManager's PanelManager.CloseAll reaches OnArbiterClose; that path sets this flag too.
        private void TickCombatTruce()
        {
            if (_vm == null) return;
            bool combat = DeNelle.Core.Combat.BattleLock.IsInBattle();
            if (combat == _hiddenForCombat) return;
            _hiddenForCombat = combat;
            if (_vm.IsOpen && _ui != null)
            {
                if (combat)
                {
                    if (_arbiterNotified) PanelManager.NotifyClosed(_handle);
                    _arbiterNotified = false;
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                        "hidden for combat -- panel off, VM paused at current line, Ended NOT fired.");
                }
                else
                {
                    _openedAt = Time.unscaledTime;
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                        "restored after combat -- current tutorial line resumed.");
                }
                Repaint();
            }
        }

        // WO-702: per-frame truce poll. Hide the live panel the frame the builder opens,
        // re-show it the frame the builder closes (min-hold reset so the closing tap
        // can't skip line 1). Publishes DialogueHiddenForBuilder truthfully every frame
        // (self-healing: a dialogue superseded/closed while hidden clears it).
        private void TickBuilderTruce()
        {
            if (_vm == null) return;
            bool builder = DeNelle.Core.BuildModeState.IsActive;
            bool live = _vm.IsOpen && _ui != null;

            // The VM owns the truce flag + the BuildModeState.DialogueHiddenForBuilder publish
            // (WO-744) — forward the per-frame builder state and repaint on a change.
            bool changed = _vm.SetBuilderActive(builder);
            if (changed && live)
            {
                if (builder)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                        "hidden for builder (WO-702 truce) — panel off, VM stays open, Ended NOT fired.");
                }
                else
                {
                    _openedAt = Time.unscaledTime;   // re-arm min-hold: the builder-close tap can't skip line 1
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                        "reshown after builder exit (WO-702 truce) — player can read/advance now.");
                }
                Repaint();
            }
        }

        // WO-795: per-frame modal truce poll (version-cheap read of PanelManager.AnyOpen).
        // While a DIFFERENT arbiter-tracked modal is open (_arbiterNotified false means the
        // open panel is not us), hide the live panel; the frame the modal closes, re-show it
        // (min-hold re-armed so the modal-closing tap can't skip line 1). A modal-to-modal
        // swap (Store to Jukebox) keeps AnyOpen true, so the truce holds without flapping.
        private void TickModalTruce()
        {
            if (_vm == null) return;
            bool otherModal = PanelManager.AnyOpen && !_arbiterNotified;
            if (otherModal == _hiddenForModal) return;
            _hiddenForModal = otherModal;

            bool live = _vm.IsOpen && _ui != null;
            if (!live) return;
            if (otherModal)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("UI",
                    "Dialogue suppressed - modal open ('" + (PanelManager.OpenPanelName ?? "?") +
                    "', WO-795 truce) -- panel off, VM stays open, Ended NOT fired.");
            }
            else
            {
                _openedAt = Time.unscaledTime;   // re-arm min-hold: the modal-close tap can't skip line 1
                DeNelle.Core.Diagnostics.FlowTrace.Step("UI",
                    "Dialogue restored - modal closed (WO-795 truce); player can read/advance now.");
            }
            Repaint();
        }

        private void OnBoxTapped()
        {
            if (_vm == null) return;
            if (Time.unscaledTime - _openedAt < AdvanceMinHold) return;
            if (!_vm.ShowingOptions) _vm.Advance();   // tapping the box advances lines, not choices
        }

        // WO-795: the arbiter-registered close action. The arbiter invokes it on two paths
        // that need DIFFERENT handling:
        //   SWAP-OUT (another modal just took the slot — NotifyOpened sets its new owner
        //   BEFORE invoking us, so PanelManager.AnyOpen is TRUE here): HIDE the live
        //   conversation, never destroy it — closing fires Ended and would falsely
        //   complete a dialogue-gated tutorial step. The modal-truce poll restores it.
        //   GENUINE CLOSE (ESC / CloseOpen / CloseAll — the record is cleared BEFORE the
        //   invoke, so AnyOpen is FALSE): the player dismissed the dialogue; truly Close.
        // The panel's own X button keeps its direct _vm.Close() — that path is always a
        // real dismissal and never routes through this.
        private void OnArbiterClose()
        {
            if (_vm != null && _vm.IsOpen && DeNelle.Core.Combat.BattleLock.IsInBattle())
            {
                _arbiterNotified = false;
                _hiddenForCombat = true;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                    "combat CloseAll converted to truce -- dialogue hidden, VM remains open at current line.");
                Repaint();
                return;
            }
            if (_vm != null && _vm.IsOpen && PanelManager.AnyOpen)
            {
                _arbiterNotified = false;   // the arbiter now tracks the other modal; re-notify on restore
                _hiddenForModal = true;
                DeNelle.Core.Diagnostics.FlowTrace.Step("UI",
                    "Dialogue suppressed - modal open ('" + (PanelManager.OpenPanelName ?? "?") +
                    "' swapped in, WO-795 truce) -- panel off, VM stays open, Ended NOT fired.");
                Repaint();
                return;
            }
            _vm?.Close();
        }

        // ── Render the VM ────────────────────────────────────────────────────────
        private void Repaint()
        {
            if (_vm == null || _ui == null) return;
            bool open = _vm.IsOpen;
            // WO-702 truce: while the builder is open the panel stays OFF even though
            // the VM is open (hidden, not closed). Skip the paint AND the arbiter
            // notification — NotifyOpened on an inactive _ui would trip the arbiter's
            // isOpen-verify false-Fail; it fires on the reshow Repaint instead.
            // WO-795: the modal truce hides by the same law — NotifyOpened while another
            // modal is up would swap-close that modal; it fires on the reshow instead.
            _ui.SetActive(open && !_vm.HiddenForBuilder && !_hiddenForCombat && !_hiddenForModal);
            if (!open || _vm.HiddenForBuilder || _hiddenForCombat || _hiddenForModal) return;

            // Register + announce to the modal arbiter on the FIRST visible paint.
            // (DialogueService raises Opened BEFORE vm.Begin(), so IsOpen is still false
            // inside OnOpened — notifying there would trip the arbiter's isOpen-verify
            // false-Fail. A command-only dialogue that closes before its first open
            // paint never registers, correctly.)
            if (!_arbiterNotified)
            {
                if (_handle == null)
                    _handle = PanelManager.RegisterBattleAllowed("Dialogue",
                        OnArbiterClose, () => _ui != null && _ui.activeSelf);
                _arbiterNotified = true;
                PanelManager.NotifyOpened(_handle);
            }

            if (_speaker != null) { _speaker.text = _vm.Speaker; _speaker.gameObject.SetActive(!string.IsNullOrEmpty(_vm.Speaker)); }
            if (_affiliation != null)
            {
                // WO-744: the catalog read now lives in the VM projection (_vm.Affiliation).
                string aff = _vm.Affiliation;
                _affiliation.text = aff ?? "";
                _affiliation.gameObject.SetActive(!string.IsNullOrEmpty(aff));
            }
            if (_body != null) _body.text = _vm.Text;
            // Short, linear helper lines are moment-to-moment guidance, not story cards.  Give
            // them a compact speaker strip and reading line instead of reserving a portrait-sized
            // header and leaving a large black void. Choices and longer passages retain the full
            // portrait composition.
            _compactPresentation = !_vm.ShowingOptions &&
                !string.IsNullOrWhiteSpace(_vm.Speaker) &&
                !string.IsNullOrWhiteSpace(_vm.Text) &&
                _vm.Text.Length <= 140;
            RefreshPortrait();
            if (_portraitHost != null) _portraitHost.gameObject.SetActive(!_compactPresentation);

            BuildOptions();

            // F8-22 ONE-ACTION ARBITRATION: exactly ONE primary action visible at any moment.
            //   Continue — a linear line with text (Advance always works: the VM's OnEnded
            //              auto-closes on the last line's Advance, so Continue suffices).
            //   Options  — ShowingOptions: each option IS the action; both chips hide.
            //   Close    — the degenerate remainder only (no options, empty text): the shared
            //              Close is the sole way out of a text-less non-choice state.
            bool showContinue = !_vm.ShowingOptions && !string.IsNullOrEmpty(_vm.Text);
            bool showClose = !_vm.ShowingOptions && !showContinue;
            if (_tapHint != null) _tapHint.SetActive(showContinue);
            if (_close != null) _close.gameObject.SetActive(showClose);
            // ResizeToContent reserves the tall Close band ONLY when the Close is actually shown —
            // a normal passage (close=False) collapses it to a thin margin so the box hugs the text
            // instead of leaving a 132px empty void below it (owner F8 2026-07-17 "empty box").
            _reserveCloseBand = showClose;
            string arb = "action arb: continue=" + showContinue + " close=" + showClose +
                " options=" + _vm.ShowingOptions;
            if (arb != _lastActionArb)
            {
                _lastActionArb = arb;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue", arb);
            }

            // Fit the panel HEIGHT to the freshly-painted content (short line -> hug, long -> scroll).
            ResizeToContent();
        }

        // ── CONTENT-FIT SIZING ───────────────────────────────────────────────────
        // OWNER F8 2026-07-16: the reading panel was a FIXED tall rect (BuildUi anchors y 0.20-0.62).
        // A one-line reply sat at the top and a large empty black void filled the rest — looked
        // unfinished. The frame's interior zones are all PANEL-FRACTION anchored, so a naive
        // panel-shrink would scale the header/close bands too: the fixed-px 36/26 speaker text would
        // clip (the F8 header-band fix), and the fixed 120px shared Close (seated bottom, growing UP
        // from a fraction of the panel) would climb into the body — undoing the F8 close-band
        // reservation. So the FIRST visible paint RE-PINS the zones to FIXED-PIXEL bands off the
        // panel edges (header+medallion to the top, body between, footer just above the Close band),
        // decoupling their pixel geometry from the panel height. The shared Close keeps its own
        // factory seat untouched — BottomBandPx is sized to clear it at every height. Then EVERY
        // paint measures the body's preferred height and sets the PANEL height to
        //   TopPad + HeaderPx + Gap + clamp(content, MinBodyPx, MaxBodyPx) + BottomBandPx.
        // The panel is collapsed onto its ORIGINAL vertical CENTER so it shrinks symmetrically and
        // never jumps or crosses a HUD control zone (MaxBodyPx is derived from the original rect
        // height, so the MAX panel == the original fixed rect — clearance preserved at any aspect).
        private void ResizeToContent()
        {
            if (_box == null || _body == null) return;

            // OWNER F8 2026-07-16 ("utilize the area not a tiny area and scrollbar"): the header
            // (150) + footer (168) bands ate ~318px of a ~420-486px landscape panel, leaving the
            // reading area a ~140px sliver so any line past ~5 rows scrolled in a tiny window while
            // the bands sat empty. Trim both to the minimum that still seats their content (header:
            // 36 speaker + 26 affiliation + letterboxed portrait; footer: the 120px shared Close +
            // margin) so the BODY is the dominant zone of the box.
            float TopPad = _compactPresentation ? 36f : 18f;
            float HeaderPx = _compactPresentation ? 68f : 108f;
            float Gap = _compactPresentation ? 8f : 10f;
            const float BottomBandPx = 132f;   // clears the fixed 120px shared Close band + 12px margin (Close SHOWN)
            // OWNER F8 2026-07-17 ("still scroll issue in window" + big empty box): for a normal
            // passage the Close is HIDDEN (action arb close=False), so the 132px band below the text
            // was an empty black void (~39% of a 336px box). Collapse it to a thin border margin when
            // the Close is not shown so the text well fills the box.
            float BottomMarginPx = _compactPresentation ? 20f : 24f;
            // OWNER F8 2026-07-17 ("still scroll issue"): the scroll WELL's content is the raw text
            // PLUS the kit scroll column's own vertical padding (MakeScrollZone padding:8 -> 8 top +
            // 8 bottom = 16px). Sizing the viewport to the bare text left the content 16px taller than
            // the viewport, so a 2-line reply scrolled + clipped its 2nd line (break-log: resize
            // contentH=68 -> 68px viewport while the well content was ~84px). Add the pad (+4px so the
            // content can never equal/exceed the viewport -> the auto-hide scrollbar stays hidden).
            const float BodyWellPadPx = 20f;   // 16px MakeScrollZone padding + 4px no-overflow margin
            float MinBodyPx = _compactPresentation ? 52f : 96f;
            // OWNER F8 2026-07-16 ("the text box should use the FULL dialog box"): the kit FrameCore
            // body zone is inset ~5.5% each side (0.055..0.945), so the text/plate read as a small
            // box floating inside the frame. Widen the body to the frame's INNER border and keep only
            // a small readable pad off the gilt edge. Core_Panel's medallion socket left = 0.037, so
            // 0.040 hugs the interior without overrunning the border art (symmetric right = 0.960).
            const float BodyInsetX = 0.040f;   // frame inner border (was the kit's 0.055 inset)
            const float SidePad = 14f;         // small readable pad so text never kisses the gilt border

            Canvas.ForceUpdateCanvases();

            if (!_pixelBandsApplied)
            {
                // OWNER F8 2026-07-16 ("utilize the area not a tiny area and scrollbar"): the old
                // cap == the authored 0.42 rect, which minus the chrome bands left only ~140px of
                // reading area (long lines scrolled in a sliver). Let the panel GROW for long
                // passages up to a HUD-SAFE height (symmetric about the panel's vertical centre)
                // so the text fills the box before it ever scrolls. HUD clearance: top stays under
                // TargetInfo (bottom y=0.660) and bottom stays above the action bar (top y=0.150).
                float cyFrac = (_box.anchorMin.y + _box.anchorMax.y) * 0.5f;      // authored centre (~0.41)
                float halfSafe = Mathf.Min(0.655f - cyFrac, cyFrac - 0.155f);     // HUD-safe half-height
                float maxFrac = Mathf.Max(_box.anchorMax.y - _box.anchorMin.y, 2f * halfSafe);
                float maxPanelH = maxFrac * CanvasLocalHeight();
                // Floor keeps a generous reading area on a small canvas; the derived value wins on
                // real phones (~220-300px body, up from the pre-fix ~140px sliver).
                _maxBodyPx = Mathf.Max(180f, maxPanelH - (TopPad + HeaderPx + Gap + BottomBandPx));

                // Panel: collapse the vertical stretch onto its original CENTER so the height is
                // sizeDelta-driven and the box stays visually anchored (symmetric shrink about the
                // same midline). Horizontal anchors are untouched (width unchanged).
                float cy = (_box.anchorMin.y + _box.anchorMax.y) * 0.5f;
                _box.anchorMin = new Vector2(_box.anchorMin.x, cy);
                _box.anchorMax = new Vector2(_box.anchorMax.x, cy);
                _box.pivot = new Vector2(_box.pivot.x, 0.5f);
                _box.anchoredPosition = new Vector2(_box.anchoredPosition.x, 0f);

                // Header + medallion pinned to the TOP edge (fixed px) — decoupled from panel height
                // so the 36/26px speaker+affiliation never clip when the panel shrinks. Square
                // portrait art letterboxes in the band via preserveAspect.
                if (_headerZone != null)   PinTopBand(_headerZone, TopPad, HeaderPx);
                if (_portraitHost != null) PinTopBand(_portraitHost, TopPad, HeaderPx);

                // Body FILLS the full frame interior. Vertically it spans header-band -> Close-band
                // (fixed px); horizontally it now spans the frame's inner border (BodyInsetX) instead
                // of the kit's inset well, so the text uses the WHOLE box width with only SidePad off
                // each edge. The ObsidianFill ZoneBacking is a 0..1 child of this zone, so the dark
                // plate widens with it (no separate plate to resize). Header/portrait (top band),
                // shared Close (bottom band), and the scroll well (0..1 child) are all preserved.
                if (_bodyZone != null)
                {
                    float wasX0 = _bodyZone.anchorMin.x, wasX1 = _bodyZone.anchorMax.x;
                    _bodyZone.anchorMin = new Vector2(BodyInsetX, 0f);
                    _bodyZone.anchorMax = new Vector2(1f - BodyInsetX, 1f);
                    _bodyZone.offsetMin = new Vector2(SidePad, BottomBandPx);
                    _bodyZone.offsetMax = new Vector2(-SidePad, -(TopPad + HeaderPx + Gap));
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue", string.Format(
                        "body widened to fill frame interior: x {0:F3}-{1:F3} -> {2:F3}-{3:F3} (+/-{4:F0}px pad)",
                        wasX0, wasX1, BodyInsetX, 1f - BodyInsetX, SidePad));
                }

                // Footer band pinned just above the Close (host of the passive hint; the shared Close
                // keeps its factory seat — the bottom band is sized to clear it).
                if (_footerZone != null)
                {
                    float fx0 = _footerZone.anchorMin.x, fx1 = _footerZone.anchorMax.x;
                    _footerZone.anchorMin = new Vector2(fx0, 0f);
                    _footerZone.anchorMax = new Vector2(fx1, 0f);
                    _footerZone.offsetMin = new Vector2(0f, BottomBandPx - 8f);
                    _footerZone.offsetMax = new Vector2(0f, BottomBandPx + 48f);
                }

                _pixelBandsApplied = true;
                Canvas.ForceUpdateCanvases();
            }

            // Presentation can change as the same VM advances from a short helper line into a
            // longer passage or a choice. Re-seat the fixed bands on every paint so that switch
            // is reversible and never inherits stale compact geometry.
            if (_headerZone != null)
            {
                _headerZone.anchorMin = new Vector2(
                    _compactPresentation ? BodyInsetX : _regularHeaderXMin, _headerZone.anchorMin.y);
                _headerZone.anchorMax = new Vector2(
                    _compactPresentation ? 1f - BodyInsetX : _regularHeaderXMax, _headerZone.anchorMax.y);
                PinTopBand(_headerZone, TopPad, HeaderPx);
            }
            if (_portraitHost != null) PinTopBand(_portraitHost, TopPad, HeaderPx);
            if (_bodyZone != null)
                _bodyZone.offsetMax = new Vector2(_bodyZone.offsetMax.x, -(TopPad + HeaderPx + Gap));

            // The compact strip has no portrait/affiliation composition, so seat the speaker
            // name comfortably inside the upper iron rail.  Leaving the stock header label at
            // y=0.45..1 makes its ascenders ride across the ornate top edge after the panel hugs
            // a one-line reply.  Use offsets (not a font shrink) so the 36px mobile reading size
            // stays intact, and restore the authored geometry for full dialogue cards.
            if (_speaker != null)
            {
                var speakerRt = _speaker.rectTransform;
                float compactInset = _compactPresentation ? 16f : 0f;
                speakerRt.offsetMin = new Vector2(speakerRt.offsetMin.x, -compactInset);
                speakerRt.offsetMax = new Vector2(speakerRt.offsetMax.x, -compactInset);
            }

            // Measure the body's preferred height at its (height-independent) current width, then add
            // the scroll well's own padding so the VIEWPORT is sized to the FULL well content (text +
            // padding) — otherwise the content overflows the viewport by the padding and the auto-hide
            // scrollbar appears + clips the last line (the recurring defect; break-log proof above).
            float w = _body.rectTransform.rect.width;
            if (w < 1f) w = 380f;
            float textPx = _body.GetPreferredValues(_body.text ?? "", w, 0f).y;
            float textWellPx = textPx > 0f ? textPx + BodyWellPadPx : 0f;

            // WO-1030: the option band's height is MEASURED from the built rows (kit scroll
            // column: padding + fixed MinTouchPx rows + spacing), with the derived arithmetic
            // as a floor so a not-yet-settled layout pass can never under-reserve the band.
            int optCount = (_vm != null && _vm.ShowingOptions && _vm.OptionLabels != null)
                ? _vm.OptionLabels.Count : 0;
            float optionsPx = 0f;
            if (optCount > 0 && _optionsCol != null)
            {
                float derived = optCount * ElarionUiKit.MinTouchPx
                    + (optCount - 1) * OptionRowGapPx + 2f * OptionsPadPx;
                optionsPx = Mathf.Max(LayoutUtility.GetPreferredHeight(_optionsCol), derived);
            }
            float contentPx = textWellPx + (optionsPx > 0f ? optionsPx + OptionsGapPx : 0f);

            // Bottom band: reserve the tall Close band ONLY when the Close is shown; a normal passage
            // collapses it to a thin margin so the box hugs the text (no empty void). Re-pin the body
            // zone's bottom inset to match so the viewport (= body zone) grows into the reclaimed space.
            float band = _reserveCloseBand ? BottomBandPx : BottomMarginPx;
            if (_bodyZone != null && Mathf.Abs(_bodyZone.offsetMin.y - band) > 0.5f)
                _bodyZone.offsetMin = new Vector2(_bodyZone.offsetMin.x, band);

            // WO-1030 DEFECT A: `Mathf.Clamp(contentPx, MinBodyPx, _maxBodyPx)` capped TEXT+OPTIONS
            // as ONE sum, so on a short landscape canvas the ceiling sliced the BOTTOM OF THE
            // OPTION LIST off the panel (the owner's screenshot: 'Repair structures' cut by the
            // panel edge). Options are NOT optional:
            //   * the option band's FULL measured height is reserved FIRST;
            //   * the TEXT gets only the remainder and scrolls (it already has a scroll well);
            //   * only when the options ALONE exceed the ceiling does the option band itself
            //     scroll, with the kit scrollbar as the visible affordance -- never a silent clip.
            // The ceiling for an options paint also reclaims the collapsed Close band: _maxBodyPx
            // was derived against the tall BottomBandPx worst case, but options hide the Close
            // (action arb), so the difference is real content room INSIDE the same HUD-safe max
            // panel height (the clamp itself and the HUD-safe band are untouched -- WO-1030 4).
            // The no-options path is byte-identical to the old clamp (reference-impl guard).
            float bodyPx;
            float optionsBandPx = 0f;
            bool optionsScroll = false;
            if (optionsPx > 0f)
            {
                float maxBodyNow = _maxBodyPx + (BottomBandPx - band);
                float gap = textWellPx > 0f ? OptionsGapPx : 0f;
                float optionsCapPx = maxBodyNow - (textWellPx > 0f ? MinBodyPx + gap : 0f);
                // Even in a degenerate sliver the band keeps one full touch row visible.
                optionsCapPx = Mathf.Max(optionsCapPx, ElarionUiKit.MinTouchPx + 2f * OptionsPadPx);
                optionsScroll = optionsPx > optionsCapPx + 0.5f;
                optionsBandPx = Mathf.Min(optionsPx, optionsCapPx);
                float textBudgetPx = Mathf.Max(0f, maxBodyNow - optionsBandPx - gap);
                bodyPx = Mathf.Max(MinBodyPx,
                    Mathf.Min(textWellPx, textBudgetPx) + gap + optionsBandPx);
            }
            else
            {
                bodyPx = Mathf.Clamp(contentPx, MinBodyPx, _maxBodyPx);
            }

            // WO-1030 geometry: the reserve is a REAL RECT, not just arithmetic. Pin the option
            // band to its height at the body zone's bottom edge and end the text well just above
            // it (the old fraction overlay let option plates paint over the text and off the
            // panel). No options -> the band deactivates and the well takes the whole zone back.
            if (_optionsHost != null)
            {
                bool showBand = optionsBandPx > 0f;
                if (_optionsHost.gameObject.activeSelf != showBand)
                    _optionsHost.gameObject.SetActive(showBand);
                if (showBand && Mathf.Abs(_optionsHost.offsetMax.y - optionsBandPx) > 0.5f)
                    _optionsHost.offsetMax = new Vector2(_optionsHost.offsetMax.x, optionsBandPx);
            }
            if (_bodyWell != null)
            {
                float wellBottom = optionsBandPx > 0f ? optionsBandPx + OptionsGapPx : 0f;
                if (Mathf.Abs(_bodyWell.offsetMin.y - wellBottom) > 0.5f)
                    _bodyWell.offsetMin = new Vector2(_bodyWell.offsetMin.x, wellBottom);
            }

            float panelH = TopPad + HeaderPx + Gap + bodyPx + band;

            if (Mathf.Abs(panelH - _lastPanelH) > 0.5f)
            {
                _box.sizeDelta = new Vector2(_box.sizeDelta.x, panelH);
                _box.anchoredPosition = new Vector2(_box.anchoredPosition.x, 0f);
                _lastPanelH = panelH;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue", string.Format(
                    "resize contentH={0:F0} (text={1:F0} well={2:F0} opts={3:F0} optsBand={4:F0} optsScroll={5}) -> panelH={6:F0} band={7:F0} (min {8:F0}/max {9:F0})",
                    contentPx, textPx, textWellPx, optionsPx, optionsBandPx, optionsScroll, panelH, band,
                    TopPad + HeaderPx + Gap + MinBodyPx + BottomMarginPx,
                    TopPad + HeaderPx + Gap + _maxBodyPx + BottomBandPx));
            }

            // WO-1030 measured-outcome verify (INSTRUMENTATION_STANDARD 1.4b): assert what the
            // player actually gets, from resolved post-settle rects -- never the authored values.
            if (optCount > 0) VerifyOptionsFit(optionsScroll, optionsBandPx);
        }

        // -- WO-1030 measured-outcome verify (INSTRUMENTATION_STANDARD 1.4b) -----------------
        // Uses the SHARED DeNelle.Core.Diagnostics.UiSurfaceProbe rect arithmetic (never a
        // re-derived copy) on rects read AFTER ForceUpdateCanvases: the option band must sit
        // inside the panel AND the viewport, and the LAST option row must be reachable (inside
        // the band when not scrolling; a scrolling band declares itself instead). A clipped
        // option list fires FlowTrace.Fail -- the falsifiable line the WO-1030 screenshot had
        // to substitute for. Unmeasurable environments (batchmode) emit a NAMED skip, never a
        // silent pass.
        private void VerifyOptionsFit(bool optionsScroll, float optionsBandPx)
        {
            if (_vm == null || !_vm.ShowingOptions || _optionsHost == null || _box == null || _ui == null) return;
            if (DeNelle.Core.Diagnostics.UiSurfaceProbe.IsUnmeasurableEnvironment(out string skip))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Dialogue",
                    "options fit verify SKIPPED -- " + skip + ". Named skip, not a pass.");
                return;
            }
            Canvas.ForceUpdateCanvases();
            var rootCanvas = _ui.GetComponent<Canvas>();
            Rect panel = DeNelle.Core.Diagnostics.UiSurfaceProbe.ScreenRectOf(_box, rootCanvas);
            Rect bandR = DeNelle.Core.Diagnostics.UiSurfaceProbe.ScreenRectOf(_optionsHost, rootCanvas);
            int rows = _optionsCol != null ? _optionsCol.childCount : 0;
            var lastRt = rows > 0 ? _optionsCol.GetChild(rows - 1) as RectTransform : null;
            Rect lastR = lastRt != null
                ? DeNelle.Core.Diagnostics.UiSurfaceProbe.ScreenRectOf(lastRt, rootCanvas)
                : new Rect(0f, 0f, 0f, 0f);
            const float eps = 1.5f;   // sub-2px rasterization slack, not a licence for a clipped row
            bool bandInPanel = bandR.yMin >= panel.yMin - eps && bandR.yMax <= panel.yMax + eps;
            bool bandOnScreen = bandR.yMin >= -eps && bandR.yMax <= Screen.height + eps;
            bool lastReachable = optionsScroll
                || (lastRt != null && lastR.yMin >= bandR.yMin - eps && lastR.yMax <= bandR.yMax + eps);
            string measured = string.Format(
                "options fit MEASURED: rows={0} bandPx={1:F0} band y {2:F0}..{3:F0} panel y {4:F0}..{5:F0} " +
                "lastRow y {6:F0}..{7:F0} (screen {8}x{9}, scroll={10})",
                rows, optionsBandPx, bandR.yMin, bandR.yMax, panel.yMin, panel.yMax,
                lastR.yMin, lastR.yMax, Screen.width, Screen.height, optionsScroll);
            if (bandInPanel && bandOnScreen && lastReachable)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue", measured);
            }
            else
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Dialogue",
                    "OPTIONS CLIPPED -- " + measured + " bandInPanel=" + bandInPanel +
                    " bandOnScreen=" + bandOnScreen + " lastRowReachable=" + lastReachable +
                    " -- an unreachable choice is a dead end (WO-1030).");
            }
        }

        // Re-pin a zone to a fixed-pixel band hugging the panel TOP edge (keeps its x anchors).
        private static void PinTopBand(RectTransform rt, float topPad, float height)
        {
            float x0 = rt.anchorMin.x, x1 = rt.anchorMax.x;
            rt.anchorMin = new Vector2(x0, 1f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = new Vector2(0f, -(topPad + height));
            rt.offsetMax = new Vector2(0f, -topPad);
        }

        // Scaler-safe canvas local height (replicates the kit's PostScaleCanvasHeight for our
        // ScaleWithScreenSize + MatchWidthOrHeight config — correct on the creation frame, where
        // rootRt.rect.height would still read raw screen px). Fallback = kit portrait reference.
        private float CanvasLocalHeight()
        {
            var scaler = _ui != null ? _ui.GetComponent<CanvasScaler>() : null;
            // WO-1030: read the kit's injectable surface, not Screen.* directly -- identical on
            // device and in play mode (the surface defaults to Screen.*), but it lets the UI
            // capture harness build THIS panel's clamp geometry at the target resolution
            // (2670x1200 etc.) instead of the editor's screen. A direct Screen read here is
            // exactly the harness's named "resolution in the filename is a LABEL" residual.
            float screenH = Mathf.Max(1f, (float)ElarionUiKit.SurfaceHeight);
            float screenW = Mathf.Max(1f, (float)ElarionUiKit.SurfaceWidth);
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                float refW = Mathf.Max(1f, scaler.referenceResolution.x);
                float refH = Mathf.Max(1f, scaler.referenceResolution.y);
                float scale = Mathf.Pow(2f, Mathf.Lerp(
                    Mathf.Log(screenW / refW, 2f), Mathf.Log(screenH / refH, 2f), scaler.matchWidthOrHeight));
                if (scale > 0.0001f) return screenH / scale;
            }
            return 1920f;
        }

        private void BuildOptions()
        {
            if (_optionsCol == null) return;
            for (int i = _optionsCol.childCount - 1; i >= 0; i--) Destroy(_optionsCol.GetChild(i).gameObject);
            if (!_vm.ShowingOptions) return;

            var labels = _vm.OptionLabels;
            for (int i = 0; i < labels.Count; i++)
            {
                int idx = i;
                var go = new GameObject("Opt" + i, typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(_optionsCol, false);
                // WO-1030 touch floor: rows seat at the kit MinTouchPx (was 48 -- under the
                // mobile touch-target standard). The kit scroll column controls WIDTH but not
                // HEIGHT (childControlHeight off, kit 1.14), so the row carries its own explicit
                // rect height; the LayoutElement keeps the same floor for any height-controlling
                // parent this row might ever be re-hosted under.
                var ort = (RectTransform)go.transform;
                ort.sizeDelta = new Vector2(0f, ElarionUiKit.MinTouchPx);
                go.GetComponent<LayoutElement>().minHeight = ElarionUiKit.MinTouchPx;
                var optionButton = go.GetComponent<Button>();
                optionButton.onClick.AddListener(() => _vm?.Choose(idx));
                // Choices are first-class actions, not raw black list rows. Use the same scalable
                // black-iron/gold control family as the rest of the reskin; runtime text remains
                // separate and the full row stays the touch target.
                MedievalUiSkin.ApplyButton(optionButton, primary: false);
                var optionFace = go.GetComponent<Image>();
                if (optionFace != null)
                {
                    optionFace.type = Image.Type.Simple;
                    optionFace.color = Color.white;
                }

                // Mobile-readable option text (was 15 — sub-legible; F8 2026-07-08). The row's
                // MinTouchPx height seats a 26px line with room; FitBlock wraps + the guard's
                // readable floor keeps a long option legible rather than shrinking it into the plate.
                var lbl = MakeLabel(go.transform, "L", new Vector2(0.08f, 0.08f), new Vector2(0.94f, 0.92f),
                    26, ElarionUi.Parchment, TMPro.FontStyles.Normal, TMPro.TextAlignmentOptions.MidlineLeft);
                lbl.text = labels[i];
                lbl.raycastTarget = false;
                ElarionUiKit.FitBlock(lbl, minSize: 20f, maxSize: 26f);
            }
        }

        // ── Speaker → portrait mapping (WO-583; speakers block, card standard 2026-07-02) ──
        // The card is DATA-DRIVEN: the catalog's top-level `speakers` block declares
        // { name, affiliation, portrait } per speaker. Resolve by priority, never blank /
        // never throw:
        //   1) an AUTHORED per-node `portrait` command (back-compat OVERRIDE) / 2) the
        //      speakers-block record's portrait path — BOTH now resolved by the VM projection
        //      (_vm.PortraitPath + _vm.PortraitForced); the View only loads the sprite;
        //   3) the speaker name mapped to a class portrait (Knight/Ranger/Wizard/Healer);
        //   4) null → RefreshPortrait draws the styled SILHOUETTE (never a raw tinted disc).
        // WO-744: the catalog/DialoguePortrait reads moved into the VM — the View resolves the
        // sprite (presentation) from the VM-projected path only.
        private Sprite ResolveSpeakerPortrait(out string source)
        {
            string path = _vm != null ? _vm.PortraitPath : null;
            if (!string.IsNullOrEmpty(path))
            {
                var sp = Resources.Load<Sprite>(path);
                if (sp != null)
                {
                    source = (_vm != null && _vm.PortraitForced) ? path + " (command)" : path;
                    return sp;
                }
            }
            string speaker = _vm != null ? _vm.Speaker : null;
            // WO-1012 P2: the GUIDE (tutorial lines authored with the "{guide}" token,
            // resolved by the VM) draws its pet-Echo portrait from the identity seam —
            // the Echoes/Portraits art ships as raw Texture2D, which the plain
            // Resources.Load<Sprite> path above cannot resolve. A per-node `portrait`
            // command override (the forced path) still wins above.
            if (DeNelle.Core.Tutorial.TutorialGuide.IsGuideSpeaker(speaker))
            {
                var gp = DeNelle.Core.Tutorial.TutorialGuide.PortraitSprite();
                if (gp != null) { source = "guide:" + speaker; return gp; }
            }
            var cls = ElarionUiKit.PortraitForClass(speaker);
            if (cls != null) { source = "class:" + speaker; return cls; }
            source = "silhouette";
            return null;
        }

        // Repaint the medallion portrait from the current speaker / speakers block / forced
        // override. Called every Repaint so a per-node portrait command (or a speaker change)
        // updates the socket live; a speaker with no resolvable art gets the styled hooded
        // silhouette — NEVER the raw tan placeholder disc (the "Sylas yellow blank").
        private string _lastCardKey;   // one FlowTrace card line per speaker, not per tap
        private void RefreshPortrait()
        {
            if (_portrait == null || _portrait.image == null) return;
            string speaker = _vm != null ? _vm.Speaker : null;
            var sp = ResolveSpeakerPortrait(out string source);
            if (sp != null)
            {
                _portrait.image.sprite = sp;
                _portrait.image.color = Color.white;
                _portrait.image.preserveAspect = true;
            }
            else
            {
                _portrait.image.sprite = SilhouetteSprite;
                _portrait.image.color = Color.white;
                _portrait.image.preserveAspect = true;
            }

            // WO-744: affiliation comes from the VM projection (no direct catalog read in the View).
            string affiliation = _vm != null ? _vm.Affiliation : null;
            string card = (speaker ?? "<narration>") + "|" + source;
            if (card != _lastCardKey)
            {
                _lastCardKey = card;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Dialogue",
                    $"card {(string.IsNullOrEmpty(speaker) ? "<narration>" : speaker)}: " +
                    $"affiliation={(!string.IsNullOrEmpty(affiliation) ? affiliation : "<none>")} " +
                    $"portrait={source}");
            }
        }

        // ── Silhouette placeholder (card standard: styled, never a flat tinted circle) ───
        // Procedurally drawn ONCE into a Texture2D and cached: a dark obsidian-toned disc
        // carrying a near-black hooded-figure bust (hood peak + head + shoulders), so an
        // unportraited speaker (Sylas / Brom / Sable) reads as "a person, art pending"
        // instead of a raw color quad. Pure UnityEngine drawing — no kit change needed.
        private static Sprite _silhouette;
        private static Sprite SilhouetteSprite
        {
            get
            {
                if (_silhouette == null) _silhouette = BuildSilhouetteSprite();
                return _silhouette;
            }
        }

        private static Sprite BuildSilhouetteSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            var clear  = new Color(0f, 0f, 0f, 0f);
            var disc   = new Color(0.16f, 0.15f, 0.19f, 1f);   // dark obsidian slate
            var figure = new Color(0.045f, 0.04f, 0.06f, 1f);  // near-black hooded figure

            float c = (size - 1) * 0.5f;
            float rDisc = size * 0.5f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    if (dx * dx + dy * dy > rDisc * rDisc) { tex.SetPixel(x, y, clear); continue; }

                    // Normalized coords (0..1, y up).
                    float nx = x / (float)(size - 1);
                    float ny = y / (float)(size - 1);

                    bool inFigure = false;
                    // Head: circle centred just above middle.
                    float hx = nx - 0.5f, hy = ny - 0.58f;
                    if (hx * hx + hy * hy <= 0.155f * 0.155f) inFigure = true;
                    // Hood peak: triangle rising above the head to a point.
                    if (!inFigure && ny >= 0.58f && ny <= 0.82f)
                    {
                        float half = 0.16f * (1f - (ny - 0.58f) / 0.24f);   // narrows to the peak
                        if (Mathf.Abs(nx - 0.5f) <= half) inFigure = true;
                    }
                    // Shoulders: wide ellipse low in the disc.
                    if (!inFigure)
                    {
                        float sx = (nx - 0.5f) / 0.34f, sy = (ny - 0.12f) / 0.30f;
                        if (sx * sx + sy * sy <= 1f && ny < 0.42f) inFigure = true;
                    }

                    tex.SetPixel(x, y, inFigure ? figure : disc);
                }
            }
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sp.name = "DialogueSilhouette";
            return sp;
        }

        // Bare fraction-anchored rect inside the panel interior — FALLBACK ONLY, for the
        // procedural chrome path (frame art absent → chrome.layout is null). On the normal
        // FrameCore path the view uses the kit's layout.{header,body,footer,medallion} zones.
        private static RectTransform MakeZone(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static TMPro.TextMeshProUGUI MakeLabel(Transform parent, string name, Vector2 aMin, Vector2 aMax,
            int size, Color col, TMPro.FontStyles style, TMPro.TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);
            t.fontSize = size; t.color = col; t.fontStyle = style; t.alignment = align;
            t.textWrappingMode = TMPro.TextWrappingModes.Normal; t.raycastTarget = false;
            return t;
        }
    }
}
