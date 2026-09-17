// =============================================================================
// EndStateView — the ONE shared Obsidian end-state screen (WO-B, UI conformance
// audit 2026-07-02 §3.2). Victory / defeat / hero-death / wave-results all render
// through THIS view from an EndStateVM. Replaces the divergent implementations:
// BattleArenaHud.ShowVictorySummary + ShowLossPanel (retired in that file) and
// WaveCelebrationManager's IMGUI toast / prefab text (retired there).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.UI
//
// Canon (docs/UI_BLINK_TEMPLATE_CANON.md + owner addenda 2026-07-02):
//   • Master factory only: ElarionUiKit.BuildObsidianModal / BuildObsidianPanel
//     with frameName = RpgUiCatalog.FrameCore; content DROPS into the returned
//     drop-zones (header / body / footer). No per-screen chrome.
//   • ONE way out (owner button law): a single primary kit Button in the footer.
//     The factory's shared Close chip is HIDDEN here — an end-state must not
//     offer a second, redundant exit. (Kit change reported: a `withClose:false`
//     parameter on BuildObsidianPanel would make this first-class.)
//   • Sized to content: the panel rect is computed from what the VM carries —
//     no cavernous empty space (the owner's F8 "THis looks bad" Victory modal).
//   • SMOOTH (owner directive): fade+scale in ~250ms ease-out (unscaled time),
//     spoils rows stagger-reveal ~50ms apart, the primary button lands last.
//     No pre-existing shared UI tween helper exists in the codebase (searched:
//     only ad-hoc coroutines — BattleArenaHud.PopCrown, VillageHudController.
//     FadeInHud), so the tween lives here. KIT-PROMOTION CANDIDATE: lift
//     RevealRoutine into ElarionUiKit once a second screen needs it.
//   • MVVM strict: this view binds the EndStateVM and reads NO game state.
//   • Never pauses time — the hero-death variant narrates HeroHealth's respawn
//     coroutine, which runs on scaled time.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village.UI
{
    /// <summary>The shared end-state screen. Build one via <see cref="Show"/>.</summary>
    public sealed class EndStateView : MonoBehaviour
    {
        private static EndStateView _open;

        /// <summary>
        /// WO-1127: true while the end-state (victory / defeat) screen is up. Read by the
        /// battle-end quiescence gate, which must NOT judge the modal invariant while a reward
        /// screen is legitimately open — failing a gate on correct behaviour is the fastest way to
        /// get it switched off.
        /// </summary>
        public static bool IsShowing => _open != null;

        private EndStateVM _vm;
        private bool _fired;                      // primary-action latch (fires exactly once)
        private readonly List<Reveal> _reveals = new List<Reveal>();

        // HUD-2 (GAP_AUDIT P2 #14): the FULL end-state modal now joins the single-modal arbiter so
        // opening it closes any lingering panel (a shop left open at death) and the back button can
        // dismiss it. RegisterBattleAllowed — an end-state is the decision node shown AT/after a battle,
        // so the WO-437 battle-lock must never reject it (like the Battle HUD / Pause). Compact banners
        // (no scrim, non-blocking, auto-dismiss) deliberately stay OUT of the arbiter -> handle is null.
        private DeNelle.Core.UI.PanelHandle _panelHandle;
        private DeNelle.Core.UI.WorldHold.Handle _worldHold;

        private struct Reveal
        {
            public CanvasGroup Group;
            public RectTransform Rect;
            public float Delay;
            public float FromScale;
        }

        // ── entry point ───────────────────────────────────────────────────────

        /// <summary>Show the end-state screen for <paramref name="vm"/> (replaces any open one).</summary>
        public static EndStateView Show(EndStateVM vm,
            // F8-15 death forensic window: name WHO opened each end-state (GameOverScreen /
            // HeroDeathEndState / BattleArenaHud / WaveCelebrationManager all funnel HERE). The
            // FULL modal now routes through PanelManager (HUD-2 fixed, below); only the COMPACT
            // banner stays out of the arbiter (non-blocking).
            [System.Runtime.CompilerServices.CallerMemberName] string openerMember = null,
            [System.Runtime.CompilerServices.CallerFilePath]  string openerFile   = null)
        {
            if (vm == null) return null;
            if (DeathTrace.Active)
            {
                string opener = DeathTrace.Describe(openerMember, openerFile);
                DeathTrace.ScreenOpened("EndState '" + vm.Title + "'", opener);
                // HUD-2 FIXED: the FULL end-state modal now routes through PanelManager
                // (RegisterBattleAllowed + NotifyOpened below), so it swaps out any prior panel and
                // the arbiter can dismiss it. Only the COMPACT banner stays out of the arbiter on
                // purpose (no scrim, non-blocking, auto-dismiss) — flag just that case in the window.
                if (vm.Compact)
                    DeathTrace.ScreenBypassedArbiter("EndState '" + vm.Title + "'", opener);
                if (_open != null)
                    DeathTrace.ScreenClosed("EndState '" + (_open._vm != null ? _open._vm.Title : "?") + "'",
                        "EndStateView.Show (replaced by '" + vm.Title + "')");
            }
            // Section 12: a NEW Show() replacing an OPEN end-state is the path that stranded the
            // owner twice - the village wave banner landing on top of an arena victory summary and
            // taking its home-return action with it. Previously this was logged only when the
            // DeathTrace forensic window happened to be open, i.e. never in normal play.
            if (_open != null)
            {
                _open.AbandonedPrimaryWarn($"EndStateView.Show - REPLACED by a new end-state '{vm.Title}'");
                Destroy(_open.gameObject);
                _open = null;
            }

            // REAL EventSystem buttons (audit §2e: GameOverScreen's manual Input hit-test
            // existed because builds lacked an EventSystem — ensure one, don't hand-roll).
            EnsureEventSystem();

            GameObject canvas;
            ElarionUiKit.PanelChrome chrome;

            if (vm.Compact)
            {
                // Wave-results banner: small top-of-screen panel, NO scrim/backdrop, non-blocking.
                canvas = ElarionUiKit.BuildModalCanvas("EndState", 31000);
                var c = canvas.GetComponent<Canvas>();
                if (c != null) c.overrideSorting = true;
                // Grown DOWN (top edge held at CompactTopY) to 0.30 of screen height: this is
                // the row-less SPLASH size (F8-45/WO-952: Bind's owned compact solve grows the
                // banner downward to its FINAL content-fitted height), so it must carry enough
                // height for the header band (Bind) plus the emblem+subtitle below it —
                // otherwise the tall title band would crush them. (Was 0.64–0.86 = 0.22h, too
                // short to seat the headline.)
                chrome = ElarionUiKit.BuildObsidianPanel(canvas.transform, vm.Title,
                    new Vector2(CompactX0, CompactSplashBottomY), new Vector2(CompactX1, CompactTopY),
                    onClose: null, withBackdrop: false, frameName: RpgUiCatalog.FrameCore,
                    medallionIcon: "crest");   // explicit: the socket seats the crest family, never blank
            }
            else
            {
                // Full end-state modal, sized to the VM's content in REAL PIXELS.
                //
                // OWNER F8 2026-08-05 ("the text is too compacted"): the panel used to be built at a
                // GUESSED fictional-unit size and then grown ~2x by a post-hoc extension block. Every
                // fraction reservation inside it — the kit's close-band reservation, the header band,
                // the CTA floor-raise — had already been computed against the PRE-growth panel and was
                // never recomputed, so the body zone kept a fraction sized for a panel half as tall and
                // the content got ~17% of the panel. Build the CANVAS FIRST (BuildObsidianModal's own
                // three steps, inlined — canvas + scrim + panel) so the panel height can be SOLVED
                // against ElarionUiKit.PostScaleCanvasHeight BEFORE the frame exists. The panel is then
                // built ONCE at its final size and never resized: nothing can desynchronise.
                canvas = ElarionUiKit.BuildModalCanvas("EndState", 31000);
                var mc = canvas.GetComponent<Canvas>();
                if (mc != null) mc.overrideSorting = true;
                ElarionUiKit.Scrim(canvas.transform, null);   // pure raycast-blocker — no second way out
                float half = PanelHalfHeight(vm, ElarionUiKit.PostScaleCanvasHeight(canvas.transform));
                // ORCHESTRATOR RULING (WO-894): vertical centre 0.53 -> 0.50. The panel is built at
                // centre +- MaxPanelHalf(0.47), so a 0.53 centre put the TOP edge at 0.53 + 0.47 =
                // 1.000 — flush with the screen top, i.e. clipping — while MaxPanelHalf's own comment
                // documents the intent as "0.03..0.97". At 0.50 the clamp lands exactly on the
                // documented 0.03..0.97 at IDENTICAL height. Costs 3% of downward drift toward the
                // bottom HUD band; a screen touching the top edge is the worse defect.
                chrome = ElarionUiKit.BuildObsidianPanel(canvas.transform, vm.Title,
                    // OWNER F8 2026-09-02 ("spacing tight and ..."): the CONTENT COLUMN was the
                    // root cause of all three captured truncations. WO-433 narrowed the modal to
                    // 0.22..0.78 = 0.56 of the canvas; FrameCore's ornate border then eats
                    // 0.055..0.945 of THAT, and a two-column spoils band halves what is left — so
                    // a damage row's label column resolved to ~220 ref px and FitSingleLine's
                    // Ellipsis chopped "Archer Tower - damaged 40%" to "Archer Tow...". The frame
                    // was consuming a large share of a panel that had horizontal room to spare.
                    // 0.14..0.86 = 0.72 of the canvas gives the body ~29% more column at the SAME
                    // solved height (the height solve is width-aware: PanelWidthFrac below feeds
                    // SubtitleLines, so a wider panel wraps LESS and the panel gets no taller).
                    // Portrait is unaffected in column count (0.72 x 1080 x 0.89 / 2 = 346 px,
                    // still under MinSpoilColumnPx, so portrait stays single-column as ruled).
                    new Vector2(0.14f, 0.50f - half), new Vector2(0.86f, 0.50f + half),   // was 0.22/0.78 (WO-433), and 0.08/0.92 before that
                    onClose: null,   // no second way out
                    frameName: RpgUiCatalog.FrameCore,
                    medallionIcon: "crest");   // explicit: the socket seats the crest family, never blank
            }

            MedievalUiSkin.ApplyShell(chrome, compact: vm.Compact);

            // Owner button law: an end-state has exactly ONE way out (the primary button).
            // Hide the factory's shared Close chip.
            // LOAD-BEARING: Bind's owned geometry pass RECLAIMS the kit's close-band reservation
            // on the strength of this line. Re-enabling the Close here without reverting that
            // pass would put the Close underneath the CTA. (A kit-level withClose:false would make
            // this first-class instead of hide-after-build — reported, deliberately not done here:
            // an ElarionUiKit signature change has game-wide blast radius.)
            if (chrome.close != null) chrome.close.gameObject.SetActive(false);

            var view = canvas.AddComponent<EndStateView>();
            view.Bind(vm, chrome);
            _open = view;

            // HUD-2: the FULL modal joins the single-modal arbiter (battle-allowed - it shows at/after
            // a battle, so the WO-437 lock must not reject it). NotifyOpened closes any lingering panel
            // (e.g. a shop open at death) and records this as THE open modal. Compact banners are
            // non-blocking (no scrim) and intentionally NOT registered.
            if (!vm.Compact)
            {
                view._panelHandle = PanelManager.RegisterBattleAllowed("EndState",
                    view.CloseFromArbiter, () => view != null);
                PanelManager.NotifyOpened(view._panelHandle);
            }
            if (vm.HoldWorld)
                // WO-1360: PLAYER-OWNED. The end-state card is the decision node  -  it stands until
                // the player chooses. No elapsed-time ceiling may judge it stuck.
                // WO-1369: the liveness probe is the SAME expression the arbiter already gets
                // above (`() => view != null`) - one liveness concept, not two. If any modal
                // destroys this view without its Dispose running, the watchdog force-releases on
                // the next tick instead of pinning the world clock at 0.
                view._worldHold = DeNelle.Core.UI.WorldHold.AcquirePlayerOwned(
                    "wave-results", () => view != null);

            // P23 (HUD_OBSIDIAN A4.6): the end-state is the DECISION NODE — while it is
            // up the posture is hostile(postbattle) and the HUD kit stands down.
            DeNelle.Core.HudModel.PostureSignals.SetEndState(true);
            return view;
        }

        // ── PANEL GEOMETRY LAW (owner F8 2026-08-05) ──────────────────────────────
        // ONE stack, all fractions of THE SAME (final) panel height, top to bottom:
        //   [0.985 .. 0.820]  header band  — one FontTitle(88) line, ~101px line box
        //   [0.805 .. floor]  BODY WELL    — owns every VM band (this is what must fit)
        //   [floor .. ctaTop] CtaGapY      — the guaranteed gap; bands never share pixels
        //   [ctaTop.. 0.045]  the canonical 360x132 CTA, seated in the RECLAIMED CLOSE BAND
        //                     (this screen HIDES the shared Close — see Show, below).
        // The CTA is a FIXED 132 reference px, so it is subtracted in PIXELS, never as a
        // fraction: that is precisely the unit mix-up that produced the compressed screen.
        private const float HeaderY0  = 0.820f;   // was 0.760 — 0.225 of the panel for ONE title line
        private const float HeaderY1  = 0.985f;
        private const float BodyTopY  = 0.805f;   // body top clears the header band
        private const float CtaBandY0 = 0.045f;   // CTA bottom edge (the freed close band's lower edge)
        private const float CtaGapY   = 0.020f;   // matches the kit's own body/close gap
        /// <summary>Panel fraction available to the body well once the header, the gap and the
        /// CTA BAND ORIGIN are taken; the CTA's own 132 px come off in pixels.</summary>
        private const float BodyFracOfPanel = BodyTopY - CtaBandY0 - CtaGapY;   // 0.740
        /// <summary>Panel may span 0.03..0.97 of the screen (the old grownHalf 0.47 clamp).
        /// TRUE AGAIN as of WO-894: the panel is centred at 0.50, so 0.50 +- 0.47 really is
        /// 0.03..0.97. It was built at centre 0.53 until now, which silently made the real span
        /// 0.06..1.00 — the top edge flush with the screen edge.</summary>
        private const float MaxPanelHalf = 0.47f;
        private const float MinPanelHalf = 0.14f;

        // ── CTA CONTENT-WIDTH CONSTANTS (owner F8 2026-09-02: "PREPARE FOR W...") ──────
        /// <summary>Bold + the kit's 1px character spacing widen a measured run by roughly this
        /// much over the regular face MeasureTextPx samples. Deliberately generous: over-reserving
        /// costs a few px of gold face, under-reserving costs the ellipsis this exists to kill.</summary>
        private const float CtaBoldWidthFactor   = 1.12f;
        /// <summary>The kit seats a button label at 0.04..0.96 of the face
        /// (ElarionUiKitObsidian.BuildObsidianButton), so the label column is this much of the box.</summary>
        private const float CtaLabelInsetFrac    = 0.92f;
        /// <summary>Extra ref px for the framed face's own shoulders, so the words never sit on
        /// the 9-sliced border even when the measurement lands exactly.</summary>
        private const float CtaFacePaddingPx     = 72f;
        /// <summary>A grown CTA may span at most this much of the BODY column — it is a button,
        /// not a bar, and it must stay clear of the frame's ornate edge.</summary>
        private const float CtaMaxWidthOfBodyFrac = 0.92f;
        /// <summary>The compact banner CTA's pre-existing floor ("Repair All - 40 wood, 12 iron"
        /// was already known not to fit the canonical box). Unchanged, just named.</summary>
        private const float BannerCtaMinWidthPx  = 680f;

        // ── THE FRAME ART IS A 9-SLICE, SO ITS BORDER IS PIXELS — NEVER A FRACTION ────────
        // (owner F8 2026-09-02, defect #2: "WAVE 7 CLEARED! extends past the ornate gold border
        // on BOTH sides", capture Builds/ui-capture/EndStateWaveClear_repairAll_1920x1080.png.)
        //
        // MedievalUiSkin.ApplyShell (MedievalUiSkin.cs:19-27) re-points the panel background at
        // the Synty shell — Resources/.../frames/content-panel (compact) or modal-frame-16x9
        // (full) — and draws it Image.Type.Sliced. A nine-slice renders its border ring and its
        // corner ornaments at a FIXED reference-pixel size no matter how large the rect solves.
        // Every title/header number in this file and in the kit is a FRACTION of the panel, so
        // the taller or wider the panel solves, the further a fraction-anchored headline climbs
        // over art that did not scale with it. Reserve the border in the unit the nine-slice
        // actually uses.
        //
        // The two numbers below are MEASURED off that capture, not picked:
        //   panel top edge -> the top rail's inner edge   = ~60 ref px  (rail 255..284 px plus
        //                                                   the sprite's ~30 px transparent margin)
        //   vertical edge  -> the end of a corner ornament = ~132 ref px (ornament x 309..420 px)
        // Rounded up so the headline never lands exactly on the art.

        /// <summary>Reference px the shell's TOP border rail (plus the sprite's own transparent
        /// margin) occupies inside the panel rect.</summary>
        private const float FrameBorderTopPx  = 64f;
        /// <summary>Reference px the shell's CORNER ornaments run in from each vertical edge —
        /// the art the headline was overprinting at both ends.</summary>
        private const float FrameBorderSidePx = 148f;
        /// <summary>Clear air between the border art and the headline's box.</summary>
        private const float TitleClearPx      = 12f;
        /// <summary>Never invert or crush the title rect: if less than this survives, the band is
        /// left EXACTLY as the kit built it and the trace says so. A zero-height title band is the
        /// 2026-07-08 "0 visible glyphs" defect, which is strictly worse than an overhanging one.</summary>
        private const float MinTitleBandFrac  = 0.05f;
        /// <summary>ElarionUiKit.Header hangs its gilt hairline 0.008 under the title band; it has
        /// to travel with the band or the rule is left drawn across the frame's border.</summary>
        private const float TitleRuleDropFrac = 0.008f;

        // ── WIDE-ROW COLUMN BUDGET (owner F8 2026-09-02, defects #1 and #4) ──────────────
        // #1: the LABEL column was widened for a wide row (0.62 -> 0.70) and the VALUE column
        //     was not, so "DESTROYED, looted 120" — 21 characters of PROSE, ~500 ref px at
        //     FontBody 50 — was handed the same ~274 px cell a "+240" gets, and FitSingleLine's
        //     autosize floor (FontFloor 30) bottoms out at ~315 px. Past the floor it ellipsises,
        //     which is exactly the "DESTROYED, looted ..." in the capture.
        // #4: and it is ALSO why the two damage rows disagreed in size — "damaged" fits at the
        //     full 50, "DESTROYED, looted 120" was pinned at the 30 floor. Two rows of the same
        //     kind rendered 40% apart because each was fitted in isolation.
        // THE FIX for both is one idea: a wide row is PROSE against PROSE, so split its plate in
        // proportion to the MEASURED words (never a fixed fraction, never a string-length sniff —
        // see MeasureTextPx on why this file does not estimate), and give every wide row on the
        // screen ONE shared font size solved across all of them.

        /// <summary>Clear air between a wide row's two text columns, reference px.</summary>
        private const float WideRowGutterPx      = 24f;
        /// <summary>The amount column is drawn BOLD (BuildSpoilRow), which measures wider than the
        /// regular face <see cref="MeasureTextPx"/> samples. Same idea as CtaBoldWidthFactor,
        /// smaller because the row face carries no extra character spacing.</summary>
        private const float WideAmountBoldFactor = 1.08f;
        /// <summary>Bounds on a wide row's LABEL share of the plate's text region. An unbounded
        /// proportional split lets one nearly-empty cell swallow the plate, which reads as a
        /// broken row just as surely as an ellipsis does.</summary>
        private const float WideLabelMinShare    = 0.28f;
        private const float WideLabelMaxShare    = 0.74f;

        // ── COMPACT BANNER GEOMETRY (the wave-clear / outpost variant) ────────────
        /// <summary>Compact body-well TOP as a fraction of the banner panel — pulled below the
        /// tall splash header band so the headline and the content can never overlap.</summary>
        private const float CompactBodyTopY = 0.785f;
        /// <summary>Compact SPLASH header band, bottom edge (fraction of the banner panel). Named
        /// so <see cref="SeatTitleInsideFrame"/> and the compact geometry branch read the SAME
        /// number — it was a bare literal in one place, which is the duplicated-state class this
        /// file keeps paying for.</summary>
        private const float CompactHeaderY0 = 0.800f;
        /// <summary>Compact body-well FLOOR. This is FrameCore's OWN art-measured well floor
        /// (ElarionUiKit ZonesFor, case FrameCore: z.body = (0.055, 0.075, 0.945, 0.835)) — it
        /// clears the frame's ornate bottom border. WO-952: the owned compact solve reclaims
        /// down to this floor on EVERY compact banner — a CTA-carrying banner seats the CTA in
        /// its own band ON this floor instead of keeping the kit's dead close-band reservation
        /// (which is what left a 249px well for 276px of rows, the captured defect).</summary>
        private const float CompactBodyFloorY = 0.075f;

        // ── WO-952 COMPACT FRAME CONSTANTS — single source for Show AND the owned solve
        //    (the capture proved these numbers were living in two places: Show's literal
        //    anchors and the growth block's literal 0.08 clamp; a solve that must invert
        //    the layout law needs them named once). ──────────────────────────────────────
        /// <summary>Banner left/right edges on the canvas (Show's build anchors).</summary>
        private const float CompactX0 = 0.15f;
        private const float CompactX1 = 0.85f;
        /// <summary>Banner panel width as a canvas fraction — the compact analogue of
        /// <see cref="PanelWidthFrac"/>. WO-952: the subtitle/spoils width chain used the
        /// full modal's 0.56 for the banner too, under-measuring the banner's real 0.70
        /// column by 20% and over-counting wrapped lines (need inflated for nothing).</summary>
        private const float CompactPanelWidthFrac = CompactX1 - CompactX0;   // 0.700
        /// <summary>Banner TOP edge (screen fraction) — held while the banner grows down.</summary>
        private const float CompactTopY = 0.86f;
        /// <summary>Row-less SPLASH bottom edge: the 0.30h build-time banner (Show).</summary>
        private const float CompactSplashBottomY = 0.56f;
        /// <summary>The grown banner's bottom-edge floor (the pre-existing growth clamp:
        /// the world stays visible below the banner, so it may span at most
        /// <see cref="CompactTopY"/> minus this of the screen = 0.78h).</summary>
        private const float CompactGrowthFloorY = 0.00f;
        /// <summary>Gap between the body well's floor and the seated banner CTA, ref px
        /// (matches the +12 the legacy footer-grow compensation used).</summary>
        private const float CompactCtaGapPx = 12f;
        // Layout groups and pixel rounding consume a few reference pixels after the
        // analytical solve. Budget that loss here so the result never enters the
        // uniform-compression fallback merely because the measured stack is 12px short.
        private const float CompactBodySafetyPx = 16f;

        /// <summary>Deterministic body-well height in reference px for the OWNED geometry path
        /// (0 = not owned; BuildBody then measures). Set by the geometry pass in Bind.</summary>
        private float _wellPx;

        /// <summary>Compact banner only: the body well's height as a fraction of the PANEL,
        /// captured once so the downward-growth block can re-solve <see cref="_wellPx"/>
        /// against the grown panel instead of re-measuring a live rect.</summary>
        private float _compactBodyFrac;

        /// <summary>WO-952: the compact banner's OWNED CTA band — canonical CTA height,
        /// seated on the frame art's measured well floor, sized against the banner's FINAL
        /// solved height. Non-null only when the owned compact solve ran for a banner that
        /// carries a CTA; the CTA build then seats the button here instead of carving a
        /// footer out of the body well (the carve is what the stale close-band reservation
        /// used to collide with).</summary>
        private RectTransform _compactCtaBand;

        /// <summary>Content-sized panel HALF-height (fraction of screen), solved in REAL PIXELS.
        /// The old body of this method summed fictional "units" (2.4 for an emblem, 1.1 per
        /// subtitle line...) and multiplied by 0.021 — a number with no relationship to the
        /// pixel-sized bands BuildBody actually lays out, which is why the panel was always the
        /// wrong size and needed the post-hoc extension that desynchronised every fraction.
        /// Invert the real layout law instead: wellPx = BodyFracOfPanel * panelPx - CanonCtaHeight,
        /// so panelPx = (RequiredBodyPx + CanonCtaHeight) / BodyFracOfPanel.</summary>
        private static bool HasModalRepair(EndStateVM vm) => vm != null && !vm.Compact
            && !string.IsNullOrEmpty(vm.PrimaryLabel) && !string.IsNullOrEmpty(vm.CtaLabel);

        private static bool StackModalActions(EndStateVM vm, float canvasH) => HasModalRepair(vm)
            && SpoilsBodyWidthPx(canvasH, PanelWidthFracFor(vm)) * CtaMaxWidthOfBodyFrac
               < ModalActionMinWidth(vm.PrimaryLabel) + ModalActionMinWidth(vm.CtaLabel) + CompactCtaGapPx;

        private const float ModalActionPaintInsetPx = 48f;
        private static TMPro.TMP_FontAsset _actionFont;
        private static float ModalActionMinWidth(string text)
        {
            // The skin uses uppercase Merriweather Title, bold, at a 30..44px floor/ceiling.
            // Reserve against that actual font, rather than splitting the footer equally
            // and wrapping two lines across an ornate face painted for one caption.
            string caption = (text ?? string.Empty).ToUpperInvariant();
            if (_actionFont == null)
                _actionFont = Resources.Load<TMPro.TMP_FontAsset>(RpgUiCatalog.FontRoot + RpgUiCatalog.FontTitleAsset);
            float labelWidth = caption.Length * 30f; // conservative if authored font is absent
            if (_actionFont != null && _actionFont.faceInfo.pointSize > 0f)
            {
                float advance = 0f;
                bool complete = true;
                foreach (char c in caption)
                {
                    if (!_actionFont.characterLookupTable.TryGetValue(c, out var ch) || ch?.glyph == null)
                    { complete = false; break; }
                    advance += ch.glyph.metrics.horizontalAdvance;
                }
                if (complete)
                {
                    float scale = 30f / _actionFont.faceInfo.pointSize * _actionFont.faceInfo.scale;
                    labelWidth = (advance * (1f + _actionFont.boldSpacing * 0.01f)
                                  + 2f * Mathf.Max(0, caption.Length - 1)) * scale;
                }
            }
            return Mathf.Max(ElarionUiKit.CanonCtaWidth, labelWidth / CtaLabelInsetFrac + 2f * ModalActionPaintInsetPx);
        }

        private static float ActionBandPx(EndStateVM vm, float canvasH) =>
            StackModalActions(vm, canvasH)
                ? 2f * ElarionUiKit.CanonCtaHeight + CompactCtaGapPx
                : ElarionUiKit.CanonCtaHeight;

        private static float PanelHalfHeight(EndStateVM vm, float canvasH)
        {
            if (canvasH < 100f) canvasH = 1920f;   // headless / no scaler — the kit's own fallback
            float panelPx = (RequiredBodyPx(vm, canvasH) + ActionBandPx(vm, canvasH)) / BodyFracOfPanel;
            return Mathf.Clamp(panelPx / (2f * canvasH), MinPanelHalf, MaxPanelHalf);
        }

        // ── SUBTITLE MEASUREMENT (WO-894, orchestrator ruling) ────────────────────────
        // This used to be `seg.Length / 36f` — a FIXED chars-per-line tuned for the portrait
        // panel. 36 chars at FontBody 50 implies a ~900px text column, which is roughly right
        // for the 2670x1200 landscape well (~985px) and nearly DOUBLE the portrait well
        // (~495px). So the same constant over-reserved on the raid victory (the 4-line clamp,
        // 240px) and under-reserved in portrait. Both the WIDTH and the TEXT are now measured.

        /// <summary>Post-scale canvas WIDTH, in the SAME reference-px space as
        /// <see cref="ElarionUiKit.PostScaleCanvasHeight"/>. The CanvasScaler divides BOTH axes
        /// by one scaleFactor, so the post-scale canvas keeps the screen's aspect and the width
        /// is simply height x aspect. DERIVED rather than measured for exactly the reason the
        /// height is (ElarionUiKit.cs:1014-1018): a live rect read on the creation frame returns
        /// RAW SCREEN pixels.</summary>
        private static float PostScaleCanvasWidth(float canvasH)
        {
            // SurfaceWidth/Height, not Screen.* — identical at runtime (no override); a capture
            // drives them so this build-time width resolves the TARGET aspect, not the editor's.
            float sw = ElarionUiKit.SurfaceWidth, sh = ElarionUiKit.SurfaceHeight;
            if (sw < 1f || sh < 1f) return canvasH * (1080f / 1920f);   // headless: kit portrait reference
            return canvasH * (sw / sh);
        }

        // The deterministic width chain down to the subtitle's own text column. Every link is a
        // constant already in the tree, cited so a future reader can re-verify without measuring:
        //   panel      x 0.14..0.86 of the canvas        (Show, above — F8 2026-09-02)
        //   body zone  x 0.055..0.945 of the panel       (ElarionUiKit ZonesFor, case FrameCore:
        //                                                 z.body = (0.055, 0.075, 0.945, 0.835))
        //   subtitle   x 0.04..0.96 of its band          (BuildBody, below)
        // ⛔ THIS MUST TRACK Show's ANCHORS. It is the ONE number the subtitle/spoils width chain
        // measures against; if it says 0.56 while the panel is built at 0.72 the solve over-counts
        // wrapped lines and the columns are mis-sized (that desync is what WO-952 fixed for the
        // banner). Widened with the panel on 2026-09-02.
        private const float PanelWidthFrac    = 0.86f - 0.14f;     // 0.720
        private const float BodyZoneWidthFrac = 0.945f - 0.055f;   // 0.890 (FrameCore)
        private const float SubtitleInsetFrac = 0.96f - 0.04f;     // 0.920

        /// <summary>The panel-width fraction the width chain must use for THIS screen.
        /// WO-952: the banner spans 0.70 of the canvas (Show: 0.15..0.85) but the chain
        /// always used the full modal's 0.56 — under-measuring the compact subtitle column
        /// by 20%, over-counting wrapped lines and inflating the banner's need.</summary>
        private static float PanelWidthFracFor(EndStateVM vm)
        {
            return vm != null && vm.Compact ? CompactPanelWidthFrac : PanelWidthFrac;
        }

        /// <summary>Reference px of text column the subtitle actually gets.
        /// <paramref name="panelWidthFrac"/> = this screen's canvas-width fraction
        /// (<see cref="PanelWidthFracFor"/>) — WO-952: never assume the modal's.</summary>
        private static float SubtitleWidthPx(float canvasH, float panelWidthFrac)
        {
            return PostScaleCanvasWidth(canvasH) * panelWidthFrac * BodyZoneWidthFrac * SubtitleInsetFrac;
        }

        /// <summary>WRAPPED line count for the subtitle at FontBody inside the REAL body-well
        /// width. Explicit '\n' segments each wrap independently. Drives the band height so a
        /// multi-line death message gets a band tall enough to hold it (F8 flag_04: a one-line
        /// band let the text spill over the emblem above and the CTA below). Clamped 1..4.</summary>
        private static int SubtitleLines(string subtitle, float canvasH, float panelWidthFrac)
        {
            if (string.IsNullOrEmpty(subtitle)) return 0;
            float columnPx = Mathf.Max(1f, SubtitleWidthPx(canvasH, panelWidthFrac));
            int lines = 0;
            foreach (var seg in subtitle.Split('\n'))
                lines += Mathf.Max(1, Mathf.CeilToInt(MeasureTextPx(seg, ElarionUi.FontBody) / columnPx));
            return Mathf.Clamp(lines, 1, 4);
        }

        private static TMPro.TMP_FontAsset _bodyFont;
        private static bool _bodyFontTried;

        /// <summary>Rendered width of <paramref name="text"/> at <paramref name="fontSize"/> in
        /// reference px, summed from the BODY FONT'S OWN GLYPH ADVANCES — the same numbers TMP
        /// lays the text out with. A MEASUREMENT, not a character estimate: it cannot drift when
        /// the copy changes (which is precisely how the fixed "36 chars/line" went wrong).
        ///
        /// Falls back to a 0.5em average ONLY if the font asset is absent or its character table
        /// is unpopulated (a dynamic atlas before anything has been rendered) — detected by how
        /// much of the string actually resolved, never assumed. Even that fallback is derived
        /// from the real font SIZE and applied against the real column width, so it is still
        /// geometry-aware. Kerning is ignored (sub-1% on Latin copy at this size).</summary>
        private static float MeasureTextPx(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            if (!_bodyFontTried)
            {
                _bodyFontTried = true;
                try
                {
                    _bodyFont = Resources.Load<TMPro.TMP_FontAsset>(
                        RpgUiCatalog.FontRoot + RpgUiCatalog.FontBodyAsset);
                }
                catch (Exception e)
                {
                    FlowTrace.Warn("EndState", "body font load failed for text measure: " + e.Message);
                    _bodyFont = null;
                }
            }

            var fa = _bodyFont;
            if (fa != null && fa.faceInfo.pointSize > 0f)
            {
                float advance = 0f;
                int matched = 0;
                try
                {
                    var table = fa.characterLookupTable;
                    if (table != null)
                    {
                        for (int i = 0; i < text.Length; i++)
                        {
                            if (table.TryGetValue(text[i], out var ch) && ch != null && ch.glyph != null)
                            {
                                advance += ch.glyph.metrics.horizontalAdvance;
                                matched++;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // §12 no silent failure: say the metric path failed, then use the estimate.
                    FlowTrace.Throttle("EndState", "measure-fallback", 10f,
                        "glyph-advance measure failed, using the em estimate: " + e.Message);
                    matched = 0;
                }
                // Only trust the sum when most of the string really resolved — an empty/partial
                // table would otherwise measure a long line as nearly zero and under-reserve.
                if (matched >= Mathf.CeilToInt(text.Length * 0.6f))
                    return advance * (fontSize / fa.faceInfo.pointSize) * fa.faceInfo.scale;
            }

            return text.Length * fontSize * 0.5f;   // ~0.5em average advance for Latin copy
        }

        // ── binding ───────────────────────────────────────────────────────────

        /// <summary>Post-scale canvas height for THIS screen, captured once in <see cref="Bind"/>.
        /// The subtitle's wrapped line count is measured against the real column width, which is
        /// derived from this — so band sizing and the panel solve use the SAME number.</summary>
        private float _canvasH;

        private void Bind(EndStateVM vm, ElarionUiKit.PanelChrome chrome)
        {
            _vm = vm;
            _canvasH = ElarionUiKit.PostScaleCanvasHeight(
                chrome.root != null ? chrome.root.transform : transform);

            // ── SPLASH TITLE HEADER BAND (F8 2026-07-08: "Wave 1 Cleared!" title rendered
            //    0 visible glyphs) ────────────────────────────────────────────────────────
            // FrameCore's stock header band is only ~0.072 of the panel — the captured title
            // rect was 906x16px, far too SHORT to seat even the kit's 20px FontHardFloor, so
            // UiKitTextFitGuard culled the whole title (0 glyphs). This is the same too-short-
            // band class as the DialogueView header fix, but the EndState panel is ALSO
            // FrameCore-based and the earlier fix didn't reach here. An end-state is a
            // victory / defeat / wave-clear SPLASH — so grow the header into a TALL top band
            // and let a BIG headline (up to FontTitle=88) render, then pull the body top below
            // the band so title and content can never overlap. These are THIS panel's OWN
            // per-instance zones (Zone() mints a fresh RectTransform for each panel), so no
            // other FrameCore screen is affected. Anchors are fractions of panel height, so the
            // splash scales with the panel; the title authors up to 88 and FitSingleLine already
            // bounds it — we only give it ROOM, never shrink the font.
            //
            // OWNED GEOMETRY PASS (full modal only). One place stamps header / CTA band / body
            // well, all against the SAME measured panel height, BEFORE anything is built into
            // them. Replaces the three desynchronised reservations the owner felt as compaction.
            bool ownGeometry = !vm.Compact && chrome.layout != null
                               && chrome.layout.header != null
                               && chrome.layout.body != null
                               && chrome.layout.footer != null
                               && chrome.root != null;
            if (ownGeometry)
            {
                // Panel height the DETERMINISTIC way (ElarionUiKit.cs:1014-1018): reading a live
                // rect on the canvas's creation frame returns RAW SCREEN pixels because the
                // CanvasScaler has not applied yet. PostScaleCanvasHeight x the panel's own anchor
                // span gives the height the fraction anchors will really resolve against.
                var rootRt = (RectTransform)chrome.root.transform;
                float panelFracH = Mathf.Max(0.05f, rootRt.anchorMax.y - rootRt.anchorMin.y);
                float panelPx = _canvasH * panelFracH;
                float ctaBandH = ActionBandPx(vm, _canvasH) / Mathf.Max(1f, panelPx);
                float bodyFloor = CtaBandY0 + ctaBandH + CtaGapY;

                // Header: 0.760-0.985 was ~0.225 of the panel — far more than ONE FontTitle(88)
                // line needs, and every px of it came out of the body.
                var hdr = chrome.layout.header;
                hdr.anchorMin = new Vector2(hdr.anchorMin.x, HeaderY0);
                hdr.anchorMax = new Vector2(hdr.anchorMax.x, HeaderY1);
                hdr.offsetMin = new Vector2(hdr.offsetMin.x, 0f);
                hdr.offsetMax = new Vector2(hdr.offsetMax.x, 0f);

                // RECLAIM THE DEAD CLOSE BAND (the recipe already merged for
                // FoundingChoiceController.cs:177-188). The kit reserves room at the bottom of
                // EVERY framed panel for the ONE shared Close (BuildObsidianPanel's close-band
                // reservation, ElarionUiKit.cs:582-647): it relocates the footer band ABOVE the
                // Close box and raises the body floor above that. THIS screen HIDES the Close
                // (owner button law — one way out; see Show), so the whole reservation is dead
                // space. Seat the CTA in the freed band and drop the body floor onto it.
                // VALID ONLY BECAUSE THE CLOSE IS HIDDEN — do not re-enable the Close without
                // reverting this pass.
                var ftr = chrome.layout.footer;
                ftr.anchorMin = new Vector2(ftr.anchorMin.x, CtaBandY0);
                ftr.anchorMax = new Vector2(ftr.anchorMax.x, CtaBandY0 + ctaBandH);
                ftr.offsetMin = new Vector2(ftr.offsetMin.x, 0f);
                ftr.offsetMax = new Vector2(ftr.offsetMax.x, 0f);

                var bdy = chrome.layout.body;
                bdy.anchorMin = new Vector2(bdy.anchorMin.x, bodyFloor);
                bdy.anchorMax = new Vector2(bdy.anchorMax.x, BodyTopY);
                bdy.offsetMin = new Vector2(bdy.offsetMin.x, 0f);
                bdy.offsetMax = new Vector2(bdy.offsetMax.x, 0f);

                // The well height is now KNOWN in reference px — hand it to BuildBody instead of
                // letting it re-measure a creation-frame rect.
                _wellPx = (BodyTopY - bodyFloor) * panelPx;
                Canvas.ForceUpdateCanvases();
                FlowTrace.Step("EndState",
                    $"geometry: panel={panelPx:0}px (frac {panelFracH:0.###}) header {HeaderY0:0.###}-{HeaderY1:0.###} " +
                    $"body {bodyFloor:0.###}-{BodyTopY:0.###} = {_wellPx:0}px cta band {CtaBandY0:0.###}-{CtaBandY0 + ctaBandH:0.###} " +
                    $"need={RequiredBodyPx(vm, _canvasH):0}px " +
                    $"(subtitle column {SubtitleWidthPx(_canvasH, PanelWidthFracFor(vm)):0}px -> {SubtitleLines(vm.Subtitle, _canvasH, PanelWidthFracFor(vm))} line(s))");
            }
            else if (chrome.layout != null && chrome.layout.header != null)
            {
                // Compact banner (and any layout without a footer zone): unchanged splash header.
                var hdr = chrome.layout.header;
                hdr.anchorMin = new Vector2(hdr.anchorMin.x, CompactHeaderY0);   // body now owns the reclaimed band below
                hdr.anchorMax = new Vector2(hdr.anchorMax.x, 0.985f);   // was ~0.972
                if (chrome.layout.body != null && chrome.layout.body.anchorMax.y > CompactBodyTopY)
                    chrome.layout.body.anchorMax =                       // body top clears the band
                        new Vector2(chrome.layout.body.anchorMax.x, CompactBodyTopY);

                // ── WO-952 OWNED COMPACT GEOMETRY (F8 capture 2026-08-10, twice: "need=276px
                //    well=249px scale=0.9") — the full modal's 2026-08-05 lesson applied to
                //    the banner ─────────────────────────────────────────────────────────────
                // WHAT WENT WRONG: the old pass reclaimed the kit's dead close-band reservation
                // ONLY when the banner carried no CTA at all. A WO-672 Repair-All banner kept
                // the reservation's 0.45 body floor — computed against the 0.30h SPLASH panel —
                // and the later downward growth scaled that stale fraction up with the panel:
                // at the growth clamp on a 16:9 desktop (1080 ref-px canvas, 842px panel),
                // 0.45 x 842 = 379px sat below the body well for a 132px button, leaving a
                // 0.295 x 842 = 249px well for 276px of rows -> uniform 0.9 compression, every
                // band below its own content size. Exactly the captured numbers.
                //
                // THE FIX IS REFLOW, NOT SHRINK: solve the banner's FINAL height up front from
                // the content it must seat (need + the canonical CTA band when one is carried),
                // stamp every band against that ONE height, and seat the CTA in its OWN bottom
                // band on the frame art's measured well floor (ZonesFor FrameCore z.body.y =
                // 0.075). The close-band reclaim therefore now runs on EVERY compact banner —
                // CTA-shaped instead of gated off — and nothing is stamped before the height it
                // is a fraction of is known, so nothing can desynchronise (the exact recipe
                // that fixed the full modal's compaction on 2026-08-05).
                //
                // Clamps: never below the 0.30h splash (a row-less banner is unchanged), never
                // past the growth floor (the world below stays visible). The growth floor is
                // the ONE remaining compression source; BuildBody's Fail net still names it
                // when it bites — the net stays, it caught this.
                bool compactAnyCta = vm.Compact
                                     && (!string.IsNullOrEmpty(vm.PrimaryLabel)
                                         || !string.IsNullOrEmpty(vm.CtaLabel));
                if (vm.Compact && chrome.layout.body != null && chrome.root != null)
                {
                    var bdy = chrome.layout.body;
                    var rootRt0 = (RectTransform)chrome.root.transform;
                    float topY = rootRt0.anchorMax.y;                            // splash top, held
                    float hNow = Mathf.Max(0.05f, topY - rootRt0.anchorMin.y);   // the 0.30h splash
                    float needPx = RequiredBodyPx(vm, _canvasH);
                    float ctaPx = compactAnyCta
                        ? ElarionUiKit.CanonCtaHeight + CompactCtaGapPx : 0f;
                    // Invert the layout law (the PanelHalfHeight recipe): the body well is
                    // (CompactBodyTopY - CompactBodyFloorY) of the panel minus the CTA band's
                    // pixels, so panelPx = (need + ctaBand) / that fraction.
                    float solvedPx = (needPx + CompactBodySafetyPx + ctaPx)
                                     / (CompactBodyTopY - CompactBodyFloorY);
                    float panelFrac = Mathf.Clamp(solvedPx / Mathf.Max(1f, _canvasH),
                                                  hNow, topY - CompactGrowthFloorY);
                    rootRt0.anchorMin = new Vector2(rootRt0.anchorMin.x, topY - panelFrac);
                    float panelPx = panelFrac * _canvasH;

                    float bodyFloor = CompactBodyFloorY + ctaPx / Mathf.Max(1f, panelPx);
                    bdy.anchorMin = new Vector2(bdy.anchorMin.x, bodyFloor);
                    bdy.anchorMax = new Vector2(bdy.anchorMax.x, CompactBodyTopY);
                    bdy.offsetMin = new Vector2(bdy.offsetMin.x, 0f);
                    bdy.offsetMax = new Vector2(bdy.offsetMax.x, 0f);

                    if (compactAnyCta)
                    {
                        // The CTA's OWN band: canonical height, seated on the art floor, its
                        // fraction computed from the FINAL panel px so PinCanonicalCtaSize's
                        // fixed 132px box fills it exactly. Parented beside the body zone so
                        // both resolve in the same (panel-fraction) space. The CTA build below
                        // seats the button here instead of carving the body well.
                        _compactCtaBand = MakeZone(bdy.parent, "Zone_CompactCta",
                            0.10f, CompactBodyFloorY,
                            0.90f, CompactBodyFloorY
                                   + ElarionUiKit.CanonCtaHeight / Mathf.Max(1f, panelPx));
                    }

                    _compactBodyFrac = Mathf.Max(0.01f, CompactBodyTopY - bodyFloor);
                    _wellPx = _compactBodyFrac * panelPx;
                    Canvas.ForceUpdateCanvases();
                    FlowTrace.Step("EndState",
                        $"compact banner geometry (WO-952 owned solve): panel={panelPx:0}px " +
                        $"(frac {panelFrac:0.###}, splash was {hNow:0.###}) body {bodyFloor:0.###}-" +
                        $"{CompactBodyTopY:0.###} = {_wellPx:0}px well, need={needPx:0}px, " +
                        $"ctaBand={(compactAnyCta ? ElarionUiKit.CanonCtaHeight : 0f):0}px" +
                        (panelFrac >= topY - CompactGrowthFloorY - 0.0005f
                            ? " (AT THE GROWTH CLAMP)" : string.Empty));
                }
            }

            // Drop-zones (sprite-first contract: layout is null on the procedural
            // fallback panel — mirror the default zone fractions on the content).
            RectTransform well   = chrome.layout != null ? chrome.layout.body
                                 : MakeZone(chrome.content.transform, "Zone_Body",   0.06f, 0.10f, 0.94f, 0.875f);

            // The Continue button owns its OWN footer band (R4: it was overlapping the last reward
            // row, "Iron +8"). FrameCore carries NO footer drop-zone (ElarionUiKit.ZonesFor leaves
            // hasFooter=false for FrameCore, ElarionUiKit.cs:365-373), and the old raw-fraction
            // fallback footer (panel y .030–.095) OVERLAPPED the body well's base (y .075) — that
            // overlap is what pushed the button onto the last row. So when there is no real footer
            // drop-zone, carve the button's band out of the BOTTOM of the body well and hand
            // BuildBody only the reward well ABOVE it, leaving a guaranteed gap between the two.
            bool hasFooterZone = chrome.layout != null && chrome.layout.footer != null;
            // F8-43: compact banners carry NO primary CTA (VM sets PrimaryLabel null/empty)
            // — they auto-dismiss in seconds, so a Continue button is a redundant control
            // (owner one-action law). No CTA => no footer band; the reward well owns the
            // whole body and the exit is auto-dismiss + tap-anywhere (wired below).
            bool hasCta = !string.IsNullOrEmpty(vm.PrimaryLabel);
            // WO-672 Slice E: the ONE case the compact banner's CTA seat returns — a
            // VM-supplied banner CTA ("Repair All - N crystals" on the wave damage
            // report). It is BUTTON-ONLY and distinct from Primary on purpose:
            // tap-anywhere + auto-dismiss keep funnelling FirePrimary (dismiss), so
            // neither can ever silently fire the crystal spend.
            bool hasBannerCta = !hasCta && vm.Compact && !string.IsNullOrEmpty(vm.CtaLabel);
            bool anyCta = hasCta || hasBannerCta;
            // WO-952: the owned compact solve minted a dedicated CTA band below the body
            // well — use it. FrameCore has no footer zone, so the legacy carve stole 16% of
            // the body well the solve had just sized to EXACTLY the content's need.
            RectTransform footer     = !anyCta ? null
                                     : _compactCtaBand != null ? _compactCtaBand
                                     : hasFooterZone ? chrome.layout.footer
                                     : MakeZone(well, "Zone_Footer",     0.10f, 0f,    0.90f, 0.16f);
            // VICTORY SWEEP (fresh 1280x720 capture, 2026-07-06: "Wood +15" / "Iron +8" ran
            // BEHIND Continue): on the REAL-footer path (FrameCore relocates its default
            // footer band above the Close) the reward well was the WHOLE body well while the
            // law-pinned canonical CTA — 120 units tall vs the ~50-unit footer band it is
            // centred in — spills UP into the body over the last reward rows. Same zone-flow
            // discipline as the death-panel fix (#22 below): the reward well is ALWAYS its
            // own zone, and on the real-footer path its floor is raised above the pinned
            // CTA's measured top edge so rewards and Continue can never share pixels.
            RectTransform rewardWell = MakeZone(well, "Zone_RewardWell",
                0f, (hasFooterZone || !anyCta || _compactCtaBand != null) ? 0f : 0.22f, 1f, 1f);

            // ONE primary action (Continue / Rise again / ...) — built FIRST so the reward
            // well can be sized around the law-pinned CTA; it still lands LAST in the reveal.
            // F8-43: skipped entirely when the VM carries no PrimaryLabel (compact banners)
            // — no button, no footer carving; the banner's exit is auto-dismiss + tap-anywhere.
            Button btn = null;
            if (anyCta)
            {
                // WO-672: the banner CTA fires FireCta (the VM action + dismiss); the
                // primary CTA keeps firing FirePrimary. Same seat, same canonical size.
                btn = ElarionUiKit.Button(footer, hasCta ? vm.PrimaryLabel : vm.CtaLabel,
                    ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.24f, 0.06f), new Vector2(0.76f, 0.94f),
                    hasCta ? (Action)FirePrimary : FireCta);
                // Unaffordable Repair-All renders DISABLED but still shows the cost
                // (informative, not dead — owner law; state carried by the disabled
                // interaction + greyed kit visuals, never color alone).
                if (hasBannerCta) btn.interactable = vm.CtaEnabled;
                MedievalUiSkin.ApplyButton(btn, primary: hasCta);
                // OWNER F8 x3: the Continue/primary action is the SAME pixel size on every
                // screen (matches the shared Close). The anchors above only centre it in the
                // footer band; the canonical size is stamped here.
                ElarionUiKit.PinCanonicalCtaSize(btn);
                // ── THE CTA SIZES TO ITS OWN WORDS (owner F8 2026-09-02) ──────────────────
                // CAPTURED DEFECT: the primary CTA read "PREPARE FOR W...". PinCanonicalCtaSize
                // stamps a FIXED CanonCtaWidth (360 ref px) box, and the kit fits the button
                // label with FitSingleLine -> TextOverflowModes.Ellipsis. "Prepare for Wave 2"
                // is simply wider than 360 px of face, so the ONE control the player is meant
                // to press could not state what it does. The canonical size exists so buttons
                // never drift SMALLER or inconsistent between screens; it was never a licence
                // to amputate the label — so treat 360 as a FLOOR and grow to the measured
                // words. Height stays exactly canonical (132 >= MinTouchPx 112), so the
                // one-handed thumb target and the cross-screen height match are untouched.
                // The banner CTA keeps its own 680 floor (a "Repair All - 40 wood, 12 iron"
                // string was already known not to fit).
                {
                    var ctaRect = (RectTransform)btn.transform;
                    string ctaText = hasCta ? vm.PrimaryLabel : vm.CtaLabel;
                    // Measured from the body font's real glyph advances (the same measurement
                    // the subtitle wrap uses), + BoldWidthFactor for the bold face and the kit's
                    // 1px character spacing, + the label's own 0.04/0.96 inset and the framed
                    // face's shoulders. A character-count estimate is what this file already
                    // learned not to trust (see MeasureTextPx).
                    float labelPx = MeasureTextPx(ctaText, ElarionUi.FontBody) * CtaBoldWidthFactor;
                    float wantPx = labelPx / CtaLabelInsetFrac + CtaFacePaddingPx;
                    // Never wider than the panel's own body column can seat.
                    float maxPx = PostScaleCanvasWidth(_canvasH) * PanelWidthFracFor(vm)
                                  * BodyZoneWidthFrac * CtaMaxWidthOfBodyFrac;
                    float floorPx = hasBannerCta ? BannerCtaMinWidthPx : ElarionUiKit.CanonCtaWidth;
                    float ctaW = Mathf.Clamp(wantPx, floorPx, Mathf.Max(floorPx, maxPx));
                    ctaRect.sizeDelta = new Vector2(ctaW, ctaRect.sizeDelta.y);
                    // Re-arm the fitter against the NEW box: FitSingleLine's autosize bounds
                    // were computed against the 360px rect.
                    var ctaLabel = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                    if (ctaLabel != null) ElarionUiKit.FitSingleLine(ctaLabel);
                    FlowTrace.Step("EndState",
                        $"CTA '{ctaText}' sized to content: label={labelPx:0}px -> box {ctaW:0}x{ctaRect.sizeDelta.y:0}px " +
                        $"(floor {floorPx:0}, max {maxPx:0})" +
                        (wantPx > maxPx ? " - CLAMPED to the body column, the label may still ellipsise" : string.Empty));
                }
                Canvas.ForceUpdateCanvases();
                var bRt = (RectTransform)btn.transform;
                // Robust CTA height: measured rect, else the pinned sizeDelta, floored at the
                // canonical constant — a 0 here silently disabled the band growth entirely.
                float need = Mathf.Max(bRt.rect.height,
                    Mathf.Max(bRt.sizeDelta.y, ElarionUiKit.CanonCtaHeight));
                // #22 (capture 9403, "YOU HAVE FALLEN" strip): on SHORT panels the law-pinned
                // canonical CTA is TALLER than the carved footer band, so the centred button
                // spilled UP over the body copy ("Try Again" on top of the death message). The
                // CTA size is law — so the BAND must grow to contain it: when the pinned button
                // exceeds the footer band, raise the band's top and lift the reward well above
                // it (gap preserved).
                // OWNED GEOMETRY: the CTA band above was sized to EXACTLY CanonCtaHeight and the
                // body floor already sits CtaGapY above its top, so neither compensation can have
                // anything to do — and both of them re-derive fractions from creation-frame rects,
                // which is what made the reservations drift apart in the first place. Skip them.
                if (ownGeometry || _compactCtaBand != null)
                {
                    // WO-952: the compact owned band is sized to EXACTLY CanonCtaHeight against
                    // the final panel, same as the full modal's reclaimed close band — both
                    // compensations below re-derive fractions from creation-frame rects, which
                    // is the desync class this pass exists to end. Skip them.
                    FlowTrace.Step("EndState",
                        $"CTA seated in the {(ownGeometry ? "reclaimed close band" : "owned compact CTA band (WO-952)")} " +
                        $"(need={need:0}px, band=={ElarionUiKit.CanonCtaHeight:0}px) - no floor-raise required");
                }
                else if (!hasFooterZone)
                {
                    float wellH = well.rect.height;
                    if (wellH > 1f && need > footer.rect.height - 4f)
                    {
                        float frac = Mathf.Clamp01((need + 12f) / wellH);
                        footer.anchorMax = new Vector2(footer.anchorMax.x, frac);
                        rewardWell.anchorMin = new Vector2(rewardWell.anchorMin.x, Mathf.Min(0.95f, frac + 0.04f));
                        FlowTrace.Step("EndState",
                            $"footer band grown to contain the canonical CTA (need={need:0}px, well={wellH:0}px, band->{frac:0.###})");
                    }
                }
                else
                {
                    // Real footer drop-zone (FrameCore): the footer band cannot grow (it is the
                    // frame's zone), so instead lift the reward well's FLOOR above the CTA's top
                    // edge. footer/well anchors are both fractions of the panel content.
                    var contentRt = chrome.content != null ? chrome.content.GetComponent<RectTransform>() : null;
                    float panelH = contentRt != null ? contentRt.rect.height : 0f;
                    float footerCentre = (footer.anchorMin.y + footer.anchorMax.y) * 0.5f;
                    float ctaTop = panelH > 1f ? footerCentre + (need * 0.5f) / panelH
                                               : footerCentre + 0.12f;   // conservative unmeasured fallback
                    float wellMin = well.anchorMin.y, wellMax = well.anchorMax.y;
                    float floor = Mathf.Clamp01((ctaTop + 0.02f - wellMin)
                                                / Mathf.Max(0.05f, wellMax - wellMin));
                    if (floor > 0f)
                    {
                        rewardWell.anchorMin = new Vector2(rewardWell.anchorMin.x, Mathf.Min(0.5f, floor));
                        FlowTrace.Step("EndState",
                            $"reward well floor raised above the canonical CTA (ctaTop={ctaTop:0.###}, well {wellMin:0.###}-{wellMax:0.###}, floor->{Mathf.Min(0.5f, floor):0.###})");
                    }
                }
            }
            // Full wave results retain their Prepare action AND the explicitly priced repair.
            // Share a row where two canonical targets fit; narrow surfaces reserve a second
            // row in the SAME panel/body solve above. Captions stay single-line on the painted face.
            Button repairBtn = null;
            if (HasModalRepair(vm) && btn != null)
            {
                repairBtn = ElarionUiKit.Button(footer, vm.CtaLabel,
                    ElarionUiKit.ButtonKind.Gold, Vector2.zero, Vector2.one, FireCta);
                repairBtn.interactable = vm.CtaEnabled;
                MedievalUiSkin.ApplyButton(repairBtn, primary: false);
                ElarionUiKit.PinCanonicalCtaSize(repairBtn);
                btn.name = "PrimaryAction";
                repairBtn.name = "RepairAction";
                bool stacked = StackModalActions(vm, _canvasH);
                // A stacked action owns the whole body column. Applying the paired-row
                // 0.92 inset again reduced portrait to 637px and clipped the full price at
                // the 30px floor. The body already clears the frame's 0.055..0.945 edges;
                // the label retains its own 0.04..0.96 inset inside the button.
                float room = SpoilsBodyWidthPx(_canvasH, PanelWidthFracFor(vm))
                             * (stacked ? 1f : CtaMaxWidthOfBodyFrac);
                float repairWidth = ModalActionMinWidth(vm.CtaLabel);
                float primaryWidth = ModalActionMinWidth(vm.PrimaryLabel);
                float spare = Mathf.Max(0f, room - CompactCtaGapPx - repairWidth - primaryWidth);
                var widths = stacked ? new[] { room, room }
                    : new[] { repairWidth + spare * 0.5f, primaryWidth + spare * 0.5f };
                var actions = new[] { repairBtn, btn };
                for (int i = 0; i < actions.Length; i++)
                {
                    var rt = (RectTransform)actions[i].transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(widths[i], ElarionUiKit.CanonCtaHeight);
                    rt.anchoredPosition = stacked
                        ? new Vector2(0f, (0.5f - i) * (ElarionUiKit.CanonCtaHeight + CompactCtaGapPx))
                        : new Vector2(i == 0 ? -room * 0.5f + widths[0] * 0.5f
                                              : room * 0.5f - widths[1] * 0.5f, 0f);
                    var label = actions[i].GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                    if (label != null)
                    {
                        if (!stacked)
                        {
                            // The paired plate has ornamental tips outside its caption face.
                            // Width reservation AND the real text rect must respect that space.
                            var labelRt = label.rectTransform;
                            labelRt.anchorMin = new Vector2(0f, labelRt.anchorMin.y);
                            labelRt.anchorMax = new Vector2(1f, labelRt.anchorMax.y);
                            labelRt.offsetMin = new Vector2(ModalActionPaintInsetPx, labelRt.offsetMin.y);
                            labelRt.offsetMax = new Vector2(-ModalActionPaintInsetPx, labelRt.offsetMax.y);
                        }
                        ElarionUiKit.FitSingleLine(label, 30f, 44f);
                    }
                }
            }
            if (vm.Compact && !hasCta)
            {
                // F8-43: no primary CTA on the compact banner — tap-anywhere on the PANEL
                // becomes the manual dismiss (compact panels have no scrim/backdrop, so the
                // world stays interactive around the banner). AutoDismissAfter (below)
                // remains the softlock guard; both funnel FirePrimary, which latches on
                // _fired so the route still fires exactly once. When a Repair-All CTA is
                // present, the dismiss surface is scoped to the report well and cannot
                // geometrically cover the separately owned CTA band.
                var tap = new GameObject("TapDismiss", typeof(Image), typeof(Button));
                // WO-1077: a Repair-All banner keeps tap-to-dismiss over its report well,
                // but the catcher's geometry stops before the separately owned CTA band.
                // With no banner CTA the original whole-panel dismiss surface remains.
                tap.transform.SetParent(hasBannerCta ? rewardWell : chrome.root.transform, false);
                var tapRt = (RectTransform)tap.transform;
                tapRt.anchorMin = Vector2.zero; tapRt.anchorMax = Vector2.one;
                tapRt.offsetMin = Vector2.zero; tapRt.offsetMax = Vector2.zero;
                var tapImg = tap.GetComponent<Image>();
                tapImg.color = Color.clear;      // invisible; raycastTarget still catches taps
                tapImg.raycastTarget = true;
                tap.GetComponent<Button>().transition = Selectable.Transition.None;
                tap.GetComponent<Button>().onClick.AddListener(FirePrimary);
                FlowTrace.Step("EndState", hasBannerCta
                    ? "compact banner: tap-dismiss scoped to report well (Repair-All CTA excluded)"
                    : "compact banner: primary CTA suppressed (auto-dismiss/tap)");
            }

            // POST-HOC PANEL EXTENSION — DELETED for the full modal (owner F8 2026-08-05).
            // It grew the frame root ~2x AFTER the kit's close-band reservation, the header band
            // and the CTA floor-raise had all been computed against the pre-growth panel, and it
            // recomputed NONE of them. That desynchronisation IS the compressed screen: the body
            // zone kept a fraction sized for the small panel, so the well resolved to ~17% of a
            // 907px panel and BuildBody scaled every band to 0.363. The full modal is now solved
            // to its final size in Show (PanelHalfHeight) and stamped once by the owned geometry
            // pass above — there is nothing left to extend.
            //
            // The COMPACT banner's downward growth — WO-952: SUPERSEDED by the owned compact
            // solve whenever the frame zones exist (_wellPx > 1): the banner is now BUILT at
            // its final solved height, so there is nothing left to grow — and growing here
            // against fractions stamped for a different height is the exact desync the
            // 2026-08-10 capture measured (the stale 0.45 reservation scaled up with the
            // panel). This block remains ONLY for the art-less procedural fallback panel
            // (chrome.layout == null), where no solve ran and the live rect is all there is.
            if (vm.Compact && chrome.root != null && _wellPx <= 1f)
            {
                Canvas.ForceUpdateCanvases();
                // Prefer the well the reclaim pass SOLVED (reference px). Falls back to the
                // live rect only on the CTA path, where no reclaim ran — unchanged behaviour.
                float wellPx = _wellPx > 1f ? _wellPx : rewardWell.rect.height;
                float needPx = RequiredBodyPx(vm, _canvasH);
                if (wellPx > 1f && needPx > wellPx + 1f)
                {
                    var rootRt = (RectTransform)chrome.root.transform;
                    // F8-45 (wave damage report): the compact banner's fixed 0.30h frame
                    // was sized for the row-less wave-clear splash; a spoils report inside
                    // it would only uniform-compress (BuildBody's scale<1 fallback) into
                    // unreadable ~13px rows — the F8-35 class. Grow DOWNWARD (top edge
                    // held at its splash anchor) just enough for the content; the world
                    // stays non-blocked (no scrim) and a row-less banner is unchanged.
                    float y0 = rootRt.anchorMin.y, y1 = rootRt.anchorMax.y;
                    float hNow = y1 - y0;
                    float grownH = Mathf.Min(y1 - 0.08f, hNow * (needPx / wellPx));
                    if (grownH > hNow + 0.001f)
                    {
                        rootRt.anchorMin = new Vector2(rootRt.anchorMin.x, y1 - grownH);
                        Canvas.ForceUpdateCanvases();
                        // Keep the SOLVED well in step with the panel it was solved against —
                        // otherwise BuildBody would measure the content against the PRE-growth
                        // height and report a compression that no longer exists.
                        if (_wellPx > 1f && _compactBodyFrac > 0f)
                            _wellPx = _compactBodyFrac * _canvasH * grownH;
                        FlowTrace.Step("EndState",
                            $"compact banner extended down for its rows: need={needPx:0}px well {wellPx:0}->" +
                            $"{(_wellPx > 1f ? _wellPx : rewardWell.rect.height):0}px h {hNow:0.###}->{grownH:0.###}" +
                            (grownH >= y1 - 0.08f - 0.0005f ? " (AT THE GROWTH CLAMP)" : string.Empty));
                    }
                }
            }

            // The panel's FINAL size is known here (the owned solves stamped it, and the
            // procedural-fallback growth block above is the last thing that can move it), so this
            // is the only point at which the frame's fixed-pixel border can be reserved correctly.
            SeatTitleInsideFrame(chrome, vm.Compact ? CompactHeaderY0 : HeaderY0);

            BuildBody(vm, rewardWell);
            if (btn != null)   // F8-43: compact banners build no CTA
                Track(btn.gameObject, 0.25f + vm.Spoils.Count * 0.05f + 0.08f, 0.92f);

            if (repairBtn != null)
                Track(repairBtn.gameObject, 0.25f + vm.Spoils.Count * 0.05f + 0.08f, 0.92f);

            // Smooth in: whole panel fades+scales, then the staggered content.
            var rootGroup = chrome.root.GetComponent<CanvasGroup>();
            if (rootGroup == null) rootGroup = chrome.root.AddComponent<CanvasGroup>();
            rootGroup.alpha = 0f;
            StartCoroutine(RevealRoutine(rootGroup, (RectTransform)chrome.root.transform, 0f, RootRevealSec, 0.94f));
            foreach (var r in _reveals)
                StartCoroutine(RevealRoutine(r.Group, r.Rect, r.Delay, BodyRevealSec, r.FromScale));
            // ⭐ THE REVEAL-COMPLETION PROBE (owner eyewitness 2026-09-06: "it is an empty box
            // was sad"). See VerifyRevealCompleted — this is INSTRUMENTATION, not a fix: the
            // reveal path reads correct at source, so the next capture has to NAME the dead step
            // instead of another seat theorising about it (§12).
            StartCoroutine(VerifyRevealCompleted(rootGroup));

#if UNITY_EDITOR
            // Synchronous edit-mode captures cannot advance the reveal coroutines. Measure and
            // render the settled player-facing posture instead of the transient 0.94 entrance
            // frame; play-mode/runtime animation is unchanged.
            if (!Application.isPlaying)
            {
                rootGroup.alpha = 1f;
                ((RectTransform)chrome.root.transform).localScale = Vector3.one;
                foreach (var r in _reveals)
                {
                    if (r.Group != null) r.Group.alpha = 1f;
                    if (r.Rect != null) r.Rect.localScale = Vector3.one;
                }
                Canvas.ForceUpdateCanvases();
            }
#endif

            if (vm.AutoDismissSeconds > 0f)
                StartCoroutine(AutoDismissAfter(vm.AutoDismissSeconds));

            SceneManager.sceneLoaded += OnSceneLoaded;

            FlowTrace.Step("EndState",
                $"{vm.Kind} shown: spoils={vm.Spoils.Count} action={vm.PrimaryRoute}");
        }

        // ── F8-35 pixel row heights (post-scale canvas units — same space as the kit's
        // canonical 360x120 CTA and the ElarionUi font constants: FontBody=50 etc.).
        // Each band OWNS this height; bands never share pixels. If the well is still
        // shorter than the total after the panel extension hit its clamp, all bands
        // compress by one uniform factor and the labels' FitSingleLine shrink-to-fit
        // keeps the text inside its band (never overprinting a sibling).
        //
        // OWNER F8 2026-08-05: EVERY constant here except the emblem was SMALLER than the fixed
        // content it has to seat, so even at scale 1.0 the band overflowed onto its neighbour —
        // the stars printed through the Time line and the row icons hung off their plates. Each
        // one is now >= its own content's fixed size (and the emblem, which was the only
        // over-budgeted band, gives its surplus back):
        private const float EmblemPx  = 64f;   // 80 -> 64: the emblem scales, it never needed 80
        private const float SubLinePx = 60f;   // 54 -> 60: FontBody 50 line box is ~57.5px
        // WO-894 §3: 48 -> 72. The 48 was budgeted for the 45-deg DIAMOND's rotated bbox. A real
        // 5-point star is RADIALLY bounded (it rotates inside its own circumscribed circle, so the
        // bbox never grows during the spin) — the band instead has to seat the 56px hero star PLUS
        // the §4.2 overshoot: 56 x 1.15 = 64.4px at the pop's peak, which 72 clears with 7.6px spare.
        private const float StarsPx   = 72f;   // 48 -> 72: 56px hero star + the 1.15x spin overshoot
        // WO-1789 — THE STAR BAND'S CAPTION ALLOWANCE. Paid ONLY when EndStateVM.StarCaption is set
        // (a raid win short of the veterancy star count), so every screen that ships today budgets
        // and lays out at exactly StarsPx and its pixels do not move.
        //
        // WHY 40 AND NOT LESS, read at source rather than picked: FitSingleLine's default floor is
        // ElarionUiKit.FontFloor = 30 (ElarionUiKitObsidian.cs:3033), and this file's own TimePx note
        // measures FontLabel 40pt at a ~46px line box - a 1.15 ratio, so 30pt needs ~34.5px. A 34px
        // allowance would therefore clip the caption at its OWN autosize floor on every screen, not
        // just in a narrow strip cell. 40 clears 34.5 with ~5px spare and still costs less than TimePx.
        //
        // ⛔ IT IS ADDED TO THE BAND, NEVER TAKEN OUT OF THE STARS. Subdividing the existing 72px
        // would shrink the hero star to ~34px — BuildStarRow clamps to host.rect.height * 0.78 —
        // and the rating is read by the SIZE and COUNT of the shapes (the colourblind law at
        // BuildStarRow). Growing the band is budgeted by the same solve that lays it out
        // (StarsBandPx is used at EVERY site StarsPx used to be), so the caption's pixels can never
        // be discovered after layout: that desync is the class this file is a monument to.
        private const float StarCaptionPx = 40f;
        private const float TimePx    = 48f;   // 44 -> 48: FontLabel 40 bold line box is ~46px
        private const float RowPx     = 64f;   // 56 -> 64: seats the fixed 40px icon + plate inset
        // OWNER F8 2026-09-02 ("spacing tight"): 8 -> 18. 8 ref px is ~3.5 screen px on the
        // owner's desktop — below the plate's own 0.04 vertical inset, so consecutive reward
        // plates read as one welded slab and the subtitle sat on the row grid. 18 clears the
        // plate insets on both sides and still costs the panel nothing it cannot afford: the
        // gap is counted in RequiredBodyPx, so the SOLVE grows the panel to pay for it (wave
        // banner: 5 bands -> +40 px of need -> +54 px of panel, a 0.36 half against the 0.47
        // clamp). Never bypass RequiredBodyPx when changing this — an uncounted gap is the
        // 2026-08-05 compaction class all over again.
        private const float BandGapPx = 18f;
        /// <summary>EXTRA breathing room between the narrative bands (emblem / subtitle / stars /
        /// time) and the SPOILS GRID below them — the copy and the ledger are two different kinds
        /// of content and the capture showed them welded together. Laid out as an empty BAND so
        /// it costs exactly one entry in both <see cref="RequiredBodyPx"/> and BuildBody.</summary>
        private const float SpoilsLeadGapPx = 16f;

        /// <summary>Total body-well pixels the VM's bands demand (drives the panel solve).
        /// <paramref name="canvasH"/> is the post-scale canvas height — the subtitle's wrapped
        /// line count depends on the real column width, which depends on it.</summary>
        private static float RequiredBodyPx(EndStateVM vm, float canvasH)
        {
            int cols = SpoilColumns(vm, canvasH);
            return RequiredBodyPxAt(vm, canvasH, cols, NarrativeStripAt(vm, canvasH, cols));
        }

        /// <summary>The same budget at an EXPLICIT column count. Split out for WO-952: the column
        /// solver has to ask "what would the body need at 2 columns / at 3?" and
        /// <see cref="RequiredBodyPx"/> above asks the solver back, which would recurse. Every
        /// caller that does not care keeps the one-line form.
        ///
        /// <para><paramref name="strip"/> is the SECOND reflow lever (WO-952, 2026-09-04): the
        /// emblem / stars / time bands laid SIDE BY SIDE in one strip instead of stacked. Same
        /// recursion reason as <paramref name="cols"/> — <see cref="NarrativeStripAt"/> decides by
        /// asking this method what the stacked budget costs, so it cannot be asked back.</para></summary>
        private static float RequiredBodyPxAt(EndStateVM vm, float canvasH, int cols, bool strip)
        {
            return RequiredBodyPxAtRows(vm, canvasH, cols, strip,
                                        SpoilRowsShown(vm, canvasH, cols, strip));
        }

        /// <summary>The same budget at an EXPLICIT SHOWN-ROW count — the recursion-free form the
        /// THIRD lever (<see cref="SpoilRowsShown"/>) asks its "what would this cost at N rows?"
        /// question through, and the form the two ESCALATION levers above must use.
        ///
        /// <para>⛔ <see cref="SpoilColumns"/> and <see cref="NarrativeStripAt"/> MUST call THIS
        /// with <c>shown = vm.Spoils.Count</c>, never the four-arg form. The lever ORDER is
        /// columns -> strip -> row trim, cheapest-and-lossless first: the trim is the only lever
        /// that removes content, so it may only ever see what the free levers could not save. If
        /// the escalation loops asked the trimmed budget they would observe "it fits" and never
        /// escalate, and the panel would silently drop rows it had the width to show.</para></summary>
        private static float RequiredBodyPxAtRows(EndStateVM vm, float canvasH, int cols, bool strip, int shown)
        {
            float px = 0f; int n = 0;
            if (strip)
            {
                // ONE band, as tall as the TALLEST element it seats — nothing is shrunk, the
                // three just stop each paying for their own row. NarrativeStripPx is the same
                // number BuildBody stamps the band at.
                float stripPx = NarrativeStripPx(vm);
                if (stripPx > 0f) { px += stripPx; n++; }
                if (!string.IsNullOrEmpty(vm.Subtitle)) { px += SubLinePx * SubtitleLines(vm.Subtitle, canvasH, PanelWidthFracFor(vm)); n++; }
            }
            else
            {
                if (vm.Emblem != null) { px += EmblemPx; n++; }
                if (!string.IsNullOrEmpty(vm.Subtitle)) { px += SubLinePx * SubtitleLines(vm.Subtitle, canvasH, PanelWidthFracFor(vm)); n++; }
                if (vm.Stars >= 0) { px += StarsBandPx(vm); n++; }
                if (vm.TimeSeconds >= 0f) { px += TimePx; n++; }
            }
            int spoilBands = SpoilBandCountAt(vm, cols, shown);
            // The SEPARATOR band (owner F8 2026-09-02: the subtitle sat straight on the row
            // grid). Budgeted as a real band so the solve and BuildBody count the SAME thing —
            // an unbudgeted spacer is the desync class this file keeps re-learning.
            if (spoilBands > 0 && n > 0) { px += SpoilsLeadGapPx; n++; }
            px += spoilBands * RowPx; n += spoilBands;
            if (n > 1) px += BandGapPx * (n - 1);
            return px;
        }

        // ── NARRATIVE STRIP (WO-952 REOPEN #2, 2026-09-04) ───────────────────────────────
        // THE SECOND REFLOW LEVER, and the one that saves the surfaces the column lever cannot.
        //
        // The oracle proved the defect is WIDER than the capture: at 2340x1080 and 1920x1080 the
        // post-scale canvas is 869 ref px, so the pinned well is 0.740 x (2 x 0.47 x 869) - 132 =
        // 472 px — and THREE spoils rows already need 496. The column lever cannot help there:
        // the body column is 1206 px (2340) and 990 px (1920), and a third column needs 3 x 420 =
        // 1260 px of legibility floor. Both surfaces cap at 2 columns and pin at the clamp.
        //
        // So take the height from the axis that is paying for a whole row to say very little. The
        // emblem (64 px), the star rating (72 px) and the Time line (48 px) are each ONE SMALL
        // CENTRED ELEMENT on a 990-1376 px wide band — three rows and two gaps, 202 px of well,
        // to draw a crest, three stars and five characters. Laid SIDE BY SIDE they cost ONE band
        // of 72 px: 202 -> 72 px, and not one element is smaller than it was. That is the whole
        // point — the clamp may not move (0.47 against a 0.50 centre already spans the documented
        // 0.03..0.97; more clips the top edge, the 2026-07-08 defect) and the bands may not shrink
        // (sub-content-size bands ARE the defect), so the content has to occupy the space
        // differently. 3/4 rows: 496 -> 348 px. 5/6 rows: 578 -> 430 px. Both clear 472 unpinned.
        //
        // ⛔ ESCALATION ONLY, exactly like the column lever: a panel that already fits keeps the
        // layout it shipped with. The Seeker's 4-row victory (496 = 496, 2 columns) and its 5-row
        // gear drop (3 columns, 496 = 496) are bit-for-bit unchanged, and the compact wave-clear
        // banner never enters here at all.

        /// <summary>
        /// WO-1789 — the STAR BAND's height: the authored <see cref="StarsPx"/>, plus a
        /// <see cref="StarCaptionPx"/> allowance ONLY when the VM carries a
        /// <see cref="EndStateVM.StarCaption"/>.
        ///
        /// <para>⛔ EVERY site that used to read <see cref="StarsPx"/> for the band reads THIS, so
        /// the panel solve (<see cref="RequiredBodyPxAtRows"/>), the strip height
        /// (<see cref="NarrativeStripPx"/>) and BuildBody's <c>bands.Add</c> can never disagree
        /// about how tall the star band is. <see cref="StarsPx"/> itself is now only the STARS'
        /// own share of it, which is what <see cref="BuildStarRow"/> divides by.</para>
        ///
        /// <para>A VM with no caption returns exactly <see cref="StarsPx"/>, so every screen that
        /// ships today solves and lays out to the pixel it does now.</para>
        /// </summary>
        private static float StarsBandPx(EndStateVM vm)
            => StarsPx + (vm != null && !string.IsNullOrEmpty(vm.StarCaption) ? StarCaptionPx : 0f);

        /// <summary>The strip band's height: the TALLEST of the elements it seats, so every one of
        /// them keeps its own authored fixed size (<see cref="EmblemPx"/> /
        /// <see cref="StarsBandPx"/> / <see cref="TimePx"/>). 0 when the VM carries none of
        /// them.</summary>
        private static float NarrativeStripPx(EndStateVM vm)
        {
            float px = 0f;
            if (vm == null) return px;
            if (vm.Emblem != null) px = Mathf.Max(px, EmblemPx);
            if (vm.Stars >= 0) px = Mathf.Max(px, StarsBandPx(vm));
            if (vm.TimeSeconds >= 0f) px = Mathf.Max(px, TimePx);
            return px;
        }

        /// <summary>How many elements the strip would seat — the emblem, the star rating and the
        /// Time line. Below two there is nothing to merge and the strip is refused.</summary>
        private static int NarrativePartCount(EndStateVM vm)
        {
            if (vm == null) return 0;
            int n = 0;
            if (vm.Emblem != null) n++;
            if (vm.Stars >= 0) n++;
            if (vm.TimeSeconds >= 0f) n++;
            return n;
        }

        /// <summary>Legibility floor for ONE strip cell, in reference px. DERIVED, not picked: the
        /// widest thing the strip seats is the star cluster, whose centres are fixed at
        /// -<see cref="StarSpacingPx"/> / 0 / +<see cref="StarSpacingPx"/> with a
        /// <see cref="StarSizePx"/> bbox, i.e. 2 x 80 + 56 = 216 ref px — plus 24 px of clear on
        /// each side so the cluster never touches the emblem or the Time line. The Time line
        /// ("Time  12:34" at FontLabel 40) measures well under that and carries FitSingleLine as
        /// its own backstop. In PORTRAIT the body column is ~692 px against a 3-cell requirement
        /// of 792, so portrait never strips — which is correct twice over: portrait has 1204 px of
        /// well and never needed it.</summary>
        private const float MinStripCellPx = 216f + 2f * 24f;   // 264

        /// <summary>Would this screen lay its narrative bands as a strip? Answered the same way
        /// the column lever is: only when the STACKED body genuinely does not fit the well the
        /// clamp allows, and only while the width can seat the cells legibly.</summary>
        private static bool NarrativeStripAt(EndStateVM vm, float canvasH, int cols)
        {
            if (vm == null || vm.Compact) return false;
            int parts = NarrativePartCount(vm);
            if (parts < 2) return false;   // nothing to merge
            // WIDTH-GATED, never height-driven: the strip is legal only where the cells clear
            // their floor. This is what keeps portrait single-file.
            if (SpoilsBodyWidthPx(canvasH, PanelWidthFracFor(vm)) < parts * MinStripCellPx) return false;
            // ESCALATION ONLY: ask the STACKED budget, so a panel that fits keeps its layout.
            // UNTRIMMED (shown = every row): the strip lever is lossless and therefore moves
            // BEFORE the row trim. See the ordering note on RequiredBodyPxAtRows.
            return RequiredBodyPxAtRows(vm, canvasH, cols, false, vm.Spoils.Count) > MaxBodyWellPx(vm, canvasH);
        }

        // -- WO-952 REOPEN: THE BODY WELL THE CLAMP ACTUALLY ALLOWS -----------------------
        /// <summary>
        /// The MOST body-well pixels this screen can ever have, in reference px: the panel pinned
        /// at <see cref="MaxPanelHalf"/>, minus the CTA band, through the same body law
        /// <see cref="PanelHalfHeight"/> inverts. This is the number the panel solve silently ran
        /// into on the owner's device, and until now nothing could ASK for it.
        ///
        /// <para>MEASURED, F8 seq 4680 (2026-09-04, SM02G4061955851, post-scale canvas 965 px):
        /// panel 907 px = frac 0.94, pinned exactly at the ceiling; well = 0.740 x 907 - 132 =
        /// 540 px against a need of 578 px, so every band compressed to 0.933.</para>
        /// </summary>
        private static float MaxBodyWellPx(EndStateVM vm, float canvasH)
        {
            return BodyFracOfPanel * (2f * MaxPanelHalf * canvasH) - ActionBandPx(vm, canvasH);
        }

        /// <summary>The most columns this screen's WIDTH can legibly carry. Derived, never picked:
        /// each column must clear <see cref="MinSpoilColumnPx"/>. Hard-capped at
        /// <see cref="MaxSpoilColumns"/> because past three a reward plate stops reading as a line
        /// item and starts reading as a tile grid.</summary>
        private static int MaxSpoilColumnsByWidth(EndStateVM vm, float canvasH)
        {
            float bodyPx = SpoilsBodyWidthPx(canvasH, PanelWidthFracFor(vm));
            int fit = Mathf.FloorToInt(bodyPx / MinSpoilColumnPx);
            return Mathf.Clamp(fit, 1, MaxSpoilColumns);
        }

        /// <summary>What the fit solve resolves to for a VM at a given canvas height. Diagnostics
        /// and oracles only - it changes nothing.</summary>
        public struct FitResult
        {
            /// <summary>Body pixels the VM's bands demand (BuildBody's <c>totalPx</c>).</summary>
            public float NeedPx;
            /// <summary>Body-well pixels the solved panel actually offers (BuildBody's <c>wellH</c>).</summary>
            public float WellPx;
            /// <summary>Solved panel height in reference px.</summary>
            public float PanelPx;
            /// <summary>Panel height as a fraction of the screen (0.94 = pinned at the clamp).</summary>
            public float PanelFrac;
            /// <summary>The uniform band compression BuildBody would apply. 1 = no compression;
            /// below <c>0.995</c> is the FlowTrace.Fail this screen has shipped twice.</summary>
            public float Scale;
            /// <summary>Spoils columns the solver chose.</summary>
            public int Columns;
            /// <summary>Spoils bands at that column count.</summary>
            public int SpoilBands;
            /// <summary>Spoils rows the VM ASKED for (<c>vm.Spoils.Count</c>).</summary>
            public int RowsRequested;
            /// <summary>Spoils rows the third lever (<see cref="SpoilRowsShown"/>) actually seats.
            /// Below <see cref="RowsRequested"/> means the tail was trimmed and a shortfall band
            /// states the difference — never a silent drop.</summary>
            public int RowsShown;
            /// <summary>Rows the trim dropped. 0 on every panel that fits, which is nearly all
            /// of them — this is the ESCALATION lever's counter, not a routine one.</summary>
            public int RowsDropped;
            /// <summary>True while the panel is pinned at <see cref="MaxPanelHalf"/> - the state in
            /// which the well can no longer grow to meet the need.</summary>
            public bool PinnedAtClamp;
            /// <summary>True when the emblem / stars / time bands were reflowed into ONE side-by-side
            /// strip - the second WO-952 lever, taken only where the columns cannot save the fit
            /// (see <see cref="NarrativeStripAt"/>).</summary>
            public bool NarrativeStripMerged;
        }

        /// <summary>
        /// ⭐ THE ORACLE SEAM (WO-952 REOPEN, 2026-09-04). Answers "would this VM compress?" from
        /// THE SAME functions the live screen solves with - <see cref="SpoilColumns"/>,
        /// <see cref="RequiredBodyPxAt"/>, <see cref="PanelHalfHeight"/> and Bind's own well
        /// derivation - so an oracle cannot pass while the screen fails.
        ///
        /// <para>⛔ IT MUST NOT RE-DERIVE THE ARITHMETIC. A suite that recomputes the band budget
        /// is duplicated state and will drift exactly the way this WO's first "DONE" did: the
        /// August close rested on an audit, the <c>COMPRESSED</c>-absence oracle it specced was
        /// never written, and the recurrence reached the owner's eyes instead of a gate's.</para>
        ///
        /// <para>⚠ The subtitle line count is WIDTH-dependent, so a caller measuring a specific
        /// device must drive <c>ElarionUiKit.SetSurfaceOverride(w, h)</c> first (editor-only) -
        /// otherwise <see cref="PostScaleCanvasWidth"/> resolves the harness's own aspect.</para>
        /// </summary>
        public static FitResult ProbeFit(EndStateVM vm, float canvasH)
        {
            var r = new FitResult();
            if (vm == null || canvasH < 100f) return r;
            r.Columns    = SpoilColumns(vm, canvasH);
            r.NarrativeStripMerged = NarrativeStripAt(vm, canvasH, r.Columns);
            // The THIRD lever, asked in the SAME order the live screen resolves it (columns ->
            // strip -> row trim), so the oracle sees the shipped row count and not an untrimmed
            // one. RowsShown/RowsDropped are what let a suite pin "rewards survived the trim".
            r.RowsRequested = vm.Spoils.Count;
            r.RowsShown     = SpoilRowsShown(vm, canvasH, r.Columns, r.NarrativeStripMerged);
            r.RowsDropped   = Mathf.Max(0, r.RowsRequested - r.RowsShown);
            r.SpoilBands = SpoilBandCountAt(vm, r.Columns, r.RowsShown);
            r.NeedPx     = RequiredBodyPxAtRows(vm, canvasH, r.Columns, r.NarrativeStripMerged, r.RowsShown);

            float half = PanelHalfHeight(vm, canvasH);
            r.PanelPx   = 2f * half * canvasH;
            r.PanelFrac = r.PanelPx / canvasH;
            r.PinnedAtClamp = Mathf.Approximately(half, MaxPanelHalf);

            // Bind's OWN well derivation, verbatim (see the geometry pass): the CTA comes off in
            // PIXELS, never as a fraction - that unit mix-up is the 2026-08-05 defect.
            float ctaBandH = ActionBandPx(vm, canvasH) / Mathf.Max(1f, r.PanelPx);
            float bodyFloor = CtaBandY0 + ctaBandH + CtaGapY;
            r.WellPx = (BodyTopY - bodyFloor) * r.PanelPx;

            // BuildBody's own line.
            r.Scale = r.WellPx > 1f && r.NeedPx > r.WellPx ? r.WellPx / r.NeedPx : 1f;
            return r;
        }

        /// <summary>The compression floor BuildBody fails below. Exposed so the oracle asserts
        /// against the SHIPPED threshold rather than a copy of it (see the epsilon note in
        /// BuildBody: 0.995 exists so a 0.9997 float residue from the self-fitting solve is not
        /// reported as a clamp).</summary>
        public const float CompressFailBelowFrac = 0.995f;

        // ── SPOILS COLUMNS (WO-894, orchestrator ruling — a DELIBERATE, DOCUMENTED deviation
        //    from the WO's §2 wireframe, which draws spoils as one vertical list) ──────────
        // WHY: the wireframe was drawn without knowing the content does not fit the surface.
        // Measured at 2670x1200 (post-scale canvas 965.4 x 2148.0 ref px), an arena win with
        // five spoils rows demands a 1027 ref px panel on a 965 ref px canvas — the content is
        // literally TALLER THAN THE SCREEN, so it hit the MaxPanelHalf clamp and every band was
        // squashed to 0.859. No spacing tweak can fix that; each one only moves the squeeze.
        // Two columns takes the spoils stack 320px -> 192px and the whole body 628px -> 484px,
        // which solves to an 832px panel UNCLAMPED at scale 1.000 — the only lever that clears
        // it without shrinking the header band (which failed exactly this way on 2026-07-08,
        // rendering zero title glyphs).
        // It is also the right SHAPE: a single narrow column of five rows starves the axis we
        // have most of (2148px of width) to overflow the one we have least.
        // LANDSCAPE ONLY — see MinSpoilColumnPx.

        /// <summary>Legibility floor for ONE spoils column, in reference px. DERIVED, not picked:
        /// the label column is (0.62 - labelLeft) of the plate, the plate is 0.88 of the cell, and
        /// the label's fixed furniture is 72px (18 inset + 40 icon + 14 gap). The longest stock
        /// reward label, "Experience", measures ~250px at FontBody 50, so it stays above the
        /// FontFloor(30) only while 0.62 * plate - 72 >= 250 * 30/50 = 150px, i.e. plate >= 358px,
        /// i.e. cell >= 407px. 420 keeps a margin. At 2670x1200 a column is 535px (28% clear);
        /// in portrait it is 269px, so portrait stays SINGLE-COLUMN as ruled.</summary>
        private const float MinSpoilColumnPx = 420f;

        /// <summary>Body-well width in reference px (the full width a spoils band spans).
        /// <paramref name="panelWidthFrac"/> = this screen's canvas-width fraction
        /// (<see cref="PanelWidthFracFor"/> — WO-952: the banner is 0.70, the modal 0.56).</summary>
        private static float SpoilsBodyWidthPx(float canvasH, float panelWidthFrac)
        {
            return PostScaleCanvasWidth(canvasH) * panelWidthFrac * BodyZoneWidthFrac;
        }

        /// <summary>Spoils columns for this screen: 2 only when a column clears
        /// <see cref="MinSpoilColumnPx"/>. The test is on the derived WIDTH, so it keys off the
        /// real aspect ratio and never off a hardcoded resolution. Compact banners stay single
        /// (their damage-report rows carry long "Rebuild 120 wood, 40 iron" amounts), and a lone
        /// reward stays single (one half-width plate beside nothing reads as a broken row).</summary>
        private static int SpoilColumns(EndStateVM vm, float canvasH)
        {
            if (vm == null || vm.Compact || vm.Spoils.Count < 2) return 1;

            int widthCap = MaxSpoilColumnsByWidth(vm, canvasH);
            if (widthCap < 2) return 1;

            // THE STARTING SHAPE IS UNCHANGED. Two columns is the WO-894 ruling and every panel
            // that fits today keeps the exact layout it has - a 4-row arena victory still solves
            // at 2 columns, 638 px, scale 1.000. Nothing reflows unless it has to.
            int cols = 2;

            // -- WO-952 REOPEN (owner F8 seq 4680): REFLOW, DO NOT SHRINK ------------------
            // A GEAR DROP adds the 5th spoils row. At 2 columns that row cannot share a band, so
            // the grid goes 2 bands -> 3 (+64 px of row, +18 px of gap) and the body needs 578 px.
            // The panel solve answers by growing... into MaxPanelHalf, where it pins at frac 0.94
            // and the well stops at 540 px. BuildBody then does the only thing left to it and
            // compresses EVERY band to 0.933 - below its own content size, which is precisely
            // what the fixed-px band law exists to prevent.
            //
            // The lever is NOT the clamp. MaxPanelHalf is 0.47 against a 0.50 centre, i.e. the
            // documented 0.03..0.97 span; raising it puts the top edge at the screen edge, which
            // is the 2026-07-08 clipping defect this file already paid for once (see the WO-894
            // ruling on the 0.53 centre, ~line 155). The lever is the axis we have MOST of: at
            // 2670x1200 the body column is 1376 ref px, so a third column is 459 px - clear of the
            // 420 px legibility floor by 9%. Five rows at 3 columns is 2 bands again, need drops
            // 578 -> 496 px, and the panel solves UNCLAMPED at 638 px, scale 1.000.
            //
            // Escalate ONLY when the body genuinely does not fit, and only as far as the width
            // floor allows. In PORTRAIT the floor gives widthCap == 1 and none of this runs, so
            // portrait stays single-column exactly as ruled.
            float wellCap = MaxBodyWellPx(vm, canvasH);
            // Asked on the STACKED budget (strip:false) on purpose: the column lever moves
            // first, so the Seeker's gear-drop victory keeps the 3-column layout WO-952 gave it
            // rather than silently swapping to a strip. Only where width caps the columns does
            // the strip lever get its turn (NarrativeStripAt).
            // UNTRIMMED (shown = every row): the column lever is lossless and moves FIRST, so it
            // must never see the row trim's saving. See the note on RequiredBodyPxAtRows.
            while (cols < widthCap && RequiredBodyPxAtRows(vm, canvasH, cols, false, vm.Spoils.Count) > wellCap)
                cols++;

            if (cols > 2)
                FlowTrace.Step("EndState",
                    $"spoils grid REFLOWED to {cols} columns: {vm.Spoils.Count} rows needed " +
                    $"{RequiredBodyPxAtRows(vm, canvasH, 2, false, vm.Spoils.Count):0}px at 2 columns against a " +
                    $"{wellCap:0}px well ceiling (the panel is clamped at {MaxPanelHalf:0.##} of " +
                    $"half-screen), and reflow to {cols} brings it to " +
                    $"{RequiredBodyPxAtRows(vm, canvasH, cols, false, vm.Spoils.Count):0}px. Reflowing is the fix; compressing " +
                    "every band below its own content size is the defect (WO-952).");

            return cols;
        }

        /// <summary>⛔ THE CEILING ON REFLOW. Two columns is the WO-894 ruling and three is the
        /// WO-952 escape hatch for a gear-drop arena victory; a fourth is not a layout, it is a
        /// tile grid, and a reward line item stops reading as a line item in it. The WIDTH floor
        /// (<see cref="MinSpoilColumnPx"/>) usually binds first - this is the cap for the day
        /// somebody builds a wider panel and it does not.</summary>
        private const int MaxSpoilColumns = 3;

        /// <summary>THE ONE spoils band plan — (first index, how many cells) per band, top to
        /// bottom. Both the panel SOLVE (via <see cref="SpoilBandCount"/>) and the LAYOUT (via
        /// BuildBody) call this, so they can never disagree about the band count.
        ///
        /// OWNER F8 2026-09-02, defect #4 ("row-shape inconsistency"): a resource row is
        /// icon + short noun + right-aligned "+180". A structure-damage row is a SENTENCE plus a
        /// COST ("Archer Tower - damaged 40%" / "Repair 40 wood, 12 iron") — a different kind of
        /// thing that was wearing a resource row's clothes, and in a half-width cell both halves
        /// ellipsised, so it read as a BROKEN resource row rather than a different one.
        /// <see cref="SpoilRowVM.Wide"/> is the row saying so: a wide row always takes a band to
        /// itself, at the full body width, and therefore reads as its own grammar.</summary>
        private static List<(int first, int count)> SpoilBandPlan(EndStateVM vm, int cols, int shown)
        {
            var plan = new List<(int, int)>();
            if (vm == null || vm.Spoils.Count == 0) return plan;
            if (cols < 1) cols = 1;
            int limit = Mathf.Clamp(shown, 0, vm.Spoils.Count);
            int i = 0;
            while (i < limit)
            {
                var row = vm.Spoils[i];
                if (cols == 1 || (row != null && row.Wide)) { plan.Add((i, 1)); i++; continue; }
                int n = 1;
                while (n < cols && i + n < limit)
                {
                    var next = vm.Spoils[i + n];
                    if (next != null && next.Wide) break;   // a wide row never shares a band
                    n++;
                }
                plan.Add((i, n));
                i += n;
            }
            // ── THE SHORTFALL BAND (WO-952 REOPEN #3) ────────────────────────────────────
            // TRUNCATION IS STATED, NEVER SILENT — the same law the VM already keeps for the
            // compact banner ("Showing N of M damaged structures", EndStateVM.FromWaveClear).
            // A dropped damage row reads as "that building is fine", which is a WORSE defect
            // than the one the trim is fixing. The band is emitted HERE, inside the one plan
            // both the solve and BuildBody count, so its pixels are BUDGETED rather than
            // discovered after layout — an uncounted band is the desync class this file is a
            // monument to. SummaryBandFirst is the sentinel: no real row index is negative.
            if (limit < vm.Spoils.Count) plan.Add((SummaryBandFirst, 0));
            return plan;
        }

        /// <summary>Sentinel <c>first</c> for the shortfall band emitted by
        /// <see cref="SpoilBandPlan"/> when the row trim dropped rows. Negative because no real
        /// spoils index ever is, so a band cannot be mistaken for a row.</summary>
        private const int SummaryBandFirst = -1;

        /// <summary>⛔ THE FLOOR ON THE ROW TRIM. Trimming to nothing would leave the player a
        /// headline and an empty ledger — the trim exists to keep rows LEGIBLE, never to remove
        /// the reason the panel opened. One row plus the shortfall band is the least this screen
        /// may ever show.</summary>
        private const int MinSpoilRowsShown = 1;

        /// <summary>
        /// ⭐ THE THIRD REFLOW LEVER (WO-952 REOPEN #3, owner F8 2026-09-06, build
        /// 2026.09.06.357599, SM02G4061955851): HOW MANY spoils rows this screen may show before
        /// the bands would have to compress below their own content size.
        ///
        /// <para>WHY A THIRD LEVER WAS NEEDED — the two we had are STRUCTURALLY INERT on the
        /// surface that failed. The captured line was <c>need=668px well=540px scale=0.808</c>
        /// from a WAVE-CLEAR DAMAGE REPORT, and for that VM:</para>
        /// <list type="bullet">
        /// <item>the COLUMN lever cannot help: every damage row is <see cref="SpoilRowVM.Wide"/>
        /// (EndStateVM.FromWaveClear sets it, because a damage line is a sentence plus a cost, not
        /// a resource row), and <see cref="SpoilBandPlan"/> gives a wide row a band to itself at
        /// ANY column count. Eight damage rows are eight bands at 1, 2 or 3 columns.</item>
        /// <item>the STRIP lever cannot help: FromWaveClear leaves <c>Stars</c> and
        /// <c>TimeSeconds</c> at their -1 defaults, so the narrative is the emblem alone and
        /// <see cref="NarrativeStripPx"/> == <see cref="EmblemPx"/> — merging one element into a
        /// strip of one saves exactly 0 px.</item>
        /// </list>
        /// <para>So both escalations ran and changed nothing, the panel pinned at
        /// <see cref="MaxPanelHalf"/>, and BuildBody did the only thing left to it. The row count
        /// is the ONLY axis left, and it is the axis that was never capped: EndStateVM's
        /// <c>damageBudget</c> is <c>vm.Compact ? CompactMaxSpoilRows - rewardRows :
        /// damageAvailable</c> — the FULL modal had NO ceiling at all and rendered up to
        /// WaveDamageReport's eight entries.</para>
        ///
        /// <para>THE ARITHMETIC, reconstructed from the captured number and the constants at
        /// source: emblem 64 + subtitle 60x1 + lead gap 16 + 64R + 18(R+2) = 116 + 60L + 82R.
        /// At L=1 that is 668 px exactly at R=6 — the captured need, to the pixel. The well is
        /// 540, so R must fall to 3 once the shortfall band's own line is paid for
        /// (116 + 120 + 82x4 = 504 <= 540, scale 1.000).</para>
        ///
        /// <para>⛔ ESCALATION ONLY, exactly like the two levers above. The search starts at the
        /// FULL row count and only steps down while the budget genuinely overruns the well the
        /// clamp allows, so every panel that fits today keeps the layout it shipped with and this
        /// method returns <c>vm.Spoils.Count</c> unchanged.</para>
        /// </summary>
        private static int SpoilRowsShown(EndStateVM vm, float canvasH, int cols, bool strip)
        {
            if (vm == null || vm.Spoils.Count == 0) return 0;
            // ⛔ COMPACT BANNERS NEVER ENTER, exactly like the two levers above (SpoilColumns
            // returns 1 on vm.Compact; the strip note says "the compact wave-clear banner never
            // enters here at all"). Two reasons, and the second is a correctness bug, not tidiness:
            //   1. a compact banner ALREADY has its budget, applied model-side where it belongs
            //      (EndStateVM.CompactMaxSpoilRows), and it STATES its own shortfall in the
            //      subtitle ("Showing N of M damaged structures").
            //   2. MaxBodyWellPx is the FULL MODAL's ceiling. The compact well is a different,
            //      SMALLER number (_compactBodyFrac x its own grown panel), so this cap would be
            //      the wrong yardstick for it — and a compact banner that trimmed here would print
            //      BOTH shortfall statements at once, its subtitle's and the band's, disagreeing
            //      about the same count. One screen, one truth.
            if (vm.Compact) return vm.Spoils.Count;
            int shown = vm.Spoils.Count;
            float wellCap = MaxBodyWellPx(vm, canvasH);
            if (wellCap <= 1f) return shown;
            // Step DOWN one row at a time. RequiredBodyPxAtRows re-asks SpoilBandPlan each pass,
            // so the shortfall band's own cost is inside the number being tested — the trim can
            // never "save" pixels it then spends on saying that it trimmed.
            while (shown > MinSpoilRowsShown &&
                   RequiredBodyPxAtRows(vm, canvasH, cols, strip, shown) > wellCap)
                shown--;
            return shown;
        }

        /// <summary>How many BANDS the spoils occupy — the number the panel solve must budget.</summary>
        private static int SpoilBandCount(EndStateVM vm, float canvasH)
        {
            int cols = SpoilColumns(vm, canvasH);
            return SpoilBandCountAt(vm, cols,
                SpoilRowsShown(vm, canvasH, cols, NarrativeStripAt(vm, canvasH, cols)));
        }

        /// <summary>Band count at an EXPLICIT column count - the recursion-free form the WO-952
        /// column solver asks its "what would this cost at N columns?" question through.</summary>
        private static int SpoilBandCountAt(EndStateVM vm, int cols, int shown)
        {
            if (vm == null || vm.Spoils.Count == 0) return 0;
            return SpoilBandPlan(vm, cols, shown).Count;
        }

        /// <summary>Stack the VM's content top-down inside the body zone. F8-35: bands are
        /// PIXEL-sized (each row owns a real row height) instead of fraction-weighted — the
        /// old weights divided whatever space survived the close-band reservation + CTA
        /// floor-raise, so a 5-reward victory squeezed every row to ~13px and all the
        /// labels/values overprinted (owner capture flag_20260708-085151_03.png).</summary>
        /// <summary>
        /// Seat the panel TITLE inside the frame art's inner well, on BOTH axes.
        ///
        /// OWNER F8 2026-09-02, defect #2: "WAVE 7 CLEARED!" overhung the ornate gold border at
        /// both ends and rode across the top rail. MECHANISM, read at source rather than inferred:
        ///
        ///   * On the PROCEDURAL panel path the title is NOT in <c>chrome.layout.header</c> at all.
        ///     ElarionUiKit.cs:815 builds it as a DIRECT CHILD of chrome.content, stamped by
        ///     ElarionUiKit.Header at x 0.06..0.94, y 0.92..0.98 OF THE PANEL — plus a shadow copy
        ///     and a gilt rule beside it. This view's owned geometry pass stamps
        ///     chrome.layout.header (see Bind) and therefore moves NOTHING about the title. The
        ///     header band this file reasons about and the headline the player sees were two
        ///     different objects.
        ///   * On the FRAME path the title IS in the header zone, and that zone is stamped up to
        ///     <see cref="HeaderY1"/> = 0.985 — i.e. deliberately flush with the panel's top edge,
        ///     which was harmless when the frame drew no border there.
        ///   * Either way MedievalUiSkin.ApplyShell then draws the shell Image.Type.Sliced, so the
        ///     border and corner ornaments occupy FIXED reference pixels (see FrameBorderTopPx).
        ///
        /// So a fraction that was inside the art at one panel size is outside it at another, and
        /// the 2026-09-02 spacing pass (BandGapPx 8 -> 18 plus the SpoilsLeadGapPx band) grew the
        /// solved panel enough to push it out. Reserve the border in PIXELS and re-fit the
        /// headline into what is left. The panel is NOT narrowed and no pixels are taken from the
        /// body well — the title simply stops being the one element that never learned where the
        /// frame is.
        /// </summary>
        /// <param name="bandBottomFrac">Bottom edge of the band the title may occupy, as a
        /// fraction of the panel — the same number the geometry pass gave the header band, so the
        /// title can never reach down into the body well.</param>
        private void SeatTitleInsideFrame(ElarionUiKit.PanelChrome chrome, float bandBottomFrac)
        {
            if (chrome == null || chrome.title == null || chrome.root == null) return;

            var rootRt = chrome.root.transform as RectTransform;
            if (rootRt == null) return;
            // Deterministic panel size (ElarionUiKit.cs:1014-1018): a live rect read on the
            // canvas's creation frame returns RAW SCREEN pixels. Derive from the anchors instead,
            // exactly as the geometry passes above do.
            float panelPx  = _canvasH * Mathf.Max(0.05f, rootRt.anchorMax.y - rootRt.anchorMin.y);
            float panelWpx = PostScaleCanvasWidth(_canvasH)
                             * Mathf.Max(0.05f, rootRt.anchorMax.x - rootRt.anchorMin.x);
            if (panelPx < 50f || panelWpx < 50f) return;

            float topFrac = Mathf.Clamp01(1f - (FrameBorderTopPx + TitleClearPx) / panelPx);
            float botFrac = Mathf.Clamp(bandBottomFrac, 0f, 1f);
            if (topFrac - botFrac < MinTitleBandFrac)
            {
                // §12 no silent failure: say we declined rather than crushing the headline.
                FlowTrace.Warn("EndState",
                    $"title band left AS BUILT: reserving the frame's {FrameBorderTopPx:0}px top border " +
                    $"on a {panelPx:0}px panel leaves only {(topFrac - botFrac):0.###} of panel " +
                    $"(min {MinTitleBandFrac:0.###}) - a crushed title renders zero glyphs, which is worse " +
                    "than an overhanging one");
                return;
            }
            float x0 = Mathf.Clamp(FrameBorderSidePx / panelWpx, 0.04f, 0.30f);
            float x1 = 1f - x0;

            var titleRt = (RectTransform)chrome.title.transform;
            bool inHeaderZone = chrome.layout != null && chrome.layout.header != null
                                && titleRt.parent == chrome.layout.header;

            if (inHeaderZone)
            {
                // The title fills its zone 0..1, so moving the ZONE moves the headline and keeps
                // every other consumer of that band (none today) consistent with it.
                var hdr = chrome.layout.header;
                hdr.anchorMin = new Vector2(x0, botFrac);
                hdr.anchorMax = new Vector2(x1, topFrac);
                hdr.offsetMin = Vector2.zero; hdr.offsetMax = Vector2.zero;
            }
            else
            {
                // PROCEDURAL path: the title, its shadow copy and the gilt rule are three loose
                // siblings on chrome.content sharing ONE authored box. Move them together or the
                // pair separates — the kit already paid for that once (its own "DOUBLE-DRAWN TITLE
                // FIX" note at ElarionUiKit.cs:1543).
                Vector2 oldMin = titleRt.anchorMin, oldMax = titleRt.anchorMax;
                var newMin = new Vector2(x0, botFrac);
                var newMax = new Vector2(x1, topFrac);
                var parent = titleRt.parent;
                for (int i = 0; parent != null && i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i) as RectTransform;
                    if (child == null) continue;
                    bool isTitlePair = child == titleRt
                        || (child.GetComponent<TMPro.TMP_Text>() != null
                            && (child.anchorMin - oldMin).sqrMagnitude < 1e-6f
                            && (child.anchorMax - oldMax).sqrMagnitude < 1e-6f);
                    if (isTitlePair)
                    {
                        // Anchors ONLY: the shadow carries its 1.5px offset in offsetMin/Max
                        // (Header applies it via anchoredPosition), and zeroing those would weld
                        // the shadow onto the title.
                        child.anchorMin = newMin;
                        child.anchorMax = newMax;
                        continue;
                    }
                    if (child.gameObject.name == "Rule"
                        && child.anchorMin.y <= oldMin.y + 0.001f
                        && child.anchorMin.y >= oldMin.y - 0.03f)
                    {
                        float ruleY = Mathf.Max(0f, botFrac - TitleRuleDropFrac);
                        child.anchorMin = new Vector2(x0, ruleY);
                        child.anchorMax = new Vector2(x1, ruleY);
                    }
                }
            }

            // Re-fit against the NEW box, with EXPLICIT bounds. Never re-fit with the default
            // maxSize: it reads the label's CURRENT fontSize, which auto-sizing has already
            // written down, so a second bare call ratchets the headline smaller every time
            // (the kit documents this exact hazard at ElarionUiKit.cs:817-819).
            Canvas.ForceUpdateCanvases();
            ElarionUiKit.FitSingleLine(chrome.title, 0f, ElarionUi.FontTitle);
            var titleParent = titleRt.parent;
            for (int i = 0; titleParent != null && i < titleParent.childCount; i++)
            {
                var sib = titleParent.GetChild(i) as RectTransform;
                if (sib == null || sib == titleRt) continue;
                var txt = sib.GetComponent<TMPro.TMP_Text>();
                if (txt == null) continue;
                if ((sib.anchorMin - titleRt.anchorMin).sqrMagnitude > 1e-6f) continue;
                ElarionUiKit.FitSingleLine(txt, 0f, ElarionUi.FontTitle);   // the shadow copy
            }

            FlowTrace.Step("EndState",
                $"title seated INSIDE the frame art: panel={panelPx:0}x{panelWpx:0}px, band " +
                $"y {botFrac:0.###}-{topFrac:0.###} x {x0:0.###}-{x1:0.###} " +
                $"(reserved {FrameBorderTopPx:0}px top rail + {FrameBorderSidePx:0}px corner ornaments, " +
                $"9-sliced so they never scale with the panel; path=" +
                (inHeaderZone ? "header zone" : "procedural title+shadow+rule") + ")");
        }

        private void BuildBody(EndStateVM vm, RectTransform body)
        {
            // (pixel height, builder) bands, top to bottom.
            var bands = new List<(float px, Action<RectTransform> build)>();

            // THE TWO REFLOW LEVERS, resolved ONCE and in the SAME ORDER the panel solve resolved
            // them (RequiredBodyPx): columns first, then the narrative strip. Both are asked of
            // the shared solvers, never re-derived here - a layout that disagrees with the solve
            // about how many bands exist is the desync class this file is a monument to.
            int spoilCols = SpoilColumns(vm, _canvasH);
            bool strip = NarrativeStripAt(vm, _canvasH, spoilCols);

            // The three narrative builders, hoisted out of the band list so the STACKED path and
            // the STRIP path seat literally the same elements - the only difference is whether
            // each gets its own band or a cell of one shared band.
            Action<RectTransform> buildEmblem = vm.Emblem == null ? null : (Action<RectTransform>)(host =>
                {
                    var go = new GameObject("Emblem", typeof(Image));
                    go.transform.SetParent(host, false);
                    var img = go.GetComponent<Image>();
                    img.sprite = vm.Emblem;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    var rt = img.rectTransform;
                    rt.anchorMin = new Vector2(0.38f, 0.04f);
                    rt.anchorMax = new Vector2(0.62f, 0.96f);
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                    Track(go, 0.10f, 0.7f);   // emblem pops from smaller — the hero beat
                });

            // F8 flag_04 ("death panel elements overlap"): this band was a FIXED 1.1 weight
            // (one line) while the death message wraps to ~3 lines — TMP renders overflow
            // OUTSIDE its rect, so the text climbed under the emblem band above (shield on
            // top of line 1) and sank into the carved footer band below ("Try Again" over
            // lines 2-3). Weight the band per wrapped line (matches PanelHalfHeight, which
            // grows the panel by the same estimate) and auto-shrink as the last-resort
            // guard so the copy can NEVER escape its rect (§1.14: text never overlaps
            // siblings) if the estimate is ever short.
            float subtitlePx = string.IsNullOrEmpty(vm.Subtitle)
                ? 0f
                : SubLinePx * SubtitleLines(vm.Subtitle, _canvasH, PanelWidthFracFor(vm));
            Action<RectTransform> buildSubtitle = string.IsNullOrEmpty(vm.Subtitle) ? null : (Action<RectTransform>)(host =>
                {
                    var l = ElarionUiKit.Label(host, vm.Subtitle, 0f, 1f, ElarionUi.Parchment,
                        ElarionUi.FontBody, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f);
                    // OWNER F8 2026-08-05 (the subtitle painted over the emblem above it): the raw
                    // enableAutoSizing path below does NOT set an overflow mode, and TMP's default
                    // Overflow RENDERS OUTSIDE THE RECT — so once the band was shorter than the
                    // wrapped copy the text simply escaped upward. FitBlock is the kit's bounded
                    // block fitter: normal wrap + bounded auto-size + TextOverflowModes.Truncate
                    // (ElarionUiKitObsidian.cs:2609-2623), so the copy is now STRUCTURALLY unable
                    // to paint on a sibling. With SubLinePx 60 >= the 50pt line box it never has to.
                    ElarionUiKit.FitBlock(l);
                    l.raycastTarget = false;
                    Track(l.gameObject, 0.14f, 1f);
                });

            // WO-1789 - the caption rides INSIDE the star band, so the row and the sentence about the
            // row can never be separated by a reflow, and the strip escalation carries both together.
            Action<RectTransform> buildStars = vm.Stars < 0 ? null : (Action<RectTransform>)(host =>
                BuildStarRow(host, vm.Stars, vm.StarCaption));

            Action<RectTransform> buildTime = vm.TimeSeconds < 0f ? null : (Action<RectTransform>)(host =>
                {
                    var l = ElarionUiKit.Label(host, "Time  " + FormatTime(vm.TimeSeconds), 0f, 1f,
                        ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center,
                        0.06f, 0.94f, bold: true);
                    ElarionUiKit.FitSingleLine(l);   // §1.14 — the time line never spills its own row
                    l.raycastTarget = false;
                    Track(l.gameObject, 0.20f, 1f);
                });

            // ── THE BANDS, IN ORDER ──────────────────────────────────────────────────────
            // STACKED (the shipped shape, unchanged): emblem / subtitle / stars / time.
            // STRIP  (WO-952 escalation): [emblem | stars | time] / subtitle - ONE band as tall
            // as the tallest element, split into equal cells. Nothing is scaled down; the three
            // simply stop each paying for a row of their own. See NarrativeStripAt.
            if (strip)
            {
                var parts = new List<Action<RectTransform>>();
                if (buildEmblem != null) parts.Add(buildEmblem);
                if (buildStars != null) parts.Add(buildStars);
                if (buildTime != null) parts.Add(buildTime);
                float stripPx = NarrativeStripPx(vm);
                // What the same elements cost STACKED, so the trace states the saving as a
                // measured number rather than a claim.
                float stackedPx = (vm.Emblem != null ? EmblemPx : 0f)
                                + (vm.Stars >= 0 ? StarsBandPx(vm) : 0f)
                                + (vm.TimeSeconds >= 0f ? TimePx : 0f)
                                + BandGapPx * Mathf.Max(0, parts.Count - 1);
                float cellPx = parts.Count > 0
                    ? SpoilsBodyWidthPx(_canvasH, PanelWidthFracFor(vm)) / parts.Count
                    : 0f;
                if (parts.Count > 0 && stripPx > 0f)
                {
                    bands.Add((stripPx, host =>
                    {
                        for (int i = 0; i < parts.Count; i++)
                        {
                            // Equal cells, left to right in the SAME order the stack read top to
                            // bottom, so the reveal (emblem 0.10s -> stars 0.18s -> time 0.20s)
                            // still sweeps in reading order.
                            var cell = MakeZone(host, "StripCell" + i,
                                                i / (float)parts.Count, 0f,
                                                (i + 1) / (float)parts.Count, 1f);
                            int ci = i;
                            Guard.Try("EndState", "narrative strip cell " + ci, () => parts[ci](cell));
                        }
                    }));
                    FlowTrace.Step("EndState",
                        $"narrative bands REFLOWED to a {parts.Count}-cell STRIP: emblem/stars/time " +
                        $"cost {stackedPx:0}px stacked and {stripPx:0}px side by side, against a " +
                        $"{MaxBodyWellPx(vm, _canvasH):0}px well ceiling at {spoilCols} spoils column(s) " +
                        $"(cell {cellPx:0}px vs a {MinStripCellPx:0}px floor). Reflowing is the fix; " +
                        "compressing every band below its own content size is the defect (WO-952).");
                }
            }
            else if (buildEmblem != null)
            {
                bands.Add((EmblemPx, buildEmblem));
            }

            if (buildSubtitle != null) bands.Add((subtitlePx, buildSubtitle));

            if (!strip)
            {
                if (buildStars != null) bands.Add((StarsBandPx(vm), buildStars));
                if (buildTime != null) bands.Add((TimePx, buildTime));
            }

            // Spoils: one band per ROW of the grid (2 columns in landscape, 1 in portrait).
            // SpoilBandCount is the same function RequiredBodyPx budgeted with, so the panel
            // solve and the layout can never disagree about how many bands there are.
            float spoilBodyPx = SpoilsBodyWidthPx(_canvasH, PanelWidthFracFor(vm));
            // THE THIRD LEVER, resolved through the SAME shared solver the panel solve used
            // (RequiredBodyPxAt -> SpoilRowsShown). BuildBody never trims on its own: a layout
            // that decided its own row count would be the exact solve/layout desync the two
            // levers above are commented against.
            int shownRows = SpoilRowsShown(vm, _canvasH, spoilCols, strip);
            int droppedRows = Mathf.Max(0, vm.Spoils.Count - shownRows);
            _shortfallRows = droppedRows;
            var spoilPlan = SpoilBandPlan(vm, spoilCols, shownRows);
            if (droppedRows > 0)
                // WARN, not Step: the F8 break-log captures Warn/Fail and DROPS Step (surveyed on
                // the device log for this capture — 263 [Flow:MagentaGuard] warns present, zero
                // EndState steps). An outcome nobody can read on the next capture is not evidence.
                FlowTrace.Warn("EndState",
                    $"spoils rows TRIMMED to fit: requested={vm.Spoils.Count} shown={shownRows} " +
                    $"dropped={droppedRows} (+1 shortfall band stating the difference) at " +
                    $"{spoilCols} column(s), strip={strip} - need " +
                    $"{RequiredBodyPxAtRows(vm, _canvasH, spoilCols, strip, vm.Spoils.Count):0}px " +
                    $"untrimmed vs a {MaxBodyWellPx(vm, _canvasH):0}px well ceiling, trimmed to " +
                    $"{RequiredBodyPxAtRows(vm, _canvasH, spoilCols, strip, shownRows):0}px. " +
                    "Dropping the tail LEGIBLY beats compressing every band below its own " +
                    "content size (WO-952); the count is stated on screen, never silent.");
            // ONE font size for EVERY wide row on this screen, solved before any of them is built
            // (owner F8 2026-09-02 defect #4: "DESTROYED, looted ..." rendered at the 30px autosize
            // floor while "damaged" rendered at the full 50, so two rows of the SAME KIND were 40%
            // apart). Fitting each row in isolation is what made them disagree; solving the whole
            // set once is what makes them agree.
            _wideRowFontPx = SolveWideRowFontPx(vm, spoilBodyPx, shownRows);
            // The separator band — same test, same order as RequiredBodyPx's (`spoilBands > 0
            // && n > 0`), so the two agree band-for-band. Empty builder: it exists to hold space.
            if (spoilPlan.Count > 0 && bands.Count > 0)
                bands.Add((SpoilsLeadGapPx, _ => { }));
            foreach (var band in spoilPlan)
            {
                var b = band;   // captured per band — never the loop variable
                bands.Add((RowPx, host => BuildSpoilBand(host, vm, b.first, b.count, spoilCols, spoilBodyPx)));
            }

            // Lay the bands out top-down at their OWN pixel heights. Only when the well is
            // still shorter than the total (panel extension clamped at 94% screen height)
            // do all bands compress by one uniform factor — logged, never silent.
            if (bands.Count == 0) return;
            Canvas.ForceUpdateCanvases();
            // OWNED GEOMETRY: use the well height the geometry pass SOLVED, not a creation-frame
            // rect read (that returns raw screen px before the CanvasScaler applies —
            // ElarionUiKit.cs:1014-1018). Compact banners still measure, as before.
            float wellH = _wellPx > 1f ? _wellPx : body.rect.height;
            float totalPx = BandGapPx * (bands.Count - 1);
            foreach (var b in bands) totalPx += b.px;
            float scale = wellH > 1f && totalPx > wellH ? wellH / totalPx : 1f;
            // ERROR, not a warning (owner F8 2026-08-05). Compression means EVERY band resolves
            // BELOW its own content's fixed size — the subtitle under its line box, the diamonds
            // under their rotated bbox, the rows under their icon. A screen that ships like that
            // is broken, and a Warn is exactly how it shipped unnoticed. Fail is loud, and the F8
            // harness captures it.
            //
            // EPSILON (owner captures 2026-08-08, twice: "need=412px well=412px scale=1").
            // BOTH solves are SELF-FITTING: the full modal sets panelPx = (need + CanonCta) /
            // BodyFracOfPanel and then derives wellPx back out of it, and the compact banner
            // grows by exactly need/well — so when neither hits its clamp the well resolves to
            // need to within float residue, and `scale < 1f` tripped a FAIL at scale 0.9997.
            // That is why the captured line printed IDENTICAL need and well numbers: a real
            // clamp leaves them different (a clamped well is visibly SHORTER than the need).
            // A hairline residue is not "every band below its content size" — it is the solve
            // landing on target. Fail below 0.995 (a real clamp lands far below that: the
            // 8-row damage report measured 0.71, the F8-35 case 0.36); log the exact fit as a
            // Step so the number is still on the record.
            const float CompressFailBelow = CompressFailBelowFrac;   // WO-952: ONE number, named once
            if (scale < CompressFailBelow)
                FlowTrace.Fail("EndState",
                    $"body rows COMPRESSED to fit: need={totalPx:0}px well={wellH:0}px scale={scale:0.###} " +
                    "- every band is now below its own content size. Since WO-952 REOPEN #3 this " +
                    "line should be UNREACHABLE on a full modal: the row trim (SpoilRowsShown) caps " +
                    "the need at MaxBodyWellPx before the panel is ever solved. If it fires, the " +
                    "trim's cap and Bind's SOLVED well have diverged - check that BodyFracOfPanel " +
                    "still equals BodyTopY - CtaBandY0 - CtaGapY, which is what makes the two " +
                    "derivations the same number. Do NOT answer it by raising MaxPanelHalf.");
            else if (scale < 1f)
                FlowTrace.Step("EndState",
                    $"body solved to an EXACT fit: need={totalPx:0.#}px well={wellH:0.#}px scale={scale:0.#####} " +
                    "- float residue from the self-fitting solve, not a clamp");
            float y = 0f;
            foreach (var (px, build) in bands)
            {
                var host = MakeZonePx(body, "Band", y, px * scale);
                y += (px + BandGapPx) * scale;
                build(host);
            }
        }

        /// <summary>One spoils BAND — up to <paramref name="cols"/> reward rows side by side.
        ///
        /// FILL ORDER = ROW-MAJOR (across, then down). The VM builds Spoils in descending
        /// importance (Experience, Wisdom, Wood, Iron, gear), and row-major is the order the eye
        /// already reads: it is the single vertical list of the wireframe simply folded, so
        /// "earlier = higher up, then left" still holds. Column-major (down, then across) would
        /// require the player to know the TOTAL count to know where the left column stops, which
        /// is unreadable at 2-3 rows.
        ///
        /// ODD TAIL: a lone final reward spans the FULL band width rather than sitting in a half
        /// cell beside an empty one. An empty cell reads as a reward that failed to load — the
        /// exact "icons are missing" complaint this WO is already fixing. A full-width capstone
        /// reads as deliberate, and on an arena win the odd tail IS the gear drop, the most
        /// notable line on the screen. So 5 rewards lay out as [1][2] / [3][4] / [ 5 ].
        ///
        /// 2026-09-02: the band's membership is no longer derived from a band INDEX (which
        /// assumed every band held exactly <paramref name="cols"/> cells). It is handed in by
        /// <see cref="SpoilBandPlan"/>, the one plan the panel solve also counts — that is what
        /// lets a <see cref="SpoilRowVM.Wide"/> damage row take a whole band without the solve
        /// and the layout disagreeing about how many bands exist.</summary>
        private void BuildSpoilBand(RectTransform host, EndStateVM vm, int first, int count,
                                    int cols, float bodyWidthPx)
        {
            if (vm == null || cols < 1) return;

            // ── THE SHORTFALL BAND (WO-952 REOPEN #3) ────────────────────────────────────
            // SpoilBandPlan emits ONE band at SummaryBandFirst when the row trim dropped rows.
            // It is the reason the trim is allowed to exist at all: the player is TOLD the
            // ledger is partial, so a hidden damage row can never read as "that building is
            // fine". Plain hyphens and no glyphs beyond Latin — the build font has a tofu
            // precedent (see BuildStarRow), same rule the damage-row copy follows.
            if (first == SummaryBandFirst)
            {
                int more = Mathf.Max(0, _shortfallRows);
                if (more <= 0) return;
                var note = ElarionUiKit.Label(host,
                    // Copy mirrors the VM's own compact-banner shortfall line ("Showing N of M
                    // damaged structures") rather than inventing a second voice for the same
                    // fact. Owner's call on the exact wording - it is player-facing.
                    "Showing " + (vm.Spoils.Count - more) + " of " + vm.Spoils.Count + " results",
                    0f, 1f, ElarionUi.Parchment, ElarionUi.FontLabel,
                    TMPro.TextAlignmentOptions.Center, 0.06f, 0.94f);
                ElarionUiKit.FitSingleLine(note);   // §1.14 — never spills its own band
                note.raycastTarget = false;
                Track(note.gameObject, 0.25f + more * 0.02f, 1f);
                return;
            }

            int remaining = vm.Spoils.Count - first;
            if (first < 0 || remaining <= 0) return;
            int inBand = Mathf.Clamp(count, 1, Mathf.Min(cols, remaining));
            bool fullWidth = inBand == 1;

            // WO-952: A SHORT TAIL BAND SPLITS THE FULL WIDTH BETWEEN THE CELLS IT ACTUALLY HAS,
            // never cols-many slots with the spare ones left blank. This is the SAME rule the lone
            // capstone above already follows ("an empty cell reads as a reward that failed to
            // load"), generalised - it only becomes reachable now that a grid can be 3 wide and a
            // tail can therefore hold 2 of 3. At cols <= 2 a short band is always inBand == 1, so
            // every existing layout is bit-for-bit unchanged.
            int slots = Mathf.Max(1, inBand);

            for (int c = 0; c < inBand; c++)
            {
                int itemIdx = first + c;
                // Cells split the band evenly with NO explicit gutter: each plate is already
                // inset 0.06 of its own cell, so two neighbours leave ~2x6% of clear space
                // between them. One less constant, and the single-column look is unchanged.
                float x0 = fullWidth ? 0f : c / (float)slots;
                float x1 = fullWidth ? 1f : (c + 1) / (float)slots;
                float cellPx = fullWidth ? bodyWidthPx : bodyWidthPx / slots;
                var cell = MakeZone(host, "SpoilCell" + itemIdx, x0, 0f, x1, 1f);
                int captured = itemIdx;
                // Stagger stays keyed to the ITEM index, so the reveal still sweeps in reading
                // order and the CTA (Track'd at Spoils.Count * 0.05) still lands last.
                Guard.Try("EndState", "spoils row " + captured,
                    () => BuildSpoilRow(cell, vm.Spoils[captured], 0.25f + captured * 0.05f, cellPx));
            }
        }

        /// <summary>The concept ids a spoils row offers the icon table, best first: its own label,
        /// then the DE-PLURALISED label. Both are the row's OWN text — no icon name is chosen in
        /// C#, the table still decides (ConceptIconResolver.ResolveAny takes the first that
        /// resolves, and Resolve(null) is a no-op, so a singular label costs nothing).
        ///
        /// The plural is why the raid victory's crystal row was broken: FromRaidVictory labels it
        /// "Crystals" (EndStateVM.cs:302) but concept-icons.json is keyed "crystal"
        /// (concept-icons.json:209), so it missed the table and fell through to icon_inventory —
        /// a CHEST (RpgUiCatalog.cs:220). The plural keys are now IN the table too (both the
        /// Resources and StreamingAssets copies); this stays as the belt to that braces, so a
        /// future plural label resolves even before someone remembers to add the key.</summary>
        private static string[] RowConcepts(SpoilRowVM row)
        {
            string concept = (row != null ? row.Label ?? "" : "").Trim().ToLowerInvariant();
            string singular = concept.Length > 3 && concept.EndsWith("s", StringComparison.Ordinal)
                ? concept.Substring(0, concept.Length - 1)
                : null;
            return new[] { concept, singular };
        }

        // The row's horizontal furniture in FIXED reference px. These were fractions of the plate,
        // which is why a 40px icon sat 105px away from its label on the wide landscape plate (the
        // 0.17 label inset resolved to ~160px) while being correct in portrait. A fraction cannot
        // hold a constant gap across a 2x width change; pixels can. Now the icon-to-label gap is
        // 14px on EVERY plate width, which matters far more with two columns halving the plate.
        private const float SpoilIconPx      = 40f;   // the fixed reward icon square (unchanged)
        private const float SpoilIconInsetPx = 18f;   // icon's left inset inside the plate
        private const float SpoilIconGapPx   = 14f;   // icon -> label
        private const float SpoilEdgeInsetPx = 18f;   // amount's right inset inside the plate

        /// <summary>The ONE font size every <see cref="SpoilRowVM.Wide"/> row on this screen
        /// renders at, in reference px. Solved once in <see cref="BuildBody"/> across ALL of them
        /// so two rows of the same kind can never resolve to different sizes; 0 until solved.</summary>
        private float _wideRowFontPx;

        /// <summary>How many spoils rows the third lever dropped on THIS screen — the number the
        /// shortfall band prints. 0 whenever nothing was trimmed (nearly always). Set in
        /// <see cref="BuildBody"/> before the bands are built.</summary>
        private int _shortfallRows;

        /// <summary>
        /// THE ONE icon resolution for a spoils row. Called by BOTH the wide-row column solve and
        /// <see cref="BuildSpoilRow"/>, so they can never disagree about whether an icon is eating
        /// 72 ref px of the plate (a solve that budgets a different plate than the layout draws is
        /// the desync class this whole file is a monument to).
        ///
        /// OWNER F8 2026-09-02, defect #3: the "North Gate" and "Wall x3" DAMAGE rows both drew a
        /// MONEY BAG. Mechanism: their model set no Icon, the label resolved no concept
        /// ("north gate" is in no icon table), and the last line here handed every unresolved row
        /// the generic loot fallback — RpgUiCatalog.IconInventory, which is a treasure chest
        /// (RpgUiCatalog.cs:220). That fallback exists so a REWARD row never blanks its slot; on a
        /// row reporting a LOSS it states the opposite of the truth, which is worse than a blank.
        ///
        /// So the generic chest is now offered only to rows that are NOT
        /// <see cref="SpoilRowVM.Wide"/> — Wide being the model's own declaration that this row is
        /// prose, not a resource line. A wide row therefore shows the icon its MODEL chose (the
        /// live damage rows carry RpgUiCatalog.IconShield, EndStateVM.FromWaveClear; the combined
        /// spoils tail and "Plans Recovered" carry IconInventory explicitly) or none at all. No
        /// icon name is chosen here and no string is sniffed.
        /// </summary>
        private static Sprite ResolveRowIcon(SpoilRowVM row)
        {
            if (row == null) return null;
            // WO-894: the reward CONCEPT gets first refusal via the resolver's designed OPT-IN
            // path (`override:true` in concept-icons.json), so any wrong reward icon is repointable
            // with ONE data entry and no C# change.
            var s = ConceptIconResolver.ResolveAnyOverride(RowConcepts(row));
            if (s == null) s = row.Icon;
            // The row LABEL is offered to the icon table (plural AND singular — see RowConcepts).
            if (s == null) s = ConceptIconResolver.ResolveAny(RowConcepts(row));
            if (s == null && !row.Wide)
                s = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconInventory);
            return s;
        }

        /// <summary>Reference px of a wide row's plate that is actually available to TEXT: the
        /// plate (0.88 of the cell) less the fixed furniture and the inter-column gutter.</summary>
        private static float WideRowTextPx(float cellWidthPx, bool hasIcon)
        {
            float platePx = Mathf.Max(200f, cellWidthPx * 0.88f);
            float furniture = (hasIcon ? SpoilIconInsetPx + SpoilIconPx + SpoilIconGapPx
                                       : SpoilIconInsetPx)
                              + SpoilEdgeInsetPx + WideRowGutterPx;
            return Mathf.Max(120f, platePx - furniture);
        }

        /// <summary>A wide row's LABEL share of its text region, in proportion to the MEASURED
        /// words on both sides. A wide row carries two different grammars depending on who built
        /// it — the live wave report puts the whole sentence in the LABEL ("North Gate - DESTROYED,
        /// looted 120") and the cost in the AMOUNT, while other producers put a short name left and
        /// the prose right — so no fixed split can serve both, and a string-LENGTH test is the
        /// estimate this file already learned not to trust (see <see cref="MeasureTextPx"/>).
        /// Measure both, split in proportion, bound it so one empty cell cannot eat the plate.</summary>
        private static float WideLabelShare(SpoilRowVM row)
        {
            if (row == null) return 0.5f;
            float lw = MeasureTextPx(row.Label ?? string.Empty, ElarionUi.FontBody);
            float aw = MeasureTextPx(row.Amount ?? string.Empty, ElarionUi.FontBody) * WideAmountBoldFactor;
            float total = lw + aw;
            if (total < 1f) return 0.5f;
            return Mathf.Clamp(lw / total, WideLabelMinShare, WideLabelMaxShare);
        }

        /// <summary>The shared wide-row font size (reference px): the largest size at which EVERY
        /// wide row's two columns still seat their measured words, floored at the kit's own
        /// legibility floor. Returns FontBody when the screen carries no wide row.</summary>
        private static float SolveWideRowFontPx(EndStateVM vm, float bodyWidthPx, int shown)
        {
            if (vm == null) return ElarionUi.FontBody;
            float scale = 1f;
            bool any = false;
            // WO-952 REOPEN #3: solve over the rows this screen SHOWS, not every row the VM
            // carries. A trimmed-away row is not on screen, and letting its (often longest)
            // sentence set the shared size would shrink the rows the player CAN see to fit one
            // he cannot — the row-shape inconsistency this solver exists to prevent, inverted.
            int limit = Mathf.Clamp(shown, 0, vm.Spoils.Count);
            for (int i = 0; i < limit; i++)
            {
                var row = vm.Spoils[i];
                if (row == null || !row.Wide) continue;
                any = true;
                // A wide row always takes a band ALONE at the full body width (SpoilBandPlan), so
                // its cell IS the body well.
                float textPx = WideRowTextPx(bodyWidthPx, ResolveRowIcon(row) != null);
                float share  = WideLabelShare(row);
                float lw = MeasureTextPx(row.Label ?? string.Empty, ElarionUi.FontBody);
                float aw = MeasureTextPx(row.Amount ?? string.Empty, ElarionUi.FontBody) * WideAmountBoldFactor;
                if (lw > 1f) scale = Mathf.Min(scale, textPx * share / lw);
                if (aw > 1f) scale = Mathf.Min(scale, textPx * (1f - share) / aw);
            }
            if (!any) return ElarionUi.FontBody;
            float solved = Mathf.Clamp(ElarionUi.FontBody * scale,
                                       ElarionUiKit.FontFloor, ElarionUi.FontBody);
            FlowTrace.Step("EndState",
                $"wide spoils rows share ONE font: {solved:0.#}px (body column {bodyWidthPx:0}px, " +
                $"raw fit {ElarionUi.FontBody * scale:0.#}px, floor {ElarionUiKit.FontFloor:0})" +
                (ElarionUi.FontBody * scale < ElarionUiKit.FontFloor
                    ? " - AT THE FLOOR, a wide row may still ellipsise" : string.Empty));
            return solved;
        }

        /// <summary>One spoils row: kit slot plate + icon (null-safe) + label + amount.
        /// <paramref name="cellWidthPx"/> is the row's own cell width in reference px (a full
        /// body well in one column, half of it in two) — it converts the pixel insets above into
        /// the fractions the anchors need.</summary>
        private void BuildSpoilRow(RectTransform host, SpoilRowVM row, float revealDelay, float cellWidthPx)
        {
            if (row == null) return;
            // The plate spans 0.06..0.94 of the cell, so it is 0.88 of it. Floored so a bad
            // measurement can never produce inset fractions above 1 (which would invert a rect).
            float platePx = Mathf.Max(200f, cellWidthPx * 0.88f);
            // WO-714 W2 (pack row-list grammar): the row plate is the REAL Blink Stat_Element
            // (element/element_stat, 9-sliced — the same plate CurrencyChip and the HUD stat rows
            // sit on), resolved sprite-first ALWAYS (P9 — never gated on ff.blinkchrome). On the
            // real plate the pack's embossed steel carries the depth, so no procedural accent:
            // gold stays reserved for content (the amount / gilt values), never chrome.
            // Null-art fallback = the previous (#23) procedural obsidian tile + thin gold left
            // bar, byte-for-byte — an art-absent run never blanks a reward row.
            GameObject plate;
            var plateSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleElement, RpgUiCatalog.ElementStat);
            if (plateSprite != null)
            {
                plate = ElarionUiKit.AddImage(host, "SpoilRow",
                    new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.96f), Color.white, rounded: false);
                var pImg = plate.GetComponent<Image>();
                pImg.sprite = plateSprite;
                pImg.type = Image.Type.Sliced;
                pImg.raycastTarget = false;
            }
            else
            {
                plate = ElarionUiKit.AddImage(host, "SpoilRow",
                    new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.96f), ElarionUiKit.ObsidianFill);
                plate.GetComponent<Image>().raycastTarget = false;
                var accent = ElarionUiKit.AddImage(plate.transform, "GoldAccent",
                    new Vector2(0f, 0.12f), new Vector2(0.02f, 0.88f), ElarionUiKit.ObsidianTrim, rounded: false);
                accent.GetComponent<Image>().raycastTarget = false;
            }
            // Icon: sprite-first from the VM; when the VM had no sheet art for the item (R4: "Wood"
            // — ItemIconCatalog.ForConsumable("mat_wood") resolves null today, EndStateVM.cs:120-122)
            // fall back to a generic resource/loot icon so a reward row NEVER blanks its slot. Same
            // RpgUiCatalog.Get path the other rows resolve through on the model side.
            // WO-894: the reward CONCEPT gets first refusal via the resolver's designed OPT-IN
            // path. ResolveAnyOverride returns a sprite ONLY for entries flagged `override:true`
            // in concept-icons.json (ConceptIconResolver.cs:136-152), so today it returns null for
            // every reward row and nothing changes — but it means any wrong reward icon can be
            // repointed by adding ONE data entry, with no C# change and no icon name chosen in
            // code. That is the lever for "Wisdom" (see the RESULT notes: it currently shows
            // icon_tree, which RpgUiCatalog.cs:226 itself documents as a campfire stand-in, and
            // Resources holds no sprite that reads as wisdom to swap it for).
            // SWEEP 9413 R2 (#7) / WO-894 / F8 2026-09-02 defect #3: the whole ladder — concept
            // override, the model's own sprite, the label's concept (plural AND singular), and the
            // generic fallback that a WIDE row is deliberately never offered — lives in ONE place
            // now, because the wide-row column solve has to budget the SAME plate this draws.
            var iconSprite = ResolveRowIcon(row);
            // Icon size first: the label's left inset is measured from the icon's REAL right
            // edge, so a band-clamped (compressed) icon does not leave a hole beside itself.
            float iconPx = Mathf.Min(SpoilIconPx, host.rect.height * 0.80f);
            float labelLeftFrac = iconSprite != null
                ? (SpoilIconInsetPx + iconPx + SpoilIconGapPx) / platePx
                : SpoilIconInsetPx / platePx;
            float amountRightFrac = 1f - SpoilEdgeInsetPx / platePx;

            if (iconSprite != null)
            {
                var go = new GameObject("Icon", typeof(Image));
                go.transform.SetParent(plate.transform, false);
                var img = go.GetComponent<Image>();
                img.sprite = iconSprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                var rt = img.rectTransform;
                // Fresh-capture sweep 2026-07-06: fraction-sized icons collapsed to ~12px
                // when the band stack squeezed the rows. Fixed 40x40 reference-unit square
                // (>= 24 screen px at the 720p landscape scale), anchored middle-left —
                // the icon never shrinks with its band.
                float iconLeftFrac = SpoilIconInsetPx / platePx;
                rt.anchorMin = new Vector2(iconLeftFrac, 0.5f);
                rt.anchorMax = new Vector2(iconLeftFrac, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                // OWNER F8 2026-08-05 (icons floating off their bars): the 40px square is
                // vertically CENTRED, so in a row plate that resolved to 18.7px it spilled
                // ~10.6px above AND below the bar. 40 is still the size we want — RowPx 64
                // seats it natively — but clamp it to the host band so a compressed row can
                // never put the icon outside its own plate again.
                rt.sizeDelta = Vector2.one * iconPx;
            }
            // F8-35: label left / value right, each FIT to ONE line in its own column —
            // "Equipped" wrapped to "Equipp/d" and long gear names spilled into the value
            // column at the fixed FontBody size. FitSingleLine (§1.14) shrinks-to-fit with
            // ellipsis so neither side can ever wrap or cross the column split again.
            // COLUMN SPLIT. A resource row is a short noun against a short "+180", so the fixed
            // 0.62/0.64 split is right for it and is unchanged.
            //
            // OWNER F8 2026-09-02, defects #1 and #4. The wide row's FIXED 0.70/0.72 split — which
            // widened the LABEL column and left the VALUE column at 0.26 of the plate — is what
            // survived the first pass: "DESTROYED, looted 120" is ~500 ref px of PROSE at FontBody
            // 50 into a ~274 px cell, and FitSingleLine's autosize bottoms out at FontFloor 30
            // (~315 px) and then ELLIPSISES. That produced BOTH the surviving truncation and the
            // size disagreement with the "damaged" row beside it, which needed no shrink at all.
            //
            // A wide row is prose against prose, and which SIDE carries the long string depends on
            // who built the row (see WideLabelShare). So split it in proportion to the measured
            // words, and render every wide row on the screen at the ONE size solved across all of
            // them (SolveWideRowFontPx) so same-kind rows can never disagree again.
            float labelRightFrac, amountLeftFrac;
            float rowFontPx = ElarionUi.FontBody;
            if (row.Wide)
            {
                float gutterFrac = WideRowGutterPx / platePx;
                float colsFrac = Mathf.Max(0.10f, amountRightFrac - labelLeftFrac - gutterFrac);
                labelRightFrac = labelLeftFrac + colsFrac * WideLabelShare(row);
                amountLeftFrac = labelRightFrac + gutterFrac;
                rowFontPx = _wideRowFontPx > 1f ? _wideRowFontPx : ElarionUi.FontBody;
            }
            else
            {
                labelRightFrac = 0.62f;
                amountLeftFrac = 0.64f;
            }
            // ElarionUiKit.Label takes an INT size (ElarionUiKit.cs:1889); the fitter below keeps
            // the float bound, so the rounding costs at most half a reference pixel.
            int rowFontInt = Mathf.Max(Mathf.RoundToInt(ElarionUiKit.FontFloor),
                                       Mathf.RoundToInt(rowFontPx));
            var label = ElarionUiKit.Label(plate.transform, row.Label ?? "", 0f, 1f,
                ElarionUi.Parchment, rowFontInt, TMPro.TextAlignmentOptions.MidlineLeft,
                labelLeftFrac, labelRightFrac);
            // EXPLICIT max: the solved size IS the answer for a wide row, so the fitter may only
            // act as the last-resort net below it (never as a second, per-row opinion).
            ElarionUiKit.FitSingleLine(label, 0f, rowFontInt);
            label.raycastTarget = false;
            var amount = ElarionUiKit.Label(plate.transform, row.Amount ?? "", 0f, 1f,
                ElarionUi.Gilt, rowFontInt, TMPro.TextAlignmentOptions.MidlineRight,
                amountLeftFrac, amountRightFrac, bold: true);
            ElarionUiKit.FitSingleLine(amount, 0f, rowFontInt);
            amount.raycastTarget = false;
            Track(plate, revealDelay, 0.96f);
        }

        // ── ④ STAR ROW (WO-894) ───────────────────────────────────────────────────
        // Every number here is the WO's §3 spacing table / §4.2 spin table, named so a
        // future reader can diff the code against the spec without re-deriving anything.
        private const float StarSizePx        = 56f;    // §3: star diameter (square bbox 56x56)
        private const float StarSpacingPx     = 80f;    // §3: centre-to-centre => centres at -80 / 0 / +80
        private const float StarsBaseDelay    = 0.18f;  // §4.3: between the subtitle (0.14) and time (0.20) beats
        private const float StarStaggerSec    = 0.15f;  // §4.2: star i starts at base + i * 0.15, left->right
        private const float StarSpinSec       = 0.40f;  // §4.2: rotation + scale duration
        private const float StarSpinDegrees   = 540f;   // §4.2: +540 -> 0 = 1.5 clockwise turns
        private const float StarFadeSec       = 0.12f;  // §4.2: alpha 0 -> 1 over the first 0.12s, linear
        private const float StarLandPulseSec  = 0.12f;  // §4.2: the landing stamp
        private const float StarLandPulse     = 0.08f;  // §4.2: 1.0 -> 1.08 -> 1.0
        private const float StarTwinkleAmp    = 0.03f;  // §4.2: idle +-3% scale...
        private const float StarTwinkleHz     = 0.5f;   // §4.2: ...at ~0.5 Hz (NEVER a perpetual full spin)
        private const float StarOvershootC1   = 2.17f;  // ease-out-back constant solved for a 1.15 peak (see EaseOutBack)
        private const float UnearnedStarAlpha = 0.14f;  // §4.1: dim OUTLINE variant at ~14%
        private const float UnearnedFadeSec   = 0.20f;  // §4.1: unearned stars fade in, they never spin

        /// <summary>Rating row (WO-894): three REAL 5-point stars — earned ones SPIN in
        /// (540deg -> 0) with an overshoot pop, staggered left-to-right, then land and settle
        /// into a gentle twinkle; unearned ones are a dim OUTLINE star that only fades.
        ///
        /// The old pips were 45-degree rotated squares (diamonds). That was NOT a font
        /// fallback — it was a deliberate sprite-free workaround for the build font having no
        /// TMP star glyph. See <see cref="StarSolidSprite"/> for why the replacement is a
        /// generated sprite rather than a glyph or a pack asset.
        ///
        /// COLOURBLIND LAW (owner is red/green colourblind): the rating reads by the NUMBER OF
        /// FILLED SHAPES and by SHAPE (solid star vs hollow outline) — never by hue. There is
        /// deliberately no "n/3" numeral on this row either: the HUD font renders the numeral 1
        /// as a bare vertical stroke, which would be unreadable beside a slash or a star point.
        ///
        /// <para>WO-1789 — <paramref name="caption"/> is ONE short line seated directly under the
        /// stars, in the band's own <see cref="StarCaptionPx"/> allowance. It exists because two
        /// outcomes this screen decides were previously written only to the log: a raid win short of
        /// <c>RaidDeployController.VeterancyStarsRequired</c> granted no ranks and said nothing at
        /// all. Null/empty on every other screen, and then this method behaves exactly as it did —
        /// the stars occupy the whole band and the star size is unchanged to the pixel.</para></summary>
        /// <param name="host">The star BAND (already <see cref="StarsBandPx"/> tall).</param>
        /// <param name="stars">Earned stars, clamped 0..3.</param>
        /// <param name="caption">Optional single line under the row; null/empty renders nothing.</param>
        private void BuildStarRow(RectTransform host, int stars, string caption = null)
        {
            // THE BAND'S SPLIT. With no caption the stars own all of it (frac 1) and every shipped
            // screen is bit-for-bit unchanged; with one, the stars keep their AUTHORED StarsPx share
            // and the caption is seated in the surplus StarsBandPx already budgeted for it. The stars
            // are NEVER shrunk to make room — the rating is read by shape and size (see above).
            bool hasCaption = !string.IsNullOrEmpty(caption);
            float starsFrac = hasCaption ? StarsPx / (StarsPx + StarCaptionPx) : 1f;

            var rowGo = new GameObject("Stars", typeof(RectTransform));
            rowGo.transform.SetParent(host, false);
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.anchorMin = new Vector2(0f, 1f - starsFrac); rowRt.anchorMax = Vector2.one;
            rowRt.offsetMin = Vector2.zero; rowRt.offsetMax = Vector2.zero;

            // DEGRADE LADDER (orchestrator ruling: the row must never VANISH — a rating that
            // silently disappears is worse than diamonds, because the player cannot tell 3 stars
            // from 0). §12 no silent failure: every rung logs.
            //   1. the generated 5-point star        (the deliverable)
            //   2. the kit's circular pip            (a real sprite, still a countable shape)
            //   3. the legacy 45-degree square       (the pre-WO diamond — ugly, but VISIBLE)
            var solid   = StarSolidSprite;
            var outline = StarOutlineSprite;
            bool legacyDiamond = false;
            if (solid == null)
            {
                solid = ElarionUiKit.CircleSprite;
                outline = solid;
                FlowTrace.Fail("EndState", solid != null
                    ? "star sprite build failed - degraded to the kit circle pip"
                    : "star AND circle sprite builds both failed - degraded to the legacy 45deg square");
                legacyDiamond = solid == null;
            }
            if (outline == null) outline = solid;

            // §3 wants a FIXED 56px star. Clamp it to the band anyway: when BuildBody's
            // uniform compression fires (it logs a Fail when it does) the band resolves BELOW
            // 72px, and a hard 56 would then overhang its own band and print through the Time
            // line above/below — the very defect the 2026-08-05 pass fixed for the diamonds.
            // 72 * 0.78 = 56.16, so at the authored band size this yields exactly the §3 56px.
            // WO-1789: the clamp measures the STARS' OWN SHARE of the band (host.rect.height x
            // starsFrac), never the whole band, or a captioned band would clamp against pixels the
            // caption owns and the stars would overhang it — the same overprint the 2026-08-05 pass
            // fixed. With no caption starsFrac is 1 and this is the identical expression it was.
            float size = Mathf.Max(8f, Mathf.Min(StarSizePx, host.rect.height * starsFrac * 0.78f));
            // A star (and a circle) is RADIALLY bounded, so its bbox is its size. A rotated
            // SQUARE is not: its axis-aligned box is side * sqrt(2), so the legacy rung has to
            // shrink or it overprints its neighbours (the original 2026-08-05 defect).
            if (legacyDiamond) size /= 1.414f;

            for (int i = 0; i < 3; i++)
            {
                bool earned = i < stars;
                var go = new GameObject("Star" + i, typeof(Image));
                go.transform.SetParent(rowRt, false);
                var img = go.GetComponent<Image>();
                // On the legacy rung sprite stays NULL on purpose: a sprite-less Image draws a
                // white quad, which rotated 45deg is exactly the old diamond. Visible beats absent.
                img.sprite = earned ? solid : outline;
                img.preserveAspect = true;
                img.raycastTarget = false;   // §4.1: the rating is never a touch target
                if (legacyDiamond) img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

                // Earned = solid GOLD star; unearned = the hollow outline at ~14%. The hue is the
                // decoration; the SHAPE and the COUNT carry the meaning (colourblind law).
                var tint = earned ? ElarionUiKit.ObsidianTrim
                                  : new Color(1f, 1f, 1f, UnearnedStarAlpha);
                img.color = new Color(tint.r, tint.g, tint.b, 0f);   // the reveal owns alpha

                // §3 EXACT: anchor + pivot dead-centre, centres at anchoredPosition.x -80 / 0 / +80.
                // FIXED PIXELS, never a fraction of the parent — the previous 0.13-of-width spacing
                // resolved to ~144px on the 2670x1200 landscape panel and ~70px on a narrow one, so
                // the group was a different shape on every device.
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((i - 1) * StarSpacingPx, 0f);
                rt.sizeDelta = new Vector2(size, size);

                // §4.4: earned stars own their OWN tween — they must NOT ride the generic
                // Track/RevealRoutine, which has no rotation curve. Unearned stars just fade.
                // On the legacy rung NOTHING spins: the spin lands at rotation 0, which would
                // straighten the diamond back into a square mid-animation.
                float delay = StarsBaseDelay + i * StarStaggerSec;
                if (earned && !legacyDiamond) StartCoroutine(SpinStarIn(img, rt, delay, tint.a));
                else                          StartCoroutine(FadeStarIn(img, delay, tint.a));
            }

            // ── THE CAPTION (WO-1789) ────────────────────────────────────────────────────
            // The outcome the star count DECIDED, said on the row that decided it. Seated in the
            // band's own StarCaptionPx surplus (see StarsBandPx), so it is budgeted by the panel
            // solve and cannot be discovered after layout.
            //
            // COLOURBLIND LAW: the caption carries its meaning in WORDS, in the same Parchment the
            // subtitle uses. It is deliberately NOT tinted "denied" red — the owner is red/green
            // colourblind, so a hue would carry nothing and Parchment carries everything.
            if (hasCaption)
            {
                Guard.Try("EndState", "star row caption", () =>
                {
                    var zone = MakeZone(host, "StarCaption", 0f, 0f, 1f, 1f - starsFrac);
                    var l = ElarionUiKit.Label(zone, caption, 0f, 1f, ElarionUi.Parchment,
                        ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f);
                    // §1.14, and the same fitter the Time line uses: ONE line that can never spill
                    // onto the stars above it or the band below, including inside a 1/3-width strip
                    // cell where this is the narrowest host on the screen.
                    ElarionUiKit.FitSingleLine(l);
                    l.raycastTarget = false;
                    // AFTER THE LAST STAR HAS LANDED, so the row reads before the sentence about the
                    // row does. The arithmetic, from this file's own constants rather than a guess:
                    // star 2 starts at StarsBaseDelay + 2 x StarStaggerSec = 0.48 and its spin runs
                    // StarSpinSec = 0.40, so the row settles at 0.88.
                    Track(l.gameObject, 0.90f, 1f);
                });
            }

            FlowTrace.Step("EndState",
                $"star row: {Mathf.Clamp(stars, 0, 3)}/3 earned, star={size:0.#}px (band {host.rect.height:0.#}px, " +
                $"stars share {starsFrac:0.00}) centres -{StarSpacingPx:0}/0/+{StarSpacingPx:0}px, " +
                $"spin {StarSpinDegrees:0}deg over {StarSpinSec:0.00}s" +
                (hasCaption ? $"; CAPTION '{caption}' in a {StarCaptionPx:0}px allowance (WO-1789)"
                            : "; no caption"));
        }

        // ── the SPIN (WO-894 §4.2) ────────────────────────────────────────────────
        // All on Time.unscaledDeltaTime: this screen never pauses time, and the hero-death
        // variant narrates a coroutine that runs on SCALED time — same rule as RevealRoutine.

        /// <summary>Ease-out-back with the overshoot constant SOLVED for the WO's exact 1.15 peak.
        /// The textbook c1 = 1.70158 only reaches 1.099. The peak of this curve is
        /// 1 + 4c1^3 / (27(c1+1)^2), which equals 1.150 at c1 = 2.17. f(0) = 0 and f(1) = 1 hold
        /// for any c1, so the "0.0 -> 1.15 -> 1.0" of the spec is exact, not approximated.</summary>
        private static float EaseOutBack(float u)
        {
            const float c1 = StarOvershootC1;
            const float c3 = c1 + 1f;
            float p = u - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        /// <summary>One EARNED star: spin 540deg -> 0 (ease-out-cubic) while popping 0 -> 1.15 -> 1
        /// (ease-out-back) and fading in over the first 0.12s, then a land pulse, then a gentle
        /// forever-twinkle. Never a continuous full spin — that reads as a loading spinner.</summary>
        private IEnumerator SpinStarIn(Image img, RectTransform rt, float delay, float targetAlpha)
        {
            if (img == null || rt == null) yield break;
            rt.localScale = Vector3.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, StarSpinDegrees);

            float t = 0f;
            while (t < delay)
            {
                t += Time.unscaledDeltaTime;
                if (img == null || rt == null) yield break;   // torn down before its turn
                yield return null;
            }

            t = 0f;
            while (t < StarSpinSec)
            {
                t += Time.unscaledDeltaTime;
                if (img == null || rt == null) yield break;
                float u = Mathf.Clamp01(t / StarSpinSec);
                float spun = 1f - Mathf.Pow(1f - u, 3f);      // ease-out-cubic on the rotation
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(StarSpinDegrees, 0f, spun));
                rt.localScale = Vector3.one * EaseOutBack(u);
                var c = img.color;
                c.a = targetAlpha * Mathf.Clamp01(t / StarFadeSec);   // linear, first 0.12s
                img.color = c;
                yield return null;
            }
            if (img == null || rt == null) yield break;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            var landed = img.color; landed.a = targetAlpha; img.color = landed;

            // LAND PULSE: a half-sine stamp so the star arrives with weight.
            t = 0f;
            while (t < StarLandPulseSec)
            {
                t += Time.unscaledDeltaTime;
                if (rt == null) yield break;
                float u = Mathf.Clamp01(t / StarLandPulseSec);
                rt.localScale = Vector3.one * (1f + StarLandPulse * Mathf.Sin(Mathf.PI * u));
                yield return null;
            }

            // IDLE TWINKLE: +-3% at ~0.5 Hz, forever. Cheap (one scale write/frame/star) and it
            // stops on its own when the screen tears down (the coroutine host dies with it).
            t = 0f;
            while (rt != null)
            {
                t += Time.unscaledDeltaTime;
                rt.localScale = Vector3.one *
                    (1f + StarTwinkleAmp * Mathf.Sin(2f * Mathf.PI * StarTwinkleHz * t));
                yield return null;
            }
        }

        /// <summary>One UNEARNED star (§4.1): a plain 0.2s fade to the dim alpha at its own slot.
        /// No spin — only what you EARNED spins, so the count reads from the motion too.</summary>
        private IEnumerator FadeStarIn(Image img, float delay, float targetAlpha)
        {
            if (img == null) yield break;

            float t = 0f;
            while (t < delay)
            {
                t += Time.unscaledDeltaTime;
                if (img == null) yield break;
                yield return null;
            }

            t = 0f;
            while (t < UnearnedFadeSec)
            {
                t += Time.unscaledDeltaTime;
                if (img == null) yield break;
                var c = img.color;
                c.a = targetAlpha * Mathf.Clamp01(t / UnearnedFadeSec);
                img.color = c;
                yield return null;
            }
            if (img == null) yield break;
            var done = img.color; done.a = targetAlpha; img.color = done;
        }

        // ── the STAR SPRITE (WO-894 §4.1) ─────────────────────────────────────────
        // WHY GENERATED, and not a glyph or a pack asset — verified in the tree, not assumed:
        //   • NO star sprite is reachable at runtime. RpgUiCatalog exposes crown_tier1..3
        //     (Resources/RpgUi/crown/) and no star role at all; the only star*.png in the
        //     project live in VFX packs (Hovl / Lana / Mirza) OUTSIDE Assets/Resources, so
        //     Resources.Load can never see them, and they are soft particle glows, not UI art.
        //   • A TMP star glyph is out: the build font tofu'd it (that tofu is the whole reason
        //     the row was drawing rotated squares in the first place).
        //   • The crown art is out: it carries a white fringe (owner F8).
        // So the star is BUILT — the same lazily-cached, try/catch-guarded, null-safe way the kit
        // builds its own rounded / circle / ring sprites (ElarionUiKit.cs:2288-2424). No import
        // step, no missing-art path, and it renders identically in a player build.
        // KIT-PROMOTION CANDIDATE (alongside RevealRoutine) once a second screen needs a star.

        private const int   StarTexSize    = 128;    // texture px; drawn at 56 ref px, so ~2x for crisp points
        private const float StarInnerRatio = 0.45f;  // inner/outer radius — fatter than a pentagram (0.382)
                                                     // so the points stay solid and legible at phone size
        private const float StarStrokePx   = 5f;     // outline-variant stroke, texture px (~2.2px at 56)

        private static Sprite _starSolid;   private static bool _starSolidTried;
        private static Sprite _starOutline; private static bool _starOutlineTried;

        /// <summary>Filled 5-point star, white with a baked top-lit bevel so an
        /// <c>Image.color</c> gold tint reads as gilded metal. Null only if texture creation
        /// itself failed (caller falls back — never a white quad).</summary>
        private static Sprite StarSolidSprite
        {
            get
            {
                if (!_starSolidTried)
                {
                    _starSolidTried = true;
                    try { _starSolid = BuildStarSprite(false); }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[EndState] solid star sprite build failed: " + e.Message);
                        _starSolid = null;
                    }
                }
                return _starSolid;
            }
        }

        /// <summary>Hollow 5-point star (stroke only) — the UNEARNED slot. A different SHAPE, not
        /// just a dimmer colour, so the earned count survives any colour perception.</summary>
        private static Sprite StarOutlineSprite
        {
            get
            {
                if (!_starOutlineTried)
                {
                    _starOutlineTried = true;
                    try { _starOutline = BuildStarSprite(true); }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[EndState] outline star sprite build failed: " + e.Message);
                        _starOutline = null;
                    }
                }
                return _starOutline;
            }
        }

        private static Sprite BuildStarSprite(bool hollow)
        {
            const int size = StarTexSize;
            const float half = size * 0.5f;
            const float outerPx = half - 3f;   // 3px margin for the AA ramp / bevel

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                // Baked top-lit ramp: bright at the top point, ~0.72 at the bottom points. It is a
                // MULTIPLIER on Image.color, so the gold tint comes out as a gilded gradient rather
                // than a flat sticker. The outline variant stays flat white (it is barely visible
                // at 14% alpha; a gradient there would just make it read as noise).
                float lumRow = hollow ? 1f : Mathf.Lerp(0.72f, 1f, (y + 0.5f) / size);
                for (int x = 0; x < size; x++)
                {
                    // Evaluate the SDF in UNIT space (outer radius 1.0) then scale back to texture
                    // px, so the 1px alpha ramp below is a true 1px feather at any texture size.
                    float d = Star5Distance((x + 0.5f - half) / outerPx,
                                            (y + 0.5f - half) / outerPx,
                                            StarInnerRatio) * outerPx;

                    float a = hollow
                        ? Mathf.Clamp01(StarStrokePx * 0.5f - Mathf.Abs(d) + 0.5f)   // ring around the edge
                        : Mathf.Clamp01(0.5f - d);                                   // solid interior
                    if (a <= 0f) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }

                    // Soft ~2px bevel: darken the last band inside the edge so the star has a rim
                    // and does not dissolve into a bright panel.
                    float lum = lumRow;
                    if (!hollow && d > -2.5f) lum *= 0.62f;

                    byte c8 = (byte)Mathf.Clamp(Mathf.RoundToInt(lum * 255f), 0, 255);
                    px[y * size + x] = new Color32(c8, c8, c8,
                        (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Signed distance from (<paramref name="x"/>,<paramref name="y"/>) to a regular
        /// POINT-UP 5-pointed star of outer radius 1 and inner/outer ratio <paramref name="inner"/>;
        /// negative inside. The standard polar-fold star field: mirror in x, reflect across the two
        /// pentagon edge normals (cos/sin of pi/5), mirror again — which folds the whole plane onto
        /// ONE star edge, so the answer is the exact distance to that single segment. Exact distance
        /// (not a bounded approximation) is what lets a flat 1px alpha ramp antialias all five points
        /// and all five notches cleanly.</summary>
        private static float Star5Distance(float x, float y, float inner)
        {
            const float k1x = 0.809016994f, k1y = -0.587785252f;   // cos(pi/5), -sin(pi/5)
            const float k2x = -k1x, k2y = k1y;

            x = Mathf.Abs(x);
            float d1 = x * k1x + y * k1y;
            if (d1 > 0f) { x -= 2f * d1 * k1x; y -= 2f * d1 * k1y; }
            float d2 = x * k2x + y * k2y;
            if (d2 > 0f) { x -= 2f * d2 * k2x; y -= 2f * d2 * k2y; }
            x = Mathf.Abs(x);
            y -= 1f;                                   // origin -> the star's tip

            // The one surviving edge: tip (0,0) -> inner vertex, at radius `inner` and 54deg.
            float bax = inner * -k1y;                  // = inner * sin(pi/5)
            float bay = inner * k1x - 1f;
            float h = Mathf.Clamp01((x * bax + y * bay) / (bax * bax + bay * bay));
            float dx = x - bax * h, dy = y - bay * h;
            return Mathf.Sqrt(dx * dx + dy * dy) * Mathf.Sign(y * bax - x * bay);
        }

        // ── actions / lifecycle ───────────────────────────────────────────────

        /// <summary>How many times the primary gate has refused this screen's route (WO-1778).</summary>
        private int _gateRefusals;

        /// <summary>Fire the VM's primary action exactly once, then tear down.</summary>
        private void FirePrimary() => FirePrimary(false);

        /// <summary>
        /// Fire the VM's primary action exactly once, then tear down.
        ///
        /// <para>⛔ WO-1778 — A REFUSED GATE USED TO BE A DEAD END, INCLUDING FOR THE GUARD. This
        /// returned before <c>Destroy(gameObject)</c> on a refusal, so the anti-softlock guard
        /// below fired ONCE into the same refusal and could not clear the screen: the player was
        /// left on a victory screen whose only CTA was a no-op (a 3-star capture with a missing
        /// precombat census). Every refusal is now TRACED, and the guard's LAST attempt passes
        /// <paramref name="forceDismissIfGateRefuses"/> so the screen always comes down.</para>
        ///
        /// <para>A player TAP never forces: a tap is a request for the route, and silently
        /// destroying the screen under the finger would throw away the action instead of running
        /// it. Only the guard — the surface whose whole job is to break a softlock — may dismiss a
        /// screen whose route refuses, and it says so loudly through
        /// <see cref="AbandonedPrimaryWarn"/>.</para>
        /// </summary>
        private void FirePrimary(bool forceDismissIfGateRefuses)
        {
            if (_fired) return;
            if (_vm.PrimaryGate != null && !_vm.PrimaryGate())
            {
                _gateRefusals++;
                FlowTrace.Warn("EndState", $"'{(_vm.Title ?? "?")}' primary GATE REFUSED " +
                    $"(refusal #{_gateRefusals}) — action={_vm.PrimaryRoute} was NOT run and the screen stays up.");
                if (!forceDismissIfGateRefuses) return;

                AbandonedPrimaryWarn($"the anti-softlock guard dismissing a screen whose primary gate " +
                                     $"refused {_gateRefusals} time(s) — WO-1778: never leave a screen whose only CTA is a no-op");
                _fired = true;
                Destroy(gameObject);
                return;
            }
            _fired = true;
            FlowTrace.Step("EndState", $"{_vm.Kind} primary fired: action={_vm.PrimaryRoute}");
            // F8-15: the continue/respawn path OUT of the death screen — name the route the
            // player chose so the death window shows the full open->close lifecycle.
            DeathTrace.Note($"END-STATE CONTINUE: '{_vm.Title}' primary fired -> action={_vm.PrimaryRoute} (screen tearing down)");
            var act = _vm.Primary;
            _vm.Primary = null;
            act?.Invoke();
            Destroy(gameObject);
        }

        /// <summary>WO-672 Slice E: fire the banner CTA (Repair All) exactly once, then
        /// dismiss the banner via the normal primary route. The repaired-summary lands
        /// through WallRepairController.FeedbackShown (the existing HUD toast surface),
        /// so the banner does not need to re-render its rows (dismiss > refresh: the
        /// simpler honest option — the toast states exactly what was repaired/spent).</summary>
        private bool _ctaFired;
        private void FireCta()
        {
            if (_ctaFired || _fired) return;
            _ctaFired = true;
            FlowTrace.Step("EndState", $"{_vm.Kind} banner CTA fired: action={_vm.CtaRoute}");
            var act = _vm.Cta;
            _vm.Cta = null;
            Guard.Try("EndState", "banner CTA action", () => act?.Invoke());
            FirePrimary();   // dismiss after the action; latched, fires exactly once
        }

        // =====================================================================
        //  WO-1543 - THE ANTI-SOFTLOCK GUARD LEARNS TO SEE THE PLAYER
        // =====================================================================
        //  Owner ruling 2026-09-06: "Hold on touch, longer guard."
        //
        //  THE DEFECT WAS NEVER "THERE IS A TIMER". RaidVictoryController calls
        //  AutoDismissSeconds "the anti-soft-lock guard" and it is correct - an end
        //  state that never dismisses can strand a player with no route home. The
        //  defect is that the timer could not tell a READING player from an ABSENT
        //  one: in 12 seconds the player had to read the star result, up to five
        //  spoils rows, a companion-join line and, at a capped bank, "Some of the
        //  reward could not be paid out" - and no tap, drag or scroll stopped it.
        //
        //  RESTART, NOT CANCEL, AND THE CHOICE IS DELIBERATE. A cancel means one
        //  stray tap pins the screen open forever, which re-opens the exact softlock
        //  the guard exists to prevent. Restart keeps the backstop alive while giving
        //  a reading player unlimited time - the safer reading of "hold on touch".
        //
        //  OPT-IN, so no other end state's timing moves silently: only a VM that sets
        //  EndStateVM.HoldOnInteraction takes this path, and today that is the two RAID
        //  templates alone (FromRaidVictory, FromRaidRetreat). FromBattleDefeat (2.5s),
        //  FromHeroDeath (6s), FromGameOver (0s), FromOutpostVictory (4s) and the
        //  wave-clear banner all keep the plain countdown below, byte for byte.
        //
        //  THE TEARDOWN PATHS ARE UNTOUCHED. OnSceneLoaded / CloseFromArbiter / OnDestroy
        //  still destroy this object without firing the primary, and still say so through
        //  AbandonedPrimaryWarn - a HELD screen whose world moves underneath it dies
        //  exactly as an unheld one does, because this coroutine dies with the GameObject.
        // =====================================================================

        /// <summary>How often the hold re-arm may speak, in seconds - a held screen is touched
        /// every frame while a finger rests on it, and an unthrottled line would flood the log
        /// and evict the boot window out of the device logcat ring (memory
        /// logcat-ring-buffer-destroys-evidence).</summary>
        private const float HoldTraceEverySeconds = 2f;

        /// <summary>
        /// The guard. When <see cref="EndStateVM.HoldOnInteraction"/> is false this is the
        /// original two-line countdown. When it is true the countdown RE-ARMS on every player
        /// interaction, and every re-arm is traced (throttled) so a capture can show whether the
        /// player was holding the screen or the guard simply fired.
        /// </summary>
        /// <summary>
        /// WO-1778 — how many guard windows a REFUSING primary gate gets before the guard stops
        /// asking and dismisses the screen anyway. Deliberately more than one: the raid capture
        /// gate's own bound (<c>RaidVictoryController.CaptureRefusalsBeforeForcedExit</c>) turns
        /// into a real route home on its second call, so on that path this last resort is never
        /// reached. It exists for every OTHER gated VM, present and future — a gate nobody bounded.
        /// </summary>
        private const int GateRefusalWindowsBeforeForcedDismiss = 3;

        /// <summary>
        /// The guard's outer loop (WO-1778). Each pass waits one full window and then asks
        /// <see cref="FirePrimary(bool)"/>; a refused gate re-arms the window instead of leaving the
        /// screen up forever, and the LAST pass forces the dismissal. A screen whose gate never
        /// refuses behaves exactly as before: one window, one fire, one teardown.
        /// </summary>
        private IEnumerator AutoDismissAfter(float seconds)
        {
            float window = Mathf.Max(0.5f, seconds);

            for (int attempt = 1; attempt <= GateRefusalWindowsBeforeForcedDismiss; attempt++)
            {
                yield return AwaitDismissWindow(window);
                if (_fired) yield break;

                bool last = attempt == GateRefusalWindowsBeforeForcedDismiss;
                FirePrimary(forceDismissIfGateRefuses: last);
                if (_fired) yield break;

                FlowTrace.Warn("EndState",
                    $"'{(_vm != null ? _vm.Title : "?")}' guard window {attempt}/" +
                    $"{GateRefusalWindowsBeforeForcedDismiss} expired but the primary GATE REFUSED — " +
                    "re-arming the window rather than leaving the screen with a no-op CTA (WO-1778). " +
                    "The last window dismisses the screen whatever the gate says.");
            }
        }

        /// <summary>
        /// One dismissal window. Extracted from <see cref="AutoDismissAfter"/> unchanged so the
        /// outer retry loop can re-arm it: a VM without <see cref="EndStateVM.HoldOnInteraction"/>
        /// still waits exactly one <c>WaitForSecondsRealtime(window)</c>, and a holding VM still
        /// re-arms on every interaction.
        /// </summary>
        private IEnumerator AwaitDismissWindow(float window)
        {
            if (_vm == null || !_vm.HoldOnInteraction)
            {
                yield return new WaitForSecondsRealtime(window);
                yield break;
            }

            FlowTrace.Step("EndState",
                $"'{(_vm.Title ?? "?")}' auto-dismiss armed at {window:0}s WITH HOLD-ON-TOUCH " +
                "(WO-1543): any interaction re-arms the full window; the guard still fires for a " +
                "player who does nothing.");

            float waited = 0f;
            int rearms = 0;
            while (waited < window)
            {
                yield return null;
                // UNSCALED throughout - a hold left at timeScale 0 by any other system must never
                // become a new way to strand the player (the same law the raid watchdog lives by).
                waited += Time.unscaledDeltaTime;

                if (!InteractedThisFrame()) continue;

                waited = 0f;
                rearms++;
                FlowTrace.Throttle("EndState", "endstate-hold-rearm", HoldTraceEverySeconds,
                    $"'{(_vm.Title ?? "?")}' HELD by interaction - auto-return re-armed to the full " +
                    $"{window:0}s (re-arms so far: {rearms}). The player who is reading is the player " +
                    "who is touching.");
            }

            FlowTrace.Step("EndState",
                $"'{(_vm.Title ?? "?")}' auto-dismiss FIRING after {window:0}s with no interaction " +
                $"(re-arms this session: {rearms}) - the anti-softlock guard doing its job.");
        }

        /// <summary>
        /// Did the player touch the screen THIS frame? New Input System only (no legacy
        /// <c>Input.*</c> - this project's rule), mouse OR touchscreen, and deliberately NOT
        /// raycast-filtered: a tap anywhere on a full-screen end state is the player reading it,
        /// and a tap that happens to land on the one primary button already fires the route home
        /// through the Button itself. Null-safe on a platform with neither device.
        /// </summary>
        private static bool InteractedThisFrame()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                  mouse.leftButton.isPressed ||
                                  mouse.scroll.ReadValue().sqrMagnitude > 0.01f))
                return true;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch != null &&
                touch.primaryTouch.press.isPressed)
                return true;

            return false;
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // The world moved on underneath us (e.g. raid-death evac loaded the hub):
            // tear down WITHOUT firing the primary route.
            //
            // Section 12 - this used to be SILENT, and that silence cost us. An end-state torn
            // down here takes its Primary action with it; when that action was an arena's only
            // route home, the player was stranded and NOTHING in the log said why.
            // Destroying without firing is still CORRECT (a displaced end-state must never
            // silently trigger continue/respawn) - it just must never be invisible.
            AbandonedPrimaryWarn("OnSceneLoaded (scene '" + s.name + "' loaded under the panel)");
            _fired = true;
            Destroy(gameObject);
        }

        /// <summary>
        /// Section 12 - no silent failures. Announces that this end-state is being destroyed WITHOUT
        /// running its Primary action, and says loudly when that action was load-bearing. Whoever
        /// owns a route that matters must not delegate it to a UI object other systems can destroy;
        /// BattleArena's stranding watchdog exists because of exactly this.
        /// </summary>
        private void AbandonedPrimaryWarn(string reason)
        {
            bool hadPrimary = _vm != null && _vm.Primary != null && !_fired;
            string title = _vm != null ? _vm.Title : "?";
            if (hadPrimary)
                FlowTrace.Warn("EndState",
                    $"'{title}' destroyed WITHOUT firing its primary action - {reason}. " +
                    "That action is now abandoned. If it was an arena home-return, the player is " +
                    "stranded until BattleArena's watchdog fires.");
            else
                FlowTrace.Step("EndState",
                    $"'{title}' torn down ({reason}) - no primary action pending, nothing abandoned.");

            // WO-969: warning about the abandonment was never enough - HAND THE TRANSITION BACK.
            SignalAbandon(reason);
        }

        /// <summary>
        /// WO-969 (owner F8 seq 2315) - THE PENDING-TRANSITION HAND-BACK.
        ///
        /// PROVEN BY CAPTURE: opening Pause over the arena victory summary runs
        /// PanelManager.NotifyOpened -> previous.Close() -> <see cref="CloseFromArbiter"/> ->
        /// Destroy, and the deferred home return that lived ONLY in <c>_vm.Primary</c> died with
        /// the GameObject. The 45s stranding watchdog then had to walk the player home.
        ///
        /// The screen keeps its correct behaviour (a displaced end-state NEVER silently fires the
        /// player's CHOICE), and gains the missing half: it TELLS the owner of the transition that
        /// the screen is gone. Fired at most once, and only while Primary never ran - so a normal
        /// Continue (which nulls Primary in <see cref="FirePrimary(bool)"/>) can never double-fire it.
        /// Called from every abandon path (<see cref="AbandonedPrimaryWarn"/>) AND from
        /// <see cref="OnDestroy"/>, which is the catch-all for any destroy path not yet written.
        /// </summary>
        private void SignalAbandon(string reason)
        {
            // The latch + the trace + the Guard all live on the VM (EndStateVM.HandBackPendingTransition)
            // ON PURPOSE: the whole point is that the transition survives THIS object, so its
            // exactly-once state must not be a field of the thing being destroyed. It also makes the
            // contract provable headlessly with no canvas and no edit-mode Destroy.
            _vm?.HandBackPendingTransition(reason);
        }

        /// <summary>HUD-2: the single-modal arbiter swapped us out (another modal opened over the
        /// end-state). Tear down WITHOUT firing the primary route — a displaced end-state must not
        /// silently trigger continue/respawn (mirrors <see cref="OnSceneLoaded"/>).</summary>
        private void CloseFromArbiter()
        {
            // Section 12: was SILENT. This fires whenever ANY other modal opens over the
            // end-state (PanelManager.NotifyOpened), so it is the widest of the three
            // abandon paths - and it left no trace at all.
            AbandonedPrimaryWarn("CloseFromArbiter (another modal opened over this end-state)");
            _fired = true;
            if (this != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // WO-969 CATCH-ALL: the three KNOWN abandon paths route through AbandonedPrimaryWarn,
            // but OnDestroy catches every path that exists and every path nobody has written yet
            // (a parent canvas torn down, an additive scene unload, a future modal). Latched, so
            // when the known path already handed back this is a no-op. This is what makes the fix
            // hold against the NEXT modal instead of only against Pause.
            SignalAbandon("OnDestroy (GameObject destroyed with a transition still pending)");

            SceneManager.sceneLoaded -= OnSceneLoaded;
            // HUD-2: release the arbiter slot (no-op for compact banners - handle is null - and a
            // no-op if we were already swapped out).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            _worldHold?.Dispose();
            _worldHold = null;
            if (_open == this)
            {
                // F8-15: close step-out for the death window — pairs with the ScreenOpened above so
                // the chain shows each end-state's full open->close lifecycle (which popup outlived which).
                if (DeathTrace.Active)
                    DeathTrace.ScreenClosed("EndState '" + (_vm != null ? _vm.Title : "?") + "'",
                        "EndStateView.OnDestroy" + (_fired ? " (primary fired)" : " (torn down without firing)"));
                _open = null;
                // P23 (A4.6): the decision node closed — the posture arc moves on.
                DeNelle.Core.HudModel.PostureSignals.SetEndState(false);
            }
        }

        // ── smooth-in tween (KIT-PROMOTION CANDIDATE) ─────────────────────────

        /// <summary>Register a GameObject for the staggered reveal (alpha 0 until its turn).</summary>
        private void Track(GameObject go, float delay, float fromScale)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            _reveals.Add(new Reveal
            {
                Group = cg,
                Rect = go.transform as RectTransform,
                Delay = delay,
                FromScale = fromScale,
            });
        }

        /// <summary>The whole panel's fade-in duration, seconds of UNSCALED time.</summary>
        private const float RootRevealSec = 0.25f;
        /// <summary>One tracked body element's fade-in duration, seconds of UNSCALED time.</summary>
        private const float BodyRevealSec = 0.20f;
        /// <summary>Grace added to the last element's (delay + duration) before the probe judges.
        /// Generous on purpose: the probe must never cry wolf on a slow first frame — a FALSE
        /// alarm here would burn exactly the trust §12 instrumentation exists to build.</summary>
        private const float RevealVerifyGraceSec = 1.0f;
        /// <summary>Alpha at or above which an element counts as VISIBLE to the player.</summary>
        private const float RevealVisibleAlpha = 0.95f;

        /// <summary>
        /// ⭐ DID THE PANEL ACTUALLY BECOME VISIBLE? (owner eyewitness, build 2026.09.06.357599:
        /// the WAVE 7 CLEARED panel showed a title over an EMPTY interior for its whole 5-8s life.)
        ///
        /// <para>⛔ WHY THIS PROBE EXISTS RATHER THAN A FIX. The reveal path READS CORRECT at
        /// source and every candidate was ruled out by reading, not by measuring: nothing calls
        /// StopAllCoroutines / StopCoroutine in this file; the host GameObject is never deactivated
        /// (the only SetActive(false) is the chrome Close chip at Show); every body builder does
        /// call <see cref="Track"/>; and the root group IS revealed on this entry point. The
        /// coroutines run on UNSCALED time, so <c>HoldWorld</c>'s timeScale 0 cannot stall them.
        /// So the code says it should work and the owner says it did not, and under §12 that means
        /// the honest next move is to CAPTURE the dead step, not to guess at it a fourth time.</para>
        ///
        /// <para>⛔ AND NO EXISTING GATE COULD EVER HAVE SEEN THIS. Both capture paths FORCE the
        /// alphas to 1 before they photograph anything — this file's own edit-mode branch in Bind,
        /// and UICaptureLaunch (`group.alpha = 1f`, UICaptureLaunch.cs:1416 / :3062, whose comment
        /// at :5161 says outright that "every CanvasGroup is still parked at its start-of-tween
        /// alpha 0"). A reveal that never completes is therefore INVISIBLE to the whole
        /// screenshot suite and visible only to the player. That is why this probe runs at
        /// RUNTIME, in the build, and reports through the F8 channel.</para>
        ///
        /// <para>It reports through <see cref="FlowTrace.Fail"/>, which the F8 break-log captures
        /// (Warn/Fail are recorded; Step is NOT — surveyed on the device log for this defect), so
        /// the next wave clear on her device either proves the reveal healthy or names exactly how
        /// many elements were still transparent and which one to look at.</para>
        /// </summary>
        private System.Collections.IEnumerator VerifyRevealCompleted(CanvasGroup rootGroup)
        {
            float longest = 0f;
            foreach (var r in _reveals)
                if (r.Delay > longest) longest = r.Delay;
            float deadline = longest + BodyRevealSec + RevealVerifyGraceSec;

            float t = 0f;
            while (t < deadline)
            {
                t += Time.unscaledDeltaTime;
                yield return null;   // unscaled + frame-driven: survives HoldWorld's timeScale 0
            }

            // The panel may legitimately have been dismissed or replaced by now — that is not a
            // defect, and a destroyed group must never be reported as a transparent one.
            if (this == null || rootGroup == null) yield break;

            int tracked = 0, invisible = 0, destroyed = 0;
            string firstOffender = null;
            foreach (var r in _reveals)
            {
                tracked++;
                if (r.Group == null) { destroyed++; continue; }
                if (r.Group.alpha < RevealVisibleAlpha)
                {
                    invisible++;
                    if (firstOffender == null)
                        firstOffender = r.Group.name + " (alpha " + r.Group.alpha.ToString("0.###") +
                                        ", delay " + r.Delay.ToString("0.##") + "s)";
                }
            }

            bool rootDark = rootGroup.alpha < RevealVisibleAlpha;
            // The player-facing verdict: a body with nothing visible in it IS the empty box.
            bool bodyDark = tracked > 0 && invisible >= tracked - destroyed && invisible > 0;

            if (rootDark || bodyDark)
                FlowTrace.Fail("EndState",
                    $"REVEAL DID NOT COMPLETE - this is the EMPTY BOX the owner saw. " +
                    $"root alpha={rootGroup.alpha:0.###} (needs >= {RevealVisibleAlpha:0.##}), " +
                    $"tracked={tracked} stillTransparent={invisible} destroyed={destroyed} " +
                    $"after {deadline:0.##}s of UNSCALED time (longest reveal delay {longest:0.##}s " +
                    $"+ {BodyRevealSec:0.##}s fade + {RevealVerifyGraceSec:0.##}s grace). " +
                    $"First still-transparent element: {firstOffender ?? "(none - the ROOT group is the dark one)"}. " +
                    "The elements were BUILT (this probe walked them), so this is NOT a layout or " +
                    "a text-fit fault - it is the fade never finishing. Look at RevealRoutine and " +
                    "at anything that could stop this object's coroutines mid-tween.");
            else
                FlowTrace.Step("EndState",
                    $"reveal completed: root alpha={rootGroup.alpha:0.###}, {tracked} tracked " +
                    $"element(s) all at/above {RevealVisibleAlpha:0.##} ({destroyed} already torn down) " +
                    $"after {deadline:0.##}s unscaled.");
        }

        /// <summary>Ease-out cubic fade+scale on UNSCALED time (plays through slow-mo /
        /// any pause). Mirrors the proven BattleArenaHud.PopCrown pattern, generalized.</summary>
        private static IEnumerator RevealRoutine(CanvasGroup cg, RectTransform rt,
                                                 float delay, float duration, float fromScale)
        {
            if (cg == null) yield break;
            if (rt != null && fromScale < 1f) rt.localScale = Vector3.one * fromScale;

            float t = 0f;
            while (t < delay)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / duration);
                float eased = 1f - Mathf.Pow(1f - u, 3f);   // ease-out cubic
                if (cg == null) yield break;                 // torn down mid-tween
                cg.alpha = eased;
                if (rt != null && fromScale < 1f)
                    rt.localScale = Vector3.one * Mathf.Lerp(fromScale, 1f, eased);
                yield return null;
            }
            if (cg != null) cg.alpha = 1f;
            if (rt != null) rt.localScale = Vector3.one;
        }

        // ── tiny helpers ──────────────────────────────────────────────────────

        /// <summary>Kit buttons need an EventSystem; builds don't always have one
        /// (the reason GameOverScreen hand-rolled hit-testing). Same proven pattern
        /// as BattleArenaHud.EnsureEventSystem.</summary>
        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            DontDestroyOnLoad(es);
        }

        /// <summary>A full-width zone at a FIXED pixel height, stacked from the TOP of the
        /// parent (F8-35: bands own their row height instead of splitting the well by
        /// fraction — a squeezed well can no longer overprint every row into ~13px).</summary>
        private static RectTransform MakeZonePx(RectTransform parent, string name,
                                                float topPx, float heightPx)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -(topPx + heightPx));
            rt.offsetMax = new Vector2(0f, -topPx);
            return rt;
        }

        private static RectTransform MakeZone(Transform parent, string name,
                                              float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
