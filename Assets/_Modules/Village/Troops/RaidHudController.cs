// =============================================================================
// RaidHudController — the LIVE raid HUD (WO-771.11, LOCKED teleport/deploy loop).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The passive readout during a raid: a 180s countdown, a star-progress indicator,
// live %-destruction, and troops alive/deployed. It reads RaidScoring.Instance every
// frame (passive — it never drives combat) and renders through code-built uGUI
// (NO UXML — repo rule §8), mirroring RaidDeployController's self-install +
// ElarionUiKit chrome.
//
// ⚠ WO-1464 CORRECTION: this header used to say the deploy tray (bottom) and this HUD
// (top) "sit on complementary edges and never overlap". They never overlapped EACH
// OTHER, and neither of them was ever checked against the town HUD underneath — which
// is how this panel came to paint its clock across the hero nameplate and its stars
// across the compass on the owner's device. Both seats now come from
// HudLayoutBands (DeNelle.Core.UI) and the exclusion is a red gate, not a claim.
//
// COLOURBLIND-SAFE (repo law): every state reads by SHAPE / MOTION / NUMBER, never
// hue alone — the timer is a number + a shrinking bar (+ a pulse under 30s), stars
// are large/small DIAMONDS + a pop on loss + an "n/3" count, destruction is a number
// + a fill bar, troops are a plain "alive/deployed" number.
//
// ⚠ WO-1594: the stars bind RaidScoring.PresentationStars (HONOR — three lit at engage,
// snuffed as milestones pass), NOT ProjectedStars (the earn-up settle preview). Do not
// "restore" ProjectedStars here: a HUD that earns up from 0 tells the player nothing
// during the fight and then jumps at the end, which is the defect the ticket names.
//
// ASCII-only runtime strings. Canon: the village is Elarion (never Avalon).
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// The live raid HUD (timer / stars / destruction% / troop counts). Passive —
    /// binds <see cref="RaidScoring"/> and renders; never mutates game state.
    /// Self-installs into any <c>RaidBase_*</c> scene (idempotent).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidHudController : MonoBehaviour
    {
        // Refresh cadence — the HUD polls the scorer ~10x/sec (cheap; the numbers
        // change slowly). The timer text still counts smoothly enough at 10Hz.
        private const float RefreshInterval = 0.1f;
        private float _refreshTimer;

        private GameObject _ui;

        // Widgets refreshed each poll.
        private TMPro.TextMeshProUGUI _timerLabel;
        private RectTransform _timerFill;          // shrinks left->right with the clock
        private TMPro.TextMeshProUGUI _destLabel;
        private RectTransform _destFill;           // grows with destruction%
        // THE OBJECTIVE (owner concept 2026-08-02): the raid is won by razing the central
        // spire, so the headline readout on the right is the spire's HP, not a corpse count.
        private TMPro.TextMeshProUGUI _objLabel;
        private RectTransform _objFill;            // DRAINS as the spire is chipped down
        private TMPro.TextMeshProUGUI _troopLabel;
        private TMPro.TextMeshProUGUI _starCount;  // "n/3"
        private readonly Image[] _starDiamonds = new Image[3];

        private static readonly Color StarLit = ElarionUi.Gilt;

        // ⛔ WO-1639 DEFECT A, SECOND HALF: THE "EMPTY" TOKENS WERE AUTHORED FOR A GREY PLATE.
        // The three progress TRACKS (timer / objective / razed) shipped as
        // `new Color(0f, 0f, 0f, 0.5f)` - a DARK groove, which read as a groove only because the
        // plate behind it was a translucent grey. On the kit's near-black ObsidianFill the plate
        // composites to ~0.037 sRGB and 50% black lands at ~0.019: **1.03 : 1**. The empty part of
        // every bar would VANISH, leaving a gilt line of varying length with nothing to say what
        // "full" was - and the draining/filling bar is the MOTION channel the repo's colourblind
        // law leans on, so losing it is worse than the illegibility this ticket started with.
        // The same logic applies to an unlit honor diamond, so both take ONE value: white at 0.40,
        // which predicts 3.77 : 1 against the plate (over the 3:1 non-text component floor) while
        // staying ~4x dimmer in luminance than a lit/filled Gilt element (12.2 : 1). Empty reads
        // as empty, full reads as full, and neither is a hue decision.
        private static readonly Color EmptyTrackFill = new Color(1f, 1f, 1f, 0.40f);
        // ⚠ WO-1639 DEFECT A: alpha was 0.14f and it measured 1.55:1 against the readout plate on
        // the owner's device frame (2026-09-10_0608_arena_01_entry.png). An unlit honor diamond is
        // a non-text UI COMPONENT, so its floor is 3:1, not the 4.5:1 the text rows answer to.
        // 0.40 over ElarionUiKit.ObsidianFill predicts 3.77:1 - above the floor with margin, and
        // still ~4x dimmer in luminance than a lit Gilt diamond (12.2:1), so the lit/unlit read
        // survives. The PRIMARY channel is unchanged and remains SHAPE (StarSizeLit 34 vs
        // StarSizeLost 20) plus the hue-free "n/3" number, per the repo's colourblind law - this
        // only stops a lost star from vanishing entirely on a bright arena.
        private static readonly Color StarDim = EmptyTrackFill;

        // ── WO-1594 honor stars: SHAPE carries the state, not hue ────────────────────────
        // The owner is red/green colourblind (repo law), and dimming alone is luminance, not
        // shape - on a bright band a 14% alpha diamond and a gilt one can read as the same
        // token. A snuffed star therefore also SHRINKS, so lit vs lost is a silhouette
        // difference at a glance, with the "n/3" number as the third, hue-free channel.
        private const float StarSizeLit = 34f;
        private const float StarSizeLost = 20f;

        /// <summary>Seconds the death-pop animation runs on the star that just went dark.</summary>
        private const float StarPopSeconds = 0.45f;

        // Presentation-only snuff state: which tier we last painted, which diamond is popping,
        // and how long it has left. Unscaled, so a hit-stop or a hold cannot freeze the feedback.
        private int _shownStars = -1;
        private int _poppingStar = -1;
        private float _popTimer;

        // =====================================================================
        //  Self-install — one HUD per RaidBase_* scene
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            TryInstall(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                          UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            TryInstall(scene.name);
        }

        private static void TryInstall(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (!sceneName.StartsWith("RaidBase", StringComparison.OrdinalIgnoreCase)) return;
            if (FindAnyObjectByType<RaidHudController>() != null) return;

            var go = new GameObject("RaidHudController");
            go.AddComponent<RaidHudController>();
            FlowTrace.Step("Raid", $"RaidHudController self-installed in raid scene '{sceneName}'.");
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Start()
        {
            BuildHud();
            StartCoroutine(WO1639Probe());
        }

        // =====================================================================
        //  WO-1639 STEP 1 INSTRUMENTATION — PERMANENT (CLAUDE.md sec.12)
        // ---------------------------------------------------------------------
        // Three things this ticket could not settle from source, and the two reads that
        // settle them on the next device run. These calls STAY IN THE CODE once the
        // systems are proven (sec.12: instrumentation is never stripped, only flagged
        // off) - a raid HUD defect must never again cost a frame-by-frame pixel audit.
        //
        // ⚠ EVERY interpolated part is computed into a local FIRST. The compile gate's
        // brace scanner has no interpolated-string model, so a quote inside a `{...}`
        // hole ends the string as far as it is concerned and the file reads unbalanced
        // (CLAUDE.md sec.1). Plain concatenation only, below.
        // =====================================================================

        /// <summary>Screen-height fraction above which a world-space renderer is reported by
        /// <see cref="LogOversizedWorldMarkers"/>. WO-1639 DEFECT D.
        /// ⚠ 0.10, NOT 0.25, AND THE FRAME IS WHY. In the ENTRY frame
        /// (Builds/device-frames/2026-09-10_0608_arena_01_entry.png, opened this session) the
        /// yellow shield-and-chevron measures roughly 240 px of 1200 = 0.20 of screen height -
        /// a 0.25 threshold would have reported NOTHING at the t+1s sample and the whole point
        /// of sampling twice is to catch the object at BOTH distances and prove it grows.
        /// 0.10 keeps the entry sample and still excludes terrain-scale geometry noise.</summary>
        private const float OversizedMarkerScreenFraction = 0.10f;

        private System.Collections.IEnumerator WO1639Probe()
        {
            // One frame so TMP has laid the rows out and the raid scene has finished spawning.
            yield return null;
            LogReadoutFitState();

            // DEFECT D: the shape grows with camera proximity, so sample twice - once at the
            // entry seat and once after the player has closed on the spire.
            yield return new WaitForSecondsRealtime(1f);
            LogOversizedWorldMarkers("t+1s");
            yield return new WaitForSecondsRealtime(4f);
            LogOversizedWorldMarkers("t+5s");
        }

        /// <summary>
        /// WO-1639 DEFECT A/B Step 1: the five readout rows' resolved fit state. Same shape as
        /// WO-1628 sec.4 Step 1 - band px, the autosize window, the line/character counts and
        /// whether TMP truncated. A row whose <c>characterCount</c> is under its
        /// <c>text.Length</c> is ellipsised; a row with <c>lineCount</c> 0 was CULLED because
        /// its band could not seat the floor (the WO-1519 class).
        /// </summary>
        private void LogReadoutFitState()
        {
            LogRowFit("timer", _timerLabel);
            LogRowFit("objective", _objLabel);
            LogRowFit("razed", _destLabel);
            LogRowFit("stars", _starCount);
            LogRowFit("troops", _troopLabel);
        }

        private static void LogRowFit(string rowName, TMPro.TextMeshProUGUI t)
        {
            if (t == null)
            {
                FlowTrace.Warn("Raid", "[wo1639-fit] readout row '" + rowName + "' is NULL.");
                return;
            }
            t.ForceMeshUpdate();
            var rt = t.rectTransform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float bandPx = Mathf.Abs(corners[1].y - corners[0].y);
            float widthPx = Mathf.Abs(corners[2].x - corners[1].x);
            string raw = t.text ?? string.Empty;
            int drawn = t.textInfo != null ? t.textInfo.characterCount : -1;
            int lines = t.textInfo != null ? t.textInfo.lineCount : -1;
            bool truncated = t.isTextTruncated;
            string size = t.fontSize.ToString("F1");
            string sizeMin = t.fontSizeMin.ToString("F1");
            string sizeMax = t.fontSizeMax.ToString("F1");
            string band = bandPx.ToString("F1");
            string wide = widthPx.ToString("F1");
            string overflow = t.overflowMode.ToString();
            string wrap = t.textWrappingMode.ToString();
            string colour = t.color.r.ToString("F3") + "/" + t.color.g.ToString("F3") + "/" +
                            t.color.b.ToString("F3") + "/a" + t.color.a.ToString("F2");
            string msg = "[wo1639-fit] row=" + rowName +
                         " band=" + band + "x" + wide + "px" +
                         " font=" + size + " [" + sizeMin + ".." + sizeMax + "]" +
                         " lines=" + lines +
                         " chars=" + drawn + "/" + raw.Length +
                         " truncated=" + truncated +
                         " overflow=" + overflow + " wrap=" + wrap +
                         " colour=" + colour +
                         // ⚠ GetWorldCorners on a ScreenSpaceOverlay canvas returns DEVICE px, not
                         // the reference px the WO-1639 band arithmetic is written in. Log the
                         // scale factor so the two reconcile without guessing (~1.243 at 2670x1200).
                         " scaleFactor=" + (t.canvas != null ? t.canvas.scaleFactor.ToString("F4") : "unknown");
            if (truncated || lines <= 0 || (drawn >= 0 && drawn < raw.Length))
                FlowTrace.Warn("Raid", msg + " <- ROW DOES NOT SHOW ITS WHOLE STRING.");
            else
                FlowTrace.Step("Raid", msg);
        }

        /// <summary>
        /// WO-1639 DEFECT D Step 1: NAME the oversized yellow shape. The frames proved its KIND
        /// (world-space: the compass strip's ScreenSpaceOverlay plate occludes it, and an
        /// overlay canvas always draws over world geometry) but no source line names it. No
        /// clamp is authored anywhere in this ticket - CLAUDE.md sec.12 forbids editing a
        /// non-trivial defect before captured data names the object. This read produces that
        /// name: every world renderer whose projected screen height exceeds
        /// <see cref="OversizedMarkerScreenFraction"/>, with its hierarchy path so the owning
        /// script is one grep away.
        /// </summary>
        private static void LogOversizedWorldMarkers(string phase)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                FlowTrace.Warn("Raid", "[wo1639-marker] " + phase + ": no Camera.main, cannot project.");
                return;
            }
            float screenH = Mathf.Max(1f, Screen.height);
            float threshold = screenH * OversizedMarkerScreenFraction;
            int reported = 0;
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                float top = float.MinValue, bottom = float.MaxValue;
                bool anyInFront = false;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z);
                    var sp = cam.WorldToScreenPoint(corner);
                    if (sp.z <= 0f) continue;
                    anyInFront = true;
                    if (sp.y > top) top = sp.y;
                    if (sp.y < bottom) bottom = sp.y;
                }
                if (!anyInFront) continue;
                float heightPx = top - bottom;
                if (heightPx < threshold) continue;
                // Cap the burst: at a 0.10 threshold the ground plane, the tower and the
                // boundary ring all qualify, and a firehose evicts the boot window out of the
                // device logcat ring (memory `logcat-ring-buffer-destroys-evidence`). 25 is
                // enough to hold every large object in a raid arena; the TINT field is what
                // makes the yellow one findable in one grep.
                if (reported >= 25) break;
                reported++;

                string path = HierarchyPath(r.transform);
                string mat = "none";
                string tint = "n/a";
                var sm = r.sharedMaterial;
                if (sm != null)
                {
                    mat = sm.name + " (" + (sm.shader != null ? sm.shader.name : "no-shader") + ")";
                    // Read the tint through HasProperty on BOTH names: Material.color silently
                    // logs an error on a shader with no _Color, and an instrument that spams the
                    // log is an instrument that evicts the evidence (memory
                    // `logcat-ring-buffer-destroys-evidence`).
                    bool hasBase = sm.HasProperty("_BaseColor");
                    bool hasLegacy = sm.HasProperty("_Color");
                    if (hasBase || hasLegacy)
                    {
                        Color c0 = hasBase ? sm.GetColor("_BaseColor") : sm.GetColor("_Color");
                        tint = c0.r.ToString("F2") + "/" + c0.g.ToString("F2") + "/" +
                               c0.b.ToString("F2") + "/a" + c0.a.ToString("F2");
                    }
                }
                string hpx = heightPx.ToString("F0");
                string frac = (heightPx / screenH).ToString("F2");
                string dist = Vector3.Distance(cam.transform.position, b.center).ToString("F1");
                string kind = r.GetType().Name;
                FlowTrace.Warn("Raid",
                    "[wo1639-marker] " + phase + " OVERSIZED world renderer: path=" + path +
                    " kind=" + kind + " screenH=" + hpx + "px (" + frac + " of screen)" +
                    " camDist=" + dist + "m mat=" + mat + " tint=" + tint);
            }
            // ⛔ SECOND PASS, AND IT IS NOT OPTIONAL: FindObjectsByType<Renderer> DOES NOT SEE uGUI.
            // A uGUI Image draws through a CanvasRenderer, which is NOT a Renderer, so a WorldSpace
            // or ScreenSpaceCamera Canvas carrying a shield/chevron SPRITE would be invisible to
            // the pass above - and this project already builds exactly that shape of thing
            // (FloatingHealthBar.cs:211-216 stands up a RenderMode.WorldSpace canvas with a gold
            // rim). Such a canvas matches EVERY fact WO-1639 sec.1d established: it is world-space,
            // so the ScreenSpaceOverlay compass strip occludes it; it scales with camera proximity;
            // and a sprite gives the crisp vector silhouette the frames show. Without this pass the
            // sweep would print "no world renderer exceeds..." and the next lane would start from
            // zero - the exact dead end sec.1d is trying to close.
            int uiReported = 0;
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                var cv = canvases[i];
                if (cv == null || !cv.enabled || !cv.gameObject.activeInHierarchy) continue;
                if (cv.renderMode == RenderMode.ScreenSpaceOverlay) continue;   // draws over the world by definition
                if (cv.isRootCanvas == false) continue;                          // report the root, not every nested one

                var crt = cv.transform as RectTransform;
                if (crt == null) continue;
                var wc = new Vector3[4];
                crt.GetWorldCorners(wc);
                float top = float.MinValue, bottom = float.MaxValue;
                bool anyInFront = false;
                for (int c = 0; c < 4; c++)
                {
                    var sp = cam.WorldToScreenPoint(wc[c]);
                    if (sp.z <= 0f) continue;
                    anyInFront = true;
                    if (sp.y > top) top = sp.y;
                    if (sp.y < bottom) bottom = sp.y;
                }
                if (!anyInFront) continue;
                float uiHeightPx = top - bottom;
                if (uiHeightPx < threshold) continue;
                if (uiReported >= 15) break;
                uiReported++;

                // Name the sprite(s) it carries - that is what identifies a shield/chevron.
                string sprites = "none";
                var images = cv.GetComponentsInChildren<Image>(false);
                if (images != null && images.Length > 0)
                {
                    var names = new System.Text.StringBuilder();
                    int listed = 0;
                    for (int k = 0; k < images.Length && listed < 6; k++)
                    {
                        var im = images[k];
                        if (im == null || !im.enabled) continue;
                        if (listed > 0) names.Append(",");
                        names.Append(im.sprite != null ? im.sprite.name : "no-sprite");
                        names.Append("@a");
                        names.Append(im.color.a.ToString("F2"));
                        listed++;
                    }
                    if (listed > 0) sprites = names.ToString();
                }
                string uHpx = uiHeightPx.ToString("F0");
                string uFrac = (uiHeightPx / screenH).ToString("F2");
                FlowTrace.Warn("Raid",
                    "[wo1639-marker] " + phase + " OVERSIZED world-space CANVAS: path=" +
                    HierarchyPath(cv.transform) + " renderMode=" + cv.renderMode.ToString() +
                    " sortingOrder=" + cv.sortingOrder + " screenH=" + uHpx + "px (" + uFrac +
                    " of screen) images=" + sprites);
            }

            if (reported == 0 && uiReported == 0)
                FlowTrace.Step("Raid",
                    "[wo1639-marker] " + phase + ": nothing - neither a world Renderer nor a " +
                    "non-overlay Canvas - exceeds " +
                    OversizedMarkerScreenFraction.ToString("F2") + " of screen height. If the " +
                    "yellow shape was on screen in this frame it is NEITHER, and the next place " +
                    "to look is a camera-stacked overlay or a projector/decal.");
        }

        private static string HierarchyPath(Transform t)
        {
            if (t == null) return "<null>";
            string path = t.name;
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 12)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui);
        }

        private void Update()
        {
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshInterval;
            Refresh();
        }

        // =====================================================================
        //  HUD construction (code-built uGUI, reserved right column — WO-1464)
        // =====================================================================

        // ── WO-1464: WHERE THE READOUT SITS, AND WHY IT IS NOT A TOP STRIP ANY MORE ──────
        //
        // ⛔ THE DEFECT, MEASURED ON THE OWNER'S DEVICE (build 358872, 2670x1200, mid-raid at
        // 1:13 - Logs/device/screens/owner-screen-20260907-004502.png): this panel was authored
        // x 0.020-0.980, y 0.860-0.990, straight across the town HUD's whole top row. In that one
        // frame "1:13" is painted over the hero nameplate ("Th... Lv 7" plus its health/XP bars),
        // and "1/3" and "Troops 10/10" are painted over the compass's NE / E ticks and the bar
        // beneath them. The comment at the head of this file claimed the two raid surfaces "sit
        // on complementary edges and never overlap" - true of each other, and never checked
        // against the HUD underneath, which is the WO-1219 / WO-1436 failure for the third time.
        //
        // ⛔ THE SEAT IS SHARED DATA: HudLayoutBands.RaidReadoutBand (DeNelle.Core.UI). That file
        // carries the whole argument for why a full-width top strip CANNOT exist on this HUD and
        // why the right-hand column is free for a raid's entire duration. Do NOT re-introduce a
        // literal rect here - a Village-local literal is precisely what could not see the
        // nameplate it was landing on.

        /// <summary>The readout's screen band. Exposed so the oracle asserts the exclusion from
        /// the AUTHORED seat this method consumes, never from a figure copied into a test.</summary>
        public static Rect ReadoutBand
        {
            get { return HudLayoutBands.RaidReadoutBand; }
        }

        private void BuildHud()
        {
            if (_ui != null) Destroy(_ui);

            // Below the deploy HUD's 30000 so the deploy tray/buttons stay tappable on top.
            _ui = ElarionUiKit.BuildModalCanvas("RaidHud", 29000);

            // A framed dark-glass COLUMN on the right (the deploy tray owns the bottom, and the
            // town HUD owns the whole top row - see the WO-1464 block above).
            var band = ReadoutBand;
            var bar = ElarionUiKit.Panel(_ui.transform,
                new Vector2(band.xMin, band.yMin), new Vector2(band.xMax, band.yMax), deep: false);
            FlowTrace.Step("Raid",
                "raid readout seated in the reserved right column: x " +
                band.xMin.ToString("F3") + ".." + band.xMax.ToString("F3") + ", y " +
                band.yMin.ToString("F3") + ".." + band.yMax.ToString("F3") +
                " (clear of the hero nameplate and the compass - WO-1464).");
            // Passive HUD: the strip must never intercept a deploy/rally tap.
            var barImg = bar.GetComponent<Image>();
            if (barImg != null)
            {
                barImg.raycastTarget = false;
                // ⛔ WO-1639 DEFECT A — "QUIET GLASS" WAS MEASURED AND IT IS NOT LEGIBLE.
                // This line shipped as new Color(0.04f, 0.035f, 0.03f, 0.42f) with the rationale
                // "quiet glass, not a gilt slab ... only the chrome gets out of the way". The
                // owner's device frames (build 363529, 2670x1200, Builds/device-frames/
                // 2026-09-10_0608_arena_01_entry.png) were sampled and the plate composited to
                // RGB ~(136,145,154) over the bright daylit arena. Against that, NOTHING on this
                // panel reached the 3:1 large-text floor: timer 2.67:1, SPIRE 1.98:1, Razed
                // 1.72:1, unlit stars 1.55:1, Troops 1.12:1. At 42% alpha the boundary-ring
                // pillars are visible THROUGH the plate behind the last two rows, so those rows
                // do not even sit on a constant background - their contrast varies with the world.
                //
                // The fix is the kit's OWN canon, not a hand-picked alpha: ElarionUiKit.
                // ObsidianFill (ElarionUiKit.cs:189, 0.02/0.02/0.025 at alpha 0.98) is the panel
                // fill every other Obsidian surface in the game uses - including the raid's own
                // end panel, which reads well in the same frames and is the counter-example that
                // proves 0.42 was the outlier, not the house style. Taking the constant rather
                // than a local literal also means this plate can never drift from the kit again
                // (the duplicated-state failure CLAUDE.md documents in sec.2, sec.5 and sec.16).
                //
                // PREDICTED ratios at ObsidianFill (WCAG relative luminance, plate composited
                // over the same sampled arena background, computed 2026-09-10 - see the WO-1639
                // RESULT for the arithmetic): Parchment 16.5:1, Gilt 12.2:1, ParchmentDim 10.6:1.
                // Every text row clears 4.5:1 on the plate change ALONE, which is why the two
                // ParchmentDim rows (Razed, Troops) are deliberately NOT promoted here - the
                // defect was the plate, and ParchmentDim is the kit's secondary-text role that
                // reads on a 0.98 plate everywhere else in the game. Only StarDim moves, and
                // only because an unlit diamond is a non-text component with a 3:1 floor.
                barImg.color = ElarionUiKit.ObsidianFill;
            }
            var barT = bar.transform;

            // ── THE COLUMN, top to bottom: TIMER / SPIRE / RAZED / STARS / TROOPS ────────
            // Fractions below are OF THE PANEL, so the whole stack follows the band above with
            // no second set of screen literals. Rows are ordered by how often the player looks:
            // the clock is the one thing checked constantly, so it takes the top of the column.
            const float PadX0 = 0.05f, PadX1 = 0.95f;

            // ── TIMER (big number + shrinking bar under it) ──────────────────────
            _timerLabel = MakeLabel(barT, "3:00", new Vector2(PadX0, 0.795f), new Vector2(PadX1, 0.985f),
                ElarionUi.Parchment, ElarionUi.FontBody, TMPro.TextAlignmentOptions.Right, bold: true);

            var timerTrack = ElarionUiKit.AddImage(barT, "TimerTrack",
                new Vector2(PadX0, 0.735f), new Vector2(PadX1, 0.780f), EmptyTrackFill, rounded: false);
            timerTrack.GetComponent<Image>().raycastTarget = false;
            var timerFillGo = ElarionUiKit.AddImage(timerTrack.transform, "TimerFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUi.Gilt, rounded: false);
            timerFillGo.GetComponent<Image>().raycastTarget = false;
            _timerFill = (RectTransform)timerFillGo.transform;

            // ── THE OBJECTIVE (spire HP) + its draining bar ──────────────────────
            // The old right column said "Razed N%" and was fed a pure corpse count, so it
            // read 100% with every structure untouched. The headline is the WIN CONDITION -
            // the spire - and the blended destruction sits under it as the secondary
            // (scoring) number.
            _objLabel = MakeLabel(barT, "SPIRE 100%", new Vector2(PadX0, 0.575f), new Vector2(PadX1, 0.705f),
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right, bold: true);

            var objTrack = ElarionUiKit.AddImage(barT, "ObjectiveTrack",
                new Vector2(PadX0, 0.515f), new Vector2(PadX1, 0.560f), EmptyTrackFill, rounded: false);
            objTrack.GetComponent<Image>().raycastTarget = false;
            var objFillGo = ElarionUiKit.AddImage(objTrack.transform, "ObjectiveFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUi.Gilt, rounded: false);
            objFillGo.GetComponent<Image>().raycastTarget = false;
            _objFill = (RectTransform)objFillGo.transform;

            // ── RAZED % + its growing bar ────────────────────────────────────────
            _destLabel = MakeLabel(barT, "Razed 0%", new Vector2(PadX0, 0.365f), new Vector2(PadX1, 0.485f),
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right);

            var destTrack = ElarionUiKit.AddImage(barT, "DestTrack",
                new Vector2(PadX0, 0.310f), new Vector2(PadX1, 0.350f), EmptyTrackFill, rounded: false);
            destTrack.GetComponent<Image>().raycastTarget = false;
            var destFillGo = ElarionUiKit.AddImage(destTrack.transform, "DestFill",
                new Vector2(0f, 0f), new Vector2(0f, 1f), ElarionUi.Affordable, rounded: false);
            destFillGo.GetComponent<Image>().raycastTarget = false;
            _destFill = (RectTransform)destFillGo.transform;

            // ── STAR PROGRESS (3 diamonds + n/3) ─────────────────────────────────
            // Diamonds are FIXED-SIZE reference px hung on a point anchor, so they keep their
            // silhouette at any band aspect; the "n/3" beside them is the number that carries
            // the state without hue (repo colourblind law).
            for (int i = 0; i < 3; i++)
            {
                float cx = 0.16f + i * 0.16f;
                var d = ElarionUiKit.AddImage(barT, "Star" + i,
                    new Vector2(cx, 0.220f), new Vector2(cx, 0.220f), StarDim, rounded: false);
                var img = d.GetComponent<Image>();
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.sizeDelta = new Vector2(StarSizeLit, StarSizeLit);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f);   // diamond
                _starDiamonds[i] = img;
            }
            _starCount = MakeLabel(barT, "0/3", new Vector2(0.58f, 0.155f), new Vector2(PadX1, 0.285f),
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right, bold: true);

            // ── Troops alive/deployed (a plain number at the foot of the column) ──
            // ⚠ 0.130 of the panel, not 0.110. The band resolves to 347.5 reference px at the
            // owner's 2670x1200 (0.360 of a 965.4-unit canvas), so a 0.110 row is 38.2 ref px -
            // UNDER the 38.6 that seats the 30 px FontFloor, and TMP Ellipsis culls a line it
            // cannot seat, rendering it BLANK. That is the WO-1519 [seat] finding applied here
            // before it could ship, not after.
            _troopLabel = MakeLabel(barT, "Troops 0/0", new Vector2(PadX0, 0.010f), new Vector2(PadX1, 0.140f),
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right);

            Refresh();
        }

        private static TMPro.TextMeshProUGUI MakeLabel(Transform parent, string text,
            Vector2 anchorMin, Vector2 anchorMax, Color color, int fontSize,
            TMPro.TextAlignmentOptions align, bool bold = false)
        {
            var go = new GameObject("Label", typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            t.text = text;
            t.color = color;
            t.fontSize = fontSize;
            t.fontStyle = bold ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
            t.alignment = align;
            t.raycastTarget = false;
            // WO-1464: the readout column is narrower than the old full-width strip, so every
            // row is overflow-protected by the kit rather than by hoping the string is short.
            ElarionUiKit.FitSingleLine(t);
            return t;
        }

        // =====================================================================
        //  Refresh — pull the live scorer numbers into the widgets (passive)
        // =====================================================================

        private void Refresh()
        {
            var s = RaidScoring.Instance;
            if (s == null) return;

            // Timer: number + shrinking bar. Pulse (motion, not hue) under 30s so a
            // colourblind player still reads "running out".
            float remaining = s.RemainingSeconds;
            if (_timerLabel != null) _timerLabel.text = FormatTime(remaining);
            if (_timerFill != null)
            {
                float frac = s.ClockSeconds > 0f ? Mathf.Clamp01(remaining / s.ClockSeconds) : 0f;
                _timerFill.anchorMax = new Vector2(frac, 1f);
                float pulse = remaining <= 30f && remaining > 0f
                    ? 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
                if (_timerLabel != null) _timerLabel.transform.localScale = Vector3.one * pulse;
            }

            // ── WO-1594 HONOR STARS ──────────────────────────────────────────────────────
            // Bind PresentationStars, NOT ProjectedStars. ProjectedStars is the settle preview
            // and it EARNS UP from 0, so the bar sat at 0/3 through the whole fight and jumped
            // at the end - which narrates nothing and is the felt defect this ticket names. The
            // honor read starts at 3/3 the instant the raid engages and goes DARK as milestones
            // pass, so the pressure is legible without arithmetic.
            int stars = s.PresentationStars;
            if (_shownStars >= 0 && stars < _shownStars)
            {
                // A star just died: pop the highest one that is now dark. Motion, not hue.
                _poppingStar = Mathf.Clamp(stars, 0, _starDiamonds.Length - 1);
                _popTimer = StarPopSeconds;
            }
            _shownStars = stars;

            if (_popTimer > 0f) _popTimer = Mathf.Max(0f, _popTimer - RefreshInterval);
            if (_popTimer <= 0f) _poppingStar = -1;

            for (int i = 0; i < _starDiamonds.Length; i++)
            {
                var d = _starDiamonds[i];
                if (d == null) continue;
                bool lit = i < stars;
                d.color = lit ? StarLit : StarDim;

                // SHAPE is the primary channel (colourblind law): a lost star is visibly smaller.
                float size = lit ? StarSizeLit : StarSizeLost;
                if (i == _poppingStar && _popTimer > 0f)
                {
                    // One outward flare that settles into the smaller silhouette, so the moment
                    // of loss is READ rather than noticed later.
                    float t = Mathf.Clamp01(_popTimer / StarPopSeconds);
                    size = Mathf.Lerp(StarSizeLost, StarSizeLit * 1.35f, t);
                }
                d.rectTransform.sizeDelta = new Vector2(size, size);
            }
            if (_starCount != null) _starCount.text = stars + "/3";

            // THE OBJECTIVE: spire HP remaining. Colourblind-safe - a NUMBER plus a bar
            // that DRAINS (motion), never hue alone. "SPIRE DOWN" is the win read.
            if (_objLabel != null || _objFill != null)
            {
                if (!s.HasObjective)
                {
                    // Legacy raid base with no spire - say so rather than showing a fake bar.
                    if (_objLabel != null) _objLabel.text = "CLEAR THE BASE";
                    if (_objFill != null) _objFill.anchorMax = new Vector2(1f, 1f);
                }
                else if (s.ObjectiveComplete)
                {
                    if (_objLabel != null) _objLabel.text = "SPIRE DOWN";
                    if (_objFill != null) _objFill.anchorMax = new Vector2(0f, 1f);
                }
                else
                {
                    float frac = Mathf.Clamp01(s.ObjectiveHpFraction);
                    if (_objLabel != null) _objLabel.text = "SPIRE " + Mathf.CeilToInt(frac * 100f) + "%";
                    if (_objFill != null) _objFill.anchorMax = new Vector2(frac, 1f);
                }
            }

            // Secondary (scoring) readout: how much of the BASE has been razed - the
            // objective-weighted blend of spire damage + garrison cleared.
            int pct = Mathf.Clamp(Mathf.RoundToInt(s.DestructionPct * 100f), 0, 100);
            if (_destLabel != null) _destLabel.text = "Razed " + pct + "%";
            if (_destFill != null) _destFill.anchorMax = new Vector2(pct / 100f, 1f);

            // Troops alive / deployed (plain number).
            if (_troopLabel != null) _troopLabel.text = "Troops " + s.TroopsAlive + "/" + s.TroopsDeployed;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
