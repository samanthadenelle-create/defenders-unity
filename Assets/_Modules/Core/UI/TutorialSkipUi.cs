// =============================================================================
// TutorialSkipUi — WO-1012 §2b kit piece 5: THE ONE SKIP.
// -----------------------------------------------------------------------------
// A single SMALL CORNER control + ONE confirm sheet. Retires BOTH previous skip
// affordances: the big floating "Skip Tutorial" button AND the objective
// banner's inline "Skip >" (the banner itself is retired by ObjectiveStripUi).
// The confirm promises what the checkpoint system already guarantees: progress
// is saved (SeenTutorials persists per step, so a declined skip resumes from
// the same beat on the next launch).
//
// Placement (WO-1033, owner 2026-08-16 "move to top middle"): TOP-CENTRE,
// horizontally centred, hung from the Status crown's LOWER edge (HudAreasHost
// Status = x 0.340-0.660, y 0.845-0.990) so it clears the compass/waveBlock
// that own the very top band in calm(town)/calm(explore) — the same rule that
// fixed the 2026-07-16 "instruction strip sits ON the compass" complaint.
// It is DISJOINT from everything that broke the old right-edge anchor: the
// build mode confirm/rotate/cancel ActionRail (x >= 0.780) and the right-rail
// Echoes/Builders/Resources chips (also x >= 0.780). It sits ABOVE the
// DialogueView safe-area ceiling (y 0.660) at every supported aspect, and the
// TargetInfo band it hangs into carries NO widget row in calm(town), build or
// calm(explore) (hud-areas.json). SAFETY: top-centre is the furthest reachable
// point from the build-mode Confirm glyph — an accidental skip is unrecoverable
// -feeling, so distance from Confirm is a requirement, not a preference.
//
// Chrome: the COMMON kit button — ElarionUiKit.BuildObsidianButton(Style1,
// Gray), the quiet grey face (never the gold/primary face; Skip is an escape
// hatch). Nothing here is hand-rolled: a fixed-PIXEL mount band + the kit
// button stretched into it, the same idiom as HudKitController.BuildRailChip.
// The kit supplies the frame, the 3-state feedback and the MinTouchPx floor.
// The confirm sheet is the kit's shared ConfirmModal, with a post-layout
// touch-floor pass on its two buttons. MVVM: this control owns the chrome +
// confirm; the caller (TutorialFlow.SkipAll) owns what skip MEANS.
// [Flow:Tutorial] throughout.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// Static single-skip TOP-MIDDLE control for the FTUE. <see cref="Show"/> arms it
    /// with the caller's confirmed-skip intent; <see cref="Hide"/> removes it
    /// (flow finished/torn down). One confirm sheet, never an instant skip.
    /// </summary>
    public sealed class TutorialSkipUi : MonoBehaviour
    {
        private const float FadeSeconds = 0.2f;
        // DialogueView is 4800 and owns a full-screen raycast surface. Skip used to
        // remain visible below it but never received the pointer.
        private const int CanvasSortOrder = 6000;

        // ── TOP-MIDDLE anchor (WO-1033) ───────────────────────────────────────
        // ANCHORS, never corner offsets: the mount's anchor is the screen FRACTION
        // (0.5, 0.845) with a (0.5, 1) pivot, so the button stays horizontally
        // centred and the same distance under the Status crown at 2670x1200, at
        // 2340x1080 and at every aspect between. Only the mount's SIZE is fixed
        // pixels (WO-841: a fraction band can resolve under MinTouchPx, and the
        // touch-floor guard then grows it about its centre into its neighbours).
        /// <summary>Screen-fraction Y the mount hangs from — the HudAreasHost Status
        /// crown's LOWER edge, so the compass/waveBlock band above stays clear.</summary>
        private const float StatusCrownBottomFraction = 0.845f;
        /// <summary>Gap in reference px between the crown edge and the button's top.</summary>
        private const float CrownGapPx = 10f;
        /// <summary>Button width in reference px (fits "Skip Tutorial" above the kit's legibility floor).</summary>
        private const float SkipWidthPx = 300f;

        private static TutorialSkipUi _instance;

        private CanvasGroup _group;
        private RectTransform _mount;
        private Button _button;
        private Action _onConfirmedSkipAll;
        private ElarionUiKit.ConfirmModal _confirm;   // live confirm sheet (null when closed)
        private bool _confirmTouchFloorApplied;
        private bool _visible;
        /// <summary>Owner 2026-08-29: hide the Skip words while Build is open so they
        /// do not sit on the category / place chrome; restore when Build exits. Does NOT
        /// disarm the tutorial skip intent — only the chrome.</summary>
        private bool _suppressed;
        private float _fadeT;
        private bool _topHitProved;
        private int _topHitProbeFrames;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Show the top-middle Skip, armed with the caller's confirmed skip-all
        /// intent (invoked ONLY after the confirm sheet's Skip).</summary>
        public static void Show(Action onConfirmedSkipAll)
        {
            var s = Ensure();
            s._onConfirmedSkipAll = onConfirmedSkipAll;
            s._topHitProved = false;
            s._topHitProbeFrames = 0;
            if (!s._visible)
            {
                s._visible = true;
                Diagnostics.FlowTrace.Step("Tutorial", "SkipControl SHOW (the ONE skip, top-middle, WO-1033)");
            }
        }

        /// <summary>Remove the skip control (and any open confirm). Safe when hidden.</summary>
        public static void Hide()
        {
            if (_instance == null || !_instance._visible) return;
            _instance._visible = false;
            _instance._suppressed = false;
            _instance._onConfirmedSkipAll = null;
            _instance.CloseConfirm();
            Diagnostics.FlowTrace.Step("Tutorial", "SkipControl HIDE");
        }

        /// <summary>
        /// Temporarily hide the Skip chrome without clearing the armed skip-all callback.
        /// Build Mode calls this on Enter/Exit so "Skip Tutorial" does not overlap the
        /// build category / place UI, then restores when Build closes.
        /// </summary>
        public static void SetSuppressed(bool suppressed)
        {
            if (_instance == null) return;
            if (_instance._suppressed == suppressed) return;
            _instance._suppressed = suppressed;
            if (suppressed) _instance.CloseConfirm();
            Diagnostics.FlowTrace.Step("Tutorial",
                suppressed
                    ? "SkipControl SUPPRESSED (build screen active)"
                    : "SkipControl RESTORED (build screen closed)");
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static TutorialSkipUi Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("TutorialSkip");
            // Runtime persistence only. Edit-mode screenshot/evidence builders create the
            // exact same view but Unity forbids DontDestroyOnLoad outside play mode.
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _instance = go.AddComponent<TutorialSkipUi>();
            _instance.Build();
            return _instance;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortOrder;
            canvas.overrideSorting = true;
            // Same scaler contract as HudAreasHost, so "reference px" means the same
            // thing here as it does for every other kit widget (MinTouchPx included).
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();   // only the kit button raycasts
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // Fixed-PIXEL mount band, anchored TOP-CENTRE by fraction (see header).
            var mountGo = new GameObject("SkipMount", typeof(RectTransform));
            mountGo.transform.SetParent(transform, false);
            _mount = (RectTransform)mountGo.transform;
            _mount.anchorMin = _mount.anchorMax = new Vector2(0.5f, StatusCrownBottomFraction);
            _mount.pivot = new Vector2(0.5f, 1f);
            _mount.sizeDelta = new Vector2(SkipWidthPx, ElarionUiKit.MinTouchPx);
            _mount.anchoredPosition = new Vector2(0f, -CrownGapPx);

            // THE common Obsidian button — quiet grey face, never the gold/primary face
            // (WO-1033 §2: a loud Skip invites accidental tutorial loss). Emphasis is
            // carried by the frame + the position, never by hue (colourblind law).
            _button = ElarionUiKit.BuildObsidianButton(_mount,
                new LocalizedText("tutorial.skip.action").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                Vector2.zero, Vector2.one, OnSkipTapped);
            if (_button == null)
            {
                Diagnostics.FlowTrace.Fail("Tutorial",
                    "SkipControl BUILD FAILED - ElarionUiKit.BuildObsidianButton returned no button; " +
                    "the FTUE has no skip affordance this session");
                return;
            }
            // The legacy gray Blink face disappears against the world and reads as raw
            // floating text. Preserve its quiet semantics but use the coherent black-iron /
            // antique-gold empty state art, with runtime text layered separately.
            var medievalFace = Resources.Load<Sprite>("UI/ElarionMedieval/buttons/button-normal-empty");
            if (medievalFace != null)
            {
                // Imported Blink button prefabs may wrap the actual Button in additional
                // silver decorative Images. Suppress all inherited shells and install one
                // deterministic sibling face owned by this control.
                var inherited = _mount.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < inherited.Length; i++)
                    if (inherited[i] != null) inherited[i].enabled = false;
                var faceGo = new GameObject("SkipMedievalFace", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                // The hit Graphic MUST live below the Button in the transform tree.
                // EventSystem dispatch starts at the raycast Graphic and walks upward;
                // the old sibling face looked enabled but could never reach OnSkipTapped.
                faceGo.transform.SetParent(_button.transform, false);
                faceGo.transform.SetAsFirstSibling();
                var faceRt = (RectTransform)faceGo.transform;
                faceRt.anchorMin = Vector2.zero;
                faceRt.anchorMax = Vector2.one;
                faceRt.offsetMin = faceRt.offsetMax = Vector2.zero;
                var face = faceGo.GetComponent<Image>();
                face.sprite = medievalFace;
                face.type = Image.Type.Simple;
                face.color = Color.white;
                // This is the only enabled Graphic left under the button. It must participate in
                // GraphicRaycaster hit testing or the visible Skip control cannot be tapped.
                face.raycastTarget = true;
                _button.targetGraphic = face;
                if (!face.transform.IsChildOf(_button.transform))
                    Diagnostics.FlowTrace.Fail("Tutorial",
                        "SkipControl hit face is not under its Button - pointer dispatch is broken.");
                _button.transition = Selectable.Transition.ColorTint;
                _button.colors = new ColorBlock
                {
                    normalColor = Color.white,
                    highlightedColor = new Color(1f, .94f, .78f, 1f),
                    pressedColor = new Color(.82f, .67f, .40f, 1f),
                    selectedColor = Color.white,
                    disabledColor = new Color(.55f, .55f, .55f, .75f),
                    colorMultiplier = 1f,
                    fadeDuration = .08f
                };
            }
            var label = _button.GetComponentInChildren<TMPro.TMP_Text>();
            if (label != null)
            {
                label.color = ElarionUi.Parchment;
                label.fontStyle |= TMPro.FontStyles.Bold;
                ElarionUiKit.FitSingleLine(label, ElarionUiKit.FontFloor, ElarionUi.FontLabel);
            }
            _button.gameObject.name = "SkipTutorialButton";
            Diagnostics.FlowTrace.Step("Tutorial",
                "SkipControl BUILT (WO-1033) kit=BuildObsidianButton(Style1,Gray) anchor=top-middle " +
                "fracXY=(0.500," + StatusCrownBottomFraction.ToString("0.000") + ") size=" +
                SkipWidthPx.ToString("0") + "x" + ElarionUiKit.MinTouchPx.ToString("0") + "px");
        }

        /// <summary>The top-middle control's tap: raise the ONE confirm sheet. Never skips
        /// on the bare tap; a second tap while the sheet is open is a no-op.</summary>
        private void OnSkipTapped()
        {
            if (_onConfirmedSkipAll == null) return;
            if (_confirm != null && _confirm.canvas != null) return;   // sheet already up
            Diagnostics.FlowTrace.Step("Tutorial",
                "SkipControl TAPPED (top-middle kit button) - raising the confirm sheet");
            _confirmTouchFloorApplied = false;
            _confirm = ElarionUiKit.BuildConfirmModal(
                "SkipTutorialConfirm",
                new LocalizedText("tutorial.skip.action").Resolve(),
                new LocalizedText("tutorial.skip.confirm_body_progress").Resolve(),
                new LocalizedText("common.skip").Resolve(),
                new LocalizedText("tutorial.skip.keep_playing").Resolve(),
                onConfirm: () =>
                {
                    Diagnostics.FlowTrace.Step("Tutorial", "SkipControl CONFIRMED - invoking skip-all");
                    var act = _onConfirmedSkipAll;
                    CloseConfirm();
                    act?.Invoke();
                },
                onCancel: () =>
                {
                    Diagnostics.FlowTrace.Step("Tutorial", "SkipControl declined - resuming the walkthrough");
                    CloseConfirm();
                });
        }

        private void CloseConfirm()
        {
            if (_confirm != null && _confirm.canvas != null) Destroy(_confirm.canvas);
            _confirm = null;
        }

        private void Update()
        {
            bool chromeOn = _visible && !_suppressed;
            float dir = chromeOn ? 1f : -1f;
            _fadeT = Mathf.Clamp01(_fadeT + dir * (Time.unscaledDeltaTime / FadeSeconds));
            _group.alpha = _fadeT * _fadeT * (3f - 2f * _fadeT);
            _group.blocksRaycasts = chromeOn && _onConfirmedSkipAll != null;
            _group.interactable = _group.blocksRaycasts;

            // This is a click-path proof, not a hover-state assumption. Once layout and
            // the fade have settled, ask the live EventSystem what is actually topmost at
            // the centre of the visible Skip button. A result outside the Button subtree
            // names the exact overlay that is stealing the tap.
            if (!_topHitProved && _confirm == null && _group.blocksRaycasts && _group.alpha > .95f)
                VerifyTopPointerHit();

            // MinTouchPx on the CONFIRM sheet's buttons: the kit modal lays its buttons
            // out as panel fractions, so measure one frame after open (rects are valid
            // post-layout) and pad any short button up to the touch floor via sizeDelta
            // (padding, not growth — anchors untouched).
            if (_confirm != null && _confirm.canvas != null && !_confirmTouchFloorApplied)
            {
                bool measured = EnsureTouchFloor(_confirm.confirm) & EnsureTouchFloor(_confirm.cancel);
                if (measured) _confirmTouchFloorApplied = true;
            }
        }

        private void VerifyTopPointerHit()
        {
            if (_button == null || EventSystem.current == null) return;
            if (++_topHitProbeFrames < 3) return; // allow canvas/layout rebuilds to settle

            Canvas.ForceUpdateCanvases();
            var rt = _button.transform as RectTransform;
            if (rt == null || rt.rect.width < 1f || rt.rect.height < 1f) return;

            Vector3 worldCentre = rt.TransformPoint(rt.rect.center);
            Vector2 screenCentre = RectTransformUtility.WorldToScreenPoint(null, worldCentre);
            var pointer = new PointerEventData(EventSystem.current) { position = screenCentre };
            var hits = new List<RaycastResult>(8);
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0) return;

            var top = hits[0].gameObject;
            bool reachesButton = top == _button.gameObject || top.transform.IsChildOf(_button.transform);
            if (reachesButton)
            {
                _topHitProved = true;
                Diagnostics.FlowTrace.Step("Tutorial",
                    "SKIP_TOP_HIT_OK top=" + top.name +
                    " is inside SkipTutorialButton; no panel blocks the click target.");
                return;
            }

            // Report once after several stable frames so a transient canvas rebuild does
            // not create noise, while retaining the blocker name for field diagnostics.
            if (_topHitProbeFrames >= 8)
            {
                _topHitProved = true;
                Diagnostics.FlowTrace.Fail("Tutorial",
                    "SKIP_TOP_HIT_BLOCKED top=" + top.name +
                    " path=" + GetHierarchyPath(top.transform));
            }
        }

        private static string GetHierarchyPath(Transform leaf)
        {
            if (leaf == null) return "<null>";
            string path = leaf.name;
            for (var p = leaf.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        /// <summary>Pad a button's rect up to the MinTouchPx floor once its layout has
        /// resolved. Returns false while the rect still reads 0 (layout pending).</summary>
        private static bool EnsureTouchFloor(Button b)
        {
            if (b == null) return true;   // no button (single-button sheet) — nothing to do
            var rt = b.transform as RectTransform;
            if (rt == null) return true;
            float h = rt.rect.height;
            if (h <= 0.5f) return false;  // layout not resolved yet — retry next frame
            if (h < ElarionUiKit.MinTouchPx)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + (ElarionUiKit.MinTouchPx - h));
            return true;
        }
    }
}
