// =============================================================================
// ObjectiveBannerUi — one-line current-objective strip (WO-T2, spec §2.2).
// -----------------------------------------------------------------------------
// ⚠ RETIRED FROM THE FTUE (WO-1012 P1, 2026-08-10): TutorialFlow no longer calls
// this class — the tutorial's objective surface is ObjectiveStripUi (thin
// bottom-center strip + progress beads; kills the top-edge F8 collision class
// for good), and the ONE skip is TutorialSkipUi (corner control + confirm).
// Kept compiling for any non-tutorial caller a future WO may add - and WO-1243
// added the first one: MaintenanceBannerDriver shows the operator maintenance
// notice through this surface. (The "ZERO callers" line that stood here until
// 2026-08-27 was true when written and is not any more.) That caller passes
// Show(..., wrap:true), which is OPT-IN and leaves tutorial behaviour untouched
// - see the WO-1245 block below. Do not wire new tutorial chrome through it.
// -----------------------------------------------------------------------------
// Top-centre, non-blocking, code-built uGUI in the kit language (obsidian glass
// plate + gold accent rule + parchment text — UiStyle/ElarionUi tokens). Replaces
// the UIToolkit TutorialHudOverlay banner as the tutorial's objective surface,
// but is REUSABLE: anything (quests, events) can Show a one-liner.
//
// Optional SKIP affordance for skippable tutorial steps: presentation raises the
// supplied onSkip intent and does nothing else (MVVM — the caller owns what skip
// means). Unscaled-time fade; never blocks gameplay input outside its own small
// Skip button.
//
// WO-1010 D16 (owner ruling 2026-08-08): ONE skip affordance, banner-integrated.
// The old floating corner "Skip Tutorial" button (anchored (1,1), y -92) is GONE —
// it collided with the build HUD's compact corner Done (D10) and doubled the skip.
// The banner's single control raises the per-step skip when the step supplies one
// ("Skip >"); when only the whole-FTUE skip is supplied it reads "Skip Tutorial"
// and routes through the kit confirm so it never fires on an accidental tap.
//
// SEPARATE from ElarionUiKit by design (do-not-touch this slice); kit-promotion
// candidate: ElarionUiKit.ObjectiveStrip(...) in WO-T5.
// =============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// Static one-line objective banner. <see cref="Show"/> sets/replaces the
    /// current objective; <see cref="Hide"/> eases it out.
    /// </summary>
    public sealed class ObjectiveBannerUi : MonoBehaviour
    {
        private const float FadeSeconds = 0.2f;
        private const int CanvasSortOrder = 4300;    // just above the spotlight dim
        private const float BannerWidth = 620f;
        private const float BannerHeight = 46f;

        // -- WO-1245: OPT-IN WRAPPING, and the plate MEASURES itself ----------
        // This banner was built NoWrap+Ellipsis for tutorial objectives ("Build a
        // tower"), which is right for those and wrong for an operator maintenance
        // message: the photographed banner read
        //     "MAINTENANCE ON RAIDS - Raids are closed whil..."
        // so the player got the headline and none of the WHY the message exists to
        // give. Wrapping is therefore OPT-IN AT THE CALL SITE (Show(..., wrap:true))
        // and every existing caller keeps byte-identical behaviour.
        //
        // The plate is a fixed-height single-line surface, so wrapping needs it to
        // GROW. It is MEASURED, never eyeballed: TMP's own GetPreferredValues is
        // asked how tall the real string is in the real font at the real width, and
        // the plate is set to that plus the plate's own measured breathing room.
        // A hardcoded "make it 92 tall" would be a guess that a longer message or a
        // different font silently breaks.

        /// <summary>The text box spans these fractions of the plate width (set in
        /// <see cref="Build"/>); a wrap must be measured against that, not the plate.</summary>
        private const float TextAnchorMinX = 0.06f;
        private const float TextAnchorMaxX = 0.80f;

        /// <summary>Ceiling on a wrapped plate, in measured LINES - not in pixels, so it
        /// stays correct if the font or size changes. Beyond this the plate stops growing
        /// and TMP ellipsizes, because a banner tall enough to be a paragraph has stopped
        /// being a banner and is covering the game.</summary>
        private const int MaxWrapLines = 3;

        private static ObjectiveBannerUi _instance;

        private CanvasGroup _group;
        private TextMeshProUGUI _label;
        // WO-1010 D16: THE ONE SKIP. A single banner-integrated control; its label and
        // action follow what the caller supplied — per-step skip ("Skip >") wins when
        // present, else the whole-FTUE skip ("Skip Tutorial", kit-confirmed). Since every
        // FTUE step today is per-step skippable, skip-all remains reachable by simply
        // walking the steps; the control degrades to the confirmed skip-all the moment a
        // non-skippable step ships. No second control exists anywhere on screen.
        private Button _skipBtn;
        private GameObject _skipHost;
        private TextMeshProUGUI _skipLabel;
        private Action _onSkip;
        private Action _onSkipAll;
        private bool _visible;
        private float _fadeT;
        private RectTransform _plate;   // WO-1245: a wrapped plate is resized, so it is held
        private bool _wrap;             // WO-1245: opt-in per Show(); false = tutorial behaviour

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Show (or update) the objective line. <paramref name="count"/> &gt; 0
        /// appends " (0/count)"-style progress via <see cref="SetProgress"/>.
        /// <paramref name="onSkip"/> non-null ⇒ the Skip affordance shows and raises it.
        /// <para>
        /// <paramref name="wrap"/> (WO-1245) is OPT-IN and defaults to FALSE, which is the
        /// original single-line NoWrap+Ellipsis behaviour every tutorial caller relies on.
        /// Pass true only for a line whose whole sentence matters (the operator maintenance
        /// notice): the label wraps and the plate GROWS to a MEASURED height, up to
        /// <see cref="MaxWrapLines"/> lines.
        /// </para></summary>
        public static void Show(string text, int count = 0, Action onSkip = null, Action onSkipAll = null,
                                bool wrap = false)
        {
            var b = Ensure();
            b._visible = true;
            b._wrap = wrap;
            b._onSkip = onSkip;
            b._onSkipAll = onSkipAll;
            b._baseText = text ?? "";
            b._count = Mathf.Max(0, count);
            b._done = 0;
            bool hasSkip = onSkip != null || onSkipAll != null;
            // D16: one control — per-step skip when the step supplies it, else skip-all.
            if (b._skipHost != null) b._skipHost.SetActive(hasSkip);
            if (b._skipLabel != null)
            {
                // TWO keys, not one with a glyph appended: the per-step face carries a direction
                // chevron (which an RTL locale flips) and the skip-all face is a whole phrase.
                string skipFace = onSkip != null
                    ? new LocalizedText("tutorial.skip.step").Resolve()
                    : new LocalizedText("tutorial.skip.action").Resolve();
                b._skipLabel.text = skipFace;
            }
            // Maintenance has no action, so its message may use the full authored plate.
            // Reserve the narrower lane only while the integrated Skip control is live.
            if (b._label != null)
                b._label.rectTransform.anchorMax = new Vector2(hasSkip ? TextAnchorMaxX : 0.94f, 0.88f);
            b.RefreshLabel();
        }

        /// <summary>Update progress on a counted objective (e.g. 1 of 1 towers).</summary>
        public static void SetProgress(int done)
        {
            if (_instance == null) return;
            _instance._done = Mathf.Max(0, done);
            _instance.RefreshLabel();
        }

        /// <summary>Ease the banner out. Safe when not shown.</summary>
        public static void Hide()
        {
            if (_instance == null) return;
            _instance._visible = false;
            _instance._onSkip = null;
            _instance._onSkipAll = null;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private string _baseText = "";
        private int _count;
        private int _done;

        private void RefreshLabel()
        {
            if (_label == null) return;
            _label.text = _count > 0 ? $"{_baseText}  <color=#C9A54A>({_done}/{_count})</color>" : _baseText;
            ApplyWrapAndFit();
        }

        /// <summary>
        /// WO-1245. Apply the current call's wrap choice and, when wrapping, MEASURE the
        /// plate height the real string actually needs.
        /// <para>
        /// wrap == false restores the exact built state (NoWrap + the fixed
        /// <see cref="BannerHeight"/>), so a maintenance line followed by a tutorial line
        /// leaves the tutorial's surface byte-identical to what it has always been.
        /// </para>
        /// <para>
        /// Guarded: TMP text metrics need a resolved font, and this runs in edit mode
        /// under the UI capture as well as at runtime. If the measurement cannot be taken
        /// we SAY SO and keep the fixed height rather than resizing on a guess - a silent
        /// catch here would turn a layout bug into a mystery (CLAUDE.md section 12).
        /// </para>
        /// </summary>
        private void ApplyWrapAndFit()
        {
            if (_label == null) return;

            _label.textWrappingMode = _wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            _label.alignment = _wrap ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            _label.fontSize = _wrap ? 15f : 20f;

            if (_plate == null) return;

            if (!_wrap)
            {
                if (!Mathf.Approximately(_plate.sizeDelta.y, BannerHeight))
                    _plate.sizeDelta = new Vector2(BannerWidth, BannerHeight);
                return;
            }

            var labelRt = _label.rectTransform;
            float textWidth = BannerWidth * (labelRt.anchorMax.x - labelRt.anchorMin.x);
            try
            {
                // The plate's built-in breathing room, DERIVED rather than assumed: the
                // difference between the fixed plate and one measured line of this font.
                float oneLine = _label.GetPreferredValues("Ay", textWidth, 0f).y;
                if (oneLine <= 0f)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("UI",
                        "ObjectiveBanner wrap: TMP measured a single line as " + oneLine +
                        " - no usable font metrics, keeping the fixed " + BannerHeight + "px plate.");
                    return;
                }
                // The authored gold frame consumes real vertical ink at both edges. Preserve
                // a minimum inner gutter even when TMP's one-line metrics nearly fill the old
                // raw 46px rectangle; otherwise the second wrapped line crosses the frame.
                float padding = Mathf.Max(36f, BannerHeight - oneLine);
                float needed = _label.GetPreferredValues(_label.text, textWidth, 0f).y;
                float height = Mathf.Clamp(needed + padding, BannerHeight, oneLine * MaxWrapLines + padding);

                if (!Mathf.Approximately(_plate.sizeDelta.y, height))
                {
                    _plate.sizeDelta = new Vector2(BannerWidth, height);
                    DeNelle.Core.Diagnostics.FlowTrace.Step("UI",
                        "ObjectiveBanner wrapped plate MEASURED: textWidth=" + textWidth.ToString("0.#") +
                        " oneLine=" + oneLine.ToString("0.#") + " needed=" + needed.ToString("0.#") +
                        " padding=" + padding.ToString("0.#") + " -> height=" + height.ToString("0.#") +
                        " (cap " + MaxWrapLines + " lines).");
                }
            }
            catch (Exception e)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("UI",
                    "ObjectiveBanner wrap measurement threw (" + e.GetType().Name + ": " + e.Message +
                    ") - keeping the fixed " + BannerHeight + "px plate rather than resizing on a guess.");
            }
        }

        private static ObjectiveBannerUi Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("ObjectiveBanner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ObjectiveBannerUi>();
            _instance.Build();
            return _instance;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortOrder;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            gameObject.AddComponent<GraphicRaycaster>();   // only the Skip button raycasts
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // Plate — obsidian glass strip, top-centre, non-blocking.
            var plate = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(transform, false);
            var prt = (RectTransform)plate.transform;
            // Owner 2026-07-16: the step-instruction strip sat ON TOP of the compass — both
            // parked top-centre. The HUD-kit compass lives in the Status "crown" area, which
            // HudAreasHost anchors at screen-fraction y 0.845..0.990 (top ~15%). Anchor this
            // banner to that crown's exact LOWER edge (0.845) so it hangs just BELOW the compass
            // in both orientations (a screen fraction is the same physical y on this
            // ConstantPixelSize canvas as on the kit's ScaleWithScreenSize canvas).
            prt.anchorMin = new Vector2(0.5f, 0.845f);
            prt.anchorMax = new Vector2(0.5f, 0.845f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -6f);
            prt.sizeDelta = new Vector2(BannerWidth, BannerHeight);
            _plate = prt;   // WO-1245: held so a wrapped line can grow it to a measured height
            var pimg = plate.GetComponent<Image>();
            // Use the authored black-iron/gold action plate. The retired presentation was a
            // raw black rectangle with a detached gold line, visibly outside the reskin.
            var plateSprite = Resources.Load<Sprite>("UI/ElarionMedieval/buttons/button-normal-empty");
            if (plateSprite != null)
            {
                pimg.sprite = plateSprite;
                pimg.type = Image.Type.Simple;
                pimg.color = Color.white;
            }
            else
            {
                pimg.color = new Color(ElarionUiKit.ObsidianFill.r, ElarionUiKit.ObsidianFill.g,
                                       ElarionUiKit.ObsidianFill.b, 0.96f);
            }
            pimg.raycastTarget = false;

            // Gold accent rule along the bottom edge (kit chrome vocabulary).
            var rule = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            rule.transform.SetParent(prt, false);
            var rrt = (RectTransform)rule.transform;
            rrt.anchorMin = new Vector2(0f, 0f);
            rrt.anchorMax = new Vector2(1f, 0f);
            rrt.pivot = new Vector2(0.5f, 0f);
            rrt.sizeDelta = new Vector2(0f, 2f);
            var rimg = rule.GetComponent<Image>();
            // The authored plate owns its edge. Keep the procedural rule only as fallback.
            rimg.color = plateSprite == null
                ? new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.85f)
                : new Color(0f, 0f, 0f, 0f);
            rimg.raycastTarget = false;

            // Objective text.
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(prt, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = new Vector2(TextAnchorMinX, 0.12f);
            // 0.80, not the old 0.86: the D16 skip control's visible face now reaches
            // ~0.83 of the plate, and an ellipsized objective must not run under it.
            trt.anchorMax = new Vector2(TextAnchorMaxX, 0.88f);
            // TMP's visual baseline sits low inside its measured line box; lift the complete
            // text lane slightly so two-line notices have equal optical air above and below.
            trt.offsetMin = new Vector2(0f, 6f);
            trt.offsetMax = new Vector2(0f, 6f);
            _label = textGo.AddComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(_label);
            _label.fontSize = 20f;
            _label.color = ElarionUi.Parchment;
            _label.alignment = TextAlignmentOptions.MidlineLeft;
            // The BUILT default stays the tutorial's: NoWrap + Ellipsis. Show(wrap:true)
            // flips wrapping on for that one line only (WO-1245). Ellipsis is kept even
            // when wrapping - it is the graceful end of the MaxWrapLines clamp, so a
            // pathological message truncates instead of swallowing the screen.
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.overflowMode = TextOverflowModes.Ellipsis;
            _label.raycastTarget = false;

            // ── THE ONE SKIP (WO-1010 D16) — banner-integrated, right edge. ──────
            // The old floating corner "Skip Tutorial" that used to be built here is GONE:
            // it doubled the skip affordance and its (1,1)/-92 box collided with the build
            // HUD's compact corner Done (D10). This single control carries BOTH intents
            // (per-step when supplied, else the confirmed skip-all — see HandleSkipTap).
            //
            // MinTouch via an INVISIBLE HIT PAD (the playbook's padding-never-growth rule):
            // the banner strip is only 46px tall, so the visible face stays small and the
            // transparent parent Image carries the full touch floor. The pad overhangs the
            // strip vertically; it is invisible, and it raycasts only while a skip intent
            // is live (the CanvasGroup drops raycasts otherwise), so the overhang can never
            // eat a tap when no skip is on offer.
            _skipHost = new GameObject("Skip", typeof(RectTransform), typeof(Image), typeof(Button));
            _skipHost.transform.SetParent(prt, false);
            var srt = (RectTransform)_skipHost.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(ElarionUiKit.MinTouchPx, ElarionUiKit.MinTouchPx);
            var simg = _skipHost.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0f);   // invisible padding, still raycastable
            _skipBtn = _skipHost.GetComponent<Button>();
            _skipBtn.targetGraphic = simg;
            _skipBtn.onClick.AddListener(HandleSkipTap);

            // The small visible face, centred inside the pad (quiet gold wash, kit hue).
            var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(srt, false);
            var facert = (RectTransform)face.transform;
            facert.anchorMin = facert.anchorMax = new Vector2(0.5f, 0.5f);
            facert.pivot = new Vector2(0.5f, 0.5f);
            facert.sizeDelta = new Vector2(104f, 32f);
            var faceImg = face.GetComponent<Image>();
            faceImg.color = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.16f);
            faceImg.raycastTarget = false;

            var skipTextGo = new GameObject("Label", typeof(RectTransform));
            skipTextGo.transform.SetParent(facert, false);
            var strt = (RectTransform)skipTextGo.transform;
            strt.anchorMin = Vector2.zero;
            strt.anchorMax = Vector2.one;
            strt.offsetMin = Vector2.zero;
            strt.offsetMax = Vector2.zero;
            _skipLabel = skipTextGo.AddComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(_skipLabel);
            _skipLabel.fontSize = 15f;
            _skipLabel.color = ElarionUi.ParchmentDim;
            _skipLabel.alignment = TextAlignmentOptions.Center;
            _skipLabel.text = new LocalizedText("tutorial.skip.step").Resolve();   // Show() retitles per intent
            _skipLabel.raycastTarget = false;
            // "Skip Tutorial" must fit the same small face without escaping it.
            _skipLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _skipLabel.enableAutoSizing = true;
            _skipLabel.fontSizeMin = 11f;
            _skipLabel.fontSizeMax = 15f;

            _skipHost.SetActive(false);

            DeNelle.Core.Diagnostics.FlowTrace.Step("UI",
                "D16: ONE skip affordance built into the banner ('Skip >' per-step / " +
                "'Skip Tutorial' confirmed skip-all fallback); the floating corner Skip Tutorial is retired");
        }

        /// <summary>The one skip control's tap (D16): the per-step skip wins when the step
        /// supplied one (instant, as before); otherwise the whole-FTUE skip runs through
        /// the kit confirm. Exactly one of the two ever backs the visible control, and the
        /// label (set in <see cref="Show"/>) always names which.</summary>
        private void HandleSkipTap()
        {
            if (_onSkip != null) { _onSkip.Invoke(); return; }
            RequestSkipAll();
        }

        /// <summary>The whole-FTUE skip (reached through the ONE banner control, D16):
        /// presentation raises a lightweight confirm (kit ConfirmModal) and only invokes
        /// the caller's onSkipAll intent on confirm — MVVM: the banner owns the confirm
        /// chrome, the caller owns what skip means (TutorialFlow.SkipAll). Never fires on
        /// an accidental single tap.</summary>
        private void RequestSkipAll()
        {
            var skip = _onSkipAll;
            if (skip == null) return;

            ElarionUiKit.ConfirmModal modal = null;
            modal = ElarionUiKit.BuildConfirmModal(
                "SkipTutorialConfirm",
                new LocalizedText("tutorial.skip.action").Resolve(),
                new LocalizedText("tutorial.skip.confirm_body_all").Resolve(),
                new LocalizedText("common.skip").Resolve(),
                new LocalizedText("tutorial.skip.keep_playing").Resolve(),
                onConfirm: () => { if (modal != null && modal.canvas != null) Destroy(modal.canvas); skip(); },
                onCancel:  () => { if (modal != null && modal.canvas != null) Destroy(modal.canvas); });
        }

        // WO-795 (16-panel audit): while ANY arbiter-tracked modal owns the screen
        // (Store, Cosmetic, Jukebox, Rumor Board, Hot-Swap, Bug Report ...) the coach
        // banner SUPPRESSES (fades out + drops raycasts) so it never crosses a modal's
        // header, and RESTORES when the modal closes. Caller state (_visible, _onSkip,
        // progress) is untouched -- the banner picks back up exactly where it was.
        private bool _modalSuppressed;

        private void Update()
        {
            bool modal = PanelManager.AnyOpen;
            if (modal != _modalSuppressed)
            {
                _modalSuppressed = modal;
                // Trace only when the change is player-visible (a shown banner); a modal
                // toggling while no objective is up would be per-open log noise.
                if (_visible)
                    DeNelle.Core.Diagnostics.FlowTrace.Step("UI", modal
                        ? "ObjectiveBanner suppressed - modal open ('" + (PanelManager.OpenPanelName ?? "?") + "')"
                        : "ObjectiveBanner restored - modal closed");
            }
            bool shown = _visible && !_modalSuppressed;
            float dir = shown ? 1f : -1f;
            _fadeT = Mathf.Clamp01(_fadeT + dir * (Time.unscaledDeltaTime / FadeSeconds));
            float eased = _fadeT * _fadeT * (3f - 2f * _fadeT);
            _group.alpha = eased;
            // Raycast only while the ONE skip control (D16) has a live intent behind it.
            _group.blocksRaycasts = shown && (_onSkip != null || _onSkipAll != null);
            _group.interactable = _group.blocksRaycasts;
        }
    }
}
