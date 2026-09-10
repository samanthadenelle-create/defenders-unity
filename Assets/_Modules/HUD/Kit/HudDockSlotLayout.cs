// =============================================================================
// HudDockSlotLayout (WO-1319) — the bottom dock's medallions are laid out in
// REFERENCE PIXELS, live, and re-solved whenever the surface changes.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD.Kit
//
// WHY A COMPONENT AND NOT BUILD-TIME MATH. The defect this closes was captured in a
// DESKTOP BROWSER WINDOW the owner had made tall and narrow — i.e. the surface changed
// AFTER the HUD was built, and can change again on the next drag. A number computed once
// in BuildAdaptivePeacefulDock would be stale the moment the window moved, and a WebGL
// canvas resize is a first-class, shipping event (a Pi Browser phone in portrait, before
// WO-1312's rotation engages or if its fail-safe fires, lands in exactly this shape).
// So the geometry is RE-SOLVED from the mount's live rect.
//
// THE ARITHMETIC IS NOT HERE. It lives in DeNelle.Core.UI.HudDockLayout — pure, public,
// static, and replayed headlessly by the editor oracle. This class only measures, applies,
// and traces. (See that file's header for the measured chain that produced
// "BUILDTALKHERO...QUEUE MANAGE" and for the four-rung degradation ladder.)
//
// TWO THINGS IT DELIBERATELY DOES:
//  * It writes ABSOLUTE PIXEL offsets against the track's LEFT edge (anchorMin.x ==
//    anchorMax.x == 0), never 1/n fractions. Fractions are what collapsed under the touch
//    floor in the first place.
//  * It keeps re-applying for a few frames after every change, because ElarionUiKit's
//    UiKitMinTouchGuard is a one-shot LateUpdate whose ordering against this one is
//    undefined. In tiers 1-3 the guard is a no-op by construction (every slot is already
//    >= MinTouchPx); the settle window is belt-and-braces for the frame it fires.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;

namespace DeNelle.HUD.Kit
{
    /// <summary>Live pixel layout for a row of action-dock medallions (WO-1319).</summary>
    [DisallowMultipleComponent]
    public sealed class HudDockSlotLayout : MonoBehaviour
    {
        private readonly List<RectTransform> _slots = new List<RectTransform>();
        private readonly List<TMP_Text> _captions = new List<TMP_Text>();
        // WO-1671 — the caption's obsidian backing band. It MUST be toggled with the caption:
        // this class already hides captions on the icon-only tier (sol.ShowCaptions below), and a
        // plate that stayed visible there would paint an empty black smear under five wordless
        // medallions. One list, one toggle, no second owner of caption visibility.
        private readonly List<GameObject> _captionPlates = new List<GameObject>();

        private RectTransform _track;      // the dock root (expands right past its mount)
        private float _y0 = 0.08f, _y1 = 0.94f;
        private float _rightHeadroomRatio;  // free width to the RIGHT, as a multiple of the mount width
        private float _gapFraction = HudDockLayout.GapFraction;
        private float _lastMountWidth = -1f;
        private int _settleFrames;
        private int _lastTier = -1;
        private bool _lastCaptions = true;
        // Apply() writes this transform's own rect, which re-enters
        // OnRectTransformDimensionsChange. The write is idempotent so it converges either way,
        // but the flag keeps a resize from costing an extra solve per frame.
        private bool _applying;

        /// <summary>Bind the dock root and its vertical band. <paramref name="rightHeadroomRatio"/>
        /// is how much free width sits to the RIGHT of the authored mount, expressed as a multiple
        /// of the mount's own width (see HudAreasHost.ActionBarRightHeadroomRatio).</summary>
        public void Configure(RectTransform track, float y0, float y1, float rightHeadroomRatio,
            float gapFraction)
        {
            _track = track;
            _y0 = y0;
            _y1 = y1;
            _rightHeadroomRatio = Mathf.Max(0f, rightHeadroomRatio);
            _gapFraction = gapFraction;
            MarkDirty();
        }

        /// <summary>Register one medallion (and its optional caption) in left-to-right order.</summary>
        public void AddSlot(RectTransform slot, TMP_Text caption)
        {
            AddSlot(slot, caption, null);
        }

        /// <summary>Register one medallion with its caption AND that caption's WO-1671 obsidian
        /// backing plate, so the two are shown and hidden as one thing. <paramref name="captionPlate"/>
        /// may be null (the combat dock passes none today).</summary>
        public void AddSlot(RectTransform slot, TMP_Text caption, GameObject captionPlate)
        {
            if (slot == null) return;
            _slots.Add(slot);
            _captions.Add(caption);
            _captionPlates.Add(captionPlate);
            MarkDirty();
        }

        /// <summary>Force a re-solve on the next LateUpdate.</summary>
        public void MarkDirty()
        {
            _lastMountWidth = -1f;
            _settleFrames = 4;
        }

        private void OnEnable() { MarkDirty(); }

        // A reparent (occupancy moves the widget into its area mount) or a canvas resize
        // both land here — this is the cheap, correct wake-up for a browser window drag.
        private void OnRectTransformDimensionsChange() { if (!_applying) MarkDirty(); }

        private void LateUpdate()
        {
            if (_track == null || _slots.Count == 0) return;
            var mount = _track.parent as RectTransform;
            if (mount == null) return;

            float mountW = mount.rect.width;
            if (mountW <= 1f) return;   // not laid out yet (or a zero-sized pool parent)

            bool changed = Mathf.Abs(mountW - _lastMountWidth) > 0.5f;
            if (!changed && _settleFrames <= 0) return;
            if (!changed && _settleFrames > 0) _settleFrames--;

            _lastMountWidth = mountW;
            if (changed) _settleFrames = 4;

            Apply(mountW);
        }

        private void Apply(float mountW)
        {
            _applying = true;
            Guard.Try("HudKit", "dock slot layout", () =>
            {
                float maxTrack = mountW * (1f + _rightHeadroomRatio);
                var sol = HudDockLayout.Solve(_slots.Count, mountW, maxTrack, _gapFraction);

                // The track grows RIGHT only. offsetMin stays zero so the dock's LEFT edge
                // never crosses into the MoveCluster's column.
                _track.anchorMin = new Vector2(0f, 0f);
                _track.anchorMax = new Vector2(1f, 1f);
                _track.offsetMin = Vector2.zero;
                _track.offsetMax = new Vector2(sol.RightExpansionPx, 0f);

                for (int i = 0; i < _slots.Count; i++)
                {
                    var rt = _slots[i];
                    if (rt == null) continue;
                    float left = sol.SlotLeftPx(i);
                    // Equal x anchors => offsetMin.x / offsetMax.x are absolute positions in
                    // reference px from the track's left edge. No 1/n fractions anywhere.
                    rt.anchorMin = new Vector2(0f, _y0);
                    rt.anchorMax = new Vector2(0f, _y1);
                    rt.offsetMin = new Vector2(left, 0f);
                    rt.offsetMax = new Vector2(left + sol.SlotWidthPx, 0f);

                    var cap = i < _captions.Count ? _captions[i] : null;
                    if (cap != null && cap.gameObject.activeSelf != sol.ShowCaptions)
                        cap.gameObject.SetActive(sol.ShowCaptions);
                    // WO-1671 — the plate follows the word it backs, never the other way round.
                    var plate = i < _captionPlates.Count ? _captionPlates[i] : null;
                    if (plate != null && plate.activeSelf != sol.ShowCaptions)
                        plate.SetActive(sol.ShowCaptions);
                }

                // Trace only when the SHAPE changes — this runs on a resize, not per frame,
                // but a drag can fire it many times and the log is not a firehose.
                if (sol.Tier != _lastTier || sol.ShowCaptions != _lastCaptions)
                {
                    _lastTier = sol.Tier;
                    _lastCaptions = sol.ShowCaptions;
                    string line = "action dock re-solved -> " + sol.ToString();
                    if (sol.Overflowed)
                        FlowTrace.Warn("HudKit", line + " | the surface is too narrow to seat " +
                            _slots.Count + " faces at MinTouchPx(" +
                            HudDockLayout.MinSlotPx.ToString("0") + ") in one row; faces are " +
                            "icon-only and the touch floor is unreachable here. Not a code bug - " +
                            "a physically impossible width (roughly narrower than 1:3.6).");
                    else
                        FlowTrace.Step("HudKit", line);
                }
            });
            _applying = false;
        }
    }
}
