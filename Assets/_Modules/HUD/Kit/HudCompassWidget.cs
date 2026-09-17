// =============================================================================
// HudCompassWidget — the COMMON, reusable compass widget for the HUD kit (A4).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD.Kit
//
// Owner redirect (2026-07-03): the compass must be a REUSABLE kit widget dropped
// into the HUD 9-slice via the posture occupancy rows — NOT a bespoke standalone
// canvas. This replaces the old DeNelle.HUD.CompassHud (its own ScreenSpaceOverlay
// canvas, spawned by CompassHudBootstrap), which was fragile: a scene-load spawn
// RACE (spawned only if a hero already existed at that instant, no retry), no
// DontDestroyOnLoad (died on any single-mode scene load), and NO objective bearing.
// Built into the kit, the compass now inherits HudKitController's ONE persistent
// canvas + posture-driven visibility, so it shows in BOTH calm(town) and
// calm(explore) the same way every other widget does (hud-areas.json rows).
//
// PRESENTATION-ONLY (§5 / owner): it READS heading + world positions, owns NO
// game state. Three provider delegates (wired by HudKitController via reflection,
// so DeNelle.HUD keeps its "HUD -> Core only" edge — no DeNelle.Village ref):
//   • HeroProvider      — the hero Transform (bearing origin + heading fallback).
//   • ObjectiveProvider — world pos of the nearest objective / region-gate seam
//     (the navigation cue the owner asked for: "favor navigation over currency").
//   • EnemyProvider     — live enemy transforms (threat bearing ticks on the strip).
// Providers are polled on a cheap throttle (reflection scans stay ~4 Hz), never
// per-frame. Heading itself comes from Camera.main's flattened forward each frame
// ("up on screen = where you're heading"), hero forward as the no-camera fallback.
//
// WO-899 §2 (owner felt-test 2026-08-07: "make the compass wider so heading changes
// + enemy bearings read clearly"): the WO-438 compact rotating OCTAGON is retired for
// a proper horizontal HEADING STRIP. Same widget, same providers, same bearing math —
// only the presentation changed:
//   • the mount spans the FULL width of the Status area (was a ~5%-of-screen square);
//   • a scrolling CARDINAL TAPE (N/NE/E/SE/S/SW/W/NW) rides the SAME BearingToStripX
//     mapping the enemy pips use, so the ticks slide as you turn under a STATIC gold
//     centre caret that marks your heading;
//   • the fan widened 120 -> 160 degrees, so more of the field shows before a marker
//     becomes an edge arrow;
//   • the objective is a GOLD DIAMOND riding the same tape (the rotating needle had no
//     meaning on a strip) — a distinct SHAPE from the red apex-up enemy triangle and
//     from the gold apex-DOWN centre caret, because the owner is red/green colourblind
//     and meaning is never carried by colour alone (CLAUDE.md canon).
// Mobile-first; matches the shared ElarionUi kit language.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;   // F8-16: FlowTrace on the enemy-tick data path (HUD -> Core edge only)

namespace DeNelle.HUD.Kit
{
    /// <summary>The common HUD-kit compass widget (see header). Built by HudKitController,
    /// placed by the hud-areas.json occupancy rows, driven by provider delegates.</summary>
    [DisallowMultipleComponent]
    public sealed class HudCompassWidget : MonoBehaviour
    {
        // ── Provider seams (set by HudKitController; presentation reads the world) ──
        /// <summary>The hero transform — bearing origin + heading fallback.</summary>
        public Func<Transform> HeroProvider;
        /// <summary>World position of the nearest objective / region-gate seam, or null.</summary>
        public Func<Vector3?> ObjectiveProvider;
        /// <summary>Live enemy transforms for the threat ticks (may be null/empty).</summary>
        public Func<IReadOnlyList<Transform>> EnemyProvider;

        // The strip plots a ±FovDegrees fan centred on the heading; markers outside the
        // fan clamp to the nearest edge so they stay visible.
        // WO-899 §2: 120 -> 160. The strip is now ~6x wider on screen, so a wider fan puts
        // more of the world on the tape before a marker degrades into an edge arrow, and
        // still leaves ~4.7 px per degree to read a bearing at.
        private const float FovDegrees   = 160f;
        // F8-16: 4px hairlines over a 120° fan were imperceptible — the enemy mark is now a
        // 10px-wide RED TRIANGLE PIP (apex up), a distinct SHAPE vs the gold objective needle
        // (owner is red/green colorblind — meaning never by color alone, §7 canon).
        private const float TickWidthPx  = 10f;
        private const float ProviderPollInterval = 0.25f;   // reflection scans ~4 Hz, never per-frame

        // ── The cardinal tape (WO-899 §2) ─────────────────────────────────────
        // Eight fixed WORLD directions. Each frame their bearing relative to the heading is
        // taken with the SAME Vector3.SignedAngle call UpdateEnemyTicks uses, and mapped to
        // an X with the SAME BearingToStripX — no second bearing derivation exists.
        private static readonly string[] CardinalNames =
            { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };   // ASCII only (TMP build font)
        private static readonly Vector3[] CardinalDirs =
        {
            new Vector3( 0f, 0f,  1f),   // N  = +Z
            new Vector3( 1f, 0f,  1f),   // NE
            new Vector3( 1f, 0f,  0f),   // E  = +X
            new Vector3( 1f, 0f, -1f),   // SE
            new Vector3( 0f, 0f, -1f),   // S  = -Z
            new Vector3(-1f, 0f, -1f),   // SW
            new Vector3(-1f, 0f,  0f),   // W  = -X
            new Vector3(-1f, 0f,  1f),   // NW
        };

        // ── Runtime refs ──────────────────────────────────────────────────────
        private RectTransform _strip;
        private RectTransform _tickLayer;   // clipped tape layer: cardinal ticks + objective + enemy pips
        private TextMeshProUGUI _cardinal;
        private RectTransform _objMarker;    // WO-899: the GOLD DIAMOND objective marker, rides the tape
        private readonly RectTransform[] _cardTicks = new RectTransform[8];
        private readonly List<RectTransform> _tickPool = new List<RectTransform>();
        private readonly List<Transform> _enemyBuf = new List<Transform>();
        private Vector3? _objective;
        private Transform _hero;
        private Camera _camera;
        private float _pollTimer;
        private bool _built;

        /// <summary>The last-resolved hero (so a provider closure can read it without re-scanning).</summary>
        public Transform Hero => _hero;

        // ── F8-16 PROBE SEAMS (AutoPilot 'AssertCompassMarks') — read-only + one nudge ──
        // -nographics renders nothing, but the enemy-buffer fill and the pip RECT math both
        // run in LateUpdate, so a headless probe can assert the data half AND the layout half.

        /// <summary>True when HudKitController wired the enemy provider delegate.</summary>
        public bool EnemyProviderWired => EnemyProvider != null;

        /// <summary>Enemies currently in the tick buffer (last provider poll).</summary>
        public int EnemyMarkCount => _enemyBuf.Count;

        /// <summary>Pooled pip GameObjects ACTIVE this frame.</summary>
        public int ActiveTickCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _tickPool.Count; i++)
                    if (_tickPool[i] != null && _tickPool[i].gameObject.activeSelf) n++;
                return n;
            }
        }

        /// <summary>Rect size (sizeDelta) of the first ACTIVE pip — the F8-16 visibility-floor
        /// assert (width >= 10, height >= 16). False when no pip is active.</summary>
        public bool TryGetFirstActiveTickSize(out Vector2 size)
        {
            for (int i = 0; i < _tickPool.Count; i++)
            {
                var t = _tickPool[i];
                if (t != null && t.gameObject.activeSelf) { size = t.sizeDelta; return true; }
            }
            size = Vector2.zero;
            return false;
        }

        /// <summary>Force the next LateUpdate to poll the providers immediately (probe
        /// determinism — skips the remaining 0.25s throttle window; no other effect).</summary>
        public void ForceProviderPoll()
        {
            // Fleet 3/4 (AssertCompassMarks link 2): a timer-only nudge is DEAD on an
            // inactive widget instance (no LateUpdate -> no poll -> buffer empty forever,
            // while the wired-provider + live-enemy checks all pass). Poll NOW instead —
            // read-only provider refresh, safe regardless of active state.
            _pollTimer = 0f;
            if (HeroProvider != null) _hero = HeroProvider();
            _objective = ObjectiveProvider != null ? ObjectiveProvider() : (Vector3?)null;
            RefreshEnemies();
        }

        /// <summary>Kit-builder factory: create the compass under a parent RectTransform
        /// (HudKitController wraps + registers it like every other widget).</summary>
        public static HudCompassWidget Create(Transform parent)
        {
            var go = new GameObject("CompassWidget", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var w = go.AddComponent<HudCompassWidget>();
            w.Build();
            return w;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            using var _flow = FlowTrace.Enter("Compass", "Build heading strip (WO-899)");

            Guard.Try("Compass", "heading strip construction", () =>
            {
                // WO-899 §2: a WIDE top band, not the WO-438 square. The widget root fills the
                // hud-areas "status" mount (screen x 0.340-0.660), so spanning the mount's full
                // width puts the strip at ~32% of screen width — about six times the old octagon.
                // It sits in the TOP of the mount and is SHORTER than the octagon was, which also
                // frees the lower half of the mount that waveBlock shares.
                // TODO(dedup): two compass implementations still exist — this HUD-kit one and the
                // standalone DeNelle.HUD.CompassHud (its own canvas). This kit widget is the
                // intended survivor; left untouched here to avoid removing a live widget blind.
                //
                // VERTICAL EXTENT IS DELIBERATELY UNCHANGED (y 0.34 -> 1.00, exactly the octagon's
                // band). Only the WIDTH changes, so the strip cannot newly collide with waveBlock,
                // which shares this mount. At the kit's 1080x1920 / match-0.5 scaler the mount
                // resolves to roughly 678 x 142 reference units on a 2340x1080 device, so the strip
                // is ~678 x 94 units — every band below is budgeted against that, because a band
                // shorter than its font CULLS ITS GLYPHS (the F8 2026-07-08 "0 visible glyphs,
                // rect 333x25" defect). Every text here also auto-sizes as a second guarantee.
                _strip = NewRect("CompassStrip", (RectTransform)transform);
                _strip.anchorMin = new Vector2(0.00f, 0.34f);
                _strip.anchorMax = new Vector2(1.00f, 1.00f);
                _strip.offsetMin = Vector2.zero; _strip.offsetMax = Vector2.zero;

                // ── the tape's dark-glass plate + gold-dim rim (rim FIRST = behind) ──
                AddPlate("StripRim", _strip, 0.00f, 0.60f, new Color(0.604f, 0.498f, 0.243f, 0.55f), 0f);
                AddPlate("StripPlate", _strip, 0.012f, 0.585f, new Color(0.043f, 0.047f, 0.059f, 0.86f), 3f);

                // ── the clipped TAPE layer: cardinal ticks + objective + enemy pips ──
                // F8-16 (kept): the band spans the strip's FULL usable width and is tall enough to
                // hold a clearly-visible triangle pip. RectMask2D clips a tick that is sliding off
                // the end, which is what makes it read as a moving tape rather than popping markers.
                var layerGo = new GameObject("CompassMarkers", typeof(RectTransform), typeof(RectMask2D));
                layerGo.transform.SetParent(_strip, false);
                _tickLayer = layerGo.GetComponent<RectTransform>();
                _tickLayer.anchorMin = new Vector2(0.016f, 0.03f);   // ~49 ref units tall: pips clear
                _tickLayer.anchorMax = new Vector2(0.984f, 0.55f);   // the 16px floor with room to spare
                _tickLayer.offsetMin = Vector2.zero; _tickLayer.offsetMax = Vector2.zero;

                // ── the eight cardinal ticks (label + graduation bar), built once ──
                for (int i = 0; i < CardinalNames.Length; i++)
                {
                    var tick = NewRect("Card_" + CardinalNames[i], _tickLayer);
                    tick.anchorMin = new Vector2(0.5f, 0f);
                    tick.anchorMax = new Vector2(0.5f, 1f);   // stretch in Y: never a pre-layout 0-height bake
                    tick.pivot     = new Vector2(0.5f, 0.5f);
                    tick.sizeDelta = new Vector2(76f, 0f);
                    tick.anchoredPosition = Vector2.zero;

                    bool north = i == 0;
                    var lbl = AddText(tick, CardinalNames[i], ElarionUi.FontFloorMobile,
                                      north ? ElarionUi.Gilt : ElarionUi.Parchment,
                                      TextAlignmentOptions.Top);
                    lbl.fontStyle = FontStyles.Bold;
                    lbl.characterSpacing = 2f;
                    // Autosize floor/ceiling so a short tape band can never cull the letters.
                    lbl.enableAutoSizing = true;
                    // ⭐ WO-1826 — THE 30 px FLOOR, APPLIED HUD-WIDE (owner: "do it, apply the 30
                    // floor to the hud", after the WO-1823 player report "Text is too small to
                    // read"). WAS: min 14f, and a max of 30 for N but 26 for the other seven — so
                    // every non-north tick was authored UNDER ElarionUi.FontFloorMobile and could
                    // additionally shrink to 14. The min and the max are raised TOGETHER and
                    // uniformly: raising the min alone would leave min > max on E/NE/S/SW/W/NW.
                    // North stays distinguished by COLOUR (Gilt) and by its thicker graduation bar
                    // (4 px vs 2 px, below), not by being the only legible tick.
                    // Seat proof: the tick label fills _tickLayer (y 0.03..0.55 of a strip that is
                    // y 0.34..1.00 of a ~142 ref px mount) = ~49 ref px, and a 30 px line needs
                    // 36 (30 x HudLabelFitRegression.LineHeightFactor 1.2). Width: the tick is
                    // 76 ref px wide and the widest name is two glyphs.
                    lbl.fontSizeMin = ElarionUi.FontFloorMobile;
                    lbl.fontSizeMax = ElarionUi.FontFloorMobile;

                    // Graduation bar under the letters — the precise position read.
                    var bar = NewRect("Grad", tick);
                    bar.anchorMin = new Vector2(0.5f, 0f);
                    bar.anchorMax = new Vector2(0.5f, 0f);
                    bar.pivot     = new Vector2(0.5f, 0f);
                    bar.sizeDelta = new Vector2(north ? 4f : 2f, 12f);
                    bar.anchoredPosition = Vector2.zero;
                    var barImg = bar.gameObject.AddComponent<Image>();
                    barImg.color = north ? ElarionUi.Gilt : new Color(0.604f, 0.498f, 0.243f, 0.85f);
                    barImg.raycastTarget = false;

                    tick.gameObject.SetActive(false);
                    _cardTicks[i] = tick;
                }

                // ── the OBJECTIVE marker: a gold DIAMOND (rounded square rotated 45 deg) ──
                // Shape-first: distinct from the red apex-up enemy pip AND from the gold apex-down
                // centre caret, so the three markers are told apart with colour fully desaturated.
                _objMarker = NewRect("ObjectiveMarker", _tickLayer);
                _objMarker.anchorMin = new Vector2(0.5f, 0.5f);
                _objMarker.anchorMax = new Vector2(0.5f, 0.5f);
                _objMarker.pivot     = new Vector2(0.5f, 0.5f);
                _objMarker.sizeDelta = new Vector2(20f, 20f);
                _objMarker.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var objImg = _objMarker.gameObject.AddComponent<Image>();
                ElarionUiKit.ApplyRounded(objImg);
                objImg.color = ElarionUi.Gilt;
                objImg.raycastTarget = false;
                _objMarker.gameObject.SetActive(false);

                // ── the STATIC centre marker: a hairline through the tape + a gold apex-DOWN caret ──
                // "Where you are pointing" never moves; the tape moves under it.
                var line = NewRect("CentreLine", _strip);
                line.anchorMin = new Vector2(0.5f, 0.03f);
                line.anchorMax = new Vector2(0.5f, 0.55f);
                line.pivot     = new Vector2(0.5f, 0.5f);
                line.sizeDelta = new Vector2(3f, 0f);
                line.anchoredPosition = Vector2.zero;
                var lineImg = line.gameObject.AddComponent<Image>();
                lineImg.color = new Color(ElarionUi.Gilt.r, ElarionUi.Gilt.g, ElarionUi.Gilt.b, 0.85f);
                lineImg.raycastTarget = false;

                var caret = NewRect("CentreCaret", _strip);
                caret.anchorMin = new Vector2(0.5f, 0.55f);
                caret.anchorMax = new Vector2(0.5f, 0.55f);
                caret.pivot     = new Vector2(0.5f, 0.5f);
                caret.sizeDelta = new Vector2(26f, 16f);
                caret.localRotation = Quaternion.Euler(0f, 0f, 180f);   // apex DOWN into the tape
                var caretImg = caret.gameObject.AddComponent<Image>();
                caretImg.sprite = EnemyPipSprite();                     // the shared apex-up triangle, flipped
                caretImg.color = ElarionUi.Gilt;
                caretImg.raycastTarget = false;

                // ── the heading readout, above the tape ──
                // FontMicro(32), NOT FontLabel(40): this band resolves to ~42 reference units and a
                // 40pt line needs ~47 with leading, which is exactly how a label gets culled here.
                // Autosizing down to 18 is the belt-and-braces guarantee.
                _cardinal = AddText(_strip, "N", ElarionUi.FontMicro, ElarionUi.Gilt, TextAlignmentOptions.Center);
                var crt = (RectTransform)_cardinal.transform;
                crt.anchorMin = new Vector2(0f, 0.56f);
                crt.anchorMax = new Vector2(1f, 1f);
                crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
                _cardinal.fontStyle = FontStyles.Bold;
                _cardinal.characterSpacing = 6f;
                _cardinal.enableAutoSizing = true;
                // WO-1826: was 18f, under ElarionUi.FontFloorMobile — the heading readout was
                // allowed to auto-shrink to 18 on a band that seats the floor. The band is
                // y 0.56..1.00 of the ~94 ref px strip = ~41 ref px, and a 30 px line needs 36.
                // fontSizeMax stays ElarionUi.FontMicro (32) — already above the floor, and the
                // comment above explains why 40 would be culled here.
                _cardinal.fontSizeMin = ElarionUi.FontFloorMobile;
                _cardinal.fontSizeMax = ElarionUi.FontMicro;

                FlowTrace.Step("Compass",
                    $"heading strip built: full-width tape, fan={FovDegrees:0} deg, 8 cardinal ticks, gold diamond objective, apex-down centre caret.");
            });
        }

        // A rounded, non-raycast plate spanning the strip's width. yTop is a fraction of the
        // strip height; inset shrinks it on x so a rim can peek out behind its face.
        private static void AddPlate(string name, RectTransform parent, float yBottom, float yTop,
                                     Color color, float inset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, yBottom);
            rt.anchorMax = new Vector2(1f, yTop);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            var img = go.GetComponent<Image>();
            var medievalFrame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (medievalFrame != null)
            {
                img.sprite = medievalFrame;
                img.type = Image.Type.Simple;
                img.preserveAspect = false;
                // One shared black-iron/gold shell replaces the temporary rounded rectangles.
                // The inner plate stays slightly dimmer so the moving tape remains readable.
                img.color = name == "StripRim"
                    ? new Color(1f, 1f, 1f, 0.96f)
                    : new Color(0.42f, 0.39f, 0.31f, 0.72f);
            }
            else
            {
                ElarionUiKit.ApplyRounded(img);
                img.color = color;
            }
            img.raycastTarget = false;   // the compass is a READOUT: it never eats a tap
        }

        private void LateUpdate()
        {
            if (!_built) return;
            if (_camera == null) _camera = Camera.main;

            // Cheap throttled provider polls (reflection scans stay off the hot path).
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = ProviderPollInterval;
                if (HeroProvider != null && (_hero == null || !_hero)) _hero = HeroProvider();
                else if (HeroProvider != null) _hero = HeroProvider();   // keep fresh across scene swaps
                _objective = ObjectiveProvider != null ? ObjectiveProvider() : (Vector3?)null;
                RefreshEnemies();
            }

            Vector3 fwd = HeadingForward();
            UpdateCardinal(fwd);
            UpdateCardinalTape(fwd);
            UpdateObjective(fwd);
            UpdateEnemyTicks(fwd);
        }

        // Heading = camera flattened forward ("up on screen = where you're heading");
        // hero forward is the fallback when there is no camera.
        private Vector3 HeadingForward()
        {
            Vector3 fwd;
            if (_camera != null)   fwd = _camera.transform.forward;
            else if (_hero != null) fwd = _hero.forward;
            else                    fwd = Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            return fwd.normalized;
        }

        // Clockwise bearing from +Z (North): +Z=N, +X=E, -Z=S, -X=W.
        private void UpdateCardinal(Vector3 fwd)
        {
            if (_cardinal == null) return;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (yaw < 0f) yaw += 360f;

            string heading;
            if      (yaw < 22.5f  || yaw >= 337.5f) heading = "N";
            else if (yaw < 67.5f)                   heading = "NE";
            else if (yaw < 112.5f)                  heading = "E";
            else if (yaw < 157.5f)                  heading = "SE";
            else if (yaw < 202.5f)                  heading = "S";
            else if (yaw < 247.5f)                  heading = "SW";
            else if (yaw < 292.5f)                  heading = "W";
            else                                    heading = "NW";
            // ASCII ONLY in a user-visible string — the build's LiberationSans SDF lacks the
            // degree sign and renders tofu (this line ships in editor play sessions too).
#if UNITY_EDITOR
            _cardinal.text = heading + "   (" + Mathf.RoundToInt(yaw) + " deg)";
#else
            _cardinal.text = heading;
#endif
        }

        // WO-899 §2: slide the eight cardinal ticks along the tape. The bearing of each fixed
        // world direction relative to the heading is taken with the SAME Vector3.SignedAngle
        // call UpdateEnemyTicks uses, and mapped with the SAME BearingToStripX — the mapping is
        // NOT re-derived here. clampToEdge:false so a tick slides off the end and is clipped by
        // the RectMask2D (a tape), instead of piling up on the edge the way a threat marker must.
        private void UpdateCardinalTape(Vector3 fwd)
        {
            if (_tickLayer == null) return;
            float halfFov = FovDegrees * 0.5f;
            for (int i = 0; i < _cardTicks.Length; i++)
            {
                var tick = _cardTicks[i];
                if (tick == null) continue;
                float bearing = Vector3.SignedAngle(fwd, CardinalDirs[i].normalized, Vector3.up);
                // A tick more than one label-width past the fan edge can never be seen through
                // the mask — keep it inactive rather than laying it out every frame.
                bool show = Mathf.Abs(bearing) <= halfFov + 12f;
                if (tick.gameObject.activeSelf != show) tick.gameObject.SetActive(show);
                if (!show) continue;
                tick.anchoredPosition = new Vector2(BearingToStripX(bearing, out _, clampToEdge: false), 0f);
            }
        }

        // WO-899 §2: the objective is a GOLD DIAMOND riding the tape at its real bearing
        // (the WO-438 rotating needle had no meaning on a strip). CLAMPED to the fan edge so
        // the "where do I go" cue can never disappear — the same guarantee the needle gave.
        // Hidden only when there is no objective at all.
        private void UpdateObjective(Vector3 fwd)
        {
            if (_objMarker == null) return;
            if (_objective == null || _hero == null || !_hero)
            {
                if (_objMarker.gameObject.activeSelf) _objMarker.gameObject.SetActive(false);
                return;
            }

            Vector3 to = _objective.Value - _hero.position; to.y = 0f;
            if (to.sqrMagnitude < 1e-4f)
            {
                if (_objMarker.gameObject.activeSelf) _objMarker.gameObject.SetActive(false);
                return;
            }
            to.Normalize();

            float bearing = Vector3.SignedAngle(fwd, to, Vector3.up);   // + = to the right
            if (!_objMarker.gameObject.activeSelf) _objMarker.gameObject.SetActive(true);
            _objMarker.anchoredPosition = new Vector2(BearingToStripX(bearing, out _), 0f);
        }

        private void UpdateEnemyTicks(Vector3 fwd)
        {
            if (_tickLayer == null || _hero == null || !_hero) { HideAllTicks(); return; }

            int n = _enemyBuf.Count;
            EnsureTickPool(n);
            Vector3 heroPos = _hero.position;
            int clampedCount = 0;   // enemies outside the ±60° fan this frame (edge-arrow mode)

            for (int i = 0; i < _tickPool.Count; i++)
            {
                var tick = _tickPool[i];
                if (i >= n || _enemyBuf[i] == null)
                {
                    if (tick.gameObject.activeSelf) tick.gameObject.SetActive(false);
                    continue;
                }
                Vector3 to = _enemyBuf[i].position - heroPos; to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) { if (tick.gameObject.activeSelf) tick.gameObject.SetActive(false); continue; }
                to.Normalize();

                float bearing = Vector3.SignedAngle(fwd, to, Vector3.up);
                float x = BearingToStripX(bearing, out bool clamped);
                if (!tick.gameObject.activeSelf) tick.gameObject.SetActive(true);
                tick.anchoredPosition = new Vector2(x, 0f);

                // F8-16 edge-arrow port (old CompassHud.UpdateArrows): an enemy OUTSIDE the fan
                // pins to the band edge with the triangle ROTATED to point the shorter way around
                // (apex-up sprite: -90° = points right, +90° = points left). Inside the fan the
                // pip stands apex-up at its bearing. Same pooled Image — zero extra alloc.
                if (clamped)
                {
                    clampedCount++;
                    tick.localRotation = Quaternion.Euler(0f, 0f, bearing > 0f ? -90f : 90f);
                }
                else if (tick.localRotation != Quaternion.identity)
                {
                    tick.localRotation = Quaternion.identity;
                }
            }

            // One Step on the ENGAGE transition only (never per-frame spam).
            if (clampedCount > 0 && _lastClampedCount == 0)
                FlowTrace.Step("Compass", $"edge-arrows engaged ({clampedCount} enemies outside the ±{FovDegrees * 0.5f:0}° fan).");
            _lastClampedCount = clampedCount;
        }

        private int _lastClampedCount;

        // Map a signed bearing (deg, + = right) to an X offset across the strip width.
        // THE single bearing->X mapping for every marker on the tape (enemy pips, the objective
        // diamond and the cardinal ticks all call this — nothing re-derives it).
        //
        // clampToEdge (WO-899): true (the default, unchanged for every existing caller) pins an
        // off-fan bearing to the nearest edge so a THREAT/objective marker never disappears.
        // false lets a marker keep sliding past the edge, where the tape's RectMask2D clips it —
        // which is what makes the cardinal ticks read as a scrolling tape instead of piling up.
        // <c>clamped</c> reports off-fan either way, so the edge-arrow logic is unaffected.
        private float BearingToStripX(float bearing, out bool clamped, bool clampToEdge = true)
        {
            float halfFov = FovDegrees * 0.5f;
            clamped = bearing < -halfFov || bearing > halfFov;
            float c = clampToEdge ? Mathf.Clamp(bearing, -halfFov, halfFov) : bearing;
            float t = (c + halfFov) / FovDegrees;                 // 0..1 across the strip
            float stripW = _tickLayer != null ? _tickLayer.rect.width : 0f;
            return (t - 0.5f) * stripW;
        }

        private int _lastEnemyCount = -1;

        private void RefreshEnemies()
        {
            _enemyBuf.Clear();
            var list = EnemyProvider != null ? EnemyProvider() : null;
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) _enemyBuf.Add(list[i]);

            // F8-16 instrumentation ("red ticks exist but the owner reports none render"):
            // log ON CHANGE only (never per-poll) so the next capture PROVES which half fails —
            // data-empty (count stays 0 => provider/type resolution) vs built-but-invisible
            // (count > 0 while the owner sees nothing => a render/layout issue in this widget).
            if (_enemyBuf.Count != _lastEnemyCount)
            {
                FlowTrace.Step("Compass",
                    // WO-976 sibling (registry LOW): `hero=ok` was a bare non-null check reading as a
                    // health claim — the WRONG hero (a stale pre-reload instance, or a body proxy
                    // rather than the root) printed `ok` while every bearing computed from it was
                    // wrong. Retokened to `heroRef=` + the object's name: still cheap, but now the
                    // capture shows WHICH transform we bound, so a wrong-hero bind is readable.
                    // Advisory breadcrumb, not a verification — the falsifiable compass assertion
                    // lives in AutoPilotDriver.AssertCompassMarks (measured pip rect vs a px floor).
                    $"enemy tick source count {_lastEnemyCount} -> {_enemyBuf.Count} (provider={(EnemyProvider != null ? "wired" : "NULL")}, heroRef={(_hero != null ? _hero.name : "NULL")}).");
                _lastEnemyCount = _enemyBuf.Count;
            }
        }

        private void HideAllTicks()
        {
            for (int i = 0; i < _tickPool.Count; i++)
                if (_tickPool[i].gameObject.activeSelf) _tickPool[i].gameObject.SetActive(false);
        }

        private void EnsureTickPool(int count)
        {
            while (_tickPool.Count < count)
            {
                var go = new GameObject("EnemyTick", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_tickLayer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                // F8-16 hardening (kept from the earlier landing): the layer rect can still be
                // 0-height when the FIRST enemies appear on the same frame the pool grows (layout
                // not yet run) — the old Mathf.Max(4f, ...) then baked a 4x4px sliver FOREVER
                // (sizeDelta is set once per pooled tick). Floor at 16px so a pip is never
                // sub-visible on mobile.
                rt.sizeDelta = new Vector2(TickWidthPx, Mathf.Max(16f, _tickLayer.rect.height - 4f));
                var img = go.GetComponent<Image>();
                // F8-16 colorblind-safe SHAPE: red apex-up TRIANGLE pip (procedural sprite,
                // built once, static-cached) — a distinct silhouette vs the gold objective
                // needle even with red/green desaturated. Doubles as the edge ARROW when
                // rotated ±90° by UpdateEnemyTicks.
                img.sprite = EnemyPipSprite();
                img.color = ElarionUi.Danger;
                img.raycastTarget = false;
                go.SetActive(false);
                _tickPool.Add(rt);
            }
        }

        // Procedural white apex-up triangle, tinted by Image.color. Built ONCE per app run
        // (static cache, HideAndDontSave) — no per-frame or per-tick allocation.
        // WO-828: `internal` (was private) so HudMinimapWidget draws its THREAT pip from
        // this exact sprite instead of re-deriving a second triangle. One owner, one
        // silhouette — the compass and the minimap must never disagree about what a
        // threat looks like, because that shape IS the colourblind-safe channel.
        private static Sprite _pipSprite;
        internal static Sprite EnemyPipSprite()
        {
            if (_pipSprite != null) return _pipSprite;
            const int W = 16, H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color32[W * H];
            float cx = (W - 1) * 0.5f;
            for (int y = 0; y < H; y++)
            {
                float t = (float)y / (H - 1);                 // 0 = bottom (wide) .. 1 = top (apex)
                float half = (1f - t) * cx;
                for (int x = 0; x < W; x++)
                {
                    bool inside = Mathf.Abs(x - cx) <= half;
                    px[y * W + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _pipSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            _pipSprite.hideFlags = HideFlags.HideAndDontSave;
            return _pipSprite;
        }

        // ── uGUI helpers (mirror the kit conventions) ──────────────────────────
        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static TextMeshProUGUI AddText(Transform parent, string text, float size,
            Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }
    }
}
